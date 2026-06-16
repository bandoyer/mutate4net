using Mutate4Net.Analysis;
using Mutate4Net.Engine;
using Mutate4Net.Manifest;
using Mutate4Net.ProjectSystem;
using Mutate4Net.Reporting;
using System.Reflection;

namespace Mutate4Net.Cli;

public sealed class CliApplication
{
    private readonly CliArgumentsParser _parser;
    private readonly MutationCatalog _catalog;
    private readonly ManifestSupport _manifestSupport;
    private readonly MutationRunService _mutationRunService;
    private readonly ScanReportFormatter _scanFormatter;
    private readonly ProjectDiscovery _projectDiscovery;

    public CliApplication()
        : this(
            new CliArgumentsParser(),
            new MutationCatalog(),
            new ManifestSupport(),
            new MutationRunService(),
            new ScanReportFormatter())
    {
    }

    public CliApplication(
        CliArgumentsParser parser,
        MutationCatalog catalog,
        ManifestSupport manifestSupport,
        MutationRunService mutationRunService,
        ScanReportFormatter scanFormatter,
        ProjectDiscovery? projectDiscovery = null)
    {
        _parser = parser;
        _catalog = catalog;
        _manifestSupport = manifestSupport;
        _mutationRunService = mutationRunService;
        _scanFormatter = scanFormatter;
        _projectDiscovery = projectDiscovery ?? new ProjectDiscovery();
    }

    public async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        ParseOutcome outcome = _parser.Parse(args);
        if (outcome.IsHelp)
        {
            await output.WriteAsync(UsageText.Text);
            return 0;
        }

        if (outcome.IsVersion)
        {
            await output.WriteLineAsync($"mutate4net {VersionText()}");
            return 0;
        }

        if (!outcome.IsSuccess || outcome.Arguments is null)
        {
            await error.WriteLineAsync(outcome.ErrorMessage);
            await error.WriteAsync(UsageText.Text);
            return 1;
        }

        if (outcome.Arguments.AllFiles)
        {
            return await RunAllFilesAsync(outcome.Arguments, output, error);
        }

        if (outcome.Arguments.Mode == CliMode.Scan)
        {
            try
            {
                var analysis = await _catalog.AnalyzeAsync(outcome.Arguments.TargetFile, outcome.Arguments.ProjectFile);
                var changedScopes = await _manifestSupport.FindChangedScopesAsync(outcome.Arguments.TargetFile, analysis);
                await output.WriteAsync(_scanFormatter.Format(
                    analysis,
                    changedScopes.ManifestPresent ? changedScopes.AllScopeIds() : null,
                    outcome.Arguments.Lines,
                    outcome.Arguments.IncludedMutators,
                    outcome.Arguments.ExcludedMutators));
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
                var analysis = await _catalog.AnalyzeAsync(outcome.Arguments.TargetFile, outcome.Arguments.ProjectFile);
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

        MutationRunOutcome run;
        try
        {
            run = await _mutationRunService.RunAsync(outcome.Arguments);
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"Failed running mutations for {outcome.Arguments.TargetFile}: {ex.Message}");
            return 1;
        }

        if (!string.IsNullOrEmpty(run.Output))
        {
            await output.WriteAsync(run.Output);
        }

        if (!string.IsNullOrEmpty(run.Error))
        {
            await error.WriteAsync(run.Error);
        }

        return run.ExitCode;
    }

    private async Task<int> RunAllFilesAsync(CliArguments arguments, TextWriter output, TextWriter error)
    {
        ProjectSourceSet sources;
        try
        {
            sources = _projectDiscovery.DiscoverProjectSources(arguments.TargetFile, arguments.ProjectFile);
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"Failed discovering source files for {arguments.TargetFile}: {ex.Message}");
            return 1;
        }

        if (sources.SourceFiles.Count == 0)
        {
            await error.WriteLineAsync($"No source files were found for {sources.Project.ProjectFile}.");
            return 1;
        }

        await output.WriteLineAsync($"All-files target: {arguments.TargetFile}");
        await output.WriteLineAsync($"Project: {sources.Project.ProjectFile}");
        await output.WriteLineAsync($"Source files: {sources.SourceFiles.Count}");

