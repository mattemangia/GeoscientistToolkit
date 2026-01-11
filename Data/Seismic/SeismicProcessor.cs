// GeoscientistToolkit/Data/Seismic/SeismicProcessor.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Seismic;

/// <summary>
/// Advanced seismic data processor for cleaning and preparing seismic lines
/// for 3D cube construction and geological interpretation.
/// Includes: noise removal, data correction, velocity analysis, stacking, and migration.
/// </summary>
public static class SeismicProcessor
{
    #region Noise Removal

    /// <summary>
    /// Apply noise removal using various methods
    /// </summary>
    public static void RemoveNoise(SeismicDataset dataset, NoiseRemovalMethod method, NoiseRemovalSettings settings,
        IProgress<(float progress, string message)>? progress = null)
    {
        var traces = dataset.SegyData?.Traces;
        if (traces == null || traces.Count == 0) return;

        progress?.Report((0.0f, $"Removing noise using {method}..."));

        switch (method)
        {
            case NoiseRemovalMethod.MedianFilter:
                ApplyMedianFilter(traces, settings.FilterWindowSize, progress);
                break;
            case NoiseRemovalMethod.FKFilter:
                ApplyFKFilter(traces, settings, progress);
                break;
            case NoiseRemovalMethod.SingularValueDecomposition:
                ApplySVDFilter(traces, settings.SVDComponents, progress);
                break;
            case NoiseRemovalMethod.WaveletDenoising:
                ApplyWaveletDenoising(traces, settings.WaveletThreshold, progress);
                break;
            case NoiseRemovalMethod.AdaptiveSubtraction:
                ApplyAdaptiveSubtraction(traces, settings, progress);
                break;
            case NoiseRemovalMethod.SpikeDeconvolution:
                ApplySpikeDeconvolution(traces, settings.SpikeThreshold, progress);
                break;
        }

        RecalculateStatistics(dataset);
        progress?.Report((1.0f, "Noise removal complete!"));
        Logger.Log($"[SeismicProcessor] Applied {method} noise removal");
    }

