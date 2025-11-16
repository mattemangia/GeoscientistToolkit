// GeoscientistToolkit/Analysis/Geomechanics/TriaxialVisualization3D.cs
// 3D visualization for triaxial simulation results
//
// FEATURES:
// - Cylindrical mesh rendering (wireframe and solid)
// - Deformed shape with displacement magnification
// - Stress field visualization (Von Mises, principal stresses)
// - Fracture plane rendering with orientations
// - Interactive camera controls
// - Color mapping for stress/strain fields

using System.Numerics;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public enum VisualizationMode
{
    Mesh,
    Displacement,
    VonMisesStress,
    PrincipalStress,
    Fractures,
    Combined
}

public enum ColorMap
{
    Turbo,
    Viridis,
    Plasma,
    Jet,
    Grayscale
}

public class TriaxialVisualization3D : IDisposable
{
    // Visualization settings
    private VisualizationMode _vizMode = VisualizationMode.Combined;
    private ColorMap _colorMap = ColorMap.Turbo;
    private bool _showWireframe = true;
    private bool _showFracturePlanes = true;
    private bool _showAxes = true;
    private float _displacementScale = 10.0f;
    private float _stressScale = 1.0f;

    // Camera
    private Vector3 _cameraPosition = new Vector3(0, -0.15f, 0.05f);
    private Vector3 _cameraTarget = new Vector3(0, 0, 0.05f);
    private float _cameraDistance = 0.2f;
    private float _cameraAzimuth = 45f;
    private float _cameraElevation = 30f;
    private bool _isDragging;
    private Vector2 _lastMousePos;

    // Rendering state
    private float _minStress, _maxStress;
    private bool _autoScaleStress = true;

    public void Dispose()
    {
        // Cleanup resources if needed
    }

    public void Draw(TriaxialMeshGenerator.TriaxialMesh mesh, TriaxialResults results)
    {
        var availSize = ImGui.GetContentRegionAvail();

        // Controls panel
        ImGui.BeginChild("3DControls", new Vector2(200, 0), ImGuiChildFlags.Border);
        DrawControls();
        ImGui.EndChild();

        ImGui.SameLine();

        // 3D viewport
        ImGui.BeginChild("3DViewport", new Vector2(0, 0), ImGuiChildFlags.Border);
        DrawViewport(mesh, results, ImGui.GetContentRegionAvail());
        ImGui.EndChild();
    }

