// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulatorCPU_Damage.cs
// Partial class extension for damage mechanics

using System.Diagnostics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public partial class GeomechanicalSimulatorCPU
{
    private bool[,,] _damageInitiated; // Has damage started at this voxel?

    // Damage state variables
    private float[,,] _damageVariable; // D ∈ [0,1]: 0=intact, 1=fully damaged
    private float[,,] _equivalentStrainHistory; // Maximum equivalent strain experienced

    /// <summary>
    ///     Initialize damage tracking arrays
    /// </summary>
    private void InitializeDamageTracking(int w, int h, int d)
    {
        Logger.Log("[GeomechCPU] Initializing damage tracking...");

        _damageVariable = new float[w, h, d];
        _equivalentStrainHistory = new float[w, h, d];
        _damageInitiated = new bool[w, h, d];

        // All voxels start intact (D=0)
        Logger.Log("[GeomechCPU] Damage tracking initialized (all voxels intact)");
    }

    /// <summary>
    ///     Update damage based on current stress/strain state
    ///     Uses continuum damage mechanics with exponential evolution
    /// </summary>
    private void UpdateDamage(GeomechanicalResults results, byte[,,] labels)
    {
        if (!_params.EnableDamageEvolution) return;

        var sw = Stopwatch.StartNew();
        Logger.Log("[GeomechCPU] Updating damage evolution...");

        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        // Initialize on first call
        if (_damageVariable == null) InitializeDamageTracking(w, h, d);

        var E = _params.YoungModulus * 1e6f; // Pa
        var nu = _params.PoissonRatio;

        // Damage parameters
        var eps_0 = _params.DamageThreshold; // Damage initiation threshold
        var eps_f = _params.DamageCriticalStrain; // Critical strain (D→1)
        var damageExponent = _params.DamageEvolutionRate; // Controls damage growth rate

        var newDamageVoxels = 0;
        var criticalDamageVoxels = 0;
        double totalDamage = 0;
        var lockObj = new object();

        // Process in slices for cache efficiency
        const int SLICE_SIZE = 16;
        var numSlices = (d + SLICE_SIZE - 1) / SLICE_SIZE;

        Parallel.For(0, numSlices, sliceIdx =>
        {
            var startZ = sliceIdx * SLICE_SIZE;
            var endZ = Math.Min(startZ + SLICE_SIZE, d);

            var localNewDamage = 0;
            var localCriticalDamage = 0;
            double localTotalDamage = 0;

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

                // Calculate equivalent strain (von Mises)
                var eps_eq = CalculateEquivalentStrain(sxx, syy, szz, sxy, sxz, syz, E, nu);

                // Update strain history (maximum strain ever experienced)
                if (eps_eq > _equivalentStrainHistory[x, y, z]) _equivalentStrainHistory[x, y, z] = eps_eq;

                var eps_max = _equivalentStrainHistory[x, y, z];

                // Check if damage should initiate
                if (!_damageInitiated[x, y, z] && eps_max >= eps_0)
                {
                    _damageInitiated[x, y, z] = true;
                    localNewDamage++;
                }

                // Update damage variable if damage has initiated
                if (_damageInitiated[x, y, z])
                {
                    var D_old = _damageVariable[x, y, z];
                    float D_new;

                    if (_params.DamageModel == DamageModel.Exponential)
                    {
                        // Exponential damage evolution (Mazars model)
                        // D = 1 - (eps_0/eps_max) * exp[-damageExponent * (eps_max - eps_0)]
                        if (eps_max > eps_0)
                        {
                            var ratio = eps_0 / eps_max;
                            var exponent = -damageExponent * (eps_max - eps_0);
                            D_new = 1f - ratio * MathF.Exp(exponent);
                        }
                        else
                        {
                            D_new = 0f;
                        }
                    }
                    else // Linear
                    {
                        // Linear damage evolution
                        // D = (eps_max - eps_0) / (eps_f - eps_0)
                        if (eps_max >= eps_f)
                            D_new = 1f;
                        else if (eps_max > eps_0)
                            D_new = (eps_max - eps_0) / (eps_f - eps_0);
                        else
                            D_new = 0f;
                    }

                    // Ensure monotonic increase (damage can't heal)
                    D_new = Math.Max(D_old, D_new);

                    // Clamp to [0,1]
                    D_new = Math.Clamp(D_new, 0f, 1f);

                    _damageVariable[x, y, z] = D_new;
                    localTotalDamage += D_new;

                    if (D_new >= 0.99f) localCriticalDamage++;

                    // Update damage field for visualization (0-255)
                    results.DamageField[x, y, z] = (byte)(D_new * 255f);

                    // Degrade material properties based on damage
                    // Effective modulus: E_eff = (1-D) * E
                    // This affects future iterations
                    if (_params.ApplyDamageToStiffness && D_new > 0.01f)
                    {
                        var stiffnessFactor = 1f - D_new;

                        // Degrade stresses proportionally
                        results.StressXX[x, y, z] *= stiffnessFactor;
                        results.StressYY[x, y, z] *= stiffnessFactor;
                        results.StressZZ[x, y, z] *= stiffnessFactor;
                        results.StressXY[x, y, z] *= stiffnessFactor;
                        results.StressXZ[x, y, z] *= stiffnessFactor;
                        results.StressYZ[x, y, z] *= stiffnessFactor;

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
            }

            lock (lockObj)
            {
                newDamageVoxels += localNewDamage;
                criticalDamageVoxels += localCriticalDamage;
                totalDamage += localTotalDamage;
            }
        });

        var damagedVoxels = 0;
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            if (labels[x, y, z] != 0 && _damageVariable[x, y, z] > 0.01f)
                damagedVoxels++;

        var damagePercent = 100f * damagedVoxels / results.TotalVoxels;
        var avgDamage = damagedVoxels > 0 ? totalDamage / damagedVoxels : 0;

        Logger.Log($"[GeomechCPU] Damage update: {damagedVoxels:N0} voxels damaged ({damagePercent:F2}%)");
        Logger.Log($"[GeomechCPU] New damage: {newDamageVoxels:N0} voxels");
        Logger.Log($"[GeomechCPU] Critical damage (D>0.99): {criticalDamageVoxels:N0} voxels");
        Logger.Log($"[GeomechCPU] Average damage: {avgDamage:F4}");
        Logger.Log($"[GeomechCPU] Damage evolution completed in {sw.ElapsedMilliseconds} ms");

        // Store damage info in results
        results.DamageVariableField = _damageVariable;
        results.DamagedVoxels = damagedVoxels;
        results.CriticallyDamagedVoxels = criticalDamageVoxels;
        results.AverageDamage = (float)avgDamage;
        results.MaximumDamage = FindMaxDamage(_damageVariable, labels);
    }

    /// <summary>
    ///     Calculate equivalent strain from stress state
    /// </summary>
    private float CalculateEquivalentStrain(float sxx, float syy, float szz,
        float sxy, float sxz, float syz, float E, float nu)
    {
        // Convert stress to strain using Hooke's law
        var eps_xx = (sxx - nu * (syy + szz)) / E;
        var eps_yy = (syy - nu * (sxx + szz)) / E;
        var eps_zz = (szz - nu * (sxx + syy)) / E;
        var gamma_xy = 2f * (1f + nu) * sxy / E;
        var gamma_xz = 2f * (1f + nu) * sxz / E;
        var gamma_yz = 2f * (1f + nu) * syz / E;

        // Calculate equivalent strain (von Mises definition)
        // eps_eq = sqrt(2/3 * eps_dev : eps_dev)
        var eps_m = (eps_xx + eps_yy + eps_zz) / 3f;
        var eps_xx_dev = eps_xx - eps_m;
        var eps_yy_dev = eps_yy - eps_m;
        var eps_zz_dev = eps_zz - eps_m;

        var eps_eq_squared = 2f / 3f * (
            eps_xx_dev * eps_xx_dev +
            eps_yy_dev * eps_yy_dev +
            eps_zz_dev * eps_zz_dev +
            0.5f * (gamma_xy * gamma_xy + gamma_xz * gamma_xz + gamma_yz * gamma_yz)
        );

        return MathF.Sqrt(Math.Max(0, eps_eq_squared));
    }

    /// <summary>
    ///     Find maximum damage value in field
    /// </summary>
    private float FindMaxDamage(float[,,] damageField, byte[,,] labels)
    {
        var maxDamage = 0f;

        for (var z = 0; z < damageField.GetLength(2); z++)
        for (var y = 0; y < damageField.GetLength(1); y++)
        for (var x = 0; x < damageField.GetLength(0); x++)
            if (labels[x, y, z] != 0)
                maxDamage = Math.Max(maxDamage, damageField[x, y, z]);

        return maxDamage;
    }
}