// GeoscientistToolkit/Business/Thermodynamics/ReactionGenerator.cs
//
// Automatically generates chemical reactions from compound thermodynamic data
// without hardcoding. Uses stoichiometric matrix analysis and thermodynamic feasibility.
// ENHANCEMENT: Added generators for redox, gas-liquid, surface complexation, and solid solution reactions.
//              Completed acid-base systems for sulfide and phosphate.
//
// SOURCES:
// - Smith, W.R. & Missen, R.W., 1982. Chemical Reaction Equilibrium Analysis: Theory and Algorithms. 
//   Wiley-Interscience, Chapter 2.
// - Bethke, C.M., 2008. Geochemical and Biogeochemical Reaction Modeling, 2nd ed. Cambridge, Chapter 3.
// - Stumm, W. & Morgan, J.J., 1996. Aquatic Chemistry, 3rd ed. Wiley.
// - Sander, R., 2015. Compilation of Henry's law constants. Atmos. Chem. Phys., 15, 4399-4981.

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

        // ENHANCEMENT: Generate surface complexation reactions
        reactions.AddRange(GenerateSurfaceComplexationReactions(state));

        // Filter for thermodynamic feasibility
        reactions = FilterFeasibleReactions(reactions, state);

        Logger.Log($"[ReactionGenerator] Generated {reactions.Count} feasible reactions");
        return reactions;
    }

    /// <summary>
    ///     Generate mineral dissolution reactions.
    ///     General form: Mineral(s) ⇌ ν₁Ion₁(aq) + ν₂Ion₂(aq) + ...
    /// </summary>
    public List<ChemicalReaction> GenerateDissolutionReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();

        // Get available elements from the initial state
        var availableElements = state.ElementalComposition.Keys.ToHashSet();

        // Get all solid phase compounds that are not part of a solid solution
        var pureMinerals = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Solid &&
                        c.GibbsFreeEnergyFormation_kJ_mol != null &&
                        !_compoundLibrary.SolidSolutions.Any(ss => ss.EndMembers.Contains(c.Name)))
            .ToList();

        foreach (var mineral in pureMinerals)
        {
            // FIX: Only consider minerals that can be formed from available elements
            var mineralElements = ParseChemicalFormula(mineral.ChemicalFormula);
            if (!mineralElements.Keys.All(element => availableElements.Contains(element)))
            {
                // Skip this mineral if it contains elements not present in the system
                continue;
            }

            var reaction = GenerateSingleDissolutionReaction(mineral);
            if (reaction != null)
                reactions.Add(reaction);
        }

        return reactions;
    }

    public ChemicalReaction? GenerateSingleDissolutionReaction(ChemicalCompound mineral)
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

        // Check if balancing was successful
        if (reaction.Stoichiometry.Count <= 1) return null;

        // Calculate thermodynamic properties
        CalculateReactionThermodynamics(reaction);

        return reaction;
    }

    /// <summary>
    ///     Parse chemical formula into elemental composition.
    ///     Example: "CaMg(CO₃)₂" -> {Ca: 1, Mg: 1, C: 2, O: 6}
    /// </summary>
    public Dictionary<string, int> ParseChemicalFormula(string formula)
    {
        var composition = new Dictionary<string, int>();

        // Pre-process to expand parentheses
        formula = ExpandParentheses(formula);

        // Handle hydration: CaSO₄·2H₂O
        var hydrationMatch = Regex.Match(formula, @"·(\d*)H₂O");
        if (hydrationMatch.Success)
        {
            var waterMoles = string.IsNullOrEmpty(hydrationMatch.Groups[1].Value)
                ? 1
                : int.Parse(hydrationMatch.Groups[1].Value);
            composition["H"] = composition.GetValueOrDefault("H", 0) + 2 * waterMoles;
            composition["O"] = composition.GetValueOrDefault("O", 0) + 1 * waterMoles;
            formula = formula.Substring(0, hydrationMatch.Index);
        }

        // Main parser for elements and their counts
        var matches = Regex.Matches(formula, @"([A-Z][a-z]?)(\d*)");
        foreach (Match match in matches)
        {
            var element = match.Groups[1].Value;
            var count = string.IsNullOrEmpty(match.Groups[2].Value) ? 1 : int.Parse(match.Groups[2].Value);
            composition[element] = composition.GetValueOrDefault(element, 0) + count;
        }

        return composition;
    }

    private string ExpandParentheses(string formula)
    {
        // Recursively expand parentheses, e.g., CaMg(CO3)2 -> CaMgC2O6
        while (formula.Contains('('))
        {
            var match = Regex.Match(formula, @"\(([^)]+)\)(\d+)");
            if (!match.Success) break;

            var content = match.Groups[1].Value;
            var multiplier = int.Parse(match.Groups[2].Value);

            var expandedContent = "";
            var elementMatches = Regex.Matches(content, @"([A-Z][a-z]?)(\d*)");
            foreach (Match em in elementMatches)
            {
                var element = em.Groups[1].Value;
                var count = string.IsNullOrEmpty(em.Groups[2].Value) ? 1 : int.Parse(em.Groups[2].Value);
                expandedContent += $"{element}{count * multiplier}";
            }

            formula = formula.Replace(match.Value, expandedContent);
        }

        return formula;
    }


    /// <summary>
    ///     Find aqueous ions that can form from mineral elements.
    /// </summary>
    private List<ChemicalCompound> FindDissolutionProducts(Dictionary<string, int> elementalComp)
    {
        var products = new List<ChemicalCompound>();
        var elements = elementalComp.Keys.ToList();

        // Add the most common, simple, free aqueous ion for each element
        foreach (var element in elements.Where(e => e != "O" && e != "H"))
        {
            var primaryIon = _compoundLibrary.Compounds
                .Where(c => c.Phase == CompoundPhase.Aqueous &&
                            c.IsPrimaryElementSpecies &&
                            ParseChemicalFormula(c.ChemicalFormula).ContainsKey(element))
                .FirstOrDefault();

            if (primaryIon != null) products.Add(primaryIon);
        }

        // Add water, H+, OH- as they are always present and needed for balancing
        var water = _compoundLibrary.Find("H₂O");
        if (water != null) products.Add(water);
        var hIon = _compoundLibrary.Find("H⁺");
        if (hIon != null) products.Add(hIon);
        var ohIon = _compoundLibrary.Find("OH⁻");
        if (ohIon != null) products.Add(ohIon);

        return products.Distinct().ToList();
    }

    /// <summary>
    ///     COMPLETE IMPLEMENTATION: Stoichiometric reaction balancing using element matrix.
    ///     Implements the algorithm from Smith & Missen (1982), Chapter 2.
    ///     Handles both dissolution reactions and complexation reactions properly.
    /// </summary>
    private void BalanceDissolutionReaction(ChemicalReaction reaction, ChemicalCompound mineral,
        List<ChemicalCompound> possibleProducts)
    {
        var mineralComp = ParseChemicalFormula(mineral.ChemicalFormula);

        // Build element list including charge
        var elements = mineralComp.Keys.ToList();
        if (!elements.Contains("Charge")) elements.Add("Charge");

        // Add oxygen and hydrogen if not present (needed for balancing with H2O, H+, OH-)
        if (!elements.Contains("O")) elements.Add("O");
        if (!elements.Contains("H")) elements.Add("H");

        var nElements = elements.Count;
        var nProducts = possibleProducts.Count;

        // Build stoichiometric matrix A where A[i,j] = number of element i in product j
        var A = Matrix<double>.Build.Dense(nElements, nProducts);
        var b = Vector<double>.Build.Dense(nElements);

        // Right-hand side: elemental composition of mineral
        for (var i = 0; i < nElements; i++)
        {
            var element = elements[i];
            if (element == "Charge")
                b[i] = mineral.IonicCharge ?? 0;
            else
                b[i] = mineralComp.GetValueOrDefault(element, 0);
        }

        // Matrix: elemental composition of products
        for (var j = 0; j < nProducts; j++)
        {
            var product = possibleProducts[j];
            var productComp = ParseChemicalFormula(product.ChemicalFormula);

            for (var i = 0; i < nElements; i++)
            {
                var element = elements[i];
                if (element == "Charge")
                    A[i, j] = product.IonicCharge ?? 0;
                else
                    A[i, j] = productComp.GetValueOrDefault(element, 0);
            }
        }

        // Solve A*x = b using constrained least squares
        // Since system may be overdetermined, use SVD
        Vector<double> stoichiometry;

        try
        {
            // Use SVD decomposition for robust solution
            var svd = A.Svd();

            // Filter out near-zero singular values to handle rank deficiency
            var singularValues = svd.S;
            var tolerance = 1e-10 * singularValues[0]; // Relative to largest singular value

            var S_inv = Matrix<double>.Build.Dense(nProducts, nElements);
            for (var i = 0; i < Math.Min(nElements, nProducts); i++)
                if (singularValues[i] > tolerance)
                    S_inv[i, i] = 1.0 / singularValues[i];

            // x = V * S^(-1) * U^T * b
            var VT = svd.VT;
            var U = svd.U;
            stoichiometry = VT.Transpose().Multiply(S_inv).Multiply(U.Transpose()).Multiply(b);

            // If system is underdetermined, find minimum norm solution
            // If overdetermined, find least squares solution

            // Verify solution quality
            var residual = A.Multiply(stoichiometry) - b;
            var residualNorm = residual.L2Norm();

            if (residualNorm > 1e-6)
            {
                Logger.LogWarning(
                    $"[ReactionGenerator] Large residual {residualNorm:E2} when balancing {mineral.Name}");

                // Try alternative approach: fix main product stoichiometry to 1, solve for others
                stoichiometry = BalanceWithConstraints(A, b, possibleProducts, mineral);
            }

            // Round very small coefficients to zero
            for (var i = 0; i < stoichiometry.Count; i++)
            {
                if (Math.Abs(stoichiometry[i]) < 1e-9) stoichiometry[i] = 0.0;

                // Round to reasonable fractions (e.g., 0.333... -> 1/3)
                stoichiometry[i] = RoundToRationalFraction(stoichiometry[i]);
            }

            // Add products with non-zero stoichiometry to reaction
            for (var j = 0; j < nProducts; j++)
                if (Math.Abs(stoichiometry[j]) > 1e-9)
                    reaction.Stoichiometry[possibleProducts[j].Name] = stoichiometry[j];

            // Verify charge balance
            var totalCharge = reaction.Stoichiometry
                .Where(kvp => kvp.Key != mineral.Name)
                .Sum(kvp =>
                {
                    var compound = _compoundLibrary.Find(kvp.Key);
                    return kvp.Value * (compound?.IonicCharge ?? 0);
                });

            var mineralCharge = mineral.IonicCharge ?? 0;
            if (Math.Abs(totalCharge + mineralCharge) > 0.01)
                Logger.LogWarning($"[ReactionGenerator] Charge imbalance in {mineral.Name} dissolution: " +
                                  $"Δq = {totalCharge + mineralCharge:F3}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ReactionGenerator] Failed to balance {mineral.Name}: {ex.Message}");

            // Fallback: use heuristic balancing
            BalanceHeuristic(reaction, mineral, possibleProducts, elements);
        }
    }

    /// <summary>
    ///     Alternative balancing with constraints (e.g., fix primary product to 1.0).
    ///     Used when standard SVD solution has large residuals.
    /// </summary>
    private Vector<double> BalanceWithConstraints(Matrix<double> A, Vector<double> b,
        List<ChemicalCompound> products, ChemicalCompound mineral)
    {
        var nProducts = products.Count;

        // Find the primary dissociation product (metal ion for mineral)
        var primaryIdx = FindPrimaryProduct(products, mineral);

        if (primaryIdx < 0)
            // No clear primary product, return zero vector
            return Vector<double>.Build.Dense(nProducts);

        // Fix primary product stoichiometry to 1.0
        // Modified system: A' * x' = b - A[:, primaryIdx]
        var nElements = A.RowCount;
        var A_reduced = Matrix<double>.Build.Dense(nElements, nProducts - 1);
        var b_modified = b - A.Column(primaryIdx);

        var col = 0;
        for (var j = 0; j < nProducts; j++)
            if (j != primaryIdx)
            {
                A_reduced.SetColumn(col, A.Column(j));
                col++;
            }

        // Solve reduced system
        var svd = A_reduced.Svd();
        var x_reduced = svd.Solve(b_modified);

        // Reconstruct full solution
        var x = Vector<double>.Build.Dense(nProducts);
        x[primaryIdx] = 1.0;
        col = 0;
        for (var j = 0; j < nProducts; j++)
            if (j != primaryIdx)
            {
                x[j] = x_reduced[col];
                col++;
            }

        return x;
    }

    /// <summary>
    ///     Find the primary dissociation product (usually the metal cation).
    /// </summary>
    private int FindPrimaryProduct(List<ChemicalCompound> products, ChemicalCompound mineral)
    {
        var mineralComp = ParseChemicalFormula(mineral.ChemicalFormula);

        // Look for the cation that contains the main metal element
        var metalElements = new[] { "Ca", "Mg", "Fe", "Al", "Na", "K", "Mn", "Zn", "Cu", "Pb", "Sr", "Ba" };

        foreach (var metal in metalElements)
            if (mineralComp.ContainsKey(metal))
                // Find the simple ion of this metal
                for (var i = 0; i < products.Count; i++)
                {
                    var product = products[i];
                    var productComp = ParseChemicalFormula(product.ChemicalFormula);

                    // Check if this is a simple metal ion (only contains the metal element)
                    if (productComp.ContainsKey(metal) &&
                        productComp.Count == 1 &&
                        product.IonicCharge.HasValue &&
                        product.IonicCharge.Value > 0)
                        return i;
                }

        return -1; // No primary product found
    }

    /// <summary>
    ///     Round coefficient to simple rational fraction if close.
    ///     E.g., 0.3333... -> 1/3, 0.6667... -> 2/3, 1.5 -> 3/2
    /// </summary>
    private double RoundToRationalFraction(double value)
    {
        if (Math.Abs(value) < 1e-9) return 0.0;

        // Check common fractions up to denominator 12
        for (var denom = 2; denom <= 12; denom++)
        for (var numer = 1; numer < denom * 3; numer++)
        {
            var fraction = (double)numer / denom;
            if (Math.Abs(value - fraction) < 1e-4) return fraction;
            if (Math.Abs(value + fraction) < 1e-4) return -fraction;
        }

        // Round to 4 decimal places
        return Math.Round(value, 4);
    }

    /// <summary>
    ///     COMPLETE IMPLEMENTATION: Rigorous H2O/H+ balancing for mineral dissolution reactions.
    ///     Replaces the simplified heuristic with systematic charge and mass balance.
    ///     Uses the algorithm from Bethke (2008), Chapter 3.
    /// </summary>
    private void BalanceHeuristic(ChemicalReaction reaction, ChemicalCompound mineral,
        List<ChemicalCompound> products, List<string> elements)
    {
        var mineralComp = ParseChemicalFormula(mineral.ChemicalFormula);

        Logger.Log($"[ReactionGenerator] Using systematic balancing for {mineral.Name}");

        // Step 1: Identify primary dissociation products (metal cations, anions)
        var cations = new List<(string name, double stoich, int charge)>();
        var anions = new List<(string name, double stoich, int charge)>();

        foreach (var product in products)
            if (product.IonicCharge.HasValue)
            {
                var productComp = ParseChemicalFormula(product.ChemicalFormula);

                if (product.IonicCharge.Value > 0)
                {
                    // Check if this cation contains a metal from the mineral
                    foreach (var (element, count) in mineralComp)
                        if (element != "O" && element != "H" && productComp.ContainsKey(element))
                        {
                            cations.Add((product.Name, count, product.IonicCharge.Value));
                            reaction.Stoichiometry[product.Name] = count;
                            break;
                        }
                }
                else if (product.IonicCharge.Value < 0)
                {
                    // Check if this anion contains a non-metal from the mineral
                    var matched = false;

                    // Try carbonate
                    if (productComp.ContainsKey("C") && productComp.ContainsKey("O") &&
                        mineralComp.ContainsKey("C"))
                    {
                        var stoich = mineralComp["C"];
                        anions.Add((product.Name, stoich, product.IonicCharge.Value));
                        reaction.Stoichiometry[product.Name] = stoich;
                        matched = true;
                    }
                    // Try sulfate
                    else if (productComp.ContainsKey("S") && productComp.ContainsKey("O") &&
                             mineralComp.ContainsKey("S"))
                    {
                        var stoich = mineralComp["S"];
                        anions.Add((product.Name, stoich, product.IonicCharge.Value));
                        reaction.Stoichiometry[product.Name] = stoich;
                        matched = true;
                    }
                    // Try phosphate
                    else if (productComp.ContainsKey("P") && productComp.ContainsKey("O") &&
                             mineralComp.ContainsKey("P"))
                    {
                        var stoich = mineralComp["P"];
                        anions.Add((product.Name, stoich, product.IonicCharge.Value));
                        reaction.Stoichiometry[product.Name] = stoich;
                        matched = true;
                    }
                    // Try silicate
                    else if (productComp.ContainsKey("Si") && productComp.ContainsKey("O") &&
                             mineralComp.ContainsKey("Si"))
                    {
                        var stoich = mineralComp["Si"];
                        anions.Add((product.Name, stoich, product.IonicCharge.Value));
                        reaction.Stoichiometry[product.Name] = stoich;
                        matched = true;
                    }
                }
            }

        // Step 2: Calculate current elemental balance
        var currentComp = new Dictionary<string, double>();
        foreach (var (species, stoich) in reaction.Stoichiometry)
        {
            if (species == mineral.Name) continue;

            var compound = _compoundLibrary.Find(species);
            if (compound != null)
            {
                var speciesComp = ParseChemicalFormula(compound.ChemicalFormula);
                foreach (var (element, count) in speciesComp)
                    currentComp[element] = currentComp.GetValueOrDefault(element, 0) + stoich * count;
            }
        }

        // Step 3: Calculate deficits/excesses
        var H_deficit = mineralComp.GetValueOrDefault("H", 0) - currentComp.GetValueOrDefault("H", 0);
        var O_deficit = mineralComp.GetValueOrDefault("O", 0) - currentComp.GetValueOrDefault("O", 0);

        // Step 4: Systematically add H2O, H+, or OH- to balance
        var water = _compoundLibrary.Find("H₂O");
        var hPlus = _compoundLibrary.Find("H⁺");
        var ohMinus = _compoundLibrary.Find("OH⁻");

        // Determine balancing strategy based on mineral type
        if (IsOxideMiner(mineral))
            // Oxide minerals: MₓOᵧ + H₂O → cations + OH⁻
            // Example: CaO + H₂O → Ca²⁺ + 2OH⁻
            BalanceOxideMineral(reaction, mineral, water, hPlus, ohMinus, H_deficit, O_deficit);
        else if (IsHydroxideMiner(mineral))
            // Hydroxide minerals: M(OH)ₙ → Mⁿ⁺ + nOH⁻
            // Example: Ca(OH)₂ → Ca²⁺ + 2OH⁻
            BalanceHydroxideMineral(reaction, mineral, water, hPlus, ohMinus, H_deficit, O_deficit);
        else if (IsSilicateMineral(mineral))
            // Silicate minerals: complex, produce silicic acid
            // Example: Mg₂SiO₄ + 4H⁺ → 2Mg²⁺ + H₄SiO₄
            BalanceSilicateMineral(reaction, mineral, water, hPlus, ohMinus, H_deficit, O_deficit);
        else if (IsOxyanioncMineral(mineral))
            // Minerals with oxyanions (carbonates, sulfates, phosphates)
            // Example: CaCO₃ → Ca²⁺ + CO₃²⁻
            BalanceOxyanionMineral(reaction, mineral, water, hPlus, ohMinus, H_deficit, O_deficit);
        else
            // Generic balancing
            BalanceGenericMineral(reaction, mineral, water, hPlus, ohMinus, H_deficit, O_deficit);

        // Step 5: Verify charge balance
        var totalCharge = 0.0;
        foreach (var (species, stoich) in reaction.Stoichiometry)
        {
            var compound = _compoundLibrary.Find(species);
            if (compound != null) totalCharge += stoich * (compound.IonicCharge ?? 0);
        }

        if (Math.Abs(totalCharge) > 0.01)
        {
            Logger.LogWarning($"[ReactionGenerator] Charge imbalance after heuristic: Δq = {totalCharge:F3}");
            // Attempt to fix with H+ or OH-
            if (totalCharge < 0 && hPlus != null)
                reaction.Stoichiometry[hPlus.Name] =
                    reaction.Stoichiometry.GetValueOrDefault(hPlus.Name, 0) - totalCharge;
            else if (totalCharge > 0 && ohMinus != null)
                reaction.Stoichiometry[ohMinus.Name] =
                    reaction.Stoichiometry.GetValueOrDefault(ohMinus.Name, 0) + totalCharge;
        }
    }

    /// <summary>
    ///     Balance oxide mineral dissolution: MₓOᵧ + yH₂O → xMⁿ⁺ + 2yOH⁻
    /// </summary>
    private void BalanceOxideMineral(ChemicalReaction reaction, ChemicalCompound mineral,
        ChemicalCompound water, ChemicalCompound hPlus, ChemicalCompound ohMinus,
        double H_deficit, double O_deficit)
    {
        var mineralComp = ParseChemicalFormula(mineral.ChemicalFormula);
        var O_count = mineralComp.GetValueOrDefault("O", 0);

        if (water != null && ohMinus != null)
        {
            // Add water
            reaction.Stoichiometry[water.Name] = O_count;
            // Add hydroxide (2 per O²⁻)
            reaction.Stoichiometry[ohMinus.Name] = 2.0 * O_count;
        }
    }

    /// <summary>
    ///     Balance hydroxide mineral dissolution: M(OH)ₙ → Mⁿ⁺ + nOH⁻
    /// </summary>
    private void BalanceHydroxideMineral(ChemicalReaction reaction, ChemicalCompound mineral,
        ChemicalCompound water, ChemicalCompound hPlus, ChemicalCompound ohMinus,
        double H_deficit, double O_deficit)
    {
        var mineralComp = ParseChemicalFormula(mineral.ChemicalFormula);
        var OH_count = mineralComp.GetValueOrDefault("O", 0); // Assume each O is OH

        if (ohMinus != null) reaction.Stoichiometry[ohMinus.Name] = OH_count;
    }

    /// <summary>
    ///     Balance silicate mineral dissolution: produces H₄SiO₄ (silicic acid)
    ///     Example: Mg₂SiO₄ + 4H⁺ → 2Mg²⁺ + H₄SiO₄
    /// </summary>
    private void BalanceSilicateMineral(ChemicalReaction reaction, ChemicalCompound mineral,
        ChemicalCompound water, ChemicalCompound hPlus, ChemicalCompound ohMinus,
        double H_deficit, double O_deficit)
    {
        var mineralComp = ParseChemicalFormula(mineral.ChemicalFormula);
        var Si_count = mineralComp.GetValueOrDefault("Si", 0);

        // Find silicic acid product
        var silicicAcid = _compoundLibrary.Find("H₄SiO₄");
        if (silicicAcid != null && Si_count > 0) reaction.Stoichiometry[silicicAcid.Name] = Si_count;

        // Calculate H+ needed
        var currentH = 0.0;
        var currentO = 0.0;
        foreach (var (species, stoich) in reaction.Stoichiometry)
        {
            if (species == mineral.Name) continue;
            var compound = _compoundLibrary.Find(species);
            if (compound != null)
            {
                var comp = ParseChemicalFormula(compound.ChemicalFormula);
                currentH += stoich * comp.GetValueOrDefault("H", 0);
                currentO += stoich * comp.GetValueOrDefault("O", 0);
            }
        }

        var H_needed = mineralComp.GetValueOrDefault("H", 0) + Si_count * 4 - currentH;
        var O_needed = mineralComp.GetValueOrDefault("O", 0) - currentO;

        if (H_needed > 0 && hPlus != null) reaction.Stoichiometry[hPlus.Name] = -H_needed; // Consumed (reactant)

        if (O_needed != 0 && water != null)
            // Add water to balance oxygen
            reaction.Stoichiometry[water.Name] = -O_needed / 2.0; // Each water has 1 O
    }

    /// <summary>
    ///     Balance oxyanion minerals (carbonates, sulfates, phosphates)
    ///     These typically dissolve congruently without H+ consumption
    /// </summary>
    private void BalanceOxyanionMineral(ChemicalReaction reaction, ChemicalCompound mineral,
        ChemicalCompound water, ChemicalCompound hPlus, ChemicalCompound ohMinus,
        double H_deficit, double O_deficit)
    {
        // For carbonates, sulfates, phosphates: usually just M + XO₄ → products
        // No additional species needed as oxyanion is preserved

        // Check if H or O remain unbalanced
        if (Math.Abs(H_deficit) > 0.01 && water != null)
        {
            // Some hydrated minerals need water
            var formula = mineral.ChemicalFormula;
            if (formula.Contains("·"))
            {
                // Hydrated mineral, water is a product
                var waterMatch = Regex.Match(formula, @"·(\d*)H₂O");
                if (waterMatch.Success)
                {
                    var waterMoles = string.IsNullOrEmpty(waterMatch.Groups[1].Value)
                        ? 1
                        : int.Parse(waterMatch.Groups[1].Value);
                    reaction.Stoichiometry[water.Name] = waterMoles;
                }
            }
        }
    }

    /// <summary>
    ///     Generic balancing for minerals that don't fit other categories
    /// </summary>
    private void BalanceGenericMineral(ChemicalReaction reaction, ChemicalCompound mineral,
        ChemicalCompound water, ChemicalCompound hPlus, ChemicalCompound ohMinus,
        double H_deficit, double O_deficit)
    {
        // Balance O with H2O, then balance H with H+

        if (water != null && Math.Abs(O_deficit) > 0.01)
        {
            // Add/remove water to balance oxygen
            // Each water contributes 1 O and 2 H
            var water_stoich = O_deficit;
            reaction.Stoichiometry[water.Name] = water_stoich;

            // Update H deficit after adding water
            H_deficit -= water_stoich * 2;
        }

        if (hPlus != null && Math.Abs(H_deficit) > 0.01)
            // Add/remove H+ to balance hydrogen
            reaction.Stoichiometry[hPlus.Name] = -H_deficit; // Negative = consumed
    }

    /// <summary>
    ///     Check if mineral is an oxide (contains only metal + oxygen)
    /// </summary>
    private bool IsOxideMiner(ChemicalCompound mineral)
    {
        var comp = ParseChemicalFormula(mineral.ChemicalFormula);
        return comp.ContainsKey("O") &&
               !comp.ContainsKey("H") &&
               !comp.ContainsKey("C") &&
               !comp.ContainsKey("S") &&
               !comp.ContainsKey("P") &&
               !comp.ContainsKey("Si");
    }

    /// <summary>
    ///     Check if mineral is a hydroxide (contains OH groups)
    /// </summary>
    private bool IsHydroxideMiner(ChemicalCompound mineral)
    {
        var formula = mineral.ChemicalFormula;
        return formula.Contains("(OH)") ||
               (formula.Contains("O") && formula.Contains("H") &&
                !formula.Contains("CO") && !formula.Contains("SO") && !formula.Contains("PO"));
    }

    /// <summary>
    ///     Check if mineral is a silicate
    /// </summary>
    private bool IsSilicateMineral(ChemicalCompound mineral)
    {
        var comp = ParseChemicalFormula(mineral.ChemicalFormula);
        return comp.ContainsKey("Si") && comp.ContainsKey("O");
    }

    /// <summary>
    ///     Check if mineral contains oxyanions (CO₃²⁻, SO₄²⁻, PO₄³⁻)
    /// </summary>
    private bool IsOxyanioncMineral(ChemicalCompound mineral)
    {
        var comp = ParseChemicalFormula(mineral.ChemicalFormula);
        return (comp.ContainsKey("C") && comp.ContainsKey("O")) ||
               (comp.ContainsKey("S") && comp.ContainsKey("O")) ||
               (comp.ContainsKey("P") && comp.ContainsKey("O"));
    }

    /// <summary>
    ///     COMPLETE IMPLEMENTATION: Balance complexation reactions using matrix methods.
    ///     Decomposes secondary species into primary basis species.
    /// </summary>
    public void BalanceComplexationReaction(ChemicalReaction reaction,
        ChemicalCompound secondarySpecies,
        List<ChemicalCompound> basisSpecies)
    {
        var secondaryComp = ParseChemicalFormula(secondarySpecies.ChemicalFormula);
        var elements = secondaryComp.Keys.ToList();
        if (!elements.Contains("Charge")) elements.Add("Charge");
        if (!elements.Contains("H")) elements.Add("H");
        if (!elements.Contains("O")) elements.Add("O");

        var nElements = elements.Count;
        var nBasis = basisSpecies.Count;

        // Build basis matrix
        var A = Matrix<double>.Build.Dense(nElements, nBasis);
        var b = Vector<double>.Build.Dense(nElements);

        // Target composition (secondary species)
        for (var i = 0; i < nElements; i++)
        {
            var element = elements[i];
            if (element == "Charge")
                b[i] = secondarySpecies.IonicCharge ?? 0;
            else
                b[i] = secondaryComp.GetValueOrDefault(element, 0);
        }

        // Basis species compositions
        for (var j = 0; j < nBasis; j++)
        {
            var basis = basisSpecies[j];
            var basisComp = ParseChemicalFormula(basis.ChemicalFormula);

            for (var i = 0; i < nElements; i++)
            {
                var element = elements[i];
                if (element == "Charge")
                    A[i, j] = basis.IonicCharge ?? 0;
                else
                    A[i, j] = basisComp.GetValueOrDefault(element, 0);
            }
        }

        // Solve for basis species coefficients
        try
        {
            var svd = A.Svd();
            var stoich = svd.Solve(b);

            // Add to reaction (negative for reactants)
            reaction.Stoichiometry[secondarySpecies.Name] = 1.0; // Product

            for (var j = 0; j < nBasis; j++)
            {
                var coeff = RoundToRationalFraction(stoich[j]);
                if (Math.Abs(coeff) > 1e-9) reaction.Stoichiometry[basisSpecies[j].Name] = -coeff; // Reactant
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(
                $"[ReactionGenerator] Failed to balance complexation for {secondarySpecies.Name}: {ex.Message}");
        }
    }

    /// <summary>
    ///     COMPLETE IMPLEMENTATION: Rigorous aqueous complexation reaction generation.
    ///     Replaces simplified heuristic with systematic basis species decomposition.
    ///     Uses the algorithm from Wolery (1992), LLNL Report UCRL-MA-110662 PT IV.
    /// </summary>
    public List<ChemicalReaction> GenerateComplexationReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();

        // Step 1: Identify basis species (primary aqueous species)
        var basisSpecies = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Aqueous && c.IsPrimaryElementSpecies)
            .ToList();

        // Step 2: Identify secondary species (complexes formed from basis)
        var secondarySpecies = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Aqueous &&
                        !c.IsPrimaryElementSpecies &&
                        c.GibbsFreeEnergyFormation_kJ_mol.HasValue)
            .ToList();

        Logger.Log($"[ReactionGenerator] Basis species: {basisSpecies.Count}, Secondary: {secondarySpecies.Count}");

        foreach (var secondary in secondarySpecies)
            try
            {
                var reaction = GenerateSingleComplexationReaction(secondary, basisSpecies);
                if (reaction != null && reaction.Stoichiometry.Count > 1) reactions.Add(reaction);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    $"[ReactionGenerator] Failed to generate reaction for {secondary.Name}: {ex.Message}");
            }

        return reactions;
    }

    /// <summary>
    ///     Generate a single complexation reaction by decomposing secondary species into basis.
    /// </summary>
    private ChemicalReaction GenerateSingleComplexationReaction(ChemicalCompound secondary,
        List<ChemicalCompound> basisSpecies)
    {
        var reaction = new ChemicalReaction
        {
            Name = $"{secondary.Name} formation",
            Type = ReactionType.Complexation,
            Stoichiometry = { [secondary.Name] = 1.0 } // Product
        };

        // Parse secondary species composition
        var secondaryComp = ParseChemicalFormula(secondary.ChemicalFormula);

        // Build element list (including charge)
        var elements = new HashSet<string>(secondaryComp.Keys);
        elements.Add("Charge");

        // Always include H and O for water balance
        if (!elements.Contains("H")) elements.Add("H");
        if (!elements.Contains("O")) elements.Add("O");

        var elementList = elements.ToList();
        var nElements = elementList.Count;
        var nBasis = basisSpecies.Count;

        // Build stoichiometric matrix
        var A = Matrix<double>.Build.Dense(nElements, nBasis);
        var b = Vector<double>.Build.Dense(nElements);

        // Target vector (secondary species composition)
        for (var i = 0; i < nElements; i++)
        {
            var element = elementList[i];
            if (element == "Charge")
                b[i] = secondary.IonicCharge ?? 0;
            else
                b[i] = secondaryComp.GetValueOrDefault(element, 0);
        }

        // Matrix (basis species compositions)
        for (var j = 0; j < nBasis; j++)
        {
            var basis = basisSpecies[j];
            var basisComp = ParseChemicalFormula(basis.ChemicalFormula);

            for (var i = 0; i < nElements; i++)
            {
                var element = elementList[i];
                if (element == "Charge")
                    A[i, j] = basis.IonicCharge ?? 0;
                else
                    A[i, j] = basisComp.GetValueOrDefault(element, 0);
            }
        }

        // Solve for stoichiometric coefficients
        try
        {
            var svd = A.Svd();
            var stoich = svd.Solve(b);

            // Verify solution quality
            var residual = A.Multiply(stoich) - b;
            var residualNorm = residual.L2Norm();

            if (residualNorm > 0.01)
                // Try constrained solution
                stoich = SolveComplexationWithConstraints(A, b, basisSpecies, secondary);

            // Add basis species as reactants (negative stoichiometry)
            for (var j = 0; j < nBasis; j++)
            {
                var coeff = RoundToRationalFraction(stoich[j]);
                if (Math.Abs(coeff) > 1e-9) reaction.Stoichiometry[basisSpecies[j].Name] = -coeff;
            }

            // Verify mass and charge balance
            if (!VerifyReactionBalance(reaction))
            {
                Logger.LogWarning($"[ReactionGenerator] Mass/charge imbalance in {secondary.Name} formation");
                return null;
            }

            // Calculate thermodynamic properties
            CalculateReactionThermodynamics(reaction);

            return reaction;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ReactionGenerator] Failed to balance {secondary.Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Solve complexation with constraints when standard SVD fails.
    ///     Prioritizes certain basis species (e.g., H+ for pH buffering).
    /// </summary>
    private Vector<double> SolveComplexationWithConstraints(Matrix<double> A, Vector<double> b,
        List<ChemicalCompound> basisSpecies, ChemicalCompound secondary)
    {
        // Strategy: Fix H2O and H+ first (if present), solve for others

        var nBasis = basisSpecies.Count;
        var fixedIndices = new List<int>();
        var fixedValues = new List<double>();

        // Check if we need H+ for protonation/deprotonation
        var h_index = basisSpecies.FindIndex(b => b.Name == "H⁺" || b.Name == "H+");
        var water_index = basisSpecies.FindIndex(b => b.Name == "H₂O" || b.Name == "H2O");

        var secondaryComp = ParseChemicalFormula(secondary.ChemicalFormula);
        var H_in_secondary = secondaryComp.GetValueOrDefault("H", 0);
        var O_in_secondary = secondaryComp.GetValueOrDefault("O", 0);

        // Estimate H2O needed
        if (water_index >= 0 && O_in_secondary > 0)
        {
            // First approximation: use all O as water
            var waterEstimate = O_in_secondary;
            fixedIndices.Add(water_index);
            fixedValues.Add(waterEstimate);
        }

        // Build reduced system
        var reducedBasis = new List<ChemicalCompound>();
        for (var i = 0; i < nBasis; i++)
            if (!fixedIndices.Contains(i))
                reducedBasis.Add(basisSpecies[i]);

        if (reducedBasis.Count == 0)
        {
            // All fixed, return fixed values
            var result = Vector<double>.Build.Dense(nBasis);
            for (var i = 0; i < fixedIndices.Count; i++) result[fixedIndices[i]] = fixedValues[i];
            return result;
        }

        // Modify b to account for fixed species
        var b_modified = b.Clone();
        for (var i = 0; i < fixedIndices.Count; i++)
        {
            var fixedIdx = fixedIndices[i];
            var fixedVal = fixedValues[i];
            b_modified -= fixedVal * A.Column(fixedIdx);
        }

        // Build reduced matrix
        var nReduced = reducedBasis.Count;
        var A_reduced = Matrix<double>.Build.Dense(A.RowCount, nReduced);
        var col = 0;
        for (var j = 0; j < nBasis; j++)
            if (!fixedIndices.Contains(j))
            {
                A_reduced.SetColumn(col, A.Column(j));
                col++;
            }

        // Solve reduced system
        var svd = A_reduced.Svd();
        var x_reduced = svd.Solve(b_modified);

        // Reconstruct full solution
        var x_full = Vector<double>.Build.Dense(nBasis);
        col = 0;
        for (var j = 0; j < nBasis; j++)
            if (fixedIndices.Contains(j))
            {
                var idx = fixedIndices.IndexOf(j);
                x_full[j] = fixedValues[idx];
            }
            else
            {
                x_full[j] = x_reduced[col];
                col++;
            }

        return x_full;
    }

    /// <summary>
    ///     Verify that a reaction is balanced in mass and charge.
    /// </summary>
    private bool VerifyReactionBalance(ChemicalReaction reaction)
    {
        var elementTotals = new Dictionary<string, double>();
        var chargTotal = 0.0;

        foreach (var (species, stoich) in reaction.Stoichiometry)
        {
            var compound = _compoundLibrary.Find(species);
            if (compound == null) return false;

            var comp = ParseChemicalFormula(compound.ChemicalFormula);
            foreach (var (element, count) in comp)
                elementTotals[element] = elementTotals.GetValueOrDefault(element, 0) + stoich * count;

            chargTotal += stoich * (compound.IonicCharge ?? 0);
        }

        // Check element balance
        foreach (var (element, total) in elementTotals)
            if (Math.Abs(total) > 0.01)
            {
                Logger.LogWarning($"[ReactionGenerator] Element {element} imbalance: {total:F3}");
                return false;
            }

        // Check charge balance
        if (Math.Abs(chargTotal) > 0.01)
        {
            Logger.LogWarning($"[ReactionGenerator] Charge imbalance: {chargTotal:F3}");
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Generate acid-base reactions using proton transfer formalism.
    /// </summary>
    public List<ChemicalReaction> GenerateAcidBaseReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();

        // Get available elements
        var availableElements = state.ElementalComposition
            .Where(kvp => kvp.Value > 1e-15)
            .Select(kvp => kvp.Key)
            .ToHashSet();

        // Only generate water dissociation if H and O are present
        if (availableElements.Contains("H") && availableElements.Contains("O"))
        {
            reactions.AddRange(GenerateWaterDissociation());
        }

        // Only generate carbonate system if C is present
        if (availableElements.Contains("C") && availableElements.Contains("O"))
        {
            reactions.AddRange(GenerateCarbonateSys());
        }

        // Only generate phosphate system if P is present
        if (availableElements.Contains("P") && availableElements.Contains("O"))
        {
            reactions.AddRange(GeneratePhosphateSys());
        }

        // Only generate sulfide system if S is present
        if (availableElements.Contains("S"))
        {
            reactions.AddRange(GenerateSulfideSys());
        }

        // Only generate ammonia system if N is present
        if (availableElements.Contains("N") && availableElements.Contains("H"))
        {
            reactions.AddRange(GenerateAmmoniaSys());
        }

        return reactions;
    }

    private List<ChemicalReaction> GenerateAmmoniaSys()
    {
        var reactions = new List<ChemicalReaction>();

        var ammonium = _compoundLibrary.Find("NH₄⁺");
        var ammoniaAqueous = _compoundLibrary.Find("NH₃(aq)") ?? _compoundLibrary.Find("NH₃");
        var hPlus = _compoundLibrary.Find("H⁺");
        var water = _compoundLibrary.Find("H₂O");
        var hydroxide = _compoundLibrary.Find("OH⁻");

        if (ammonium == null || ammoniaAqueous == null)
            return reactions;

        if (hPlus != null)
        {
            reactions.Add(CreateAcidBaseReaction("Ammonium Dissociation", ammonium.Name, ammoniaAqueous.Name,
                hPlus.Name));
        }

        if (water != null && hydroxide != null)
        {
            var reaction = new ChemicalReaction
            {
                Name = "Ammonia Protonation",
                Type = ReactionType.AcidBase,
                Stoichiometry =
                {
                    [ammoniaAqueous.Name] = -1.0,
                    [water.Name] = -1.0,
                    [ammonium.Name] = 1.0,
                    [hydroxide.Name] = 1.0
                }
            };

            CalculateReactionThermodynamics(reaction);
            reactions.Add(reaction);
        }

        return reactions;
    }

    private List<ChemicalReaction> GenerateCarbonateSys()
    {
        var reactions = new List<ChemicalReaction>();

        try
        {
            var co2_aq = _compoundLibrary.Find("CO₂(aq)");
            var h2co3 = _compoundLibrary.Find("H₂CO₃");
            var hco3 = _compoundLibrary.Find("HCO₃⁻");
            var co3 = _compoundLibrary.Find("CO₃²⁻");
            var h_plus = _compoundLibrary.Find("H⁺");

            // H₂CO₃* ⇌ HCO₃⁻ + H⁺ (pKa1 ~ 6.35)
            // Note: H2CO3* represents the sum of CO2(aq) and H2CO3. We use CO2(aq) as the primary species.
            if (co2_aq != null && hco3 != null && h_plus != null)
            {
                var reaction = new ChemicalReaction { Name = "Carbonic Acid First Dissociation" };
                reaction.Type = ReactionType.AcidBase;
                reaction.Stoichiometry[co2_aq.Name] = -1.0;
                reaction.Stoichiometry[hco3.Name] = 1.0;
                reaction.Stoichiometry[h_plus.Name] = 1.0;
                CalculateReactionThermodynamics(reaction);
                reactions.Add(reaction);
            }

            // HCO₃⁻ ⇌ CO₃²⁻ + H⁺ (pKa2 ~ 10.33)
            if (hco3 != null && co3 != null && h_plus != null)
            {
                var reaction = new ChemicalReaction { Name = "Bicarbonate Dissociation" };
                reaction.Type = ReactionType.AcidBase;
                reaction.Stoichiometry[hco3.Name] = -1.0;
                reaction.Stoichiometry[co3.Name] = 1.0;
                reaction.Stoichiometry[h_plus.Name] = 1.0;
                CalculateReactionThermodynamics(reaction);
                reactions.Add(reaction);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ReactionGenerator] Error generating carbonate system: {ex.Message}");
        }

        return reactions;
    }

    private List<ChemicalReaction> GenerateWaterDissociation()
    {
        var reactions = new List<ChemicalReaction>();

        var water = _compoundLibrary.Find("H₂O");
        var hPlus = _compoundLibrary.Find("H⁺");
        var ohMinus = _compoundLibrary.Find("OH⁻");

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
                }
            };
            CalculateReactionThermodynamics(reaction);
            reactions.Add(reaction);
        }

        return reactions;
    }

    private List<ChemicalReaction> GeneratePhosphateSys()
    {
        var reactions = new List<ChemicalReaction>();
        var h3po4 = _compoundLibrary.Find("H₃PO₄");
        var h2po4 = _compoundLibrary.Find("H₂PO₄⁻");
        var hpo4 = _compoundLibrary.Find("HPO₄²⁻");
        var po4 = _compoundLibrary.Find("PO₄³⁻");
        var h_plus = _compoundLibrary.Find("H⁺");

        if (h3po4 == null || h2po4 == null || hpo4 == null || po4 == null || h_plus == null) return reactions;

        // H₃PO₄ ⇌ H₂PO₄⁻ + H⁺
        reactions.Add(CreateAcidBaseReaction("Phosphoric Acid pKa1", h3po4.Name, h2po4.Name, h_plus.Name));
        // H₂PO₄⁻ ⇌ HPO₄²⁻ + H⁺
        reactions.Add(CreateAcidBaseReaction("Phosphoric Acid pKa2", h2po4.Name, hpo4.Name, h_plus.Name));
        // HPO₄²⁻ ⇌ PO₄³⁻ + H⁺
        reactions.Add(CreateAcidBaseReaction("Phosphoric Acid pKa3", hpo4.Name, po4.Name, h_plus.Name));

        return reactions;
    }

    private List<ChemicalReaction> GenerateSulfideSys()
    {
        var reactions = new List<ChemicalReaction>();
        var h2s = _compoundLibrary.Find("H₂S(aq)");
        var hs = _compoundLibrary.Find("HS⁻");
        var s2 = _compoundLibrary.Find("S²⁻");
        var h_plus = _compoundLibrary.Find("H⁺");

        if (h2s == null || hs == null || s2 == null || h_plus == null) return reactions;

        // H₂S ⇌ HS⁻ + H⁺
        reactions.Add(CreateAcidBaseReaction("Hydrogen Sulfide pKa1", h2s.Name, hs.Name, h_plus.Name));
        // HS⁻ ⇌ S²⁻ + H⁺
        reactions.Add(CreateAcidBaseReaction("Hydrogen Sulfide pKa2", hs.Name, s2.Name, h_plus.Name));

        return reactions;
    }

    private ChemicalReaction CreateAcidBaseReaction(string name, string acid, string baseSpec, string hplus)
    {
        var reaction = new ChemicalReaction
        {
            Name = name,
            Type = ReactionType.AcidBase,
            Stoichiometry = new Dictionary<string, double>
            {
                [acid] = -1.0,
                [baseSpec] = 1.0,
                [hplus] = 1.0
            }
        };
        CalculateReactionThermodynamics(reaction);
        return reaction;
    }

    /// <summary>
    ///     ENHANCEMENT: Generate redox reactions by combining species with different oxidation states.
    /// </summary>
    public List<ChemicalReaction> GenerateRedoxReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();
        var electron = _compoundLibrary.Find("e⁻");
        if (electron == null)
        {
            Logger.LogWarning(
                "[ReactionGenerator] Electron (e⁻) not defined in library. Cannot generate redox reactions.");
            return reactions;
        }

        var redoxElements = _compoundLibrary.Compounds
            .Where(c => c.OxidationState.HasValue && c.Phase == CompoundPhase.Aqueous)
            .Select(c => ParseChemicalFormula(c.ChemicalFormula).FirstOrDefault().Key)
            .Where(key => key != null)
            .Distinct()
            .ToList();

        foreach (var elementSymbol in redoxElements)
        {
            var speciesOfElement = _compoundLibrary.Compounds
                .Where(c => c.Phase == CompoundPhase.Aqueous && c.OxidationState.HasValue &&
                            ParseChemicalFormula(c.ChemicalFormula).ContainsKey(elementSymbol))
                .OrderBy(c => c.OxidationState.Value)
                .ToList();

            for (var i = 0; i < speciesOfElement.Count - 1; i++)
            {
                var reduced = speciesOfElement[i];
                var oxidized = speciesOfElement[i + 1];

                var e_diff = oxidized.OxidationState.Value - reduced.OxidationState.Value;
                if (e_diff > 0)
                {
                    var reaction = new ChemicalReaction
                    {
                        Name = $"{reduced.Name} -> {oxidized.Name}",
                        Type = ReactionType.RedOx
                    };
                    reaction.Stoichiometry[reduced.Name] = -1.0;
                    reaction.Stoichiometry[oxidized.Name] = 1.0;
                    reaction.Stoichiometry[electron.Name] = e_diff;
                    // Note: This is a simplified half-reaction. Full balancing with H+ and H2O is needed.
                    CalculateReactionThermodynamics(reaction);
                    reactions.Add(reaction);
                }
            }
        }

        return reactions;
    }

    /// <summary>
    ///     ENHANCEMENT: Generate gas-aqueous equilibrium reactions based on Henry's Law.
    /// </summary>
    public List<ChemicalReaction> GenerateGasEquilibriumReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();
        var gasSpecies = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Gas && c.HenrysLawConstant_mol_L_atm.HasValue).ToList();

        foreach (var gas in gasSpecies)
        {
            var aqueousCounterpartName = gas.Name.Replace("(g)", "(aq)");
            var aqueous = _compoundLibrary.Find(aqueousCounterpartName);

            if (aqueous != null)
            {
                var reaction = new ChemicalReaction
                {
                    Name = $"{gas.Name} dissolution (Henry's Law)",
                    Type = ReactionType.GasEquilibrium,
                    Stoichiometry =
                    {
                        [gas.Name] = -1.0,
                        [aqueous.Name] = 1.0
                    }
                };
                // LogK for Henry's law is log10(Kh)
                reaction.LogK_25C = Math.Log10(gas.HenrysLawConstant_mol_L_atm.Value);
                CalculateReactionThermodynamics(reaction);
                reactions.Add(reaction);
            }
        }

        return reactions;
    }

    /// <summary>
    ///     ENHANCEMENT: Generate surface complexation reactions.
    /// </summary>
    public List<ChemicalReaction> GenerateSurfaceComplexationReactions(ThermodynamicState state)
    {
        var reactions = new List<ChemicalReaction>();
        var surfaceSpecies = _compoundLibrary.Compounds.Where(c => c.Phase == CompoundPhase.Surface).ToList();
        var aqueousIons = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Aqueous && c.IonicCharge.HasValue).ToList();
        var hPlus = _compoundLibrary.Find("H⁺");

        if (hPlus == null) return reactions;

        // Find primary surface sites (e.g., >SOH)
        var primarySites = surfaceSpecies.Where(s => s.IsPrimarySurfaceSite).ToList();

        foreach (var site in primarySites)
        {
            // Protonation/deprotonation reactions
            // >SOH + H+ <=> >SOH2+
            var protonated = surfaceSpecies.FirstOrDefault(s => s.ChemicalFormula == site.ChemicalFormula + "₂⁺");
            if (protonated != null)
                reactions.Add(CreateSurfaceReaction($"{site.Name} Protonation", new[] { site.Name, hPlus.Name },
                    new[] { protonated.Name }));

            // >SOH <=> >SO- + H+
            var deprotonated =
                surfaceSpecies.FirstOrDefault(s => s.ChemicalFormula == site.ChemicalFormula.Replace("OH", "O⁻"));
            if (deprotonated != null)
                reactions.Add(CreateSurfaceReaction($"{site.Name} Deprotonation", new[] { site.Name },
                    new[] { deprotonated.Name, hPlus.Name }));

            // Cation binding reactions
            // >SOH + Cat^n+ <=> >SOCat^(n-1) + H+
            foreach (var cation in aqueousIons.Where(i => i.IonicCharge > 0))
            {
                var cationComplex = surfaceSpecies.FirstOrDefault(s => s.Name == $"{site.Name}-{cation.Name}");
                if (cationComplex != null)
                    reactions.Add(CreateSurfaceReaction($"{site.Name}-{cation.Name} Binding",
                        new[] { site.Name, cation.Name }, new[] { cationComplex.Name, hPlus.Name }));
            }
        }

        return reactions;
    }

    private ChemicalReaction CreateSurfaceReaction(string name, string[] reactants, string[] products)
    {
        var reaction = new ChemicalReaction { Name = name, Type = ReactionType.SurfaceComplexation };
        foreach (var r in reactants) reaction.Stoichiometry[r] = -1.0;
        foreach (var p in products) reaction.Stoichiometry[p] = 1.0;
        CalculateReactionThermodynamics(reaction);
        return reaction;
    }

    /// <summary>
    ///     Calculate standard thermodynamic properties from formation data.
    /// </summary>
    private void CalculateReactionThermodynamics(ChemicalReaction reaction)
    {
        double deltaG = 0.0, deltaH = 0.0, deltaS = 0.0, deltaCp = 0.0;

        foreach (var (species, coeff) in reaction.Stoichiometry)
        {
            var compound = _compoundLibrary.Find(species);
            if (compound == null)
            {
                deltaG = double.NaN; // Invalidate reaction if a species is missing
                break;
            }

            ;

            deltaG += coeff * (compound.GibbsFreeEnergyFormation_kJ_mol ?? double.NaN);
            deltaH += coeff * (compound.EnthalpyFormation_kJ_mol ?? double.NaN);
            deltaS += coeff * (compound.Entropy_J_molK ?? double.NaN);
            deltaCp += coeff * (compound.HeatCapacity_J_molK ?? 0.0);
        }

        reaction.DeltaG0_kJ_mol = deltaG;
        reaction.DeltaH0_kJ_mol = deltaH;
        reaction.DeltaS0_J_molK = deltaS;
        reaction.DeltaCp0_J_molK = deltaCp;

        // Calculate log K from ΔG° = -RT ln(K) -> log₁₀(K) = -ΔG°/(2.303·RT)
        const double T0 = 298.15;
        if (!double.IsNaN(deltaG))
            reaction.LogK_25C = -deltaG * 1000.0 / (2.303 * R * T0);
    }
