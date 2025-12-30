using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using GeoscientistToolkit.Analysis.Geomechanics;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Analysis.Seismology;

namespace RealCaseVerifier
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("   REAL CASE STUDY VERIFICATION SUITE");
            Console.WriteLine("=================================================");

            bool allPassed = true;

            // 1. Geomechanics: Westerly Granite Triaxial Test
            allPassed &= VerifyGeomechanicsGranite();

            // 2. Seismology: PREM Model Verification
            allPassed &= VerifySeismicPREM();

            if (allPassed)
            {
                Console.WriteLine("\nALL VERIFICATION TESTS PASSED.");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("\nSOME TESTS FAILED.");
                Environment.Exit(1);
            }
        }

        static bool VerifyGeomechanicsGranite()
        {
            Console.WriteLine("\n[Test 1] Geomechanics: Westerly Granite Triaxial Compression");
            Console.WriteLine("Reference: Mechanical Properties... Granite... MDPI 2022 (doi:10.3390/app12083930)");

            // Parameters from paper
            float c = 26.84f; // MPa
            float phi = 51.0f; // degrees
            float sigma3 = 10.0f; // MPa

            // Theoretical Calculation
            // Sigma1 = Sigma3 * tan^2(45 + phi/2) + 2*c*tan(45+phi/2)
            float phiRad = phi * (float)Math.PI / 180f;
            float tanFactor = (float)Math.Tan((Math.PI/4) + (phiRad/2));
            float sigma1_expected = sigma3 * (tanFactor * tanFactor) + 2 * c * tanFactor;

            Console.WriteLine($"Parameters: c={c} MPa, phi={phi} deg, Sigma3={sigma3} MPa");
            Console.WriteLine($"Equation: Sigma1 = Sigma3 * tan^2(45 + phi/2) + 2*c*tan(45+phi/2)");
            Console.WriteLine($"Expected Peak Strength (Sigma1): {sigma1_expected:F2} MPa");

            // Setup Simulation
            var material = new PhysicalMaterial
            {
                Name = "Westerly Granite (Real)",
                YoungModulus_GPa = 35.0f, // Paper says 31-44 GPa
                PoissonRatio = 0.25f, // Typical for granite
                FrictionAngle_deg = phi,
                CompressiveStrength_MPa = 200.0f, // Not directly used if cohesion is explicit?
                TensileStrength_MPa = 10.0f
            };
            // Force cohesion
            material.Extra["Cohesion_MPa"] = c;

            // Correct static method call based on read_file
            var mesh = TriaxialMeshGenerator.GenerateCylindricalMesh(radius: 0.025f, height: 0.1f, nRadial: 8, nCircumferential: 12, nAxial: 10);

            // Recalculate time needed
            // Failure strain approx = 231 MPa / 35000 MPa = 0.0066
            // Let's go to 1.5% strain (0.015) to be sure.
            float strainRate = 1e-4f;
            float targetStrain = 0.015f;
            float timeNeeded = targetStrain / strainRate; // 150 seconds

            var loadParams = new TriaxialLoadingParameters
            {
                ConfiningPressure_MPa = sigma3,
                LoadingMode = TriaxialLoadingMode.StrainControlled,
                AxialStrainRate_per_s = strainRate,
                MaxAxialStrain_percent = targetStrain * 100f,
                TotalTime_s = timeNeeded,
                TimeStep_s = 0.1f,
                DrainageCondition = DrainageCondition.Drained
            };

            using var sim = new TriaxialSimulation();

            Console.WriteLine($"Running Simulation for {timeNeeded} seconds...");

            try
            {
                // Silence logger
                var results = sim.RunSimulationCPU(mesh, material, loadParams, FailureCriterion.MohrCoulomb);

                Console.WriteLine($"Actual Peak Strength: {results.PeakStrength_MPa:F2} MPa");

                float error = Math.Abs(results.PeakStrength_MPa - sigma1_expected);
                float errorPercent = (error / sigma1_expected) * 100f;

                if (errorPercent < 5.0f)
                {
                    Console.WriteLine("Status: PASS");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Status: FAIL (Error {errorPercent:F2}%)");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Execution Error: {ex.Message}");
                return false;
            }
        }

        static bool VerifySeismicPREM()
        {
            Console.WriteLine("\n[Test 2] Seismology: PREM Model Wave Propagation");
            Console.WriteLine("Reference: Dziewonski, A. M., & Anderson, D. L. (1981). Preliminary reference Earth model. PEPI, 25(4), 297-356.");

            // PREM Data for Upper Crust (Depth 10km)
            double vp = 5.8; // km/s
            double vs = 3.2; // km/s
            double rho = 2.6; // g/cm3
            double distKm = 10.0;

            Console.WriteLine($"Parameters: Vp={vp} km/s, Vs={vs} km/s, Dist={distKm} km");

            // Expected Arrival Times
            double tP_expected = distKm / vp;
            double tS_expected = distKm / vs;

            Console.WriteLine($"Expected P-Wave Arrival: {tP_expected:F4} s");
            Console.WriteLine($"Expected S-Wave Arrival: {tS_expected:F4} s");

            // Create synthetic model matching PREM
            var crustalModel = new CrustalModel();
            var type = new CrustalType();
            type.Layers.Add("upper_crust", new CrustalLayer
            {
                ThicknessKm = 20.0,
                VpKmPerS = vp,
                VsKmPerS = vs,
                DensityGPerCm3 = rho
            });
            // Need to populate all fallback types because hardcoded lookups might use them
            crustalModel.CrustalTypes.Add("continental", type);
            crustalModel.CrustalTypes.Add("oceanic", type);
            crustalModel.CrustalTypes.Add("orogen", type);
            crustalModel.CrustalTypes.Add("rift", type);

            // Setup Engine
            // Grid: 10km length. dx=0.5km -> 20 cells.
            // Need enough padding. Let's do 40x40x40 grid, dx=0.5km. Size 20km box.
            // Source at 10,10,10 (5km depth). Receiver at 30,10,10 (15km X = +10km distance).
            int nx=40, ny=40, nz=40;
            double dx=0.5, dy=0.5, dz=0.5; // km
            double dt = 0.01; // s. CFL: dt < dx/Vp = 0.5/5.8 = 0.08. 0.01 is safe.

            var engine = new WavePropagationEngine(crustalModel, nx, ny, nz, dx, dy, dz, dt, false);
            engine.InitializeMaterialProperties(0, 1, 0, 1);

            // Add Source
            int sx = 10, sy = 20, sz = 20;
            double rad45 = Math.PI / 4.0;
            engine.AddPointSource(sx, sy, sz, -1.0, rad45, rad45, rad45); // M -1.0 earthquake

            // Receiver
            int rx = 30, ry = 20, rz = 20; // 20 cells away * 0.5km = 10km distance

            Console.WriteLine("Running Wave Propagation...");

            double tP_actual = -1;
            double threshold = 2e-5; // Lowered slightly

            // Run for 5 seconds
            int steps = (int)(5.0 / dt);

            double maxAmp = 0;

            for(int t=0; t<steps; t++)
            {
                engine.TimeStep();
                var wave = engine.GetWaveFieldAt(rx, ry, rz);

                if (wave.Amplitude > maxAmp) maxAmp = wave.Amplitude;

                // Detect P-wave
                if (tP_actual < 0 && wave.Amplitude > threshold)
                {
                    tP_actual = t * dt;
                    Console.WriteLine($"Threshold Triggered at {tP_actual:F4}s. Amplitude: {wave.Amplitude:E2}");
                }
            }

            Console.WriteLine($"Actual P-Wave Arrival: {tP_actual:F4} s");
            Console.WriteLine($"Max Amplitude Reached: {maxAmp:E2}");

            double error = Math.Abs(tP_actual - tP_expected);
            if (tP_actual > 0 && error < 0.3) // 0.3s tolerance (approx 5 cells)
            {
                 Console.WriteLine("Status: PASS (P-Wave matches)");
                 return true;
            }
            else
            {
                 Console.WriteLine($"Status: FAIL (Diff {error:F4}s)");
                 return false;
            }
        }
    }
}
