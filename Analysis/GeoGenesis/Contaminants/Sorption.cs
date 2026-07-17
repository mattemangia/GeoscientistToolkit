// GAIA.GeoGenesis/Contaminants/Sorption.cs
//
// Soil / sediment sorption of dissolved contaminants — the partitioning of a solute between the
// aqueous phase and the solid matrix. Three standard equilibrium isotherms are supported, each
// giving the sorbed mass q (mg per kg of solid) as a function of the aqueous concentration C (mg/L):
//
//   • Linear      q = Kd · C
//   • Freundlich  q = Kf · C^(1/n)
//   • Langmuir    q = qmax · KL · C / (1 + KL · C)
//
// The transport retardation factor follows from the local slope of the isotherm:
//
//   R = 1 + (ρ_b / θ) · dq/dC
//
// so a sorbing solute migrates R times slower than the groundwater (Freeze & Cherry, 1979,
// "Groundwater"; Fetter, 1999, "Contaminant Hydrogeology", 2nd ed.; EPA/600/R-99/004a Kd reports).

namespace GAIA.GeoGenesis.Contaminants;

public enum SorptionIsotherm { None, Linear, Freundlich, Langmuir }

/// <summary>An equilibrium sorption isotherm plus the soil properties needed for retardation.</summary>
public sealed class SorptionModel
{
    public SorptionIsotherm Isotherm { get; set; } = SorptionIsotherm.None;

    /// <summary>Linear distribution coefficient Kd (L/kg).</summary>
    public double Kd_L_kg { get; set; }

    /// <summary>Freundlich capacity Kf ((mg/kg)/(mg/L)^(1/n)) and exponent n (dimensionless).</summary>
    public double FreundlichKf { get; set; }
    public double FreundlichN { get; set; } = 1.0;

    /// <summary>Langmuir maximum sorbed mass qmax (mg/kg) and affinity KL (L/mg).</summary>
    public double LangmuirQmax_mg_kg { get; set; }
    public double LangmuirKL_L_mg { get; set; }

    /// <summary>Dry bulk density of the soil/aquifer matrix (kg/L). Typical sand ≈ 1.6.</summary>
    public double BulkDensity_kg_L { get; set; } = 1.6;

    /// <summary>Effective (kinematic) porosity (fraction). Typical sand ≈ 0.3.</summary>
    public double Porosity { get; set; } = 0.3;

    /// <summary>Sorbed concentration q (mg/kg) at aqueous concentration <paramref name="c"/> (mg/L).</summary>
    public double SorbedConcentration(double c)
    {
        if (c <= 0) return 0;
        return Isotherm switch
        {
            SorptionIsotherm.Linear => Kd_L_kg * c,
            SorptionIsotherm.Freundlich => FreundlichKf * Math.Pow(c, 1.0 / Math.Max(FreundlichN, 1e-9)),
            SorptionIsotherm.Langmuir => LangmuirQmax_mg_kg * LangmuirKL_L_mg * c / (1.0 + LangmuirKL_L_mg * c),
            _ => 0.0
        };
    }

    /// <summary>Local isotherm slope dq/dC (L/kg) at aqueous concentration <paramref name="c"/>.</summary>
    public double Slope(double c)
    {
        return Isotherm switch
        {
            SorptionIsotherm.Linear => Kd_L_kg,
            SorptionIsotherm.Freundlich => c <= 0 ? FreundlichKf // limiting slope is ∞ as C→0; clamp to Kf
                : FreundlichKf / Math.Max(FreundlichN, 1e-9) * Math.Pow(c, 1.0 / Math.Max(FreundlichN, 1e-9) - 1.0),
            SorptionIsotherm.Langmuir => LangmuirQmax_mg_kg * LangmuirKL_L_mg / Math.Pow(1.0 + LangmuirKL_L_mg * c, 2),
            _ => 0.0
        };
    }

    /// <summary>
    ///     Retardation factor R = 1 + (ρ_b/θ)·dq/dC (dimensionless, ≥ 1). The contaminant front
    ///     advances R times slower than the average linear groundwater velocity.
    /// </summary>
    public double RetardationFactor(double c)
    {
        if (Isotherm == SorptionIsotherm.None) return 1.0;
        var theta = Math.Max(Porosity, 1e-6);
        var r = 1.0 + BulkDensity_kg_L / theta * Math.Max(0.0, Slope(c));
        return Math.Max(1.0, r);
    }

    /// <summary>Fraction of total contaminant mass that remains dissolved (mobile) at concentration C.</summary>
    public double DissolvedMassFraction(double c)
    {
        // Total = θ·C (aqueous) + ρ_b·q (sorbed), per bulk volume. Dissolved fraction = θC/(θC+ρ_b q).
        if (Isotherm == SorptionIsotherm.None || c <= 0) return 1.0;
        var aqueous = Porosity * c;
        var sorbed = BulkDensity_kg_L * SorbedConcentration(c);
        var total = aqueous + sorbed;
        return total > 0 ? aqueous / total : 1.0;
    }

    public static SorptionModel NoSorption() => new() { Isotherm = SorptionIsotherm.None };
}
