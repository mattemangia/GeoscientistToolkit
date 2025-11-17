using System.Text.Json;
using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.Installer.Utilities;

namespace GeoscientistToolkit.InstallerPackager.Utilities;

internal static class ManifestPersistence
{
    public static async Task<InstallerManifest> LoadAsync(string path, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<InstallerManifest>(stream, JsonOptions.Value.Value, token).ConfigureAwait(false);
        return manifest ?? new InstallerManifest();
    }

    public static async Task SaveAsync(string path, InstallerManifest manifest, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions.Value.Value, token).ConfigureAwait(false);
    }
}
