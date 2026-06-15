namespace Mutate4Net.Model;

public sealed record SourceAnalysis(
    string FilePath,
    string Source,
    IReadOnlyList<MutationSite> Sites,
    IReadOnlyList<MutationScope> Scopes,
    string ModuleHash);
