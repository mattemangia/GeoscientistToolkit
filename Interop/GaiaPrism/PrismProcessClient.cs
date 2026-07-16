using System.Diagnostics;

namespace GAIA.Interop.GaiaPrism;

public sealed record PrismBridgeProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
}

public static class PrismProcessClient
{
    public static string? Locate(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return Path.GetFullPath(configuredPath);
        var environment = Environment.GetEnvironmentVariable("PRISM_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(environment) && File.Exists(environment)) return Path.GetFullPath(environment);
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "PRISMApp.exe", "Prism.exe" }
            : new[] { "PRISMApp", "Prism", "prism" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var candidate in candidates)
        {
            var path = Path.Combine(directory, candidate);
            if (File.Exists(path)) return path;
        }
        var sibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Prism", "artifacts", "common-bin", OperatingSystem.IsWindows() ? "PRISMApp.exe" : "PRISMApp"));
        return File.Exists(sibling) ? sibling : null;
    }

    public static async Task<PrismBridgeProcessResult> ImportMaterialAsync(string packagePath, bool exploratory,
        string? executable = null, CancellationToken cancellationToken = default)
    {
        executable = Locate(executable) ?? throw new FileNotFoundException("PRISM is not installed or configured. Set PRISM_EXECUTABLE or export the .gpex package for manual transfer.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add("prism-gaia-bridge"); start.ArgumentList.Add("import-material"); start.ArgumentList.Add(Path.GetFullPath(packagePath));
        if (exploratory) start.ArgumentList.Add("--allow-exploratory");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start PRISM.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return new PrismBridgeProcessResult(process.ExitCode, await stdout, await stderr);
    }
}
