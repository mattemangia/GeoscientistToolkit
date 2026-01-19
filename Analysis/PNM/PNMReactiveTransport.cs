// GeoscientistToolkit/Analysis/PNM/PNMReactiveTransport.cs
//
// Reactive transport solver for Pore Network Models
// Couples flow, heat transfer, species transport, and thermodynamic reactions

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;
using ChemicalCompound = GeoscientistToolkit.Data.Materials.ChemicalCompound;
using CompoundLibrary = GeoscientistToolkit.Data.Materials.CompoundLibrary;
using CompoundPhase = GeoscientistToolkit.Data.Materials.CompoundPhase;

namespace GeoscientistToolkit.Analysis.Pnm;

/// <summary>
/// Options for PNM reactive transport simulation
/// </summary>
public sealed class PNMReactiveTransportOptions
{
    // Simulation time control
    public double TotalTime { get; set; } = 3600.0; // seconds
    public double TimeStep { get; set; } = 1.0; // seconds
    public double OutputInterval { get; set; } = 60.0; // seconds

    // Convergence control
    public float ConvergenceTolerance { get; set; } = 1e-6f;

    // Flow parameters
    public FlowAxis FlowAxis { get; set; } = FlowAxis.Z;
    public float InletPressure { get; set; } = 1.0f; // Pa
    public float OutletPressure { get; set; } = 0.0f; // Pa
    public float FluidViscosity { get; set; } = 1.0f; // cP
    public float FluidDensity { get; set; } = 1000f; // kg/m³

    // Heat transfer parameters
    public float InletTemperature { get; set; } = 298.15f; // K (25°C)
    public float OutletTemperature { get; set; } = 298.15f; // K
    public float ThermalConductivity { get; set; } = 0.6f; // W/(m·K) for water
    public float SpecificHeat { get; set; } = 4184f; // J/(kg·K) for water

    // Transport parameters
    public float MolecularDiffusivity { get; set; } = 2.299e-9f; // m²/s (Na+ in water)
    public float Dispersivity { get; set; } = 0.1f; // m

    // Initial conditions
    public Dictionary<string, float> InitialConcentrations { get; set; } = new();
    public Dictionary<string, float> InletConcentrations { get; set; } = new();
    public Dictionary<string, float> InitialMinerals { get; set; } = new();

    // Reaction parameters
    public bool EnableReactions { get; set; } = true;
    public List<string> ReactionMinerals { get; set; } = new();

    // Geometry update parameters
    public bool UpdateGeometry { get; set; } = true;
    public float MinPoreRadius { get; set; } = 0.1f; // Minimum pore radius (voxels)
    public float MinThroatRadius { get; set; } = 0.05f; // Minimum throat radius (voxels)
}

/// <summary>
/// State for PNM reactive transport at a given time
/// </summary>
public sealed class PNMReactiveTransportState
{
    public double CurrentTime { get; set; }

    // Per-pore fields
    public Dictionary<int, float> PorePressures { get; set; } = new();
    public Dictionary<int, float> PoreTemperatures { get; set; } = new();
    public Dictionary<int, Dictionary<string, float>> PoreConcentrations { get; set; } = new();
    public Dictionary<int, Dictionary<string, float>> PoreMinerals { get; set; } = new();
    public Dictionary<int, float> PoreRadii { get; set; } = new(); // Current radii (may change due to precipitation)
    public Dictionary<int, float> PoreVolumes { get; set; } = new(); // Current volumes

    // Per-throat fields
    public Dictionary<int, float> ThroatFlowRates { get; set; } = new();
    public Dictionary<int, float> ThroatRadii { get; set; } = new(); // Current radii
    public Dictionary<int, float> ThroatHeatFluxes { get; set; } = new();

    // Computed properties
    public float CurrentPermeability { get; set; }
    public Dictionary<int, float> ReactionRates { get; set; } = new(); // Per pore

    public PNMReactiveTransportState Clone()
    {
        return new PNMReactiveTransportState
        {
            CurrentTime = CurrentTime,
            PorePressures = new Dictionary<int, float>(PorePressures),
            PoreTemperatures = new Dictionary<int, float>(PoreTemperatures),
            PoreConcentrations = PoreConcentrations.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, float>(kvp.Value)
            ),
            PoreMinerals = PoreMinerals.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, float>(kvp.Value)
            ),
            PoreRadii = new Dictionary<int, float>(PoreRadii),
            PoreVolumes = new Dictionary<int, float>(PoreVolumes),
            ThroatFlowRates = new Dictionary<int, float>(ThroatFlowRates),
            ThroatRadii = new Dictionary<int, float>(ThroatRadii),
            ThroatHeatFluxes = new Dictionary<int, float>(ThroatHeatFluxes),
            CurrentPermeability = CurrentPermeability,
            ReactionRates = new Dictionary<int, float>(ReactionRates)
        };
    }
}

