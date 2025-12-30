// GeoscientistToolkit/Analysis/Thermodynamic/ReactiveTransportSolver.cs
//
// Reactive transport solver coupling flow, transport, and chemical reactions
// Similar to TOUGHREACT's Sequential Iterative Approach (SIA)
//
// References:
// - Xu, T., et al. (2011). TOUGHREACT Version 2.0: A simulator for subsurface reactive transport.
//   Computers & Geosciences, 37(6), 763-774.
// - Steefel, C. I., & MacQuarrie, K. T. B. (1996). Approaches to modeling of reactive transport in porous media.
//   Reviews in Mineralogy and Geochemistry, 34(1), 85-129.
// - Pruess, K. (2004). The TOUGH codes—A family of simulation tools for multiphase flow and transport processes.
//   Vadose Zone Journal, 3(3), 738-746.

using System;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Thermodynamic;

/// <summary>
/// Reactive transport solver using operator splitting:
/// 1. Transport step (advection + dispersion)
/// 2. Reaction step (equilibrium + kinetics)
/// 3. Feedback (update porosity/permeability)
///
/// Similar to TOUGHREACT's Sequential Iterative Approach (SIA)
/// </summary>
public class ReactiveTransportSolver : SimulatorNodeSupport
{
    private readonly ThermodynamicSolver _thermoSolver;
    private readonly KineticsSolver _kineticsSolver;

    // Iteration parameters
    private const int MAX_OUTER_ITERATIONS = 10;
    private const double CONVERGENCE_TOLERANCE = 1e-6;

    public ReactiveTransportSolver() : this(null)
    {
    }

    public ReactiveTransportSolver(bool? useNodes) : base(useNodes)
    {
        _thermoSolver = new ThermodynamicSolver();
        _kineticsSolver = new KineticsSolver();

        if (_useNodes)
        {
            Logger.Log("[ReactiveTransportSolver] Node Manager integration: ENABLED");
        }
    }

    /// <summary>
    /// Solve one reactive transport time step using Sequential Iteration Approach.
    /// </summary>
    /// <param name="state">Current state of the system</param>
    /// <param name="dt">Time step (seconds)</param>
    /// <param name="flowData">Flow field data</param>
    /// <returns>Updated state after transport and reaction</returns>
    public ReactiveTransportState SolveTimeStep(
        ReactiveTransportState state,
        double dt,
        FlowFieldData flowData)
    {
        var newState = state.Clone();

        // Outer iteration loop for coupling
        for (int iter = 0; iter < MAX_OUTER_ITERATIONS; iter++)
        {
            var oldConcentrations = CloneConcentrations(newState.Concentrations);

            // Step 1: Transport (advection + dispersion + diffusion)
            SolveTransportStep(newState, dt, flowData);

            // Step 2: Chemical reactions (equilibrium + kinetics)
            SolveReactionStep(newState, dt);

            // Step 3: Update porosity from mineral precipitation/dissolution
            UpdatePorosityFromMinerals(newState);

            // Step 4: Update permeability from porosity changes
            UpdatePermeabilityFromPorosity(newState, flowData);

            // Check convergence
            double maxChange = CalculateMaxConcentrationChange(oldConcentrations, newState.Concentrations);

            if (maxChange < CONVERGENCE_TOLERANCE)
            {
                Logger.Log($"[ReactiveTransport] Converged in {iter + 1} outer iterations");
                break;
            }

            if (iter == MAX_OUTER_ITERATIONS - 1)
            {
                Logger.LogWarning($"[ReactiveTransport] Did not converge in {MAX_OUTER_ITERATIONS} iterations. Max change: {maxChange:E3}");
            }
        }

        return newState;
    }

