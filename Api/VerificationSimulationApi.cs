using System.Numerics;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Analysis.Geomechanics;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Analysis.PhysicoChem;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Analysis.Seismology;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Analysis.Thermodynamic;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Data.Pnm;

namespace GeoscientistToolkit.Api;

/// <summary>
///     Wraps the verification simulations described in <c>VerificationReport.md</c>.
/// </summary>
public class VerificationSimulationApi
{
    /// <summary>
    ///     Runs the Westerly Granite triaxial compression verification case.
    /// </summary>
    public GeomechanicsTriaxialResult RunGeomechanicsGraniteVerification(
        float cohesionMPa = 26.84f,
        float frictionAngleDeg = 51.0f,
        float confiningPressureMPa = 10.0f)
    {
        var phiRad = frictionAngleDeg * (float)Math.PI / 180f;
        var tanFactor = (float)Math.Tan((Math.PI / 4) + (phiRad / 2));
        var expectedSigma1 = confiningPressureMPa * (tanFactor * tanFactor) + 2 * cohesionMPa * tanFactor;

        var material = new PhysicalMaterial
        {
            Name = "Westerly Granite (Real)",
            YoungModulus_GPa = 35.0f,
            PoissonRatio = 0.25f,
            FrictionAngle_deg = frictionAngleDeg,
            CompressiveStrength_MPa = 200.0f,
            TensileStrength_MPa = 10.0f
        };
        material.Extra["Cohesion_MPa"] = cohesionMPa;

        var mesh = TriaxialMeshGenerator.GenerateCylindricalMesh(0.025f, 0.1f, 8, 12, 10);
        var loadParams = new TriaxialLoadingParameters
        {
            ConfiningPressure_MPa = confiningPressureMPa,
            LoadingMode = TriaxialLoadingMode.StrainControlled,
            AxialStrainRate_per_s = 1e-4f,
            MaxAxialStrain_percent = 1.5f,
            TotalTime_s = 150.0f,
            TimeStep_s = 0.1f,
            DrainageCondition = DrainageCondition.Drained
        };

        using var sim = new TriaxialSimulation();
        var results = sim.RunSimulationCPU(mesh, material, loadParams, FailureCriterion.MohrCoulomb);
        var errorPercent = Math.Abs(results.PeakStrength_MPa - expectedSigma1) / expectedSigma1 * 100f;
        var passed = errorPercent < 5.0f;

        return new GeomechanicsTriaxialResult(expectedSigma1, results.PeakStrength_MPa, errorPercent, passed);
    }

    /// <summary>
    ///     Runs the PREM upper crust wave propagation verification case.
    /// </summary>
    public SeismicPremResult RunSeismicPremVerification(
        double vpKmPerS = 5.8,
        double vsKmPerS = 3.2,
        double densityGPerCm3 = 2.6,
        double distanceKm = 10.0,
        double timeStepSeconds = 0.001)
    {
        var expectedArrival = distanceKm / vpKmPerS;

        var crustalModel = new CrustalModel();
        var type = new CrustalType();
        type.Layers.Add("upper_crust", new CrustalLayer
        {
            ThicknessKm = 20.0,
            VpKmPerS = vpKmPerS,
            VsKmPerS = vsKmPerS,
            DensityGPerCm3 = densityGPerCm3
        });
        crustalModel.CrustalTypes.Add("continental", type);
        crustalModel.CrustalTypes.Add("oceanic", type);
        crustalModel.CrustalTypes.Add("orogen", type);
        crustalModel.CrustalTypes.Add("rift", type);

        var engine = new WavePropagationEngine(crustalModel, 240, 40, 40, 50.0, 50.0, 50.0, timeStepSeconds, false);
        engine.InitializeMaterialProperties(0, 1, 0, 1);

        var sx = 20;
        var sy = 20;
        var sz = 20;
        var rx = 220;
        var ry = 20;
        var rz = 20;

        var sigma = 2.0;
        var ampTotal = 10000.0;

        for (var i = -2; i <= 2; i++)
        for (var j = -2; j <= 2; j++)
        for (var k = -2; k <= 2; k++)
        {
            var distSq = i * i + j * j + k * k;
            var val = ampTotal * Math.Exp(-distSq / (2 * sigma * sigma));
            engine.AddPointSource(sx + i, sy + j, sz + k, val, 0, 0, 0);
        }

        var trace = new List<double>();
        var steps = 3000;
        double maxAmp = 0;

        for (var t = 0; t < steps; t++)
        {
            engine.TimeStep();
            var wave = engine.GetWaveFieldAt(rx, ry, rz);
            var amp = Math.Abs(wave.Amplitude);
            trace.Add(amp);
            if (amp > maxAmp) maxAmp = amp;
        }

        var threshold = maxAmp * 0.1;
        var actualArrival = -1.0;
        for (var t = 0; t < steps; t++)
        {
            if (trace[t] > threshold)
            {
                actualArrival = t * timeStepSeconds;
                break;
            }
        }

        var errorPercent = actualArrival > 0
            ? Math.Abs(actualArrival - expectedArrival) / expectedArrival * 100.0
            : double.PositiveInfinity;
        var passed = actualArrival > 0 && errorPercent < 10.0;

        return new SeismicPremResult(expectedArrival, actualArrival, errorPercent, passed);
    }

