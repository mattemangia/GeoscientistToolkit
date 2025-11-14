// GeoscientistToolkit/UI/Seismic/SeismicTools.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Seismic;

/// <summary>
/// Tools panel for seismic datasets - manage line packages, analysis, and processing
/// </summary>
public class SeismicTools : IDatasetTools
{
    private SeismicViewer? _activeViewer;

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

        ImGui.Text($"Seismic Line Package Manager");
        ImGui.Separator();

        // Show dataset info
        ImGui.Text($"Total Traces: {seismicDataset.GetTraceCount()}");
        ImGui.Text($"Packages: {seismicDataset.LinePackages.Count}");
        ImGui.Spacing();

        // Package list
        DrawPackageList(seismicDataset);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Package creation from selection
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
                    _newPackageName = $"Package {seismicDataset.LinePackages.Count + 1}";
                }

                ImGui.Spacing();
            }
        }

        // Manual package creation
        DrawPackageCreation(seismicDataset);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Auto-detection tools
        DrawAutoDetectionTools(seismicDataset);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Package editor
        if (_selectedPackage != null)
        {
            DrawPackageEditor(seismicDataset);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Analysis tools
        DrawAnalysisTools(seismicDataset);
    }

    private void DrawPackageList(SeismicDataset dataset)
    {
        ImGui.Text("Line Packages:");

        if (dataset.LinePackages.Count == 0)
        {
            ImGui.TextDisabled("  No packages created yet");
            return;
        }

        ImGui.BeginChild("PackageList", new Vector2(0, 200), true);

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
}
