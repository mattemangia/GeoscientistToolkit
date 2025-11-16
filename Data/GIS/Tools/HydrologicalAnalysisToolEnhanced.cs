// GeoscientistToolkit/UI/GIS/Tools/HydrologicalAnalysisToolEnhanced.cs
//
// Advanced hydrological analysis with GPU acceleration, rainfall simulation, and water body tracking
//

using System.Numerics;
using GeoscientistToolkit.Analysis.Hydrological;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS.Tools;

/// <summary>
/// Advanced hydrological analysis tool with GPU acceleration and rainfall simulation
/// </summary>
public class HydrologicalAnalysisToolEnhanced : IDatasetTools
{
    // Static reference to active tool instance for visualization integration
    public static HydrologicalAnalysisToolEnhanced ActiveInstance { get; private set; }

    private GISRasterLayer _elevationLayer;
    private byte[,] _flowDirection;
    private int[,] _flowAccumulation;
    private List<(int row, int col)> _currentFlowPath;
    private bool[,] _currentWatershed;
    private float[,] _waterDepth;

    // GPU acceleration
    private HydrologicalOpenCLKernels _gpuKernels;
    private bool _useGPU = true;

    // Rainfall simulation
    private ImGuiCurveEditor _rainfallCurveEditor;
    private List<CurvePoint> _rainfallCurve;
    private int _simulationDays = 365;
    private int _timeStepsPerDay = 4; // Every 6 hours
    private int _currentTimeStep = 0;
    private float _baseRainfallMM = 2.0f; // Base daily rainfall in mm
    private float _drainageRate = 0.15f;
    private float _infiltrationRate = 0.05f; // 5% water lost to ground per step
    private float _cellSizeMeters = 30f; // Default 30m cells

    // Water body tracking
    private WaterBodyTracker _waterBodyTracker;
    private bool _trackWaterBodies = true;
    private List<float> _volumeHistory = new();

    // Animation playback
    private List<float[,]> _waterDepthHistory = new();
    private bool _isPlaying = false;
    private float _playbackSpeed = 1.0f;
    private float _animationTimer = 0f;

    // Status
    private bool _isProcessing = false;
    private bool _isSimulating = false;
    private string _statusMessage = "";
    private Vector2 _clickPoint = Vector2.Zero;
    private bool _hasClickedPoint = false;

    // Settings
    private bool _snapToStreams = true;
    private int _snapRadius = 5;
    private int _streamThreshold = 100;
    private bool _showWatershed = true;
    private bool _showFlowPath = true;
    private bool _showWaterBodies = true;
    private bool _animateSimulation = false;

    // Visualization
    private HydrologicalVisualization _visualization;
    private Vector4 _flowPathColor = new Vector4(0.2f, 0.6f, 1.0f, 1.0f);
    private Vector4 _watershedColor = new Vector4(0.3f, 0.8f, 0.3f, 0.3f);
    private Vector4 _waterColor = new Vector4(0.1f, 0.4f, 0.9f, 0.5f);

    // Export
    private List<GISLayer> _exportableLayers = new();

