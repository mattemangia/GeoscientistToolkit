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

    public static async Task<InstallerManifest> LoadOrCreateAsync(string path, CancellationToken token = default)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Manifest non trovato in {path}. Creazione manifest di default...");
            var defaultManifest = CreateDefaultManifest();
            await SaveAsync(path, defaultManifest, token).ConfigureAwait(false);
            Console.WriteLine($"Manifest di default creato in {path}");
            Console.WriteLine("Puoi modificare il manifest per aggiungere/rimuovere piattaforme o cambiare le opzioni.");
            return defaultManifest;
        }

        return await LoadAsync(path, token).ConfigureAwait(false);
    }

    private static InstallerManifest CreateDefaultManifest()
    {
        return new InstallerManifest
        {
            Version = "1.0.0",
            Packages = new List<RuntimePackage>
            {
                new RuntimePackage
                {
                    RuntimeIdentifier = "win-x64",
                    SelfContained = true,
                    Description = "Windows 64-bit"
                },
                new RuntimePackage
                {
                    RuntimeIdentifier = "linux-x64",
                    SelfContained = true,
                    Description = "Linux 64-bit"
                },
                new RuntimePackage
                {
                    RuntimeIdentifier = "osx-x64",
                    SelfContained = true,
                    Description = "macOS Intel 64-bit"
                },
                new RuntimePackage
                {
                    RuntimeIdentifier = "osx-arm64",
                    SelfContained = true,
                    Description = "macOS Apple Silicon (ARM64)"
                }
            }
        };
    }

    public static async Task SaveAsync(string path, InstallerManifest manifest, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions.Value.Value, token).ConfigureAwait(false);
    }
}
