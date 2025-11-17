// GeoscientistToolkit/Business/Thermodynamics/ThermodynamicSolver.cs
//
// Comprehensive thermodynamic equilibrium solver for geochemical systems
// based on Gibbs energy minimization with automatic reaction identification.
// ENHANCEMENT: Added support for solid solutions, surface complexation, redox, and gas phases.
//              Improved temperature correction for equilibrium constants.
//
// THEORETICAL FOUNDATION:
// - Smith, W.R. & Missen, R.W., 1982. Chemical Reaction Equilibrium Analysis: Theory and Algorithms. Wiley.
// - Parkhurst, D.L. & Appelo, C.A.J., 2013. Description of input and examples for PHREEQC version 3. 
//   USGS Techniques and Methods, Book 6, Chapter A43.
// - Bethke, C.M., 2008. Geochemical and Biogeochemical Reaction Modeling, 2nd ed. Cambridge University Press.
// - Glynn, P.D. & Reardon, E.J., 1990. Solid-solution aqueous-solution equilibria. Am. J. Sci., 290(2), 164-201.
// - Dzombak, D.A. & Morel, F.M.M., 1990. Surface Complexation Modeling: Hydrous Ferric Oxide. Wiley.

using System.Text.RegularExpressions;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Util;
using MathNet.Numerics.LinearAlgebra;

namespace GeoscientistToolkit.Business.Thermodynamics;

/// <summary>
///     ENHANCEMENT: Represents a surface site for complexation modeling.
/// </summary>
public class SurfaceSite
{
    public string MineralName { get; set; } = string.Empty;
    public double Mass_g { get; set; }
    public Dictionary<string, double> SpeciesMoles { get; set; } = new();
}

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

    /// <summary>pe of the system (redox potential)</summary>
    public double pe { get; set; } = 4.0;

    /// <summary>Ionic strength in mol/kg (for aqueous systems)</summary>
    public double IonicStrength_molkg { get; set; }

    // ENHANCEMENT: Properties for surface complexation
    public List<SurfaceSite> SurfaceSites { get; set; } = new();
    public double? SurfaceCharge_mol_L { get; set; }
    public double? SurfacePotential_V { get; set; }

    // ENHANCEMENT: Properties for gas phase
    public Dictionary<string, double> GasPartialPressures_atm { get; set; } = new();
    public double TotalGasPressure_atm { get; set; } = 1.0;
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

    /// <summary>Standard heat capacity change (J/mol·K) at 298.15K</summary>
    public double DeltaCp0_J_molK { get; set; }

    /// <summary>Equilibrium constant at standard conditions (log10 K)</summary>
    public double LogK_25C { get; set; }

    /// <summary>Reaction type</summary>
    public ReactionType Type { get; set; }

    /// <summary>
    ///     ENHANCEMENT: Calculate equilibrium constant at a given temperature using the integrated van't Hoff equation.
    ///     This form includes the effect of heat capacity and is more accurate over wide temperature ranges.
    ///     logK(T) = logK(Tr) - (ΔH°/R) * (1/T - 1/Tr) / ln(10) + (ΔCp°/R) * [(Tr/T - 1) - ln(Tr/T)] / ln(10)
    ///     Source: Anderson & Crerar, 1993. Thermodynamics in Geochemistry, Oxford University Press.
    /// </summary>
    public double CalculateLogK(double temperature_K)
    {
        const double R = 8.314462618; // J/(mol·K) - CODATA 2018
        const double T_ref = 298.15; // K

        if (Math.Abs(temperature_K - T_ref) < 1e-6)
            return LogK_25C;

        // Term 1: logK at reference temperature
        var logK_T = LogK_25C;

        // Term 2: Enthalpy contribution (classic van't Hoff)
        if (Math.Abs(DeltaH0_kJ_mol) > 1e-9)
        {
            var deltaH_J = DeltaH0_kJ_mol * 1000.0;
            logK_T -= deltaH_J / (R * Math.Log(10)) * (1.0 / temperature_K - 1.0 / T_ref);
        }

        // Term 3: Heat capacity contribution
        if (Math.Abs(DeltaCp0_J_molK) > 1e-9)
        {
            var deltaCp = DeltaCp0_J_molK;
            logK_T += deltaCp / (R * Math.Log(10)) * (T_ref / temperature_K - 1.0 - Math.Log(T_ref / temperature_K));
        }

        return logK_T;
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
    GasEquilibrium, // Gas <-> Aqueous
    SurfaceComplexation // Aqueous/Surface <-> Surface
}

/// <summary>
///     Main thermodynamic equilibrium solver using Gibbs energy minimization.
/// </summary>
public class ThermodynamicSolver : SimulatorNodeSupport
{
    // Convergence criteria from PHREEQC (Parkhurst & Appelo, 2013)
    private const double TOLERANCE_MOLES = 1e-12;
    private const double TOLERANCE_GIBBS = 1e-10;
    private const int MAX_ITERATIONS = 100;


    internal static readonly double R_GAS_CONSTANT = 8.314462618e-3; // kJ/(mol·K)
    private readonly ActivityCoefficientCalculator _activityCalculator;
    private readonly CompoundLibrary _compoundLibrary;
    private readonly ReactionGenerator _reactionGenerator;

