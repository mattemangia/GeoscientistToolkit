// GAIA/Data/Mesh3D/SurfaceNetsMesher.cs

using System.Collections.Concurrent;
using System.Numerics;
using GAIA.Data.VolumeData;
using GAIA.Util;

namespace GAIA.Data.Mesh3D;

/// <summary>Options controlling how a material label is turned into a mesh.</summary>
public struct MeshingOptions
{
    /// <summary>Volume sub-sampling before meshing (1 = full resolution). Higher = faster, coarser.</summary>
    public int Downsampling;

    /// <summary>When true, internal cavities are filled and only the outermost surface is meshed
    /// (a watertight external "shell") — useful when internal parts were also segmented and the model
    /// is destined for 3D printing.</summary>
    public bool ShellOnly;

    /// <summary>Number of Taubin (λ|μ) smoothing iterations. 0 disables smoothing.</summary>
    public int SmoothingIterations;

    /// <summary>Fraction of triangles to KEEP via QEM decimation, in (0, 1]. 1 = no decimation.</summary>
    public float DecimateKeepRatio;

    public static MeshingOptions Default => new()
    {
        Downsampling = 1,
        ShellOnly = false,
        SmoothingIterations = 3,
        DecimateKeepRatio = 1.0f
    };
}

/// <summary>
///     Turns a segmented material into a triangle mesh with a dual <b>Surface Nets</b> extractor.
///     Surface Nets is table-free (no 256-case Marching-Cubes triangle table), produces a watertight
///     surface on binary label data, and places one shared vertex per boundary cell at the centroid of
///     its edge crossings. The raw surface is relaxed with Taubin (λ|μ) smoothing — which removes voxel
///     staircasing without the volume shrinkage of plain Laplacian smoothing — and can optionally be
///     simplified with Quadric Error Metric (QEM) decimation.
///
///     References: Gibson (1998), "Constrained Elastic SurfaceNets"; Taubin (1995), "A signal
///     processing approach to fair surface design"; Garland &amp; Heckbert (1997), "Surface
///     simplification using quadric error metrics".
/// </summary>
public class SurfaceNetsMesher
{
    // 12 edges of a cube as pairs of corner indices. A corner index g packs (i,j,k) as g = i|j<<1|k<<2.
    private static readonly (int a, int b)[] CubeEdges =
    {
        (0, 1), (2, 3), (4, 5), (6, 7), // along X
        (0, 2), (1, 3), (4, 6), (5, 7), // along Y
        (0, 4), (1, 5), (2, 6), (3, 7)  // along Z
    };

    public async Task<(List<Vector3> vertices, List<int[]> faces)> GenerateMeshAsync(
        ChunkedLabelVolume labels,
        byte materialId,
        MeshingOptions options,
        IProgress<(float progress, string message)> progress,
        CancellationToken token)
    {
        var ds = Math.Max(1, options.Downsampling);

        // Grid of sample nodes at every ds-th voxel (last voxel always included via clamping).
        var nx = (labels.Width + ds - 1) / ds;
        var ny = (labels.Height + ds - 1) / ds;
        var nz = (labels.Depth + ds - 1) / ds;
        if (nx < 2 || ny < 2 || nz < 2)
            return (new List<Vector3>(), new List<int[]>());

        var solid = await Task.Run(
            () => BuildOccupancy(labels, materialId, ds, nx, ny, nz, progress, 0.00f, 0.20f, token), token);

        if (options.ShellOnly)
            await Task.Run(() => FillInternalCavities(solid, nx, ny, nz, progress, 0.20f, 0.42f, token), token);

        token.ThrowIfCancellationRequested();

        var (vertices, triangles) = await Task.Run(
            () => ExtractSurfaceNets(solid, nx, ny, nz, ds, progress, 0.42f, 0.68f, token), token);

        if (vertices.Count == 0 || triangles.Count == 0)
            return (new List<Vector3>(), new List<int[]>());

        if (options.SmoothingIterations > 0)
            await Task.Run(
                () => TaubinSmooth(vertices, triangles, options.SmoothingIterations, progress, 0.68f, 0.80f, token),
                token);

        if (options.DecimateKeepRatio < 0.999f)
            await Task.Run(() =>
            {
                var (dv, dt) = QuadricMeshSimplifier.Simplify(
                    vertices, triangles, options.DecimateKeepRatio, progress, 0.80f, 0.97f, token);
                vertices = dv;
                triangles = dt;
            }, token);

        progress?.Report((0.98f, $"Finalizing mesh ({triangles.Count / 3} triangles)..."));

        // Convert the flat triangle index list to the List<int[]> face format the mesh dataset expects.
        var faces = new List<int[]>(triangles.Count / 3);
        for (var i = 0; i + 2 < triangles.Count; i += 3)
            faces.Add(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });

