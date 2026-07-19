using System.Globalization;
using GAIA.Data;
using GAIA.Data.Borehole;
using GAIA.Data.Pnm;

namespace GAIA.Interop.GaiaPrism;

/// <summary>
/// Builds GAIA→PRISM upscaling packages: pore networks characterised in GAIA are summarised,
/// assigned to borehole lithology units, upscaled to interval petrophysics, and exported as a
/// GPEX archive PRISM can import onto its wells and reservoir zones.
/// </summary>
public static class UpscalingGpexExporter
{
    /// <summary>Origin tag written on intervals whose petrophysics come from PNM upscaling.</summary>
    public const string PnmUpscaledOrigin = "PnmUpscaled";

    /// <summary>Explicit assignment of a pore network to a depth interval of a borehole.
    /// A null <paramref name="WellName"/> applies the assignment to every exported well.</summary>
    public sealed record PnmWellAssignment(PNMDataset Pnm, float DepthFromMetres, float DepthToMetres,
        double Weight = 1.0, string? WellName = null);

    /// <summary>Summarises a GAIA pore-network dataset into the exchange record (SI + native mD units).</summary>
    public static PnmNetworkSummary CreateSummary(PNMDataset pnm, string? id = null,
        QualificationStatus qualification = QualificationStatus.Verified)
    {
        ArgumentNullException.ThrowIfNull(pnm);
        double? Positive(float value) => value > 0 ? value : null;
        return new PnmNetworkSummary
        {
            Id = id ?? MakeId(pnm.Name),
            SampleId = pnm.Name,
            SourceDataset = pnm.FilePath,
            VoxelSizeMicrometres = pnm.VoxelSize,
            ImageDimensions = [pnm.ImageWidth, pnm.ImageHeight, pnm.ImageDepth],
            PoreCount = pnm.Pores?.Count ?? 0,
            ThroatCount = pnm.Throats?.Count ?? 0,
            PorosityFraction = PorosityFraction(pnm),
            DarcyPermeabilityMilliDarcy = Positive(pnm.DarcyPermeability),
            NavierStokesPermeabilityMilliDarcy = Positive(pnm.NavierStokesPermeability),
            LatticeBoltzmannPermeabilityMilliDarcy = Positive(pnm.LatticeBoltzmannPermeability),
            Tortuosity = Positive(pnm.Tortuosity),
            TransportTortuosity = Positive(pnm.TransportTortuosity),
            FormationFactor = Positive(pnm.FormationFactor),
            BulkDiffusivitySquareMetresPerSecond = Positive(pnm.BulkDiffusivity),
            EffectiveDiffusivitySquareMetresPerSecond = Positive(pnm.EffectiveDiffusivity),
            Qualification = qualification
        };
    }

    /// <summary>Total pore volume over bulk image volume, as a fraction (both in µm³).</summary>
    public static double? PorosityFraction(PNMDataset pnm)
    {
        if (pnm.Pores == null || pnm.Pores.Count == 0) return null;
        var bulk = (double)pnm.ImageWidth * pnm.ImageHeight * pnm.ImageDepth * Math.Pow(pnm.VoxelSize, 3);
        if (bulk <= 0) return null;
        return pnm.Pores.Sum(p => (double)p.VolumePhysical) / bulk;
    }

