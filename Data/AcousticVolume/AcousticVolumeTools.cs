// GeoscientistToolkit/UI/AcousticVolume/AcousticVolumeTools.cs

using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.AcousticVolume;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;

namespace GeoscientistToolkit.Data.AcousticVolume;

/// <summary>
///     Categorized tool panel for Acoustic Volume datasets.
///     Uses a compact dropdown + tabs navigation to maximize usable space,
///     managing all related sub-tools.
/// </summary>
public class AcousticVolumeTools : IDatasetTools
{
    private readonly Dictionary<ToolCategory, string> _categoryDescriptions;
    private readonly Dictionary<ToolCategory, string> _categoryNames;

    // All tools organized by category
    private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;

    private ToolCategory _selectedCategory = ToolCategory.Analysis; // Default to analysis
    private int _selectedToolIndex;

    public AcousticVolumeTools()
    {
        // Category metadata
        _categoryNames = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Animation, "Animation" },
            { ToolCategory.Analysis, "Analysis" },
            { ToolCategory.Export, "Export" }
        };

        _categoryDescriptions = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Animation, "Control and export time-series animations" },
            { ToolCategory.Analysis, "Quantitative analysis and visualization of wave field data" },
            { ToolCategory.Export, "Export raw wave fields, calculated properties, and metadata" }
        };

        // Initialize tools and add them to their respective categories
        _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
        {
            {
                ToolCategory.Animation,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Animation Controls",
                        Description = "Playback and export settings for time-series data",
                        Tool = new AcousticAnimationTool(),
                        Category = ToolCategory.Animation
                    }
                }
            },
            {
                ToolCategory.Analysis,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Data Analysis",
                        Description = "Calculate statistics, histograms, and frequency spectrums",
                        Tool = new AcousticAnalysisTool(),
                        Category = ToolCategory.Analysis
                    },
                    new()
                    {
                        Name = "Damage Analysis",
                        Description = "Tools for analyzing fracture and damage patterns",
                        Tool = new DamageAnalysisTool(),
                        Category = ToolCategory.Analysis
                    },
                    new()
                    {
                        Name = "Velocity Profile",
                        Description = "Analyze Vp and Vs along a user-defined line from calibrated density data",
                        Tool = new VelocityProfileTool(),
                        Category = ToolCategory.Analysis
                    },
                    new()
                    {
                        Name = "Waveform Viewer",
                        Description = "Extract and view 1D waveforms between two points in time",
                        Tool = new WaveformViewerAdapter(),
                        Category = ToolCategory.Analysis
                    }
                }
            },
            {
                ToolCategory.Export,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Wave Field Export",
                        Description = "Export raw wave field volumes and metadata",
                        Tool = new AcousticExportTool(),
                        Category = ToolCategory.Export
                    },
                    new()
                    {
                        Name = "Properties Export",
                        Description = "Export calculated physical properties and damage data",
                        Tool = new AcousticExportResultsTool(),
                        Category = ToolCategory.Export
                    },
                    new()
                    {
                        Name = "Analysis Report",
                        Description = "Generate a full textual and graphical report of the dataset analysis.",
                        Tool = new AcousticReportGeneratorTool(),
                        Category = ToolCategory.Export
                    }
                }
            }
        };
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not AcousticVolumeDataset)
        {
            ImGui.TextDisabled("These tools are available for Acoustic Volume datasets.");
            return;
        }

        DrawCompactUI(dataset);
    }

    private void DrawCompactUI(Dataset dataset)
    {
        // Compact category selector as dropdown
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4));
        ImGui.Text("Category:");
        ImGui.SameLine();

        var currentCategoryName = _categoryNames[_selectedCategory];
        var categoryTools = _toolsByCategory[_selectedCategory];
        var preview = $"{currentCategoryName} ({categoryTools.Count})";

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##CategorySelector", preview))
        {
            foreach (var category in Enum.GetValues<ToolCategory>())
            {
                var tools = _toolsByCategory[category];
                var isSelected = _selectedCategory == category;
                var label = $"{_categoryNames[category]} ({tools.Count} tools)";

                if (ImGui.Selectable(label, isSelected))
                {
                    _selectedCategory = category;
                    _selectedToolIndex = 0;
                }

                if (ImGui.IsItemHovered()) ImGui.SetTooltip(_categoryDescriptions[category]);
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleVar();

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), _categoryDescriptions[_selectedCategory]);
        ImGui.Separator();
        ImGui.Spacing();

        // Render tools in the selected category as tabs
        if (categoryTools.Count > 0)
        {
            if (ImGui.BeginTabBar($"Tools_{_selectedCategory}", ImGuiTabBarFlags.None))
            {
                for (var i = 0; i < categoryTools.Count; i++)
                {
                    var entry = categoryTools[i];
                    if (ImGui.BeginTabItem(entry.Name))
                    {
                        _selectedToolIndex = i;
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), entry.Description);
                        ImGui.Separator();
                        ImGui.Spacing();

                        ImGui.BeginChild($"ToolContent_{entry.Name}", new Vector2(0, 0), ImGuiChildFlags.None,
                            ImGuiWindowFlags.HorizontalScrollbar);
                        {
                            entry.Tool.Draw(dataset);
                        }
                        ImGui.EndChild();

                        ImGui.EndTabItem();
                    }
                }

                ImGui.EndTabBar();
            }
        }
        else
        {
            ImGui.TextDisabled("No tools available in this category.");
        }
    }

    // Tool categories
    private enum ToolCategory
    {
        Animation,
        Analysis,
        Export
    }

    private class ToolEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public IDatasetTools Tool { get; set; }
        public ToolCategory Category { get; set; }
    }

    /// <summary>
    ///     Adapter to use the WaveformViewer, which creates its own window,
    ///     within the composite tool structure.
    /// </summary>
    private sealed class WaveformViewerAdapter : IDatasetTools
    {
        private AcousticVolumeDataset _lastDataset;
        private WaveformViewer _viewer;

        public void Draw(Dataset dataset)
        {
            if (dataset is not AcousticVolumeDataset avd)
            {
                ImGui.TextDisabled("Requires an Acoustic Volume Dataset.");
                return;
            }

            // Re-create viewer if dataset changes to ensure it has the correct data reference
            if (!ReferenceEquals(_lastDataset, avd))
            {
                _viewer?.Dispose();
                _viewer = new WaveformViewer(avd);
                _lastDataset = avd;
            }

            // Synchronize checkbox state with actual window state
            var isWindowOpen = _viewer?.IsWindowOpen ?? false;

            if (ImGui.Checkbox("Show Waveform Viewer Window", ref isWindowOpen))
            {
                if (_viewer == null)
                {
                    _viewer = new WaveformViewer(avd);
                    _lastDataset = avd;
                }

                _viewer.IsWindowOpen = isWindowOpen;
            }

            ImGui.TextWrapped(
                "This tool opens in a separate window, allowing you to view waveforms while interacting with other tools.");

            // Draw the viewer if the window should be open
            if (_viewer != null && _viewer.IsWindowOpen)
            {
                _viewer.Draw();
                // Update our local state in case the window was closed via the X button
                if (!_viewer.IsWindowOpen)
                {
                    // Window was closed, no need to dispose as it might be reopened
                }
            }
        }
    }
}

