using System.IO.Compression;
using System.Numerics;
using GAIA.Data.Borehole;

namespace GAIA.Interop.GaiaPrism;

/// <summary>
/// Reads PRISM→GAIA upscaling packages (reservoir-model wells for pore-scale downscaling) and
/// maps them onto GAIA borehole datasets: intervals become lithology units, canonical SI
/// properties are converted back to GAIA display units, and logs become parameter tracks.
/// </summary>
public static class UpscalingGpexImporter
{
    public sealed record ImportedUpscalingPackage(
        ExchangeManifest Manifest,
        WellExchangePayload Wells,
        IReadOnlyDictionary<string, PnmNetworkSummary> PnmSummaries,
        ValidationReport Validation);

    /// <summary>Reads, checksum-validates and domain-validates an upscaling package.</summary>
    public static ImportedUpscalingPackage Read(string packagePath)
    {
        var manifest = GpexArchive.ReadAndValidate(packagePath);
        if (!string.Equals(manifest.Domain, UpscalingExchangeContract.DomainId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The package domain is '{manifest.Domain}', not '{UpscalingExchangeContract.DomainId}'.");

        WellExchangePayload? wells = null;
        var summaries = new Dictionary<string, PnmNetworkSummary>(StringComparer.Ordinal);
        using (var zip = ZipFile.OpenRead(packagePath))
        {
            foreach (var artifact in manifest.Artifacts)
            {
                var entry = zip.GetEntry(artifact.Path);
                if (entry is null) continue;
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                if (artifact.Id == UpscalingExchangeContract.WellsArtifactId
                    || string.Equals(artifact.Role, UpscalingExchangeContract.WellsRole, StringComparison.Ordinal))
                    wells = UpscalingPayloads.DeserializeWells(buffer.ToArray());
                else if (string.Equals(artifact.Role, UpscalingExchangeContract.PnmSummaryRole, StringComparison.Ordinal)
                         || artifact.Path.StartsWith(GaiaPrismExchangeSchema.PayloadPrefix + UpscalingExchangeContract.PnmSummaryFilePrefix, StringComparison.Ordinal))
                {
                    var summary = UpscalingPayloads.DeserializePnmSummary(buffer.ToArray());
                    summaries[summary.Id] = summary;
                }
            }
        }

        var validation = UpscalingValidation.Validate(manifest, wells, summaries);
        if (!validation.IsValid)
            throw new InvalidDataException("The upscaling package failed validation: " + string.Join("; ",
                validation.Messages.Where(m => m.Severity == ValidationSeverity.Error).Select(m => $"{m.Code}: {m.Message}")));
        return new(manifest, wells!, summaries, validation);
    }

    /// <summary>Creates a new GAIA borehole dataset from an exchange well record.</summary>
    public static BoreholeDataset ToBoreholeDataset(WellRecord well, string filePath)
    {
        ArgumentNullException.ThrowIfNull(well);
        var borehole = BoreholeDataset.CreateEmpty(well.Name, filePath);
        borehole.WellName = well.Name;
        borehole.Field = well.Field ?? string.Empty;
        borehole.Elevation = (float)(well.ElevationMetres ?? 0);
        borehole.TotalDepth = (float)(well.TotalDepthMetres
            ?? (well.Intervals.Count > 0 ? well.Intervals.Max(i => i.BottomDepthMetres) : 0));
        if (well.WaterTableDepthMetres is { } waterTable) borehole.WaterTableDepth = (float)waterTable;
        if (well.WellDiameterMetres is { } diameter) borehole.WellDiameter = (float)diameter;
        if (well is { ProjectedX: { } x, ProjectedY: { } y })
            borehole.SurfaceCoordinates = new Vector2((float)x, (float)y);
        ApplyToBorehole(well, borehole);
        return borehole;
    }

    /// <summary>Applies intervals and logs of a well record onto an existing borehole dataset.</summary>
    public static void ApplyToBorehole(WellRecord well, BoreholeDataset borehole)
    {
        ArgumentNullException.ThrowIfNull(well);
        ArgumentNullException.ThrowIfNull(borehole);
        foreach (var interval in well.Intervals.OrderBy(i => i.TopDepthMetres))
        {
            var unit = borehole.GetLithologyUnitAtDepth((float)((interval.TopDepthMetres + interval.BottomDepthMetres) / 2));
            if (unit == null
                || Math.Abs(unit.DepthFrom - interval.TopDepthMetres) > 0.01
                || Math.Abs(unit.DepthTo - interval.BottomDepthMetres) > 0.01)
            {
                unit = new LithologyUnit
                {
                    Name = string.IsNullOrWhiteSpace(interval.Lithology) ? $"Unit {interval.Id}" : interval.Lithology!,
                    LithologyType = interval.Lithology ?? "Unknown",
                    DepthFrom = (float)interval.TopDepthMetres,
                    DepthTo = (float)interval.BottomDepthMetres,
                    Description = interval.Description ?? string.Empty,
                    GrainSize = interval.GrainSize ?? "Medium"
                };
                borehole.AddLithologyUnit(unit);
            }
            ApplyIntervalParameters(interval, unit, well);
        }

        foreach (var log in well.Logs)
        {
            if (log.DepthsMetres.Length == 0) continue;
            var track = new ParameterTrack
            {
                Name = log.Name,
                Unit = log.Unit,
                IsLogarithmic = log.IsLogarithmic,
                MinValue = (float)log.Values.Min(),
                MaxValue = (float)log.Values.Max(),
                Color = new Vector4(0.31f, 0.55f, 0.83f, 1f),
                Points = log.DepthsMetres.Select((depth, i) => new ParameterPoint
                {
                    Depth = (float)depth,
                    Value = (float)log.Values[i],
                    SourceDataset = "PRISM"
                }).ToList()
            };
            borehole.ParameterTracks[log.Name] = track;
        }
        borehole.SyncMetadata();
    }

    private static void ApplyIntervalParameters(WellIntervalRecord interval, LithologyUnit unit, WellRecord well)
    {
        void Set(string gaiaKey, string canonicalName, Func<double, double>? convert = null)
        {
            if (IntervalUpscaler.ScalarProperty(interval, canonicalName) is not { } value) return;
            var converted = (float)(convert?.Invoke(value) ?? value);
            unit.Parameters[gaiaKey] = converted;
            unit.ParameterSources[gaiaKey] = new ParameterSource
            {
                DatasetName = $"PRISM:{well.Name}",
                DatasetPath = interval.PropertyOrigin ?? "PrismModel",
                SourceDepthFrom = (float)interval.TopDepthMetres,
                SourceDepthTo = (float)interval.BottomDepthMetres,
                Value = converted,
                LastUpdated = DateTime.Now
            };
        }

        // GAIA display conventions: porosity in percent, permeability in millidarcy.
        Set("Porosity", UpscalingPropertyNames.Porosity, fraction => fraction * 100.0);
        Set("Permeability", UpscalingPropertyNames.Permeability, PetrophysicalUnits.SquareMetresToMilliDarcy);
        Set("Tortuosity", UpscalingPropertyNames.Tortuosity);
        Set("Density", UpscalingPropertyNames.Density);
        Set("Thermal Conductivity", UpscalingPropertyNames.ThermalConductivity);
        Set("Specific Heat", UpscalingPropertyNames.SpecificHeat);
        Set("P-Wave Velocity", UpscalingPropertyNames.PWaveVelocity);
        Set("S-Wave Velocity", UpscalingPropertyNames.SWaveVelocity);
        Set("Young's Modulus", UpscalingPropertyNames.YoungModulus, pascal => pascal / 1e9);
        Set("Poisson's Ratio", UpscalingPropertyNames.PoissonRatio);
    }
}