/// <summary>
/// Results from PNM reactive transport simulation
/// </summary>
public sealed class PNMReactiveTransportResults
{
    public List<PNMReactiveTransportState> TimeSteps { get; set; } = new();
    public TimeSpan ComputationTime { get; set; }
    public int TotalIterations { get; set; }
    public bool Converged { get; set; }

    // Summary statistics
    public Dictionary<string, float> FinalMineralVolumes { get; set; } = new();
    public float InitialPermeability { get; set; }
    public float FinalPermeability { get; set; }
    public float PermeabilityChange { get; set; }
}

/// <summary>
/// Reactive transport solver for Pore Network Models.
/// Couples flow, heat, transport, and reactions in pore-scale simulations.
/// </summary>
public static class PNMReactiveTransport
{
    /// <summary>
    /// Run reactive transport simulation on a PNM
    /// </summary>
    public static PNMReactiveTransportResults Solve(
        PNMDataset pnm,
        PNMReactiveTransportOptions options,
        IProgress<(float progress, string message)> progress = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Logger.Log("[PNMReactiveTransport] Starting simulation...");
        Logger.Log($"[PNMReactiveTransport] Total time: {options.TotalTime} s, dt: {options.TimeStep} s");

        var results = new PNMReactiveTransportResults();

        // Initialize state
        var state = InitializeState(pnm, options);
        results.TimeSteps.Add(state.Clone());

        // Calculate initial permeability
        var initialPerm = CalculatePermeability(pnm, state, options);
        results.InitialPermeability = initialPerm;
        state.CurrentPermeability = initialPerm;
        Logger.Log($"[PNMReactiveTransport] Initial permeability: {initialPerm:E3} mD");

        // Time stepping
        double t = 0;
        int step = 0;
        int outputCount = 0;

        while (t < options.TotalTime)
        {
            step++;
            t += options.TimeStep;
            state.CurrentTime = t;

            var progressFraction = (float)(t / options.TotalTime);
            progress?.Report((progressFraction, $"Step {step}: t = {t:F1} s"));

            try
            {
                // 1. Solve flow (pressure field)
                SolveFlow(pnm, state, options);

                // 2. Solve heat transfer (temperature field)
                SolveHeat(pnm, state, options);

                // 3. Solve species transport (advection-diffusion)
                SolveTransport(pnm, state, options);

                // 4. Solve reactions and update minerals
                if (options.EnableReactions)
                {
                    SolveReactions(pnm, state, options);
                }

                // 5. Update pore/throat geometry from mineral precipitation
                if (options.UpdateGeometry)
                {
                    UpdateGeometry(pnm, state, options);

                    // Recalculate permeability
                    state.CurrentPermeability = CalculatePermeability(pnm, state, options);
                }

                // Save output
                if (t >= outputCount * options.OutputInterval)
                {
                    results.TimeSteps.Add(state.Clone());
                    outputCount++;

                    Logger.Log($"[PNMReactiveTransport] t={t:F1} s: K={state.CurrentPermeability:E3} mD");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PNMReactiveTransport] Error at t={t:F1} s: {ex.Message}");
                results.Converged = false;
                break;
            }
        }

        results.FinalPermeability = state.CurrentPermeability;
        results.PermeabilityChange = (results.FinalPermeability - results.InitialPermeability) / results.InitialPermeability;
        results.TotalIterations = step;
        results.Converged = true;

        stopwatch.Stop();
        results.ComputationTime = stopwatch.Elapsed;

        Logger.Log($"[PNMReactiveTransport] Simulation complete in {stopwatch.Elapsed.TotalSeconds:F1} s");
        Logger.Log($"[PNMReactiveTransport] Permeability change: {results.PermeabilityChange:P2}");

        return results;
    }

    private static PNMReactiveTransportState InitializeState(PNMDataset pnm, PNMReactiveTransportOptions options)
    {
        var state = new PNMReactiveTransportState();

        // Initialize pore properties
        foreach (var pore in pnm.Pores)
        {
            state.PorePressures[pore.ID] = (options.InletPressure + options.OutletPressure) / 2f;
            state.PoreTemperatures[pore.ID] = options.InletTemperature;
            state.PoreRadii[pore.ID] = pore.Radius;
            state.PoreVolumes[pore.ID] = pore.VolumePhysical;

            // Initialize concentrations
            state.PoreConcentrations[pore.ID] = new Dictionary<string, float>();
            foreach (var kvp in options.InitialConcentrations)
            {
                state.PoreConcentrations[pore.ID][kvp.Key] = kvp.Value;
            }

            // Initialize minerals
            state.PoreMinerals[pore.ID] = new Dictionary<string, float>();
            foreach (var kvp in options.InitialMinerals)
            {
                state.PoreMinerals[pore.ID][kvp.Key] = kvp.Value;
            }

            state.ReactionRates[pore.ID] = 0;
        }

        // Initialize throat properties
        foreach (var throat in pnm.Throats)
        {
            state.ThroatRadii[throat.ID] = throat.Radius;
            state.ThroatFlowRates[throat.ID] = 0;
            state.ThroatHeatFluxes[throat.ID] = 0;
        }

        Logger.Log($"[PNMReactiveTransport] Initialized {state.PorePressures.Count} pores and {state.ThroatRadii.Count} throats");

        return state;
    }

