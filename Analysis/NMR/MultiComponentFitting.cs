// GeoscientistToolkit/Analysis/NMR/MultiComponentFitting.cs

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.NMR;

/// <summary>
///     Automatic multi-component peak fitting for T2 distributions.
///     Identifies and fits multiple Gaussian peaks representing different pore populations.
/// </summary>
public static class MultiComponentFitting
{
    /// <summary>
    ///     Automatically detects and fits multiple peaks in T2 distribution
    /// </summary>
    public static FittingResults FitMultipleComponents(
        double[] t2Bins,
        double[] t2Distribution,
        int maxComponents = 5,
        double minPeakHeight = 0.01)
    {
        Logger.Log("[MultiComponentFitting] Starting automatic peak detection...");

        // Convert to log space for better peak detection
        var logT2 = t2Bins.Select(Math.Log10).ToArray();

        // Detect peaks using derivative analysis
        var detectedPeaks = DetectPeaks(logT2, t2Distribution, minPeakHeight);
        Logger.Log($"[MultiComponentFitting] Detected {detectedPeaks.Count} initial peaks");

        // Limit to maxComponents
        if (detectedPeaks.Count > maxComponents)
        {
            detectedPeaks = detectedPeaks.OrderByDescending(p => p.Amplitude).Take(maxComponents).ToList();
            Logger.Log($"[MultiComponentFitting] Limited to {maxComponents} largest peaks");
        }

        if (detectedPeaks.Count == 0)
        {
            Logger.LogWarning("[MultiComponentFitting] No peaks detected");
            return new FittingResults
            {
                Peaks = new List<PeakComponent>(),
                FittedCurve = new double[t2Distribution.Length],
                Residuals = t2Distribution.ToArray(),
                RSquared = 0,
                RMSE = CalculateRMSE(t2Distribution, new double[t2Distribution.Length])
            };
        }

        // Refine peaks using Levenberg-Marquardt optimization
        var results = RefinePeaks(logT2, t2Distribution, detectedPeaks);

        // Assign labels to peaks based on T2 values
        AssignPeakLabels(results.Peaks);

        // Calculate goodness of fit
        CalculateGoodnessOfFit(t2Distribution, results);

        Logger.Log($"[MultiComponentFitting] Fitted {results.Peaks.Count} components, R² = {results.RSquared:F4}");

        return results;
    }

    private static List<PeakComponent> DetectPeaks(double[] logT2, double[] distribution, double minHeight)
    {
        var peaks = new List<PeakComponent>();

        // Smooth the distribution to reduce noise
        var smoothed = GaussianSmooth(distribution, 2);

        // Find local maxima
        for (var i = 2; i < smoothed.Length - 2; i++)
            if (smoothed[i] > smoothed[i - 1] && smoothed[i] > smoothed[i + 1] &&
                smoothed[i] > smoothed[i - 2] && smoothed[i] > smoothed[i + 2] &&
                smoothed[i] > minHeight)
            {
                // Estimate initial width using FWHM
                var width = EstimatePeakWidth(logT2, smoothed, i);

                peaks.Add(new PeakComponent
                {
                    Center = Math.Pow(10, logT2[i]),
                    Amplitude = smoothed[i],
                    Width = width
                });
            }

        return peaks;
    }

    private static double EstimatePeakWidth(double[] logT2, double[] distribution, int peakIndex)
    {
        var halfMax = distribution[peakIndex] / 2.0;

        // Find left half-max
        var left = peakIndex;
        while (left > 0 && distribution[left] > halfMax) left--;

        // Find right half-max
        var right = peakIndex;
        while (right < distribution.Length - 1 && distribution[right] > halfMax) right++;

        // FWHM to sigma: sigma ≈ FWHM / 2.355
        var fwhm = logT2[right] - logT2[left];
        return fwhm / 2.355;
    }

    private static double[] GaussianSmooth(double[] data, double sigma)
    {
        var kernelSize = (int)(6 * sigma) | 1; // Ensure odd
        var halfSize = kernelSize / 2;
        var kernel = new double[kernelSize];

        // Generate Gaussian kernel
        var sum = 0.0;
        for (var i = 0; i < kernelSize; i++)
        {
            var x = i - halfSize;
            kernel[i] = Math.Exp(-0.5 * x * x / (sigma * sigma));
            sum += kernel[i];
        }

        // Normalize kernel
        for (var i = 0; i < kernelSize; i++) kernel[i] /= sum;

        // Apply convolution
        var smoothed = new double[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            var value = 0.0;
            var weightSum = 0.0;

            for (var j = 0; j < kernelSize; j++)
            {
                var idx = i + j - halfSize;
                if (idx >= 0 && idx < data.Length)
                {
                    value += data[idx] * kernel[j];
                    weightSum += kernel[j];
                }
            }

            smoothed[i] = value / weightSum;
        }

        return smoothed;
    }

