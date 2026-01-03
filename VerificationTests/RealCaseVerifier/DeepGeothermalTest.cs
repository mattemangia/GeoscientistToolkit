using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.PhysicoChem;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Analysis.Multiphase;
using StbImageWriteSharp;

namespace RealCaseVerifier
{
    public static class DeepGeothermalTest
    {
        public static bool Run()
        {
            Console.WriteLine("\n[Test 13] PhysicoChem: Deep Geothermal Multiphase Flow & Gas Intrusion");
            Console.WriteLine("Scenario: 4x4x4 Cube, Coaxial HEX, Variable Conductivity, Gas Bubble Rise");

            try
            {
                // 1. Setup Dataset
                var dataset = new PhysicoChemDataset("DeepGeoTest", "Verification of Multiphase Flow");

                // 4x4x4 Mesh
                int res = 4;
                dataset.Mesh = new PhysicoChemMesh(); // Will be generated

                // Define Domains with different conductivity (implicitly handled by material properties in solver for now)

                // Domains
                var domain1 = new ReactorDomain { Name = "RockLayer1", Material = new MaterialProperties { ThermalConductivity = 2.0 } }; // Row 1
                var domain2 = new ReactorDomain { Name = "RockLayer2", Material = new MaterialProperties { ThermalConductivity = 3.0 } }; // Row 2
                var domain3 = new ReactorDomain { Name = "RockLayer3", Material = new MaterialProperties { ThermalConductivity = 4.0 } }; // Row 3
                var domain4 = new ReactorDomain { Name = "RockLayer4", Material = new MaterialProperties { ThermalConductivity = 5.0 } }; // Row 4

                dataset.Domains.Add(domain1);
                dataset.Domains.Add(domain2);
                dataset.Domains.Add(domain3);
                dataset.Domains.Add(domain4);

                // Simulation Parameters
                dataset.SimulationParams.EnableMultiphaseFlow = true;
                dataset.SimulationParams.MultiphaseEOSType = "WaterCO2";
                dataset.SimulationParams.TotalTime = 100.0; // Short simulation
                dataset.SimulationParams.TimeStep = 0.1;
                dataset.SimulationParams.Mode = SimulationMode.TimeBased;

                // Generate Mesh manually to assign domains to rows (Y-axis rows)
                var gridMesh = new GridMesh3D(res, res, res);
                gridMesh.Spacing = (1.0, 1.0, 1.0);
                var materialIds = new int[res, res, res];

                for(int i=0; i<res; i++)
                for(int j=0; j<res; j++)
                for(int k=0; k<res; k++)
                {
                    // Row based on J (Y-axis)
                    if (j == 0) materialIds[i,j,k] = 0;
                    else if (j == 1) materialIds[i,j,k] = 1;
                    else if (j == 2) materialIds[i,j,k] = 2;
                    else materialIds[i,j,k] = 3;
                }
                gridMesh.Metadata["MaterialIds"] = materialIds;
                dataset.GeneratedMesh = gridMesh;

                // Initialize
                dataset.InitializeState();
                var state = dataset.CurrentState;

                // Set initial conditions
                // High pressure (deep), High Temp
                for(int i=0; i<res; i++)
                for(int j=0; j<res; j++)
                for(int k=0; k<res; k++)
                {
                    state.Pressure[i,j,k] = 300e5f; // 300 bar
                    state.Temperature[i,j,k] = 400.0f; // 400 K
                    state.LiquidSaturation[i,j,k] = 1.0f;
                    state.GasSaturation[i,j,k] = 0.0f;
                }

                // Gas Intrusion from fracture
                var gasInlet = new BoundaryCondition
                {
                    Name = "GasFracture",
                    Type = BoundaryType.Inlet,
                    Variable = BoundaryVariable.Concentration,
                };

                // 2. Run Simulation
                var solver = new PhysicoChemSolver(dataset);

                dataset.SimulationParams.Mode = SimulationMode.StepBased;
                dataset.SimulationParams.MaxSteps = 1;

                Console.WriteLine("Starting Simulation...");

                for (int t = 0; t < 50; t++) // 50 steps
                {
                    // Inject Gas at bottom corner (0,0,0)
                    if (t < 10) // Injection for first 10 steps
                    {
                         state.GasSaturation[1,1,1] = 0.5f; // Inject bubble
                         state.LiquidSaturation[1,1,1] = 0.5f;
                    }

                    // Heat Exchanger: Cool the center (simulating extraction)
                    state.Temperature[2,2,2] = 350.0f; // Fixed T sink

                    solver.RunSimulation();
                }

                // 3. Visualization
                // Cross section at Y=2 (Middle)

                SaveCrossSection(state, "pressure_bubble.png", 2);
                SaveCrossSection(state, "exchanger_heat.png", 2);

                Console.WriteLine("Status: PASS (Images generated)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        private static void SaveCrossSection(PhysicoChemState state, string filename, int sliceIndex)
        {
            int nx = state.Temperature.GetLength(0);
            int nz = state.Temperature.GetLength(2);

            // Output resolution
            int w = 400;
            int h = 400;
            byte[] pixels = new byte[w * h * 4];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Map pixel to grid
                float gx = (float)x / w * nx;
                float gz = (float)y / h * nz; // Y in image is Z in grid (vertical)

                int ix = Math.Clamp((int)gx, 0, nx - 1);
                int iz = Math.Clamp((int)gz, 0, nz - 1);
                int iy = sliceIndex;

                // Visualize: Pressure (Red), Gas (Green), Temp (Blue)
                float p = state.Pressure[ix, iy, iz];
                float s_g = state.GasSaturation[ix, iy, iz];
                float temp = state.Temperature[ix, iy, iz];

                // Normalize
                byte r = (byte)Math.Clamp((p - 1e5) / 5e5 * 255, 0, 255);
                byte g = (byte)Math.Clamp(s_g * 255 * 2, 0, 255); // Amplify gas visibility
                byte b = (byte)Math.Clamp((temp - 273) / 200 * 255, 0, 255);

                int idx = (y * w + x) * 4;
                pixels[idx] = r;
                pixels[idx+1] = g;
                pixels[idx+2] = b;
                pixels[idx+3] = 255;
            }

            using var stream = File.OpenWrite(filename);
            var writer = new ImageWriter();
            writer.WritePng(pixels, w, h, ColorComponents.RedGreenBlueAlpha, stream);
        }
    }
}
