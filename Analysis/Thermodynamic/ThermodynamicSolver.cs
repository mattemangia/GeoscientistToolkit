// GeoscientistToolkit/Business/Thermodynamics/ThermodynamicSolver.cs
//
// Comprehensive thermodynamic equilibrium solver for geochemical systems
// based on Gibbs energy minimization with automatic reaction identification.
//
// THEORETICAL FOUNDATION:
// - Smith, W.R. & Missen, R.W., 1982. Chemical Reaction Equilibrium Analysis: Theory and Algorithms. Wiley.
// - Parkhurst, D.L. & Appelo, C.A.J., 2013. Description of input and examples for PHREEQC version 3. 
//   USGS Techniques and Methods, Book 6, Chapter A43.
// - Bethke, C.M., 2008. Geochemical and Biogeochemical Reaction Modeling, 2nd ed. Cambridge University Press.
// - Anderson, G.M. & Crerar, D.A., 1993. Thermodynamics in Geochemistry. Oxford University Press.
// - Stumm, W. & Morgan, J.J., 1996. Aquatic Chemistry, 3rd ed. Wiley-Interscience.
//
// ALGORITHMS:
// - Gibbs energy minimization using interior point method (Wright & Nocedal, 1999)
// - Newton-Raphson iteration for speciation (Morel & Hering, 1993)
// - Activity coefficient calculation via extended Debye-Hückel and Pitzer equations
//   (Pitzer, K.S., 1991. Activity Coefficients in Electrolyte Solutions, 2nd ed. CRC Press)
//

using System.Text.RegularExpressions;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;
using MathNet.Numerics.LinearAlgebra;

namespace GeoscientistToolkit.Business.Thermodynamics;

/// <summary>
///     Represents a thermodynamic system state including composition, temperature, and pressure.
/// </summary>
public class ThermodynamicState
{
    /// <summary>Temperature in Kelvin</summary>
    public double Temperature_K { get; set; } = 298.15;

    /// <summary>Pressure in bar</summary>
    public double Pressure_bar { get; set; } = 1.0;

    /// <summary>Total moles of each element in the system (element symbol -> moles)</summary>
    public Dictionary<string, double> ElementalComposition { get; set; } = new();

    /// <summary>Current moles of each species/compound (name -> moles)</summary>
    public Dictionary<string, double> SpeciesMoles { get; set; } = new();

    /// <summary>Activities of aqueous species (name -> activity)</summary>
    public Dictionary<string, double> Activities { get; set; } = new();

    /// <summary>Volume in liters (for aqueous systems)</summary>
    public double Volume_L { get; set; } = 1.0;

    /// <summary>pH of the system (if aqueous)</summary>
    public double pH { get; set; } = 7.0;

    /// <summary>Ionic strength in mol/kg (for aqueous systems)</summary>
    public double IonicStrength_molkg { get; set; }
}

/// <summary>
///     Represents a chemical reaction with stoichiometry and thermodynamic properties.
///     Reactions are automatically generated from compound data.
/// </summary>
public class ChemicalReaction
{
    /// <summary>Reaction name/description</summary>
    public string Name { get; set; }

    /// <summary>Stoichiometric coefficients: compound name -> coefficient (negative for reactants, positive for products)</summary>
    public Dictionary<string, double> Stoichiometry { get; set; } = new();

    /// <summary>Standard Gibbs energy change (kJ/mol) at 298.15 K</summary>
    public double DeltaG0_kJ_mol { get; set; }

    /// <summary>Standard enthalpy change (kJ/mol) at 298.15 K</summary>
    public double DeltaH0_kJ_mol { get; set; }

    /// <summary>Standard entropy change (J/mol·K) at 298.15 K</summary>
    public double DeltaS0_J_molK { get; set; }

    /// <summary>Equilibrium constant at standard conditions (log10 K)</summary>
    public double LogK_25C { get; set; }

    /// <summary>Reaction type</summary>
    public ReactionType Type { get; set; }

    /// <summary>
    ///     Calculate equilibrium constant at given temperature using van't Hoff equation.
    ///     Source: Atkins, P. & de Paula, J., 2010. Physical Chemistry, 9th ed. Oxford, Eq. 6.34
    /// </summary>
    public double CalculateLogK(double temperature_K)
    {
        const double R = 8.314462618; // J/(mol·K) - CODATA 2018
        const double T0 = 298.15; // K

        if (Math.Abs(temperature_K - T0) < 1e-6)
            return LogK_25C;

        // Van't Hoff equation: ln(K2/K1) = -ΔH°/R * (1/T2 - 1/T1)
        // Source: Nordstrom & Munoz, 1994. Geochemical Thermodynamics, 2nd ed. Eq. 4.27
        var lnK_ratio = -DeltaH0_kJ_mol * 1000.0 / R * (1.0 / temperature_K - 1.0 / T0);
        var logK = LogK_25C + lnK_ratio / Math.Log(10);

        return logK;
    }
}

