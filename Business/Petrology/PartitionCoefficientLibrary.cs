// GeoscientistToolkit/Business/Petrology/PartitionCoefficientLibrary.cs
//
// Library of partition coefficients (Kd) for trace elements between minerals and melt.
// Used for modeling fractional crystallization and trace element evolution in magmas.
//
// THEORY:
// Partition coefficient: Kd = C_mineral / C_melt
// - Kd > 1: Compatible element (concentrates in solid)
// - Kd < 1: Incompatible element (stays in melt)
//
// Rayleigh fractionation equation:
// C_L = C_0 * F^(D-1)
// where:
//   C_L = concentration in liquid
//   C_0 = initial concentration
//   F = fraction of melt remaining
//   D = bulk partition coefficient = Σ(Xi * Kdi)
//
// SOURCES:
// - Rollinson, H., 1993. Using Geochemical Data. Longman.
// - Winter, J.D., 2013. Principles of Igneous and Metamorphic Petrology, 2nd ed.
// - Henderson, P., 1982. Inorganic Geochemistry. Pergamon Press.
// - Philpotts, A.R. & Ague, J.J., 2009. Principles of Igneous and Metamorphic Petrology.

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.Petrology;

/// <summary>
///     Represents a partition coefficient for a trace element in a mineral-melt system.
/// </summary>
public class PartitionCoefficient
{
    public string Element { get; set; } = "";
    public string Mineral { get; set; } = "";
    public string MagmaType { get; set; } = "Basaltic"; // Basaltic, Andesitic, Rhyolitic
    public double Kd { get; set; }
    public double? Temperature_C { get; set; }
    public string Source { get; set; } = "";
    public string Notes { get; set; } = "";
}

/// <summary>
///     Singleton library of partition coefficients for trace element modeling.
/// </summary>
public sealed class PartitionCoefficientLibrary
{
    private static readonly Lazy<PartitionCoefficientLibrary> _lazy = new(() => new PartitionCoefficientLibrary());
    private readonly List<PartitionCoefficient> _coefficients = new();

    private PartitionCoefficientLibrary()
    {
        SeedPartitionCoefficients();
    }

    public static PartitionCoefficientLibrary Instance => _lazy.Value;
    public IReadOnlyList<PartitionCoefficient> Coefficients => _coefficients;

    /// <summary>
    ///     Gets partition coefficient for a specific element-mineral-magma combination.
    /// </summary>
    public double? GetKd(string element, string mineral, string magmaType = "Basaltic")
    {
        var match = _coefficients.FirstOrDefault(c =>
            c.Element.Equals(element, StringComparison.OrdinalIgnoreCase) &&
            c.Mineral.Equals(mineral, StringComparison.OrdinalIgnoreCase) &&
            c.MagmaType.Equals(magmaType, StringComparison.OrdinalIgnoreCase));

        return match?.Kd;
    }