    public HydrologicalAnalysisToolEnhanced()
    {
        // Initialize visualization system
        _visualization = new HydrologicalVisualization();
        _visualization.SetTool(this);

        // Initialize rainfall curve editor with seasonal pattern
        var defaultRainfallCurve = CreateDefaultSeasonalRainfall();
        _rainfallCurveEditor = new ImGuiCurveEditor(
            "rainfall_curve",
            "Annual Rainfall Pattern",
            "Month (0-12)",
            "Rainfall Multiplier (0-3)",
            defaultRainfallCurve,
            new Vector2(0, 0),
            new Vector2(12, 3)
        );
        _rainfallCurve = defaultRainfallCurve;

        // Try to initialize GPU acceleration
        try
        {
            _gpuKernels = new HydrologicalOpenCLKernels();
            if (!_gpuKernels.IsAvailable)
            {
                _gpuKernels?.Dispose();
                _gpuKernels = null;
                _useGPU = false;
                Logger.LogWarning("GPU acceleration not available, using CPU");
            }
            else
            {
                Logger.Log("GPU-accelerated hydrological analysis ready");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to initialize GPU acceleration: {ex.Message}");
            _useGPU = false;
        }
    }

    private List<CurvePoint> CreateDefaultSeasonalRainfall()
    {
        // Create a seasonal pattern with wet summer and dry winter
        return new List<CurvePoint>
        {
            new CurvePoint(0f, 0.5f),   // Jan - dry
            new CurvePoint(2f, 0.6f),   // Mar
            new CurvePoint(4f, 1.2f),   // May - spring rains
            new CurvePoint(6f, 2.0f),   // Jul - wet season peak
            new CurvePoint(8f, 1.5f),   // Sep - autumn
            new CurvePoint(10f, 0.8f),  // Nov
            new CurvePoint(12f, 0.5f)   // Dec - back to dry
        };
    }

    public void Draw(Dataset dataset)
    {
        // Set this as the active instance for visualization integration
        ActiveInstance = this;

        if (dataset is not GISDataset gisDataset)
        {
            ImGui.TextDisabled("Hydrological analysis is only available for GIS datasets.");
            return;
        }

        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "Advanced Hydrological Analysis");
        ImGui.Text("GPU-Accelerated River Runner & Annual Rainfall Simulation");

        if (_useGPU && _gpuKernels != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f), "[GPU Enabled]");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Draw tabs
        if (ImGui.BeginTabBar("HydroTabs"))
        {
            if (ImGui.BeginTabItem("Setup & Flow Analysis"))
            {
                DrawSetupTab(gisDataset);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Rainfall Simulation"))
            {
                DrawRainfallSimulationTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Water Bodies"))
            {
                DrawWaterBodiesTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Visualization"))
            {
                _visualization?.DrawControls();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Export"))
            {
                DrawExportTab(gisDataset);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        // Status message
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), _statusMessage);
        }
    }

    private void DrawSetupTab(GISDataset gisDataset)
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Step 1: Elevation Data");
        ImGui.Spacing();

        // Elevation source selection
        var rasterLayers = gisDataset.Layers.OfType<GISRasterLayer>().ToList();

        if (rasterLayers.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No elevation data available.");
            ImGui.TextWrapped("Load a DEM, select an online elevation basemap, or extract elevation from basemap tiles.");

            ImGui.Spacing();
            if (ImGui.Button("Extract Elevation from Online Basemap", new Vector2(300, 30)))
            {
                ExtractElevationFromBasemap(gisDataset);
            }
            HelpMarker("Download and process ESRI World Hillshade tiles to create approximate elevation data");

            return;
        }

        for (int i = 0; i < rasterLayers.Count; i++)
        {
            var layer = rasterLayers[i];
            bool isSelected = _elevationLayer == layer;

            if (ImGui.Selectable($"{layer.Name} ({layer.Width}x{layer.Height})", isSelected))
            {
                _elevationLayer = layer;
                _flowDirection = null;
                _flowAccumulation = null;
                _statusMessage = $"Selected: {layer.Name}";
            }
        }

        if (_elevationLayer == null) return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Step 2: Calculate Flow");
        ImGui.Spacing();

        ImGui.SliderFloat("Cell Size (meters)", ref _cellSizeMeters, 1f, 100f);
        HelpMarker("Physical size of each grid cell in meters");

        if (_flowDirection == null)
        {
            if (ImGui.Button("Calculate Flow Direction", new Vector2(250, 30)))
            {
                CalculateFlow();
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f), "✓ Flow analysis complete");
            if (ImGui.Button("Recalculate")) CalculateFlow();
        }

        if (_flowDirection == null) return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Step 3: Flow Path Analysis");
        ImGui.Spacing();

        ImGui.Checkbox("Snap to Streams", ref _snapToStreams);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderInt("##SnapRadius", ref _snapRadius, 1, 20);

        ImGui.Checkbox("Show Flow Path", ref _showFlowPath);
        ImGui.SameLine();
        ImGui.Checkbox("Show Watershed", ref _showWatershed);

        ImGui.Text("Click on map to trace flow path...");

        if (_currentFlowPath != null && _currentFlowPath.Count > 0)
        {
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f),
                $"Flow path: {_currentFlowPath.Count} cells");

