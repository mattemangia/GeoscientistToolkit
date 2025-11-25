using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.PhysicoChem;
using Gtk;

namespace GeoscientistToolkit.GtkUI;

/// <summary>
/// Lightweight 3D viewport built with GTK that mirrors the mesh behaviour of the ImGui renderer.
/// It renders PhysicoChem Voronoi cells or imported Mesh3D datasets using a simple orbit camera,
/// so users can sculpt meshes in a user friendly view without leaving the GTK client.
/// </summary>
public class MeshViewport3D : DrawingArea
{
    private readonly List<Vector3> _points = new();
    private readonly List<(int Start, int End)> _edges = new();
    private readonly List<List<int>> _faces = new();
    private readonly List<Vector2> _projected = new();
    private readonly List<CellInfo> _cells = new();
    private Mesh3DDataset? _activeMesh;
    private PhysicoChemMesh? _activePhysicoMesh;
    private PhysicoChemDataset? _activePhysicoDataset;
    private int? _selectedIndex;
    private int? _hoverIndex;
    private int? _hoveredCellIndex;
    private bool _isDragging;
    private Matrix4x4 _lastRotation = Matrix4x4.Identity;
    private Vector3 _lastMin = Vector3.Zero;
    private float _lastScale = 1f;
    private Vector2 _lastPointer;

    private float _yaw = 35f;
    private float _pitch = -20f;
    private float _zoom = 1.2f;

    public RenderMode RenderMode { get; set; } = RenderMode.Wireframe;
    public SelectionMode SelectionMode { get; set; } = SelectionMode.Single;
    public ColorCodingMode ColorMode { get; set; } = ColorCodingMode.Material;
    public HashSet<string> SelectedCellIDs { get; } = new();

    // Visualization settings
    public bool EnableSlicing { get; set; }
    public Vector4 SlicePlane { get; set; } = new Vector4(0, 0, 1, 0); // Default Z plane
    public bool ShowCrossSection { get; set; }

    public bool ShowIsosurface { get; set; }
    public double IsosurfaceThreshold { get; set; }
    public bool IsosurfaceGreaterThan { get; set; } = true;

    public event EventHandler<CellSelectionEventArgs>? CellSelectionChanged;

    public MeshViewport3D()
    {
        AddEvents((int)(Gdk.EventMask.ScrollMask | Gdk.EventMask.ButtonPressMask | Gdk.EventMask.PointerMotionMask | Gdk.EventMask.ButtonReleaseMask));
        ScrollEvent += (_, args) =>
        {
            _zoom = Math.Clamp(_zoom + (float)(-args.Event.DeltaY * 0.05), 0.1f, 8f);
            QueueDraw();
        };

        ButtonPressEvent += (_, args) =>
        {
            _lastPointer = new Vector2((float)args.Event.X, (float)args.Event.Y);

            // Try to select a cell first
            var cellIdx = FindCellAtPosition(_lastPointer);
            if (cellIdx.HasValue && cellIdx.Value < _cells.Count)
            {
                var cell = _cells[cellIdx.Value];
                bool shiftPressed = (args.Event.State & Gdk.ModifierType.ShiftMask) != 0;
                bool ctrlPressed = (args.Event.State & Gdk.ModifierType.ControlMask) != 0;

                if (shiftPressed || ctrlPressed || SelectionMode != SelectionMode.Single)
                {
                    // Multi-selection mode
                    if (SelectedCellIDs.Contains(cell.ID))
                        SelectedCellIDs.Remove(cell.ID);
                    else
                        SelectedCellIDs.Add(cell.ID);
                }
                else
                {
                    // Single selection mode
                    SelectedCellIDs.Clear();
                    SelectedCellIDs.Add(cell.ID);
                }

                CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(SelectedCellIDs.ToList()));
                QueueDraw();
                return;
            }

            // Fallback to vertex selection for mesh editing
            if (_projected.Count == 0) return;
            var idx = FindClosestPoint(_lastPointer);
            _selectedIndex = idx;
            _isDragging = idx.HasValue;
            QueueDraw();
        };

        MotionNotifyEvent += (_, args) =>
        {
            var previousPointer = _lastPointer;
            _lastPointer = new Vector2((float)args.Event.X, (float)args.Event.Y);

            // Update hovered cell
            _hoveredCellIndex = FindCellAtPosition(_lastPointer);
            _hoverIndex = FindClosestPoint(_lastPointer, 18f);

            if (_isDragging && _selectedIndex.HasValue && _activeMesh != null)
            {
                var delta = _lastPointer - previousPointer;
                ApplyDragDelta(_selectedIndex.Value, delta);
            }

            QueueDraw();
        };

