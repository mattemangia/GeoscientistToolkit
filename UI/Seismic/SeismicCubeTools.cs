// GeoscientistToolkit/UI/Seismic/SeismicCubeTools.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Seismic;

/// <summary>
/// Tools panel for seismic cube datasets - manage lines, intersections, normalization, and packages
/// </summary>
public class SeismicCubeTools : IDatasetTools
{
    private SeismicCubeViewer? _activeViewer;

    // Tool categories
    private enum ToolCategory
    {
        Lines,
        Intersections,
        Normalization,
        Packages,
        Export
    }

    private ToolCategory _selectedCategory = ToolCategory.Lines;

    // Line addition
    private string _newLineName = "New Line";
    private Vector3 _newLineStart = Vector3.Zero;
    private Vector3 _newLineEnd = new Vector3(1000, 0, 0);
    private float _newLineAzimuth = 0f;

    // Selected items
    private string? _selectedLineId;
    private string? _selectedIntersectionId;
    private string? _selectedPackageId;

    // Package creation
    private string _newPackageName = "New Package";
    private Vector4 _newPackageColor = new Vector4(1, 1, 0, 1);
    private string _newPackageLithology = "";

    // Export dialogs
    private readonly ImGuiExportFileDialog _exportDialog;
    private readonly ImGuiExportFileDialog _cubeExportDialog;
    private bool _showExportDialog = false;
    private bool _showCubeExportDialog = false;

    // Export options
    private int _exportCompressionLevel = 2;
    private bool _exportEmbedSeismicData = true;
    private bool _exportIncludeVolume = true;

    // Export progress
    private bool _isExporting = false;
    private float _exportProgress = 0f;
    private string _exportProgressMessage = "";
    private Task? _exportTask;
    private SeismicCubeDataset? _exportingDataset;

    public SeismicCubeTools()
    {
        _exportDialog = new ImGuiExportFileDialog("SeismicCubeImageExport", "Export Image");
        _exportDialog.SetExtensions((".png", "PNG Image"), (".jpg", "JPEG Image"));

        _cubeExportDialog = new ImGuiExportFileDialog("SeismicCubeExport", "Export Seismic Cube");
        _cubeExportDialog.SetExtensions(
            (SeismicCubeSerializer.FileExtension, "Seismic Cube File"),
            (".json", "JSON Project"));
    }

    public void SetViewer(SeismicCubeViewer viewer)
    {
        _activeViewer = viewer;
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not SeismicCubeDataset cubeDataset)
        {
            ImGui.TextDisabled("Invalid dataset type for seismic cube tools");
            return;
        }

        DrawCategorySelector();
        ImGui.Separator();

