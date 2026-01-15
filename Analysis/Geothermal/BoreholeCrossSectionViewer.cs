// GeoscientistToolkit/UI/Visualization/BoreholeCrossSectionViewer.cs

using System.Numerics;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Visualization;

/// <summary>
///     Fixed 2D visualization of borehole cross-section and lateral-section with correct fluid temperature display using a
///     custom ImGui renderer.
/// </summary>
public class BoreholeCrossSectionViewer
{
    public enum ViewMode
    {
        CombinedTemperature,
        GroundTemperature,
        FluidCirculation,
        Velocity,
        FluidTemperatureProfile,
        Debug
    }

    public enum ViewPlane
    {
        CrossSection_XY,
        LateralView_XZ // MODIFIED: Changed from YZ to XZ for clarity with U-Tube
    }

    private bool _autoColorScale = true;
    private bool _autoScale = true;
    private ViewPlane _currentPlane = ViewPlane.CrossSection_XY;

    private ViewMode _currentView = ViewMode.CombinedTemperature;
    private float _depthPosition = 0.5f;

    // NEW: State for lateral view interactivity
    private Vector2 _lateralViewOffset = Vector2.Zero;
    private float _lateralViewZoom = 1.0f;
    private float _maxTempScale = 90f;
    private GeothermalMesh _mesh;
    private float _minTempScale = 10f;
    private GeothermalSimulationOptions _options;
    private GeothermalSimulationResults _results;
    private float _selectedDepthMeters;
    private bool _showGrid = true;
    private bool _showLegend = true;
    private float _viewScale = 1.0f;

    // BTES Animation Support
    private bool _btesAnimationEnabled;
    private int _selectedTimeFrameIndex;
    private List<double> _availableTimeFrames = new();
    private bool _isPlaying;
    private double _lastPlayTime;
    private float _playbackSpeed = 1.0f; // days per second


    public void LoadResults(GeothermalSimulationResults results, GeothermalMesh mesh,
        GeothermalSimulationOptions options)
    {
        _results = results;
        _mesh = mesh;
        _options = options;
        _selectedDepthMeters = (float)(_options.BoreholeDataset.TotalDepth * 0.5);

        // Initialize BTES animation if multiple timeframes are available
        if (_results.TemperatureFields.Count > 1)
        {
            _btesAnimationEnabled = true;
            _availableTimeFrames = _results.TemperatureFields.Keys.OrderBy(t => t).ToList();
            _selectedTimeFrameIndex = _availableTimeFrames.Count - 1; // Start at final frame
            Logger.Log($"BTES Animation enabled: {_availableTimeFrames.Count} timeframes available");
        }
        else
        {
            _btesAnimationEnabled = false;
            _availableTimeFrames.Clear();
        }
    }

