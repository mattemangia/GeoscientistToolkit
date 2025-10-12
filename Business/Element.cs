// GeoscientistToolkit/Data/Materials/Element.cs

namespace GeoscientistToolkit.Data.Materials;

/// <summary>
///     Defines the type or category of a chemical element.
/// </summary>
public enum ElementType
{
    Nonmetal,
    NobleGas,
    AlkaliMetal,
    AlkalineEarthMetal,
    Metalloid,
    Halogen,
    PostTransitionMetal,
    TransitionMetal,
    Lanthanide,
    Actinide
}

/// <summary>
///     Represents a chemical element with its fundamental atomic, physical, and chemical properties.
/// </summary>
public sealed class Element
{
    /// <summary>Element's common name (e.g., "Hydrogen").</summary>
    public string Name { get; set; } = "Unknown";

    /// <summary>Standard chemical symbol (e.g., "H").</summary>
    public string Symbol { get; set; } = "?";

    /// <summary>The number of protons in the nucleus.</summary>
    public int AtomicNumber { get; set; }

    /// <summary>The weighted average mass of an element's isotopes (in atomic mass units, u).</summary>
    public double AtomicMass { get; set; }

    /// <summary>The category of the element (e.g., AlkaliMetal, Nonmetal).</summary>
    public ElementType ElementType { get; set; } = ElementType.Nonmetal;

    /// <summary>The group number in the periodic table (1-18).</summary>
    public int Group { get; set; }

    /// <summary>The period number in the periodic table (1-7).</summary>
    public int Period { get; set; }

    // --- Atomic & Electronic Properties ---

    /// <summary>The element's ground-state electron configuration.</summary>
    public string ElectronConfiguration { get; set; } = "";

    /// <summary>The number of electrons in the outermost shell.</summary>
    public int? ValenceElectrons { get; set; }

    /// <summary>Electronegativity on the Pauling scale.</summary>
    public double? Electronegativity { get; set; }

    /// <summary>Common oxidation states the element can assume.</summary>
    public List<int> OxidationStates { get; set; } = new();

    /// <summary>Energy required to remove one electron from a neutral atom (kJ/mol).</summary>
    public double? FirstIonizationEnergy_kJ_mol { get; set; }

    /// <summary>Energy change when an electron is added to a neutral atom (kJ/mol).</summary>
    public double? ElectronAffinity_kJ_mol { get; set; }

    // --- Radii ---

    /// <summary>Calculated atomic radius (picometers).</summary>
    public int? AtomicRadius_pm { get; set; }

    /// <summary>Radius of an atom forming a covalent bond (picometers).</summary>
    public int? CovalentRadius_pm { get; set; }

    /// <summary>
    ///     Radius of an imaginary hard sphere representing the distance of closest approach for another atom
    ///     (picometers).
    /// </summary>
    public int? VanDerWaalsRadius_pm { get; set; }

    /// <summary>Radii for different ionic charges (picometers). Key is charge, value is radius.</summary>
    public Dictionary<int, int> IonicRadii { get; set; } = new();

    // --- Physical Properties ---

    /// <summary>Melting point at standard pressure (Kelvin).</summary>
    public double? MeltingPoint_K { get; set; }

    /// <summary>Boiling point at standard pressure (Kelvin).</summary>
    public double? BoilingPoint_K { get; set; }

    /// <summary>Density at standard conditions (g/cm³).</summary>
    public double? Density_g_cm3 { get; set; }

    /// <summary>Thermal conductivity at 300 K (W/m·K).</summary>
    public double? ThermalConductivity_W_mK { get; set; }

    // --- Abundance ---

    /// <summary>Abundance in Earth's crust (parts per million by weight).</summary>
    public double? CrustalAbundance_ppm { get; set; }

    /// <summary>Concentration in seawater (mg/L).</summary>
    public double? SeawaterConcentration_mg_L { get; set; }

    // --- Metadata ---

    /// <summary>Literature or data sources for the element's properties.</summary>
    public List<string> Sources { get; set; } = new();
}