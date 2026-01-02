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
    private List<string> _selectedCellIDs = new();
    private int _selectedBoundaryIndex = -1;
    private List<int> _selectedVertices = new();
    private SelectionMode _selectionMode = SelectionMode.Single;

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

    // Rectangle selection
    private bool _isRectangleSelecting = false;
    private Vector2 _rectangleStart;
    private Vector2 _rectangleEnd;

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

        // Selection mode
        if (_activeTool == BuilderTool.Select)
        {
            ImGui.Text("Selection Mode:");
            ImGui.SameLine();
            if (ImGui.RadioButton("Single", _selectionMode == SelectionMode.Single))
                _selectionMode = SelectionMode.Single;
            ImGui.SameLine();
            if (ImGui.RadioButton("Plane XY", _selectionMode == SelectionMode.PlaneXY))
                _selectionMode = SelectionMode.PlaneXY;
            ImGui.SameLine();
            if (ImGui.RadioButton("Plane XZ", _selectionMode == SelectionMode.PlaneXZ))
                _selectionMode = SelectionMode.PlaneXZ;
            ImGui.SameLine();
            if (ImGui.RadioButton("Plane YZ", _selectionMode == SelectionMode.PlaneYZ))
                _selectionMode = SelectionMode.PlaneYZ;
            ImGui.SameLine();
            if (ImGui.RadioButton("Rectangle", _selectionMode == SelectionMode.Rectangle))
                _selectionMode = SelectionMode.Rectangle;
        }


        if (ImGui.RadioButton("Deform", _activeTool == BuilderTool.Deform))
            _activeTool = BuilderTool.Deform;
        ImGui.SameLine();

        if (ImGui.RadioButton("Paint", _activeTool == BuilderTool.Paint))
            _activeTool = BuilderTool.Paint;

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Gizmo mode (for select tool)
        if (_activeTool == BuilderTool.Select && _selectedCellIDs.Count > 0)
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

        if (_selectedCellIDs.Count > 0)
        {
            if (_selectedCellIDs.Count == 1 && _dataset.Mesh.Cells.TryGetValue(_selectedCellIDs[0], out var selectedCell))
            {
                DrawCellProperties(selectedCell);
            }
            else
            {
                ImGui.Text($"{_selectedCellIDs.Count} cells selected.");
            }
        }
        else if (_selectedBoundaryIndex >= 0 && _selectedBoundaryIndex < _dataset.BoundaryConditions.Count)
        {
            DrawBoundaryProperties(_dataset.BoundaryConditions[_selectedBoundaryIndex]);
        }
        else
        {
            // Dataset-level properties
            ImGui.Text("Dataset:");
            var datasetName = _dataset.Name ?? "";
            if (ImGui.InputText("Name", ref datasetName, 256))
            {
                _dataset.Name = datasetName;
            }

            if (ImGui.Button("Generate Mesh"))
            {
                _dataset.GenerateMesh();
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear All"))
            {
                _dataset.Mesh.Cells.Clear();
                _dataset.BoundaryConditions.Clear();
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
        // Render cell bounding boxes with selection highlight
        foreach (var cell in _dataset.Mesh.Cells.Values)
        {
            var cellIsSelected = _selectedCellIDs.Contains(cell.ID);
            RenderCellBoundingBox(drawList, screenPos, size, cell, cellIsSelected, cameraYaw, cameraPitch, cameraDistance);
        }

        // Render gizmo for selected cell (only if a single cell is selected)
        if (_selectedCellIDs.Count == 1 && _dataset.Mesh.Cells.TryGetValue(_selectedCellIDs[0], out var selectedCell))
        {
            RenderGizmo(drawList, screenPos, size, selectedCell, cameraYaw, cameraPitch, cameraDistance);
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

        // Render selection rectangle
        RenderSelectionRectangle(drawList);

        // Handle context menu
        if (ImGui.BeginPopupContextWindow())
        {
            if (ImGui.MenuItem("Enable Selected"))
            {
                foreach (var cellID in _selectedCellIDs)
                {
                    if (_dataset.Mesh.Cells.TryGetValue(cellID, out var cell))
                    {
                        cell.IsActive = true;
                    }
                }
            }
            if (ImGui.MenuItem("Disable Selected"))
            {
                foreach (var cellID in _selectedCellIDs)
                {
                    if (_dataset.Mesh.Cells.TryGetValue(cellID, out var cell))
                    {
                        cell.IsActive = false;
                    }
                }
            }
            ImGui.EndPopup();
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

            if (picked.Type == PickType.GizmoAxis)
            {
                _activeAxis = picked.Axis;
                _isDragging = true;
                _dragStart = mousePos;
                return true;
            }
            if (picked.Type == PickType.Cell)
            {
                if (_selectionMode == SelectionMode.Single)
                {
                    _selectedCellIDs.Clear();
                    _selectedCellIDs.Add(picked.CellID);
                }
                else
                {
                    SelectPlane(picked.CellID);
                }
                _selectedBoundaryIndex = -1;
                return true;
            }
            if (picked.Type == PickType.Boundary)
            {
                _selectedBoundaryIndex = picked.Index;
                _selectedCellIDs.Clear();
                return true;
            }
            else
            {
                // Clicked empty space
                _selectedCellIDs.Clear();
                _selectedBoundaryIndex = -1;
            }
        }

        if (isDragging && _isDragging && _activeAxis != GizmoAxis.None)
        {
            ApplyGizmoTransform(mousePos, screenPos, size);
            return true;
        }

        if (_selectionMode == SelectionMode.Rectangle)
        {
            if (isClicked)
            {
                _isRectangleSelecting = true;
                _rectangleStart = mousePos;
                _rectangleEnd = mousePos;
            }

            if (_isRectangleSelecting)
            {
                _rectangleEnd = mousePos;

                if (!isDragging)
                {
                    _isRectangleSelecting = false;
                    SelectCellsInRectangle(screenPos, size, cameraYaw, cameraPitch);
                }
            }
        }

        if (!isDragging && _isDragging)
        {
            _isDragging = false;
            _activeAxis = GizmoAxis.None;
        }

        return false;
    }


    private void RenderGizmo(ImDrawListPtr drawList, Vector2 screenPos, Vector2 size,
                            Cell cell, float cameraYaw, float cameraPitch, float cameraDistance)
    {
        var center = screenPos + size * 0.5f;
        var gizmoCenter = ProjectPoint(cell.Center.X, cell.Center.Y, cell.Center.Z,
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
        if (_selectedCellIDs.Count > 0)
        {
            var axis = PickGizmoAxis(mousePos, screenPos, size, cameraYaw, cameraPitch);
            if (axis != GizmoAxis.None)
            {
                return new PickResult { Type = PickType.GizmoAxis, Axis = axis };
            }
        }

        // Check cells
        foreach (var cell in _dataset.Mesh.Cells.Values)
        {
            if (IsPointNearCell(mousePos, screenPos, size, cell, cameraYaw, cameraPitch))
            {
                return new PickResult { Type = PickType.Cell, CellID = cell.ID };
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
        if (_selectedCellIDs.Count == 0 || !_dataset.Mesh.Cells.TryGetValue(_selectedCellIDs[0], out var cell))
            return GizmoAxis.None;

        var center = screenPos + size * 0.5f;
        var gizmoCenter = ProjectPoint(cell.Center.X, cell.Center.Y, cell.Center.Z,
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
        if (_selectedCellIDs.Count == 0 || !_dataset.Mesh.Cells.TryGetValue(_selectedCellIDs[0], out var cell))
            return;

        Vector2 delta = mousePos - _dragStart;
        float sensitivity = 0.01f;

        switch (_gizmoMode)
        {
            case GizmoMode.Translate:
                ApplyTranslation(cell, delta, sensitivity);
                break;

            case GizmoMode.Rotate:
                //ApplyRotation(cell, delta, sensitivity);
                break;

            case GizmoMode.Scale:
                ApplyScale(cell, delta, sensitivity);
                break;
        }
    }

    private void ApplyTranslation(Cell cell, Vector2 delta, float sensitivity)
    {
        if (cell == null)
            return;

        switch (_activeAxis)
        {
            case GizmoAxis.X:
                cell.Center = (cell.Center.X + delta.X * sensitivity, cell.Center.Y, cell.Center.Z);
                break;

            case GizmoAxis.Y:
                cell.Center = (cell.Center.X, cell.Center.Y - delta.Y * sensitivity, cell.Center.Z);
                break;

            case GizmoAxis.Z:
                cell.Center = (cell.Center.X, cell.Center.Y, cell.Center.Z + delta.X * sensitivity);
                break;
        }
    }

    private void ApplyRotation(Cell cell, Vector2 delta, float sensitivity)
    {
        if (cell == null)
            return;

        if (cell.Vertices == null || cell.Vertices.Count == 0)
            return;

        var angle = (delta.X - delta.Y) * sensitivity;
        if (Math.Abs(angle) < 1e-6f)
            return;

        var axis = _activeAxis switch
        {
            GizmoAxis.X => Vector3.UnitX,
            GizmoAxis.Y => Vector3.UnitY,
            GizmoAxis.Z => Vector3.UnitZ,
            _ => Vector3.UnitY
        };

        var rotation = Matrix4x4.CreateFromAxisAngle(axis, angle);
        var center = new Vector3((float)cell.Center.X, (float)cell.Center.Y, (float)cell.Center.Z);

        for (var i = 0; i < cell.Vertices.Count; i++)
        {
            var vertex = cell.Vertices[i];
            var local = vertex - center;
            var rotated = Vector3.Transform(local, rotation);
            cell.Vertices[i] = rotated + center;
        }
    }

    private void ApplyScale(Cell cell, Vector2 delta, float sensitivity)
    {
        if (cell == null)
            return;

        float scaleFactor = 1.0f + delta.X * sensitivity;

        // Prevent negative or zero scaling
        if (scaleFactor <= 0.01f)
            scaleFactor = 0.01f;

        cell.Volume *= Math.Pow(scaleFactor, 3);
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

        var bcIsActive = bc.IsActive;
        if (ImGui.Checkbox("Active", ref bcIsActive))
        {
            bc.IsActive = bcIsActive;
        }

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

    private void RenderCellBoundingBox(ImDrawListPtr drawList, Vector2 screenPos, Vector2 size, Cell cell, bool isSelected, float cameraYaw, float cameraPitch, float cameraDistance)
    {
        var color = isSelected ? ImGui.GetColorU32(new Vector4(1, 1, 0, 1)) : ImGui.GetColorU32(new Vector4(1, 1, 1, 0.5f));
        var corners = GetProjectedCellCorners(cell, screenPos + size * 0.5f, 100.0f, cameraYaw, cameraPitch);

        for (int i = 0; i < 4; i++)
        {
            drawList.AddLine(corners[i], corners[(i + 1) % 4], color);
            drawList.AddLine(corners[i + 4], corners[((i + 1) % 4) + 4], color);
            drawList.AddLine(corners[i], corners[i + 4], color);
        }
    }

    private Vector2[] GetProjectedCellCorners(Cell cell, Vector2 center, float scale, float cameraYaw, float cameraPitch)
    {
        var corners = new Vector2[8];
        var halfSize = (float)Math.Pow(cell.Volume, 1.0 / 3.0) / 2.0f;

        for (int i = 0; i < 8; i++)
        {
            var x = cell.Center.X + halfSize * ((i & 1) == 0 ? -1 : 1);
            var y = cell.Center.Y + halfSize * ((i & 2) == 0 ? -1 : 1);
            var z = cell.Center.Z + halfSize * ((i & 4) == 0 ? -1 : 1);
            corners[i] = ProjectPoint(x, y, z, center, scale, cameraYaw, cameraPitch);
        }

        return corners;
    }

    private bool IsPointNearCell(Vector2 mousePos, Vector2 screenPos, Vector2 size, Cell cell, float cameraYaw, float cameraPitch)
    {
        // Simplified picking logic - check distance to projected center
        var center = screenPos + size * 0.5f;
        var projectedCenter = ProjectPoint(cell.Center.X, cell.Center.Y, cell.Center.Z, center, 100.0f, cameraYaw, cameraPitch);
        return Vector2.Distance(mousePos, projectedCenter) < 20.0f;
    }

    private void DrawCellProperties(Cell cell)
    {
        ImGui.Text("Cell Properties");
        ImGui.Separator();

        ImGui.LabelText("ID", cell.ID);

        var isActive = cell.IsActive;
        if (ImGui.Checkbox("Active", ref isActive))
        {
            cell.IsActive = isActive;
        }
    }

    public List<string> SelectedCellIDs => _selectedCellIDs;
    public int SelectedBoundaryIndex => _selectedBoundaryIndex;
    public BuilderTool ActiveTool => _activeTool;

    private void SelectPlane(string cellID)
    {
        _selectedCellIDs.Clear();
        if (!_dataset.Mesh.Cells.TryGetValue(cellID, out var selectedCell))
            return;

        _selectedCellIDs.Add(cellID);

        foreach (var cell in _dataset.Mesh.Cells.Values)
        {
            if (cell.ID == cellID) continue;

            bool isInPlane = _selectionMode switch
            {
                SelectionMode.PlaneXY => Math.Abs(cell.Center.Z - selectedCell.Center.Z) < 0.01,
                SelectionMode.PlaneXZ => Math.Abs(cell.Center.Y - selectedCell.Center.Y) < 0.01,
                SelectionMode.PlaneYZ => Math.Abs(cell.Center.X - selectedCell.Center.X) < 0.01,
                _ => false
            };

            if (isInPlane)
            {
                _selectedCellIDs.Add(cell.ID);
            }
        }
    }

    private void SelectCellsInRectangle(Vector2 screenPos, Vector2 size, float cameraYaw, float cameraPitch)
    {
        _selectedCellIDs.Clear();

        var min = new Vector2(Math.Min(_rectangleStart.X, _rectangleEnd.X), Math.Min(_rectangleStart.Y, _rectangleEnd.Y));
        var max = new Vector2(Math.Max(_rectangleStart.X, _rectangleEnd.X), Math.Max(_rectangleStart.Y, _rectangleEnd.Y));

        foreach (var cell in _dataset.Mesh.Cells.Values)
        {
            var projectedCenter = ProjectPoint(cell.Center.X, cell.Center.Y, cell.Center.Z, screenPos + size * 0.5f, 100.0f, cameraYaw, cameraPitch);

            if (projectedCenter.X >= min.X && projectedCenter.X <= max.X &&
                projectedCenter.Y >= min.Y && projectedCenter.Y <= max.Y)
            {
                _selectedCellIDs.Add(cell.ID);
            }
        }
    }

    private void RenderSelectionRectangle(ImDrawListPtr drawList)
    {
        if (_isRectangleSelecting)
        {
            drawList.AddRect(_rectangleStart, _rectangleEnd, ImGui.GetColorU32(new Vector4(1, 1, 0, 1)));
        }
    }
}

/// <summary>
/// Builder tool modes
/// </summary>
public enum BuilderTool
{
    Select,
    Deform,
    Paint
}

public enum SelectionMode
{
    Single,
    PlaneXY,
    PlaneXZ,
    PlaneYZ,
    Rectangle
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
    public string CellID;
    public GizmoAxis Axis;
}

/// <summary>
/// Type of picked object
/// </summary>
public enum PickType
{
    None,
    Cell,
    Boundary,
    GizmoAxis,
    Vertex
}
