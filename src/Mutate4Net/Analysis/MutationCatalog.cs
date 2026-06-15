using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mutate4Net.Manifest;
using Mutate4Net.Model;

namespace Mutate4Net.Analysis;

public sealed class MutationCatalog
{
    private readonly ManifestSupport _manifestSupport = new();

    public async Task<SourceAnalysis> AnalyzeAsync(string file)
    {
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

        SemanticModel semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        SyntaxNode root = await tree.GetRootAsync();
        var scanner = new MutationScanner(file, source, tree, semanticModel);
        scanner.Visit(root);

        return new SourceAnalysis(
            file,
            source,
            scanner.Sites.OrderBy(site => site.Start).ToArray(),
            scanner.Scopes.OrderBy(scope => scope.Id, StringComparer.Ordinal).ToArray());
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

