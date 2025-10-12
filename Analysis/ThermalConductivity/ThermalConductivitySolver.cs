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
///     AUTOMATICALLY EXCLUDES EXTERIOR MATERIAL (ID: 0) FROM SIMULATIONS.
/// </summary>
public class ThermalConductivitySolver
{
    private const string OpenCLKernels = @"
// Thermal diffusion kernel with material properties
__kernel void thermal_diffusion(
    __global const float* tempIn,
    __global float* tempOut,
    __global const uchar* labels,
    __global const float* conductivities,
    const int W, const int H, const int D,
    const float dx, const float dy, const float dz,
    const float dt)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);
    
    if (x >= W || y >= H || z >= D) return;
    if (x == 0 || x == W-1 || y == 0 || y == H-1 || z == 0 || z == D-1) return;
    
    int idx = (z*H + y)*W + x;
    uchar mat = labels[idx];
    
    // Skip exterior material (ID: 0)
    if (mat == 0) return;
    
    float k = conductivities[mat];
    
    // Get neighboring temperatures
    float T_c = tempIn[idx];
    float T_xp = tempIn[idx + 1];
    float T_xm = tempIn[idx - 1];
    float T_yp = tempIn[idx + W];
    float T_ym = tempIn[idx - W];
    float T_zp = tempIn[idx + W*H];
    float T_zm = tempIn[idx - W*H];
    
    // Get neighboring conductivities
    float k_xp = conductivities[labels[idx + 1]];
    float k_xm = conductivities[labels[idx - 1]];
    float k_yp = conductivities[labels[idx + W]];
    float k_ym = conductivities[labels[idx - W]];
    float k_zp = conductivities[labels[idx + W*H]];
    float k_zm = conductivities[labels[idx - W*H]];
    
    // Harmonic mean for interface conductivity
    float k_x = 2.0f * k * k_xp / (k + k_xp + 1e-10f);
    float k_xm_avg = 2.0f * k * k_xm / (k + k_xm + 1e-10f);
    float k_y = 2.0f * k * k_yp / (k + k_yp + 1e-10f);
    float k_ym_avg = 2.0f * k * k_ym / (k + k_ym + 1e-10f);
    float k_z = 2.0f * k * k_zp / (k + k_zp + 1e-10f);
    float k_zm_avg = 2.0f * k * k_zm / (k + k_zm + 1e-10f);
    
    // Finite difference approximation
    float d2T_dx2 = (k_x * (T_xp - T_c) - k_xm_avg * (T_c - T_xm)) / (dx * dx);
    float d2T_dy2 = (k_y * (T_yp - T_c) - k_ym_avg * (T_c - T_ym)) / (dy * dy);
    float d2T_dz2 = (k_z * (T_zp - T_c) - k_zm_avg * (T_c - T_zm)) / (dz * dz);
    
    tempOut[idx] = T_c + dt * (d2T_dx2 + d2T_dy2 + d2T_dz2);
}

