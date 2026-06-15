namespace Mutate4Net.Execution;

public sealed record TestCommand(IReadOnlyList<string> Command, string WorkingDirectory);

