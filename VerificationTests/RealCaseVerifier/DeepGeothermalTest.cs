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

                // 2. Run Simulation
                var solver = new PhysicoChemSolver(dataset);

                dataset.SimulationParams.Mode = SimulationMode.StepBased;
                dataset.SimulationParams.MaxSteps = 1;

                Console.WriteLine("Starting Simulation...");

                for (int t = 0; t < 50; t++) // 50 steps
                {
                    // Inject Gas at bottom center (res/2, 0, res/2)
                    // Y is vertical in this setup?
                    // Let's assume Z is vertical as gravity is typically -Z.

                    if (t < 20) // Injection pulse
                    {
                         // Inject bubble at bottom center
                         int cx = res/2;
                         int cy = res/2;
                         state.GasSaturation[cx, cy, 1] = 0.8f; // Inject bubble at Z=1
                         state.LiquidSaturation[cx, cy, 1] = 0.2f;
                    }

                    // Heat Exchanger: Cool the center
                    // Coaxial probe in the middle (X=res/2, Y=res/2, Z=all)
                    for(int z=2; z<res-2; z++)
                    {
                        state.Temperature[res/2, res/2, z] = 350.0f; // Cooled probe
                    }

                    solver.RunSimulation();
                }

                // 3. Visualization
                // Cross section at X=res/2 (Middle plane showing Y-Z or similar)
                // Let's slice Y (Y is horizontal), showing X-Z (Vertical)

                SaveCrossSection(state, "pressure_bubble.png", res/2, "Gas Saturation & Pressure", "Gas Sat (Green)", "Pressure (Red)");
                SaveCrossSection(state, "exchanger_heat.png", res/2, "Heat Exchanger Temperature", "Temp (Blue=Cool)", "Red=Hot");

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

        private static void SaveCrossSection(PhysicoChemState state, string filename, int sliceIndex, string title, string label1, string label2)
        {
            int nx = state.Temperature.GetLength(0); // X
            int ny = state.Temperature.GetLength(1); // Y
            int nz = state.Temperature.GetLength(2); // Z (Vertical)

            // Output resolution
            int w = 600;
            int h = 600;
            int margin = 60;
            int plotW = w - 2 * margin;
            int plotH = h - 2 * margin;

            byte[] pixels = new byte[w * h * 4];

            // Fill background white
            for(int i=0; i<pixels.Length; i++) pixels[i] = 255;

            // Draw Plot Area (Background gray)
            for(int y=margin; y<h-margin; y++)
            for(int x=margin; x<w-margin; x++)
            {
                SetPixel(pixels, w, h, x, y, 240, 240, 240);
            }

            // Draw Data
            for (int y = 0; y < plotH; y++)
            for (int x = 0; x < plotW; x++)
            {
                // Map pixel to grid (X-Z plane at Y=sliceIndex)

                float gx = (float)x / plotW * nx;
                float gz = (1.0f - (float)y / plotH) * nz; // Flip Y for visualization

                int ix = Math.Clamp((int)gx, 0, nx - 1);
                int iz = Math.Clamp((int)gz, 0, nz - 1);
                int iy = sliceIndex;

                // Visualize
                float p = state.Pressure[ix, iy, iz];
                float s_g = state.GasSaturation[ix, iy, iz];
                float temp = state.Temperature[ix, iy, iz];

                byte r=0, g=0, b=0;

                if (filename.Contains("pressure"))
                {
                    // Pressure (Red background), Gas (Green overlay)
                    // Normalize Pressure
                    float pNorm = (p - 1e5f) / 5e5f;
                    pNorm = Math.Clamp(pNorm, 0.0f, 1.0f);

                    r = (byte)(50 + pNorm * 150);

                    // Gas overlay
                    float gNorm = Math.Clamp(s_g, 0.0f, 1.0f);
                    if (gNorm > 0.05f)
                    {
                        g = (byte)(gNorm * 255);
                        // Blend with pressure background
                        r = (byte)(r * 0.5f);
                        b = 50;
                    }
                    else
                    {
                        g = 50;
                        b = 50;
                    }
                }
                else
                {
                    // Temperature (Heat Map: Blue to Red)
                    // Range 350K to 400K
                    float tNorm = Math.Clamp((temp - 350.0f) / 50.0f, 0.0f, 1.0f);
                    r = (byte)(tNorm * 255);
                    b = (byte)((1.0f - tNorm) * 255);
                    g = 0;
                }

                // Pixel scaling (nearest neighbor for sharp grid look)
                int px = x + margin;
                int py = y + margin;

                int idx = (py * w + px) * 4;
                pixels[idx] = r;
                pixels[idx+1] = g;
                pixels[idx+2] = b;
                pixels[idx+3] = 255;
            }

            // Draw Axes (Black lines)
            DrawLine(pixels, w, h, margin, margin, margin, h - margin, 0, 0, 0); // Y axis
            DrawLine(pixels, w, h, margin, h - margin, w - margin, h - margin, 0, 0, 0); // X axis

            // Draw Text using SimpleBitmapFont
            SimpleBitmapFont.DrawString(pixels, w, h, title, w/2 - title.Length*3, margin/2, 0, 0, 0);
            SimpleBitmapFont.DrawString(pixels, w, h, "Depth (Z)", margin/4, h/2, 0, 0, 0); // Sideways text is hard, just label
            SimpleBitmapFont.DrawString(pixels, w, h, "Width (X)", w/2, h - margin/2, 0, 0, 0);

            // Legend
            SimpleBitmapFont.DrawString(pixels, w, h, label1, margin, h - margin + 15, 0, 100, 0);
            SimpleBitmapFont.DrawString(pixels, w, h, label2, margin, h - margin + 30, 150, 0, 0);

            // Save
            using var stream = File.OpenWrite(filename);
            var writer = new ImageWriter();
            writer.WritePng(pixels, w, h, ColorComponents.RedGreenBlueAlpha, stream);
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

        private static void DrawLine(byte[] pixels, int w, int h, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, e2;

            while (true)
            {
                SetPixel(pixels, w, h, x0, y0, r, g, b);
                if (x0 == x1 && y0 == y1) break;
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
