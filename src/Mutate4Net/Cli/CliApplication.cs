using Mutate4Net.Analysis;
using Mutate4Net.Reporting;

namespace Mutate4Net.Cli;

public sealed class CliApplication
{
    private readonly CliArgumentsParser _parser;
    private readonly MutationCatalog _catalog;
    private readonly ScanReportFormatter _scanFormatter;

    public CliApplication()
        : this(new CliArgumentsParser(), new MutationCatalog(), new ScanReportFormatter())
    {
    }

    public CliApplication(CliArgumentsParser parser, MutationCatalog catalog, ScanReportFormatter scanFormatter)
    {
        _parser = parser;
        _catalog = catalog;
        _scanFormatter = scanFormatter;
    }

    public async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        ParseOutcome outcome = _parser.Parse(args);
        if (outcome.IsHelp)
        {
            await output.WriteAsync(UsageText.Text);
            return 0;
        }

        if (!outcome.IsSuccess || outcome.Arguments is null)
        {
            await error.WriteLineAsync(outcome.ErrorMessage);
            await error.WriteAsync(UsageText.Text);
            return 1;
        }

        if (outcome.Arguments.Mode == CliMode.Scan)
        {
            try
            {
                var analysis = await _catalog.AnalyzeAsync(outcome.Arguments.TargetFile);
                await output.WriteAsync(_scanFormatter.Format(analysis));
                return 0;
            }
            catch (Exception ex)
            {
                await error.WriteLineAsync($"Failed analyzing {outcome.Arguments.TargetFile}: {ex.Message}");
                return 1;
            }
        }

        await error.WriteLineAsync("Only --scan is implemented in this initial slice.");
        return 1;
    }
}