    private static void SolveFlow(PNMDataset pnm, PNMReactiveTransportState state, PNMReactiveTransportOptions options)
    {
        // Build flow system using current geometry
        var voxelSize_m = pnm.VoxelSize * 1e-6f;
        var viscosity_PaS = options.FluidViscosity * 0.001f;

        // Get inlet/outlet pores
        var (inlets, outlets) = GetBoundaryPores(pnm, state, options.FlowAxis);

        // Build conductance matrix
        var maxId = pnm.Pores.Max(p => p.ID);
        var matrix = new SparseMatrix(maxId + 1);
        var b = new float[maxId + 1];

        var poreMap = pnm.Pores.ToDictionary(p => p.ID);

        foreach (var throat in pnm.Throats)
        {
            if (!poreMap.ContainsKey(throat.Pore1ID) || !poreMap.ContainsKey(throat.Pore2ID))
                continue;

            var p1 = poreMap[throat.Pore1ID];
            var p2 = poreMap[throat.Pore2ID];

            // Get current radii (may have changed due to precipitation)
            var r_t = state.ThroatRadii[throat.ID] * voxelSize_m;
            if (r_t <= 0) continue; // Throat is closed

            // Calculate throat length
            var length = Vector3.Distance(p1.Position, p2.Position) * voxelSize_m;
            if (length < 1e-12f) length = voxelSize_m;

            // Hagen-Poiseuille conductance
            var conductance = (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * length);

            // Add to matrix
            matrix.Add(p1.ID, p1.ID, conductance);
            matrix.Add(p2.ID, p2.ID, conductance);
            matrix.Add(p1.ID, p2.ID, -conductance);
            matrix.Add(p2.ID, p1.ID, -conductance);
        }

        // Apply boundary conditions
        foreach (var id in inlets)
        {
            matrix.ClearRow(id);
            matrix.Set(id, id, 1.0f);
            b[id] = options.InletPressure;
        }

        foreach (var id in outlets)
        {
            matrix.ClearRow(id);
            matrix.Set(id, id, 1.0f);
            b[id] = options.OutletPressure;
        }

        // Solve for pressures
        var pressures = SolveCG(matrix, b, options.ConvergenceTolerance);

        // Update state
        foreach (var pore in pnm.Pores)
        {
            if (pore.ID < pressures.Length)
                state.PorePressures[pore.ID] = pressures[pore.ID];
        }

        // Calculate throat flow rates
        foreach (var throat in pnm.Throats)
        {
            if (!poreMap.ContainsKey(throat.Pore1ID) || !poreMap.ContainsKey(throat.Pore2ID))
                continue;

            var p1 = poreMap[throat.Pore1ID];
            var p2 = poreMap[throat.Pore2ID];

            var r_t = state.ThroatRadii[throat.ID] * voxelSize_m;
            if (r_t <= 0)
            {
                state.ThroatFlowRates[throat.ID] = 0;
                continue;
            }

            var length = Vector3.Distance(p1.Position, p2.Position) * voxelSize_m;
            if (length < 1e-12f) length = voxelSize_m;

            var conductance = (float)(Math.PI * Math.Pow(r_t, 4)) / (8 * viscosity_PaS * length);
            var deltaP = state.PorePressures[p1.ID] - state.PorePressures[p2.ID];

            state.ThroatFlowRates[throat.ID] = conductance * deltaP; // m³/s
        }
    }

