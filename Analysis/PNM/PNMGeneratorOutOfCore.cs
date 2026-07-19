// GAIA/Analysis/Pnm/PNMGeneratorOutOfCore.cs
// Out-of-core PNM generation: streams the segmented stack through RAM in overlapping blocks and
// stitches the per-block networks exactly on the shared faces, so stacks far larger than the
// working-set budget generate at full resolution on low-memory machines.
//
// ALGORITHM: Block-decomposed watershed network extraction with halo overlap and face stitching.
//
// The volume is partitioned into a regular grid of core blocks. Each block is read from the
// (memory-mapped) label volume together with a halo of context voxels and run through the exact
// same per-voxel pipeline as the in-RAM generator: material mask, binary erosion, seed labelling,
// watershed expansion, unseeded-region recovery, distance transform and throat scan. Per-pore
// statistics are accumulated over core voxels only, so every voxel of the volume is counted exactly
// once. Pores that span a block cut appear in both blocks under different local labels; the two
// labellings of the shared face plane are reconciled with a union-find:
//
//   - voxels that survive erosion in both windows belong to the same seed component by
//     construction (the erosion is exact because the halo exceeds the erosion count), so their
//     labels are unioned unconditionally — this reconstructs the global seed labelling exactly;
//   - the remaining label pairs are matched by mutual best overlap on the face, which joins the two
//     halves of a pore crossing the cut while refusing to weld two distinct pores whose watershed
//     boundary merely grazes the plane.
//
// The approach follows the parallel/chunked variants of the SNOW watershed extraction family:
//
// - Gostick, J.T. (2017). "Versatile and efficient pore network extraction method using
//   marker-based watershed segmentation." Physical Review E, 96(2), 023307.
//   DOI: 10.1103/PhysRevE.96.023307
//
// - Khan, Z.A., et al. (2019). "Dual network extraction algorithm to investigate multiple
//   transport processes in porous materials: Image-based modeling of pore and grain scale
//   processes." Computers & Chemical Engineering, 123, 64-77.
//   DOI: 10.1016/j.compchemeng.2018.12.025

using System.Numerics;
using System.Runtime.InteropServices;
using GAIA.Data.CtImageStack;
using GAIA.Data.Pnm;
using GAIA.Util;

namespace GAIA.Analysis.Pnm;

public static partial class PNMGenerator
{
    /// <summary>
    ///     Peak bytes per extended-block voxel across mask, eroded mask, scratch, seed labels,
    ///     expanded labels and EDT held at once while a block is processed.
    /// </summary>
    private const long OutOfCoreBytesPerVoxel = 26L;

    private static long MakeGid(int blockId, int label)
    {
        return ((long)blockId << 32) | (uint)label;
    }

    #region Driver