#region Refactored Child Tool Classes

/// <summary>
///     Handles Animation controls with fully implemented export and cancellation logic.
/// </summary>
internal class AcousticAnimationTool : IDatasetTools
{
    private readonly ImGuiExportFileDialog _animationExportDialog;
    private readonly ProgressBarDialog _progressDialog;
    private readonly ImGuiExportFileDialog _snapshotExportDialog;

    private int _animationFormat; // 0=PNG, 1=GIF, 2=MP4
    private int _animationFPS = 30;
    private int _currentFrame;
    private bool _isExporting;

    public AcousticAnimationTool()
    {
        _animationExportDialog = new ImGuiExportFileDialog("AnimationExport", "Export Animation");
        _animationExportDialog.SetExtensions((".png", "PNG Sequence"), (".gif", "Animated GIF (Requires ImageMagick)"),
            (".mp4", "MP4 Video (Requires FFmpeg)"));
        _snapshotExportDialog = new ImGuiExportFileDialog("SnapshotExport", "Export Snapshot Frame");
        _snapshotExportDialog.SetExtensions((".png", "PNG Image"));
        _progressDialog = new ProgressBarDialog("Exporting Media");
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not AcousticVolumeDataset ad) return;

        if (ad.TimeSeriesSnapshots == null || ad.TimeSeriesSnapshots.Count == 0)
        {
            ImGui.TextDisabled("No time series data available for animation.");
            return;
        }

