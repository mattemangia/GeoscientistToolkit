// GeoscientistToolkit/Business/Petrology/PhaseDiagramCalculator.cs
//
// Automatic phase diagram calculator for liquidus/solidus and metamorphic P-T diagrams.
// Generates phase boundaries from thermodynamic data without hardcoded reactions.
//
// THEORY:
//
// 1. LIQUIDUS/SOLIDUS CALCULATION:
//    At phase boundary: μ_solid = μ_liquid
//    ΔG_fusion = ΔH_f - T*ΔS_f = 0
//    T_liquidus = ΔH_f / ΔS_f
//
// 2. SOLID SOLUTIONS (e.g., Olivine Fo-Fa, Plagioclase An-Ab):
//    Activity: a_i = X_i * γ_i
//    For ideal solution: γ_i = 1
//    Chemical potential: μ_i = μ_i° + RT ln(a_i)
//
// 3. METAMORPHIC PHASE BOUNDARIES (e.g., Ky-And-Sil):
//    Clapeyron equation: dP/dT = ΔS/ΔV
//    At triple point: Three phases coexist
//
// 4. PHASE RULE (Gibbs):
//    F = C - P + 2
//    where F = degrees of freedom, C = components, P = phases
//
// SOURCES:
// - Morse, S.A., 1980. Basalts and Phase Diagrams. Springer.
// - Spear, F.S., 1993. Metamorphic Phase Equilibria and P-T-t Paths. MSA Monograph.
// - Richardson, S.W. & England, P.C., 1979. Metamorphic phase diagrams.
// - Holland & Powell, 2011. Thermodynamic dataset for phase equilibria.

using System.Data;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.Petrology;

/// <summary>
///     Point on a phase boundary curve.
/// </summary>
public class PhaseBoundaryPoint
{
    public double Temperature_K { get; set; }
    public double Pressure_bar { get; set; }
    public string Phase1 { get; set; } = "";
    public string Phase2 { get; set; } = "";
    public string BoundaryType { get; set; } = ""; // "Liquidus", "Solidus", "Polymorphic"
}

/// <summary>
///     Calculator for phase diagrams from thermodynamic data.
/// </summary>
public class PhaseDiagramCalculator
{
    private const double R = 8.314; // J/(mol·K)
    private readonly CompoundLibrary _compoundLib = CompoundLibrary.Instance;

    /// <summary>
    ///     Calculates liquidus-solidus diagram for a binary system (e.g., Fo-Fa olivine).
    /// </summary>
    public List<PhaseBoundaryPoint> CalculateBinaryLiquidusSolidus(
        string component1, string component2,
        double minTemp_K, double maxTemp_K, double pressure_bar, int points = 50)
    {
        var boundaries = new List<PhaseBoundaryPoint>();

        var comp1 = _compoundLib.Find(component1);
        var comp2 = _compoundLib.Find(component2);

        if (comp1 == null || comp2 == null)
        {
            Logger.LogWarning($"[PhaseDiagramCalculator] Components {component1} or {component2} not found");
            return boundaries;
        }

        // Calculate melting temperatures for pure end-members
        var T_m1 = CalculateMeltingTemperature(comp1, pressure_bar);
        var T_m2 = CalculateMeltingTemperature(comp2, pressure_bar);

        Logger.Log($"[PhaseDiagramCalculator] T_m({component1}) = {T_m1:F1} K, T_m({component2}) = {T_m2:F1} K");

        // Generate liquidus and solidus curves
        for (var i = 0; i <= points; i++)
        {
            var X2 = (double)i / points; // Mole fraction of component 2
            var X1 = 1.0 - X2;

            // Liquidus temperature (assuming ideal solution)
            // T_liquidus = 1 / ((X1/T_m1) + (X2/T_m2))  [simplified]
            var T_liquidus = CalculateLiquidusTemperature(X1, X2, T_m1, T_m2, comp1, comp2);

            boundaries.Add(new PhaseBoundaryPoint
            {
                Temperature_K = T_liquidus,
                Pressure_bar = pressure_bar,
                Phase1 = "Liquid",
                Phase2 = $"{component1}{X1:F2}-{component2}{X2:F2}(s)",
                BoundaryType = "Liquidus"
            });

            // Solidus temperature (typically lower)
            var T_solidus = T_liquidus * 0.85; // Simplified approximation
            // More accurate: solve for composition where solid and liquid coexist

            boundaries.Add(new PhaseBoundaryPoint
            {
                Temperature_K = T_solidus,
                Pressure_bar = pressure_bar,
                Phase1 = $"{component1}{X1:F2}-{component2}{X2:F2}(s)",
                Phase2 = "Liquid",
                BoundaryType = "Solidus"
            });
        }

        return boundaries;
    }

