// GeoscientistToolkit/Data/Materials/CompoundLibrary.cs
//
// Singleton service for thermodynamic compound properties used in dissolution/precipitation calculations.
// Provides comprehensive database of minerals, salts, and aqueous species relevant to petrophysics.
//
// SOURCES (see per-compound citations in SeedDefaults()):
// - Robie & Hemingway (1995): Thermodynamic Properties of Minerals and Related Substances at 298.15 K and 1 Bar
// - NIST Chemistry WebBook (webbook.nist.gov)
// - Parkhurst & Appelo (2013): PHREEQC database
// - Nordstrom & Munoz (1994): Geochemical Thermodynamics
// - Handbook of Mineralogy (mindat.org, rruff.info)
// - Stumm & Morgan (1996): Aquatic Chemistry
// - Holland & Powell (2011): Updated thermodynamic dataset
//

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Materials;

public enum CrystalSystem
{
    Triclinic,
    Monoclinic,
    Orthorhombic,
    Tetragonal,
    Trigonal,
    Hexagonal,
    Cubic,
    Amorphous
}

public enum CompoundPhase
{
    Solid,
    Aqueous,
    Gas,
    Liquid,
    Surface // ENHANCEMENT: For surface complexation species
}

// ENHANCEMENT: Add classes for solid solution modeling
public enum SolidSolutionMixingModel
{
    Ideal,
    Regular
}

public class SolidSolution
{
    public string Name { get; set; } = string.Empty;
    public List<string> EndMembers { get; set; } = new();

    public SolidSolutionMixingModel MixingModel { get; set; } = SolidSolutionMixingModel.Ideal;

    // Interaction parameters (W) for regular solution model (in kJ/mol)
    public List<double> InteractionParameters { get; set; } = new();
}

/// <summary>
///     Represents a chemical compound with comprehensive thermodynamic and physical properties
///     for dissolution/precipitation modeling in petrophysics applications.
/// </summary>
public sealed class ChemicalCompound
{
    
    public string Name { get; set; } = "Unnamed";
    public string ChemicalFormula { get; set; } = "";
    public CompoundPhase Phase { get; set; } = CompoundPhase.Solid;
    public CrystalSystem? CrystalSystem { get; set; } // Only for solid phases

    // --- Core Thermodynamic Properties (Standard State: 298.15 K, 1 bar) ---

    /// <summary>Standard Gibbs free energy of formation (kJ/mol)</summary>
    public double? GibbsFreeEnergyFormation_kJ_mol { get; set; }

    public double? SetchenowCoefficient { get; set; }

    /// <summary>Standard enthalpy of formation (kJ/mol)</summary>
    public double? EnthalpyFormation_kJ_mol { get; set; }

    /// <summary>Standard entropy (J/mol·K)</summary>
    public double? Entropy_J_molK { get; set; }
    
    /// <summary>Acid dissociation constant (negative log)</summary>
    public double? pKa { get; set; }
    
    /// <summary>Base dissociation constant (negative log)</summary>
    public double? pKb { get; set; }
    
    /// <summary>Enthalpy of dissociation for acids/bases (kJ/mol)</summary>
    public double? DissociationEnthalpy_kJ_mol { get; set; }
    
    /// <summary>Henry's law constant (mol/(L·atm)) at 25°C</summary>
    public double? HenryConstant_mol_L_atm { get; set; }

    /// <summary>Heat capacity at constant pressure (J/mol·K)</summary>
    public double? HeatCapacity_J_molK { get; set; }

    /// <summary>Molar volume (cm³/mol)</summary>
    public double? MolarVolume_cm3_mol { get; set; }

    /// <summary>Molecular weight (g/mol)</summary>
    public double? MolecularWeight_g_mol { get; set; }

    /// <summary>Density (g/cm³) - for solids</summary>
    public double? Density_g_cm3 { get; set; }

    // --- Solubility & Equilibrium Properties ---

    /// <summary>Solubility product constant (Ksp) at 25°C - log10(Ksp)</summary>
    public double? LogKsp_25C { get; set; }

    /// <summary>Solubility in water (g/100mL) at 25°C</summary>
    public double? Solubility_g_100mL_25C { get; set; }

    /// <summary>Dissolution enthalpy (kJ/mol) - heat absorbed during dissolution</summary>
    public double? DissolutionEnthalpy_kJ_mol { get; set; }

    // --- Kinetic Parameters for Dissolution/Precipitation ---

    /// <summary>Activation energy for dissolution (kJ/mol)</summary>
    public double? ActivationEnergy_Dissolution_kJ_mol { get; set; }

    /// <summary>Activation energy for precipitation (kJ/mol)</summary>
    public double? ActivationEnergy_Precipitation_kJ_mol { get; set; }

    /// <summary>Pre-exponential factor for dissolution rate (mol/m²/s)</summary>
    public double? RateConstant_Dissolution_mol_m2_s { get; set; }

    /// <summary>Pre-exponential factor for precipitation rate (mol/m²/s)</summary>
    public double? RateConstant_Precipitation_mol_m2_s { get; set; }

    /// <summary>Reaction order for dissolution (dimensionless)</summary>
    public double? ReactionOrder_Dissolution { get; set; }

    /// <summary>Specific surface area (m²/g) - typical for reactive transport</summary>
    public double? SpecificSurfaceArea_m2_g { get; set; }

    // ENHANCEMENT: Additional kinetic parameters from Steefel & Lasaga (1994)
    /// <summary>Order of the acid catalysis term in the rate law</summary>
    public double? AcidCatalysisOrder { get; set; }

    /// <summary>Order of the base (OH-) catalysis term in the rate law</summary>
    public double? BaseCatalysisOrder { get; set; }

    /// <summary>Exponent 'p' in the thermodynamic driving force term [1 - Ω^p]^q</summary>
    public double? ReactionOrder_p { get; set; }

    /// <summary>Exponent 'q' in the thermodynamic driving force term [1 - Ω^p]^q</summary>
    public double? ReactionOrder_q { get; set; }


    // --- Temperature-Dependent Parameters ---

    /// <summary>Heat capacity polynomial coefficients: Cp = a + bT + cT² + dT⁻²</summary>
    public double[]? HeatCapacityPolynomial_a_b_c_d { get; set; }

    /// <summary>Temperature range validity (K) [min, max]</summary>
    public double[]? TemperatureRange_K { get; set; }

    // --- Aqueous, Gas, and Surface Species Properties ---

    /// <summary>Ionic charge (for aqueous species)</summary>
    public int? IonicCharge { get; set; }

    /// <summary>Activity coefficient model parameters</summary>
    public Dictionary<string, double>? ActivityCoefficientParams { get; set; }

    /// <summary>Limiting ionic conductivity (S·cm²/mol) at 25°C</summary>
    public double? IonicConductivity_S_cm2_mol { get; set; }

    // ENHANCEMENT: Properties for reaction generation and speciation
    /// <summary>Is this species the primary basis species for its main element?</summary>
    public bool IsPrimaryElementSpecies { get; set; }

    /// <summary>Oxidation state of the primary element in the species</summary>
    public int? OxidationState { get; set; }

    /// <summary>Henry's Law constant (mol·L⁻¹·atm⁻¹) for gas dissolution</summary>
    public double? HenrysLawConstant_mol_L_atm { get; set; }

    /// <summary>Is this a primary surface site (e.g., >SOH) from which others are derived?</summary>
    public bool IsPrimarySurfaceSite { get; set; }

    /// <summary>Site density (mol/g) for minerals used as surface sorbents</summary>
    public double? SiteDensity_mol_g { get; set; }

    /// <summary>Inner-sphere vs outer-sphere complex flag for Stern Layer modeling.</summary>
    public bool? IsInnerSphereComplex { get; set; }

    /// <summary>Capacitance of the mineral surface (F/m²) for advanced surface models.</summary>
    public double? SurfaceCapacitance_F_m2 { get; set; }

    /// <summary>Thickness of the Stern layer (nm) for surface complexation models.</summary>
    public double? SternLayerThickness_nm { get; set; }

    // --- Additional Physical Properties ---

    /// <summary>Refractive index (for transparent minerals)</summary>
    public double? RefractiveIndex { get; set; }

    /// <summary>Mohs hardness (for minerals)</summary>
    public double? MohsHardness { get; set; }

    /// <summary>Color description</summary>
    public string Color { get; set; } = "";

    /// <summary>Cleavage planes description</summary>
    public string Cleavage { get; set; } = "";

    // --- Metadata ---

    /// <summary>Alternative names or mineral varieties</summary>
    public List<string> Synonyms { get; set; } = new();

    /// <summary>Notes on geological occurrence, stability, or special properties</summary>
    public string Notes { get; set; } = "";

    /// <summary>Literature sources for data</summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>Custom user-defined parameters</summary>
    public Dictionary<string, double> CustomParams { get; set; } = new();

    [JsonIgnore] public bool IsUserCompound { get; set; } = true;
}

/// <summary>
///     Singleton library managing chemical compounds for thermodynamic calculations.
/// </summary>
public sealed class CompoundLibrary
{
    private static readonly Lazy<CompoundLibrary> _lazy = new(() => new CompoundLibrary());
    private readonly List<ChemicalCompound> _compounds = new();
    private readonly List<Element> _elements = new();


    private CompoundLibrary()
    {
        SeedElements(); // Seed elements first
        SeedDefaults(); // Then seed compounds
        SeedSolidSolutions(); // Then define solid solutions
        this.SeedAdditionalCompounds(); // Add extended database (from CompoundLibraryExtensions)
        this.SeedMetamorphicMinerals(); // Add metamorphic minerals (from CompoundLibraryMetamorphicExtensions)
        this.SeedMultiphaseCompounds(); // Add multiphase flow compounds (from CompoundLibraryMultiphaseExtensions)
    }

    // ENHANCEMENT: Add support for solid solutions
    public List<SolidSolution> SolidSolutions { get; } = new();

    public static CompoundLibrary Instance => _lazy.Value;
    public IReadOnlyList<ChemicalCompound> Compounds => _compounds;
    public IReadOnlyList<Element> Elements => _elements;
    public string LibraryFilePath { get; private set; } = "Compounds.library.json";

    public void SetLibraryFilePath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            LibraryFilePath = path;
    }

    public void Clear()
    {
        _compounds.Clear();
    }

    public ChemicalCompound? Find(string nameOrFormula)
    {
        return _compounds.FirstOrDefault(c =>
            string.Equals(c.Name, nameOrFormula, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.ChemicalFormula, nameOrFormula, StringComparison.OrdinalIgnoreCase) ||
            c.Synonyms.Any(s => string.Equals(s, nameOrFormula, StringComparison.OrdinalIgnoreCase)));
    }
/// <summary>
/// Normalize chemical formula input to handle various user input formats.
/// Converts plain text representations to proper Unicode subscripts/superscripts.
/// </summary>
public static string NormalizeFormulaInput(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return input;
    
    var normalized = input;
    
    // Handle charge notation: + and - to superscripts
    // Must be done before number replacements
    normalized = Regex.Replace(normalized, @"(\d*)\+", m =>
    {
        var num = m.Groups[1].Value;
        if (string.IsNullOrEmpty(num)) return "⁺";
        return ConvertToSuperscript(num) + "⁺";
    });
    
    normalized = Regex.Replace(normalized, @"(\d*)\-", m =>
    {
        var num = m.Groups[1].Value;
        if (string.IsNullOrEmpty(num)) return "⁻";
        return ConvertToSuperscript(num) + "⁻";
    });
    
    // Handle parentheses with numbers: (OH)2 -> (OH)₂
    normalized = Regex.Replace(normalized, @"\)(\d+)", m => ")" + ConvertToSubscript(m.Groups[1].Value));
    
    // Handle element-number patterns: H2O -> H₂O, but not 2H2O
    normalized = Regex.Replace(normalized, @"(?<![0-9])([A-Z][a-z]?)(\d+)", m =>
        m.Groups[1].Value + ConvertToSubscript(m.Groups[2].Value));
    
    // Handle hydration dot: *nH2O or .nH2O -> ·nH₂O
    normalized = normalized.Replace("*", "·").Replace(".H", "·H");
    
    // Handle (aq), (s), (l), (g) phase indicators - keep as is
    // They're already handled properly
    
    return normalized;
}

/// <summary>
/// Convert regular numbers to subscript Unicode characters.
/// </summary>
private static string ConvertToSubscript(string numbers)
{
    if (string.IsNullOrEmpty(numbers)) return "";
    
    var subscriptMap = new Dictionary<char, char>
    {
        {'0', '₀'}, {'1', '₁'}, {'2', '₂'}, {'3', '₃'}, {'4', '₄'},
        {'5', '₅'}, {'6', '₆'}, {'7', '₇'}, {'8', '₈'}, {'9', '₉'}
    };
    
    var result = new StringBuilder();
    foreach (char c in numbers)
    {
        result.Append(subscriptMap.TryGetValue(c, out var sub) ? sub : c);
    }
    return result.ToString();
}

