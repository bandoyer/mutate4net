namespace Mutate4Net.Model;

public sealed record DifferentialManifest(
    int Version,
    string ModuleHash,
    IReadOnlyList<MutationScope> Scopes);

