using GAIA.GeoGenesis;
using GAIA.GeoGenesis.Materials;
using GAIA.GeoGenesis.Thermodynamics;

namespace GAIA.VerificationTests;

/// <summary>
/// Validates that the GeoGenesis thermodynamic engine reproduces COMMON, PUBLISHED geochemical
/// reactions from first principles — the compound formation energies and the automatic
/// <see cref="ReactionGenerator"/> the simulators use — rather than from any hardcoded, scenario
/// specific answer. The reactions below are the bread-and-butter equilibria of carbonate, evaporite
/// and sulfate geochemistry; the test only checks the generated chemistry against peer-reviewed
/// equilibrium constants so that *any* simulation built on this engine inherits the right numbers.
///
/// References:
///  • Plummer, L.N. &amp; Busenberg, E. (1982) Geochim. Cosmochim. Acta 46, 1011 — calcite −8.48, aragonite −8.34.
///  • Langmuir, D. (1997) Aqueous Environmental Geochemistry — gypsum −4.58, anhydrite −4.36, fluorite −10.6,
///    barite −9.97, celestine −6.63.
///  • Nordstrom, D.K. et al. (1990) ACS Symp. Ser. 416 — dolomite ≈ −17.
///  • Rimstidt, J.D. (1997) Geochim. Cosmochim. Acta 61, 2553 — quartz (SiO2 + 2H2O = H4SiO4) −3.98.
///  • CRC Handbook of Chemistry and Physics — halite +1.58.
///  • Stumm, W. &amp; Morgan, J.J. (1996) Aquatic Chemistry, 3rd ed. — carbonic acid pKa1 6.35, pKa2 10.33; pKw 14.0.
/// </summary>
public class GeoGenesisRealReactionTests
{
    static GeoGenesisRealReactionTests() => Logger.EchoToConsole = false;

    private static readonly CompoundLibrary Lib = GeoGenesisModule.Library;
    private static readonly ReactionGenerator Gen = new(GeoGenesisModule.Library);

    private const double Ln10 = 2.302585092994046;
    private const double RkJ = 8.314462618e-3; // kJ/(mol·K)

    private static ChemicalCompound Require(string name)
    {
        var c = Lib.Find(name);
        Assert.NotNull(c);
        return c!;
    }

    /// <summary>logK from the reaction's ΔG° of formation — independent of any tabulated Ksp override.</summary>
    private static double GibbsLogK(ChemicalReaction r, double tK = 298.15) =>
        -r.DeltaG0_kJ_mol / (Ln10 * RkJ * tK);

    private static ThermodynamicState RichState()
    {
        // A solution that "contains" all the elements of the common systems, so the generator emits the
        // carbonate / sulfide / phosphate / water acid–base subsystems. Values are nominal (mol/L of element).
        var s = new ThermodynamicState { Temperature_K = 298.15, pH = 7.0 };
        foreach (var e in new[] { "H", "O", "C", "Ca", "Na", "Cl", "S", "Mg", "K", "Si", "F", "Ba", "Sr" })
            s.ElementalComposition[e] = 0.1;
        return s;
    }

    // ── Published solubility products the engine ships with (the data every simulation reads) ──────

    [Theory]
    [InlineData("Calcite", -8.48)]
    [InlineData("Aragonite", -8.34)]
    [InlineData("Gypsum", -4.58)]
    [InlineData("Anhydrite", -4.36)]
    [InlineData("Barite", -9.97)]
    [InlineData("Celestine", -6.63)]
    [InlineData("Fluorite", -10.6)]
    [InlineData("Halite", 1.58)]
    [InlineData("Quartz", -3.98)]
    [InlineData("Dolomite", -17.09)]
    public void TabulatedSolubilityProducts_MatchPublishedValues(string mineral, double logKsp)
    {
        var c = Require(mineral);
        Assert.NotNull(c.LogKsp_25C);
        Assert.True(Math.Abs(c.LogKsp_25C!.Value - logKsp) <= 0.3,
            $"{mineral} log Ksp = {c.LogKsp_25C.Value:F2}, published ≈ {logKsp:F2}");
    }

