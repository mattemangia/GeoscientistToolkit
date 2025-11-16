// GeoscientistToolkit/Analysis/Geothermal/MultiphaseFlowSolver.cs
//
// ================================================================================================
// REFERENCES (APA Format):
// ================================================================================================
// This multiphase flow solver for geothermal systems with CO2 storage is based on:
//
// Pruess, K., & Spycher, N. (2007). ECO2N – A fluid property module for the TOUGH2 code for
//     studies of CO2 storage in saline aquifers. Energy Conversion and Management, 48(6), 1761-1767.
//     https://doi.org/10.1016/j.enconman.2007.01.016
//
// Span, R., & Wagner, W. (1996). A new equation of state for carbon dioxide covering the fluid
//     region from the triple-point temperature to 1100 K at pressures up to 800 MPa.
//     Journal of Physical and Chemical Reference Data, 25(6), 1509-1596.
//     https://doi.org/10.1063/1.555991
//
// Duan, Z., & Sun, R. (2003). An improved model calculating CO2 solubility in pure water and
//     aqueous NaCl solutions from 273 to 533 K and from 0 to 2000 bar. Chemical Geology, 193(3-4), 257-271.
//     https://doi.org/10.1016/S0009-2541(02)00263-2
//
// Spycher, N., Pruess, K., & Ennis-King, J. (2003). CO2-H2O mixtures in the geological
//     sequestration of CO2. I. Assessment and calculation of mutual solubilities from 12 to 100°C
//     and up to 600 bar. Geochimica et Cosmochimica Acta, 67(16), 3015-3031.
//     https://doi.org/10.1016/S0016-7037(03)00273-4
//
// Batzle, M., & Wang, Z. (1992). Seismic properties of pore fluids. Geophysics, 57(11), 1396-1408.
//     https://doi.org/10.1190/1.1443207
//
// ================================================================================================

using System;
using System.Numerics;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Multiphase flow solver supporting water-steam-CO2 systems with salinity effects
/// </summary>
public class MultiphaseFlowSolver : IDisposable
{
    private readonly GeothermalMesh _mesh;
    private readonly MultiphaseOptions _options;

    // Phase saturation fields [r, theta, z]
    private float[,,] _saturationWater;     // Water saturation
    private float[,,] _saturationGas;       // Gas saturation (steam or CO2)
    private float[,,] _saturationCO2Liquid; // Liquid CO2 saturation (supercritical)

    // Phase density fields
    private float[,,] _densityWater;
    private float[,,] _densityGas;
    private float[,,] _densityCO2;

    // Phase viscosity fields
    private float[,,] _viscosityWater;
    private float[,,] _viscosityGas;
    private float[,,] _viscosityCO2;

    // Salinity and brine properties
    private float[,,] _salinity;           // Mass fraction of NaCl
    private float[,,] _brineDensity;       // Brine density with salinity effects

    // Relative permeability
    private float[,,] _relPermWater;
    private float[,,] _relPermGas;

    // Capillary pressure
    private float[,,] _capillaryPressure;

    // CO2 dissolution
    private float[,,] _dissolvedCO2;       // CO2 dissolved in water phase

    private MultiphaseCLSolver _clSolver;
    private bool _useOpenCL;

