using GAIA.Analysis.Geomechanics;
using GAIA.Interop.GaiaPrism;

namespace VerificationTests;

public class GeomechanicalVoxelQualificationTests
{
    [Fact]
    public void HomogeneousVoxel_RecoversCompressionPositivePrincipalLoadingInMpa()
    {
        var parameters = CreateParameters();
        using var simulator = new GeomechanicalSimulatorCPU(parameters, false);

        var results = simulator.Simulate(new byte[1, 1, 1] { { { 1 } } },
            new float[1, 1, 1] { { { 2650f } } }, null, CancellationToken.None);

        Assert.InRange(results.StressXX[0, 0, 0], 9.99f, 10.01f);
        Assert.InRange(results.StressYY[0, 0, 0], 19.99f, 20.01f);
        Assert.InRange(results.StressZZ[0, 0, 0], 29.99f, 30.01f);
        Assert.InRange(results.Sigma1[0, 0, 0], 29.99f, 30.01f);
        Assert.InRange(results.Sigma3[0, 0, 0], 9.99f, 10.01f);
    }

    [Fact]
    public void LabelMaterialMapping_ChangesElementStiffness()
    {
        var parameters = CreateParameters();
        parameters.MaterialsByLabel[2] = new VoxelMaterialProperties
        {
            YoungModulus = 60000f,
            PoissonRatio = parameters.PoissonRatio,
            Density = 2800f
        };
        using var simulator = new GeomechanicalSimulatorCPU(parameters, false);

        var results = simulator.Simulate(new byte[1, 1, 1] { { { 2 } } },
            new float[1, 1, 1] { { { 2800f } } }, null, CancellationToken.None);

        // The imposed strain is derived from the global 30 GPa reference material;
        // the phase is twice as stiff and therefore develops twice the stress.
        Assert.InRange(results.StressXX[0, 0, 0], 19.98f, 20.02f);
        Assert.InRange(results.StressYY[0, 0, 0], 39.98f, 40.02f);
        Assert.InRange(results.StressZZ[0, 0, 0], 59.98f, 60.02f);
    }

    [Fact]
    public void SelectedMaterialIds_ExcludeUnselectedCtPhases()
    {
        var parameters = CreateParameters();
        parameters.SelectedMaterialIDs.Add(1);
        using var simulator = new GeomechanicalSimulatorCPU(parameters, false);

        var exception = Assert.Throws<ArgumentException>(() => simulator.Simulate(
            new byte[1, 1, 1] { { { 2 } } }, new float[1, 1, 1], null, CancellationToken.None));

        Assert.Contains("no selected material voxels", exception.Message);
    }

    [Fact]
    public void ConvergedCtScreening_WithoutRevAndLaboratoryRecord_RemainsForbidden()
    {
        var qualification = CtGeomechanicsQualification.Evaluate(CreateParameters(), new GeomechanicalResults
        {
            Converged = true,
            TotalVoxels = 1
        });

        Assert.Equal(QualificationStatus.UnverifiedForbidden, qualification.Status);
        Assert.Contains(qualification.Messages, m => m.Code == "ct.q5.experiment" && m.Severity == ValidationSeverity.Error);
    }

    private static GeomechanicalParameters CreateParameters() => new()
    {
        Width = 1,
        Height = 1,
        Depth = 1,
        PixelSize = 1000f,
        SimulationExtent = new BoundingBox(0, 0, 0, 1, 1, 1),
        YoungModulus = 30000f,
        PoissonRatio = 0.25f,
        Cohesion = 1000f,
        TensileStrength = 1000f,
        Sigma1 = 30f,
        Sigma2 = 20f,
        Sigma3 = 10f,
        UseGPU = false,
        MaxIterations = 25,
        Tolerance = 1e-6f
    };
}
