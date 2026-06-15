using Mutate4Net.Cli;
using Mutate4Net.Execution;
using Mutate4Net.Model;

namespace Mutate4Net.Coverage;

public sealed class CoverageRunner
{
    private readonly ICommandExecutor _executor;
    private readonly TestCommandFactory _testCommandFactory;
    private readonly CoverageLoader _coverageLoader;

    public CoverageRunner(ICommandExecutor executor, TestCommandFactory testCommandFactory, CoverageLoader coverageLoader)
    {
        _executor = executor;
        _testCommandFactory = testCommandFactory;
        _coverageLoader = coverageLoader;
    }

    public async Task<CoverageRun> RunBaselineAsync(CliArguments arguments, TestCommand baselineCommand)
    {
        if (!string.IsNullOrWhiteSpace(arguments.TestCommand))
        {
            CommandResult customResult = await TestCommandRunner.RunAsync(_executor, baselineCommand, 0);
            return new CoverageRun(ToTestRun(customResult), CoverageReport.AllCovered(), ReusedCoverage: false, ReportAvailable: false);
        }

        if (arguments.ReuseCoverage)
        {
            CommandResult baselineResult = await TestCommandRunner.RunAsync(_executor, baselineCommand, 0);
            string reusedPath = CoverageLoader.DefaultCoveragePath(baselineCommand.WorkingDirectory);
            bool reportAvailable = File.Exists(reusedPath);
            CoverageReport report = _coverageLoader.LoadPathOrAllCovered(reusedPath);
            return new CoverageRun(ToTestRun(baselineResult), report, ReusedCoverage: true, reportAvailable);
        }

        string coverageDirectory = CoverageLoader.DefaultCoverageDirectory(baselineCommand.WorkingDirectory);
        Directory.CreateDirectory(coverageDirectory);
        ClearCoverageDirectory(coverageDirectory);

        string coverageOutputPrefix = CoverageLoader.DefaultCoverageOutputPrefix(baselineCommand.WorkingDirectory);
        TestCommand coverageCommand = _testCommandFactory.CreateCoverageCommand(arguments, coverageOutputPrefix);
        CommandResult coverageResult = await TestCommandRunner.RunAsync(_executor, coverageCommand, 0);
        string[] coveragePaths = CoveragePaths(coverageOutputPrefix, coverageCommand.Commands.Count);
        bool generated = coveragePaths.Any(File.Exists);
        if (generated || coverageResult.ExitCode != 0 || coverageResult.TimedOut)
        {
            CoverageReport generatedReport = _coverageLoader.LoadPathsOrAllCovered(coveragePaths);
            return new CoverageRun(ToTestRun(coverageResult), generatedReport, ReusedCoverage: false, generated);
        }

        TestCommand collectorCoverageCommand = _testCommandFactory.CreateCollectorCoverageCommand(arguments, coverageDirectory);
        CommandResult collectorResult = await TestCommandRunner.RunAsync(_executor, collectorCoverageCommand, 0);
        string[] collectorCoveragePaths = CollectorCoveragePaths(coverageDirectory);
        bool collectorGenerated = collectorCoveragePaths.Length > 0;
        if (collectorResult.ExitCode == 0 && !collectorResult.TimedOut && collectorGenerated)
        {
            CoverageReport collectorReport = _coverageLoader.LoadPathsOrAllCovered(collectorCoveragePaths);
            return new CoverageRun(ToTestRun(collectorResult), collectorReport, ReusedCoverage: false, ReportAvailable: true);
        }

        return new CoverageRun(ToTestRun(coverageResult), CoverageReport.AllCovered(), ReusedCoverage: false, ReportAvailable: false);
    }

    private static string[] CoveragePaths(string coverageOutputPrefix, int commandCount) =>
        Enumerable.Range(0, commandCount)
            .Select(index => TestCommandFactory.CoverageOutputPrefix(coverageOutputPrefix, index, commandCount) + ".cobertura.xml")
            .ToArray();

    private static string[] CollectorCoveragePaths(string coverageDirectory) =>
        Directory.Exists(coverageDirectory)
            ? Directory.EnumerateFiles(coverageDirectory, "coverage.cobertura.xml", SearchOption.AllDirectories).ToArray()
            : [];

    private static void ClearCoverageDirectory(string coverageDirectory)
    {
        foreach (string file in Directory.EnumerateFiles(coverageDirectory))
        {
            File.Delete(file);
        }

        foreach (string directory in Directory.EnumerateDirectories(coverageDirectory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TestRun ToTestRun(CommandResult result) =>
        new(result.ExitCode, result.Output, result.DurationMillis, result.TimedOut);
}
