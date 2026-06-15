using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Mutate4Net.Manifest;
using Mutate4Net.ProjectSystem;

namespace Mutate4Net.Analysis;

internal sealed class ProjectCompilationBuilder
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        ".idea",
        ".mutate4net"
    };

    private readonly ProjectDiscovery _projectDiscovery;
    private readonly ManifestSupport _manifestSupport;

    public ProjectCompilationBuilder(ProjectDiscovery projectDiscovery, ManifestSupport manifestSupport)
    {
        _projectDiscovery = projectDiscovery;
        _manifestSupport = manifestSupport;
    }

    public async Task<ProjectCompilation?> BuildAsync(string targetFile, string? projectFile)
    {
        ProjectInfo? project = _projectDiscovery.Discover(targetFile, projectFile);
        if (project is null)
        {
            return null;
        }

        string fullTarget = Path.GetFullPath(targetFile);
        string[] projectFiles = ProjectGraph(project.ProjectFile).ToArray();
        string[] sourceFiles = projectFiles
            .SelectMany(SourceFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var syntaxTrees = new List<SyntaxTree>();
        SyntaxTree? targetTree = null;
        string? targetSource = null;
        foreach (string sourceFile in sourceFiles)
        {
            string source = _manifestSupport.StripManifest(await File.ReadAllTextAsync(sourceFile));
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                SourceText.From(source),
                ParseOptions,
                path: sourceFile);
            syntaxTrees.Add(tree);

            if (string.Equals(Path.GetFullPath(sourceFile), fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                targetTree = tree;
                targetSource = source;
            }
        }

        if (targetTree is null || targetSource is null)
        {
            return null;
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "Mutate4Net.ProjectAnalysis",
            syntaxTrees,
            CreateMetadataReferences(projectFiles),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new ProjectCompilation(compilation, targetTree, targetSource);
    }

    private static IEnumerable<string> ProjectGraph(string projectFile)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(Path.GetFullPath(projectFile));

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;

            string projectDirectory = Path.GetDirectoryName(current)!;
            foreach (string reference in ProjectReferences(current))
            {
                string referencedProject = Path.GetFullPath(Path.Combine(projectDirectory, reference));
                if (File.Exists(referencedProject))
                {
                    stack.Push(referencedProject);
                }
            }
        }
    }

    private static IEnumerable<string> SourceFiles(string projectFile)
    {
        string projectDirectory = Path.GetDirectoryName(projectFile)!;
        XDocument document = XDocument.Load(projectFile, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidOperationException($"Invalid project file: {projectFile}");
        string[] removed = Descendants(root, "Compile")
            .Select(element => element.Attribute("Remove")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(SplitItems)
            .ToArray();

        bool defaultCompileItems = !Descendants(root, "EnableDefaultCompileItems")
            .Any(element => string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));

        IEnumerable<string> candidates = defaultCompileItems
            ? Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            : Descendants(root, "Compile")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .SelectMany(SplitItems)
                .Select(item => Path.GetFullPath(Path.Combine(projectDirectory, item)))
                .Where(File.Exists);

        foreach (string candidate in candidates)
        {
            string relative = NormalizePath(Path.GetRelativePath(projectDirectory, candidate));
            if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                IsUnderExcludedDirectory(relative) ||
                removed.Any(remove => MatchesItem(remove, relative)))
            {
                continue;
            }

            yield return Path.GetFullPath(candidate);
        }
    }

    private static IEnumerable<MetadataReference> CreateMetadataReferences(IEnumerable<string> projectFiles)
    {
        foreach (MetadataReference reference in PlatformReferences())
        {
            yield return reference;
        }

        foreach (string referencePath in projectFiles.SelectMany(HintPathReferences).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return MetadataReference.CreateFromFile(referencePath);
        }
    }

    private static IEnumerable<MetadataReference> PlatformReferences()
    {
        string? trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (trustedAssemblies is null)
        {
            yield break;
        }

        foreach (string assemblyPath in trustedAssemblies.Split(Path.PathSeparator))
        {
            yield return MetadataReference.CreateFromFile(assemblyPath);
        }
    }

    private static IEnumerable<string> HintPathReferences(string projectFile)
    {
        string projectDirectory = Path.GetDirectoryName(projectFile)!;
        XDocument document = XDocument.Load(projectFile, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidOperationException($"Invalid project file: {projectFile}");
        foreach (string hintPath in Descendants(root, "HintPath")
                     .Select(element => element.Value.Trim())
                     .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            string fullPath = Path.GetFullPath(Path.Combine(projectDirectory, hintPath));
            if (File.Exists(fullPath))
            {
                yield return fullPath;
            }
        }
    }

    private static IEnumerable<string> ProjectReferences(string projectFile)
    {
        XDocument document = XDocument.Load(projectFile, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidOperationException($"Invalid project file: {projectFile}");
        return Descendants(root, "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))!;
    }

    private static IEnumerable<XElement> Descendants(XElement root, string localName) =>
        root.Descendants().Where(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));

    private static IEnumerable<string> SplitItems(string? itemSpec) =>
        string.IsNullOrWhiteSpace(itemSpec)
            ? []
            : itemSpec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool MatchesItem(string itemSpec, string relativePath)
    {
        string pattern = NormalizePath(itemSpec);
        return string.Equals(pattern, relativePath, StringComparison.OrdinalIgnoreCase) ||
               MatchesGlob(pattern, relativePath);
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

    private static bool IsUnderExcludedDirectory(string relativePath) =>
        relativePath.Split('/').Any(part => ExcludedDirectories.Contains(part));

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');
}

internal sealed record ProjectCompilation(
    CSharpCompilation Compilation,
    SyntaxTree TargetTree,
    string TargetSource);
