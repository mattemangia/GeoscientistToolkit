// GeoscientistToolkit/Tools/BoreholeSeismic/BoreholeSeismicIntegration.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Tools.BoreholeSeismic;

/// <summary>
/// Integration tools for borehole and seismic data:
/// - Generate synthetic seismic traces from borehole data
/// - Create pseudo-boreholes from seismic data
/// - Tie well logs to seismic sections
/// </summary>
public static class BoreholeSeismicIntegration
{
    /// <summary>
    /// Generate a synthetic seismic trace from borehole acoustic impedance
    /// Uses the convolutional model: seismic = reflectivity * wavelet
    /// </summary>
    public static float[] GenerateSyntheticSeismic(BoreholeDataset borehole, float dominantFrequency = 30f, float sampleRateMs = 2.0f)
    {
        Logger.Log($"[BoreholeSeismicIntegration] Generating synthetic seismic for {borehole.Name}");

        // Check if borehole has acoustic impedance data
        if (!borehole.ParameterTracks.TryGetValue("AI", out var aiTrack) &&
            !borehole.ParameterTracks.TryGetValue("Acoustic Impedance", out aiTrack))
        {
            // Try to calculate from Vp and Density if available
            if (borehole.ParameterTracks.TryGetValue("Vp", out var vpTrack) &&
                borehole.ParameterTracks.TryGetValue("Density", out var densityTrack))
            {
                aiTrack = CalculateAcousticImpedance(vpTrack, densityTrack);
            }
            else
            {
                Logger.LogWarning("[BoreholeSeismicIntegration] No acoustic impedance or Vp/Density data found");
                return Array.Empty<float>();
            }
        }

        // Calculate reflectivity coefficients
        var reflectivity = CalculateReflectivity(aiTrack);

        // Generate Ricker wavelet
        var wavelet = GenerateRickerWavelet(dominantFrequency, sampleRateMs);

        // Convolve reflectivity with wavelet
        var synthetic = Convolve(reflectivity, wavelet);

        Logger.Log($"[BoreholeSeismicIntegration] Generated {synthetic.Length} samples");
        return synthetic;
    }

    /// <summary>
    /// Create a pseudo-borehole dataset from a seismic trace
    /// Extracts amplitude envelope and attempts to estimate acoustic properties
    /// </summary>
    public static BoreholeDataset CreateBoreholeFromSeismic(SeismicDataset seismicData, int traceIndex,
        string boreholeName, float x, float y, float elevation = 0)
    {
        Logger.Log($"[BoreholeSeismicIntegration] Creating borehole from seismic trace {traceIndex}");

        if (seismicData.SegyData == null || traceIndex < 0 || traceIndex >= seismicData.GetTraceCount())
        {
            Logger.LogError("[BoreholeSeismicIntegration] Invalid seismic data or trace index");
            return null;
        }

        var trace = seismicData.SegyData.Traces[traceIndex];
        var sampleInterval = seismicData.GetSampleIntervalMs() / 1000.0f; // Convert to seconds
        var numSamples = trace.Samples.Length;

        // Create new borehole
        var borehole = new BoreholeDataset(boreholeName, "")
        {
            X = x,
            Y = y,
            Elevation = elevation,
            TotalDepth = numSamples * sampleInterval * 1500.0f // Assume average velocity of 1500 m/s for depth conversion
        };

        // Create seismic amplitude track
        var amplitudeTrack = new ParameterTrack
        {
            Name = "Seismic Amplitude",
            Unit = "amplitude",
            Color = new Vector4(1, 0, 0, 1),
            IsVisible = true
        };

        // Sample seismic data at regular depth intervals
        var depthInterval = borehole.TotalDepth / numSamples;
        for (int i = 0; i < numSamples; i++)
        {
            var depth = i * depthInterval;
            var amplitude = trace.Samples[i];
            amplitudeTrack.Values.Add(new ParameterValue { Depth = depth, Value = amplitude });
        }

        borehole.ParameterTracks["Seismic Amplitude"] = amplitudeTrack;

        // Create envelope track (instantaneous amplitude)
        var envelope = CalculateEnvelope(trace.Samples);
        var envelopeTrack = new ParameterTrack
        {
            Name = "Seismic Envelope",
            Unit = "amplitude",
            Color = new Vector4(0, 1, 0, 1),
            IsVisible = true
        };

        for (int i = 0; i < numSamples; i++)
        {
            var depth = i * depthInterval;
            envelopeTrack.Values.Add(new ParameterValue { Depth = depth, Value = envelope[i] });
        }

        borehole.ParameterTracks["Seismic Envelope"] = envelopeTrack;

        Logger.Log($"[BoreholeSeismicIntegration] Created borehole with {numSamples} samples, depth = {borehole.TotalDepth:F2} m");
        return borehole;
    }