        return arguments.Mode switch
        {
            CliMode.Scan => await ScanAllFilesAsync(arguments, sources, output, error),
            CliMode.UpdateManifest => await UpdateAllManifestsAsync(arguments, sources, output, error),
            _ => await MutateAllFilesAsync(arguments, sources, output, error)
        };
    }

    private async Task<int> ScanAllFilesAsync(
        CliArguments arguments,
        ProjectSourceSet sources,
        TextWriter output,
        TextWriter error)
    {
        for (int i = 0; i < sources.SourceFiles.Count; i++)
        {
            string sourceFile = sources.SourceFiles[i];
            CliArguments fileArguments = FileArguments(arguments, sources, sourceFile);
            try
            {
                var analysis = await _catalog.AnalyzeAsync(sourceFile, sources.Project.ProjectFile);
                var changedScopes = await _manifestSupport.FindChangedScopesAsync(sourceFile, analysis);
                await output.WriteLineAsync();
                await output.WriteLineAsync($"== {i + 1}/{sources.SourceFiles.Count}: {RelativeToProject(sources, sourceFile)} ==");
                await output.WriteAsync(_scanFormatter.Format(
                    analysis,
                    changedScopes.ManifestPresent ? changedScopes.AllScopeIds() : null,
                    fileArguments.Lines,
                    fileArguments.IncludedMutators,
                    fileArguments.ExcludedMutators));
            }
            catch (Exception ex)
            {
                await error.WriteLineAsync($"Failed analyzing {sourceFile}: {ex.Message}");
                return 1;
            }
        }

        return 0;
    }

    private async Task<int> UpdateAllManifestsAsync(
        CliArguments arguments,
        ProjectSourceSet sources,
        TextWriter output,
        TextWriter error)
    {
        foreach (string sourceFile in sources.SourceFiles)
        {
            try
            {
                var analysis = await _catalog.AnalyzeAsync(sourceFile, sources.Project.ProjectFile);
                await _manifestSupport.WriteAsync(
                    sourceFile,
                    analysis.Source,
                    _manifestSupport.CreateManifest(analysis));
                await output.WriteLineAsync($"Updated manifest for {RelativeToProject(sources, sourceFile)}");
            }
            catch (Exception ex)
            {
                await error.WriteLineAsync($"Failed updating manifest for {sourceFile}: {ex.Message}");
                return 1;
            }
        }

        return 0;
    }

    private async Task<int> MutateAllFilesAsync(
        CliArguments arguments,
        ProjectSourceSet sources,
        TextWriter output,
        TextWriter error)
    {
        int exitCode = 0;
        for (int i = 0; i < sources.SourceFiles.Count; i++)
        {
            string sourceFile = sources.SourceFiles[i];
            await output.WriteLineAsync();
            await output.WriteLineAsync($"== {i + 1}/{sources.SourceFiles.Count}: {RelativeToProject(sources, sourceFile)} ==");

            MutationRunOutcome run;
            try
            {
                run = await _mutationRunService.RunAsync(FileArguments(arguments, sources, sourceFile));
            }
            catch (Exception ex)
            {
                await error.WriteLineAsync($"Failed running mutations for {sourceFile}: {ex.Message}");
                return 1;
            }

            if (!string.IsNullOrEmpty(run.Output))
            {
                await output.WriteAsync(run.Output);
            }

            if (!string.IsNullOrEmpty(run.Error))
            {
                await error.WriteAsync(run.Error);
            }

            if (run.ExitCode == 2)
            {
                return 2;
            }

            if (run.ExitCode == 3)
            {
                exitCode = 3;
            }
            else if (run.ExitCode != 0)
            {
                return run.ExitCode;
            }
        }

        return exitCode;
    }

    private static CliArguments FileArguments(CliArguments arguments, ProjectSourceSet sources, string sourceFile) =>
        arguments with
        {
            TargetFile = sourceFile,
            ProjectFile = sources.Project.ProjectFile,
            AllFiles = false
        };

    private static string RelativeToProject(ProjectSourceSet sources, string sourceFile)
    {
        string relative = Path.GetRelativePath(sources.Project.ProjectDirectory, sourceFile);
        return relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative)
            ? sourceFile
            : relative;
    }

    private static string VersionText()
    {
        Assembly assembly = typeof(CliApplication).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