    public ThermodynamicSolver() : this(null)
    {
    }

    public ThermodynamicSolver(bool? useNodes) : base(useNodes)
    {
        _compoundLibrary = CompoundLibrary.Instance;
        _activityCalculator = new ActivityCoefficientCalculator();
        _reactionGenerator = new ReactionGenerator(_compoundLibrary);

        if (_useNodes)
        {
            Logger.Log("[ThermodynamicSolver] Node Manager integration: ENABLED");
        }
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
        
        // --- FIX START: Create a comprehensive list of ALL elements in the system ---
        // The original code only considered elements from the initialState, which is the
        // source of the bug. By considering all possible elements from all species,
        // we can enforce a mass balance of zero for elements not initially present.
        var allElementsInSystem = new HashSet<string>();
        foreach (var speciesName in species)
        {
            var compound = _compoundLibrary.Find(speciesName);
            if (compound != null)
            {
                var elementsInCompound = _reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula).Keys;
                foreach (var element in elementsInCompound)
                {
                    allElementsInSystem.Add(element);
                }
            }
        }
        var comprehensiveElementList = allElementsInSystem.ToList();
        // --- FIX END ---

        var elementMatrix = BuildElementMatrix(species, comprehensiveElementList);

        // Step 3: Initial guess - use input moles or small positive values
        var x0 = CreateInitialGuess(species, initialState);

        // Step 4: Solve using Newton-Raphson with line search, now passing the comprehensive element list
        // Source: Bethke, 2008. Geochemical Reaction Modeling, Chapter 4
        var solution = SolveGibbsMinimization(x0, species, elementMatrix, comprehensiveElementList, initialState);

        // Step 5: Calculate final properties
        var finalState = BuildFinalState(solution, species, initialState);
        CalculateAqueousProperties(finalState);

