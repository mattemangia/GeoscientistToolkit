using System.Text.Json;

namespace GAIA.Interop.GaiaPrism;

/// <summary>
/// Domain contract for the GAIA↔PRISM upscaling/downscaling exchange. A GPEX package with
/// <c>Domain == "upscaling"</c> carries wells (header + stratigraphic intervals + logs) and the
/// pore-network summaries that back the interval petrophysics. GAIA→PRISM packages upscale
/// pore/core-scale petrophysics onto well intervals and reservoir zones; PRISM→GAIA packages
/// downscale reservoir-model wells (stratigraphy, in-situ conditions) into targets for
/// pore-scale simulation. All payload units are SI unless the field name states otherwise.
/// </summary>
public static class UpscalingExchangeContract
{
    public const string DomainId = "upscaling";
    public const string Version = "1.0.0";

    public const string WellsPayloadSchemaId = "org.gaia-prism.upscaling.wells";
    public const string PnmSummarySchemaId = "org.gaia-prism.upscaling.pnm-summary";

    public const string WellsArtifactId = "wells";
    public const string WellsFileName = "wells.json";
    public const string PnmSummaryFilePrefix = "pnm/";

    public const string WellsRole = "wells";
    public const string PnmSummaryRole = "pnm-summary";
    public const string PnmNetworkRole = "pnm-network";

    public static string PnmSummaryFileName(string pnmId) => PnmSummaryFilePrefix + pnmId + ".json";
}

/// <summary>Canonical property names used in interval property maps and manifest effective properties.</summary>
public static class UpscalingPropertyNames
{
    public const string Porosity = "porosity";                            // "1", fraction
    public const string Permeability = "permeability";                    // "m2", [k] or [kx,ky,kz]
    public const string HorizontalPermeability = "permeabilityHorizontal"; // "m2"
    public const string VerticalPermeability = "permeabilityVertical";     // "m2"
    public const string FormationFactor = "formationFactor";              // "1"
    public const string Tortuosity = "tortuosity";                        // "1"
    public const string EffectiveDiffusivity = "effectiveDiffusivity";    // "m2/s"
    public const string ThermalConductivity = "thermalConductivity";      // "W/(m*K)"
    public const string Density = "density";                              // "kg/m3"
    public const string SpecificHeat = "specificHeat";                    // "J/(kg*K)"
    public const string PWaveVelocity = "vp";                             // "m/s"
    public const string SWaveVelocity = "vs";                             // "m/s"
    public const string YoungModulus = "youngModulus";                    // "Pa"
    public const string PoissonRatio = "poissonRatio";                    // "1"
    public const string Temperature = "temperature";                      // "degC"
    public const string PorePressure = "porePressure";                    // "Pa"

    /// <summary>Canonical unit for each known property name; unknown names are free-form.</summary>
    public static readonly IReadOnlyDictionary<string, string> CanonicalUnits = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Porosity] = "1", [Permeability] = "m2", [HorizontalPermeability] = "m2", [VerticalPermeability] = "m2",
        [FormationFactor] = "1", [Tortuosity] = "1", [EffectiveDiffusivity] = "m2/s",
        [ThermalConductivity] = "W/(m*K)", [Density] = "kg/m3", [SpecificHeat] = "J/(kg*K)",
        [PWaveVelocity] = "m/s", [SWaveVelocity] = "m/s", [YoungModulus] = "Pa", [PoissonRatio] = "1",
        [Temperature] = "degC", [PorePressure] = "Pa"
    };
}

/// <summary>Unit conversions shared by both producers so the payload stays strictly SI.</summary>
public static class PetrophysicalUnits
{
    /// <summary>1 millidarcy in square metres.</summary>
    public const double MilliDarcyInSquareMetres = 9.869233e-16;

    /// <summary>ρ·g/μ for water at 20 °C (998.2 kg/m³ · 9.80665 m/s² / 1.002e-3 Pa·s), in 1/(m·s).
    /// Converts intrinsic permeability [m²] to hydraulic conductivity [m/s].</summary>
    public const double WaterConductivityFactor = 9.7697e6;

