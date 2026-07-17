// GAIA.GeoGenesis/Reactor/ReactorModels.cs
//
// Data model for the GeoGenesis virtual 3-D reactor "sandbox": a regular grid of aqueous cells in
// which the user dissolves a base solution, injects fluids at points, places nucleation sites for
// minerals, lets phases precipitate / crystallise and (optionally) flow, and records the evolution
// of any chosen variable for 3-D playback. Kept free of UI/PRISM dependencies so it can be unit
// tested, scripted from the CLI, and run on a background thread.

using System.Text.Json.Serialization;

using GAIA.GeoGenesis.Contaminants;

namespace GAIA.GeoGenesis.Reactor;

/// <summary>A fluid injected into the reactor at a grid cell (adds species over time).</summary>

public enum ReactorForceKind { MixingWater, Vortex, PressureGradient, Gravity, InletJet, EvaporationPull }

public sealed class ReactorForce
{
    public string Name { get; set; } = "force";
    public ReactorForceKind Kind { get; set; } = ReactorForceKind.MixingWater;
    public double Strength { get; set; } = 1.0;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double DirectionX { get; set; } = 1.0;
    public double DirectionY { get; set; }
    public double DirectionZ { get; set; }
    public double RadiusCells { get; set; } = 6.0;
}

public enum ReactorObjectKind { InertSolid, Rock, Mesh, PoreNetworkRock }

public sealed class ReactorSolidObject
{
    public string Name { get; set; } = "solid";
    public ReactorObjectKind Kind { get; set; } = ReactorObjectKind.InertSolid;
    public string Material { get; set; } = "Quartz";
    public string Composition { get; set; } = string.Empty;
    public string MeshPath { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int SizeX { get; set; } = 2;
    public int SizeY { get; set; } = 2;
    public int SizeZ { get; set; } = 2;
    public bool SubjectToForces { get; set; }
    public bool NucleationPoint { get; set; }
    public double Porosity { get; set; } = 0.2;
    public double Permeability_m2 { get; set; } = 1e-13;
    public double Tortuosity { get; set; } = 2.0;
}

public sealed class PoreNetworkModel
{
    public int NodeCount { get; set; }
    public int ThroatCount { get; set; }
    public double MeanPoreRadius_m { get; set; }
    public double Connectivity { get; set; }
    public double Porosity { get; set; }
    public double Permeability_m2 { get; set; }
    public double Tortuosity { get; set; }
    public int ConnectedNodeCount { get; set; }
    public int DisconnectedNodeCount { get; set; }
    public List<PoreNode> Nodes { get; set; } = new();
    public List<PoreThroat> Throats { get; set; } = new();
}

public sealed class PoreNode
{
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public double Radius_m { get; set; }
    public bool Connected { get; set; }
    public double Pressure_bar { get; set; }
}

public sealed class PoreThroat
{
    public int From { get; set; }
    public int To { get; set; }
    public double Radius_m { get; set; }
    public double Length_m { get; set; } = 1.0;
    public double Conductance_m3 { get; set; }
}

public sealed class ReactorPetrophysics
{
    public bool UseDelvePorosity { get; set; }
    public double DefaultPorosity { get; set; } = 0.25;
    public double DefaultPermeability_m2 { get; set; } = 1e-13;
    public double DefaultTortuosity { get; set; } = 2.0;
    public double OilResidualSaturation { get; set; } = 0.15;
    public double InitialReservoirPressure_bar { get; set; } = 50.0;
    public double OverburdenPressure_bar { get; set; } = 120.0;
    public double PoreCompressibility_1_per_bar { get; set; } = 2e-4;
    public double MatrixReopeningCoefficient { get; set; } = 0.35;
    public double WaterViscosityPaS { get; set; } = 1e-3;
    public double OilViscosityPaS { get; set; } = 5e-3;
    public int SyntheticPoreSeed { get; set; } = 1337;
    public double Heterogeneity { get; set; } = 0.22;
    public double ConnectedPoreFraction { get; set; } = 0.78;
    public float[]? PorosityField { get; set; }
    public float[]? PulsePorosityField { get; set; }
    public PoreNetworkModel? SyntheticPoreNetwork { get; set; }
}

public sealed class ReactorProbe
{
    public string Name { get; set; } = "probe";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public List<string> Variables { get; set; } = new() { "pressure", "temperature", "water_saturation" };
}

public sealed class FluidSource
{
    public string Name { get; set; } = "inlet";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    /// <summary>Species → molality added per day at this source (scaled by <see cref="Rate"/>).</summary>
    public Dictionary<string, double> Species { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Injection rate multiplier (per day).</summary>
    public double Rate { get; set; } = 1.0;
    public double Temperature_K { get; set; } = 298.15;

    /// <summary>Simulation time (days) at which this solution starts being injected. 0 ⇒ from the start.</summary>
    public double StartDay { get; set; }
    /// <summary>Simulation time (days) at which injection stops. Null ⇒ inject until the run ends.</summary>
    public double? EndDay { get; set; }

    /// <summary>True while the source is injecting at the given simulation time.</summary>
    public bool IsActiveAt(double timeDays)
        => timeDays >= StartDay - 1e-9 && (EndDay is not { } end || timeDays < end - 1e-9);
}

/// <summary>
/// Per-cell role in the reactor domain.
///   Active   – normal cell: reacts and (if flow) transports.
///   Inactive – not part of the domain (a hole / wall): no reaction, no through-flow.
///   Steady   – fixed-composition boundary: re-imposed to its initial solution every step.
///   Locked   – composition frozen (neither reacts nor changes by transport).
///   Outlet   – open drain: dissolved mass leaves the domain here (held at zero).
/// </summary>
public enum CellState : byte { Active = 0, Inactive = 1, Steady = 2, Locked = 3, Outlet = 4 }

/// <summary>A nucleation site where a mineral is allowed to nucleate (precipitate) once supersaturated.</summary>
public sealed class NucleationSite
{
    public string Name { get; set; } = "seed";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Mineral { get; set; } = string.Empty;
    /// <summary>Radius (in cells) around the site where nucleation is enabled.</summary>
    public int Radius { get; set; } = 1;
}

/// <summary>Full reactor configuration (initial state, inputs, what to simulate and for how long).</summary>
public sealed class ReactorConfig
{
    public int Nx { get; set; } = 24;
    public int Ny { get; set; } = 24;
    public int Nz { get; set; } = 1;
    public double SpacingX { get; set; } = 1.0;
    public double SpacingY { get; set; } = 1.0;
    public double SpacingZ { get; set; } = 1.0;

