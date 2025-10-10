// GeoscientistToolkit/Analysis/NMR/DiffusionEditing.cs

using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.NMR;

/// <summary>
///     Implements diffusion editing sequences for NMR to separate fluids based on diffusion coefficients.
///     Diffusion editing helps distinguish bound vs. free fluids and identify fluid types.
/// </summary>
public class DiffusionEditing
{
    /// <summary>
    ///     Runs a diffusion editing sequence with multiple b-values
    /// </summary>
    public static async Task<DiffusionResults> RunDiffusionSequenceAsync(
        CtImageStackDataset dataset,
        NMRSimulationConfig baseConfig,
        DiffusionSequenceConfig diffusionConfig,
        IProgress<(float, string)> progress)
    {
        Logger.Log("[DiffusionEditing] Starting diffusion editing sequence...");

        var results = new DiffusionResults
        {
            BValues = diffusionConfig.BValues,
            DecayCurves = new double[diffusionConfig.BValues.Length][],
            T2Distributions = new double[diffusionConfig.BValues.Length][]
        };

        // Run simulation for each b-value
        for (var i = 0; i < diffusionConfig.BValues.Length; i++)
        {
            var bValue = diffusionConfig.BValues[i];
            var progressPercent = i / (float)diffusionConfig.BValues.Length;
            progress?.Report((progressPercent, $"Simulating b = {bValue} s/mm²..."));

            // Modify config for this b-value
            var config = CloneConfig(baseConfig);

            // Apply diffusion weighting: Signal attenuation = exp(-b * D)
            // Adjust time step to account for diffusion encoding
            config.TimeStepMs = diffusionConfig.EchoSpacing;
            config.NumberOfSteps = diffusionConfig.EchoCount;

            // Calculate effective diffusion coefficient accounting for gradients
            var diffusionAttenuation = Math.Exp(-bValue * config.DiffusionCoefficient * 1e6); // Convert to s/mm²

            // Run simulation
            NMRResults nmrResult;
            if (config.UseOpenCL)
            {
                // GPU path: Convert the callback-based method to an awaitable Task
                var tcs = new TaskCompletionSource<NMRResults>();
                Action<NMRResults> onSuccess = result => tcs.TrySetResult(result);
                Action<Exception> onError = ex => tcs.TrySetException(ex);

                using var simulation = new NMRSimulationOpenCL(dataset, config);
                // The original code passed 'null' for progress, so we do the same here.
                simulation.RunSimulationAsync(null, onSuccess, onError);

                // Await the task that will be completed by one of the callbacks.
                nmrResult = await tcs.Task;
            }
            else
            {
                var simulation = new NMRSimulation(dataset, config);
                nmrResult = await simulation.RunSimulationAsync(null);
            }

            // Apply diffusion weighting to decay curve
            results.DecayCurves[i] = new double[nmrResult.Magnetization.Length];
            for (var j = 0; j < nmrResult.Magnetization.Length; j++)
                results.DecayCurves[i][j] = nmrResult.Magnetization[j] *
                                            Math.Exp(-bValue * config.DiffusionCoefficient * 1e6 *
                                                     nmrResult.TimePoints[j] * 1e-3);

            // Store T2 distribution
            results.T2Distributions[i] = nmrResult.T2Histogram;
            results.T2Bins = nmrResult.T2HistogramBins;
        }

        progress?.Report((0.9f, "Computing apparent diffusion coefficients..."));

        // Compute ADC for each T2 component
        ComputeApparentDiffusionCoefficients(results);

        // Separate bound vs. free fluids
        SeparateFluidComponents(results);

        progress?.Report((1.0f, "Diffusion editing completed"));

        Logger.Log($"[DiffusionEditing] Completed. Mean ADC: {results.MeanADC:E2} m²/s");
        Logger.Log(
            $"[DiffusionEditing] Free fluid: {results.FastDiffusingFraction:P1}, Bound fluid: {results.SlowDiffusingFraction:P1}");

        return results;
    }

    private static void ComputeApparentDiffusionCoefficients(DiffusionResults results)
    {
        var t2BinCount = results.T2Bins.Length;
        results.ApparentDiffusionCoefficients = new double[t2BinCount];

        // For each T2 bin, compute ADC from signal vs. b-value
        for (var t2Idx = 0; t2Idx < t2BinCount; t2Idx++)
        {
            var signals = new List<double>();
            var bValues = new List<double>();

            for (var bIdx = 0; bIdx < results.BValues.Length; bIdx++)
            {
                var signal = results.T2Distributions[bIdx][t2Idx];
                if (signal > 1e-6)
                {
                    signals.Add(Math.Log(signal));
                    bValues.Add(results.BValues[bIdx]);
                }
            }

            if (signals.Count >= 2)
            {
                // Linear fit: ln(S) = ln(S0) - b*ADC
                var adc = FitADC(bValues.ToArray(), signals.ToArray());
                results.ApparentDiffusionCoefficients[t2Idx] = adc;
            }
        }

        // Compute mean ADC weighted by T2 distribution
        var totalWeight = 0.0;
        var weightedADC = 0.0;
        for (var i = 0; i < t2BinCount; i++)
        {
            var weight = results.T2Distributions[0][i]; // Use b=0 distribution
            totalWeight += weight;
            weightedADC += results.ApparentDiffusionCoefficients[i] * weight;
        }

        results.MeanADC = totalWeight > 0 ? weightedADC / totalWeight : 0;
    }