    /// <summary>
    ///     Gets all partition coefficients for a specific element.
    /// </summary>
    public IEnumerable<PartitionCoefficient> GetForElement(string element)
    {
        return _coefficients.Where(c => c.Element.Equals(element, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Gets all partition coefficients for a specific mineral.
    /// </summary>
    public IEnumerable<PartitionCoefficient> GetForMineral(string mineral)
    {
        return _coefficients.Where(c => c.Mineral.Equals(mineral, StringComparison.OrdinalIgnoreCase));
    }

    private void SeedPartitionCoefficients()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // OLIVINE PARTITION COEFFICIENTS (Basaltic)
        // ═══════════════════════════════════════════════════════════════════════

        // Highly compatible elements in olivine
        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ni", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 10.0,
            Source = "Rollinson (1993)", Notes = "Highly compatible in olivine"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Co", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 5.0,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Cr", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 1.2,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Mg", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 3.5,
            Source = "Winter (2013)", Notes = "Major element"
        });

        // Moderately compatible
        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Mn", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 1.5,
            Source = "Rollinson (1993)"
        });

        // Incompatible elements in olivine
        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Rb", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 0.001,
            Source = "Henderson (1982)", Notes = "Highly incompatible"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ba", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 0.001,
            Source = "Henderson (1982)", Notes = "Highly incompatible"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "K", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 0.001,
            Source = "Rollinson (1993)", Notes = "Highly incompatible"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "La", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 0.002,
            Source = "Henderson (1982)", Notes = "Light REE, incompatible"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ce", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 0.002,
            Source = "Henderson (1982)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Yb", Mineral = "Olivine", MagmaType = "Basaltic", Kd = 0.01,
            Source = "Henderson (1982)", Notes = "Heavy REE"
        });

        // ═══════════════════════════════════════════════════════════════════════
        // CLINOPYROXENE PARTITION COEFFICIENTS (Basaltic)
        // ═══════════════════════════════════════════════════════════════════════

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ni", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 6.0,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Cr", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 7.0,
            Source = "Rollinson (1993)", Notes = "Compatible in Cpx"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Co", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 2.0,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Sc", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 3.0,
            Source = "Rollinson (1993)", Notes = "Compatible in Cpx"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "V", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 2.5,
            Source = "Rollinson (1993)"
        });

        // Heavy REE are more compatible in Cpx than light REE
        _coefficients.Add(new PartitionCoefficient
        {
            Element = "La", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 0.05,
            Source = "Henderson (1982)", Notes = "LREE incompatible"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ce", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 0.08,
            Source = "Henderson (1982)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Sm", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 0.25,
            Source = "Henderson (1982)", Notes = "Middle REE"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Yb", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 0.5,
            Source = "Henderson (1982)", Notes = "HREE more compatible"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Rb", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 0.001,
            Source = "Rollinson (1993)", Notes = "Highly incompatible"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Sr", Mineral = "Clinopyroxene", MagmaType = "Basaltic", Kd = 0.1,
            Source = "Rollinson (1993)"
        });

        // ═══════════════════════════════════════════════════════════════════════
        // PLAGIOCLASE PARTITION COEFFICIENTS (Basaltic)
        // ═══════════════════════════════════════════════════════════════════════

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Sr", Mineral = "Plagioclase", MagmaType = "Basaltic", Kd = 2.0,
            Source = "Rollinson (1993)", Notes = "Compatible in plagioclase (substitutes for Ca)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ba", Mineral = "Plagioclase", MagmaType = "Basaltic", Kd = 0.3,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Eu", Mineral = "Plagioclase", MagmaType = "Basaltic", Kd = 1.5,
            Source = "Henderson (1982)", Notes = "Eu2+ substitutes for Ca2+, positive Eu anomaly"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "La", Mineral = "Plagioclase", MagmaType = "Basaltic", Kd = 0.2,
            Source = "Henderson (1982)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ce", Mineral = "Plagioclase", MagmaType = "Basaltic", Kd = 0.15,
            Source = "Henderson (1982)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Yb", Mineral = "Plagioclase", MagmaType = "Basaltic", Kd = 0.05,
            Source = "Henderson (1982)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Rb", Mineral = "Plagioclase", MagmaType = "Basaltic", Kd = 0.07,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "K", Mineral = "Plagioclase", MagmaType = "Basaltic", Kd = 0.18,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ni", Mineral = "Plagioclase", MagmaType = "Basaltic", Kd = 0.01,
            Source = "Rollinson (1993)", Notes = "Incompatible"
        });

        // ═══════════════════════════════════════════════════════════════════════
        // ORTHOPYROXENE PARTITION COEFFICIENTS (Basaltic)
        // ═══════════════════════════════════════════════════════════════════════

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ni", Mineral = "Orthopyroxene", MagmaType = "Basaltic", Kd = 5.0,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Cr", Mineral = "Orthopyroxene", MagmaType = "Basaltic", Kd = 5.0,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Co", Mineral = "Orthopyroxene", MagmaType = "Basaltic", Kd = 2.5,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "La", Mineral = "Orthopyroxene", MagmaType = "Basaltic", Kd = 0.005,
            Source = "Henderson (1982)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Yb", Mineral = "Orthopyroxene", MagmaType = "Basaltic", Kd = 0.3,
            Source = "Henderson (1982)"
        });

        // ═══════════════════════════════════════════════════════════════════════
        // MAGNETITE PARTITION COEFFICIENTS (Basaltic)
        // ═══════════════════════════════════════════════════════════════════════

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ni", Mineral = "Magnetite", MagmaType = "Basaltic", Kd = 25.0,
            Source = "Rollinson (1993)", Notes = "Highly compatible"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "V", Mineral = "Magnetite", MagmaType = "Basaltic", Kd = 15.0,
            Source = "Rollinson (1993)", Notes = "Highly compatible"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Cr", Mineral = "Magnetite", MagmaType = "Basaltic", Kd = 10.0,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ti", Mineral = "Magnetite", MagmaType = "Basaltic", Kd = 5.0,
            Source = "Rollinson (1993)", Notes = "Ti often in magnetite"
        });

        // ═══════════════════════════════════════════════════════════════════════
        // K-FELDSPAR / SANIDINE PARTITION COEFFICIENTS (Rhyolitic)
        // ═══════════════════════════════════════════════════════════════════════

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Rb", Mineral = "K-Feldspar", MagmaType = "Rhyolitic", Kd = 0.5,
            Source = "Rollinson (1993)", Notes = "Rb substitutes for K"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ba", Mineral = "K-Feldspar", MagmaType = "Rhyolitic", Kd = 5.0,
            Source = "Rollinson (1993)", Notes = "Compatible in K-feldspar"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Sr", Mineral = "K-Feldspar", MagmaType = "Rhyolitic", Kd = 8.0,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Eu", Mineral = "K-Feldspar", MagmaType = "Rhyolitic", Kd = 10.0,
            Source = "Henderson (1982)", Notes = "Strong Eu anomaly"
        });

        // ═══════════════════════════════════════════════════════════════════════
        // BIOTITE PARTITION COEFFICIENTS (Intermediate-Felsic)
        // ═══════════════════════════════════════════════════════════════════════

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Rb", Mineral = "Biotite", MagmaType = "Rhyolitic", Kd = 3.0,
            Source = "Rollinson (1993)", Notes = "Highly compatible in biotite"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "Ba", Mineral = "Biotite", MagmaType = "Rhyolitic", Kd = 6.0,
            Source = "Rollinson (1993)"
        });

        _coefficients.Add(new PartitionCoefficient
        {
            Element = "K", Mineral = "Biotite", MagmaType = "Rhyolitic", Kd = 4.0,
            Source = "Rollinson (1993)"
        });

        Logger.Log($"[PartitionCoefficientLibrary] Seeded {_coefficients.Count} partition coefficients");
    }
}
