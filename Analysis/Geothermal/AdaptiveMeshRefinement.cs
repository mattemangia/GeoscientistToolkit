// GeoscientistToolkit/Analysis/Geothermal/AdaptiveMeshRefinement.cs
//
// ================================================================================================
// REFERENCES (APA Format):
// ================================================================================================
// Adaptive mesh refinement for geothermal simulations based on:
//
// Peaceman, D. W. (1983). Interpretation of well-block pressures in numerical reservoir simulation
//     with nonsquare grid blocks and anisotropic permeability. Society of Petroleum Engineers Journal,
//     23(03), 531-543. https://doi.org/10.2118/10528-PA
//
// Berger, M. J., & Colella, P. (1989). Local adaptive mesh refinement for shock hydrodynamics.
//     Journal of Computational Physics, 82(1), 64-84. https://doi.org/10.1016/0021-9991(89)90035-1
//
// Geiger, S., Roberts, S., Matthai, S. K., Zoppou, C., & Burri, A. (2004). Combining finite element
//     and finite volume methods for efficient multiphase flow simulations in highly heterogeneous and
//     structurally complex geologic media. Geofluids, 4(4), 284-299.
//     https://doi.org/10.1111/j.1468-8123.2004.00093.x
//
// ================================================================================================

using System;
using System.Collections.Generic;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Adaptive mesh refinement for geothermal simulations
/// Refines mesh in regions with high gradients or near boreholes
/// </summary>
public class AdaptiveMeshRefinement
{
    private readonly GeothermalMesh _baseMesh;
    private readonly AdaptiveMeshOptions _options;

    // Refinement levels (0 = base mesh, 1+ = refined)
    private int[,,] _refinementLevel;

    // Gradient indicators
    private float[,,] _temperatureGradient;
    private float[,,] _pressureGradient;
    private float[,,] _saturationGradient;

    // Refinement flags
    private bool[,,] _needsRefinement;
    private bool[,,] _needsCoarsening;

    public AdaptiveMeshRefinement(GeothermalMesh baseMesh, AdaptiveMeshOptions options)
    {
        _baseMesh = baseMesh;
        _options = options;

        InitializeFields();
    }

