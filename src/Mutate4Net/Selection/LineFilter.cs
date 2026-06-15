using Mutate4Net.Model;

namespace Mutate4Net.Selection;

public sealed class LineFilter
{
    public IReadOnlyList<MutationSite> Filter(IReadOnlyList<MutationSite> sites, IReadOnlySet<int> lines)
    {
        if (lines.Count == 0)
        {
            return sites.OrderBy(site => site.Start).ToArray();
        }

        return sites
            .Where(site => lines.Contains(site.Line))
            .OrderBy(site => site.Start)
            .ToArray();
    }
}

