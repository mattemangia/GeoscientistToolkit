// GAIA.GeoGenesis/Thermodynamics/MineralKinetics.cs
//
// Per-mineral kinetic limits for equilibrium scaling/clogging screening. Carbonates and sulphates
// (calcite, gypsum, anhydrite, halite, …) precipitate fast enough that equilibrium screening is
// acceptable, but silicate precipitation (quartz, amorphous silica) is kinetically limited and the
// equilibrium assumption grossly over-estimates scaling at low temperature. This helper caps the
// equilibrium precipitation by the fraction of equilibrium that the Lasaga/Arrhenius rate law can
// actually reach over a node residence time, reusing the rate-law structure of KineticsSolver.
//
// Sources:
// - Rimstidt & Barnes (1980), Geochim. Cosmochim. Acta 44, 1683-1699 (quartz kinetics).
// - Palandri & Kharaka (2004), USGS Open-File Report 2004-1068 (rate-parameter compilation).
// - Gunnarsson & Arnorsson (2005), Geothermics 34, 320-329, doi:10.1016/j.geothermics.2005.02.002 (amorphous silica ppt).
// - Steefel & Lasaga (1994), Am. J. Sci. 294, 529-592 (rate law: r = A_s·k(T)·|1-Ω^p|^q).
// Screening-grade: the surface-area term is a representative constant, not a calibrated value.

using GAIA.GeoGenesis.Materials;

namespace GAIA.GeoGenesis.Thermodynamics;

/// <summary>
/// Per-mineral kinetic cap for equilibrium scaling. Provides a [0,1] fraction of the equilibrium
/// precipitation extent that kinetics actually reaches over a residence time, so kinetically-limited
/// silicates (quartz, amorphous silica) are not grossly over-estimated at low temperature.
/// </summary>
public static class MineralKinetics
{
    private const double R = 8.314462618; // J/(mol·K)
    private const double T0 = 298.15;     // reference temperature (K) for k25 rate constants

    // Representative reactive surface area per kg of water available for a forming scale (m²/kgw).
    // Screening constant: real systems vary by orders of magnitude; this mainly sets WHERE the
    // kinetic cap begins to bite, while the Arrhenius T-dependence sets its shape.
    public const double RepresentativeSurfaceArea_m2_per_kgw = 1.0;
    public const double DefaultResidenceTime_s = 3600.0;

    // Minerals whose precipitation is kinetically limited (slow) vs equilibrium-controlled (fast).
    // Carbonates/sulphates/halides precipitate rapidly → equilibrium screening is valid.
    private static readonly HashSet<string> KineticallyLimited = new(StringComparer.OrdinalIgnoreCase)
    {
        "Quartz", "Amorphous Silica", "Chalcedony", "Opal", "Cristobalite", "Tridymite", "Silica",
        "K-Feldspar", "Albite", "Anorthite", "Microcline", "Orthoclase", "Plagioclase",
        "Muscovite", "Biotite", "Chlorite", "Smectite", "Illite"
    };

    /// <summary>True for kinetically-limited (slow) minerals that need a kinetic cap on equilibrium.</summary>
    public static bool IsKineticallyLimited(string mineral)
        => KineticallyLimited.Contains(mineral);

    /// <summary>
    /// Fraction of the equilibrium precipitation extent (0–1) that kinetics actually reaches over a
    /// residence time <paramref name="dtSeconds"/> at temperature <paramref name="temperatureK"/>.
    /// Returns 1.0 for equilibrium-controlled (fast) minerals and for minerals without kinetic data
    /// (conservative: keeps the equilibrium upper bound). For kinetically-limited minerals the
    /// fraction follows a first-order relaxation toward equilibrium, 1 − exp(−Da), with Damköhler
    /// number Da = A_s·k(T)·dt using the Arrhenius rate constant k(T) = k25·exp[−Ea/R·(1/T−1/T0)].
    /// </summary>
    public static double EquilibriumFractionReached(
        CompoundLibrary library, string mineral, double temperatureK, double dtSeconds)
    {
        if (!IsKineticallyLimited(mineral) || dtSeconds <= 0.0)
            return 1.0;

        var compound = library.Find(mineral);
        // Prefer precipitation parameters; fall back to dissolution (principle of microscopic
        // reversibility — order-of-magnitude proxy). Amorphous silica carries none in the library,
        // so use the Gunnarsson & Arnórsson (2005) fast-silica defaults.
        var k25 = compound?.RateConstant_Precipitation_mol_m2_s
                  ?? compound?.RateConstant_Dissolution_mol_m2_s;
        var ea = compound?.ActivationEnergy_Precipitation_kJ_mol
                 ?? compound?.ActivationEnergy_Dissolution_kJ_mol;

        if ((k25 == null || ea == null) && TryDefaultSilicaKinetics(mineral, out var defaultK25, out var defaultEa))
        {
            k25 ??= defaultK25;
            ea ??= defaultEa;
        }

        if (k25 is not > 0.0 || ea is not > 0.0)
            return 1.0;

        var tK = Math.Max(273.15, temperatureK);
        var kT = k25.Value * Math.Exp(-ea.Value * 1000.0 / R * (1.0 / tK - 1.0 / T0));
        var damkohler = RepresentativeSurfaceArea_m2_per_kgw * kT * dtSeconds;
        return Math.Clamp(1.0 - Math.Exp(-damkohler), 0.0, 1.0);
    }

    // Default precipitation rate constant for silica polymorphs missing kinetic data (mol/m²/s at 25°C).
    // Amorphous silica precipitates far faster than quartz; Gunnarsson & Arnórsson (2005) report
    // near-equilibrium on the order of hours above ~150 °C. log k ≈ −7.5.
    private const double AmorphousSilica_k25 = 3.0e-8;
    private const double AmorphousSilica_Ea_kJ_mol = 60.0;

    private static bool TryDefaultSilicaKinetics(string mineral, out double k25, out double ea)
    {
        if (mineral.Equals("Amorphous Silica", StringComparison.OrdinalIgnoreCase))
        {
            k25 = AmorphousSilica_k25;
            ea = AmorphousSilica_Ea_kJ_mol;
            return true;
        }

        if (mineral.Equals("Silica", StringComparison.OrdinalIgnoreCase)
            || mineral.Equals("Chalcedony", StringComparison.OrdinalIgnoreCase)
            || mineral.Equals("Opal", StringComparison.OrdinalIgnoreCase)
            || mineral.Equals("Cristobalite", StringComparison.OrdinalIgnoreCase)
            || mineral.Equals("Tridymite", StringComparison.OrdinalIgnoreCase))
        {
            k25 = 1.26e-10;
            ea = AmorphousSilica_Ea_kJ_mol;
            return true;
        }

        k25 = 0.0;
        ea = 0.0;
        return false;
    }
}
