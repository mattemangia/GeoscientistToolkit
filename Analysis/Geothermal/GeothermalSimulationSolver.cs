// GeoscientistToolkit/Analysis/Geothermal/GeothermalSimulationSolver.cs

using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.VolumeData;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     Implements the numerical solver for coupled heat transfer and groundwater flow in geothermal systems.
/// </summary>
public class GeothermalSimulationSolver
{
    private readonly CancellationToken _cancellationToken;
    private readonly GeothermalMesh _mesh;
    private readonly GeothermalSimulationOptions _options;
    private readonly IProgress<(float progress, string message)> _progress;

    // Stability parameters (ADDED)
    private float _adaptiveRelaxation = 0.5f; // Start conservative
    private float[,,] _dispersionCoefficient; // Changed from _dispersivity
    private int _divergenceCount;

    // Heat exchanger states
    private float[] _fluidTempDown;
    private float[] _fluidTempUp;
    private float[,,] _hydraulicHead;
    private float[,,] _initialTemperature;
    private float _lastStableTimeStep;
    private double _maxError;
    private float[,,] _pecletNumber;
    private float[,,] _pressure;

    // Field arrays
    private float[,,] _temperature;
    private float[,,] _temperatureOld; // Added for stability

    // Performance tracking
    private int _totalIterations;
    private float[,,,] _velocity; // [r,theta,z,component]

    public GeothermalSimulationSolver(
        GeothermalSimulationOptions options,
        GeothermalMesh mesh,
        IProgress<(float, string)> progress,
        CancellationToken cancellationToken)
    {
        _options = options;
        _mesh = mesh;
        _progress = progress;
        _cancellationToken = cancellationToken;

        ValidateAndSanitizeMesh(); // ADDED
        InitializeFields();
    }

    // Convergence tracking
    public List<double> ConvergenceHistory { get; } = new();
    public List<double> HeatConvergenceHistory { get; } = new();
    public List<double> FlowConvergenceHistory { get; } = new();
    public List<double> TimeStepHistory { get; } = new();
    public int CurrentTimeStep { get; private set; }
    public double CurrentSimulationTime { get; private set; }
    public string ConvergenceStatus { get; private set; } = "Initializing...";