    public static double MilliDarcyToSquareMetres(double milliDarcy) => milliDarcy * MilliDarcyInSquareMetres;
    public static double SquareMetresToMilliDarcy(double squareMetres) => squareMetres / MilliDarcyInSquareMetres;
    public static double SquareMetresToHydraulicConductivity(double squareMetres) => squareMetres * WaterConductivityFactor;
    public static double HydraulicConductivityToSquareMetres(double metresPerSecond) => metresPerSecond / WaterConductivityFactor;
}

/// <summary>Root payload stored as <c>payload/wells.json</c>.</summary>
public sealed record WellExchangePayload
{
    public string SchemaId { get; init; } = UpscalingExchangeContract.WellsPayloadSchemaId;
    public string Version { get; init; } = UpscalingExchangeContract.Version;
    public IReadOnlyList<WellRecord> Wells { get; init; } = [];
}

public sealed record WellRecord
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Field { get; init; }
    public string? Source { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? ProjectedX { get; init; }
    public double? ProjectedY { get; init; }
    public string? Crs { get; init; }
    public double? ElevationMetres { get; init; }
    public double? TotalDepthMetres { get; init; }
    public double? WaterTableDepthMetres { get; init; }
    public double? WellDiameterMetres { get; init; }
    public IReadOnlyList<WellIntervalRecord> Intervals { get; init; } = [];
    public IReadOnlyList<WellLogRecord> Logs { get; init; } = [];
}

/// <summary>A stratigraphic interval of a well (GAIA lithology unit / PRISM stratigraphy layer).</summary>
public sealed record WellIntervalRecord
{
    public required string Id { get; init; }
    public required double TopDepthMetres { get; init; }
    public required double BottomDepthMetres { get; init; }
    public string? Lithology { get; init; }
    public string? Description { get; init; }
    public string? GrainSize { get; init; }
    /// <summary>Canonical property name → value(s) with unit. See <see cref="UpscalingPropertyNames"/>.</summary>
    public IReadOnlyDictionary<string, QuantityValue> Properties { get; init; } = new Dictionary<string, QuantityValue>();
    /// <summary>Pore networks assigned to this interval; ids reference PNM summary artifacts.</summary>
    public IReadOnlyList<PnmAssignment> PnmAssignments { get; init; } = [];
    /// <summary>Provenance tag, e.g. "Measured", "PnmUpscaled", "PrismModel".</summary>
    public string? PropertyOrigin { get; init; }
    /// <summary>Quality score in [0,1] mirroring PRISM's PetrophysicsQuality.</summary>
    public double? PropertyQuality { get; init; }
}

/// <summary>Assignment of one pore network to a well interval with a volume-fraction weight.</summary>
public sealed record PnmAssignment
{
    public required string PnmId { get; init; }
    public double Weight { get; init; } = 1.0;
}

/// <summary>A depth-indexed curve (parameter track / digitized log).</summary>
public sealed record WellLogRecord
{
    public required string Name { get; init; }
    public required string Unit { get; init; }
    public required double[] DepthsMetres { get; init; }
    public required double[] Values { get; init; }
    public bool IsLogarithmic { get; init; }
}

/// <summary>
/// Pore-network characterisation exported by GAIA, stored one-per-network under
/// <c>payload/pnm/&lt;id&gt;.json</c>. Permeabilities stay in millidarcy (native PNM unit);
/// converters in <see cref="PetrophysicalUnits"/> take them to SI.
/// </summary>
public sealed record PnmNetworkSummary
{
    public string SchemaId { get; init; } = UpscalingExchangeContract.PnmSummarySchemaId;
    public required string Id { get; init; }
    public required string SampleId { get; init; }
    public string? SourceDataset { get; init; }
    public double VoxelSizeMicrometres { get; init; }
    /// <summary>Source image dimensions in voxels (width, height, depth).</summary>
    public int[] ImageDimensions { get; init; } = [];
    public int PoreCount { get; init; }
    public int ThroatCount { get; init; }
    public double? PorosityFraction { get; init; }
    public double? DarcyPermeabilityMilliDarcy { get; init; }
    public double? NavierStokesPermeabilityMilliDarcy { get; init; }
    public double? LatticeBoltzmannPermeabilityMilliDarcy { get; init; }
    public double? Tortuosity { get; init; }
    public double? TransportTortuosity { get; init; }
    public double? FormationFactor { get; init; }
    public double? BulkDiffusivitySquareMetresPerSecond { get; init; }
    public double? EffectiveDiffusivitySquareMetresPerSecond { get; init; }
    /// <summary>Dual-scale (macro+micro) effective permeability for a Dual PNM, in millidarcy.
    /// When present it takes precedence over the single-scale solves so upscaling cannot silently
    /// drop the microporosity contribution. Null for ordinary single-scale networks.</summary>
    public double? CombinedPermeabilityMilliDarcy { get; init; }
    public QualificationStatus Qualification { get; init; } = QualificationStatus.Raw;
    /// <summary>Optional artifact id of the full pore/throat network (GAIA PNM JSON) in the same package.</summary>
    public string? NetworkArtifactId { get; init; }