    private static PNMDataset GenerateOutOfCore(CtImageStackDataset ct, PNMGeneratorOptions opt, PnmRegion roi,
        DetailedProgressReporter progress, CancellationToken token)
    {
        var W = (int)roi.Width;
        var H = (int)roi.Height;
        var D = (int)roi.Depth;

        // Streaming never allocates a full-volume array, so the total voxel count is unbounded by
        // array limits (PnmRegion counts in long). The one whole-slice allocation left is the
        // ReadSliceZ buffer the label volume API requires; guard it with long arithmetic so an
        // extreme slice fails with a diagnosis instead of an overflowed negative array length.
        var sliceVoxels = (long)ct.Width * ct.Height;
        if (sliceVoxels > Array.MaxLength)
            throw new InvalidOperationException(
                $"Slices of {ct.Width}×{ct.Height} voxels ({sliceVoxels:N0}) exceed the largest .NET array " +
                $"({Array.MaxLength:N0} elements). Reading a label slice requires one slice-sized buffer, " +
                "so stacks this wide cannot be streamed; split the stack laterally first.");

        var erosions = opt.Mode == GenerationMode.Conservative ? opt.ConservativeErosions : opt.AggressiveErosions;
        var halo = Math.Max(Math.Max(1, opt.OutOfCoreHaloVoxels), erosions + 2);

        var vx = Math.Max(1e-9, ct.PixelSize);
        var vy = Math.Max(1e-9, ct.PixelSize);
        var vz = Math.Max(1e-9, ct.SliceThickness > 0 ? ct.SliceThickness : ct.PixelSize);
        var vEdge = Math.Pow(vx * vy * vz, 1.0 / 3.0);

        var budgetBytes = Math.Max(1L, opt.MaxWorkingSetMB) * 1024L * 1024L;
        var budgetVoxels = Math.Min(budgetBytes / OutOfCoreBytesPerVoxel, int.MaxValue);
        var (cx, cy, cz) = PlanOutOfCoreBlockSize(W, H, D, halo, budgetVoxels);

        var nbx = (W + cx - 1) / cx;
        var nby = (H + cy - 1) / cy;
        var nbz = (D + cz - 1) / cz;
        // Block ids are packed into the upper 32 bits of a gid, so the grid itself must stay
        // int-addressable. Hitting this needs a tera-voxel region with a pathologically small
        // budget; the cure is a bigger budget, so say so.
        var totalBlocks = (long)nbx * nby * nbz;
        if (totalBlocks > int.MaxValue)
            throw new InvalidOperationException(
                $"The region would stream as {totalBlocks:N0} blocks, more than the block grid can address. " +
                "Raise the working-set budget so blocks are cut larger.");

        progress.Report(0.02f,
            $"Out-of-core streaming: {totalBlocks} block(s) of {cx}×{cy}×{cz} core voxels " +
            $"(+{halo} halo) within the {opt.MaxWorkingSetMB} MB budget...");

        var dsu = new GidUnionFind();
        var poreAgg = new Dictionary<long, OocPoreAccumulator>();
        var edgeAgg = new Dictionary<(long a, long b), OocEdgeAccumulator>();
        // Overlap votes of "foreign" labels (basins whose seed lies outside the owning block's
        // core, and recovered regions, which have no seed at all) towards the label of the block
        // that owns their seed. Resolved by majority once every face has voted. Labels that were
        // already tied by exact seed identity never vote: their pore is known.
        var foreignVotes = new Dictionary<long, Dictionary<long, int>>();
        var strongMerged = new HashSet<long>();

        // Face planes waiting for their higher neighbour: one X face within the current row, one Y
        // face per column within the current layer, one Z face per column across layers. Together
        // they never exceed roughly one full slice of the volume.
        var pendingZ = new OocFacePlane[nbx, nby];
        var haloExceeded = false;

        var blockId = 0;
        for (var iz = 0; iz < nbz; iz++)
        {
            var pendingY = new OocFacePlane[nbx];
            for (var iy = 0; iy < nby; iy++)
            {
                OocFacePlane pendingX = null;
                for (var ix = 0; ix < nbx; ix++, blockId++)
                {
                    token.ThrowIfCancellationRequested();
                    var blockProgress = 0.05f + 0.80f * blockId / totalBlocks;
                    progress.Report(blockProgress, $"Streaming block {blockId + 1}/{totalBlocks}...");

                    var blk = ComputeOutOfCoreBlock(ix, iy, iz, cx, cy, cz, W, H, D, halo);
                    var faces = ProcessOutOfCoreBlock(ct, opt, roi, blk, blockId, erosions, halo,
                        ix + 1 < nbx, iy + 1 < nby, iz + 1 < nbz, ix > 0, iy > 0, iz > 0,
                        poreAgg, edgeAgg, ref haloExceeded, token);

                    if (ix > 0) MergeFacePlanes(pendingX, faces.LowX, dsu, foreignVotes, strongMerged, edgeAgg);
                    if (iy > 0) MergeFacePlanes(pendingY[ix], faces.LowY, dsu, foreignVotes, strongMerged, edgeAgg);
                    if (iz > 0)
                        MergeFacePlanes(pendingZ[ix, iy], faces.LowZ, dsu, foreignVotes, strongMerged, edgeAgg);

                    pendingX = faces.HighX;
                    pendingY[ix] = faces.HighY;
                    pendingZ[ix, iy] = faces.HighZ;
                }
            }
        }

        // Resolve foreign labels by majority: each merges with the single partner it shares the
        // most face voxels with, so a foreign view can never weld two distinct native pores.
        foreach (var kv in foreignVotes)
        {
            if (strongMerged.Contains(kv.Key)) continue;

            long best = 0;
            var bestCount = 0;
            foreach (var vote in kv.Value)
                if (vote.Value > bestCount)
                {
                    best = vote.Key;
                    bestCount = vote.Value;
                }

            if (bestCount > 0) dsu.Union(kv.Key, best);
        }

        foreignVotes.Clear();

        if (haloExceeded)
            Logger.LogWarning(
                $"[PNMGenerator] Out-of-core: pore bodies wider than the {halo}-voxel halo touch a block cut. " +
                "Their radii may be slightly overestimated there. Raise the working-set budget or the halo " +
                "for exact radii on such large pores.");

        // --- Stitch: resolve the union-find and fold merged accumulators together ---
        progress.Report(0.86f, "Stitching pore network across blocks...");
        var rootAgg = new Dictionary<long, OocPoreAccumulator>();
        foreach (var kv in poreAgg)
        {
            var root = dsu.Find(kv.Key);
            ref var acc = ref CollectionsMarshal.GetValueRefOrAddDefault(rootAgg, root, out var existed);
            if (!existed) acc = OocPoreAccumulator.Empty;
            acc.Merge(kv.Value);
        }

        poreAgg.Clear();

        if (rootAgg.Count == 0)
        {
            Logger.LogWarning("[PNMGenerator] No pores found: the selected material has no voxels in this volume.");
            var empty = new PNMDataset($"PNM_{ct.Name}_{opt.MaterialId}_Empty", "")
            {
                VoxelSize = (float)vEdge,
                Tortuosity = 0,
                ImageWidth = W,
                ImageHeight = H,
                ImageDepth = D
            };
            empty.InitializeFromCurrentLists();
            PersistAndRegister(ct, empty, progress);
            return empty;
        }

        // Deterministic pore numbering: sort roots so repeated runs produce identical IDs.
        var roots = rootAgg.Keys.ToArray();
        Array.Sort(roots);
        var rootToId = new Dictionary<long, int>(roots.Length);
        var pores = new Pore[roots.Length + 1];
        var extMinX = new int[roots.Length + 1];
        var extMaxX = new int[roots.Length + 1];
        var extMinY = new int[roots.Length + 1];
        var extMaxY = new int[roots.Length + 1];
        var extMinZ = new int[roots.Length + 1];
        var extMaxZ = new int[roots.Length + 1];

        for (var i = 0; i < roots.Length; i++)
        {
            var id = i + 1;
            var acc = rootAgg[roots[i]];
            rootToId[roots[i]] = id;
            pores[id] = new Pore
            {
                ID = id,
                Position = new Vector3((float)(acc.SumX / acc.Voxels), (float)(acc.SumY / acc.Voxels),
                    (float)(acc.SumZ / acc.Voxels)),
                Area = acc.Area,
                VolumeVoxels = acc.Voxels,
                VolumePhysical = (float)(acc.Voxels * vx * vy * vz),
                Connections = 0,
                // Same convention as the in-RAM path: voxel units, physical size applied downstream.
                Radius = acc.MaxEdt
            };
            extMinX[id] = acc.MinX;
            extMaxX[id] = acc.MaxX;
            extMinY[id] = acc.MinY;
            extMaxY[id] = acc.MaxY;
            extMinZ[id] = acc.MinZ;
            extMaxZ[id] = acc.MaxZ;
        }

        Logger.Log($"[PNMGenerator] Out-of-core: {roots.Length} pores after stitching {totalBlocks} block(s)");

        // --- Throats: resolve edge endpoints through the union-find and fold duplicates ---
        progress.Report(0.88f, "Stitching throat network...");
        var resolvedEdges = new Dictionary<(int a, int b), OocEdgeAccumulator>();
        var orphanEdges = 0;
        foreach (var kv in edgeAgg)
        {
            // A halo-only label that never stitched to a core-owned pore has no identity to hang a
            // throat on; its interface is rediscovered by the block that owns the far voxel.
            if (!rootToId.TryGetValue(dsu.Find(kv.Key.a), out var ra) ||
                !rootToId.TryGetValue(dsu.Find(kv.Key.b), out var rb))
            {
                orphanEdges++;
                continue;
            }

            // Both labels stitched into one pore: the interface lies inside it, not a throat.
            if (ra == rb) continue;
            var key = ra < rb ? (ra, rb) : (rb, ra);
            ref var acc = ref CollectionsMarshal.GetValueRefOrAddDefault(resolvedEdges, key, out var existed);
            if (!existed) acc = OocEdgeAccumulator.Empty;
            acc.Merge(kv.Value);
        }

        edgeAgg.Clear();
        if (orphanEdges > 0)
            Logger.Log($"[PNMGenerator] Out-of-core: dropped {orphanEdges} halo-only interface(s) without identity");

        var throats = new List<Throat>(resolvedEdges.Count);
        var adjacency = new Dictionary<int, List<(int nb, float w)>>();
        var tid = 1;
        foreach (var kv in resolvedEdges.OrderBy(e => e.Key))
        {
            var (a, b) = kv.Key;
            // Same radius model as the in-RAM path.
            var finalRadius = kv.Value.MinRadius * 0.7f + kv.Value.MaxRadius * 0.3f;
            finalRadius *= 0.35f;
            finalRadius = Math.Max(0.01f, finalRadius);

            throats.Add(new Throat { ID = tid++, Pore1ID = a, Pore2ID = b, Radius = finalRadius });

            if (!adjacency.TryGetValue(a, out var la)) adjacency[a] = la = new List<(int nb, float w)>();
            if (!adjacency.TryGetValue(b, out var lb)) adjacency[b] = lb = new List<(int nb, float w)>();
            la.Add((b, 1));
            lb.Add((a, 1));
        }

        foreach (var th in throats)
        {
            if (th.Pore1ID > 0 && th.Pore1ID < pores.Length) pores[th.Pore1ID].Connections++;
            if (th.Pore2ID > 0 && th.Pore2ID < pores.Length) pores[th.Pore2ID].Connections++;
        }

        Logger.Log($"[PNMGenerator] Out-of-core: {throats.Count} throats connecting pores");

        token.ThrowIfCancellationRequested();

        if (opt.EnforceInletOutletConnectivity)
        {
            progress.Report(0.90f, "Enforcing inlet-outlet connectivity...");
            EnforceInOutConnectivityFromExtents(pores, throats,
                extMinX, extMaxX, extMinY, extMaxY, extMinZ, extMaxZ,
                opt.Axis, opt.InletIsMinSide, opt.OutletIsMaxSide, (float)vx, (float)vy, (float)vz);

            // Rebuild adjacency after any bridging throats (IDs equal array indices here).
            adjacency.Clear();
            foreach (var t in throats)
            {
                if (t.Pore1ID <= 0 || t.Pore1ID >= pores.Length || pores[t.Pore1ID] == null) continue;
                if (t.Pore2ID <= 0 || t.Pore2ID >= pores.Length || pores[t.Pore2ID] == null) continue;
                if (!adjacency.TryGetValue(t.Pore1ID, out var la)) adjacency[t.Pore1ID] = la = new List<(int, float)>();
                if (!adjacency.TryGetValue(t.Pore2ID, out var lb)) adjacency[t.Pore2ID] = lb = new List<(int, float)>();
                if (la.All(x => x.Item1 != t.Pore2ID)) la.Add((t.Pore2ID, 1));
                if (lb.All(x => x.Item1 != t.Pore1ID)) lb.Add((t.Pore1ID, 1));
            }
        }

        progress.Report(0.93f, "Calculating tortuosity...");
        var tort = ComputeTortuosity(
            pores, adjacency, opt.Axis, opt.InletIsMinSide, opt.OutletIsMaxSide,
            W, H, D, (float)vx, (float)vy, (float)vz,
            null, roots.Length);

        progress.Report(0.95f, "Creating PNM dataset...");
        var pnm = new PNMDataset($"PNM_{ct.Name}_Mat{opt.MaterialId}", "")
        {
            VoxelSize = (float)vEdge,
            Tortuosity = tort,
            ImageWidth = W,
            ImageHeight = H,
            ImageDepth = D
        };

        for (var i = 1; i < pores.Length; i++)
            if (pores[i] != null)
                pnm.Pores.Add(pores[i]);

        var renumber = 1;
        foreach (var t in throats)
        {
            t.ID = renumber++;
            pnm.Throats.Add(t);
        }

        pnm.InitializeFromCurrentLists();
        pnm.CalculateBounds();

        PersistAndRegister(ct, pnm, progress);

        progress.Report(1.0f, "PNM generation complete!");
        return pnm;
    }

