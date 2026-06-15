using Mutate4Net.Cli;

namespace Mutate4Net.Tests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_Version_PrintsVersionWithoutTarget()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await new CliApplication().RunAsync(["--version"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("mutate4net ", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

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

    [Fact]
    public async Task RunAsync_Mutate_ReturnsCliErrorForTestProjectTarget()
    {
        using var workspace = CliWorkspace.Create();
        workspace.Write("Sample.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
              </ItemGroup>
            </Project>
            """);
        string source = workspace.Write("CalculatorTests.cs", """
            class CalculatorTests
            {
                bool Flag() => true;
            }
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await new CliApplication().RunAsync([source, "--test-command", "fake"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Failed running mutations for", error.ToString());
        Assert.Contains("test projects are not supported", error.ToString());
    }

    [Fact]
    public async Task RunAsync_Mutate_ReturnsCliErrorForAmbiguousProjectOwnership()
    {
        using var workspace = CliWorkspace.Create();
        workspace.Write("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        workspace.Write("AlsoApp.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        string source = workspace.Write("Calculator.cs", """
            class Calculator
            {
                bool Flag() => true;
            }
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await new CliApplication().RunAsync([source, "--test-command", "fake"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Multiple projects include", error.ToString());
    }

    private sealed class CliWorkspace : IDisposable
    {
        private readonly string _root;

        private CliWorkspace(string root)
        {
            _root = root;
        }

        public static CliWorkspace Create()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "mutate4net-cli-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new CliWorkspace(root);
        }

        public string Write(string relativePath, string contents)
        {
            string path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
