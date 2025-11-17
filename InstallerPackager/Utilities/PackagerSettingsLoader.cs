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
        var baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        if (File.Exists(baseDirectoryPath))
        {
            return baseDirectoryPath;
        }

        var fallback = Path.Combine(Environment.CurrentDirectory, SettingsFileName);
        return File.Exists(fallback) ? fallback : baseDirectoryPath;
    }
}
