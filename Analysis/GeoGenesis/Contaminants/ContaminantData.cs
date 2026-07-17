// GAIA.GeoGenesis/Contaminants/ContaminantData.cs
//
// Data model for contaminant / chemical-composition sampling: a set of georeferenced sampling
// points (wells / samples), each carrying one or more analyte concentrations, optionally at
// several points in time (time series for the same wells). This is the input to the spatial
// interpolation (kriging) and flow visualisation.

namespace GAIA.GeoGenesis.Contaminants;

/// <summary>A single georeferenced measurement of one or more analytes at one well/time.</summary>
public sealed class SamplePoint
{
    public string Well { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    /// <summary>Optional sampling time (days). Null for a single (steady) snapshot.</summary>
    public double? TimeDays { get; set; }
    /// <summary>Analyte/element name → concentration (mg/L by convention).</summary>
    public Dictionary<string, double> Concentrations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A collection of sampling points plus the analytes present across them.</summary>
public sealed class ContaminantDataset
{
    public List<SamplePoint> Samples { get; } = new();

    /// <summary>Distinct analyte names across all samples (sorted).</summary>
    public IReadOnlyList<string> Analytes =>
        Samples.SelectMany(s => s.Concentrations.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
               .OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Distinct sampling times (days) present, sorted; empty if the data is steady-state.</summary>
    public IReadOnlyList<double> TimeSteps =>
        Samples.Where(s => s.TimeDays.HasValue).Select(s => s.TimeDays!.Value)
               .Distinct().OrderBy(t => t).ToList();

    public bool HasTimeSeries => Samples.Any(s => s.TimeDays.HasValue);

    /// <summary>Samples at a given time (within tolerance); all samples when <paramref name="timeDays"/> is null.</summary>
    public IEnumerable<SamplePoint> AtTime(double? timeDays, double tol = 1e-6)
        => timeDays == null ? Samples : Samples.Where(s => s.TimeDays.HasValue && Math.Abs(s.TimeDays.Value - timeDays.Value) <= tol);

    /// <summary>(point, value) pairs for one analyte that have a finite concentration, at the given time.</summary>
    public List<(double X, double Y, double Z, double Value)> ValuesFor(string analyte, double? timeDays = null)
    {
        var list = new List<(double, double, double, double)>();
        foreach (var s in AtTime(timeDays))
            if (s.Concentrations.TryGetValue(analyte, out var v) && double.IsFinite(v))
                list.Add((s.X, s.Y, s.Z, v));
        return list;
    }

    /// <summary>Axis-aligned bounding box of all sample positions.</summary>
    public (double minX, double minY, double minZ, double maxX, double maxY, double maxZ) Bounds()
    {
        if (Samples.Count == 0) return (0, 0, 0, 0, 0, 0);
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var s in Samples)
        {
            minX = Math.Min(minX, s.X); maxX = Math.Max(maxX, s.X);
            minY = Math.Min(minY, s.Y); maxY = Math.Max(maxY, s.Y);
            minZ = Math.Min(minZ, s.Z); maxZ = Math.Max(maxZ, s.Z);
        }
        return (minX, minY, minZ, maxX, maxY, maxZ);
    }
}
