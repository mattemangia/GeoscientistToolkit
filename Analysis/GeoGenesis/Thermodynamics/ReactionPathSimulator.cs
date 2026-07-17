// GAIA.GeoGenesis/Thermodynamics/ReactionPathSimulator.cs
//
// Runs a chemical / geochemical reaction path: an initial aqueous system is carried through a
// schedule of (time, temperature, pressure) points, and at each point the speciation, ionic
// strength, pH and mineral saturation indices are evaluated. Optionally, minerals that become
// supersaturated are precipitated to equilibrium (mass transfer out of solution), so the user
// sees both the driving force (SI) and the amount of scale/mineral formed as the system evolves.
//
// This is the "titration / reaction path" mode familiar from PHREEQC and Geochemist's Workbench
// (Bethke, 2008, "Geochemical and Biogeochemical Reaction Modeling", CUP).

using GAIA.GeoGenesis.Materials;

namespace GAIA.GeoGenesis.Thermodynamics;

/// <summary>One node of a temperature/pressure schedule.</summary>
public readonly record struct PtStep(double Time_s, double Temperature_K, double Pressure_bar);

/// <summary>State of the system recorded at one reaction-path node.</summary>
public sealed class ReactionPathPoint
{
    public double Time_s { get; init; }
    public double Temperature_K { get; init; }
    public double Pressure_bar { get; init; }
    public double pH { get; init; }
    public double IonicStrength_molkg { get; init; }
    /// <summary>Saturation index (log10 IAP/Ksp) per mineral of interest at this node.</summary>
    public Dictionary<string, double> SaturationIndices { get; init; } = new();
    /// <summary>Cumulative moles precipitated (+) or dissolved (−) per mineral up to this node.</summary>
    public Dictionary<string, double> CumulativeMineralMoles { get; init; } = new();
}

public sealed class ReactionPathResult
{
    public List<ReactionPathPoint> Points { get; } = new();
    /// <summary>Final cumulative precipitated/dissolved moles per mineral.</summary>
    public Dictionary<string, double> NetMineralMoles { get; } = new();
}

/// <summary>
///     Evaluates a reaction path over a temperature/pressure schedule, optionally precipitating
///     supersaturated minerals to equilibrium at each step.
/// </summary>
public sealed class ReactionPathSimulator
{
    private readonly CompoundLibrary _library;
    private readonly ThermodynamicSolver _solver;
    private readonly ReactionGenerator _generator;

    public ReactionPathSimulator(CompoundLibrary? library = null)
    {
        _library = library ?? CompoundLibrary.Instance;
        _solver = new ThermodynamicSolver();
        _generator = new ReactionGenerator(_library);
    }

    /// <summary>
    ///     Run the path. <paramref name="precipitate"/> enables equilibrium mass transfer of any
    ///     mineral whose SI exceeds <paramref name="precipitationThreshold"/> (default 0 = exactly
    ///     saturated). <paramref name="mineralsOfInterest"/> limits which solids are tracked/allowed
    ///     to precipitate; pass null to consider all solids in the library that the system can form.
    /// </summary>
    public ReactionPathResult Run(
        ThermodynamicState initial,
        IReadOnlyList<PtStep> schedule,
        IReadOnlyList<string>? mineralsOfInterest = null,
        bool precipitate = true,
        double precipitationThreshold = 0.0)
    {
        var result = new ReactionPathResult();
        var state = CloneState(initial);
        var cumulative = new Dictionary<string, double>();

        var allowed = mineralsOfInterest?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var step in schedule)
        {
            state.Temperature_K = step.Temperature_K;
            state.Pressure_bar = step.Pressure_bar;

            _solver.ComputeActivities(state);

            // For an explicit mineral list evaluate each phase exactly (handles polymorphs such as
            // calcite vs aragonite); otherwise let the solver enumerate all formable solids.
            var si = allowed != null
                ? allowed.ToDictionary(m => m, m => _solver.SaturationIndex(state, m), StringComparer.OrdinalIgnoreCase)
                : _solver.CalculateSaturationIndices(state);

            if (precipitate)
                PrecipitateSupersaturated(state, si, allowed, precipitationThreshold, cumulative);

            var tracked = new Dictionary<string, double>();
            foreach (var (mineral, value) in si)
                if (allowed == null || allowed.Contains(mineral))
                    tracked[mineral] = value;

            result.Points.Add(new ReactionPathPoint
            {
                Time_s = step.Time_s,
                Temperature_K = step.Temperature_K,
                Pressure_bar = step.Pressure_bar,
                pH = state.pH,
                IonicStrength_molkg = state.IonicStrength_molkg,
                SaturationIndices = tracked,
                CumulativeMineralMoles = new Dictionary<string, double>(cumulative)
            });
        }

