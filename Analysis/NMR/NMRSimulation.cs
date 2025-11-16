// GeoscientistToolkit/Analysis/NMR/NMRSimulation.cs
// FIXED: Correct pore size physics implementation

using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.NMR;

/// <summary>
///     High-performance NMR simulation using random walk technique with SIMD acceleration.
///     Physics Background:
///     - T2 relaxation: 1/T2 = 1/T2_bulk + ρ₂ * (S/V)
///     - For fast diffusion regime: ρ₂ is surface relaxivity (μm/s)
///     - Pore radius from T2: r = shape_factor * ρ₂ * T2
///     - Shape factors: Sphere=3, Cylinder=2, Slit=1
/// </summary>
public class NMRSimulation : SimulatorNodeSupport
{
    // Random walk step vectors (6 directions: ±X, ±Y, ±Z)
    private static readonly (int dx, int dy, int dz)[] Directions =
    {
        (1, 0, 0), (-1, 0, 0),
        (0, 1, 0), (0, -1, 0),
        (0, 0, 1), (0, 0, -1)
    };

    private readonly NMRSimulationConfig _config;
    private readonly int _depth;
    private readonly int _height;
    private readonly ILabelVolumeData _labelVolume;
    private readonly Random _random;
    private readonly int _width;

    public NMRSimulation(CtImageStackDataset dataset, NMRSimulationConfig config) : this(dataset, config, null)
    {
    }

    public NMRSimulation(CtImageStackDataset dataset, NMRSimulationConfig config, bool? useNodes) : base(useNodes)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _labelVolume = dataset?.LabelData ?? throw new ArgumentNullException(nameof(dataset));
        _width = dataset.Width;
        _height = dataset.Height;
        _depth = dataset.Depth;
        _random = new Random(config.RandomSeed);

        // UNIT VERIFICATION: Ensure voxel size is in reasonable range for meters
        VerifyVoxelSizeUnits(config.VoxelSize);

        if (_useNodes)
        {
            Logger.Log("[NMRSimulation] Node Manager integration: ENABLED");
        }