    /// <summary>
    ///     Shrinks core block dimensions until block + halo fits the voxel budget, preferring to cut
    ///     along Z first (whole slices stream naturally) and keeping blocks roughly cubic otherwise
    ///     to minimise halo overhead. Best effort: at the minimum core size the block is used even if
    ///     it still exceeds a very small budget.
    /// </summary>
    private static (int cx, int cy, int cz) PlanOutOfCoreBlockSize(int W, int H, int D, int halo, long budgetVoxels)
    {
        int cx = W, cy = H, cz = D;
        var minCore = Math.Max(16, halo);

        long Ext(int core, int full)
        {
            return Math.Min(full, (long)core + 2L * halo);
        }

        while (Ext(cx, W) * Ext(cy, H) * Ext(cz, D) > budgetVoxels)
        {
            if (cz > minCore && cz >= cx && cz >= cy) cz = Math.Max(minCore, cz / 2);
            else if (cy > minCore && cy >= cx) cy = Math.Max(minCore, cy / 2);
            else if (cx > minCore) cx = Math.Max(minCore, cx / 2);
            else if (cz > minCore) cz = Math.Max(minCore, cz / 2);
            else if (cy > minCore) cy = Math.Max(minCore, cy / 2);
            else break;
        }

        return (cx, cy, cz);
    }

