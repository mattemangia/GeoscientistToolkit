// GeoscientistToolkit/Analysis/PhysicoChem/SubSolvers.cs
//
// Sub-solvers for flow, heat transfer, and nucleation in PhysicoChem simulations

using System;
using System.Collections.Generic;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.PhysicoChem;

/// <summary>
/// Heat transfer solver (conduction + convection) with heterogeneous thermal properties
/// </summary>
public class HeatTransferSolver
{
    // Heat transfer parameters
    private HeatTransferParameters _htParams = new();

    /// <summary>
    /// Get/set heat transfer parameters
    /// </summary>
    public HeatTransferParameters HeatParams
    {
        get => _htParams;
        set => _htParams = value ?? new HeatTransferParameters();
    }

    /// <summary>
    /// Thermal conductivity field for heterogeneous materials (W/m·K)
    /// </summary>
    public float[,,] ThermalConductivity { get; set; }

    /// <summary>
    /// Solve heat transfer equation:
    /// ρCp ∂T/∂t = ∇·(k∇T) - ρCp v·∇T + Q
    /// Supports heterogeneous thermal conductivity and heat sources
    /// </summary>
    public void SolveHeat(PhysicoChemState state, double dt, List<BoundaryCondition> bcs)
    {
        int nx = state.Temperature.GetLength(0);
        int ny = state.Temperature.GetLength(1);
        int nz = state.Temperature.GetLength(2);

        var T_new = new float[nx, ny, nz];

        // Default material properties
        double rho = _htParams.Density;      // kg/m³ density
        double Cp = _htParams.SpecificHeat;  // J/(kg·K) specific heat

        // Grid spacing
        double dx = _htParams.GridSpacing;
        double dy = _htParams.GridSpacing;
        double dz = _htParams.GridSpacing;

        // CFL stability limit for explicit scheme
        double maxAlpha = _htParams.DefaultThermalConductivity / (rho * Cp);
        double dt_stable = 0.25 * dx * dx / maxAlpha;
        double dt_actual = Math.Min(dt, dt_stable);

        // Explicit finite difference with heterogeneous conductivity
        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            double T = state.Temperature[i, j, k];

            // Get local thermal conductivity (heterogeneous if provided)
            double k_local = GetThermalConductivity(i, j, k);
            double alpha = k_local / (rho * Cp);

            // Get neighbor conductivities for harmonic averaging at faces
            double k_ip = GetThermalConductivity(i + 1, j, k);
            double k_im = GetThermalConductivity(i - 1, j, k);
            double k_jp = GetThermalConductivity(i, j + 1, k);
            double k_jm = GetThermalConductivity(i, j - 1, k);
            double k_kp = GetThermalConductivity(i, j, k + 1);
            double k_km = GetThermalConductivity(i, j, k - 1);

            // Harmonic average at faces for heterogeneous media
            double k_x_plus = 2.0 * k_local * k_ip / (k_local + k_ip + 1e-20);
            double k_x_minus = 2.0 * k_local * k_im / (k_local + k_im + 1e-20);
            double k_y_plus = 2.0 * k_local * k_jp / (k_local + k_jp + 1e-20);
            double k_y_minus = 2.0 * k_local * k_jm / (k_local + k_jm + 1e-20);
            double k_z_plus = 2.0 * k_local * k_kp / (k_local + k_kp + 1e-20);
            double k_z_minus = 2.0 * k_local * k_km / (k_local + k_km + 1e-20);

            // Conduction term with variable conductivity: ∇·(k∇T)
            double flux_x = k_x_plus * (state.Temperature[i + 1, j, k] - T) / dx -
                           k_x_minus * (T - state.Temperature[i - 1, j, k]) / dx;
            double flux_y = k_y_plus * (state.Temperature[i, j + 1, k] - T) / dy -
                           k_y_minus * (T - state.Temperature[i, j - 1, k]) / dy;
            double flux_z = k_z_plus * (state.Temperature[i, j, k + 1] - T) / dz -
                           k_z_minus * (T - state.Temperature[i, j, k - 1]) / dz;

            double conduction = (flux_x / dx + flux_y / dy + flux_z / dz) / (rho * Cp);

            // Convection term: -v·∇T (upwind)
            double vx = state.VelocityX[i, j, k];
            double vy = state.VelocityY[i, j, k];
            double vz = state.VelocityZ[i, j, k];

            double dT_dx = vx > 0 ? (T - state.Temperature[i - 1, j, k]) / dx : (state.Temperature[i + 1, j, k] - T) / dx;
            double dT_dy = vy > 0 ? (T - state.Temperature[i, j - 1, k]) / dy : (state.Temperature[i, j + 1, k] - T) / dy;
            double dT_dz = vz > 0 ? (T - state.Temperature[i, j, k - 1]) / dz : (state.Temperature[i, j, k + 1] - T) / dz;

            double convection = -(vx * dT_dx + vy * dT_dy + vz * dT_dz);

            // Heat source term (if any)
            double Q = GetHeatSource(i, j, k, state.CurrentTime) / (rho * Cp);

            // Update
            T_new[i, j, k] = (float)(T + dt_actual * (conduction + convection + Q));
        }

