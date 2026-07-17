// GAIA.GeoGenesis/Thermodynamics/GeothermalCoupler.cs
//
// Couples GeoGenesis aqueous chemistry to a geothermal production/injection trajectory produced
// by ReservoirFlux or TerraYield (temperature, pressure and mass-flow versus time at a chosen node, e.g.
// a production well). Along that trajectory it evaluates ALL mineral processes — scaling,
// crystallization and dissolution — for every solid the brine can form, and quantifies brine
// resource extraction for any dissolved element (e.g. lithium, the key direct-lithium-extraction
// target in geothermal brines such as the Salton Sea / Upper Rhine Graben).
//
// References: Plummer & Busenberg (1982); Stumm & Morgan (1996); Bethke (2008); and for Li-in-brine
// resources e.g. Stringfellow & Dobson (2021), "Technology for Lithium Extraction in the Context of
// Hybrid Geothermal Power", Geothermics.

using GAIA.GeoGenesis.Materials;
using GAIA.GeoGenesis.Reactor;

namespace GAIA.GeoGenesis.Thermodynamics;

/// <summary>One node of a geothermal production trajectory.</summary>
public readonly record struct GeothermalTrajectoryPoint(
    double TimeYears, double Temperature_K, double Pressure_bar, double FlowRate_kg_s,
    double ResidenceTime_s = 0.0);

/// <summary>A produced-fluid trajectory; can be populated from ReservoirFlux snapshots or TerraYield output.</summary>
public sealed class GeothermalTrajectory
{
    public List<GeothermalTrajectoryPoint> Points { get; } = new();
    public GeothermalTrajectory Add(double timeYears, double tK, double pBar, double flow_kg_s, double residenceTime_s = 0.0)
    {
        Points.Add(new GeothermalTrajectoryPoint(timeYears, tK, pBar, flow_kg_s, residenceTime_s));
        return this;
    }
}

/// <summary>Scaling/crystallization result for one mineral along the trajectory.</summary>
public sealed class MineralScalingSeries
{
    public string Mineral { get; init; } = string.Empty;
    public List<double> SaturationIndex { get; } = new();
    /// <summary>Instantaneous precipitation rate (kg/s of mineral) at each node (≥0; 0 if undersaturated).</summary>
    public List<double> ScaleRate_kg_s { get; } = new();
    /// <summary>Cumulative scale mass (kg) deposited up to each node.</summary>
    public List<double> CumulativeScale_kg { get; } = new();
    public double TotalScale_kg => CumulativeScale_kg.Count > 0 ? CumulativeScale_kg[^1] : 0.0;
}

/// <summary>
/// Chemistry-driven evolution of the near-well formation petrophysics along the trajectory: the
/// cumulative mineral scale of <see cref="GeothermalCoupler.EvaluateScaling"/> deposited into a
/// pore network progressively clogs it. Optional — computed only when a pore network is supplied.
/// </summary>
public sealed class FormationCloggingSeries
{
    /// <summary>Bulk rock volume (m³) in which the scale is assumed to deposit (near-well formation).</summary>
    public double AffectedRockVolume_m3 { get; init; }
    public ReactivePnmState Initial { get; init; }
    public List<double> TimeYears { get; } = new();
    public List<ReactivePnmState> States { get; } = new();
    public ReactivePnmState Final => States.Count > 0 ? States[^1] : Initial;
    /// <summary>φ/φ0 at the end of the trajectory — apply to any downstream porosity field.</summary>
    public double PorosityRatio => Initial.Porosity > 0 ? Final.Porosity / Initial.Porosity : 1.0;
    /// <summary>k/k0 at the end of the trajectory — apply to ReservoirFlux rock/mesh permeability or PINN petrophysics.</summary>
    public double PermeabilityRatio => Initial.Permeability_m2 > 0 ? Final.Permeability_m2 / Initial.Permeability_m2 : 1.0;
}