    public void RenderControls()
    {
        if (_results == null || _mesh == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Load simulation results to view borehole cross-section");
            return;
        }

        ImGui.Text("Borehole Section Viewer");
        ImGui.Separator();

        // View Plane selector
        if (ImGui.BeginCombo("View Plane", _currentPlane.ToString().Replace("_", " ")))
        {
            foreach (var plane in Enum.GetValues<ViewPlane>())
                if (ImGui.Selectable(plane.ToString().Replace("_", " "), _currentPlane == plane))
                    _currentPlane = plane;
            ImGui.EndCombo();
        }

        // View mode selector
        if (ImGui.BeginCombo("View Mode", _currentView.ToString()))
        {
            foreach (var mode in Enum.GetValues<ViewMode>())
                if (ImGui.Selectable(mode.ToString(), _currentView == mode))
                    _currentView = mode;
            ImGui.EndCombo();
        }

        // Depth slider is only relevant for XY cross-section
        if (_currentPlane == ViewPlane.CrossSection_XY && _currentView != ViewMode.FluidTemperatureProfile)
        {
            var depthMeters = _selectedDepthMeters;
            if (ImGui.SliderFloat("Depth (m)", ref depthMeters, 0f, _options.BoreholeDataset.TotalDepth))
            {
                _selectedDepthMeters = depthMeters;
                _depthPosition = depthMeters / _options.BoreholeDataset.TotalDepth;
            }

            ImGui.Text($"At depth: {_selectedDepthMeters:F1} m ({_depthPosition * 100:F0}%)");
        }

        // Lateral view controls
        if (_currentPlane == ViewPlane.LateralView_XZ)
        {
            if (ImGui.Button("Reset View"))
            {
                _lateralViewOffset = Vector2.Zero;
                _lateralViewZoom = 1.0f;
            }

            ImGui.Text("Right-drag to pan, scroll to zoom.");
        }


        // Get fluid temperatures at this depth
        if (_results.FluidTemperatureProfile?.Any() == true && _currentPlane == ViewPlane.CrossSection_XY)
        {
            var fluidPoint = GetFluidTemperaturesAtDepth(_selectedDepthMeters);
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 1f, 1), $"Downflow fluid: {fluidPoint.down - 273.15:F1}°C");
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1), $"Upflow fluid: {fluidPoint.up - 273.15:F1}°C");
        }

        ImGui.Separator();

        // Scale controls
        ImGui.Checkbox("Auto View Scale", ref _autoScale);
        if (!_autoScale) ImGui.SliderFloat("View Radius (m)", ref _viewScale, 0.1f, 10.0f);

        if (_currentView == ViewMode.CombinedTemperature || _currentView == ViewMode.GroundTemperature ||
            _currentView == ViewMode.Velocity)
        {
            ImGui.Checkbox("Auto Color Scale", ref _autoColorScale);
            if (!_autoColorScale)
                ImGui.DragFloatRange2("Temp Range (°C)", ref _minTempScale, ref _maxTempScale, 0.5f, 0f, 150f);
        }

        ImGui.Checkbox("Show Grid", ref _showGrid);
        ImGui.Checkbox("Show Legend", ref _showLegend);

        // BTES Animation Controls
        if (_btesAnimationEnabled)
        {
            ImGui.Separator();
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.8f, 0.3f, 0.2f, 1.0f));
            if (ImGui.CollapsingHeader("BTES Animation", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var currentTime = _availableTimeFrames[_selectedTimeFrameIndex];
                var days = currentTime / 86400.0;

                ImGui.Text($"Time: {days:F2} days ({days/365.0:F2} years)");
                ImGui.Text($"Frame: {_selectedTimeFrameIndex + 1} / {_availableTimeFrames.Count}");

                var frameIndex = _selectedTimeFrameIndex;
                if (ImGui.SliderInt("##TimeFrame", ref frameIndex, 0, _availableTimeFrames.Count - 1))
                {
                    _selectedTimeFrameIndex = frameIndex;
                }

                // Playback controls
                ImGui.Spacing();
                if (_isPlaying)
                {
                    if (ImGui.Button("Pause", new Vector2(-1, 30)))
                    {
                        _isPlaying = false;
                    }

                    // Update animation
                    var currentRealTime = DateTime.Now.TimeOfDay.TotalSeconds;
                    if (_lastPlayTime > 0)
                    {
                        var deltaRealTime = currentRealTime - _lastPlayTime;
                        var deltaSimDays = deltaRealTime * _playbackSpeed;
                        var deltaSimSeconds = deltaSimDays * 86400.0;

                        // Find next frame
                        var targetTime = currentTime + deltaSimSeconds;
                        for (int i = _selectedTimeFrameIndex + 1; i < _availableTimeFrames.Count; i++)
                        {
                            if (_availableTimeFrames[i] >= targetTime)
                            {
                                _selectedTimeFrameIndex = i;
                                break;
                            }
                        }

                        // Loop to beginning if reached end
                        if (_selectedTimeFrameIndex >= _availableTimeFrames.Count - 1)
                        {
                            _selectedTimeFrameIndex = 0;
                        }
                    }
                    _lastPlayTime = currentRealTime;
                }
                else
                {
                    if (ImGui.Button("▶ Play", new Vector2(-1, 30)))
                    {
                        _isPlaying = true;
                        _lastPlayTime = DateTime.Now.TimeOfDay.TotalSeconds;
                    }
                }

                ImGui.SliderFloat("Speed (days/sec)", ref _playbackSpeed, 0.1f, 100.0f, "%.1f", ImGuiSliderFlags.Logarithmic);

                if (ImGui.Button("⏮ First Frame", new Vector2(-1, 0)))
                {
                    _selectedTimeFrameIndex = 0;
                    _isPlaying = false;
                }

                ImGui.SameLine();

                if (ImGui.Button("⏭ Last Frame", new Vector2(-1, 0)))
                {
                    _selectedTimeFrameIndex = _availableTimeFrames.Count - 1;
                    _isPlaying = false;
                }
            }
            ImGui.PopStyleColor();
        }

        ImGui.Separator();

        if (ImGui.Button("Debug Data Structure", new Vector2(-1, 30)))
            DebugDataStructure();
    }

    /// <summary>
    ///     Renders the entire plot view using ImGui draw commands.
    ///     This is the main entry point for rendering any view.
    /// </summary>
    public void RenderPlotInImGui()
    {
        if (_results == null || _mesh == null)
        {
            ImGui.Text("No data loaded");
            return;
        }

        // Top-level switch for the view plane (XY vs XZ)
        switch (_currentPlane)
        {
            case ViewPlane.CrossSection_XY:
                RenderCrossSectionPlane();
                break;
            case ViewPlane.LateralView_XZ:
                RenderLateralPlane();
                break;
        }
    }

    /// <summary>
    ///     Handles rendering for all modes in the XY Cross-Section plane.
    /// </summary>
    private void RenderCrossSectionPlane()
    {
        switch (_currentView)
        {
            case ViewMode.CombinedTemperature:
                RenderCombinedTemperatureViewImGui();
                break;
            case ViewMode.GroundTemperature:
                RenderGroundTemperatureOnlyImGui();
                break;
            case ViewMode.FluidCirculation:
                RenderFluidCirculationViewImGui();
                break;
            case ViewMode.Velocity:
                RenderVelocityViewImGui();
                break;
            case ViewMode.FluidTemperatureProfile:
                RenderFluidTemperatureProfileImGui();
                break;
            case ViewMode.Debug:
                RenderDebugViewImGui();
                break;
            default:
                var drawList = ImGui.GetWindowDrawList();
                var p0 = ImGui.GetCursorScreenPos();
                drawList.AddText(p0 + new Vector2(20, 20), 0xFFFFFFFF,
                    $"View Mode '{_currentView}' not yet implemented.");
                break;
        }
    }

    /// <summary>
    ///     Renders the combined temperature heatmap view directly using ImGui.
    /// </summary>
    private void RenderCombinedTemperatureViewImGui()
    {
        var drawList = ImGui.GetWindowDrawList();
        var canvas_p0 = ImGui.GetCursorScreenPos();
        var canvas_sz = ImGui.GetContentRegionAvail();

        if (canvas_sz.X < 50 || canvas_sz.Y < 50) return;

        var margin = new Vector4(80, 40, 100, 60);
        var plot_p0 = new Vector2(canvas_p0.X + margin.X, canvas_p0.Y + margin.Y);
        var plot_sz = new Vector2(canvas_sz.X - margin.X - margin.Z, canvas_sz.Y - margin.Y - margin.W);
        var plot_p1 = new Vector2(plot_p0.X + plot_sz.X, plot_p0.Y + plot_sz.Y);

        drawList.AddRectFilled(plot_p0, plot_p1, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1f)));

        var zIndex = FindVerticalIndex(_selectedDepthMeters);
        var fluidTemps = GetFluidTemperaturesAtDepth(_selectedDepthMeters);
        var maxRadius = _autoScale ? 1.0f : _viewScale;

        var groundTempList = new List<double>();
        var pipeOuterRadius = _options.PipeOuterDiameter / 2.0;

        var gridSize = 150;
        var data = new double[gridSize, gridSize];
        for (var ix = 0; ix < gridSize; ix++)
        for (var iy = 0; iy < gridSize; iy++)
        {
            var x = -maxRadius + 2 * maxRadius * ix / (gridSize - 1);
            var y = -maxRadius + 2 * maxRadius * iy / (gridSize - 1);

            var temp = GetTemperatureAtPoint(x, y, zIndex, fluidTemps);
            data[ix, iy] = temp - 273.15;

            var r = Math.Sqrt(x * x + y * y);
            if (r > pipeOuterRadius * 1.2) groundTempList.Add(data[ix, iy]);
        }

        double minTemp, maxTemp;
        if (_autoColorScale)
        {
            if (groundTempList.Any())
            {
                groundTempList.Sort();
                minTemp = groundTempList[(int)(groundTempList.Count * 0.02)];
                maxTemp = groundTempList[(int)(groundTempList.Count * 0.98)];
            }
            else
            {
                minTemp = 10;
                maxTemp = 20;
            }

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

        var cell_sz = new Vector2(plot_sz.X / gridSize, plot_sz.Y / gridSize);
        for (var ix = 0; ix < gridSize; ix++)
        for (var iy = 0; iy < gridSize; iy++)
        {
            var temp = data[ix, iy];
            if (double.IsNaN(temp)) continue;

            var t = (temp - minTemp) / (maxTemp - minTemp);
            var color = GetJetColor((float)t);

            var cell_p0 =
                new Vector2(plot_p0.X + ix * cell_sz.X,
                    plot_p0.Y + (gridSize - 1 - iy) * cell_sz.Y); // Y is inverted for drawing
            var cell_p1 = new Vector2(cell_p0.X + cell_sz.X, cell_p0.Y + cell_sz.Y);

            drawList.AddRectFilled(cell_p0, cell_p1, ImGui.ColorConvertFloat4ToU32(color));
        }

        Func<Vector2, Vector2> WorldToScreen = worldPos =>
        {
            var screenX = plot_p0.X + (worldPos.X - -maxRadius) / (2 * maxRadius) * plot_sz.X;
            var screenY = plot_p1.Y - (worldPos.Y - -maxRadius) / (2 * maxRadius) * plot_sz.Y;
            return new Vector2(screenX, screenY);
        };
        Func<float, float> ScaleRadius = r => r / maxRadius * (plot_sz.X / 2f);

        // Draw Borehole and Pipe outlines
        var centerScreen = WorldToScreen(Vector2.Zero);
        drawList.AddCircle(centerScreen, ScaleRadius(_options.BoreholeDataset.WellDiameter / 2), 0xFF000000, 0, 2f);

        var title = $"Combined Temperature at {_selectedDepthMeters:F1} m depth";
        var titleSize = ImGui.CalcTextSize(title);
        drawList.AddText(new Vector2(plot_p0.X + plot_sz.X / 2 - titleSize.X / 2, plot_p0.Y - 30), 0xFFFFFFFF, title);

        if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
        {
            drawList.AddCircle(centerScreen, ScaleRadius((float)_options.PipeOuterDiameter / 2), 0xFF808080, 0, 1.5f);
            drawList.AddCircle(centerScreen, ScaleRadius((float)_options.PipeSpacing / 2), 0xFF505050, 0, 1.5f);

            if (_options.FlowConfiguration == FlowConfiguration.CounterFlow)
            {
                drawList.AddText(centerScreen - new Vector2(10, 8), 0xFFFFFFFF, $"↓ {fluidTemps.down - 273.15:F0}°C");
                var annulusPos =
                    WorldToScreen(
                        new Vector2((float)(_options.PipeSpacing / 2.0 + _options.PipeOuterDiameter / 2.0) / 2.0f, 0));
                drawList.AddText(annulusPos - new Vector2(10, 8), 0xFFFFFFFF, $"↑ {fluidTemps.up - 273.15:F0}°C");
            }
            else if (_options.FlowConfiguration == FlowConfiguration.CounterFlowReversed)
            {
                drawList.AddText(centerScreen - new Vector2(10, 8), 0xFFFFFFFF, $"↑ {fluidTemps.up - 273.15:F0}°C");
                var annulusPos =
                    WorldToScreen(
                        new Vector2((float)(_options.PipeSpacing / 2.0 + _options.PipeOuterDiameter / 2.0) / 2.0f, 0));
                drawList.AddText(annulusPos - new Vector2(10, 8), 0xFFFFFFFF, $"↓ {fluidTemps.down - 273.15:F0}°C");
            }
        }
        else // U-Tube
        {
            var pipeRadius = (float)_options.PipeOuterDiameter / 2;
            var pipeSpacing = (float)_options.PipeSpacing / 2;
            var p1_world = new Vector2(-pipeSpacing, 0);
            var p2_world = new Vector2(pipeSpacing, 0);

            var p1_screen = WorldToScreen(p1_world);
            var p2_screen = WorldToScreen(p2_world);

            drawList.AddCircle(p1_screen, ScaleRadius(pipeRadius), 0xFF808080, 0, 1.5f);
            drawList.AddCircle(p2_screen, ScaleRadius(pipeRadius), 0xFF808080, 0, 1.5f);

            drawList.AddText(p1_screen - new Vector2(10, 8), 0xFFFFFFFF, $"↓ {fluidTemps.down - 273.15:F0}°C");
            drawList.AddText(p2_screen - new Vector2(10, 8), 0xFFFFFFFF, $"↑ {fluidTemps.up - 273.15:F0}°C");
        }

        DrawAxes(drawList, plot_p0, plot_sz, maxRadius, maxRadius);
        if (_showLegend)
            DrawColorBar(drawList, new Vector2(plot_p1.X + 20, plot_p0.Y), new Vector2(20, plot_sz.Y), minTemp, maxTemp,
                "Temp (°C)");
    }

    /// <summary>
    ///     Handles rendering for all modes in the XZ Lateral View plane.
    /// </summary>
    private void RenderLateralPlane()
    {
        // For the lateral view, only certain modes make sense.
        switch (_currentView)
        {
            case ViewMode.CombinedTemperature:
            case ViewMode.GroundTemperature:
                RenderLateralTemperatureViewImGui();
                break;
            case ViewMode.FluidTemperatureProfile:
                RenderFluidTemperatureProfileImGui(); // This is a 1D plot, independent of plane
                break;
            default:
                var drawList = ImGui.GetWindowDrawList();
                var p0 = ImGui.GetCursorScreenPos();
                drawList.AddText(p0 + new Vector2(20, 20), 0xFFFFFFFF,
                    $"View Mode '{_currentView}' is not applicable in Lateral View.");
                break;
        }
    }

    /// <summary>
    ///     Renders a XZ slice showing temperature along the borehole depth, with zoom and pan.
    /// </summary>
    private void RenderLateralTemperatureViewImGui()
    {
        var drawList = ImGui.GetWindowDrawList();
        var canvas_p0 = ImGui.GetCursorScreenPos();
        var canvas_sz = ImGui.GetContentRegionAvail();
        if (canvas_sz.X < 50 || canvas_sz.Y < 50) return;

        var io = ImGui.GetIO();

        var margin = new Vector4(80, 40, 100, 60);
        var plot_p0 = new Vector2(canvas_p0.X + margin.X, canvas_p0.Y + margin.Y);
        var plot_sz = new Vector2(canvas_sz.X - margin.X - margin.Z, canvas_sz.Y - margin.Y - margin.W);
        var plot_p1 = new Vector2(plot_p0.X + plot_sz.X, plot_p0.Y + plot_sz.Y);

        drawList.AddRectFilled(plot_p0, plot_p1, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1f)));
        ImGui.InvisibleButton("##plot_canvas", plot_sz); // For mouse interaction

        // --- Interactivity (Zoom and Pan) ---
        if (ImGui.IsItemHovered())
        {
            // Zooming
            if (io.MouseWheel != 0)
            {
                var mousePosAbs = ImGui.GetMousePos();
                var mousePosRel = mousePosAbs - plot_p0;
                var mouseWorldBeforeZoom = new Vector2(
                    (mousePosRel.X / plot_sz.X * 2 - 1) * (_autoScale ? 5.0f : _viewScale) / _lateralViewZoom +
                    _lateralViewOffset.X,
                    mousePosRel.Y / plot_sz.Y * _options.BoreholeDataset.TotalDepth / _lateralViewZoom +
                    _lateralViewOffset.Y
                );

                var zoomFactor = MathF.Pow(1.1f, io.MouseWheel);
                _lateralViewZoom *= zoomFactor;
                _lateralViewZoom = Math.Clamp(_lateralViewZoom, 0.01f, 100.0f);

                var mouseWorldAfterZoom = new Vector2(
                    (mousePosRel.X / plot_sz.X * 2 - 1) * (_autoScale ? 5.0f : _viewScale) / _lateralViewZoom +
                    _lateralViewOffset.X,
                    mousePosRel.Y / plot_sz.Y * _options.BoreholeDataset.TotalDepth / _lateralViewZoom +
                    _lateralViewOffset.Y
                );

                _lateralViewOffset += mouseWorldBeforeZoom - mouseWorldAfterZoom;
            }

            // Panning
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Right))
            {
                var baseRadius = (_autoScale ? 5.0f : _viewScale) / _lateralViewZoom;
                var baseDepth = _options.BoreholeDataset.TotalDepth / _lateralViewZoom;

                _lateralViewOffset.X -= io.MouseDelta.X / plot_sz.X * baseRadius * 2;
                _lateralViewOffset.Y -= io.MouseDelta.Y / plot_sz.Y * baseDepth;
            }
        }

        var viewRadius = (_autoScale ? 5.0f : _viewScale) / _lateralViewZoom;
        var viewDepth = _options.BoreholeDataset.TotalDepth;
        var viewDepthRange = viewDepth / _lateralViewZoom;

        var min_x = _lateralViewOffset.X - viewRadius;
        var max_x = _lateralViewOffset.X + viewRadius;
        var min_z = _lateralViewOffset.Y;
        var max_z = _lateralViewOffset.Y + viewDepthRange;

        var gridX = 150;
        var gridZ = 200;
        var data = new double[gridX, gridZ];
        var allTemps = new List<double>();

        for (var iz = 0; iz < gridZ; iz++)
        {
            var depth = min_z + (float)iz / (gridZ - 1) * viewDepthRange;
            if (depth < 0 || depth > viewDepth) continue;

            var zIndex = FindVerticalIndex(depth);
            var fluidTemps = GetFluidTemperaturesAtDepth(depth);

            for (var ix = 0; ix < gridX; ix++)
            {
                var x = min_x + (max_x - min_x) * ix / (gridX - 1);

                // For a XZ slice, we assume y=0
                var temp = GetTemperatureAtPoint(x, 0, zIndex, fluidTemps);
                data[ix, iz] = temp - 273.15;
                allTemps.Add(data[ix, iz]);
            }
        }

        double minTemp, maxTemp;
        if (_autoColorScale && allTemps.Any())
        {
            allTemps.Sort();
            minTemp = allTemps[(int)(allTemps.Count * 0.02)];
            maxTemp = allTemps[(int)(allTemps.Count * 0.98)];
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

        drawList.PushClipRect(plot_p0, plot_p1, true);

        var cell_sz = new Vector2(plot_sz.X / gridX, plot_sz.Y / gridZ);
        for (var ix = 0; ix < gridX; ix++)
        for (var iz = 0; iz < gridZ; iz++)
        {
            var temp = data[ix, iz];
            var t = (temp - minTemp) / (maxTemp - minTemp);
            var color = GetJetColor((float)t);

            var cell_p0 = new Vector2(plot_p0.X + ix * cell_sz.X, plot_p0.Y + iz * cell_sz.Y);
            var cell_p1 = new Vector2(cell_p0.X + cell_sz.X, cell_p0.Y + cell_sz.Y);
            drawList.AddRectFilled(cell_p0, cell_p1, ImGui.ColorConvertFloat4ToU32(color));
        }

        Func<Vector2, Vector2> WorldToScreen = worldPos =>
        {
            var screenX = plot_p0.X + (worldPos.X - min_x) / (max_x - min_x) * plot_sz.X;
            var screenY = plot_p0.Y + (worldPos.Y - min_z) / viewDepthRange * plot_sz.Y;
            return new Vector2(screenX, screenY);
        };

        // Draw Borehole geometry
        var borehole_p0_world = new Vector2(-_options.BoreholeDataset.WellDiameter / 2, 0);
        var borehole_p1_world = new Vector2(_options.BoreholeDataset.WellDiameter / 2, viewDepth);
        drawList.AddRect(WorldToScreen(borehole_p0_world), WorldToScreen(borehole_p1_world), 0x80000000, 0,
            ImDrawFlags.None, Math.Max(1f, _lateralViewZoom * 0.5f));

        // --- MODIFICATION START: Draw U-Tube pipes ---
        if (_options.HeatExchangerType == HeatExchangerType.UTube)
        {
            var pipeRadius = (float)_options.PipeOuterDiameter / 2.0f;
            var pipeSpacing = (float)_options.PipeSpacing / 2.0f;

            // Downflow pipe
            var p1_start = new Vector2(-pipeSpacing - pipeRadius, 0);
            var p1_end = new Vector2(-pipeSpacing + pipeRadius, viewDepth);
            drawList.AddRectFilled(WorldToScreen(p1_start), WorldToScreen(p1_end), 0x90303030);

            // Upflow pipe
            var p2_start = new Vector2(pipeSpacing - pipeRadius, 0);
            var p2_end = new Vector2(pipeSpacing + pipeRadius, viewDepth);
            drawList.AddRectFilled(WorldToScreen(p2_start), WorldToScreen(p2_end), 0x90303030);
        }
        // --- MODIFICATION END ---

        drawList.PopClipRect();

        var title = "Lateral Temperature View (XZ Plane)";
        var titleSize = ImGui.CalcTextSize(title);
        drawList.AddText(new Vector2(plot_p0.X + plot_sz.X / 2 - titleSize.X / 2, plot_p0.Y - 30), 0xFFFFFFFF, title);

        var xLabel = _options.HeatExchangerType == HeatExchangerType.UTube ? "X (m)" : "Y (m)";
        DrawAxes(drawList, plot_p0, plot_sz, viewRadius, viewDepth, xLabel, "Depth (m)", true, _lateralViewOffset);
        if (_showLegend)
            DrawColorBar(drawList, new Vector2(plot_p1.X + 20, plot_p0.Y), new Vector2(20, plot_sz.Y), minTemp, maxTemp,
                "Temp (°C)");
    }

    /// <summary>
    ///     Renders the ground temperature only, masking the borehole.
    /// </summary>
    private void RenderGroundTemperatureOnlyImGui()
    {
        // This is very similar to the Combined view, but we mask the center.
        var drawList = ImGui.GetWindowDrawList();
        var canvas_p0 = ImGui.GetCursorScreenPos();
        var canvas_sz = ImGui.GetContentRegionAvail();
        if (canvas_sz.X < 50 || canvas_sz.Y < 50) return;

        var margin = new Vector4(80, 40, 100, 60);
        var plot_p0 = new Vector2(canvas_p0.X + margin.X, canvas_p0.Y + margin.Y);
        var plot_sz = new Vector2(canvas_sz.X - margin.X - margin.Z, canvas_sz.Y - margin.Y - margin.W);
        var plot_p1 = new Vector2(plot_p0.X + plot_sz.X, plot_p0.Y + plot_sz.Y);

        drawList.AddRectFilled(plot_p0, plot_p1, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1f)));

        var zIndex = FindVerticalIndex(_selectedDepthMeters);
        var maxRadius = _autoScale ? 2.0f : _viewScale; // Slightly larger default view
        var heRadius = _options.PipeOuterDiameter / 2.0 * 1.5;

        var gridSize = 150;
        var data = new double[gridSize, gridSize];
        var tempList = new List<double>();

        for (var ix = 0; ix < gridSize; ix++)
        for (var iy = 0; iy < gridSize; iy++)
        {
            var x = -maxRadius + 2 * maxRadius * ix / (gridSize - 1);
            var y = -maxRadius + 2 * maxRadius * iy / (gridSize - 1);
            var r = Math.Sqrt(x * x + y * y);

            if (r < heRadius)
            {
                data[ix, iy] = double.NaN; // Mask region
            }
            else
            {
                var temp = InterpolateGroundTemperature(x, y, zIndex);
                data[ix, iy] = temp - 273.15;
                tempList.Add(data[ix, iy]);
            }
        }

        double minTemp = 10, maxTemp = 20;
        if (_autoColorScale && tempList.Any())
        {
            tempList.Sort();
            minTemp = tempList[(int)(tempList.Count * 0.05)];
            maxTemp = tempList[(int)(tempList.Count * 0.95)];
        }
        else if (!_autoColorScale)
        {
            minTemp = _minTempScale;
            maxTemp = _maxTempScale;
        }

        var cell_sz = new Vector2(plot_sz.X / gridSize, plot_sz.Y / gridSize);
        for (var ix = 0; ix < gridSize; ix++)
        for (var iy = 0; iy < gridSize; iy++)
        {
            if (double.IsNaN(data[ix, iy])) continue;
            var t = (data[ix, iy] - minTemp) / (maxTemp - minTemp);
            var color = GetJetColor((float)t);
            var cell_p0 = new Vector2(plot_p0.X + ix * cell_sz.X, plot_p0.Y + (gridSize - 1 - iy) * cell_sz.Y);
            var cell_p1 = new Vector2(cell_p0.X + cell_sz.X, cell_p0.Y + cell_sz.Y);
            drawList.AddRectFilled(cell_p0, cell_p1, ImGui.ColorConvertFloat4ToU32(color));
        }

        // Draw a grey circle over the masked region
        Func<float, float> ScaleRadius = r => r / maxRadius * (plot_sz.X / 2f);
        var centerScreen = plot_p0 + plot_sz / 2;
        drawList.AddCircleFilled(centerScreen, ScaleRadius((float)heRadius), 0xFF404040);

        var title = $"Ground Temperature at {_selectedDepthMeters:F1} m";
        var titleSize = ImGui.CalcTextSize(title);
        drawList.AddText(new Vector2(plot_p0.X + plot_sz.X / 2 - titleSize.X / 2, plot_p0.Y - 30), 0xFFFFFFFF, title);

        DrawAxes(drawList, plot_p0, plot_sz, maxRadius, maxRadius);
        if (_showLegend)
            DrawColorBar(drawList, new Vector2(plot_p1.X + 20, plot_p0.Y), new Vector2(20, plot_sz.Y), minTemp, maxTemp,
                "Temp (°C)");
    }

    /// <summary>
    ///     NEW: Renders a schematic of the fluid circulation direction.
    /// </summary>
    private void RenderFluidCirculationViewImGui()
    {
        var drawList = ImGui.GetWindowDrawList();
        var canvas_p0 = ImGui.GetCursorScreenPos();
        var canvas_sz = ImGui.GetContentRegionAvail();
        if (canvas_sz.X < 50 || canvas_sz.Y < 50) return;

        var margin = new Vector4(80, 40, 100, 60);
        var plot_p0 = new Vector2(canvas_p0.X + margin.X, canvas_p0.Y + margin.Y);
        var plot_sz = new Vector2(canvas_sz.X - margin.X - margin.Z, canvas_sz.Y - margin.Y - margin.W);
        var plot_p1 = new Vector2(plot_p0.X + plot_sz.X, plot_p0.Y + plot_sz.Y);

        drawList.AddRectFilled(plot_p0, plot_p1, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1f)));

        var maxRadius = _options.BoreholeDataset.WellDiameter * 2;

        Func<Vector2, Vector2> WorldToScreen = worldPos =>
        {
            var screenX = plot_p0.X + (worldPos.X - -maxRadius) / (2 * maxRadius) * plot_sz.X;
            var screenY = plot_p1.Y - (worldPos.Y - -maxRadius) / (2 * maxRadius) * plot_sz.Y;
            return new Vector2(screenX, screenY);
        };
        Func<float, float> ScaleRadius = r => r / maxRadius * (plot_sz.X / 2f);

        var centerScreen = WorldToScreen(Vector2.Zero);

        // Draw Borehole and grout
        drawList.AddCircleFilled(centerScreen, ScaleRadius(_options.BoreholeDataset.WellDiameter / 2), 0xFF606060);

        var title = $"Fluid Circulation at {_selectedDepthMeters:F1} m";
        var titleSize = ImGui.CalcTextSize(title);
        drawList.AddText(new Vector2(plot_p0.X + plot_sz.X / 2 - titleSize.X / 2, plot_p0.Y - 30), 0xFFFFFFFF, title);

        var downFlowSymbol = "INTO PAGE (DOWN)";
        var upFlowSymbol = "OUT OF PAGE (UP)";
        var downColor = ImGui.GetColorU32(new Vector4(0.2f, 0.5f, 1.0f, 1.0f));
        var upColor = ImGui.GetColorU32(new Vector4(1.0f, 0.5f, 0.2f, 1.0f));

        if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
        {
            var innerPipeOuterRadius = (float)_options.PipeSpacing / 2;
            var outerPipeOuterRadius = (float)_options.PipeOuterDiameter / 2;

            drawList.AddCircleFilled(centerScreen, ScaleRadius(outerPipeOuterRadius), 0xFFA0A0A0);
            drawList.AddCircleFilled(centerScreen, ScaleRadius(innerPipeOuterRadius), 0xFFC0C0C0);
            drawList.AddCircle(centerScreen, ScaleRadius(innerPipeOuterRadius), 0xFF000000, 0, 2f);
            drawList.AddCircle(centerScreen, ScaleRadius(outerPipeOuterRadius), 0xFF000000, 0, 2f);

            var downIsInCenter = _options.FlowConfiguration == FlowConfiguration.CounterFlow;

            // Draw symbols
            var symbolSize = ScaleRadius(innerPipeOuterRadius * 0.3f);
            if (downIsInCenter)
            {
                drawList.AddCircleFilled(centerScreen, symbolSize, downColor, 20);
                drawList.AddCircle(centerScreen, symbolSize * 0.5f, 0xFFFFFFFF, 20, 2f);
                var annulusPos = WorldToScreen(new Vector2(innerPipeOuterRadius * 1.5f, 0));
                drawList.AddCircleFilled(annulusPos, symbolSize, upColor, 20);
                drawList.AddLine(annulusPos - new Vector2(symbolSize, 0) * 0.3f,
                    annulusPos + new Vector2(symbolSize, 0) * 0.3f, 0xFFFFFFFF, 2f);
                drawList.AddLine(annulusPos - new Vector2(0, symbolSize) * 0.3f,
                    annulusPos + new Vector2(0, symbolSize) * 0.3f, 0xFFFFFFFF, 2f);
            }
            else // Up is in center
            {
                drawList.AddCircleFilled(centerScreen, symbolSize, upColor, 20);
                drawList.AddLine(centerScreen - new Vector2(symbolSize, 0) * 0.3f,
                    centerScreen + new Vector2(symbolSize, 0) * 0.3f, 0xFFFFFFFF, 2f);
                drawList.AddLine(centerScreen - new Vector2(0, symbolSize) * 0.3f,
                    centerScreen + new Vector2(0, symbolSize) * 0.3f, 0xFFFFFFFF, 2f);
                var annulusPos = WorldToScreen(new Vector2(innerPipeOuterRadius * 1.5f, 0));
                drawList.AddCircleFilled(annulusPos, symbolSize, downColor, 20);
                drawList.AddCircle(annulusPos, symbolSize * 0.5f, 0xFFFFFFFF, 20, 2f);
            }
        }
        else // U-Tube
        {
            var pipeRadius = (float)_options.PipeOuterDiameter / 2;
            var pipeSpacing = (float)_options.PipeSpacing / 2;
            var p1_world = new Vector2(-pipeSpacing, 0);
            var p2_world = new Vector2(pipeSpacing, 0);

            var p1_screen = WorldToScreen(p1_world);
            var p2_screen = WorldToScreen(p2_world);

            drawList.AddCircleFilled(p1_screen, ScaleRadius(pipeRadius), downColor);
            drawList.AddCircleFilled(p2_screen, ScaleRadius(pipeRadius), upColor);

            var symbolSize = ScaleRadius(pipeRadius * 0.5f);
            // Down pipe
            drawList.AddCircle(p1_screen, symbolSize * 0.5f, 0xFFFFFFFF, 20, 2f);
            // Up pipe
            drawList.AddLine(p2_screen - new Vector2(symbolSize, 0) * 0.3f,
                p2_screen + new Vector2(symbolSize, 0) * 0.3f, 0xFFFFFFFF, 2f);
            drawList.AddLine(p2_screen - new Vector2(0, symbolSize) * 0.3f,
                p2_screen + new Vector2(0, symbolSize) * 0.3f, 0xFFFFFFFF, 2f);
        }

        // Legend
        var legendP = new Vector2(plot_p1.X + 20, plot_p0.Y);
        drawList.AddCircleFilled(legendP, 10, downColor);
        drawList.AddText(legendP + new Vector2(15, -7), 0xFFFFFFFF, downFlowSymbol);
        drawList.AddCircleFilled(legendP + new Vector2(0, 30), 10, upColor);
        drawList.AddText(legendP + new Vector2(15, 23), 0xFFFFFFFF, upFlowSymbol);
    }

    /// <summary>
    ///     NEW: Renders a heatmap of the fluid velocity magnitude.
    /// </summary>
    private void RenderVelocityViewImGui()
    {
        var drawList = ImGui.GetWindowDrawList();
        var canvas_p0 = ImGui.GetCursorScreenPos();
        var canvas_sz = ImGui.GetContentRegionAvail();
        if (canvas_sz.X < 50 || canvas_sz.Y < 50) return;

        var margin = new Vector4(80, 40, 100, 60);
        var plot_p0 = new Vector2(canvas_p0.X + margin.X, canvas_p0.Y + margin.Y);
        var plot_sz = new Vector2(canvas_sz.X - margin.X - margin.Z, canvas_sz.Y - margin.Y - margin.W);
        var plot_p1 = new Vector2(plot_p0.X + plot_sz.X, plot_p0.Y + plot_sz.Y);

        drawList.AddRectFilled(plot_p0, plot_p1, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1f)));

        var maxRadius = (float)(_options.BoreholeDataset.WellDiameter * 1.2);

        // Calculate velocities
        double v_down = 0, v_up = 0;
        if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
        {
            var r_inner_effective = _options.PipeSpacing / 2.0;
            var r_outer_inner = _options.PipeInnerDiameter / 2.0;

            var area_center = Math.PI * r_inner_effective * r_inner_effective;
            var area_annulus = Math.PI * (r_outer_inner * r_outer_inner - r_inner_effective * r_inner_effective);

            var v_center = _options.FluidMassFlowRate / (_options.FluidDensity * area_center);
            var v_annulus = _options.FluidMassFlowRate / (_options.FluidDensity * area_annulus);

            v_down = _options.FlowConfiguration == FlowConfiguration.CounterFlow ? v_center : v_annulus;
            v_up = _options.FlowConfiguration == FlowConfiguration.CounterFlow ? v_annulus : v_center;
        }
        else // U-Tube
        {
            var area = Math.PI * Math.Pow(_options.PipeInnerDiameter / 2.0, 2);
            v_down = v_up = _options.FluidMassFlowRate / (_options.FluidDensity * area);
        }

        var max_v = Math.Max(v_down, v_up);
        double min_v = 0; // Ground velocity is zero

        var gridSize = 100;
        var cell_sz = new Vector2(plot_sz.X / gridSize, plot_sz.Y / gridSize);

        for (var ix = 0; ix < gridSize; ix++)
        for (var iy = 0; iy < gridSize; iy++)
        {
            var x = -maxRadius + 2 * maxRadius * ix / (gridSize - 1);
            var y = -maxRadius + 2 * maxRadius * iy / (gridSize - 1);

            var r = Math.Sqrt(x * x + y * y);
            double v = 0;

            if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
            {
                if (r <= _options.PipeSpacing / 2.0)
                    v = _options.FlowConfiguration == FlowConfiguration.CounterFlow ? v_down : v_up;
                else if (r > _options.PipeSpacing / 2.0 && r <= _options.PipeOuterDiameter / 2.0)
                    v = _options.FlowConfiguration == FlowConfiguration.CounterFlow ? v_up : v_down;
            }
            else // U-Tube
            {
                var pipe_r = _options.PipeInnerDiameter / 2.0; // Check against inner diameter for fluid
                var spacing = _options.PipeSpacing / 2.0;
                var dist_p1_sq = Math.Pow(x + spacing, 2) + y * y;
                var dist_p2_sq = Math.Pow(x - spacing, 2) + y * y;

                if (dist_p1_sq <= pipe_r * pipe_r)
                    v = v_down;
                else if (dist_p2_sq <= pipe_r * pipe_r)
                    v = v_up;
            }

            var t = max_v > min_v ? (v - min_v) / (max_v - min_v) : 0;
            var color = GetJetColor((float)t);

            var cell_p0 = new Vector2(plot_p0.X + ix * cell_sz.X, plot_p0.Y + (gridSize - 1 - iy) * cell_sz.Y);
            var cell_p1 = new Vector2(cell_p0.X + cell_sz.X, cell_p0.Y + cell_sz.Y);
            drawList.AddRectFilled(cell_p0, cell_p1, ImGui.ColorConvertFloat4ToU32(color));
        }

        var title = $"Fluid Velocity at {_selectedDepthMeters:F1} m";
        var titleSize = ImGui.CalcTextSize(title);
        drawList.AddText(new Vector2(plot_p0.X + plot_sz.X / 2 - titleSize.X / 2, plot_p0.Y - 30), 0xFFFFFFFF, title);

        DrawAxes(drawList, plot_p0, plot_sz, maxRadius, maxRadius);
        if (_showLegend)
            DrawColorBar(drawList, new Vector2(plot_p1.X + 20, plot_p0.Y), new Vector2(20, plot_sz.Y), min_v, max_v,
                "Velocity (m/s)");
    }

    /// <summary>
    ///     NEW: Renders a 2D line plot of the fluid temperature profile vs. depth.
    /// </summary>
    private void RenderFluidTemperatureProfileImGui()
    {
        var drawList = ImGui.GetWindowDrawList();
        var canvas_p0 = ImGui.GetCursorScreenPos();
        var canvas_sz = ImGui.GetContentRegionAvail();
        if (canvas_sz.X < 50 || canvas_sz.Y < 50) return;

        var margin = new Vector4(80, 40, 100, 60);
        var plot_p0 = new Vector2(canvas_p0.X + margin.X, canvas_p0.Y + margin.Y);
        var plot_sz = new Vector2(canvas_sz.X - margin.X - margin.Z, canvas_sz.Y - margin.Y - margin.W);
        var plot_p1 = new Vector2(plot_p0.X + plot_sz.X, plot_p0.Y + plot_sz.Y);

        drawList.AddRectFilled(plot_p0, plot_p1, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1f)));

        if (_results.FluidTemperatureProfile == null || !_results.FluidTemperatureProfile.Any())
        {
            drawList.AddText(plot_p0 + new Vector2(20, 20), 0xFFFFFFFF, "No fluid temperature profile data available.");
            return;
        }

        var profile = _results.FluidTemperatureProfile.OrderBy(p => p.depth).ToList();

        var minTemp = profile.Min(p => Math.Min(p.temperatureDown, p.temperatureUp)) - 273.15;
        var maxTemp = profile.Max(p => Math.Max(p.temperatureDown, p.temperatureUp)) - 273.15;
        var maxDepth = _options.BoreholeDataset.TotalDepth;
        var minDepth = 0;

        var tempRange = maxTemp - minTemp;
        minTemp -= tempRange * 0.1;
        maxTemp += tempRange * 0.1;

        Func<Vector2, Vector2> WorldToScreen = worldPos =>
        {
            var screenX = plot_p0.X + (float)((worldPos.X - minTemp) / (maxTemp - minTemp)) * plot_sz.X;
            var screenY = plot_p0.Y + (worldPos.Y - minDepth) / (maxDepth - minDepth) * plot_sz.Y;
            return new Vector2(screenX, screenY);
        };

        // Draw grid
        if (_showGrid)
        {
            uint gridColor = 0x40FFFFFF;
            for (var i = 0; i <= 4; i++) // Vertical grid lines (temp)
            {
                var x = plot_p0.X + i / 4.0f * plot_sz.X;
                drawList.AddLine(new Vector2(x, plot_p0.Y), new Vector2(x, plot_p1.Y), gridColor);
            }

            for (var i = 0; i <= 4; i++) // Horizontal grid lines (depth)
            {
                var y = plot_p0.Y + i / 4.0f * plot_sz.Y;
                drawList.AddLine(new Vector2(plot_p0.X, y), new Vector2(plot_p1.X, y), gridColor);
            }
        }

        // Plot Downflow
        var downColor = ImGui.GetColorU32(new Vector4(0.2f, 0.5f, 1.0f, 1.0f));
        for (var i = 0; i < profile.Count - 1; i++)
        {
            var p1_world = new Vector2((float)(profile[i].temperatureDown - 273.15), (float)profile[i].depth);
            var p2_world = new Vector2((float)(profile[i + 1].temperatureDown - 273.15), (float)profile[i + 1].depth);
            drawList.AddLine(WorldToScreen(p1_world), WorldToScreen(p2_world), downColor, 2.0f);
        }

        // Plot Upflow
        var upColor = ImGui.GetColorU32(new Vector4(1.0f, 0.5f, 0.2f, 1.0f));
        for (var i = 0; i < profile.Count - 1; i++)
        {
            var p1_world = new Vector2((float)(profile[i].temperatureUp - 273.15), (float)profile[i].depth);
            var p2_world = new Vector2((float)(profile[i + 1].temperatureUp - 273.15), (float)profile[i + 1].depth);
            drawList.AddLine(WorldToScreen(p1_world), WorldToScreen(p2_world), upColor, 2.0f);
        }

        var title = "Fluid Temperature vs. Depth";
        var titleSize = ImGui.CalcTextSize(title);
        drawList.AddText(new Vector2(plot_p0.X + plot_sz.X / 2 - titleSize.X / 2, plot_p0.Y - 30), 0xFFFFFFFF, title);

        // Axes
        var axisColor = 0xFFAAAAAA;
        // X-Axis (Temperature)
        drawList.AddLine(new Vector2(plot_p0.X, plot_p1.Y), new Vector2(plot_p1.X, plot_p1.Y), axisColor, 1f);
        for (var i = 0; i <= 4; i++)
        {
            var temp = minTemp + i / 4.0 * (maxTemp - minTemp);
            var pos = WorldToScreen(new Vector2((float)temp, maxDepth));
            drawList.AddLine(pos, pos + new Vector2(0, 5), axisColor);
            var label = $"{temp:F1}";
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(pos + new Vector2(-labelSize.X / 2, 8), axisColor, label);
        }

        var xLabel = "Temperature (°C)";
        var xLabelSize = ImGui.CalcTextSize(xLabel);
        drawList.AddText(new Vector2(plot_p0.X + plot_sz.X / 2 - xLabelSize.X / 2, plot_p1.Y + 25), axisColor, xLabel);

        // Y-Axis (Depth)
        drawList.AddLine(new Vector2(plot_p0.X, plot_p0.Y), new Vector2(plot_p0.X, plot_p1.Y), axisColor, 1f);
        for (var i = 0; i <= 4; i++)
        {
            var depth = minDepth + i / 4.0 * (maxDepth - minDepth);
            var pos = WorldToScreen(new Vector2((float)minTemp, (float)depth));
            drawList.AddLine(pos, pos - new Vector2(5, 0), axisColor);
            var label = $"{depth:F0}";
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(pos - new Vector2(labelSize.X + 8, labelSize.Y / 2), axisColor, label);
        }

        AddTextVertical(drawList, new Vector2(plot_p0.X - 50, plot_p0.Y + plot_sz.Y / 2), axisColor, "Depth (m)");

        // Legend
        if (_showLegend)
        {
            var legendP = new Vector2(plot_p1.X + 15, plot_p0.Y);
            drawList.AddLine(legendP, legendP + new Vector2(20, 0), downColor, 2f);
            drawList.AddText(legendP + new Vector2(25, -7), 0xFFFFFFFF, "Downflow");
            drawList.AddLine(legendP + new Vector2(0, 30), legendP + new Vector2(20, 30), upColor, 2f);
            drawList.AddText(legendP + new Vector2(25, 23), 0xFFFFFFFF, "Upflow");
        }
    }

    /// <summary>
    ///     NEW: Renders a text-based debug view of the data at the current cross-section.
    /// </summary>
    private void RenderDebugViewImGui()
    {
        var canvas_p0 = ImGui.GetCursorScreenPos();
        var canvas_sz = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("DebugText", canvas_sz);

        if (_results?.FinalTemperatureField == null || _mesh == null)
        {
            ImGui.Text("No data loaded for debug view.");
            ImGui.EndChild();
            return;
        }

        var zIndex = FindVerticalIndex(_selectedDepthMeters);
        var fluidTemps = GetFluidTemperaturesAtDepth(_selectedDepthMeters);

        ImGui.Text("=== CROSS-SECTION DEBUG ===");
        ImGui.Text($"Depth: {_selectedDepthMeters:F1}m, z-index: {zIndex}");
        ImGui.Text($"Flow Configuration: {_options.FlowConfiguration}");
        ImGui.Text($"Heat Exchanger Type: {_options.HeatExchangerType}");
        ImGui.Text($"Fluid Temps - Down: {fluidTemps.down - 273.15:F1}°C, Up: {fluidTemps.up - 273.15:F1}°C");

        var centerTemp = GetTemperatureAtPoint(0, 0, zIndex, fluidTemps) - 273.15;
        ImGui.Text($"Center Temperature (GetTemperatureAtPoint): {centerTemp:F1}°C");

        ImGui.Separator();
        ImGui.Text("Radial Temperature Profile (y=0):");
        for (var i = 0; i < Math.Min(20, _mesh.RadialPoints); i++)
        {
            var r = _mesh.R[i];
            var temp = GetTemperatureAtPoint(r, 0, zIndex, fluidTemps) - 273.15;
            var label = "";

            if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
            {
                if (r <= _options.PipeSpacing / 2.0) label = " [Inner Pipe]";
                else if (r <= _options.PipeOuterDiameter / 2) label = " [Annulus]";
                else label = " [Ground]";
            }
            else // U-Tube
            {
                var pipeRadius = _options.PipeOuterDiameter / 2.0;
                var pipeDist = _options.PipeSpacing / 2.0;
                if (Math.Abs(r - pipeDist) < pipeRadius) label = " [U-Tube Pipe]";
                else if (r < _options.BoreholeDataset.WellDiameter / 2.0) label = " [Grout]";
                else label = " [Ground]";
            }

            ImGui.Text($"  r={r:F4}m: {temp:F1}°C{label}");
        }

        ImGui.EndChild();
    }

    /// <summary>
    ///     A flexible axis drawing function for different view planes.
    /// </summary>
    private void DrawAxes(ImDrawListPtr drawList, Vector2 p0, Vector2 sz, float xMax, float yMax,
        string xLabel = "X (m)", string yLabel = "Y (m)", bool yInverted = false, Vector2? worldOffset = null)
    {
        var p1 = p0 + sz;
        var axisColor = 0xFFAAAAAA;
        uint gridColor = 0x40FFFFFF;
        var offset = worldOffset ?? Vector2.Zero;

        // X-Axis
        drawList.AddLine(new Vector2(p0.X, p1.Y), new Vector2(p1.X, p1.Y), axisColor, 1f);
        for (var i = 0; i <= 4; i++)
        {
            var val = yInverted ? offset.X - xMax + i / 4f * (2 * xMax) : -xMax + i / 4f * (2 * xMax);
            var xPos = p0.X + i / 4f * sz.X;
            if (_showGrid) drawList.AddLine(new Vector2(xPos, p0.Y), new Vector2(xPos, p1.Y), gridColor);
            drawList.AddLine(new Vector2(xPos, p1.Y), new Vector2(xPos, p1.Y + 5), axisColor);
            var label = $"{val:F1}";
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(xPos - labelSize.X / 2, p1.Y + 8), axisColor, label);
        }

        var xLabelSize = ImGui.CalcTextSize(xLabel);
        drawList.AddText(new Vector2(p0.X + sz.X / 2 - xLabelSize.X / 2, p1.Y + 25), axisColor, xLabel);

        // Y-Axis
        drawList.AddLine(new Vector2(p0.X, p0.Y), new Vector2(p0.X, p1.Y), axisColor, 1f);
        for (var i = 0; i <= 4; i++)
        {
            var val = yInverted ? offset.Y + i / 4f * (yMax / _lateralViewZoom) : -yMax + i / 4f * (2 * yMax);
            var yPos = yInverted ? p0.Y + i / 4f * sz.Y : p1.Y - i / 4f * sz.Y;
            if (_showGrid) drawList.AddLine(new Vector2(p0.X, yPos), new Vector2(p1.X, yPos), gridColor);
            drawList.AddLine(new Vector2(p0.X - 5, yPos), new Vector2(p0.X, yPos), axisColor);
            var label = yInverted ? $"{val:F0}" : $"{val:F1}";
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(p0.X - labelSize.X - 8, yPos - labelSize.Y / 2), axisColor, label);
        }

        AddTextVertical(drawList, new Vector2(p0.X - 50, p0.Y + sz.Y / 2), axisColor, yLabel);
    }

    private void DrawColorBar(ImDrawListPtr drawList, Vector2 p0, Vector2 sz, double min, double max, string title)
    {
        if (!_showLegend) return;
        var p1 = p0 + sz;
        var nSteps = 100;
        var stepHeight = sz.Y / nSteps;
        for (var i = 0; i < nSteps; i++)
        {
            var t = (float)i / (nSteps - 1);
            var color = GetJetColor(1f - t); // Invert t because we draw from top to bottom
            var step_p0 = new Vector2(p0.X, p0.Y + i * stepHeight);
            var step_p1 = new Vector2(p1.X, p0.Y + (i + 1) * stepHeight);
            drawList.AddRectFilled(step_p0, step_p1, ImGui.ColorConvertFloat4ToU32(color));
        }

        drawList.AddRect(p0, p1, 0xFFFFFFFF);

        var textColor = 0xFFAAAAAA;
        drawList.AddText(new Vector2(p1.X + 5, p0.Y - 7), textColor, $"{max:F1}");
        drawList.AddText(new Vector2(p1.X + 5, p1.Y - 7), textColor, $"{min:F1}");
        AddTextVertical(drawList, new Vector2(p0.X + sz.X + 15, p0.Y + sz.Y / 2), textColor, title);
    }

    private Vector4 GetJetColor(float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        float r = 0, g = 0, b = 0;
        if (t < 0.34f)
        {
            r = 0;
            g = 0;
            b = 0.5f + t / 0.34f * 0.5f;
        }
        else if (t < 0.61f)
        {
            r = 0;
            g = (t - 0.34f) / 0.27f;
            b = 1;
        }
        else if (t < 0.84f)
        {
            r = (t - 0.61f) / 0.23f;
            g = 1;
            b = 1.0f - r;
        }
        else
        {
            r = 1;
            g = 1.0f - (t - 0.84f) / 0.16f;
            b = 0;
        }

        return new Vector4(r, g, b, 1.0f);
    }

    private void AddTextVertical(ImDrawListPtr drawList, Vector2 pos, uint color, string text)
    {
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var totalTextHeight = text.Length * (fontSize * 0.7f);
        var startY = pos.Y - totalTextHeight / 2;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i].ToString();
            var charSize = font.CalcTextSizeA(fontSize, float.MaxValue, -1.0f, c);
            var currentPos = new Vector2(pos.X - charSize.X / 2, startY + i * (fontSize * 0.85f));
            drawList.AddText(currentPos, color, c);
        }
    }

    /// <summary>
    ///     *** DEFINITIVE FIX ***
    ///     Get temperature at a specific point (x,y), correctly considering heat exchanger fluid regions.
    ///     This now accurately handles Coaxial and U-Tube configurations.
    /// </summary>
    private double GetTemperatureAtPoint(double x, double y, int zIndex, (float down, float up) fluidTemps)
    {
        var r = Math.Sqrt(x * x + y * y);
        var pipeOuterRadius = _options.PipeOuterDiameter / 2.0;

        if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
        {
            var innerPipeOuterRadius = _options.PipeSpacing / 2.0;

            // --- REGION 1: Inside the inner pipe ---
            if (r <= innerPipeOuterRadius)
                return _options.FlowConfiguration == FlowConfiguration.CounterFlow ? fluidTemps.down : fluidTemps.up;
            // --- REGION 2: In the annulus between the inner and outer pipes ---
            if (r > innerPipeOuterRadius && r <= pipeOuterRadius)
                return _options.FlowConfiguration == FlowConfiguration.CounterFlow ? fluidTemps.up : fluidTemps.down;
        }
        else if (_options.HeatExchangerType == HeatExchangerType.UTube)
        {
            var pipeRadius = _options.PipeOuterDiameter / 2.0;
            var pipeSpacing = _options.PipeSpacing / 2.0;

            var p1_x = -pipeSpacing; // Downflow pipe
            var p2_x = pipeSpacing; // Upflow pipe

            var dist_p1_sq = (x - p1_x) * (x - p1_x) + y * y;
            if (dist_p1_sq <= pipeRadius * pipeRadius) return fluidTemps.down;

            var dist_p2_sq = (x - p2_x) * (x - p2_x) + y * y;
            if (dist_p2_sq <= pipeRadius * pipeRadius) return fluidTemps.up;

            // --- REGION 3 (U-TUBE): Transition from pipe to ground ---
            var wellRadius = _options.BoreholeDataset.WellDiameter / 2.0;
            if (r <= wellRadius)
            {
                var groundTemp = InterpolateGroundTemperature(x, y, zIndex);
                // Determine temperature of the nearest fluid pipe
                var nearestPipeDist = Math.Sqrt(Math.Min(dist_p1_sq, dist_p2_sq));
                var nearestFluidTemp = dist_p1_sq < dist_p2_sq ? fluidTemps.down : fluidTemps.up;

                // Interpolate between the nearest pipe and the ground
                var t = (nearestPipeDist - pipeRadius) / (wellRadius - pipeRadius);
                t = Math.Clamp(t, 0, 1);
                return nearestFluidTemp * (1 - t) + groundTemp * t;
            }
        }

        // --- REGION 3 (COAXIAL): Transition from outer pipe to ground ---
        if (_options.HeatExchangerType == HeatExchangerType.Coaxial && r > pipeOuterRadius &&
            r <= pipeOuterRadius * 1.2)
        {
            var groundTemp = InterpolateGroundTemperature(x, y, zIndex);

            double outerFluidTemp = _options.FlowConfiguration == FlowConfiguration.CounterFlow
                ? fluidTemps.up
                : fluidTemps.down;

            var t = (r - pipeOuterRadius) / (pipeOuterRadius * 0.2);
            return outerFluidTemp * (1 - t) + groundTemp * t;
        }

        // Default: If we are clearly in the ground, interpolate from the mesh
        return InterpolateGroundTemperature(x, y, zIndex);
    }

    /// <summary>
    ///     Interpolate ground temperature from mesh data using cartesian coordinates.
    ///     MODIFIED: Now supports BTES animation by using GetCurrentTemperatureField().
    /// </summary>
    private double InterpolateGroundTemperature(double x, double y, int zIndex)
    {
        // Convert to cylindrical for mesh lookup
        var r = Math.Sqrt(x * x + y * y);
        var theta = Math.Atan2(y, x);

        // Find radial indices for interpolation
        int r1 = -1, r2 = -1;
        for (var i = 0; i < _mesh.RadialPoints - 1; i++)
            if (r >= _mesh.R[i] && r <= _mesh.R[i + 1])
            {
                r1 = i;
                r2 = i + 1;
                break;
            }

        // Handle out of bounds
        if (r1 == -1)
        {
            if (r < _mesh.R[0])
                r1 = r2 = 0;
            else
                r1 = r2 = _mesh.RadialPoints - 1;
        }

        // FIX: Normalize theta to prevent negative indices
        while (theta < 0) theta += 2 * Math.PI;
        while (theta >= 2 * Math.PI) theta -= 2 * Math.PI;

        var thetaIndex = theta / (2 * Math.PI) * _mesh.AngularPoints;
        var th1 = (int)thetaIndex % _mesh.AngularPoints;
        var th2 = (th1 + 1) % _mesh.AngularPoints;
        var thetaFrac = thetaIndex - th1;

        // Get current temperature field (supports BTES animation)
        var temperatureField = GetCurrentTemperatureField();

        // Bilinear interpolation
        if (r1 == r2)
        {
            // Angular interpolation only
            var t1 = temperatureField[r1, th1, zIndex];
            var t2 = temperatureField[r1, th2, zIndex];
            return t1 + (t2 - t1) * thetaFrac;
        }

        if (_mesh.R[r2] - _mesh.R[r1] < 1e-9) // Avoid division by zero
        {
            var t1 = temperatureField[r1, th1, zIndex];
            var t2 = temperatureField[r1, th2, zIndex];
            return t1 + (t2 - t1) * thetaFrac;
        }

        // Full bilinear interpolation
        var t11 = temperatureField[r1, th1, zIndex];
        var t12 = temperatureField[r1, th2, zIndex];
        var t21 = temperatureField[r2, th1, zIndex];
        var t22 = temperatureField[r2, th2, zIndex];

        var rFrac = (r - _mesh.R[r1]) / (_mesh.R[r2] - _mesh.R[r1]);

        var t_th1 = t11 + (t21 - t11) * rFrac;
        var t_th2 = t12 + (t22 - t12) * rFrac;

        return t_th1 + (t_th2 - t_th1) * thetaFrac;
    }


    /// <summary>
    ///     Get fluid temperatures at specific depth
    /// </summary>
    private (float down, float up) GetFluidTemperaturesAtDepth(float depth)
    {
        if (_results.FluidTemperatureProfile == null || !_results.FluidTemperatureProfile.Any())
            return ((float)_options.FluidInletTemperature, (float)_options.FluidInletTemperature);

        var sortedProfile = _results.FluidTemperatureProfile.OrderBy(p => p.depth).ToList();

        // Before first point
        if (depth < sortedProfile.First().depth)
            return ((float)sortedProfile.First().temperatureDown, (float)sortedProfile.First().temperatureUp);

        // After last point
        if (depth > sortedProfile.Last().depth)
            return ((float)sortedProfile.Last().temperatureDown, (float)sortedProfile.Last().temperatureUp);

        // Linear interpolation between points
        for (var i = 0; i < sortedProfile.Count - 1; i++)
        {
            var p1 = sortedProfile[i];
            var p2 = sortedProfile[i + 1];
            if (depth >= p1.depth && depth <= p2.depth)
            {
                var t = p2.depth - p1.depth > 1e-6 ? (depth - p1.depth) / (p2.depth - p1.depth) : 0;
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
        var closestIndex = 0;
        var minDiff = float.MaxValue;

        for (var k = 0; k < _mesh.VerticalPoints; k++)
        {
            var diff = Math.Abs(_mesh.Z[k] - targetZ);
            if (diff < minDiff)
            {
                minDiff = diff;
                closestIndex = k;
            }
        }

        return closestIndex;
    }

    /// <summary>
    ///     Gets the current temperature field for visualization (supports BTES animation).
    /// </summary>
    private float[,,] GetCurrentTemperatureField()
    {
        if (_btesAnimationEnabled && _availableTimeFrames.Count > 0)
        {
            var selectedTime = _availableTimeFrames[_selectedTimeFrameIndex];
            if (_results.TemperatureFields.TryGetValue(selectedTime, out var tempField))
            {
                return tempField;
            }
        }

        // Fallback to final temperature field
        return _results.FinalTemperatureField;
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
        Logger.Log("\nRadial Temperature Profile (y=0):");
        for (var i = 0; i < Math.Min(10, _mesh.RadialPoints); i++)
        {
            var r = _mesh.R[i];
            var temp = GetTemperatureAtPoint(r, 0, zIndex, fluidTemps) - 273.15;
            var label = "";
            if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
            {
                if (r <= _options.PipeSpacing / 2.0) label = " [Inner Pipe]";
                else if (r <= _options.PipeOuterDiameter / 2) label = " [Annulus]";
                else label = " [Ground]";
            }
            else // U-Tube
            {
                var pipeRadius = _options.PipeOuterDiameter / 2.0;
                var pipeDist = _options.PipeSpacing / 2.0;
                if (Math.Abs(r - pipeDist) < pipeRadius) label = " [U-Tube Pipe]";
                else if (r < _options.BoreholeDataset.WellDiameter / 2.0) label = " [Grout]";
                else label = " [Ground]";
            }

            Logger.Log($"  x={r:F4}m: {temp:F1}°C{label}");
        }

        Logger.Log("\n=== END DEBUG ===");
    }
}
