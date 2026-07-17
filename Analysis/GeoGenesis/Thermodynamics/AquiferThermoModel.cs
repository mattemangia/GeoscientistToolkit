// GAIA.GeoGenesis/Thermodynamics/AquiferThermoModel.cs
//
// Bridges a segmented aquifer/reservoir model (e.g. CRAFT segments in PRISM) to GeoGenesis
// thermodynamics. Each segment carries petrophysics — porosity, permeability, bulk density,
// lithology — which are mapped to a representative mineral assemblage and a reactive surface area,
// then equilibrated with a brine to assess dissolution/precipitation (mineral formation) per segment.
//
// Lithology→mineralogy assemblages follow standard sedimentary/igneous petrology (e.g. Tucker,
// 2001, "Sedimentary Petrology"; Deer, Howie & Zussman). Reactive surface area uses the
// geometric/specific-surface relation A = SSA·ρ_bulk·(1−φ)·V (Steefel & Lasaga, 1994).

using GAIA.GeoGenesis.Materials;
using GAIA.GeoGenesis.Reactor;

namespace GAIA.GeoGenesis.Thermodynamics;

/// <summary>Petrophysical description of one aquifer/reservoir segment.</summary>
public sealed class AquiferSegment
{
    public string Name { get; set; } = "Segment";
    public string LithologyCode { get; set; } = string.Empty;
    public double Porosity { get; set; } = 0.2;            // fraction
    public double Permeability_m2 { get; set; } = 1e-13;
    public double BulkDensity_kg_m3 { get; set; } = 2400;
    public double Temperature_K { get; set; } = 298.15;
    public double Pressure_bar { get; set; } = 1.0;
    public double Volume_m3 { get; set; } = 1.0;
    /// <summary>Optional explicit mineral list; if empty it is inferred from LithologyCode.</summary>
    public List<string> Minerals { get; } = new();

    /// <summary>
    ///     Optional pore network representing the segment (e.g. simulated in the GeoGenesis reactor).
    ///     When set, <see cref="AquiferThermoModel.EvaluateSegment"/> also precipitates the
    ///     supersaturated host minerals to equilibrium in the pore brine and reports the resulting
    ///     clogging (evolved porosity/permeability/tortuosity).
    /// </summary>
    public PoreNetworkModel? PoreNetwork { get; set; }

    /// <summary>
    ///     Optional fracture pressure (bar) of the segment. When set and the segment pressure exceeds
    ///     it, overpressure fracture damage reopens the coupled pore network (competing with the
    ///     precipitation clogging). Null ⇒ no fracturing considered.
    /// </summary>
    public double? FracturePressure_bar { get; set; }
}

/// <summary>Per-segment thermodynamic assessment result.</summary>
public sealed class AquiferSegmentResult
{
    public string Segment { get; init; } = string.Empty;
    public double Temperature_K { get; init; }
    public double pH { get; init; }
    public double IonicStrength_molkg { get; init; }
    public List<string> Minerals { get; init; } = new();
    /// <summary>Saturation index per host-rock mineral (SI&gt;0 ⇒ precipitation, &lt;0 ⇒ dissolution).</summary>
    public Dictionary<string, double> SaturationIndices { get; init; } = new();
    /// <summary>Reactive mineral surface area (m²) in the segment, from SSA·ρ_bulk·(1−φ)·V.</summary>
    public double ReactiveSurfaceArea_m2 { get; init; }
    /// <summary>Pore volume (m³) = φ·V, i.e. the brine volume available for reaction.</summary>
    public double PoreVolume_m3 { get; init; }

    /// <summary>Mass (kg) precipitated per supersaturated host mineral when a pore network is coupled.</summary>
    public Dictionary<string, double> PrecipitatedMass_kg { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Initial pore-network state; null when the segment carries no pore network.</summary>
    public ReactivePnmState? PoreNetworkInitial { get; init; }
    /// <summary>Pore-network state after precipitating to equilibrium; null when no pore network.</summary>
    public ReactivePnmState? PoreNetworkAfterPrecipitation { get; init; }
    /// <summary>k/k0 due to precipitation clogging (1 when no pore network or nothing precipitates).</summary>
    public double PermeabilityRatio =>
        PoreNetworkInitial is { Permeability_m2: > 0 } i && PoreNetworkAfterPrecipitation is { } a
            ? a.Permeability_m2 / i.Permeability_m2 : 1.0;
}

public sealed class AquiferThermoModel
{
    private readonly CompoundLibrary _library;
    private readonly ThermodynamicSolver _solver;
    private readonly ReactionGenerator _generator;

    public AquiferThermoModel(CompoundLibrary? library = null)
    {
        _library = library ?? CompoundLibrary.Instance;
        _solver = new ThermodynamicSolver();
        _generator = new ReactionGenerator(_library);
    }