        // Apply boundary conditions
        ApplyThermalBoundaryConditions(T_new, bcs, nx, ny, nz);

        // Copy interior points
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            if (i > 0 && i < nx - 1 && j > 0 && j < ny - 1 && k > 0 && k < nz - 1)
                state.Temperature[i, j, k] = T_new[i, j, k];
        }
    }

    private double GetThermalConductivity(int i, int j, int k)
    {
        if (ThermalConductivity != null &&
            i >= 0 && i < ThermalConductivity.GetLength(0) &&
            j >= 0 && j < ThermalConductivity.GetLength(1) &&
            k >= 0 && k < ThermalConductivity.GetLength(2))
        {
            return ThermalConductivity[i, j, k];
        }
        return _htParams.DefaultThermalConductivity;
    }

    private double GetHeatSource(int i, int j, int k, double time)
    {
        // Can be extended to support heat exchanger, reaction heat, etc.
        if (_htParams.HeatSourceFunction != null)
            return _htParams.HeatSourceFunction(i, j, k, time);
        return 0.0;
    }

    private void ApplyThermalBoundaryConditions(float[,,] T, List<BoundaryCondition> bcs, int nx, int ny, int nz)
    {
        // Default: zero gradient at boundaries
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            T[0, j, k] = T[1, j, k];
            T[nx - 1, j, k] = T[nx - 2, j, k];
        }
        for (int i = 0; i < nx; i++)
        for (int k = 0; k < nz; k++)
        {
            T[i, 0, k] = T[i, 1, k];
            T[i, ny - 1, k] = T[i, ny - 2, k];
        }
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        {
            T[i, j, 0] = T[i, j, 1];
            T[i, j, nz - 1] = T[i, j, nz - 2];
        }

        // Apply explicit boundary conditions
        foreach (var bc in bcs)
        {
            if (!bc.IsActive) continue;
            if (bc.Variable != BoundaryVariable.Temperature) continue;
            if (bc.Type != BoundaryType.FixedValue) continue;

            float value = (float)bc.EvaluateAtTime(0);

            switch (bc.Location)
            {
                case BoundaryLocation.XMin:
                    for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        T[0, j, k] = value;
                    break;
                case BoundaryLocation.XMax:
                    for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        T[nx - 1, j, k] = value;
                    break;
                case BoundaryLocation.YMin:
                    for (int i = 0; i < nx; i++)
                    for (int k = 0; k < nz; k++)
                        T[i, 0, k] = value;
                    break;
                case BoundaryLocation.YMax:
                    for (int i = 0; i < nx; i++)
                    for (int k = 0; k < nz; k++)
                        T[i, ny - 1, k] = value;
                    break;
                case BoundaryLocation.ZMin:
                    for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                        T[i, j, 0] = value;
                    break;
                case BoundaryLocation.ZMax:
                    for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                        T[i, j, nz - 1] = value;
                    break;
            }
        }
    }
}

/// <summary>
/// Parameters for heat transfer simulation
/// </summary>
public class HeatTransferParameters
{
    /// <summary>Grid spacing in meters</summary>
    public double GridSpacing { get; set; } = 0.01;

