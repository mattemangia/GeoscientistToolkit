using System.Text.Json;
using System.Text.Json.Serialization;

namespace GAIA.Interop.GaiaPrism;

public static class GaiaPrismExchangeSchema
{
    public const string Id = "org.gaia-prism.exchange";
    public const string Version = "1.0.0";
    public const string ManifestEntryName = "manifest.json";
    public const string PayloadPrefix = "payload/";
    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public enum ExchangeDirection { GaiaToPrism, PrismToGaia }
public enum ScaleKind { Pore, Voxel, Core, Well, Reservoir, Field, Basin, Region }
public enum RevStatus { NotEvaluated, Representative, NonRepresentative }
public enum ValidationSeverity { Information, Warning, Error }
public enum QualificationStatus { Raw, Verified, Validated, Certified, UnverifiedForbidden }

public sealed record ExchangeManifest
{
    public string SchemaId { get; init; } = GaiaPrismExchangeSchema.Id;
    public string SchemaVersion { get; init; } = GaiaPrismExchangeSchema.Version;
    public required Guid ExchangeId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required ExchangeDirection Direction { get; init; }
    public required string Domain { get; init; }
    public required string DomainContractVersion { get; init; }
    public required string Producer { get; init; }
    public required string ProducerVersion { get; init; }
    public required ScaleSupport SourceSupport { get; init; }
    public required ScaleSupport TargetSupport { get; init; }
    public required Provenance Provenance { get; init; }
    public RevAssessment? RevAssessment { get; init; }
    public IReadOnlyList<EffectiveProperty> EffectiveProperties { get; init; } = [];
    public IReadOnlyList<MaterialLaw> MaterialLaws { get; init; } = [];
    public IReadOnlyList<BoundaryConditionRequest> BoundaryConditionRequests { get; init; } = [];
    public CouplingControl? Coupling { get; init; }
    public IReadOnlyList<ValidationMessage> Validation { get; init; } = [];
    public IReadOnlyList<ArtifactReference> Artifacts { get; init; } = [];
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; init; } = new Dictionary<string, JsonElement>();
}

public sealed record CouplingControl
{
    public required string RunId { get; init; }
    public int Iteration { get; init; }
    public int MaxIterations { get; init; } = 20;
    public double PropertyTolerance { get; init; } = 0.01;
    public double MacroStateTolerance { get; init; } = 0.01;
    public double BalanceTolerance { get; init; } = 0.001;
    public double UnderRelaxation { get; init; } = 0.5;
    public int RequiredConsecutiveIterations { get; init; } = 2;
    public int ConsecutiveConvergedIterations { get; init; }
    public double PropertyRelativeChange { get; init; } = double.PositiveInfinity;
    public double MacroStateRelativeChange { get; init; } = double.PositiveInfinity;
    public double BalanceRelativeResidual { get; init; } = double.PositiveInfinity;
    public bool Converged { get; init; }
}

public sealed record ScaleSupport
{
    public required ScaleKind Kind { get; init; }
    public required double[] OriginMetres { get; init; }
    public required double[] ExtentMetres { get; init; }
    public required double[] OrientationMatrix { get; init; }
    public string? Crs { get; init; }
    public string? Datum { get; init; }
    public string? SampleId { get; init; }
    public string? Lithology { get; init; }
    public string? Facies { get; init; }
    public double? TopDepthMetres { get; init; }
    public double? BottomDepthMetres { get; init; }
}

public sealed record RevAssessment
{
    public required RevStatus Status { get; init; }
    public required string Method { get; init; }
    public required double RelativeChangeThreshold { get; init; }
    public required double InterWindowCoefficientOfVariationThreshold { get; init; }
    public required IReadOnlyList<RevWindowResult> Windows { get; init; }
    public string? Rationale { get; init; }
}

public sealed record RevWindowResult(double SupportMetres, double RelativeChange, double CoefficientOfVariation);

public sealed record EffectiveProperty
{
    public required string Name { get; init; }
    public required string QuantityKind { get; init; }
    public required string Unit { get; init; }
    public required double[] Values { get; init; }
    public required int[] Shape { get; init; }
    public double[]? OrientationMatrix { get; init; }
    public required Uncertainty Uncertainty { get; init; }
    public required QualificationStatus Qualification { get; init; }
    public required string Method { get; init; }
    public string? ValidityEnvelope { get; init; }
}

public sealed record Uncertainty
{
    public required string Distribution { get; init; }
    public required double[] Parameters { get; init; }
    public double? ConfidenceLevel { get; init; }
    public double[]? LowerBounds { get; init; }
    public double[]? UpperBounds { get; init; }
}

public sealed record MaterialLaw
{
    public required string Id { get; init; }
    public required string Model { get; init; }
    public required QualificationStatus Qualification { get; init; }
    public required IReadOnlyDictionary<string, QuantityValue> Parameters { get; init; }
    public string? CalibrationArtifactId { get; init; }
    public string? ValidityEnvelope { get; init; }
}

public sealed record QuantityValue(double[] Values, string Unit, Uncertainty? Uncertainty = null);

public sealed record BoundaryConditionRequest
{
    public required string Id { get; init; }
    public required string Field { get; init; }
    public required string Unit { get; init; }
    public required string SupportArtifactId { get; init; }
    public required string TimeAxisArtifactId { get; init; }
    public required string ValuesArtifactId { get; init; }
    public required string CoordinateFrame { get; init; }
}

public sealed record Provenance
{
    public required string ProjectId { get; init; }
    public required string Method { get; init; }
    public required string SoftwareCommit { get; init; }
    public required string Solver { get; init; }
    public required string SolverVersion { get; init; }
    public long? RandomSeed { get; init; }
    public IReadOnlyList<string> SourceIds { get; init; } = [];
}

public sealed record ValidationMessage(ValidationSeverity Severity, string Code, string Message, string? Target = null);

public sealed record ArtifactReference
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public required string MediaType { get; init; }
    public required long Length { get; init; }
    public required string Sha256 { get; init; }
    public string? Role { get; init; }
}

public sealed record ValidationReport(IReadOnlyList<ValidationMessage> Messages)
{
    [JsonIgnore] public bool IsValid => Messages.All(message => message.Severity != ValidationSeverity.Error);
}
