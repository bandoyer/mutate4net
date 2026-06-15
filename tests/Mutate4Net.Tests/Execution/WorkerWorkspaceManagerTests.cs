using Mutate4Net.Execution;

namespace Mutate4Net.Tests.Execution;

public sealed class WorkerWorkspaceManagerTests
{
    [Fact]
    public void Delete_RemovesEmptyToolDirectories()
    {
        using var workspace = TempWorkspace.Create();
        string source = workspace.Write("src/Sample.cs", "class Sample { }");
        var manager = new WorkerWorkspaceManager();
        WorkerWorkspace worker = manager.Create(workspace.Root, source);

        manager.Delete(worker);

        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".mutate4net")));
    }

    [Fact]
    public void Delete_PreservesToolDirectoryWhenCoverageExists()
    {
        using var workspace = TempWorkspace.Create();
        string source = workspace.Write("src/Sample.cs", "class Sample { }");
        string coverageDirectory = Path.Combine(workspace.Root, ".mutate4net", "coverage");
        Directory.CreateDirectory(coverageDirectory);
        File.WriteAllText(Path.Combine(coverageDirectory, "coverage.cobertura.xml"), "<coverage />");
        var manager = new WorkerWorkspaceManager();
        WorkerWorkspace worker = manager.Create(workspace.Root, source);

        manager.Delete(worker);

        Assert.True(Directory.Exists(coverageDirectory));
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".mutate4net", "workers")));
    }

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TempWorkspace Create()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "mutate4net-worker-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TempWorkspace(root);
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