        Logger.Log("[ThermodynamicSolver] Equilibrium calculation complete");
        return finalState;
    }

    /// <summary>
    ///     ENHANCEMENT: Calculate saturation indices for all minerals, now including solid solutions.
    ///     SI = log10(IAP/K)
    ///     For solid solutions, SI is calculated for each end-member: SI_i = log10(IAP_i / (K_i * a_i))
    ///     where a_i is the activity of the end-member in the solid phase.
    /// </summary>
    public Dictionary<string, double> CalculateSaturationIndices(ThermodynamicState state)
    {
        var saturationIndices = new Dictionary<string, double>();

        // Handle pure minerals
        var mineralReactions = _reactionGenerator.GenerateDissolutionReactions(state);
        foreach (var reaction in mineralReactions)
        {
            var mineralName = reaction.Stoichiometry.First(kvp => kvp.Value < 0).Key;
            var logIAP = CalculateLogIAP(reaction, state);
            var logK = reaction.CalculateLogK(state.Temperature_K);
            saturationIndices[mineralName] = logIAP - logK;
        }

        // ENHANCEMENT: Handle solid solutions
        var solidSolutions = _compoundLibrary.SolidSolutions;
        foreach (var ss in solidSolutions)
        {
            var totalSSMoles = ss.EndMembers.Sum(em => state.SpeciesMoles.GetValueOrDefault(em, 0.0));
            if (totalSSMoles < TOLERANCE_MOLES) continue;

            for (var i = 0; i < ss.EndMembers.Count; i++)
            {
                var endMemberName = ss.EndMembers[i];
                var endMemberCompound = _compoundLibrary.Find(endMemberName);
                if (endMemberCompound == null) continue;

                var endMemberReaction = _reactionGenerator.GenerateSingleDissolutionReaction(endMemberCompound);
                if (endMemberReaction == null) continue;

                var logIAP = CalculateLogIAP(endMemberReaction, state);
                var logK = endMemberReaction.CalculateLogK(state.Temperature_K);

                // Calculate activity of the end-member in the solid solution
                var moleFraction = state.SpeciesMoles.GetValueOrDefault(endMemberName, 0.0) / totalSSMoles;
                var activityCoeff = ss.MixingModel == SolidSolutionMixingModel.Ideal
                    ? 1.0
                    : Math.Exp(ss.InteractionParameters[i] * Math.Pow(1 - moleFraction, 2) /
                               (R_GAS_CONSTANT * state.Temperature_K));
                var activity_solid = moleFraction * activityCoeff;

                if (activity_solid > 1e-20)
                    saturationIndices[endMemberName] = logIAP - (logK + Math.Log10(activity_solid));
            }
        }

        return saturationIndices;
    }

    private double CalculateLogIAP(ChemicalReaction reaction, ThermodynamicState state)
    {
        var logIAP = 0.0;
        foreach (var (species, coeff) in reaction.Stoichiometry)
            // Only consider products (aqueous species) for IAP
            if (coeff > 0)
            {
                if (state.Activities.TryGetValue(species, out var activity))
                    logIAP += coeff * Math.Log10(Math.Max(activity, 1e-30));
                else
                    // If an ion is missing, IAP is effectively zero, SI is -infinity
                    return double.NegativeInfinity;
            }

        return logIAP;
    }

    /// <summary>
    ///     Selects a set of basis species from a list of all available species.
    ///     Basis species are the fundamental components (e.g., primary ions, neutral molecules, surface sites)
    ///     from which all other (secondary) species can be formed via chemical reactions.
    ///     This selection relies on the `IsPrimaryElementSpecies` and `IsPrimarySurfaceSite` flags
    ///     defined for each compound in the CompoundLibrary.
    /// </summary>
    /// <param name="allSpecies">A list containing the names of all species present in the system.</param>
    /// <returns>A list of names for the selected basis species.</returns>
    private List<string> SelectBasisSpecies(List<string> allSpecies)
    {
        var basisSpecies = new HashSet<string>();

        foreach (var speciesName in allSpecies)
        {
            var compound = _compoundLibrary.Find(speciesName);
            if (compound != null)
                // A species is chosen for the basis if it is designated as a "primary"
                // representative for an element (e.g., Na+, Ca2+, H+, e-) or if it is a
                // primary surface site (e.g., >SOH).
                if (compound.IsPrimaryElementSpecies || compound.IsPrimarySurfaceSite)
                    basisSpecies.Add(speciesName);
        }

        // If no basis species are found, the system is ill-defined, and the solver cannot proceed.
        // This usually indicates a problem with the compound library's definitions.
        if (basisSpecies.Count == 0 && allSpecies.Any())
            Logger.LogWarning(
                "[ThermodynamicSolver] Could not identify any basis species from the provided list. Speciation is likely to fail.");

        return basisSpecies.ToList();
    }

    /// <summary>
    ///     IMPROVED: Better initial guess for aqueous speciation solver.
    ///     Replaces arbitrary 1e-8 with chemically informed estimates.
    ///     Source: Bethke, 2008. Geochemical Modeling, Section 4.4.
    /// </summary>
    public ThermodynamicState SolveSpeciation(ThermodynamicState state)
    {
        Logger.Log("[ThermodynamicSolver] Solving aqueous speciation");

        var reactions = _reactionGenerator.GenerateComplexationReactions(state);
        reactions.AddRange(_reactionGenerator.GenerateAcidBaseReactions(state));
        reactions.AddRange(_reactionGenerator.GenerateRedoxReactions(state));
        reactions.AddRange(_reactionGenerator.GenerateSurfaceComplexationReactions(state));

        var species = GetAllAqueousAndSurfaceSpecies(state, reactions);
        var basisSpecies = SelectBasisSpecies(species);
        var secondarySpecies = species.Except(basisSpecies).ToList();

        var iteration = 0;
        var converged = false;

        // IMPROVED: Chemically informed initial guess
        InitializeBasisSpeciesActivities(state, basisSpecies);

        while (iteration < MAX_ITERATIONS && !converged)
        {
            UpdateIonicStrength(state);
            _activityCalculator.CalculateActivityCoefficients(state);

            UpdateSecondarySpecies(state, reactions, secondarySpecies);

            var (jacobian, residual) = BuildSpeciationSystem(state, basisSpecies, secondarySpecies, reactions);

            Vector<double> delta;
            try
            {
                delta = jacobian.Solve(-residual);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ThermodynamicSolver] Linear solve failed: {ex.Message}");

                // Fallback: Use damped steepest descent
                delta = -residual * 0.01;
            }

            var dampingFactor = CalculateDampingFactor(delta, state, basisSpecies);
            UpdateBasisSpeciesConcentrations(state, delta, dampingFactor, basisSpecies);

            UpdateAllSpeciesMolesFromActivities(state, species);

            converged = residual.L2Norm() < TOLERANCE_MOLES;
            iteration++;

            if (iteration % 20 == 0)
                Logger.Log($"[ThermodynamicSolver] Iteration {iteration}: ||residual|| = {residual.L2Norm():E3}");
        }

        if (!converged)
            Logger.LogWarning($"[ThermodynamicSolver] Speciation did not converge after {MAX_ITERATIONS} iterations");
        else
            Logger.Log($"[ThermodynamicSolver] Speciation converged in {iteration} iterations");

        CalculateAqueousProperties(state);
        return state;
    }

    /// <summary>
    ///     COMPLETE: Initialize basis species activities using chemical intuition.
    ///     Much better than arbitrary 1e-8 for all species.
    /// </summary>
    private void InitializeBasisSpeciesActivities(ThermodynamicState state, List<string> basisSpecies)
    {
        foreach (var speciesName in basisSpecies)
        {
            var compound = _compoundLibrary.Find(speciesName);
            if (compound == null) continue;

            // Check if activity already exists from previous calculations
            if (state.Activities.ContainsKey(speciesName) && state.Activities[speciesName] > 0)
                continue;

            // Use chemically informed initial guesses
            if (speciesName == "H⁺" || speciesName == "H+")
            {
                // Initialize from pH (if available) or assume neutral
                var pH = state.pH > 0 ? state.pH : 7.0;
                state.Activities[speciesName] = Math.Pow(10, -pH);
            }
            else if (speciesName == "OH⁻" || speciesName == "OH-")
            {
                // From water dissociation: Kw = [H+][OH-] = 10^-14
                var h_activity = state.Activities.GetValueOrDefault("H⁺", 1e-7);
                state.Activities[speciesName] = 1e-14 / h_activity;
            }
            else if (speciesName == "e⁻" || speciesName == "e-")
            {
                // Initialize from pe (if available) or assume oxidizing
                var pe = state.pe > 0 ? state.pe : 4.0;
                state.Activities[speciesName] = Math.Pow(10, -pe);
            }
            else if (speciesName == "H₂O" || speciesName == "H2O")
            {
                // Water activity ~ 1
                state.Activities[speciesName] = 1.0;
            }
            else if (compound.IonicCharge.HasValue)
            {
                // For ions: estimate from total elemental composition
                var formula =
                    _reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula); // FIX: Added _reactionGenerator.
                var primaryElement = formula.Keys.FirstOrDefault(k => k != "O" && k != "H");

                if (primaryElement != null && state.ElementalComposition.ContainsKey(primaryElement))
                {
                    var totalMoles = state.ElementalComposition[primaryElement];
                    var molality = totalMoles / state.Volume_L;

                    // Start with assumption that primary ion has 50% of total element
                    state.Activities[speciesName] = molality * 0.5;
                }
                else
                {
                    // Generic low concentration
                    state.Activities[speciesName] = 1e-8;
                }
            }
            else
            {
                // Neutral species: low concentration
                state.Activities[speciesName] = 1e-10;
            }

            // Ensure minimum value for numerical stability
            state.Activities[speciesName] = Math.Max(state.Activities[speciesName], 1e-30);
        }
    }

    /// <summary>
    ///     IMPROVED: Better damping factor calculation with adaptive scaling.
    ///     Prevents oscillations in highly non-linear systems.
    /// </summary>
    private double CalculateDampingFactor(Vector<double> delta, ThermodynamicState state, List<string> basisSpecies)
    {
        var maxAllowedChange = 0.7; // Don't let activities change by more than factor of 2 (0.7 in log space)
        var dampingFactor = 1.0;

        for (var i = 0; i < delta.Count; i++)
        {
            var speciesName = basisSpecies[i];
            var currentActivity = state.Activities.GetValueOrDefault(speciesName, 1e-10);

            // delta is in ln(activity) space, so actual change is exp(delta)
            // We want: |ln(a_new/a_old)| < maxAllowedChange
            // Which means: |delta_damped| < maxAllowedChange

            if (Math.Abs(delta[i]) > maxAllowedChange)
            {
                var requiredDamping = maxAllowedChange / Math.Abs(delta[i]);
                dampingFactor = Math.Min(dampingFactor, requiredDamping);
            }

            // Also prevent activities from going negative (in real space)
            // ln(a_new) = ln(a_old) + damp*delta
            // a_new = a_old * exp(damp*delta)
            // We need a_new > 0, which is always satisfied, but we want a_new > 1e-30
            var projectedActivity = currentActivity * Math.Exp(dampingFactor * delta[i]);
            if (projectedActivity < 1e-30)
            {
                // Reduce damping to keep activity above floor
                var maxDelta = Math.Log(1e-30 / currentActivity);
                if (delta[i] < maxDelta) dampingFactor = Math.Min(dampingFactor, maxDelta / delta[i]);
            }
        }

        // Adaptive damping: increase damping if residual is decreasing
        // This helps convergence in the final iterations
        dampingFactor = Math.Max(0.01, dampingFactor); // Minimum 1% of full Newton step

        return dampingFactor;
    }

    /// <summary>
    ///     IMPROVED: Update species concentrations with better handling of extreme values.
    /// </summary>
    private void UpdateBasisSpeciesConcentrations(ThermodynamicState state, Vector<double> delta,
        double dampingFactor, List<string> basisSpecies)
    {
        for (var i = 0; i < basisSpecies.Count; i++)
        {
            var species = basisSpecies[i];
            var currentActivity = state.Activities.GetValueOrDefault(species, 1e-10);

            // Update: ln(a_new) = ln(a_old) + damp*delta
            var newLogActivity = Math.Log(currentActivity) + dampingFactor * delta[i];

            // Clamp to reasonable range
            newLogActivity = Math.Max(-100, Math.Min(10, newLogActivity)); // 10^-100 to 10^10

            state.Activities[species] = Math.Exp(newLogActivity);
        }
    }

    /// <summary>
    ///     IMPROVED: Ionic strength calculation with better handling of very dilute solutions.
    /// </summary>
    private void UpdateIonicStrength(ThermodynamicState state)
    {
        // I = 0.5 * Σ(m_i * z_i²)
        var I = 0.0;
        var solventMass_kg = state.Volume_L; // Approximation

        // Get better solvent mass estimate
        var (_, rho_water) = WaterPropertiesIAPWS.GetWaterPropertiesCached(state.Temperature_K, state.Pressure_bar);
        solventMass_kg = state.Volume_L * (rho_water / 1000.0);

        foreach (var (species, moles) in state.SpeciesMoles)
        {
            var compound = _compoundLibrary.Find(species);
            if (compound?.IonicCharge != null && compound.Phase == CompoundPhase.Aqueous)
            {
                var molality = moles / solventMass_kg;
                var z = compound.IonicCharge.Value;
                I += 0.5 * molality * z * z;
            }
        }

        // For very dilute solutions, add minimum ionic strength for numerical stability
        // This prevents division by zero in activity coefficient calculations
        state.IonicStrength_molkg = Math.Max(I, 1e-10);

        // Warn if ionic strength is very high
        if (I > 6.0)
            Logger.LogWarning($"[ThermodynamicSolver] Very high ionic strength: {I:F2} mol/kg. " +
                              "Pitzer model may be less accurate above 6 M.");
    }

    /// <summary>
    ///     IMPROVED: Calculate aqueous properties with validation.
    /// </summary>
    private void CalculateAqueousProperties(ThermodynamicState state)
    {
        // Calculate pH from H+ activity
        var hActivity = state.Activities.GetValueOrDefault("H⁺",
            state.Activities.GetValueOrDefault("H+", 1e-7));

        state.pH = -Math.Log10(Math.Max(hActivity, 1e-14));

        // Validate pH is in reasonable range
        if (state.pH < 0 || state.pH > 14)
            Logger.LogWarning($"[ThermodynamicSolver] pH out of normal range: {state.pH:F2}");

        // Calculate pe from e- activity
        var eActivity = state.Activities.GetValueOrDefault("e⁻",
            state.Activities.GetValueOrDefault("e-", 1e-4));

        state.pe = -Math.Log10(Math.Max(eActivity, 1e-20));

        // Validate pe is in reasonable range
        if (state.pe < -10 || state.pe > 20)
            Logger.LogWarning($"[ThermodynamicSolver] pe out of normal range: {state.pe:F2}");

        // Calculate ionic strength
        UpdateIonicStrength(state);

        // Log summary
        Logger.Log(
            $"[ThermodynamicSolver] Final: pH={state.pH:F2}, pe={state.pe:F2}, I={state.IonicStrength_molkg:F4} M");
    }

    private List<string> GetAllAqueousAndSurfaceSpecies(ThermodynamicState state, List<ChemicalReaction> reactions)
    {
        var species = new HashSet<string>();

        // Get available elements from the system
        var availableElements = state.ElementalComposition.Keys.ToHashSet();

        foreach (var s in state.SpeciesMoles.Keys)
        {
            var compound = _compoundLibrary.Find(s);
            if (compound?.Phase == CompoundPhase.Aqueous || compound?.Phase == CompoundPhase.Surface)
                species.Add(s);
        }

        foreach (var reaction in reactions)
        foreach (var s in reaction.Stoichiometry.Keys)
        {
            var compound = _compoundLibrary.Find(s);
            if (compound?.Phase == CompoundPhase.Aqueous || compound?.Phase == CompoundPhase.Surface)
            {
                // Check if this species contains ONLY elements present in the system
                var speciesElements = _reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
                var canBeFormed = speciesElements.Keys.All(element => availableElements.Contains(element));

                if (canBeFormed)
                {
                    species.Add(s);
                }
            }
        }

        return species.ToList();
    }


    private List<string> GetAllSpecies(ThermodynamicState state, List<ChemicalReaction> reactions)
    {
        var species = new HashSet<string>();

        // Get available elements from the system
        var availableElements = state.ElementalComposition.Keys.ToHashSet();

        // Add species from initial state
        foreach (var s in state.SpeciesMoles.Keys)
            species.Add(s);

        // Add species from reactions - BUT ONLY if they can be formed from available elements
        foreach (var reaction in reactions)
        foreach (var s in reaction.Stoichiometry.Keys)
        {
            var compound = _compoundLibrary.Find(s);
            if (compound != null)
            {
                // Check if this species contains ONLY elements present in the system
                var speciesElements = _reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
                var canBeFormed = speciesElements.Keys.All(element => availableElements.Contains(element));

                if (canBeFormed)
                {
                    species.Add(s);
                }
                else
                {
                    Logger.LogWarning($"[ThermodynamicSolver] Skipping species '{s}' - contains elements not in system: " +
                                     $"{string.Join(", ", speciesElements.Keys.Where(e => !availableElements.Contains(e)))}");
                }
            }
        }

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
                    // ENHANCEMENT: Use the robust formula parser from ReactionGenerator
                    var comp = _reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
                    matrix[i, j] = comp.GetValueOrDefault(element, 0);
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

    /// <summary>
    ///     Solve Gibbs Energy Minimization using Lagrange-Newton method with inequality constraints.
    ///     Implements the algorithm from Smith & Missen (1982), Chapter 5.
    /// </summary>
    private Vector<double> SolveGibbsMinimization(Vector<double> x0, List<string> species,
        Matrix<double> elementMatrix, List<string> comprehensiveElementList, ThermodynamicState state)
    {
        var n = species.Count;
        var m = elementMatrix.RowCount; // Number of elements (now the comprehensive count)
        var x = x0.Clone();

        // --- FIX START: Build the constraint vector 'b' using the comprehensive element list ---
        // The original code failed to create zero-constraints for elements not in the
        // initial state. This fix ensures that if an element (e.g., Na, K, Cl) was not
        // in the input, its total final amount in the system MUST be zero.
        var b = Vector<double>.Build.Dense(m);
        for (var i = 0; i < m; i++)
        {
            var element = comprehensiveElementList[i];
            
            // Get the initial moles of this element. If it wasn't in the initial state,
            // GetValueOrDefault correctly returns 0.0, creating the necessary constraint.
            b[i] = state.ElementalComposition.GetValueOrDefault(element, 0.0);
        }
        // --- FIX END ---

        var converged = false;
        var iteration = 0;
        const double epsilon = 1e-10;
        const int maxIter = 100;

        while (!converged && iteration < maxIter)
        {
            // Calculate chemical potentials μ_i = μ_i° + RT ln(a_i)
            var mu = CalculateChemicalPotentials(x, species, state);

            // Calculate gradient of Gibbs energy: ∇G = μ
            var grad = mu.Clone();

            // Calculate constraint residuals: A*x - b = 0
            var constraintResidual = elementMatrix.Multiply(x) - b;

            // Build KKT system:
            // [ H   A^T ] [ Δx ] = [ -∇G ]
            // [ A    0  ] [ λ  ]   [ -r  ]
            // where H is Hessian (approximated as identity for simplicity)

            var kktSize = n + m;
            var kktMatrix = Matrix<double>.Build.Dense(kktSize, kktSize);
            var kktRhs = Vector<double>.Build.Dense(kktSize);

            // Upper-left block: Hessian (use identity + small regularization)
            for (var i = 0; i < n; i++) kktMatrix[i, i] = 1.0 / Math.Max(x[i], 1e-10);

            // Upper-right block: A^T
            for (var i = 0; i < n; i++)
            for (var j = 0; j < m; j++)
                kktMatrix[i, n + j] = elementMatrix[j, i];

            // Lower-left block: A
            for (var i = 0; i < m; i++)
            for (var j = 0; j < n; j++)
                kktMatrix[n + i, j] = elementMatrix[i, j];

            // Right-hand side
            for (var i = 0; i < n; i++) kktRhs[i] = -grad[i];
            for (var i = 0; i < m; i++) kktRhs[n + i] = -constraintResidual[i];

            // Solve KKT system
            Vector<double> solution;
            try
            {
                solution = kktMatrix.Solve(kktRhs);
            }
            catch
            {
                Logger.LogWarning("[ThermodynamicSolver] KKT system singular, using constrained gradient descent");
                // Fallback: project gradient onto constraint manifold
                var projectedGrad = ProjectGradient(grad, elementMatrix, x);
                solution = Vector<double>.Build.Dense(kktSize);
                for (var i = 0; i < n; i++) solution[i] = -projectedGrad[i] * 0.1;
            }

            // Extract step direction
            var dx = solution.SubVector(0, n);

            // Line search with backtracking to ensure positivity
            var alpha = 1.0;
            var xNew = x.Clone();
            for (var ls = 0; ls < 20; ls++)
            {
                xNew = x + alpha * dx;

                // Check positivity constraint
                var allPositive = true;
                for (var i = 0; i < n; i++)
                    if (xNew[i] < 0)
                    {
                        allPositive = false;
                        break;
                    }

                if (allPositive)
                {
                    // Check sufficient decrease in Gibbs energy
                    var GNew = CalculateTotalGibbsEnergy(xNew, species, state);
                    var G = CalculateTotalGibbsEnergy(x, species, state);

                    if (GNew < G + 1e-4 * alpha * grad.DotProduct(dx)) break;
                }

                alpha *= 0.5;
            }

            x = x + alpha * dx;

            // Ensure strict positivity
            for (var i = 0; i < n; i++) x[i] = Math.Max(x[i], 1e-15);

            // Check convergence
            var normDx = dx.L2Norm();
            var normConstraint = constraintResidual.L2Norm();

            converged = normDx < epsilon && normConstraint < epsilon;
            iteration++;

            if (iteration % 10 == 0)
                Logger.Log($"[GibbsMin] Iter {iteration}: ||dx||={normDx:E3}, ||constraint||={normConstraint:E3}");
        }

        if (!converged)
            Logger.LogWarning($"[ThermodynamicSolver] Gibbs minimization reached max iterations ({maxIter})");
        else
            Logger.Log($"[ThermodynamicSolver] Gibbs minimization converged in {iteration} iterations");

        return x;
    }

    /// <summary>
    ///     Calculate chemical potentials for all species.
    ///     μ_i = μ_i° + RT ln(a_i)
    /// </summary>
    private Vector<double> CalculateChemicalPotentials(Vector<double> moles, List<string> species,
        ThermodynamicState state)
    {
        var n = species.Count;
        var mu = Vector<double>.Build.Dense(n);
        const double R = 8.314462618; // J/(mol·K)
        var T = state.Temperature_K;

        for (var i = 0; i < n; i++)
        {
            var compound = _compoundLibrary.Find(species[i]);
            if (compound == null)
            {
                mu[i] = 0;
                continue;
            }

            // Standard chemical potential from formation data
            var mu0 = (compound.GibbsFreeEnergyFormation_kJ_mol ?? 0.0) * 1000.0; // Convert to J/mol

            // Activity term
            var activity = CalculateActivity(species[i], moles[i], state);
            var activityTerm = R * T * Math.Log(Math.Max(activity, 1e-30));

            mu[i] = mu0 + activityTerm;
        }

        return mu;
    }

    /// <summary>
    ///     Calculate total Gibbs energy G = Σ(n_i * μ_i)
    /// </summary>
    private double CalculateTotalGibbsEnergy(Vector<double> moles, List<string> species, ThermodynamicState state)
    {
        var mu = CalculateChemicalPotentials(moles, species, state);
        return moles.DotProduct(mu);
    }

    /// <summary>
    ///     Project gradient onto constraint manifold using A*(A^T*A)^(-1)*A^T
    /// </summary>
    private Vector<double> ProjectGradient(Vector<double> grad, Matrix<double> A, Vector<double> x)
    {
        try
        {
            var AtA = A.Multiply(A.Transpose());
            var AtA_inv = AtA.Inverse();
            var projectionMatrix = A.Transpose().Multiply(AtA_inv).Multiply(A);
            var projectedGrad = grad - projectionMatrix.Multiply(grad);
            return projectedGrad;
        }
        catch
        {
            // If projection fails, just scale the gradient
            return grad * 0.1;
        }
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
        // This is now handled by the more comprehensive ActivityCoefficientCalculator
        if (state.Activities.TryGetValue(species, out var activity))
            return activity;

        // Fallback for species not yet calculated
        var compound = _compoundLibrary.Find(species);
        if (compound?.Phase == CompoundPhase.Aqueous)
        {
            var molality = moles / state.Volume_L;
            return molality; // Assume gamma = 1
        }

        return 1.0; // Assume activity = 1 for solids
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
            if (moles > TOLERANCE_MOLES) finalState.SpeciesMoles[species[i]] = moles;
        }

        SolveSpeciation(finalState); // Recalculate speciation and activities
        return finalState;
    }


    /// <summary>
    ///     Builds the system of equations (Jacobian matrix and residual vector) for the Newton-Raphson speciation solver.
    ///     The system is defined by the mass balance equation for each basis species.
    ///     Residual (r_i) = T_i - C_i
    ///     where T_i is the known total moles of basis component i, and
    ///     C_i is the calculated total moles based on the current activity guess.
    ///     Jacobian (J_ij) = - ∂C_i / ∂(ln a_j)
    ///     where a_j is the activity of basis species j.
    ///     Source: Bethke, C.M., 2008. Geochemical and Biogeochemical Reaction Modeling, Chapter 4.
    /// </summary>
    private (Matrix<double>, Vector<double>) BuildSpeciationSystem(ThermodynamicState state, List<string> basisSpecies,
        List<string> secondarySpecies, List<ChemicalReaction> reactions)
    {
        var nBasis = basisSpecies.Count;
        var jacobian = Matrix<double>.Build.Dense(nBasis, nBasis);
        var residual = Vector<double>.Build.Dense(nBasis);
        var basisSpeciesSet = new HashSet<string>(basisSpecies);
        var basisIndexMap = basisSpecies.Select((name, index) => new { name, index })
            .ToDictionary(x => x.name, x => x.index);

        // --- Step 1: Pre-calculate stoichiometry of secondary species in terms of basis species ---
        // This creates a lookup map: secondary_species -> (basis_species -> stoich_coeff)
        var secondarySpeciesStoich = new Dictionary<string, Dictionary<string, double>>();
        foreach (var secSpeciesName in secondarySpecies)
        {
            var formationReaction = reactions.FirstOrDefault(r =>
                r.Stoichiometry.ContainsKey(secSpeciesName) && r.Stoichiometry[secSpeciesName] > 0 &&
                r.Stoichiometry.Where(kvp => kvp.Value < 0).All(reactant => basisSpeciesSet.Contains(reactant.Key)));

            if (formationReaction != null)
            {
                var stoichMap = new Dictionary<string, double>();
                foreach (var (reactant, stoich) in formationReaction.Stoichiometry)
                    if (stoich < 0) // Reactants are basis species
                        stoichMap[reactant] = -stoich; // Store as positive coefficient for formation

                secondarySpeciesStoich[secSpeciesName] = stoichMap;
            }
        }

        // --- Step 2: Build the Residual Vector (Mass Balance Errors) ---
        for (var i = 0; i < nBasis; i++)
        {
            var basisName = basisSpecies[i];
            var compound = _compoundLibrary.Find(basisName);
            if (compound == null) continue;

            double calculatedTotalMoles = 0;

            if (compound.Phase == CompoundPhase.Surface)
            {
                // Mass balance on a surface site type (e.g., total >SOH sites)
                var site = state.SurfaceSites.FirstOrDefault(s => s.SpeciesMoles.ContainsKey(basisName));
                if (site == null) continue;

                var mineral = _compoundLibrary.Find(site.MineralName);
                if (mineral == null || mineral.SiteDensity_mol_g == null) continue;

                var totalSites = site.Mass_g * mineral.SiteDensity_mol_g.Value;

                // Sum moles of all species containing this primary site
                calculatedTotalMoles = state.SpeciesMoles
                    .Where(kvp => _compoundLibrary.Find(kvp.Key)?.Phase == CompoundPhase.Surface)
                    .Sum(kvp =>
                    {
                        var speciesComp = _compoundLibrary.Find(kvp.Key);
                        // FIXED: Use ChemicalFormula and add null check
                        if (speciesComp == null || string.IsNullOrEmpty(speciesComp.ChemicalFormula)) return 0.0;
                        // Logic: if ">SOH2+".Contains(">SOH"), include it in the sum
                        return speciesComp.ChemicalFormula.Contains(compound.ChemicalFormula) ? kvp.Value : 0.0;
                    });

                residual[i] = totalSites - calculatedTotalMoles;
            }
            else
            {
                // Mass balance on an element
                // Get the element symbol from the basis species (e.g., "Ca" from "Ca2+")
                var element = _reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula).FirstOrDefault().Key;
                if (element == null) continue;

                var totalElement = state.ElementalComposition.GetValueOrDefault(element, 0.0);

                // Sum moles of the element across ALL species in the system
                calculatedTotalMoles = state.SpeciesMoles.Sum(kvp =>
                {
                    var speciesComp = _compoundLibrary.Find(kvp.Key);
                    if (speciesComp == null) return 0.0;
                    var elementsInSpecies = _reactionGenerator.ParseChemicalFormula(speciesComp.ChemicalFormula);
                    return elementsInSpecies.GetValueOrDefault(element, 0) * kvp.Value;
                });

                residual[i] = totalElement - calculatedTotalMoles;
            }
        }

        // --- Step 3: Build the Jacobian Matrix (Derivatives) ---
        // J_ij = -d(CalculatedTotal_i) / d(ln(activity_j))
        for (var i = 0; i < nBasis; i++)
        for (var j = 0; j < nBasis; j++)
        {
            var basis_i = basisSpecies[i];
            var basis_j = basisSpecies[j];
            double derivativeSum = 0;

            // Part 1: Contribution from the basis species themselves.
            // This is non-zero only for diagonal elements (i == j).
            // d(moles_i) / d(ln(activity_j)) is moles_i if i=j, and 0 otherwise.
            if (i == j) derivativeSum += state.SpeciesMoles.GetValueOrDefault(basis_i, 0.0);

            // Part 2: Contribution from all secondary species.
            // d(moles_k)/d(ln(a_j)) = nu_jk * moles_k, where nu_jk is the stoich coeff of basis j in the
            // formation reaction of secondary species k.
            foreach (var (secSpeciesName, stoichMap) in secondarySpeciesStoich)
            {
                var nu_ik = stoichMap.GetValueOrDefault(basis_i, 0.0);
                if (Math.Abs(nu_ik) < 1e-12) continue; // Basis i is not in this secondary species

                var nu_jk = stoichMap.GetValueOrDefault(basis_j, 0.0);
                if (Math.Abs(nu_jk) < 1e-12) continue; // Basis j does not affect this secondary species

                var moles_k = state.SpeciesMoles.GetValueOrDefault(secSpeciesName, 0.0);

                derivativeSum += nu_ik * nu_jk * moles_k;
            }

            jacobian[i, j] = -derivativeSum;
        }

        return (jacobian, residual);
    }

    private void UpdateAllSpeciesMolesFromActivities(ThermodynamicState state, List<string> allSpecies)
    {
        var solventMass_kg = state.Volume_L; // Approx
        foreach (var speciesName in allSpecies)
        {
            var compound = _compoundLibrary.Find(speciesName);
            if (compound == null) continue;

            if (compound.Phase == CompoundPhase.Aqueous)
            {
                var activity = state.Activities.GetValueOrDefault(speciesName, 0.0);
                // gamma = activity / molality -> molality = activity / gamma
                // This requires single-ion activity coefficients, which is complex.
                // We approximate gamma from the activity calculator for the previous step.
                var gamma = _activityCalculator.CalculateSingleIonActivityCoefficient(speciesName, state);
                var molality = activity / Math.Max(gamma, 1e-9);
                state.SpeciesMoles[speciesName] = molality * solventMass_kg;
            }
            // Moles of surface species are updated during the secondary species update
        }
    }

    private void UpdateSecondarySpecies(ThermodynamicState state, List<ChemicalReaction> reactions,
        List<string> secondarySpecies)
    {
        foreach (var speciesName in secondarySpecies)
        {
            // Find the reaction that forms this species from basis species
            var formationReaction = reactions.FirstOrDefault(r =>
                r.Stoichiometry.ContainsKey(speciesName) && r.Stoichiometry[speciesName] > 0 &&
                r.Stoichiometry.Where(kvp => kvp.Value < 0).All(reactant => !secondarySpecies.Contains(reactant.Key)));

            if (formationReaction != null)
            {
                var logK = formationReaction.CalculateLogK(state.Temperature_K);
                var logActivitySum = 0.0;
                foreach (var (reactant, stoich) in formationReaction.Stoichiometry)
                    if (reactant != speciesName)
                    {
                        if (state.Activities.TryGetValue(reactant, out var activity))
                            logActivitySum += -stoich * Math.Log10(Math.Max(activity, 1e-30));
                        else
                            logActivitySum = double.NaN; // Cannot calculate if a reactant is missing
                    }

                if (!double.IsNaN(logActivitySum))
                {
                    var logActivity = logK + logActivitySum;
                    state.Activities[speciesName] = Math.Pow(10, logActivity);
                }
            }
        }
    }
}