    /// <summary>
    ///     Runs the slope stability gravity drop verification case.
    /// </summary>
    public SlopeGravityDropResult RunSlopeStabilityGravityVerification()
    {
        var dataset = new SlopeStabilityDataset();

        var floor = new Block { Id = 1, IsFixed = true, Mass = 1000f, MaterialId = 1, Volume = 1000f };
        floor.Vertices = new List<Vector3>
        {
            new(-10, -10, -1), new(10, -10, -1),
            new(10, 10, -1), new(-10, 10, -1),
            new(-10, -10, 0), new(10, -10, 0),
            new(10, 10, 0), new(-10, 10, 0)
        };
        floor.CalculateGeometricProperties();

        var h = 100.0f;
        var block = new Block { Id = 2, IsFixed = false, Mass = 10.0f, MaterialId = 1 };
        block.Vertices = new List<Vector3>
        {
            new(-0.5f, -0.5f, -0.5f + h), new(0.5f, -0.5f, -0.5f + h),
            new(0.5f, 0.5f, -0.5f + h), new(-0.5f, 0.5f, -0.5f + h),
            new(-0.5f, -0.5f, 0.5f + h), new(0.5f, -0.5f, 0.5f + h),
            new(0.5f, 0.5f, 0.5f + h), new(-0.5f, 0.5f, 0.5f + h)
        };
        block.CalculateGeometricProperties();

        dataset.Blocks.Add(floor);
        dataset.Blocks.Add(block);
        dataset.Materials.Add(new SlopeStabilityMaterial { Id = 1, FrictionAngle = 30f });

        var parameters = new SlopeStabilityParameters
        {
            TimeStep = 0.001f,
            TotalTime = 2.0f,
            Gravity = new Vector3(0, 0, -9.81f),
            UseCustomGravityDirection = true,
            SpatialHashGridSize = 10,
            LocalDamping = 0.0f
        };

        var sim = new SlopeStabilitySimulator(dataset, parameters);
        var results = sim.RunSimulation();
        var finalBlock = results.BlockResults.First(b => b.BlockId == 2);
        var displacement = finalBlock.Displacement.Length();
        var expected = 19.62f;
        var error = Math.Abs(displacement - expected);

        return new SlopeGravityDropResult(expected, displacement, error, error < 0.5f);
    }

