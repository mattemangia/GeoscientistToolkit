// GeoscientistToolkit/Analysis/Multiphase/MultiphaseFlowSolver.cs
//
// Multiphase flow solver for water-steam-NCG (non-condensable gas) systems
// Implements TOUGH2/TOUGH3-like multiphase flow equations with phase equilibrium
//
// References:
// - Pruess, K., Oldenburg, C., & Moridis, G. (2012). TOUGH2 User's Guide, Version 2.1. LBNL-43134.
// - Pruess, K. (2004). The TOUGH codes—A family of simulation tools for multiphase flow and transport. VZJ, 3(3), 738-746.
// - Bear, J. (1972). Dynamics of Fluids in Porous Media. Dover Publications.
// - Corey, A. T. (1954). The interrelation between gas and oil relative permeabilities. Producers Monthly, 19(1), 38-41.
// - IAPWS-IF97: International standard for industrial water and steam properties

using System;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Analysis.Thermodynamic;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Multiphase;

/// <summary>
/// Multiphase flow solver for 3-phase systems (liquid water, steam, non-condensable gas)
/// Solves coupled mass and energy conservation equations with phase equilibrium
/// Similar to TOUGH2's EOS1 (water-steam), EOS2 (water-CO2), and EOS3 (water-air)
/// </summary>
public class MultiphaseFlowSolver
{
    private const double R_GAS_CONSTANT = 8.314462618; // J/(mol·K)
    private const double MOLECULAR_WEIGHT_H2O = 18.015e-3; // kg/mol
    private const double MOLECULAR_WEIGHT_CO2 = 44.01e-3; // kg/mol
    private const double MOLECULAR_WEIGHT_AIR = 28.97e-3; // kg/mol

    private const int MAX_ITERATIONS = 50;
    private const double CONVERGENCE_TOLERANCE = 1e-6;

    /// <summary>
    /// Multiphase equation of state type
    /// </summary>
    public enum EOSType
    {
        /// <summary>Water-steam two-phase (like TOUGH2 EOS1)</summary>
        WaterSteam,

        /// <summary>Water-steam-CO2 three-phase (like TOUGH2 EOS2)</summary>
        WaterCO2,

        /// <summary>Water-steam-air three-phase (like TOUGH2 EOS3)</summary>
        WaterAir,

        /// <summary>Water-steam-H2S three-phase</summary>
        WaterH2S,

        /// <summary>Water-steam-CH4 three-phase</summary>
        WaterMethane
    }

    private readonly EOSType _eosType;
    private readonly WaterPropertiesIAPWS _waterProps;

    public MultiphaseFlowSolver(EOSType eosType = EOSType.WaterCO2)
    {
        _eosType = eosType;
        _waterProps = new WaterPropertiesIAPWS();
    }

    /// <summary>
    /// Solve one time step of multiphase flow equations
    /// Implements sequential solution: pressure equation → saturation update → temperature update
    /// </summary>
    public MultiphaseState SolveTimeStep(MultiphaseState state, double dt, MultiphaseParameters parameters)
    {
        var newState = state.Clone();

        // Outer iteration loop for coupling between pressure, saturation, and temperature
        for (int iter = 0; iter < MAX_ITERATIONS; iter++)
        {
            var oldState = newState.Clone();

            // Step 1: Update phase equilibrium at current T, P
            UpdatePhaseEquilibrium(newState);

            // Step 2: Solve pressure equation (implicit)
            SolvePressureEquation(newState, dt, parameters);

            // Step 3: Solve saturation equations (IMPES or implicit)
            SolveSaturationEquations(newState, dt, parameters);

            // Step 4: Solve energy equation for temperature
            SolveEnergyEquation(newState, dt, parameters);

            // Step 5: Update fluid properties
            UpdateFluidProperties(newState);

            // Check convergence
            double maxChange = CalculateMaxChange(oldState, newState);

            if (maxChange < CONVERGENCE_TOLERANCE)
            {
                Logger.Log($"[MultiphaseFlow] Converged in {iter + 1} iterations");
                break;
            }

            if (iter == MAX_ITERATIONS - 1)
            {
                Logger.LogWarning($"[MultiphaseFlow] Did not converge in {MAX_ITERATIONS} iterations. Max change: {maxChange:E3}");
            }
        }

        return newState;
    }

