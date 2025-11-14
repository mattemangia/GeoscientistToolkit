// GeoscientistToolkit/Data/PhysicoChem/ParameterSweep.cs
//
// Parameter sweep configuration for sensitivity analysis and optimization

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Configuration for parameter sweep experiments
/// </summary>
public class ParameterSweepConfig
{
    [JsonProperty]
    public List<ParameterRange> Parameters { get; set; } = new();

    [JsonProperty]
    public SweepType Type { get; set; } = SweepType.FullFactorial;

    [JsonProperty]
    public int SamplesPerParameter { get; set; } = 10;

    [JsonProperty]
    public List<string> OutputMetrics { get; set; } = new();

    [JsonProperty]
    public bool ParallelExecution { get; set; } = true;

    [JsonProperty]
    public int MaxParallelRuns { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Generate all parameter combinations for sweep
    /// </summary>
    public List<Dictionary<string, double>> GenerateCombinations()
    {
        switch (Type)
        {
            case SweepType.FullFactorial:
                return GenerateFullFactorial();

            case SweepType.LatinHypercube:
                return GenerateLatinHypercube();

            case SweepType.RandomSampling:
                return GenerateRandomSamples();

            case SweepType.SobolSequence:
                return GenerateSobolSequence();

            default:
                return GenerateFullFactorial();
        }
    }

    private List<Dictionary<string, double>> GenerateFullFactorial()
    {
        var results = new List<Dictionary<string, double>>();

        // Generate all combinations
        GenerateRecursive(0, new Dictionary<string, double>(), results);

        return results;
    }

    private void GenerateRecursive(int paramIndex, Dictionary<string, double> current,
        List<Dictionary<string, double>> results)
    {
        if (paramIndex >= Parameters.Count)
        {
            results.Add(new Dictionary<string, double>(current));
            return;
        }

        var param = Parameters[paramIndex];
        var values = param.GenerateValues(SamplesPerParameter);

        foreach (var value in values)
        {
            current[param.Name] = value;
            GenerateRecursive(paramIndex + 1, current, results);
        }

        if (current.ContainsKey(param.Name))
            current.Remove(param.Name);
    }

    private List<Dictionary<string, double>> GenerateLatinHypercube()
    {
        int n = SamplesPerParameter;
        int d = Parameters.Count;
        var results = new List<Dictionary<string, double>>();

        var random = new Random();

        // Create Latin Hypercube samples
        var samples = new double[n, d];

        for (int j = 0; j < d; j++)
        {
            var permutation = Enumerable.Range(0, n).OrderBy(x => random.Next()).ToArray();
            for (int i = 0; i < n; i++)
            {
                double u = (permutation[i] + random.NextDouble()) / n;
                samples[i, j] = u;
            }
        }

        // Map to parameter ranges
        for (int i = 0; i < n; i++)
        {
            var dict = new Dictionary<string, double>();
            for (int j = 0; j < d; j++)
            {
                var param = Parameters[j];
                double value = param.Min + samples[i, j] * (param.Max - param.Min);
                dict[param.Name] = value;
            }
            results.Add(dict);
        }

        return results;
    }

    private List<Dictionary<string, double>> GenerateRandomSamples()
    {
        var results = new List<Dictionary<string, double>>();
        var random = new Random();

        int totalSamples = (int)Math.Pow(SamplesPerParameter, Parameters.Count);

        for (int i = 0; i < totalSamples; i++)
        {
            var dict = new Dictionary<string, double>();
            foreach (var param in Parameters)
            {
                double value = param.Min + random.NextDouble() * (param.Max - param.Min);
                dict[param.Name] = value;
            }
            results.Add(dict);
        }

        return results;
    }

    private List<Dictionary<string, double>> GenerateSobolSequence()
    {
        // Simplified Sobol sequence (full implementation would be more complex)
        return GenerateLatinHypercube();
    }
}

/// <summary>
/// Parameter range definition for sweep
/// </summary>
public class ParameterRange
{
    [JsonProperty]
    public string Name { get; set; }

    [JsonProperty]
    public string Description { get; set; }

    [JsonProperty]
    public double Min { get; set; }

    [JsonProperty]
    public double Max { get; set; }

    [JsonProperty]
    public ParameterScaleType Scale { get; set; } = ParameterScaleType.Linear;

    [JsonProperty]
    public string TargetPath { get; set; } // e.g., "Domain[0].Material.Porosity"

    public ParameterRange()
    {
    }

