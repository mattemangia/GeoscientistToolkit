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

        var cellIndex = new Dictionary<string, int>();
        foreach (var (id, cell) in mesh.Cells.OrderBy(c => c.Key))
        {
            cellIndex[id] = _points.Count;
            _points.Add(new Vector3((float)cell.Center.X, (float)cell.Center.Y, (float)cell.Center.Z));
        }

        foreach (var (cell1, cell2) in mesh.Connections)
        {
            if (cellIndex.TryGetValue(cell1, out var start) && cellIndex.TryGetValue(cell2, out var end))
                _edges.Add((start, end));
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

        cr.SetSourceRGB(0.25, 0.55, 0.95);
        cr.LineWidth = 1.2;

        if (_edges.Count > 0)
        {
            foreach (var edge in _edges)
            {
                var start = Project(projected[edge.Start], min, scale, width, height);
                var end = Project(projected[edge.End], min, scale, width, height);
                cr.MoveTo(start.X, start.Y);
                cr.LineTo(end.X, end.Y);
                cr.Stroke();
            }
        }
        else
        {
            foreach (var pt in projected)
            {
                var p = Project(pt, min, scale, width, height);
                cr.Arc(p.X, p.Y, 2, 0, Math.PI * 2);
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
        cr.ShowText($"Punti: {_points.Count} | Lati: {_edges.Count} | Yaw {_yaw:F0}° | Pitch {_pitch:F0}°");

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