    /// <summary>
    /// Update phase equilibrium: determine which phases are present and their compositions
    /// </summary>
    private void UpdatePhaseEquilibrium(MultiphaseState state)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            double T_K = state.Temperature[i, j, k];
            double P_Pa = state.Pressure[i, j, k];

            // Get water saturation pressure (phase transition)
            double P_sat = PhaseTransitionHandler.GetSaturationPressure(T_K) * 1e6; // MPa to Pa

            // Determine phase state
            if (P_Pa >= P_sat)
            {
                // Subcooled liquid or compressed liquid
                // Only liquid water present (unless NCG is present)
                state.LiquidSaturation[i, j, k] = 1.0f - state.GasSaturation[i, j, k];
                state.VaporSaturation[i, j, k] = 0.0f;
            }
            else
            {
                // Two-phase or superheated steam
                // Calculate equilibrium saturations based on total enthalpy
                CalculateTwoPhaseEquilibrium(state, i, j, k);
            }

            // For NCG (CO2, air, etc.)
            if (_eosType != EOSType.WaterSteam)
            {
                // Calculate gas phase composition using Henry's law for dissolved gas
                // and ideal gas law for gas phase
                CalculateGasEquilibrium(state, i, j, k);
            }

            // Ensure saturations sum to 1
            NormalizeSaturations(state, i, j, k);
        }
    }

    /// <summary>
    /// Calculate two-phase (liquid-vapor) equilibrium for water
    /// </summary>
    private void CalculateTwoPhaseEquilibrium(MultiphaseState state, int i, int j, int k)
    {
        double T_K = state.Temperature[i, j, k];
        double P_Pa = state.Pressure[i, j, k];
        double enthalpy = state.Enthalpy[i, j, k];

        // Get saturation properties
        var (h_liquid, h_vapor, rho_liquid, rho_vapor) = GetSaturationProperties(T_K);

        if (enthalpy < h_liquid)
        {
            // Subcooled liquid
            state.LiquidSaturation[i, j, k] = 1.0f;
            state.VaporSaturation[i, j, k] = 0.0f;
        }
        else if (enthalpy > h_vapor)
        {
            // Superheated vapor
            state.LiquidSaturation[i, j, k] = 0.0f;
            state.VaporSaturation[i, j, k] = 1.0f;
        }
        else
        {
            // Two-phase: calculate vapor quality x = (h - h_l) / (h_v - h_l)
            double vapor_quality = (enthalpy - h_liquid) / (h_vapor - h_liquid);
            vapor_quality = Math.Clamp(vapor_quality, 0.0, 1.0);

            // Vapor saturation from quality and densities
            // x = (S_v * rho_v) / (S_v * rho_v + S_l * rho_l)
            // Solve for S_v:
            double S_v = vapor_quality * rho_liquid /
                        (rho_vapor * (1 - vapor_quality) + vapor_quality * rho_liquid);

            state.VaporSaturation[i, j, k] = (float)Math.Clamp(S_v, 0.0, 1.0);
            state.LiquidSaturation[i, j, k] = 1.0f - state.VaporSaturation[i, j, k] - state.GasSaturation[i, j, k];
        }
    }

    /// <summary>
    /// Calculate gas (NCG) equilibrium with water using Henry's law
    /// </summary>
    private void CalculateGasEquilibrium(MultiphaseState state, int i, int j, int k)
    {
        double T_K = state.Temperature[i, j, k];
        double P_Pa = state.Pressure[i, j, k];

        // Get partial pressure of water vapor
        double P_sat = PhaseTransitionHandler.GetSaturationPressure(T_K) * 1e6; // MPa to Pa
        double P_steam = Math.Min(P_Pa, P_sat);

        // Partial pressure of NCG
        double P_gas = Math.Max(0, P_Pa - P_steam);

        if (P_gas < 1.0)
        {
            // No gas phase
            state.GasSaturation[i, j, k] = 0.0f;
            state.DissolvedGasConcentration[i, j, k] = 0.0f;
            return;
        }

        // Henry's law: C_dissolved = H * P_gas
        // H varies with temperature and gas type
        double H = GetHenryConstant(T_K);
        double C_dissolved = H * P_gas; // mol/L

        state.DissolvedGasConcentration[i, j, k] = (float)C_dissolved;

        // Gas saturation from ideal gas law
        // Simplified: S_g is determined from total gas mass balance
        // This is handled in the saturation solver
    }

    /// <summary>
    /// Get Henry's law constant for the NCG at given temperature
    /// </summary>
    private double GetHenryConstant(double T_K)
    {
        // Henry's constant in mol/(L·Pa)
        // Temperature-dependent: ln(H) = A + B/T + C*ln(T)

        return _eosType switch
        {
            EOSType.WaterCO2 => GetHenryConstantCO2(T_K),
            EOSType.WaterAir => GetHenryConstantAir(T_K),
            EOSType.WaterH2S => GetHenryConstantH2S(T_K),
            EOSType.WaterMethane => GetHenryConstantCH4(T_K),
            _ => 0.0
        };
    }

    /// <summary>
    /// Henry's constant for CO2 in water (Carroll et al., 1991)
    /// </summary>
    private double GetHenryConstantCO2(double T_K)
    {
        // ln(H) = A + B/T + C*ln(T) + D*T
        // H in MPa/(mole fraction)
        double A = 7.96;
        double B = -1631.0;
        double C = -5.39;
        double D = 0.0;

        double ln_H_MPa = A + B / T_K + C * Math.Log(T_K);
        double H_MPa = Math.Exp(ln_H_MPa);

        // Convert to mol/(L·Pa)
        double H_mol_L_Pa = 1.0 / (H_MPa * 1e6 * 18.015); // Approximate conversion

        return H_mol_L_Pa * 1e-6; // Order of magnitude: 1e-5 to 1e-6
    }

    private double GetHenryConstantAir(double T_K)
    {
        // Simplified model for air (mixture of N2, O2)
        return 1.5e-6; // mol/(L·Pa) at 25°C
    }

    private double GetHenryConstantH2S(double T_K)
    {
        // H2S is much more soluble than CO2
        return 1.0e-4; // mol/(L·Pa) at 25°C
    }

    private double GetHenryConstantCH4(double T_K)
    {
        // Methane solubility
        return 2.5e-6; // mol/(L·Pa) at 25°C
    }

    /// <summary>
    /// Normalize saturations to ensure S_l + S_v + S_g = 1
    /// </summary>
    private void NormalizeSaturations(MultiphaseState state, int i, int j, int k)
    {
        double S_l = state.LiquidSaturation[i, j, k];
        double S_v = state.VaporSaturation[i, j, k];
        double S_g = state.GasSaturation[i, j, k];

        double total = S_l + S_v + S_g;

        if (total < 0.01)
        {
            // Default to liquid
            state.LiquidSaturation[i, j, k] = 1.0f;
            state.VaporSaturation[i, j, k] = 0.0f;
            state.GasSaturation[i, j, k] = 0.0f;
        }
        else if (Math.Abs(total - 1.0) > 1e-6)
        {
            state.LiquidSaturation[i, j, k] = (float)(S_l / total);
            state.VaporSaturation[i, j, k] = (float)(S_v / total);
            state.GasSaturation[i, j, k] = (float)(S_g / total);
        }
    }

    /// <summary>
    /// Solve pressure equation: ∇·(λ_t ∇P) = accumulation term
    /// where λ_t = Σ(k_r_α / μ_α) is total mobility
    /// </summary>
    private void SolvePressureEquation(MultiphaseState state, double dt, MultiphaseParameters parameters)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        // Simple explicit update for now (can be upgraded to implicit)
        // P^(n+1) = P^n + dt * (sources - sinks + flow)

        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            // Calculate total mobility at cell
            double mobility_total = CalculateTotalMobility(state, i, j, k, parameters);

            // Calculate flow terms using finite differences
            double dx = parameters.GridSpacing.X;
            double dy = parameters.GridSpacing.Y;
            double dz = parameters.GridSpacing.Z;

            double dP_dx = (state.Pressure[i + 1, j, k] - state.Pressure[i - 1, j, k]) / (2 * dx);
            double dP_dy = (state.Pressure[i, j + 1, k] - state.Pressure[i, j - 1, k]) / (2 * dy);
            double dP_dz = (state.Pressure[i, j, k + 1] - state.Pressure[i, j, k - 1]) / (2 * dz);

            // Permeability
            double k_perm = state.Permeability[i, j, k];
            double phi = state.Porosity[i, j, k];

            // Flow term: ∇·(k*λ_t*∇P)
            double d2P_dx2 = (state.Pressure[i + 1, j, k] - 2 * state.Pressure[i, j, k] + state.Pressure[i - 1, j, k]) / (dx * dx);
            double d2P_dy2 = (state.Pressure[i, j + 1, k] - 2 * state.Pressure[i, j, k] + state.Pressure[i, j - 1, k]) / (dy * dy);
            double d2P_dz2 = (state.Pressure[i, j, k + 1] - 2 * state.Pressure[i, j, k] + state.Pressure[i, j, k - 1]) / (dz * dz);

            double laplacian_P = d2P_dx2 + d2P_dy2 + d2P_dz2;

            double flow = k_perm * mobility_total * laplacian_P;

            // Accumulation term (simplified)
            double compressibility = 1e-9; // Pa^-1 (typical for water)
            double accumulation = phi * compressibility;

            // Update pressure
            double dP_dt = flow / accumulation;

            state.Pressure[i, j, k] += (float)(dt * dP_dt);

            // Clamp to reasonable range
            state.Pressure[i, j, k] = Math.Clamp(state.Pressure[i, j, k], 1e5f, 1e8f);
        }
    }

    /// <summary>
    /// Calculate total mobility: λ_t = Σ(k_r_α / μ_α) for all phases α
    /// </summary>
    private double CalculateTotalMobility(MultiphaseState state, int i, int j, int k, MultiphaseParameters parameters)
    {
        double S_l = state.LiquidSaturation[i, j, k];
        double S_v = state.VaporSaturation[i, j, k];
        double S_g = state.GasSaturation[i, j, k];

        double T_K = state.Temperature[i, j, k];
        double P_Pa = state.Pressure[i, j, k];

        // Get fluid viscosities
        double mu_liquid = GetWaterViscosity(T_K, P_Pa); // Pa·s
        double mu_vapor = GetSteamViscosity(T_K, P_Pa);
        double mu_gas = GetGasViscosity(T_K, P_Pa);

        // Get relative permeabilities
        double kr_liquid = RelativePermeabilityModels.VanGenuchtenLiquid(
            S_l, parameters.S_lr, parameters.S_lr + S_l + S_v, parameters.m_vG);

        double kr_vapor = RelativePermeabilityModels.VanGenuchtenGas(
            S_l, parameters.S_lr, 1.0 - parameters.S_gr, parameters.m_vG);

        double kr_gas = RelativePermeabilityModels.Corey(
            S_g, parameters.S_gr, 1.0, 3.0);

        // Total mobility
        double lambda_l = kr_liquid / mu_liquid;
        double lambda_v = kr_vapor / mu_vapor;
        double lambda_g = kr_gas / mu_gas;

        return lambda_l + lambda_v + lambda_g;
    }

    /// <summary>
    /// Get gas viscosity (for NCG)
    /// </summary>
    private double GetGasViscosity(double T_K, double P_Pa)
    {
        // Simple model: μ = μ_0 * (T/T_0)^0.7
        // Typical: 1.5e-5 Pa·s for CO2 at 25°C
        return _eosType switch
        {
            EOSType.WaterCO2 => 1.5e-5 * Math.Pow(T_K / 298.15, 0.7),
            EOSType.WaterAir => 1.8e-5 * Math.Pow(T_K / 298.15, 0.7),
            EOSType.WaterH2S => 1.2e-5 * Math.Pow(T_K / 298.15, 0.7),
            EOSType.WaterMethane => 1.1e-5 * Math.Pow(T_K / 298.15, 0.7),
            _ => 1.5e-5
        };
    }

    /// <summary>
    /// Solve saturation equations for each phase
    /// </summary>
    private void SolveSaturationEquations(MultiphaseState state, double dt, MultiphaseParameters parameters)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        // IMPES approach: saturation is updated explicitly using known pressure field
        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            // Calculate phase velocities using Darcy's law with gravity
            var (v_l, v_v, v_g) = CalculatePhaseVelocities(state, i, j, k, parameters);

            // Update saturations using upwind scheme
            double dx = parameters.GridSpacing.X;
            double dy = parameters.GridSpacing.Y;
            double dz = parameters.GridSpacing.Z;
            double phi = state.Porosity[i, j, k];

            // Liquid saturation update
            double dS_l = -dt / phi * (
                (v_l.vx > 0 ? v_l.vx * (state.LiquidSaturation[i, j, k] - state.LiquidSaturation[i - 1, j, k]) / dx :
                             v_l.vx * (state.LiquidSaturation[i + 1, j, k] - state.LiquidSaturation[i, j, k]) / dx) +
                (v_l.vy > 0 ? v_l.vy * (state.LiquidSaturation[i, j, k] - state.LiquidSaturation[i, j - 1, k]) / dy :
                             v_l.vy * (state.LiquidSaturation[i, j + 1, k] - state.LiquidSaturation[i, j, k]) / dy) +
                (v_l.vz > 0 ? v_l.vz * (state.LiquidSaturation[i, j, k] - state.LiquidSaturation[i, j, k - 1]) / dz :
                             v_l.vz * (state.LiquidSaturation[i, j, k + 1] - state.LiquidSaturation[i, j, k]) / dz)
            );

            state.LiquidSaturation[i, j, k] = (float)Math.Clamp(state.LiquidSaturation[i, j, k] + dS_l, 0.0, 1.0);
        }

        // Normalize saturations after update
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            NormalizeSaturations(state, i, j, k);
        }
    }

    /// <summary>
    /// Calculate phase velocities using multiphase Darcy's law
    /// v_α = -(k * k_r_α / μ_α) * (∇P_α - ρ_α * g)
    /// </summary>
    private ((double vx, double vy, double vz) v_l,
              (double vx, double vy, double vz) v_v,
              (double vx, double vy, double vz) v_g)
    CalculatePhaseVelocities(MultiphaseState state, int i, int j, int k, MultiphaseParameters parameters)
    {
        double dx = parameters.GridSpacing.X;
        double dy = parameters.GridSpacing.Y;
        double dz = parameters.GridSpacing.Z;

        double P = state.Pressure[i, j, k];
        double T_K = state.Temperature[i, j, k];
        double k_perm = state.Permeability[i, j, k];

        double S_l = state.LiquidSaturation[i, j, k];
        double S_v = state.VaporSaturation[i, j, k];
        double S_g = state.GasSaturation[i, j, k];

        // Get fluid properties
        var (rho_l, rho_v) = WaterPropertiesIAPWS.GetWaterPropertiesCached(T_K, P / 1e5);
        double rho_g = GetGasDensity(T_K, P);

        double mu_l = GetWaterViscosity(T_K, P);
        double mu_v = GetSteamViscosity(T_K, P);
        double mu_g = GetGasViscosity(T_K, P);

        // Get relative permeabilities
        double kr_l = RelativePermeabilityModels.VanGenuchtenLiquid(S_l, parameters.S_lr, 1.0 - parameters.S_gr, parameters.m_vG);
        double kr_v = RelativePermeabilityModels.VanGenuchtenGas(S_l, parameters.S_lr, 1.0 - parameters.S_gr, parameters.m_vG);
        double kr_g = RelativePermeabilityModels.Corey(S_g, parameters.S_gr, 1.0, 3.0);

        // Capillary pressures
        double Pc_lv = CapillaryPressureModels.VanGenuchten(S_l, parameters.S_lr, 1.0, parameters.alpha_vG, parameters.m_vG);
        double Pc_lg = Pc_lv * 1.2; // Simplified scaling

        // Phase pressures
        double P_l = P;
        double P_v = P + Pc_lv;
        double P_g = P + Pc_lg;

        // Pressure gradients (simplified - should use phase pressures)
        double dP_dx = i > 0 && i < state.GridDimensions.X - 1 ?
            (state.Pressure[i + 1, j, k] - state.Pressure[i - 1, j, k]) / (2 * dx) : 0;
        double dP_dy = j > 0 && j < state.GridDimensions.Y - 1 ?
            (state.Pressure[i, j + 1, k] - state.Pressure[i, j - 1, k]) / (2 * dy) : 0;
        double dP_dz = k > 0 && k < state.GridDimensions.Z - 1 ?
            (state.Pressure[i, j, k + 1] - state.Pressure[i, j, k - 1]) / (2 * dz) : 0;

        double g = 9.81; // m/s²

        // Darcy velocities for each phase
        double v_lx = -(k_perm * kr_l / mu_l) * (dP_dx);
        double v_ly = -(k_perm * kr_l / mu_l) * (dP_dy);
        double v_lz = -(k_perm * kr_l / mu_l) * (dP_dz - rho_l * g);

        double v_vx = -(k_perm * kr_v / mu_v) * (dP_dx);
        double v_vy = -(k_perm * kr_v / mu_v) * (dP_dy);
        double v_vz = -(k_perm * kr_v / mu_v) * (dP_dz - rho_v * g);

        double v_gx = -(k_perm * kr_g / mu_g) * (dP_dx);
        double v_gy = -(k_perm * kr_g / mu_g) * (dP_dy);
        double v_gz = -(k_perm * kr_g / mu_g) * (dP_dz - rho_g * g);

        return ((v_lx, v_ly, v_lz), (v_vx, v_vy, v_vz), (v_gx, v_gy, v_gz));
    }

    /// <summary>
    /// Get gas density using ideal gas law (can be upgraded to real gas EOS)
    /// </summary>
    private double GetGasDensity(double T_K, double P_Pa)
    {
        // ρ = (P * M) / (R * T)
        double M = _eosType switch
        {
            EOSType.WaterCO2 => MOLECULAR_WEIGHT_CO2,
            EOSType.WaterAir => MOLECULAR_WEIGHT_AIR,
            EOSType.WaterH2S => 34.08e-3, // kg/mol
            EOSType.WaterMethane => 16.04e-3, // kg/mol
            _ => MOLECULAR_WEIGHT_AIR
        };

        return (P_Pa * M) / (R_GAS_CONSTANT * T_K);
    }

    /// <summary>
    /// Solve energy equation: φ*ρ*c_p*∂T/∂t = ∇·(κ∇T) + sources
    /// </summary>
    private void SolveEnergyEquation(MultiphaseState state, double dt, MultiphaseParameters parameters)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            double dx = parameters.GridSpacing.X;
            double dy = parameters.GridSpacing.Y;
            double dz = parameters.GridSpacing.Z;

            // Thermal conductivity (mixture)
            double kappa = parameters.ThermalConductivity; // W/(m·K)

            // Heat capacity (mixture)
            double phi = state.Porosity[i, j, k];
            double rho_rock = 2650.0; // kg/m³
            double c_rock = 800.0; // J/(kg·K)

            double c_eff = (1 - phi) * rho_rock * c_rock; // Simplified

            // Laplacian of temperature
            double d2T_dx2 = (state.Temperature[i + 1, j, k] - 2 * state.Temperature[i, j, k] + state.Temperature[i - 1, j, k]) / (dx * dx);
            double d2T_dy2 = (state.Temperature[i, j + 1, k] - 2 * state.Temperature[i, j, k] + state.Temperature[i, j - 1, k]) / (dy * dy);
            double d2T_dz2 = (state.Temperature[i, j, k + 1] - 2 * state.Temperature[i, j, k] + state.Temperature[i, j, k - 1]) / (dz * dz);

            double laplacian_T = d2T_dx2 + d2T_dy2 + d2T_dz2;

            double dT_dt = (kappa / c_eff) * laplacian_T;

            state.Temperature[i, j, k] += (float)(dt * dT_dt);

            // Clamp to reasonable range
            state.Temperature[i, j, k] = Math.Clamp(state.Temperature[i, j, k], 273.15f, 673.15f);
        }
    }

    /// <summary>
    /// Update fluid properties (density, viscosity, etc.) at current T, P
    /// </summary>
    private void UpdateFluidProperties(MultiphaseState state)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            double T_K = state.Temperature[i, j, k];
            double P_Pa = state.Pressure[i, j, k];

            // Get water/steam properties
            var (rho_l, rho_v) = WaterPropertiesIAPWS.GetWaterPropertiesCached(T_K, P_Pa / 1e5);

            state.LiquidDensity[i, j, k] = (float)rho_l;
            state.VaporDensity[i, j, k] = (float)rho_v;
            state.GasDensity[i, j, k] = (float)GetGasDensity(T_K, P_Pa);

            // Get enthalpy
            var (h_l, h_v, _, _) = GetSaturationProperties(T_K);

            double S_l = state.LiquidSaturation[i, j, k];
            double S_v = state.VaporSaturation[i, j, k];

            // Mixture enthalpy
            state.Enthalpy[i, j, k] = (float)((S_l * rho_l * h_l + S_v * rho_v * h_v) /
                                              (S_l * rho_l + S_v * rho_v + 1e-10));
        }
    }

    /// <summary>
    /// Calculate maximum change between states for convergence check
    /// </summary>
    private double CalculateMaxChange(MultiphaseState oldState, MultiphaseState newState)
    {
        double maxChange = 0.0;

        int nx = oldState.GridDimensions.X;
        int ny = oldState.GridDimensions.Y;
        int nz = oldState.GridDimensions.Z;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            double dP = Math.Abs(newState.Pressure[i, j, k] - oldState.Pressure[i, j, k]) /
                       Math.Max(oldState.Pressure[i, j, k], 1e5);
            double dT = Math.Abs(newState.Temperature[i, j, k] - oldState.Temperature[i, j, k]) /
                       Math.Max(oldState.Temperature[i, j, k], 273.15);
            double dS = Math.Abs(newState.LiquidSaturation[i, j, k] - oldState.LiquidSaturation[i, j, k]);

            maxChange = Math.Max(maxChange, Math.Max(dP, Math.Max(dT, dS)));
        }

        return maxChange;
    }

    /// <summary>
    /// Get saturation properties (enthalpy and density) for liquid and vapor water at given temperature
    /// </summary>
    private static (double h_liquid, double h_vapor, double rho_liquid, double rho_vapor) GetSaturationProperties(double T_K)
    {
        // Get saturation pressure
        double P_sat_MPa = PhaseTransitionHandler.GetSaturationPressure(T_K);
        double P_sat_bar = P_sat_MPa * 10.0; // MPa to bar

        // Get liquid and vapor properties at saturation
        var (_, rho_liquid) = WaterPropertiesIAPWS.GetWaterPropertiesCached(T_K, P_sat_bar);

        // For vapor, use simplified correlation
        // Ideal gas approximation: ρ = P·M/(R·T)
        const double M_H2O = 18.015e-3; // kg/mol
        const double R = 8.314462618; // J/(mol·K)
        double rho_vapor = (P_sat_MPa * 1e6 * M_H2O) / (R * T_K);

        // Get enthalpies
        double h_liquid = PhaseTransitionHandler.GetSaturatedLiquidEnthalpy(T_K);
        double h_fg = PhaseTransitionHandler.GetLatentHeat(T_K);
        double h_vapor = h_liquid + h_fg;

        return (h_liquid, h_vapor, rho_liquid, rho_vapor);
    }

    /// <summary>
    /// Get water (liquid) viscosity at given temperature and pressure
    /// </summary>
    private static double GetWaterViscosity(double T_K, double P_Pa)
    {
        // Simplified correlation for water viscosity (IAPWS formulation simplified)
        // μ(T) = μ_ref * exp(B / T)
        // Valid for liquid water at moderate pressures

        double T_C = T_K - 273.15;

        // Vogel-Fulcher-Tammann equation (simplified)
        double A = 2.414e-5; // Pa·s
        double B = 247.8; // K
        double C = 140.0; // K

        double mu = A * Math.Pow(10.0, B / (T_C + C));

        return mu; // Pa·s
    }

    /// <summary>
    /// Get steam (vapor) viscosity at given temperature and pressure
    /// </summary>
    private static double GetSteamViscosity(double T_K, double P_Pa)
    {
        // Simplified correlation for steam viscosity
        // μ = μ_0 * (T/T_0)^0.7

        const double mu_0 = 1.0e-5; // Pa·s at T_0 = 373.15 K
        const double T_0 = 373.15; // K

        double mu = mu_0 * Math.Pow(T_K / T_0, 0.7);

        return mu; // Pa·s
    }
}