    #endregion

    #region Per-block pipeline

    private readonly record struct OocBlockBounds(
        int CoreX0, int CoreX1, int CoreY0, int CoreY1, int CoreZ0, int CoreZ1,
        int ExtX0, int ExtX1, int ExtY0, int ExtY1, int ExtZ0, int ExtZ1)
    {
        public int ExtW => ExtX1 - ExtX0;
        public int ExtH => ExtY1 - ExtY0;
        public int ExtD => ExtZ1 - ExtZ0;
    }

    private static OocBlockBounds ComputeOutOfCoreBlock(int ix, int iy, int iz, int cx, int cy, int cz,
        int W, int H, int D, int halo)
    {
        var coreX0 = ix * cx;
        var coreY0 = iy * cy;
        var coreZ0 = iz * cz;
        var coreX1 = Math.Min(W, coreX0 + cx);
        var coreY1 = Math.Min(H, coreY0 + cy);
        var coreZ1 = Math.Min(D, coreZ0 + cz);
        return new OocBlockBounds(
            coreX0, coreX1, coreY0, coreY1, coreZ0, coreZ1,
            Math.Max(0, coreX0 - halo), Math.Min(W, coreX1 + halo),
            Math.Max(0, coreY0 - halo), Math.Min(H, coreY1 + halo),
            Math.Max(0, coreZ0 - halo), Math.Min(D, coreZ1 + halo));
    }

    private sealed class OocBlockFaces
    {
        public OocFacePlane LowX, HighX, LowY, HighY, LowZ, HighZ;
    }