    private void InitializeFields()
    {
        int nr = _baseMesh.RadialPoints;
        int ntheta = _baseMesh.AngularPoints;
        int nz = _baseMesh.VerticalPoints;

        _refinementLevel = new int[nr, ntheta, nz];
        _temperatureGradient = new float[nr, ntheta, nz];
        _pressureGradient = new float[nr, ntheta, nz];
        _saturationGradient = new float[nr, ntheta, nz];
        _needsRefinement = new bool[nr, ntheta, nz];
        _needsCoarsening = new bool[nr, ntheta, nz];

        // Initialize all cells to base level
        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 0; k < nz; k++)
        {
            _refinementLevel[i, j, k] = 0;
        }
    }

    /// <summary>
    /// Analyze fields and determine which cells need refinement/coarsening
    /// </summary>
    public void AnalyzeRefinement(float[,,] temperature, float[,,] pressure, float[,,] saturation, double currentTime)
    {
        int nr = _baseMesh.RadialPoints;
        int ntheta = _baseMesh.AngularPoints;
        int nz = _baseMesh.VerticalPoints;

        // Compute gradients
        ComputeGradients(temperature, pressure, saturation);

        // Reset flags
        Array.Clear(_needsRefinement, 0, _needsRefinement.Length);
        Array.Clear(_needsCoarsening, 0, _needsCoarsening.Length);

        for (int i = 1; i < nr - 1; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            float gradT = _temperatureGradient[i, j, k];
            float gradP = _pressureGradient[i, j, k];
            float gradS = _saturationGradient[i, j, k];

            int currentLevel = _refinementLevel[i, j, k];

            // Check if refinement is needed
            bool highGradient = gradT > _options.TemperatureGradientThreshold ||
                              gradP > _options.PressureGradientThreshold ||
                              gradS > _options.SaturationGradientThreshold;

            // Check proximity to borehole
            float r = _baseMesh.R[i];
            bool nearBorehole = r < _options.BoreholeRefinementRadius;

            // Check time-dependent refinement (e.g., thermal front tracking)
            bool frontRegion = IsThermalFrontRegion(i, j, k, temperature, currentTime);

            if ((highGradient || nearBorehole || frontRegion) && currentLevel < _options.MaxRefinementLevel)
            {
                _needsRefinement[i, j, k] = true;
            }
            else if (!highGradient && !nearBorehole && !frontRegion && currentLevel > 0)
            {
                // Can coarsen if gradients are low and not near critical regions
                _needsCoarsening[i, j, k] = true;
            }
        }

        // Smooth refinement flags (ensure gradual transitions)
        SmoothRefinementFlags();
    }

    private void ComputeGradients(float[,,] temperature, float[,,] pressure, float[,,] saturation)
    {
        int nr = _baseMesh.RadialPoints;
        int ntheta = _baseMesh.AngularPoints;
        int nz = _baseMesh.VerticalPoints;

        for (int i = 1; i < nr - 1; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            // Central difference gradients
            float r = _baseMesh.R[i];
            float dr = (i < nr - 1) ? (_baseMesh.R[i + 1] - _baseMesh.R[i]) : (_baseMesh.R[i] - _baseMesh.R[i - 1]);
            float dz = (k < nz - 1) ? (_baseMesh.Z[k + 1] - _baseMesh.Z[k]) : (_baseMesh.Z[k] - _baseMesh.Z[k - 1]);
            float dtheta = (j < ntheta - 1) ? (_baseMesh.Theta[j + 1] - _baseMesh.Theta[j]) : (_baseMesh.Theta[j] - _baseMesh.Theta[j - 1]);

            // Temperature gradient magnitude
            float dT_dr = (temperature[i + 1, j, k] - temperature[i - 1, j, k]) / (2.0f * dr);
            int jnext = (j + 1) % ntheta;
            int jprev = (j - 1 + ntheta) % ntheta;
            float dT_dtheta = (temperature[i, jnext, k] - temperature[i, jprev, k]) / (2.0f * r * dtheta);
            float dT_dz = (temperature[i, j, k + 1] - temperature[i, j, k - 1]) / (2.0f * dz);
            _temperatureGradient[i, j, k] = (float)Math.Sqrt(dT_dr * dT_dr + dT_dtheta * dT_dtheta + dT_dz * dT_dz);

            // Pressure gradient magnitude
            float dP_dr = (pressure[i + 1, j, k] - pressure[i - 1, j, k]) / (2.0f * dr);
            float dP_dtheta = (pressure[i, jnext, k] - pressure[i, jprev, k]) / (2.0f * r * dtheta);
            float dP_dz = (pressure[i, j, k + 1] - pressure[i, j, k - 1]) / (2.0f * dz);
            _pressureGradient[i, j, k] = (float)Math.Sqrt(dP_dr * dP_dr + dP_dtheta * dP_dtheta + dP_dz * dP_dz);

            // Saturation gradient magnitude (if multiphase)
            if (saturation != null)
            {
                float dS_dr = (saturation[i + 1, j, k] - saturation[i - 1, j, k]) / (2.0f * dr);
                float dS_dtheta = (saturation[i, jnext, k] - saturation[i, jprev, k]) / (2.0f * r * dtheta);
                float dS_dz = (saturation[i, j, k + 1] - saturation[i, j, k - 1]) / (2.0f * dz);
                _saturationGradient[i, j, k] = (float)Math.Sqrt(dS_dr * dS_dr + dS_dtheta * dS_dtheta + dS_dz * dS_dz);
            }
        }
    }

    private bool IsThermalFrontRegion(int i, int j, int k, float[,,] temperature, double currentTime)
    {
        // Detect thermal front based on temperature range
        float T = temperature[i, j, k];
        float T_init = _options.InitialTemperature;
        float T_injection = _options.InjectionTemperature;

        // Front is in transition zone between initial and injection temperatures
        float delta_T = Math.Abs(T_injection - T_init);
        if (delta_T < 1.0f) return false;

        float T_normalized = (T - T_init) / delta_T;

        // Refine cells where temperature is between 10% and 90% of transition
        return T_normalized > 0.1f && T_normalized < 0.9f;
    }

    private void SmoothRefinementFlags()
    {
        // Ensure smooth refinement level transitions (max 1 level difference between neighbors)
        int nr = _baseMesh.RadialPoints;
        int ntheta = _baseMesh.AngularPoints;
        int nz = _baseMesh.VerticalPoints;

        bool changed = true;
        int iterations = 0;
        const int maxIterations = 5;

        while (changed && iterations < maxIterations)
        {
            changed = false;
            iterations++;

            for (int i = 1; i < nr - 1; i++)
            for (int j = 0; j < ntheta; j++)
            for (int k = 1; k < nz - 1; k++)
            {
                if (!_needsRefinement[i, j, k]) continue;

                int targetLevel = _refinementLevel[i, j, k] + 1;

                // Check neighbors
                int[] di = { -1, 1, 0, 0, 0, 0 };
                int[] dj = { 0, 0, -1, 1, 0, 0 };
                int[] dk = { 0, 0, 0, 0, -1, 1 };

                for (int n = 0; n < 6; n++)
                {
                    int ii = i + di[n];
                    int jj = (j + dj[n] + ntheta) % ntheta;
                    int kk = k + dk[n];

                    if (ii < 0 || ii >= nr || kk < 0 || kk >= nz) continue;

                    int neighborLevel = _needsRefinement[ii, jj, kk] ? _refinementLevel[ii, jj, kk] + 1 : _refinementLevel[ii, jj, kk];

                    // If level difference > 1, refine neighbor too
                    if (targetLevel - neighborLevel > 1)
                    {
                        if (!_needsRefinement[ii, jj, kk] && _refinementLevel[ii, jj, kk] < _options.MaxRefinementLevel)
                        {
                            _needsRefinement[ii, jj, kk] = true;
                            changed = true;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Apply refinement/coarsening to update refinement levels
    /// </summary>
    public void ApplyRefinement()
    {
        int nr = _baseMesh.RadialPoints;
        int ntheta = _baseMesh.AngularPoints;
        int nz = _baseMesh.VerticalPoints;

        int refinedCells = 0;
        int coarsenedCells = 0;

        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 0; k < nz; k++)
        {
            if (_needsRefinement[i, j, k] && _refinementLevel[i, j, k] < _options.MaxRefinementLevel)
            {
                _refinementLevel[i, j, k]++;
                refinedCells++;
            }
            else if (_needsCoarsening[i, j, k] && _refinementLevel[i, j, k] > 0)
            {
                _refinementLevel[i, j, k]--;
                coarsenedCells++;
            }
        }

        if (refinedCells > 0 || coarsenedCells > 0)
        {
            Logger.Log($"AMR: Refined {refinedCells} cells, coarsened {coarsenedCells} cells");
        }
    }

    /// <summary>
    /// Get effective grid spacing for a cell based on refinement level
    /// </summary>
    public (float dr, float dtheta, float dz) GetEffectiveSpacing(int i, int j, int k)
    {
        int level = _refinementLevel[i, j, k];
        float factor = (float)Math.Pow(2.0, -level); // Each level halves the spacing

        int nr = _baseMesh.RadialPoints;
        int ntheta = _baseMesh.AngularPoints;
        int nz = _baseMesh.VerticalPoints;

        float dr = (i < nr - 1) ? (_baseMesh.R[i + 1] - _baseMesh.R[i]) : (_baseMesh.R[i] - _baseMesh.R[i - 1]);
        float dz = (k < nz - 1) ? (_baseMesh.Z[k + 1] - _baseMesh.Z[k]) : (_baseMesh.Z[k] - _baseMesh.Z[k - 1]);
        float dtheta = (j < ntheta - 1) ? (_baseMesh.Theta[j + 1] - _baseMesh.Theta[j]) : (_baseMesh.Theta[j] - _baseMesh.Theta[j - 1]);

        return (dr * factor, dtheta * factor, dz * factor);
    }

    public int[,,] GetRefinementLevels() => _refinementLevel;
    public float[,,] GetTemperatureGradient() => _temperatureGradient;
    public bool[,,] GetRefinementFlags() => _needsRefinement;
}

/// <summary>
/// Options for adaptive mesh refinement
/// </summary>
public class AdaptiveMeshOptions
{
    // Gradient thresholds for refinement
    public float TemperatureGradientThreshold { get; set; } = 5.0f;  // K/m
    public float PressureGradientThreshold { get; set; } = 1e5f;     // Pa/m
    public float SaturationGradientThreshold { get; set; } = 0.1f;   // 1/m

    // Spatial refinement criteria
    public float BoreholeRefinementRadius { get; set; } = 1.0f;      // m

    // Refinement levels
    public int MaxRefinementLevel { get; set; } = 3;                 // 0 = base, 1-3 = refined

    // Thermal front tracking
    public float InitialTemperature { get; set; } = 10.0f;           // °C
    public float InjectionTemperature { get; set; } = 50.0f;         // °C

    // Refinement frequency
    public int RefinementInterval { get; set; } = 10;                // Refine every N timesteps
}