    private static void SolveHeat(PNMDataset pnm, PNMReactiveTransportState state, PNMReactiveTransportOptions options)
    {
        // Heat transfer through PNM: advection + conduction
        var voxelSize_m = pnm.VoxelSize * 1e-6f;
        var dt = (float)options.TimeStep;

        var (inlets, outlets) = GetBoundaryPores(pnm, state, options.FlowAxis);
        var poreMap = pnm.Pores.ToDictionary(p => p.ID);

        // Create copy for updates
        var newTemps = new Dictionary<int, float>(state.PoreTemperatures);

        foreach (var pore in pnm.Pores)
        {
            if (inlets.Contains(pore.ID))
            {
                newTemps[pore.ID] = options.InletTemperature;
                continue;
            }

            if (outlets.Contains(pore.ID))
            {
                newTemps[pore.ID] = options.OutletTemperature;
                continue;
            }

            var T_pore = state.PoreTemperatures[pore.ID];
            var V_pore = state.PoreVolumes[pore.ID] * 1e-18f; // μm³ to m³
            var mass = V_pore * options.FluidDensity;

            float heatChange = 0;

            // Advective heat transfer through throats
            foreach (var throat in pnm.Throats)
            {
                if (throat.Pore1ID == pore.ID)
                {
                    var flowRate = state.ThroatFlowRates[throat.ID]; // m³/s
                    if (flowRate > 0) // Flow out of pore
                    {
                        heatChange -= flowRate * options.FluidDensity * options.SpecificHeat * T_pore * dt;
                    }
                    else if (flowRate < 0 && poreMap.ContainsKey(throat.Pore2ID)) // Flow into pore
                    {
                        var T_neighbor = state.PoreTemperatures[throat.Pore2ID];
                        heatChange -= flowRate * options.FluidDensity * options.SpecificHeat * T_neighbor * dt;
                    }
                }
                else if (throat.Pore2ID == pore.ID)
                {
                    var flowRate = -state.ThroatFlowRates[throat.ID]; // Reverse direction
                    if (flowRate > 0)
                    {
                        heatChange -= flowRate * options.FluidDensity * options.SpecificHeat * T_pore * dt;
                    }
                    else if (flowRate < 0 && poreMap.ContainsKey(throat.Pore1ID))
                    {
                        var T_neighbor = state.PoreTemperatures[throat.Pore1ID];
                        heatChange -= flowRate * options.FluidDensity * options.SpecificHeat * T_neighbor * dt;
                    }
                }
            }

            // Conductive heat transfer
            foreach (var throat in pnm.Throats)
            {
                int neighborId = -1;
                if (throat.Pore1ID == pore.ID && poreMap.ContainsKey(throat.Pore2ID))
                    neighborId = throat.Pore2ID;
                else if (throat.Pore2ID == pore.ID && poreMap.ContainsKey(throat.Pore1ID))
                    neighborId = throat.Pore1ID;

                if (neighborId >= 0)
                {
                    var T_neighbor = state.PoreTemperatures[neighborId];
                    var dT = T_neighbor - T_pore;

                    var p_neighbor = poreMap[neighborId];
                    var length = Vector3.Distance(pore.Position, p_neighbor.Position) * voxelSize_m;
                    if (length < 1e-12f) length = voxelSize_m;

                    var r_t = state.ThroatRadii[throat.ID] * voxelSize_m;
                    var area = (float)(Math.PI * r_t * r_t);

                    // Q = k * A * dT / L
                    var heatFlux = options.ThermalConductivity * area * dT / length;
                    heatChange += heatFlux * dt;

                    state.ThroatHeatFluxes[throat.ID] = heatFlux;
                }
            }

            // Update temperature
            if (mass > 0)
            {
                var dT = heatChange / (mass * options.SpecificHeat);
                newTemps[pore.ID] = T_pore + dT;

                // Clamp to reasonable range
                newTemps[pore.ID] = Math.Clamp(newTemps[pore.ID], 273.15f, 573.15f);
            }
        }

        // Apply updates
        foreach (var kvp in newTemps)
            state.PoreTemperatures[kvp.Key] = kvp.Value;
    }

