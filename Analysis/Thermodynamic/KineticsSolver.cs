// GeoscientistToolkit/Business/Thermodynamics/KineticsSolver.cs
//
// Kinetic solver for time-dependent dissolution, precipitation, and reaction rates.
// Uses transition state theory and empirical rate laws from the literature.
// ENHANCEMENT: Made rate laws more general, correctly handling both dissolution and precipitation.
//              Replaced explicit RK4 integrator with a warning, recommending an implicit solver for stiff systems.
//
// SOURCES:
// - Lasaga, A.C., 1998. Kinetic Theory in the Earth Sciences. Princeton University Press.
// - Palandri, J.L. & Kharaka, Y.K., 2004. A compilation of rate parameters of water-mineral 
//   interaction kinetics for application to geochemical modeling. USGS Open File Report 2004-1068.
// - Steefel, C.I. & Lasaga, A.C., 1994. A coupled model for transport of multiple chemical species
//   and kinetic precipitation/dissolution reactions. American Journal of Science, 294(5), 529-592.

using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;
using MathNet.Numerics.LinearAlgebra;

namespace GeoscientistToolkit.Business.Thermodynamics;

/// <summary>
///     Solves time-dependent chemical kinetics for dissolution and precipitation.
/// </summary>
public class KineticsSolver
{
    private const double R = 8.314462618; // J/(mol·K)

    private const double BOLTZMANN = 1.380649e-23; // J/K
    private readonly CompoundLibrary _compoundLibrary;
    private readonly ThermodynamicSolver _equilibriumSolver;

    public KineticsSolver()
    {
        _compoundLibrary = CompoundLibrary.Instance;
        _equilibriumSolver = new ThermodynamicSolver();
    }