    /// <summary>Base (initial) solution that fills every cell: species → molality (mol/kgw).</summary>
    public Dictionary<string, double> BaseSolution { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public double Temperature_K { get; set; } = 298.15;
    public double Pressure_bar { get; set; } = 1.0;
    public double pH { get; set; } = 7.0;

    /// <summary>Optional end temperature (K): if set, T ramps linearly start→end over the run (heating/cooling).</summary>
    public double? TemperatureEnd_K { get; set; }
    /// <summary>Optional end pressure (bar): if set, P ramps linearly start→end over the run.</summary>
    public double? PressureEnd_bar { get; set; }

    /// <summary>Evaporation rate as a fraction of pore water removed per day (concentrates solutes; drives evaporites).</summary>
    public double EvaporationRate_per_day { get; set; }

    /// <summary>Optional per-cell role (length Nx·Ny·Nz). Null ⇒ every cell is Active.</summary>
    public byte[]? CellStates { get; set; }

    public List<FluidSource> Sources { get; set; } = new();
    public List<NucleationSite> NucleationSites { get; set; } = new();
    public List<string> TrackedMinerals { get; set; } = new();

    /// <summary>If true minerals may nucleate in any supersaturated cell (homogeneous nucleation).</summary>
    public bool AllowNucleationEverywhere { get; set; }

    // Optional regional flow (m/day) advecting the dissolved species across the grid.
    public double FlowVx { get; set; }
    public double FlowVy { get; set; }
    public double FlowVz { get; set; }
    public double Dispersivity { get; set; }

    public List<ReactorForce> Forces { get; set; } = new();
    public List<ReactorSolidObject> SolidObjects { get; set; } = new();
    public ReactorPetrophysics Petrophysics { get; set; } = new();
    public List<ReactorProbe> Probes { get; set; } = new();

    public int Frames { get; set; } = 12;
    public double FrameDt_days { get; set; } = 1.0;

    /// <summary>Optional soil/matrix sorption applied to dissolved species during transport.</summary>
    public SorptionModel? Sorption { get; set; }
}

/// <summary>Ready-to-run reactor scenarios. Values remain editable after loading in the UI.</summary>
public static class ReactorPresets
{
    /// <summary>
    /// Modern marine water evaporated at 25 °C in a closed, static basin. The candidate phases
    /// cover the usual carbonate, sulfate, halide and terminal K-Mg evaporite assemblage; their
    /// saturation and precipitation order are calculated by the reactor and are not prescribed.
    /// </summary>
    public static ReactorConfig CreateMarineEvaporation()
    {
        var config = new ReactorConfig
        {
            Nx = 12, Ny = 12, Nz = 5,
            SpacingX = 1.0, SpacingY = 1.0, SpacingZ = 1.0,
            Temperature_K = 298.15, Pressure_bar = 1.0, pH = 8.1,
            EvaporationRate_per_day = 0.25,
            Frames = 20, FrameDt_days = 1.0,
            AllowNucleationEverywhere = false,
            Dispersivity = 0
        };

        // Major-ion composition of IAPSO Standard Seawater — the Reference Composition of Millero,
        // Feistel, Wright & McDougall (2008) / IAPWS-08, at Reference Salinity S_R = 35.16504 g/kg.
        // Values are molalities (mol per kg of water); the seven majors below carry > 99.5 % of the
        // sea-salt and sum to the reference total molality of 1.1606 mol/kg. The trace constituents
        // (Sr2+, Br-, F-, borate) are omitted as they do not affect the tracked evaporite phases.
        config.BaseSolution = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sodium Ion"] = 0.48606,
            ["Chloride Ion"] = 0.565765,
            ["Magnesium Ion"] = 0.054742,
            ["Sulfate Ion"] = 0.029264,
            ["Calcium Ion"] = 0.010657,
            ["Potassium Ion"] = 0.010580,
            ["Bicarbonate"] = 0.0017803
        };

