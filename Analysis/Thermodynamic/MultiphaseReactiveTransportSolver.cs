// GeoscientistToolkit/Analysis/Thermodynamic/MultiphaseReactiveTransportSolver.cs
//
// Integrated multiphase reactive transport solver
// Couples MultiphaseFlowSolver with ReactiveTransportSolver for complete simulation
//
// References:
// - Xu, T., et al. (2011). TOUGHREACT Version 2.0: A simulator for subsurface reactive transport.
// - Pruess, K., et al. (2012). TOUGH2 User's Guide, Version 2.1. LBNL-43134.
// - Steefel, C. I., & MacQuarrie, K. T. B. (1996). Approaches to modeling of reactive transport in porous media.

using System;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Analysis.Multiphase;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Thermodynamic;

/// <summary>
/// Integrated multiphase reactive transport solver combining:
/// - Multiphase flow (water-steam-NCG)
/// - Chemical reactions (equilibrium + kinetics)
/// - Heat transport
/// - Porosity/permeability evolution
/// </summary>
public class MultiphaseReactiveTransportSolver
{
    private readonly MultiphaseFlowSolver _multiphaseFlow;
    private readonly ReactiveTransportSolver _reactiveTransport;
    private readonly ThermodynamicSolver _thermodynamicSolver;

    private const int MAX_OUTER_ITERATIONS = 20;
    private const double CONVERGENCE_TOLERANCE = 1e-5;

    public MultiphaseReactiveTransportSolver(MultiphaseFlowSolver.EOSType eosType = MultiphaseFlowSolver.EOSType.WaterCO2)
    {
        _multiphaseFlow = new MultiphaseFlowSolver(eosType);
        _reactiveTransport = new ReactiveTransportSolver();
        _thermodynamicSolver = new ThermodynamicSolver();
    }

    /// <summary>
    /// Solve one time step of fully coupled multiphase reactive transport
    /// Uses operator splitting: flow → transport → chemistry → property update
    /// </summary>
    public MultiphaseReactiveState SolveTimeStep(
        MultiphaseReactiveState state,
        double dt,
        MultiphaseParameters flowParams)
    {
        Logger.Log($"[MultiphaseReactiveTransport] Starting time step: dt = {dt} s");

        var newState = state.Clone();
        var converged = false;

        for (int outerIter = 0; outerIter < MAX_OUTER_ITERATIONS; outerIter++)
        {
            var oldState = newState.Clone();

            // Step 1: Solve multiphase flow (pressure, temperature, saturations)
            Logger.Log($"[MultiphaseReactiveTransport] Iteration {outerIter + 1}: Solving multiphase flow");
            SolveMultiphaseFlow(newState, dt, flowParams);

            // Step 2: Solve reactive transport (species transport + reactions)
            Logger.Log($"[MultiphaseReactiveTransport] Iteration {outerIter + 1}: Solving reactive transport");
            SolveReactiveTransport(newState, dt);

            // Step 3: Update porosity and permeability from mineral precipitation/dissolution
            Logger.Log($"[MultiphaseReactiveTransport] Iteration {outerIter + 1}: Updating rock properties");
            UpdateRockProperties(newState);

            // Step 4: Check convergence
            double maxChange = CalculateMaxChange(oldState, newState);
            Logger.Log($"[MultiphaseReactiveTransport] Iteration {outerIter + 1}: Max change = {maxChange:E3}");

            if (maxChange < CONVERGENCE_TOLERANCE)
            {
                Logger.Log($"[MultiphaseReactiveTransport] Converged in {outerIter + 1} iterations");
                converged = true;
                break;
            }
        }

        if (!converged)
        {
            Logger.LogWarning($"[MultiphaseReactiveTransport] Did not converge in {MAX_OUTER_ITERATIONS} iterations");
        }

        newState.CurrentTime += dt;
        return newState;
    }

    /// <summary>
    /// Solve multiphase flow equations
    /// </summary>
    private void SolveMultiphaseFlow(MultiphaseReactiveState state, double dt, MultiphaseParameters flowParams)
    {
        // Create MultiphaseState from current state
        var multiphaseState = ConvertToMultiphaseState(state);

        // Solve multiphase flow
        var newMultiphaseState = _multiphaseFlow.SolveTimeStep(multiphaseState, dt, flowParams);

        // Update state from multiphase result
        UpdateFromMultiphaseState(state, newMultiphaseState);
    }

