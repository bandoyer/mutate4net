using Mutate4Net.Analysis;
using Mutate4Net.Selection;

namespace Mutate4Net.Tests.Selection;

public sealed class MutatorFilterTests
{
    [Fact]
    public async Task Filter_IncludesByCategory()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag(int count) => count == 0 && true;
            }
            """);
        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        var filtered = new MutatorFilter().Filter(
            analysis.Sites,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "boolean" },
            new HashSet<string>());

        Assert.NotEmpty(filtered);
        Assert.All(filtered, site => Assert.Equal("boolean", site.Category));
    }

    [Fact]
    public async Task Filter_IncludesByMutatorIdAndExcludesByCategory()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag(int count) => count == 0 && true;
            }
            """);
        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        var filtered = new MutatorFilter().Filter(
            analysis.Sites,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "equality-operator", "numeric-literal" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "literal" });

        Assert.Single(filtered);
        Assert.Equal("equality-operator", filtered[0].MutatorId);
    }
}
