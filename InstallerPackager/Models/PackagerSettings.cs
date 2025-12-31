using System.Text.Json.Serialization;

namespace GeoscientistToolkit.InstallerPackager.Models;

public sealed record PackagerSettings
{
    public string ManifestPath { get; init; } = "docs/installer-manifest.json";
    public string PackagesOutputDirectory { get; init; } = "artifacts/installers";
    public string PackageBaseUrl { get; init; } = "https://example.com/installers";
    public string ImGuiProjectPath { get; init; } = "../GeoscientistToolkit.csproj";
    public string ImGuiExecutableName { get; init; } = "GeoscientistToolkit";
    public string GtkProjectPath { get; init; } = "../GTK/GeoscientistToolkit.Gtk.csproj";
    public string GtkExecutableName { get; init; } = "GeoscientistToolkit.Gtk";
    public string NodeEndpointProjectPath { get; init; } = "../NodeEndpoint/NodeEndpoint.csproj";
    public string NodeEndpointExecutableName { get; init; } = "NodeEndpoint";
    public string NodeServerProjectPath { get; init; } = "../NodeEndpoint/NodeEndpoint.csproj";
    public string NodeServerExecutableName { get; init; } = "NodeEndpoint";
    public string InstallerProjectPath { get; init; } = "../InstallerWizard/InstallerWizard.csproj";
    public string InstallerExecutableName { get; init; } = "GeoscientistToolkitInstaller";
    public string PublishConfiguration { get; init; } = "Release";
    public string OnnxDirectory { get; init; } = "../ONNX";
    public string OnnxInstallerTemplatePath { get; init; } = "../InstallerPackager/Assets/onnx-installer";
    public string PackageName { get; init; } = "GeoscientistToolkit";
    public string? NodeAdditionalPublishArguments { get; init; }
    public string? AdditionalPublishArguments { get; init; }

    [JsonIgnore]
    public string SettingsDirectory { get; init; } = AppContext.BaseDirectory;
}
