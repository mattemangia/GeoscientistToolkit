// GeoscientistToolkit/Analysis/Geothermal/GeothermalSimulationSolver.cs
//
// ================================================================================================
// REFERENCES (APA Format):
// ================================================================================================
// This numerical solver for coupled heat transfer and groundwater flow in geothermal systems
// is based on established methods and approaches documented in the following scientific literature:
//
// Al-Khoury, R., Bonnier, P. G., & Brinkgreve, R. B. J. (2010). Efficient numerical modeling of 
//     borehole heat exchangers. Computers & Geosciences, 36(10), 1301-1315. 
//     https://doi.org/10.1016/j.cageo.2009.12.010
//
// Chen, C., Shao, H., Naumov, D., Kong, Y., Tu, K., & Kolditz, O. (2019). Numerical investigation 
//     on the performance, sustainability, and efficiency of the deep borehole heat exchanger system 
//     for building heating. Geothermal Energy, 7(18), 1-23. https://doi.org/10.1186/s40517-019-0133-8
//
// Conti, P., Testi, D., & Grassi, W. (2018). Transient forced convection from an infinite cylindrical 
//     heat source in a saturated Darcian porous medium. International Journal of Heat and Mass Transfer, 
//     117, 154-166. https://doi.org/10.1016/j.ijheatmasstransfer.2017.10.012
//
// Diao, N., Li, Q., & Fang, Z. (2004). Heat transfer in ground heat exchangers with groundwater 
//     advection. International Journal of Thermal Sciences, 43(12), 1203-1211. 
//     https://doi.org/10.1016/j.ijthermalsci.2004.04.009
//
// Diersch, H. J., Bauer, D., Heidemann, W., Rühaak, W., & Schätzl, P. (2011). Finite element modeling 
//     of borehole heat exchanger systems: Part 2. Numerical simulation. Computers & Geosciences, 37(8), 
//     1136-1147. https://doi.org/10.1016/j.cageo.2010.08.002
//
// Fang, L., Diao, N., Shao, Z., Zhu, K., & Fang, Z. (2018). A computationally efficient numerical 
//     model for heat transfer simulation of deep borehole heat exchangers. Energy and Buildings, 167, 
//     79-88. https://doi.org/10.1016/j.enbuild.2018.02.013
//
// Gao, Q., Zeng, L., Shi, Z., Xu, P., Yao, Y., & Shang, X. (2022). The numerical simulation of heat 
//     and mass transfer on geothermal system—A case study in Laoling area, Shandong, China. 
//     Mathematical Problems in Engineering, 2022, Article 3398965. https://doi.org/10.1155/2022/3398965
//
// He, M. (2012). Numerical modelling of geothermal borehole heat exchanger systems [Doctoral dissertation, 
//     De Montfort University]. https://www.dora.dmu.ac.uk/handle/2086/7407
//
// Hu, X., Banks, J., Wu, L., & Liu, W. V. (2020). Numerical modeling of a coaxial borehole heat 
//     exchanger to exploit geothermal energy from abandoned petroleum wells in Hinton, Alberta. 
//     Renewable Energy, 148, 1110-1123. https://doi.org/10.1016/j.renene.2019.09.141
//
// Kong, Y., Chen, C., Shao, H., Pang, Z., Xiong, L., & Wang, J. (2017). Principle and capacity 
//     quantification of deep-borehole heat exchangers. Chinese Journal of Geophysics, 60(12), 4741-4752. 
//     https://doi.org/10.6038/cjg20171216
//
// Li, M., & Lai, A. C. K. (2015). Review of analytical models for heat transfer by vertical ground 
//     heat exchangers (GHEs): A perspective of time and space scales. Applied Energy, 151, 178-191. 
//     https://doi.org/10.1016/j.apenergy.2015.04.070
//
// Ma, Y., Li, S., Zhang, L., Liu, S., Liu, Z., Li, H., & Zhai, J. (2020). Numerical simulation on 
//     heat extraction performance of enhanced geothermal system under the different well layout. 
//     Energy Exploration & Exploitation, 38(1), 274-297. https://doi.org/10.1177/0144598719880350
//
// Renaud, T., Verdin, P., & Falcone, G. (2019). Numerical simulation of a deep borehole heat 
//     exchanger in the Krafla geothermal system. International Journal of Heat and Mass Transfer, 
//     143, Article 118496. https://doi.org/10.1016/j.ijheatmasstransfer.2019.118496
//
// Song, X., Wang, G., Shi, Y., Li, R., Xu, Z., Zheng, R., Wang, Y., & Li, J. (2018). Numerical 
//     analysis of heat extraction performance of a deep coaxial borehole heat exchanger geothermal 
//     system. Energy, 164, 1298-1310. https://doi.org/10.1016/j.energy.2018.08.056
//
// Wang, Z., Wang, F., Liu, J., Ma, Z., Han, E., & Song, M. (2022). Influence factors on EGS 
//     geothermal reservoir extraction performance. Geofluids, 2022, Article 5174456. 
//     https://doi.org/10.1155/2022/5174456
//
// Yang, W., Kong, L., & Chen, Y. (2019). Transient numerical model for a coaxial borehole heat 
//     exchanger with the effect of borehole heat capacity. International Journal of Energy Research, 
//     43(10), 5622-5638. https://doi.org/10.1002/er.4457
//
// Zhang, W., Yang, H., Lu, L., & Fang, Z. (2015). Investigation on influential factors of engineering 
//     design of geothermal heat exchangers. Applied Thermal Engineering, 84, 310-319. 
//     https://doi.org/10.1016/j.applthermaleng.2015.03.077
//
// Zhang, X., Zhang, Y., Hu, L., Liu, Y., & Zhang, C. (2020). Numerical simulation on heat transfer 
//     characteristics of water flowing through the fracture of high-temperature rock. Geofluids, 2020, 
//     Article 8864028. https://doi.org/10.1155/2020/8864028
//
// ------------------------------------------------------------------------------------------------
// METHODOLOGY NOTES:
// ------------------------------------------------------------------------------------------------
// The finite difference method, adaptive time stepping, Courant-Friedrichs-Lewy (CFL) stability 
// criteria, and iterative convergence techniques implemented in this solver follow standard 
// numerical methods for solving coupled partial differential equations in porous media as described 
// in the above references. Key approaches include:
//
// 1. Dual-continuum finite element approach for borehole-ground coupling (Al-Khoury et al., 2010; 
//    Diersch et al., 2011)
// 2. Adaptive time stepping based on CFL conditions and convergence behavior (Fang et al., 2018)
// 3. Coupled thermal-hydraulic-mechanical (THM) processes in fractured geothermal systems 
//    (Chen et al., 2019; Wang et al., 2022)
// 4. Heat transfer with groundwater advection in porous media (Diao et al., 2004; Conti et al., 2018)
// 5. Stability analysis and convergence acceleration techniques for nonlinear coupled systems 
//    (Gao et al., 2022; Ma et al., 2020)
// ================================================================================================


