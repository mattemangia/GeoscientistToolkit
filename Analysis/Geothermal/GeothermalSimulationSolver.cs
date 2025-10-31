// GeoscientistToolkit/Analysis/Geothermal/GeothermalSimulationSolver.cs

using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.VolumeData;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Implements the numerical solver for coupled heat transfer and groundwater flow in geothermal systems.
/// </summary>
public class GeothermalSimulationSolver
{
    private readonly GeothermalSimulationOptions _options;
    private readonly GeothermalMesh _mesh;
    private readonly IProgress<(float progress, string message)> _progress;
    private readonly CancellationToken _cancellationToken;
    
    // Field arrays
    private float[,,] _temperature;
    private float[,,] _initialTemperature;
    private float[,,] _pressure;
    private float[,,] _hydraulicHead;
    private float[,,,] _velocity; // [r,theta,z,component]
    private float[,,] _pecletNumber;
    private float[,,] _dispersionCoefficient; // Changed from _dispersivity
    
    // Heat exchanger states
    private float[] _fluidTempDown;
    private float[] _fluidTempUp;
    
    // Performance tracking
    private int _totalIterations = 0;
    private double _maxError = 0;
    
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
        
        InitializeFields();
    }
    
    /// <summary>
    /// Executes the geothermal simulation.
    /// </summary>
    public async Task<GeothermalSimulationResults> RunSimulationAsync()
    {
        var results = new GeothermalSimulationResults { Options = _options };
        var startTime = DateTime.Now;
        
        _progress?.Report((0f, "Initializing simulation..."));
        
        // Time stepping loop
        var currentTime = 0.0;
        var timeSteps = (int)(_options.SimulationTime / _options.TimeStep);
        var saveCounter = 0;
        
        for (int step = 0; step < timeSteps; step++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            
            // Progress reporting
            if (step % 10 == 0)
            {
                var progress = (float)step / timeSteps;
                var message = $"Time step {step}/{timeSteps}, t = {currentTime/86400:F1} days";
                _progress?.Report((progress, message));
            }
            
            // Solve coupled system
            if (_options.SimulateGroundwaterFlow)
            {
                await SolveGroundwaterFlowAsync();
                CalculatePecletAndDispersivity();
            }
            
            await SolveHeatTransferAsync();
            UpdateHeatExchanger();
            
            // Save results at intervals
            if (++saveCounter >= _options.SaveInterval)
            {
                saveCounter = 0;
                SaveTimeStepResults(results, currentTime);
            }
            
            currentTime += _options.TimeStep;
        }
        
        // Final results processing
        _progress?.Report((0.9f, "Processing final results..."));
        
        results.FinalTemperatureField = (float[,,])_temperature.Clone();
        results.PressureField = (float[,,])_pressure.Clone();
        results.HydraulicHeadField = (float[,,])_hydraulicHead.Clone();
        results.DarcyVelocityField = (float[,,,])_velocity.Clone();
        results.PecletNumberField = (float[,,])_pecletNumber.Clone();
        results.DispersivityField = (float[,,])_dispersionCoefficient.Clone(); // Corrected field name
        
        // Calculate performance metrics
        CalculatePerformanceMetrics(results);
        
        // Generate visualization data
        await GenerateVisualizationDataAsync(results);
        
        // Computational statistics
        results.ComputationTime = DateTime.Now - startTime;
        results.TimeStepsComputed = timeSteps;
        results.AverageIterationsPerStep = (double)_totalIterations / timeSteps;
        results.FinalConvergenceError = _maxError;
        results.PeakMemoryUsage = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        
        _progress?.Report((1f, "Simulation complete"));
        
        return results;
    }
    
    /// <summary>
    /// Initializes all field arrays with initial conditions.
    /// </summary>
    private void InitializeFields()
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        
        _temperature = new float[nr, nth, nz];
        _pressure = new float[nr, nth, nz];
        _hydraulicHead = new float[nr, nth, nz];
        _velocity = new float[nr, nth, nz, 3]; // r, theta, z components
        _pecletNumber = new float[nr, nth, nz];
        _dispersionCoefficient = new float[nr, nth, nz];
        
        // NEW, ROBUST LOGIC for Initial Temperature Field
        Func<float, float> getTempAtDepth;

        if (_options.InitialTemperatureProfile != null && _options.InitialTemperatureProfile.Any())
        {
            var sortedProfile = _options.InitialTemperatureProfile.OrderBy(p => p.Depth).ToList();
            
            getTempAtDepth = (depth) =>
            {
                if (sortedProfile.Count == 1)
                {
                    return (float)sortedProfile[0].Temperature;
                }

                // Find points to interpolate between
                for (int i = 0; i < sortedProfile.Count - 1; i++)
                {
                    var p1 = sortedProfile[i];
                    var p2 = sortedProfile[i+1];
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
            getTempAtDepth = (depth) => surfaceTemp + gradient * depth;
        }

        for (int i = 0; i < nr; i++)
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    // Note: mesh Z is negative downwards, so depth is -Z
                    var depth = Math.Max(0, -_mesh.Z[k]);
                    _temperature[i, j, k] = getTempAtDepth(depth);
                    
                    // Initialize hydraulic head
                    var z = _mesh.Z[k];
                    _hydraulicHead[i, j, k] = (float)(_options.HydraulicHeadTop + 
                        (_options.HydraulicHeadBottom - _options.HydraulicHeadTop) * 
                        (z - _mesh.Z[0]) / (_mesh.Z[nz-1] - _mesh.Z[0]));
                    
                    // Convert to pressure (Pa)
                    _pressure[i, j, k] = (float)(1000 * 9.81 * _hydraulicHead[i, j, k]);
                }
            }
        }

        // Store a copy for calculating temperature changes later
        _initialTemperature = (float[,,])_temperature.Clone();
        
        // Initialize heat exchanger
        var nzHE = 20; // Discretization along heat exchanger
        _fluidTempDown = new float[nzHE];
        _fluidTempUp = new float[nzHE];
        
        for (int i = 0; i < nzHE; i++)
        {
            _fluidTempDown[i] = (float)_options.FluidInletTemperature;
            _fluidTempUp[i] = (float)_options.FluidInletTemperature;
        }
    }
    
    /// <summary>
    /// Solves the groundwater flow equation using SIMD-optimized iterations.
    /// </summary>
    private async Task SolveGroundwaterFlowAsync()
    {
        await Task.Run(() =>
        {
            var nr = _mesh.RadialPoints;
            var nth = _mesh.AngularPoints;
            var nz = _mesh.VerticalPoints;
            
            var newHead = new float[nr, nth, nz];
            
            for (int iter = 0; iter < _options.MaxIterationsPerStep; iter++)
            {
                float maxChange;
                
                // Interior points - SIMD optimized
                if (_options.UseSIMD && Avx2.IsSupported)
                {
                    maxChange = SolveGroundwaterFlowSIMD(newHead);
                }
                else
                {
                    maxChange = SolveGroundwaterFlowScalar(newHead);
                }
                if (float.IsNaN(maxChange) || float.IsInfinity(maxChange))
                {
                    // The solver has exploded. Throw a specific, informative exception.
                    throw new ArithmeticException(
                        "Groundwater flow solver diverged. The solution exploded to Infinity or NaN. " +
                        "This is likely caused by numerical instability. " +
                        "Try reducing the 'Time Step' or increasing grid spacing."
                    );
                }
                // Swap arrays
                (_hydraulicHead, newHead) = (newHead, _hydraulicHead);
                
                if (maxChange < _options.ConvergenceTolerance)
                    break;
            }
            
            // Calculate velocities from hydraulic head using Darcy's law
            CalculateDarcyVelocities();
        });
    }
    
    /// <summary>
    /// SIMD-optimized groundwater flow solver.
    /// </summary>
    private float SolveGroundwaterFlowSIMD(float[,,] newHead)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        var vecSize = Vector256<float>.Count;
        float maxChange = 0f;
        var lockObj = new object();
        
        Parallel.For(1, nr - 1, 
            () => 0f,
            (i, loopState, localMaxChange) =>
            {
                for (int k = 1; k < nz - 1; k++)
                {
                    var r = _mesh.R[i];
                    var dr_m = _mesh.R[i] - _mesh.R[i-1];
                    var dr_p = _mesh.R[i+1] - _mesh.R[i];
                    var dz_m = _mesh.Z[k] - _mesh.Z[k-1];
                    var dz_p = _mesh.Z[k+1] - _mesh.Z[k];
                    
                    // Process multiple angular points at once
                    int j = 0;
                    for (; j <= nth - vecSize; j += vecSize)
                    {
                        // Load permeabilities
                        var K = Vector256.Create(
                            _mesh.Permeabilities[i,j,k], _mesh.Permeabilities[i,j+1,k],
                            _mesh.Permeabilities[i,j+2,k], _mesh.Permeabilities[i,j+3,k],
                            _mesh.Permeabilities[i,j+4,k], _mesh.Permeabilities[i,j+5,k],
                            _mesh.Permeabilities[i,j+6,k], _mesh.Permeabilities[i,j+7,k]
                        );
                        
                        // Finite difference coefficients
                        var invR2 = Vector256.Create(1f / (r * r));
                        var invDr2 = Vector256.Create(2f / (dr_m * dr_p));
                        var invDz2 = Vector256.Create(2f / (dz_m * dz_p));
                        var invDth2 = Vector256.Create((float)(nth * nth / (4 * Math.PI * Math.PI)));
                        
                        // Load neighboring heads
                        var h_c = Vector256.Create(
                            _hydraulicHead[i,j,k], _hydraulicHead[i,j+1,k],
                            _hydraulicHead[i,j+2,k], _hydraulicHead[i,j+3,k],
                            _hydraulicHead[i,j+4,k], _hydraulicHead[i,j+5,k],
                            _hydraulicHead[i,j+6,k], _hydraulicHead[i,j+7,k]
                        );
                        
                        // Radial neighbors
                        var h_rm = Vector256.Create(
                            _hydraulicHead[i-1,j,k], _hydraulicHead[i-1,j+1,k],
                            _hydraulicHead[i-1,j+2,k], _hydraulicHead[i-1,j+3,k],
                            _hydraulicHead[i-1,j+4,k], _hydraulicHead[i-1,j+5,k],
                            _hydraulicHead[i-1,j+6,k], _hydraulicHead[i-1,j+7,k]
                        );
                        
                        var h_rp = Vector256.Create(
                            _hydraulicHead[i+1,j,k], _hydraulicHead[i+1,j+1,k],
                            _hydraulicHead[i+1,j+2,k], _hydraulicHead[i+1,j+3,k],
                            _hydraulicHead[i+1,j+4,k], _hydraulicHead[i+1,j+5,k],
                            _hydraulicHead[i+1,j+6,k], _hydraulicHead[i+1,j+7,k]
                        );
                        
                        // Angular neighbors (with periodic BC)
                        var jm = new int[vecSize];
                        var jp = new int[vecSize];
                        for (int v = 0; v < vecSize; v++)
                        {
                            jm[v] = (j + v - 1 + nth) % nth;
                            jp[v] = (j + v + 1) % nth;
                        }
                        
                        var h_thm = Vector256.Create(
                            _hydraulicHead[i,jm[0],k], _hydraulicHead[i,jm[1],k],
                            _hydraulicHead[i,jm[2],k], _hydraulicHead[i,jm[3],k],
                            _hydraulicHead[i,jm[4],k], _hydraulicHead[i,jm[5],k],
                            _hydraulicHead[i,jm[6],k], _hydraulicHead[i,jm[7],k]
                        );
                        
                        var h_thp = Vector256.Create(
                            _hydraulicHead[i,jp[0],k], _hydraulicHead[i,jp[1],k],
                            _hydraulicHead[i,jp[2],k], _hydraulicHead[i,jp[3],k],
                            _hydraulicHead[i,jp[4],k], _hydraulicHead[i,jp[5],k],
                            _hydraulicHead[i,jp[6],k], _hydraulicHead[i,jp[7],k]
                        );
                        
                        // Vertical neighbors
                        var h_zm = Vector256.Create(
                            _hydraulicHead[i,j,k-1], _hydraulicHead[i,j+1,k-1],
                            _hydraulicHead[i,j+2,k-1], _hydraulicHead[i,j+3,k-1],
                            _hydraulicHead[i,j+4,k-1], _hydraulicHead[i,j+5,k-1],
                            _hydraulicHead[i,j+6,k-1], _hydraulicHead[i,j+7,k-1]
                        );
                        
                        var h_zp = Vector256.Create(
                            _hydraulicHead[i,j,k+1], _hydraulicHead[i,j+1,k+1],
                            _hydraulicHead[i,j+2,k+1], _hydraulicHead[i,j+3,k+1],
                            _hydraulicHead[i,j+4,k+1], _hydraulicHead[i,j+5,k+1],
                            _hydraulicHead[i,j+6,k+1], _hydraulicHead[i,j+7,k+1]
                        );
                        
                        // Laplacian in cylindrical coordinates
                        var laplacian = Avx2.Multiply(invDr2, Avx2.Subtract(h_rp, Avx2.Subtract(Avx2.Multiply(Vector256.Create(2f), h_c), h_rm)));
                        laplacian = Avx2.Add(laplacian, Avx2.Multiply(invR2, Avx2.Multiply(invDth2, 
                            Avx2.Subtract(h_thp, Avx2.Subtract(Avx2.Multiply(Vector256.Create(2f), h_c), h_thm)))));
                        laplacian = Avx2.Add(laplacian, Avx2.Multiply(invDz2, Avx2.Subtract(h_zp, Avx2.Subtract(Avx2.Multiply(Vector256.Create(2f), h_c), h_zm))));
                        
                        // Add radial derivative term
                        var radialTerm = Avx2.Divide(Avx2.Subtract(h_rp, h_rm), Vector256.Create(r * (dr_p + dr_m)));
                        laplacian = Avx2.Add(laplacian, radialTerm);
                        
                        // Update with relaxation
                        var omega = Vector256.Create(1.5f); // SOR factor
                        var h_new = Avx2.Add(h_c, Avx2.Multiply(omega, Avx2.Multiply(K, laplacian)));
                        
                        // Store results
                        for (int v = 0; v < vecSize && j + v < nth; v++)
                        {
                            newHead[i, j + v, k] = h_new.GetElement(v);
                            var change = Math.Abs(h_new.GetElement(v) - h_c.GetElement(v));
                            localMaxChange = Math.Max(localMaxChange, change);
                        }
                    }
                    
                    // Handle remaining elements
                    for (; j < nth; j++)
                    {
                        var change = SolveGroundwaterFlowSinglePoint(i, j, k, newHead);
                        localMaxChange = Math.Max(localMaxChange, change);
                    }
                }
                return localMaxChange;
            },
            (localMaxChange) =>
            {
                lock (lockObj)
                {
                    maxChange = Math.Max(maxChange, localMaxChange);
                }
            });
        return maxChange;
    }
    
    /// <summary>
    /// Scalar fallback for groundwater flow solver.
    /// </summary>
    private float SolveGroundwaterFlowScalar(float[,,] newHead)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        float maxChange = 0f;
        var lockObj = new object();

        Parallel.For(1, nr - 1,
            () => 0f, // localInit
            (i, loopState, localMaxChange) =>
            {
                for (int j = 0; j < nth; j++)
                {
                    for (int k = 1; k < nz - 1; k++)
                    {
                        var change = SolveGroundwaterFlowSinglePoint(i, j, k, newHead);
                        localMaxChange = Math.Max(localMaxChange, change);
                    }
                }
                return localMaxChange;
            },
            (localMaxChange) =>
            {
                lock (lockObj)
                {
                    maxChange = Math.Max(maxChange, localMaxChange);
                }
            });
        return maxChange;
    }
    
    private float SolveGroundwaterFlowSinglePoint(int i, int j, int k, float[,,] newHead)
    {
        var nth = _mesh.AngularPoints;
        var r = _mesh.R[i];
        var K = _mesh.Permeabilities[i, j, k];
        
        var jm = (j - 1 + nth) % nth;
        var jp = (j + 1) % nth;
        
        var dr_m = _mesh.R[i] - _mesh.R[i-1];
        var dr_p = _mesh.R[i+1] - _mesh.R[i];
        var dth = 2f * MathF.PI / nth;
        var dz_m = _mesh.Z[k] - _mesh.Z[k-1];
        var dz_p = _mesh.Z[k+1] - _mesh.Z[k];
        
        // Laplacian in cylindrical coordinates
        var d2h_dr2 = (_hydraulicHead[i+1,j,k] - 2*_hydraulicHead[i,j,k] + _hydraulicHead[i-1,j,k]) / (dr_m * dr_p);
        var dh_dr = (_hydraulicHead[i+1,j,k] - _hydraulicHead[i-1,j,k]) / (dr_p + dr_m);
        var d2h_dth2 = (_hydraulicHead[i,jp,k] - 2*_hydraulicHead[i,j,k] + _hydraulicHead[i,jm,k]) / (r * r * dth * dth);
        var d2h_dz2 = (_hydraulicHead[i,j,k+1] - 2*_hydraulicHead[i,j,k] + _hydraulicHead[i,j,k-1]) / (dz_m * dz_p);
        
        var laplacian = d2h_dr2 + dh_dr/r + d2h_dth2 + d2h_dz2;
        
        newHead[i,j,k] = _hydraulicHead[i,j,k] + 1.5f * K * laplacian;
        
        return Math.Abs(newHead[i,j,k] - _hydraulicHead[i,j,k]);
    }
    
    /// <summary>
    /// Calculates Darcy velocities from the hydraulic head field.
    /// </summary>
    private void CalculateDarcyVelocities()
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        
        Parallel.For(0, nr, i =>
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    var K = _mesh.Permeabilities[i,j,k];
                    var phi = _mesh.Porosities[i,j,k];
                    
                    // Radial component
                    if (i > 0 && i < nr - 1)
                    {
                        var dh_dr = (_hydraulicHead[i+1,j,k] - _hydraulicHead[i-1,j,k]) / 
                                   (_mesh.R[i+1] - _mesh.R[i-1]);
                        _velocity[i,j,k,0] = -K * dh_dr / phi;
                    }
                    
                    // Angular component
                    var jm = (j - 1 + nth) % nth;
                    var jp = (j + 1) % nth;
                    var dh_dth = (_hydraulicHead[i,jp,k] - _hydraulicHead[i,jm,k]) / 
                                (2f * 2f * MathF.PI / nth);
                    _velocity[i,j,k,1] = -K * dh_dth / (_mesh.R[i] * phi);
                    
                    // Vertical component
                    if (k > 0 && k < nz - 1)
                    {
                        var dh_dz = (_hydraulicHead[i,j,k+1] - _hydraulicHead[i,j,k-1]) / 
                                   (_mesh.Z[k+1] - _mesh.Z[k-1]);
                        _velocity[i,j,k,2] = -K * dh_dz / phi;
                    }
                    
                    // Add regional flow
                    _velocity[i,j,k,0] += (float)_options.GroundwaterVelocity.X;
                    _velocity[i,j,k,1] += (float)_options.GroundwaterVelocity.Y / _mesh.R[i];
                    _velocity[i,j,k,2] += (float)_options.GroundwaterVelocity.Z;
                }
            }
        });
    }
    
    /// <summary>
    /// Solves the heat transfer equation with advection and dispersion.
    /// </summary>
    private async Task SolveHeatTransferAsync()
    {
        await Task.Run(() =>
        {
            var nr = _mesh.RadialPoints;
            var nth = _mesh.AngularPoints;
            var nz = _mesh.VerticalPoints;
            
            var newTemp = new float[nr, nth, nz];
            var dt = (float)_options.TimeStep;
            
            _maxError = 0;
            
            for (int iter = 0; iter < _options.MaxIterationsPerStep; iter++)
            {
                float maxChange;
                _totalIterations++;
                
                // Interior points
                if (_options.UseSIMD && Avx2.IsSupported)
                {
                    maxChange = SolveHeatTransferSIMD(newTemp, dt);
                }
                else
                {
                    maxChange = SolveHeatTransferScalar(newTemp, dt);
                }
                if (float.IsNaN(maxChange) || float.IsInfinity(maxChange))
                {
                    throw new ArithmeticException(
                        "Heat transfer solver diverged. The solution exploded to Infinity or NaN. " +
                        "This is likely caused by numerical instability. " +
                        "Try reducing the 'Time Step'. Check for extreme thermal properties."
                    );
                }
                // Apply boundary conditions
                ApplyBoundaryConditions(newTemp);
                
                // Apply heat exchanger source/sink
                ApplyHeatExchangerSource(newTemp);
                
                // Swap arrays
                (_temperature, newTemp) = (newTemp, _temperature);
                
                _maxError = maxChange;
                
                if (maxChange < _options.ConvergenceTolerance)
                    break;
            }
        });
    }
    /// <summary>
    /// Validates the simulation options and borehole dataset before starting simulation.
    /// This method should be called at the beginning of RunSimulationAsync().
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
        {
            throw new InvalidOperationException(
                $"Invalid borehole depth: {options.BoreholeDataset.TotalDepth} meters. " +
                "The borehole must have a positive depth. Please ensure the borehole dataset is properly initialized.");
        }
        
        // Check for empty lithology units
        if (options.BoreholeDataset.LithologyUnits == null || options.BoreholeDataset.LithologyUnits.Count == 0)
        {
            throw new InvalidOperationException(
                "The borehole dataset has no lithology units defined. " +
                "At least one lithology unit is required for simulation. " +
                "Please define geological layers before running the simulation.");
        }
        
        // Validate lithology units
        foreach (var unit in options.BoreholeDataset.LithologyUnits)
        {
            if (unit.DepthTo <= unit.DepthFrom)
            {
                throw new InvalidOperationException(
                    $"Invalid lithology unit '{unit.Name}': DepthTo ({unit.DepthTo}) must be greater than DepthFrom ({unit.DepthFrom}).");
            }
            
            if (unit.DepthFrom < 0)
            {
                throw new InvalidOperationException(
                    $"Invalid lithology unit '{unit.Name}': DepthFrom ({unit.DepthFrom}) cannot be negative.");
            }
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
            throw new ArgumentException("Fluid thermal conductivity must be positive.", nameof(options.FluidThermalConductivity));
        
        // Check layer properties
        if (!options.LayerThermalConductivities.Any())
        {
            // Set defaults if not specified
            options.SetDefaultValues();
        }
        
        // Validate layer properties
        foreach (var kvp in options.LayerThermalConductivities)
        {
            if (kvp.Value <= 0)
                throw new ArgumentException($"Thermal conductivity for layer '{kvp.Key}' must be positive.");
        }
        
        foreach (var kvp in options.LayerSpecificHeats)
        {
            if (kvp.Value <= 0)
                throw new ArgumentException($"Specific heat for layer '{kvp.Key}' must be positive.");
        }
        
        foreach (var kvp in options.LayerDensities)
        {
            if (kvp.Value <= 0)
                throw new ArgumentException($"Density for layer '{kvp.Key}' must be positive.");
        }
        
        // Check convergence parameters
        if (options.ConvergenceTolerance <= 0)
            throw new ArgumentException("Convergence tolerance must be positive.", nameof(options.ConvergenceTolerance));
        
        if (options.MaxIterationsPerStep < 1)
            throw new ArgumentException("Maximum iterations per step must be at least 1.", nameof(options.MaxIterationsPerStep));
    }
    /// <summary>
    /// SIMD-optimized heat transfer solver.
    /// </summary>
    private float SolveHeatTransferSIMD(float[,,] newTemp, float dt)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        var vecSize = Vector256<float>.Count;
        float maxChange = 0f;
        var lockObj = new object();
        
        Parallel.For(1, nr - 1, 
            () => 0f, // localInit
            (i, loopState, localMaxChange) =>
            {
                for (int k = 1; k < nz - 1; k++)
                {
                    var r = _mesh.R[i];
                    var dr_m = _mesh.R[i] - _mesh.R[i-1];
                    var dr_p = _mesh.R[i+1] - _mesh.R[i];
                    var dth = 2f * MathF.PI / nth;
                    var dz_m = _mesh.Z[k] - _mesh.Z[k-1];
                    var dz_p = _mesh.Z[k+1] - _mesh.Z[k];
                    
                    int j = 0;
                    for (; j <= nth - vecSize; j += vecSize)
                    {
                        // Load material properties
                        var lambda = Vector256.Create(
                            _mesh.ThermalConductivities[i,j,k], _mesh.ThermalConductivities[i,j+1,k],
                            _mesh.ThermalConductivities[i,j+2,k], _mesh.ThermalConductivities[i,j+3,k],
                            _mesh.ThermalConductivities[i,j+4,k], _mesh.ThermalConductivities[i,j+5,k],
                            _mesh.ThermalConductivities[i,j+6,k], _mesh.ThermalConductivities[i,j+7,k]
                        );
                        
                        var rho_cp = Vector256.Create(
                            _mesh.Densities[i,j,k] * _mesh.SpecificHeats[i,j,k],
                            _mesh.Densities[i,j+1,k] * _mesh.SpecificHeats[i,j+1,k],
                            _mesh.Densities[i,j+2,k] * _mesh.SpecificHeats[i,j+2,k],
                            _mesh.Densities[i,j+3,k] * _mesh.SpecificHeats[i,j+3,k],
                            _mesh.Densities[i,j+4,k] * _mesh.SpecificHeats[i,j+4,k],
                            _mesh.Densities[i,j+5,k] * _mesh.SpecificHeats[i,j+5,k],
                            _mesh.Densities[i,j+6,k] * _mesh.SpecificHeats[i,j+6,k],
                            _mesh.Densities[i,j+7,k] * _mesh.SpecificHeats[i,j+7,k]
                        );
                        
                        // Effective thermal diffusivity (alpha_thermal + dispersion_coeff)
                        var alpha_thermal = Avx2.Divide(lambda, rho_cp);
                        var disp_coeff = Vector256.Create(
                            _dispersionCoefficient[i,j,k], _dispersionCoefficient[i,j+1,k],
                            _dispersionCoefficient[i,j+2,k], _dispersionCoefficient[i,j+3,k],
                            _dispersionCoefficient[i,j+4,k], _dispersionCoefficient[i,j+5,k],
                            _dispersionCoefficient[i,j+6,k], _dispersionCoefficient[i,j+7,k]
                        );
                        var alpha_eff = Avx2.Add(alpha_thermal, disp_coeff);
                        
                        // Load temperatures
                        var T_c = Vector256.Create(_temperature[i,j,k], _temperature[i,j+1,k], _temperature[i,j+2,k], _temperature[i,j+3,k], _temperature[i,j+4,k], _temperature[i,j+5,k], _temperature[i,j+6,k], _temperature[i,j+7,k]);
                        var T_rm = Vector256.Create(_temperature[i-1,j,k], _temperature[i-1,j+1,k], _temperature[i-1,j+2,k], _temperature[i-1,j+3,k], _temperature[i-1,j+4,k], _temperature[i-1,j+5,k], _temperature[i-1,j+6,k], _temperature[i-1,j+7,k]);
                        var T_rp = Vector256.Create(_temperature[i+1,j,k], _temperature[i+1,j+1,k], _temperature[i+1,j+2,k], _temperature[i+1,j+3,k], _temperature[i+1,j+4,k], _temperature[i+1,j+5,k], _temperature[i+1,j+6,k], _temperature[i+1,j+7,k]);
                        var T_zm = Vector256.Create(_temperature[i,j,k-1], _temperature[i,j+1,k-1], _temperature[i,j+2,k-1], _temperature[i,j+3,k-1], _temperature[i,j+4,k-1], _temperature[i,j+5,k-1], _temperature[i,j+6,k-1], _temperature[i,j+7,k-1]);
                        var T_zp = Vector256.Create(_temperature[i,j,k+1], _temperature[i,j+1,k+1], _temperature[i,j+2,k+1], _temperature[i,j+3,k+1], _temperature[i,j+4,k+1], _temperature[i,j+5,k+1], _temperature[i,j+6,k+1], _temperature[i,j+7,k+1]);

                        var jm = new int[vecSize]; var jp = new int[vecSize];
                        for (int v = 0; v < vecSize; v++) { jm[v] = (j + v - 1 + nth) % nth; jp[v] = (j + v + 1) % nth; }
                        var T_thm = Vector256.Create(_temperature[i,jm[0],k], _temperature[i,jm[1],k], _temperature[i,jm[2],k], _temperature[i,jm[3],k], _temperature[i,jm[4],k], _temperature[i,jm[5],k], _temperature[i,jm[6],k], _temperature[i,jm[7],k]);
                        var T_thp = Vector256.Create(_temperature[i,jp[0],k], _temperature[i,jp[1],k], _temperature[i,jp[2],k], _temperature[i,jp[3],k], _temperature[i,jp[4],k], _temperature[i,jp[5],k], _temperature[i,jp[6],k], _temperature[i,jp[7],k]);

                        // Laplacian
                        var two_vec = Vector256.Create(2f);
                        var d2T_dr2 = Avx2.Divide(Avx2.Subtract(Avx2.Add(T_rp, T_rm), Avx2.Multiply(two_vec, T_c)), Vector256.Create(dr_m * dr_p));
                        var dT_dr_term = Avx2.Divide(Avx2.Subtract(T_rp, T_rm), Vector256.Create(r * (dr_p + dr_m)));
                        var d2T_dth2 = Avx2.Divide(Avx2.Subtract(Avx2.Add(T_thp, T_thm), Avx2.Multiply(two_vec, T_c)), Vector256.Create(r * r * dth * dth));
                        var d2T_dz2 = Avx2.Divide(Avx2.Subtract(Avx2.Add(T_zp, T_zm), Avx2.Multiply(two_vec, T_c)), Vector256.Create(dz_m * dz_p));
                        var laplacian = Avx2.Add(d2T_dr2, Avx2.Add(dT_dr_term, Avx2.Add(d2T_dth2, d2T_dz2)));
                        
                        // Advection term
                        var advection = Vector256<float>.Zero;
                        if (_options.SimulateGroundwaterFlow)
                        {
                            var vr = Vector256.Create(_velocity[i,j,k,0], _velocity[i,j+1,k,0], _velocity[i,j+2,k,0], _velocity[i,j+3,k,0], _velocity[i,j+4,k,0], _velocity[i,j+5,k,0], _velocity[i,j+6,k,0], _velocity[i,j+7,k,0]);
                            var vth = Vector256.Create(_velocity[i,j,k,1], _velocity[i,j+1,k,1], _velocity[i,j+2,k,1], _velocity[i,j+3,k,1], _velocity[i,j+4,k,1], _velocity[i,j+5,k,1], _velocity[i,j+6,k,1], _velocity[i,j+7,k,1]);
                            var vz = Vector256.Create(_velocity[i,j,k,2], _velocity[i,j+1,k,2], _velocity[i,j+2,k,2], _velocity[i,j+3,k,2], _velocity[i,j+4,k,2], _velocity[i,j+5,k,2], _velocity[i,j+6,k,2], _velocity[i,j+7,k,2]);
                            
                            var dT_dr = Avx2.Divide(Avx2.Subtract(T_rp, T_rm), Vector256.Create(dr_p + dr_m));
                            var dT_dth = Avx2.Divide(Avx2.Subtract(T_thp, T_thm), Vector256.Create(2f * r * dth));
                            var dT_dz = Avx2.Divide(Avx2.Subtract(T_zp, T_zm), Vector256.Create(dz_p + dz_m));
                            
                            var adv_term = Avx2.Add(Avx2.Multiply(vr, dT_dr), Avx2.Add(Avx2.Multiply(vth, dT_dth), Avx2.Multiply(vz, dT_dz)));
                            advection = Avx2.Multiply(Vector256.Create(-1f), adv_term);
                        }
                        
                        // Time integration
                        var diffusion_term = Avx2.Multiply(alpha_eff, laplacian);
                        var dT_dt = Avx2.Add(diffusion_term, advection);
                        var T_new = Avx2.Add(T_c, Avx2.Multiply(Vector256.Create(dt), dT_dt));
                        
                        // Store results
                        for (int v = 0; v < vecSize && j + v < nth; v++)
                        {
                            newTemp[i, j + v, k] = T_new.GetElement(v);
                            var change = Math.Abs(T_new.GetElement(v) - T_c.GetElement(v));
                            localMaxChange = Math.Max(localMaxChange, change);
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
            (localMaxChange) =>
            {
                lock(lockObj)
                {
                    maxChange = Math.Max(maxChange, localMaxChange);
                }
            });
        return maxChange;
    }

    /// <summary>
    /// Scalar fallback for the heat transfer solver.
    /// </summary>
    private float SolveHeatTransferScalar(float[,,] newTemp, float dt)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        float maxChange = 0f;
        var lockObj = new object();

        Parallel.For(1, nr - 1,
            () => 0f, // localInit
            (i, loopState, localMaxChange) =>
            {
                for (int j = 0; j < nth; j++)
                {
                    for (int k = 1; k < nz - 1; k++)
                    {
                        var change = SolveHeatTransferSinglePoint(i, j, k, newTemp, dt);
                        localMaxChange = Math.Max(localMaxChange, change);
                    }
                }
                return localMaxChange;
            },
            (localMaxChange) =>
            {
                lock (lockObj)
                {
                    maxChange = Math.Max(maxChange, localMaxChange);
                }
            });
        return maxChange;
    }
    
    /// <summary>
    /// Scalar heat transfer solver for single point.
    /// </summary>
    private float SolveHeatTransferSinglePoint(int i, int j, int k, float[,,] newTemp, float dt)
    {
        var nth = _mesh.AngularPoints;
        var r = _mesh.R[i];
        
        var lambda = _mesh.ThermalConductivities[i,j,k];
        var rho = _mesh.Densities[i,j,k];
        var cp = _mesh.SpecificHeats[i,j,k];
        var alpha_thermal = lambda / (rho * cp);
        var disp_coeff = _dispersionCoefficient[i,j,k];
        var alpha_eff = alpha_thermal + disp_coeff;
        
        var T_old = _temperature[i,j,k];
        
        // Calculate Laplacian
        var jm = (j - 1 + nth) % nth;
        var jp = (j + 1) % nth;
        
        var dr_m = _mesh.R[i] - _mesh.R[i-1];
        var dr_p = _mesh.R[i+1] - _mesh.R[i];
        var dth = 2f * MathF.PI / nth;
        var dz_m = _mesh.Z[k] - _mesh.Z[k-1];
        var dz_p = _mesh.Z[k+1] - _mesh.Z[k];
        
        var d2T_dr2 = (_temperature[i+1,j,k] - 2*T_old + _temperature[i-1,j,k]) / (dr_m * dr_p);
        var dT_dr = (_temperature[i+1,j,k] - _temperature[i-1,j,k]) / (dr_p + dr_m);
        var d2T_dth2 = (_temperature[i,jp,k] - 2*T_old + _temperature[i,jm,k]) / (r * r * dth * dth);
        var d2T_dz2 = (_temperature[i,j,k+1] - 2*T_old + _temperature[i,j,k-1]) / (dz_m * dz_p);
        
        var laplacian = d2T_dr2 + dT_dr/r + d2T_dth2 + d2T_dz2;
        
        // Advection term
        var advection = 0f;
        if (_options.SimulateGroundwaterFlow)
        {
            var vr = _velocity[i,j,k,0];
            var vth = _velocity[i,j,k,1];
            var vz = _velocity[i,j,k,2];
            
            var dT_dth = (_temperature[i,jp,k] - _temperature[i,jm,k]) / (2f * r * dth);
            var dT_dz = (_temperature[i,j,k+1] - _temperature[i,j,k-1]) / (dz_p + dz_m);
            
            advection = -(vr * dT_dr + vth * dT_dth + vz * dT_dz);
        }
        
        // Update temperature
        newTemp[i,j,k] = T_old + dt * (alpha_eff * laplacian + advection);
        
        return Math.Abs(newTemp[i,j,k] - T_old);
    }
    
    /// <summary>
    /// Apply boundary conditions to the temperature field.
    /// </summary>
    private void ApplyBoundaryConditions(float[,,] temp)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        
        // Outer radial boundary
        if (_options.OuterBoundaryCondition == BoundaryConditionType.Dirichlet)
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    temp[nr-1,j,k] = (float)_options.OuterBoundaryTemperature;
                }
            }
        }
        else if (_options.OuterBoundaryCondition == BoundaryConditionType.Neumann)
        {
            // Implement Neumann BC
            var dr = _mesh.R[nr-1] - _mesh.R[nr-2];
            var flux = (float)_options.OuterBoundaryHeatFlux;
            
            for (int j = 0; j < nth; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    var lambda = _mesh.ThermalConductivities[nr-1,j,k];
                    temp[nr-1,j,k] = temp[nr-2,j,k] + flux * dr / lambda;
                }
            }
        }
        
        // Top boundary
        if (_options.TopBoundaryCondition == BoundaryConditionType.Dirichlet)
        {
            for (int i = 0; i < nr; i++)
            {
                for (int j = 0; j < nth; j++)
                {
                    temp[i,j,0] = (float)_options.TopBoundaryTemperature;
                }
            }
        }
        else if (_options.TopBoundaryCondition == BoundaryConditionType.Adiabatic)
        {
            for (int i = 0; i < nr; i++)
            {
                for (int j = 0; j < nth; j++)
                {
                    temp[i,j,0] = temp[i,j,1];
                }
            }
        }
        
        // Bottom boundary
        if (_options.BottomBoundaryCondition == BoundaryConditionType.Neumann)
        {
            var dz = _mesh.Z[nz-1] - _mesh.Z[nz-2];
            var flux = (float)_options.GeothermalHeatFlux;
            
            for (int i = 0; i < nr; i++)
            {
                for (int j = 0; j < nth; j++)
                {
                    var lambda = _mesh.ThermalConductivities[i,j,nz-1];
                    temp[i,j,nz-1] = temp[i,j,nz-2] - flux * dz / lambda; // Negative because heat flows up
                }
            }
        }
    }
    
    /// <summary>
    /// Applies heat exchanger as a source/sink term.
    /// </summary>
    private void ApplyHeatExchangerSource(float[,,] temp)
    {
        // Find cells containing heat exchanger
        var rHE = (float)(_options.BoreholeDataset.Diameter / 2000.0);
        
        for (int i = 0; i < _mesh.RadialPoints; i++)
        {
            if (_mesh.R[i] > rHE * 1.5f) break;
            
            for (int j = 0; j < _mesh.AngularPoints; j++)
            {
                for (int k = 0; k < _mesh.VerticalPoints; k++)
                {
                    var depth = -_mesh.Z[k];
                    if (depth < 0 || depth > _options.BoreholeDataset.TotalDepth) continue;
                    
                    if (_mesh.MaterialIds[i,j,k] == 255) // Heat exchanger region
                    {
                        // Calculate local heat transfer
                        var heIndex = (int)(depth / _options.BoreholeDataset.TotalDepth * _fluidTempDown.Length);
                        heIndex = Math.Min(heIndex, _fluidTempDown.Length - 1);
                        
                        var Tfluid = (_options.HeatExchangerType == HeatExchangerType.UTube) ?
                            0.5f * (_fluidTempDown[heIndex] + _fluidTempUp[heIndex]) :
                            _fluidTempDown[heIndex];
                        
                        var Tground = temp[i,j,k];
                        
                        // Simple heat transfer coefficient model
                        var U = 50f; // W/m²K
                        var A = 2f * MathF.PI * (float)_options.PipeOuterDiameter * 
                               (_options.BoreholeDataset.TotalDepth / _fluidTempDown.Length);
                        
                        var Q = U * A * (Tfluid - Tground);
                        var volume = _mesh.CellVolumes[i,j,k];
                        var rho_cp = _mesh.Densities[i,j,k] * _mesh.SpecificHeats[i,j,k];
                        
                        // Apply as source term
                        temp[i,j,k] += Q * (float)_options.TimeStep / (rho_cp * volume);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Updates heat exchanger fluid temperatures.
    /// </summary>
    private void UpdateHeatExchanger()
    {
        var nz = _fluidTempDown.Length;
        var mdot = (float)_options.FluidMassFlowRate;
        var cp = (float)_options.FluidSpecificHeat;
        var dz = _options.BoreholeDataset.TotalDepth / nz;
        
        // Downward flow
        _fluidTempDown[0] = (float)_options.FluidInletTemperature;
        
        for (int i = 1; i < nz; i++)
        {
            var depth = i * dz;
            
            // Get ground temperature at this depth
            var Tground = InterpolateGroundTemperature(depth);
            
            // Heat transfer
            var U = CalculateHeatTransferCoefficient();
            var A = 2f * MathF.PI * (float)_options.PipeOuterDiameter * dz;
            var Q = U * A * (Tground - _fluidTempDown[i-1]);
            
            _fluidTempDown[i] = _fluidTempDown[i-1] + Q / (mdot * cp);
        }
        
        // Upward flow (for U-tube)
        if (_options.HeatExchangerType == HeatExchangerType.UTube)
        {
            _fluidTempUp[nz-1] = _fluidTempDown[nz-1];
            
            for (int i = nz-2; i >= 0; i--)
            {
                var depth = i * dz;
                var Tground = InterpolateGroundTemperature(depth);
                
                var U = CalculateHeatTransferCoefficient();
                var A = 2f * MathF.PI * (float)_options.PipeOuterDiameter * dz;
                var Q = U * A * (Tground - _fluidTempUp[i+1]);
                
                _fluidTempUp[i] = _fluidTempUp[i+1] + Q / (mdot * cp);
            }
        }
    }
    
    /// <summary>
    /// Interpolates ground temperature at a specific depth.
    /// </summary>
    private float InterpolateGroundTemperature(float depth)
    {
        // Find nearest grid point
        int kIndex = 0;
        for (int k = 0; k < _mesh.VerticalPoints; k++)
        {
            if (-_mesh.Z[k] >= depth)
            {
                kIndex = k;
                break;
            }
        }
        
        // Average temperature near borehole
        var temp = 0f;
        var count = 0;
        
        for (int i = 0; i < Math.Min(5, _mesh.RadialPoints); i++)
        {
            for (int j = 0; j < _mesh.AngularPoints; j++)
            {
                temp += _temperature[i,j,kIndex];
                count++;
            }
        }
        
        return temp / count;
    }
    
    /// <summary>
    /// Calculates overall heat transfer coefficient using robust correlations.
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
            
            Nu = (f / 8.0f) * (Re - 1000.0f) * Pr / 
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
    /// Calculates Péclet number and dispersivity fields.
    /// </summary>
    private void CalculatePecletAndDispersivity()
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        // Longitudinal dispersivity length (alpha_L) should be a physically-based input parameter.
        // It is a property of the geological medium.
        var longitudinalDispersivityLength = (float)(_options.LongitudinalDispersivity); // e.g., 0.5 meters
        
        Parallel.For(0, nr, i =>
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    // Velocity magnitude
                    var vr = _velocity[i,j,k,0];
                    var vth = _velocity[i,j,k,1];
                    var vz = _velocity[i,j,k,2];
                    var v_mag = MathF.Sqrt(vr*vr + vth*vth + vz*vz);
                    
                    // Characteristic length (e.g., average grain size or mesh size)
                    var L = Math.Min(_mesh.R[1] - _mesh.R[0], _mesh.Z[1] - _mesh.Z[0]);
                    
                    // Thermal diffusivity
                    var alpha = _mesh.ThermalConductivities[i,j,k] / 
                               (_mesh.Densities[i,j,k] * _mesh.SpecificHeats[i,j,k]);
                    
                    // Péclet number
                    _pecletNumber[i,j,k] = v_mag * L / alpha;
                    
                    // Mechanical dispersion coefficient D_m = alpha_L * |v|
                    _dispersionCoefficient[i,j,k] = longitudinalDispersivityLength * v_mag;
                }
            }
        });
    }
    
    /// <summary>
    /// Saves results for current time step.
    /// </summary>
    private void SaveTimeStepResults(GeothermalSimulationResults results, double currentTime)
    {
        // Save temperature field
        results.TemperatureFields[currentTime] = (float[,,])_temperature.Clone();
        
        // Calculate heat extraction rate
        var outletTemp = _options.HeatExchangerType == HeatExchangerType.UTube ? 
            _fluidTempUp[0] : _fluidTempDown[_fluidTempDown.Length-1];
        
        var Q = _options.FluidMassFlowRate * _options.FluidSpecificHeat * 
               (outletTemp - _options.FluidInletTemperature);
        
        results.HeatExtractionRate.Add((currentTime, Q));
        results.OutletTemperature.Add((currentTime, outletTemp));
        
        // Robust COP calculation based on Carnot cycle
        // These parameters should be defined in GeothermalSimulationOptions
        var hvacSupplyTempK = _options.HvacSupplyTemperatureKelvin ?? (273.15 + 35.0); // Default: 35 C for radiant heating
        var compressorEfficiency = _options.CompressorIsentropicEfficiency ?? 0.6; // Typical isentropic efficiency
        
        var inletTempK = _options.FluidInletTemperature + 273.15;
        var outletTempK = outletTemp + 273.15;
        var avgFluidTempK = (inletTempK + outletTempK) / 2.0;

        double cop;
        var Q_abs = Math.Abs(Q); // Absolute heat transfer with ground, in Watts

        if (Q > 0) // Heating mode: Q is positive (heat extracted from ground)
        {
            var T_hot_k = hvacSupplyTempK;
            var T_cold_k = avgFluidTempK;
            
            if (T_hot_k > T_cold_k)
            {
                var carnotCop = T_hot_k / (T_hot_k - T_cold_k);
                var idealWork = Q_abs / (carnotCop - 1);
                var actualWork = idealWork / compressorEfficiency;
                // COP_heating = Q_hot / W = (Q_cold + W) / W
                cop = (actualWork > 0) ? (Q_abs + actualWork) / actualWork : double.PositiveInfinity;
            }
            else
            {
                cop = double.PositiveInfinity; // No work needed if ground is hotter than supply
            }
        }
        else // Cooling mode: Q is negative (heat rejected to ground)
        {
            var T_hot_k = avgFluidTempK;
            var T_cold_k = hvacSupplyTempK; // Here T_cold is the building (chilled water) temperature
            
            if (T_hot_k > T_cold_k)
            {
                var carnotCopCooling = T_cold_k / (T_hot_k - T_cold_k);
                var idealWork = Q_abs / carnotCopCooling;
                var actualWork = idealWork / compressorEfficiency;
                // COP_cooling = Q_cold / W
                cop = (actualWork > 0) ? Q_abs / actualWork : double.PositiveInfinity;
            }
            else
            {
                cop = double.PositiveInfinity; // No work needed if ground is colder than chilled water setpoint
            }
        }
        results.CoefficientOfPerformance.Add((currentTime, cop));
    }
    
    /// <summary>
    /// Calculates final performance metrics.
    /// </summary>
    private void CalculatePerformanceMetrics(GeothermalSimulationResults results)
    {
        // Average heat extraction
        results.AverageHeatExtractionRate = results.HeatExtractionRate.Average(h => h.heatRate);
        
        // Total energy
        results.TotalExtractedEnergy = results.HeatExtractionRate.Sum(h => h.heatRate * _options.TimeStep);
        
        // Borehole thermal resistance
        var Tin = _options.FluidInletTemperature;
        var Tout = results.OutletTemperature.Last().temperature;
        var Tground = InterpolateGroundTemperature((float)(_options.BoreholeDataset.TotalDepth / 2));
        var Q_avg = results.AverageHeatExtractionRate;
        
        if (Math.Abs(Q_avg) > 1e-6)
        {
            results.BoreholeThermalResistance = Math.Abs((Tground - 0.5 * (Tin + Tout)) / Q_avg);
        }
        else
        {
            results.BoreholeThermalResistance = 0;
        }

        // Effective ground properties
        CalculateEffectiveGroundProperties(results);
        
        // Layer contributions
        CalculateLayerContributions(results);
        
        // Fluid temperature profile
        for (int i = 0; i < _fluidTempDown.Length; i++)
        {
            var depth = i * _options.BoreholeDataset.TotalDepth / _fluidTempDown.Length;
            results.FluidTemperatureProfile.Add((depth, _fluidTempDown[i], _fluidTempUp[i]));
        }
        
        // Average Péclet number
        var totalPe = 0.0;
        var count = 0;
        for (int i = 0; i < _mesh.RadialPoints; i++)
        {
            for (int j = 0; j < _mesh.AngularPoints; j++)
            {
                for (int k = 0; k < _mesh.VerticalPoints; k++)
                {
                    if (_mesh.Porosities[i,j,k] > 0.05) // Aquifer zones
                    {
                        totalPe += _pecletNumber[i,j,k];
                        count++;
                    }
                }
            }
        }
        results.AveragePecletNumber = count > 0 ? totalPe / count : 0;
        
        // Report the input dispersivity length scales used in the simulation.
        // These are material properties and should be part of the simulation options.
        results.LongitudinalDispersivity = _options.LongitudinalDispersivity;
        results.TransverseDispersivity = _options.TransverseDispersivity;
    }
    
    /// <summary>
    /// Calculates effective ground thermal properties.
    /// </summary>
    private void CalculateEffectiveGroundProperties(GeothermalSimulationResults results)
    {
        var totalVolume = 0.0;
        var totalConductivity = 0.0;
        var totalDiffusivity = 0.0;
        
        for (int i = 0; i < _mesh.RadialPoints; i++)
        {
            for (int j = 0; j < _mesh.AngularPoints; j++)
            {
                for (int k = 0; k < _mesh.VerticalPoints; k++)
                {
                    var volume = _mesh.CellVolumes[i,j,k];
                    var lambda = _mesh.ThermalConductivities[i,j,k];
                    var alpha = lambda / (_mesh.Densities[i,j,k] * _mesh.SpecificHeats[i,j,k]);
                    
                    totalVolume += volume;
                    totalConductivity += lambda * volume;
                    totalDiffusivity += alpha * volume;
                }
            }
        }
        
        results.EffectiveGroundConductivity = totalConductivity / totalVolume;
        results.GroundThermalDiffusivity = totalDiffusivity / totalVolume;
        
        // Thermal influence radius determined by analyzing the temperature field
        var halfDepthIndex = _mesh.VerticalPoints / 2;
        var tempChangeAtWall = Math.Abs(_temperature[1, 0, halfDepthIndex] - _initialTemperature[1, 0, halfDepthIndex]);
        var threshold = 0.05 * tempChangeAtWall; // Threshold is 5% of the change at the borehole wall
        double thermalRadius = _mesh.R[1];

        if (tempChangeAtWall > 1e-3)
        {
            for (int i = 2; i < _mesh.RadialPoints; i++)
            {
                double avgTempChange = 0;
                for (int j = 0; j < _mesh.AngularPoints; j++)
                {
                    avgTempChange += Math.Abs(_temperature[i, j, halfDepthIndex] - _initialTemperature[i, j, halfDepthIndex]);
                }
                avgTempChange /= _mesh.AngularPoints;

                if (avgTempChange < threshold)
                {
                    // Interpolate between the last two points for a more precise radius
                    double prevChange = 0;
                    for (int j = 0; j < _mesh.AngularPoints; j++)
                    {
                        prevChange += Math.Abs(_temperature[i - 1, j, halfDepthIndex] - _initialTemperature[i - 1, j, halfDepthIndex]);
                    }
                    prevChange /= _mesh.AngularPoints;
                    
                    if (prevChange > threshold)
                    {
                        var fraction = (prevChange - threshold) / (prevChange - avgTempChange);
                        thermalRadius = _mesh.R[i - 1] + fraction * (_mesh.R[i] - _mesh.R[i - 1]);
                    }
                    else
                    {
                         thermalRadius = _mesh.R[i-1];
                    }
                    break;
                }
                
                if (i == _mesh.RadialPoints - 1)
                {
                    // Influence reaches the edge of the domain
                    thermalRadius = _mesh.R[i];
                }
            }
        }
        results.ThermalInfluenceRadius = thermalRadius;
    }
    
    /// <summary>
    /// Calculates heat flux contributions from each geological layer.
    /// </summary>
    private void CalculateLayerContributions(GeothermalSimulationResults results)
    {
        var layerHeatFluxes = new Dictionary<string, double>();
        var layerTempChanges = new Dictionary<string, double>();
        var layerFlowRates = new Dictionary<string, double>();
        
        // Map material ID back to layer name
        var materialIdToLayerName = new Dictionary<int, string>();
        var lithologyList = _options.BoreholeDataset.Lithology;
        for(int i = 0; i < lithologyList.Count; i++)
        {
            var layerName = lithologyList[i].RockType ?? "Unknown";
            materialIdToLayerName[i + 1] = layerName; // Material ID is index + 1
            
            if (!layerHeatFluxes.ContainsKey(layerName))
            {
                layerHeatFluxes[layerName] = 0;
                layerTempChanges[layerName] = 0;
                layerFlowRates[layerName] = 0;
            }
        }

        var layerCellCounts = new Dictionary<string, int>();
        foreach (var key in layerHeatFluxes.Keys)
        {
            layerCellCounts[key] = 0;
        }

        // Iterate through mesh cells to aggregate data
        for (int k = 0; k < _mesh.VerticalPoints; k++)
        {
            for (int j = 0; j < _mesh.AngularPoints; j++)
            {
                for (int i = 0; i < _mesh.RadialPoints; i++)
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
                            var dz = (k < _mesh.VerticalPoints - 1) ? _mesh.Z[k + 1] - _mesh.Z[k] : _mesh.Z[k] - _mesh.Z[k - 1];
                            var faceArea = r * dtheta * dz;
                            layerFlowRates[layerName] += _velocity[i, j, k, 0] * faceArea; 
                        }
                    }
                }
            }
        }

        // Calculate Heat Flux at the borehole wall (approximated at the second radial node)
        int borehole_wall_r_index = 1; 
        if (_mesh.RadialPoints > 1)
        {
            var r0 = _mesh.R[borehole_wall_r_index - 1];
            var r1 = _mesh.R[borehole_wall_r_index];
            var dr = r1 - r0;

            if (dr > 1e-6)
            {
                for (int k = 0; k < _mesh.VerticalPoints; k++)
                {
                    // Assume material is consistent around circumference for a given depth
                    var matId = _mesh.MaterialIds[borehole_wall_r_index, 0, k]; 
                    if (materialIdToLayerName.TryGetValue(matId, out var layerName))
                    {
                        double totalLayerHeatFlow = 0;
                        for (int j = 0; j < _mesh.AngularPoints; j++)
                        {
                            var T0 = _temperature[borehole_wall_r_index - 1, j, k];
                            var T1 = _temperature[borehole_wall_r_index, j, k];
                            var lambda = _mesh.ThermalConductivities[borehole_wall_r_index, j, k];
                            
                            var dT_dr = (T1 - T0) / dr;
                            var q_r = -lambda * dT_dr; // Radial heat flux (W/m^2)

                            var dz = (k < _mesh.VerticalPoints - 1) ? _mesh.Z[k + 1] - _mesh.Z[k] : _mesh.Z[k] - _mesh.Z[k - 1];
                            var dtheta = 2f * MathF.PI / _mesh.AngularPoints;
                            var area = r1 * dtheta * dz;

                            totalLayerHeatFlow += q_r * area; // Total heat flow (W)
                        }
                        layerHeatFluxes[layerName] += totalLayerHeatFlow;
                    }
                }
            }
        }
        
        // Finalize averages
        foreach (var key in layerCellCounts.Keys.ToList())
        {
            if (layerCellCounts.ContainsKey(key) && layerCellCounts[key] > 0)
            {
                layerTempChanges[key] /= layerCellCounts[key];
            }
        }

        // Normalize to percentages
        var totalFlux = layerHeatFluxes.Values.Sum(Math.Abs);
        if (totalFlux > 1e-6)
        {
            foreach (var key in layerHeatFluxes.Keys.ToList())
            {
                results.LayerHeatFluxContributions[key] = 100 * Math.Abs(layerHeatFluxes[key]) / totalFlux;
                results.LayerTemperatureChanges[key] = layerTempChanges[key];
                results.LayerFlowRates[key] = layerFlowRates[key];
            }
        }
    }
    
    /// <summary>
    /// Generates visualization data.
    /// </summary>
    private async Task GenerateVisualizationDataAsync(GeothermalSimulationResults results)
    {
        _progress?.Report((0.91f, "Generating visualization data..."));
        
        // Generate temperature isosurfaces
        if (_options.Generate3DIsosurfaces)
        {
            foreach (var isoTemp in _options.IsosurfaceTemperatures)
            {
                // Create a label volume to exclude regions outside domain
                var labelData = new SimpleLabelVolume(_mesh.RadialPoints, _mesh.AngularPoints, _mesh.VerticalPoints);
                for (int i = 0; i < _mesh.RadialPoints; i++)
                {
                    for (int j = 0; j < _mesh.AngularPoints; j++)
                    {
                        for (int k = 0; k < _mesh.VerticalPoints; k++)
                        {
                            labelData.Data[i,j,k] = _mesh.MaterialIds[i,j,k] == 255 ? (byte)0 : (byte)1;
                        }
                    }
                }
                
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
        }
        
        // Generate 2D slices
        if (_options.Generate2DSlices)
        {
            foreach (var slicePos in _options.SlicePositions)
            {
                var zIndex = (int)(slicePos * (_mesh.VerticalPoints - 1));
                var depth = -_mesh.Z[zIndex];
                
                // Temperature slice
                var tempSlice = new float[_mesh.RadialPoints, _mesh.AngularPoints];
                var pressureSlice = new float[_mesh.RadialPoints, _mesh.AngularPoints];
                var velocitySlice = new float[_mesh.RadialPoints, _mesh.AngularPoints];
                
                for (int i = 0; i < _mesh.RadialPoints; i++)
                {
                    for (int j = 0; j < _mesh.AngularPoints; j++)
                    {
                        tempSlice[i,j] = _temperature[i,j,zIndex];
                        pressureSlice[i,j] = _pressure[i,j,zIndex];
                        
                        var vr = _velocity[i,j,zIndex,0];
                        var vth = _velocity[i,j,zIndex,1];
                        var vz = _velocity[i,j,zIndex,2];
                        velocitySlice[i,j] = MathF.Sqrt(vr*vr + vth*vth + vz*vz);
                    }
                }
                
                results.TemperatureSlices[depth] = tempSlice;
                results.PressureSlices[depth] = pressureSlice;
                results.VelocityMagnitudeSlices[depth] = velocitySlice;
            }
        }
        
        // Generate streamlines
        if (_options.GenerateStreamlines)
        {
            GenerateStreamlines(results);
        }
        
        // Create domain mesh
        results.DomainMesh = CreateDomainVisualizationMesh();
        
        // Create borehole mesh
        results.BoreholeMesh = GeothermalMeshGenerator.CreateBoreholeMesh(_options.BoreholeDataset, _options);
    }
    
    /// <summary>
    /// Generates streamlines for flow visualization.
    /// </summary>
    private void GenerateStreamlines(GeothermalSimulationResults results)
    {
        var random = new Random(42);
        
        for (int s = 0; s < _options.StreamlineCount; s++)
        {
            var streamline = new List<Vector3>();
            
            // Random starting point
            var r = (float)(random.NextDouble() * _options.DomainRadius);
            var theta = (float)(random.NextDouble() * 2 * Math.PI);
            var z = (float)(random.NextDouble() * (_mesh.Z[_mesh.VerticalPoints-1] - _mesh.Z[0]) + _mesh.Z[0]);
            
            var pos = new Vector3(r * MathF.Cos(theta), r * MathF.Sin(theta), z);
            
            // Trace streamline
            var dt = 0.1f;
            for (int step = 0; step < 1000; step++)
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
                    pos.Z > _mesh.Z[_mesh.VerticalPoints-1])
                {
                    break;
                }
            }
            
            if (streamline.Count > 5)
            {
                results.Streamlines.Add(streamline);
            }
        }
    }
    
    /// <summary>
    /// Interpolates velocity at arbitrary position.
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
        for (int i = 0; i < _mesh.RadialPoints - 1; i++)
        {
            if (r >= _mesh.R[i] && r <= _mesh.R[i+1])
            {
                ir = i;
                break;
            }
        }
        
        ith = (int)(theta / (2 * Math.PI) * _mesh.AngularPoints) % _mesh.AngularPoints;
        
        for (int k = 0; k < _mesh.VerticalPoints - 1; k++)
        {
            if (z >= _mesh.Z[k] && z <= _mesh.Z[k+1])
            {
                iz = k;
                break;
            }
        }
        
        // Get velocity components
        var vr = _velocity[ir,ith,iz,0];
        var vth = _velocity[ir,ith,iz,1];
        var vz = _velocity[ir,ith,iz,2];
        
        // Convert to Cartesian
        var vx = vr * MathF.Cos(theta) - vth * MathF.Sin(theta);
        var vy = vr * MathF.Sin(theta) + vth * MathF.Cos(theta);
        
        return new Vector3(vx, vy, vz);
    }
    
    /// <summary>
    /// Creates a mesh for visualizing the simulation domain.
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
        var rIndices = Enumerable.Range(0, _mesh.RadialPoints).Where((x, i) => i % (_mesh.RadialPoints/nr_vis) == 0).Take(nr_vis).ToList();
        var thIndices = Enumerable.Range(0, _mesh.AngularPoints).Where((x, i) => i % (_mesh.AngularPoints/nth_vis) == 0).Take(nth_vis).ToList();
        var zIndices = Enumerable.Range(0, _mesh.VerticalPoints).Where((x, i) => i % (_mesh.VerticalPoints/nz_vis) == 0).Take(nz_vis).ToList();
        
        nr_vis = rIndices.Count;
        nth_vis = thIndices.Count;
        nz_vis = zIndices.Count;

        // Generate vertices
        foreach (var z_idx in zIndices)
        {
            foreach (var th_idx in thIndices)
            {
                foreach (var r_idx in rIndices)
                {
                    var r = _mesh.R[r_idx];
                    var theta = _mesh.Theta[th_idx];
                    var z = _mesh.Z[z_idx];
                    
                    var x = r * MathF.Cos(theta);
                    var y = r * MathF.Sin(theta);
                    
                    vertices.Add(new Vector3(x, y, z));
                }
            }
        }
        
        // Create faces for the outer cylindrical surface
        for (int k = 0; k < nz_vis - 1; k++)
        {
            for (int j = 0; j < nth_vis; j++)
            {
                // Quad vertices on the outer shell (i = nr_vis - 1)
                int i_outer = nr_vis - 1;
                
                // Wrap around for theta index
                int j_next = (j + 1) % nth_vis;

                // Vertex indices
                int v0 = k * (nth_vis * nr_vis) + j * nr_vis + i_outer;      // bottom-left
                int v1 = k * (nth_vis * nr_vis) + j_next * nr_vis + i_outer; // bottom-right
                int v2 = (k + 1) * (nth_vis * nr_vis) + j * nr_vis + i_outer;  // top-left
                int v3 = (k + 1) * (nth_vis * nr_vis) + j_next * nr_vis + i_outer; // top-right

                // Create two triangles for the quad
                faces.Add(new[] { v0, v2, v1 });
                faces.Add(new[] { v1, v2, v3 });
            }
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
/// Simple label volume implementation for isosurface generation.
/// </summary>
internal class SimpleLabelVolume : ILabelVolumeData
{
    public byte[,,] Data { get; }
    
    public SimpleLabelVolume(int nx, int ny, int nz)
    {
        Data = new byte[nx, ny, nz];
    }

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

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                buffer[y * Width + x] = Data[x, y, z];
            }
        }
    }

    public void WriteSliceZ(int z, byte[] data)
    {
        if (data == null || data.Length != Width * Height)
            throw new ArgumentException("Data size must match slice dimensions (Width * Height).");

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Data[x, y, z] = data[y * Width + x];
            }
        }
    }

    public void Dispose()
    {
        // Nothing to dispose in this simple in-memory implementation
    }
}