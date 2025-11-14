// GeoscientistToolkit/Analysis/Multiphase/RelativePermeability.cs
//
// Relative permeability and capillary pressure functions for multiphase flow
// Similar to TOUGH2/TOUGH3 kr and Pc models
//
// References:
// - Brooks, R. H., & Corey, A. T. (1964). Hydraulic properties of porous media. Hydrology Papers, Colorado State University.
// - van Genuchten, M. T. (1980). A closed-form equation for predicting the hydraulic conductivity of unsaturated soils. SSSA J, 44(5), 892-898.
// - Corey, A. T. (1954). The interrelation between gas and oil relative permeabilities. Producers Monthly, 19(1), 38-41.
// - Pruess, K., Oldenburg, C., & Moridis, G. (2012). TOUGH2 User's Guide. LBNL-43134.

using System;

namespace GeoscientistToolkit.Analysis.Multiphase;

/// <summary>
/// Relative permeability models for multiphase flow in porous media.
/// Implements models commonly used in TOUGH simulators.
/// </summary>
public static class RelativePermeabilityModels
{
    /// <summary>
    /// Linear relative permeability model (simplest)
    /// kr = (S - S_r) / (1 - S_r)
    /// </summary>
    /// <param name="S">Phase saturation (0-1)</param>
    /// <param name="S_residual">Residual saturation</param>
    public static double Linear(double S, double S_residual = 0.0)
    {
        double S_eff = CalculateEffectiveSaturation(S, S_residual, 1.0);
        return Math.Clamp(S_eff, 0.0, 1.0);
    }

    /// <summary>
    /// Corey relative permeability model.
    /// kr = (S_eff)^n
    /// Commonly used in TOUGH2 (IRP=1,2)
    /// </summary>
    /// <param name="S">Phase saturation (0-1)</param>
    /// <param name="S_residual">Residual saturation</param>
    /// <param name="S_max">Maximum saturation (default 1.0)</param>
    /// <param name="n">Corey exponent (typical: 2-4)</param>
    public static double Corey(double S, double S_residual, double S_max = 1.0, double n = 2.0)
    {
        double S_eff = CalculateEffectiveSaturation(S, S_residual, S_max);
        return Math.Pow(S_eff, n);
    }

    /// <summary>
    /// van Genuchten-Mualem relative permeability model.
    /// kr_l = sqrt(S_eff) * [1 - (1 - S_eff^(1/m))^m]^2
    /// Commonly used in TOUGH2 (IRP=7)
    /// </summary>
    /// <param name="S">Phase saturation (0-1)</param>
    /// <param name="S_residual">Residual saturation</param>
    /// <param name="S_max">Maximum saturation</param>
    /// <param name="m">van Genuchten parameter (m = 1 - 1/n)</param>
    public static double VanGenuchtenLiquid(double S, double S_residual, double S_max, double m)
    {
        double S_eff = CalculateEffectiveSaturation(S, S_residual, S_max);

        if (S_eff <= 0.0) return 0.0;
        if (S_eff >= 1.0) return 1.0;

        double term1 = Math.Sqrt(S_eff);
        double term2 = 1.0 - Math.Pow(1.0 - Math.Pow(S_eff, 1.0 / m), m);

        return term1 * term2 * term2;
    }

    /// <summary>
    /// van Genuchten-Mualem relative permeability for gas phase.
    /// kr_g = sqrt(1 - S_eff) * [1 - S_eff^(1/m)]^(2m)
    /// </summary>
    public static double VanGenuchtenGas(double S_liquid, double S_residual_liquid, double S_max_liquid, double m)
    {
        double S_eff = CalculateEffectiveSaturation(S_liquid, S_residual_liquid, S_max_liquid);

        if (S_eff >= 1.0) return 0.0;
        if (S_eff <= 0.0) return 1.0;

        double term1 = Math.Sqrt(1.0 - S_eff);
        double term2 = Math.Pow(1.0 - Math.Pow(S_eff, 1.0 / m), m);

        return term1 * term2 * term2;
    }

    /// <summary>
    /// Stone's first three-phase model (for oil-water-gas systems)
    /// Extension of two-phase models
    /// </summary>
    public static double StoneFirstModel(double S_oil, double S_water, double S_gas,
        double S_or, double S_wr, double S_gr)
    {
        // Normalized saturations
        double S_o_eff = (S_oil - S_or) / (1.0 - S_or - S_wr - S_gr);
        double S_w_eff = (S_water - S_wr) / (1.0 - S_or - S_wr - S_gr);
        double S_g_eff = (S_gas - S_gr) / (1.0 - S_or - S_wr - S_gr);

        // This is a simplified version - full model requires two-phase kr curves
        return Math.Clamp(S_o_eff, 0.0, 1.0);
    }