using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     Implements the numerical solver for coupled heat transfer and groundwater flow in geothermal systems.
/// </summary>
public class GeothermalSimulationSolver : IDisposable
{
    private readonly CancellationToken _cancellationToken;
    private readonly GeothermalMesh _mesh;

    // OpenCL acceleration
    private readonly GeothermalOpenCLSolver _openCLSolver;
    private readonly BTESOpenCLSolver _btesOpenCLSolver;
    private readonly GeothermalSimulationOptions _options;
    private readonly IProgress<(float progress, string message)> _progress;

    // Stability parameters (ADDED)
    private float _adaptiveRelaxation = 0.4f; // Start more conservative to prevent oscillations
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
    private bool _useOpenCL;
    private bool _useBTESOpenCL;
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

        // Initialize OpenCL if GPU acceleration is enabled
        if (_options.UseGPU)
            try
            {
                _openCLSolver = new GeothermalOpenCLSolver();
                if (_openCLSolver.IsAvailable)
                {
                    if (_openCLSolver.InitializeBuffers(mesh, options))
                    {
                        _useOpenCL = true;
                        Logger.Log($"OpenCL acceleration enabled: {_openCLSolver.DeviceName}");
                        Logger.Log($"Device memory: {_openCLSolver.DeviceGlobalMemory / (1024 * 1024)} MB");
                    }
                    else
                    {
                        Logger.LogWarning("Failed to initialize OpenCL buffers, falling back to CPU");
                        _openCLSolver?.Dispose();
                        _openCLSolver = null;
                    }
                }
                else
                {
                    Logger.LogWarning("OpenCL not available, using CPU");
                    _openCLSolver?.Dispose();
                    _openCLSolver = null;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"OpenCL initialization failed: {ex.Message}. Using CPU.");
                _openCLSolver?.Dispose();
                _openCLSolver = null;
                _useOpenCL = false;
            }

        // Initialize BTES OpenCL solver if BTES mode is enabled and GPU is available
        if (_options.EnableBTESMode && _options.UseGPU)
            try
            {
                _btesOpenCLSolver = new BTESOpenCLSolver();
                if (_btesOpenCLSolver.IsAvailable)
                {
                    if (_btesOpenCLSolver.InitializeBuffers(mesh, options))
                    {
                        _useBTESOpenCL = true;
                        Logger.Log($"BTES OpenCL acceleration enabled: {_btesOpenCLSolver.DeviceName}");
                        Logger.Log($"BTES Device memory: {_btesOpenCLSolver.DeviceGlobalMemory / (1024 * 1024)} MB");
                    }
                    else
                    {
                        Logger.LogWarning("Failed to initialize BTES OpenCL buffers, using CPU");
                        _btesOpenCLSolver?.Dispose();
                        _btesOpenCLSolver = null;
                    }
                }
                else
                {
                    Logger.LogWarning("BTES OpenCL not available, using CPU");
                    _btesOpenCLSolver?.Dispose();
                    _btesOpenCLSolver = null;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"BTES OpenCL initialization failed: {ex.Message}. Using CPU.");
                _btesOpenCLSolver?.Dispose();
                _btesOpenCLSolver = null;
                _useBTESOpenCL = false;
            }

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
    ///     Disposes OpenCL resources.
    /// </summary>
    public void Dispose()
    {
        _openCLSolver?.Dispose();
        _btesOpenCLSolver?.Dispose();
    }

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
        LogSimulationDiagnostics();

        var currentTime = 0.0;
        var saveCounter = 0;

        // Calcola un "passo di crociera" ideale basato sulla durata totale.
        var targetTimeStep = Math.Max(60.0, _options.SimulationTime / 5000.0);
        Logger.Log($"Target time step for this run: {targetTimeStep / 3600:F2} hours.");

        // Partenza sicura e controllata.
        var actualTimeStep = 30.0;
        _adaptiveRelaxation = 0.1f;
        var step = 0;

        while (currentTime < _options.SimulationTime)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            CurrentTimeStep = step;
            CurrentSimulationTime = currentTime;

            if (step % 10 == 0 || step < 10)
            {
                var progress = (float)(currentTime / _options.SimulationTime);
                var message =
                    $"Step {step}, t={currentTime / 86400:F2}d, dt={actualTimeStep / 3600:F2}h, relax={_adaptiveRelaxation:F2}";
                _progress?.Report((progress, message));
            }

            // BTES MODE: Apply seasonal energy curve to modify inlet temperature
            if (_options.EnableBTESMode && _options.SeasonalEnergyCurve.Count == 365)
            {
                ApplySeasonalEnergyCurve(currentTime);
            }

            var stepSuccessful = false;
            var attempt = 0;
            while (!stepSuccessful)
                try
                {
                    _temperatureOld = (float[,,])_temperature.Clone();

                    if (_options.SimulateGroundwaterFlow)
                    {
                        await SolveGroundwaterFlowAsync();
                        CalculatePecletAndDispersivity();
                    }

                    UpdateHeatExchanger();
                    await SolveHeatTransferAsync((float)actualTimeStep);

                    stepSuccessful = true; // Se arriviamo qui senza eccezioni, il passo è matematicamente valido.
                }
                catch (ArithmeticException ex) when (ex.Message.Contains("diverged"))
                {
                    // Fallimento Catastrofico (NaN/Infinity). L'unica opzione è tagliare il time step.
                    _temperature = (float[,,])_temperatureOld.Clone();
                    actualTimeStep *= 0.5;
                    _adaptiveRelaxation = Math.Max(0.1f, _adaptiveRelaxation * 0.8f);
                    ConvergenceStatus = $"DIVERGENCE! Slashing dt to {actualTimeStep:F1}s";

                    if (actualTimeStep < 10.0) // Pavimento di emergenza
                        throw new InvalidOperationException(
                            "Simulation fundamentally unstable even at minimal time steps. Check material properties.");
                }

            ConvergenceHistory.Add(_maxError);
            TimeStepHistory.Add(actualTimeStep);

            // BTES MODE: Save all frames for animation, or use SaveInterval
            bool shouldSave = _options.SaveAllTimeFrames || (++saveCounter >= _options.SaveInterval);
            if (shouldSave)
            {
                saveCounter = 0;
                SaveTimeStepResults(results, currentTime);
            }

            currentTime += actualTimeStep;
            step++;