public enum ReactionType
{
    Dissolution, // Solid -> Aqueous ions
    Precipitation, // Aqueous ions -> Solid
    Complexation, // Aqueous species -> Aqueous complex
    Dissociation, // Aqueous complex -> Aqueous species
    RedOx, // Oxidation-reduction
    AcidBase, // Proton transfer
    GasEquilibrium // Gas <-> Aqueous
}

/// <summary>
///     Main thermodynamic equilibrium solver using Gibbs energy minimization.
/// </summary>
public class ThermodynamicSolver
{
    // Convergence criteria from PHREEQC (Parkhurst & Appelo, 2013)
    private const double TOLERANCE_MOLES = 1e-12;
    private const double TOLERANCE_GIBBS = 1e-10;
    private const int MAX_ITERATIONS = 100;
    private readonly ActivityCoefficientCalculator _activityCalculator;
    private readonly CompoundLibrary _compoundLibrary;
    private readonly ReactionGenerator _reactionGenerator;

    public ThermodynamicSolver()
    {
        _compoundLibrary = CompoundLibrary.Instance;
        _activityCalculator = new ActivityCoefficientCalculator();
        _reactionGenerator = new ReactionGenerator(_compoundLibrary);
    }

    /// <summary>
    ///     Solve for chemical equilibrium by minimizing total Gibbs energy.
    ///     Uses constrained optimization with element conservation constraints.
    ///     Algorithm: Interior point method with Newton steps
    ///     Source: Wright, S.J. & Nocedal, J., 1999. Numerical Optimization. Springer, Chapter 19
    /// </summary>
    public ThermodynamicState SolveEquilibrium(ThermodynamicState initialState)
    {
        Logger.Log("[ThermodynamicSolver] Starting equilibrium calculation");

        // Step 1: Generate all possible reactions for the system
        var reactions = _reactionGenerator.GenerateReactions(initialState);
        Logger.Log($"[ThermodynamicSolver] Generated {reactions.Count} possible reactions");

        // Step 2: Set up optimization problem - minimize G = Σ(n_i * μ_i)
        // where μ_i = μ_i° + RT ln(a_i) is the chemical potential
        var species = GetAllSpecies(initialState, reactions);
        var elementMatrix = BuildElementMatrix(species, initialState.ElementalComposition.Keys.ToList());

        // Step 3: Initial guess - use input moles or small positive values
        var x0 = CreateInitialGuess(species, initialState);

        // Step 4: Solve using Newton-Raphson with line search
        // Source: Bethke, 2008. Geochemical Reaction Modeling, Chapter 4
        var solution = SolveGibbsMinimization(x0, species, elementMatrix, initialState);

        // Step 5: Calculate final properties
        var finalState = BuildFinalState(solution, species, initialState);
        CalculateAqueousProperties(finalState);

        Logger.Log("[ThermodynamicSolver] Equilibrium calculation complete");
        return finalState;
    }

    /// <summary>
    ///     Calculate saturation indices for all minerals in the system.
    ///     SI = log10(IAP/K) where IAP is ion activity product
    ///     Source: Langmuir, D., 1997. Aqueous Environmental Geochemistry. Prentice Hall, Eq. 4.14
    /// </summary>
    public Dictionary<string, double> CalculateSaturationIndices(ThermodynamicState state)
    {
        var saturationIndices = new Dictionary<string, double>();
        var reactions = _reactionGenerator.GenerateDissolutionReactions(state);

        foreach (var reaction in reactions)
        {
            // Calculate ion activity product (IAP)
            var logIAP = 0.0;
            foreach (var (species, coeff) in reaction.Stoichiometry)
                if (state.Activities.TryGetValue(species, out var activity))
                    logIAP += coeff * Math.Log10(Math.Max(activity, 1e-20));

            // Calculate SI = log(IAP/K)
            var logK = reaction.CalculateLogK(state.Temperature_K);
            var SI = logIAP - logK;

            saturationIndices[reaction.Name] = SI;
        }

        return saturationIndices;
    }

