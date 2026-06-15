namespace Mutate4Net.Model;

public sealed record MutationScope(
    string Id,
    string Kind,
    int StartLine,
    int EndLine,
    string SemanticHash);