/// <summary>
/// Options for the overpressure-fracturing term of <see cref="GeothermalCoupler.EvaluateFormationClogging"/>.
/// The legacy single-threshold path judged fracturing on the PRODUCED-fluid pressure (the flowing BHP),
/// which is in drawdown and almost never fractures a producer. These options let the caller supply the
/// INJECTION/FORMATION pore pressure instead, and optionally judge fracturing on Terzaghi effective
/// stress σ' = σ_h,min − P_pore (tensile failure when σ' ≤ −T) rather than raw pore pressure.
/// Refs: Terzaghi (1943); Jaeger, Cook & Zimmerman (2007), Fundamentals of Rock Mechanics.
/// Screening-grade.
/// </summary>
public sealed class GeothermalFracturingOptions
{
    /// <summary>Fracture threshold: the minimum total horizontal stress σ_h,min of the formation in bar
    /// (the "fracture pressure"/fracture gradient). Pore pressure at/above this (plus tensile strength
    /// in effective-stress mode) fractures the rock.</summary>
    public double FracturePressure_bar { get; init; }
    /// <summary>Per-node pore pressure (bar) judged against the threshold. Supply the INJECTION or
    /// FORMATION pressure — NOT the produced-fluid BHP, which is in drawdown and rarely fractures a
    /// producer. When null the trajectory (produced-fluid) pressure is used (legacy behaviour).</summary>
    public IReadOnlyList<double>? FracturingPressureSeries_bar { get; init; }
    /// <summary>When true, fracturing is judged on Terzaghi effective stress σ' = σ_h,min − P_pore:
    /// damage begins at the tensile-failure threshold P = σ_h,min + T (σ' = −T) instead of raw
    /// P = σ_h,min. Physically correct criterion; otherwise the raw-pressure screening is used.</summary>
    public bool UseEffectiveStress { get; init; }
    /// <summary>Tensile strength of the formation (bar); only used when UseEffectiveStress is true.</summary>
    public double TensileStrength_bar { get; init; }
}

/// <summary>Resource-extraction result for one dissolved element along the trajectory.</summary>
public sealed class ElementExtractionResult
{
    public string Element { get; init; } = string.Empty;
    public double Concentration_mg_L { get; init; }
    public double RecoveryFactor { get; init; }
    /// <summary>Recoverable element mass rate (kg/s) at each trajectory node.</summary>
    public List<double> Rate_kg_s { get; } = new();
    /// <summary>Cumulative recoverable element mass (tonnes) up to each node.</summary>
    public List<double> Cumulative_tonnes { get; } = new();
    public double Total_tonnes => Cumulative_tonnes.Count > 0 ? Cumulative_tonnes[^1] : 0.0;
}

public sealed class GeothermalCoupler
{
    private readonly CompoundLibrary _library;
    private readonly ThermodynamicSolver _solver;
    private readonly ReactionGenerator _generator;

    public GeothermalCoupler(CompoundLibrary? library = null)
    {
        _library = library ?? CompoundLibrary.Instance;
        _solver = new ThermodynamicSolver();
        _generator = new ReactionGenerator(_library);
    }