    /// <summary>
    /// Maps a GAIA borehole to the exchange well record. Lithology units become intervals; unit
    /// parameters are converted to canonical SI properties; parameter tracks become logs. Pore
    /// networks given in <paramref name="assignments"/> (or discovered through the unit's PNM
    /// parameter sources against <paramref name="availablePnms"/>) are attached to the intervals
    /// they overlap, and their upscaled properties fill any petrophysics the unit lacks.
    /// </summary>
    public static WellRecord CreateWellRecord(BoreholeDataset borehole,
        IReadOnlyList<PnmWellAssignment>? assignments = null,
        IReadOnlyDictionary<string, PnmNetworkSummary>? summariesById = null,
        IEnumerable<PNMDataset>? availablePnms = null)
    {
        ArgumentNullException.ThrowIfNull(borehole);
        var wellName = string.IsNullOrWhiteSpace(borehole.WellName) ? borehole.Name : borehole.WellName;
        var pnmByName = (availablePnms ?? []).ToDictionary(p => p.Name, StringComparer.Ordinal);
        var intervals = new List<WellIntervalRecord>();
        foreach (var unit in borehole.LithologyUnits.OrderBy(u => u.DepthFrom))
        {
            var unitAssignments = new List<PnmAssignment>();
            if (assignments is not null)
                foreach (var assignment in assignments)
                {
                    if (assignment.WellName is not null && !string.Equals(assignment.WellName, wellName, StringComparison.Ordinal)) continue;
                    if (assignment.DepthFromMetres < unit.DepthTo && assignment.DepthToMetres > unit.DepthFrom)
                        unitAssignments.Add(new PnmAssignment { PnmId = MakeId(assignment.Pnm.Name), Weight = assignment.Weight });
                }
            // Discover networks already linked through GAIA's parameter-source bookkeeping.
            foreach (var source in unit.ParameterSources.Values)
                if (source.DatasetType == DatasetType.PNM && pnmByName.ContainsKey(source.DatasetName))
                {
                    var pnmId = MakeId(source.DatasetName);
                    if (unitAssignments.All(a => a.PnmId != pnmId))
                        unitAssignments.Add(new PnmAssignment { PnmId = pnmId });
                }

            var properties = MapUnitProperties(unit);
            var hasPnm = unitAssignments.Count > 0 && summariesById is not null;
            if (hasPnm)
            {
                var probe = new WellIntervalRecord
                {
                    Id = unit.ID, TopDepthMetres = unit.DepthFrom, BottomDepthMetres = unit.DepthTo,
                    PnmAssignments = unitAssignments
                };
                var upscaled = IntervalUpscaler.UpscaleInterval(probe, summariesById!);
                void Fill(string name, double? value)
                {
                    if (value is { } raw && !properties.ContainsKey(name))
                        properties[name] = new QuantityValue([raw], UpscalingPropertyNames.CanonicalUnits[name]);
                }
                Fill(UpscalingPropertyNames.Porosity, upscaled.PorosityFraction);
                if (upscaled.PermeabilityMilliDarcy is { } milliDarcy && !properties.ContainsKey(UpscalingPropertyNames.Permeability))
                    properties[UpscalingPropertyNames.Permeability] =
                        new QuantityValue([PetrophysicalUnits.MilliDarcyToSquareMetres(milliDarcy)], "m2");
                Fill(UpscalingPropertyNames.FormationFactor, upscaled.FormationFactor);
                Fill(UpscalingPropertyNames.Tortuosity, upscaled.Tortuosity);
                Fill(UpscalingPropertyNames.EffectiveDiffusivity, upscaled.EffectiveDiffusivitySquareMetresPerSecond);
            }

            intervals.Add(new WellIntervalRecord
            {
                Id = unit.ID,
                TopDepthMetres = unit.DepthFrom,
                BottomDepthMetres = unit.DepthTo,
                Lithology = unit.LithologyType,
                Description = unit.Description,
                GrainSize = unit.GrainSize,
                Properties = properties,
                PnmAssignments = unitAssignments,
                PropertyOrigin = hasPnm ? PnmUpscaledOrigin : "Measured"
            });
        }

        var logs = borehole.ParameterTracks.Values
            .Where(track => track.Points.Count > 0)
            .Select(track => new WellLogRecord
            {
                Name = track.Name,
                Unit = string.IsNullOrWhiteSpace(track.Unit) ? "1" : track.Unit,
                DepthsMetres = track.Points.OrderBy(p => p.Depth).Select(p => (double)p.Depth).ToArray(),
                Values = track.Points.OrderBy(p => p.Depth).Select(p => (double)p.Value).ToArray(),
                IsLogarithmic = track.IsLogarithmic
            })
            .ToList();

        return new WellRecord
        {
            Id = MakeId(string.IsNullOrWhiteSpace(borehole.WellName) ? borehole.Name : borehole.WellName),
            Name = string.IsNullOrWhiteSpace(borehole.WellName) ? borehole.Name : borehole.WellName,
            Field = borehole.Field,
            Source = "GAIA",
            ProjectedX = borehole.SurfaceCoordinates.X,
            ProjectedY = borehole.SurfaceCoordinates.Y,
            ElevationMetres = borehole.Elevation,
            TotalDepthMetres = borehole.TotalDepth,
            WaterTableDepthMetres = borehole.WaterTableDepth,
            WellDiameterMetres = borehole.WellDiameter,
            Intervals = intervals,
            Logs = logs
        };
    }

