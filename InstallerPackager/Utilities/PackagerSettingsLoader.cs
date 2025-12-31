using System.Text.Json;
using GeoscientistToolkit.InstallerPackager.Models;
using GeoscientistToolkit.Installer.Utilities;

namespace GeoscientistToolkit.InstallerPackager.Utilities;

internal static class PackagerSettingsLoader
{
    private const string SettingsFileName = "packager-settings.json";

    public static PackagerSettings Load()
    {
        var settingsPath = ResolveSettingsPath();
        if (!File.Exists(settingsPath))
        {
            var defaults = new PackagerSettings { SettingsDirectory = Path.GetDirectoryName(settingsPath)! };
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            var json = JsonSerializer.Serialize(defaults, JsonOptions.Value.Value);
            File.WriteAllText(settingsPath, json);
            return defaults;
        }

        using var stream = File.OpenRead(settingsPath);
        var settings = JsonSerializer.Deserialize<PackagerSettings>(stream, JsonOptions.Value.Value) ?? new PackagerSettings();
        return settings with { SettingsDirectory = Path.GetDirectoryName(settingsPath)! };
    }

    private static string ResolveSettingsPath()
    {
        var repoPath = Path.Combine(Environment.CurrentDirectory, "InstallerPackager", SettingsFileName);
        if (File.Exists(repoPath))
        {
            return repoPath;
        }

        var fallback = Path.Combine(Environment.CurrentDirectory, SettingsFileName);
        if (File.Exists(fallback))
        {
            return fallback;
        }

        return Path.Combine(AppContext.BaseDirectory, SettingsFileName);
    }
}
