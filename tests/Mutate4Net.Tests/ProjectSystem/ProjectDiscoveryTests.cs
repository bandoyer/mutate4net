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
        Assert.Null(info.SolutionFile);
    }

    [Fact]
    public void Discover_AttachesNearestSolution()
    {
        using var workspace = ProjectWorkspace.Create();
        string solution = workspace.Write("App.sln", string.Empty);
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
        Assert.Equal(solution, info.SolutionFile);
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
    public void Discover_UsesExplicitProjectWhenMultipleProjectsIncludeSource()
    {
        using var workspace = ProjectWorkspace.Create();
        string project = workspace.Write("src/App/App.csproj", """
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

        ProjectInfo? info = new ProjectDiscovery().Discover(source, project);

        Assert.NotNull(info);
        Assert.Equal(project, info.ProjectFile);
    }

    [Fact]
    public void Discover_RejectsExplicitProjectThatDoesNotIncludeSource()
    {
        using var workspace = ProjectWorkspace.Create();
        string project = workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
            </Project>
            """);
        string source = workspace.Write("src/App/Calculator.cs", "class Calculator { }");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new ProjectDiscovery().Discover(source, project));

        Assert.Contains("does not include", ex.Message);
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
        Assert.Equal(["dotnet", "test", project], command.Command);
    }

    [Fact]
    public void TestCommandFactory_PrefersNearestSolution()
    {
        using var workspace = ProjectWorkspace.Create();
        string solution = workspace.Write("App.sln", string.Empty);
        workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        string source = workspace.Write("src/App/Calculator.cs", "class Calculator { }");

        TestCommand command = new TestCommandFactory().Create(source, customCommand: null);

        Assert.Equal(workspace.Path(""), command.WorkingDirectory);
        Assert.Equal(["dotnet", "test", solution], command.Command);
    }

    [Fact]
    public void TestCommandFactory_UsesExplicitProjectForOwnership()
    {
        using var workspace = ProjectWorkspace.Create();
        string project = workspace.Write("src/App/App.csproj", """
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

        TestCommand command = new TestCommandFactory().Create(
            source,
            projectFile: project,
            customCommand: null,
            testProjects: [],
            excludedTestProjects: []);

        Assert.Equal(workspace.Path("src/App"), command.WorkingDirectory);
        Assert.Equal(["dotnet", "test", project], command.Command);
    }

    [Fact]
    public void DiscoverTestProjects_FindsSdkTestProjectsOutsideExcludedDirectories()
    {
        using var workspace = ProjectWorkspace.Create();
        string unit = workspace.Write("tests/App.Unit/App.Unit.csproj", TestProjectXml());
        string functional = workspace.Write("tests/App.Functional/App.Functional.csproj", TestProjectXml());
        workspace.Write("tests/App.Browser/bin/Debug/App.Browser.csproj", TestProjectXml());
        workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        IReadOnlyList<string> projects = new ProjectDiscovery().DiscoverTestProjects(workspace.Path(""));

        Assert.Equal([functional, unit], projects);
    }

    [Fact]
    public void TestCommandFactory_ExcludesDiscoveredTestProjectByName()
    {
        using var workspace = ProjectWorkspace.Create();
        workspace.Write("App.sln", string.Empty);
        workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        string unit = workspace.Write("tests/App.Unit/App.Unit.csproj", TestProjectXml());
        string functional = workspace.Write("tests/App.Functional/App.Functional.csproj", TestProjectXml());
        workspace.Write("tests/App.Browser/App.Browser.csproj", TestProjectXml());
        string source = workspace.Write("src/App/Calculator.cs", "class Calculator { }");

        TestCommand command = new TestCommandFactory().Create(
            source,
            customCommand: null,
            testProjects: [],
            excludedTestProjects: ["App.Browser"]);

        Assert.Equal(workspace.Path(""), command.WorkingDirectory);
        Assert.Equal(2, command.Commands.Count);
        Assert.Contains(command.Commands, candidate => candidate.SequenceEqual(["dotnet", "test", functional]));
        Assert.Contains(command.Commands, candidate => candidate.SequenceEqual(["dotnet", "test", unit]));
    }

    [Fact]
    public void TestCommandFactory_UsesExplicitTestProjects()
    {
        using var workspace = ProjectWorkspace.Create();
        workspace.Write("App.sln", string.Empty);
        workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        string unit = workspace.Write("tests/App.Unit/App.Unit.csproj", TestProjectXml());
        workspace.Write("tests/App.Browser/App.Browser.csproj", TestProjectXml());
        string source = workspace.Write("src/App/Calculator.cs", "class Calculator { }");

        TestCommand command = new TestCommandFactory().Create(
            source,
            customCommand: null,
            testProjects: ["tests/App.Unit/App.Unit.csproj"],
            excludedTestProjects: []);

        Assert.Single(command.Commands);
        Assert.Equal(["dotnet", "test", unit], command.Command);
    }

    [Fact]
    public void TestCommandFactory_AppendsTestFilterToGeneratedCommands()
    {
        using var workspace = ProjectWorkspace.Create();
        workspace.Write("App.sln", string.Empty);
        workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        string unit = workspace.Write("tests/App.Unit/App.Unit.csproj", TestProjectXml());
        string source = workspace.Write("src/App/Calculator.cs", "class Calculator { }");

        TestCommand command = new TestCommandFactory().Create(
            source,
            customCommand: null,
            testProjects: ["tests/App.Unit/App.Unit.csproj"],
            excludedTestProjects: [],
            testFilter: "FullyQualifiedName~CalculatorTests");

        Assert.Single(command.Commands);
        Assert.Equal(["dotnet", "test", unit, "--filter", "FullyQualifiedName~CalculatorTests"], command.Command);
    }

    private static string TestProjectXml() =>
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
          </ItemGroup>
        </Project>
        """;

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
