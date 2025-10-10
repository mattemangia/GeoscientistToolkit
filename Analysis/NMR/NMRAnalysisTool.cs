// GeoscientistToolkit/Analysis/NMR/NMRAnalysisTool.cs

using System.Data;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.NMR;

public class NMRAnalysisTool : IDatasetTools
{
    private readonly NMRSimulationConfig _config = new();
    private readonly ImGuiExportFileDialog _exportDialog = new("NMRExportDialog", "Export NMR Data");

    // GPU support
    private readonly bool _gpuAvailable;
    private readonly Dictionary<byte, bool> _materialExpanded = new();
    private LabDataCalibration.CalibrationResults _calibrationResults;
    private CancellationTokenSource _cancellationSource;

    private CtImageStackDataset _currentDataset;

    // Advanced features
    private DiffusionEditing.DiffusionResults _diffusionResults;
    private bool _isSimulating;
    private LabDataCalibration.LabNMRData _labData;

    private NMRResults _lastResults;
    private MultiComponentFitting.FittingResults _peakFittingResults;
    private Material _selectedPoreMaterial;
    private int _selectedPoreMaterialIndex;

    // Fluid presets
    private string _selectedPreset = "Water (25°C)";
    private int _selectedPresetIndex;

    // Visualization
    private int _selectedVisualization; // 0=Decay, 1=T2 Spectrum, 2=Pore Size, 3=T1-T2 Map
    private float _simulationProgress;
    private string _simulationStatus = "";

    public NMRAnalysisTool()
    {
        _exportDialog.SetExtensions(
            (".png", "PNG Image"),
            (".csv", "CSV Data"),
            (".txt", "Text Report")
        );

        // Check GPU availability
        try
        {
            var cl = CL.GetApi();
            uint numPlatforms = 0;
            unsafe
            {
                cl.GetPlatformIDs(0, null, &numPlatforms);
            }

            _gpuAvailable = numPlatforms > 0;

            if (_gpuAvailable)
                Logger.Log("[NMRAnalysisTool] OpenCL GPU available");
            else
                Logger.LogWarning("[NMRAnalysisTool] No OpenCL GPU found, using CPU");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[NMRAnalysisTool] GPU detection failed: {ex.Message}");
            _gpuAvailable = false;
        }
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ctDataset)
        {
            ImGui.TextDisabled("NMR Analysis requires a CT Image Stack dataset.");
            return;
        }

        if (_currentDataset != ctDataset)
        {
            _currentDataset = ctDataset;
            _selectedPoreMaterial = null;
            _lastResults = null;
            InitializeDefaultConfig(ctDataset);
        }

        DrawConfigurationPanel();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawSimulationControls();

        if (_lastResults != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawResultsPanel();
        }

