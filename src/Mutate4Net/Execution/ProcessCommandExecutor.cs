using System.Diagnostics;
using Mutate4Net.Model;

namespace Mutate4Net.Execution;

public sealed class ProcessCommandExecutor : ICommandExecutor
{
    public async Task<CommandResult> RunAsync(
        IReadOnlyList<string> command,
        string workingDirectory,
        long timeoutMillis,
        CancellationToken cancellationToken = default)
    {
        if (command.Count == 0)
        {
            throw new ArgumentException("Command must contain at least one token.", nameof(command));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command[0],
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in command.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        bool timedOut = false;

        try
        {
            Task waitTask = process.WaitForExitAsync(cancellationToken);
            if (timeoutMillis > 0)
            {
                Task completed = await Task.WhenAny(
                    waitTask,
                    Task.Delay(TimeSpan.FromMilliseconds(timeoutMillis), cancellationToken));
                if (!ReferenceEquals(completed, waitTask))
                {
                    timedOut = true;
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            else
            {
                await waitTask;
            }
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        stopwatch.Stop();
        string output = await outputTask + await errorTask;
        int exitCode = timedOut ? -1 : process.ExitCode;
        return new CommandResult(exitCode, output, stopwatch.ElapsedMilliseconds, timedOut);
    }
}