    /// <summary>
    /// Solve transport equation: ∂C/∂t + ∇·(vC) = ∇·(D∇C)
    /// Using finite volume method with upwind advection
    /// </summary>
    private void SolveTransportStep(ReactiveTransportState state, double dt, FlowFieldData flowData)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        // For each aqueous component
        foreach (var component in state.Concentrations.Keys.ToList())
        {
            var C = state.Concentrations[component];
            var C_new = new float[nx, ny, nz];

            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
            {
                double advection = CalculateAdvectionTerm(C, i, j, k, flowData, state.Porosity);
                double dispersion = CalculateDispersionTerm(C, i, j, k, flowData, state.Porosity);

                // Forward Euler (can be upgraded to BDF or Crank-Nicolson)
                double dC_dt = -advection + dispersion;

                C_new[i, j, k] = (float)Math.Max(0.0, C[i, j, k] + dt * dC_dt);
            }

            state.Concentrations[component] = C_new;
        }
    }

    /// <summary>
    /// Calculate advection term: ∇·(vC) using upwind scheme
    /// </summary>
    private double CalculateAdvectionTerm(float[,,] C, int i, int j, int k,
        FlowFieldData flowData, float[,,] porosity)
    {
        double dx = flowData.GridSpacing.X;
        double dy = flowData.GridSpacing.Y;
        double dz = flowData.GridSpacing.Z;

        // Get velocities at cell faces (Darcy velocity / porosity = pore velocity)
        double vx = flowData.VelocityX[i, j, k] / Math.Max(porosity[i, j, k], 0.01);
        double vy = flowData.VelocityY[i, j, k] / Math.Max(porosity[i, j, k], 0.01);
        double vz = flowData.VelocityZ[i, j, k] / Math.Max(porosity[i, j, k], 0.01);

        // Upwind scheme for advection
        double flux_x = 0.0, flux_y = 0.0, flux_z = 0.0;

        // X-direction
        if (vx > 0 && i > 0)
            flux_x = vx * (C[i, j, k] - C[i - 1, j, k]) / dx;
        else if (vx < 0 && i < C.GetLength(0) - 1)
            flux_x = vx * (C[i + 1, j, k] - C[i, j, k]) / dx;

        // Y-direction
        if (vy > 0 && j > 0)
            flux_y = vy * (C[i, j, k] - C[i, j - 1, k]) / dy;
        else if (vy < 0 && j < C.GetLength(1) - 1)
            flux_y = vy * (C[i, j + 1, k] - C[i, j, k]) / dy;

        // Z-direction
        if (vz > 0 && k > 0)
            flux_z = vz * (C[i, j, k] - C[i, j, k - 1]) / dz;
        else if (vz < 0 && k < C.GetLength(2) - 1)
            flux_z = vz * (C[i, j, k + 1] - C[i, j, k]) / dz;

        return flux_x + flux_y + flux_z;
    }

    /// <summary>
    /// Calculate dispersion term: ∇·(D∇C) using central differences
    /// Includes molecular diffusion + mechanical dispersion
    /// </summary>
    private double CalculateDispersionTerm(float[,,] C, int i, int j, int k,
        FlowFieldData flowData, float[,,] porosity)
    {
        int nx = C.GetLength(0);
        int ny = C.GetLength(1);
        int nz = C.GetLength(2);

        double dx = flowData.GridSpacing.X;
        double dy = flowData.GridSpacing.Y;
        double dz = flowData.GridSpacing.Z;

        // Effective dispersion coefficient: D_eff = D_mol * tau + alpha_L * |v|
        double D_molecular = 1e-9; // m²/s (typical for ions in water)
        double tortuosity = Math.Pow(porosity[i, j, k], 0.33); // Millington-Quirk
        double alpha_L = flowData.Dispersivity; // Longitudinal dispersivity (m)

        double velocity_magnitude = Math.Sqrt(
            Math.Pow(flowData.VelocityX[i, j, k], 2) +
            Math.Pow(flowData.VelocityY[i, j, k], 2) +
            Math.Pow(flowData.VelocityZ[i, j, k], 2)
        ) / Math.Max(porosity[i, j, k], 0.01);

        double D_eff = D_molecular * tortuosity + alpha_L * velocity_magnitude;

        // Central difference for ∇²C
        double d2C_dx2 = 0.0, d2C_dy2 = 0.0, d2C_dz2 = 0.0;

        // X-direction
        if (i > 0 && i < nx - 1)
            d2C_dx2 = (C[i + 1, j, k] - 2 * C[i, j, k] + C[i - 1, j, k]) / (dx * dx);

        // Y-direction
        if (j > 0 && j < ny - 1)
            d2C_dy2 = (C[i, j + 1, k] - 2 * C[i, j, k] + C[i, j - 1, k]) / (dy * dy);

        // Z-direction
        if (k > 0 && k < nz - 1)
            d2C_dz2 = (C[i, j, k + 1] - 2 * C[i, j, k] + C[i, j, k - 1]) / (dz * dz);

        return D_eff * porosity[i, j, k] * (d2C_dx2 + d2C_dy2 + d2C_dz2);
    }

    /// <summary>
    /// Solve chemical reactions at each grid cell (equilibrium + kinetics)
    /// </summary>
    private void SolveReactionStep(ReactiveTransportState state, double dt)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        // Process each cell independently
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            // Extract local composition
            var localComposition = new Dictionary<string, double>();
            foreach (var kvp in state.Concentrations)
            {
                localComposition[kvp.Key] = kvp.Value[i, j, k];
            }

            double T_K = state.Temperature[i, j, k];
            double P_bar = state.Pressure[i, j, k] / 1e5; // Pa to bar

            try
            {
                // 1. Create thermodynamic state
                var thermoState = new ThermodynamicState
                {
                    Temperature_K = T_K,
                    Pressure_bar = P_bar,
                    SpeciesMoles = new Dictionary<string, double>(localComposition)
                };

                // 2. Aqueous equilibrium (fast)
                var equilibriumResult = _thermoSolver.SolveEquilibrium(thermoState);

                if (equilibriumResult.SpeciesMoles.Count == 0 ||
                    equilibriumResult.SpeciesMoles.Values.All(value => Math.Abs(value) < 1e-20))
                {
                    continue;
                }

                // 3. Update concentrations from equilibrium
                foreach (var kvp in equilibriumResult.SpeciesMoles)
                {
                    if (state.Concentrations.ContainsKey(kvp.Key))
                        state.Concentrations[kvp.Key][i, j, k] = (float)kvp.Value;
                }

                // Note: Full kinetics integration would be added here
                // For now, equilibrium-only approach is used
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[ReactiveTransport] Reaction failed at cell ({i},{j},{k}): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Update porosity from mineral volume fraction changes.
    /// φ_new = φ_old + Σ(ΔV_mineral / V_total)
    /// </summary>
    private void UpdatePorosityFromMinerals(ReactiveTransportState state)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        if (state.InitialMineralVolumeFractions == null || state.InitialMineralVolumeFractions.Count == 0)
            return;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            double phi_initial = state.InitialPorosity[i, j, k];
            double delta_volume = 0.0;

            foreach (var mineral in state.MineralVolumeFractions.Keys)
            {
                if (state.InitialMineralVolumeFractions.ContainsKey(mineral))
                {
                    double V_old = state.InitialMineralVolumeFractions[mineral][i, j, k];
                    double V_new = state.MineralVolumeFractions[mineral][i, j, k];
                    delta_volume += (V_new - V_old);
                }
            }

            // New porosity = old porosity - precipitation + dissolution
            state.Porosity[i, j, k] = (float)Math.Clamp(phi_initial - delta_volume, 0.01, 0.99);
        }
    }

    /// <summary>
    /// Update permeability from porosity using Kozeny-Carman relation.
    /// k/k0 = (φ/φ0)³ · [(1-φ0)/(1-φ)]²
    /// </summary>
    private void UpdatePermeabilityFromPorosity(ReactiveTransportState state, FlowFieldData flowData)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            double phi = state.Porosity[i, j, k];
            double phi0 = state.InitialPorosity[i, j, k];
            double k0 = flowData.InitialPermeability[i, j, k];

            double k_new = Multiphase.PorosityPermeabilityCoupling.KozenyCarman(phi, phi0, k0);

            flowData.Permeability[i, j, k] = (float)k_new;
        }
    }

    /// <summary>
    /// Calculate mineral surface area from volume fraction
    /// A = V * specific_surface_area (m²/m³)
    /// </summary>
    private double CalculateSurfaceArea(Dictionary<string, double> minerals, double porosity)
    {
        // Typical specific surface area for minerals: 0.1 - 10 m²/g
        // Here we use a simplified geometric estimate
        const double specific_surface_area = 1000.0; // m²/m³ of mineral

        double total_mineral_volume = minerals.Values.Sum();
        return total_mineral_volume * specific_surface_area;
    }

    private Dictionary<string, float[,,]> CloneConcentrations(Dictionary<string, float[,,]> original)
    {
        var clone = new Dictionary<string, float[,,]>();
        foreach (var kvp in original)
        {
            clone[kvp.Key] = (float[,,])kvp.Value.Clone();
        }
        return clone;
    }

    private double CalculateMaxConcentrationChange(
        Dictionary<string, float[,,]> old_conc,
        Dictionary<string, float[,,]> new_conc)
    {
        double maxChange = 0.0;

        foreach (var component in old_conc.Keys)
        {
            if (!new_conc.ContainsKey(component)) continue;

            var C_old = old_conc[component];
            var C_new = new_conc[component];

            for (int i = 0; i < C_old.GetLength(0); i++)
            for (int j = 0; j < C_old.GetLength(1); j++)
            for (int k = 0; k < C_old.GetLength(2); k++)
            {
                double change = Math.Abs(C_new[i, j, k] - C_old[i, j, k]) /
                               Math.Max(C_old[i, j, k], 1e-10);
                maxChange = Math.Max(maxChange, change);
            }
        }

        return maxChange;
    }
}