    /// <summary>
    /// Solve reactive transport (advection + dispersion + reactions)
    /// </summary>
    private void SolveReactiveTransport(MultiphaseReactiveState state, double dt)
    {
        // Create ReactiveTransportState from current state
        var reactiveState = ConvertToReactiveTransportState(state);

        // Create flow field data from multiphase velocities
        var flowData = CreateFlowFieldData(state);

        // Solve reactive transport
        var newReactiveState = _reactiveTransport.SolveTimeStep(reactiveState, dt, flowData);

        // Update state from reactive transport result
        UpdateFromReactiveTransportState(state, newReactiveState);
    }

    /// <summary>
    /// Update porosity and permeability from mineral volume changes
    /// </summary>
    private void UpdateRockProperties(MultiphaseReactiveState state)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            // Calculate mineral volume fraction change
            double delta_VF = 0.0;
            foreach (var (mineral, volumeFraction) in state.MineralVolumeFractions)
            {
                double VF_initial = state.InitialMineralVolumeFractions.GetValueOrDefault(mineral, new float[nx, ny, nz])[i, j, k];
                double VF_current = volumeFraction[i, j, k];
                delta_VF += (VF_current - VF_initial);
            }

            // Update porosity: φ_new = φ_old - ΔVF
            double phi_old = state.InitialPorosity[i, j, k];
            double phi_new = Math.Clamp(phi_old - delta_VF, 0.01, 0.99);
            state.Porosity[i, j, k] = (float)phi_new;

