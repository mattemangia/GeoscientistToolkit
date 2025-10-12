// GeoscientistToolkit/Data/Mesh3D/Mesh3DEditor.cs

using System.Numerics;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Mesh3D;

/// <summary>
///     Basic 3D mesh editor with modeling tools and primitives
/// </summary>
public class Mesh3DEditor
{
    public enum EditorMode
    {
        View,
        Edit,
        AddPrimitive
    }

    private const int MaxUndoSteps = 50;
    private readonly Mesh3DDataset _dataset;
    private readonly Stack<MeshState> _redoStack = new();
    private readonly List<int> _selectedVertices = new();

    // Undo/Redo stacks
    private readonly Stack<MeshState> _undoStack = new();
    private float _angleSnapDegrees = 45f;
    private float _gridSnapSize = 0.5f;

    // Editing state
    private Vector3 _newVertexPosition = Vector3.Zero;
    private Vector3 _primitivePosition = Vector3.Zero;
    private Vector3 _primitiveScale = Vector3.One;
    private PrimitiveType _selectedPrimitive = PrimitiveType.Cube;
    private bool _snapAngle;

    // Snapping settings
    private bool _snapToGrid;
    private bool _snapToVertex;
    private float _vertexSnapDistance = 0.1f;

    public Mesh3DEditor(Mesh3DDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        SaveState(); // Initial state
    }

    public EditorMode Mode { get; private set; } = EditorMode.View;

    public bool IsEditMode => Mode != EditorMode.View;

    /// <summary>
    ///     Draw editor controls in the viewer toolbar
    /// </summary>
    public void DrawToolbarControls()
    {
        ImGui.Separator();
        ImGui.Text("Editor:");
        ImGui.SameLine();

        // Mode buttons
        if (ImGui.RadioButton("View", Mode == EditorMode.View))
            Mode = EditorMode.View;
        ImGui.SameLine();

        if (ImGui.RadioButton("Edit", Mode == EditorMode.Edit))
            Mode = EditorMode.Edit;
        ImGui.SameLine();

        if (ImGui.RadioButton("Add Primitive", Mode == EditorMode.AddPrimitive))
            Mode = EditorMode.AddPrimitive;

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Undo/Redo
        if (ImGui.Button("Undo") && _undoStack.Count > 1) Undo();
        ImGui.SameLine();

        if (ImGui.Button("Redo") && _redoStack.Count > 0) Redo();

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Snapping indicators
        if (_snapToGrid || _snapToVertex || _snapAngle)
        {
            ImGui.Text("Snap:");
            ImGui.SameLine();

            if (_snapToGrid)
            {
                ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), $"Grid({_gridSnapSize:F2})");
                ImGui.SameLine();
            }

            if (_snapToVertex)
            {
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Vtx({_vertexSnapDistance:F2})");
                ImGui.SameLine();
            }