        if (_isExporting) ImGui.BeginDisabled();

        ImGui.Text($"Time Series: {ad.TimeSeriesSnapshots.Count} frames");
        var duration = ad.TimeSeriesSnapshots.Last().SimulationTime - ad.TimeSeriesSnapshots.First().SimulationTime;
        ImGui.Text($"Duration: {duration * 1000:F3} ms");

        ImGui.Separator();

        // Snapshot Export
        ImGui.Text("Single Frame Export:");
        ImGui.SliderInt("Frame to Export", ref _currentFrame, 0, ad.TimeSeriesSnapshots.Count - 1);
        if (ImGui.Button("Export Current Frame..."))
            _snapshotExportDialog.Open($"{ad.Name}_frame_{_currentFrame:D4}");

        ImGui.Separator();

        // Animation Export
        ImGui.Text("Animation Export Settings:");
        ImGui.Combo("Format", ref _animationFormat, "PNG Sequence\0GIF (External Tool)\0MP4 (External Tool)\0");
        ImGui.InputInt("FPS", ref _animationFPS);
        _animationFPS = Math.Clamp(_animationFPS, 1, 120);

        if (ImGui.Button("Export Animation..."))
            _animationExportDialog.Open($"{ad.Name}_animation");

        if (_isExporting) ImGui.EndDisabled();

