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
    private float[,,] _pressure;
    private float[,,] _hydraulicHead;
    private float[,,,] _velocity; // [r,theta,z,component]
    private float[,,] _pecletNumber;
    private float[,,] _dispersivity;
    
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
        results.DispersivityField = (float[,,])_dispersivity.Clone();
        
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
        _dispersivity = new float[nr, nth, nz];
        
        // Initialize temperature with geothermal gradient
        var surfaceTemp = (float)_options.OuterBoundaryTemperature;
        var gradient = 0.03f; // 30°C/km typical
        
        for (int i = 0; i < nr; i++)
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    var depth = Math.Max(0, -_mesh.Z[k]);
                    _temperature[i, j, k] = surfaceTemp + gradient * depth;
                    
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
                var maxChange = 0f;
                
                // Interior points - SIMD optimized
                if (_options.UseSIMD && Avx2.IsSupported)
                {
                    SolveGroundwaterFlowSIMD(newHead, ref maxChange);
                }
                else
                {
                    SolveGroundwaterFlowScalar(newHead, ref maxChange);
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
    private void SolveGroundwaterFlowSIMD(float[,,] newHead, ref float maxChange)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        var vecSize = Vector256<float>.Count;
        
        Parallel.For(1, nr - 1, i =>
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
                        maxChange = Math.Max(maxChange, change);
                    }
                }
                
                // Handle remaining elements
                for (; j < nth; j++)
                {
                    SolveGroundwaterFlowSinglePoint(i, j, k, newHead, ref maxChange);
                }
            }
        });
    }
    
    /// <summary>
    /// Scalar fallback for groundwater flow solver.
    /// </summary>
    private void SolveGroundwaterFlowScalar(float[,,] newHead, ref float maxChange)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        
        Parallel.For(1, nr - 1, i =>
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 1; k < nz - 1; k++)
                {
                    SolveGroundwaterFlowSinglePoint(i, j, k, newHead, ref maxChange);
                }
            }
        });
    }
    
    private void SolveGroundwaterFlowSinglePoint(int i, int j, int k, float[,,] newHead, ref float maxChange)
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
        var d2h_dr2 = (_hydraulicHead[i+1,j,k] - 2*_hydraulicHead[i,j,k] + _hydraulicHead[i-1,j,k]) / (0.5f * dr_m * dr_p);
        var dh_dr = (_hydraulicHead[i+1,j,k] - _hydraulicHead[i-1,j,k]) / (dr_p + dr_m);
        var d2h_dth2 = (_hydraulicHead[i,jp,k] - 2*_hydraulicHead[i,j,k] + _hydraulicHead[i,jm,k]) / (r * r * dth * dth);
        var d2h_dz2 = (_hydraulicHead[i,j,k+1] - 2*_hydraulicHead[i,j,k] + _hydraulicHead[i,j,k-1]) / (0.5f * dz_m * dz_p);
        
        var laplacian = d2h_dr2 + dh_dr/r + d2h_dth2 + d2h_dz2;
        
        newHead[i,j,k] = _hydraulicHead[i,j,k] + 1.5f * K * laplacian;
        
        var change = Math.Abs(newHead[i,j,k] - _hydraulicHead[i,j,k]);
        maxChange = Math.Max(maxChange, change);
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
                var maxChange = 0f;
                _totalIterations++;
                
                // Interior points
                if (_options.UseSIMD && Avx2.IsSupported)
                {
                    SolveHeatTransferSIMD(newTemp, dt, ref maxChange);
                }
                else
                {
                    SolveHeatTransferScalar(newTemp, dt, ref maxChange);
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
    /// SIMD-optimized heat transfer solver.
    /// </summary>
    private void SolveHeatTransferSIMD(float[,,] newTemp, float dt, ref float maxChange)
    {
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        var vecSize = Vector256<float>.Count;
        
        Parallel.For(1, nr - 1, i =>
        {
            for (int k = 1; k < nz - 1; k++)
            {
                var r = _mesh.R[i];
                var dr_m = _mesh.R[i] - _mesh.R[i-1];
                var dr_p = _mesh.R[i+1] - _mesh.R[i];
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
                    
                    // Thermal diffusivity
                    var alpha = Avx2.Divide(lambda, rho_cp);
                    
                    // Load temperatures (similar to hydraulic head)
                    var T_c = Vector256.Create(
                        _temperature[i,j,k], _temperature[i,j+1,k],
                        _temperature[i,j+2,k], _temperature[i,j+3,k],
                        _temperature[i,j+4,k], _temperature[i,j+5,k],
                        _temperature[i,j+6,k], _temperature[i,j+7,k]
                    );
                    
                    // Calculate Laplacian (similar structure to groundwater)
                    // ... (implement similar to groundwater but with temperature)
                    
                    // Add advection term if groundwater flow is enabled
                    var advection = Vector256<float>.Zero;
                    if (_options.SimulateGroundwaterFlow)
                    {
                        // Load velocities
                        var vr = Vector256.Create(
                            _velocity[i,j,k,0], _velocity[i,j+1,k,0],
                            _velocity[i,j+2,k,0], _velocity[i,j+3,k,0],
                            _velocity[i,j+4,k,0], _velocity[i,j+5,k,0],
                            _velocity[i,j+6,k,0], _velocity[i,j+7,k,0]
                        );
                        
                        // Temperature gradients
                        var dT_dr = Vector256<float>.Zero; // Calculate gradient
                        
                        // Advection = -v·∇T
                        advection = Avx2.Multiply(Vector256.Create(-1f), Avx2.Multiply(vr, dT_dr));
                    }
                    
                    // Add dispersion if Peclet number is high
                    var dispersion = Vector256<float>.Zero;
                    if (_options.SimulateGroundwaterFlow)
                    {
                        var disp_coeff = Vector256.Create(
                            _dispersivity[i,j,k], _dispersivity[i,j+1,k],
                            _dispersivity[i,j+2,k], _dispersivity[i,j+3,k],
                            _dispersivity[i,j+4,k], _dispersivity[i,j+5,k],
                            _dispersivity[i,j+6,k], _dispersivity[i,j+7,k]
                        );
                        
                        // Calculate dispersive heat flux
                        // ... (implement mechanical dispersion)
                    }
                    
                    // Time integration (explicit)
                    var dT_dt = alpha; // * laplacian + advection + dispersion
                    var T_new = Avx2.Add(T_c, Avx2.Multiply(Vector256.Create(dt), dT_dt));
                    
                    // Store results
                    for (int v = 0; v < vecSize && j + v < nth; v++)
                    {
                        newTemp[i, j + v, k] = T_new.GetElement(v);
                        var change = Math.Abs(T_new.GetElement(v) - T_c.GetElement(v));
                        maxChange = Math.Max(maxChange, change);
                    }
                }
                
                // Handle remaining elements
                for (; j < nth; j++)
                {
                    SolveHeatTransferSinglePoint(i, j, k, newTemp, dt, ref maxChange);
                }
            }
        });
    }
    
    /// <summary>
    /// Scalar heat transfer solver for single point.
    /// </summary>
    private void SolveHeatTransferSinglePoint(int i, int j, int k, float[,,] newTemp, float dt, ref float maxChange)
    {
        var nth = _mesh.AngularPoints;
        var r = _mesh.R[i];
        
        var lambda = _mesh.ThermalConductivities[i,j,k];
        var rho = _mesh.Densities[i,j,k];
        var cp = _mesh.SpecificHeats[i,j,k];
        var alpha = lambda / (rho * cp);
        
        var T_old = _temperature[i,j,k];
        
        // Calculate Laplacian
        var jm = (j - 1 + nth) % nth;
        var jp = (j + 1) % nth;
        
        var dr_m = _mesh.R[i] - _mesh.R[i-1];
        var dr_p = _mesh.R[i+1] - _mesh.R[i];
        var dth = 2f * MathF.PI / nth;
        var dz_m = _mesh.Z[k] - _mesh.Z[k-1];
        var dz_p = _mesh.Z[k+1] - _mesh.Z[k];
        
        // Second derivatives
        var d2T_dr2 = (_temperature[i+1,j,k] - 2*T_old + _temperature[i-1,j,k]) / (0.5f * dr_m * dr_p);
        var dT_dr = (_temperature[i+1,j,k] - _temperature[i-1,j,k]) / (dr_p + dr_m);
        var d2T_dth2 = (_temperature[i,jp,k] - 2*T_old + _temperature[i,jm,k]) / (r * r * dth * dth);
        var d2T_dz2 = (_temperature[i,j,k+1] - 2*T_old + _temperature[i,j,k-1]) / (0.5f * dz_m * dz_p);
        
        var laplacian = d2T_dr2 + dT_dr/r + d2T_dth2 + d2T_dz2;
        
        // Advection term
        var advection = 0f;
        if (_options.SimulateGroundwaterFlow)
        {
            var vr = _velocity[i,j,k,0];
            var vth = _velocity[i,j,k,1];
            var vz = _velocity[i,j,k,2];
            
            var dT_dth = (_temperature[i,jp,k] - _temperature[i,jm,k]) / (2f * r * dth);
            var dT_dz = (k > 0 && k < _mesh.VerticalPoints-1) ? 
                (_temperature[i,j,k+1] - _temperature[i,j,k-1]) / (dz_p + dz_m) : 0f;
            
            advection = -(vr * dT_dr + vth * dT_dth + vz * dT_dz);
        }
        
        // Update temperature
        newTemp[i,j,k] = T_old + dt * (alpha * laplacian + advection);
        
        var change = Math.Abs(newTemp[i,j,k] - T_old);
        maxChange = Math.Max(maxChange, change);
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
            var U = CalculateHeatTransferCoefficient(i);
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
                
                var U = CalculateHeatTransferCoefficient(i);
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
    /// Calculates heat transfer coefficient.
    /// </summary>
    private float CalculateHeatTransferCoefficient(int index)
    {
        // Simplified model - could be enhanced with Nusselt correlations
        var Re = (float)(_options.FluidMassFlowRate * _options.PipeInnerDiameter / 
                        (_options.FluidViscosity * Math.PI * _options.PipeInnerDiameter * _options.PipeInnerDiameter / 4));
        
        var Pr = (float)(_options.FluidViscosity * _options.FluidSpecificHeat / _options.FluidThermalConductivity);
        
        // Dittus-Boelter correlation
        var Nu = 0.023f * MathF.Pow(Re, 0.8f) * MathF.Pow(Pr, 0.4f);
        
        var h_fluid = Nu * (float)_options.FluidThermalConductivity / (float)_options.PipeInnerDiameter;
        
        // Overall heat transfer coefficient
        var r_i = (float)(_options.PipeInnerDiameter / 2);
        var r_o = (float)(_options.PipeOuterDiameter / 2);
        var k_pipe = (float)_options.PipeThermalConductivity;
        
        var U = 1f / (1f/h_fluid + r_i * MathF.Log(r_o/r_i) / k_pipe);
        
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
                    
                    // Characteristic length (grain size or mesh size)
                    var L = Math.Min(_mesh.R[1] - _mesh.R[0], _mesh.Z[1] - _mesh.Z[0]);
                    
                    // Thermal diffusivity
                    var alpha = _mesh.ThermalConductivities[i,j,k] / 
                               (_mesh.Densities[i,j,k] * _mesh.SpecificHeats[i,j,k]);
                    
                    // Péclet number
                    _pecletNumber[i,j,k] = v_mag * L / alpha;
                    
                    // Dispersivity (simplified model)
                    if (_pecletNumber[i,j,k] > 1)
                    {
                        _dispersivity[i,j,k] = 0.1f * L * MathF.Log(_pecletNumber[i,j,k]);
                    }
                    else
                    {
                        _dispersivity[i,j,k] = 0.01f * L;
                    }
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
        
        // Simple COP calculation
        var cop = Math.Abs(Q) / 1000; // Simplified, assumes 1kW compressor
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
        var Q = results.AverageHeatExtractionRate;
        
        results.BoreholeThermalResistance = Math.Abs((Tground - 0.5 * (Tin + Tout)) / Q);
        
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
        
        // Dispersivities
        results.LongitudinalDispersivity = 0.1 * _options.DomainRadius / 50; // Simplified
        results.TransverseDispersivity = results.LongitudinalDispersivity / 10;
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
        
        // Thermal influence radius (approximate)
        results.ThermalInfluenceRadius = 2 * Math.Sqrt(results.GroundThermalDiffusivity * _options.SimulationTime);
    }
    
    /// <summary>
    /// Calculates heat flux contributions from each geological layer.
    /// </summary>
    private void CalculateLayerContributions(GeothermalSimulationResults results)
    {
        var layerHeatFluxes = new Dictionary<string, double>();
        var layerTempChanges = new Dictionary<string, double>();
        var layerFlowRates = new Dictionary<string, double>();
        
        foreach (var layer in _options.BoreholeDataset.Lithology)
        {
            var layerName = layer.RockType ?? "Unknown";
            if (!layerHeatFluxes.ContainsKey(layerName))
            {
                layerHeatFluxes[layerName] = 0;
                layerTempChanges[layerName] = 0;
                layerFlowRates[layerName] = 0;
            }
            
            // Calculate contribution
            // ... (implement detailed layer analysis)
        }
        
        // Normalize to percentages
        var totalFlux = layerHeatFluxes.Values.Sum();
        foreach (var key in layerHeatFluxes.Keys.ToList())
        {
            results.LayerHeatFluxContributions[key] = 100 * layerHeatFluxes[key] / totalFlux;
            results.LayerTemperatureChanges[key] = layerTempChanges[key];
            results.LayerFlowRates[key] = layerFlowRates[key];
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
        
        ith = (int)(theta / (2 * MathF.PI) * _mesh.AngularPoints) % _mesh.AngularPoints;
        
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
        var nr = Math.Min(10, _mesh.RadialPoints);
        var nth = Math.Min(24, _mesh.AngularPoints);
        var nz = Math.Min(20, _mesh.VerticalPoints);
        
        // Sample points from mesh
        var rIndices = Enumerable.Range(0, _mesh.RadialPoints).Where((x, i) => i % (_mesh.RadialPoints/nr) == 0).Take(nr);
        var thIndices = Enumerable.Range(0, _mesh.AngularPoints).Where((x, i) => i % (_mesh.AngularPoints/nth) == 0).Take(nth);
        var zIndices = Enumerable.Range(0, _mesh.VerticalPoints).Where((x, i) => i % (_mesh.VerticalPoints/nz) == 0).Take(nz);
        
        // Generate vertices
        foreach (var k in zIndices)
        {
            foreach (var j in thIndices)
            {
                foreach (var i in rIndices)
                {
                    var r = _mesh.R[i];
                    var theta = _mesh.Theta[j];
                    var z = _mesh.Z[k];
                    
                    var x = r * MathF.Cos(theta);
                    var y = r * MathF.Sin(theta);
                    
                    vertices.Add(new Vector3(x, y, z));
                }
            }
        }
        
        // Create faces (simplified - just outer surface)
        // ... (implement mesh face generation)
        
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
    
    public byte this[int x, int y, int z] => Data[x, y, z];
}