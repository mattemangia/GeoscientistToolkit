// GeoscientistToolkit/UI/AcousticVolume/AcousticVolumeTools.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.AcousticVolume;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace GeoscientistToolkit.Data.AcousticVolume
{
    /// <summary>
    /// Categorized tool panel for Acoustic Volume datasets.
    /// Uses a compact dropdown + tabs navigation to maximize usable space,
    /// managing all related sub-tools.
    /// </summary>
    public class AcousticVolumeTools : IDatasetTools
    {
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

        private ToolCategory _selectedCategory = ToolCategory.Analysis; // Default to analysis
        private int _selectedToolIndex = 0;

        // All tools organized by category
        private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;
        private readonly Dictionary<ToolCategory, string> _categoryNames;
        private readonly Dictionary<ToolCategory, string> _categoryDescriptions;

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
                        new ToolEntry
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
                        new ToolEntry
                        {
                            Name = "Data Analysis",
                            Description = "Calculate statistics, histograms, and frequency spectrums",
                            Tool = new AcousticAnalysisTool(),
                            Category = ToolCategory.Analysis
                        },
                        new ToolEntry
                        {
                            Name = "Damage Analysis",
                            Description = "Tools for analyzing fracture and damage patterns",
                            Tool = new DamageAnalysisTool(),
                            Category = ToolCategory.Analysis
                        },
                        new ToolEntry
                        {
                            Name = "Velocity Profile",
                            Description = "Analyze Vp and Vs along a user-defined line from calibrated density data",
                            Tool = new VelocityProfileTool(),
                            Category = ToolCategory.Analysis
                        },
                         new ToolEntry
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
                        new ToolEntry
                        {
                            Name = "Wave Field Export",
                            Description = "Export raw wave field volumes and metadata",
                            Tool = new AcousticExportTool(),
                            Category = ToolCategory.Export
                        },
                        new ToolEntry
                        {
                            Name = "Properties Export",
                            Description = "Export calculated physical properties and damage data",
                            Tool = new AcousticExportResultsTool(),
                            Category = ToolCategory.Export
                        },
                        // --- NEW TOOL ADDED HERE ---
                        new ToolEntry
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

            string currentCategoryName = _categoryNames[_selectedCategory];
            var categoryTools = _toolsByCategory[_selectedCategory];
            string preview = $"{currentCategoryName} ({categoryTools.Count})";

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.BeginCombo("##CategorySelector", preview))
            {
                foreach (var category in Enum.GetValues<ToolCategory>())
                {
                    var tools = _toolsByCategory[category];
                    bool isSelected = _selectedCategory == category;
                    string label = $"{_categoryNames[category]} ({tools.Count} tools)";

                    if (ImGui.Selectable(label, isSelected))
                    {
                        _selectedCategory = category;
                        _selectedToolIndex = 0;
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(_categoryDescriptions[category]);
                    }
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
                    for (int i = 0; i < categoryTools.Count; i++)
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
        
        /// <summary>
        /// Adapter to use the WaveformViewer, which creates its own window,
        /// within the composite tool structure.
        /// </summary>
        private sealed class WaveformViewerAdapter : IDatasetTools
        {
            private WaveformViewer _viewer;
            private AcousticVolumeDataset _lastDataset;
            private bool _isWindowOpen = false;

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
                    _isWindowOpen = false; // Close window for old dataset
                }
                
                if (ImGui.Checkbox("Show Waveform Viewer Window", ref _isWindowOpen))
                {
                    if (_viewer == null)
                    {
                         _viewer = new WaveformViewer(avd);
                         _lastDataset = avd;
                    }
                }
                
                ImGui.TextWrapped("This tool opens in a separate window, allowing you to view waveforms while interacting with other tools.");
                
                // If the window is open, call its Draw method.
                // The WaveformViewer's Draw method contains its own ImGui.Begin/End calls.
                if (_isWindowOpen)
                {
                    _viewer.Draw();
                }
            }
        }
    }
    
    #region Refactored Child Tool Classes
    
    /// <summary>
    /// Handles Animation controls, extracted from the old monolithic AcousticVolumeTools.
    /// </summary>
    internal class AcousticAnimationTool : IDatasetTools
    {
        private readonly ImGuiExportFileDialog _animationExportDialog;
        private readonly ImGuiExportFileDialog _snapshotExportDialog;
        private int _animationFormat = 0; // 0=PNG, 1=GIF, 2=MP4
        private int _animationFPS = 30;
        private int _animationQuality = 80;
        private bool _includeColorBar = true;

        public AcousticAnimationTool()
        {
            _animationExportDialog = new ImGuiExportFileDialog("AnimationExport", "Export Animation");
            _animationExportDialog.SetExtensions((".png", "PNG Sequence"),(".gif", "Animated GIF"),(".mp4", "MP4 Video"));
            _snapshotExportDialog = new ImGuiExportFileDialog("SnapshotExport", "Export Snapshot");
            _snapshotExportDialog.SetExtensions((".png", "PNG Image"),(".jpg", "JPEG Image"),(".bmp", "Bitmap Image"));
        }
        
        public void Draw(Dataset dataset)
        {
            if (dataset is not AcousticVolumeDataset ad) return;

            if (ad.TimeSeriesSnapshots == null || ad.TimeSeriesSnapshots.Count == 0)
            {
                ImGui.TextDisabled("No time series data available for animation.");
                return;
            }

            ImGui.Text($"Time Series: {ad.TimeSeriesSnapshots.Count} frames");
            float duration = ad.TimeSeriesSnapshots.Last().SimulationTime - ad.TimeSeriesSnapshots.First().SimulationTime;
            ImGui.Text($"Duration: {duration * 1000:F3} ms");

            if (ImGui.Button("Export Animation...")) _animationExportDialog.Open($"{ad.Name}_animation");
            ImGui.SameLine();
            if (ImGui.Button("Export Current Frame...")) _snapshotExportDialog.Open($"{ad.Name}_frame");

            ImGui.Separator();
            ImGui.Text("Animation Export Settings:");
            ImGui.Combo("Format", ref _animationFormat, "Image Sequence\0Animated GIF\0MP4 Video\0");
            ImGui.InputInt("FPS", ref _animationFPS);
            _animationFPS = Math.Clamp(_animationFPS, 1, 120);
            ImGui.SliderInt("Quality", ref _animationQuality, 1, 100);
            ImGui.Checkbox("Include Color Bar", ref _includeColorBar);

            // Handle dialog submissions
            if (_animationExportDialog.Submit()) Logger.Log($"[AnimationTool] Exporting animation to {_animationExportDialog.SelectedPath}. (Implementation pending)");
            if (_snapshotExportDialog.Submit()) Logger.Log($"[AnimationTool] Exporting frame to {_snapshotExportDialog.SelectedPath}. (Implementation pending)");
        }
    }

    /// <summary>
    /// Handles raw data export, extracted from the old monolithic AcousticVolumeTools.
    /// </summary>
    internal class AcousticExportTool : IDatasetTools
    {
        private readonly ImGuiExportFileDialog _exportDialog;
        private int _exportFormat = 0;
        private bool _exportPWave = true;
        private bool _exportSWave = true;
        private bool _exportCombined = true;
        private bool _exportDamage = true;

        public AcousticExportTool()
        {
            _exportDialog = new ImGuiExportFileDialog("AcousticWaveFieldExport", "Export Wave Field Data");
            _exportDialog.SetExtensions((".bin", "Binary Format"), (".vtk", "VTK Format (Not Implemented)"), (".csv", "CSV Format (Not Implemented)"));
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not AcousticVolumeDataset ad) return;

            ImGui.Text("Export Format:");
            ImGui.RadioButton("Binary", ref _exportFormat, 0); ImGui.SameLine();
            ImGui.BeginDisabled();
            ImGui.RadioButton("VTK", ref _exportFormat, 1); ImGui.SameLine();
            ImGui.RadioButton("CSV", ref _exportFormat, 2);
            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.Text("Fields to Export:");
            if (ad.PWaveField != null) ImGui.Checkbox("P-Wave Field", ref _exportPWave);
            if (ad.SWaveField != null) ImGui.Checkbox("S-Wave Field", ref _exportSWave);
            if (ad.CombinedWaveField != null) ImGui.Checkbox("Combined Field", ref _exportCombined);
            if (ad.DamageField != null) ImGui.Checkbox("Damage Field", ref _exportDamage);
            
            ImGui.Spacing();
            if (ImGui.Button("Export Wave Fields...", new Vector2(-1, 0))) _exportDialog.Open($"{ad.Name}_export");
            
            ImGui.Separator();
            
            if (ImGui.Button("Export Metadata as JSON", new Vector2(-1, 0)))
            {
                Logger.Log("[ExportTool] Exporting metadata as JSON. (Implementation pending)");
            }

            if (_exportDialog.Submit())
            {
                Logger.Log($"[ExportTool] Exporting selected wave fields to base path: {_exportDialog.SelectedPath}. (Implementation pending)");
            }
        }
    }
    
    /// <summary>
    /// A new tool to analyze Vp/Vs along a user-defined profile.
    /// </summary>
    internal class VelocityProfileTool : IDatasetTools
    {
        private List<float> _vpData;
        private List<float> _vsData;
        private string _statsResult = "No profile selected.";
        private bool _isCalculating = false;

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
                if (ImGui.Button("Cancel Drawing"))
                {
                    AcousticInteractionManager.CancelLineDrawing();
                }
            }
            else
            {
                if (ImGui.Button("Select Profile in Viewer..."))
                {
                    AcousticInteractionManager.StartLineDrawing();
                }
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
                    ImGui.PlotLines("P-Wave Velocity (Vp)", ref vpArray[0], vpArray.Length, 0, "Distance ->", _vsData.Min(), _vpData.Max(), new Vector2(0, 120));
                }

                if (_vsData != null && _vsData.Count > 0)
                {
                    var vsArray = _vsData.ToArray();
                    ImGui.PlotLines("S-Wave Velocity (Vs)", ref vsArray[0], vsArray.Length, 0, "Distance ->", _vsData.Min(), _vpData.Max(), new Vector2(0, 120));
                }
            }
        }
        
        /// <summary>
        /// Extracts velocity data along a line defined in the viewer.
        /// </summary>
        private void CalculateProfile(AcousticVolumeDataset dataset)
        {
            var densityVolume = dataset.DensityData;
            if (densityVolume == null)
            {
                _statsResult = "Density data is not available for calculation.";
                _isCalculating = false;
                return;
            }
            var (vpData, vsData) = CalculateProfile_Internal(densityVolume);
            _vpData = vpData;
            _vsData = vsData;

            if (_vpData.Count > 0)
            {
                float avgVp = _vpData.Average();
                float avgVs = _vsData.Average();
                float avgVpVs = avgVs > 0 ? avgVp / avgVs : 0;
                _statsResult = $"Points Sampled: {_vpData.Count}\n" +
                               $"Average Vp: {avgVp:F2} m/s\n" +
                               $"Average Vs: {avgVs:F2} m/s\n" +
                               $"Average Vp/Vs Ratio: {avgVpVs:F3}";
            }
            else
            {
                _statsResult = "No data points found along the selected line.";
            }

            Logger.Log($"[VelocityProfileTool] Extracted {_vpData.Count} data points for velocity profile.");
            _isCalculating = false;
        }

        public (List<float> vpData, List<float> vsData) CalculateProfile_Internal(DensityVolume densityVolume)
        {
             // Get coordinates from the interaction manager
            int x1 = (int)AcousticInteractionManager.LineStartPoint.X;
            int y1 = (int)AcousticInteractionManager.LineStartPoint.Y;
            int x2 = (int)AcousticInteractionManager.LineEndPoint.X;
            int y2 = (int)AcousticInteractionManager.LineEndPoint.Y;
            int slice_coord = AcousticInteractionManager.LineSliceIndex;
            int viewIndex = AcousticInteractionManager.LineViewIndex;

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
                bool inBounds = false;
                switch (viewIndex)
                {
                    case 0: // XY View
                        volX = x1; volY = y1; volZ = slice_coord;
                        if (volX >= 0 && volX < densityVolume.Width && volY >= 0 && volY < densityVolume.Height && volZ >= 0 && volZ < densityVolume.Depth)
                            inBounds = true;
                        break;
                    case 1: // XZ View
                        volX = x1; volY = slice_coord; volZ = y1;
                        if (volX >= 0 && volX < densityVolume.Width && volY >= 0 && volY < densityVolume.Height && volZ >= 0 && volZ < densityVolume.Depth)
                             inBounds = true;
                        break;
                    case 2: // YZ View
                        volX = slice_coord; volY = x1; volZ = y1;
                         if (volX >= 0 && volX < densityVolume.Width && volY >= 0 && volY < densityVolume.Height && volZ >= 0 && volZ < densityVolume.Depth)
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
                if (e2 >= dy) { err += dy; x1 += sx; }
                if (e2 <= dx) { err += dx; y1 += sy; }
            }
            return (vpData, vsData);
        }
    }
    #endregion
}