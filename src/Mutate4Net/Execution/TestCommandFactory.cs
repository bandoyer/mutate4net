using Mutate4Net.Cli;
using Mutate4Net.ProjectSystem;

namespace Mutate4Net.Execution;

public sealed class TestCommandFactory
{
    private readonly ProjectDiscovery _projectDiscovery;

    public TestCommandFactory()
        : this(new ProjectDiscovery())
    {
    }

    public TestCommandFactory(ProjectDiscovery projectDiscovery)
    {
        _projectDiscovery = projectDiscovery;
    }

    public TestCommand Create(CliArguments arguments) =>
        Create(
            arguments.TargetFile,
            arguments.TestCommand,
            arguments.TestProjects,
            arguments.ExcludedTestProjects);

    public TestCommand Create(string sourceFile, string? customCommand)
    {
        return Create(sourceFile, customCommand, [], []);
    }

    public TestCommand Create(
        string sourceFile,
        string? customCommand,
        IReadOnlyList<string> testProjects,
        IReadOnlyList<string> excludedTestProjects)
    {
        ProjectInfo? project = _projectDiscovery.Discover(sourceFile);
        string workingDirectory = project is null
            ? Path.GetDirectoryName(sourceFile)!
            : WorkingDirectory(project);
        if (!string.IsNullOrWhiteSpace(customCommand))
        {
            return new TestCommand(ShellCommand(customCommand), workingDirectory, IsCustom: true, DisplayCommand: customCommand);
        }

        IReadOnlyList<string> selectedTestProjects = SelectTestProjects(
            workingDirectory,
            testProjects,
            excludedTestProjects);
        if (selectedTestProjects.Count > 0)
        {
            return new TestCommand(
                selectedTestProjects.Select(testProject => (IReadOnlyList<string>)["dotnet", "test", testProject]).ToArray(),
                workingDirectory);
        }

        if (project is null)
        {
            return new TestCommand(["dotnet", "test"], workingDirectory);
        }

        string testTarget = project.SolutionFile ?? project.ProjectFile;
        return new TestCommand(["dotnet", "test", testTarget], workingDirectory);
    }

    public TestCommand CreateCoverageCommand(CliArguments arguments, string coverageOutputPrefix)
    {
        TestCommand baseline = Create(
            arguments.TargetFile,
            customCommand: null,
            arguments.TestProjects,
            arguments.ExcludedTestProjects);
        int commandCount = baseline.Commands.Count;
        IReadOnlyList<IReadOnlyList<string>> commands = baseline.Commands
            .Select((command, index) =>
            {
                var coverageCommand = command.ToList();
                coverageCommand.Add("/p:CollectCoverage=true");
                coverageCommand.Add("/p:CoverletOutputFormat=cobertura");
                coverageCommand.Add($"/p:CoverletOutput={CoverageOutputPrefix(coverageOutputPrefix, index, commandCount)}");
                return (IReadOnlyList<string>)coverageCommand;
            })
            .ToArray();
        return new TestCommand(commands, baseline.WorkingDirectory);
    }

    public TestCommand CreateCollectorCoverageCommand(CliArguments arguments, string resultsDirectory)
    {
        TestCommand baseline = Create(
            arguments.TargetFile,
            customCommand: null,
            arguments.TestProjects,
            arguments.ExcludedTestProjects);
        IReadOnlyList<IReadOnlyList<string>> commands = baseline.Commands
            .Select(command =>
            {
                var coverageCommand = command.ToList();
                coverageCommand.Add("--collect");
                coverageCommand.Add("XPlat Code Coverage");
                coverageCommand.Add("--results-directory");
                coverageCommand.Add(resultsDirectory);
                return (IReadOnlyList<string>)coverageCommand;
            })
            .ToArray();
        return new TestCommand(commands, baseline.WorkingDirectory);
    }

    public static string CoverageOutputPrefix(string coverageOutputPrefix, int index, int commandCount) =>
        commandCount == 1 ? coverageOutputPrefix : $"{coverageOutputPrefix}-{index + 1}";

    private IReadOnlyList<string> SelectTestProjects(
        string workingDirectory,
        IReadOnlyList<string> testProjects,
        IReadOnlyList<string> excludedTestProjects)
    {
        if (testProjects.Count == 0 && excludedTestProjects.Count == 0)
        {
            return [];
        }

        IReadOnlyList<string> candidates = testProjects.Count > 0
            ? testProjects.Select(selector => ResolveTestProject(workingDirectory, selector)).ToArray()
            : _projectDiscovery.DiscoverTestProjects(workingDirectory);
        string[] selected = candidates
            .Where(project => !excludedTestProjects.Any(selector => MatchesProjectSelector(project, selector, workingDirectory)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(project => project, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (selected.Length == 0)
        {
            throw new InvalidOperationException("No test projects matched the requested test project selection.");
        }

        return selected;
    }

    private string ResolveTestProject(string workingDirectory, string selector)
    {
        string candidate = Path.IsPathRooted(selector)
            ? Path.GetFullPath(selector)
            : Path.GetFullPath(Path.Combine(workingDirectory, selector));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        string[] matches = _projectDiscovery.DiscoverTestProjects(workingDirectory)
            .Where(project => MatchesProjectSelector(project, selector, workingDirectory))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"Test project was not found: {selector}"),
            _ => throw new InvalidOperationException($"Multiple test projects match {selector}: {string.Join(", ", matches)}")
        };
    }

    private static IReadOnlyList<string> ShellCommand(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return ["cmd.exe", "/c", command];
        }

        return ["/bin/sh", "-c", command];
    }

    private static string WorkingDirectory(ProjectInfo project) =>
        project.SolutionFile is null
            ? project.ProjectDirectory
            : Path.GetDirectoryName(project.SolutionFile)!;

    private static bool MatchesProjectSelector(string projectPath, string selector, string workingDirectory)
    {
        string normalizedSelector = NormalizePath(selector);
        string fullProjectPath = Path.GetFullPath(projectPath);
        string relativeProjectPath = NormalizePath(Path.GetRelativePath(workingDirectory, fullProjectPath));
        string projectFileName = Path.GetFileName(fullProjectPath);
        string projectName = Path.GetFileNameWithoutExtension(fullProjectPath);

        if (Path.IsPathRooted(selector) &&
            string.Equals(Path.GetFullPath(selector), fullProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(relativeProjectPath, normalizedSelector, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(projectFileName, selector, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(projectName, selector, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
