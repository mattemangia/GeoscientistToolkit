// GeoscientistToolkit/Analysis/Pnm/PNMGenerator.cs
// .NET 8, cross-platform (Win/macOS/Linux). Optional GPU acceleration via Silk.NET.OpenCL.
// Depends on existing types in main repo: CtImageStackDataset, ChunkedLabelVolume, PNMDataset, Dataset, ProjectManager, ImGui, etc.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using Silk.NET.Core.Native;

// Optional GPU
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Pnm
{
    #region Options & enums
    
    public enum Neighborhood3D { N6 = 6, N18 = 18, N26 = 26 }
    public enum GenerationMode { Conservative, Aggressive }
    public enum FlowAxis { X, Y, Z }

    public sealed class PNMGeneratorOptions
    {
        public int MaterialId { get; set; } = 0;
        public Neighborhood3D Neighborhood { get; set; } = Neighborhood3D.N26;
        public GenerationMode Mode { get; set; } = GenerationMode.Conservative;
        public bool UseOpenCL { get; set; } = false;

        // inlet↔outlet path options for absolute perm setups
        public bool EnforceInletOutletConnectivity { get; set; } = false;
        public FlowAxis Axis { get; set; } = FlowAxis.Z;      // default Z-flow
        public bool InletIsMinSide { get; set; } = true;      // min-face is inlet
        public bool OutletIsMaxSide { get; set; } = true;     // max-face is outlet

        // Aggressiveness controls (number of erosions to split constrictions)
        public int ConservativeErosions { get; set; } = 1;
        public int AggressiveErosions { get; set; } = 3;
        
    }

    #endregion

    #region Generator (CPU with optional OpenCL assist)

    public static class PNMGenerator
    {
        private sealed class PoreFaceContacts
        {
            public bool[] XMin, XMax, YMin, YMax, ZMin, ZMax;
        }
        // GPU kernels (small, well-scoped): binary erosion & 1-pass EDT relax
        private const string OpenCLKernels = @"
        __kernel void binary_erosion(
            __global const uchar* src, __global uchar* dst,
            const int W, const int H, const int D,
            const int neighMode) // 6,18,26
        {
            int x = get_global_id(0);
            int y = get_global_id(1);
            int z = get_global_id(2);
            if (x>=W || y>=H || z>=D) return;

            int idx = (z*H + y)*W + x;
            uchar v = src[idx];
            if (v==0) { dst[idx] = 0; return; }

            // Check neighbors
            int dxs[26] = { -1,1,0,0,0,0, -1,-1,-1, 1,1,1, 0,0,0,  0,0,  -1,-1,1,1,  -1,1,-1,1 };
            int dys[26] = { 0,0,-1,1,0,0, -1,0,1, -1,0,1, -1,0,1, -1,1,  0,0,  -1,1,-1,1 };
            int dzs[26] = { 0,0,0,0,-1,1,  0,0,0,  0,0,0, -1,-1,-1, 1,1, -1,1,  0,0,  0,0,0,0 };

            int needCount = neighMode; // treat as number of neighbor directions
            // 6-neigh: only axis-aligned (first 6 in lists)
            // 18: include axis + edge neighbors (first 18)
            // 26: include all

            int N = (neighMode==6) ? 6 : (neighMode==18 ? 18 : 26);

            for (int i=0;i<N;i++)
            {
                int nx = x + dxs[i];
                int ny = y + dys[i];
                int nz = z + dzs[i];
                if (nx<0||ny<0||nz<0||nx>=W||ny>=H||nz>=D) { dst[idx]=0; return; }
                int nidx = (nz*H + ny)*W + nx;
                if (src[nidx]==0) { dst[idx]=0; return; }
            }
            dst[idx]=1;
        }

        __kernel void edt_relax(
            __global float* dist, __global const uchar* mask,
            const int W, const int H, const int D)
        {
            int x = get_global_id(0);
            int y = get_global_id(1);
            int z = get_global_id(2);
            if (x>=W || y>=H || z>=D) return;
            int idx = (z*H + y)*W + x;
            if (mask[idx]==0) { dist[idx]=0.0f; return; }

            float best = dist[idx];

            // scan a 3x3x3 neighborhood (approximate relaxation)
            for (int dz=-1; dz<=1; dz++)
            for (int dy=-1; dy<=1; dy++)
            for (int dx=-1; dx<=1; dx++)
            {
                if (dx==0 && dy==0 && dz==0) continue;
                int nx=x+dx, ny=y+dy, nz=z+dz;
                if (nx<0||ny<0||nz<0||nx>=W||ny>=H||nz>=D) continue;
                int nidx = (nz*H + ny)*W + nx;
                float w = sqrt((float)(dx*dx+dy*dy+dz*dz));
                float nd = dist[nidx] + w;
                if (nd < best) best = nd;
            }
            dist[idx] = best;
        }";

        public static PNMDataset Generate(CtImageStackDataset ct, PNMGeneratorOptions opt, IProgress<float> progress, CancellationToken token)
        {
            if (ct == null) throw new ArgumentNullException(nameof(ct));
            if (ct.LabelData == null) throw new InvalidOperationException("CtImageStackDataset has no LabelData loaded.");
            if (opt.MaterialId == 0) throw new ArgumentException("Please select a non-zero material ID (0 is reserved for Exterior).");

            // Voxel geometry (µm) — we take geometric mean if anisotropic spacing
            double vx = Math.Max(1e-9, ct.PixelSize);
            double vy = Math.Max(1e-9, ct.PixelSize);
            double vz = Math.Max(1e-9, ct.SliceThickness > 0 ? ct.SliceThickness : ct.PixelSize);
            double vEdge = Math.Pow(vx * vy * vz, 1.0 / 3.0);

            int W = ct.Width, H = ct.Height, D = ct.Depth;
            var labels = ct.LabelData; // ChunkedLabelVolume with indexer & fast slice read. :contentReference[oaicite:3]{index=3}

            // Step 1: extract material mask
            progress?.Report(0.02f);
            var mask = new byte[W * H * D];
            Parallel.For(0, D, z =>
            {
                int plane = W * H;
                for (int y = 0; y < H; y++)
                {
                    int row = (z * H + y) * W;
                    for (int x = 0; x < W; x++)
                    {
                        mask[row + x] = (labels[x, y, z] == (byte)opt.MaterialId) ? (byte)1 : (byte)0;
                    }
                }
            });

            token.ThrowIfCancellationRequested();
            progress?.Report(0.08f);

            // Step 2: split constrictions by a small number of erosions (conservative/aggressive)
            int erosions = opt.Mode == GenerationMode.Conservative ? opt.ConservativeErosions : opt.AggressiveErosions;
            if (erosions > 0)
            {
                var tmp = new byte[mask.Length];
                for (int i = 0; i < erosions; i++)
                {
                    if (opt.UseOpenCL && TryBinaryErosionOpenCL(mask, tmp, W, H, D, (int)opt.Neighborhood))
                    {
                        // swap
                        var t = mask; mask = tmp; tmp = t;
                    }
                    else
                    {
                        BinaryErosionCPU(mask, tmp, W, H, D, opt.Neighborhood);
                        var t = mask; mask = tmp; tmp = t;
                    }
                    progress?.Report(0.08f + 0.02f * (i + 1));
                    token.ThrowIfCancellationRequested();
                }
            }

            // Step 3: connected-component labeling on eroded mask → provisional pores
            progress?.Report(0.15f);
            var labelsPores = ConnectedComponents3D(mask, W, H, D, opt.Neighborhood);
            int poreCount = labelsPores.ComponentCount;

            token.ThrowIfCancellationRequested();
            progress?.Report(0.35f);

            // Step 4: pore stats (volume voxels, centroid, surface area approx) — on original material mask
            var pores = new Pore[poreCount + 1]; // index from 1..N
            ComputePoreStats(labelsPores.Labels, mask, W, H, D, vx, vy, vz, pores);

            // Step 5: estimate pore radii via EDT (distance-to-solid-boundary) on original mask; CPU or CL
            progress?.Report(0.55f);
            float[] edt = null;
            try
            {
                edt = (opt.UseOpenCL && TryDistanceRelaxOpenCL(mask, W, H, D))
                    ? _lastDist // filled by TryDistanceRelaxOpenCL
                    : DistanceTransformApproxCPU(mask, W, H, D);
            }
            finally { }
            // assign pore radius as max EDT value observed within the component (× edge size)
            AssignPoreRadiiFromEDT(pores, labelsPores.Labels, edt, (float)vEdge);

            token.ThrowIfCancellationRequested();
            progress?.Report(0.7f);

            // Step 6: build throats (adjacency of components along original, non-eroded mask)
            var throats = BuildThroats(labelsPores.Labels, W, H, D, edt, (float)vEdge, out var adjacency);
            // connections count:
            foreach (var th in throats)
            {
                if (th.Pore1ID > 0 && th.Pore1ID < pores.Length) pores[th.Pore1ID].Connections++;
                if (th.Pore2ID > 0 && th.Pore2ID < pores.Length) pores[th.Pore2ID].Connections++;
            }

            token.ThrowIfCancellationRequested();
            progress?.Report(0.82f);

            // Step 7: optional inlet↔outlet path enforcement (add synthetic throats if needed)
            if (opt.EnforceInletOutletConnectivity && poreCount > 0)
            {
                EnforceInOutConnectivity(pores, adjacency, opt.Axis, opt.InletIsMinSide, opt.OutletIsMaxSide, W, H, D, (float)vx, (float)vy, (float)vz, throats);
            }

            // Step 8: tortuosity (shortest path between any inlet & outlet pores / physical length)
            float tort = ComputeTortuosity(
                pores,
                adjacency,
                opt.Axis,
                opt.InletIsMinSide,
                opt.OutletIsMaxSide,
                W, H, D,
                (float)vx, (float)vy, (float)vz,
                labelsPores.Labels,
                labelsPores.ComponentCount);

            progress?.Report(0.92f);

            // Step 9: pack PNMDataset
            var pnm = new PNMDataset($"PNM_{ct.Name}_{opt.MaterialId}", System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PNM_{Guid.NewGuid()}.pnm.json"))
            {
                VoxelSize = (float)vEdge, // um
                Tortuosity = tort,
            };
            // Add pores & throats (skip 0)
            for (int i = 1; i < pores.Length; i++)
                if (pores[i] != null) pnm.Pores.Add(pores[i]);

            int tid = 1;
            foreach (var t in throats)
            {
                t.ID = tid++;
                pnm.Throats.Add(t);
            }
            pnm.CalculateBounds();

            // Register to project, so the existing PNM viewer & property panel can show it.
            try
            {
                GeoscientistToolkit.Business.ProjectManager.Instance.AddDataset(pnm);
                GeoscientistToolkit.Business.ProjectManager.Instance.NotifyDatasetDataChanged(pnm);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[PNMGenerator] Could not auto-add PNMDataset to ProjectManager: {ex.Message}. You can add it manually.");
            }

            progress?.Report(1.0f);
            return pnm;
        }
        
        #region CPU morphology & CCL

        private static void BinaryErosionCPU(byte[] src, byte[] dst, int W, int H, int D, Neighborhood3D nbh)
{
    // Clear destination first
    Array.Clear(dst, 0, dst.Length);

    // Full 26-neighbourhood deltas (faces + edges + corners).
    // The first 6 entries are the 6-neighbourhood (faces).
    // The first 18 entries are the 18-neighbourhood (faces + edges).
    var OFF26 = new (int dx, int dy, int dz)[]
    {
        // 6-neigh (faces)
        (-1, 0, 0), ( 1, 0, 0), ( 0,-1, 0), ( 0, 1, 0), ( 0, 0,-1), ( 0, 0, 1),

        // edges (12)
        (-1,-1, 0), (-1, 1, 0), ( 1,-1, 0), ( 1, 1, 0),
        (-1, 0,-1), (-1, 0, 1), ( 1, 0,-1), ( 1, 0, 1),
        ( 0,-1,-1), ( 0,-1, 1), ( 0, 1,-1), ( 0, 1, 1),

        // corners (8)
        (-1,-1,-1), (-1,-1, 1), (-1, 1,-1), (-1, 1, 1),
        ( 1,-1,-1), ( 1,-1, 1), ( 1, 1,-1), ( 1, 1, 1)
    };

    int useCount = nbh == Neighborhood3D.N6 ? 6 : (nbh == Neighborhood3D.N18 ? 18 : 26);

    // Main parallel sweep
    System.Threading.Tasks.Parallel.For(0, D, z =>
    {
        for (int y = 0; y < H; y++)
        {
            int row = (z * H + y) * W;
            for (int x = 0; x < W; x++)
            {
                int idx = row + x;

                // If source voxel is background, erosion result is background
                if (src[idx] == 0) { dst[idx] = 0; continue; }

                bool keep = true;

                if (useCount == 6)
                {
                    // Explicit fast path for 6-neighbourhood
                    if (Get(src, x - 1, y    , z    , W, H, D) == 0) keep = false;
                    else if (Get(src, x + 1, y    , z    , W, H, D) == 0) keep = false;
                    else if (Get(src, x    , y - 1, z    , W, H, D) == 0) keep = false;
                    else if (Get(src, x    , y + 1, z    , W, H, D) == 0) keep = false;
                    else if (Get(src, x    , y    , z - 1, W, H, D) == 0) keep = false;
                    else if (Get(src, x    , y    , z + 1, W, H, D) == 0) keep = false;
                }
                else
                {
                    // 18- or 26-neighbourhood via the OFF26 table
                    for (int i = 0; i < useCount; i++)
                    {
                        var n = OFF26[i];
                        if (Get(src, x + n.dx, y + n.dy, z + n.dz, W, H, D) == 0)
                        {
                            keep = false;
                            break;
                        }
                    }
                }

                dst[idx] = (byte)(keep ? 1 : 0);
            }
        }
    });
}


        private static byte Get(byte[] a, int x, int y, int z, int W, int H, int D)
        {
            if ((uint)x >= W || (uint)y >= H || (uint)z >= D) return 0;
            return a[(z * H + y) * W + x];
        }

        private sealed class LabelResult
        {
            public int[] Labels;
            public int ComponentCount;
        }

        private static LabelResult ConnectedComponents3D(byte[] mask, int W, int H, int D, Neighborhood3D nbh)
        {
            var labels = new int[mask.Length];
            var parent = new List<int> { 0 }; // 0 unused
            parent.Add(1); // first comp id 1
            int next = 1;

            int[] neighOffsets6 = new int[]
            {
                -1, // x-1
                -W, // y-1
                -W*H // z-1
            };

            // 1st pass (RASTER, union-find)
            for (int z = 0; z < D; z++)
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int idx = (z * H + y) * W + x;
                if (mask[idx] == 0) continue;

                int minLabel = int.MaxValue;
                // check previous neighbors depending on nbh — use restricted set for speed
                // We only check predecessors (x-1, y-1, z-1 and their combos).
                // For robustness we include diagonals when using 18/26.
                foreach (var offset in NeighborBackOffsets(x, y, z, W, H, D, nbh))
                {
                    int nlab = labels[idx + offset];
                    if (nlab > 0 && nlab < minLabel) minLabel = nlab;
                }

                if (minLabel == int.MaxValue)
                {
                    minLabel = ++next;
                    parent.Add(minLabel);
                }
                labels[idx] = minLabel;

                // union with any different neighbor labels
                foreach (var offset in NeighborBackOffsets(x, y, z, W, H, D, nbh))
                {
                    int nlab = labels[idx + offset];
                    if (nlab > 0 && nlab != minLabel)
                    {
                        Union(parent, minLabel, nlab);
                    }
                }
            }

            // 2nd pass: flatten labels
            var map = new Dictionary<int, int>();
            int comp = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                int lab = labels[i];
                if (lab == 0) continue;
                int root = Find(parent, lab);
                if (!map.TryGetValue(root, out int newLab))
                {
                    newLab = ++comp;
                    map[root] = newLab;
                }
                labels[i] = newLab;
            }

            return new LabelResult { Labels = labels, ComponentCount = comp };
        }

        private static IEnumerable<int> NeighborBackOffsets(int x, int y, int z, int W, int H, int D, Neighborhood3D nbh)
        {
            // only predecessors to avoid double visiting
            // axis
            if (x > 0) yield return -1;
            if (y > 0) yield return -W;
            if (z > 0) yield return -W * H;

            if (nbh == Neighborhood3D.N6) yield break;

            // 18: edges (two axis negative)
            if (x > 0 && y > 0) yield return -1 - W;
            if (x > 0 && z > 0) yield return -1 - W * H;
            if (y > 0 && z > 0) yield return -W - W * H;

            if (nbh == Neighborhood3D.N18) yield break;

            // 26: corners (three axis negative)
            if (x > 0 && y > 0 && z > 0) yield return -1 - W - W * H;
        }

        private static int Find(List<int> parent, int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }
        private static void Union(List<int> parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra == rb) return;
            if (ra < rb) parent[rb] = ra; else parent[ra] = rb;
        }

        #endregion

        #region Pore stats + EDT

        private static void ComputePoreStats(int[] lbl, byte[] mask, int W, int H, int D, double vx, double vy, double vz, Pore[] pores)
        {
            var sums = new ConcurrentDictionary<int, (double cx, double cy, double cz, long vox, long area)>();

            Parallel.For(0, D, z =>
            {
                int plane = W * H;
                for (int y = 0; y < H; y++)
                {
                    int row = (z * H + y) * W;
                    for (int x = 0; x < W; x++)
                    {
                        int idx = row + x;
                        int id = lbl[idx];
                        if (id <= 0) continue;
                        bool isSolid = mask[idx] == 1;

                        // peripheral face count (approx surface area in voxel faces)
                        int openFaces = 0;
                        if (x == 0 || mask[idx - 1] == 0) openFaces++;
                        if (x == W - 1 || mask[idx + 1] == 0) openFaces++;
                        if (y == 0 || mask[idx - W] == 0) openFaces++;
                        if (y == H - 1 || mask[idx + W] == 0) openFaces++;
                        if (z == 0 || mask[idx - W * H] == 0) openFaces++;
                        if (z == D - 1 || mask[idx + W * H] == 0) openFaces++;

                        sums.AddOrUpdate(id,
                            _ => (x, y, z, 1, openFaces),
                            (_, s) => (s.cx + x, s.cy + y, s.cz + z, s.vox + 1, s.area + openFaces));
                    }
                }
            });

            foreach (var kv in sums)
            {
                int id = kv.Key;
                var (cx, cy, cz, vox, area) = kv.Value;
                if (vox <= 0) continue;
                var p = new Pore
                {
                    ID = id,
                    Position = new Vector3((float)(cx / vox), (float)(cy / vox), (float)(cz / vox)),
                    Area = (float)area, // voxel-face count, not physical yet (viewer scales by voxel size) 
                    VolumeVoxels = vox,
                    VolumePhysical = (float)(vox * vx * vy * vz),
                    Connections = 0,
                    Radius = 0 // filled later
                };
                pores[id] = p;
            }
        }

        private static float[] DistanceTransformApproxCPU(byte[] mask, int W, int H, int D)
        {
            var dist = new float[mask.Length];
            // init
            for (int i = 0; i < dist.Length; i++) dist[i] = mask[i] == 0 ? 0f : 1e6f;

            // forward pass
            for (int z = 0; z < D; z++)
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int idx = (z * H + y) * W + x;
                if (mask[idx] == 0) { dist[idx] = 0; continue; }
                float best = dist[idx];
                for (int dz = -1; dz <= 0; dz++)
                for (int dy = -1; dy <= 0; dy++)
                for (int dx = -1; dx <= 0; dx++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    if ((uint)nx >= W || (uint)ny >= H || (uint)nz >= D) continue;
                    int nidx = (nz * H + ny) * W + nx;
                    float w = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    float nd = dist[nidx] + w;
                    if (nd < best) best = nd;
                }
                dist[idx] = best;
            }
            // backward pass
            for (int z = D - 1; z >= 0; z--)
            for (int y = H - 1; y >= 0; y--)
            for (int x = W - 1; x >= 0; x--)
            {
                int idx = (z * H + y) * W + x;
                if (mask[idx] == 0) { dist[idx] = 0; continue; }
                float best = dist[idx];
                for (int dz = 0; dz <= 1; dz++)
                for (int dy = 0; dy <= 1; dy++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    if ((uint)nx >= W || (uint)ny >= H || (uint)nz >= D) continue;
                    int nidx = (nz * H + ny) * W + nx;
                    float w = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    float nd = dist[nidx] + w;
                    if (nd < best) best = nd;
                }
                dist[idx] = best;
            }

            return dist;
        }

        private static void AssignPoreRadiiFromEDT(Pore[] pores, int[] ccl, float[] edt, float vEdge)
        {
            if (edt == null) return;
            var maxByComp = new float[pores.Length];
            for (int i = 0; i < ccl.Length; i++)
            {
                int id = ccl[i];
                if (id <= 0) continue;
                float d = edt[i];
                if (d > maxByComp[id]) maxByComp[id] = d;
            }
            for (int id = 1; id < pores.Length; id++)
            {
                if (pores[id] != null)
                {
                    // EDT is in voxel-steps; convert to physical (approx radius ~ dist to boundary)
                    pores[id].Radius = maxByComp[id] * vEdge;
                }
            }
        }

        #endregion

        #region Throats & adjacency

        private static List<Throat> BuildThroats(int[] lbl, int W, int H, int D, float[] edt, float vEdge, out Dictionary<int, List<(int nb, float w)>> adjacency)
        {
            var edges = new ConcurrentDictionary<(int a, int b), float>(); // min radius along boundary
            Parallel.For(0, D, z =>
            {
                int plane = W * H;
                for (int y = 0; y < H; y++)
                {
                    int row = (z * H + y) * W;
                    for (int x = 0; x < W; x++)
                    {
                        int idx = row + x;
                        int a = lbl[idx];
                        if (a == 0) continue;

                        // Check 6-neighbors for cross-component contacts
                        void CheckNeighbor(int nx, int ny, int nz)
                        {
                            if ((uint)nx >= W || (uint)ny >= H || (uint)nz >= D) return;
                            int nidx = (nz * H + ny) * W + nx;
                            int b = lbl[nidx];
                            if (b == 0 || b == a) return;
                            var key = (a < b) ? (a, b) : (b, a);
                            // throat radius at this constriction ≈ min(edt at interface voxels) * vEdge
                            float r = MathF.Min(edt[idx], edt[nidx]) * vEdge;
                            edges.AddOrUpdate(key, r, (_, old) => MathF.Min(old, r));
                        }

                        CheckNeighbor(x + 1, y, z);
                        CheckNeighbor(x, y + 1, z);
                        CheckNeighbor(x, y, z + 1);
                    }
                }
            });

            var list = new List<Throat>(edges.Count);
            adjacency = new Dictionary<int, List<(int nb, float w)>>();
            int tid = 1;
            foreach (var kv in edges)
            {
                var (a, b) = kv.Key;
                float r = Math.Max(0.0f, kv.Value);
                list.Add(new Throat { ID = tid++, Pore1ID = a, Pore2ID = b, Radius = r });

                if (!adjacency.TryGetValue(a, out var la)) adjacency[a] = la = new List<(int nb, float w)>();
                if (!adjacency.TryGetValue(b, out var lb)) adjacency[b] = lb = new List<(int nb, float w)>();
                // weight as Euclidean between centroids will be used for tortuosity; store here as 1 (we'll replace later)
                la.Add((b, 1));
                lb.Add((a, 1));
            }

            return list;
        }

        private static void EnforceInOutConnectivity(Pore[] pores, Dictionary<int, List<(int nb, float w)>> adj,
            FlowAxis axis, bool inletMin, bool outletMax, int W, int H, int D, float vx, float vy, float vz, List<Throat> throats)
        {
            if (pores.Length <= 1) return;

            // Identify inlet/outlet pore sets by touching dataset faces in chosen axis
            var inlet = new HashSet<int>();
            var outlet = new HashSet<int>();
            float minFace = 0, maxFace = 0, tol = 0.5f; // in vox units
            switch (axis)
            {
                case FlowAxis.X: minFace = 0; maxFace = W - 1; break;
                case FlowAxis.Y: minFace = 0; maxFace = H - 1; break;
                default: minFace = 0; maxFace = D - 1; break;
            }

            for (int i = 1; i < pores.Length; i++)
            {
                var p = pores[i]; if (p == null) continue;
                float coord = axis == FlowAxis.X ? p.Position.X : axis == FlowAxis.Y ? p.Position.Y : p.Position.Z;
                if (Math.Abs(coord - minFace) <= tol && inletMin) inlet.Add(i);
                if (Math.Abs(coord - maxFace) <= tol && outletMax) outlet.Add(i);
            }
            if (inlet.Count == 0 || outlet.Count == 0) return;

            // Check connectivity; if disconnected, iteratively connect nearest pairs
            var comp = ConnectedSets(adj, pores.Length - 1);
            int inletComp = -1;
            var outletComps = new HashSet<int>();
            foreach (var id in inlet) inletComp = comp[id];
            foreach (var od in outlet) outletComps.Add(comp[od]);

            if (inletComp != -1 && outletComps.Contains(inletComp)) return; // already connected

            // Build quick centroid array
            var centers = new Vector3[pores.Length];
            for (int i = 1; i < pores.Length; i++) centers[i] = pores[i]?.Position ?? new Vector3(-1, -1, -1);

            // Greedy: connect nearest components (inlet comp to any outlet comp) until connected
            // Weight (edge) is Euclidean distance in physical space (scaled by voxel edges).
            Vector3 scale = new((float)vx, (float)vy, (float)vz);

            // Compile component→members
            var compMembers = new Dictionary<int, List<int>>();
            for (int i = 1; i < pores.Length; i++)
            {
                if (pores[i] == null) continue;
                int c = comp[i];
                if (!compMembers.TryGetValue(c, out var list)) compMembers[c] = list = new List<int>();
                list.Add(i);
            }

            int safety = 0;
            while (safety++ < 1000)
            {
                // Recompute comps
                comp = ConnectedSets(adj, pores.Length - 1);
                inletComp = comp[inlet.First()];
                bool ok = false;
                foreach (var od in outlet)
                {
                    if (comp[od] == inletComp) { ok = true; break; }
                }
                if (ok) break;

                // Find nearest pair between any member of inletComp and any member of (one) outlet comp
                float bestDist = float.MaxValue; (int a, int b) best = default;
                foreach (var kv in compMembers)
                {
                    int c = kv.Key;
                    if (c == inletComp) continue;
                    if (!kv.Value.Any(v => outlet.Contains(v))) continue; // only outlet-side comps

                    foreach (var ai in compMembers[inletComp])
                    foreach (var bi in kv.Value)
                    {
                        var d = Vector3.Distance(centers[ai] * scale, centers[bi] * scale);
                        if (d < bestDist)
                        {
                            bestDist = d; best = (ai, bi);
                        }
                    }
                }
                if (best.a == 0) break;

                // Add synthetic throat
                var newT = new Throat
                {
                    Pore1ID = best.a,
                    Pore2ID = best.b,
                    Radius = Math.Max(1e-3f, Math.Min(pores[best.a].Radius, pores[best.b].Radius) * 0.5f)
                };
                throats.Add(newT);
                if (!adj.TryGetValue(best.a, out var la)) adj[best.a] = la = new List<(int nb, float w)>();
                if (!adj.TryGetValue(best.b, out var lb)) adj[best.b] = lb = new List<(int nb, float w)>();
                float wght = Vector3.Distance(centers[best.a] * scale, centers[best.b] * scale);
                la.Add((best.b, wght)); lb.Add((best.a, wght));
            }
        }

        private static Dictionary<int, int> ConnectedSets(Dictionary<int, List<(int nb, float w)>> adj, int maxId)
        {
            var comp = new Dictionary<int, int>();
            int cid = 0;
            var visited = new HashSet<int>();
            for (int i = 1; i <= maxId; i++)
            {
                if (visited.Contains(i) || !adj.ContainsKey(i)) continue;
                cid++;
                var q = new Queue<int>();
                q.Enqueue(i); visited.Add(i); comp[i] = cid;
                while (q.Count > 0)
                {
                    int u = q.Dequeue();
                    if (!adj.TryGetValue(u, out var list)) continue;
                    foreach (var (v, _) in list)
                    {
                        if (visited.Add(v))
                        {
                            comp[v] = cid; q.Enqueue(v);
                        }
                    }
                }
            }
            return comp;
        }

        private static float ComputeTortuosity(
    Pore[] pores,
    Dictionary<int, List<(int nb, float w)>> adjacency,
    FlowAxis axis,
    bool inletMin,
    bool outletMax,
    int W, int H, int D,
    float vx, float vy, float vz,
    int[] labels,              // NEW: full label field (for contacts)
    int componentCount)        // NEW: number of pore components
{
    if (pores == null || pores.Length <= 1 || adjacency == null || adjacency.Count == 0)
        return 0f;

    // 1) Build/refresh edge weights = physical centroid distance
    var pos = new Vector3[pores.Length];
    for (int i = 1; i < pores.Length; i++)
        pos[i] = pores[i]?.Position ?? new Vector3(-1);

    Vector3 scale = new(vx, vy, vz);

    foreach (var kv in adjacency)
    {
        int u = kv.Key;
        var list = kv.Value;
        for (int i = 0; i < list.Count; i++)
        {
            int v = list[i].nb;
            float w = Vector3.Distance(pos[u] * scale, pos[v] * scale);
            list[i] = (v, w);
        }
    }

    // 2) Determine inlet/outlet nodes based on *face contacts* (robust for macropores)
    var contacts = ComputeFaceContacts(labels, W, H, D, componentCount);
    var inlet = new List<int>();
    var outlet = new List<int>();

    switch (axis)
    {
        case FlowAxis.X:
            for (int i = 1; i < pores.Length; i++)
            {
                if (pores[i] == null) continue;
                if (inletMin  && contacts.XMin[i]) inlet.Add(i);
                if (outletMax && contacts.XMax[i]) outlet.Add(i);
            }
            break;

        case FlowAxis.Y:
            for (int i = 1; i < pores.Length; i++)
            {
                if (pores[i] == null) continue;
                if (inletMin  && contacts.YMin[i]) inlet.Add(i);
                if (outletMax && contacts.YMax[i]) outlet.Add(i);
            }
            break;

        default: // FlowAxis.Z
            for (int i = 1; i < pores.Length; i++)
            {
                if (pores[i] == null) continue;
                if (inletMin  && contacts.ZMin[i]) inlet.Add(i);
                if (outletMax && contacts.ZMax[i]) outlet.Add(i);
            }
            break;
    }

    if (inlet.Count == 0 || outlet.Count == 0)
        return 0f; // no valid path endpoints

    // 3) Dijkstra from each inlet to closest outlet
    float best = float.MaxValue;

    foreach (int s in inlet)
    {
        var dist = new Dictionary<int, float>(capacity: Math.Max(64, adjacency.Count));
        var visited = new HashSet<int>();
        var pq = new PriorityQueue<int, float>();

        dist[s] = 0f;
        pq.Enqueue(s, 0f);

        while (pq.Count > 0)
        {
            pq.TryDequeue(out int u, out float du);
            if (!visited.Add(u)) continue;

            if (outlet.Contains(u))
            {
                if (du < best) best = du;
                break;
            }

            if (!adjacency.TryGetValue(u, out var list)) continue;
            for (int i = 0; i < list.Count; i++)
            {
                var (v, w) = list[i];
                float nd = du + w;
                if (!dist.TryGetValue(v, out float dv) || nd < dv)
                {
                    dist[v] = nd;
                    pq.Enqueue(v, nd);
                }
            }
        }
    }

    if (best == float.MaxValue)
        return 0f; // disconnected

    // 4) Normalize by physical sample length along the flow axis
    float L = axis == FlowAxis.X ? vx * Math.Max(1, W - 1)
             : axis == FlowAxis.Y ? vy * Math.Max(1, H - 1)
             :                      vz * Math.Max(1, D - 1);

    return (L > 0f) ? (best / L) : 0f;
}


        #endregion

        #region OpenCL helpers

        private static CL _cl;
        private static bool _clReady;
        private static nint _ctx, _queue, _prog;
        private static float[] _lastDist;

        private static void EnsureCL()
        {
            if (_clReady) return;
            _cl = CL.GetApi();

            unsafe
            {
                uint nPlatforms = 0;
                _cl.GetPlatformIDs(0, null, &nPlatforms);
                if (nPlatforms == 0) throw new Exception("No OpenCL platforms found.");

                var platforms = stackalloc nint[(int)nPlatforms];
                _cl.GetPlatformIDs(nPlatforms, platforms, null);

                nint device = 0;
                nint platform = 0;

                for (int i = 0; i < nPlatforms; i++)
                {
                    platform = platforms[i];

                    uint nDevices = 0;
                    _cl.GetDeviceIDs(platform, DeviceType.Gpu, 0, null, &nDevices);
                    if (nDevices == 0)
                        _cl.GetDeviceIDs(platform, DeviceType.Cpu, 0, null, &nDevices);
                    if (nDevices == 0) continue;

                    var devices = stackalloc nint[(int)nDevices];
                    _cl.GetDeviceIDs(platform, DeviceType.All, nDevices, devices, null);
                    device = devices[0];
                    if (device != 0) break;
                }

                if (device == 0) throw new Exception("No OpenCL devices found.");

                int err;
                _ctx = _cl.CreateContext(null, 1, &device, null, null, &err);
                if (err != 0) throw new Exception("clCreateContext failed: " + err);

                _queue = _cl.CreateCommandQueue(_ctx, device, CommandQueueProperties.None, &err);
                if (err != 0) throw new Exception("clCreateCommandQueue failed: " + err);

                // Build program from string source
                string[] sources = new[] { OpenCLKernels };
                var srcLen = (nuint)OpenCLKernels.Length;
                _prog = _cl.CreateProgramWithSource(_ctx, 1, sources, in srcLen, &err);
                if (err != 0) throw new Exception("clCreateProgramWithSource failed: " + err);

                err = _cl.BuildProgram(_prog, 0, null, string.Empty, null, null);
                if (err != 0) throw new Exception("clBuildProgram failed: " + err);

                _clReady = true;
            }
        }
