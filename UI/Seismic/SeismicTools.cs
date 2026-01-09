// GeoscientistToolkit/UI/Seismic/SeismicTools.cs

using System;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.Tools.BoreholeSeismic;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;

namespace GeoscientistToolkit.UI.Seismic;

/// <summary>
/// Tools panel for seismic datasets - manage line packages, analysis, and processing
/// </summary>
public class SeismicTools : IDatasetTools
{
    private SeismicViewer? _activeViewer;

    private readonly Dictionary<ToolCategory, string> _categoryDescriptions;
    private readonly Dictionary<ToolCategory, string> _categoryNames;
    private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;
    private ToolCategory _selectedCategory = ToolCategory.Packages;
    private int _selectedToolIndex;

    // Package creation
    private string _newPackageName = "New Package";
    private int _newPackageStartTrace = 0;
    private int _newPackageEndTrace = 0;
    private Vector4 _newPackageColor = new Vector4(1, 1, 0, 1);

    // Package editing
    private SeismicLinePackage? _selectedPackage;
    private int _editStartTrace = 0;
    private int _editEndTrace = 0;
    private Vector4 _editColor = new Vector4(1, 1, 0, 1);
    private string _editName = "";
    private string _editNotes = "";

    // Auto-detection parameters
    private float _amplitudeThreshold = 0.5f;
    private int _minTracesPerPackage = 10;
    private bool _showAutoDetectSettings = false;

    // Borehole-Seismic integration
    private BoreholeSeismicToolsPanel _boreholeSeismicTools = new();

    // Filter settings
    private float _filterLowFreq = 5.0f;
    private float _filterHighFreq = 100.0f;
    private float _agcWindowMs = 200.0f;
    private bool _filtersApplied = false;
    private float[][]? _originalSamples; // Backup for undo

    // Export dialogs
    private readonly ImGuiExportFileDialog _imageExportDialog;
    private readonly ImGuiExportFileDialog _segyExportDialog;
    private bool _showImageExportDialog = false;
    private bool _showSegyExportDialog = false;

