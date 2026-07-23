// GAIA/Data/Mesh3D/QuadricMeshSimplifier.cs

using System.Numerics;
using GAIA.Util;

namespace GAIA.Data.Mesh3D;

/// <summary>
///     Mesh decimation with the Quadric Error Metric (Garland &amp; Heckbert, 1997). Iteratively
///     collapses the edge whose merge introduces the least squared distance to the original surface
///     planes, preserving overall shape and features far better than uniform vertex clustering.
///     Collapses that would flip a triangle are rejected, so the result stays consistent.
/// </summary>
public static class QuadricMeshSimplifier
{
    private readonly struct Edge
    {
        public readonly int I;
        public readonly int J;
        public readonly int VersionI;
        public readonly int VersionJ;

        public Edge(int i, int j, int versionI, int versionJ)
        {
            I = i;
            J = j;
            VersionI = versionI;
            VersionJ = versionJ;
        }
    }

    public static (List<Vector3> vertices, List<int> triangles) Simplify(
        List<Vector3> verticesIn, List<int> trianglesIn, float keepRatio,
        IProgress<(float progress, string message)> progress, float lo, float hi, CancellationToken token)
    {
        keepRatio = Math.Clamp(keepRatio, 0.01f, 1f);
        if (keepRatio >= 0.999f) return (verticesIn, trianglesIn);

        try
        {
            return Run(verticesIn, trianglesIn, keepRatio, progress, lo, hi, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Decimation is a best-effort refinement: never lose the mesh over it.
            Logger.LogWarning($"[QEM] Decimation failed, keeping full-resolution mesh: {ex.Message}");
            return (verticesIn, trianglesIn);
        }
    }

    private static (List<Vector3>, List<int>) Run(
        List<Vector3> verticesIn, List<int> trianglesIn, float keepRatio,
        IProgress<(float progress, string message)> progress, float lo, float hi, CancellationToken token)
    {
        var vertexCount = verticesIn.Count;
        var pos = verticesIn.ToArray();
        var vertAlive = new bool[vertexCount];
        var version = new int[vertexCount];
        var quadrics = new double[vertexCount][];
        for (var v = 0; v < vertexCount; v++) quadrics[v] = new double[10];

        var tri = trianglesIn.ToArray();
        var triCount = tri.Length / 3;
        var triAlive = new bool[triCount];
        var vertTris = new List<int>[vertexCount];

        var aliveTriangles = 0;
        for (var t = 0; t < triCount; t++)
        {
            int a = tri[t * 3], b = tri[t * 3 + 1], c = tri[t * 3 + 2];
            if (a == b || b == c || a == c) continue;

            var q = PlaneQuadric(pos[a], pos[b], pos[c]);
            if (q == null) continue; // degenerate area

            triAlive[t] = true;
            aliveTriangles++;
            foreach (var vi in stackalloc[] { a, b, c })
            {
                vertAlive[vi] = true;
                (vertTris[vi] ??= new List<int>()).Add(t);
                AddInto(quadrics[vi], q);
            }
        }

        var targetTriangles = Math.Max(4, (int)Math.Round(aliveTriangles * keepRatio));
        if (aliveTriangles <= targetTriangles)
            return (verticesIn, trianglesIn);

        // Seed the priority queue with every unique edge.
        var queue = new PriorityQueue<Edge, double>();
        var seen = new HashSet<long>();
        for (var t = 0; t < triCount; t++)
        {
            if (!triAlive[t]) continue;
            int a = tri[t * 3], b = tri[t * 3 + 1], c = tri[t * 3 + 2];
            TryQueueEdge(a, b);
            TryQueueEdge(b, c);
            TryQueueEdge(c, a);
        }

        void TryQueueEdge(int a, int b)
        {
            var lo = Math.Min(a, b);
            var hi = Math.Max(a, b);
            if (!seen.Add(((long)lo << 32) | (uint)hi)) return;
            var (_, cost) = EvaluateCollapse(quadrics[a], quadrics[b], pos[a], pos[b]);
            queue.Enqueue(new Edge(a, b, version[a], version[b]), cost);
        }

        var startTriangles = aliveTriangles;
        var progressStep = Math.Max(1, (startTriangles - targetTriangles) / 20);
        var collapses = 0;

        while (aliveTriangles > targetTriangles && queue.Count > 0)
        {
            var edge = queue.Dequeue();
            int i = edge.I, j = edge.J;

            // Skip stale entries: a vertex was already removed or moved since this edge was queued.
            if (!vertAlive[i] || !vertAlive[j] || i == j) continue;
            if (version[i] != edge.VersionI || version[j] != edge.VersionJ) continue;

            var merged = AddCopy(quadrics[i], quadrics[j]);
            var (target, _) = EvaluateCollapse(quadrics[i], quadrics[j], pos[i], pos[j]);

            if (!IsCollapseValid(i, j, target, pos, tri, triAlive, vertTris)) continue;

            // Rewire triangles from j onto i, dropping the ones that degenerate (shared edge i-j).
            var removed = 0;
            var jTris = vertTris[j];
            for (var idx = 0; idx < jTris.Count; idx++)
            {
                var t = jTris[idx];
                if (!triAlive[t]) continue;

                int a = tri[t * 3], b = tri[t * 3 + 1], c = tri[t * 3 + 2];
                var hasI = a == i || b == i || c == i;
                if (hasI)
                {
                    triAlive[t] = false;
                    removed++;
                    continue;
                }

                if (a == j) tri[t * 3] = i;
                if (b == j) tri[t * 3 + 1] = i;
                if (c == j) tri[t * 3 + 2] = i;
                vertTris[i].Add(t);
            }

            pos[i] = target;
            quadrics[i] = merged;
            vertAlive[j] = false;
            version[i]++;
            aliveTriangles -= removed;

            // Re-price every edge still incident to the merged vertex.
            var neighbours = new HashSet<int>();
            foreach (var t in vertTris[i])
            {
                if (!triAlive[t]) continue;
                int a = tri[t * 3], b = tri[t * 3 + 1], c = tri[t * 3 + 2];
                if (a != i && vertAlive[a]) neighbours.Add(a);
                if (b != i && vertAlive[b]) neighbours.Add(b);
                if (c != i && vertAlive[c]) neighbours.Add(c);
            }

            foreach (var n in neighbours)
            {
                var (_, cost) = EvaluateCollapse(quadrics[i], quadrics[n], pos[i], pos[n]);
                queue.Enqueue(new Edge(i, n, version[i], version[n]), cost);
            }

            if (++collapses % progressStep == 0)
            {
                token.ThrowIfCancellationRequested();
                var done = (startTriangles - aliveTriangles) / (float)Math.Max(1, startTriangles - targetTriangles);
                progress?.Report((lo + (hi - lo) * Math.Clamp(done, 0f, 1f),
                    $"Decimating... {aliveTriangles} triangles"));
            }
        }

        return Compact(pos, vertAlive, tri, triAlive);
    }

    private static bool IsCollapseValid(int i, int j, Vector3 target, Vector3[] pos,
        int[] tri, bool[] triAlive, List<int>[] vertTris)
    {
        // A collapse is rejected if any surviving incident triangle would flip or become degenerate.
        return CheckSide(vertTris[i]) && CheckSide(vertTris[j]);

        bool CheckSide(List<int> tris)
        {
            foreach (var t in tris)
            {
                if (!triAlive[t]) continue;
                int a = tri[t * 3], b = tri[t * 3 + 1], c = tri[t * 3 + 2];

                // Triangles containing both endpoints vanish on collapse; they cannot flip.
                var containsI = a == i || b == i || c == i;
                var containsJ = a == j || b == j || c == j;
                if (containsI && containsJ) continue;

                var pa = a == i || a == j ? target : pos[a];
                var pb = b == i || b == j ? target : pos[b];
                var pc = c == i || c == j ? target : pos[c];

                var oldN = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                var newN = Vector3.Cross(pb - pa, pc - pa);
                if (newN.LengthSquared() < 1e-12f) return false; // collapsed to a sliver
                if (Vector3.Dot(oldN, newN) <= 0f) return false; // orientation flipped
            }

            return true;
        }
    }

    private static (List<Vector3>, List<int>) Compact(Vector3[] pos, bool[] vertAlive, int[] tri, bool[] triAlive)
    {
        var remap = new int[pos.Length];
        Array.Fill(remap, -1);
        var vertices = new List<Vector3>();
        for (var v = 0; v < pos.Length; v++)
            if (vertAlive[v])
            {
                remap[v] = vertices.Count;
                vertices.Add(pos[v]);
            }

        var triangles = new List<int>();
        for (var t = 0; t < triAlive.Length; t++)
        {
            if (!triAlive[t]) continue;
            int a = remap[tri[t * 3]], b = remap[tri[t * 3 + 1]], c = remap[tri[t * 3 + 2]];
            if (a < 0 || b < 0 || c < 0 || a == b || b == c || a == c) continue;
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        return (vertices, triangles);
    }

    // ── Quadric maths (double precision) ───────────────────────────────────────
    private static double[] PlaneQuadric(Vector3 p0, Vector3 p1, Vector3 p2)
    {
        var normal = Vector3.Cross(p1 - p0, p2 - p0);
        var length = normal.Length();
        if (length < 1e-12f) return null;
        normal /= length;

        double a = normal.X, b = normal.Y, c = normal.Z;
        var d = -(a * p0.X + b * p0.Y + c * p0.Z);
        return new[]
        {
            a * a, a * b, a * c, a * d,
            b * b, b * c, b * d,
            c * c, c * d,
            d * d
        };
    }

    private static void AddInto(double[] target, double[] source)
    {
        for (var i = 0; i < 10; i++) target[i] += source[i];
    }

    private static double[] AddCopy(double[] x, double[] y)
    {
        var result = new double[10];
        for (var i = 0; i < 10; i++) result[i] = x[i] + y[i];
        return result;
    }

    private static double Error(double[] q, double x, double y, double z)
    {
        return q[0] * x * x + 2 * q[1] * x * y + 2 * q[2] * x * z + 2 * q[3] * x
               + q[4] * y * y + 2 * q[5] * y * z + 2 * q[6] * y
               + q[7] * z * z + 2 * q[8] * z
               + q[9];
    }

    private static (Vector3 position, double cost) EvaluateCollapse(double[] qi, double[] qj, Vector3 pi, Vector3 pj)
    {
        var q = AddCopy(qi, qj);

        // Optimal position minimises v^T Q v: solve the 3x3 sub-system A v = -b.
        var det =
            q[0] * (q[4] * q[7] - q[5] * q[5])
            - q[1] * (q[1] * q[7] - q[5] * q[2])
            + q[2] * (q[1] * q[5] - q[4] * q[2]);

        if (Math.Abs(det) > 1e-10)
        {
            var invDet = 1.0 / det;
            var bx = -q[3];
            var by = -q[6];
            var bz = -q[8];

            var x = invDet * (
                bx * (q[4] * q[7] - q[5] * q[5])
                - q[1] * (by * q[7] - q[5] * bz)
                + q[2] * (by * q[5] - q[4] * bz));
            var y = invDet * (
                q[0] * (by * q[7] - bz * q[5])
                - bx * (q[1] * q[7] - q[5] * q[2])
                + q[2] * (q[1] * bz - by * q[2]));
            var z = invDet * (
                q[0] * (q[4] * bz - by * q[5])
                - q[1] * (q[1] * bz - by * q[2])
                + bx * (q[1] * q[5] - q[4] * q[2]));

            var cost = Error(q, x, y, z);
            if (!double.IsNaN(cost) && !double.IsInfinity(cost))
                return (new Vector3((float)x, (float)y, (float)z), Math.Max(0, cost));
        }

        // Singular quadric: pick the cheapest of the two endpoints and the midpoint.
        var mid = (pi + pj) * 0.5f;
        var candidates = new[] { pi, pj, mid };
        var best = pi;
        var bestCost = double.MaxValue;
        foreach (var candidate in candidates)
        {
            var cost = Error(q, candidate.X, candidate.Y, candidate.Z);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = candidate;
            }
        }

        return (best, Math.Max(0, bestCost));
    }
}
