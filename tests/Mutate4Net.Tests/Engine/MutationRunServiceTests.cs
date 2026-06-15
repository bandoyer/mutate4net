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

    [Fact]
    public async Task RunAsync_LineSelectionRunsOnlyRequestedLine()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool First() => true;
                bool Second() => false;
            }
            """);
        var executor = new FakeCommandExecutor(
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "mutant failed", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path, lines: new HashSet<int> { 3 }));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(2, executor.RunCount);
        Assert.Contains("Summary: 1 killed, 0 survived, 1 total.", outcome.Output);
    }

    [Fact]
    public async Task RunAsync_UnchangedManifestRunsNoMutants()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var catalog = new MutationCatalog();
        var manifestSupport = new ManifestSupport();
        var analysis = await catalog.AnalyzeAsync(sample.Path);
        await manifestSupport.WriteAsync(sample.Path, analysis.Source, manifestSupport.CreateManifest(analysis));
        var executor = new FakeCommandExecutor(new CommandResult(0, "baseline ok", 10, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(1, executor.RunCount);
        Assert.Contains("No mutations need testing.", outcome.Output);
        Assert.Contains("Summary: 0 killed, 0 survived, 0 total.", outcome.Output);
    }

    [Fact]
    public async Task RunAsync_ChangedManifestRunsOnlyChangedScope()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool First() => true;
                bool Second() => false;
            }
            """);
        var catalog = new MutationCatalog();
        var manifestSupport = new ManifestSupport();
        var analysis = await catalog.AnalyzeAsync(sample.Path);
        await manifestSupport.WriteAsync(sample.Path, analysis.Source, manifestSupport.CreateManifest(analysis));
        string edited = (await File.ReadAllTextAsync(sample.Path))
            .Replace("bool First() => true;", "bool First() => false;", StringComparison.Ordinal);
        await File.WriteAllTextAsync(sample.Path, edited);
        var executor = new FakeCommandExecutor(
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "mutant failed", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(2, executor.RunCount);
        Assert.Contains("Changed mutation sites: 1", outcome.Output);
        Assert.Contains("Summary: 1 killed, 0 survived, 1 total.", outcome.Output);
    }

    [Fact]
    public async Task RunAsync_MutateAllIgnoresUnchangedManifest()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var catalog = new MutationCatalog();
        var manifestSupport = new ManifestSupport();
        var analysis = await catalog.AnalyzeAsync(sample.Path);
        await manifestSupport.WriteAsync(sample.Path, analysis.Source, manifestSupport.CreateManifest(analysis));
        var executor = new FakeCommandExecutor(
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "mutant failed", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path, mutateAll: true));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(2, executor.RunCount);
        Assert.Contains("Summary: 1 killed, 0 survived, 1 total.", outcome.Output);
    }

    [Fact]
    public async Task RunAsync_ReuseCoverageSkipsUncoveredMutants()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool First() => true;
                bool Second() => false;
            }
            """);
        string moduleRoot = Path.GetDirectoryName(sample.Path)!;
        string coverageDirectory = Path.Combine(moduleRoot, ".mutate4net", "coverage");
        Directory.CreateDirectory(coverageDirectory);
        await File.WriteAllTextAsync(Path.Combine(coverageDirectory, "coverage.cobertura.xml"), """
            <coverage>
              <packages>
                <package name="">
                  <classes>
                    <class name="Sample" filename="Sample.cs">
                      <lines>
                        <line number="4" hits="1" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """);
        var executor = new FakeCommandExecutor(
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "mutant failed", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(
            sample.Path,
            reuseCoverage: true,
            testCommand: null));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(2, executor.RunCount);
        Assert.Contains("UNCOVERED", outcome.Output);
        Assert.Contains("Uncovered mutation sites: 1", outcome.Output);
        Assert.Contains("Summary: 1 killed, 0 survived, 1 total.", outcome.Output);
    }

    private static MutationRunService CreateService(ICommandExecutor executor) =>
        new(
            new MutationCatalog(),
            new ManifestSupport(),
            executor,
            new TestCommandFactory(),
            new ReportFormatter());

    private static CliArguments Arguments(
        string path,
        IReadOnlySet<int>? lines = null,
        bool mutateAll = false,
        bool reuseCoverage = false,
        string? testCommand = "fake") =>
        new(
            path,
            CliMode.Mutate,
            ReuseCoverage: reuseCoverage,
            Lines: lines ?? new HashSet<int>(),
            SinceLastRun: false,
            MutateAll: mutateAll,
            MutationWarning: 50,
            MaxWorkers: 1,
            TimeoutFactor: 10,
            TestCommand: testCommand,
            Verbose: false);

    private sealed class FakeCommandExecutor : ICommandExecutor
    {
        private readonly Queue<CommandResult> _results;

        public FakeCommandExecutor(params CommandResult[] results)
        {
            _results = new Queue<CommandResult>(results);
        }

        public int RunCount { get; private set; }

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

            RunCount++;
            return Task.FromResult(_results.Dequeue());
        }
    }
}
