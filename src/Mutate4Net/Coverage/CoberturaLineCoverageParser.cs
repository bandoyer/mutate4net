using System.Xml.Linq;
using Mutate4Net.Model;

namespace Mutate4Net.Coverage;

public sealed class CoberturaLineCoverageParser
{
    public CoverageReport Parse(string coberturaXmlPath)
    {
        if (!File.Exists(coberturaXmlPath))
        {
            return new CoverageReport(new HashSet<CoverageSite>());
        }

        XDocument document = XDocument.Load(coberturaXmlPath, LoadOptions.None);
        var coveredLines = new HashSet<CoverageSite>();

        foreach (XElement classElement in document.Descendants("class"))
        {
            string? filename = (string?)classElement.Attribute("filename");
            if (string.IsNullOrWhiteSpace(filename))
            {
                continue;
            }

            string sourcePath = CoverageReport.NormalizePath(filename);
            foreach (XElement line in classElement.Descendants("line"))
            {
                int lineNumber = ParseInt((string?)line.Attribute("number"));
                int hits = ParseInt((string?)line.Attribute("hits"));
                if (lineNumber > 0 && hits > 0)
                {
                    coveredLines.Add(new CoverageSite(sourcePath, lineNumber));
                }
            }
        }

        return new CoverageReport(coveredLines);
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, out int parsed) ? parsed : 0;
}