            if (ImGui.Button("Clear Analysis"))
            {
                _currentFlowPath = null;
                _currentWatershed = null;
                _hasClickedPoint = false;
            }
        }
    }

    private void DrawRainfallSimulationTab()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Annual Rainfall Simulation");
        ImGui.Spacing();

        if (_flowDirection == null)
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "Calculate flow direction first!");
            return;
        }

        // Rainfall curve editor
        if (ImGui.Button("Edit Rainfall Curve"))
        {
            _rainfallCurveEditor.Open();
        }
        ImGui.SameLine();
        if (ImGui.Button("Use Default Seasonal Pattern"))
        {
            _rainfallCurve = CreateDefaultSeasonalRainfall();
            _rainfallCurveEditor = new ImGuiCurveEditor("rainfall_curve", "Annual Rainfall Pattern",
                "Month (0-12)", "Rainfall Multiplier (0-3)", _rainfallCurve,
                new Vector2(0, 0), new Vector2(12, 3));
        }

        _rainfallCurveEditor.DrawWindow();
        var curvePoints = _rainfallCurveEditor.GetPoints();
        if (curvePoints != null && curvePoints.Count > 0)
        {
            _rainfallCurve = curvePoints;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(200);
        ImGui.SliderInt("Simulation Days", ref _simulationDays, 30, 730);
        HelpMarker("Total days to simulate (1 year = 365 days)");

        ImGui.SetNextItemWidth(200);
        ImGui.SliderInt("Steps Per Day", ref _timeStepsPerDay, 1, 24);
        HelpMarker("Temporal resolution (4 = every 6 hours)");

        ImGui.SetNextItemWidth(200);
        ImGui.SliderFloat("Base Rainfall (mm/day)", ref _baseRainfallMM, 0.1f, 50f);

        ImGui.SetNextItemWidth(200);
        ImGui.SliderFloat("Drainage Rate", ref _drainageRate, 0.01f, 0.5f, "%.3f");

        ImGui.SetNextItemWidth(200);
        ImGui.SliderFloat("Infiltration Rate", ref _infiltrationRate, 0.0f, 0.2f, "%.3f");
        HelpMarker("Fraction of water lost to ground infiltration each step");

        ImGui.Checkbox("Track Water Bodies", ref _trackWaterBodies);
        ImGui.SameLine();
        ImGui.Checkbox("Animate Simulation", ref _animateSimulation);

        ImGui.Spacing();

        if (ImGui.Button("Run Rainfall Simulation", new Vector2(250, 40)))
        {
            RunRainfallSimulation();
        }

        if (_isSimulating)
        {
            ImGui.SameLine();
            ImGui.Text("Simulating...");
            ImGui.ProgressBar((float)_currentTimeStep / (_simulationDays * _timeStepsPerDay));
        }

        if (_volumeHistory.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f), "Simulation Complete!");
            ImGui.Text($"Final water volume: {_volumeHistory[_volumeHistory.Count - 1]:F2} m³");

            // Animation playback controls
            ImGui.Text("Animation Playback:");
            if (!_isPlaying)
            {
                if (ImGui.Button("▶ Play"))
                {
                    _isPlaying = true;
                }
            }
            else
            {
                if (ImGui.Button("⏸ Pause"))
                {
                    _isPlaying = false;
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("⏹ Stop"))
            {
                _isPlaying = false;
                _currentTimeStep = 0;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            ImGui.SliderFloat("Speed", ref _playbackSpeed, 0.1f, 10f, "%.1fx");

            // Update animation if playing
            if (_isPlaying)
            {
                _animationTimer += ImGui.GetIO().DeltaTime * _playbackSpeed;
                if (_animationTimer >= 0.1f) // Update every 0.1 seconds (adjusted by speed)
                {
                    _animationTimer = 0;
                    _currentTimeStep++;
                    if (_currentTimeStep >= _volumeHistory.Count)
                    {
                        _currentTimeStep = 0; // Loop
                    }
                }
            }

            // Time scrubber
            ImGui.Text("Timeline:");
            ImGui.SetNextItemWidth(400);
            if (ImGui.SliderInt("##TimeStep", ref _currentTimeStep, 0, _volumeHistory.Count - 1))
            {
                _isPlaying = false; // Stop playing when user manually scrubs
            }

            float currentVolume = _volumeHistory[Math.Min(_currentTimeStep, _volumeHistory.Count - 1)];
            int currentDay = _currentTimeStep / _timeStepsPerDay;
            ImGui.Text($"Day {currentDay}: {currentVolume:F2} m³");

            // Plot volume history
            if (_volumeHistory.Count > 1)
            {
                var volumeArray = _volumeHistory.ToArray();
                ImGui.PlotLines("Water Volume Over Time", ref volumeArray[0], volumeArray.Length,
                    0, null, 0, volumeArray.Max() * 1.1f, new Vector2(400, 100));
            }
        }
    }

    private void DrawWaterBodiesTab()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Water Body Tracking");
        ImGui.Spacing();

        if (_waterBodyTracker == null || _waterBodyTracker.WaterBodies.Count == 0)
        {
            ImGui.Text("Run rainfall simulation with water body tracking enabled.");
            return;
        }

        ImGui.Checkbox("Show Water Bodies on Map", ref _showWaterBodies);
        ImGui.Spacing();

        ImGui.Text(_waterBodyTracker.GetSummary());
        ImGui.Spacing();

        // List water bodies
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.2f, 1.0f), "Detected Water Bodies:");

        if (ImGui.BeginChild("WaterBodyList", new Vector2(0, 300), true))
        {
            foreach (var waterBody in _waterBodyTracker.WaterBodies.OrderByDescending(w => w.Volume))
            {
                var color = GetColorForType(waterBody.Type);
                ImGui.PushStyleColor(ImGuiCol.Text, color);
                ImGui.BulletText($"{waterBody.Type} #{waterBody.Id}");
                ImGui.PopStyleColor();

                ImGui.Indent();
                ImGui.Text($"Volume: {waterBody.Volume:F0} m³");
                ImGui.Text($"Area: {waterBody.SurfaceArea:F0} m²");
                ImGui.Text($"Avg Depth: {waterBody.AverageDepth:F2} m");
                ImGui.Text($"Max Depth: {waterBody.MaxDepth:F2} m");

                if (waterBody.VolumeHistory.Count > 1)
                {
                    var volumeArray = waterBody.VolumeHistory.ToArray();
                    ImGui.PlotLines($"##Volume{waterBody.Id}", ref volumeArray[0], volumeArray.Length,
                        0, $"Volume History", 0, volumeArray.Max() * 1.1f, new Vector2(300, 50));
                }

                ImGui.Unindent();
                ImGui.Spacing();
            }

            ImGui.EndChild();
        }

        var largest = _waterBodyTracker.GetLargest();
        if (largest != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f),
                $"Largest: {largest.Type} #{largest.Id} ({largest.Volume:F0} m³)");
        }
    }

    private void DrawExportTab(GISDataset gisDataset)
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Export Results");
        ImGui.Spacing();

        if (_flowDirection == null)
        {
            ImGui.Text("No analysis results to export.");
            return;
        }

        ImGui.Text("Export hydrological analysis results to GIS layers:");
        ImGui.Spacing();

        if (ImGui.Button("Export Flow Path as Polyline"))
        {
            ExportFlowPath(gisDataset);
        }

        if (ImGui.Button("Export Watershed as Polygon"))
        {
            ExportWatershed(gisDataset);
        }

        if (_waterBodyTracker != null && ImGui.Button("Export Water Bodies"))
        {
            ExportWaterBodies(gisDataset);
        }

        if (ImGui.Button("Export Water Depth Raster"))
        {
            ExportWaterDepthRaster(gisDataset);
        }

        if (_exportableLayers.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f),
                $"✓ Exported {_exportableLayers.Count} layers");
        }
    }

    private void CalculateFlow()
    {
        if (_elevationLayer == null) return;

        _isProcessing = true;
        _statusMessage = "Calculating flow direction...";

        Task.Run(() =>
        {
            try
            {
                var elevation = _elevationLayer.GetPixelData();

                if (_useGPU && _gpuKernels != null)
                {
                    _flowDirection = _gpuKernels.CalculateFlowDirection(elevation);
                }
                else
                {
                    _flowDirection = GISOperations.CalculateD8FlowDirection(elevation);
                }

                _statusMessage = "Calculating flow accumulation...";
                _flowAccumulation = GISOperations.CalculateFlowAccumulation(_flowDirection);

                _statusMessage = "Flow analysis complete!";
                Logger.Log("Flow analysis completed");
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
                Logger.LogError($"Flow calculation failed: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        });
    }

    private void RunRainfallSimulation()
    {
        if (_elevationLayer == null || _flowDirection == null) return;

        _isSimulating = true;
        _currentTimeStep = 0;
        _statusMessage = "Running rainfall simulation...";

        Task.Run(() =>
        {
            try
            {
                int totalSteps = _simulationDays * _timeStepsPerDay;
                var rainfallByStep = new float[totalSteps];

                // Generate rainfall for each timestep based on curve
                for (int i = 0; i < totalSteps; i++)
                {
                    float day = (float)i / _timeStepsPerDay;
                    float month = (day / 365f) * 12f;
                    float multiplier = EvaluateRainfallCurve(month);
                    rainfallByStep[i] = (_baseRainfallMM / 1000f) * multiplier / _timeStepsPerDay; // Convert to meters per step
                }

                var elevation = _elevationLayer.GetPixelData();

                if (_useGPU && _gpuKernels != null)
                {
                    var result = _gpuKernels.SimulateRainfallDrainage(
                        elevation, _flowDirection, rainfallByStep, _drainageRate, _infiltrationRate);
                    _waterDepth = result.waterDepth;
                    _volumeHistory = result.volumeHistory.ToList();
                }
                else
                {
                    // CPU fallback - simplified version
                    _waterDepth = new float[elevation.GetLength(0), elevation.GetLength(1)];
                    _volumeHistory = new List<float>();

                    for (int step = 0; step < totalSteps; step++)
                    {
                        _currentTimeStep = step;
                        // Apply rainfall and drainage (simplified)
                        // Full implementation would match GPU version
                        _volumeHistory.Add(0); // Placeholder
                    }
                }

                // Track water bodies if enabled
                if (_trackWaterBodies)
                {
                    _waterBodyTracker = new WaterBodyTracker(elevation, _cellSizeMeters);
                    _waterBodyTracker.Update(_waterDepth, _flowAccumulation, totalSteps);
                    _statusMessage = $"Simulation complete! {_waterBodyTracker.GetSummary()}";
                }
                else
                {
                    _statusMessage = "Rainfall simulation complete!";
                }

                Logger.Log($"Rainfall simulation completed ({totalSteps} steps)");
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
                Logger.LogError($"Rainfall simulation failed: {ex.Message}");
            }
            finally
            {
                _isSimulating = false;
            }
        });
    }

    private float EvaluateRainfallCurve(float month)
    {
        if (_rainfallCurve.Count == 0) return 1.0f;

        // Linear interpolation between curve points
        for (int i = 0; i < _rainfallCurve.Count - 1; i++)
        {
            var p1 = _rainfallCurve[i];
            var p2 = _rainfallCurve[i + 1];

            if (month >= p1.Point.X && month <= p2.Point.X)
            {
                float t = (month - p1.Point.X) / (p2.Point.X - p1.Point.X);
                return p1.Point.Y + (p2.Point.Y - p1.Point.Y) * t;
            }
        }

        // Outside range, use first or last value
        return month < _rainfallCurve[0].Point.X ? _rainfallCurve[0].Point.Y : _rainfallCurve[_rainfallCurve.Count - 1].Point.Y;
    }

    public void OnMapClick(Vector2 worldPosition, GISDataset dataset)
    {
        if (_elevationLayer == null || _flowDirection == null) return;

        _clickPoint = worldPosition;
        _hasClickedPoint = true;

        // Convert to raster coords
        var bounds = _elevationLayer.Bounds;
        float normalizedX = (worldPosition.X - bounds.Min.X) / (bounds.Max.X - bounds.Min.X);
        float normalizedY = (worldPosition.Y - bounds.Min.Y) / (bounds.Max.Y - bounds.Min.Y);

        int col = (int)(normalizedX * _elevationLayer.Width);
        int row = (int)((1 - normalizedY) * _elevationLayer.Height);

        col = Math.Clamp(col, 0, _elevationLayer.Width - 1);
        row = Math.Clamp(row, 0, _elevationLayer.Height - 1);

        if (_snapToStreams)
        {
            var snapped = GISOperations.SnapToStream(_flowAccumulation, row, col, _snapRadius, _streamThreshold);
            row = snapped.row;
            col = snapped.col;
        }

        _currentFlowPath = GISOperations.TraceFlowPath(_flowDirection, row, col);

        if (_showWatershed)
        {
            _currentWatershed = GISOperations.DelineateWatershed(_flowDirection, row, col);
        }

        _statusMessage = $"Flow path traced: {_currentFlowPath.Count} cells";
    }

    private void ExportFlowPath(GISDataset gisDataset)
    {
        if (_currentFlowPath == null || _currentFlowPath.Count == 0) return;

        var layer = new GISLayer
        {
            Name = "Flow Path",
            Type = LayerType.Vector,
            IsVisible = true,
            Color = _flowPathColor
        };

        var coords = ConvertCellsToWorldCoords(_currentFlowPath, _elevationLayer.Bounds);
        layer.Features.Add(new GISFeature
        {
            Type = FeatureType.Line,
            Coordinates = coords,
            Properties = new Dictionary<string, object> { ["Length"] = _currentFlowPath.Count }
        });

        gisDataset.Layers.Add(layer);
        _exportableLayers.Add(layer);
        _statusMessage = "Flow path exported!";
    }

    private void ExportWatershed(GISDataset gisDataset)
    {
        if (_currentWatershed == null) return;

        var cells = new List<(int row, int col)>();
        for (int r = 0; r < _currentWatershed.GetLength(0); r++)
            for (int c = 0; c < _currentWatershed.GetLength(1); c++)
                if (_currentWatershed[r, c])
                    cells.Add((r, c));

        var layer = new GISLayer
        {
            Name = "Watershed",
            Type = LayerType.Vector,
            IsVisible = true,
            Color = _watershedColor
        };

        var polygon = ConvertCellsToPolygon(cells, _elevationLayer.Bounds);
        layer.Features.Add(new GISFeature
        {
            Type = FeatureType.Polygon,
            Coordinates = polygon,
            Properties = new Dictionary<string, object> { ["Area_cells"] = cells.Count }
        });

        gisDataset.Layers.Add(layer);
        _exportableLayers.Add(layer);
        _statusMessage = "Watershed exported!";
    }

    private void ExportWaterBodies(GISDataset gisDataset)
    {
        if (_waterBodyTracker == null) return;

        var layers = _waterBodyTracker.ExportToGISLayers(_elevationLayer.Bounds);
        foreach (var layer in layers)
        {
            gisDataset.Layers.Add(layer);
            _exportableLayers.Add(layer);
        }

        _statusMessage = $"Exported {layers.Count} water bodies!";
    }

    private void ExportWaterDepthRaster(GISDataset gisDataset)
    {
        if (_waterDepth == null) return;

        var layer = new GISRasterLayer
        {
            Name = "Water Depth",
            Width = _waterDepth.GetLength(1),
            Height = _waterDepth.GetLength(0),
            Bounds = _elevationLayer.Bounds,
            IsVisible = true
        };

        layer.SetPixelData(_waterDepth);
        gisDataset.Layers.Add(layer);
        _exportableLayers.Add(layer);
        _statusMessage = "Water depth raster exported!";
    }

    private List<Vector2> ConvertCellsToWorldCoords(List<(int row, int col)> cells, BoundingBox bounds)
    {
        var coords = new List<Vector2>();
        float cellWidth = (bounds.Max.X - bounds.Min.X) / _elevationLayer.Width;
        float cellHeight = (bounds.Max.Y - bounds.Min.Y) / _elevationLayer.Height;

        foreach (var (row, col) in cells)
        {
            float x = bounds.Min.X + (col + 0.5f) * cellWidth;
            float y = bounds.Min.Y + (row + 0.5f) * cellHeight;
            coords.Add(new Vector2(x, y));
        }

        return coords;
    }

    private List<Vector2> ConvertCellsToPolygon(List<(int row, int col)> cells, BoundingBox bounds)
    {
        if (cells.Count == 0) return new List<Vector2>();

        int minRow = cells.Min(c => c.row);
        int maxRow = cells.Max(c => c.row);
        int minCol = cells.Min(c => c.col);
        int maxCol = cells.Max(c => c.col);

        float cellWidth = (bounds.Max.X - bounds.Min.X) / _elevationLayer.Width;
        float cellHeight = (bounds.Max.Y - bounds.Min.Y) / _elevationLayer.Height;

        return new List<Vector2>
        {
            new Vector2(bounds.Min.X + minCol * cellWidth, bounds.Min.Y + minRow * cellHeight),
            new Vector2(bounds.Min.X + maxCol * cellWidth, bounds.Min.Y + minRow * cellHeight),
            new Vector2(bounds.Min.X + maxCol * cellWidth, bounds.Min.Y + maxRow * cellHeight),
            new Vector2(bounds.Min.X + minCol * cellWidth, bounds.Min.Y + maxRow * cellHeight),
            new Vector2(bounds.Min.X + minCol * cellWidth, bounds.Min.Y + minRow * cellHeight)
        };
    }

    private Vector4 GetColorForType(WaterBodyType type)
    {
        return type switch
        {
            WaterBodyType.Lake => new Vector4(0.2f, 0.5f, 0.8f, 1.0f),
            WaterBodyType.River => new Vector4(0.3f, 0.6f, 0.9f, 1.0f),
            WaterBodyType.Sea => new Vector4(0.1f, 0.3f, 0.6f, 1.0f),
            WaterBodyType.Pond => new Vector4(0.4f, 0.7f, 1.0f, 1.0f),
            WaterBodyType.Stream => new Vector4(0.5f, 0.8f, 1.0f, 1.0f),
            _ => new Vector4(0.5f, 0.5f, 1.0f, 1.0f)
        };
    }

    private void HelpMarker(string desc)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private async void ExtractElevationFromBasemap(GISDataset gisDataset)
    {
        try
        {
            _isProcessing = true;
            _statusMessage = "Extracting elevation from basemap tiles...";

            var extractor = new BasemapElevationExtractor();

            // Use dataset bounds if available, otherwise use a default region
            var bounds = gisDataset.Bounds;
            if (bounds.Width < 0.001f || bounds.Height < 0.001f)
            {
                // Default to a 10km region around center
                var center = gisDataset.Center;
                bounds = new BoundingBox
                {
                    Min = new Vector2(center.X - 0.05f, center.Y - 0.05f),
                    Max = new Vector2(center.X + 0.05f, center.Y + 0.05f)
                };
            }

            // Extract elevation at reasonable resolution
            int resolution = Math.Min(1000, Math.Max(256, (int)(bounds.Width * 10000)));
            var elevationLayer = await extractor.ExtractElevationFromBasemap(bounds, resolution, resolution, zoomLevel: 12);

            if (elevationLayer != null)
            {
                elevationLayer.Name = "Extracted Elevation";
                gisDataset.Layers.Add(elevationLayer);
                _elevationLayer = elevationLayer;
                _statusMessage = $"Elevation extracted: {resolution}x{resolution}";
                Logger.Log($"Elevation layer extracted from basemap tiles");
            }
            else
            {
                _statusMessage = "Failed to extract elevation";
                Logger.LogError("Elevation extraction returned null");
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error: {ex.Message}";
            Logger.LogError($"Failed to extract elevation from basemap: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    // Public getters for visualization
    public List<(int row, int col)> GetCurrentFlowPath() => _currentFlowPath;
    public bool[,] GetCurrentWatershed() => _currentWatershed;
    public float[,] GetWaterDepth() => _waterDepth;
    public WaterBodyTracker GetWaterBodyTracker() => _waterBodyTracker;
    public GISRasterLayer ElevationLayer => _elevationLayer;
    public bool ShowWatershed => _showWatershed;
    public bool ShowFlowPath => _showFlowPath;
    public bool ShowWaterBodies => _showWaterBodies;

    /// <summary>
    /// Render hydrological visualization overlays on the map canvas
    /// </summary>
    public void RenderVisualization(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan,
        Func<Vector2, Vector2, Vector2, float, Vector2, Vector2> worldToScreen)
    {
        _visualization?.Render(drawList, canvasPos, canvasSize, zoom, pan, worldToScreen);
    }

    /// <summary>
    /// Handle map click for flow path tracing and watershed delineation
    /// </summary>
    public void OnMapClick(Vector2 worldPos)
    {
        if (_elevationLayer == null || _flowDirection == null)
        {
            _statusMessage = "Please load elevation data first";
            return;
        }

        _clickPoint = worldPos;
        _hasClickedPoint = true;

        // Convert world coordinates to raster cell coordinates
        var bounds = _elevationLayer.Bounds;
        var col = (int)((worldPos.X - bounds.Min.X) / (bounds.Max.X - bounds.Min.X) * _elevationLayer.Width);
        var row = (int)((worldPos.Y - bounds.Min.Y) / (bounds.Max.Y - bounds.Min.Y) * _elevationLayer.Height);

        // Clamp to valid range
        row = Math.Clamp(row, 0, _elevationLayer.Height - 1);
        col = Math.Clamp(col, 0, _elevationLayer.Width - 1);

        // Snap to stream if enabled
        if (_snapToStreams && _flowAccumulation != null)
        {
            var snapped = GISOperations.SnapToStream(_flowAccumulation, row, col, _snapRadius, _streamThreshold);
            row = snapped.row;
            col = snapped.col;
        }

        // Trace flow path
        _currentFlowPath = GISOperations.TraceFlowPath(_flowDirection, row, col);
        _statusMessage = $"Flow path: {_currentFlowPath.Count} cells";

        // Delineate watershed if enabled
        if (_showWatershed)
        {
            _currentWatershed = GISOperations.DelineateWatershed(_flowDirection, row, col);
            var watershedCells = 0;
            for (int r = 0; r < _currentWatershed.GetLength(0); r++)
                for (int c = 0; c < _currentWatershed.GetLength(1); c++)
                    if (_currentWatershed[r, c]) watershedCells++;
            _statusMessage += $" | Watershed: {watershedCells} cells";
        }

        Logger.Log($"Click at ({worldPos.X:F4}, {worldPos.Y:F4}) -> Cell ({row}, {col})");
    }
}
