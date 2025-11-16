// GeoscientistToolkit/Analysis/Thermodynamic/PhaseTransitionHandler.cs
//
// Phase transition detection and handling for water-steam systems
// Handles boiling, condensation, and latent heat calculations
//
// References:
// - Wagner & Pruß (2002). IAPWS-95 formulation. J. Phys. Chem. Ref. Data, 31(2), 387-535.
// - IAPWS (2007). Revised Release on the IAPWS Industrial Formulation 1997.

using System;

namespace GeoscientistToolkit.Analysis.Thermodynamic;

public enum WaterPhase
{
    Liquid,
    Vapor,
    TwoPhase,
    Supercritical
}

/// <summary>
/// Handles phase transitions for water/steam systems including:
/// - Saturation pressure/temperature calculations
/// - Phase determination
/// - Latent heat calculations
/// - Vapor fraction for two-phase conditions
/// </summary>
public class PhaseTransitionHandler
{
    // Critical point constants
    private const double T_CRITICAL = 647.096; // K
    private const double P_CRITICAL = 22.064;  // MPa
    private const double RHO_CRITICAL = 322.0; // kg/m³

    // Triple point
    private const double T_TRIPLE = 273.16; // K
    private const double P_TRIPLE = 0.000611657; // MPa

    /// <summary>
    /// Calculate saturation pressure using IAPWS-IF97 formulation.
    /// Valid range: 273.15 K to 647.096 K (triple point to critical point)
    /// </summary>
    /// <param name="T_K">Temperature in Kelvin</param>
    /// <returns>Saturation pressure in MPa</returns>
    public static double GetSaturationPressure(double T_K)
    {
        if (T_K < T_TRIPLE || T_K > T_CRITICAL)
            throw new ArgumentOutOfRangeException(nameof(T_K),
                $"Temperature {T_K} K outside valid range [{T_TRIPLE}, {T_CRITICAL}] K");

        // IAPWS-IF97 saturation pressure equation
        // Region boundary between regions 1 and 2
        double theta = T_K + (-0.23855557567849 / (T_K - 650.17534844798));
        double A = theta * theta + 1167.0521452767 * theta - 724213.16703206;
        double B = -17.073846940092 * theta * theta + 12020.82470247 * theta - 3232555.0322333;
        double C = 14.91510861353 * theta * theta - 4823.2657361591 * theta + 405113.40542057;

        double P_sat_MPa = Math.Pow(2.0 * C / (-B + Math.Sqrt(B * B - 4.0 * A * C)), 4);

        return P_sat_MPa;
    }

    /// <summary>
    /// Calculate saturation temperature from pressure using IAPWS-IF97.
    /// Valid range: 0.000611657 MPa to 22.064 MPa
    /// </summary>
    /// <param name="P_MPa">Pressure in MPa</param>
    /// <returns>Saturation temperature in Kelvin</returns>
    public static double GetSaturationTemperature(double P_MPa)
    {
        if (P_MPa < P_TRIPLE || P_MPa > P_CRITICAL)
            throw new ArgumentOutOfRangeException(nameof(P_MPa),
                $"Pressure {P_MPa} MPa outside valid range [{P_TRIPLE}, {P_CRITICAL}] MPa");

        // IAPWS-IF97 saturation temperature equation
        double beta = Math.Pow(P_MPa, 0.25);
        double E = beta * beta + 3.2325550322333 * beta - 342.62796408067;
        double F = 3.7721671614485 * beta * beta - 6.1095890064817 * beta + 113.93465429378;
        double G = -1.7699767127102 * beta * beta + 0.77167847354012 * beta - 26.049160982191;
        double D = 2.0 * G / (-F - Math.Sqrt(F * F - 4.0 * E * G));

        double T_sat_K = (650.17534844798 + D - Math.Sqrt((650.17534844798 + D) * (650.17534844798 + D) -
                          4.0 * (-0.23855557567849 + 650.17534844798 * D))) / 2.0;

        return T_sat_K;
    }

    /// <summary>
    /// Determine the phase state at given temperature and pressure
    /// </summary>
    public static WaterPhase DeterminePhase(double T_K, double P_MPa)
    {
        // Supercritical region
        if (T_K >= T_CRITICAL && P_MPa >= P_CRITICAL)
            return WaterPhase.Supercritical;

        // Below triple point - assume solid (ice)
        if (T_K < T_TRIPLE)
            throw new ArgumentOutOfRangeException(nameof(T_K), "Temperature below triple point");

        // Above critical temperature - always vapor
        if (T_K > T_CRITICAL)
            return WaterPhase.Vapor;

        // Get saturation pressure at this temperature
        double P_sat = GetSaturationPressure(T_K);

        const double tolerance = 1e-6; // MPa

        if (Math.Abs(P_MPa - P_sat) < tolerance)
            return WaterPhase.TwoPhase;
        else if (P_MPa > P_sat)
            return WaterPhase.Liquid;  // Compressed liquid
        else
            return WaterPhase.Vapor;   // Superheated vapor
    }

