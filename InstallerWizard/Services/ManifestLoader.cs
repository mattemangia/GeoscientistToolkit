using System.Net.Http;
using System.Text.Json;
using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.Installer.Utilities;

namespace GeoscientistToolkit.Installer.Services;

internal sealed class ManifestLoader
{
    private readonly InstallerSettings _settings;
    private readonly HttpClient _http = new();

    public ManifestLoader(InstallerSettings settings)
    {
        _settings = settings;
    }

    public async Task<InstallerManifest> LoadAsync(string? overrideUrl = null, CancellationToken token = default)
    {
        var path = overrideUrl ?? _settings.ManifestUrl;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Manifest URL non configurato");
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http"))
        {
            using var response = await _http.GetAsync(uri, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var manifest = await JsonSerializer.DeserializeAsync<InstallerManifest>(stream, JsonOptions.Value.Value, token)
                .ConfigureAwait(false);
            return manifest ?? new InstallerManifest();
        }

        var resolvedPath = ResolveLocalPath(path);
        await using var fileStream = File.OpenRead(resolvedPath);
        var localManifest = await JsonSerializer.DeserializeAsync<InstallerManifest>(fileStream, JsonOptions.Value.Value, token)
            .ConfigureAwait(false);
        return localManifest ?? new InstallerManifest();
    }

    private string ResolveLocalPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = Path.Combine(home, expanded[2..]);
        }

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var baseDirectory = string.IsNullOrEmpty(_settings.SettingsDirectory)
            ? AppContext.BaseDirectory
            : _settings.SettingsDirectory;
        return Path.GetFullPath(Path.Combine(baseDirectory, expanded));
    }
}
