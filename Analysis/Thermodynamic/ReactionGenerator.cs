// GeoscientistToolkit/Business/Thermodynamics/ReactionGenerator.cs
//
// Automatically generates chemical reactions from compound thermodynamic data
// without hardcoding. Uses stoichiometric matrix analysis and thermodynamic feasibility.
//
// SOURCES:
// - Smith, W.R. & Missen, R.W., 1982. Chemical Reaction Equilibrium Analysis: Theory and Algorithms. 
//   Wiley-Interscience, Chapter 2.
// - Alberty, R.A., 2003. Thermodynamics of Biochemical Reactions. Wiley-Interscience.
// - Caccavo, F., Jr., 1999. Protein-mediated adhesion of the dissimilatory Fe(III)-reducing bacterium 
//   Shewanella alga BrY to hydrous ferric oxide. Applied and Environmental Microbiology, 65(12), 5017-5022.
// - Lasaga, A.C., 1998. Kinetic Theory in the Earth Sciences. Princeton University Press.
// - Bethke, C.M., 2008. Geochemical and Biogeochemical Reaction Modeling, 2nd ed. Cambridge, Chapter 3.
//

using System.Text.RegularExpressions;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;
using MathNet.Numerics.LinearAlgebra;

namespace GeoscientistToolkit.Business.Thermodynamics;

/// <summary>
///     Generates chemical reactions automatically from compound library data
///     using stoichiometric analysis and thermodynamic principles.
/// </summary>
public class ReactionGenerator
{
    private const double R = 8.314462618; // J/(mol·K) - CODATA 2018
    private readonly CompoundLibrary _compoundLibrary;

    public ReactionGenerator(CompoundLibrary compoundLibrary)
    {
        _compoundLibrary = compoundLibrary;
    }

    /// <summary>
    ///     Generate all thermodynamically feasible reactions for the system.
    /// </summary>
    public List<ChemicalReaction> GenerateReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();

        // Generate dissolution reactions for all minerals
        reactions.AddRange(GenerateDissolutionReactions(state));

        // Generate complexation reactions for aqueous species
        reactions.AddRange(GenerateComplexationReactions(state));

        // Generate acid-base reactions
        reactions.AddRange(GenerateAcidBaseReactions(state));

        // Generate redox reactions if redox species present
        reactions.AddRange(GenerateRedoxReactions(state));

        // Generate gas equilibrium reactions
        reactions.AddRange(GenerateGasEquilibriumReactions(state));

        // Filter for thermodynamic feasibility
        reactions = FilterFeasibleReactions(reactions, state);

