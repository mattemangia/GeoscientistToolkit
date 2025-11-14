// GeoscientistToolkit/Analysis/PNM/PNMReactiveTransport.cs
//
// Reactive transport solver for Pore Network Models
// Couples flow, heat transfer, species transport, and thermodynamic reactions

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Analysis.Thermodynamic;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;

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
        var pressures = SolveCG(matrix, b);

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
        // Simplified reaction model: mineral precipitation/dissolution
        // In practice, this would use the ReactiveTransportSolver from PhysicoChem

        var dt = (float)options.TimeStep;

        foreach (var pore in pnm.Pores)
        {
            var T = state.PoreTemperatures[pore.ID];
            var P = state.PorePressures[pore.ID];

            // Example: Calcite precipitation from Ca2+ and CO3^2-
            // CaCO3(s) <=> Ca2+ + CO3^2-

            var hasCa = state.PoreConcentrations[pore.ID].ContainsKey("Ca2+");
            var hasCO3 = state.PoreConcentrations[pore.ID].ContainsKey("CO3^2-");

            if (hasCa && hasCO3)
            {
                var C_Ca = state.PoreConcentrations[pore.ID]["Ca2+"];
                var C_CO3 = state.PoreConcentrations[pore.ID]["CO3^2-"];

                // Solubility product (simplified, should use thermodynamic database)
                var Ksp = CalculateKsp_Calcite(T, P);

                // Ion activity product
                var IAP = C_Ca * C_CO3;

                // Supersaturation ratio
                var omega = IAP / Ksp;

                // Reaction rate (mol/m³/s) - simplified kinetic model
                float rate = 0;

                if (omega > 1) // Supersaturated - precipitation
                {
                    var k_precip = 1e-6f; // Rate constant (should be from database)
                    rate = -k_precip * (omega - 1);
                }
                else if (omega < 1) // Undersaturated - dissolution
                {
                    var k_diss = 1e-7f;
                    rate = k_diss * (1 - omega);

                    // Can only dissolve if mineral is present
                    if (!state.PoreMinerals[pore.ID].ContainsKey("Calcite") ||
                        state.PoreMinerals[pore.ID]["Calcite"] <= 0)
                    {
                        rate = 0;
                    }
                }

                if (rate != 0)
                {
                    // Update concentrations
                    var dmol = rate * dt;

                    state.PoreConcentrations[pore.ID]["Ca2+"] -= dmol;
                    state.PoreConcentrations[pore.ID]["CO3^2-"] -= dmol;

                    // Ensure non-negative
                    state.PoreConcentrations[pore.ID]["Ca2+"] = Math.Max(0, state.PoreConcentrations[pore.ID]["Ca2+"]);
                    state.PoreConcentrations[pore.ID]["CO3^2-"] = Math.Max(0, state.PoreConcentrations[pore.ID]["CO3^2-"]);

                    // Update mineral volume
                    var molarVolume_Calcite = 36.9e-6f; // m³/mol
                    var dV = -dmol * molarVolume_Calcite * 1e18f; // Convert to μm³

                    if (!state.PoreMinerals[pore.ID].ContainsKey("Calcite"))
                        state.PoreMinerals[pore.ID]["Calcite"] = 0;

                    state.PoreMinerals[pore.ID]["Calcite"] += dV;
                    state.PoreMinerals[pore.ID]["Calcite"] = Math.Max(0, state.PoreMinerals[pore.ID]["Calcite"]);

                    state.ReactionRates[pore.ID] = rate;
                }
            }
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
        // Quick permeability estimate using current geometry
        // Uses simple Kozeny-Carman-like approach

        var totalThroatConductance = 0.0;
        var throatCount = 0;
        var voxelSize_m = pnm.VoxelSize * 1e-6f;
        var viscosity_PaS = options.FluidViscosity * 0.001f;

        var poreMap = pnm.Pores.ToDictionary(p => p.ID);

        foreach (var throat in pnm.Throats)
        {
            if (!poreMap.ContainsKey(throat.Pore1ID) || !poreMap.ContainsKey(throat.Pore2ID))
                continue;

            var p1 = poreMap[throat.Pore1ID];
            var p2 = poreMap[throat.Pore2ID];

            var r_t = state.ThroatRadii[throat.ID] * voxelSize_m;
            if (r_t <= 0) continue;

            var length = Vector3.Distance(p1.Position, p2.Position) * voxelSize_m;
            if (length < 1e-12f) length = voxelSize_m;

            var conductance = Math.PI * Math.Pow(r_t, 4) / (8 * viscosity_PaS * length);
            totalThroatConductance += conductance;
            throatCount++;
        }

        if (throatCount == 0) return 0;

        var avgConductance = totalThroatConductance / throatCount;

        // Convert to permeability (rough estimate)
        var k_m2 = avgConductance * viscosity_PaS * voxelSize_m;
        var k_mD = (float)(k_m2 * 1.01325e15);

        return k_mD;
    }

    private static float CalculateKsp_Calcite(float T, float P)
    {
        // Calcite solubility product as function of T and P
        // Simplified model - in practice use thermodynamic database

        var T_ref = 298.15f; // K
        var Ksp_ref = 3.3e-9f; // mol²/L² at 25°C

        // Van't Hoff equation (simplified)
        var deltaH = -12000f; // J/mol (dissolution enthalpy)
        var R = 8.314f;

        var Ksp = Ksp_ref * (float)Math.Exp(-deltaH / R * (1 / T - 1 / T_ref));

        return Ksp;
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
