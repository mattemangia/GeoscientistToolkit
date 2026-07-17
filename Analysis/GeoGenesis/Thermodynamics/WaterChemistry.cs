// GAIA.GeoGenesis/Thermodynamics/WaterChemistry.cs
//
// Convenience layer for assembling an aqueous system from a simple list of dissolved species
// (mol/kgw), so the higher-level GUIs (reaction builder, geothermal coupling, CRAFT aquifer)
// and the CLI can set up a ThermodynamicState without hand-filling ElementalComposition.

using GAIA.GeoGenesis.Materials;

namespace GAIA.GeoGenesis.Thermodynamics;

/// <summary>
///     A water composition expressed as molalities (mol per kg of water) of named aqueous species,
///     plus temperature, pressure and pH. Knows how to materialise a <see cref="ThermodynamicState"/>.
/// </summary>
public sealed class WaterComposition
{
    /// <summary>Dissolved species → molality (mol/kgw). Keys must resolve in the compound library.</summary>
    public Dictionary<string, double> SpeciesMolality { get; } = new();

    public double Temperature_K { get; set; } = 298.15;
    public double Pressure_bar { get; set; } = 1.0;
    public double pH { get; set; } = 7.0;

    public WaterComposition Set(string species, double molality)
    {
        SpeciesMolality[species] = molality;
        return this;
    }

    /// <summary>Total dissolved solids (mg/L), approximated from species molality × molar mass.</summary>
    public double EstimateTdsMgL(CompoundLibrary library)
    {
        double tds = 0;
        foreach (var (name, m) in SpeciesMolality)
        {
            var c = library.Find(name);
            if (c?.MolecularWeight_g_mol is double mw) tds += m * mw * 1000.0; // g→mg, per kg≈per L
        }
        return tds;
    }

    /// <summary>
    ///     Build a <see cref="ThermodynamicState"/> (1 kg water basis) with SpeciesMoles and the
    ///     derived ElementalComposition. Charge from the species is preserved for charge balance.
    /// </summary>
    public ThermodynamicState ToState(CompoundLibrary library, ReactionGenerator generator)
    {
        var state = new ThermodynamicState
        {
            Temperature_K = Temperature_K,
            Pressure_bar = Pressure_bar,
            pH = pH,
            Volume_L = 1.0
        };

        var species = new Dictionary<string, double>(SpeciesMolality);
        DistributeCarbonateSystem(species, pH); // ensure HCO3⁻/CO3²⁻/CO2(aq) are consistent with pH

        foreach (var (name, molality) in species)
        {
            if (molality <= 0) continue;
            state.SpeciesMoles[name] = molality;

            var compound = library.Find(name);
            if (compound == null) continue;
            foreach (var (element, count) in generator.ParseChemicalFormula(compound.ChemicalFormula))
                state.ElementalComposition[element] =
                    state.ElementalComposition.GetValueOrDefault(element) + molality * count;
        }

        // Ensure the solvent elements are present so water-derived species can form.
        state.ElementalComposition["H"] = state.ElementalComposition.GetValueOrDefault("H") + 2 * 55.51;
        state.ElementalComposition["O"] = state.ElementalComposition.GetValueOrDefault("O") + 55.51;

        return state;
    }

    // Standard 25 °C carbonic-acid dissociation constants (Plummer & Busenberg 1982; Stumm & Morgan 1996).
    private const double PK1 = 6.352; // CO2(aq) + H2O ⇌ HCO3⁻ + H⁺
    private const double PK2 = 10.329; // HCO3⁻ ⇌ CO3²⁻ + H⁺

    /// <summary>
    ///     Speciate the dissolved-inorganic-carbon system so that CO2(aq), HCO3⁻ and CO3²⁻ are mutually
    ///     consistent with the specified pH. The user typically supplies alkalinity as HCO3⁻ (and/or
    ///     CO3²⁻); the carbonate ion activity is essential for carbonate-mineral saturation indices
    ///     (calcite/dolomite/aragonite). Total inorganic carbon is conserved and redistributed using
    ///     the Bjerrum relations:  [HCO3⁻]=αT/(…),  [CO3²⁻]=[HCO3⁻]·10^(pH−pK2),  [CO2]=[HCO3⁻]·10^(pK1−pH).
    /// </summary>
    private static void DistributeCarbonateSystem(Dictionary<string, double> species, double pH)
    {
        double total =
            species.GetValueOrDefault("Bicarbonate") + species.GetValueOrDefault("HCO3-") +
            species.GetValueOrDefault("Carbonate") + species.GetValueOrDefault("CO32-") +
            species.GetValueOrDefault("Aqueous Carbon Dioxide") + species.GetValueOrDefault("CO2(aq)");
        if (total <= 0) return;

        // Bjerrum fractions from pH.
        var h = Math.Pow(10, -pH);
        var k1 = Math.Pow(10, -PK1);
        var k2 = Math.Pow(10, -PK2);
        var denom = h * h + k1 * h + k1 * k2;
        var fCO2 = h * h / denom;
        var fHCO3 = k1 * h / denom;
        var fCO3 = k1 * k2 / denom;

        // Clear any carbonate aliases then write the canonical library names.
        foreach (var k in new[] { "Bicarbonate", "HCO3-", "Carbonate", "CO32-", "Aqueous Carbon Dioxide", "CO2(aq)" })
            species.Remove(k);

        species["Aqueous Carbon Dioxide"] = total * fCO2;
        species["Bicarbonate"] = total * fHCO3;
        species["Carbonate"] = total * fCO3;
    }
}
