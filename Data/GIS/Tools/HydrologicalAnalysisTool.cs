// GeoscientistToolkit/UI/GIS/Tools/HydrologicalAnalysisTool.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using GISOperations = GeoscientistToolkit.Business.GIS.GISOperationsImpl;
using GeoscientistToolkit.Business.GIS;

namespace GeoscientistToolkit.UI.GIS.Tools;

/// <summary>
/// Hydrological analysis tool similar to River Runner - trace flow paths and simulate flooding
/// </summary>
public class HydrologicalAnalysisTool : IDatasetTools
{
    private GISRasterLayer _elevationLayer;
    private byte[,] _flowDirection;
    private int[,] _flowAccumulation;
    private List<(int row, int col)> _currentFlowPath;
    private bool[,] _currentWatershed;
    private float[,] _floodDepth;
    private List<FloodTimeStep> _floodHistory;

    private bool _isProcessing = false;
    private string _statusMessage = "";
    private Vector2 _clickPoint = Vector2.Zero;
    private bool _hasClickedPoint = false;

    // Settings
    private bool _snapToStreams = true;
    private int _snapRadius = 5;
    private int _streamThreshold = 100;
    private bool _showWatershed = true;
    private bool _showFlowPath = true;

    // Flood simulation settings
    private float _initialFloodDepth = 1.0f;
    private float _drainageRate = 0.1f;
    private int _simulationSteps = 100;
    private bool _isFloodSimulating = false;
    private int _currentFloodStep = 0;

    // Visualization
    private Vector4 _flowPathColor = new Vector4(0.2f, 0.6f, 1.0f, 1.0f);
    private Vector4 _watershedColor = new Vector4(0.3f, 0.8f, 0.3f, 0.3f);
    private Vector4 _floodColor = new Vector4(0.1f, 0.3f, 0.8f, 0.5f);