    private static void ApplyMedianFilter(List<SegyTrace> traces, int windowSize, IProgress<(float progress, string message)>? progress)
    {
        int halfWindow = windowSize / 2;

        Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            var filtered = new float[samples.Length];

            for (int j = 0; j < samples.Length; j++)
            {
                var windowSamples = new List<float>();
                for (int k = Math.Max(0, j - halfWindow); k <= Math.Min(samples.Length - 1, j + halfWindow); k++)
                {
                    windowSamples.Add(samples[k]);
                }
                windowSamples.Sort();
                filtered[j] = windowSamples[windowSamples.Count / 2];
            }

            // Subtract median to get denoised signal (residual = signal - median_noise)
            for (int j = 0; j < samples.Length; j++)
            {
                samples[j] = samples[j] - (filtered[j] - samples[j]) * 0.5f; // Partial subtraction
            }
        });
    }

    private static void ApplyFKFilter(List<SegyTrace> traces, NoiseRemovalSettings settings, IProgress<(float progress, string message)>? progress)
    {
        // F-K (Frequency-Wavenumber) filter for coherent noise removal
        // This is a simplified implementation - full implementation would use 2D FFT

        int numTraces = traces.Count;
        int numSamples = traces[0].Samples.Length;

        progress?.Report((0.2f, "Computing F-K transform..."));

        // Create 2D array
        var data = new float[numTraces, numSamples];
        for (int i = 0; i < numTraces; i++)
        {
            for (int j = 0; j < numSamples; j++)
            {
                data[i, j] = traces[i].Samples[j];
            }
        }

        // Apply 2D FK filter (simplified via row/column processing)
        // Apply along time axis (columns)
        for (int i = 0; i < numTraces; i++)
        {
            var column = new float[numSamples];
            for (int j = 0; j < numSamples; j++) column[j] = data[i, j];

            var filtered = ApplyBandpassFFT(column, settings.FKLowCutVelocity, settings.FKHighCutVelocity);

            for (int j = 0; j < numSamples; j++) data[i, j] = filtered[j];
        }

        progress?.Report((0.6f, "Applying FK filter..."));

        // Apply along space axis (rows) to remove linear noise
        for (int j = 0; j < numSamples; j++)
        {
            var row = new float[numTraces];
            for (int i = 0; i < numTraces; i++) row[i] = data[i, j];

            // Simple spatial filter for linear event removal
            var filtered = ApplySpatialFilter(row, settings.FKRejectSlope);

            for (int i = 0; i < numTraces; i++) data[i, j] = filtered[i];
        }

        // Copy back to traces
        for (int i = 0; i < numTraces; i++)
        {
            for (int j = 0; j < numSamples; j++)
            {
                traces[i].Samples[j] = data[i, j];
            }
        }
    }

    private static void ApplySVDFilter(List<SegyTrace> traces, int componentsToKeep, IProgress<(float progress, string message)>? progress)
    {
        // Singular Value Decomposition filtering for random noise attenuation
        int numTraces = traces.Count;
        int numSamples = traces[0].Samples.Length;

        progress?.Report((0.2f, "Computing SVD..."));

        // Build data matrix
        var matrix = new double[numTraces, numSamples];
        for (int i = 0; i < numTraces; i++)
        {
            for (int j = 0; j < numSamples; j++)
            {
                matrix[i, j] = traces[i].Samples[j];
            }
        }

        // Simplified SVD via power iteration (for computational efficiency)
        var reconstructed = ComputeTruncatedSVDReconstruction(matrix, componentsToKeep, progress);

        // Copy back
        for (int i = 0; i < numTraces; i++)
        {
            for (int j = 0; j < numSamples; j++)
            {
                traces[i].Samples[j] = (float)reconstructed[i, j];
            }
        }
    }

    private static void ApplyWaveletDenoising(List<SegyTrace> traces, float threshold, IProgress<(float progress, string message)>? progress)
    {
        // Wavelet denoising using Haar wavelet (simple implementation)
        Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            var length = samples.Length;

            // Ensure power of 2 length
            int padLength = (int)Math.Pow(2, Math.Ceiling(Math.Log2(length)));
            var padded = new float[padLength];
            Array.Copy(samples, padded, length);

            // Forward Haar transform
            HaarTransform(padded, true);

            // Soft thresholding
            for (int j = 0; j < padded.Length; j++)
            {
                if (Math.Abs(padded[j]) < threshold)
                    padded[j] = 0;
                else
                    padded[j] = Math.Sign(padded[j]) * (Math.Abs(padded[j]) - threshold);
            }

            // Inverse Haar transform
            HaarTransform(padded, false);

            // Copy back
            Array.Copy(padded, samples, length);
        });
    }

    private static void ApplyAdaptiveSubtraction(List<SegyTrace> traces, NoiseRemovalSettings settings, IProgress<(float progress, string message)>? progress)
    {
        // Adaptive noise subtraction using reference traces
        int windowSize = settings.AdaptiveWindowSize;

        // Estimate noise model from edge traces or user-specified reference
        var noiseModel = EstimateNoiseModel(traces, settings.AdaptiveReferenceTraces);

        Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            var adaptedNoise = AdaptNoiseToTrace(noiseModel, samples, windowSize);

            for (int j = 0; j < samples.Length; j++)
            {
                samples[j] -= adaptedNoise[j];
            }
        });
    }

    private static void ApplySpikeDeconvolution(List<SegyTrace> traces, float threshold, IProgress<(float progress, string message)>? progress)
    {
        // Remove spike noise (anomalous high-amplitude samples)
        Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            var rms = (float)Math.Sqrt(samples.Sum(s => s * s) / samples.Length);
            var spikeThreshold = rms * threshold;

            for (int j = 1; j < samples.Length - 1; j++)
            {
                if (Math.Abs(samples[j]) > spikeThreshold)
                {
                    // Replace spike with interpolated value
                    samples[j] = (samples[j - 1] + samples[j + 1]) / 2f;
                }
            }
        });
    }

    #endregion

    #region Data Correction

    /// <summary>
    /// Apply various data corrections to seismic traces
    /// </summary>
    public static void ApplyDataCorrections(SeismicDataset dataset, DataCorrectionSettings settings,
        IProgress<(float progress, string message)>? progress = null)
    {
        var traces = dataset.SegyData?.Traces;
        if (traces == null || traces.Count == 0) return;

        float progressStep = 0.0f;
        int totalSteps = 0;
        if (settings.ApplyStaticCorrection) totalSteps++;
        if (settings.ApplySphericalDivergence) totalSteps++;
        if (settings.ApplyAttenuation) totalSteps++;
        if (settings.ApplyGeometricSpreading) totalSteps++;
        if (settings.ApplySourceWaveletDecon) totalSteps++;
        if (settings.ApplyPolarityCorrection) totalSteps++;

        progressStep = totalSteps > 0 ? 1.0f / totalSteps : 1.0f;
        float currentProgress = 0.0f;

        if (settings.ApplyStaticCorrection)
        {
            progress?.Report((currentProgress, "Applying static corrections..."));
            ApplyStaticCorrections(traces, settings);
            currentProgress += progressStep;
        }

        if (settings.ApplySphericalDivergence)
        {
            progress?.Report((currentProgress, "Compensating spherical divergence..."));
            ApplySphericalDivergenceCorrection(traces, settings);
            currentProgress += progressStep;
        }

        if (settings.ApplyAttenuation)
        {
            progress?.Report((currentProgress, "Applying attenuation compensation..."));
            ApplyAttenuationCompensation(traces, settings);
            currentProgress += progressStep;
        }

        if (settings.ApplyGeometricSpreading)
        {
            progress?.Report((currentProgress, "Correcting geometric spreading..."));
            ApplyGeometricSpreadingCorrection(traces, settings);
            currentProgress += progressStep;
        }

        if (settings.ApplySourceWaveletDecon)
        {
            progress?.Report((currentProgress, "Applying source wavelet deconvolution..."));
            ApplySourceWaveletDeconvolution(traces, settings);
            currentProgress += progressStep;
        }

        if (settings.ApplyPolarityCorrection)
        {
            progress?.Report((currentProgress, "Correcting trace polarity..."));
            ApplyPolarityCorrection(traces, settings);
            currentProgress += progressStep;
        }

        RecalculateStatistics(dataset);
        progress?.Report((1.0f, "Data corrections complete!"));
        Logger.Log("[SeismicProcessor] Applied data corrections");
    }

    private static void ApplyStaticCorrections(List<SegyTrace> traces, DataCorrectionSettings settings)
    {
        // Static corrections to align traces (datum correction)
        var sampleInterval = settings.SampleIntervalMs;

        Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            var staticShift = settings.StaticShiftMs;

            // Apply source/receiver statics if available
            if (settings.UseTraceStatics && settings.TraceStatics.TryGetValue(i, out var traceStatic))
            {
                staticShift += traceStatic;
            }

            var shiftSamples = (int)(staticShift / sampleInterval);
            if (shiftSamples == 0) return;

            var corrected = new float[samples.Length];
            for (int j = 0; j < samples.Length; j++)
            {
                int sourceIdx = j - shiftSamples;
                if (sourceIdx >= 0 && sourceIdx < samples.Length)
                    corrected[j] = samples[sourceIdx];
            }
            Array.Copy(corrected, samples, samples.Length);
        });
    }

    private static void ApplySphericalDivergenceCorrection(List<SegyTrace> traces, DataCorrectionSettings settings)
    {
        // Correct for amplitude decay due to wavefront spreading
        var sampleInterval = settings.SampleIntervalMs / 1000.0f; // Convert to seconds
        var velocity = settings.AverageVelocity;

        Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            for (int j = 0; j < samples.Length; j++)
            {
                var time = j * sampleInterval;
                if (time > 0.001f) // Avoid division by zero
                {
                    var depth = velocity * time / 2; // Two-way time
                    var correction = (float)(depth * depth); // r^2 correction
                    samples[j] *= correction * settings.SphericalDivergenceGain;
                }
            }
        });
    }

    private static void ApplyAttenuationCompensation(List<SegyTrace> traces, DataCorrectionSettings settings)
    {
        // Q-compensation (inverse Q filtering) for frequency-dependent attenuation
        var sampleInterval = settings.SampleIntervalMs / 1000.0f;
        var qFactor = settings.QFactor;

        Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            for (int j = 0; j < samples.Length; j++)
            {
                var time = j * sampleInterval;
                // Simplified exponential compensation
                var compensation = (float)Math.Exp(Math.PI * settings.DominantFrequency * time / qFactor);
                samples[j] *= Math.Min(compensation, settings.MaxAttenuationGain);
            }
        });
    }

    private static void ApplyGeometricSpreadingCorrection(List<SegyTrace> traces, DataCorrectionSettings settings)
    {
        // t^n amplitude correction where n is typically 1 to 2
        var sampleInterval = settings.SampleIntervalMs / 1000.0f;
        var exponent = settings.GeometricSpreadingExponent;

        Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            for (int j = 0; j < samples.Length; j++)
            {
                var time = Math.Max(0.001f, j * sampleInterval);
                var correction = (float)Math.Pow(time, exponent);
                samples[j] *= correction;
            }
        });
    }

    private static void ApplySourceWaveletDeconvolution(List<SegyTrace> traces, DataCorrectionSettings settings)
    {
        // Spiking deconvolution to compress the source wavelet
        int filterLength = settings.DeconvolutionFilterLength;
        float prewhitening = settings.DeconvolutionPrewhitening;

        Parallel.For(0, traces.Count, i =>
        {
            var samples = traces[i].Samples;
            var deconvolved = WienerDeconvolution(samples, filterLength, prewhitening);
            Array.Copy(deconvolved, samples, samples.Length);
        });
    }

    private static void ApplyPolarityCorrection(List<SegyTrace> traces, DataCorrectionSettings settings)
    {
        // Correct polarity reversals based on correlation with reference
        if (settings.ReferenceTrace < 0 || settings.ReferenceTrace >= traces.Count)
            return;

        var reference = traces[settings.ReferenceTrace].Samples;

        Parallel.For(0, traces.Count, i =>
        {
            if (i == settings.ReferenceTrace) return;

            var samples = traces[i].Samples;
            var correlation = ComputeCorrelation(samples, reference);

            // If correlation is negative, flip polarity
            if (correlation < -settings.PolarityCorrectionThreshold)
            {
                for (int j = 0; j < samples.Length; j++)
                    samples[j] = -samples[j];
            }
        });
    }

    #endregion

    #region Velocity Analysis

    /// <summary>
    /// Perform velocity analysis on seismic gather
    /// </summary>
    public static VelocityAnalysisResult AnalyzeVelocity(SeismicDataset dataset, VelocityAnalysisSettings settings,
        IProgress<(float progress, string message)>? progress = null)
    {
        var traces = dataset.SegyData?.Traces;
        if (traces == null || traces.Count == 0)
            return new VelocityAnalysisResult();

        progress?.Report((0.0f, "Starting velocity analysis..."));

        var result = new VelocityAnalysisResult
        {
            TimeRange = (0, dataset.GetDurationSeconds() * 1000),
            VelocityRange = (settings.MinVelocity, settings.MaxVelocity)
        };

        var numSamples = dataset.GetSampleCount();
        var sampleInterval = dataset.GetSampleIntervalMs();

        // Generate velocity scan
        int numVelocities = settings.VelocityScanSteps;
        int numTimes = settings.TimeScanSteps;
        float velocityStep = (settings.MaxVelocity - settings.MinVelocity) / (numVelocities - 1);
        float timeStep = (numSamples * sampleInterval) / (numTimes - 1);

        result.Semblance = new float[numTimes, numVelocities];
        result.TimeAxis = new float[numTimes];
        result.VelocityAxis = new float[numVelocities];

        // Initialize axes
        for (int t = 0; t < numTimes; t++)
            result.TimeAxis[t] = t * timeStep;
        for (int v = 0; v < numVelocities; v++)
            result.VelocityAxis[v] = settings.MinVelocity + v * velocityStep;

        progress?.Report((0.1f, "Computing velocity semblance..."));

        // Compute semblance for each (time, velocity) pair
        for (int tIdx = 0; tIdx < numTimes; tIdx++)
        {
            float t0 = result.TimeAxis[tIdx];

            for (int vIdx = 0; vIdx < numVelocities; vIdx++)
            {
                float velocity = result.VelocityAxis[vIdx];
                result.Semblance[tIdx, vIdx] = ComputeSemblance(traces, t0, velocity, settings, sampleInterval);
            }

            progress?.Report((0.1f + 0.7f * tIdx / numTimes, $"Velocity scan: {tIdx + 1}/{numTimes}"));
        }

        // Pick velocity function
        progress?.Report((0.8f, "Picking velocity function..."));
        result.PickedVelocityFunction = PickVelocityFunction(result.Semblance, result.TimeAxis, result.VelocityAxis, settings);

        // Compute interval velocities
        progress?.Report((0.9f, "Computing interval velocities..."));
        result.IntervalVelocities = ComputeIntervalVelocities(result.PickedVelocityFunction);

        progress?.Report((1.0f, "Velocity analysis complete!"));
        Logger.Log("[SeismicProcessor] Velocity analysis complete");

        return result;
    }

    private static float ComputeSemblance(List<SegyTrace> traces, float t0, float velocity,
        VelocityAnalysisSettings settings, float sampleInterval)
    {
        // Semblance = (sum of stacked amplitudes)^2 / (N * sum of squared amplitudes)
        int windowSamples = (int)(settings.SemblanceWindowMs / sampleInterval);
        int halfWindow = windowSamples / 2;
        int t0Sample = (int)(t0 / sampleInterval);

        double sumStacked = 0;
        double sumSquared = 0;
        int count = 0;

        for (int w = -halfWindow; w <= halfWindow; w++)
        {
            int sampleIdx = t0Sample + w;
            if (sampleIdx < 0) continue;

            double stackedAmplitude = 0;
            double squaredSum = 0;

            for (int traceIdx = 0; traceIdx < traces.Count; traceIdx++)
            {
                // Get offset (simplified - using trace index as proxy for offset)
                float offset = traceIdx * settings.TraceSpacing;

                // NMO correction: t = sqrt(t0^2 + (x/v)^2)
                float t = (float)Math.Sqrt(t0 * t0 + (offset * offset) / (velocity * velocity));
                int nmoCorrectedSample = (int)(t / sampleInterval);

                if (nmoCorrectedSample >= 0 && nmoCorrectedSample < traces[traceIdx].Samples.Length)
                {
                    var amp = traces[traceIdx].Samples[nmoCorrectedSample];
                    stackedAmplitude += amp;
                    squaredSum += amp * amp;
                    count++;
                }
            }

            sumStacked += stackedAmplitude * stackedAmplitude;
            sumSquared += squaredSum;
        }

        if (count == 0 || sumSquared < 1e-10) return 0;
        return (float)(sumStacked / (count * sumSquared));
    }

    private static List<(float time, float velocity)> PickVelocityFunction(float[,] semblance,
        float[] timeAxis, float[] velocityAxis, VelocityAnalysisSettings settings)
    {
        var picks = new List<(float time, float velocity)>();
        int numTimes = semblance.GetLength(0);
        int numVelocities = semblance.GetLength(1);

        for (int t = 0; t < numTimes; t += settings.PickInterval)
        {
            // Find maximum semblance at this time
            float maxSemblance = 0;
            int maxVelIdx = 0;

            for (int v = 0; v < numVelocities; v++)
            {
                if (semblance[t, v] > maxSemblance)
                {
                    maxSemblance = semblance[t, v];
                    maxVelIdx = v;
                }
            }

            if (maxSemblance > settings.SemblanceThreshold)
            {
                picks.Add((timeAxis[t], velocityAxis[maxVelIdx]));
            }
        }

        return picks;
    }

    private static List<(float time, float intervalVelocity)> ComputeIntervalVelocities(
        List<(float time, float velocity)> rmsVelocities)
    {
        // Dix equation: Vint = sqrt((V2^2*t2 - V1^2*t1) / (t2 - t1))
        var intervalVelocities = new List<(float time, float intervalVelocity)>();

        for (int i = 0; i < rmsVelocities.Count; i++)
        {
            if (i == 0)
            {
                intervalVelocities.Add((rmsVelocities[i].time, rmsVelocities[i].velocity));
            }
            else
            {
                var (t1, v1) = rmsVelocities[i - 1];
                var (t2, v2) = rmsVelocities[i];

                var numerator = v2 * v2 * t2 - v1 * v1 * t1;
                var denominator = t2 - t1;

                if (denominator > 0 && numerator > 0)
                {
                    var vInt = (float)Math.Sqrt(numerator / denominator);
                    intervalVelocities.Add((t2, vInt));
                }
            }
        }

        return intervalVelocities;
    }

    #endregion

    #region NMO Correction and Stacking

    /// <summary>
    /// Apply NMO (Normal Moveout) correction and stack traces
    /// </summary>
    public static SeismicDataset ApplyNMOAndStack(SeismicDataset dataset, VelocityAnalysisResult velocityModel,
        StackingSettings settings, IProgress<(float progress, string message)>? progress = null)
    {
        var traces = dataset.SegyData?.Traces;
        if (traces == null || traces.Count == 0 || velocityModel.PickedVelocityFunction.Count == 0)
            return dataset;

        progress?.Report((0.0f, "Applying NMO correction..."));

        var sampleInterval = dataset.GetSampleIntervalMs();
        var numSamples = dataset.GetSampleCount();

        // Apply NMO correction to each trace
        var nmoTraces = new List<float[]>();
        for (int i = 0; i < traces.Count; i++)
        {
            var nmoCorrected = ApplyNMOCorrection(traces[i].Samples, i, velocityModel, settings, sampleInterval);
            nmoTraces.Add(nmoCorrected);

            if (i % 50 == 0)
                progress?.Report((0.3f * i / traces.Count, $"NMO correction: trace {i + 1}/{traces.Count}"));
        }

        progress?.Report((0.3f, "Applying mute..."));

        // Apply stretch mute
        if (settings.ApplyStretchMute)
        {
            ApplyStretchMute(nmoTraces, settings.StretchMutePercent, velocityModel, settings.TraceSpacing, sampleInterval);
        }

        progress?.Report((0.5f, "Stacking traces..."));

        // Stack traces into CDPs
        var stackedData = StackTraces(nmoTraces, settings);

        progress?.Report((0.8f, "Creating stacked dataset..."));

        // Create new dataset with stacked traces
        var stackedDataset = CreateStackedDataset(dataset, stackedData, settings, sampleInterval);

        progress?.Report((1.0f, "NMO and stacking complete!"));
        Logger.Log($"[SeismicProcessor] NMO and stacking complete: {nmoTraces.Count} traces -> {stackedData.Count} CDPs");

        return stackedDataset;
    }

    private static float[] ApplyNMOCorrection(float[] samples, int traceIndex, VelocityAnalysisResult velocityModel,
        StackingSettings settings, float sampleInterval)
    {
        var corrected = new float[samples.Length];
        float offset = traceIndex * settings.TraceSpacing;

        for (int i = 0; i < samples.Length; i++)
        {
            float t0 = i * sampleInterval;
            float velocity = InterpolateVelocity(velocityModel.PickedVelocityFunction, t0);

            // NMO equation: t = sqrt(t0^2 + (x/v)^2)
            float t = (float)Math.Sqrt(t0 * t0 + (offset * offset) / (velocity * velocity));
            int sourceSample = (int)(t / sampleInterval);

            if (sourceSample >= 0 && sourceSample < samples.Length)
            {
                // Linear interpolation for better accuracy
                float frac = (t / sampleInterval) - sourceSample;
                if (sourceSample + 1 < samples.Length)
                    corrected[i] = samples[sourceSample] * (1 - frac) + samples[sourceSample + 1] * frac;
                else
                    corrected[i] = samples[sourceSample];
            }
        }

        return corrected;
    }

    private static float InterpolateVelocity(List<(float time, float velocity)> velocityFunction, float time)
    {
        if (velocityFunction.Count == 0) return 2000f; // Default velocity
        if (time <= velocityFunction[0].time) return velocityFunction[0].velocity;
        if (time >= velocityFunction[^1].time) return velocityFunction[^1].velocity;

        for (int i = 1; i < velocityFunction.Count; i++)
        {
            if (time <= velocityFunction[i].time)
            {
                var (t1, v1) = velocityFunction[i - 1];
                var (t2, v2) = velocityFunction[i];
                float t = (time - t1) / (t2 - t1);
                return v1 + t * (v2 - v1);
            }
        }

        return velocityFunction[^1].velocity;
    }

    private static void ApplyStretchMute(List<float[]> traces, float stretchMutePercent,
        VelocityAnalysisResult velocityModel, float traceSpacing, float sampleInterval)
    {
        Parallel.For(0, traces.Count, i =>
        {
            float offset = i * traceSpacing;
            var samples = traces[i];

            for (int j = 0; j < samples.Length; j++)
            {
                float t0 = j * sampleInterval;
                float velocity = InterpolateVelocity(velocityModel.PickedVelocityFunction, t0);

                float t = (float)Math.Sqrt(t0 * t0 + (offset * offset) / (velocity * velocity));
                float stretch = (t - t0) / (t0 + 0.001f) * 100f;

                if (stretch > stretchMutePercent)
                    samples[j] = 0;
            }
        });
    }

    private static List<float[]> StackTraces(List<float[]> nmoTraces, StackingSettings settings)
    {
        int numSamples = nmoTraces[0].Length;
        int cdpFold = settings.CDPFold;
        int numCDPs = nmoTraces.Count / cdpFold;
        if (numCDPs == 0) numCDPs = 1;

        var stackedTraces = new List<float[]>();

        for (int cdp = 0; cdp < numCDPs; cdp++)
        {
            var stacked = new float[numSamples];
            int traceStart = cdp * cdpFold;
            int traceEnd = Math.Min(traceStart + cdpFold, nmoTraces.Count);
            int count = traceEnd - traceStart;

            for (int sample = 0; sample < numSamples; sample++)
            {
                float sum = 0;
                for (int t = traceStart; t < traceEnd; t++)
                {
                    sum += nmoTraces[t][sample];
                }
                stacked[sample] = sum / count;
            }

            stackedTraces.Add(stacked);
        }

        return stackedTraces;
    }

    private static SeismicDataset CreateStackedDataset(SeismicDataset original, List<float[]> stackedData,
        StackingSettings settings, float sampleInterval)
    {
        var stacked = new SeismicDataset($"{original.Name}_stacked", original.FilePath)
        {
            IsStack = true,
            SurveyName = original.SurveyName,
            LineNumber = original.LineNumber,
            ProcessingHistory = original.ProcessingHistory + $"; NMO+Stack (fold={settings.CDPFold})"
        };

        var header = new SegyHeader
        {
            NumSamples = stackedData[0].Length,
            SampleInterval = (int)(sampleInterval * 1000), // Convert to microseconds
            NumTraces = stackedData.Count
        };

        var traces = new List<SegyTrace>(stackedData.Count);
        for (int i = 0; i < stackedData.Count; i++)
        {
            traces.Add(new SegyTrace
            {
                TraceSequenceNumber = i + 1,
                Samples = stackedData[i]
            });
        }

        stacked.SegyData = new SegyParser(header, traces);

        RecalculateStatistics(stacked);
        return stacked;
    }

    #endregion

    #region Migration

    /// <summary>
    /// Apply seismic migration to move dipping reflectors to their true positions
    /// </summary>
    public static void ApplyMigration(SeismicDataset dataset, MigrationSettings settings,
        IProgress<(float progress, string message)>? progress = null)
    {
        var traces = dataset.SegyData?.Traces;
        if (traces == null || traces.Count == 0) return;

        progress?.Report((0.0f, $"Applying {settings.Method} migration..."));

        switch (settings.Method)
        {
            case MigrationMethod.Kirchhoff:
                ApplyKirchhoffMigration(traces, settings, progress);
                break;
            case MigrationMethod.PhaseShift:
                ApplyPhaseShiftMigration(traces, settings, progress);
                break;
            case MigrationMethod.FiniteDifference:
                ApplyFiniteDifferenceMigration(traces, settings, progress);
                break;
            case MigrationMethod.StoltFK:
                ApplyStoltFKMigration(traces, settings, progress);
                break;
        }

        dataset.IsMigrated = true;
        dataset.ProcessingHistory += $"; {settings.Method} migration (v={settings.MigrationVelocity})";
        RecalculateStatistics(dataset);

        progress?.Report((1.0f, "Migration complete!"));
        Logger.Log($"[SeismicProcessor] Applied {settings.Method} migration");
    }

    private static void ApplyKirchhoffMigration(List<SegyTrace> traces, MigrationSettings settings,
        IProgress<(float progress, string message)>? progress)
    {
        // Kirchhoff migration - diffraction summation
        int numTraces = traces.Count;
        int numSamples = traces[0].Samples.Length;
        float velocity = settings.MigrationVelocity;
        float sampleInterval = settings.SampleIntervalMs / 1000f;
        float traceSpacing = settings.TraceSpacing;
        int aperture = settings.ApertureSamples;

        var migrated = new float[numTraces, numSamples];

        for (int outTrace = 0; outTrace < numTraces; outTrace++)
        {
            for (int outSample = 0; outSample < numSamples; outSample++)
            {
                float t0 = outSample * sampleInterval;
                float z = velocity * t0 / 2; // Depth

                float sum = 0;
                int count = 0;

                // Sum over aperture
                int startTrace = Math.Max(0, outTrace - aperture);
                int endTrace = Math.Min(numTraces, outTrace + aperture);

                for (int inTrace = startTrace; inTrace < endTrace; inTrace++)
                {
                    float x = (inTrace - outTrace) * traceSpacing;
                    float travelTime = 2 * (float)Math.Sqrt(z * z + x * x) / velocity;
                    int inSample = (int)(travelTime / sampleInterval);

                    if (inSample >= 0 && inSample < numSamples)
                    {
                        // Obliquity factor
                        float obliquity = z / (float)Math.Sqrt(z * z + x * x + 0.001f);
                        sum += traces[inTrace].Samples[inSample] * obliquity;
                        count++;
                    }
                }

                if (count > 0)
                    migrated[outTrace, outSample] = sum / count;
            }

            if (outTrace % 20 == 0)
                progress?.Report((0.1f + 0.8f * outTrace / numTraces, $"Migrating trace {outTrace + 1}/{numTraces}"));
        }

        // Copy back
        for (int i = 0; i < numTraces; i++)
        {
            for (int j = 0; j < numSamples; j++)
            {
                traces[i].Samples[j] = migrated[i, j];
            }
        }
    }

    private static void ApplyPhaseShiftMigration(List<SegyTrace> traces, MigrationSettings settings,
        IProgress<(float progress, string message)>? progress)
    {
        // Phase-shift migration (frequency-wavenumber domain)
        int numTraces = traces.Count;
        int numSamples = traces[0].Samples.Length;
        float velocity = settings.MigrationVelocity;
        float sampleInterval = settings.SampleIntervalMs / 1000f;

        // Build 2D data array
        var data = new Complex[numSamples, numTraces];
        for (int t = 0; t < numTraces; t++)
        {
            for (int s = 0; s < numSamples; s++)
            {
                data[s, t] = new Complex(traces[t].Samples[s], 0);
            }
        }

        progress?.Report((0.2f, "Computing 2D FFT..."));

        // Apply 2D FFT
        FFT2D(data, false);

        progress?.Report((0.4f, "Applying phase shift..."));

        // Apply phase shift for each (f, k) pair
        float df = 1.0f / (numSamples * sampleInterval);
        float dk = 1.0f / (numTraces * settings.TraceSpacing);

        for (int f = 0; f < numSamples; f++)
        {
            float freq = (f < numSamples / 2) ? f * df : (f - numSamples) * df;
            float omega = (float)(2 * Math.PI * freq);

            for (int k = 0; k < numTraces; k++)
            {
                float kx = (k < numTraces / 2) ? k * dk : (k - numTraces) * dk;
                float kz2 = (omega * omega) / (velocity * velocity) - kx * kx;

                if (kz2 > 0)
                {
                    float kz = (float)Math.Sqrt(kz2);
                    // Phase shift for downward continuation
                    var phase = Complex.Exp(new Complex(0, kz * velocity * sampleInterval));
                    data[f, k] *= phase;
                }
                else
                {
                    data[f, k] = Complex.Zero; // Evanescent wave
                }
            }
        }

        progress?.Report((0.7f, "Computing inverse FFT..."));

        // Inverse 2D FFT
        FFT2D(data, true);

        // Copy back real part
        for (int t = 0; t < numTraces; t++)
        {
            for (int s = 0; s < numSamples; s++)
            {
                traces[t].Samples[s] = (float)data[s, t].Real;
            }
        }
    }

    private static void ApplyFiniteDifferenceMigration(List<SegyTrace> traces, MigrationSettings settings,
        IProgress<(float progress, string message)>? progress)
    {
        // Finite-difference migration (15-degree equation)
        int numTraces = traces.Count;
        int numSamples = traces[0].Samples.Length;
        float velocity = settings.MigrationVelocity;
        float dt = settings.SampleIntervalMs / 1000f;
        float dx = settings.TraceSpacing;

        // Stability parameters
        float alpha = velocity * velocity * dt / (4 * dx * dx);

        var current = new float[numTraces, numSamples];
        var next = new float[numTraces, numSamples];

        // Initialize with input data
        for (int t = 0; t < numTraces; t++)
        {
            for (int s = 0; s < numSamples; s++)
            {
                current[t, s] = traces[t].Samples[s];
            }
        }

        // Downward continuation
        int numSteps = settings.MigrationDepthSteps;
        for (int step = 0; step < numSteps; step++)
        {
            for (int t = 1; t < numTraces - 1; t++)
            {
                for (int s = 0; s < numSamples; s++)
                {
                    // 15-degree implicit finite difference
                    next[t, s] = current[t, s] + alpha * (current[t - 1, s] - 2 * current[t, s] + current[t + 1, s]);
                }
            }

            // Swap buffers
            var temp = current;
            current = next;
            next = temp;

            if (step % 10 == 0)
                progress?.Report((0.1f + 0.8f * step / numSteps, $"Migration step {step + 1}/{numSteps}"));
        }

        // Copy result back
        for (int t = 0; t < numTraces; t++)
        {
            for (int s = 0; s < numSamples; s++)
            {
                traces[t].Samples[s] = current[t, s];
            }
        }
    }

    private static void ApplyStoltFKMigration(List<SegyTrace> traces, MigrationSettings settings,
        IProgress<(float progress, string message)>? progress)
    {
        // Stolt (F-K) migration - constant velocity
        // Maps data from (kx, omega) to (kx, kz)
        ApplyPhaseShiftMigration(traces, settings, progress); // Simplified - uses phase shift
    }

    #endregion

    #region Helper Methods

    private static float[] ApplyBandpassFFT(float[] data, float lowVel, float highVel)
    {
        // Simplified bandpass in time domain
        return ApplyButterworth(data, lowVel, highVel);
    }

    private static float[] ApplySpatialFilter(float[] data, float rejectSlope)
    {
        // Simple spatial smoothing filter
        var result = new float[data.Length];
        for (int i = 1; i < data.Length - 1; i++)
        {
            result[i] = 0.25f * data[i - 1] + 0.5f * data[i] + 0.25f * data[i + 1];
        }
        result[0] = data[0];
        result[data.Length - 1] = data[data.Length - 1];
        return result;
    }

    private static float[] ApplyButterworth(float[] samples, float lowCut, float highCut)
    {
        // Simple IIR bandpass filter
        var result = new float[samples.Length];
        float alpha = 0.1f; // Smoothing factor

        // High-pass
        float prev = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            result[i] = alpha * (result[i > 0 ? i - 1 : 0] + samples[i] - prev);
            prev = samples[i];
        }

        // Low-pass
        for (int i = 1; i < result.Length; i++)
        {
            result[i] = result[i - 1] + alpha * (result[i] - result[i - 1]);
        }

        return result;
    }

    private static double[,] ComputeTruncatedSVDReconstruction(double[,] matrix, int components,
        IProgress<(float progress, string message)>? progress)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        var result = new double[rows, cols];

        // Simplified: Use power iteration for dominant singular vectors
        for (int comp = 0; comp < Math.Min(components, Math.Min(rows, cols)); comp++)
        {
            // Initialize random vector
            var v = new double[cols];
            var random = new Random(42 + comp);
            for (int i = 0; i < cols; i++) v[i] = random.NextDouble();

            // Power iteration
            for (int iter = 0; iter < 20; iter++)
            {
                // u = A * v
                var u = new double[rows];
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                        u[i] += matrix[i, j] * v[j];
                }

                // Normalize u
                double normU = Math.Sqrt(u.Sum(x => x * x));
                for (int i = 0; i < rows; i++) u[i] /= normU;

                // v = A^T * u
                v = new double[cols];
                for (int j = 0; j < cols; j++)
                {
                    for (int i = 0; i < rows; i++)
                        v[j] += matrix[i, j] * u[i];
                }

                // Normalize v
                double normV = Math.Sqrt(v.Sum(x => x * x));
                for (int j = 0; j < cols; j++) v[j] /= normV;
            }

            // Compute singular value
            var Av = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                    Av[i] += matrix[i, j] * v[j];
            }
            double sigma = Math.Sqrt(Av.Sum(x => x * x));

            // Add contribution to result
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    result[i, j] += sigma * Av[i] / (sigma + 0.001) * v[j];
                }
            }

            // Deflate matrix for next component
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    matrix[i, j] -= sigma * Av[i] / (sigma + 0.001) * v[j];
                }
            }

            progress?.Report((0.2f + 0.6f * comp / components, $"SVD component {comp + 1}/{components}"));
        }

        return result;
    }

    private static void HaarTransform(float[] data, bool forward)
    {
        int n = data.Length;
        var temp = new float[n];

        if (forward)
        {
            while (n > 1)
            {
                n /= 2;
                for (int i = 0; i < n; i++)
                {
                    temp[i] = (data[2 * i] + data[2 * i + 1]) / 1.414f;
                    temp[n + i] = (data[2 * i] - data[2 * i + 1]) / 1.414f;
                }
                Array.Copy(temp, data, n * 2);
            }
        }
        else
        {
            n = 1;
            while (n * 2 <= data.Length)
            {
                for (int i = 0; i < n; i++)
                {
                    temp[2 * i] = (data[i] + data[n + i]) / 1.414f;
                    temp[2 * i + 1] = (data[i] - data[n + i]) / 1.414f;
                }
                Array.Copy(temp, data, n * 2);
                n *= 2;
            }
        }
    }

    private static float[] EstimateNoiseModel(List<SegyTrace> traces, int[] referenceTraces)
    {
        int numSamples = traces[0].Samples.Length;
        var noiseModel = new float[numSamples];

        if (referenceTraces == null || referenceTraces.Length == 0)
        {
            // Use edge traces as noise reference
            for (int j = 0; j < numSamples; j++)
            {
                noiseModel[j] = (traces[0].Samples[j] + traces[^1].Samples[j]) / 2;
            }
        }
        else
        {
            for (int j = 0; j < numSamples; j++)
            {
                float sum = 0;
                foreach (var idx in referenceTraces)
                {
                    if (idx >= 0 && idx < traces.Count)
                        sum += traces[idx].Samples[j];
                }
                noiseModel[j] = sum / referenceTraces.Length;
            }
        }

        return noiseModel;
    }

    private static float[] AdaptNoiseToTrace(float[] noiseModel, float[] trace, int windowSize)
    {
        int n = trace.Length;
        var adapted = new float[n];

        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - windowSize / 2);
            int end = Math.Min(n, i + windowSize / 2);

            // Compute local scaling factor
            float tracePower = 0, noisePower = 0;
            for (int j = start; j < end; j++)
            {
                tracePower += trace[j] * trace[j];
                noisePower += noiseModel[j] * noiseModel[j];
            }

            float scale = noisePower > 0.001f ? (float)Math.Sqrt(tracePower / noisePower) : 1;
            adapted[i] = noiseModel[i] * Math.Min(scale, 2);
        }

        return adapted;
    }

    private static float[] WienerDeconvolution(float[] samples, int filterLength, float prewhitening)
    {
        // Simplified Wiener deconvolution
        int n = samples.Length;
        var result = new float[n];

        // Compute autocorrelation
        var autocorr = new float[filterLength];
        for (int lag = 0; lag < filterLength; lag++)
        {
            float sum = 0;
            for (int i = 0; i < n - lag; i++)
            {
                sum += samples[i] * samples[i + lag];
            }
            autocorr[lag] = sum / (n - lag);
        }

        // Add prewhitening
        autocorr[0] *= (1 + prewhitening);

        // Solve Toeplitz system using Levinson recursion (simplified)
        var filter = new float[filterLength];
        filter[0] = 1.0f / autocorr[0];

        // Apply filter via convolution
        for (int i = 0; i < n; i++)
        {
            float sum = 0;
            for (int j = 0; j < filterLength && i - j >= 0; j++)
            {
                sum += samples[i - j] * filter[j];
            }
            result[i] = sum;
        }

        return result;
    }

    private static float ComputeCorrelation(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        float sumA = 0, sumB = 0, sumAB = 0, sumA2 = 0, sumB2 = 0;
        int n = a.Length;

        for (int i = 0; i < n; i++)
        {
            sumA += a[i];
            sumB += b[i];
            sumAB += a[i] * b[i];
            sumA2 += a[i] * a[i];
            sumB2 += b[i] * b[i];
        }

        float numerator = n * sumAB - sumA * sumB;
        float denominator = (float)Math.Sqrt((n * sumA2 - sumA * sumA) * (n * sumB2 - sumB * sumB));

        return denominator > 0.001f ? numerator / denominator : 0;
    }

    private static void FFT2D(Complex[,] data, bool inverse)
    {
        int rows = data.GetLength(0);
        int cols = data.GetLength(1);

        // FFT on rows
        for (int r = 0; r < rows; r++)
        {
            var row = new Complex[cols];
            for (int c = 0; c < cols; c++) row[c] = data[r, c];
            FFT1D(row, inverse);
            for (int c = 0; c < cols; c++) data[r, c] = row[c];
        }

        // FFT on columns
        for (int c = 0; c < cols; c++)
        {
            var col = new Complex[rows];
            for (int r = 0; r < rows; r++) col[r] = data[r, c];
            FFT1D(col, inverse);
            for (int r = 0; r < rows; r++) data[r, c] = col[r];
        }
    }

    private static void FFT1D(Complex[] data, bool inverse)
    {
        int n = data.Length;
        if (n <= 1) return;

        // Bit-reversal permutation
        int j = 0;
        for (int i = 0; i < n - 1; i++)
        {
            if (i < j)
            {
                var temp = data[i];
                data[i] = data[j];
                data[j] = temp;
            }
            int k = n / 2;
            while (k <= j)
            {
                j -= k;
                k /= 2;
            }
            j += k;
        }

        // Cooley-Tukey iterative FFT
        for (int len = 2; len <= n; len *= 2)
        {
            double angle = (inverse ? 1 : -1) * 2 * Math.PI / len;
            var wlen = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int k = 0; k < len / 2; k++)
                {
                    var t = w * data[i + k + len / 2];
                    var u = data[i + k];
                    data[i + k] = u + t;
                    data[i + k + len / 2] = u - t;
                    w *= wlen;
                }
            }
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
                data[i] /= n;
        }
    }

    private static void RecalculateStatistics(SeismicDataset dataset)
    {
        var traces = dataset.SegyData?.Traces;
        var header = dataset.SegyData?.Header;
        if (traces == null || header == null) return;

        float minAmp = float.MaxValue;
        float maxAmp = float.MinValue;
        double sumSquares = 0;
        long count = 0;

        foreach (var trace in traces)
        {
            foreach (var sample in trace.Samples)
            {
                if (sample < minAmp) minAmp = sample;
                if (sample > maxAmp) maxAmp = sample;
                sumSquares += sample * sample;
                count++;
            }
        }

        header.MinAmplitude = minAmp;
        header.MaxAmplitude = maxAmp;
        header.RmsAmplitude = count > 0 ? (float)Math.Sqrt(sumSquares / count) : 0;
    }

    #endregion
}

