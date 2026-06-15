using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mutate4Net.ProjectSystem;

public sealed class ProjectDiscovery
{
    private static readonly HashSet<string> TestPackageReferences = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.NET.Test.Sdk",
        "xunit",
        "xunit.runner.visualstudio",
        "NUnit",
        "NUnit3TestAdapter",
        "MSTest.TestFramework",
        "MSTest.TestAdapter"
    };

    private static readonly HashSet<string> DefaultExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        ".idea",
        ".mutate4net"
    };

    public ProjectInfo? Discover(string sourceFile)
    {
        string fullSource = Path.GetFullPath(sourceFile);
        ProjectInfo[] matches = CandidateProjects(fullSource)
            .Where(project => IncludesSource(project.ProjectFile, fullSource))
            .ToArray();

        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length > 1)
        {
            string projects = string.Join(", ", matches.Select(match => match.ProjectFile));
            throw new InvalidOperationException($"Multiple projects include {fullSource}: {projects}");
        }

        ProjectInfo match = matches[0];
        if (match.IsTestProject)
        {
            throw new InvalidOperationException($"Mutation targets in test projects are not supported: {match.ProjectFile}");
        }

        return match;
    }

    public IReadOnlyList<string> DiscoverTestProjects(string moduleRoot)
    {
        string fullRoot = Path.GetFullPath(moduleRoot);
        return Directory.EnumerateFiles(fullRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(project => !IsUnderDefaultExcludedDirectory(
                NormalizePath(Path.GetRelativePath(fullRoot, Path.GetDirectoryName(project)!))))
            .Where(IsTestProject)
            .OrderBy(project => project, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<ProjectInfo> CandidateProjects(string sourceFile)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null)
        {
            foreach (FileInfo project in directory.GetFiles("*.csproj"))
            {
                yield return new ProjectInfo(
                    project.FullName,
                    project.DirectoryName!,
                    IsTestProject(project.FullName),
                    FindNearestSolution(project.Directory!));
            }

            directory = directory.Parent;
        }
    }

    private static bool IncludesSource(string projectFile, string sourceFile)
    {
        string projectDirectory = Path.GetDirectoryName(projectFile)!;
        string relative = NormalizePath(Path.GetRelativePath(projectDirectory, sourceFile));
        if (relative.StartsWith("../", StringComparison.Ordinal) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            !relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        XDocument document = XDocument.Load(projectFile, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidOperationException($"Invalid project file: {projectFile}");

        if (Descendants(root, "Compile").Any(element => MatchesItem(element.Attribute("Remove")?.Value, relative)))
        {
            return false;
        }

        bool defaultCompileItems = !Descendants(root, "EnableDefaultCompileItems")
            .Any(element => string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
        if (defaultCompileItems)
        {
            return !IsUnderDefaultExcludedDirectory(relative);
        }

        return Descendants(root, "Compile")
            .Any(element => MatchesItem(element.Attribute("Include")?.Value, relative));
    }

    private static bool IsTestProject(string projectFile)
    {
        string name = Path.GetFileNameWithoutExtension(projectFile);
        if (name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        XDocument document = XDocument.Load(projectFile, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidOperationException($"Invalid project file: {projectFile}");

        if (Descendants(root, "IsTestProject")
            .Any(element => string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return Descendants(root, "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value => TestPackageReferences.Contains(value!));
    }

    private static bool MatchesItem(string? itemSpec, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(itemSpec))
        {
            return false;
        }

        foreach (string item in itemSpec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string pattern = NormalizePath(item);
            if (string.Equals(pattern, relativePath, StringComparison.OrdinalIgnoreCase) ||
                MatchesGlob(pattern, relativePath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesGlob(string pattern, string relativePath)
    {
        if (pattern.StartsWith("**/", StringComparison.Ordinal) &&
            MatchesGlob(pattern[3..], relativePath))
        {
            return true;
        }

        string regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal)
            .Replace("\\?", "[^/]", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(relativePath, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsUnderDefaultExcludedDirectory(string relativePath) =>
        relativePath.Split('/').Any(part => DefaultExcludedDirectories.Contains(part));

    private static IEnumerable<XElement> Descendants(XElement root, string localName) =>
        root.Descendants().Where(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    private static string? FindNearestSolution(DirectoryInfo projectDirectory)
    {
        DirectoryInfo? directory = projectDirectory;
        while (directory is not null)
        {
            FileInfo[] solutions = directory.GetFiles("*.sln")
                .Concat(directory.GetFiles("*.slnx"))
                .ToArray();
            if (solutions.Length == 1)
            {
                return solutions[0].FullName;
            }

            if (solutions.Length > 1)
            {
                string files = string.Join(", ", solutions.Select(solution => solution.FullName));
                throw new InvalidOperationException($"Multiple solution files found near {projectDirectory.FullName}: {files}");
            }

            directory = directory.Parent;
        }

        return null;
    }
}
