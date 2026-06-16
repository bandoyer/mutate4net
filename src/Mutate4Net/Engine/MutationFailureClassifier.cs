using System.Text.RegularExpressions;
using Mutate4Net.Model;

namespace Mutate4Net.Engine;

public static class MutationFailureClassifier
{
    private static readonly Regex BuildErrorPattern = new(
        @"\b(error\s+(CS|NETSDK|MSB|NU)\d{3,5}|MSBUILD\s*:\s*error)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] InfrastructureMarkers =
    [
        "Project file does not exist",
        "Assets file",
        "project.assets.json",
        "Restore failed",
        "Failed to restore",
        "Unable to find package",
        "The imported project",
        "No test matches the given testcase filter",
        "No tests are available",
        "Zero tests ran",
        "Minimum expected tests policy violation",
        "tests ran 0, minimum expected",
        "mutate4net detected that the test command ran zero tests"
    ];

    public static MutationStatus Classify(CommandResult result)
    {
        if (result.ExitCode == 0 && !result.TimedOut)
        {
            return MutationStatus.Survived;
        }

        if (result.TimedOut)
        {
            return MutationStatus.Killed;
        }

        return IsInfrastructureFailure(result.Output)
            ? MutationStatus.Error
            : MutationStatus.Killed;
    }

    public static bool IsInfrastructureFailure(string output) =>
        BuildErrorPattern.IsMatch(output) ||
        InfrastructureMarkers.Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
