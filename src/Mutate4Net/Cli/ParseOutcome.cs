namespace Mutate4Net.Cli;

public sealed record ParseOutcome(bool IsSuccess, bool IsHelp, bool IsVersion, CliArguments? Arguments, string? ErrorMessage)
{
    public static ParseOutcome Success(CliArguments arguments) => new(true, false, false, arguments, null);

    public static ParseOutcome Help() => new(false, true, false, null, null);

    public static ParseOutcome Version() => new(false, false, true, null, null);

    public static ParseOutcome Error(string? message) => new(false, false, false, null, message);
}
