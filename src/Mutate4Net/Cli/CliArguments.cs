namespace Mutate4Net.Cli;

public sealed record CliArguments(
    string TargetFile,
    CliMode Mode,
    bool ReuseCoverage,
    IReadOnlySet<int> Lines,
    bool SinceLastRun,
    bool MutateAll,
    int MutationWarning,
    int MaxWorkers,
    int TimeoutFactor,
    string? ProjectFile,
    string? TestCommand,
    string? TestFilter,
    bool Verbose,
    IReadOnlyList<string> TestProjects,
    IReadOnlyList<string> ExcludedTestProjects,
    IReadOnlySet<string> IncludedMutators,
    IReadOnlySet<string> ExcludedMutators,
    bool AllFiles = false)
{
    public TestRunner TestRunner { get; init; } = TestRunner.VsTest;

    public IReadOnlyList<string> MtpFilterClasses { get; init; } = Array.Empty<string>();
}
