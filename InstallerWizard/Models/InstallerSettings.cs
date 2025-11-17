using System.Text.Json.Serialization;

namespace GeoscientistToolkit.Installer.Models;

public sealed record InstallerSettings(
    string ProductName,
    string ManifestUrl,
    string DefaultInstallRoot,
    string ProjectPath,
    string MetadataFileName,
    bool EnableLogs)
{
    public static InstallerSettings Default => new(
        ProductName: "Geoscientist Toolkit",
        ManifestUrl: "docs/installer-manifest.json",
        DefaultInstallRoot: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GeoscientistToolkit"),
        ProjectPath: "../GeoscientistToolkit.csproj",
        MetadataFileName: "install-info.json",
        EnableLogs: true);

    [JsonIgnore]
    public string SettingsDirectory { get; init; } = AppContext.BaseDirectory;
}