        config.TrackedMinerals = new List<string> { "Calcite", "Gypsum", "Halite", "Carnallite" };

        // Separate seeds make each phase front legible in the 3-D reactor view.
        config.NucleationSites = new List<NucleationSite>
        {
            new() { Name = "carbonate seed", X = 3, Y = 3, Z = 1, Radius = 1, Mineral = "Calcite" },
            new() { Name = "gypsum seed", X = 8, Y = 3, Z = 2, Radius = 1, Mineral = "Gypsum" },
            new() { Name = "halite seed", X = 5, Y = 8, Z = 1, Radius = 1, Mineral = "Halite" },
            new() { Name = "bittern seed", X = 8, Y = 8, Z = 3, Radius = 1, Mineral = "Carnallite" }
        };
        return config;
    }

    /// <summary>
    /// Calcite scale formation by RETROGRADE solubility: a calcium–bicarbonate water heated from
    /// 25 °C to 90 °C (a geothermal / production well). Calcite is LESS soluble hot, so it
    /// supersaturates and scales as the brine warms. A seed marks where scale nucleates; the amount
    /// and timing are computed from the saturation state, not prescribed.
    /// Refs: Plummer &amp; Busenberg (1982); standard well-scaling geochemistry.
    /// </summary>
    public static ReactorConfig CreateCalciteScaling()
    {
        var c = new ReactorConfig
        {
            Nx = 8, Ny = 8, Nz = 1, SpacingX = 1, SpacingY = 1, SpacingZ = 1,
            Temperature_K = 298.15, TemperatureEnd_K = 363.15, Pressure_bar = 1.0, pH = 7.8,
            Frames = 16, FrameDt_days = 1.0, AllowNucleationEverywhere = false,
            BaseSolution = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Calcium Ion"] = 0.012, ["Bicarbonate"] = 0.024, ["Sodium Ion"] = 0.05, ["Chloride Ion"] = 0.05
            },
            TrackedMinerals = new() { "Calcite" },
            NucleationSites = new() { new() { Name = "scale seed", X = 4, Y = 4, Z = 0, Radius = 1, Mineral = "Calcite" } }
        };
        return c;
    }