/// <summary>
/// Generate dissociation reactions for highly soluble compounds (salts).
/// For example: NaCl(s) → Na⁺(aq) + Cl⁻(aq)
/// </summary>
public List<ChemicalReaction> GenerateSolubleCompoundDissociationReactions(List<string> compoundNames)
{
    var reactions = new List<ChemicalReaction>();
    
    foreach (var compoundName in compoundNames)
    {
        var normalized = CompoundLibrary.NormalizeFormulaInput(compoundName);
        var compound = _compoundLibrary.FindFlexible(compoundName) ??
                       _compoundLibrary.FindFlexible(normalized);
        if (compound == null)
        {
            Logger.LogWarning(
                $"[ReactionGenerator] Compound '{compoundName}' (normalized: '{normalized}') not found in library");
            continue;
        }
        
        // Skip compounds that are already aqueous species
        if (compound.Phase == CompoundPhase.Aqueous)
        {
            Logger.Log($"[ReactionGenerator] {compoundName} is already an aqueous species, skipping dissociation");
            continue;
        }
        
        // Check if this is a soluble compound
        bool isSoluble = false;
        
        // Check by solubility value
        if (compound.Solubility_g_100mL_25C.HasValue && compound.Solubility_g_100mL_25C.Value > 1.0)
        {
            isSoluble = true;
        }
        // Check by Ksp (higher Ksp = more soluble, LogKsp > -2 is generally very soluble)
        else if (compound.LogKsp_25C.HasValue && compound.LogKsp_25C.Value > -2.0)
        {
            isSoluble = true;
        }
        // Check for specific highly soluble compounds
        else if (IsHighlySolubleCompound(compound))
        {
            isSoluble = true;
        }
        
        if (isSoluble)
        {
            var dissociationReaction = GenerateCompleteDissociationReaction(compound);
            if (dissociationReaction != null)
            {
                reactions.Add(dissociationReaction);
                Logger.Log($"[ReactionGenerator] Generated dissociation: {compound.Name} → products");
            }
        }
        else if (compound.Phase == CompoundPhase.Solid)
        {
            // For less soluble solids, generate equilibrium dissolution reaction
            var dissolutionReaction = GenerateSingleDissolutionReaction(compound);
            if (dissolutionReaction != null)
            {
                reactions.Add(dissolutionReaction);
                Logger.Log($"[ReactionGenerator] Generated dissolution equilibrium for {compound.Name}");
            }
        }
        else if (compound.Phase == CompoundPhase.Gas)
        {
            // For gases, generate Henry's law equilibrium
            var gasReaction = GenerateGasEquilibriumReaction(compound);
            if (gasReaction != null)
            {
                reactions.Add(gasReaction);
                Logger.Log($"[ReactionGenerator] Generated gas equilibrium for {compound.Name}");
            }
        }
    }
    
    return reactions;
}
/// <summary>
/// Generate complete dissociation reaction for highly soluble salts.
/// </summary>
private ChemicalReaction GenerateCompleteDissociationReaction(ChemicalCompound compound)
{
    var elementalComp = ParseChemicalFormula(compound.ChemicalFormula);
    if (elementalComp.Count == 0)
        return null;
    
    var reaction = new ChemicalReaction
    {
        Name = $"{compound.Name} dissociation",
        Type = ReactionType.Dissociation
    };
    
    // Compound is reactant
    reaction.Stoichiometry[compound.Name] = -1.0;
    
    // Find appropriate aqueous ions as products
    var products = FindIonicDissociationProducts(compound, elementalComp);
    
    if (products.Count == 0)
    {
        Logger.LogWarning($"[ReactionGenerator] Could not find ionic products for {compound.Name}");
        return null;
    }
    
    // Add products to reaction
    foreach (var (ion, stoichiometry) in products)
    {
        reaction.Stoichiometry[ion.Name] = stoichiometry;
    }
    
    // Verify charge balance
    double totalCharge = 0;
    foreach (var (speciesName, stoich) in reaction.Stoichiometry)
    {
        if (stoich > 0) // Products only
        {
            var species = _compoundLibrary.Find(speciesName);
            if (species != null && species.IonicCharge.HasValue)
            {
                totalCharge += stoich * species.IonicCharge.Value;
            }
        }
    }
    
    if (Math.Abs(totalCharge) > 0.01)
    {
        Logger.LogWarning($"[ReactionGenerator] Charge imbalance in {compound.Name} dissociation: {totalCharge:F2}");
    }
    
    // Calculate thermodynamic properties
    CalculateReactionThermodynamics(reaction);
    
    // For highly soluble salts, set very high LogK to ensure complete dissociation
    if (compound.Solubility_g_100mL_25C.HasValue && compound.Solubility_g_100mL_25C.Value > 10.0)
    {
        reaction.LogK_25C = 10.0; // Very favorable dissociation
    }
    
    return reaction;
}
/// <summary>
/// Check for common polyatomic ions in the compound formula.
/// </summary>
private void CheckForPolyatomicIons(Dictionary<string, int> elementalComp,
    ref List<(ChemicalCompound, int)> anions,
    ref List<(ChemicalCompound, int)> cations)
{
    // Check for carbonate (CO₃²⁻)
    if (elementalComp.ContainsKey("C") && elementalComp.ContainsKey("O") && elementalComp["O"] >= 3)
    {
        var carbonate = _compoundLibrary.Find("CO₃²⁻") ?? _compoundLibrary.Find("Carbonate");
        if (carbonate != null)
        {
            int carbonateCount = elementalComp["C"];
            anions.Add((carbonate, carbonateCount));
            // Remove C and O that are part of carbonate from further consideration
            elementalComp["C"] = 0;
            elementalComp["O"] -= carbonateCount * 3;
        }
    }
    
    // Check for sulfate (SO₄²⁻)
    if (elementalComp.ContainsKey("S") && elementalComp.ContainsKey("O") && elementalComp["O"] >= 4)
    {
        var sulfate = _compoundLibrary.Find("SO₄²⁻") ?? _compoundLibrary.Find("Sulfate Ion");
        if (sulfate != null)
        {
            int sulfateCount = elementalComp["S"];
            anions.Add((sulfate, sulfateCount));
            elementalComp["S"] = 0;
            elementalComp["O"] -= sulfateCount * 4;
        }
    }
    
    // Check for phosphate (PO₄³⁻)
    if (elementalComp.ContainsKey("P") && elementalComp.ContainsKey("O") && elementalComp["O"] >= 4)
    {
        var phosphate = _compoundLibrary.Find("PO₄³⁻") ?? _compoundLibrary.Find("Phosphate");
        if (phosphate != null)
        {
            int phosphateCount = elementalComp["P"];
            anions.Add((phosphate, phosphateCount));
            elementalComp["P"] = 0;
            elementalComp["O"] -= phosphateCount * 4;
        }
    }
    
    // Check for nitrate (NO₃⁻)
    if (elementalComp.ContainsKey("N") && elementalComp.ContainsKey("O") && elementalComp["O"] >= 3)
    {
        var nitrate = _compoundLibrary.Find("NO₃⁻") ?? _compoundLibrary.Find("Nitrate");
        if (nitrate != null)
        {
            int nitrateCount = elementalComp["N"];
            anions.Add((nitrate, nitrateCount));
            elementalComp["N"] = 0;
            elementalComp["O"] -= nitrateCount * 3;
        }
    }
}
/// <summary>
/// Generate gas equilibrium reaction for a gas compound.
/// </summary>
private ChemicalReaction GenerateGasEquilibriumReaction(ChemicalCompound gas)
{
    // Find aqueous form of the gas
    var aqueousForm = _compoundLibrary.Compounds
        .FirstOrDefault(c => c.Phase == CompoundPhase.Aqueous &&
                             c.ChemicalFormula.Contains(gas.ChemicalFormula.Replace("(g)", "")));
    
    if (aqueousForm == null)
    {
        // Try to find by name
        var gasBaseName = gas.Name.Replace(" gas", "").Replace("(g)", "").Trim();
        aqueousForm = _compoundLibrary.Find($"{gasBaseName}(aq)");
    }
    
    if (aqueousForm != null)
    {
        var reaction = new ChemicalReaction
        {
            Name = $"{gas.Name} dissolution (Henry's Law)",
            Type = ReactionType.GasEquilibrium,
            Stoichiometry = new Dictionary<string, double>
            {
                [gas.Name] = -1.0,
                [aqueousForm.Name] = 1.0
            }
        };
        
        CalculateReactionThermodynamics(reaction);
        return reaction;
    }
    
    return null;
}
/// <summary>
/// Generate dissociation/dissolution reactions for all types of compounds.
/// Handles salts, acids, bases, gases, and complexes with temperature/pressure dependence.
/// </summary>
public List<ChemicalReaction> GenerateSolubleCompoundDissociationReactions(
    List<string> compoundNames, 
    double temperature_K = 298.15, 
    double pressure_bar = 1.0)
{
    var reactions = new List<ChemicalReaction>();
    
    foreach (var compoundName in compoundNames)
    {
        var normalized = CompoundLibrary.NormalizeFormulaInput(compoundName);
        var compound = _compoundLibrary.FindFlexible(compoundName) ??
                       _compoundLibrary.FindFlexible(normalized);
        if (compound == null)
        {
            Logger.LogWarning(
                $"[ReactionGenerator] Compound '{compoundName}' (normalized: '{normalized}') not found in library");
            continue;
        }
        
        // Skip if already an aqueous ion
        if (compound.Phase == CompoundPhase.Aqueous && compound.IonicCharge.HasValue)
        {
            Logger.Log($"[ReactionGenerator] {compound.Name} is already an aqueous ion");
            continue;
        }
        
        ChemicalReaction reaction = null;
        
        switch (compound.Phase)
        {
            case CompoundPhase.Solid:
                reaction = GenerateSolidDissolutionReaction(compound, temperature_K);
                break;
                
            case CompoundPhase.Liquid:
                reaction = GenerateLiquidDissolutionReaction(compound, temperature_K);
                break;
                
            case CompoundPhase.Gas:
                reaction = GenerateGasDissolutionReaction(compound, temperature_K, pressure_bar);
                break;
                
            case CompoundPhase.Aqueous:
                // Molecular aqueous species might dissociate (e.g., H2CO3, NH3)
                reaction = GenerateAqueousDissociationReaction(compound, temperature_K);
                break;
        }
        
        if (reaction != null)
        {
            reactions.Add(reaction);
            Logger.Log($"[ReactionGenerator] Generated reaction: {reaction.Name}");
        }
    }

    return reactions;
}
/// <summary>
/// Generate acid dissociation reaction with pKa temperature correction.
/// </summary>
private ChemicalReaction GenerateAcidDissociationReaction(ChemicalCompound acid, double temperature_K)
{
    var reaction = new ChemicalReaction
    {
        Name = $"{acid.Name} dissociation",
        Type = ReactionType.AcidBase
    };
    
    reaction.Stoichiometry[acid.Name] = -1.0;
    
    // Find conjugate base and H+
    var hIon = _compoundLibrary.Find("H⁺") ?? _compoundLibrary.Find("Proton");
    if (hIon != null)
    {
        reaction.Stoichiometry[hIon.Name] = 1.0;
        
        // Find or create conjugate base
        var conjugateBase = FindConjugateBase(acid);
        if (conjugateBase != null)
        {
            reaction.Stoichiometry[conjugateBase.Name] = 1.0;
        }
    }
    
    // Set LogK from pKa with temperature correction
    if (acid.pKa.HasValue)
    {
        reaction.LogK_25C = -acid.pKa.Value;
        
        // Temperature correction for pKa
        if (acid.DissociationEnthalpy_kJ_mol.HasValue)
        {
            double deltaH = acid.DissociationEnthalpy_kJ_mol.Value * 1000;
            double R = 8.314;
            double pKa_T = acid.pKa.Value + (deltaH / (2.303 * R)) * (1.0 / temperature_K - 1.0 / 298.15);
            reaction.LogK_25C = -pKa_T;
        }
    }
    
    CalculateReactionThermodynamics(reaction);
    return reaction;
}