    /// <summary>
    ///     Representative mineral assemblage for a lithology code (USCS / common rock names).
    ///     Returns library mineral names; unknown codes fall back to a quartz-dominated clastic.
    /// </summary>
    public IReadOnlyList<string> MineralsForLithology(string lithologyCode)
    {
        var code = (lithologyCode ?? string.Empty).Trim().ToLowerInvariant();
        bool Has(params string[] keys) => keys.Any(k => code.Contains(k));

        if (Has("limestone", "calc", "chalk", "carbonate")) return Keep("Calcite", "Dolomite");
        if (Has("dolomite", "dolostone")) return Keep("Dolomite", "Calcite");
        if (Has("sandstone", "sand", "ss", "quartzite", "sp", "sw", "sm")) return Keep("Quartz", "Calcite", "Kaolinite", "Albite");
        if (Has("shale", "clay", "mud", "cl", "ml", "marl")) return Keep("Kaolinite", "Illite", "Quartz", "Calcite");
        if (Has("evaporite", "gypsum", "anhydrite", "salt", "halite")) return Keep("Gypsum", "Anhydrite", "Halite", "Calcite");
        if (Has("basalt", "mafic", "volcanic")) return Keep("Albite", "Anorthite", "Quartz", "Calcite");
        if (Has("granite", "felsic", "gneiss")) return Keep("Quartz", "Albite", "K-Feldspar", "Muscovite");
        // default clastic aquifer
        return Keep("Quartz", "Calcite", "Kaolinite");
    }

    /// <summary>Filter to minerals that actually exist in the library.</summary>
    private List<string> Keep(params string[] names) => names.Where(n => _library.Find(n) != null).ToList();

    /// <summary>
    ///     Equilibrate the segment's brine at the segment (T,P) and report saturation indices for the
    ///     host-rock minerals, plus the reactive surface area and pore volume. SI tells whether each
    ///     mineral tends to dissolve (rock weathering) or precipitate (cementation/mineral formation).
    /// </summary>
    public AquiferSegmentResult EvaluateSegment(AquiferSegment segment, WaterComposition brine)
    {
        var minerals = segment.Minerals.Count > 0 ? segment.Minerals : MineralsForLithology(segment.LithologyCode).ToList();

        var state = brine.ToState(_library, _generator);
        state.Temperature_K = segment.Temperature_K;
        state.Pressure_bar = segment.Pressure_bar;
        _solver.ComputeActivities(state);

        var tracked = new Dictionary<string, double>();
        foreach (var m in minerals)
            tracked[m] = _solver.SaturationIndex(state, m);

        // Optional pore-network coupling: precipitate every supersaturated host mineral to
        // equilibrium in the pore brine (mass of water = φ·V·ρw) and clog the network with the
        // deposited volume. SI-only behaviour is unchanged when no pore network is attached.
        var precipitated = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        ReactivePnmState? pnmInitial = null, pnmAfter = null;
        if (segment.PoreNetwork is { } pnm)
        {
            pnmInitial = ReactivePoreNetworkCoupling.InitialState(pnm);
            var waterMass_kg = segment.Porosity * segment.Volume_m3 * 1000.0; // ρw ≈ 1000 kg/m³
            foreach (var m in minerals)
            {
                if (!tracked.TryGetValue(m, out var si) || !double.IsFinite(si) || si <= 0) continue;
                var mol_per_kgw = ReactivePoreNetworkCoupling.PrecipitateToEquilibrium(state, m, si, _library, _generator);
                if (mol_per_kgw <= 0) continue;
                var mw = _library.Find(m)?.MolecularWeight_g_mol ?? 0.0;
                precipitated[m] = mol_per_kgw * waterMass_kg * mw / 1000.0; // mol/kgw → kg mineral
            }
            var vfrac = ReactivePoreNetworkCoupling.PrecipitateVolumeFraction(_library, precipitated, segment.Volume_m3);
            var damage = segment.FracturePressure_bar is { } pFrac
                ? ReactivePoreNetworkCoupling.FractureDamageFromPressure(segment.Pressure_bar, pFrac)
                : 0.0;
            pnmAfter = ReactivePoreNetworkCoupling.Evolve(pnm, vfrac, damage);
        }

        // Reactive surface area: average specific surface area of the host minerals (m²/g) times
        // grain mass = ρ_bulk·(1−φ)·V. Convert g↔kg with ×1000.
        var ssaValues = minerals
            .Select(m => _library.Find(m)?.SpecificSurfaceArea_m2_g)
            .Where(v => v is > 0).Select(v => v!.Value).ToList();
        var ssa = ssaValues.Count > 0 ? ssaValues.Average() : 0.1; // m²/g typical default
        var grainMass_g = segment.BulkDensity_kg_m3 * (1.0 - segment.Porosity) * segment.Volume_m3 * 1000.0;
        var area = ssa * grainMass_g;

        return new AquiferSegmentResult
        {
            Segment = segment.Name,
            Temperature_K = segment.Temperature_K,
            pH = state.pH,
            IonicStrength_molkg = state.IonicStrength_molkg,
            Minerals = minerals,
            SaturationIndices = tracked,
            ReactiveSurfaceArea_m2 = area,
            PoreVolume_m3 = segment.Porosity * segment.Volume_m3,
            PrecipitatedMass_kg = precipitated,
            PoreNetworkInitial = pnmInitial,
            PoreNetworkAfterPrecipitation = pnmAfter
        };
    }
}