    /// <summary>
    /// Tie a borehole to a seismic section by finding the best correlation
    /// Returns the trace index with highest correlation
    /// </summary>
    public static int TieWellToSeismic(BoreholeDataset borehole, SeismicDataset seismic, float dominantFrequency = 30f)
    {
        Logger.Log($"[BoreholeSeismicIntegration] Tying well {borehole.Name} to seismic");

        var synthetic = GenerateSyntheticSeismic(borehole, dominantFrequency);
        if (synthetic.Length == 0)
        {
            Logger.LogWarning("[BoreholeSeismicIntegration] Could not generate synthetic seismic");
            return -1;
        }

        // Find trace with best correlation
        int bestTrace = -1;
        float bestCorrelation = float.MinValue;

        for (int i = 0; i < seismic.GetTraceCount(); i++)
        {
            var trace = seismic.SegyData.Traces[i];
            var correlation = CalculateCorrelation(synthetic, trace.Samples);

            if (correlation > bestCorrelation)
            {
                bestCorrelation = correlation;
                bestTrace = i;
            }
        }

        Logger.Log($"[BoreholeSeismicIntegration] Best correlation: {bestCorrelation:F3} at trace {bestTrace}");
        return bestTrace;
    }

    #region Private Helper Methods

    private static ParameterTrack CalculateAcousticImpedance(ParameterTrack vpTrack, ParameterTrack densityTrack)
    {
        var aiTrack = new ParameterTrack
        {
            Name = "Acoustic Impedance",
            Unit = "kg/m2s",
            Color = new Vector4(0, 0.5f, 1, 1),
            IsVisible = true
        };

        // Match depths and multiply Vp * Density
        foreach (var vpValue in vpTrack.Values)
        {
            var densityValue = densityTrack.Values.FirstOrDefault(d => Math.Abs(d.Depth - vpValue.Depth) < 0.01f);
            if (densityValue != null)
            {
                var ai = vpValue.Value * densityValue.Value;
                aiTrack.Values.Add(new ParameterValue { Depth = vpValue.Depth, Value = ai });
            }
        }

        return aiTrack;
    }

    private static float[] CalculateReflectivity(ParameterTrack aiTrack)
    {
        var reflectivity = new float[aiTrack.Values.Count - 1];

        for (int i = 0; i < aiTrack.Values.Count - 1; i++)
        {
            var ai1 = aiTrack.Values[i].Value;
            var ai2 = aiTrack.Values[i + 1].Value;

            // Reflection coefficient: RC = (AI2 - AI1) / (AI2 + AI1)
            if (Math.Abs(ai1 + ai2) > 1e-6)
            {
                reflectivity[i] = (ai2 - ai1) / (ai2 + ai1);
            }
        }

        return reflectivity;
    }

    private static float[] GenerateRickerWavelet(float dominantFreq, float sampleRateMs)
    {
        var dt = sampleRateMs / 1000.0f; // Convert to seconds
        var length = (int)(0.128f / dt); // 128ms wavelet
        var wavelet = new float[length];
        var center = length / 2;

        for (int i = 0; i < length; i++)
        {
            var t = (i - center) * dt;
            var pf2 = Math.PI * Math.PI * dominantFreq * dominantFreq;
            var t2 = t * t;

            // Ricker wavelet: (1 - 2*pi^2*f^2*t^2) * exp(-pi^2*f^2*t^2)
            wavelet[i] = (float)((1.0 - 2.0 * pf2 * t2) * Math.Exp(-pf2 * t2));
        }

        // Normalize
        var maxAbs = wavelet.Max(Math.Abs);
        if (maxAbs > 0)
        {
            for (int i = 0; i < length; i++)
                wavelet[i] /= maxAbs;
        }

        return wavelet;
    }

    private static float[] Convolve(float[] signal, float[] kernel)
    {
        var outputLength = signal.Length + kernel.Length - 1;
        var result = new float[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            float sum = 0;
            for (int j = 0; j < kernel.Length; j++)
            {
                var signalIndex = i - j;
                if (signalIndex >= 0 && signalIndex < signal.Length)
                {
                    sum += signal[signalIndex] * kernel[j];
                }
            }
            result[i] = sum;
        }

        return result;
    }

    private static float[] CalculateEnvelope(float[] signal)
    {
        // Calculate Hilbert transform approximation for envelope
        var envelope = new float[signal.Length];

        for (int i = 0; i < signal.Length; i++)
        {
            // Simple envelope: use moving window RMS
            int windowSize = 5;
            float sumSquares = 0;
            int count = 0;

            for (int j = Math.Max(0, i - windowSize); j < Math.Min(signal.Length, i + windowSize + 1); j++)
            {
                sumSquares += signal[j] * signal[j];
                count++;
            }

            envelope[i] = (float)Math.Sqrt(sumSquares / count);
        }

        return envelope;
    }

    private static float CalculateCorrelation(float[] signal1, float[] signal2)
    {
        var minLength = Math.Min(signal1.Length, signal2.Length);

        float sum1 = 0, sum2 = 0, sum12 = 0, sum1Sq = 0, sum2Sq = 0;

        for (int i = 0; i < minLength; i++)
        {
            sum1 += signal1[i];
            sum2 += signal2[i];
            sum12 += signal1[i] * signal2[i];
            sum1Sq += signal1[i] * signal1[i];
            sum2Sq += signal2[i] * signal2[i];
        }

        var n = minLength;
        var numerator = n * sum12 - sum1 * sum2;
        var denominator = Math.Sqrt((n * sum1Sq - sum1 * sum1) * (n * sum2Sq - sum2 * sum2));

        if (Math.Abs(denominator) < 1e-10)
            return 0;

        return (float)(numerator / denominator);
    }

    #endregion
}
