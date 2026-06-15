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
        return CreatePool(moduleRoot, sourceFile, workerCount: 1)[0];
    }

    public IReadOnlyList<WorkerWorkspace> CreatePool(string moduleRoot, string sourceFile, int workerCount)
    {
        string runRoot = Path.Combine(moduleRoot, ".mutate4net", "workers", "run-" + Guid.NewGuid().ToString("N"));
        string relativeSource = Path.GetRelativePath(moduleRoot, sourceFile);
        var workers = new List<WorkerWorkspace>(workerCount);

        for (int i = 1; i <= workerCount; i++)
        {
            string workerRoot = Path.Combine(runRoot, "worker-" + i);
            Directory.CreateDirectory(workerRoot);
            CopyDirectory(moduleRoot, workerRoot);

            string workerSource = Path.GetFullPath(Path.Combine(workerRoot, relativeSource));
            workers.Add(new WorkerWorkspace(runRoot, workerRoot, workerSource));
        }

        return workers;
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
