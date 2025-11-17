namespace GeoscientistToolkit.Installer.Models;

public sealed record InstallMetadata
{
    public string ProductName { get; init; } = string.Empty;
    public string Version { get; init; } = "0.0.0";
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public string InstallPath { get; init; } = string.Empty;
    public string ManifestUrl { get; init; } = string.Empty;
    public DateTime InstalledAt { get; init; } = DateTime.UtcNow;
    public List<string> Components { get; init; } = new();
    public bool CreateDesktopShortcut { get; init; }
}
