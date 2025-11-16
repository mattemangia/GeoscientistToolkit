// GeoscientistToolkit/Data/PhysicoChem/ParameterSweepManager.cs
//
// Parameter sweep manager for running simulations with varying parameters

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.UI;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Manages parameter sweeps for PhysicoChem simulations
/// Uses ImGuiCurveEditor to define parameter variations over time or sweep ranges
/// </summary>
public class ParameterSweepManager
{
    [JsonProperty]
    public List<ParameterSweep> Sweeps { get; set; } = new();

    [JsonProperty]
    public bool Enabled { get; set; } = false;

    [JsonProperty]
    public SweepMode Mode { get; set; } = SweepMode.Temporal;

    /// <summary>
    /// Get the value of a parameter at a given time or sweep index
    /// </summary>
    public double GetParameterValue(string parameterName, double timeOrIndex)
    {
        var sweep = Sweeps.FirstOrDefault(s => s.ParameterName == parameterName);
        if (sweep == null || !sweep.Enabled)
            return double.NaN;

        return sweep.Evaluate((float)timeOrIndex);
    }

    /// <summary>
    /// Apply parameter sweep values to simulation parameters
    /// </summary>
    public void ApplyToSimulation(PhysicoChemDataset dataset, double timeOrIndex)
    {
        if (!Enabled) return;

        foreach (var sweep in Sweeps.Where(s => s.Enabled))
        {
            double value = sweep.Evaluate((float)timeOrIndex);
            ApplyParameterValue(dataset, sweep.ParameterName, sweep.TargetPath, value);
        }
    }

    private void ApplyParameterValue(PhysicoChemDataset dataset, string parameterName, string targetPath, double value)
    {
        // Parse target path (e.g., "BoundaryConditions[0].Temperature" or "Domains[1].Material.Porosity")
        var parts = targetPath.Split('.');
        object current = dataset;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            current = GetPropertyValue(current, parts[i]);
            if (current == null) return;
        }

        SetPropertyValue(current, parts[^1], value);
    }

    private object GetPropertyValue(object obj, string propertyPath)
    {
        // Handle array indexing (e.g., "BoundaryConditions[0]")
        if (propertyPath.Contains('['))
        {
            var propName = propertyPath.Substring(0, propertyPath.IndexOf('['));
            var indexStr = propertyPath.Substring(propertyPath.IndexOf('[') + 1,
                                                  propertyPath.IndexOf(']') - propertyPath.IndexOf('[') - 1);
            var index = int.Parse(indexStr);

            var property = obj.GetType().GetProperty(propName);
            if (property == null) return null;

            var collection = property.GetValue(obj);
            if (collection is System.Collections.IList list && index < list.Count)
                return list[index];

            return null;
        }

        var prop = obj.GetType().GetProperty(propertyPath);
        return prop?.GetValue(obj);
    }

    private void SetPropertyValue(object obj, string propertyName, double value)
    {
        var property = obj.GetType().GetProperty(propertyName);
        if (property == null) return;

        if (property.PropertyType == typeof(double))
            property.SetValue(obj, value);
        else if (property.PropertyType == typeof(float))
            property.SetValue(obj, (float)value);
        else if (property.PropertyType == typeof(int))
            property.SetValue(obj, (int)value);
    }
}

/// <summary>
/// Defines a single parameter sweep using a curve editor
/// </summary>
public class ParameterSweep
{
    [JsonProperty]
    public string ParameterName { get; set; }

    [JsonProperty]
    public string TargetPath { get; set; }

    [JsonProperty]
    public bool Enabled { get; set; } = true;

    [JsonProperty]
    public double MinValue { get; set; } = 0.0;

    [JsonProperty]
    public double MaxValue { get; set; } = 100.0;

    [JsonProperty]
    public List<CurvePoint> CurvePoints { get; set; } = new();

    [JsonProperty]
    public InterpolationType Interpolation { get; set; } = InterpolationType.Linear;

