using System.Text.Json;
using GeoscientistToolkit.Installer.Models;

namespace GeoscientistToolkit.Installer.Utilities;

internal static class InstallerSettingsLoader
{
    private const string SettingsFileName = "installer-settings.json";

    public static InstallerSettings Load()
    {
        var settingsPath = ResolveSettingsPath();
        if (!File.Exists(settingsPath))
        {
            var defaultSettings = InstallerSettings.Default with { SettingsDirectory = Path.GetDirectoryName(settingsPath)! };
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(defaultSettings, JsonOptions.Value.Value));
            return defaultSettings;
        }

        using var stream = File.OpenRead(settingsPath);
        var settings = JsonSerializer.Deserialize<InstallerSettings>(stream, JsonOptions.Value.Value) ?? InstallerSettings.Default;
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