    /// <summary>
    ///     Evaluate scaling/crystallization for the requested minerals along the trajectory. The system
    ///     is OPEN: the produced brine is continuously replenished, so at each node a FRESH charge of
    ///     the feed brine is re-equilibrated at the node (T,P) and any supersaturated mineral
    ///     precipitates. The removed mass is converted to a rate using the local mass flow. Re-creating
    ///     the state per node (instead of depleting one closed packet) keeps the chemistry consistent
    ///     with the rate × FlowRate open-system assumption and avoids artificial concentration of all
    ///     the scaling in the first 1–2 nodes.
    ///     When <paramref name="useKinetics"/> is set, kinetically-limited silicates (quartz, amorphous
    ///     silica) are capped by the Lasaga/Arrhenius rate law over the node residence time
    ///     (<see cref="MineralKinetics"/>) so low-temperature clogging is not grossly over-estimated.
    /// </summary>
    public List<MineralScalingSeries> EvaluateScaling(
        WaterComposition brine, GeothermalTrajectory trajectory, IReadOnlyList<string> minerals,
        bool useKinetics = false, double defaultResidenceTime_s = MineralKinetics.DefaultResidenceTime_s)
    {
        var series = minerals.ToDictionary(m => m, m => new MineralScalingSeries { Mineral = m }, StringComparer.OrdinalIgnoreCase);

        double prevYears = trajectory.Points.Count > 0 ? trajectory.Points[0].TimeYears : 0.0;

        foreach (var pt in trajectory.Points)
        {
            var dtYears = Math.Max(0.0, pt.TimeYears - prevYears);
            var dtSeconds = dtYears * 365.25 * 86400.0;
            prevYears = pt.TimeYears;

            // OPEN system: re-equilibrate a FRESH feed-brine charge at each node. The previous
            // implementation carried a single closed packet whose constituents were exhausted node
            // after node, so scaling collapsed into the first nodes — the opposite of field data.
            var state = brine.ToState(_library, _generator);
            state.Temperature_K = pt.Temperature_K;
            state.Pressure_bar = pt.Pressure_bar;
            _solver.ComputeActivities(state);

            foreach (var mineral in minerals)
            {
                var s = series[mineral];
                var index = _solver.SaturationIndex(state, mineral);
                s.SaturationIndex.Add(index);

                double ratePerKg = 0.0; // mol mineral precipitated per kg water at this node
                if (index > 0)
                {
                    // Kinetically-limited silicates never reach the full equilibrium extent at low T:
                    // scale the precipitated amount by the Arrhenius fraction over the residence time.
                    var contactTime = pt.ResidenceTime_s > 0.0
                        ? pt.ResidenceTime_s
                        : Math.Max(0.0, defaultResidenceTime_s);
                    var frac = useKinetics && MineralKinetics.IsKineticallyLimited(mineral)
                        ? MineralKinetics.EquilibriumFractionReached(_library, mineral, pt.Temperature_K, contactTime)
                        : 1.0;
                    ratePerKg = PrecipitateToEquilibrium(state, mineral, index, frac);
                }

                var mw = _library.Find(mineral)?.MolecularWeight_g_mol ?? 0.0;
                // mol/kgw → kg mineral per kg water; × flow (kg water/s ≈ brine kg/s) → kg/s.
                var scaleKgPerKgWater = ratePerKg * mw / 1000.0;
                var rate = scaleKgPerKgWater * pt.FlowRate_kg_s;
                s.ScaleRate_kg_s.Add(rate);
                var prevCum = s.CumulativeScale_kg.Count > 0 ? s.CumulativeScale_kg[^1] : 0.0;
                s.CumulativeScale_kg.Add(prevCum + rate * dtSeconds);
            }
        }

        return series.Values.ToList();
    }

