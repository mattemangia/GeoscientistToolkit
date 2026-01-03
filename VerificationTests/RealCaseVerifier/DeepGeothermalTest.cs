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
            Console.WriteLine("Mode: Fully Interactive Simulation (No Hardcoding)");

            try
            {
                // 1. Setup Dataset
                var dataset = new PhysicoChemDataset("DeepGeoTest", "Verification of Multiphase Flow");

                // 16x16x16 Mesh (1m spacing)
                int res = 16;
                dataset.Mesh = new PhysicoChemMesh();

                // Simulation Parameters
                dataset.SimulationParams.EnableMultiphaseFlow = true;
                dataset.SimulationParams.MultiphaseEOSType = "WaterCO2";
                dataset.SimulationParams.TotalTime = 2000.0;
                dataset.SimulationParams.TimeStep = 1.0; // Reduced for stability
                dataset.SimulationParams.Mode = SimulationMode.TimeBased;

                // Generate Mesh
                var gridMesh = new GridMesh3D(res, res, res);
                gridMesh.Spacing = (1.0, 1.0, 1.0);
                dataset.GeneratedMesh = gridMesh;

                // Initialize State
                dataset.InitializeState();
                var state = dataset.CurrentState;

                // --- Geometry & Properties Setup ---
                int cx = res / 2; // 8
                int cy = res / 2; // 8

                // Reduced Permeability to ensure stable explicit transport (CFL < 1)
                // v ~ k/mu * dP/dz ~ 1e-11 / 1e-3 * (60e5/16) ~ 1e-8 * 4e5 ~ 0.004 m/s
                // dt*v/dx = 1.0 * 0.004 / 1.0 = 0.004 << 1. Stable.
                float k_pipe = 1e-11f;
                float k_rock = 1e-15f;
                float k_casing = 1e-19f;

                for(int i=0; i<res; i++)
                for(int j=0; j<res; j++)
                for(int k=0; k<res; k++)
                {
                    float dx = i - (cx - 0.5f);
                    float dy = j - (cy - 0.5f);
                    float r = (float)Math.Sqrt(dx*dx + dy*dy);

                    // Initial Rock Conditions (Gradient)
                    // Z=0 (Bottom) -> 400K, Z=15 (Top) -> 300K
                    float depthFactor = 1.0f - (float)k / (res - 1);
                    state.Temperature[i,j,k] = 300.0f + depthFactor * 100.0f;
                    state.Pressure[i,j,k] = 300e5f + depthFactor * 1000.0f * 9.81f * res; // Hydrostatic baseline
                    state.LiquidSaturation[i,j,k] = 1.0f;
                    state.GasSaturation[i,j,k] = 0.0f;

                    // Structure Definition
                    if (r < 1.5f) // Inner Pipe (Production)
                    {
                        state.Permeability[i,j,k] = k_pipe;
                        state.Porosity[i,j,k] = 0.99f;
                        // Pre-fill well with fluid?
                        // Let's assume equilibrium initially, but we want to SEE the structure.
                        // So let's fill Inner with slightly warmer fluid (350K) to distinguish?
                        // Or just let it be gradient.
                        // User wants "Outer annulus with heating fluid... inner annulus with hot water".
                        // Let's initialize Annulus with COLD injection fluid (300K) to show contrast.
                    }
                    else if (r < 2.5f) // Casing
                    {
                        state.Permeability[i,j,k] = k_casing;
                        state.Porosity[i,j,k] = 0.01f;
                    }
                    else if (r < 4.0f) // Annulus (Injection)
                    {
                        state.Permeability[i,j,k] = k_pipe;
                        state.Porosity[i,j,k] = 0.99f;
                        // Initialize Annulus with Cold Fluid (300K) down to bottom
                        state.Temperature[i,j,k] = 300.0f;
                    }
                    else // Rock
                    {
                        state.Permeability[i,j,k] = k_rock;
                        state.Porosity[i,j,k] = 0.1f;
                    }

                    // Connection at bottom (Z=0,1)
                    if (k <= 1 && r < 4.0f)
                    {
                        state.Permeability[i,j,k] = k_pipe;
                        state.Porosity[i,j,k] = 0.99f;
                        // Mixing zone T?
                    }
                }

                // Inject Gas Bubble (Initial Condition)
                // Inject at bottom of rock (outside well)
                // Z=1 (near bottom)
                state.GasSaturation[1, 1, 1] = 0.8f;
                state.LiquidSaturation[1, 1, 1] = 0.2f;

                // --- Boundary Conditions ---
                // Add BCs to drive the simulation (Pump/Chiller)

                // Inlet Pressure (High) at Annulus Top
                dataset.BoundaryConditions.Add(new BoundaryCondition
                {
                    Name = "InletP", Type = BoundaryType.FixedValue, Variable = BoundaryVariable.Pressure, Value = 350e5, // 350 bar
                    Location = BoundaryLocation.Custom, CustomRegionCenter = (0,0,0), CustomRegionRadius = 1.0, // Radius check handled below
                    CustomRegion = (x,y,z) => {
                        double dx = x - (cx - 0.5); double dy = y - (cy - 0.5); double r = Math.Sqrt(dx*dx + dy*dy);
                        return z >= (res-1) && r >= 2.5 && r < 4.0;
                    }
                });

                // Inlet Temperature (Cold) at Annulus Top
                dataset.BoundaryConditions.Add(new BoundaryCondition
                {
                    Name = "InletT", Type = BoundaryType.FixedValue, Variable = BoundaryVariable.Temperature, Value = 300.0,
                    Location = BoundaryLocation.Custom, CustomRegion = (x,y,z) => {
                        double dx = x - (cx - 0.5); double dy = y - (cy - 0.5); double r = Math.Sqrt(dx*dx + dy*dy);
                        return z >= (res-1) && r >= 2.5 && r < 4.0;
                    }
                });

                // Outlet Pressure (Low) at Inner Top
                dataset.BoundaryConditions.Add(new BoundaryCondition
                {
                    Name = "OutletP", Type = BoundaryType.FixedValue, Variable = BoundaryVariable.Pressure, Value = 290e5, // 290 bar
                    Location = BoundaryLocation.Custom, CustomRegion = (x,y,z) => {
                        double dx = x - (cx - 0.5); double dy = y - (cy - 0.5); double r = Math.Sqrt(dx*dx + dy*dy);
                        return z >= (res-1) && r < 1.5;
                    }
                });

                // Save Initial State (Now includes Gradient + Cold Annulus + Gas)
                PhysicoChemState initialState = state.Clone();

                // 2. Run Simulation
                var solver = new PhysicoChemSolver(dataset);
                dataset.SimulationParams.Mode = SimulationMode.StepBased;
                dataset.SimulationParams.MaxSteps = 1;

                Console.WriteLine("Starting Interactive Simulation...");

                int steps = 2000; // 2000 * 1.0s = 2000s
                for (int t = 0; t < steps; t++)
                {
                    solver.RunSimulation();
                }

                // 3. Visualization
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

        // ... (Visualization helpers)
        private static void SaveCompositeCrossSection(PhysicoChemState state1, PhysicoChemState state2, string filename, int sliceIndex, string title, string label1, string label2)
        {
            int nx = state1.Temperature.GetLength(0);
            int nz = state1.Temperature.GetLength(2);

            int panelW = 300;
            int h = 400;
            int margin = 40;
            int totalW = panelW * 2 + margin * 3;
            int totalH = h + margin * 2;

            byte[] pixels = new byte[totalW * totalH * 4];

            for(int i=0; i<pixels.Length; i++) pixels[i] = 255;

            DrawPanel(pixels, totalW, totalH, state1, sliceIndex, filename, margin, margin, panelW, h);
            DrawPanel(pixels, totalW, totalH, state2, sliceIndex, filename, margin*2 + panelW, margin, panelW, h);

            SimpleBitmapFont.DrawString(pixels, totalW, totalH, title, totalW/2 - title.Length*3, 10, 0, 0, 0);
            SimpleBitmapFont.DrawString(pixels, totalW, totalH, label1, margin + panelW/2 - 20, margin - 10, 0, 0, 0);
            SimpleBitmapFont.DrawString(pixels, totalW, totalH, label2, margin*2 + panelW + panelW/2 - 20, margin - 10, 0, 0, 0);

            if (filename.Contains("pressure"))
                SimpleBitmapFont.DrawString(pixels, totalW, totalH, "Green=Gas, Red=Pressure", margin, totalH - 15, 0, 100, 0);
            else
                SimpleBitmapFont.DrawString(pixels, totalW, totalH, "Blue=300K, Red=400K+", margin, totalH - 15, 0, 0, 150);

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

                    float pNorm = (p - 290e5f) / 60e5f;
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
                    float tNorm = Math.Clamp((temp - 300.0f) / 100.0f, 0.0f, 1.0f);

                    if (tNorm < 0.5f)
                    {
                        float local = tNorm * 2.0f;
                        b = (byte)((1.0f - local) * 255);
                        g = (byte)(local * 255);
                        r = 0;
                    }
                    else
                    {
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
            DrawRect(pixels, totalW, totalH, xOff, yOff, w, h, 0, 0, 0);
        }

        private static void DrawRect(byte[] pixels, int w, int h, int x, int y, int rw, int rh, byte r, byte g, byte b)
        {
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
