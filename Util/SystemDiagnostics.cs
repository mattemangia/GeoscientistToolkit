// GAIA/Util/SystemDiagnostics.cs

using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace GAIA.Util;

/// <summary>
///     Cross-platform host machine facts, gathered once and cached.
///     Probing the CPU/RAM/GPU shells out to WMI or external tools on some platforms, so callers
///     that need this during startup should kick off <see cref="BeginGather" /> early and read
///     <see cref="Snapshot" />, which never blocks.
/// </summary>
public static class SystemDiagnostics
{
    private static Task<IReadOnlyList<(string Key, string Value)>> _gather;
    private static readonly object _gate = new();

    /// <summary>
    ///     Starts collecting in the background. Safe to call more than once; only the first call gathers.
    /// </summary>
    public static void BeginGather()
    {
        lock (_gate)
        {
            _gather ??= Task.Run(Collect);
        }
    }

    /// <summary>
    ///     The gathered facts, or an empty list while collection is still running.
    ///     Never blocks, so a slow WMI/lspci probe cannot stall the render loop.
    /// </summary>
    public static IReadOnlyList<(string Key, string Value)> Snapshot
    {
        get
        {
            var task = _gather;
            return task is { IsCompletedSuccessfully: true }
                ? task.Result
                : Array.Empty<(string, string)>();
        }
    }

    private static IReadOnlyList<(string Key, string Value)> Collect()
    {
        var facts = new List<(string, string)>
        {
            ("CPU", GetCpuName()),
            ("Cores", $"{Environment.ProcessorCount} logical"),
            ("Memory", GetTotalRam()),
            ("GPU", GetPrimaryGpu()),
            ("OS", RuntimeInformation.OSDescription.Trim()),
            ("Runtime", RuntimeInformation.FrameworkDescription)
        };

        return facts;
    }

    /// <summary>
    ///     Processor model name. Each platform exposes it somewhere different.
    /// </summary>
    public static string GetCpuName()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var searcher = new ManagementObjectSearcher("select Name from Win32_Processor");
                var name = searcher.Get().Cast<ManagementObject>().FirstOrDefault()?["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var line = File.ReadLines("/proc/cpuinfo")
                    .FirstOrDefault(l => l.StartsWith("model name", StringComparison.OrdinalIgnoreCase));
                var name = line?.Split(':', 2).ElementAtOrDefault(1)?.Trim();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var name = ExecuteCommand("sysctl", "-n machdep.cpu.brand_string").Trim();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] CPU name lookup failed: {ex.Message}");
        }

        // Architecture is always known, so degrade to that rather than showing nothing.
        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    /// <summary>
    ///     Total installed physical memory, formatted for display.
    /// </summary>
    public static string GetTotalRam()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var searcher =
                    new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                var raw = searcher.Get().Cast<ManagementObject>().FirstOrDefault()?["TotalPhysicalMemory"];
                if (raw != null) return FormatGigabytes(Convert.ToUInt64(raw));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var line = File.ReadLines("/proc/meminfo")
                    .FirstOrDefault(l => l.StartsWith("MemTotal:", StringComparison.Ordinal));
                var parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts?.Length >= 2 && ulong.TryParse(parts[1], out var kib))
                    return FormatGigabytes(kib * 1024);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (ulong.TryParse(ExecuteCommand("sysctl", "-n hw.memsize").Trim(), out var bytes))
                    return FormatGigabytes(bytes);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] RAM lookup failed: {ex.Message}");
        }

        return "N/A";
    }

    private static string GetPrimaryGpu()
    {
        try
        {
            var gpus = GraphicsAdapterUtil.GetGpuList();
            if (gpus.Count == 0) return "N/A";

            // Extra adapters are listed in Help > System Info; the splash only has room for one line.
            return gpus.Count > 1 ? $"{gpus[0]} (+{gpus.Count - 1} more)" : gpus[0];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] GPU lookup failed: {ex.Message}");
            return "N/A";
        }
    }

    private static string FormatGigabytes(ulong bytes) => $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";

    private static string ExecuteCommand(string command, string args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var result = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return result;
        }
        catch
        {
            return string.Empty;
        }
    }
}
