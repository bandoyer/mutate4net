using Mutate4Net.Analysis;
using Mutate4Net.Manifest;

namespace Mutate4Net.Tests.Manifest;

public sealed class ManifestSupportTests
{
    [Fact]
    public async Task WriteAsync_AppendsManifestThatCanBeRead()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var catalog = new MutationCatalog();
        var support = new ManifestSupport();
        var analysis = await catalog.AnalyzeAsync(sample.Path);

        await support.WriteAsync(sample.Path, analysis.Source, support.CreateManifest(analysis));
        var manifest = await support.ReadAsync(sample.Path);

        Assert.NotNull(manifest);
        Assert.Equal(1, manifest.Version);
        Assert.Equal(analysis.ModuleHash, manifest.ModuleHash);
        Assert.Equal(analysis.Scopes.Count, manifest.Scopes.Count);
        Assert.Contains("mutate4net-manifest", await File.ReadAllTextAsync(sample.Path));
    }

    [Fact]
    public async Task FindChangedScopesAsync_ReportsChangedRegisteredScope()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var catalog = new MutationCatalog();
        var support = new ManifestSupport();
        var analysis = await catalog.AnalyzeAsync(sample.Path);
        await support.WriteAsync(sample.Path, analysis.Source, support.CreateManifest(analysis));

        string edited = (await File.ReadAllTextAsync(sample.Path)).Replace("true", "false", StringComparison.Ordinal);
        await File.WriteAllTextAsync(sample.Path, edited);
        var changedAnalysis = await catalog.AnalyzeAsync(sample.Path);

        var changedScopes = await support.FindChangedScopesAsync(sample.Path, changedAnalysis);

        Assert.True(changedScopes.ManifestPresent);
        Assert.True(changedScopes.ModuleHashChanged);
        Assert.NotEmpty(changedScopes.ManifestViolationScopeIds);
    }
}