    private static double FitADC(double[] bValues, double[] logSignals)
    {
        // Linear least squares fit: ln(S) = a - b*ADC
        var n = bValues.Length;
        var sumB = bValues.Sum();
        var sumLogS = logSignals.Sum();
        var sumB2 = bValues.Sum(b => b * b);
        var sumBLogS = 0.0;
        for (var i = 0; i < n; i++) sumBLogS += bValues[i] * logSignals[i];

        var slope = (n * sumBLogS - sumB * sumLogS) / (n * sumB2 - sumB * sumB);
        return Math.Abs(slope) * 1e-6; // Convert from mm²/s to m²/s
    }

    private static void SeparateFluidComponents(DiffusionResults results)
    {
        // Threshold for separating bound (slow) vs. free (fast) fluids
        var adcThreshold = 1e-9; // 1 μm²/ms = 1e-9 m²/s

        results.BoundFluidFraction = new double[results.T2Bins.Length];
        results.FreeFluidFraction = new double[results.T2Bins.Length];

        var totalBound = 0.0;
        var totalFree = 0.0;

        for (var i = 0; i < results.T2Bins.Length; i++)
        {
            var adc = results.ApparentDiffusionCoefficients[i];
            var amplitude = results.T2Distributions[0][i]; // Use b=0

            if (adc < adcThreshold)
            {
                results.BoundFluidFraction[i] = amplitude;
                totalBound += amplitude;
            }
            else
            {
                results.FreeFluidFraction[i] = amplitude;
                totalFree += amplitude;
            }
        }

        var totalFluid = totalBound + totalFree;
        results.SlowDiffusingFraction = totalFluid > 0 ? totalBound / totalFluid : 0;
        results.FastDiffusingFraction = totalFluid > 0 ? totalFree / totalFluid : 0;
    }

    private static NMRSimulationConfig CloneConfig(NMRSimulationConfig source)
    {
        return new NMRSimulationConfig
        {
            NumberOfWalkers = source.NumberOfWalkers,
            NumberOfSteps = source.NumberOfSteps,
            TimeStepMs = source.TimeStepMs,
            DiffusionCoefficient = source.DiffusionCoefficient,
            VoxelSize = source.VoxelSize,
            PoreMaterialID = source.PoreMaterialID,
            MaterialRelaxivities = new Dictionary<byte, MaterialRelaxivityConfig>(source.MaterialRelaxivities),
            T2BinCount = source.T2BinCount,
            T2MinMs = source.T2MinMs,
            T2MaxMs = source.T2MaxMs,
            RandomSeed = source.RandomSeed,
            UseOpenCL = source.UseOpenCL,
            ComputeT1T2Map = false // Disable for diffusion sequences
        };
    }

    /// <summary>
    ///     Exports diffusion editing results to CSV
    /// </summary>
    public static void ExportDiffusionResults(DiffusionResults results, string filePath)
    {
        using var writer = new StreamWriter(filePath);

        // Write header
        writer.WriteLine("# Diffusion Editing NMR Results");
        writer.WriteLine($"# Mean ADC: {results.MeanADC:E6} m²/s");
        writer.WriteLine($"# Free fluid fraction: {results.FastDiffusingFraction:P2}");
        writer.WriteLine($"# Bound fluid fraction: {results.SlowDiffusingFraction:P2}");
        writer.WriteLine();

        // Write T2 bins and ADC
        writer.WriteLine("T2_ms,ADC_m2_per_s,Bound_Fraction,Free_Fraction");
        for (var i = 0; i < results.T2Bins.Length; i++)
            writer.WriteLine($"{results.T2Bins[i]:F3},{results.ApparentDiffusionCoefficients[i]:E6}," +
                             $"{results.BoundFluidFraction[i]:F6},{results.FreeFluidFraction[i]:F6}");

        writer.WriteLine();

        // Write decay curves for each b-value
        writer.Write("Time_ms");
        foreach (var b in results.BValues) writer.Write($",Signal_b{b}");
        writer.WriteLine();

        var maxLength = results.DecayCurves.Max(d => d.Length);
        for (var i = 0; i < maxLength; i++)
        {
            writer.Write($"{i * 0.5:F2}"); // Assuming 0.5 ms echo spacing
            foreach (var decay in results.DecayCurves)
                if (i < decay.Length)
                    writer.Write($",{decay[i]:E6}");
                else
                    writer.Write(",");

            writer.WriteLine();
        }

        Logger.Log($"[DiffusionEditing] Results exported to {filePath}");
    }

    /// <summary>
    ///     Configuration for diffusion-weighted NMR sequence
    /// </summary>
    public class DiffusionSequenceConfig
    {
        public double[] BValues { get; set; } = { 0, 10, 50, 100, 500, 1000 }; // s/mm²
        public double GradientDuration { get; set; } = 1.0; // ms
        public double GradientSeparation { get; set; } = 5.0; // ms (Δ)
        public int EchoCount { get; set; } = 32;
        public double EchoSpacing { get; set; } = 0.5; // ms
    }

    /// <summary>
    ///     Results from diffusion editing sequence
    /// </summary>
    public class DiffusionResults
    {
        public double[] BValues { get; set; }
        public double[][] DecayCurves { get; set; } // [b-value index][time index]
        public double[][] T2Distributions { get; set; } // [b-value index][T2 bin]
        public double[] ApparentDiffusionCoefficients { get; set; } // ADC per T2 bin
        public double[] T2Bins { get; set; }

        // Component separation
        public double[] BoundFluidFraction { get; set; }
        public double[] FreeFluidFraction { get; set; }
        public double MeanADC { get; set; }
        public double FastDiffusingFraction { get; set; } // Free fluid
        public double SlowDiffusingFraction { get; set; } // Bound fluid
    }
}