    private static OocBlockFaces ProcessOutOfCoreBlock(CtImageStackDataset ct, PNMGeneratorOptions opt,
        PnmRegion roi, OocBlockBounds blk, int blockId, int erosions, int halo,
        bool needHighX, bool needHighY, bool needHighZ, bool needLowX, bool needLowY, bool needLowZ,
        Dictionary<long, OocPoreAccumulator> poreAgg,
        Dictionary<(long a, long b), OocEdgeAccumulator> edgeAgg,
        ref bool haloExceeded, CancellationToken token)
    {
        var eW = blk.ExtW;
        var eH = blk.ExtH;
        var eD = blk.ExtD;
        var voxels = (long)eW * eH * eD;
        if (voxels > int.MaxValue)
            throw new InvalidOperationException(
                $"Out-of-core block of {eW}×{eH}×{eD} voxels exceeds the addressable working set. " +
                "Lower the working-set budget so blocks are cut smaller.");

        // 1) Material mask, streamed slice by slice from the (possibly memory-mapped) label volume.
        var labels = ct.LabelData;
        var mask = new byte[voxels];
        Parallel.For(0, eD, new ParallelOptions
        {
            MaxDegreeOfParallelism = labels.IsMemoryMapped ? 2 : Math.Max(1, Environment.ProcessorCount - 1),
            CancellationToken = token
        }, () => new byte[ct.Width * ct.Height], (ez, _, sourceSlice) =>
        {
            labels.ReadSliceZ(roi.MinZ + blk.ExtZ0 + ez, sourceSlice);
            for (var ey = 0; ey < eH; ey++)
            {
                var row = (ez * eH + ey) * eW;
                var sourceRow = (roi.MinY + blk.ExtY0 + ey) * ct.Width + roi.MinX + blk.ExtX0;
                for (var ex = 0; ex < eW; ex++)
                    mask[row + ex] = sourceSlice[sourceRow + ex] == (byte)opt.MaterialId ? (byte)1 : (byte)0;
            }

            return sourceSlice;
        }, _ => { });

        token.ThrowIfCancellationRequested();

        // 2) Erosion to split constrictions. Exact for every voxel at least `erosions` from the
        //    window edge; the halo exceeds that, so core voxels and face planes are exact.
        var eroded = (byte[])mask.Clone();
        if (erosions > 0)
        {
            var tmp = new byte[eroded.Length];
            for (var i = 0; i < erosions; i++)
            {
                if (opt.UseOpenCL && TryBinaryErosionOpenCL(eroded, tmp, eW, eH, eD, (int)opt.Neighborhood))
                {
                    (eroded, tmp) = (tmp, eroded);
                }
                else
                {
                    BinaryErosionCPU(eroded, tmp, eW, eH, eD, opt.Neighborhood);
                    (eroded, tmp) = (tmp, eroded);
                }

                token.ThrowIfCancellationRequested();
            }
        }

        // 3) Seeds, watershed expansion, recovery — identical to the in-RAM pipeline.
        var ccResult = ConnectedComponents3D(eroded, eW, eH, eD, opt.Neighborhood);
        var expanded = WatershedExpansion(ccResult.Labels, mask, eW, eH, eD, opt.Neighborhood, null, 0f, 0f);
        RecoverUnseededRegions(expanded, mask, eW, eH, eD, ccResult.ComponentCount, opt.Neighborhood, null);

        token.ThrowIfCancellationRequested();

        // 4) Distance transform for radii.
        float[] edt;
        try
        {
            edt = opt.UseOpenCL && TryDistanceRelaxOpenCL(mask, eW, eH, eD)
                ? _lastDist
                : DistanceTransformApproxCPU(mask, eW, eH, eD);
        }
        catch
        {
            edt = DistanceTransformApproxCPU(mask, eW, eH, eD);
        }

        token.ThrowIfCancellationRequested();

        // 5) Accumulate pore statistics and throats over CORE voxels only, so each voxel of the
        //    volume contributes exactly once across all blocks.
        var lx0 = blk.CoreX0 - blk.ExtX0;
        var lx1 = blk.CoreX1 - blk.ExtX0;
        var ly0 = blk.CoreY0 - blk.ExtY0;
        var ly1 = blk.CoreY1 - blk.ExtY0;
        var lz0 = blk.CoreZ0 - blk.ExtZ0;
        var lz1 = blk.CoreZ1 - blk.ExtZ0;
        var blockHasCut = needLowX || needLowY || needLowZ || needHighX || needHighY || needHighZ;
        var localHaloExceeded = false;

        // Labels whose seed reaches this block's core. They own their pore: face stitching never
        // merges two native labels, only foreign views (spill-over basins, recovered regions) into
        // their native owner.
        var nativeSeeded = new HashSet<int>();

        for (var z = lz0; z < lz1; z++)
        for (var y = ly0; y < ly1; y++)
        {
            var row = (z * eH + y) * eW;
            for (var x = lx0; x < lx1; x++)
            {
                var idx = row + x;
                var lab = expanded[idx];
                if (lab <= 0) continue;

                var gid = MakeGid(blockId, lab);
                ref var acc = ref CollectionsMarshal.GetValueRefOrAddDefault(poreAgg, gid, out var existed);
                if (!existed) acc = OocPoreAccumulator.Empty;

                var gx = blk.ExtX0 + x;
                var gy = blk.ExtY0 + y;
                var gz = blk.ExtZ0 + z;

                // Open-face count matches the in-RAM path: window edges coincide with volume edges
                // for core voxels, because cut faces always carry at least one halo voxel.
                var openFaces = 0;
                if (x == 0 || mask[idx - 1] == 0) openFaces++;
                if (x == eW - 1 || mask[idx + 1] == 0) openFaces++;
                if (y == 0 || mask[idx - eW] == 0) openFaces++;
                if (y == eH - 1 || mask[idx + eW] == 0) openFaces++;
                if (z == 0 || mask[idx - eW * eH] == 0) openFaces++;
                if (z == eD - 1 || mask[idx + eW * eH] == 0) openFaces++;

                acc.Add(gx, gy, gz, openFaces, edt[idx]);
                if (eroded[idx] > 0) nativeSeeded.Add(lab);
                if (edt[idx] > halo && blockHasCut) localHaloExceeded = true;

                // Throats: each +axis voxel pair is owned by the block whose core contains the
                // lower voxel, so no pair is ever counted twice.
                AccumulateEdge(expanded, edt, edgeAgg, blockId, lab, idx, x + 1 < eW ? idx + 1 : -1);
                AccumulateEdge(expanded, edt, edgeAgg, blockId, lab, idx, y + 1 < eH ? idx + eW : -1);
                AccumulateEdge(expanded, edt, edgeAgg, blockId, lab, idx, z + 1 < eD ? idx + eW * eH : -1);
            }
        }

        if (localHaloExceeded) haloExceeded = true;

        // 6) Capture the face planes adjacent blocks will stitch against.
        var faces = new OocBlockFaces();
        if (needLowX) faces.LowX = ExtractFacePlaneX(expanded, mask, eroded, edt, blk, lx0, blockId, nativeSeeded);
        if (needHighX) faces.HighX = ExtractFacePlaneX(expanded, mask, eroded, edt, blk, lx1, blockId, nativeSeeded);
        if (needLowY) faces.LowY = ExtractFacePlaneY(expanded, mask, eroded, edt, blk, ly0, blockId, nativeSeeded);
        if (needHighY) faces.HighY = ExtractFacePlaneY(expanded, mask, eroded, edt, blk, ly1, blockId, nativeSeeded);
        if (needLowZ) faces.LowZ = ExtractFacePlaneZ(expanded, mask, eroded, edt, blk, lz0, blockId, nativeSeeded);
        if (needHighZ) faces.HighZ = ExtractFacePlaneZ(expanded, mask, eroded, edt, blk, lz1, blockId, nativeSeeded);
        return faces;
    }