    private static void SolveTransport(PNMDataset pnm, PNMReactiveTransportState state, PNMReactiveTransportOptions options)
    {
        // Species transport: advection + diffusion
        var voxelSize_m = pnm.VoxelSize * 1e-6f;
        var dt = (float)options.TimeStep;

        var (inlets, outlets) = GetBoundaryPores(pnm, state, options.FlowAxis);
        var poreMap = pnm.Pores.ToDictionary(p => p.ID);

        // Get list of all species
        var species = new HashSet<string>();
        foreach (var concentrations in state.PoreConcentrations.Values)
            foreach (var sp in concentrations.Keys)
                species.Add(sp);

        // Add inlet species
        foreach (var sp in options.InletConcentrations.Keys)
            species.Add(sp);

        // Transport each species
        foreach (var sp in species)
        {
            var newConc = new Dictionary<int, float>();

            foreach (var pore in pnm.Pores)
            {
                // Boundary conditions
                if (inlets.Contains(pore.ID))
                {
                    newConc[pore.ID] = options.InletConcentrations.GetValueOrDefault(sp, 0);
                    continue;
                }

                var C_pore = state.PoreConcentrations[pore.ID].GetValueOrDefault(sp, 0);
                var V_pore = state.PoreVolumes[pore.ID] * 1e-18f; // μm³ to m³

                float massChange = 0;

                // Advective transport through throats
                foreach (var throat in pnm.Throats)
                {
                    if (throat.Pore1ID == pore.ID)
                    {
                        var flowRate = state.ThroatFlowRates[throat.ID];
                        if (flowRate > 0) // Flow out
                        {
                            massChange -= flowRate * C_pore * dt;
                        }
                        else if (flowRate < 0 && poreMap.ContainsKey(throat.Pore2ID)) // Flow in
                        {
                            var C_neighbor = state.PoreConcentrations[throat.Pore2ID].GetValueOrDefault(sp, 0);
                            massChange -= flowRate * C_neighbor * dt;
                        }
                    }
                    else if (throat.Pore2ID == pore.ID)
                    {
                        var flowRate = -state.ThroatFlowRates[throat.ID];
                        if (flowRate > 0)
                        {
                            massChange -= flowRate * C_pore * dt;
                        }
                        else if (flowRate < 0 && poreMap.ContainsKey(throat.Pore1ID))
                        {
                            var C_neighbor = state.PoreConcentrations[throat.Pore1ID].GetValueOrDefault(sp, 0);
                            massChange -= flowRate * C_neighbor * dt;
                        }
                    }
                }

                // Diffusive transport
                foreach (var throat in pnm.Throats)
                {
                    int neighborId = -1;
                    if (throat.Pore1ID == pore.ID && poreMap.ContainsKey(throat.Pore2ID))
                        neighborId = throat.Pore2ID;
                    else if (throat.Pore2ID == pore.ID && poreMap.ContainsKey(throat.Pore1ID))
                        neighborId = throat.Pore1ID;

                    if (neighborId >= 0)
                    {
                        var C_neighbor = state.PoreConcentrations[neighborId].GetValueOrDefault(sp, 0);
                        var dC = C_neighbor - C_pore;

                        var p_neighbor = poreMap[neighborId];
                        var length = Vector3.Distance(pore.Position, p_neighbor.Position) * voxelSize_m;
                        if (length < 1e-12f) length = voxelSize_m;

                        var r_t = state.ThroatRadii[throat.ID] * voxelSize_m;
                        var area = (float)(Math.PI * r_t * r_t);

                        // J = -D * A * dC / L
                        var diffusiveFlux = options.MolecularDiffusivity * area * dC / length;
                        massChange += diffusiveFlux * dt;
                    }
                }

                // Update concentration
                if (V_pore > 0)
                {
                    var dC = massChange / V_pore;
                    newConc[pore.ID] = Math.Max(0, C_pore + dC); // Ensure non-negative
                }
                else
                {
                    newConc[pore.ID] = C_pore;
                }
            }

            // Apply updates
            foreach (var pore in pnm.Pores)
            {
                if (!state.PoreConcentrations.ContainsKey(pore.ID))
                    state.PoreConcentrations[pore.ID] = new Dictionary<string, float>();

                state.PoreConcentrations[pore.ID][sp] = newConc[pore.ID];
            }
        }
    }

