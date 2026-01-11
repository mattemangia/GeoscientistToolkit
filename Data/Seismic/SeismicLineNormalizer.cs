// GeoscientistToolkit/Data/Seismic/SeismicLineNormalizer.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Seismic;

/// <summary>
/// Normalizes seismic lines at intersections to create consistent amplitude,
/// frequency, and phase characteristics across the seismic cube.
/// </summary>
public class SeismicLineNormalizer
{
    private readonly CubeNormalizationSettings _settings;

    public SeismicLineNormalizer(CubeNormalizationSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Normalize two seismic lines at their intersection point
    /// </summary>
    public NormalizationResult NormalizeAtIntersection(
        SeismicDataset line1,
        SeismicDataset line2,
        int line1TraceIndex,
        int line2TraceIndex)
    {
        var result = new NormalizationResult
        {
            Line1Name = line1.Name,
            Line2Name = line2.Name,
            Line1TraceIndex = line1TraceIndex,
            Line2TraceIndex = line2TraceIndex
        };

        // Extract traces around intersection
        var traces1 = ExtractTracesAroundIndex(line1, line1TraceIndex, _settings.MatchingWindowTraces);
        var traces2 = ExtractTracesAroundIndex(line2, line2TraceIndex, _settings.MatchingWindowTraces);

        if (traces1.Count == 0 || traces2.Count == 0)
        {
            result.Success = false;
            result.ErrorMessage = "Could not extract traces at intersection";
            return result;
        }

        // Calculate initial mismatch
        result.InitialAmplitudeMismatch = CalculateAmplitudeMismatch(traces1, traces2);
        result.InitialPhaseMismatch = CalculatePhaseMismatch(traces1, traces2);
        result.InitialFrequencyMismatch = CalculateFrequencyMismatch(traces1, traces2, line1.GetSampleIntervalMs());

        // Apply normalization
        if (_settings.NormalizeAmplitude)
        {
            ApplyAmplitudeNormalization(line1, line2, line1TraceIndex, line2TraceIndex, traces1, traces2);
            result.AmplitudeNormalizationApplied = true;
        }

        if (_settings.MatchFrequency)
        {
            ApplyFrequencyMatching(line1, line2, line1TraceIndex, line2TraceIndex);
            result.FrequencyMatchingApplied = true;
        }

        if (_settings.MatchPhase)
        {
            ApplyPhaseMatching(line1, line2, line1TraceIndex, line2TraceIndex, traces1, traces2);
            result.PhaseMatchingApplied = true;
        }

        if (_settings.SmoothTransitions)
        {
            ApplyTransitionSmoothing(line1, line1TraceIndex);
            ApplyTransitionSmoothing(line2, line2TraceIndex);
            result.TransitionSmoothingApplied = true;
        }

        // Calculate final mismatch
        traces1 = ExtractTracesAroundIndex(line1, line1TraceIndex, _settings.MatchingWindowTraces);
        traces2 = ExtractTracesAroundIndex(line2, line2TraceIndex, _settings.MatchingWindowTraces);

        result.FinalAmplitudeMismatch = CalculateAmplitudeMismatch(traces1, traces2);
        result.FinalPhaseMismatch = CalculatePhaseMismatch(traces1, traces2);
        result.FinalFrequencyMismatch = CalculateFrequencyMismatch(traces1, traces2, line1.GetSampleIntervalMs());

        // Calculate tie quality (0-1, higher is better)
        result.TieQuality = CalculateTieQuality(traces1, traces2);
        result.Success = true;

        Logger.Log($"[Normalizer] Normalized intersection: Amp mismatch {result.InitialAmplitudeMismatch:F3} -> {result.FinalAmplitudeMismatch:F3}, Tie quality: {result.TieQuality:F3}");

        return result;
    }

    /// <summary>
    /// Extract traces around a given index
    /// </summary>
    private List<float[]> ExtractTracesAroundIndex(SeismicDataset dataset, int centerIndex, int windowSize)
    {
        var traces = new List<float[]>();
        int halfWindow = windowSize / 2;
        int traceCount = dataset.GetTraceCount();

        for (int i = centerIndex - halfWindow; i <= centerIndex + halfWindow; i++)
        {
            if (i >= 0 && i < traceCount)
            {
                var trace = dataset.GetTrace(i);
                if (trace != null)
                {
                    traces.Add((float[])trace.Samples.Clone());
                }
            }
        }

        return traces;
    }

    /// <summary>
    /// Calculate amplitude mismatch between two sets of traces
    /// </summary>
    private float CalculateAmplitudeMismatch(List<float[]> traces1, List<float[]> traces2)
    {
        var rms1 = CalculateRMS(traces1);
        var rms2 = CalculateRMS(traces2);

        if (rms1 < 1e-10f || rms2 < 1e-10f)
            return 0f;

        return Math.Abs(rms1 - rms2) / Math.Max(rms1, rms2);
    }

    /// <summary>
    /// Calculate phase mismatch using cross-correlation
    /// </summary>
    private float CalculatePhaseMismatch(List<float[]> traces1, List<float[]> traces2)
    {
        if (traces1.Count == 0 || traces2.Count == 0)
            return 0f;

        // Use center traces for phase comparison
        var trace1 = traces1[traces1.Count / 2];
        var trace2 = traces2[traces2.Count / 2];

        // Find phase shift via cross-correlation
        int maxLag = Math.Min(50, trace1.Length / 4);
        float maxCorr = float.MinValue;
        int bestLag = 0;

        for (int lag = -maxLag; lag <= maxLag; lag++)
        {
            float corr = CrossCorrelation(trace1, trace2, lag);
            if (corr > maxCorr)
            {
                maxCorr = corr;
                bestLag = lag;
            }
        }

        // Convert lag to phase in degrees (assuming sample interval)
        return bestLag; // Return as sample shift
    }

    /// <summary>
    /// Calculate frequency content difference
    /// </summary>
    private float CalculateFrequencyMismatch(List<float[]> traces1, List<float[]> traces2, float sampleIntervalMs)
    {
        var centroid1 = CalculateSpectralCentroid(traces1, sampleIntervalMs);
        var centroid2 = CalculateSpectralCentroid(traces2, sampleIntervalMs);

        if (centroid1 < 1e-10f || centroid2 < 1e-10f)
            return 0f;

        return Math.Abs(centroid1 - centroid2);
    }

    /// <summary>
    /// Apply amplitude normalization
    /// </summary>
    private void ApplyAmplitudeNormalization(
        SeismicDataset line1,
        SeismicDataset line2,
        int line1TraceIndex,
        int line2TraceIndex,
        List<float[]> traces1,
        List<float[]> traces2)
    {
        float scalar1, scalar2;

        switch (_settings.AmplitudeMethod)
        {
            case AmplitudeNormalizationMethod.RMS:
                var rms1 = CalculateRMS(traces1);
                var rms2 = CalculateRMS(traces2);
                var targetRms = (rms1 + rms2) / 2;
                scalar1 = rms1 > 1e-10f ? targetRms / rms1 : 1f;
                scalar2 = rms2 > 1e-10f ? targetRms / rms2 : 1f;
                break;

            case AmplitudeNormalizationMethod.Mean:
                var mean1 = CalculateMeanAbsAmplitude(traces1);
                var mean2 = CalculateMeanAbsAmplitude(traces2);
                var targetMean = (mean1 + mean2) / 2;
                scalar1 = mean1 > 1e-10f ? targetMean / mean1 : 1f;
                scalar2 = mean2 > 1e-10f ? targetMean / mean2 : 1f;
                break;

            case AmplitudeNormalizationMethod.Peak:
                var peak1 = CalculatePeakAmplitude(traces1);
                var peak2 = CalculatePeakAmplitude(traces2);
                var targetPeak = (peak1 + peak2) / 2;
                scalar1 = peak1 > 1e-10f ? targetPeak / peak1 : 1f;
                scalar2 = peak2 > 1e-10f ? targetPeak / peak2 : 1f;
                break;

            case AmplitudeNormalizationMethod.Median:
                var median1 = CalculateMedianAbsAmplitude(traces1);
                var median2 = CalculateMedianAbsAmplitude(traces2);
                var targetMedian = (median1 + median2) / 2;
                scalar1 = median1 > 1e-10f ? targetMedian / median1 : 1f;
                scalar2 = median2 > 1e-10f ? targetMedian / median2 : 1f;
                break;

            case AmplitudeNormalizationMethod.Balanced:
            default:
                // Combination of RMS and median for robust normalization
                var rms1b = CalculateRMS(traces1);
                var rms2b = CalculateRMS(traces2);
                var median1b = CalculateMedianAbsAmplitude(traces1);
                var median2b = CalculateMedianAbsAmplitude(traces2);
                var balanced1 = (rms1b + median1b) / 2;
                var balanced2 = (rms2b + median2b) / 2;
                var targetBalanced = (balanced1 + balanced2) / 2;
                scalar1 = balanced1 > 1e-10f ? targetBalanced / balanced1 : 1f;
                scalar2 = balanced2 > 1e-10f ? targetBalanced / balanced2 : 1f;
                break;
        }

        // Apply scalars with smooth transition
        ApplyAmplitudeScalarWithTransition(line1, line1TraceIndex, scalar1);
        ApplyAmplitudeScalarWithTransition(line2, line2TraceIndex, scalar2);
    }

    /// <summary>
    /// Apply amplitude scalar with smooth transition zone
    /// </summary>
    private void ApplyAmplitudeScalarWithTransition(SeismicDataset dataset, int centerIndex, float scalar)
    {
        if (Math.Abs(scalar - 1.0f) < 0.001f)
            return; // No significant change needed

        int transitionZone = _settings.TransitionZoneTraces;
        int traceCount = dataset.GetTraceCount();

        // Apply full scalar at center, tapering off at edges
        for (int offset = -transitionZone; offset <= transitionZone; offset++)
        {
            int traceIndex = centerIndex + offset;
            if (traceIndex < 0 || traceIndex >= traceCount)
                continue;

            var trace = dataset.GetTrace(traceIndex);
            if (trace == null)
                continue;

            // Calculate transition weight (1.0 at center, 0.0 at edges)
            float weight = 1.0f - Math.Abs(offset) / (float)(transitionZone + 1);
            float effectiveScalar = 1.0f + (scalar - 1.0f) * weight;

            // Apply scalar to trace samples
            for (int i = 0; i < trace.Samples.Length; i++)
            {
                trace.Samples[i] *= effectiveScalar;
            }
        }
    }

    /// <summary>
    /// Apply frequency matching using spectral shaping
    /// </summary>
    private void ApplyFrequencyMatching(
        SeismicDataset line1,
        SeismicDataset line2,
        int line1TraceIndex,
        int line2TraceIndex)
    {
        float sampleIntervalMs = line1.GetSampleIntervalMs();
        if (sampleIntervalMs <= 0) return;

        float sampleRate = 1000f / sampleIntervalMs;
        float lowFreq = _settings.TargetFrequencyLow;
        float highFreq = _settings.TargetFrequencyHigh;

        // Apply bandpass filter to traces around intersection
        int windowSize = _settings.MatchingWindowTraces;
        int traceCount1 = line1.GetTraceCount();
        int traceCount2 = line2.GetTraceCount();

        for (int offset = -windowSize; offset <= windowSize; offset++)
        {
            int idx1 = line1TraceIndex + offset;
            int idx2 = line2TraceIndex + offset;

            if (idx1 >= 0 && idx1 < traceCount1)
            {
                var trace = line1.GetTrace(idx1);
                if (trace != null)
                {
                    trace.Samples = ApplyBandpassFilter(trace.Samples, lowFreq, highFreq, sampleRate);
                }
            }

            if (idx2 >= 0 && idx2 < traceCount2)
            {
                var trace = line2.GetTrace(idx2);
                if (trace != null)
                {
                    trace.Samples = ApplyBandpassFilter(trace.Samples, lowFreq, highFreq, sampleRate);
                }
            }
        }
    }

    /// <summary>
    /// Apply phase matching by shifting traces
    /// </summary>
    private void ApplyPhaseMatching(
        SeismicDataset line1,
        SeismicDataset line2,
        int line1TraceIndex,
        int line2TraceIndex,
        List<float[]> traces1,
        List<float[]> traces2)
    {
        // Find optimal phase shift
        float phaseShift = CalculatePhaseMismatch(traces1, traces2);
        int sampleShift = (int)Math.Round(phaseShift / 2); // Split shift between both lines

        if (Math.Abs(sampleShift) < 1)
            return; // No significant shift needed

        // Apply shift to line 2 (keep line 1 as reference)
        int windowSize = _settings.MatchingWindowTraces;
        int traceCount2 = line2.GetTraceCount();

        for (int offset = -windowSize; offset <= windowSize; offset++)
        {
            int idx2 = line2TraceIndex + offset;
            if (idx2 >= 0 && idx2 < traceCount2)
            {
                var trace = line2.GetTrace(idx2);
                if (trace != null)
                {
                    trace.Samples = ShiftSamples(trace.Samples, sampleShift);
                }
            }
        }
    }

    /// <summary>
    /// Apply transition smoothing at intersection
    /// </summary>
    private void ApplyTransitionSmoothing(SeismicDataset dataset, int centerIndex)
    {
        int windowSize = _settings.TransitionZoneTraces;
        int traceCount = dataset.GetTraceCount();

        // Apply gentle smoothing filter to transition zone
        for (int offset = -windowSize; offset <= windowSize; offset++)
        {
            int traceIndex = centerIndex + offset;
            if (traceIndex < 0 || traceIndex >= traceCount)
                continue;

            var trace = dataset.GetTrace(traceIndex);
            if (trace == null)
                continue;

            // Apply mild smoothing (3-point moving average)
            var smoothed = new float[trace.Samples.Length];
            for (int i = 1; i < trace.Samples.Length - 1; i++)
            {
                float weight = 0.5f + 0.5f * (Math.Abs(offset) / (float)(windowSize + 1));
                smoothed[i] = weight * trace.Samples[i] +
                             (1 - weight) * 0.5f * (trace.Samples[i - 1] + trace.Samples[i + 1]);
            }
            smoothed[0] = trace.Samples[0];
            smoothed[trace.Samples.Length - 1] = trace.Samples[trace.Samples.Length - 1];

            Array.Copy(smoothed, trace.Samples, smoothed.Length);
        }
    }

    /// <summary>
    /// Calculate tie quality (cross-correlation at zero lag)
    /// </summary>
    private float CalculateTieQuality(List<float[]> traces1, List<float[]> traces2)
    {
        if (traces1.Count == 0 || traces2.Count == 0)
            return 0f;

        // Use center traces
        var trace1 = traces1[traces1.Count / 2];
        var trace2 = traces2[traces2.Count / 2];

        // Calculate normalized cross-correlation at zero lag
        float corr = CrossCorrelation(trace1, trace2, 0);

        // Normalize by autocorrelations
        float auto1 = CrossCorrelation(trace1, trace1, 0);
        float auto2 = CrossCorrelation(trace2, trace2, 0);

        if (auto1 < 1e-10f || auto2 < 1e-10f)
            return 0f;

        return corr / MathF.Sqrt(auto1 * auto2);
    }

    #region Helper Methods

    private float CalculateRMS(List<float[]> traces)
    {
        if (traces.Count == 0)
            return 0f;

        double sumSquares = 0;
        int count = 0;

        foreach (var trace in traces)
        {
            foreach (var sample in trace)
            {
                sumSquares += sample * sample;
                count++;
            }
        }

        return count > 0 ? (float)Math.Sqrt(sumSquares / count) : 0f;
    }

    private float CalculateMeanAbsAmplitude(List<float[]> traces)
    {
        if (traces.Count == 0)
            return 0f;

        double sum = 0;
        int count = 0;

        foreach (var trace in traces)
        {
            foreach (var sample in trace)
            {
                sum += Math.Abs(sample);
                count++;
            }
        }

        return count > 0 ? (float)(sum / count) : 0f;
    }

    private float CalculatePeakAmplitude(List<float[]> traces)
    {
        if (traces.Count == 0)
            return 0f;

        float peak = 0;
        foreach (var trace in traces)
        {
            foreach (var sample in trace)
            {
                peak = Math.Max(peak, Math.Abs(sample));
            }
        }

        return peak;
    }

    private float CalculateMedianAbsAmplitude(List<float[]> traces)
    {
        if (traces.Count == 0)
            return 0f;

        var allSamples = new List<float>();
        foreach (var trace in traces)
        {
            foreach (var sample in trace)
            {
                allSamples.Add(Math.Abs(sample));
            }
        }

        if (allSamples.Count == 0)
            return 0f;

        allSamples.Sort();
        return allSamples[allSamples.Count / 2];
    }

    private float CalculateSpectralCentroid(List<float[]> traces, float sampleIntervalMs)
    {
        if (traces.Count == 0 || sampleIntervalMs <= 0)
            return 0f;

        // Use center trace for spectral analysis
        var trace = traces[traces.Count / 2];
        int n = trace.Length;

        // Simple DFT to estimate spectral centroid
        float sampleRate = 1000f / sampleIntervalMs;
        float freqResolution = sampleRate / n;

        double numerator = 0;
        double denominator = 0;

        for (int k = 1; k < n / 2; k++)
        {
            // Calculate magnitude at frequency k
            double real = 0, imag = 0;
            for (int i = 0; i < n; i++)
            {
                double angle = -2 * Math.PI * k * i / n;
                real += trace[i] * Math.Cos(angle);
                imag += trace[i] * Math.Sin(angle);
            }

            double magnitude = Math.Sqrt(real * real + imag * imag);
            float freq = k * freqResolution;

            numerator += freq * magnitude;
            denominator += magnitude;
        }

        return denominator > 1e-10 ? (float)(numerator / denominator) : 0f;
    }

    private float CrossCorrelation(float[] a, float[] b, int lag)
    {
        int n = Math.Min(a.Length, b.Length);
        float sum = 0;
        int count = 0;

        for (int i = 0; i < n; i++)
        {
            int j = i + lag;
            if (j >= 0 && j < n)
            {
                sum += a[i] * b[j];
                count++;
            }
        }

        return count > 0 ? sum / count : 0f;
    }

    private float[] ShiftSamples(float[] samples, int shift)
    {
        var result = new float[samples.Length];

        for (int i = 0; i < samples.Length; i++)
        {
            int srcIndex = i - shift;
            if (srcIndex >= 0 && srcIndex < samples.Length)
            {
                result[i] = samples[srcIndex];
            }
        }

        return result;
    }

    private float[] ApplyBandpassFilter(float[] samples, float lowFreq, float highFreq, float sampleRate)
    {
        if (samples.Length < 3)
            return samples;

        var result = (float[])samples.Clone();

        // Apply high-pass
        result = ApplyHighPassFilter(result, lowFreq, sampleRate);
        // Apply low-pass
        result = ApplyLowPassFilter(result, highFreq, sampleRate);

        return result;
    }

    private float[] ApplyHighPassFilter(float[] samples, float cutoffFreq, float sampleRate)
    {
        if (samples.Length < 3)
            return samples;

        var result = new float[samples.Length];
        var omega = 2.0 * Math.PI * cutoffFreq / sampleRate;
        var cos_omega = Math.Cos(omega);
        var alpha = Math.Sin(omega) / (2.0 * 0.707);

        var a0 = 1.0 + alpha;
        var a1 = -2.0 * cos_omega;
        var a2 = 1.0 - alpha;
        var b0 = (1.0 + cos_omega) / 2.0;
        var b1 = -(1.0 + cos_omega);
        var b2 = (1.0 + cos_omega) / 2.0;

        b0 /= a0; b1 /= a0; b2 /= a0;
        a1 /= a0; a2 /= a0;

        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            double x0 = samples[i];
            double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            result[i] = (float)y0;
            x2 = x1; x1 = x0;
            y2 = y1; y1 = y0;
        }

        return result;
    }

