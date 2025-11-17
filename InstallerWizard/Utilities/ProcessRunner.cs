using System.Diagnostics;

namespace GeoscientistToolkit.Installer.Utilities;

public static class ProcessRunner
{
    public static async Task<int> RunAsync(string fileName, string arguments, string workingDirectory, Action<string>? onOutput = null, CancellationToken token = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Impossibile avviare {fileName}");

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                onOutput?.Invoke(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                onOutput?.Invoke(args.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.Run(() =>
        {
            while (!process.HasExited)
            {
                if (token.IsCancellationRequested)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                        // ignore
                    }
                    token.ThrowIfCancellationRequested();
                }
                Thread.Sleep(100);
            }
        }, token).ConfigureAwait(false);

        return process.ExitCode;
    }
}
