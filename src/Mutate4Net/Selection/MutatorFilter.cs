using Mutate4Net.Model;

namespace Mutate4Net.Selection;

public sealed class MutatorFilter
{
    public IReadOnlyList<MutationSite> Filter(
        IReadOnlyList<MutationSite> sites,
        IReadOnlySet<string> includedMutators,
        IReadOnlySet<string> excludedMutators)
    {
        if (includedMutators.Count == 0 && excludedMutators.Count == 0)
        {
            return sites.OrderBy(site => site.Start).ToArray();
        }

        return sites
            .Where(site => IsIncluded(site, includedMutators) && !IsExcluded(site, excludedMutators))
            .OrderBy(site => site.Start)
            .ToArray();
    }

    private static bool IsIncluded(MutationSite site, IReadOnlySet<string> includedMutators) =>
        includedMutators.Count == 0 || MatchesAny(site, includedMutators);

    private static bool IsExcluded(MutationSite site, IReadOnlySet<string> excludedMutators) =>
        excludedMutators.Count > 0 && MatchesAny(site, excludedMutators);

    private static bool MatchesAny(MutationSite site, IReadOnlySet<string> mutators) =>
        mutators.Any(mutator =>
            string.Equals(mutator, site.MutatorId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mutator, site.Category, StringComparison.OrdinalIgnoreCase));
}
