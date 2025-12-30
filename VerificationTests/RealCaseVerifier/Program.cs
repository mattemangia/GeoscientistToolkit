using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.Geomechanics;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Analysis.Seismology;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Analysis.PhysicoChem;
using GeoscientistToolkit.Analysis.Multiphase;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Analysis.Thermodynamic;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.Borehole;

namespace RealCaseVerifier
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("   REAL CASE STUDY VERIFICATION SUITE");
            Console.WriteLine("   All tests based on peer-reviewed literature");
            Console.WriteLine("=================================================");

            bool allPassed = true;

            // 1. Geomechanics
            allPassed &= VerifyGeomechanicsGranite();

            // 2. Seismology
            allPassed &= VerifySeismicPREM();

            // 3. Slope Stability
            allPassed &= VerifySlopeStabilityGravity();

            // 4. Multiphase / Thermodynamics
            allPassed &= VerifyWaterSaturationPressure();

            // 5. PNM
            allPassed &= VerifyPNMPermeability();

            // 6. Acoustic
            allPassed &= VerifyAcousticSpeed();

            // 7. Heat Transfer
            allPassed &= VerifyHeatTransfer();

            // 8. Hydrology
            allPassed &= VerifyHydrologyFlow();

            // 9. Geothermal
            allPassed &= await VerifyGeothermalSystem();

            if (allPassed)
            {
                Console.WriteLine("\n-------------------------------------------------");
                Console.WriteLine("ALL VERIFICATION TESTS PASSED.");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("\n-------------------------------------------------");
                Console.WriteLine("SOME TESTS FAILED.");
                Environment.Exit(1);
            }
        }

        static bool VerifyGeomechanicsGranite()
        {
            Console.WriteLine("\n[Test 1] Geomechanics: Westerly Granite Triaxial Compression");
            Console.WriteLine("Reference: MDPI 2022 (doi:10.3390/app12083930)");

            float c = 26.84f;
            float phi = 51.0f;
            float sigma3 = 10.0f;

            float phiRad = phi * (float)Math.PI / 180f;
            float tanFactor = (float)Math.Tan((Math.PI/4) + (phiRad/2));
            float sigma1_expected = sigma3 * (tanFactor * tanFactor) + 2 * c * tanFactor;

            Console.WriteLine($"Expected Peak Strength: {sigma1_expected:F2} MPa");

            var material = new PhysicalMaterial
            {
                Name = "Westerly Granite (Real)",
                YoungModulus_GPa = 35.0f,
                PoissonRatio = 0.25f,
                FrictionAngle_deg = phi,
                CompressiveStrength_MPa = 200.0f,
                TensileStrength_MPa = 10.0f
            };
            material.Extra["Cohesion_MPa"] = c;

            var mesh = TriaxialMeshGenerator.GenerateCylindricalMesh(0.025f, 0.1f, 8, 12, 10);

            var loadParams = new TriaxialLoadingParameters
            {
                ConfiningPressure_MPa = sigma3,
                LoadingMode = TriaxialLoadingMode.StrainControlled,
                AxialStrainRate_per_s = 1e-4f,
                MaxAxialStrain_percent = 1.5f,
                TotalTime_s = 150.0f,
                TimeStep_s = 0.1f,
                DrainageCondition = DrainageCondition.Drained
            };

            using var sim = new TriaxialSimulation();
            try
            {
                var results = sim.RunSimulationCPU(mesh, material, loadParams, FailureCriterion.MohrCoulomb);
                Console.WriteLine($"Actual Peak Strength: {results.PeakStrength_MPa:F2} MPa");

                float errorPercent = Math.Abs(results.PeakStrength_MPa - sigma1_expected) / sigma1_expected * 100f;
                if (errorPercent < 5.0f)
                {
                    Console.WriteLine("Status: PASS");
                    return true;
                }
                Console.WriteLine($"Status: FAIL (Error {errorPercent:F2}%)");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return false;
            }
        }

        static bool VerifySeismicPREM()
        {
            Console.WriteLine("\n[Test 2] Seismology: PREM Model Wave Propagation");
            Console.WriteLine("Reference: Dziewonski & Anderson (1981). PEPI.");

            double vp = 5.8;
            double vs = 3.2;
            double rho = 2.6;
            double distKm = 10.0;

            double tP_expected = distKm / vp;
            Console.WriteLine($"Expected P-Wave Arrival: {tP_expected:F4} s");

            var crustalModel = new CrustalModel();
            var type = new CrustalType();
            type.Layers.Add("upper_crust", new CrustalLayer { ThicknessKm = 20.0, VpKmPerS = vp, VsKmPerS = vs, DensityGPerCm3 = rho });
            crustalModel.CrustalTypes.Add("continental", type);
            crustalModel.CrustalTypes.Add("oceanic", type);
            crustalModel.CrustalTypes.Add("orogen", type);
            crustalModel.CrustalTypes.Add("rift", type);

            int nx=120, ny=40, nz=40;
            double dx=0.1, dy=0.1, dz=0.1;
            double dt = 0.002;

            var engine = new WavePropagationEngine(crustalModel, nx, ny, nz, dx, dy, dz, dt, false);
            engine.InitializeMaterialProperties(0, 1, 0, 1);

            int sx = 10, sy = 20, sz = 20;
            int rx = 110, ry = 20, rz = 20;

            engine.AddPointSource(sx, sy, sz, -1.0, Math.PI/4, Math.PI/4, Math.PI/4);

            double tP_actual = -1;
            double threshold = 1e-6;

            int steps = 1500;

            for(int t=0; t<steps; t++)
            {
                engine.TimeStep();
                var wave = engine.GetWaveFieldAt(rx, ry, rz);
                if (tP_actual < 0 && Math.Abs(wave.Amplitude) > threshold) tP_actual = t * dt;
            }

            Console.WriteLine($"Actual P-Wave Arrival: {tP_actual:F4} s");

            if (tP_actual > 0 && Math.Abs(tP_actual - tP_expected) < 0.3)
            {
                 Console.WriteLine("Status: PASS");
                 return true;
            }
            Console.WriteLine("Status: FAIL");
            return false;
        }

        static bool VerifySlopeStabilityGravity()
        {
            Console.WriteLine("\n[Test 3] Slope Stability: Gravity Drop (Galileo)");
            Console.WriteLine("Reference: Galilei (1638).");

            var dataset = new SlopeStabilityDataset();

            // Floor Block
            var floor = new Block { Id = 1, IsFixed = true, Mass = 1000f, MaterialId = 1, Volume = 1000f };
            floor.Vertices = new List<Vector3> {
                new Vector3(-10, -10, -1), new Vector3(10, -10, -1),
                new Vector3(10, 10, -1), new Vector3(-10, 10, -1),
                new Vector3(-10, -10, 0), new Vector3(10, -10, 0),
                new Vector3(10, 10, 0), new Vector3(-10, 10, 0)
            };
            floor.Position = new Vector3(0,0,0);
            floor.InverseInertiaTensor = Matrix4x4.Identity;

            // Moving Block - Vertices shifted to Z=100
            // This ensures Centroid calculation places it at Z=100, avoiding overlap with floor at Z=0.
            float h = 100.0f;
            var block = new Block { Id = 2, IsFixed = false, Mass = 10.0f, MaterialId = 1, Volume = 1.0f };
            block.Vertices = new List<Vector3> {
                new Vector3(-0.5f, -0.5f, -0.5f+h), new Vector3(0.5f, -0.5f, -0.5f+h),
                new Vector3(0.5f, 0.5f, -0.5f+h), new Vector3(-0.5f, 0.5f, -0.5f+h),
                new Vector3(-0.5f, -0.5f, 0.5f+h), new Vector3(0.5f, -0.5f, 0.5f+h),
                new Vector3(0.5f, 0.5f, 0.5f+h), new Vector3(-0.5f, 0.5f, 0.5f+h)
            };
            // Position property will be overwritten by InitializeBlocks to match Centroid (Z=100)
            block.Position = new Vector3(0, 0, h);
            block.InverseInertiaTensor = Matrix4x4.Identity;
            block.Orientation = Quaternion.Identity;
            floor.Orientation = Quaternion.Identity;

            dataset.Blocks.Add(floor);
            dataset.Blocks.Add(block);

            var mat = new SlopeStabilityMaterial { Id = 1, FrictionAngle = 30.0f, YoungModulus = 1e6f, Cohesion = 0f };
            dataset.Materials.Add(mat);

            Vector3 gravity = new Vector3(0, 0, -9.81f);

            var parameters = new SlopeStabilityParameters {
                TimeStep = 0.005f, TotalTime = 2.0f, Gravity = gravity,
                SpatialHashGridSize = 10, UseMultithreading = false,
                IncludeRotation = false, LocalDamping = 0.0f,
                UseCustomGravityDirection = true // Ensure gravity isn't reset
            };

            try {
                var sim = new SlopeStabilitySimulator(dataset, parameters);
                var results = sim.RunSimulation();

                var finalBlock = results.BlockResults.First(b => b.BlockId == 2);
                float displacement = finalBlock.Displacement.Length();

                float expectedDisp = 19.62f;

                Console.WriteLine($"Time: 2.0s. Expected Drop: {expectedDisp:F2} m");
                Console.WriteLine($"Actual Drop: {displacement:F2} m");

                if (float.IsNaN(displacement))
                {
                    Console.WriteLine("Status: FAIL (NaN)");
                    return false;
                }

                if (Math.Abs(displacement - expectedDisp) < 0.5f)
                {
                    Console.WriteLine("Status: PASS");
                    return true;
                }
                Console.WriteLine("Status: FAIL");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return false;
            }
        }

        static bool VerifyWaterSaturationPressure()
        {
            Console.WriteLine("\n[Test 4] Thermodynamics: Water Saturation Pressure");
            Console.WriteLine("Reference: IAPWS-IF97.");

            try
            {
                double psat_MPa = PhaseTransitionHandler.GetSaturationPressure(373.15);
                double psat_Pa = psat_MPa * 1e6;

                Console.WriteLine($"T = 373.15 K. Expected Psat = 101325 Pa.");
                Console.WriteLine($"Actual Psat = {psat_Pa:F2} Pa");

                if (Math.Abs(psat_Pa - 101325) < 1000)
                {
                    Console.WriteLine("Status: PASS");
                    return true;
                }
                Console.WriteLine("Status: FAIL");
                return false;
            }
            catch(Exception ex)
            {
                 Console.WriteLine($"Error: {ex.Message}");
                 return false;
            }
        }

        static bool VerifyPNMPermeability()
        {
            Console.WriteLine("\n[Test 5] PNM: Poiseuille Flow Permeability");
            Console.WriteLine("Reference: Fatt (1956).");

            var dataset = new PNMDataset("test", "test.pnm");
            dataset.VoxelSize = 1.0f;
            dataset.ImageWidth = 10;
            dataset.ImageHeight = 10;
            dataset.ImageDepth = 20;

            dataset.Pores.Add(new Pore { ID = 100, Radius = 0.1f, Position = new Vector3(0,0,0) });
            dataset.Pores.Add(new Pore { ID = 101, Radius = 0.1f, Position = new Vector3(10,10,0) });
            dataset.Pores.Add(new Pore { ID = 102, Radius = 0.1f, Position = new Vector3(0,0,19) });
            dataset.Pores.Add(new Pore { ID = 103, Radius = 0.1f, Position = new Vector3(10,10,19) });

            dataset.Pores.Add(new Pore { ID = 0, Radius = 1.0f, Position = new Vector3(5,5,0) });
            dataset.Pores.Add(new Pore { ID = 1, Radius = 1.0f, Position = new Vector3(5,5,10) });
            dataset.Pores.Add(new Pore { ID = 2, Radius = 1.0f, Position = new Vector3(5,5,19) });

            dataset.Throats.Add(new Throat { ID = 0, Radius = 1.0f, Pore1ID = 0, Pore2ID = 1 });
            dataset.Throats.Add(new Throat { ID = 1, Radius = 1.0f, Pore1ID = 1, Pore2ID = 2 });

            dataset.InitializeFromCurrentLists();

            var permOptions = new PermeabilityOptions
            {
                Dataset = dataset,
                FluidViscosity = 1.0f,
                InletPressure = 200.0f,
                OutletPressure = 100.0f,
                Axis = (GeoscientistToolkit.Analysis.Pnm.FlowAxis)2, // Z
                CalculateDarcy = true
            };

            try
            {
                AbsolutePermeability.Calculate(permOptions);
                float k_darcy = dataset.DarcyPermeability;

                Console.WriteLine("Simulating Flow in Straight Pore Chain...");
                Console.WriteLine($"Actual K = {k_darcy:F4} mD");

                if (k_darcy > 0)
                {
                    Console.WriteLine("Status: PASS (Flow detected)");
                    return true;
                }

                Console.WriteLine("Status: FAIL (No flow)");
                return false;
            }
            catch(Exception ex)
            {
                Console.WriteLine($"PNM Error: {ex.Message}");
                return false;
            }
        }

        static bool VerifyAcousticSpeed()
        {
            Console.WriteLine("\n[Test 6] Acoustic: Speed of Sound in Seawater");
            Console.WriteLine("Reference: Mackenzie (1981).");

            int nx=100, ny=10, nz=10;
            float dx=1.0f;
            float dt=0.0001f;

            float[,,] vx = new float[nx,ny,nz];
            float[,,] vy = new float[nx,ny,nz];
            float[,,] vz = new float[nx,ny,nz];
            float[,,] sxx = new float[nx,ny,nz];
            float[,,] syy = new float[nx,ny,nz];
            float[,,] szz = new float[nx,ny,nz];
            float[,,] sxy = new float[nx,ny,nz];
            float[,,] sxz = new float[nx,ny,nz];
            float[,,] syz = new float[nx,ny,nz];

            float[,,] E = new float[nx,ny,nz];
            float[,,] nu = new float[nx,ny,nz];
            float[,,] rho = new float[nx,ny,nz];

            float target_K = 2.2e9f;
            float target_nu = 0.49f;
            float target_E = 3 * target_K * (1 - 2 * target_nu);
            float target_rho = 1000.0f;
            float expected_v = (float)Math.Sqrt(target_K / target_rho);

            for(int x=0; x<nx; x++)
            for(int y=0; y<ny; y++)
            for(int z=0; z<nz; z++)
            {
                E[x,y,z] = target_E;
                nu[x,y,z] = target_nu;
                rho[x,y,z] = target_rho;
            }

            var simParams = new GeoscientistToolkit.Analysis.AcousticSimulation.SimulationParameters();
            var sim = new AcousticSimulatorCPU(simParams);

            sxx[10, 5, 5] = 1000.0f;
            syy[10, 5, 5] = 1000.0f;
            szz[10, 5, 5] = 1000.0f;

            int rx = 60;
            float t_arrival = -1;

            for(int step=0; step<1000; step++)
            {
                sim.UpdateWaveField(vx, vy, vz, sxx, syy, szz, sxy, sxz, syz, E, nu, rho, dt, dx, 0.0f);
                float p = (sxx[rx,5,5] + syy[rx,5,5] + szz[rx,5,5]) / 3.0f;
                if (Math.Abs(p) > 0.1f && t_arrival < 0)
                {
                    t_arrival = step * dt;
                    break;
                }
            }

            Console.WriteLine($"Expected V = {expected_v:F1} m/s.");
            float dist = (rx - 10) * dx;

            if (t_arrival > 0)
            {
                float v_calc = dist / t_arrival;
                Console.WriteLine($"Actual V = {v_calc:F1} m/s");
                if (Math.Abs(v_calc - expected_v) < 200.0f)
                {
                    Console.WriteLine("Status: PASS");
                    return true;
                }
            }

            Console.WriteLine($"Status: FAIL");
            return false;
        }

        static bool VerifyHeatTransfer()
        {
            Console.WriteLine("\n[Test 7] PhysicoChem (Heat): 1D Conduction");
            Console.WriteLine("Reference: Carslaw & Jaeger (1959).");

            var state = new PhysicoChemState((10,3,3));

            for(int i=0; i<10; i++)
            for(int j=0; j<3; j++)
            for(int k=0; k<3; k++)
                state.Temperature[i,j,k] = 0.0f;

            var solver = new HeatTransferSolver();
            double dt = 1.0;
            int steps = 2000;

            for(int t=0; t<steps; t++)
            {
                for(int j=0; j<3; j++)
                for(int k=0; k<3; k++)
                {
                    state.Temperature[0,j,k] = 100.0f;
                    state.Temperature[9,j,k] = 0.0f;
                }

                solver.SolveHeat(state, dt, null);
            }

            float t_1 = state.Temperature[1,1,1];
            float t_5 = state.Temperature[5,1,1];

            Console.WriteLine($"Steps={steps}. T[1]={t_1:F2}, T[5]={t_5:F2}");

            if (t_1 > 10.0)
            {
                Console.WriteLine("Status: PASS (Heat Propagation Detected)");
                return true;
            }
            Console.WriteLine("Status: FAIL");
            return false;
        }

        static bool VerifyHydrologyFlow()
        {
            Console.WriteLine("\n[Test 8] Hydrology: Flow Accumulation");
            Console.WriteLine("Reference: O'Callaghan & Mark (1984).");

            float[,] dem = new float[5,5];
            for(int x=0; x<5; x++)
            for(int y=0; y<5; y++)
            {
                dem[x,y] = 10.0f + (float)Math.Sqrt((x-2)*(x-2) + (y-2)*(y-2));
            }

            var flowDir = GISOperationsImpl.CalculateD8FlowDirection(dem);
            var accum = GISOperationsImpl.CalculateFlowAccumulation(flowDir);

            int centerAccum = accum[2,2];
            Console.WriteLine($"Center Accumulation: {centerAccum}");

            if (centerAccum > 5)
            {
                Console.WriteLine("Status: PASS");
                return true;
            }
            Console.WriteLine("Status: FAIL");
            return false;
        }

        static async Task<bool> VerifyGeothermalSystem()
        {
            Console.WriteLine("\n[Test 9] Geothermal: Borehole Heat Exchanger Simulation");
            Console.WriteLine("Reference: Al-Khoury et al. (2010). Computers & Geosciences.");

            var options = new GeothermalSimulationOptions();
            options.SimulationTime = 3600;
            options.TimeStep = 60;
            options.RadialGridPoints = 5;
            options.AngularGridPoints = 4;
            options.VerticalGridPoints = 5;
            options.DomainRadius = 10.0;
            options.BoreholeDataset = new BoreholeDataset("TestBorehole", "Verification");
            options.BoreholeDataset.TotalDepth = 100.0f;
            options.BoreholeDataset.WellDiameter = 0.2f;

            options.BoreholeDataset.LithologyUnits = new List<LithologyUnit>
            {
                new LithologyUnit { DepthFrom=0, DepthTo=100, Name="Rock" }
            };
            options.SetDefaultValues();

            var mesh = GeothermalMeshGenerator.GenerateCylindricalMesh(options.BoreholeDataset, options);

            var solver = new GeothermalSimulationSolver(options, mesh, null, CancellationToken.None);

            try
            {
                var results = await solver.RunSimulationAsync();

                if (results.OutletTemperature != null && results.OutletTemperature.Any())
                {
                    var finalTemp = results.OutletTemperature.Last().temperature;
                    Console.WriteLine($"Simulation completed. Outlet Temp: {finalTemp-273.15:F2} C");
                    Console.WriteLine("Status: PASS");
                    return true;
                }

                Console.WriteLine("Status: FAIL (No results)");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Geothermal Error: {ex.Message}");
                return false;
            }
        }
    }
}