    /// <summary>
    ///     Runs the slope stability sliding block verification case.
    /// </summary>
    public SlopeSlidingResult RunSlopeStabilitySlidingVerification()
    {
        var dataset = new SlopeStabilityDataset();

        var floor = new Block { Id = 1, IsFixed = true, Mass = 10000f, MaterialId = 1, Volume = 1000f };
        floor.Vertices = new List<Vector3>
        {
            new(-50, -50, -1), new(50, -50, -1),
            new(50, 50, -1), new(-50, 50, -1),
            new(-50, -50, 0), new(50, -50, 0),
            new(50, 50, 0), new(-50, 50, 0)
        };
        floor.CalculateGeometricProperties();

        var z0 = 0.01f;
        var z1 = 1.01f;
        var block = new Block { Id = 2, IsFixed = false, Mass = 1000.0f, MaterialId = 1 };
        block.Vertices = new List<Vector3>
        {
            new(-0.5f, -0.5f, z0), new(0.5f, -0.5f, z0),
            new(0.5f, 0.5f, z0), new(-0.5f, 0.5f, z0),
            new(-0.5f, -0.5f, z1), new(0.5f, -0.5f, z1),
            new(0.5f, 0.5f, z1), new(-0.5f, 0.5f, z1)
        };
        block.CalculateGeometricProperties();

        dataset.Blocks.Add(floor);
        dataset.Blocks.Add(block);

        var frictionAngle = 30.0f;
        dataset.Materials.Add(new SlopeStabilityMaterial
        {
            Id = 1,
            FrictionAngle = frictionAngle,
            YoungModulus = 100000.0f,
            Cohesion = 0f
        });

        var g = 9.81f;
        var angle = 45f * (float)Math.PI / 180f;
        var gravity = new Vector3(g * (float)Math.Sin(angle), 0, -g * (float)Math.Cos(angle));

        var fricRad = frictionAngle * (float)Math.PI / 180f;
        var expectedAcc = g * (float)Math.Sin(angle) - g * (float)Math.Cos(angle) * (float)Math.Tan(fricRad);
        var time = 1.0f;
        var expectedDist = 0.5f * expectedAcc * time * time;

        var parameters = new SlopeStabilityParameters
        {
            TimeStep = 0.0001f,
            TotalTime = time,
            Gravity = gravity,
            UseCustomGravityDirection = true,
            SpatialHashGridSize = 5,
            IncludeRotation = false,
            LocalDamping = 0.0f,
            SaveIntermediateStates = true,
            OutputFrequency = 100
        };

        var sim = new SlopeStabilitySimulator(dataset, parameters);
        var results = sim.RunSimulation(null, _ => { });
        var finalBlock = results.BlockResults.First(b => b.BlockId == 2);
        var actualDist = finalBlock.FinalPosition.X;
        var error = Math.Abs(actualDist - expectedDist);

        return new SlopeSlidingResult(expectedDist, actualDist, error, !float.IsNaN(actualDist) && error < 1.2f);
    }

    /// <summary>
    ///     Runs the water saturation pressure verification case.
    /// </summary>
    public WaterSaturationResult RunWaterSaturationPressureVerification(double temperatureKelvin = 373.15)
    {
        var expectedPa = 101325.0;
        var psatMPa = PhaseTransitionHandler.GetSaturationPressure(temperatureKelvin);
        var simulatedPa = psatMPa * 1e6;
        var error = Math.Abs(simulatedPa - expectedPa);
        var passed = error < 1000.0;

        return new WaterSaturationResult(expectedPa, simulatedPa, error, passed);
    }

    /// <summary>
    ///     Runs the PNM permeability verification case.
    /// </summary>
    public PnmPermeabilityResult RunPnmPermeabilityVerification()
    {
        var dataset = new PNMDataset("test", "test.pnm")
        {
            VoxelSize = 1.0f,
            ImageWidth = 10,
            ImageHeight = 10,
            ImageDepth = 20
        };

        dataset.Pores.Add(new Pore { ID = 100, Radius = 0.1f, Position = new Vector3(0, 0, 0) });
        dataset.Pores.Add(new Pore { ID = 101, Radius = 0.1f, Position = new Vector3(10, 10, 0) });
        dataset.Pores.Add(new Pore { ID = 102, Radius = 0.1f, Position = new Vector3(0, 0, 19) });
        dataset.Pores.Add(new Pore { ID = 103, Radius = 0.1f, Position = new Vector3(10, 10, 19) });

        dataset.Pores.Add(new Pore { ID = 0, Radius = 1.0f, Position = new Vector3(5, 5, 0) });
        dataset.Pores.Add(new Pore { ID = 1, Radius = 1.0f, Position = new Vector3(5, 5, 10) });
        dataset.Pores.Add(new Pore { ID = 2, Radius = 1.0f, Position = new Vector3(5, 5, 19) });

        dataset.Throats.Add(new Throat { ID = 0, Radius = 1.0f, Pore1ID = 0, Pore2ID = 1 });
        dataset.Throats.Add(new Throat { ID = 1, Radius = 1.0f, Pore1ID = 1, Pore2ID = 2 });

        dataset.InitializeFromCurrentLists();

        var options = new PermeabilityOptions
        {
            Dataset = dataset,
            FluidViscosity = 1.0f,
            InletPressure = 200.0f,
            OutletPressure = 100.0f,
            Axis = FlowAxis.Z,
            CalculateDarcy = true
        };

        AbsolutePermeability.Calculate(options);

        return new PnmPermeabilityResult(dataset.DarcyPermeability, dataset.DarcyPermeability > 0);
    }

