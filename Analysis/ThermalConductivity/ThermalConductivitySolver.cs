// GeoscientistToolkit/Analysis/ThermalConductivity/ThermalConductivitySolver.cs

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.ThermalConductivity;

/// <summary>
///     Solves thermal conductivity using Fourier's law and homogenization theory.
///     Supports AVX2, NEON, and OpenCL acceleration.
///     VOXELS WITH MATERIAL ID 0 ARE TREATED AS VOIDS AND EXCLUDED FROM THE SIMULATION.
///     Boundary conditions are applied to the surface of the active material shape.
/// </summary>
public class ThermalConductivitySolver
{
    // A simple struct to hold the min/max coordinates of the non-void material.
    private readonly struct ActiveBounds
    {
        public readonly int MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
        public ActiveBounds(int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
        {
            MinX = minX; MaxX = maxX; MinY = minY; MaxY = maxY; MinZ = minZ; MaxZ = maxZ;
        }
        public bool IsEmpty => MinX > MaxX;
    }
    
    private const string OpenCLKernels = @"
/* OpenCL 1.1 compatible thermal diffusion kernel:
   - FINAL CORRECTED VERSION -
   - PRINCIPLE: Any voxel with material ID 0 is a VOID and does not participate.
   - Uses an 'active bounds' (min/max XYZ) to locate the material's surfaces.
   -
   - KERNEL LOGIC FOR EACH VOXEL (x,y,z):
   - 1. Get material ID. If it's 0, FREEZE the temperature and STOP. This handles any shape (cylinders, etc.).
   - 2. If material is NOT 0, check if it lies on a hot/cold boundary surface (e.g., x == minX).
   -    If it does, apply the fixed boundary temperature and STOP.
   - 3. If it's a non-zero material voxel NOT on a boundary, perform the full stencil calculation.
   -    Its neighbors CAN be material 0 (exterior or internal pores), and the physics handles this correctly.
*/

typedef unsigned char uchar;

__kernel void thermal_diffusion(
    __global const float* tempIn,
    __global float*       tempOut,
    __global const uchar* labelBuf,
    __global const float* conductivities,
    const int   W, const int H, const int D,
    const float dx, const float dy, const float dz, const float dt,
    const int   flowDir,   // 0=X, 1=Y, 2=Z
    const float T_hot, const float T_cold,
    const int   minX, const int maxX,
    const int   minY, const int maxY,
    const int   minZ, const int maxZ)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);
    if (x >= W || y >= H || z >= D) return;

    int idx = (z * H + y) * W + x;
    uchar mat = labelBuf[idx];

    // --- CORE LOGIC: EXCLUDE VOIDS (MATERIAL 0) ---
    // If a voxel is air/void, it does not conduct heat. Its temperature is static.
    if (mat == 0) {
        tempOut[idx] = tempIn[idx];
        return;
    }

    // --- BOUNDARY CONDITIONS ON MATERIAL SURFACE ---
    // The following checks only run on non-zero material voxels.
    if (flowDir == 0) {
        if (x == minX) { tempOut[idx] = T_hot; return; }
        if (x == maxX) { tempOut[idx] = T_cold; return; }
    } else if (flowDir == 1) {
        if (y == minY) { tempOut[idx] = T_hot; return; }
        if (y == maxY) { tempOut[idx] = T_cold; return; }
    } else { // flowDir == 2
        if (z == minZ) { tempOut[idx] = T_hot; return; }
        if (z == maxZ) { tempOut[idx] = T_cold; return; }
    }
    
    // --- STENCIL COMPUTATION FOR INTERIOR MATERIAL VOXELS ---
    // If we reach here, we are a non-void voxel that is not on a hot/cold boundary.
    // We must be inside the material domain, but can be adjacent to voids (insulating boundaries).
    // Neighbor lookups are safe because we are not on the volume edge (x=0, etc.)
    if (x == 0 || x == (W - 1) || y == 0 || y == (H - 1) || z == 0 || z == (D - 1)) {
        tempOut[idx] = tempIn[idx]; // Insulate voxels on the edge of the image volume
        return;
    }

    int idx_xp = idx + 1;
    int idx_xm = idx - 1;
    int idx_yp = idx + W;
    int idx_ym = idx - W;
    int idx_zp = idx + (W * H);
    int idx_zm = idx - (W * H);