/// <summary>
/// Convert regular numbers to superscript Unicode characters.
/// </summary>
private static string ConvertToSuperscript(string numbers)
{
    if (string.IsNullOrEmpty(numbers)) return "";
    
    var superscriptMap = new Dictionary<char, char>
    {
        {'0', '⁰'}, {'1', '¹'}, {'2', '²'}, {'3', '³'}, {'4', '⁴'},
        {'5', '⁵'}, {'6', '⁶'}, {'7', '⁷'}, {'8', '⁸'}, {'9', '⁹'}
    };
    
    var result = new StringBuilder();
    foreach (char c in numbers)
    {
        result.Append(superscriptMap.TryGetValue(c, out var sup) ? sup : c);
    }
    return result.ToString();
}

/// <summary>
/// Enhanced Find method that handles multiple input formats.
/// </summary>
public ChemicalCompound FindFlexible(string name)
{
    if (string.IsNullOrWhiteSpace(name)) return null;
    
    // First try exact match
    var compound = Find(name);
    if (compound != null) return compound;
    
    // Try normalized version
    var normalized = NormalizeFormulaInput(name);
    compound = Find(normalized);
    if (compound != null) return compound;
    
    // Try to find by matching normalized formulas
    compound = _compounds.FirstOrDefault(c => 
        NormalizeFormulaInput(c.ChemicalFormula) == normalized ||
        NormalizeFormulaInput(c.Name) == normalized);
    if (compound != null) return compound;
    
    // Try synonyms
    compound = _compounds.FirstOrDefault(c => 
        c.Synonyms != null && c.Synonyms.Any(s => 
            s.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            NormalizeFormulaInput(s) == normalized));
    if (compound != null) return compound;
    
    // Try case-insensitive name match
    compound = _compounds.FirstOrDefault(c =>
        c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (compound != null) return compound;
    
    // Try removing phase indicators and search again
    var nameWithoutPhase = Regex.Replace(name, @"\((aq|s|l|g)\)", "", RegexOptions.IgnoreCase).Trim();
    if (nameWithoutPhase != name)
        return FindFlexible(nameWithoutPhase);
    
    return null;
}
    public Element? FindElement(string symbolOrName)
    {
        return _elements.FirstOrDefault(e =>
            string.Equals(e.Symbol, symbolOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Name, symbolOrName, StringComparison.OrdinalIgnoreCase));
    }

    public void AddOrUpdate(ChemicalCompound compound)
    {
        if (compound == null || string.IsNullOrWhiteSpace(compound.Name)) return;

        var existing = Find(compound.Name);
        if (existing == null)
        {
            _compounds.Add(compound);
            Logger.Log($"[CompoundLibrary] Added compound: {compound.Name} ({compound.ChemicalFormula})");
        }
        else
        {
            var idx = _compounds.IndexOf(existing);
            _compounds[idx] = compound;
            Logger.Log($"[CompoundLibrary] Updated compound: {compound.Name}");
        }
    }

    /// <summary>
    ///     Removes all user-defined compounds from the library, restoring it to the default set.
    /// </summary>
    public void ClearUserCompounds()
    {
        var removedCount = _compounds.RemoveAll(c => c.IsUserCompound);
        if (removedCount > 0)
            Logger.Log($"[CompoundLibrary] Cleared {removedCount} user-defined compounds.");
    }

    public bool Remove(string name)
    {
        var c = Find(name);
        if (c == null) return false;
        _compounds.Remove(c);
        Logger.Log($"[CompoundLibrary] Removed compound: {name}");
        return true;
    }

    public bool Load(string? path = null)
    {
        try
        {
            var p = path ?? LibraryFilePath;
            if (!File.Exists(p))
            {
                Logger.LogWarning($"[CompoundLibrary] File not found: {p}");
                return false;
            }

            var json = File.ReadAllText(p);
            var loaded = JsonSerializer.Deserialize<List<ChemicalCompound>>(json, JsonOptions());
            if (loaded == null) return false;

            _compounds.Clear();
            _compounds.AddRange(loaded);

            foreach (var c in _compounds) c.IsUserCompound = true;

            Logger.Log($"[CompoundLibrary] Loaded {loaded.Count} compounds from {p}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CompoundLibrary] Load error: {ex.Message}");
            return false;
        }
    }

    public bool Save(string? path = null)
    {
        try
        {
            var p = path ?? LibraryFilePath;
            var json = JsonSerializer.Serialize(_compounds, JsonOptions());
            File.WriteAllText(p, json);
            Logger.Log($"[CompoundLibrary] Saved {Compounds.Count} compounds to {p}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CompoundLibrary] Save error: {ex.Message}");
            return false;
        }
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    ///     Seeds the library with common petrophysical compounds and their thermodynamic properties.
    ///     All values at standard state (298.15 K, 1 bar) unless noted.
    /// </summary>
    private void SeedDefaults()
    {
        _compounds.Clear();

        // ═══════════════════════════════════════════════════════════════════════
        // FUNDAMENTAL AQUEOUS SPECIES
        // ═══════════════════════════════════════════════════════════════════════
        _compounds.Add(new ChemicalCompound
        {
            Name = "Water",
            ChemicalFormula = "H₂O",
            Synonyms = new List<string> { "H2O" },
            Phase = CompoundPhase.Liquid,
            GibbsFreeEnergyFormation_kJ_mol = -237.1,
            EnthalpyFormation_kJ_mol = -285.8,
            Entropy_J_molK = 70.0,
            HeatCapacity_J_molK = 75.3,
            MolecularWeight_g_mol = 18.015,
            MolarVolume_cm3_mol = 18.068,
            Density_g_cm3 = 0.997,
            Notes = "The solvent.",
            Sources = { "NIST Chemistry WebBook" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Proton",
            ChemicalFormula = "H⁺",
            Synonyms = new List<string> { "H+" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 0.0,
            MolecularWeight_g_mol = 1.008,
            IonicCharge = 1,
            IsPrimaryElementSpecies = true,
            OxidationState = 1,
            Notes = "Basis of pH. Thermodynamic properties are zero by convention.",
            Sources = { "Stumm & Morgan (1996)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Hydroxide",
            ChemicalFormula = "OH⁻",
            Synonyms = new List<string> { "OH-" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -157.2,
            EnthalpyFormation_kJ_mol = -230.0,
            Entropy_J_molK = -10.9,
            MolecularWeight_g_mol = 17.008,
            IonicCharge = -1,
            Sources = { "PHREEQC database" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Electron",
            ChemicalFormula = "e⁻",
            Synonyms = new List<string> { "e-" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 0.0,
            MolecularWeight_g_mol = 5.4858e-4,
            IonicCharge = -1,
            IsPrimaryElementSpecies = true,
            Notes = "Basis of pe. Thermodynamic properties are zero by convention.",
            Sources = { "Stumm & Morgan (1996)" },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // CARBONATES - Critical for reservoir geochemistry
        // ═══════════════════════════════════════════════════════════════════════

        _compounds.Add(new ChemicalCompound
        {
            Name = "Calcite",
            ChemicalFormula = "CaCO₃",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -1128.8,
            EnthalpyFormation_kJ_mol = -1206.9,
            Entropy_J_molK = 92.9,
            HeatCapacity_J_molK = 81.88,
            MolarVolume_cm3_mol = 36.934,
            MolecularWeight_g_mol = 100.09,
            Density_g_cm3 = 2.71,
            LogKsp_25C = -8.48,
            Solubility_g_100mL_25C = 0.0014,
            DissolutionEnthalpy_kJ_mol = -12.3,
            ActivationEnergy_Dissolution_kJ_mol = 23.5, // Neutral mechanism
            RateConstant_Dissolution_mol_m2_s = 1.0e-6,
            AcidCatalysisOrder = 1.0, // log k = -0.37 + 1.0 log(aH+)
            ReactionOrder_Dissolution = 1.0,
            SpecificSurfaceArea_m2_g = 0.2,
            MohsHardness = 3.0,
            Color = "Colorless to white",
            Cleavage = "Perfect rhombohedral {10̄14}",
            HeatCapacityPolynomial_a_b_c_d = new[] { 104.52, 0.02192, -2617800, 0.0 },
            TemperatureRange_K = new[] { 298.0, 1200.0 },
            Notes = "Most common carbonate mineral in sedimentary rocks. Shows retrograde solubility with temperature.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): Thermodynamic Properties of Minerals, USGS Bulletin 2131, p.136",
                "Palandri & Kharaka (2004): USGS Open-File Report 2004-1068 (kinetics)",
                "Parkhurst & Appelo (2013): PHREEQC Version 3, USGS Techniques and Methods 6-A43 (Ksp)"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Dolomite",
            ChemicalFormula = "CaMg(CO₃)₂",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -2161.7,
            EnthalpyFormation_kJ_mol = -2326.3,
            Entropy_J_molK = 155.2,
            HeatCapacity_J_molK = 157.5,
            MolarVolume_cm3_mol = 64.365,
            MolecularWeight_g_mol = 184.40,
            Density_g_cm3 = 2.86,
            LogKsp_25C = -17.09,
            Solubility_g_100mL_25C = 0.0032,
            DissolutionEnthalpy_kJ_mol = -21.3,
            ActivationEnergy_Dissolution_kJ_mol = 52.2,
            RateConstant_Dissolution_mol_m2_s = 2.9e-12,
            ReactionOrder_Dissolution = 0.5,
            SpecificSurfaceArea_m2_g = 0.15,
            MohsHardness = 3.5,
            Color = "White to pink",
            Cleavage = "Perfect rhombohedral {10̄14}",
            Notes = "Dissolves ~50x slower than calcite. Important in dolomitization processes.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.194",
                "Pokrovsky & Schott (2001): Geochim. Cosmochim. Acta 65, 3643-3652 (kinetics)",
                "Nordstrom et al. (1990): Chemical modeling of aqueous systems II, ACS Symp. Ser. 416 (Ksp)"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Magnesite",
            ChemicalFormula = "MgCO₃",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -1027.9,
            EnthalpyFormation_kJ_mol = -1111.7,
            Entropy_J_molK = 65.7,
            HeatCapacity_J_molK = 75.5,
            MolarVolume_cm3_mol = 28.018,
            MolecularWeight_g_mol = 84.31,
            Density_g_cm3 = 3.01,
            LogKsp_25C = -7.83,
            Solubility_g_100mL_25C = 0.0106,
            MohsHardness = 4.0,
            Color = "White to yellowish",
            Notes = "CO₂ sequestration mineral. Slow precipitation kinetics.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.283",
                "PHREEQC database (phreeqc.dat)"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Siderite",
            ChemicalFormula = "FeCO₃",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -680.1,
            EnthalpyFormation_kJ_mol = -755.9,
            Entropy_J_molK = 95.5,
            HeatCapacity_J_molK = 82.13,
            MolarVolume_cm3_mol = 29.378,
            MolecularWeight_g_mol = 115.86,
            Density_g_cm3 = 3.94,
            LogKsp_25C = -10.89,
            MohsHardness = 4.0,
            Color = "Brown to yellow-brown",
            Notes = "Forms in reducing environments. Iron source in diagenesis.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.374",
                "Stumm & Morgan (1996): Aquatic Chemistry, 3rd ed., Wiley"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Aragonite",
            ChemicalFormula = "CaCO₃",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1127.8,
            EnthalpyFormation_kJ_mol = -1207.4,
            Entropy_J_molK = 88.0,
            HeatCapacity_J_molK = 81.25,
            MolarVolume_cm3_mol = 34.15,
            MolecularWeight_g_mol = 100.09,
            Density_g_cm3 = 2.93,
            LogKsp_25C = -8.34,
            MohsHardness = 3.5,
            Color = "Colorless to white",
            Notes = "Metastable CaCO₃ polymorph. Transforms to calcite. Common in biogenic carbonates.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.84",
                "Plummer & Busenberg (1982): Geochim. Cosmochim. Acta 46, 1011-1040"
            },
            IsUserCompound = false
        });


        // ═══════════════════════════════════════════════════════════════════════
        // SULFATES - Important in evaporite sequences
        // ═══════════════════════════════════════════════════════════════════════

        _compounds.Add(new ChemicalCompound
        {
            Name = "Gypsum",
            ChemicalFormula = "CaSO₄·2H₂O",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -1797.2,
            EnthalpyFormation_kJ_mol = -2022.6,
            Entropy_J_molK = 194.1,
            HeatCapacity_J_molK = 186.0,
            MolarVolume_cm3_mol = 74.69,
            MolecularWeight_g_mol = 172.17,
            Density_g_cm3 = 2.31,
            LogKsp_25C = -4.58,
            Solubility_g_100mL_25C = 0.241,
            DissolutionEnthalpy_kJ_mol = -17.8,
            MohsHardness = 2.0,
            Color = "Colorless to white",
            Cleavage = "Perfect {010}, distinct {100}",
            Notes = "Dehydrates to anhydrite at elevated temperatures. Common in evaporites.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.223",
                "Blount & Dickson (1973): Am. Mineral. 58, 323-331 (solubility)",
                "PHREEQC database"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Anhydrite",
            ChemicalFormula = "CaSO₄",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1321.8,
            EnthalpyFormation_kJ_mol = -1434.5,
            Entropy_J_molK = 106.5,
            HeatCapacity_J_molK = 99.7,
            MolarVolume_cm3_mol = 45.94,
            MolecularWeight_g_mol = 136.14,
            Density_g_cm3 = 2.96,
            LogKsp_25C = -4.36,
            Solubility_g_100mL_25C = 0.209,
            MohsHardness = 3.5,
            Color = "White to gray",
            Notes = "Hydrates to gypsum in water. Important cap rock mineral.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.83",
                "Blount & Dickson (1973): Am. Mineral. 58, 323-331"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Barite",
            ChemicalFormula = "BaSO₄",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1362.2,
            EnthalpyFormation_kJ_mol = -1473.2,
            Entropy_J_molK = 132.2,
            HeatCapacity_J_molK = 101.8,
            MolarVolume_cm3_mol = 52.10,
            MolecularWeight_g_mol = 233.39,
            Density_g_cm3 = 4.48,
            LogKsp_25C = -9.97,
            Solubility_g_100mL_25C = 0.00024,
            MohsHardness = 3.0,
            Color = "Colorless to white",
            Notes = "Very low solubility. Forms scale in petroleum production.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.99",
                "Blount (1977): Clays Clay Miner. 25, 365-369 (solubility)",
                "Benton et al. (2019): SPE Int. Oilfield Scale Conf., SPE-193583-MS (scale formation)"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Celestite",
            ChemicalFormula = "SrSO₄",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1341.0,
            EnthalpyFormation_kJ_mol = -1453.1,
            Entropy_J_molK = 117.0,
            MolarVolume_cm3_mol = 46.25,
            MolecularWeight_g_mol = 183.68,
            Density_g_cm3 = 3.97,
            LogKsp_25C = -6.63,
            MohsHardness = 3.0,
            Notes = "Strontium source. Often co-occurs with evaporites.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.156",
                "PHREEQC database"
            },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // HALIDES - Evaporite minerals
        // ═══════════════════════════════════════════════════════════════════════

        _compounds.Add(new ChemicalCompound
        {
            Name = "Halite",
            ChemicalFormula = "NaCl",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -384.1,
            EnthalpyFormation_kJ_mol = -411.2,
            Entropy_J_molK = 72.1,
            HeatCapacity_J_molK = 50.5,
            MolarVolume_cm3_mol = 27.015,
            MolecularWeight_g_mol = 58.44,
            Density_g_cm3 = 2.16,
            LogKsp_25C = 1.58,
            Solubility_g_100mL_25C = 36.0,
            DissolutionEnthalpy_kJ_mol = 3.9,
            MohsHardness = 2.5,
            Color = "Colorless to white",
            Cleavage = "Perfect cubic {100}",
            Notes = "Highly soluble. Forms massive evaporite deposits. Salt dome formation.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.245",
                "Pitzer & Mayorga (1973): J. Phys. Chem. 77, 2300-2308 (activity)",
                "NIST Chemistry WebBook (webbook.nist.gov)"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Sylvite",
            ChemicalFormula = "KCl",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -409.1,
            EnthalpyFormation_kJ_mol = -436.7,
            Entropy_J_molK = 82.6,
            HeatCapacity_J_molK = 51.3,
            MolarVolume_cm3_mol = 37.52,
            MolecularWeight_g_mol = 74.55,
            Density_g_cm3 = 1.99,
            LogKsp_25C = 0.90,
            Solubility_g_100mL_25C = 34.0,
            MohsHardness = 2.0,
            Color = "Colorless to white",
            Notes = "Potassium ore. Forms in late-stage evaporites.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.387",
                "NIST Chemistry WebBook"
            },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // SILICATES - Dominant rock-forming minerals
        // ═══════════════════════════════════════════════════════════════════════

        _compounds.Add(new ChemicalCompound
        {
            Name = "Quartz",
            ChemicalFormula = "SiO₂",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -856.3,
            EnthalpyFormation_kJ_mol = -910.7,
            Entropy_J_molK = 41.5,
            HeatCapacity_J_molK = 44.6,
            MolarVolume_cm3_mol = 22.688,
            MolecularWeight_g_mol = 60.08,
            Density_g_cm3 = 2.65,
            LogKsp_25C = -3.98, // For SiO2(q) + 2H2O = H4SiO4
            Solubility_g_100mL_25C = 0.0012,
            ActivationEnergy_Dissolution_kJ_mol = 87.7,
            RateConstant_Dissolution_mol_m2_s = 1.26e-10, // Neutral mechanism
            BaseCatalysisOrder = 0.29, // for OH-
            MohsHardness = 7.0,
            Color = "Colorless, various",
            Notes =
                "Extremely slow dissolution kinetics. Thermodynamically stable silica polymorph at surface conditions.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.356",
                "Palandri & Kharaka (2004): USGS Open-File Report 2004-1068 (kinetics)",
                "Dove & Rimstidt (1994): Rev. Mineral. Geochem. 29, 259-308 (dissolution mechanisms)"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Amorphous Silica",
            ChemicalFormula = "SiO₂",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Amorphous,
            GibbsFreeEnergyFormation_kJ_mol = -849.0,
            EnthalpyFormation_kJ_mol = -903.5,
            MolecularWeight_g_mol = 60.08,
            Density_g_cm3 = 2.2,
            LogKsp_25C = -2.71, // For SiO2(am) + 2H2O = H4SiO4
            Solubility_g_100mL_25C = 0.012,
            Notes = "Metastable phase. Higher solubility than quartz. Forms opal.",
            Sources = new List<string>
            {
                "Rimstidt (1997): Geochim. Cosmochim. Acta 61, 2553-2558",
                "Gunnarsson & Arnórsson (2000): Geochim. Cosmochim. Acta 64, 2295-2307"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Kaolinite",
            ChemicalFormula = "Al₂Si₂O₅(OH)₄",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -3799.4,
            EnthalpyFormation_kJ_mol = -4120.1,
            Entropy_J_molK = 203.7,
            HeatCapacity_J_molK = 205.0,
            MolarVolume_cm3_mol = 99.52,
            MolecularWeight_g_mol = 258.16,
            Density_g_cm3 = 2.59,
            LogKsp_25C = 6.81,
            MohsHardness = 2.0,
            Color = "White",
            Notes = "Common clay mineral. Forms from weathering of feldspars.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.261",
                "Nagy (1995): Rev. Mineral. Geochem. 31, 173-233 (dissolution kinetics)",
                "PHREEQC database"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Albite",
            ChemicalFormula = "NaAlSi₃O₈",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -3711.5,
            EnthalpyFormation_kJ_mol = -3935.1,
            Entropy_J_molK = 207.4,
            HeatCapacity_J_molK = 205.1,
            MolarVolume_cm3_mol = 100.07,
            MolecularWeight_g_mol = 262.22,
            Density_g_cm3 = 2.62,
            LogKsp_25C = 2.76,
            MohsHardness = 6.0,
            Color = "White to gray",
            Notes = "Sodium feldspar. Important in diagenesis and metamorphism.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.61",
                "Helgeson et al. (1978): Am. J. Sci. 278-A (SUPCRT database)"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "K-Feldspar",
            Synonyms = new List<string> { "Microcline" },
            ChemicalFormula = "KAlSi₃O₈",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -3744.1,
            EnthalpyFormation_kJ_mol = -3989.4,
            Entropy_J_molK = 214.2,
            MolarVolume_cm3_mol = 109.1,
            MolecularWeight_g_mol = 278.33,
            Density_g_cm3 = 2.55,
            ActivationEnergy_Dissolution_kJ_mol = 51.7,
            RateConstant_Dissolution_mol_m2_s = 6.17e-13, // Neutral mechanism
            AcidCatalysisOrder = 0.5,
            BaseCatalysisOrder = 0.3,
            MohsHardness = 6.0,
            Color = "White to pink",
            Notes = "Potassium feldspar, common in granites and arkosic sandstones.",
            Sources = { "Robie & Hemingway (1995)", "Palandri & Kharaka (2004)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Muscovite",
            ChemicalFormula = "KAl₂(AlSi₃O₁₀)(OH)₂",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -5608.0,
            EnthalpyFormation_kJ_mol = -5988.6,
            Entropy_J_molK = 287.9,
            MolarVolume_cm3_mol = 140.7,
            MolecularWeight_g_mol = 398.31,
            Density_g_cm3 = 2.83,
            MohsHardness = 2.5,
            Color = "Colorless, silver-white",
            Notes = "Common mica mineral, indicates metamorphic grade.",
            Sources = { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });


        // ═══════════════════════════════════════════════════════════════════════
        // OXIDES & HYDROXIDES - Alteration products
        // ═══════════════════════════════════════════════════════════════════════

        _compounds.Add(new ChemicalCompound
        {
            Name = "Hematite",
            ChemicalFormula = "Fe₂O₃",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -742.2,
            EnthalpyFormation_kJ_mol = -825.5,
            Entropy_J_molK = 87.4,
            HeatCapacity_J_molK = 103.9,
            MolarVolume_cm3_mol = 30.274,
            MolecularWeight_g_mol = 159.69,
            Density_g_cm3 = 5.27,
            MohsHardness = 6.5,
            SiteDensity_mol_g = 1.0e-5, // Typical value for surface complexation
            Color = "Red to steel-gray",
            Notes = "Iron oxide. Forms in oxidizing conditions. Red beds.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.234",
                "Cornell & Schwertmann (2003): The Iron Oxides, 2nd ed., Wiley-VCH"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Goethite",
            ChemicalFormula = "FeO(OH)",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -488.6,
            EnthalpyFormation_kJ_mol = -559.4,
            Entropy_J_molK = 60.4,
            HeatCapacity_J_molK = 49.2,
            MolarVolume_cm3_mol = 20.82,
            MolecularWeight_g_mol = 88.85,
            Density_g_cm3 = 4.27,
            LogKsp_25C = -41.0,
            MohsHardness = 5.5,
            SiteDensity_mol_g = 1.2e-5, // Typical value for surface complexation
            Color = "Yellow-brown",
            Notes = "Most stable iron oxyhydroxide at Earth surface conditions.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.217",
                "Cornell & Schwertmann (2003): The Iron Oxides"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Magnetite",
            ChemicalFormula = "Fe₃O₄",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -1015.4,
            EnthalpyFormation_kJ_mol = -1118.4,
            Entropy_J_molK = 146.4,
            MolarVolume_cm3_mol = 44.5,
            MolecularWeight_g_mol = 231.53,
            Density_g_cm3 = 5.20,
            MohsHardness = 6.0,
            Color = "Black",
            Notes = "Ferrimagnetic iron oxide. Common in igneous rocks and BIFs.",
            Sources = { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Gibbsite",
            ChemicalFormula = "Al(OH)₃",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -1154.9,
            EnthalpyFormation_kJ_mol = -1293.1,
            Entropy_J_molK = 68.4,
            HeatCapacity_J_molK = 93.2,
            MolarVolume_cm3_mol = 31.96,
            MolecularWeight_g_mol = 78.00,
            Density_g_cm3 = 2.44,
            LogKsp_25C = 8.11,
            MohsHardness = 3.0,
            Color = "White",
            Notes = "Aluminum hydroxide. Forms in tropical weathering.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.213",
                "Nordstrom & May (1996): Rev. Mineral. Geochem. 31, 133-155"
            },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // AQUEOUS IONS - Essential for solution chemistry
        // ═══════════════════════════════════════════════════════════════════════

        _compounds.Add(new ChemicalCompound
        {
            Name = "Calcium Ion",
            ChemicalFormula = "Ca²⁺",
            Synonyms = { "Ca+2" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -553.5,
            EnthalpyFormation_kJ_mol = -543.0,
            Entropy_J_molK = -56.2,
            HeatCapacity_J_molK = -31.5,
            MolecularWeight_g_mol = 40.08,
            IonicCharge = 2,
            IsPrimaryElementSpecies = true,
            OxidationState = 2,
            IonicConductivity_S_cm2_mol = 59.5,
            Notes = "Major cation in natural waters. Key player in carbonate equilibria.",
            Sources = { "Shock & Helgeson (1988)", "Marcus (1997)", "PHREEQC database" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Magnesium Ion",
            ChemicalFormula = "Mg²⁺",
            Synonyms = { "Mg+2" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -454.8,
            EnthalpyFormation_kJ_mol = -467.0,
            Entropy_J_molK = -137.0,
            HeatCapacity_J_molK = 23.8,
            MolecularWeight_g_mol = 24.31,
            IonicCharge = 2,
            IsPrimaryElementSpecies = true,
            OxidationState = 2,
            IonicConductivity_S_cm2_mol = 53.1,
            Notes = "Important in dolomitization and clay mineral formation.",
            Sources = { "Shock & Helgeson (1988)", "PHREEQC database" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Sulfate Ion",
            ChemicalFormula = "SO₄²⁻",
            Synonyms = { "SO4-2" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -744.0,
            EnthalpyFormation_kJ_mol = -909.3,
            Entropy_J_molK = 18.5,
            HeatCapacity_J_molK = -293.0,
            MolecularWeight_g_mol = 96.06,
            IonicCharge = -2,
            IsPrimaryElementSpecies = true,
            OxidationState = 6,
            IonicConductivity_S_cm2_mol = 80.0,
            Notes = "Major anion in seawater and formation waters.",
            Sources = { "Shock & Helgeson (1988)", "PHREEQC database" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Chloride Ion",
            ChemicalFormula = "Cl⁻",
            Synonyms = { "Cl-" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -131.2,
            EnthalpyFormation_kJ_mol = -167.1,
            Entropy_J_molK = 56.6,
            HeatCapacity_J_molK = -136.4,
            MolecularWeight_g_mol = 35.45,
            IonicCharge = -1,
            IsPrimaryElementSpecies = true,
            OxidationState = -1,
            IonicConductivity_S_cm2_mol = 76.3,
            Notes = "Conservative tracer. Most abundant anion in saline waters.",
            Sources = { "Shock & Helgeson (1988)", "NIST Chemistry WebBook" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Sodium Ion",
            ChemicalFormula = "Na⁺",
            Synonyms = { "Na+" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -261.9,
            EnthalpyFormation_kJ_mol = -240.3,
            Entropy_J_molK = 58.4,
            HeatCapacity_J_molK = 46.4,
            MolecularWeight_g_mol = 22.99,
            IonicCharge = 1,
            IsPrimaryElementSpecies = true,
            OxidationState = 1,
            IonicConductivity_S_cm2_mol = 50.1,
            Notes = "Dominant cation in seawater.",
            Sources = { "Shock & Helgeson (1988)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Potassium Ion",
            ChemicalFormula = "K⁺",
            Synonyms = { "K+" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -282.5,
            EnthalpyFormation_kJ_mol = -252.1,
            Entropy_J_molK = 101.2,
            MolecularWeight_g_mol = 39.10,
            IonicCharge = 1,
            IsPrimaryElementSpecies = true,
            OxidationState = 1,
            Notes = "Important nutrient and component of clay minerals.",
            Sources = { "Shock & Helgeson (1988)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Ferrous Iron",
            ChemicalFormula = "Fe²⁺",
            Synonyms = { "Fe+2" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -90.5,
            EnthalpyFormation_kJ_mol = -89.1,
            Entropy_J_molK = -137.7,
            MolecularWeight_g_mol = 55.85,
            IonicCharge = 2,
            IsPrimaryElementSpecies = true,
            OxidationState = 2,
            Notes = "Dominant form of iron in anoxic waters.",
            Sources = { "Stumm & Morgan (1996)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Ferric Iron",
            ChemicalFormula = "Fe³⁺",
            Synonyms = { "Fe+3" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -16.7,
            EnthalpyFormation_kJ_mol = -48.5,
            Entropy_J_molK = -315.9,
            MolecularWeight_g_mol = 55.85,
            IonicCharge = 3,
            OxidationState = 3,
            Notes = "Dominant form of iron in oxic waters; highly insoluble above pH 3.5.",
            Sources = { "Stumm & Morgan (1996)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Aluminum Ion",
            ChemicalFormula = "Al³⁺",
            Synonyms = { "Al+3" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -489.4,
            EnthalpyFormation_kJ_mol = -531.0,
            Entropy_J_molK = -321.7,
            MolecularWeight_g_mol = 26.98,
            IonicCharge = 3,
            IsPrimaryElementSpecies = true,
            OxidationState = 3,
            Notes = "Important in weathering, only significant at low pH.",
            Sources = { "PHREEQC database" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Silicic Acid",
            ChemicalFormula = "H₄SiO₄",
            Synonyms = { "Si(OH)4" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -1308.0,
            EnthalpyFormation_kJ_mol = -1460.0,
            Entropy_J_molK = 180.0,
            MolecularWeight_g_mol = 96.12,
            IonicCharge = 0,
            IsPrimaryElementSpecies = true,
            OxidationState = 4,
            Notes = "Primary dissolved form of silicon in most natural waters.",
            Sources = { "PHREEQC database" },
            IsUserCompound = false
        });


        // ═══════════════════════════════════════════════════════════════════════
        // AQUEOUS COMPLEXES AND DISSOLVED GASES
        // ═══════════════════════════════════════════════════════════════════════

        _compounds.Add(new ChemicalCompound
        {
            Name = "Aqueous Carbon Dioxide",
            ChemicalFormula = "CO₂(aq)",
            Synonyms = { "CO2(aq)" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -385.9,
            EnthalpyFormation_kJ_mol = -413.8,
            Entropy_J_molK = 117.6,
            MolecularWeight_g_mol = 44.01,
            IonicCharge = 0,
            IsPrimaryElementSpecies = true,
            OxidationState = 4,
            Notes = "Represents dissolved CO2 and H2CO3.",
            Sources = { "Stumm & Morgan (1996)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Bicarbonate",
            ChemicalFormula = "HCO₃⁻",
            Synonyms = { "HCO3-" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -586.8,
            EnthalpyFormation_kJ_mol = -689.9,
            Entropy_J_molK = 98.4,
            HeatCapacity_J_molK = -6.3,
            MolecularWeight_g_mol = 61.02,
            IonicCharge = -1,
            IonicConductivity_S_cm2_mol = 44.5,
            Notes = "Dominant carbonate species at pH 6.5-10.3. Critical for pH buffering.",
            Sources = { "Shock & Helgeson (1988)", "Stumm & Morgan (1996)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Carbonate",
            ChemicalFormula = "CO₃²⁻",
            Synonyms = { "CO3-2" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -527.8,
            EnthalpyFormation_kJ_mol = -677.1,
            Entropy_J_molK = -59.0,
            MolecularWeight_g_mol = 60.01,
            IonicCharge = -2,
            Notes = "Dominant carbonate species above pH 10.3.",
            Sources = { "Shock & Helgeson (1988)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Aqueous Hydrogen Sulfide",
            ChemicalFormula = "H₂S(aq)",
            Synonyms = { "H2S(aq)" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -27.8,
            EnthalpyFormation_kJ_mol = -39.7,
            Entropy_J_molK = 83.7,
            MolecularWeight_g_mol = 34.08,
            IonicCharge = 0,
            IsPrimaryElementSpecies = true,
            OxidationState = -2,
            Notes = "Dissolved hydrogen sulfide gas.",
            Sources = { "PHREEQC database" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Bisulfide",
            ChemicalFormula = "HS⁻",
            Synonyms = { "HS-" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = 12.1,
            EnthalpyFormation_kJ_mol = -17.6,
            Entropy_J_molK = 62.8,
            MolecularWeight_g_mol = 33.07,
            IonicCharge = -1,
            OxidationState = -2,
            Notes = "Dominant sulfide species between pH 7 and 13.",
            Sources = { "PHREEQC database" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Sulfide",
            ChemicalFormula = "S²⁻",
            Synonyms = { "S-2" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = 85.8,
            EnthalpyFormation_kJ_mol = 33.1,
            Entropy_J_molK = -14.6,
            MolecularWeight_g_mol = 32.06,
            IonicCharge = -2,
            OxidationState = -2,
            Notes = "Only dominant at very high pH.",
            Sources = { "PHREEQC database" },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // GAS PHASE SPECIES
        // ═══════════════════════════════════════════════════════════════════════

        _compounds.Add(new ChemicalCompound
        {
            Name = "Carbon Dioxide (g)",
            ChemicalFormula = "CO₂(g)",
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = -394.38,
            EnthalpyFormation_kJ_mol = -393.5,
            Entropy_J_molK = 213.8,
            HenrysLawConstant_mol_L_atm = 3.4e-2,
            Sources = { "NIST Chemistry WebBook", "Sander (2015) Atmos. Chem. Phys." },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Oxygen (g)",
            ChemicalFormula = "O₂(g)",
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 205.2,
            HenrysLawConstant_mol_L_atm = 1.3e-3,
            Sources = { "NIST Chemistry WebBook", "Sander (2015)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Hydrogen Sulfide (g)",
            ChemicalFormula = "H₂S(g)",
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = -33.5,
            EnthalpyFormation_kJ_mol = -20.6,
            Entropy_J_molK = 205.8,
            HenrysLawConstant_mol_L_atm = 1.0e-1,
            Sources = { "NIST Chemistry WebBook", "Sander (2015)" },
            IsUserCompound = false
        });


        // ═══════════════════════════════════════════════════════════════════════
        // SURFACE SPECIES (using generic >SOH for HFO - Hydrous Ferric Oxide)
        // ═══════════════════════════════════════════════════════════════════════
        _compounds.Add(new ChemicalCompound
        {
            Name = ">SOH",
            ChemicalFormula = ">SOH",
            Phase = CompoundPhase.Surface,
            IsPrimarySurfaceSite = true,
            IonicCharge = 0,
            LogKsp_25C = 7.29, // pKa1 for >SOH + H+ = >SOH2+
            Notes = "Primary neutral surface hydroxyl site. Data from Dzombak & Morel (1990) for HFO.",
            Sources = { "Dzombak & Morel (1990). Surface Complexation Modeling." },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = ">SOH₂⁺",
            ChemicalFormula = ">SOH₂⁺",
            Synonyms = { ">SOH2+" },
            Phase = CompoundPhase.Surface,
            IonicCharge = 1,
            Notes = "Protonated surface site.",
            Sources = { "Dzombak & Morel (1990)" },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = ">SO⁻",
            ChemicalFormula = ">SO⁻",
            Synonyms = { ">SO-" },
            Phase = CompoundPhase.Surface,
            IonicCharge = -1,
            LogKsp_25C = -8.93, // pKa2 for >SOH = >SO- + H+
            Notes = "Deprotonated surface site.",
            Sources = { "Dzombak & Morel (1990)" },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // ADDITIONAL PETROPHYSICALLY RELEVANT MINERALS
        // ═══════════════════════════════════════════════════════════════════════

        _compounds.Add(new ChemicalCompound
        {
            Name = "Pyrite",
            ChemicalFormula = "FeS₂",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -166.9,
            EnthalpyFormation_kJ_mol = -178.2,
            Entropy_J_molK = 52.9,
            HeatCapacity_J_molK = 62.2,
            MolarVolume_cm3_mol = 23.94,
            MolecularWeight_g_mol = 119.98,
            Density_g_cm3 = 5.01,
            LogKsp_25C = -16.4,
            MohsHardness = 6.5,
            Color = "Brass-yellow",
            Notes = "Common sulfide. Oxidizes to produce acid mine drainage. Important in anoxic diagenesis.",
            Sources = new List<string>
            {
                "Robie & Hemingway (1995): USGS Bulletin 2131, p.359",
                "Rickard & Luther (2007): Chem. Rev. 107, 514-562 (iron sulfide chemistry)"
            },
            IsUserCompound = false
        });

        _compounds.Add(new ChemicalCompound
        {
            Name = "Illite",
            ChemicalFormula = "K₀.₆₅Al₂.₀[Al₀.₆₅Si₃.₃₅O₁₀](OH)₂",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -5301.8,
            EnthalpyFormation_kJ_mol = -5653.3,
            Entropy_J_molK = 235.0,
            MolarVolume_cm3_mol = 140.0,
            MolecularWeight_g_mol = 383.89,
            Density_g_cm3 = 2.75,
            MohsHardness = 2.0,
            Color = "White to gray",
            Notes = "Common clay mineral. Forms during diagenesis of shales. Potassium indicator.",
            Sources = new List<string>
            {
                "Holland & Powell (2011): J. Metamorph. Geol. 29, 333-383 (updated thermodynamic dataset)",
                "Chermak & Rimstidt (1989): Geochim. Cosmochim. Acta 53, 2699-2707"
            },
            IsUserCompound = false
        });

        Logger.Log($"[CompoundLibrary] Seeded {_compounds.Count} default compounds");
    }

    /// <summary>
    ///     ENHANCEMENT: Seeds solid solution definitions.
    /// </summary>
    private void SeedSolidSolutions()
    {
        SolidSolutions.Clear();

        SolidSolutions.Add(new SolidSolution
        {
            Name = "Calcite-Magnesite Solid Solution",
            EndMembers = new List<string> { "Calcite", "Magnesite" },
            MixingModel = SolidSolutionMixingModel.Regular,
            InteractionParameters = new List<double> { 5.0, 5.0 } // Simplified interaction parameter (W)
        });

        Logger.Log($"[CompoundLibrary] Seeded {SolidSolutions.Count} solid solutions.");
    }

    /// <summary>
    ///     Seeds the library with elements and their atomic properties.
    ///     Sources: IUPAC Periodic Table, CRC Handbook, NIST Atomic Spectra Database
    /// </summary>
    private void SeedElements()
    {
        _elements.Clear();

        // ═══════════════════════════════════════════════════════════════════════
        // PERIOD 1
        // ═══════════════════════════════════════════════════════════════════════

        _elements.Add(new Element
        {
            Name = "Hydrogen", Symbol = "H", AtomicNumber = 1, AtomicMass = 1.008,
            ElementType = ElementType.Nonmetal, Group = 1, Period = 1,
            ElectronConfiguration = "1s¹", ValenceElectrons = 1,
            Electronegativity = 2.20, OxidationStates = new List<int> { -1, 1 },
            FirstIonizationEnergy_kJ_mol = 1312.0, ElectronAffinity_kJ_mol = 72.8,
            CovalentRadius_pm = 31, VanDerWaalsRadius_pm = 120,
            IonicRadii = new Dictionary<int, int> { { 1, 10 }, { -1, 154 } },
            MeltingPoint_K = 13.99, BoilingPoint_K = 20.271, Density_g_cm3 = 0.00008988,
            CrustalAbundance_ppm = 1400, SeawaterConcentration_mg_L = 108000.0,
            Sources = new List<string> { "IUPAC Periodic Table 2023", "CRC Handbook 104th ed." }
        });

        _elements.Add(new Element
        {
            Name = "Helium", Symbol = "He", AtomicNumber = 2, AtomicMass = 4.0026,
            ElementType = ElementType.NobleGas, Group = 18, Period = 1,
            ElectronConfiguration = "1s²", ValenceElectrons = 0,
            FirstIonizationEnergy_kJ_mol = 2372.3,
            CovalentRadius_pm = 28, VanDerWaalsRadius_pm = 140,
            MeltingPoint_K = 0.95, BoilingPoint_K = 4.222, Density_g_cm3 = 0.0001785,
            CrustalAbundance_ppm = 0.008, SeawaterConcentration_mg_L = 0.0000072,
            Sources = new List<string> { "IUPAC", "CRC Handbook" }
        });

        // ═══════════════════════════════════════════════════════════════════════
        // PERIOD 2
        // ═══════════════════════════════════════════════════════════════════════

        _elements.Add(new Element
        {
            Name = "Lithium", Symbol = "Li", AtomicNumber = 3, AtomicMass = 6.94,
            ElementType = ElementType.AlkaliMetal, Group = 1, Period = 2,
            ElectronConfiguration = "[He] 2s¹", ValenceElectrons = 1,
            Electronegativity = 0.98, OxidationStates = new List<int> { 1 },
            FirstIonizationEnergy_kJ_mol = 520.2, ElectronAffinity_kJ_mol = 59.6,
            AtomicRadius_pm = 152, CovalentRadius_pm = 128, IonicRadii = new Dictionary<int, int> { { 1, 76 } },
            MeltingPoint_K = 453.65, BoilingPoint_K = 1603, Density_g_cm3 = 0.534, ThermalConductivity_W_mK = 84.8,
            CrustalAbundance_ppm = 20, SeawaterConcentration_mg_L = 0.18,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Beryllium", Symbol = "Be", AtomicNumber = 4, AtomicMass = 9.0122,
            ElementType = ElementType.AlkalineEarthMetal, Group = 2, Period = 2,
            ElectronConfiguration = "[He] 2s²", ValenceElectrons = 2,
            Electronegativity = 1.57, OxidationStates = new List<int> { 2 },
            FirstIonizationEnergy_kJ_mol = 899.5, AtomicRadius_pm = 112, CovalentRadius_pm = 96,
            IonicRadii = new Dictionary<int, int> { { 2, 45 } },
            MeltingPoint_K = 1560, BoilingPoint_K = 2742, Density_g_cm3 = 1.85, ThermalConductivity_W_mK = 200,
            CrustalAbundance_ppm = 2.8, SeawaterConcentration_mg_L = 0.0006,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Boron", Symbol = "B", AtomicNumber = 5, AtomicMass = 10.81,
            ElementType = ElementType.Metalloid, Group = 13, Period = 2,
            ElectronConfiguration = "[He] 2s² 2p¹", ValenceElectrons = 3,
            Electronegativity = 2.04, OxidationStates = new List<int> { -5, -1, 1, 2, 3 },
            FirstIonizationEnergy_kJ_mol = 800.6, ElectronAffinity_kJ_mol = 26.7,
            AtomicRadius_pm = 85, CovalentRadius_pm = 84, VanDerWaalsRadius_pm = 192,
            MeltingPoint_K = 2349, BoilingPoint_K = 4200, Density_g_cm3 = 2.34, ThermalConductivity_W_mK = 27.4,
            CrustalAbundance_ppm = 10, SeawaterConcentration_mg_L = 4.44,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Carbon", Symbol = "C", AtomicNumber = 6, AtomicMass = 12.011,
            ElementType = ElementType.Nonmetal, Group = 14, Period = 2,
            ElectronConfiguration = "[He] 2s² 2p²", ValenceElectrons = 4,
            Electronegativity = 2.55, OxidationStates = new List<int> { -4, -3, -2, -1, 0, 1, 2, 3, 4 },
            FirstIonizationEnergy_kJ_mol = 1086.5, ElectronAffinity_kJ_mol = 121.9,
            CovalentRadius_pm = 76, VanDerWaalsRadius_pm = 170,
            MeltingPoint_K = 3823, Density_g_cm3 = 2.267, ThermalConductivity_W_mK = 129,
            CrustalAbundance_ppm = 200, SeawaterConcentration_mg_L = 28.0,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Nitrogen", Symbol = "N", AtomicNumber = 7, AtomicMass = 14.007,
            ElementType = ElementType.Nonmetal, Group = 15, Period = 2,
            ElectronConfiguration = "[He] 2s² 2p³", ValenceElectrons = 5,
            Electronegativity = 3.04, OxidationStates = new List<int> { -3, -2, -1, 1, 2, 3, 4, 5 },
            FirstIonizationEnergy_kJ_mol = 1402.3, ElectronAffinity_kJ_mol = 7.0,
            CovalentRadius_pm = 71, VanDerWaalsRadius_pm = 155,
            MeltingPoint_K = 63.15, BoilingPoint_K = 77.355,
            CrustalAbundance_ppm = 19, SeawaterConcentration_mg_L = 0.5,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Oxygen", Symbol = "O", AtomicNumber = 8, AtomicMass = 15.999,
            ElementType = ElementType.Nonmetal, Group = 16, Period = 2,
            ElectronConfiguration = "[He] 2s² 2p⁴", ValenceElectrons = 6,
            Electronegativity = 3.44, OxidationStates = new List<int> { -2, -1, 0, 1, 2 },
            FirstIonizationEnergy_kJ_mol = 1313.9, ElectronAffinity_kJ_mol = 141.0,
            CovalentRadius_pm = 66, VanDerWaalsRadius_pm = 152,
            IonicRadii = new Dictionary<int, int> { { -2, 140 } },
            MeltingPoint_K = 54.36, BoilingPoint_K = 90.188,
            CrustalAbundance_ppm = 461000, SeawaterConcentration_mg_L = 857000.0,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Fluorine", Symbol = "F", AtomicNumber = 9, AtomicMass = 18.998,
            ElementType = ElementType.Halogen, Group = 17, Period = 2,
            ElectronConfiguration = "[He] 2s² 2p⁵", ValenceElectrons = 7,
            Electronegativity = 3.98, OxidationStates = new List<int> { -1 },
            FirstIonizationEnergy_kJ_mol = 1681.0, ElectronAffinity_kJ_mol = 328.0,
            CovalentRadius_pm = 57, VanDerWaalsRadius_pm = 147,
            IonicRadii = new Dictionary<int, int> { { -1, 133 } },
            MeltingPoint_K = 53.48, BoilingPoint_K = 85.03,
            CrustalAbundance_ppm = 585, SeawaterConcentration_mg_L = 1.3,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Neon", Symbol = "Ne", AtomicNumber = 10, AtomicMass = 20.180,
            ElementType = ElementType.NobleGas, Group = 18, Period = 2,
            ElectronConfiguration = "[He] 2s² 2p⁶", ValenceElectrons = 0,
            FirstIonizationEnergy_kJ_mol = 2080.7,
            CovalentRadius_pm = 58, VanDerWaalsRadius_pm = 154,
            MeltingPoint_K = 24.56, BoilingPoint_K = 27.104, Density_g_cm3 = 0.0009,
            CrustalAbundance_ppm = 0.005, SeawaterConcentration_mg_L = 0.00012,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        // ═══════════════════════════════════════════════════════════════════════
        // PERIOD 3
        // ═══════════════════════════════════════════════════════════════════════

        _elements.Add(new Element
        {
            Name = "Sodium", Symbol = "Na", AtomicNumber = 11, AtomicMass = 22.990,
            ElementType = ElementType.AlkaliMetal, Group = 1, Period = 3,
            ElectronConfiguration = "[Ne] 3s¹", ValenceElectrons = 1,
            Electronegativity = 0.93, OxidationStates = new List<int> { 1 },
            FirstIonizationEnergy_kJ_mol = 495.8, ElectronAffinity_kJ_mol = 52.8,
            AtomicRadius_pm = 186, CovalentRadius_pm = 166, IonicRadii = new Dictionary<int, int> { { 1, 102 } },
            MeltingPoint_K = 370.944, BoilingPoint_K = 1156.09, Density_g_cm3 = 0.968, ThermalConductivity_W_mK = 142,
            CrustalAbundance_ppm = 23600, SeawaterConcentration_mg_L = 10800.0,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Magnesium", Symbol = "Mg", AtomicNumber = 12, AtomicMass = 24.305,
            ElementType = ElementType.AlkalineEarthMetal, Group = 2, Period = 3,
            ElectronConfiguration = "[Ne] 3s²", ValenceElectrons = 2,
            Electronegativity = 1.31, OxidationStates = new List<int> { 2 },
            FirstIonizationEnergy_kJ_mol = 737.7, ElectronAffinity_kJ_mol = 0.0,
            AtomicRadius_pm = 160, CovalentRadius_pm = 141, IonicRadii = new Dictionary<int, int> { { 2, 72 } },
            MeltingPoint_K = 923, BoilingPoint_K = 1363, Density_g_cm3 = 1.738, ThermalConductivity_W_mK = 156,
            CrustalAbundance_ppm = 23300, SeawaterConcentration_mg_L = 1290.0,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Aluminum", Symbol = "Al", AtomicNumber = 13, AtomicMass = 26.982,
            ElementType = ElementType.PostTransitionMetal, Group = 13, Period = 3,
            ElectronConfiguration = "[Ne] 3s² 3p¹", ValenceElectrons = 3,
            Electronegativity = 1.61, OxidationStates = new List<int> { 3 },
            FirstIonizationEnergy_kJ_mol = 577.5, ElectronAffinity_kJ_mol = 42.5,
            AtomicRadius_pm = 143, CovalentRadius_pm = 121, IonicRadii = new Dictionary<int, int> { { 3, 54 } },
            MeltingPoint_K = 933.47, BoilingPoint_K = 2743, Density_g_cm3 = 2.70, ThermalConductivity_W_mK = 237,
            CrustalAbundance_ppm = 82300, SeawaterConcentration_mg_L = 0.001,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Silicon", Symbol = "Si", AtomicNumber = 14, AtomicMass = 28.085,
            ElementType = ElementType.Metalloid, Group = 14, Period = 3,
            ElectronConfiguration = "[Ne] 3s² 3p²", ValenceElectrons = 4,
            Electronegativity = 1.90, OxidationStates = new List<int> { -4, 2, 4 },
            FirstIonizationEnergy_kJ_mol = 786.5, ElectronAffinity_kJ_mol = 133.6,
            AtomicRadius_pm = 118, CovalentRadius_pm = 111, IonicRadii = new Dictionary<int, int> { { 4, 40 } },
            MeltingPoint_K = 1687, BoilingPoint_K = 3538, Density_g_cm3 = 2.329, ThermalConductivity_W_mK = 148,
            CrustalAbundance_ppm = 282000, SeawaterConcentration_mg_L = 2.9,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Phosphorus", Symbol = "P", AtomicNumber = 15, AtomicMass = 30.974,
            ElementType = ElementType.Nonmetal, Group = 15, Period = 3,
            ElectronConfiguration = "[Ne] 3s² 3p³", ValenceElectrons = 5,
            Electronegativity = 2.19, OxidationStates = new List<int> { -3, 3, 5 },
            FirstIonizationEnergy_kJ_mol = 1011.8, ElectronAffinity_kJ_mol = 72.0,
            CovalentRadius_pm = 107, VanDerWaalsRadius_pm = 180,
            MeltingPoint_K = 317.3, BoilingPoint_K = 553, Density_g_cm3 = 1.823,
            CrustalAbundance_ppm = 1050, SeawaterConcentration_mg_L = 0.088,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Sulfur", Symbol = "S", AtomicNumber = 16, AtomicMass = 32.06,
            ElementType = ElementType.Nonmetal, Group = 16, Period = 3,
            ElectronConfiguration = "[Ne] 3s² 3p⁴", ValenceElectrons = 6,
            Electronegativity = 2.58, OxidationStates = new List<int> { -2, 0, 2, 4, 6 },
            FirstIonizationEnergy_kJ_mol = 999.6, ElectronAffinity_kJ_mol = 200.0,
            CovalentRadius_pm = 105, VanDerWaalsRadius_pm = 180,
            IonicRadii = new Dictionary<int, int> { { -2, 184 }, { 6, 29 } },
            MeltingPoint_K = 388.36, BoilingPoint_K = 717.8, Density_g_cm3 = 2.07,
            CrustalAbundance_ppm = 350, SeawaterConcentration_mg_L = 904.0,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Chlorine", Symbol = "Cl", AtomicNumber = 17, AtomicMass = 35.45,
            ElementType = ElementType.Halogen, Group = 17, Period = 3,
            ElectronConfiguration = "[Ne] 3s² 3p⁵", ValenceElectrons = 7,
            Electronegativity = 3.16, OxidationStates = new List<int> { -1, 1, 3, 5, 7 },
            FirstIonizationEnergy_kJ_mol = 1251.2, ElectronAffinity_kJ_mol = 349.0,
            CovalentRadius_pm = 102, VanDerWaalsRadius_pm = 175, IonicRadii = new Dictionary<int, int> { { -1, 181 } },
            MeltingPoint_K = 171.6, BoilingPoint_K = 239.11,
            CrustalAbundance_ppm = 145, SeawaterConcentration_mg_L = 19400.0,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Argon", Symbol = "Ar", AtomicNumber = 18, AtomicMass = 39.948,
            ElementType = ElementType.NobleGas, Group = 18, Period = 3,
            ElectronConfiguration = "[Ne] 3s² 3p⁶", ValenceElectrons = 0,
            FirstIonizationEnergy_kJ_mol = 1520.6,
            CovalentRadius_pm = 106, VanDerWaalsRadius_pm = 188,
            MeltingPoint_K = 83.81, BoilingPoint_K = 87.302, Density_g_cm3 = 0.001784,
            CrustalAbundance_ppm = 3.5, SeawaterConcentration_mg_L = 0.45,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        // ═══════════════════════════════════════════════════════════════════════
        // PERIOD 4
        // ═══════════════════════════════════════════════════════════════════════

        _elements.Add(new Element
        {
            Name = "Potassium", Symbol = "K", AtomicNumber = 19, AtomicMass = 39.098,
            ElementType = ElementType.AlkaliMetal, Group = 1, Period = 4,
            ElectronConfiguration = "[Ar] 4s¹", ValenceElectrons = 1,
            Electronegativity = 0.82, OxidationStates = new List<int> { 1 },
            FirstIonizationEnergy_kJ_mol = 418.8, ElectronAffinity_kJ_mol = 48.4,
            AtomicRadius_pm = 227, CovalentRadius_pm = 203, IonicRadii = new Dictionary<int, int> { { 1, 138 } },
            MeltingPoint_K = 336.7, BoilingPoint_K = 1032, Density_g_cm3 = 0.862, ThermalConductivity_W_mK = 102,
            CrustalAbundance_ppm = 20900, SeawaterConcentration_mg_L = 399.0,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Calcium", Symbol = "Ca", AtomicNumber = 20, AtomicMass = 40.078,
            ElementType = ElementType.AlkalineEarthMetal, Group = 2, Period = 4,
            ElectronConfiguration = "[Ar] 4s²", ValenceElectrons = 2,
            Electronegativity = 1.00, OxidationStates = new List<int> { 2 },
            FirstIonizationEnergy_kJ_mol = 589.8, ElectronAffinity_kJ_mol = 2.37,
            AtomicRadius_pm = 197, CovalentRadius_pm = 176, IonicRadii = new Dictionary<int, int> { { 2, 100 } },
            MeltingPoint_K = 1115, BoilingPoint_K = 1757, Density_g_cm3 = 1.550, ThermalConductivity_W_mK = 201,
            CrustalAbundance_ppm = 41500, SeawaterConcentration_mg_L = 411.0,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Scandium", Symbol = "Sc", AtomicNumber = 21, AtomicMass = 44.956,
            ElementType = ElementType.TransitionMetal, Group = 3, Period = 4,
            ElectronConfiguration = "[Ar] 3d¹ 4s²", ValenceElectrons = 2,
            Electronegativity = 1.36, OxidationStates = new List<int> { 3 },
            FirstIonizationEnergy_kJ_mol = 633.1, AtomicRadius_pm = 162, CovalentRadius_pm = 170,
            IonicRadii = new Dictionary<int, int> { { 3, 75 } },
            MeltingPoint_K = 1814, BoilingPoint_K = 3109, Density_g_cm3 = 2.985, ThermalConductivity_W_mK = 15.8,
            CrustalAbundance_ppm = 22, SeawaterConcentration_mg_L = 0.000004,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Titanium", Symbol = "Ti", AtomicNumber = 22, AtomicMass = 47.867,
            ElementType = ElementType.TransitionMetal, Group = 4, Period = 4,
            ElectronConfiguration = "[Ar] 3d² 4s²", ValenceElectrons = 2,
            Electronegativity = 1.54, OxidationStates = new List<int> { 2, 3, 4 },
            FirstIonizationEnergy_kJ_mol = 658.8, ElectronAffinity_kJ_mol = 7.6,
            AtomicRadius_pm = 147, CovalentRadius_pm = 160,
            IonicRadii = new Dictionary<int, int> { { 4, 61 }, { 3, 67 } },
            MeltingPoint_K = 1941, BoilingPoint_K = 3560, Density_g_cm3 = 4.506, ThermalConductivity_W_mK = 21.9,
            CrustalAbundance_ppm = 5650, SeawaterConcentration_mg_L = 0.001,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Vanadium", Symbol = "V", AtomicNumber = 23, AtomicMass = 50.942,
            ElementType = ElementType.TransitionMetal, Group = 5, Period = 4,
            ElectronConfiguration = "[Ar] 3d³ 4s²", ValenceElectrons = 2,
            Electronegativity = 1.63, OxidationStates = new List<int> { 2, 3, 4, 5 },
            FirstIonizationEnergy_kJ_mol = 650.9, ElectronAffinity_kJ_mol = 50.6,
            AtomicRadius_pm = 134, CovalentRadius_pm = 153,
            IonicRadii = new Dictionary<int, int> { { 5, 54 }, { 4, 58 } },
            MeltingPoint_K = 2183, BoilingPoint_K = 3680, Density_g_cm3 = 6.0, ThermalConductivity_W_mK = 30.7,
            CrustalAbundance_ppm = 120, SeawaterConcentration_mg_L = 0.0019,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Chromium", Symbol = "Cr", AtomicNumber = 24, AtomicMass = 51.996,
            ElementType = ElementType.TransitionMetal, Group = 6, Period = 4,
            ElectronConfiguration = "[Ar] 3d⁵ 4s¹", ValenceElectrons = 1,
            Electronegativity = 1.66, OxidationStates = new List<int> { 2, 3, 6 },
            FirstIonizationEnergy_kJ_mol = 652.9, ElectronAffinity_kJ_mol = 64.3,
            AtomicRadius_pm = 128, CovalentRadius_pm = 139,
            IonicRadii = new Dictionary<int, int> { { 3, 62 }, { 6, 44 } },
            MeltingPoint_K = 2180, BoilingPoint_K = 2944, Density_g_cm3 = 7.15, ThermalConductivity_W_mK = 93.9,
            CrustalAbundance_ppm = 102, SeawaterConcentration_mg_L = 0.0003,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Manganese", Symbol = "Mn", AtomicNumber = 25, AtomicMass = 54.938,
            ElementType = ElementType.TransitionMetal, Group = 7, Period = 4,
            ElectronConfiguration = "[Ar] 3d⁵ 4s²", ValenceElectrons = 2,
            Electronegativity = 1.55, OxidationStates = new List<int> { 2, 3, 4, 6, 7 },
            FirstIonizationEnergy_kJ_mol = 717.3, AtomicRadius_pm = 127, CovalentRadius_pm = 139,
            IonicRadii = new Dictionary<int, int> { { 2, 83 }, { 4, 53 } },
            MeltingPoint_K = 1519, BoilingPoint_K = 2334, Density_g_cm3 = 7.21, ThermalConductivity_W_mK = 7.81,
            CrustalAbundance_ppm = 950, SeawaterConcentration_mg_L = 0.0004,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Iron", Symbol = "Fe", AtomicNumber = 26, AtomicMass = 55.845,
            ElementType = ElementType.TransitionMetal, Group = 8, Period = 4,
            ElectronConfiguration = "[Ar] 3d⁶ 4s²", ValenceElectrons = 2,
            Electronegativity = 1.83, OxidationStates = new List<int> { -2, -1, 1, 2, 3, 4, 5, 6 },
            FirstIonizationEnergy_kJ_mol = 762.5, ElectronAffinity_kJ_mol = 15.7,
            AtomicRadius_pm = 126, CovalentRadius_pm = 132,
            IonicRadii = new Dictionary<int, int> { { 2, 78 }, { 3, 65 } },
            MeltingPoint_K = 1811, BoilingPoint_K = 3134, Density_g_cm3 = 7.874, ThermalConductivity_W_mK = 80.4,
            CrustalAbundance_ppm = 56300, SeawaterConcentration_mg_L = 0.001,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Cobalt", Symbol = "Co", AtomicNumber = 27, AtomicMass = 58.933,
            ElementType = ElementType.TransitionMetal, Group = 9, Period = 4,
            ElectronConfiguration = "[Ar] 3d⁷ 4s²", ValenceElectrons = 2,
            Electronegativity = 1.88, OxidationStates = new List<int> { 2, 3 },
            FirstIonizationEnergy_kJ_mol = 760.4, ElectronAffinity_kJ_mol = 63.7,
            AtomicRadius_pm = 125, CovalentRadius_pm = 126,
            IonicRadii = new Dictionary<int, int> { { 2, 75 }, { 3, 61 } },
            MeltingPoint_K = 1768, BoilingPoint_K = 3200, Density_g_cm3 = 8.86, ThermalConductivity_W_mK = 100,
            CrustalAbundance_ppm = 25, SeawaterConcentration_mg_L = 0.00002,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Nickel", Symbol = "Ni", AtomicNumber = 28, AtomicMass = 58.693,
            ElementType = ElementType.TransitionMetal, Group = 10, Period = 4,
            ElectronConfiguration = "[Ar] 3d⁸ 4s²", ValenceElectrons = 2,
            Electronegativity = 1.91, OxidationStates = new List<int> { 2, 3 },
            FirstIonizationEnergy_kJ_mol = 737.1, ElectronAffinity_kJ_mol = 112.0,
            AtomicRadius_pm = 124, CovalentRadius_pm = 124, IonicRadii = new Dictionary<int, int> { { 2, 69 } },
            MeltingPoint_K = 1728, BoilingPoint_K = 3186, Density_g_cm3 = 8.912, ThermalConductivity_W_mK = 90.9,
            CrustalAbundance_ppm = 84, SeawaterConcentration_mg_L = 0.0056,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Copper", Symbol = "Cu", AtomicNumber = 29, AtomicMass = 63.546,
            ElementType = ElementType.TransitionMetal, Group = 11, Period = 4,
            ElectronConfiguration = "[Ar] 3d¹⁰ 4s¹", ValenceElectrons = 1,
            Electronegativity = 1.90, OxidationStates = new List<int> { 1, 2 },
            FirstIonizationEnergy_kJ_mol = 745.5, ElectronAffinity_kJ_mol = 118.4,
            AtomicRadius_pm = 128, CovalentRadius_pm = 132,
            IonicRadii = new Dictionary<int, int> { { 1, 77 }, { 2, 73 } },
            MeltingPoint_K = 1357.77, BoilingPoint_K = 2835, Density_g_cm3 = 8.96, ThermalConductivity_W_mK = 401,
            CrustalAbundance_ppm = 60, SeawaterConcentration_mg_L = 0.00025,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Zinc", Symbol = "Zn", AtomicNumber = 30, AtomicMass = 65.38,
            ElementType = ElementType.TransitionMetal, Group = 12, Period = 4,
            ElectronConfiguration = "[Ar] 3d¹⁰ 4s²", ValenceElectrons = 2,
            Electronegativity = 1.65, OxidationStates = new List<int> { 2 },
            FirstIonizationEnergy_kJ_mol = 906.4, AtomicRadius_pm = 134, CovalentRadius_pm = 122,
            IonicRadii = new Dictionary<int, int> { { 2, 74 } },
            MeltingPoint_K = 692.68, BoilingPoint_K = 1180, Density_g_cm3 = 7.134, ThermalConductivity_W_mK = 116,
            CrustalAbundance_ppm = 70, SeawaterConcentration_mg_L = 0.0049,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Gallium", Symbol = "Ga", AtomicNumber = 31, AtomicMass = 69.723,
            ElementType = ElementType.PostTransitionMetal, Group = 13, Period = 4,
            ElectronConfiguration = "[Ar] 3d¹⁰ 4s² 4p¹", ValenceElectrons = 3,
            Electronegativity = 1.81, OxidationStates = new List<int> { 3 },
            FirstIonizationEnergy_kJ_mol = 578.8, ElectronAffinity_kJ_mol = 41.0,
            AtomicRadius_pm = 135, CovalentRadius_pm = 122, IonicRadii = new Dictionary<int, int> { { 3, 62 } },
            MeltingPoint_K = 302.91, BoilingPoint_K = 2477, Density_g_cm3 = 5.91, ThermalConductivity_W_mK = 40.6,
            CrustalAbundance_ppm = 19, SeawaterConcentration_mg_L = 0.00003,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Germanium", Symbol = "Ge", AtomicNumber = 32, AtomicMass = 72.630,
            ElementType = ElementType.Metalloid, Group = 14, Period = 4,
            ElectronConfiguration = "[Ar] 3d¹⁰ 4s² 4p²", ValenceElectrons = 4,
            Electronegativity = 2.01, OxidationStates = new List<int> { 2, 4 },
            FirstIonizationEnergy_kJ_mol = 762.0, ElectronAffinity_kJ_mol = 119.0,
            AtomicRadius_pm = 125, CovalentRadius_pm = 120,
            MeltingPoint_K = 1211.40, BoilingPoint_K = 3106, Density_g_cm3 = 5.323, ThermalConductivity_W_mK = 60.2,
            CrustalAbundance_ppm = 1.5, SeawaterConcentration_mg_L = 0.00006,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Arsenic", Symbol = "As", AtomicNumber = 33, AtomicMass = 74.922,
            ElementType = ElementType.Metalloid, Group = 15, Period = 4,
            ElectronConfiguration = "[Ar] 3d¹⁰ 4s² 4p³", ValenceElectrons = 5,
            Electronegativity = 2.18, OxidationStates = new List<int> { -3, 3, 5 },
            FirstIonizationEnergy_kJ_mol = 947.0, ElectronAffinity_kJ_mol = 78.0,
            AtomicRadius_pm = 115, CovalentRadius_pm = 119, VanDerWaalsRadius_pm = 185,
            MeltingPoint_K = 1090, Density_g_cm3 = 5.727,
            CrustalAbundance_ppm = 1.8, SeawaterConcentration_mg_L = 0.0037,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Selenium", Symbol = "Se", AtomicNumber = 34, AtomicMass = 78.971,
            ElementType = ElementType.Nonmetal, Group = 16, Period = 4,
            ElectronConfiguration = "[Ar] 3d¹⁰ 4s² 4p⁴", ValenceElectrons = 6,
            Electronegativity = 2.55, OxidationStates = new List<int> { -2, 2, 4, 6 },
            FirstIonizationEnergy_kJ_mol = 941.0, ElectronAffinity_kJ_mol = 195.0,
            CovalentRadius_pm = 120, VanDerWaalsRadius_pm = 190,
            MeltingPoint_K = 494, BoilingPoint_K = 958, Density_g_cm3 = 4.809,
            CrustalAbundance_ppm = 0.05, SeawaterConcentration_mg_L = 0.00009,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Bromine", Symbol = "Br", AtomicNumber = 35, AtomicMass = 79.904,
            ElementType = ElementType.Halogen, Group = 17, Period = 4,
            ElectronConfiguration = "[Ar] 3d¹⁰ 4s² 4p⁵", ValenceElectrons = 7,
            Electronegativity = 2.96, OxidationStates = new List<int> { -1, 1, 3, 5, 7 },
            FirstIonizationEnergy_kJ_mol = 1139.9, ElectronAffinity_kJ_mol = 324.6,
            CovalentRadius_pm = 120, VanDerWaalsRadius_pm = 185, IonicRadii = new Dictionary<int, int> { { -1, 196 } },
            MeltingPoint_K = 265.8, BoilingPoint_K = 332.0, Density_g_cm3 = 3.1028,
            CrustalAbundance_ppm = 2.4, SeawaterConcentration_mg_L = 67.3,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Krypton", Symbol = "Kr", AtomicNumber = 36, AtomicMass = 83.798,
            ElementType = ElementType.NobleGas, Group = 18, Period = 4,
            ElectronConfiguration = "[Ar] 3d¹⁰ 4s² 4p⁶", ValenceElectrons = 0,
            FirstIonizationEnergy_kJ_mol = 1350.8,
            CovalentRadius_pm = 116, VanDerWaalsRadius_pm = 202,
            MeltingPoint_K = 115.78, BoilingPoint_K = 119.93, Density_g_cm3 = 0.003733,
            CrustalAbundance_ppm = 0.0001, SeawaterConcentration_mg_L = 0.00021,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        // ═══════════════════════════════════════════════════════════════════════
        // PERIOD 5 - Adding all elements 37-54
        // ═══════════════════════════════════════════════════════════════════════

        _elements.Add(new Element
        {
            Name = "Rubidium", Symbol = "Rb", AtomicNumber = 37, AtomicMass = 85.468,
            ElementType = ElementType.AlkaliMetal, Group = 1, Period = 5,
            ElectronConfiguration = "[Kr] 5s¹", ValenceElectrons = 1,
            Electronegativity = 0.82, OxidationStates = new List<int> { 1 },
            FirstIonizationEnergy_kJ_mol = 403.0, ElectronAffinity_kJ_mol = 46.9,
            AtomicRadius_pm = 248, CovalentRadius_pm = 220, IonicRadii = new Dictionary<int, int> { { 1, 152 } },
            MeltingPoint_K = 312.45, BoilingPoint_K = 961, Density_g_cm3 = 1.532, ThermalConductivity_W_mK = 58.2,
            CrustalAbundance_ppm = 90, SeawaterConcentration_mg_L = 0.12,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Strontium", Symbol = "Sr", AtomicNumber = 38, AtomicMass = 87.62,
            ElementType = ElementType.AlkalineEarthMetal, Group = 2, Period = 5,
            ElectronConfiguration = "[Kr] 5s²", ValenceElectrons = 2,
            Electronegativity = 0.95, OxidationStates = new List<int> { 2 },
            FirstIonizationEnergy_kJ_mol = 549.5, ElectronAffinity_kJ_mol = 5.03,
            AtomicRadius_pm = 215, CovalentRadius_pm = 195, IonicRadii = new Dictionary<int, int> { { 2, 118 } },
            MeltingPoint_K = 1050, BoilingPoint_K = 1655, Density_g_cm3 = 2.630, ThermalConductivity_W_mK = 35.4,
            CrustalAbundance_ppm = 370, SeawaterConcentration_mg_L = 7.9,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Yttrium", Symbol = "Y", AtomicNumber = 39, AtomicMass = 88.906,
            ElementType = ElementType.TransitionMetal, Group = 3, Period = 5,
            ElectronConfiguration = "[Kr] 4d¹ 5s²", ValenceElectrons = 2,
            Electronegativity = 1.22, OxidationStates = new List<int> { 3 },
            FirstIonizationEnergy_kJ_mol = 600.0, AtomicRadius_pm = 180, CovalentRadius_pm = 190,
            IonicRadii = new Dictionary<int, int> { { 3, 90 } },
            MeltingPoint_K = 1799, BoilingPoint_K = 3609, Density_g_cm3 = 4.469, ThermalConductivity_W_mK = 17.2,
            CrustalAbundance_ppm = 33, SeawaterConcentration_mg_L = 0.000013,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Zirconium", Symbol = "Zr", AtomicNumber = 40, AtomicMass = 91.224,
            ElementType = ElementType.TransitionMetal, Group = 4, Period = 5,
            ElectronConfiguration = "[Kr] 4d² 5s²", ValenceElectrons = 2,
            Electronegativity = 1.33, OxidationStates = new List<int> { 4 },
            FirstIonizationEnergy_kJ_mol = 640.1, ElectronAffinity_kJ_mol = 41.1,
            AtomicRadius_pm = 160, CovalentRadius_pm = 175, IonicRadii = new Dictionary<int, int> { { 4, 72 } },
            MeltingPoint_K = 2128, BoilingPoint_K = 4682, Density_g_cm3 = 6.506, ThermalConductivity_W_mK = 22.6,
            CrustalAbundance_ppm = 165, SeawaterConcentration_mg_L = 0.000026,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Niobium", Symbol = "Nb", AtomicNumber = 41, AtomicMass = 92.906,
            ElementType = ElementType.TransitionMetal, Group = 5, Period = 5,
            ElectronConfiguration = "[Kr] 4d⁴ 5s¹", ValenceElectrons = 1,
            Electronegativity = 1.6, OxidationStates = new List<int> { 3, 5 },
            FirstIonizationEnergy_kJ_mol = 652.1, ElectronAffinity_kJ_mol = 86.1,
            AtomicRadius_pm = 146, CovalentRadius_pm = 164, IonicRadii = new Dictionary<int, int> { { 5, 64 } },
            MeltingPoint_K = 2750, BoilingPoint_K = 5017, Density_g_cm3 = 8.57, ThermalConductivity_W_mK = 53.7,
            CrustalAbundance_ppm = 20, SeawaterConcentration_mg_L = 0.000001,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Molybdenum", Symbol = "Mo", AtomicNumber = 42, AtomicMass = 95.95,
            ElementType = ElementType.TransitionMetal, Group = 6, Period = 5,
            ElectronConfiguration = "[Kr] 4d⁵ 5s¹", ValenceElectrons = 1,
            Electronegativity = 2.16, OxidationStates = new List<int> { 2, 3, 4, 5, 6 },
            FirstIonizationEnergy_kJ_mol = 684.3, ElectronAffinity_kJ_mol = 71.9,
            AtomicRadius_pm = 139, CovalentRadius_pm = 154, IonicRadii = new Dictionary<int, int> { { 6, 59 } },
            MeltingPoint_K = 2896, BoilingPoint_K = 4912, Density_g_cm3 = 10.28, ThermalConductivity_W_mK = 138,
            CrustalAbundance_ppm = 1.2, SeawaterConcentration_mg_L = 0.01,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Technetium", Symbol = "Tc", AtomicNumber = 43, AtomicMass = 98,
            ElementType = ElementType.TransitionMetal, Group = 7, Period = 5,
            ElectronConfiguration = "[Kr] 4d⁵ 5s²", ValenceElectrons = 2,
            Electronegativity = 1.9, OxidationStates = new List<int> { 4, 6, 7 },
            FirstIonizationEnergy_kJ_mol = 702.0, AtomicRadius_pm = 136, CovalentRadius_pm = 147,
            MeltingPoint_K = 2430, BoilingPoint_K = 4538, Density_g_cm3 = 11.0,
            Sources = new List<string> { "IUPAC", "CRC (synthetic)" }
        });

        _elements.Add(new Element
        {
            Name = "Ruthenium", Symbol = "Ru", AtomicNumber = 44, AtomicMass = 101.07,
            ElementType = ElementType.TransitionMetal, Group = 8, Period = 5,
            ElectronConfiguration = "[Kr] 4d⁷ 5s¹", ValenceElectrons = 1,
            Electronegativity = 2.2, OxidationStates = new List<int> { 2, 3, 4, 6, 8 },
            FirstIonizationEnergy_kJ_mol = 710.2, ElectronAffinity_kJ_mol = 101.3,
            AtomicRadius_pm = 134, CovalentRadius_pm = 146,
            MeltingPoint_K = 2607, BoilingPoint_K = 4423, Density_g_cm3 = 12.37, ThermalConductivity_W_mK = 117,
            CrustalAbundance_ppm = 0.001,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Rhodium", Symbol = "Rh", AtomicNumber = 45, AtomicMass = 102.91,
            ElementType = ElementType.TransitionMetal, Group = 9, Period = 5,
            ElectronConfiguration = "[Kr] 4d⁸ 5s¹", ValenceElectrons = 1,
            Electronegativity = 2.28, OxidationStates = new List<int> { 2, 3, 4 },
            FirstIonizationEnergy_kJ_mol = 719.7, ElectronAffinity_kJ_mol = 110.0,
            AtomicRadius_pm = 134, CovalentRadius_pm = 142,
            MeltingPoint_K = 2237, BoilingPoint_K = 3968, Density_g_cm3 = 12.41, ThermalConductivity_W_mK = 150,
            CrustalAbundance_ppm = 0.001,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Palladium", Symbol = "Pd", AtomicNumber = 46, AtomicMass = 106.42,
            ElementType = ElementType.TransitionMetal, Group = 10, Period = 5,
            ElectronConfiguration = "[Kr] 4d¹⁰", ValenceElectrons = 0,
            Electronegativity = 2.20, OxidationStates = new List<int> { 2, 4 },
            FirstIonizationEnergy_kJ_mol = 804.4, ElectronAffinity_kJ_mol = 54.24,
            AtomicRadius_pm = 137, CovalentRadius_pm = 139, IonicRadii = new Dictionary<int, int> { { 2, 86 } },
            MeltingPoint_K = 1828.05, BoilingPoint_K = 3236, Density_g_cm3 = 12.023, ThermalConductivity_W_mK = 71.8,
            CrustalAbundance_ppm = 0.015,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Silver", Symbol = "Ag", AtomicNumber = 47, AtomicMass = 107.87,
            ElementType = ElementType.TransitionMetal, Group = 11, Period = 5,
            ElectronConfiguration = "[Kr] 4d¹⁰ 5s¹", ValenceElectrons = 1,
            Electronegativity = 1.93, OxidationStates = new List<int> { 1 },
            FirstIonizationEnergy_kJ_mol = 731.0, ElectronAffinity_kJ_mol = 125.6,
            AtomicRadius_pm = 144, CovalentRadius_pm = 145, IonicRadii = new Dictionary<int, int> { { 1, 115 } },
            MeltingPoint_K = 1234.93, BoilingPoint_K = 2435, Density_g_cm3 = 10.501, ThermalConductivity_W_mK = 429,
            CrustalAbundance_ppm = 0.075, SeawaterConcentration_mg_L = 0.00001,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Cadmium", Symbol = "Cd", AtomicNumber = 48, AtomicMass = 112.41,
            ElementType = ElementType.TransitionMetal, Group = 12, Period = 5,
            ElectronConfiguration = "[Kr] 4d¹⁰ 5s²", ValenceElectrons = 2,
            Electronegativity = 1.69, OxidationStates = new List<int> { 2 },
            FirstIonizationEnergy_kJ_mol = 867.8, AtomicRadius_pm = 151, CovalentRadius_pm = 144,
            IonicRadii = new Dictionary<int, int> { { 2, 95 } },
            MeltingPoint_K = 594.22, BoilingPoint_K = 1040, Density_g_cm3 = 8.69, ThermalConductivity_W_mK = 96.6,
            CrustalAbundance_ppm = 0.15, SeawaterConcentration_mg_L = 0.00011,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Indium", Symbol = "In", AtomicNumber = 49, AtomicMass = 114.82,
            ElementType = ElementType.PostTransitionMetal, Group = 13, Period = 5,
            ElectronConfiguration = "[Kr] 4d¹⁰ 5s² 5p¹", ValenceElectrons = 3,
            Electronegativity = 1.78, OxidationStates = new List<int> { 3 },
            FirstIonizationEnergy_kJ_mol = 558.3, ElectronAffinity_kJ_mol = 37.043,
            AtomicRadius_pm = 167, CovalentRadius_pm = 142, IonicRadii = new Dictionary<int, int> { { 3, 80 } },
            MeltingPoint_K = 429.75, BoilingPoint_K = 2345, Density_g_cm3 = 7.31, ThermalConductivity_W_mK = 81.8,
            CrustalAbundance_ppm = 0.25,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Tin", Symbol = "Sn", AtomicNumber = 50, AtomicMass = 118.71,
            ElementType = ElementType.PostTransitionMetal, Group = 14, Period = 5,
            ElectronConfiguration = "[Kr] 4d¹⁰ 5s² 5p²", ValenceElectrons = 4,
            Electronegativity = 1.96, OxidationStates = new List<int> { 2, 4 },
            FirstIonizationEnergy_kJ_mol = 708.6, ElectronAffinity_kJ_mol = 107.3,
            AtomicRadius_pm = 158, CovalentRadius_pm = 139, IonicRadii = new Dictionary<int, int> { { 4, 69 } },
            MeltingPoint_K = 505.08, BoilingPoint_K = 2875, Density_g_cm3 = 7.265, ThermalConductivity_W_mK = 66.8,
            CrustalAbundance_ppm = 2.3, SeawaterConcentration_mg_L = 0.0004,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Antimony", Symbol = "Sb", AtomicNumber = 51, AtomicMass = 121.76,
            ElementType = ElementType.Metalloid, Group = 15, Period = 5,
            ElectronConfiguration = "[Kr] 4d¹⁰ 5s² 5p³", ValenceElectrons = 5,
            Electronegativity = 2.05, OxidationStates = new List<int> { -3, 3, 5 },
            FirstIonizationEnergy_kJ_mol = 834.0, ElectronAffinity_kJ_mol = 103.2,
            AtomicRadius_pm = 145, CovalentRadius_pm = 139,
            MeltingPoint_K = 903.78, BoilingPoint_K = 1860, Density_g_cm3 = 6.697, ThermalConductivity_W_mK = 24.4,
            CrustalAbundance_ppm = 0.2, SeawaterConcentration_mg_L = 0.00024,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Tellurium", Symbol = "Te", AtomicNumber = 52, AtomicMass = 127.60,
            ElementType = ElementType.Metalloid, Group = 16, Period = 5,
            ElectronConfiguration = "[Kr] 4d¹⁰ 5s² 5p⁴", ValenceElectrons = 6,
            Electronegativity = 2.1, OxidationStates = new List<int> { -2, 2, 4, 6 },
            FirstIonizationEnergy_kJ_mol = 869.3, ElectronAffinity_kJ_mol = 190.2,
            CovalentRadius_pm = 138, VanDerWaalsRadius_pm = 206,
            MeltingPoint_K = 722.66, BoilingPoint_K = 1261, Density_g_cm3 = 6.232,
            CrustalAbundance_ppm = 0.001,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Iodine", Symbol = "I", AtomicNumber = 53, AtomicMass = 126.90,
            ElementType = ElementType.Halogen, Group = 17, Period = 5,
            ElectronConfiguration = "[Kr] 4d¹⁰ 5s² 5p⁵", ValenceElectrons = 7,
            Electronegativity = 2.66, OxidationStates = new List<int> { -1, 1, 3, 5, 7 },
            FirstIonizationEnergy_kJ_mol = 1008.4, ElectronAffinity_kJ_mol = 295.2,
            CovalentRadius_pm = 139, VanDerWaalsRadius_pm = 198, IonicRadii = new Dictionary<int, int> { { -1, 220 } },
            MeltingPoint_K = 386.85, BoilingPoint_K = 457.4, Density_g_cm3 = 4.933,
            CrustalAbundance_ppm = 0.45, SeawaterConcentration_mg_L = 0.064,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Xenon", Symbol = "Xe", AtomicNumber = 54, AtomicMass = 131.29,
            ElementType = ElementType.NobleGas, Group = 18, Period = 5,
            ElectronConfiguration = "[Kr] 4d¹⁰ 5s² 5p⁶", ValenceElectrons = 0,
            FirstIonizationEnergy_kJ_mol = 1170.4,
            CovalentRadius_pm = 140, VanDerWaalsRadius_pm = 216,
            MeltingPoint_K = 161.40, BoilingPoint_K = 165.051, Density_g_cm3 = 0.005894,
            CrustalAbundance_ppm = 0.000003, SeawaterConcentration_mg_L = 0.00005,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        // ═══════════════════════════════════════════════════════════════════════
        // PERIOD 6 - Key elements (55-86)
        // ═══════════════════════════════════════════════════════════════════════

        _elements.Add(new Element
        {
            Name = "Cesium", Symbol = "Cs", AtomicNumber = 55, AtomicMass = 132.91,
            ElementType = ElementType.AlkaliMetal, Group = 1, Period = 6,
            ElectronConfiguration = "[Xe] 6s¹", ValenceElectrons = 1,
            Electronegativity = 0.79, OxidationStates = new List<int> { 1 },
            FirstIonizationEnergy_kJ_mol = 375.7, ElectronAffinity_kJ_mol = 45.5,
            AtomicRadius_pm = 265, CovalentRadius_pm = 244, IonicRadii = new Dictionary<int, int> { { 1, 167 } },
            MeltingPoint_K = 301.7, BoilingPoint_K = 944, Density_g_cm3 = 1.873, ThermalConductivity_W_mK = 35.9,
            CrustalAbundance_ppm = 3, SeawaterConcentration_mg_L = 0.0005,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Barium", Symbol = "Ba", AtomicNumber = 56, AtomicMass = 137.33,
            ElementType = ElementType.AlkalineEarthMetal, Group = 2, Period = 6,
            ElectronConfiguration = "[Xe] 6s²", ValenceElectrons = 2,
            Electronegativity = 0.89, OxidationStates = new List<int> { 2 },
            FirstIonizationEnergy_kJ_mol = 502.9, ElectronAffinity_kJ_mol = 13.95,
            AtomicRadius_pm = 222, CovalentRadius_pm = 215, IonicRadii = new Dictionary<int, int> { { 2, 135 } },
            MeltingPoint_K = 1000, BoilingPoint_K = 2170, Density_g_cm3 = 3.510, ThermalConductivity_W_mK = 18.4,
            CrustalAbundance_ppm = 425, SeawaterConcentration_mg_L = 0.013,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        // Lanthanides (57-71) - abbreviated to save space but including key ones
        _elements.Add(new Element
        {
            Name = "Lanthanum", Symbol = "La", AtomicNumber = 57, AtomicMass = 138.91,
            ElementType = ElementType.Lanthanide, Group = 3, Period = 6,
            ElectronConfiguration = "[Xe] 5d¹ 6s²", ValenceElectrons = 2,
            Electronegativity = 1.10, OxidationStates = new List<int> { 3 },
            FirstIonizationEnergy_kJ_mol = 538.1, AtomicRadius_pm = 195, CovalentRadius_pm = 207,
            IonicRadii = new Dictionary<int, int> { { 3, 103 } },
            MeltingPoint_K = 1193, BoilingPoint_K = 3737, Density_g_cm3 = 6.145, ThermalConductivity_W_mK = 13.4,
            CrustalAbundance_ppm = 39, SeawaterConcentration_mg_L = 0.0000034,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Cerium", Symbol = "Ce", AtomicNumber = 58, AtomicMass = 140.12,
            ElementType = ElementType.Lanthanide, Group = 3, Period = 6,
            ElectronConfiguration = "[Xe] 4f¹ 5d¹ 6s²", ValenceElectrons = 2,
            Electronegativity = 1.12, OxidationStates = new List<int> { 3, 4 },
            FirstIonizationEnergy_kJ_mol = 534.4, IonicRadii = new Dictionary<int, int> { { 3, 102 }, { 4, 87 } },
            MeltingPoint_K = 1068, BoilingPoint_K = 3716, Density_g_cm3 = 6.770, ThermalConductivity_W_mK = 11.3,
            CrustalAbundance_ppm = 66.5, SeawaterConcentration_mg_L = 0.0000012,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Neodymium", Symbol = "Nd", AtomicNumber = 60, AtomicMass = 144.24,
            ElementType = ElementType.Lanthanide, Group = 3, Period = 6,
            ElectronConfiguration = "[Xe] 4f⁴ 6s²", ValenceElectrons = 2,
            Electronegativity = 1.14, OxidationStates = new List<int> { 3 },
            FirstIonizationEnergy_kJ_mol = 533.1, IonicRadii = new Dictionary<int, int> { { 3, 98 } },
            MeltingPoint_K = 1297, BoilingPoint_K = 3347, Density_g_cm3 = 7.007, ThermalConductivity_W_mK = 16.5,
            CrustalAbundance_ppm = 41.5, SeawaterConcentration_mg_L = 0.0000028,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        // Continue with key transition metals in period 6
        _elements.Add(new Element
        {
            Name = "Gold", Symbol = "Au", AtomicNumber = 79, AtomicMass = 196.97,
            ElementType = ElementType.TransitionMetal, Group = 11, Period = 6,
            ElectronConfiguration = "[Xe] 4f¹⁴ 5d¹⁰ 6s¹", ValenceElectrons = 1,
            Electronegativity = 2.54, OxidationStates = new List<int> { 1, 3 },
            FirstIonizationEnergy_kJ_mol = 890.1, ElectronAffinity_kJ_mol = 222.8,
            AtomicRadius_pm = 144, CovalentRadius_pm = 136, IonicRadii = new Dictionary<int, int> { { 1, 137 } },
            MeltingPoint_K = 1337.33, BoilingPoint_K = 3129, Density_g_cm3 = 19.282, ThermalConductivity_W_mK = 318,
            CrustalAbundance_ppm = 0.004, SeawaterConcentration_mg_L = 0.00001,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Mercury", Symbol = "Hg", AtomicNumber = 80, AtomicMass = 200.59,
            ElementType = ElementType.TransitionMetal, Group = 12, Period = 6,
            ElectronConfiguration = "[Xe] 4f¹⁴ 5d¹⁰ 6s²", ValenceElectrons = 2,
            Electronegativity = 2.00, OxidationStates = new List<int> { 1, 2 },
            FirstIonizationEnergy_kJ_mol = 1007.1, AtomicRadius_pm = 151, CovalentRadius_pm = 132,
            IonicRadii = new Dictionary<int, int> { { 2, 102 } },
            MeltingPoint_K = 234.43, BoilingPoint_K = 629.88, Density_g_cm3 = 13.534, ThermalConductivity_W_mK = 8.3,
            CrustalAbundance_ppm = 0.085, SeawaterConcentration_mg_L = 0.00003,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Lead", Symbol = "Pb", AtomicNumber = 82, AtomicMass = 207.2,
            ElementType = ElementType.PostTransitionMetal, Group = 14, Period = 6,
            ElectronConfiguration = "[Xe] 4f¹⁴ 5d¹⁰ 6s² 6p²", ValenceElectrons = 4,
            Electronegativity = 2.33, OxidationStates = new List<int> { 2, 4 },
            FirstIonizationEnergy_kJ_mol = 715.6, ElectronAffinity_kJ_mol = 35.1,
            AtomicRadius_pm = 175, CovalentRadius_pm = 146, IonicRadii = new Dictionary<int, int> { { 2, 119 } },
            MeltingPoint_K = 600.61, BoilingPoint_K = 2022, Density_g_cm3 = 11.342, ThermalConductivity_W_mK = 35.3,
            CrustalAbundance_ppm = 14, SeawaterConcentration_mg_L = 0.00003,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Uranium", Symbol = "U", AtomicNumber = 92, AtomicMass = 238.03,
            ElementType = ElementType.Actinide, Group = 3, Period = 7,
            ElectronConfiguration = "[Rn] 5f³ 6d¹ 7s²", ValenceElectrons = 2,
            Electronegativity = 1.38, OxidationStates = new List<int> { 3, 4, 5, 6 },
            FirstIonizationEnergy_kJ_mol = 597.6, AtomicRadius_pm = 175, CovalentRadius_pm = 196,
            IonicRadii = new Dictionary<int, int> { { 4, 89 }, { 6, 73 } },
            MeltingPoint_K = 1405.3, BoilingPoint_K = 4404, Density_g_cm3 = 19.1, ThermalConductivity_W_mK = 27.5,
            CrustalAbundance_ppm = 2.7, SeawaterConcentration_mg_L = 0.0033,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        _elements.Add(new Element
        {
            Name = "Thorium", Symbol = "Th", AtomicNumber = 90, AtomicMass = 232.04,
            ElementType = ElementType.Actinide, Group = 3, Period = 7,
            ElectronConfiguration = "[Rn] 6d² 7s²", ValenceElectrons = 2,
            Electronegativity = 1.3, OxidationStates = new List<int> { 4 },
            FirstIonizationEnergy_kJ_mol = 587.0, CovalentRadius_pm = 206,
            IonicRadii = new Dictionary<int, int> { { 4, 94 } },
            MeltingPoint_K = 2023, BoilingPoint_K = 5061, Density_g_cm3 = 11.7, ThermalConductivity_W_mK = 54.0,
            CrustalAbundance_ppm = 9.6, SeawaterConcentration_mg_L = 0.00007,
            Sources = new List<string> { "IUPAC", "CRC" }
        });

        Logger.Log($"[CompoundLibrary] Seeded {_elements.Count} elements");
    }
}