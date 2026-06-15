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
        string? projectFile = null;
        string? testCommand = null;
        string? testFilter = null;
        var testProjects = new List<string>();
        var excludedTestProjects = new List<string>();
        var includedMutators = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludedMutators = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

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
                case "--project":
                    if (!TryReadValue(args, ref i, arg, out projectFile, out ParseOutcome? projectError))
                    {
                        return projectError!;
                    }

                    if (string.IsNullOrWhiteSpace(projectFile))
                    {
                        return ParseOutcome.Error("--project requires a non-empty value.");
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
                case "--test-filter":
                    if (!TryReadValue(args, ref i, arg, out testFilter, out ParseOutcome? filterError))
                    {
                        return filterError!;
                    }

                    if (string.IsNullOrWhiteSpace(testFilter))
                    {
                        return ParseOutcome.Error("--test-filter requires a non-empty value.");
                    }

                    break;
                case "--test-project":
                    if (!TryReadValue(args, ref i, arg, out string? testProject, out ParseOutcome? testProjectError))
                    {
                        return testProjectError!;
                    }

                    if (string.IsNullOrWhiteSpace(testProject))
                    {
                        return ParseOutcome.Error("--test-project requires a non-empty value.");
                    }

                    testProjects.Add(testProject);
                    break;
                case "--exclude-test-project":
                    if (!TryReadValue(args, ref i, arg, out string? excludedTestProject, out ParseOutcome? excludedTestProjectError))
                    {
                        return excludedTestProjectError!;
                    }

                    if (string.IsNullOrWhiteSpace(excludedTestProject))
                    {
                        return ParseOutcome.Error("--exclude-test-project requires a non-empty value.");
                    }

                    excludedTestProjects.Add(excludedTestProject);
                    break;
                case "--mutator":
                    if (!TryReadValueList(args, ref i, arg, includedMutators, out ParseOutcome? mutatorError))
                    {
                        return mutatorError!;
                    }

                    break;
                case "--exclude-mutator":
                    if (!TryReadValueList(args, ref i, arg, excludedMutators, out ParseOutcome? excludedMutatorError))
                    {
                        return excludedMutatorError!;
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

        string? mutatorOverlap = includedMutators.FirstOrDefault(mutator => excludedMutators.Contains(mutator));
        if (mutatorOverlap is not null)
        {
            return ParseOutcome.Error($"Mutator filter includes and excludes the same value: {mutatorOverlap}");
        }

        if (projectFile is not null)
        {
            projectFile = Path.GetFullPath(projectFile);
            if (!File.Exists(projectFile))
            {
                return ParseOutcome.Error($"Project file does not exist: {projectFile}");
            }

            if (!string.Equals(Path.GetExtension(projectFile), ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return ParseOutcome.Error("--project must reference a .csproj file.");
            }
        }

        string? conflict = FindConflict(
            scan,
            updateManifest,
            reuseCoverage,
            lines.Count > 0,
            sinceLastRun,
            mutateAll,
            !string.IsNullOrWhiteSpace(testCommand),
            !string.IsNullOrWhiteSpace(testFilter),
            testProjects.Count > 0,
            excludedTestProjects.Count > 0,
            includedMutators.Count > 0 || excludedMutators.Count > 0);
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
            projectFile,
            testCommand,
            testFilter,
            verbose,
            testProjects,
            excludedTestProjects,
            includedMutators,
            excludedMutators);

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

    private static bool TryReadValueList(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        ISet<string> values,
        out ParseOutcome? error)
    {
        if (!TryReadValue(args, ref index, option, out string raw, out error))
        {
            return false;
        }

        int before = values.Count;
        foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0)
            {
                values.Add(part.ToLowerInvariant());
            }
        }

        if (values.Count == before)
        {
            error = ParseOutcome.Error($"{option} requires at least one comma-separated value.");
            return false;
        }

        error = null;
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
        bool mutateAll,
        bool hasTestCommand,
        bool hasTestFilter,
        bool hasTestProjects,
        bool hasExcludedTestProjects,
        bool hasMutatorFilters)
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

        if (scan && (hasTestProjects || hasExcludedTestProjects))
        {
            return "--scan may not be combined with test project selection.";
        }

        if (scan && hasTestFilter)
        {
            return "--scan may not be combined with --test-filter.";
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

        if (updateManifest && (hasTestProjects || hasExcludedTestProjects))
        {
            return "--update-manifest may not be combined with test project selection.";
        }

        if (updateManifest && hasTestFilter)
        {
            return "--update-manifest may not be combined with --test-filter.";
        }

        if (updateManifest && hasMutatorFilters)
        {
            return "--update-manifest may not be combined with mutator filters.";
        }

        if (hasTestCommand && (hasTestProjects || hasExcludedTestProjects))
        {
            return "--test-command may not be combined with test project selection.";
        }

        if (hasTestCommand && hasTestFilter)
        {
            return "--test-command may not be combined with --test-filter.";
        }

        return null;
    }
}