        Logger.Log($"[SurfaceNets] Material {materialId}: {vertices.Count} vertices, {faces.Count} triangles " +
                   $"(ds={ds}, shell={options.ShellOnly}, smooth={options.SmoothingIterations}, " +
                   $"keep={options.DecimateKeepRatio:0.##}).");
        return (vertices, faces);
    }

    // ── Occupancy sampling ────────────────────────────────────────────────────
    private static bool[] BuildOccupancy(ChunkedLabelVolume labels, byte materialId, int ds,
        int nx, int ny, int nz, IProgress<(float, string)> progress, float lo, float hi, CancellationToken token)
    {
        int width = labels.Width, height = labels.Height, depth = labels.Depth;
        var solid = new bool[(long)nx * ny * nz];
        var done = 0;

        Parallel.For(0, nz, new ParallelOptions { CancellationToken = token },
            () => new byte[width * height],
            (k, _, buffer) =>
            {
                var z = Math.Min(k * ds, depth - 1);
                labels.ReadSliceZ(z, buffer);
                var planeBase = (long)nx * ny * k;
                for (var j = 0; j < ny; j++)
                {
                    var y = Math.Min(j * ds, height - 1);
                    var rowBase = y * width;
                    var outRow = planeBase + (long)nx * j;
                    for (var i = 0; i < nx; i++)
                    {
                        var x = Math.Min(i * ds, width - 1);
                        solid[outRow + i] = buffer[rowBase + x] == materialId;
                    }
                }

                var n = Interlocked.Increment(ref done);
                if ((n & 7) == 0)
                    progress?.Report((lo + (hi - lo) * n / nz, $"Sampling volume... slice {n}/{nz}"));

                return buffer;
            },
            _ => { });

        return solid;
    }

    // ── Shell mode: flood the exterior empty space in parallel, then fill what it never reached ──
    private static void FillInternalCavities(bool[] solid, int nx, int ny, int nz,
        IProgress<(float, string)> progress, float lo, float hi, CancellationToken token)
    {
        progress?.Report((lo, "Shell: measuring empty space..."));
        long emptyCount = 0;
        Parallel.For(0, nz, new ParallelOptions { CancellationToken = token }, () => 0L,
            (k, _, local) =>
            {
                var baseIdx = (long)nx * ny * k;
                for (long i = 0; i < (long)nx * ny; i++)
                    if (!solid[baseIdx + i])
                        local++;
                return local;
            },
            local => Interlocked.Add(ref emptyCount, local));
        if (emptyCount == 0) return;

        // outside[n] = 1 once the exterior flood has claimed empty node n. Non-atomic writes are fine:
        // the mark is idempotent, so at worst a node is enqueued a couple of extra times.
        var outside = new byte[solid.Length];
        var frontier = new List<int>();

        void Seed(int x, int y, int z)
        {
            var id = x + nx * (y + ny * z);
            if (!solid[id] && outside[id] == 0)
            {
                outside[id] = 1;
                frontier.Add(id);
            }
        }

        for (var y = 0; y < ny; y++)
        for (var x = 0; x < nx; x++)
        {
            Seed(x, y, 0);
            Seed(x, y, nz - 1);
        }

        for (var z = 0; z < nz; z++)
        for (var x = 0; x < nx; x++)
        {
            Seed(x, 0, z);
            Seed(x, ny - 1, z);
        }

        for (var z = 0; z < nz; z++)
        for (var y = 0; y < ny; y++)
        {
            Seed(0, y, z);
            Seed(nx - 1, y, z);
        }

        long visited = frontier.Count;
        long planeArea = (long)nx * ny;

        // Level-synchronous parallel BFS: expand the whole frontier at once, collect the next one.
        while (frontier.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var next = new ConcurrentBag<List<int>>();

            Parallel.ForEach(Partitioner.Create(0, frontier.Count),
                new ParallelOptions { CancellationToken = token },
                () => new List<int>(),
                (range, _, local) =>
                {
                    for (var idx = range.Item1; idx < range.Item2; idx++)
                    {
                        var id = frontier[idx];
                        var z = (int)(id / planeArea);
                        var rem = id - planeArea * z;
                        var y = (int)(rem / nx);
                        var x = (int)(rem - (long)nx * y);

                        TryVisit(x - 1, y, z, local);
                        TryVisit(x + 1, y, z, local);
                        TryVisit(x, y - 1, z, local);
                        TryVisit(x, y + 1, z, local);
                        TryVisit(x, y, z - 1, local);
                        TryVisit(x, y, z + 1, local);
                    }

                    return local;
                },
                local =>
                {
                    if (local.Count > 0) next.Add(local);
                });

            var merged = new List<int>();
            foreach (var list in next) merged.AddRange(list);
            frontier = merged;
            visited += frontier.Count;
            progress?.Report((lo + (hi - lo) * Math.Min(1f, visited / (float)emptyCount),
                $"Shell: flooding exterior... {visited * 100 / emptyCount}%"));
        }

        void TryVisit(int x, int y, int z, List<int> local)
        {
            if (x < 0 || x >= nx || y < 0 || y >= ny || z < 0 || z >= nz) return;
            var id = x + nx * (y + ny * z);
            if (solid[id] || outside[id] != 0) return;
            outside[id] = 1;
            local.Add(id);
        }

        // Any empty node the exterior never reached is an internal cavity -> make it solid.
        progress?.Report((hi, "Shell: filling cavities..."));
        Parallel.For(0L, solid.LongLength, new ParallelOptions { CancellationToken = token },
            i => { if (!solid[i] && outside[i] == 0) solid[i] = true; });
    }

    // ── Dual Surface Nets extraction (single pass, rolling two-plane vertex buffer) ──
    private static (List<Vector3> vertices, List<int> triangles) ExtractSurfaceNets(
        bool[] solid, int nx, int ny, int nz, int ds,
        IProgress<(float, string)> progress, float lo, float hi, CancellationToken token)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        int cnx = nx - 1, cny = ny - 1; // cells per axis in the plane
        var buffer = new int[2 * cnx * cny];
        Array.Fill(buffer, -1);

        int Plane(int ck) => (ck & 1) * cnx * cny;
        int CellVert(int ci, int cj, int ck) => buffer[Plane(ck) + ci + cnx * cj];

        var solids = new bool[8];

        for (var k = 0; k < nz - 1; k++)
        {
            if ((k & 0x1F) == 0)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report((lo + (hi - lo) * k / (nz - 1),
                    $"Surface Nets... plane {k}/{nz - 1} ({vertices.Count} verts)"));
            }

            var planeCur = Plane(k);

            for (var j = 0; j < ny - 1; j++)
            for (var i = 0; i < nx - 1; i++)
            {
                var mask = 0;
                for (var g = 0; g < 8; g++)
                {
                    var s = solid[(i + (g & 1)) +
                                  (long)nx * ((j + ((g >> 1) & 1)) + (long)ny * (k + ((g >> 2) & 1)))];
                    solids[g] = s;
                    if (s) mask |= 1 << g;
                }

                var cellVert = -1;
                if (mask != 0 && mask != 0xFF)
                {
                    // Vertex = cell origin + centroid of the crossing-edge midpoints.
                    float ax = 0, ay = 0, az = 0;
                    var count = 0;
                    foreach (var (a, b) in CubeEdges)
                        if (solids[a] != solids[b])
                        {
                            ax += ((a & 1) + (b & 1)) * 0.5f;
                            ay += (((a >> 1) & 1) + ((b >> 1) & 1)) * 0.5f;
                            az += (((a >> 2) & 1) + ((b >> 2) & 1)) * 0.5f;
                            count++;
                        }

                    var inv = 1f / count;
                    cellVert = vertices.Count;
                    vertices.Add(new Vector3((i + ax * inv) * ds, (j + ay * inv) * ds, (k + az * inv) * ds));
                }

                // CRITICAL: publish this cell's vertex index into the buffer BEFORE emitting quads, so
                // EmitQuad reads the correct current-cell vertex (not a stale value from an older plane).
                buffer[planeCur + i + cnx * j] = cellVert;

                if (cellVert >= 0)
                {
                    // Emit a quad for each of the three edges at corner 0 that straddle the surface.
                    EmitQuad(0, i, j, k, solids[0], solids[1], CellVert, triangles);
                    EmitQuad(1, i, j, k, solids[0], solids[2], CellVert, triangles);
                    EmitQuad(2, i, j, k, solids[0], solids[4], CellVert, triangles);
                }
            }
        }

        return (vertices, triangles);
    }

    private static void EmitQuad(int axis, int i, int j, int k, bool corner0, bool cornerAxis,
        Func<int, int, int, int> cellVert, List<int> triangles)
    {
        if (corner0 == cornerAxis) return; // edge does not cross the surface

        // The two axes perpendicular to the edge; the four sharing cells step back along them.
        int iu = (axis + 1) % 3, iv = (axis + 2) % 3;
        var cu = iu == 0 ? i : iu == 1 ? j : k;
        var cv = iv == 0 ? i : iv == 1 ? j : k;
        if (cu == 0 || cv == 0) return; // neighbours out of range at the grid border

        int uI = iu == 0 ? 1 : 0, uJ = iu == 1 ? 1 : 0, uK = iu == 2 ? 1 : 0;
        int vI = iv == 0 ? 1 : 0, vJ = iv == 1 ? 1 : 0, vK = iv == 2 ? 1 : 0;

        var q00 = cellVert(i, j, k);
        var qU = cellVert(i - uI, j - uJ, k - uK);
        var qV = cellVert(i - vI, j - vJ, k - vK);
        var qUV = cellVert(i - uI - vI, j - uJ - vJ, k - uK - vK);
        if (q00 < 0 || qU < 0 || qV < 0 || qUV < 0) return; // any neighbour missing -> skip

        // Wind the quad so the front face points out of the solid, then split into two triangles.
        int a, b, c, d;
        if (corner0)
        {
            a = q00; b = qU; c = qUV; d = qV;
        }
        else
        {
            a = q00; b = qV; c = qUV; d = qU;
        }

        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(a); triangles.Add(c); triangles.Add(d);
    }

    // ── Taubin (λ|μ) smoothing ─────────────────────────────────────────────────
    private static void TaubinSmooth(List<Vector3> vertices, List<int> triangles, int iterations,
        IProgress<(float, string)> progress, float lo, float hi, CancellationToken token)
    {
        progress?.Report((lo, "Smoothing: building adjacency..."));
        var adjacency = BuildAdjacency(vertices.Count, triangles);
        const float lambda = 0.5f;
        const float mu = -0.53f;

        var current = vertices.ToArray();
        var next = new Vector3[current.Length];

        void Pass(float factor)
        {
            Parallel.For(0, current.Length, new ParallelOptions { CancellationToken = token }, v =>
            {
                var neighbours = adjacency[v];
                if (neighbours == null || neighbours.Length == 0)
                {
                    next[v] = current[v];
                    return;
                }

                var sum = Vector3.Zero;
                foreach (var n in neighbours) sum += current[n];
                var average = sum / neighbours.Length;
                next[v] = current[v] + (average - current[v]) * factor;
            });

            Array.Copy(next, current, current.Length);
        }

        for (var it = 0; it < iterations; it++)
        {
            token.ThrowIfCancellationRequested();
            Pass(lambda);
            Pass(mu);
            progress?.Report((lo + (hi - lo) * (it + 1) / iterations,
                $"Smoothing... iteration {it + 1}/{iterations}"));
        }

        for (var v = 0; v < current.Length; v++) vertices[v] = current[v];
    }

    private static int[][] BuildAdjacency(int vertexCount, List<int> triangles)
    {
        var sets = new HashSet<int>[vertexCount];
        for (var i = 0; i + 2 < triangles.Count; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            AddPair(sets, a, b);
            AddPair(sets, b, c);
            AddPair(sets, c, a);
        }

        var adjacency = new int[vertexCount][];
        for (var v = 0; v < vertexCount; v++)
            adjacency[v] = sets[v] != null ? sets[v].ToArray() : Array.Empty<int>();
        return adjacency;
    }

    private static void AddPair(HashSet<int>[] sets, int a, int b)
    {
        (sets[a] ??= new HashSet<int>()).Add(b);
        (sets[b] ??= new HashSet<int>()).Add(a);
    }
}
