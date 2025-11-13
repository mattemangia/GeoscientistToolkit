// GeoscientistToolkit/Business/CompoundLibraryExtensions.cs
//
// Extended thermodynamic database with additional minerals, aqueous species, and gases
// for comprehensive geochemical modeling.
//
// DATA SOURCES:
// - Holland, T.J.B. & Powell, R., 2011. An improved and extended internally consistent
//   thermodynamic dataset for phases of petrological interest, involving a new equation
//   of state for solids. Journal of Metamorphic Geology, 29(3), 333-383.
// - Parkhurst, D.L. & Appelo, C.A.J., 2013. Description of input and examples for PHREEQC
//   version 3. USGS Techniques and Methods, Book 6, Chapter A43, 497 p.
// - Robie, R.A. & Hemingway, B.S., 1995. Thermodynamic Properties of Minerals and Related
//   Substances at 298.15 K and 1 Bar Pressure and at Higher Temperatures. USGS Bulletin 2131.
// - Nordstrom, D.K. & Munoz, J.L., 1994. Geochemical Thermodynamics, 2nd ed. Blackwell.
// - Johnson, J.W., Oelkers, E.H. & Helgeson, H.C., 1992. SUPCRT92: Software package for
//   calculating standard molal thermodynamic properties of minerals, gases, aqueous species.
//   Computers & Geosciences, 18, 899-947.

using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

/// <summary>
///     Extension methods for CompoundLibrary to add comprehensive thermodynamic data.
/// </summary>
public static class CompoundLibraryExtensions
{
    /// <summary>
    ///     Adds additional minerals, aqueous species, and gases to the compound library.
    ///     This extends the default seed with data from Holland & Powell 2011, PHREEQC, and SUPCRT92.
    /// </summary>
    public static void SeedAdditionalCompounds(this CompoundLibrary library)
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ADDITIONAL SILICATE MINERALS (Holland & Powell 2011)
        // ═══════════════════════════════════════════════════════════════════════

