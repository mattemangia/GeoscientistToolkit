using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.IO;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Analysis.Geomechanics;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Analysis.Multiphase;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Analysis.Seismology;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Analysis.PhysicoChem;
using GeoscientistToolkit.Analysis.Thermodynamic;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Data.Pnm;
using MathNet.Numerics;
using Xunit;
using AcousticSimulationParameters = GeoscientistToolkit.Analysis.AcousticSimulation.SimulationParameters;
using MultiphaseSolver = GeoscientistToolkit.Analysis.Multiphase.MultiphaseFlowSolver;

namespace VerificationTests;

public class SimulationVerificationTests
{
    [Fact]
    public void SlopeStability_GravityDrop_MatchesAnalyticalFreeFall()
    {
        var dataset = new SlopeStabilityDataset();
        dataset.Blocks.Add(CreateCubeBlock(id: 1, center: new Vector3(0f, 0f, 10f), size: 1f, density: 2500f));

        var parameters = new SlopeStabilityParameters
        {
            TimeStep = 0.01f,
            TotalTime = 0.1f,
            UseCustomGravityDirection = true,
            Gravity = new Vector3(0f, 0f, -9.81f),
            LocalDamping = 0f,
            UseAdaptiveDamping = false,
            ViscousDamping = 0f,
            SaveIntermediateStates = false,
            ComputeFinalState = false,
            UseMultithreading = false,
            UseSIMD = false,
            IncludeRotation = false
        };

        var simulator = new SlopeStabilitySimulator(dataset, parameters);
        var results = simulator.RunSimulation();

        var block = results.BlockResults.Single();
        var expectedZ = 10f - 0.5f * 9.81f * parameters.TotalTime * parameters.TotalTime;

        Assert.InRange(block.FinalPosition.Z, expectedZ - 0.05f, expectedZ + 0.05f);
    }

    [Fact]
    public void SlopeStability_TiltedGravitySliding_MatchesDownslopeDisplacement()
    {
        var dataset = new SlopeStabilityDataset();
        dataset.Blocks.Add(CreateCubeBlock(id: 1, center: new Vector3(0f, 0f, 10f), size: 1f, density: 2500f));

        var parameters = new SlopeStabilityParameters
        {
            TimeStep = 0.01f,
            TotalTime = 0.2f,
            SlopeAngle = 30f,
            UseCustomGravityDirection = false,
            LocalDamping = 0f,
            UseAdaptiveDamping = false,
            ViscousDamping = 0f,
            SaveIntermediateStates = false,
            ComputeFinalState = false,
            UseMultithreading = false,
            UseSIMD = false,
            IncludeRotation = false
        };

        var simulator = new SlopeStabilitySimulator(dataset, parameters);
        var results = simulator.RunSimulation();

        var block = results.BlockResults.Single();
        var expectedX = 0.5f * 9.81f * MathF.Sin(parameters.SlopeAngle * MathF.PI / 180f)
                        * parameters.TotalTime * parameters.TotalTime;

        Assert.InRange(block.Displacement.X, expectedX - 0.02f, expectedX + 0.02f);
    }

    [Fact]
    public void PhysicoChem_DualBoxMixing_TracksReportedDiffusionMagnitude()
    {
        const int nx = 21;
        const double dx = 0.001;
        const double dt = 100.0;
        const int steps = 36;
        const double diffusionCoefficient = 1e-9;
        const float c0 = 0.01f;

        var state = new ReactiveTransportState
        {
            GridDimensions = (nx, 1, 1),
            Temperature = new float[nx, 1, 1],
            Pressure = new float[nx, 1, 1],
            Porosity = new float[nx, 1, 1]
        };

        var tracer = new float[nx, 1, 1];
        for (int i = 0; i < nx; i++)
        {
            state.Temperature[i, 0, 0] = 298.15f;
            state.Pressure[i, 0, 0] = 101325f;
            state.Porosity[i, 0, 0] = 0.3f;
            tracer[i, 0, 0] = i < nx / 2 ? c0 : 0f;
        }

        state.Concentrations["Tracer"] = tracer;
        state.InitialPorosity = (float[,,])state.Porosity.Clone();

        var flowData = new FlowFieldData
        {
            GridSpacing = (dx, 1.0, 1.0),
            VelocityX = new float[nx, 1, 1],
            VelocityY = new float[nx, 1, 1],
            VelocityZ = new float[nx, 1, 1],
            Permeability = new float[nx, 1, 1],
            InitialPermeability = new float[nx, 1, 1],
            Dispersivity = 0.0
        };

        for (int i = 0; i < nx; i++)
        {
            flowData.Permeability[i, 0, 0] = 1e-12f;
            flowData.InitialPermeability[i, 0, 0] = 1e-12f;
        }

        var solver = new ReactiveTransportSolver();
        for (int step = 0; step < steps; step++)
        {
            state = solver.SolveTimeStep(state, dt, flowData);
        }

        double totalTime = dt * steps;
        double x = dx / 2.0;
        double expected = 0.5 * c0 *
                          (1.0 - SpecialFunctions.Erf(x / (2.0 * Math.Sqrt(diffusionCoefficient * totalTime))));

        var observed = state.Concentrations["Tracer"][nx / 2, 0, 0];
        Assert.InRange(observed, expected * 0.5, expected * 1.5);
    }

