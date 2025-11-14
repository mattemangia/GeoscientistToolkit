// GeoscientistToolkit/Analysis/PhysicoChem/SubSolvers.cs
//
// Sub-solvers for flow, heat transfer, and nucleation in PhysicoChem simulations

using System;
using System.Collections.Generic;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.PhysicoChem;

/// <summary>
/// Heat transfer solver (conduction + convection)
/// </summary>
public class HeatTransferSolver
{
    /// <summary>
    /// Solve heat transfer equation:
    /// ρCp ∂T/∂t = ∇·(k∇T) - ρCp v·∇T + Q
    /// </summary>
    public void SolveHeat(PhysicoChemState state, double dt, List<BoundaryCondition> bcs)
    {
        int nx = state.Temperature.GetLength(0);
        int ny = state.Temperature.GetLength(1);
        int nz = state.Temperature.GetLength(2);

        var T_new = new float[nx, ny, nz];

        // Material properties (simplified - should come from domains)
        double k = 2.0; // W/(m·K) thermal conductivity
        double rho = 2500.0; // kg/m³ density
        double Cp = 1000.0; // J/(kg·K) specific heat
        double alpha = k / (rho * Cp); // m²/s thermal diffusivity

        // Grid spacing (should come from mesh)
        double dx = 0.01; // m
        double dy = 0.01;
        double dz = 0.01;

        // Explicit finite difference
        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            double T = state.Temperature[i, j, k];

            // Conduction term: α·∇²T
            double d2T_dx2 = (state.Temperature[i + 1, j, k] - 2 * T + state.Temperature[i - 1, j, k]) / (dx * dx);
            double d2T_dy2 = (state.Temperature[i, j + 1, k] - 2 * T + state.Temperature[i, j - 1, k]) / (dy * dy);
            double d2T_dz2 = (state.Temperature[i, j, k + 1] - 2 * T + state.Temperature[i, j, k - 1]) / (dz * dz);

            double conduction = alpha * (d2T_dx2 + d2T_dy2 + d2T_dz2);

            // Convection term: -v·∇T (upwind)
            double vx = state.VelocityX[i, j, k];
            double vy = state.VelocityY[i, j, k];
            double vz = state.VelocityZ[i, j, k];

            double dT_dx = vx > 0 ? (T - state.Temperature[i - 1, j, k]) / dx : (state.Temperature[i + 1, j, k] - T) / dx;
            double dT_dy = vy > 0 ? (T - state.Temperature[i, j - 1, k]) / dy : (state.Temperature[i, j + 1, k] - T) / dy;
            double dT_dz = vz > 0 ? (T - state.Temperature[i, j, k - 1]) / dz : (state.Temperature[i, j, k + 1] - T) / dz;

            double convection = -(vx * dT_dx + vy * dT_dy + vz * dT_dz);

            // Update
            T_new[i, j, k] = (float)(T + dt * (conduction + convection));
        }

        // Copy interior points
        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            state.Temperature[i, j, k] = T_new[i, j, k];
        }
    }
}

/// <summary>
/// Flow solver (Darcy or Navier-Stokes)
/// </summary>
public class FlowSolver
{
    /// <summary>
    /// Solve flow equations:
    /// For Darcy: v = -(k/μ)·(∇P - ρg)
    /// For Navier-Stokes: ρ(∂v/∂t + v·∇v) = -∇P + μ∇²v + ρg
    /// </summary>
    public void SolveFlow(PhysicoChemState state, double dt, List<BoundaryCondition> bcs)
    {
        int nx = state.Pressure.GetLength(0);
        int ny = state.Pressure.GetLength(1);
        int nz = state.Pressure.GetLength(2);

        // For now, use simple Darcy flow
        SolveDarcyFlow(state);
    }

    private void SolveDarcyFlow(PhysicoChemState state)
    {
        int nx = state.Pressure.GetLength(0);
        int ny = state.Pressure.GetLength(1);
        int nz = state.Pressure.GetLength(2);

        double dx = 0.01; // m (should come from mesh)
        double dy = 0.01;
        double dz = 0.01;

        double mu = 0.001; // Pa·s (water viscosity)
        double rho = 1000.0; // kg/m³
        double g = 9.81; // m/s²

        // Calculate velocities from pressure gradient
        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            double k_perm = state.Permeability[i, j, k]; // m²
            double phi = state.Porosity[i, j, k];

            if (phi < 0.01) continue; // No flow in non-porous regions

            // Pressure gradient
            double dP_dx = (state.Pressure[i + 1, j, k] - state.Pressure[i - 1, j, k]) / (2 * dx);
            double dP_dy = (state.Pressure[i, j + 1, k] - state.Pressure[i, j - 1, k]) / (2 * dy);
            double dP_dz = (state.Pressure[i, j, k + 1] - state.Pressure[i, j, k - 1]) / (2 * dz);

            // Add force contributions
            double Fx = state.ForceX[i, j, k];
            double Fy = state.ForceY[i, j, k];
            double Fz = state.ForceZ[i, j, k];

            // Darcy velocity: v = -(k/μ)·(∇P - ρF)
            state.VelocityX[i, j, k] = (float)(-(k_perm / mu) * (dP_dx - Fx / rho));
            state.VelocityY[i, j, k] = (float)(-(k_perm / mu) * (dP_dy - Fy / rho));
            state.VelocityZ[i, j, k] = (float)(-(k_perm / mu) * (dP_dz - (Fz + rho * g) / rho));
        }
    }

    /// <summary>
    /// Solve pressure Poisson equation for incompressible flow
    /// ∇²P = -ρ·∇·a (where a is acceleration from forces)
    /// </summary>
    private void SolvePressurePoisson(PhysicoChemState state, double dt)
    {
        int nx = state.Pressure.GetLength(0);
        int ny = state.Pressure.GetLength(1);
        int nz = state.Pressure.GetLength(2);

        // Gauss-Seidel iteration
        int maxIter = 50;
        double omega = 1.5; // SOR relaxation

        double dx = 0.01;
        double dy = 0.01;
        double dz = 0.01;

        for (int iter = 0; iter < maxIter; iter++)
        {
            for (int i = 1; i < nx - 1; i++)
            for (int j = 1; j < ny - 1; j++)
            for (int k = 1; k < nz - 1; k++)
            {
                double P_new = (
                    (state.Pressure[i + 1, j, k] + state.Pressure[i - 1, j, k]) / (dx * dx) +
                    (state.Pressure[i, j + 1, k] + state.Pressure[i, j - 1, k]) / (dy * dy) +
                    (state.Pressure[i, j, k + 1] + state.Pressure[i, j, k - 1]) / (dz * dz)
                ) / (2.0 / (dx * dx) + 2.0 / (dy * dy) + 2.0 / (dz * dz));

                // SOR update
                state.Pressure[i, j, k] = (float)((1 - omega) * state.Pressure[i, j, k] + omega * P_new);
            }
        }
    }
}

