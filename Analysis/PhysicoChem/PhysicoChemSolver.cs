// GeoscientistToolkit/Analysis/PhysicoChem/PhysicoChemSolver.cs
//
// Main multiphysics solver for PhysicoChem reactor simulations
// Integrates flow, heat transfer, reactive transport, forces, and nucleation

using System;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Analysis.Thermodynamic;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.PhysicoChem;

/// <summary>
/// Main solver for PHYSICOCHEM multiphysics reactor simulations.
/// Couples:
/// - Flow (Darcy/Navier-Stokes)
/// - Heat transfer
/// - Reactive transport (TOUGH-like)
/// - Force fields (gravity, vortex)
/// - Nucleation and growth
/// </summary>
public class PhysicoChemSolver : SimulatorNodeSupport
{
    private readonly PhysicoChemDataset _dataset;
    private readonly IProgress<(float progress, string message)> _progress;

    // Sub-solvers
    private readonly ReactiveTransportSolver _reactiveTransport;
    private readonly HeatTransferSolver _heatTransfer;
    private readonly FlowSolver _flowSolver;
    private readonly NucleationSolver _nucleationSolver;

    // Coupling to geothermal simulation
    private object _geothermalSimulation; // Reference to coupled simulation

    public PhysicoChemSolver(PhysicoChemDataset dataset, IProgress<(float, string)> progress = null) : this(dataset, progress, null)
    {
    }

    public PhysicoChemSolver(PhysicoChemDataset dataset, IProgress<(float, string)> progress, bool? useNodes) : base(useNodes)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _progress = progress;

        _reactiveTransport = new ReactiveTransportSolver();
        _heatTransfer = new HeatTransferSolver();
        _flowSolver = new FlowSolver();
        _nucleationSolver = new NucleationSolver();

