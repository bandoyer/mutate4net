using System.Diagnostics;
using Mutate4Net.Cli;

namespace Mutate4Net.Tests.Integration;

public sealed class EndToEndMutationTests
{
    [Fact]
    public async Task RunAsync_MutatesRealSolutionAndWritesManifest()
    {
        using var workspace = IntegrationWorkspace.Create();
        string sourceFile = workspace.Write("src/SampleLib/Calculator.cs", """
            namespace SampleLib;

            public static class Calculator
            {
                public static bool IsOne(int value) => value == 1;
            }
            """);
        string libraryProject = workspace.Write("src/SampleLib/SampleLib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        string testProject = workspace.Write("tests/SampleLib.Tests/SampleLib.Tests.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\..\src\SampleLib\SampleLib.csproj" />
                <PackageReference Include="coverlet.msbuild" Version="6.0.0" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
                <PackageReference Include="xunit" Version="2.5.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
              </ItemGroup>
            </Project>
            """);
        workspace.Write("tests/SampleLib.Tests/CalculatorTests.cs", """
            using SampleLib;
            using Xunit;

            namespace SampleLib.Tests;

            public sealed class CalculatorTests
            {
                [Fact]
                public void OneIsTrue() => Assert.True(Calculator.IsOne(1));

                [Fact]
                public void ZeroIsFalse() => Assert.False(Calculator.IsOne(0));
            }
            """);

        await workspace.DotnetAsync("new", "sln", "-n", "Sample");
        await workspace.DotnetAsync("sln", workspace.SolutionFileName("Sample"), "add", libraryProject, testProject);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await new CliApplication().RunAsync([sourceFile, "--max-workers", "2"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("KILLED", output.ToString());
        Assert.Contains("Coverage report available: true", output.ToString());
        Assert.Contains("Summary: 4 killed, 0 survived, 4 total.", output.ToString());
        Assert.Contains("mutate4net-manifest", await File.ReadAllTextAsync(sourceFile));
    }

    [Fact]
    public async Task RunAsync_ReturnsSurvivedForWeakRealTests()
    {
        using var workspace = IntegrationWorkspace.Create();
        string sourceFile = workspace.Write("src/SampleLib/Threshold.cs", """
            namespace SampleLib;

            public static class Threshold
            {
                public static bool IsNonNegative(int value) => value >= 0;
            }
            """);
        string libraryProject = workspace.Write("src/SampleLib/SampleLib.csproj", LibraryProjectXml());
        string testProject = workspace.Write("tests/SampleLib.Tests/SampleLib.Tests.csproj", TestProjectXml());
        workspace.Write("tests/SampleLib.Tests/ThresholdTests.cs", """
            using SampleLib;
            using Xunit;

            namespace SampleLib.Tests;

            public sealed class ThresholdTests
            {
                [Fact]
                public void PositiveValueIsNonNegative() => Assert.True(Threshold.IsNonNegative(1));
            }
            """);

        await workspace.DotnetAsync("new", "sln", "-n", "Sample");
        await workspace.DotnetAsync("sln", workspace.SolutionFileName("Sample"), "add", libraryProject, testProject);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await new CliApplication().RunAsync([sourceFile, "--lines", "5", "--max-workers", "1"], output, error);

        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("SURVIVED", output.ToString());
        Assert.DoesNotContain("mutate4net-manifest", await File.ReadAllTextAsync(sourceFile));
    }

    [Fact]
    public async Task RunAsync_ReportsUncoveredSitesInRealSolution()
    {
        using var workspace = IntegrationWorkspace.Create();
        string sourceFile = workspace.Write("src/SampleLib/Calculator.cs", """
            namespace SampleLib;

            public static class Calculator
            {
                public static bool Covered(int value) => value == 1;
                public static bool Uncovered(int value) => value == 0;
            }
            """);
        string libraryProject = workspace.Write("src/SampleLib/SampleLib.csproj", LibraryProjectXml());
        string testProject = workspace.Write("tests/SampleLib.Tests/SampleLib.Tests.csproj", TestProjectXml());
        workspace.Write("tests/SampleLib.Tests/CalculatorTests.cs", """
            using SampleLib;
            using Xunit;

            namespace SampleLib.Tests;

            public sealed class CalculatorTests
            {
                [Fact]
                public void CoveredMethodIsTested() => Assert.True(Calculator.Covered(1));
            }
            """);

        await workspace.DotnetAsync("new", "sln", "-n", "Sample");
        await workspace.DotnetAsync("sln", workspace.SolutionFileName("Sample"), "add", libraryProject, testProject);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await new CliApplication().RunAsync([sourceFile, "--lines", "6"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("UNCOVERED", output.ToString());
        Assert.Contains("Summary: 0 killed, 0 survived, 0 total.", output.ToString());
    }

    private static string LibraryProjectXml() =>
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private static string TestProjectXml() =>
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\..\src\SampleLib\SampleLib.csproj" />
            <PackageReference Include="coverlet.msbuild" Version="6.0.0" />
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
            <PackageReference Include="xunit" Version="2.5.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
          </ItemGroup>
        </Project>
        """;

    private sealed class IntegrationWorkspace : IDisposable
    {
        private readonly string _root;

        private IntegrationWorkspace(string root)
        {
            _root = root;
        }

        public static IntegrationWorkspace Create()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "mutate4net-integration-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new IntegrationWorkspace(root);
        }

        public string Write(string relativePath, string contents)
        {
            string path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }

        public async Task DotnetAsync(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = _root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"dotnet {string.Join(' ', arguments)} timed out.\n{output}\n{error}");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"dotnet {string.Join(' ', arguments)} failed.\n{output}\n{error}");
            }
        }

        public string SolutionFileName(string name)
        {
            if (File.Exists(Path.Combine(_root, name + ".sln")))
            {
                return name + ".sln";
            }

            if (File.Exists(Path.Combine(_root, name + ".slnx")))
            {
                return name + ".slnx";
            }

            throw new FileNotFoundException($"Could not find solution {name}.sln or {name}.slnx in {_root}.");
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