    // ── The generator must derive the same equilibria from formation energies (no hardcoding) ──────

    [Theory]
    [InlineData("Calcite", -8.48)]
    [InlineData("Aragonite", -8.34)]
    [InlineData("Gypsum", -4.58)]
    [InlineData("Halite", 1.58)]
    [InlineData("Fluorite", -10.6)]
    [InlineData("Barite", -9.97)]
    public void GeneratedDissolution_ReproducesPublishedEquilibrium(string mineral, double logKsp)
    {
        var rxn = Gen.GenerateSingleDissolutionReaction(Require(mineral));
        Assert.NotNull(rxn);

        // The mineral is consumed, aqueous ions are produced, and the reaction is balanced.
        Assert.Equal(-1.0, rxn!.Stoichiometry[mineral], 6);
        Assert.True(rxn.Stoichiometry.Values.Any(v => v > 0), "dissolution must release aqueous species");
        AssertMassAndChargeBalanced(rxn);

        // The equilibrium constant the simulator actually uses must match the published Ksp.
        Assert.True(Math.Abs(rxn.LogK_25C - logKsp) <= 0.5,
            $"{mineral}: generated logK={rxn.LogK_25C:F2} vs published {logKsp:F2}");

        // Where the full formation-energy data is present, the value derived from ΔG° (i.e. computed
        // from first principles, not the tabulated Ksp) must also land on the published constant.
        var gibbs = GibbsLogK(rxn);
        if (double.IsFinite(gibbs) && Math.Abs(rxn.DeltaG0_kJ_mol) > 1e-6)
            Assert.True(Math.Abs(gibbs - logKsp) <= 1.5,
                $"{mineral}: ΔG°-derived logK={gibbs:F2} vs published {logKsp:F2}");
    }

    [Fact]
    public void GeneratedDissolution_GypsumReleasesCalciumSulfateAndWater_MassAndChargeBalanced()
    {
        var rxn = Gen.GenerateSingleDissolutionReaction(Require("Gypsum"))!;

        // CaSO4·2H2O → Ca²⁺ + SO4²⁻ + 2 H2O
        Assert.True(rxn.Stoichiometry.GetValueOrDefault("Calcium Ion") > 0, "gypsum must release Ca²⁺");
        var sulfate = rxn.Stoichiometry.Keys.FirstOrDefault(k => Require(k).ChemicalFormula.Replace(" ", "").StartsWith("SO4"));
        Assert.False(string.IsNullOrEmpty(sulfate), "gypsum must release a sulfate species");
        Assert.True(rxn.Stoichiometry[sulfate!] > 0);

        AssertMassAndChargeBalanced(rxn);
    }

    [Fact]
    public void GeneratedDissolution_HaliteReleasesSodiumAndChloride_MassAndChargeBalanced()
    {
        var rxn = Gen.GenerateSingleDissolutionReaction(Require("Halite"))!;
        Assert.True(rxn.Stoichiometry.GetValueOrDefault("Sodium Ion") > 0, "halite must release Na⁺");
        Assert.True(rxn.Stoichiometry.GetValueOrDefault("Chloride Ion") > 0, "halite must release Cl⁻");
        AssertMassAndChargeBalanced(rxn);
    }

    // ── Carbonate acid–base system and water self-ionisation (the pH backbone of every brine) ──────