        HandleDialogs(ad);
    }

    private void HandleDialogs(AcousticVolumeDataset ad)
    {
        if (_snapshotExportDialog.Submit())
        {
            _isExporting = true;
            _progressDialog.Open("Exporting Snapshot...");
            Task.Run(
                () => ExportSnapshotAsync(ad, _snapshotExportDialog.SelectedPath, _currentFrame,
                    _progressDialog.CancellationToken), _progressDialog.CancellationToken);
        }

        if (_animationExportDialog.Submit())
        {
            _isExporting = true;
            _progressDialog.Open("Exporting Animation Frames...");
            Task.Run(
                () => ExportAnimationAsync(ad, _animationExportDialog.SelectedPath, _progressDialog.CancellationToken),
                _progressDialog.CancellationToken);
        }

        if (_isExporting) _progressDialog.Submit();
    }

    private async Task ExportSnapshotAsync(AcousticVolumeDataset ad, string path, int frameIndex,
        CancellationToken token)
    {
        try
        {
            await RenderAndSaveFrame(ad, path, frameIndex, token);
            Logger.Log($"[AnimationTool] Successfully exported frame {frameIndex} to {path}");
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("[AnimationTool] Snapshot export was cancelled by the user.");
            // Optionally delete the partially created file if it exists
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AnimationTool] Failed to export snapshot: {ex.Message}");
        }
        finally
        {
            _isExporting = false;
            // The dialog closes itself on cancel or completion, so we don't call Close() here unless needed.
        }
    }

    private async Task ExportAnimationAsync(AcousticVolumeDataset ad, string path, CancellationToken token)
    {
        var frameDir = "";
        try
        {
            var directory = Path.GetDirectoryName(path);
            var baseName = Path.GetFileNameWithoutExtension(path);
            frameDir = Path.Combine(directory, baseName);
            Directory.CreateDirectory(frameDir);

            var frameCount = ad.TimeSeriesSnapshots.Count;
            for (var i = 0; i < frameCount; i++)
            {
                token.ThrowIfCancellationRequested(); // Check for cancellation before each frame

                _progressDialog.Update((float)i / frameCount, $"Rendering frame {i + 1}/{frameCount}...");
                var framePath = Path.Combine(frameDir, $"{baseName}_{i:D4}.png");
                await RenderAndSaveFrame(ad, framePath, i, token);
            }

            Logger.Log($"[AnimationTool] Successfully exported {frameCount} frames to folder: {frameDir}");

            var instructions = GetExternalToolInstructions(frameDir, baseName);
            Logger.Log(instructions);
            _progressDialog.Update(1.0f, "Export complete. See logs for next steps.");
            await Task.Delay(3000, token);
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("[AnimationTool] Animation export was cancelled by the user.");
            // Clean up created directory and its contents
            if (!string.IsNullOrEmpty(frameDir) && Directory.Exists(frameDir)) Directory.Delete(frameDir, true);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AnimationTool] Failed to export animation frames: {ex.Message}");
        }
        finally
        {
            _isExporting = false;
        }
    }

    private async Task RenderAndSaveFrame(AcousticVolumeDataset ad, string path, int frameIndex,
        CancellationToken token)
    {
        var snapshot = ad.TimeSeriesSnapshots[frameIndex];
        var field = snapshot.GetVelocityField(0); // Using X-component for visualization
        if (field == null) return;

        var sliceZ = field.GetLength(2) / 2;
        var width = field.GetLength(0);
        var height = field.GetLength(1);

        var rgbaData = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var slice = new byte[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                slice[y * width + x] = (byte)Math.Clamp((field[x, y, sliceZ] + 1) * 127.5f, 0, 255);

            var rgba = new byte[width * height * 4];
            for (var i = 0; i < slice.Length; i++)
            {
                var color = GetJetColor(slice[i] / 255f);
                rgba[i * 4 + 0] = (byte)(color.X * 255);
                rgba[i * 4 + 1] = (byte)(color.Y * 255);
                rgba[i * 4 + 2] = (byte)(color.Z * 255);
                rgba[i * 4 + 3] = 255;
            }

            return rgba;
        }, token);

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            var writer = new ImageWriter();
            writer.WritePng(rgbaData, width, height, ColorComponents.RedGreenBlueAlpha, stream);
        }
    }

    private string GetExternalToolInstructions(string frameDir, string baseName)
    {
        var framePattern = Path.Combine(frameDir, $"{baseName}_%04d.png");
        switch (_animationFormat)
        {
            case 1: // GIF
                return
                    $"To create a GIF, use ImageMagick:\n`magick convert -delay {100 / _animationFPS} -loop 0 \"{framePattern}\" \"{Path.Combine(Path.GetDirectoryName(frameDir), baseName)}.gif\"`";
            case 2: // MP4
                return
                    $"To create an MP4 video, use FFmpeg:\n`ffmpeg -framerate {_animationFPS} -i \"{framePattern}\" -c:v libx264 -pix_fmt yuv420p \"{Path.Combine(Path.GetDirectoryName(frameDir), baseName)}.mp4\"`";
            default:
                return "PNG sequence exported successfully.";
        }
    }

    private Vector4 GetJetColor(float v)
    {
        v = Math.Clamp(v, 0.0f, 1.0f);
        if (v < 0.125f) return new Vector4(0, 0, 0.5f + 4 * v, 1);
        if (v < 0.375f) return new Vector4(0, 4 * (v - 0.125f), 1, 1);
        if (v < 0.625f) return new Vector4(4 * (v - 0.375f), 1, 1 - 4 * (v - 0.375f), 1);
        if (v < 0.875f) return new Vector4(1, 1 - 4 * (v - 0.625f), 0, 1);
        return new Vector4(1 - 4 * (v - 0.875f), 0, 0, 1);
    }
}

/// <summary>
///     Handles raw data export with full implementation and cancellation.
/// </summary>
internal class AcousticExportTool : IDatasetTools
{
    private readonly ImGuiExportFileDialog _exportDialog;
    private readonly ImGuiExportFileDialog _jsonExportDialog;
    private readonly ProgressBarDialog _progressDialog;
    private bool _exportCombined = true;
    private bool _exportDamage = true;

    private int _exportFormat;
    private bool _exportPWave = true;
    private bool _exportSWave = true;
    private bool _isExporting;

    public AcousticExportTool()
    {
        _exportDialog = new ImGuiExportFileDialog("AcousticWaveFieldExport", "Export Wave Field Data");
        _jsonExportDialog = new ImGuiExportFileDialog("JsonExport", "Export Metadata");
        _jsonExportDialog.SetExtensions((".json", "JSON Metadata File"));
        _progressDialog = new ProgressBarDialog("Exporting Data");
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not AcousticVolumeDataset ad) return;

        if (_isExporting) ImGui.BeginDisabled();

