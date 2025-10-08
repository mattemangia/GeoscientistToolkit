// GeoscientistToolkit/UI/SystemInfoWindow.cs

using System.Diagnostics;
using System.Management;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;
using Veldrid;
// Ensure you have added the System.Management NuGet package to your project.

namespace GeoscientistToolkit.UI;

public class SystemInfoWindow
{
    private (string key, string value)[] _cpuInfo;
    private string _exportMessage = string.Empty;
    private float _exportMessageTimer;
    private string[] _gpuInfo;
    private bool _isOpen;
    private (string key, string value)[] _ramInfo;

    // Store gathered info to avoid re-querying every frame
    private (string key, string value)[] _systemInfo;
    private (string key, string value)[] _veldridInfo;


    /// <summary>
    ///     Opens the System Info window and gathers the required information.
    /// </summary>
    /// <param name="gd">The active GraphicsDevice to query GPU info from.</param>
    public void Open(GraphicsDevice gd)
    {
        _isOpen = true;
        GatherAllInfo(gd);
        _exportMessage = string.Empty;
    }

    /// <summary>
    ///     Renders the System Info window if it is open.
    /// </summary>
    public void Submit()
    {
        if (!_isOpen) return;

        // CORRECTED: Increased default window width significantly
        ImGui.SetNextWindowSize(new Vector2(700, 450), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("System Information", ref _isOpen, ImGuiWindowFlags.NoCollapse))
        {
            RenderSection("System", _systemInfo);
            RenderSection("Processor", _cpuInfo);
            RenderSection("Memory", _ramInfo);
            RenderGpuSection("Graphics", _gpuInfo, _veldridInfo);

            ImGui.Separator();

            if (ImGui.Button("Export to File")) ExportInfoToFile();

            ImGui.SameLine();

            if (ImGui.Button("Close", new Vector2(80, 0))) _isOpen = false;

            if (!string.IsNullOrEmpty(_exportMessage))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 1.0f, 0.0f, 1.0f));
                ImGui.Text(_exportMessage);
                ImGui.PopStyleColor();
                _exportMessageTimer -= ImGui.GetIO().DeltaTime;
                if (_exportMessageTimer <= 0) _exportMessage = string.Empty;
            }

            if (ImGui.IsKeyReleased(ImGuiKey.Escape)) _isOpen = false;

            ImGui.End();
        }
    }

    private void RenderSection(string title, (string key, string value)[] data)
    {
        ImGui.SeparatorText(title);
        if (ImGui.BeginTable($"table_{title}", 2, ImGuiTableFlags.BordersInnerV))
        {
            // CORRECTED: Increased fixed width for the key column
            ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed, 160);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            foreach (var (key, value) in data)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
                ImGui.Text(key);
                ImGui.PopStyleColor();

                ImGui.TableSetColumnIndex(1);
                ImGui.TextWrapped(value);
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
    }

    private void RenderGpuSection(string title, string[] gpus, (string key, string value)[] veldridData)
    {
        ImGui.SeparatorText(title);
        if (ImGui.BeginTable("table_gpu", 2, ImGuiTableFlags.BordersInnerV))
        {
            // CORRECTED: Increased fixed width for the key column
            ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed, 160);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            // List all GPUs
            var i = 1;
            foreach (var gpu in gpus)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
                ImGui.Text($"Detected GPU #{i++}");
                ImGui.PopStyleColor();

                ImGui.TableSetColumnIndex(1);
                ImGui.TextWrapped(gpu);
            }

            // Add Veldrid-specific info
            foreach (var (key, value) in veldridData)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
                ImGui.Text(key);
                ImGui.PopStyleColor();

                ImGui.TableSetColumnIndex(1);
                ImGui.TextWrapped(value);
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
    }

    private void GatherAllInfo(GraphicsDevice gd)
    {
        _systemInfo = new[]
        {
            ("OS", RuntimeInformation.OSDescription),
            ("Framework", RuntimeInformation.FrameworkDescription)
        };

        _cpuInfo = new[]
        {
            ("Processor Name", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? GetCpuNameWindows() : "N/A"),
            ("Architecture", RuntimeInformation.ProcessArchitecture.ToString()),
            ("Logical Cores", Environment.ProcessorCount.ToString())
        };

        _ramInfo = new[]
        {
            ("Total Physical Memory", GetTotalRam())
        };

        _gpuInfo = GetGpuList().ToArray();

        _veldridInfo = new[]
        {
            ("Veldrid Backend", gd.BackendType.ToString()),
            ("Active Device", gd.DeviceName)
        };
    }

    #region Data Gathering Methods

    private string GetCpuNameWindows()
    {
        try
        {
            var searcher = new ManagementObjectSearcher("select Name from Win32_Processor");
            return searcher.Get().Cast<ManagementObject>().FirstOrDefault()?["Name"]?.ToString() ?? "N/A";
        }
        catch
        {
            return "N/A (Requires WMI permissions)";
        }
    }

    private string GetTotalRam()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                var totalRamBytes =
                    Convert.ToUInt64(searcher.Get().Cast<ManagementObject>().FirstOrDefault()?["TotalPhysicalMemory"]);
                return $"{totalRamBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var meminfo = File.ReadAllLines("/proc/meminfo").FirstOrDefault(line => line.StartsWith("MemTotal:"));
                var parts = meminfo?.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts?.Length >= 2 && long.TryParse(parts[1], out var ramKiB))
                    return $"{ramKiB / (1024.0 * 1024.0):F2} GB";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var output = ExecuteCommand("sysctl", "-n hw.memsize");
                if (long.TryParse(output.Trim(), out var ramBytes))
                    return $"{ramBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
        }
        catch
        {
            return "N/A";
        }

        return "N/A";
    }

    private List<string> GetGpuList()
    {
        var gpuList = new List<string>();
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (ManagementObject mo in searcher.Get())
                    gpuList.Add(mo["Name"]?.ToString() ?? "Unknown Video Controller");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var output = ExecuteCommand("lspci", "| grep -i 'vga\\|3d\\|2d'");
                gpuList.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Split(':').Length > 2 ? line.Split(':')[2].Trim() : line.Trim()));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var output = ExecuteCommand("system_profiler", "SPDisplaysDataType");
                gpuList.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.Trim().StartsWith("Chipset Model:"))
                    .Select(line => line.Split(':').Length > 1 ? line.Split(':')[1].Trim() : line.Trim()));
            }
        }
        catch
        {
            /* Fallback */
        }

        if (!gpuList.Any()) gpuList.Add("Could not enumerate GPUs.");
        return gpuList;
    }

    private string ExecuteCommand(string command, string args)
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

    #endregion

    #region Export Logic

    private string BuildStringForExport()
    {
        var sb = new StringBuilder();
        Action<string, (string, string)[]> appendSection = (title, data) =>
        {
            sb.AppendLine($"--- {title} ---");
            foreach (var (key, value) in data) sb.AppendLine($"{key,-25}: {value}");
            sb.AppendLine();
        };

        appendSection("System", _systemInfo);
        appendSection("Processor", _cpuInfo);
        appendSection("Memory", _ramInfo);

        sb.AppendLine("--- Graphics ---");
        var i = 1;
        foreach (var gpu in _gpuInfo) sb.AppendLine($"{"Detected GPU #" + i++,-25}: {gpu}");
        foreach (var (key, value) in _veldridInfo) sb.AppendLine($"{key,-25}: {value}");

        return sb.ToString();
    }

    private void ExportInfoToFile()
    {
        try
        {
            var content = BuildStringForExport();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var filePath = Path.Combine(userProfile, "HWInfo.txt");
            File.WriteAllText(filePath, content);

            _exportMessage = "Exported!";
            _exportMessageTimer = 3.0f;
        }
        catch (Exception)
        {
            _exportMessage = "Export failed!";
            _exportMessageTimer = 3.0f;
        }
    }

    #endregion
}