#region Settings and Result Classes

public enum NoiseRemovalMethod
{
    MedianFilter,
    FKFilter,
    SingularValueDecomposition,
    WaveletDenoising,
    AdaptiveSubtraction,
    SpikeDeconvolution
}

public class NoiseRemovalSettings
{
    public int FilterWindowSize { get; set; } = 5;
    public int SVDComponents { get; set; } = 10;
    public float WaveletThreshold { get; set; } = 0.1f;
    public float SpikeThreshold { get; set; } = 5.0f;
    public float FKLowCutVelocity { get; set; } = 1000f;
    public float FKHighCutVelocity { get; set; } = 5000f;
    public float FKRejectSlope { get; set; } = 0.001f;
    public int AdaptiveWindowSize { get; set; } = 50;
    public int[] AdaptiveReferenceTraces { get; set; } = Array.Empty<int>();
}

public class DataCorrectionSettings
{
    public float SampleIntervalMs { get; set; } = 4.0f;

    // Static corrections
    public bool ApplyStaticCorrection { get; set; } = false;
    public float StaticShiftMs { get; set; } = 0f;
    public bool UseTraceStatics { get; set; } = false;
    public Dictionary<int, float> TraceStatics { get; set; } = new();

    // Spherical divergence
    public bool ApplySphericalDivergence { get; set; } = false;
    public float AverageVelocity { get; set; } = 2500f;
    public float SphericalDivergenceGain { get; set; } = 0.001f;