        ImGui.Text("Export Format:");
        ImGui.RadioButton("Binary (*.bin)", ref _exportFormat, 0);
        ImGui.SameLine();
        ImGui.BeginDisabled();
        ImGui.RadioButton("VTK", ref _exportFormat, 1);
        ImGui.SameLine();
        ImGui.RadioButton("CSV", ref _exportFormat, 2);
        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Text("Fields to Export:");
        if (ad.PWaveField != null) ImGui.Checkbox("P-Wave Field", ref _exportPWave);
        if (ad.SWaveField != null) ImGui.Checkbox("S-Wave Field", ref _exportSWave);
        if (ad.CombinedWaveField != null) ImGui.Checkbox("Combined Field", ref _exportCombined);
        if (ad.DamageField != null) ImGui.Checkbox("Damage Field", ref _exportDamage);

        ImGui.Spacing();
        if (ImGui.Button("Export Wave Fields...", new Vector2(-1, 0)))
            _exportDialog.Open($"{ad.Name}_export");

        ImGui.Separator();

        if (ImGui.Button("Export Metadata as JSON", new Vector2(-1, 0)))
            _jsonExportDialog.Open($"{ad.Name}_metadata");

        if (_isExporting) ImGui.EndDisabled();

        HandleDialogs(ad);
    }

    private void HandleDialogs(AcousticVolumeDataset ad)
    {
        if (_exportDialog.Submit())
        {
            _isExporting = true;
            _progressDialog.Open("Exporting Wave Fields...");
            Task.Run(() => ExportWaveFieldsAsync(ad, _exportDialog.SelectedPath, _progressDialog.CancellationToken),
                _progressDialog.CancellationToken);
        }

        if (_jsonExportDialog.Submit()) ExportMetadataAsJson(ad, _jsonExportDialog.SelectedPath);

        if (_isExporting) _progressDialog.Submit();
    }

    private async Task ExportWaveFieldsAsync(AcousticVolumeDataset ad, string basePath, CancellationToken token)
    {
        try
        {
            var dir = Path.GetDirectoryName(basePath);
            var baseName = Path.GetFileNameWithoutExtension(basePath);

            var fieldsToExport = new List<Tuple<ChunkedVolume, string>>();
            if (_exportPWave && ad.PWaveField != null) fieldsToExport.Add(Tuple.Create(ad.PWaveField, "PWaveField"));
            if (_exportSWave && ad.SWaveField != null) fieldsToExport.Add(Tuple.Create(ad.SWaveField, "SWaveField"));
            if (_exportCombined && ad.CombinedWaveField != null)
                fieldsToExport.Add(Tuple.Create(ad.CombinedWaveField, "CombinedField"));
            if (_exportDamage && ad.DamageField != null)
                fieldsToExport.Add(Tuple.Create(ad.DamageField, "DamageField"));

            for (var i = 0; i < fieldsToExport.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var field = fieldsToExport[i];
                _progressDialog.Update((float)i / fieldsToExport.Count, $"Exporting {field.Item2}...");
                var path = Path.Combine(dir, $"{baseName}_{field.Item2}.bin");
                await field.Item1
                    .SaveAsBinAsync(path); // Assuming SaveAsBinAsync supports cancellation internally or is fast
            }

            _progressDialog.Update(1.0f, "Export complete.");
            Logger.Log($"[ExportTool] Successfully exported selected wave fields to {dir}");
            await Task.Delay(2000, token);
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("[ExportTool] Wave field export was cancelled by the user.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ExportTool] Failed to export wave fields: {ex.Message}");
        }
        finally
        {
            _isExporting = false;
        }
    }

    private void ExportMetadataAsJson(AcousticVolumeDataset ad, string path)
    {
        try
        {
            var metadata = new AcousticMetadata
            {
                PWaveVelocity = ad.PWaveVelocity,
                SWaveVelocity = ad.SWaveVelocity,
                VpVsRatio = ad.VpVsRatio,
                TimeSteps = ad.TimeSteps,
                ComputationTimeSeconds = ad.ComputationTime.TotalSeconds,
                YoungsModulusMPa = ad.YoungsModulusMPa,
                PoissonRatio = ad.PoissonRatio,
                ConfiningPressureMPa = ad.ConfiningPressureMPa,
                SourceFrequencyKHz = ad.SourceFrequencyKHz,
                SourceEnergyJ = ad.SourceEnergyJ,
                SourceDatasetPath = ad.SourceDatasetPath,
                SourceMaterialName = ad.SourceMaterialName,
                TensileStrengthMPa = ad.TensileStrengthMPa,
                CohesionMPa = ad.CohesionMPa,
                FailureAngleDeg = ad.FailureAngleDeg,
                MaxDamage = ad.MaxDamage
            };

            var json = JsonSerializer.Serialize(metadata,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            Logger.Log($"[ExportTool] Successfully exported metadata to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ExportTool] Failed to export metadata: {ex.Message}");
        }
    }
}

