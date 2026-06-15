using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mutate4Net.Manifest;
using Mutate4Net.Model;
using Mutate4Net.ProjectSystem;

namespace Mutate4Net.Analysis;

public sealed class MutationCatalog
{
    private readonly ManifestSupport _manifestSupport = new();
    private readonly ProjectCompilationBuilder _projectCompilationBuilder;

    public MutationCatalog()
        : this(new ProjectDiscovery())
    {
    }

    public MutationCatalog(ProjectDiscovery projectDiscovery)
    {
        _projectCompilationBuilder = new ProjectCompilationBuilder(projectDiscovery, _manifestSupport);
    }

    public async Task<SourceAnalysis> AnalyzeAsync(string file, string? projectFile = null)
    {
        ProjectCompilation? projectCompilation = await _projectCompilationBuilder.BuildAsync(file, projectFile);
        if (projectCompilation is not null)
        {
            return Analyze(file, projectCompilation.TargetSource, projectCompilation.TargetTree, projectCompilation.Compilation);
        }

        string raw = await File.ReadAllTextAsync(file);
        string source = _manifestSupport.StripManifest(raw);
        var sourceText = Microsoft.CodeAnalysis.Text.SourceText.From(source);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            sourceText,
            new CSharpParseOptions(LanguageVersion.Latest),
            path: file);
        Compilation compilation = CSharpCompilation.Create(
            "Mutate4Net.Analysis",
            new[] { tree },
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return Analyze(file, source, tree, compilation);
    }

    private SourceAnalysis Analyze(string file, string source, SyntaxTree tree, Compilation compilation)
    {
        SemanticModel semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        SyntaxNode root = tree.GetRoot();
        var scanner = new MutationScanner(file, source, tree, semanticModel);
        scanner.Visit(root);
        var scopes = scanner.Scopes.OrderBy(scope => scope.Id, StringComparer.Ordinal).ToArray();

        return new SourceAnalysis(
            file,
            source,
            scanner.Sites.OrderBy(site => site.Start).ToArray(),
            scopes,
            _manifestSupport.HashScopes(scopes));
    }

    private static IEnumerable<MetadataReference> CreateMetadataReferences()
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
}
