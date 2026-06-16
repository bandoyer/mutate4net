using System.Text;
using Mutate4Net.Model;

namespace Mutate4Net.Reporting;

public sealed class ReportFormatter
{
    public string Format(
        string projectRoot,
        TestRun baseline,
        string extra,
        IReadOnlyList<MutationSite> uncovered,
        IReadOnlyList<MutationResult> results)
    {
        var builder = new StringBuilder();
        builder.Append("Baseline tests passed in ").Append(baseline.DurationMillis).Append(" ms.\n");
        if (!string.IsNullOrWhiteSpace(extra))
        {
            builder.Append(extra);
        }

        AppendUncovered(projectRoot, uncovered, builder);
        AppendResults(projectRoot, results, builder);
        AppendSummary(uncovered, results, builder);
        AppendDuration(baseline, results, builder);
        return builder.ToString();
    }

    private static void AppendUncovered(string projectRoot, IReadOnlyList<MutationSite> uncovered, StringBuilder builder)
    {
        foreach (MutationSite site in uncovered)
        {
            builder.Append("UNCOVERED ")
                .Append(RelativePath(projectRoot, site.File))
                .Append(':')
                .Append(site.Line)
                .Append(' ')
                .Append('[')
                .Append(site.Category)
                .Append(':')
                .Append(site.MutatorId)
                .Append("] ")
                .Append(site.Description)
                .Append('\n');
        }
    }

    private static void AppendResults(string projectRoot, IReadOnlyList<MutationResult> results, StringBuilder builder)
    {
        foreach (MutationResult result in results)
        {
            builder.Append(StatusLabel(result.Status))
                .Append(' ')
                .Append(RelativePath(projectRoot, result.Site.File))
                .Append(':')
                .Append(result.Site.Line)
                .Append(' ')
                .Append('[')
                .Append(result.Site.Category)
                .Append(':')
                .Append(result.Site.MutatorId)
                .Append("] ")
                .Append(result.Site.Description)
                .Append(" (")
                .Append(result.DurationMillis)
                .Append(" ms)\n");
            if (result.TimedOut)
            {
                builder.Append("  timed out\n");
            }

            AppendFailureOutput(result.FailureOutput, builder);
        }
    }

    private static string StatusLabel(MutationStatus status) =>
        status switch
        {
            MutationStatus.Killed => "KILLED",
            MutationStatus.Survived => "SURVIVED",
            MutationStatus.Error => "ERROR",
            _ => status.ToString().ToUpperInvariant()
        };

    private static void AppendFailureOutput(string? failureOutput, StringBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(failureOutput))
        {
            return;
        }

        builder.Append("  output:\n");
        foreach (string line in Truncate(failureOutput.Trim(), 4_000)
                     .Split(Environment.NewLine, StringSplitOptions.None))
        {
            builder.Append("    ").Append(line).Append('\n');
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "\n    ... output truncated ...";
    }

    private static void AppendSummary(
        IReadOnlyList<MutationSite> uncovered,
        IReadOnlyList<MutationResult> results,
        StringBuilder builder)
    {
        int killed = results.Count(result => result.Killed);
        int survived = results.Count(result => result.Survived);
        int errors = results.Count(result => result.Error);
        builder.Append("Coverage: ").Append(uncovered.Count).Append(" uncovered sites skipped.\n");
        builder.Append("Summary: ")
            .Append(killed)
            .Append(" killed, ")
            .Append(survived)
            .Append(" survived, ");
        if (errors > 0)
        {
            builder.Append(errors).Append(" errors, ");
        }

        builder
            .Append(results.Count)
            .Append(" total.\n");
    }

    private static void AppendDuration(TestRun baseline, IReadOnlyList<MutationResult> results, StringBuilder builder)
    {
        long mutantMillis = results.Sum(result => result.DurationMillis);
        builder.Append("Duration: ")
            .Append(baseline.DurationMillis + mutantMillis)
            .Append(" ms total (baseline ")
            .Append(baseline.DurationMillis)
            .Append(" ms, mutants ")
            .Append(mutantMillis)
            .Append(" ms).\n");
    }

    private static string RelativePath(string projectRoot, string file)
    {
        try
        {
            return Path.GetRelativePath(projectRoot, file);
        }
        catch (ArgumentException)
        {
            return file;
        }
    }
}
