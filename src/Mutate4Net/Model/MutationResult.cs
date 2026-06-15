namespace Mutate4Net.Model;

public sealed record MutationResult(
    MutationSite Site,
    bool Killed,
    long DurationMillis,
    bool TimedOut,
    int Order,
    int TotalJobs);

