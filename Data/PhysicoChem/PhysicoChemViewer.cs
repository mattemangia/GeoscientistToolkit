// GeoscientistToolkit/Data/PhysicoChem/PhysicoChemViewer.cs

using System;
using System.Numerics;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Viewer for PhysicoChem datasets - displays 3D mesh, simulation results,
/// and field variables (temperature, pressure, concentrations, etc.)
/// </summary>
public class PhysicoChemViewer : IDatasetViewer
{
    private readonly PhysicoChemDataset _dataset;

    // Camera controls
    private float _cameraDistance = 5.0f;
    private float _cameraPitch = MathF.PI / 6f;
    private Vector3 _cameraTarget = Vector3.Zero;
    private float _cameraYaw = -MathF.PI / 4f;
    private bool _isDragging;
    private bool _isPanning;
    private Vector2 _lastMousePos;

    // Visualization options
    private readonly string[] _fieldOptions = new[]
    {
        "Temperature",
        "Pressure",
        "Porosity",
        "Permeability",
        "Velocity Magnitude",
        "Liquid Saturation",
        "Vapor Saturation",
        "Gas Saturation"
    };

    private int _selectedFieldIndex = 0;
    private bool _showDomains = true;
    private bool _showBoundaryConditions = true;
    private bool _showMesh = true;
    private bool _showVectorField = false;

    // Slice visualization
    private bool _showSlice = false;
    private int _sliceAxis = 2; // 0=X, 1=Y, 2=Z
    private float _slicePosition = 0.5f;

    // Animation
    private bool _isAnimating = false;
    private int _currentTimeStep = 0;
    private float _animationSpeed = 1.0f;

    // View mode
    private ViewMode _viewMode = ViewMode.View3D;
    private bool _showGraphPanel = false;

    // Graph tracking
    private bool[] _trackerEnabled;
    private float _graphTimeRange = 60.0f; // seconds to show

    // Builder mode
    private bool _builderMode = false;
    private PhysicoChemBuilder _builder;

    // Rendering options
    private RenderMode _renderMode = RenderMode.Solid;
    private bool _showWireframe = true;
    private bool _showNormals = false;

    public PhysicoChemViewer(PhysicoChemDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _dataset.Load();
        _builder = new PhysicoChemBuilder(_dataset);
    }

