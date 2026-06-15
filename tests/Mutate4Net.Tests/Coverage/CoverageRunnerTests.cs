using Mutate4Net.Cli;
using Mutate4Net.Coverage;
using Mutate4Net.Execution;
using Mutate4Net.Model;

namespace Mutate4Net.Tests.Coverage;

public sealed class CoverageRunnerTests
{
    [Fact]
    public async Task RunBaselineAsync_GeneratesCoverageWithCoverletCommand()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var factory = new TestCommandFactory();
        TestCommand baseline = factory.Create(sample.Path, customCommand: null);
        var executor = new FakeCommandExecutor((command, workingDirectory) =>
        {
            Assert.Contains("/p:CollectCoverage=true", command);
            Assert.Contains(command, argument => argument.StartsWith("/p:CoverletOutput=", StringComparison.Ordinal));
            string coverageDirectory = CoverageLoader.DefaultCoverageDirectory(workingDirectory);
            Directory.CreateDirectory(coverageDirectory);
            File.WriteAllText(CoverageLoader.DefaultCoveragePath(workingDirectory), """
                <coverage>
                  <packages>
                    <package name="">
                      <classes>
                        <class name="Sample" filename="Sample.cs">
                          <lines>
                            <line number="3" hits="1" />
                          </lines>
                        </class>
                      </classes>
                    </package>
                  </packages>
                </coverage>
                """);
            return new CommandResult(0, "coverage ok", 25, false);
        });
        var runner = new CoverageRunner(executor, factory, new CoverageLoader());

        CoverageRun run = await runner.RunBaselineAsync(Arguments(sample.Path, testCommand: null), baseline);

        Assert.True(run.Baseline.Passed);
        Assert.True(run.ReportAvailable);
        Assert.False(run.ReusedCoverage);
        Assert.True(run.Report.Covers("Sample.cs", 3));
    }

    [Fact]
    public async Task RunBaselineAsync_CustomCommandTreatsAllLinesAsCovered()
    {
        using var sample = SampleFile.Create("class Sample { bool Flag() => true; }");
        var factory = new TestCommandFactory();
        TestCommand baseline = factory.Create(sample.Path, customCommand: "fake");
        var executor = new FakeCommandExecutor((command, workingDirectory) =>
        {
            Assert.Contains("fake", string.Join(" ", command));
            return new CommandResult(0, "custom ok", 10, false);
        });
        var runner = new CoverageRunner(executor, factory, new CoverageLoader());

        CoverageRun run = await runner.RunBaselineAsync(Arguments(sample.Path, testCommand: "fake"), baseline);

        Assert.True(run.Baseline.Passed);
        Assert.False(run.ReportAvailable);
        Assert.True(run.Report.Covers("anything.cs", 99));
    }

    private static CliArguments Arguments(string path, string? testCommand) =>
        new(
            path,
            CliMode.Mutate,
            ReuseCoverage: false,
            Lines: new HashSet<int>(),
            SinceLastRun: false,
            MutateAll: false,
            MutationWarning: 50,
            MaxWorkers: 1,
            TimeoutFactor: 10,
            TestCommand: testCommand,
            Verbose: false);

    private sealed class FakeCommandExecutor : ICommandExecutor
    {
        private readonly Func<IReadOnlyList<string>, string, CommandResult> _handler;

        public FakeCommandExecutor(Func<IReadOnlyList<string>, string, CommandResult> handler)
        {
            _handler = handler;
        }

        public Task<CommandResult> RunAsync(
            IReadOnlyList<string> command,
            string workingDirectory,
            long timeoutMillis,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_handler(command, workingDirectory));
    }
}