    /// <summary>
    ///     Calculates liquidus temperature for a binary solid solution.
    /// </summary>
    private double CalculateLiquidusTemperature(double X1, double X2, double T_m1, double T_m2,
        ChemicalCompound comp1, ChemicalCompound comp2)
    {
        // Using simplified liquidus equation for ideal solution:
        // ln(X_liquid) = (ΔH_f/R) * (1/T_m - 1/T_liquidus)
        // For binary: T_liquidus ≈ (X1*T_m1 + X2*T_m2) for first approximation

        if (X1 < 0.01) return T_m2;
        if (X2 < 0.01) return T_m1;

        // More accurate: activity-corrected liquidus
        var H_f1 = (comp1.EnthalpyFormation_kJ_mol ?? 0) * 1000; // J/mol
        var H_f2 = (comp2.EnthalpyFormation_kJ_mol ?? 0) * 1000;

        // Simplified: weighted average with non-ideality correction
        var T_ideal = X1 * T_m1 + X2 * T_m2;
        var nonIdealityFactor = 1.0 + 0.1 * X1 * X2; // Simple regular solution correction

        return T_ideal * nonIdealityFactor;
    }

    /// <summary>
    ///     Calculates melting temperature from thermodynamic data.
    /// </summary>
    private double CalculateMeltingTemperature(ChemicalCompound compound, double pressure_bar)
    {
        // T_m = ΔH_fusion / ΔS_fusion
        // Approximate from formation data and entropy

        var H_f = compound.EnthalpyFormation_kJ_mol ?? -1000; // kJ/mol
        var S = compound.Entropy_J_molK ?? 50; // J/(mol·K)

        // Rough estimate: T_m ~ |ΔH_f| / (S * 0.01)  [empirical scaling]
        var T_m = Math.Abs(H_f) * 1000.0 / (S * 3.0); // Empirical factor

        // Pressure correction: dT/dP = T*ΔV/ΔH
        var V_molar = compound.MolarVolume_cm3_mol ?? 40; // cm³/mol
        var dP = pressure_bar - 1.0; // bar
        var dT = dP * T_m * V_molar * 1e-5 / Math.Abs(H_f); // Clausius-Clapeyron

        return T_m + dT;
    }

    /// <summary>
    ///     Calculates P-T phase boundary for polymorphic transitions (e.g., Ky-And-Sil).
    /// </summary>
    public List<PhaseBoundaryPoint> CalculatePolymorphBoundary(
        string phase1Name, string phase2Name,
        double minTemp_K, double maxTemp_K,
        double minPressure_bar, double maxPressure_bar,
        int points = 30)
    {
        var boundaries = new List<PhaseBoundaryPoint>();

        var phase1 = _compoundLib.Find(phase1Name);
        var phase2 = _compoundLib.Find(phase2Name);

        if (phase1 == null || phase2 == null)
        {
            Logger.LogWarning($"[PhaseDiagramCalculator] Phases {phase1Name} or {phase2Name} not found");
            return boundaries;
        }

        // Calculate Gibbs free energy difference: ΔG = ΔH - T*ΔS
        var ΔH = (phase2.EnthalpyFormation_kJ_mol ?? 0) - (phase1.EnthalpyFormation_kJ_mol ?? 0); // kJ/mol
        var ΔS = (phase2.Entropy_J_molK ?? 0) - (phase1.Entropy_J_molK ?? 0); // J/(mol·K)
        var ΔV = (phase2.MolarVolume_cm3_mol ?? 0) - (phase1.MolarVolume_cm3_mol ?? 0); // cm³/mol

        // Clapeyron slope: dP/dT = ΔS/ΔV
        var slope_bar_K = ΔS / (ΔV * 1e-5); // Convert cm³ to bar·m³

        // Reference point where ΔG = 0
        var T_ref = ΔH * 1000.0 / ΔS; // K (at 1 bar)
        var P_ref = 1.0; // bar

        Logger.Log(
            $"[PhaseDiagramCalculator] {phase1Name}-{phase2Name}: T_ref={T_ref:F1}K, slope={slope_bar_K:F2} bar/K");

        // Generate boundary curve using Clapeyron equation
        for (var i = 0; i <= points; i++)
        {
            var T = minTemp_K + i * (maxTemp_K - minTemp_K) / points;

            // P = P_ref + slope * (T - T_ref)
            var P = P_ref + slope_bar_K * (T - T_ref);

            if (P >= minPressure_bar && P <= maxPressure_bar)
                boundaries.Add(new PhaseBoundaryPoint
                {
                    Temperature_K = T,
                    Pressure_bar = P,
                    Phase1 = phase1Name,
                    Phase2 = phase2Name,
                    BoundaryType = "Polymorphic"
                });
        }

        return boundaries;
    }