    /// <summary>
    /// Grant relative permeability model (used in geothermal applications).
    /// Specific to TOUGH2 geothermal simulations (IRP=4)
    /// </summary>
    public static double Grant(double S, double S_residual, double S_max = 1.0)
    {
        double S_eff = CalculateEffectiveSaturation(S, S_residual, S_max);

        if (S_eff <= 0.0) return 0.0;
        if (S_eff >= 1.0) return 1.0;

        // Grant's cubic model
        return S_eff * S_eff * S_eff;
    }

    /// <summary>
    /// Calculate effective (normalized) saturation.
    /// S* = (S - S_r) / (S_max - S_r)
    /// </summary>
    private static double CalculateEffectiveSaturation(double S, double S_residual, double S_max)
    {
        if (S_max <= S_residual)
            throw new ArgumentException("S_max must be greater than S_residual");

        return (S - S_residual) / (S_max - S_residual);
    }

    /// <summary>
    /// Interpolation factor for relative permeability
    /// (used in hysteresis models)
    /// </summary>
    public static double InterpolateKr(double kr1, double kr2, double factor)
    {
        return kr1 + factor * (kr2 - kr1);
    }
}

/// <summary>
/// Capillary pressure models for multiphase flow.
/// </summary>
public static class CapillaryPressureModels
{
    /// <summary>
    /// Linear capillary pressure model.
    /// Pc = -P0 * (S - 1) / (1 - S_r)
    /// </summary>
    public static double Linear(double S, double S_residual, double P0_Pa)
    {
        double S_eff = (S - S_residual) / (1.0 - S_residual);
        return -P0_Pa * (1.0 - S_eff);
    }

    /// <summary>
    /// van Genuchten capillary pressure model.
    /// Pc = -P0 * [S_eff^(-1/m) - 1]^(1-m)
    /// Most commonly used in TOUGH2 (ICP=7)
    /// </summary>
    /// <param name="S">Liquid saturation</param>
    /// <param name="S_residual">Residual liquid saturation</param>
    /// <param name="S_max">Maximum liquid saturation</param>
    /// <param name="alpha">van Genuchten alpha parameter (1/Pa)</param>
    /// <param name="m">van Genuchten m parameter (m = 1 - 1/n)</param>
    public static double VanGenuchten(double S, double S_residual, double S_max, double alpha, double m)
    {
        double S_eff = (S - S_residual) / (S_max - S_residual);

        if (S_eff >= 1.0) return 0.0;  // No capillary pressure when fully saturated
        if (S_eff <= 0.0) return double.PositiveInfinity; // Infinite capillary pressure at residual

        S_eff = Math.Clamp(S_eff, 1e-10, 0.9999); // Avoid singularities

        double term = Math.Pow(S_eff, -1.0 / m) - 1.0;
        double Pc = (1.0 / alpha) * Math.Pow(term, 1.0 - m);

        return Pc; // Pa
    }

    /// <summary>
    /// Brooks-Corey capillary pressure model.
    /// Pc = Pe * S_eff^(-1/lambda)
    /// Commonly used in TOUGH2 (ICP=1)
    /// </summary>
    /// <param name="S">Liquid saturation</param>
    /// <param name="S_residual">Residual liquid saturation</param>
    /// <param name="S_max">Maximum liquid saturation</param>
    /// <param name="Pe_Pa">Entry pressure (Pa)</param>
    /// <param name="lambda">Pore size distribution index</param>
    public static double BrooksCorey(double S, double S_residual, double S_max, double Pe_Pa, double lambda)
    {
        double S_eff = (S - S_residual) / (S_max - S_residual);

        if (S_eff >= 1.0) return 0.0;
        if (S_eff <= 0.0) return double.PositiveInfinity;

        S_eff = Math.Clamp(S_eff, 1e-10, 0.9999);

        return Pe_Pa * Math.Pow(S_eff, -1.0 / lambda);
    }

    /// <summary>
    /// Leverett J-function for scaling capillary pressure.
    /// Pc = sigma * sqrt(phi/k) * J(S_eff)
    /// </summary>
    /// <param name="S">Liquid saturation</param>
    /// <param name="S_residual">Residual saturation</param>
    /// <param name="porosity">Porosity (fraction)</param>
    /// <param name="permeability_m2">Absolute permeability (m²)</param>
    /// <param name="surface_tension_N_m">Surface tension (N/m)</param>
    public static double Leverett(double S, double S_residual, double porosity,
        double permeability_m2, double surface_tension_N_m)
    {
        double S_eff = (S - S_residual) / (1.0 - S_residual);

        if (S_eff >= 1.0) return 0.0;
        if (S_eff <= 0.0) return double.PositiveInfinity;

        S_eff = Math.Clamp(S_eff, 1e-10, 0.9999);

        // Leverett J-function (simplified form)
        double J = 0.0;
        if (S_eff < 1.0)
            J = 1.417 * (1.0 - S_eff) - 2.120 * Math.Pow(1.0 - S_eff, 2) +
                1.263 * Math.Pow(1.0 - S_eff, 3);

        double scaling = surface_tension_N_m * Math.Sqrt(porosity / permeability_m2);

        return scaling * J;
    }

