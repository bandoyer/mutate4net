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
        string workingDirectory = project?.ProjectDirectory ?? Path.GetDirectoryName(sourceFile)!;
        if (!string.IsNullOrWhiteSpace(customCommand))
        {
            return new TestCommand(ShellCommand(customCommand), workingDirectory, IsCustom: true, DisplayCommand: customCommand);
        }

        if (project is null)
        {
            return new TestCommand(["dotnet", "test", "--no-restore"], workingDirectory);
        }

        return new TestCommand(["dotnet", "test", project.ProjectFile, "--no-restore"], workingDirectory);
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
}