        foreach (var (m, n) in cumulative) result.NetMineralMoles[m] = n;
        return result;
    }

    /// <summary>
    ///     Build a uniform schedule that ramps temperature and pressure linearly between start and
    ///     end conditions over <paramref name="steps"/> nodes and <paramref name="totalTime_s"/>.
    /// </summary>
    public static List<PtStep> LinearSchedule(
        double tStart_K, double tEnd_K, double pStart_bar, double pEnd_bar, int steps, double totalTime_s)
    {
        var list = new List<PtStep>(Math.Max(steps, 1));
        for (var i = 0; i < steps; i++)
        {
            var f = steps == 1 ? 0.0 : i / (double)(steps - 1);
            list.Add(new PtStep(f * totalTime_s, tStart_K + f * (tEnd_K - tStart_K), pStart_bar + f * (pEnd_bar - pStart_bar)));
        }
        return list;
    }

    private void PrecipitateSupersaturated(
        ThermodynamicState state, IReadOnlyDictionary<string, double> si,
        HashSet<string>? allowed, double threshold, Dictionary<string, double> cumulative)
    {
        // Greedy equilibrium step: for each supersaturated mineral, transfer a fraction of the
        // excess toward saturation by removing its constituent ions from solution. Iterating the
        // outer reaction-path loop drives SI → 0 for precipitating phases.
        foreach (var (mineral, index) in si)
        {
            if (double.IsNaN(index) || index <= threshold) continue;
            if (allowed != null && !allowed.Contains(mineral)) continue;

            var compound = _library.Find(mineral);
            if (compound == null || compound.Phase != CompoundPhase.Solid) continue;

            var reaction = _generator.GenerateSingleDissolutionReaction(compound);
            if (reaction == null) continue;

            // Limiting reactant among the dissolution products (ions consumed on precipitation).
            double maxExtent = double.MaxValue;
            foreach (var (species, coeff) in reaction.Stoichiometry)
            {
                if (coeff <= 0) continue; // products of dissolution = reactants of precipitation
                var available = state.SpeciesMoles.GetValueOrDefault(species, 0.0);
                maxExtent = Math.Min(maxExtent, available / coeff);
            }
            if (!double.IsFinite(maxExtent) || maxExtent <= 0) continue;

            // Move a fraction of the way; (1 − 10^(−SI)) approaches 1 for strong supersaturation
            // and 0 near equilibrium, giving a stable relaxation toward SI = 0.
            var relax = 1.0 - Math.Pow(10.0, -(index - threshold));
            var extent = Math.Clamp(relax, 0.0, 0.95) * maxExtent;
            if (extent <= 0) continue;

            foreach (var (species, coeff) in reaction.Stoichiometry)
            {
                if (coeff <= 0) continue;
                state.SpeciesMoles[species] = Math.Max(0.0, state.SpeciesMoles.GetValueOrDefault(species) - coeff * extent);
            }
            cumulative[mineral] = cumulative.GetValueOrDefault(mineral) + extent;
        }
    }

    private static ThermodynamicState CloneState(ThermodynamicState s) => new()
    {
        Temperature_K = s.Temperature_K,
        Pressure_bar = s.Pressure_bar,
        pH = s.pH,
        pe = s.pe,
        Volume_L = s.Volume_L,
        IonicStrength_molkg = s.IonicStrength_molkg,
        SpeciesMoles = new Dictionary<string, double>(s.SpeciesMoles),
        Activities = new Dictionary<string, double>(s.Activities),
        ElementalComposition = new Dictionary<string, double>(s.ElementalComposition),
        GasPartialPressures_atm = new Dictionary<string, double>(s.GasPartialPressures_atm),
        TotalGasPressure_atm = s.TotalGasPressure_atm
    };
}
