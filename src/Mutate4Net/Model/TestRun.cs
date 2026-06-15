namespace Mutate4Net.Model;

public sealed record TestRun(
    int ExitCode,
    string Output,
    long DurationMillis,
    bool TimedOut)
{
    public bool Passed => ExitCode == 0 && !TimedOut;
}

