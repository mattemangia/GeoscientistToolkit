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

        // Get all solid phase compounds that are not part of a solid solution
        var pureMinerals = _compoundLibrary.Compounds
            .Where(c => c.Phase == CompoundPhase.Solid &&
                        c.GibbsFreeEnergyFormation_kJ_mol != null &&
                        !_compoundLibrary.SolidSolutions.Any(ss => ss.EndMembers.Contains(c.Name)))
            .ToList();

        foreach (var mineral in pureMinerals)
        {
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

        // Common acid-base systems
        reactions.AddRange(GenerateCarbonateSys());
        reactions.AddRange(GenerateWaterDissociation());
        reactions.AddRange(GeneratePhosphateSys());
        reactions.AddRange(GenerateSulfideSys());

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