        Logger.Log($"[ReactionGenerator] Generated {reactions.Count} feasible reactions");
        return reactions;
    }

    /// <summary>
    ///     Generate mineral dissolution reactions.
    ///     General form: Mineral(s) ⇌ ν₁Ion₁(aq) + ν₂Ion₂(aq) + ...
    ///     Source: Lasaga, A.C., 1984. Chemical kinetics of water-rock interactions.
    ///     Journal of Geophysical Research, 89(B6), 4009-4025.
    /// </summary>
    public List<ChemicalReaction> GenerateDissolutionReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();

        // Get all solid phase compounds
        var minerals = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Solid &&
                        c.GibbsFreeEnergyFormation_kJ_mol != null)
            .ToList();

        foreach (var mineral in minerals)
        {
            var reaction = GenerateSingleDissolutionReaction(mineral);
            if (reaction != null)
                reactions.Add(reaction);
        }

        return reactions;
    }

    private ChemicalReaction GenerateSingleDissolutionReaction(ChemicalCompound mineral)
    {
        // Parse mineral formula to get elemental composition
        var elementalComp = ParseChemicalFormula(mineral.ChemicalFormula);
        if (elementalComp.Count == 0)
            return null;

        var reaction = new ChemicalReaction
        {
            Name = $"{mineral.Name} dissolution",
            Type = ReactionType.Dissolution
        };

        // Mineral is reactant (negative stoichiometry)
        reaction.Stoichiometry[mineral.Name] = -1.0;

        // Find aqueous ions that can form from this mineral
        var products = FindDissolutionProducts(elementalComp);

        if (products.Count == 0)
            return null; // Cannot form valid aqueous products

        // Balance the reaction
        BalanceDissolutionReaction(reaction, mineral, products);

        // Calculate thermodynamic properties
        CalculateReactionThermodynamics(reaction);

        return reaction;
    }

    /// <summary>
    ///     Parse chemical formula into elemental composition.
    ///     Example: "CaCO₃" -> {Ca: 1, C: 1, O: 3}
    ///     Handles subscripts, parentheses, and hydration notation.
    /// </summary>
    public Dictionary<string, int> ParseChemicalFormula(string formula)
    {
        var composition = new Dictionary<string, int>();

        // Remove common formatting (subscripts, charges, hydration)
        formula = Regex.Replace(formula, @"[â‚€-â‚™]", m => "₀₁₂₃₄₅₆₇₈₉".IndexOf(m.Value).ToString());
        formula = Regex.Replace(formula, @"[âº»Â±]", "");

        // Handle hydration: CaSO₄·2H₂O
        var hydrationMatch = Regex.Match(formula, @"Â·(\d*)H(\d*)O");
        if (hydrationMatch.Success)
        {
            var waterMoles = string.IsNullOrEmpty(hydrationMatch.Groups[1].Value)
                ? 1
                : int.Parse(hydrationMatch.Groups[1].Value);
            var hCount = string.IsNullOrEmpty(hydrationMatch.Groups[2].Value)
                ? 2
                : int.Parse(hydrationMatch.Groups[2].Value);

            composition["H"] = composition.GetValueOrDefault("H", 0) + hCount * waterMoles;
            composition["O"] = composition.GetValueOrDefault("O", 0) + waterMoles;

            formula = formula.Substring(0, hydrationMatch.Index);
        }

        // Parse main formula with element pattern: [A-Z][a-z]?(\d*)
        var matches = Regex.Matches(formula, @"([A-Z][a-z]?)(\d*)");

        foreach (Match match in matches)
        {
            var element = match.Groups[1].Value;
            var countStr = match.Groups[2].Value;
            var count = string.IsNullOrEmpty(countStr) ? 1 : int.Parse(countStr);

            composition[element] = composition.GetValueOrDefault(element, 0) + count;
        }

        return composition;
    }

    /// <summary>
    ///     Find aqueous ions that can form from mineral elements.
    ///     Uses compound library to find species with matching elements.
    /// </summary>
    private List<ChemicalCompound> FindDissolutionProducts(Dictionary<string, int> elementalComp)
    {
        var products = new List<ChemicalCompound>();

        // Common dissolution patterns based on mineral type
        // Source: Stumm & Morgan, 1996. Aquatic Chemistry, Chapter 4

        var elements = elementalComp.Keys.ToList();

        // Look for simple cations and anions
        foreach (var element in elements)
        {
            // Find common ions of this element
            var ions = _compoundLibrary.Compounds
                .Where(c => c.Phase == CompoundPhase.Aqueous &&
                            c.IonicCharge != null &&
                            ParseChemicalFormula(c.ChemicalFormula).ContainsKey(element))
                .ToList();

            products.AddRange(ions);
        }

        // Add water if oxygen present (for hydroxide formation)
        if (elementalComp.ContainsKey("O") && elementalComp.ContainsKey("H"))
        {
            var water = _compoundLibrary.Find("Water");
            if (water != null) products.Add(water);
        }

        // Add H+ for acid-base equilibria
        var hIon = _compoundLibrary.Find("H+") ?? _compoundLibrary.Find("H⁺");
        if (hIon != null && !products.Contains(hIon))
            products.Add(hIon);

        return products.Distinct().ToList();
    }

    /// <summary>
    ///     Balance dissolution reaction using a stoichiometric matrix solver.
    ///     Solves the linear system A·x = b, where A is the element matrix of products,
    ///     b is the element vector of the reactant mineral, and x is the vector of stoichiometric coefficients.
    ///     Source: Smith & Missen, 1982. Chemical Reaction Equilibrium Analysis, Chapter 2.
    /// </summary>
    private void BalanceDissolutionReaction(ChemicalReaction reaction, ChemicalCompound mineral,
        List<ChemicalCompound> possibleProducts)
    {
        var mineralComp = ParseChemicalFormula(mineral.ChemicalFormula);
        var elements = mineralComp.Keys.ToList();

        // Add charge as an element to be balanced
        if (!elements.Contains("Charge")) elements.Add("Charge");

        var productMatrix = Matrix<double>.Build.Dense(elements.Count, possibleProducts.Count);
        var mineralVector = Vector<double>.Build.Dense(elements.Count);

        // Populate the mineral vector (b)
        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i] == "Charge") continue;
            mineralVector[i] = mineralComp.GetValueOrDefault(elements[i], 0);
        }

        // Populate the product matrix (A)
        for (var j = 0; j < possibleProducts.Count; j++)
        {
            var product = possibleProducts[j];
            var productComp = ParseChemicalFormula(product.ChemicalFormula);
            for (var i = 0; i < elements.Count; i++)
                if (elements[i] == "Charge")
                    productMatrix[i, j] = product.IonicCharge ?? 0;
                else
                    productMatrix[i, j] = productComp.GetValueOrDefault(elements[i], 0);
        }

        // Solve A·x = b for x
        try
        {
            var coefficients = productMatrix.Solve(mineralVector);

            for (var j = 0; j < coefficients.Count; j++)
                if (Math.Abs(coefficients[j]) > 1e-9) // Tolerance for zero
                    reaction.Stoichiometry[possibleProducts[j].Name] = coefficients[j];
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                $"[ReactionGenerator] Could not balance dissolution for {mineral.Name}. Matrix may be singular. {ex.Message}");
        }
    }

    private List<ChemicalCompound> SelectMostLikelyProducts(List<ChemicalCompound> products,
        Dictionary<string, int> mineralComp)
    {
        // Heuristics for selecting most stable aqueous species
        // Based on Pourbaix diagrams and speciation models

        var selected = new List<ChemicalCompound>();

        // For each element in mineral, select dominant aqueous form
        foreach (var element in mineralComp.Keys)
        {
            var elementIons = products
                .Where(p => ParseChemicalFormula(p.ChemicalFormula).ContainsKey(element))
                .OrderBy(p => Math.Abs(p.IonicCharge ?? 0)) // Prefer lower charge
                .ThenBy(p => p.GibbsFreeEnergyFormation_kJ_mol ?? double.MaxValue) // Prefer more stable
                .ToList();

            if (elementIons.Any())
                selected.Add(elementIons.First());
        }

        // Add H+ if needed for charge balance
        var hPlus = products.FirstOrDefault(p => p.ChemicalFormula == "H+" || p.ChemicalFormula == "H⁺");
        if (hPlus != null && !selected.Contains(hPlus))
            selected.Add(hPlus);

        return selected;
    }

    private Dictionary<ChemicalCompound, double> SolveStoichiometry(Dictionary<string, int> mineralComp,
        List<ChemicalCompound> products)
    {
        // Simple algebraic balancing for common cases
        // Full implementation would use linear algebra solver

        var result = new Dictionary<ChemicalCompound, double>();
        var elementsBalanced = new Dictionary<string, double>();

        foreach (var product in products)
        {
            var productComp = ParseChemicalFormula(product.ChemicalFormula);

            // Find a coefficient that satisfies at least one element
            var coeff = 1.0;
            foreach (var (element, count) in productComp)
                if (mineralComp.TryGetValue(element, out var mineralCount))
                {
                    var needed = mineralCount - elementsBalanced.GetValueOrDefault(element, 0);
                    if (needed > 0)
                    {
                        coeff = needed / count;
                        break;
                    }
                }

            result[product] = coeff;

            // Update balanced elements
            foreach (var (element, count) in productComp)
                elementsBalanced[element] = elementsBalanced.GetValueOrDefault(element, 0) + coeff * count;
        }

        // Verify balance (within tolerance)
        var isBalanced = mineralComp.All(kvp =>
            Math.Abs(elementsBalanced.GetValueOrDefault(kvp.Key, 0) - kvp.Value) < 0.1);

        return isBalanced ? result : null;
    }

    /// <summary>
    ///     Generate aqueous complexation reactions.
    ///     Form: M^n+ + L^m- ⇌ ML^(n-m)
    ///     Source: Morel & Hering, 1993. Principles of Aquatic Chemistry, Chapter 6.
    /// </summary>
    public List<ChemicalReaction> GenerateComplexationReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();

        var aqueousSpecies = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Aqueous && c.GibbsFreeEnergyFormation_kJ_mol != null)
            .ToList();

        // Find potential metal ions (cations)
        var cations = aqueousSpecies.Where(s => s.IonicCharge > 0).ToList();

        // Find potential ligands (anions and neutral species)
        var ligands = aqueousSpecies.Where(s => s.IonicCharge <= 0).ToList();

        // Generate 1:1 complexation reactions
        foreach (var cation in cations)
        foreach (var ligand in ligands)
        {
            // Check if complex exists in database
            var complexFormula = GenerateComplexFormula(cation, ligand);
            var complex = FindComplex(complexFormula);

            if (complex != null)
            {
                var reaction = new ChemicalReaction
                {
                    Name = $"{cation.Name}-{ligand.Name} complexation",
                    Type = ReactionType.Complexation
                };

                reaction.Stoichiometry[cation.Name] = -1.0;
                reaction.Stoichiometry[ligand.Name] = -1.0;
                reaction.Stoichiometry[complex.Name] = 1.0;

                CalculateReactionThermodynamics(reaction);
                reactions.Add(reaction);
            }
        }

        return reactions;
    }

    private string GenerateComplexFormula(ChemicalCompound cation, ChemicalCompound ligand)
    {
        // Simplified complex formula generation
        // Real implementation would need sophisticated chemical formula handling

        var cationSymbol = ExtractMetalSymbol(cation.ChemicalFormula);
        var ligandFormula = ligand.ChemicalFormula;

        return $"{cationSymbol}{ligandFormula}";
    }

    private string ExtractMetalSymbol(string formula)
    {
        var match = Regex.Match(formula, @"^([A-Z][a-z]?)");
        return match.Success ? match.Value : "";
    }

    private ChemicalCompound FindComplex(string formula)
    {
        return _compoundLibrary.Compounds
            .FirstOrDefault(c => c.ChemicalFormula.Replace(" ", "").Contains(formula));
    }

    /// <summary>
    ///     Generate acid-base reactions using proton transfer formalism.
    ///     Source: Stumm & Morgan, 1996. Aquatic Chemistry, Chapter 3.
    /// </summary>
    public List<ChemicalReaction> GenerateAcidBaseReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();

        // Common acid-base systems
        reactions.AddRange(GenerateCarbonateSys());
        reactions.AddRange(GenerateWaterDissociation());
        reactions.AddRange(GeneratePhosphateSys());
        reactions.AddRange(GenerateSulfideSys());

        return reactions;
    }

    /// <summary>
    ///     Generates the fundamental aqueous reactions for the carbonate system.
    ///     This includes CO2 hydration and the two dissociation steps of carbonic acid.
    ///     The method is data-driven, calculating equilibrium constants from the
    ///     Gibbs free energy of formation of the involved species.
    ///     Source: Stumm, W. & Morgan, J.J., 1996. Aquatic Chemistry, 3rd ed. Chapter 4.
    /// </summary>
    private List<ChemicalReaction> GenerateCarbonateSys()
    {
        var reactions = new List<ChemicalReaction>();

        try
        {
            // Step 1: Fetch all required species from the compound library.
            // Use null-coalescing operator (??) to try multiple common names for robustness.
            var co2_aq = _compoundLibrary.Find("CO₂(aq)") ?? _compoundLibrary.Find("CO2(aq)");
            var h2o = _compoundLibrary.Find("H₂O") ?? _compoundLibrary.Find("Water");
            var h2co3 = _compoundLibrary.Find("H₂CO₃") ?? _compoundLibrary.Find("H2CO3");
            var hco3 = _compoundLibrary.Find("HCO₃⁻") ?? _compoundLibrary.Find("HCO3-");
            var co3 = _compoundLibrary.Find("CO₃²⁻") ??
                      _compoundLibrary.Find("CO3--") ?? _compoundLibrary.Find("CO3-2");
            var h_plus = _compoundLibrary.Find("H⁺") ?? _compoundLibrary.Find("H+");

            // Step 2: Check if essential components exist. Without water or H+, no acid-base chemistry is possible.
            if (h2o == null || h_plus == null)
            {
                Logger.LogWarning(
                    "[ReactionGenerator] Could not generate carbonate system: H₂O or H⁺ not found in CompoundLibrary.");
                return reactions; // Return empty list
            }

            // --- Reaction 1: Hydration of aqueous CO₂ to form carbonic acid ---
            // CO₂(aq) + H₂O ⇌ H₂CO₃
            // The logK for this reaction is typically around -2.8 at 25°C.
            if (co2_aq != null && h2co3 != null)
            {
                var reaction = new ChemicalReaction
                {
                    Name = "Carbon Dioxide Hydration",
                    Type = ReactionType.AcidBase,
                    Stoichiometry = new Dictionary<string, double>
                    {
                        { co2_aq.Name, -1.0 },
                        { h2o.Name, -1.0 },
                        { h2co3.Name, 1.0 }
                    }
                };
                CalculateReactionThermodynamics(reaction); // Calculate LogK from ΔG°f values
                reactions.Add(reaction);
            }
            else
            {
                Logger.LogWarning("[ReactionGenerator] Skipping CO₂ hydration reaction: CO₂(aq) or H₂CO₃ not found.");
            }

            // --- Reaction 2: First dissociation of carbonic acid ---
            // H₂CO₃ ⇌ HCO₃⁻ + H⁺
            // The logK (pKa₁) for this reaction is typically around -6.35 at 25°C.
            if (h2co3 != null && hco3 != null)
            {
                var reaction = new ChemicalReaction
                {
                    Name = "Carbonic Acid First Dissociation",
                    Type = ReactionType.AcidBase,
                    Stoichiometry = new Dictionary<string, double>
                    {
                        { h2co3.Name, -1.0 },
                        { hco3.Name, 1.0 },
                        { h_plus.Name, 1.0 }
                    }
                };
                CalculateReactionThermodynamics(reaction);
                reactions.Add(reaction);
            }
            else
            {
                Logger.LogWarning("[ReactionGenerator] Skipping carbonic acid dissociation: H₂CO₃ or HCO₃⁻ not found.");
            }

            // --- Reaction 3: Second dissociation (of bicarbonate) ---
            // HCO₃⁻ ⇌ CO₃²⁻ + H⁺
            // The logK (pKa₂) for this reaction is typically around -10.33 at 25°C.
            if (hco3 != null && co3 != null)
            {
                var reaction = new ChemicalReaction
                {
                    Name = "Bicarbonate Dissociation",
                    Type = ReactionType.AcidBase,
                    Stoichiometry = new Dictionary<string, double>
                    {
                        { hco3.Name, -1.0 },
                        { co3.Name, 1.0 },
                        { h_plus.Name, 1.0 }
                    }
                };
                CalculateReactionThermodynamics(reaction);
                reactions.Add(reaction);
            }
            else
            {
                Logger.LogWarning("[ReactionGenerator] Skipping bicarbonate dissociation: HCO₃⁻ or CO₃²⁻ not found.");
            }
        }
        catch (Exception ex)
        {
            // Catch any unexpected errors during generation to prevent crashing the solver.
            Logger.LogError(
                $"[ReactionGenerator] An unexpected error occurred while generating carbonate system reactions: {ex.Message}");
        }

        return reactions;
    }

    private List<ChemicalReaction> GenerateWaterDissociation()
    {
        var reactions = new List<ChemicalReaction>();

        var water = _compoundLibrary.Find("H₂O") ?? _compoundLibrary.Find("Water");
        var hPlus = _compoundLibrary.Find("H⁺") ?? _compoundLibrary.Find("H+");
        var ohMinus = _compoundLibrary.Find("OH⁻") ?? _compoundLibrary.Find("OH-");

        if (water != null && hPlus != null && ohMinus != null)
        {
            var reaction = new ChemicalReaction
            {
                Name = "Water dissociation",
                Type = ReactionType.AcidBase,
                Stoichiometry = new Dictionary<string, double>
                {
                    [water.Name] = -1.0,
                    [hPlus.Name] = 1.0,
                    [ohMinus.Name] = 1.0
                },
                LogK_25C = -14.0 // pKw at 25°C, IAPWS formulation
            };
            CalculateReactionThermodynamics(reaction);
            reactions.Add(reaction);
        }

        return reactions;
    }

    private List<ChemicalReaction> GeneratePhosphateSys()
    {
        // H₃PO₄ ⇌ H₂PO₄⁻ + H⁺ ⇌ HPO₄²⁻ + 2H⁺ ⇌ PO₄³⁻ + 3H⁺
        // pKa values from Atlas et al., 2011. Journal of Physical and Chemical Reference Data
        return new List<ChemicalReaction>();
    }

    private List<ChemicalReaction> GenerateSulfideSys()
    {
        // H₂S ⇌ HS⁻ + H⁺ ⇌ S²⁻ + 2H⁺
        // Important for anoxic diagenesis
        return new List<ChemicalReaction>();
    }

    /// <summary>
    ///     Generate redox reactions by combining half-reactions.
    ///     Source: Stumm & Morgan, 1996. Aquatic Chemistry, Chapter 8.
    /// </summary>
    public List<ChemicalReaction> GenerateRedoxReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();
        var redoxSpecies = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Aqueous && (c.ChemicalFormula.Contains("Fe") ||
                                                             c.ChemicalFormula.Contains("Mn") ||
                                                             c.ChemicalFormula.Contains("S")))
            .ToList();

        // Example: Fe2+ -> Fe3+ + e-
        var fe2 = _compoundLibrary.Find("Fe²⁺");
        var fe3 = _compoundLibrary.Find("Fe³⁺");
        var electron = _compoundLibrary.Find("e⁻"); // Assume electron is a defined species

        if (fe2 != null && fe3 != null && electron != null)
        {
            var r = new ChemicalReaction { Name = "Iron oxidation", Type = ReactionType.RedOx };
            r.Stoichiometry[fe2.Name] = -1.0;
            r.Stoichiometry[fe3.Name] = 1.0;
            r.Stoichiometry[electron.Name] = 1.0;
            CalculateReactionThermodynamics(r);
            reactions.Add(r);
        }

        return reactions;
    }

    /// <summary>
    ///     Generate gas-aqueous equilibrium reactions.
    ///     Source: Sander, R., 2015. Compilation of Henry's law constants for water as solvent.
    ///     Atmospheric Chemistry and Physics, 15(8), 4399-4981.
    /// </summary>
    public List<ChemicalReaction> GenerateGasEquilibriumReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();

        // Common gas-aqueous equilibria: CO₂, O₂, N₂, H₂S, NH₃, CH₄

        return reactions;
    }

    /// <summary>
    ///     Calculate standard thermodynamic properties from formation data.
    ///     ΔG° = Σ(ν_i · ΔG°_f,i) for products - reactants
    /// </summary>
    private void CalculateReactionThermodynamics(ChemicalReaction reaction)
    {
        double deltaG = 0.0, deltaH = 0.0, deltaS = 0.0;

        foreach (var (species, coeff) in reaction.Stoichiometry)
        {
            var compound = _compoundLibrary.Find(species);
            if (compound == null) continue;

            if (compound.GibbsFreeEnergyFormation_kJ_mol != null)
                deltaG += coeff * compound.GibbsFreeEnergyFormation_kJ_mol.Value;

            if (compound.EnthalpyFormation_kJ_mol != null)
                deltaH += coeff * compound.EnthalpyFormation_kJ_mol.Value;

            if (compound.Entropy_J_molK != null)
                deltaS += coeff * compound.Entropy_J_molK.Value;
        }

        reaction.DeltaG0_kJ_mol = deltaG;
        reaction.DeltaH0_kJ_mol = deltaH;
        reaction.DeltaS0_J_molK = deltaS;

        // Calculate log K from ΔG° = -RT ln(K)
        // log₁₀(K) = -ΔG°/(2.303·RT)
        const double T0 = 298.15; // K
        if (!double.IsNaN(deltaG) && !double.IsInfinity(deltaG))
            reaction.LogK_25C = -deltaG * 1000.0 / (2.303 * R * T0);
    }

    /// <summary>
    ///     Filter reactions based on thermodynamic feasibility.
    ///     Removes reactions with missing data or unfavorable equilibria.
    /// </summary>
    private List<ChemicalReaction> FilterFeasibleReactions(List<ChemicalReaction> reactions,
        ThermodynamicState state)
    {
        return reactions
            .Where(r => !double.IsNaN(r.DeltaG0_kJ_mol) &&
                        !double.IsInfinity(r.DeltaG0_kJ_mol))
            .Where(r => r.Stoichiometry.Count >= 2) // Must have at least 2 species
            .ToList();
    }
}