    public void DrawToolbarControls()
    {
        // Builder mode toggle
        if (ImGui.Checkbox("Builder Mode", ref _builderMode))
        {
            // Reset view to 3D when entering builder mode
            if (_builderMode)
                _viewMode = ViewMode.View3D;
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Builder tools (only visible in builder mode)
        if (_builderMode)
        {
            _builder.DrawToolbar();

            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
        }

        // View mode selector
        ImGui.Text("View:");
        ImGui.SameLine();
        if (ImGui.RadioButton("3D", _viewMode == ViewMode.View3D))
            _viewMode = ViewMode.View3D;
        ImGui.SameLine();
        if (ImGui.RadioButton("2D Slice", _viewMode == ViewMode.View2DSlice))
            _viewMode = ViewMode.View2DSlice;
        ImGui.SameLine();
        if (ImGui.RadioButton("Graphs", _viewMode == ViewMode.ViewGraphs))
            _viewMode = ViewMode.ViewGraphs;

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Rendering mode (only for 3D view)
        if (_viewMode == ViewMode.View3D && !_builderMode)
        {
            ImGui.Text("Render:");
            ImGui.SameLine();
            if (ImGui.RadioButton("Solid", _renderMode == RenderMode.Solid))
                _renderMode = RenderMode.Solid;
            ImGui.SameLine();
            if (ImGui.RadioButton("Wireframe", _renderMode == RenderMode.Wireframe))
                _renderMode = RenderMode.Wireframe;
            ImGui.SameLine();
            if (ImGui.RadioButton("Both", _renderMode == RenderMode.SolidWireframe))
                _renderMode = RenderMode.SolidWireframe;

            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
        }

        // Reset camera (only for 3D view)
        if (_viewMode == ViewMode.View3D && ImGui.Button("Reset Camera"))
        {
            ResetCamera();
        }

        if (_viewMode != ViewMode.ViewGraphs)
        {
            ImGui.SameLine();

            // Field selector
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("Field", ref _selectedFieldIndex, _fieldOptions, _fieldOptions.Length))
            {
                // Field changed
            }
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Visualization toggles (only for 3D and 2D slice views)
        if (_viewMode != ViewMode.ViewGraphs)
        {
            if (_viewMode == ViewMode.View3D)
            {
                ImGui.Checkbox("Mesh", ref _showMesh);
                ImGui.SameLine();
                ImGui.Checkbox("Domains", ref _showDomains);
                ImGui.SameLine();
                ImGui.Checkbox("BC", ref _showBoundaryConditions);
                ImGui.SameLine();
                ImGui.Checkbox("Vectors", ref _showVectorField);
            }

            // Slice controls (only for 2D slice mode)
            if (_viewMode == ViewMode.View2DSlice)
            {
                ImGui.Text("Slice:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                string[] axes = { "X", "Y", "Z" };
                if (ImGui.Combo("##SliceAxis", ref _sliceAxis, axes, axes.Length))
                {
                    // Axis changed
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                ImGui.SliderFloat("##SlicePos", ref _slicePosition, 0.0f, 1.0f, "%.2f");
            }
        }
        else
        {
            // Graph controls
            ImGui.SetNextItemWidth(150);
            ImGui.SliderFloat("Time Range", ref _graphTimeRange, 10.0f, 300.0f, "%.0f s");
        }

        // Animation controls (if simulation has run)
        if (_dataset.ResultHistory != null && _dataset.ResultHistory.Count > 0)
        {
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();

            string playIcon = _isAnimating ? "⏸" : "▶";
            if (ImGui.Button(playIcon))
            {
                _isAnimating = !_isAnimating;
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt("Time", ref _currentTimeStep, 0, _dataset.ResultHistory.Count - 1))
            {
                _isAnimating = false; // Stop animation when manually changing
            }

            ImGui.SameLine();
            ImGui.Text($"t = {GetCurrentTime():F2}s");
        }
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        var availableSize = ImGui.GetContentRegionAvail();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Check if mesh is generated (not needed for graph view)
        if (_dataset.GeneratedMesh == null && _viewMode != ViewMode.ViewGraphs)
        {
            DrawNoMeshMessage();
            return;
        }

        // Update animation
        if (_isAnimating && _dataset.ResultHistory.Count > 0)
        {
            _currentTimeStep = (_currentTimeStep + 1) % _dataset.ResultHistory.Count;
        }

        // Draw based on view mode
        switch (_viewMode)
        {
            case ViewMode.View3D:
                Draw3DView(cursorPos, availableSize);
                break;
            case ViewMode.View2DSlice:
                Draw2DSliceView(cursorPos, availableSize);
                break;
            case ViewMode.ViewGraphs:
                DrawGraphsView(cursorPos, availableSize);
                break;
        }
    }

    private void Draw3DView(Vector2 cursorPos, Vector2 availableSize)
    {
        var drawList = ImGui.GetWindowDrawList();
        var bgColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1.0f));
        drawList.AddRectFilled(cursorPos, cursorPos + availableSize, bgColor);

        // Invisible button for mouse interaction
        ImGui.InvisibleButton("PhysicoChemViewArea", availableSize);
        var isHovered = ImGui.IsItemHovered();
        var isActive = ImGui.IsItemActive();
        var isClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && isHovered;
        var isMouseDragging = ImGui.IsMouseDragging(ImGuiMouseButton.Left);

        // Handle builder interactions first
        if (_builderMode)
        {
            var io = ImGui.GetIO();
            bool handledByBuilder = _builder.HandleMouseInteraction(
                io.MousePos, cursorPos, availableSize,
                isClicked, isMouseDragging,
                _cameraYaw, _cameraPitch
            );

            if (!handledByBuilder && (isHovered || isActive || _isDragging || _isPanning))
            {
                HandleMouseInput();
            }
        }
        else if (isHovered || isActive || _isDragging || _isPanning)
        {
            HandleMouseInput();
        }

        // Render 3D content with render mode
        Render3DContent(drawList, cursorPos, availableSize, _renderMode);

        // Render builder handles if in builder mode
        if (_builderMode)
        {
            _builder.RenderHandles(drawList, cursorPos, availableSize,
                                  _cameraYaw, _cameraPitch, _cameraDistance);
        }

        // Draw info overlay
        DrawInfoOverlay(cursorPos, availableSize);

        // Builder properties panel
        if (_builderMode)
        {
            _builder.DrawPropertiesPanel();
        }
    }

    private void Draw2DSliceView(Vector2 cursorPos, Vector2 availableSize)
    {
        var drawList = ImGui.GetWindowDrawList();
        var bgColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1.0f));
        drawList.AddRectFilled(cursorPos, cursorPos + availableSize, bgColor);

