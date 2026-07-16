// GAIA/UI/SystemInfoWindow.cs

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GAIA.Util;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;

// Ensure you have added the System.Management NuGet package to your project.

namespace GAIA.UI;

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


    public void OpenOpenTk()
    {
        _isOpen = true;
        GatherCommonInfo();
        _veldridInfo = new[]
        {
            ("Graphics Backend", "OpenTK / OpenGL"),
            ("OpenGL Renderer", GL.GetString(StringName.Renderer) ?? "Unknown"),
            ("OpenGL Version", GL.GetString(StringName.Version) ?? "Unknown")
        };
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

            // Add active OpenGL backend information.
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

    private void GatherCommonInfo()
    {
        _systemInfo = new[]
        {
            ("OS", RuntimeInformation.OSDescription),
            ("Framework", RuntimeInformation.FrameworkDescription)
        };

        _cpuInfo = new[]
        {
            ("Processor Name", SystemDiagnostics.GetCpuName()),
            ("Architecture", RuntimeInformation.ProcessArchitecture.ToString()),
            ("Logical Cores", Environment.ProcessorCount.ToString())
        };

        _ramInfo = new[]
        {
            ("Total Physical Memory", SystemDiagnostics.GetTotalRam())
        };

        _gpuInfo = GraphicsAdapterUtil.GetGpuList().ToArray();

    }

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
