// GeoscientistToolkit/Business/CompoundLibraryMultiphaseExtensions.cs
//
// Extension to CompoundLibrary adding compounds for multiphase flow simulations
// Includes gases (CO2, CH4, H2S, N2, O2), aqueous species, and dissolved gas species
//
// References:
// - NIST Chemistry WebBook (webbook.nist.gov)
// - Parkhurst & Appelo (2013): PHREEQC database (phreeqc.dat, llnl.dat)
// - Stumm & Morgan (1996): Aquatic Chemistry
// - Duan & Sun (2003): CO2-water system thermodynamics
// - Spycher et al. (2003): CO2-H2O-NaCl system phase equilibria

using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

/// <summary>
/// Extension methods to add multiphase flow compounds to CompoundLibrary
/// </summary>
public static class CompoundLibraryMultiphaseExtensions
{
    /// <summary>
    /// Seed additional compounds for multiphase reactive transport simulations
    /// </summary>
    public static void SeedMultiphaseCompounds(this CompoundLibrary library)
    {
        Logger.Log("[CompoundLibrary] Seeding multiphase flow compounds...");

        // ==================== GASES ====================

        AddCO2(library);
        AddMethane(library);
        AddH2S(library);
        AddN2(library);
        AddO2(library);
        AddH2(library);
        AddNH3(library);

        // ==================== AQUEOUS DISSOLVED GASES ====================

        AddDissolvedCO2(library);
        AddCarbonicAcid(library);
        AddBicarbonateIon(library);
        AddCarbonateIon(library);
        AddDissolvedH2S(library);
        AddHS_Ion(library);
        AddS2_Ion(library);
        AddDissolvedNH3(library);
        AddNH4_Ion(library);
        AddDissolvedO2(library);
        AddDissolvedN2(library);
        AddDissolvedCH4(library);

        // ==================== ADDITIONAL AQUEOUS SPECIES ====================

        AddSO4_Ion(library);
        AddHSO4_Ion(library);
        AddNO3_Ion(library);
        AddNO2_Ion(library);
        AddPO4_Ion(library);
        AddHPO4_Ion(library);
        AddH2PO4_Ion(library);
        AddFe2_Ion(library);
        AddFe3_Ion(library);
        AddMn2_Ion(library);
        AddAl3_Ion(library);
        AddPb2_Ion(library);
        AddZn2_Ion(library);
        AddCu2_Ion(library);

        // ==================== ADDITIONAL MINERALS FOR REACTIVE TRANSPORT ====================

        AddPyrite(library);
        AddMarkasite(library);
        AddAnhydrite(library);
        AddGypsum(library);
        AddApatite(library);
        AddMagnetite(library);
        AddHematite(library);
        AddGoethite(library);
        AddFerrihydrite(library);
        AddSiderite(library);
        AddRhodochrosite(library);

        Logger.Log("[CompoundLibrary] Multiphase compound seeding complete.");
    }

    // ==================== GAS PHASE COMPOUNDS ====================