    // Attenuation
    public bool ApplyAttenuation { get; set; } = false;
    public float QFactor { get; set; } = 100f;
    public float DominantFrequency { get; set; } = 30f;
    public float MaxAttenuationGain { get; set; } = 10f;

    // Geometric spreading
    public bool ApplyGeometricSpreading { get; set; } = false;
    public float GeometricSpreadingExponent { get; set; } = 1.5f;

    // Source wavelet deconvolution
    public bool ApplySourceWaveletDecon { get; set; } = false;
    public int DeconvolutionFilterLength { get; set; } = 100;
    public float DeconvolutionPrewhitening { get; set; } = 0.01f;

    // Polarity correction
    public bool ApplyPolarityCorrection { get; set; } = false;
    public int ReferenceTrace { get; set; } = 0;
    public float PolarityCorrectionThreshold { get; set; } = 0.5f;
}

public class VelocityAnalysisSettings
{
    public float MinVelocity { get; set; } = 1500f;
    public float MaxVelocity { get; set; } = 6000f;
    public int VelocityScanSteps { get; set; } = 100;
    public int TimeScanSteps { get; set; } = 200;
    public float SemblanceWindowMs { get; set; } = 50f;
    public float TraceSpacing { get; set; } = 25f;
    public int PickInterval { get; set; } = 10;
    public float SemblanceThreshold { get; set; } = 0.3f;
}

