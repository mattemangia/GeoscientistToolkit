using System;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Installer.Models;

namespace GeoscientistToolkit.Installer.Services;

internal sealed class InstallPlanBuilder
{

    public InstallPlan CreatePlan(
        InstallerManifest manifest,
        string runtimeIdentifier,
        string installPath,
        IEnumerable<string> selectedComponentIds,
        bool createDesktopShortcut)
    {
        var package = manifest.Packages.FirstOrDefault(p => string.Equals(p.RuntimeIdentifier, runtimeIdentifier, StringComparison.OrdinalIgnoreCase));
        if (package is null)
        {
            throw new InvalidOperationException($"Nessun pacchetto configurato per {runtimeIdentifier}");
        }

        var availableComponents = package.Components.Count == 0
            ? new List<RuntimeComponent>
            {
                new()
                {
                    Id = "payload",
                    DisplayName = package.Description ?? "Contenuto",
                    RelativePath = ".",
                    TargetSubdirectory = string.Empty,
                    EntryExecutable = null,
                    DefaultSelected = true,
                    SupportsDesktopShortcut = false
                }
            }
            : package.Components;

        var components = availableComponents
            .Where(c => selectedComponentIds.Contains(c.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (components.Count == 0)
        {
            components = availableComponents.Where(c => c.DefaultSelected).ToList();
        }

        return new InstallPlan(manifest, package, runtimeIdentifier, installPath, components, createDesktopShortcut);
    }
}
