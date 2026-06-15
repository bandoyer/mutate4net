using Mutate4Net.Cli;

namespace Mutate4Net.Tests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_UpdateManifest_WritesFooter()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await new CliApplication().RunAsync([sample.Path, "--update-manifest"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Updated manifest for", output.ToString());
        Assert.Contains("mutate4net-manifest", await File.ReadAllTextAsync(sample.Path));
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunAsync_Scan_MarksChangedScopesWhenManifestExists()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var app = new CliApplication();
        await app.RunAsync([sample.Path, "--update-manifest"], TextWriter.Null, TextWriter.Null);
        string edited = (await File.ReadAllTextAsync(sample.Path)).Replace("true", "false", StringComparison.Ordinal);
        await File.WriteAllTextAsync(sample.Path, edited);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await app.RunAsync([sample.Path, "--scan"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("* ", output.ToString());
        Assert.Contains("* indicates a scope", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }
}

