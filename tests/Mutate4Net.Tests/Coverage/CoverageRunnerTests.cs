using Mutate4Net.Cli;
using Mutate4Net.Coverage;
using Mutate4Net.Execution;
using Mutate4Net.Model;

namespace Mutate4Net.Tests.Coverage;

public sealed class CoverageRunnerTests
{
    [Fact]
    public async Task RunBaselineAsync_GeneratesCoverageWithCoverletCommand()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var factory = new TestCommandFactory();
        TestCommand baseline = factory.Create(sample.Path, customCommand: null);
        var executor = new FakeCommandExecutor((command, workingDirectory) =>
        {
            Assert.Contains("/p:CollectCoverage=true", command);
            Assert.Contains(command, argument => argument.StartsWith("/p:CoverletOutput=", StringComparison.Ordinal));
            string coverageDirectory = CoverageLoader.DefaultCoverageDirectory(workingDirectory);
            Directory.CreateDirectory(coverageDirectory);
            File.WriteAllText(CoverageLoader.DefaultCoveragePath(workingDirectory), """
                <coverage>
                  <packages>
                    <package name="">
                      <classes>
                        <class name="Sample" filename="Sample.cs">
                          <lines>
                            <line number="3" hits="1" />
                          </lines>
                        </class>
                      </classes>
                    </package>
                  </packages>
                </coverage>
                """);
            return new CommandResult(0, "coverage ok", 25, false);
        });
        var runner = new CoverageRunner(executor, factory, new CoverageLoader());

        CoverageRun run = await runner.RunBaselineAsync(Arguments(sample.Path, testCommand: null), baseline);

