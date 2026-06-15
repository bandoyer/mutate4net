using Mutate4Net.Execution;
using Mutate4Net.ProjectSystem;

namespace Mutate4Net.Tests.ProjectSystem;

public sealed class ProjectDiscoveryTests
{
    [Fact]
    public void Discover_FindsSdkProjectOwningSource()
    {
        using var workspace = ProjectWorkspace.Create();
        string project = workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        string source = workspace.Write("src/App/Calculator.cs", "class Calculator { }");

        ProjectInfo? info = new ProjectDiscovery().Discover(source);

        Assert.NotNull(info);
        Assert.Equal(project, info.ProjectFile);
        Assert.False(info.IsTestProject);
    }

    [Fact]
    public void Discover_UsesExplicitCompileIncludeWhenDefaultItemsAreDisabled()
    {
        using var workspace = ProjectWorkspace.Create();
        string project = workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Sources/*.cs" />
              </ItemGroup>
            </Project>
            """);
        string source = workspace.Write("src/App/Sources/Calculator.cs", "class Calculator { }");
        workspace.Write("src/App/Other.cs", "class Other { }");

        ProjectInfo? info = new ProjectDiscovery().Discover(source);

        Assert.NotNull(info);
        Assert.Equal(project, info.ProjectFile);
        Assert.Null(new ProjectDiscovery().Discover(workspace.Path("src/App/Other.cs")));
    }

    [Fact]
    public void Discover_RejectsTestProjectByPackageReference()
    {
        using var workspace = ProjectWorkspace.Create();
        workspace.Write("tests/App.Tests/App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
              </ItemGroup>
            </Project>
            """);
        string source = workspace.Write("tests/App.Tests/CalculatorTests.cs", "class CalculatorTests { }");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new ProjectDiscovery().Discover(source));
        Assert.Contains("test projects are not supported", ex.Message);
    }

    [Fact]
    public void Discover_FailsWhenMultipleProjectsIncludeSource()
    {
        using var workspace = ProjectWorkspace.Create();
        workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        workspace.Write("src/App/AlsoApp.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        string source = workspace.Write("src/App/Calculator.cs", "class Calculator { }");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new ProjectDiscovery().Discover(source));
        Assert.Contains("Multiple projects include", ex.Message);
    }

    [Fact]
    public void TestCommandFactory_UsesDiscoveredProjectPath()
    {
        using var workspace = ProjectWorkspace.Create();
        string project = workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        string source = workspace.Write("src/App/Calculator.cs", "class Calculator { }");

        TestCommand command = new TestCommandFactory().Create(source, customCommand: null);

        Assert.Equal(workspace.Path("src/App"), command.WorkingDirectory);
        Assert.Equal(["dotnet", "test", project, "--no-restore"], command.Command);
    }

    private sealed class ProjectWorkspace : IDisposable
    {
        private readonly string _root;

        private ProjectWorkspace(string root)
        {
            _root = root;
        }

        public static ProjectWorkspace Create()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "mutate4net-project-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new ProjectWorkspace(root);
        }

        public string Path(string relativePath) =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));

        public string Write(string relativePath, string contents)
        {
            string path = Path(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
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

