namespace GAIA.Interop.GaiaPrism;

public enum AveragingMethod { Arithmetic, Geometric, Harmonic }

/// <summary>Interval-scale effective properties computed from the pore networks assigned to it.</summary>
public sealed record UpscaledIntervalProperties
{
    public required string IntervalId { get; init; }
    public double? PorosityFraction { get; init; }
    /// <summary>Geometric mean of the assigned networks — the conventional estimator for spatially random media.</summary>
    public double? PermeabilityMilliDarcy { get; init; }
    /// <summary>Arithmetic mean — upper (flow-parallel) bound.</summary>
    public double? PermeabilityArithmeticMilliDarcy { get; init; }
    /// <summary>Harmonic mean — lower (flow-series) bound.</summary>
    public double? PermeabilityHarmonicMilliDarcy { get; init; }
    public double? FormationFactor { get; init; }
    public double? Tortuosity { get; init; }
    public double? EffectiveDiffusivitySquareMetresPerSecond { get; init; }
}

/// <summary>Reservoir-zone (layered) effective properties from a stack of well intervals.</summary>
public sealed record LayeredZoneUpscale
{
    public required double ThicknessMetres { get; init; }
    public double? PorosityFraction { get; init; }
    /// <summary>Thickness-weighted arithmetic mean — flow parallel to layering (kh).</summary>
    public double? HorizontalPermeabilityMilliDarcy { get; init; }
    /// <summary>Thickness-weighted harmonic mean — flow across layering (kv).</summary>
    public double? VerticalPermeabilityMilliDarcy { get; init; }
}

