using Mutate4Net.Cli;
using Mutate4Net.Manifest;
using Mutate4Net.Model;

namespace Mutate4Net.Selection;

public sealed class DifferentialSelector
{
    private readonly ManifestSupport _manifestSupport;

    public DifferentialSelector(ManifestSupport manifestSupport)
    {
        _manifestSupport = manifestSupport;
    }

    public async Task<DifferentialSelection> SelectAsync(
        string sourceFile,
        CliArguments arguments,
        SourceAnalysis analysis)
    {
        if (arguments.MutateAll)
        {
            return NotDifferential(analysis);
        }

        if (!arguments.SinceLastRun && arguments.Lines.Count > 0)
        {
            return NotDifferential(analysis);
        }

        ChangedScopes changedScopes = await _manifestSupport.FindChangedScopesAsync(sourceFile, analysis);
        IReadOnlySet<string> changedScopeIds = changedScopes.AllScopeIds();
        if (changedScopeIds.Count == 0 && !changedScopes.ManifestPresent)
        {
            return NotDifferential(analysis);
        }

        int changedMutationSites = MutationCount(analysis, changedScopeIds);
        int differentialSurfaceArea = MutationCount(analysis, changedScopes.UnregisteredScopeIds);
        int manifestViolatingSurfaceArea = MutationCount(analysis, changedScopes.ManifestViolationScopeIds);

        if (changedScopeIds.Count == 0)
        {
            return new DifferentialSelection(
                [],
                UnchangedModule: true,
                ManifestExists: changedScopes.ManifestPresent,
                ModuleHashChanged: changedScopes.ModuleHashChanged,
                TotalMutationSites: analysis.Sites.Count,
                ChangedMutationSites: changedMutationSites,
                DifferentialSurfaceArea: differentialSurfaceArea,
                ManifestViolatingSurfaceArea: manifestViolatingSurfaceArea);
        }

        IReadOnlyList<MutationSite> selected = analysis.Sites
            .Where(site => changedScopeIds.Contains(site.ScopeId))
            .OrderBy(site => site.Start)
            .ToArray();

        return new DifferentialSelection(
            selected,
            UnchangedModule: false,
            ManifestExists: changedScopes.ManifestPresent,
            ModuleHashChanged: changedScopes.ModuleHashChanged,
            TotalMutationSites: analysis.Sites.Count,
            ChangedMutationSites: changedMutationSites,
            DifferentialSurfaceArea: differentialSurfaceArea,
            ManifestViolatingSurfaceArea: manifestViolatingSurfaceArea);
    }

    private static DifferentialSelection NotDifferential(SourceAnalysis analysis) =>
        new(
            analysis.Sites.OrderBy(site => site.Start).ToArray(),
            UnchangedModule: false,
            ManifestExists: false,
            ModuleHashChanged: false,
            TotalMutationSites: analysis.Sites.Count,
            ChangedMutationSites: 0,
            DifferentialSurfaceArea: 0,
            ManifestViolatingSurfaceArea: 0);

    private static int MutationCount(SourceAnalysis analysis, IReadOnlySet<string> scopeIds) =>
        analysis.Sites.Count(site => scopeIds.Contains(site.ScopeId));
}