    public ParameterRange(string name, double min, double max)
    {
        Name = name;
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Generate evenly-spaced values across the range
    /// </summary>
    public List<double> GenerateValues(int count)
    {
        var values = new List<double>();

        for (int i = 0; i < count; i++)
        {
            double fraction = count > 1 ? (double)i / (count - 1) : 0.5;

            double value;
            if (Scale == ParameterScaleType.Linear)
            {
                value = Min + fraction * (Max - Min);
            }
            else // Logarithmic
            {
                double logMin = Math.Log10(Min);
                double logMax = Math.Log10(Max);
                value = Math.Pow(10, logMin + fraction * (logMax - logMin));
            }

            values.Add(value);
        }

        return values;
    }
}

/// <summary>
/// Types of parameter sweeps
/// </summary>
public enum SweepType
{
    /// <summary>
    /// All combinations of all parameters
    /// </summary>
    FullFactorial,

    /// <summary>
    /// Latin Hypercube Sampling (more efficient)
    /// </summary>
    LatinHypercube,

    /// <summary>
    /// Random sampling
    /// </summary>
    RandomSampling,

    /// <summary>
    /// Sobol quasi-random sequence
    /// </summary>
    SobolSequence,

    /// <summary>
    /// One-at-a-time variation
    /// </summary>
    OneAtATime
}

/// <summary>
/// Parameter scaling type
/// </summary>
public enum ParameterScaleType
{
    Linear,
    Logarithmic
}

/// <summary>
/// Results from a parameter sweep run
/// </summary>
public class ParameterSweepResults
{
    [JsonProperty]
    public List<ParameterSweepRun> Runs { get; set; } = new();

    [JsonProperty]
    public DateTime StartTime { get; set; }

    [JsonProperty]
    public DateTime EndTime { get; set; }

    [JsonProperty]
    public TimeSpan TotalDuration => EndTime - StartTime;

    [JsonProperty]
    public int SuccessfulRuns => Runs.Count(r => r.Success);

    [JsonProperty]
    public int FailedRuns => Runs.Count(r => !r.Success);

    /// <summary>
    /// Perform sensitivity analysis on results
    /// </summary>
    public Dictionary<string, double> CalculateSensitivities(string outputMetric)
    {
        var sensitivities = new Dictionary<string, double>();

        // Simple correlation-based sensitivity
        var successfulRuns = Runs.Where(r => r.Success && r.Outputs.ContainsKey(outputMetric)).ToList();

        if (successfulRuns.Count == 0)
            return sensitivities;

        foreach (var paramName in successfulRuns[0].Parameters.Keys)
        {
            var paramValues = successfulRuns.Select(r => r.Parameters[paramName]).ToArray();
            var outputValues = successfulRuns.Select(r => r.Outputs[outputMetric]).ToArray();

            double correlation = CalculateCorrelation(paramValues, outputValues);
            sensitivities[paramName] = Math.Abs(correlation);
        }

        return sensitivities;
    }

    private double CalculateCorrelation(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length == 0)
            return 0;

        double meanX = x.Average();
        double meanY = y.Average();

        double numerator = 0;
        double denomX = 0;
        double denomY = 0;

        for (int i = 0; i < x.Length; i++)
        {
            double dx = x[i] - meanX;
            double dy = y[i] - meanY;
            numerator += dx * dy;
            denomX += dx * dx;
            denomY += dy * dy;
        }

        if (denomX == 0 || denomY == 0)
            return 0;

        return numerator / Math.Sqrt(denomX * denomY);
    }

    /// <summary>
    /// Export results to CSV
    /// </summary>
    public string ExportToCsv()
    {
        if (Runs.Count == 0)
            return "";

        var csv = new System.Text.StringBuilder();

        // Header
        var firstRun = Runs[0];
        var paramNames = firstRun.Parameters.Keys.ToList();
        var outputNames = firstRun.Outputs.Keys.ToList();

        csv.Append(string.Join(",", paramNames));
        csv.Append(",");
        csv.Append(string.Join(",", outputNames));
        csv.Append(",Success,Duration");
        csv.AppendLine();

        // Data rows
        foreach (var run in Runs)
        {
            foreach (var paramName in paramNames)
                csv.Append($"{run.Parameters[paramName]},");

            foreach (var outputName in outputNames)
                csv.Append($"{(run.Outputs.ContainsKey(outputName) ? run.Outputs[outputName] : 0)},");

            csv.Append($"{run.Success},{run.Duration.TotalSeconds}");
            csv.AppendLine();
        }

        return csv.ToString();
    }
}

/// <summary>
/// Single run in a parameter sweep
/// </summary>
public class ParameterSweepRun
{
    [JsonProperty]
    public int RunId { get; set; }

    [JsonProperty]
    public Dictionary<string, double> Parameters { get; set; } = new();

    [JsonProperty]
    public Dictionary<string, double> Outputs { get; set; } = new();

    [JsonProperty]
    public bool Success { get; set; }

    [JsonProperty]
    public string ErrorMessage { get; set; }

    [JsonProperty]
    public DateTime StartTime { get; set; }

    [JsonProperty]
    public DateTime EndTime { get; set; }

    [JsonProperty]
    public TimeSpan Duration => EndTime - StartTime;

    [JsonProperty]
    public string OutputDirectory { get; set; }
}
