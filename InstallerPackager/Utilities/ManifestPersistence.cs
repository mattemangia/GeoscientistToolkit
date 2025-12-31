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
            Console.WriteLine($"Manifest not found at {path}. Creating default manifest...");
            var defaultManifest = CreateDefaultManifest();
            await SaveAsync(path, defaultManifest, token).ConfigureAwait(false);
            Console.WriteLine($"Default manifest created at {path}");
            Console.WriteLine("You can edit the manifest to add/remove packages or tweak options.");
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
                    PackageId = "imgui",
                    RuntimeIdentifier = "win-x64",
                    SelfContained = true,
                    Description = "Geoscientist's Toolkit (ImGui)"
                },
                new RuntimePackage
                {
                    PackageId = "imgui",
                    RuntimeIdentifier = "linux-x64",
                    SelfContained = true,
                    Description = "Geoscientist's Toolkit (ImGui)"
                },
                new RuntimePackage
                {
                    PackageId = "imgui",
                    RuntimeIdentifier = "osx-x64",
                    SelfContained = true,
                    Description = "Geoscientist's Toolkit (ImGui)"
                },
                new RuntimePackage
                {
                    PackageId = "imgui",
                    RuntimeIdentifier = "osx-arm64",
                    SelfContained = true,
                    Description = "Geoscientist's Toolkit (ImGui)"
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
