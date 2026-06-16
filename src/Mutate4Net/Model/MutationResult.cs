namespace Mutate4Net.Model;

public sealed record MutationResult(
    MutationSite Site,
    MutationStatus Status,
    long DurationMillis,
    bool TimedOut,
    int Order,
    int TotalJobs,
    string? FailureOutput = null)
{
    public bool Killed => Status == MutationStatus.Killed;

    public bool Survived => Status == MutationStatus.Survived;

    public bool Error => Status == MutationStatus.Error;
}
