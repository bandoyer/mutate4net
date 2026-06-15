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

    public TestCommand Create(string sourceFile, string? customCommand)
    {
        ProjectInfo? project = _projectDiscovery.Discover(sourceFile);
        string workingDirectory = project is null
            ? Path.GetDirectoryName(sourceFile)!
            : WorkingDirectory(project);
        if (!string.IsNullOrWhiteSpace(customCommand))
        {
            return new TestCommand(ShellCommand(customCommand), workingDirectory, IsCustom: true, DisplayCommand: customCommand);
        }

        if (project is null)
        {
            return new TestCommand(["dotnet", "test"], workingDirectory);
        }

        string testTarget = project.SolutionFile ?? project.ProjectFile;
        return new TestCommand(["dotnet", "test", testTarget], workingDirectory);
    }

    public TestCommand CreateCoverageCommand(string sourceFile, string coverageOutputPrefix)
    {
        TestCommand baseline = Create(sourceFile, customCommand: null);
        var command = baseline.Command.ToList();
        command.Add("/p:CollectCoverage=true");
        command.Add("/p:CoverletOutputFormat=cobertura");
        command.Add($"/p:CoverletOutput={coverageOutputPrefix}");
        return new TestCommand(command, baseline.WorkingDirectory);
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
}
