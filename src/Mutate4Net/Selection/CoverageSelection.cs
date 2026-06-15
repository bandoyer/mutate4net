using Mutate4Net.Model;

namespace Mutate4Net.Selection;

public sealed record CoverageSelection(
    IReadOnlyList<MutationSite> Covered,
    IReadOnlyList<MutationSite> Uncovered);

