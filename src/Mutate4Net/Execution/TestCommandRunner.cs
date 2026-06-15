using System.Text;
using Mutate4Net.Model;

namespace Mutate4Net.Execution;

public static class TestCommandRunner
{
    public static async Task<CommandResult> RunAsync(
        ICommandExecutor executor,
        TestCommand command,
        long timeoutMillis,
        CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        long durationMillis = 0;

        foreach (IReadOnlyList<string> step in command.Commands)
        {
            CommandResult result = await executor.RunAsync(
                step,
                command.WorkingDirectory,
                timeoutMillis,
                cancellationToken);
            output.Append(result.Output);
            durationMillis += result.DurationMillis;

            if (result.ExitCode != 0 || result.TimedOut)
            {
                return result with
                {
                    Output = output.ToString(),
                    DurationMillis = durationMillis
                };
            }
        }

        return new CommandResult(0, output.ToString(), durationMillis, TimedOut: false);
    }
}
