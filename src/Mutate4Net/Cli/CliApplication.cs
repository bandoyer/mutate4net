using Mutate4Net.Analysis;
using Mutate4Net.Manifest;
using Mutate4Net.Reporting;

namespace Mutate4Net.Cli;

public sealed class CliApplication
{
    private readonly CliArgumentsParser _parser;
    private readonly MutationCatalog _catalog;
    private readonly ManifestSupport _manifestSupport;
    private readonly ScanReportFormatter _scanFormatter;

    public CliApplication()
        : this(new CliArgumentsParser(), new MutationCatalog(), new ManifestSupport(), new ScanReportFormatter())
    {
    }

    public CliApplication(
        CliArgumentsParser parser,
        MutationCatalog catalog,
        ManifestSupport manifestSupport,
        ScanReportFormatter scanFormatter)
    {
        _parser = parser;
        _catalog = catalog;
        _manifestSupport = manifestSupport;
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
                var changedScopes = await _manifestSupport.FindChangedScopesAsync(outcome.Arguments.TargetFile, analysis);
                await output.WriteAsync(_scanFormatter.Format(
                    analysis,
                    changedScopes.ManifestPresent ? changedScopes.AllScopeIds() : null));
                return 0;
            }
            catch (Exception ex)
            {
                await error.WriteLineAsync($"Failed analyzing {outcome.Arguments.TargetFile}: {ex.Message}");
                return 1;
            }
        }

        if (outcome.Arguments.Mode == CliMode.UpdateManifest)
        {
            try
            {
                var analysis = await _catalog.AnalyzeAsync(outcome.Arguments.TargetFile);
                await _manifestSupport.WriteAsync(
                    outcome.Arguments.TargetFile,
                    analysis.Source,
                    _manifestSupport.CreateManifest(analysis));
                await output.WriteLineAsync($"Updated manifest for {outcome.Arguments.TargetFile}");
                return 0;
            }
            catch (Exception ex)
            {
                await error.WriteLineAsync($"Failed updating manifest for {outcome.Arguments.TargetFile}: {ex.Message}");
                return 1;
            }
        }

        await error.WriteLineAsync("Only --scan is implemented in this initial slice.");
        return 1;
    }
}
