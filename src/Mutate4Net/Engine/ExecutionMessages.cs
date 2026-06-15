using System.Text;
using Mutate4Net.Cli;
using Mutate4Net.Execution;
using Mutate4Net.Model;

namespace Mutate4Net.Engine;

public sealed class ExecutionMessages
{
    public string ExtraText(
        CliArguments arguments,
        TestCommand command,
        DifferentialSelection differentialSelection,
        CoverageRun coverageRun,
        int coveredMutationSites,
        int uncoveredMutationSites)
    {
        var extra = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(arguments.ProjectFile))
        {
            extra.Append("Project: ").Append(arguments.ProjectFile).Append('\n');
        }

        extra.Append("Test command steps: ").Append(command.Commands.Count).Append('\n');
        if (command.Commands.Count == 1)
        {
            extra.Append("Test command: ").Append(string.Join(' ', command.Command)).Append('\n');
        }

        if (arguments.TestProjects.Count > 0)
        {
            extra.Append("Selected test projects: ").Append(string.Join(", ", arguments.TestProjects)).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(arguments.TestFilter))
        {
            extra.Append("Test filter: ").Append(arguments.TestFilter).Append('\n');
        }

        if (arguments.ExcludedTestProjects.Count > 0)
        {
            extra.Append("Excluded test projects: ").Append(string.Join(", ", arguments.ExcludedTestProjects)).Append('\n');
        }

        if (arguments.IncludedMutators.Count > 0)
        {
            extra.Append("Included mutators: ").Append(string.Join(", ", arguments.IncludedMutators)).Append('\n');
        }

        if (arguments.ExcludedMutators.Count > 0)
        {
            extra.Append("Excluded mutators: ").Append(string.Join(", ", arguments.ExcludedMutators)).Append('\n');
        }

        extra.Append("Total mutation sites: ").Append(differentialSelection.TotalMutationSites).Append('\n');
        extra.Append("Covered mutation sites: ").Append(coveredMutationSites).Append('\n');
        extra.Append("Uncovered mutation sites: ").Append(uncoveredMutationSites).Append('\n');
        extra.Append("Changed mutation sites: ").Append(differentialSelection.ChangedMutationSites).Append('\n');
        extra.Append("Manifest exists: ").Append(differentialSelection.ManifestExists.ToString().ToLowerInvariant()).Append('\n');
        extra.Append("Module hash changed: ").Append(differentialSelection.ModuleHashChanged.ToString().ToLowerInvariant()).Append('\n');
        extra.Append("Differential surface area: ").Append(differentialSelection.DifferentialSurfaceArea).Append('\n');
        extra.Append("Manifest-violating surface area: ")
            .Append(differentialSelection.ManifestViolatingSurfaceArea)
            .Append('\n');
        extra.Append("Coverage reused: ").Append(coverageRun.ReusedCoverage.ToString().ToLowerInvariant()).Append('\n');
        extra.Append("Coverage report available: ").Append(coverageRun.ReportAvailable.ToString().ToLowerInvariant()).Append('\n');

        if (differentialSelection.UnchangedModule)
        {
            extra.Append("No mutations need testing.\n");
        }

        if (arguments.Lines.Count > 0)
        {
            extra.Append("Line-filtered run; manifest not updated.\n");
        }

        if (arguments.IncludedMutators.Count > 0 || arguments.ExcludedMutators.Count > 0)
        {
            extra.Append("Mutator-filtered run; manifest not updated.\n");
        }

        if (!coverageRun.ReportAvailable)
        {
            if (!string.IsNullOrWhiteSpace(arguments.TestCommand))
            {
                extra.Append("Custom test command supplied; treating all selected mutation sites as covered.\n");
            }
            else
            {
                extra.Append("Coverage report unavailable; treating all selected mutation sites as covered.\n");
            }
        }

        if (coveredMutationSites > arguments.MutationWarning)
        {
            extra.Append("WARNING: Found ")
                .Append(coveredMutationSites)
                .Append(" mutations. Consider splitting this module.\n");
        }

        return extra.ToString();
    }
}
