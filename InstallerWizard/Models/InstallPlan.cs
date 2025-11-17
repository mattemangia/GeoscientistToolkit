namespace GeoscientistToolkit.Installer.Models;

public sealed record InstallPlan(
    InstallerManifest Manifest,
    RuntimePackage Package,
    string RuntimeIdentifier,
    string InstallPath,
    IReadOnlyList<RuntimeComponent> Components,
    bool CreateDesktopShortcut);
