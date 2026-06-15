namespace Mutate4Net.Cli;

public sealed class CliArgumentsParser
{
    public ParseOutcome Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return ParseOutcome.Error("Expected exactly one .cs file target.");
        }

        var targets = new List<string>();
        var lines = new SortedSet<int>();
        bool scan = false;
        bool updateManifest = false;
        bool reuseCoverage = false;
        bool sinceLastRun = false;
        bool mutateAll = false;
        bool verbose = false;
        int mutationWarning = 50;
        int maxWorkers = Math.Max(1, Environment.ProcessorCount / 2);
        int timeoutFactor = 10;
        string? testCommand = null;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    return ParseOutcome.Help();
                case "--version":
                    return ParseOutcome.Version();
                case "--scan":
                    scan = true;
                    break;
                case "--update-manifest":
                    updateManifest = true;
                    break;
                case "--reuse-coverage":
                    reuseCoverage = true;
                    break;
                case "--since-last-run":
                    sinceLastRun = true;
                    break;
                case "--mutate-all":
                    mutateAll = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--lines":
                    if (!TryReadValue(args, ref i, arg, out string? lineValue, out ParseOutcome? lineError))
                    {
                        return lineError!;
                    }

                    if (!TryParseLines(lineValue, lines, out string? linesError))
                    {
                        return ParseOutcome.Error(linesError);
                    }

                    break;
                case "--mutation-warning":
                    if (!TryReadPositiveInteger(args, ref i, arg, out mutationWarning, out ParseOutcome? warningError))
                    {
                        return warningError!;
                    }

                    break;
                case "--max-workers":
                    if (!TryReadPositiveInteger(args, ref i, arg, out maxWorkers, out ParseOutcome? workersError))
                    {
                        return workersError!;
                    }

                    break;
                case "--timeout-factor":
                    if (!TryReadPositiveInteger(args, ref i, arg, out timeoutFactor, out ParseOutcome? timeoutError))
                    {
                        return timeoutError!;
                    }

                    break;
                case "--test-command":
                    if (!TryReadValue(args, ref i, arg, out testCommand, out ParseOutcome? commandError))
                    {
                        return commandError!;
                    }

                    if (string.IsNullOrWhiteSpace(testCommand))
                    {
                        return ParseOutcome.Error("--test-command requires a non-empty value.");
                    }

                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        return ParseOutcome.Error($"Unknown option: {arg}");
                    }

                    targets.Add(arg);
                    break;
            }
        }

        if (targets.Count != 1)
        {
            return ParseOutcome.Error("Expected exactly one .cs file target.");
        }

        string target = targets[0];
        if (Directory.Exists(target))
        {
            return ParseOutcome.Error("Directory targets are not supported.");
        }

        if (!string.Equals(Path.GetExtension(target), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return ParseOutcome.Error("Target must be a .cs source file.");
        }

        if (!File.Exists(target))
        {
            return ParseOutcome.Error($"Target file does not exist: {target}");
        }

        string? conflict = FindConflict(scan, updateManifest, reuseCoverage, lines.Count > 0, sinceLastRun, mutateAll);
        if (conflict is not null)
        {
            return ParseOutcome.Error(conflict);
        }

        CliMode mode = updateManifest ? CliMode.UpdateManifest : scan ? CliMode.Scan : CliMode.Mutate;
        var parsed = new CliArguments(
            Path.GetFullPath(target),
            mode,
            reuseCoverage,
            lines,
            sinceLastRun,
            mutateAll,
            mutationWarning,
            maxWorkers,
            timeoutFactor,
            testCommand,
            verbose);

        return ParseOutcome.Success(parsed);
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        out string value,
        out ParseOutcome? error)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = string.Empty;
            error = ParseOutcome.Error($"{option} requires a value.");
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }

    private static bool TryReadPositiveInteger(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        out int value,
        out ParseOutcome? error)
    {
        if (!TryReadValue(args, ref index, option, out string raw, out error))
        {
            value = 0;
            return false;
        }

        if (!int.TryParse(raw, out value) || value <= 0)
        {
            error = ParseOutcome.Error($"{option} requires a positive integer.");
            return false;
        }

        return true;
    }

    private static bool TryParseLines(string raw, ISet<int> lines, out string? error)
    {
        foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out int line) || line <= 0)
            {
                error = "--lines requires a comma-separated list of positive integers.";
                return false;
            }

            lines.Add(line);
        }

        if (lines.Count == 0)
        {
            error = "--lines requires at least one line number.";
            return false;
        }

        error = null;
        return true;
    }

    private static string? FindConflict(
        bool scan,
        bool updateManifest,
        bool reuseCoverage,
        bool hasLines,
        bool sinceLastRun,
        bool mutateAll)
    {
        if (scan && updateManifest)
        {
            return "--scan may not be combined with --update-manifest.";
        }

        if (scan && sinceLastRun)
        {
            return "--scan may not be combined with --since-last-run.";
        }

        if (scan && mutateAll)
        {
            return "--scan may not be combined with --mutate-all.";
        }

        if (scan && reuseCoverage)
        {
            return "--scan may not be combined with --reuse-coverage.";
        }

        if (hasLines && sinceLastRun)
        {
            return "--lines may not be combined with --since-last-run.";
        }

        if (hasLines && mutateAll)
        {
            return "--lines may not be combined with --mutate-all.";
        }

        if (hasLines && updateManifest)
        {
            return "--lines may not be combined with --update-manifest.";
        }

        if (sinceLastRun && mutateAll)
        {
            return "--since-last-run may not be combined with --mutate-all.";
        }

        if (updateManifest && sinceLastRun)
        {
            return "--update-manifest may not be combined with --since-last-run.";
        }

        if (updateManifest && mutateAll)
        {
            return "--update-manifest may not be combined with --mutate-all.";
        }

        if (updateManifest && reuseCoverage)
        {
            return "--update-manifest may not be combined with --reuse-coverage.";
        }

        return null;
    }
}
