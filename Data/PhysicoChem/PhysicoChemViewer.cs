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

    public PhysicoChemViewer(PhysicoChemDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _dataset.Load();
    }

    public void DrawToolbarControls()
    {
        // Reset camera
        if (ImGui.Button("Reset Camera"))
        {
            ResetCamera();
        }

        ImGui.SameLine();

        // Field selector
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("Field", ref _selectedFieldIndex, _fieldOptions, _fieldOptions.Length))
        {
            // Field changed
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Visualization toggles
        ImGui.Checkbox("Mesh", ref _showMesh);
        ImGui.SameLine();
        ImGui.Checkbox("Domains", ref _showDomains);
        ImGui.SameLine();
        ImGui.Checkbox("BC", ref _showBoundaryConditions);
        ImGui.SameLine();
        ImGui.Checkbox("Vectors", ref _showVectorField);

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Slice controls
        ImGui.Checkbox("Slice", ref _showSlice);
        if (_showSlice)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            string[] axes = { "X", "Y", "Z" };
            if (ImGui.Combo("##SliceAxis", ref _sliceAxis, axes, axes.Length))
            {
                // Axis changed
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.SliderFloat("##SlicePos", ref _slicePosition, 0.0f, 1.0f, "%.2f");
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

        // Check if mesh is generated
        if (_dataset.GeneratedMesh == null)
        {
            DrawNoMeshMessage();
            return;
        }

        // Update animation
        if (_isAnimating && _dataset.ResultHistory.Count > 0)
        {
            _currentTimeStep = (_currentTimeStep + 1) % _dataset.ResultHistory.Count;
        }

        // Draw 3D visualization area
        var drawList = ImGui.GetWindowDrawList();
        var bgColor = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1.0f));
        drawList.AddRectFilled(cursorPos, cursorPos + availableSize, bgColor);

        // Invisible button for mouse interaction
        ImGui.InvisibleButton("PhysicoChemViewArea", availableSize);
        var isHovered = ImGui.IsItemHovered();
        var isActive = ImGui.IsItemActive();

        if (isHovered || isActive || _isDragging || _isPanning)
        {
            HandleMouseInput();
        }

        // Render 3D content (simplified for now)
        Render3DContent(drawList, cursorPos, availableSize);

        // Draw info overlay
        DrawInfoOverlay(cursorPos, availableSize);
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

    private void Render3DContent(ImDrawListPtr drawList, Vector2 screenPos, Vector2 size)
    {
        // For now, draw a simple 3D representation using ImGui primitives
        // In a full implementation, this would use Veldrid rendering like Mesh3DViewer

        var center = screenPos + size * 0.5f;
        var gridSize = _dataset.GeneratedMesh.GridSize;

        // Draw domain bounding boxes
        if (_showDomains)
        {
            foreach (var domain in _dataset.Domains)
            {
                DrawDomainBox(drawList, center, domain);
            }
        }

        // Draw mesh grid (simplified)
        if (_showMesh)
        {
            DrawMeshGrid(drawList, center, gridSize);
        }

        // Draw boundary conditions markers
        if (_showBoundaryConditions)
        {
            foreach (var bc in _dataset.BoundaryConditions)
            {
                DrawBoundaryCondition(drawList, center, bc);
            }
        }

        // Draw axes
        DrawAxes(drawList, center);
    }

    private void DrawDomainBox(ImDrawListPtr drawList, Vector2 center, ReactorDomain domain)
    {
        if (domain.Geometry == null) return;

        var color = ImGui.GetColorU32(new Vector4(0.3f, 0.6f, 0.9f, 0.5f));
        var scale = 80.0f;

        // Simple 2D projection of 3D box
        var corners = GetProjectedDomainCorners(domain, center, scale);

        // Draw box edges
        for (int i = 0; i < 4; i++)
        {
            drawList.AddLine(corners[i], corners[(i + 1) % 4], color, 1.5f);
            drawList.AddLine(corners[i + 4], corners[((i + 1) % 4) + 4], color, 1.5f);
            drawList.AddLine(corners[i], corners[i + 4], color, 1.5f);
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

    private void DrawMeshGrid(ImDrawListPtr drawList, Vector2 center, (int X, int Y, int Z) gridSize)
    {
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

    private double GetCurrentTime()
    {
        if (_dataset.ResultHistory == null || _currentTimeStep >= _dataset.ResultHistory.Count)
            return _dataset.CurrentState?.CurrentTime ?? 0.0;

        return _dataset.ResultHistory[_currentTimeStep].CurrentTime;
    }
}