    /// <summary>
    ///     Runs the speed of sound verification case for seawater.
    /// </summary>
    public AcousticSpeedResult RunAcousticSpeedVerification()
    {
        const int nx = 100;
        const int ny = 10;
        const int nz = 10;
        const float dx = 1.0f;
        const float dt = 0.0001f;

        var vx = new float[nx, ny, nz];
        var vy = new float[nx, ny, nz];
        var vz = new float[nx, ny, nz];
        var sxx = new float[nx, ny, nz];
        var syy = new float[nx, ny, nz];
        var szz = new float[nx, ny, nz];
        var sxy = new float[nx, ny, nz];
        var sxz = new float[nx, ny, nz];
        var syz = new float[nx, ny, nz];

        var e = new float[nx, ny, nz];
        var nu = new float[nx, ny, nz];
        var rho = new float[nx, ny, nz];

        var targetK = 2.2e9f;
        var targetNu = 0.49f;
        var targetE = 3 * targetK * (1 - 2 * targetNu);
        var targetRho = 1000.0f;
        var expectedV = (float)Math.Sqrt(targetK / targetRho);

        for (var x = 0; x < nx; x++)
        for (var y = 0; y < ny; y++)
        for (var z = 0; z < nz; z++)
        {
            e[x, y, z] = targetE;
            nu[x, y, z] = targetNu;
            rho[x, y, z] = targetRho;
        }

        var simParams = new GeoscientistToolkit.Analysis.AcousticSimulation.SimulationParameters();
        var sim = new AcousticSimulatorCPU(simParams);

        sxx[10, 5, 5] = 1000.0f;
        syy[10, 5, 5] = 1000.0f;
        szz[10, 5, 5] = 1000.0f;

        var rx = 60;
        var arrival = -1f;

        for (var step = 0; step < 1000; step++)
        {
            sim.UpdateWaveField(vx, vy, vz, sxx, syy, szz, sxy, sxz, syz, e, nu, rho, dt, dx, 0.0f);
            var p = (sxx[rx, 5, 5] + syy[rx, 5, 5] + szz[rx, 5, 5]) / 3.0f;
            if (Math.Abs(p) > 0.1f && arrival < 0)
            {
                arrival = step * dt;
                break;
            }
        }

        var dist = (rx - 10) * dx;
        var actualV = arrival > 0 ? dist / arrival : 0f;
        var error = Math.Abs(actualV - expectedV);
        var passed = arrival > 0 && error < 200.0f;

        return new AcousticSpeedResult(expectedV, actualV, error, passed);
    }

    /// <summary>
    ///     Runs the 1D heat conduction verification case.
    /// </summary>
    public HeatTransferResult RunHeatTransferVerification()
    {
        var state = new PhysicoChemState((10, 3, 3));

        for (var i = 0; i < 10; i++)
        for (var j = 0; j < 3; j++)
        for (var k = 0; k < 3; k++)
            state.Temperature[i, j, k] = 0.0f;

        var solver = new HeatTransferSolver();
        var steps = 2000;

        for (var t = 0; t < steps; t++)
        {
            for (var j = 0; j < 3; j++)
            for (var k = 0; k < 3; k++)
            {
                state.Temperature[0, j, k] = 100.0f;
                state.Temperature[9, j, k] = 0.0f;
            }

            solver.SolveHeat(state, 1.0, null);

            for (var i = 1; i < 9; i++)
            {
                var centerVal = state.Temperature[i, 1, 1];
                for (var j = 0; j < 3; j++)
                for (var k = 0; k < 3; k++)
                    state.Temperature[i, j, k] = centerVal;
            }
        }

        var t1 = state.Temperature[1, 1, 1];
        var t5 = state.Temperature[5, 1, 1];
        var passed = t1 > 80.0f && t1 < 90.0f;

        return new HeatTransferResult(t1, t5, passed);
    }