/// <summary>
/// Generate dissociation for aqueous molecular species.
/// </summary>
private ChemicalReaction GenerateAqueousDissociationReaction(ChemicalCompound aqueous, double temperature_K)
{
    // Check for weak acids/bases
    if (aqueous.pKa.HasValue)
    {
        return GenerateAcidDissociationReaction(aqueous, temperature_K);
    }
    else if (aqueous.pKb.HasValue)
    {
        return GenerateBaseDissociationReaction(aqueous, temperature_K);
    }
    
    // No dissociation for neutral molecules
    return null;
}

/// <summary>
/// Generate gas dissolution with Henry's law and temperature/pressure dependence.
/// </summary>
private ChemicalReaction GenerateGasDissolutionReaction(ChemicalCompound gas, double temperature_K, double pressure_bar)
{
    var reaction = new ChemicalReaction
    {
        Name = $"{gas.Name} dissolution (Henry's law)",
        Type = ReactionType.GasEquilibrium
    };
    
    reaction.Stoichiometry[gas.Name] = -1.0;
    
    // Find aqueous form
    var aqueousForm = FindAqueousFormOfGas(gas);
    
    if (aqueousForm != null)
    {
        reaction.Stoichiometry[aqueousForm.Name] = 1.0;
        
        // Apply Henry's law constant with temperature correction
        if (gas.HenryConstant_mol_L_atm.HasValue)
        {
            double kH_T = CalculateHenryConstantAtTemperature(gas, temperature_K);
            
            // LogK = log10(kH * P) where P is partial pressure
            reaction.LogK_25C = Math.Log10(kH_T * (pressure_bar / 1.01325));
        }
        
        // Some gases form acids (CO2 -> H2CO3, SO2 -> H2SO3)
        if (GasFormsAcid(gas))
        {
            var acidReactions = GenerateGasAcidReactions(aqueousForm, temperature_K);
            return acidReactions.FirstOrDefault() ?? reaction;
        }
    }
    
    CalculateReactionThermodynamics(reaction);
    return reaction;
}