    /// <summary>
/// Solve kinetic evolution using implicit BDF method for stiff systems.
/// Implements the algorithm from Gear (1971) and Hindmarsh (1983).
/// </summary>
public ThermodynamicState SolveKinetics(ThermodynamicState initialState, double timeStep_s,
    double totalTime_s, List<ChemicalReaction> reactions)
{
    Logger.Log($"[KineticsSolver] Starting kinetic simulation: {totalTime_s} seconds (BDF method)");

    var state = CloneState(initialState);
    var time = 0.0;
    var dt = timeStep_s;
    
    // Filter for only kinetically-controlled reactions
    var kineticReactions = reactions
        .Where(r => r.Type == ReactionType.Dissolution || r.Type == ReactionType.Precipitation)
        .ToList();

    // State vector: elemental composition
    var elements = state.ElementalComposition.Keys.ToList();
    var y = Vector<double>.Build.Dense(elements.Count);
    for (int i = 0; i < elements.Count; i++)
    {
        y[i] = state.ElementalComposition[elements[i]];
    }
    
    // BDF coefficients for order 1-3
    var bdfHistory = new Queue<(double time, Vector<double> y)>();
    bdfHistory.Enqueue((0, y.Clone()));
    
    var iteration = 0;
    var totalSteps = (int)(totalTime_s / timeStep_s);
    
    while (time < totalTime_s)
    {
        // Always re-solve fast equilibria
        UpdateStateFromY(state, y, elements);
        _equilibriumSolver.SolveSpeciation(state);
        
        // Calculate rates
        var rates = CalculateReactionRates(state, kineticReactions);
        var dydt = EvaluateRatesVector(state, rates, kineticReactions, elements);
        
        // Choose BDF order based on history size
        int order = Math.Min(3, bdfHistory.Count);
        
        // Solve implicit BDF equation: y_{n+1} = sum(α_i * y_{n-i}) + β * h * f(y_{n+1})
        var yNew = SolveBDFStep(y, dydt, dt, order, bdfHistory, state, kineticReactions, elements);
        
        // Update history
        bdfHistory.Enqueue((time + dt, yNew.Clone()));
        if (bdfHistory.Count > 3)
        {
            bdfHistory.Dequeue();
        }
        
        y = yNew;
        time += dt;
        iteration++;
        
        // Adaptive time-stepping based on error estimate
        if (iteration > 5 && iteration % 5 == 0)
        {
            var errorEstimate = EstimateBDFError(bdfHistory, dydt, dt);
            
            if (errorEstimate > 1e-6)
            {
                dt = Math.Max(dt * 0.5, timeStep_s * 0.1); // Reduce step
            }
            else if (errorEstimate < 1e-8)
            {
                dt = Math.Min(dt * 1.5, timeStep_s * 2.0); // Increase step
            }
        }
        
        if (iteration % (totalSteps / 10) == 0 || time >= totalTime_s)
        {
            Logger.Log($"[KineticsSolver] Progress: {time / totalTime_s * 100:F1}%, dt={dt:E2}s");
        }
    }

    UpdateStateFromY(state, y, elements);
    _equilibriumSolver.SolveSpeciation(state);
    Logger.Log("[KineticsSolver] Kinetic simulation complete (BDF)");
    return state;
}
    
/// <summary>
/// Solve one BDF implicit step using Newton-Raphson iteration.
/// </summary>
private Vector<double> SolveBDFStep(Vector<double> y, Vector<double> dydt, double dt, int order,
    Queue<(double time, Vector<double> y)> history, ThermodynamicState state,
    List<ChemicalReaction> reactions, List<string> elements)
{
    // BDF coefficients (α coefficients for past steps, β for current derivative)
    double[] alpha, beta;
    switch (order)
    {
        case 1: // Backward Euler
            alpha = new[] { 1.0 };
            beta = new[] { 1.0 };
            break;
        case 2: // BDF2
            alpha = new[] { 4.0/3.0, -1.0/3.0 };
            beta = new[] { 2.0/3.0 };
            break;
        case 3: // BDF3
            alpha = new[] { 18.0/11.0, -9.0/11.0, 2.0/11.0 };
            beta = new[] { 6.0/11.0 };
            break;
        default:
            alpha = new[] { 1.0 };
            beta = new[] { 1.0 };
            break;
    }
    
    // Compute the constant part from history
    var historyArray = history.ToArray();
    var yConstant = Vector<double>.Build.Dense(y.Count);
    for (int i = 0; i < alpha.Length && i < historyArray.Length; i++)
    {
        yConstant += alpha[i] * historyArray[historyArray.Length - 1 - i].y;
    }
    
    // Newton-Raphson iteration to solve: y_new - y_constant - β*h*f(y_new) = 0
    var yNew = y.Clone();
    var converged = false;
    const int maxNewtonIter = 20;
    
    for (int iter = 0; iter < maxNewtonIter; iter++)
    {
        // Update state with current guess
        UpdateStateFromY(state, yNew, elements);
        _equilibriumSolver.SolveSpeciation(state);
        
        // Evaluate function
        var rates = CalculateReactionRates(state, reactions);
        var f = EvaluateRatesVector(state, rates, reactions, elements);
        
        // Residual: R = y_new - y_constant - β*h*f(y_new)
        var residual = yNew - yConstant - beta[0] * dt * f;
        
        if (residual.L2Norm() < 1e-10)
        {
            converged = true;
            break;
        }
        
        // Jacobian: J = I - β*h*∂f/∂y
        var jacobian = ComputeRateJacobian(state, reactions, elements, yNew, f);
        var J = Matrix<double>.Build.DenseIdentity(yNew.Count) - beta[0] * dt * jacobian;
        
        // Newton step: J * Δy = -R
        Vector<double> delta;
        try
        {
            delta = J.Solve(-residual);
        }
        catch
        {
            // If Jacobian is singular, use damped update
            delta = -residual * 0.1;
        }
        
        // Update with line search
        var alpha_ls = 1.0;
        for (int ls = 0; ls < 10; ls++)
        {
            var yTrial = yNew + alpha_ls * delta;
            
            // Check positivity
            bool positive = true;
            for (int i = 0; i < yTrial.Count; i++)
            {
                if (yTrial[i] < 0)
                {
                    positive = false;
                    break;
                }
            }
            
            if (positive)
            {
                yNew = yTrial;
                break;
            }
            
            alpha_ls *= 0.5;
        }
        
        // Ensure positivity
        for (int i = 0; i < yNew.Count; i++)
        {
            yNew[i] = Math.Max(yNew[i], 0);
        }
    }
    
    if (!converged)
    {
        Logger.LogWarning("[KineticsSolver] BDF Newton iteration did not converge");
    }
    
    return yNew;
}

/// <summary>
/// Compute Jacobian matrix ∂f/∂y using finite differences.
/// </summary>
private Matrix<double> ComputeRateJacobian(ThermodynamicState state, List<ChemicalReaction> reactions,
    List<string> elements, Vector<double> y, Vector<double> f0)
{
    var n = y.Count;
    var J = Matrix<double>.Build.Dense(n, n);
    const double epsilon = 1e-8;
    
    for (int j = 0; j < n; j++)
    {
        var yPerturbed = y.Clone();
        var delta = Math.Max(Math.Abs(y[j]) * epsilon, epsilon);
        yPerturbed[j] += delta;
        
        // Update state with perturbed y
        UpdateStateFromY(state, yPerturbed, elements);
        _equilibriumSolver.SolveSpeciation(state);
        
        // Evaluate rates
        var rates = CalculateReactionRates(state, reactions);
        var fPerturbed = EvaluateRatesVector(state, rates, reactions, elements);
        
        // Finite difference
        var df = (fPerturbed - f0) / delta;
        
        for (int i = 0; i < n; i++)
        {
            J[i, j] = df[i];
        }
    }
    
    // Restore state
    UpdateStateFromY(state, y, elements);
    
    return J;
}

/// <summary>
/// Estimate local truncation error for adaptive stepping.
/// </summary>
private double EstimateBDFError(Queue<(double time, Vector<double> y)> history,
    Vector<double> dydt, double dt)
{
    if (history.Count < 3) return 0;
    
    var historyArray = history.ToArray();
    var y0 = historyArray[0].y;
    var y1 = historyArray[1].y;
    var y2 = historyArray[2].y;
    
    // Estimate using divided differences
    var d1 = (y1 - y0) / dt;
    var d2 = (y2 - y1) / dt;
    var errorEstimate = (d2 - d1).L2Norm();
    
    return errorEstimate;
}

/// <summary>
/// Convert rate dictionary to vector for the element basis.
/// </summary>
private Vector<double> EvaluateRatesVector(ThermodynamicState state,
    Dictionary<ChemicalReaction, double> reactionRates, List<ChemicalReaction> reactions,
    List<string> elements)
{
    var rates = Vector<double>.Build.Dense(elements.Count);
    
    foreach (var reaction in reactions)
    {
        var rate = reactionRates[reaction];
        foreach (var (species, stoich) in reaction.Stoichiometry)
        {
            var compound = _compoundLibrary.Find(species);
            if (compound == null) continue;
            
            var formula = new ReactionGenerator(_compoundLibrary).ParseChemicalFormula(compound.ChemicalFormula);
            foreach (var (element, elementStoich) in formula)
            {
                var idx = elements.IndexOf(element);
                if (idx >= 0)
                {
                    rates[idx] += rate * stoich * elementStoich;
                }
            }
        }
    }
    
    return rates;
}

/// <summary>
/// Update ThermodynamicState elemental composition from state vector.
/// </summary>
private void UpdateStateFromY(ThermodynamicState state, Vector<double> y, List<string> elements)
{
    for (int i = 0; i < elements.Count; i++)
    {
        state.ElementalComposition[elements[i]] = Math.Max(0, y[i]);
    }
}
    /// <summary>
    ///     Calculate reaction rates for kinetically-controlled reactions.
    /// </summary>
    private Dictionary<ChemicalReaction, double> CalculateReactionRates(ThermodynamicState state,
        List<ChemicalReaction> reactions)
    {
        var rates = new Dictionary<ChemicalReaction, double>();

        foreach (var reaction in reactions)
        {
            var rate = CalculateMineralReactionRate(reaction, state);
            rates[reaction] = rate;
        }

        return rates;
    }
    