            // Update permeability using Kozeny-Carman
            double k_old = state.InitialPermeability[i, j, k];
            double k_new = PorosityPermeabilityCoupling.KozenyCarman(phi_new, phi_old, k_old);
            state.Permeability[i, j, k] = (float)k_new;
        }
    }

    /// <summary>
    /// Convert to MultiphaseState for multiphase flow solver
    /// </summary>
    private MultiphaseState ConvertToMultiphaseState(MultiphaseReactiveState state)
    {
        var mpState = new MultiphaseState(state.GridDimensions)
        {
            Pressure = (float[,,])state.Pressure.Clone(),
            Temperature = (float[,,])state.Temperature.Clone(),
            LiquidSaturation = (float[,,])state.LiquidSaturation.Clone(),
            VaporSaturation = (float[,,])state.VaporSaturation.Clone(),
            GasSaturation = (float[,,])state.GasSaturation.Clone(),
            Porosity = (float[,,])state.Porosity.Clone(),
            Permeability = (float[,,])state.Permeability.Clone()
        };

        return mpState;
    }

    /// <summary>
    /// Update state from MultiphaseState
    /// </summary>
    private void UpdateFromMultiphaseState(MultiphaseReactiveState state, MultiphaseState mpState)
    {
        state.Pressure = (float[,,])mpState.Pressure.Clone();
        state.Temperature = (float[,,])mpState.Temperature.Clone();
        state.LiquidSaturation = (float[,,])mpState.LiquidSaturation.Clone();
        state.VaporSaturation = (float[,,])mpState.VaporSaturation.Clone();
        state.GasSaturation = (float[,,])mpState.GasSaturation.Clone();
        state.LiquidDensity = (float[,,])mpState.LiquidDensity.Clone();
        state.VaporDensity = (float[,,])mpState.VaporDensity.Clone();
        state.GasDensity = (float[,,])mpState.GasDensity.Clone();
        state.DissolvedGasConcentration = (float[,,])mpState.DissolvedGasConcentration.Clone();
    }

    /// <summary>
    /// Convert to ReactiveTransportState for reactive transport solver
    /// </summary>
    private ReactiveTransportState ConvertToReactiveTransportState(MultiphaseReactiveState state)
    {
        var rtState = new ReactiveTransportState
        {
            GridDimensions = state.GridDimensions,
            Concentrations = new Dictionary<string, float[,,]>(),
            MineralVolumeFractions = new Dictionary<string, float[,,]>(),
            InitialMineralVolumeFractions = new Dictionary<string, float[,,]>(),
            InitialPorosity = (float[,,])state.InitialPorosity.Clone(),
            Temperature = (float[,,])state.Temperature.Clone(),
            Pressure = (float[,,])state.Pressure.Clone(),
            Porosity = (float[,,])state.Porosity.Clone()
        };

        // Copy concentrations
        foreach (var (species, conc) in state.Concentrations)
        {
            rtState.Concentrations[species] = (float[,,])conc.Clone();
        }

        // Copy mineral volume fractions
        foreach (var (mineral, vf) in state.MineralVolumeFractions)
        {
            rtState.MineralVolumeFractions[mineral] = (float[,,])vf.Clone();
        }

        foreach (var (mineral, vf) in state.InitialMineralVolumeFractions)
        {
            rtState.InitialMineralVolumeFractions[mineral] = (float[,,])vf.Clone();
        }

        return rtState;
    }

    /// <summary>
    /// Update state from ReactiveTransportState
    /// </summary>
    private void UpdateFromReactiveTransportState(MultiphaseReactiveState state, ReactiveTransportState rtState)
    {
        state.Concentrations.Clear();
        foreach (var (species, conc) in rtState.Concentrations)
        {
            state.Concentrations[species] = (float[,,])conc.Clone();
        }

        state.MineralVolumeFractions.Clear();
        foreach (var (mineral, vf) in rtState.MineralVolumeFractions)
        {
            state.MineralVolumeFractions[mineral] = (float[,,])vf.Clone();
        }

        state.Porosity = (float[,,])rtState.Porosity.Clone();
    }

    /// <summary>
    /// Create FlowFieldData from multiphase state
    /// </summary>
    private FlowFieldData CreateFlowFieldData(MultiphaseReactiveState state)
    {
        int nx = state.GridDimensions.X;
        int ny = state.GridDimensions.Y;
        int nz = state.GridDimensions.Z;

        var flowData = new FlowFieldData
        {
            GridSpacing = (1.0, 1.0, 1.0), // TODO: Get from parameters
            VelocityX = new float[nx, ny, nz],
            VelocityY = new float[nx, ny, nz],
            VelocityZ = new float[nx, ny, nz],
            Permeability = (float[,,])state.Permeability.Clone(),
            InitialPermeability = (float[,,])state.InitialPermeability.Clone(),
            Dispersivity = 0.1
        };

        // Calculate phase velocities (simplified - total Darcy velocity)
        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            double dx = flowData.GridSpacing.X;
            double dy = flowData.GridSpacing.Y;
            double dz = flowData.GridSpacing.Z;

            // Approximate total velocity from pressure gradient
            double dP_dx = (state.Pressure[i + 1, j, k] - state.Pressure[i - 1, j, k]) / (2 * dx);
            double dP_dy = (state.Pressure[i, j + 1, k] - state.Pressure[i, j - 1, k]) / (2 * dy);
            double dP_dz = (state.Pressure[i, j, k + 1] - state.Pressure[i, j, k - 1]) / (2 * dz);

            double k_perm = state.Permeability[i, j, k];
            double mu = 1e-3; // Pa·s (water viscosity, simplified)

            flowData.VelocityX[i, j, k] = (float)(-k_perm / mu * dP_dx);
            flowData.VelocityY[i, j, k] = (float)(-k_perm / mu * dP_dy);
            flowData.VelocityZ[i, j, k] = (float)(-k_perm / mu * dP_dz);
        }

        return flowData;
    }

    /// <summary>
    /// Calculate maximum change between states for convergence check
    /// </summary>
    private double CalculateMaxChange(MultiphaseReactiveState oldState, MultiphaseReactiveState newState)
    {
        double maxChange = 0.0;
        int nx = oldState.GridDimensions.X;
        int ny = oldState.GridDimensions.Y;
        int nz = oldState.GridDimensions.Z;

        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            // Pressure change
            double dP = Math.Abs(newState.Pressure[i, j, k] - oldState.Pressure[i, j, k]) /
                       Math.Max(oldState.Pressure[i, j, k], 1e5);

            // Temperature change
            double dT = Math.Abs(newState.Temperature[i, j, k] - oldState.Temperature[i, j, k]) /
                       Math.Max(oldState.Temperature[i, j, k], 273.15);

            // Saturation change
            double dS = Math.Abs(newState.LiquidSaturation[i, j, k] - oldState.LiquidSaturation[i, j, k]);

            // Porosity change
            double dPhi = Math.Abs(newState.Porosity[i, j, k] - oldState.Porosity[i, j, k]);

            maxChange = Math.Max(maxChange, Math.Max(dP, Math.Max(dT, Math.Max(dS, dPhi))));
        }

        // Check concentration changes
        foreach (var species in oldState.Concentrations.Keys)
        {
            if (!newState.Concentrations.ContainsKey(species)) continue;

            var C_old = oldState.Concentrations[species];
            var C_new = newState.Concentrations[species];

            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
            {
                double dC = Math.Abs(C_new[i, j, k] - C_old[i, j, k]) /
                           Math.Max(C_old[i, j, k], 1e-10);
                maxChange = Math.Max(maxChange, dC);
            }
        }

        return maxChange;
    }
}

