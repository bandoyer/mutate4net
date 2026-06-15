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
        "TestResults",
        "node_modules",
        "packages",
        "artifacts",
        "coverage",
        "dist",
        ".cache",
        ".next",
        ".angular"
    };

    public WorkerWorkspace Create(string moduleRoot, string sourceFile)
    {
        return CreatePool(moduleRoot, sourceFile, workerCount: 1)[0];
    }

    public IReadOnlyList<WorkerWorkspace> CreatePool(string moduleRoot, string sourceFile, int workerCount)
    {
        string runRoot = Path.Combine(moduleRoot, ".mutate4net", "workers", "run-" + Guid.NewGuid().ToString("N"));
        string relativeSource = Path.GetRelativePath(moduleRoot, sourceFile);
        IReadOnlyList<IgnorePattern> ignorePatterns = LoadIgnorePatterns(moduleRoot);
        var workers = new List<WorkerWorkspace>(workerCount);

        for (int i = 1; i <= workerCount; i++)
        {
            string workerRoot = Path.Combine(runRoot, "worker-" + i);
            Directory.CreateDirectory(workerRoot);
            CopyDirectory(moduleRoot, workerRoot, moduleRoot, ignorePatterns);

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
            PruneEmptyDirectory(Path.GetDirectoryName(workspace.RunRoot));
            PruneEmptyDirectory(Path.GetDirectoryName(Path.GetDirectoryName(workspace.RunRoot)!));
        }
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory,
        string moduleRoot,
        IReadOnlyList<IgnorePattern> ignorePatterns)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            string name = Path.GetFileName(directory);
            string relativePath = NormalizePath(Path.GetRelativePath(moduleRoot, directory));
            if (ExcludedDirectories.Contains(name) ||
                ignorePatterns.Any(pattern => pattern.Matches(relativePath, isDirectory: true)))
            {
                continue;
            }

            string destination = Path.Combine(destinationDirectory, name);
            Directory.CreateDirectory(destination);
            CopyDirectory(directory, destination, moduleRoot, ignorePatterns);
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            string relativePath = NormalizePath(Path.GetRelativePath(moduleRoot, file));
            if (ignorePatterns.Any(pattern => pattern.Matches(relativePath, isDirectory: false)))
            {
                continue;
            }

            string destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void PruneEmptyDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) ||
            !Directory.Exists(directory) ||
            Directory.EnumerateFileSystemEntries(directory).Any())
        {
            return;
        }

        Directory.Delete(directory);
    }

    private static IReadOnlyList<IgnorePattern> LoadIgnorePatterns(string moduleRoot)
    {
        string ignoreFile = Path.Combine(moduleRoot, ".mutate4netignore");
        if (!File.Exists(ignoreFile))
        {
            return [];
        }

        return File.ReadAllLines(ignoreFile)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(IgnorePattern.Create)
            .ToArray();
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private sealed record IgnorePattern(string Pattern, bool DirectoryOnly)
    {
        public static IgnorePattern Create(string raw)
        {
            string normalized = NormalizePath(raw.Trim());
            bool directoryOnly = normalized.EndsWith('/');
            if (directoryOnly)
            {
                normalized = normalized.TrimEnd('/');
            }

            return new IgnorePattern(normalized, directoryOnly);
        }

        public bool Matches(string relativePath, bool isDirectory)
        {
            if (DirectoryOnly && !isDirectory)
            {
                return false;
            }

            string normalized = NormalizePath(relativePath);
            if (!Pattern.Contains('/') && !HasWildcard(Pattern))
            {
                return normalized
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => string.Equals(part, Pattern, StringComparison.OrdinalIgnoreCase));
            }

            if (!Pattern.Contains('/') && HasWildcard(Pattern))
            {
                return normalized
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Any(part => MatchesGlob(Pattern, part));
            }

            return string.Equals(normalized, Pattern, StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(Pattern + "/", StringComparison.OrdinalIgnoreCase) ||
                   MatchesGlob(Pattern, normalized);
        }

        private static bool HasWildcard(string pattern) =>
            pattern.Contains('*') || pattern.Contains('?');

        private static bool MatchesGlob(string pattern, string relativePath)
        {
            string regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*\\*", ".*", StringComparison.Ordinal)
                .Replace("\\*", "[^/]*", StringComparison.Ordinal)
                .Replace("\\?", "[^/]", StringComparison.Ordinal) + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(
                relativePath,
                regex,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
    }
}
