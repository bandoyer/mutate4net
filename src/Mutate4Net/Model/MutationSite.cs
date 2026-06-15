namespace Mutate4Net.Model;

public sealed record MutationSite(
    string File,
    int Line,
    int Start,
    int End,
    string Original,
    string Replacement,
    string Description,
    string MutatorId,
    string Category,
    string ScopeId,
    string ScopeKind,
    int ScopeStartLine,
    int ScopeEndLine);