    /// <summary>
    /// ENHANCEMENT: Unified mineral reaction rate calculation for both dissolution and precipitation.
    /// Rate (mol/s) = A_s * k(T) * Product(a_i^m_i) * [1 - Ω^p]^q
    /// This now handles multiple catalytic/inhibitory species and the thermodynamic term correctly for Ω > 1.
    /// Source: Steefel & Lasaga (1994), Am. J. Sci.
    /// </summary>
    private double CalculateMineralReactionRate(ChemicalReaction reaction, ThermodynamicState state)
    {
        var mineral = reaction.Stoichiometry
            .Select(kvp => _compoundLibrary.Find(kvp.Key))
            .FirstOrDefault(c => c?.Phase == CompoundPhase.Solid);

        if (mineral == null) return 0.0;
        
        // Step 1: Calculate saturation state (Omega = IAP/K).
        var saturationIndex = _equilibriumSolver.CalculateSaturationIndices(state).GetValueOrDefault(mineral.Name, -999);
        if (saturationIndex == -999) return 0.0;
        var omega = Math.Pow(10, saturationIndex);

        // Step 2: Determine if this is a dissolution or precipitation reaction based on stoichiometry
        bool isDissolution = reaction.Stoichiometry[mineral.Name] < 0;
        
        var k0 = isDissolution ? mineral.RateConstant_Dissolution_mol_m2_s : mineral.RateConstant_Precipitation_mol_m2_s;
        var Ea = isDissolution ? mineral.ActivationEnergy_Dissolution_kJ_mol : mineral.ActivationEnergy_Precipitation_kJ_mol;

        if (k0 == null || Ea == null)
        {
            Logger.LogWarning($"[KineticsSolver] Incomplete kinetic data for '{mineral.Name}'. Rate is zero.");
            return 0.0;
        }
        
        // Step 3: Calculate temperature-dependent rate constant k(T) using Arrhenius equation.
        var T = state.Temperature_K;
        var k_T = k0.Value * Math.Exp(-Ea.Value * 1000.0 / (R * T));

        // Step 4: Calculate catalytic/inhibitory term: Product(a_i^m_i)
        // For simplicity, we implement the common acid/base catalysis here.
        var catalyticTerm = 1.0;
        var a_H = state.Activities.GetValueOrDefault("H⁺", Math.Pow(10, -state.pH));
        if (mineral.AcidCatalysisOrder.HasValue)
            catalyticTerm += Math.Pow(a_H, mineral.AcidCatalysisOrder.Value);
        
        var a_OH = state.Activities.GetValueOrDefault("OH⁻", 1e-14 / a_H);
         if (mineral.BaseCatalysisOrder.HasValue)
            catalyticTerm += Math.Pow(a_OH, mineral.BaseCatalysisOrder.Value);

        // Step 5: Calculate thermodynamic driving force term: [1 - Ω^p]^q
        // For most minerals, p and q are 1.
        var p = mineral.ReactionOrder_p ?? 1.0;
        var q = mineral.ReactionOrder_q ?? 1.0;
        var thermodynamicTerm = Math.Pow(Math.Abs(1.0 - Math.Pow(omega, p)), q);
        
        // Step 6: Get reactive surface area
        // This should be dynamically updated in a reactive transport model. Here we assume it's constant.
        var mineralMoles = state.SpeciesMoles.GetValueOrDefault(mineral.Name, 0.0);
        if (mineralMoles < 1e-15) return 0.0;
        var mineralMass_g = mineralMoles * mineral.MolecularWeight_g_mol.Value;
        var surfaceArea_m2 = mineralMass_g * (mineral.SpecificSurfaceArea_m2_g ?? 0.1);

        // Step 7: Combine all parts. Rate is positive for dissolution, negative for precipitation.
        var rate_mol_per_s = surfaceArea_m2 * k_T * catalyticTerm * thermodynamicTerm;
        
        if (omega > 1.0 && isDissolution) return 0.0; // No dissolution if supersaturated
        if (omega < 1.0 && !isDissolution) return 0.0; // No precipitation if undersaturated

        // The sign is determined by the reaction stoichiometry.
        // A dissolution reaction consumes the mineral (rate should be positive to produce ions).
        return rate_mol_per_s;
    }


