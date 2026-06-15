namespace Mutate4Net.Execution;

public sealed class WorkerWorkspaceManager
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        ".mutate4net",
        "bin",
        "obj",
        "TestResults"
    };

    public WorkerWorkspace Create(string moduleRoot, string sourceFile)
    {
        string runRoot = Path.Combine(moduleRoot, ".mutate4net", "workers", "run-" + Guid.NewGuid().ToString("N"));
        string workerRoot = Path.Combine(runRoot, "worker-1");
        Directory.CreateDirectory(workerRoot);
        CopyDirectory(moduleRoot, workerRoot);

        string relativeSource = Path.GetRelativePath(moduleRoot, sourceFile);
        string workerSource = Path.GetFullPath(Path.Combine(workerRoot, relativeSource));
        return new WorkerWorkspace(runRoot, workerRoot, workerSource);
    }

    public void Delete(WorkerWorkspace workspace)
    {
        if (Directory.Exists(workspace.RunRoot))
        {
            Directory.Delete(workspace.RunRoot, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            string name = Path.GetFileName(directory);
            if (ExcludedDirectories.Contains(name))
            {
                continue;
            }

            string destination = Path.Combine(destinationDirectory, name);
            Directory.CreateDirectory(destination);
            CopyDirectory(directory, destination);
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            string destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
        }
    }
}