    private static void AccumulateEdge(int[] expanded, float[] edt,
        Dictionary<(long a, long b), OocEdgeAccumulator> edgeAgg, int blockId, int lab, int idx, int nidx)
    {
        if (nidx < 0) return;
        var nlab = expanded[nidx];
        if (nlab <= 0 || nlab == lab) return;

        var ga = MakeGid(blockId, lab);
        var gb = MakeGid(blockId, nlab);
        var key = ga < gb ? (ga, gb) : (gb, ga);
        // Constriction radius at the interface, as in the in-RAM path.
        var r = MathF.Min(edt[idx], edt[nidx]);

        ref var acc = ref CollectionsMarshal.GetValueRefOrAddDefault(edgeAgg, key, out var existed);
        if (!existed) acc = OocEdgeAccumulator.Empty;
        acc.Add(r);
    }

    #endregion

    #region Face stitching

    /// <summary>
    ///     One block's labelling of a shared face plane: watershed labels plus eroded flags and the
    ///     distance transform. Two adjacent blocks label the same physical plane; reconciling the two
    ///     labellings is what joins pores that span the cut.
    ///     <para>
    ///         Stored sparsely: only mask voxels, in plane scan order. The mask is a deterministic
    ///         function of the source labels, so both sides of a cut compact to the same entry
    ///         sequence and the arrays align index-for-index. A whole layer of pending Z faces then
    ///         costs a porosity-fraction of a slice instead of a dense multiple of it, which is what
    ///         keeps very wide stacks within a low-RAM footprint.
    ///     </para>
    /// </summary>
    private sealed class OocFacePlane
    {
        public const byte ErodedFlag = 1;

        public int BlockId;
        public float[] Edt;
        public byte[] Flags;
        public int[] Labels;

        /// <summary> Labels of the owning block whose seed reaches its core (shared per block). </summary>
        public HashSet<int> NativeSeeded;
    }

    private static OocFacePlane ExtractFacePlaneX(int[] expanded, byte[] mask, byte[] eroded, float[] edt,
        OocBlockBounds blk, int planeX, int blockId, HashSet<int> nativeSeeded)
    {
        var eW = blk.ExtW;
        var eH = blk.ExtH;
        var ly0 = blk.CoreY0 - blk.ExtY0;
        var ly1 = blk.CoreY1 - blk.ExtY0;
        var lz0 = blk.CoreZ0 - blk.ExtZ0;
        var lz1 = blk.CoreZ1 - blk.ExtZ0;
        var plane = NewFacePlane((ly1 - ly0) * (lz1 - lz0), blockId, nativeSeeded);
        var o = 0;
        for (var z = lz0; z < lz1; z++)
        for (var y = ly0; y < ly1; y++)
            AppendFaceVoxel(plane, ref o, (z * eH + y) * eW + planeX, expanded, mask, eroded, edt);

        return TrimFacePlane(plane, o);
    }

    private static OocFacePlane ExtractFacePlaneY(int[] expanded, byte[] mask, byte[] eroded, float[] edt,
        OocBlockBounds blk, int planeY, int blockId, HashSet<int> nativeSeeded)
    {
        var eW = blk.ExtW;
        var eH = blk.ExtH;
        var lx0 = blk.CoreX0 - blk.ExtX0;
        var lx1 = blk.CoreX1 - blk.ExtX0;
        var lz0 = blk.CoreZ0 - blk.ExtZ0;
        var lz1 = blk.CoreZ1 - blk.ExtZ0;
        var plane = NewFacePlane((lx1 - lx0) * (lz1 - lz0), blockId, nativeSeeded);
        var o = 0;
        for (var z = lz0; z < lz1; z++)
        {
            var row = (z * eH + planeY) * eW;
            for (var x = lx0; x < lx1; x++)
                AppendFaceVoxel(plane, ref o, row + x, expanded, mask, eroded, edt);
        }

        return TrimFacePlane(plane, o);
    }

    private static OocFacePlane ExtractFacePlaneZ(int[] expanded, byte[] mask, byte[] eroded, float[] edt,
        OocBlockBounds blk, int planeZ, int blockId, HashSet<int> nativeSeeded)
    {
        var eW = blk.ExtW;
        var eH = blk.ExtH;
        var lx0 = blk.CoreX0 - blk.ExtX0;
        var lx1 = blk.CoreX1 - blk.ExtX0;
        var ly0 = blk.CoreY0 - blk.ExtY0;
        var ly1 = blk.CoreY1 - blk.ExtY0;
        var plane = NewFacePlane((lx1 - lx0) * (ly1 - ly0), blockId, nativeSeeded);
        var o = 0;
        for (var y = ly0; y < ly1; y++)
        {
            var row = (planeZ * eH + y) * eW;
            for (var x = lx0; x < lx1; x++)
                AppendFaceVoxel(plane, ref o, row + x, expanded, mask, eroded, edt);
        }

        return TrimFacePlane(plane, o);
    }

    private static OocFacePlane NewFacePlane(int size, int blockId, HashSet<int> nativeSeeded)
    {
        return new OocFacePlane
        {
            BlockId = blockId,
            Labels = new int[size],
            Flags = new byte[size],
            Edt = new float[size],
            NativeSeeded = nativeSeeded
        };
    }

    private static void AppendFaceVoxel(OocFacePlane plane, ref int o, int idx,
        int[] expanded, byte[] mask, byte[] eroded, float[] edt)
    {
        if (mask[idx] == 0) return;
        plane.Labels[o] = expanded[idx];
        plane.Edt[o] = edt[idx];
        plane.Flags[o] = eroded[idx] > 0 ? OocFacePlane.ErodedFlag : (byte)0;
        o++;
    }

    private static OocFacePlane TrimFacePlane(OocFacePlane plane, int count)
    {
        Array.Resize(ref plane.Labels, count);
        Array.Resize(ref plane.Flags, count);
        Array.Resize(ref plane.Edt, count);
        return plane;
    }