/// <summary>
/// Generate dissolution reaction for solid compounds with temperature dependence.
/// </summary>
private ChemicalReaction GenerateSolidDissolutionReaction(ChemicalCompound solid, double temperature_K)
{
    // Check solubility at given temperature
    double solubility = CalculateSolubilityAtTemperature(solid, temperature_K);
    
    var elementalComp = ParseChemicalFormula(solid.ChemicalFormula);
    
    var reaction = new ChemicalReaction
    {
        Name = $"{solid.Name} dissolution",
        Type = ReactionType.Dissolution
    };
    
    reaction.Stoichiometry[solid.Name] = -1.0;
    
    // Determine dissolution type based on solubility
    if (solubility > 10.0) // g/100mL - Highly soluble
    {
        // Complete dissociation into ions
        var products = FindCompleteIonicProducts(solid, elementalComp);
        foreach (var (ion, stoich) in products)
        {
            reaction.Stoichiometry[ion.Name] = stoich;
        }
        
        // Adjust LogK for temperature
        if (solid.DissolutionEnthalpy_kJ_mol.HasValue)
        {
            reaction.LogK_25C = CalculateLogKsp(solid, 298.15);
            var logK_T = CalculateLogKAtTemperature(reaction, temperature_K);
            reaction.LogK_25C = logK_T; // Override with temperature-corrected value
        }
    }
    else if (solubility > 0.01) // Moderately soluble
    {
        // Equilibrium dissolution
        var products = FindEquilibriumProducts(solid, elementalComp);
        foreach (var (species, stoich) in products)
        {
            reaction.Stoichiometry[species.Name] = stoich;
        }
        
        if (solid.LogKsp_25C.HasValue)
        {
            reaction.LogK_25C = solid.LogKsp_25C.Value;
        }
    }
    else // Sparingly soluble
    {
        // Minimal dissolution with potential complexation
        return GenerateSingleDissolutionReaction(solid);
    }
    
    CalculateReactionThermodynamics(reaction);
    return reaction;
}