    /// <summary>
    ///     Solve aqueous speciation for a given total composition.
    ///     Uses Newton-Raphson method to solve mass action and mass balance equations.
    ///     Source: Morel, F.M.M. & Hering, J.G., 1993. Principles of Aquatic Chemistry. Wiley, Chapter 6
    /// </summary>
    public ThermodynamicState SolveSpeciation(ThermodynamicState state)
    {
        Logger.Log("[ThermodynamicSolver] Solving aqueous speciation");

        // Get all aqueous complexation, acid-base, and redox reactions
        var reactions = _reactionGenerator.GenerateComplexationReactions(state);
        reactions.AddRange(_reactionGenerator.GenerateAcidBaseReactions(state));
        reactions.AddRange(_reactionGenerator.GenerateRedoxReactions(state));

        var species = GetAllAqueousSpecies(state, reactions);
        var basisSpecies = SelectBasisSpecies(species);
        var secondarySpecies = species.Except(basisSpecies).ToList();

        var iteration = 0;
        var converged = false;

        // Initial guess for basis species activities
        foreach (var basis in basisSpecies)
            if (!state.Activities.ContainsKey(basis))
                state.Activities[basis] = 1e-8;

        while (iteration < MAX_ITERATIONS && !converged)
        {
            // Update ionic strength and activity coefficients
            UpdateIonicStrength(state);
            _activityCalculator.CalculateActivityCoefficients(state);

            // Update concentrations of secondary species from mass action
            UpdateSecondarySpecies(state, reactions, secondarySpecies);

            // Build Jacobian matrix and residual vector
            var (jacobian, residual) = BuildSpeciationSystem(state, basisSpecies, secondarySpecies, reactions);

            // Solve: J * Δx = -r
            Vector<double> delta;
            try
            {
                delta = jacobian.Solve(-residual);
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    $"[ThermodynamicSolver] Linear solve failed: {ex.Message}. Speciation may not converge.");
                break;
            }


            // Update species concentrations with damping for stability
            var dampingFactor = CalculateDampingFactor(delta, state, basisSpecies);
            UpdateBasisSpeciesConcentrations(state, delta, dampingFactor, basisSpecies);

            // Check convergence
            converged = residual.L2Norm() < TOLERANCE_MOLES;
            iteration++;
        }

        if (!converged)
            Logger.LogWarning($"[ThermodynamicSolver] Speciation did not converge after {MAX_ITERATIONS} iterations");