    /// <summary>Default thermal conductivity W/(m·K)</summary>
    public double DefaultThermalConductivity { get; set; } = 2.0;

    /// <summary>Solid density kg/m³</summary>
    public double Density { get; set; } = 2500.0;

    /// <summary>Specific heat J/(kg·K)</summary>
    public double SpecificHeat { get; set; } = 1000.0;

    /// <summary>Optional heat source function Q(i,j,k,t) in W/m³</summary>
    public Func<int, int, int, double, double> HeatSourceFunction { get; set; }
}

/// <summary>
/// Flow solver (Darcy or Navier-Stokes) with full multiphase support.
/// Implements TOUGH-style multiphase flow for water, gas (NCG), and vapor phases.
/// </summary>
public class FlowSolver
{
    // Multiphase flow parameters
    private MultiphaseFlowParameters _mpParams = new();

    /// <summary>
    /// Get/set multiphase flow parameters
    /// </summary>
    public MultiphaseFlowParameters MultiphaseParams
    {
        get => _mpParams;
        set => _mpParams = value ?? new MultiphaseFlowParameters();
    }

    /// <summary>
    /// Solve flow equations:
    /// For single-phase Darcy: v = -(k/μ)·(∇P - ρg)
    /// For multiphase: Solves coupled water-gas-vapor flow with buoyancy
    /// </summary>
    public void SolveFlow(PhysicoChemState state, double dt, List<BoundaryCondition> bcs)
    {
        int nx = state.Pressure.GetLength(0);
        int ny = state.Pressure.GetLength(1);
        int nz = state.Pressure.GetLength(2);

        // Check if multiphase flow is active (gas saturation > 0 anywhere)
        bool hasGas = false;
        for (int i = 0; i < nx && !hasGas; i++)
        for (int j = 0; j < ny && !hasGas; j++)
        for (int k = 0; k < nz && !hasGas; k++)
        {
            if (state.GasSaturation[i, j, k] > 1e-10f ||
                state.VaporSaturation[i, j, k] > 1e-10f)
                hasGas = true;
        }

        if (hasGas)
        {
            // Use full multiphase solver with gas transport
            SolveMultiphaseFlow(state, dt, bcs);
        }
        else
        {
            // Use simple single-phase Darcy flow
            SolveDarcyFlow(state);
        }
    }