    /// <summary>Highest-fidelity available permeability: dual-scale combined (if present), then
    /// lattice-Boltzmann, then Navier-Stokes, then Darcy network solve.</summary>
    public double? PreferredPermeabilityMilliDarcy =>
        CombinedPermeabilityMilliDarcy ?? LatticeBoltzmannPermeabilityMilliDarcy
            ?? NavierStokesPermeabilityMilliDarcy ?? DarcyPermeabilityMilliDarcy;
}

/// <summary>Serialization helpers for the upscaling payload artifacts.</summary>
public static class UpscalingPayloads
{
    public static byte[] SerializeWells(WellExchangePayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, GaiaPrismExchangeSchema.JsonOptions);

    public static WellExchangePayload DeserializeWells(byte[] utf8Json)
    {
        var payload = JsonSerializer.Deserialize<WellExchangePayload>(utf8Json, GaiaPrismExchangeSchema.JsonOptions)
                      ?? throw new InvalidDataException("The wells payload is empty or invalid.");
        if (payload.SchemaId != UpscalingExchangeContract.WellsPayloadSchemaId)
            throw new InvalidDataException($"Unsupported wells payload schema '{payload.SchemaId}'.");
        return payload;
    }

    public static byte[] SerializePnmSummary(PnmNetworkSummary summary) =>
        JsonSerializer.SerializeToUtf8Bytes(summary, GaiaPrismExchangeSchema.JsonOptions);

    public static PnmNetworkSummary DeserializePnmSummary(byte[] utf8Json)
    {
        var summary = JsonSerializer.Deserialize<PnmNetworkSummary>(utf8Json, GaiaPrismExchangeSchema.JsonOptions)
                      ?? throw new InvalidDataException("The PNM summary payload is empty or invalid.");
        if (summary.SchemaId != UpscalingExchangeContract.PnmSummarySchemaId)
            throw new InvalidDataException($"Unsupported PNM summary schema '{summary.SchemaId}'.");
        return summary;
    }
}