/// <summary>
/// Multiphase flow state container
/// </summary>
public class MultiphaseState
{
    public (int X, int Y, int Z) GridDimensions { get; set; }

    // Primary variables
    public float[,,] Pressure { get; set; }       // Pa
    public float[,,] Temperature { get; set; }    // K
    public float[,,] LiquidSaturation { get; set; } // fraction
    public float[,,] VaporSaturation { get; set; }  // fraction
    public float[,,] GasSaturation { get; set; }    // fraction

    // Secondary variables
    public float[,,] LiquidDensity { get; set; }  // kg/m³
    public float[,,] VaporDensity { get; set; }   // kg/m³
    public float[,,] GasDensity { get; set; }     // kg/m³
    public float[,,] Enthalpy { get; set; }       // J/kg
    public float[,,] DissolvedGasConcentration { get; set; } // mol/L

    // Rock properties
    public float[,,] Porosity { get; set; }
    public float[,,] Permeability { get; set; }

    public MultiphaseState((int x, int y, int z) gridSize)
    {
        GridDimensions = gridSize;
        int nx = gridSize.x, ny = gridSize.y, nz = gridSize.z;

        Pressure = new float[nx, ny, nz];
        Temperature = new float[nx, ny, nz];
        LiquidSaturation = new float[nx, ny, nz];
        VaporSaturation = new float[nx, ny, nz];
        GasSaturation = new float[nx, ny, nz];

        LiquidDensity = new float[nx, ny, nz];
        VaporDensity = new float[nx, ny, nz];
        GasDensity = new float[nx, ny, nz];
        Enthalpy = new float[nx, ny, nz];
        DissolvedGasConcentration = new float[nx, ny, nz];

        Porosity = new float[nx, ny, nz];
        Permeability = new float[nx, ny, nz];
    }

