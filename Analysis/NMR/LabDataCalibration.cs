// GeoscientistToolkit/Analysis/NMR/LabDataCalibration.cs

using System.Globalization;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.NMR;

/// <summary>
///     Imports and calibrates simulation results against laboratory NMR measurements.
///     Supports common NMR file formats and provides comparison metrics.
/// </summary>
public static class LabDataCalibration
{
    /// <summary>
    ///     Imports laboratory NMR data from CSV file
    /// </summary>
    public static LabNMRData ImportFromCSV(string filePath)
    {
        Logger.Log($"[LabDataCalibration] Importing lab data from: {filePath}");

        var data = new LabNMRData
        {
            SampleName = Path.GetFileNameWithoutExtension(filePath)
        };

        var lines = File.ReadAllLines(filePath);
        var section = "";
        var timePoints = new List<double>();
        var magnetization = new List<double>();
        var t2Bins = new List<double>();
        var t2Dist = new List<double>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            {
                // Metadata or comment
                if (line.StartsWith("# ")) ParseMetadata(line.Substring(2), data);
                continue;
            }

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line.Trim('[', ']').ToLower();
                continue;
            }

            var parts = line.Split(new[] { ',', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            switch (section)
            {
                case "decay":
                case "magnetization":
                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mag))
                    {
                        timePoints.Add(time);
                        magnetization.Add(mag);
                    }

                    break;

                case "t2":
                case "t2distribution":
                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t2) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amp))
                    {
                        t2Bins.Add(t2);
                        t2Dist.Add(amp);
                    }

                    break;
            }
        }

        data.TimePoints = timePoints.ToArray();
        data.Magnetization = magnetization.ToArray();
        data.T2Bins = t2Bins.ToArray();
        data.T2Distribution = t2Dist.ToArray();

        Logger.Log($"[LabDataCalibration] Imported: {timePoints.Count} decay points, {t2Bins.Count} T2 bins");

        return data;
    }

    /// <summary>
    ///     Imports NMR data from Bruker format (common in research)
    /// </summary>
    public static LabNMRData ImportFromBruker(string filePath)
    {
        Logger.Log($"[LabDataCalibration] Importing Bruker format from: {filePath}");

        // Bruker format: binary data with parameter files
        var data = new LabNMRData
        {
            SampleName = Path.GetFileNameWithoutExtension(filePath),
            InstrumentType = "Bruker"
        };

        // Read acqus file for parameters
        var directory = Path.GetDirectoryName(filePath);
        var acqusFile = Path.Combine(directory, "acqus");

        if (File.Exists(acqusFile))
        {
            var acqusLines = File.ReadAllLines(acqusFile);
            foreach (var line in acqusLines)
                if (line.StartsWith("##$TE="))
                    data.Temperature = double.Parse(line.Substring(6)) - 273.15; // K to °C
            // Parse other parameters as needed
        }

        // For now, return placeholder - full Bruker parser would be complex
        Logger.LogWarning("[LabDataCalibration] Bruker format import is simplified - use CSV for full support");

        return data;
    }

    /// <summary>
    ///     Calibrates simulation results against laboratory data
    /// </summary>
    public static CalibrationResults CalibrateWithLabData(NMRResults simulation, LabNMRData labData)
    {
        Logger.Log("[LabDataCalibration] Calibrating simulation against lab data...");

        var results = new CalibrationResults
        {
            LabData = labData,
            SimulationData = simulation
        };

        // Compare decay curves
        if (labData.Magnetization != null && labData.Magnetization.Length > 0)
            results.DecayCurveCorrelation = ComputeCorrelation(
                InterpolateData(labData.TimePoints, labData.Magnetization, simulation.TimePoints),
                simulation.Magnetization);

        // Compare T2 distributions
        if (labData.T2Distribution != null && labData.T2Distribution.Length > 0)
            results.T2DistributionCorrelation = ComputeCorrelation(
                InterpolateData(labData.T2Bins, labData.T2Distribution, simulation.T2HistogramBins),
                simulation.T2Histogram);

        // Compare statistical metrics
        var labMeanT2 = ComputeMeanT2(labData.T2Bins, labData.T2Distribution);
        if (labMeanT2 > 0 && simulation.MeanT2 > 0)
            results.MeanT2Error = Math.Abs(simulation.MeanT2 - labMeanT2) / labMeanT2;

        var labPeakT2 = FindPeakT2(labData.T2Bins, labData.T2Distribution);
        if (labPeakT2 > 0 && simulation.T2PeakValue > 0)
            results.PeakT2Error = Math.Abs(simulation.T2PeakValue - labPeakT2) / labPeakT2;

        // Compute calibration factors
        if (labMeanT2 > 0 && simulation.MeanT2 > 0) results.RelaxivityScaleFactor = simulation.MeanT2 / labMeanT2;

        // Overall agreement score
        var weights = new[] { 0.3, 0.4, 0.15, 0.15 }; // Decay, T2, Mean, Peak
        var scores = new[]
        {
            results.DecayCurveCorrelation,
            results.T2DistributionCorrelation,
            1.0 - Math.Min(1.0, results.MeanT2Error),
            1.0 - Math.Min(1.0, results.PeakT2Error)
        };

        results.OverallAgreement = 0;
        for (var i = 0; i < weights.Length; i++) results.OverallAgreement += weights[i] * Math.Max(0, scores[i]);

        // Generate recommendations
        results.Recommendations = GenerateRecommendations(results);

        Logger.Log($"[LabDataCalibration] Calibration complete. Overall agreement: {results.OverallAgreement:P1}");

        return results;
    }

    private static double[] InterpolateData(double[] xOld, double[] yOld, double[] xNew)
    {
        var yNew = new double[xNew.Length];

        for (var i = 0; i < xNew.Length; i++)
        {
            var x = xNew[i];

            // Find interpolation points
            var idx = Array.BinarySearch(xOld, x);
            if (idx >= 0)
            {
                yNew[i] = yOld[idx];
            }
            else
            {
                idx = ~idx; // Get insertion point
                if (idx == 0)
                {
                    yNew[i] = yOld[0];
                }
                else if (idx >= xOld.Length)
                {
                    yNew[i] = yOld[xOld.Length - 1];
                }
                else
                {
                    // Linear interpolation
                    var x0 = xOld[idx - 1];
                    var x1 = xOld[idx];
                    var y0 = yOld[idx - 1];
                    var y1 = yOld[idx];
                    var t = (x - x0) / (x1 - x0);
                    yNew[i] = y0 + t * (y1 - y0);
                }
            }
        }

        return yNew;
    }

    private static double ComputeCorrelation(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length == 0) return 0;

        var meanX = x.Average();
        var meanY = y.Average();

        var covariance = 0.0;
        var varX = 0.0;
        var varY = 0.0;

        for (var i = 0; i < x.Length; i++)
        {
            var dx = x[i] - meanX;
            var dy = y[i] - meanY;
            covariance += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        if (varX == 0 || varY == 0) return 0;

        return covariance / Math.Sqrt(varX * varY);
    }

    private static double ComputeMeanT2(double[] t2Bins, double[] distribution)
    {
        if (t2Bins == null || distribution == null || t2Bins.Length == 0) return 0;

        var sum = 0.0;
        var weight = 0.0;

        for (var i = 0; i < Math.Min(t2Bins.Length, distribution.Length); i++)
        {
            sum += t2Bins[i] * distribution[i];
            weight += distribution[i];
        }

        return weight > 0 ? sum / weight : 0;
    }

    private static double FindPeakT2(double[] t2Bins, double[] distribution)
    {
        if (t2Bins == null || distribution == null || t2Bins.Length == 0) return 0;

        var maxIdx = 0;
        var maxVal = distribution[0];

        for (var i = 1; i < Math.Min(t2Bins.Length, distribution.Length); i++)
            if (distribution[i] > maxVal)
            {
                maxVal = distribution[i];
                maxIdx = i;
            }

        return t2Bins[maxIdx];
    }

    private static void ParseMetadata(string line, LabNMRData data)
    {
        var parts = line.Split(new[] { ':', '=' }, 2);
        if (parts.Length == 2)
        {
            var key = parts[0].Trim().ToLower();
            var value = parts[1].Trim();

            switch (key)
            {
                case "sample":
                case "sample name":
                    data.SampleName = value;
                    break;
                case "instrument":
                case "instrument type":
                    data.InstrumentType = value;
                    break;
                case "temperature":
                    if (double.TryParse(value.Split(' ')[0], out var temp)) data.Temperature = temp;
                    break;
                default:
                    data.Metadata[key] = value;
                    break;
            }
        }
    }

    private static string[] GenerateRecommendations(CalibrationResults results)
    {
        var recommendations = new List<string>();

        if (results.OverallAgreement < 0.5)
            recommendations.Add("Poor agreement with lab data. Consider:");
        else if (results.OverallAgreement < 0.8)
            recommendations.Add("Moderate agreement. Improvements possible:");
        else
            recommendations.Add("Good agreement with lab data!");

        if (results.MeanT2Error > 0.3)
        {
            if (results.RelaxivityScaleFactor > 1.5)
                recommendations.Add("- Decrease surface relaxivities (simulation T2 too long)");
            else if (results.RelaxivityScaleFactor < 0.7)
                recommendations.Add("- Increase surface relaxivities (simulation T2 too short)");
        }

        if (results.DecayCurveCorrelation < 0.7)
        {
            recommendations.Add("- Check diffusion coefficient matches fluid type");
            recommendations.Add("- Verify temperature matches lab conditions");
        }

        if (results.T2DistributionCorrelation < 0.7)
        {
            recommendations.Add("- Review segmentation quality (pore space vs. matrix)");
            recommendations.Add("- Check voxel size calibration");
        }

        if (results.OverallAgreement > 0.85) recommendations.Add("Simulation parameters are well-calibrated!");

        return recommendations.ToArray();
    }

    /// <summary>
    ///     Exports calibration results to report
    /// </summary>
    public static void ExportCalibrationReport(CalibrationResults results, string filePath)
    {
        using var writer = new StreamWriter(filePath);

        writer.WriteLine("NMR CALIBRATION REPORT");
        writer.WriteLine("======================");
        writer.WriteLine();
        writer.WriteLine($"Lab Sample: {results.LabData.SampleName}");
        writer.WriteLine($"Instrument: {results.LabData.InstrumentType}");
        writer.WriteLine($"Temperature: {results.LabData.Temperature:F1} °C");
        writer.WriteLine();
        writer.WriteLine("COMPARISON METRICS");
        writer.WriteLine("------------------");
        writer.WriteLine($"Decay Curve Correlation: {results.DecayCurveCorrelation:F4}");
        writer.WriteLine($"T2 Distribution Correlation: {results.T2DistributionCorrelation:F4}");
        writer.WriteLine($"Mean T2 Error: {results.MeanT2Error:P1}");
        writer.WriteLine($"Peak T2 Error: {results.PeakT2Error:P1}");
        writer.WriteLine();
        writer.WriteLine($"Overall Agreement: {results.OverallAgreement:P1}");
        writer.WriteLine();
        writer.WriteLine("CALIBRATION FACTORS");
        writer.WriteLine("-------------------");
        writer.WriteLine($"Relaxivity Scale: {results.RelaxivityScaleFactor:F3}");
        writer.WriteLine($"Porosity Scale: {results.PorosityScaleFactor:F3}");
        writer.WriteLine();
        writer.WriteLine("RECOMMENDATIONS");
        writer.WriteLine("---------------");
        foreach (var rec in results.Recommendations) writer.WriteLine(rec);

        Logger.Log($"[LabDataCalibration] Report exported to {filePath}");
    }

    /// <summary>
    ///     Creates a template CSV file for lab data import
    /// </summary>
    public static void CreateImportTemplate(string filePath)
    {
        using var writer = new StreamWriter(filePath);

        writer.WriteLine("# Laboratory NMR Data Import Template");
        writer.WriteLine("# Lines starting with # are comments");
        writer.WriteLine("# Metadata (optional):");
        writer.WriteLine("# Sample Name: Sample-001");
        writer.WriteLine("# Instrument: Bruker Minispec mq20");
        writer.WriteLine("# Temperature: 25 °C");
        writer.WriteLine("# Date: 2026-01-10");
        writer.WriteLine();
        writer.WriteLine("[Decay]");
        writer.WriteLine("# Time (ms), Magnetization (normalized)");
        writer.WriteLine("0.0, 1.000");
        writer.WriteLine("0.5, 0.950");
        writer.WriteLine("1.0, 0.900");
        writer.WriteLine("# ... add more points");
        writer.WriteLine();
        writer.WriteLine("[T2Distribution]");
        writer.WriteLine("# T2 (ms), Amplitude (normalized)");
        writer.WriteLine("0.1, 0.01");
        writer.WriteLine("1.0, 0.15");
        writer.WriteLine("10.0, 0.45");
        writer.WriteLine("100.0, 0.30");
        writer.WriteLine("# ... add more points");

        Logger.Log($"[LabDataCalibration] Template created: {filePath}");
    }

    /// <summary>
    ///     Laboratory NMR data
    /// </summary>
    public class LabNMRData
    {
        public string SampleName { get; set; }
        public string InstrumentType { get; set; }
        public double Temperature { get; set; } // °C
        public double[] TimePoints { get; set; } // ms
        public double[] Magnetization { get; set; }
        public double[] T2Distribution { get; set; }
        public double[] T2Bins { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    ///     Calibration results comparing simulation vs. lab data
    /// </summary>
    public class CalibrationResults
    {
        public LabNMRData LabData { get; set; }
        public NMRResults SimulationData { get; set; }

        // Comparison metrics
        public double DecayCurveCorrelation { get; set; }
        public double T2DistributionCorrelation { get; set; }
        public double MeanT2Error { get; set; } // Relative error
        public double PeakT2Error { get; set; }

        // Calibration factors
        public double RelaxivityScaleFactor { get; set; } = 1.0;
        public double PorosityScaleFactor { get; set; } = 1.0;

        // Quality metrics
        public double OverallAgreement { get; set; } // 0-1 score
        public string[] Recommendations { get; set; }
    }
}