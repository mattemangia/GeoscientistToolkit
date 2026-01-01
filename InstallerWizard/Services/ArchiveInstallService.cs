using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using GeoscientistToolkit.Installer.Models;

namespace GeoscientistToolkit.Installer.Services;

internal sealed class ArchiveInstallService
{
    private readonly HttpClient _httpClient = new();

    public async Task<string> DownloadAndExtractAsync(RuntimePackage package, Action<string>? onOutput = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(package.PackageUrl))
        {
            throw new InvalidOperationException("PackageUrl is not configured for the archive package.");
        }

        var tempFile = Path.Combine(Path.GetTempPath(), "gstk-archive-" + Guid.NewGuid() + ".zip");
        await DownloadAsync(package.PackageUrl, tempFile, onOutput, token).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(package.Sha256))
        {
            ValidateHash(tempFile, package.Sha256);
        }

        var extractDirectory = Path.Combine(Path.GetTempPath(), "gstk-archive-" + Guid.NewGuid());
        ZipFile.ExtractToDirectory(tempFile, extractDirectory, true);
        File.Delete(tempFile);
        return extractDirectory;
    }

    private async Task DownloadAsync(string urlOrPath, string destination, Action<string>? onOutput, CancellationToken token)
    {
        if (Uri.TryCreate(urlOrPath, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            onOutput?.Invoke($"Downloading {urlOrPath}...");
            await using var stream = await _httpClient.GetStreamAsync(uri, token).ConfigureAwait(false);
            await using var file = File.Create(destination);
            await stream.CopyToAsync(file, token).ConfigureAwait(false);
            return;
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(urlOrPath);
        if (expandedPath.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expandedPath = Path.Combine(home, expandedPath[2..]);
        }

        if (!Path.IsPathRooted(expandedPath))
        {
             expandedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, expandedPath));
        }

        if (!File.Exists(expandedPath))
        {
            throw new FileNotFoundException($"Local archive not found: {expandedPath}");
        }

        File.Copy(expandedPath, destination, true);
    }

    private static void ValidateHash(string filePath, string expectedHash)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        var normalized = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(normalized, expectedHash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Package SHA256 verification failed.");
        }
    }
}