    public SeismicTools()
    {
        _imageExportDialog = new ImGuiExportFileDialog("SeismicImageExport", "Export Seismic Image");
        _imageExportDialog.SetExtensions((".png", "PNG Image"), (".jpg", "JPEG Image"), (".bmp", "Bitmap Image"));
        _segyExportDialog = new ImGuiExportFileDialog("SeismicSegyExport", "Export SEG-Y File");
        _segyExportDialog.SetExtensions((".sgy", "SEG-Y File"), (".segy", "SEG-Y File"));

        _categoryNames = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Packages, "Packages" },
            { ToolCategory.Analysis, "Analysis" },
            { ToolCategory.Processing, "Processing" },
            { ToolCategory.Export, "Export" },
            { ToolCategory.Borehole, "Borehole" }
        };

        _categoryDescriptions = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Packages, "Organize line packages and automate package creation" },
            { ToolCategory.Analysis, "Analyze trace amplitudes and quality metrics" },
            { ToolCategory.Processing, "Apply filters, normalization, and gain controls" },
            { ToolCategory.Export, "Export seismic data and images" },
            { ToolCategory.Borehole, "Integrate seismic data with borehole tools" }
        };

        _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
        {
            {
                ToolCategory.Packages,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Package Overview",
                        Description = "Review packages, visibility, and trace ranges",
                        Draw = DrawPackageOverview
                    },
                    new()
                    {
                        Name = "Create Packages",
                        Description = "Create packages manually or from viewer selection",
                        Draw = DrawPackageCreationPanel
                    },
                    new()
                    {
                        Name = "Auto-Detection",
                        Description = "Automatically detect packages based on trace boundaries",
                        Draw = DrawAutoDetectionTools
                    },
                    new()
                    {
                        Name = "Edit Package",
                        Description = "Update the selected package details and ranges",
                        Draw = DrawPackageEditorPanel
                    }
                }
            },
            {
                ToolCategory.Analysis,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Analysis Tools",
                        Description = "Evaluate statistics and detect anomalies",
                        Draw = DrawAnalysisTools
                    }
                }
            },
            {
                ToolCategory.Processing,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Signal Processing",
                        Description = "Apply filters, AGC, and normalization controls",
                        Draw = DrawSignalProcessingTools
                    }
                }
            },
            {
                ToolCategory.Export,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Export",
                        Description = "Export images and SEG-Y files",
                        Draw = DrawExportTools
                    }
                }
            },
            {
                ToolCategory.Borehole,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Borehole Integration",
                        Description = "Move data between seismic lines and borehole tools",
                        Draw = _ => DrawBoreholeSeismicIntegration()
                    }
                }
            }
        };
    }

    public void SetViewer(SeismicViewer viewer)
    {
        _activeViewer = viewer;
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not SeismicDataset seismicDataset)
        {
            ImGui.TextDisabled("Invalid dataset type for seismic tools");
            return;
        }

        if (seismicDataset.SegyData == null)
        {
            ImGui.TextDisabled("No seismic data loaded");
            return;
        }

        DrawCategorizedTools(seismicDataset);
    }

    private void DrawCategorizedTools(SeismicDataset dataset)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4));
        ImGui.Text("Category:");
        ImGui.SameLine();

        var currentCategoryName = _categoryNames[_selectedCategory];
        var categoryTools = _toolsByCategory[_selectedCategory];
        var preview = $"{currentCategoryName} ({categoryTools.Count})";

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##SeismicCategorySelector", preview))
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

        if (categoryTools.Count == 0)
        {
            ImGui.TextDisabled("No tools available in this category.");
            return;
        }

        if (categoryTools.Count == 1)
        {
            DrawToolEntry(dataset, categoryTools[0], $"SeismicTool_{categoryTools[0].Name}");
            return;
        }

        if (ImGui.BeginTabBar($"SeismicTools_{_selectedCategory}", ImGuiTabBarFlags.None))
        {
            for (var i = 0; i < categoryTools.Count; i++)
            {
                var entry = categoryTools[i];
                if (ImGui.BeginTabItem(entry.Name))
                {
                    _selectedToolIndex = i;
                    DrawToolEntry(dataset, entry, $"SeismicTool_{entry.Name}");
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawToolEntry(SeismicDataset dataset, ToolEntry entry, string childId)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1));
        ImGui.Text(entry.Name);
        ImGui.PopStyleColor();

        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), entry.Description);
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginChild(childId, new Vector2(0, 0), ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar);
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 5));

            entry.Draw(dataset);

            ImGui.PopStyleVar(2);
        }
        ImGui.EndChild();
    }

    private void DrawPackageOverview(SeismicDataset dataset)
    {
        ImGui.Text("Seismic Line Package Manager");
        ImGui.Separator();
        ImGui.Text($"Total Traces: {dataset.GetTraceCount()}");
        ImGui.Text($"Packages: {dataset.LinePackages.Count}");
        ImGui.Spacing();

        DrawPackageList(dataset);
    }

    private void DrawPackageCreationPanel(SeismicDataset dataset)
    {
        if (_activeViewer != null)
        {
            var (startTrace, endTrace) = _activeViewer.GetCurrentSelection();
            if (startTrace >= 0 && endTrace >= 0)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Selection: Traces {startTrace} - {endTrace}");

                if (ImGui.Button("Create Package from Selection", new Vector2(-1, 0)))
                {
                    _newPackageStartTrace = startTrace;
                    _newPackageEndTrace = endTrace;
                    _newPackageName = $"Package {dataset.LinePackages.Count + 1}";
                }

                ImGui.Spacing();
            }
        }

        DrawPackageCreation(dataset);
    }

    private void DrawPackageEditorPanel(SeismicDataset dataset)
    {
        if (_selectedPackage != null)
        {
            DrawPackageEditor(dataset);
            return;
        }

        ImGui.TextDisabled("Select a package from the Package Overview to edit it.");
    }

    private void DrawPackageList(SeismicDataset dataset)
    {
        ImGui.Text("Line Packages:");

        if (dataset.LinePackages.Count == 0)
        {
            ImGui.TextDisabled("  No packages created yet");
            return;
        }

        ImGui.BeginChild("PackageList", new Vector2(0, 200), ImGuiChildFlags.Border);

        for (int i = 0; i < dataset.LinePackages.Count; i++)
        {
            var package = dataset.LinePackages[i];

            ImGui.PushID(i);

            // Visibility checkbox
            bool visible = package.IsVisible;
            if (ImGui.Checkbox("##visible", ref visible))
            {
                package.IsVisible = visible;
            }

            ImGui.SameLine();

            // Color indicator
            ImGui.ColorButton("##color", package.Color, ImGuiColorEditFlags.NoTooltip, new Vector2(20, 20));

            ImGui.SameLine();

            // Package info
            var isSelected = _selectedPackage == package;
            if (ImGui.Selectable($"{package.Name} [{package.StartTrace}-{package.EndTrace}] ({package.TraceCount} traces)", isSelected))
            {
                _selectedPackage = package;
                _editName = package.Name;
                _editStartTrace = package.StartTrace;
                _editEndTrace = package.EndTrace;
                _editColor = package.Color;
                _editNotes = package.Notes;
            }

            // Context menu
            if (ImGui.BeginPopupContextItem($"PackageContext_{i}"))
            {
                if (ImGui.MenuItem("Edit"))
                {
                    _selectedPackage = package;
                    _editName = package.Name;
                    _editStartTrace = package.StartTrace;
                    _editEndTrace = package.EndTrace;
                    _editColor = package.Color;
                    _editNotes = package.Notes;
                }

                if (ImGui.MenuItem("Delete", package.Name != "Full Section"))
                {
                    dataset.RemoveLinePackage(package);
                    if (_selectedPackage == package)
                        _selectedPackage = null;
                }

                if (ImGui.MenuItem("Duplicate"))
                {
                    var clone = package.Clone();
                    clone.Name += " (Copy)";
                    dataset.AddLinePackage(clone);
                }

                ImGui.EndPopup();
            }

            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private void DrawPackageCreation(SeismicDataset dataset)
    {
        if (ImGui.CollapsingHeader("Create New Package", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.InputText("Name", ref _newPackageName, 256);

            var maxTrace = dataset.GetTraceCount() - 1;
            ImGui.DragInt("Start Trace", ref _newPackageStartTrace, 1, 0, maxTrace);
            ImGui.DragInt("End Trace", ref _newPackageEndTrace, 1, _newPackageStartTrace, maxTrace);

            ImGui.ColorEdit4("Color", ref _newPackageColor);

            var traceCount = Math.Max(0, _newPackageEndTrace - _newPackageStartTrace + 1);
            ImGui.Text($"Package will contain {traceCount} traces");

            if (ImGui.Button("Create Package", new Vector2(-1, 0)))
            {
                var package = new SeismicLinePackage
                {
                    Name = _newPackageName,
                    StartTrace = _newPackageStartTrace,
                    EndTrace = _newPackageEndTrace,
                    Color = _newPackageColor,
                    IsVisible = true
                };

                dataset.AddLinePackage(package);

                // Reset for next package
                _newPackageName = $"Package {dataset.LinePackages.Count + 1}";
                _newPackageStartTrace = _newPackageEndTrace + 1;
                _newPackageEndTrace = Math.Min(_newPackageStartTrace + 99, maxTrace);

                // Clear viewer selection
                _activeViewer?.ClearSelection();

                Logger.Log($"[SeismicTools] Created package '{package.Name}' with {package.TraceCount} traces");
            }
        }
    }

    private void DrawAutoDetectionTools(SeismicDataset dataset)
    {
        if (ImGui.CollapsingHeader("Auto-Detection Tools"))
        {
            ImGui.TextWrapped("Semi-automatic tools to help identify and create line packages based on seismic characteristics.");
            ImGui.Spacing();

            // Divide by trace count
            ImGui.Text("Divide Equally:");
            int numDivisions = 4;
            ImGui.SetNextItemWidth(100);
            ImGui.DragInt("##divisions", ref numDivisions, 1, 2, 20);
            ImGui.SameLine();
            if (ImGui.Button("Create Equal Packages"))
            {
                CreateEqualPackages(dataset, numDivisions);
            }

            ImGui.Spacing();

            // Detect by amplitude changes
            ImGui.Text("Detect by Amplitude:");
            ImGui.SetNextItemWidth(150);
            ImGui.SliderFloat("Threshold", ref _amplitudeThreshold, 0.1f, 2.0f);
            ImGui.SetNextItemWidth(150);
            ImGui.DragInt("Min Traces", ref _minTracesPerPackage, 1, 5, 100);

            if (ImGui.Button("Auto-Detect Packages"))
            {
                AutoDetectPackages(dataset);
            }

            ImGui.Spacing();

            // Clear all packages (except Full Section)
            if (ImGui.Button("Clear All Packages", new Vector2(-1, 0)))
            {
                var toRemove = dataset.LinePackages.Where(p => p.Name != "Full Section").ToList();
                foreach (var pkg in toRemove)
                {
                    dataset.RemoveLinePackage(pkg);
                }
                _selectedPackage = null;
            }
        }
    }

    private void CreateEqualPackages(SeismicDataset dataset, int numDivisions)
    {
        var totalTraces = dataset.GetTraceCount();
        var tracesPerPackage = totalTraces / numDivisions;

        // Remove existing packages except Full Section
        var toRemove = dataset.LinePackages.Where(p => p.Name != "Full Section").ToList();
        foreach (var pkg in toRemove)
        {
            dataset.RemoveLinePackage(pkg);
        }

        // Create new packages
        var colors = new[]
        {
            new Vector4(1, 0, 0, 1),  // Red
            new Vector4(0, 1, 0, 1),  // Green
            new Vector4(0, 0, 1, 1),  // Blue
            new Vector4(1, 1, 0, 1),  // Yellow
            new Vector4(1, 0, 1, 1),  // Magenta
            new Vector4(0, 1, 1, 1),  // Cyan
        };

        for (int i = 0; i < numDivisions; i++)
        {
            var startTrace = i * tracesPerPackage;
            var endTrace = (i == numDivisions - 1) ? totalTraces - 1 : (i + 1) * tracesPerPackage - 1;

            var package = new SeismicLinePackage
            {
                Name = $"Section {i + 1}",
                StartTrace = startTrace,
                EndTrace = endTrace,
                Color = colors[i % colors.Length],
                IsVisible = true
            };

            dataset.AddLinePackage(package);
        }

        Logger.Log($"[SeismicTools] Created {numDivisions} equal packages");
    }

    private void AutoDetectPackages(SeismicDataset dataset)
    {
        // Simple auto-detection based on RMS amplitude changes between trace groups
        var totalTraces = dataset.GetTraceCount();
        var windowSize = _minTracesPerPackage;

        var rmsValues = new List<float>();

        // Calculate RMS for each window
        for (int i = 0; i < totalTraces - windowSize; i += windowSize / 2)
        {
            var windowEnd = Math.Min(i + windowSize, totalTraces);
            float sumSquares = 0;
            int count = 0;

            for (int t = i; t < windowEnd; t++)
            {
                var trace = dataset.GetTrace(t);
                if (trace?.Samples != null)
                {
                    foreach (var sample in trace.Samples)
                    {
                        sumSquares += sample * sample;
                        count++;
                    }
                }
            }

            var rms = count > 0 ? (float)Math.Sqrt(sumSquares / count) : 0;
            rmsValues.Add(rms);
        }

        // Find boundaries where RMS changes significantly
        var boundaries = new List<int> { 0 };

        for (int i = 1; i < rmsValues.Count - 1; i++)
        {
            var change = Math.Abs(rmsValues[i + 1] - rmsValues[i]) / (rmsValues[i] + 0.001f);
            if (change > _amplitudeThreshold)
            {
                boundaries.Add(i * windowSize / 2);
            }
        }

        boundaries.Add(totalTraces - 1);

        // Remove existing packages except Full Section
        var toRemove = dataset.LinePackages.Where(p => p.Name != "Full Section").ToList();
        foreach (var pkg in toRemove)
        {
            dataset.RemoveLinePackage(pkg);
        }

        // Create packages from boundaries
        var colors = new[]
        {
            new Vector4(1, 0.5f, 0, 1),   // Orange
            new Vector4(0.5f, 0, 1, 1),   // Purple
            new Vector4(0, 0.5f, 0.5f, 1), // Teal
            new Vector4(1, 0, 0.5f, 1),   // Pink
        };

        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            var package = new SeismicLinePackage
            {
                Name = $"Auto-Detected {i + 1}",
                StartTrace = boundaries[i],
                EndTrace = boundaries[i + 1],
                Color = colors[i % colors.Length],
                IsVisible = true
            };

            dataset.AddLinePackage(package);
        }

        Logger.Log($"[SeismicTools] Auto-detected {boundaries.Count - 1} packages");
    }

    private void DrawPackageEditor(SeismicDataset dataset)
    {
        if (_selectedPackage == null)
            return;

        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.3f, 0.3f, 0.5f, 1.0f));

        if (ImGui.CollapsingHeader("Edit Package", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.PopStyleColor();

            ImGui.InputText("Package Name", ref _editName, 256);

            var maxTrace = dataset.GetTraceCount() - 1;
            ImGui.DragInt("Start Trace##edit", ref _editStartTrace, 1, 0, maxTrace);
            ImGui.DragInt("End Trace##edit", ref _editEndTrace, 1, _editStartTrace, maxTrace);

            ImGui.ColorEdit4("Package Color", ref _editColor);

            ImGui.InputTextMultiline("Notes", ref _editNotes, 1024, new Vector2(-1, 80));

            var traceCount = Math.Max(0, _editEndTrace - _editStartTrace + 1);
            ImGui.Text($"Package contains {traceCount} traces");

            ImGui.Spacing();

            if (ImGui.Button("Apply Changes", new Vector2(-1, 0)))
            {
                _selectedPackage.Name = _editName;
                _selectedPackage.StartTrace = _editStartTrace;
                _selectedPackage.EndTrace = _editEndTrace;
                _selectedPackage.Color = _editColor;
                _selectedPackage.Notes = _editNotes;

                Logger.Log($"[SeismicTools] Updated package '{_selectedPackage.Name}'");
            }

            if (ImGui.Button("Close Editor"))
            {
                _selectedPackage = null;
            }
        }
        else
        {
            ImGui.PopStyleColor();
        }
    }

    private void DrawAnalysisTools(SeismicDataset dataset)
    {
        if (ImGui.CollapsingHeader("Analysis Tools"))
        {
            ImGui.TextWrapped("Analyze seismic data within packages.");
            ImGui.Spacing();

            if (_selectedPackage != null)
            {
                ImGui.Text($"Selected Package: {_selectedPackage.Name}");

                var traces = dataset.GetTracesInPackage(_selectedPackage);
                var stats = CalculatePackageStatistics(traces);

                ImGui.Text($"Traces: {traces.Count}");
                ImGui.Text($"Min Amplitude: {stats.min:F4}");
                ImGui.Text($"Max Amplitude: {stats.max:F4}");
                ImGui.Text($"Mean Amplitude: {stats.mean:F4}");
                ImGui.Text($"RMS Amplitude: {stats.rms:F4}");
            }
            else
            {
                ImGui.TextDisabled("Select a package to analyze");
            }
        }
    }

    private (float min, float max, float mean, float rms) CalculatePackageStatistics(List<SegyTrace> traces)
    {
        if (traces.Count == 0)
            return (0, 0, 0, 0);

        var allSamples = traces.SelectMany(t => t.Samples).ToArray();
        if (allSamples.Length == 0)
            return (0, 0, 0, 0);

        var min = allSamples.Min();
        var max = allSamples.Max();
        var mean = allSamples.Average();
        var sumSquares = allSamples.Sum(s => s * s);
        var rms = (float)Math.Sqrt(sumSquares / allSamples.Length);

        return (min, max, mean, rms);
    }

    private void DrawSignalProcessingTools(SeismicDataset dataset)
    {
        if (ImGui.CollapsingHeader("Signal Processing"))
        {
            ImGui.TextWrapped("Apply filters and processing to seismic traces.");
            ImGui.Spacing();

            // Get sample rate for frequency calculations
            var sampleIntervalMs = dataset.GetSampleIntervalMs();
            var nyquistFreq = sampleIntervalMs > 0 ? 500.0f / sampleIntervalMs : 250.0f;

            ImGui.Text($"Sample Rate: {(sampleIntervalMs > 0 ? 1000.0f / sampleIntervalMs : 0):F1} Hz (Nyquist: {nyquistFreq:F1} Hz)");
            ImGui.Spacing();

            // Filter indicator
            if (_filtersApplied)
            {
                ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f), "Filters applied to data");
                if (ImGui.Button("Undo All Filters", new Vector2(-1, 0)))
                {
                    UndoFilters(dataset);
                }
                ImGui.Spacing();
            }

            // Bandpass filter
            ImGui.Text("Bandpass Filter:");
            ImGui.SetNextItemWidth(120);
            ImGui.SliderFloat("Low Cut (Hz)", ref _filterLowFreq, 0.1f, nyquistFreq * 0.5f, "%.1f");
            ImGui.SetNextItemWidth(120);
            ImGui.SliderFloat("High Cut (Hz)", ref _filterHighFreq, _filterLowFreq + 1, nyquistFreq, "%.1f");

            if (ImGui.Button("Apply Bandpass", new Vector2(-1, 0)))
            {
                ApplyBandpassFilter(dataset, _filterLowFreq, _filterHighFreq);
            }

            ImGui.Spacing();

            // High-pass filter
            ImGui.Text("High-Pass Filter:");
            if (ImGui.Button($"Apply High-Pass ({_filterLowFreq:F1} Hz)", new Vector2(-1, 0)))
            {
                ApplyHighPassFilter(dataset, _filterLowFreq);
            }

            ImGui.Spacing();

            // Low-pass filter
            ImGui.Text("Low-Pass Filter:");
            if (ImGui.Button($"Apply Low-Pass ({_filterHighFreq:F1} Hz)", new Vector2(-1, 0)))
            {
                ApplyLowPassFilter(dataset, _filterHighFreq);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // AGC (Automatic Gain Control)
            ImGui.Text("Automatic Gain Control (AGC):");
            ImGui.SetNextItemWidth(120);
            ImGui.SliderFloat("Window (ms)", ref _agcWindowMs, 50.0f, 1000.0f, "%.0f");

            if (ImGui.Button("Apply AGC", new Vector2(-1, 0)))
            {
                ApplyAGC(dataset, _agcWindowMs);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Trace normalization
            ImGui.Text("Trace Normalization:");
            if (ImGui.Button("Normalize Traces", new Vector2(-1, 0)))
            {
                NormalizeTraces(dataset);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Normalize each trace to unit amplitude");

            ImGui.Spacing();

            // DC removal
            if (ImGui.Button("Remove DC Bias", new Vector2(-1, 0)))
            {
                RemoveDCBias(dataset);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove constant offset from each trace");
        }
    }

    private void DrawExportTools(SeismicDataset dataset)
    {
        if (ImGui.CollapsingHeader("Export", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Export seismic data to various formats.");
            ImGui.Spacing();

            // Export as Image
            if (ImGui.Button("Export as Image...", new Vector2(-1, 0)))
            {
                _showImageExportDialog = true;
                _imageExportDialog.Open();
            }

            // Handle image export dialog
            if (_showImageExportDialog)
            {
                if (_imageExportDialog.Submit())
                {
                    ExportAsImage(dataset, _imageExportDialog.SelectedPath);
                    _showImageExportDialog = false;
                }
                if (!_imageExportDialog.IsOpen)
                {
                    _showImageExportDialog = false;
                }
            }

            ImGui.Spacing();

            // Export as SEG-Y
            if (ImGui.Button("Export as SEG-Y...", new Vector2(-1, 0)))
            {
                _showSegyExportDialog = true;
                _segyExportDialog.Open();
            }

            // Handle SEG-Y export dialog
            if (_showSegyExportDialog)
            {
                if (_segyExportDialog.Submit())
                {
                    ExportAsSegy(dataset, _segyExportDialog.SelectedPath);
                    _showSegyExportDialog = false;
                }
                if (!_segyExportDialog.IsOpen)
                {
                    _showSegyExportDialog = false;
                }
            }
        }
    }

    #region Signal Processing Methods

    private void BackupOriginalSamples(SeismicDataset dataset)
    {
        if (_originalSamples != null) return; // Already backed up

        var traces = dataset.SegyData?.Traces;
        if (traces == null) return;

        _originalSamples = new float[traces.Count][];
        for (int i = 0; i < traces.Count; i++)
        {
            _originalSamples[i] = (float[])traces[i].Samples.Clone();
        }

        Logger.Log("[SeismicTools] Backed up original samples for undo");
    }

    private void UndoFilters(SeismicDataset dataset)
    {
        if (_originalSamples == null) return;

        var traces = dataset.SegyData?.Traces;
        if (traces == null) return;

        for (int i = 0; i < Math.Min(traces.Count, _originalSamples.Length); i++)
        {
            traces[i].Samples = (float[])_originalSamples[i].Clone();
        }

        _originalSamples = null;
        _filtersApplied = false;
        _activeViewer?.RequestRedraw();

        Logger.Log("[SeismicTools] Restored original samples");
    }

    private void ApplyBandpassFilter(SeismicDataset dataset, float lowFreq, float highFreq)
    {
        BackupOriginalSamples(dataset);

        var traces = dataset.SegyData?.Traces;
        if (traces == null) return;

        var sampleIntervalMs = dataset.GetSampleIntervalMs();
        if (sampleIntervalMs <= 0) return;

        var sampleRate = 1000.0f / sampleIntervalMs;

        System.Threading.Tasks.Parallel.For(0, traces.Count, i =>
        {
            traces[i].Samples = ApplyButterworthBandpass(traces[i].Samples, lowFreq, highFreq, sampleRate);
        });

        _filtersApplied = true;
        _activeViewer?.RequestRedraw();
        RecalculateStatistics(dataset);

        Logger.Log($"[SeismicTools] Applied bandpass filter: {lowFreq}-{highFreq} Hz");
    }

    private void ApplyHighPassFilter(SeismicDataset dataset, float cutoffFreq)
    {
        BackupOriginalSamples(dataset);

        var traces = dataset.SegyData?.Traces;
        if (traces == null) return;

        var sampleIntervalMs = dataset.GetSampleIntervalMs();
        if (sampleIntervalMs <= 0) return;

        var sampleRate = 1000.0f / sampleIntervalMs;

        System.Threading.Tasks.Parallel.For(0, traces.Count, i =>
        {
            traces[i].Samples = ApplyButterworthHighPass(traces[i].Samples, cutoffFreq, sampleRate);
        });

        _filtersApplied = true;
        _activeViewer?.RequestRedraw();
        RecalculateStatistics(dataset);

        Logger.Log($"[SeismicTools] Applied high-pass filter: {cutoffFreq} Hz");
    }

    private void ApplyLowPassFilter(SeismicDataset dataset, float cutoffFreq)
    {
        BackupOriginalSamples(dataset);

        var traces = dataset.SegyData?.Traces;
        if (traces == null) return;

        var sampleIntervalMs = dataset.GetSampleIntervalMs();
        if (sampleIntervalMs <= 0) return;

        var sampleRate = 1000.0f / sampleIntervalMs;

        System.Threading.Tasks.Parallel.For(0, traces.Count, i =>
        {
            traces[i].Samples = ApplyButterworthLowPass(traces[i].Samples, cutoffFreq, sampleRate);
        });

        _filtersApplied = true;
        _activeViewer?.RequestRedraw();
        RecalculateStatistics(dataset);

        Logger.Log($"[SeismicTools] Applied low-pass filter: {cutoffFreq} Hz");
    }

    private void ApplyAGC(SeismicDataset dataset, float windowMs)
    {
        BackupOriginalSamples(dataset);

        var traces = dataset.SegyData?.Traces;
        if (traces == null) return;

        var sampleIntervalMs = dataset.GetSampleIntervalMs();
        if (sampleIntervalMs <= 0) return;

        var windowSamples = (int)(windowMs / sampleIntervalMs);
        windowSamples = Math.Max(windowSamples, 3);

        System.Threading.Tasks.Parallel.For(0, traces.Count, i =>
        {
            traces[i].Samples = ApplyAGCToTrace(traces[i].Samples, windowSamples);
        });

        _filtersApplied = true;
        _activeViewer?.RequestRedraw();
        RecalculateStatistics(dataset);

        Logger.Log($"[SeismicTools] Applied AGC with {windowMs}ms window");
    }

    private void NormalizeTraces(SeismicDataset dataset)
    {
        BackupOriginalSamples(dataset);

        var traces = dataset.SegyData?.Traces;
        if (traces == null) return;

        System.Threading.Tasks.Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            if (samples.Length == 0) return;

            var maxAbs = samples.Max(s => Math.Abs(s));
            if (maxAbs > 0)
            {
                for (int j = 0; j < samples.Length; j++)
                {
                    samples[j] /= maxAbs;
                }
            }
        });

        _filtersApplied = true;
        _activeViewer?.RequestRedraw();
        RecalculateStatistics(dataset);

        Logger.Log("[SeismicTools] Normalized all traces");
    }

    private void RemoveDCBias(SeismicDataset dataset)
    {
        BackupOriginalSamples(dataset);

        var traces = dataset.SegyData?.Traces;
        if (traces == null) return;

        System.Threading.Tasks.Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            if (samples.Length == 0) return;

            var mean = samples.Average();
            for (int j = 0; j < samples.Length; j++)
            {
                samples[j] -= mean;
            }
        });

        _filtersApplied = true;
        _activeViewer?.RequestRedraw();
        RecalculateStatistics(dataset);

        Logger.Log("[SeismicTools] Removed DC bias from all traces");
    }

    private static float[] ApplyButterworthBandpass(float[] samples, float lowFreq, float highFreq, float sampleRate)
    {
        // Simple 2nd-order Butterworth bandpass filter using biquad coefficients
        var result = new float[samples.Length];
        Array.Copy(samples, result, samples.Length);

        // Apply high-pass first
        result = ApplyButterworthHighPass(result, lowFreq, sampleRate);
        // Then low-pass
        result = ApplyButterworthLowPass(result, highFreq, sampleRate);

        return result;
    }

    private static float[] ApplyButterworthHighPass(float[] samples, float cutoffFreq, float sampleRate)
    {
        if (samples.Length < 3) return samples;

        var result = new float[samples.Length];
        var omega = 2.0 * Math.PI * cutoffFreq / sampleRate;
        var cos_omega = Math.Cos(omega);
        var alpha = Math.Sin(omega) / (2.0 * 0.707); // Q = 0.707 for Butterworth

        var a0 = 1.0 + alpha;
        var a1 = -2.0 * cos_omega;
        var a2 = 1.0 - alpha;
        var b0 = (1.0 + cos_omega) / 2.0;
        var b1 = -(1.0 + cos_omega);
        var b2 = (1.0 + cos_omega) / 2.0;

        // Normalize coefficients
        b0 /= a0; b1 /= a0; b2 /= a0;
        a1 /= a0; a2 /= a0;

        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            double x0 = samples[i];
            double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;

            result[i] = (float)y0;

            x2 = x1; x1 = x0;
            y2 = y1; y1 = y0;
        }

        return result;
    }

    private static float[] ApplyButterworthLowPass(float[] samples, float cutoffFreq, float sampleRate)
    {
        if (samples.Length < 3) return samples;

        var result = new float[samples.Length];
        var omega = 2.0 * Math.PI * cutoffFreq / sampleRate;
        var cos_omega = Math.Cos(omega);
        var alpha = Math.Sin(omega) / (2.0 * 0.707);

        var a0 = 1.0 + alpha;
        var a1 = -2.0 * cos_omega;
        var a2 = 1.0 - alpha;
        var b0 = (1.0 - cos_omega) / 2.0;
        var b1 = 1.0 - cos_omega;
        var b2 = (1.0 - cos_omega) / 2.0;

        // Normalize coefficients
        b0 /= a0; b1 /= a0; b2 /= a0;
        a1 /= a0; a2 /= a0;

        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            double x0 = samples[i];
            double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;

            result[i] = (float)y0;

            x2 = x1; x1 = x0;
            y2 = y1; y1 = y0;
        }

        return result;
    }

    private static float[] ApplyAGCToTrace(float[] samples, int windowSamples)
    {
        if (samples.Length < windowSamples) return samples;

        var result = new float[samples.Length];
        var halfWindow = windowSamples / 2;

        for (int i = 0; i < samples.Length; i++)
        {
            var startIdx = Math.Max(0, i - halfWindow);
            var endIdx = Math.Min(samples.Length, i + halfWindow);

            // Calculate RMS in window
            double sumSquares = 0;
            for (int j = startIdx; j < endIdx; j++)
            {
                sumSquares += samples[j] * samples[j];
            }
            var rms = Math.Sqrt(sumSquares / (endIdx - startIdx));

            // Apply gain (avoid division by zero)
            if (rms > 1e-10)
            {
                result[i] = (float)(samples[i] / rms);
            }
            else
            {
                result[i] = samples[i];
            }
        }

        return result;
    }

    private void RecalculateStatistics(SeismicDataset dataset)
    {
        // Recalculate amplitude statistics after filtering
        var traces = dataset.SegyData?.Traces;
        var header = dataset.SegyData?.Header;
        if (traces == null || header == null) return;

        float minAmp = float.MaxValue;
        float maxAmp = float.MinValue;
        double sumSquares = 0;
        long count = 0;

        foreach (var trace in traces)
        {
            foreach (var sample in trace.Samples)
            {
                if (sample < minAmp) minAmp = sample;
                if (sample > maxAmp) maxAmp = sample;
                sumSquares += sample * sample;
                count++;
            }
        }

        header.MinAmplitude = minAmp;
        header.MaxAmplitude = maxAmp;
        header.RmsAmplitude = count > 0 ? (float)Math.Sqrt(sumSquares / count) : 0;
    }

    #endregion

    #region Export Methods

    private void ExportAsImage(SeismicDataset dataset, string filePath)
    {
        try
        {
            Logger.Log($"[SeismicTools] Exporting seismic image to: {filePath}");

            var numTraces = dataset.GetTraceCount();
            var numSamples = dataset.GetSampleCount();

            if (numTraces == 0 || numSamples == 0)
            {
                Logger.LogError("[SeismicTools] No data to export");
                return;
            }

            // Create RGBA pixel data
            var pixelData = new byte[numTraces * numSamples * 4];

            var (minAmp, maxAmp, _) = dataset.GetAmplitudeStatistics();
            var amplitudeRange = maxAmp - minAmp;
            if (amplitudeRange == 0) amplitudeRange = 1.0f;

            for (int traceIdx = 0; traceIdx < numTraces; traceIdx++)
            {
                var trace = dataset.GetTrace(traceIdx);
                if (trace == null) continue;

                for (int sampleIdx = 0; sampleIdx < Math.Min(numSamples, trace.Samples.Length); sampleIdx++)
                {
                    var amplitude = trace.Samples[sampleIdx] * dataset.GainValue;
                    var normalized = (amplitude - minAmp) / amplitudeRange;
                    normalized = Math.Clamp(normalized, 0.0f, 1.0f);

                    // Seismic colormap (blue-white-red)
                    byte r, g, b;
                    if (normalized < 0.5f)
                    {
                        var t = normalized * 2;
                        r = (byte)(t * 255);
                        g = (byte)(t * 255);
                        b = 255;
                    }
                    else
                    {
                        var t = (normalized - 0.5f) * 2;
                        r = 255;
                        g = (byte)((1 - t) * 255);
                        b = (byte)((1 - t) * 255);
                    }

                    var pixelIdx = (sampleIdx * numTraces + traceIdx) * 4;
                    pixelData[pixelIdx] = r;
                    pixelData[pixelIdx + 1] = g;
                    pixelData[pixelIdx + 2] = b;
                    pixelData[pixelIdx + 3] = 255;
                }
            }

            // Write image using StbImageWrite
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var writer = new ImageWriter();

            using var stream = File.Create(filePath);

            if (extension == ".png")
            {
                writer.WritePng(pixelData, numTraces, numSamples, ColorComponents.RedGreenBlueAlpha, stream);
            }
            else if (extension == ".jpg" || extension == ".jpeg")
            {
                writer.WriteJpg(pixelData, numTraces, numSamples, ColorComponents.RedGreenBlueAlpha, stream, 95);
            }
            else if (extension == ".bmp")
            {
                writer.WriteBmp(pixelData, numTraces, numSamples, ColorComponents.RedGreenBlueAlpha, stream);
            }
            else
            {
                writer.WritePng(pixelData, numTraces, numSamples, ColorComponents.RedGreenBlueAlpha, stream);
            }

            Logger.Log($"[SeismicTools] Successfully exported image to: {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SeismicTools] Error exporting image: {ex.Message}");
        }
    }

    private void ExportAsSegy(SeismicDataset dataset, string filePath)
    {
        try
        {
            Logger.Log($"[SeismicTools] Exporting SEG-Y to: {filePath}");

            var segyData = dataset.SegyData;
            if (segyData == null)
            {
                Logger.LogError("[SeismicTools] No SEG-Y data to export");
                return;
            }

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            // Write textual header (3200 bytes EBCDIC)
            var textualHeader = CreateTextualHeader(dataset);
            var ebcdicBytes = AsciiToEbcdic(textualHeader);
            writer.Write(ebcdicBytes);

            // Write binary header (400 bytes)
            WriteBinaryHeader(writer, segyData.Header, segyData.Traces.Count);

            // Write traces
            foreach (var trace in segyData.Traces)
            {
                WriteTrace(writer, trace, segyData.Header.SampleFormat);
            }

            Logger.Log($"[SeismicTools] Successfully exported SEG-Y to: {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SeismicTools] Error exporting SEG-Y: {ex.Message}");
        }
    }

    private string CreateTextualHeader(SeismicDataset dataset)
    {
        var lines = new string[40];
        for (int i = 0; i < 40; i++) lines[i] = new string(' ', 80);

        lines[0] = $"C 1 CLIENT: {dataset.DatasetMetadata.SampleName,-69}".PadRight(80);
        lines[1] = $"C 2 LINE: {dataset.Name,-71}".PadRight(80);
        lines[2] = $"C 3 EXPORTED FROM GEOSCIENTIST TOOLKIT{new string(' ', 42)}".PadRight(80);
        lines[3] = $"C 4 DATE: {DateTime.Now:yyyy-MM-dd HH:mm,-69}".PadRight(80);
        lines[4] = $"C 5 TRACES: {dataset.GetTraceCount(),-68}".PadRight(80);
        lines[5] = $"C 6 SAMPLES PER TRACE: {dataset.GetSampleCount(),-57}".PadRight(80);
        lines[6] = $"C 7 SAMPLE INTERVAL (us): {dataset.SegyData?.Header.SampleInterval ?? 0,-54}".PadRight(80);
        lines[7] = $"C 8 DATA FORMAT: IEEE FLOATING POINT (5){new string(' ', 40)}".PadRight(80);

        for (int i = 8; i < 39; i++)
            lines[i] = $"C{i + 1,2}{new string(' ', 77)}";

        lines[39] = $"C40 END TEXTUAL HEADER{new string(' ', 58)}";

        return string.Join("", lines);
    }

    private void WriteBinaryHeader(BinaryWriter writer, SegyHeader header, int numTraces)
    {
        var buffer = new byte[400];

        // Job ID (bytes 1-4)
        WriteInt32BigEndian(buffer, 0, header.JobId);
        // Line number (bytes 5-8)
        WriteInt32BigEndian(buffer, 4, header.LineNumber);
        // Reel number (bytes 9-12)
        WriteInt32BigEndian(buffer, 8, header.ReelNumber);
        // Traces per ensemble (bytes 13-14)
        WriteInt16BigEndian(buffer, 12, (short)header.NumTracesPerEnsemble);
        // Aux traces per ensemble (bytes 15-16)
        WriteInt16BigEndian(buffer, 14, (short)header.NumAuxTracesPerEnsemble);
        // Sample interval (bytes 17-18)
        WriteInt16BigEndian(buffer, 16, (short)header.SampleInterval);
        // Original sample interval (bytes 19-20)
        WriteInt16BigEndian(buffer, 18, (short)header.SampleIntervalOriginal);
        // Samples per trace (bytes 21-22)
        WriteInt16BigEndian(buffer, 20, (short)header.NumSamples);
        // Original samples per trace (bytes 23-24)
        WriteInt16BigEndian(buffer, 22, (short)header.NumSamplesOriginal);
        // Sample format (bytes 25-26) - 5 = IEEE float
        WriteInt16BigEndian(buffer, 24, 5);
        // Ensemble fold (bytes 27-28)
        WriteInt16BigEndian(buffer, 26, (short)header.EnsembleFold);
        // Trace sorting (bytes 29-30)
        WriteInt16BigEndian(buffer, 28, (short)header.TraceSorting);
        // Measurement system (bytes 55-56) - 1=meters
        WriteInt16BigEndian(buffer, 54, (short)header.MeasurementSystem);
        // SEG-Y revision (bytes 301-302) - Rev 1
        WriteInt16BigEndian(buffer, 300, 256); // 0x0100 = Rev 1.0

        writer.Write(buffer);
    }

    private void WriteTrace(BinaryWriter writer, SegyTrace trace, int sampleFormat)
    {
        // Write 240-byte trace header
        var headerBuffer = new byte[240];

        WriteInt32BigEndian(headerBuffer, 0, trace.TraceSequenceNumber);
        WriteInt32BigEndian(headerBuffer, 4, trace.TraceSequenceNumberInLine);
        WriteInt32BigEndian(headerBuffer, 8, trace.FieldRecordNumber);
        WriteInt32BigEndian(headerBuffer, 12, trace.TraceNumberInField);
        WriteInt32BigEndian(headerBuffer, 16, trace.EnergySourcePoint);
        WriteInt32BigEndian(headerBuffer, 20, trace.EnsembleNumber);
        WriteInt32BigEndian(headerBuffer, 24, trace.TraceNumberInEnsemble);
        WriteInt16BigEndian(headerBuffer, 28, trace.TraceIdentificationCode);
        WriteInt16BigEndian(headerBuffer, 114, trace.NumSamplesInTrace > 0 ? trace.NumSamplesInTrace : (short)trace.Samples.Length);
        WriteInt16BigEndian(headerBuffer, 116, trace.SampleIntervalInTrace);
        WriteInt16BigEndian(headerBuffer, 70, trace.CoordinateScalar);
        WriteInt32BigEndian(headerBuffer, 72, trace.SourceX);
        WriteInt32BigEndian(headerBuffer, 76, trace.SourceY);
        WriteInt32BigEndian(headerBuffer, 80, trace.GroupX);
        WriteInt32BigEndian(headerBuffer, 84, trace.GroupY);
        WriteInt32BigEndian(headerBuffer, 180, trace.CdpX);
        WriteInt32BigEndian(headerBuffer, 184, trace.CdpY);

        writer.Write(headerBuffer);

        // Write samples as IEEE float (big-endian)
        foreach (var sample in trace.Samples)
        {
            var bytes = BitConverter.GetBytes(sample);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            writer.Write(bytes);
        }
    }

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteInt16BigEndian(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static byte[] AsciiToEbcdic(string ascii)
    {
        var result = new byte[3200];
        for (int i = 0; i < Math.Min(ascii.Length, 3200); i++)
        {
            result[i] = AsciiCharToEbcdic(ascii[i]);
        }
        // Pad with EBCDIC spaces
        for (int i = ascii.Length; i < 3200; i++)
        {
            result[i] = 0x40; // EBCDIC space
        }
        return result;
    }

    private static byte AsciiCharToEbcdic(char c)
    {
        // Simplified ASCII to EBCDIC mapping for common characters
        return c switch
        {
            ' ' => 0x40,
            >= '0' and <= '9' => (byte)(0xF0 + (c - '0')),
            >= 'A' and <= 'I' => (byte)(0xC1 + (c - 'A')),
            >= 'J' and <= 'R' => (byte)(0xD1 + (c - 'J')),
            >= 'S' and <= 'Z' => (byte)(0xE2 + (c - 'S')),
            >= 'a' and <= 'i' => (byte)(0x81 + (c - 'a')),
            >= 'j' and <= 'r' => (byte)(0x91 + (c - 'j')),
            >= 's' and <= 'z' => (byte)(0xA2 + (c - 's')),
            '.' => 0x4B,
            ':' => 0x7A,
            '-' => 0x60,
            '/' => 0x61,
            '(' => 0x4D,
            ')' => 0x5D,
            _ => 0x40 // Default to space
        };
    }

    #endregion

    private void DrawBoreholeSeismicIntegration()
    {
        if (ImGui.CollapsingHeader("Borehole Integration", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Spacing();

            if (ImGui.BeginTabBar("BoreholeSeismicTabs"))
            {
                if (ImGui.BeginTabItem("Seismic -> Borehole"))
                {
                    _boreholeSeismicTools.DrawSeismicToBorehole();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Well Tie"))
                {
                    _boreholeSeismicTools.DrawWellTie();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
    }

    private enum ToolCategory
    {
        Packages,
        Analysis,
        Processing,
        Export,
        Borehole
    }

    private class ToolEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Action<SeismicDataset> Draw { get; set; }
    }
}
