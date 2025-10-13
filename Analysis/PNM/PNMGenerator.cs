// GeoscientistToolkit/Analysis/Pnm/PNMGenerator.cs
// Fixed version with proper throat detection and detailed progress reporting

using System.Collections.Concurrent;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Pnm;

#region Options & enums

public enum Neighborhood3D
{
    N6 = 6,
    N18 = 18,
    N26 = 26
}

public enum GenerationMode
{
    Conservative,
    Aggressive
}

public enum FlowAxis
{
    X,
    Y,
    Z
}

public sealed class PNMGeneratorOptions
{
    public int MaterialId { get; set; }
    public Neighborhood3D Neighborhood { get; set; } = Neighborhood3D.N26;
    public GenerationMode Mode { get; set; } = GenerationMode.Conservative;
    public bool UseOpenCL { get; set; }

    // inlet↔outlet path options for absolute perm setups
    public bool EnforceInletOutletConnectivity { get; set; }
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

    public static PNMDataset Generate(CtImageStackDataset ct, PNMGeneratorOptions opt, IProgress<float> progress,
        CancellationToken token)
    {
        if (ct == null) throw new ArgumentNullException(nameof(ct));
        if (ct.LabelData == null) throw new InvalidOperationException("CtImageStackDataset has no LabelData loaded.");
        if (opt.MaterialId == 0)
            throw new ArgumentException("Please select a non-zero material ID (0 is reserved for Exterior).");

        var progressReporter = new DetailedProgressReporter(progress);
        progressReporter.Report(0.01f, "Initializing PNM generation...");

        // Voxel geometry (µm) — we take geometric mean if anisotropic spacing
        var vx = Math.Max(1e-9, ct.PixelSize);
        var vy = Math.Max(1e-9, ct.PixelSize);
        var vz = Math.Max(1e-9, ct.SliceThickness > 0 ? ct.SliceThickness : ct.PixelSize);
        var vEdge = Math.Pow(vx * vy * vz, 1.0 / 3.0);

        int W = ct.Width, H = ct.Height, D = ct.Depth;
        var labels = ct.LabelData;

        // Step 1: Extract material mask
        progressReporter.Report(0.05f, "Extracting material mask...");
        var originalMask = new byte[W * H * D];
        Parallel.For(0, D, z =>
        {
            var plane = W * H;
            for (var y = 0; y < H; y++)
            {
                var row = (z * H + y) * W;
                for (var x = 0; x < W; x++)
                    originalMask[row + x] = labels[x, y, z] == (byte)opt.MaterialId ? (byte)1 : (byte)0;
            }

            if (z % 10 == 0)
                progressReporter.Report(0.05f + 0.05f * z / D, $"Extracting slice {z}/{D}...");
        });

        token.ThrowIfCancellationRequested();

        // Step 2: Split constrictions by erosion (work on a copy)
        var erodedMask = (byte[])originalMask.Clone();
        var erosions = opt.Mode == GenerationMode.Conservative ? opt.ConservativeErosions : opt.AggressiveErosions;

        if (erosions > 0)
        {
            progressReporter.Report(0.10f, $"Applying {erosions} erosion(s) to identify pore centers...");
            var tmp = new byte[erodedMask.Length];
            for (var i = 0; i < erosions; i++)
            {
                progressReporter.Report(0.10f + 0.05f * i / erosions, $"Erosion pass {i + 1}/{erosions}...");

                if (opt.UseOpenCL && TryBinaryErosionOpenCL(erodedMask, tmp, W, H, D, (int)opt.Neighborhood))
                {
                    var t = erodedMask;
                    erodedMask = tmp;
                    tmp = t;
                }
                else
                {
                    BinaryErosionCPU(erodedMask, tmp, W, H, D, opt.Neighborhood);
                    var t = erodedMask;
                    erodedMask = tmp;
                    tmp = t;
                }

                token.ThrowIfCancellationRequested();
            }
        }

        // Step 3: Connected-component labeling on ERODED mask → pore centers
        progressReporter.Report(0.20f, "Identifying pore centers via connected components...");
        var ccResult = ConnectedComponents3D(erodedMask, W, H, D, opt.Neighborhood);
        var poreCount = ccResult.ComponentCount;
        Logger.Log($"[PNMGenerator] Found {poreCount} pore centers after erosion");

        if (poreCount == 0)
        {
            Logger.LogWarning("[PNMGenerator] No pores found. Try reducing erosion count or using a different mode.");
            return new PNMDataset($"PNM_{ct.Name}_{opt.MaterialId}_Empty", "")
            {
                VoxelSize = (float)vEdge,
                Tortuosity = 0,
                ImageWidth = ct.Width,
                ImageHeight = ct.Height,
                ImageDepth = ct.Depth
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
            edt = opt.UseOpenCL && TryDistanceRelaxOpenCL(originalMask, W, H, D)
                ? _lastDist
                : DistanceTransformApproxCPU(originalMask, W, H, D);
        }
        catch
        {
            edt = DistanceTransformApproxCPU(originalMask, W, H, D);
        }

        AssignPoreRadiiFromEDT(pores, fullPoreLabels, edt, (float)vEdge);

        token.ThrowIfCancellationRequested();

        // Step 7: Build throats from EXPANDED pore labels
        progressReporter.Report(0.70f, "Building throat network...");
        var throats = BuildThroats(fullPoreLabels, W, H, D, edt, (float)vEdge, out var adjacency, progressReporter,
            0.70f, 0.80f);

        // Update connection counts
        foreach (var th in throats)
        {
            if (th.Pore1ID > 0 && th.Pore1ID < pores.Length) pores[th.Pore1ID].Connections++;
            if (th.Pore2ID > 0 && th.Pore2ID < pores.Length) pores[th.Pore2ID].Connections++;
        }

        Logger.Log($"[PNMGenerator] Found {throats.Count} throats connecting pores");

        token.ThrowIfCancellationRequested();

        if (opt.EnforceInletOutletConnectivity && poreCount > 0)
{
    progressReporter.Report(0.82f, "Enforcing inlet-outlet connectivity...");
    EnforceInOutConnectivity(
        pores, adjacency, opt.Axis, opt.InletIsMinSide, opt.OutletIsMaxSide,
        W, H, D, (float)vx, (float)vy, (float)vz,
        throats,
        fullPoreLabels, poreCount
    );
    
    // CRITICAL: Rebuild adjacency after adding bridging throats!
    Logger.Log("[PNMGenerator] Rebuilding adjacency after connectivity enforcement...");
    adjacency.Clear();
    foreach (var throat in throats)
    {
        var p1 = pores.FirstOrDefault(p => p?.ID == throat.Pore1ID);
        var p2 = pores.FirstOrDefault(p => p?.ID == throat.Pore2ID);
        if (p1 == null || p2 == null) continue;
        
        var dx = Math.Abs(p1.Position.X - p2.Position.X) * vx;
        var dy = Math.Abs(p1.Position.Y - p2.Position.Y) * vy;
        var dz = Math.Abs(p1.Position.Z - p2.Position.Z) * vz;
        var dist = MathF.Sqrt((float)(dx * dx + dy * dy + dz * dz));
        
        // Find pore indices (not IDs)
        int p1Idx = -1, p2Idx = -1;
        for (int i = 1; i < pores.Length; i++)
        {
            if (pores[i]?.ID == throat.Pore1ID) p1Idx = i;
            if (pores[i]?.ID == throat.Pore2ID) p2Idx = i;
        }
        
        if (p1Idx > 0 && p2Idx > 0)
        {
            if (!adjacency.TryGetValue(p1Idx, out var l1)) 
                adjacency[p1Idx] = l1 = new List<(int, float)>();
            if (!adjacency.TryGetValue(p2Idx, out var l2)) 
                adjacency[p2Idx] = l2 = new List<(int, float)>();
            
            // Avoid duplicates
            if (!l1.Any(x => x.Item1 == p2Idx)) l1.Add((p2Idx, dist));
            if (!l2.Any(x => x.Item1 == p1Idx)) l2.Add((p1Idx, dist));
        }
    }
}

// Step 9: Calculate tortuosity (now with updated adjacency)
progressReporter.Report(0.90f, "Calculating tortuosity...");
var tort = ComputeTortuosity(
    pores, adjacency, opt.Axis, opt.InletIsMinSide, opt.OutletIsMaxSide,
    W, H, D, (float)vx, (float)vy, (float)vz,
    fullPoreLabels, poreCount);

        // Step 10: Package into PNMDataset
        progressReporter.Report(0.95f, "Creating PNM dataset...");
        var pnm = new PNMDataset($"PNM_{ct.Name}_Mat{opt.MaterialId}", "")
        {
            VoxelSize = (float)vEdge,
            Tortuosity = tort
        };

        // Add non-null pores & throats
        for (var i = 1; i < pores.Length; i++)
            if (pores[i] != null)
                pnm.Pores.Add(pores[i]);

        var tid = 1;
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
            ProjectManager.Instance.AddDataset(pnm);
            ProjectManager.Instance.NotifyDatasetDataChanged(pnm);
            Logger.Log(
                $"[PNMGenerator] Successfully created PNM with {pnm.Pores.Count} pores and {pnm.Throats.Count} throats");
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
        for (var i = 0; i < seedLabels.Length; i++)
            if (seedLabels[i] > 0 && mask[i] > 0)
                queue.Enqueue((i, seedLabels[i]));

        var totalVoxels = queue.Count;
        var processedVoxels = 0;
        var lastReportedPercent = 0;

        // Expand labels to neighboring unlabeled voxels within the mask
        while (queue.Count > 0)
        {
            var (idx, label) = queue.Dequeue();
            processedVoxels++;

            // Report progress every 10000 voxels to avoid overhead
            if (processedVoxels % 10000 == 0)
            {
                var currentPercent = processedVoxels * 100 / Math.Max(1, totalVoxels);
                if (currentPercent > lastReportedPercent + 5)
                {
                    var prog = startProgress +
                               (endProgress - startProgress) * processedVoxels / Math.Max(1, totalVoxels);
                    progress?.Report(prog, $"Watershed expansion: {currentPercent}%...");
                    lastReportedPercent = currentPercent;
                }
            }

            var z = idx / (W * H);
            var y = idx % (W * H) / W;
            var x = idx % W;

            // Check 6-neighbors
            int[] dx = { -1, 1, 0, 0, 0, 0 };
            int[] dy = { 0, 0, -1, 1, 0, 0 };
            int[] dz = { 0, 0, 0, 0, -1, 1 };

            for (var i = 0; i < 6; i++)
            {
                var nx = x + dx[i];
                var ny = y + dy[i];
                var nz = z + dz[i];

                if (nx >= 0 && nx < W && ny >= 0 && ny < H && nz >= 0 && nz < D)
                {
                    var nidx = (nz * H + ny) * W + nx;

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
    struct FaceContacts
    {
        public bool[] XMin, XMax, YMin, YMax, ZMin, ZMax;
        public FaceContacts(int n)
        {
            XMin = new bool[n]; XMax = new bool[n];
            YMin = new bool[n]; YMax = new bool[n];
            ZMin = new bool[n]; ZMax = new bool[n];
        }
    }
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

    // First, find the actual extent of the material (non-zero labels)
    int xMin = W, xMax = -1;
    int yMin = H, yMax = -1;
    int zMin = D, zMax = -1;
    
    for (var z = 0; z < D; z++)
    for (var y = 0; y < H; y++)
    for (var x = 0; x < W; x++)
    {
        var idx = (z * H + y) * W + x;
        if (labels[idx] > 0)
        {
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
            if (z < zMin) zMin = z;
            if (z > zMax) zMax = z;
        }
    }
    
    // If no material found, return empty
    if (xMax < 0) return c;
    
    Logger.Log($"[Face Contacts] Material extent: X[{xMin},{xMax}], Y[{yMin},{yMax}], Z[{zMin},{zMax}]");
    
    // Use a tolerance band (e.g., within 5 voxels of material boundaries)
    const int TOLERANCE = 5;
    
    // Check which pores are near the material boundaries (not image boundaries)
    for (var z = 0; z < D; z++)
    for (var y = 0; y < H; y++)
    for (var x = 0; x < W; x++)
    {
        var idx = (z * H + y) * W + x;
        var label = labels[idx];
        if (label <= 0) continue;
        
        // X boundaries
        if (x <= xMin + TOLERANCE) c.XMin[label] = true;
        if (x >= xMax - TOLERANCE) c.XMax[label] = true;
        
        // Y boundaries
        if (y <= yMin + TOLERANCE) c.YMin[label] = true;
        if (y >= yMax - TOLERANCE) c.YMax[label] = true;
        
        // Z boundaries
        if (z <= zMin + TOLERANCE) c.ZMin[label] = true;
        if (z >= zMax - TOLERANCE) c.ZMax[label] = true;
    }
    
    // Count how many pores touch each face
    int xMinCount = 0, xMaxCount = 0, yMinCount = 0, yMaxCount = 0, zMinCount = 0, zMaxCount = 0;
    for (int i = 1; i <= maxLabel; i++)
    {
        if (c.XMin[i]) xMinCount++;
        if (c.XMax[i]) xMaxCount++;
        if (c.YMin[i]) yMinCount++;
        if (c.YMax[i]) yMaxCount++;
        if (c.ZMin[i]) zMinCount++;
        if (c.ZMax[i]) zMaxCount++;
    }
    
    Logger.Log($"[Face Contacts] Boundary pores found: XMin={xMinCount}, XMax={xMaxCount}, " +
               $"YMin={yMinCount}, YMax={yMaxCount}, ZMin={zMinCount}, ZMax={zMaxCount}");
    
    return c;
}
    private sealed class PoreFaceContacts
    {
        public bool[] XMin, XMax, YMin, YMax, ZMin, ZMax;
    }

    #region CPU morphology & CCL

    private static void BinaryErosionCPU(byte[] src, byte[] dst, int W, int H, int D, Neighborhood3D nbh)
    {
        Array.Clear(dst, 0, dst.Length);

        var OFF26 = new (int dx, int dy, int dz)[]
        {
            // 6-neigh (faces)
            (-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1),
            // edges (12)
            (-1, -1, 0), (-1, 1, 0), (1, -1, 0), (1, 1, 0),
            (-1, 0, -1), (-1, 0, 1), (1, 0, -1), (1, 0, 1),
            (0, -1, -1), (0, -1, 1), (0, 1, -1), (0, 1, 1),
            // corners (8)
            (-1, -1, -1), (-1, -1, 1), (-1, 1, -1), (-1, 1, 1),
            (1, -1, -1), (1, -1, 1), (1, 1, -1), (1, 1, 1)
        };

        var useCount = nbh == Neighborhood3D.N6 ? 6 : nbh == Neighborhood3D.N18 ? 18 : 26;

        Parallel.For(0, D, z =>
        {
            for (var y = 0; y < H; y++)
            {
                var row = (z * H + y) * W;
                for (var x = 0; x < W; x++)
                {
                    var idx = row + x;

                    if (src[idx] == 0)
                    {
                        dst[idx] = 0;
                        continue;
                    }

                    var keep = true;

                    if (useCount == 6)
                    {
                        if (Get(src, x - 1, y, z, W, H, D) == 0) keep = false;
                        else if (Get(src, x + 1, y, z, W, H, D) == 0) keep = false;
                        else if (Get(src, x, y - 1, z, W, H, D) == 0) keep = false;
                        else if (Get(src, x, y + 1, z, W, H, D) == 0) keep = false;
                        else if (Get(src, x, y, z - 1, W, H, D) == 0) keep = false;
                        else if (Get(src, x, y, z + 1, W, H, D) == 0) keep = false;
                    }
                    else
                    {
                        for (var i = 0; i < useCount; i++)
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
        public int ComponentCount;
        public int[] Labels;
    }

    private static LabelResult ConnectedComponents3D(byte[] mask, int W, int H, int D, Neighborhood3D nbh)
    {
        var labels = new int[mask.Length];
        var parent = new List<int> { 0 };
        parent.Add(1);
        var next = 1;

        // 1st pass
        for (var z = 0; z < D; z++)
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var idx = (z * H + y) * W + x;
            if (mask[idx] == 0) continue;

            var minLabel = int.MaxValue;
            foreach (var offset in NeighborBackOffsets(x, y, z, W, H, D, nbh))
            {
                var nlab = labels[idx + offset];
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
                var nlab = labels[idx + offset];
                if (nlab > 0 && nlab != minLabel) Union(parent, minLabel, nlab);
            }
        }

        // 2nd pass: flatten
        var map = new Dictionary<int, int>();
        var comp = 0;
        for (var i = 0; i < labels.Length; i++)
        {
            var lab = labels[i];
            if (lab == 0) continue;
            var root = Find(parent, lab);
            if (!map.TryGetValue(root, out var newLab))
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
        if (ra < rb) parent[rb] = ra;
        else parent[ra] = rb;
    }

    #endregion

    #region Pore stats + EDT

    private static void ComputePoreStats(int[] lbl, byte[] mask, int W, int H, int D, double vx, double vy, double vz,
        Pore[] pores)
    {
        var sums = new ConcurrentDictionary<int, (double cx, double cy, double cz, long vox, long area)>();

        Parallel.For(0, D, z =>
        {
            var plane = W * H;
            for (var y = 0; y < H; y++)
            {
                var row = (z * H + y) * W;
                for (var x = 0; x < W; x++)
                {
                    var idx = row + x;
                    var id = lbl[idx];
                    if (id <= 0) continue;

                    var openFaces = 0;
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
            var id = kv.Key;
            var (cx, cy, cz, vox, area) = kv.Value;
            if (vox <= 0) continue;
            var p = new Pore
            {
                ID = id,
                Position = new Vector3((float)(cx / vox), (float)(cy / vox), (float)(cz / vox)),
                Area = area,
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
        for (var i = 0; i < dist.Length; i++) dist[i] = mask[i] == 0 ? 0f : 1e6f;

        // forward pass
        for (var z = 0; z < D; z++)
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var idx = (z * H + y) * W + x;
            if (mask[idx] == 0)
            {
                dist[idx] = 0;
                continue;
            }

            var best = dist[idx];
            for (var dz = -1; dz <= 0; dz++)
            for (var dy = -1; dy <= 0; dy++)
            for (var dx = -1; dx <= 0; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if ((uint)nx >= W || (uint)ny >= H || (uint)nz >= D) continue;
                var nidx = (nz * H + ny) * W + nx;
                var w = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                var nd = dist[nidx] + w;
                if (nd < best) best = nd;
            }

            dist[idx] = best;
        }

        // backward pass
        for (var z = D - 1; z >= 0; z--)
        for (var y = H - 1; y >= 0; y--)
        for (var x = W - 1; x >= 0; x--)
        {
            var idx = (z * H + y) * W + x;
            if (mask[idx] == 0)
            {
                dist[idx] = 0;
                continue;
            }

            var best = dist[idx];
            for (var dz = 0; dz <= 1; dz++)
            for (var dy = 0; dy <= 1; dy++)
            for (var dx = 0; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if ((uint)nx >= W || (uint)ny >= H || (uint)nz >= D) continue;
                var nidx = (nz * H + ny) * W + nx;
                var w = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                var nd = dist[nidx] + w;
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
        for (var i = 0; i < ccl.Length; i++)
        {
            var id = ccl[i];
            if (id <= 0) continue;
            var d = edt[i];
            if (d > maxByComp[id]) maxByComp[id] = d;
        }

        for (var id = 1; id < pores.Length; id++)
            if (pores[id] != null)
                pores[id].Radius = maxByComp[id] * vEdge;
    }

    #endregion

    #region Throats & adjacency

    private static List<Throat> BuildThroats(int[] lbl, int W, int H, int D, float[] edt, float vEdge,
    out Dictionary<int, List<(int nb, float w)>> adjacency, DetailedProgressReporter progress,
    float startProgress, float endProgress)
{
    var edges = new ConcurrentDictionary<(int a, int b), (float radius, int count, float minRadius)>();
    var totalSlices = D;
    var processedSlices = 0;

    Parallel.For(0, D, z =>
    {
        var plane = W * H;
        for (var y = 0; y < H; y++)
        {
            var row = (z * H + y) * W;
            for (var x = 0; x < W; x++)
            {
                var idx = row + x;
                var a = lbl[idx];
                if (a == 0) continue;

                void CheckNeighbor(int nx, int ny, int nz)
                {
                    if ((uint)nx >= W || (uint)ny >= H || (uint)nz >= D) return;
                    var nidx = (nz * H + ny) * W + nx;
                    var b = lbl[nidx];
                    if (b == 0 || b == a) return;

                    var key = a < b ? (a, b) : (b, a);
                    
                    // Calculate throat radius at this interface point
                    // Use MINIMUM of the two EDT values (constriction point)
                    var r = MathF.Min(edt[idx], edt[nidx]) * vEdge;

                    edges.AddOrUpdate(key,
                        k => (r, 1, r),
                        (k, old) => (
                            Math.Max(old.radius, r),  // Track maximum interface radius
                            old.count + 1,
                            Math.Min(old.minRadius, r) // Track minimum as well
                        ));
                }

                CheckNeighbor(x + 1, y, z);
                CheckNeighbor(x, y + 1, z);
                CheckNeighbor(x, y, z + 1);
            }
        }

        var current = Interlocked.Increment(ref processedSlices);
        if (current % 10 == 0)
        {
            var prog = startProgress + (endProgress - startProgress) * current / totalSlices;
            progress?.Report(prog, $"Finding throats: slice {current}/{totalSlices}...");
        }
    });

    var list = new List<Throat>(edges.Count);
    adjacency = new Dictionary<int, List<(int nb, float w)>>();
    var tid = 1;

    foreach (var kv in edges)
    {
        var (a, b) = kv.Key;
        var (maxRadius, count, minRadius) = kv.Value;

        // These radii are already in micrometers from EDT * vEdge
        var finalRadius = minRadius * 0.7f + maxRadius * 0.3f;
        
        // Apply reduction factor for realism
        finalRadius *= 0.35f;
        
        // Ensure minimum radius (in micrometers)
        finalRadius = Math.Max(0.01f * vEdge, finalRadius);  // At least 0.01 voxels

        // Store radius IN MICROMETERS
        list.Add(new Throat { ID = tid++, Pore1ID = a, Pore2ID = b, Radius = finalRadius });

        if (!adjacency.TryGetValue(a, out var la)) adjacency[a] = la = new List<(int nb, float w)>();
        if (!adjacency.TryGetValue(b, out var lb)) adjacency[b] = lb = new List<(int nb, float w)>();
        la.Add((b, 1));
        lb.Add((a, 1));
    }

    Logger.Log($"[BuildThroats] Found {list.Count} unique throats with radius range " +
               $"{list.Min(t => t.Radius):F3} to {list.Max(t => t.Radius):F3} µm");

    return list;
}

 // Enforce a single through-going inlet→outlet path using minimal, axis-monotone bridges.
// Name/signature preserved to be drop-in compatible with your call-site. :contentReference[oaicite:1]{index=1}
static void EnforceInOutConnectivity(
    Pore[] pores,
    Dictionary<int, List<(int nb, float w)>> adj,
    FlowAxis axis, bool inletMin, bool outletMax,
    int W, int H, int D, float vx, float vy, float vz,
    List<Throat> throats,
    int[] expandedLabels, int maxLabel)
{
    if (pores == null || pores.Length <= 1) return;

    // Build poreId<->index maps & physical coords
    var poreIdToIndex = new Dictionary<int, int>();
    for (int i = 1; i < pores.Length; i++)
        if (pores[i] != null) poreIdToIndex[pores[i].ID] = i;

    var phys = new Vector3[pores.Length];
    for (int i = 1; i < pores.Length; i++)
        if (pores[i] != null)
            phys[i] = new Vector3(pores[i].Position.X * vx,
                                  pores[i].Position.Y * vy,
                                  pores[i].Position.Z * vz);

    // --- 1) Enhanced inlet/outlet detection for sparse boundaries ---
    var inletIdx = new HashSet<int>();
    var outletIdx = new HashSet<int>();
    
    // Find material AABB
    int xMin = W, xMax = -1, yMin = H, yMax = -1, zMin = D, zMax = -1;
    for (int idx = 0, z = 0; z < D; z++)
    for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++, idx++)
    {
        int lab = expandedLabels[idx];
        if (lab <= 0) continue;
        if (x < xMin) xMin = x; if (x > xMax) xMax = x;
        if (y < yMin) yMin = y; if (y > yMax) yMax = y;
        if (z < zMin) zMin = z; if (z > zMax) zMax = z;
    }
    if (xMax < 0) return;

    // Use adaptive tolerance based on pore sparsity
    float GetAxisLength(FlowAxis ax) => ax switch
    {
        FlowAxis.X => xMax - xMin,
        FlowAxis.Y => yMax - yMin,
        _ => zMax - zMin
    };

    // Start with a reasonable tolerance
    float axisLength = GetAxisLength(axis);
    float initialTolRatio = 0.05f; // 5% of axis length
    int baseTol = Math.Max(3, (int)(axisLength * initialTolRatio));
    
    // Progressively increase tolerance until we find enough boundary pores
    int minBoundaryPores = Math.Max(3, pores.Length / 100); // At least 3 or 1% of total
    
    for (int tolIncrease = 0; tolIncrease <= 10; tolIncrease++)
    {
        int tol = baseTol + tolIncrease * 2; // Increase tolerance progressively
        inletIdx.Clear();
        outletIdx.Clear();
        
        for (int idx = 0, z = 0; z < D; z++)
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++, idx++)
        {
            int poreId = expandedLabels[idx];
            if (poreId <= 0 || !poreIdToIndex.ContainsKey(poreId)) continue;
            int pi = poreIdToIndex[poreId];

            switch (axis)
            {
                case FlowAxis.X:
                    if (inletMin && x <= xMin + tol) inletIdx.Add(pi);
                    if (outletMax && x >= xMax - tol) outletIdx.Add(pi);
                    break;
                case FlowAxis.Y:
                    if (inletMin && y <= yMin + tol) inletIdx.Add(pi);
                    if (outletMax && y >= yMax - tol) outletIdx.Add(pi);
                    break;
                default: // Z
                    if (inletMin && z <= zMin + tol) inletIdx.Add(pi);
                    if (outletMax && z >= zMax - tol) outletIdx.Add(pi);
                    break;
            }
        }
        
        // Check if we have enough boundary pores
        if (inletIdx.Count >= minBoundaryPores && outletIdx.Count >= minBoundaryPores)
        {
            Logger.Log($"[Connectivity] Found {inletIdx.Count} inlet and {outletIdx.Count} outlet pores with tolerance {tol}");
            break;
        }
    }

    // Fallback: use percentile-based selection if still not enough pores
    if (inletIdx.Count < minBoundaryPores || outletIdx.Count < minBoundaryPores)
    {
        var ordered = new List<(int idx, float pos)>();
        for (int i = 1; i < pores.Length; i++)
        {
            if (pores[i] == null) continue;
            float pos = axis switch {
                FlowAxis.X => pores[i].Position.X,
                FlowAxis.Y => pores[i].Position.Y,
                _ => pores[i].Position.Z
            };
            ordered.Add((i, pos));
        }
        ordered.Sort((a, b) => a.pos.CompareTo(b.pos));
        
        int take = Math.Max(minBoundaryPores, ordered.Count / 10);
        if (inletIdx.Count < minBoundaryPores)
        {
            inletIdx.Clear();
            for (int i = 0; i < take && i < ordered.Count; i++) 
                inletIdx.Add(ordered[i].idx);
        }
        if (outletIdx.Count < minBoundaryPores)
        {
            outletIdx.Clear();
            for (int i = Math.Max(0, ordered.Count - take); i < ordered.Count; i++) 
                outletIdx.Add(ordered[i].idx);
        }
        Logger.Log($"[Connectivity] Used fallback selection: {inletIdx.Count} inlet, {outletIdx.Count} outlet pores");
    }

    if (inletIdx.Count == 0 || outletIdx.Count == 0) return;

    // --- 2) Build adjacency on INDEX space ---
    var adjIdx = new Dictionary<int, List<(int nb, float w)>>();
    foreach (var t in throats)
    {
        if (!poreIdToIndex.TryGetValue(t.Pore1ID, out var a)) continue;
        if (!poreIdToIndex.TryGetValue(t.Pore2ID, out var b)) continue;

        float wdist = Vector3.Distance(phys[a], phys[b]);
        if (!adjIdx.TryGetValue(a, out var la)) adjIdx[a] = la = new List<(int, float)>();
        if (!adjIdx.TryGetValue(b, out var lb)) adjIdx[b] = lb = new List<(int, float)>();
        la.Add((b, wdist)); 
        lb.Add((a, wdist));
    }

    // --- 3) Enhanced: Create inlet/outlet super-nodes for sparse boundaries ---
    // Instead of trying to connect individual sparse pores, we'll ensure the boundary
    // regions are well-connected internally first
    
    void ConnectBoundaryRegion(HashSet<int> boundaryPores, string regionName)
    {
        if (boundaryPores.Count <= 1) return;
        
        var boundaryList = boundaryPores.ToList();
        var connected = new HashSet<int>();
        connected.Add(boundaryList[0]);
        
        // Connect all boundary pores into a mesh using nearest-neighbor approach
        while (connected.Count < boundaryList.Count)
        {
            float minDist = float.MaxValue;
            int bestFrom = -1, bestTo = -1;
            
            foreach (var from in connected)
            {
                foreach (var to in boundaryList)
                {
                    if (connected.Contains(to)) continue;
                    
                    float dist = Vector3.Distance(phys[from], phys[to]);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestFrom = from;
                        bestTo = to;
                    }
                }
            }
            
            if (bestFrom >= 0 && bestTo >= 0 && minDist < float.MaxValue)
            {
                // Add connection
                connected.Add(bestTo);
                
                // Check if already connected
                bool alreadyConnected = false;
                if (adjIdx.TryGetValue(bestFrom, out var lst))
                {
                    foreach (var (nb, _) in lst)
                    {
                        if (nb == bestTo) 
                        {
                            alreadyConnected = true;
                            break;
                        }
                    }
                }
                
                if (!alreadyConnected)
                {
                    // Create bridging throat
                    float radius = Math.Max(0.01f, 
                        Math.Min(pores[bestFrom].Radius, pores[bestTo].Radius) * 0.25f);
                    throats.Add(new Throat { 
                        Pore1ID = pores[bestFrom].ID, 
                        Pore2ID = pores[bestTo].ID, 
                        Radius = radius 
                    });
                    
                    if (!adjIdx.TryGetValue(bestFrom, out var la)) adjIdx[bestFrom] = la = new List<(int, float)>();
                    if (!adjIdx.TryGetValue(bestTo, out var lb)) adjIdx[bestTo] = lb = new List<(int, float)>();
                    la.Add((bestTo, minDist));
                    lb.Add((bestFrom, minDist));
                    
                    Logger.Log($"[Connectivity] Connected {regionName} pores {bestFrom} to {bestTo} (dist={minDist:F3})");
                }
            }
            else
            {
                break; // No more connections possible
            }
        }
    }
    
    // Connect inlet and outlet regions internally first
    ConnectBoundaryRegion(inletIdx, "inlet");
    ConnectBoundaryRegion(outletIdx, "outlet");

    // --- 4) Find connected components ---
    var compOf = new Dictionary<int, int>();
    int compId = 0;
    foreach (var start in adjIdx.Keys)
    {
        if (compOf.ContainsKey(start)) continue;
        compId++;
        var q = new Queue<int>();
        q.Enqueue(start);
        compOf[start] = compId;
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            if (!adjIdx.TryGetValue(u, out var ns)) continue;
            foreach (var (v, _) in ns)
                if (!compOf.ContainsKey(v)) 
                { 
                    compOf[v] = compId; 
                    q.Enqueue(v); 
                }
        }
    }

    // Handle isolated pores
    for (int i = 1; i < pores.Length; i++)
        if (pores[i] != null && !compOf.ContainsKey(i))
            compOf[i] = ++compId;

    // Map components
    var compPores = new Dictionary<int, List<int>>();
    var compCentroid = new Dictionary<int, Vector3>();
    var compAxisPos = new Dictionary<int, float>();
    
    for (int i = 1; i < pores.Length; i++)
    {
        if (pores[i] == null) continue;
        int c = compOf[i];
        if (!compPores.TryGetValue(c, out var lp)) compPores[c] = lp = new List<int>();
        lp.Add(i);
    }
    
    foreach (var kv in compPores)
    {
        var sum = Vector3.Zero;
        foreach (var i in kv.Value) sum += phys[i];
        var ctr = sum / kv.Value.Count;
        compCentroid[kv.Key] = ctr;
        compAxisPos[kv.Key] = axis switch {
            FlowAxis.X => ctr.X,
            FlowAxis.Y => ctr.Y,
            _ => ctr.Z
        };
    }

    // Get inlet/outlet components
    var inletComps = new HashSet<int>(inletIdx.Select(i => compOf[i]));
    var outletComps = new HashSet<int>(outletIdx.Select(i => compOf[i]));
    
    if (inletComps.Overlaps(outletComps))
    {
        Logger.Log("[Connectivity] Inlet and outlet already connected!");
        return;
    }

    // --- 5) Enhanced pathfinding with relaxed monotonicity for sparse regions ---
    // Build component graph with adaptive connections
    var compGraph = new Dictionary<int, List<(int nb, float cost, bool isBridge)>>();
    
    void AddCompEdge(int a, int b, float cost, bool isBridge)
    {
        if (!compGraph.TryGetValue(a, out var la)) compGraph[a] = la = new List<(int, float, bool)>();
        la.Add((b, cost, isBridge));
    }
    
    // Existing connections (virtually free)
    foreach (var e in adjIdx)
        foreach (var (v, wdist) in e.Value)
        {
            int ca = compOf[e.Key], cb = compOf[v];
            if (ca == cb) continue;
            AddCompEdge(ca, cb, 1e-6f, false);
        }
    
    // Enhanced: Build candidate bridges with relaxed constraints for boundary components
    const float ALPHA = 1.0f, BETA = 0.10f, GAMMA = 0.02f;
    int K_NORMAL = 8;  // More connections than before
    int K_BOUNDARY = 15; // Even more for boundary components
    
    foreach (var c in compPores.Keys)
    {
        bool isNearBoundary = compPores[c].Any(p => inletIdx.Contains(p) || outletIdx.Contains(p));
        int k = isNearBoundary ? K_BOUNDARY : K_NORMAL;
        
        // Find k nearest components (with relaxed monotonicity for boundaries)
        var candidates = new List<(int comp, float dist, bool isForward)>();
        
        foreach (var other in compPores.Keys)
        {
            if (other == c) continue;
            
            var d = compCentroid[other] - compCentroid[c];
            float axisGap = axis switch { 
                FlowAxis.X => d.X,
                FlowAxis.Y => d.Y,
                _ => d.Z 
            };
            
            bool isForward = axisGap > 0;
            
            // For boundary components, allow some backward connections too
            if (!isForward && !isNearBoundary) continue;
            if (!isForward && Math.Abs(axisGap) > axisLength * 0.1f) continue; // Limited backward
            
            float dist = d.Length();
            candidates.Add((other, dist, isForward));
        }
        
        // Sort by distance and take k nearest
        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
        
        foreach (var (nb, dist, isForward) in candidates.Take(k))
        {
            var d = compCentroid[nb] - compCentroid[c];
            float axisGap = Math.Abs(axis switch { 
                FlowAxis.X => d.X,
                FlowAxis.Y => d.Y,
                _ => d.Z 
            });
            float lateral = axis switch
            {
                FlowAxis.X => new Vector2(d.Y, d.Z).Length(),
                FlowAxis.Y => new Vector2(d.X, d.Z).Length(),
                _ => new Vector2(d.X, d.Y).Length()
            };
            
            // Reduce cost penalty for boundary connections
            float alpha = isNearBoundary ? ALPHA * 0.5f : ALPHA;
            float beta = isNearBoundary ? BETA * 0.5f : BETA;
            
            float cost = alpha * axisGap + beta * lateral + GAMMA * dist;
            AddCompEdge(c, nb, cost, true);
        }
    }

    // --- 6) A* search with multiple attempts ---
    float HeuristicToOutlet(int c)
    {
        float best = float.MaxValue;
        foreach (var o in outletComps)
        {
            var d = compCentroid[o] - compCentroid[c];
            float dist = d.Length();
            if (dist < best) best = dist;
        }
        return best * GAMMA; // Use full 3D distance as heuristic
    }

    var gScore = new Dictionary<int, float>();
    var fScore = new Dictionary<int, float>();
    var cameFrom = new Dictionary<int, int>();
    var open = new PriorityQueue<int, float>();
    
    foreach (var s in inletComps)
    {
        gScore[s] = 0;
        float f = HeuristicToOutlet(s);
        fScore[s] = f;
        open.Enqueue(s, f);
    }

    int goal = -1;
    int iterations = 0;
    int maxIterations = compPores.Count * 100; // Prevent infinite loops
    
    while (open.Count > 0 && iterations++ < maxIterations)
    {
        open.TryDequeue(out var cur, out _);
        
        if (outletComps.Contains(cur)) 
        { 
            goal = cur; 
            break; 
        }

        if (!compGraph.TryGetValue(cur, out var nbrs)) continue;
        
        foreach (var (nb, cost, _) in nbrs)
        {
            float tentative = gScore[cur] + cost;
            if (!gScore.TryGetValue(nb, out var best) || tentative < best)
            {
                gScore[nb] = tentative;
                float f = tentative + HeuristicToOutlet(nb);
                fScore[nb] = f;
                cameFrom[nb] = cur;
                open.Enqueue(nb, f);
            }
        }
    }
    
    if (goal < 0)
    {
        Logger.LogWarning("[Connectivity] Could not find path with A*. Attempting direct connection...");
        
        // Fallback: Connect nearest inlet-outlet pair directly
        float minDist = float.MaxValue;
        int bestInlet = -1, bestOutlet = -1;
        
        foreach (var inlet in inletIdx)
        foreach (var outlet in outletIdx)
        {
            float dist = Vector3.Distance(phys[inlet], phys[outlet]);
            if (dist < minDist)
            {
                minDist = dist;
                bestInlet = inlet;
                bestOutlet = outlet;
            }
        }
        
        if (bestInlet >= 0 && bestOutlet >= 0)
        {
            Logger.Log($"[Connectivity] Creating direct bridge from inlet {bestInlet} to outlet {bestOutlet}");
            float radius = Math.Max(0.01f, 
                Math.Min(pores[bestInlet].Radius, pores[bestOutlet].Radius) * 0.15f);
            throats.Add(new Throat { 
                Pore1ID = pores[bestInlet].ID,
                Pore2ID = pores[bestOutlet].ID,
                Radius = radius 
            });
            return;
        }
        
        Logger.LogError("[Connectivity] Failed to establish inlet-outlet connectivity!");
        return;
    }

    // --- 7) Recover component path and create bridges ---
    var compPath = new List<int>();
    for (int c = goal; ; )
    {
        compPath.Add(c);
        if (!cameFrom.TryGetValue(c, out var prev)) break;
        c = prev;
    }
    compPath.Reverse();
    
    Logger.Log($"[Connectivity] Found path through {compPath.Count} components");

    // Create bridges where needed
    int bridgesAdded = 0;
    for (int i = 0; i + 1 < compPath.Count; i++)
    {
        int aComp = compPath[i], bComp = compPath[i + 1];
        bool alreadyLinked = false;

        // Check if already connected
        foreach (var aIdx in compPores[aComp])
        {
            if (!adjIdx.TryGetValue(aIdx, out var lst)) continue;
            foreach (var (bIdx, _) in lst)
                if (compOf[bIdx] == bComp) 
                { 
                    alreadyLinked = true; 
                    break; 
                }
            if (alreadyLinked) break;
        }
        
        if (alreadyLinked) continue;

        // Build bridge: nearest pore pair
        float bestDist = float.MaxValue;
        int bestA = -1, bestB = -1;
        
        foreach (var aIdx in compPores[aComp])
        foreach (var bIdx in compPores[bComp])
        {
            float d = Vector3.Distance(phys[aIdx], phys[bIdx]);
            if (d < bestDist) 
            { 
                bestDist = d; 
                bestA = aIdx; 
                bestB = bIdx; 
            }
        }
        
        if (bestA < 0) continue;

        float radius = Math.Max(0.01f, 
            Math.Min(pores[bestA].Radius, pores[bestB].Radius) * 0.2f);
        throats.Add(new Throat { 
            Pore1ID = pores[bestA].ID,
            Pore2ID = pores[bestB].ID,
            Radius = radius 
        });

        if (!adjIdx.TryGetValue(bestA, out var la2)) adjIdx[bestA] = la2 = new List<(int, float)>();
        if (!adjIdx.TryGetValue(bestB, out var lb2)) adjIdx[bestB] = lb2 = new List<(int, float)>();
        la2.Add((bestB, bestDist));
        lb2.Add((bestA, bestDist));
        bridgesAdded++;
    }

    Logger.Log($"[Connectivity] Added {bridgesAdded} bridging throats to establish inlet→outlet connectivity");
}

// Compute AABB of segmented material (labels>0)
static (int x0,int x1,int y0,int y1,int z0,int z1) MaterialAABB(int[] labels, int W, int H, int D)
{
    int x0=W, y0=H, z0=D, x1=-1, y1=-1, z1=-1;
    int idx=0;
    for (int z=0; z<D; z++)
    for (int y=0; y<H; y++)
    for (int x=0; x<W; x++, idx++)
    {
        if (labels[idx] > 0)
        {
            if (x < x0) x0 = x; if (x > x1) x1 = x;
            if (y < y0) y0 = y; if (y > y1) y1 = y;
            if (z < z0) z0 = z; if (z > z1) z1 = z;
        }
    }
    if (x1 < x0) return (0, -1, 0, -1, 0, -1); // empty
    return (x0, x1, y0, y1, z0, z1);
}

    private static Dictionary<int, int> ConnectedSets(Dictionary<int, List<(int nb, float w)>> adj, int maxId)
    {
        var comp = new Dictionary<int, int>();
        var cid = 0;
        var visited = new HashSet<int>();
        for (var i = 1; i <= maxId; i++)
        {
            if (visited.Contains(i) || !adj.ContainsKey(i)) continue;
            cid++;
            var q = new Queue<int>();
            q.Enqueue(i);
            visited.Add(i);
            comp[i] = cid;
            while (q.Count > 0)
            {
                var u = q.Dequeue();
                if (!adj.TryGetValue(u, out var list)) continue;
                foreach (var (v, _) in list)
                    if (visited.Add(v))
                    {
                        comp[v] = cid;
                        q.Enqueue(v);
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
    if (pores == null || pores.Length <= 1)
        return 1.0f;

    // Build physical position array
    var pos = new Vector3[pores.Length];
    for (var i = 1; i < pores.Length; i++)
        if (pores[i] != null)
            pos[i] = new Vector3(pores[i].Position.X * vx,
                                 pores[i].Position.Y * vy,
                                 pores[i].Position.Z * vz);

    // Update adjacency weights to physical distances
    var physicalAdjacency = new Dictionary<int, List<(int nb, float w)>>();
    foreach (var kv in adjacency)
    {
        var u = kv.Key;
        var list = new List<(int, float)>();
        foreach (var (v, _) in kv.Value)
        {
            var dist = Vector3.Distance(pos[u], pos[v]);
            list.Add((v, dist));
        }
        physicalAdjacency[u] = list;
    }

    // CRITICAL: Find MATERIAL bounds, not image bounds!
    float materialMin = float.MaxValue, materialMax = float.MinValue;
    for (int i = 1; i < pores.Length; i++)
    {
        if (pores[i] == null) continue;
        float axisPos = axis switch
        {
            FlowAxis.X => pos[i].X,
            FlowAxis.Y => pos[i].Y,
            _ => pos[i].Z
        };
        if (axisPos < materialMin) materialMin = axisPos;
        if (axisPos > materialMax) materialMax = axisPos;
    }

    // Identify inlet/outlet pores based on position along axis
    var inlet = new HashSet<int>();
    var outlet = new HashSet<int>();
    float tolerance = (materialMax - materialMin) * 0.15f; // 15% tolerance band

    for (int i = 1; i < pores.Length; i++)
    {
        if (pores[i] == null) continue;
        float axisPos = axis switch
        {
            FlowAxis.X => pos[i].X,
            FlowAxis.Y => pos[i].Y,
            _ => pos[i].Z
        };

        if (inletMin && axisPos <= materialMin + tolerance) inlet.Add(i);
        if (outletMax && axisPos >= materialMax - tolerance) outlet.Add(i);
    }

    Logger.Log($"[Tortuosity] Material extent along {axis}: {materialMin:F3} to {materialMax:F3} µm");
    Logger.Log($"[Tortuosity] Found {inlet.Count} inlet and {outlet.Count} outlet pores");

    if (inlet.Count == 0 || outlet.Count == 0)
    {
        Logger.LogWarning("[Tortuosity] No boundary pores found. Using default tortuosity of 1.0");
        return 1.0f;
    }

    // Find shortest paths from all inlets to all outlets
    var allPaths = new List<(float length, int inlet, int outlet)>();
    
    foreach (var s in inlet)
    {
        var dist = new Dictionary<int, float>();
        var parent = new Dictionary<int, int>();
        var visited = new HashSet<int>();
        var pq = new PriorityQueue<int, float>();

        dist[s] = 0f;
        pq.Enqueue(s, 0f);

        while (pq.Count > 0)
        {
            pq.TryDequeue(out var u, out var du);
            if (!visited.Add(u)) continue;

            // Check all outlets from this inlet
            if (outlet.Contains(u))
            {
                allPaths.Add((du, s, u));
            }

            if (!physicalAdjacency.TryGetValue(u, out var neighbors)) continue;

            foreach (var (v, weight) in neighbors)
            {
                if (visited.Contains(v)) continue;

                var newDist = du + weight;
                if (!dist.ContainsKey(v) || newDist < dist[v])
                {
                    dist[v] = newDist;
                    parent[v] = u;
                    pq.Enqueue(v, newDist);
                }
            }
        }
    }

    if (allPaths.Count == 0)
    {
        Logger.LogError("[Tortuosity] No path found between inlet and outlet!");
        return 1.0f;
    }

    // Use median path length (more robust than minimum)
    allPaths.Sort((a, b) => a.length.CompareTo(b.length));
    var medianIdx = allPaths.Count / 2;
    var chosenPath = allPaths[medianIdx];
    
    Logger.Log($"[Tortuosity] Found {allPaths.Count} paths. Shortest: {allPaths[0].length:F3} µm, " +
               $"Median: {chosenPath.length:F3} µm, Longest: {allPaths[^1].length:F3} µm");

    // CRITICAL: Use MATERIAL extent for straight-line distance, not image dimensions!
    var straightLineDistance = materialMax - materialMin;

    // Calculate tortuosity
    var tortuosity = straightLineDistance > 0f ? chosenPath.length / straightLineDistance : 1.0f;

    // Sanity check - if tortuosity is still 1.0, something is wrong
    if (Math.Abs(tortuosity - 1.0f) < 0.01f)
    {
        Logger.LogWarning($"[Tortuosity] Suspiciously low tortuosity ({tortuosity:F3}). " +
                          $"Path length={chosenPath.length:F3}, Straight={straightLineDistance:F3}");
        
        // Use a more conservative estimate based on path statistics
        var avgPath = allPaths.Average(p => p.length);
        tortuosity = avgPath / straightLineDistance;
    }

    // Clamp to physically reasonable range
    tortuosity = Math.Max(1.01f, Math.Min(5.0f, tortuosity)); // At least 1.01, max 5.0

    Logger.Log($"[Tortuosity] Final: Path={chosenPath.length:F3} µm, " +
               $"Straight={straightLineDistance:F3} µm, Tortuosity={tortuosity:F3}");

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

            for (var i = 0; i < nPlatforms; i++)
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

            var sources = new[] { OpenCLKernels };
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
                var count = (nuint)((long)W * H * D);

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
                _cl.SetKernelArg(kernel, 2, sizeof(int), in W);
                _cl.SetKernelArg(kernel, 3, sizeof(int), in H);
                _cl.SetKernelArg(kernel, 4, sizeof(int), in D);
                _cl.SetKernelArg(kernel, 5, sizeof(int), in neigh);

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
                var n = (long)W * H * D;
                var bytesMask = (nuint)n;
                var bytesDist = (nuint)(n * sizeof(float));

                var distHost = new float[n];
                for (long i = 0; i < n; i++) distHost[i] = mask[i] == 0 ? 0f : 1e6f;

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
                _cl.SetKernelArg(kernel, 2, sizeof(int), in W);
                _cl.SetKernelArg(kernel, 3, sizeof(int), in H);
                _cl.SetKernelArg(kernel, 4, sizeof(int), in D);

                nuint[] gws = { (nuint)W, (nuint)H, (nuint)D };
                fixed (nuint* pg = gws)
                {
                    for (var it = 0; it < 4; it++)
                    {
                        var e1 = _cl.EnqueueNdrangeKernel(_queue, kernel, 3, null, pg, null, 0, null, null);
                        if (e1 != 0) throw new Exception("EnqueueNdrangeKernel edt failed: " + e1);
                        _cl.Finish(_queue);
                    }
                }

                _lastDist = new float[n];
                fixed (float* pr = _lastDist)
                {
                    var e2 = _cl.EnqueueReadBuffer(_queue, distBuf, true, 0, bytesDist, pr, 0, null, null);
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

        if (!string.IsNullOrEmpty(message)) Logger.Log($"[PNMGenerator] {message}");
    }
}

#endregion

#region UI Tool with Progress Bar

public sealed class PNMGenerationTool : IDatasetTools
{
    // Progress dialog
    private readonly ProgressBarDialog _progressDialog;
    private int _axisIndex = 2; // Z
    private CancellationTokenSource _cancellationTokenSource;

    private bool _enforce;
    private bool _inMin = true;
    private int _materialIndex;
    private int _modeIndex; // Conservative
    private int _neighIndex = 2; // default 26
    private bool _outMax = true;
    private bool _useOpenCL;

    public PNMGenerationTool()
    {
        _progressDialog = new ProgressBarDialog("PNM Generation");
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
            ImGui.Button("Generating...", new Vector2(-1, 0));
            ImGui.EndDisabled();
        }
        else
        {
            if (ImGui.Button("Generate PNM", new Vector2(-1, 0)))
            {
                var opt = new PNMGeneratorOptions
                {
                    MaterialId = materials[Math.Clamp(_materialIndex, 0, materials.Count - 1)].ID,
                    Neighborhood = _neighIndex == 0 ? Neighborhood3D.N6 :
                        _neighIndex == 1 ? Neighborhood3D.N18 : Neighborhood3D.N26,
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
            var status = value < 0.1f ? "Extracting material mask..." :
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
                Logger.Log(
                    $"[PNMGenerationTool] Successfully generated PNM with {pnm.Pores.Count} pores and {pnm.Throats.Count} throats");
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