    private float[] ApplyLowPassFilter(float[] samples, float cutoffFreq, float sampleRate)
    {
        if (samples.Length < 3)
            return samples;

        var result = new float[samples.Length];
        var omega = 2.0 * Math.PI * cutoffFreq / sampleRate;
        var cos_omega = Math.Cos(omega);
        var alpha = Math.Sin(omega) / (2.0 * 0.707);

        var a0 = 1.0 + alpha;
        var a1 = -2.0 * cos_omega;
        var a2 = 1.0 - alpha;
        var b0 = (1.0 - cos_omega) / 2.0;
        var b1 = 1.0 - cos_omega;
        var b2 = (1.0 - cos_omega) / 2.0;

        b0 /= a0; b1 /= a0; b2 /= a0;
        a1 /= a0; a2 /= a0;

        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            double x0 = samples[i];
            double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            result[i] = (float)y0;
            x2 = x1; x1 = x0;
            y2 = y1; y1 = y0;
        }

        return result;
    }

    #endregion
}

/// <summary>
/// Result of a normalization operation
/// </summary>
public class NormalizationResult
{
    public string Line1Name { get; set; } = "";
    public string Line2Name { get; set; } = "";
    public int Line1TraceIndex { get; set; }
    public int Line2TraceIndex { get; set; }

    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public float InitialAmplitudeMismatch { get; set; }
    public float InitialPhaseMismatch { get; set; }
    public float InitialFrequencyMismatch { get; set; }

    public float FinalAmplitudeMismatch { get; set; }
    public float FinalPhaseMismatch { get; set; }
    public float FinalFrequencyMismatch { get; set; }

    public bool AmplitudeNormalizationApplied { get; set; }
    public bool FrequencyMatchingApplied { get; set; }
    public bool PhaseMatchingApplied { get; set; }
    public bool TransitionSmoothingApplied { get; set; }

    public float TieQuality { get; set; }
}
