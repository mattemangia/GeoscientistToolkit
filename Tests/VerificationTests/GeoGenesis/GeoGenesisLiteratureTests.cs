using GAIA.GeoGenesis;
using GAIA.GeoGenesis.Materials;
using GAIA.GeoGenesis.Thermodynamics;

namespace GAIA.VerificationTests;

/// <summary>
/// Validates the ported GeoGenesis thermodynamic engine against peer-reviewed literature values.
///
/// References:
///  • Plummer, L.N. &amp; Busenberg, E. (1982). The solubilities of calcite, aragonite and vaterite
///    in CO2–H2O solutions … Geochim. Cosmochim. Acta 46, 1011–1040.  (calcite log Ksp = −8.48)
///  • Robie, R.A. &amp; Hemingway, B.S. (1995). Thermodynamic Properties of Minerals … USGS Bull. 2131.
///  • Shock, E.L. &amp; Helgeson, H.C. (1988). Calculation of the thermodynamic … aqueous species.
///    Geochim. Cosmochim. Acta 52, 2009–2036.  (ΔGf° of Ca²⁺, HCO3⁻, CO3²⁻)
///  • Stumm, W. &amp; Morgan, J.J. (1996). Aquatic Chemistry, 3rd ed., Wiley.
///  • Langmuir, D. (1997). Aqueous Environmental Geochemistry, Prentice Hall. (Debye–Hückel A=0.509)
///  • Anderson, G.M. &amp; Crerar, D.A. (1993). Thermodynamics in Geochemistry, OUP. (van't Hoff)
/// </summary>
public class GeoGenesisLiteratureTests
{
    static GeoGenesisLiteratureTests() => Logger.EchoToConsole = false; // keep test output clean

    private static ChemicalCompound Require(string name)
    {
        var c = GeoGenesisModule.Library.Find(name);
        Assert.NotNull(c);
        return c!;
    }

    [Fact]
    public void Calcite_ThermodynamicData_MatchesRobieAndPlummerBusenberg()
    {
        var calcite = Require("Calcite");

        // Plummer & Busenberg (1982): log Ksp(calcite, 25 °C) = −8.48
        Assert.Equal(-8.48, calcite.LogKsp_25C!.Value, 2);

        // Robie & Hemingway (1995): ΔGf° = −1128.8 kJ/mol, ΔHf° = −1206.9 kJ/mol, S° = 92.9 J/mol·K
        Assert.Equal(-1128.8, calcite.GibbsFreeEnergyFormation_kJ_mol!.Value, 1);
        Assert.Equal(-1206.9, calcite.EnthalpyFormation_kJ_mol!.Value, 1);
        Assert.Equal(92.9, calcite.Entropy_J_molK!.Value, 1);

        // Density and molar volume (Handbook of Mineralogy)
        Assert.Equal(2.71, calcite.Density_g_cm3!.Value, 2);
        Assert.Equal(36.93, calcite.MolarVolume_cm3_mol!.Value, 1);
    }

    [Fact]
    public void AqueousCarbonateSpecies_GibbsEnergies_MatchShockHelgeson()
    {
        // Shock & Helgeson (1988) / CODATA standard apparent ΔGf° (kJ/mol)
        Assert.Equal(-553.5, Require("Calcium Ion").GibbsFreeEnergyFormation_kJ_mol!.Value, 1);
        Assert.Equal(-586.8, Require("Bicarbonate").GibbsFreeEnergyFormation_kJ_mol!.Value, 1);
        Assert.Equal(-527.8, Require("Carbonate").GibbsFreeEnergyFormation_kJ_mol!.Value, 1);

        // Ionic charges must be correct for activity / charge-balance calculations
        Assert.Equal(2, Require("Calcium Ion").IonicCharge);
        Assert.Equal(-1, Require("Bicarbonate").IonicCharge);
        Assert.Equal(-2, Require("Carbonate").IonicCharge);
    }

    [Fact]
    public void CalciteDissolution_GibbsDerivedLogK_IsConsistentWithMeasuredKsp()
    {
        // Independently of the tabulated Ksp, the reaction generator must derive logK from the
        // Gibbs energies of formation:  CaCO3 -> Ca²⁺ + CO3²⁻
        //   ΔG° = ΔGf(Ca²⁺) + ΔGf(CO3²⁻) − ΔGf(calcite)
        //       = −553.5 + (−527.8) − (−1128.8) = +47.5 kJ/mol
        //   logK = −ΔG° / (2.303 R T) ≈ −8.3
        // which agrees with the measured −8.48 to within third-law/solubility uncertainty (~0.3).
        var gen = new ReactionGenerator(GeoGenesisModule.Library);
        var rxn = gen.GenerateSingleDissolutionReaction(Require("Calcite"));

        Assert.NotNull(rxn);
        Assert.Equal(-1.0, rxn!.Stoichiometry["Calcite"], 6);          // calcite consumed
        Assert.True(rxn.Stoichiometry.Values.Any(v => v > 0), "dissolution must produce aqueous ions");

        var logK = rxn.LogK_25C;
        Assert.InRange(logK, -8.8, -7.9); // brackets both the Gibbs-derived (−8.3) and measured (−8.48)
    }