    /// <summary>
    /// Gypsum (and a little halite) precipitating from an evaporating calcium-sulfate brine — a
    /// coastal sabkha / evaporation pond. As pore water evaporates the solution passes gypsum
    /// saturation and CaSO4·2H2O precipitates first, halite only when much more concentrated.
    /// Refs: Langmuir (1997); Usiglio evaporite sequence.
    /// </summary>
    public static ReactorConfig CreateGypsumPrecipitation()
    {
        var c = new ReactorConfig
        {
            Nx = 10, Ny = 10, Nz = 1, SpacingX = 1, SpacingY = 1, SpacingZ = 1,
            Temperature_K = 298.15, Pressure_bar = 1.0, pH = 7.5, EvaporationRate_per_day = 0.2,
            Frames = 18, FrameDt_days = 1.0, AllowNucleationEverywhere = false,
            BaseSolution = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Calcium Ion"] = 0.03, ["Sulfate Ion"] = 0.03, ["Sodium Ion"] = 0.12, ["Chloride Ion"] = 0.12
            },
            TrackedMinerals = new() { "Gypsum", "Halite" },
            NucleationSites = new()
            {
                new() { Name = "gypsum seed", X = 3, Y = 5, Z = 0, Radius = 1, Mineral = "Gypsum" },
                new() { Name = "halite seed", X = 7, Y = 5, Z = 0, Radius = 1, Mineral = "Halite" }
            }
        };
        return c;
    }
}

/// <summary>One recorded time step: each tracked variable as a flat scalar field over the grid.</summary>
public sealed class ReactorFrame
{
    public double TimeDays { get; set; }
    /// <summary>Variable name (e.g. "aq:Calcium Ion", "min:Calcite", "SI:Calcite", "pH", "I") → field.</summary>
    public Dictionary<string, float[]> Fields { get; set; } = new();
}

/// <summary>Live-progress payload emitted after each recorded reactor frame.</summary>
public sealed class ReactorProgressFrame
{
    public int FrameIndex { get; set; }
    public int FrameCount { get; set; }
    public double Progress { get; set; }
    public double TimeDays { get; set; }
    public string Variable { get; set; } = string.Empty;
    public Dictionary<string, float[]> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Nx { get; set; }
    public int Ny { get; set; }
    public int Nz { get; set; }
    public float[] Field { get; set; } = Array.Empty<float>();
    public float FieldMin { get; set; }
    public float FieldMax { get; set; }
}

/// <summary>The reactor configuration plus the recorded evolution; serialisable for save/reload.</summary>
public sealed class ReactorResult
{
    public ReactorConfig Config { get; set; } = new();
    public int Nx { get; set; }
    public int Ny { get; set; }
    public int Nz { get; set; }
    public double[] Origin { get; set; } = { 0, 0, 0 };
    public double[] Spacing { get; set; } = { 1, 1, 1 };
    public List<string> Variables { get; set; } = new();
    public List<ReactorFrame> Frames { get; set; } = new();

    [JsonIgnore] public int CellCount => Nx * Ny * Nz;
    public int Index(int x, int y, int z) => x + Nx * (y + Ny * z);
    public (double X, double Y, double Z) NodePosition(int x, int y, int z)
        => (Origin[0] + x * Spacing[0], Origin[1] + y * Spacing[1], Origin[2] + z * Spacing[2]);

    /// <summary>
    /// Per-frame convergence residual of one variable: the relative L2 change between consecutive
    /// frames (‖Fₜ − Fₜ₋₁‖ / ‖Fₜ‖). Frame 0 is 0 by definition. Trends toward zero as the reactor
    /// approaches a steady state; it is the curve shown in the live view and written to the
    /// persistent convergence log.
    /// </summary>
    public float[] ConvergenceSeries(string variable)
    {
        var series = new float[Frames.Count];
        float[]? prev = null;
        for (int f = 0; f < Frames.Count; f++)
        {
            if (!Frames[f].Fields.TryGetValue(variable, out var cur)) { series[f] = 0f; prev = null; continue; }
            series[f] = prev == null ? 0f : RelativeChange(cur, prev);
            prev = cur;
        }
        return series;
    }

    private static float RelativeChange(float[] cur, float[] prev)
    {
        double num = 0, den = 0;
        int n = Math.Min(cur.Length, prev.Length);
        for (int i = 0; i < n; i++)
        {
            if (!float.IsFinite(cur[i]) || !float.IsFinite(prev[i])) continue;
            double d = cur[i] - prev[i];
            num += d * d;
            den += (double)cur[i] * cur[i];
        }
        if (den <= 0) return num > 0 ? 1f : 0f;
        return (float)Math.Sqrt(num / den);
    }
}
