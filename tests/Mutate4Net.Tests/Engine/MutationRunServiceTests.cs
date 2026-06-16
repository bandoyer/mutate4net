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
        Assert.Contains("Test command steps: 1", outcome.Output);
        Assert.Contains("Test command: ", outcome.Output);
        Assert.Contains("fake", outcome.Output);
        Assert.Contains("Coverage report available: false", outcome.Output);
        Assert.Contains("Custom test command supplied; treating all selected mutation sites as covered.", outcome.Output);
        Assert.Contains("Summary: 1 killed, 0 survived, 1 total.", outcome.Output);
        Assert.Contains("Duration: 21 ms total (baseline 10 ms, mutants 11 ms).", outcome.Output);
        string finalSource = await File.ReadAllTextAsync(sample.Path);
        Assert.Contains("bool Flag() => true;", finalSource);
        Assert.Contains("mutate4net-manifest", finalSource);
    }

    [Fact]
    public async Task RunAsync_RunsMutantsInWorkerWorkspaceWithoutMutatingOriginalFile()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var executor = new FakeCommandExecutor(
            (runCount, command, workingDirectory) =>
            {
                if (runCount == 2)
                {
                    Assert.Contains(".mutate4net", workingDirectory);
                    Assert.Contains("bool Flag() => true;", File.ReadAllText(sample.Path));
                }
            },
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "mutant failed", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Contains("bool Flag() => true;", await File.ReadAllTextAsync(sample.Path));
    }

    [Fact]
    public async Task RunAsync_RemapsExplicitProjectIntoWorkerWorkspace()
    {
        using var workspace = TempProjectWorkspace.Create();
        string projectFile = workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        string sourceFile = workspace.Write("src/App/Sample.cs", """
            namespace App;

            public static class Sample
            {
                public static bool Flag() => true;
            }
            """);
        var executor = new FakeCommandExecutor(
            (runCount, _, workingDirectory) =>
            {
                if (runCount == 2)
                {
                    Assert.Contains(".mutate4net", workingDirectory);
                    Assert.True(File.Exists(Path.Combine(workingDirectory, "App.csproj")));
                }
            },
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "mutant failed", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(
            sourceFile,
            projectFile: projectFile));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(2, executor.RunCount);
    }

    [Fact]
    public async Task RunAsync_MaxWorkersRunsAllMutantsWithDeterministicReportOrder()
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
            new CommandResult(1, "first mutant failed", 11, false),
            new CommandResult(1, "second mutant failed", 12, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path, maxWorkers: 2));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(3, executor.RunCount);
        Assert.Contains("Summary: 2 killed, 0 survived, 2 total.", outcome.Output);
        Assert.True(
            outcome.Output.IndexOf("replace true with false", StringComparison.Ordinal) <
            outcome.Output.IndexOf("replace false with true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ReusesWorkerWorkspaceForSequentialMutants()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool First() => true;
                bool Second() => false;
            }
            """);
        var mutantWorkingDirectories = new List<string>();
        var executor = new FakeCommandExecutor(
            (runCount, _, workingDirectory) =>
            {
                if (runCount > 1)
                {
                    mutantWorkingDirectories.Add(workingDirectory);
                }
            },
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "first mutant failed", 11, false),
            new CommandResult(1, "second mutant failed", 12, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path, maxWorkers: 1));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(2, mutantWorkingDirectories.Count);
        Assert.Single(mutantWorkingDirectories.Distinct(StringComparer.OrdinalIgnoreCase));
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
    public async Task RunAsync_LineSelectionDoesNotWriteManifest()
    {
        const string source = """
            class Sample
            {
                bool First() => true;
                bool Second() => false;
            }
            """;
        using var sample = SampleFile.Create(source);
        var executor = new FakeCommandExecutor(
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "mutant failed", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path, lines: new HashSet<int> { 3 }));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Contains("Line-filtered run; manifest not updated.", outcome.Output);
        Assert.DoesNotContain("mutate4net-manifest", await File.ReadAllTextAsync(sample.Path));
    }

    [Fact]
    public async Task RunAsync_MutatorSelectionRunsOnlyIncludedMutatorsAndDoesNotWriteManifest()
    {
        const string source = """
            class Sample
            {
                bool Flag() => true;
                int Count() => 0;
            }
            """;
        using var sample = SampleFile.Create(source);
        var executor = new FakeCommandExecutor(
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "mutant failed", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(
            sample.Path,
            includedMutators: new HashSet<string> { "boolean" }));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(2, executor.RunCount);
        Assert.Contains("Included mutators: boolean", outcome.Output);
        Assert.Contains("Mutator-filtered run; manifest not updated.", outcome.Output);
        Assert.Contains("[boolean:boolean-literal] replace true with false", outcome.Output);
        Assert.DoesNotContain("[literal:numeric-literal]", outcome.Output);
        Assert.Contains("Summary: 1 killed, 0 survived, 1 total.", outcome.Output);
        Assert.Equal(source, await File.ReadAllTextAsync(sample.Path));
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
        Assert.Contains("Coverage reused: true", outcome.Output);
        Assert.Contains("Coverage report available: true", outcome.Output);
        Assert.Contains("Summary: 1 killed, 0 survived, 1 total.", outcome.Output);
    }

    [Fact]
    public async Task RunAsync_ReportsInfrastructureFailureAsError()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var executor = new FakeCommandExecutor(
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "Sample.cs(1,1): error CS0246: missing type", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path));

        Assert.Equal(2, outcome.ExitCode);
        Assert.Contains("ERROR", outcome.Output);
        Assert.Contains("Summary: 0 killed, 0 survived, 1 errors, 1 total.", outcome.Output);
        Assert.DoesNotContain("mutate4net-manifest", await File.ReadAllTextAsync(sample.Path));
    }

    [Fact]
    public async Task RunAsync_ReportsMtpZeroTestPolicyFailureAsError()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var executor = new FakeCommandExecutor(
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(8, "Minimum expected tests policy violation, tests ran 0, minimum expected 1", 11, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(sample.Path));

        Assert.Equal(2, outcome.ExitCode);
        Assert.Contains("ERROR", outcome.Output);
        Assert.Contains("Summary: 0 killed, 0 survived, 1 errors, 1 total.", outcome.Output);
        Assert.DoesNotContain("mutate4net-manifest", await File.ReadAllTextAsync(sample.Path));
    }

    [Fact]
    public async Task RunAsync_AddsNoRestoreAfterWorkerFirstGeneratedTestRun()
    {
        using var workspace = TempProjectWorkspace.Create();
        string projectFile = workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        string sourceFile = workspace.Write("src/App/Sample.cs", """
            namespace App;

            public static class Sample
            {
                public static bool First() => true;
                public static bool Second() => false;
            }
            """);
        var mutantCommands = new List<IReadOnlyList<string>>();
        var executor = new FakeCommandExecutor(
            (runCount, command, _) =>
            {
                if (runCount > 1)
                {
                    mutantCommands.Add(command);
                }
            },
            new CommandResult(0, "baseline ok", 10, false),
            new CommandResult(1, "first mutant failed", 11, false),
            new CommandResult(1, "second mutant failed", 12, false));
        var service = CreateService(executor);

        MutationRunOutcome outcome = await service.RunAsync(Arguments(
            sourceFile,
            projectFile: projectFile,
            reuseCoverage: true,
            testCommand: null,
            maxWorkers: 1));

        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(2, mutantCommands.Count);
        Assert.DoesNotContain("--no-restore", mutantCommands[0]);
        Assert.Contains("--no-restore", mutantCommands[1]);
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
        string? testCommand = "fake",
        string? projectFile = null,
        int maxWorkers = 1,
        IReadOnlySet<string>? includedMutators = null,
        IReadOnlySet<string>? excludedMutators = null) =>
        new(
            path,
            CliMode.Mutate,
            ReuseCoverage: reuseCoverage,
            Lines: lines ?? new HashSet<int>(),
            SinceLastRun: false,
            MutateAll: mutateAll,
            MutationWarning: 50,
            MaxWorkers: maxWorkers,
            TimeoutFactor: 10,
            ProjectFile: projectFile,
            TestCommand: testCommand,
            TestFilter: null,
            Verbose: false,
            TestProjects: [],
            ExcludedTestProjects: [],
            IncludedMutators: includedMutators ?? new HashSet<string>(),
            ExcludedMutators: excludedMutators ?? new HashSet<string>());

    private sealed class FakeCommandExecutor : ICommandExecutor
    {
        private readonly Queue<CommandResult> _results;
        private readonly Action<int, IReadOnlyList<string>, string>? _onRun;
        private readonly object _gate = new();

        public FakeCommandExecutor(params CommandResult[] results)
            : this(null, results)
        {
        }

        public FakeCommandExecutor(
            Action<int, IReadOnlyList<string>, string>? onRun,
            params CommandResult[] results)
        {
            _onRun = onRun;
            _results = new Queue<CommandResult>(results);
        }

        public int RunCount { get; private set; }

        public Task<CommandResult> RunAsync(
            IReadOnlyList<string> command,
            string workingDirectory,
            long timeoutMillis,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_results.Count == 0)
                {
                    throw new InvalidOperationException("No fake command result is queued.");
                }

                RunCount++;
                _onRun?.Invoke(RunCount, command, workingDirectory);
                return Task.FromResult(_results.Dequeue());
            }
        }
    }

    private sealed class TempProjectWorkspace : IDisposable
    {
        private TempProjectWorkspace(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TempProjectWorkspace Create()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "mutate4net-project-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TempProjectWorkspace(root);
        }

        public string Write(string relativePath, string contents)
        {
            string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
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
