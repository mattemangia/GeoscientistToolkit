namespace GeoscientistToolkit.InstallerPackager.Models;

public sealed record CommandLineOptions
{
    public List<string> Platforms { get; init; } = new();
    public string? Configuration { get; init; }
    public string? OutputDirectory { get; init; }
    public string? Version { get; init; }
    public string? PackageBaseUrl { get; init; }
    public bool ShowHelp { get; init; }
    public bool Interactive { get; init; }

    public bool HasOverrides =>
        Platforms.Count > 0 ||
        Configuration != null ||
        OutputDirectory != null ||
        Version != null ||
        PackageBaseUrl != null;
}