        ButtonReleaseEvent += (_, _) => _isDragging = false;
    }

    public void LoadFromPhysicoChem(PhysicoChemMesh mesh, PhysicoChemDataset? dataset = null)
    {
        _points.Clear();
        _edges.Clear();
        _faces.Clear();
        _cells.Clear();
        _activePhysicoMesh = mesh;
        _activePhysicoDataset = dataset;

        var cellIndex = new Dictionary<string, int>();

        // For each cell, create a box based on its volume
        foreach (var (id, cell) in mesh.Cells.OrderBy(c => c.Key))
        {
            // Calculate box dimensions from volume (assume cubic cells)
            float size = (float)Math.Pow(cell.Volume, 1.0 / 3.0) * 0.5f;

            var center = new Vector3((float)cell.Center.X, (float)cell.Center.Y, (float)cell.Center.Z);

            // Store starting index for this cell's vertices
            int startIdx = _points.Count;
            int cellIdx = _cells.Count;
            cellIndex[id] = startIdx;

            // Store cell info for selection
            _cells.Add(new CellInfo
            {
                ID = id,
                Cell = cell,
                VertexStartIndex = startIdx,
                FaceStartIndex = _faces.Count,
                Center = center
            });

            // Generate 8 corners of the box
            for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
            for (int k = 0; k < 2; k++)
            {
                float x = center.X + (i == 0 ? -size : size);
                float y = center.Y + (j == 0 ? -size : size);
                float z = center.Z + (k == 0 ? -size : size);
                _points.Add(new Vector3(x, y, z));
            }

            // Add edges for the box (12 edges total)
            // Bottom face (z = -size)
            _edges.Add((startIdx + 0, startIdx + 1)); // 000 -> 100
            _edges.Add((startIdx + 1, startIdx + 3)); // 100 -> 110
            _edges.Add((startIdx + 3, startIdx + 2)); // 110 -> 010
            _edges.Add((startIdx + 2, startIdx + 0)); // 010 -> 000

            // Top face (z = +size)
            _edges.Add((startIdx + 4, startIdx + 5)); // 001 -> 101
            _edges.Add((startIdx + 5, startIdx + 7)); // 101 -> 111
            _edges.Add((startIdx + 7, startIdx + 6)); // 111 -> 011
            _edges.Add((startIdx + 6, startIdx + 4)); // 011 -> 001

            // Vertical edges connecting bottom to top
            _edges.Add((startIdx + 0, startIdx + 4)); // 000 -> 001
            _edges.Add((startIdx + 1, startIdx + 5)); // 100 -> 101
            _edges.Add((startIdx + 2, startIdx + 6)); // 010 -> 011
            _edges.Add((startIdx + 3, startIdx + 7)); // 110 -> 111

            // Add faces for solid rendering (6 faces per box)
            // Bottom face (z = -size)
            _faces.Add(new List<int> { startIdx + 0, startIdx + 1, startIdx + 3, startIdx + 2 });
            // Top face (z = +size)
            _faces.Add(new List<int> { startIdx + 4, startIdx + 5, startIdx + 7, startIdx + 6 });
            // Front face (y = -size)
            _faces.Add(new List<int> { startIdx + 0, startIdx + 1, startIdx + 5, startIdx + 4 });
            // Back face (y = +size)
            _faces.Add(new List<int> { startIdx + 2, startIdx + 3, startIdx + 7, startIdx + 6 });
            // Left face (x = -size)
            _faces.Add(new List<int> { startIdx + 0, startIdx + 2, startIdx + 6, startIdx + 4 });
            // Right face (x = +size)
            _faces.Add(new List<int> { startIdx + 1, startIdx + 3, startIdx + 7, startIdx + 5 });
        }

        // Add connections between cells (optional - can be commented out for cleaner view)
        foreach (var (cell1, cell2) in mesh.Connections)
        {
            if (cellIndex.TryGetValue(cell1, out var start) && cellIndex.TryGetValue(cell2, out var end))
            {
                // Connect centers of cells (indices 0 of each cell's 8 vertices)
                // Skip for now to avoid cluttering the view with too many edges
                // _edges.Add((start, end));
            }
        }

        QueueDraw();
    }

    public void LoadFromMesh(Mesh3DDataset mesh)
    {
        _points.Clear();
        _edges.Clear();
        _activeMesh = mesh;

        _points.AddRange(mesh.Vertices);

        var uniqueEdges = new HashSet<(int, int)>();
        foreach (var face in mesh.Faces)
        {
            if (face.Length < 3) continue;
            AddEdge(uniqueEdges, face[0], face[1]);
            AddEdge(uniqueEdges, face[1], face[2]);
            AddEdge(uniqueEdges, face[2], face[0]);
        }

        _edges.AddRange(uniqueEdges.Select(e => (Start: e.Item1, End: e.Item2)));
        QueueDraw();
    }

    public void Clear()
    {
        _points.Clear();
        _edges.Clear();
        _activeMesh = null;
        _selectedIndex = null;
        _hoverIndex = null;
        QueueDraw();
    }

    public void SetCamera(float yawDegrees, float pitchDegrees, float zoom)
    {
        _yaw = yawDegrees;
        _pitch = pitchDegrees;
        _zoom = zoom;
        QueueDraw();
    }

    protected override bool OnDrawn(Cairo.Context cr)
    {
        cr.SetSourceRGB(0.08, 0.09, 0.11);
        cr.Paint();

        DrawAxisGizmo(cr, Allocation.Width, Allocation.Height);

        if (_points.Count == 0)
            return base.OnDrawn(cr);

        var allocation = Allocation;
        var width = allocation.Width;
        var height = allocation.Height;

        var rotation = Matrix4x4.CreateFromYawPitchRoll(
            MathF.PI / 180f * _yaw,
            MathF.PI / 180f * _pitch,
            0f);

        _lastRotation = rotation;
        _projected.Clear();

        var projected = new List<Vector2>(_points.Count);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var point in _points)
        {
            var rotated = Vector3.Transform(point, rotation) * _zoom;
            min = Vector3.Min(min, rotated);
            max = Vector3.Max(max, rotated);
            projected.Add(new Vector2(rotated.X, rotated.Y));
            _projected.Add(new Vector2(rotated.X, rotated.Y));
        }

        var size = Vector3.Max(max - min, new Vector3(1, 1, 1));
        var scale = MathF.Min(width / size.X, height / size.Y) * 0.45f;
        _lastScale = scale;
        _lastMin = min;

        // Render faces (solid mode)
        if ((RenderMode == RenderMode.Solid || RenderMode == RenderMode.SolidWireframe) && _faces.Count > 0)
        {
            // Sort faces by average Z depth (painter's algorithm for simple depth sorting)
            var faceDepths = new List<(List<int> face, float depth, int originalIndex)>();
            for (int i = 0; i < _faces.Count; i++)
            {
                var face = _faces[i];

                // 1. Visibility Check (Slicing)
                if (EnableSlicing)
                {
                    // Check if face center is on the correct side of the plane
                    var p1 = _points[face[0]];
                    var p2 = _points[face[2]]; // Diagonal
                    var center = (p1 + p2) * 0.5f;

                    // Plane equation: Ax + By + Cz + D = 0
                    float dist = SlicePlane.X * center.X + SlicePlane.Y * center.Y + SlicePlane.Z * center.Z + SlicePlane.W;
                    if (dist < 0) continue; // Clipped
                }

                // 2. Isosurface Filtering
                var cellInfo = FindCellForFace(i);
                if (ShowIsosurface && cellInfo != null)
                {
                    double val = GetValueForColorMode(cellInfo.Cell);
                    bool visible = IsosurfaceGreaterThan ? val >= IsosurfaceThreshold : val <= IsosurfaceThreshold;
                    if (!visible) continue;
                }

                float avgZ = 0;
                foreach (var idx in face)
                {
                    var rotated = Vector3.Transform(_points[idx], rotation) * _zoom;
                    avgZ += rotated.Z;
                }
                avgZ /= face.Count;
                faceDepths.Add((face, avgZ, i));
            }
            faceDepths.Sort((a, b) => a.depth.CompareTo(b.depth)); // Draw back to front

            // Draw filled faces
            foreach (var (face, _, originalIdx) in faceDepths)
            {
                if (face.Count < 3) continue;

                // Find which cell this face belongs to
                var cellInfo = FindCellForFace(originalIdx);
                bool isSelected = cellInfo != null && SelectedCellIDs.Contains(cellInfo.ID);
                bool isHovered = cellInfo != null && _hoveredCellIndex.HasValue &&
                                _hoveredCellIndex.Value < _cells.Count &&
                                _cells[_hoveredCellIndex.Value].ID == cellInfo.ID;
                bool isInactive = cellInfo?.Cell.IsActive == false;

                var firstPoint = Project(projected[face[0]], min, scale, width, height);
                cr.MoveTo(firstPoint.X, firstPoint.Y);

                for (int i = 1; i < face.Count; i++)
                {
                    var point = Project(projected[face[i]], min, scale, width, height);
                    cr.LineTo(point.X, point.Y);
                }
                cr.ClosePath();

                // Get color based on mode and state
                var color = GetCellColor(cellInfo, isSelected, isHovered, isInactive);
                cr.SetSourceRGBA(color.Item1, color.Item2, color.Item3, color.Item4);
                cr.FillPreserve();

                // Outline the face
                if (RenderMode == RenderMode.SolidWireframe || isSelected)
                {
                    if (isSelected)
                        cr.SetSourceRGB(1.0, 0.8, 0.0); // Bright yellow for selection
                    else
                        cr.SetSourceRGB(0.2, 0.4, 0.7);
                    cr.LineWidth = isSelected ? 2.0 : 0.8;
                    cr.Stroke();
                }
                else
                {
                    cr.NewPath(); // Clear the path without stroking
                }
            }
        }

        // Render edges (wireframe mode)
        if ((RenderMode == RenderMode.Wireframe || RenderMode == RenderMode.SolidWireframe) && _edges.Count > 0)
        {
            cr.SetSourceRGB(0.25, 0.55, 0.95);
            cr.LineWidth = 1.2;

            foreach (var edge in _edges)
            {
                var start = Project(projected[edge.Start], min, scale, width, height);
                var end = Project(projected[edge.End], min, scale, width, height);
                cr.MoveTo(start.X, start.Y);
                cr.LineTo(end.X, end.Y);
                cr.Stroke();
            }
        }
        else if (_edges.Count == 0 && _faces.Count == 0)
        {
            // Fallback to point rendering if no edges or faces
            var radius = _points.Count <= 2 ? 9 : 3;
            foreach (var pt in projected)
            {
                var p = Project(pt, min, scale, width, height);
                cr.Arc(p.X, p.Y, radius, 0, Math.PI * 2);
                cr.SetSourceRGB(0.3, 0.7, 1.0);
                cr.Fill();
            }
        }

        if (_selectedIndex.HasValue && _selectedIndex.Value < projected.Count)
        {
            var p = Project(projected[_selectedIndex.Value], min, scale, width, height);
            cr.SetSourceRGB(1, 0.8, 0.2);
            cr.Arc(p.X, p.Y, 6, 0, Math.PI * 2);
            cr.LineWidth = 2.5;
            cr.Stroke();
        }

        if (_hoverIndex.HasValue && _hoverIndex.Value < projected.Count && _hoverIndex != _selectedIndex)
        {
            var p = Project(projected[_hoverIndex.Value], min, scale, width, height);
            cr.SetSourceRGB(0.9, 0.6, 0.9);
            cr.Arc(p.X, p.Y, 4, 0, Math.PI * 2);
            cr.LineWidth = 1.5;
            cr.Stroke();
        }

        cr.SetSourceRGB(0.8, 0.8, 0.8);
        cr.SelectFontFace("Sans", Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
        cr.SetFontSize(12);
        cr.MoveTo(12, 18);
        cr.ShowText($"Points: {_points.Count} | Edges: {_edges.Count} | Yaw {_yaw:F0}° | Pitch {_pitch:F0}°");

        return base.OnDrawn(cr);
    }

    private static Vector2 Project(Vector2 v, Vector3 min, float scale, int width, int height)
    {
        var x = (v.X - min.X) * scale + width / 2f;
        var y = height / 2f - (v.Y - min.Y) * scale;
        return new Vector2(x, y);
    }

    private static void AddEdge(HashSet<(int, int)> edges, int a, int b)
    {
        if (a == b) return;
        var ordered = a < b ? (a, b) : (b, a);
        edges.Add(ordered);
    }

    private static void DrawAxisGizmo(Cairo.Context cr, int width, int height)
    {
        var origin = new Vector2(width - 70, height - 60);
        var axisLength = 36f;

        cr.LineWidth = 2.4;

        cr.SetSourceRGB(0.82, 0.32, 0.32); // X red
        cr.MoveTo(origin.X, origin.Y);
        cr.LineTo(origin.X + axisLength, origin.Y);
        cr.Stroke();
        cr.MoveTo(origin.X + axisLength + 6, origin.Y + 4);
        cr.ShowText("X");

        cr.SetSourceRGB(0.35, 0.8, 0.45); // Y green
        cr.MoveTo(origin.X, origin.Y);
        cr.LineTo(origin.X, origin.Y - axisLength);
        cr.Stroke();
        cr.MoveTo(origin.X - 8, origin.Y - axisLength - 4);
        cr.ShowText("Y");

        cr.SetSourceRGB(0.35, 0.55, 0.95); // Z blue
        cr.MoveTo(origin.X, origin.Y);
        cr.LineTo(origin.X - axisLength * 0.7, origin.Y + axisLength * 0.7);
        cr.Stroke();
        cr.MoveTo(origin.X - axisLength * 0.7 - 12, origin.Y + axisLength * 0.7 + 2);
        cr.ShowText("Z");
    }

    private int? FindClosestPoint(Vector2 pointer, float maxDistance = 12f)
    {
        if (_projected.Count == 0) return null;
        var allocation = Allocation;
        var width = allocation.Width;
        var height = allocation.Height;
        var min = _lastMin;
        var scale = _lastScale;

        int? bestIndex = null;
        var bestDistance = maxDistance;
        for (var i = 0; i < _projected.Count; i++)
        {
            var p = Project(_projected[i], min, scale, width, height);
            var dist = (float)Math.Sqrt(Math.Pow(p.X - pointer.X, 2) + Math.Pow(p.Y - pointer.Y, 2));
            if (dist <= bestDistance)
            {
                bestDistance = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private void ApplyDragDelta(int index, Vector2 screenDelta)
    {
        if (_activeMesh == null || index >= _points.Count) return;

        var viewDelta = new Vector3(screenDelta.X / _lastScale, -screenDelta.Y / _lastScale, 0);
        var worldDelta = Vector3.Transform(viewDelta, Matrix4x4.Transpose(_lastRotation));

        var updated = _points[index] + worldDelta;
        _points[index] = updated;
        _activeMesh.Vertices[index] = updated;
        _activeMesh.CalculateBounds();
    }

    private int? FindCellAtPosition(Vector2 pointer)
    {
        if (_cells.Count == 0) return null;

        var allocation = Allocation;
        var width = allocation.Width;
        var height = allocation.Height;

        // Check each cell's center
        float minDistance = 50f; // Max click distance
        int? closestCell = null;

        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            var projected = _projected[cell.VertexStartIndex];
            var screenPos = Project(projected, _lastMin, _lastScale, width, height);

            float dist = MathF.Sqrt(MathF.Pow(screenPos.X - pointer.X, 2) + MathF.Pow(screenPos.Y - pointer.Y, 2));
            if (dist < minDistance)
            {
                minDistance = dist;
                closestCell = i;
            }
        }

        return closestCell;
    }

    private CellInfo? FindCellForFace(int faceIndex)
    {
        foreach (var cell in _cells)
        {
            int cellFaceStart = cell.FaceStartIndex;
            int cellFaceEnd = cellFaceStart + 6; // 6 faces per box
            if (faceIndex >= cellFaceStart && faceIndex < cellFaceEnd)
                return cell;
        }
        return null;
    }

    private double GetValueForColorMode(Cell cell)
    {
        if (cell.InitialConditions == null) return 0;
        return ColorMode switch
        {
            ColorCodingMode.Temperature => cell.InitialConditions.Temperature,
            ColorCodingMode.Pressure => cell.InitialConditions.Pressure,
            _ => 0
        };
    }

    private (double, double, double, double) GetCellColor(CellInfo? cellInfo, bool isSelected, bool isHovered, bool isInactive)
    {
        // Inactive cells are gray
        if (isInactive)
            return (0.3, 0.3, 0.3, 0.3);

        // Selected cells are highlighted in orange/yellow
        if (isSelected)
            return (1.0, 0.7, 0.2, 0.8);

        // Hovered cells are highlighted in cyan
        if (isHovered)
            return (0.4, 0.9, 0.9, 0.7);

        if (cellInfo == null || _activePhysicoMesh == null)
            return (0.3, 0.6, 0.9, 0.6);

        // Color-code by selected property
        switch (ColorMode)
        {
            case ColorCodingMode.Material:
                return GetMaterialColor(cellInfo.Cell);

            case ColorCodingMode.Temperature:
                return GetTemperatureColor(cellInfo.Cell);

            case ColorCodingMode.Pressure:
                return GetPressureColor(cellInfo.Cell);

            case ColorCodingMode.Active:
                return cellInfo.Cell.IsActive ? (0.3, 0.9, 0.3, 0.6) : (0.9, 0.3, 0.3, 0.6);

            default:
                return (0.3, 0.6, 0.9, 0.6);
        }
    }

    private (double, double, double, double) GetMaterialColor(Cell cell)
    {
        // Use material color if available
        if (_activePhysicoDataset != null)
        {
            var material = _activePhysicoDataset.Materials?.FirstOrDefault(m => m.MaterialID == cell.MaterialID);
            if (material != null)
            {
                return (material.Color.X, material.Color.Y, material.Color.Z, 0.6);
            }
        }
        return (0.3, 0.6, 0.9, 0.6);
    }

    private (double, double, double, double) GetTemperatureColor(Cell cell)
    {
        if (cell.InitialConditions == null)
            return (0.3, 0.6, 0.9, 0.6);

        // Map temperature to color (blue=cold, red=hot)
        double temp = cell.InitialConditions.Temperature;
        double normalized = Math.Clamp((temp - 273.15) / 100.0, 0.0, 1.0); // 0-100°C range

        double r = normalized;
        double g = 0.3;
        double b = 1.0 - normalized;
        return (r, g, b, 0.6);
    }

    private (double, double, double, double) GetPressureColor(Cell cell)
    {
        if (cell.InitialConditions == null)
            return (0.3, 0.6, 0.9, 0.6);

        // Map pressure to color
        double pressure = cell.InitialConditions.Pressure;
        double normalized = Math.Clamp((pressure - 101325.0) / 100000.0, 0.0, 1.0);

        double r = 0.3;
        double g = normalized;
        double b = 1.0 - normalized;
        return (r, g, b, 0.6);
    }

    public void SelectCellsInPlane(PlaneType plane, double position, double tolerance = 0.1)
    {
        SelectedCellIDs.Clear();

        foreach (var cellInfo in _cells)
        {
            bool inPlane = plane switch
            {
                PlaneType.XY => Math.Abs(cellInfo.Cell.Center.Z - position) < tolerance,
                PlaneType.XZ => Math.Abs(cellInfo.Cell.Center.Y - position) < tolerance,
                PlaneType.YZ => Math.Abs(cellInfo.Cell.Center.X - position) < tolerance,
                _ => false
            };

            if (inPlane && cellInfo.Cell.IsActive)
                SelectedCellIDs.Add(cellInfo.ID);
        }

        CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(SelectedCellIDs.ToList()));
        QueueDraw();
    }

    public void ToggleSelectedCellsActive()
    {
        if (_activePhysicoMesh == null) return;

        foreach (var cellId in SelectedCellIDs)
        {
            if (_activePhysicoMesh.Cells.TryGetValue(cellId, out var cell))
            {
                cell.IsActive = !cell.IsActive;
            }
        }
        QueueDraw();
    }

    public void ClearSelection()
    {
        SelectedCellIDs.Clear();
        CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(new List<string>()));
        QueueDraw();
    }
}

