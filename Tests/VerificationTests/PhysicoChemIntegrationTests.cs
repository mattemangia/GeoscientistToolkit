using GAIA.Analysis.PhysicoChem;
using GAIA.Data;
using GAIA.Data.PhysicoChem;

namespace GAIA.VerificationTests;

public class PhysicoChemIntegrationTests
{
    [Fact]
    public void SplitSelectedCells_PreservesVolumeAndDeepCopiesChemistry()
    {
        var mesh = new PhysicoChemMesh();
        var initial = new InitialConditions { Concentrations = new Dictionary<string, double> { ["Na+"] = 0.1 } };
        mesh.Cells["C_0_0_0"] = new Cell { ID = "C_0_0_0", Volume = 8, Center = (0, 0, 0), InitialConditions = initial };

        mesh.SplitCells(new[] { "C_0_0_0" }, 2, 2, 2);

        Assert.Equal(8, mesh.Cells.Count);
        Assert.Equal(8, mesh.Cells.Values.Sum(c => c.Volume), 10);
        mesh.Cells.Values.First().InitialConditions.Concentrations["Na+"] = 99;
        Assert.All(mesh.Cells.Values.Skip(1), c => Assert.Equal(0.1, c.InitialConditions.Concentrations["Na+"]));
    }

    [Fact]
    public void LegacyDto_MigratesToAdvancedMultiphysics()
    {
        var dataset = new PhysicoChemDataset("legacy");
        dataset.ImportFromDTO(new PhysicoChemDatasetDTO { SimulationParams = new SimulationParametersDTO() });
        Assert.Equal(PhysicoChemSolverMode.AdvancedMultiphysics, dataset.SimulationParams.EngineMode);
    }

    [Fact]
    public void UniformSplit_RebuildsComputationalGrid()
    {
        var dataset = StructuredDataset();
        dataset.SplitIntoStructuredGrid(3, 4, 5);
        Assert.Equal((3, 4, 5), dataset.GeneratedMesh.GridSize);
        Assert.Equal(60, dataset.Mesh.Cells.Count);
        Assert.False(dataset.ComputationalMeshDirty);
        dataset.InitializeState();
        Assert.Equal(3, dataset.CurrentState.Temperature.GetLength(0));
        Assert.Equal(4, dataset.CurrentState.Temperature.GetLength(1));
        Assert.Equal(5, dataset.CurrentState.Temperature.GetLength(2));
    }

    [Fact]
    public void LocalRefinement_BlocksSolverUntilComputationalRemesh()
    {
        var dataset = StructuredDataset();
        var parent = dataset.Mesh.Cells.Keys.First();
        dataset.Mesh.SplitCells(new[] { parent }, 2, 1, 1);
        dataset.ComputationalMeshDirty = true;
        Assert.Contains(dataset.Validate(), e => e.Contains("not represented", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(PhysicoChemSolverMode.AdvancedMultiphysics)]
    [InlineData(PhysicoChemSolverMode.CoupledThermoMultiphysics)]
    public void PhysicsEngineModes_AdvanceOneCompleteStep(PhysicoChemSolverMode mode)
    {
        var dataset = StructuredDataset();
        dataset.SimulationParams.EngineMode = mode;
        dataset.SimulationParams.TotalTime = 0.25;
        dataset.SimulationParams.TimeStep = 1;
        dataset.SimulationParams.EnableFlow = false;
        dataset.SimulationParams.EnableHeatTransfer = false;
        dataset.SimulationParams.EnableReactiveTransport = false;
        dataset.SimulationParams.EnableForces = false;
        dataset.SimulationParams.EnableNucleation = false;
        new PhysicoChemSolver(dataset).RunSimulation();
        Assert.Equal(0.25, dataset.CurrentState.CurrentTime, 10);
    }

    [Fact]
    public void TimeBasedMode_IntegratesEndpointExactly()
    {
        var dataset = StructuredDataset();
        dataset.SimulationParams.EngineMode = PhysicoChemSolverMode.AdvancedMultiphysics;
        dataset.SimulationParams.TotalTime = 2.5;
        dataset.SimulationParams.TimeStep = 1;
        dataset.SimulationParams.EnableFlow = false;
        dataset.SimulationParams.EnableHeatTransfer = false;
        dataset.SimulationParams.EnableReactiveTransport = false;
        dataset.SimulationParams.EnableForces = false;
        dataset.SimulationParams.EnableNucleation = false;

        new PhysicoChemSolver(dataset).RunSimulation();

        Assert.Equal(2.5, dataset.CurrentState.CurrentTime, 10);
    }

    [Fact]
    public void Cancellation_IsPropagatedToCaller()
    {
        var dataset = StructuredDataset();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new PhysicoChemSolver(dataset).RunSimulation(cancellation.Token));
    }

    private static PhysicoChemDataset StructuredDataset()
    {
        var dataset = new PhysicoChemDataset("test");
        dataset.Domains.Add(new ReactorDomain
        {
            Name = "box",
            Geometry = new ReactorGeometry { Type = GeometryType.Box, Dimensions = (1, 1, 1) },
            InitialConditions = new InitialConditions(), Material = new MaterialProperties()
        });
        dataset.GenerateMesh(2);
        return dataset;
    }
}
