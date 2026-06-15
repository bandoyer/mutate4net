using Mutate4Net.Cli;

namespace Mutate4Net.Tests.Cli;

public sealed class CliArgumentsParserTests
{
    [Fact]
    public void Parse_ReturnsHelp_ForHelpFlag()
    {
        ParseOutcome outcome = new CliArgumentsParser().Parse(["--help"]);

        Assert.True(outcome.IsHelp);
    }

    [Fact]
    public void Parse_ReturnsVersion_ForVersionFlag()
    {
        ParseOutcome outcome = new CliArgumentsParser().Parse(["--version"]);

        Assert.True(outcome.IsVersion);
    }

    [Fact]
    public void Parse_RejectsMissingTarget()
    {
        ParseOutcome outcome = new CliArgumentsParser().Parse([]);

        Assert.False(outcome.IsSuccess);
        Assert.Contains("Expected exactly one", outcome.ErrorMessage);
    }

    [Fact]
    public void Parse_RejectsScanWithReuseCoverage()
    {
        using var sample = SampleFile.Create("class Sample { }");

        ParseOutcome outcome = new CliArgumentsParser().Parse([sample.Path, "--scan", "--reuse-coverage"]);

        Assert.False(outcome.IsSuccess);
        Assert.Contains("--scan may not be combined with --reuse-coverage", outcome.ErrorMessage);
    }

    [Fact]
    public void Parse_ReadsScanArguments()
    {
        using var sample = SampleFile.Create("class Sample { }");

        ParseOutcome outcome = new CliArgumentsParser().Parse([sample.Path, "--scan", "--max-workers", "3"]);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.Arguments);
        Assert.Equal(CliMode.Scan, outcome.Arguments.Mode);
        Assert.Equal(3, outcome.Arguments.MaxWorkers);
        Assert.Equal(System.IO.Path.GetFullPath(sample.Path), outcome.Arguments.TargetFile);
    }

    [Fact]
    public void Parse_ReadsTestProjectSelection()
    {
        using var sample = SampleFile.Create("class Sample { }");

        ParseOutcome outcome = new CliArgumentsParser().Parse([
            sample.Path,
            "--test-project",
            "tests/App.Unit/App.Unit.csproj",
            "--test-project",
            "tests/App.Functional/App.Functional.csproj",
            "--exclude-test-project",
            "App.Browser"
        ]);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.Arguments);
        Assert.Equal(
            ["tests/App.Unit/App.Unit.csproj", "tests/App.Functional/App.Functional.csproj"],
            outcome.Arguments.TestProjects);
        Assert.Equal(["App.Browser"], outcome.Arguments.ExcludedTestProjects);
    }

    [Fact]
    public void Parse_ReadsExplicitProject()
    {
        using var sample = SampleFile.Create("class Sample { }");
        string project = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sample.Path)!, "Sample.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        ParseOutcome outcome = new CliArgumentsParser().Parse([sample.Path, "--project", project]);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.Arguments);
        Assert.Equal(System.IO.Path.GetFullPath(project), outcome.Arguments.ProjectFile);
    }

    [Fact]
    public void Parse_RejectsMissingExplicitProject()
    {
        using var sample = SampleFile.Create("class Sample { }");

        ParseOutcome outcome = new CliArgumentsParser().Parse([sample.Path, "--project", "missing.csproj"]);

        Assert.False(outcome.IsSuccess);
        Assert.Contains("Project file does not exist", outcome.ErrorMessage);
    }

    [Fact]
    public void Parse_RejectsTestCommandWithTestProjectSelection()
    {
        using var sample = SampleFile.Create("class Sample { }");

        ParseOutcome outcome = new CliArgumentsParser().Parse([
            sample.Path,
            "--test-command",
            "dotnet test",
            "--test-project",
            "tests/App.Unit/App.Unit.csproj"
        ]);

        Assert.False(outcome.IsSuccess);
        Assert.Contains("--test-command may not be combined", outcome.ErrorMessage);
    }
}
