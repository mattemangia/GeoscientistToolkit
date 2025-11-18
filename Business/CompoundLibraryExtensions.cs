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
        // =======================================================================
        // ADDITIONAL SILICATE MINERALS (Holland & Powell 2011)
        // =======================================================================

        // --- OLIVINES ---
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Forsterite",
            ChemicalFormula = "Mg2SiO4",
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
            ChemicalFormula = "Fe2SiO4",
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
            ChemicalFormula = "MgSiO3",
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
            ChemicalFormula = "FeSiO3",
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
            ChemicalFormula = "CaMgSi2O6",
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
            ChemicalFormula = "NaAlSi3O8",
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
            ChemicalFormula = "CaAl2Si2O8",
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
            ChemicalFormula = "KAlSi3O8",
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

        // =======================================================================
        // ADDITIONAL CARBONATE MINERALS
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Magnesite",
            ChemicalFormula = "MgCO3",
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
            ChemicalFormula = "FeCO3",
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
            ChemicalFormula = "MnCO3",
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
            ChemicalFormula = "SrCO3",
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

        // =======================================================================
        // ADDITIONAL SULFATE MINERALS
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Anhydrite",
            ChemicalFormula = "CaSO4",
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
            ChemicalFormula = "BaSO4",
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
            ChemicalFormula = "SrSO4",
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
            ChemicalFormula = "PbSO4",
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

        // =======================================================================
        // ADDITIONAL CHLORIDE MINERALS AND SALTS
        // =======================================================================

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
            ChemicalFormula = "KMgCl3*6H2O",
            Synonyms = new List<string> { "KMgCl3*6H2O" },
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
            ChemicalFormula = "MgCl2*6H2O",
            Synonyms = new List<string> { "MgCl2*6H2O" },
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

        // =======================================================================
        // ADDITIONAL OXIDE MINERALS
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Corundum",
            ChemicalFormula = "Al2O3",
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
            Notes = "Non-stoichiometric, stable only above 570degC",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Magnetite",
            ChemicalFormula = "Fe3O4",
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
            ChemicalFormula = "FeCr2O4",
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

        // =======================================================================
        // ADDITIONAL AQUEOUS SPECIES (PHREEQC database)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Strontium ion",
            ChemicalFormula = "Sr2+",
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
            ChemicalFormula = "Ba2+",
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
            ChemicalFormula = "Mn2+",
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
            ChemicalFormula = "Zn2+",
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
            ChemicalFormula = "Pb2+",
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
            ChemicalFormula = "Al3+",
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
            ChemicalFormula = "NO3-",
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
            ChemicalFormula = "PO43-",
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
            ChemicalFormula = "F-",
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
            ChemicalFormula = "Br-",
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

        // =======================================================================
        // ADDITIONAL GAS SPECIES (SUPCRT92)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Nitrogen gas",
            ChemicalFormula = "N2(g)",
            Synonyms = new List<string> { "N2(g)", "N2" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,  // Element in standard state
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 191.6,
            HeatCapacity_J_molK = 29.1,
            HenrysLawConstant_mol_L_atm = 6.1e-4,  // 25degC
            MolecularWeight_g_mol = 28.01,
            Sources = new List<string> { "NIST WebBook", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Oxygen gas",
            ChemicalFormula = "O2(g)",
            Synonyms = new List<string> { "O2(g)", "O2" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,  // Element in standard state
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 205.2,
            HeatCapacity_J_molK = 29.4,
            HenrysLawConstant_mol_L_atm = 1.3e-3,  // 25degC
            MolecularWeight_g_mol = 32.00,
            Sources = new List<string> { "NIST WebBook", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Methane gas",
            ChemicalFormula = "CH4(g)",
            Synonyms = new List<string> { "CH4(g)", "CH4" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = -50.5,  // SUPCRT92
            EnthalpyFormation_kJ_mol = -74.6,
            Entropy_J_molK = 186.3,
            HeatCapacity_J_molK = 35.7,
            HenrysLawConstant_mol_L_atm = 1.4e-3,  // 25degC
            MolecularWeight_g_mol = 16.04,
            Sources = new List<string> { "NIST WebBook", "SUPCRT92" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hydrogen gas",
            ChemicalFormula = "H2(g)",
            Synonyms = new List<string> { "H2(g)", "H2" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,  // Element in standard state
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 130.7,
            HeatCapacity_J_molK = 28.8,
            HenrysLawConstant_mol_L_atm = 7.8e-4,  // 25degC
            MolecularWeight_g_mol = 2.02,
            Sources = new List<string> { "NIST WebBook" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hydrogen sulfide gas",
            ChemicalFormula = "H2S(g)",
            Synonyms = new List<string> { "H2S(g)", "H2S" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = -33.4,  // NIST
            EnthalpyFormation_kJ_mol = -20.6,
            Entropy_J_molK = 205.8,
            HeatCapacity_J_molK = 34.2,
            HenrysLawConstant_mol_L_atm = 0.102,  // 25degC
            MolecularWeight_g_mol = 34.08,
            Sources = new List<string> { "NIST WebBook", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Ammonia gas",
            ChemicalFormula = "NH3(g)",
            Synonyms = new List<string> { "NH3(g)", "NH3" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = -16.4,  // NIST
            EnthalpyFormation_kJ_mol = -45.9,
            Entropy_J_molK = 192.8,
            HeatCapacity_J_molK = 35.1,
            HenrysLawConstant_mol_L_atm = 58.0,  // 25degC - very soluble
            MolecularWeight_g_mol = 17.03,
            Sources = new List<string> { "NIST WebBook" },
            IsUserCompound = false
        });

        // =======================================================================
        // METAMORPHIC MINERALS - Al-Silicates (Holland & Powell 2011)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Kyanite",
            ChemicalFormula = "Al2SiO5",
            Synonyms = new List<string> { "Al2SiO5", "Disthene" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -2443.9,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -2594.3,
            Entropy_J_molK = 83.8,
            HeatCapacity_J_molK = 121.7,
            MolarVolume_cm3_mol = 44.09,
            MolecularWeight_g_mol = 162.05,
            Density_g_cm3 = 3.68,
            MohsHardness = 7.0,
            Color = "Blue, white, gray",
            Notes = "High-pressure polymorph of Al2SiO5, forms in blueschist and eclogite facies",
            Sources = new List<string> { "Holland & Powell (2011)", "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Andalusite",
            ChemicalFormula = "Al2SiO5",
            Synonyms = new List<string> { "Al2SiO5" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -2441.2,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -2590.2,
            Entropy_J_molK = 93.2,
            HeatCapacity_J_molK = 122.7,
            MolarVolume_cm3_mol = 51.53,
            MolecularWeight_g_mol = 162.05,
            Density_g_cm3 = 3.15,
            MohsHardness = 7.5,
            Color = "Pink, white, gray",
            Notes = "Low-pressure polymorph of Al2SiO5, contact metamorphism indicator",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Sillimanite",
            ChemicalFormula = "Al2SiO5",
            Synonyms = new List<string> { "Al2SiO5", "Fibrolite" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -2440.7,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -2587.8,
            Entropy_J_molK = 95.4,
            HeatCapacity_J_molK = 124.5,
            MolarVolume_cm3_mol = 49.90,
            MolecularWeight_g_mol = 162.05,
            Density_g_cm3 = 3.25,
            MohsHardness = 7.0,
            Color = "White, gray, brown",
            Notes = "High-temperature polymorph of Al2SiO5, granulite facies indicator",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        // --- GARNETS (Holland & Powell 2011) ---

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Almandine",
            ChemicalFormula = "Fe3Al2Si3O12",
            Synonyms = new List<string> { "Fe3Al2Si3O12", "Fe-Garnet" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -4937.6,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -5261.2,
            Entropy_J_molK = 342.0,
            HeatCapacity_J_molK = 325.1,
            MolarVolume_cm3_mol = 115.28,
            MolecularWeight_g_mol = 497.75,
            Density_g_cm3 = 4.32,
            MohsHardness = 7.5,
            Color = "Red, brown-red",
            Notes = "Fe-rich garnet end-member, common in pelitic schists",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Pyrope",
            ChemicalFormula = "Mg3Al2Si3O12",
            Synonyms = new List<string> { "Mg3Al2Si3O12", "Mg-Garnet" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -6285.5,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -6640.7,
            Entropy_J_molK = 266.3,
            HeatCapacity_J_molK = 325.1,
            MolarVolume_cm3_mol = 113.13,
            MolecularWeight_g_mol = 403.13,
            Density_g_cm3 = 3.56,
            MohsHardness = 7.5,
            Color = "Red, purple-red",
            Notes = "Mg-rich garnet, indicator of high pressure (eclogite, peridotite)",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Grossular",
            ChemicalFormula = "Ca3Al2Si3O12",
            Synonyms = new List<string> { "Ca3Al2Si3O12", "Ca-Garnet" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -6632.5,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -6984.8,
            Entropy_J_molK = 255.2,
            HeatCapacity_J_molK = 325.5,
            MolarVolume_cm3_mol = 125.30,
            MolecularWeight_g_mol = 450.45,
            Density_g_cm3 = 3.59,
            MohsHardness = 7.5,
            Color = "Green, yellow-green, brown",
            Notes = "Ca-rich garnet, found in calc-silicate rocks and rodingites",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        // =======================================================================
        // MICAS (Holland & Powell 2011)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Muscovite",
            ChemicalFormula = "KAl2(AlSi3O10)(OH)2",
            Synonyms = new List<string> { "KAl2(AlSi3O10)(OH)2", "White mica" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -5600.1,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -5976.3,
            Entropy_J_molK = 287.0,
            HeatCapacity_J_molK = 292.9,
            MolarVolume_cm3_mol = 140.71,
            MolecularWeight_g_mol = 398.31,
            Density_g_cm3 = 2.83,
            MohsHardness = 2.5,
            Color = "White, colorless, silver",
            Notes = "Common phyllosilicate in pelitic schists and granites",
            Sources = new List<string> { "Holland & Powell (2011)", "Deer et al. (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Phlogopite",
            ChemicalFormula = "KMg3(AlSi3O10)(OH)2",
            Synonyms = new List<string> { "KMg3(AlSi3O10)(OH)2", "Mg-biotite" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -6215.8,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -6550.2,
            Entropy_J_molK = 328.1,
            HeatCapacity_J_molK = 316.9,
            MolarVolume_cm3_mol = 149.66,
            MolecularWeight_g_mol = 417.26,
            Density_g_cm3 = 2.79,
            MohsHardness = 2.5,
            Color = "Brown, green-brown",
            Notes = "Mg-rich mica, stable in ultramafic rocks and marbles",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Biotite",
            ChemicalFormula = "K(Mg,Fe)3(AlSi3O10)(OH)2",
            Synonyms = new List<string> { "Black mica" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -5644.3,  // Average composition, Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -6023.1,
            Entropy_J_molK = 340.5,
            HeatCapacity_J_molK = 310.5,
            MolarVolume_cm3_mol = 154.4,
            MolecularWeight_g_mol = 512.76,
            Density_g_cm3 = 3.10,
            MohsHardness = 2.5,
            Color = "Black, dark brown",
            Notes = "Common Fe-Mg mica in igneous and metamorphic rocks, solid solution series",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        // =======================================================================
        // CLAY MINERALS & PHYLLOSILICATES (Robie & Hemingway 1995)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Kaolinite",
            ChemicalFormula = "Al2Si2O5(OH)4",
            Synonyms = new List<string> { "Al2Si2O5(OH)4", "Kaolin" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -3799.4,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -4119.7,
            Entropy_J_molK = 203.1,
            HeatCapacity_J_molK = 205.0,
            MolarVolume_cm3_mol = 99.52,
            MolecularWeight_g_mol = 258.16,
            Density_g_cm3 = 2.59,
            MohsHardness = 2.0,
            Color = "White, gray",
            Notes = "Common weathering product of feldspars, major clay mineral",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Pyrophyllite",
            ChemicalFormula = "Al2Si4O10(OH)2",
            Synonyms = new List<string> { "Al2Si4O10(OH)2" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -5266.0,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -5640.3,
            Entropy_J_molK = 239.4,
            HeatCapacity_J_molK = 294.0,
            MolarVolume_cm3_mol = 126.6,
            MolecularWeight_g_mol = 360.31,
            Density_g_cm3 = 2.85,
            MohsHardness = 1.5,
            Color = "White, gray, green",
            Notes = "Hydrothermal alteration product, similar to talc but with Al",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Talc",
            ChemicalFormula = "Mg3Si4O10(OH)2",
            Synonyms = new List<string> { "Mg3Si4O10(OH)2", "Talcum" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -5520.5,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -5897.8,
            Entropy_J_molK = 260.8,
            HeatCapacity_J_molK = 324.0,
            MolarVolume_cm3_mol = 136.25,
            MolecularWeight_g_mol = 379.27,
            Density_g_cm3 = 2.78,
            MohsHardness = 1.0,
            Color = "White, green, gray",
            Notes = "Softest common mineral, alteration product of ultramafic rocks",
            Sources = new List<string> { "Holland & Powell (2011)", "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Serpentine",
            ChemicalFormula = "Mg3Si2O5(OH)4",
            Synonyms = new List<string> { "Mg3Si2O5(OH)4", "Antigorite", "Chrysotile", "Lizardite" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -4032.2,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -4360.5,
            Entropy_J_molK = 221.3,
            HeatCapacity_J_molK = 244.3,
            MolarVolume_cm3_mol = 107.0,
            MolecularWeight_g_mol = 277.11,
            Density_g_cm3 = 2.59,
            MohsHardness = 3.0,
            Color = "Green, yellow-green",
            Notes = "Major serpentinization product of olivine and pyroxene",
            Sources = new List<string> { "Holland & Powell (2011)", "Evans et al. (2013)" },
            IsUserCompound = false
        });

        // =======================================================================
        // AMPHIBOLES (Holland & Powell 2011)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Tremolite",
            ChemicalFormula = "Ca2Mg5Si8O22(OH)2",
            Synonyms = new List<string> { "Ca2Mg5Si8O22(OH)2" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -12067.4,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -12779.4,
            Entropy_J_molK = 549.3,
            HeatCapacity_J_molK = 665.0,
            MolarVolume_cm3_mol = 272.92,
            MolecularWeight_g_mol = 812.37,
            Density_g_cm3 = 2.98,
            MohsHardness = 5.5,
            Color = "White, gray, green",
            Notes = "Mg-rich amphibole, forms from metamorphism of dolomitic limestones",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Actinolite",
            ChemicalFormula = "Ca2(Mg,Fe)5Si8O22(OH)2",
            Synonyms = new List<string> { "Ca2(Mg,Fe)5Si8O22(OH)2" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -11523.8,  // Average, Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -12198.5,
            Entropy_J_molK = 572.0,
            HeatCapacity_J_molK = 680.0,
            MolarVolume_cm3_mol = 277.4,
            MolecularWeight_g_mol = 970.20,
            Density_g_cm3 = 3.20,
            MohsHardness = 5.5,
            Color = "Green, dark green",
            Notes = "Ca-Mg-Fe amphibole, greenschist facies indicator",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        // =======================================================================
        // ADDITIONAL SILICATES
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Wollastonite",
            ChemicalFormula = "CaSiO3",
            Synonyms = new List<string> { "CaSiO3" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -1549.7,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -1634.9,
            Entropy_J_molK = 82.0,
            HeatCapacity_J_molK = 85.8,
            MolarVolume_cm3_mol = 39.93,
            MolecularWeight_g_mol = 116.16,
            Density_g_cm3 = 2.91,
            MohsHardness = 5.0,
            Color = "White, gray",
            Notes = "Forms from thermal metamorphism of siliceous limestones",
            Sources = new List<string> { "Holland & Powell (2011)", "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Epidote",
            ChemicalFormula = "Ca2Al2FeSi3O12(OH)",
            Synonyms = new List<string> { "Ca2Al2FeSi3O12(OH)" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -6477.1,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -6890.3,
            Entropy_J_molK = 301.3,
            HeatCapacity_J_molK = 315.5,
            MolarVolume_cm3_mol = 139.2,
            MolecularWeight_g_mol = 483.25,
            Density_g_cm3 = 3.47,
            MohsHardness = 6.5,
            Color = "Green, yellow-green",
            Notes = "Common in low-grade metamorphic rocks, forms during hydrothermal alteration",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        // =======================================================================
        // PHOSPHATE MINERALS (Robie & Hemingway 1995)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Fluorapatite",
            ChemicalFormula = "Ca5(PO4)3F",
            Synonyms = new List<string> { "Ca5(PO4)3F", "FAP" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Hexagonal,
            GibbsFreeEnergyFormation_kJ_mol = -6342.9,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -6783.6,
            Entropy_J_molK = 390.4,
            HeatCapacity_J_molK = 390.0,
            LogKsp_25C = -60.4,
            MolarVolume_cm3_mol = 158.4,
            MolecularWeight_g_mol = 504.30,
            Density_g_cm3 = 3.18,
            MohsHardness = 5.0,
            Color = "Green, blue, yellow, colorless",
            Notes = "Most common apatite, important in phosphate deposits",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hydroxyapatite",
            ChemicalFormula = "Ca5(PO4)3OH",
            Synonyms = new List<string> { "Ca5(PO4)3OH", "HAP" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Hexagonal,
            GibbsFreeEnergyFormation_kJ_mol = -6314.6,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -6721.2,
            Entropy_J_molK = 390.4,
            HeatCapacity_J_molK = 402.6,
            LogKsp_25C = -58.4,
            MolarVolume_cm3_mol = 159.0,
            MolecularWeight_g_mol = 502.31,
            Density_g_cm3 = 3.16,
            MohsHardness = 5.0,
            Color = "White, colorless",
            Notes = "Major component of bone and teeth, biogenic mineral",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Vivianite",
            ChemicalFormula = "Fe3(PO4)2*8H2O",
            Synonyms = new List<string> { "Fe3(PO4)2*8H2O" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -4366.5,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -5096.2,
            Entropy_J_molK = 792.0,
            LogKsp_25C = -36.0,
            MolarVolume_cm3_mol = 210.0,
            MolecularWeight_g_mol = 501.60,
            Density_g_cm3 = 2.68,
            MohsHardness = 2.0,
            Color = "Colorless when fresh, blue-green when oxidized",
            Notes = "Forms in reducing environments rich in Fe2+ and phosphate",
            Sources = new List<string> { "PHREEQC database", "Nriagu & Dell (1974)" },
            IsUserCompound = false
        });

        // =======================================================================
        // ADDITIONAL CARBONATE MINERALS (Robie & Hemingway 1995)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Witherite",
            ChemicalFormula = "BaCO3",
            Synonyms = new List<string> { "BaCO3" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -1132.2,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -1216.3,
            Entropy_J_molK = 112.1,
            HeatCapacity_J_molK = 85.4,
            LogKsp_25C = -8.56,
            MolarVolume_cm3_mol = 45.81,
            MolecularWeight_g_mol = 197.34,
            Density_g_cm3 = 4.31,
            MohsHardness = 3.5,
            Color = "White, gray",
            Notes = "Rare Ba carbonate, forms in hydrothermal veins",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Cerussite",
            ChemicalFormula = "PbCO3",
            Synonyms = new List<string> { "PbCO3", "Lead carbonate" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -625.5,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -699.1,
            Entropy_J_molK = 131.0,
            HeatCapacity_J_molK = 87.4,
            LogKsp_25C = -13.13,
            MolarVolume_cm3_mol = 48.09,
            MolecularWeight_g_mol = 267.21,
            Density_g_cm3 = 6.55,
            MohsHardness = 3.0,
            Color = "White, gray, colorless",
            Notes = "Secondary mineral from oxidation of galena",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Smithsonite",
            ChemicalFormula = "ZnCO3",
            Synonyms = new List<string> { "ZnCO3", "Zinc spar" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -731.5,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -812.8,
            Entropy_J_molK = 82.4,
            HeatCapacity_J_molK = 79.7,
            LogKsp_25C = -10.0,
            MolarVolume_cm3_mol = 31.86,
            MolecularWeight_g_mol = 125.40,
            Density_g_cm3 = 4.43,
            MohsHardness = 4.5,
            Color = "White, yellow, green, blue",
            Notes = "Secondary Zn mineral, forms from weathering of sphalerite",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Azurite",
            ChemicalFormula = "Cu3(CO3)2(OH)2",
            Synonyms = new List<string> { "Cu3(CO3)2(OH)2" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -1448.5,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -1655.5,
            Entropy_J_molK = 251.0,
            LogKsp_25C = -46.2,
            MolarVolume_cm3_mol = 91.4,
            MolecularWeight_g_mol = 344.67,
            Density_g_cm3 = 3.77,
            MohsHardness = 3.5,
            Color = "Azure blue",
            Notes = "Secondary Cu mineral, forms with malachite in oxidized Cu deposits",
            Sources = new List<string> { "PHREEQC database", "Nordstrom & Archer (2003)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Malachite",
            ChemicalFormula = "Cu2CO3(OH)2",
            Synonyms = new List<string> { "Cu2CO3(OH)2" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -893.7,  // PHREEQC database
            EnthalpyFormation_kJ_mol = -1051.4,
            Entropy_J_molK = 186.2,
            LogKsp_25C = -33.8,
            MolarVolume_cm3_mol = 54.1,
            MolecularWeight_g_mol = 221.12,
            Density_g_cm3 = 4.05,
            MohsHardness = 3.5,
            Color = "Bright green",
            Notes = "Common secondary Cu mineral, ornamental stone",
            Sources = new List<string> { "PHREEQC database", "Nordstrom & Archer (2003)" },
            IsUserCompound = false
        });

        // =======================================================================
        // SULFIDE MINERALS (Robie & Hemingway 1995)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Pyrite",
            ChemicalFormula = "FeS2",
            Synonyms = new List<string> { "FeS2", "Iron pyrite", "Fool's gold" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -160.1,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -178.2,
            Entropy_J_molK = 52.9,
            HeatCapacity_J_molK = 62.2,
            LogKsp_25C = -18.5,
            MolarVolume_cm3_mol = 23.94,
            MolecularWeight_g_mol = 119.98,
            Density_g_cm3 = 5.01,
            MohsHardness = 6.5,
            Color = "Brass yellow, metallic",
            Notes = "Most common sulfide mineral, forms in many environments",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Galena",
            ChemicalFormula = "PbS",
            Synonyms = new List<string> { "Lead sulfide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -92.7,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -100.4,
            Entropy_J_molK = 91.2,
            HeatCapacity_J_molK = 49.5,
            LogKsp_25C = -27.5,
            MolarVolume_cm3_mol = 31.54,
            MolecularWeight_g_mol = 239.27,
            Density_g_cm3 = 7.58,
            MohsHardness = 2.5,
            Color = "Lead-gray, metallic",
            Notes = "Primary ore of lead, common in hydrothermal veins",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Sphalerite",
            ChemicalFormula = "ZnS",
            Synonyms = new List<string> { "Zinc blende" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -198.3,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -206.0,
            Entropy_J_molK = 57.7,
            HeatCapacity_J_molK = 46.0,
            LogKsp_25C = -23.8,
            MolarVolume_cm3_mol = 23.83,
            MolecularWeight_g_mol = 97.47,
            Density_g_cm3 = 4.09,
            MohsHardness = 4.0,
            Color = "Yellow, brown, black",
            Notes = "Primary ore of zinc, common in Mississippi Valley-type deposits",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Chalcopyrite",
            ChemicalFormula = "CuFeS2",
            Synonyms = new List<string> { "CuFeS2", "Copper pyrite" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Tetragonal,
            GibbsFreeEnergyFormation_kJ_mol = -193.7,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -194.6,
            Entropy_J_molK = 124.9,
            HeatCapacity_J_molK = 98.3,
            LogKsp_25C = -36.0,
            MolarVolume_cm3_mol = 42.45,
            MolecularWeight_g_mol = 183.52,
            Density_g_cm3 = 4.19,
            MohsHardness = 3.5,
            Color = "Brass yellow with green tint",
            Notes = "Most important Cu ore mineral, porphyry deposits",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Pyrrhotite",
            ChemicalFormula = "Fe1-xS",
            Synonyms = new List<string> { "FeS", "Magnetic pyrite" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -99.6,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -101.7,
            Entropy_J_molK = 60.3,
            HeatCapacity_J_molK = 49.9,
            MolarVolume_cm3_mol = 18.20,
            MolecularWeight_g_mol = 87.91,
            Density_g_cm3 = 4.61,
            MohsHardness = 4.0,
            Color = "Bronze-brown, metallic",
            Notes = "Non-stoichiometric Fe sulfide, weakly magnetic",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        // =======================================================================
        // ZEOLITE MINERALS (Bowers et al. 1984)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Analcime",
            ChemicalFormula = "NaAlSi2O6*H2O",
            Synonyms = new List<string> { "NaAlSi2O6*H2O", "Analcite" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -3087.8,  // Bowers et al. 1984
            EnthalpyFormation_kJ_mol = -3306.6,
            Entropy_J_molK = 226.0,
            HeatCapacity_J_molK = 217.0,
            MolarVolume_cm3_mol = 97.2,
            MolecularWeight_g_mol = 220.16,
            Density_g_cm3 = 2.27,
            MohsHardness = 5.5,
            Color = "White, colorless",
            Notes = "Common zeolite in basalts and tuffs, hydrothermal alteration",
            Sources = new List<string> { "Bowers et al. (1984)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Laumontite",
            ChemicalFormula = "CaAl2Si4O12*4H2O",
            Synonyms = new List<string> { "CaAl2Si4O12*4H2O" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -6870.9,  // Bowers et al. 1984
            EnthalpyFormation_kJ_mol = -7450.3,
            Entropy_J_molK = 424.0,
            HeatCapacity_J_molK = 480.0,
            MolarVolume_cm3_mol = 189.0,
            MolecularWeight_g_mol = 470.43,
            Density_g_cm3 = 2.25,
            MohsHardness = 3.5,
            Color = "White, pink",
            Notes = "Low-temperature zeolite, burial metamorphism indicator",
            Sources = new List<string> { "Bowers et al. (1984)" },
            IsUserCompound = false
        });

        // =======================================================================
        // ADDITIONAL OXIDE MINERALS (Robie & Hemingway 1995)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Rutile",
            ChemicalFormula = "TiO2",
            Synonyms = new List<string> { "TiO2", "Titanium dioxide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Tetragonal,
            GibbsFreeEnergyFormation_kJ_mol = -889.5,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -944.0,
            Entropy_J_molK = 50.6,
            HeatCapacity_J_molK = 55.0,
            MolarVolume_cm3_mol = 18.69,
            MolecularWeight_g_mol = 79.87,
            Density_g_cm3 = 4.25,
            MohsHardness = 6.5,
            Color = "Red-brown, black",
            Notes = "Common accessory mineral, high-pressure polymorph of TiO2",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Ilmenite",
            ChemicalFormula = "FeTiO3",
            Synonyms = new List<string> { "FeTiO3" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -1162.3,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -1234.9,
            Entropy_J_molK = 108.5,
            HeatCapacity_J_molK = 96.5,
            MolarVolume_cm3_mol = 31.86,
            MolecularWeight_g_mol = 151.71,
            Density_g_cm3 = 4.72,
            MohsHardness = 5.5,
            Color = "Black, metallic",
            Notes = "Primary Ti ore mineral, common in mafic igneous rocks",
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Spinel",
            ChemicalFormula = "MgAl2O4",
            Synonyms = new List<string> { "MgAl2O4" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -2166.9,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -2300.3,
            Entropy_J_molK = 80.6,
            HeatCapacity_J_molK = 117.1,
            MolarVolume_cm3_mol = 39.71,
            MolecularWeight_g_mol = 142.27,
            Density_g_cm3 = 3.58,
            MohsHardness = 8.0,
            Color = "Red, blue, green, colorless",
            Notes = "Important in ultramafic rocks and high-grade metamorphism",
            Sources = new List<string> { "Holland & Powell (2011)", "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hercynite",
            ChemicalFormula = "FeAl2O4",
            Synonyms = new List<string> { "FeAl2O4", "Fe-spinel" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -1879.5,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -1969.9,
            Entropy_J_molK = 106.3,
            HeatCapacity_J_molK = 130.8,
            MolarVolume_cm3_mol = 40.75,
            MolecularWeight_g_mol = 173.81,
            Density_g_cm3 = 4.39,
            MohsHardness = 7.5,
            Color = "Dark green, black",
            Notes = "Fe end-member of spinel group",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        // =======================================================================
        // HYDROXIDE MINERALS (Robie & Hemingway 1995)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Gibbsite",
            ChemicalFormula = "Al(OH)3",
            Synonyms = new List<string> { "Al(OH)3", "Hydrargillite" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -1154.9,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -1293.1,
            Entropy_J_molK = 68.4,
            HeatCapacity_J_molK = 93.2,
            LogKsp_25C = 8.11,  // For dissolution to Al3+
            MolarVolume_cm3_mol = 31.96,
            MolecularWeight_g_mol = 78.00,
            Density_g_cm3 = 2.42,
            MohsHardness = 3.0,
            Color = "White, gray",
            Notes = "Common in bauxite, weathering product of Al-silicates",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Brucite",
            ChemicalFormula = "Mg(OH)2",
            Synonyms = new List<string> { "Mg(OH)2", "Magnesium hydroxide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -833.5,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -924.5,
            Entropy_J_molK = 63.2,
            HeatCapacity_J_molK = 77.0,
            LogKsp_25C = -11.16,
            MolarVolume_cm3_mol = 24.63,
            MolecularWeight_g_mol = 58.32,
            Density_g_cm3 = 2.37,
            MohsHardness = 2.5,
            Color = "White, gray, green",
            Notes = "Forms from serpentinization of periclase, pH buffer in seawater",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Goethite",
            ChemicalFormula = "FeOOH",
            Synonyms = new List<string> { "alpha-FeOOH", "Iron oxyhydroxide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -488.6,  // Robie & Hemingway 1995
            EnthalpyFormation_kJ_mol = -559.3,
            Entropy_J_molK = 60.4,
            HeatCapacity_J_molK = 84.0,
            LogKsp_25C = -41.0,  // For dissolution to Fe3+
            MolarVolume_cm3_mol = 20.82,
            MolecularWeight_g_mol = 88.85,
            Density_g_cm3 = 4.27,
            MohsHardness = 5.5,
            Color = "Yellow-brown, red-brown",
            Notes = "Most common Fe oxide in soils, weathering product",
            Sources = new List<string> { "Robie & Hemingway (1995)", "Cornell & Schwertmann (2003)" },
            IsUserCompound = false
        });

        // =======================================================================
        // ADDITIONAL GAS SPECIES (NIST WebBook)
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Argon gas",
            ChemicalFormula = "Ar(g)",
            Synonyms = new List<string> { "Ar", "Ar(g)" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,  // Element in standard state
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 154.8,
            HeatCapacity_J_molK = 20.8,
            HenrysLawConstant_mol_L_atm = 1.4e-3,  // 25degC
            MolecularWeight_g_mol = 39.95,
            Sources = new List<string> { "NIST WebBook" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Helium gas",
            ChemicalFormula = "He(g)",
            Synonyms = new List<string> { "He", "He(g)" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,  // Element in standard state
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 126.2,
            HeatCapacity_J_molK = 20.8,
            HenrysLawConstant_mol_L_atm = 3.7e-4,  // 25degC
            MolecularWeight_g_mol = 4.00,
            Sources = new List<string> { "NIST WebBook" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Carbon monoxide gas",
            ChemicalFormula = "CO(g)",
            Synonyms = new List<string> { "CO", "CO(g)" },
            Phase = CompoundPhase.Gas,
            GibbsFreeEnergyFormation_kJ_mol = -137.2,  // NIST WebBook
            EnthalpyFormation_kJ_mol = -110.5,
            Entropy_J_molK = 197.7,
            HeatCapacity_J_molK = 29.1,
            HenrysLawConstant_mol_L_atm = 9.5e-4,  // 25degC
            MolecularWeight_g_mol = 28.01,
            Sources = new List<string> { "NIST WebBook", "SUPCRT92" },
            IsUserCompound = false
        });

        Logger.Log($"[CompoundLibraryExtensions] Added {110} additional compounds (minerals, aqueous species, gases)");
    }
}