    [Fact]
    public void Geothermal_DualContinuumExchange_ReducesTemperatureGap()
    {
        var mesh = new GeothermalMesh
        {
            RadialPoints = 3,
            AngularPoints = 1,
            VerticalPoints = 3
        };

        var options = new FracturedMediaOptions
        {
            InitialTemperature = 280f,
            FractureAperture = 1e-4f,
            FractureSpacing = 1.0f,
            FractureDensity = 1.0f,
            MatrixThermalConductivity = 2.5f,
            MatrixSpecificHeat = 1000f,
            MatrixDensity = 2650f
        };

        var solver = new FracturedMediaSolver(mesh, options);

        var matrixTemp = new float[3, 1, 3];
        var fractureTemp = new float[3, 1, 3];

        for (var i = 0; i < 3; i++)
        for (var k = 0; k < 3; k++)
        {
            matrixTemp[i, 0, k] = 280f;
            fractureTemp[i, 0, k] = 320f;
        }

        solver.SetMatrixTemperature(matrixTemp);
        solver.SetFractureTemperature(fractureTemp);

        solver.UpdateDualContinuum(10f);

        var updatedMatrix = solver.GetMatrixTemperature();
        var updatedFracture = solver.GetFractureTemperature();

        Assert.True(updatedMatrix[1, 0, 1] > 280f);
        Assert.True(updatedFracture[1, 0, 1] < 320f);
    }

    [Fact]
    public void TriaxialSimulation_MohrCoulombPeakMatchesReference()
    {
        var mesh = TriaxialMeshGenerator.GenerateCylindricalMesh(0.025f, 0.05f, 2, 8, 4);
        var material = new PhysicalMaterial
        {
            Name = "Reference Granite",
            YoungModulus_GPa = 50.0,
            PoissonRatio = 0.25,
            FrictionAngle_deg = 30.0,
            Density_kg_m3 = 2650.0
        };
        material.Extra["Cohesion_MPa"] = 10.0;

        var loadParams = new TriaxialLoadingParameters
        {
            ConfiningPressure_MPa = 20.0f,
            LoadingMode = TriaxialLoadingMode.StrainControlled,
            AxialStrainRate_per_s = 1e-4f,
            TotalTime_s = 1000f,
            TimeStep_s = 1f,
            MaxAxialStrain_percent = 5f
        };

        var simulation = new TriaxialSimulation();
        var results = simulation.RunSimulationCPU(mesh, material, loadParams, FailureCriterion.MohrCoulomb);

        var peakStress = results.PeakStrength_MPa;
        var expected = 20f * MathF.Pow(MathF.Tan(MathF.PI / 4f + MathF.PI / 12f), 2f)
                       + 2f * 10f * MathF.Tan(MathF.PI / 4f + MathF.PI / 12f);

        Assert.InRange(peakStress, expected - 2.0f, expected + 2.0f);
    }

    [Fact]
    public void SeismicSimulation_PAndSArrivalsMatchVelocityRatio()
    {
        var parameters = EarthquakeSimulationParameters.CreateDefault();
        parameters.SimulationDurationSeconds = 4.0;
        parameters.TimeStepSeconds = 0.01;
        parameters.GridNX = 10;
        parameters.GridNY = 10;
        parameters.GridNZ = 10;
        parameters.MinLatitude = 0;
        parameters.MaxLatitude = 1;
        parameters.MinLongitude = 0;
        parameters.MaxLongitude = 1;
        parameters.EpicenterLatitude = 0.5;
        parameters.EpicenterLongitude = 0.5;
        parameters.HypocenterDepthKm = 5.0;

        var crustalModelPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../Data/CrustalModels/GlobalCrustalModel.json"));
        parameters.CrustalModelPath = crustalModelPath;

        var engine = new EarthquakeSimulationEngine(parameters);
        engine.Initialize();
        var results = engine.Run();

        var pArrival = results.PWaveArrivalTime?[5, 5] ?? 0.0;
        var sArrival = results.SWaveArrivalTime?[5, 5] ?? 0.0;

        Assert.True(pArrival > 0);
        Assert.True(sArrival > pArrival);
        Assert.True(sArrival / pArrival > 1.2);
    }

    [Fact]
    public void Hydrology_D8FlowAccumulation_MatchesBenchmarkPattern()
    {
        var elevation = new float[5, 5];
        for (var i = 0; i < 5; i++)
        for (var j = 0; j < 5; j++)
            elevation[i, j] = 100f - i * 2f - j;

        var flowDirection = GeoscientistToolkit.Business.GIS.GISOperationsImpl.CalculateD8FlowDirection(elevation);
        var accumulation = GeoscientistToolkit.Business.GIS.GISOperationsImpl.CalculateFlowAccumulation(flowDirection);

        Assert.True(accumulation[4, 4] >= accumulation[0, 0]);
        Assert.True(accumulation[4, 4] > 1);
    }

