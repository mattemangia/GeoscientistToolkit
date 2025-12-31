using GeoscientistToolkit.InstallerPackager.Models;

namespace GeoscientistToolkit.InstallerPackager.Services;

internal sealed class InstallerPublishService
{
    public async Task<string> PublishInstallerAsync(
        string runtimeIdentifier,
        PackagerSettings settings,
        PublishService publisher,
        Action<string>? onLog = null)
    {
        var log = onLog ?? Console.WriteLine;
        log($"Publishing installer for {runtimeIdentifier}...");

        var projectPath = ExpandPath(settings.InstallerProjectPath, settings.SettingsDirectory);
        var outputDirectory = ExpandPath(settings.PackagesOutputDirectory, settings.SettingsDirectory);
        Directory.CreateDirectory(outputDirectory);

        var publishPath = await publisher.PublishAsync(
            projectPath,
            runtimeIdentifier,
            selfContained: true,
            settings.PublishConfiguration,
            settings.AdditionalPublishArguments,
            log,
            CancellationToken.None).ConfigureAwait(false);

        var extension = runtimeIdentifier.StartsWith("win", StringComparison.OrdinalIgnoreCase) ? ".exe" : string.Empty;
        var executableName = settings.InstallerExecutableName + extension;
        var source = Path.Combine(publishPath, executableName);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"Installer executable not found at {source}");
        }

        var target = Path.Combine(outputDirectory, $"{settings.InstallerExecutableName}-{runtimeIdentifier}{extension}");
        File.Copy(source, target, true);
        CleanupTemporaryDirectory(publishPath);
        log($"Installer saved to {target}");

        return target;
    }

    private static void CleanupTemporaryDirectory(string publishDirectory)
    {
        var root = Directory.GetParent(publishDirectory)?.FullName ?? publishDirectory;
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string ExpandPath(string path, string baseDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, expanded[2..]);
        }

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var root = string.IsNullOrEmpty(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
        return Path.GetFullPath(Path.Combine(root, expanded));
    }
}