    /// <summary>
    ///     Optional pore-network coupling: convert the cumulative scale of
    ///     <see cref="EvaluateScaling"/> into progressive clogging of a pore network representing the
    ///     near-well formation (<paramref name="affectedRockVolume_m3"/> of bulk rock in which the
    ///     scale deposits). Returns porosity/permeability/tortuosity per trajectory node; the final
    ///     ratios can be applied to ReservoirFlux rock properties / mesh cells or to the geothermal
    ///     and aquifer PINN petrophysics to propagate mineral precipitation into the flow models.
    ///     When <paramref name="fracturePressure_bar"/> is set, nodes whose pressure exceeds it
    ///     accumulate overpressure fracture damage that reopens the network (clogging vs fracturing
    ///     compete; damage never heals along the trajectory). Prefer <paramref name="fracturing"/>
    ///     (<see cref="GeothermalFracturingOptions"/>) to judge fracturing on the injection/formation
    ///     pressure (not the produced-fluid BHP) and/or on Terzaghi effective stress.
    /// </summary>
    public FormationCloggingSeries EvaluateFormationClogging(
        IReadOnlyList<MineralScalingSeries> scaling, GeothermalTrajectory trajectory,
        PoreNetworkModel poreNetwork, double affectedRockVolume_m3, double? fracturePressure_bar = null,
        GeothermalFracturingOptions? fracturing = null)
    {
        var result = new FormationCloggingSeries
        {
            AffectedRockVolume_m3 = affectedRockVolume_m3,
            Initial = ReactivePoreNetworkCoupling.InitialState(poreNetwork)
        };

        double damage = 0.0;
        for (int i = 0; i < trajectory.Points.Count; i++)
        {
            var mass = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in scaling)
                if (i < s.CumulativeScale_kg.Count)
                    mass[s.Mineral] = s.CumulativeScale_kg[i];

            var vfrac = ReactivePoreNetworkCoupling.PrecipitateVolumeFraction(_library, mass, affectedRockVolume_m3);
            if (fracturing is { FracturePressure_bar: > 0 } frac)
            {
                // Use the injection/formation pressure when supplied, else fall back to the trajectory
                // (produced-fluid) pressure. The produced BHP is in drawdown and almost never fractures
                // a producer, so a separate formation-pressure input is the physically meaningful one.
                var pPore = frac.FracturingPressureSeries_bar != null && i < frac.FracturingPressureSeries_bar.Count
                    ? frac.FracturingPressureSeries_bar[i]
                    : trajectory.Points[i].Pressure_bar;
                damage = Math.Max(damage, frac.UseEffectiveStress
                    ? ReactivePoreNetworkCoupling.FractureDamageFromEffectiveStress(pPore, frac.FracturePressure_bar, frac.TensileStrength_bar)
                    : ReactivePoreNetworkCoupling.FractureDamageFromPressure(pPore, frac.FracturePressure_bar));
            }
            else if (fracturePressure_bar is { } pFrac)
                damage = Math.Max(damage, ReactivePoreNetworkCoupling.FractureDamageFromPressure(trajectory.Points[i].Pressure_bar, pFrac));
            result.TimeYears.Add(trajectory.Points[i].TimeYears);
            result.States.Add(ReactivePoreNetworkCoupling.Evolve(poreNetwork, vfrac, damage));
        }
        return result;
    }

    /// <summary>
    ///     Recoverable mass of a dissolved element from the produced brine along the trajectory.
    ///     <paramref name="concentration_mg_L"/> is the element concentration in the brine and
    ///     <paramref name="recoveryFactor"/> the extraction efficiency (0–1). Generic — use for
    ///     lithium, or any other element (B, K, Mn, Zn, …).
    /// </summary>
    public ElementExtractionResult EvaluateExtraction(
        GeothermalTrajectory trajectory, string element, double concentration_mg_L, double recoveryFactor)
    {
        var res = new ElementExtractionResult
        {
            Element = element,
            Concentration_mg_L = concentration_mg_L,
            RecoveryFactor = Math.Clamp(recoveryFactor, 0.0, 1.0)
        };

        double prevYears = trajectory.Points.Count > 0 ? trajectory.Points[0].TimeYears : 0.0;
        foreach (var pt in trajectory.Points)
        {
            // mass rate (kg/s) = c[kg/m³] × Q_volumetric[m³/s] ≈ c[mg/L]·1e-3·(flow_kg_s / ρ)·...
            // With brine density ≈ 1000 kg/m³, 1 L ≈ 1 kg, so mg/L ≈ mg/kg:
            //   element kg/s = concentration[mg/kg] × 1e-6 × flow[kg/s] × recovery
            var rate = concentration_mg_L * 1e-6 * pt.FlowRate_kg_s * res.RecoveryFactor;
            res.Rate_kg_s.Add(rate);

            var dtYears = Math.Max(0.0, pt.TimeYears - prevYears);
            prevYears = pt.TimeYears;
            var dtSeconds = dtYears * 365.25 * 86400.0;
            var prevCum = res.Cumulative_tonnes.Count > 0 ? res.Cumulative_tonnes[^1] : 0.0;
            res.Cumulative_tonnes.Add(prevCum + rate * dtSeconds / 1000.0); // kg → tonnes
        }
        return res;
    }

    /// <summary>Precipitate a supersaturated mineral toward equilibrium; returns mol removed per kg water.</summary>
    private double PrecipitateToEquilibrium(ThermodynamicState state, string mineral, double index, double extentScale = 1.0)
        => ReactivePoreNetworkCoupling.PrecipitateToEquilibrium(state, mineral, index, _library, _generator, extentScale);
}
