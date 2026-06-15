namespace Mutate4Net.Execution;

public sealed class TestCommandFactory
{
    public TestCommand Create(string sourceFile, string? customCommand)
    {
        string workingDirectory = FindProjectDirectory(sourceFile);
        if (!string.IsNullOrWhiteSpace(customCommand))
        {
            return new TestCommand(ShellCommand(customCommand), workingDirectory);
        }

        string? project = FindNearestProject(sourceFile);
        if (project is null)
        {
            return new TestCommand(["dotnet", "test", "--no-restore"], workingDirectory);
        }

        return new TestCommand(["dotnet", "test", project, "--no-restore"], workingDirectory);
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

    private static string FindProjectDirectory(string sourceFile)
    {
        string? project = FindNearestProject(sourceFile);
        return project is null ? Path.GetDirectoryName(sourceFile)! : Path.GetDirectoryName(project)!;
    }

    private static string? FindNearestProject(string sourceFile)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null)
        {
            FileInfo[] projects = directory.GetFiles("*.csproj");
            if (projects.Length == 1)
            {
                return projects[0].FullName;
            }

            directory = directory.Parent;
        }

        return null;
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
