// GeoscientistToolkit/Data/PhysicoChem/PhysicoChemBuilder.cs
//
// Interactive 3D builder for PhysicoChem datasets with handles, gizmos, and mesh editing

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Interactive builder for creating and editing PhysicoChemDatasets
/// Provides 3D handles for domain manipulation, boundary selection, and mesh deformation
/// </summary>
public class PhysicoChemBuilder
{
    private readonly PhysicoChemDataset _dataset;

    // Selection state
    private int _selectedDomainIndex = -1;
    private int _selectedBoundaryIndex = -1;
    private List<int> _selectedVertices = new();

    // Gizmo state
    private GizmoMode _gizmoMode = GizmoMode.Translate;
    private GizmoAxis _activeAxis = GizmoAxis.None;
    private bool _isDragging = false;
    private Vector2 _dragStart;
    private Vector3 _objectStartPosition;
    private Vector3 _objectStartScale;
    private float _objectStartRotation;

    // Tool state
    private BuilderTool _activeTool = BuilderTool.Select;
    private DeformationMode _deformMode = DeformationMode.Move;

    // Mesh editing
    private float _brushSize = 0.1f;
    private float _brushStrength = 1.0f;
    private Vector3 _deformationDirection = new Vector3(0, 0, 1);

    // Boolean operations state
    private int _selectedOtherDomain = 0;

    public PhysicoChemBuilder(PhysicoChemDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
    }

    /// <summary>
    /// Draw the builder toolbar with tool selection
    /// </summary>
    public void DrawToolbar()
    {
        ImGui.Text("Builder Tools:");
        ImGui.SameLine();

        // Tool selection
        if (ImGui.RadioButton("Select", _activeTool == BuilderTool.Select))
            _activeTool = BuilderTool.Select;
        ImGui.SameLine();

        if (ImGui.RadioButton("Add Domain", _activeTool == BuilderTool.AddDomain))
            _activeTool = BuilderTool.AddDomain;
        ImGui.SameLine();

        if (ImGui.RadioButton("Deform", _activeTool == BuilderTool.Deform))
            _activeTool = BuilderTool.Deform;
        ImGui.SameLine();

        if (ImGui.RadioButton("Paint", _activeTool == BuilderTool.Paint))
            _activeTool = BuilderTool.Paint;

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Gizmo mode (for select tool)
        if (_activeTool == BuilderTool.Select && _selectedDomainIndex >= 0)
        {
            ImGui.Text("Gizmo:");
            ImGui.SameLine();

            if (ImGui.RadioButton("Move", _gizmoMode == GizmoMode.Translate))
                _gizmoMode = GizmoMode.Translate;
            ImGui.SameLine();

            if (ImGui.RadioButton("Rotate", _gizmoMode == GizmoMode.Rotate))
                _gizmoMode = GizmoMode.Rotate;
            ImGui.SameLine();

            if (ImGui.RadioButton("Scale", _gizmoMode == GizmoMode.Scale))
                _gizmoMode = GizmoMode.Scale;
        }

        // Deformation controls
        if (_activeTool == BuilderTool.Deform)
        {
            ImGui.SameLine();
            ImGui.Text("Mode:");
            ImGui.SameLine();

            if (ImGui.RadioButton("Move##Deform", _deformMode == DeformationMode.Move))
                _deformMode = DeformationMode.Move;
            ImGui.SameLine();

            if (ImGui.RadioButton("Smooth", _deformMode == DeformationMode.Smooth))
                _deformMode = DeformationMode.Smooth;
            ImGui.SameLine();

            if (ImGui.RadioButton("Inflate", _deformMode == DeformationMode.Inflate))
                _deformMode = DeformationMode.Inflate;

            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.SliderFloat("Brush Size", ref _brushSize, 0.01f, 1.0f);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.SliderFloat("Strength", ref _brushStrength, 0.1f, 10.0f);
        }
    }

