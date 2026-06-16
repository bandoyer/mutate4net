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

    [Fact]
    public void Create_SkipsDefaultHeavyweightDirectories()
    {
        using var workspace = TempWorkspace.Create();
        string source = workspace.Write("src/Sample.cs", "class Sample { }");
        workspace.Write("node_modules/pkg/index.js", "module.exports = {};");
        workspace.Write("dist/bundle.js", "compiled");
        workspace.Write(".dotnet-cli/cache/state.txt", "tool state");
        workspace.Write(".claude/settings.local.json", "{}");
        workspace.Write("StrykerOutput/report.html", "<html></html>");
        workspace.Write("logs/latest.log", "log");
        workspace.Write("src/Models/Logs/ApplicationLog.cs", "class ApplicationLog { }");
        workspace.Write("src/Web/.Infrastructure/Alert.cs", "class Alert { }");
        workspace.Write("tmp/cache.txt", "cache");
        workspace.Write("tmp-run/cache.txt", "cache");
        var manager = new WorkerWorkspaceManager();

        WorkerWorkspace worker = manager.Create(workspace.Root, source);

        Assert.False(Directory.Exists(Path.Combine(worker.ModuleRoot, "node_modules")));
        Assert.False(Directory.Exists(Path.Combine(worker.ModuleRoot, "dist")));
        Assert.False(Directory.Exists(Path.Combine(worker.ModuleRoot, ".dotnet-cli")));
        Assert.False(Directory.Exists(Path.Combine(worker.ModuleRoot, ".claude")));
        Assert.False(Directory.Exists(Path.Combine(worker.ModuleRoot, "StrykerOutput")));
        Assert.False(Directory.Exists(Path.Combine(worker.ModuleRoot, "logs")));
        Assert.True(File.Exists(Path.Combine(worker.ModuleRoot, "src", "Models", "Logs", "ApplicationLog.cs")));
        Assert.True(File.Exists(Path.Combine(worker.ModuleRoot, "src", "Web", ".Infrastructure", "Alert.cs")));
        Assert.False(Directory.Exists(Path.Combine(worker.ModuleRoot, "tmp")));
        Assert.False(Directory.Exists(Path.Combine(worker.ModuleRoot, "tmp-run")));
    }

    [Fact]
    public void Create_UsesMutate4NetIgnorePatterns()
    {
        using var workspace = TempWorkspace.Create();
        string source = workspace.Write("src/Sample.cs", "class Sample { }");
        workspace.Write(".mutate4netignore", """
            # trim non-build files from worker copies
            docs/
            *.tmp
            src/**/Generated.cs
            """);
        workspace.Write("docs/guide.md", "large docs");
        workspace.Write("src/Notes.tmp", "scratch");
        workspace.Write("src/Nested/Generated.cs", "class Generated { }");
        var manager = new WorkerWorkspaceManager();

        WorkerWorkspace worker = manager.Create(workspace.Root, source);

        Assert.False(Directory.Exists(Path.Combine(worker.ModuleRoot, "docs")));
        Assert.False(File.Exists(Path.Combine(worker.ModuleRoot, "src", "Notes.tmp")));
        Assert.False(File.Exists(Path.Combine(worker.ModuleRoot, "src", "Nested", "Generated.cs")));
        Assert.True(File.Exists(Path.Combine(worker.ModuleRoot, "src", "Sample.cs")));
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