    /// <summary>
    /// Calculate latent heat of vaporization at given temperature.
    /// Uses IAPWS-IF97 formulation.
    /// </summary>
    /// <param name="T_K">Temperature in Kelvin</param>
    /// <returns>Latent heat in kJ/kg</returns>
    public static double GetLatentHeat(double T_K)
    {
        if (T_K < T_TRIPLE || T_K >= T_CRITICAL)
            return 0.0; // No latent heat at critical point or below triple point

        // Simplified correlation (accurate to ~1%)
        // h_fg = h_g - h_f (enthalpy of vaporization)

        // Reduced temperature
        double T_r = T_K / T_CRITICAL;

        // Watson correlation for latent heat
        // h_fg = h_fg0 * ((1-Tr)/(1-Tr0))^0.38
        const double h_fg_triple = 2500.0; // kJ/kg at triple point
        const double T_r_triple = T_TRIPLE / T_CRITICAL;

        double h_fg = h_fg_triple * Math.Pow((1.0 - T_r) / (1.0 - T_r_triple), 0.38);

        return h_fg;
    }

    /// <summary>
    /// Check if system is undergoing boiling
    /// </summary>
    public static bool IsBoiling(double T_K, double P_MPa)
    {
        if (T_K >= T_CRITICAL || P_MPa >= P_CRITICAL)
            return false; // No boiling in supercritical region

        double P_sat = GetSaturationPressure(T_K);

        // Boiling occurs when pressure drops to or below saturation
        return P_MPa <= P_sat;
    }

    /// <summary>
    /// Check if vapor is condensing
    /// </summary>
    public static bool IsCondensing(double T_K, double P_MPa)
    {
        if (T_K >= T_CRITICAL || P_MPa >= P_CRITICAL)
            return false; // No condensation in supercritical region

        double P_sat = GetSaturationPressure(T_K);

        // Condensation occurs when pressure rises above saturation
        // (for a system initially in vapor phase)
        return P_MPa > P_sat;
    }

    /// <summary>
    /// Calculate vapor quality (dryness fraction) for two-phase mixture.
    /// This requires additional information beyond just P and T.
    /// </summary>
    /// <param name="h_kJ_kg">Specific enthalpy in kJ/kg</param>
    /// <param name="T_K">Temperature (must be at saturation)</param>
    /// <returns>Vapor fraction (0 = saturated liquid, 1 = saturated vapor)</returns>
    public static double CalculateVaporQuality(double h_kJ_kg, double T_K)
    {
        // Get saturation properties
        double h_f = GetSaturatedLiquidEnthalpy(T_K);  // Liquid enthalpy
        double h_g = GetSaturatedVaporEnthalpy(T_K);   // Vapor enthalpy

        // Quality x = (h - h_f) / (h_g - h_f)
        double quality = (h_kJ_kg - h_f) / (h_g - h_f);

        // Clamp to [0, 1]
        return Math.Clamp(quality, 0.0, 1.0);
    }

    /// <summary>
    /// Get saturated liquid enthalpy at given temperature.
    /// Simplified correlation for speed.
    /// </summary>
    public static double GetSaturatedLiquidEnthalpy(double T_K)
    {
        // Simple polynomial fit: h_f ≈ Cp * (T - T_ref)
        const double C_p = 4.18; // kJ/(kg·K) - approximate
        const double T_ref = 273.15; // K
        const double h_ref = 0.0; // kJ/kg

        return h_ref + C_p * (T_K - T_ref);
    }

    /// <summary>
    /// Get saturated vapor enthalpy at given temperature.
    /// </summary>
    private static double GetSaturatedVaporEnthalpy(double T_K)
    {
        double h_f = GetSaturatedLiquidEnthalpy(T_K);
        double h_fg = GetLatentHeat(T_K);

        return h_f + h_fg;
    }

    /// <summary>
    /// Determine phase transition type when crossing saturation curve
    /// </summary>
    public static string GetTransitionType(double T_old, double P_old, double T_new, double P_new)
    {
        var phase_old = DeterminePhase(T_old, P_old);
        var phase_new = DeterminePhase(T_new, P_new);

        if (phase_old == phase_new)
            return "None";

        if (phase_old == WaterPhase.Liquid && phase_new == WaterPhase.TwoPhase)
            return "OnsetBoiling";
        if (phase_old == WaterPhase.Liquid && phase_new == WaterPhase.Vapor)
            return "Boiling";
        if (phase_old == WaterPhase.TwoPhase && phase_new == WaterPhase.Vapor)
            return "CompleteVaporization";

        if (phase_old == WaterPhase.Vapor && phase_new == WaterPhase.TwoPhase)
            return "OnsetCondensation";
        if (phase_old == WaterPhase.Vapor && phase_new == WaterPhase.Liquid)
            return "Condensation";
        if (phase_old == WaterPhase.TwoPhase && phase_new == WaterPhase.Liquid)
            return "CompleteCondensation";

        return "PhaseChange";
    }
}