    /// <summary>
    /// Draw the properties panel for selected object
    /// </summary>
    public void DrawPropertiesPanel()
    {
        ImGui.Begin("Properties");

        if (_selectedDomainIndex >= 0 && _selectedDomainIndex < _dataset.Domains.Count)
        {
            DrawDomainProperties(_dataset.Domains[_selectedDomainIndex]);
        }
        else if (_selectedBoundaryIndex >= 0 && _selectedBoundaryIndex < _dataset.BoundaryConditions.Count)
        {
            DrawBoundaryProperties(_dataset.BoundaryConditions[_selectedBoundaryIndex]);
        }
        else
        {
            ImGui.TextDisabled("No object selected");
            ImGui.Separator();

            // Dataset-level properties
            ImGui.Text("Dataset:");
            ImGui.InputText("Name", ref _dataset.Name, 256);

            if (ImGui.Button("Generate Mesh"))
            {
                _dataset.GenerateMesh();
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear All"))
            {
                _dataset.Domains.Clear();
                _dataset.BoundaryConditions.Clear();
                _selectedDomainIndex = -1;
                _selectedBoundaryIndex = -1;
            }
        }

        ImGui.End();
    }

    /// <summary>
    /// Render 3D handles and gizmos
    /// </summary>
    public void RenderHandles(ImDrawListPtr drawList, Vector2 screenPos, Vector2 size,
                             float cameraYaw, float cameraPitch, float cameraDistance)
    {
        // Render domain bounding boxes with selection highlight
        for (int i = 0; i < _dataset.Domains.Count; i++)
        {
            var domain = _dataset.Domains[i];
            if (domain.Geometry == null) continue;

            bool isSelected = i == _selectedDomainIndex;
            RenderDomainBoundingBox(drawList, screenPos, size, domain, isSelected,
                                   cameraYaw, cameraPitch, cameraDistance);
        }

        // Render gizmo for selected domain
        if (_selectedDomainIndex >= 0 && _selectedDomainIndex < _dataset.Domains.Count)
        {
            var domain = _dataset.Domains[_selectedDomainIndex];
            RenderGizmo(drawList, screenPos, size, domain.Geometry,
                       cameraYaw, cameraPitch, cameraDistance);
        }

        // Render boundary condition markers
        for (int i = 0; i < _dataset.BoundaryConditions.Count; i++)
        {
            var bc = _dataset.BoundaryConditions[i];
            bool isSelected = i == _selectedBoundaryIndex;
            RenderBoundaryMarker(drawList, screenPos, size, bc, isSelected,
                               cameraYaw, cameraPitch, cameraDistance);
        }

        // Render deformation brush
        if (_activeTool == BuilderTool.Deform)
        {
            RenderDeformationBrush(drawList, screenPos, size);
        }
    }

    /// <summary>
    /// Handle mouse interactions for selection and manipulation
    /// </summary>
    public bool HandleMouseInteraction(Vector2 mousePos, Vector2 screenPos, Vector2 size,
                                       bool isClicked, bool isDragging,
                                       float cameraYaw, float cameraPitch)
    {
        if (isClicked && !_isDragging)
        {
            // Pick object at mouse position
            var picked = PickObjectAtPosition(mousePos, screenPos, size, cameraYaw, cameraPitch);

            if (picked.Type == PickType.Domain)
            {
                _selectedDomainIndex = picked.Index;
                _selectedBoundaryIndex = -1;
                return true;
            }
            else if (picked.Type == PickType.Boundary)
            {
                _selectedBoundaryIndex = picked.Index;
                _selectedDomainIndex = -1;
                return true;
            }
            else if (picked.Type == PickType.GizmoAxis)
            {
                _activeAxis = picked.Axis;
                _isDragging = true;
                _dragStart = mousePos;

                if (_selectedDomainIndex >= 0)
                {
                    var domain = _dataset.Domains[_selectedDomainIndex];
                    _objectStartPosition = new Vector3(
                        (float)domain.Geometry.Center.X,
                        (float)domain.Geometry.Center.Y,
                        (float)domain.Geometry.Center.Z
                    );
                    _objectStartScale = new Vector3(
                        (float)domain.Geometry.Dimensions.Width,
                        (float)domain.Geometry.Dimensions.Height,
                        (float)domain.Geometry.Dimensions.Depth
                    );
                }
                return true;
            }
            else
            {
                // Clicked empty space
                _selectedDomainIndex = -1;
                _selectedBoundaryIndex = -1;
            }
        }

        if (isDragging && _isDragging && _activeAxis != GizmoAxis.None)
        {
            ApplyGizmoTransform(mousePos, screenPos, size);
            return true;
        }

        if (!isDragging && _isDragging)
        {
            _isDragging = false;
            _activeAxis = GizmoAxis.None;
        }

        return false;
    }

    private void RenderDomainBoundingBox(ImDrawListPtr drawList, Vector2 screenPos, Vector2 size,
                                        ReactorDomain domain, bool isSelected,
                                        float cameraYaw, float cameraPitch, float cameraDistance)
    {
        var geom = domain.Geometry;
        var center = screenPos + size * 0.5f;

        // Color based on selection
        var color = isSelected
            ? ImGui.GetColorU32(new Vector4(1.0f, 0.5f, 0.0f, 1.0f))  // Orange for selected
            : ImGui.GetColorU32(new Vector4(0.3f, 0.6f, 0.9f, 0.7f)); // Blue for normal

        var lineThickness = isSelected ? 2.5f : 1.5f;

        // Get projected corners
        var corners = GetProjectedDomainCorners(geom, center, 100.0f, cameraYaw, cameraPitch);

        // Draw bounding box edges
        // Bottom face (0-1-2-3)
        drawList.AddLine(corners[0], corners[1], color, lineThickness);
        drawList.AddLine(corners[1], corners[3], color, lineThickness);
        drawList.AddLine(corners[3], corners[2], color, lineThickness);
        drawList.AddLine(corners[2], corners[0], color, lineThickness);

        // Top face (4-5-6-7)
        drawList.AddLine(corners[4], corners[5], color, lineThickness);
        drawList.AddLine(corners[5], corners[7], color, lineThickness);
        drawList.AddLine(corners[7], corners[6], color, lineThickness);
        drawList.AddLine(corners[6], corners[4], color, lineThickness);

        // Vertical edges
        drawList.AddLine(corners[0], corners[4], color, lineThickness);
        drawList.AddLine(corners[1], corners[5], color, lineThickness);
        drawList.AddLine(corners[2], corners[6], color, lineThickness);
        drawList.AddLine(corners[3], corners[7], color, lineThickness);

        // Draw domain name
        if (isSelected)
        {
            var textPos = corners[6] + new Vector2(5, -20);
            var textColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
            drawList.AddText(textPos, textColor, domain.Name);
        }
    }

    private void RenderGizmo(ImDrawListPtr drawList, Vector2 screenPos, Vector2 size,
                            ReactorGeometry geom, float cameraYaw, float cameraPitch, float cameraDistance)
    {
        var center = screenPos + size * 0.5f;
        var gizmoCenter = ProjectPoint(geom.Center.X, geom.Center.Y, geom.Center.Z,
                                      center, 100.0f, cameraYaw, cameraPitch);

        float handleSize = 40.0f;

        switch (_gizmoMode)
        {
            case GizmoMode.Translate:
                RenderTranslationGizmo(drawList, gizmoCenter, handleSize, cameraYaw, cameraPitch);
                break;

            case GizmoMode.Rotate:
                RenderRotationGizmo(drawList, gizmoCenter, handleSize);
                break;

            case GizmoMode.Scale:
                RenderScaleGizmo(drawList, gizmoCenter, handleSize, cameraYaw, cameraPitch);
                break;
        }
    }

    private void RenderTranslationGizmo(ImDrawListPtr drawList, Vector2 center, float size,
                                       float cameraYaw, float cameraPitch)
    {
        float arrowLength = size;
        float arrowHeadSize = 8.0f;

        // X axis (red)
        var xColor = _activeAxis == GizmoAxis.X
            ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1))  // Yellow when active
            : ImGui.GetColorU32(new Vector4(1, 0, 0, 1));  // Red

