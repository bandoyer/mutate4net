using System.Text;
using Mutate4Net.Cli;
using Mutate4Net.Model;

namespace Mutate4Net.Engine;

public sealed class ExecutionMessages
{
    public string ExtraText(
        CliArguments arguments,
        DifferentialSelection differentialSelection,
        int coveredMutationSites,
        int uncoveredMutationSites)
    {
        var extra = new StringBuilder();
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

        if (differentialSelection.UnchangedModule)
        {
            extra.Append("No mutations need testing.\n");
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
