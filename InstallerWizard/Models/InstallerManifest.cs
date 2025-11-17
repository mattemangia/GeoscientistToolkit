namespace GeoscientistToolkit.Installer.Models;

public sealed record InstallerManifest
{
    public string Version { get; init; } = "0.0.0";
    public string? ReleaseNotesUrl { get; init; }
    public string? MinimumDotnetSdk { get; init; }
    public List<RuntimePackage> Packages { get; init; } = new();
    public List<InstallerPrerequisite> Prerequisites { get; init; } = new();
}

public sealed record RuntimePackage
{
    public string RuntimeIdentifier { get; init; } = "win-x64";
    public PackageTransport Transport { get; set; } = PackageTransport.Archive;
    public bool SelfContained { get; init; } = true;
    public string? PackageUrl { get; set; }
    public string? Sha256 { get; set; }
    public long SizeBytes { get; set; }
    public string? Description { get; init; }
    public string? AdditionalPublishArguments { get; init; }
    public List<RuntimeComponent> Components { get; init; } = new();
}

public sealed record RuntimeComponent
{
    public string Id { get; init; } = "app";
    public string DisplayName { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string TargetSubdirectory { get; init; } = string.Empty;
    public string? EntryExecutable { get; init; }
    public bool DefaultSelected { get; init; } = true;
    public bool SupportsDesktopShortcut { get; init; }
}

public enum PackageTransport
{
    Archive,
    Publish
}

public sealed record InstallerPrerequisite
{
    public string Id { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string HelpUrl { get; init; } = string.Empty;
}