        var xEnd = center + new Vector2(arrowLength, 0);
        drawList.AddLine(center, xEnd, xColor, 3.0f);
        DrawArrowHead(drawList, xEnd, new Vector2(1, 0), arrowHeadSize, xColor);

        // Y axis (green)
        var yColor = _activeAxis == GizmoAxis.Y
            ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1))
            : ImGui.GetColorU32(new Vector4(0, 1, 0, 1));

        var yEnd = center + new Vector2(0, -arrowLength);
        drawList.AddLine(center, yEnd, yColor, 3.0f);
        DrawArrowHead(drawList, yEnd, new Vector2(0, -1), arrowHeadSize, yColor);

        // Z axis (blue) - projected
        var zColor = _activeAxis == GizmoAxis.Z
            ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1))
            : ImGui.GetColorU32(new Vector4(0, 0, 1, 1));

        float cosYaw = MathF.Cos(cameraYaw);
        float sinYaw = MathF.Sin(cameraYaw);
        var zEnd = center + new Vector2(-sinYaw * arrowLength, 0);
        drawList.AddLine(center, zEnd, zColor, 3.0f);
        DrawArrowHead(drawList, zEnd, new Vector2(-sinYaw, 0), arrowHeadSize, zColor);

        // Draw axis labels
        var textColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
        drawList.AddText(xEnd + new Vector2(5, -10), textColor, "X");
        drawList.AddText(yEnd + new Vector2(5, -10), textColor, "Y");
        drawList.AddText(zEnd + new Vector2(5, -10), textColor, "Z");
    }

    private void RenderRotationGizmo(ImDrawListPtr drawList, Vector2 center, float radius)
    {
        // Draw rotation circles for each axis
        int segments = 32;

        // X axis rotation (red circle in YZ plane)
        var xColor = _activeAxis == GizmoAxis.X
            ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1))
            : ImGui.GetColorU32(new Vector4(1, 0, 0, 0.7f));
        DrawCircle(drawList, center, radius, xColor, segments, 2.0f);

        // Y axis rotation (green circle)
        var yColor = _activeAxis == GizmoAxis.Y
            ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1))
            : ImGui.GetColorU32(new Vector4(0, 1, 0, 0.7f));
        DrawCircle(drawList, center, radius * 0.9f, yColor, segments, 2.0f);

        // Z axis rotation (blue circle)
        var zColor = _activeAxis == GizmoAxis.Z
            ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1))
            : ImGui.GetColorU32(new Vector4(0, 0, 1, 0.7f));
        DrawCircle(drawList, center, radius * 0.8f, zColor, segments, 2.0f);

        // Center point
        drawList.AddCircleFilled(center, 4, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)));
    }

    private void RenderScaleGizmo(ImDrawListPtr drawList, Vector2 center, float size,
                                 float cameraYaw, float cameraPitch)
    {
        float handleSize = 8.0f;
        float lineLength = size;

        // X axis (red)
        var xColor = _activeAxis == GizmoAxis.X
            ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1))
            : ImGui.GetColorU32(new Vector4(1, 0, 0, 1));

        var xEnd = center + new Vector2(lineLength, 0);
        drawList.AddLine(center, xEnd, xColor, 2.0f);
        drawList.AddRectFilled(xEnd - new Vector2(handleSize / 2), xEnd + new Vector2(handleSize / 2), xColor);

        // Y axis (green)
        var yColor = _activeAxis == GizmoAxis.Y
            ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1))
            : ImGui.GetColorU32(new Vector4(0, 1, 0, 1));

        var yEnd = center + new Vector2(0, -lineLength);
        drawList.AddLine(center, yEnd, yColor, 2.0f);
        drawList.AddRectFilled(yEnd - new Vector2(handleSize / 2), yEnd + new Vector2(handleSize / 2), yColor);

        // Z axis (blue)
        var zColor = _activeAxis == GizmoAxis.Z
            ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1))
            : ImGui.GetColorU32(new Vector4(0, 0, 1, 1));

        float cosYaw = MathF.Cos(cameraYaw);
        float sinYaw = MathF.Sin(cameraYaw);
        var zEnd = center + new Vector2(-sinYaw * lineLength, 0);
        drawList.AddLine(center, zEnd, zColor, 2.0f);
        drawList.AddRectFilled(zEnd - new Vector2(handleSize / 2), zEnd + new Vector2(handleSize / 2), zColor);

        // Center cube for uniform scaling
        var centerColor = _activeAxis == GizmoAxis.All
            ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1))
            : ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 1));
        drawList.AddRectFilled(center - new Vector2(handleSize), center + new Vector2(handleSize), centerColor);
    }

    private void RenderBoundaryMarker(ImDrawListPtr drawList, Vector2 screenPos, Vector2 size,
                                     BoundaryCondition bc, bool isSelected,
                                     float cameraYaw, float cameraPitch, float cameraDistance)
    {
        var center = screenPos + size * 0.5f;

        // Determine position based on boundary location
        Vector2 markerPos = center;
        float offsetX = size.X * 0.4f;
        float offsetY = size.Y * 0.4f;

        switch (bc.Location)
        {
            case BoundaryLocation.XMin:
                markerPos = center - new Vector2(offsetX, 0);
                break;
            case BoundaryLocation.XMax:
                markerPos = center + new Vector2(offsetX, 0);
                break;
            case BoundaryLocation.YMin:
                markerPos = center - new Vector2(0, offsetY);
                break;
            case BoundaryLocation.YMax:
                markerPos = center + new Vector2(0, offsetY);
                break;
            case BoundaryLocation.ZMin:
                markerPos = center + new Vector2(0, offsetY * 0.5f);
                break;
            case BoundaryLocation.ZMax:
                markerPos = center - new Vector2(0, offsetY * 0.5f);
                break;
            case BoundaryLocation.Custom:
                markerPos = ProjectPoint(bc.CustomRegionCenter.X, bc.CustomRegionCenter.Y, bc.CustomRegionCenter.Z,
                                       center, 100.0f, cameraYaw, cameraPitch);
                break;
        }

        // Draw marker
        var color = bc.Type switch
        {
            BoundaryType.FixedValue => ImGui.GetColorU32(new Vector4(1.0f, 0.3f, 0.3f, 0.8f)),
            BoundaryType.FixedFlux => ImGui.GetColorU32(new Vector4(0.3f, 1.0f, 0.3f, 0.8f)),
            BoundaryType.Inlet => ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 1.0f, 0.8f)),
            BoundaryType.Outlet => ImGui.GetColorU32(new Vector4(1.0f, 0.5f, 0.0f, 0.8f)),
            _ => ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.3f, 0.8f))
        };

        float markerSize = isSelected ? 8 : 6;
        drawList.AddCircleFilled(markerPos, markerSize, color);
        drawList.AddCircle(markerPos, markerSize + 2, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.9f)), 0, 2.0f);

        // Draw boundary type indicator
        if (isSelected)
        {
            var textColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
            drawList.AddText(markerPos + new Vector2(10, -10), textColor, bc.Name);
        }
    }

    private void RenderDeformationBrush(ImDrawListPtr drawList, Vector2 screenPos, Vector2 size)
    {
        var io = ImGui.GetIO();
        var mousePos = io.MousePos;

        // Draw brush circle
        var brushColor = ImGui.GetColorU32(new Vector4(1, 1, 0, 0.3f));
        var brushOutline = ImGui.GetColorU32(new Vector4(1, 1, 0, 1));

        float brushRadius = _brushSize * 100.0f;
        drawList.AddCircleFilled(mousePos, brushRadius, brushColor);
        drawList.AddCircle(mousePos, brushRadius, brushOutline, 0, 2.0f);

        // Draw direction indicator
        if (_deformMode == DeformationMode.Move)
        {
            var dirEnd = mousePos + new Vector2(_deformationDirection.X, -_deformationDirection.Y) * brushRadius;
            drawList.AddLine(mousePos, dirEnd, brushOutline, 2.0f);
            DrawArrowHead(drawList, dirEnd,
                         Vector2.Normalize(new Vector2(_deformationDirection.X, -_deformationDirection.Y)),
                         8.0f, brushOutline);
        }
    }

    private PickResult PickObjectAtPosition(Vector2 mousePos, Vector2 screenPos, Vector2 size,
                                           float cameraYaw, float cameraPitch)
    {
        // Check gizmo axes first (if selected object exists)
        if (_selectedDomainIndex >= 0)
        {
            var axis = PickGizmoAxis(mousePos, screenPos, size, cameraYaw, cameraPitch);
            if (axis != GizmoAxis.None)
            {
                return new PickResult { Type = PickType.GizmoAxis, Axis = axis };
            }
        }

        // Check domains
        for (int i = _dataset.Domains.Count - 1; i >= 0; i--)
        {
            if (IsPointNearDomain(mousePos, screenPos, size, _dataset.Domains[i], cameraYaw, cameraPitch))
            {
                return new PickResult { Type = PickType.Domain, Index = i };
            }
        }

        // Check boundaries
        for (int i = 0; i < _dataset.BoundaryConditions.Count; i++)
        {
            if (IsPointNearBoundary(mousePos, screenPos, size, _dataset.BoundaryConditions[i], cameraYaw, cameraPitch))
            {
                return new PickResult { Type = PickType.Boundary, Index = i };
            }
        }

        return new PickResult { Type = PickType.None };
    }

    private GizmoAxis PickGizmoAxis(Vector2 mousePos, Vector2 screenPos, Vector2 size,
                                   float cameraYaw, float cameraPitch)
    {
        if (_selectedDomainIndex < 0) return GizmoAxis.None;

        var domain = _dataset.Domains[_selectedDomainIndex];
        var center = screenPos + size * 0.5f;
        var gizmoCenter = ProjectPoint(domain.Geometry.Center.X, domain.Geometry.Center.Y, domain.Geometry.Center.Z,
                                      center, 100.0f, cameraYaw, cameraPitch);

        float pickRadius = 10.0f;
        float handleSize = 40.0f;

        // Check X axis
        var xEnd = gizmoCenter + new Vector2(handleSize, 0);
        if (Vector2.Distance(mousePos, xEnd) < pickRadius)
            return GizmoAxis.X;

        // Check Y axis
        var yEnd = gizmoCenter + new Vector2(0, -handleSize);
        if (Vector2.Distance(mousePos, yEnd) < pickRadius)
            return GizmoAxis.Y;

        // Check Z axis
        float cosYaw = MathF.Cos(cameraYaw);
        float sinYaw = MathF.Sin(cameraYaw);
        var zEnd = gizmoCenter + new Vector2(-sinYaw * handleSize, 0);
        if (Vector2.Distance(mousePos, zEnd) < pickRadius)
            return GizmoAxis.Z;

        // Check center (for uniform scaling)
        if (_gizmoMode == GizmoMode.Scale && Vector2.Distance(mousePos, gizmoCenter) < pickRadius)
            return GizmoAxis.All;

        return GizmoAxis.None;
    }

    private bool IsPointNearDomain(Vector2 mousePos, Vector2 screenPos, Vector2 size,
                                  ReactorDomain domain, float cameraYaw, float cameraPitch)
    {
        if (domain.Geometry == null) return false;

        var corners = GetProjectedDomainCorners(domain.Geometry, screenPos + size * 0.5f, 100.0f, cameraYaw, cameraPitch);

        // Check if mouse is near any edge
        float threshold = 10.0f;

        for (int i = 0; i < 4; i++)
        {
            // Bottom face
            if (DistanceToLineSegment(mousePos, corners[i], corners[(i + 1) % 4]) < threshold)
                return true;

            // Top face
            if (DistanceToLineSegment(mousePos, corners[i + 4], corners[((i + 1) % 4) + 4]) < threshold)
                return true;

            // Vertical edges
            if (DistanceToLineSegment(mousePos, corners[i], corners[i + 4]) < threshold)
                return true;
        }

        return false;
    }

    private bool IsPointNearBoundary(Vector2 mousePos, Vector2 screenPos, Vector2 size,
                                    BoundaryCondition bc, float cameraYaw, float cameraPitch)
    {
        // Simplified - check distance to boundary marker
        var center = screenPos + size * 0.5f;
        Vector2 markerPos = center; // Calculate actual position based on bc.Location...

        return Vector2.Distance(mousePos, markerPos) < 15.0f;
    }

    private void ApplyGizmoTransform(Vector2 mousePos, Vector2 screenPos, Vector2 size)
    {
        if (_selectedDomainIndex < 0) return;

        var domain = _dataset.Domains[_selectedDomainIndex];
        var geom = domain.Geometry;

        Vector2 delta = mousePos - _dragStart;
        float sensitivity = 0.01f;

        switch (_gizmoMode)
        {
            case GizmoMode.Translate:
                ApplyTranslation(geom, delta, sensitivity);
                break;

            case GizmoMode.Rotate:
                ApplyRotation(geom, delta, sensitivity);
                break;

            case GizmoMode.Scale:
                ApplyScale(geom, delta, sensitivity);
                break;
        }
    }

    private void ApplyTranslation(ReactorGeometry geom, Vector2 delta, float sensitivity)
    {
        if (geom == null)
            return;

        switch (_activeAxis)
        {
            case GizmoAxis.X:
                geom.Center = (geom.Center.X + delta.X * sensitivity, geom.Center.Y, geom.Center.Z);
                break;

            case GizmoAxis.Y:
                geom.Center = (geom.Center.X, geom.Center.Y - delta.Y * sensitivity, geom.Center.Z);
                break;

            case GizmoAxis.Z:
                geom.Center = (geom.Center.X, geom.Center.Y, geom.Center.Z + delta.X * sensitivity);
                break;
        }
    }

    private void ApplyRotation(ReactorGeometry geom, Vector2 delta, float sensitivity)
    {
        if (geom == null)
            return;

        // Rotation implementation would require quaternions or rotation matrices
        // For now, simplified rotation around axes
        float angle = delta.Length() * sensitivity;

        // Store rotation for later implementation
        // This would modify geometry orientation
    }

    private void ApplyScale(ReactorGeometry geom, Vector2 delta, float sensitivity)
    {
        if (geom == null)
            return;

        float scaleFactor = 1.0f + delta.X * sensitivity;

        // Prevent negative or zero scaling
        if (scaleFactor <= 0.01f)
            scaleFactor = 0.01f;

        switch (_activeAxis)
        {
            case GizmoAxis.X:
                geom.Dimensions = (geom.Dimensions.Width * scaleFactor, geom.Dimensions.Height, geom.Dimensions.Depth);
                break;

            case GizmoAxis.Y:
                geom.Dimensions = (geom.Dimensions.Width, geom.Dimensions.Height * scaleFactor, geom.Dimensions.Depth);
                break;

            case GizmoAxis.Z:
                geom.Dimensions = (geom.Dimensions.Width, geom.Dimensions.Height, geom.Dimensions.Depth * scaleFactor);
                break;

            case GizmoAxis.All:
                geom.Dimensions = (geom.Dimensions.Width * scaleFactor, geom.Dimensions.Height * scaleFactor,
                                  geom.Dimensions.Depth * scaleFactor);
                if (geom.Type == GeometryType.Sphere || geom.Type == GeometryType.Cylinder)
                {
                    geom.Radius *= scaleFactor;
                }
                break;
        }
    }

    private void DrawDomainProperties(ReactorDomain domain)
    {
        ImGui.Text("Domain Properties");
        ImGui.Separator();

        // Name
        var name = domain.Name;
        if (ImGui.InputText("Name", ref name, 256))
        {
            domain.Name = name;
        }

        // Active checkbox
        ImGui.Checkbox("Active", ref domain.IsActive);
        ImGui.Checkbox("Allow Interaction", ref domain.AllowInteraction);

        ImGui.Spacing();

        // Geometry
        if (domain.Geometry != null)
        {
            ImGui.Text("Geometry");
            ImGui.Indent();

            var geom = domain.Geometry;

            // Type selector
            var geometryTypes = Enum.GetNames(typeof(GeometryType));
            int currentType = (int)geom.Type;
            if (ImGui.Combo("Type", ref currentType, geometryTypes, geometryTypes.Length))
            {
                geom.Type = (GeometryType)currentType;
            }

            // Position
            var center = new System.Numerics.Vector3((float)geom.Center.X, (float)geom.Center.Y, (float)geom.Center.Z);
            if (ImGui.DragFloat3("Center", ref center, 0.1f))
            {
                geom.Center = (center.X, center.Y, center.Z);
            }

            // Dimensions based on type
            switch (geom.Type)
            {
                case GeometryType.Box:
                case GeometryType.Parallelepiped:
                    var dims = new System.Numerics.Vector3((float)geom.Dimensions.Width, (float)geom.Dimensions.Height, (float)geom.Dimensions.Depth);
                    if (ImGui.DragFloat3("Dimensions", ref dims, 0.1f, 0.01f, 100.0f))
                    {
                        geom.Dimensions = (dims.X, dims.Y, dims.Z);
                    }
                    break;

                case GeometryType.Sphere:
                    float radius = (float)geom.Radius;
                    if (ImGui.DragFloat("Radius", ref radius, 0.1f, 0.01f, 100.0f))
                    {
                        geom.Radius = radius;
                    }
                    break;

                case GeometryType.Cylinder:
                    float cylRadius = (float)geom.Radius;
                    float cylHeight = (float)geom.Height;
                    if (ImGui.DragFloat("Radius##Cyl", ref cylRadius, 0.1f, 0.01f, 100.0f))
                    {
                        geom.Radius = cylRadius;
                    }
                    if (ImGui.DragFloat("Height##Cyl", ref cylHeight, 0.1f, 0.01f, 100.0f))
                    {
                        geom.Height = cylHeight;
                    }
                    break;
            }

            ImGui.Unindent();
        }

        // Boolean operations
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Boolean Operations:");

        if (_dataset.Domains.Count > 1 && _selectedDomainIndex >= 0)
        {
            ImGui.Text("Combine with:");
            ImGui.SameLine();

            // Create combo of other domains
            var otherDomains = new List<string>();
            for (int i = 0; i < _dataset.Domains.Count; i++)
            {
                if (i != _selectedDomainIndex)
                    otherDomains.Add($"{i}: {_dataset.Domains[i].Name}");
            }

            // Ensure selected index is within valid range
            if (_selectedOtherDomain >= otherDomains.Count)
                _selectedOtherDomain = 0;

            if (otherDomains.Count > 0)
            {
                ImGui.Combo("##OtherDomain", ref _selectedOtherDomain, otherDomains.ToArray(), otherDomains.Count);
            }

            ImGui.Spacing();
            if (otherDomains.Count > 0)
            {
                if (ImGui.Button("Union"))
                {
                    ApplyBooleanOperation(BooleanOp.Union, _selectedOtherDomain);
                }
                ImGui.SameLine();
                if (ImGui.Button("Subtract"))
                {
                    ApplyBooleanOperation(BooleanOp.Subtract, _selectedOtherDomain);
                }
                ImGui.SameLine();
                if (ImGui.Button("Intersect"))
                {
                    ApplyBooleanOperation(BooleanOp.Intersect, _selectedOtherDomain);
                }
            }
        }
        else
        {
            ImGui.TextDisabled("Need at least 2 domains for boolean operations");
        }

        // Buttons
        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Duplicate Domain"))
        {
            var clone = CloneDomain(domain);
            clone.Name += " (Copy)";
            _dataset.Domains.Add(clone);
        }
        ImGui.SameLine();
        if (ImGui.Button("Delete Domain"))
        {
            _dataset.Domains.RemoveAt(_selectedDomainIndex);
            _selectedDomainIndex = -1;
        }
    }

    private void ApplyBooleanOperation(BooleanOp operation, int otherDomainRelativeIndex)
    {
        if (_selectedDomainIndex < 0 || _selectedDomainIndex >= _dataset.Domains.Count)
            return;

        if (_dataset.Domains.Count < 2)
            return;

        // Convert relative index to absolute index (skipping selected)
        int otherDomainIndex = otherDomainRelativeIndex;
        int currentAbsoluteIndex = 0;
        for (int i = 0; i < _dataset.Domains.Count; i++)
        {
            if (i == _selectedDomainIndex)
                continue;

            if (currentAbsoluteIndex == otherDomainRelativeIndex)
            {
                otherDomainIndex = i;
                break;
            }
            currentAbsoluteIndex++;
        }

        // Validate the index
        if (otherDomainIndex < 0 || otherDomainIndex >= _dataset.Domains.Count || otherDomainIndex == _selectedDomainIndex)
            return;

        var domain1 = _dataset.Domains[_selectedDomainIndex];
        var domain2 = _dataset.Domains[otherDomainIndex];

        // Null safety check
        if (domain1?.Geometry == null || domain2?.Geometry == null)
            return;

        // Create boolean result
        var result = _dataset.BooleanOperation(domain1, domain2, operation);
        result.Name = $"{domain1.Name} {operation} {domain2.Name}";

        _dataset.Domains.Add(result);

        // Reset selection to avoid out-of-bounds after adding
        _selectedOtherDomain = 0;
    }

    private ReactorDomain CloneDomain(ReactorDomain source)
    {
        return new ReactorDomain
        {
            Name = source.Name,
            Geometry = source.Geometry != null ? new ReactorGeometry
            {
                Type = source.Geometry.Type,
                InterpolationMode = source.Geometry.InterpolationMode,
                Center = source.Geometry.Center,
                Dimensions = source.Geometry.Dimensions,
                Radius = source.Geometry.Radius,
                InnerRadius = source.Geometry.InnerRadius,
                Height = source.Geometry.Height,
                Profile2D = source.Geometry.Profile2D != null ? new List<(double, double)>(source.Geometry.Profile2D) : null,
                ExtrusionDepth = source.Geometry.ExtrusionDepth,
                RadialSegments = source.Geometry.RadialSegments
            } : null,
            Material = source.Material != null ? new MaterialProperties
            {
                Porosity = source.Material.Porosity,
                Permeability = source.Material.Permeability,
                ThermalConductivity = source.Material.ThermalConductivity,
                SpecificHeat = source.Material.SpecificHeat,
                Density = source.Material.Density,
                MineralComposition = source.Material.MineralComposition,
                MineralFractions = new Dictionary<string, double>(source.Material.MineralFractions ?? new Dictionary<string, double>())
            } : null,
            InitialConditions = source.InitialConditions != null ? new InitialConditions
            {
                Temperature = source.InitialConditions.Temperature,
                Pressure = source.InitialConditions.Pressure,
                Concentrations = new Dictionary<string, double>(source.InitialConditions.Concentrations ?? new Dictionary<string, double>()),
                InitialVelocity = source.InitialConditions.InitialVelocity,
                LiquidSaturation = source.InitialConditions.LiquidSaturation,
                FluidType = source.InitialConditions.FluidType
            } : null,
            IsActive = source.IsActive,
            AllowInteraction = source.AllowInteraction
        };
    }

    private void DrawBoundaryProperties(BoundaryCondition bc)
    {
        ImGui.Text("Boundary Condition");
        ImGui.Separator();

        var name = bc.Name;
        if (ImGui.InputText("Name", ref name, 256))
        {
            bc.Name = name;
        }

        ImGui.Checkbox("Active", ref bc.IsActive);

        // Type
        var bcTypes = Enum.GetNames(typeof(BoundaryType));
        int currentType = (int)bc.Type;
        if (ImGui.Combo("Type", ref currentType, bcTypes, bcTypes.Length))
        {
            bc.Type = (BoundaryType)currentType;
        }

        // Location
        var bcLocations = Enum.GetNames(typeof(BoundaryLocation));
        int currentLocation = (int)bc.Location;
        if (ImGui.Combo("Location", ref currentLocation, bcLocations, bcLocations.Length))
        {
            bc.Location = (BoundaryLocation)currentLocation;
        }

        // Variable
        var bcVariables = Enum.GetNames(typeof(BoundaryVariable));
        int currentVariable = (int)bc.Variable;
        if (ImGui.Combo("Variable", ref currentVariable, bcVariables, bcVariables.Length))
        {
            bc.Variable = (BoundaryVariable)currentVariable;
        }

        // Value
        float value = (float)bc.Value;
        if (ImGui.DragFloat("Value", ref value, 0.1f))
        {
            bc.Value = value;
        }

        // Delete button
        ImGui.Spacing();
        if (ImGui.Button("Delete Boundary"))
        {
            _dataset.BoundaryConditions.RemoveAt(_selectedBoundaryIndex);
            _selectedBoundaryIndex = -1;
        }
    }

    // Helper methods
    private Vector2[] GetProjectedDomainCorners(ReactorGeometry geom, Vector2 center, float scale,
                                               float cameraYaw, float cameraPitch)
    {
        var corners = new Vector2[8];

        float x = (float)geom.Center.X;
        float y = (float)geom.Center.Y;
        float z = (float)geom.Center.Z;

        float w = 0.5f, h = 0.5f, d = 0.5f;

        switch (geom.Type)
        {
            case GeometryType.Box:
            case GeometryType.Parallelepiped:
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

        // 8 corners of bounding box
        int idx = 0;
        for (int i = 0; i < 2; i++)
        for (int j = 0; j < 2; j++)
        for (int k = 0; k < 2; k++)
        {
            float px = x + (i == 0 ? -w : w);
            float py = y + (j == 0 ? -h : h);
            float pz = z + (k == 0 ? -d : d);

            corners[idx++] = ProjectPoint(px, py, pz, center, scale, cameraYaw, cameraPitch);
        }

        return corners;
    }

    private Vector2 ProjectPoint(double x, double y, double z, Vector2 center, float scale,
                                float cameraYaw, float cameraPitch)
    {
        // Simple orthographic projection with rotation
        float cosYaw = MathF.Cos(cameraYaw);
        float sinYaw = MathF.Sin(cameraYaw);
        float cosPitch = MathF.Cos(cameraPitch);
        float sinPitch = MathF.Sin(cameraPitch);

        float px = (float)x;
        float py = (float)y;
        float pz = (float)z;

        // Rotate
        float rx = px * cosYaw - pz * sinYaw;
        float ry = py * cosPitch - (px * sinYaw + pz * cosYaw) * sinPitch;

        return center + new Vector2(rx * scale, -ry * scale);
    }

    private void DrawArrowHead(ImDrawListPtr drawList, Vector2 tip, Vector2 direction, float size, uint color)
    {
        Vector2 perpendicular = new Vector2(-direction.Y, direction.X);

        Vector2 p1 = tip - direction * size + perpendicular * (size * 0.5f);
        Vector2 p2 = tip - direction * size - perpendicular * (size * 0.5f);

        drawList.AddTriangleFilled(tip, p1, p2, color);
    }

    private void DrawCircle(ImDrawListPtr drawList, Vector2 center, float radius, uint color, int segments, float thickness)
    {
        for (int i = 0; i < segments; i++)
        {
            float angle1 = (float)i / segments * MathF.PI * 2;
            float angle2 = (float)(i + 1) / segments * MathF.PI * 2;

            Vector2 p1 = center + new Vector2(MathF.Cos(angle1), MathF.Sin(angle1)) * radius;
            Vector2 p2 = center + new Vector2(MathF.Cos(angle2), MathF.Sin(angle2)) * radius;

            drawList.AddLine(p1, p2, color, thickness);
        }
    }

    private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 line = lineEnd - lineStart;
        float lineLength = line.Length();
        if (lineLength < 0.001f) return Vector2.Distance(point, lineStart);

        float t = Math.Max(0, Math.Min(1, Vector2.Dot(point - lineStart, line) / (lineLength * lineLength)));
        Vector2 projection = lineStart + t * line;

        return Vector2.Distance(point, projection);
    }

    public int SelectedDomainIndex => _selectedDomainIndex;
    public int SelectedBoundaryIndex => _selectedBoundaryIndex;
    public BuilderTool ActiveTool => _activeTool;
}

/// <summary>
/// Builder tool modes
/// </summary>
public enum BuilderTool
{
    Select,
    AddDomain,
    Deform,
    Paint
}

/// <summary>
/// 3D gizmo modes for object manipulation
/// </summary>
public enum GizmoMode
{
    Translate,
    Rotate,
    Scale
}

/// <summary>
/// Gizmo axis selection
/// </summary>
public enum GizmoAxis
{
    None,
    X,
    Y,
    Z,
    All
}

/// <summary>
/// Mesh deformation modes
/// </summary>
public enum DeformationMode
{
    Move,
    Smooth,
    Inflate,
    Sculpt
}

/// <summary>
/// Result of picking operation
/// </summary>
public struct PickResult
{
    public PickType Type;
    public int Index;
    public GizmoAxis Axis;
}

/// <summary>
/// Type of picked object
/// </summary>
public enum PickType
{
    None,
    Domain,
    Boundary,
    GizmoAxis,
    Vertex
}