    /// <summary>
    /// Convert capillary pressure to water saturation using inverse functions
    /// (useful for initialization)
    /// </summary>
    public static double InverseVanGenuchten(double Pc_Pa, double S_residual, double S_max,
        double alpha, double m)
    {
        if (Pc_Pa <= 0.0) return S_max; // Fully saturated

        // Pc = (1/alpha) * [(S_eff)^(-1/m) - 1]^(1-m)
        // Solve for S_eff:
        double term = Math.Pow(Pc_Pa * alpha, 1.0 / (1.0 - m));
        double S_eff = Math.Pow(1.0 + term, -m);

        double S = S_residual + S_eff * (S_max - S_residual);

        return Math.Clamp(S, S_residual, S_max);
    }

    /// <summary>
    /// Inverse Brooks-Corey
    /// </summary>
    public static double InverseBrooksCorey(double Pc_Pa, double S_residual, double S_max,
        double Pe_Pa, double lambda)
    {
        if (Pc_Pa <= 0.0) return S_max;
        if (Pc_Pa < Pe_Pa) return S_max; // Below entry pressure

        // Pc = Pe * S_eff^(-1/lambda)
        // S_eff = (Pe / Pc)^lambda
        double S_eff = Math.Pow(Pe_Pa / Pc_Pa, lambda);

        double S = S_residual + S_eff * (S_max - S_residual);

        return Math.Clamp(S, S_residual, S_max);
    }
}

/// <summary>
/// Porosity-permeability relationships for reactive transport coupling.
/// Used when minerals precipitate/dissolve and change pore structure.
/// </summary>
public static class PorosityPermeabilityCoupling
{
    /// <summary>
    /// Kozeny-Carman relationship (most common in TOUGH2).
    /// k/k0 = (phi/phi0)^3 * [(1-phi0)/(1-phi)]^2
    /// </summary>
    public static double KozenyCarman(double phi, double phi0, double k0_m2)
    {
        if (phi <= 0.0 || phi >= 1.0)
            return 0.0;

        if (phi0 <= 0.0 || phi0 >= 1.0)
            throw new ArgumentException("Initial porosity must be between 0 and 1");

        double ratio = Math.Pow(phi / phi0, 3.0) * Math.Pow((1.0 - phi0) / (1.0 - phi), 2.0);

        return k0_m2 * ratio;
    }

    /// <summary>
    /// Cubic law for fractures.
    /// k = b^2 / 12
    /// where b is the fracture aperture
    /// </summary>
    public static double CubicLaw(double aperture_m)
    {
        return aperture_m * aperture_m / 12.0;
    }

    /// <summary>
    /// Verma-Pruess model (includes percolation threshold).
    /// k/k0 = [(phi - phi_c) / (phi0 - phi_c)]^n
    /// </summary>
    /// <param name="phi">Current porosity</param>
    /// <param name="phi0">Initial porosity</param>
    /// <param name="phi_critical">Critical porosity (percolation threshold, ~0.03)</param>
    /// <param name="k0_m2">Initial permeability</param>
    /// <param name="n">Exponent (typically 2-3)</param>
    public static double VermaPruess(double phi, double phi0, double phi_critical, double k0_m2, double n = 2.0)
    {
        if (phi <= phi_critical)
            return 0.0; // Below percolation threshold

        if (phi0 <= phi_critical)
            throw new ArgumentException("Initial porosity must be above critical porosity");

        double ratio = Math.Pow((phi - phi_critical) / (phi0 - phi_critical), n);

        return k0_m2 * ratio;
    }

    /// <summary>
    /// Carmen-Kozeny with tortuosity.
    /// k = (phi^3 * d^2) / (180 * (1-phi)^2 * tau^2)
    /// </summary>
    /// <param name="phi">Porosity</param>
    /// <param name="grain_diameter_m">Mean grain diameter (m)</param>
    /// <param name="tortuosity">Tortuosity factor (typical: 1.4-2.0)</param>
    public static double CarmenKozenyWithTortuosity(double phi, double grain_diameter_m, double tortuosity = 1.5)
    {
        if (phi <= 0.0 || phi >= 1.0)
            return 0.0;

        double k = (phi * phi * phi * grain_diameter_m * grain_diameter_m) /
                   (180.0 * (1.0 - phi) * (1.0 - phi) * tortuosity * tortuosity);

        return k;
    }

    /// <summary>
    /// Tubes-in-series model (for systems with variable pore sizes).
    /// k = (phi * r_max^2) / (8 * F)
    /// where F is a formation factor
    /// </summary>
    public static double TubesInSeries(double phi, double max_pore_radius_m, double formation_factor = 3.0)
    {
        return (phi * max_pore_radius_m * max_pore_radius_m) / (8.0 * formation_factor);
    }
}