        switch (_selectedCategory)
        {
            case ToolCategory.Lines:
                DrawLinesTools(cubeDataset);
                break;
            case ToolCategory.Intersections:
                DrawIntersectionsTools(cubeDataset);
                break;
            case ToolCategory.Normalization:
                DrawNormalizationTools(cubeDataset);
                break;
            case ToolCategory.Packages:
                DrawPackagesTools(cubeDataset);
                break;
            case ToolCategory.Export:
                DrawExportTools(cubeDataset);
                break;
        }
    }

    private void DrawCategorySelector()
    {
        ImGui.Text("Category:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

        var categoryNames = Enum.GetNames<ToolCategory>();
        int selectedIndex = (int)_selectedCategory;
        if (ImGui.Combo("##CategorySelector", ref selectedIndex, categoryNames, categoryNames.Length))
        {
            _selectedCategory = (ToolCategory)selectedIndex;
        }
    }

    private void DrawLinesTools(SeismicCubeDataset dataset)
    {
        ImGui.Text("Seismic Lines");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Manage lines in the seismic cube");
        ImGui.Separator();

        // Line list
        if (ImGui.CollapsingHeader("Lines in Cube", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (dataset.Lines.Count == 0)
            {
                ImGui.TextDisabled("No lines in cube yet");
            }
            else
            {
                ImGui.BeginChild("LineList", new Vector2(0, 150), ImGuiChildFlags.Border);

                foreach (var line in dataset.Lines)
                {
                    ImGui.PushID(line.Id);

                    bool isSelected = _selectedLineId == line.Id;
                    bool isVisible = line.IsVisible;

                    if (ImGui.Checkbox("##visible", ref isVisible))
                    {
                        line.IsVisible = isVisible;
                        _activeViewer?.RequestRedraw();
                    }

                    ImGui.SameLine();
                    ImGui.ColorButton("##color", line.Color, ImGuiColorEditFlags.NoTooltip, new Vector2(20, 20));

                    ImGui.SameLine();
                    string label = line.IsPerpendicular ? $"{line.Name} (perp)" : line.Name;
                    if (ImGui.Selectable(label, isSelected))
                    {
                        _selectedLineId = line.Id;
                    }

                    ImGui.PopID();
                }

                ImGui.EndChild();
            }
        }

        // Selected line details
        if (_selectedLineId != null && ImGui.CollapsingHeader("Line Details"))
        {
            var line = dataset.Lines.FirstOrDefault(l => l.Id == _selectedLineId);
            if (line != null)
            {
                ImGui.Text($"Name: {line.Name}");
                ImGui.Text($"Traces: {line.SeismicData?.GetTraceCount() ?? 0}");
                ImGui.Text($"Start: ({line.Geometry.StartPoint.X:F1}, {line.Geometry.StartPoint.Y:F1})");
                ImGui.Text($"End: ({line.Geometry.EndPoint.X:F1}, {line.Geometry.EndPoint.Y:F1})");
                ImGui.Text($"Length: {line.Geometry.Length:F1} m");
                ImGui.Text($"Azimuth: {line.Geometry.Azimuth:F1}°");
                ImGui.Text($"Trace Spacing: {line.Geometry.TraceSpacing:F1} m");

                if (line.IsPerpendicular)
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "Perpendicular line");
                }

                ImGui.Spacing();

                Vector4 color = line.Color;
                if (ImGui.ColorEdit4("Line Color", ref color))
                {
                    line.Color = color;
                    _activeViewer?.RequestRedraw();
                }

                if (ImGui.Button("Remove Line"))
                {
                    dataset.RemoveLine(line.Id);
                    _selectedLineId = null;
                    _activeViewer?.RequestRedraw();
                }
            }
        }

        // Add perpendicular line
        if (_selectedLineId != null && ImGui.CollapsingHeader("Add Perpendicular Line"))
        {
            var baseLine = dataset.Lines.FirstOrDefault(l => l.Id == _selectedLineId);
            if (baseLine != null)
            {
                ImGui.Text($"Base line: {baseLine.Name}");

                int traceIndex = baseLine.SeismicData?.GetTraceCount() / 2 ?? 0;
                int maxTrace = baseLine.SeismicData?.GetTraceCount() - 1 ?? 0;
                ImGui.SliderInt("Trace Index", ref traceIndex, 0, maxTrace);

                ImGui.InputText("New Line Name", ref _newLineName, 256);

                if (ImGui.Button("Add Perpendicular Line"))
                {
                    // Note: In real implementation, you'd load a SEG-Y file here
                    ImGui.OpenPopup("LoadSegyForPerpendicular");
                }

                if (ImGui.BeginPopup("LoadSegyForPerpendicular"))
                {
                    ImGui.Text("Load a SEG-Y file to create perpendicular line");
                    ImGui.Text("(File browser would be shown here)");
                    ImGui.EndPopup();
                }
            }
        }
    }

    private void DrawIntersectionsTools(SeismicCubeDataset dataset)
    {
        ImGui.Text("Line Intersections");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "View and manage intersection points");
        ImGui.Separator();

        // Intersection list
        if (ImGui.CollapsingHeader("Detected Intersections", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (dataset.Intersections.Count == 0)
            {
                ImGui.TextDisabled("No intersections detected");
                if (ImGui.Button("Detect Intersections"))
                {
                    dataset.DetectIntersections();
                }
            }
            else
            {
                ImGui.BeginChild("IntersectionList", new Vector2(0, 200), ImGuiChildFlags.Border);

                foreach (var intersection in dataset.Intersections)
                {
                    ImGui.PushID(intersection.Id);

                    bool isSelected = _selectedIntersectionId == intersection.Id;

                    // Status indicator
                    if (intersection.NormalizationApplied)
                    {
                        ImGui.TextColored(new Vector4(0, 1, 0, 1), "[OK]");
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "[!]");
                    }

                    ImGui.SameLine();

                    string label = $"{intersection.Line1Name} x {intersection.Line2Name}";
                    if (intersection.IsPerpendicular)
                    {
                        label += " (90°)";
                    }
                    else
                    {
                        label += $" ({intersection.IntersectionAngle:F0}°)";
                    }

                    if (ImGui.Selectable(label, isSelected))
                    {
                        _selectedIntersectionId = intersection.Id;
                    }

                    ImGui.PopID();
                }

                ImGui.EndChild();
            }
        }

        // Selected intersection details
        if (_selectedIntersectionId != null && ImGui.CollapsingHeader("Intersection Details"))
        {
            var intersection = dataset.Intersections.FirstOrDefault(i => i.Id == _selectedIntersectionId);
            if (intersection != null)
            {
                ImGui.Text($"Line 1: {intersection.Line1Name} (trace {intersection.Line1TraceIndex})");
                ImGui.Text($"Line 2: {intersection.Line2Name} (trace {intersection.Line2TraceIndex})");
                ImGui.Text($"Position: ({intersection.IntersectionPoint.X:F1}, {intersection.IntersectionPoint.Y:F1})");
                ImGui.Text($"Angle: {intersection.IntersectionAngle:F1}°");

                ImGui.Separator();

                ImGui.Text("Mismatch Analysis:");
                ImGui.Text($"  Amplitude: {intersection.AmplitudeMismatch:F3}");
                ImGui.Text($"  Phase: {intersection.PhaseMismatch:F1} samples");
                ImGui.Text($"  Frequency: {intersection.FrequencyMismatch:F1} Hz");

                ImGui.Separator();

                if (intersection.NormalizationApplied)
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "Normalization Applied");
                    ImGui.Text($"Tie Quality: {intersection.TieQuality:F3}");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "Not Normalized");
                    if (ImGui.Button("Apply Normalization"))
                    {
                        var normalizer = new SeismicLineNormalizer(dataset.NormalizationSettings);
                        var line1 = dataset.Lines.FirstOrDefault(l => l.Id == intersection.Line1Id);
                        var line2 = dataset.Lines.FirstOrDefault(l => l.Id == intersection.Line2Id);

                        if (line1?.SeismicData != null && line2?.SeismicData != null)
                        {
                            var result = normalizer.NormalizeAtIntersection(
                                line1.SeismicData,
                                line2.SeismicData,
                                intersection.Line1TraceIndex,
                                intersection.Line2TraceIndex
                            );
                            intersection.NormalizationApplied = result.Success;
                            intersection.TieQuality = result.TieQuality;
                            _activeViewer?.RequestRedraw();
                        }
                    }
                }
            }
        }
    }

    private void DrawNormalizationTools(SeismicCubeDataset dataset)
    {
        ImGui.Text("Normalization Settings");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Configure line matching at intersections");
        ImGui.Separator();

        var settings = dataset.NormalizationSettings;

        if (ImGui.CollapsingHeader("Amplitude Normalization", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Checkbox("Enable Amplitude Normalization", ref settings.NormalizeAmplitude);

            if (settings.NormalizeAmplitude)
            {
                var methods = Enum.GetNames<AmplitudeNormalizationMethod>();
                int methodIndex = (int)settings.AmplitudeMethod;
                ImGui.SetNextItemWidth(150);
                if (ImGui.Combo("Method", ref methodIndex, methods, methods.Length))
                {
                    settings.AmplitudeMethod = (AmplitudeNormalizationMethod)methodIndex;
                }
            }
        }

        if (ImGui.CollapsingHeader("Frequency Matching", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Checkbox("Enable Frequency Matching", ref settings.MatchFrequency);

            if (settings.MatchFrequency)
            {
                ImGui.SetNextItemWidth(100);
                ImGui.InputFloat("Low Frequency (Hz)", ref settings.TargetFrequencyLow);
                ImGui.SetNextItemWidth(100);
                ImGui.InputFloat("High Frequency (Hz)", ref settings.TargetFrequencyHigh);
            }
        }

        if (ImGui.CollapsingHeader("Phase Matching"))
        {
            ImGui.Checkbox("Enable Phase Matching", ref settings.MatchPhase);
        }

        if (ImGui.CollapsingHeader("Transition Smoothing"))
        {
            ImGui.Checkbox("Enable Transition Smoothing", ref settings.SmoothTransitions);

            if (settings.SmoothTransitions)
            {
                ImGui.SetNextItemWidth(100);
                ImGui.InputInt("Transition Zone (traces)", ref settings.TransitionZoneTraces);
            }
        }

        if (ImGui.CollapsingHeader("Matching Window"))
        {
            ImGui.SetNextItemWidth(100);
            ImGui.InputInt("Window Size (traces)", ref settings.MatchingWindowTraces);
            ImGui.SetNextItemWidth(100);
            ImGui.InputFloat("Window Time (ms)", ref settings.MatchingWindowMs);
        }

        ImGui.Separator();

        if (ImGui.Button("Apply to All Intersections", new Vector2(-1, 0)))
        {
            dataset.ApplyNormalization();
            _activeViewer?.RequestRedraw();
        }

        // Summary
        int normalized = dataset.Intersections.Count(i => i.NormalizationApplied);
        int total = dataset.Intersections.Count;
        ImGui.Text($"Normalized: {normalized}/{total} intersections");

        if (normalized > 0)
        {
            float avgQuality = dataset.Intersections
                .Where(i => i.NormalizationApplied)
                .Average(i => i.TieQuality);
            ImGui.Text($"Average Tie Quality: {avgQuality:F3}");
        }
    }

    private void DrawPackagesTools(SeismicCubeDataset dataset)
    {
        ImGui.Text("Seismic Packages");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Define and manage seismic horizons/packages");
        ImGui.Separator();

        // Package list
        if (ImGui.CollapsingHeader("Packages", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (dataset.Packages.Count == 0)
            {
                ImGui.TextDisabled("No packages defined");
            }
            else
            {
                ImGui.BeginChild("PackageList", new Vector2(0, 150), ImGuiChildFlags.Border);

                foreach (var package in dataset.Packages)
                {
                    ImGui.PushID(package.Id);

                    bool isVisible = package.IsVisible;
                    if (ImGui.Checkbox("##visible", ref isVisible))
                    {
                        package.IsVisible = isVisible;
                        _activeViewer?.RequestRedraw();
                    }

                    ImGui.SameLine();
                    ImGui.ColorButton("##color", package.Color, ImGuiColorEditFlags.NoTooltip, new Vector2(20, 20));

                    ImGui.SameLine();
                    if (ImGui.Selectable(package.Name, _selectedPackageId == package.Id))
                    {
                        _selectedPackageId = package.Id;
                    }

                    ImGui.PopID();
                }

                ImGui.EndChild();
            }
        }

        // Create new package
        if (ImGui.CollapsingHeader("Create Package"))
        {
            ImGui.InputText("Name", ref _newPackageName, 256);
            ImGui.ColorEdit4("Color", ref _newPackageColor);
            ImGui.InputText("Lithology", ref _newPackageLithology, 256);

            if (ImGui.Button("Create Package"))
            {
                var package = new SeismicCubePackage
                {
                    Name = _newPackageName,
                    Color = _newPackageColor,
                    LithologyType = _newPackageLithology
                };
                dataset.AddPackage(package);
                _newPackageName = $"Package {dataset.Packages.Count + 1}";
                _activeViewer?.RequestRedraw();
            }
        }

        // Selected package details
        if (_selectedPackageId != null && ImGui.CollapsingHeader("Package Details"))
        {
            var package = dataset.Packages.FirstOrDefault(p => p.Id == _selectedPackageId);
            if (package != null)
            {
                string name = package.Name;
                if (ImGui.InputText("Name##edit", ref name, 256))
                {
                    package.Name = name;
                }

                Vector4 color = package.Color;
                if (ImGui.ColorEdit4("Color##edit", ref color))
                {
                    package.Color = color;
                    _activeViewer?.RequestRedraw();
                }

                string description = package.Description;
                if (ImGui.InputTextMultiline("Description", ref description, 1024, new Vector2(-1, 60)))
                {
                    package.Description = description;
                }

                string lithology = package.LithologyType;
                if (ImGui.InputText("Lithology##edit", ref lithology, 256))
                {
                    package.LithologyType = lithology;
                }

                string facies = package.SeismicFacies;
                if (ImGui.InputText("Seismic Facies", ref facies, 256))
                {
                    package.SeismicFacies = facies;
                }

                float confidence = package.Confidence;
                if (ImGui.SliderFloat("Confidence", ref confidence, 0, 1))
                {
                    package.Confidence = confidence;
                }

                ImGui.Text($"Horizon Points: {package.HorizonPoints.Count}");

                ImGui.Spacing();

                if (ImGui.Button("Delete Package"))
                {
                    dataset.Packages.Remove(package);
                    _selectedPackageId = null;
                    _activeViewer?.RequestRedraw();
                }
            }
        }
    }

    private void DrawExportTools(SeismicCubeDataset dataset)
    {
        ImGui.Text("Export Options");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Export cube data and generate GIS maps");
        ImGui.Separator();

        // Export progress indicator
        if (_isExporting)
        {
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.2f, 0.6f, 1.0f, 1.0f));
            ImGui.ProgressBar(_exportProgress, new Vector2(-1, 0), _exportProgressMessage);
            ImGui.PopStyleColor();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Export in progress...");

            // Check if export completed
            if (_exportTask?.IsCompleted == true)
            {
                _isExporting = false;
                _exportTask = null;
                _exportingDataset = null;
                Logger.Log("[SeismicCubeTools] Export completed!");
            }

            ImGui.Separator();
        }

        // Build volume
        if (ImGui.CollapsingHeader("Volume Building", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (dataset.RegularizedVolume == null)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Regularized volume not built");
                if (ImGui.Button("Build Regularized Volume", new Vector2(-1, 0)))
                {
                    dataset.BuildRegularizedVolume();
                    _activeViewer?.RequestRedraw();
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Regularized volume ready");
                var grid = dataset.GridParameters;
                ImGui.Text($"Size: {grid.InlineCount} x {grid.CrosslineCount} x {grid.SampleCount}");
                ImGui.Text($"Spacing: {grid.InlineSpacing}m x {grid.CrosslineSpacing}m x {grid.SampleInterval}ms");
            }
        }

        // Export Seismic Cube (.seiscube)
        if (ImGui.CollapsingHeader("Export Seismic Cube", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Export the seismic cube to a compressed .seiscube file for sharing or backup.");

            // Export options
            ImGui.Separator();
            ImGui.Text("Export Options:");

            string[] compressionLevels = { "None", "Fast", "Optimal", "Maximum" };
            ImGui.SetNextItemWidth(120);
            ImGui.Combo("Compression", ref _exportCompressionLevel, compressionLevels, compressionLevels.Length);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Higher compression reduces file size but takes longer");

            ImGui.Checkbox("Embed seismic data", ref _exportEmbedSeismicData);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Include SEG-Y trace data in the file (larger but self-contained)");

            ImGui.Checkbox("Include regularized volume", ref _exportIncludeVolume);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Include the 3D interpolated volume (can be rebuilt later)");

            // Size estimate
            long estimatedSize = EstimateExportSize(dataset);
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"Estimated size: {FormatBytes(estimatedSize)}");

            ImGui.Spacing();

            // Export button
            bool canExport = !_isExporting && dataset.Lines.Count > 0;
            if (!canExport)
                ImGui.BeginDisabled();

            if (ImGui.Button("Export Seismic Cube...", new Vector2(-1, 0)))
            {
                _showCubeExportDialog = true;
                _cubeExportDialog.Open();
            }

            if (!canExport)
                ImGui.EndDisabled();

            if (dataset.Lines.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Add seismic lines before exporting");
            }
        }

        // Export to GIS
        if (ImGui.CollapsingHeader("Export to Subsurface GIS"))
        {
            ImGui.TextWrapped("Generate a subsurface GIS dataset from the seismic cube packages.");

            if (dataset.Packages.Count == 0)
            {
                ImGui.TextDisabled("Define packages first to export to GIS");
            }
            else
            {
                if (ImGui.Button("Generate Subsurface GIS Map", new Vector2(-1, 0)))
                {
                    try
                    {
                        var gisDataset = SeismicCubeGISExporter.ExportToSubsurfaceGIS(dataset);
                        Logger.Log($"[SeismicCubeTools] Generated subsurface GIS map with {gisDataset.LayerBoundaries.Count} layers");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[SeismicCubeTools] GIS export failed: {ex.Message}");
                    }
                }
            }
        }

        // Export time slices
        if (ImGui.CollapsingHeader("Export Time Slices"))
        {
            ImGui.Text("Export time slices as images");

            if (ImGui.Button("Export Current Slice as Image"))
            {
                _showExportDialog = true;
                _exportDialog.Open();
            }

            if (ImGui.Button("Export All Slices"))
            {
                Logger.Log("[SeismicCubeTools] Exporting all time slices...");
            }
        }

        // Handle cube export dialog
        if (_showCubeExportDialog)
        {
            if (_cubeExportDialog.Submit())
            {
                var outputPath = _cubeExportDialog.SelectedPath;
                if (!outputPath.EndsWith(SeismicCubeSerializer.FileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    outputPath += SeismicCubeSerializer.FileExtension;
                }

                StartCubeExport(dataset, outputPath);
                _showCubeExportDialog = false;
            }
            if (!_cubeExportDialog.IsOpen)
            {
                _showCubeExportDialog = false;
            }
        }

        // Handle image export dialog
        if (_showExportDialog)
        {
            if (_exportDialog.Submit())
            {
                Logger.Log($"[SeismicCubeTools] Exporting image to: {_exportDialog.SelectedPath}");
                _showExportDialog = false;
            }
            if (!_exportDialog.IsOpen)
            {
                _showExportDialog = false;
            }
        }
    }

    private void StartCubeExport(SeismicCubeDataset dataset, string outputPath)
    {
        if (_isExporting) return;

        _isExporting = true;
        _exportProgress = 0f;
        _exportProgressMessage = "Initializing export...";
        _exportingDataset = dataset;

        var options = new SeismicCubeExportOptions
        {
            CompressionLevel = _exportCompressionLevel,
            EmbedSeismicData = _exportEmbedSeismicData,
            IncludeRegularizedVolume = _exportIncludeVolume
        };

        var progress = new Progress<(float progress, string message)>(update =>
        {
            _exportProgress = update.progress;
            _exportProgressMessage = update.message;
        });

        _exportTask = Task.Run(async () =>
        {
            try
            {
                await SeismicCubeSerializer.ExportAsync(dataset, outputPath, options, progress);
                Logger.Log($"[SeismicCubeTools] Successfully exported cube to: {outputPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SeismicCubeTools] Export failed: {ex.Message}");
                _exportProgressMessage = $"Error: {ex.Message}";
            }
        });
    }

    private long EstimateExportSize(SeismicCubeDataset dataset)
    {
        long size = 1024; // Header and metadata

        // Line data
        foreach (var line in dataset.Lines)
        {
            if (_exportEmbedSeismicData && line.SeismicData != null)
            {
                size += line.SeismicData.GetSizeInBytes();
            }
            else
            {
                size += 512; // Reference only
            }
        }

        // Volume data
        if (_exportIncludeVolume && dataset.RegularizedVolume != null)
        {
            var grid = dataset.GridParameters;
            size += (long)grid.InlineCount * grid.CrosslineCount * grid.SampleCount * 2; // 16-bit
        }

        // Apply compression estimate
        float compressionRatio = _exportCompressionLevel switch
        {
            0 => 1.0f,
            1 => 0.7f,
            2 => 0.5f,
            _ => 0.3f
        };

        return (long)(size * compressionRatio);
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