    /// <summary>
    ///     Reconciles two labellings of the same face plane.
    ///     <para>
    ///         Voxels that survive erosion in both windows carry exact seed identity — a connected
    ///         seed component crossing a cut must occupy the cut plane, and the halo exceeds the
    ///         erosion count so the erosion is exact there — which makes the unconditional unions a
    ///         complete distributed connected-component labelling of the seed mask.
    ///     </para>
    ///     <para>
    ///         For the rest, native labels (seed inside the owning block's core) are never merged
    ///         with each other: a voxel claimed by two native labels is watershed-contested ground,
    ///         exactly like the voxels the in-RAM watershed splits between two pores at a throat, so
    ///         the disagreement region is recorded as a throat interface instead — this recovers
    ///         throats whose meeting front lies on or near a cut, where neither block sees an
    ///         interface within its own labelling. Foreign labels — spill-over basins whose seed
    ///         lives in another block's core, and recovered regions, which have no seed at all —
    ///         additionally cast overlap votes; the driver merges each into its single majority
    ///         partner at the end, after which contested edges inside one pore resolve to self-edges
    ///         and are dropped.
    ///     </para>
    /// </summary>
    private static void MergeFacePlanes(OocFacePlane a, OocFacePlane b, GidUnionFind dsu,
        Dictionary<long, Dictionary<long, int>> foreignVotes, HashSet<long> strongMerged,
        Dictionary<(long a, long b), OocEdgeAccumulator> edgeAgg)
    {
        if (a == null || b == null) return;
        // Both sides compact the same physical plane with the same mask in the same scan order, so
        // the sparse entries align index-for-index. A length mismatch would mean the two blocks read
        // different source data — impossible short of a bug, but degrade loudly rather than misalign.
        if (a.Labels.Length != b.Labels.Length)
            Logger.LogError($"[PNMGenerator] Out-of-core: face plane mask mismatch " +
                            $"({a.Labels.Length} vs {b.Labels.Length} pore voxels); stitching what aligns.");
        var n = Math.Min(a.Labels.Length, b.Labels.Length);
        var counts = new Dictionary<(long ga, long gb), int>();

        for (var i = 0; i < n; i++)
        {
            var la = a.Labels[i];
            var lb = b.Labels[i];
            if (la <= 0 || lb <= 0) continue;

            var ga = MakeGid(a.BlockId, la);
            var gb = MakeGid(b.BlockId, lb);
            if ((a.Flags[i] & OocFacePlane.ErodedFlag) != 0 && (b.Flags[i] & OocFacePlane.ErodedFlag) != 0)
            {
                dsu.Union(ga, gb);
                strongMerged.Add(ga);
                strongMerged.Add(gb);
            }
            else
            {
                ref var c = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, (ga, gb), out _);
                c++;

                // Contested interface voxel: if the two labels end up as distinct pores this is
                // their throat, with the constriction radius read off the distance transform; if
                // they stitch into one pore it resolves to a self-edge and is dropped.
                var key = ga < gb ? (ga, gb) : (gb, ga);
                ref var acc = ref CollectionsMarshal.GetValueRefOrAddDefault(edgeAgg, key, out var existed);
                if (!existed) acc = OocEdgeAccumulator.Empty;
                acc.Add(MathF.Min(a.Edt[i], b.Edt[i]));
            }
        }