    public void Draw(Dataset dataset)
    {
        if (dataset is not GISDataset gisDataset)
        {
            ImGui.TextDisabled("Hydrological analysis is only available for GIS datasets.");
            return;
        }

        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "River Runner & Flood Simulation");
        ImGui.TextWrapped("Click on the map to trace where a raindrop would flow to the sea, or simulate flooding.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Step 1: Select elevation data source
        DrawElevationSourceSelection(gisDataset);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (_elevationLayer == null)
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "Select an elevation data source to begin analysis.");
            return;
        }

        // Step 2: Process elevation data
        DrawProcessingSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (_flowDirection == null)
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "Click 'Calculate Flow' to analyze terrain.");
            return;
        }

        // Step 3: Flow path analysis
        DrawFlowPathSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Step 4: Flood simulation
        DrawFloodSimulationSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Status message
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), _statusMessage);
        }
    }

    private void DrawElevationSourceSelection(GISDataset gisDataset)
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Step 1: Select Elevation Data");
        ImGui.Spacing();

        var rasterLayers = gisDataset.Layers
            .OfType<GISRasterLayer>()
            .ToList();

        if (rasterLayers.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No elevation data available.");
            ImGui.TextWrapped("Load a DEM (Digital Elevation Model) or GeoTIFF with elevation data.");

            if (ImGui.Button("Load Elevation GeoTIFF..."))
            {
                // TODO: Open file dialog to load GeoTIFF
                Logger.Log("GeoTIFF loading dialog would open here");
            }

            return;
        }

        ImGui.Text("Available elevation layers:");

        for (int i = 0; i < rasterLayers.Count; i++)
        {
            var layer = rasterLayers[i];
            bool isSelected = _elevationLayer == layer;

            if (ImGui.Selectable($"{layer.Name} ({layer.Width}x{layer.Height})", isSelected))
            {
                _elevationLayer = layer;
                _flowDirection = null; // Reset analysis
                _flowAccumulation = null;
                _currentFlowPath = null;
                _currentWatershed = null;
                _statusMessage = $"Selected elevation layer: {layer.Name}";
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }
    }

    private void DrawProcessingSection()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Step 2: Calculate Flow Direction");
        ImGui.Spacing();

        if (_flowDirection == null)
        {
            if (ImGui.Button("Calculate Flow Direction & Accumulation", new Vector2(300, 30)))
            {
                CalculateHydrology();
            }

            ImGui.SameLine();
            HelpMarker("Analyzes terrain to determine how water flows downhill using D8 algorithm.");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f), "✓ Flow analysis complete");

            if (ImGui.Button("Recalculate"))
            {
                CalculateHydrology();
            }
        }
    }

    private void DrawFlowPathSection()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Step 3: Trace Flow Path (River Runner)");
        ImGui.Spacing();

        ImGui.Checkbox("Snap to Streams", ref _snapToStreams);
        HelpMarker("Automatically snap clicked points to nearest stream channel");

        if (_snapToStreams)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.SliderInt("##SnapRadius", ref _snapRadius, 1, 20);
            ImGui.SameLine();
            ImGui.Text("pixels");

            ImGui.SetNextItemWidth(150);
            ImGui.SliderInt("Stream Threshold", ref _streamThreshold, 10, 1000);
            HelpMarker("Minimum flow accumulation to be considered a stream");
        }

        ImGui.Checkbox("Show Flow Path", ref _showFlowPath);
        ImGui.SameLine();
        ImGui.Checkbox("Show Watershed", ref _showWatershed);

        ImGui.Spacing();

        if (_hasClickedPoint)
        {
            ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f),
                $"Click point: ({_clickPoint.X:F2}, {_clickPoint.Y:F2})");

            if (_currentFlowPath != null && _currentFlowPath.Count > 0)
            {
                ImGui.Text($"Flow path length: {_currentFlowPath.Count} cells");

                var lastCell = _currentFlowPath[_currentFlowPath.Count - 1];
                bool reachedEdge = lastCell.row < 0 || lastCell.row >= _elevationLayer.Height ||
                                  lastCell.col < 0 || lastCell.col >= _elevationLayer.Width;

                if (reachedEdge)
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.7f, 1.0f, 1.0f), "→ Flows to the sea/outlet");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f), "→ Ends in a sink/depression");
                }
            }

            if (_currentWatershed != null)
            {
                int watershedArea = 0;
                for (int r = 0; r < _currentWatershed.GetLength(0); r++)
                    for (int c = 0; c < _currentWatershed.GetLength(1); c++)
                        if (_currentWatershed[r, c]) watershedArea++;

                ImGui.Text($"Watershed area: {watershedArea} cells");
            }

            if (ImGui.Button("Clear Analysis"))
            {
                _currentFlowPath = null;
                _currentWatershed = null;
                _hasClickedPoint = false;
                _floodDepth = null;
                _floodHistory = null;
                _statusMessage = "";
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0.3f, 1), "Click on the map to trace flow path...");
        }
    }

    private void DrawFloodSimulationSection()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Step 4: Flood Simulation");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(200);
        ImGui.SliderFloat("Initial Flood Depth (m)", ref _initialFloodDepth, 0.1f, 10.0f);

        ImGui.SetNextItemWidth(200);
        ImGui.SliderFloat("Drainage Rate", ref _drainageRate, 0.01f, 0.5f, "%.3f");
        HelpMarker("How quickly water drains per time step (higher = faster drainage)");

        ImGui.SetNextItemWidth(200);
        ImGui.SliderInt("Simulation Steps", ref _simulationSteps, 10, 500);
        HelpMarker("Number of time steps to simulate");

        ImGui.Spacing();

        if (ImGui.Button("Simulate Flooding", new Vector2(200, 30)))
        {
            SimulateFlooding();
        }

        if (_floodHistory != null && _floodHistory.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear Flood Simulation"))
            {
                _floodDepth = null;
                _floodHistory = null;
                _isFloodSimulating = false;
                _currentFloodStep = 0;
            }
        }

        if (_floodHistory != null && _floodHistory.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f), "Flood Simulation Results:");

            int completedSteps = _floodHistory.Count;
            var finalStep = _floodHistory[_floodHistory.Count - 1];

            ImGui.Text($"Simulation ran for {completedSteps} time steps");
            ImGui.Text($"Final water volume: {finalStep.TotalWaterVolume:F2} cubic meters");
            ImGui.Text($"Flooded cells remaining: {finalStep.FloodedCellCount}");

            if (finalStep.TotalWaterVolume < 0.01f)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f),
                    $"✓ Area fully drained after {completedSteps} steps");
            }
            else
            {
                ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f),
                    "⚠ Area not fully drained (increase simulation steps)");
            }

            // Timeline scrubber
            ImGui.Spacing();
            ImGui.Text("Flood Timeline:");
            ImGui.SetNextItemWidth(400);
            if (ImGui.SliderInt("##FloodStep", ref _currentFloodStep, 0, completedSteps - 1))
            {
                // Update visualization to this time step (would need to recalculate or store intermediate states)
                _statusMessage = $"Viewing flood at step {_currentFloodStep}";
            }

            var stepData = _floodHistory[Math.Min(_currentFloodStep, completedSteps - 1)];
            ImGui.Text($"Water volume: {stepData.TotalWaterVolume:F2} m³");
            ImGui.Text($"Flooded cells: {stepData.FloodedCellCount}");
            ImGui.Text($"Average depth: {stepData.AverageDepth:F3} m");
        }
    }

    private void CalculateHydrology()
    {
        if (_elevationLayer == null)
            return;

        _isProcessing = true;
        _statusMessage = "Calculating flow direction...";

        Task.Run(() =>
        {
            try
            {
                var elevation = _elevationLayer.GetPixelData();

                // Calculate flow direction using D8 algorithm
                _flowDirection = GISOperations.CalculateD8FlowDirection(elevation);
                _statusMessage = "Calculating flow accumulation...";

                // Calculate flow accumulation
                _flowAccumulation = GISOperations.CalculateFlowAccumulation(_flowDirection);

                _statusMessage = "Flow analysis complete!";
                Logger.Log("Hydrological analysis completed successfully");
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
                Logger.LogError($"Hydrological analysis failed: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        });
    }

    private void SimulateFlooding()
    {
        if (_elevationLayer == null || _flowDirection == null)
            return;

        _isFloodSimulating = true;
        _statusMessage = "Simulating flooding...";

        Task.Run(() =>
        {
            try
            {
                var elevation = _elevationLayer.GetPixelData();

                var result = GISOperations.SimulateFlooding(
                    elevation,
                    _flowDirection,
                    _initialFloodDepth,
                    _drainageRate,
                    _simulationSteps);

                _floodDepth = result.waterDepth;
                _floodHistory = result.timeSteps;
                _currentFloodStep = _floodHistory.Count - 1;

                _statusMessage = $"Flood simulation complete! ({_floodHistory.Count} steps)";
                Logger.Log($"Flood simulation completed in {_floodHistory.Count} steps");
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
                Logger.LogError($"Flood simulation failed: {ex.Message}");
            }
            finally
            {
                _isFloodSimulating = false;
            }
        });
    }

    public void OnMapClick(Vector2 worldPosition, GISDataset dataset)
    {
        if (_elevationLayer == null || _flowDirection == null || _flowAccumulation == null)
            return;

        _clickPoint = worldPosition;
        _hasClickedPoint = true;

        // Convert world position to raster coordinates
        var bounds = _elevationLayer.Bounds;
        float normalizedX = (worldPosition.X - bounds.Min.X) / (bounds.Max.X - bounds.Min.X);
        float normalizedY = (worldPosition.Y - bounds.Min.Y) / (bounds.Max.Y - bounds.Min.Y);

        int col = (int)(normalizedX * _elevationLayer.Width);
        int row = (int)((1 - normalizedY) * _elevationLayer.Height); // Flip Y

        // Clamp to valid range
        col = Math.Clamp(col, 0, _elevationLayer.Width - 1);
        row = Math.Clamp(row, 0, _elevationLayer.Height - 1);

        // Snap to stream if enabled
        if (_snapToStreams)
        {
            var snapped = GISOperations.SnapToStream(_flowAccumulation, row, col, _snapRadius, _streamThreshold);
            row = snapped.row;
            col = snapped.col;
        }

        // Trace flow path
        _currentFlowPath = GISOperations.TraceFlowPath(_flowDirection, row, col);

        // Delineate watershed
        if (_showWatershed)
        {
            _currentWatershed = GISOperations.DelineateWatershed(_flowDirection, row, col);
        }

        _statusMessage = $"Flow path traced from ({row}, {col})";
        Logger.Log($"Traced flow path: {_currentFlowPath.Count} cells");
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

    // Public methods for visualization (to be called from GISViewer)
    public List<(int row, int col)> GetCurrentFlowPath() => _currentFlowPath;
    public bool[,] GetCurrentWatershed() => _currentWatershed;
    public float[,] GetFloodDepth() => _floodDepth;
    public bool ShowFlowPath => _showFlowPath;
    public bool ShowWatershed => _showWatershed;
    public Vector4 FlowPathColor => _flowPathColor;
    public Vector4 WatershedColor => _watershedColor;
    public Vector4 FloodColor => _floodColor;
    public GISRasterLayer ElevationLayer => _elevationLayer;
}
