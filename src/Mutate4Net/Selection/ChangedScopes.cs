namespace Mutate4Net.Selection;

public sealed record ChangedScopes(
    bool ManifestPresent,
    bool ModuleHashChanged,
    IReadOnlySet<string> UnregisteredScopeIds,
    IReadOnlySet<string> ManifestViolationScopeIds)
{
    public IReadOnlySet<string> AllScopeIds()
    {
        var ids = new HashSet<string>(UnregisteredScopeIds, StringComparer.Ordinal);
        ids.UnionWith(ManifestViolationScopeIds);
        return ids;
    }
}