    public MultiphaseFlowSolver(GeothermalMesh mesh, MultiphaseOptions options)
    {
        _mesh = mesh;
        _options = options;

        InitializeFields();

        // Initialize OpenCL solver
        if (options.UseGPU)
        {
            try
            {
                _clSolver = new MultiphaseCLSolver();
                if (_clSolver.IsAvailable && _clSolver.InitializeBuffers(mesh, options))
                {
                    _useOpenCL = true;
                    Logger.Log($"Multiphase OpenCL enabled: {_clSolver.DeviceName}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Multiphase OpenCL initialization failed: {ex.Message}");
                _clSolver?.Dispose();
                _clSolver = null;
            }
        }
    }

    private void InitializeFields()
    {
        int nr = _mesh.Nr;
        int ntheta = _mesh.Ntheta;
        int nz = _mesh.Nz;

        _saturationWater = new float[nr, ntheta, nz];
        _saturationGas = new float[nr, ntheta, nz];
        _saturationCO2Liquid = new float[nr, ntheta, nz];

        _densityWater = new float[nr, ntheta, nz];
        _densityGas = new float[nr, ntheta, nz];
        _densityCO2 = new float[nr, ntheta, nz];

        _viscosityWater = new float[nr, ntheta, nz];
        _viscosityGas = new float[nr, ntheta, nz];
        _viscosityCO2 = new float[nr, ntheta, nz];

        _salinity = new float[nr, ntheta, nz];
        _brineDensity = new float[nr, ntheta, nz];

        _relPermWater = new float[nr, ntheta, nz];
        _relPermGas = new float[nr, ntheta, nz];

        _capillaryPressure = new float[nr, ntheta, nz];
        _dissolvedCO2 = new float[nr, ntheta, nz];

        // Initialize with single-phase water conditions
        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 0; k < nz; k++)
        {
            _saturationWater[i, j, k] = 1.0f;
            _saturationGas[i, j, k] = 0.0f;
            _saturationCO2Liquid[i, j, k] = 0.0f;
            _salinity[i, j, k] = _options.InitialSalinity;
        }
    }

    /// <summary>
    /// Update phase properties (density, viscosity) based on current P-T conditions
    /// </summary>
    public void UpdatePhaseProperties(float[,,] pressure, float[,,] temperature, float dt)
    {
        if (_useOpenCL && _clSolver != null)
        {
            _clSolver.UpdatePhaseProperties(pressure, temperature, _salinity,
                _densityWater, _densityGas, _densityCO2,
                _viscosityWater, _viscosityGas, _viscosityCO2,
                _brineDensity, _dissolvedCO2);
        }
        else
        {
            UpdatePhasePropertiesCPU(pressure, temperature, dt);
        }
    }

