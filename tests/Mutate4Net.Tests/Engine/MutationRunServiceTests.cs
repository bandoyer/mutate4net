using Mutate4Net.Analysis;
using Mutate4Net.Cli;
using Mutate4Net.Engine;
using Mutate4Net.Execution;
using Mutate4Net.Manifest;
using Mutate4Net.Model;
using Mutate4Net.Reporting;

namespace Mutate4Net.Tests.Engine;

public sealed class MutationRunServiceTests
{
    [Fact]
    public async Task RunAsync_ReturnsSuccessAndWritesManifest_WhenMutantIsKilled()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var executor = new FakeCommandExecutor(
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "mutant failed", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Contains("KILLED", outcome.Output);
        Assert.Contains("Summary: 1 killed, 0 survived, 1 total.", outcome.Output);
        string finalSource = await File.ReadAllTextAsync(sample.Path);
        Assert.Contains("bool Flag() => true;", finalSource);
        Assert.Contains("mutate4net-manifest", finalSource);
    }

    [Fact]
    public async Task RunAsync_ReturnsSurvivedAndRestoresOriginal_WhenMutantSurvives()
    {
        const string source = """
            class Sample
            {
                bool Flag() => true;
            }
            """;
        using var sample = SampleFile.Create(source);
        var executor = new FakeCommandExecutor(
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(0, "mutant ok", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path));

        Assert.Equal(3, outcome.ExitCode);
        Assert.Contains("SURVIVED", outcome.Output);
        Assert.Equal(source, await File.ReadAllTextAsync(sample.Path));
    }

    [Fact]
    public async Task RunAsync_ReturnsBaselineFailureAndDoesNotMutate_WhenBaselineFails()
    {
        const string source = """
            class Sample
            {
                bool Flag() => true;
            }
            """;
        using var sample = SampleFile.Create(source);
        var executor = new FakeCommandExecutor(new CommandResult(1, "baseline failed", 10, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path));

        Assert.Equal(2, outcome.ExitCode);
        Assert.Contains("Baseline tests failed.", outcome.Error);
        Assert.Equal(source, await File.ReadAllTextAsync(sample.Path));
    }

    private static MutationRunService CreateService(ICommandExecutor executor) =>
        new(
            new MutationCatalog(),
            new ManifestSupport(),
            executor,
            new TestCommandFactory(),
            new ReportFormatter());

    private static CliArguments Arguments(string path) =>
        new(
            path,
            CliMode.Mutate,
            ReuseCoverage: false,
            Lines: new HashSet<int>(),
            SinceLastRun: false,
            MutateAll: false,
            MutationWarning: 50,
            MaxWorkers: 1,
            TimeoutFactor: 10,
            TestCommand: "fake",
            Verbose: false);

    private sealed class FakeCommandExecutor : ICommandExecutor
    {
        private readonly Queue<CommandResult> _results;

        public FakeCommandExecutor(params CommandResult[] results)
        {
            _results = new Queue<CommandResult>(results);
        }

        public Task<CommandResult> RunAsync(
            IReadOnlyList<string> command,
            string workingDirectory,
            long timeoutMillis,
            CancellationToken cancellationToken = default)
        {
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No fake command result is queued.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }
}

