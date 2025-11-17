using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.InstallerPackager.Models;
using GeoscientistToolkit.InstallerPackager.Services;
using GeoscientistToolkit.InstallerPackager.Utilities;

namespace GeoscientistToolkit.InstallerPackager;

internal sealed class PackagerApp
{
    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            var settings = PackagerSettingsLoader.Load();
            var manifest = await ManifestPersistence.LoadOrCreateAsync(settings.ManifestPath).ConfigureAwait(false);
            var publisher = new PublishService();
            foreach (var package in manifest.Packages)
            {
                await BuildPackageAsync(package, settings, publisher).ConfigureAwait(false);
            }

            await ManifestPersistence.SaveAsync(settings.ManifestPath, manifest).ConfigureAwait(false);
            Console.WriteLine("Pacchetti generati correttamente.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Errore durante il packaging: {ex.Message}\n{ex}");
            return 1;
        }
    }

    private async Task BuildPackageAsync(RuntimePackage package, PackagerSettings settings, PublishService publisher)
    {
        Console.WriteLine($"Preparazione pacchetto {package.RuntimeIdentifier}...");
        var tempRoot = Path.Combine(Path.GetTempPath(), "gstk-package-" + Guid.NewGuid());
        Directory.CreateDirectory(tempRoot);
        var payloadRoot = Path.Combine(tempRoot, "payload");
        Directory.CreateDirectory(payloadRoot);

        try
        {
            var projectPath = ExpandPath(settings.ProjectPath, settings.SettingsDirectory);
            var nodeProjectPath = ExpandPath(settings.NodeProjectPath, settings.SettingsDirectory);
            var outputDirectory = ExpandPath(settings.PackagesOutputDirectory, settings.SettingsDirectory);

            var mainPublish = await publisher.PublishAsync(
                projectPath,
                package.RuntimeIdentifier,
                package.SelfContained,
                settings.PublishConfiguration,
                settings.AdditionalPublishArguments,
                Console.WriteLine,
                CancellationToken.None).ConfigureAwait(false);

            var appTarget = Path.Combine(payloadRoot, "app");
            CopyDirectory(mainPublish, appTarget);
            CleanupTemporaryDirectory(mainPublish);
            CopyOnnx(settings, appTarget);

            var nodeTarget = Path.Combine(payloadRoot, "node-endpoint");
            if (File.Exists(nodeProjectPath))
            {
                var nodePublish = await publisher.PublishAsync(
                    nodeProjectPath,
                    package.RuntimeIdentifier,
                    package.SelfContained,
                    settings.PublishConfiguration,
                    settings.NodeAdditionalPublishArguments,
                    Console.WriteLine,
                    CancellationToken.None).ConfigureAwait(false);
                CopyDirectory(nodePublish, nodeTarget);
                CleanupTemporaryDirectory(nodePublish);
                CreateNodeLaunchers(nodeTarget, settings.NodeExecutableName);
            }

            var archiveName = $"{settings.PackageName}-{package.RuntimeIdentifier}.zip";
            Directory.CreateDirectory(outputDirectory);
            var archivePath = Path.Combine(outputDirectory, archiveName);
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

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

    private static void CopyOnnx(PackagerSettings settings, string appTarget)
    {
        var source = ExpandPath(settings.OnnxDirectory, settings.SettingsDirectory);
        if (Directory.Exists(source))
        {
            var destination = Path.Combine(appTarget, "ONNX");
            CopyDirectory(source, destination);
        }
    }

    private static void CreateNodeLaunchers(string nodeTarget, string executableName)
    {
        if (!Directory.Exists(nodeTarget))
        {
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(executableName);
        var windowsExecutable = Path.Combine(nodeTarget, baseName + ".exe");
        var unixExecutable = Path.Combine(nodeTarget, baseName);

        if (File.Exists(windowsExecutable))
        {
            var windowsScript = Path.Combine(nodeTarget, "start-endpoint.cmd");
            File.WriteAllText(windowsScript, $"@echo off\r\n%~dp0{baseName}.exe %*\r\n");
        }

        if (File.Exists(unixExecutable))
        {
            var linuxScript = Path.Combine(nodeTarget, "start-endpoint.sh");
            File.WriteAllText(linuxScript, $"#!/bin/sh\nDIR=\"$(cd \"$(dirname \"$0\")\" && pwd)\"\n\"$DIR/{baseName}\" \"$@\"\n");
            MakeExecutable(linuxScript);

            var macScript = Path.Combine(nodeTarget, "start-endpoint.command");
            File.WriteAllText(macScript, $"#!/bin/bash\n\"$(dirname \"$0\")/{baseName}\" \"$@\"\n");
            MakeExecutable(macScript);
        }
    }

    private static void MakeExecutable(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();
        }
        catch
        {
            // ignore
        }
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
}