/// <summary>
/// State container for multiphase reactive transport
/// Combines fields from MultiphaseState and ReactiveTransportState
/// </summary>
public class MultiphaseReactiveState
{
    public (int X, int Y, int Z) GridDimensions { get; set; }
    public double CurrentTime { get; set; }

    // ========== Multiphase Flow Fields ==========
    public float[,,] Pressure { get; set; }       // Pa
    public float[,,] Temperature { get; set; }    // K
    public float[,,] LiquidSaturation { get; set; } // fraction
    public float[,,] VaporSaturation { get; set; }  // fraction
    public float[,,] GasSaturation { get; set; }    // fraction

    public float[,,] LiquidDensity { get; set; }  // kg/m³
    public float[,,] VaporDensity { get; set; }   // kg/m³
    public float[,,] GasDensity { get; set; }     // kg/m³
    public float[,,] DissolvedGasConcentration { get; set; } // mol/L

    // ========== Reactive Transport Fields ==========
    public Dictionary<string, float[,,]> Concentrations { get; set; } = new(); // mol/L for each species
    public Dictionary<string, float[,,]> MineralVolumeFractions { get; set; } = new(); // fraction for each mineral

    // ========== Rock Properties ==========
    public float[,,] Porosity { get; set; }       // fraction
    public float[,,] Permeability { get; set; }   // m²

    // Initial values (for calculating changes)
    public float[,,] InitialPorosity { get; set; }
    public float[,,] InitialPermeability { get; set; }
    public Dictionary<string, float[,,]> InitialMineralVolumeFractions { get; set; } = new();

    public MultiphaseReactiveState((int x, int y, int z) gridSize)
    {
        GridDimensions = gridSize;
        int nx = gridSize.x, ny = gridSize.y, nz = gridSize.z;

        Pressure = new float[nx, ny, nz];
        Temperature = new float[nx, ny, nz];
        LiquidSaturation = new float[nx, ny, nz];
        VaporSaturation = new float[nx, ny, nz];
        GasSaturation = new float[nx, ny, nz];

        LiquidDensity = new float[nx, ny, nz];
        VaporDensity = new float[nx, ny, nz];
        GasDensity = new float[nx, ny, nz];
        DissolvedGasConcentration = new float[nx, ny, nz];

        Porosity = new float[nx, ny, nz];
        Permeability = new float[nx, ny, nz];
        InitialPorosity = new float[nx, ny, nz];
        InitialPermeability = new float[nx, ny, nz];
    }

    public MultiphaseReactiveState Clone()
    {
        var clone = new MultiphaseReactiveState(GridDimensions)
        {
            CurrentTime = CurrentTime,
            Pressure = (float[,,])Pressure.Clone(),
            Temperature = (float[,,])Temperature.Clone(),
            LiquidSaturation = (float[,,])LiquidSaturation.Clone(),
            VaporSaturation = (float[,,])VaporSaturation.Clone(),
            GasSaturation = (float[,,])GasSaturation.Clone(),
            LiquidDensity = (float[,,])LiquidDensity.Clone(),
            VaporDensity = (float[,,])VaporDensity.Clone(),
            GasDensity = (float[,,])GasDensity.Clone(),
            DissolvedGasConcentration = (float[,,])DissolvedGasConcentration.Clone(),
            Porosity = (float[,,])Porosity.Clone(),
            Permeability = (float[,,])Permeability.Clone(),
            InitialPorosity = (float[,,])InitialPorosity.Clone(),
            InitialPermeability = (float[,,])InitialPermeability.Clone()
        };

        foreach (var (species, conc) in Concentrations)
            clone.Concentrations[species] = (float[,,])conc.Clone();

        foreach (var (mineral, vf) in MineralVolumeFractions)
            clone.MineralVolumeFractions[mineral] = (float[,,])vf.Clone();

        foreach (var (mineral, vf) in InitialMineralVolumeFractions)
            clone.InitialMineralVolumeFractions[mineral] = (float[,,])vf.Clone();

        return clone;
    }
}
