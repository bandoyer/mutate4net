using Mutate4Net.Analysis;
using Mutate4Net.Cli;
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
    private readonly ReportFormatter _reportFormatter;
    private readonly DifferentialSelector _selector;
    private readonly LineFilter _lineFilter;
    private readonly ExecutionMessages _messages;

    public MutationRunService()
        : this(
            new MutationCatalog(),
            new ManifestSupport(),
            new ProcessCommandExecutor(),
            new TestCommandFactory(),
            new ReportFormatter(),
            null,
            new LineFilter(),
            new ExecutionMessages())
    {
    }

    public MutationRunService(
        MutationCatalog catalog,
        ManifestSupport manifestSupport,
        ICommandExecutor executor,
        TestCommandFactory testCommandFactory,
        ReportFormatter reportFormatter,
        DifferentialSelector? selector = null,
        LineFilter? lineFilter = null,
        ExecutionMessages? messages = null)
    {
        _catalog = catalog;
        _manifestSupport = manifestSupport;
        _executor = executor;
        _testCommandFactory = testCommandFactory;
        _reportFormatter = reportFormatter;
        _selector = selector ?? new DifferentialSelector(manifestSupport);
        _lineFilter = lineFilter ?? new LineFilter();
        _messages = messages ?? new ExecutionMessages();
    }

    public async Task<MutationRunOutcome> RunAsync(CliArguments arguments)
    {
        SourceAnalysis analysis = await _catalog.AnalyzeAsync(arguments.TargetFile);
        DifferentialSelection differentialSelection = await _selector.SelectAsync(arguments.TargetFile, arguments, analysis);
        IReadOnlyList<MutationSite> selectedSites = _lineFilter.Filter(differentialSelection.Selected, arguments.Lines);
        TestCommand command = _testCommandFactory.Create(arguments.TargetFile, arguments.TestCommand);
        CommandResult baselineResult = await _executor.RunAsync(command.Command, command.WorkingDirectory, 0);
        var baseline = new TestRun(
            baselineResult.ExitCode,
            baselineResult.Output,
            baselineResult.DurationMillis,
            baselineResult.TimedOut);

        if (!baseline.Passed)
        {
            string error = (baseline.TimedOut ? "Baseline tests timed out.\n" : string.Empty)
                + "Baseline tests failed.\n"
                + baseline.Output;
            return new MutationRunOutcome(2, string.Empty, error);
        }

        IReadOnlyList<MutationResult> results = await RunMutantsAsync(
            arguments.TargetFile,
            analysis.Source,
            selectedSites,
            command,
            TimeoutMillis(baseline.DurationMillis, arguments.TimeoutFactor));

        bool survived = results.Any(result => !result.Killed);
        if (!survived)
        {
            await _manifestSupport.WriteAsync(
                arguments.TargetFile,
                analysis.Source,
                _manifestSupport.CreateManifest(analysis));
        }

        string report = _reportFormatter.Format(
            command.WorkingDirectory,
            baseline,
            _messages.ExtraText(arguments, differentialSelection, selectedSites.Count),
            [],
            results);
        return new MutationRunOutcome(survived ? 3 : 0, report, string.Empty);
    }

    private async Task<IReadOnlyList<MutationResult>> RunMutantsAsync(
        string sourceFile,
        string originalSource,
        IReadOnlyList<MutationSite> sites,
        TestCommand command,
        long timeoutMillis)
    {
        var results = new List<MutationResult>();
        string originalFileContents = await File.ReadAllTextAsync(sourceFile);

        try
        {
            for (int i = 0; i < sites.Count; i++)
            {
                MutationSite site = sites[i];
                string mutated = originalSource[..site.Start] + site.Replacement + originalSource[site.End..];
                await File.WriteAllTextAsync(sourceFile, mutated);
                CommandResult result = await _executor.RunAsync(command.Command, command.WorkingDirectory, timeoutMillis);
                results.Add(new MutationResult(
                    site,
                    result.ExitCode != 0 || result.TimedOut,
                    result.DurationMillis,
                    result.TimedOut,
                    i + 1,
                    sites.Count));
                await File.WriteAllTextAsync(sourceFile, originalFileContents);
            }
        }
        finally
        {
            await File.WriteAllTextAsync(sourceFile, originalFileContents);
        }

        return results;
    }

    private static long TimeoutMillis(long baselineDurationMillis, int timeoutFactor) =>
        Math.Max(1_000, baselineDurationMillis * timeoutFactor);
}
