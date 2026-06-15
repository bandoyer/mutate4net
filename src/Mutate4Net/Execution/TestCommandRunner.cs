using System.Text;
using System.Text.RegularExpressions;
using Mutate4Net.Model;

namespace Mutate4Net.Execution;

public static class TestCommandRunner
{
    private static readonly Regex TotalTestsZeroPattern = new(
        @"^\s*Total tests:\s*0\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

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

            if (result.ExitCode == 0 && TestRunReportedZeroTests(result.Output))
            {
                AppendZeroTestsMessage(output);
                return new CommandResult(1, output.ToString(), durationMillis, TimedOut: false);
            }

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

    private static bool TestRunReportedZeroTests(string output) =>
        output.Contains("No test matches the given testcase filter", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("No tests are available", StringComparison.OrdinalIgnoreCase) ||
        TotalTestsZeroPattern.IsMatch(output);

    private static void AppendZeroTestsMessage(StringBuilder output)
    {
        if (output.Length > 0 && output[output.Length - 1] != '\n')
        {
            output.AppendLine();
        }

        output.AppendLine("mutate4net detected that the test command ran zero tests. Check --test-filter, --test-project, and excluded test projects.");
    }
}