    /// <summary>
    ///     Runs the D8 flow accumulation verification case.
    /// </summary>
    public HydrologyFlowResult RunHydrologyFlowVerification()
    {
        var dem = new float[5, 5];
        for (var x = 0; x < 5; x++)
        for (var y = 0; y < 5; y++)
            dem[x, y] = 10.0f + (float)Math.Sqrt((x - 2) * (x - 2) + (y - 2) * (y - 2));

        var flowDir = GISOperationsImpl.CalculateD8FlowDirection(dem);
        var accum = GISOperationsImpl.CalculateFlowAccumulation(flowDir);
        var centerAccum = accum[2, 2];

        return new HydrologyFlowResult(centerAccum, centerAccum > 5);
    }

    /// <summary>
    ///     Runs the geothermal borehole heat exchanger verification case.
    /// </summary>
    public async Task<GeothermalBoreholeResult> RunGeothermalBoreholeVerificationAsync(
        CancellationToken cancellationToken = default)
    {
        var options = new GeothermalSimulationOptions
        {
            SimulationTime = 3600,
            TimeStep = 60,
            RadialGridPoints = 10,
            AngularGridPoints = 8,
            VerticalGridPoints = 10,
            DomainRadius = 10.0,
            BoreholeDataset = new BoreholeDataset("TestBorehole", "Verification")
            {
                TotalDepth = 100.0f,
                WellDiameter = 0.2f,
                LithologyUnits = new List<LithologyUnit>
                {
                    new() { DepthFrom = 0, DepthTo = 100, Name = "Granite" }
                }
            },
            HeatExchangerDepth = 100.0f,
            FluidInletTemperature = 293.15,
            FluidMassFlowRate = 0.5
        };

        options.SetDefaultValues();

        var mesh = GeothermalMeshGenerator.GenerateCylindricalMesh(options.BoreholeDataset, options);
        var solver = new GeothermalSimulationSolver(options, mesh, null, cancellationToken);
        var results = await solver.RunSimulationAsync();

        var outlet = results.OutletTemperature.LastOrDefault();
        var outletC = outlet.temperature - 273.15;
        var passed = outletC < 19.8 && outletC > 10.0;

        return new GeothermalBoreholeResult(outletC, passed);
    }

    /// <summary>
    ///     Runs the deep coaxial geothermal verification case.
    /// </summary>
    public async Task<GeothermalCoaxialResult> RunGeothermalCoaxialVerificationAsync(
        CancellationToken cancellationToken = default)
    {
        var options = new GeothermalSimulationOptions
        {
            SimulationTime = 3600 * 24,
            TimeStep = 300,
            RadialGridPoints = 15,
            AngularGridPoints = 8,
            VerticalGridPoints = 30,
            DomainRadius = 20.0,
            BoreholeDataset = new BoreholeDataset("DeepWell", "Verification")
            {
                TotalDepth = 3000.0f,
                WellDiameter = 0.25f,
                LithologyUnits = new List<LithologyUnit>
                {
                    new() { DepthFrom = 0, DepthTo = 3000, Name = "Granite" }
                }
            },
            HeatExchangerType = HeatExchangerType.Coaxial,
            FlowConfiguration = FlowConfiguration.CounterFlowReversed,
            HeatExchangerDepth = 3000.0f,
            AverageGeothermalGradient = 0.06,
            SurfaceTemperature = 288.15,
            FluidInletTemperature = 313.15,
            FluidMassFlowRate = 2.0,
            InnerPipeThermalConductivity = 0.02
        };

        options.SetDefaultValues();

        var mesh = GeothermalMeshGenerator.GenerateCylindricalMesh(options.BoreholeDataset, options);
        var solver = new GeothermalSimulationSolver(options, mesh, null, cancellationToken);
        var results = await solver.RunSimulationAsync();

        var outlet = results.OutletTemperature.LastOrDefault();
        var outletC = outlet.temperature - 273.15;
        var passed = outletC > 60.0;

        return new GeothermalCoaxialResult(outletC, passed);
    }
}