        if (_useNodes)
        {
            Logger.Log("[PhysicoChemSolver] Node Manager integration: ENABLED");
        }
    }

    /// <summary>
    /// Run the full simulation
    /// </summary>
    public void RunSimulation()
    {
        var errors = _dataset.Validate();
        if (errors.Count > 0)
        {
            Logger.LogError($"[PhysicoChemSolver] Validation failed: {string.Join("; ", errors)}");
            return;
        }

        // Initialize
        _dataset.InitializeState();
        var state = _dataset.CurrentState;

        var simParams = _dataset.SimulationParams;
        double t = 0.0;
        double dt = simParams.TimeStep;
        int step = 0;
        int outputStep = 0;

        // Initialize tracking if enabled
        if (simParams.EnableTracking && _dataset.TrackingManager != null)
        {
            _dataset.TrackingManager.Enabled = true;
            _dataset.TrackingManager.SamplingInterval = simParams.TrackingSampleInterval;

            // Add default trackers if none exist
            if (_dataset.TrackingManager.Trackers.Count == 0)
            {
                _dataset.TrackingManager.AddTracker("AverageTemperature", "Average Temperature", "K", TrackerType.Scalar);
                _dataset.TrackingManager.AddTracker("AveragePressure", "Average Pressure", "Pa", TrackerType.Scalar);
                _dataset.TrackingManager.AddTracker("TotalMass", "Total Mass", "kg", TrackerType.Scalar);
                _dataset.TrackingManager.AddTracker("MaxVelocity", "Max Velocity", "m/s", TrackerType.Scalar);
            }
        }

        Logger.Log($"[PhysicoChemSolver] Starting simulation ({simParams.Mode}): {simParams.TotalTime} s, dt={dt} s");

        // Main time loop - support both time-based and step-based modes
        bool shouldContinue = true;
        while (shouldContinue)
        {
            step++;
            t += dt;
            state.CurrentTime = t;

            // Check termination condition based on mode
            if (simParams.Mode == SimulationMode.TimeBased)
            {
                shouldContinue = t < simParams.TotalTime;
                _progress?.Report(((float)(t / simParams.TotalTime), $"Step {step}: t = {t:F2} s"));
            }
            else // StepBased
            {
                shouldContinue = step < simParams.MaxSteps;
                _progress?.Report(((float)step / simParams.MaxSteps, $"Step {step}/{simParams.MaxSteps}: t = {t:F2} s"));
            }

            if (!shouldContinue) break;

            // Operator splitting approach (like TOUGHREACT)
            var newState = state.Clone();

            try
            {
                // 0. Apply parameter sweeps if enabled
                if (simParams.EnableParameterSweep && _dataset.ParameterSweepManager != null)
                {
                    double normalizedTime = simParams.Mode == SimulationMode.TimeBased
                        ? t / simParams.TotalTime
                        : (double)step / simParams.MaxSteps;
                    _dataset.ParameterSweepManager.ApplyToSimulation(_dataset, normalizedTime);
                }

                // 1. Apply forces
                if (simParams.EnableForces && _dataset.Forces.Count > 0)
                {
                    ApplyForces(newState, dt);
                }

                // 2. Solve flow
                if (simParams.EnableFlow)
                {
                    _flowSolver.SolveFlow(newState, dt, _dataset.BoundaryConditions);
                }

                // 3. Solve heat transfer
                if (simParams.EnableHeatTransfer)
                {
                    _heatTransfer.SolveHeat(newState, dt, _dataset.BoundaryConditions);
                }

                // 4. Solve reactive transport
                if (simParams.EnableReactiveTransport)
                {
                    var flowData = CreateFlowData(newState);
                    var transportState = CreateTransportState(newState);

                    transportState = _reactiveTransport.SolveTimeStep(transportState, dt, flowData);

                    UpdateStateFromTransport(newState, transportState);
                }

                // 5. Handle nucleation
                if (simParams.EnableNucleation && _dataset.NucleationSites.Count > 0)
                {
                    _nucleationSolver.UpdateNucleation(newState, _dataset.NucleationSites, dt);
                }

                // 6. Apply boundary conditions
                ApplyBoundaryConditions(newState);

                // 7. Couple with geothermal if enabled
                if (_dataset.CoupleWithGeothermal)
                {
                    CoupleWithGeothermalSimulation(newState);
                }

                state = newState;
                _dataset.CurrentState = state;

                // Update computed properties for tracking
                UpdateComputedProperties(state);

                // Record tracked parameters
                if (simParams.EnableTracking && _dataset.TrackingManager != null)
                {
                    _dataset.TrackingManager.RecordValues(t, _dataset);
                }

                // Output results based on mode
                bool shouldOutput = false;
                if (simParams.Mode == SimulationMode.TimeBased)
                {
                    shouldOutput = t >= outputStep * simParams.OutputInterval;
                }
                else // StepBased
                {
                    shouldOutput = step % simParams.OutputEveryNSteps == 0;
                }

                if (shouldOutput)
                {
                    _dataset.ResultHistory.Add(state.Clone());
                    outputStep++;
                    Logger.Log($"[PhysicoChemSolver] t={t:F2} s, step={step}, saved output #{outputStep}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PhysicoChemSolver] Error at t={t:F2} s: {ex.Message}");
                break;
            }

            // Adaptive time stepping (optional)
            dt = CalculateAdaptiveTimeStep(state, simParams);
        }

        Logger.Log($"[PhysicoChemSolver] Simulation completed: {step} steps, {outputStep} outputs");
    }

    /// <summary>
    /// Run parameter sweep
    /// </summary>
    public ParameterSweepResults RunParameterSweep()
    {
        if (_dataset.ParameterSweep == null)
            throw new InvalidOperationException("No parameter sweep configured");

        var sweepConfig = _dataset.ParameterSweep;
        var results = new ParameterSweepResults { StartTime = DateTime.Now };

        var combinations = sweepConfig.GenerateCombinations();

        Logger.Log($"[PhysicoChemSolver] Starting parameter sweep: {combinations.Count} runs");

        int runId = 0;
        foreach (var paramSet in combinations)
        {
            runId++;
            var run = new ParameterSweepRun
            {
                RunId = runId,
                Parameters = paramSet,
                StartTime = DateTime.Now
            };

            try
            {
                // Apply parameters to dataset
                ApplyParametersToDataset(paramSet);

                // Run simulation
                RunSimulation();

                // Extract output metrics
                run.Outputs = ExtractOutputMetrics();
                run.Success = true;
            }
            catch (Exception ex)
            {
                run.Success = false;
                run.ErrorMessage = ex.Message;
                Logger.LogError($"[PhysicoChemSolver] Sweep run {runId} failed: {ex.Message}");
            }
            finally
            {
                run.EndTime = DateTime.Now;
                results.Runs.Add(run);
            }

            _progress?.Report(((float)runId / combinations.Count, $"Sweep: {runId}/{combinations.Count}"));
        }

        results.EndTime = DateTime.Now;
        Logger.Log($"[PhysicoChemSolver] Parameter sweep completed: {results.SuccessfulRuns}/{combinations.Count} successful");

        return results;
    }

    private void ApplyForces(PhysicoChemState state, double dt)
    {
        int nx = state.Temperature.GetLength(0);
        int ny = state.Temperature.GetLength(1);
        int nz = state.Temperature.GetLength(2);

        var mesh = _dataset.GeneratedMesh;
        double dx = mesh.Spacing.X;
        double dy = mesh.Spacing.Y;
        double dz = mesh.Spacing.Z;

        // Reset forces
        Array.Clear(state.ForceX, 0, state.ForceX.Length);
        Array.Clear(state.ForceY, 0, state.ForceY.Length);
        Array.Clear(state.ForceZ, 0, state.ForceZ.Length);

        foreach (var force in _dataset.Forces)
        {
            if (!force.IsActive) continue;

            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
            {
                double x = mesh.Origin.X + (i + 0.5) * dx;
                double y = mesh.Origin.Y + (j + 0.5) * dy;
                double z = mesh.Origin.Z + (k + 0.5) * dz;

                // Get material density (from domain or state)
                double density = GetDensityAtCell(i, j, k);

                var (fx, fy, fz) = force.CalculateForce(x, y, z, state.CurrentTime, density);

                state.ForceX[i, j, k] += (float)fx;
                state.ForceY[i, j, k] += (float)fy;
                state.ForceZ[i, j, k] += (float)fz;
            }
        }
    }

    private void ApplyBoundaryConditions(PhysicoChemState state)
    {
        var mesh = _dataset.GeneratedMesh;
        int nx = state.Temperature.GetLength(0);
        int ny = state.Temperature.GetLength(1);
        int nz = state.Temperature.GetLength(2);

        foreach (var bc in _dataset.BoundaryConditions)
        {
            if (!bc.IsActive) continue;

            ApplySingleBoundaryCondition(state, bc, mesh);
        }
    }

    private void ApplySingleBoundaryCondition(PhysicoChemState state, BoundaryCondition bc,
        GridMesh3D mesh)
    {
        int nx = state.Temperature.GetLength(0);
        int ny = state.Temperature.GetLength(1);
        int nz = state.Temperature.GetLength(2);

        var domainSize = (mesh.Spacing.X * nx, mesh.Spacing.Y * ny, mesh.Spacing.Z * nz);

        // Apply BC based on location
        switch (bc.Location)
        {
            case BoundaryLocation.XMin:
                ApplyBCToFace(state, bc, 0, -1, -1, true, false, false);
                break;
            case BoundaryLocation.XMax:
                ApplyBCToFace(state, bc, nx - 1, -1, -1, true, false, false);
                break;
            case BoundaryLocation.YMin:
                ApplyBCToFace(state, bc, -1, 0, -1, false, true, false);
                break;
            case BoundaryLocation.YMax:
                ApplyBCToFace(state, bc, -1, ny - 1, -1, false, true, false);
                break;
            case BoundaryLocation.ZMin:
                ApplyBCToFace(state, bc, -1, -1, 0, false, false, true);
                break;
            case BoundaryLocation.ZMax:
                ApplyBCToFace(state, bc, -1, -1, nz - 1, false, false, true);
                break;
        }
    }

    private void ApplyBCToFace(PhysicoChemState state, BoundaryCondition bc,
        int fixedI, int fixedJ, int fixedK, bool isX, bool isY, bool isZ)
    {
        int nx = state.Temperature.GetLength(0);
        int ny = state.Temperature.GetLength(1);
        int nz = state.Temperature.GetLength(2);

        int iStart = fixedI >= 0 ? fixedI : 0;
        int iEnd = fixedI >= 0 ? fixedI : nx - 1;
        int jStart = fixedJ >= 0 ? fixedJ : 0;
        int jEnd = fixedJ >= 0 ? fixedJ : ny - 1;
        int kStart = fixedK >= 0 ? fixedK : 0;
        int kEnd = fixedK >= 0 ? fixedK : nz - 1;

        for (int i = iStart; i <= iEnd; i++)
        for (int j = jStart; j <= jEnd; j++)
        for (int k = kStart; k <= kEnd; k++)
        {
            double value = bc.EvaluateAtTime(state.CurrentTime);

            switch (bc.Variable)
            {
                case BoundaryVariable.Temperature:
                    if (bc.Type == BoundaryType.FixedValue)
                        state.Temperature[i, j, k] = (float)value;
                    break;

                case BoundaryVariable.Pressure:
                    if (bc.Type == BoundaryType.FixedValue)
                        state.Pressure[i, j, k] = (float)value;
                    break;

                case BoundaryVariable.VelocityX:
                    if (bc.Type == BoundaryType.FixedValue || bc.Type == BoundaryType.Inlet)
                        state.VelocityX[i, j, k] = (float)value;
                    else if (bc.Type == BoundaryType.NoSlipWall)
                        state.VelocityX[i, j, k] = 0;
                    break;

                case BoundaryVariable.Concentration:
                    if (bc.SpeciesName != null && state.Concentrations.ContainsKey(bc.SpeciesName))
                    {
                        if (bc.Type == BoundaryType.FixedValue || bc.Type == BoundaryType.Inlet)
                            state.Concentrations[bc.SpeciesName][i, j, k] = (float)value;
                    }
                    break;
            }
        }
    }

    private FlowFieldData CreateFlowData(PhysicoChemState state)
    {
        var mesh = _dataset.GeneratedMesh;

        return new FlowFieldData
        {
            GridSpacing = mesh.Spacing,
            VelocityX = state.VelocityX,
            VelocityY = state.VelocityY,
            VelocityZ = state.VelocityZ,
            Permeability = state.Permeability,
            InitialPermeability = state.Permeability, // TODO: track initial
            Dispersivity = 0.1
        };
    }

    private ReactiveTransportState CreateTransportState(PhysicoChemState state)
    {
        return new ReactiveTransportState
        {
            GridDimensions = (state.Temperature.GetLength(0),
                            state.Temperature.GetLength(1),
                            state.Temperature.GetLength(2)),
            Temperature = state.Temperature,
            Pressure = state.Pressure,
            Porosity = state.Porosity,
            Concentrations = state.Concentrations,
            MineralVolumeFractions = state.Minerals
        };
    }

    private void UpdateStateFromTransport(PhysicoChemState state, ReactiveTransportState transportState)
    {
        state.Concentrations = transportState.Concentrations;
        state.Minerals = transportState.MineralVolumeFractions;
        state.Porosity = transportState.Porosity;
    }

    private double GetDensityAtCell(int i, int j, int k)
    {
        // Get density from domain material properties
        // For now, return water density
        return 1000.0; // kg/m³
    }

    private double CalculateAdaptiveTimeStep(PhysicoChemState state, SimulationParameters simParams)
    {
        // Simple adaptive time stepping based on max velocity
        double maxVelocity = 0;

        for (int i = 0; i < state.VelocityX.GetLength(0); i++)
        for (int j = 0; j < state.VelocityX.GetLength(1); j++)
        for (int k = 0; k < state.VelocityX.GetLength(2); k++)
        {
            double v = Math.Sqrt(
                state.VelocityX[i, j, k] * state.VelocityX[i, j, k] +
                state.VelocityY[i, j, k] * state.VelocityY[i, j, k] +
                state.VelocityZ[i, j, k] * state.VelocityZ[i, j, k]
            );
            maxVelocity = Math.Max(maxVelocity, v);
        }

        // CFL condition
        double dx = _dataset.GeneratedMesh.Spacing.X;
        double dtMax = 0.5 * dx / Math.Max(maxVelocity, 1e-10);

        return Math.Min(dtMax, simParams.TimeStep);
    }

    private void CoupleWithGeothermalSimulation(PhysicoChemState state)
    {
        // Exchange data with geothermal simulation
        // This would require access to the geothermal solver state
        Logger.Log("[PhysicoChemSolver] Coupling with geothermal simulation");
    }

    private void ApplyParametersToDataset(Dictionary<string, double> parameters)
    {
        foreach (var kvp in parameters)
        {
            // Use reflection or direct assignment to set parameter values
            // Based on TargetPath in ParameterRange
            Logger.Log($"[PhysicoChemSolver] Setting {kvp.Key} = {kvp.Value}");
        }
    }

    private Dictionary<string, double> ExtractOutputMetrics()
    {
        var metrics = new Dictionary<string, double>();

        var state = _dataset.CurrentState;

        // Calculate common metrics
        metrics["FinalTemperature_Mean"] = CalculateMean(state.Temperature);
        metrics["FinalPressure_Mean"] = CalculateMean(state.Pressure);
        metrics["FinalPorosity_Mean"] = CalculateMean(state.Porosity);

        // Add concentration metrics
        foreach (var species in state.Concentrations.Keys)
        {
            metrics[$"{species}_Final_Mean"] = CalculateMean(state.Concentrations[species]);
        }

        return metrics;
    }

    private double CalculateMean(float[,,] field)
    {
        double sum = 0;
        int count = 0;

        foreach (var value in field)
        {
            sum += value;
            count++;
        }

        return count > 0 ? sum / count : 0;
    }

    private void UpdateComputedProperties(PhysicoChemState state)
    {
        // Update max velocity
        double maxVelocity = 0;
        for (int i = 0; i < state.VelocityX.GetLength(0); i++)
        for (int j = 0; j < state.VelocityX.GetLength(1); j++)
        for (int k = 0; k < state.VelocityX.GetLength(2); k++)
        {
            double v = Math.Sqrt(
                state.VelocityX[i, j, k] * state.VelocityX[i, j, k] +
                state.VelocityY[i, j, k] * state.VelocityY[i, j, k] +
                state.VelocityZ[i, j, k] * state.VelocityZ[i, j, k]
            );
            maxVelocity = Math.Max(maxVelocity, v);
        }
        state.MaxVelocity = maxVelocity;

        // Calculate total mass (simplified - assumes uniform density and porosity)
        double totalMass = 0;
        double cellVolume = _dataset.GeneratedMesh.Spacing.X *
                          _dataset.GeneratedMesh.Spacing.Y *
                          _dataset.GeneratedMesh.Spacing.Z;
        double fluidDensity = 1000.0; // kg/m³ (water)

        for (int i = 0; i < state.Porosity.GetLength(0); i++)
        for (int j = 0; j < state.Porosity.GetLength(1); j++)
        for (int k = 0; k < state.Porosity.GetLength(2); k++)
        {
            double porosity = state.Porosity[i, j, k];
            double saturation = state.LiquidSaturation[i, j, k];
            totalMass += cellVolume * porosity * saturation * fluidDensity;
        }
        state.TotalMass = totalMass;
    }
}
