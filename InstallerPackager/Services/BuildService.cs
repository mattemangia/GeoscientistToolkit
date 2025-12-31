using System.IO.Compression;
using System.Security.Cryptography;
using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.InstallerPackager.Models;

namespace GeoscientistToolkit.InstallerPackager.Services;

internal sealed class BuildService
{
    public async Task BuildPackageAsync(
        RuntimePackage package,
        PackagerSettings settings,
        PublishService publisher,
        Action<string>? onLog = null)
    {
        var log = onLog ?? Console.WriteLine;
        log($"Preparing package {package.PackageId} ({package.RuntimeIdentifier})...");

        var tempRoot = Path.Combine(Path.GetTempPath(), "gstk-package-" + Guid.NewGuid());
        Directory.CreateDirectory(tempRoot);
        var payloadRoot = Path.Combine(tempRoot, "payload");
        Directory.CreateDirectory(payloadRoot);

        try
        {
            var outputDirectory = ExpandPath(settings.PackagesOutputDirectory, settings.SettingsDirectory);
            var packageDefinition = ResolvePackageDefinition(package, settings);

            var additionalArgs = package.PackageId.StartsWith("node", StringComparison.OrdinalIgnoreCase)
                ? settings.NodeAdditionalPublishArguments
                : settings.AdditionalPublishArguments;

            var publishPath = await publisher.PublishAsync(
                packageDefinition.ProjectPath,
                package.RuntimeIdentifier,
                package.SelfContained,
                settings.PublishConfiguration,
                additionalArgs,
                log,
                CancellationToken.None).ConfigureAwait(false);

            var appTarget = Path.Combine(payloadRoot, packageDefinition.PayloadFolder);
            CopyDirectory(publishPath, appTarget);
            CleanupTemporaryDirectory(publishPath);
            CreateOnnxInstallerPackage(settings, payloadRoot);

            var archiveName = $"{settings.PackageName}-{package.PackageId}-{package.RuntimeIdentifier}.zip";
            Directory.CreateDirectory(outputDirectory);
            var archivePath = Path.Combine(outputDirectory, archiveName);
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            log("Creating archive...");
            ZipFile.CreateFromDirectory(payloadRoot, archivePath, CompressionLevel.SmallestSize, includeBaseDirectory: false);
            package.Transport = PackageTransport.Archive;
            package.PackageUrl = CombineUrl(settings.PackageBaseUrl, archiveName);
            package.SizeBytes = new FileInfo(archivePath).Length;
            package.Sha256 = ComputeSha256(archivePath);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void CreateOnnxInstallerPackage(PackagerSettings settings, string payloadRoot)
    {
        var templateSource = ExpandPath(settings.OnnxInstallerTemplatePath, settings.SettingsDirectory);
        var installerTarget = Path.Combine(payloadRoot, "onnx-installer");
        Directory.CreateDirectory(installerTarget);

        if (Directory.Exists(templateSource))
        {
            CopyDirectory(templateSource, installerTarget);
        }

        var modelsSource = ExpandPath(settings.OnnxDirectory, settings.SettingsDirectory);
        if (Directory.Exists(modelsSource))
        {
            var modelsTarget = Path.Combine(installerTarget, "models");
            CopyDirectory(modelsSource, modelsTarget);
        }
    }

    private static PackageDefinition ResolvePackageDefinition(RuntimePackage package, PackagerSettings settings)
    {
        return package.PackageId.ToLowerInvariant() switch
        {
            "imgui" => new PackageDefinition(
                ExpandPath(settings.ImGuiProjectPath, settings.SettingsDirectory),
                settings.ImGuiExecutableName,
                "app-imgui"),
            "gtk" => new PackageDefinition(
                ExpandPath(settings.GtkProjectPath, settings.SettingsDirectory),
                settings.GtkExecutableName,
                "app-gtk"),
            "node-server" => new PackageDefinition(
                ExpandPath(settings.NodeServerProjectPath, settings.SettingsDirectory),
                settings.NodeServerExecutableName,
                "node-server"),
            "node-endpoint" => new PackageDefinition(
                ExpandPath(settings.NodeEndpointProjectPath, settings.SettingsDirectory),
                settings.NodeEndpointExecutableName,
                "node-endpoint"),
            _ => throw new InvalidOperationException($"Unknown package id '{package.PackageId}'.")
        };
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Directory sorgente non trovata: {source}");
        }

        Directory.CreateDirectory(destination);

        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            var targetDir = Path.Combine(destination, relative);
            Directory.CreateDirectory(targetDir);
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, true);
        }
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

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
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

    private static string CombineUrl(string baseUrl, string fileName)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return fileName;
        }

        return baseUrl.TrimEnd('/') + "/" + fileName;
    }

    private sealed record PackageDefinition(string ProjectPath, string ExecutableName, string PayloadFolder);
}
