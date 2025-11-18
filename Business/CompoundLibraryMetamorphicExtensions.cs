// GeoscientistToolkit/Business/CompoundLibraryMetamorphicExtensions.cs
//
// Extension for adding metamorphic minerals to the compound library.
// Focus on Al2SiO5 polymorphs (Kyanite, Andalusite, Sillimanite) for P-T diagrams.
//
// SOURCES:
// - Holland & Powell (2011): Thermodynamic dataset for metamorphic rocks
// - Robie & Hemingway (1995): USGS thermodynamic properties
// - Spear, F.S., 1993. Metamorphic Phase Equilibria and P-T-t Paths. MSA Monograph.

using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

public static class CompoundLibraryMetamorphicExtensions
{
    public static void SeedMetamorphicMinerals(this CompoundLibrary library)
    {
        // =======================================================================
        // Al2SiO5 POLYMORPHS - The classic metamorphic P-T indicator minerals
        // =======================================================================

        // KYANITE - High pressure polymorph
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Kyanite",
            ChemicalFormula = "Al2SiO5",
            Synonyms = new List<string> { "Al2SiO5", "Cyanite", "Disthene" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Triclinic,
            GibbsFreeEnergyFormation_kJ_mol = -2443.9,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -2594.3,
            Entropy_J_molK = 83.8,
            HeatCapacity_J_molK = 121.7,
            MolarVolume_cm3_mol = 44.09,
            MolecularWeight_g_mol = 162.05,
            Density_g_cm3 = 3.67,
            MohsHardness = 5.5,
            Color = "Blue, white, gray",
            Notes = "High P polymorph of Al2SiO5. Triple point: ~500degC, 3.8 kbar",
            Sources = new List<string> { "Holland & Powell (2011)", "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        // ANDALUSITE - Low pressure, low temperature polymorph
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Andalusite",
            ChemicalFormula = "Al2SiO5",
            Synonyms = new List<string> { "Al2SiO5", "Chiastolite" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -2442.7,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -2590.3,
            Entropy_J_molK = 93.2,
            HeatCapacity_J_molK = 122.7,
            MolarVolume_cm3_mol = 51.53,
            MolecularWeight_g_mol = 162.05,
            Density_g_cm3 = 3.15,
            MohsHardness = 7.5,
            Color = "Pink, red, white, brown",
            Notes = "Low P/T polymorph, contact metamorphism indicator",
            Sources = new List<string> { "Holland & Powell (2011)", "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        // SILLIMANITE - High temperature polymorph
        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Sillimanite",
            ChemicalFormula = "Al2SiO5",
            Synonyms = new List<string> { "Al2SiO5", "Fibrolite" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -2440.0,  // Holland & Powell 2011
            EnthalpyFormation_kJ_mol = -2587.8,
            Entropy_J_molK = 96.1,
            HeatCapacity_J_molK = 124.5,
            MolarVolume_cm3_mol = 49.90,
            MolecularWeight_g_mol = 162.05,
            Density_g_cm3 = 3.25,
            MohsHardness = 7.0,
            Color = "White, gray, brown",
            Notes = "High T polymorph, regional metamorphism at depth",
            Sources = new List<string> { "Holland & Powell (2011)", "Robie & Hemingway (1995)" },
            IsUserCompound = false
        });

        // =======================================================================
        // OTHER KEY METAMORPHIC INDEX MINERALS
        // =======================================================================

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Staurolite",
            ChemicalFormula = "Fe2Al9Si4O23(OH)",
            Synonyms = new List<string> { "Fe2Al9Si4O23(OH)" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -9076.7,
            EnthalpyFormation_kJ_mol = -9652.5,
            Entropy_J_molK = 585.0,
            HeatCapacity_J_molK = 650.0,
            MolarVolume_cm3_mol = 208.2,
            MolecularWeight_g_mol = 853.51,
            Density_g_cm3 = 3.75,
            MohsHardness = 7.5,
            Color = "Red-brown, brown",
            Notes = "Index mineral for amphibolite facies, medium-grade metamorphism",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Cordierite",
            ChemicalFormula = "Mg2Al4Si5O18",
            Synonyms = new List<string> { "Mg2Al4Si5O18", "Iolite" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Orthorhombic,
            GibbsFreeEnergyFormation_kJ_mol = -8986.8,
            EnthalpyFormation_kJ_mol = -9161.4,
            Entropy_J_molK = 407.5,
            HeatCapacity_J_molK = 460.0,
            MolarVolume_cm3_mol = 233.1,
            MolecularWeight_g_mol = 584.95,
            Density_g_cm3 = 2.51,
            MohsHardness = 7.0,
            Color = "Blue, gray, violet",
            Notes = "Low P indicator, contact metamorphism",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        library.AddOrUpdate(new ChemicalCompound
        {
            Name = "Chloritoid",
            ChemicalFormula = "FeAl2SiO5(OH)2",
            Synonyms = new List<string> { "FeAl2SiO5(OH)2" },
            Phase = CompoundPhase.Solid,
            CrystalSystem = CrystalSystem.Monoclinic,
            GibbsFreeEnergyFormation_kJ_mol = -2257.0,
            EnthalpyFormation_kJ_mol = -2395.6,
            Entropy_J_molK = 142.0,
            MolarVolume_cm3_mol = 69.5,
            MolecularWeight_g_mol = 220.82,
            Density_g_cm3 = 3.56,
            MohsHardness = 6.5,
            Color = "Dark green, black",
            Notes = "Indicative of blueschist and greenschist facies",
            Sources = new List<string> { "Holland & Powell (2011)" },
            IsUserCompound = false
        });

        Logger.Log("[CompoundLibraryMetamorphicExtensions] Added 6 metamorphic index minerals");
    }
}
