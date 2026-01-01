using GeoscientistToolkit.Installer.Utilities;

namespace GeoscientistToolkit.InstallerPackager.Services;

internal sealed class PublishService
{
    public async Task<string> PublishAsync(
        string projectPath,
        string runtimeIdentifier,
        bool selfContained,
        string configuration,
        string? additionalArgs,
        Action<string>? log,
        CancellationToken token)
    {
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"Project not found: {projectPath}");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "gstk-packager-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDirectory);
        var publishDirectory = Path.Combine(tempDirectory, "publish");

        var args = new List<string>
        {
            "publish",
            $"\"{projectPath}\"",
            "-c", configuration,
            "-r", runtimeIdentifier,
            "-o", $"\"{publishDirectory}\"",
            "--nologo",
            "--self-contained", selfContained ? "true" : "false",
            "/p:PublishSingleFile=true",
            "/p:IncludeNativeLibrariesForSelfExtract=true",
            "/p:IncludeAllContentForSelfExtract=true",
            "/p:SkipVerificationTests=true"
        };

        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args.Add(additionalArgs);
        }

        var exitCode = await ProcessRunner.RunAsync("dotnet", string.Join(' ', args), Directory.GetCurrentDirectory(), log, token)
            .ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"dotnet publish for {projectPath} exited with code {exitCode}");
        }

        return publishDirectory;
    }
}
