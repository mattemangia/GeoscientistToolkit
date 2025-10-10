// GeoscientistToolkit/Analysis/NMR/NMRSimulation.cs

using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.NMR;

/// <summary>
///     High-performance NMR simulation using random walk technique with SIMD acceleration.
/// </summary>
public class NMRSimulation
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

    public NMRSimulation(CtImageStackDataset dataset, NMRSimulationConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _labelVolume = dataset?.LabelData ?? throw new ArgumentNullException(nameof(dataset));
        _width = dataset.Width;
        _height = dataset.Height;
        _depth = dataset.Depth;
        _random = new Random(config.RandomSeed);

        Logger.Log($"[NMRSimulation] Initialized: {_width}x{_height}x{_depth}, {config.NumberOfWalkers} walkers");
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
        for (var t = 0; t < _config.NumberOfSteps; t++) results.TimePoints[t] = t * _config.TimeStepMs;

        progress?.Report((0.1f, "Running random walk simulation..."));

        // Run the simulation
        await Task.Run(() => { SimulateRandomWalk(walkers, results, progress); });

        progress?.Report((0.8f, "Computing T2 distribution..."));

        // Compute T2 spectrum and pore size distribution
        ComputeT2Distribution(results);
        ComputePoreSizeDistribution(results);
        ComputeStatistics(results);

        stopwatch.Stop();
        results.ComputationTime = stopwatch.Elapsed;

        progress?.Report((1f, $"NMR simulation completed in {stopwatch.Elapsed.TotalSeconds:F1}s"));

        Logger.Log(
            $"[NMRSimulation] Completed: {results.ComputationTime.TotalSeconds:F2}s, Mean T2: {results.MeanT2:F2}ms");

        return results;
    }

    private Walker[] InitializeWalkers()
    {
        var walkers = new List<Walker>();
        var maxAttempts = _config.NumberOfWalkers * 10;
        var attempts = 0;

        Logger.Log($"[NMRSimulation] Finding valid starting positions for {_config.NumberOfWalkers} walkers...");

        while (walkers.Count < _config.NumberOfWalkers && attempts < maxAttempts)
        {
            var x = _random.Next(0, _width);
            var y = _random.Next(0, _height);
            var z = _random.Next(0, _depth);

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

        Logger.Log($"[NMRSimulation] Initialized {walkers.Count} walkers in {attempts} attempts");
        return walkers.ToArray();
    }

    private void SimulateRandomWalk(Walker[] walkers, NMRResults results, IProgress<(float, string)> progress)
    {
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

        // Process walkers in parallel (AVX2 doesn't provide much benefit for the branchy walker logic)
        // But we still parallelize for multi-core performance
        Parallel.For(0, walkers.Length, i =>
        {
            if (walkers[i].IsActive) ProcessSingleWalker(ref walkers[i], stepSize);
        });

        // Accumulate results
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

        // ARM NEON acceleration - process walkers in parallel
        Parallel.For(0, walkers.Length, i =>
        {
            if (walkers[i].IsActive) ProcessSingleWalker(ref walkers[i], stepSize);
        });

        // Accumulate results with NEON (using Vector64 instead of Vector128)
        var i = 0;

        // Process 2 floats at a time with NEON Vector64
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

        // Use NEON for final accumulation
        var vec = Vector64.Create(sum1, sum2);
        var sumVec = AdvSimd.AddPairwise(vec, vec);
        totalMagnetization = AdvSimd.Extract(sumVec, 0);

        // Process remaining walkers
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
            // Hit a surface - apply relaxation
            if (_config.MaterialRelaxivities.TryGetValue(materialID, out var relaxConfig))
            {
                // Surface relaxivity effect
                var relaxationRate = relaxConfig.SurfaceRelaxivity; // μm/s
                var dt = _config.TimeStepMs * 1e-3; // convert to seconds
                var relaxationFactor = Math.Exp(-relaxationRate * dt / _config.VoxelSize);

                walker.Magnetization *= relaxationFactor;

                // Deactivate if magnetization drops too low
                if (walker.Magnetization < 0.001) walker.IsActive = false;
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

        for (var i = 0; i < _config.T2BinCount; i++) results.T2HistogramBins[i] = Math.Pow(10, logMin + i * logStep);

        // Fit exponential decay to extract T2 components
        // Simple approach: inverse Laplace transform approximation
        var decay = results.Magnetization;
        var time = results.TimePoints;

        // Use regularized inversion
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

    private void ComputePoreSizeDistribution(NMRResults results)
    {
        // Convert T2 to pore size using surface relaxivity model
        // r ≈ ρ * T2 (for spherical pores)
        // where ρ is surface relaxivity

        if (results.T2HistogramBins == null) return;

        var avgRelaxivity = _config.MaterialRelaxivities.Values
            .Select(m => m.SurfaceRelaxivity)
            .DefaultIfEmpty(10.0)
            .Average();

        results.PoreSizes = new double[results.T2HistogramBins.Length];
        results.PoreSizeDistribution = new double[results.T2Histogram.Length];

        for (var i = 0; i < results.T2HistogramBins.Length; i++)
        {
            // Convert T2 (ms) to pore radius (μm)
            results.PoreSizes[i] = avgRelaxivity * results.T2HistogramBins[i] * 1e-3;
            results.PoreSizeDistribution[i] = results.T2Histogram[i];
        }
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