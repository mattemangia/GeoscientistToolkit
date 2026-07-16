using GAIA.Interop.GaiaPrism;

namespace VerificationTests;

public sealed class GpexArchiveTests
{
    [Fact]
    public void RoundTrip_PreservesPrismCompatibleManifestAndVerifiesArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), "gaia-gpex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var artifact = Path.Combine(root, "curve.csv");
            File.WriteAllText(artifact, "strain,stress_pa\n0,0\n0.001,1000000\n");
            var package = Path.Combine(root, "exchange.gpex");
            GpexArchive.Write(package, ValidManifest(), new Dictionary<string, string> { ["payload/curve.csv"] = artifact });
            var read = GpexArchive.ReadAndValidate(package);
            Assert.Equal(GaiaPrismExchangeSchema.Version, read.SchemaVersion);
            Assert.Single(read.Artifacts);
            Assert.Equal("text/csv", read.Artifacts[0].MediaType);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ValidatedProperty_WithoutRepresentativeRev_IsRejected()
    {
        var manifest = ValidManifest() with { RevAssessment = ValidManifest().RevAssessment! with { Status = RevStatus.NonRepresentative } };
        Assert.Throws<InvalidDataException>(() => GpexArchive.ValidateManifest(manifest));
    }

    private static ExchangeManifest ValidManifest() => new()
    {
        ExchangeId = Guid.NewGuid(), CreatedUtc = DateTimeOffset.UtcNow,
        Direction = ExchangeDirection.GaiaToPrism, Domain = "geomechanics", DomainContractVersion = "1.0.0",
        Producer = "GAIA", ProducerVersion = "test",
        SourceSupport = Support(ScaleKind.Core), TargetSupport = Support(ScaleKind.Reservoir),
        Provenance = new Provenance { ProjectId = "test", Method = "six-load-case", SoftwareCommit = "test", Solver = "GAIA CT voxel FEM", SolverVersion = "test" },
        RevAssessment = new RevAssessment
        {
            Status = RevStatus.Representative, Method = "nested-window", RelativeChangeThreshold = 0.05,
            InterWindowCoefficientOfVariationThreshold = 0.10,
            Windows = new[] { new RevWindowResult(0.01, 0.02, 0.04), new RevWindowResult(0.02, 0.01, 0.03) }
        },
        EffectiveProperties = new[]
        {
            new EffectiveProperty
            {
                Name = "elastic.stiffness", QuantityKind = "symmetricTensor", Unit = "Pa",
                Values = Enumerable.Repeat(1e9, 21).ToArray(), Shape = new[] { 21 },
                Uncertainty = new Uncertainty { Distribution = "empirical", Parameters = Array.Empty<double>() },
                Qualification = QualificationStatus.Validated, Method = "six-load-case"
            }
        }
    };

    private static ScaleSupport Support(ScaleKind kind) => new()
    {
        Kind = kind, OriginMetres = new[] { 0d, 0d, 0d }, ExtentMetres = new[] { 0.05, 0.05, 0.10 },
        OrientationMatrix = new[] { 1d, 0, 0, 0, 1, 0, 0, 0, 1 }
    };
}