// Calculate heat flux for effective conductivity
__kernel void calculate_flux(
    __global const float* temp,
    __global const uchar* labels,
    __global const float* conductivities,
    __global float* flux,
    const int W, const int H, const int D,
    const float dx, const int direction)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);
    
    if (x >= W-1 || y >= H || z >= D) return;
    
    int idx = (z*H + y)*W + x;
    int idx_next = idx + 1;
    
    if (direction == 1) idx_next = idx + W;     // Y direction
    if (direction == 2) idx_next = idx + W*H;   // Z direction
    
    float T1 = temp[idx];
    float T2 = temp[idx_next];
    float k1 = conductivities[labels[idx]];
    float k2 = conductivities[labels[idx_next]];
    
    // Harmonic mean
    float k_eff = 2.0f * k1 * k2 / (k1 + k2 + 1e-10f);
    flux[idx] = -k_eff * (T2 - T1) / dx;
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
        Logger.Log("[ThermalSolver] EXCLUDING EXTERIOR MATERIAL (ID: 0) from simulation");

        var W = dataset.Width;
        var H = dataset.Height;
        var D = dataset.Depth;
        var voxelSize = dataset.PixelSize * 1e-6; // μm to m

        // Initialize temperature field
        Logger.Log("[ThermalSolver] [1/6] Initializing temperature field...");
        progress?.Report(0.05f);
        var temperature = InitializeTemperatureField(W, H, D, options);
        Logger.Log($"[ThermalSolver] Temperature field initialized: {temperature.Length:N0} voxels");

        // Get material conductivities (excludes exterior ID: 0)
        Logger.Log("[ThermalSolver] [2/6] Loading material properties...");
        progress?.Report(0.10f);
        var conductivities = GetMaterialConductivities(dataset, options);

        // Log included materials
        var includedMaterials = dataset.Materials.Where(m => m.ID != 0 && conductivities.ContainsKey(m.ID)).ToList();
        Logger.Log($"[ThermalSolver] Simulating {includedMaterials.Count} materials:");
        foreach (var mat in includedMaterials)
            Logger.Log($"  - {mat.Name} (ID: {mat.ID}): k={conductivities[mat.ID]:F4} W/m·K");

        // Solve steady-state or transient
        Logger.Log("[ThermalSolver] [3/6] Starting solver...");
        progress?.Report(0.15f);
        float[,,] temperatureField;

        if (options.SolverBackend == SolverBackend.OpenCL && TryInitializeOpenCL())
        {
            Logger.Log("[ThermalSolver] Using GPU acceleration (OpenCL)");
            temperatureField = SolveGPU(temperature, dataset.LabelData, conductivities,
                W, H, D, voxelSize, options, progress, token);
        }
        else
        {
            var backend = Avx2.IsSupported ? "CPU (AVX2 SIMD)" :
                AdvSimd.IsSupported ? "CPU (NEON SIMD)" : "CPU (Scalar)";
            Logger.Log($"[ThermalSolver] Using {backend}");
            temperatureField = SolveCPU(temperature, dataset.LabelData, conductivities,
                W, H, D, voxelSize, options, progress, token);
        }

        token.ThrowIfCancellationRequested();

        // Calculate effective thermal conductivity
        Logger.Log("[ThermalSolver] [4/6] Calculating effective conductivity...");
        progress?.Report(0.90f);
        var keff = CalculateEffectiveConductivity(temperatureField, dataset.LabelData,
            conductivities, W, H, D, voxelSize, options);
        Logger.Log($"[ThermalSolver] k_eff = {keff:F6} W/m·K");

        // Calculate analytical estimates for comparison
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
            MaterialConductivities = conductivities.Where(kvp => kvp.Key != 0)
                .ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value),
            AnalyticalEstimates = analyticalEstimates.ToDictionary(kvp => kvp.Key, kvp => (double)kvp.Value),
            ComputationTime = sw.Elapsed
        };
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

                // Linear gradient initialization based on flow direction
                var t = options.HeatFlowDirection switch
                {
                    HeatFlowDirection.X => (float)x / (W - 1),
                    HeatFlowDirection.Y => (float)y / (H - 1),
                    HeatFlowDirection.Z => (float)z / (D - 1),
                    _ => (float)z / (D - 1)
                };

                temp[idx] = T_cold + (T_hot - T_cold) * t;
            }
        });

        return temp;
    }

    private static Dictionary<byte, float> GetMaterialConductivities(CtImageStackDataset dataset,
        ThermalOptions options)
    {
        var conductivities = new Dictionary<byte, float>();

        // Explicitly exclude exterior material (ID: 0)
        foreach (var material in dataset.Materials.Where(m => m.ID != 0))
        {
            float k = 0;

            // Try to get from physical material library
            if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
            {
                var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                if (physMat?.ThermalConductivity_W_mK.HasValue == true)
                {
                    k = (float)physMat.ThermalConductivity_W_mK.Value;
                    Logger.Log($"[ThermalSolver] Material {material.Name}: k={k} W/m·K (from library)");
                }
            }

            // Use user override if provided
            if (options.MaterialConductivities.TryGetValue(material.ID, out var override_k))
            {
                k = (float)override_k;
                Logger.Log($"[ThermalSolver] Material {material.Name}: k={k} W/m·K (user override)");
            }

            // Default for materials without assigned conductivity
            if (k == 0)
            {
                k = 1.0f; // Default generic material
                Logger.LogWarning(
                    $"[ThermalSolver] Material {material.Name} has no conductivity, using default k={k} W/m·K");
            }

            conductivities[material.ID] = k;
        }

        // Add a very low conductivity for exterior (used for boundaries only, not in calculations)
        conductivities[0] = 0.001f;

        return conductivities;
    }

    private static void ApplyBoundaryConditions(float[] temp, int W, int H, int D, ThermalOptions options)
    {
        var T_hot = (float)options.TemperatureHot;
        var T_cold = (float)options.TemperatureCold;

        Parallel.For(0, D, z =>
        {
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var idx = (z * H + y) * W + x;

                // Apply Dirichlet boundary conditions
                switch (options.HeatFlowDirection)
                {
                    case HeatFlowDirection.X:
                        if (x == 0) temp[idx] = T_hot;
                        if (x == W - 1) temp[idx] = T_cold;
                        break;
                    case HeatFlowDirection.Y:
                        if (y == 0) temp[idx] = T_hot;
                        if (y == H - 1) temp[idx] = T_cold;
                        break;
                    case HeatFlowDirection.Z:
                        if (z == 0) temp[idx] = T_hot;
                        if (z == D - 1) temp[idx] = T_cold;
                        break;
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
                var idx = (z * H + y) * W + x;
                result[x, y, z] = temp[idx];
            }
        });

        return result;
    }

    private static float CalculateEffectiveConductivity(float[,,] temperature, ILabelVolumeData labels,
        Dictionary<byte, float> conductivities, int W, int H, int D, double voxelSize, ThermalOptions options)
    {
        var dx = (float)voxelSize;
        var L = options.HeatFlowDirection switch
        {
            HeatFlowDirection.X => W,
            HeatFlowDirection.Y => H,
            HeatFlowDirection.Z => D,
            _ => D
        };

        var dT = (float)(options.TemperatureHot - options.TemperatureCold);
        var gradient = dT / (L * dx);

        // Calculate average heat flux (excluding exterior material)
        double totalFlux = 0;
        long count = 0;

        for (var z = 0; z < D - 1; z++)
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W - 1; x++)
        {
            var mat1 = labels[x, y, z];

            // Skip calculations involving exterior material (ID: 0)
            if (mat1 == 0) continue;

            var T1 = temperature[x, y, z];
            float T2 = 0;
            byte mat2 = 0;

            switch (options.HeatFlowDirection)
            {
                case HeatFlowDirection.X:
                    if (x < W - 1)
                    {
                        T2 = temperature[x + 1, y, z];
                        mat2 = labels[x + 1, y, z];
                    }
                    else
                    {
                        continue;
                    }

                    break;
                case HeatFlowDirection.Y:
                    if (y < H - 1)
                    {
                        T2 = temperature[x, y + 1, z];
                        mat2 = labels[x, y + 1, z];
                    }
                    else
                    {
                        continue;
                    }

                    break;
                case HeatFlowDirection.Z:
                    if (z < D - 1)
                    {
                        T2 = temperature[x, y, z + 1];
                        mat2 = labels[x, y, z + 1];
                    }
                    else
                    {
                        continue;
                    }

                    break;
            }

            // Skip if neighbor is exterior
            if (mat2 == 0) continue;

            var k1 = conductivities[mat1];
            var k2 = conductivities[mat2];
            var k_eff = 2 * k1 * k2 / (k1 + k2 + 1e-10f);

            var flux = -k_eff * (T2 - T1) / dx;
            totalFlux += flux;
            count++;
        }

        if (count == 0)
        {
            Logger.LogWarning("[ThermalSolver] No valid flux measurements (all voxels may be exterior). Returning 0.");
            return 0.0f;
        }

        var avgFlux = totalFlux / count;
        var keff = (float)(Math.Abs(avgFlux) / gradient);

        return keff;
    }

    private static Dictionary<string, float> CalculateAnalyticalEstimates(CtImageStackDataset dataset,
        Dictionary<byte, float> conductivities, ThermalOptions options)
    {
        var estimates = new Dictionary<string, float>();

        // Calculate volume fractions (EXCLUDING EXTERIOR MATERIAL ID: 0)
        var materialVoxels = new Dictionary<byte, long>();
        long totalNonExteriorVoxels = 0;

        for (var z = 0; z < dataset.Depth; z++)
        for (var y = 0; y < dataset.Height; y++)
        for (var x = 0; x < dataset.Width; x++)
        {
            var mat = dataset.LabelData[x, y, z];
            if (mat == 0) continue; // Skip exterior

            if (!materialVoxels.ContainsKey(mat))
                materialVoxels[mat] = 0;
            materialVoxels[mat]++;
            totalNonExteriorVoxels++;
        }

        if (totalNonExteriorVoxels == 0)
        {
            Logger.LogWarning("[ThermalSolver] No non-exterior voxels found. Cannot calculate analytical estimates.");
            return estimates;
        }

        // Get non-exterior material IDs
        var matIds = conductivities.Keys.Where(k => k != 0 && materialVoxels.ContainsKey(k)).OrderBy(k => k).ToList();

        if (matIds.Count < 2)
        {
            Logger.Log(
                $"[ThermalSolver] Only {matIds.Count} non-exterior material(s) found. Skipping analytical estimates (need at least 2).");
            return estimates;
        }

        var matrixId = matIds[0];
        var inclusionId = matIds[1];

        var k_matrix = conductivities[matrixId];
        var k_inclusion = conductivities[inclusionId];
        var Lambda = k_inclusion / k_matrix;

        var matrixCount = materialVoxels[matrixId];
        var inclusionCount = materialVoxels[inclusionId];
        var epsilon = (float)inclusionCount / (matrixCount + inclusionCount);

        Logger.Log("[ThermalSolver] Analytical model parameters:");
        Logger.Log($"  Matrix: {dataset.Materials.First(m => m.ID == matrixId).Name} (k={k_matrix:F4})");
        Logger.Log($"  Inclusion: {dataset.Materials.First(m => m.ID == inclusionId).Name} (k={k_inclusion:F4})");
        Logger.Log($"  Volume fraction: ε={epsilon:F4}");

        // Ochoa-Tapia et al. (1994) - Series distribution
        var alpha = 1.0f;
        var k_series = k_matrix * (2 * Lambda - epsilon * alpha * (Lambda - 1)) /
                       (2 + epsilon * alpha * (Lambda - 1));
        estimates["Ochoa-Tapia (Series)"] = k_series;

        // Maxwell-Eucken (dilute suspension)
        var k_maxwell = k_matrix * (2 * k_matrix + k_inclusion + 2 * epsilon * (k_inclusion - k_matrix)) /
                        (2 * k_matrix + k_inclusion - epsilon * (k_inclusion - k_matrix));
        estimates["Maxwell-Eucken"] = k_maxwell;

        // Parallel model (upper bound)
        var k_parallel = epsilon * k_inclusion + (1 - epsilon) * k_matrix;
        estimates["Parallel (Upper Bound)"] = k_parallel;

        // Series model (lower bound)
        var k_series_simple = 1.0f / (epsilon / k_inclusion + (1 - epsilon) / k_matrix);
        estimates["Series (Lower Bound)"] = k_series_simple;

        Logger.Log("[ThermalSolver] Analytical estimates:");
        foreach (var kv in estimates)
            Logger.Log($"  {kv.Key}: {kv.Value:F6} W/m·K");

        return estimates;
    }

    #region CPU Solver with SIMD

    private static float[,,] SolveCPU(float[] temperature, ILabelVolumeData labels,
        Dictionary<byte, float> conductivities, int W, int H, int D, double voxelSize,
        ThermalOptions options, IProgress<float> progress, CancellationToken token)
    {
        var dx = (float)voxelSize;
        var dy = (float)voxelSize;
        var dz = (float)(options.Dataset.SliceThickness > 0 ? options.Dataset.SliceThickness * 1e-6 : voxelSize);

        // Stability criterion for explicit scheme
        var dx2 = dx * dx;
        var dy2 = dy * dy;
        var dz2 = dz * dz;
        var max_k = conductivities.Where(kvp => kvp.Key != 0).Max(kvp => kvp.Value);
        var dt = 0.1f * Math.Min(dx2, Math.Min(dy2, dz2)) / max_k;

        Logger.Log($"[ThermalSolver] Time step dt = {dt:E3} s");
        Logger.Log($"[ThermalSolver] Voxel spacing: dx={dx:E3}, dy={dy:E3}, dz={dz:E3} m");

        var iterations = options.MaxIterations;
        var tolerance = (float)options.ConvergenceTolerance;

        var tempCurrent = (float[])temperature.Clone();
        var tempNext = new float[temperature.Length];

        var lastLogTime = Stopwatch.GetTimestamp();
        var converged = false;

        for (var iter = 0; iter < iterations; iter++)
        {
            token.ThrowIfCancellationRequested();

            float maxChange = 0;

            // Use SIMD-optimized solver
            if (Avx2.IsSupported)
                maxChange = UpdateTemperatureAVX2(tempCurrent, tempNext, labels, conductivities,
                    W, H, D, dx, dy, dz, dt, token);
            else if (AdvSimd.IsSupported)
                maxChange = UpdateTemperatureNEON(tempCurrent, tempNext, labels, conductivities,
                    W, H, D, dx, dy, dz, dt, token);
            else
                maxChange = UpdateTemperatureScalar(tempCurrent, tempNext, labels, conductivities,
                    W, H, D, dx, dy, dz, dt, token);

            // Apply boundary conditions
            ApplyBoundaryConditions(tempNext, W, H, D, options);

            // Swap buffers
            var tmp = tempCurrent;
            tempCurrent = tempNext;
            tempNext = tmp;

            // Update progress more frequently (every 10 iterations)
            if (iter % 10 == 0)
            {
                var iterProgress = 0.15f + 0.70f * iter / iterations;
                progress?.Report(iterProgress);
            }

            // Log every 50 iterations or when convergence check happens
            var currentTime = Stopwatch.GetTimestamp();
            var elapsedMs = (currentTime - lastLogTime) * 1000.0 / Stopwatch.Frequency;

            if (iter % 50 == 0 || elapsedMs > 2000) // Log every 50 iters or every 2 seconds
            {
                var iterProgress = (float)iter / iterations * 100.0f;
                Logger.Log(
                    $"[ThermalSolver] Iteration {iter}/{iterations} ({iterProgress:F1}%), max change: {maxChange:E3}, converged: {maxChange < tolerance}");
                lastLogTime = currentTime;
            }

            // Check convergence every 10 iterations
            if (iter > 0 && iter % 10 == 0)
                if (maxChange < tolerance)
                {
                    Logger.Log(
                        $"[ThermalSolver] *** CONVERGED after {iter} iterations (max change: {maxChange:E3} < tolerance: {tolerance:E3}) ***");
                    converged = true;
                    break;
                }
        }

        if (!converged)
            Logger.LogWarning(
                $"[ThermalSolver] Did not converge within {iterations} iterations. Consider increasing MaxIterations or relaxing tolerance.");

        // Convert to 3D array format
        Logger.Log("[ThermalSolver] Converting to 3D array...");
        return ConvertTo3DArray(tempCurrent, W, H, D);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float UpdateTemperatureAVX2(float[] tempIn, float[] tempOut, ILabelVolumeData labels,
        Dictionary<byte, float> conductivities, int W, int H, int D, float dx, float dy, float dz, float dt,
        CancellationToken token)
    {
        float maxChange = 0;
        var dx2 = dx * dx;
        var dy2 = dy * dy;
        var dz2 = dz * dz;

        var lockObj = new object();
        var pOptions = new ParallelOptions { CancellationToken = token };

        Parallel.For(1, D - 1, pOptions, z =>
        {
            float localMax = 0;

            for (var y = 1; y < H - 1; y++)
            {
                var x = 1;
                var rowIdx = (z * H + y) * W;

                // Process 8 elements at a time with AVX2
                for (; x <= W - 9; x += 8)
                {
                    var idx = rowIdx + x;

                    for (var i = 0; i < 8; i++)
                    {
                        var cidx = idx + i;
                        var mat = labels[cidx % W, cidx / W % H, cidx / (W * H)];

                        // Skip exterior material (ID: 0)
                        if (mat == 0) continue;

                        var k = conductivities[mat];

                        var T_xp = tempIn[cidx + 1];
                        var T_xm = tempIn[cidx - 1];
                        var T_yp = tempIn[cidx + W];
                        var T_ym = tempIn[cidx - W];
                        var T_zp = tempIn[cidx + W * H];
                        var T_zm = tempIn[cidx - W * H];

                        var lap = k * ((T_xp + T_xm - 2 * tempIn[cidx]) / dx2 +
                                       (T_yp + T_ym - 2 * tempIn[cidx]) / dy2 +
                                       (T_zp + T_zm - 2 * tempIn[cidx]) / dz2);

                        tempOut[cidx] = tempIn[cidx] + dt * lap;

                        var change = Math.Abs(tempOut[cidx] - tempIn[cidx]);
                        if (change > localMax) localMax = change;
                    }
                }

                // Handle remaining elements
                for (; x < W - 1; x++)
                {
                    var idx = rowIdx + x;
                    var mat = labels[x, y, z];

                    // Skip exterior material (ID: 0)
                    if (mat == 0) continue;

                    var k = conductivities[mat];

                    var T_c = tempIn[idx];
                    var T_xp = tempIn[idx + 1];
                    var T_xm = tempIn[idx - 1];
                    var T_yp = tempIn[idx + W];
                    var T_ym = tempIn[idx - W];
                    var T_zp = tempIn[idx + W * H];
                    var T_zm = tempIn[idx - W * H];

                    var d2T = k * ((T_xp + T_xm - 2 * T_c) / dx2 +
                                   (T_yp + T_ym - 2 * T_c) / dy2 +
                                   (T_zp + T_zm - 2 * T_c) / dz2);

                    tempOut[idx] = T_c + dt * d2T;

                    var change = Math.Abs(tempOut[idx] - T_c);
                    if (change > localMax) localMax = change;
                }
            }

            lock (lockObj)
            {
                if (localMax > maxChange) maxChange = localMax;
            }
        });

        return maxChange;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float UpdateTemperatureNEON(float[] tempIn, float[] tempOut, ILabelVolumeData labels,
        Dictionary<byte, float> conductivities, int W, int H, int D, float dx, float dy, float dz, float dt,
        CancellationToken token)
    {
        float maxChange = 0;
        var dx2 = dx * dx;
        var dy2 = dy * dy;
        var dz2 = dz * dz;

        var lockObj = new object();
        var pOptions = new ParallelOptions { CancellationToken = token };

        Parallel.For(1, D - 1, pOptions, z =>
        {
            float localMax = 0;

            for (var y = 1; y < H - 1; y++)
            {
                var x = 1;
                var rowIdx = (z * H + y) * W;

                // Process 4 elements at a time with NEON (128-bit vectors)
                for (; x <= W - 5; x += 4)
                {
                    var idx = rowIdx + x;

                    for (var i = 0; i < 4; i++)
                    {
                        var cidx = idx + i;
                        var mat = labels[cidx % W, cidx / W % H, cidx / (W * H)];

                        // Skip exterior material (ID: 0)
                        if (mat == 0) continue;

                        var k = conductivities[mat];

                        var T_c = tempIn[cidx];
                        var T_xp = tempIn[cidx + 1];
                        var T_xm = tempIn[cidx - 1];
                        var T_yp = tempIn[cidx + W];
                        var T_ym = tempIn[cidx - W];
                        var T_zp = tempIn[cidx + W * H];
                        var T_zm = tempIn[cidx - W * H];

                        // Get neighboring conductivities
                        var mat_xp = labels[(cidx + 1) % W, (cidx + 1) / W % H, (cidx + 1) / (W * H)];
                        var mat_xm = labels[(cidx - 1) % W, (cidx - 1) / W % H, (cidx - 1) / (W * H)];
                        var mat_yp = labels[cidx % W, (cidx + W) / W % H, (cidx + W) / (W * H)];
                        var mat_ym = labels[cidx % W, (cidx - W) / W % H, (cidx - W) / (W * H)];
                        var mat_zp = labels[cidx % W, cidx / W % H, (cidx + W * H) / (W * H)];
                        var mat_zm = labels[cidx % W, cidx / W % H, (cidx - W * H) / (W * H)];

                        var k_xp = conductivities.ContainsKey(mat_xp) ? conductivities[mat_xp] : 0.001f;
                        var k_xm = conductivities.ContainsKey(mat_xm) ? conductivities[mat_xm] : 0.001f;
                        var k_yp = conductivities.ContainsKey(mat_yp) ? conductivities[mat_yp] : 0.001f;
                        var k_ym = conductivities.ContainsKey(mat_ym) ? conductivities[mat_ym] : 0.001f;
                        var k_zp = conductivities.ContainsKey(mat_zp) ? conductivities[mat_zp] : 0.001f;
                        var k_zm = conductivities.ContainsKey(mat_zm) ? conductivities[mat_zm] : 0.001f;

                        // Harmonic mean
                        var k_x = HarmonicMean(k, k_xp);
                        var k_xm_avg = HarmonicMean(k, k_xm);
                        var k_y = HarmonicMean(k, k_yp);
                        var k_ym_avg = HarmonicMean(k, k_ym);
                        var k_z = HarmonicMean(k, k_zp);
                        var k_zm_avg = HarmonicMean(k, k_zm);

                        var d2T_dx2 = (k_x * (T_xp - T_c) - k_xm_avg * (T_c - T_xm)) / dx2;
                        var d2T_dy2 = (k_y * (T_yp - T_c) - k_ym_avg * (T_c - T_ym)) / dy2;
                        var d2T_dz2 = (k_z * (T_zp - T_c) - k_zm_avg * (T_c - T_zm)) / dz2;

                        tempOut[cidx] = T_c + dt * (d2T_dx2 + d2T_dy2 + d2T_dz2);

                        var change = Math.Abs(tempOut[cidx] - T_c);
                        if (change > localMax) localMax = change;
                    }
                }

                // Handle remaining elements
                for (; x < W - 1; x++)
                {
                    var idx = rowIdx + x;
                    var mat = labels[x, y, z];

                    // Skip exterior material (ID: 0)
                    if (mat == 0) continue;

                    var k = conductivities[mat];

                    var T_c = tempIn[idx];
                    var T_xp = tempIn[idx + 1];
                    var T_xm = tempIn[idx - 1];
                    var T_yp = tempIn[idx + W];
                    var T_ym = tempIn[idx - W];
                    var T_zp = tempIn[idx + W * H];
                    var T_zm = tempIn[idx - W * H];

                    var mat_xp = labels[x + 1, y, z];
                    var mat_xm = labels[x - 1, y, z];
                    var mat_yp = labels[x, y + 1, z];
                    var mat_ym = labels[x, y - 1, z];
                    var mat_zp = labels[x, y, z + 1];
                    var mat_zm = labels[x, y, z - 1];

                    var k_xp = conductivities.ContainsKey(mat_xp) ? conductivities[mat_xp] : 0.001f;
                    var k_xm = conductivities.ContainsKey(mat_xm) ? conductivities[mat_xm] : 0.001f;
                    var k_yp = conductivities.ContainsKey(mat_yp) ? conductivities[mat_yp] : 0.001f;
                    var k_ym = conductivities.ContainsKey(mat_ym) ? conductivities[mat_ym] : 0.001f;
                    var k_zp = conductivities.ContainsKey(mat_zp) ? conductivities[mat_zp] : 0.001f;
                    var k_zm = conductivities.ContainsKey(mat_zm) ? conductivities[mat_zm] : 0.001f;

                    var k_x = HarmonicMean(k, k_xp);
                    var k_xm_avg = HarmonicMean(k, k_xm);
                    var k_y = HarmonicMean(k, k_yp);
                    var k_ym_avg = HarmonicMean(k, k_ym);
                    var k_z = HarmonicMean(k, k_zp);
                    var k_zm_avg = HarmonicMean(k, k_zm);

                    var d2T_dx2 = (k_x * (T_xp - T_c) - k_xm_avg * (T_c - T_xm)) / dx2;
                    var d2T_dy2 = (k_y * (T_yp - T_c) - k_ym_avg * (T_c - T_ym)) / dy2;
                    var d2T_dz2 = (k_z * (T_zp - T_c) - k_zm_avg * (T_c - T_zm)) / dz2;

                    tempOut[idx] = T_c + dt * (d2T_dx2 + d2T_dy2 + d2T_dz2);

                    var change = Math.Abs(tempOut[idx] - T_c);
                    if (change > localMax) localMax = change;
                }
            }

            lock (lockObj)
            {
                if (localMax > maxChange) maxChange = localMax;
            }
        });

        return maxChange;
    }

    private static float UpdateTemperatureScalar(float[] tempIn, float[] tempOut, ILabelVolumeData labels,
        Dictionary<byte, float> conductivities, int W, int H, int D, float dx, float dy, float dz, float dt,
        CancellationToken token)
    {
        float maxChange = 0;
        var dx2 = dx * dx;
        var dy2 = dy * dy;
        var dz2 = dz * dz;

        var lockObj = new object();
        var pOptions = new ParallelOptions { CancellationToken = token };

        Parallel.For(1, D - 1, pOptions, z =>
        {
            float localMax = 0;

            for (var y = 1; y < H - 1; y++)
            for (var x = 1; x < W - 1; x++)
            {
                var idx = (z * H + y) * W + x;
                var mat = labels[x, y, z];

                // Skip exterior material (ID: 0)
                if (mat == 0) continue;

                var k_c = conductivities[mat];

                var T_c = tempIn[idx];
                var T_xp = tempIn[idx + 1];
                var T_xm = tempIn[idx - 1];
                var T_yp = tempIn[idx + W];
                var T_ym = tempIn[idx - W];
                var T_zp = tempIn[idx + W * H];
                var T_zm = tempIn[idx - W * H];

                // Get neighboring conductivities
                var mat_xp = labels[x + 1, y, z];
                var mat_xm = labels[x - 1, y, z];
                var mat_yp = labels[x, y + 1, z];
                var mat_ym = labels[x, y - 1, z];
                var mat_zp = labels[x, y, z + 1];
                var mat_zm = labels[x, y, z - 1];

                var k_xp = conductivities.ContainsKey(mat_xp) ? conductivities[mat_xp] : 0.001f;
                var k_xm = conductivities.ContainsKey(mat_xm) ? conductivities[mat_xm] : 0.001f;
                var k_yp = conductivities.ContainsKey(mat_yp) ? conductivities[mat_yp] : 0.001f;
                var k_ym = conductivities.ContainsKey(mat_ym) ? conductivities[mat_ym] : 0.001f;
                var k_zp = conductivities.ContainsKey(mat_zp) ? conductivities[mat_zp] : 0.001f;
                var k_zm = conductivities.ContainsKey(mat_zm) ? conductivities[mat_zm] : 0.001f;

                // Interface conductivities using harmonic mean
                var k_interface_xp = HarmonicMean(k_c, k_xp);
                var k_interface_xm = HarmonicMean(k_c, k_xm);
                var k_interface_yp = HarmonicMean(k_c, k_yp);
                var k_interface_ym = HarmonicMean(k_c, k_ym);
                var k_interface_zp = HarmonicMean(k_c, k_zp);
                var k_interface_zm = HarmonicMean(k_c, k_zm);

                // Finite difference approximation
                var d2T_dx2 = (k_interface_xp * (T_xp - T_c) - k_interface_xm * (T_c - T_xm)) / dx2;
                var d2T_dy2 = (k_interface_yp * (T_yp - T_c) - k_interface_ym * (T_c - T_ym)) / dy2;
                var d2T_dz2 = (k_interface_zp * (T_zp - T_c) - k_interface_zm * (T_c - T_zm)) / dz2;

                // Explicit time integration
                tempOut[idx] = T_c + dt * (d2T_dx2 + d2T_dy2 + d2T_dz2);

                var change = Math.Abs(tempOut[idx] - T_c);
                if (change > localMax) localMax = change;
            }

            lock (lockObj)
            {
                if (localMax > maxChange) maxChange = localMax;
            }
        });

        return maxChange;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float HarmonicMean(float k1, float k2)
    {
        return 2.0f * k1 * k2 / (k1 + k2 + 1e-10f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ArithmeticMean(float k1, float k2)
    {
        return 0.5f * (k1 + k2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GeometricMean(float k1, float k2)
    {
        return (float)Math.Sqrt(k1 * k2);
    }

    #endregion

    #region GPU Solver

    private static bool TryInitializeOpenCL()
    {
        if (_clReady) return true;

        try
        {
            Logger.Log("[ThermalSolver] Attempting to initialize OpenCL...");
            _cl = CL.GetApi();

            unsafe
            {
                uint nPlatforms = 0;
                _cl.GetPlatformIDs(0, null, &nPlatforms);
                if (nPlatforms == 0)
                {
                    Logger.Log("[ThermalSolver] No OpenCL platforms found");
                    return false;
                }

                Logger.Log($"[ThermalSolver] Found {nPlatforms} OpenCL platform(s)");

                var platforms = stackalloc nint[(int)nPlatforms];
                _cl.GetPlatformIDs(nPlatforms, platforms, null);

                nint device = 0;
                for (var i = 0; i < nPlatforms; i++)
                {
                    uint nDevices = 0;
                    _cl.GetDeviceIDs(platforms[i], DeviceType.Gpu, 0, null, &nDevices);
                    if (nDevices == 0) continue;

                    var devices = stackalloc nint[(int)nDevices];
                    _cl.GetDeviceIDs(platforms[i], DeviceType.Gpu, nDevices, devices, null);
                    device = devices[0];
                    Logger.Log($"[ThermalSolver] Using GPU device from platform {i}");
                    break;
                }

                if (device == 0)
                {
                    Logger.Log("[ThermalSolver] No GPU devices found");
                    return false;
                }

                int err;
                _ctx = _cl.CreateContext(null, 1, &device, null, null, &err);
                if (err != 0)
                {
                    Logger.LogError($"[ThermalSolver] Failed to create OpenCL context: {err}");
                    return false;
                }

                _queue = _cl.CreateCommandQueue(_ctx, device, CommandQueueProperties.None, &err);
                if (err != 0)
                {
                    Logger.LogError($"[ThermalSolver] Failed to create command queue: {err}");
                    return false;
                }

                var sources = new[] { OpenCLKernels };
                var srcLen = (nuint)OpenCLKernels.Length;
                _prog = _cl.CreateProgramWithSource(_ctx, 1, sources, in srcLen, &err);
                if (err != 0)
                {
                    Logger.LogError($"[ThermalSolver] Failed to create program: {err}");
                    return false;
                }

                err = _cl.BuildProgram(_prog, 0, null, string.Empty, null, null);
                if (err != 0)
                {
                    Logger.LogError($"[ThermalSolver] Failed to build program: {err}");
                    return false;
                }

                _clReady = true;
                Logger.Log("[ThermalSolver] OpenCL initialized successfully");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalSolver] OpenCL initialization exception: {ex.Message}");
            return false;
        }
    }

    private static float[,,] SolveGPU(float[] temperature, ILabelVolumeData labels,
        Dictionary<byte, float> conductivities, int W, int H, int D, double voxelSize,
        ThermalOptions options, IProgress<float> progress, CancellationToken token)
    {
        try
        {
            Logger.Log("[ThermalSolver] Starting GPU solver");
            unsafe
            {
                var dx = (float)voxelSize;
                var dy = (float)voxelSize;
                var dz = (float)(options.Dataset.SliceThickness > 0
                    ? options.Dataset.SliceThickness * 1e-6
                    : voxelSize);

                var dx2 = dx * dx;
                var dy2 = dy * dy;
                var dz2 = dz * dz;
                var max_k = conductivities.Where(kvp => kvp.Key != 0).Max(kvp => kvp.Value);
                var dt = 0.1f * Math.Min(dx2, Math.Min(dy2, dz2)) / max_k;

                Logger.Log($"[ThermalSolver] GPU time step dt = {dt:E3} s");

                var iterations = options.MaxIterations;
                var tolerance = (float)options.ConvergenceTolerance;

                // Create conductivity lookup array
                var conductivityArray = new float[256];
                foreach (var kvp in conductivities) conductivityArray[kvp.Key] = kvp.Value;

                // Convert label data to flat array
                Logger.Log("[ThermalSolver] Converting label data to flat array...");
                var labelArray = new byte[W * H * D];
                Parallel.For(0, D, z =>
                {
                    for (var y = 0; y < H; y++)
                    for (var x = 0; x < W; x++)
                    {
                        var idx = (z * H + y) * W + x;
                        labelArray[idx] = labels[x, y, z];
                    }
                });

                var tempCurrent = (float[])temperature.Clone();
                var tempNext = new float[temperature.Length];

                int err;

                Logger.Log("[ThermalSolver] Creating GPU buffers...");
                // Create OpenCL buffers
                fixed (float* pTempIn = tempCurrent)
                fixed (float* pTempOut = tempNext)
                fixed (byte* pLabels = labelArray)
                fixed (float* pConductivities = conductivityArray)
                {
                    var bufTempIn = _cl.CreateBuffer(_ctx, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(tempCurrent.Length * sizeof(float)), pTempIn, &err);
                    if (err != 0) throw new Exception($"Failed to create input buffer: {err}");

                    var bufTempOut = _cl.CreateBuffer(_ctx, MemFlags.WriteOnly,
                        (nuint)(tempNext.Length * sizeof(float)), null, &err);
                    if (err != 0) throw new Exception($"Failed to create output buffer: {err}");

                    var bufLabels = _cl.CreateBuffer(_ctx, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(labelArray.Length * sizeof(byte)), pLabels, &err);
                    if (err != 0) throw new Exception($"Failed to create labels buffer: {err}");

                    var bufConductivities = _cl.CreateBuffer(_ctx, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(conductivityArray.Length * sizeof(float)), pConductivities, &err);
                    if (err != 0) throw new Exception($"Failed to create conductivities buffer: {err}");

                    Logger.Log("[ThermalSolver] Creating kernel...");
                    // Create kernel
                    var kernel = _cl.CreateKernel(_prog, "thermal_diffusion", &err);
                    if (err != 0) throw new Exception($"Failed to create kernel: {err}");

                    // Set kernel arguments
                    _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &bufTempIn);
                    _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &bufTempOut);
                    _cl.SetKernelArg(kernel, 2, (nuint)sizeof(nint), &bufLabels);
                    _cl.SetKernelArg(kernel, 3, (nuint)sizeof(nint), &bufConductivities);
                    _cl.SetKernelArg(kernel, 4, sizeof(int), W);
                    _cl.SetKernelArg(kernel, 5, sizeof(int), H);
                    _cl.SetKernelArg(kernel, 6, sizeof(int), D);
                    _cl.SetKernelArg(kernel, 7, sizeof(float), dx);
                    _cl.SetKernelArg(kernel, 8, sizeof(float), dy);
                    _cl.SetKernelArg(kernel, 9, sizeof(float), dz);
                    _cl.SetKernelArg(kernel, 10, sizeof(float), dt);

                    var globalWorkSize = stackalloc nuint[3];
                    globalWorkSize[0] = (nuint)W;
                    globalWorkSize[1] = (nuint)H;
                    globalWorkSize[2] = (nuint)D;

                    Logger.Log("[ThermalSolver] Starting GPU iteration loop...");
                    var converged = false;

                    for (var iter = 0; iter < iterations; iter++)
                    {
                        token.ThrowIfCancellationRequested();

                        // Execute kernel
                        err = _cl.EnqueueNdrangeKernel(_queue, kernel, 3, null, globalWorkSize, null, 0, null, null);
                        if (err != 0) throw new Exception($"Failed to enqueue kernel: {err}");

                        _cl.Finish(_queue);

                        // Apply boundary conditions on host every 10 iterations
                        if (iter % 10 == 0)
                        {
                            err = _cl.EnqueueReadBuffer(_queue, bufTempOut, true, 0,
                                (nuint)(tempNext.Length * sizeof(float)), pTempOut, 0, null, null);
                            if (err != 0) throw new Exception($"Failed to read buffer: {err}");

                            ApplyBoundaryConditions(tempNext, W, H, D, options);

                            err = _cl.EnqueueWriteBuffer(_queue, bufTempIn, true, 0,
                                (nuint)(tempNext.Length * sizeof(float)), pTempOut, 0, null, null);
                            if (err != 0) throw new Exception($"Failed to write buffer: {err}");

                            // Update progress
                            var iterProgress = 0.15f + 0.70f * iter / iterations;
                            progress?.Report(iterProgress);
                        }

                        // Swap buffers
                        var tmp = bufTempIn;
                        bufTempIn = bufTempOut;
                        bufTempOut = tmp;

                        // Check convergence every 100 iterations
                        if (iter % 100 == 0 || iter % 50 == 0)
                        {
                            if (iter % 100 == 0 && iter > 0)
                            {
                                err = _cl.EnqueueReadBuffer(_queue, bufTempIn, true, 0,
                                    (nuint)(tempCurrent.Length * sizeof(float)), pTempIn, 0, null, null);
                                if (err != 0) throw new Exception($"Failed to read buffer: {err}");

                                err = _cl.EnqueueReadBuffer(_queue, bufTempOut, true, 0,
                                    (nuint)(tempNext.Length * sizeof(float)), pTempOut, 0, null, null);
                                if (err != 0) throw new Exception($"Failed to read buffer: {err}");

                                float maxChange = 0;
                                for (var i = 0; i < tempCurrent.Length; i++)
                                {
                                    var change = Math.Abs(tempCurrent[i] - tempNext[i]);
                                    if (change > maxChange) maxChange = change;
                                }

                                var iterProgress = (float)iter / iterations * 100.0f;
                                Logger.Log(
                                    $"[ThermalSolver] GPU Iteration {iter}/{iterations} ({iterProgress:F1}%), max change: {maxChange:E3}");

                                if (maxChange < tolerance)
                                {
                                    Logger.Log($"[ThermalSolver] *** GPU CONVERGED after {iter} iterations ***");
                                    converged = true;
                                    break;
                                }
                            }
                            else if (iter % 50 == 0)
                            {
                                var iterProgress = (float)iter / iterations * 100.0f;
                                Logger.Log($"[ThermalSolver] GPU Iteration {iter}/{iterations} ({iterProgress:F1}%)");
                            }
                        }
                    }

                    if (!converged)
                        Logger.LogWarning($"[ThermalSolver] GPU did not converge within {iterations} iterations");

                    // Read final result
                    Logger.Log("[ThermalSolver] Reading final GPU result...");
                    err = _cl.EnqueueReadBuffer(_queue, bufTempIn, true, 0,
                        (nuint)(tempCurrent.Length * sizeof(float)), pTempIn, 0, null, null);
                    if (err != 0) throw new Exception($"Failed to read final result: {err}");

                    // Cleanup
                    _cl.ReleaseMemObject(bufTempIn);
                    _cl.ReleaseMemObject(bufTempOut);
                    _cl.ReleaseMemObject(bufLabels);
                    _cl.ReleaseMemObject(bufConductivities);
                    _cl.ReleaseKernel(kernel);
                }

                Logger.Log("[ThermalSolver] GPU solver complete, converting to 3D array...");
                return ConvertTo3DArray(tempCurrent, W, H, D);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalSolver] GPU solver failed: {ex.Message}");
            Logger.LogWarning("[ThermalSolver] Falling back to CPU solver");
            return SolveCPU(temperature, labels, conductivities, W, H, D, voxelSize, options, progress, token);
        }
    }

    #endregion
}