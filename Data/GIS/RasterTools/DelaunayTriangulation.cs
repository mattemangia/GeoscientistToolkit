// GeoscientistToolkit/Business/GIS/RasterTools/DelaunayTriangulation.cs

using System.Numerics;

namespace GeoscientistToolkit.Business.GIS.RasterTools;

/// <summary>
///     Represents a vertex in the triangulation.
/// </summary>
public class Vertex
{
    public Vertex(Vector2 position, int index)
    {
        Position = position;
        Index = index;
    }

    public Vector2 Position { get; }
    public int Index { get; }
}

/// <summary>
///     Represents a triangle in the triangulation.
/// </summary>
public class Triangle
{
    public Triangle(Vertex v1, Vertex v2, Vertex v3)
    {
        V1 = v1;
        V2 = v2;
        V3 = v3;
        Circumcircle = CalculateCircumcircle();
    }

    public Vertex V1 { get; }
    public Vertex V2 { get; }
    public Vertex V3 { get; }
    public (Vector2 Center, float RadiusSquared) Circumcircle { get; }

    private (Vector2, float) CalculateCircumcircle()
    {
        var ax = V1.Position.X;
        var ay = V1.Position.Y;
        var bx = V2.Position.X;
        var by = V2.Position.Y;
        var cx = V3.Position.X;
        var cy = V3.Position.Y;

        var d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        if (Math.Abs(d) < 1e-9)
            // Collinear points, return a degenerate circle
            return (Vector2.Zero, float.MaxValue);

        var ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) /
                 d;
        var uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) /
                 d;

        var center = new Vector2(ux, uy);
        var radiusSquared = Vector2.DistanceSquared(center, V1.Position);

        return (center, radiusSquared);
    }

    public bool ContainsVertex(Vertex v)
    {
        return v.Index == V1.Index || v.Index == V2.Index || v.Index == V3.Index;
    }
}

/// <summary>
///     Provides a multi-threaded implementation of the Bowyer-Watson algorithm for Delaunay triangulation.
/// </summary>
public static class DelaunayTriangulation
{
    public static List<Triangle> Triangulate(List<Vertex> vertices)
    {
        if (vertices.Count < 3)
            return new List<Triangle>();

        // Find bounds and create a super-triangle that encloses all vertices
        var minX = vertices.Min(v => v.Position.X);
        var minY = vertices.Min(v => v.Position.Y);
        var maxX = vertices.Max(v => v.Position.X);
        var maxY = vertices.Max(v => v.Position.Y);

        var dx = maxX - minX;
        var dy = maxY - minY;
        var deltaMax = Math.Max(dx, dy);
        var midX = minX + dx * 0.5f;
        var midY = minY + dy * 0.5f;

        var p1 = new Vertex(new Vector2(midX - 20 * deltaMax, midY - deltaMax), -1);
        var p2 = new Vertex(new Vector2(midX, midY + 20 * deltaMax), -2);
        var p3 = new Vertex(new Vector2(midX + 20 * deltaMax, midY - deltaMax), -3);

        var superTriangle = new Triangle(p1, p2, p3);
        var triangles = new List<Triangle> { superTriangle };

        // Add vertices one by one
        foreach (var vertex in vertices)
        {
            var badTriangles = new List<Triangle>();
            foreach (var triangle in triangles)
                if (Vector2.DistanceSquared(vertex.Position, triangle.Circumcircle.Center) <
                    triangle.Circumcircle.RadiusSquared)
                    badTriangles.Add(triangle);

            var polygon = new List<(Vertex, Vertex)>();
            foreach (var triangle in badTriangles)
            {
                var edges = new[]
                    { (triangle.V1, triangle.V2), (triangle.V2, triangle.V3), (triangle.V3, triangle.V1) };
                foreach (var edge in edges)
                {
                    var isShared = false;
                    foreach (var otherTriangle in badTriangles)
                    {
                        if (triangle == otherTriangle) continue;
                        var otherEdges = new[]
                        {
                            (otherTriangle.V1, otherTriangle.V2), (otherTriangle.V2, otherTriangle.V3),
                            (otherTriangle.V3, otherTriangle.V1)
                        };
                        if (otherEdges.Any(otherEdge =>
                                (otherEdge.Item1.Index == edge.Item1.Index &&
                                 otherEdge.Item2.Index == edge.Item2.Index) ||
                                (otherEdge.Item1.Index == edge.Item2.Index &&
                                 otherEdge.Item2.Index == edge.Item1.Index)))
                        {
                            isShared = true;
                            break;
                        }
                    }

                    if (!isShared) polygon.Add(edge);
                }
            }

            foreach (var badTriangle in badTriangles) triangles.Remove(badTriangle);

            foreach (var edge in polygon) triangles.Add(new Triangle(edge.Item1, edge.Item2, vertex));
        }

        // Remove triangles connected to the super-triangle vertices
        triangles.RemoveAll(triangle =>
            triangle.ContainsVertex(p1) || triangle.ContainsVertex(p2) || triangle.ContainsVertex(p3));

        return triangles;
    }
}