        // Draw 2D slice visualization
        DrawSliceVisualization(drawList, cursorPos, availableSize);

        // Draw info overlay
        DrawInfoOverlay(cursorPos, availableSize);
    }

    private void DrawGraphsView(Vector2 cursorPos, Vector2 availableSize)
    {
        if (_dataset.TrackingManager == null || _dataset.TrackingManager.Trackers.Count == 0)
        {
            DrawNoTrackingDataMessage(cursorPos, availableSize);
            return;
        }

        // Initialize tracker enabled array if needed
        if (_trackerEnabled == null || _trackerEnabled.Length != _dataset.TrackingManager.Trackers.Count)
        {
            _trackerEnabled = new bool[_dataset.TrackingManager.Trackers.Count];
            for (int i = 0; i < _trackerEnabled.Length; i++)
                _trackerEnabled[i] = _dataset.TrackingManager.Trackers[i].Enabled;
        }

        var drawList = ImGui.GetWindowDrawList();

        // Draw tracker selection panel on the left
        float panelWidth = 200;
        DrawTrackerSelectionPanel(cursorPos, new Vector2(panelWidth, availableSize.Y));

        // Draw graphs on the right
        var graphPos = cursorPos + new Vector2(panelWidth + 10, 0);
        var graphSize = new Vector2(availableSize.X - panelWidth - 10, availableSize.Y);
        DrawTrackedParameterGraphs(graphPos, graphSize);
    }

    public void Dispose()
    {
        // Cleanup resources if needed
    }

    private void ResetCamera()
    {
        _cameraYaw = -MathF.PI / 4f;
        _cameraPitch = MathF.PI / 6f;
        _cameraDistance = 5.0f;
        _cameraTarget = Vector3.Zero;
    }

    private void HandleMouseInput()
    {
        var io = ImGui.GetIO();

        // Mouse wheel zoom
        if (io.MouseWheel != 0)
        {
            _cameraDistance = Math.Clamp(_cameraDistance * (1.0f - io.MouseWheel * 0.1f), 0.5f, 50.0f);
        }

        // Start dragging/panning
        if (!_isDragging && !_isPanning)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _isDragging = true;
                _lastMousePos = io.MousePos;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
            {
                _isPanning = true;
                _lastMousePos = io.MousePos;
            }
        }

        // Orbit rotation (left mouse)
        if (_isDragging)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var delta = io.MousePos - _lastMousePos;
                _cameraYaw -= delta.X * 0.01f;
                _cameraPitch = Math.Clamp(_cameraPitch - delta.Y * 0.01f, -MathF.PI / 2.01f, MathF.PI / 2.01f);
                _lastMousePos = io.MousePos;
            }
            else
            {
                _isDragging = false;
            }
        }

        // Pan (middle/right mouse)
        if (_isPanning)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right) || ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                var delta = io.MousePos - _lastMousePos;
                var panSpeed = _cameraDistance * 0.002f;
                _cameraTarget.X -= delta.X * panSpeed;
                _cameraTarget.Y += delta.Y * panSpeed;
                _lastMousePos = io.MousePos;
            }
            else
            {
                _isPanning = false;
            }
        }
    }

    private void Render3DContent(ImDrawListPtr drawList, Vector2 screenPos, Vector2 size, RenderMode renderMode = RenderMode.Solid)
    {
        // For now, draw a simple 3D representation using ImGui primitives
        // In a full implementation, this would use Veldrid rendering like Mesh3DViewer

        var center = screenPos + size * 0.5f;

        // Draw domain bounding boxes (unless in builder mode, builder handles rendering)
        if (_showDomains && !_builderMode)
        {
            foreach (var domain in _dataset.Domains)
            {
                DrawDomainBox(drawList, center, domain, renderMode);
            }
        }

        // Draw mesh grid (simplified)
        if (_showMesh && _dataset.GeneratedMesh != null)
        {
            var gridSize = _dataset.GeneratedMesh.GridSize;
            DrawMeshGrid(drawList, center, gridSize, renderMode);
        }

        // Draw boundary conditions markers (unless in builder mode)
        if (_showBoundaryConditions && !_builderMode)
        {
            foreach (var bc in _dataset.BoundaryConditions)
            {
                DrawBoundaryCondition(drawList, center, bc);
            }
        }

        // Draw axes
        DrawAxes(drawList, center);
    }

    private void DrawDomainBox(ImDrawListPtr drawList, Vector2 center, ReactorDomain domain, RenderMode renderMode)
    {
        if (domain.Geometry == null) return;

        var wireframeColor = ImGui.GetColorU32(new Vector4(0.3f, 0.6f, 0.9f, 0.8f));
        var solidColor = ImGui.GetColorU32(new Vector4(0.3f, 0.6f, 0.9f, 0.3f));
        var scale = 80.0f;

        // Simple 2D projection of 3D box
        var corners = GetProjectedDomainCorners(domain, center, scale);

        // Draw solid fill if enabled
        if (renderMode == RenderMode.Solid || renderMode == RenderMode.SolidWireframe)
        {
            // Draw filled faces (simplified - just draw some quads)
            // Front face
            drawList.AddQuadFilled(corners[0], corners[1], corners[3], corners[2], solidColor);
            // Top face
            drawList.AddQuadFilled(corners[4], corners[5], corners[7], corners[6], solidColor);
        }

        // Draw wireframe if enabled
        if (renderMode == RenderMode.Wireframe || renderMode == RenderMode.SolidWireframe)
        {
            // Draw box edges
            for (int i = 0; i < 4; i++)
            {
                drawList.AddLine(corners[i], corners[(i + 1) % 4], wireframeColor, 1.5f);
                drawList.AddLine(corners[i + 4], corners[((i + 1) % 4) + 4], wireframeColor, 1.5f);
                drawList.AddLine(corners[i], corners[i + 4], wireframeColor, 1.5f);
            }
        }
    }

    private Vector2[] GetProjectedDomainCorners(ReactorDomain domain, Vector2 center, float scale)
    {
        // Simple orthographic projection for now
        var geom = domain.Geometry;
        var corners = new Vector2[8];

        float x = (float)geom.Center.X;
        float y = (float)geom.Center.Y;
        float z = (float)geom.Center.Z;

        // Get dimensions based on geometry type
        float w = 0.5f, h = 0.5f, d = 0.5f;

        switch (geom.Type)
        {
            case GeometryType.Box:
                w = (float)geom.Dimensions.Width * 0.5f;
                h = (float)geom.Dimensions.Height * 0.5f;
                d = (float)geom.Dimensions.Depth * 0.5f;
                break;
            case GeometryType.Sphere:
                w = h = d = (float)geom.Radius;
                break;
            case GeometryType.Cylinder:
                w = d = (float)geom.Radius;
                h = (float)geom.Height * 0.5f;
                break;
        }

        // Apply camera rotation
        float cosYaw = MathF.Cos(_cameraYaw);
        float sinYaw = MathF.Sin(_cameraYaw);
        float cosPitch = MathF.Cos(_cameraPitch);
        float sinPitch = MathF.Sin(_cameraPitch);

        int idx = 0;
        for (int i = 0; i < 2; i++)
        for (int j = 0; j < 2; j++)
        for (int k = 0; k < 2; k++)
        {
            float px = x + (i == 0 ? -w : w);
            float py = y + (j == 0 ? -h : h);
            float pz = z + (k == 0 ? -d : d);

            // Simple rotation
            float rx = px * cosYaw - pz * sinYaw;
            float ry = py * cosPitch - (px * sinYaw + pz * cosYaw) * sinPitch;

            corners[idx++] = center + new Vector2(rx * scale, -ry * scale);
        }

        return corners;
    }

    private void DrawMeshGrid(ImDrawListPtr drawList, Vector2 center, (int X, int Y, int Z) gridSize, RenderMode renderMode)
    {
        // Only draw grid in wireframe modes
        if (renderMode == RenderMode.Solid)
            return;

        var gridColor = ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 0.3f));
        var scale = 3.0f;

        // Draw simple grid overlay
        int step = Math.Max(1, gridSize.X / 10);
        for (int i = 0; i <= gridSize.X; i += step)
        {
            float x = (i - gridSize.X * 0.5f) * scale;
            drawList.AddLine(
                center + new Vector2(x, -50),
                center + new Vector2(x, 50),
                gridColor);
        }

        step = Math.Max(1, gridSize.Y / 10);
        for (int j = 0; j <= gridSize.Y; j += step)
        {
            float y = (j - gridSize.Y * 0.5f) * scale;
            drawList.AddLine(
                center + new Vector2(-50, y),
                center + new Vector2(50, y),
                gridColor);
        }
    }

    private void DrawBoundaryCondition(ImDrawListPtr drawList, Vector2 center, BoundaryCondition bc)
    {
        var color = bc.Type switch
        {
            BoundaryType.FixedValue => ImGui.GetColorU32(new Vector4(1.0f, 0.3f, 0.3f, 0.8f)),
            BoundaryType.FixedFlux => ImGui.GetColorU32(new Vector4(0.3f, 1.0f, 0.3f, 0.8f)),
            BoundaryType.Inlet => ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 1.0f, 0.8f)),
            BoundaryType.Outlet => ImGui.GetColorU32(new Vector4(1.0f, 0.5f, 0.0f, 0.8f)),
            _ => ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.3f, 0.8f))
        };

        // Draw marker at BC location (use custom region center if available)
        float x = bc.Location == BoundaryLocation.Custom ? (float)bc.CustomRegionCenter.X : 0;
        float y = bc.Location == BoundaryLocation.Custom ? (float)bc.CustomRegionCenter.Y : 0;
        var pos = center + new Vector2(x * 2, y * 2);
        drawList.AddCircleFilled(pos, 5, color);
        drawList.AddCircle(pos, 6, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.8f)), 0, 1.5f);
    }

    private void DrawAxes(ImDrawListPtr drawList, Vector2 center)
    {
        float axisLength = 40;

        // X axis (red)
        drawList.AddLine(center, center + new Vector2(axisLength, 0),
            ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), 2.0f);

        // Y axis (green)
        drawList.AddLine(center, center + new Vector2(0, -axisLength),
            ImGui.GetColorU32(new Vector4(0, 1, 0, 1)), 2.0f);

        // Z axis (blue) - projected
        float cosYaw = MathF.Cos(_cameraYaw);
        float sinYaw = MathF.Sin(_cameraYaw);
        drawList.AddLine(center, center + new Vector2(-sinYaw * axisLength, 0),
            ImGui.GetColorU32(new Vector4(0, 0, 1, 1)), 2.0f);
    }

    private void DrawInfoOverlay(Vector2 screenPos, Vector2 size)
    {
        // Draw info text in top-left corner
        var textPos = screenPos + new Vector2(10, 10);
        var drawList = ImGui.GetWindowDrawList();
        var textColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.9f));
        var bgColor = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.5f));

        var gridSize = _dataset.GeneratedMesh.GridSize;
        string info = $"Grid: {gridSize.X} x {gridSize.Y} x {gridSize.Z}\n";
        info += $"Domains: {_dataset.Domains.Count}\n";
        info += $"BCs: {_dataset.BoundaryConditions.Count}\n";
        info += $"Field: {_fieldOptions[_selectedFieldIndex]}";

        if (_dataset.CurrentState != null)
        {
            info += $"\nTime: {_dataset.CurrentState.CurrentTime:F2}s";
        }

        // Draw background
        var textSize = ImGui.CalcTextSize(info);
        drawList.AddRectFilled(textPos - new Vector2(5, 5),
            textPos + textSize + new Vector2(5, 5), bgColor, 5.0f);

        // Draw text
        drawList.AddText(textPos, textColor, info);

        // Draw legend for current field
        if (_dataset.CurrentState != null)
        {
            DrawFieldLegend(screenPos, size);
        }
    }

    private void DrawFieldLegend(Vector2 screenPos, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var legendPos = screenPos + new Vector2(size.X - 80, 20);
        var legendSize = new Vector2(40, 200);

        // Draw colorbar
        for (int i = 0; i < 50; i++)
        {
            float t = i / 50.0f;
            var color = GetColorForValue(t);
            var p1 = legendPos + new Vector2(0, i * 4);
            var p2 = legendPos + new Vector2(legendSize.X, (i + 1) * 4);
            drawList.AddRectFilled(p1, p2, color);
        }

        // Draw border
        drawList.AddRect(legendPos, legendPos + legendSize,
            ImGui.GetColorU32(new Vector4(1, 1, 1, 0.8f)), 0, 0, 1.5f);

        // Draw min/max labels
        var (min, max) = GetFieldRange();
        var textColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.9f));
        drawList.AddText(legendPos + new Vector2(legendSize.X + 5, -5), textColor, $"{max:F1}");
        drawList.AddText(legendPos + new Vector2(legendSize.X + 5, legendSize.Y - 10), textColor, $"{min:F1}");
    }

    private uint GetColorForValue(float normalizedValue)
    {
        // Jet colormap: blue -> cyan -> green -> yellow -> red
        float r, g, b;

        if (normalizedValue < 0.25f)
        {
            r = 0;
            g = normalizedValue * 4;
            b = 1;
        }
        else if (normalizedValue < 0.5f)
        {
            r = 0;
            g = 1;
            b = 1 - (normalizedValue - 0.25f) * 4;
        }
        else if (normalizedValue < 0.75f)
        {
            r = (normalizedValue - 0.5f) * 4;
            g = 1;
            b = 0;
        }
        else
        {
            r = 1;
            g = 1 - (normalizedValue - 0.75f) * 4;
            b = 0;
        }

        return ImGui.GetColorU32(new Vector4(r, g, b, 1.0f));
    }

    private (float min, float max) GetFieldRange()
    {
        // Return reasonable defaults based on field type
        return _selectedFieldIndex switch
        {
            0 => (273.15f, 373.15f), // Temperature (K)
            1 => (1e5f, 1e7f), // Pressure (Pa)
            2 => (0.0f, 0.5f), // Porosity
            3 => (1e-15f, 1e-10f), // Permeability (m²)
            4 => (0.0f, 1.0f), // Velocity
            5 => (0.0f, 1.0f), // Liquid saturation
            6 => (0.0f, 1.0f), // Vapor saturation
            7 => (0.0f, 1.0f), // Gas saturation
            _ => (0.0f, 1.0f)
        };
    }

    private void DrawNoMeshMessage()
    {
        var availableSize = ImGui.GetContentRegionAvail();
        var cursorPos = ImGui.GetCursorScreenPos();

        var drawList = ImGui.GetWindowDrawList();
        var bgColor = ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.17f, 1.0f));
        drawList.AddRectFilled(cursorPos, cursorPos + availableSize, bgColor);

        // Center message
        string message = "No mesh generated.\nUse the Tools panel to create domains and generate mesh.";
        var textSize = ImGui.CalcTextSize(message);
        var textPos = cursorPos + (availableSize - textSize) * 0.5f;

        var textColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
        drawList.AddText(textPos, textColor, message);
    }

    private void DrawSliceVisualization(ImDrawListPtr drawList, Vector2 cursorPos, Vector2 availableSize)
    {
        // Draw 2D slice of the selected field
        var bgColor = ImGui.GetColorU32(new Vector4(0.05f, 0.05f, 0.07f, 1.0f));
        drawList.AddRectFilled(cursorPos, cursorPos + availableSize, bgColor);

        if (_dataset.CurrentState == null)
        {
            var messageColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            var message = "No simulation data available";
            var textSize = ImGui.CalcTextSize(message);
            var textPos = cursorPos + (availableSize - textSize) * 0.5f;
            drawList.AddText(textPos, messageColor, message);
            return;
        }

        var state = _dataset.CurrentState;
        var gridSize = _dataset.GeneratedMesh.GridSize;

        // Get slice index based on position
        int sliceIndex = (int)(_slicePosition * (_sliceAxis == 0 ? gridSize.X : _sliceAxis == 1 ? gridSize.Y : gridSize.Z));

        // Draw colored grid representing field values
        float cellSize = Math.Min(availableSize.X / 100, availableSize.Y / 100);
        var startPos = cursorPos + new Vector2(10, 10);

        int width = _sliceAxis == 0 ? gridSize.Y : gridSize.X;
        int height = _sliceAxis == 2 ? gridSize.Y : gridSize.Z;

        for (int i = 0; i < Math.Min(width, 50); i++)
        for (int j = 0; j < Math.Min(height, 50); j++)
        {
            float value = GetSliceFieldValue(state, i, j, sliceIndex);
            var (min, max) = GetFieldRange();
            float normalizedValue = (value - min) / (max - min + 0.0001f);
            normalizedValue = Math.Clamp(normalizedValue, 0.0f, 1.0f);

            var color = GetColorForValue(normalizedValue);
            var p1 = startPos + new Vector2(i * cellSize, j * cellSize);
            var p2 = p1 + new Vector2(cellSize, cellSize);
            drawList.AddRectFilled(p1, p2, color);
        }

        // Draw axis labels
        var textColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.9f));
        string[] axisNames = { "X", "Y", "Z" };
        string sliceInfo = $"Slice: {axisNames[_sliceAxis]} = {_slicePosition:F2}";
        drawList.AddText(cursorPos + new Vector2(10, availableSize.Y - 30), textColor, sliceInfo);
    }

    private float GetSliceFieldValue(PhysicoChemState state, int i, int j, int sliceIndex)
    {
        var gridSize = _dataset.GeneratedMesh.GridSize;

        // Map 2D coordinates to 3D grid based on slice axis
        int x = 0, y = 0, z = 0;
        switch (_sliceAxis)
        {
            case 0: // X slice
                x = Math.Min(sliceIndex, gridSize.X - 1);
                y = Math.Min(i, gridSize.Y - 1);
                z = Math.Min(j, gridSize.Z - 1);
                break;
            case 1: // Y slice
                x = Math.Min(i, gridSize.X - 1);
                y = Math.Min(sliceIndex, gridSize.Y - 1);
                z = Math.Min(j, gridSize.Z - 1);
                break;
            case 2: // Z slice
                x = Math.Min(i, gridSize.X - 1);
                y = Math.Min(j, gridSize.Y - 1);
                z = Math.Min(sliceIndex, gridSize.Z - 1);
                break;
        }

        return _selectedFieldIndex switch
        {
            0 => state.Temperature[x, y, z],
            1 => state.Pressure[x, y, z],
            2 => state.Porosity[x, y, z],
            3 => state.Permeability[x, y, z],
            4 => MathF.Sqrt(state.VelocityX[x, y, z] * state.VelocityX[x, y, z] +
                           state.VelocityY[x, y, z] * state.VelocityY[x, y, z] +
                           state.VelocityZ[x, y, z] * state.VelocityZ[x, y, z]),
            5 => state.LiquidSaturation[x, y, z],
            6 => state.VaporSaturation[x, y, z],
            7 => state.GasSaturation[x, y, z],
            _ => 0.0f
        };
    }

    private void DrawTrackerSelectionPanel(Vector2 cursorPos, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var bgColor = ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.17f, 1.0f));
        drawList.AddRectFilled(cursorPos, cursorPos + size, bgColor);

        // Draw panel title
        var textColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.9f));
        var titlePos = cursorPos + new Vector2(10, 10);
        drawList.AddText(titlePos, textColor, "Tracked Parameters:");

        // Draw checkboxes for each tracker
        ImGui.SetCursorScreenPos(cursorPos + new Vector2(10, 35));
        for (int i = 0; i < _dataset.TrackingManager.Trackers.Count; i++)
        {
            var tracker = _dataset.TrackingManager.Trackers[i];
            ImGui.Checkbox(tracker.DisplayName, ref _trackerEnabled[i]);
        }
    }

    private void DrawTrackedParameterGraphs(Vector2 cursorPos, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();

        // Count enabled trackers
        int enabledCount = 0;
        for (int i = 0; i < _trackerEnabled.Length; i++)
            if (_trackerEnabled[i])
                enabledCount++;

        if (enabledCount == 0)
        {
            var textColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            var message = "No parameters selected for tracking";
            var textSize = ImGui.CalcTextSize(message);
            var textPos = cursorPos + (size - textSize) * 0.5f;
            drawList.AddText(textPos, textColor, message);
            return;
        }

        // Calculate graph size
        float graphHeight = (size.Y - 20 * (enabledCount + 1)) / enabledCount;
        float yOffset = 10;

        // Draw each enabled tracker's graph
        for (int i = 0; i < _dataset.TrackingManager.Trackers.Count; i++)
        {
            if (!_trackerEnabled[i]) continue;

            var tracker = _dataset.TrackingManager.Trackers[i];
            var graphPos = cursorPos + new Vector2(0, yOffset);
            var graphSize = new Vector2(size.X, graphHeight);

            DrawSingleParameterGraph(drawList, graphPos, graphSize, tracker);

            yOffset += graphHeight + 20;
        }
    }

    private void DrawSingleParameterGraph(ImDrawListPtr drawList, Vector2 cursorPos, Vector2 size, ParameterTracker tracker)
    {
        // Draw background
        var bgColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1.0f));
        drawList.AddRectFilled(cursorPos, cursorPos + size, bgColor);

        // Draw border
        var borderColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
        drawList.AddRect(cursorPos, cursorPos + size, borderColor, 0, 0, 1.0f);

        // Draw title
        var textColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.9f));
        var titleText = $"{tracker.DisplayName} ({tracker.Unit})";
        drawList.AddText(cursorPos + new Vector2(10, 5), textColor, titleText);

        if (tracker.TimePoints.Count < 2)
        {
            var noDataColor = ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
            drawList.AddText(cursorPos + new Vector2(10, size.Y * 0.5f), noDataColor, "No data available");
            return;
        }

        // Get time range to display
        double currentTime = _dataset.CurrentState?.CurrentTime ?? tracker.TimePoints[^1];
        double minTime = Math.Max(0, currentTime - _graphTimeRange);
        double maxTime = currentTime;

        // Get value range
        var stats = tracker.GetStatistics();
        double minValue = stats.Min;
        double maxValue = stats.Max;
        double valueRange = maxValue - minValue;
        if (valueRange < 1e-10) valueRange = 1.0;

        // Draw grid lines
        var gridColor = ImGui.GetColorU32(new Vector4(0.25f, 0.25f, 0.25f, 0.5f));
        for (int i = 0; i <= 4; i++)
        {
            float y = cursorPos.Y + 30 + (size.Y - 50) * i / 4.0f;
            drawList.AddLine(new Vector2(cursorPos.X + 40, y),
                           new Vector2(cursorPos.X + size.X - 10, y), gridColor);
        }

        // Draw data points
        var lineColor = ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1.0f, 1.0f));
        var graphArea = new Vector2(size.X - 50, size.Y - 50);
        var graphStart = cursorPos + new Vector2(40, 30);

        Vector2? lastPoint = null;
        for (int i = 0; i < tracker.TimePoints.Count; i++)
        {
            double time = tracker.TimePoints[i];
            if (time < minTime || time > maxTime) continue;

            double value = tracker.Values[i];
            float x = graphStart.X + (float)((time - minTime) / (maxTime - minTime)) * graphArea.X;
            float y = graphStart.Y + graphArea.Y - (float)((value - minValue) / valueRange) * graphArea.Y;

            var point = new Vector2(x, y);

            if (lastPoint.HasValue)
            {
                drawList.AddLine(lastPoint.Value, point, lineColor, 2.0f);
            }

            lastPoint = point;
        }

        // Draw axis labels
        var labelColor = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
        drawList.AddText(cursorPos + new Vector2(5, 30), labelColor, $"{maxValue:F2}");
        drawList.AddText(cursorPos + new Vector2(5, size.Y - 25), labelColor, $"{minValue:F2}");
        drawList.AddText(cursorPos + new Vector2(40, size.Y - 20), labelColor, $"{minTime:F1}s");
        drawList.AddText(cursorPos + new Vector2(size.X - 50, size.Y - 20), labelColor, $"{maxTime:F1}s");
    }

    private void DrawNoTrackingDataMessage(Vector2 cursorPos, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var bgColor = ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.17f, 1.0f));
        drawList.AddRectFilled(cursorPos, cursorPos + size, bgColor);

        var textColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
        var message = "No tracking data available.\nRun a simulation with tracking enabled.";
        var textSize = ImGui.CalcTextSize(message);
        var textPos = cursorPos + (size - textSize) * 0.5f;
        drawList.AddText(textPos, textColor, message);
    }

    private double GetCurrentTime()
    {
        if (_dataset.ResultHistory == null || _currentTimeStep >= _dataset.ResultHistory.Count)
            return _dataset.CurrentState?.CurrentTime ?? 0.0;

        return _dataset.ResultHistory[_currentTimeStep].CurrentTime;
    }
}

/// <summary>
/// View mode for PhysicoChem viewer
/// </summary>
public enum ViewMode
{
    View3D,
    View2DSlice,
    ViewGraphs
}

/// <summary>
/// Rendering mode for 3D visualization
/// </summary>
public enum RenderMode
{
    Solid,
    Wireframe,
    SolidWireframe
}
