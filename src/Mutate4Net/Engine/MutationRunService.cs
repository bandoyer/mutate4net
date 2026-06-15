using Mutate4Net.Analysis;
using Mutate4Net.Cli;
using Mutate4Net.Execution;
using Mutate4Net.Manifest;
using Mutate4Net.Model;
using Mutate4Net.Reporting;

namespace Mutate4Net.Engine;

public sealed class MutationRunService
{
    private readonly MutationCatalog _catalog;
    private readonly ManifestSupport _manifestSupport;
    private readonly ICommandExecutor _executor;
    private readonly TestCommandFactory _testCommandFactory;
    private readonly ReportFormatter _reportFormatter;

    public MutationRunService()
        : this(
            new MutationCatalog(),
            new ManifestSupport(),
            new ProcessCommandExecutor(),
            new TestCommandFactory(),
            new ReportFormatter())
    {
    }

    public MutationRunService(
        MutationCatalog catalog,
        ManifestSupport manifestSupport,
        ICommandExecutor executor,
        TestCommandFactory testCommandFactory,
        ReportFormatter reportFormatter)
    {
        _catalog = catalog;
        _manifestSupport = manifestSupport;
        _executor = executor;
        _testCommandFactory = testCommandFactory;
        _reportFormatter = reportFormatter;
    }

    public async Task<MutationRunOutcome> RunAsync(CliArguments arguments)
    {
        SourceAnalysis analysis = await _catalog.AnalyzeAsync(arguments.TargetFile);
        IReadOnlyList<MutationSite> selectedSites = SelectSites(analysis.Sites, arguments.Lines);
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
            string.Empty,
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

    private static IReadOnlyList<MutationSite> SelectSites(
        IReadOnlyList<MutationSite> sites,
        IReadOnlySet<int> lines)
    {
        if (lines.Count == 0)
        {
            return sites.OrderBy(site => site.Start).ToArray();
        }

        return sites
            .Where(site => lines.Contains(site.Line))
            .OrderBy(site => site.Start)
            .ToArray();
    }

    private static long TimeoutMillis(long baselineDurationMillis, int timeoutFactor) =>
        Math.Max(1_000, baselineDurationMillis * timeoutFactor);
}

