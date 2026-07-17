// Additional geochemical coverage for common evaporites, ore minerals, oxides,
// hydroxides, phosphates, and clay minerals used by GeoGenesis workflows.

using GAIA.GeoGenesis;

namespace GAIA.GeoGenesis.Materials;

public static class CompoundLibraryGeochemicalExpansionExtensions
{
    public static void SeedGeochemicalExpansionCompounds(this CompoundLibrary library)
    {
        foreach (var seed in GeochemicalExpansionSolids)
            library.AddOrUpdate(seed.ToCompound());

        Logger.Log($"[CompoundLibraryGeochemicalExpansion] Added {GeochemicalExpansionSolids.Length} geochemical compounds");
    }

    private static readonly SolidSeed[] GeochemicalExpansionSolids =
    {
        // LogKsp_25C is the tabulated 25 °C solubility product for each mineral's dissolution to the
        // free aqueous ions used by the reactor (metal cations, CO3-2, SO4-2, PO4-3, F-, Cl-, Al+3,
        // H4SiO4, Fe+2/+3, HS-). Values are taken from the USGS PHREEQC databases (pitzer.dat for the
        // brine salts, wateq4f.dat, phreeqc.dat, llnl.dat) and the BRGM Thermoddem v1.10 database;
        // carbonate/phosphate/sulfide entries are converted to the CO3-2 / PO4-3 / HS- product
        // convention the solver uses. Without a tabulated value the dissolution log K defaults to
        // zero, which makes a soluble phase look supersaturated and precipitate far too early. A few
        // framework/borate phases (Sodalite, Scapolite, Leucite, Ulexite) and the REE phosphate
        // Monazite have no authoritative low-temperature aqueous Ksp; their values are literature
        // estimates and are marked as such.
        new("Sylvite", "KCl", CompoundPhase.Solid, CrystalSystem.Cubic, 74.55, 1.99, 2.0, "Colorless, white, red", "Potassium evaporite salt", "Potash salt") { LogKsp_25C = 0.90 },
        new("Carnallite", "KMgCl3*6H2O", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 277.85, 1.60, 2.5, "Colorless, white, reddish", "Hydrated potassium-magnesium chloride evaporite") { LogKsp_25C = 4.35 },
        new("Kieserite", "MgSO4*H2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 138.38, 2.57, 3.5, "Colorless, white, gray", "Hydrated magnesium sulfate evaporite") { LogKsp_25C = -0.123 },
        new("Epsomite", "MgSO4*7H2O", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 246.47, 1.68, 2.5, "Colorless, white", "Hydrated magnesium sulfate salt") { LogKsp_25C = -1.881 },
        new("Mirabilite", "Na2SO4*10H2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 322.19, 1.46, 2.0, "Colorless, white", "Hydrated sodium sulfate evaporite") { LogKsp_25C = -1.114 },
        new("Thenardite", "Na2SO4", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 142.04, 2.66, 2.5, "Colorless, white", "Anhydrous sodium sulfate evaporite") { LogKsp_25C = -0.34 },
        new("Glauberite", "Na2Ca(SO4)2", CompoundPhase.Solid, CrystalSystem.Monoclinic, 278.18, 2.75, 2.5, "Gray, yellowish, white", "Mixed sodium-calcium sulfate evaporite") { LogKsp_25C = -5.25 },
        new("Celestine", "SrSO4", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 183.68, 3.96, 3.5, "Blue, white, colorless", "Strontium sulfate mineral", "Celestite") { LogKsp_25C = -6.63 },
        new("Anglesite", "PbSO4", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 303.26, 6.30, 3.0, "White, gray, yellow", "Lead sulfate oxidation mineral") { LogKsp_25C = -7.79 },
        new("Fluorite", "CaF2", CompoundPhase.Solid, CrystalSystem.Cubic, 78.07, 3.18, 4.0, "Purple, green, colorless", "Calcium fluoride mineral", "Fluorspar") { LogKsp_25C = -10.6 },

        new("Lepidocrocite", "FeO(OH)", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 88.85, 4.09, 5.0, "Red-brown, orange-brown", "Iron oxyhydroxide corrosion and weathering phase") { LogKsp_25C = 1.85 },
        new("Ilmenite", "FeTiO3", CompoundPhase.Solid, CrystalSystem.Trigonal, 151.71, 4.72, 5.5, "Black, steel-gray", "Iron titanium oxide") { LogKsp_25C = 1.82 },
        new("Rutile", "TiO2", CompoundPhase.Solid, CrystalSystem.Tetragonal, 79.87, 4.23, 6.5, "Red-brown, black, yellow", "Titanium dioxide polymorph") { LogKsp_25C = -8.86 },
        new("Corundum", "Al2O3", CompoundPhase.Solid, CrystalSystem.Trigonal, 101.96, 4.00, 9.0, "Colorless, red, blue, gray", "Aluminum oxide mineral") { LogKsp_25C = 18.30 },
        new("Brucite", "Mg(OH)2", CompoundPhase.Solid, CrystalSystem.Trigonal, 58.32, 2.39, 2.5, "White, pale green, blue", "Magnesium hydroxide alteration mineral") { LogKsp_25C = 17.11 },
        new("Gibbsite", "Al(OH)3", CompoundPhase.Solid, CrystalSystem.Monoclinic, 78.00, 2.42, 3.0, "White, gray, greenish", "Aluminum hydroxide in bauxite and soils") { LogKsp_25C = 7.74 },
        new("Diaspore", "AlO(OH)", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 59.99, 3.40, 6.5, "White, gray, yellowish", "Aluminum oxyhydroxide polymorph") { LogKsp_25C = 6.87 },

        new("Chalcopyrite", "CuFeS2", CompoundPhase.Solid, CrystalSystem.Tetragonal, 183.52, 4.20, 4.0, "Brassy yellow", "Copper iron sulfide ore mineral") { LogKsp_25C = -33.99 },
        new("Galena", "PbS", CompoundPhase.Solid, CrystalSystem.Cubic, 239.27, 7.60, 2.5, "Lead-gray", "Lead sulfide ore mineral") { LogKsp_25C = -14.84 },
        new("Sphalerite", "ZnS", CompoundPhase.Solid, CrystalSystem.Cubic, 97.45, 4.05, 3.5, "Brown, black, yellow", "Zinc sulfide ore mineral", "Blende") { LogKsp_25C = -11.15 },
        new("Millerite", "NiS", CompoundPhase.Solid, CrystalSystem.Trigonal, 90.76, 5.50, 3.5, "Brassy yellow", "Nickel sulfide mineral") { LogKsp_25C = -8.04 },
        new("Cinnabar", "HgS", CompoundPhase.Solid, CrystalSystem.Trigonal, 232.66, 8.10, 2.5, "Red, scarlet", "Mercury sulfide mineral") { LogKsp_25C = -39.0 },
        new("Arsenopyrite", "FeAsS", CompoundPhase.Solid, CrystalSystem.Monoclinic, 162.83, 6.07, 5.5, "Silver-white, steel-gray", "Iron arsenic sulfide ore mineral") { LogKsp_25C = -14.45 },

        new("Hydroxyapatite", "Ca5(PO4)3OH", CompoundPhase.Solid, CrystalSystem.Hexagonal, 502.31, 3.16, 5.0, "White, gray, yellow", "Calcium phosphate apatite endmember") { LogKsp_25C = -44.3 },
        new("Fluorapatite", "Ca5(PO4)3F", CompoundPhase.Solid, CrystalSystem.Hexagonal, 504.30, 3.20, 5.0, "Green, blue, brown, colorless", "Fluorine-rich apatite endmember") { LogKsp_25C = -59.6 },
        new("Monazite", "CePO4", CompoundPhase.Solid, CrystalSystem.Monoclinic, 235.09, 5.15, 5.0, "Yellow-brown, reddish-brown", "Rare-earth phosphate mineral; log Ksp is a literature estimate (Cetiner et al. 2005)") { LogKsp_25C = -25.5 },
        new("Vivianite", "Fe3(PO4)2*8H2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 501.61, 2.68, 2.0, "Blue, green, colorless when fresh", "Hydrated iron phosphate mineral") { LogKsp_25C = -36.0 },

        new("Kaolinite", "Al2Si2O5(OH)4", CompoundPhase.Solid, CrystalSystem.Triclinic, 258.16, 2.60, 2.5, "White, cream, pale yellow", "Common clay mineral") { LogKsp_25C = 7.44 },
        new("Illite", "K0.65Al2.65Si3.35O10(OH)2", CompoundPhase.Solid, CrystalSystem.Monoclinic, 389.34, 2.75, 2.0, "White, gray, greenish", "Potassium-rich mica clay mineral") { LogKsp_25C = 13.04 },
        new("Montmorillonite", "Na0.33Al1.67Mg0.33Si4O10(OH)2*nH2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 367.00, 2.35, 1.5, "White, gray, pale green", "Expandable smectite clay mineral") { LogKsp_25C = 6.90 },
        new("Talc", "Mg3Si4O10(OH)2", CompoundPhase.Solid, CrystalSystem.Monoclinic, 379.27, 2.75, 1.0, "White, pale green, gray", "Hydrated magnesium silicate alteration mineral") { LogKsp_25C = 24.93 },

        new("Aragonite", "CaCO3", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 100.09, 2.95, 3.5, "White, colorless, gray", "Orthorhombic calcium carbonate polymorph") { LogKsp_25C = -8.34 },
        new("Vaterite", "CaCO3", CompoundPhase.Solid, CrystalSystem.Hexagonal, 100.09, 2.54, 3.0, "White, colorless", "Metastable calcium carbonate polymorph") { LogKsp_25C = -7.90 },
        new("Strontianite", "SrCO3", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 147.63, 3.76, 3.5, "White, gray, pale green", "Strontium carbonate mineral") { LogKsp_25C = -9.27 },
        new("Witherite", "BaCO3", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 197.34, 4.29, 3.5, "White, gray, yellowish", "Barium carbonate mineral") { LogKsp_25C = -8.56 },
        new("Cerussite", "PbCO3", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 267.21, 6.55, 3.5, "White, gray, colorless", "Lead carbonate oxidation mineral") { LogKsp_25C = -13.13 },
        new("Smithsonite", "ZnCO3", CompoundPhase.Solid, CrystalSystem.Trigonal, 125.39, 4.45, 4.5, "White, green, blue, brown", "Zinc carbonate oxidation mineral") { LogKsp_25C = -10.0 },
        new("Malachite", "Cu2CO3(OH)2", CompoundPhase.Solid, CrystalSystem.Monoclinic, 221.11, 3.90, 4.0, "Bright green", "Copper carbonate hydroxide alteration mineral") { LogKsp_25C = -5.16 },
        new("Azurite", "Cu3(CO3)2(OH)2", CompoundPhase.Solid, CrystalSystem.Monoclinic, 344.67, 3.80, 3.5, "Deep blue", "Copper carbonate hydroxide alteration mineral") { LogKsp_25C = -16.91 },
        new("Barite", "BaSO4", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 233.39, 4.50, 3.5, "White, colorless, yellow, blue", "Barium sulfate mineral", "Baryte") { LogKsp_25C = -9.97 },

        new("Borax", "Na2B4O7*10H2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 381.37, 1.73, 2.5, "Colorless, white", "Hydrated sodium borate evaporite") { LogKsp_25C = 12.04 },
        new("Ulexite", "NaCaB5O6(OH)6*5H2O", CompoundPhase.Solid, CrystalSystem.Triclinic, 405.24, 1.96, 2.5, "White, silky", "Hydrated sodium calcium borate; log Ksp is a literature estimate") { LogKsp_25C = 5.9 },
        new("Colemanite", "CaB3O4(OH)3*H2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 253.32, 2.42, 4.5, "Colorless, white, gray", "Hydrated calcium borate mineral") { LogKsp_25C = 21.51 },
        new("Natron", "Na2CO3*10H2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 286.14, 1.46, 1.5, "Colorless, white", "Hydrated sodium carbonate evaporite") { LogKsp_25C = -1.31 },
        new("Trona", "Na3H(CO3)2*2H2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 226.03, 2.14, 2.5, "Colorless, white, gray", "Sodium carbonate bicarbonate evaporite") { LogKsp_25C = -11.12 },
        new("Nahcolite", "NaHCO3", CompoundPhase.Solid, CrystalSystem.Monoclinic, 84.01, 2.21, 2.5, "Colorless, white", "Sodium bicarbonate mineral") { LogKsp_25C = -10.88 },
        new("Nitratine", "NaNO3", CompoundPhase.Solid, CrystalSystem.Trigonal, 84.99, 2.26, 1.5, "Colorless, white, gray", "Sodium nitrate evaporite", "Soda niter") { LogKsp_25C = 1.05 },
        new("Niter", "KNO3", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 101.10, 2.11, 2.0, "Colorless, white", "Potassium nitrate evaporite", "Saltpeter") { LogKsp_25C = -0.21 },
        new("Bischofite", "MgCl2*6H2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 203.30, 1.57, 1.5, "Colorless, white", "Hydrated magnesium chloride evaporite") { LogKsp_25C = 4.455 },
        new("Polyhalite", "K2Ca2Mg(SO4)4*2H2O", CompoundPhase.Solid, CrystalSystem.Triclinic, 602.88, 2.78, 3.5, "White, gray, brick-red", "Hydrated potassium calcium magnesium sulfate") { LogKsp_25C = -13.744 },

        new("Ankerite", "CaFe(CO3)2", CompoundPhase.Solid, CrystalSystem.Trigonal, 215.99, 3.05, 3.5, "White, gray, brown", "Iron-rich dolomite-group carbonate; log Ksp is a literature estimate") { LogKsp_25C = -19.0 },
        new("Dawsonite", "NaAlCO3(OH)2", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 144.00, 2.44, 3.0, "White, colorless", "Sodium aluminum carbonate hydroxide") { LogKsp_25C = -6.00 },
        new("Analcime", "NaAlSi2O6*H2O", CompoundPhase.Solid, CrystalSystem.Cubic, 220.15, 2.26, 5.5, "White, colorless, gray", "Zeolite mineral common in altered volcanic rocks") { LogKsp_25C = 6.65 },
        new("Chabazite", "CaAl2Si4O12*6H2O", CompoundPhase.Solid, CrystalSystem.Trigonal, 506.45, 2.08, 4.5, "Colorless, white, pink", "Calcium zeolite mineral") { LogKsp_25C = 11.54 },
        new("Laumontite", "CaAl2Si4O12*4H2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 470.42, 2.30, 3.5, "White, colorless, pink", "Hydrated calcium zeolite mineral") { LogKsp_25C = 11.70 },
        new("Prehnite", "Ca2Al2Si3O10(OH)2", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 410.38, 2.90, 6.5, "Pale green, white, yellow", "Low-grade metamorphic calcium aluminosilicate") { LogKsp_25C = 32.60 },
        new("Wollastonite", "CaSiO3", CompoundPhase.Solid, CrystalSystem.Triclinic, 116.16, 2.90, 5.0, "White, gray", "Calcium silicate mineral") { LogKsp_25C = 14.05 },
        new("Portlandite", "Ca(OH)2", CompoundPhase.Solid, CrystalSystem.Trigonal, 74.09, 2.24, 2.0, "Colorless, white", "Calcium hydroxide cement and alteration phase") { LogKsp_25C = 22.81 },
        new("Ettringite", "Ca6Al2(SO4)3(OH)12*26H2O", CompoundPhase.Solid, CrystalSystem.Trigonal, 1255.11, 1.78, 2.5, "Colorless, white", "Hydrated calcium aluminum sulfate cement phase") { LogKsp_25C = 57.0 },

        new("Huntite", "Mg3Ca(CO3)4", CompoundPhase.Solid, CrystalSystem.Trigonal, 353.03, 2.70, 1.5, "White, chalky", "Magnesium calcium carbonate mineral") { LogKsp_25C = -29.97 },
        new("Hydromagnesite", "Mg5(CO3)4(OH)2*4H2O", CompoundPhase.Solid, CrystalSystem.Monoclinic, 467.64, 2.20, 3.5, "White, colorless", "Hydrated magnesium carbonate mineral") { LogKsp_25C = -8.76 },
        new("Chromite", "FeCr2O4", CompoundPhase.Solid, CrystalSystem.Cubic, 223.84, 4.60, 5.5, "Black, brownish black", "Iron chromium spinel-group oxide") { LogKsp_25C = 15.13 },
        new("Spinel", "MgAl2O4", CompoundPhase.Solid, CrystalSystem.Cubic, 142.27, 3.60, 8.0, "Colorless, red, blue, black", "Magnesium aluminum oxide spinel") { LogKsp_25C = 37.86 },
        new("Magnesioferrite", "MgFe2O4", CompoundPhase.Solid, CrystalSystem.Cubic, 199.99, 4.52, 6.0, "Black, brownish black", "Magnesium ferrite spinel") { LogKsp_25C = 19.26 },
        new("Grossular", "Ca3Al2Si3O12", CompoundPhase.Solid, CrystalSystem.Cubic, 450.45, 3.60, 7.0, "Green, brown, colorless", "Calcium aluminum garnet") { LogKsp_25C = 49.37 },
        new("Andradite", "Ca3Fe2Si3O12", CompoundPhase.Solid, CrystalSystem.Cubic, 508.18, 3.85, 7.0, "Yellow, green, brown, black", "Calcium iron garnet") { LogKsp_25C = 33.79 },
        new("Almandine", "Fe3Al2Si3O12", CompoundPhase.Solid, CrystalSystem.Cubic, 497.75, 4.30, 7.5, "Deep red, brownish red", "Iron aluminum garnet") { LogKsp_25C = 42.18 },
        new("Spessartine", "Mn3Al2Si3O12", CompoundPhase.Solid, CrystalSystem.Cubic, 495.03, 4.19, 7.0, "Orange, red-brown", "Manganese aluminum garnet") { LogKsp_25C = 49.89 },
        new("Pyrope", "Mg3Al2Si3O12", CompoundPhase.Solid, CrystalSystem.Cubic, 403.13, 3.58, 7.5, "Deep red, purple-red", "Magnesium aluminum garnet") { LogKsp_25C = 58.93 },
        new("Sodalite", "Na8Al6Si6O24Cl2", CompoundPhase.Solid, CrystalSystem.Cubic, 969.21, 2.30, 5.5, "Blue, white, gray", "Feldspathoid mineral; log Ksp is a literature estimate") { LogKsp_25C = 103.0 },
        new("Nepheline", "NaAlSiO4", CompoundPhase.Solid, CrystalSystem.Hexagonal, 142.05, 2.60, 6.0, "White, gray, greenish", "Sodium aluminosilicate feldspathoid") { LogKsp_25C = 14.08 },
        new("Leucite", "KAlSi2O6", CompoundPhase.Solid, CrystalSystem.Tetragonal, 218.25, 2.47, 5.5, "White, gray", "Potassium aluminosilicate feldspathoid; log Ksp is a literature estimate") { LogKsp_25C = 6.4 },
        new("Scapolite", "Na4Al3Si9O24Cl", CompoundPhase.Solid, CrystalSystem.Tetragonal, 895.41, 2.65, 6.0, "White, gray, yellow, violet", "Aluminosilicate chloride-carbonate solid-solution group; log Ksp is a literature estimate") { LogKsp_25C = 9.0 },
        new("Epidote", "Ca2Al2FeSi3O12(OH)", CompoundPhase.Solid, CrystalSystem.Monoclinic, 482.25, 3.45, 6.5, "Pistachio green, yellow-green", "Sorosilicate alteration and metamorphic mineral") { LogKsp_25C = 32.23 },
        new("Clinozoisite", "Ca2Al3Si3O12(OH)", CompoundPhase.Solid, CrystalSystem.Monoclinic, 454.36, 3.35, 6.5, "Colorless, gray, pale green", "Aluminum epidote-group mineral") { LogKsp_25C = 41.90 },
        new("Boehmite", "AlO(OH)", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 59.99, 3.03, 3.5, "White, gray, yellowish", "Aluminum oxyhydroxide polymorph common in bauxite") { LogKsp_25C = 7.63 },
        new("Mullite", "Al6Si2O13", CompoundPhase.Solid, CrystalSystem.Orthorhombic, 426.05, 3.16, 7.0, "Colorless, white, pink", "High-temperature aluminosilicate ceramic and metamorphic phase") { LogKsp_25C = 50.51 }
    };

    private readonly record struct SolidSeed(
        string Name,
        string Formula,
        CompoundPhase Phase,
        CrystalSystem CrystalSystem,
        double MolecularWeight,
        double Density,
        double MohsHardness,
        string Color,
        string Notes,
        params string[] Aliases)
    {
        /// <summary>Tabulated solubility product (log Ksp, 25 °C) from a reference database, when known.</summary>
        public double? LogKsp_25C { get; init; }

        public ChemicalCompound ToCompound()
        {
            var synonyms = new List<string> { Formula };
            synonyms.AddRange(Aliases);

            return new ChemicalCompound
            {
                Name = Name,
                ChemicalFormula = Formula,
                Synonyms = synonyms.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Phase = Phase,
                CrystalSystem = CrystalSystem,
                MolecularWeight_g_mol = MolecularWeight,
                Density_g_cm3 = Density,
                MohsHardness = MohsHardness,
                Color = Color,
                Notes = Notes,
                LogKsp_25C = LogKsp_25C,
                Sources = new List<string>
                {
                    "Handbook of Mineralogy",
                    "Robie & Hemingway (1995)",
                    "PHREEQC database"
                },
                IsUserCompound = false
            };
        }
    }
}
