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
            InitializeDefaultConfig(ctDataset);
        }

        DrawConfigurationPanel();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawSimulationControls();

        if (ctDataset.NmrResults != null)
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
        // FIXED: Use proper unit conversion
        _config.VoxelSize = dataset.GetPixelSizeInMeters();
        var voxelSizeUm = dataset.GetPixelSizeInMicrometers();

        Logger.Log($"[NMRAnalysisTool] Voxel size: {voxelSizeUm:F2} µm ({dataset.PixelSize} {dataset.Unit})");

        // Warn if voxel size is unusual
        if (voxelSizeUm < 0.01)
            Logger.LogWarning($"[NMRAnalysisTool] Very small voxel size ({voxelSizeUm:F4} µm). Check units!");
        else if (voxelSizeUm > 1000)
            Logger.LogWarning(
                $"[NMRAnalysisTool] Very large voxel size ({voxelSizeUm:F1} µm). Pores at mm scale - consider if NMR is appropriate.");

        _config.MaterialRelaxivities.Clear();

        // Add default relaxivities ONLY for non-pore materials
        foreach (var material in dataset.Materials)
        {
            if (material.ID == 0) continue; // Skip exterior

            // Initialize all materials but mark which is pore space
            _config.MaterialRelaxivities[material.ID] = new MaterialRelaxivityConfig
            {
                MaterialName = material.Name,
                SurfaceRelaxivity = 10.0, // Default value for matrix materials
                Color = material.Color
            };
        }
    }

    private void DrawConfigurationPanel()
    {
        ImGui.SeparatorText("Configuration");

        // Fluid presets
        ImGui.Text("Fluid Preset");
        ImGui.SetNextItemWidth(-1);
        var presetNames = FluidPresets.Presets.Keys.ToArray();
        if (ImGui.Combo("##FluidPreset", ref _selectedPresetIndex, presetNames, presetNames.Length))
        {
            _selectedPreset = presetNames[_selectedPresetIndex];
            if (_currentDataset != null) FluidPresets.ApplyPreset(_config, _selectedPreset, _currentDataset);
        }

        if (FluidPresets.Presets.TryGetValue(_selectedPreset, out var preset))
            ImGui.TextDisabled(preset.Description);

        ImGui.Spacing();

        // Pore material selection
        var materials = _currentDataset.Materials.Where(m => m.ID != 0).ToList();
        if (materials.Count == 0)
        {
            ImGui.TextDisabled("No materials defined. Create materials first.");
            return;
        }

        ImGui.Text("Pore Space Material");
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
            ImGui.Text("Number of Walkers");
            var walkers = _config.NumberOfWalkers;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt("##Walkers", ref walkers, 1000, 100000))
                _config.NumberOfWalkers = walkers;

            ImGui.Text("Time Steps");
            var steps = _config.NumberOfSteps;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt("##Steps", ref steps, 100, 5000))
                _config.NumberOfSteps = steps;

            ImGui.Text("Time Step (ms)");
            var timeStep = (float)_config.TimeStepMs;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat("##TimeStep", ref timeStep, 0.001f, 0.001f, 1.0f, "%.3f"))
                _config.TimeStepMs = timeStep;

            ImGui.Text("Diffusion Coefficient (×10⁻⁹ m²/s)");
            var diffusion = (float)(_config.DiffusionCoefficient * 1e9);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat("##Diffusion", ref diffusion, 0.1f, 0.1f, 10.0f))
                _config.DiffusionCoefficient = diffusion * 1e-9;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Water at 25°C: ~2.0");
        }

        ImGui.Spacing();

        // Material relaxivities - ONLY show NON-PORE materials
        if (ImGui.CollapsingHeader("Matrix Surface Relaxivities (ρ)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped(
                "Surface relaxivity controls T2 decay at pore-matrix interfaces. Only matrix materials are shown.");
            ImGui.Spacing();

            foreach (var matConfig in _config.MaterialRelaxivities.Values.ToList())
            {
                var materialID = _config.MaterialRelaxivities.First(kvp => kvp.Value == matConfig).Key;

                // SKIP the pore material - it doesn't have a relaxivity!
                if (materialID == _config.PoreMaterialID) continue;

                ImGui.PushStyleColor(ImGuiCol.Text, matConfig.Color);
                ImGui.Text("■ ");
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.Text(matConfig.MaterialName);

                ImGui.Indent();
                ImGui.Text("Relaxivity ρ (μm/s)");
                var relaxivity = (float)matConfig.SurfaceRelaxivity;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat($"##Relax{materialID}", ref relaxivity, 0.1f, 0.1f, 1000.0f, "%.1f"))
                    matConfig.SurfaceRelaxivity = relaxivity;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Typical: Quartz 1-10, Clay 10-100, Carbonate 5-20 μm/s");
                ImGui.Unindent();
                ImGui.Spacing();
            }
        }

        ImGui.Spacing();

        // Advanced settings
        if (ImGui.CollapsingHeader("Advanced Settings"))
        {
            // Pore shape factor
            ImGui.Text("Pore Geometry Shape Factor");
            var shapeFactor = (float)_config.PoreShapeFactor;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderFloat("##ShapeFactor", ref shapeFactor, 1.0f, 5.0f, "%.1f"))
                _config.PoreShapeFactor = shapeFactor;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Spherical pores: 3.0, Cylindrical: 2.0, Slit-like: 1.0");

            ImGui.Spacing();

            // GPU toggle
            if (_gpuAvailable)
            {
                var useGpu = _config.UseOpenCL;
                if (ImGui.Checkbox("Use GPU (OpenCL)", ref useGpu))
                    _config.UseOpenCL = useGpu;
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
            var computeT1T2 = _config.ComputeT1T2Map;
            if (ImGui.Checkbox("Compute T1-T2 Correlation Map", ref computeT1T2))
                _config.ComputeT1T2Map = computeT1T2;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("2D NMR map for fluid identification (adds processing time)");

            if (_config.ComputeT1T2Map)
            {
                ImGui.Indent();
                ImGui.Text("T1/T2 Ratio");
                var t1t2Ratio = (float)_config.T1T2Ratio;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderFloat("##T1T2Ratio", ref t1t2Ratio, 1.0f, 5.0f, "%.1f"))
                    _config.T1T2Ratio = t1t2Ratio;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Typical: 1.5 for water-saturated, 2-3 for oil");

                ImGui.Text("T1 Bins");
                var t1Bins = _config.T1BinCount;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderInt("##T1Bins", ref t1Bins, 16, 128))
                    _config.T1BinCount = t1Bins;
                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("T2 Histogram Bins");
            var t2Bins = _config.T2BinCount;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt("##T2Bins", ref t2Bins, 16, 128))
                _config.T2BinCount = t2Bins;

            ImGui.Text("T2 Min (ms)");
            var t2Min = (float)_config.T2MinMs;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat("##T2Min", ref t2Min, 0.01f, 0.01f, 10.0f, "%.2f"))
                _config.T2MinMs = t2Min;

            ImGui.Text("T2 Max (ms)");
            var t2Max = (float)_config.T2MaxMs;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat("##T2Max", ref t2Max, 10f, 100f, 100000f, "%.0f"))
                _config.T2MaxMs = t2Max;

            ImGui.Text("Random Seed");
            var seed = _config.RandomSeed;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt("##RandomSeed", ref seed))
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
            ImGui.TextDisabled($"Method: {method} | Estimated time: {estimatedTime:F1}s");

            if (ImGui.Button("Run NMR Simulation", new Vector2(-1, 0))) _ = RunSimulationAsync();
        }
    }

    private void DrawResultsPanel()
    {
        ImGui.SeparatorText("Results");
        var results = _currentDataset.NmrResults;

        // Statistics summary
        if (ImGui.CollapsingHeader("Summary Statistics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // FIXED: Show voxel size with correct units
            var voxelSizeUm = _currentDataset.GetPixelSizeInMicrometers();
            ImGui.Text($"Voxel Size: {voxelSizeUm:F2} µm ({_currentDataset.PixelSize:F2} {_currentDataset.Unit})");

            ImGui.Separator();
            ImGui.Text($"Computation Time: {results.ComputationTime.TotalSeconds:F2}s");
            ImGui.Text($"Method: {results.ComputationMethod}");
            ImGui.Text($"Walkers: {results.NumberOfWalkers:N0}");
            ImGui.Separator();
            ImGui.Text($"Mean T2: {results.MeanT2:F2} ms");
            ImGui.Text($"Geometric Mean T2: {results.GeometricMeanT2:F2} ms");
            ImGui.Text($"Peak T2: {results.T2PeakValue:F2} ms");

            if (results.PoreSizes != null)
            {
                ImGui.Separator();
                ImGui.Text("Pore Size Distribution:");
                var meanPoreSize = results.PoreSizes.Zip(results.PoreSizeDistribution, (s, d) => s * d).Sum() /
                                   results.PoreSizeDistribution.Sum();

                // FIXED: Display in appropriate units
                if (meanPoreSize > 1000)
                    ImGui.Text($"Mean Pore Radius: {meanPoreSize / 1000:F2} mm");
                else if (meanPoreSize < 1)
                    ImGui.Text($"Mean Pore Radius: {meanPoreSize * 1000:F2} nm");
                else
                    ImGui.Text($"Mean Pore Radius: {meanPoreSize:F2} µm");
            }
        }

        ImGui.Spacing();

        // Visualization selector
        ImGui.SeparatorText("Visualization");

        var vizOptions = results.HasT1T2Data
            ? new[] { "Decay Curve", "T2 Distribution", "Pore Size Distribution", "T1-T2 Map" }
            : new[] { "Decay Curve", "T2 Distribution", "Pore Size Distribution" };

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##VizSelector", ref _selectedVisualization, vizOptions, vizOptions.Length);

        ImGui.Spacing();

        // Draw selected visualization
        var plotSize = new Vector2(-1, 400);

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
                if (results.HasT1T2Data) DrawT1T2Map(plotSize);
                break;
        }

        ImGui.Spacing();

        // Export controls
        DrawExportControls();
    }

    private void DrawDecayCurve(Vector2 size)
    {
        var results = _currentDataset.NmrResults;
        if (ImGui.BeginChild("##DecayPlot", size, ImGuiChildFlags.Border))
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if (contentSize.X > 50 && contentSize.Y > 50)
            {
                var plotColor = new Vector4(0.2f, 0.8f, 0.2f, 1.0f);
                DrawPlot(drawList, pos, contentSize,
                    results.TimePoints,
                    results.Magnetization,
                    "Time (ms)", "Norm. Magnet.",
                    new List<(string, Vector4)> { ("Decay", plotColor) });
            }
        }

        ImGui.EndChild();
    }

    private void DrawT2Distribution(Vector2 size)
    {
        var results = _currentDataset.NmrResults;
        if (ImGui.BeginChild("##T2Plot", size, ImGuiChildFlags.Border))
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if (contentSize.X > 50 && contentSize.Y > 50)
            {
                var plotColor = new Vector4(0.3f, 0.6f, 0.9f, 1.0f);
                DrawHistogram(drawList, pos, contentSize,
                    results.T2HistogramBins,
                    results.T2Histogram,
                    "T₂ (ms)", "Amplitude",
                    new List<(string, Vector4)> { ("T₂ Distribution", plotColor) },
                    true);
            }
        }

        ImGui.EndChild();
    }

    private void DrawPoreSizeDistribution(Vector2 size)
    {
        var results = _currentDataset.NmrResults;
        if (ImGui.BeginChild("##PoreSizePlot", size, ImGuiChildFlags.Border))
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if (contentSize.X > 50 && contentSize.Y > 50 && results.PoreSizes != null)
            {
                var plotColor = new Vector4(0.9f, 0.5f, 0.2f, 1.0f);

                // FIXED: Determine appropriate unit for pore size display
                var maxPoreSize = results.PoreSizes.Max();
                string xLabel;
                double[] displaySizes;

                if (maxPoreSize > 1000) // > 1 mm
                {
                    xLabel = "Pore Radius (mm)";
                    displaySizes = results.PoreSizes.Select(s => s / 1000.0).ToArray();
                }
                else if (maxPoreSize < 1) // < 1 µm
                {
                    xLabel = "Pore Radius (nm)";
                    displaySizes = results.PoreSizes.Select(s => s * 1000.0).ToArray();
                }
                else
                {
                    xLabel = "Pore Radius (µm)";
                    displaySizes = results.PoreSizes;
                }

                DrawHistogram(drawList, pos, contentSize,
                    displaySizes,
                    results.PoreSizeDistribution,
                    xLabel, "Frequency",
                    new List<(string, Vector4)> { ("Pore Size", plotColor) },
                    true);
            }
        }

        ImGui.EndChild();
    }

    private void DrawT1T2Map(Vector2 size)
    {
        var results = _currentDataset.NmrResults;
        if (ImGui.BeginChild("##T1T2Plot", size, ImGuiChildFlags.Border))
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if (contentSize.X > 50 && contentSize.Y > 50 && results.HasT1T2Data)
                Draw2DMap(drawList, pos, contentSize, results.T1T2Map,
                    results.T1HistogramBins, results.T2HistogramBins,
                    "T₂ (ms)", "T₁ (ms)");
        }

        ImGui.EndChild();
    }

    // FIXED: Proper layout restored with shorter labels and correct positioning
    private void DrawPlot(ImDrawListPtr drawList, Vector2 pos, Vector2 size,
        double[] xData, double[] yData, string xLabel, string yLabel, List<(string, Vector4)> legendItems,
        bool logScaleX = false, bool logScaleY = false)
    {
        var padding = new Vector4(100, 80, 120, 100); // Left, Top, Right, Bottom
        var plotArea = new Vector2(size.X - padding.X - padding.Z, size.Y - padding.Y - padding.W);
        var plotPos = new Vector2(pos.X + padding.X, pos.Y + padding.Y);

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, 1.0f)));
        drawList.AddRectFilled(plotPos, plotPos + plotArea, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)));

        if (xData == null || yData == null || xData.Length == 0) return;

        // Data range
        var xMin = logScaleX ? Math.Log10(xData.Where(x => x > 0).DefaultIfEmpty(1e-6).Min()) : xData.Min();
        var xMax = logScaleX ? Math.Log10(xData.Max()) : xData.Max();

        double yMin, yMax;
        if (logScaleY)
        {
            yMin = Math.Log10(yData.Where(y => y > 0).DefaultIfEmpty(1e-6).Min());
            yMax = Math.Log10(yData.Max());
        }
        else
        {
            yMin = yData.Min();
            yMax = yData.Max();

            if (Math.Abs(yMax - yMin) < 1e-10)
            {
                yMin = 0.0;
                yMax = 1.0;
            }
            else
            {
                var yRange = yMax - yMin;
                yMin -= yRange * 0.05;
                yMax += yRange * 0.05;
            }
        }

        var xRange = xMax - xMin;
        if (xRange > 0)
        {
            xMin -= xRange * 0.05;
            xMax += xRange * 0.05;
        }

        // Grid
        var gridColor = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        for (var i = 0; i <= 10; i++)
        {
            var frac = i / 10.0f;
            var x = plotPos.X + frac * plotArea.X;
            drawList.AddLine(new Vector2(x, plotPos.Y), new Vector2(x, plotPos.Y + plotArea.Y), gridColor, 1f);
            var y = plotPos.Y + frac * plotArea.Y;
            drawList.AddLine(new Vector2(plotPos.X, y), new Vector2(plotPos.X + plotArea.X, y), gridColor, 1f);
        }

        // Axes
        var axisColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
        drawList.AddLine(plotPos, new Vector2(plotPos.X, plotPos.Y + plotArea.Y), axisColor, 2f);
        drawList.AddLine(new Vector2(plotPos.X, plotPos.Y + plotArea.Y),
            new Vector2(plotPos.X + plotArea.X, plotPos.Y + plotArea.Y), axisColor, 2f);

        // Draw data line
        var lineColor = ImGui.GetColorU32(legendItems[0].Item2);
        if (Math.Abs(xMax - xMin) > 1e-10 && Math.Abs(yMax - yMin) > 1e-10)
            for (var i = 0; i < xData.Length - 1; i++)
            {
                var x1Val = logScaleX ? Math.Log10(Math.Max(xData[i], 1e-10)) : xData[i];
                var y1Val = logScaleY ? Math.Log10(Math.Max(yData[i], 1e-10)) : yData[i];
                var x2Val = logScaleX ? Math.Log10(Math.Max(xData[i + 1], 1e-10)) : xData[i + 1];
                var y2Val = logScaleY ? Math.Log10(Math.Max(yData[i + 1], 1e-10)) : yData[i + 1];

                var x1 = plotPos.X + (float)((x1Val - xMin) / (xMax - xMin)) * plotArea.X;
                var y1 = plotPos.Y + plotArea.Y - (float)((y1Val - yMin) / (yMax - yMin)) * plotArea.Y;
                var x2 = plotPos.X + (float)((x2Val - xMin) / (xMax - xMin)) * plotArea.X;
                var y2 = plotPos.Y + plotArea.Y - (float)((y2Val - yMin) / (yMax - yMin)) * plotArea.Y;

                drawList.AddLine(new Vector2(x1, y1), new Vector2(x2, y2), lineColor, 2.5f);
            }

        // Draw tick labels FIRST (so we know how much space they take)
        DrawTickLabels(drawList, plotPos, plotArea, xMin, xMax, yMin, yMax, logScaleX, logScaleY,
            ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));

        // Labels
        var textColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
        var xLabelSize = ImGui.CalcTextSize(xLabel);
        var yLabelSize = ImGui.CalcTextSize(yLabel);

        // X-axis label (centered, below plot)
        drawList.AddText(new Vector2(plotPos.X + plotArea.X / 2 - xLabelSize.X / 2, plotPos.Y + plotArea.Y + 60),
            textColor, xLabel);

        // Y-axis label (LEFT side, vertically centered, with clearance from tick labels)
        // Position at x=15 (far left) to avoid crossing axis at plotPos.X
        drawList.AddText(new Vector2(pos.X + 15, plotPos.Y + plotArea.Y / 2 - yLabelSize.Y / 2),
            textColor, yLabel);

        DrawLegendOutside(drawList, pos, size, padding, legendItems);
    }