/// <summary>
/// Information about a cell for selection and rendering
/// </summary>
internal class CellInfo
{
    public string ID { get; set; } = "";
    public Cell Cell { get; set; } = null!;
    public int VertexStartIndex { get; set; }
    public int FaceStartIndex { get; set; }
    public Vector3 Center { get; set; }
}

/// <summary>
/// Event args for cell selection changes
/// </summary>
public class CellSelectionEventArgs : EventArgs
{
    public List<string> SelectedCellIDs { get; }

    public CellSelectionEventArgs(List<string> selectedCellIDs)
    {
        SelectedCellIDs = selectedCellIDs;
    }
}

/// <summary>
/// Rendering mode for 3D visualization
/// </summary>
public enum RenderMode
{
    Wireframe,
    Solid,
    SolidWireframe
}

/// <summary>
/// Selection mode for cells
/// </summary>
public enum SelectionMode
{
    Single,
    Multiple,
    PlaneXY,
    PlaneXZ,
    PlaneYZ
}

/// <summary>
/// Color coding mode for cells
/// </summary>
public enum ColorCodingMode
{
    Material,
    Temperature,
    Pressure,
    Active,
    None
}

/// <summary>
/// Plane types for selection
/// </summary>
public enum PlaneType
{
    XY,
    XZ,
    YZ
}
