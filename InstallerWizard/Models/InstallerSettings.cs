using System.Text.Json.Serialization;

namespace GAIA.Installer.Models;

public sealed record InstallerSettings(
    string ProductName,
    string ManifestUrl,
    string DefaultInstallRoot,
    string ProjectPath,
    string MetadataFileName,
    bool EnableLogs)
{
    public static InstallerSettings Default => new(
        ProductName: "GAIA (Geoscience Analysis, Imaging & Automation)",
        ManifestUrl: "installer-manifest.json",
        DefaultInstallRoot: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GAIA"),
        ProjectPath: "../GAIA.csproj",
        MetadataFileName: "install-info.json",
        EnableLogs: true);

    [JsonIgnore]
    public string SettingsDirectory { get; init; } = AppContext.BaseDirectory;
}