/// <summary>
/// Nucleation and crystal growth solver
/// </summary>
public class NucleationSolver
{
    /// <summary>
    /// Update nucleation and crystal growth
    /// </summary>
    public void UpdateNucleation(PhysicoChemState state, List<NucleationSite> sites, double dt)
    {
        // Check for new nucleation events
        foreach (var site in sites)
        {
            if (!site.IsActive) continue;

            // Get local conditions at nucleation site
            var (i, j, k) = FindNearestCell(site.Position, state);

            double temperature = state.Temperature[i, j, k];

            // Calculate supersaturation
            double supersaturation = CalculateSupersaturation(state, site, i, j, k);

            // Check if nucleation occurs
            double nucleationRate = site.CalculateNucleationRate(supersaturation, temperature);

            if (nucleationRate > 0)
            {
                // Create new nucleus
                CreateNucleus(state, site, nucleationRate, dt);
            }
        }

        // Grow existing nuclei
        GrowNuclei(state, dt);
    }

    private (int i, int j, int k) FindNearestCell((double X, double Y, double Z) position, PhysicoChemState state)
    {
        // Simplified - should use actual mesh coordinates
        int nx = state.Temperature.GetLength(0);
        int ny = state.Temperature.GetLength(1);
        int nz = state.Temperature.GetLength(2);

        int i = Math.Clamp((int)(position.X * 10), 0, nx - 1);
        int j = Math.Clamp((int)(position.Y * 10), 0, ny - 1);
        int k = Math.Clamp((int)(position.Z * 10), 0, nz - 1);

        return (i, j, k);
    }

    private double CalculateSupersaturation(PhysicoChemState state, NucleationSite site, int i, int j, int k)
    {
        // Calculate saturation index: S = IAP/K
        // For simplicity, assume supersaturation from concentration
        if (state.Concentrations.Count == 0)
            return 1.0;

        // Get first species concentration as proxy
        var firstSpecies = state.Concentrations.Values.First();
        double concentration = firstSpecies[i, j, k];

        // S = C/C_eq (simplified)
        double equilibriumConc = 0.01; // mol/L
        return concentration / equilibriumConc;
    }

    private void CreateNucleus(PhysicoChemState state, NucleationSite site, double rate, double dt)
    {
        // Probability of nucleation in this timestep
        double prob = rate * dt;

        if (new Random().NextDouble() < prob)
        {
            var nucleus = new Nucleus
            {
                Id = state.ActiveNuclei.Count,
                Position = site.Position,
                Radius = site.InitialRadius,
                MineralType = site.MineralType,
                GrowthRate = 1e-9, // m/s (1 nm/s)
                BirthTime = state.CurrentTime
            };

            state.ActiveNuclei.Add(nucleus);
            Logger.Log($"[NucleationSolver] New nucleus #{nucleus.Id} at {site.Position}");
        }
    }

    private void GrowNuclei(PhysicoChemState state, double dt)
    {
        foreach (var nucleus in state.ActiveNuclei)
        {
            // Get local supersaturation
            var (i, j, k) = FindNearestCell(nucleus.Position, state);

            // Simple linear growth: dr/dt = k_g
            nucleus.Radius += nucleus.GrowthRate * dt;

            // Update mineral volume in cell
            if (state.Minerals.ContainsKey(nucleus.MineralType))
            {
                double volumeChange = (4.0 / 3.0) * Math.PI * nucleus.GrowthRate * nucleus.Radius * nucleus.Radius * dt;
                double cellVolume = 0.01 * 0.01 * 0.01; // m³ (should come from mesh)

                state.Minerals[nucleus.MineralType][i, j, k] += (float)(volumeChange / cellVolume);

                // Reduce porosity
                state.Porosity[i, j, k] -= (float)(volumeChange / cellVolume);
                state.Porosity[i, j, k] = Math.Max(0.01f, state.Porosity[i, j, k]);
            }
        }
    }
}