    private static FittingResults RefinePeaks(double[] logT2, double[] data, List<PeakComponent> initialPeaks)
    {
        // Levenberg-Marquardt optimization
        var maxIterations = 100;
        var tolerance = 1e-6;
        var lambda = 0.01;

        var peaks = initialPeaks.Select(p => new PeakComponent
        {
            Center = p.Center,
            Amplitude = p.Amplitude,
            Width = p.Width
        }).ToList();

        var bestRMSE = double.MaxValue;
        var iterationsUsed = 0;

        for (var iter = 0; iter < maxIterations; iter++)
        {
            // Compute current fit
            var fitted = EvaluateModel(logT2, peaks);
            var residuals = new double[data.Length];
            for (var i = 0; i < data.Length; i++) residuals[i] = data[i] - fitted[i];

            var rmse = CalculateRMSE(data, fitted);

            if (rmse < bestRMSE)
            {
                bestRMSE = rmse;
                iterationsUsed = iter + 1;
            }

            if (iter > 0 && Math.Abs(rmse - bestRMSE) < tolerance) break; // Converged

            // Compute Jacobian and update parameters
            var jacobian = ComputeJacobian(logT2, peaks);
            var delta = SolveLevenbergMarquardt(jacobian, residuals, lambda);

            // Update parameters
            var paramIndex = 0;
            foreach (var peak in peaks)
            {
                peak.Amplitude = Math.Max(0, peak.Amplitude + delta[paramIndex++]);
                peak.Center *= Math.Pow(10, delta[paramIndex++]); // Update in log space
                peak.Width = Math.Max(0.01, peak.Width + delta[paramIndex++]);
            }

            // Adapt lambda
            lambda *= 0.9;
        }

        // Final evaluation
        var finalFitted = EvaluateModel(logT2, peaks);
        var finalResiduals = new double[data.Length];
        for (var i = 0; i < data.Length; i++) finalResiduals[i] = data[i] - finalFitted[i];

        // Calculate peak areas
        foreach (var peak in peaks) peak.Area = peak.Amplitude * peak.Width * Math.Sqrt(2 * Math.PI);

        // Normalize areas to sum to 1
        var totalArea = peaks.Sum(p => p.Area);
        if (totalArea > 0)
            foreach (var peak in peaks)
                peak.Area /= totalArea;

        return new FittingResults
        {
            Peaks = peaks,
            FittedCurve = finalFitted,
            Residuals = finalResiduals,
            IterationsUsed = iterationsUsed
        };
    }

    private static double[] EvaluateModel(double[] logT2, List<PeakComponent> peaks)
    {
        var result = new double[logT2.Length];

        foreach (var peak in peaks)
        {
            var logCenter = Math.Log10(peak.Center);
            for (var i = 0; i < logT2.Length; i++)
            {
                var x = (logT2[i] - logCenter) / peak.Width;
                result[i] += peak.Amplitude * Math.Exp(-0.5 * x * x);
            }
        }

        return result;
    }

    private static double[,] ComputeJacobian(double[] logT2, List<PeakComponent> peaks)
    {
        var nPoints = logT2.Length;
        var nParams = peaks.Count * 3; // amplitude, center, width per peak
        var jacobian = new double[nPoints, nParams];

        var paramIndex = 0;
        foreach (var peak in peaks)
        {
            var logCenter = Math.Log10(peak.Center);

            for (var i = 0; i < nPoints; i++)
            {
                var x = (logT2[i] - logCenter) / peak.Width;
                var gaussian = Math.Exp(-0.5 * x * x);

                // Derivative w.r.t. amplitude
                jacobian[i, paramIndex] = gaussian;

                // Derivative w.r.t. center (in log space)
                jacobian[i, paramIndex + 1] = peak.Amplitude * gaussian * x / peak.Width;

                // Derivative w.r.t. width
                jacobian[i, paramIndex + 2] = peak.Amplitude * gaussian * x * x / peak.Width;
            }

            paramIndex += 3;
        }

        return jacobian;
    }

