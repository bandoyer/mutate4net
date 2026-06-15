using Mutate4Net.Execution;
using Mutate4Net.Model;

namespace Mutate4Net.Tests.Execution;

public sealed class TestCommandRunnerTests
{
    [Fact]
    public async Task RunAsync_FailsWhenOutputReportsNoMatchingTests()
    {
        var executor = new FakeCommandExecutor(new CommandResult(
            0,
            "No test matches the given testcase filter `FullyQualifiedName~Missing` in C:\\App.Tests.dll",
            15,
            TimedOut: false));
        var command = new TestCommand(["dotnet", "test", "--filter", "FullyQualifiedName~Missing"], Directory.GetCurrentDirectory());

        CommandResult result = await TestCommandRunner.RunAsync(executor, command, timeoutMillis: 0);

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("zero tests", result.Output);
        Assert.Equal(15, result.DurationMillis);
    }

    [Fact]
    public async Task RunAsync_FailsWhenOutputReportsZeroTotalTests()
    {
        var executor = new FakeCommandExecutor(new CommandResult(
            0,
            """
            Test Run Successful.
            Total tests: 0
            """,
            20,
            TimedOut: false));
        var command = new TestCommand(["dotnet", "test"], Directory.GetCurrentDirectory());

        CommandResult result = await TestCommandRunner.RunAsync(executor, command, timeoutMillis: 0);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("zero tests", result.Output);
    }

    private sealed class FakeCommandExecutor : ICommandExecutor
    {
        private readonly CommandResult _result;

        public FakeCommandExecutor(CommandResult result)
        {
            _result = result;
        }

        public Task<CommandResult> RunAsync(
            IReadOnlyList<string> command,
            string workingDirectory,
            long timeoutMillis,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}
