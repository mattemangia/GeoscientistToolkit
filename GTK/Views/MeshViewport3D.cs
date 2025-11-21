using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.PhysicoChem;
using Gtk;

namespace GeoscientistToolkit.Gtk;

/// <summary>
/// Lightweight 3D viewport built with GTK that mirrors the mesh behaviour of the ImGui renderer.
/// It renders PhysicoChem Voronoi cells or imported Mesh3D datasets using a simple orbit camera,
/// so users can sculpt meshes in a PetraSim/COMSOL-like view without leaving the GTK client.
/// </summary>
public class MeshViewport3D : DrawingArea
{
    private readonly List<Vector3> _points = new();
    private readonly List<(int Start, int End)> _edges = new();

    private float _yaw = 35f;
    private float _pitch = -20f;
    private float _zoom = 1.2f;

    public MeshViewport3D()
    {
        AddEvents((int)Gdk.EventMask.ScrollMask);
        ScrollEvent += (_, args) =>
        {
            _zoom = Math.Clamp(_zoom + (float)(-args.Event.DeltaY * 0.05), 0.1f, 8f);
            QueueDraw();
        };
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

        var projected = new List<Vector2>(_points.Count);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var point in _points)
        {
            var rotated = Vector3.Transform(point, rotation) * _zoom;
            min = Vector3.Min(min, rotated);
            max = Vector3.Max(max, rotated);
            projected.Add(new Vector2(rotated.X, rotated.Y));
        }

        var size = Vector3.Max(max - min, new Vector3(1, 1, 1));
        var scale = MathF.Min(width / size.X, height / size.Y) * 0.45f;

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
}
