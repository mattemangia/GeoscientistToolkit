// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/GeomechanicsParameterSweepManager.cs
//
// Parameter sweep utilities for 2D geomechanics simulations.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using GeoscientistToolkit.UI;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

/// <summary>
/// Manages parameter sweeps for geomechanical simulations.
/// </summary>
public class GeomechanicsParameterSweepManager
{
    [JsonProperty]
    public List<GeomechanicsParameterSweep> Sweeps { get; set; } = new();

    [JsonProperty]
    public bool Enabled { get; set; }

    [JsonProperty]
    public GeomechanicsSweepMode Mode { get; set; } = GeomechanicsSweepMode.Step;

    public void Apply(TwoDGeomechanicalSimulator simulator, double normalizedProgress)
    {
        if (!Enabled || simulator == null) return;

        foreach (var sweep in Sweeps.Where(s => s.Enabled))
        {
            double value = sweep.Evaluate((float)normalizedProgress);
            ApplyParameterValue(simulator, sweep.TargetPath, value);
        }
    }

    private static void ApplyParameterValue(object target, string targetPath, double value)
    {
        if (target == null || string.IsNullOrWhiteSpace(targetPath)) return;

        var parts = targetPath.Split('.');
        object current = target;
        object parent = null;
        string parentProperty = null;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            parent = current;
            parentProperty = parts[i];
            current = GetPropertyValue(current, parts[i]);
            if (current == null) return;
        }

        if (TrySetVectorComponent(parent, parentProperty, current, parts[^1], value))
            return;

        SetPropertyValue(current, parts[^1], value);
    }

    private static object GetPropertyValue(object obj, string propertyPath)
    {
        if (obj == null) return null;

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

    private static void SetPropertyValue(object obj, string propertyName, double value)
    {
        if (obj == null) return;

        var property = obj.GetType().GetProperty(propertyName);
        if (property == null || !property.CanWrite) return;

        if (property.PropertyType == typeof(double))
            property.SetValue(obj, value);
        else if (property.PropertyType == typeof(float))
            property.SetValue(obj, (float)value);
        else if (property.PropertyType == typeof(int))
            property.SetValue(obj, (int)value);
        else if (property.PropertyType == typeof(bool))
            property.SetValue(obj, value > 0.5);
    }

    private static bool TrySetVectorComponent(object parent, string parentProperty, object obj, string propertyName, double value)
    {
        var componentName = propertyName.Trim();
        if (componentName is not ("X" or "Y" or "Z"))
            return false;

        if (obj is Vector2 vector2 && parent != null && !string.IsNullOrWhiteSpace(parentProperty))
        {
            var vector = componentName == "X"
                ? new Vector2((float)value, vector2.Y)
                : new Vector2(vector2.X, (float)value);
            SetPropertyValue(parent, parentProperty, vector);
            return true;
        }

        if (obj is Vector3 vector3 && parent != null && !string.IsNullOrWhiteSpace(parentProperty))
        {
            vector3 = componentName switch
            {
                "X" => new Vector3((float)value, vector3.Y, vector3.Z),
                "Y" => new Vector3(vector3.X, (float)value, vector3.Z),
                _ => new Vector3(vector3.X, vector3.Y, (float)value)
            };
            SetPropertyValue(parent, parentProperty, vector3);
            return true;
        }

        return false;
    }

    private static void SetPropertyValue(object obj, string propertyName, Vector2 value)
    {
        var property = obj.GetType().GetProperty(propertyName);
        if (property == null || !property.CanWrite) return;
        if (property.PropertyType == typeof(Vector2))
            property.SetValue(obj, value);
    }

    private static void SetPropertyValue(object obj, string propertyName, Vector3 value)
    {
        var property = obj.GetType().GetProperty(propertyName);
        if (property == null || !property.CanWrite) return;
        if (property.PropertyType == typeof(Vector3))
            property.SetValue(obj, value);
    }
}

/// <summary>
/// Defines a single parameter sweep using a curve editor.
/// </summary>
public class GeomechanicsParameterSweep
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
    public double MaxValue { get; set; } = 1.0;

    [JsonProperty]
    public List<CurvePoint> CurvePoints { get; set; } = new();

    [JsonProperty]
    public GeomechanicsInterpolationType Interpolation { get; set; } = GeomechanicsInterpolationType.Linear;

    [JsonIgnore]
    private ImGuiCurveEditor _curveEditor;

    public GeomechanicsParameterSweep()
    {
        CurvePoints = new List<CurvePoint>
        {
            new CurvePoint(0.0f, 0.0f),
            new CurvePoint(1.0f, 1.0f)
        };
    }

    public double Evaluate(float normalizedProgress)
    {
        if (CurvePoints.Count == 0)
            return MinValue;

        normalizedProgress = Math.Max(0.0f, Math.Min(1.0f, normalizedProgress));

        if (normalizedProgress <= CurvePoints[0].Point.X)
            return MinValue + CurvePoints[0].Point.Y * (MaxValue - MinValue);

        if (normalizedProgress >= CurvePoints[^1].Point.X)
            return MinValue + CurvePoints[^1].Point.Y * (MaxValue - MinValue);

        for (int i = 0; i < CurvePoints.Count - 1; i++)
        {
            if (normalizedProgress >= CurvePoints[i].Point.X &&
                normalizedProgress <= CurvePoints[i + 1].Point.X)
            {
                float t = (normalizedProgress - CurvePoints[i].Point.X) /
                          (CurvePoints[i + 1].Point.X - CurvePoints[i].Point.X);
                float curveValue = Interpolation switch
                {
                    GeomechanicsInterpolationType.Smooth => t * t * (3.0f - 2.0f * t),
                    GeomechanicsInterpolationType.Step => CurvePoints[i].Point.Y,
                    _ => CurvePoints[i].Point.Y + t * (CurvePoints[i + 1].Point.Y - CurvePoints[i].Point.Y)
                };

                return MinValue + curveValue * (MaxValue - MinValue);
            }
        }

        return MinValue;
    }

    public ImGuiCurveEditor GetCurveEditor()
    {
        _curveEditor ??= new ImGuiCurveEditor(
            $"geomech_sweep_{ParameterName}",
            $"Geomechanics Sweep: {ParameterName}",
            "Normalized Progress",
            "Normalized Value",
            CurvePoints);
        return _curveEditor;
    }
}

public enum GeomechanicsSweepMode
{
    Step,
    Time
}

public enum GeomechanicsInterpolationType
{
    Linear,
    Smooth,
    Step
}