/// <summary>Builds upscaling-domain manifests the same way on both sides of the bridge.</summary>
public static class UpscalingManifestFactory
{
    public static ExchangeManifest Create(
        ExchangeDirection direction, string producer, string producerVersion, string projectId,
        WellExchangePayload wells, IReadOnlyDictionary<string, PnmNetworkSummary>? pnmSummaries = null,
        string method = "well-interval petrophysical upscaling",
        string solver = "IntervalUpscaler", string solverVersion = UpscalingExchangeContract.Version,
        string softwareCommit = "unknown", RevAssessment? revAssessment = null,
        ScaleSupport? sourceSupport = null, ScaleSupport? targetSupport = null)
    {
        ArgumentNullException.ThrowIfNull(wells);
        var summaries = pnmSummaries ?? new Dictionary<string, PnmNetworkSummary>();
        var maxDepth = wells.Wells.Count == 0
            ? 0
            : wells.Wells.Max(w => Math.Max(w.TotalDepthMetres ?? 0, w.Intervals.Count == 0 ? 0 : w.Intervals.Max(i => i.BottomDepthMetres)));
        var wellSupport = new ScaleSupport
        {
            Kind = ScaleKind.Well, OriginMetres = [0, 0, 0], ExtentMetres = [0, 0, maxDepth],
            OrientationMatrix = [1, 0, 0, 0, 1, 0, 0, 0, 1]
        };
        var microSupport = wellSupport with { Kind = summaries.Count > 0 ? ScaleKind.Pore : ScaleKind.Core };
        var reservoirSupport = wellSupport with { Kind = ScaleKind.Reservoir };

        // The most conservative qualification of any contributing pore network gates the whole package.
        var qualification = summaries.Count == 0
            ? QualificationStatus.Raw
            : summaries.Values.Any(s => s.Qualification == QualificationStatus.UnverifiedForbidden)
                ? QualificationStatus.UnverifiedForbidden
                : summaries.Values.Min(s => s.Qualification);
        var uncertainty = new Uncertainty { Distribution = "not-characterized", Parameters = [], ConfidenceLevel = 0 };

        var allIntervals = wells.Wells.SelectMany(w => w.Intervals).ToList();
        var zone = IntervalUpscaler.UpscaleLayers(allIntervals);
        var properties = new List<EffectiveProperty>();
        void Add(string name, double? value, Func<double, double>? convert = null)
        {
            if (value is not { } raw) return;
            properties.Add(new EffectiveProperty
            {
                Name = name, QuantityKind = "scalar", Unit = UpscalingPropertyNames.CanonicalUnits[name],
                Values = [convert?.Invoke(raw) ?? raw], Shape = [1], Uncertainty = uncertainty,
                Qualification = qualification, Method = method,
                ValidityEnvelope = $"thickness-weighted over {allIntervals.Count} intervals, {zone.ThicknessMetres:g6} m"
            });
        }
        Add(UpscalingPropertyNames.Porosity, zone.PorosityFraction);
        Add(UpscalingPropertyNames.HorizontalPermeability, zone.HorizontalPermeabilityMilliDarcy, PetrophysicalUnits.MilliDarcyToSquareMetres);
        Add(UpscalingPropertyNames.VerticalPermeability, zone.VerticalPermeabilityMilliDarcy, PetrophysicalUnits.MilliDarcyToSquareMetres);

        var manifest = new ExchangeManifest
        {
            ExchangeId = Guid.NewGuid(), CreatedUtc = DateTimeOffset.UtcNow,
            Direction = direction, Domain = UpscalingExchangeContract.DomainId,
            DomainContractVersion = UpscalingExchangeContract.Version,
            Producer = producer, ProducerVersion = producerVersion,
            SourceSupport = sourceSupport ?? (direction == ExchangeDirection.GaiaToPrism ? microSupport : reservoirSupport),
            TargetSupport = targetSupport ?? (direction == ExchangeDirection.GaiaToPrism ? reservoirSupport : microSupport),
            Provenance = new Provenance
            {
                ProjectId = projectId, Method = method, SoftwareCommit = softwareCommit,
                Solver = solver, SolverVersion = solverVersion,
                SourceIds = wells.Wells.Select(w => $"well:{w.Id}")
                    .Concat(summaries.Keys.Select(id => $"pnm:{id}")).ToArray()
            },
            RevAssessment = revAssessment ?? new RevAssessment
            {
                Status = RevStatus.NotEvaluated, Method = "not-run",
                RelativeChangeThreshold = 0.05, InterWindowCoefficientOfVariationThreshold = 0.10,
                Windows = [], Rationale = "Run a property-specific nested-window REV analysis before promoting these values beyond screening priors."
            },
            EffectiveProperties = properties
        };
        return manifest with { Validation = UpscalingValidation.Validate(manifest, wells, summaries).Messages };
    }
}