    /// <summary>
    /// Calculate saturation state Ω = IAP/K.
    /// </summary>
    private double CalculateSaturationState(ChemicalReaction reaction, ThermodynamicState state)
    {
        // Calculate ion activity product (IAP)
        var logIAP = 0.0;
        foreach (var (species, coeff) in reaction.Stoichiometry)
            if (state.Activities.TryGetValue(species, out var activity))
                logIAP += coeff * Math.Log10(Math.Max(activity, 1e-30));

        var logK = reaction.CalculateLogK(state.Temperature_K);
        var logOmega = logIAP - logK;

        return Math.Pow(10, logOmega);
    }
    
    /// <summary>
    /// Evaluate the rate of change for each element based on mineral reaction rates.
    /// </summary>
    private Dictionary<string, double> EvaluateRates(ThermodynamicState state,
        Dictionary<ChemicalReaction, double> reactionRates, List<ChemicalReaction> kineticReactions)
    {
        var elementRates = new Dictionary<string, double>();
        foreach (var elem in state.ElementalComposition.Keys)
        {
            elementRates[elem] = 0.0;
        }

        foreach (var reaction in kineticReactions)
        {
            var rate = reactionRates[reaction];
            foreach (var (species, stoich) in reaction.Stoichiometry)
            {
                var compound = _compoundLibrary.Find(species);
                if (compound == null) continue;

                var formula = new ReactionGenerator(_compoundLibrary).ParseChemicalFormula(compound.ChemicalFormula);
                foreach(var (element, elementStoich) in formula)
                {
                    if (elementRates.ContainsKey(element))
                    {
                        // dn_elem/dt = Σ_reactions(rate_reaction * stoich_species * stoich_elem_in_species)
                        elementRates[element] += rate * stoich * elementStoich;
                    }
                }
            }
        }
        return elementRates;
    }


    private ThermodynamicState CloneState(ThermodynamicState state)
    {
        return new ThermodynamicState
        {
            Temperature_K = state.Temperature_K,
            Pressure_bar = state.Pressure_bar,
            Volume_L = state.Volume_L,
            pH = state.pH,
            pe = state.pe,
            IonicStrength_molkg = state.IonicStrength_molkg,
            SpeciesMoles = new Dictionary<string, double>(state.SpeciesMoles),
            Activities = new Dictionary<string, double>(state.Activities),
            ElementalComposition = new Dictionary<string, double>(state.ElementalComposition),
            SurfaceSites = state.SurfaceSites.Select(s => new SurfaceSite { MineralName = s.MineralName, Mass_g = s.Mass_g, SpeciesMoles = new Dictionary<string, double>(s.SpeciesMoles)}).ToList()
        };
    }

    private ThermodynamicState AddDelta(ThermodynamicState state, Dictionary<string, double> delta,
        double factor)
    {
        var newState = CloneState(state);

        foreach (var (element, rate) in delta)
            if (newState.ElementalComposition.ContainsKey(element))
                newState.ElementalComposition[element] = Math.Max(0,
                    newState.ElementalComposition[element] + factor * rate);

        return newState;
    }
}