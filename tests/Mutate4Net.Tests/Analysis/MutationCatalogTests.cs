using Mutate4Net.Analysis;

namespace Mutate4Net.Tests.Analysis;

public sealed class MutationCatalogTests
{
    [Fact]
    public async Task AnalyzeAsync_DiscoversInitialMutationSet()
    {
        const string source = """
            class Sample
            {
                bool Check(int count, string name)
                {
                    string label = "x";
                    object value = label;
                    return count == 0 && !string.IsNullOrEmpty(name);
                }

                int Math(int value)
                {
                    return -value + 1;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site.Description == "replace == with !=");
        Assert.Contains(analysis.Sites, site => site.Description == "replace 0 with 1");
        Assert.Contains(analysis.Sites, site => site.Description == "replace && with ||");
        Assert.Contains(analysis.Sites, site => site.Description == "replace ! with removed !");
        Assert.Contains(analysis.Sites, site => site.Description == "replace - with removed -");
        Assert.Contains(analysis.Sites, site => site.Description == "replace + with -");
        Assert.Contains(analysis.Sites, site => site.Description == "replace 1 with 0");
        Assert.Contains(analysis.Sites, site => site.Replacement == "null");
        Assert.NotEmpty(analysis.Scopes);
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotTreatStringConcatenationAsNumericAddition()
    {
        const string source = """
            class Sample
            {
                string Join(string left, string right)
                {
                    return left + right;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.DoesNotContain(analysis.Sites, site => site.Original == "+" && site.Replacement == "-");
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversPatternOperators()
    {
        const string source = """
            class Sample
            {
                bool IsPermanent(int statusCode)
                {
                    return statusCode is >= 500 and < 600;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site.Description == "replace >= with >");
        Assert.Contains(analysis.Sites, site => site.Description == "replace and with or");
        Assert.Contains(analysis.Sites, site => site.Description == "replace < with <=");
    }

    [Fact]
    public async Task AnalyzeAsync_StripsEmbeddedManifestBeforeScanning()
    {
        const string source = """
            class Sample
            {
                bool Flag() => true;
            }

            /* mutate4net-manifest
            version=1
            scope.0.semanticHash=false
            */
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Single(analysis.Sites);
        Assert.Equal("replace true with false", analysis.Sites[0].Description);
    }
}
