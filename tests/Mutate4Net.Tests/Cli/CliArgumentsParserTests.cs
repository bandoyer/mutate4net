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
}

