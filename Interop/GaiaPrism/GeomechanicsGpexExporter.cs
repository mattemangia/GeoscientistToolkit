using GAIA.Analysis.Geomechanics;

namespace GAIA.Interop.GaiaPrism;

public sealed record CtGeomechanicsQualification(QualificationStatus Status, IReadOnlyList<ValidationMessage> Messages)
{
    public static CtGeomechanicsQualification Evaluate(GeomechanicalParameters parameters, GeomechanicalResults results)
    {
        var messages = new List<ValidationMessage>();
        if (!results.Converged) messages.Add(new(ValidationSeverity.Error, "ct.q1.convergence", "The microscale equilibrium solve did not converge."));
        if (results.TotalVoxels <= 0) messages.Add(new(ValidationSeverity.Error, "ct.q2.geometry", "The selected CT support contains no active voxels."));
        if (parameters.MaterialsByLabel.Count == 0) messages.Add(new(ValidationSeverity.Warning, "ct.q2.labels", "No phase-specific material map was supplied."));
        if (parameters.EnablePlasticity) messages.Add(new(ValidationSeverity.Error, "ct.q3.plasticity", "Nonlinear plastic upscaling remains quarantined until global consistent-tangent tests pass."));
        if (parameters.EnableDamageEvolution) messages.Add(new(ValidationSeverity.Error, "ct.q3.damage", "Damage-law promotion remains quarantined until fracture-energy regularization is validated."));
        if (parameters.UseGPU) messages.Add(new(ValidationSeverity.Error, "ct.q4.gpu", "The legacy GPU path has not passed field-level parity against the requalified CPU solver."));
        // This exporter currently has neither a same-sample laboratory record nor a
        // property-specific REV result. A numerically converged screening run is not
        // sufficient evidence for macro promotion.
        messages.Add(new(ValidationSeverity.Error, "ct.q5.experiment", "No same-sample laboratory validation and representative-volume record is attached; operational macro promotion is forbidden."));
        var status = messages.Any(m => m.Severity == ValidationSeverity.Error)
            ? QualificationStatus.UnverifiedForbidden : QualificationStatus.Verified;
        return new(status, messages);
    }
}