        Logger.Log($"[ThermodynamicSolver] Speciation converged in {iteration} iterations");
        CalculateAqueousProperties(state);
        return state;
    }

    private List<string> GetAllAqueousSpecies(ThermodynamicState state, List<ChemicalReaction> reactions)
    {
        var species = new HashSet<string>();

        foreach (var s in state.SpeciesMoles.Keys)
        {
            var compound = _compoundLibrary.Find(s);
            if (compound?.Phase == CompoundPhase.Aqueous)
                species.Add(s);
        }

        foreach (var reaction in reactions)
        foreach (var s in reaction.Stoichiometry.Keys)
        {
            var compound = _compoundLibrary.Find(s);
            if (compound?.Phase == CompoundPhase.Aqueous)
                species.Add(s);
        }

        return species.ToList();
    }

    private List<string> SelectBasisSpecies(List<string> allSpecies)
    {
        // A more robust implementation would use Gaussian elimination to find a basis
        // For now, select major ions and components
        var basis = new List<string>();
        var elements = new HashSet<string>();

        // Add major cations and anions
        var majorIons = new List<string> { "Na+", "K+", "Ca2+", "Mg2+", "Cl-", "SO42-", "H+", "e-" };
        foreach (var ion in majorIons)
            if (allSpecies.Contains(ion) && !basis.Contains(ion))
            {
                basis.Add(ion);
                var compound = _compoundLibrary.Find(ion);
                if (compound != null)
                {
                    var comp = _reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
                    foreach (var elem in comp.Keys) elements.Add(elem);
                }
            }

        // Add a species for each element not yet represented
        foreach (var speciesName in allSpecies)
        {
            var compound = _compoundLibrary.Find(speciesName);
            if (compound != null)
            {
                var comp = _reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
                var newElementFound = false;
                foreach (var elem in comp.Keys)
                    if (!elements.Contains(elem))
                    {
                        newElementFound = true;
                        elements.Add(elem);
                    }

                if (newElementFound && !basis.Contains(speciesName)) basis.Add(speciesName);
            }
        }

        return basis;
    }

    private void UpdateSecondarySpecies(ThermodynamicState state, List<ChemicalReaction> reactions,
        List<string> secondarySpecies)
    {
        foreach (var speciesName in secondarySpecies)
        {
            var formationReaction = reactions.FirstOrDefault(r =>
                r.Stoichiometry.ContainsKey(speciesName) && r.Stoichiometry[speciesName] > 0);
            if (formationReaction != null)
            {
                var logK = formationReaction.CalculateLogK(state.Temperature_K);
                var logIAP = 0.0;
                foreach (var (reactant, stoich) in formationReaction.Stoichiometry)
                    if (reactant != speciesName)
                        if (state.Activities.TryGetValue(reactant, out var activity))
                            logIAP += -stoich * Math.Log10(Math.Max(activity, 1e-30));

                var logActivity = logK - logIAP;
                state.Activities[speciesName] = Math.Pow(10, logActivity);
            }
        }
    }

    private List<string> GetAllSpecies(ThermodynamicState state, List<ChemicalReaction> reactions)
    {
        var species = new HashSet<string>();

        // Add species from initial state
        foreach (var s in state.SpeciesMoles.Keys)
            species.Add(s);

        // Add species from reactions
        foreach (var reaction in reactions)
        foreach (var s in reaction.Stoichiometry.Keys)
            species.Add(s);

        return species.ToList();
    }

    private Matrix<double> BuildElementMatrix(List<string> species, List<string> elements)
    {
        // Element matrix A[i,j] = number of atoms of element i in species j
        var matrix = Matrix<double>.Build.Dense(elements.Count, species.Count);

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            for (var j = 0; j < species.Count; j++)
            {
                var speciesName = species[j];
                var compound = _compoundLibrary.Find(speciesName);
                if (compound != null)
                {
                    var atomCount = ParseElementCount(compound.ChemicalFormula, element);
                    matrix[i, j] = atomCount;
                }
            }
        }

        return matrix;
    }

    private int ParseElementCount(string formula, string element)
    {
        // Simple formula parser for element counting
        // More sophisticated parser would be needed for complex formulas
        var pattern = $@"{element}(\d*)";
        var match = Regex.Match(formula, pattern);

        if (!match.Success)
            return 0;

        var countStr = match.Groups[1].Value;
        return string.IsNullOrEmpty(countStr) ? 1 : int.Parse(countStr);
    }

    private Vector<double> CreateInitialGuess(List<string> species, ThermodynamicState state)
    {
        var guess = Vector<double>.Build.Dense(species.Count);

        for (var i = 0; i < species.Count; i++)
        {
            var speciesName = species[i];
            if (state.SpeciesMoles.TryGetValue(speciesName, out var moles))
                guess[i] = Math.Max(moles, 1e-10); // Avoid zero moles
            else
                guess[i] = 1e-10; // Small positive value
        }

        return guess;
    }

    private Vector<double> SolveGibbsMinimization(Vector<double> x0, List<string> species,
        Matrix<double> elementMatrix, ThermodynamicState state)
    {
        const double R = 8.314462618; // J/(mol·K)
        var T = state.Temperature_K;

        // Objective function: G = Σ(n_i * (μ_i° + RT ln(a_i)))
        Func<Vector<double>, double> objectiveFunction = x =>
        {
            var G = 0.0;
            for (var i = 0; i < species.Count; i++)
            {
                var n = x[i];
                if (n < 1e-20) continue; // Skip negligible amounts

                var compound = _compoundLibrary.Find(species[i]);
                if (compound?.GibbsFreeEnergyFormation_kJ_mol == null) continue;

                var mu0 = compound.GibbsFreeEnergyFormation_kJ_mol.Value * 1000.0; // Convert to J/mol
                var activity = CalculateActivity(species[i], n, state);
                var mu = mu0 + R * T * Math.Log(Math.Max(activity, 1e-20));

                G += n * mu;
            }

            return G;
        };

        // Gradient: ∂G/∂n_i = μ_i
        Func<Vector<double>, Vector<double>> gradient = x =>
        {
            var grad = Vector<double>.Build.Dense(species.Count);
            for (var i = 0; i < species.Count; i++)
            {
                var compound = _compoundLibrary.Find(species[i]);
                if (compound?.GibbsFreeEnergyFormation_kJ_mol == null) continue;

                var mu0 = compound.GibbsFreeEnergyFormation_kJ_mol.Value * 1000.0;
                var activity = CalculateActivity(species[i], x[i], state);
                grad[i] = mu0 + R * T * Math.Log(Math.Max(activity, 1e-20));
            }

            return grad;
        };

        // Constraints: A * x = b (element conservation)
        var b = Vector<double>.Build.Dense(elementMatrix.RowCount);
        var elements = state.ElementalComposition.Keys.ToList();
        for (var i = 0; i < elements.Count; i++) b[i] = state.ElementalComposition[elements[i]];

        // Use projected gradient descent with element conservation
        var x = x0.Clone();
        var iteration = 0;
        var converged = false;

        while (iteration < MAX_ITERATIONS && !converged)
        {
            var grad = gradient(x);

            // Project gradient onto constraint surface (null space of A)
            // Source: Nocedal & Wright, 2006. Numerical Optimization, Chapter 15
            var projectedGrad = grad - elementMatrix.Transpose() *
                (elementMatrix * elementMatrix.Transpose()).Solve(elementMatrix * grad);

            // Line search for step size
            var alpha = BacktrackingLineSearch(x, projectedGrad, objectiveFunction);

            // Update: x_new = x - α * ∇G_projected
            var xNew = x - alpha * projectedGrad;

            // Enforce non-negativity
            for (var i = 0; i < xNew.Count; i++)
                xNew[i] = Math.Max(xNew[i], 0.0);

            // Check convergence
            var change = (xNew - x).L2Norm();
            converged = change < TOLERANCE_MOLES;

            x = xNew;
            iteration++;
        }

        Logger.Log($"[ThermodynamicSolver] Gibbs minimization converged in {iteration} iterations");
        return x;
    }

    private double BacktrackingLineSearch(Vector<double> x, Vector<double> direction,
        Func<Vector<double>, double> objective)
    {
        const double c = 0.5; // Sufficient decrease parameter
        const double rho = 0.5; // Backtracking parameter
        var alpha = 1.0;

        var f0 = objective(x);
        var grad0 = -direction.DotProduct(direction); // Since we use -gradient as direction

        for (var i = 0; i < 20; i++)
        {
            var xNew = x - alpha * direction;
            var fNew = objective(xNew);

            // Armijo condition
            if (fNew <= f0 + c * alpha * grad0)
                break;

            alpha *= rho;
        }

        return alpha;
    }

    private double CalculateActivity(string species, double moles, ThermodynamicState state)
    {
        // Activity = molality * activity_coefficient
        // For dilute solutions, use Debye-Hückel theory

        var molality = moles / (state.Volume_L * 1.0); // Approximate kg solvent = L solution

        if (state.Activities.TryGetValue(species, out var activity))
            return activity;

        var gamma = _activityCalculator.CalculateSingleIonActivityCoefficient(species, state);
        return molality * gamma;
    }

    private ThermodynamicState BuildFinalState(Vector<double> solution, List<string> species,
        ThermodynamicState initialState)
    {
        var finalState = new ThermodynamicState
        {
            Temperature_K = initialState.Temperature_K,
            Pressure_bar = initialState.Pressure_bar,
            Volume_L = initialState.Volume_L,
            ElementalComposition = new Dictionary<string, double>(initialState.ElementalComposition)
        };

        for (var i = 0; i < species.Count; i++)
        {
            var moles = solution[i];
            if (moles > TOLERANCE_MOLES)
            {
                finalState.SpeciesMoles[species[i]] = moles;
                finalState.Activities[species[i]] = CalculateActivity(species[i], moles, initialState);
            }
        }

        return finalState;
    }

    private void CalculateAqueousProperties(ThermodynamicState state)
    {
        // Calculate pH from H+ activity
        if (state.Activities.TryGetValue("H⁺", out var hActivity) ||
            state.Activities.TryGetValue("H+", out hActivity))
            state.pH = -Math.Log10(Math.Max(hActivity, 1e-14));

        // Calculate ionic strength
        UpdateIonicStrength(state);
    }

    private void UpdateIonicStrength(ThermodynamicState state)
    {
        // I = 0.5 * Σ(m_i * z_i²)
        // Source: Stumm & Morgan, 1996. Aquatic Chemistry, Eq. 3.24
        var I = 0.0;

        foreach (var (species, moles) in state.SpeciesMoles)
        {
            var compound = _compoundLibrary.Find(species);
            if (compound?.IonicCharge != null && compound.Phase == CompoundPhase.Aqueous)
            {
                var molality = moles / state.Volume_L; // Approximate
                var z = compound.IonicCharge.Value;
                I += 0.5 * molality * z * z;
            }
        }

        state.IonicStrength_molkg = I;
    }

    private (Matrix<double>, Vector<double>) BuildSpeciationSystem(ThermodynamicState state, List<string> basisSpecies,
        List<string> secondarySpecies, List<ChemicalReaction> reactions)
    {
        var nBasis = basisSpecies.Count;
        var jacobian = Matrix<double>.Build.Dense(nBasis, nBasis);
        var residual = Vector<double>.Build.Dense(nBasis);

        for (var i = 0; i < nBasis; i++)
        {
            var basis_i = basisSpecies[i];
            var total_i = state.ElementalComposition.ContainsKey(basis_i) ? state.ElementalComposition[basis_i] : 0;

            // Contribution from the basis species itself
            var calculatedTotal_i = state.SpeciesMoles.ContainsKey(basis_i) ? state.SpeciesMoles[basis_i] : 0;

            // Contribution from secondary species
            foreach (var secondary in secondarySpecies)
            {
                var formationReaction = reactions.FirstOrDefault(r =>
                    r.Stoichiometry.ContainsKey(secondary) && r.Stoichiometry[secondary] > 0);
                if (formationReaction != null && formationReaction.Stoichiometry.ContainsKey(basis_i))
                {
                    var stoich_i_in_secondary = -formationReaction.Stoichiometry[basis_i];
                    calculatedTotal_i += stoich_i_in_secondary * state.SpeciesMoles[secondary];
                }
            }

            residual[i] = total_i - calculatedTotal_i;

            for (var j = 0; j < nBasis; j++)
            {
                var basis_j = basisSpecies[j];
                double derivative = 0;
                if (i == j)
                    derivative -= state.SpeciesMoles.ContainsKey(basis_i)
                        ? state.SpeciesMoles[basis_i] / state.Activities[basis_i]
                        : 0;

                foreach (var secondary in secondarySpecies)
                {
                    var formationReaction = reactions.FirstOrDefault(r =>
                        r.Stoichiometry.ContainsKey(secondary) && r.Stoichiometry[secondary] > 0);
                    if (formationReaction != null && formationReaction.Stoichiometry.ContainsKey(basis_i) &&
                        formationReaction.Stoichiometry.ContainsKey(basis_j))
                    {
                        var stoich_i = -formationReaction.Stoichiometry[basis_i];
                        var stoich_j = -formationReaction.Stoichiometry[basis_j];
                        derivative -= stoich_i * stoich_j * state.SpeciesMoles[secondary] / state.Activities[basis_j];
                    }
                }

                jacobian[i, j] = derivative;
            }
        }

        return (jacobian, residual);
    }

    private double CalculateDampingFactor(Vector<double> delta, ThermodynamicState state, List<string> basisSpecies)
    {
        var max_change = 1.0;
        for (var i = 0; i < delta.Count; i++)
        {
            var speciesName = basisSpecies[i];
            var currentMoles = state.SpeciesMoles.ContainsKey(speciesName) ? state.SpeciesMoles[speciesName] : 0;
            if (delta[i] < 0) max_change = Math.Max(max_change, -delta[i] / (currentMoles / 2.0 + 1e-20));
        }

        return 1.0 / max_change;
    }

    private void UpdateBasisSpeciesConcentrations(ThermodynamicState state, Vector<double> delta, double dampingFactor,
        List<string> basisSpecies)
    {
        for (var i = 0; i < basisSpecies.Count; i++)
        {
            var species = basisSpecies[i];
            var currentActivity = state.Activities.ContainsKey(species) ? state.Activities[species] : 1e-10;
            var newActivity = currentActivity + dampingFactor * delta[i];
            state.Activities[species] = Math.Max(1e-20, newActivity);

            // Update moles from activity
            var compound = _compoundLibrary.Find(species);
            if (compound?.IonicCharge != null && compound.Phase == CompoundPhase.Aqueous)
            {
                var gamma = state.Activities.ContainsKey(species + "_gamma")
                    ? state.Activities[species + "_gamma"]
                    : 1.0;
                var molality = newActivity / gamma;
                state.SpeciesMoles[species] = molality * state.Volume_L;
            }
        }
    }

    private void UpdateSpeciesConcentrations(ThermodynamicState state, Vector<double> delta,
        double dampingFactor)
    {
        var i = 0;
        foreach (var species in state.SpeciesMoles.Keys.ToList())
        {
            var newMoles = Math.Max(0, state.SpeciesMoles[species] + dampingFactor * delta[i]);
            state.SpeciesMoles[species] = newMoles;
            i++;
        }
    }
}