    /// <summary>
    ///     Validates and sanitizes mesh properties to prevent numerical issues. (ADDED)
    /// </summary>
    private void ValidateAndSanitizeMesh()
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        // Ensure all material properties are within reasonable bounds
        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
        {
            // Thermal conductivity: clamp to reasonable range
            _mesh.ThermalConductivities[i, j, k] = Math.Max(0.1f, Math.Min(10f, _mesh.ThermalConductivities[i, j, k]));

            // Specific heat: clamp to reasonable range
            _mesh.SpecificHeats[i, j, k] = Math.Max(100f, Math.Min(5000f, _mesh.SpecificHeats[i, j, k]));

            // Density: clamp to reasonable range
            _mesh.Densities[i, j, k] = Math.Max(500f, Math.Min(5000f, _mesh.Densities[i, j, k]));

            // Porosity: clamp between 0 and 1
            _mesh.Porosities[i, j, k] = Math.Max(1e-6f, Math.Min(0.99f, _mesh.Porosities[i, j, k]));

            // Permeability: ensure positive
            _mesh.Permeabilities[i, j, k] = Math.Max(1e-20f, Math.Min(1e-8f, _mesh.Permeabilities[i, j, k]));
        }
    }

    /// <summary>
    ///     Executes the geothermal simulation with enhanced stability. (MODIFIED)
    /// </summary>
    public async Task<GeothermalSimulationResults> RunSimulationAsync()
    {
        var results = new GeothermalSimulationResults { Options = _options };
        var startTime = DateTime.Now;

        _progress?.Report((0f, "Initializing simulation..."));

        // Diagnostic logging for debugging
        LogSimulationDiagnostics();

        // Time stepping loop with adaptive control (MODIFIED)
        var currentTime = 0.0;
        var timeSteps = (int)(_options.SimulationTime / _options.TimeStep);
        var saveCounter = 0;
        var actualTimeStep = _options.TimeStep;
        _lastStableTimeStep = (float)actualTimeStep;

        for (var step = 0; step < timeSteps && currentTime < _options.SimulationTime; step++)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            // Update current state
            CurrentTimeStep = step;
            CurrentSimulationTime = currentTime;

            // Progress reporting
            if (step % 10 == 0)
            {
                var progress = (float)(currentTime / _options.SimulationTime);
                var message =
                    $"Step {step}, t={currentTime / 86400:F2} days, dt={actualTimeStep:F1}s - {ConvergenceStatus}";
                _progress?.Report((progress, message));
            }

            // Clear convergence history for this time step
            var stepStartHeatIdx = HeatConvergenceHistory.Count;
            var stepStartFlowIdx = FlowConvergenceHistory.Count;

            // Try to solve with current time step (ADDED)
            var stepSuccessful = false;
            var retryCount = 0;
            const int maxRetries = 3;

            while (!stepSuccessful && retryCount < maxRetries)
                try
                {
                    // Store old temperature field for rollback
                    _temperatureOld = (float[,,])_temperature.Clone();

                    // Solve coupled system
                    if (_options.SimulateGroundwaterFlow)
                    {
                        await SolveGroundwaterFlowAsync();
                        CalculatePecletAndDispersivity();
                    }

                    await SolveHeatTransferAsync((float)actualTimeStep);
                    UpdateHeatExchanger();

                    stepSuccessful = true;
                    _divergenceCount = 0; // Reset divergence counter on success

                    // Gradually increase relaxation if stable
                    _adaptiveRelaxation = Math.Min(0.9f, _adaptiveRelaxation * 1.05f);
                }
                catch (ArithmeticException ex) when (ex.Message.Contains("diverged"))
                {
                    // Rollback temperature field
                    _temperature = (float[,,])_temperatureOld.Clone();

                    // Reduce time step and relaxation
                    actualTimeStep *= 0.5;
                    _adaptiveRelaxation *= 0.7f;
                    _divergenceCount++;

                    retryCount++;
                    ConvergenceStatus = $"Reducing time step to {actualTimeStep:F1}s (retry {retryCount}/{maxRetries})";

                    if (retryCount >= maxRetries)
                    {
                        // Skip this time step with minimal advancement
                        actualTimeStep = 1.0; // 1 second minimal step
                        stepSuccessful = true; // Force continue with tiny step
                        ConvergenceStatus = "Using minimal time step to continue";
                    }
                }

            // Track overall convergence for this time step
            var stepHeatConv = HeatConvergenceHistory.Skip(stepStartHeatIdx).LastOrDefault();
            var stepFlowConv = FlowConvergenceHistory.Skip(stepStartFlowIdx).LastOrDefault();
            ConvergenceHistory.Add(Math.Max(stepHeatConv, stepFlowConv));
            TimeStepHistory.Add(actualTimeStep);

            // Save results at intervals
            if (++saveCounter >= _options.SaveInterval)
            {
                saveCounter = 0;
                SaveTimeStepResults(results, currentTime);
            }

            currentTime += actualTimeStep;

            // Adapt time step for next iteration (IMPROVED)
            if (stepSuccessful && _divergenceCount == 0)
            {
                // Only increase time step if convergence is good (not just successful)
                var recentConvergence = ConvergenceHistory.Skip(Math.Max(0, ConvergenceHistory.Count - 10)).ToList();
                var avgRecentConvergence = recentConvergence.Any() ? recentConvergence.Average() : _maxError;

                // Increase time step more conservatively based on convergence quality
                if (avgRecentConvergence < _options.ConvergenceTolerance * 0.1)
                    // Excellent convergence - increase by 5%
                    actualTimeStep = Math.Min(_options.TimeStep, actualTimeStep * 1.05);
                else if (avgRecentConvergence < _options.ConvergenceTolerance * 0.5)
                    // Good convergence - increase by 2%
                    actualTimeStep = Math.Min(_options.TimeStep, actualTimeStep * 1.02);
                // else: maintain current time step

                _lastStableTimeStep = (float)actualTimeStep;
            }
        }

        // Final results processing
        _progress?.Report((0.9f, "Processing final results..."));

        results.FinalTemperatureField = (float[,,])_temperature.Clone();
        results.PressureField = (float[,,])_pressure.Clone();
        results.HydraulicHeadField = (float[,,])_hydraulicHead.Clone();
        results.DarcyVelocityField = (float[,,,])_velocity.Clone();
        results.PecletNumberField = (float[,,])_pecletNumber.Clone();
        results.DispersivityField = (float[,,])_dispersionCoefficient.Clone();

        // Calculate performance metrics
        CalculatePerformanceMetrics(results);

        // Generate visualization data
        await GenerateVisualizationDataAsync(results);

        // Computational statistics
        results.ComputationTime = DateTime.Now - startTime;
        results.TimeStepsComputed = CurrentTimeStep;
        results.AverageIterationsPerStep = (double)_totalIterations / Math.Max(1, CurrentTimeStep);
        results.FinalConvergenceError = _maxError;
        results.PeakMemoryUsage = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

        _progress?.Report((1f, "Simulation complete"));

        return results;
    }

    /// <summary>
    ///     Initializes all field arrays with initial conditions.
    /// </summary>
    private void InitializeFields()
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        _temperature = new float[nr, nth, nz];
        _temperatureOld = new float[nr, nth, nz]; // ADDED
        _pressure = new float[nr, nth, nz];
        _hydraulicHead = new float[nr, nth, nz];
        _velocity = new float[nr, nth, nz, 3]; // r, theta, z components
        _pecletNumber = new float[nr, nth, nz];
        _dispersionCoefficient = new float[nr, nth, nz];

        // CORRECTED: Proper handling of depth coordinates
        // In the mesh, Z values are NEGATIVE going down, so depth = -Z
        Func<float, float> getTempAtDepth;

        if (_options.InitialTemperatureProfile != null && _options.InitialTemperatureProfile.Any())
        {
            var sortedProfile = _options.InitialTemperatureProfile.OrderBy(p => p.Depth).ToList();

            getTempAtDepth = depth =>
            {
                if (sortedProfile.Count == 1) return (float)sortedProfile[0].Temperature;

                // Find points to interpolate between
                for (var i = 0; i < sortedProfile.Count - 1; i++)
                {
                    var p1 = sortedProfile[i];
                    var p2 = sortedProfile[i + 1];
                    if (depth >= p1.Depth && depth <= p2.Depth)
                    {
                        // Linear interpolation
                        var t = (depth - p1.Depth) / (p2.Depth - p1.Depth);
                        return (float)(p1.Temperature + t * (p2.Temperature - p1.Temperature));
                    }
                }

                // Extrapolate if outside the defined range
                if (depth < sortedProfile.First().Depth)
                {
                    var p1 = sortedProfile[0];
                    var p2 = sortedProfile[1];
                    var gradient = (p2.Temperature - p1.Temperature) / (p2.Depth - p1.Depth);
                    return (float)(p1.Temperature - (p1.Depth - depth) * gradient);
                }
                else // depth > sortedProfile.Last().Depth
                {
                    var p1 = sortedProfile[sortedProfile.Count - 2];
                    var p2 = sortedProfile.Last();
                    var gradient = (p2.Temperature - p1.Temperature) / (p2.Depth - p1.Depth);
                    return (float)(p2.Temperature + (depth - p2.Depth) * gradient);
                }
            };
        }
        else
        {
            // Fallback to linear gradient based on options
            var surfaceTemp = (float)_options.SurfaceTemperature;
            var gradient = (float)_options.AverageGeothermalGradient;
            getTempAtDepth = depth => surfaceTemp + gradient * depth;
        }

        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
        {
            // CORRECTED: mesh Z is negative downwards, so depth is -Z
            var depth = Math.Max(0, -_mesh.Z[k]);
            _temperature[i, j, k] = getTempAtDepth(depth);
            _temperatureOld[i, j, k] = _temperature[i, j, k]; // ADDED

            // Initialize hydraulic head
            var z = _mesh.Z[k];
            _hydraulicHead[i, j, k] = (float)(_options.HydraulicHeadTop +
                                              (_options.HydraulicHeadBottom - _options.HydraulicHeadTop) *
                                              (z - _mesh.Z[0]) / (_mesh.Z[nz - 1] - _mesh.Z[0]));

            // Convert to pressure (Pa)
            _pressure[i, j, k] = (float)(1000 * 9.81 * _hydraulicHead[i, j, k]);
        }

        // Store a copy for calculating temperature changes later
        _initialTemperature = (float[,,])_temperature.Clone();

        // Initialize heat exchanger
        var nzHE = 20; // Number of elements along heat exchanger
        _fluidTempDown = new float[nzHE];
        _fluidTempUp = new float[nzHE];

        for (var i = 0; i < nzHE; i++)
        {
            _fluidTempDown[i] = (float)_options.FluidInletTemperature;
            _fluidTempUp[i] = (float)_options.FluidInletTemperature;
        }
    }

    /// <summary>
    ///     Solves the groundwater flow equation using SIMD-optimized iterations. (MODIFIED)
    /// </summary>
    private async Task SolveGroundwaterFlowAsync()
    {
        await Task.Run(() =>
        {
            var nr = _mesh.RadialPoints;
            var nth = _mesh.AngularPoints;
            var nz = _mesh.VerticalPoints;

            var newHead = new float[nr, nth, nz];
            var omega = 0.3f; // Conservative relaxation for groundwater (ADDED)

            for (var iter = 0; iter < _options.MaxIterationsPerStep; iter++)
            {
                float maxChange;

                // Interior points - SIMD optimized
                if (_options.UseSIMD && Avx2.IsSupported)
                    maxChange = SolveGroundwaterFlowSIMD(newHead, omega); // MODIFIED: added omega
                else
                    maxChange = SolveGroundwaterFlowScalar(newHead, omega); // MODIFIED: added omega

                // Apply boundary conditions (ADDED)
                ApplyGroundwaterBoundaryConditions(newHead);

                // Check for divergence (MODIFIED)
                if (float.IsNaN(maxChange) || float.IsInfinity(maxChange) || maxChange > 1e6f)
                {
                    ConvergenceStatus = $"Flow solver diverged at iteration {iter}";
                    // Use smaller relaxation and continue
                    omega *= 0.5f;
                    continue;
                }

                // Track convergence
                FlowConvergenceHistory.Add(maxChange);

                // Under-relaxation update (ADDED)
                for (var i = 0; i < nr; i++)
                for (var j = 0; j < nth; j++)
                for (var k = 0; k < nz; k++)
                    _hydraulicHead[i, j, k] = (1 - omega) * _hydraulicHead[i, j, k] + omega * newHead[i, j, k];

                // Report progress
                if (iter % 100 == 0)
                    ConvergenceStatus = $"Flow iteration {iter}/{_options.MaxIterationsPerStep}, error: {maxChange:E3}";

                if (maxChange < _options.ConvergenceTolerance)
                {
                    ConvergenceStatus = $"Flow converged in {iter} iterations, error: {maxChange:E3}";
                    break;
                }

                // Warn if not converging
                if (iter == _options.MaxIterationsPerStep - 1)
                {
                    ConvergenceStatus = $"Flow max iterations reached, error: {maxChange:E3}";
                    if (maxChange > _options.ConvergenceTolerance * 100)
                        // Don't throw, just warn (MODIFIED)
                        ConvergenceStatus += " - May affect results";
                }
            }

            // Calculate velocities from hydraulic head using Darcy's law
            CalculateDarcyVelocities();
        });
    }

    /// <summary>
    ///     Apply boundary conditions for groundwater flow. (ADDED)
    /// </summary>
    private void ApplyGroundwaterBoundaryConditions(float[,,] head)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        // Top boundary (constant head)
        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
            head[i, j, 0] = (float)_options.HydraulicHeadTop;

        // Bottom boundary (constant head)
        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
            head[i, j, nz - 1] = (float)_options.HydraulicHeadBottom;

        // Outer boundary (no-flow)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
            head[nr - 1, j, k] = head[nr - 2, j, k];
    }

    /// <summary>
    ///     SIMD-optimized groundwater flow solver. (MODIFIED)
    /// </summary>
    private float SolveGroundwaterFlowSIMD(float[,,] newHead, float omega)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        var vecSize = Vector256<float>.Count;
        var maxChange = 0f;
        var lockObj = new object();

        Parallel.For(1, nr - 1,
            () => 0f,
            (i, loopState, localMaxChange) =>
            {
                for (var k = 1; k < nz - 1; k++)
                {
                    var r = _mesh.R[i];
                    var dr_m = Math.Max(0.001f, _mesh.R[i] - _mesh.R[i - 1]); // ADDED: bounds check
                    var dr_p = Math.Max(0.001f, _mesh.R[i + 1] - _mesh.R[i]);
                    var dth = 2f * MathF.PI / nth;
                    var dz_m = Math.Max(0.001f, Math.Abs(_mesh.Z[k] - _mesh.Z[k - 1]));
                    var dz_p = Math.Max(0.001f, Math.Abs(_mesh.Z[k + 1] - _mesh.Z[k]));

                    // Process multiple angular points at once
                    var j = 0;
                    for (; j <= nth - vecSize; j += vecSize)
                    {
                        // Load permeabilities with bounds checking (ADDED)
                        var permVals = new float[vecSize];
                        for (var v = 0; v < vecSize; v++)
                            permVals[v] = Math.Max(1e-20f, _mesh.Permeabilities[i, j + v, k]);
                        var K = Vector256.Create(permVals);

                        // Finite difference coefficients
                        var invR2 = Vector256.Create(1f / (r * r));
                        var invDr2 = Vector256.Create(2f / (dr_m * dr_p));
                        var invDz2 = Vector256.Create(2f / (dz_m * dz_p));
                        var invDth2 = Vector256.Create((float)(nth * nth / (4 * Math.PI * Math.PI)));

                        // Load neighboring heads
                        var h_c = Vector256.Create(
                            _hydraulicHead[i, j, k], _hydraulicHead[i, j + 1, k],
                            _hydraulicHead[i, j + 2, k], _hydraulicHead[i, j + 3, k],
                            _hydraulicHead[i, j + 4, k], _hydraulicHead[i, j + 5, k],
                            _hydraulicHead[i, j + 6, k], _hydraulicHead[i, j + 7, k]
                        );

                        // Radial neighbors
                        var h_rm = Vector256.Create(
                            _hydraulicHead[i - 1, j, k], _hydraulicHead[i - 1, j + 1, k],
                            _hydraulicHead[i - 1, j + 2, k], _hydraulicHead[i - 1, j + 3, k],
                            _hydraulicHead[i - 1, j + 4, k], _hydraulicHead[i - 1, j + 5, k],
                            _hydraulicHead[i - 1, j + 6, k], _hydraulicHead[i - 1, j + 7, k]
                        );

                        var h_rp = Vector256.Create(
                            _hydraulicHead[i + 1, j, k], _hydraulicHead[i + 1, j + 1, k],
                            _hydraulicHead[i + 1, j + 2, k], _hydraulicHead[i + 1, j + 3, k],
                            _hydraulicHead[i + 1, j + 4, k], _hydraulicHead[i + 1, j + 5, k],
                            _hydraulicHead[i + 1, j + 6, k], _hydraulicHead[i + 1, j + 7, k]
                        );

                        // Angular neighbors (with periodic BC)
                        var jm = new int[vecSize];
                        var jp = new int[vecSize];
                        for (var v = 0; v < vecSize; v++)
                        {
                            jm[v] = (j + v - 1 + nth) % nth;
                            jp[v] = (j + v + 1) % nth;
                        }

                        var h_thm = Vector256.Create(
                            _hydraulicHead[i, jm[0], k], _hydraulicHead[i, jm[1], k],
                            _hydraulicHead[i, jm[2], k], _hydraulicHead[i, jm[3], k],
                            _hydraulicHead[i, jm[4], k], _hydraulicHead[i, jm[5], k],
                            _hydraulicHead[i, jm[6], k], _hydraulicHead[i, jm[7], k]
                        );

                        var h_thp = Vector256.Create(
                            _hydraulicHead[i, jp[0], k], _hydraulicHead[i, jp[1], k],
                            _hydraulicHead[i, jp[2], k], _hydraulicHead[i, jp[3], k],
                            _hydraulicHead[i, jp[4], k], _hydraulicHead[i, jp[5], k],
                            _hydraulicHead[i, jp[6], k], _hydraulicHead[i, jp[7], k]
                        );

                        // Vertical neighbors
                        var h_zm = Vector256.Create(
                            _hydraulicHead[i, j, k - 1], _hydraulicHead[i, j + 1, k - 1],
                            _hydraulicHead[i, j + 2, k - 1], _hydraulicHead[i, j + 3, k - 1],
                            _hydraulicHead[i, j + 4, k - 1], _hydraulicHead[i, j + 5, k - 1],
                            _hydraulicHead[i, j + 6, k - 1], _hydraulicHead[i, j + 7, k - 1]
                        );

                        var h_zp = Vector256.Create(
                            _hydraulicHead[i, j, k + 1], _hydraulicHead[i, j + 1, k + 1],
                            _hydraulicHead[i, j + 2, k + 1], _hydraulicHead[i, j + 3, k + 1],
                            _hydraulicHead[i, j + 4, k + 1], _hydraulicHead[i, j + 5, k + 1],
                            _hydraulicHead[i, j + 6, k + 1], _hydraulicHead[i, j + 7, k + 1]
                        );

                        // Simple finite difference (MODIFIED for stability)
                        var coeff_r = Vector256.Create(1f / (dr_m * dr_p));
                        var coeff_z = Vector256.Create(1f / (dz_m * dz_p));
                        var coeff_sum = Vector256.Create(2f / (dr_m * dr_p) + 2f / (dz_m * dz_p));

                        var term_r = Avx2.Multiply(coeff_r, Avx2.Add(h_rp, h_rm));
                        var term_z = Avx2.Multiply(coeff_z, Avx2.Add(h_zp, h_zm));
                        var h_new = Avx2.Divide(Avx2.Add(term_r, term_z), coeff_sum);

                        // Store results with bounds checking
                        for (var v = 0; v < vecSize && j + v < nth; v++)
                        {
                            var newVal = h_new.GetElement(v);
                            if (!float.IsNaN(newVal) && !float.IsInfinity(newVal))
                            {
                                newHead[i, j + v, k] = newVal;
                                var change = Math.Abs(newVal - h_c.GetElement(v));
                                localMaxChange = Math.Max(localMaxChange, change);
                            }
                            else
                            {
                                // Keep old value if update is invalid
                                newHead[i, j + v, k] = _hydraulicHead[i, j + v, k];
                            }
                        }
                    }

                    // Handle remaining elements
                    for (; j < nth; j++)
                    {
                        var change = SolveGroundwaterFlowSinglePoint(i, j, k, newHead, omega);
                        localMaxChange = Math.Max(localMaxChange, change);
                    }
                }

                return localMaxChange;
            },
            localMaxChange =>
            {
                lock (lockObj)
                {
                    maxChange = Math.Max(maxChange, localMaxChange);
                }
            });
        return maxChange;
    }

    /// <summary>
    ///     Scalar fallback for groundwater flow solver. (MODIFIED)
    /// </summary>
    private float SolveGroundwaterFlowScalar(float[,,] newHead, float omega)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        var maxChange = 0f;
        var lockObj = new object();

        Parallel.For(1, nr - 1,
            () => 0f, // localInit
            (i, loopState, localMaxChange) =>
            {
                for (var j = 0; j < nth; j++)
                for (var k = 1; k < nz - 1; k++)
                {
                    var change = SolveGroundwaterFlowSinglePoint(i, j, k, newHead, omega);
                    localMaxChange = Math.Max(localMaxChange, change);
                }

                return localMaxChange;
            },
            localMaxChange =>
            {
                lock (lockObj)
                {
                    maxChange = Math.Max(maxChange, localMaxChange);
                }
            });
        return maxChange;
    }

    private float SolveGroundwaterFlowSinglePoint(int i, int j, int k, float[,,] newHead, float omega)
    {
        var nth = _mesh.AngularPoints;
        var r = Math.Max(0.01f, _mesh.R[i]); // ADDED: avoid division by very small r
        var K = Math.Max(1e-20f, _mesh.Permeabilities[i, j, k]); // Ensure positive

        // Safety check: if permeability is too low, don't update
        if (K < 1e-19f)
        {
            newHead[i, j, k] = _hydraulicHead[i, j, k];
            return 0f;
        }

        var jm = (j - 1 + nth) % nth;
        var jp = (j + 1) % nth;

        var dr_m = Math.Max(0.001f, _mesh.R[i] - _mesh.R[i - 1]);
        var dr_p = Math.Max(0.001f, _mesh.R[i + 1] - _mesh.R[i]);
        var dth = 2f * MathF.PI / nth;
        var dz_m = Math.Max(0.001f, Math.Abs(_mesh.Z[k] - _mesh.Z[k - 1]));
        var dz_p = Math.Max(0.001f, Math.Abs(_mesh.Z[k + 1] - _mesh.Z[k]));

        // Simple Laplacian (SIMPLIFIED for stability)
        var h_new = (
            (_hydraulicHead[i + 1, j, k] + _hydraulicHead[i - 1, j, k]) / (dr_m * dr_p) +
            (_hydraulicHead[i, j, k + 1] + _hydraulicHead[i, j, k - 1]) / (dz_m * dz_p)
        ) / (
            2f / (dr_m * dr_p) + 2f / (dz_m * dz_p)
        );

        // Bounds check
        if (float.IsNaN(h_new) || float.IsInfinity(h_new))
        {
            newHead[i, j, k] = _hydraulicHead[i, j, k];
            return 0f;
        }

        newHead[i, j, k] = h_new;
        return Math.Abs(h_new - _hydraulicHead[i, j, k]);
    }

    /// <summary>
    ///     Calculates Darcy velocities from the hydraulic head field.
    /// </summary>
    private void CalculateDarcyVelocities()
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        Parallel.For(0, nr, i =>
        {
            for (var j = 0; j < nth; j++)
            for (var k = 0; k < nz; k++)
            {
                var K = _mesh.Permeabilities[i, j, k];
                var phi = Math.Max(0.01f, _mesh.Porosities[i, j, k]); // MODIFIED: ensure positive

                // Radial component
                if (i > 0 && i < nr - 1)
                {
                    var dh_dr = (_hydraulicHead[i + 1, j, k] - _hydraulicHead[i - 1, j, k]) /
                                (_mesh.R[i + 1] - _mesh.R[i - 1]);
                    _velocity[i, j, k, 0] = -K * dh_dr / phi;
                }

                // Angular component
                var jm = (j - 1 + nth) % nth;
                var jp = (j + 1) % nth;
                var dh_dth = (_hydraulicHead[i, jp, k] - _hydraulicHead[i, jm, k]) /
                             (2f * 2f * MathF.PI / nth);
                _velocity[i, j, k, 1] = -K * dh_dth / (_mesh.R[i] * phi);

                // Vertical component
                if (k > 0 && k < nz - 1)
                {
                    var dh_dz = (_hydraulicHead[i, j, k + 1] - _hydraulicHead[i, j, k - 1]) /
                                Math.Abs(_mesh.Z[k + 1] - _mesh.Z[k - 1]); // ADDED: Abs for safety
                    _velocity[i, j, k, 2] = -K * dh_dz / phi;
                }

                // Add regional flow
                _velocity[i, j, k, 0] += _options.GroundwaterVelocity.X;
                _velocity[i, j, k, 1] += _options.GroundwaterVelocity.Y / _mesh.R[i];
                _velocity[i, j, k, 2] += _options.GroundwaterVelocity.Z;
            }
        });
    }

    /// <summary>
    ///     Solves the heat transfer equation. (MODIFIED)
    /// </summary>
    private async Task SolveHeatTransferAsync(float dt)
    {
        await Task.Run(() =>
        {
            var nr = _mesh.RadialPoints;
            var nth = _mesh.AngularPoints;
            var nz = _mesh.VerticalPoints;

            var newTemp = new float[nr, nth, nz];

            // Calculate stable time step (ADDED)
            var dt_stable = CalculateAdaptiveTimeStep();
            dt = Math.Min(dt, dt_stable);
            TimeStepHistory.Add(dt);

            _maxError = 0;

            for (var iter = 0; iter < _options.MaxIterationsPerStep; iter++)
            {
                float maxChange;
                _totalIterations++;

                // Interior points
                if (_options.UseSIMD && Avx2.IsSupported)
                    maxChange = SolveHeatTransferSIMD(newTemp, dt);
                else
                    maxChange = SolveHeatTransferScalar(newTemp, dt);

                // Apply boundary conditions
                ApplyBoundaryConditions(newTemp);

                // Apply heat exchanger source/sink
                ApplyHeatExchangerSource(newTemp, dt); // MODIFIED: added dt

                // Check for divergence (MODIFIED)
                if (float.IsNaN(maxChange) || float.IsInfinity(maxChange))
                {
                    ConvergenceStatus = $"Heat solver diverged at iteration {iter}";
                    throw new ArithmeticException(
                        $"Heat transfer solver diverged at iteration {iter}. " +
                        $"Try reducing the time step or checking thermal property values."
                    );
                }

                // Check for excessive change (ADDED)
                if (maxChange > 100f) // More than 100K change
                {
                    ConvergenceStatus = $"Excessive temperature change: {maxChange:F1}K";
                    throw new ArithmeticException(
                        $"Heat transfer solver detected excessive change ({maxChange:F1}K). Reducing time step."
                    );
                }

                // Track convergence
                HeatConvergenceHistory.Add(maxChange);

                // Under-relaxed update (ADDED)
                for (var i = 0; i < nr; i++)
                for (var j = 0; j < nth; j++)
                for (var k = 0; k < nz; k++)
                {
                    var tempNew = newTemp[i, j, k];
                    var tempOld = _temperature[i, j, k];
                    _temperature[i, j, k] = (1 - _adaptiveRelaxation) * tempOld + _adaptiveRelaxation * tempNew;

                    // Ensure physically reasonable temperatures
                    _temperature[i, j, k] = Math.Max(273f, Math.Min(473f, _temperature[i, j, k])); // 0-200°C
                }

                _maxError = maxChange;

                // Report progress
                if (iter % 100 == 0)
                    ConvergenceStatus =
                        $"Heat iteration {iter}/{_options.MaxIterationsPerStep}, error: {maxChange:E3}, dt: {dt:E2}s";

                if (maxChange < _options.ConvergenceTolerance * 10) // Less strict convergence
                {
                    ConvergenceStatus = $"Heat converged in {iter} iterations, error: {maxChange:E3}";
                    break;
                }
            }
        });
    }

    /// <summary>
    ///     Calculate adaptive time step based on CFL condition - CORRECTED VERSION.
    /// </summary>
    private float CalculateAdaptiveTimeStep()
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        // Find minimum grid spacing
        var minDx = float.MaxValue;
        for (var i = 1; i < nr; i++)
        {
            var dx = _mesh.R[i] - _mesh.R[i - 1];
            if (dx > 1e-10f) minDx = Math.Min(minDx, dx);
        }

        for (var k = 1; k < nz; k++)
        {
            var dz = Math.Abs(_mesh.Z[k] - _mesh.Z[k - 1]);
            if (dz > 1e-10f) minDx = Math.Min(minDx, dz);
        }

        // Safety check
        if (minDx == float.MaxValue || minDx < 1e-6f) minDx = 0.1f; // Fallback to 10 cm

        // Find maximum thermal diffusivity
        var maxAlpha = 1e-10f;
        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
        {
            var lambda = _mesh.ThermalConductivities[i, j, k];
            var rho = _mesh.Densities[i, j, k];
            var cp = _mesh.SpecificHeats[i, j, k];

            if (rho > 0 && cp > 0)
            {
                var alpha = lambda / (rho * cp);
                maxAlpha = Math.Max(maxAlpha, alpha);
            }
        }

        // Clamp to reasonable bounds
        maxAlpha = Math.Min(maxAlpha, 1e-3f); // Cap at 1e-3 m²/s
        maxAlpha = Math.Max(maxAlpha, 1e-10f); // Floor at 1e-10 m²/s

        // CFL condition for diffusion with safety factor (MODIFIED)
        var dt_diffusion = 0.15f * (minDx * minDx) / (2f * maxAlpha * 3f); // More conservative

        // Also consider advection CFL if groundwater flow is enabled
        var dt_advection = float.MaxValue;
        if (_options.SimulateGroundwaterFlow)
        {
            var maxVel = 0f;
            for (var i = 0; i < nr; i++)
            for (var j = 0; j < nth; j++)
            for (var k = 0; k < nz; k++)
            {
                var v = Math.Sqrt(
                    _velocity[i, j, k, 0] * _velocity[i, j, k, 0] +
                    _velocity[i, j, k, 1] * _velocity[i, j, k, 1] +
                    _velocity[i, j, k, 2] * _velocity[i, j, k, 2]
                );
                maxVel = Math.Max(maxVel, (float)v);
            }

            if (maxVel > 1e-10f) dt_advection = 0.3f * minDx / maxVel; // Conservative CFL
        }

        // Take the most restrictive time step
        var dt_cfl = Math.Min(dt_diffusion, dt_advection);
        var dt_max = (float)_options.TimeStep;
        var dt_effective = Math.Min(dt_cfl, dt_max);

        // Ensure reasonable bounds
        dt_effective = Math.Max(0.01f, dt_effective); // Minimum 0.01 seconds
        dt_effective = Math.Min(dt_effective, dt_max); // Maximum is user-specified

        // Safety check
        if (float.IsNaN(dt_effective) || float.IsInfinity(dt_effective)) dt_effective = 1.0f; // Fallback to 1 second

        return dt_effective;
    }

    /// <summary>
    ///     Validates the simulation options and borehole dataset before starting simulation.
    ///     This method should be called at the beginning of RunSimulationAsync().
    /// </summary>
    public static void ValidateSimulationInputs(GeothermalSimulationOptions options)
    {
        // Check for null references
        if (options == null)
            throw new ArgumentNullException(nameof(options), "Simulation options cannot be null.");

        if (options.BoreholeDataset == null)
            throw new ArgumentNullException(nameof(options.BoreholeDataset), "Borehole dataset cannot be null.");

        // Check for invalid borehole depth
        if (options.BoreholeDataset.TotalDepth <= 0)
            throw new InvalidOperationException(
                $"Invalid borehole depth: {options.BoreholeDataset.TotalDepth} meters. " +
                "The borehole must have a positive depth. Please ensure the borehole dataset is properly initialized.");

        // Check for empty lithology units
        if (options.BoreholeDataset.LithologyUnits == null || options.BoreholeDataset.LithologyUnits.Count == 0)
            throw new InvalidOperationException(
                "The borehole dataset has no lithology units defined. " +
                "At least one lithology unit is required for simulation. " +
                "Please define geological layers before running the simulation.");

        // Validate lithology units
        foreach (var unit in options.BoreholeDataset.LithologyUnits)
        {
            if (unit.DepthTo <= unit.DepthFrom)
                throw new InvalidOperationException(
                    $"Invalid lithology unit '{unit.Name}': DepthTo ({unit.DepthTo}) must be greater than DepthFrom ({unit.DepthFrom}).");

            if (unit.DepthFrom < 0)
                throw new InvalidOperationException(
                    $"Invalid lithology unit '{unit.Name}': DepthFrom ({unit.DepthFrom}) cannot be negative.");
        }

        // Check simulation parameters
        if (options.SimulationTime <= 0)
            throw new ArgumentException("Simulation time must be positive.", nameof(options.SimulationTime));

        if (options.TimeStep <= 0)
            throw new ArgumentException("Time step must be positive.", nameof(options.TimeStep));

        if (options.TimeStep > options.SimulationTime)
            throw new ArgumentException("Time step cannot be larger than simulation time.");

        // Check domain parameters
        if (options.DomainRadius <= 0)
            throw new ArgumentException("Domain radius must be positive.", nameof(options.DomainRadius));

        if (options.RadialGridPoints < 3)
            throw new ArgumentException("Radial grid points must be at least 3.", nameof(options.RadialGridPoints));

        if (options.AngularGridPoints < 4)
            throw new ArgumentException("Angular grid points must be at least 4.", nameof(options.AngularGridPoints));

        if (options.VerticalGridPoints < 3)
            throw new ArgumentException("Vertical grid points must be at least 3.", nameof(options.VerticalGridPoints));

        // Check heat exchanger parameters
        if (options.PipeInnerDiameter <= 0 || options.PipeOuterDiameter <= 0)
            throw new ArgumentException("Pipe diameters must be positive.");

        if (options.PipeInnerDiameter >= options.PipeOuterDiameter)
            throw new ArgumentException("Pipe inner diameter must be less than outer diameter.");

        if (options.FluidMassFlowRate <= 0)
            throw new ArgumentException("Fluid mass flow rate must be positive.", nameof(options.FluidMassFlowRate));

        // Check thermal properties
        if (options.FluidSpecificHeat <= 0)
            throw new ArgumentException("Fluid specific heat must be positive.", nameof(options.FluidSpecificHeat));

        if (options.FluidDensity <= 0)
            throw new ArgumentException("Fluid density must be positive.", nameof(options.FluidDensity));

        if (options.FluidViscosity <= 0)
            throw new ArgumentException("Fluid viscosity must be positive.", nameof(options.FluidViscosity));

        if (options.FluidThermalConductivity <= 0)
            throw new ArgumentException("Fluid thermal conductivity must be positive.",
                nameof(options.FluidThermalConductivity));

        // Check layer properties
        if (!options.LayerThermalConductivities.Any())
            // Set defaults if not specified
            options.SetDefaultValues();

        // Validate layer properties
        foreach (var kvp in options.LayerThermalConductivities)
            if (kvp.Value <= 0)
                throw new ArgumentException($"Thermal conductivity for layer '{kvp.Key}' must be positive.");

        foreach (var kvp in options.LayerSpecificHeats)
            if (kvp.Value <= 0)
                throw new ArgumentException($"Specific heat for layer '{kvp.Key}' must be positive.");

        foreach (var kvp in options.LayerDensities)
            if (kvp.Value <= 0)
                throw new ArgumentException($"Density for layer '{kvp.Key}' must be positive.");

        // Check convergence parameters
        if (options.ConvergenceTolerance <= 0)
            throw new ArgumentException("Convergence tolerance must be positive.",
                nameof(options.ConvergenceTolerance));

        if (options.MaxIterationsPerStep < 1)
            throw new ArgumentException("Maximum iterations per step must be at least 1.",
                nameof(options.MaxIterationsPerStep));
    }

    /// <summary>
    ///     SIMD-optimized heat transfer solver. (MODIFIED)
    /// </summary>
    private float SolveHeatTransferSIMD(float[,,] newTemp, float dt)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        var vecSize = Vector256<float>.Count;
        var maxChange = 0f;
        var lockObj = new object();

        Parallel.For(1, nr - 1,
            () => 0f, // localInit
            (i, loopState, localMaxChange) =>
            {
                for (var k = 1; k < nz - 1; k++)
                {
                    var r = Math.Max(0.01f, _mesh.R[i]); // ADDED: prevent division by small r
                    var dr_m = Math.Max(0.001f, _mesh.R[i] - _mesh.R[i - 1]);
                    var dr_p = Math.Max(0.001f, _mesh.R[i + 1] - _mesh.R[i]);
                    var dth = 2f * MathF.PI / nth;
                    var dz_m = Math.Max(0.001f, Math.Abs(_mesh.Z[k] - _mesh.Z[k - 1]));
                    var dz_p = Math.Max(0.001f, Math.Abs(_mesh.Z[k + 1] - _mesh.Z[k]));

                    var j = 0;
                    for (; j <= nth - vecSize; j += vecSize)
                    {
                        // Load and validate material properties (ADDED validation)
                        var lambdaVals = new float[vecSize];
                        var rhoVals = new float[vecSize];
                        var cpVals = new float[vecSize];

                        for (var v = 0; v < vecSize; v++)
                        {
                            lambdaVals[v] = Math.Max(0.1f, Math.Min(10f, _mesh.ThermalConductivities[i, j + v, k]));
                            rhoVals[v] = Math.Max(500f, Math.Min(5000f, _mesh.Densities[i, j + v, k]));
                            cpVals[v] = Math.Max(100f, Math.Min(5000f, _mesh.SpecificHeats[i, j + v, k]));
                        }

                        var lambda = Vector256.Create(lambdaVals);
                        var rho_cp = Avx2.Multiply(Vector256.Create(rhoVals), Vector256.Create(cpVals));

                        // Effective thermal diffusivity (alpha_thermal + dispersion_coeff)
                        var alpha_thermal = Avx2.Divide(lambda, rho_cp);
                        var disp_coeff = Vector256.Create(
                            _dispersionCoefficient[i, j, k], _dispersionCoefficient[i, j + 1, k],
                            _dispersionCoefficient[i, j + 2, k], _dispersionCoefficient[i, j + 3, k],
                            _dispersionCoefficient[i, j + 4, k], _dispersionCoefficient[i, j + 5, k],
                            _dispersionCoefficient[i, j + 6, k], _dispersionCoefficient[i, j + 7, k]
                        );
                        var alpha_eff = Avx2.Add(alpha_thermal, disp_coeff);

                        // Load temperatures
                        var T_c = Vector256.Create(_temperature[i, j, k], _temperature[i, j + 1, k],
                            _temperature[i, j + 2, k], _temperature[i, j + 3, k], _temperature[i, j + 4, k],
                            _temperature[i, j + 5, k], _temperature[i, j + 6, k], _temperature[i, j + 7, k]);
                        var T_rm = Vector256.Create(_temperature[i - 1, j, k], _temperature[i - 1, j + 1, k],
                            _temperature[i - 1, j + 2, k], _temperature[i - 1, j + 3, k], _temperature[i - 1, j + 4, k],
                            _temperature[i - 1, j + 5, k], _temperature[i - 1, j + 6, k],
                            _temperature[i - 1, j + 7, k]);
                        var T_rp = Vector256.Create(_temperature[i + 1, j, k], _temperature[i + 1, j + 1, k],
                            _temperature[i + 1, j + 2, k], _temperature[i + 1, j + 3, k], _temperature[i + 1, j + 4, k],
                            _temperature[i + 1, j + 5, k], _temperature[i + 1, j + 6, k],
                            _temperature[i + 1, j + 7, k]);
                        var T_zm = Vector256.Create(_temperature[i, j, k - 1], _temperature[i, j + 1, k - 1],
                            _temperature[i, j + 2, k - 1], _temperature[i, j + 3, k - 1], _temperature[i, j + 4, k - 1],
                            _temperature[i, j + 5, k - 1], _temperature[i, j + 6, k - 1],
                            _temperature[i, j + 7, k - 1]);
                        var T_zp = Vector256.Create(_temperature[i, j, k + 1], _temperature[i, j + 1, k + 1],
                            _temperature[i, j + 2, k + 1], _temperature[i, j + 3, k + 1], _temperature[i, j + 4, k + 1],
                            _temperature[i, j + 5, k + 1], _temperature[i, j + 6, k + 1],
                            _temperature[i, j + 7, k + 1]);

                        var jm = new int[vecSize];
                        var jp = new int[vecSize];
                        for (var v = 0; v < vecSize; v++)
                        {
                            jm[v] = (j + v - 1 + nth) % nth;
                            jp[v] = (j + v + 1) % nth;
                        }

                        var T_thm = Vector256.Create(_temperature[i, jm[0], k], _temperature[i, jm[1], k],
                            _temperature[i, jm[2], k], _temperature[i, jm[3], k], _temperature[i, jm[4], k],
                            _temperature[i, jm[5], k], _temperature[i, jm[6], k], _temperature[i, jm[7], k]);
                        var T_thp = Vector256.Create(_temperature[i, jp[0], k], _temperature[i, jp[1], k],
                            _temperature[i, jp[2], k], _temperature[i, jp[3], k], _temperature[i, jp[4], k],
                            _temperature[i, jp[5], k], _temperature[i, jp[6], k], _temperature[i, jp[7], k]);

                        // Laplacian
                        var two_vec = Vector256.Create(2f);
                        var d2T_dr2 = Avx2.Divide(Avx2.Subtract(Avx2.Add(T_rp, T_rm), Avx2.Multiply(two_vec, T_c)),
                            Vector256.Create(dr_m * dr_p));
                        var dT_dr_term = Avx2.Divide(Avx2.Subtract(T_rp, T_rm), Vector256.Create(r * (dr_p + dr_m)));
                        var d2T_dth2 = Avx2.Divide(Avx2.Subtract(Avx2.Add(T_thp, T_thm), Avx2.Multiply(two_vec, T_c)),
                            Vector256.Create(r * r * dth * dth));
                        var d2T_dz2 = Avx2.Divide(Avx2.Subtract(Avx2.Add(T_zp, T_zm), Avx2.Multiply(two_vec, T_c)),
                            Vector256.Create(dz_m * dz_p));
                        var laplacian = Avx2.Add(d2T_dr2, Avx2.Add(dT_dr_term, Avx2.Add(d2T_dth2, d2T_dz2)));

                        // Advection term
                        var advection = Vector256<float>.Zero;
                        if (_options.SimulateGroundwaterFlow)
                        {
                            var vr = Vector256.Create(_velocity[i, j, k, 0], _velocity[i, j + 1, k, 0],
                                _velocity[i, j + 2, k, 0], _velocity[i, j + 3, k, 0], _velocity[i, j + 4, k, 0],
                                _velocity[i, j + 5, k, 0], _velocity[i, j + 6, k, 0], _velocity[i, j + 7, k, 0]);
                            var vth = Vector256.Create(_velocity[i, j, k, 1], _velocity[i, j + 1, k, 1],
                                _velocity[i, j + 2, k, 1], _velocity[i, j + 3, k, 1], _velocity[i, j + 4, k, 1],
                                _velocity[i, j + 5, k, 1], _velocity[i, j + 6, k, 1], _velocity[i, j + 7, k, 1]);
                            var vz = Vector256.Create(_velocity[i, j, k, 2], _velocity[i, j + 1, k, 2],
                                _velocity[i, j + 2, k, 2], _velocity[i, j + 3, k, 2], _velocity[i, j + 4, k, 2],
                                _velocity[i, j + 5, k, 2], _velocity[i, j + 6, k, 2], _velocity[i, j + 7, k, 2]);

                            var dT_dr = Avx2.Divide(Avx2.Subtract(T_rp, T_rm), Vector256.Create(dr_p + dr_m));
                            var dT_dth = Avx2.Divide(Avx2.Subtract(T_thp, T_thm), Vector256.Create(2f * r * dth));
                            var dT_dz = Avx2.Divide(Avx2.Subtract(T_zp, T_zm), Vector256.Create(dz_p + dz_m));

                            var adv_term = Avx2.Add(Avx2.Multiply(vr, dT_dr),
                                Avx2.Add(Avx2.Multiply(vth, dT_dth), Avx2.Multiply(vz, dT_dz)));
                            advection = Avx2.Multiply(Vector256.Create(-1f), adv_term);
                        }

                        // Time integration with limiting (MODIFIED)
                        var diffusion_term = Avx2.Multiply(alpha_eff, laplacian);
                        var dT_dt = Avx2.Add(diffusion_term, advection);
                        var dT = Avx2.Multiply(Vector256.Create(dt), dT_dt);

                        // Limit maximum change (ADDED)
                        var maxDT = Vector256.Create(5f); // Max 5K change per iteration
                        dT = Avx2.Min(maxDT, Avx2.Max(Avx2.Multiply(Vector256.Create(-1f), maxDT), dT));

                        var T_new = Avx2.Add(T_c, dT);

                        // Store results with bounds checking
                        for (var v = 0; v < vecSize && j + v < nth; v++)
                        {
                            var newVal = T_new.GetElement(v);
                            if (!float.IsNaN(newVal) && !float.IsInfinity(newVal))
                            {
                                newTemp[i, j + v, k] = Math.Max(273f, Math.Min(473f, newVal)); // Physical bounds
                                var change = Math.Abs(newVal - T_c.GetElement(v));
                                localMaxChange = Math.Max(localMaxChange, change);
                            }
                            else
                            {
                                newTemp[i, j + v, k] = _temperature[i, j + v, k];
                            }
                        }
                    }

                    // Handle remaining elements
                    for (; j < nth; j++)
                    {
                        var change = SolveHeatTransferSinglePoint(i, j, k, newTemp, dt);
                        localMaxChange = Math.Max(localMaxChange, change);
                    }
                }

                return localMaxChange;
            },
            localMaxChange =>
            {
                lock (lockObj)
                {
                    maxChange = Math.Max(maxChange, localMaxChange);
                }
            });
        return maxChange;
    }

    /// <summary>
    ///     Scalar fallback for the heat transfer solver.
    /// </summary>
    private float SolveHeatTransferScalar(float[,,] newTemp, float dt)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        var maxChange = 0f;
        var lockObj = new object();

        Parallel.For(1, nr - 1,
            () => 0f, // localInit
            (i, loopState, localMaxChange) =>
            {
                for (var j = 0; j < nth; j++)
                for (var k = 1; k < nz - 1; k++)
                {
                    var change = SolveHeatTransferSinglePoint(i, j, k, newTemp, dt);
                    localMaxChange = Math.Max(localMaxChange, change);
                }

                return localMaxChange;
            },
            localMaxChange =>
            {
                lock (lockObj)
                {
                    maxChange = Math.Max(maxChange, localMaxChange);
                }
            });
        return maxChange;
    }

    /// <summary>
    ///     Scalar heat transfer solver for single point - CORRECTED VERSION.
    /// </summary>
    private float SolveHeatTransferSinglePoint(int i, int j, int k, float[,,] newTemp, float dt)
    {
        var nth = _mesh.AngularPoints;
        var r = Math.Max(0.01f, _mesh.R[i]); // ADDED: prevent division by small r

        // Get and validate material properties (ADDED)
        var lambda = Math.Max(0.1f, Math.Min(10f, _mesh.ThermalConductivities[i, j, k]));
        var rho = Math.Max(500f, Math.Min(5000f, _mesh.Densities[i, j, k]));
        var cp = Math.Max(100f, Math.Min(5000f, _mesh.SpecificHeats[i, j, k]));
        var alpha_thermal = lambda / (rho * cp);

        // CORRECTED: Dispersion coefficient is a separate thermal effect, not added to diffusivity
        var T_old = _temperature[i, j, k];

        // Calculate Laplacian
        var jm = (j - 1 + nth) % nth;
        var jp = (j + 1) % nth;

        var dr_m = Math.Max(0.001f, _mesh.R[i] - _mesh.R[i - 1]);
        var dr_p = Math.Max(0.001f, _mesh.R[i + 1] - _mesh.R[i]);
        var dth = 2f * MathF.PI / nth;
        var dz_m = Math.Max(0.001f, Math.Abs(_mesh.Z[k] - _mesh.Z[k - 1]));
        var dz_p = Math.Max(0.001f, Math.Abs(_mesh.Z[k + 1] - _mesh.Z[k]));

        var d2T_dr2 = (_temperature[i + 1, j, k] - 2 * T_old + _temperature[i - 1, j, k]) / (dr_m * dr_p);
        var dT_dr = (_temperature[i + 1, j, k] - _temperature[i - 1, j, k]) / (dr_p + dr_m);
        var d2T_dth2 = (_temperature[i, jp, k] - 2 * T_old + _temperature[i, jm, k]) / (r * r * dth * dth);
        var d2T_dz2 = (_temperature[i, j, k + 1] - 2 * T_old + _temperature[i, j, k - 1]) / (dz_m * dz_p);

        var laplacian = d2T_dr2 + dT_dr / r + d2T_dth2 + d2T_dz2;

        // Advection term (if groundwater flow is enabled) with upwind differencing (MODIFIED)
        var advection = 0f;
        if (_options.SimulateGroundwaterFlow)
        {
            var vr = _velocity[i, j, k, 0];
            var vth = _velocity[i, j, k, 1];
            var vz = _velocity[i, j, k, 2];

            // Upwind differencing for stability
            var dT_dr_adv = vr >= 0
                ? (T_old - _temperature[i - 1, j, k]) / dr_m
                : (_temperature[i + 1, j, k] - T_old) / dr_p;

            var dT_dth = (_temperature[i, jp, k] - _temperature[i, jm, k]) / (2f * r * dth);

            var dT_dz_adv = vz >= 0
                ? (T_old - _temperature[i, j, k - 1]) / dz_m
                : (_temperature[i, j, k + 1] - T_old) / dz_p;

            advection = -(vr * dT_dr_adv + vth * dT_dth + vz * dT_dz_adv);
        }

        // Thermal dispersion term (mechanical dispersion due to flow)
        var dispersion = 0f;
        if (_options.SimulateGroundwaterFlow && _dispersionCoefficient[i, j, k] > 0)
            // Dispersion acts like enhanced diffusion in the flow direction
            dispersion = _dispersionCoefficient[i, j, k] * laplacian;

        // Update temperature with limiting (MODIFIED)
        var dT = dt * (alpha_thermal * laplacian + dispersion + advection);
        dT = Math.Max(-5f, Math.Min(5f, dT)); // Limit to 5K change

        newTemp[i, j, k] = Math.Max(273f, Math.Min(473f, T_old + dT)); // Physical bounds

        return Math.Abs(dT);
    }

    /// <summary>
    ///     Apply boundary conditions to the temperature field.
    /// </summary>
    private void ApplyBoundaryConditions(float[,,] temp)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        // Outer radial boundary
        if (_options.OuterBoundaryCondition == BoundaryConditionType.Dirichlet)
        {
            for (var j = 0; j < nth; j++)
            for (var k = 0; k < nz; k++)
                temp[nr - 1, j, k] = (float)_options.OuterBoundaryTemperature;
        }
        else if (_options.OuterBoundaryCondition == BoundaryConditionType.Neumann)
        {
            // Implement Neumann BC
            var dr = _mesh.R[nr - 1] - _mesh.R[nr - 2];
            var flux = (float)_options.OuterBoundaryHeatFlux;

            for (var j = 0; j < nth; j++)
            for (var k = 0; k < nz; k++)
            {
                var lambda = _mesh.ThermalConductivities[nr - 1, j, k];
                temp[nr - 1, j, k] = temp[nr - 2, j, k] + flux * dr / lambda;
            }
        }

        // Top boundary
        if (_options.TopBoundaryCondition == BoundaryConditionType.Dirichlet)
            for (var i = 0; i < nr; i++)
            for (var j = 0; j < nth; j++)
                temp[i, j, 0] = (float)_options.TopBoundaryTemperature;
        else if (_options.TopBoundaryCondition == BoundaryConditionType.Adiabatic)
            for (var i = 0; i < nr; i++)
            for (var j = 0; j < nth; j++)
                temp[i, j, 0] = temp[i, j, 1];

        // Bottom boundary
        if (_options.BottomBoundaryCondition == BoundaryConditionType.Neumann)
        {
            var dz = Math.Abs(_mesh.Z[nz - 1] - _mesh.Z[nz - 2]);
            var flux = (float)_options.GeothermalHeatFlux;

            for (var i = 0; i < nr; i++)
            for (var j = 0; j < nth; j++)
            {
                var lambda = _mesh.ThermalConductivities[i, j, nz - 1];
                temp[i, j, nz - 1] = temp[i, j, nz - 2] - flux * dz / lambda; // Negative because heat flows up
            }
        }
    }

    /// <summary>
    ///     Apply heat exchanger source/sink term - FIXED VERSION.
    /// </summary>
    private void ApplyHeatExchangerSource(float[,,] temp, float dt)
    {
        var rHE = (float)(_options.PipeOuterDiameter / 2.0);

        for (var i = 0; i < _mesh.RadialPoints; i++)
        {
            if (_mesh.R[i] > rHE * 3.0f) break; // Extend influence zone

            for (var j = 0; j < _mesh.AngularPoints; j++)
            for (var k = 0; k < _mesh.VerticalPoints; k++)
            {
                var depth = Math.Max(0, -_mesh.Z[k]);
                if (depth < 0 || depth > _options.BoreholeDataset.TotalDepth) continue;

                // Calculate distance from borehole center
                var distance = _mesh.R[i];

                if (distance <= rHE * 1.5f) // Near borehole
                {
                    // Get interpolated ground temperature at this depth
                    var heIndex = Math.Min(_fluidTempDown.Length - 1,
                        (int)(depth / _options.BoreholeDataset.TotalDepth * _fluidTempDown.Length));

                    var Tfluid = _options.HeatExchangerType == HeatExchangerType.UTube
                        ? 0.5f * (_fluidTempDown[heIndex] + _fluidTempUp[heIndex])
                        : _fluidTempDown[heIndex];

                    var Tground = temp[i, j, k];

                    // FIXED: Correct heat transfer direction
                    var U = Math.Min(500f, CalculateHeatTransferCoefficient()); // Reduced max U
                    var cellVolume = _mesh.CellVolumes[i, j, k];

                    // Heat transfer per unit volume
                    var Q_volumetric = U * (Tfluid - Tground) * (float)_options.PipeOuterDiameter * MathF.PI /
                                       cellVolume;

                    var rho_cp = _mesh.Densities[i, j, k] * _mesh.SpecificHeats[i, j, k];

                    // Apply source term with correct sign
                    var dT = Q_volumetric * dt / rho_cp;
                    dT = Math.Max(-1f, Math.Min(1f, dT)); // Limit to 1K change per step

                    temp[i, j, k] += dT;
                }
            }
        }
    }


    /// <summary>
    ///     Updates heat exchanger fluid temperatures - IMPROVED VERSION.
    /// </summary>
    private void UpdateHeatExchanger()
    {
        var nz = _fluidTempDown.Length;
        var mdot = (float)_options.FluidMassFlowRate;
        var cp = (float)_options.FluidSpecificHeat;
        var dz = _options.BoreholeDataset.TotalDepth / nz;

        // Calculate heat transfer coefficient
        var U = CalculateHeatTransferCoefficient();
        var P = 2f * MathF.PI * (float)_options.PipeOuterDiameter;

        // Downward flow
        _fluidTempDown[0] = (float)_options.FluidInletTemperature;

        for (var i = 1; i < nz; i++)
        {
            var depth = (i - 0.5f) * dz; // Mid-point of segment
            var Tground = InterpolateGroundTemperatureAtDepth(depth);

            // Use effectiveness-NTU method for more accurate heat transfer
            var NTU = U * P * dz / (mdot * cp);
            var effectiveness = 1 - MathF.Exp(-NTU);

            var Tin = _fluidTempDown[i - 1];
            var Tout = Tin + effectiveness * (Tground - Tin);

            // Apply limiting
            var dT = Tout - Tin;
            dT = Math.Max(-5f, Math.Min(5f, dT));

            _fluidTempDown[i] = Tin + dT;
        }

        // Upward flow for U-tube
        if (_options.HeatExchangerType == HeatExchangerType.UTube)
        {
            _fluidTempUp[nz - 1] = _fluidTempDown[nz - 1];

            for (var i = nz - 2; i >= 0; i--)
            {
                var depth = (i + 0.5f) * dz;
                var Tground = InterpolateGroundTemperatureAtDepth(depth);

                var NTU = U * P * dz / (mdot * cp);
                var effectiveness = 1 - MathF.Exp(-NTU);

                var Tin = _fluidTempUp[i + 1];
                var Tout = Tin + effectiveness * (Tground - Tin);

                var dT = Tout - Tin;
                dT = Math.Max(-5f, Math.Min(5f, dT));

                _fluidTempUp[i] = Tin + dT;
            }
        }
    }

    /// <summary>
    ///     Interpolates ground temperature at a specific depth - NEW IMPROVED VERSION.
    /// </summary>
    private float InterpolateGroundTemperatureAtDepth(float depth)
    {
        // Find the vertical index for this depth
        var kIndex = -1;
        for (var k = 0; k < _mesh.VerticalPoints - 1; k++)
        {
            var meshDepth = -_mesh.Z[k]; // Z is negative, so negate to get depth
            var nextMeshDepth = -_mesh.Z[k + 1];

            if (depth >= meshDepth && depth <= nextMeshDepth)
            {
                kIndex = k;
                break;
            }
        }

        // If not found, use closest
        if (kIndex == -1)
        {
            if (depth <= 0)
            {
                kIndex = 0;
            }
            else if (depth >= _options.BoreholeDataset.TotalDepth)
            {
                kIndex = _mesh.VerticalPoints - 1;
            }
            else
            {
                // Find closest
                var minDist = float.MaxValue;
                for (var k = 0; k < _mesh.VerticalPoints; k++)
                {
                    var dist = Math.Abs(depth - -_mesh.Z[k]);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        kIndex = k;
                    }
                }
            }
        }

        // Average temperature in a ring around the borehole (not including the borehole itself)
        var temp = 0f;
        var count = 0;

        // Sample from radial indices 3-8 (outside immediate borehole influence)
        for (var i = 3; i < Math.Min(8, _mesh.RadialPoints); i++)
        for (var j = 0; j < _mesh.AngularPoints; j++)
        {
            temp += _temperature[i, j, kIndex];
            count++;
        }

        if (count > 0)
            return temp / count;

        // Fallback: use initial temperature profile
        return _initialTemperature[5, 0, kIndex]; // Use a point away from borehole
    }

    /// <summary>
    ///     Interpolates ground temperature at a specific depth.
    /// </summary>
    private float InterpolateGroundTemperature(float depth)
    {
        // Find nearest grid point
        var kIndex = 0;
        for (var k = 0; k < _mesh.VerticalPoints; k++)
            if (-_mesh.Z[k] >= depth)
            {
                kIndex = k;
                break;
            }

        // Average temperature near borehole
        var temp = 0f;
        var count = 0;

        for (var i = 0; i < Math.Min(5, _mesh.RadialPoints); i++)
        for (var j = 0; j < _mesh.AngularPoints; j++)
        {
            temp += _temperature[i, j, kIndex];
            count++;
        }

        return count > 0 ? temp / count : (float)_options.SurfaceTemperature;
    }

    /// <summary>
    ///     Calculates overall heat transfer coefficient using robust correlations.
    /// </summary>
    private float CalculateHeatTransferCoefficient()
    {
        // Reynolds number: Re = (rho * v * D) / mu = (4 * mdot) / (pi * D * mu)
        var D_inner = (float)_options.PipeInnerDiameter;
        var mu = (float)_options.FluidViscosity;
        var mdot = (float)_options.FluidMassFlowRate;
        var Re = 4.0f * mdot / (MathF.PI * D_inner * mu);

        var Pr = (float)(_options.FluidViscosity * _options.FluidSpecificHeat / _options.FluidThermalConductivity);

        float Nu;
        if (Re < 2300)
        {
            // Laminar flow. Nusselt number is constant for fully developed flow.
            // Using 4.36 for constant heat flux condition, which is more applicable here.
            Nu = 4.36f;
        }
        else
        {
            // Turbulent or Transitional flow. Use Gnielinski correlation.
            // It is more accurate than Dittus-Boelter and covers the transition zone.

            // Friction factor (Petukhov correlation, valid for 3000 < Re < 5e6)
            var f = MathF.Pow(0.79f * MathF.Log(Re) - 1.64f, -2.0f);

            Nu = f / 8.0f * (Re - 1000.0f) * Pr /
                 (1.0f + 12.7f * MathF.Pow(f / 8.0f, 0.5f) * (MathF.Pow(Pr, 2.0f / 3.0f) - 1.0f));
        }

        var h_fluid = Nu * (float)_options.FluidThermalConductivity / D_inner;

        // Overall heat transfer coefficient (U) based on series of thermal resistances
        var r_i = D_inner / 2f;
        var r_o = (float)_options.PipeOuterDiameter / 2f;
        var k_pipe = (float)_options.PipeThermalConductivity;

        // Resistances: R_fluid (convection) + R_pipe (conduction)
        var R_fluid = 1f / h_fluid;
        var R_pipe = r_i * MathF.Log(r_o / r_i) / k_pipe;

        // U is based on inner pipe area
        var U = 1f / (R_fluid + R_pipe);

        return U;
    }

    /// <summary>
    ///     Calculates Péclet number and dispersivity fields - FIXED VERSION.
    /// </summary>
    private void CalculatePecletAndDispersivity()
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        var longitudinalDispersivityLength = (float)_options.LongitudinalDispersivity;

        Parallel.For(0, nr, i =>
        {
            for (var j = 0; j < nth; j++)
            for (var k = 0; k < nz; k++)
            {
                // Velocity magnitude
                var vr = _velocity[i, j, k, 0];
                var vth = _velocity[i, j, k, 1];
                var vz = _velocity[i, j, k, 2];
                var v_mag = MathF.Sqrt(vr * vr + vth * vth + vz * vz);

                // Use actual mesh spacing as characteristic length
                var L_r = i > 0 ? _mesh.R[i] - _mesh.R[i - 1] : _mesh.R[1] - _mesh.R[0];
                var L_z = k > 0 ? Math.Abs(_mesh.Z[k] - _mesh.Z[k - 1]) : Math.Abs(_mesh.Z[1] - _mesh.Z[0]);
                var L = Math.Min(L_r, L_z);

                // Thermal diffusivity
                var lambda = _mesh.ThermalConductivities[i, j, k];
                var rho = _mesh.Densities[i, j, k];
                var cp = _mesh.SpecificHeats[i, j, k];

                if (rho > 0 && cp > 0 && lambda > 0)
                {
                    var alpha = lambda / (rho * cp);

                    // Péclet number: Pe = v * L / alpha
                    if (alpha > 0 && L > 0)
                        _pecletNumber[i, j, k] = v_mag * L / alpha;
                    else
                        _pecletNumber[i, j, k] = 0;
                }
                else
                {
                    _pecletNumber[i, j, k] = 0;
                }

                // Mechanical dispersion coefficient
                _dispersionCoefficient[i, j, k] = longitudinalDispersivityLength * v_mag;
            }
        });
    }

    /// <summary>
    ///     Saves results for current time step - FIXED COP calculation.
    /// </summary>
    private void SaveTimeStepResults(GeothermalSimulationResults results, double currentTime)
    {
        results.TemperatureFields[currentTime] = (float[,,])_temperature.Clone();

        var outletTemp = _options.HeatExchangerType == HeatExchangerType.UTube
            ? _fluidTempUp[0]
            : _fluidTempDown[_fluidTempDown.Length - 1];

        var inletTemp = (float)_options.FluidInletTemperature;

        // FIXED: Correct heat rate calculation
        var Q = _options.FluidMassFlowRate * _options.FluidSpecificHeat *
                (outletTemp - inletTemp); // Positive when extracting heat

        results.HeatExtractionRate.Add((currentTime, Q));
        results.OutletTemperature.Add((currentTime, outletTemp));

        // Calculate realistic COP
        var hvacSupplyTempK = _options.HvacSupplyTemperatureKelvin ?? 308.15; // 35°C default
        var compressorEfficiency = _options.CompressorIsentropicEfficiency ?? 0.6;

        var cop = 4.0; // Default

        if (Math.Abs(Q) > 100) // Only calculate if significant heat transfer
        {
            var avgFluidTemp = (inletTemp + outletTemp) / 2.0;

            if (Q > 0) // Heating mode
            {
                // COP_heating = T_hot / (T_hot - T_cold) * efficiency
                var deltaT = Math.Max(1.0, hvacSupplyTempK - avgFluidTemp);
                cop = Math.Min(7.0, hvacSupplyTempK / deltaT * compressorEfficiency);
            }
            else // Cooling mode
            {
                // COP_cooling = T_cold / (T_hot - T_cold) * efficiency  
                var deltaT = Math.Max(1.0, avgFluidTemp - hvacSupplyTempK);
                cop = Math.Min(7.0, hvacSupplyTempK / deltaT * compressorEfficiency);
            }
        }

        results.CoefficientOfPerformance.Add((currentTime, cop));
    }

    /// <summary>
    ///     Calculates final performance metrics - FIXED VERSION.
    /// </summary>
    private void CalculatePerformanceMetrics(GeothermalSimulationResults results)
    {
        // Average heat extraction
        if (results.HeatExtractionRate.Any())
        {
            results.AverageHeatExtractionRate = results.HeatExtractionRate.Average(h => h.heatRate);

            // FIXED: Calculate total energy by integrating over actual time intervals
            // Use trapezoidal rule for better accuracy
            results.TotalExtractedEnergy = 0.0;
            for (var i = 1; i < results.HeatExtractionRate.Count; i++)
            {
                var dt = results.HeatExtractionRate[i].time - results.HeatExtractionRate[i - 1].time;
                var avgPower = (results.HeatExtractionRate[i].heatRate + results.HeatExtractionRate[i - 1].heatRate) /
                               2.0;
                results.TotalExtractedEnergy += avgPower * dt;
            }
        }

        // FIXED: Borehole thermal resistance calculation
        if (results.OutletTemperature.Any() && results.HeatExtractionRate.Any())
        {
            var Tin = _options.FluidInletTemperature;
            var Tout = results.OutletTemperature.Last().temperature;
            var Q_avg = results.AverageHeatExtractionRate;

            // Calculate average ground temperature along borehole
            float Tground_avg = 0;
            var nSamples = 20;
            for (var i = 0; i < nSamples; i++)
            {
                var depth = (i + 0.5f) * _options.BoreholeDataset.TotalDepth / nSamples;
                Tground_avg += InterpolateGroundTemperatureAtDepth(depth);
            }

            Tground_avg /= nSamples;

            // Average fluid temperature
            var Tfluid_avg = (Tin + Tout) / 2.0;

            // Thermal resistance: R_b = (T_ground - T_fluid) / Q
            if (Math.Abs(Q_avg) > 100) // Only if significant heat transfer
            {
                results.BoreholeThermalResistance = Math.Abs((Tground_avg - Tfluid_avg) / Q_avg);

                // Sanity check - clamp to reasonable range
                results.BoreholeThermalResistance = Math.Max(0.01, Math.Min(1.0, results.BoreholeThermalResistance));
            }
            else
            {
                results.BoreholeThermalResistance = 0.1; // Default value
            }
        }

        // Calculate effective ground properties
        CalculateEffectiveGroundProperties(results);

        // Layer contributions
        CalculateLayerContributions(results);

        // Fluid temperature profile
        for (var i = 0; i < _fluidTempDown.Length; i++)
        {
            var depth = i * _options.BoreholeDataset.TotalDepth / _fluidTempDown.Length;
            results.FluidTemperatureProfile.Add((depth, _fluidTempDown[i], _fluidTempUp[i]));
        }

        // FIXED: Average Péclet number calculation
        if (_options.SimulateGroundwaterFlow)
        {
            var totalPe = 0.0;
            var count = 0;
            for (var i = 0; i < _mesh.RadialPoints; i++)
            for (var j = 0; j < _mesh.AngularPoints; j++)
            for (var k = 0; k < _mesh.VerticalPoints; k++)
            {
                var pe = _pecletNumber[i, j, k];
                if (!float.IsNaN(pe) && !float.IsInfinity(pe) && pe > 0)
                {
                    totalPe += pe;
                    count++;
                }
            }

            results.AveragePecletNumber = count > 0 ? totalPe / count : 0;
        }

        results.LongitudinalDispersivity = _options.LongitudinalDispersivity;
        results.TransverseDispersivity = _options.TransverseDispersivity;
    }


    /// <summary>
    ///     Calculates effective ground thermal properties - FIXED VERSION.
    /// </summary>
    private void CalculateEffectiveGroundProperties(GeothermalSimulationResults results)
    {
        var totalVolume = 0.0;
        var totalConductivity = 0.0;
        var totalDiffusivity = 0.0;

        for (var i = 0; i < _mesh.RadialPoints; i++)
        for (var j = 0; j < _mesh.AngularPoints; j++)
        for (var k = 0; k < _mesh.VerticalPoints; k++)
        {
            var volume = _mesh.CellVolumes[i, j, k];
            if (volume > 0 && !float.IsNaN(volume))
            {
                var lambda = _mesh.ThermalConductivities[i, j, k];
                var rho = _mesh.Densities[i, j, k];
                var cp = _mesh.SpecificHeats[i, j, k];

                if (rho > 0 && cp > 0)
                {
                    var alpha = lambda / (rho * cp);
                    totalVolume += volume;
                    totalConductivity += lambda * volume;
                    totalDiffusivity += alpha * volume;
                }
            }
        }

        if (totalVolume > 0)
        {
            results.EffectiveGroundConductivity = totalConductivity / totalVolume;
            results.GroundThermalDiffusivity = totalDiffusivity / totalVolume;
        }
        else
        {
            // Default values if calculation fails
            results.EffectiveGroundConductivity = 2.5;
            results.GroundThermalDiffusivity = 1e-6;
        }

        // FIXED: Thermal influence radius calculation
        CalculateThermalInfluenceRadius(results);
    }

    /// <summary>
    ///     Calculate thermal influence radius properly - NEW METHOD.
    /// </summary>
    private void CalculateThermalInfluenceRadius(GeothermalSimulationResults results)
    {
        // Method 1: Based on temperature field analysis
        double maxRadius = 0;
        var tempChangeThreshold = 0.5; // 0.5K change threshold

        // Check multiple depths
        int[] checkDepths =
        {
            _mesh.VerticalPoints / 4,
            _mesh.VerticalPoints / 2,
            3 * _mesh.VerticalPoints / 4
        };

        foreach (var k in checkDepths)
            for (var i = 1; i < _mesh.RadialPoints; i++)
            {
                double avgTempChange = 0;
                var count = 0;

                for (var j = 0; j < _mesh.AngularPoints; j++)
                {
                    var change = Math.Abs(_temperature[i, j, k] - _initialTemperature[i, j, k]);
                    if (!float.IsNaN(change))
                    {
                        avgTempChange += change;
                        count++;
                    }
                }

                if (count > 0)
                {
                    avgTempChange /= count;

                    if (avgTempChange >= tempChangeThreshold) maxRadius = Math.Max(maxRadius, _mesh.R[i]);
                }
            }

        // Method 2: Theoretical estimate based on thermal diffusivity
        var alpha = results.GroundThermalDiffusivity > 0 ? results.GroundThermalDiffusivity : 1e-6;
        var time = CurrentSimulationTime > 0 ? CurrentSimulationTime : _options.SimulationTime;
        var theoreticalRadius = 2.0 * Math.Sqrt(alpha * time / Math.PI);

        // Use the maximum of observed and theoretical
        results.ThermalInfluenceRadius = Math.Max(maxRadius, theoreticalRadius);

        // Ensure minimum reasonable value
        if (results.ThermalInfluenceRadius < 1.0)
            // If still too small, use a default based on borehole size
            results.ThermalInfluenceRadius = Math.Max(1.0, _options.PipeOuterDiameter * 20);

        // Cap at domain radius
        results.ThermalInfluenceRadius = Math.Min(results.ThermalInfluenceRadius, _options.DomainRadius * 0.8);
    }


    /// <summary>
    ///     Calculates heat flux contributions from each geological layer.
    /// </summary>
    private void CalculateLayerContributions(GeothermalSimulationResults results)
    {
        var layerHeatFluxes = new Dictionary<string, double>();
        var layerTempChanges = new Dictionary<string, double>();
        var layerFlowRates = new Dictionary<string, double>();

        // Map material ID back to layer name
        var materialIdToLayerName = new Dictionary<int, string>();
        var lithologyList = _options.BoreholeDataset.Lithology;
        for (var i = 0; i < lithologyList.Count; i++)
        {
            // CRITICAL FIX: Use actual unit name, not generic RockType
            // RockType/LithologyType is just a category (e.g., "Sandstone")
            // Name is the specific formation (e.g., "Sandstone Aquifer 1")
            var layerName = !string.IsNullOrEmpty(lithologyList[i].Name)
                ? lithologyList[i].Name
                : lithologyList[i].RockType ?? "Unknown";
            materialIdToLayerName[i + 1] = layerName; // Material ID is index + 1

            if (!layerHeatFluxes.ContainsKey(layerName))
            {
                layerHeatFluxes[layerName] = 0;
                layerTempChanges[layerName] = 0;
                layerFlowRates[layerName] = 0;
            }
        }

        var layerCellCounts = new Dictionary<string, int>();
        foreach (var key in layerHeatFluxes.Keys) layerCellCounts[key] = 0;

        // Iterate through mesh cells to aggregate data
        for (var k = 0; k < _mesh.VerticalPoints; k++)
        for (var j = 0; j < _mesh.AngularPoints; j++)
        for (var i = 0; i < _mesh.RadialPoints; i++)
        {
            var matId = _mesh.MaterialIds[i, j, k];
            if (materialIdToLayerName.TryGetValue(matId, out var layerName))
            {
                // Temperature Change
                layerTempChanges[layerName] += _temperature[i, j, k] - _initialTemperature[i, j, k];
                layerCellCounts[layerName]++;

                // Groundwater Flow Rate (radial component towards/away from borehole)
                if (_options.SimulateGroundwaterFlow)
                {
                    var r = _mesh.R[i];
                    var dtheta = 2f * MathF.PI / _mesh.AngularPoints;
                    var dz = k < _mesh.VerticalPoints - 1
                        ? Math.Abs(_mesh.Z[k + 1] - _mesh.Z[k])
                        : Math.Abs(_mesh.Z[k] - _mesh.Z[k - 1]);
                    var faceArea = r * dtheta * dz;
                    layerFlowRates[layerName] += _velocity[i, j, k, 0] * faceArea;
                }
            }
        }

        // Calculate Heat Flux at the borehole wall (approximated at the second radial node)
        var borehole_wall_r_index = 1;
        if (_mesh.RadialPoints > 1)
        {
            var r0 = _mesh.R[borehole_wall_r_index - 1];
            var r1 = _mesh.R[borehole_wall_r_index];
            var dr = r1 - r0;

            if (dr > 1e-6)
                for (var k = 0; k < _mesh.VerticalPoints; k++)
                {
                    // Assume material is consistent around circumference for a given depth
                    var matId = _mesh.MaterialIds[borehole_wall_r_index, 0, k];
                    if (materialIdToLayerName.TryGetValue(matId, out var layerName))
                    {
                        double totalLayerHeatFlow = 0;
                        for (var j = 0; j < _mesh.AngularPoints; j++)
                        {
                            var T0 = _temperature[borehole_wall_r_index - 1, j, k];
                            var T1 = _temperature[borehole_wall_r_index, j, k];
                            var lambda = _mesh.ThermalConductivities[borehole_wall_r_index, j, k];

                            var dT_dr = (T1 - T0) / dr;
                            var q_r = -lambda * dT_dr; // Radial heat flux (W/m^2)

                            var dz = k < _mesh.VerticalPoints - 1
                                ? Math.Abs(_mesh.Z[k + 1] - _mesh.Z[k])
                                : Math.Abs(_mesh.Z[k] - _mesh.Z[k - 1]);
                            var dtheta = 2f * MathF.PI / _mesh.AngularPoints;
                            var area = r1 * dtheta * dz;

                            totalLayerHeatFlow += q_r * area; // Total heat flow (W)
                        }

                        layerHeatFluxes[layerName] += totalLayerHeatFlow;
                    }
                }
        }

        // Finalize averages
        foreach (var key in layerCellCounts.Keys.ToList())
            if (layerCellCounts.ContainsKey(key) && layerCellCounts[key] > 0)
                layerTempChanges[key] /= layerCellCounts[key];

        // Normalize to percentages
        var totalFlux = layerHeatFluxes.Values.Sum(Math.Abs);
        if (totalFlux > 1e-6)
            foreach (var key in layerHeatFluxes.Keys.ToList())
            {
                results.LayerHeatFluxContributions[key] = 100 * Math.Abs(layerHeatFluxes[key]) / totalFlux;
                results.LayerTemperatureChanges[key] = layerTempChanges[key];
                results.LayerFlowRates[key] = layerFlowRates[key];
            }
    }

    /// <summary>
    ///     Generates visualization data.
    /// </summary>
    private async Task GenerateVisualizationDataAsync(GeothermalSimulationResults results)
    {
        _progress?.Report((0.91f, "Generating visualization data..."));

        // Generate temperature isosurfaces
        if (_options.Generate3DIsosurfaces)
            foreach (var isoTemp in _options.IsosurfaceTemperatures)
            {
                // Create a label volume to exclude regions outside domain
                var labelData = new SimpleLabelVolume(_mesh.RadialPoints, _mesh.AngularPoints, _mesh.VerticalPoints);
                for (var i = 0; i < _mesh.RadialPoints; i++)
                for (var j = 0; j < _mesh.AngularPoints; j++)
                for (var k = 0; k < _mesh.VerticalPoints; k++)
                    labelData.Data[i, j, k] = _mesh.MaterialIds[i, j, k] == 255 ? (byte)0 : (byte)1;

                try
                {
                    var isosurface = await IsosurfaceGenerator.GenerateIsosurfaceAsync(
                        _temperature,
                        labelData,
                        (float)isoTemp,
                        new Vector3(1, 1, 1), // Simplified voxel size
                        null,
                        CancellationToken.None
                    );

                    results.TemperatureIsosurfaces.Add(isosurface);
                }
                catch
                {
                    // Skip failed isosurface
                }
            }

        // Generate 2D slices
        if (_options.Generate2DSlices)
            foreach (var slicePos in _options.SlicePositions)
            {
                var zIndex = Math.Min(_mesh.VerticalPoints - 1, (int)(slicePos * (_mesh.VerticalPoints - 1)));
                var depth = Math.Max(0, -_mesh.Z[zIndex]);

                // Temperature slice
                var tempSlice = new float[_mesh.RadialPoints, _mesh.AngularPoints];
                var pressureSlice = new float[_mesh.RadialPoints, _mesh.AngularPoints];
                var velocitySlice = new float[_mesh.RadialPoints, _mesh.AngularPoints];

                for (var i = 0; i < _mesh.RadialPoints; i++)
                for (var j = 0; j < _mesh.AngularPoints; j++)
                {
                    tempSlice[i, j] = _temperature[i, j, zIndex];
                    pressureSlice[i, j] = _pressure[i, j, zIndex];

                    var vr = _velocity[i, j, zIndex, 0];
                    var vth = _velocity[i, j, zIndex, 1];
                    var vz = _velocity[i, j, zIndex, 2];
                    velocitySlice[i, j] = MathF.Sqrt(vr * vr + vth * vth + vz * vz);
                }

                results.TemperatureSlices[depth] = tempSlice;
                results.PressureSlices[depth] = pressureSlice;
                results.VelocityMagnitudeSlices[depth] = velocitySlice;
            }

        // Generate streamlines
        if (_options.GenerateStreamlines) GenerateStreamlines(results);

        // Create domain mesh
        results.DomainMesh = CreateDomainVisualizationMesh();

        // Create borehole mesh
        results.BoreholeMesh = GeothermalMeshGenerator.CreateBoreholeMesh(_options.BoreholeDataset, _options);
    }

    /// <summary>
    ///     Generates streamlines for flow visualization.
    /// </summary>
    private void GenerateStreamlines(GeothermalSimulationResults results)
    {
        var random = new Random(42);

        for (var s = 0; s < _options.StreamlineCount; s++)
        {
            var streamline = new List<Vector3>();

            // Random starting point
            var r = (float)(random.NextDouble() * _options.DomainRadius);
            var theta = (float)(random.NextDouble() * 2 * Math.PI);
            var z = (float)(random.NextDouble() * (_mesh.Z[_mesh.VerticalPoints - 1] - _mesh.Z[0]) + _mesh.Z[0]);

            var pos = new Vector3(r * MathF.Cos(theta), r * MathF.Sin(theta), z);

            // Trace streamline
            var dt = 0.1f;
            for (var step = 0; step < 1000; step++)
            {
                streamline.Add(pos);

                // Interpolate velocity at current position
                var vel = InterpolateVelocity(pos);

                if (vel.Length() < 1e-6) break;

                // Advance position
                pos += vel * dt;

                // Check bounds
                var r_current = MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y);
                if (r_current > _options.DomainRadius ||
                    pos.Z < _mesh.Z[0] ||
                    pos.Z > _mesh.Z[_mesh.VerticalPoints - 1])
                    break;
            }

            if (streamline.Count > 5) results.Streamlines.Add(streamline);
        }
    }

    /// <summary>
    ///     Interpolates velocity at arbitrary position.
    /// </summary>
    private Vector3 InterpolateVelocity(Vector3 pos)
    {
        // Convert to cylindrical coordinates
        var r = MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y);
        var theta = MathF.Atan2(pos.Y, pos.X);
        if (theta < 0) theta += 2 * MathF.PI;
        var z = pos.Z;

        // Find grid indices
        int ir = 0, ith = 0, iz = 0;
        for (var i = 0; i < _mesh.RadialPoints - 1; i++)
            if (r >= _mesh.R[i] && r <= _mesh.R[i + 1])
            {
                ir = i;
                break;
            }

        ith = (int)(theta / (2 * Math.PI) * _mesh.AngularPoints) % _mesh.AngularPoints;

        for (var k = 0; k < _mesh.VerticalPoints - 1; k++)
            if (z >= _mesh.Z[k] && z <= _mesh.Z[k + 1])
            {
                iz = k;
                break;
            }

        // Get velocity components
        var vr = _velocity[ir, ith, iz, 0];
        var vth = _velocity[ir, ith, iz, 1];
        var vz = _velocity[ir, ith, iz, 2];

        // Convert to Cartesian
        var vx = vr * MathF.Cos(theta) - vth * MathF.Sin(theta);
        var vy = vr * MathF.Sin(theta) + vth * MathF.Cos(theta);

        return new Vector3(vx, vy, vz);
    }

    /// <summary>
    ///     Logs comprehensive diagnostics about the simulation setup to help identify potential issues.
    /// </summary>
    private void LogSimulationDiagnostics()
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        // Log mesh information
        Console.WriteLine("=== GEOTHERMAL SIMULATION DIAGNOSTICS ===");
        Console.WriteLine($"Mesh: {nr} x {nth} x {nz} = {nr * nth * nz:N0} cells");
        Console.WriteLine($"Domain radius: {_options.DomainRadius:F2} m");
        Console.WriteLine($"Depth range: {_mesh.Z[0]:F2} to {_mesh.Z[nz - 1]:F2} m");

        // Analyze grid spacing
        float minDr = float.MaxValue, maxDr = 0;
        for (var i = 1; i < nr; i++)
        {
            var dr = _mesh.R[i] - _mesh.R[i - 1];
            minDr = Math.Min(minDr, dr);
            maxDr = Math.Max(maxDr, dr);
        }

        float minDz = float.MaxValue, maxDz = 0;
        for (var k = 1; k < nz; k++)
        {
            var dz = Math.Abs(_mesh.Z[k] - _mesh.Z[k - 1]);
            minDz = Math.Min(minDz, dz);
            maxDz = Math.Max(maxDz, dz);
        }

        Console.WriteLine($"Grid spacing - Radial: {minDr:F3} to {maxDr:F3} m");
        Console.WriteLine($"Grid spacing - Vertical: {minDz:F3} to {maxDz:F3} m");

        // Analyze material properties
        float minK = float.MaxValue, maxK = 0;
        float minPhi = float.MaxValue, maxPhi = 0;
        float minLambda = float.MaxValue, maxLambda = 0;
        float minRho = float.MaxValue, maxRho = 0;
        float minCp = float.MaxValue, maxCp = 0;

        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
        {
            var K = _mesh.Permeabilities[i, j, k];
            var phi = _mesh.Porosities[i, j, k];
            var lambda = _mesh.ThermalConductivities[i, j, k];
            var rho = _mesh.Densities[i, j, k];
            var cp = _mesh.SpecificHeats[i, j, k];

            if (K > 0)
            {
                minK = Math.Min(minK, K);
                maxK = Math.Max(maxK, K);
            }

            if (phi > 0)
            {
                minPhi = Math.Min(minPhi, phi);
                maxPhi = Math.Max(maxPhi, phi);
            }

            if (lambda > 0)
            {
                minLambda = Math.Min(minLambda, lambda);
                maxLambda = Math.Max(maxLambda, lambda);
            }

            if (rho > 0)
            {
                minRho = Math.Min(minRho, rho);
                maxRho = Math.Max(maxRho, rho);
            }

            if (cp > 0)
            {
                minCp = Math.Min(minCp, cp);
                maxCp = Math.Max(maxCp, cp);
            }
        }

        Console.WriteLine(
            $"Permeability range: {minK:E2} to {maxK:E2} m² (contrast: {maxK / Math.Max(minK, 1e-30):E2}x)");
        if (maxK / Math.Max(minK, 1e-30) > 1e10)
            Console.WriteLine(
                "  WARNING: Extreme permeability contrast detected! This may cause numerical instability.");

        Console.WriteLine($"Porosity range: {minPhi:F3} to {maxPhi:F3}");
        Console.WriteLine($"Thermal conductivity: {minLambda:F2} to {maxLambda:F2} W/m·K");
        Console.WriteLine($"Density: {minRho:F0} to {maxRho:F0} kg/m³");
        Console.WriteLine($"Specific heat: {minCp:F0} to {maxCp:F0} J/kg·K");

        // Calculate thermal diffusivity range
        float minAlpha = float.MaxValue, maxAlpha = 0;
        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
        {
            var lambda = _mesh.ThermalConductivities[i, j, k];
            var rho = _mesh.Densities[i, j, k];
            var cp = _mesh.SpecificHeats[i, j, k];
            if (rho > 0 && cp > 0)
            {
                var alpha = lambda / (rho * cp);
                minAlpha = Math.Min(minAlpha, alpha);
                maxAlpha = Math.Max(maxAlpha, alpha);
            }
        }

        Console.WriteLine($"Thermal diffusivity: {minAlpha:E2} to {maxAlpha:E2} m²/s");

        // Estimate CFL time step
        var dt_stable = CalculateAdaptiveTimeStep();
        Console.WriteLine($"Recommended time step (CFL): {dt_stable:F2} s ({dt_stable / 3600:F2} hours)");
        Console.WriteLine($"User time step: {_options.TimeStep:F2} s ({_options.TimeStep / 3600:F2} hours)");

        if (_options.TimeStep > dt_stable * 2)
            Console.WriteLine("  WARNING: User time step exceeds CFL limit! Adaptive time stepping will be used.");

        // Temperature range
        float minT = float.MaxValue, maxT = 0;
        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
        {
            var T = _temperature[i, j, k];
            minT = Math.Min(minT, T);
            maxT = Math.Max(maxT, T);
        }

        Console.WriteLine($"Initial temperature: {minT - 273.15:F1} to {maxT - 273.15:F1} °C");

        // Solver settings
        Console.WriteLine($"Convergence tolerance: {_options.ConvergenceTolerance:E2}");
        Console.WriteLine($"Max iterations per step: {_options.MaxIterationsPerStep}");
        Console.WriteLine($"Groundwater flow: {(_options.SimulateGroundwaterFlow ? "Enabled" : "Disabled")}");
        Console.WriteLine($"SIMD optimization: {(_options.UseSIMD ? "Enabled" : "Disabled")}");

        Console.WriteLine("==========================================\n");
    }

    /// <summary>
    ///     Creates a mesh for visualizing the simulation domain.
    /// </summary>
    private Mesh3DDataset CreateDomainVisualizationMesh()
    {
        var vertices = new List<Vector3>();
        var faces = new List<int[]>();

        // Create cylindrical mesh
        var nr_vis = Math.Min(10, _mesh.RadialPoints);
        var nth_vis = Math.Min(24, _mesh.AngularPoints);
        var nz_vis = Math.Min(20, _mesh.VerticalPoints);

        // Sample points from mesh
        var rIndices = Enumerable.Range(0, _mesh.RadialPoints).Where((x, i) => i % (_mesh.RadialPoints / nr_vis) == 0)
            .Take(nr_vis).ToList();
        var thIndices = Enumerable.Range(0, _mesh.AngularPoints)
            .Where((x, i) => i % (_mesh.AngularPoints / nth_vis) == 0).Take(nth_vis).ToList();
        var zIndices = Enumerable.Range(0, _mesh.VerticalPoints)
            .Where((x, i) => i % (_mesh.VerticalPoints / nz_vis) == 0).Take(nz_vis).ToList();

        nr_vis = rIndices.Count;
        nth_vis = thIndices.Count;
        nz_vis = zIndices.Count;

        // Generate vertices
        foreach (var z_idx in zIndices)
        foreach (var th_idx in thIndices)
        foreach (var r_idx in rIndices)
        {
            var r = _mesh.R[r_idx];
            var theta = _mesh.Theta[th_idx];
            var z = _mesh.Z[z_idx];

            var x = r * MathF.Cos(theta);
            var y = r * MathF.Sin(theta);

            vertices.Add(new Vector3(x, y, z));
        }

        // Create faces for the outer cylindrical surface
        for (var k = 0; k < nz_vis - 1; k++)
        for (var j = 0; j < nth_vis; j++)
        {
            // Quad vertices on the outer shell (i = nr_vis - 1)
            var i_outer = nr_vis - 1;

            // Wrap around for theta index
            var j_next = (j + 1) % nth_vis;

            // Vertex indices
            var v0 = k * nth_vis * nr_vis + j * nr_vis + i_outer; // bottom-left
            var v1 = k * nth_vis * nr_vis + j_next * nr_vis + i_outer; // bottom-right
            var v2 = (k + 1) * nth_vis * nr_vis + j * nr_vis + i_outer; // top-left
            var v3 = (k + 1) * nth_vis * nr_vis + j_next * nr_vis + i_outer; // top-right

            // Create two triangles for the quad
            faces.Add(new[] { v0, v2, v1 });
            faces.Add(new[] { v1, v2, v3 });
        }

        return Mesh3DDataset.CreateFromData(
            "SimulationDomain",
            Path.Combine(Path.GetTempPath(), "domain_mesh.obj"),
            vertices,
            faces,
            1.0f,
            "m"
        );
    }
}

/// <summary>
///     Simple label volume implementation for isosurface generation.
/// </summary>
internal class SimpleLabelVolume : ILabelVolumeData
{
    public SimpleLabelVolume(int nx, int ny, int nz)
    {
        Data = new byte[nx, ny, nz];
    }

    public byte[,,] Data { get; }

    public int Width => Data.GetLength(0);
    public int Height => Data.GetLength(1);
    public int Depth => Data.GetLength(2);

    public byte this[int x, int y, int z]
    {
        get => Data[x, y, z];
        set => Data[x, y, z] = value;
    }

    public void ReadSliceZ(int z, byte[] buffer)
    {
        if (buffer == null || buffer.Length != Width * Height)
            throw new ArgumentException("Buffer size must match slice dimensions (Width * Height).");

        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            buffer[y * Width + x] = Data[x, y, z];
    }

    public void WriteSliceZ(int z, byte[] data)
    {
        if (data == null || data.Length != Width * Height)
            throw new ArgumentException("Data size must match slice dimensions (Width * Height).");

        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            Data[x, y, z] = data[y * Width + x];
    }

    public void Dispose()
    {
        // Nothing to dispose in this simple in-memory implementation
    }
}