    public MultiphaseState Clone()
    {
        var clone = new MultiphaseState(GridDimensions)
        {
            Pressure = (float[,,])Pressure.Clone(),
            Temperature = (float[,,])Temperature.Clone(),
            LiquidSaturation = (float[,,])LiquidSaturation.Clone(),
            VaporSaturation = (float[,,])VaporSaturation.Clone(),
            GasSaturation = (float[,,])GasSaturation.Clone(),
            LiquidDensity = (float[,,])LiquidDensity.Clone(),
            VaporDensity = (float[,,])VaporDensity.Clone(),
            GasDensity = (float[,,])GasDensity.Clone(),
            Enthalpy = (float[,,])Enthalpy.Clone(),
            DissolvedGasConcentration = (float[,,])DissolvedGasConcentration.Clone(),
            Porosity = (float[,,])Porosity.Clone(),
            Permeability = (float[,,])Permeability.Clone()
        };
        return clone;
    }
}

/// <summary>
/// Multiphase flow parameters
/// </summary>
public class MultiphaseParameters
{
    public (double X, double Y, double Z) GridSpacing { get; set; } = (1.0, 1.0, 1.0); // m

    // Relative permeability parameters (van Genuchten)
    public double S_lr { get; set; } = 0.05;  // Residual liquid saturation
    public double S_gr { get; set; } = 0.01;  // Residual gas saturation
    public double m_vG { get; set; } = 0.5;   // van Genuchten m parameter
    public double alpha_vG { get; set; } = 1e-4; // van Genuchten alpha (1/Pa)

    // Thermal parameters
    public double ThermalConductivity { get; set; } = 2.5; // W/(m·K)
}
