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

        string coveragePath = Path.Combine(moduleRoot, ".mutate4net", "coverage", "coverage.cobertura.xml");
        return File.Exists(coveragePath)
            ? _parser.Parse(coveragePath)
            : CoverageReport.AllCovered();
    }
}

