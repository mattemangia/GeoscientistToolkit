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
/// so users can sculpt meshes in a PetraSim/COMSOL-like view without leaving the GTK client.
/// </summary>
public class MeshViewport3D : DrawingArea
{
    private readonly List<Vector3> _points = new();
    private readonly List<(int Start, int End)> _edges = new();
    private readonly List<List<int>> _faces = new();
    private readonly List<Vector2> _projected = new();
    private Mesh3DDataset? _activeMesh;
    private int? _selectedIndex;
    private int? _hoverIndex;
    private bool _isDragging;
    private Matrix4x4 _lastRotation = Matrix4x4.Identity;
    private Vector3 _lastMin = Vector3.Zero;
    private float _lastScale = 1f;
    private Vector2 _lastPointer;

    private float _yaw = 35f;
    private float _pitch = -20f;
    private float _zoom = 1.2f;

    public RenderMode RenderMode { get; set; } = RenderMode.Wireframe;

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
            _hoverIndex = FindClosestPoint(_lastPointer, 18f);
            if (_isDragging && _selectedIndex.HasValue && _activeMesh != null)
            {
                var delta = _lastPointer - previousPointer;
                ApplyDragDelta(_selectedIndex.Value, delta);
                QueueDraw();
            }
        };

        ButtonReleaseEvent += (_, _) => _isDragging = false;
    }

    public void LoadFromPhysicoChem(PhysicoChemMesh mesh)
    {
        _points.Clear();
        _edges.Clear();
        _faces.Clear();

        var cellIndex = new Dictionary<string, int>();

        // For each cell, create a box based on its volume
        foreach (var (id, cell) in mesh.Cells.OrderBy(c => c.Key))
        {
            // Calculate box dimensions from volume (assume cubic cells)
            float size = (float)Math.Pow(cell.Volume, 1.0 / 3.0) * 0.5f;

            var center = new Vector3((float)cell.Center.X, (float)cell.Center.Y, (float)cell.Center.Z);

            // Store starting index for this cell's vertices
            int startIdx = _points.Count;
            cellIndex[id] = startIdx;

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
            var faceDepths = new List<(List<int> face, float depth)>();
            foreach (var face in _faces)
            {
                float avgZ = 0;
                foreach (var idx in face)
                {
                    var rotated = Vector3.Transform(_points[idx], rotation) * _zoom;
                    avgZ += rotated.Z;
                }
                avgZ /= face.Count;
                faceDepths.Add((face, avgZ));
            }
            faceDepths.Sort((a, b) => a.depth.CompareTo(b.depth)); // Draw back to front

            // Draw filled faces
            foreach (var (face, _) in faceDepths)
            {
                if (face.Count < 3) continue;

                var firstPoint = Project(projected[face[0]], min, scale, width, height);
                cr.MoveTo(firstPoint.X, firstPoint.Y);

                for (int i = 1; i < face.Count; i++)
                {
                    var point = Project(projected[face[i]], min, scale, width, height);
                    cr.LineTo(point.X, point.Y);
                }
                cr.ClosePath();

                // Fill with semi-transparent color
                cr.SetSourceRGBA(0.3, 0.6, 0.9, 0.6);
                cr.FillPreserve();

                // Outline the face
                if (RenderMode == RenderMode.SolidWireframe)
                {
                    cr.SetSourceRGB(0.2, 0.4, 0.7);
                    cr.LineWidth = 0.8;
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
