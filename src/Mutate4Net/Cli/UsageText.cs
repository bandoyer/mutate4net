namespace Mutate4Net.Cli;

public static class UsageText
{
    public const string Text = """
        mutate4net - mutation testing for C# source files

        Usage:
          mutate4net <file.cs> [options]
          mutate4net <project.csproj|directory> --all-files [options]
          mutate4net --help

        Options:
          --all-files               Run scan/update/mutation across all production files in a project.
          --scan                    Print mutation sites without running tests.
          --update-manifest         Refresh the embedded mutate4net manifest.
          --reuse-coverage          Reuse existing coverage data.
          --lines 12,18             Restrict mutation to specific source lines.
          --mutator ID              Include mutator category/id; comma-separated or repeat.
          --exclude-mutator ID      Exclude mutator category/id; comma-separated or repeat.
          --since-last-run          Mutate only scopes changed since the manifest.
          --mutate-all              Ignore the manifest and mutate all covered sites.
          --mutation-warning N      Warn when selected mutation count exceeds N.
          --max-workers N           Limit parallel isolated workers.
          --timeout-factor N        Multiply baseline duration for mutant timeout.
          --project PATH            Owning production .csproj when discovery is ambiguous.
          --test-command CMD        Override the default dotnet test command.
          --test-filter EXPR        Pass a VSTest filter to generated dotnet test commands.
          --test-project PATH       Test project to run; repeat for multiple projects.
          --exclude-test-project ID Exclude discovered test project by path or name.
          --verbose                 Print live worker progress.
          --help                    Show this help text.
          --version                 Show the mutate4net version.

        """;
}