/// <summary>
/// Domain validation shared by both sides. The manifest gate (schema, shapes, REV promotion)
/// is enforced by the archive layer; this validates the upscaling payload semantics.
/// </summary>
public static class UpscalingValidation
{
    public static ValidationReport Validate(ExchangeManifest manifest, WellExchangePayload? wells,
        IReadOnlyDictionary<string, PnmNetworkSummary>? pnmSummaries = null)
    {
        var messages = new List<ValidationMessage>();
        if (!string.Equals(manifest.Domain, UpscalingExchangeContract.DomainId, StringComparison.OrdinalIgnoreCase))
            messages.Add(new(ValidationSeverity.Error, "domain.mismatch", "The manifest is not an upscaling exchange.", "domain"));
        if (manifest.DomainContractVersion != UpscalingExchangeContract.Version)
            messages.Add(new(ValidationSeverity.Error, "domain.version.unsupported",
                $"Upscaling contract '{manifest.DomainContractVersion}' is unsupported.", "domainContractVersion"));
        if (manifest.Direction == ExchangeDirection.GaiaToPrism && manifest.RevAssessment?.Status != RevStatus.Representative)
            messages.Add(new(ValidationSeverity.Warning, "rev.not-representative",
                "Pore/core-scale properties without a representative REV must be treated as screening priors, not operational reservoir properties.", "revAssessment"));

        if (wells is null)
        {
            messages.Add(new(ValidationSeverity.Error, "wells.missing", "The package declares the upscaling domain but carries no wells payload.", UpscalingExchangeContract.WellsArtifactId));
            return new(messages);
        }

        var wellIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var well in wells.Wells)
        {
            if (string.IsNullOrWhiteSpace(well.Id) || !wellIds.Add(well.Id))
                messages.Add(new(ValidationSeverity.Error, "well.id.invalid", "Well ids must be unique and non-empty.", well.Name));
            ValidateIntervals(well, pnmSummaries, messages);
            ValidateLogs(well, messages);
        }
        return new(messages);
    }

    private static void ValidateIntervals(WellRecord well, IReadOnlyDictionary<string, PnmNetworkSummary>? pnmSummaries, List<ValidationMessage> messages)
    {
        var intervalIds = new HashSet<string>(StringComparer.Ordinal);
        var ordered = well.Intervals.OrderBy(i => i.TopDepthMetres).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var interval = ordered[index];
            var target = $"{well.Id}/{interval.Id}";
            if (string.IsNullOrWhiteSpace(interval.Id) || !intervalIds.Add(interval.Id))
                messages.Add(new(ValidationSeverity.Error, "interval.id.invalid", "Interval ids must be unique and non-empty within a well.", target));
            if (!(interval.BottomDepthMetres > interval.TopDepthMetres) || interval.TopDepthMetres < 0)
                messages.Add(new(ValidationSeverity.Error, "interval.depth.invalid", "Interval bottom depth must exceed its non-negative top depth.", target));
            if (index > 0 && interval.TopDepthMetres < ordered[index - 1].BottomDepthMetres - 1e-9)
                messages.Add(new(ValidationSeverity.Warning, "interval.depth.overlap", "Intervals overlap; downstream consumers will use document order.", target));

            foreach (var (name, value) in interval.Properties)
            {
                if (value.Values.Length == 0 || string.IsNullOrWhiteSpace(value.Unit))
                    messages.Add(new(ValidationSeverity.Error, "interval.property.invalid", $"Property '{name}' requires values and a unit.", target));
                else if (UpscalingPropertyNames.CanonicalUnits.TryGetValue(name, out var unit) && !string.Equals(unit, value.Unit, StringComparison.Ordinal))
                    messages.Add(new(ValidationSeverity.Error, "interval.property.unit", $"Property '{name}' must use canonical unit '{unit}' but declares '{value.Unit}'.", target));
            }

            foreach (var assignment in interval.PnmAssignments)
            {
                if (assignment.Weight <= 0)
                    messages.Add(new(ValidationSeverity.Error, "interval.pnm.weight", $"PNM assignment '{assignment.PnmId}' requires a positive weight.", target));
                if (pnmSummaries is not null && !pnmSummaries.ContainsKey(assignment.PnmId))
                    messages.Add(new(ValidationSeverity.Error, "interval.pnm.missing", $"PNM assignment references unknown network '{assignment.PnmId}'.", target));
            }
        }
    }

    private static void ValidateLogs(WellRecord well, List<ValidationMessage> messages)
    {
        foreach (var log in well.Logs)
        {
            var target = $"{well.Id}/log:{log.Name}";
            if (log.DepthsMetres.Length != log.Values.Length || log.DepthsMetres.Length == 0)
                messages.Add(new(ValidationSeverity.Error, "log.shape.invalid", "Log depth and value arrays must be non-empty and equally long.", target));
            else
                for (var i = 1; i < log.DepthsMetres.Length; i++)
                    if (log.DepthsMetres[i] < log.DepthsMetres[i - 1])
                    {
                        messages.Add(new(ValidationSeverity.Error, "log.depth.order", "Log depths must be monotonically non-decreasing.", target));
                        break;
                    }
            if (string.IsNullOrWhiteSpace(log.Unit))
                messages.Add(new(ValidationSeverity.Error, "log.unit.missing", "Every log requires an explicit unit.", target));
        }
    }
}