/// <summary>
/// State container for reactive transport simulation
/// </summary>
public class ReactiveTransportState
{
    public (int X, int Y, int Z) GridDimensions { get; set; }

    // Aqueous concentrations (mol/L) for each component
    public Dictionary<string, float[,,]> Concentrations { get; set; } = new();

    // Mineral volume fractions (0-1)
    public Dictionary<string, float[,,]> MineralVolumeFractions { get; set; } = new();

    // Initial state (for calculating changes)
    public Dictionary<string, float[,,]> InitialMineralVolumeFractions { get; set; } = new();
    public float[,,] InitialPorosity { get; set; }

    // Physical properties
    public float[,,] Temperature { get; set; } // K
    public float[,,] Pressure { get; set; }    // Pa
    public float[,,] Porosity { get; set; }    // fraction

    public ReactiveTransportState Clone()
    {
        return new ReactiveTransportState
        {
            GridDimensions = GridDimensions,
            Concentrations = CloneArrayDict(Concentrations),
            MineralVolumeFractions = CloneArrayDict(MineralVolumeFractions),
            InitialMineralVolumeFractions = InitialMineralVolumeFractions,
            InitialPorosity = InitialPorosity,
            Temperature = (float[,,])Temperature.Clone(),
            Pressure = (float[,,])Pressure.Clone(),
            Porosity = (float[,,])Porosity.Clone()
        };
    }

    private Dictionary<string, float[,,]> CloneArrayDict(Dictionary<string, float[,,]> original)
    {
        var result = new Dictionary<string, float[,,]>();
        foreach (var kvp in original)
        {
            result[kvp.Key] = (float[,,])kvp.Value.Clone();
        }
        return result;
    }
}

/// <summary>
/// Flow field data for reactive transport
/// </summary>
public class FlowFieldData
{
    public (double X, double Y, double Z) GridSpacing { get; set; }  // m
    public float[,,] VelocityX { get; set; }  // m/s (Darcy velocity)
    public float[,,] VelocityY { get; set; }
    public float[,,] VelocityZ { get; set; }
    public float[,,] Permeability { get; set; }  // m²
    public float[,,] InitialPermeability { get; set; }
    public double Dispersivity { get; set; } = 0.1;  // m (longitudinal dispersivity)
}