/// <summary>
/// Analytical upscaling shared by GAIA (producer) and PRISM (consumer). Pore-network → interval
/// uses weighted arithmetic/geometric/harmonic estimators; interval stack → reservoir zone uses
/// the classical layered-media bounds (arithmetic for kh, harmonic for kv).
/// </summary>
public static class IntervalUpscaler
{
    public static double Average(IReadOnlyList<double> values, IReadOnlyList<double>? weights, AveragingMethod method)
    {
        if (values.Count == 0) throw new ArgumentException("At least one value is required.", nameof(values));
        if (weights is not null && weights.Count != values.Count)
            throw new ArgumentException("Weights must match values.", nameof(weights));
        var totalWeight = 0.0;
        var accumulator = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            var weight = weights?[i] ?? 1.0;
            if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weights), "Weights must be positive.");
            var value = values[i];
            if (method != AveragingMethod.Arithmetic && value <= 0)
                throw new ArgumentOutOfRangeException(nameof(values), "Geometric and harmonic means require positive values.");
            totalWeight += weight;
            accumulator += method switch
            {
                AveragingMethod.Arithmetic => weight * value,
                AveragingMethod.Geometric => weight * Math.Log(value),
                AveragingMethod.Harmonic => weight / value,
                _ => throw new ArgumentOutOfRangeException(nameof(method))
            };
        }
        return method switch
        {
            AveragingMethod.Arithmetic => accumulator / totalWeight,
            AveragingMethod.Geometric => Math.Exp(accumulator / totalWeight),
            _ => totalWeight / accumulator
        };
    }

    /// <summary>Combines the pore networks assigned to an interval into interval-scale effective properties.</summary>
    public static UpscaledIntervalProperties UpscaleInterval(WellIntervalRecord interval,
        IReadOnlyDictionary<string, PnmNetworkSummary> pnmSummaries)
    {
        ArgumentNullException.ThrowIfNull(interval);
        ArgumentNullException.ThrowIfNull(pnmSummaries);
        var assigned = interval.PnmAssignments
            .Select(a => (Summary: pnmSummaries.TryGetValue(a.PnmId, out var s) ? s : throw new KeyNotFoundException($"Unknown PNM '{a.PnmId}'."), a.Weight))
            .ToList();
        if (assigned.Count == 0) return new UpscaledIntervalProperties { IntervalId = interval.Id };

        double? Combine(Func<PnmNetworkSummary, double?> selector, AveragingMethod method)
        {
            var pairs = new List<(double Value, double Weight)>();
            foreach (var (summary, weight) in assigned)
            {
                var value = selector(summary);
                if (!value.HasValue) continue;
                if (method != AveragingMethod.Arithmetic && value.Value <= 0) continue;
                pairs.Add((value.Value, weight));
            }
            if (pairs.Count == 0) return null;
            return Average(pairs.Select(p => p.Value).ToList(), pairs.Select(p => p.Weight).ToList(), method);
        }

        return new UpscaledIntervalProperties
        {
            IntervalId = interval.Id,
            PorosityFraction = Combine(s => s.PorosityFraction, AveragingMethod.Arithmetic),
            PermeabilityMilliDarcy = Combine(s => s.PreferredPermeabilityMilliDarcy, AveragingMethod.Geometric),
            PermeabilityArithmeticMilliDarcy = Combine(s => s.PreferredPermeabilityMilliDarcy, AveragingMethod.Arithmetic),
            PermeabilityHarmonicMilliDarcy = Combine(s => s.PreferredPermeabilityMilliDarcy, AveragingMethod.Harmonic),
            FormationFactor = Combine(s => s.FormationFactor, AveragingMethod.Geometric),
            Tortuosity = Combine(s => s.Tortuosity, AveragingMethod.Arithmetic),
            EffectiveDiffusivitySquareMetresPerSecond = Combine(s => s.EffectiveDiffusivitySquareMetresPerSecond, AveragingMethod.Geometric)
        };
    }

    /// <summary>
    /// Upscales a stack of well intervals into one reservoir zone. Intervals without the needed
    /// property are excluded from that property's average (their thickness does not contribute).
    /// Permeability is read from the canonical interval properties (m²) and returned in millidarcy.
    /// </summary>
    public static LayeredZoneUpscale UpscaleLayers(IEnumerable<WellIntervalRecord> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        var layers = intervals.Select(i => (
                Thickness: i.BottomDepthMetres - i.TopDepthMetres,
                Porosity: ScalarProperty(i, UpscalingPropertyNames.Porosity),
                PermeabilityMilliDarcy: PermeabilityMilliDarcy(i)))
            .Where(l => l.Thickness > 0)
            .ToList();
        var total = layers.Sum(l => l.Thickness);
        if (total <= 0) return new LayeredZoneUpscale { ThicknessMetres = 0 };

        double? Weighted(Func<(double Thickness, double? Porosity, double? PermeabilityMilliDarcy), double?> selector, AveragingMethod method)
        {
            var pairs = layers.Where(l => selector(l) is { } v && (method == AveragingMethod.Arithmetic || v > 0))
                .Select(l => (Value: selector(l)!.Value, Weight: l.Thickness)).ToList();
            if (pairs.Count == 0) return null;
            return Average(pairs.Select(p => p.Value).ToList(), pairs.Select(p => p.Weight).ToList(), method);
        }

        return new LayeredZoneUpscale
        {
            ThicknessMetres = total,
            PorosityFraction = Weighted(l => l.Porosity, AveragingMethod.Arithmetic),
            HorizontalPermeabilityMilliDarcy = Weighted(l => l.PermeabilityMilliDarcy, AveragingMethod.Arithmetic),
            VerticalPermeabilityMilliDarcy = Weighted(l => l.PermeabilityMilliDarcy, AveragingMethod.Harmonic)
        };
    }

    /// <summary>First value of a canonical scalar interval property, or null when absent.</summary>
    public static double? ScalarProperty(WellIntervalRecord interval, string name) =>
        interval.Properties.TryGetValue(name, out var value) && value.Values.Length > 0 ? value.Values[0] : null;

    /// <summary>Isotropic-equivalent interval permeability in millidarcy from the canonical m² property.</summary>
    public static double? PermeabilityMilliDarcy(WellIntervalRecord interval)
    {
        if (!interval.Properties.TryGetValue(UpscalingPropertyNames.Permeability, out var value) || value.Values.Length == 0)
            return null;
        var squareMetres = value.Values.Length >= 3 && value.Values.All(v => v > 0)
            ? Math.Pow(value.Values[0] * value.Values[1] * value.Values[2], 1.0 / 3.0)
            : value.Values[0];
        return PetrophysicalUnits.SquareMetresToMilliDarcy(squareMetres);
    }
}