    [JsonIgnore]
    private ImGuiCurveEditor _curveEditor;

    public ParameterSweep()
    {
        // Initialize with default linear curve
        CurvePoints = new List<CurvePoint>
        {
            new CurvePoint(0.0f, 0.0f),
            new CurvePoint(1.0f, 1.0f)
        };
    }

    public ParameterSweep(string parameterName, string targetPath, double minValue, double maxValue)
    {
        ParameterName = parameterName;
        TargetPath = targetPath;
        MinValue = minValue;
        MaxValue = maxValue;

        CurvePoints = new List<CurvePoint>
        {
            new CurvePoint(0.0f, 0.0f),
            new CurvePoint(1.0f, 1.0f)
        };
    }

    /// <summary>
    /// Evaluate the parameter value at a given time or sweep index
    /// </summary>
    public double Evaluate(float normalizedTime)
    {
        if (CurvePoints.Count == 0)
            return MinValue;

        // Clamp to [0, 1]
        normalizedTime = Math.Max(0.0f, Math.Min(1.0f, normalizedTime));

        // Find the two points to interpolate between
        if (normalizedTime <= CurvePoints[0].Point.X)
            return MinValue + CurvePoints[0].Point.Y * (MaxValue - MinValue);

        if (normalizedTime >= CurvePoints[^1].Point.X)
            return MinValue + CurvePoints[^1].Point.Y * (MaxValue - MinValue);

        for (int i = 0; i < CurvePoints.Count - 1; i++)
        {
            if (normalizedTime >= CurvePoints[i].Point.X && normalizedTime <= CurvePoints[i + 1].Point.X)
            {
                float t = (normalizedTime - CurvePoints[i].Point.X) / (CurvePoints[i + 1].Point.X - CurvePoints[i].Point.X);
                float curveValue = 0.0f;

                switch (Interpolation)
                {
                    case InterpolationType.Linear:
                        curveValue = CurvePoints[i].Point.Y + t * (CurvePoints[i + 1].Point.Y - CurvePoints[i].Point.Y);
                        break;

                    case InterpolationType.Smooth:
                        // Smoothstep interpolation
                        t = t * t * (3.0f - 2.0f * t);
                        curveValue = CurvePoints[i].Point.Y + t * (CurvePoints[i + 1].Point.Y - CurvePoints[i].Point.Y);
                        break;

                    case InterpolationType.Step:
                        curveValue = CurvePoints[i].Point.Y;
                        break;
                }

                return MinValue + curveValue * (MaxValue - MinValue);
            }
        }

        return MinValue;
    }

    /// <summary>
    /// Get the curve editor for UI rendering
    /// </summary>
    public ImGuiCurveEditor GetCurveEditor()
    {
        if (_curveEditor == null)
        {
            _curveEditor = new ImGuiCurveEditor(
                $"sweep_{ParameterName}",
                $"Parameter Sweep: {ParameterName}",
                "Normalized Time",
                "Normalized Value",
                CurvePoints
            );
        }

        return _curveEditor;
    }

    /// <summary>
    /// Update curve points from the editor
    /// </summary>
    public void UpdateFromEditor()
    {
        // The curve editor maintains the curve points internally
        // No need to sync back since we pass the list by reference
    }
}

/// <summary>
/// Mode for parameter sweep application
/// </summary>
public enum SweepMode
{
    /// <summary>
    /// Parameters vary over simulation time
    /// </summary>
    Temporal,

    /// <summary>
    /// Parameters vary over sweep index (for batch runs)
    /// </summary>
    Batch
}

/// <summary>
/// Interpolation type for parameter curves
/// </summary>
public enum InterpolationType
{
    Linear,
    Smooth,
    Step
}

/// <summary>
/// Tracks parameter values over time for plotting
/// </summary>
public class ParameterTracker
{
    [JsonProperty]
    public string ParameterName { get; set; }

    [JsonProperty]
    public string DisplayName { get; set; }

    [JsonProperty]
    public bool Enabled { get; set; } = true;

