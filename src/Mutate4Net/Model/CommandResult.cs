namespace Mutate4Net.Model;

public sealed record CommandResult(
    int ExitCode,
    string Output,
    long DurationMillis,
    bool TimedOut);

