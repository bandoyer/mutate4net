namespace Mutate4Net.Model;

public sealed record DifferentialSelection(
    IReadOnlyList<MutationSite> Selected,
    bool UnchangedModule,
    bool ManifestExists,
    bool ModuleHashChanged,
    int TotalMutationSites,
    int ChangedMutationSites,
    int DifferentialSurfaceArea,
    int ManifestViolatingSurfaceArea);