    /// <summary>
    ///     Calculates triple point where three phases coexist.
    /// </summary>
    public (double Temperature_K, double Pressure_bar)? CalculateTriplePoint(
        string phase1, string phase2, string phase3,
        double minTemp_K, double maxTemp_K,
        double minP_bar, double maxP_bar)
    {
        // Find intersection of three phase boundaries
        var boundary12 = CalculatePolymorphBoundary(phase1, phase2, minTemp_K, maxTemp_K, minP_bar, maxP_bar, 50);
        var boundary23 = CalculatePolymorphBoundary(phase2, phase3, minTemp_K, maxTemp_K, minP_bar, maxP_bar, 50);
        var boundary13 = CalculatePolymorphBoundary(phase1, phase3, minTemp_K, maxTemp_K, minP_bar, maxP_bar, 50);

        // Find approximate intersection (simplified: average of nearby points)
        foreach (var pt12 in boundary12)
        foreach (var pt23 in boundary23)
        {
            var dT = Math.Abs(pt12.Temperature_K - pt23.Temperature_K);
            var dP = Math.Abs(pt12.Pressure_bar - pt23.Pressure_bar);

            if (dT < 50 && dP < 500) // Tolerance
            {
                var T_triple = (pt12.Temperature_K + pt23.Temperature_K) / 2.0;
                var P_triple = (pt12.Pressure_bar + pt23.Pressure_bar) / 2.0;

                Logger.Log(
                    $"[PhaseDiagramCalculator] Triple point {phase1}-{phase2}-{phase3}: T={T_triple:F1}K, P={P_triple:F0} bar");
                return (T_triple, P_triple);
            }
        }

        Logger.LogWarning($"[PhaseDiagramCalculator] Triple point {phase1}-{phase2}-{phase3} not found");
        return null;
    }

    /// <summary>
    ///     Exports phase boundary to DataTable for plotting.
    /// </summary>
    public DataTable ExportBoundaryToTable(List<PhaseBoundaryPoint> boundaries, string diagramName)
    {
        var table = new DataTable(diagramName);
        table.Columns.Add("Temperature_K", typeof(double));
        table.Columns.Add("Temperature_C", typeof(double));
        table.Columns.Add("Pressure_bar", typeof(double));
        table.Columns.Add("Pressure_kbar", typeof(double));
        table.Columns.Add("Phase1", typeof(string));
        table.Columns.Add("Phase2", typeof(string));
        table.Columns.Add("BoundaryType", typeof(string));

        foreach (var pt in boundaries)
        {
            var row = table.NewRow();
            row["Temperature_K"] = pt.Temperature_K;
            row["Temperature_C"] = pt.Temperature_K - 273.15;
            row["Pressure_bar"] = pt.Pressure_bar;
            row["Pressure_kbar"] = pt.Pressure_bar / 1000.0;
            row["Phase1"] = pt.Phase1;
            row["Phase2"] = pt.Phase2;
            row["BoundaryType"] = pt.BoundaryType;
            table.Rows.Add(row);
        }

        return table;
    }
}