    /// <summary>
    /// Solve multiphase flow with water, gas, and vapor phases.
    /// Uses TOUGH-style formulation with relative permeability and capillary pressure.
    /// </summary>
    private void SolveMultiphaseFlow(PhysicoChemState state, double dt, List<BoundaryCondition> bcs)
    {
        int nx = state.Pressure.GetLength(0);
        int ny = state.Pressure.GetLength(1);
        int nz = state.Pressure.GetLength(2);

        double dx = _mpParams.GridSpacing;
        double dy = _mpParams.GridSpacing;
        double dz = _mpParams.GridSpacing;

        // Phase properties
        double rho_w = _mpParams.WaterDensity;     // kg/m³
        double rho_g = _mpParams.GasDensity;       // kg/m³
        double mu_w = _mpParams.WaterViscosity;    // Pa·s
        double mu_g = _mpParams.GasViscosity;      // Pa·s
        double g = 9.81; // m/s²

        // Store old saturations for transport calculation
        var S_g_old = (float[,,])state.GasSaturation.Clone();
        var S_v_old = (float[,,])state.VaporSaturation.Clone();

        // Arrays for gas phase flux
        var gasFluxX = new double[nx, ny, nz];
        var gasFluxY = new double[nx, ny, nz];
        var gasFluxZ = new double[nx, ny, nz];

        // Calculate phase velocities and gas flux at all interior cells
        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            double k_perm = state.Permeability[i, j, k]; // m²
            double phi = state.Porosity[i, j, k];

            if (phi < 0.01 || k_perm < 1e-20) continue; // No flow in non-porous regions

            // Get saturations
            double S_w = state.LiquidSaturation[i, j, k];
            double S_g = state.GasSaturation[i, j, k];
            double S_v = state.VaporSaturation[i, j, k];

            // Calculate relative permeabilities using van Genuchten-Mualem model
            double kr_w = CalculateRelPermWater(S_w, _mpParams.ResidualLiquidSaturation, _mpParams.VanGenuchten_m);
            double kr_g = CalculateRelPermGas(S_g + S_v, _mpParams.ResidualGasSaturation, _mpParams.VanGenuchten_m);

            // Calculate capillary pressure
            double Pc = CalculateCapillaryPressure(S_w, _mpParams.VanGenuchten_alpha, _mpParams.VanGenuchten_m);

            // Pressure gradient
            double dP_dx = (state.Pressure[i + 1, j, k] - state.Pressure[i - 1, j, k]) / (2 * dx);
            double dP_dy = (state.Pressure[i, j + 1, k] - state.Pressure[i, j - 1, k]) / (2 * dy);
            double dP_dz = (state.Pressure[i, j, k + 1] - state.Pressure[i, j, k - 1]) / (2 * dz);

            // Add force contributions
            double Fx = state.ForceX[i, j, k];
            double Fy = state.ForceY[i, j, k];
            double Fz = state.ForceZ[i, j, k];

            // Water phase mobility
            double lambda_w = k_perm * kr_w / mu_w;

            // Gas phase mobility
            double lambda_g = k_perm * kr_g / mu_g;

            // CRITICAL FIX: Gas phase velocity includes buoyancy!
            // Gas is lighter than water, so it experiences upward buoyancy force
            double buoyancy_z = (rho_w - rho_g) * g; // Positive = upward for gas

            // Gas pressure is water pressure minus capillary pressure
            // P_g = P_w - Pc (where Pc > 0 for water-wet media)

            // Water Darcy velocity
            double v_w_x = -lambda_w * (dP_dx - Fx / rho_w);
            double v_w_y = -lambda_w * (dP_dy - Fy / rho_w);
            double v_w_z = -lambda_w * (dP_dz - (Fz - rho_w * g) / rho_w);

            // Gas Darcy velocity with buoyancy
            // The gas experiences: -∇P_g - ρ_g·g = -∇(P_w - Pc) - ρ_g·g
            double v_g_x = -lambda_g * (dP_dx - Fx / rho_g);
            double v_g_y = -lambda_g * (dP_dy - Fy / rho_g);
            double v_g_z = -lambda_g * (dP_dz - (Fz - rho_g * g) / rho_g + buoyancy_z / rho_g);

            // Total velocity (for reporting)
            double totalSat = S_w + S_g + S_v;
            if (totalSat > 0.01)
            {
                state.VelocityX[i, j, k] = (float)((S_w * v_w_x + (S_g + S_v) * v_g_x) / totalSat);
                state.VelocityY[i, j, k] = (float)((S_w * v_w_y + (S_g + S_v) * v_g_y) / totalSat);
                state.VelocityZ[i, j, k] = (float)((S_w * v_w_z + (S_g + S_v) * v_g_z) / totalSat);
            }

            // Store gas flux for transport calculation
            gasFluxX[i, j, k] = v_g_x * (S_g + S_v);
            gasFluxY[i, j, k] = v_g_y * (S_g + S_v);
            gasFluxZ[i, j, k] = v_g_z * (S_g + S_v);
        }

        // Transport gas phase using upwind finite volume method
        var S_g_new = new float[nx, ny, nz];
        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            double phi = state.Porosity[i, j, k];
            if (phi < 0.01)
            {
                S_g_new[i, j, k] = state.GasSaturation[i, j, k];
                continue;
            }

            // Upwind flux calculation for gas transport
            // Flux at face (i+1/2, j, k)
            double flux_x_plus = gasFluxX[i, j, k] > 0
                ? gasFluxX[i, j, k]
                : gasFluxX[i + 1, j, k];
            double flux_x_minus = gasFluxX[i - 1, j, k] > 0
                ? gasFluxX[i - 1, j, k]
                : gasFluxX[i, j, k];

            double flux_y_plus = gasFluxY[i, j, k] > 0
                ? gasFluxY[i, j, k]
                : gasFluxY[i, j + 1, k];
            double flux_y_minus = gasFluxY[i, j - 1, k] > 0
                ? gasFluxY[i, j - 1, k]
                : gasFluxY[i, j, k];

