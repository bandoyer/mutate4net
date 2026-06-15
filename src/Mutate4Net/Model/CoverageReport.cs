namespace Mutate4Net.Model;

public sealed class CoverageReport
{
    private readonly IReadOnlySet<CoverageSite> _coveredLines;
    private readonly bool _treatAllAsCovered;

    public CoverageReport(IReadOnlySet<CoverageSite> coveredLines)
        : this(coveredLines, treatAllAsCovered: false)
    {
    }

    private CoverageReport(IReadOnlySet<CoverageSite> coveredLines, bool treatAllAsCovered)
    {
        _coveredLines = coveredLines;
        _treatAllAsCovered = treatAllAsCovered;
    }

    public static CoverageReport AllCovered() => new(new HashSet<CoverageSite>(), treatAllAsCovered: true);

    public static CoverageReport Combine(IEnumerable<CoverageReport> reports)
    {
        var coveredLines = new HashSet<CoverageSite>();
        foreach (CoverageReport report in reports)
        {
            if (report._treatAllAsCovered)
            {
                return AllCovered();
            }

            coveredLines.UnionWith(report._coveredLines);
        }

        return new CoverageReport(coveredLines);
    }

    public bool Covers(string sourcePath, int lineNumber)
    {
        if (_treatAllAsCovered)
        {
            return true;
        }

        string normalized = NormalizePath(sourcePath);
        return _coveredLines.Any(site =>
            site.LineNumber == lineNumber &&
            (string.Equals(site.SourcePath, normalized, StringComparison.OrdinalIgnoreCase) ||
             site.SourcePath.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase) ||
             normalized.EndsWith("/" + site.SourcePath, StringComparison.OrdinalIgnoreCase)));
    }

    public static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