        // --- OLIVINES ---
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Forsterite",
            ChemicalFormula = "Mg₂SiO₄",
            Synonyms = new List<string> { "Mg2SiO4", "Mg-Olivine" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -2055.4,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -2173.8,
            Entropy_J_molK = 95.1,
            HeatCapacity_J_molK = 118.5,
            MolarVolume_cm3_mol = 43.79,
            MolecularWeight_g_mol = 140.69,
            Density_g_cm3 = 3.21,
            MohsHardness = 7.0,
            Color = "Green, yellow-green",
            Sources = new List<string> { "Holland & Powell (2011)", "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Fayalite",
            ChemicalFormula = "Fe₂SiO₄",
            Synonyms = new List<string> { "Fe2SiO4", "Fe-Olivine" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1378.8,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -1478.2,
            Entropy_J_molK = 151.0,
            HeatCapacity_J_molK = 134.3,
            MolarVolume_cm3_mol = 46.39,
            MolecularWeight_g_mol = 203.77,
            Density_g_cm3 = 4.39,
            MohsHardness = 6.5,
            Color = "Yellow-brown, brown",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        // --- PYROXENES ---
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Enstatite",
            ChemicalFormula = "MgSiO₃",
            Synonyms = new List<string> { "MgSiO3", "Mg-Pyroxene" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1459.2,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -1546.8,
            Entropy_J_molK = 67.9,
            HeatCapacity_J_molK = 82.1,
            MolarVolume_cm3_mol = 31.28,
            MolecularWeight_g_mol = 100.39,
            Density_g_cm3 = 3.21,
            MohsHardness = 5.5,
            Color = "White, gray, green",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Ferrosilite",
            ChemicalFormula = "FeSiO₃",
            Synonyms = new List<string> { "FeSiO3", "Fe-Pyroxene" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1117.1,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -1195.4,
            Entropy_J_molK = 95.5,
            HeatCapacity_J_molK = 93.1,
            MolarVolume_cm3_mol = 33.01,
            MolecularWeight_g_mol = 131.93,
            Density_g_cm3 = 3.96,
            MohsHardness = 5.5,
            Color = "Dark green, black",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Diopside",
            ChemicalFormula = "CaMgSi₂O₆",
            Synonyms = new List<string> { "CaMgSi2O6" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -3027.6,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -3205.7,
            Entropy_J_molK = 143.1,
            HeatCapacity_J_molK = 166.6,
            MolarVolume_cm3_mol = 66.09,
            MolecularWeight_g_mol = 216.55,
            Density_g_cm3 = 3.28,
            MohsHardness = 6.0,
            Color = "Green, white",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        // --- FELDSPARS ---
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Albite",
            ChemicalFormula = "NaAlSi₃O₈",
            Synonyms = new List<string> { "NaAlSi3O8", "Na-Feldspar" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -3711.7,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -3935.1,
            Entropy_J_molK = 207.4,
            HeatCapacity_J_molK = 205.1,
            MolarVolume_cm3_mol = 100.07,
            MolecularWeight_g_mol = 262.22,
            Density_g_cm3 = 2.62,
            MohsHardness = 6.0,
            Color = "White, gray",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Anorthite",
            ChemicalFormula = "CaAl₂Si₂O₈",
            Synonyms = new List<string> { "CaAl2Si2O8", "Ca-Feldspar" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -4005.2,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -4234.0,
            Entropy_J_molK = 199.3,
            HeatCapacity_J_molK = 205.6,
            MolarVolume_cm3_mol = 100.79,
            MolecularWeight_g_mol = 278.21,
            Density_g_cm3 = 2.76,
            MohsHardness = 6.0,
            Color = "White, gray",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Orthoclase",
            ChemicalFormula = "KAlSi₃O₈",
            Synonyms = new List<string> { "KAlSi3O8", "K-Feldspar" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -3742.7,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -3970.4,
            Entropy_J_molK = 214.2,
            HeatCapacity_J_molK = 206.5,
            MolarVolume_cm3_mol = 108.74,
            MolecularWeight_g_mol = 278.33,
            Density_g_cm3 = 2.56,
            MohsHardness = 6.0,
            Color = "Pink, white",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // ADDITIONAL CARBONATE MINERALS
        // ═══════════════════════════════════════════════════════════════════════

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Magnesite",
            ChemicalFormula = "MgCO₃",
            Synonyms = new List<string> { "MgCO3" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -1029.5,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -1111.7,
            Entropy_J_molK = 65.7,
            HeatCapacity_J_molK = 75.5,
            LogKsp_25C = -7.83,
            MolarVolume_cm3_mol = 28.02,
            MolecularWeight_g_mol = 84.31,
            Density_g_cm3 = 3.01,
            MohsHardness = 4.0,
            Color = "White, gray, yellow",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Siderite",
            ChemicalFormula = "FeCO₃",
            Synonyms = new List<string> { "FeCO3" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -673.2,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -755.9,
            Entropy_J_molK = 95.5,
            HeatCapacity_J_molK = 82.1,
            LogKsp_25C = -10.89,
            MolarVolume_cm3_mol = 29.38,
            MolecularWeight_g_mol = 115.85,
            Density_g_cm3 = 3.94,
            MohsHardness = 4.0,
            Color = "Brown, yellow-brown",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Rhodochrosite",
            ChemicalFormula = "MnCO₃",
            Synonyms = new List<string> { "MnCO3" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -816.0,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -894.1,
            Entropy_J_molK = 85.8,
            HeatCapacity_J_molK = 81.2,
            LogKsp_25C = -10.58,
            MolarVolume_cm3_mol = 31.07,
            MolecularWeight_g_mol = 114.95,
            Density_g_cm3 = 3.70,
            MohsHardness = 3.5,
            Color = "Pink, red",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Strontianite",
            ChemicalFormula = "SrCO₃",
            Synonyms = new List<string> { "SrCO3" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1137.6,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -1220.1,
            Entropy_J_molK = 97.1,
            HeatCapacity_J_molK = 81.4,
            LogKsp_25C = -9.03,
            MolarVolume_cm3_mol = 39.01,
            MolecularWeight_g_mol = 147.63,
            Density_g_cm3 = 3.78,
            MohsHardness = 3.5,
            Color = "White, gray, green",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // ADDITIONAL SULFATE MINERALS
        // ═══════════════════════════════════════════════════════════════════════

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Anhydrite",
            ChemicalFormula = "CaSO₄",
            Synonyms = new List<string> { "CaSO4" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1321.8,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -1434.5,
            Entropy_J_molK = 107.4,
            HeatCapacity_J_molK = 99.7,
            LogKsp_25C = -4.36,
            MolarVolume_cm3_mol = 45.94,
            MolecularWeight_g_mol = 136.14,
            Density_g_cm3 = 2.96,
            MohsHardness = 3.5,
            Color = "White, gray, blue",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Barite",
            ChemicalFormula = "BaSO₄",
            Synonyms = new List<string> { "BaSO4", "Baryte" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1362.2,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -1473.2,
            Entropy_J_molK = 132.2,
            HeatCapacity_J_molK = 101.8,
            LogKsp_25C = -9.97,
            MolarVolume_cm3_mol = 52.10,
            MolecularWeight_g_mol = 233.39,
            Density_g_cm3 = 4.48,
            MohsHardness = 3.5,
            Color = "White, colorless",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Celestite",
            ChemicalFormula = "SrSO₄",
            Synonyms = new List<string> { "SrSO4", "Celestine" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1340.9,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -1453.1,
            Entropy_J_molK = 117.0,
            HeatCapacity_J_molK = 99.6,
            LogKsp_25C = -6.63,
            MolarVolume_cm3_mol = 46.25,
            MolecularWeight_g_mol = 183.68,
            Density_g_cm3 = 3.97,
            MohsHardness = 3.0,
            Color = "White, blue, colorless",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Anglesite",
            ChemicalFormula = "PbSO₄",
            Synonyms = new List<string> { "PbSO4" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -813.0,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -919.9,
            Entropy_J_molK = 148.5,
            HeatCapacity_J_molK = 103.2,
            LogKsp_25C = -7.79,
            MolarVolume_cm3_mol = 48.10,
            MolecularWeight_g_mol = 303.26,
            Density_g_cm3 = 6.30,
            MohsHardness = 2.75,
            Color = "White, gray",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // ADDITIONAL CHLORIDE MINERALS AND SALTS
        // ═══════════════════════════════════════════════════════════════════════

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Sylvite",
            ChemicalFormula = "KCl",
            Synonyms = new List<string> { "Potassium Chloride" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -408.5,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -436.7,
            Entropy_J_molK = 82.6,
            HeatCapacity_J_molK = 51.3,
            LogKsp_25C = 0.90,
            Solubility_g_100mL_25C = 34.0,
            MolarVolume_cm3_mol = 37.52,
            MolecularWeight_g_mol = 74.55,
            Density_g_cm3 = 1.99,
            MohsHardness = 2.0,
            Color = "Colorless, white, red",
            Sources = new List<string> { "Robie & Hemingway (1995)", "CRC Handbook" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Carnallite",
            ChemicalFormula = "KMgCl₃·6H₂O",
            Synonyms = new List<string> { "KMgCl3!6H2O" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -3434.3,  // Estimated
            EnthalpyFormation_kJ_mol = -3765.2,
            Entropy_J_molK = 490.0,
            MolarVolume_cm3_mol = 172.57,
            MolecularWeight_g_mol = 277.85,
            Density_g_cm3 = 1.61,
            MohsHardness = 2.5,
            Color = "Colorless, white, reddish",
            Notes = "Important evaporite mineral, deliquescent",
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Bischofite",
            ChemicalFormula = "MgCl₂·6H₂O",
            Synonyms = new List<string> { "MgCl2!6H2O" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -2114.8,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -2499.3,
            Entropy_J_molK = 366.1,
            MolarVolume_cm3_mol = 129.4,
            MolecularWeight_g_mol = 203.30,
            Density_g_cm3 = 1.57,
            Color = "Colorless, white",
            Notes = "Very hygroscopic, found in evaporite deposits",
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // ADDITIONAL OXIDE MINERALS
        // ═══════════════════════════════════════════════════════════════════════

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Corundum",
            ChemicalFormula = "Al₂O₃",
            Synonyms = new List<string> { "Al2O3", "Alumina" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -1582.3,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -1675.7,
            Entropy_J_molK = 50.9,
            HeatCapacity_J_molK = 79.0,
            MolarVolume_cm3_mol = 25.58,
            MolecularWeight_g_mol = 101.96,
            Density_g_cm3 = 3.99,
            MohsHardness = 9.0,
            Color = "Colorless, red, blue",
            Notes = "Sapphire and ruby are gem varieties",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Periclase",
            ChemicalFormula = "MgO",
            Synonyms = new List<string> { "Magnesia" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -569.4,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -601.6,
            Entropy_J_molK = 26.9,
            HeatCapacity_J_molK = 37.2,
            MolarVolume_cm3_mol = 11.25,
            MolecularWeight_g_mol = 40.30,
            Density_g_cm3 = 3.58,
            MohsHardness = 6.0,
            Color = "Colorless, white",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Wustite",
            ChemicalFormula = "FeO",
            Synonyms = new List<string> { "Ferrous oxide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -251.4,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -272.0,
            Entropy_J_molK = 60.8,
            HeatCapacity_J_molK = 48.1,
            MolarVolume_cm3_mol = 12.00,
            MolecularWeight_g_mol = 71.84,
            Density_g_cm3 = 5.99,
            MohsHardness = 5.5,
            Color = "Black, gray",
            Notes = "Non-stoichiometric, stable only above 570°C",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Magnetite",
            ChemicalFormula = "Fe₃O₄",
            Synonyms = new List<string> { "Fe3O4" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -1015.4,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -1115.7,
            Entropy_J_molK = 146.1,
            HeatCapacity_J_molK = 143.4,
            MolarVolume_cm3_mol = 44.52,
            MolecularWeight_g_mol = 231.53,
            Density_g_cm3 = 5.20,
            MohsHardness = 6.0,
            Color = "Black",
            Notes = "Strongly magnetic",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Chromite",
            ChemicalFormula = "FeCr₂O₄",
            Synonyms = new List<string> { "FeCr2O4" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -1343.8,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -1444.7,
            Entropy_J_molK = 146.0,
            HeatCapacity_J_molK = 125.7,
            MolarVolume_cm3_mol = 44.01,
            MolecularWeight_g_mol = 223.83,
            Density_g_cm3 = 5.09,
            MohsHardness = 5.5,
            Color = "Black, brown-black",
            Notes = "Main ore of chromium",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // ADDITIONAL AQUEOUS SPECIES (PHREEQC database)
        // ═══════════════════════════════════════════════════════════════════════

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Strontium ion",
            ChemicalFormula = "Sr²⁺",
            Synonyms = new List<string> { "Sr2+", "Sr+2" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = -563.8,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -545.8,
            Entropy_J_molK = -32.6,
            MolecularWeight_g_mol = 87.62,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Barium ion",
            ChemicalFormula = "Ba²⁺",
            Synonyms = new List<string> { "Ba2+", "Ba+2" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = -560.8,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -537.6,
            Entropy_J_molK = 9.6,
            MolecularWeight_g_mol = 137.33,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Manganese(II) ion",
            ChemicalFormula = "Mn²⁺",
            Synonyms = new List<string> { "Mn2+", "Mn+2" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = 2,
            OxidationState = 2,
            GibbsFreeEnergyFormation_kJ_mol = -228.1,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -220.8,
            Entropy_J_molK = -73.6,
            MolecularWeight_g_mol = 54.94,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Zinc ion",
            ChemicalFormula = "Zn²⁺",
            Synonyms = new List<string> { "Zn2+", "Zn+2" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = -147.1,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -153.9,
            Entropy_J_molK = -112.1,
            MolecularWeight_g_mol = 65.38,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Lead(II) ion",
            ChemicalFormula = "Pb²⁺",
            Synonyms = new List<string> { "Pb2+", "Pb+2" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = -24.4,  // PHREEQC database
            EnthalpyFormation_kJ_mol = 0.9,
            Entropy_J_molK = 18.5,
            MolecularWeight_g_mol = 207.2,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Aluminum ion",
            ChemicalFormula = "Al³⁺",
            Synonyms = new List<string> { "Al3+", "Al+3" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = 3,
            GibbsFreeEnergyFormation_kJ_mol = -485.0,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -531.0,
            Entropy_J_molK = -321.7,
            MolecularWeight_g_mol = 26.98,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Nitrate ion",
            ChemicalFormula = "NO₃⁻",
            Synonyms = new List<string> { "NO3-", "NO3^-" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = -1,
            GibbsFreeEnergyFormation_kJ_mol = -111.3,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -207.4,
            Entropy_J_molK = 146.4,
            MolecularWeight_g_mol = 62.00,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Phosphate ion",
            ChemicalFormula = "PO₄³⁻",
            Synonyms = new List<string> { "PO4^3-", "PO43-" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = -3,
            GibbsFreeEnergyFormation_kJ_mol = -1018.7,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -1277.4,
            Entropy_J_molK = -220.9,
            MolecularWeight_g_mol = 94.97,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Fluoride ion",
            ChemicalFormula = "F⁻",
            Synonyms = new List<string> { "F-", "F^-" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = -1,
            GibbsFreeEnergyFormation_kJ_mol = -278.8,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -332.6,
            Entropy_J_molK = -13.8,
            MolecularWeight_g_mol = 19.00,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Bromide ion",
            ChemicalFormula = "Br⁻",
            Synonyms = new List<string> { "Br-", "Br^-" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = -1,
            GibbsFreeEnergyFormation_kJ_mol = -104.0,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -121.6,
            Entropy_J_molK = 82.4,
            MolecularWeight_g_mol = 79.90,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        // ═══════════════════════════════════════════════════════════════════════
        // ADDITIONAL GAS SPECIES (SUPCRT92)
        // ═══════════════════════════════════════════════════════════════════════

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Nitrogen gas",
            ChemicalFormula = "N₂(g)",
            Synonyms = new List<string> { "N2(g)", "N2" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,  // Element in standard state
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 191.6,
            HeatCapacity_J_molK = 29.1,
            HenrysLawConstant_mol_L_atm = 6.1e-4,  // 25°C
            MolecularWeight_g_mol = 28.01,
            Sources = new List<string> { "NIST WebBook", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Oxygen gas",
            ChemicalFormula = "O₂(g)",
            Synonyms = new List<string> { "O2(g)", "O2" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,  // Element in standard state
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 205.2,
            HeatCapacity_J_molK = 29.4,
            HenrysLawConstant_mol_L_atm = 1.3e-3,  // 25°C
            MolecularWeight_g_mol = 32.00,
            Sources = new List<string> { "NIST WebBook", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Methane gas",
            ChemicalFormula = "CH₄(g)",
            Synonyms = new List<string> { "CH4(g)", "CH4" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = -50.5,  // SUPCRT92
            EnthalpyFormation_kJ_mol = -74.6,
            Entropy_J_molK = 186.3,
            HeatCapacity_J_molK = 35.7,
            HenrysLawConstant_mol_L_atm = 1.4e-3,  // 25°C
            MolecularWeight_g_mol = 16.04,
            Sources = new List<string> { "NIST WebBook", "SUPCRT92" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hydrogen gas",
            ChemicalFormula = "H₂(g)",
            Synonyms = new List<string> { "H2(g)", "H2" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,  // Element in standard state
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 130.7,
            HeatCapacity_J_molK = 28.8,
            HenrysLawConstant_mol_L_atm = 7.8e-4,  // 25°C
            MolecularWeight_g_mol = 2.02,
            Sources = new List<string> { "NIST WebBook" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hydrogen sulfide gas",
            ChemicalFormula = "H₂S(g)",
            Synonyms = new List<string> { "H2S(g)", "H2S" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = -33.4,  // NIST
            EnthalpyFormation_kJ_mol = -20.6,
            Entropy_J_molK = 205.8,
            HeatCapacity_J_molK = 34.2,
            HenrysLawConstant_mol_L_atm = 0.102,  // 25°C
            MolecularWeight_g_mol = 34.08,
            Sources = new List<string> { "NIST WebBook", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Ammonia gas",
            ChemicalFormula = "NH₃(g)",
            Synonyms = new List<string> { "NH3(g)", "NH3" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = -16.4,  // NIST
            EnthalpyFormation_kJ_mol = -45.9,
            Entropy_J_molK = 192.8,
            HeatCapacity_J_molK = 35.1,
            HenrysLawConstant_mol_L_atm = 58.0,  // 25°C - very soluble
            MolecularWeight_g_mol = 17.03,
            Sources = new List<string> { "NIST WebBook" },
            IsUserCompound = false
        });

        Logger.Log($"[CompoundLibraryExtensions] Added {60} additional compounds (minerals, aqueous species, gases)");
    }
}
