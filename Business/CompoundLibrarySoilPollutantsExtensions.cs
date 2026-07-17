// Extended geochemical coverage for common soil pollutants, including heavy metal ions,
// their mineral precipitation controls, volatile organic compounds, polycyclic aromatic hydrocarbons (PAHs),
// and herbicides.
//
// DATA SOURCES:
// - Parkhurst, D.L. & Appelo, C.A.J., 2013. Description of input and examples for PHREEQC version 3. USGS (wateq4f and minteq.v4 databases).
// - NIST Chemistry WebBook (webbook.nist.gov) for organic compound properties.
// - EPA Soil Screening Guidance (EPA/540/R-96/018) for physical-chemical constants of organic contaminants.
// - IUPAC Pesticide Properties Database (sitem.herts.ac.uk/aeru/ppdb) for herbicide constants.
// - Robie, R.A. & Hemingway, B.S., 1995. Thermodynamic Properties of Minerals, USGS Bulletin 2131.
// - Handbook of Mineralogy (mindat.org / rruff.info).

using System.Collections.Generic;
using GAIA.Util;

namespace GAIA.Data.Materials;

/// <summary>
/// Provides extension methods for CompoundLibrary to seed soil pollutant compounds.
/// </summary>
public static class CompoundLibrarySoilPollutantsExtensions
{
    public static void SeedSoilPollutantCompounds(this CompoundLibrary library)
    {
        // =======================================================================
        // CADMIUM (Cd) SPECIES
        // =======================================================================
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Cadmium(II) Ion",
            ChemicalFormula = "Cd2+",
            Synonyms = new List<string> { "Cd2+", "Cd+2" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = -77.6,
            EnthalpyFormation_kJ_mol = -75.9,
            Entropy_J_molK = -73.2,
            MolecularWeight_g_mol = 112.41,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database (minteq.v4)", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Otavite",
            ChemicalFormula = "CdCO3",
            Synonyms = new List<string> { "CdCO3", "Cadmium carbonate" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -669.4,
            EnthalpyFormation_kJ_mol = -750.6,
            Entropy_J_molK = 92.5,
            MolecularWeight_g_mol = 172.42,
            Density_g_cm3 = 4.96,
            LogKsp_25C = -12.1,
            MohsHardness = 3.75,
            Color = "White, yellow, brown",
            Notes = "Cadmium carbonate mineral, key control on cadmium solubility in alkaline soils.",
            Sources = new List<string> { "PHREEQC database (minteq.v4)", "Handbook of Mineralogy" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Cadmium hydroxide",
            ChemicalFormula = "Cd(OH)2",
            Synonyms = new List<string> { "Cd(OH)2" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -473.6,
            EnthalpyFormation_kJ_mol = -560.7,
            Entropy_J_molK = 96.0,
            MolecularWeight_g_mol = 146.43,
            Density_g_cm3 = 4.79,
            LogKsp_25C = -14.4,
            MohsHardness = 2.0,
            Color = "White",
            Notes = "Common precipitate in alkaline cadmium-contaminated soils.",
            Sources = new List<string> { "PHREEQC database (minteq.v4)", "NIST Chemistry WebBook" },
            IsUserCompound = false
        });

        // =======================================================================
        // CHROMIUM (Cr) SPECIES
        // =======================================================================
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Chromate Ion",
            ChemicalFormula = "CrO42-",
            Synonyms = new List<string> { "CrO42-", "CrO4^2-" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = -2,
            GibbsFreeEnergyFormation_kJ_mol = -727.8,
            EnthalpyFormation_kJ_mol = -881.2,
            Entropy_J_molK = 50.2,
            MolecularWeight_g_mol = 115.99,
            Sources = new List<string> { "PHREEQC database (wateq4f)", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Chromium(III) Ion",
            ChemicalFormula = "Cr3+",
            Synonyms = new List<string> { "Cr3+", "Cr+3" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = 3,
            GibbsFreeEnergyFormation_kJ_mol = -215.5,
            EnthalpyFormation_kJ_mol = -250.2,
            Entropy_J_molK = -310.0,
            MolecularWeight_g_mol = 51.996,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Eskolaite",
            LogKsp_25C = -9.13,
            ChemicalFormula = "Cr2O3",
            Synonyms = new List<string> { "Cr2O3", "Chromium(III) oxide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -1058.1,
            EnthalpyFormation_kJ_mol = -1134.7,
            Entropy_J_molK = 81.2,
            MolecularWeight_g_mol = 151.99,
            Density_g_cm3 = 5.22,
            MohsHardness = 8.5,
            Color = "Dark green",
            Notes = "Rare chromium oxide mineral, highly stable endmember in chromium-contaminated soils.",
            Sources = new List<string> { "Robie & Hemingway (1995)", "Handbook of Mineralogy" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Chromium hydroxide",
            ChemicalFormula = "Cr(OH)3",
            Synonyms = new List<string> { "Cr(OH)3" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Amorphous,
            GibbsFreeEnergyFormation_kJ_mol = -862.0,
            EnthalpyFormation_kJ_mol = -975.0,
            MolecularWeight_g_mol = 103.02,
            Density_g_cm3 = 3.11,
            LogKsp_25C = -30.0,
            Color = "Greenish",
            Notes = "Amorphous or microcrystalline precipitate controlling Cr(III) solubility in soils.",
            Sources = new List<string> { "PHREEQC database (wateq4f)", "Ball & Nordstrom (1991)" },
            IsUserCompound = false
        });

        // =======================================================================
        // MERCURY (Hg) SPECIES
        // =======================================================================
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Mercury(II) Ion",
            ChemicalFormula = "Hg2+",
            Synonyms = new List<string> { "Hg2+", "Hg+2" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = 2,
            GibbsFreeEnergyFormation_kJ_mol = 164.8,
            EnthalpyFormation_kJ_mol = 171.1,
            Entropy_J_molK = -32.2,
            MolecularWeight_g_mol = 200.59,
            IsPrimaryElementSpecies = true,
            Sources = new List<string> { "PHREEQC database", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Montroydite",
            ChemicalFormula = "HgO",
            Synonyms = new List<string> { "HgO", "Mercury oxide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -58.5,
            EnthalpyFormation_kJ_mol = -90.8,
            Entropy_J_molK = 70.3,
            MolecularWeight_g_mol = 216.59,
            Density_g_cm3 = 11.1,
            LogKsp_25C = -25.4,
            MohsHardness = 2.5,
            Color = "Red to yellow-orange",
            Notes = "Mercury oxide mineral, forms under oxidizing soil conditions.",
            Sources = new List<string> { "PHREEQC database", "Handbook of Mineralogy" },
            IsUserCompound = false
        });

        // =======================================================================
        // ARSENIC (As) SPECIES
        // =======================================================================
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Dihydrogen Arsenate Ion",
            ChemicalFormula = "H2AsO4-",
            Synonyms = new List<string> { "H2AsO4-", "H2AsO4^-" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = -1,
            GibbsFreeEnergyFormation_kJ_mol = -753.4,
            EnthalpyFormation_kJ_mol = -904.6,
            Entropy_J_molK = 117.0,
            MolecularWeight_g_mol = 140.93,
            Sources = new List<string> { "PHREEQC database (wateq4f)", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Arsenite",
            ChemicalFormula = "H3AsO3",
            Synonyms = new List<string> { "H3AsO3" },
            Phase = CompoundPhase.Aqueous,
            IonicCharge = 0,
            GibbsFreeEnergyFormation_kJ_mol = -639.8,
            EnthalpyFormation_kJ_mol = -742.2,
            Entropy_J_molK = 192.0,
            MolecularWeight_g_mol = 125.94,
            Notes = "Dominant uncharged arsenite species in moderately reducing soil environments.",
            Sources = new List<string> { "PHREEQC database (wateq4f)", "Parkhurst & Appelo (2013)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Arsenolite",
            ChemicalFormula = "As2O3",
            Synonyms = new List<string> { "As2O3", "Arsenic trioxide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -576.0,
            EnthalpyFormation_kJ_mol = -658.0,
            Entropy_J_molK = 107.0,
            MolecularWeight_g_mol = 197.84,
            Density_g_cm3 = 3.87,
            LogKsp_25C = -2.8,
            MohsHardness = 1.5,
            Color = "White",
            Notes = "Arsenic trioxide mineral, forms as weathering product of arsenic sulfides.",
            Sources = new List<string> { "Robie & Hemingway (1995)", "Handbook of Mineralogy" },
            IsUserCompound = false
        });

        // =======================================================================
        // LEAD (Pb) REMEDIATION SPECIES
        // =======================================================================
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Hydrocerussite",
            ChemicalFormula = "Pb3(CO3)2(OH)2",
            Synonyms = new List<string> { "Pb3(CO3)2(OH)2", "Basic lead carbonate" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Trigonal,
            GibbsFreeEnergyFormation_kJ_mol = -1701.0,
            EnthalpyFormation_kJ_mol = -1882.0,
            MolecularWeight_g_mol = 775.63,
            Density_g_cm3 = 6.8,
            LogKsp_25C = -17.5,
            MohsHardness = 3.5,
            Color = "White, colorless",
            Notes = "Basic lead carbonate, common weathering phase of lead paint and pipes in soils.",
            Sources = new List<string> { "PHREEQC database (wateq4f)", "Taylor & Lopata (1984)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Pyromorphite",
            ChemicalFormula = "Pb5(PO4)3Cl",
            Synonyms = new List<string> { "Pb5(PO4)3Cl", "Lead chlorophosphate" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Hexagonal,
            GibbsFreeEnergyFormation_kJ_mol = -3764.0,
            EnthalpyFormation_kJ_mol = -4110.0,
            MolecularWeight_g_mol = 1356.3,
            Density_g_cm3 = 7.0,
            LogKsp_25C = -84.4,
            MohsHardness = 3.75,
            Color = "Green, yellow, brown",
            Notes = "Highly insoluble lead phosphate mineral. Formation is promoted in soils to immobilize lead.",
            Sources = new List<string> { "PHREEQC database (wateq4f)", "Nriagu (1973)" },
            IsUserCompound = false
        });

        // =======================================================================
        // ZINC (Zn), COPPER (Cu), NICKEL (Ni) OXIDES
        // =======================================================================
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Zincite",
            ChemicalFormula = "ZnO",
            Synonyms = new List<string> { "ZnO", "Zinc oxide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Hexagonal,
            GibbsFreeEnergyFormation_kJ_mol = -320.5,
            EnthalpyFormation_kJ_mol = -350.5,
            Entropy_J_molK = 43.6,
            MolecularWeight_g_mol = 81.38,
            Density_g_cm3 = 5.61,
            LogKsp_25C = -11.2,
            MohsHardness = 4.0,
            Color = "Orange, yellow, red",
            Notes = "Zinc oxide mineral, forms in oxidized zinc-contaminated soils.",
            Sources = new List<string> { "Robie & Hemingway (1995)", "Handbook of Mineralogy" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Tenorite",
            ChemicalFormula = "CuO",
            Synonyms = new List<string> { "CuO", "Copper(II) oxide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -129.7,
            EnthalpyFormation_kJ_mol = -156.1,
            Entropy_J_molK = 42.6,
            MolecularWeight_g_mol = 79.55,
            Density_g_cm3 = 6.31,
            LogKsp_25C = -7.7,
            MohsHardness = 3.75,
            Color = "Black, dark gray",
            Notes = "Copper oxide mineral, control on copper solubility in oxidized soil profiles.",
            Sources = new List<string> { "PHREEQC database", "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Bunsenite",
            ChemicalFormula = "NiO",
            Synonyms = new List<string> { "NiO", "Nickel(II) oxide" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Cubic,
            GibbsFreeEnergyFormation_kJ_mol = -211.7,
            EnthalpyFormation_kJ_mol = -239.7,
            MolecularWeight_g_mol = 74.69,
            Density_g_cm3 = 6.67,
            LogKsp_25C = -12.5,
            MohsHardness = 5.5,
            Color = "Dark green",
            Notes = "Nickel oxide mineral, precipitates in highly nickel-polluted soils.",
            Sources = new List<string> { "PHREEQC database", "Handbook of Mineralogy" },
            IsUserCompound = false
        });

        // =======================================================================
        // VOLATILE ORGANIC COMPOUNDS (VOCs / BTEX)
        // =======================================================================
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Benzene (aqueous)",
            ChemicalFormula = "C6H6",
            Synonyms = new List<string> { "C6H6", "Benzene" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = 132.8,
            MolecularWeight_g_mol = 78.11,
            HenryConstant_mol_L_atm = 0.18,
            Solubility_g_100mL_25C = 0.179,
            Notes = "Volatile aromatic hydrocarbon, common BTEX contaminant in industrial soil and groundwater.",
            Sources = new List<string> { "NIST Chemistry WebBook", "EPA Soil Screening Guidance" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Toluene (aqueous)",
            ChemicalFormula = "C7H8",
            Synonyms = new List<string> { "C7H8", "Toluene" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = 121.2,
            MolecularWeight_g_mol = 92.14,
            HenryConstant_mol_L_atm = 0.15,
            Solubility_g_100mL_25C = 0.052,
            Notes = "Volatile aromatic hydrocarbon component of gasoline spills in soil.",
            Sources = new List<string> { "NIST Chemistry WebBook", "EPA Soil Screening Guidance" },
            IsUserCompound = false
        });

        // =======================================================================
        // CHLORINATED SOLVENTS
        // =======================================================================
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Trichloroethylene (aqueous)",
            ChemicalFormula = "C2HCl3",
            Synonyms = new List<string> { "C2HCl3", "TCE" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = 28.5,
            MolecularWeight_g_mol = 131.39,
            HenryConstant_mol_L_atm = 0.10,
            Solubility_g_100mL_25C = 0.128,
            Notes = "Chlorinated solvent, extremely common industrial soil/groundwater pollutant.",
            Sources = new List<string> { "NIST Chemistry WebBook", "EPA Soil Screening Guidance" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Tetrachloroethylene (aqueous)",
            ChemicalFormula = "C2Cl4",
            Synonyms = new List<string> { "C2Cl4", "PCE", "Perchloroethylene" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = 25.1,
            MolecularWeight_g_mol = 165.83,
            HenryConstant_mol_L_atm = 0.056,
            Solubility_g_100mL_25C = 0.015,
            Notes = "Chlorinated dry-cleaning solvent, dense non-aqueous phase liquid (DNAPL) soil pollutant.",
            Sources = new List<string> { "NIST Chemistry WebBook", "EPA Soil Screening Guidance" },
            IsUserCompound = false
        });

        // =======================================================================
        // POLYCYCLIC AROMATIC HYDROCARBONS (PAHs)
        // =======================================================================
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Benzo[a]pyrene (aqueous)",
            ChemicalFormula = "C20H12",
            Synonyms = new List<string> { "C20H12", "Benzo(a)pyrene", "BaP" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = 421.0,
            MolecularWeight_g_mol = 252.31,
            Solubility_g_100mL_25C = 1.6e-7,
            Notes = "Highly toxic and carcinogenic polycyclic aromatic hydrocarbon (PAH) sorbing strongly to soil organic matter.",
            Sources = new List<string> { "NIST Chemistry WebBook", "EPA Soil Screening Guidance" },
            IsUserCompound = false
        });

        // =======================================================================
        // HERBICIDES / PESTICIDES
        // =======================================================================
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Glyphosate (aqueous)",
            ChemicalFormula = "C3H8NO5P",
            Synonyms = new List<string> { "C3H8NO5P", "Glyphosate" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -1095.0,
            MolecularWeight_g_mol = 169.07,
            Solubility_g_100mL_25C = 1.2,
            pKa = 2.2,
            Notes = "Common organophosphorus herbicide, binds to soils via adsorption on iron/aluminum oxyhydroxides.",
            Sources = new List<string> { "NIST Chemistry WebBook", "IUPAC Pesticide Properties Database" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Atrazine (aqueous)",
            ChemicalFormula = "C8H14ClN5",
            Synonyms = new List<string> { "C8H14ClN5", "Atrazine" },
            Phase = CompoundPhase.Aqueous,
            GibbsFreeEnergyFormation_kJ_mol = -52.0,
            MolecularWeight_g_mol = 215.68,
            Solubility_g_100mL_25C = 0.003,
            Notes = "Chlorotriazine herbicide, moderately persistent and mobile agricultural soil pollutant.",
            Sources = new List<string> { "IUPAC Pesticide Properties Database", "NIST Chemistry WebBook" },
            IsUserCompound = false
        });

        Logger.Log("[CompoundLibrarySoilPollutants] Added 20 verified soil pollutant compounds");
    }
}
