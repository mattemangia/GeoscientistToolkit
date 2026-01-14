using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.PhysicoChem;
using Gtk;
using Gdk;
using GdkKey = Gdk.Key;

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
    private Dictionary<Vector3, int> _vertexMap = new();
    private Mesh3DDataset? _activeMesh;
    private PhysicoChemMesh? _activePhysicoMesh;
    private PhysicoChemDataset? _activePhysicoDataset;
    private int? _selectedIndex;
    private int? _hoverIndex;
    private int? _hoveredCellIndex;
    private bool _isDragging;
    private bool _isCameraRotating;
    private bool _isCameraPanning;
    private bool _isRectangleSelecting;
    private Vector2 _rectangleStart;
    private Vector2 _rectangleEnd;
    private Matrix4x4 _lastRotation = Matrix4x4.Identity;
    private Vector3 _lastMin = Vector3.Zero;
    private float _lastScale = 1f;
    private Vector2 _lastPointer;
    private Vector2 _clickStartPointer;
    private Vector3 _cameraTarget = Vector3.Zero;

    private float _yaw = 35f;
    private float _pitch = -20f;
    private float _zoom = 1.2f;
    private Vector2 _panOffset = Vector2.Zero;
    private float _baseScale = 1f;
    private Vector3 _meshCenter = Vector3.Zero;

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
        CanFocus = true;
        AddEvents((int)(Gdk.EventMask.ScrollMask | Gdk.EventMask.SmoothScrollMask | Gdk.EventMask.ButtonPressMask | Gdk.EventMask.PointerMotionMask | Gdk.EventMask.ButtonReleaseMask | Gdk.EventMask.KeyPressMask));
        ScrollEvent += (_, args) =>
        {
            float delta = 0f;

            // Handle both smooth scrolling and discrete scroll events
            if (args.Event.Direction == Gdk.ScrollDirection.Smooth)
            {
                delta = (float)args.Event.DeltaY;
            }
            else if (args.Event.Direction == Gdk.ScrollDirection.Up)
            {
                delta = -1f;
            }
            else if (args.Event.Direction == Gdk.ScrollDirection.Down)
            {
                delta = 1f;
            }

            _zoom = Math.Clamp(_zoom - delta * 0.1f, 0.1f, 8f);
            QueueDraw();
            args.RetVal = true; // Mark event as handled
        };

        ButtonPressEvent += (_, args) =>
        {
            GrabFocus(); // Ensure we have focus for scroll events
            _lastPointer = new Vector2((float)args.Event.X, (float)args.Event.Y);
            _clickStartPointer = _lastPointer; // Track where click started

            // Middle or right button: Camera panning
            if (args.Event.Button == 2 || args.Event.Button == 3)
            {
                _isCameraPanning = true;
                args.RetVal = true;
                return;
            }

            // Left button with Alt/Ctrl: Camera rotation
            bool altPressed = (args.Event.State & Gdk.ModifierType.Mod1Mask) != 0;
            bool ctrlPressed = (args.Event.State & Gdk.ModifierType.ControlMask) != 0;

            if (args.Event.Button == 1 && (altPressed || ctrlPressed))
            {
                _isCameraRotating = true;
                args.RetVal = true;
                return;
            }

            // Left button: Handle based on selection mode
            if (args.Event.Button == 1)
            {
                // Rectangle selection mode: Start rectangle selection
                if (SelectionMode == SelectionMode.Rectangle)
                {
                    _isRectangleSelecting = true;
                    _rectangleStart = _lastPointer;
                    _rectangleEnd = _lastPointer;
                    args.RetVal = true;
                    return;
                }

                // Other modes: Try to select a cell first
                var cellIdx = FindCellAtPosition(_lastPointer);

                // Handle plane selection modes
                if (SelectionMode == SelectionMode.PlaneXY || SelectionMode == SelectionMode.PlaneXZ || SelectionMode == SelectionMode.PlaneYZ)
                {
                    if (cellIdx.HasValue && cellIdx.Value < _cells.Count)
                    {
                        var cell = _cells[cellIdx.Value].Cell;
                        double position;
                        PlaneType planeType;

                        switch (SelectionMode)
                        {
                            case SelectionMode.PlaneXY:
                                position = cell.Center.Z;
                                planeType = PlaneType.XY;
                                break;
                            case SelectionMode.PlaneXZ:
                                position = cell.Center.Y;
                                planeType = PlaneType.XZ;
                                break;
                            case SelectionMode.PlaneYZ:
                                position = cell.Center.X;
                                planeType = PlaneType.YZ;
                                break;
                            default:
                                return; // Should not happen
                        }
                        bool shiftPressed = (args.Event.State & Gdk.ModifierType.ShiftMask) != 0;
                        SelectCellsInPlane(planeType, position, additive: shiftPressed);
                    }
                    return;
                }


                if (cellIdx.HasValue && cellIdx.Value < _cells.Count)
                {
                    var cell = _cells[cellIdx.Value];
                    bool shiftPressed = (args.Event.State & Gdk.ModifierType.ShiftMask) != 0;

                    if (shiftPressed || SelectionMode != SelectionMode.Single)
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

                // No cell clicked
                // In Rectangle mode, clear selection if not shift-clicking
                // In other modes, we're starting rotation so preserve selection
                bool shiftHeld = (args.Event.State & Gdk.ModifierType.ShiftMask) != 0;
                if (!shiftHeld && SelectedCellIDs.Count > 0 && SelectionMode == SelectionMode.Rectangle)
                {
                    SelectedCellIDs.Clear();
                    CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(SelectedCellIDs.ToList()));
                    QueueDraw();
                }

                // Start camera rotation (only if not in rectangle selection mode)
                if (SelectionMode != SelectionMode.Rectangle)
                {
                    _isCameraRotating = true;
                }
            }
        };

        MotionNotifyEvent += (_, args) =>
        {
            var previousPointer = _lastPointer;
            _lastPointer = new Vector2((float)args.Event.X, (float)args.Event.Y);
            var delta = _lastPointer - previousPointer;

            // Handle rectangle selection
            if (_isRectangleSelecting)
            {
                _rectangleEnd = _lastPointer;
                QueueDraw();
                args.RetVal = true;
                return;
            }

            // Handle camera rotation
            if (_isCameraRotating)
            {
                _yaw += delta.X * 0.5f;
                _pitch = Math.Clamp(_pitch - delta.Y * 0.5f, -89f, 89f);
                QueueDraw();
                args.RetVal = true;
                return;
            }

            // Handle camera panning
            if (_isCameraPanning)
            {
                _panOffset.X += delta.X;
                _panOffset.Y += delta.Y;
                QueueDraw();
                args.RetVal = true;
                return;
            }

            // Update hovered cell
            _hoveredCellIndex = FindCellAtPosition(_lastPointer);
            _hoverIndex = FindClosestPoint(_lastPointer, 18f);

            if (_isDragging && _selectedIndex.HasValue && _activeMesh != null)
            {
                ApplyDragDelta(_selectedIndex.Value, delta);
            }

            QueueDraw();
        };

        ButtonReleaseEvent += (_, args) =>
        {
            // Handle rectangle selection completion
            if (_isRectangleSelecting)
            {
                _isRectangleSelecting = false;
                SelectCellsInRectangle(_rectangleStart, _rectangleEnd);
                QueueDraw();
                args.RetVal = true;
                return;
            }

            // Check if this was a click (no drag) on empty space in single selection mode
            // If so, clear selection
            if (args.Event.Button == 1 && _isCameraRotating)
            {
                var dragDistance = (_lastPointer - _clickStartPointer).Length();
                const float clickThreshold = 5.0f; // pixels

                if (dragDistance < clickThreshold && SelectionMode == SelectionMode.Single)
                {
                    // This was a click, not a drag - clear selection if not shift-clicking
                    bool shiftHeld = (args.Event.State & Gdk.ModifierType.ShiftMask) != 0;
                    if (!shiftHeld && SelectedCellIDs.Count > 0)
                    {
                        SelectedCellIDs.Clear();
                        CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(SelectedCellIDs.ToList()));
                        QueueDraw();
                    }
                }
            }

            _isDragging = false;
            _isCameraRotating = false;
            _isCameraPanning = false;
            args.RetVal = true;
        };

        KeyPressEvent += (_, args) =>
        {
            bool ctrlPressed = (args.Event.State & ModifierType.ControlMask) != 0;
            bool isCtrlA = args.Event.KeyValue == (uint)GdkKey.a || args.Event.KeyValue == (uint)GdkKey.A;

            if (ctrlPressed && isCtrlA)
            {
                SelectAllCells();
                args.RetVal = true;
            }
        };
    }

    public void LoadFromPhysicoChem(PhysicoChemMesh mesh, PhysicoChemDataset? dataset = null)
    {
        _points.Clear();
        _edges.Clear();
        _faces.Clear();
        _cells.Clear();
        _activePhysicoMesh = mesh;
        _activePhysicoDataset = dataset;

        // Calculate mesh bounds for centering
        var minBounds = new Vector3(float.MaxValue);
        var maxBounds = new Vector3(float.MinValue);

        var cellIndex = new Dictionary<string, int>();

        _vertexMap.Clear();

        foreach (var (id, cell) in mesh.Cells.OrderBy(c => c.Key))
        {
            var center = new Vector3((float)cell.Center.X, (float)cell.Center.Y, (float)cell.Center.Z);
            int startIdx = _points.Count;

            _cells.Add(new CellInfo
            {
                ID = id,
                Cell = cell,
                VertexStartIndex = startIdx,
                FaceStartIndex = _faces.Count,
                Center = center
            });

            // Check if this is a Voronoi cell (has vertices) or a normal cell (uses volume)
            if (cell.Vertices != null && cell.Vertices.Count > 0)
            {
                // Voronoi cell - use vertices
                var cellVertexIndices = new List<int>();

                foreach (var vertex in cell.Vertices)
                {
                    if (!_vertexMap.TryGetValue(vertex, out var index))
                    {
                        index = _points.Count;
                        _points.Add(vertex);
                        _vertexMap[vertex] = index;

                        minBounds = Vector3.Min(minBounds, vertex);
                        maxBounds = Vector3.Max(maxBounds, vertex);
                    }
                    cellVertexIndices.Add(index);
                }

                int half = cellVertexIndices.Count / 2;

                // Top and bottom faces
                for (int i = 1; i < half - 1; i++)
                {
                    _faces.Add(new List<int> { cellVertexIndices[0], cellVertexIndices[i], cellVertexIndices[i + 1] });
                    _faces.Add(new List<int> { cellVertexIndices[half], cellVertexIndices[half + i], cellVertexIndices[half + i + 1] });
                }

                // Side faces
                for (int i = 0; i < half; i++)
                {
                    int i2 = (i + 1) % half;
                    _faces.Add(new List<int> { cellVertexIndices[i], cellVertexIndices[i2], cellVertexIndices[i2 + half], cellVertexIndices[i + half] });
                }

                // Edges for Voronoi cells
                for (int i = 0; i < cell.Vertices.Count; i++)
                {
                    var p1 = cell.Vertices[i];
                    var p2 = cell.Vertices[(i + 1) % (cell.Vertices.Count/2) + (i < cell.Vertices.Count/2 ? 0 : cell.Vertices.Count/2)];
                    if (_vertexMap.TryGetValue(p1, out var idx1) && _vertexMap.TryGetValue(p2, out var idx2))
                    {
                        var edge = idx1 < idx2 ? (idx1, idx2) : (idx2, idx1);
                        if (!_edges.Contains(edge))
                            _edges.Add(edge);
                    }
                }
                for (int i = 0; i < half; i++)
                {
                    if (_vertexMap.TryGetValue(cell.Vertices[i], out var idx1) && _vertexMap.TryGetValue(cell.Vertices[i+half], out var idx2))
                    {
                        var edge = idx1 < idx2 ? (idx1, idx2) : (idx2, idx1);
                        if (!_edges.Contains(edge))
                            _edges.Add(edge);
                    }
                }
            }
            else
            {
                // Normal cell - create a box based on volume
                float size = (float)Math.Pow(cell.Volume, 1.0 / 3.0) * 0.5f;

                cellIndex[id] = startIdx;

                // Generate 8 corners of the box
                for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                for (int k = 0; k < 2; k++)
                {
                    float x = center.X + (i == 0 ? -size : size);
                    float y = center.Y + (j == 0 ? -size : size);
                    float z = center.Z + (k == 0 ? -size : size);
                    var point = new Vector3(x, y, z);
                    _points.Add(point);

                    // Update bounds for centering
                    minBounds = Vector3.Min(minBounds, point);
                    maxBounds = Vector3.Max(maxBounds, point);
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

        // Center the mesh in the viewport
        if (_points.Count > 0)
        {
            _meshCenter = (minBounds + maxBounds) * 0.5f;
            _panOffset = Vector2.Zero; // Reset pan when loading new mesh
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

        // Center the mesh in the viewport
        if (_points.Count > 0)
        {
            var minBounds = new Vector3(float.MaxValue);
            var maxBounds = new Vector3(float.MinValue);
            foreach (var point in _points)
            {
                minBounds = Vector3.Min(minBounds, point);
                maxBounds = Vector3.Max(maxBounds, point);
            }
            _meshCenter = (minBounds + maxBounds) * 0.5f;
            _panOffset = Vector2.Zero; // Reset pan when loading new mesh
        }

        QueueDraw();
    }

    public void Clear()
    {
        _points.Clear();
        _edges.Clear();
        _faces.Clear();
        _cells.Clear();
        _activeMesh = null;
        _activePhysicoMesh = null;
        _selectedIndex = null;
        _hoverIndex = null;
        _panOffset = Vector2.Zero;
        _meshCenter = Vector3.Zero;
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

        // Calculate bounds WITHOUT zoom for stable base scale
        foreach (var point in _points)
        {
            var rotated = Vector3.Transform(point - _meshCenter, rotation);
            min = Vector3.Min(min, rotated);
            max = Vector3.Max(max, rotated);
            projected.Add(new Vector2(rotated.X, rotated.Y));
            _projected.Add(new Vector2(rotated.X, rotated.Y));
        }

        var size = Vector3.Max(max - min, new Vector3(1, 1, 1));
        // Apply zoom to the scale, not to the coordinates
        var scale = MathF.Min(width / size.X, height / size.Y) * 0.45f * _zoom;
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
                if (cellInfo?.Cell.IsVisible == false)
                    continue;
                if (ShowIsosurface && cellInfo != null)
                {
                    double val = GetValueForColorMode(cellInfo.Cell);
                    bool visible = IsosurfaceGreaterThan ? val >= IsosurfaceThreshold : val <= IsosurfaceThreshold;
                    if (!visible) continue;
                }

                float avgZ = 0;
                foreach (var idx in face)
                {
                    var rotated = Vector3.Transform(_points[idx] - _meshCenter, rotation);
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
                if (cellInfo?.Cell.IsVisible == false)
                    continue;
                bool isSelected = cellInfo != null && SelectedCellIDs.Contains(cellInfo.ID);
                bool isHovered = cellInfo != null && _hoveredCellIndex.HasValue &&
                                _hoveredCellIndex.Value < _cells.Count &&
                                _cells[_hoveredCellIndex.Value].ID == cellInfo.ID;
                bool isInactive = cellInfo?.Cell.IsActive == false;

                var firstPoint = Project(projected[face[0]], min, scale, width, height);
                
                // Draw selection glow FIRST (outer glow for selected cells)
                if (isSelected)
                {
                    cr.MoveTo(firstPoint.X, firstPoint.Y);
                    for (int i = 1; i < face.Count; i++)
                    {
                        var point = Project(projected[face[i]], min, scale, width, height);
                        cr.LineTo(point.X, point.Y);
                    }
                    cr.ClosePath();
                    
                    // Outer glow - thick semi-transparent orange
                    cr.SetSourceRGBA(1.0, 0.6, 0.0, 0.4);
                    cr.LineWidth = 6.0;
                    cr.StrokePreserve();
                    
                    // Middle glow
                    cr.SetSourceRGBA(1.0, 0.8, 0.2, 0.6);
                    cr.LineWidth = 3.0;
                    cr.Stroke();
                }

                // Draw the face fill
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
                if (RenderMode == RenderMode.SolidWireframe || isSelected || isHovered)
                {
                    if (isSelected)
                    {
                        cr.SetSourceRGB(1.0, 0.9, 0.3); // Bright yellow for selection
                        cr.LineWidth = 2.5;
                    }
                    else if (isHovered)
                    {
                        cr.SetSourceRGB(0.4, 0.9, 0.9); // Cyan for hover
                        cr.LineWidth = 1.5;
                    }
                    else
                    {
                        cr.SetSourceRGB(0.2, 0.4, 0.7);
                        cr.LineWidth = 0.8;
                    }
                    cr.Stroke();
                }
                else
                {
                    cr.NewPath(); // Clear the path without stroking
                }
            }
        }

        // Render edges (wireframe mode)
        if ((RenderMode == RenderMode.Wireframe || RenderMode == RenderMode.SolidWireframe) && _cells.Count > 0)
        {
            cr.SetSourceRGB(0.25, 0.55, 0.95);
            cr.LineWidth = 1.2;

            foreach (var cellInfo in _cells)
            {
                if (!cellInfo.Cell.IsVisible) continue;
                DrawCellEdges(cr, projected, cellInfo, min, scale, width, height);
            }
            
            // Highlight selected cells in wireframe mode
            if (SelectedCellIDs.Count > 0 && RenderMode == RenderMode.Wireframe)
            {
                foreach (var cellInfo in _cells)
                {
                    if (!SelectedCellIDs.Contains(cellInfo.ID)) continue;
                    if (!cellInfo.Cell.IsVisible) continue;
                    
                    int startIdx = cellInfo.VertexStartIndex;
                    
                    // Draw glow effect first
                    cr.SetSourceRGBA(1.0, 0.6, 0.0, 0.5);
                    cr.LineWidth = 5.0;
                    DrawCellEdges(cr, projected, cellInfo, min, scale, width, height);
                    
                    // Draw bright outline
                    cr.SetSourceRGB(1.0, 0.9, 0.3);
                    cr.LineWidth = 2.5;
                    DrawCellEdges(cr, projected, cellInfo, min, scale, width, height);
                }
            }
            
            // Highlight hovered cell in wireframe mode
            if (_hoveredCellIndex.HasValue && _hoveredCellIndex.Value < _cells.Count && RenderMode == RenderMode.Wireframe)
            {
                var cellInfo = _cells[_hoveredCellIndex.Value];
                if (cellInfo.Cell.IsVisible && !SelectedCellIDs.Contains(cellInfo.ID))
                {
                    cr.SetSourceRGBA(0.3, 0.95, 0.95, 0.8);
                    cr.LineWidth = 2.0;
                    DrawCellEdges(cr, projected, cellInfo, min, scale, width, height);
                }
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

        // Draw HUD
        DrawHUD(cr, width, height);

        // Draw rectangle selection
        if (_isRectangleSelecting)
        {
            float minX = MathF.Min(_rectangleStart.X, _rectangleEnd.X);
            float maxX = MathF.Max(_rectangleStart.X, _rectangleEnd.X);
            float minY = MathF.Min(_rectangleStart.Y, _rectangleEnd.Y);
            float maxY = MathF.Max(_rectangleStart.Y, _rectangleEnd.Y);
            float rectWidth = maxX - minX;
            float rectHeight = maxY - minY;

            // Draw semi-transparent fill
            cr.SetSourceRGBA(0.3, 0.6, 1.0, 0.15);
            cr.Rectangle(minX, minY, rectWidth, rectHeight);
            cr.Fill();

            // Draw border
            cr.SetSourceRGBA(0.4, 0.7, 1.0, 0.8);
            cr.LineWidth = 2.0;
            cr.Rectangle(minX, minY, rectWidth, rectHeight);
            cr.Stroke();

            // Draw corner indicators
            float cornerSize = 8f;
            cr.SetSourceRGBA(0.5, 0.8, 1.0, 1.0);
            cr.LineWidth = 2.5;

            // Top-left corner
            cr.MoveTo(minX, minY + cornerSize);
            cr.LineTo(minX, minY);
            cr.LineTo(minX + cornerSize, minY);
            cr.Stroke();

            // Top-right corner
            cr.MoveTo(maxX - cornerSize, minY);
            cr.LineTo(maxX, minY);
            cr.LineTo(maxX, minY + cornerSize);
            cr.Stroke();

            // Bottom-left corner
            cr.MoveTo(minX, maxY - cornerSize);
            cr.LineTo(minX, maxY);
            cr.LineTo(minX + cornerSize, maxY);
            cr.Stroke();

            // Bottom-right corner
            cr.MoveTo(maxX - cornerSize, maxY);
            cr.LineTo(maxX, maxY);
            cr.LineTo(maxX, maxY - cornerSize);
            cr.Stroke();
        }

        return base.OnDrawn(cr);
    }

    private void DrawHUD(Cairo.Context cr, int width, int height)
    {
        cr.SelectFontFace("Sans", Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
        
        // Calculate HUD dimensions
        int hudWidth = 200;
        int lineHeight = 18;
        int padding = 8;
        int hudX = 8;
        int hudY = 8;
        
        // Count lines needed
        int lines = 3; // Camera, Mesh, Render mode
        if (SelectedCellIDs.Count > 0) lines++;
        if (_hoveredCellIndex.HasValue) lines++;
        
        int hudHeight = lines * lineHeight + padding * 2;
        
        // Draw semi-transparent background
        cr.SetSourceRGBA(0.05, 0.05, 0.1, 0.75);
        DrawRoundedRect(cr, hudX, hudY, hudWidth, hudHeight, 6);
        cr.Fill();
        
        // Draw border
        cr.SetSourceRGBA(0.3, 0.4, 0.6, 0.6);
        cr.LineWidth = 1;
        DrawRoundedRect(cr, hudX, hudY, hudWidth, hudHeight, 6);
        cr.Stroke();
        
        int textX = hudX + padding;
        int textY = hudY + padding + 12;
        
        cr.SetFontSize(11);
        
        // Camera info
        cr.SetSourceRGB(0.7, 0.8, 0.9);
        cr.MoveTo(textX, textY);
        cr.ShowText($"⟳ Yaw {_yaw:F0}°  Pitch {_pitch:F0}°  ×{_zoom:F1}");
        textY += lineHeight;
        
        // Mesh info
        cr.SetSourceRGB(0.6, 0.8, 1.0);
        cr.MoveTo(textX, textY);
        cr.ShowText($"◈ Cells: {_cells.Count}  Edges: {_edges.Count}");
        textY += lineHeight;
        
        // Render mode
        cr.SetSourceRGB(0.6, 0.7, 0.8);
        cr.MoveTo(textX, textY);
        string modeStr = RenderMode switch
        {
            RenderMode.Wireframe => "Wireframe",
            RenderMode.Solid => "Solid",
            RenderMode.SolidWireframe => "Solid+Wire",
            _ => "Unknown"
        };
        cr.ShowText($"◉ Mode: {modeStr}");
        textY += lineHeight;
        
        // Selection info
        if (SelectedCellIDs.Count > 0)
        {
            cr.SetSourceRGB(1.0, 0.75, 0.25);
            cr.MoveTo(textX, textY);
            cr.ShowText($"▸ Selected: {SelectedCellIDs.Count} cell{(SelectedCellIDs.Count > 1 ? "s" : "")}");
            textY += lineHeight;
        }
        
        // Hover info
        if (_hoveredCellIndex.HasValue && _hoveredCellIndex.Value < _cells.Count)
        {
            var hoveredCell = _cells[_hoveredCellIndex.Value];
            cr.SetSourceRGB(0.5, 0.9, 0.9);
            cr.MoveTo(textX, textY);
            string cellName = hoveredCell.ID.Length > 18 ? hoveredCell.ID[..15] + "..." : hoveredCell.ID;
            cr.ShowText($"◌ Hover: {cellName}");
        }
        
        // Draw controls hint at bottom
        int hintY = height - 28;
        int hintHeight = 22;
        int hintWidth = 380;
        int hintX = (width - hintWidth) / 2;
        
        // Background for hint
        cr.SetSourceRGBA(0.05, 0.05, 0.1, 0.7);
        DrawRoundedRect(cr, hintX, hintY, hintWidth, hintHeight, 4);
        cr.Fill();
        
        cr.SetSourceRGBA(0.3, 0.4, 0.5, 0.5);
        cr.LineWidth = 1;
        DrawRoundedRect(cr, hintX, hintY, hintWidth, hintHeight, 4);
        cr.Stroke();
        
        cr.SetFontSize(10);
        cr.SetSourceRGB(0.6, 0.65, 0.7);
        cr.MoveTo(hintX + 8, hintY + 15);
        cr.ShowText("LMB: Select  Shift+LMB: Multi-select  MMB/RMB: Pan  Scroll: Zoom  Alt+LMB: Rotate");
    }

    private static void DrawRoundedRect(Cairo.Context cr, double x, double y, double w, double h, double r)
    {
        cr.MoveTo(x + r, y);
        cr.LineTo(x + w - r, y);
        cr.Arc(x + w - r, y + r, r, -Math.PI / 2, 0);
        cr.LineTo(x + w, y + h - r);
        cr.Arc(x + w - r, y + h - r, r, 0, Math.PI / 2);
        cr.LineTo(x + r, y + h);
        cr.Arc(x + r, y + h - r, r, Math.PI / 2, Math.PI);
        cr.LineTo(x, y + r);
        cr.Arc(x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
        cr.ClosePath();
    }

    private Vector2 Project(Vector2 v, Vector3 min, float scale, int width, int height)
    {
        // Project from rotated coordinates to screen, apply pan offset
        var x = v.X * scale + width / 2f + _panOffset.X;
        var y = height / 2f - v.Y * scale + _panOffset.Y;
        return new Vector2(x, y);
    }

    private void DrawCellEdges(Cairo.Context cr, List<Vector2> projected, CellInfo cellInfo, Vector3 min, float scale, int width, int height)
    {
        var vertices = cellInfo.Cell.Vertices;

        // Check if this is a Voronoi cell or a normal box cell
        if (vertices != null && vertices.Count > 0)
        {
            // Voronoi cell - draw edges from vertices
            for (int i = 0; i < vertices.Count; i++)
            {
                var p1 = vertices[i];
                var p2 = vertices[(i + 1) % (vertices.Count / 2) + (i < vertices.Count / 2 ? 0 : vertices.Count / 2)];

                if (_vertexMap.TryGetValue(p1, out var idx1) && _vertexMap.TryGetValue(p2, out var idx2))
                {
                    DrawEdgeLine(cr, projected, idx1, idx2, min, scale, width, height);
                }
            }

            int half = vertices.Count / 2;
            for (int i = 0; i < half; i++)
            {
                if (_vertexMap.TryGetValue(vertices[i], out var idx1) && _vertexMap.TryGetValue(vertices[i + half], out var idx2))
                {
                    DrawEdgeLine(cr, projected, idx1, idx2, min, scale, width, height);
                }
            }
        }
        else
        {
            // Normal box cell - draw the 12 edges of a box
            int startIdx = cellInfo.VertexStartIndex;

            // Bottom face edges
            DrawEdgeLine(cr, projected, startIdx + 0, startIdx + 1, min, scale, width, height);
            DrawEdgeLine(cr, projected, startIdx + 1, startIdx + 3, min, scale, width, height);
            DrawEdgeLine(cr, projected, startIdx + 3, startIdx + 2, min, scale, width, height);
            DrawEdgeLine(cr, projected, startIdx + 2, startIdx + 0, min, scale, width, height);

            // Top face edges
            DrawEdgeLine(cr, projected, startIdx + 4, startIdx + 5, min, scale, width, height);
            DrawEdgeLine(cr, projected, startIdx + 5, startIdx + 7, min, scale, width, height);
            DrawEdgeLine(cr, projected, startIdx + 7, startIdx + 6, min, scale, width, height);
            DrawEdgeLine(cr, projected, startIdx + 6, startIdx + 4, min, scale, width, height);

            // Vertical edges
            DrawEdgeLine(cr, projected, startIdx + 0, startIdx + 4, min, scale, width, height);
            DrawEdgeLine(cr, projected, startIdx + 1, startIdx + 5, min, scale, width, height);
            DrawEdgeLine(cr, projected, startIdx + 2, startIdx + 6, min, scale, width, height);
            DrawEdgeLine(cr, projected, startIdx + 3, startIdx + 7, min, scale, width, height);
        }
    }

    private void DrawEdgeLine(Cairo.Context cr, List<Vector2> projected, int idx1, int idx2, Vector3 min, float scale, int width, int height)
    {
        if (idx1 >= projected.Count || idx2 >= projected.Count) return;
        var p1 = Project(projected[idx1], min, scale, width, height);
        var p2 = Project(projected[idx2], min, scale, width, height);
        cr.MoveTo(p1.X, p1.Y);
        cr.LineTo(p2.X, p2.Y);
        cr.Stroke();
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

        // Check each cell's actual center (not first vertex)
        float minDistance = 25f; // Max click distance - reduced from 50f
        int? closestCell = null;

        var rotation = Matrix4x4.CreateFromYawPitchRoll(
            MathF.PI / 180f * _yaw,
            MathF.PI / 180f * _pitch,
            0f);

        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (!cell.Cell.IsVisible)
                continue;
            // Project the actual cell center, not a vertex
            var rotatedCenter = Vector3.Transform(cell.Center - _meshCenter, rotation);
            var projectedCenter = new Vector2(rotatedCenter.X, rotatedCenter.Y);
            var screenPos = Project(projectedCenter, _lastMin, _lastScale, width, height);

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
        // This method is now less accurate due to triangulation, but can be approximated
        // by finding which cell's faces are currently being processed.
        int faceCounter = 0;
        foreach (var cell in _cells)
        {
            // Number of triangles for a convex polygon is (num_vertices - 2)
            int numTriangles = Math.Max(0, cell.Cell.Vertices.Count - 2);
            if (faceIndex >= faceCounter && faceIndex < faceCounter + numTriangles)
            {
                return cell;
            }
            faceCounter += numTriangles;
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

        // Selected cells are highlighted with bright orange fill
        if (isSelected)
            return (1.0, 0.6, 0.1, 0.85);

        // Hovered cells are highlighted in bright cyan
        if (isHovered)
            return (0.3, 0.95, 0.95, 0.75);

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

    public void SelectCellsInPlane(PlaneType plane, double position, double tolerance = 0.1, bool additive = false)
    {
        if (!additive)
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

            if (inPlane && cellInfo.Cell.IsActive && cellInfo.Cell.IsVisible)
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

    public void ToggleSelectedCellsVisible()
    {
        if (_activePhysicoMesh == null) return;

        foreach (var cellId in SelectedCellIDs)
        {
            if (_activePhysicoMesh.Cells.TryGetValue(cellId, out var cell))
            {
                cell.IsVisible = !cell.IsVisible;
            }
        }

        SelectedCellIDs.RemoveWhere(id => _activePhysicoMesh.Cells.TryGetValue(id, out var cell) && !cell.IsVisible);
        CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(SelectedCellIDs.ToList()));
        QueueDraw();
    }

    public void SetSelectedCellsVisible(bool isVisible)
    {
        if (_activePhysicoMesh == null) return;

        foreach (var cellId in SelectedCellIDs)
        {
            if (_activePhysicoMesh.Cells.TryGetValue(cellId, out var cell))
            {
                cell.IsVisible = isVisible;
            }
        }

        if (!isVisible)
        {
            SelectedCellIDs.Clear();
            CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(new List<string>()));
        }
        else
        {
            CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(SelectedCellIDs.ToList()));
        }
        QueueDraw();
    }

    public void ShowAllCells()
    {
        if (_activePhysicoMesh == null) return;

        foreach (var cell in _activePhysicoMesh.Cells.Values)
        {
            cell.IsVisible = true;
        }
        QueueDraw();
    }

    public void ClearSelection()
    {
        SelectedCellIDs.Clear();
        CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(new List<string>()));
        QueueDraw();
    }

    public void SelectAllCells()
    {
        if (_cells.Count == 0)
            return;

        SelectedCellIDs.Clear();

        foreach (var cell in _cells)
        {
            if (cell.Cell.IsVisible)
                SelectedCellIDs.Add(cell.ID);
        }

        CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(SelectedCellIDs.ToList()));
        QueueDraw();
    }

    private void SelectCellsInRectangle(Vector2 start, Vector2 end)
    {
        if (_cells.Count == 0) return;

        // Calculate rectangle bounds
        float minX = MathF.Min(start.X, end.X);
        float maxX = MathF.Max(start.X, end.X);
        float minY = MathF.Min(start.Y, end.Y);
        float maxY = MathF.Max(start.Y, end.Y);

        // Get viewport dimensions
        var allocation = Allocation;
        var width = allocation.Width;
        var height = allocation.Height;

        // Create rotation matrix
        var rotation = Matrix4x4.CreateFromYawPitchRoll(
            MathF.PI / 180f * _yaw,
            MathF.PI / 180f * _pitch,
            0f);

        // Calculate scale and min for projection
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var point in _points)
        {
            var rotated = Vector3.Transform(point - _meshCenter, rotation);
            min = Vector3.Min(min, rotated);
            max = Vector3.Max(max, rotated);
        }
        var size = Vector3.Max(max - min, new Vector3(1, 1, 1));
        var scale = MathF.Min(width / size.X, height / size.Y) * 0.45f * _zoom;

        // Sort cells by Z-depth (front to back)
        var cellsWithDepth = new List<(CellInfo cell, float depth, Vector2 screenPos)>();
        foreach (var cellInfo in _cells)
        {
            if (!cellInfo.Cell.IsVisible)
                continue;
            // Transform cell center to view space
            var rotatedCenter = Vector3.Transform(cellInfo.Center - _meshCenter, rotation);
            var projectedCenter = new Vector2(rotatedCenter.X, rotatedCenter.Y);
            var screenPos = Project(projectedCenter, min, scale, width, height);

            // Check if within rectangle
            if (screenPos.X >= minX && screenPos.X <= maxX && screenPos.Y >= minY && screenPos.Y <= maxY)
            {
                // Store cell with its Z-depth (rotatedCenter.Z is the depth)
                cellsWithDepth.Add((cellInfo, rotatedCenter.Z, screenPos));
            }
        }

        // Sort by depth (front to back - higher Z values are closer to camera)
        cellsWithDepth.Sort((a, b) => b.depth.CompareTo(a.depth));

        // Select only the frontmost cells (visible/surface cells)
        // We use a spatial grid to determine which cells occlude others
        var selectedCells = new HashSet<string>();
        var occupiedPositions = new HashSet<(int, int)>();

        // Grid cell size for occlusion detection (in screen pixels)
        const float gridSize = 15f;

        foreach (var (cellInfo, depth, screenPos) in cellsWithDepth)
        {
            // Calculate grid position
            int gridX = (int)(screenPos.X / gridSize);
            int gridY = (int)(screenPos.Y / gridSize);
            var gridPos = (gridX, gridY);

            // If this grid position is already occupied by a closer cell, skip
            if (occupiedPositions.Contains(gridPos))
                continue;

            // This cell is visible - add it to selection
            if (cellInfo.Cell.IsActive && cellInfo.Cell.IsVisible)
            {
                selectedCells.Add(cellInfo.ID);
                occupiedPositions.Add(gridPos);
            }
        }

        // Update selection
        SelectedCellIDs.Clear();
        foreach (var id in selectedCells)
            SelectedCellIDs.Add(id);

        CellSelectionChanged?.Invoke(this, new CellSelectionEventArgs(SelectedCellIDs.ToList()));
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
    Rectangle,
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
