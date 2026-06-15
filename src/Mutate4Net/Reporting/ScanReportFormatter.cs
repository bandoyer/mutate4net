using System.Text;
using Mutate4Net.Model;

namespace Mutate4Net.Reporting;

public sealed class ScanReportFormatter
{
    public string Format(
        SourceAnalysis analysis,
        IReadOnlySet<string>? changedScopes = null,
        IReadOnlySet<int>? lines = null)
    {
        MutationSite[] sites = analysis.Sites
            .Where(site => lines is null || lines.Count == 0 || lines.Contains(site.Line))
            .OrderBy(site => site.Start)
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"Scan: {sites.Length} mutation sites in {analysis.FilePath}");

        foreach (MutationSite site in sites)
        {
            string marker = changedScopes is not null && changedScopes.Contains(site.ScopeId) ? "*" : " ";
            builder.AppendLine($"{marker} {site.File}:{site.Line} {site.Description}");
        }

        if (changedScopes is not null)
        {
            builder.AppendLine("* indicates a scope that differs from the embedded manifest.");
        }

        return builder.ToString();
    }
}