    private static void AddCO2(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Carbon Dioxide (gas)",
            ChemicalFormula = "CO₂(g)",
            Phase = CompoundPhase.Gas,
            MolecularWeight_g_mol = 44.01,
            GibbsFreeEnergyFormation_kJ_mol = -394.36, // at 298.15 K, 1 bar
            EnthalpyFormation_kJ_mol = -393.51,
            Entropy_J_molK = 213.79,
            HeatCapacity_J_molK = 37.13,
            HenrysLawConstant_mol_L_atm = 0.034, // at 25°C
            SetchenowCoefficient = 0.1, // for salting-out effect
            Sources = new List<string> { "NIST Chemistry WebBook", "Duan & Sun (2003)" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = false
        });
    }

    private static void AddMethane(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Methane (gas)",
            ChemicalFormula = "CH₄(g)",
            Phase = CompoundPhase.Gas,
            MolecularWeight_g_mol = 16.04,
            GibbsFreeEnergyFormation_kJ_mol = -50.72,
            EnthalpyFormation_kJ_mol = -74.87,
            Entropy_J_molK = 186.25,
            HeatCapacity_J_molK = 35.69,
            HenrysLawConstant_mol_L_atm = 0.0014, // at 25°C
            SetchenowCoefficient = 0.12,
            Sources = new List<string> { "NIST Chemistry WebBook" },
            IsUserCompound = false
        });
    }

    private static void AddH2S(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hydrogen Sulfide (gas)",
            ChemicalFormula = "H₂S(g)",
            Phase = CompoundPhase.Gas,
            MolecularWeight_g_mol = 34.08,
            GibbsFreeEnergyFormation_kJ_mol = -33.56,
            EnthalpyFormation_kJ_mol = -20.6,
            Entropy_J_molK = 205.79,
            HeatCapacity_J_molK = 34.23,
            HenrysLawConstant_mol_L_atm = 0.102, // at 25°C - much more soluble than CO2
            SetchenowCoefficient = 0.08,
            Sources = new List<string> { "NIST Chemistry WebBook" },
            IsUserCompound = false
        });
    }

    private static void AddN2(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Nitrogen (gas)",
            ChemicalFormula = "N₂(g)",
            Phase = CompoundPhase.Gas,
            MolecularWeight_g_mol = 28.01,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 191.61,
            HeatCapacity_J_molK = 29.12,
            HenrysLawConstant_mol_L_atm = 0.00065, // at 25°C
            SetchenowCoefficient = 0.14,
            Sources = new List<string> { "NIST Chemistry WebBook" },
            IsUserCompound = false
        });
    }

    private static void AddO2(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Oxygen (gas)",
            ChemicalFormula = "O₂(g)",
            Phase = CompoundPhase.Gas,
            MolecularWeight_g_mol = 32.00,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 205.15,
            HeatCapacity_J_molK = 29.38,
            HenrysLawConstant_mol_L_atm = 0.0013, // at 25°C
            SetchenowCoefficient = 0.12,
            Sources = new List<string> { "NIST Chemistry WebBook" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = false,
            OxidationState = 0
        });
    }

    private static void AddH2(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hydrogen (gas)",
            ChemicalFormula = "H₂(g)",
            Phase = CompoundPhase.Gas,
            MolecularWeight_g_mol = 2.016,
            GibbsFreeEnergyFormation_kJ_mol = 0.0,
            EnthalpyFormation_kJ_mol = 0.0,
            Entropy_J_molK = 130.68,
            HeatCapacity_J_molK = 28.82,
            HenrysLawConstant_mol_L_atm = 0.00078, // at 25°C
            Sources = new List<string> { "NIST Chemistry WebBook" },
            IsUserCompound = false
        });
    }

    private static void AddNH3(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Ammonia (gas)",
            ChemicalFormula = "NH₃(g)",
            Phase = CompoundPhase.Gas,
            MolecularWeight_g_mol = 17.03,
            GibbsFreeEnergyFormation_kJ_mol = -16.45,
            EnthalpyFormation_kJ_mol = -45.94,
            Entropy_J_molK = 192.77,
            HeatCapacity_J_molK = 35.06,
            HenrysLawConstant_mol_L_atm = 58.0, // at 25°C - very soluble
            Sources = new List<string> { "NIST Chemistry WebBook" },
            IsUserCompound = false
        });
    }

    // ==================== AQUEOUS DISSOLVED GASES ====================

    private static void AddDissolvedCO2(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Dissolved CO2",
            ChemicalFormula = "CO₂(aq)",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 44.01,
            GibbsFreeEnergyFormation_kJ_mol = -385.98,
            EnthalpyFormation_kJ_mol = -413.26,
            Entropy_J_molK = 119.36,
            Sources = new List<string> { "PHREEQC database", "Stumm & Morgan (1996)" },
            IsUserCompound = false
        });
    }

    private static void AddCarbonicAcid(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Carbonic Acid",
            ChemicalFormula = "H₂CO₃",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 62.03,
            GibbsFreeEnergyFormation_kJ_mol = -623.16,
            EnthalpyFormation_kJ_mol = -699.65,
            Entropy_J_molK = 187.4,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddBicarbonateIon(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Bicarbonate Ion",
            ChemicalFormula = "HCO₃⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 61.02,
            IonicCharge = -1,
            GibbsFreeEnergyFormation_kJ_mol = -586.85,
            EnthalpyFormation_kJ_mol = -691.11,
            Entropy_J_molK = 98.4,
            IonicConductivity_S_cm2_mol = 44.5,
            Sources = new List<string> { "PHREEQC database", "Nordstrom & Munoz (1994)" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = false
        });
    }

    private static void AddCarbonateIon(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Carbonate Ion",
            ChemicalFormula = "CO₃²⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 60.01,
            IonicCharge = -2,
            GibbsFreeEnergyFormation_kJ_mol = -527.90,
            EnthalpyFormation_kJ_mol = -675.23,
            Entropy_J_molK = -50.0,
            IonicConductivity_S_cm2_mol = 138.6,
            Sources = new List<string> { "PHREEQC database", "Nordstrom & Munoz (1994)" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = false
        });
    }

    private static void AddDissolvedH2S(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Dissolved H2S",
            ChemicalFormula = "H₂S(aq)",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 34.08,
            GibbsFreeEnergyFormation_kJ_mol = -27.87,
            EnthalpyFormation_kJ_mol = -39.3,
            Entropy_J_molK = 126.0,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddHS_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Bisulfide Ion",
            ChemicalFormula = "HS⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 33.07,
            IonicCharge = -1,
            GibbsFreeEnergyFormation_kJ_mol = 12.05,
            EnthalpyFormation_kJ_mol = -16.3,
            Entropy_J_molK = 67.0,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddS2_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Sulfide Ion",
            ChemicalFormula = "S²⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 32.06,
            IonicCharge = -2,
            GibbsFreeEnergyFormation_kJ_mol = 85.8,
            EnthalpyFormation_kJ_mol = 33.0,
            Entropy_J_molK = -14.6,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddDissolvedNH3(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Dissolved Ammonia",
            ChemicalFormula = "NH₃(aq)",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 17.03,
            GibbsFreeEnergyFormation_kJ_mol = -26.50,
            EnthalpyFormation_kJ_mol = -80.29,
            Entropy_J_molK = 111.3,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddNH4_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Ammonium Ion",
            ChemicalFormula = "NH₄⁺",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 18.04,
            IonicCharge = 1,
            GibbsFreeEnergyFormation_kJ_mol = -79.37,
            EnthalpyFormation_kJ_mol = -132.51,
            Entropy_J_molK = 113.4,
            IonicConductivity_S_cm2_mol = 73.4,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddDissolvedO2(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Dissolved Oxygen",
            ChemicalFormula = "O₂(aq)",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 32.00,
            GibbsFreeEnergyFormation_kJ_mol = 16.32,
            EnthalpyFormation_kJ_mol = -11.7,
            Entropy_J_molK = 110.9,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddDissolvedN2(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Dissolved Nitrogen",
            ChemicalFormula = "N₂(aq)",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 28.01,
            GibbsFreeEnergyFormation_kJ_mol = 18.7,
            EnthalpyFormation_kJ_mol = -10.54,
            Entropy_J_molK = 108.0,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddDissolvedCH4(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Dissolved Methane",
            ChemicalFormula = "CH₄(aq)",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 16.04,
            GibbsFreeEnergyFormation_kJ_mol = -34.33,
            EnthalpyFormation_kJ_mol = -89.04,
            Entropy_J_molK = 86.0,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    // ==================== ADDITIONAL AQUEOUS IONS ====================

    private static void AddSO4_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Sulfate Ion",
            ChemicalFormula = "SO₄²⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 96.06,
            IonicCharge = -2,
            GibbsFreeEnergyFormation_kJ_mol = -744.63,
            EnthalpyFormation_kJ_mol = -909.27,
            Entropy_J_molK = 18.5,
            IonicConductivity_S_cm2_mol = 160.0,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = false
        });
    }

    private static void AddHSO4_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Bisulfate Ion",
            ChemicalFormula = "HSO₄⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 97.07,
            IonicCharge = -1,
            GibbsFreeEnergyFormation_kJ_mol = -755.91,
            EnthalpyFormation_kJ_mol = -887.34,
            Entropy_J_molK = 131.8,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddNO3_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Nitrate Ion",
            ChemicalFormula = "NO₃⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 62.00,
            IonicCharge = -1,
            GibbsFreeEnergyFormation_kJ_mol = -111.34,
            EnthalpyFormation_kJ_mol = -207.36,
            Entropy_J_molK = 146.7,
            IonicConductivity_S_cm2_mol = 71.4,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false,
            OxidationState = 5
        });
    }

    private static void AddNO2_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Nitrite Ion",
            ChemicalFormula = "NO₂⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 46.01,
            IonicCharge = -1,
            GibbsFreeEnergyFormation_kJ_mol = -32.2,
            EnthalpyFormation_kJ_mol = -104.6,
            Entropy_J_molK = 123.0,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false,
            OxidationState = 3
        });
    }

    private static void AddPO4_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Phosphate Ion",
            ChemicalFormula = "PO₄³⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 94.97,
            IonicCharge = -3,
            GibbsFreeEnergyFormation_kJ_mol = -1018.8,
            EnthalpyFormation_kJ_mol = -1284.4,
            Entropy_J_molK = -221.8,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddHPO4_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hydrogen Phosphate Ion",
            ChemicalFormula = "HPO₄²⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 95.98,
            IonicCharge = -2,
            GibbsFreeEnergyFormation_kJ_mol = -1089.3,
            EnthalpyFormation_kJ_mol = -1299.0,
            Entropy_J_molK = -33.5,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddH2PO4_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Dihydrogen Phosphate Ion",
            ChemicalFormula = "H₂PO₄⁻",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 96.99,
            IonicCharge = -1,
            GibbsFreeEnergyFormation_kJ_mol = -1137.2,
            EnthalpyFormation_kJ_mol = -1302.6,
            Entropy_J_molK = 90.4,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddFe2_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Ferrous Iron Ion",
            ChemicalFormula = "Fe²⁺",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 55.85,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = -90.0,
            EnthalpyFormation_kJ_mol = -89.1,
            Entropy_J_molK = -137.7,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = true,
            OxidationState = 2
        });
    }

    private static void AddFe3_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Ferric Iron Ion",
            ChemicalFormula = "Fe³⁺",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 55.85,
            IonicCharge = 3,
            GibbsFreeEnergyFormation_kJ_mol = -16.7,
            EnthalpyFormation_kJ_mol = -48.5,
            Entropy_J_molK = -315.9,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = false,
            OxidationState = 3
        });
    }

    private static void AddMn2_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Manganese Ion",
            ChemicalFormula = "Mn²⁺",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 54.94,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = -228.1,
            EnthalpyFormation_kJ_mol = -220.8,
            Entropy_J_molK = -73.6,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = true
        });
    }

    private static void AddAl3_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Aluminum Ion",
            ChemicalFormula = "Al³⁺",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 26.98,
            IonicCharge = 3,
            GibbsFreeEnergyFormation_kJ_mol = -489.4,
            EnthalpyFormation_kJ_mol = -538.4,
            Entropy_J_molK = -325.0,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = true
        });
    }

    private static void AddPb2_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Lead Ion",
            ChemicalFormula = "Pb²⁺",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 207.2,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = -24.4,
            EnthalpyFormation_kJ_mol = -1.7,
            Entropy_J_molK = 10.5,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = true
        });
    }

    private static void AddZn2_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Zinc Ion",
            ChemicalFormula = "Zn²⁺",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 65.38,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = -147.1,
            EnthalpyFormation_kJ_mol = -153.9,
            Entropy_J_molK = -112.1,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = true
        });
    }

    private static void AddCu2_Ion(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Copper Ion",
            ChemicalFormula = "Cu²⁺",
            Phase = CompoundPhase.Aqueous,
            MolecularWeight_g_mol = 63.55,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = 65.5,
            EnthalpyFormation_kJ_mol = 64.8,
            Entropy_J_molK = -99.6,
            Sources = new List<string> { "PHREEQC database" },
            IsUserCompound = false,
            IsPrimaryElementSpecies = true
        });
    }

    // ==================== ADDITIONAL MINERALS ====================

    private static void AddPyrite(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Pyrite",
            ChemicalFormula = "FeS₂",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            MolecularWeight_g_mol = 119.98,
            Density_g_cm3 = 5.02,
            GibbsFreeEnergyFormation_kJ_mol = -166.9,
            EnthalpyFormation_kJ_mol = -178.2,
            Entropy_J_molK = 52.93,
            LogKsp_25C = -18.48,
            SpecificSurfaceArea_m2_g = 0.05,
            ActivationEnergy_Dissolution_kJ_mol = 56.9,
            RateConstant_Dissolution_mol_m2_s = 3.02e-8,
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddMarkasite(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Marcasite",
            ChemicalFormula = "FeS₂",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            MolecularWeight_g_mol = 119.98,
            Density_g_cm3 = 4.89,
            GibbsFreeEnergyFormation_kJ_mol = -165.7,
            EnthalpyFormation_kJ_mol = -171.5,
            LogKsp_25C = -18.20,
            Sources = new List<string> { "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });
    }

    private static void AddAnhydrite(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Anhydrite",
            ChemicalFormula = "CaSO₄",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            MolecularWeight_g_mol = 136.14,
            Density_g_cm3 = 2.96,
            GibbsFreeEnergyFormation_kJ_mol = -1322.0,
            EnthalpyFormation_kJ_mol = -1434.5,
            Entropy_J_molK = 107.0,
            LogKsp_25C = -4.36,
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddGypsum(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Gypsum",
            ChemicalFormula = "CaSO₄·2H₂O",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            MolecularWeight_g_mol = 172.17,
            Density_g_cm3 = 2.32,
            GibbsFreeEnergyFormation_kJ_mol = -1797.2,
            EnthalpyFormation_kJ_mol = -2022.6,
            Entropy_J_molK = 194.1,
            LogKsp_25C = -4.58,
            MohsHardness = 2.0,
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddApatite(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hydroxyapatite",
            ChemicalFormula = "Ca₅(PO₄)₃OH",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Hexagonal,
            MolecularWeight_g_mol = 502.31,
            Density_g_cm3 = 3.16,
            GibbsFreeEnergyFormation_kJ_mol = -6338.4,
            EnthalpyFormation_kJ_mol = -6783.0,
            LogKsp_25C = -58.40,
            MohsHardness = 5.0,
            Sources = new List<string> { "PHREEQC database", "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });
    }

    private static void AddMagnetite(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Magnetite",
            ChemicalFormula = "Fe₃O₄",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            MolecularWeight_g_mol = 231.53,
            Density_g_cm3 = 5.20,
            GibbsFreeEnergyFormation_kJ_mol = -1015.4,
            EnthalpyFormation_kJ_mol = -1118.4,
            Entropy_J_molK = 146.1,
            LogKsp_25C = -10.47,
            MohsHardness = 5.5,
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddHematite(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hematite",
            ChemicalFormula = "Fe₂O₃",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            MolecularWeight_g_mol = 159.69,
            Density_g_cm3 = 5.26,
            GibbsFreeEnergyFormation_kJ_mol = -742.2,
            EnthalpyFormation_kJ_mol = -825.5,
            Entropy_J_molK = 87.4,
            LogKsp_25C = -3.96,
            MohsHardness = 6.0,
            Color = "Red to black",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddGoethite(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Goethite",
            ChemicalFormula = "FeOOH",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            MolecularWeight_g_mol = 88.85,
            Density_g_cm3 = 4.28,
            GibbsFreeEnergyFormation_kJ_mol = -488.6,
            EnthalpyFormation_kJ_mol = -559.3,
            Entropy_J_molK = 60.4,
            LogKsp_25C = 0.22,
            MohsHardness = 5.3,
            SpecificSurfaceArea_m2_g = 50.0,
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddFerrihydrite(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Ferrihydrite",
            ChemicalFormula = "Fe₅HO₈·4H₂O",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Amorphous,
            MolecularWeight_g_mol = 481.58,
            Density_g_cm3 = 3.96,
            GibbsFreeEnergyFormation_kJ_mol = -2338.5,
            LogKsp_25C = 4.89,
            SpecificSurfaceArea_m2_g = 300.0, // Very high surface area
            Sources = new List<string> { "PHREEQC database", "Dzombak & Morel (1990)" },
            IsUserCompound = false
        });
    }

    private static void AddSiderite(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Siderite",
            ChemicalFormula = "FeCO₃",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            MolecularWeight_g_mol = 115.86,
            Density_g_cm3 = 3.96,
            GibbsFreeEnergyFormation_kJ_mol = -682.8,
            EnthalpyFormation_kJ_mol = -755.9,
            Entropy_J_molK = 95.5,
            LogKsp_25C = -10.89,
            MohsHardness = 4.0,
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });
    }

    private static void AddRhodochrosite(CompoundLibrary library)
    {
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Rhodochrosite",
            ChemicalFormula = "MnCO₃",
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            MolecularWeight_g_mol = 114.95,
            Density_g_cm3 = 3.70,
            GibbsFreeEnergyFormation_kJ_mol = -816.7,
            EnthalpyFormation_kJ_mol = -889.4,
            Entropy_J_molK = 85.8,
            LogKsp_25C = -11.13,
            MohsHardness = 3.5,
            Color = "Pink to red",
            Sources = new List<string> { "Robie & Hemingway (1995)", "PHREEQC database" },
            IsUserCompound = false
        });
    }
}
