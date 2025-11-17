using System.Text.Json.Serialization;

namespace GeoscientistToolkit.InstallerPackager.Models;

public sealed record PackagerSettings
{
    public string ManifestPath { get; init; } = "docs/installer-manifest.json";
    public string PackagesOutputDirectory { get; init; } = "artifacts/installers";
    public string PackageBaseUrl { get; init; } = "https://example.com/installers";
    public string ProjectPath { get; init; } = "../GeoscientistToolkit.csproj";
    public string NodeProjectPath { get; init; } = "../NodeEndpoint/NodeEndpoint.csproj";
    public string PublishConfiguration { get; init; } = "Release";
    public string OnnxDirectory { get; init; } = "../ONNX";
    public string PackageName { get; init; } = "GeoscientistToolkit";
    public string NodeExecutableName { get; init; } = "NodeEndpoint";
    public string? NodeAdditionalPublishArguments { get; init; }
    public string? AdditionalPublishArguments { get; init; }

    [JsonIgnore]
    public string SettingsDirectory { get; init; } = AppContext.BaseDirectory;
}
