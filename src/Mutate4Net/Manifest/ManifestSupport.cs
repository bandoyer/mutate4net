namespace Mutate4Net.Manifest;

public sealed class ManifestSupport
{
    private const string StartMarker = "/* mutate4net-manifest";
    private const string EndMarker = "*/";

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
}

