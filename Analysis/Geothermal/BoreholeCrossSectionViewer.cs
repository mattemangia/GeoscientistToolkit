// GeoscientistToolkit/UI/Visualization/BoreholeCrossSectionViewer.cs
// FIXED: Correctly displays heat exchanger fluid temperatures in coaxial systems
// FIXED: IndexOutOfRangeException in interpolation due to negative angles.
// FIXED: Color map display to show the full temperature range.
// REFACTORED: Replaced OxyPlot with a custom ImGui renderer to ensure correct color mapping and remove external dependencies.

using System.Numerics;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Visualization;

/// <summary>
/// Fixed 2D visualization of borehole cross-section with correct fluid temperature display
/// </summary>
public class BoreholeCrossSectionViewer
{
    public enum ViewMode
    {
        CombinedTemperature,  // NEW: Shows ground + fluid temperatures correctly
        GroundTemperature,    // Ground temperature only
        FluidCirculation,     // Shows fluid flow pattern and temps
        Velocity,
        FluidTemperatureProfile,
        Debug
    }

    private ViewMode _currentView = ViewMode.CombinedTemperature;
    private float _depthPosition = 0.5f;
    private GeothermalMesh _mesh;
    private GeothermalSimulationOptions _options;
    private GeothermalSimulationResults _results;
    private float _selectedDepthMeters;
    private bool _showGrid = true;
    private bool _showLegend = true;
    private float _viewScale = 1.0f;
    private bool _autoScale = true;
    private bool _autoColorScale = true;  // NEW: Auto-adjust color scale
    private float _minTempScale = 10f;
    private float _maxTempScale = 90f;

    public void LoadResults(GeothermalSimulationResults results, GeothermalMesh mesh,
        GeothermalSimulationOptions options)
    {
        _results = results;
        _mesh = mesh;
        _options = options;
        _selectedDepthMeters = (float)(_options.BoreholeDataset.TotalDepth * 0.5);
    }