    [Fact]
    public void MultiphaseFlow_WaterSteamTransition_UpdatesSaturations()
    {
        var solver = new MultiphaseSolver(MultiphaseSolver.EOSType.WaterSteam);
        var state = new MultiphaseState((3, 3, 3));
        var parameters = new MultiphaseParameters();

        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        for (int k = 0; k < 3; k++)
        {
            state.Pressure[i, j, k] = 1e5f;
            state.Temperature[i, j, k] = 450f;
            state.Enthalpy[i, j, k] = 2.6e6f;
            state.LiquidSaturation[i, j, k] = 0.9f;
            state.VaporSaturation[i, j, k] = 0.1f;
            state.GasSaturation[i, j, k] = 0.0f;
            state.Porosity[i, j, k] = 0.2f;
            state.Permeability[i, j, k] = 1e-13f;
        }

        var updated = solver.SolveTimeStep(state, 1.0, parameters);
        var vapor = updated.VaporSaturation[1, 1, 1];
        var liquid = updated.LiquidSaturation[1, 1, 1];

        Assert.True(vapor > 0f);
        Assert.InRange(vapor + liquid + updated.GasSaturation[1, 1, 1], 0.99f, 1.01f);
    }

    [Fact]
    public void AcousticSimulation_StressPulseGeneratesVelocity()
    {
        var parameters = new AcousticSimulationParameters();
        var simulator = new AcousticSimulatorCPU(parameters);

        int size = 5;
        var vx = new float[size, size, size];
        var vy = new float[size, size, size];
        var vz = new float[size, size, size];
        var sxx = new float[size, size, size];
        var syy = new float[size, size, size];
        var szz = new float[size, size, size];
        var sxy = new float[size, size, size];
        var sxz = new float[size, size, size];
        var syz = new float[size, size, size];
        var E = new float[size, size, size];
        var nu = new float[size, size, size];
        var rho = new float[size, size, size];

        for (var i = 0; i < size; i++)
        for (var j = 0; j < size; j++)
        for (var k = 0; k < size; k++)
        {
            E[i, j, k] = 1e9f;
            nu[i, j, k] = 0.25f;
            rho[i, j, k] = 2500f;
        }

        sxx[2, 2, 2] = 1e6f;

        simulator.UpdateWaveField(vx, vy, vz, sxx, syy, szz, sxy, sxz, syz, E, nu, rho, 1e-4f, 1f, 0f);

        Assert.NotEqual(0f, vx[3, 2, 2]);
    }

    [Fact]
    public void PnmAbsolutePermeability_SingleTubeMatchesPoiseuille()
    {
        var dataset = new PNMDataset("SingleTube", string.Empty)
        {
            VoxelSize = 1.0f,
            ImageWidth = 2,
            ImageHeight = 1,
            ImageDepth = 2
        };

        dataset.Pores.AddRange(new List<Pore>
        {
            new() { ID = 1, Position = new Vector3(0, 0, 0), Radius = 0.5f },
            new() { ID = 2, Position = new Vector3(0, 0, 10), Radius = 0.5f }
        });

        dataset.Throats.AddRange(new List<Throat>
        {
            new() { ID = 1, Pore1ID = 1, Pore2ID = 2, Radius = 0.5f }
        });

        dataset.InitializeFromCurrentLists();

        var options = new PermeabilityOptions
        {
            Dataset = dataset,
            InletPressure = 100.0f,
            OutletPressure = 0.0f,
            FluidViscosity = 1.0f,
            CalculateDarcy = true,
            Axis = FlowAxis.Z
        };

        AbsolutePermeability.Calculate(options);
        var permeability = dataset.DarcyPermeability;

        Assert.InRange(permeability, 10.0f, 40.0f);
    }

    private static Block CreateCubeBlock(int id, Vector3 center, float size, float density)
    {
        float half = size / 2f;
        var vertices = new List<Vector3>
        {
            new(center.X - half, center.Y - half, center.Z - half),
            new(center.X + half, center.Y - half, center.Z - half),
            new(center.X + half, center.Y + half, center.Z - half),
            new(center.X - half, center.Y + half, center.Z - half),
            new(center.X - half, center.Y - half, center.Z + half),
            new(center.X + half, center.Y - half, center.Z + half),
            new(center.X + half, center.Y + half, center.Z + half),
            new(center.X - half, center.Y + half, center.Z + half)
        };

        var faces = new List<int[]>
        {
            new[] { 0, 1, 2, 3 },
            new[] { 4, 5, 6, 7 },
            new[] { 0, 1, 5, 4 },
            new[] { 2, 3, 7, 6 },
            new[] { 1, 2, 6, 5 },
            new[] { 0, 3, 7, 4 }
        };

        var volume = size * size * size;
        var block = new Block
        {
            Id = id,
            Name = $"Block_{id}",
            Vertices = vertices,
            Faces = faces,
            Density = density,
            Volume = volume,
            Centroid = center,
            Mass = density * volume,
            InertiaTensor = Matrix4x4.Identity,
            InverseInertiaTensor = Matrix4x4.Identity,
            MaterialId = 0
        };
        return block;
    }
}