        // Handle export dialog
        if (_exportDialog.Submit()) HandleExport(_exportDialog.SelectedPath);
    }

    private void InitializeDefaultConfig(CtImageStackDataset dataset)
    {
        _config.VoxelSize = dataset.PixelSize * 1e-6; // Convert μm to m
        _config.MaterialRelaxivities.Clear();

        // Add default relaxivities for all materials
        foreach (var material in dataset.Materials)
            if (material.ID != 0) // Skip exterior
                _config.MaterialRelaxivities[material.ID] = new MaterialRelaxivityConfig
                {
                    MaterialName = material.Name,
                    SurfaceRelaxivity = 10.0, // Default value
                    Color = material.Color
                };
    }

    private void DrawConfigurationPanel()
    {
        ImGui.SeparatorText("NMR Simulation Configuration");

        // Fluid presets
        ImGui.Text("Fluid Preset:");
        ImGui.SetNextItemWidth(-1);
        var presetNames = FluidPresets.Presets.Keys.ToArray();
        if (ImGui.Combo("##FluidPreset", ref _selectedPresetIndex, presetNames, presetNames.Length))
        {
            _selectedPreset = presetNames[_selectedPresetIndex];
            if (_currentDataset != null) FluidPresets.ApplyPreset(_config, _selectedPreset, _currentDataset);
        }

        if (FluidPresets.Presets.TryGetValue(_selectedPreset, out var preset))
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), preset.Description);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Pore material selection
        var materials = _currentDataset.Materials.Where(m => m.ID != 0).ToList();
        if (materials.Count == 0)
        {
            ImGui.TextDisabled("No materials defined. Create materials first.");
            return;
        }

        ImGui.Text("Pore Space Material:");
        ImGui.SetNextItemWidth(-1);
        var materialNames = materials.Select(m => m.Name).ToArray();
        if (_selectedPoreMaterialIndex >= materialNames.Length) _selectedPoreMaterialIndex = 0;

        if (ImGui.Combo("##PoreMaterial", ref _selectedPoreMaterialIndex, materialNames, materialNames.Length))
        {
            _selectedPoreMaterial = materials[_selectedPoreMaterialIndex];
            _config.PoreMaterialID = _selectedPoreMaterial.ID;
        }

        _selectedPoreMaterial ??= materials.FirstOrDefault();
        if (_selectedPoreMaterial != null) _config.PoreMaterialID = _selectedPoreMaterial.ID;

        ImGui.Spacing();

        // Simulation parameters
        if (ImGui.CollapsingHeader("Simulation Parameters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var walkers = _config.NumberOfWalkers;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt("Number of Walkers", ref walkers, 1000, 100000))
                _config.NumberOfWalkers = walkers;

            var steps = _config.NumberOfSteps;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt("Time Steps", ref steps, 100, 5000))
                _config.NumberOfSteps = steps;

            var timeStep = (float)_config.TimeStepMs;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat("Time Step (ms)", ref timeStep, 0.001f, 0.001f, 1.0f, "%.3f"))
                _config.TimeStepMs = timeStep;

            var diffusion = (float)(_config.DiffusionCoefficient * 1e9);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat("Diffusion Coefficient (×10⁻⁹ m²/s)", ref diffusion, 0.1f, 0.1f, 10.0f))
                _config.DiffusionCoefficient = diffusion * 1e-9;

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Water at 25°C: ~2.0×10⁻⁹ m²/s");
        }

        ImGui.Spacing();

        // Material relaxivities
        if (ImGui.CollapsingHeader("Material Surface Relaxivities", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped(
                "Surface relaxivity controls how quickly magnetization decays when random walkers hit material surfaces.");
            ImGui.Spacing();

            foreach (var matConfig in _config.MaterialRelaxivities.Values.ToList())
            {
                var materialID = _config.MaterialRelaxivities.First(kvp => kvp.Value == matConfig).Key;

                if (materialID == _config.PoreMaterialID)
                    continue; // Skip pore material

                if (!_materialExpanded.ContainsKey(materialID))
                    _materialExpanded[materialID] = false;

                // Material color indicator
                ImGui.PushStyleColor(ImGuiCol.Text, matConfig.Color);
                ImGui.Text("■");
                ImGui.PopStyleColor();
                ImGui.SameLine();

                _materialExpanded[materialID] = ImGui.TreeNode($"{matConfig.MaterialName}##Mat{materialID}");

                if (_materialExpanded[materialID])
                {
                    var relaxivity = (float)matConfig.SurfaceRelaxivity;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.DragFloat($"Relaxivity (μm/s)##Relax{materialID}", ref relaxivity, 0.1f, 0.1f, 1000.0f))
                        matConfig.SurfaceRelaxivity = relaxivity;

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Typical values: Quartz: 1-10 μm/s, Clay: 10-100 μm/s");

                    ImGui.TreePop();
                }
            }
        }

        ImGui.Spacing();

        // Advanced settings
        if (ImGui.CollapsingHeader("Advanced Settings"))
        {
            // GPU toggle
            if (_gpuAvailable)
            {
                var configUseOpenCl = _config.UseOpenCL;
                ImGui.Checkbox("Use GPU (OpenCL)", ref configUseOpenCl);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("GPU acceleration provides 10-100× speedup");
            }
            else
            {
                ImGui.BeginDisabled();
                var gpuDisabled = false;
                ImGui.Checkbox("Use GPU (OpenCL)", ref gpuDisabled);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("GPU not available - check OpenCL drivers");
            }

            ImGui.Spacing();

            // T1-T2 2D NMR
            var configComputeT1T2Map = _config.ComputeT1T2Map;
            ImGui.Checkbox("Compute T1-T2 Correlation Map", ref configComputeT1T2Map);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("2D NMR map for fluid identification (adds processing time)");

            if (configComputeT1T2Map)
            {
                ImGui.Indent();

                var t1t2Ratio = (float)_config.T1T2Ratio;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderFloat("T1/T2 Ratio", ref t1t2Ratio, 1.0f, 5.0f, "%.1f"))
                    _config.T1T2Ratio = t1t2Ratio;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Typical: 1.5 for water-saturated, 2-3 for oil");

                var t1Bins = _config.T1BinCount;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderInt("T1 Bins", ref t1Bins, 16, 128))
                    _config.T1BinCount = t1Bins;

                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var t2Bins = _config.T2BinCount;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt("T2 Histogram Bins", ref t2Bins, 16, 128))
                _config.T2BinCount = t2Bins;

            var t2Min = (float)_config.T2MinMs;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat("T2 Min (ms)", ref t2Min, 0.01f, 0.01f, 10.0f, "%.2f"))
                _config.T2MinMs = t2Min;

            var t2Max = (float)_config.T2MaxMs;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat("T2 Max (ms)", ref t2Max, 10f, 100f, 100000f, "%.0f"))
                _config.T2MaxMs = t2Max;

            var seed = _config.RandomSeed;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt("Random Seed", ref seed))
                _config.RandomSeed = seed;
        }
    }

    private void DrawSimulationControls()
    {
        ImGui.SeparatorText("Simulation");

        if (_selectedPoreMaterial == null)
        {
            ImGui.TextDisabled("Select a pore material to run simulation.");
            return;
        }

        if (_isSimulating)
        {
            ImGui.ProgressBar(_simulationProgress, new Vector2(-1, 0));
            ImGui.TextWrapped(_simulationStatus);

            if (ImGui.Button("Cancel", new Vector2(-1, 0)))
            {
                _cancellationSource?.Cancel();
                _simulationStatus = "Cancelling...";
            }
        }
        else
        {
            var estimatedTime = EstimateComputationTime();
            var method = _config.UseOpenCL && _gpuAvailable ? "GPU (OpenCL)" : "CPU (SIMD)";
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1),
                $"Method: {method} | Estimated time: {estimatedTime:F1}s");

            if (ImGui.Button("Run NMR Simulation", new Vector2(-1, 0))) _ = RunSimulationAsync();
        }
    }

    private void DrawResultsPanel()
    {
        ImGui.SeparatorText("NMR Analysis Results");

        // Statistics summary
        if (ImGui.CollapsingHeader("Statistics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Computation Time: {_lastResults.ComputationTime.TotalSeconds:F2}s");
            ImGui.Text($"Method: {_lastResults.ComputationMethod}");
            ImGui.Text($"Walkers: {_lastResults.NumberOfWalkers:N0}");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text($"Mean T2: {_lastResults.MeanT2:F2} ms");
            ImGui.Text($"Geometric Mean T2: {_lastResults.GeometricMeanT2:F2} ms");
            ImGui.Text($"Peak T2: {_lastResults.T2PeakValue:F2} ms");
        }

        ImGui.Spacing();

        // Visualization selector
        ImGui.SeparatorText("Visualization");

        var vizOptions = _lastResults.HasT1T2Data
            ? new[] { "Decay Curve", "T2 Distribution", "Pore Size Distribution", "T1-T2 Map" }
            : new[] { "Decay Curve", "T2 Distribution", "Pore Size Distribution" };

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##VizSelector", ref _selectedVisualization, vizOptions, vizOptions.Length);

        ImGui.Spacing();

        // Draw selected visualization
        var plotSize = new Vector2(-1, 300);

        switch (_selectedVisualization)
        {
            case 0:
                DrawDecayCurve(plotSize);
                break;
            case 1:
                DrawT2Distribution(plotSize);
                break;
            case 2:
                DrawPoreSizeDistribution(plotSize);
                break;
            case 3:
                if (_lastResults.HasT1T2Data) DrawT1T2Map(plotSize);
                break;
        }

        ImGui.Spacing();

        // Export controls
        DrawExportControls();
    }

    private void DrawDecayCurve(Vector2 size)
    {
        if (ImGui.BeginChild("##DecayPlot", size, ImGuiChildFlags.Border))
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if (contentSize.X > 50 && contentSize.Y > 50)
                DrawPlot(drawList, pos, contentSize,
                    _lastResults.TimePoints,
                    _lastResults.Magnetization,
                    "Time (ms)",
                    "Magnetization",
                    new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        }

        ImGui.EndChild();
    }

    private void DrawT2Distribution(Vector2 size)
    {
        if (ImGui.BeginChild("##T2Plot", size, ImGuiChildFlags.Border))
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if (contentSize.X > 50 && contentSize.Y > 50)
                DrawHistogram(drawList, pos, contentSize,
                    _lastResults.T2HistogramBins,
                    _lastResults.T2Histogram,
                    "T2 (ms)",
                    "Amplitude",
                    new Vector4(0.3f, 0.6f, 0.9f, 1.0f),
                    true); // Use log scale for X axis
        }

        ImGui.EndChild();
    }

    private void DrawPoreSizeDistribution(Vector2 size)
    {
        if (ImGui.BeginChild("##PoreSizePlot", size, ImGuiChildFlags.Border))
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if (contentSize.X > 50 && contentSize.Y > 50 && _lastResults.PoreSizes != null)
                DrawHistogram(drawList, pos, contentSize,
                    _lastResults.PoreSizes,
                    _lastResults.PoreSizeDistribution,
                    "Pore Radius (μm)",
                    "Frequency",
                    new Vector4(0.9f, 0.5f, 0.2f, 1.0f),
                    true); // Use log scale
        }

        ImGui.EndChild();
    }

    private void DrawT1T2Map(Vector2 size)
    {
        if (ImGui.BeginChild("##T1T2Plot", size, ImGuiChildFlags.Border))
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if (contentSize.X > 50 && contentSize.Y > 50 && _lastResults.HasT1T2Data)
                Draw2DMap(drawList, pos, contentSize, _lastResults.T1T2Map,
                    _lastResults.T1HistogramBins, _lastResults.T2HistogramBins,
                    "T2 (ms)", "T1 (ms)");
        }

        ImGui.EndChild();
    }

    private void Draw2DMap(ImDrawListPtr drawList, Vector2 pos, Vector2 size,
        double[,] map, double[] xBins, double[] yBins, string xLabel, string yLabel)
    {
        var padding = 50f;
        var plotArea = new Vector2(size.X - padding * 2, size.Y - padding * 2);
        var plotPos = new Vector2(pos.X + padding, pos.Y + padding);

        // Background
        drawList.AddRectFilled(pos, new Vector2(pos.X + size.X, pos.Y + size.Y),
            ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));

        if (map == null || xBins == null || yBins == null) return;

        var t1Count = map.GetLength(0);
        var t2Count = map.GetLength(1);

        // Find max amplitude
        var maxAmplitude = 0.0;
        for (var i = 0; i < t1Count; i++)
        for (var j = 0; j < t2Count; j++)
            maxAmplitude = Math.Max(maxAmplitude, map[i, j]);

        if (maxAmplitude < 1e-10) return;

        // Draw 2D map
        var pixelWidth = plotArea.X / t2Count;
        var pixelHeight = plotArea.Y / t1Count;

        for (var i = 0; i < t1Count; i++)
        for (var j = 0; j < t2Count; j++)
        {
            var amplitude = map[i, j];
            if (amplitude < 1e-10) continue;

            var normalized = amplitude / maxAmplitude;
            var (r, g, b) = GetHotColor(normalized);

            var px = plotPos.X + j * pixelWidth;
            var py = plotPos.Y + (t1Count - 1 - i) * pixelHeight;

            drawList.AddRectFilled(
                new Vector2(px, py),
                new Vector2(px + pixelWidth, py + pixelHeight),
                ImGui.GetColorU32(new Vector4(r / 255f, g / 255f, b / 255f, 1.0f)));
        }

        // Draw axes
        var axisColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
        drawList.AddLine(plotPos, new Vector2(plotPos.X, plotPos.Y + plotArea.Y), axisColor, 2f);
        drawList.AddLine(new Vector2(plotPos.X, plotPos.Y + plotArea.Y),
            new Vector2(plotPos.X + plotArea.X, plotPos.Y + plotArea.Y), axisColor, 2f);

        // Labels
        var textColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
        drawList.AddText(new Vector2(pos.X + size.X / 2 - 30, pos.Y + size.Y - 20), textColor, xLabel);
        drawList.AddText(new Vector2(pos.X + 5, pos.Y + 5), textColor, yLabel);
    }

    private (byte, byte, byte) GetHotColor(double value)
    {
        value = Math.Clamp(value, 0, 1);

        if (value < 0.33)
        {
            var t = value / 0.33;
            return ((byte)(t * 255), 0, 0);
        }

        if (value < 0.66)
        {
            var t = (value - 0.33) / 0.33;
            return (255, (byte)(t * 255), 0);
        }
        else
        {
            var t = (value - 0.66) / 0.34;
            return (255, 255, (byte)(t * 255));
        }
    }

    private void DrawPlot(ImDrawListPtr drawList, Vector2 pos, Vector2 size,
        double[] xData, double[] yData, string xLabel, string yLabel, Vector4 color)
    {
        var padding = 50f;
        var plotArea = new Vector2(size.X - padding * 2, size.Y - padding * 2);
        var plotPos = new Vector2(pos.X + padding, pos.Y + padding);

        // Background
        drawList.AddRectFilled(pos, new Vector2(pos.X + size.X, pos.Y + size.Y),
            ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));

        if (xData == null || yData == null || xData.Length == 0) return;

        // Find data range
        var xMin = xData.Min();
        var xMax = xData.Max();
        var yMin = yData.Min();
        var yMax = yData.Max();

        // Draw axes
        var axisColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
        drawList.AddLine(plotPos, new Vector2(plotPos.X, plotPos.Y + plotArea.Y), axisColor, 2f);
        drawList.AddLine(new Vector2(plotPos.X, plotPos.Y + plotArea.Y),
            new Vector2(plotPos.X + plotArea.X, plotPos.Y + plotArea.Y), axisColor, 2f);

        // Draw data
        var lineColor = ImGui.GetColorU32(color);
        for (var i = 0; i < xData.Length - 1; i++)
        {
            var x1 = plotPos.X + (float)((xData[i] - xMin) / (xMax - xMin)) * plotArea.X;
            var y1 = plotPos.Y + plotArea.Y - (float)((yData[i] - yMin) / (yMax - yMin)) * plotArea.Y;
            var x2 = plotPos.X + (float)((xData[i + 1] - xMin) / (xMax - xMin)) * plotArea.X;
            var y2 = plotPos.Y + plotArea.Y - (float)((yData[i + 1] - yMin) / (yMax - yMin)) * plotArea.Y;

            drawList.AddLine(new Vector2(x1, y1), new Vector2(x2, y2), lineColor, 2f);
        }

        // Labels
        var textColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
        drawList.AddText(new Vector2(pos.X + size.X / 2 - 30, pos.Y + size.Y - 20), textColor, xLabel);
        // Rotated Y label would require more complex rendering
        drawList.AddText(new Vector2(pos.X + 5, pos.Y + 5), textColor, yLabel);
    }

    private void DrawHistogram(ImDrawListPtr drawList, Vector2 pos, Vector2 size,
        double[] bins, double[] values, string xLabel, string yLabel, Vector4 color, bool logScaleX)
    {
        var padding = 50f;
        var plotArea = new Vector2(size.X - padding * 2, size.Y - padding * 2);
        var plotPos = new Vector2(pos.X + padding, pos.Y + padding);

        // Background
        drawList.AddRectFilled(pos, new Vector2(pos.X + size.X, pos.Y + size.Y),
            ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));

        if (bins == null || values == null || bins.Length == 0) return;

        // Transform bins to log scale if requested
        var xData = logScaleX ? bins.Select(Math.Log10).ToArray() : bins;
        var xMin = xData.Min();
        var xMax = xData.Max();
        var yMin = 0.0;
        var yMax = values.Max();

        // Draw axes
        var axisColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
        drawList.AddLine(plotPos, new Vector2(plotPos.X, plotPos.Y + plotArea.Y), axisColor, 2f);
        drawList.AddLine(new Vector2(plotPos.X, plotPos.Y + plotArea.Y),
            new Vector2(plotPos.X + plotArea.X, plotPos.Y + plotArea.Y), axisColor, 2f);

        // Draw bars
        var barColor = ImGui.GetColorU32(color);
        var barWidth = plotArea.X / bins.Length * 0.8f;

        for (var i = 0; i < bins.Length; i++)
        {
            var x = plotPos.X + (float)((xData[i] - xMin) / (xMax - xMin)) * plotArea.X;
            var y = plotPos.Y + plotArea.Y - (float)((values[i] - yMin) / (yMax - yMin)) * plotArea.Y;
            var barHeight = plotArea.Y - (y - plotPos.Y);

            drawList.AddRectFilled(
                new Vector2(x - barWidth / 2, y),
                new Vector2(x + barWidth / 2, plotPos.Y + plotArea.Y),
                barColor);
        }

        // Labels
        var textColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
        drawList.AddText(new Vector2(pos.X + size.X / 2 - 30, pos.Y + size.Y - 20), textColor, xLabel);
        drawList.AddText(new Vector2(pos.X + 5, pos.Y + 5), textColor, yLabel);
    }

    private void DrawExportControls()
    {
        ImGui.SeparatorText("Export");

        if (ImGui.Button("Export Decay Curve (PNG)", new Vector2(-1, 0)))
        {
            _selectedVisualization = 0;
            _exportDialog.Open("nmr_decay.png");
        }

        if (ImGui.Button("Export T2 Distribution (PNG)", new Vector2(-1, 0)))
        {
            _selectedVisualization = 1;
            _exportDialog.Open("nmr_t2_distribution.png");
        }

        if (ImGui.Button("Export Pore Size Distribution (PNG)", new Vector2(-1, 0)))
        {
            _selectedVisualization = 2;
            _exportDialog.Open("nmr_pore_size.png");
        }

        if (_lastResults.HasT1T2Data)
        {
            if (ImGui.Button("Export T1-T2 Map (PNG)", new Vector2(-1, 0)))
            {
                _selectedVisualization = 3;
                _exportDialog.Open("nmr_t1t2_map.png");
            }

            if (ImGui.Button("Export T1-T2 Map (CSV)", new Vector2(-1, 0))) _exportDialog.Open("nmr_t1t2_map.csv");
        }

        if (ImGui.Button("Export All Data (CSV)", new Vector2(-1, 0))) _exportDialog.Open("nmr_results.csv");

        if (ImGui.Button("Import as Table Dataset", new Vector2(-1, 0))) ImportAsTableDataset();
    }

    private double EstimateComputationTime()
    {
        // Rough estimate based on walkers and steps
        var operations = (long)_config.NumberOfWalkers * _config.NumberOfSteps;

        if (_config.UseOpenCL && _gpuAvailable)
        {
            // GPU is much faster
            var opsPerSecond = 200_000_000.0; // GPU performance
            return operations / opsPerSecond;
        }
        else
        {
            // CPU with SIMD
            var opsPerSecond = 10_000_000.0;
            return operations / opsPerSecond;
        }
    }

    private async Task RunSimulationAsync()
    {
        _isSimulating = true;
        _simulationStatus = "Starting simulation...";
        _simulationProgress = 0f;
        _cancellationSource = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(float, string)>(update =>
            {
                _simulationProgress = update.Item1;
                _simulationStatus = update.Item2;
            });

            if (_config.UseOpenCL && _gpuAvailable)
            {
                // ========================= START OF CHANGED SNIPPET =========================
                // GPU path: Convert the callback-based method to an awaitable Task.

                var tcs = new TaskCompletionSource<NMRResults>();

                // Define the actions that will complete the task.
                Action<NMRResults> onSuccess = results => tcs.TrySetResult(results);
                Action<Exception> onError = ex => tcs.TrySetException(ex);

                // Start the simulation. This call is non-blocking.
                using var simulation = new NMRSimulationOpenCL(_currentDataset, _config);
                simulation.RunSimulationAsync(progress, onSuccess, onError);

                // Await the Task from the TaskCompletionSource. The method execution
                // will pause here until either onSuccess or onError is called.
                _lastResults = await tcs.Task;
                // ========================== END OF CHANGED SNIPPET ==========================
            }
            else
            {
                // CPU path
                var simulation = new NMRSimulation(_currentDataset, _config);
                _lastResults = await simulation.RunSimulationAsync(progress);
            }

            _simulationStatus = "Simulation completed successfully!";
        }
        catch (OperationCanceledException)
        {
            _simulationStatus = "Simulation cancelled by user.";
            Logger.Log("[NMRAnalysisTool] Simulation cancelled");
        }
        catch (Exception ex)
        {
            _simulationStatus = $"Simulation failed: {ex.Message}";
            Logger.LogError($"[NMRAnalysisTool] Simulation error: {ex}");
        }
        finally
        {
            _isSimulating = false;
            _cancellationSource?.Dispose();
            _cancellationSource = null;
        }
    }

    private void HandleExport(string filePath)
    {
        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            switch (extension)
            {
                case ".png":
                    ExportVisualizationAsPng(filePath);
                    break;
                case ".csv":
                    // Check if this is a T1-T2 map export
                    if (filePath.Contains("t1t2", StringComparison.OrdinalIgnoreCase) && _lastResults.HasT1T2Data)
                        T1T2Computation.ExportT1T2MapToCSV(_lastResults, filePath);
                    else
                        ExportDataAsCsv(filePath);
                    break;
                case ".txt":
                    ExportReport(filePath);
                    break;
            }

            Logger.Log($"[NMRAnalysisTool] Exported to: {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[NMRAnalysisTool] Export failed: {ex.Message}");
        }
    }

    private void ExportVisualizationAsPng(string filePath)
    {
        try
        {
            // Create a high-resolution plot (1920x1080)
            var width = 1920;
            var height = 1080;
            var imageData = new byte[width * height * 4]; // RGBA

            // Fill with background color
            for (var i = 0; i < width * height * 4; i += 4)
            {
                imageData[i] = 26; // R - Dark background
                imageData[i + 1] = 26; // G
                imageData[i + 2] = 26; // B
                imageData[i + 3] = 255; // A
            }

            // Render the plot
            switch (_selectedVisualization)
            {
                case 0:
                    RenderDecayCurveToBuffer(imageData, width, height);
                    break;
                case 1:
                    RenderT2DistributionToBuffer(imageData, width, height);
                    break;
                case 2:
                    RenderPoreSizeDistributionToBuffer(imageData, width, height);
                    break;
                case 3:
                    if (_lastResults.HasT1T2Data)
                        T1T2Computation.RenderT1T2MapToBuffer(_lastResults, imageData, width, height);
                    break;
            }

            // Save using ImageExporter
            ImageExporter.ExportColorSlice(imageData, width, height, filePath);

            Logger.Log($"[NMRAnalysisTool] Exported visualization to: {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[NMRAnalysisTool] PNG export failed: {ex.Message}");
        }
    }

    private void RenderDecayCurveToBuffer(byte[] buffer, int width, int height)
    {
        var padding = 150;
        var plotWidth = width - padding * 2;
        var plotHeight = height - padding * 2;

        if (_lastResults.TimePoints == null || _lastResults.Magnetization == null) return;

        var xMin = _lastResults.TimePoints.Min();
        var xMax = _lastResults.TimePoints.Max();
        var yMin = 0.0;
        var yMax = 1.0;

        // Draw axes
        DrawLineInBuffer(buffer, width, padding, padding, padding, padding + plotHeight, 200, 200, 200);
        DrawLineInBuffer(buffer, width, padding, padding + plotHeight, padding + plotWidth, padding + plotHeight, 200,
            200, 200);

        // Draw data
        for (var i = 0; i < _lastResults.TimePoints.Length - 1; i++)
        {
            var x1 = padding + (int)((_lastResults.TimePoints[i] - xMin) / (xMax - xMin) * plotWidth);
            var y1 = padding + plotHeight - (int)((_lastResults.Magnetization[i] - yMin) / (yMax - yMin) * plotHeight);
            var x2 = padding + (int)((_lastResults.TimePoints[i + 1] - xMin) / (xMax - xMin) * plotWidth);
            var y2 = padding + plotHeight -
                     (int)((_lastResults.Magnetization[i + 1] - yMin) / (yMax - yMin) * plotHeight);

            DrawLineInBuffer(buffer, width, x1, y1, x2, y2, 51, 204, 51);
        }

        // Draw title and labels
        DrawTextInBuffer(buffer, width, width / 2 - 100, 50, "NMR Decay Curve", 255, 255, 255);
        DrawTextInBuffer(buffer, width, width / 2 - 50, height - 80, "Time (ms)", 255, 255, 255);
        DrawTextInBuffer(buffer, width, 40, height / 2, "Magnetization", 255, 255, 255);
    }

    private void RenderT2DistributionToBuffer(byte[] buffer, int width, int height)
    {
        var padding = 150;
        var plotWidth = width - padding * 2;
        var plotHeight = height - padding * 2;

        if (_lastResults.T2HistogramBins == null || _lastResults.T2Histogram == null) return;

        var xMin = Math.Log10(_lastResults.T2HistogramBins.Min());
        var xMax = Math.Log10(_lastResults.T2HistogramBins.Max());
        var yMin = 0.0;
        var yMax = _lastResults.T2Histogram.Max();

        // Draw axes
        DrawLineInBuffer(buffer, width, padding, padding, padding, padding + plotHeight, 200, 200, 200);
        DrawLineInBuffer(buffer, width, padding, padding + plotHeight, padding + plotWidth, padding + plotHeight, 200,
            200, 200);

        // Draw histogram bars
        var barWidth = plotWidth / _lastResults.T2HistogramBins.Length;
        for (var i = 0; i < _lastResults.T2HistogramBins.Length; i++)
        {
            var x = padding + (int)((Math.Log10(_lastResults.T2HistogramBins[i]) - xMin) / (xMax - xMin) * plotWidth);
            var barHeight = (int)((_lastResults.T2Histogram[i] - yMin) / (yMax - yMin) * plotHeight);

            DrawFilledRectInBuffer(buffer, width, x, padding + plotHeight - barHeight, barWidth - 2, barHeight, 76, 153,
                230);
        }

        // Draw title and labels
        DrawTextInBuffer(buffer, width, width / 2 - 100, 50, "T2 Distribution", 255, 255, 255);
        DrawTextInBuffer(buffer, width, width / 2 - 50, height - 80, "T2 (ms)", 255, 255, 255);
        DrawTextInBuffer(buffer, width, 40, height / 2, "Amplitude", 255, 255, 255);
    }

    private void RenderPoreSizeDistributionToBuffer(byte[] buffer, int width, int height)
    {
        var padding = 150;
        var plotWidth = width - padding * 2;
        var plotHeight = height - padding * 2;

        if (_lastResults.PoreSizes == null || _lastResults.PoreSizeDistribution == null) return;

        var xMin = Math.Log10(_lastResults.PoreSizes.Min());
        var xMax = Math.Log10(_lastResults.PoreSizes.Max());
        var yMin = 0.0;
        var yMax = _lastResults.PoreSizeDistribution.Max();

        // Draw axes
        DrawLineInBuffer(buffer, width, padding, padding, padding, padding + plotHeight, 200, 200, 200);
        DrawLineInBuffer(buffer, width, padding, padding + plotHeight, padding + plotWidth, padding + plotHeight, 200,
            200, 200);

        // Draw histogram bars
        var barWidth = plotWidth / _lastResults.PoreSizes.Length;
        for (var i = 0; i < _lastResults.PoreSizes.Length; i++)
        {
            var x = padding + (int)((Math.Log10(_lastResults.PoreSizes[i]) - xMin) / (xMax - xMin) * plotWidth);
            var barHeight = (int)((_lastResults.PoreSizeDistribution[i] - yMin) / (yMax - yMin) * plotHeight);

            DrawFilledRectInBuffer(buffer, width, x, padding + plotHeight - barHeight, barWidth - 2, barHeight, 230,
                128, 51);
        }

        // Draw title and labels
        DrawTextInBuffer(buffer, width, width / 2 - 150, 50, "Pore Size Distribution", 255, 255, 255);
        DrawTextInBuffer(buffer, width, width / 2 - 80, height - 80, "Pore Radius (μm)", 255, 255, 255);
        DrawTextInBuffer(buffer, width, 40, height / 2, "Frequency", 255, 255, 255);
    }

    private void DrawLineInBuffer(byte[] buffer, int width, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        // Bresenham's line algorithm
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            SetPixel(buffer, width, x0, y0, r, g, b);

            if (x0 == x1 && y0 == y1) break;

            var e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private void DrawFilledRectInBuffer(byte[] buffer, int width, int x, int y, int w, int h, byte r, byte g, byte b)
    {
        for (var dy = 0; dy < h; dy++)
        for (var dx = 0; dx < w; dx++)
            SetPixel(buffer, width, x + dx, y + dy, r, g, b);
    }

    private void DrawTextInBuffer(byte[] buffer, int width, int x, int y, string text, byte r, byte g, byte b)
    {
        // Simple text rendering (just draws a rectangle for now)
        // For proper text, you'd need a font rasterizer
        var textWidth = text.Length * 10;
        DrawFilledRectInBuffer(buffer, width, x, y, textWidth, 20, r, g, b);
    }

    private void SetPixel(byte[] buffer, int width, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || x >= width || y < 0 || y >= buffer.Length / (width * 4)) return;

        var index = (y * width + x) * 4;
        buffer[index] = r;
        buffer[index + 1] = g;
        buffer[index + 2] = b;
        buffer[index + 3] = 255;
    }

    private void ExportDataAsCsv(string filePath)
    {
        var sb = new StringBuilder();

        // Decay data
        sb.AppendLine("Time (ms),Magnetization");
        for (var i = 0; i < _lastResults.TimePoints.Length; i++)
            sb.AppendLine($"{_lastResults.TimePoints[i]},{_lastResults.Magnetization[i]}");

        sb.AppendLine();

        // T2 distribution
        sb.AppendLine("T2 (ms),Amplitude");
        for (var i = 0; i < _lastResults.T2HistogramBins.Length; i++)
            sb.AppendLine($"{_lastResults.T2HistogramBins[i]},{_lastResults.T2Histogram[i]}");

        if (_lastResults.PoreSizes != null)
        {
            sb.AppendLine();
            sb.AppendLine("Pore Radius (μm),Frequency");
            for (var i = 0; i < _lastResults.PoreSizes.Length; i++)
                sb.AppendLine($"{_lastResults.PoreSizes[i]},{_lastResults.PoreSizeDistribution[i]}");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    private void ExportReport(string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NMR ANALYSIS REPORT");
        sb.AppendLine("===================");
        sb.AppendLine();
        sb.AppendLine($"Dataset: {_currentDataset.Name}");
        sb.AppendLine($"Pore Material: {_lastResults.PoreMaterial}");
        sb.AppendLine($"Analysis Date: {DateTime.Now}");
        sb.AppendLine();
        sb.AppendLine("SIMULATION PARAMETERS");
        sb.AppendLine("---------------------");
        sb.AppendLine($"Number of Walkers: {_lastResults.NumberOfWalkers:N0}");
        sb.AppendLine($"Time Steps: {_lastResults.TotalSteps}");
        sb.AppendLine($"Time Step: {_lastResults.TimeStep} ms");
        sb.AppendLine($"Computation Time: {_lastResults.ComputationTime.TotalSeconds:F2}s");
        sb.AppendLine($"Method: {_lastResults.ComputationMethod}");
        sb.AppendLine();
        sb.AppendLine("RESULTS");
        sb.AppendLine("-------");
        sb.AppendLine($"Mean T2: {_lastResults.MeanT2:F2} ms");
        sb.AppendLine($"Geometric Mean T2: {_lastResults.GeometricMeanT2:F2} ms");
        sb.AppendLine($"Peak T2: {_lastResults.T2PeakValue:F2} ms");

        if (_lastResults.HasT1T2Data)
        {
            sb.AppendLine();
            sb.AppendLine("T1-T2 CORRELATION");
            sb.AppendLine("-----------------");
            sb.AppendLine($"T1/T2 Ratio: {_config.T1T2Ratio:F2}");
            sb.AppendLine($"T1 Range: {_lastResults.T1HistogramBins[0]:F2} - {_lastResults.T1HistogramBins[^1]:F2} ms");
            sb.AppendLine($"T2 Range: {_lastResults.T2HistogramBins[0]:F2} - {_lastResults.T2HistogramBins[^1]:F2} ms");
            sb.AppendLine("2D T1-T2 map computed successfully");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    private void ImportAsTableDataset()
    {
        try
        {
            var dataTable = new DataTable("NMR Results");

            // Add columns
            dataTable.Columns.Add("Time_ms", typeof(double));
            dataTable.Columns.Add("Magnetization", typeof(double));
            dataTable.Columns.Add("T2_ms", typeof(double));
            dataTable.Columns.Add("T2_Amplitude", typeof(double));

            if (_lastResults.PoreSizes != null)
            {
                dataTable.Columns.Add("PoreRadius_um", typeof(double));
                dataTable.Columns.Add("PoreSize_Frequency", typeof(double));
            }

            // Add data rows
            var maxRows = Math.Max(_lastResults.TimePoints.Length, _lastResults.T2HistogramBins.Length);
            for (var i = 0; i < maxRows; i++)
            {
                var row = dataTable.NewRow();

                if (i < _lastResults.TimePoints.Length)
                {
                    row["Time_ms"] = _lastResults.TimePoints[i];
                    row["Magnetization"] = _lastResults.Magnetization[i];
                }
                else
                {
                    row["Time_ms"] = DBNull.Value;
                    row["Magnetization"] = DBNull.Value;
                }

                if (i < _lastResults.T2HistogramBins.Length)
                {
                    row["T2_ms"] = _lastResults.T2HistogramBins[i];
                    row["T2_Amplitude"] = _lastResults.T2Histogram[i];
                }
                else
                {
                    row["T2_ms"] = DBNull.Value;
                    row["T2_Amplitude"] = DBNull.Value;
                }

                if (_lastResults.PoreSizes != null && i < _lastResults.PoreSizes.Length)
                {
                    row["PoreRadius_um"] = _lastResults.PoreSizes[i];
                    row["PoreSize_Frequency"] = _lastResults.PoreSizeDistribution[i];
                }
                else if (_lastResults.PoreSizes != null)
                {
                    row["PoreRadius_um"] = DBNull.Value;
                    row["PoreSize_Frequency"] = DBNull.Value;
                }

                dataTable.Rows.Add(row);
            }

            var tableDataset = new TableDataset($"{_currentDataset.Name}_NMR", dataTable);
            ProjectManager.Instance.AddDataset(tableDataset);

            Logger.Log("[NMRAnalysisTool] Imported NMR results as table dataset");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[NMRAnalysisTool] Failed to import as table: {ex.Message}");
        }
    }
}