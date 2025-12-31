using System.Text.Json;
using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.Installer.Utilities;

namespace GeoscientistToolkit.Installer.Services;

internal sealed class UpdateService
{
    private readonly ManifestLoader _manifestLoader;
    private readonly InstallerSettings _settings;

    public UpdateService(ManifestLoader manifestLoader, InstallerSettings settings)
    {
        _manifestLoader = manifestLoader;
        _settings = settings;
    }

    public InstallMetadata? TryLoadExistingMetadata(string installPath)
    {
        var metadataFile = Path.Combine(installPath, _settings.MetadataFileName);
        if (!File.Exists(metadataFile))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(metadataFile);
            return JsonSerializer.Deserialize<InstallMetadata>(stream, JsonOptions.Value.Value);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool hasUpdate, Version? latestVersion, InstallerManifest manifest)> CheckForUpdatesAsync(InstallMetadata? metadata, CancellationToken token = default)
    {
        var manifest = await _manifestLoader.LoadAsync(metadata?.ManifestUrl, token).ConfigureAwait(false);
        if (metadata is null)
        {
            return (false, Version.TryParse(manifest.Version, out var parsed) ? parsed : null, manifest);
        }

        if (!Version.TryParse(metadata.Version, out var localVersion))
        {
            return (true, Version.TryParse(manifest.Version, out var parsed) ? parsed : null, manifest);
        }

        var remoteVersion = Version.TryParse(manifest.Version, out var manifestVersion) ? manifestVersion : localVersion;
        var hasUpdate = remoteVersion > localVersion;
        return (hasUpdate, remoteVersion, manifest);
    }

    public async Task SaveMetadataAsync(InstallPlan plan, CancellationToken token = default)
    {
        var metadata = new InstallMetadata
        {
            ProductName = _settings.ProductName,
            Version = plan.Manifest.Version,
            PackageId = plan.Package.PackageId,
            RuntimeIdentifier = plan.RuntimeIdentifier,
            InstallPath = plan.InstallPath,
            ManifestUrl = _settings.ManifestUrl,
            InstalledAt = DateTime.UtcNow,
            Components = plan.Components.Select(c => c.Id).ToList(),
            CreateDesktopShortcut = plan.CreateDesktopShortcut,
        };

        var metadataFile = Path.Combine(plan.InstallPath, _settings.MetadataFileName);
        Directory.CreateDirectory(plan.InstallPath);
        await using var stream = File.Create(metadataFile);
        await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions.Value.Value, token).ConfigureAwait(false);
    }
}
