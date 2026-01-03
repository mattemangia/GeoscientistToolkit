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
            MaxAxialStrain_percent = 5f,
            EnableHeterogeneity = false
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

    /// <summary>
    /// Deep Geothermal Reservoir test with PhysicoChem dataset.
    /// Tests: 16x16x16 cube with heterogeneous thermal conductivity, coaxial heat exchanger,
    /// natural gas intrusion from fracture, and bubble rise simulation.
    /// Generates PNG cross-section images of pressure gradient/bubbles and heat exchange.
    /// </summary>
    [Fact]
    public void PhysicoChem_DeepGeothermalReservoir_WithCoaxialExchangerAndGasBubbles()
    {
        // ==================== CONFIGURATION ====================
        const int nx = 16, ny = 16, nz = 16;
        const double domainSize = 100.0; // meters (100m x 100m x 100m deep reservoir)
        const double dx = domainSize / nx;
        const double dt = 0.1; // seconds
        const int maxSteps = 500;
        const double convergenceTolerance = 1e-4;

        // ==================== CREATE PHYSICOCHEM STATE ====================
        var state = new PhysicoChemState((nx, ny, nz));

        // Initialize thermal conductivity field with different values per row (k layer)
        // Simulates layered geology: shallow = 1.5 W/mK, middle = 2.5 W/mK, deep = 4.0 W/mK
        var thermalConductivity = new float[nx, ny, nz];
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            // Different conductivity per row (layer)
            double depthFraction = (double)k / (nz - 1);
            double k_thermal = 1.5 + depthFraction * 2.5; // 1.5 W/mK at top to 4.0 W/mK at bottom
            thermalConductivity[i, j, k] = (float)k_thermal;

            // Set initial temperature (geothermal gradient: 30°C/km = 0.03°C/m)
            double depth = k * dx;
            double T_surface = 15.0 + 273.15; // 15°C at surface
            double T_gradient = 0.03; // °C/m
            state.Temperature[i, j, k] = (float)(T_surface + depth * T_gradient);

            // Set hydrostatic pressure
            double P_surface = 101325.0; // Pa
            double rho_water = 1000.0;
            double g = 9.81;
            state.Pressure[i, j, k] = (float)(P_surface + rho_water * g * depth);

            // Set porosity and permeability (typical for reservoir rock)
            state.Porosity[i, j, k] = 0.2f; // 20% porosity
            state.Permeability[i, j, k] = 1e-13f; // 100 mD
            state.InitialPermeability[i, j, k] = state.Permeability[i, j, k];

            // Initialize saturation (water-saturated)
            state.LiquidSaturation[i, j, k] = 1.0f;
            state.GasSaturation[i, j, k] = 0.0f;
            state.VaporSaturation[i, j, k] = 0.0f;
        }

        // ==================== COAXIAL HEAT EXCHANGER ====================
        // Place coaxial probe in the center of the domain
        int probeX = nx / 2, probeY = ny / 2;
        float probeInletTemp = 10.0f + 273.15f; // 10°C inlet temperature

        // Mark exchanger cells (central column) with high thermal conductivity
        // and set as heat sink (fluid extracts heat)
        for (int k = 0; k < nz; k++)
        {
            // Inner pipe (downgoing cold fluid) - cooling the rock
            thermalConductivity[probeX, probeY, k] = 50.0f; // Steel pipe conductivity

            // Set low temperature in probe (simulates cold fluid flow)
            // Temperature increases as fluid goes down (picks up heat)
            double fluidTempIncrease = (double)k / nz * 30.0; // Picks up 30°C over depth
            state.Temperature[probeX, probeY, k] = (float)(probeInletTemp + fluidTempIncrease);
        }

        // ==================== NATURAL GAS FRACTURE INTRUSION ====================
        // Create a fracture zone at the bottom with gas intrusion
        int fractureK = 1; // Near bottom
        int fractureCenterX = nx / 4;
        int fractureCenterY = ny / 4;
        int fractureRadius = 2;

        for (int i = fractureCenterX - fractureRadius; i <= fractureCenterX + fractureRadius; i++)
        for (int j = fractureCenterY - fractureRadius; j <= fractureCenterY + fractureRadius; j++)
        {
            if (i >= 0 && i < nx && j >= 0 && j < ny)
            {
                // High permeability fracture
                state.Permeability[i, j, fractureK] = 1e-11f; // 10 Darcy (fracture)

                // Inject natural gas (methane) - 30% gas saturation
                state.GasSaturation[i, j, fractureK] = 0.3f;
                state.LiquidSaturation[i, j, fractureK] = 0.7f;
            }
        }

        // ==================== CREATE HEAT TRANSFER SOLVER ====================
        var heatSolver = new HeatTransferSolver();
        heatSolver.ThermalConductivity = thermalConductivity;
        heatSolver.HeatParams.GridSpacing = dx;
        heatSolver.HeatParams.Density = 2650.0; // Rock density kg/m³
        heatSolver.HeatParams.SpecificHeat = 850.0; // Rock specific heat J/kg·K

        // Define heat source for coaxial exchanger (heat extraction)
        heatSolver.HeatParams.HeatSourceFunction = (i, j, k, t) =>
        {
            if (i == probeX && j == probeY)
            {
                // Negative heat source = heat extraction by the probe
                return -50000.0; // 50 kW/m³ heat extraction
            }
            return 0.0;
        };

        // ==================== CREATE FLOW SOLVER ====================
        var flowSolver = new FlowSolver();
        flowSolver.MultiphaseParams.GridSpacing = dx;
        flowSolver.MultiphaseParams.WaterDensity = 1000.0;
        flowSolver.MultiphaseParams.GasDensity = 0.7; // Methane at reservoir conditions
        flowSolver.MultiphaseParams.WaterViscosity = 0.001;
        flowSolver.MultiphaseParams.GasViscosity = 1.5e-5;
        flowSolver.MultiphaseParams.ResidualLiquidSaturation = 0.05;
        flowSolver.MultiphaseParams.ResidualGasSaturation = 0.02;
        flowSolver.MultiphaseParams.VanGenuchten_m = 0.45;

        // Boundary conditions list (empty for now - using default zero-gradient)
        var bcs = new List<BoundaryCondition>();

        // ==================== SIMULATION LOOP - RUN UNTIL CONVERGENCE ====================
        double prevTotalGas = 0;
        double prevAvgTemp = 0;
        int step = 0;
        bool converged = false;

        while (step < maxSteps && !converged)
        {
            step++;
            state.CurrentTime = step * dt;

            // Apply gravity forces
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
            {
                state.ForceX[i, j, k] = 0;
                state.ForceY[i, j, k] = 0;
                state.ForceZ[i, j, k] = -9810f; // Gravity * water density
            }

            // Solve flow (updates velocities and gas transport)
            flowSolver.SolveFlow(state, dt, bcs);

            // Solve heat transfer
            heatSolver.SolveHeat(state, dt, bcs);

            // Continuously inject gas from fracture (simulates ongoing gas intrusion)
            for (int i = fractureCenterX - fractureRadius; i <= fractureCenterX + fractureRadius; i++)
            for (int j = fractureCenterY - fractureRadius; j <= fractureCenterY + fractureRadius; j++)
            {
                if (i >= 0 && i < nx && j >= 0 && j < ny)
                {
                    // Add small amount of gas each timestep
                    float newGas = state.GasSaturation[i, j, fractureK] + 0.01f;
                    if (newGas < 0.5f) // Cap at 50%
                    {
                        state.GasSaturation[i, j, fractureK] = newGas;
                        state.LiquidSaturation[i, j, fractureK] = 1.0f - newGas;
                    }
                }
            }

            // Check convergence
            double totalGas = 0;
            double avgTemp = 0;
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
            {
                totalGas += state.GasSaturation[i, j, k];
                avgTemp += state.Temperature[i, j, k];
            }
            avgTemp /= (nx * ny * nz);

            double gasChange = Math.Abs(totalGas - prevTotalGas) / (Math.Abs(prevTotalGas) + 1e-10);
            double tempChange = Math.Abs(avgTemp - prevAvgTemp) / (Math.Abs(prevAvgTemp) + 1e-10);

            if (step > 50 && gasChange < convergenceTolerance && tempChange < convergenceTolerance)
            {
                converged = true;
            }

            prevTotalGas = totalGas;
            prevAvgTemp = avgTemp;
        }

        // ==================== GENERATE PNG IMAGES ====================
        string outputDir = Path.Combine(Path.GetTempPath(), "GeothermalTest");
        Directory.CreateDirectory(outputDir);

        // Cross-section at Y = ny/2
        int sliceY = ny / 2;

        // IMAGE 1: Pressure gradient and gas bubbles (X-Z cross section)
        string pressureBubblesPath = Path.Combine(outputDir, "pressure_bubbles_crosssection.png");
        GeneratePressureBubblesImage(state, sliceY, nx, nz, pressureBubblesPath);

        // IMAGE 2: Heat exchanger with temperature field (X-Z cross section)
        string heatExchangePath = Path.Combine(outputDir, "heat_exchanger_crosssection.png");
        GenerateHeatExchangeImage(state, thermalConductivity, probeX, sliceY, nx, nz, heatExchangePath);

        // ==================== ASSERTIONS ====================
        // 1. Verify gas has risen from the bottom fracture
        double gasAtTop = 0, gasAtBottom = 0;
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        {
            gasAtTop += state.GasSaturation[i, j, nz - 2]; // Near top
            gasAtBottom += state.GasSaturation[i, j, 1];   // Near bottom
        }

        // Gas should have moved upward due to buoyancy
        Assert.True(gasAtTop > 0, "Gas bubbles should rise to the top due to buoyancy");

        // 2. Verify heat exchanger has cooled the surrounding rock
        double tempNearProbe = state.Temperature[probeX + 1, probeY, nz / 2];
        double tempFarFromProbe = state.Temperature[0, 0, nz / 2];
        Assert.True(tempNearProbe < tempFarFromProbe,
            "Temperature near the coaxial heat exchanger should be lower than far from it");

        // 3. Verify pressure gradient exists (hydrostatic)
        double pressureTop = state.Pressure[nx / 2, ny / 2, nz - 1];
        double pressureBottom = state.Pressure[nx / 2, ny / 2, 0];
        Assert.True(pressureBottom > pressureTop,
            "Pressure should increase with depth (hydrostatic)");

        // 4. Verify temperature gradient (geothermal)
        double tempTop = state.Temperature[0, 0, nz - 1];
        double tempBottom = state.Temperature[0, 0, 0];
        Assert.True(tempBottom > tempTop,
            "Temperature should increase with depth (geothermal gradient)");

        // 5. Verify PNG files were created
        Assert.True(File.Exists(pressureBubblesPath), $"Pressure/bubbles PNG should exist at {pressureBubblesPath}");
        Assert.True(File.Exists(heatExchangePath), $"Heat exchange PNG should exist at {heatExchangePath}");

        // 6. Verify simulation converged or ran enough steps
        Assert.True(step >= 50, $"Simulation should run at least 50 steps, ran {step}");

        // Log results for verification report
        Console.WriteLine($"=== Deep Geothermal Reservoir Test Results ===");
        Console.WriteLine($"Grid size: {nx}x{ny}x{nz}");
        Console.WriteLine($"Domain size: {domainSize}m x {domainSize}m x {domainSize}m");
        Console.WriteLine($"Simulation steps: {step}, Converged: {converged}");
        Console.WriteLine($"Gas at top: {gasAtTop:F4}, Gas at bottom: {gasAtBottom:F4}");
        Console.WriteLine($"Temperature near probe: {tempNearProbe - 273.15:F1}°C");
        Console.WriteLine($"Temperature far from probe: {tempFarFromProbe - 273.15:F1}°C");
        Console.WriteLine($"Pressure at bottom: {pressureBottom / 1e6:F2} MPa");
        Console.WriteLine($"Pressure at top: {pressureTop / 1e6:F2} MPa");
        Console.WriteLine($"PNG outputs: {pressureBubblesPath}");
        Console.WriteLine($"            {heatExchangePath}");
    }

    /// <summary>
    /// Generates a PNG showing pressure gradient and gas bubbles in cross-section
    /// </summary>
    private static void GeneratePressureBubblesImage(PhysicoChemState state, int sliceY, int nx, int nz, string filePath)
    {
        // Create RGBA image data (4 bytes per pixel)
        var imageData = new byte[nx * nz * 4];

        // Find pressure range for normalization
        float minP = float.MaxValue, maxP = float.MinValue;
        float maxGas = 0;
        for (int i = 0; i < nx; i++)
        for (int k = 0; k < nz; k++)
        {
            minP = Math.Min(minP, state.Pressure[i, sliceY, k]);
            maxP = Math.Max(maxP, state.Pressure[i, sliceY, k]);
            maxGas = Math.Max(maxGas, state.GasSaturation[i, sliceY, k]);
        }

        // Generate pixels
        for (int k = 0; k < nz; k++)
        for (int i = 0; i < nx; i++)
        {
            int idx = ((nz - 1 - k) * nx + i) * 4; // Flip Y for image coordinates

            float pressure = state.Pressure[i, sliceY, k];
            float gas = state.GasSaturation[i, sliceY, k];

            // Normalize pressure to 0-1
            float pNorm = (pressure - minP) / (maxP - minP + 1e-10f);

            // Background color: blue gradient for pressure (low=light, high=dark)
            byte r = (byte)(50 + (1 - pNorm) * 150);
            byte g = (byte)(50 + (1 - pNorm) * 100);
            byte b = (byte)(150 + (1 - pNorm) * 100);

            // Overlay gas bubbles as bright yellow/white circles
            if (gas > 0.01f)
            {
                float gasIntensity = Math.Min(1.0f, gas / 0.3f);
                r = (byte)(r + (255 - r) * gasIntensity);
                g = (byte)(g + (255 - g) * gasIntensity);
                b = (byte)(b * (1 - gasIntensity * 0.5f));
            }

            imageData[idx] = r;
            imageData[idx + 1] = g;
            imageData[idx + 2] = b;
            imageData[idx + 3] = 255; // Alpha
        }

        // Write PNG using StbImageSharp
        using var stream = File.Create(filePath);
        var writer = new StbImageWriteSharp.ImageWriter();
        writer.WritePng(imageData, nx, nz, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }

    /// <summary>
    /// Generates a PNG showing the heat exchanger and temperature field in cross-section
    /// </summary>
    private static void GenerateHeatExchangeImage(PhysicoChemState state, float[,,] thermalConductivity,
        int probeX, int sliceY, int nx, int nz, string filePath)
    {
        var imageData = new byte[nx * nz * 4];

        // Find temperature range for normalization
        float minT = float.MaxValue, maxT = float.MinValue;
        for (int i = 0; i < nx; i++)
        for (int k = 0; k < nz; k++)
        {
            minT = Math.Min(minT, state.Temperature[i, sliceY, k]);
            maxT = Math.Max(maxT, state.Temperature[i, sliceY, k]);
        }

        // Generate pixels
        for (int k = 0; k < nz; k++)
        for (int i = 0; i < nx; i++)
        {
            int idx = ((nz - 1 - k) * nx + i) * 4;

            float temp = state.Temperature[i, sliceY, k];
            float tNorm = (temp - minT) / (maxT - minT + 1e-10f);

            // Temperature colormap: blue (cold) -> white -> red (hot)
            byte r, g, b;
            if (tNorm < 0.5f)
            {
                float t2 = tNorm * 2;
                r = (byte)(t2 * 255);
                g = (byte)(t2 * 255);
                b = 255;
            }
            else
            {
                float t2 = (tNorm - 0.5f) * 2;
                r = 255;
                g = (byte)((1 - t2) * 255);
                b = (byte)((1 - t2) * 255);
            }

            // Mark the heat exchanger probe as black/dark gray
            if (i == probeX)
            {
                r = 40;
                g = 40;
                b = 40;
            }

            // Show thermal conductivity layers as subtle texture
            float kNorm = (thermalConductivity[i, sliceY, k] - 1.5f) / 2.5f;
            r = (byte)Math.Min(255, r + kNorm * 20);

            imageData[idx] = r;
            imageData[idx + 1] = g;
            imageData[idx + 2] = b;
            imageData[idx + 3] = 255;
        }

        using var stream = File.Create(filePath);
        var writer = new StbImageWriteSharp.ImageWriter();
        writer.WritePng(imageData, nx, nz, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
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