        Assert.True(run.Baseline.Passed);
        Assert.True(run.ReportAvailable);
        Assert.False(run.ReusedCoverage);
        Assert.True(run.Report.Covers("Sample.cs", 3));
    }

    [Fact]
    public async Task RunBaselineAsync_CustomCommandTreatsAllLinesAsCovered()
    {
        using var sample = SampleFile.Create("class Sample { bool Flag() => true; }");
        var factory = new TestCommandFactory();
        TestCommand baseline = factory.Create(sample.Path, customCommand: "fake");
        var executor = new FakeCommandExecutor((command, workingDirectory) =>
        {
            Assert.Contains("fake", string.Join(" ", command));
            return new CommandResult(0, "custom ok", 10, false);
        });
        var runner = new CoverageRunner(executor, factory, new CoverageLoader());

        CoverageRun run = await runner.RunBaselineAsync(Arguments(sample.Path, testCommand: "fake"), baseline);

        Assert.True(run.Baseline.Passed);
        Assert.False(run.ReportAvailable);
        Assert.True(run.Report.Covers("anything.cs", 99));
    }

    [Fact]
    public async Task RunBaselineAsync_MergesCoverageFromSelectedTestProjects()
    {
        using var workspace = CoverageWorkspace.Create();
        workspace.Write("App.sln", string.Empty);
        string source = workspace.Write("src/App/Sample.cs", "class Sample { bool Flag() => true; }");
        workspace.Write("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        workspace.Write("tests/App.Unit/App.Unit.csproj", TestProjectXml());
        workspace.Write("tests/App.Functional/App.Functional.csproj", TestProjectXml());
        var factory = new TestCommandFactory();
        CliArguments arguments = Arguments(
            source,
            testCommand: null,
            testProjects:
            [
                "tests/App.Unit/App.Unit.csproj",
                "tests/App.Functional/App.Functional.csproj"
            ]);
        TestCommand baseline = factory.Create(arguments);
        int coverageRuns = 0;
        var executor = new FakeCommandExecutor((command, workingDirectory) =>
        {
            coverageRuns++;
            string outputPrefix = command
                .Single(argument => argument.StartsWith("/p:CoverletOutput=", StringComparison.Ordinal))
                .Split('=', 2)[1];
            int coveredLine = coverageRuns == 1 ? 3 : 4;
            Directory.CreateDirectory(Path.GetDirectoryName(outputPrefix)!);
            File.WriteAllText(outputPrefix + ".cobertura.xml", $$"""
                <coverage>
                  <packages>
                    <package name="">
                      <classes>
                        <class name="Sample" filename="src/App/Sample.cs">
                          <lines>
                            <line number="{{coveredLine}}" hits="1" />
                          </lines>
                        </class>
                      </classes>
                    </package>
                  </packages>
                </coverage>
                """);
            return new CommandResult(0, "coverage ok", 25, false);
        });
        var runner = new CoverageRunner(executor, factory, new CoverageLoader());

        CoverageRun run = await runner.RunBaselineAsync(arguments, baseline);

        Assert.True(run.Baseline.Passed);
        Assert.Equal(2, coverageRuns);
        Assert.True(run.ReportAvailable);
        Assert.True(run.Report.Covers("src/App/Sample.cs", 3));
        Assert.True(run.Report.Covers("src/App/Sample.cs", 4));
    }

    [Fact]
    public async Task RunBaselineAsync_FallsBackToCollectorCoverage()
    {
        using var sample = SampleFile.Create("""
            class Sample
            {
                bool Flag() => true;
            }
            """);
        var factory = new TestCommandFactory();
        TestCommand baseline = factory.Create(sample.Path, customCommand: null);
        int runs = 0;
        var executor = new FakeCommandExecutor((command, workingDirectory) =>
        {
            runs++;
            if (command.Contains("--collect"))
            {
                var commandList = command.ToList();
                int resultsIndex = commandList.IndexOf("--results-directory");
                string resultsDirectory = commandList[resultsIndex + 1];
                string reportDirectory = Path.Combine(resultsDirectory, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(reportDirectory);
                File.WriteAllText(Path.Combine(reportDirectory, "coverage.cobertura.xml"), """
                    <coverage>
                      <packages>
                        <package name="">
                          <classes>
                            <class name="Sample" filename="Sample.cs">
                              <lines>
                                <line number="3" hits="1" />
                              </lines>
                            </class>
                          </classes>
                        </package>
                      </packages>
                    </coverage>
                    """);
            }

            return new CommandResult(0, "coverage ok", 25, false);
        });
        var runner = new CoverageRunner(executor, factory, new CoverageLoader());

        CoverageRun run = await runner.RunBaselineAsync(Arguments(sample.Path, testCommand: null), baseline);

        Assert.Equal(2, runs);
        Assert.True(run.ReportAvailable);
        Assert.True(run.Report.Covers("Sample.cs", 3));
    }

    private static CliArguments Arguments(
        string path,
        string? testCommand,
        IReadOnlyList<string>? testProjects = null,
        IReadOnlyList<string>? excludedTestProjects = null) =>
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
            ProjectFile: null,
            TestCommand: testCommand,
            Verbose: false,
            TestProjects: testProjects ?? [],
            ExcludedTestProjects: excludedTestProjects ?? []);

    private static string TestProjectXml() =>
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
          </ItemGroup>
        </Project>
        """;

    private sealed class FakeCommandExecutor : ICommandExecutor
    {
        private readonly Func<IReadOnlyList<string>, string, CommandResult> _handler;

        public FakeCommandExecutor(Func<IReadOnlyList<string>, string, CommandResult> handler)
        {
            _handler = handler;
        }

        public Task<CommandResult> RunAsync(
            IReadOnlyList<string> command,
            string workingDirectory,
            long timeoutMillis,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_handler(command, workingDirectory));
    }

    private sealed class CoverageWorkspace : IDisposable
    {
        private readonly string _root;

        private CoverageWorkspace(string root)
        {
            _root = root;
        }

        public static CoverageWorkspace Create()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "mutate4net-coverage-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new CoverageWorkspace(root);
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