    public void RenderControls()
    {
        if (_results == null || _mesh == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Load simulation results to view borehole cross-section");
            return;
        }

        ImGui.Text("Borehole Cross-Section Viewer");
        ImGui.Separator();

        // View mode selector
        if (ImGui.BeginCombo("View Mode", _currentView.ToString()))
        {
            foreach (var mode in Enum.GetValues<ViewMode>())
                if (ImGui.Selectable(mode.ToString(), _currentView == mode))
                    _currentView = mode;
            ImGui.EndCombo();
        }

        // Depth slider
        var depthMeters = _selectedDepthMeters;
        if (ImGui.SliderFloat("Depth (m)", ref depthMeters, 0f, (float)_options.BoreholeDataset.TotalDepth))
        {
            _selectedDepthMeters = depthMeters;
            _depthPosition = depthMeters / (float)_options.BoreholeDataset.TotalDepth;
        }

        ImGui.Text($"At depth: {_selectedDepthMeters:F1} m ({_depthPosition * 100:F0}%)");

        // Get fluid temperatures at this depth
        if (_results.FluidTemperatureProfile?.Any() == true)
        {
            var fluidPoint = GetFluidTemperaturesAtDepth(_selectedDepthMeters);
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 1f, 1), $"Downflow fluid: {fluidPoint.down - 273.15:F1}°C");
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1), $"Upflow fluid: {fluidPoint.up - 273.15:F1}°C");
        }

        ImGui.Separator();
        
        // Scale controls
        ImGui.Checkbox("Auto View Scale", ref _autoScale);
        if (!_autoScale)
        {
            ImGui.SliderFloat("View Radius (m)", ref _viewScale, 0.1f, 10.0f);
        }
        
        ImGui.Checkbox("Auto Color Scale", ref _autoColorScale);
        if (!_autoColorScale)
        {
            ImGui.DragFloatRange2("Temp Range (°C)", ref _minTempScale, ref _maxTempScale, 0.5f, 0f, 150f);
        }
        
        ImGui.Checkbox("Show Grid", ref _showGrid);
        ImGui.Checkbox("Show Legend", ref _showLegend);

        ImGui.Separator();
            
        if (ImGui.Button("Debug Data Structure", new Vector2(-1, 30)))
            DebugDataStructure();
    }
    
    /// <summary>
    /// Renders the entire plot view using ImGui draw commands.
    /// This replaces the OxyPlot implementation.
    /// </summary>
    public void RenderPlotInImGui()
    {
        if (_results == null || _mesh == null)
        {
            ImGui.Text("No data loaded");
            return;
        }
        
        // Use a switch to call the appropriate rendering function for the current view mode.
        // This keeps the code organized.
        switch (_currentView)
        {
            case ViewMode.CombinedTemperature:
                RenderCombinedTemperatureViewImGui();
                break;
            // TODO: Implement ImGui rendering for other view modes as needed.
            // For now, they will show a placeholder.
            default:
                var drawList = ImGui.GetWindowDrawList();
                var p0 = ImGui.GetCursorScreenPos();
                var size = ImGui.GetContentRegionAvail();
                drawList.AddText(p0 + new Vector2(20, 20), 0xFFFFFFFF, $"View Mode '{_currentView}' not yet implemented in ImGui renderer.");
                break;
        }
    }

    /// <summary>
    /// Renders the combined temperature heatmap view directly using ImGui.
    /// </summary>
    private void RenderCombinedTemperatureViewImGui()
    {
        var drawList = ImGui.GetWindowDrawList();
        var canvas_p0 = ImGui.GetCursorScreenPos();
        var canvas_sz = ImGui.GetContentRegionAvail();

        if (canvas_sz.X < 50 || canvas_sz.Y < 50) return;

        // --- Define Plot Area and Margins ---
        var margin = new Vector4(80, 40, 100, 60); // Left, Top, Right, Bottom
        var plot_p0 = new Vector2(canvas_p0.X + margin.X, canvas_p0.Y + margin.Y);
        var plot_sz = new Vector2(canvas_sz.X - margin.X - margin.Z, canvas_sz.Y - margin.Y - margin.W);
        var plot_p1 = new Vector2(plot_p0.X + plot_sz.X, plot_p0.Y + plot_sz.Y);
        
        // Background
        drawList.AddRectFilled(plot_p0, plot_p1, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1f)));

        // --- Data Calculation ---
        var zIndex = FindVerticalIndex(_selectedDepthMeters);
        var fluidTemps = GetFluidTemperaturesAtDepth(_selectedDepthMeters);
        float maxRadius = _autoScale ? 1.0f : _viewScale;

        var groundTempList = new List<double>();
        var pipeOuterRadius = _options.PipeOuterDiameter / 2.0;

        // Create a temperature data grid
        int gridSize = 150; 
        var data = new double[gridSize, gridSize];
        for (int ix = 0; ix < gridSize; ix++)
        {
            for (int iy = 0; iy < gridSize; iy++)
            {
                var x = -maxRadius + (2 * maxRadius * ix / (gridSize - 1));
                var y = -maxRadius + (2 * maxRadius * iy / (gridSize - 1));
                var r = Math.Sqrt(x * x + y * y);
                var theta = Math.Atan2(y, x);
                
                double temp = GetTemperatureAtPoint(r, theta, zIndex, fluidTemps);
                data[ix, iy] = temp - 273.15; // Convert to °C
                
                if (r > pipeOuterRadius * 1.2)
                {
                    groundTempList.Add(data[ix, iy]);
                }
            }
        }
        
        // --- Color Scale Calculation (The existing robust logic) ---
        double minTemp, maxTemp;
        if (_autoColorScale)
        {
            if (groundTempList.Any())
            {
                groundTempList.Sort();
                minTemp = groundTempList[(int)(groundTempList.Count * 0.02)];
                maxTemp = groundTempList[(int)(groundTempList.Count * 0.98)];
            }
            else { minTemp = 10; maxTemp = 20; }

            var fluidTempDownC = fluidTemps.down - 273.15;
            var fluidTempUpC = fluidTemps.up - 273.15;
            minTemp = Math.Min(minTemp, Math.Min(fluidTempDownC, fluidTempUpC));
            maxTemp = Math.Max(maxTemp, Math.Max(fluidTempDownC, fluidTempUpC));

            var range = maxTemp - minTemp;
            if (range < 5) range = 5;
            minTemp -= range * 0.1;
            maxTemp += range * 0.1;
        }
        else
        {
            minTemp = _minTempScale;
            maxTemp = _maxTempScale;
        }

        // --- Render Heatmap ---
        var cell_sz = new Vector2(plot_sz.X / gridSize, plot_sz.Y / gridSize);
        for (int ix = 0; ix < gridSize; ix++)
        {
            for (int iy = 0; iy < gridSize; iy++)
            {
                var temp = data[ix, iy];
                if (double.IsNaN(temp)) continue;

                var t = (temp - minTemp) / (maxTemp - minTemp);
                var color = GetJetColor((float)t);
                
                var cell_p0 = new Vector2(plot_p0.X + ix * cell_sz.X, plot_p0.Y + iy * cell_sz.Y);
                var cell_p1 = new Vector2(cell_p0.X + cell_sz.X, cell_p0.Y + cell_sz.Y);

                drawList.AddRectFilled(cell_p0, cell_p1, ImGui.ColorConvertFloat4ToU32(color));
            }
        }
        
        // --- World to Screen Transformation ---
        Func<Vector2, Vector2> WorldToScreen = (worldPos) =>
        {
            float screenX = plot_p0.X + (worldPos.X - (-maxRadius)) / (2 * maxRadius) * plot_sz.X;
            float screenY = plot_p1.Y - (worldPos.Y - (-maxRadius)) / (2 * maxRadius) * plot_sz.Y; // Y is inverted
            return new Vector2(screenX, screenY);
        };
        Func<float, float> ScaleRadius = (r) => (r / maxRadius) * (plot_sz.X / 2f);

        // --- Render Geometry Overlay ---
        var centerScreen = WorldToScreen(Vector2.Zero);
        // Borehole Wall
        drawList.AddCircle(centerScreen, ScaleRadius((float)_options.BoreholeDataset.WellDiameter / 2), 0xFF000000, 0, 2f);
        // Outer Pipe
        drawList.AddCircle(centerScreen, ScaleRadius((float)_options.PipeOuterDiameter / 2), 0xFF808080, 0, 1.5f);
        // Inner pipe (for coaxial)
        if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
        {
            drawList.AddCircle(centerScreen, ScaleRadius((float)_options.PipeSpacing / 2), 0xFF505050, 0, 1.5f);
        }
        
        // --- Render Text and Annotations ---
        var title = $"Cross-Section at {_selectedDepthMeters:F1} m depth";
        var titleSize = ImGui.CalcTextSize(title);
        drawList.AddText(new Vector2(plot_p0.X + plot_sz.X / 2 - titleSize.X / 2, plot_p0.Y - 30), 0xFFFFFFFF, title);
        
        // Flow indicators
        if (_options.FlowConfiguration == FlowConfiguration.CounterFlow)
        {
            drawList.AddText(centerScreen - new Vector2(10, 8), 0xFFFFFFFF, $"↓ {fluidTemps.down - 273.15:F0}°C");
            var annulusPos = WorldToScreen(new Vector2((float)(_options.PipeSpacing / 2.0 + _options.PipeOuterDiameter / 2.0) / 2.0f, 0));
            drawList.AddText(annulusPos - new Vector2(10, 8), 0xFFFFFFFF, $"↑ {fluidTemps.up - 273.15:F0}°C");
        }
        else if (_options.FlowConfiguration == FlowConfiguration.CounterFlowReversed)
        {
            drawList.AddText(centerScreen - new Vector2(10, 8), 0xFFFFFFFF, $"↑ {fluidTemps.up - 273.15:F0}°C");
            var annulusPos = WorldToScreen(new Vector2((float)(_options.PipeSpacing / 2.0 + _options.PipeOuterDiameter / 2.0) / 2.0f, 0));
            drawList.AddText(annulusPos - new Vector2(10, 8), 0xFFFFFFFF, $"↓ {fluidTemps.down - 273.15:F0}°C");
        }
        
        // --- Draw Axes and Color Bar ---
        DrawAxes(drawList, plot_p0, plot_sz, maxRadius);
        DrawColorBar(drawList, new Vector2(plot_p1.X + 20, plot_p0.Y), new Vector2(20, plot_sz.Y), minTemp, maxTemp);
    }
    
    private void DrawAxes(ImDrawListPtr drawList, Vector2 p0, Vector2 sz, float maxRadius)
    {
        var p1 = p0 + sz;
        uint axisColor = 0xFFAAAAAA;
        uint gridColor = 0x40FFFFFF;

        // X-Axis
        drawList.AddLine(new Vector2(p0.X, p1.Y), new Vector2(p1.X, p1.Y), axisColor, 1f);
        for (int i = 0; i <= 4; i++)
        {
            float val = -maxRadius + (i / 4f) * (2 * maxRadius);
            float xPos = p0.X + (i / 4f) * sz.X;
            if (_showGrid) drawList.AddLine(new Vector2(xPos, p0.Y), new Vector2(xPos, p1.Y), gridColor);
            drawList.AddLine(new Vector2(xPos, p1.Y), new Vector2(xPos, p1.Y + 5), axisColor);
            var label = $"{val:F1}";
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(xPos - labelSize.X / 2, p1.Y + 8), axisColor, label);
        }
        var xLabel = "X (m)";
        var xLabelSize = ImGui.CalcTextSize(xLabel);
        drawList.AddText(new Vector2(p0.X + sz.X/2 - xLabelSize.X/2, p1.Y + 25), axisColor, xLabel);
        
        // Y-Axis
        drawList.AddLine(new Vector2(p0.X, p0.Y), new Vector2(p0.X, p1.Y), axisColor, 1f);
        for (int i = 0; i <= 4; i++)
        {
            float val = -maxRadius + (i / 4f) * (2 * maxRadius);
            float yPos = p1.Y - (i / 4f) * sz.Y;
             if (_showGrid) drawList.AddLine(new Vector2(p0.X, yPos), new Vector2(p1.X, yPos), gridColor);
            drawList.AddLine(new Vector2(p0.X - 5, yPos), new Vector2(p0.X, yPos), axisColor);
            var label = $"{val:F1}";
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(p0.X - labelSize.X - 8, yPos - labelSize.Y/2), axisColor, label);
        }
        var yLabel = "Y (m)";
        var yLabelSize = ImGui.CalcTextSize(yLabel);
        AddTextVertical(drawList, new Vector2(p0.X - 50, p0.Y + sz.Y/2), axisColor, yLabel);
    }
    
    private void DrawColorBar(ImDrawListPtr drawList, Vector2 p0, Vector2 sz, double min, double max)
    {
        var p1 = p0 + sz;
        int nSteps = 100;
        float stepHeight = sz.Y / nSteps;
        for (int i = 0; i < nSteps; i++)
        {
            float t = (float)i / (nSteps - 1);
            var color = GetJetColor(1f - t); // Invert t because we draw from top to bottom
            var step_p0 = new Vector2(p0.X, p0.Y + i * stepHeight);
            var step_p1 = new Vector2(p1.X, p0.Y + (i + 1) * stepHeight);
            drawList.AddRectFilled(step_p0, step_p1, ImGui.ColorConvertFloat4ToU32(color));
        }
        drawList.AddRect(p0, p1, 0xFFFFFFFF);
        
        // Labels
        uint textColor = 0xFFAAAAAA;
        drawList.AddText(new Vector2(p1.X + 5, p0.Y - 7), textColor, $"{max:F1}");
        drawList.AddText(new Vector2(p1.X + 5, p1.Y - 7), textColor, $"{min:F1}");
        var title = "Temp (°C)";
        AddTextVertical(drawList, new Vector2(p0.X + sz.X + 15, p0.Y + sz.Y / 2), textColor, title);
    }
    
    private Vector4 GetJetColor(float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        float r=0, g=0, b=0;
        if (t < 0.34f) { r = 0; g = 0; b = 0.5f + t / 0.34f * 0.5f; }
        else if (t < 0.61f) { r = 0; g = (t - 0.34f) / 0.27f; b = 1; }
        else if (t < 0.84f) { r = (t - 0.61f) / 0.23f; g = 1; b = 1.0f - r; }
        else { r = 1; g = 1.0f - (t - 0.84f) / 0.16f; b = 0; }
        return new Vector4(r, g, b, 1.0f);
    }

    private void AddTextVertical(ImDrawListPtr drawList, Vector2 pos, uint color, string text)
    {
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var totalTextHeight = text.Length * (fontSize * 0.7f); 
        var startY = pos.Y - (totalTextHeight / 2);
        for (int i = 0; i < text.Length; i++)
        {
            string c = text[i].ToString();
            var charSize = font.CalcTextSizeA(fontSize, float.MaxValue, -1.0f, c);
            var currentPos = new Vector2(pos.X - charSize.X / 2, startY + i * (fontSize * 0.85f));
            drawList.AddText(currentPos, color, c);
        }
    }

    /// <summary>
    /// *** DEFINITIVE FIX ***
    /// Get temperature at a specific point, correctly considering heat exchanger fluid regions.
    /// This now accurately shows the counter-flow pattern in coaxial systems.
    /// </summary>
    private double GetTemperatureAtPoint(double r, double theta, int zIndex, (float down, float up) fluidTemps)
    {
        var pipeInnerRadius = _options.PipeInnerDiameter / 2.0;
        var pipeOuterRadius = _options.PipeOuterDiameter / 2.0;
        // In coaxial systems, PipeSpacing is defined as the OUTER diameter of the inner pipe.
        var innerPipeOuterRadius = _options.PipeSpacing / 2.0;

        if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
        {
            // --- REGION 1: Inside the inner pipe ---
            if (r <= innerPipeOuterRadius) 
            {
                if (_options.FlowConfiguration == FlowConfiguration.CounterFlow)
                {
                    // Standard: Cold fluid flows DOWN the center.
                    return fluidTemps.down;
                }
                else // CounterFlowReversed or ParallelFlow
                {
                    // Reversed: Hot fluid flows UP the center.
                    return fluidTemps.up;
                }
            }
            // --- REGION 2: In the annulus between the inner and outer pipes ---
            else if (r > innerPipeOuterRadius && r <= pipeOuterRadius)
            {
                if (_options.FlowConfiguration == FlowConfiguration.CounterFlow)
                {
                    // Standard: Hot fluid flows UP the annulus.
                    return fluidTemps.up;
                }
                else // CounterFlowReversed or ParallelFlow
                {
                    // Reversed: Cold fluid flows DOWN the annulus.
                    return fluidTemps.down;
                }
            }
        }
        else if (_options.HeatExchangerType == HeatExchangerType.UTube)
        {
            // For a U-Tube, we can't resolve the two pipes in a simple radial view.
            // We show the average fluid temperature inside the borehole region.
            if (r <= pipeOuterRadius)
            {
                return (fluidTemps.down + fluidTemps.up) / 2.0;
            }
        }

        // --- REGION 3: Outside the borehole (in the ground) ---
        // Create a smooth thermal transition from the outer pipe to the ground.
        if (r > pipeOuterRadius && r <= pipeOuterRadius * 1.2)
        {
            var groundTemp = InterpolateGroundTemperature(r, theta, zIndex);
            
            // Determine the temperature of the fluid in contact with the outer pipe
            double outerFluidTemp;
            if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
            {
                outerFluidTemp = (_options.FlowConfiguration == FlowConfiguration.CounterFlow) ? fluidTemps.up : fluidTemps.down;
            }
            else // U-Tube
            {
                outerFluidTemp = (fluidTemps.down + fluidTemps.up) / 2.0;
            }

            // Interpolate between the outer fluid and the ground
            var t = (r - pipeOuterRadius) / (pipeOuterRadius * 0.2);
            return outerFluidTemp * (1 - t) + groundTemp * t;
        }
        
        // Default: If we are clearly in the ground, interpolate from the mesh
        return InterpolateGroundTemperature(r, theta, zIndex);
    }
    
    /// <summary>
    /// Interpolate ground temperature from mesh data
    /// </summary>
    private double InterpolateGroundTemperature(double r, double theta, int zIndex)
    {
        // Find radial indices for interpolation
        int r1 = -1, r2 = -1;
        for (int i = 0; i < _mesh.RadialPoints - 1; i++)
        {
            if (r >= _mesh.R[i] && r <= _mesh.R[i + 1])
            {
                r1 = i;
                r2 = i + 1;
                break;
            }
        }
        
        // Handle out of bounds
        if (r1 == -1)
        {
            if (r < _mesh.R[0])
            {
                r1 = r2 = 0;
            }
            else
            {
                r1 = r2 = _mesh.RadialPoints - 1;
            }
        }
        
        // DEFINITIVE FIX: Normalize theta to prevent negative indices
        while (theta < 0) theta += 2 * Math.PI;
        while (theta >= 2 * Math.PI) theta -= 2 * Math.PI;
        
        var thetaIndex = (theta / (2 * Math.PI)) * _mesh.AngularPoints;
        int th1 = (int)thetaIndex % _mesh.AngularPoints;
        int th2 = (th1 + 1) % _mesh.AngularPoints;
        var thetaFrac = thetaIndex - th1;
        
        // Bilinear interpolation
        if (r1 == r2)
        {
            // Angular interpolation only
            var t1 = _results.FinalTemperatureField[r1, th1, zIndex];
            var t2 = _results.FinalTemperatureField[r1, th2, zIndex];
            return t1 + (t2 - t1) * thetaFrac;
        }
        
        if (_mesh.R[r2] - _mesh.R[r1] < 1e-9) // Avoid division by zero
        {
             var t1 = _results.FinalTemperatureField[r1, th1, zIndex];
             var t2 = _results.FinalTemperatureField[r1, th2, zIndex];
             return t1 + (t2 - t1) * thetaFrac;
        }

        // Full bilinear interpolation
        var t11 = _results.FinalTemperatureField[r1, th1, zIndex];
        var t12 = _results.FinalTemperatureField[r1, th2, zIndex];
        var t21 = _results.FinalTemperatureField[r2, th1, zIndex];
        var t22 = _results.FinalTemperatureField[r2, th2, zIndex];
        
        var rFrac = (r - _mesh.R[r1]) / (_mesh.R[r2] - _mesh.R[r1]);
        
        var t_th1 = t11 + (t21 - t11) * rFrac;
        var t_th2 = t12 + (t22 - t12) * rFrac;
        
        return t_th1 + (t_th2 - t_th1) * thetaFrac;
    }

    /// <summary>
    /// Get fluid temperatures at specific depth
    /// </summary>
    private (float down, float up) GetFluidTemperaturesAtDepth(float depth)
    {
        if (_results.FluidTemperatureProfile == null || !_results.FluidTemperatureProfile.Any())
            return ((float)_options.FluidInletTemperature, (float)_options.FluidInletTemperature);
        
        var sortedProfile = _results.FluidTemperatureProfile.OrderBy(p => p.depth).ToList();
        
        // Before first point
        if (depth < sortedProfile.First().depth)
        {
             return ((float)sortedProfile.First().temperatureDown, (float)sortedProfile.First().temperatureUp);
        }

        // After last point
        if (depth > sortedProfile.Last().depth)
        {
            return ((float)sortedProfile.Last().temperatureDown, (float)sortedProfile.Last().temperatureUp);
        }

        // Linear interpolation between points
        for (int i = 0; i < sortedProfile.Count - 1; i++)
        {
            var p1 = sortedProfile[i];
            var p2 = sortedProfile[i+1];
            if (depth >= p1.depth && depth <= p2.depth)
            {
                var t = (depth - p1.depth) / (p2.depth - p1.depth);
                if (double.IsNaN(t) || double.IsInfinity(t)) t = 0;
                
                var down = p1.temperatureDown + t * (p2.temperatureDown - p1.temperatureDown);
                var up = p1.temperatureUp + t * (p2.temperatureUp - p1.temperatureUp);
                return ((float)down, (float)up);
            }
        }
        
        // Fallback to nearest if something goes wrong
        var nearest = _results.FluidTemperatureProfile.OrderBy(p => Math.Abs(p.depth - depth)).First();
        return ((float)nearest.temperatureDown, (float)nearest.temperatureUp);
    }

    private int FindVerticalIndex(float depthMeters)
    {
        if (_mesh?.Z == null || _mesh.VerticalPoints == 0) return 0;
        
        // Find the index where the mesh Z coordinate is closest to the negative depth
        var targetZ = -depthMeters;
        int closestIndex = 0;
        float minDiff = float.MaxValue;

        for (var k = 0; k < _mesh.VerticalPoints; k++)
        {
            float diff = Math.Abs(_mesh.Z[k] - targetZ);
            if (diff < minDiff)
            {
                minDiff = diff;
                closestIndex = k;
            }
        }
        return closestIndex;
    }

    private void DebugDataStructure()
    {
        if (_results?.FinalTemperatureField == null || _mesh == null)
        {
            Logger.LogError("No data loaded");
            return;
        }
        
        var zIndex = FindVerticalIndex(_selectedDepthMeters);
        var fluidTemps = GetFluidTemperaturesAtDepth(_selectedDepthMeters);
        
        Logger.Log("=== CROSS-SECTION DEBUG ===");
        Logger.Log($"Depth: {_selectedDepthMeters:F1}m, z-index: {zIndex}");
        Logger.Log($"Flow Configuration: {_options.FlowConfiguration}");
        Logger.Log($"Heat Exchanger Type: {_options.HeatExchangerType}");
        Logger.Log($"Fluid Temps - Down: {fluidTemps.down - 273.15:F1}°C, Up: {fluidTemps.up - 273.15:F1}°C");
        
        // Check center temperature
        var centerTemp = GetTemperatureAtPoint(0, 0, zIndex, fluidTemps) - 273.15;
        Logger.Log($"Center Temperature (GetTemperatureAtPoint): {centerTemp:F1}°C");
        
        // Radial temperature profile
        Logger.Log("\nRadial Temperature Profile (theta=0):");
        for (int i = 0; i < Math.Min(10, _mesh.RadialPoints); i++)
        {
            var r = _mesh.R[i];
            var temp = GetTemperatureAtPoint(r, 0, zIndex, fluidTemps) - 273.15;
            var label = "";
            if (r <= _options.PipeSpacing / 2.0) label = " [Inner Pipe]";
            else if (r <= _options.PipeOuterDiameter / 2) label = " [Annulus]";
            else label = " [Ground]";
            
            Logger.Log($"  r={r:F4}m: {temp:F1}°C{label}");
        }
        
        Logger.Log("\n=== END DEBUG ===");
    }
}