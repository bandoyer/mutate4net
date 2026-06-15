using Mutate4Net.Model;

namespace Mutate4Net.Execution;

public interface ICommandExecutor
{
    Task<CommandResult> RunAsync(
        IReadOnlyList<string> command,
        string workingDirectory,
        long timeoutMillis,
        CancellationToken cancellationToken = default);
}

