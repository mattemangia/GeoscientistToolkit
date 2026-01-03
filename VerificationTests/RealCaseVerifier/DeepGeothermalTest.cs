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
            Console.WriteLine("Scenario: 16x16x16 Cube, Coaxial HEX, Variable Conductivity, Gas Bubble Rise");

            try
            {
                // 1. Setup Dataset
                var dataset = new PhysicoChemDataset("DeepGeoTest", "Verification of Multiphase Flow");

                // 16x16x16 Mesh
                int res = 16;
                dataset.Mesh = new PhysicoChemMesh(); // Will be generated

                // Domains with different conductivity (implicitly handled by material properties in solver for now)

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
                dataset.SimulationParams.TotalTime = 200.0; // Increased simulation time
                dataset.SimulationParams.TimeStep = 0.5; // Larger step
                dataset.SimulationParams.Mode = SimulationMode.TimeBased;

                // Generate Mesh manually to assign domains to rows (Y-axis rows)
                var gridMesh = new GridMesh3D(res, res, res);
                gridMesh.Spacing = (1.0, 1.0, 1.0);
                var materialIds = new int[res, res, res];

                for(int i=0; i<res; i++)
                for(int j=0; j<res; j++)
                for(int k=0; k<res; k++)
                {
                    // Row based on J (Y-axis) - 4 layers
                    int layer = (j * 4) / res;
                    materialIds[i,j,k] = Math.Clamp(layer, 0, 3);
                }
                gridMesh.Metadata["MaterialIds"] = materialIds;
                dataset.GeneratedMesh = gridMesh;

                // Initialize
                dataset.InitializeState();
                var state = dataset.CurrentState;

                // Set initial conditions
                // High pressure (deep), Hot Rock Env
                for(int i=0; i<res; i++)
                for(int j=0; j<res; j++)
                for(int k=0; k<res; k++)
                {
                    state.Pressure[i,j,k] = 300e5f; // 300 bar
                    state.Temperature[i,j,k] = 400.0f; // 400 K (Hot Rock)
                    state.LiquidSaturation[i,j,k] = 1.0f;
                    state.GasSaturation[i,j,k] = 0.0f;
                    state.Porosity[i,j,k] = 0.2f;
                    state.Permeability[i,j,k] = 1e-9f; // Increased Permeability to verify movement
                }

                // Coaxial Heat Exchanger Setup
                // Inner (8,8) = Hot (e.g., return flow)
                // Outer Ring (7-9, 7-9 excluding center) = Cold (Injection)
                // Let's fix T at these locations.

                int cx = res/2;
                int cy = res/2;

                // Save Initial State
                PhysicoChemState initialState = state.Clone();

                // 2. Run Simulation
                var solver = new PhysicoChemSolver(dataset);

                dataset.SimulationParams.Mode = SimulationMode.StepBased;
                dataset.SimulationParams.MaxSteps = 1;

                Console.WriteLine("Starting Simulation...");

                int steps = 100; // More steps
                for (int t = 0; t < steps; t++)
                {
                    // Inject Gas at bottom center (Pulse)
                    if (t < 20)
                    {
                         state.GasSaturation[cx, cy, 1] = 0.8f; // Inject bubble at Z=1
                         state.LiquidSaturation[cx, cy, 1] = 0.2f;
                    }

                    // Coaxial Heat Exchanger:
                    // Fix temperatures along Z (vertical probe)
                    for(int z=2; z<res-2; z++)
                    {
                        // Inner (Hot)
                        state.Temperature[cx, cy, z] = 350.0f; // Center: Warmed fluid return?
                        // Wait, if we cool the rock, fluid heats up.
                        // Injection is COLD.
                        // Let's say Injection (Annulus) = 300K.
                        // Production (Inner) = 350K.

                        // Annulus (Ring)
                        for(int dx=-1; dx<=1; dx++)
                        for(int dy=-1; dy<=1; dy++)
                        {
                            if (dx==0 && dy==0) continue; // Skip center
                            state.Temperature[cx+dx, cy+dy, z] = 300.0f; // Cold Injection
                        }

                        // Center
                        state.Temperature[cx, cy, z] = 350.0f; // Inner Pipe
                    }

                    solver.RunSimulation();
                }

                // 3. Visualization
                // Save composite images (Initial vs Final)

                SaveCompositeCrossSection(initialState, state, "pressure_bubble.png", res/2, "Gas & Pressure", "Initial", "Final");
                SaveCompositeCrossSection(initialState, state, "exchanger_heat.png", res/2, "Temperature", "Initial", "Final");

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

        private static void SaveCompositeCrossSection(PhysicoChemState state1, PhysicoChemState state2, string filename, int sliceIndex, string title, string label1, string label2)
        {
            int nx = state1.Temperature.GetLength(0);
            int nz = state1.Temperature.GetLength(2);

            // Output resolution
            int panelW = 300;
            int h = 400;
            int margin = 40;
            int totalW = panelW * 2 + margin * 3;
            int totalH = h + margin * 2;

            byte[] pixels = new byte[totalW * totalH * 4];

            // Fill background white
            for(int i=0; i<pixels.Length; i++) pixels[i] = 255;

            // Draw Panel 1
            DrawPanel(pixels, totalW, totalH, state1, sliceIndex, filename, margin, margin, panelW, h);

            // Draw Panel 2
            DrawPanel(pixels, totalW, totalH, state2, sliceIndex, filename, margin*2 + panelW, margin, panelW, h);

            // Labels
            SimpleBitmapFont.DrawString(pixels, totalW, totalH, title, totalW/2 - title.Length*3, 10, 0, 0, 0);
            SimpleBitmapFont.DrawString(pixels, totalW, totalH, label1, margin + panelW/2 - 20, margin - 10, 0, 0, 0);
            SimpleBitmapFont.DrawString(pixels, totalW, totalH, label2, margin*2 + panelW + panelW/2 - 20, margin - 10, 0, 0, 0);

            // Legend
            if (filename.Contains("pressure"))
            {
               SimpleBitmapFont.DrawString(pixels, totalW, totalH, "Green=Gas, Red=Pressure", margin, totalH - 15, 0, 100, 0);
            }
            else
            {
               SimpleBitmapFont.DrawString(pixels, totalW, totalH, "Blue=300K, Red=400K+", margin, totalH - 15, 0, 0, 150);
            }

            using var stream = File.OpenWrite(filename);
            var writer = new ImageWriter();
            writer.WritePng(pixels, totalW, totalH, ColorComponents.RedGreenBlueAlpha, stream);
        }

        private static void DrawPanel(byte[] pixels, int totalW, int totalH, PhysicoChemState state, int sliceIndex, string type, int xOff, int yOff, int w, int h)
        {
            int nx = state.Temperature.GetLength(0);
            int nz = state.Temperature.GetLength(2);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float gx = (float)x / w * nx;
                float gz = (1.0f - (float)y / h) * nz;

                int ix = Math.Clamp((int)gx, 0, nx - 1);
                int iz = Math.Clamp((int)gz, 0, nz - 1);
                int iy = sliceIndex;

                byte r=0, g=0, b=0;

                if (type.Contains("pressure"))
                {
                    float p = state.Pressure[ix, iy, iz];
                    float s_g = state.GasSaturation[ix, iy, iz];

                    float pNorm = (p - 1e5f) / 5e5f;
                    r = (byte)(50 + Math.Clamp(pNorm, 0, 1) * 150);

                    if (s_g > 0.01f)
                    {
                        g = (byte)(Math.Clamp(s_g * 255 * 2, 0, 255));
                        r = (byte)(r * 0.5f);
                    }
                    else
                    {
                        g = 50;
                        b = 50;
                    }
                }
                else
                {
                    float temp = state.Temperature[ix, iy, iz];
                    // Range 300K (Cold) to 400K (Hot)
                    float tNorm = Math.Clamp((temp - 300.0f) / 100.0f, 0.0f, 1.0f);

                    // Jet-like color map (Blue -> Green -> Red)
                    if (tNorm < 0.5f)
                    {
                        // Blue to Green
                        float local = tNorm * 2.0f;
                        b = (byte)((1.0f - local) * 255);
                        g = (byte)(local * 255);
                        r = 0;
                    }
                    else
                    {
                        // Green to Red
                        float local = (tNorm - 0.5f) * 2.0f;
                        b = 0;
                        g = (byte)((1.0f - local) * 255);
                        r = (byte)(local * 255);
                    }
                }

                int px = x + xOff;
                int py = y + yOff;
                int idx = (py * totalW + px) * 4;
                pixels[idx] = r;
                pixels[idx+1] = g;
                pixels[idx+2] = b;
                pixels[idx+3] = 255;
            }

            // Border
            DrawRect(pixels, totalW, totalH, xOff, yOff, w, h, 0, 0, 0);
        }

        private static void DrawRect(byte[] pixels, int w, int h, int x, int y, int rw, int rh, byte r, byte g, byte b)
        {
            // Simple outline
            for(int i=0; i<rw; i++) { SetPixel(pixels, w, h, x+i, y, r,g,b); SetPixel(pixels, w, h, x+i, y+rh-1, r,g,b); }
            for(int i=0; i<rh; i++) { SetPixel(pixels, w, h, x, y+i, r,g,b); SetPixel(pixels, w, h, x+rw-1, y+i, r,g,b); }
        }

        private static void SetPixel(byte[] pixels, int w, int h, int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            int idx = (y * w + x) * 4;
            pixels[idx] = r;
            pixels[idx+1] = g;
            pixels[idx+2] = b;
            pixels[idx+3] = 255;
        }
    }
}
