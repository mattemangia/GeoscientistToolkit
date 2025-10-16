// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulatorCPU_Plasticity.cs
// Partial class extension for plasticity algorithms

using System.Diagnostics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public partial class GeomechanicalSimulatorCPU
{
    // Plasticity state variables
    private float[,,] _plasticStrainEq; // Equivalent plastic strain
    private float[,,] _yieldStress; // Current yield stress (with hardening)

    /// <summary>
    ///     Apply plasticity correction to stresses using radial return mapping
    /// </summary>
    private void ApplyPlasticity(GeomechanicalResults results, byte[,,] labels)
    {
        if (!_params.EnablePlasticity) return;

        var sw = Stopwatch.StartNew();
        Logger.Log("[GeomechCPU] Applying plasticity correction (von Mises with isotropic hardening)...");

        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        // Initialize plasticity state if first time
        if (_plasticStrainEq == null)
        {
            _plasticStrainEq = new float[w, h, d];
            _yieldStress = new float[w, h, d];

            // Initialize yield stress from cohesion
            var initialYield = _params.Cohesion * 1e6f * 2f; // von Mises ~ 2*c for Mohr-Coulomb
            Parallel.For(0, d, z =>
            {
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (labels[x, y, z] != 0)
                        _yieldStress[x, y, z] = initialYield;
            });
        }

        var E = _params.YoungModulus * 1e6f; // Pa
        var nu = _params.PoissonRatio;
        var mu = E / (2f * (1f + nu)); // Shear modulus
        var K = E / (3f * (1f - 2f * nu)); // Bulk modulus

        // Hardening parameters
        var H_iso = _params.PlasticHardeningModulus * 1e6f; // Isotropic hardening modulus (Pa)

        var yieldedVoxels = 0;
        double totalPlasticStrain = 0;
        var lockObj = new object();

        // Process in slices for cache efficiency
        const int SLICE_SIZE = 16;
        var numSlices = (d + SLICE_SIZE - 1) / SLICE_SIZE;

        Parallel.For(0, numSlices, sliceIdx =>
        {
            var startZ = sliceIdx * SLICE_SIZE;
            var endZ = Math.Min(startZ + SLICE_SIZE, d);

            var localYielded = 0;
            double localPlasticStrain = 0;

            for (var z = startZ; z < endZ; z++)
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (labels[x, y, z] == 0) continue;

                // Get current stress state
                var sxx = results.StressXX[x, y, z];
                var syy = results.StressYY[x, y, z];
                var szz = results.StressZZ[x, y, z];
                var sxy = results.StressXY[x, y, z];
                var sxz = results.StressXZ[x, y, z];
                var syz = results.StressYZ[x, y, z];

                // Decompose into volumetric and deviatoric parts
                var p = (sxx + syy + szz) / 3f; // Mean stress (pressure)
                var sxx_dev = sxx - p;
                var syy_dev = syy - p;
                var szz_dev = szz - p;

                // Calculate von Mises equivalent stress
                var J2 = 0.5f * (sxx_dev * sxx_dev + syy_dev * syy_dev + szz_dev * szz_dev)
                         + sxy * sxy + sxz * sxz + syz * syz;
                var q = MathF.Sqrt(3f * J2); // von Mises stress

                // Get current yield stress (with hardening)
                var sigma_y = _yieldStress[x, y, z];

                // Check yield criterion: f = q - sigma_y <= 0
                var f = q - sigma_y;

                if (f > 0) // Yielding
                {
                    localYielded++;

                    // Radial return mapping (closest point projection)
                    // For von Mises with isotropic hardening:
                    // q_new = sigma_y + H_iso * delta_eps_p
                    // where delta_eps_p = (q - sigma_y) / (3*mu + H_iso)
                    var denominator = 3f * mu + H_iso;
                    var delta_eps_p = f / denominator;

                    // Update plastic strain
                    _plasticStrainEq[x, y, z] += delta_eps_p;
                    localPlasticStrain += delta_eps_p;

                    // Update yield stress (isotropic hardening)
                    _yieldStress[x, y, z] += H_iso * delta_eps_p;

                    // Return to yield surface: scale deviatoric stress
                    var scale = (q - 3f * mu * delta_eps_p) / q;

                    if (scale < 0) scale = 0; // Safety check

                    // Update stress state
                    sxx_dev *= scale;
                    syy_dev *= scale;
                    szz_dev *= scale;
                    sxy *= scale;
                    sxz *= scale;
                    syz *= scale;

                    // Reconstruct total stress
                    results.StressXX[x, y, z] = sxx_dev + p;
                    results.StressYY[x, y, z] = syy_dev + p;
                    results.StressZZ[x, y, z] = szz_dev + p;
                    results.StressXY[x, y, z] = sxy;
                    results.StressXZ[x, y, z] = sxz;
                    results.StressYZ[x, y, z] = syz;

                    // Update principal stresses
                    var (s1, s2, s3) = CalculatePrincipalStresses(
                        results.StressXX[x, y, z],
                        results.StressYY[x, y, z],
                        results.StressZZ[x, y, z],
                        results.StressXY[x, y, z],
                        results.StressXZ[x, y, z],
                        results.StressYZ[x, y, z]);

                    results.Sigma1[x, y, z] = s1;
                    results.Sigma2[x, y, z] = s2;
                    results.Sigma3[x, y, z] = s3;
                }
            }

            lock (lockObj)
            {
                yieldedVoxels += localYielded;
                totalPlasticStrain += localPlasticStrain;
            }
        });

        var yieldPercent = 100f * yieldedVoxels / results.TotalVoxels;
        var avgPlasticStrain = totalPlasticStrain / Math.Max(1, yieldedVoxels);

        Logger.Log($"[GeomechCPU] Plasticity correction: {yieldedVoxels:N0} voxels yielded ({yieldPercent:F2}%)");
        Logger.Log($"[GeomechCPU] Average plastic strain: {avgPlasticStrain:E4}");
        Logger.Log($"[GeomechCPU] Plasticity completed in {sw.ElapsedMilliseconds} ms");

        // Store plasticity info in results
        results.PlasticStrainField = _plasticStrainEq;
        results.YieldedVoxels = yieldedVoxels;
        results.AveragePlasticStrain = (float)avgPlasticStrain;
    }
}