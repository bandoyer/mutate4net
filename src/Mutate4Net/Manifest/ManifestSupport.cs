using System.Security.Cryptography;
using System.Text;
using Mutate4Net.Model;
using Mutate4Net.Selection;

namespace Mutate4Net.Manifest;

public sealed class ManifestSupport
{
    private const string StartMarker = "/* mutate4net-manifest";
    private const string EndMarker = "*/";
    private const int CurrentVersion = 1;

    public async Task<DifferentialManifest?> ReadAsync(string sourceFile)
    {
        string raw = await File.ReadAllTextAsync(sourceFile);
        int start = raw.LastIndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        int end = raw.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        string body = raw[(start + StartMarker.Length)..end].Trim();
        return Parse(body);
    }

    public string StripManifest(string source)
    {
        int start = source.LastIndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return source;
        }

        int end = source.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return source;
        }

        string after = source[(end + EndMarker.Length)..];
        if (!string.IsNullOrWhiteSpace(after))
        {
            return source;
        }

        return source[..start].TrimEnd() + Environment.NewLine;
    }

    public DifferentialManifest CreateManifest(SourceAnalysis analysis) =>
        new(
            CurrentVersion,
            analysis.ModuleHash,
            analysis.Scopes.OrderBy(scope => scope.Id, StringComparer.Ordinal).ToArray());

    public async Task WriteAsync(string sourceFile, string sourceWithoutManifest, DifferentialManifest manifest)
    {
        string updated = sourceWithoutManifest.TrimEnd() + "\n\n" + Serialize(manifest);
        await File.WriteAllTextAsync(sourceFile, updated);
    }

    public async Task<ChangedScopes> FindChangedScopesAsync(string sourceFile, SourceAnalysis analysis)
    {
        DifferentialManifest? previous = await ReadAsync(sourceFile);
        if (previous is null)
        {
            return new ChangedScopes(false, false, new HashSet<string>(), new HashSet<string>());
        }

        if (string.Equals(previous.ModuleHash, analysis.ModuleHash, StringComparison.Ordinal))
        {
            return new ChangedScopes(true, false, new HashSet<string>(), new HashSet<string>());
        }

        var previousHashes = previous.Scopes.ToDictionary(scope => scope.Id, scope => scope.SemanticHash, StringComparer.Ordinal);
        var unregisteredScopes = new HashSet<string>(StringComparer.Ordinal);
        var manifestViolations = new HashSet<string>(StringComparer.Ordinal);

        foreach (MutationScope scope in analysis.Scopes)
        {
            if (!previousHashes.TryGetValue(scope.Id, out string? previousHash))
            {
                unregisteredScopes.Add(scope.Id);
            }
            else if (!string.Equals(scope.SemanticHash, previousHash, StringComparison.Ordinal))
            {
                manifestViolations.Add(scope.Id);
            }
        }

        return new ChangedScopes(true, true, unregisteredScopes, manifestViolations);
    }

    public string HashScopes(IEnumerable<MutationScope> scopes)
    {
        var builder = new StringBuilder();
        foreach (MutationScope scope in scopes.OrderBy(scope => scope.Id, StringComparer.Ordinal))
        {
            builder.Append(scope.Id)
                .Append('|')
                .Append(scope.SemanticHash)
                .Append('\n');
        }

        return Hash(builder.ToString());
    }

    public string Hash(string text)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static DifferentialManifest Parse(string body)
    {
        var scopes = new SortedDictionary<int, Dictionary<string, string>>();
        int version = CurrentVersion;
        string moduleHash = string.Empty;

        foreach (string line in body.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            string key = line[..separator];
            string value = line[(separator + 1)..];

            if (string.Equals(key, "version", StringComparison.Ordinal))
            {
                version = int.Parse(value);
                continue;
            }

            if (string.Equals(key, "moduleHash", StringComparison.Ordinal))
            {
                moduleHash = value;
                continue;
            }

            if (!key.StartsWith("scope.", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = key.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[1], out int index))
            {
                continue;
            }

            if (!scopes.TryGetValue(index, out Dictionary<string, string>? scopeValues))
            {
                scopeValues = new Dictionary<string, string>(StringComparer.Ordinal);
                scopes[index] = scopeValues;
            }

            scopeValues[parts[2]] = value;
        }

        return new DifferentialManifest(version, moduleHash, scopes.Values.Select(ToScope).ToArray());
    }

    private static string Serialize(DifferentialManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine(StartMarker);
        builder.Append("version=").Append(manifest.Version).Append('\n');
        builder.Append("moduleHash=").Append(manifest.ModuleHash).Append('\n');

        for (int i = 0; i < manifest.Scopes.Count; i++)
        {
            AppendScope(builder, i, manifest.Scopes[i]);
        }

        builder.Append(EndMarker).Append('\n');
        return builder.ToString();
    }

    private static void AppendScope(StringBuilder builder, int index, MutationScope scope)
    {
        builder.Append("scope.").Append(index).Append(".id=").Append(Encode(scope.Id)).Append('\n');
        builder.Append("scope.").Append(index).Append(".kind=").Append(scope.Kind).Append('\n');
        builder.Append("scope.").Append(index).Append(".startLine=").Append(scope.StartLine).Append('\n');
        builder.Append("scope.").Append(index).Append(".endLine=").Append(scope.EndLine).Append('\n');
        builder.Append("scope.").Append(index).Append(".semanticHash=").Append(scope.SemanticHash).Append('\n');
    }

    private static MutationScope ToScope(IReadOnlyDictionary<string, string> values) =>
        new(
            Decode(values["id"]),
            values["kind"],
            int.Parse(values["startLine"]),
            int.Parse(values["endLine"]),
            values["semanticHash"]);

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string Decode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        int padding = (4 - padded.Length % 4) % 4;
        padded = padded.PadRight(padded.Length + padding, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