    private void DrawControls()
    {
        ImGui.Text("Visualization");
        ImGui.Separator();

        int mode = (int)_vizMode;
        if (ImGui.RadioButton("Mesh", ref mode, (int)VisualizationMode.Mesh))
            _vizMode = (VisualizationMode)mode;
        if (ImGui.RadioButton("Displacement", ref mode, (int)VisualizationMode.Displacement))
            _vizMode = (VisualizationMode)mode;
        if (ImGui.RadioButton("Von Mises", ref mode, (int)VisualizationMode.VonMisesStress))
            _vizMode = (VisualizationMode)mode;
        if (ImGui.RadioButton("Principal σ", ref mode, (int)VisualizationMode.PrincipalStress))
            _vizMode = (VisualizationMode)mode;
        if (ImGui.RadioButton("Fractures", ref mode, (int)VisualizationMode.Fractures))
            _vizMode = (VisualizationMode)mode;
        if (ImGui.RadioButton("Combined", ref mode, (int)VisualizationMode.Combined))
            _vizMode = (VisualizationMode)mode;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Display Options");
        ImGui.Spacing();

        ImGui.Checkbox("Wireframe", ref _showWireframe);
        ImGui.Checkbox("Fracture Planes", ref _showFracturePlanes);
        ImGui.Checkbox("Axes", ref _showAxes);

        ImGui.Spacing();

        if (_vizMode == VisualizationMode.Displacement)
        {
            ImGui.DragFloat("Disp. Scale", ref _displacementScale, 0.5f, 1f, 100f);
        }

        if (_vizMode == VisualizationMode.VonMisesStress || _vizMode == VisualizationMode.PrincipalStress)
        {
            ImGui.Checkbox("Auto Scale", ref _autoScaleStress);
            if (!_autoScaleStress)
            {
                ImGui.DragFloat("Min Stress", ref _minStress, 1f, 0f, _maxStress);
                ImGui.DragFloat("Max Stress", ref _maxStress, 1f, _minStress, 1000f);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Color Map");
        ImGui.Spacing();

        int colorMap = (int)_colorMap;
        if (ImGui.Combo("##ColorMap", ref colorMap, "Turbo\0Viridis\0Plasma\0Jet\0Grayscale\0"))
            _colorMap = (ColorMap)colorMap;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Camera");
        ImGui.Spacing();

        ImGui.DragFloat("Distance", ref _cameraDistance, 0.01f, 0.05f, 2f);
        ImGui.DragFloat("Azimuth", ref _cameraAzimuth, 1f, -180f, 180f);
        ImGui.DragFloat("Elevation", ref _cameraElevation, 1f, -89f, 89f);

        if (ImGui.Button("Reset Camera", new Vector2(-1, 0)))
        {
            ResetCamera();
        }

        ImGui.Spacing();
        ImGui.Text("Controls:");
        ImGui.BulletText("Drag: Rotate");
        ImGui.BulletText("Scroll: Zoom");
    }

    private void DrawViewport(TriaxialMeshGenerator.TriaxialMesh mesh, TriaxialResults results, Vector2 size)
    {
        if (mesh == null)
        {
            ImGui.Text("No mesh to display");
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = size;

        // Background
        drawList.AddRectFilled(canvasPos,
            new Vector2(canvasPos.X + canvasSize.X, canvasPos.Y + canvasSize.Y),
            ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.15f, 1)));

        // Handle mouse interaction
        HandleMouseInput(canvasPos, canvasSize);

        // Update camera position
        UpdateCamera();

        // Project and render mesh
        RenderMesh(drawList, canvasPos, canvasSize, mesh, results);

        // Render overlays
        if (_showAxes)
            RenderAxes(drawList, canvasPos, canvasSize);

        if (results != null && _showFracturePlanes && results.FracturePlanes.Count > 0)
            RenderFracturePlanes(drawList, canvasPos, canvasSize, results.FracturePlanes);

        // Info overlay
        DrawInfoOverlay(canvasPos, canvasSize, mesh, results);
    }

    private void HandleMouseInput(Vector2 canvasPos, Vector2 canvasSize)
    {
        var mousePos = ImGui.GetMousePos();
        bool isHovered = mousePos.X >= canvasPos.X && mousePos.X <= canvasPos.X + canvasSize.X &&
                        mousePos.Y >= canvasPos.Y && mousePos.Y <= canvasPos.Y + canvasSize.Y;

        if (isHovered)
        {
            // Zoom with scroll
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0)
            {
                _cameraDistance *= (1 - wheel * 0.1f);
                _cameraDistance = Math.Clamp(_cameraDistance, 0.05f, 2f);
            }

            // Rotate with drag
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    _lastMousePos = mousePos;
                }
                else
                {
                    var delta = mousePos - _lastMousePos;
                    _cameraAzimuth += delta.X * 0.5f;
                    _cameraElevation -= delta.Y * 0.5f;
                    _cameraElevation = Math.Clamp(_cameraElevation, -89f, 89f);
                    _lastMousePos = mousePos;
                }
            }
            else
            {
                _isDragging = false;
            }
        }
        else
        {
            _isDragging = false;
        }
    }

    private void UpdateCamera()
    {
        // Convert spherical to Cartesian
        float azimuthRad = _cameraAzimuth * MathF.PI / 180f;
        float elevationRad = _cameraElevation * MathF.PI / 180f;

        _cameraPosition = _cameraTarget + new Vector3(
            _cameraDistance * MathF.Cos(elevationRad) * MathF.Cos(azimuthRad),
            _cameraDistance * MathF.Cos(elevationRad) * MathF.Sin(azimuthRad),
            _cameraDistance * MathF.Sin(elevationRad)
        );
    }

    private void RenderMesh(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize,
        TriaxialMeshGenerator.TriaxialMesh mesh, TriaxialResults results)
    {
        // Simple orthographic projection
        Vector2 Project(Vector3 p)
        {
            // Transform to camera space
            var view = Vector3.Normalize(_cameraTarget - _cameraPosition);
            var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, view));
            var up = Vector3.Cross(view, right);

            var rel = p - _cameraPosition;
            float x = Vector3.Dot(rel, right);
            float y = Vector3.Dot(rel, up);

            // Project to screen
            float scale = Math.Min(canvasSize.X, canvasSize.Y) * 2.5f;
            float screenX = canvasPos.X + canvasSize.X / 2 + x * scale;
            float screenY = canvasPos.Y + canvasSize.Y / 2 - y * scale;

            return new Vector2(screenX, screenY);
        }

        // Compute stress range if needed
        if (results != null && _autoScaleStress && results.VonMisesStress_MPa != null)
        {
            _minStress = results.VonMisesStress_MPa.Min();
            _maxStress = results.VonMisesStress_MPa.Max();
        }

        // Render elements
        int nElements = mesh.TotalElements;
        for (int i = 0; i < nElements; i++)
        {
            // Get element nodes
            int[] nodeIndices = new int[8];
            for (int j = 0; j < 8; j++)
                nodeIndices[j] = mesh.Elements[i * 8 + j];

            // Get positions (with optional displacement)
            Vector3[] positions = new Vector3[8];
            for (int j = 0; j < 8; j++)
            {
                var pos = mesh.Nodes[nodeIndices[j]];

                if (results?.Displacement != null && _vizMode == VisualizationMode.Displacement)
                {
                    var disp = results.Displacement[nodeIndices[j]] * _displacementScale;
                    pos += disp;
                }

                positions[j] = pos;
            }

            // Compute element color
            uint color = GetElementColor(nodeIndices, results);

            // Draw element faces (simplified - just bottom and top for speed)
            DrawQuad(drawList, Project, positions, new[] { 0, 1, 2, 3 }, color, _showWireframe);
            DrawQuad(drawList, Project, positions, new[] { 4, 5, 6, 7 }, color, _showWireframe);
        }
    }

    private void DrawQuad(ImDrawListPtr drawList, Func<Vector3, Vector2> project,
        Vector3[] positions, int[] indices, uint color, bool wireframe)
    {
        var p0 = project(positions[indices[0]]);
        var p1 = project(positions[indices[1]]);
        var p2 = project(positions[indices[2]]);
        var p3 = project(positions[indices[3]]);

        // Fill
        if (!wireframe)
            drawList.AddQuadFilled(p0, p1, p2, p3, color);

        // Outline
        var lineColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1));
        drawList.AddLine(p0, p1, lineColor, 1f);
        drawList.AddLine(p1, p2, lineColor, 1f);
        drawList.AddLine(p2, p3, lineColor, 1f);
        drawList.AddLine(p3, p0, lineColor, 1f);
    }

    private uint GetElementColor(int[] nodeIndices, TriaxialResults results)
    {
        if (results == null || _vizMode == VisualizationMode.Mesh)
            return ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1));

        // Average stress at element nodes
        float avgStress = 0f;

        if (_vizMode == VisualizationMode.VonMisesStress && results.VonMisesStress_MPa != null)
        {
            foreach (var idx in nodeIndices)
                avgStress += results.VonMisesStress_MPa[idx];
            avgStress /= nodeIndices.Length;
        }
        else if (_vizMode == VisualizationMode.PrincipalStress && results.Stress != null)
        {
            foreach (var idx in nodeIndices)
                avgStress += results.Stress[idx].Z; // Axial stress
            avgStress /= nodeIndices.Length;
        }

        // Map to color
        return GetColorFromValue(avgStress, _minStress, _maxStress);
    }

    private uint GetColorFromValue(float value, float min, float max)
    {
        float t = (max > min) ? (value - min) / (max - min) : 0.5f;
        t = Math.Clamp(t, 0f, 1f);

        Vector3 rgb = _colorMap switch
        {
            ColorMap.Turbo => GetTurboColor(t),
            ColorMap.Viridis => GetViridisColor(t),
            ColorMap.Plasma => GetPlasmaColor(t),
            ColorMap.Jet => GetJetColor(t),
            ColorMap.Grayscale => new Vector3(t, t, t),
            _ => new Vector3(t, t, t)
        };

        return ImGui.GetColorU32(new Vector4(rgb.X, rgb.Y, rgb.Z, 1));
    }

    private Vector3 GetTurboColor(float t)
    {
        // Turbo colormap approximation
        const float r0 = 0.13572138f, r1 = 4.61539260f, r2 = -42.66032258f, r3 = 132.13108234f, r4 = -152.94239396f;
        const float g0 = 0.09140261f, g1 = 2.19418839f, g2 = 4.84296658f, g3 = -14.18503333f, g4 = 4.27729857f;
        const float b0 = 0.10667330f, b1 = 12.64194608f, b2 = -60.58204836f, b3 = 110.36276771f, b4 = -89.90310912f;

        float r = r0 + t * (r1 + t * (r2 + t * (r3 + t * r4)));
        float g = g0 + t * (g1 + t * (g2 + t * (g3 + t * g4)));
        float b = b0 + t * (b1 + t * (b2 + t * (b3 + t * b4)));

        return new Vector3(
            Math.Clamp(r, 0f, 1f),
            Math.Clamp(g, 0f, 1f),
            Math.Clamp(b, 0f, 1f)
        );
    }

    private Vector3 GetViridisColor(float t)
    {
        // Simplified Viridis approximation
        return new Vector3(
            0.267f * t + 0.004f,
            0.005f + 0.989f * t,
            0.329f + 0.549f * t
        );
    }

    private Vector3 GetPlasmaColor(float t)
    {
        // Simplified Plasma approximation
        return new Vector3(
            0.050f + 0.950f * t,
            0.030f + 0.570f * t * (1 - t),
            0.528f - 0.428f * t
        );
    }

    private Vector3 GetJetColor(float t)
    {
        float r = Math.Clamp(Math.Min(4 * t - 1.5f, -4 * t + 4.5f), 0f, 1f);
        float g = Math.Clamp(Math.Min(4 * t - 0.5f, -4 * t + 3.5f), 0f, 1f);
        float b = Math.Clamp(Math.Min(4 * t + 0.5f, -4 * t + 2.5f), 0f, 1f);
        return new Vector3(r, g, b);
    }

    private void RenderAxes(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
    {
        // Draw axis triad in corner
        var origin = new Vector2(canvasPos.X + 50, canvasPos.Y + canvasSize.Y - 50);
        float axisLength = 30f;

        // Transform axis directions to screen space
        Vector2 ProjectAxis(Vector3 dir)
        {
            var view = Vector3.Normalize(_cameraTarget - _cameraPosition);
            var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, view));
            var up = Vector3.Cross(view, right);

            float x = Vector3.Dot(dir, right);
            float y = Vector3.Dot(dir, up);

            return origin + new Vector2(x * axisLength, -y * axisLength);
        }

        var xEnd = ProjectAxis(Vector3.UnitX);
        var yEnd = ProjectAxis(Vector3.UnitY);
        var zEnd = ProjectAxis(Vector3.UnitZ);

        drawList.AddLine(origin, xEnd, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), 2f);
        drawList.AddLine(origin, yEnd, ImGui.GetColorU32(new Vector4(0, 1, 0, 1)), 2f);
        drawList.AddLine(origin, zEnd, ImGui.GetColorU32(new Vector4(0, 0, 1, 1)), 2f);

        drawList.AddText(xEnd, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), "X");
        drawList.AddText(yEnd, ImGui.GetColorU32(new Vector4(0, 1, 0, 1)), "Y");
        drawList.AddText(zEnd, ImGui.GetColorU32(new Vector4(0, 0, 1, 1)), "Z (axial)");
    }

    private void RenderFracturePlanes(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize,
        List<FracturePlane> fracturePlanes)
    {
        // Project function from camera space to screen space
        Vector2 Project(Vector3 p)
        {
            var view = Vector3.Normalize(_cameraTarget - _cameraPosition);
            var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, view));
            var up = Vector3.Cross(view, right);

            var rel = p - _cameraPosition;
            float x = Vector3.Dot(rel, right);
            float y = Vector3.Dot(rel, up);

            float scale = Math.Min(canvasSize.X, canvasSize.Y) * 2.5f;
            float screenX = canvasPos.X + canvasSize.X / 2 + x * scale;
            float screenY = canvasPos.Y + canvasSize.Y / 2 - y * scale;

            return new Vector2(screenX, screenY);
        }

        // Render each fracture plane as an oriented disk
        foreach (var fracture in fracturePlanes)
        {
            // Create disk vertices perpendicular to fracture normal
            var tangent1 = Math.Abs(fracture.Normal.Z) < 0.9f
                ? Vector3.Normalize(Vector3.Cross(fracture.Normal, Vector3.UnitZ))
                : Vector3.Normalize(Vector3.Cross(fracture.Normal, Vector3.UnitX));
            var tangent2 = Vector3.Cross(fracture.Normal, tangent1);

            float diskRadius = 0.01f; // 10mm disk
            int segments = 12;

            var diskPoints = new List<Vector2>();
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2 * MathF.PI / segments;
                var point3D = fracture.Position +
                    diskRadius * (MathF.Cos(angle) * tangent1 + MathF.Sin(angle) * tangent2);
                diskPoints.Add(Project(point3D));
            }

            // Draw filled disk
            var fracturePlaneColor = ImGui.GetColorU32(new Vector4(1, 0, 0, 0.5f));
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                var center = Project(fracture.Position);
                drawList.AddTriangleFilled(center, diskPoints[i], diskPoints[next], fracturePlaneColor);
            }

            // Draw outline
            var outlineColor = ImGui.GetColorU32(new Vector4(1, 0, 0, 1));
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                drawList.AddLine(diskPoints[i], diskPoints[next], outlineColor, 2f);
            }

            // Draw normal vector
            var normalEnd = Project(fracture.Position + fracture.Normal * diskRadius * 1.5f);
            var normalStart = Project(fracture.Position);
            drawList.AddLine(normalStart, normalEnd, outlineColor, 2f);

            // Arrow head
            var arrowSize = 5f;
            var arrowDir = Vector2.Normalize(normalEnd - normalStart);
            var arrowPerp = new Vector2(-arrowDir.Y, arrowDir.X);
            var arrowTip = normalEnd;
            var arrowLeft = normalEnd - arrowDir * arrowSize + arrowPerp * arrowSize * 0.5f;
            var arrowRight = normalEnd - arrowDir * arrowSize - arrowPerp * arrowSize * 0.5f;
            drawList.AddTriangleFilled(arrowTip, arrowLeft, arrowRight, outlineColor);
        }
    }

    private void DrawInfoOverlay(Vector2 canvasPos, Vector2 canvasSize,
        TriaxialMeshGenerator.TriaxialMesh mesh, TriaxialResults results)
    {
        var textPos = new Vector2(canvasPos.X + 10, canvasPos.Y + 10);
        var white = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));

        drawList = ImGui.GetWindowDrawList();
        drawList.AddText(textPos, white, $"Nodes: {mesh.TotalNodes:N0}");
        drawList.AddText(textPos + new Vector2(0, 20), white, $"Elements: {mesh.TotalElements:N0}");

        if (results != null)
        {
            drawList.AddText(textPos + new Vector2(0, 40), white,
                $"Peak Stress: {results.PeakStrength_MPa:F2} MPa");
        }

        if (_vizMode == VisualizationMode.VonMisesStress || _vizMode == VisualizationMode.PrincipalStress)
        {
            // Draw color scale
            DrawColorScale(drawList, canvasPos, canvasSize);
        }
    }

    private void DrawColorScale(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
    {
        float barWidth = 20f;
        float barHeight = 200f;
        var barPos = new Vector2(canvasPos.X + canvasSize.X - barWidth - 40, canvasPos.Y + 50);

        // Draw gradient bar
        int steps = 50;
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps;
            float nextT = (i + 1) / (float)steps;

            var color = GetColorFromValue(_minStress + t * (_maxStress - _minStress), _minStress, _maxStress);
            var nextColor = GetColorFromValue(_minStress + nextT * (_maxStress - _minStress), _minStress, _maxStress);

            float y = barPos.Y + barHeight * (1 - t);
            float nextY = barPos.Y + barHeight * (1 - nextT);

            drawList.AddRectFilledMultiColor(
                new Vector2(barPos.X, nextY),
                new Vector2(barPos.X + barWidth, y),
                nextColor, nextColor, color, color);
        }

        // Draw outline
        drawList.AddRect(barPos, barPos + new Vector2(barWidth, barHeight),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), 0, ImDrawFlags.None, 2f);

        // Draw labels
        var white = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
        drawList.AddText(barPos + new Vector2(barWidth + 5, -10), white, $"{_maxStress:F1}");
        drawList.AddText(barPos + new Vector2(barWidth + 5, barHeight - 10), white, $"{_minStress:F1}");
        drawList.AddText(barPos + new Vector2(barWidth + 5, barHeight / 2 - 10), white, "MPa");
    }

    private void ResetCamera()
    {
        _cameraDistance = 0.2f;
        _cameraAzimuth = 45f;
        _cameraElevation = 30f;
    }
}