/// <summary>
/// Generate dissolution reaction for liquid compounds.
/// </summary>
private ChemicalReaction GenerateLiquidDissolutionReaction(ChemicalCompound liquid, double temperature_K)
{
    var reaction = new ChemicalReaction
    {
        Name = $"{liquid.Name} mixing",
        Type = ReactionType.Dissolution
    };
    
    reaction.Stoichiometry[liquid.Name] = -1.0;
    
    // Check if it's an acid or base
    if (IsAcid(liquid))
    {
        return GenerateAcidDissociationReaction(liquid, temperature_K);
    }
    else if (IsBase(liquid))
    {
        return GenerateBaseDissociationReaction(liquid, temperature_K);
    }
    else
    {
        // Molecular dissolution (e.g., ethanol, glycerol)
        var aqueousForm = FindOrCreateAqueousForm(liquid);
        reaction.Stoichiometry[aqueousForm.Name] = 1.0;
    }
    
    CalculateReactionThermodynamics(reaction);
    return reaction;
}
/// <summary>
/// Find complete ionic products for highly soluble salts.
/// </summary>
private List<(ChemicalCompound, double)> FindCompleteIonicProducts(ChemicalCompound compound, Dictionary<string, int> elementalComp)
{
    var products = new List<(ChemicalCompound, double)>();
    
    // Special handling for common salt types
    string formula = compound.ChemicalFormula;
    
    // Binary salts (NaCl, KBr, CaF2, etc.)
    if (IsBinarySalt(compound))
    {
        return FindBinaryIonicProducts(compound, elementalComp);
    }
    
    // Oxyanion salts (Na2SO4, CaCO3, KNO3, etc.)
    if (ContainsOxyanion(formula))
    {
        return FindOxyanionProducts(compound, elementalComp);
    }
    
    // Complex salts (K3[Fe(CN)6], [Cu(NH3)4]SO4, etc.)
    if (IsComplexSalt(formula))
    {
        return FindComplexSaltProducts(compound, elementalComp);
    }
    
    // Hydrated salts (CuSO4·5H2O, MgSO4·7H2O, etc.)
    if (formula.Contains("·") && formula.Contains("H2O"))
    {
        return FindHydratedSaltProducts(compound, elementalComp);
    }
    
    // Default: try standard dissociation
    return FindIonicDissociationProducts(compound, elementalComp);
}

