// GAIA.GeoGenesis/Reactor/VirtualReactor.cs
//
// The GeoGenesis virtual reactor: advances a 3-D grid of aqueous cells through time, injecting
// fluids, transporting (advection–dispersion, optionally retarded by sorption), and at every cell
// evaluating aqueous speciation + mineral saturation and precipitating supersaturated phases where
// nucleation is allowed (at seed sites or homogeneously). It records each tracked variable per frame
// so the evolution can be played back in 3-D. Cell updates are parallelised; the whole run executes
// off the UI thread.
//
// Chemistry reuses the validated GeoGenesis core (ThermodynamicSolver activities + saturation index,
// ReactionGenerator dissolution stoichiometry, the carbonate-system speciation in WaterComposition).
// pH is treated as buffered (held at the configured value) so dissolved mass bookkeeping stays in
// the user's tracked species — the standard simplification for an interactive reactor sandbox.

using GAIA.GeoGenesis.Contaminants;
using GAIA.GeoGenesis.Materials;
using GAIA.GeoGenesis.Thermodynamics;

namespace GAIA.GeoGenesis.Reactor;

public sealed class VirtualReactor
{
    private readonly CompoundLibrary _library;
    private readonly ReactionGenerator _generator;

    public VirtualReactor(CompoundLibrary? library = null)
    {
        _library = library ?? CompoundLibrary.Instance;
        _generator = new ReactionGenerator(_library);
    }

    public ReactorResult Run(ReactorConfig cfg, IProgress<double>? progress = null, CancellationToken ct = default,
        IProgress<ReactorProgressFrame>? frameProgress = null)
    {
        int nx = Math.Max(1, cfg.Nx), ny = Math.Max(1, cfg.Ny), nz = Math.Max(1, cfg.Nz);
        int n = nx * ny * nz;
        int Idx(int x, int y, int z) => x + nx * (y + ny * z);

        // --- Field allocation ---------------------------------------------------------------
        // Aqueous species fields (primary species the user works with), seeded from the base solution.
        var aqueous = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var sp in CollectPrimarySpecies(cfg))
            aqueous[sp] = Filled(n, (float)cfg.BaseSolution.GetValueOrDefault(sp));