public static class GeomechanicsGpexExporter
{
    public static ExchangeManifest CreateManifest(string projectId, string sampleId,
        GeomechanicalParameters parameters, GeomechanicalResults results, string softwareCommit = "unknown")
    {
        ArgumentNullException.ThrowIfNull(parameters); ArgumentNullException.ThrowIfNull(results);
        var qualification = CtGeomechanicsQualification.Evaluate(parameters, results);
        var labels = results.MaterialLabels;
        var phaseCounts = CountLabels(labels, parameters.SelectedMaterialIDs);
        var total = Math.Max(1L, phaseCounts.Values.Sum());
        double Weighted(Func<VoxelMaterialProperties, double> selector, double fallback)
        {
            if (phaseCounts.Count == 0) return fallback;
            return phaseCounts.Sum(pair => pair.Value * selector(parameters.MaterialsByLabel.TryGetValue(pair.Key, out var material)
                ? material : new VoxelMaterialProperties { YoungModulus = parameters.YoungModulus, PoissonRatio = parameters.PoissonRatio, Density = parameters.Density })) / total;
        }
        var ePa = Weighted(m => m.YoungModulus * 1e6, parameters.YoungModulus * 1e6);
        var nu = Weighted(m => m.PoissonRatio, parameters.PoissonRatio);
        var density = Weighted(m => m.Density, parameters.Density);
        var uncertainty = new Uncertainty { Distribution = "not-characterized", Parameters = [], ConfidenceLevel = 0 };
        EffectiveProperty Property(string name, string kind, string unit, double[] values, int[] shape) => new()
        {
            Name = name, QuantityKind = kind, Unit = unit, Values = values, Shape = shape,
            OrientationMatrix = [1, 0, 0, 0, 1, 0, 0, 0, 1], Uncertainty = uncertainty,
            Qualification = qualification.Status, Method = "CT-label volume-weighted screening; six-load-case homogenization required for anisotropy",
            ValidityEnvelope = $"sigma1={parameters.Sigma1:g6} MPa;sigma2={parameters.Sigma2:g6} MPa;sigma3={parameters.Sigma3:g6} MPa"
        };
        var extent = parameters.SimulationExtent;
        var spacing = parameters.PixelSize * 1e-6; // UI pixel size is µm
        var source = new ScaleSupport
        {
            Kind = ScaleKind.Core, SampleId = sampleId,
            OriginMetres = [extent.MinX * spacing, extent.MinY * spacing, extent.MinZ * spacing],
            ExtentMetres = [extent.Width * spacing, extent.Height * spacing, extent.Depth * spacing],
            OrientationMatrix = [1, 0, 0, 0, 1, 0, 0, 0, 1]
        };
        return new ExchangeManifest
        {
            ExchangeId = Guid.NewGuid(), CreatedUtc = DateTimeOffset.UtcNow,
            Direction = ExchangeDirection.GaiaToPrism, Domain = "geomechanics", DomainContractVersion = "1.0.0",
            Producer = "GAIA", ProducerVersion = typeof(GeomechanicsGpexExporter).Assembly.GetName().Version?.ToString() ?? "unknown",
            SourceSupport = source,
            TargetSupport = new ScaleSupport { Kind = ScaleKind.Reservoir, OriginMetres = [0, 0, 0], ExtentMetres = [0, 0, 0], OrientationMatrix = [1, 0, 0, 0, 1, 0, 0, 0, 1] },
            Provenance = new Provenance
            {
                ProjectId = projectId, Method = "GAIA CT voxel FEM qualification export", SoftwareCommit = softwareCommit,
                Solver = "GeomechanicalSimulatorCPU", SolverVersion = "requalification-1",
                SourceIds = phaseCounts.Keys.Select(label => $"ct-label:{label}").ToArray()
            },
            RevAssessment = new RevAssessment
            {
                Status = RevStatus.NotEvaluated, Method = "not-run", RelativeChangeThreshold = 0.05,
                InterWindowCoefficientOfVariationThreshold = 0.10, Windows = [],
                Rationale = "A property-specific nested-window REV analysis is required before macro promotion."
            },
            EffectiveProperties =
            [
                Property("youngModulus", "scalar", "Pa", [ePa], [1]),
                Property("poissonRatio", "scalar", "1", [nu], [1]),
                Property("density", "scalar", "kg/m3", [density], [1]),
                Property("cohesion", "scalar", "Pa", [parameters.Cohesion * 1e6], [1]),
                Property("frictionAngle", "scalar", "rad", [parameters.FrictionAngle * Math.PI / 180], [1]),
                Property("tensileStrength", "scalar", "Pa", [parameters.TensileStrength * 1e6], [1])
            ],
            MaterialLaws =
            [
                new MaterialLaw
                {
                    Id = "ct-core-screening", Model = parameters.FailureCriterion.ToString(), Qualification = qualification.Status,
                    Parameters = new Dictionary<string, QuantityValue>
                    {
                        ["cohesion"] = new([parameters.Cohesion * 1e6], "Pa"),
                        ["frictionAngle"] = new([parameters.FrictionAngle * Math.PI / 180], "rad"),
                        ["tensileStrength"] = new([parameters.TensileStrength * 1e6], "Pa")
                    },
                    ValidityEnvelope = "Screening only; attach virtual multi-path curves and laboratory validation before promotion."
                }
            ],
            Validation = qualification.Messages
        };
    }

    private static Dictionary<byte, long> CountLabels(byte[,,]? labels, IReadOnlySet<byte> selected)
    {
        var counts = new Dictionary<byte, long>();
        if (labels == null) return counts;
        foreach (var label in labels)
        {
            if (label == 0 || selected.Count > 0 && !selected.Contains(label)) continue;
            counts[label] = counts.GetValueOrDefault(label) + 1;
        }
        return counts;
    }
}
