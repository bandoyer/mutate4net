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
            CommandResult customResult = await _executor.RunAsync(baselineCommand.Command, baselineCommand.WorkingDirectory, 0);
            return new CoverageRun(ToTestRun(customResult), CoverageReport.AllCovered(), ReusedCoverage: false, ReportAvailable: false);
        }

        if (arguments.ReuseCoverage)
        {
            CommandResult baselineResult = await _executor.RunAsync(baselineCommand.Command, baselineCommand.WorkingDirectory, 0);
            string reusedPath = CoverageLoader.DefaultCoveragePath(baselineCommand.WorkingDirectory);
            bool reportAvailable = File.Exists(reusedPath);
            CoverageReport report = _coverageLoader.LoadPathOrAllCovered(reusedPath);
            return new CoverageRun(ToTestRun(baselineResult), report, ReusedCoverage: true, reportAvailable);
        }

        string coverageDirectory = CoverageLoader.DefaultCoverageDirectory(baselineCommand.WorkingDirectory);
        Directory.CreateDirectory(coverageDirectory);
        string coveragePath = CoverageLoader.DefaultCoveragePath(baselineCommand.WorkingDirectory);
        if (File.Exists(coveragePath))
        {
            File.Delete(coveragePath);
        }

        TestCommand coverageCommand = _testCommandFactory.CreateCoverageCommand(
            arguments.TargetFile,
            CoverageLoader.DefaultCoverageOutputPrefix(baselineCommand.WorkingDirectory));
        CommandResult coverageResult = await _executor.RunAsync(coverageCommand.Command, coverageCommand.WorkingDirectory, 0);
        bool generated = File.Exists(coveragePath);
        CoverageReport generatedReport = _coverageLoader.LoadPathOrAllCovered(coveragePath);
        return new CoverageRun(ToTestRun(coverageResult), generatedReport, ReusedCoverage: false, generated);
    }

    private static TestRun ToTestRun(CommandResult result) =>
        new(result.ExitCode, result.Output, result.DurationMillis, result.TimedOut);
}