    /// <summary>
    /// Converts a lithology unit's GAIA parameter map (GAIA display units) into canonical SI
    /// exchange properties. GAIA stores PNM-derived porosity in percent and permeability in mD.
    /// </summary>
    public static Dictionary<string, QuantityValue> MapUnitProperties(LithologyUnit unit)
    {
        var properties = new Dictionary<string, QuantityValue>(StringComparer.Ordinal);
        void Add(string canonicalName, string gaiaKey, Func<double, double>? convert = null)
        {
            if (unit.Parameters.TryGetValue(gaiaKey, out var value))
                properties[canonicalName] = new QuantityValue([convert?.Invoke(value) ?? value],
                    UpscalingPropertyNames.CanonicalUnits[canonicalName]);
        }

        // GAIA's PNM import stores porosity in percent; older manual entries may already be fractional.
        Add(UpscalingPropertyNames.Porosity, "Porosity", v => v > 1 ? v / 100.0 : v);
        Add(UpscalingPropertyNames.Permeability, "Permeability", PetrophysicalUnits.MilliDarcyToSquareMetres);
        Add(UpscalingPropertyNames.Tortuosity, "Tortuosity");
        Add(UpscalingPropertyNames.Density, "Density");
        Add(UpscalingPropertyNames.ThermalConductivity, "Thermal Conductivity");
        Add(UpscalingPropertyNames.SpecificHeat, "Specific Heat");
        Add(UpscalingPropertyNames.PWaveVelocity, "P-Wave Velocity");
        Add(UpscalingPropertyNames.SWaveVelocity, "S-Wave Velocity");
        Add(UpscalingPropertyNames.YoungModulus, "Young's Modulus", gigapascal => gigapascal * 1e9);
        Add(UpscalingPropertyNames.PoissonRatio, "Poisson's Ratio");
        return properties;
    }

    /// <summary>Builds the manifest and writes the .gpex package (wells + PNM summaries).</summary>
    public static ExchangeManifest Export(string destinationPath, string projectId,
        IReadOnlyList<BoreholeDataset> boreholes, IReadOnlyList<PNMDataset> pnms,
        IReadOnlyList<PnmWellAssignment>? assignments = null, string softwareCommit = "unknown")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(boreholes);
        ArgumentNullException.ThrowIfNull(pnms);

        var summaries = pnms.Select(p => CreateSummary(p)).ToDictionary(s => s.Id, StringComparer.Ordinal);
        var payload = new WellExchangePayload
        {
            Wells = boreholes.Select(b => CreateWellRecord(b, assignments, summaries, pnms)).ToList()
        };
        var manifest = UpscalingManifestFactory.Create(
            ExchangeDirection.GaiaToPrism, "GAIA",
            typeof(UpscalingGpexExporter).Assembly.GetName().Version?.ToString() ?? "unknown",
            projectId, payload, summaries,
            method: "GAIA pore-network to well-interval petrophysical upscaling",
            solver: "GAIA PNM (Darcy/Navier-Stokes/lattice-Boltzmann)",
            solverVersion: "1", softwareCommit: softwareCommit);

        var staging = Path.Combine(Path.GetTempPath(), "gaia-upscaling-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var artifacts = new Dictionary<string, string>(StringComparer.Ordinal);
            var wellsFile = Path.Combine(staging, "wells.json");
            File.WriteAllBytes(wellsFile, UpscalingPayloads.SerializeWells(payload));
            artifacts[GaiaPrismExchangeSchema.PayloadPrefix + UpscalingExchangeContract.WellsFileName] = wellsFile;
            foreach (var summary in summaries.Values)
            {
                var file = Path.Combine(staging, summary.Id + ".json");
                File.WriteAllBytes(file, UpscalingPayloads.SerializePnmSummary(summary));
                artifacts[GaiaPrismExchangeSchema.PayloadPrefix + UpscalingExchangeContract.PnmSummaryFileName(summary.Id)] = file;
            }
            GpexArchive.Write(destinationPath, manifest, artifacts);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { /* best effort */ }
        }
        return manifest;
    }

    /// <summary>Stable, path-safe artifact id derived from a dataset name.</summary>
    public static string MakeId(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Guid.NewGuid().ToString("N");
        var cleaned = new string(name.Trim().ToLower(CultureInfo.InvariantCulture)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrEmpty(cleaned) ? Guid.NewGuid().ToString("N") : cleaned;
    }
}