    [Fact]
    public void CarbonateSystem_DissociationConstants_MatchPublishedpKa()
    {
        var rxns = Gen.GenerateAcidBaseReactions(RichState());

        var ka1 = rxns.FirstOrDefault(r => r.Name.Contains("Carbonic Acid First", StringComparison.OrdinalIgnoreCase));
        var ka2 = rxns.FirstOrDefault(r => r.Name.Contains("Bicarbonate Dissociation", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(ka1);
        Assert.NotNull(ka2);

        // pKa = −logK. Published: pKa1 ≈ 6.35, pKa2 ≈ 10.33 (Stumm & Morgan 1996).
        var pka1 = -ka1!.LogK_25C;
        var pka2 = -ka2!.LogK_25C;
        Assert.InRange(pka1, 5.5, 7.5);
        Assert.InRange(pka2, 9.0, 11.5);
        Assert.True(pka2 > pka1, $"the second deprotonation must be weaker (pKa2 {pka2:F2} > pKa1 {pka1:F2})");
    }

    [Fact]
    public void WaterSelfIonisation_pKw_IsAboutFourteen()
    {
        var water = Gen.GenerateAcidBaseReactions(RichState())
            .FirstOrDefault(r => r.Name.Contains("Water dissociation", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(water);
        var pkw = -water!.LogK_25C;
        Assert.InRange(pkw, 13.0, 15.0); // published pKw = 14.0 at 25 °C
    }

    [Fact]
    public void CalciteDissolution_IsExothermic_RetrogradeSolubility()
    {
        // Calcite is famously LESS soluble in hotter water (retrograde) — the engine must get the sign
        // of ΔH right or geothermal scaling predictions invert.
        var rxn = Gen.GenerateSingleDissolutionReaction(Require("Calcite"))!;
        Assert.True(rxn.DeltaH0_kJ_mol < 0, $"calcite dissolution should be exothermic, got ΔH={rxn.DeltaH0_kJ_mol:F1} kJ/mol");
        Assert.True(rxn.CalculateLogK(363.15) < rxn.CalculateLogK(298.15),
            "calcite solubility must drop from 25 °C to 90 °C");
    }

    [Theory]
    [InlineData("Barite", "Barium Ion", "Sulfate Ion")]
    [InlineData("Celestine", "Strontium Ion", "Sulfate Ion")]
    [InlineData("Witherite", "Barium Ion", "Carbonate")]
    [InlineData("Strontianite", "Strontium Ion", "Carbonate")]
    [InlineData("Fluorite", "Calcium Ion", "Fluoride Ion")]
    [InlineData("Millerite", "Nickel Ion", "Sulfide")]
    [InlineData("Cinnabar", "Mercuric Ion", "Sulfide")]
    [InlineData("Monazite", "Cerium(III) Ion", "Phosphate Ion")]
    [InlineData("Chromite", "Chromium(III) Ion", "Ferrous Iron")]
    [InlineData("Rutile", "Aqueous Titanium Hydroxide")]
    [InlineData("Malachite", "Cupric Ion", "Carbonate")]
    [InlineData("Azurite", "Cupric Ion", "Carbonate")]
    public void GeneratedDissolution_NewSpeciesReactions_AreMassAndChargeBalanced(string mineral, string cation, string? anion = null)
    {
        var rxn = Gen.GenerateSingleDissolutionReaction(Require(mineral));
        Assert.NotNull(rxn);
        AssertMassAndChargeBalanced(rxn!);
        
        Assert.True(rxn!.Stoichiometry.GetValueOrDefault(cation) > 0, $"{mineral} must release {cation}");
        if (anion != null)
        {
            Assert.True(rxn.Stoichiometry.GetValueOrDefault(anion) > 0 || rxn.Stoichiometry.Keys.Any(k => k.Contains(anion, StringComparison.OrdinalIgnoreCase)), $"{mineral} must release {anion}");
        }
    }

    private static void AssertMassAndChargeBalanced(ChemicalReaction rxn)
    {
        var elements = new Dictionary<string, double>();
        double charge = 0;
        foreach (var (name, nu) in rxn.Stoichiometry)
        {
            var c = Require(name);
            foreach (var (el, count) in Gen.ParseChemicalFormula(c.ChemicalFormula))
                elements[el] = elements.GetValueOrDefault(el) + nu * count;
            charge += nu * (c.IonicCharge ?? 0);
        }
        foreach (var (el, total) in elements)
            Assert.True(Math.Abs(total) < 1e-6, $"element {el} not balanced: net {total:E2}");
        Assert.True(Math.Abs(charge) < 1e-6, $"charge not balanced: net {charge:E2}");
    }
}
