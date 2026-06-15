using Mutate4Net.Model;

namespace Mutate4Net.Selection;

public sealed class MutationCoverageFilter
{
    public CoverageSelection Filter(string moduleRoot, IReadOnlyList<MutationSite> sites, CoverageReport coverage)
    {
        var covered = new List<MutationSite>();
        var uncovered = new List<MutationSite>();

        foreach (MutationSite site in sites)
        {
            string sourcePath = Path.GetRelativePath(moduleRoot, site.File);
            if (coverage.Covers(sourcePath, site.Line))
            {
                covered.Add(site);
            }
            else
            {
                uncovered.Add(site);
            }
        }

        return new CoverageSelection(covered, uncovered);
    }
}

