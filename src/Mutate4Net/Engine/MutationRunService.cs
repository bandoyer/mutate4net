using System.Collections.Concurrent;
using Mutate4Net.Analysis;
using Mutate4Net.Cli;
using Mutate4Net.Coverage;
using Mutate4Net.Execution;
using Mutate4Net.Manifest;
using Mutate4Net.Model;
using Mutate4Net.Reporting;
using Mutate4Net.Selection;

namespace Mutate4Net.Engine;

public sealed class MutationRunService
{
    private readonly MutationCatalog _catalog;
    private readonly ManifestSupport _manifestSupport;
    private readonly ICommandExecutor _executor;
    private readonly TestCommandFactory _testCommandFactory;
    private readonly WorkerWorkspaceManager _workspaceManager;
    private readonly ReportFormatter _reportFormatter;
    private readonly DifferentialSelector _selector;
    private readonly MutatorFilter _mutatorFilter;
    private readonly LineFilter _lineFilter;
    private readonly CoverageRunner _coverageRunner;
    private readonly MutationCoverageFilter _coverageFilter;
    private readonly ExecutionMessages _messages;

    public MutationRunService()
        : this(
            new MutationCatalog(),
            new ManifestSupport(),
            new ProcessCommandExecutor(),
            new TestCommandFactory(),
            new ReportFormatter(),
            new WorkerWorkspaceManager(),
            null,
            null,
            new LineFilter(),
            null,
            new MutationCoverageFilter(),
            new ExecutionMessages())
    {
    }

    public MutationRunService(
        MutationCatalog catalog,
        ManifestSupport manifestSupport,
        ICommandExecutor executor,
        TestCommandFactory testCommandFactory,
        ReportFormatter reportFormatter,
        WorkerWorkspaceManager? workspaceManager = null,
        DifferentialSelector? selector = null,
        MutatorFilter? mutatorFilter = null,
        LineFilter? lineFilter = null,
        CoverageRunner? coverageRunner = null,
        MutationCoverageFilter? coverageFilter = null,
        ExecutionMessages? messages = null)
    {
        _catalog = catalog;
        _manifestSupport = manifestSupport;
        _executor = executor;
        _testCommandFactory = testCommandFactory;
        _workspaceManager = workspaceManager ?? new WorkerWorkspaceManager();
        _reportFormatter = reportFormatter;
        _selector = selector ?? new DifferentialSelector(manifestSupport);
        _mutatorFilter = mutatorFilter ?? new MutatorFilter();
        _lineFilter = lineFilter ?? new LineFilter();
        _coverageRunner = coverageRunner ?? new CoverageRunner(executor, testCommandFactory, new CoverageLoader());
        _coverageFilter = coverageFilter ?? new MutationCoverageFilter();
        _messages = messages ?? new ExecutionMessages();
    }

    public async Task<MutationRunOutcome> RunAsync(CliArguments arguments)
    {
        SourceAnalysis analysis = await _catalog.AnalyzeAsync(arguments.TargetFile, arguments.ProjectFile);
        DifferentialSelection differentialSelection = await _selector.SelectAsync(arguments.TargetFile, arguments, analysis);
        IReadOnlyList<MutationSite> mutatorSelectedSites = _mutatorFilter.Filter(
            differentialSelection.Selected,
            arguments.IncludedMutators,
            arguments.ExcludedMutators);
        IReadOnlyList<MutationSite> selectedSites = _lineFilter.Filter(mutatorSelectedSites, arguments.Lines);
        TestCommand command = _testCommandFactory.Create(arguments);
        CliArguments runArguments = NormalizeTestProjectSelectors(arguments, command.WorkingDirectory);
        CoverageRun coverageRun = await _coverageRunner.RunBaselineAsync(runArguments, command);
        TestRun baseline = coverageRun.Baseline;

        if (!baseline.Passed)
        {
            string error = (baseline.TimedOut ? "Baseline tests timed out.\n" : string.Empty)
                + "Baseline tests failed.\n"
                + baseline.Output;
            return new MutationRunOutcome(2, string.Empty, error);
        }

        CoverageSelection coverageSelection = _coverageFilter.Filter(command.WorkingDirectory, selectedSites, coverageRun.Report);
        IReadOnlyList<MutationResult> results = await RunMutantsAsync(
            arguments.TargetFile,
            command.WorkingDirectory,
            analysis.Source,
            coverageSelection.Covered,
            runArguments,
            TimeoutMillis(baseline.DurationMillis, arguments.TimeoutFactor),
            arguments.MaxWorkers);

        bool survived = results.Any(result => !result.Killed);
        if (!survived && ShouldWriteManifest(arguments))
        {
            await _manifestSupport.WriteAsync(
                arguments.TargetFile,
                analysis.Source,
                _manifestSupport.CreateManifest(analysis));
        }

        string report = _reportFormatter.Format(
            command.WorkingDirectory,
            baseline,
            _messages.ExtraText(
                arguments,
                command,
                differentialSelection,
                coverageRun,
                coverageSelection.Covered.Count,
                coverageSelection.Uncovered.Count),
            coverageSelection.Uncovered,
            results);
        return new MutationRunOutcome(survived ? 3 : 0, report, string.Empty);
    }