        var minerals = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in cfg.TrackedMinerals) minerals[ResolveName(m)] = new float[n];

        // Nucleation masks per mineral.
        var nucleation = new Dictionary<string, bool[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in minerals.Keys)
        {
            var mask = new bool[n];
            if (cfg.AllowNucleationEverywhere) Array.Fill(mask, true);
            nucleation[m] = mask;
        }
        foreach (var site in cfg.NucleationSites)
        {
            var mineral = ResolveName(site.Mineral);
            if (!nucleation.TryGetValue(mineral, out var mask)) continue;
            for (int z = 0; z < nz; z++)
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                        if (IsInsideNucleationSite(x, y, z, site))
                            mask[Idx(x, y, z)] = true;
        }

        var siField = minerals.Keys.ToDictionary(m => m, _ => new float[n], StringComparer.OrdinalIgnoreCase);
        var ionicField = new float[n];
        var pressureField = new float[n];
        var temperatureField = new float[n];
        cfg.Petrophysics.SyntheticPoreNetwork ??= GenerateSyntheticPoreNetwork(cfg.Petrophysics.DefaultPorosity, cfg.Petrophysics.DefaultPermeability_m2, cfg.Petrophysics.DefaultTortuosity, nx, ny, nz, cfg.Petrophysics.SyntheticPoreSeed, cfg.Petrophysics.ConnectedPoreFraction);
        var porosityField = BuildPorosityField(cfg, n, nx, ny, nz);
        var permeabilityField = BuildPermeabilityField(cfg, porosityField);
        var tortuosityField = BuildTortuosityField(cfg, porosityField);
        var connectedPoreField = BuildPoreNetworkOccupancy(cfg, n, nx, ny, nz);
        var porePressureField = new float[n];
        var waterSaturation = Filled(n, (float)Math.Clamp(1.0 - cfg.Petrophysics.OilResidualSaturation, 0.0, 1.0));
        var oilSaturation = Filled(n, (float)Math.Clamp(cfg.Petrophysics.OilResidualSaturation, 0.0, 1.0));
        var fluxX = new float[n]; var fluxY = new float[n]; var fluxZ = new float[n];

        // Per-cell role + snapshot of the initial solution (re-imposed on Steady/Locked boundaries).
        var states = new byte[n];
        if (cfg.CellStates is { Length: > 0 } cs) Array.Copy(cs, states, Math.Min(cs.Length, n));
        RasterizeSolidObjects(cfg, states, porosityField, permeabilityField, tortuosityField, nucleation, nx, ny, nz);
        var initialAqueous = aqueous.ToDictionary(kv => kv.Key, kv => (float[])kv.Value.Clone(), StringComparer.OrdinalIgnoreCase);

        var result = new ReactorResult
        {
            Config = cfg, Nx = nx, Ny = ny, Nz = nz,
            Origin = new double[] { 0, 0, 0 },
            Spacing = new[] { cfg.SpacingX, cfg.SpacingY, cfg.SpacingZ }
        };

        // --- Time stepping ------------------------------------------------------------------
        bool hasFlow = Math.Abs(cfg.FlowVx) + Math.Abs(cfg.FlowVy) + Math.Abs(cfg.FlowVz) > 0;
        int frames = Math.Max(1, cfg.Frames);
        double frameDt = Math.Max(1e-6, cfg.FrameDt_days);

        // Frame 0 is the pristine initial state: evaluate saturation/ionic strength for display but
        // do not precipitate yet, so the evolution (mass leaving solution into minerals) is visible.
        ApplyBoundaries(states, aqueous, initialAqueous, n);
        UpdateReservoirPhysics(cfg, states, porosityField, permeabilityField, tortuosityField, connectedPoreField, minerals, pressureField, porePressureField, temperatureField, waterSaturation, oilSaturation, fluxX, fluxY, fluxZ, cfg.Temperature_K, cfg.Pressure_bar, 0d, nx, ny, nz);
        ReactCells(cfg, cfg.Temperature_K, cfg.Pressure_bar, 0d, states, aqueous, minerals, nucleation, siField, ionicField, nx, ny, nz, ct, allowPrecipitation: false);
        result.Frames.Add(Snapshot(cfg, aqueous, minerals, siField, ionicField, pressureField, porePressureField, temperatureField, porosityField, permeabilityField, tortuosityField, connectedPoreField, waterSaturation, oilSaturation, fluxX, fluxY, fluxZ, n, 0));
        ReportFrame(result.Frames[^1], 0, frames, nx, ny, nz, frameProgress);

        for (int f = 1; f < frames; f++)
        {
            ct.ThrowIfCancellationRequested();
            // Linear temperature / pressure ramp over the run (heating, cooling, (de)pressurisation).
            double frac = frames > 1 ? (double)f / (frames - 1) : 0;
            double curT = cfg.Temperature_K + frac * ((cfg.TemperatureEnd_K ?? cfg.Temperature_K) - cfg.Temperature_K);
            double curP = cfg.Pressure_bar + frac * ((cfg.PressureEnd_bar ?? cfg.Pressure_bar) - cfg.Pressure_bar);

            int sub = hasFlow ? Substeps(cfg, nx, ny, nz, frameDt) : 1;
            double dt = frameDt / sub;
            for (int s = 0; s < sub; s++)
            {
                double tNow = (f - 1) * frameDt + s * dt;
                ApplyForces(cfg, states, permeabilityField, fluxX, fluxY, fluxZ, nx, ny, nz, tNow);
                Inject(cfg, states, aqueous, Idx, dt, tNow);
                if (hasFlow || cfg.Forces.Count > 0) Transport(cfg, states, aqueous, permeabilityField, tortuosityField, fluxX, fluxY, fluxZ, nx, ny, nz, dt);
                if (cfg.EvaporationRate_per_day > 0) Evaporate(states, aqueous, n, cfg.EvaporationRate_per_day * dt);
                ApplyBoundaries(states, aqueous, initialAqueous, n);
                ReactCells(cfg, curT, curP, dt, states, aqueous, minerals, nucleation, siField, ionicField, nx, ny, nz, ct, allowPrecipitation: true);
                UpdateReservoirPhysics(cfg, states, porosityField, permeabilityField, tortuosityField, connectedPoreField, minerals, pressureField, porePressureField, temperatureField, waterSaturation, oilSaturation, fluxX, fluxY, fluxZ, curT, curP, dt, nx, ny, nz);
            }
            result.Frames.Add(Snapshot(cfg, aqueous, minerals, siField, ionicField, pressureField, porePressureField, temperatureField, porosityField, permeabilityField, tortuosityField, connectedPoreField, waterSaturation, oilSaturation, fluxX, fluxY, fluxZ, n, f * frameDt));
            ReportFrame(result.Frames[^1], f, frames, nx, ny, nz, frameProgress);
            progress?.Report(frames > 1 ? (double)f / (frames - 1) : 1.0);
        }

        result.Variables = result.Frames.Count > 0 ? result.Frames[0].Fields.Keys.OrderBy(k => k).ToList() : new List<string>();
        return result;
    }


    private static void ReportFrame(ReactorFrame frame, int frameIndex, int frameCount, int nx, int ny, int nz,
        IProgress<ReactorProgressFrame>? progress)
    {
        if (progress == null || frame.Fields.Count == 0) return;

        var chosen = frame.Fields
            .Select(kv => (kv.Key, Field: kv.Value, Stats: FieldStats(kv.Value)))
            .Where(x => float.IsFinite(x.Stats.Min) && float.IsFinite(x.Stats.Max))
            .OrderByDescending(x => x.Key.StartsWith("min:", StringComparison.OrdinalIgnoreCase) && x.Stats.Max > x.Stats.Min)
            .ThenByDescending(x => x.Key.StartsWith("SI:", StringComparison.OrdinalIgnoreCase) && x.Stats.Max > x.Stats.Min)
            .ThenByDescending(x => x.Stats.Max - x.Stats.Min)
            .FirstOrDefault();
        if (chosen.Field == null) return;

        progress.Report(new ReactorProgressFrame
        {
            FrameIndex = frameIndex,
            FrameCount = frameCount,
            Progress = frameCount > 1 ? (double)frameIndex / (frameCount - 1) : 1.0,
            TimeDays = frame.TimeDays,
            Variable = chosen.Key,
            Fields = frame.Fields.ToDictionary(kv => kv.Key, kv => (float[])kv.Value.Clone(), StringComparer.OrdinalIgnoreCase),
            Nx = nx,
            Ny = ny,
            Nz = nz,
            Field = (float[])chosen.Field.Clone(),
            FieldMin = chosen.Stats.Min,
            FieldMax = chosen.Stats.Max
        });
    }

    private static (float Min, float Max) FieldStats(float[] field)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in field)
        {
            if (!float.IsFinite(v)) continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (min == float.MaxValue) return (float.NaN, float.NaN);
        if (max <= min) max = min + 1e-6f;
        return (min, max);
    }

    // ----------------------------------------------------------------------------- reactions
    private void ReactCells(ReactorConfig cfg, double curT_K, double curP_bar, double dtDays, byte[] states,
        Dictionary<string, float[]> aqueous,
        Dictionary<string, float[]> minerals, Dictionary<string, bool[]> nucleation,
        Dictionary<string, float[]> siField, float[] ionicField, int nx, int ny, int nz, CancellationToken ct,
        bool allowPrecipitation = true)
    {
        int n = nx * ny * nz;
        var speciesNames = aqueous.Keys.ToArray();
        var mineralNames = minerals.Keys.ToArray();
        var previousMinerals = allowPrecipitation
            ? minerals.ToDictionary(kv => kv.Key, kv => (float[])kv.Value.Clone(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        int XOf(int idx) => idx % nx;
        int YOf(int idx) => (idx / nx) % ny;
        int ZOf(int idx) => idx / (nx * ny);

        // Generating a mineral's dissolution reaction (formula parsing + matrix balancing) is far too
        // expensive to repeat per cell. Do it ONCE per tracked mineral, here, and reuse the cached
        // reaction, ion-consumption map and (temperature-dependent) precipitation rate in every cell.
        var plans = new MineralPlan[mineralNames.Length];
        for (int m = 0; m < mineralNames.Length; m++)
        {
            var name = mineralNames[m];
            var compound = _library.Find(name);
            var reaction = compound != null ? _generator.GenerateSingleDissolutionReaction(compound) : null;
            plans[m] = new MineralPlan
            {
                Name = name,
                SiField = siField[name],
                MineralField = minerals[name],
                Nucleation = nucleation[name],
                Previous = previousMinerals.GetValueOrDefault(name),
                Compound = compound,
                Reaction = reaction,
                Consume = BuildConsumption(reaction),
                RatePerDay = compound != null ? EstimatePrecipitationRatePerDay(compound, curT_K) : 0.0
            };
        }

        // One ThermodynamicSolver per worker thread (its constructor loads the full Pitzer parameter
        // tables, so a fresh instance per cell would dominate the runtime). Reused across that
        // thread's cells via the thread-local state of Parallel.For.
        Parallel.For(0, n, new ParallelOptions { CancellationToken = ct },
            () => new ThermodynamicSolver(),
            (i, _, solver) =>
            {
                // Inactive / locked / outlet cells do not undergo chemistry.
                var st = (CellState)states[i];
                if (st is CellState.Inactive or CellState.Locked or CellState.Outlet)
                {
                    foreach (var plan in plans) plan.SiField[i] = float.NaN;
                    ionicField[i] = 0f;
                    return solver;
                }

                // Build this cell's solution from its primary species.
                var water = new WaterComposition { Temperature_K = curT_K, Pressure_bar = curP_bar, pH = cfg.pH };
                foreach (var sp in speciesNames)
                {
                    var v = aqueous[sp][i];
                    if (v > 0) water.Set(sp, v);
                }

                var state = water.ToState(_library, _generator);
                solver.ComputeActivities(state);
                ionicField[i] = (float)state.IonicStrength_molkg;

                foreach (var plan in plans)
                {
                    var si = plan.Reaction != null ? solver.SaturationIndex(state, plan.Reaction) : double.NaN;
                    plan.SiField[i] = (float)(double.IsFinite(si) ? si : double.NaN);

                    if (!allowPrecipitation) continue;
                    var previous = plan.Previous!;
                    bool present = previous[i] > 0;
                    bool seeded = plan.Nucleation[i];
                    var growthFactor = present || seeded
                        ? 1.0
                        : CrystalGrowthFactor(previous, XOf(i), YOf(i), ZOf(i), nx, ny, nz, plan.Compound);
                    if (growthFactor <= 0 || !(si > 0)) continue;

                    // Precipitate toward equilibrium, consuming the constituent ions from the cell.
                    var extent = PrecipitationExtent(plan, aqueous, i, si, dtDays, growthFactor);
                    if (extent > 0) plan.MineralField[i] += (float)extent;
                }

                return solver;
            },
            _ => { });
    }

    /// <summary>Per-mineral data computed once per step and reused across all cells (see <see cref="ReactCells"/>).</summary>
    private sealed class MineralPlan
    {
        public string Name = string.Empty;
        public float[] SiField = Array.Empty<float>();
        public float[] MineralField = Array.Empty<float>();
        public bool[] Nucleation = Array.Empty<bool>();
        public float[]? Previous;
        public ChemicalCompound? Compound;
        public ChemicalReaction? Reaction;
        /// <summary>Primary tracked species → moles consumed per unit precipitation extent.</summary>
        public Dictionary<string, double> Consume = new(StringComparer.OrdinalIgnoreCase);
        public double RatePerDay;
    }

    /// <summary>Map a dissolution reaction's products to the primary species consumed when it precipitates.</summary>
    private Dictionary<string, double> BuildConsumption(ChemicalReaction? reaction)
    {
        var consume = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (reaction == null) return consume;
        foreach (var (species, coeff) in reaction.Stoichiometry)
        {
            if (coeff <= 0) continue; // dissolution products = precipitation reactants
            var primary = MapToPrimary(species);
            if (primary == null) continue; // buffered (H+/OH-/H2O)
            consume[primary] = consume.GetValueOrDefault(primary) + coeff;
        }
        return consume;
    }

    /// <summary>Kinetic relaxation toward SI=0; consumes mapped primary ions from the cell. Returns mol/kgw precipitated.</summary>
    private double PrecipitationExtent(
        MineralPlan plan,
        Dictionary<string, float[]> aqueous,
        int i,
        double si,
        double dtDays,
        double growthFactor)
    {
        if (plan.Compound is not { Phase: CompoundPhase.Solid } || plan.Consume.Count == 0) return 0;

        // Limit extent by the scarcest reactant available in the cell.
        double maxExtent = double.MaxValue;
        foreach (var (sp, per) in plan.Consume)
        {
            if (!aqueous.TryGetValue(sp, out var field)) return 0; // a needed ion isn't in the system
            maxExtent = Math.Min(maxExtent, field[i] / per);
        }
        if (!double.IsFinite(maxExtent) || maxExtent <= 0) return 0;

        var drive = Math.Clamp(1.0 - Math.Pow(10.0, -si), 0.0, 0.95);
        var kinetic = 1.0 - Math.Exp(-plan.RatePerDay * Math.Max(1e-9, dtDays) * Math.Clamp(growthFactor, 0.05, 1.5));
        kinetic = Math.Clamp(kinetic, 0.0, 0.22);
        var extent = drive * kinetic * maxExtent;
        if (extent <= 0) return 0;

        foreach (var (sp, per) in plan.Consume)
            aqueous[sp][i] = (float)Math.Max(0.0, aqueous[sp][i] - per * extent);
        return extent;
    }

    private static double EstimatePrecipitationRatePerDay(ChemicalCompound compound, double temperatureK)
    {
        var k0 = compound.RateConstant_Precipitation_mol_m2_s
                 ?? compound.RateConstant_Dissolution_mol_m2_s;
        if (!k0.HasValue)
        {
            return 0.08;
        }

        var surfaceArea = Math.Max(0.01, compound.SpecificSurfaceArea_m2_g ?? 0.05);
        var molarMass = Math.Max(1.0, compound.MolecularWeight_g_mol ?? 100.0);
        var rate = k0.Value * surfaceArea * molarMass * 86400.0;

        var activation = compound.ActivationEnergy_Precipitation_kJ_mol
                         ?? compound.ActivationEnergy_Dissolution_kJ_mol;
        if (activation.HasValue)
        {
            const double r = 8.31446261815324;
            rate *= Math.Exp((-activation.Value * 1000.0 / r) * (1.0 / Math.Max(1.0, temperatureK) - 1.0 / 298.15));
        }

        return Math.Clamp(rate * 0.08, 0.015, 0.35);
    }

    private static bool IsInsideNucleationSite(int x, int y, int z, NucleationSite site)
    {
        var radius = Math.Max(0, site.Radius);
        var dx = x - site.X;
        var dy = y - site.Y;
        var dz = z - site.Z;
        return dx * dx + dy * dy + dz * dz <= radius * radius;
    }

    private static double CrystalGrowthFactor(
        float[] mineral,
        int x,
        int y,
        int z,
        int nx,
        int ny,
        int nz,
        ChemicalCompound? compound)
    {
        double best = 0d;
        void Check(int dx, int dy, int dz, double weight)
        {
            var xx = x + dx;
            var yy = y + dy;
            var zz = z + dz;
            if (xx < 0 || xx >= nx || yy < 0 || yy >= ny || zz < 0 || zz >= nz)
            {
                return;
            }

            var idx = xx + nx * (yy + ny * zz);
            if (mineral[idx] > 0)
            {
                best = Math.Max(best, weight);
            }
        }

        switch (compound?.CrystalSystem)
        {
            case CrystalSystem.Trigonal:
            case CrystalSystem.Hexagonal:
                Check(1, 0, 0, 1.00); Check(-1, 0, 0, 1.00);
                Check(0, 1, 0, 0.92); Check(0, -1, 0, 0.92);
                Check(1, -1, 0, 0.82); Check(-1, 1, 0, 0.82);
                Check(0, 0, 1, 0.45); Check(0, 0, -1, 0.45);
                break;
            case CrystalSystem.Cubic:
                Check(1, 0, 0, 1.00); Check(-1, 0, 0, 1.00);
                Check(0, 1, 0, 1.00); Check(0, -1, 0, 1.00);
                Check(0, 0, 1, 1.00); Check(0, 0, -1, 1.00);
                break;
            case CrystalSystem.Orthorhombic:
            case CrystalSystem.Tetragonal:
                Check(1, 0, 0, 1.00); Check(-1, 0, 0, 1.00);
                Check(0, 1, 0, 0.75); Check(0, -1, 0, 0.75);
                Check(0, 0, 1, 0.60); Check(0, 0, -1, 0.60);
                break;
            case CrystalSystem.Monoclinic:
            case CrystalSystem.Triclinic:
                Check(1, 0, 0, 1.00); Check(-1, 0, 0, 0.80);
                Check(0, 1, 0, 0.65); Check(0, -1, 0, 0.55);
                Check(0, 0, 1, 0.45); Check(0, 0, -1, 0.40);
                break;
            case CrystalSystem.Amorphous:
            case null:
            default:
                Check(1, 0, 0, 0.75); Check(-1, 0, 0, 0.75);
                Check(0, 1, 0, 0.75); Check(0, -1, 0, 0.75);
                Check(0, 0, 1, 0.75); Check(0, 0, -1, 0.75);
                break;
        }

        return best;
    }

    // ----------------------------------------------------------------------------- transport
    private void Inject(ReactorConfig cfg, byte[] states, Dictionary<string, float[]> aqueous, Func<int, int, int, int> idx, double dt, double timeDays)
    {
        foreach (var src in cfg.Sources)
        {
            if (!src.IsActiveAt(timeDays)) continue;
            if (src.X < 0 || src.X >= cfg.Nx || src.Y < 0 || src.Y >= cfg.Ny || src.Z < 0 || src.Z >= cfg.Nz) continue;
            int i = idx(src.X, src.Y, src.Z);
            if ((CellState)states[i] == CellState.Inactive) continue;
            foreach (var (sp, m) in src.Species)
            {
                var name = ResolveName(sp);
                if (!aqueous.TryGetValue(name, out var field)) continue;
                field[i] += (float)(m * src.Rate * dt);
            }
        }
    }

    /// <summary>Remove a fraction of pore water from Active cells, concentrating the dissolved species.</summary>
    private static void Evaporate(byte[] states, Dictionary<string, float[]> aqueous, int n, double removedFraction)
    {
        var factor = 1.0 / Math.Max(1e-6, 1.0 - Math.Clamp(removedFraction, 0.0, 0.99));
        for (int i = 0; i < n; i++)
        {
            if ((CellState)states[i] != CellState.Active) continue;
            foreach (var field in aqueous.Values) field[i] = (float)(field[i] * factor);
        }
    }

    /// <summary>Re-impose boundary conditions: Steady/Locked cells hold their initial solution; Outlet drains to zero.</summary>
    private static void ApplyBoundaries(byte[] states, Dictionary<string, float[]> aqueous, Dictionary<string, float[]> initial, int n)
    {
        for (int i = 0; i < n; i++)
        {
            switch ((CellState)states[i])
            {
                case CellState.Steady:
                case CellState.Locked:
                    foreach (var (sp, field) in aqueous) field[i] = initial[sp][i];
                    break;
                case CellState.Outlet:
                case CellState.Inactive:
                    foreach (var field in aqueous.Values) field[i] = 0f;
                    break;
            }
        }
    }

    private void Transport(ReactorConfig cfg, byte[] states, Dictionary<string, float[]> aqueous, float[] permeability, float[] tortuosity, float[] fluxX, float[] fluxY, float[] fluxZ, int nx, int ny, int nz, double dt)
    {
        double dx = Math.Max(cfg.SpacingX, 1e-9), dy = Math.Max(cfg.SpacingY, 1e-9), dz = nz > 1 ? Math.Max(cfg.SpacingZ, 1e-9) : double.PositiveInfinity;
        double baseSpeed = Math.Sqrt(cfg.FlowVx * cfg.FlowVx + cfg.FlowVy * cfg.FlowVy + cfg.FlowVz * cfg.FlowVz);
        double forcedSpeed = Math.Sqrt(fluxX.Select(v => (double)v * v).DefaultIfEmpty(0).Average() + fluxY.Select(v => (double)v * v).DefaultIfEmpty(0).Average() + fluxZ.Select(v => (double)v * v).DefaultIfEmpty(0).Average());
        double Dbase = cfg.Dispersivity * (baseSpeed + forcedSpeed) + 1e-6;
        int Idx(int x, int y, int z) => x + nx * (y + ny * z);

        foreach (var field in aqueous.Values)
        {
            var next = new float[field.Length];
            Parallel.For(0, nz, z =>
            {
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        int i = Idx(x, y, z);
                        double ci = field[i];
                        // Conservative donor-cell flux divergence (open boundaries) — see PlumeTransport.
                        if ((CellState)states[i] is CellState.Inactive or CellState.Locked) { next[i] = field[i]; continue; }
                        double vx = cfg.FlowVx + fluxX[i];
                        double vy = cfg.FlowVy + fluxY[i];
                        double vz = cfg.FlowVz + fluxZ[i];
                        double advDiv = 0;
                        advDiv += FluxDiv(field, Idx, x, nx, y, ny, z, nz, 0, vx) / dx;
                        advDiv += FluxDiv(field, Idx, x, nx, y, ny, z, nz, 1, vy) / dy;
                        if (nz > 1) advDiv += FluxDiv(field, Idx, x, nx, y, ny, z, nz, 2, vz) / dz;
                        double lap = (field[Idx(Math.Min(nx - 1, x + 1), y, z)] - 2 * ci + field[Idx(Math.Max(0, x - 1), y, z)]) / (dx * dx)
                                   + (field[Idx(x, Math.Min(ny - 1, y + 1), z)] - 2 * ci + field[Idx(x, Math.Max(0, y - 1), z)]) / (dy * dy);
                        double r = (cfg.Sorption?.RetardationFactor(ci) ?? 1.0) * Math.Max(1.0, tortuosity[i]);
                        double permScale = Math.Clamp(permeability[i] / Math.Max(1e-20, cfg.Petrophysics.DefaultPermeability_m2), 0.02, 50.0);
                        double D = Dbase / Math.Max(1.0, tortuosity[i]) * Math.Sqrt(permScale);
                        next[i] = (float)Math.Max(0.0, ci + dt / r * (-advDiv + D * lap));
                    }
            });
            Array.Copy(next, field, field.Length);
        }
    }

    private static float[] BuildPorosityField(ReactorConfig cfg, int n, int nx, int ny, int nz)
    {
        var field = Filled(n, (float)Math.Clamp(cfg.Petrophysics.DefaultPorosity, 0.01, 0.95));
        ApplySyntheticPorosityTexture(cfg, field, nx, ny, nz);
        if (cfg.Petrophysics.PorosityField is { Length: > 0 } p)
            Array.Copy(p, field, Math.Min(p.Length, n));
        if (cfg.Petrophysics.UseDelvePorosity && cfg.Petrophysics.PulsePorosityField is { Length: > 0 } pulse)
            for (int i = 0; i < Math.Min(pulse.Length, field.Length); i++)
                field[i] = (float)Math.Clamp(0.7 * field[i] + 0.3 * pulse[i], 0.005, 0.95);
        return field;
    }

    private static void ApplySyntheticPorosityTexture(ReactorConfig cfg, float[] field, int nx, int ny, int nz)
    {
        var p = cfg.Petrophysics;
        if (p.Heterogeneity <= 0) return;
        var rnd = new Random(p.SyntheticPoreSeed);
        var coarse = new float[Math.Max(1, nx * ny * nz)];
        for (int i = 0; i < coarse.Length; i++)
            coarse[i] = (float)(rnd.NextDouble() * 2.0 - 1.0);
        int Idx(int x, int y, int z) => x + nx * (y + ny * z);
        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    double sum = 0; int count = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy, zz = z + dz;
                                if (xx < 0 || xx >= nx || yy < 0 || yy >= ny || zz < 0 || zz >= nz) continue;
                                sum += coarse[Idx(xx, yy, zz)]; count++;
                            }
                    int i = Idx(x, y, z);
                    field[i] = (float)Math.Clamp(field[i] * (1.0 + p.Heterogeneity * sum / Math.Max(1, count)), 0.005, 0.95);
                }
    }

    private static float[] BuildPermeabilityField(ReactorConfig cfg, float[] porosity)
    {
        var field = new float[porosity.Length];
        double phi0 = Math.Max(0.01, cfg.Petrophysics.DefaultPorosity);
        for (int i = 0; i < field.Length; i++)
        {
            double phi = Math.Clamp(porosity[i], 0.01, 0.95);
            field[i] = (float)(cfg.Petrophysics.DefaultPermeability_m2 * Math.Pow(phi / phi0, 3.0) * Math.Pow((1 - phi0) / Math.Max(0.01, 1 - phi), 2.0));
        }
        return field;
    }

    private static float[] BuildTortuosityField(ReactorConfig cfg, float[] porosity)
    {
        var field = new float[porosity.Length];
        for (int i = 0; i < field.Length; i++)
            field[i] = (float)Math.Max(1.0, cfg.Petrophysics.DefaultTortuosity / Math.Sqrt(Math.Clamp(porosity[i], 0.01, 0.95) / Math.Max(0.01, cfg.Petrophysics.DefaultPorosity)));
        return field;
    }

    public static PoreNetworkModel GenerateSyntheticPoreNetwork(double porosity, double permeability_m2, double tortuosity, int nx, int ny, int nz)
        => GenerateSyntheticPoreNetwork(porosity, permeability_m2, tortuosity, nx, ny, nz, 1337, 0.78);

    public static PoreNetworkModel GenerateSyntheticPoreNetwork(double porosity, double permeability_m2, double tortuosity, int nx, int ny, int nz, int seed, double connectedFraction)
    {
        porosity = Math.Clamp(porosity, 0.01, 0.95);
        permeability_m2 = Math.Max(1e-20, permeability_m2);
        tortuosity = Math.Max(1.0, tortuosity);
        int cells = Math.Max(1, nx * ny * nz);
        var nodes = Math.Max(8, (int)Math.Round(cells * porosity));
        var connectivity = Math.Clamp(6.0 / tortuosity, 1.2, 12.0);
        var rnd = new Random(seed);
        var connected = Math.Clamp((int)Math.Round(nodes * Math.Clamp(connectedFraction, 0.05, 1.0)), 1, nodes);
        var model = new PoreNetworkModel
        {
            NodeCount = nodes,
            ThroatCount = Math.Max(nodes - 1, (int)Math.Round(nodes * connectivity * 0.5)),
            MeanPoreRadius_m = Math.Sqrt(8.0 * permeability_m2 * tortuosity / Math.Max(1e-9, porosity)),
            Connectivity = connectivity,
            Porosity = porosity,
            Permeability_m2 = permeability_m2,
            Tortuosity = tortuosity,
            ConnectedNodeCount = connected,
            DisconnectedNodeCount = nodes - connected
        };
        for (int i = 0; i < nodes; i++)
        {
            bool isConnected = i < connected;
            model.Nodes.Add(new PoreNode
            {
                Id = i,
                X = isConnected ? (int)Math.Round((nx - 1) * (i / Math.Max(1.0, connected - 1.0))) : rnd.Next(Math.Max(1, nx)),
                Y = rnd.Next(Math.Max(1, ny)),
                Z = rnd.Next(Math.Max(1, nz)),
                Radius_m = model.MeanPoreRadius_m * Math.Clamp(0.55 + rnd.NextDouble(), 0.35, 1.8),
                Connected = isConnected
            });
        }
        for (int i = 1; i < connected; i++) AddThroat(model, i - 1, i);
        while (model.Throats.Count < model.ThroatCount)
        {
            int a = rnd.Next(connected), b = rnd.Next(connected);
            if (a != b) AddThroat(model, a, b);
            else if (connected < nodes)
            {
                int d = connected + rnd.Next(nodes - connected);
                AddThroat(model, d, d);
            }
        }
        model.ThroatCount = model.Throats.Count;
        return model;
    }

    private static void AddThroat(PoreNetworkModel model, int a, int b)
    {
        var na = model.Nodes[Math.Clamp(a, 0, model.Nodes.Count - 1)];
        var nb = model.Nodes[Math.Clamp(b, 0, model.Nodes.Count - 1)];
        var r = Math.Min(na.Radius_m, nb.Radius_m) * 0.72;
        double dx = na.X - nb.X, dy = na.Y - nb.Y, dz = na.Z - nb.Z;
        var len = Math.Max(1.0, Math.Sqrt(dx * dx + dy * dy + dz * dz));
        model.Throats.Add(new PoreThroat { From = a, To = b, Radius_m = r, Length_m = len, Conductance_m3 = Math.PI * Math.Pow(r, 4) / (8.0 * len) });
    }

    private static float[] BuildPoreNetworkOccupancy(ReactorConfig cfg, int n, int nx, int ny, int nz)
    {
        var field = new float[n];
        int Idx(int x, int y, int z) => x + nx * (y + ny * z);
        foreach (var node in cfg.Petrophysics.SyntheticPoreNetwork?.Nodes ?? Enumerable.Empty<PoreNode>())
        {
            if (node.X < 0 || node.X >= nx || node.Y < 0 || node.Y >= ny || node.Z < 0 || node.Z >= nz) continue;
            field[Idx(node.X, node.Y, node.Z)] = node.Connected ? 1f : 0.35f;
        }
        return field;
    }

    private static void RasterizeSolidObjects(ReactorConfig cfg, byte[] states, float[] porosity, float[] permeability, float[] tortuosity, Dictionary<string, bool[]> nucleation, int nx, int ny, int nz)
    {
        int Idx(int x, int y, int z) => x + nx * (y + ny * z);
        foreach (var obj in cfg.SolidObjects)
        {
            int x1 = Math.Clamp(obj.X + Math.Max(1, obj.SizeX), 0, nx);
            int y1 = Math.Clamp(obj.Y + Math.Max(1, obj.SizeY), 0, ny);
            int z1 = Math.Clamp(obj.Z + Math.Max(1, obj.SizeZ), 0, nz);
            for (int z = Math.Clamp(obj.Z, 0, nz); z < z1; z++)
                for (int y = Math.Clamp(obj.Y, 0, ny); y < y1; y++)
                    for (int x = Math.Clamp(obj.X, 0, nx); x < x1; x++)
                    {
                        int i = Idx(x, y, z);
                        if (obj.Kind == ReactorObjectKind.InertSolid || obj.Kind == ReactorObjectKind.Mesh) states[i] = (byte)CellState.Inactive;
                        porosity[i] = (float)Math.Clamp(obj.Porosity, 0.0, 0.95);
                        permeability[i] = (float)Math.Max(1e-20, obj.Permeability_m2);
                        tortuosity[i] = (float)Math.Max(1.0, obj.Tortuosity);
                        if (obj.NucleationPoint)
                            foreach (var mask in nucleation.Values) mask[i] = true;
                    }
        }
        cfg.Petrophysics.SyntheticPoreNetwork ??= GenerateSyntheticPoreNetwork(cfg.Petrophysics.DefaultPorosity, cfg.Petrophysics.DefaultPermeability_m2, cfg.Petrophysics.DefaultTortuosity, nx, ny, nz, cfg.Petrophysics.SyntheticPoreSeed, cfg.Petrophysics.ConnectedPoreFraction);
    }

    private static void ApplyForces(ReactorConfig cfg, byte[] states, float[] permeability, float[] fluxX, float[] fluxY, float[] fluxZ, int nx, int ny, int nz, double timeDays)
    {
        Array.Clear(fluxX); Array.Clear(fluxY); Array.Clear(fluxZ);
        int Idx(int x, int y, int z) => x + nx * (y + ny * z);
        for (int z = 0; z < nz; z++) for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
        {
            int i = Idx(x, y, z);
            if ((CellState)states[i] is CellState.Inactive or CellState.Locked) continue;
            double vx = 0, vy = 0, vz = 0;
            foreach (var force in cfg.Forces)
            {
                double dx = x - force.X, dy = y - force.Y, dz = z - force.Z;
                double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                double influence = Math.Exp(-(r * r) / Math.Max(1e-6, force.RadiusCells * force.RadiusCells));
                double k = force.Strength * influence * Math.Sqrt(Math.Max(1e-20, permeability[i]) / Math.Max(1e-20, cfg.Petrophysics.DefaultPermeability_m2));
                switch (force.Kind)
                {
                    case ReactorForceKind.MixingWater:
                        vx += k * Math.Sin(timeDays + y * 0.37); vy += k * Math.Cos(timeDays + x * 0.31); break;
                    case ReactorForceKind.Vortex:
                        vx += -dy * k / Math.Max(1.0, r); vy += dx * k / Math.Max(1.0, r); vz += 0.15 * k * Math.Sin(r); break;
                    case ReactorForceKind.PressureGradient:
                    case ReactorForceKind.InletJet:
                        vx += k * force.DirectionX; vy += k * force.DirectionY; vz += k * force.DirectionZ; break;
                    case ReactorForceKind.Gravity:
                        vz -= Math.Abs(k); break;
                    case ReactorForceKind.EvaporationPull:
                        vz += Math.Abs(k); break;
                }
            }
            fluxX[i] = (float)vx; fluxY[i] = (float)vy; fluxZ[i] = (float)vz;
        }
    }

    private static void UpdateReservoirPhysics(ReactorConfig cfg, byte[] states, float[] porosity, float[] permeability, float[] tortuosity, float[] connectedPores, Dictionary<string, float[]> minerals, float[] pressure, float[] porePressure, float[] temperature, float[] waterSat, float[] oilSat, float[] fluxX, float[] fluxY, float[] fluxZ, double curT, double curP, double dt, int nx, int ny, int nz)
    {
        double maxMineral = minerals.Values.SelectMany(v => v).DefaultIfEmpty(0f).Max();
        for (int i = 0; i < pressure.Length; i++)
        {
            double mineralLoad = maxMineral > 0 ? minerals.Values.Sum(m => m[i]) / maxMineral : 0.0;
            double speed = Math.Sqrt(fluxX[i] * fluxX[i] + fluxY[i] * fluxY[i] + fluxZ[i] * fluxZ[i]);
            double injectedPressure = cfg.Petrophysics.InitialReservoirPressure_bar + speed * cfg.Petrophysics.WaterViscosityPaS * 8e5;
            double effectiveStress = Math.Max(0.0, cfg.Petrophysics.OverburdenPressure_bar - injectedPressure);
            double referenceStress = Math.Max(0.0, cfg.Petrophysics.OverburdenPressure_bar - cfg.Petrophysics.InitialReservoirPressure_bar);
            double compaction = cfg.Petrophysics.PoreCompressibility_1_per_bar * (effectiveStress - referenceStress);
            double reopening = cfg.Petrophysics.MatrixReopeningCoefficient * Math.Max(0.0, injectedPressure - cfg.Petrophysics.InitialReservoirPressure_bar) / Math.Max(1.0, referenceStress + 1.0);
            double chemistryScaling = mineralLoad * 0.002 * Math.Max(1.0, dt);
            porosity[i] = (float)Math.Clamp(porosity[i] - chemistryScaling - compaction + reopening * dt, 0.005, 0.95);
            permeability[i] = (float)Math.Max(1e-20, KozenyCarman(cfg.Petrophysics.DefaultPermeability_m2, cfg.Petrophysics.DefaultPorosity, porosity[i]) * (0.2 + 0.8 * Math.Max(connectedPores[i], 0.05f)));
            tortuosity[i] = (float)Math.Max(1.0, cfg.Petrophysics.DefaultTortuosity / Math.Sqrt(Math.Max(0.01, porosity[i])));
            pressure[i] = (float)(curP + 1e-5 * speed / Math.Max(1e-20, permeability[i]));
            porePressure[i] = (float)injectedPressure;
            temperature[i] = (float)(curT - 273.15 + cfg.EvaporationRate_per_day * 2.0);
            if ((CellState)states[i] == CellState.Inactive) { waterSat[i] = 0; oilSat[i] = 0; continue; }
            waterSat[i] = (float)Math.Clamp(waterSat[i] + speed * dt * 0.01, 0.0, 1.0);
            oilSat[i] = (float)Math.Clamp(1.0 - waterSat[i], 0.0, cfg.Petrophysics.OilResidualSaturation);
        }
    }

    private static double KozenyCarman(double k0, double phi0, double phi)
    {
        phi0 = Math.Clamp(phi0, 0.01, 0.95);
        phi = Math.Clamp(phi, 0.005, 0.95);
        return k0 * Math.Pow(phi / phi0, 3.0) * Math.Pow((1.0 - phi0) / Math.Max(0.01, 1.0 - phi), 2.0);
    }

    private static double FluxDiv(float[] c, Func<int, int, int, int> Idx, int x, int nx, int y, int ny, int z, int nz, int axis, double v)
    {
        if (v == 0) return 0;
        double ci = c[Idx(x, y, z)];
        int hi = axis == 0 ? nx : axis == 1 ? ny : nz;
        int p = axis == 0 ? x : axis == 1 ? y : z;
        double cPlus = p + 1 < hi ? c[Idx(axis == 0 ? x + 1 : x, axis == 1 ? y + 1 : y, axis == 2 ? z + 1 : z)] : 0.0;
        double cMinus = p - 1 >= 0 ? c[Idx(axis == 0 ? x - 1 : x, axis == 1 ? y - 1 : y, axis == 2 ? z - 1 : z)] : 0.0;
        double fRight = v >= 0 ? v * ci : v * cPlus;
        double fLeft = v >= 0 ? v * cMinus : v * ci;
        return fRight - fLeft;
    }

    private static int Substeps(ReactorConfig cfg, int nx, int ny, int nz, double frameDt)
    {
        double dx = Math.Max(cfg.SpacingX, 1e-9), dy = Math.Max(cfg.SpacingY, 1e-9), dz = nz > 1 ? Math.Max(cfg.SpacingZ, 1e-9) : 1e9;
        double cfl = Math.Abs(cfg.FlowVx) / dx + Math.Abs(cfg.FlowVy) / dy + Math.Abs(cfg.FlowVz) / dz;
        double dtMax = cfl > 0 ? 0.4 / cfl : frameDt;
        return Math.Clamp((int)Math.Ceiling(frameDt / dtMax), 1, 200);
    }

    // ----------------------------------------------------------------------------- helpers
    private ReactorFrame Snapshot(ReactorConfig cfg, Dictionary<string, float[]> aqueous,
        Dictionary<string, float[]> minerals, Dictionary<string, float[]> siField, float[] ionicField,
        float[] pressureField, float[] porePressureField, float[] temperatureField, float[] porosityField, float[] permeabilityField, float[] tortuosityField,
        float[] connectedPoreField, float[] waterSaturation, float[] oilSaturation, float[] fluxX, float[] fluxY, float[] fluxZ, int n, double timeDays)
    {
        var frame = new ReactorFrame { TimeDays = timeDays };
        foreach (var (sp, field) in aqueous) frame.Fields[$"aq:{sp}"] = (float[])field.Clone();
        foreach (var (m, field) in minerals) frame.Fields[$"min:{m}"] = (float[])field.Clone();
        foreach (var (m, field) in siField) frame.Fields[$"SI:{m}"] = (float[])field.Clone();
        frame.Fields["I"] = (float[])ionicField.Clone();
        frame.Fields["pH"] = Filled(n, (float)cfg.pH);
        frame.Fields["pressure"] = (float[])pressureField.Clone();
        frame.Fields["pore_pressure"] = (float[])porePressureField.Clone();
        frame.Fields["temperature"] = (float[])temperatureField.Clone();
        frame.Fields["porosity"] = (float[])porosityField.Clone();
        frame.Fields["permeability"] = (float[])permeabilityField.Clone();
        frame.Fields["tortuosity"] = (float[])tortuosityField.Clone();
        frame.Fields["pnm:connected_pores"] = (float[])connectedPoreField.Clone();
        frame.Fields["pnm:pressure"] = (float[])porePressureField.Clone();
        frame.Fields["pnm:permeability"] = (float[])permeabilityField.Clone();
        frame.Fields["water_saturation"] = (float[])waterSaturation.Clone();
        frame.Fields["oil_saturation"] = (float[])oilSaturation.Clone();
        frame.Fields["flux:x"] = (float[])fluxX.Clone();
        frame.Fields["flux:y"] = (float[])fluxY.Clone();
        frame.Fields["flux:z"] = (float[])fluxZ.Clone();
        frame.Fields["diffusion:ionic"] = Filled(n, (float)(1e-9 / Math.Max(1.0, cfg.Petrophysics.DefaultTortuosity)));
        AddProbeSeries(cfg, frame, n);
        return frame;
    }

    private static void AddProbeSeries(ReactorConfig cfg, ReactorFrame frame, int n)
    {
        foreach (var probe in cfg.Probes)
        {
            int idx = probe.X + Math.Max(1, cfg.Nx) * (probe.Y + Math.Max(1, cfg.Ny) * probe.Z);
            if ((uint)idx >= (uint)n) continue;
            foreach (var variable in probe.Variables.Distinct(StringComparer.OrdinalIgnoreCase))
                if (frame.Fields.TryGetValue(variable, out var field))
                    frame.Fields[$"probe:{probe.Name}:{variable}"] = Filled(n, field[idx]);
        }
    }

    /// <summary>All primary aqueous species the run needs: base + sources + tracked-mineral dissolution products.</summary>
    private IEnumerable<string> CollectPrimarySpecies(ReactorConfig cfg)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in cfg.BaseSolution.Keys) set.Add(ResolveName(k));
        foreach (var s in cfg.Sources) foreach (var k in s.Species.Keys) set.Add(ResolveName(k));
        foreach (var m in cfg.TrackedMinerals)
        {
            var comp = _library.Find(m);
            if (comp == null) continue;
            var rxn = _generator.GenerateSingleDissolutionReaction(comp);
            if (rxn == null) continue;
            foreach (var (sp, coeff) in rxn.Stoichiometry)
            {
                if (coeff <= 0) continue;
                var primary = MapToPrimary(sp);
                if (primary != null) set.Add(primary);
            }
        }
        return set;
    }

    /// <summary>Map a dissolution product to the primary tracked species that carries it.</summary>
    private string? MapToPrimary(string species)
    {
        var c = _library.Find(species);
        var name = c?.Name ?? species;
        switch (name)
        {
            case "Carbonate":
            case "Aqueous Carbon Dioxide":
                return "Bicarbonate"; // total dissolved inorganic carbon carrier
            case "Proton":
            case "Hydroxide":
            case "Water":
                return null; // pH is buffered; do not track H/OH/water mass
            default:
                return name;
        }
    }

    private string ResolveName(string nameOrFormula) => _library.Find(nameOrFormula)?.Name ?? nameOrFormula;

    private static float[] Filled(int n, float v)
    {
        var a = new float[n];
        if (v != 0) Array.Fill(a, v);
        return a;
    }
}