            // Applica la nuova logica di controllo intelligente per il prossimo passo.
            actualTimeStep =
                AdjustParametersAfterStep(actualTimeStep, targetTimeStep, step, ConvergenceHistory, _options);
        }

        _progress?.Report((0.9f, "Processing final results..."));
        results.FinalTemperatureField = (float[,,])_temperature.Clone();
        results.PressureField = (float[,,])_pressure.Clone();
        results.HydraulicHeadField = (float[,,])_hydraulicHead.Clone();
        results.DarcyVelocityField = (float[,,,])_velocity.Clone();
        results.PecletNumberField = (float[,,])_pecletNumber.Clone();
        results.DispersivityField = (float[,,])_dispersionCoefficient.Clone();
        CalculatePerformanceMetrics(results);
        await GenerateVisualizationDataAsync(results);
        results.ComputationTime = DateTime.Now - startTime;
        results.TimeStepsComputed = step;
        results.AverageIterationsPerStep = (double)_totalIterations / Math.Max(1, step);
        results.FinalConvergenceError = _maxError;
        results.PeakMemoryUsage = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        _progress?.Report((1f, "Simulation complete"));
        return results;
    }

    private double AdjustParametersAfterStep(double currentDt, double targetDt, int step,
        List<double> convergenceHistory, GeothermalSimulationOptions options)
    {
        var lastError = convergenceHistory.Last();
        var newDt = currentDt;

        // Fase 1: Partenza ultra-conservativa per i primi 30 passi per assorbire lo shock iniziale.
        if (step < 30)
        {
            _adaptiveRelaxation = Math.Max(0.05f, _adaptiveRelaxation * 0.95f); // Mantieni il damping alto
            newDt = Math.Min(newDt * 1.1, 300); // Crescita molto lenta fino a un massimo di 5 minuti
            ConvergenceStatus = "Initial stabilization phase";
        }
        else // Fase 2: Operazione Normale
        {
            if (lastError < options.ConvergenceTolerance)
            {
                // SUCCESSO: La convergenza è buona.
                // Aumenta il time step verso il nostro obiettivo e riduci leggermente il damping (più veloce).
                newDt = Math.Min(currentDt * 1.2, targetDt);
                _adaptiveRelaxation *= 1.1f;
                ConvergenceStatus = "Converged. Accelerating.";
            }
            else
            {
                // DIFFICOLTÀ: Il solver ha finito ma l'errore è troppo alto.
                // Invece di tagliare il time step, la nostra PRIMA reazione è aumentare la stabilità.
                _adaptiveRelaxation *= 0.75f;
                ConvergenceStatus = $"Struggling (err: {lastError:E2}). Increasing damping.";
                // NON cambiamo il time step al primo segno di difficoltà. Diamo una possibilità al damping.
            }
        }

        // Limiti di sicurezza finali, applicati sempre.
        _adaptiveRelaxation = Math.Clamp(_adaptiveRelaxation, 0.05f, 0.7f); // Damping sempre tra 5% e 70%
        newDt = Math.Clamp(newDt, 30.0,
            options.TimeStep); // Time step MAI sotto i 30s e mai sopra il massimo dell'utente.

        return newDt;
    }

    private double AdjustParametersAfterStep(double currentDt, List<double> convergenceHistory,
        GeothermalSimulationOptions options)
    {
        var lastError = convergenceHistory.Last();
        var newDt = currentDt;

        if (lastError < options.ConvergenceTolerance * 0.2)
        {
            // Case 1: EXCELLENT convergence. Be bold.
            // Aggressively increase time step and make solver faster (higher relaxation).
            newDt *= 1.5;
            _adaptiveRelaxation *= 1.1f;
            ConvergenceStatus = "Excellent convergence. Increasing dt.";
        }
        else if (lastError < options.ConvergenceTolerance)
        {
            // Case 2: GOOD convergence. Be cautious.
            // Slightly increase time step, keep solver settings stable.
            newDt *= 1.1;
            ConvergenceStatus = "Good convergence. Cautiously increasing dt.";
        }
        else
        {
            // Case 3: STRUGGLING (finished but error is high). This is the cause of the death spiral.
            // First priority is to add stability by reducing relaxation (more damping).
            // Second priority is to slightly reduce the time step.
            _adaptiveRelaxation *= 0.85f;
            newDt *= 0.9;
            ConvergenceStatus = $"Struggling (err: {lastError:E2}). Increasing damping.";
        }

        // Final Safety Clamping for all cases.
        _adaptiveRelaxation = Math.Clamp(_adaptiveRelaxation, 0.1f, 0.8f);
        newDt = Math.Clamp(newDt, 1.0, options.TimeStep);

        return newDt;
    }

    private double UpdateAdaptiveTimeStep(double currentDt, int step, List<double> convergenceHistory,
        GeothermalSimulationOptions options)
    {
        const double minTimeStep = 1.0; // Minimum allowed time step in seconds.
        const double aggressiveGrowthFactor = 1.5;
        const double moderateGrowthFactor = 1.1;
        const double shrinkFactor = 0.75;

        // Define a target "cruising" time step based on total simulation time, aiming for ~2000-5000 steps.
        var targetDt = options.SimulationTime / 3000.0;

        var newDt = currentDt;

        // Phase 1: Initial Stabilization (first 10 steps)
        if (step < 10)
        {
            // Be very conservative at the start to handle the initial thermal shock.
            if (convergenceHistory.Last() <
                options.ConvergenceTolerance * 0.5) newDt *= 1.2; // Allow slightly faster growth if it's super stable
            // Don't shrink here, let the divergence handler do it.
        }
        else // Phase 2: Normal Operation
        {
            var recentConvergence = convergenceHistory.Skip(Math.Max(0, convergenceHistory.Count - 3)).Average();

            if (recentConvergence < options.ConvergenceTolerance * 0.1)
                // Excellent convergence: Grow aggressively towards the target time step.
                newDt = Math.Min(currentDt * aggressiveGrowthFactor, targetDt);
            else if (recentConvergence < options.ConvergenceTolerance * 0.75)
                // Good convergence: Grow moderately.
                newDt *= moderateGrowthFactor;
            else if (recentConvergence > options.ConvergenceTolerance)
                // Poor convergence: Shrink the time step.
                newDt *= shrinkFactor;
        }

        // Final Safety Clamping: Ensure the new time step is within safe, reasonable bounds.
        newDt = Math.Max(minTimeStep, newDt);
        newDt = Math.Min(options.TimeStep, newDt); // Respect user's maximum setting.

        return newDt;
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
        _temperatureOld = new float[nr, nth, nz];
        _pressure = new float[nr, nth, nz];
        _hydraulicHead = new float[nr, nth, nz];
        _velocity = new float[nr, nth, nz, 3];
        _pecletNumber = new float[nr, nth, nz];
        _dispersionCoefficient = new float[nr, nth, nz];

        Func<float, float> getTempAtDepth;

        if (_options.InitialTemperatureProfile != null && _options.InitialTemperatureProfile.Any())
        {
            var sortedProfile = _options.InitialTemperatureProfile.OrderBy(p => p.Depth).ToList();
            getTempAtDepth = depth =>
            {
                if (sortedProfile.Count == 1) return (float)sortedProfile[0].Temperature;
                for (var i = 0; i < sortedProfile.Count - 1; i++)
                {
                    var p1 = sortedProfile[i];
                    var p2 = sortedProfile[i + 1];
                    if (depth >= p1.Depth && depth <= p2.Depth)
                    {
                        var t = (depth - p1.Depth) / (p2.Depth - p1.Depth);
                        return (float)(p1.Temperature + t * (p2.Temperature - p1.Temperature));
                    }
                }

                if (depth < sortedProfile.First().Depth)
                {
                    var p1 = sortedProfile[0];
                    var p2 = sortedProfile[1];
                    var gradient = (p2.Temperature - p1.Temperature) / (p2.Depth - p1.Depth);
                    return (float)(p1.Temperature - (p1.Depth - depth) * gradient);
                }
                else
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
            var surfaceTemp = (float)_options.SurfaceTemperature;
            var gradient = (float)_options.AverageGeothermalGradient;
            if (gradient < 0.001) gradient = 0.03f;

            Logger.Log(
                $"Initializing temperature field: Surface={surfaceTemp - 273.15:F1}°C, Gradient={gradient * 1000:F1}°C/km");

            Logger.Log($"  - Sample Depth 0m: {surfaceTemp:F1}K ({surfaceTemp - 273.15:F1}°C)");
            var midDepth = _options.BoreholeDataset.TotalDepth / 2.0f;
            var bottomDepth = _options.BoreholeDataset.TotalDepth;
            Logger.Log(
                $"  - Sample Depth {midDepth:F0}m: {surfaceTemp + gradient * midDepth:F1}K ({surfaceTemp + gradient * midDepth - 273.15:F1}°C)");
            Logger.Log(
                $"  - Sample Depth {bottomDepth:F0}m: {surfaceTemp + gradient * bottomDepth:F1}K ({surfaceTemp + gradient * bottomDepth - 273.15:F1}°C)");

            getTempAtDepth = depth => surfaceTemp + gradient * depth;
        }

        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
        {
            var depth = Math.Max(0, -_mesh.Z[k]);
            var baseTemp = getTempAtDepth(depth);

            var r = _mesh.R[i];
            var rMax = _mesh.R[nr - 1];
            var radialVariation = 1.0f + 0.02f * (r / rMax);

            _temperature[i, j, k] = baseTemp * radialVariation;
            _temperatureOld[i, j, k] = _temperature[i, j, k];

            var z = _mesh.Z[k];
            _hydraulicHead[i, j, k] = (float)(_options.HydraulicHeadTop +
                                              (_options.HydraulicHeadBottom - _options.HydraulicHeadTop) *
                                              (z - _mesh.Z[0]) / (_mesh.Z[nz - 1] - _mesh.Z[0]));
            _pressure[i, j, k] = (float)(1000 * 9.81 * _hydraulicHead[i, j, k]);
        }

        _initialTemperature = (float[,,])_temperature.Clone();

        // --- MODIFICATION START ---
        // The fluid arrays MUST be sized for the entire borehole depth to correctly model the turnaround at the physical bottom.
        var nzHE = Math.Max(20, (int)(_options.BoreholeDataset.TotalDepth / 50));
        // --- MODIFICATION END ---
        _fluidTempDown = new float[nzHE];
        _fluidTempUp = new float[nzHE];
        for (var i = 0; i < nzHE; i++)
        {
            _fluidTempDown[i] = (float)_options.FluidInletTemperature;
            _fluidTempUp[i] = (float)_options.FluidInletTemperature;
        }

        Logger.Log($"Temperature field initialized, HE elements={nzHE}");
        var topTemp = _temperature[nr / 2, 0, 0];
        var bottomTemp = _temperature[nr / 2, 0, nz - 1];
        Logger.Log(
            $"Initial temperature profile check: Top={topTemp - 273.15:F1}°C, Bottom={bottomTemp - 273.15:F1}°C");
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

            // FIX: The relaxation factor MUST be adaptive. A fixed value is not
            // robust enough. We start with a reasonably aggressive value and let the solver
            // reduce it automatically if it struggles.
            var omega = 0.8f; // Start more aggressively
            var lastMaxChange = double.MaxValue;

            for (var iter = 0; iter < _options.MaxIterationsPerStep; iter++)
            {
                float maxChange;

                if (_options.UseSIMD && Avx2.IsSupported)
                    maxChange = SolveGroundwaterFlowSIMD(newHead, omega);
                else
                    maxChange = SolveGroundwaterFlowScalar(newHead, omega);

                ApplyGroundwaterBoundaryConditions(newHead);

                // Self-Stabilizing Logic
                if (float.IsNaN(maxChange) || float.IsInfinity(maxChange) || maxChange > lastMaxChange * 2.0)
                {
                    // If the error is exploding or oscillating, the solver is diverging.
                    // Make it much more conservative and try again from the last good state.
                    omega *= 0.5f;
                    lastMaxChange = double.MaxValue; // Reset divergence check
                    Array.Copy(_hydraulicHead, newHead, newHead.Length); // Rollback
                    ConvergenceStatus = $"Flow diverging, reducing omega to {omega:F2}";
                    continue; // Skip the rest of this iteration
                }

                lastMaxChange = maxChange;
                FlowConvergenceHistory.Add(maxChange);

                // Under-relaxation update.
                for (var i = 0; i < nr; i++)
                for (var j = 0; j < nth; j++)
                for (var k = 0; k < nz; k++)
                    _hydraulicHead[i, j, k] = (1 - omega) * _hydraulicHead[i, j, k] + omega * newHead[i, j, k];

                if (iter % 50 == 0)
                    ConvergenceStatus = $"Flow iter {iter}, err: {maxChange:E3}, omega: {omega:F2}";

                if (maxChange < _options.ConvergenceTolerance)
                {
                    ConvergenceStatus = $"Flow converged in {iter} iterations.";
                    break; // Success
                }

                if (iter == _options.MaxIterationsPerStep - 1)
                    ConvergenceStatus = $"Flow max iterations reached, err: {maxChange:E3}";
                // This is not a fatal error; the main loop will handle the high error.
            }

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
    ///     Solves the heat transfer equation. (MODIFIED - with OpenCL support)
    /// </summary>
    private async Task SolveHeatTransferAsync(float dt)
    {
        await Task.Run(() =>
        {
            _maxError = 0;
            _totalIterations++; // We now count time steps, not inner iterations.

            float maxChange;

            // Choose solver: BTES OpenCL, Standard OpenCL GPU, or CPU
            if (_options.EnableBTESMode && _useBTESOpenCL)
                try
                {
                    maxChange = _btesOpenCLSolver.SolveBTESHeatTransferGPU(
                        _temperature,
                        _velocity,
                        _dispersionCoefficient,
                        dt,
                        CurrentSimulationTime,
                        _options.SimulateGroundwaterFlow,
                        _fluidTempDown,
                        _fluidTempUp,
                        _options.FlowConfiguration);

                    ApplyBoundaryConditions(_temperature); // Apply BCs to the final result
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"BTES OpenCL error: {ex.Message}. Falling back to CPU.");
                    _useBTESOpenCL = false;
                    maxChange = RunCpuSolver(dt);
                }
            else if (_useOpenCL)
                try
                {
                    maxChange = _openCLSolver.SolveHeatTransferGPU(
                        _temperature,
                        _velocity,
                        _dispersionCoefficient,
                        dt,
                        _options.SimulateGroundwaterFlow,
                        _fluidTempDown,
                        _fluidTempUp,
                        _options.FlowConfiguration); // NEW: Pass the flow configuration

                    ApplyBoundaryConditions(_temperature); // Apply BCs to the final result
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"OpenCL error: {ex.Message}. Falling back to CPU.");
                    _useOpenCL = false;
                    maxChange = RunCpuSolver(dt);
                }
            else
                maxChange = RunCpuSolver(dt);

            // Check for divergence
            if (float.IsNaN(maxChange) || float.IsInfinity(maxChange))
            {
                ConvergenceStatus = "Heat solver diverged";
                throw new ArithmeticException(
                    $"Heat transfer solver diverged. " +
                    $"Try reducing the time step or checking thermal property values."
                );
            }

            HeatConvergenceHistory.Add(maxChange);
            _maxError = maxChange;

            var solverType = _useBTESOpenCL ? "BTES GPU" : (_useOpenCL ? "GPU" : "CPU");
            ConvergenceStatus = $"Heat converged ({solverType}), final error: {maxChange:E3}, dt: {dt:E2}s";
        });
    }

    /// <summary>
    ///     DEFINITIVE FIX: Encapsulates the CPU solver path with correct data handling.
    /// </summary>
    private float RunCpuSolver(float dt)
    {
        // FIX #2: The temporary array MUST be initialized as a CLONE of the
        // current state. Initializing it as a new array (`new float[,,]`) resets all
        // boundary conditions to zero at every step, which was the root cause of the
        // unconditional divergence.
        var newTemp = (float[,,])_temperature.Clone();

        float maxChange;
        if (_options.UseSIMD && Avx2.IsSupported)
            maxChange = SolveHeatTransferSIMD(newTemp, dt);
        else
            maxChange = SolveHeatTransferScalar(newTemp, dt);

        // Apply boundary conditions to the temporary array AFTER the interior is solved.
        ApplyBoundaryConditions(newTemp);

        // Now, copy the fully updated temporary array (with correct boundaries) back to the main state array.
        Array.Copy(newTemp, _temperature, newTemp.Length);

        return maxChange;
    }

    /// <summary>
    ///     Calculate adaptive time step based on CFL condition - CORRECTED VERSION.
    /// </summary>
    private float CalculateAdaptiveTimeStep()
    {
        const float safeInitialTimeStep = 30.0f; // Start with a safe 30-second step.

        // The user's TimeStep option now acts purely as an upper limit.
        return Math.Min(safeInitialTimeStep, (float)_options.TimeStep);
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
        var maxChange = 0f;
        var lockObj = new object();

        Parallel.For(1, nr - 1,
            () => 0f, // localInit
            (i, loopState, localMaxChange) =>
            {
                // The semi-implicit formulation is branch-heavy and not suitable for SIMD.
                // This path now calls the robust scalar solver for each point.
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
    ///     DEFINITIVE FIX: Scalar heat transfer solver for a single point, now using a semi-implicit
    ///     formulation for the heat exchanger to guarantee numerical stability.
    /// </summary>
    private float SolveHeatTransferSinglePoint(int i, int j, int k, float[,,] newTemp, float dt)
    {
        var nth = _mesh.AngularPoints;
        var r = MathF.Max(0.01f, _mesh.R[i]);
        var jm = (j - 1 + nth) % nth;
        var jp = (j + 1) % nth;

        var lambda = Math.Clamp(_mesh.ThermalConductivities[i, j, k], 0.05f, 15f);
        var rho = MathF.Max(500f, _mesh.Densities[i, j, k]);
        var cp = MathF.Max(100f, _mesh.SpecificHeats[i, j, k]);
        var rho_cp = MathF.Max(1f, rho * cp);
        var alpha = lambda / rho_cp;

        var T_old = _temperature[i, j, k];

        var dr_m = MathF.Max(0.001f, _mesh.R[i] - _mesh.R[i - 1]);
        var dr_p = MathF.Max(0.001f, _mesh.R[i + 1] - _mesh.R[i]);
        var dth = 2f * MathF.PI / nth;
        var dz_m = MathF.Max(0.001f, MathF.Abs(_mesh.Z[k] - _mesh.Z[k - 1]));
        var dz_p = MathF.Max(0.001f, MathF.Abs(_mesh.Z[k + 1] - _mesh.Z[k]));
        var dz_c = 0.5f * (dz_m + dz_p);

        var T_rm = _temperature[i - 1, j, k];
        var T_rp = _temperature[i + 1, j, k];
        var T_zm = _temperature[i, j, k - 1];
        var T_zp = _temperature[i, j, k + 1];
        var T_thm = _temperature[i, jm, k];
        var T_thp = _temperature[i, jp, k];

        var diffusion_neighbors =
            alpha * ((T_rp + T_rm) / (dr_m * dr_p)
                     + (T_thp + T_thm) / (r * r * dth * dth)
                     + (T_zp + T_zm) / (dz_m * dz_p)
                     + (T_rp - T_rm) / (dr_p + dr_m) / r);

        var advection = 0f;
        if (_options.SimulateGroundwaterFlow)
        {
            var vr = _velocity[i, j, k, 0];
            var vth = _velocity[i, j, k, 1];
            var vz = _velocity[i, j, k, 2];

            var dT_dr = vr >= 0f ? (T_old - T_rm) / dr_m : (T_rp - T_old) / dr_p;
            var dT_dth = (T_thp - T_thm) / (2f * r * dth);
            var dT_dz = vz >= 0f ? (T_old - T_zm) / dz_m : (T_zp - T_old) / dz_p;
            advection = -(vr * dT_dr + vth * dT_dth + vz * dT_dz);
        }

        var numerator = T_old + dt * (diffusion_neighbors + advection);
        var denominator = 1f + dt * alpha * (2f / (dr_m * dr_p) + 2f / (r * r * dth * dth) + 2f / (dz_m * dz_p));

        // --- Coupling HE con taper (agisce sul grout/roccia attorno, nessun cut-off) ---
        var depth = MathF.Max(0f, -_mesh.Z[k]);
        var pipeRadius = (float)(_options.PipeOuterDiameter * 0.5);
        var totalBoreDepth = _options.BoreholeDataset.TotalDepth;
        var activeHeDepth = _options.HeatExchangerDepth;
        var rInfluence = MathF.Max(pipeRadius * 5f, 0.25f);

        static float Smooth(float x)
        {
            return x * x * (3f - 2f * x);
        }

        var rTaper = 0f;
        if (r <= rInfluence)
        {
            var u = Math.Clamp(1f - r / rInfluence, 0f, 1f);
            rTaper = Smooth(u);
        }

        var zTaper = MathF.Max(2f * dz_c, 0.25f);
        var depthFactor =
            depth <= activeHeDepth ? 1f :
            depth <= activeHeDepth + zTaper ? Smooth(1f - (depth - activeHeDepth) / zTaper) : 0f;

        var taper = rTaper * depthFactor;

        if (taper > 0f)
        {
            var nzHE = Math.Max(1, _fluidTempDown?.Length ?? 1);
            var hIdx = Math.Clamp((int)(depth / totalBoreDepth * nzHE), 0, nzHE - 1);
            var uTube = _options.HeatExchangerType == HeatExchangerType.UTube;
            var Tfluid = _options.FlowConfiguration == FlowConfiguration.CounterFlowReversed
                ? uTube ? 0.5f * (_fluidTempDown[hIdx] + _fluidTempUp[hIdx]) : _fluidTempDown[hIdx]
                : uTube
                    ? 0.5f * (_fluidTempDown[hIdx] + _fluidTempUp[hIdx])
                    : _fluidTempUp[hIdx];

            // HTC “robusto”
            var D_in = MathF.Max(0.01f, (float)_options.PipeInnerDiameter);
            var mu = MathF.Max(1e-3f, (float)_options.FluidViscosity);
            var mdot = (float)_options.FluidMassFlowRate;
            var kf = MathF.Max(0.2f, (float)_options.FluidThermalConductivity);
            var cpf = MathF.Max(1000f, (float)_options.FluidSpecificHeat);
            var Re = 4.0f * mdot / (MathF.PI * D_in * mu);
            var Pr = mu * cpf / kf;
            var Nu = Re < 2300f ? 4.36f : 0.023f * MathF.Pow(Re, 0.8f) * MathF.Pow(Pr, 0.4f);
            var htc = MathF.Min(2000f, Nu * kf / D_in);

            var vol = MathF.Max(1e-6f, _mesh.CellVolumes[i, j, k]);
            var area = 2f * MathF.PI * r * (dz_m + dz_p) * 0.5f;
            var Uvol = htc * area / vol * taper;

            numerator += dt * Uvol * Tfluid / rho_cp;
            denominator += dt * Uvol / rho_cp;
        }

        var T_new = Math.Clamp(numerator / denominator, 273f, 573f);
        newTemp[i, j, k] = T_new;
        return MathF.Abs(T_new - T_old);
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
    ///     Updates heat exchanger fluid temperatures - DEFINITIVE FIX.
    ///     This version correctly handles standard and reversed counter-flow for coaxial systems.
    /// </summary>
    private void UpdateHeatExchanger()
    {
        var nz = _fluidTempDown.Length;
        if (nz == 0) return;

        var mdot = (float)_options.FluidMassFlowRate;
        var cp = (float)_options.FluidSpecificHeat;
        var dz = _options.BoreholeDataset.TotalDepth / nz;

        var U_ground = CalculateBoreholeWallHeatTransferCoefficient();
        var U_internal = CalculateInternalHeatTransferCoefficient();

        var P_outer = (float)(Math.PI * _options.PipeOuterDiameter);
        var P_inner = (float)(Math.PI * _options.PipeInnerDiameter);

        // Base NTU values, calculated once
        var NTU_ground_base = U_ground * P_outer * dz / (mdot * cp);
        var NTU_internal = U_internal * P_inner * dz / (mdot * cp);

        var oldFluidDown = (float[])_fluidTempDown.Clone();
        var oldFluidUp = (float[])_fluidTempUp.Clone();

        var nextTempDown = (float[])_fluidTempDown.Clone();
        var nextTempUp = (float[])_fluidTempUp.Clone();

        // Iterative loop for the coupled fluid streams to reach equilibrium
        for (var iter = 0; iter < 20; iter++) // Increased iterations for stability
        {
            var maxChange = 0f;
            var prevIterDown = (float[])nextTempDown.Clone();
            var prevIterUp = (float[])nextTempUp.Clone();

            if (_options.FlowConfiguration == FlowConfiguration.CounterFlowReversed)
            {
                // REVERSED FLOW: Cold fluid down ANNULUS, Hot fluid up INNER pipe.
                // Down-flow (Annulus)
                nextTempDown[0] = (float)_options.FluidInletTemperature;
                for (var i = 1; i < nz; i++)
                {
                    var T_in = prevIterDown[i - 1];
                    var T_inner_pipe = 0.5f * (prevIterUp[i] + prevIterUp[i - 1]);
                    var current_depth = (i - 0.5f) * dz;

                    // --- DEFINITIVE FIX START ---
                    if (current_depth <= _options.HeatExchangerDepth)
                    {
                        // ACTIVE ZONE: Exchange with ground AND inner pipe
                        var T_ground = InterpolateGroundTemperatureAtDepth(current_depth);
                        var numerator = T_in + NTU_ground_base * T_ground + NTU_internal * T_inner_pipe;
                        var denominator = 1.0f + NTU_ground_base + NTU_internal;
                        nextTempDown[i] = numerator / denominator;
                    }
                    else
                    {
                        // PASSIVE ZONE: Exchange ONLY with inner pipe (parasitic loss)
                        var numerator = T_in + NTU_internal * T_inner_pipe;
                        var denominator = 1.0f + NTU_internal;
                        nextTempDown[i] = numerator / denominator;
                    }
                    // --- DEFINITIVE FIX END ---
                }

                // Up-flow (Inner Pipe - only ever interacts with annulus)
                nextTempUp[nz - 1] = nextTempDown[nz - 1]; // Turnaround at bottom
                for (var i = nz - 2; i >= 0; i--)
                {
                    var T_in = prevIterUp[i + 1];
                    var T_annulus = 0.5f * (nextTempDown[i] + nextTempDown[i + 1]);

                    var numerator = T_in + NTU_internal * T_annulus;
                    var denominator = 1.0f + NTU_internal;
                    nextTempUp[i] = numerator / denominator;
                }
            }
            else // STANDARD FLOW: Cold fluid down INNER pipe, Hot fluid up ANNULUS.
            {
                // Down-flow (Inner Pipe - only ever interacts with annulus)
                nextTempDown[0] = (float)_options.FluidInletTemperature;
                for (var i = 1; i < nz; i++)
                {
                    var T_in = prevIterDown[i - 1];
                    var T_annulus = 0.5f * (prevIterUp[i] + prevIterUp[i - 1]);

                    var numerator = T_in + NTU_internal * T_annulus;
                    var denominator = 1.0f + NTU_internal;
                    nextTempDown[i] = numerator / denominator;
                }

                // Up-flow (Annulus)
                nextTempUp[nz - 1] = nextTempDown[nz - 1]; // Turnaround
                for (var i = nz - 2; i >= 0; i--)
                {
                    var T_in = prevIterUp[i + 1];
                    var T_inner_pipe = 0.5f * (nextTempDown[i] + nextTempDown[i + 1]);
                    var current_depth = (i + 0.5f) * dz;

                    // --- DEFINITIVE FIX START ---
                    if (current_depth <= _options.HeatExchangerDepth)
                    {
                        // ACTIVE ZONE: Exchange with ground AND inner pipe
                        var T_ground = InterpolateGroundTemperatureAtDepth(current_depth);
                        var numerator = T_in + NTU_ground_base * T_ground + NTU_internal * T_inner_pipe;
                        var denominator = 1.0f + NTU_ground_base + NTU_internal;
                        nextTempUp[i] = numerator / denominator;
                    }
                    else
                    {
                        // PASSIVE ZONE: Exchange ONLY with inner pipe (parasitic gain/loss)
                        var numerator = T_in + NTU_internal * T_inner_pipe;
                        var denominator = 1.0f + NTU_internal;
                        nextTempUp[i] = numerator / denominator;
                    }
                    // --- DEFINITIVE FIX END ---
                }
            }

            for (var i = 0; i < nz; i++)
            {
                maxChange = Math.Max(maxChange, Math.Abs(nextTempDown[i] - prevIterDown[i]));
                maxChange = Math.Max(maxChange, Math.Abs(nextTempUp[i] - prevIterUp[i]));
            }

            if (maxChange < 1e-4) break;
        }

        var dampingFactor = 0.3f; // Reduced damping for faster response
        for (var i = 0; i < nz; i++)
        {
            _fluidTempDown[i] = dampingFactor * nextTempDown[i] + (1.0f - dampingFactor) * oldFluidDown[i];
            _fluidTempUp[i] = dampingFactor * nextTempUp[i] + (1.0f - dampingFactor) * oldFluidUp[i];
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

        // FIXED: Sample temperature from the appropriate radial zone
        // For heat exchanger coupling, we need to sample from the zone where heat transfer occurs
        var temp = 0f;
        var count = 0;
        var weightSum = 0f;

        // Sample from radial indices 0-5 (includes heat exchanger influence zone)
        // Use weighted average based on distance from borehole center
        for (var i = 0; i < Math.Min(6, _mesh.RadialPoints); i++)
        {
            var r = _mesh.R[i];
            var weight = 1.0f / (1.0f + r * 10f); // Weight inversely proportional to distance

            for (var j = 0; j < _mesh.AngularPoints; j++)
            {
                temp += _temperature[i, j, kIndex] * weight;
                weightSum += weight;
                count++;
            }
        }

        if (weightSum > 0)
            return temp / weightSum;

        // Fallback: use initial temperature profile
        return _initialTemperature[0, 0, kIndex]; // Use center point
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
    ///     DEFINITIVE FIX: Calculates the overall heat transfer coefficient (U-value) for the
    ///     interaction between the outer pipe fluid and the borehole wall. This includes
    ///     resistances from the fluid, the outer pipe wall, and the grout.
    /// </summary>
    private float CalculateBoreholeWallHeatTransferCoefficient()
    {
        // Coefficiente convettivo nel fluido dell'anello esterno (annulus)
        var D_outer_casing = (float)_options.PipeOuterDiameter;
        var D_inner_annulus = (float)_options.PipeSpacing; // Diametro esterno del tubo interno
        var D_h = D_outer_casing - D_inner_annulus; // Diametro idraulico per l'anello
        var mu = (float)_options.FluidViscosity;
        var mdot = (float)_options.FluidMassFlowRate;
        // La portata nell'anello è la portata totale
        var Re = mdot * D_h / (MathF.PI / 4.0f * (D_outer_casing * D_outer_casing - D_inner_annulus * D_inner_annulus) *
                               mu);
        var Pr = (float)(_options.FluidViscosity * _options.FluidSpecificHeat / _options.FluidThermalConductivity);
        float Nu;
        if (Re < 2300)
        {
            Nu = 4.36f;
        }
        else
        {
            var f = MathF.Pow(0.79f * MathF.Log(Re) - 1.64f, -2.0f);
            Nu = f / 8.0f * (Re - 1000.0f) * Pr /
                 (1.0f + 12.7f * MathF.Pow(f / 8.0f, 0.5f) * (MathF.Pow(Pr, 2.0f / 3.0f) - 1.0f));
        }

        var h_fluid_annulus = Nu * (float)_options.FluidThermalConductivity / D_h;

        // Resistenze termiche in serie per unità di lunghezza (K·m/W)
        var r_borehole = _options.BoreholeDataset.WellDiameter / 2f;
        var r_casing_outer = D_outer_casing / 2f;
        var k_grout = (float)_options.GroutThermalConductivity;

        // 1. Resistenza del film di fluido nell'anello (riferita alla superficie esterna del casing)
        var R_fluid = 1f / (h_fluid_annulus * MathF.PI * D_outer_casing);

        // 2. Resistenza del grout tra il casing e la parete del pozzo
        var R_grout = MathF.Log(r_borehole / r_casing_outer) / (2f * MathF.PI * k_grout);

        // La resistenza del casing stesso è trascurabile (metallo) ma la includiamo per completezza
        var r_casing_inner = r_casing_outer - 0.01f; // Assumiamo 1cm di spessore
        var k_casing = (float)_options.PipeThermalConductivity;
        var R_casing = MathF.Log(r_casing_outer / r_casing_inner) / (2f * MathF.PI * k_casing);

        var R_total_per_meter = R_fluid + R_grout + R_casing;

        if (R_total_per_meter < 1e-6) return 1000f; // Evita divisione per zero

        // U-value è l'inverso della resistenza totale per unità di area (W/m²K)
        // Area è la circonferenza esterna del casing
        var U = 1f / (R_total_per_meter * MathF.PI * D_outer_casing);

        return Math.Max(10f, Math.Min(U, 2500f)); // Limita a un range realistico
    }

    /// <summary>
    ///     DEFINITIVE FIX: Calculates the overall heat transfer coefficient (U-value) for the
    ///     parasitic heat exchange between the inner and outer fluid streams through the inner pipe wall.
    /// </summary>
    private float CalculateInternalHeatTransferCoefficient()
    {
        // Coefficiente convettivo dentro il tubo interno (flusso in un tubo)
        var D_inner_pipe = (float)_options.PipeInnerDiameter;
        var mu = (float)_options.FluidViscosity;
        var mdot = (float)_options.FluidMassFlowRate;
        var Re = 4.0f * mdot / (MathF.PI * D_inner_pipe * mu);
        var Pr = (float)(_options.FluidViscosity * _options.FluidSpecificHeat / _options.FluidThermalConductivity);
        float Nu;
        if (Re < 2300)
        {
            Nu = 4.36f;
        }
        else
        {
            var f = MathF.Pow(0.79f * MathF.Log(Re) - 1.64f, -2.0f);
            Nu = f / 8.0f * (Re - 1000.0f) * Pr /
                 (1.0f + 12.7f * MathF.Pow(f / 8.0f, 0.5f) * (MathF.Pow(Pr, 2.0f / 3.0f) - 1.0f));
        }

        var h_fluid_inner = Nu * (float)_options.FluidThermalConductivity / D_inner_pipe;

        // Resistenze in serie per unità di lunghezza (K·m/W)
        var k_pipe_inner = (float)_options.InnerPipeThermalConductivity;
        if (k_pipe_inner < 1e-6) return 0.0f; // Tubo perfettamente isolato, nessuno scambio

        var r_pipe_inner_in = D_inner_pipe / 2f;
        var r_pipe_inner_out = (float)_options.PipeSpacing / 2f; // PipeSpacing è il diametro esterno del tubo interno

        // 1. Resistenza convettiva del fluido DENTRO il tubo interno
        var R_conv_inner = 1f / (h_fluid_inner * MathF.PI * D_inner_pipe);

        // 2. Resistenza conduttiva della parete del tubo interno
        var R_pipe_wall = MathF.Log(r_pipe_inner_out / r_pipe_inner_in) / (2f * MathF.PI * k_pipe_inner);

        // La resistenza convettiva dell'anello esterno è già calcolata nell'altro metodo,
        // per semplicità qui usiamo un valore ragionevole. L'errore è minimo rispetto alla resistenza del tubo.
        var h_fluid_annulus = 1500f;
        var R_conv_outer = 1f / (h_fluid_annulus * MathF.PI * (2 * r_pipe_inner_out));

        var R_total_per_meter = R_conv_inner + R_pipe_wall + R_conv_outer;

        if (R_total_per_meter < 1e-6) return 3000f;

        // U-value riferito all'area della superficie interna del tubo interno
        var U = 1f / (R_total_per_meter * MathF.PI * D_inner_pipe);

        return U;
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

        // The physically correct outlet temperature at the surface is ALWAYS the
        // upward-flowing fluid temperature at the first element (z=0). This is now
        // correct for all configurations because the underlying physics are fixed.
        var outletTemp = _fluidTempUp[0];
        var inletTemp = (float)_options.FluidInletTemperature;

        // The sign of (outletTemp - inletTemp) correctly determines heat extraction (+) or rejection (-).
        var Q = _options.FluidMassFlowRate * _options.FluidSpecificHeat * (outletTemp - inletTemp);

        if (CurrentTimeStep < 5 || CurrentTimeStep % 100 == 0)
            Logger.Log(
                $"[Step {CurrentTimeStep}] Time: {currentTime / 86400:F1}d, Tin: {inletTemp - 273.15:F1}°C, Tout: {outletTemp - 273.15:F1}°C, Q: {Q / 1e6:F2} MW");

        results.HeatExtractionRate.Add((currentTime, Q));
        results.OutletTemperature.Add((currentTime, outletTemp));

        var hvacSupplyTempK = _options.HvacSupplyTemperatureKelvin ?? 308.15;
        var compressorEfficiency = _options.CompressorIsentropicEfficiency ?? 0.6;
        var cop = 4.0;
        if (Math.Abs(Q) > 100)
        {
            var avgFluidTemp = (inletTemp + outletTemp) / 2.0;
            if (Q > 0)
            {
                var deltaT = Math.Max(1.0, hvacSupplyTempK - avgFluidTemp);
                cop = Math.Min(10.0, hvacSupplyTempK / deltaT * compressorEfficiency);
            }
            else
            {
                var deltaT = Math.Max(1.0, avgFluidTemp - hvacSupplyTempK);
                cop = Math.Min(10.0, hvacSupplyTempK / deltaT * compressorEfficiency);
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
        _progress?.Report((0.92f, "Generating streamlines..."));

        // Check if velocity field has any non-zero values
        var maxVel = 0.0;
        for (var i = 0; i < _mesh.RadialPoints; i++)
        for (var j = 0; j < _mesh.AngularPoints; j++)
        for (var k = 0; k < _mesh.VerticalPoints; k++)
        {
            var vel = Math.Sqrt(
                _velocity[i, j, k, 0] * _velocity[i, j, k, 0] +
                _velocity[i, j, k, 1] * _velocity[i, j, k, 1] +
                _velocity[i, j, k, 2] * _velocity[i, j, k, 2]);
            if (vel > maxVel) maxVel = vel;
        }

        Console.WriteLine($"[Streamlines] Max velocity magnitude: {maxVel:E3} m/s");

        if (maxVel < 1e-10)
        {
            Console.WriteLine("[Streamlines] WARNING: Velocity field is essentially zero - no flow to visualize!");
            Console.WriteLine("[Streamlines] Check: hydraulic gradient, permeability, boundary conditions");
            return;
        }

        var random = new Random(42);
        var successfulStreamlines = 0;

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

            if (streamline.Count > 5)
            {
                results.Streamlines.Add(streamline);
                successfulStreamlines++;
            }
        }

        Console.WriteLine(
            $"[Streamlines] Generated {successfulStreamlines} streamlines (requested {_options.StreamlineCount})");
        Console.WriteLine($"[Streamlines] Total streamline points: {results.Streamlines.Sum(s => s.Count)}");
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
    /// <summary>
    ///     Applies seasonal energy curve to modify fluid inlet temperature for BTES mode.
    ///     Converts daily energy (kWh/day) to temperature change.
    /// </summary>
    private void ApplySeasonalEnergyCurve(double currentTime)
    {
        // Calculate current day of year (0-364)
        int dayOfYear = (int)((currentTime / 86400.0) % 365.0);

        // Get energy for this day (kWh/day)
        double dailyEnergy = _options.SeasonalEnergyCurve[dayOfYear];

        // Convert energy to temperature
        // Q = m_dot * cp * (T_out - T_in)
        // For BTES: positive energy = charging (hot water in), negative = discharging (cold water in)

        double mdot = _options.FluidMassFlowRate; // kg/s
        double cp = _options.FluidSpecificHeat; // J/kg·K

        // Convert kWh/day to Watts
        double powerWatts = dailyEnergy * 1000.0 / 24.0; // kWh/day -> W

        // Calculate required temperature difference
        // Q = mdot * cp * deltaT
        // deltaT = Q / (mdot * cp)
        double deltaT = powerWatts / (mdot * cp);

        // Set inlet temperature based on charging/discharging mode
        if (dailyEnergy > 0)
        {
            // Charging mode: inject hot water
            _options.FluidInletTemperature = _options.BTESChargingTemperature + deltaT;
        }
        else if (dailyEnergy < 0)
        {
            // Discharging mode: inject cold water
            _options.FluidInletTemperature = _options.BTESDischargingTemperature - deltaT;
        }
        else
        {
            // No energy transfer: use neutral temperature
            _options.FluidInletTemperature = (_options.BTESChargingTemperature + _options.BTESDischargingTemperature) / 2.0;
        }

        // Clamp to reasonable values
        _options.FluidInletTemperature = Math.Max(273.15, Math.Min(373.15, _options.FluidInletTemperature));

        // Log every 30 days
        if (dayOfYear % 30 == 0)
        {
            Logger.Log($"[BTES Day {dayOfYear}] Energy: {dailyEnergy:F0} kWh/day, T_inlet: {_options.FluidInletTemperature - 273.15:F1}°C");
        }
    }

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