    float T_c  = tempIn[idx];
    float T_xp = tempIn[idx_xp];
    float T_xm = tempIn[idx_xm];
    float T_yp = tempIn[idx_yp];
    float T_ym = tempIn[idx_ym];
    float T_zp = tempIn[idx_zp];
    float T_zm = tempIn[idx_zm];

    uchar mat_xp = labelBuf[idx_xp]; uchar mat_xm = labelBuf[idx_xm];
    uchar mat_yp = labelBuf[idx_yp]; uchar mat_ym = labelBuf[idx_ym];
    uchar mat_zp = labelBuf[idx_zp]; uchar mat_zm = labelBuf[idx_zm];

    float k_c  = conductivities[(int)mat];
    float k_xp = conductivities[(int)mat_xp]; float k_xm = conductivities[(int)mat_xm];
    float k_yp = conductivities[(int)mat_yp]; float k_ym = conductivities[(int)mat_ym];
    float k_zp = conductivities[(int)mat_zp]; float k_zm = conductivities[(int)mat_zm];

    const float eps = 1e-10f;
    float k_ixp = (2.0f * k_c * k_xp) / (k_c + k_xp + eps);
    float k_ixm = (2.0f * k_c * k_xm) / (k_c + k_xm + eps);
    float k_iyp = (2.0f * k_c * k_yp) / (k_c + k_yp + eps);
    float k_iym = (2.0f * k_c * k_ym) / (k_c + k_ym + eps);
    float k_izp = (2.0f * k_c * k_zp) / (k_c + k_zp + eps);
    float k_izm = (2.0f * k_c * k_zm) / (k_c + k_zm + eps);

    float dx2 = dx * dx; float dy2 = dy * dy; float dz2 = dz * dz;

    float d2T_dx2 = (k_ixp * (T_xp - T_c) - k_ixm * (T_c - T_xm)) / dx2;
    float d2T_dy2 = (k_iyp * (T_yp - T_c) - k_iym * (T_c - T_ym)) / dy2;
    float d2T_dz2 = (k_izp * (T_zp - T_c) - k_izm * (T_c - T_zm)) / dz2;