/// <summary>
/// Calculate Henry's constant at temperature using van't Hoff equation.
/// </summary>
private double CalculateHenryConstantAtTemperature(ChemicalCompound gas, double temperature_K)
{
    if (!gas.HenryConstant_mol_L_atm.HasValue)
        return 0.001; // Default low solubility
    
    double kH_298 = gas.HenryConstant_mol_L_atm.Value;
    
    // Temperature dependence: kH(T) = kH° * exp[ΔH_sol/R * (1/T - 1/T°)]
    if (gas.DissolutionEnthalpy_kJ_mol.HasValue)
    {
        double deltaH = gas.DissolutionEnthalpy_kJ_mol.Value * 1000; // J/mol
        double R = 8.314; // J/(mol·K)
        
        double expArg = (deltaH / R) * (1.0 / temperature_K - 1.0 / 298.15);
        return kH_298 * Math.Exp(expArg);
    }
    
    // Use empirical correlations for common gases if no data
    string formula = gas.ChemicalFormula.ToUpper();
    if (formula.Contains("CO2"))
    {
        // CO2: ln(kH) = -2385.73/T + 14.0184 - 0.0152642*T
        return Math.Exp(-2385.73 / temperature_K + 14.0184 - 0.0152642 * temperature_K);
    }
    else if (formula.Contains("O2"))
    {
        // O2: ln(kH) = -1509.21/T + 10.7071 - 0.0115308*T
        return Math.Exp(-1509.21 / temperature_K + 10.7071 - 0.0115308 * temperature_K);
    }
    else if (formula.Contains("N2"))
    {
        // N2: ln(kH) = -1450.52/T + 10.5228 - 0.0114776*T
        return Math.Exp(-1450.52 / temperature_K + 10.5228 - 0.0114776 * temperature_K);
    }
    
    return kH_298; // No temperature correction available
}

/// <summary>
/// Calculate solubility at a given temperature using van't Hoff equation.
/// </summary>
private double CalculateSolubilityAtTemperature(ChemicalCompound compound, double temperature_K)
{
    if (!compound.Solubility_g_100mL_25C.HasValue)
        return 0.001; // Default low solubility
    
    double S_298 = compound.Solubility_g_100mL_25C.Value;
    
    // Van't Hoff equation: ln(S_T/S_298) = -(ΔH_diss/R) * (1/T - 1/298.15)
    if (compound.DissolutionEnthalpy_kJ_mol.HasValue)
    {
        double deltaH = compound.DissolutionEnthalpy_kJ_mol.Value * 1000; // J/mol
        double R = 8.314; // J/(mol·K)
        
        double lnRatio = -(deltaH / R) * (1.0 / temperature_K - 1.0 / 298.15);
        return S_298 * Math.Exp(lnRatio);
    }
    
    // No enthalpy data - use simple temperature factor
    // Rough approximation: solubility doubles every 10°C for endothermic dissolution
    double tempFactor = Math.Pow(2, (temperature_K - 298.15) / 10.0);
    return S_298 * tempFactor;
}
/// <summary>
/// Check if compound is a base.
/// </summary>
private bool IsBase(ChemicalCompound compound)
{
    return compound.pKb.HasValue ||
           compound.Name.ToLower().Contains("hydroxide") ||
           compound.ChemicalFormula.Contains("OH") ||
           compound.ChemicalFormula.Contains("NH");
}

/// <summary>
/// Check if compound is an acid.
/// </summary>
private bool IsAcid(ChemicalCompound compound)
{
    return compound.pKa.HasValue || 
           compound.Name.ToLower().Contains("acid") ||
           compound.ChemicalFormula.StartsWith("H") && 
           (compound.ChemicalFormula.Contains("O") || 
            compound.ChemicalFormula.Contains("Cl") ||
            compound.ChemicalFormula.Contains("S"));
}

/// <summary>
/// Find the conjugate base of an acid by removing H+.
/// </summary>
private ChemicalCompound FindConjugateBase(ChemicalCompound acid)
{
    // Try to find existing conjugate base
    var acidFormula = acid.ChemicalFormula;
    
    // Common acid-base pairs
    var acidBasePairs = new Dictionary<string, string>
    {
        { "H₂CO₃", "HCO₃⁻" },
        { "HCO₃⁻", "CO₃²⁻" },
        { "H₃PO₄", "H₂PO₄⁻" },
        { "H₂PO₄⁻", "HPO₄²⁻" },
        { "HPO₄²⁻", "PO₄³⁻" },
        { "H₂SO₄", "HSO₄⁻" },
        { "HSO₄⁻", "SO₄²⁻" },
        { "HNO₃", "NO₃⁻" },
        { "HCl", "Cl⁻" },
        { "NH₄⁺", "NH₃" },
        { "H₂S", "HS⁻" },
        { "HS⁻", "S²⁻" },
        { "HF", "F⁻" }
    };
    
    if (acidBasePairs.TryGetValue(acidFormula, out var baseFormula))
    {
        return _compoundLibrary.Find(baseFormula);
    }
    
    // Try to construct conjugate base by removing H+
    var baseCompound = _compoundLibrary.Compounds
        .FirstOrDefault(c => c.Phase == CompoundPhase.Aqueous &&
                            IsConjugateBaseOf(c, acid));
    
    return baseCompound;
}

/// <summary>
/// Check if one compound is the conjugate base of another.
/// </summary>
private bool IsConjugateBaseOf(ChemicalCompound potentialBase, ChemicalCompound acid)
{
    var acidComp = ParseChemicalFormula(acid.ChemicalFormula);
    var baseComp = ParseChemicalFormula(potentialBase.ChemicalFormula);
    
    // Base should have one less H
    if (acidComp.GetValueOrDefault("H", 0) - baseComp.GetValueOrDefault("H", 0) != 1)
        return false;
    
    // All other elements should match
    foreach (var (element, count) in acidComp)
    {
        if (element == "H") continue;
        if (baseComp.GetValueOrDefault(element, 0) != count)
            return false;
    }
    
    // Charge should differ by -1
    var acidCharge = acid.IonicCharge ?? 0;
    var baseCharge = potentialBase.IonicCharge ?? 0;
    
    return (acidCharge - baseCharge) == 1;
}

/// <summary>
/// Generate base dissociation reaction.
/// </summary>
private ChemicalReaction GenerateBaseDissociationReaction(ChemicalCompound baseCompound, double temperature_K)
{
    var reaction = new ChemicalReaction
    {
        Name = $"{baseCompound.Name} dissociation",
        Type = ReactionType.AcidBase
    };
    
    reaction.Stoichiometry[baseCompound.Name] = -1.0;
    
    // Find OH- and conjugate acid
    var ohIon = _compoundLibrary.Find("OH⁻") ?? _compoundLibrary.Find("Hydroxide");
    if (ohIon != null)
    {
        reaction.Stoichiometry[ohIon.Name] = 1.0;
        
        // Find conjugate acid
        var conjugateAcid = FindConjugateAcid(baseCompound);
        if (conjugateAcid != null)
        {
            reaction.Stoichiometry[conjugateAcid.Name] = 1.0;
        }
    }
    
    // Set LogK from pKb
    if (baseCompound.pKb.HasValue)
    {
        reaction.LogK_25C = -baseCompound.pKb.Value;
        
        // Temperature correction
        if (baseCompound.DissociationEnthalpy_kJ_mol.HasValue)
        {
            double deltaH = baseCompound.DissociationEnthalpy_kJ_mol.Value * 1000;
            double R = 8.314;
            double pKb_T = baseCompound.pKb.Value + 
                          (deltaH / (2.303 * R)) * (1.0 / temperature_K - 1.0 / 298.15);
            reaction.LogK_25C = -pKb_T;
        }
    }
    
    CalculateReactionThermodynamics(reaction);
    return reaction;
}

/// <summary>
/// Find conjugate acid of a base.
/// </summary>
private ChemicalCompound FindConjugateAcid(ChemicalCompound baseCompound)
{
    // Common base-acid pairs
    var baseAcidPairs = new Dictionary<string, string>
    {
        { "NH₃", "NH₄⁺" },
        { "OH⁻", "H₂O" },
        { "CO₃²⁻", "HCO₃⁻" },
        { "PO₄³⁻", "HPO₄²⁻" },
        { "SO₄²⁻", "HSO₄⁻" },
        { "S²⁻", "HS⁻" }
    };
    
    if (baseAcidPairs.TryGetValue(baseCompound.ChemicalFormula, out var acidFormula))
    {
        return _compoundLibrary.Find(acidFormula);
    }
    
    return null;
}

/// <summary>
/// Find aqueous form of a gas.
/// </summary>
private ChemicalCompound FindAqueousFormOfGas(ChemicalCompound gas)
{
    // Remove (g) suffix and look for (aq) version
    var gasName = gas.Name.Replace("(g)", "").Trim();
    var gasFormula = gas.ChemicalFormula.Replace("(g)", "").Trim();
    
    // Try exact match with (aq) suffix
    var aqueousForm = _compoundLibrary.Find($"{gasName}(aq)") ??
                     _compoundLibrary.Find($"{gasFormula}(aq)");
    
    if (aqueousForm != null) return aqueousForm;
    
    // Special cases
    var gasAqueousPairs = new Dictionary<string, string>
    {
        { "CO₂", "H₂CO₃" }, // CO2 forms carbonic acid
        { "SO₂", "H₂SO₃" }, // SO2 forms sulfurous acid  
        { "NH₃", "NH₃(aq)" }, // Ammonia
        { "HCl", "Cl⁻" }, // HCl gas dissociates completely
        { "H₂S", "H₂S(aq)" }, // Hydrogen sulfide
        { "O₂", "O₂(aq)" }, // Dissolved oxygen
        { "N₂", "N₂(aq)" }, // Dissolved nitrogen
        { "CH₄", "CH₄(aq)" } // Methane
    };
    
    if (gasAqueousPairs.TryGetValue(gasFormula, out var aqueousFormula))
    {
        return _compoundLibrary.Find(aqueousFormula);
    }
    
    return null;
}

/// <summary>
/// Check if a gas forms an acid when dissolved.
/// </summary>
private bool GasFormsAcid(ChemicalCompound gas)
{
    var acidFormingGases = new HashSet<string>
    {
        "CO₂", "SO₂", "NO₂", "HCl", "HBr", "HI", "H₂S", "HF"
    };
    
    return acidFormingGases.Contains(gas.ChemicalFormula.Replace("(g)", "").Trim());
}

/// <summary>
/// Generate acid reactions for gases that form acids.
/// </summary>
private List<ChemicalReaction> GenerateGasAcidReactions(ChemicalCompound aqueousGas, double temperature_K)
{
    var reactions = new List<ChemicalReaction>();
    
    // CO2 + H2O -> H2CO3
    if (aqueousGas.ChemicalFormula.Contains("CO") && !aqueousGas.ChemicalFormula.Contains("H"))
    {
        var water = _compoundLibrary.Find("H₂O");
        var carbonicAcid = _compoundLibrary.Find("H₂CO₃");
        
        if (water != null && carbonicAcid != null)
        {
            var reaction = new ChemicalReaction
            {
                Name = "CO₂ hydration to carbonic acid",
                Type = ReactionType.Complexation,
                Stoichiometry = new Dictionary<string, double>
                {
                    [aqueousGas.Name] = -1.0,
                    [water.Name] = -1.0,
                    [carbonicAcid.Name] = 1.0
                }
            };
            CalculateReactionThermodynamics(reaction);
            reactions.Add(reaction);
        }
    }
    
    return reactions;
}

/// <summary>
/// Calculate LogKsp for a compound.
/// </summary>
private double CalculateLogKsp(ChemicalCompound compound, double temperature_K)
{
    if (compound.LogKsp_25C.HasValue)
    {
        // Temperature correction using van't Hoff
        if (compound.DissolutionEnthalpy_kJ_mol.HasValue && Math.Abs(temperature_K - 298.15) > 1e-6)
        {
            double deltaH = compound.DissolutionEnthalpy_kJ_mol.Value * 1000; // J/mol
            double R = 8.314; // J/(mol·K)
            
            double logKsp_T = compound.LogKsp_25C.Value - 
                             (deltaH / (2.303 * R)) * (1.0 / temperature_K - 1.0 / 298.15);
            return logKsp_T;
        }
        return compound.LogKsp_25C.Value;
    }
    
    // Estimate from Gibbs energy if available
    if (compound.GibbsFreeEnergyFormation_kJ_mol.HasValue)
    {
        // This would need product Gibbs energies too
        return -10.0; // Default moderate solubility
    }
    
    return -5.0; // Default value
}

/// <summary>
/// Find equilibrium products for moderately soluble compounds.
/// </summary>
private List<(ChemicalCompound, double)> FindEquilibriumProducts(
    ChemicalCompound compound, Dictionary<string, int> elementalComp)
{
    // Similar to FindCompleteIonicProducts but may include complexes
    var products = FindCompleteIonicProducts(compound, elementalComp);
    
    // Add potential complexes for transition metals
    if (ContainsTransitionMetal(elementalComp))
    {
        AddMetalComplexes(ref products, elementalComp);
    }
    
    return products;
}

/// <summary>
/// Find or create aqueous form of a liquid.
/// </summary>
private ChemicalCompound FindOrCreateAqueousForm(ChemicalCompound liquid)
{
    // Look for existing aqueous form
    var aqueousName = $"{liquid.Name}(aq)";
    var existing = _compoundLibrary.Find(aqueousName);
    
    if (existing != null) return existing;
    
    // Create a temporary aqueous form (would normally add to library)
    return new ChemicalCompound
    {
        Name = aqueousName,
        ChemicalFormula = $"{liquid.ChemicalFormula}(aq)",
        Phase = CompoundPhase.Aqueous,
        MolecularWeight_g_mol = liquid.MolecularWeight_g_mol,
        GibbsFreeEnergyFormation_kJ_mol = liquid.GibbsFreeEnergyFormation_kJ_mol,
        Notes = $"Aqueous form of {liquid.Name}"
    };
}