            if (_snapAngle)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.4f, 1.0f), $"Ang({_angleSnapDegrees:F0}°)");
                ImGui.SameLine();
            }

            ImGui.Separator();
            ImGui.SameLine();
        }

        // Save/Export
        if (ImGui.Button("Save Model")) SaveModel();
    }

    /// <summary>
    ///     Draw editor panel in the viewer content area
    /// </summary>
    public void DrawEditorPanel()
    {
        if (Mode == EditorMode.View)
            return;

        ImGui.SetNextWindowPos(new Vector2(ImGui.GetIO().DisplaySize.X - 320, 80), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(300, 500), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Mesh Editor", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.Text($"Mode: {Mode}");
            ImGui.Text($"Vertices: {_dataset.VertexCount}");
            ImGui.Text($"Faces: {_dataset.FaceCount}");
            ImGui.Separator();

            // Snapping Settings (always visible)
            DrawSnappingSettings();
            ImGui.Separator();

            switch (Mode)
            {
                case EditorMode.Edit:
                    DrawEditModePanel();
                    break;
                case EditorMode.AddPrimitive:
                    DrawAddPrimitivePanel();
                    break;
            }
        }

        ImGui.End();
    }

    private void DrawEditModePanel()
    {
        ImGui.TextWrapped("Edit Mode: Select and modify vertices");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Add Vertex", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.DragFloat3("Position", ref _newVertexPosition, 0.1f);

            // Apply snapping to display
            var snappedPos = ApplySnapping(_newVertexPosition);
            if (snappedPos != _newVertexPosition)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("→");
                ImGui.SameLine();
                ImGui.Text($"({snappedPos.X:F2}, {snappedPos.Y:F2}, {snappedPos.Z:F2})");
            }

            if (ImGui.Button("Add Vertex", new Vector2(-1, 0))) AddVertex(ApplySnapping(_newVertexPosition));
        }

        if (ImGui.CollapsingHeader("Selection Info"))
        {
            ImGui.Text($"Selected: {_selectedVertices.Count} vertices");

            if (_selectedVertices.Count > 0)
            {
                if (ImGui.Button("Clear Selection", new Vector2(-1, 0))) _selectedVertices.Clear();

                ImGui.Spacing();

                if (_selectedVertices.Count >= 3)
                    if (ImGui.Button("Create Face from Selection", new Vector2(-1, 0)))
                        CreateFaceFromSelection();

                if (ImGui.Button("Delete Selected", new Vector2(-1, 0))) DeleteSelectedVertices();

                ImGui.Spacing();

                if (_selectedVertices.Count > 0 && ImGui.Button("Snap Selected to Grid", new Vector2(-1, 0)))
                    SnapSelectedToGrid();
            }
        }

        if (ImGui.CollapsingHeader("Mesh Operations"))
        {
            if (ImGui.Button("Recalculate Normals", new Vector2(-1, 0))) RecalculateNormals();

            if (ImGui.Button("Center Model", new Vector2(-1, 0))) CenterModel();

            if (ImGui.Button("Clear Mesh", new Vector2(-1, 0))) ClearMesh();
        }
    }

    private void DrawAddPrimitivePanel()
    {
        ImGui.TextWrapped("Add Primitive: Create basic 3D shapes");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Primitive Type", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var primitiveInt = (int)_selectedPrimitive;

            if (ImGui.RadioButton("Cube", ref primitiveInt, (int)PrimitiveType.Cube))
                _selectedPrimitive = PrimitiveType.Cube;
            if (ImGui.RadioButton("Sphere", ref primitiveInt, (int)PrimitiveType.Sphere))
                _selectedPrimitive = PrimitiveType.Sphere;
            if (ImGui.RadioButton("Cylinder", ref primitiveInt, (int)PrimitiveType.Cylinder))
                _selectedPrimitive = PrimitiveType.Cylinder;
            if (ImGui.RadioButton("Plane", ref primitiveInt, (int)PrimitiveType.Plane))
                _selectedPrimitive = PrimitiveType.Plane;
            if (ImGui.RadioButton("Cone", ref primitiveInt, (int)PrimitiveType.Cone))
                _selectedPrimitive = PrimitiveType.Cone;
        }

        if (ImGui.CollapsingHeader("Parameters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.DragFloat3("Position", ref _primitivePosition, 0.1f);

            // Show snapped position preview
            var snappedPos = ApplySnapping(_primitivePosition);
            if (snappedPos != _primitivePosition)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("→");
                ImGui.SameLine();
                ImGui.Text($"({snappedPos.X:F2}, {snappedPos.Y:F2}, {snappedPos.Z:F2})");
            }

            ImGui.DragFloat3("Scale", ref _primitiveScale, 0.1f, 0.1f, 10.0f);
        }

        if (ImGui.Button("Add Primitive", new Vector2(-1, 0)))
            AddPrimitive(_selectedPrimitive, ApplySnapping(_primitivePosition), _primitiveScale);
    }

    private void DrawSnappingSettings()
    {
        if (ImGui.CollapsingHeader("Snapping", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // Grid snapping
            ImGui.Checkbox("Snap to Grid", ref _snapToGrid);
            if (_snapToGrid)
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat("Grid Size", ref _gridSnapSize, 0.05f, 0.01f, 10.0f, "%.2f");

                // Quick preset buttons
                ImGui.Text("Presets:");
                ImGui.SameLine();
                if (ImGui.SmallButton("0.1")) _gridSnapSize = 0.1f;
                ImGui.SameLine();
                if (ImGui.SmallButton("0.25")) _gridSnapSize = 0.25f;
                ImGui.SameLine();
                if (ImGui.SmallButton("0.5")) _gridSnapSize = 0.5f;
                ImGui.SameLine();
                if (ImGui.SmallButton("1.0")) _gridSnapSize = 1.0f;

                ImGui.Unindent();
            }

            ImGui.Spacing();

            // Vertex snapping
            ImGui.Checkbox("Snap to Vertex", ref _snapToVertex);
            if (_snapToVertex)
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat("Snap Distance", ref _vertexSnapDistance, 0.01f, 0.01f, 2.0f, "%.2f");
                ImGui.TextDisabled("Snaps to nearest vertex within distance");
                ImGui.Unindent();
            }

            ImGui.Spacing();

            // Angle snapping (for future rotation tools)
            ImGui.Checkbox("Snap Angles", ref _snapAngle);
            if (_snapAngle)
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat("Angle Step", ref _angleSnapDegrees, 1f, 1f, 90f, "%.0f°");

                // Quick preset buttons
                ImGui.Text("Presets:");
                ImGui.SameLine();
                if (ImGui.SmallButton("15°")) _angleSnapDegrees = 15f;
                ImGui.SameLine();
                if (ImGui.SmallButton("45°")) _angleSnapDegrees = 45f;
                ImGui.SameLine();
                if (ImGui.SmallButton("90°")) _angleSnapDegrees = 90f;

                ImGui.Unindent();
            }

            ImGui.Unindent();
        }
    }

    private void AddVertex(Vector3 position)
    {
        SaveState();
        _dataset.Vertices.Add(position);
        _dataset.VertexCount = _dataset.Vertices.Count;
        UpdateDataset();
        Logger.Log($"Added vertex at {position}");
    }

    private void CreateFaceFromSelection()
    {
        if (_selectedVertices.Count < 3)
        {
            Logger.LogWarning("Need at least 3 vertices to create a face");
            return;
        }

        SaveState();
        _dataset.Faces.Add(_selectedVertices.ToArray());
        _dataset.FaceCount = _dataset.Faces.Count;
        _selectedVertices.Clear();
        UpdateDataset();
        Logger.Log("Created face from selection");
    }

    private void DeleteSelectedVertices()
    {
        if (_selectedVertices.Count == 0)
            return;

        SaveState();

        // Sort in descending order to maintain correct indices while removing
        _selectedVertices.Sort((a, b) => b.CompareTo(a));

        foreach (var idx in _selectedVertices)
            if (idx >= 0 && idx < _dataset.Vertices.Count)
                _dataset.Vertices.RemoveAt(idx);

        // Update faces to remove references to deleted vertices
        var validFaces = new List<int[]>();
        foreach (var face in _dataset.Faces)
        {
            var validFace = face.Where(v => !_selectedVertices.Contains(v)).ToArray();
            if (validFace.Length >= 3) validFaces.Add(validFace);
        }

        _dataset.Faces.Clear();
        _dataset.Faces.AddRange(validFaces);

        _dataset.VertexCount = _dataset.Vertices.Count;
        _dataset.FaceCount = _dataset.Faces.Count;
        _selectedVertices.Clear();
        UpdateDataset();
        Logger.Log("Deleted selected vertices");
    }

    private void RecalculateNormals()
    {
        SaveState();
        RecalculateNormalsInternal();
        _dataset.CalculateBounds();
        Logger.Log("Recalculated normals");
    }

    private void RecalculateNormalsInternal()
    {
        _dataset.Normals.Clear();

        // Initialize normals
        for (var i = 0; i < _dataset.Vertices.Count; i++) _dataset.Normals.Add(Vector3.Zero);

        // Calculate face normals and accumulate
        foreach (var face in _dataset.Faces)
            if (face.Length >= 3)
            {
                var v0 = _dataset.Vertices[face[0]];
                var v1 = _dataset.Vertices[face[1]];
                var v2 = _dataset.Vertices[face[2]];

                var edge1 = v1 - v0;
                var edge2 = v2 - v0;
                var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

                foreach (var idx in face) _dataset.Normals[idx] += normal;
            }

        // Normalize
        for (var i = 0; i < _dataset.Normals.Count; i++)
            if (_dataset.Normals[i].LengthSquared() > 0)
                _dataset.Normals[i] = Vector3.Normalize(_dataset.Normals[i]);
    }

    private void CenterModel()
    {
        if (_dataset.Vertices.Count == 0)
            return;

        SaveState();

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var v in _dataset.Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        var center = (min + max) * 0.5f;

        for (var i = 0; i < _dataset.Vertices.Count; i++) _dataset.Vertices[i] -= center;

        UpdateDataset();
        Logger.Log("Centered model");
    }

    private void ClearMesh()
    {
        SaveState();
        _dataset.Vertices.Clear();
        _dataset.Faces.Clear();
        _dataset.Normals.Clear();
        _dataset.VertexCount = 0;
        _dataset.FaceCount = 0;
        _selectedVertices.Clear();
        UpdateDataset();
        Logger.Log("Cleared mesh");
    }

    private void AddPrimitive(PrimitiveType type, Vector3 position, Vector3 scale)
    {
        SaveState();

        var baseVertexIndex = _dataset.Vertices.Count;

        switch (type)
        {
            case PrimitiveType.Cube:
                AddCube(position, scale);
                break;
            case PrimitiveType.Sphere:
                AddSphere(position, scale.X);
                break;
            case PrimitiveType.Cylinder:
                AddCylinder(position, scale);
                break;
            case PrimitiveType.Plane:
                AddPlane(position, scale);
                break;
            case PrimitiveType.Cone:
                AddCone(position, scale);
                break;
        }

        _dataset.VertexCount = _dataset.Vertices.Count;
        _dataset.FaceCount = _dataset.Faces.Count;
        UpdateDataset();
        Logger.Log($"Added {type} primitive");
    }

    private void AddCube(Vector3 position, Vector3 scale)
    {
        var baseIdx = _dataset.Vertices.Count;

        // Define 8 cube vertices
        var vertices = new[]
        {
            new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
            new Vector3(1, 1, -1), new Vector3(-1, 1, -1),
            new Vector3(-1, -1, 1), new Vector3(1, -1, 1),
            new Vector3(1, 1, 1), new Vector3(-1, 1, 1)
        };

        foreach (var v in vertices) _dataset.Vertices.Add(position + v * scale * 0.5f);

        // Define 12 triangular faces (2 per cube face)
        var faces = new[]
        {
            // Front
            new[] { 0, 1, 2 }, new[] { 0, 2, 3 },
            // Back
            new[] { 5, 4, 7 }, new[] { 5, 7, 6 },
            // Left
            new[] { 4, 0, 3 }, new[] { 4, 3, 7 },
            // Right
            new[] { 1, 5, 6 }, new[] { 1, 6, 2 },
            // Top
            new[] { 3, 2, 6 }, new[] { 3, 6, 7 },
            // Bottom
            new[] { 4, 5, 1 }, new[] { 4, 1, 0 }
        };

        foreach (var face in faces)
            _dataset.Faces.Add(new[] { baseIdx + face[0], baseIdx + face[1], baseIdx + face[2] });
    }

    private void AddSphere(Vector3 position, float radius)
    {
        var baseIdx = _dataset.Vertices.Count;
        var segments = 16;
        var rings = 12;

        // Generate vertices
        for (var ring = 0; ring <= rings; ring++)
        {
            var phi = MathF.PI * ring / rings;
            var y = MathF.Cos(phi);
            var ringRadius = MathF.Sin(phi);

            for (var seg = 0; seg <= segments; seg++)
            {
                var theta = 2.0f * MathF.PI * seg / segments;
                var x = ringRadius * MathF.Cos(theta);
                var z = ringRadius * MathF.Sin(theta);

                _dataset.Vertices.Add(position + new Vector3(x, y, z) * radius);
            }
        }

        // Generate faces
        for (var ring = 0; ring < rings; ring++)
        for (var seg = 0; seg < segments; seg++)
        {
            var i0 = baseIdx + ring * (segments + 1) + seg;
            var i1 = i0 + segments + 1;
            var i2 = i0 + 1;
            var i3 = i1 + 1;

            _dataset.Faces.Add(new[] { i0, i1, i2 });
            _dataset.Faces.Add(new[] { i2, i1, i3 });
        }
    }

    private void AddCylinder(Vector3 position, Vector3 scale)
    {
        var baseIdx = _dataset.Vertices.Count;
        var segments = 16;
        var radius = scale.X * 0.5f;
        var height = scale.Y;

        // Bottom cap center
        _dataset.Vertices.Add(position + new Vector3(0, -height * 0.5f, 0));

        // Bottom cap vertices
        for (var i = 0; i <= segments; i++)
        {
            var angle = 2.0f * MathF.PI * i / segments;
            var x = radius * MathF.Cos(angle);
            var z = radius * MathF.Sin(angle);
            _dataset.Vertices.Add(position + new Vector3(x, -height * 0.5f, z));
        }

        // Top cap vertices
        for (var i = 0; i <= segments; i++)
        {
            var angle = 2.0f * MathF.PI * i / segments;
            var x = radius * MathF.Cos(angle);
            var z = radius * MathF.Sin(angle);
            _dataset.Vertices.Add(position + new Vector3(x, height * 0.5f, z));
        }

        // Top cap center
        _dataset.Vertices.Add(position + new Vector3(0, height * 0.5f, 0));

        // Bottom cap faces
        for (var i = 0; i < segments; i++) _dataset.Faces.Add(new[] { baseIdx, baseIdx + i + 1, baseIdx + i + 2 });

        // Side faces
        for (var i = 0; i < segments; i++)
        {
            var b0 = baseIdx + i + 1;
            var b1 = baseIdx + i + 2;
            var t0 = baseIdx + segments + 2 + i;
            var t1 = baseIdx + segments + 2 + i + 1;

            _dataset.Faces.Add(new[] { b0, t0, b1 });
            _dataset.Faces.Add(new[] { b1, t0, t1 });
        }

        // Top cap faces
        var topCenter = baseIdx + 2 * (segments + 1) + 1;
        for (var i = 0; i < segments; i++)
            _dataset.Faces.Add(new[] { topCenter, baseIdx + segments + 3 + i, baseIdx + segments + 2 + i });
    }

    private void AddPlane(Vector3 position, Vector3 scale)
    {
        var baseIdx = _dataset.Vertices.Count;
        var halfX = scale.X * 0.5f;
        var halfZ = scale.Z * 0.5f;

        _dataset.Vertices.Add(position + new Vector3(-halfX, 0, -halfZ));
        _dataset.Vertices.Add(position + new Vector3(halfX, 0, -halfZ));
        _dataset.Vertices.Add(position + new Vector3(halfX, 0, halfZ));
        _dataset.Vertices.Add(position + new Vector3(-halfX, 0, halfZ));

        _dataset.Faces.Add(new[] { baseIdx, baseIdx + 1, baseIdx + 2 });
        _dataset.Faces.Add(new[] { baseIdx, baseIdx + 2, baseIdx + 3 });
    }

    private void AddCone(Vector3 position, Vector3 scale)
    {
        var baseIdx = _dataset.Vertices.Count;
        var segments = 16;
        var radius = scale.X * 0.5f;
        var height = scale.Y;

        // Apex
        _dataset.Vertices.Add(position + new Vector3(0, height * 0.5f, 0));

        // Base center
        _dataset.Vertices.Add(position + new Vector3(0, -height * 0.5f, 0));

        // Base rim
        for (var i = 0; i <= segments; i++)
        {
            var angle = 2.0f * MathF.PI * i / segments;
            var x = radius * MathF.Cos(angle);
            var z = radius * MathF.Sin(angle);
            _dataset.Vertices.Add(position + new Vector3(x, -height * 0.5f, z));
        }

        // Side faces
        for (var i = 0; i < segments; i++) _dataset.Faces.Add(new[] { baseIdx, baseIdx + i + 2, baseIdx + i + 3 });

        // Base faces
        for (var i = 0; i < segments; i++) _dataset.Faces.Add(new[] { baseIdx + 1, baseIdx + i + 3, baseIdx + i + 2 });
    }

    private void SaveState()
    {
        var state = new MeshState
        {
            Vertices = new List<Vector3>(_dataset.Vertices),
            Faces = new List<int[]>(_dataset.Faces.Select(f => (int[])f.Clone())),
            Normals = new List<Vector3>(_dataset.Normals)
        };

        _undoStack.Push(state);
        _redoStack.Clear();

        // Limit undo stack size
        while (_undoStack.Count > MaxUndoSteps)
        {
            var states = _undoStack.ToList();
            states.RemoveAt(states.Count - 1);
            _undoStack.Clear();
            foreach (var s in states.AsEnumerable().Reverse()) _undoStack.Push(s);
        }
    }

    private void Undo()
    {
        if (_undoStack.Count <= 1)
            return;

        var currentState = _undoStack.Pop();
        _redoStack.Push(currentState);

        var previousState = _undoStack.Peek();
        RestoreState(previousState);
        Logger.Log("Undo");
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
            return;

        var state = _redoStack.Pop();
        _undoStack.Push(state);
        RestoreState(state);
        Logger.Log("Redo");
    }

    private void RestoreState(MeshState state)
    {
        _dataset.Vertices.Clear();
        _dataset.Vertices.AddRange(state.Vertices);

        _dataset.Faces.Clear();
        _dataset.Faces.AddRange(state.Faces);

        _dataset.Normals.Clear();
        _dataset.Normals.AddRange(state.Normals);

        _dataset.VertexCount = _dataset.Vertices.Count;
        _dataset.FaceCount = _dataset.Faces.Count;

        UpdateDataset();
    }

    private void UpdateDataset()
    {
        _dataset.VertexCount = _dataset.Vertices.Count;
        _dataset.FaceCount = _dataset.Faces.Count;
        _dataset.CalculateBounds();
        RecalculateNormalsInternal();
    }

    private void SaveModel()
    {
        try
        {
            _dataset.Save();
            Logger.Log($"Saved 3D model: {_dataset.Name}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save model: {ex.Message}");
        }
    }

    // ========================================================================
    // SNAPPING FUNCTIONS
    // ========================================================================

    /// <summary>
    ///     Apply all enabled snapping modes to a position
    /// </summary>
    private Vector3 ApplySnapping(Vector3 position)
    {
        var snappedPos = position;

        // Vertex snapping takes priority over grid snapping
        if (_snapToVertex)
        {
            var nearestVertex = FindNearestVertex(position, out var distance);
            if (distance <= _vertexSnapDistance) return nearestVertex;
        }

        // Grid snapping
        if (_snapToGrid) snappedPos = SnapToGrid(position);

        return snappedPos;
    }

    /// <summary>
    ///     Snap a position to the nearest grid point
    /// </summary>
    private Vector3 SnapToGrid(Vector3 position)
    {
        return new Vector3(
            MathF.Round(position.X / _gridSnapSize) * _gridSnapSize,
            MathF.Round(position.Y / _gridSnapSize) * _gridSnapSize,
            MathF.Round(position.Z / _gridSnapSize) * _gridSnapSize
        );
    }

    /// <summary>
    ///     Find the nearest vertex to a given position
    /// </summary>
    private Vector3 FindNearestVertex(Vector3 position, out float distance)
    {
        if (_dataset.Vertices.Count == 0)
        {
            distance = float.MaxValue;
            return position;
        }

        var nearest = _dataset.Vertices[0];
        var minDist = Vector3.Distance(position, nearest);

        for (var i = 1; i < _dataset.Vertices.Count; i++)
        {
            var dist = Vector3.Distance(position, _dataset.Vertices[i]);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = _dataset.Vertices[i];
            }
        }

        distance = minDist;
        return nearest;
    }

    /// <summary>
    ///     Snap an angle to the nearest angle step
    /// </summary>
    private float SnapAngle(float angleDegrees)
    {
        if (!_snapAngle)
            return angleDegrees;

        return MathF.Round(angleDegrees / _angleSnapDegrees) * _angleSnapDegrees;
    }

    /// <summary>
    ///     Snap all selected vertices to the grid
    /// </summary>
    private void SnapSelectedToGrid()
    {
        if (_selectedVertices.Count == 0 || !_snapToGrid)
            return;

        SaveState();

        foreach (var idx in _selectedVertices)
            if (idx >= 0 && idx < _dataset.Vertices.Count)
                _dataset.Vertices[idx] = SnapToGrid(_dataset.Vertices[idx]);

        UpdateDataset();
        Logger.Log($"Snapped {_selectedVertices.Count} vertices to grid");
    }

    private class MeshState
    {
        public List<Vector3> Vertices { get; set; }
        public List<int[]> Faces { get; set; }
        public List<Vector3> Normals { get; set; }
    }

    private enum PrimitiveType
    {
        Cube,
        Sphere,
        Cylinder,
        Plane,
        Cone
    }
}