    [JsonProperty]
    public List<double> TimePoints { get; set; } = new();

    [JsonProperty]
    public List<double> Values { get; set; } = new();

    [JsonProperty]
    public string Unit { get; set; } = "";

    [JsonProperty]
    public TrackerType Type { get; set; } = TrackerType.Scalar;

    [JsonProperty]
    public int MaxDataPoints { get; set; } = 10000;

    public ParameterTracker()
    {
    }

    public ParameterTracker(string parameterName, string displayName, string unit, TrackerType type)
    {
        ParameterName = parameterName;
        DisplayName = displayName;
        Unit = unit;
        Type = type;
    }

    /// <summary>
    /// Add a data point to the tracker
    /// </summary>
    public void AddPoint(double time, double value)
    {
        if (!Enabled) return;

        TimePoints.Add(time);
        Values.Add(value);

        // Keep only the most recent points to prevent memory issues
        if (TimePoints.Count > MaxDataPoints)
        {
            TimePoints.RemoveAt(0);
            Values.RemoveAt(0);
        }
    }

    /// <summary>
    /// Clear all tracked data
    /// </summary>
    public void Clear()
    {
        TimePoints.Clear();
        Values.Clear();
    }

    /// <summary>
    /// Get statistics for the tracked parameter
    /// </summary>
    public (double Min, double Max, double Mean, double StdDev) GetStatistics()
    {
        if (Values.Count == 0)
            return (0, 0, 0, 0);

        double min = Values.Min();
        double max = Values.Max();
        double mean = Values.Average();
        double variance = Values.Select(v => Math.Pow(v - mean, 2)).Average();
        double stdDev = Math.Sqrt(variance);

        return (min, max, mean, stdDev);
    }
}

/// <summary>
/// Type of tracked parameter
/// </summary>
public enum TrackerType
{
    Scalar,
    Vector,
    Field,
    Custom
}

/// <summary>
/// Manages tracking of multiple parameters during simulation
/// </summary>
public class SimulationTrackingManager
{
    [JsonProperty]
    public List<ParameterTracker> Trackers { get; set; } = new();

    [JsonProperty]
    public bool Enabled { get; set; } = true;

    [JsonProperty]
    public double SamplingInterval { get; set; } = 0.1; // seconds

    [JsonIgnore]
    private double _lastSampleTime = 0.0;

    /// <summary>
    /// Add a new parameter to track
    /// </summary>
    public void AddTracker(string parameterName, string displayName, string unit, TrackerType type)
    {
        if (Trackers.Any(t => t.ParameterName == parameterName))
            return;

        Trackers.Add(new ParameterTracker(parameterName, displayName, unit, type));
    }

    /// <summary>
    /// Record current values for all enabled trackers
    /// </summary>
    public void RecordValues(double currentTime, PhysicoChemDataset dataset)
    {
        if (!Enabled) return;

        // Check sampling interval
        if (currentTime - _lastSampleTime < SamplingInterval && _lastSampleTime > 0)
            return;

        _lastSampleTime = currentTime;

        foreach (var tracker in Trackers.Where(t => t.Enabled))
        {
            double value = GetParameterValue(dataset, tracker.ParameterName);
            tracker.AddPoint(currentTime, value);
        }
    }

    private double GetParameterValue(PhysicoChemDataset dataset, string parameterName)
    {
        // Extract parameter value based on name
        switch (parameterName)
        {
            case "AverageTemperature":
                return dataset.AverageTemperature;

            case "AveragePressure":
                return dataset.AveragePressure;

            case "TotalMass":
                return dataset.TotalMass;

            case "MaxVelocity":
                return dataset.MaxVelocity;

            default:
                return 0.0;
        }
    }

    /// <summary>
    /// Clear all tracked data
    /// </summary>
    public void ClearAll()
    {
        foreach (var tracker in Trackers)
        {
            tracker.Clear();
        }
        _lastSampleTime = 0.0;
    }
}
