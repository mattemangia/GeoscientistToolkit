// GeoscientistToolkit/Analysis/Pnm/PNMGenerator.cs
// Fixed version with proper throat detection and detailed progress reporting
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
        public FlowAxis Axis { get; set; } = FlowAxis.Z;
        public bool InletIsMinSide { get; set; } = true;
        public bool OutletIsMaxSide { get; set; } = true;

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

            var progressReporter = new DetailedProgressReporter(progress);
            progressReporter.Report(0.01f, "Initializing PNM generation...");

            // Voxel geometry (µm) — we take geometric mean if anisotropic spacing
            double vx = Math.Max(1e-9, ct.PixelSize);
            double vy = Math.Max(1e-9, ct.PixelSize);
            double vz = Math.Max(1e-9, ct.SliceThickness > 0 ? ct.SliceThickness : ct.PixelSize);
            double vEdge = Math.Pow(vx * vy * vz, 1.0 / 3.0);

            int W = ct.Width, H = ct.Height, D = ct.Depth;
            var labels = ct.LabelData;

            // Step 1: Extract material mask
            progressReporter.Report(0.05f, "Extracting material mask...");
            var originalMask = new byte[W * H * D];
            Parallel.For(0, D, z =>
            {
                int plane = W * H;
                for (int y = 0; y < H; y++)
                {
                    int row = (z * H + y) * W;
                    for (int x = 0; x < W; x++)
                    {
                        originalMask[row + x] = (labels[x, y, z] == (byte)opt.MaterialId) ? (byte)1 : (byte)0;
                    }
                }
                if (z % 10 == 0)
                    progressReporter.Report(0.05f + 0.05f * z / D, $"Extracting slice {z}/{D}...");
            });

            token.ThrowIfCancellationRequested();

            // Step 2: Split constrictions by erosion (work on a copy)
            byte[] erodedMask = (byte[])originalMask.Clone();
            int erosions = opt.Mode == GenerationMode.Conservative ? opt.ConservativeErosions : opt.AggressiveErosions;
            
            if (erosions > 0)
            {
                progressReporter.Report(0.10f, $"Applying {erosions} erosion(s) to identify pore centers...");
                var tmp = new byte[erodedMask.Length];
                for (int i = 0; i < erosions; i++)
                {
                    progressReporter.Report(0.10f + 0.05f * i / erosions, $"Erosion pass {i + 1}/{erosions}...");
                    
                    if (opt.UseOpenCL && TryBinaryErosionOpenCL(erodedMask, tmp, W, H, D, (int)opt.Neighborhood))
                    {
                        var t = erodedMask; erodedMask = tmp; tmp = t;
                    }
                    else
                    {
                        BinaryErosionCPU(erodedMask, tmp, W, H, D, opt.Neighborhood);
                        var t = erodedMask; erodedMask = tmp; tmp = t;
                    }
                    token.ThrowIfCancellationRequested();
                }
            }

            // Step 3: Connected-component labeling on ERODED mask → pore centers
            progressReporter.Report(0.20f, "Identifying pore centers via connected components...");
            var ccResult = ConnectedComponents3D(erodedMask, W, H, D, opt.Neighborhood);
            int poreCount = ccResult.ComponentCount;
            Logger.Log($"[PNMGenerator] Found {poreCount} pore centers after erosion");

            if (poreCount == 0)
            {
                Logger.LogWarning("[PNMGenerator] No pores found. Try reducing erosion count or using a different mode.");
                return new PNMDataset($"PNM_{ct.Name}_{opt.MaterialId}_Empty", "")
                {
                    VoxelSize = (float)vEdge,
                    Tortuosity = 0
                };
            }

            token.ThrowIfCancellationRequested();

            // Step 4: Watershed expansion to recover full pore volumes in original mask
            progressReporter.Report(0.30f, "Expanding pores back to original boundaries (watershed)...");
            var fullPoreLabels = WatershedExpansion(ccResult.Labels, originalMask, W, H, D, progressReporter, 0.30f, 0.45f);

            token.ThrowIfCancellationRequested();

            // Step 5: Compute pore statistics on the EXPANDED regions
            progressReporter.Report(0.45f, "Computing pore statistics...");
            var pores = new Pore[poreCount + 1];
            ComputePoreStats(fullPoreLabels, originalMask, W, H, D, vx, vy, vz, pores);

            // Step 6: Estimate pore radii via EDT on original mask
            progressReporter.Report(0.55f, "Calculating distance transform for pore radii...");
            float[] edt = null;
            try
            {
                edt = (opt.UseOpenCL && TryDistanceRelaxOpenCL(originalMask, W, H, D))
                    ? _lastDist
                    : DistanceTransformApproxCPU(originalMask, W, H, D);
            }
            catch { edt = DistanceTransformApproxCPU(originalMask, W, H, D); }
            
            AssignPoreRadiiFromEDT(pores, fullPoreLabels, edt, (float)vEdge);

            token.ThrowIfCancellationRequested();

            // Step 7: Build throats from EXPANDED pore labels
            progressReporter.Report(0.70f, "Building throat network...");
            var throats = BuildThroats(fullPoreLabels, W, H, D, edt, (float)vEdge, out var adjacency, progressReporter, 0.70f, 0.80f);
            
            // Update connection counts
            foreach (var th in throats)
            {
                if (th.Pore1ID > 0 && th.Pore1ID < pores.Length) pores[th.Pore1ID].Connections++;
                if (th.Pore2ID > 0 && th.Pore2ID < pores.Length) pores[th.Pore2ID].Connections++;
            }
            
            Logger.Log($"[PNMGenerator] Found {throats.Count} throats connecting pores");

            token.ThrowIfCancellationRequested();

            // Step 8: Optional inlet↔outlet path enforcement
            if (opt.EnforceInletOutletConnectivity && poreCount > 0)
            {
                progressReporter.Report(0.82f, "Enforcing inlet-outlet connectivity...");
                EnforceInOutConnectivity(pores, adjacency, opt.Axis, opt.InletIsMinSide, opt.OutletIsMaxSide, 
                    W, H, D, (float)vx, (float)vy, (float)vz, throats);
            }

            // Step 9: Calculate tortuosity
            progressReporter.Report(0.90f, "Calculating tortuosity...");
            float tort = ComputeTortuosity(
                pores, adjacency, opt.Axis, opt.InletIsMinSide, opt.OutletIsMaxSide,
                W, H, D, (float)vx, (float)vy, (float)vz,
                fullPoreLabels, poreCount);

            // Step 10: Package into PNMDataset
            progressReporter.Report(0.95f, "Creating PNM dataset...");
            var pnm = new PNMDataset($"PNM_{ct.Name}_Mat{opt.MaterialId}", "")
            {
                VoxelSize = (float)vEdge,
                Tortuosity = tort,
            };

            // Add non-null pores & throats
            for (int i = 1; i < pores.Length; i++)
                if (pores[i] != null) pnm.Pores.Add(pores[i]);

            int tid = 1;
            foreach (var t in throats)
            {
                t.ID = tid++;
                pnm.Throats.Add(t);
            }
            
            pnm.InitializeFromCurrentLists();
            pnm.CalculateBounds();

            // Register with ProjectManager
            try
            {
                GeoscientistToolkit.Business.ProjectManager.Instance.AddDataset(pnm);
                GeoscientistToolkit.Business.ProjectManager.Instance.NotifyDatasetDataChanged(pnm);
                Logger.Log($"[PNMGenerator] Successfully created PNM with {pnm.Pores.Count} pores and {pnm.Throats.Count} throats");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[PNMGenerator] Could not auto-add PNMDataset to ProjectManager: {ex.Message}");
            }

            progressReporter.Report(1.0f, "PNM generation complete!");
            return pnm;
        }

        // NEW: Watershed expansion to recover full pore volumes
        private static int[] WatershedExpansion(int[] seedLabels, byte[] mask, int W, int H, int D, 
            DetailedProgressReporter progress, float startProgress, float endProgress)
        {
            var expanded = new int[seedLabels.Length];
            Array.Copy(seedLabels, expanded, seedLabels.Length);

            // Priority queue for wavefront expansion
            var queue = new Queue<(int idx, int label)>();
            
            // Initialize with all labeled seed voxels
            for (int i = 0; i < seedLabels.Length; i++)
            {
                if (seedLabels[i] > 0 && mask[i] > 0)
                {
                    queue.Enqueue((i, seedLabels[i]));
                }
            }

            int totalVoxels = queue.Count;
            int processedVoxels = 0;
            int lastReportedPercent = 0;

            // Expand labels to neighboring unlabeled voxels within the mask
            while (queue.Count > 0)
            {
                var (idx, label) = queue.Dequeue();
                processedVoxels++;

                // Report progress every 10000 voxels to avoid overhead
                if (processedVoxels % 10000 == 0)
                {
                    int currentPercent = (processedVoxels * 100) / Math.Max(1, totalVoxels);
                    if (currentPercent > lastReportedPercent + 5)
                    {
                        float prog = startProgress + (endProgress - startProgress) * processedVoxels / (float)Math.Max(1, totalVoxels);
                        progress?.Report(prog, $"Watershed expansion: {currentPercent}%...");
                        lastReportedPercent = currentPercent;
                    }
                }

                int z = idx / (W * H);
                int y = (idx % (W * H)) / W;
                int x = idx % W;

                // Check 6-neighbors
                int[] dx = { -1, 1, 0, 0, 0, 0 };
                int[] dy = { 0, 0, -1, 1, 0, 0 };
                int[] dz = { 0, 0, 0, 0, -1, 1 };

                for (int i = 0; i < 6; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];
                    int nz = z + dz[i];

                    if (nx >= 0 && nx < W && ny >= 0 && ny < H && nz >= 0 && nz < D)
                    {
                        int nidx = (nz * H + ny) * W + nx;
                        
                        // If neighbor is in mask but not labeled, assign current label
                        if (mask[nidx] > 0 && expanded[nidx] == 0)
                        {
                            expanded[nidx] = label;
                            queue.Enqueue((nidx, label));
                            totalVoxels++; // Update total for progress calculation
                        }
                    }
                }
            }

            return expanded;
        }
        
        #region CPU morphology & CCL

        private static void BinaryErosionCPU(byte[] src, byte[] dst, int W, int H, int D, Neighborhood3D nbh)
        {
            Array.Clear(dst, 0, dst.Length);

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

            Parallel.For(0, D, z =>
            {
                for (int y = 0; y < H; y++)
                {
                    int row = (z * H + y) * W;
                    for (int x = 0; x < W; x++)
                    {
                        int idx = row + x;

                        if (src[idx] == 0) { dst[idx] = 0; continue; }

                        bool keep = true;

                        if (useCount == 6)
                        {
                            if (Get(src, x - 1, y    , z    , W, H, D) == 0) keep = false;
                            else if (Get(src, x + 1, y    , z    , W, H, D) == 0) keep = false;
                            else if (Get(src, x    , y - 1, z    , W, H, D) == 0) keep = false;
                            else if (Get(src, x    , y + 1, z    , W, H, D) == 0) keep = false;
                            else if (Get(src, x    , y    , z - 1, W, H, D) == 0) keep = false;
                            else if (Get(src, x    , y    , z + 1, W, H, D) == 0) keep = false;
                        }
                        else
                        {
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
            var parent = new List<int> { 0 };
            parent.Add(1);
            int next = 1;

            // 1st pass
            for (int z = 0; z < D; z++)
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int idx = (z * H + y) * W + x;
                if (mask[idx] == 0) continue;

                int minLabel = int.MaxValue;
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

                foreach (var offset in NeighborBackOffsets(x, y, z, W, H, D, nbh))
                {
                    int nlab = labels[idx + offset];
                    if (nlab > 0 && nlab != minLabel)
                    {
                        Union(parent, minLabel, nlab);
                    }
                }
            }

            // 2nd pass: flatten
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
            if (x > 0) yield return -1;
            if (y > 0) yield return -W;
            if (z > 0) yield return -W * H;

            if (nbh == Neighborhood3D.N6) yield break;

            if (x > 0 && y > 0) yield return -1 - W;
            if (x > 0 && z > 0) yield return -1 - W * H;
            if (y > 0 && z > 0) yield return -W - W * H;

            if (nbh == Neighborhood3D.N18) yield break;

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
                    Area = (float)area,
                    VolumeVoxels = vox,
                    VolumePhysical = (float)(vox * vx * vy * vz),
                    Connections = 0,
                    Radius = 0
                };
                pores[id] = p;
            }
        }

        private static float[] DistanceTransformApproxCPU(byte[] mask, int W, int H, int D)
        {
            var dist = new float[mask.Length];
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
                    pores[id].Radius = maxByComp[id] * vEdge;
                }
            }
        }

        #endregion

        #region Throats & adjacency

        private static List<Throat> BuildThroats(int[] lbl, int W, int H, int D, float[] edt, float vEdge, 
            out Dictionary<int, List<(int nb, float w)>> adjacency, DetailedProgressReporter progress, 
            float startProgress, float endProgress)
        {
            var edges = new ConcurrentDictionary<(int a, int b), (float radius, int count)>();
            int totalSlices = D;
            int processedSlices = 0;

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

                        void CheckNeighbor(int nx, int ny, int nz)
                        {
                            if ((uint)nx >= W || (uint)ny >= H || (uint)nz >= D) return;
                            int nidx = (nz * H + ny) * W + nx;
                            int b = lbl[nidx];
                            if (b == 0 || b == a) return;
                            
                            var key = (a < b) ? (a, b) : (b, a);
                            // Calculate throat radius at this interface point
                            float r = MathF.Min(edt[idx], edt[nidx]) * vEdge;
                            
                            edges.AddOrUpdate(key, 
                                k => (r, 1),
                                (k, old) => (Math.Max(old.radius, r), old.count + 1)); // Use MAX radius instead of MIN
                        }

                        CheckNeighbor(x + 1, y, z);
                        CheckNeighbor(x, y + 1, z);
                        CheckNeighbor(x, y, z + 1);
                    }
                }

                int current = Interlocked.Increment(ref processedSlices);
                if (current % 10 == 0)
                {
                    float prog = startProgress + (endProgress - startProgress) * current / (float)totalSlices;
                    progress?.Report(prog, $"Finding throats: slice {current}/{totalSlices}...");
                }
            });

            var list = new List<Throat>(edges.Count);
            adjacency = new Dictionary<int, List<(int nb, float w)>>();
            int tid = 1;
            
            foreach (var kv in edges)
            {
                var (a, b) = kv.Key;
                var (radius, count) = kv.Value;
                
                // Ensure minimum radius for numerical stability
                float finalRadius = Math.Max(0.1f, radius);
                
                list.Add(new Throat { ID = tid++, Pore1ID = a, Pore2ID = b, Radius = finalRadius });

                if (!adjacency.TryGetValue(a, out var la)) adjacency[a] = la = new List<(int nb, float w)>();
                if (!adjacency.TryGetValue(b, out var lb)) adjacency[b] = lb = new List<(int nb, float w)>();
                la.Add((b, 1));
                lb.Add((a, 1));
            }
            
            Logger.Log($"[BuildThroats] Found {list.Count} unique throats with radius range {list.Min(t => t.Radius):F3} to {list.Max(t => t.Radius):F3} µm");

            return list;
        }

        private static void EnforceInOutConnectivity(Pore[] pores, Dictionary<int, List<(int nb, float w)>> adj,
            FlowAxis axis, bool inletMin, bool outletMax, int W, int H, int D, float vx, float vy, float vz, List<Throat> throats)
        {
            if (pores.Length <= 1) return;

            var inlet = new HashSet<int>();
            var outlet = new HashSet<int>();
            float minFace = 0, maxFace = 0, tol = 0.5f;
            switch (axis)
            {
                case FlowAxis.X: minFace = 0; maxFace = W - 1; break;
                case FlowAxis.Y: minFace = 0; maxFace = H - 1; break;
                default: minFace = 0; maxFace = D - 1; break;
            }

            for (int i = 1; i < pores.Length; i++)
            {
                var p = pores[i]; 
                if (p == null) continue;
                float coord = axis == FlowAxis.X ? p.Position.X : axis == FlowAxis.Y ? p.Position.Y : p.Position.Z;
                if (Math.Abs(coord - minFace) <= tol && inletMin) inlet.Add(i);
                if (Math.Abs(coord - maxFace) <= tol && outletMax) outlet.Add(i);
            }
            
            if (inlet.Count == 0 || outlet.Count == 0) return;

            var comp = ConnectedSets(adj, pores.Length - 1);
            int inletComp = -1;
            var outletComps = new HashSet<int>();
            foreach (var id in inlet) inletComp = comp[id];
            foreach (var od in outlet) outletComps.Add(comp[od]);

            if (inletComp != -1 && outletComps.Contains(inletComp)) return;

            var centers = new Vector3[pores.Length];
            for (int i = 1; i < pores.Length; i++) centers[i] = pores[i]?.Position ?? new Vector3(-1, -1, -1);

            Vector3 scale = new((float)vx, (float)vy, (float)vz);

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
                comp = ConnectedSets(adj, pores.Length - 1);
                inletComp = comp[inlet.First()];
                bool ok = false;
                foreach (var od in outlet)
                {
                    if (comp[od] == inletComp) { ok = true; break; }
                }
                if (ok) break;

                float bestDist = float.MaxValue; 
                (int a, int b) best = default;
                foreach (var kv in compMembers)
                {
                    int c = kv.Key;
                    if (c == inletComp) continue;
                    if (!kv.Value.Any(v => outlet.Contains(v))) continue;

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
                la.Add((best.b, wght)); 
                lb.Add((best.a, wght));
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
                q.Enqueue(i); 
                visited.Add(i); 
                comp[i] = cid;
                while (q.Count > 0)
                {
                    int u = q.Dequeue();
                    if (!adj.TryGetValue(u, out var list)) continue;
                    foreach (var (v, _) in list)
                    {
                        if (visited.Add(v))
                        {
                            comp[v] = cid; 
                            q.Enqueue(v);
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
            int[] labels,
            int componentCount)
        {
            if (pores == null || pores.Length <= 1 || adjacency == null || adjacency.Count == 0)
                return 1.0f; // Default tortuosity is 1.0 (straight path)

            var pos = new Vector3[pores.Length];
            for (int i = 1; i < pores.Length; i++)
                pos[i] = pores[i]?.Position ?? new Vector3(-1);

            // Update edge weights to be actual physical distances
            foreach (var kv in adjacency.ToList())
            {
                int u = kv.Key;
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    int v = list[i].nb;
                    // Calculate physical distance between pore centers
                    float dx = Math.Abs(pos[u].X - pos[v].X) * vx;
                    float dy = Math.Abs(pos[u].Y - pos[v].Y) * vy;
                    float dz = Math.Abs(pos[u].Z - pos[v].Z) * vz;
                    float w = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    list[i] = (v, w);
                }
                adjacency[u] = list;
            }

            var contacts = ComputeFaceContacts(labels, W, H, D, componentCount);
            var inlet = new HashSet<int>();
            var outlet = new HashSet<int>();

            switch (axis)
            {
                case FlowAxis.X:
                    for (int i = 1; i < pores.Length; i++)
                    {
                        if (pores[i] == null) continue;
                        if (inletMin && contacts.XMin[i]) inlet.Add(i);
                        if (outletMax && contacts.XMax[i]) outlet.Add(i);
                    }
                    break;

                case FlowAxis.Y:
                    for (int i = 1; i < pores.Length; i++)
                    {
                        if (pores[i] == null) continue;
                        if (inletMin && contacts.YMin[i]) inlet.Add(i);
                        if (outletMax && contacts.YMax[i]) outlet.Add(i);
                    }
                    break;

                default: // FlowAxis.Z
                    for (int i = 1; i < pores.Length; i++)
                    {
                        if (pores[i] == null) continue;
                        if (inletMin && contacts.ZMin[i]) inlet.Add(i);
                        if (outletMax && contacts.ZMax[i]) outlet.Add(i);
                    }
                    break;
            }

            if (inlet.Count == 0 || outlet.Count == 0)
            {
                Logger.LogWarning("[Tortuosity] No inlet or outlet pores found. Using default tortuosity of 1.0");
                return 1.0f;
            }

            // Find shortest path from any inlet to any outlet
            float shortestPath = float.MaxValue;

            foreach (int s in inlet)
            {
                var dist = new Dictionary<int, float>();
                var visited = new HashSet<int>();
                var pq = new PriorityQueue<int, float>();

                dist[s] = 0f;
                pq.Enqueue(s, 0f);

                while (pq.Count > 0)
                {
                    pq.TryDequeue(out int u, out float du);
                    if (!visited.Add(u)) continue;

                    // Check if we reached an outlet
                    if (outlet.Contains(u))
                    {
                        if (du < shortestPath)
                        {
                            shortestPath = du;
                            Logger.Log($"[Tortuosity] Found path from inlet {s} to outlet {u} with length {du:F3} µm");
                        }
                        break; // Found shortest from this inlet
                    }

                    if (!adjacency.TryGetValue(u, out var neighbors)) continue;
                    
                    foreach (var (v, weight) in neighbors)
                    {
                        if (visited.Contains(v)) continue;
                        
                        float newDist = du + weight;
                        if (!dist.ContainsKey(v) || newDist < dist[v])
                        {
                            dist[v] = newDist;
                            pq.Enqueue(v, newDist);
                        }
                    }
                }
            }

            if (shortestPath == float.MaxValue)
            {
                Logger.LogWarning("[Tortuosity] No path found between inlet and outlet. Using default tortuosity of 1.0");
                return 1.0f;
            }

            // Calculate straight-line distance (physical sample length along flow axis)
            float straightLineDistance = axis switch
            {
                FlowAxis.X => W * vx,
                FlowAxis.Y => H * vy,
                _ => D * vz
            };

            // Tortuosity = path length / straight-line distance
            float tortuosity = (straightLineDistance > 0f) ? (shortestPath / straightLineDistance) : 1.0f;
            
            // Clamp to reasonable range (tortuosity should be >= 1.0)
            tortuosity = Math.Max(1.0f, Math.Min(10.0f, tortuosity));
            
            Logger.Log($"[Tortuosity] Calculated: Path={shortestPath:F3} µm, Straight={straightLineDistance:F3} µm, Tortuosity={tortuosity:F3}");
            
            return tortuosity;
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

            // X faces
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

            // Y faces
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

            // Z faces
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

    // Helper class for detailed progress reporting
    public class DetailedProgressReporter
    {
        private readonly IProgress<float> _progress;
        private float _lastReportedProgress;

        public DetailedProgressReporter(IProgress<float> progress)
        {
            _progress = progress;
            _lastReportedProgress = 0;
        }

        public void Report(float progress, string message)
        {
            if (_progress != null && Math.Abs(progress - _lastReportedProgress) > 0.001f)
            {
                _progress.Report(progress);
                _lastReportedProgress = progress;
            }
            if (!string.IsNullOrEmpty(message))
            {
                Logger.Log($"[PNMGenerator] {message}");
            }
        }
    }

    #endregion

    #region UI Tool with Progress Bar

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

        // Progress dialog
        private readonly GeoscientistToolkit.UI.ProgressBarDialog _progressDialog;
        private CancellationTokenSource _cancellationTokenSource;

        public PNMGenerationTool()
        {
            _progressDialog = new GeoscientistToolkit.UI.ProgressBarDialog("PNM Generation");
        }

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
            string[] modes = { "Conservative (1 erosion)", "Aggressive (3 erosions)" };
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("Generation Mode", ref _modeIndex, modes, modes.Length);

            ImGui.Checkbox("Use OpenCL (GPU acceleration)", ref _useOpenCL);

            // Inlet/Outlet
            ImGui.Separator();
            ImGui.Text("Inlet—Outlet (for permeability)");
            ImGui.Checkbox("Enforce inlet↔outlet connectivity", ref _enforce);
            string[] axes = { "X", "Y", "Z" };
            ImGui.Combo("Flow Axis", ref _axisIndex, axes, axes.Length);
            ImGui.Checkbox("Inlet = Min-face", ref _inMin);
            ImGui.SameLine();
            ImGui.Checkbox("Outlet = Max-face", ref _outMax);

            ImGui.Separator();

            // Generate button
            if (_progressDialog.IsActive)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Generating...", new System.Numerics.Vector2(-1, 0));
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

                    StartGeneration(ct, opt);
                }
            }

            // Render progress dialog
            _progressDialog.Submit();
        }

        private void StartGeneration(CtImageStackDataset ct, PNMGeneratorOptions options)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _progressDialog.Open("Starting PNM generation...");

            var progress = new Progress<float>(value =>
            {
                string status = value < 0.1f ? "Extracting material mask..." :
                               value < 0.2f ? "Identifying pore centers..." :
                               value < 0.35f ? "Expanding pore regions..." :
                               value < 0.55f ? "Computing pore statistics..." :
                               value < 0.7f ? "Building throat network..." :
                               value < 0.9f ? "Calculating flow properties..." :
                               "Finalizing PNM dataset...";
                _progressDialog.Update(value, status);
            });

            Task.Run(() =>
            {
                try
                {
                    var pnm = PNMGenerator.Generate(ct, options, progress, _cancellationTokenSource.Token);
                    _progressDialog.Close();
                    Logger.Log($"[PNMGenerationTool] Successfully generated PNM with {pnm.Pores.Count} pores and {pnm.Throats.Count} throats");
                }
                catch (OperationCanceledException)
                {
                    _progressDialog.Close();
                    Logger.Log("[PNMGenerationTool] Generation cancelled by user");
                }
                catch (Exception ex)
                {
                    _progressDialog.Close();
                    Logger.LogError($"[PNMGenerationTool] Generation failed: {ex.Message}");
                }
            });
        }
    }

    #endregion
}