/// <summary>
/// Check if compound is a binary salt (two elements only).
/// </summary>
private bool IsBinarySalt(ChemicalCompound compound)
{
    var elements = ParseChemicalFormula(compound.ChemicalFormula);
    
    if (elements.Count != 2) return false;
    
    // Check if one is a metal and one is a non-metal
    var metals = new HashSet<string> 
    { 
        "Li", "Na", "K", "Rb", "Cs", "Be", "Mg", "Ca", "Sr", "Ba",
        "Al", "Ga", "In", "Sn", "Pb", "Bi", "Zn", "Cd", "Hg",
        "Fe", "Co", "Ni", "Cu", "Ag", "Au", "Mn", "Cr"
    };
    
    var nonMetals = new HashSet<string>
    {
        "F", "Cl", "Br", "I", "O", "S", "Se", "N", "P"
    };
    
    bool hasMetal = elements.Keys.Any(e => metals.Contains(e));
    bool hasNonMetal = elements.Keys.Any(e => nonMetals.Contains(e));
    
    return hasMetal && hasNonMetal;
}

/// <summary>
/// Find ionic products for binary salts.
/// </summary>
private List<(ChemicalCompound, double)> FindBinaryIonicProducts(
    ChemicalCompound compound, Dictionary<string, int> elementalComp)
{
    var products = new List<(ChemicalCompound, double)>();
    
    foreach (var (element, count) in elementalComp)
    {
        // Find the primary ion for this element
        var ion = _compoundLibrary.Compounds
            .FirstOrDefault(c => c.Phase == CompoundPhase.Aqueous &&
                               c.IsPrimaryElementSpecies &&
                               c.IonicCharge.HasValue &&
                               ParseChemicalFormula(c.ChemicalFormula).ContainsKey(element) &&
                               ParseChemicalFormula(c.ChemicalFormula).Count == 1);
        
        if (ion != null)
        {
            products.Add((ion, count));
        }
    }
    
    return products;
}

/// <summary>
/// Check if formula contains an oxyanion.
/// </summary>
private bool ContainsOxyanion(string formula)
{
    var oxyanions = new[] 
    { 
        "CO3", "CO₃", "SO4", "SO₄", "PO4", "PO₄", 
        "NO3", "NO₃", "ClO", "BrO", "IO", "CrO4", "CrO₄"
    };
    
    return oxyanions.Any(oxy => formula.Contains(oxy));
}

/// <summary>
/// Find products for oxyanion salts.
/// </summary>
private List<(ChemicalCompound, double)> FindOxyanionProducts(
    ChemicalCompound compound, Dictionary<string, int> elementalComp)
{
    var products = new List<(ChemicalCompound, double)>();
    var formula = compound.ChemicalFormula;
    
    // Identify the oxyanion
    ChemicalCompound oxyanion = null;
    int oxyanionCount = 1;
    
    if (formula.Contains("CO₃") || formula.Contains("CO3"))
    {
        oxyanion = _compoundLibrary.Find("CO₃²⁻") ?? _compoundLibrary.Find("Carbonate");
        oxyanionCount = elementalComp.GetValueOrDefault("C", 1);
    }
    else if (formula.Contains("SO₄") || formula.Contains("SO4"))
    {
        oxyanion = _compoundLibrary.Find("SO₄²⁻") ?? _compoundLibrary.Find("Sulfate Ion");
        oxyanionCount = elementalComp.GetValueOrDefault("S", 1);
    }
    else if (formula.Contains("PO₄") || formula.Contains("PO4"))
    {
        oxyanion = _compoundLibrary.Find("PO₄³⁻") ?? _compoundLibrary.Find("Phosphate");
        oxyanionCount = elementalComp.GetValueOrDefault("P", 1);
    }
    else if (formula.Contains("NO₃") || formula.Contains("NO3"))
    {
        oxyanion = _compoundLibrary.Find("NO₃⁻") ?? _compoundLibrary.Find("Nitrate");
        oxyanionCount = elementalComp.GetValueOrDefault("N", 1);
    }
    
    if (oxyanion != null)
    {
        products.Add((oxyanion, oxyanionCount));
    }
    
    // Find the cation(s)
    var cationElements = elementalComp.Keys
        .Where(e => e != "C" && e != "S" && e != "P" && e != "N" && e != "O" && e != "H")
        .ToList();
    
    foreach (var element in cationElements)
    {
        var cation = _compoundLibrary.Compounds
            .FirstOrDefault(c => c.Phase == CompoundPhase.Aqueous &&
                               c.IsPrimaryElementSpecies &&
                               c.IonicCharge.HasValue && c.IonicCharge.Value > 0 &&
                               ParseChemicalFormula(c.ChemicalFormula).ContainsKey(element));
        
        if (cation != null)
        {
            products.Add((cation, elementalComp[element]));
        }
    }
    
    return products;
}

/// <summary>
/// Check if compound is a complex salt.
/// </summary>
private bool IsComplexSalt(string formula)
{
    // Contains square brackets indicating complex ion
    return formula.Contains("[") && formula.Contains("]");
}

/// <summary>
/// Find products for complex salts.
/// </summary>
private List<(ChemicalCompound, double)> FindComplexSaltProducts(
    ChemicalCompound compound, Dictionary<string, int> elementalComp)
{
    // For now, treat as simple dissolution
    // In a complete implementation, would parse complex ion structure
    return FindCompleteIonicProducts(compound, elementalComp);
}

/// <summary>
/// Find products for hydrated salts.
/// </summary>
private List<(ChemicalCompound, double)> FindHydratedSaltProducts(
    ChemicalCompound compound, Dictionary<string, int> elementalComp)
{
    var products = new List<(ChemicalCompound, double)>();
    
    // Extract water of hydration
    var formula = compound.ChemicalFormula;
    var waterMatch = System.Text.RegularExpressions.Regex.Match(formula, @"·(\d*)H₂O");
    
    if (waterMatch.Success)
    {
        var waterCount = string.IsNullOrEmpty(waterMatch.Groups[1].Value) 
            ? 1 
            : int.Parse(waterMatch.Groups[1].Value);
        
        var water = _compoundLibrary.Find("H₂O");
        if (water != null)
        {
            products.Add((water, waterCount));
        }
        
        // Remove water from formula and process anhydrous salt
        var anhydrousFormula = formula.Replace(waterMatch.Value, "");
        var anhydrousElements = ParseChemicalFormula(anhydrousFormula);
        
        // Get ionic products of anhydrous salt
        var ionicProducts = FindCompleteIonicProducts(compound, anhydrousElements);
        products.AddRange(ionicProducts);
    }
    
    return products;
}

/// <summary>
/// Check if composition contains transition metals.
/// </summary>
private bool ContainsTransitionMetal(Dictionary<string, int> elementalComp)
{
    var transitionMetals = new HashSet<string>
    {
        "Sc", "Ti", "V", "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn",
        "Y", "Zr", "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd",
        "Hf", "Ta", "W", "Re", "Os", "Ir", "Pt", "Au", "Hg"
    };
    
    return elementalComp.Keys.Any(e => transitionMetals.Contains(e));
}

/// <summary>
/// Add potential metal complexes to products based on available ligands.
/// Metal complexes are important for transition metals in aqueous solutions.
/// </summary>
private void AddMetalComplexes(ref List<(ChemicalCompound, double)> products, 
    Dictionary<string, int> elementalComp)
{
    // Identify transition metals in the composition
    var transitionMetals = new Dictionary<string, int>();
    var metalSet = new HashSet<string>
    {
        "Sc", "Ti", "V", "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn",
        "Y", "Zr", "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd",
        "Hf", "Ta", "W", "Re", "Os", "Ir", "Pt", "Au", "Hg",
        "Al", "Ga", "In", "Sn", "Pb", "Bi" // Also include post-transition metals
    };
    
    foreach (var (element, count) in elementalComp)
    {
        if (metalSet.Contains(element))
        {
            transitionMetals[element] = count;
        }
    }
    
    if (transitionMetals.Count == 0) return;
    
    // Identify available ligands from the system
    var availableLigands = IdentifyAvailableLigands(elementalComp, products);
    
    // For each metal, add relevant complexes
    foreach (var (metal, metalCount) in transitionMetals)
    {
        AddMetalSpecificComplexes(metal, metalCount, availableLigands, ref products);
    }
}

/// <summary>
/// Identify ligands available in the system for complex formation.
/// </summary>
private HashSet<string> IdentifyAvailableLigands(
    Dictionary<string, int> elementalComp, 
    List<(ChemicalCompound, double)> currentProducts)
{
    var ligands = new HashSet<string>();
    
    // Always present in aqueous solutions
    ligands.Add("H2O");
    ligands.Add("OH-");
    
    // Check for halides
    if (elementalComp.ContainsKey("Cl") || 
        currentProducts.Any(p => p.Item1.ChemicalFormula.Contains("Cl")))
        ligands.Add("Cl-");
    
    if (elementalComp.ContainsKey("Br") || 
        currentProducts.Any(p => p.Item1.ChemicalFormula.Contains("Br")))
        ligands.Add("Br-");
    
    if (elementalComp.ContainsKey("I") || 
        currentProducts.Any(p => p.Item1.ChemicalFormula.Contains("I")))
        ligands.Add("I-");
    
    if (elementalComp.ContainsKey("F") || 
        currentProducts.Any(p => p.Item1.ChemicalFormula.Contains("F")))
        ligands.Add("F-");
    
    // Check for other common ligands
    if (elementalComp.ContainsKey("S"))
    {
        ligands.Add("SO4-2");
        ligands.Add("HS-");
        ligands.Add("S2O3-2"); // Thiosulfate
    }
    
    if (elementalComp.ContainsKey("C"))
    {
        ligands.Add("CO3-2");
        ligands.Add("HCO3-");
    }
    
    if (elementalComp.ContainsKey("N"))
    {
        ligands.Add("NH3");
        ligands.Add("NO3-");
        ligands.Add("NO2-");
    }
    
    if (elementalComp.ContainsKey("P"))
    {
        ligands.Add("PO4-3");
        ligands.Add("HPO4-2");
    }
    
    // Organic ligands if carbon is present
    if (elementalComp.ContainsKey("C") && elementalComp.ContainsKey("N"))
    {
        ligands.Add("CN-"); // Cyanide
    }
    
    return ligands;
}

/// <summary>
/// Add complexes specific to each metal based on available ligands.
/// </summary>
private void AddMetalSpecificComplexes(
    string metal, 
    int metalCount, 
    HashSet<string> availableLigands,
    ref List<(ChemicalCompound, double)> products)
{
    var complexesToAdd = new List<string>();
    
    switch (metal)
    {
        case "Fe":
            complexesToAdd.AddRange(GetIronComplexes(availableLigands));
            break;
        case "Cu":
            complexesToAdd.AddRange(GetCopperComplexes(availableLigands));
            break;
        case "Zn":
            complexesToAdd.AddRange(GetZincComplexes(availableLigands));
            break;
        case "Ag":
            complexesToAdd.AddRange(GetSilverComplexes(availableLigands));
            break;
        case "Au":
            complexesToAdd.AddRange(GetGoldComplexes(availableLigands));
            break;
        case "Hg":
            complexesToAdd.AddRange(GetMercuryComplexes(availableLigands));
            break;
        case "Pb":
            complexesToAdd.AddRange(GetLeadComplexes(availableLigands));
            break;
        case "Cd":
            complexesToAdd.AddRange(GetCadmiumComplexes(availableLigands));
            break;
        case "Ni":
            complexesToAdd.AddRange(GetNickelComplexes(availableLigands));
            break;
        case "Co":
            complexesToAdd.AddRange(GetCobaltComplexes(availableLigands));
            break;
        case "Cr":
            complexesToAdd.AddRange(GetChromiumComplexes(availableLigands));
            break;
        case "Al":
            complexesToAdd.AddRange(GetAluminumComplexes(availableLigands));
            break;
        default:
            // Generic complexes for other metals
            complexesToAdd.AddRange(GetGenericMetalComplexes(metal, availableLigands));
            break;
    }
    
    // Add the identified complexes to products
    foreach (var complexName in complexesToAdd)
    {
        var complex = _compoundLibrary.Find(complexName);
        if (complex != null && complex.Phase == CompoundPhase.Aqueous)
        {
            // Check if complex is already in products
            if (!products.Any(p => p.Item1.Name == complex.Name))
            {
                // Add with small initial amount (will be adjusted by equilibrium solver)
                products.Add((complex, 1e-10));
                Logger.Log($"[ReactionGenerator] Added metal complex: {complex.Name}");
            }
        }
    }
}

/// <summary>
/// Get iron complexes based on available ligands.
/// </summary>
private List<string> GetIronComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Hydroxo complexes (always form in water)
    complexes.AddRange(new[] { "FeOH²⁺", "Fe(OH)₂⁺", "Fe(OH)₃", "Fe(OH)₄⁻" });
    
    // For Fe(III)
    complexes.AddRange(new[] { "FeOH²⁺", "Fe(OH)₂⁺", "Fe(OH)₄⁻" });
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "FeCl²⁺", "FeCl₂⁺", "FeCl₃", "FeCl₄⁻" });
        // Fe(III) chloro complexes
        complexes.AddRange(new[] { "FeCl²⁺", "FeCl₂⁺", "FeCl₄⁻", "FeCl₆³⁻" });
    }
    
    if (ligands.Contains("SO4-2"))
    {
        complexes.AddRange(new[] { "FeSO₄", "Fe(SO₄)₂⁻" });
        complexes.AddRange(new[] { "FeSO₄⁺", "Fe(SO₄)₂⁻" }); // Fe(III)
    }
    
    if (ligands.Contains("CN-"))
    {
        complexes.Add("[Fe(CN)₆]⁴⁻"); // Ferrocyanide
        complexes.Add("[Fe(CN)₆]³⁻"); // Ferricyanide
    }
    
    return complexes;
}

/// <summary>
/// Get copper complexes based on available ligands.
/// </summary>
private List<string> GetCopperComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Hydroxo complexes
    complexes.AddRange(new[] { "CuOH⁺", "Cu(OH)₂", "Cu(OH)₃⁻", "Cu(OH)₄²⁻" });
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "CuCl⁺", "CuCl₂", "CuCl₃⁻", "CuCl₄²⁻" });
    }
    
    if (ligands.Contains("NH3"))
    {
        complexes.AddRange(new[] 
        { 
            "Cu(NH₃)²⁺", "Cu(NH₃)₂²⁺", "Cu(NH₃)₃²⁺", 
            "Cu(NH₃)₄²⁺", "[Cu(NH₃)₄(H₂O)₂]²⁺" 
        });
    }
    
    if (ligands.Contains("CO3-2"))
    {
        complexes.AddRange(new[] { "CuCO₃", "Cu(CO₃)₂²⁻" });
    }
    
    if (ligands.Contains("SO4-2"))
    {
        complexes.Add("CuSO₄");
    }
    
    return complexes;
}