private static bool TryBinaryErosionOpenCL(byte[] src, byte[] dst, int W, int H, int D, int neigh)
{
    try
    {
        EnsureCL();
        unsafe
        {
            int err;
            nuint count = (nuint)((long)W * H * D);

            // Allocate device buffers (no CopyHostPtr to avoid pinning issues)
            var srcBuf = _cl.CreateBuffer(_ctx, MemFlags.ReadOnly, count, null, &err);
            if (err != 0) throw new Exception("CreateBuffer src failed: " + err);
            var dstBuf = _cl.CreateBuffer(_ctx, MemFlags.WriteOnly, count, null, &err);
            if (err != 0) throw new Exception("CreateBuffer dst failed: " + err);

            fixed (byte* pSrc = src)
            {
                err = _cl.EnqueueWriteBuffer(_queue, srcBuf, true, 0, count, pSrc, 0, null, null);
                if (err != 0) throw new Exception("EnqueueWriteBuffer src failed: " + err);
            }

            int kernelErr;
            var kernel = _cl.CreateKernel(_prog, "binary_erosion", &kernelErr);
            if (kernelErr != 0) throw new Exception("CreateKernel failed: " + kernelErr);

            // Set args (use generic overloads with sizes)
            _cl.SetKernelArg(kernel, 0, (nuint)IntPtr.Size, in srcBuf);
            _cl.SetKernelArg(kernel, 1, (nuint)IntPtr.Size, in dstBuf);
            _cl.SetKernelArg(kernel, 2, (nuint)sizeof(int), in W);
            _cl.SetKernelArg(kernel, 3, (nuint)sizeof(int), in H);
            _cl.SetKernelArg(kernel, 4, (nuint)sizeof(int), in D);
            _cl.SetKernelArg(kernel, 5, (nuint)sizeof(int), in neigh);

            nuint[] gws = { (nuint)W, (nuint)H, (nuint)D };
            fixed (nuint* pg = gws)
            {
                err = _cl.EnqueueNdrangeKernel(_queue, kernel, 3, null, pg, null, 0, null, null);
                if (err != 0) throw new Exception("EnqueueNdrangeKernel failed: " + err);
            }

            _cl.Finish(_queue);

            fixed (byte* pDst = dst)
            {
                err = _cl.EnqueueReadBuffer(_queue, dstBuf, true, 0, count, pDst, 0, null, null);
                if (err != 0) throw new Exception("EnqueueReadBuffer failed: " + err);
            }

            _cl.ReleaseKernel(kernel);
            _cl.ReleaseMemObject(srcBuf);
            _cl.ReleaseMemObject(dstBuf);
            return true;
        }
    }
    catch (Exception ex)
    {
        Logger.LogWarning($"[OpenCL] Erosion fallback to CPU: {ex.Message}");
        return false;
    }
}


        private static bool TryDistanceRelaxOpenCL(byte[] mask, int W, int H, int D)
{
    try
    {
        EnsureCL();
        unsafe
        {
            int err;
            long n = (long)W * H * D;
            nuint bytesMask = (nuint)n;
            nuint bytesDist = (nuint)(n * sizeof(float));

            // Host dist init
            var distHost = new float[n];
            for (long i = 0; i < n; i++) distHost[i] = (mask[i] == 0) ? 0f : 1e6f;

            var distBuf = _cl.CreateBuffer(_ctx, MemFlags.ReadWrite, bytesDist, null, &err);
            if (err != 0) throw new Exception("CreateBuffer dist failed: " + err);

            var maskBuf = _cl.CreateBuffer(_ctx, MemFlags.ReadOnly, bytesMask, null, &err);
            if (err != 0) throw new Exception("CreateBuffer mask failed: " + err);

            fixed (float* pd = distHost)
            fixed (byte* pm = mask)
            {
                err = _cl.EnqueueWriteBuffer(_queue, distBuf, true, 0, bytesDist, pd, 0, null, null);
                if (err != 0) throw new Exception("Write dist failed: " + err);
                err = _cl.EnqueueWriteBuffer(_queue, maskBuf, true, 0, bytesMask, pm, 0, null, null);
                if (err != 0) throw new Exception("Write mask failed: " + err);
            }

            int kernelErr;
            var kernel = _cl.CreateKernel(_prog, "edt_relax", &kernelErr);
            if (kernelErr != 0) throw new Exception("CreateKernel edt_relax failed: " + kernelErr);

            _cl.SetKernelArg(kernel, 0, (nuint)IntPtr.Size, in distBuf);
            _cl.SetKernelArg(kernel, 1, (nuint)IntPtr.Size, in maskBuf);
            _cl.SetKernelArg(kernel, 2, (nuint)sizeof(int), in W);
            _cl.SetKernelArg(kernel, 3, (nuint)sizeof(int), in H);
            _cl.SetKernelArg(kernel, 4, (nuint)sizeof(int), in D);

            nuint[] gws = { (nuint)W, (nuint)H, (nuint)D };
            fixed (nuint* pg = gws)
            {
                // a few relaxation sweeps
                for (int it = 0; it < 4; it++)
                {
                    int e1 = _cl.EnqueueNdrangeKernel(_queue, kernel, 3, null, pg, null, 0, null, null);
                    if (e1 != 0) throw new Exception("EnqueueNdrangeKernel edt failed: " + e1);
                    _cl.Finish(_queue);
                }
            }

            _lastDist = new float[n];
            fixed (float* pr = _lastDist)
            {
                int e2 = _cl.EnqueueReadBuffer(_queue, distBuf, true, 0, bytesDist, pr, 0, null, null);
                if (e2 != 0) throw new Exception("Read dist failed: " + e2);
            }

            _cl.ReleaseKernel(kernel);
            _cl.ReleaseMemObject(distBuf);
            _cl.ReleaseMemObject(maskBuf);
            return true;
        }
    }
    catch (Exception ex)
    {
        Logger.LogWarning($"[OpenCL] EDT fallback to CPU: {ex.Message}");
        _lastDist = null;
        return false;
    }
}


        #endregion
       
        private static PoreFaceContacts ComputeFaceContacts(int[] labels, int W, int H, int D, int maxLabel)
        {
            var c = new PoreFaceContacts
            {
                XMin = new bool[maxLabel + 1],
                XMax = new bool[maxLabel + 1],
                YMin = new bool[maxLabel + 1],
                YMax = new bool[maxLabel + 1],
                ZMin = new bool[maxLabel + 1],
                ZMax = new bool[maxLabel + 1]
            };

            // X faces (x=0 and x=W-1)
            for (int z = 0; z < D; z++)
            {
                for (int y = 0; y < H; y++)
                {
                    int idxL = (z * H + y) * W + 0;
                    int idxR = (z * H + y) * W + (W - 1);
                    int l = labels[idxL]; if (l > 0) c.XMin[l] = true;
                    int r = labels[idxR]; if (r > 0) c.XMax[r] = true;
                }
            }

            // Y faces (y=0 and y=H-1)
            for (int z = 0; z < D; z++)
            {
                int rowTop = (z * H + 0) * W;
                int rowBot = (z * H + (H - 1)) * W;
                for (int x = 0; x < W; x++)
                {
                    int t = labels[rowTop + x]; if (t > 0) c.YMin[t] = true;
                    int b = labels[rowBot + x]; if (b > 0) c.YMax[b] = true;
                }
            }

            // Z faces (z=0 and z=D-1)
            for (int y = 0; y < H; y++)
            {
                int slabF = (0 * H + y) * W;
                int slabB = ((D - 1) * H + y) * W;
                for (int x = 0; x < W; x++)
                {
                    int f = labels[slabF + x]; if (f > 0) c.ZMin[f] = true;
                    int b = labels[slabB + x]; if (b > 0) c.ZMax[b] = true;
                }
            }

            return c;
        }

    }

    #endregion

    #region UI Tool (ImGui): runs generator with a progress bar and adds PNMDataset

    public sealed class PNMGenerationTool : IDatasetTools
    {
        private int _materialIndex = 0;
        private int _neighIndex = 2; // default 26
        private int _modeIndex = 0;  // Conservative
        private bool _useOpenCL = false;

        private bool _enforce = false;
        private int _axisIndex = 2; // Z
        private bool _inMin = true;
        private bool _outMax = true;

        // live state
        private float _progress = 0f;
        private bool _isRunning = false;
        private string _status = "";

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ct) return;

            ImGui.Text("PNM Generator");
            ImGui.Separator();

            // Materials (ignore exterior ID 0)
            var materials = ct.Materials.Where(m => m.ID != 0).ToList();
            if (materials.Count == 0)
            {
                ImGui.TextDisabled("No materials available. Define materials in the CT dataset first.");
                return;
            }
            var matNames = materials.Select(m => $"{m.Name} (ID {m.ID})").ToArray();
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("Segmented Material", ref _materialIndex, matNames, matNames.Length);

            // Neighborhood
            string[] neighs = { "6-neighborhood", "18-neighborhood", "26-neighborhood" };
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("Neighborhood", ref _neighIndex, neighs, neighs.Length);

            // Mode
            string[] modes = { "Conservative", "Aggressive" };
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("Generation Mode", ref _modeIndex, modes, modes.Length);

            ImGui.Checkbox("Use OpenCL (Silk.NET)", ref _useOpenCL);

            // Inlet/Outlet
            ImGui.Separator();
            ImGui.Text("Inlet–Outlet (absolute perm setup)");
            ImGui.Checkbox("Enforce inlet↔outlet connectivity", ref _enforce);
            string[] axes = { "X", "Y", "Z" };
            ImGui.Combo("Flow Axis", ref _axisIndex, axes, axes.Length);
            ImGui.Checkbox("Inlet = Min-face", ref _inMin);
            ImGui.SameLine();
            ImGui.Checkbox("Outlet = Max-face", ref _outMax);

            ImGui.Separator();

            // Buttons
            if (_isRunning)
            {
                ImGui.ProgressBar(_progress, new System.Numerics.Vector2(-1, 0));
                if (!string.IsNullOrWhiteSpace(_status)) ImGui.TextDisabled(_status);
                ImGui.BeginDisabled();
                ImGui.Button("Generate PNM", new System.Numerics.Vector2(-1, 0));
                ImGui.EndDisabled();
            }
            else
            {
                if (ImGui.Button("Generate PNM", new System.Numerics.Vector2(-1, 0)))
                {
                    var opt = new PNMGeneratorOptions
                    {
                        MaterialId = materials[Math.Clamp(_materialIndex, 0, materials.Count - 1)].ID,
                        Neighborhood = _neighIndex == 0 ? Neighborhood3D.N6 : _neighIndex == 1 ? Neighborhood3D.N18 : Neighborhood3D.N26,
                        Mode = _modeIndex == 0 ? GenerationMode.Conservative : GenerationMode.Aggressive,
                        UseOpenCL = _useOpenCL,
                        EnforceInletOutletConnectivity = _enforce,
                        Axis = (FlowAxis)_axisIndex,
                        InletIsMinSide = _inMin,
                        OutletIsMaxSide = _outMax
                    };

                    _isRunning = true; _progress = 0f; _status = "Starting…";
                    var cts = new CancellationTokenSource();
                    var progress = new Progress<float>(p => _progress = Math.Clamp(p, 0f, 1f));

                    Task.Run(() =>
                    {
                        try
                        {
                            var pnm = PNMGenerator.Generate(ct, opt, progress, cts.Token);
                            _status = $"PNM generated: {pnm.Pores.Count:N0} pores, {pnm.Throats.Count:N0} throats.";
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("[PNMGenerationTool] " + ex);
                            _status = "Error: " + ex.Message;
                        }
                        finally { _isRunning = false; }
                    });
                }
            }
        }
    }

    #endregion
}