    private async Task<IReadOnlyList<MutationResult>> RunMutantsAsync(
        string sourceFile,
        string moduleRoot,
        string originalSource,
        IReadOnlyList<MutationSite> sites,
        CliArguments arguments,
        long timeoutMillis,
        int maxWorkers)
    {
        if (sites.Count == 0)
        {
            return [];
        }

        int workerCount = Math.Max(1, Math.Min(maxWorkers, sites.Count));
        IReadOnlyList<WorkerWorkspace> workerPool = _workspaceManager.CreatePool(moduleRoot, sourceFile, workerCount);
        var availableWorkers = new ConcurrentQueue<WorkerWorkspace>(workerPool);
        using var throttle = new SemaphoreSlim(workerCount, workerCount);
        try
        {
            Task<MutationResult>[] tasks = sites
                .Select((site, index) => RunMutantAsync(
                    moduleRoot,
                    originalSource,
                    site,
                    arguments,
                    timeoutMillis,
                    index + 1,
                    sites.Count,
                    throttle,
                    availableWorkers))
                .ToArray();

            MutationResult[] results = await Task.WhenAll(tasks);
            return results.OrderBy(result => result.Order).ToArray();
        }
        finally
        {
            _workspaceManager.Delete(workerPool[0]);
        }
    }

    private async Task<MutationResult> RunMutantAsync(
        string moduleRoot,
        string originalSource,
        MutationSite site,
        CliArguments arguments,
        long timeoutMillis,
        int order,
        int totalJobs,
        SemaphoreSlim throttle,
        ConcurrentQueue<WorkerWorkspace> availableWorkers)
    {
        await throttle.WaitAsync();
        if (!availableWorkers.TryDequeue(out WorkerWorkspace? workspace))
        {
            throttle.Release();
            throw new InvalidOperationException("No worker workspace was available.");
        }

        try
        {
            CliArguments workerArguments = arguments with
            {
                TargetFile = workspace.SourceFile,
                ProjectFile = RemapPathToWorker(arguments.ProjectFile, moduleRoot, workspace.ModuleRoot)
            };
            TestCommand workerCommand = _testCommandFactory.Create(workerArguments);
            string mutated = originalSource[..site.Start] + site.Replacement + originalSource[site.End..];
            await File.WriteAllTextAsync(workspace.SourceFile, mutated);
            CommandResult result = await TestCommandRunner.RunAsync(_executor, workerCommand, timeoutMillis);
            return new MutationResult(
                site,
                result.ExitCode != 0 || result.TimedOut,
                result.DurationMillis,
                result.TimedOut,
                order,
                totalJobs);
        }
        finally
        {
            try
            {
                await File.WriteAllTextAsync(workspace.SourceFile, originalSource);
                availableWorkers.Enqueue(workspace);
            }
            finally
            {
                throttle.Release();
            }
        }
    }

    private static long TimeoutMillis(long baselineDurationMillis, int timeoutFactor) =>
        Math.Max(1_000, baselineDurationMillis * timeoutFactor);

    private static bool ShouldWriteManifest(CliArguments arguments) =>
        arguments.Lines.Count == 0 &&
        arguments.IncludedMutators.Count == 0 &&
        arguments.ExcludedMutators.Count == 0;

    private static CliArguments NormalizeTestProjectSelectors(CliArguments arguments, string workingDirectory) =>
        arguments with
        {
            TestProjects = NormalizeSelectors(arguments.TestProjects, workingDirectory),
            ExcludedTestProjects = NormalizeSelectors(arguments.ExcludedTestProjects, workingDirectory)
        };

    private static IReadOnlyList<string> NormalizeSelectors(IReadOnlyList<string> selectors, string workingDirectory) =>
        selectors.Select(selector => NormalizeSelector(selector, workingDirectory)).ToArray();

    private static string NormalizeSelector(string selector, string workingDirectory)
    {
        if (!Path.IsPathRooted(selector))
        {
            return selector;
        }

        string fullSelector = Path.GetFullPath(selector);
        string fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        string relative = Path.GetRelativePath(fullWorkingDirectory, fullSelector);
        return IsOutsideDirectory(relative)
            ? selector
            : relative;
    }

    private static string? RemapPathToWorker(string? path, string moduleRoot, string workerRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        string fullPath = Path.GetFullPath(path);
        string fullModuleRoot = Path.GetFullPath(moduleRoot);
        string relative = Path.GetRelativePath(fullModuleRoot, fullPath);
        return IsOutsideDirectory(relative)
            ? path
            : Path.GetFullPath(Path.Combine(workerRoot, relative));
    }

    private static bool IsOutsideDirectory(string relativePath) =>
        relativePath == ".." ||
        relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
        relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
        Path.IsPathRooted(relativePath);
}
