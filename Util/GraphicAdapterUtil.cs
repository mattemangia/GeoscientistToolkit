// GAIA/Util/GraphicsAdapterUtil.cs

using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace GAIA.Util;

/// <summary>
///     A multiplatform utility to enumerate the names of graphics adapters installed in the system.
/// </summary>
public static class GraphicsAdapterUtil
{
    public static List<string> GetGpuList()
    {
        var gpuList = new List<string>();
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Try WMI approach first
                try
                {
                    gpuList.AddRange(GetWindowsGpusViaWmi());
                }
                catch
                {
                    // If WMI fails, try alternative methods
                    gpuList.AddRange(GetWindowsGpusViaProcess());
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Plain `lspci`, filtered here rather than piped through grep: the output is small,
                // and it avoids quoting a regex through `bash -c`. Matching on the device class is
                // also what keeps hex addresses (e3d00000 contains "3d") from being read as GPUs.
                var output = ExecuteCommand("lspci", string.Empty);
                gpuList.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(IsDisplayController)
                    .Select(ExtractPciDeviceName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var output = ExecuteCommand("system_profiler", "SPDisplaysDataType");
                gpuList.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.Trim().StartsWith("Chipset Model:"))
                    .Select(line => line.Split(':').Length > 1 ? line.Split(':')[1].Trim() : line.Trim()));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Could not enumerate GPUs: {ex.Message}");
        }

        if (!gpuList.Any())
            // If we couldn't find any GPUs, add some common ones
            gpuList.Add("Default GPU");

        return gpuList;
    }

    /// <summary>
    ///     True for an lspci line describing a graphics adapter, e.g.
    ///     "05:00.0 VGA compatible controller: Advanced Micro Devices, Inc. [AMD/ATI] Navi 14".
    /// </summary>
    private static bool IsDisplayController(string line) =>
        line.Contains("VGA compatible controller", StringComparison.OrdinalIgnoreCase)
        || line.Contains("3D controller", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Display controller", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Pulls the device name out of an lspci line, which reads "SLOT CLASS: DEVICE".
    ///     The slot ("05:00.0") has no space after its colons, so the first ": " is the class separator.
    /// </summary>
    private static string ExtractPciDeviceName(string line)
    {
        var separator = line.IndexOf(": ", StringComparison.Ordinal);
        return separator > 0 ? line[(separator + 2)..].Trim() : line.Trim();
    }

    private static List<string> GetWindowsGpusViaWmi()
    {
        var gpuList = new List<string>();

        // Use dynamic loading to avoid compile-time dependency on System.Management
        try
        {
            var assembly = Assembly.Load("System.Management");
            if (assembly != null)
            {
                var searcherType = assembly.GetType("System.Management.ManagementObjectSearcher");
                var searcher = Activator.CreateInstance(searcherType, "SELECT * FROM Win32_VideoController");

                var getMethod = searcherType.GetMethod("Get", Type.EmptyTypes);
                var results = getMethod.Invoke(searcher, null);

                foreach (var mo in (IEnumerable)results)
                {
                    var nameProperty = mo.GetType().GetProperty("Item");
                    var name = nameProperty.GetValue(mo, new object[] { "Name" })?.ToString();
                    if (!string.IsNullOrEmpty(name)) gpuList.Add(name);
                }
            }
        }
        catch
        {
            // WMI not available, will fall back to process method
        }

        return gpuList;
    }

    private static List<string> GetWindowsGpusViaProcess()
    {
        var gpuList = new List<string>();

        try
        {
            // Try using WMIC command line tool
            var output = ExecuteCommand("wmic", "path win32_VideoController get name");
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1) // Skip header
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line));

            gpuList.AddRange(lines);
        }
        catch
        {
            // If WMIC fails, try DirectX diagnostic
            try
            {
                var output = ExecuteCommand("dxdiag", "/t dxdiag_output.txt");
                Thread.Sleep(2000); // Give dxdiag time to write
                if (File.Exists("dxdiag_output.txt"))
                {
                    var content = File.ReadAllText("dxdiag_output.txt");
                    File.Delete("dxdiag_output.txt");

                    // Parse display devices from dxdiag output
                    var displaySections = content.Split(new[] { "Display Devices" }, StringSplitOptions.None);
                    if (displaySections.Length > 1)
                    {
                        var lines = displaySections[1].Split('\n');
                        foreach (var line in lines)
                            if (line.Trim().StartsWith("Card name:"))
                                gpuList.Add(line.Substring(line.IndexOf(':') + 1).Trim());
                    }
                }
            }
            catch
            {
                // All methods failed
            }
        }

        return gpuList;
    }

    private static string ExecuteCommand(string command, string args)
    {
        var process = new Process
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

        // On non-Windows, commands with pipes need to be run through a shell
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && args.Contains('|'))
        {
            process.StartInfo.FileName = "/bin/bash";
            process.StartInfo.Arguments = $"-c \"{command} {args}\"";
        }

        try
        {
            process.Start();
            var result = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return result;
        }
        catch
        {
            return string.Empty;
        }
    }
}