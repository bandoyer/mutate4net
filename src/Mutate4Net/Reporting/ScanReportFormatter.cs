using System.Text;
using Mutate4Net.Model;

namespace Mutate4Net.Reporting;

public sealed class ScanReportFormatter
{
    public string Format(SourceAnalysis analysis, IReadOnlySet<string>? changedScopes = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Scan: {analysis.Sites.Count} mutation sites in {analysis.FilePath}");

        foreach (MutationSite site in analysis.Sites.OrderBy(site => site.Start))
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