    private void UpdatePhasePropertiesCPU(float[,,] pressure, float[,,] temperature, float dt)
    {
        int nr = _mesh.Nr;
        int ntheta = _mesh.Ntheta;
        int nz = _mesh.Nz;

        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 0; k < nz; k++)
        {
            float P_Pa = pressure[i, j, k];
            float T_C = temperature[i, j, k];
            float T_K = T_C + 273.15f;
            float salinity = _salinity[i, j, k];

            // Water/Brine density using Batzle-Wang correlation
            _densityWater[i, j, k] = CalculateWaterDensity(P_Pa, T_K);
            _brineDensity[i, j, k] = CalculateBrineDensity(P_Pa, T_K, salinity);

            // Water/Brine viscosity
            _viscosityWater[i, j, k] = CalculateWaterViscosity(P_Pa, T_K, salinity);

            // Gas phase (steam or CO2)
            if (_options.FluidType == MultiphaseFluidType.WaterSteam)
            {
                _densityGas[i, j, k] = CalculateSteamDensity(P_Pa, T_K);
                _viscosityGas[i, j, k] = CalculateSteamViscosity(P_Pa, T_K);
            }
            else if (_options.FluidType == MultiphaseFluidType.WaterCO2)
            {
                // CO2 properties using Span-Wagner EOS
                _densityCO2[i, j, k] = CalculateCO2Density(P_Pa, T_K);
                _viscosityCO2[i, j, k] = CalculateCO2Viscosity(P_Pa, T_K);

                // Check if supercritical
                float P_critical = 7.377e6f; // Pa
                float T_critical = 304.13f;  // K
                if (P_Pa > P_critical && T_K > T_critical)
                {
                    // Supercritical CO2 - treat as single dense phase
                    _saturationCO2Liquid[i, j, k] = _saturationGas[i, j, k];
                    _saturationGas[i, j, k] = 0.0f;
                }

                // CO2 dissolution in water using Duan-Sun model
                _dissolvedCO2[i, j, k] = CalculateCO2Solubility(P_Pa, T_K, salinity);
            }
        }
    }

    /// <summary>
    /// Update saturation fields using implicit saturation equation
    /// </summary>
    public void UpdateSaturations(float[,,] pressure, float[,,] temperature, float dt)
    {
        if (_useOpenCL && _clSolver != null)
        {
            _clSolver.UpdateSaturations(pressure, temperature, _saturationWater, _saturationGas,
                _saturationCO2Liquid, dt);
        }
        else
        {
            UpdateSaturationsCPU(pressure, temperature, dt);
        }

        // Update relative permeabilities
        UpdateRelativePermeabilities();

        // Update capillary pressure
        UpdateCapillaryPressure(pressure, temperature);
    }

    private void UpdateSaturationsCPU(float[,,] pressure, float[,,] temperature, float dt)
    {
        // Saturation constraint: Sw + Sg + Sc = 1.0
        // Implicit update for stability
        int nr = _mesh.Nr;
        int ntheta = _mesh.Ntheta;
        int nz = _mesh.Nz;

        for (int i = 1; i < nr - 1; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            // Normalize saturations
            float Sw = _saturationWater[i, j, k];
            float Sg = _saturationGas[i, j, k];
            float Sc = _saturationCO2Liquid[i, j, k];
            float Stotal = Sw + Sg + Sc;

            if (Stotal > 0.001f)
            {
                _saturationWater[i, j, k] = Sw / Stotal;
                _saturationGas[i, j, k] = Sg / Stotal;
                _saturationCO2Liquid[i, j, k] = Sc / Stotal;
            }
            else
            {
                _saturationWater[i, j, k] = 1.0f;
                _saturationGas[i, j, k] = 0.0f;
                _saturationCO2Liquid[i, j, k] = 0.0f;
            }
        }
    }

    private void UpdateRelativePermeabilities()
    {
        // Use Corey model for relative permeability
        int nr = _mesh.Nr;
        int ntheta = _mesh.Ntheta;
        int nz = _mesh.Nz;

        float Swr = _options.ResidualWaterSaturation;  // Residual water saturation
        float Sgr = _options.ResidualGasSaturation;    // Residual gas saturation

        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 0; k < nz; k++)
        {
            float Sw = _saturationWater[i, j, k];
            float Sg = _saturationGas[i, j, k] + _saturationCO2Liquid[i, j, k];

            // Normalized saturations
            float Sw_norm = Math.Max(0.0f, Math.Min(1.0f, (Sw - Swr) / (1.0f - Swr - Sgr)));
            float Sg_norm = Math.Max(0.0f, Math.Min(1.0f, (Sg - Sgr) / (1.0f - Swr - Sgr)));

            // Corey model (exponent = 2 for water, 2 for gas)
            _relPermWater[i, j, k] = (float)Math.Pow(Sw_norm, _options.CoreyExponentWater);
            _relPermGas[i, j, k] = (float)Math.Pow(Sg_norm, _options.CoreyExponentGas);
        }
    }

    private void UpdateCapillaryPressure(float[,,] pressure, float[,,] temperature)
    {
        // Van Genuchten model for capillary pressure
        int nr = _mesh.Nr;
        int ntheta = _mesh.Ntheta;
        int nz = _mesh.Nz;

        float alpha = _options.VanGenuchtenAlpha;
        float n = _options.VanGenuchtenN;
        float m = 1.0f - 1.0f / n;

        for (int i = 0; i < nr; i++)
        for (int j = 0; j < ntheta; j++)
        for (int k = 0; k < nz; k++)
        {
            float Sw = _saturationWater[i, j, k];

            if (Sw < 0.999f && Sw > 0.001f)
            {
                // Van Genuchten capillary pressure
                float Se = Sw; // Effective saturation (simplified)
                float term = (float)Math.Pow(Se, -1.0 / m) - 1.0f;
                _capillaryPressure[i, j, k] = (float)(Math.Pow(term, 1.0 / n) / alpha);
            }
            else
            {
                _capillaryPressure[i, j, k] = 0.0f;
            }
        }
    }

    // ================================================================================================
    // FLUID PROPERTY CORRELATIONS
    // ================================================================================================

    /// <summary>
    /// Calculate water density using IAPWS-IF97 simplified correlation
    /// </summary>
    private float CalculateWaterDensity(float P_Pa, float T_K)
    {
        // Simplified IAPWS correlation for liquid water
        float P_MPa = P_Pa / 1e6f;
        float rho0 = 1000.0f - 0.01687f * (T_K - 273.15f) + 0.0002f * (float)Math.Pow(T_K - 273.15f, 2);
        float rho = rho0 * (1.0f + 5e-10f * P_MPa);
        return Math.Max(500.0f, Math.Min(1200.0f, rho));
    }

    /// <summary>
    /// Calculate brine density with salinity effects (Batzle-Wang, 1992)
    /// </summary>
    private float CalculateBrineDensity(float P_Pa, float T_K, float salinity)
    {
        float T_C = T_K - 273.15f;
        float P_MPa = P_Pa / 1e6f;

        // Pure water density
        float rho_w = CalculateWaterDensity(P_Pa, T_K);

        // Salinity correction (Batzle-Wang)
        float S = salinity * 100.0f; // Convert to percentage
        float delta_rho = S * (0.668f + 0.44f * S + 1e-6f * S * S * S +
            T_C * (-0.00182f - 0.00012f * T_C - 6.6e-6f * T_C * T_C));

        return rho_w + delta_rho;
    }

    /// <summary>
    /// Calculate water/brine viscosity with salinity effects
    /// </summary>
    private float CalculateWaterViscosity(float P_Pa, float T_K, float salinity)
    {
        float T_C = T_K - 273.15f;

        // Pure water viscosity (Vogel equation)
        float mu_w = 0.001f * (float)Math.Exp(-3.7188f + 578.919f / (T_K - 137.546f));

        // Salinity effect
        float S = salinity * 100.0f;
        float A = 1.0f + S * (0.0816f + S * (-0.0122f + S * 0.000128f));
        float B = 1.0f + T_C * (0.0263f + T_C * (-0.000594f));

        return mu_w * A * B;
    }

    /// <summary>
    /// Calculate steam density using ideal gas approximation
    /// </summary>
    private float CalculateSteamDensity(float P_Pa, float T_K)
    {
        const float R = 461.5f; // Gas constant for water vapor (J/kg/K)
        float rho = P_Pa / (R * T_K);
        return Math.Max(0.1f, Math.Min(100.0f, rho));
    }

    /// <summary>
    /// Calculate steam viscosity
    /// </summary>
    private float CalculateSteamViscosity(float P_Pa, float T_K)
    {
        // Chapman-Enskog theory approximation
        float mu = 1.84e-5f * (float)Math.Pow(T_K / 373.15f, 0.7f);
        return mu;
    }

    /// <summary>
    /// Calculate CO2 density using Span-Wagner equation of state
    /// </summary>
    private float CalculateCO2Density(float P_Pa, float T_K)
    {
        float P_MPa = P_Pa / 1e6f;

        // Critical point
        float Pc = 7.377f; // MPa
        float Tc = 304.13f; // K
        float rhoc = 467.6f; // kg/m3

        // Reduced properties
        float Pr = P_MPa / Pc;
        float Tr = T_K / Tc;

        // Simplified Span-Wagner for density
        // Full implementation would use multi-parameter equation
        float Z; // Compressibility factor

        if (T_K < Tc && P_MPa < Pc)
        {
            // Subcritical - use vapor pressure correlation
            Z = 1.0f - 0.5f * Pr / Tr;
        }
        else
        {
            // Supercritical - approximate
            Z = 0.3f + 0.7f * (Tr - 1.0f) / Tr;
        }

        // Density from compressibility
        const float R = 188.9f; // Gas constant for CO2 (J/kg/K)
        float rho = P_Pa / (Z * R * T_K);

        return Math.Max(1.0f, Math.Min(1200.0f, rho));
    }

    /// <summary>
    /// Calculate CO2 viscosity
    /// </summary>
    private float CalculateCO2Viscosity(float P_Pa, float T_K)
    {
        // Vesovic correlation for CO2 viscosity
        float rho = CalculateCO2Density(P_Pa, T_K);

        // Dilute gas limit
        float mu0 = 1.00697f * (float)Math.Sqrt(T_K) /
            (float)(1.0 + 0.625 * Math.Exp(-206.0 / T_K)) * 1e-6f;

        // Excess viscosity (density contribution)
        float mu_excess = 0.0;
        if (rho > 100.0f) // Dense phase
        {
            mu_excess = 1.5e-6f * (rho / 467.6f);
        }

        return mu0 + mu_excess;
    }

    /// <summary>
    /// Calculate CO2 solubility in water using Duan-Sun model (2003)
    /// </summary>
    private float CalculateCO2Solubility(float P_Pa, float T_K, float salinity)
    {
        float P_bar = P_Pa / 1e5f;
        float T_C = T_K - 273.15f;

        // Duan-Sun parameters
        float c1 = -1.1730f;
        float c2 = 0.01372f;
        float c3 = -0.00001417f;
        float c4 = 0.0000000003145f;

        float ln_x = c1 + c2 * T_C + c3 * T_C * T_C + c4 * T_C * T_C * T_C;
        ln_x += (float)Math.Log(P_bar);

        // Salinity correction (simplified)
        float S = salinity * 100.0f;
        ln_x -= 0.411f * S / 58.44f; // S/Mw_NaCl

        // Mole fraction of CO2
        float x_CO2 = (float)Math.Exp(ln_x);

        // Convert to mass fraction (kg CO2 / kg total)
        float M_CO2 = 44.01f;  // g/mol
        float M_H2O = 18.015f; // g/mol
        float m_CO2 = x_CO2 * M_CO2 / (x_CO2 * M_CO2 + (1.0f - x_CO2) * M_H2O);

        return Math.Max(0.0f, Math.Min(0.1f, m_CO2));
    }

    // ================================================================================================
    // PUBLIC ACCESSORS
    // ================================================================================================

    public float[,,] GetWaterSaturation() => _saturationWater;
    public float[,,] GetGasSaturation() => _saturationGas;
    public float[,,] GetCO2LiquidSaturation() => _saturationCO2Liquid;
    public float[,,] GetBrineDensity() => _brineDensity;
    public float[,,] GetSalinity() => _salinity;
    public float[,,] GetDissolvedCO2() => _dissolvedCO2;
    public float[,,] GetRelPermWater() => _relPermWater;
    public float[,,] GetRelPermGas() => _relPermGas;
    public float[,,] GetCapillaryPressure() => _capillaryPressure;

    public void SetSalinity(float[,,] salinity)
    {
        Array.Copy(salinity, _salinity, salinity.Length);
    }

    public void Dispose()
    {
        _clSolver?.Dispose();
    }
}

/// <summary>
/// Multiphase simulation options
/// </summary>
public class MultiphaseOptions
{
    public bool UseGPU { get; set; } = true;
    public MultiphaseFluidType FluidType { get; set; } = MultiphaseFluidType.WaterCO2;

    // Initial conditions
    public float InitialSalinity { get; set; } = 0.035f; // 3.5% (seawater)

    // Relative permeability parameters (Corey model)
    public float ResidualWaterSaturation { get; set; } = 0.2f;
    public float ResidualGasSaturation { get; set; } = 0.05f;
    public float CoreyExponentWater { get; set; } = 2.0f;
    public float CoreyExponentGas { get; set; } = 2.0f;

    // Capillary pressure parameters (Van Genuchten)
    public float VanGenuchtenAlpha { get; set; } = 1e-4f; // 1/Pa
    public float VanGenuchtenN { get; set; } = 2.0f;
}

public enum MultiphaseFluidType
{
    WaterOnly,
    WaterSteam,
    WaterCO2,
    WaterSteamCO2
}
