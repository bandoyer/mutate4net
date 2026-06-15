using Mutate4Net.Cli;
using Mutate4Net.Model;

namespace Mutate4Net.Coverage;

public sealed class CoverageLoader
{
    private readonly CoberturaLineCoverageParser _parser;

    public CoverageLoader()
        : this(new CoberturaLineCoverageParser())
    {
    }

    public CoverageLoader(CoberturaLineCoverageParser parser)
    {
        _parser = parser;
    }

    public CoverageReport Load(CliArguments arguments, string moduleRoot)
    {
        if (!arguments.ReuseCoverage)
        {
            return CoverageReport.AllCovered();
        }

        string coveragePath = DefaultCoveragePath(moduleRoot);
        return File.Exists(coveragePath)
            ? _parser.Parse(coveragePath)
            : CoverageReport.AllCovered();
    }

    public CoverageReport LoadPathOrAllCovered(string coveragePath) =>
        File.Exists(coveragePath) ? _parser.Parse(coveragePath) : CoverageReport.AllCovered();

    public static string DefaultCoverageDirectory(string moduleRoot) =>
        Path.Combine(moduleRoot, ".mutate4net", "coverage");

    public static string DefaultCoverageOutputPrefix(string moduleRoot) =>
        Path.Combine(DefaultCoverageDirectory(moduleRoot), "coverage");

    public static string DefaultCoveragePath(string moduleRoot) =>
        DefaultCoverageOutputPrefix(moduleRoot) + ".cobertura.xml";
}
