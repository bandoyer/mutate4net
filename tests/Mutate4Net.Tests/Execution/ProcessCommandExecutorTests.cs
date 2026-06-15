using Mutate4Net.Execution;

namespace Mutate4Net.Tests.Execution;

public sealed class ProcessCommandExecutorTests
{
    [Fact]
    public async Task RunAsync_CapturesOutputAndExitCode()
    {
        var executor = new ProcessCommandExecutor();

        var result = await executor.RunAsync(["dotnet", "--version"], Directory.GetCurrentDirectory(), timeoutMillis: 10_000);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
        Assert.True(result.DurationMillis >= 0);
    }

    [Fact]
    public async Task RunAsync_TimesOutAndKillsProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var executor = new ProcessCommandExecutor();

        var result = await executor.RunAsync(
            ["powershell", "-NoProfile", "-Command", "Start-Sleep -Seconds 5"],
            Directory.GetCurrentDirectory(),
            timeoutMillis: 100);

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
    }
}

