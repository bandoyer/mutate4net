namespace Mutate4Net.Cli;

public static class UsageText
{
    public const string Text = """
        mutate4net - mutation testing for one C# source file

        Usage:
          mutate4net <file.cs> [options]
          mutate4net --help

        Options:
          --scan                    Print mutation sites without running tests.
          --update-manifest         Refresh the embedded mutate4net manifest.
          --reuse-coverage          Reuse existing coverage data.
          --lines 12,18             Restrict mutation to specific source lines.
          --since-last-run          Mutate only scopes changed since the manifest.
          --mutate-all              Ignore the manifest and mutate all covered sites.
          --mutation-warning N      Warn when selected mutation count exceeds N.
          --max-workers N           Limit parallel isolated workers.
          --timeout-factor N        Multiply baseline duration for mutant timeout.
          --test-command CMD        Override the default dotnet test command.
          --verbose                 Print live worker progress.
          --help                    Show this help text.

        """;
}

