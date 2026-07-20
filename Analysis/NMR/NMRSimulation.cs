// GAIA/Analysis/NMR/NMRSimulation.cs

using System.Diagnostics;
using GAIA.Data.CtImageStack;
using GAIA.Data.VolumeData;
using GAIA.Network;
using GAIA.Util;

namespace GAIA.Analysis.NMR;

/// <summary>
///     High-performance NMR simulation using the random walk technique.
///     Physics Background:
///     - T2 relaxation: 1/T2 = 1/T2_bulk + ρ₂ * (S/V)
///     - For fast diffusion regime: ρ₂ is surface relaxivity (μm/s)
///     - Pore radius from T2: r = shape_factor * ρ₂ * T2
///     - Shape factors: Sphere=3, Cylinder=2, Slit=1
///     Walkers hop exactly one voxel per sub-step; the physical duration of a hop is
///     dt_hop = a²/(6D) (Einstein relation), and each recorded time step executes as many
///     hops as fit in it. This keeps the effective diffusion coefficient equal to the
///     configured one and prevents walkers from tunnelling through matrix walls.
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

    // Guard against pathological hop counts (very high D or very coarse time steps)
    private const int MaxHopsPerStep = 500;

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

    public async Task<NMRResults> RunSimulationAsync(IProgress<(float progress, string message)> progress,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        progress?.Report((0f, "Initializing walkers..."));

        var (walkers, porosity) = InitializeWalkers();
        if (walkers.Length == 0)
            throw new InvalidOperationException(
                $"No valid starting positions found for pore material ID {_config.PoreMaterialID}");

        var results = new NMRResults(_config.NumberOfSteps)
        {
            NumberOfWalkers = walkers.Length,
            TotalSteps = _config.NumberOfSteps,
            TimeStep = _config.TimeStepMs,
            TotalPorosity = porosity,
            PoreMaterial = _config.MaterialRelaxivities.ContainsKey(_config.PoreMaterialID)
                ? _config.MaterialRelaxivities[_config.PoreMaterialID].MaterialName
                : "Unknown",
            MaterialRelaxivities = _config.MaterialRelaxivities.ToDictionary(
                kvp => kvp.Value.MaterialName,
                kvp => kvp.Value.SurfaceRelaxivity),
            ComputationMethod = "CPU (Parallel)"
        };

        // Time points
        for (var t = 0; t < _config.NumberOfSteps; t++)
            results.TimePoints[t] = t * _config.TimeStepMs;

        progress?.Report((0.1f, "Running random walk simulation..."));

        // Run the simulation
        await Task.Run(() => { SimulateRandomWalk(walkers, results, progress, cancellationToken); },
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report((0.8f, "Computing T2 distribution..."));

        // Compute T2 spectrum and pore size distribution
        ComputeT2Distribution(results);
        ComputePoreSizeDistribution(results);
        ComputeStatistics(results);

        // Optional: Compute T1-T2 map (estimated from the T2 spectrum, not simulated)
        if (_config.ComputeT1T2Map)
        {
            progress?.Report((0.95f, "Estimating T1-T2 correlation map..."));
            T1T2Computation.ComputeT1T2Map(results, _config);
        }

        stopwatch.Stop();
        results.ComputationTime = stopwatch.Elapsed;

        progress?.Report((1f, $"NMR simulation completed in {stopwatch.Elapsed.TotalSeconds:F1}s"));

        Logger.Log(
            $"[NMRSimulation] Completed: {results.ComputationTime.TotalSeconds:F2}s, Mean T2: {results.MeanT2:F2}ms");

        return results;
    }

    private (Walker[] walkers, double porosity) InitializeWalkers()
    {
        // Calculate material extent FIRST (like PNM does); also counts pore voxels for porosity
        var (xMin, xMax, yMin, yMax, zMin, zMax, poreVoxelCount) = CalculateMaterialBounds();

        var totalVoxels = (long)_width * _height * _depth;
        var porosity = totalVoxels > 0 ? poreVoxelCount / (double)totalVoxels : 0.0;

        if (xMin > xMax)
        {
            Logger.LogError($"[NMRSimulation] No voxels found for material ID {_config.PoreMaterialID}");
            return (Array.Empty<Walker>(), 0.0);
        }

        Logger.Log($"[NMRSimulation] Material extent: [{xMin},{xMax}] x [{yMin},{yMax}] x [{zMin},{zMax}]");
        Logger.Log($"[NMRSimulation] Pore material occupies {poreVoxelCount:N0} / {totalVoxels:N0} voxels " +
                   $"(porosity {porosity * 100:F2}% of total volume)");

        var walkers = new List<Walker>();
        var maxAttempts = _config.NumberOfWalkers * 10;
        var attempts = 0;

        Logger.Log($"[NMRSimulation] Finding valid starting positions for {_config.NumberOfWalkers} walkers...");

        // Search only within material bounds (MUCH more efficient!)
        while (walkers.Count < _config.NumberOfWalkers && attempts < maxAttempts)
        {
            var x = _random.Next(xMin, xMax + 1);
            var y = _random.Next(yMin, yMax + 1);
            var z = _random.Next(zMin, zMax + 1);

            // Only start walkers in PORE SPACE (not matrix)
            if (_labelVolume[x, y, z] == _config.PoreMaterialID)
            {
                // Per-walker RNG state: System.Random is not thread-safe, and the walk
                // runs walkers in parallel, so each walker carries its own xorshift state.
                uint rngState;
                do
                {
                    rngState = (uint)_random.Next(int.MinValue, int.MaxValue);
                } while (rngState == 0);

                walkers.Add(new Walker
                {
                    X = x,
                    Y = y,
                    Z = z,
                    Magnetization = 1.0,
                    IsActive = true,
                    RngState = rngState
                });
            }

            attempts++;
        }

        var efficiency = walkers.Count / (float)attempts * 100;
        Logger.Log(
            $"[NMRSimulation] Initialized {walkers.Count} walkers in {attempts} attempts ({efficiency:F1}% efficiency)");
        return (walkers.ToArray(), porosity);
    }

    /// <summary>
    ///     Calculate the bounding box of the selected material (same approach as PNM),
    ///     plus the pore voxel count used for porosity.
    /// </summary>
    private (int xMin, int xMax, int yMin, int yMax, int zMin, int zMax, long poreVoxels) CalculateMaterialBounds()
    {
        int xMin = _width, xMax = -1;
        int yMin = _height, yMax = -1;
        int zMin = _depth, zMax = -1;
        long poreVoxels = 0;

        for (var z = 0; z < _depth; z++)
        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
            if (_labelVolume[x, y, z] == _config.PoreMaterialID)
            {
                poreVoxels++;
                if (x < xMin) xMin = x;
                if (x > xMax) xMax = x;
                if (y < yMin) yMin = y;
                if (y > yMax) yMax = y;
                if (z < zMin) zMin = z;
                if (z > zMax) zMax = z;
            }

        return (xMin, xMax, yMin, yMax, zMin, zMax, poreVoxels);
    }

    private void SimulateRandomWalk(Walker[] walkers, NMRResults results,
        IProgress<(float, string)> progress, CancellationToken cancellationToken)
    {
        // Einstein relation: a hop of one voxel (length a) takes dt_hop = a²/(6D).
        // Each recorded step of duration dt executes round(dt/dt_hop) hops (cumulative
        // rounding so the average rate is exact even when dt < dt_hop).
        var dtStepSec = _config.TimeStepMs * 1e-3;
        var dtHopSec = _config.VoxelSize * _config.VoxelSize / (6.0 * _config.DiffusionCoefficient);
        var hopsPerStep = dtStepSec / dtHopSec;

        Logger.Log($"[NMRSimulation] Hop time: {dtHopSec:E3} s ({hopsPerStep:F2} hops per {_config.TimeStepMs} ms step)");

        if (hopsPerStep > MaxHopsPerStep)
            Logger.LogWarning(
                $"[NMRSimulation] Time step requires {hopsPerStep:F0} voxel hops per step (capped at {MaxHopsPerStep}). " +
                "Diffusion will be underestimated; reduce the time step or use a finer voxel size.");

        // Precompute per-material relaxation factor for one hop:
        // M(t+dt_hop) = M(t) * exp(-ρ * dt_hop / a), ρ in µm/s, a in µm → dimensionless exponent.
        var voxelSizeUm = _config.VoxelSize * 1e6;
        var relaxationFactors = new double[256];
        Array.Fill(relaxationFactors, 1.0);
        foreach (var kvp in _config.MaterialRelaxivities)
            relaxationFactors[kvp.Key] = Math.Exp(-kvp.Value.SurfaceRelaxivity * dtHopSec / voxelSizeUm);

        long hopsDone = 0;

        for (var step = 0; step < _config.NumberOfSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (step % 100 == 0)
            {
                var progressPercent = 0.1f + 0.7f * (step / (float)_config.NumberOfSteps);
                progress?.Report((progressPercent, $"Simulating step {step}/{_config.NumberOfSteps}..."));
            }

            var hopsTarget = (long)Math.Round((step + 1) * dtStepSec / dtHopSec);
            var hopsThisStep = (int)Math.Min(MaxHopsPerStep, Math.Max(0, hopsTarget - hopsDone));
            hopsDone += hopsThisStep;

            if (hopsThisStep > 0)
                Parallel.For(0, walkers.Length, i =>
                {
                    if (walkers[i].IsActive)
                        ProcessWalkerHops(ref walkers[i], hopsThisStep, relaxationFactors);
                });

            var totalMagnetization = 0.0;
            for (var i = 0; i < walkers.Length; i++)
                if (walkers[i].IsActive)
                    totalMagnetization += walkers[i].Magnetization;

            results.Magnetization[step] = totalMagnetization / walkers.Length;
        }
    }

    private void ProcessWalkerHops(ref Walker walker, int hops, double[] relaxationFactors)
    {
        for (var h = 0; h < hops; h++)
        {
            var direction = Directions[NextRandom(ref walker.RngState) % 6];

            var newX = walker.X + direction.dx;
            var newY = walker.Y + direction.dy;
            var newZ = walker.Z + direction.dz;

            // Reflect at volume boundary
            newX = Math.Clamp(newX, 0, _width - 1);
            newY = Math.Clamp(newY, 0, _height - 1);
            newZ = Math.Clamp(newZ, 0, _depth - 1);

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
                // Hit a MATRIX surface - apply surface relaxation, walker stays put
                walker.Magnetization *= relaxationFactors[materialID];

                if (walker.Magnetization < 0.001)
                {
                    walker.IsActive = false;
                    return;
                }
            }
        }
    }

    private static uint NextRandom(ref uint state)
    {
        // xorshift32 — cheap, decent-quality PRNG with per-walker state (thread-safe)
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private void ComputeT2Distribution(NMRResults results)
    {
        var (bins, amplitudes) = T2Inversion.Invert(
            results.TimePoints, results.Magnetization,
            _config.T2MinMs, _config.T2MaxMs, _config.T2BinCount);

        results.T2HistogramBins = bins;
        results.T2Histogram = amplitudes;
    }

    /// <summary>
    ///     Pore size distribution from the T2 spectrum.
    ///     In the fast diffusion regime:
    ///     1/T2 = ρ₂ * (S/V)
    ///     For different pore geometries:
    ///     - Spherical pores: S/V = 3/r  →  r = 3·ρ₂·T2
    ///     - Cylindrical pores: S/V = 2/r  →  r = 2·ρ₂·T2
    ///     - Slit-like pores: S/V = 1/r  →  r = ρ₂·T2
    ///     General formula: r = shape_factor · ρ₂ · T2
    ///     Units check:
    ///     [r] = [shape_factor] · [ρ₂] · [T2]
    ///     μm = (dimensionless) · (μm/s) · (s)  ✓
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
            // r = shape_factor * ρ₂ * T2, with T2 converted ms → s
            var t2Seconds = results.T2HistogramBins[i] * 1e-3;
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
        public uint RngState;
    }
}