    private static void SolveReactions(PNMDataset pnm, PNMReactiveTransportState state, PNMReactiveTransportOptions options)
    {
        var compoundLibrary = CompoundLibrary.Instance;
        var reactionGenerator = new ReactionGenerator(compoundLibrary);
        var kineticsSolver = new KineticsSolver();
        var reactionMinerals = new HashSet<string>(options.ReactionMinerals, StringComparer.OrdinalIgnoreCase);

        foreach (var pore in pnm.Pores)
        {
            var volume_um3 = state.PoreVolumes.GetValueOrDefault(pore.ID, pore.VolumePhysical);
            var volume_L = ConvertPoreVolumeToLiters(volume_um3);
            if (volume_L <= 0) continue;

            var thermoState = new ThermodynamicState
            {
                Temperature_K = state.PoreTemperatures[pore.ID],
                Pressure_bar = state.PorePressures[pore.ID] / 1e5,
                Volume_L = volume_L
            };

            var concentrationMap = state.PoreConcentrations[pore.ID];
            var inputSpeciesMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (speciesKey, concentration) in concentrationMap)
            {
                if (concentration <= 0) continue;
                var compound = compoundLibrary.FindFlexible(speciesKey);
                if (compound == null)
                {
                    Logger.LogWarning($"[PNMReactiveTransport] Unknown species '{speciesKey}' in pore {pore.ID}");
                    continue;
                }

                var moles = concentration * volume_L;
                AddCompoundToState(thermoState, reactionGenerator, compound, moles);
                inputSpeciesMap[compound.Name] = speciesKey;
            }

            var water = compoundLibrary.FindFlexible("H2O") ?? compoundLibrary.FindFlexible("Water");
            if (water != null && !thermoState.SpeciesMoles.ContainsKey(water.Name))
            {
                AddCompoundToState(thermoState, reactionGenerator, water, 55.508 * volume_L);
            }

            var mineralVolumes = state.PoreMinerals[pore.ID];
            foreach (var (mineralKey, mineralVolume_um3) in mineralVolumes)
            {
                if (mineralVolume_um3 <= 0) continue;
                if (reactionMinerals.Count > 0 && !reactionMinerals.Contains(mineralKey)) continue;

                var mineral = compoundLibrary.FindFlexible(mineralKey);
                if (mineral == null)
                {
                    Logger.LogWarning($"[PNMReactiveTransport] Unknown mineral '{mineralKey}' in pore {pore.ID}");
                    continue;
                }

                var molarVolume_m3 = ResolveMolarVolume_m3(mineral);
                if (molarVolume_m3 <= 0)
                {
                    Logger.LogError($"[PNMReactiveTransport] Missing molar volume for mineral '{mineral.Name}'");
                    continue;
                }

                var moles = mineralVolume_um3 * 1e-18 / molarVolume_m3;
                AddCompoundToState(thermoState, reactionGenerator, mineral, moles);
            }

            var reactions = reactionGenerator.GenerateReactions(thermoState);
            if (reactionMinerals.Count > 0)
            {
                reactions = reactions
                    .Where(r => r.Stoichiometry.Keys.Any(k => reactionMinerals.Contains(k)))
                    .ToList();
            }

            if (reactions.Count == 0) continue;

            var initialMineralMoles = CaptureMineralMoles(thermoState, compoundLibrary, reactionMinerals);
            var finalState = kineticsSolver.SolveKinetics(thermoState, options.TimeStep, options.TimeStep, reactions);
            var finalMineralMoles = CaptureMineralMoles(finalState, compoundLibrary, reactionMinerals);

            ApplyThermodynamicState(pore.ID, finalState, compoundLibrary, inputSpeciesMap, state);

            var deltaMoles = finalMineralMoles - initialMineralMoles;
            state.ReactionRates[pore.ID] = (float)(deltaMoles / options.TimeStep);
        }
    }

    private static void UpdateGeometry(PNMDataset pnm, PNMReactiveTransportState state, PNMReactiveTransportOptions options)
    {
        // Update pore and throat radii based on mineral precipitation
        var voxelSize = pnm.VoxelSize;

        foreach (var pore in pnm.Pores)
        {
            // Calculate total mineral volume in pore
            float totalMineralVolume = 0;
            if (state.PoreMinerals.ContainsKey(pore.ID))
            {
                foreach (var mineral in state.PoreMinerals[pore.ID].Values)
                    totalMineralVolume += mineral;
            }

            // Update pore volume (original - mineral volume)
            var originalVolume = pore.VolumePhysical;
            var newVolume = originalVolume - totalMineralVolume;
            newVolume = Math.Max(newVolume, originalVolume * 0.01f); // Ensure at least 1% remains

            state.PoreVolumes[pore.ID] = newVolume;

            // Update pore radius assuming spherical geometry
            // V = (4/3) * π * r³  =>  r = (3V / 4π)^(1/3)
            var newRadius_um = (float)Math.Pow(3 * newVolume / (4 * Math.PI), 1.0 / 3.0);
            var newRadius_vox = newRadius_um / voxelSize;
            newRadius_vox = Math.Max(newRadius_vox, options.MinPoreRadius);

            state.PoreRadii[pore.ID] = newRadius_vox;
        }

        // Update throat radii proportionally
        var poreMap = pnm.Pores.ToDictionary(p => p.ID);

        foreach (var throat in pnm.Throats)
        {
            if (!poreMap.ContainsKey(throat.Pore1ID) || !poreMap.ContainsKey(throat.Pore2ID))
                continue;

            var p1 = poreMap[throat.Pore1ID];
            var p2 = poreMap[throat.Pore2ID];

            // Throat radius scales with adjacent pore radii
            var r1_original = p1.Radius;
            var r2_original = p2.Radius;
            var r1_current = state.PoreRadii[p1.ID];
            var r2_current = state.PoreRadii[p2.ID];

            var scaleFactor = Math.Min(r1_current / r1_original, r2_current / r2_original);

            var newThroatRadius = throat.Radius * scaleFactor;
            newThroatRadius = Math.Max(newThroatRadius, options.MinThroatRadius);

            state.ThroatRadii[throat.ID] = newThroatRadius;
        }
    }

    private static float CalculatePermeability(PNMDataset pnm, PNMReactiveTransportState state, PNMReactiveTransportOptions options)
    {
        // Kozeny-Carman permeability estimate derived from porosity and specific surface area:
        // k = (1 / C) * (phi^3 / (Sv^2 * (1 - phi)^2)), with Kozeny constant C ≈ 5.

        if (pnm.Pores.Count == 0) return 0;

        var voxelSize_m = pnm.VoxelSize * 1e-6f;
        var poreVolume_m3 = 0.0;
        var poreSurfaceArea_m2 = 0.0;

        foreach (var pore in pnm.Pores)
        {
            var volumeVoxels = pore.VolumePhysical > 0
                ? pore.VolumePhysical / Math.Pow(pnm.VoxelSize, 3)
                : pore.VolumeVoxels;

            if (volumeVoxels > 0)
                poreVolume_m3 += volumeVoxels * voxelSize_m * voxelSize_m * voxelSize_m;

            var areaVoxels = pore.Area;
            if (areaVoxels <= 0 && pore.Radius > 0)
            {
                var radius_m = pore.Radius * voxelSize_m;
                poreSurfaceArea_m2 += 4.0 * Math.PI * radius_m * radius_m;
            }
            else if (areaVoxels > 0)
            {
                poreSurfaceArea_m2 += areaVoxels * voxelSize_m * voxelSize_m;
            }
        }

        var materialVolume_m3 = CalculateMaterialBoundingBoxVolume(pnm, voxelSize_m);
        if (materialVolume_m3 <= 0 || poreVolume_m3 <= 0 || poreSurfaceArea_m2 <= 0) return 0;

        var porosity = poreVolume_m3 / materialVolume_m3;
        porosity = Math.Clamp(porosity, 0.001, 0.99);

        var specificSurfaceArea = poreSurfaceArea_m2 / materialVolume_m3;
        if (specificSurfaceArea <= 0) return 0;

        const double kozenyConstant = 5.0;
        var k_m2 = (1.0 / kozenyConstant) *
                   (Math.Pow(porosity, 3) /
                    (specificSurfaceArea * specificSurfaceArea * Math.Pow(1.0 - porosity, 2)));

        return (float)(k_m2 / 9.869233e-16);
    }

    private static double CalculateMaterialBoundingBoxVolume(PNMDataset pnm, float voxelSize_m)
    {
        var minBounds = new Vector3(
            pnm.Pores.Min(p => p.Position.X),
            pnm.Pores.Min(p => p.Position.Y),
            pnm.Pores.Min(p => p.Position.Z));
        var maxBounds = new Vector3(
            pnm.Pores.Max(p => p.Position.X),
            pnm.Pores.Max(p => p.Position.Y),
            pnm.Pores.Max(p => p.Position.Z));

        var margin = pnm.MaxPoreRadius;
        var widthVoxels = maxBounds.X - minBounds.X + 2 * margin;
        var heightVoxels = maxBounds.Y - minBounds.Y + 2 * margin;
        var depthVoxels = maxBounds.Z - minBounds.Z + 2 * margin;

        return widthVoxels * heightVoxels * depthVoxels *
               voxelSize_m * voxelSize_m * voxelSize_m;
    }

    private static void ApplyThermodynamicState(int poreId, ThermodynamicState thermoState, CompoundLibrary library,
        Dictionary<string, string> inputSpeciesMap, PNMReactiveTransportState state)
    {
        var volume_L = thermoState.Volume_L;
        if (volume_L <= 0) return;

        if (!state.PoreConcentrations.ContainsKey(poreId))
            state.PoreConcentrations[poreId] = new Dictionary<string, float>();

        foreach (var (speciesName, moles) in thermoState.SpeciesMoles)
        {
            var compound = library.Find(speciesName);
            if (compound == null || compound.Phase != CompoundPhase.Aqueous) continue;

            var concentration = Math.Max(0.0, moles / volume_L);
            var key = inputSpeciesMap.GetValueOrDefault(speciesName, compound.ChemicalFormula);
            state.PoreConcentrations[poreId][key] = (float)concentration;
        }

        if (!state.PoreMinerals.ContainsKey(poreId))
            state.PoreMinerals[poreId] = new Dictionary<string, float>();

        var updatedMinerals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (speciesName, moles) in thermoState.SpeciesMoles)
        {
            var compound = library.Find(speciesName);
            if (compound == null || compound.Phase != CompoundPhase.Solid) continue;

            var molarVolume_m3 = ResolveMolarVolume_m3(compound);
            if (molarVolume_m3 <= 0)
            {
                Logger.LogError($"[PNMReactiveTransport] Missing molar volume for mineral '{compound.Name}'");
                continue;
            }

            var volume_um3 = Math.Max(0.0, moles * molarVolume_m3 * 1e18);
            state.PoreMinerals[poreId][compound.Name] = (float)volume_um3;
            updatedMinerals.Add(compound.Name);
        }

        var keys = state.PoreMinerals[poreId].Keys.ToList();
        foreach (var key in keys)
        {
            if (!updatedMinerals.Contains(key))
                state.PoreMinerals[poreId][key] = 0f;
        }
    }

    private static double CaptureMineralMoles(ThermodynamicState state, CompoundLibrary library,
        HashSet<string> reactionMinerals)
    {
        var total = 0.0;
        foreach (var (speciesName, moles) in state.SpeciesMoles)
        {
            var compound = library.Find(speciesName);
            if (compound == null || compound.Phase != CompoundPhase.Solid) continue;
            if (reactionMinerals.Count > 0 && !reactionMinerals.Contains(compound.Name)) continue;
            total += moles;
        }

        return total;
    }

    private static void AddCompoundToState(ThermodynamicState state, ReactionGenerator reactionGenerator,
        ChemicalCompound compound, double moles)
    {
        if (moles <= 0) return;

        state.SpeciesMoles[compound.Name] = state.SpeciesMoles.GetValueOrDefault(compound.Name, 0.0) + moles;

        var composition = reactionGenerator.ParseChemicalFormula(compound.ChemicalFormula);
        foreach (var (element, stoichiometry) in composition)
        {
            var addition = moles * stoichiometry;
            state.ElementalComposition[element] = state.ElementalComposition.GetValueOrDefault(element, 0.0) + addition;
        }
    }

    private static double ResolveMolarVolume_m3(ChemicalCompound compound)
    {
        if (compound.MolarVolume_cm3_mol.HasValue)
            return compound.MolarVolume_cm3_mol.Value * 1e-6;

        if (compound.Density_g_cm3.HasValue && compound.MolecularWeight_g_mol.HasValue &&
            compound.Density_g_cm3.Value > 0)
        {
            var molarVolume_cm3 = compound.MolecularWeight_g_mol.Value / compound.Density_g_cm3.Value;
            return molarVolume_cm3 * 1e-6;
        }

        return 0.0;
    }

    private static double ConvertPoreVolumeToLiters(float volume_um3)
    {
        return volume_um3 * 1e-18 * 1000.0;
    }

    private static (HashSet<int> inlets, HashSet<int> outlets) GetBoundaryPores(
        PNMDataset pnm, PNMReactiveTransportState state, FlowAxis axis)
    {
        var inlets = new HashSet<int>();
        var outlets = new HashSet<int>();

        if (pnm.Pores.Count == 0) return (inlets, outlets);

        // Find extremes along flow axis
        float minPos = float.MaxValue, maxPos = float.MinValue;

        foreach (var pore in pnm.Pores)
        {
            var pos = axis switch
            {
                FlowAxis.X => pore.Position.X,
                FlowAxis.Y => pore.Position.Y,
                _ => pore.Position.Z
            };

            if (pos < minPos) minPos = pos;
            if (pos > maxPos) maxPos = pos;
        }

        var tolerance = Math.Max(2.0f, (maxPos - minPos) * 0.05f);

        foreach (var pore in pnm.Pores)
        {
            var pos = axis switch
            {
                FlowAxis.X => pore.Position.X,
                FlowAxis.Y => pore.Position.Y,
                _ => pore.Position.Z
            };

            if (pos <= minPos + tolerance) inlets.Add(pore.ID);
            if (pos >= maxPos - tolerance) outlets.Add(pore.ID);
        }

        return (inlets, outlets);
    }

    // Simple conjugate gradient solver (CPU only for now)
    private static float[] SolveCG(SparseMatrix A, float[] b, float tolerance = 1e-6f, int maxIter = 5000)
    {
        var n = b.Length;
        var x = new float[n];
        var r = new float[n];
        Array.Copy(b, r, n);

        var p = new float[n];
        Array.Copy(r, p, n);

        var rsold = Dot(r, r);

        if (rsold < tolerance * tolerance) return x;

        for (var iter = 0; iter < maxIter; iter++)
        {
            var Ap = A.Multiply(p);
            var pAp = Dot(p, Ap);

            if (Math.Abs(pAp) < 1e-10f) break;

            var alpha = rsold / pAp;

            for (var i = 0; i < n; i++)
            {
                x[i] += alpha * p[i];
                r[i] -= alpha * Ap[i];
            }

            var rsnew = Dot(r, r);

            if (Math.Sqrt(rsnew) < tolerance) break;

            var beta = rsnew / rsold;

            for (var i = 0; i < n; i++)
                p[i] = r[i] + beta * p[i];

            rsold = rsnew;
        }

        return x;
    }

    private static float Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return (float)sum;
    }
}