    [Fact]
    public void VantHoff_CalciteSolubility_IsRetrograde()
    {
        // Calcite dissolution is exothermic (ΔH°rxn < 0), so log K decreases with temperature
        // (retrograde solubility) — calcite is LESS soluble in hotter water. This is exactly the
        // behaviour that drives calcite scaling in geothermal/well systems (Plummer & Busenberg 1982).
        var gen = new ReactionGenerator(GeoGenesisModule.Library);
        var rxn = gen.GenerateSingleDissolutionReaction(Require("Calcite"))!;

        Assert.True(rxn.DeltaH0_kJ_mol < 0, $"calcite dissolution should be exothermic, got ΔH={rxn.DeltaH0_kJ_mol}");

        var logK25 = rxn.CalculateLogK(298.15);
        var logK50 = rxn.CalculateLogK(323.15);
        var logK90 = rxn.CalculateLogK(363.15);

        Assert.True(logK50 < logK25, $"logK(50°C)={logK50} should be < logK(25°C)={logK25}");
        Assert.True(logK90 < logK50, $"logK(90°C)={logK90} should be < logK(50°C)={logK50}");
        // Magnitude sanity: P&B report ~0.3–0.4 log-unit drop from 25→90 °C.
        Assert.InRange(logK25 - logK90, 0.05, 1.0);
    }

    [Theory]
    // Langmuir (1997) Table: at low I the extended Debye–Hückel γ for mono/divalent ions
    [InlineData("Sodium Ion", 0.01, 0.88, 0.92)]   // monovalent, I=0.01  → γ≈0.90
    [InlineData("Sodium Ion", 0.05, 0.79, 0.86)]   // monovalent, I=0.05  → γ≈0.82
    [InlineData("Calcium Ion", 0.01, 0.66, 0.78)]  // divalent,   I=0.01  → γ≈0.67–0.74
    [InlineData("Calcium Ion", 0.05, 0.43, 0.62)]  // divalent,   I=0.05  → γ≈0.48–0.57
    public void DebyeHuckel_ActivityCoefficients_AreInLiteratureRange(
        string ion, double ionicStrength, double lo, double hi)
    {
        var calc = new ActivityCoefficientCalculator();
        var state = new ThermodynamicState
        {
            Temperature_K = 298.15,
            IonicStrength_molkg = ionicStrength
        };

        var gamma = calc.CalculateSingleIonActivityCoefficient(ion, state);
        Assert.InRange(gamma, lo, hi);
    }

    [Fact]
    public void DebyeHuckel_LimitingLaw_ReducesToMinusAzSqrtI_AtTrace()
    {
        // As I→0, log γ → −A z² √I with A ≈ 0.509 (25 °C). Test a divalent ion at very low I.
        var calc = new ActivityCoefficientCalculator();
        var I = 1e-4;
        var state = new ThermodynamicState { Temperature_K = 298.15, IonicStrength_molkg = I };

        var gamma = calc.CalculateSingleIonActivityCoefficient("Calcium Ion", state); // z = 2
        var logGamma = Math.Log10(gamma);
        var expected = -0.509 * 4.0 * Math.Sqrt(I); // z²=4

        Assert.Equal(expected, logGamma, 2); // within 0.01 log-unit of the limiting law
    }

    [Fact]
    public void Library_ContainsRichMineralAndChemicalDatabase()
    {
        var lib = GeoGenesisModule.Library;
        Assert.True(lib.Compounds.Count >= 150, $"expected a substantial database, got {lib.Compounds.Count}");
        Assert.True(lib.Elements.Count >= 118, $"expected a complete periodic table, got {lib.Elements.Count}");

        // Key minerals for the scaling / aquifer use-cases must be present.
        foreach (var m in new[] { "Calcite", "Dolomite", "Gypsum", "Quartz", "Halite" })
            Assert.NotNull(lib.Find(m));
    }

    [Fact]
    public void Library_PeriodicTableContainsEveryElement()
    {
        var lib = GeoGenesisModule.Library;
        var elements = lib.Elements;
        var atomicNumbers = elements.Select(e => e.AtomicNumber).ToHashSet();
        var symbols = elements.Select(e => e.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingAtomicNumbers = Enumerable.Range(1, 118).Where(n => !atomicNumbers.Contains(n)).ToArray();

        Assert.Empty(missingAtomicNumbers);
        Assert.Equal(118, elements.Count);
        Assert.Equal(118, atomicNumbers.Count);
        Assert.Equal(118, symbols.Count);
        Assert.All(elements, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.Symbol));
            Assert.InRange(e.Group, 1, 18);
            Assert.InRange(e.Period, 1, 7);
            Assert.NotNull(lib.FindElement(e.Symbol));
            Assert.NotNull(lib.FindElement(e.Name));
        });

        Assert.Equal(118, lib.FindElement("Og")?.AtomicNumber);
        Assert.Equal("Oganesson", lib.FindElement("Oganesson")?.Name);
    }

    [Fact]
    public void Library_ContainsExpandedGeochemicalCompoundCoverage()
    {
        var lib = GeoGenesisModule.Library;
        var expected = new[]
        {
            "Sylvite", "Carnallite", "Kieserite", "Mirabilite", "Glauberite",
            "Celestine", "Anglesite", "Fluorite", "Lepidocrocite", "Ilmenite",
            "Rutile", "Corundum", "Brucite", "Gibbsite", "Chalcopyrite",
            "Galena", "Sphalerite", "Cinnabar", "Hydroxyapatite", "Fluorapatite",
            "Monazite", "Vivianite", "Kaolinite", "Illite", "Montmorillonite", "Talc",
            "Aragonite", "Vaterite", "Strontianite", "Witherite", "Malachite",
            "Azurite", "Barite", "Borax", "Trona", "Niter", "Ankerite",
            "Dawsonite", "Analcime", "Chabazite", "Wollastonite", "Ettringite",
            "Hydromagnesite", "Chromite", "Spinel", "Grossular", "Almandine",
            "Sodalite", "Nepheline", "Epidote", "Clinozoisite"
        };

        Assert.True(lib.Compounds.Count >= 190, $"expected expanded compound coverage, got {lib.Compounds.Count}");
        foreach (var name in expected)
            Assert.NotNull(lib.Find(name));
    }
}