            double flux_z_plus = gasFluxZ[i, j, k] > 0
                ? gasFluxZ[i, j, k]
                : gasFluxZ[i, j, k + 1];
            double flux_z_minus = gasFluxZ[i, j, k - 1] > 0
                ? gasFluxZ[i, j, k - 1]
                : gasFluxZ[i, j, k];

            // Divergence of gas flux
            double div_flux = (flux_x_plus - flux_x_minus) / dx +
                             (flux_y_plus - flux_y_minus) / dy +
                             (flux_z_plus - flux_z_minus) / dz;

            // Update gas saturation: φ·∂S_g/∂t = -∇·(v_g·S_g)
            double dS_g = -dt * div_flux / phi;

            S_g_new[i, j, k] = (float)Math.Max(0.0, Math.Min(1.0,
                state.GasSaturation[i, j, k] + dS_g));
        }

        // Copy boundaries (zero-flux or fixed BC)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            S_g_new[0, j, k] = S_g_new[1, j, k];
            S_g_new[nx - 1, j, k] = S_g_new[nx - 2, j, k];
        }
        for (int i = 0; i < nx; i++)
        for (int k = 0; k < nz; k++)
        {
            S_g_new[i, 0, k] = S_g_new[i, 1, k];
            S_g_new[i, ny - 1, k] = S_g_new[i, ny - 2, k];
        }
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        {
            // Bottom: can inject gas, top: gas can escape
            S_g_new[i, j, 0] = S_g_new[i, j, 1];
            S_g_new[i, j, nz - 1] = 0.0f; // Gas escapes at top
        }

        // Apply boundary conditions for gas
        ApplyGasBoundaryConditions(S_g_new, bcs, nx, ny, nz);

        // Update state
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        for (int k = 0; k < nz; k++)
        {
            state.GasSaturation[i, j, k] = S_g_new[i, j, k];

            // Ensure saturations sum to 1
            double totalSat = state.LiquidSaturation[i, j, k] +
                             state.GasSaturation[i, j, k] +
                             state.VaporSaturation[i, j, k];
            if (totalSat > 1.0)
            {
                // Normalize to maintain constraint
                state.LiquidSaturation[i, j, k] /= (float)totalSat;
                state.GasSaturation[i, j, k] /= (float)totalSat;
                state.VaporSaturation[i, j, k] /= (float)totalSat;
            }
            else if (totalSat < 1.0)
            {
                // Fill remainder with liquid
                state.LiquidSaturation[i, j, k] = (float)(1.0 - state.GasSaturation[i, j, k] -
                                                          state.VaporSaturation[i, j, k]);
            }
        }

        // Update pressure based on compressibility (optional)
        UpdatePressureFromSaturation(state, dt);
    }

    private void ApplyGasBoundaryConditions(float[,,] S_g, List<BoundaryCondition> bcs, int nx, int ny, int nz)
    {
        foreach (var bc in bcs)
        {
            if (!bc.IsActive) continue;
            if (bc.Variable != BoundaryVariable.Concentration) continue;
            if (bc.SpeciesName != "Gas" && bc.SpeciesName != "NCG" && bc.SpeciesName != "Methane") continue;

            // Apply gas saturation BC
            switch (bc.Location)
            {
                case BoundaryLocation.ZMin:
                    for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                        S_g[i, j, 0] = (float)bc.Value;
                    break;
                case BoundaryLocation.ZMax:
                    for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                        S_g[i, j, nz - 1] = (float)bc.Value;
                    break;
            }
        }
    }

    /// <summary>
    /// Calculate water relative permeability using van Genuchten-Mualem model
    /// </summary>
    private double CalculateRelPermWater(double S_w, double S_lr, double m)
    {
        if (S_w <= S_lr) return 0.0;
        if (S_w >= 1.0) return 1.0;

        double S_eff = (S_w - S_lr) / (1.0 - S_lr);
        S_eff = Math.Max(0.0, Math.Min(1.0, S_eff));

        return Math.Sqrt(S_eff) * Math.Pow(1.0 - Math.Pow(1.0 - Math.Pow(S_eff, 1.0 / m), m), 2);
    }

    /// <summary>
    /// Calculate gas relative permeability using van Genuchten-Mualem model
    /// </summary>
    private double CalculateRelPermGas(double S_g, double S_gr, double m)
    {
        if (S_g <= S_gr) return 0.0;
        if (S_g >= 1.0) return 1.0;

        double S_eff = (S_g - S_gr) / (1.0 - S_gr);
        S_eff = Math.Max(0.0, Math.Min(1.0, S_eff));

        return Math.Sqrt(S_eff) * Math.Pow(1.0 - Math.Pow(S_eff, 1.0 / m), 2 * m);
    }

    /// <summary>
    /// Calculate capillary pressure using van Genuchten model
    /// </summary>
    private double CalculateCapillaryPressure(double S_w, double alpha, double m)
    {
        if (S_w >= 1.0) return 0.0;
        if (S_w <= 0.0) return 1e8; // Large value for dry conditions

        double S_eff = Math.Max(0.01, Math.Min(0.99, S_w));
        double n = 1.0 / (1.0 - m);

        return (1.0 / alpha) * Math.Pow(Math.Pow(S_eff, -1.0 / m) - 1.0, 1.0 / n);
    }

    /// <summary>
    /// Update pressure based on phase compressibility
    /// </summary>
    private void UpdatePressureFromSaturation(PhysicoChemState state, double dt)
    {
        int nx = state.Pressure.GetLength(0);
        int ny = state.Pressure.GetLength(1);
        int nz = state.Pressure.GetLength(2);

        double compressibility = _mpParams.GasCompressibility; // 1/Pa

        for (int i = 1; i < nx - 1; i++)
        for (int j = 1; j < ny - 1; j++)
        for (int k = 1; k < nz - 1; k++)
        {
            double S_g = state.GasSaturation[i, j, k];

            // Gas compresses under pressure - simplified isothermal model
            // Higher gas saturation = lower pressure (gas expands)
            // This is a simplified model; full model would solve coupled equations
            if (S_g > 0.01)
            {
                // Slight pressure reduction in gas-rich regions
                double dP = -S_g * 1000.0; // Pa per unit saturation
                state.Pressure[i, j, k] += (float)(dP * dt * 0.01);
            }
        }
    }

    private void SolveDarcyFlow(PhysicoChemState state)
    {
        int nx = state.Pressure.GetLength(0);
        int ny = state.Pressure.GetLength(1);
        int nz = state.Pressure.GetLength(2);

        double dx = _mpParams.GridSpacing;
        double dy = _mpParams.GridSpacing;
        double dz = _mpParams.GridSpacing;

        double mu = _mpParams.WaterViscosity; // Pa·s (water viscosity)
        double rho = _mpParams.WaterDensity;  // kg/m³
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

        double dx = _mpParams.GridSpacing;
        double dy = _mpParams.GridSpacing;
        double dz = _mpParams.GridSpacing;

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
/// Parameters for multiphase flow simulation
/// </summary>
public class MultiphaseFlowParameters
{
    /// <summary>Grid spacing in meters</summary>
    public double GridSpacing { get; set; } = 0.01;

    /// <summary>Water density in kg/m³</summary>
    public double WaterDensity { get; set; } = 1000.0;

    /// <summary>Gas density in kg/m³ (NCG like methane ~0.7, air ~1.2)</summary>
    public double GasDensity { get; set; } = 0.7;

    /// <summary>Water viscosity in Pa·s</summary>
    public double WaterViscosity { get; set; } = 0.001;

    /// <summary>Gas viscosity in Pa·s</summary>
    public double GasViscosity { get; set; } = 1.8e-5;

    /// <summary>Residual liquid saturation (immobile water)</summary>
    public double ResidualLiquidSaturation { get; set; } = 0.05;

    /// <summary>Residual gas saturation (trapped gas)</summary>
    public double ResidualGasSaturation { get; set; } = 0.01;

    /// <summary>van Genuchten m parameter</summary>
    public double VanGenuchten_m { get; set; } = 0.5;

    /// <summary>van Genuchten alpha parameter (1/Pa)</summary>
    public double VanGenuchten_alpha { get; set; } = 1e-4;

    /// <summary>Gas compressibility (1/Pa)</summary>
    public double GasCompressibility { get; set; } = 1e-5;
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