// Update DrawHistogram the same way
    private void DrawHistogram(ImDrawListPtr drawList, Vector2 pos, Vector2 size,
        double[] bins, double[] values, string xLabel, string yLabel, List<(string, Vector4)> legendItems,
        bool logScaleX)
    {
        var padding = new Vector4(100, 80, 120, 100);
        var plotArea = new Vector2(size.X - padding.X - padding.Z, size.Y - padding.Y - padding.W);
        var plotPos = new Vector2(pos.X + padding.X, pos.Y + padding.Y);

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, 1.0f)));
        drawList.AddRectFilled(plotPos, plotPos + plotArea, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)));

        if (bins == null || values == null || bins.Length == 0) return;

        var xData = logScaleX ? bins.Select(Math.Log10).ToArray() : bins;
        var xMin = xData.Min();
        var xMax = xData.Max();
        var yMin = 0.0;
        var yMax = values.Max();

        var xRange = xMax - xMin;
        var yRange = yMax - yMin;
        if (xRange > 0)
        {
            xMin -= xRange * 0.05;
            xMax += xRange * 0.05;
        }

        if (yRange > 0)
            yMax += yRange * 0.05;
        else
            yMax = 1.0;

        // Grid
        var gridColor = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        for (var i = 0; i <= 10; i++)
        {
            var frac = i / 10.0f;
            var x = plotPos.X + frac * plotArea.X;
            drawList.AddLine(new Vector2(x, plotPos.Y), new Vector2(x, plotPos.Y + plotArea.Y), gridColor, 1f);
            var y = plotPos.Y + frac * plotArea.Y;
            drawList.AddLine(new Vector2(plotPos.X, y), new Vector2(plotPos.X + plotArea.X, y), gridColor, 1f);
        }

        // Axes
        var axisColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
        drawList.AddLine(plotPos, new Vector2(plotPos.X, plotPos.Y + plotArea.Y), axisColor, 2f);
        drawList.AddLine(new Vector2(plotPos.X, plotPos.Y + plotArea.Y),
            new Vector2(plotPos.X + plotArea.X, plotPos.Y + plotArea.Y), axisColor, 2f);

        // Draw bars
        var barColor = ImGui.GetColorU32(legendItems[0].Item2);
        var barOutlineColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f));

        if (Math.Abs(xMax - xMin) > 1e-10 && Math.Abs(yMax - yMin) > 1e-10)
            for (var i = 0; i < bins.Length; i++)
            {
                var x1 = plotPos.X + (float)((xData[i] - xMin) / (xMax - xMin)) * plotArea.X;
                var x2 = i < bins.Length - 1
                    ? plotPos.X + (float)((xData[i + 1] - xMin) / (xMax - xMin)) * plotArea.X
                    : plotPos.X + plotArea.X;

                var y = plotPos.Y + plotArea.Y - (float)((values[i] - yMin) / (yMax - yMin)) * plotArea.Y;
                var yBottom = plotPos.Y + plotArea.Y;

                drawList.AddRectFilled(new Vector2(x1 + 1, y), new Vector2(x2 - 1, yBottom), barColor);
                drawList.AddRect(new Vector2(x1 + 1, y), new Vector2(x2 - 1, yBottom), barOutlineColor, 0f, 0, 1f);
            }

        // Draw tick labels first
        DrawTickLabels(drawList, plotPos, plotArea, xMin, xMax, yMin, yMax, logScaleX, false,
            ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));

        // Labels
        var textColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
        var xLabelSize = ImGui.CalcTextSize(xLabel);
        var yLabelSize = ImGui.CalcTextSize(yLabel);

        drawList.AddText(new Vector2(plotPos.X + plotArea.X / 2 - xLabelSize.X / 2, plotPos.Y + plotArea.Y + 60),
            textColor, xLabel);

        // Y-axis label on LEFT side with clearance
        drawList.AddText(new Vector2(pos.X + 15, plotPos.Y + plotArea.Y / 2 - yLabelSize.Y / 2),
            textColor, yLabel);

        DrawLegendOutside(drawList, pos, size, padding, legendItems);
    }

    private void DrawTickLabels(ImDrawListPtr drawList, Vector2 plotPos, Vector2 plotArea,
        double xMin, double xMax, double yMin, double yMax, bool logX, bool logY, uint textColor)
    {
        var yRange = yMax - yMin;
        string yFormat;
        if (yRange < 0.01)
            yFormat = "F4";
        else if (yRange < 0.1)
            yFormat = "F3";
        else if (yRange < 10)
            yFormat = "F2";
        else
            yFormat = "F1";

        var xRange = xMax - xMin;
        string xFormat;
        if (xRange < 0.01)
            xFormat = "F4";
        else if (xRange < 0.1)
            xFormat = "F3";
        else if (xRange < 10)
            xFormat = "F2";
        else
            xFormat = "F1";

        // X-axis ticks
        for (var i = 0; i <= 5; i++)
        {
            var frac = i / 5.0;
            var xVal = xMin + frac * (xMax - xMin);
            var xLabel = logX ? $"{Math.Pow(10, xVal).ToString(xFormat)}" : xVal.ToString(xFormat);
            var labelSize = ImGui.CalcTextSize(xLabel);
            var x = plotPos.X + (float)frac * plotArea.X;

            drawList.AddLine(new Vector2(x, plotPos.Y + plotArea.Y),
                new Vector2(x, plotPos.Y + plotArea.Y + 5), textColor, 2f);
            drawList.AddText(new Vector2(x - labelSize.X / 2, plotPos.Y + plotArea.Y + 10), textColor, xLabel);
        }

        // Y-axis ticks
        for (var i = 0; i <= 5; i++)
        {
            var frac = i / 5.0;
            var yVal = yMin + frac * (yMax - yMin);
            var yLabel = logY ? $"{Math.Pow(10, yVal).ToString(yFormat)}" : yVal.ToString(yFormat);
            var labelSize = ImGui.CalcTextSize(yLabel);
            var y = plotPos.Y + plotArea.Y - (float)frac * plotArea.Y;

            drawList.AddLine(new Vector2(plotPos.X - 5, y), new Vector2(plotPos.X, y), textColor, 2f);
            drawList.AddText(new Vector2(plotPos.X - labelSize.X - 10, y - labelSize.Y / 2), textColor, yLabel);
        }
    }

    private void DrawLegendOutside(ImDrawListPtr drawList, Vector2 pos, Vector2 size, Vector4 padding,
        List<(string, Vector4)> items)
    {
        // Position legend in top-right margin
        var legendPos = new Vector2(pos.X + size.X - padding.Z + 10, pos.Y + 10);
        var textColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
        var boxSize = 12f;

        foreach (var (label, color) in items)
        {
            drawList.AddRectFilled(legendPos, new Vector2(legendPos.X + boxSize, legendPos.Y + boxSize),
                ImGui.GetColorU32(color));
            drawList.AddRect(legendPos, new Vector2(legendPos.X + boxSize, legendPos.Y + boxSize),
                ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f)), 0f, 0, 1f);
            drawList.AddText(new Vector2(legendPos.X + boxSize + 8, legendPos.Y), textColor, label);
            legendPos.Y += 25;
        }
    }

    private void Draw2DMap(ImDrawListPtr drawList, Vector2 pos, Vector2 size,
        double[,] map, double[] xBins, double[] yBins, string xLabel, string yLabel)
    {
        var padding = new Vector4(100, 80, 150, 100);
        var plotArea = new Vector2(size.X - padding.X - padding.Z, size.Y - padding.Y - padding.W);
        var plotPos = new Vector2(pos.X + padding.X, pos.Y + padding.Y);

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, 1.0f)));
        drawList.AddRectFilled(plotPos, plotPos + plotArea, ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)));

        if (map == null || xBins == null || yBins == null) return;

        var t1Count = map.GetLength(0);
        var t2Count = map.GetLength(1);

        var maxAmplitude = 0.0;
        for (var i = 0; i < t1Count; i++)
        for (var j = 0; j < t2Count; j++)
            maxAmplitude = Math.Max(maxAmplitude, map[i, j]);

        if (maxAmplitude < 1e-10) return;

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

            drawList.AddRectFilled(new Vector2(px, py), new Vector2(px + pixelWidth, py + pixelHeight),
                ImGui.GetColorU32(new Vector4(r / 255f, g / 255f, b / 255f, 1.0f)));
        }

        var axisColor = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        drawList.AddRect(plotPos, plotPos + plotArea, axisColor, 0f, 0, 2f);

        var textColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
        var xLabelSize = ImGui.CalcTextSize(xLabel);
        drawList.AddText(new Vector2(plotPos.X + plotArea.X / 2 - xLabelSize.X / 2, plotPos.Y + plotArea.Y + 60),
            textColor, xLabel);
        drawList.AddText(new Vector2(pos.X + 20, plotPos.Y + plotArea.Y / 2), textColor, yLabel);

        // Draw colorbar
        DrawColorbar(drawList, new Vector2(plotPos.X + plotArea.X + 20, plotPos.Y),
            new Vector2(30, plotArea.Y), maxAmplitude);
    }

    private void DrawColorbar(ImDrawListPtr drawList, Vector2 pos, Vector2 size, double maxValue)
    {
        var steps = 100;
        var stepHeight = size.Y / steps;

        for (var i = 0; i < steps; i++)
        {
            var value = i / (float)steps;
            var (r, g, b) = GetHotColor(value);
            var y = pos.Y + size.Y - (i + 1) * stepHeight;

            drawList.AddRectFilled(new Vector2(pos.X, y),
                new Vector2(pos.X + size.X, y + stepHeight),
                ImGui.GetColorU32(new Vector4(r / 255f, g / 255f, b / 255f, 1.0f)));
        }

        // Outline
        drawList.AddRect(pos, pos + size,
            ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f)), 0f, 0, 1f);

        // Labels
        var textColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
        drawList.AddText(new Vector2(pos.X + size.X + 5, pos.Y - 10), textColor, $"{maxValue:E2}");
        drawList.AddText(new Vector2(pos.X + size.X + 5, pos.Y + size.Y - 10), textColor, "0");
    }

    private (byte, byte, byte) GetHotColor(double value)
    {
        value = Math.Clamp(value, 0, 1);
        if (value < 0.33) return ((byte)(value / 0.33 * 255), 0, 0);
        if (value < 0.66) return (255, (byte)((value - 0.33) / 0.33 * 255), 0);
        return (255, 255, (byte)((value - 0.66) / 0.34 * 255));
    }

    private void DrawExportControls()
    {
        var results = _currentDataset.NmrResults;
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

        if (results.HasT1T2Data)
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
        var operations = (long)_config.NumberOfWalkers * _config.NumberOfSteps;

        if (_config.UseOpenCL && _gpuAvailable)
        {
            var opsPerSecond = 200_000_000.0;
            return operations / opsPerSecond;
        }
        else
        {
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

            NMRResults results;
            if (_config.UseOpenCL && _gpuAvailable)
            {
                var tcs = new TaskCompletionSource<NMRResults>();
                Action<NMRResults> onSuccess = res => tcs.TrySetResult(res);
                Action<Exception> onError = ex => tcs.TrySetException(ex);

                using var simulation = new NMRSimulationOpenCL(_currentDataset, _config);
                simulation.RunSimulationAsync(progress, onSuccess, onError);

                results = await tcs.Task;
            }
            else
            {
                var simulation = new NMRSimulation(_currentDataset, _config);
                results = await simulation.RunSimulationAsync(progress);
            }

            _currentDataset.NmrResults = results;
            ProjectManager.Instance.NotifyDatasetDataChanged(_currentDataset);

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
            var results = _currentDataset.NmrResults;

            switch (extension)
            {
                case ".png":
                    ExportVisualizationAsPng(filePath);
                    break;
                case ".csv":
                    if (filePath.Contains("t1t2", StringComparison.OrdinalIgnoreCase) && results.HasT1T2Data)
                        T1T2Computation.ExportT1T2MapToCSV(results, filePath);
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
            var results = _currentDataset.NmrResults;
            var width = 1920;
            var height = 1080;
            var imageData = new byte[width * height * 4];

            for (var i = 0; i < width * height * 4; i += 4)
            {
                imageData[i] = 242;
                imageData[i + 1] = 242;
                imageData[i + 2] = 242;
                imageData[i + 3] = 255;
            }

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
                    if (results.HasT1T2Data)
                        T1T2Computation.RenderT1T2MapToBuffer(results, imageData, width, height);
                    break;
            }

            ImageExporter.ExportColorSlice(imageData, width, height, filePath);
            Logger.Log($"[NMRAnalysisTool] Exported visualization to: {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[NMRAnalysisTool] PNG export failed: {ex.Message}");
        }
    }

    // Rendering methods remain similar but use improved algorithms
    private void RenderDecayCurveToBuffer(byte[] buffer, int width, int height)
    {
        var results = _currentDataset.NmrResults;
        var padding = 150;
        var plotWidth = width - padding * 2;
        var plotHeight = height - padding * 2;

        if (results.TimePoints == null || results.Magnetization == null) return;

        var xMin = results.TimePoints.Min();
        var xMax = results.TimePoints.Max();
        var yMin = 0.0;
        var yMax = 1.0;

        DrawLineInBuffer(buffer, width, padding, padding, padding, padding + plotHeight, 200, 200, 200);
        DrawLineInBuffer(buffer, width, padding, padding + plotHeight, padding + plotWidth, padding + plotHeight, 200,
            200, 200);

        for (var i = 0; i < results.TimePoints.Length - 1; i++)
        {
            var x1 = padding + (int)((results.TimePoints[i] - xMin) / (xMax - xMin) * plotWidth);
            var y1 = padding + plotHeight - (int)((results.Magnetization[i] - yMin) / (yMax - yMin) * plotHeight);
            var x2 = padding + (int)((results.TimePoints[i + 1] - xMin) / (xMax - xMin) * plotWidth);
            var y2 = padding + plotHeight -
                     (int)((results.Magnetization[i + 1] - yMin) / (yMax - yMin) * plotHeight);

            DrawLineInBuffer(buffer, width, x1, y1, x2, y2, 51, 204, 51);
        }

        DrawTextInBuffer(buffer, width, width / 2 - 100, 50, "NMR Decay Curve", 255, 255, 255);
        DrawTextInBuffer(buffer, width, width / 2 - 50, height - 80, "Time (ms)", 255, 255, 255);
        DrawTextInBuffer(buffer, width, 40, height / 2, "Magnetization", 255, 255, 255);
    }

    private void RenderT2DistributionToBuffer(byte[] buffer, int width, int height)
    {
        var results = _currentDataset.NmrResults;
        var padding = 150;
        var plotWidth = width - padding * 2;
        var plotHeight = height - padding * 2;

        if (results.T2HistogramBins == null || results.T2Histogram == null) return;

        var xMin = Math.Log10(results.T2HistogramBins.Min());
        var xMax = Math.Log10(results.T2HistogramBins.Max());
        var yMin = 0.0;
        var yMax = results.T2Histogram.Max();

        DrawLineInBuffer(buffer, width, padding, padding, padding, padding + plotHeight, 200, 200, 200);
        DrawLineInBuffer(buffer, width, padding, padding + plotHeight, padding + plotWidth, padding + plotHeight, 200,
            200, 200);

        var barWidth = plotWidth / results.T2HistogramBins.Length;
        for (var i = 0; i < results.T2HistogramBins.Length; i++)
        {
            var x = padding + (int)((Math.Log10(results.T2HistogramBins[i]) - xMin) / (xMax - xMin) * plotWidth);
            var barHeight = (int)((results.T2Histogram[i] - yMin) / (yMax - yMin) * plotHeight);

            DrawFilledRectInBuffer(buffer, width, x, padding + plotHeight - barHeight, barWidth - 2, barHeight, 76, 153,
                230);
        }

        DrawTextInBuffer(buffer, width, width / 2 - 100, 50, "T2 Distribution", 255, 255, 255);
        DrawTextInBuffer(buffer, width, width / 2 - 50, height - 80, "T2 (ms)", 255, 255, 255);
        DrawTextInBuffer(buffer, width, 40, height / 2, "Amplitude", 255, 255, 255);
    }

    private void RenderPoreSizeDistributionToBuffer(byte[] buffer, int width, int height)
    {
        var results = _currentDataset.NmrResults;
        var padding = 150;
        var plotWidth = width - padding * 2;
        var plotHeight = height - padding * 2;

        if (results.PoreSizes == null || results.PoreSizeDistribution == null) return;

        var xMin = Math.Log10(results.PoreSizes.Min());
        var xMax = Math.Log10(results.PoreSizes.Max());
        var yMin = 0.0;
        var yMax = results.PoreSizeDistribution.Max();

        DrawLineInBuffer(buffer, width, padding, padding, padding, padding + plotHeight, 200, 200, 200);
        DrawLineInBuffer(buffer, width, padding, padding + plotHeight, padding + plotWidth, padding + plotHeight, 200,
            200, 200);

        var barWidth = plotWidth / results.PoreSizes.Length;
        for (var i = 0; i < results.PoreSizes.Length; i++)
        {
            var x = padding + (int)((Math.Log10(results.PoreSizes[i]) - xMin) / (xMax - xMin) * plotWidth);
            var barHeight = (int)((results.PoreSizeDistribution[i] - yMin) / (yMax - yMin) * plotHeight);

            DrawFilledRectInBuffer(buffer, width, x, padding + plotHeight - barHeight, barWidth - 2, barHeight, 230,
                128, 51);
        }

        DrawTextInBuffer(buffer, width, width / 2 - 150, 50, "Pore Size Distribution", 255, 255, 255);
        DrawTextInBuffer(buffer, width, width / 2 - 80, height - 80, "Pore Radius (μm)", 255, 255, 255);
        DrawTextInBuffer(buffer, width, 40, height / 2, "Frequency", 255, 255, 255);
    }

    private void DrawLineInBuffer(byte[] buffer, int width, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
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
        var results = _currentDataset.NmrResults;
        var sb = new StringBuilder();

        sb.AppendLine("Time (ms),Magnetization");
        for (var i = 0; i < results.TimePoints.Length; i++)
            sb.AppendLine($"{results.TimePoints[i]},{results.Magnetization[i]}");

        sb.AppendLine();

        sb.AppendLine("T2 (ms),Amplitude");
        for (var i = 0; i < results.T2HistogramBins.Length; i++)
            sb.AppendLine($"{results.T2HistogramBins[i]},{results.T2Histogram[i]}");

        if (results.PoreSizes != null)
        {
            sb.AppendLine();
            sb.AppendLine("Pore Radius (μm),Frequency");
            for (var i = 0; i < results.PoreSizes.Length; i++)
                sb.AppendLine($"{results.PoreSizes[i]},{results.PoreSizeDistribution[i]}");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    private void ExportReport(string filePath)
    {
        var results = _currentDataset.NmrResults;
        var voxelSizeUm = _currentDataset.GetPixelSizeInMicrometers();

        var sb = new StringBuilder();
        sb.AppendLine("NMR ANALYSIS REPORT");
        sb.AppendLine("===================");
        sb.AppendLine();
        sb.AppendLine($"Dataset: {_currentDataset.Name}");
        sb.AppendLine($"Pore Material: {results.PoreMaterial}");
        sb.AppendLine($"Analysis Date: {DateTime.Now}");
        sb.AppendLine();
        sb.AppendLine("VOXEL INFORMATION");
        sb.AppendLine("-----------------");
        sb.AppendLine($"Voxel Size: {voxelSizeUm:F2} µm ({_currentDataset.PixelSize:F2} {_currentDataset.Unit})");
        sb.AppendLine(
            $"Volume Dimensions: {_currentDataset.Width} x {_currentDataset.Height} x {_currentDataset.Depth}");
        sb.AppendLine();
        sb.AppendLine("SIMULATION PARAMETERS");
        sb.AppendLine("---------------------");
        sb.AppendLine($"Number of Walkers: {results.NumberOfWalkers:N0}");
        sb.AppendLine($"Time Steps: {results.TotalSteps}");
        sb.AppendLine($"Time Step: {results.TimeStep} ms");
        sb.AppendLine($"Computation Time: {results.ComputationTime.TotalSeconds:F2}s");
        sb.AppendLine($"Method: {results.ComputationMethod}");
        sb.AppendLine();
        sb.AppendLine("RESULTS");
        sb.AppendLine("-------");
        sb.AppendLine($"Mean T2: {results.MeanT2:F2} ms");
        sb.AppendLine($"Geometric Mean T2: {results.GeometricMeanT2:F2} ms");
        sb.AppendLine($"Peak T2: {results.T2PeakValue:F2} ms");

        if (results.PoreSizes != null)
        {
            var meanPoreSize = results.PoreSizes.Zip(results.PoreSizeDistribution, (s, d) => s * d).Sum() /
                               results.PoreSizeDistribution.Sum();
            sb.AppendLine();
            sb.AppendLine("PORE SIZE ANALYSIS");
            sb.AppendLine("------------------");
            if (meanPoreSize > 1000)
                sb.AppendLine($"Mean Pore Radius: {meanPoreSize / 1000:F3} mm");
            else if (meanPoreSize < 1)
                sb.AppendLine($"Mean Pore Radius: {meanPoreSize * 1000:F2} nm");
            else
                sb.AppendLine($"Mean Pore Radius: {meanPoreSize:F2} µm");
        }

        if (results.HasT1T2Data)
        {
            sb.AppendLine();
            sb.AppendLine("T1-T2 CORRELATION");
            sb.AppendLine("-----------------");
            sb.AppendLine($"T1/T2 Ratio: {_config.T1T2Ratio:F2}");
            sb.AppendLine($"T1 Range: {results.T1HistogramBins[0]:F2} - {results.T1HistogramBins[^1]:F2} ms");
            sb.AppendLine($"T2 Range: {results.T2HistogramBins[0]:F2} - {results.T2HistogramBins[^1]:F2} ms");
            sb.AppendLine("2D T1-T2 map computed successfully");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    private void ImportAsTableDataset()
    {
        try
        {
            var results = _currentDataset.NmrResults;
            var dataTable = new DataTable("NMR Results");

            dataTable.Columns.Add("Time_ms", typeof(double));
            dataTable.Columns.Add("Magnetization", typeof(double));
            dataTable.Columns.Add("T2_ms", typeof(double));
            dataTable.Columns.Add("T2_Amplitude", typeof(double));

            if (results.PoreSizes != null)
            {
                dataTable.Columns.Add("PoreRadius_um", typeof(double));
                dataTable.Columns.Add("PoreSize_Frequency", typeof(double));
            }

            var maxRows = Math.Max(results.TimePoints.Length, results.T2HistogramBins.Length);
            for (var i = 0; i < maxRows; i++)
            {
                var row = dataTable.NewRow();

                if (i < results.TimePoints.Length)
                {
                    row["Time_ms"] = results.TimePoints[i];
                    row["Magnetization"] = results.Magnetization[i];
                }
                else
                {
                    row["Time_ms"] = DBNull.Value;
                    row["Magnetization"] = DBNull.Value;
                }

                if (i < results.T2HistogramBins.Length)
                {
                    row["T2_ms"] = results.T2HistogramBins[i];
                    row["T2_Amplitude"] = results.T2Histogram[i];
                }
                else
                {
                    row["T2_ms"] = DBNull.Value;
                    row["T2_Amplitude"] = DBNull.Value;
                }

                if (results.PoreSizes != null && i < results.PoreSizes.Length)
                {
                    row["PoreRadius_um"] = results.PoreSizes[i];
                    row["PoreSize_Frequency"] = results.PoreSizeDistribution[i];
                }
                else if (results.PoreSizes != null)
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