        foreach (var kv in counts)
        {
            var (ga, gb) = kv.Key;
            var aForeign = !a.NativeSeeded.Contains((int)(ga & 0xFFFFFFFF));
            var bForeign = !b.NativeSeeded.Contains((int)(gb & 0xFFFFFFFF));

            if (aForeign) Vote(foreignVotes, ga, gb, kv.Value);
            if (bForeign) Vote(foreignVotes, gb, ga, kv.Value);
        }
    }

    private static void Vote(Dictionary<long, Dictionary<long, int>> foreignVotes, long from, long to, int count)
    {
        if (!foreignVotes.TryGetValue(from, out var votes)) foreignVotes[from] = votes = new Dictionary<long, int>();
        ref var c = ref CollectionsMarshal.GetValueRefOrAddDefault(votes, to, out _);
        c += count;
    }

    #endregion

    #region Accumulators & union-find

    private struct OocPoreAccumulator
    {
        public double SumX, SumY, SumZ;
        public long Voxels, Area;
        public float MaxEdt;
        public int MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        public static OocPoreAccumulator Empty => new()
        {
            MinX = int.MaxValue, MinY = int.MaxValue, MinZ = int.MaxValue,
            MaxX = -1, MaxY = -1, MaxZ = -1
        };

        public void Add(int x, int y, int z, int openFaces, float edt)
        {
            SumX += x;
            SumY += y;
            SumZ += z;
            Voxels++;
            Area += openFaces;
            if (edt > MaxEdt) MaxEdt = edt;
            if (x < MinX) MinX = x;
            if (x > MaxX) MaxX = x;
            if (y < MinY) MinY = y;
            if (y > MaxY) MaxY = y;
            if (z < MinZ) MinZ = z;
            if (z > MaxZ) MaxZ = z;
        }

        public void Merge(in OocPoreAccumulator other)
        {
            SumX += other.SumX;
            SumY += other.SumY;
            SumZ += other.SumZ;
            Voxels += other.Voxels;
            Area += other.Area;
            if (other.MaxEdt > MaxEdt) MaxEdt = other.MaxEdt;
            if (other.MinX < MinX) MinX = other.MinX;
            if (other.MaxX > MaxX) MaxX = other.MaxX;
            if (other.MinY < MinY) MinY = other.MinY;
            if (other.MaxY > MaxY) MaxY = other.MaxY;
            if (other.MinZ < MinZ) MinZ = other.MinZ;
            if (other.MaxZ > MaxZ) MaxZ = other.MaxZ;
        }
    }

    private struct OocEdgeAccumulator
    {
        public float MaxRadius, MinRadius;
        public int Count;

        public static OocEdgeAccumulator Empty => new() { MinRadius = float.MaxValue };

        public void Add(float r)
        {
            if (r > MaxRadius) MaxRadius = r;
            if (r < MinRadius) MinRadius = r;
            Count++;
        }

        public void Merge(in OocEdgeAccumulator other)
        {
            if (other.MaxRadius > MaxRadius) MaxRadius = other.MaxRadius;
            if (other.MinRadius < MinRadius) MinRadius = other.MinRadius;
            Count += other.Count;
        }
    }

    /// <summary> Union-find over (block, label) identities packed into longs. </summary>
    private sealed class GidUnionFind
    {
        private readonly Dictionary<long, long> _parent = new();

        public long Find(long x)
        {
            if (!_parent.TryGetValue(x, out var p) || p == x) return x;
            var root = x;
            while (_parent.TryGetValue(root, out var pr) && pr != root) root = pr;
            while (_parent[x] != root)
            {
                var next = _parent[x];
                _parent[x] = root;
                x = next;
            }

            return root;
        }

        public void Union(long a, long b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra == rb) return;
            if (ra < rb) _parent[rb] = ra;
            else _parent[ra] = rb;
        }
    }

    #endregion

    #region Extent-based connectivity enforcement

    /// <summary>
    ///     Out-of-core counterpart of the label-scan boundary detection: a pore has a voxel inside an
    ///     axis-aligned boundary band exactly when its voxel extent reaches the band, so the per-pore
    ///     extents accumulated during streaming reproduce the in-RAM selection without a label volume.
    /// </summary>
    private static void EnforceInOutConnectivityFromExtents(Pore[] pores, List<Throat> throats,
        int[] minX, int[] maxX, int[] minY, int[] maxY, int[] minZ, int[] maxZ,
        FlowAxis axis, bool inletMin, bool outletMax, float vx, float vy, float vz)
    {
        if (pores == null || pores.Length <= 1) return;

        var poreIdToIndex = new Dictionary<int, int>();
        var phys = new Vector3[pores.Length];
        for (var i = 1; i < pores.Length; i++)
        {
            if (pores[i] == null) continue;
            poreIdToIndex[pores[i].ID] = i;
            phys[i] = new Vector3(pores[i].Position.X * vx, pores[i].Position.Y * vy, pores[i].Position.Z * vz);
        }

        // Material AABB from the pore extents.
        int aabbX0 = int.MaxValue, aabbY0 = int.MaxValue, aabbZ0 = int.MaxValue;
        int aabbX1 = -1, aabbY1 = -1, aabbZ1 = -1;
        for (var i = 1; i < pores.Length; i++)
        {
            if (pores[i] == null) continue;
            if (minX[i] < aabbX0) aabbX0 = minX[i];
            if (maxX[i] > aabbX1) aabbX1 = maxX[i];
            if (minY[i] < aabbY0) aabbY0 = minY[i];
            if (maxY[i] > aabbY1) aabbY1 = maxY[i];
            if (minZ[i] < aabbZ0) aabbZ0 = minZ[i];
            if (maxZ[i] > aabbZ1) aabbZ1 = maxZ[i];
        }

        if (aabbX1 < 0) return;

        var axisLength = (float)(axis switch
        {
            FlowAxis.X => aabbX1 - aabbX0,
            FlowAxis.Y => aabbY1 - aabbY0,
            _ => aabbZ1 - aabbZ0
        });

        var baseTol = Math.Max(3, (int)(axisLength * 0.05f));
        var minBoundaryPores = Math.Max(3, pores.Length / 100);
        var inletIdx = new HashSet<int>();
        var outletIdx = new HashSet<int>();

        for (var tolIncrease = 0; tolIncrease <= 10; tolIncrease++)
        {
            var tol = baseTol + tolIncrease * 2;
            inletIdx.Clear();
            outletIdx.Clear();

            for (var i = 1; i < pores.Length; i++)
            {
                if (pores[i] == null) continue;
                switch (axis)
                {
                    case FlowAxis.X:
                        if (inletMin && minX[i] <= aabbX0 + tol) inletIdx.Add(i);
                        if (outletMax && maxX[i] >= aabbX1 - tol) outletIdx.Add(i);
                        break;
                    case FlowAxis.Y:
                        if (inletMin && minY[i] <= aabbY0 + tol) inletIdx.Add(i);
                        if (outletMax && maxY[i] >= aabbY1 - tol) outletIdx.Add(i);
                        break;
                    default:
                        if (inletMin && minZ[i] <= aabbZ0 + tol) inletIdx.Add(i);
                        if (outletMax && maxZ[i] >= aabbZ1 - tol) outletIdx.Add(i);
                        break;
                }
            }

            if (inletIdx.Count >= minBoundaryPores && outletIdx.Count >= minBoundaryPores)
            {
                Logger.Log(
                    $"[Connectivity] Found {inletIdx.Count} inlet and {outletIdx.Count} outlet pores with tolerance {tol}");
                break;
            }
        }

        EnforceInOutConnectivityGraph(pores, throats, inletIdx, outletIdx, poreIdToIndex, phys,
            axis, axisLength, minBoundaryPores);
    }

    #endregion
}