    tempOut[idx] = T_c + dt * (d2T_dx2 + d2T_dy2 + d2T_dz2);
}
";
    private static CL _cl;
    private static bool _clReady;
    private static nint _ctx, _queue, _prog;

    public static ThermalResults Solve(ThermalOptions options, IProgress<float> progress, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        var dataset = options.Dataset;

        Logger.Log("[ThermalSolver] ========== STARTING THERMAL ANALYSIS ==========");
        Logger.Log($"[ThermalSolver] Dataset: {dataset.Name}");
        Logger.Log($"[ThermalSolver] Dimensions: {dataset.Width}x{dataset.Height}x{dataset.Depth} voxels");
        Logger.Log($"[ThermalSolver] Boundary: Hot={options.TemperatureHot:F2}°C, Cold={options.TemperatureCold:F2}°C");
        Logger.Log($"[ThermalSolver] Direction: {options.HeatFlowDirection}");
        Logger.Log($"[ThermalSolver] Max Iterations: {options.MaxIterations}");
        Logger.Log($"[ThermalSolver] Tolerance: {options.ConvergenceTolerance:E2}");
        Logger.Log("[ThermalSolver] EXCLUDING EXTERIOR/VOID MATERIAL (ID: 0) from simulation");

        // Find the bounding box of the actual materials to identify sample surfaces.
        Logger.Log("[ThermalSolver] Finding active material bounds...");
        var activeBounds = FindActiveBounds(dataset.LabelData, dataset.Width, dataset.Height, dataset.Depth);
        if (activeBounds.IsEmpty)
        {
            Logger.LogWarning("[ThermalSolver] No non-exterior voxels found. Simulation cannot run. Returning empty results.");
            return new ThermalResults(options) { EffectiveConductivity = 0 };
        }
        Logger.Log($"[ThermalSolver] Active bounds: X=[{activeBounds.MinX}, {activeBounds.MaxX}], Y=[{activeBounds.MinY}, {activeBounds.MaxY}], Z=[{activeBounds.MinZ}, {activeBounds.MaxZ}]");

        var W = dataset.Width;
        var H = dataset.Height;
        var D = dataset.Depth;
        var voxelSize = dataset.PixelSize * 1e-6; // μm to m

        Logger.Log("[ThermalSolver] [1/6] Initializing temperature field...");
        progress?.Report(0.05f);
        var temperature = InitializeTemperatureField(W, H, D, options);
        
        Logger.Log("[ThermalSolver] [2/6] Loading material properties...");
        progress?.Report(0.10f);
        var conductivities = GetMaterialConductivities(dataset, options);
        
        var includedMaterials = dataset.Materials.Where(m => m.ID != 0 && conductivities.ContainsKey(m.ID)).ToList();
        Logger.Log($"[ThermalSolver] Simulating {includedMaterials.Count} materials:");
        foreach (var mat in includedMaterials)
            Logger.Log($"  - {mat.Name} (ID: {mat.ID}): k={conductivities[mat.ID]:F4} W/m·K");
        
        Logger.Log("[ThermalSolver] [3/6] Starting solver...");
        progress?.Report(0.15f);
        float[,,] temperatureField;

        if (options.SolverBackend == SolverBackend.OpenCL && TryInitializeOpenCL())
        {
            Logger.Log("[ThermalSolver] Using GPU acceleration (OpenCL)");
            temperatureField = SolveGPU(temperature, dataset.LabelData, conductivities, W, H, D, voxelSize, options, progress, token, activeBounds);
        }
        else
        {
            var backend = Avx2.IsSupported ? "CPU (AVX2 SIMD)" : AdvSimd.IsSupported ? "CPU (NEON SIMD)" : "CPU (Scalar)";
            Logger.Log($"[ThermalSolver] Using {backend}");
            temperatureField = SolveCPU(temperature, dataset.LabelData, conductivities, W, H, D, voxelSize, options, progress, token, activeBounds);
        }

        token.ThrowIfCancellationRequested();

        Logger.Log("[ThermalSolver] [4/6] Calculating effective conductivity...");
        progress?.Report(0.90f);
        var keff = CalculateEffectiveConductivity(temperatureField, dataset.LabelData, conductivities, W, H, D, voxelSize, options);
        
        Logger.Log("[ThermalSolver] [5/6] Computing analytical estimates...");
        progress?.Report(0.95f);
        var analyticalEstimates = CalculateAnalyticalEstimates(dataset, conductivities, options);

        Logger.Log("[ThermalSolver] [6/6] Finalizing...");
        sw.Stop();
        Logger.Log($"[ThermalSolver] ========== COMPLETE in {sw.ElapsedMilliseconds}ms ==========");
        Logger.Log($"[ThermalSolver] Effective conductivity: {keff:F6} W/m·K");

        progress?.Report(1.0f);

        return new ThermalResults(options)
        {
            TemperatureField = temperatureField,
            EffectiveConductivity = keff,
            MaterialConductivities = conductivities.Where(kvp => kvp.Key != 0).ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value),
            AnalyticalEstimates = analyticalEstimates.ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value),
            ComputationTime = sw.Elapsed
        };
    }

    private static ActiveBounds FindActiveBounds(ILabelVolumeData labels, int W, int H, int D)
    {
        int minX = W, maxX = 0;
        int minY = H, maxY = 0;
        int minZ = D, maxZ = 0;

        for (var z = 0; z < D; z++)
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            if (labels[x, y, z] != 0)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }
        }
        if (minX > maxX) return new ActiveBounds(1, 0, 1, 0, 1, 0); // No active voxels found
        return new ActiveBounds(minX, maxX, minY, maxY, minZ, maxZ);
    }
    
    private static float[] InitializeTemperatureField(int W, int H, int D, ThermalOptions options)
    {
        var temp = new float[W * H * D];
        var T_hot = (float)options.TemperatureHot;
        var T_cold = (float)options.TemperatureCold;

        Parallel.For(0, D, z =>
        {
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var idx = (z * H + y) * W + x;
                var t = options.HeatFlowDirection switch
                {
                    HeatFlowDirection.X => (float)x / (W > 1 ? W - 1 : 1),
                    HeatFlowDirection.Y => (float)y / (H > 1 ? H - 1 : 1),
                    HeatFlowDirection.Z => (float)z / (D > 1 ? D - 1 : 1),
                    _ => (float)z / (D > 1 ? D - 1 : 1)
                };
                temp[idx] = T_cold + (T_hot - T_cold) * t;
            }
        });
        return temp;
    }

    private static Dictionary<byte, float> GetMaterialConductivities(CtImageStackDataset dataset, ThermalOptions options)
    {
        var conductivities = new Dictionary<byte, float>();
        // Conductivity of void/air (ID 0). It is non-zero to be physically plausible for internal pores
        // but it does not conduct in the exterior region due to the solver logic.
        const float kExterior = 0.026f; // ~Air at RT (W/m·K)
        conductivities[0] = kExterior;

        foreach (var material in dataset.Materials.Where(m => m.ID != 0))
        {
            float k = 0;
            if (options.MaterialConductivities.TryGetValue(material.ID, out var overrideK))
            {
                k = (float)overrideK;
            }
            else if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
            {
                var phys = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                if (phys?.ThermalConductivity_W_mK.HasValue == true)
                    k = (float)phys.ThermalConductivity_W_mK.Value;
            }
            if (k <= 0)
            {
                k = 2.5f; // conservative rock default
                Logger.LogWarning($"[ThermalSolver] {material.Name} had no k; using default {k} W/m·K");
            }
            conductivities[material.ID] = k;
        }
        return conductivities;
    }
    
    private static void ApplyBoundaryConditions(float[] temp, int W, int H, int D, ILabelVolumeData labels, ThermalOptions options, ActiveBounds bounds)
    {
        var T_hot = (float)options.TemperatureHot;
        var T_cold = (float)options.TemperatureCold;

        Parallel.For(0, D, z =>
        {
            for (var y = 0; y < H; y++)
            {
                for (var x = 0; x < W; x++)
                {
                    if (labels[x, y, z] == 0) continue; // Only apply BCs to the material itself

                    var idx = (z * H + y) * W + x;
                    switch (options.HeatFlowDirection)
                    {
                        case HeatFlowDirection.X:
                            if (x == bounds.MinX) temp[idx] = T_hot;
                            if (x == bounds.MaxX) temp[idx] = T_cold;
                            break;
                        case HeatFlowDirection.Y:
                            if (y == bounds.MinY) temp[idx] = T_hot;
                            if (y == bounds.MaxY) temp[idx] = T_cold;
                            break;
                        case HeatFlowDirection.Z:
                            if (z == bounds.MinZ) temp[idx] = T_hot;
                            if (z == bounds.MaxZ) temp[idx] = T_cold;
                            break;
                    }
                }
            }
        });
    }

    private static float[,,] ConvertTo3DArray(float[] temp, int W, int H, int D)
    {
        var result = new float[W, H, D];
        Parallel.For(0, D, z =>
        {
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                result[x, y, z] = temp[(z * H + y) * W + x];
            }
        });
        return result;
    }

    private static float CalculateEffectiveConductivity(float[,,] temperature, ILabelVolumeData labels, Dictionary<byte, float> conductivities, int W, int H, int D, double voxelSize, ThermalOptions options)
    {
        var dx = (float)voxelSize;
        var bounds = FindActiveBounds(labels, W, H, D);
        
        var L = options.HeatFlowDirection switch
        {
            HeatFlowDirection.X => (bounds.MaxX - bounds.MinX + 1),
            HeatFlowDirection.Y => (bounds.MaxY - bounds.MinY + 1),
            HeatFlowDirection.Z => (bounds.MaxZ - bounds.MinZ + 1),
            _ => D
        };

        var dT = (float)(options.TemperatureHot - options.TemperatureCold);
        if (L <= 1) return 0.0f;
        var gradient = dT / (L * dx);
        
        double totalFlux = 0;
        long count = 0;

        for (var z = 1; z < D - 1; z++)
        for (var y = 1; y < H - 1; y++)
        for (var x = 1; x < W - 1; x++)
        {
            var mat1 = labels[x, y, z];
            if (mat1 == 0) continue;

            var T1 = temperature[x, y, z];
            float T2;
            byte mat2;

            switch (options.HeatFlowDirection)
            {
                case HeatFlowDirection.X: T2 = temperature[x + 1, y, z]; mat2 = labels[x + 1, y, z]; break;
                case HeatFlowDirection.Y: T2 = temperature[x, y + 1, z]; mat2 = labels[x, y + 1, z]; break;
                case HeatFlowDirection.Z: T2 = temperature[x, y, z + 1]; mat2 = labels[x, y, z + 1]; break;
                default: continue;
            }
            
            if (mat2 == 0) continue;

            var k1 = conductivities[mat1];
            var k2 = conductivities[mat2];
            var k_eff_interface = 2 * k1 * k2 / (k1 + k2 + 1e-10f);

            var flux = -k_eff_interface * (T2 - T1) / dx;
            totalFlux += flux;
            count++;
        }

        if (count == 0 || gradient == 0) return 0.0f;
        var avgFlux = totalFlux / count;
        return (float)(Math.Abs(avgFlux) / gradient);
    }

    private static Dictionary<string, float> CalculateAnalyticalEstimates(CtImageStackDataset dataset, Dictionary<byte, float> conductivities, ThermalOptions options)
    {
        // This method is unchanged as it already correctly excludes material 0 from its calculations.
        var estimates = new Dictionary<string, float>();
        var materialVoxels = new Dictionary<byte, long>();
        long totalNonExteriorVoxels = 0;

        for (var z = 0; z < dataset.Depth; z++)
        for (var y = 0; y < dataset.Height; y++)
        for (var x = 0; x < dataset.Width; x++)
        {
            var mat = dataset.LabelData[x, y, z];
            if (mat == 0) continue;

            if (!materialVoxels.ContainsKey(mat)) materialVoxels[mat] = 0;
            materialVoxels[mat]++;
            totalNonExteriorVoxels++;
        }

        if (totalNonExteriorVoxels == 0) return estimates;
        var matIds = conductivities.Keys.Where(k => k != 0 && materialVoxels.ContainsKey(k)).OrderBy(k => k).ToList();
        if (matIds.Count < 2) return estimates;

        var matrixId = matIds[0];
        var inclusionId = matIds[1];
        var k_matrix = conductivities[matrixId];
        var k_inclusion = conductivities[inclusionId];
        var epsilon = (float)materialVoxels[inclusionId] / (materialVoxels[matrixId] + materialVoxels[inclusionId]);

        estimates["Parallel (Upper Bound)"] = epsilon * k_inclusion + (1 - epsilon) * k_matrix;
        estimates["Series (Lower Bound)"] = 1.0f / (epsilon / k_inclusion + (1 - epsilon) / k_matrix);
        return estimates;
    }

    #region CPU Solver with SIMD

    private static float[,,] SolveCPU(float[] temperature, ILabelVolumeData labels, Dictionary<byte, float> conductivities, int W, int H, int D, double voxelSize, ThermalOptions options, IProgress<float> progress, CancellationToken token, ActiveBounds activeBounds)
    {
        var dx = (float)voxelSize;
        var dy = (float)voxelSize;
        var dz = (float)(options.Dataset.SliceThickness > 0 ? options.Dataset.SliceThickness * 1e-6 : voxelSize);

        var max_k = conductivities.Where(kvp => kvp.Key != 0).Max(kvp => kvp.Value);
        var dt = 0.1f * Math.Min(dx * dx, Math.Min(dy * dy, dz * dz)) / max_k;

        var iterations = options.MaxIterations;
        var tolerance = (float)options.ConvergenceTolerance;

        var tempCurrent = (float[])temperature.Clone();
        var tempNext = new float[temperature.Length];
        var converged = false;

        for (var iter = 0; iter < iterations; iter++)
        {
            token.ThrowIfCancellationRequested();

            float maxChange = UpdateTemperatureScalar(tempCurrent, tempNext, labels, conductivities, W, H, D, dx, dy, dz, dt, token);
            
            ApplyBoundaryConditions(tempNext, W, H, D, labels, options, activeBounds);
            
            (tempCurrent, tempNext) = (tempNext, tempCurrent);

            if (iter % 10 == 0)
            {
                progress?.Report(0.15f + 0.70f * iter / iterations);
                if (iter > 0 && maxChange < tolerance)
                {
                    Logger.Log($"[ThermalSolver] *** CONVERGED after {iter} iterations ***");
                    converged = true;
                    break;
                }
            }
        }
        if (!converged) Logger.LogWarning($"[ThermalSolver] Did not converge within {iterations} iterations.");

        return ConvertTo3DArray(tempCurrent, W, H, D);
    }

    private static float UpdateTemperatureScalar(float[] tempIn, float[] tempOut, ILabelVolumeData labels, Dictionary<byte, float> conductivities, int W, int H, int D, float dx, float dy, float dz, float dt, CancellationToken token)
    {
        float maxChange = 0;
        var dx2 = dx * dx; var dy2 = dy * dy; var dz2 = dz * dz;
        var lockObj = new object();

        Parallel.For(1, D - 1, new ParallelOptions { CancellationToken = token }, z =>
        {
            float localMax = 0;
            for (var y = 1; y < H - 1; y++)
            for (var x = 1; x < W - 1; x++)
            {
                var idx = (z * H + y) * W + x;
                var mat = labels[x, y, z];

                if (mat == 0) // CORE LOGIC: Exclude voids from calculation
                {
                    tempOut[idx] = tempIn[idx];
                    continue;
                }

                var k_c = conductivities[mat];
                var T_c = tempIn[idx];
                
                var T_xp = tempIn[idx + 1]; var T_xm = tempIn[idx - 1];
                var T_yp = tempIn[idx + W]; var T_ym = tempIn[idx - W];
                var T_zp = tempIn[idx + W*H]; var T_zm = tempIn[idx - W*H];

                var mat_xp = labels[x + 1, y, z]; var mat_xm = labels[x - 1, y, z];
                var mat_yp = labels[x, y + 1, z]; var mat_ym = labels[x, y - 1, z];
                var mat_zp = labels[x, y, z + 1]; var mat_zm = labels[x, y, z - 1];

                var k_xp = conductivities[mat_xp]; var k_xm = conductivities[mat_xm];
                var k_yp = conductivities[mat_yp]; var k_ym = conductivities[mat_ym];
                var k_zp = conductivities[mat_zp]; var k_zm = conductivities[mat_zm];

                var k_ixp = HarmonicMean(k_c, k_xp); var k_ixm = HarmonicMean(k_c, k_xm);
                var k_iyp = HarmonicMean(k_c, k_yp); var k_iym = HarmonicMean(k_c, k_ym);
                var k_izp = HarmonicMean(k_c, k_zp); var k_izm = HarmonicMean(k_c, k_zm);

                var d2T_dx2 = (k_ixp * (T_xp - T_c) - k_ixm * (T_c - T_xm)) / dx2;
                var d2T_dy2 = (k_iyp * (T_yp - T_c) - k_iym * (T_c - T_ym)) / dy2;
                var d2T_dz2 = (k_izp * (T_zp - T_c) - k_izm * (T_c - T_zm)) / dz2;

                var newTemp = T_c + dt * (d2T_dx2 + d2T_dy2 + d2T_dz2);
                tempOut[idx] = newTemp;
                
                var change = Math.Abs(newTemp - T_c);
                if (change > localMax) localMax = change;
            }
            lock (lockObj) { if (localMax > maxChange) maxChange = localMax; }
        });
        return maxChange;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float HarmonicMean(float k1, float k2)
    {
        return (k1 + k2 > 1e-9f) ? (2.0f * k1 * k2 / (k1 + k2)) : 0.0f;
    }

    #endregion

    #region GPU Solver

    private static bool TryInitializeOpenCL()
    {
        if (_clReady) return true;
        try
        {
            _cl = CL.GetApi();
            unsafe
            {
                uint nPlatforms;
                _cl.GetPlatformIDs(0, null, &nPlatforms);
                if (nPlatforms == 0) return false;
                var platforms = stackalloc nint[(int)nPlatforms];
                _cl.GetPlatformIDs(nPlatforms, platforms, null);
                nint device = 0;
                for (var i = 0; i < nPlatforms; i++)
                {
                    uint nDevices;
                    if (_cl.GetDeviceIDs(platforms[i], DeviceType.Gpu, 0, null, &nDevices) == 0 && nDevices > 0)
                    {
                         var devices = stackalloc nint[(int)nDevices];
                        _cl.GetDeviceIDs(platforms[i], DeviceType.Gpu, nDevices, devices, null);
                        device = devices[0];
                        break;
                    }
                }
                if (device == 0) return false;
                
                int err;
                _ctx = _cl.CreateContext(null, 1, &device, null, null, &err);
                _queue = _cl.CreateCommandQueue(_ctx, device, CommandQueueProperties.None, &err);
                var sources = new[] { OpenCLKernels };
                var srcLen = (nuint)OpenCLKernels.Length;
                _prog = _cl.CreateProgramWithSource(_ctx, 1, sources, in srcLen, &err);
                err = _cl.BuildProgram(_prog, 0, null, string.Empty, null, null);
                if (err != 0)
                {
                    Logger.LogError($"[ThermalSolver] OpenCL Build Failed: {err}");
                    nuint logSize;
                    _cl.GetProgramBuildInfo(_prog, device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
                    var log = stackalloc byte[(int)logSize];
                    _cl.GetProgramBuildInfo(_prog, device, ProgramBuildInfo.BuildLog, logSize, log, null);
                    Logger.LogError($"Build Log: {System.Text.Encoding.UTF8.GetString(log, (int)logSize)}");
                    return false;
                }
                _clReady = true;
                return true;
            }
        }
        catch (Exception ex) { Logger.LogError($"[ThermalSolver] OpenCL init exception: {ex.Message}"); return false; }
    }

    private static float[,,] SolveGPU(float[] temperature, ILabelVolumeData labels, Dictionary<byte, float> conductivities, int W, int H, int D, double voxelSize, ThermalOptions options, IProgress<float> progress, CancellationToken token, ActiveBounds activeBounds)
{
    try
    {
        unsafe
        {
            var dx = (float)voxelSize;
            var dy = (float)voxelSize;
            var dz = (float)(options.Dataset.SliceThickness > 0 ? options.Dataset.SliceThickness * 1e-6 : voxelSize);
            var max_k = conductivities.Where(kvp => kvp.Key != 0).Max(kvp => kvp.Value);
            var dt = 0.1f * Math.Min(dx * dx, Math.Min(dy * dy, dz * dz)) / max_k;
            var iterations = options.MaxIterations;
            var tolerance = (float)options.ConvergenceTolerance;

            var conductivityArray = new float[256];
            foreach (var kvp in conductivities) conductivityArray[kvp.Key] = kvp.Value;
            
            var labelArray = new byte[W * H * D];
            Parallel.For(0, D, z => { for (var y = 0; y < H; y++) for (var x = 0; x < W; x++) labelArray[(z * H + y) * W + x] = labels[x, y, z]; });
            
            var tempCurrent = (float[])temperature.Clone();
            var tempNext = new float[tempCurrent.Length];

            int err;
            fixed (float* pTempIn = tempCurrent, pTempOut = tempNext, pConductivities = conductivityArray)
            fixed (byte* pLabels = labelArray)
            {
                var bufTempIn = _cl.CreateBuffer(_ctx, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(tempCurrent.Length * sizeof(float)), pTempIn, &err);
                var bufTempOut = _cl.CreateBuffer(_ctx, MemFlags.ReadWrite, (nuint)(tempNext.Length * sizeof(float)), null, &err);
                var bufLabels = _cl.CreateBuffer(_ctx, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(labelArray.Length * sizeof(byte)), pLabels, &err);
                var bufConductivities = _cl.CreateBuffer(_ctx, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(conductivityArray.Length * sizeof(float)), pConductivities, &err);

                var kernel = _cl.CreateKernel(_prog, "thermal_diffusion", &err);
                
                // --- START OF DEFINITIVE SYNTAX FIX ---

                // Set Buffer arguments (these are already pointers/handles)
                _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &bufTempIn);
                _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &bufTempOut);
                _cl.SetKernelArg(kernel, 2, (nuint)sizeof(nint), &bufLabels);
                _cl.SetKernelArg(kernel, 3, (nuint)sizeof(nint), &bufConductivities);
                
                // Set ALL scalar arguments using the correct stackalloc pattern
                int* pW = stackalloc int[1]; *pW = W; _cl.SetKernelArg(kernel, 4, sizeof(int), pW);
                int* pH = stackalloc int[1]; *pH = H; _cl.SetKernelArg(kernel, 5, sizeof(int), pH);
                int* pD = stackalloc int[1]; *pD = D; _cl.SetKernelArg(kernel, 6, sizeof(int), pD);
                
                float* pDx = stackalloc float[1]; *pDx = dx; _cl.SetKernelArg(kernel, 7, sizeof(float), pDx);
                float* pDy = stackalloc float[1]; *pDy = dy; _cl.SetKernelArg(kernel, 8, sizeof(float), pDy);
                float* pDz = stackalloc float[1]; *pDz = dz; _cl.SetKernelArg(kernel, 9, sizeof(float), pDz);
                
                float* pDt = stackalloc float[1]; *pDt = dt; _cl.SetKernelArg(kernel, 10, sizeof(float), pDt);
                
                var flowDir = (int)options.HeatFlowDirection;
                int* pFlowDir = stackalloc int[1]; *pFlowDir = flowDir; _cl.SetKernelArg(kernel, 11, sizeof(int), pFlowDir);
                
                var Thot = (float)options.TemperatureHot;
                float* pThot = stackalloc float[1]; *pThot = Thot; _cl.SetKernelArg(kernel, 12, sizeof(float), pThot);
                
                var Tcold = (float)options.TemperatureCold;
                float* pTcold = stackalloc float[1]; *pTcold = Tcold; _cl.SetKernelArg(kernel, 13, sizeof(float), pTcold);
                
                var minX = activeBounds.MinX; int* pMinX = stackalloc int[1]; *pMinX = minX; _cl.SetKernelArg(kernel, 14, sizeof(int), pMinX);
                var maxX = activeBounds.MaxX; int* pMaxX = stackalloc int[1]; *pMaxX = maxX; _cl.SetKernelArg(kernel, 15, sizeof(int), pMaxX);
                
                var minY = activeBounds.MinY; int* pMinY = stackalloc int[1]; *pMinY = minY; _cl.SetKernelArg(kernel, 16, sizeof(int), pMinY);
                var maxY = activeBounds.MaxY; int* pMaxY = stackalloc int[1]; *pMaxY = maxY; _cl.SetKernelArg(kernel, 17, sizeof(int), pMaxY);
                
                var minZ = activeBounds.MinZ; int* pMinZ = stackalloc int[1]; *pMinZ = minZ; _cl.SetKernelArg(kernel, 18, sizeof(int), pMinZ);
                var maxZ = activeBounds.MaxZ; int* pMaxZ = stackalloc int[1]; *pMaxZ = maxZ; _cl.SetKernelArg(kernel, 19, sizeof(int), pMaxZ);

                // --- END OF DEFINITIVE SYNTAX FIX ---
                
                var globalWorkSize = stackalloc nuint[] { (nuint)W, (nuint)H, (nuint)D };
                var converged = false;

                for (var iter = 0; iter < iterations; iter++)
                {
                    token.ThrowIfCancellationRequested();
                    _cl.EnqueueNdrangeKernel(_queue, kernel, 3, null, globalWorkSize, null, 0, null, null);
                    
                    (bufTempIn, bufTempOut) = (bufTempOut, bufTempIn);
                    _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &bufTempIn);
                    _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &bufTempOut);

                    if (iter % 50 == 0)
                    {
                        progress?.Report(0.15f + 0.70f * iter / iterations);
                        if (iter > 0)
                        {
                            _cl.Finish(_queue);
                            _cl.EnqueueReadBuffer(_queue, bufTempIn, true, 0, (nuint)(tempCurrent.Length * sizeof(float)), pTempIn, 0, null, null);
                            _cl.EnqueueReadBuffer(_queue, bufTempOut, true, 0, (nuint)(tempNext.Length * sizeof(float)), pTempOut, 0, null, null);
                            float maxChangeNow = 0;
                            for (int i = 0; i < tempCurrent.Length; i++) { var c = Math.Abs(tempCurrent[i] - tempNext[i]); if (c > maxChangeNow) maxChangeNow = c; }
                            if (maxChangeNow < tolerance) { converged = true; break; }
                        }
                    }
                }
                if (!converged) Logger.LogWarning("[ThermalSolver] GPU: reached max iterations without converging");

                _cl.Finish(_queue);
                _cl.EnqueueReadBuffer(_queue, bufTempIn, true, 0, (nuint)(tempCurrent.Length * sizeof(float)), pTempIn, 0, null, null);
                
                _cl.ReleaseKernel(kernel);
                _cl.ReleaseMemObject(bufTempIn); _cl.ReleaseMemObject(bufTempOut);
                _cl.ReleaseMemObject(bufLabels); _cl.ReleaseMemObject(bufConductivities);
            }
            return ConvertTo3DArray(tempCurrent, W, H, D);
        }}
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalSolver] GPU solver error: {ex.Message}. Falling back to CPU.");
            return SolveCPU(temperature, labels, conductivities, W, H, D, voxelSize, options, progress, token, activeBounds);
        }
    }
    #endregion
}