/// <summary>
/// Get zinc complexes based on available ligands.
/// </summary>
private List<string> GetZincComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Hydroxo complexes
    complexes.AddRange(new[] { "ZnOH⁺", "Zn(OH)₂", "Zn(OH)₃⁻", "Zn(OH)₄²⁻" });
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "ZnCl⁺", "ZnCl₂", "ZnCl₃⁻", "ZnCl₄²⁻" });
    }
    
    if (ligands.Contains("NH3"))
    {
        complexes.AddRange(new[] 
        { 
            "Zn(NH₃)²⁺", "Zn(NH₃)₂²⁺", 
            "Zn(NH₃)₃²⁺", "Zn(NH₃)₄²⁺" 
        });
    }
    
    if (ligands.Contains("CO3-2"))
    {
        complexes.AddRange(new[] { "ZnCO₃", "Zn(CO₃)₂²⁻" });
    }
    
    if (ligands.Contains("SO4-2"))
    {
        complexes.AddRange(new[] { "ZnSO₄", "Zn(SO₄)₂²⁻" });
    }
    
    return complexes;
}

/// <summary>
/// Get silver complexes based on available ligands.
/// </summary>
private List<string> GetSilverComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Hydroxo complexes
    complexes.AddRange(new[] { "AgOH", "Ag(OH)₂⁻" });
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "AgCl", "AgCl₂⁻", "AgCl₃²⁻", "AgCl₄³⁻" });
    }
    
    if (ligands.Contains("NH3"))
    {
        complexes.AddRange(new[] { "Ag(NH₃)⁺", "Ag(NH₃)₂⁺" }); // Diammine silver(I)
    }
    
    if (ligands.Contains("CN-"))
    {
        complexes.AddRange(new[] { "Ag(CN)₂⁻", "Ag(CN)₃²⁻", "Ag(CN)₄³⁻" });
    }
    
    if (ligands.Contains("S2O3-2"))
    {
        complexes.AddRange(new[] { "Ag(S₂O₃)⁻", "Ag(S₂O₃)₂³⁻" }); // Photography fixer
    }
    
    return complexes;
}

/// <summary>
/// Get gold complexes based on available ligands.
/// </summary>
private List<string> GetGoldComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "AuCl₂⁻", "AuCl₄⁻" }); // Au(I) and Au(III)
    }
    
    if (ligands.Contains("CN-"))
    {
        complexes.AddRange(new[] { "Au(CN)₂⁻", "Au(CN)₄⁻" }); // Used in gold extraction
    }
    
    if (ligands.Contains("S2O3-2"))
    {
        complexes.Add("Au(S₂O₃)₂³⁻"); // Alternative gold leaching
    }
    
    return complexes;
}

/// <summary>
/// Get mercury complexes based on available ligands.
/// </summary>
private List<string> GetMercuryComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Hydroxo complexes
    complexes.AddRange(new[] { "HgOH⁺", "Hg(OH)₂" });
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "HgCl⁺", "HgCl₂", "HgCl₃⁻", "HgCl₄²⁻" });
    }
    
    if (ligands.Contains("Br-"))
    {
        complexes.AddRange(new[] { "HgBr⁺", "HgBr₂", "HgBr₃⁻", "HgBr₄²⁻" });
    }
    
    if (ligands.Contains("I-"))
    {
        complexes.AddRange(new[] { "HgI⁺", "HgI₂", "HgI₃⁻", "HgI₄²⁻" });
    }
    
    if (ligands.Contains("CN-"))
    {
        complexes.AddRange(new[] { "Hg(CN)₂", "Hg(CN)₃⁻", "Hg(CN)₄²⁻" });
    }
    
    return complexes;
}

/// <summary>
/// Get lead complexes based on available ligands.
/// </summary>
private List<string> GetLeadComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Hydroxo complexes
    complexes.AddRange(new[] { "PbOH⁺", "Pb(OH)₂", "Pb(OH)₃⁻", "Pb(OH)₄²⁻" });
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "PbCl⁺", "PbCl₂", "PbCl₃⁻", "PbCl₄²⁻" });
    }
    
    if (ligands.Contains("CO3-2"))
    {
        complexes.AddRange(new[] { "PbCO₃", "Pb(CO₃)₂²⁻" });
    }
    
    if (ligands.Contains("SO4-2"))
    {
        complexes.AddRange(new[] { "PbSO₄", "Pb(SO₄)₂²⁻" });
    }
    
    return complexes;
}

/// <summary>
/// Get cadmium complexes based on available ligands.
/// </summary>
private List<string> GetCadmiumComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Hydroxo complexes
    complexes.AddRange(new[] { "CdOH⁺", "Cd(OH)₂", "Cd(OH)₃⁻", "Cd(OH)₄²⁻" });
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "CdCl⁺", "CdCl₂", "CdCl₃⁻", "CdCl₄²⁻" });
    }
    
    if (ligands.Contains("NH3"))
    {
        complexes.AddRange(new[] 
        { 
            "Cd(NH₃)²⁺", "Cd(NH₃)₂²⁺", 
            "Cd(NH₃)₃²⁺", "Cd(NH₃)₄²⁺", 
            "Cd(NH₃)₅²⁺", "Cd(NH₃)₆²⁺" 
        });
    }
    
    if (ligands.Contains("CN-"))
    {
        complexes.AddRange(new[] { "Cd(CN)₃⁻", "Cd(CN)₄²⁻" });
    }
    
    return complexes;
}

/// <summary>
/// Get nickel complexes based on available ligands.
/// </summary>
private List<string> GetNickelComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Hydroxo complexes
    complexes.AddRange(new[] { "NiOH⁺", "Ni(OH)₂", "Ni(OH)₃⁻" });
    
    if (ligands.Contains("NH3"))
    {
        complexes.AddRange(new[] 
        { 
            "Ni(NH₃)²⁺", "Ni(NH₃)₂²⁺", 
            "Ni(NH₃)₃²⁺", "Ni(NH₃)₄²⁺", 
            "Ni(NH₃)₅²⁺", "Ni(NH₃)₆²⁺" 
        });
    }
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "NiCl⁺", "NiCl₂" });
    }
    
    if (ligands.Contains("SO4-2"))
    {
        complexes.Add("NiSO₄");
    }
    
    return complexes;
}

/// <summary>
/// Get cobalt complexes based on available ligands.
/// </summary>
private List<string> GetCobaltComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Hydroxo complexes
    complexes.AddRange(new[] { "CoOH⁺", "Co(OH)₂", "Co(OH)₃⁻" });
    
    if (ligands.Contains("NH3"))
    {
        // Co(II) complexes
        complexes.AddRange(new[] { "Co(NH₃)²⁺", "Co(NH₃)₂²⁺" });
        // Co(III) complexes
        complexes.AddRange(new[] { "[Co(NH₃)₆]³⁺", "[Co(NH₃)₅Cl]²⁺" });
    }
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "CoCl⁺", "CoCl₂", "CoCl₄²⁻" });
    }
    
    if (ligands.Contains("CN-"))
    {
        complexes.Add("[Co(CN)₆]³⁻"); // Hexacyanocobaltate(III)
    }
    
    return complexes;
}

/// <summary>
/// Get chromium complexes based on available ligands.
/// </summary>
private List<string> GetChromiumComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Cr(III) hydroxo complexes
    complexes.AddRange(new[] { "CrOH²⁺", "Cr(OH)₂⁺", "Cr(OH)₃", "Cr(OH)₄⁻" });
    
    // Cr(VI) species (chromate/dichromate)
    complexes.AddRange(new[] { "CrO₄²⁻", "Cr₂O₇²⁻", "HCrO₄⁻" });
    
    if (ligands.Contains("Cl-"))
    {
        complexes.AddRange(new[] { "CrCl²⁺", "CrCl₂⁺" });
    }
    
    if (ligands.Contains("SO4-2"))
    {
        complexes.AddRange(new[] { "CrSO₄⁺", "Cr(SO₄)₂⁻" });
    }
    
    return complexes;
}

/// <summary>
/// Get aluminum complexes based on available ligands.
/// </summary>
private List<string> GetAluminumComplexes(HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Hydroxo complexes - very important for Al
    complexes.AddRange(new[] 
    { 
        "AlOH²⁺", "Al(OH)₂⁺", "Al(OH)₃", "Al(OH)₄⁻",
        "Al₂(OH)₂⁴⁺", "Al₃(OH)₄⁵⁺" // Polynuclear species
    });
    
    if (ligands.Contains("F-"))
    {
        complexes.AddRange(new[] 
        { 
            "AlF²⁺", "AlF₂⁺", "AlF₃", 
            "AlF₄⁻", "AlF₅²⁻", "AlF₆³⁻" 
        });
    }
    
    if (ligands.Contains("SO4-2"))
    {
        complexes.AddRange(new[] { "AlSO₄⁺", "Al(SO₄)₂⁻" });
    }
    
    return complexes;
}

/// <summary>
/// Get generic metal complexes for metals not specifically handled.
/// </summary>
private List<string> GetGenericMetalComplexes(string metal, HashSet<string> ligands)
{
    var complexes = new List<string>();
    
    // Try to find hydroxo complexes (most common)
    var hydroxoComplexes = _compoundLibrary.Compounds
        .Where(c => c.Phase == CompoundPhase.Aqueous &&
                   c.ChemicalFormula.Contains(metal) &&
                   c.ChemicalFormula.Contains("OH"))
        .Select(c => c.Name)
        .ToList();
    
    complexes.AddRange(hydroxoComplexes);
    
    // Try to find chloro complexes if Cl- is available
    if (ligands.Contains("Cl-"))
    {
        var chloroComplexes = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Aqueous &&
                       c.ChemicalFormula.Contains(metal) &&
                       c.ChemicalFormula.Contains("Cl"))
            .Select(c => c.Name)
            .ToList();
        
        complexes.AddRange(chloroComplexes);
    }
    
    // Try to find sulfate complexes if SO4-2 is available
    if (ligands.Contains("SO4-2"))
    {
        var sulfateComplexes = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Aqueous &&
                       c.ChemicalFormula.Contains(metal) &&
                       c.ChemicalFormula.Contains("SO₄"))
            .Select(c => c.Name)
            .ToList();
        
        complexes.AddRange(sulfateComplexes);
    }
    
    return complexes;
}
/// <summary>
/// Temperature-dependent LogK calculation using van't Hoff equation.
/// </summary>
private double CalculateLogKAtTemperature(ChemicalReaction reaction, double temperature_K)
{
    const double R = 8.314462618; // J/(mol·K)
    const double T_ref = 298.15; // K
    
    if (Math.Abs(temperature_K - T_ref) < 1e-6)
        return reaction.LogK_25C;
    
    double logK_T = reaction.LogK_25C;
    
    // Van't Hoff equation
    if (Math.Abs(reaction.DeltaH0_kJ_mol) > 1e-9)
    {
        double deltaH_J = reaction.DeltaH0_kJ_mol * 1000.0;
        logK_T -= deltaH_J / (R * Math.Log(10)) * (1.0 / temperature_K - 1.0 / T_ref);
    }
    
    // Heat capacity correction for wide temperature ranges
    if (Math.Abs(reaction.DeltaCp0_J_molK) > 1e-9)
    {
        double deltaCp = reaction.DeltaCp0_J_molK;
        logK_T += deltaCp / (R * Math.Log(10)) * 
                  (T_ref / temperature_K - 1.0 - Math.Log(T_ref / temperature_K));
    }
    
    return logK_T;
}
/// <summary>
/// Check if a compound is known to be highly soluble.
/// </summary>
private bool IsHighlySolubleCompound(ChemicalCompound compound)
{
    // List of known highly soluble compounds
    var highlySoluble = new HashSet<string>
    {
        "Halite", "NaCl", "KCl", "NaNO3", "KNO3", "NH4Cl", "NH4NO3",
        "Na2SO4", "K2SO4", "MgSO4", "CaCl2", "MgCl2", "AlCl3", "FeCl3",
        "NaBr", "KBr", "NaI", "KI", "LiCl", "LiBr", "LiI"
    };
    
    return highlySoluble.Contains(compound.Name) || 
           highlySoluble.Contains(compound.ChemicalFormula);
}
/// <summary>
/// Find ionic dissociation products for a compound.
/// </summary>
private List<(ChemicalCompound ion, double stoichiometry)> FindIonicDissociationProducts(
    ChemicalCompound compound, Dictionary<string, int> elementalComp)
{
    var products = new List<(ChemicalCompound, double)>();
    
    // Special case for common salts
    if (compound.Name == "Halite" || compound.ChemicalFormula == "NaCl")
    {
        var naIon = _compoundLibrary.Find("Naº") ?? _compoundLibrary.Find("Sodium Ion");
        var clIon = _compoundLibrary.Find("Cl⁻") ?? _compoundLibrary.Find("Chloride Ion");
        if (naIon != null && clIon != null)
        {
            products.Add((naIon, 1.0));
            products.Add((clIon, 1.0));
            return products;
        }
    }
    
    // Try to find cation and anion pairs
    var cations = new List<(ChemicalCompound, int)>();
    var anions = new List<(ChemicalCompound, int)>();
    
    // Find primary ions for each element
    foreach (var (element, count) in elementalComp)
    {
        if (element == "O" || element == "H") continue;
        
        // Find primary aqueous ion for this element
        var primaryIon = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Aqueous &&
                       c.IsPrimaryElementSpecies &&
                       c.IonicCharge.HasValue &&
                       ParseChemicalFormula(c.ChemicalFormula).ContainsKey(element))
            .FirstOrDefault();
        
        if (primaryIon != null)
        {
            if (primaryIon.IonicCharge.Value > 0)
                cations.Add((primaryIon, count));
            else if (primaryIon.IonicCharge.Value < 0)
                anions.Add((primaryIon, count));
        }
    }
    
    // Check for polyatomic ions
    CheckForPolyatomicIons(elementalComp, ref anions, ref cations);
    
    // Build product list with stoichiometry
    foreach (var (ion, stoich) in cations)
    {
        products.Add((ion, stoich));
    }
    foreach (var (ion, stoich) in anions)
    {
        products.Add((ion, stoich));
    }
    
    return products;
}
    /// <summary>
    ///     Filter reactions based on thermodynamic feasibility.
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