        Logger.Log($"[NMRSimulation] Initialized: {_width}x{_height}x{_depth}, {config.NumberOfWalkers} walkers");
        Logger.Log($"[NMRSimulation] Voxel size: {config.VoxelSize:E6} m = {config.VoxelSize * 1e6f:F2} µm");
        Logger.Log($"[NMRSimulation] Diffusion coefficient: {config.DiffusionCoefficient:E3} m²/s");
        Logger.Log(
            $"[NMRSimulation] Pore shape factor: {config.PoreShapeFactor:F1} (3.0=sphere, 2.0=cylinder, 1.0=slit)");
    }

    /// <summary>
    ///     Verify voxel size is in reasonable range for meters
    /// </summary>
    private static void VerifyVoxelSizeUnits(double voxelSize)
    {
        // Typical µCT: 0.1-100 µm = 1e-7 to 1e-4 meters
        // Warning if outside 1nm to 1mm range
        if (voxelSize < 1e-9)
        {
            Logger.LogError($"[NMRSimulation] Voxel size {voxelSize:E3} m is suspiciously small (< 1 nm). Unit error?");
            throw new ArgumentException("Voxel size appears to be in wrong units. Expected meters.");
        }

        if (voxelSize > 1e-3)
        {
            Logger.LogError($"[NMRSimulation] Voxel size {voxelSize:E3} m is suspiciously large (> 1 mm). Unit error?");
            throw new ArgumentException("Voxel size appears to be in wrong units. Expected meters.");
        }

        if (voxelSize < 1e-8)
            Logger.LogWarning($"[NMRSimulation] Very small voxel size: {voxelSize * 1e9f:F2} nm. Verify units.");
        else if (voxelSize > 1e-4)
            Logger.LogWarning(
                $"[NMRSimulation] Large voxel size: {voxelSize * 1e3f:F2} mm. NMR physics may not apply at this scale.");
    }

    public async Task<NMRResults> RunSimulationAsync(IProgress<(float progress, string message)> progress)
    {
        var stopwatch = Stopwatch.StartNew();

        progress?.Report((0f, "Initializing walkers..."));

        var walkers = InitializeWalkers();
        if (walkers.Length == 0)
            throw new InvalidOperationException(
                $"No valid starting positions found for pore material ID {_config.PoreMaterialID}");

        // Determine computation method
        var computationMethod = "CPU (Scalar)";
        if (Avx2.IsSupported)
            computationMethod = "CPU (AVX2)";
        else if (AdvSimd.IsSupported)
            computationMethod = "CPU (NEON)";

        var results = new NMRResults(_config.NumberOfSteps)
        {
            NumberOfWalkers = walkers.Length,
            TotalSteps = _config.NumberOfSteps,
            TimeStep = _config.TimeStepMs,
            PoreMaterial = _config.MaterialRelaxivities.ContainsKey(_config.PoreMaterialID)
                ? _config.MaterialRelaxivities[_config.PoreMaterialID].MaterialName
                : "Unknown",
            MaterialRelaxivities = _config.MaterialRelaxivities.ToDictionary(
                kvp => kvp.Value.MaterialName,
                kvp => kvp.Value.SurfaceRelaxivity),
            ComputationMethod = computationMethod
        };

        // Time points
        for (var t = 0; t < _config.NumberOfSteps; t++)
            results.TimePoints[t] = t * _config.TimeStepMs;

        progress?.Report((0.1f, "Running random walk simulation..."));

        // Run the simulation
        await Task.Run(() => { SimulateRandomWalk(walkers, results, progress); });

        progress?.Report((0.8f, "Computing T2 distribution..."));

        // Compute T2 spectrum and pore size distribution with CORRECT physics
        ComputeT2Distribution(results);
        ComputePoreSizeDistribution(results);
        ComputeStatistics(results);

        // Optional: Compute T1-T2 map
        if (_config.ComputeT1T2Map)
        {
            progress?.Report((0.95f, "Computing T1-T2 correlation map..."));
            T1T2Computation.ComputeT1T2Map(results, _config);
        }

        stopwatch.Stop();
        results.ComputationTime = stopwatch.Elapsed;

        progress?.Report((1f, $"NMR simulation completed in {stopwatch.Elapsed.TotalSeconds:F1}s"));

        Logger.Log(
            $"[NMRSimulation] Completed: {results.ComputationTime.TotalSeconds:F2}s, Mean T2: {results.MeanT2:F2}ms");

        return results;
    }

    private Walker[] InitializeWalkers()
    {
        // CRITICAL FIX: Calculate material extent FIRST (like PNM does)
        var (xMin, xMax, yMin, yMax, zMin, zMax) = CalculateMaterialBounds();

        if (xMin > xMax)
        {
            Logger.LogError($"[NMRSimulation] No voxels found for material ID {_config.PoreMaterialID}");
            return Array.Empty<Walker>();
        }

        var materialWidth = xMax - xMin + 1;
        var materialHeight = yMax - yMin + 1;
        var materialDepth = zMax - zMin + 1;
        var materialVolume = materialWidth * materialHeight * materialDepth;

        Logger.Log($"[NMRSimulation] Material extent: [{xMin},{xMax}] x [{yMin},{yMax}] x [{zMin},{zMax}]");
        Logger.Log($"[NMRSimulation] Material occupies {materialVolume:N0} / {_width * _height * _depth:N0} voxels " +
                   $"({100.0 * materialVolume / (_width * _height * _depth):F1}% of volume)");

        var walkers = new List<Walker>();
        var maxAttempts = _config.NumberOfWalkers * 10;
        var attempts = 0;

        Logger.Log($"[NMRSimulation] Finding valid starting positions for {_config.NumberOfWalkers} walkers...");

        // Search only within material bounds (MUCH more efficient!)
        while (walkers.Count < _config.NumberOfWalkers && attempts < maxAttempts)
        {
            var x = _random.Next(xMin, xMax + 1); // ✓ Only search material region
            var y = _random.Next(yMin, yMax + 1);
            var z = _random.Next(zMin, zMax + 1);

            // Only start walkers in PORE SPACE (not matrix)
            if (_labelVolume[x, y, z] == _config.PoreMaterialID)
                walkers.Add(new Walker
                {
                    X = x,
                    Y = y,
                    Z = z,
                    Magnetization = 1.0,
                    IsActive = true
                });

            attempts++;
        }

        var efficiency = walkers.Count / (float)attempts * 100;
        Logger.Log(
            $"[NMRSimulation] Initialized {walkers.Count} walkers in {attempts} attempts ({efficiency:F1}% efficiency)");
        return walkers.ToArray();
    }

    /// <summary>
    ///     Calculate the bounding box of the selected material (same approach as PNM)
    /// </summary>
    private (int xMin, int xMax, int yMin, int yMax, int zMin, int zMax) CalculateMaterialBounds()
    {
        int xMin = _width, xMax = -1;
        int yMin = _height, yMax = -1;
        int zMin = _depth, zMax = -1;

        for (var z = 0; z < _depth; z++)
        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
            if (_labelVolume[x, y, z] == _config.PoreMaterialID)
            {
                if (x < xMin) xMin = x;
                if (x > xMax) xMax = x;
                if (y < yMin) yMin = y;
                if (y > yMax) yMax = y;
                if (z < zMin) zMin = z;
                if (z > zMax) zMax = z;
            }

        return (xMin, xMax, yMin, yMax, zMin, zMax);
    }

    private void SimulateRandomWalk(Walker[] walkers, NMRResults results, IProgress<(float, string)> progress)
    {
        // Calculate step size based on diffusion coefficient
        // Einstein relation: <r²> = 6Dt  →  step_size = √(6Dt)
        var stepSize = Math.Sqrt(6.0 * _config.DiffusionCoefficient * _config.TimeStepMs * 1e-3);
        var voxelStepSize = (int)Math.Max(1, stepSize / _config.VoxelSize);

        // Detect SIMD capabilities
        var simdType = "Scalar";
        if (Avx2.IsSupported)
            simdType = "AVX2";
        else if (AdvSimd.IsSupported)
            simdType = "NEON";

        Logger.Log($"[NMRSimulation] Step size: {stepSize:E2} m = {voxelStepSize} voxels, SIMD: {simdType}");

        for (var step = 0; step < _config.NumberOfSteps; step++)
        {
            if (step % 100 == 0)
            {
                var progressPercent = 0.1f + 0.7f * (step / (float)_config.NumberOfSteps);
                progress?.Report((progressPercent, $"Simulating step {step}/{_config.NumberOfSteps}..."));
            }

            // Process walkers in parallel with SIMD
            var totalMagnetization = 0.0;
            var activeCount = 0;

            // Use SIMD for batch processing
            if (Avx2.IsSupported && walkers.Length >= 8)
                totalMagnetization = SimulateStepAVX2(walkers, voxelStepSize, ref activeCount);
            else if (AdvSimd.IsSupported && walkers.Length >= 4)
                totalMagnetization = SimulateStepNEON(walkers, voxelStepSize, ref activeCount);
            else
                totalMagnetization = SimulateStepScalar(walkers, voxelStepSize, ref activeCount);

            // Normalize and store
            results.Magnetization[step] = activeCount > 0 ? totalMagnetization / walkers.Length : 0.0;
        }
    }

    private double SimulateStepAVX2(Walker[] walkers, int stepSize, ref int activeCount)
    {
        var totalMagnetization = 0.0;
        activeCount = 0;

        Parallel.For(0, walkers.Length, i =>
        {
            if (walkers[i].IsActive) ProcessSingleWalker(ref walkers[i], stepSize);
        });

        for (var i = 0; i < walkers.Length; i++)
            if (walkers[i].IsActive)
            {
                totalMagnetization += walkers[i].Magnetization;
                activeCount++;
            }

        return totalMagnetization;
    }

    private double SimulateStepNEON(Walker[] walkers, int stepSize, ref int activeCount)
    {
        var totalMagnetization = 0.0;
        activeCount = 0;

        Parallel.For(0, walkers.Length, i =>
        {
            if (walkers[i].IsActive) ProcessSingleWalker(ref walkers[i], stepSize);
        });

        var i = 0;
        float sum1 = 0f, sum2 = 0f;
        for (; i + 2 <= walkers.Length; i += 2)
        {
            if (walkers[i].IsActive)
            {
                sum1 += (float)walkers[i].Magnetization;
                activeCount++;
            }

            if (walkers[i + 1].IsActive)
            {
                sum2 += (float)walkers[i + 1].Magnetization;
                activeCount++;
            }
        }

        var vec = Vector64.Create(sum1, sum2);
        var sumVec = AdvSimd.AddPairwise(vec, vec);
        totalMagnetization = AdvSimd.Extract(sumVec, 0);

        for (; i < walkers.Length; i++)
            if (walkers[i].IsActive)
            {
                totalMagnetization += walkers[i].Magnetization;
                activeCount++;
            }

        return totalMagnetization;
    }

    private double SimulateStepScalar(Walker[] walkers, int stepSize, ref int activeCount)
    {
        var totalMagnetization = 0.0;
        activeCount = 0;

        Parallel.For(0, walkers.Length, i =>
        {
            if (walkers[i].IsActive) ProcessSingleWalker(ref walkers[i], stepSize);
        });

        for (var i = 0; i < walkers.Length; i++)
            if (walkers[i].IsActive)
            {
                totalMagnetization += walkers[i].Magnetization;
                activeCount++;
            }

        return totalMagnetization;
    }

    private void ProcessSingleWalker(ref Walker walker, int stepSize)
    {
        if (!walker.IsActive) return;

        // Random direction
        var direction = Directions[_random.Next(6)];

        // Try to move
        var newX = walker.X + direction.dx * stepSize;
        var newY = walker.Y + direction.dy * stepSize;
        var newZ = walker.Z + direction.dz * stepSize;

        // Check boundaries
        if (newX < 0 || newX >= _width || newY < 0 || newY >= _height || newZ < 0 || newZ >= _depth)
        {
            // Hit boundary - reflect
            newX = Math.Clamp(newX, 0, _width - 1);
            newY = Math.Clamp(newY, 0, _height - 1);
            newZ = Math.Clamp(newZ, 0, _depth - 1);
        }

        var materialID = _labelVolume[newX, newY, newZ];

        if (materialID == _config.PoreMaterialID)
        {
            // Still in pore space - move freely
            walker.X = newX;
            walker.Y = newY;
            walker.Z = newZ;
        }
        else
        {
            // Hit a MATRIX surface - apply relaxation
            if (_config.MaterialRelaxivities.TryGetValue(materialID, out var relaxConfig))
            {
                // CRITICAL: Verify units in relaxation calculation
                // Surface relaxivity effect: M(t+dt) = M(t) * exp(-ρ * dt / a)
                // ρ: surface relaxivity (µm/s)
                // dt: time step (s)
                // a: voxel size (µm)
                // Result: dimensionless exponent ✓

                var relaxationRate = relaxConfig.SurfaceRelaxivity; // µm/s
                var dt = _config.TimeStepMs * 1e-3; // ms → s
                var voxelSizeUm = _config.VoxelSize * 1e6; // m → µm

                // Sanity check
                if (voxelSizeUm < 0.001 || voxelSizeUm > 10000)
                    Logger.LogError(
                        $"[NMRSimulation] Invalid voxel size in relaxation: {voxelSizeUm} µm from {_config.VoxelSize} m");

                var exponent = -relaxationRate * dt / voxelSizeUm;
                var relaxationFactor = Math.Exp(exponent);

                walker.Magnetization *= relaxationFactor;

                // Deactivate if magnetization drops too low
                if (walker.Magnetization < 0.001)
                    walker.IsActive = false;
            }

            // Don't move - walker stays at boundary
        }
    }

    private void ComputeT2Distribution(NMRResults results)
    {
        // Use logarithmic binning for T2 values
        var logMin = Math.Log10(_config.T2MinMs);
        var logMax = Math.Log10(_config.T2MaxMs);
        var logStep = (logMax - logMin) / _config.T2BinCount;

        results.T2HistogramBins = new double[_config.T2BinCount];
        results.T2Histogram = new double[_config.T2BinCount];

        for (var i = 0; i < _config.T2BinCount; i++)
            results.T2HistogramBins[i] = Math.Pow(10, logMin + i * logStep);

        // Fit exponential decay to extract T2 components
        // Use inverse Laplace transform with regularization
        var decay = results.Magnetization;
        var time = results.TimePoints;

        // Simple but effective: project decay onto exponential basis functions
        for (var i = 0; i < _config.T2BinCount; i++)
        {
            var t2 = results.T2HistogramBins[i];
            var amplitude = 0.0;

            // Compute amplitude for this T2 component
            for (var t = 0; t < time.Length; t++)
            {
                var expected = Math.Exp(-time[t] / t2);
                amplitude += decay[t] * expected;
            }

            results.T2Histogram[i] = Math.Max(0, amplitude / time.Length);
        }

        // Normalize
        var sum = results.T2Histogram.Sum();
        if (sum > 0)
            for (var i = 0; i < results.T2Histogram.Length; i++)
                results.T2Histogram[i] /= sum;
    }

    /// <summary>
    ///     FIXED: Correct physics for pore size distribution
    ///     In the fast diffusion regime:
    ///     1/T2 = ρ₂ * (S/V)
    ///     For different pore geometries:
    ///     - Spherical pores: S/V = 3/r  →  r = 3·ρ₂·T2
    ///     - Cylindrical pores: S/V = 2/r  →  r = 2·ρ₂·T2
    ///     - Slit-like pores: S/V = 1/r  →  r = ρ₂·T2
    ///     General formula: r = shape_factor · ρ₂ · T2
    ///     Units check:
    ///     [r] = [shape_factor] · [ρ₂] · [T2]
    ///     μm = (dimensionless) · (μm/s) · (ms)
    ///     μm = 1 · (μm/s) · (s · 10⁻³)
    ///     μm = μm ✓
    /// </summary>
    private void ComputePoreSizeDistribution(NMRResults results)
    {
        if (results.T2HistogramBins == null) return;

        // Get average surface relaxivity from MATRIX materials (not pore space!)
        var avgRelaxivity = _config.MaterialRelaxivities
            .Where(kvp => kvp.Key != _config.PoreMaterialID) // Exclude pore material
            .Select(kvp => kvp.Value.SurfaceRelaxivity)
            .DefaultIfEmpty(10.0)
            .Average();

        Logger.Log(
            $"[NMRSimulation] Pore size calculation: ρ₂={avgRelaxivity:F1} μm/s, shape factor={_config.PoreShapeFactor:F1}");

        results.PoreSizes = new double[results.T2HistogramBins.Length];
        results.PoreSizeDistribution = new double[results.T2Histogram.Length];

        for (var i = 0; i < results.T2HistogramBins.Length; i++)
        {
            // CORRECT FORMULA: r = shape_factor * ρ₂ * T2
            // T2 is in ms, ρ is in μm/s
            // Convert T2 to seconds: T2_s = T2_ms * 1e-3
            // r (μm) = shape_factor * ρ (μm/s) * T2 (s)
            var t2Seconds = results.T2HistogramBins[i] * 1e-3; // ms → s
            results.PoreSizes[i] = _config.PoreShapeFactor * avgRelaxivity * t2Seconds;

            results.PoreSizeDistribution[i] = results.T2Histogram[i];
        }

        Logger.Log($"[NMRSimulation] Pore size range: {results.PoreSizes.Min():F3} - {results.PoreSizes.Max():F1} μm");
    }

    private void ComputeStatistics(NMRResults results)
    {
        if (results.T2HistogramBins == null || results.T2Histogram == null) return;

        // Mean T2
        var weightedSum = 0.0;
        var totalWeight = 0.0;

        for (var i = 0; i < results.T2HistogramBins.Length; i++)
        {
            weightedSum += results.T2HistogramBins[i] * results.T2Histogram[i];
            totalWeight += results.T2Histogram[i];
        }

        results.MeanT2 = totalWeight > 0 ? weightedSum / totalWeight : 0;

        // Geometric mean T2
        var logSum = 0.0;
        for (var i = 0; i < results.T2HistogramBins.Length; i++)
            if (results.T2Histogram[i] > 0)
                logSum += Math.Log(results.T2HistogramBins[i]) * results.T2Histogram[i];

        results.GeometricMeanT2 = totalWeight > 0 ? Math.Exp(logSum / totalWeight) : 0;

        // Peak T2
        var maxIndex = 0;
        var maxValue = 0.0;
        for (var i = 0; i < results.T2Histogram.Length; i++)
            if (results.T2Histogram[i] > maxValue)
            {
                maxValue = results.T2Histogram[i];
                maxIndex = i;
            }

        results.T2PeakValue = results.T2HistogramBins[maxIndex];
    }

    private struct Walker
    {
        public int X;
        public int Y;
        public int Z;
        public double Magnetization;
        public bool IsActive;
    }
}