/// <summary>
///     A tool to analyze Vp/Vs along a user-defined profile.
/// </summary>
internal class VelocityProfileTool : IDatasetTools
{
    private bool _isCalculating;
    private string _statsResult = "No profile selected.";
    private List<float> _vpData;
    private List<float> _vsData;

    public void Draw(Dataset dataset)
    {
        if (dataset is not AcousticVolumeDataset ad)
        {
            ImGui.TextDisabled("This tool requires an Acoustic Volume Dataset.");
            return;
        }

        if (ad.DensityData == null)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Warning: Density data has not been calibrated.");
            ImGui.TextWrapped("Please run the Density Calibration tool before using the profile tool.");
            return;
        }

        // Check for new line data from the viewer on every frame
        if (AcousticInteractionManager.HasNewLine)
        {
            AcousticInteractionManager.HasNewLine = false;
            if (!_isCalculating)
            {
                _isCalculating = true;
                // Run calculation in a background thread to keep UI responsive
                Task.Run(() => CalculateProfile(ad));
            }
        }

        ImGui.Text("Analyze Vp and Vs along a user-defined line.");
        ImGui.Separator();

        if (AcousticInteractionManager.InteractionMode == ViewerInteractionMode.DrawingLine)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Drawing mode active in viewer window...");
            if (ImGui.Button("Cancel Drawing")) AcousticInteractionManager.CancelLineDrawing();
        }
        else
        {
            if (ImGui.Button("Select Profile in Viewer...")) AcousticInteractionManager.StartLineDrawing();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Results:");

        if (_isCalculating)
        {
            ImGui.Text("Calculating...");
        }
        else
        {
            ImGui.TextWrapped(_statsResult);
            if (_vpData != null && _vpData.Count > 0)
            {
                var vpArray = _vpData.ToArray();
                ImGui.PlotLines("P-Wave Velocity (Vp)", ref vpArray[0], vpArray.Length, 0, "Distance ->", _vsData.Min(),
                    _vpData.Max(), new Vector2(0, 120));
            }

            if (_vsData != null && _vsData.Count > 0)
            {
                var vsArray = _vsData.ToArray();
                ImGui.PlotLines("S-Wave Velocity (Vs)", ref vsArray[0], vsArray.Length, 0, "Distance ->", _vsData.Min(),
                    _vpData.Max(), new Vector2(0, 120));
            }
        }
    }

    /// <summary>
    ///     Extracts velocity data along a line defined in the viewer.
    /// </summary>
    private void CalculateProfile(AcousticVolumeDataset dataset)
    {
        var pWaveField = dataset.PWaveField;
        var sWaveField = dataset.SWaveField;

        if (pWaveField == null || sWaveField == null)
        {
            _statsResult = "Wave field data is not available.";
            _isCalculating = false;
            return;
        }

        var (vpData, vsData) = CalculateProfile_Internal(dataset);
        _vpData = vpData;
        _vsData = vsData;

        if (_vpData.Count > 0)
        {
            var avgVp = _vpData.Average();
            var avgVs = _vsData.Average();
            var avgVpVs = avgVs > 0 ? avgVp / avgVs : 0;

            _statsResult = $"SIMULATED Wave Velocities:\n" +
                           $"Points Sampled: {_vpData.Count}\n" +
                           $"Average Vp: {avgVp:F2} m/s\n" +
                           $"Average Vs: {avgVs:F2} m/s\n" +
                           $"Average Vp/Vs Ratio: {avgVpVs:F3}\n\n";

            // Also show theoretical values for comparison
            if (dataset.DensityData != null)
            {
                var (vpTheory, vsTheory) = GetTheoreticalVelocities(dataset.DensityData);
                var avgVpTheory = vpTheory.Average();
                var avgVsTheory = vsTheory.Average();
                var avgVpVsTheory = avgVsTheory > 0 ? avgVpTheory / avgVsTheory : 0;

                _statsResult += $"THEORETICAL (Input) Velocities:\n" +
                                $"Average Vp: {avgVpTheory:F2} m/s\n" +
                                $"Average Vs: {avgVsTheory:F2} m/s\n" +
                                $"Average Vp/Vs Ratio: {avgVpVsTheory:F3}\n\n" +
                                $"Difference: {(avgVpVs - avgVpVsTheory) / avgVpVsTheory * 100:F1}%";
            }
        }
        else
        {
            _statsResult = "No data points found along the selected line.";
        }

        Logger.Log($"[VelocityProfileTool] Extracted {_vpData.Count} data points for velocity profile.");
        _isCalculating = false;
    }

    /// <summary>
    ///     Extracts theoretical velocities from the calibrated density volume along the user-defined line.
    /// </summary>
    private (List<float> vpTheory, List<float> vsTheory) GetTheoreticalVelocities(DensityVolume densityVolume)
    {
        var x1 = (int)AcousticInteractionManager.LineStartPoint.X;
        var y1 = (int)AcousticInteractionManager.LineStartPoint.Y;
        var x2 = (int)AcousticInteractionManager.LineEndPoint.X;
        var y2 = (int)AcousticInteractionManager.LineEndPoint.Y;
        var slice_coord = AcousticInteractionManager.LineSliceIndex;
        var viewIndex = AcousticInteractionManager.LineViewIndex;

        var vpData = new List<float>();
        var vsData = new List<float>();

        // Bresenham's line algorithm
        int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
        int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
        int err = dx + dy, e2;

        while (true)
        {
            int volX, volY, volZ;
            var inBounds = false;

            switch (viewIndex)
            {
                case 0: // XY View
                    volX = x1;
                    volY = y1;
                    volZ = slice_coord;
                    if (volX >= 0 && volX < densityVolume.Width &&
                        volY >= 0 && volY < densityVolume.Height &&
                        volZ >= 0 && volZ < densityVolume.Depth)
                        inBounds = true;
                    break;

                case 1: // XZ View
                    volX = x1;
                    volY = slice_coord;
                    volZ = y1;
                    if (volX >= 0 && volX < densityVolume.Width &&
                        volY >= 0 && volY < densityVolume.Height &&
                        volZ >= 0 && volZ < densityVolume.Depth)
                        inBounds = true;
                    break;

                case 2: // YZ View
                    volX = slice_coord;
                    volY = x1;
                    volZ = y1;
                    if (volX >= 0 && volX < densityVolume.Width &&
                        volY >= 0 && volY < densityVolume.Height &&
                        volZ >= 0 && volZ < densityVolume.Depth)
                        inBounds = true;
                    break;

                default:
                    volX = volY = volZ = 0;
                    break;
            }

            if (inBounds)
            {
                // Read theoretical velocities from calibrated material properties
                var vp = densityVolume.GetPWaveVelocity(volX, volY, volZ);
                var vs = densityVolume.GetSWaveVelocity(volX, volY, volZ);

                vpData.Add(vp);
                vsData.Add(vs);
            }

            if (x1 == x2 && y1 == y2) break;

            e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x1 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y1 += sy;
            }
        }

        return (vpData, vsData);
    }

    public (List<float> vpData, List<float> vsData) CalculateProfile_Internal(DensityVolume densityVolume)
    {
        // Get coordinates from the interaction manager
        var x1 = (int)AcousticInteractionManager.LineStartPoint.X;
        var y1 = (int)AcousticInteractionManager.LineStartPoint.Y;
        var x2 = (int)AcousticInteractionManager.LineEndPoint.X;
        var y2 = (int)AcousticInteractionManager.LineEndPoint.Y;
        var slice_coord = AcousticInteractionManager.LineSliceIndex;
        var viewIndex = AcousticInteractionManager.LineViewIndex;

        var vpData = new List<float>();
        var vsData = new List<float>();

        // Bresenham's line algorithm to iterate over pixels
        int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
        int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
        int err = dx + dy, e2;

        while (true)
        {
            // Convert 2D view coordinates to 3D volume coordinates
            int volX, volY, volZ;
            var inBounds = false;
            switch (viewIndex)
            {
                case 0: // XY View
                    volX = x1;
                    volY = y1;
                    volZ = slice_coord;
                    if (volX >= 0 && volX < densityVolume.Width && volY >= 0 && volY < densityVolume.Height &&
                        volZ >= 0 && volZ < densityVolume.Depth)
                        inBounds = true;
                    break;
                case 1: // XZ View
                    volX = x1;
                    volY = slice_coord;
                    volZ = y1;
                    if (volX >= 0 && volX < densityVolume.Width && volY >= 0 && volY < densityVolume.Height &&
                        volZ >= 0 && volZ < densityVolume.Depth)
                        inBounds = true;
                    break;
                case 2: // YZ View
                    volX = slice_coord;
                    volY = x1;
                    volZ = y1;
                    if (volX >= 0 && volX < densityVolume.Width && volY >= 0 && volY < densityVolume.Height &&
                        volZ >= 0 && volZ < densityVolume.Depth)
                        inBounds = true;
                    break;
                default:
                    volX = volY = volZ = 0;
                    break;
            }

            if (inBounds)
            {
                vpData.Add(densityVolume.GetPWaveVelocity(volX, volY, volZ));
                vsData.Add(densityVolume.GetSWaveVelocity(volX, volY, volZ));
            }

            if (x1 == x2 && y1 == y2) break;
            e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x1 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y1 += sy;
            }
        }

        return (vpData, vsData);
    }

    public (List<float> vpData, List<float> vsData) CalculateProfile_Internal(AcousticVolumeDataset dataset)
    {
        // Get SIMULATED wave fields
        var pWaveField = dataset.PWaveField;
        var sWaveField = dataset.SWaveField;

        // Get scaling factors to convert bytes back to m/s
        var vpScale = dataset.PWaveFieldMaxVelocity;
        var vsScale = dataset.SWaveFieldMaxVelocity;

        if (vpScale <= 0) vpScale = 1.0f; // Fallback for old datasets
        if (vsScale <= 0) vsScale = 1.0f;

        var x1 = (int)AcousticInteractionManager.LineStartPoint.X;
        var y1 = (int)AcousticInteractionManager.LineStartPoint.Y;
        var x2 = (int)AcousticInteractionManager.LineEndPoint.X;
        var y2 = (int)AcousticInteractionManager.LineEndPoint.Y;
        var slice_coord = AcousticInteractionManager.LineSliceIndex;
        var viewIndex = AcousticInteractionManager.LineViewIndex;

        var vpData = new List<float>();
        var vsData = new List<float>();

        // Bresenham's line algorithm
        int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
        int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
        int err = dx + dy, e2;

        while (true)
        {
            int volX, volY, volZ;
            var inBounds = false;
            switch (viewIndex)
            {
                case 0: // XY View
                    volX = x1;
                    volY = y1;
                    volZ = slice_coord;
                    if (volX >= 0 && volX < pWaveField.Width && volY >= 0 && volY < pWaveField.Height &&
                        volZ >= 0 && volZ < pWaveField.Depth)
                        inBounds = true;
                    break;
                case 1: // XZ View
                    volX = x1;
                    volY = slice_coord;
                    volZ = y1;
                    if (volX >= 0 && volX < pWaveField.Width && volY >= 0 && volY < pWaveField.Height &&
                        volZ >= 0 && volZ < pWaveField.Depth)
                        inBounds = true;
                    break;
                case 2: // YZ View
                    volX = slice_coord;
                    volY = x1;
                    volZ = y1;
                    if (volX >= 0 && volX < pWaveField.Width && volY >= 0 && volY < pWaveField.Height &&
                        volZ >= 0 && volZ < pWaveField.Depth)
                        inBounds = true;
                    break;
                default:
                    volX = volY = volZ = 0;
                    break;
            }

            if (inBounds)
            {
                // ✅ Read from SIMULATED wave fields and denormalize
                var vpByte = pWaveField[volX, volY, volZ];
                var vsByte = sWaveField[volX, volY, volZ];

                // Convert byte (0-255) back to physical velocity (m/s)
                var vp = vpByte / 255f * vpScale;
                var vs = vsByte / 255f * vsScale;

                vpData.Add(vp);
                vsData.Add(vs);
            }

            if (x1 == x2 && y1 == y2) break;
            e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x1 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y1 += sy;
            }
        }

        return (vpData, vsData);
    }
}

#endregion