    private static double[] SolveLevenbergMarquardt(double[,] jacobian, double[] residuals, double lambda)
    {
        var nParams = jacobian.GetLength(1);
        var jTj = new double[nParams, nParams];
        var jTr = new double[nParams];

        // Compute J^T * J and J^T * r
        for (var i = 0; i < nParams; i++)
        {
            for (var j = 0; j < nParams; j++)
            for (var k = 0; k < residuals.Length; k++)
                jTj[i, j] += jacobian[k, i] * jacobian[k, j];

            for (var k = 0; k < residuals.Length; k++) jTr[i] += jacobian[k, i] * residuals[k];
        }

        // Add damping: (J^T * J + λI)
        for (var i = 0; i < nParams; i++) jTj[i, i] *= 1.0 + lambda;

        // Solve: (J^T * J + λI) * δ = J^T * r
        return SolveLinearSystem(jTj, jTr);
    }

    private static double[] SolveLinearSystem(double[,] A, double[] b)
    {
        var n = b.Length;
        var x = new double[n];

        // Simple Gauss-Seidel iteration
        for (var iter = 0; iter < 50; iter++)
        for (var i = 0; i < n; i++)
        {
            var sum = b[i];
            for (var j = 0; j < n; j++)
                if (j != i)
                    sum -= A[i, j] * x[j];

            x[i] = sum / Math.Max(A[i, i], 1e-10);
        }

        return x;
    }

    private static void AssignPeakLabels(List<PeakComponent> peaks)
    {
        // Sort by T2 value
        peaks = peaks.OrderBy(p => p.Center).ToList();

        var labels = new[] { "Micropores", "Mesopores", "Macropores", "Large Pores", "Fractures" };

        for (var i = 0; i < peaks.Count; i++)
            if (i < labels.Length)
                peaks[i].Label = labels[i];
            else
                peaks[i].Label = $"Component {i + 1}";
    }

    private static void CalculateGoodnessOfFit(double[] data, FittingResults results)
    {
        var meanData = data.Average();
        var ssTot = data.Sum(d => Math.Pow(d - meanData, 2));
        var ssRes = results.Residuals.Sum(r => r * r);

        results.RSquared = 1.0 - ssRes / Math.Max(ssTot, 1e-10);
        results.RMSE = Math.Sqrt(ssRes / data.Length);
    }

    private static double CalculateRMSE(double[] data, double[] fitted)
    {
        var sum = 0.0;
        for (var i = 0; i < data.Length; i++) sum += Math.Pow(data[i] - fitted[i], 2);
        return Math.Sqrt(sum / data.Length);
    }

    /// <summary>
    ///     Exports peak fitting results to CSV
    /// </summary>
    public static void ExportFittingResults(FittingResults results, double[] t2Bins, string filePath)
    {
        using var writer = new StreamWriter(filePath);

        writer.WriteLine("# Multi-Component Peak Fitting Results");
        writer.WriteLine($"# Number of components: {results.Peaks.Count}");
        writer.WriteLine($"# R²: {results.RSquared:F6}");
        writer.WriteLine($"# RMSE: {results.RMSE:F6}");
        writer.WriteLine();

        writer.WriteLine("Component,Label,Center_T2_ms,Amplitude,Width_log,Area_Fraction");
        for (var i = 0; i < results.Peaks.Count; i++)
        {
            var peak = results.Peaks[i];
            writer.WriteLine($"{i + 1},{peak.Label},{peak.Center:F3},{peak.Amplitude:F6}," +
                             $"{peak.Width:F4},{peak.Area:F4}");
        }

        writer.WriteLine();
        writer.WriteLine("T2_ms,Data,Fitted,Residual");
        for (var i = 0; i < t2Bins.Length; i++)
            writer.WriteLine($"{t2Bins[i]:F3},{results.FittedCurve[i]:F6},{results.Residuals[i]:F6}");

        Logger.Log($"[MultiComponentFitting] Results exported to {filePath}");
    }

    /// <summary>
    ///     Represents a single peak component in the T2 distribution
    /// </summary>
    public class PeakComponent
    {
        public double Center { get; set; } // Peak center (T2 in ms)
        public double Amplitude { get; set; } // Peak height
        public double Width { get; set; } // Peak width (standard deviation in log space)
        public double Area { get; set; } // Integrated area (fraction of total signal)
        public string Label { get; set; } // e.g., "Micropores", "Macropores"
    }

    /// <summary>
    ///     Results from multi-component fitting
    /// </summary>
    public class FittingResults
    {
        public List<PeakComponent> Peaks { get; set; } = new();
        public double[] FittedCurve { get; set; }
        public double[] Residuals { get; set; }
        public double RSquared { get; set; }
        public double RMSE { get; set; }
        public int IterationsUsed { get; set; }
    }
}