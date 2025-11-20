// GeoscientistToolkit/Data/PhysicoChem/ParameterSweepManager.cs
// Modified for GTK - Removed ImGui dependencies

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.Numerics;

namespace GeoscientistToolkit.Data.PhysicoChem;

public class CurvePoint
{
    public Vector2 Point { get; set; }

    public CurvePoint(float x, float y)
    {
        Point = new Vector2(x, y);
    }
}

public class ParameterSweepManager
{
    [JsonProperty]
    public List<ParameterSweep> Sweeps { get; set; } = new();

    [JsonProperty]
    public bool Enabled { get; set; } = false;

    [JsonProperty]
    public SweepMode Mode { get; set; } = SweepMode.Temporal;

    public double GetParameterValue(string parameterName, double timeOrIndex)
    {
        var sweep = Sweeps.FirstOrDefault(s => s.ParameterName == parameterName);
        if (sweep == null || !sweep.Enabled)
            return double.NaN;

        return sweep.Evaluate((float)timeOrIndex);
    }

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

    public ParameterSweep()
    {
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

    public double Evaluate(float normalizedTime)
    {
        if (CurvePoints.Count == 0)
            return MinValue;

        normalizedTime = Math.Max(0.0f, Math.Min(1.0f, normalizedTime));

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

    public void UpdateFromEditor()
    {
    }
}

public enum SweepMode
{
    Temporal,
    Batch
}

public enum InterpolationType
{
    Linear,
    Smooth,
    Step
}

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

    public void AddPoint(double time, double value)
    {
        if (!Enabled) return;

        TimePoints.Add(time);
        Values.Add(value);

        if (TimePoints.Count > MaxDataPoints)
        {
            TimePoints.RemoveAt(0);
            Values.RemoveAt(0);
        }
    }

    public void Clear()
    {
        TimePoints.Clear();
        Values.Clear();
    }

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

public enum TrackerType
{
    Scalar,
    Vector,
    Field,
    Custom
}

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

    public void AddTracker(string parameterName, string displayName, string unit, TrackerType type)
    {
        if (Trackers.Any(t => t.ParameterName == parameterName))
            return;

        Trackers.Add(new ParameterTracker(parameterName, displayName, unit, type));
    }

    public void RecordValues(double currentTime, PhysicoChemDataset dataset)
    {
        if (!Enabled) return;

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
        if (dataset.CurrentState == null)
            return 0.0;

        switch (parameterName)
        {
            case "AverageTemperature":
                return dataset.CurrentState.AverageTemperature;

            case "AveragePressure":
                return dataset.CurrentState.AveragePressure;

            case "TotalMass":
                return dataset.CurrentState.TotalMass;

            case "MaxVelocity":
                return dataset.CurrentState.MaxVelocity;

            default:
                return 0.0;
        }
    }

    public void ClearAll()
    {
        foreach (var tracker in Trackers)
        {
            tracker.Clear();
        }
        _lastSampleTime = 0.0;
    }
}