public class VelocityAnalysisResult
{
    public float[,] Semblance { get; set; } = new float[0, 0];
    public float[] TimeAxis { get; set; } = Array.Empty<float>();
    public float[] VelocityAxis { get; set; } = Array.Empty<float>();
    public List<(float time, float velocity)> PickedVelocityFunction { get; set; } = new();
    public List<(float time, float intervalVelocity)> IntervalVelocities { get; set; } = new();
    public (float min, float max) TimeRange { get; set; }
    public (float min, float max) VelocityRange { get; set; }
}

public class StackingSettings
{
    public int CDPFold { get; set; } = 12;
    public float TraceSpacing { get; set; } = 25f;
    public bool ApplyStretchMute { get; set; } = true;
    public float StretchMutePercent { get; set; } = 30f;
}

public enum MigrationMethod
{
    Kirchhoff,
    PhaseShift,
    FiniteDifference,
    StoltFK
}

public class MigrationSettings
{
    public MigrationMethod Method { get; set; } = MigrationMethod.Kirchhoff;
    public float MigrationVelocity { get; set; } = 2500f;
    public float SampleIntervalMs { get; set; } = 4.0f;
    public float TraceSpacing { get; set; } = 25f;
    public int ApertureSamples { get; set; } = 50;
    public int MigrationDepthSteps { get; set; } = 100;
}

#endregion
