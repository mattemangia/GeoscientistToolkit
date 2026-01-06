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

        var data = new LabNMRData
        {
            SampleName = Path.GetFileNameWithoutExtension(filePath),
            InstrumentType = "Bruker"
        };

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
            directory = Path.GetDirectoryName(Path.GetFullPath(filePath));

        // Parse acquisition parameters from acqus file
        var acqusFile = Path.Combine(directory, "acqus");
        double dwellTime = 1.0; // Default dwell time in µs
        int numPoints = 0;
        double spectralWidth = 0;

        if (File.Exists(acqusFile))
        {
            var acqusLines = File.ReadAllLines(acqusFile);
            foreach (var line in acqusLines)
            {
                if (line.StartsWith("##$TE="))
                {
                    if (double.TryParse(line.Substring(6).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
                        data.Temperature = temp - 273.15; // K to °C
                }
                else if (line.StartsWith("##$TD="))
                {
                    if (int.TryParse(line.Substring(6).Trim(), out var td))
                        numPoints = td / 2; // TD is total points (real + imaginary)
                }
                else if (line.StartsWith("##$DW="))
                {
                    if (double.TryParse(line.Substring(6).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dw))
                        dwellTime = dw; // Dwell time in µs
                }
                else if (line.StartsWith("##$SW_h="))
                {
                    if (double.TryParse(line.Substring(8).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sw))
                        spectralWidth = sw;
                }
                else if (line.StartsWith("##$PULPROG="))
                {
                    data.Metadata["PulseProgram"] = line.Substring(11).Trim().Trim('<', '>');
                }
                else if (line.StartsWith("##$NS="))
                {
                    data.Metadata["NumScans"] = line.Substring(6).Trim();
                }
            }
        }

        // Try to load processed data first (pdata/1/1r for real part)
        var pdataPath = Path.Combine(directory, "pdata", "1");
        var proc1rPath = Path.Combine(pdataPath, "1r");
        var proc1iPath = Path.Combine(pdataPath, "1i");

        if (File.Exists(proc1rPath))
        {
            // Read processed spectrum data
            var (t2Bins, t2Dist) = ReadBrukerProcessedData(pdataPath, proc1rPath);
            if (t2Bins != null && t2Dist != null)
            {
                data.T2Bins = t2Bins;
                data.T2Distribution = t2Dist;
                Logger.Log($"[LabDataCalibration] Loaded processed T2 distribution: {t2Bins.Length} bins");
            }
        }

        // Try to load raw FID data
        var fidPath = Path.Combine(directory, "fid");
        if (!File.Exists(fidPath))
            fidPath = Path.Combine(directory, "ser"); // Series file for 2D experiments

        if (File.Exists(fidPath))
        {
            var (timePoints, magnetization) = ReadBrukerFID(fidPath, numPoints, dwellTime);
            if (timePoints != null && magnetization != null)
            {
                data.TimePoints = timePoints;
                data.Magnetization = magnetization;
                Logger.Log($"[LabDataCalibration] Loaded FID data: {timePoints.Length} points");
            }
        }

        // Check if we got any data
        if ((data.TimePoints == null || data.TimePoints.Length == 0) &&
            (data.T2Bins == null || data.T2Bins.Length == 0))
        {
            Logger.LogWarning("[LabDataCalibration] No decay or T2 data found in Bruker dataset");
        }
        else
        {
            Logger.Log($"[LabDataCalibration] Bruker import complete: {data.TimePoints?.Length ?? 0} decay points, {data.T2Bins?.Length ?? 0} T2 bins");
        }

        return data;
    }

    /// <summary>
    ///     Reads Bruker FID (Free Induction Decay) binary data
    /// </summary>
    private static (double[]? timePoints, double[]? magnetization) ReadBrukerFID(string fidPath, int numPoints, double dwellTime)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(fidPath);

            // Bruker FID files are typically 32-bit integers, big-endian
            // Each complex point has real and imaginary parts
            var pointsInFile = fileBytes.Length / 4; // 4 bytes per int32

            if (numPoints <= 0 || numPoints > pointsInFile / 2)
                numPoints = pointsInFile / 2;

            var timePoints = new double[numPoints];
            var magnetization = new double[numPoints];

            using var ms = new MemoryStream(fileBytes);
            using var br = new BinaryReader(ms);

            // Detect endianness by checking if first few values are reasonable
            var testBytes = new byte[4];
            Array.Copy(fileBytes, 0, testBytes, 0, 4);
            var littleEndian = BitConverter.ToInt32(testBytes, 0);
            Array.Reverse(testBytes);
            var bigEndian = BitConverter.ToInt32(testBytes, 0);

            bool useBigEndian = Math.Abs(bigEndian) < Math.Abs(littleEndian) * 10;

            for (int i = 0; i < numPoints; i++)
            {
                // Time in milliseconds
                timePoints[i] = i * dwellTime / 1000.0;

                // Read real part (skip imaginary for magnitude calculation)
                var realBytes = br.ReadBytes(4);
                var imagBytes = br.ReadBytes(4);

                if (useBigEndian)
                {
                    Array.Reverse(realBytes);
                    Array.Reverse(imagBytes);
                }

                int realPart = BitConverter.ToInt32(realBytes, 0);
                int imagPart = BitConverter.ToInt32(imagBytes, 0);

                // Calculate magnitude
                magnetization[i] = Math.Sqrt((double)realPart * realPart + (double)imagPart * imagPart);
            }

            // Normalize magnetization to 0-1
            var maxMag = magnetization.Max();
            if (maxMag > 0)
                for (int i = 0; i < magnetization.Length; i++)
                    magnetization[i] /= maxMag;

            return (timePoints, magnetization);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[LabDataCalibration] Failed to read Bruker FID: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    ///     Reads Bruker processed spectrum data
    /// </summary>
    private static (double[]? t2Bins, double[]? distribution) ReadBrukerProcessedData(string pdataPath, string dataPath)
    {
        try
        {
            // Read processing parameters
            var procsPath = Path.Combine(pdataPath, "procs");
            int si = 1024; // Default size
            double sf = 1.0; // Spectrometer frequency
            double offset = 0;
            double sw = 1000; // Spectral width in Hz

            if (File.Exists(procsPath))
            {
                var procsLines = File.ReadAllLines(procsPath);
                foreach (var line in procsLines)
                {
                    if (line.StartsWith("##$SI="))
                        int.TryParse(line.Substring(6).Trim(), out si);
                    else if (line.StartsWith("##$SF="))
                        double.TryParse(line.Substring(6).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out sf);
                    else if (line.StartsWith("##$OFFSET="))
                        double.TryParse(line.Substring(10).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
                    else if (line.StartsWith("##$SW_p="))
                        double.TryParse(line.Substring(8).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out sw);
                }
            }

            // Read binary spectrum data
            var fileBytes = File.ReadAllBytes(dataPath);
            var numPoints = Math.Min(si, fileBytes.Length / 4);

            var t2Bins = new double[numPoints];
            var distribution = new double[numPoints];

            using var ms = new MemoryStream(fileBytes);
            using var br = new BinaryReader(ms);

            // For T2 relaxometry, convert frequency axis to T2 time
            // T2 bins are typically logarithmically spaced
            double t2Min = 0.1; // ms
            double t2Max = 10000; // ms
            double logStep = (Math.Log10(t2Max) - Math.Log10(t2Min)) / (numPoints - 1);

            for (int i = 0; i < numPoints; i++)
            {
                // Create logarithmic T2 bins
                t2Bins[i] = Math.Pow(10, Math.Log10(t2Min) + i * logStep);

                // Read spectrum intensity
                var bytes = br.ReadBytes(4);
                distribution[i] = Math.Abs(BitConverter.ToInt32(bytes, 0));
            }

            // Normalize distribution
            var maxDist = distribution.Max();
            if (maxDist > 0)
                for (int i = 0; i < distribution.Length; i++)
                    distribution[i] /= maxDist;

            return (t2Bins, distribution);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[LabDataCalibration] Failed to read processed data: {ex.Message}");
            return (null, null);
        }
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