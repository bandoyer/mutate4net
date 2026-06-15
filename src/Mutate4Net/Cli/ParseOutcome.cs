namespace Mutate4Net.Cli;

public sealed record ParseOutcome(bool IsSuccess, bool IsHelp, CliArguments? Arguments, string? ErrorMessage)
{
    public static ParseOutcome Success(CliArguments arguments) => new(true, false, arguments, null);

    public static ParseOutcome Help() => new(false, true, null, null);

    public static ParseOutcome Error(string? message) => new(false, false, null, message);
}

