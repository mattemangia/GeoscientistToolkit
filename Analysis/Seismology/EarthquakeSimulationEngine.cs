using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace GeoscientistToolkit.Analysis.Seismology
{
    /// <summary>
    /// Main earthquake simulation engine integrating all components
    /// Parallelized spectral element method inspired by SpecFEM
    /// </summary>
    public class EarthquakeSimulationEngine
    {
        private readonly EarthquakeSimulationParameters _params;
        private CrustalModel? _crustalModel;
        private WavePropagationEngine? _waveEngine;
        private DamageMapper? _damageMapper;
        private FractureAnalyzer? _fractureAnalyzer;

        // Progress reporting
        public event EventHandler<SimulationProgressEventArgs>? ProgressChanged;

        public EarthquakeSimulationEngine(EarthquakeSimulationParameters parameters)
        {
            _params = parameters;
        }

        /// <summary>
        /// Initialize all simulation components
        /// </summary>
        public void Initialize()
        {
            ReportProgress(0, "Initializing simulation...");

            // Validate parameters
            if (!_params.Validate(out string error))
            {
                throw new ArgumentException($"Invalid parameters: {error}");
            }

            // Load crustal model
            ReportProgress(5, "Loading crustal model...");
            _crustalModel = CrustalModel.LoadFromFile(_params.CrustalModelPath);

            // Initialize wave propagation engine
            ReportProgress(10, "Initializing wave propagation engine...");
            _waveEngine = new WavePropagationEngine(
                _crustalModel,
                _params.GridNX,
                _params.GridNY,
                _params.GridNZ,
                _params.GridDX,
                _params.GridDY,
                _params.GridDZ,
                _params.TimeStepSeconds
            );

            _waveEngine.InitializeMaterialProperties(
                _params.MinLatitude,
                _params.MaxLatitude,
                _params.MinLongitude,
                _params.MaxLongitude
            );

            // Initialize damage mapper
            if (_params.CalculateDamage)
            {
                ReportProgress(15, "Initializing damage mapper...");
                _damageMapper = new DamageMapper(_params.GridNX, _params.GridNY);
            }

            // Initialize fracture analyzer
            if (_params.CalculateFractures)
            {
                ReportProgress(20, "Initializing fracture analyzer...");
                _fractureAnalyzer = new FractureAnalyzer(
                    _params.GridNX,
                    _params.GridNY,
                    _params.GridNZ,
                    _params.GridDX,
                    _params.GridDY,
                    _params.GridDZ
                );

                _fractureAnalyzer.InitializeMaterialProperties(
                    _crustalModel,
                    _params.MinLatitude,
                    _params.MaxLatitude,
                    _params.MinLongitude,
                    _params.MaxLongitude
                );
            }

            ReportProgress(25, "Initialization complete");
        }

        /// <summary>
        /// Run the complete earthquake simulation
        /// </summary>
        public EarthquakeSimulationResults Run()
        {
            if (_waveEngine == null || _crustalModel == null)
            {
                throw new InvalidOperationException("Simulation not initialized. Call Initialize() first.");
            }

            var stopwatch = Stopwatch.StartNew();
            var results = new EarthquakeSimulationResults
            {
                // Copy source parameters
                EpicenterLatitude = _params.EpicenterLatitude,
                EpicenterLongitude = _params.EpicenterLongitude,
                HypocenterDepthKm = _params.HypocenterDepthKm,
                MomentMagnitude = _params.MomentMagnitude,
                StrikeDegrees = _params.StrikeDegrees,
                DipDegrees = _params.DipDegrees,
                RakeDegrees = _params.RakeDegrees,

                // Domain info
                MinLatitude = _params.MinLatitude,
                MaxLatitude = _params.MaxLatitude,
                MinLongitude = _params.MinLongitude,
                MaxLongitude = _params.MaxLongitude,
                GridNX = _params.GridNX,
                GridNY = _params.GridNY,
                GridNZ = _params.GridNZ,

                SimulationDurationSeconds = _params.SimulationDurationSeconds,

                // Initialize result arrays
                PeakGroundAcceleration = new double[_params.GridNX, _params.GridNY],
                PeakGroundVelocity = new double[_params.GridNX, _params.GridNY],
                PeakGroundDisplacement = new double[_params.GridNX, _params.GridNY],
                WaveSnapshots = _params.SaveWaveSnapshots ? new List<WaveSnapshot>() : null
            };

            // Calculate wave arrival times and amplitudes
            if (_params.TrackWaveTypes)
            {
                ReportProgress(30, "Calculating wave arrivals...");
                CalculateWaveArrivals(results);
            }

            // Add earthquake source
            ReportProgress(35, "Adding earthquake source...");
            AddEarthquakeSource();

            // Time integration loop
            int totalSteps = (int)(_params.SimulationDurationSeconds / _params.TimeStepSeconds);
            results.TotalTimeSteps = totalSteps;

            ReportProgress(40, $"Starting time integration ({totalSteps} steps)...");

            for (int step = 0; step < totalSteps; step++)
            {
                // Perform one time step
                _waveEngine.TimeStep();

                // Update damage mapper
                if (_damageMapper != null && _params.CalculateDamage)
                {
                    _damageMapper.UpdateGroundMotion(_waveEngine, _params.TimeStepSeconds);
                }

                // Update fracture analyzer
                if (_fractureAnalyzer != null && _params.CalculateFractures && step % 10 == 0)
                {
                    _fractureAnalyzer.UpdateStressFromWaves(
                        _waveEngine,
                        _crustalModel,
                        _params.MinLatitude,
                        _params.MaxLatitude,
                        _params.MinLongitude,
                        _params.MaxLongitude
                    );

                    _fractureAnalyzer.CheckFractureInitiation();
                }

                // Save snapshots
                if (_params.SaveWaveSnapshots && step % _params.SnapshotIntervalSteps == 0)
                {
                    var snapshot = new WaveSnapshot
                    {
                        TimeSeconds = step * _params.TimeStepSeconds,
                        SurfaceDisplacement = _waveEngine.GetSurfaceSnapshot()
                    };
                    results.WaveSnapshots?.Add(snapshot);
                }

                // Report progress
                if (step % 100 == 0)
                {
                    double progress = 40 + (step / (double)totalSteps) * 40;
                    ReportProgress((int)progress, $"Time step {step}/{totalSteps}");
                }
            }

            ReportProgress(80, "Processing results...");

            // Extract peak ground motion from damage mapper
            if (_damageMapper != null)
            {
                results.PeakGroundAcceleration = _damageMapper.GetPGAMap();
                results.PeakGroundVelocity = _damageMapper.GetPGVMap();
                results.PeakGroundDisplacement = _damageMapper.GetPGDMap();
            }

            // Calculate damage
            if (_damageMapper != null && _params.CalculateDamage)
            {
                ReportProgress(85, "Calculating structural damage...");
                results.DamageMap = _damageMapper.GenerateDamageMap(
                    _params.MinLatitude,
                    _params.MaxLatitude,
                    _params.MinLongitude,
                    _params.MaxLongitude,
                    _params.BuildingVulnerability
                );

                // Create damage ratio and MMI maps
                results.DamageRatioMap = new double[_params.GridNX, _params.GridNY];
                results.MMIMap = new int[_params.GridNX, _params.GridNY];

                for (int i = 0; i < _params.GridNX; i++)
                {
                    for (int j = 0; j < _params.GridNY; j++)
                    {
                        results.DamageRatioMap[i, j] = results.DamageMap[i, j].DamageRatio;
                        results.MMIMap[i, j] = _damageMapper.CalculateMMI(
                            results.DamageMap[i, j].PeakGroundAcceleration,
                            results.DamageMap[i, j].PeakGroundVelocity
                        );
                    }
                }
            }

            // Extract fracture results
            if (_fractureAnalyzer != null && _params.CalculateFractures)
            {
                ReportProgress(90, "Analyzing fractures...");
                results.FractureDensityMap = _fractureAnalyzer.GetFractureDensityMap();

                // Calculate surface Coulomb stress
                results.CoulombStressMap = new double[_params.GridNX, _params.GridNY];
                for (int i = 0; i < _params.GridNX; i++)
                {
                    for (int j = 0; j < _params.GridNY; j++)
                    {
                        results.CoulombStressMap[i, j] = _fractureAnalyzer.CalculateCoulombStress(
                            i, j, 0,
                            _params.StrikeRadians,
                            _params.DipRadians,
                            _params.RakeRadians
                        );
                    }
                }
            }

            // Calculate statistics
            ReportProgress(95, "Calculating statistics...");
            results.CalculateStatistics();

            stopwatch.Stop();
            results.ComputationTimeSeconds = stopwatch.Elapsed.TotalSeconds;

            ReportProgress(100, "Simulation complete!");

            return results;
        }

        /// <summary>
        /// Add earthquake point source to wave engine
        /// </summary>
        private void AddEarthquakeSource()
        {
            if (_waveEngine == null) return;

            // Convert epicenter to grid coordinates
            double latFrac = (_params.EpicenterLatitude - _params.MinLatitude) /
                           (_params.MaxLatitude - _params.MinLatitude);
            double lonFrac = (_params.EpicenterLongitude - _params.MinLongitude) /
                           (_params.MaxLongitude - _params.MinLongitude);
            double depthFrac = _params.HypocenterDepthKm / _params.MaxDepthKm;

            int ix = (int)(latFrac * (_params.GridNX - 1));
            int iy = (int)(lonFrac * (_params.GridNY - 1));
            int iz = (int)(depthFrac * (_params.GridNZ - 1));

            // Ensure indices are within bounds
            ix = Math.Clamp(ix, 0, _params.GridNX - 1);
            iy = Math.Clamp(iy, 0, _params.GridNY - 1);
            iz = Math.Clamp(iz, 0, _params.GridNZ - 1);

            _waveEngine.AddPointSource(
                ix, iy, iz,
                _params.MomentMagnitude,
                _params.StrikeRadians,
                _params.DipRadians,
                _params.RakeRadians
            );
        }

        /// <summary>
        /// Calculate wave arrival times for all grid points
        /// </summary>
        private void CalculateWaveArrivals(EarthquakeSimulationResults results)
        {
            if (_waveEngine == null) return;

            results.PWaveArrivalTime = new double[_params.GridNX, _params.GridNY];
            results.SWaveArrivalTime = new double[_params.GridNX, _params.GridNY];
            results.LoveWaveArrivalTime = new double[_params.GridNX, _params.GridNY];
            results.RayleighWaveArrivalTime = new double[_params.GridNX, _params.GridNY];

            results.PWaveAmplitude = new double[_params.GridNX, _params.GridNY];
            results.SWaveAmplitude = new double[_params.GridNX, _params.GridNY];
            results.LoveWaveAmplitude = new double[_params.GridNX, _params.GridNY];
            results.RayleighWaveAmplitude = new double[_params.GridNX, _params.GridNY];

            double frequency = 1.0; // 1 Hz dominant frequency

            Parallel.For(0, _params.GridNX, i =>
            {
                double lat = _params.MinLatitude + (i / (double)(_params.GridNX - 1)) *
                           (_params.MaxLatitude - _params.MinLatitude);

                for (int j = 0; j < _params.GridNY; j++)
                {
                    double lon = _params.MinLongitude + (j / (double)(_params.GridNY - 1)) *
                               (_params.MaxLongitude - _params.MinLongitude);

                    // Calculate arrival times
                    var (pTime, sTime, loveTime, rayleighTime) = _waveEngine.CalculateArrivalTimes(
                        _params.EpicenterLatitude,
                        _params.EpicenterLongitude,
                        _params.HypocenterDepthKm,
                        lat, lon
                    );

                    results.PWaveArrivalTime[i, j] = pTime;
                    results.SWaveArrivalTime[i, j] = sTime;
                    results.LoveWaveArrivalTime[i, j] = loveTime;
                    results.RayleighWaveArrivalTime[i, j] = rayleighTime;

                    // Calculate distance
                    double distance = CalculateDistance(
                        _params.EpicenterLatitude, _params.EpicenterLongitude,
                        lat, lon
                    );

                    // Calculate amplitudes
                    results.PWaveAmplitude[i, j] = _waveEngine.CalculateSurfaceWaveAmplitude(
                        WaveType.P, distance, _params.MomentMagnitude, frequency);

                    results.SWaveAmplitude[i, j] = _waveEngine.CalculateSurfaceWaveAmplitude(
                        WaveType.S, distance, _params.MomentMagnitude, frequency);

                    results.LoveWaveAmplitude[i, j] = _waveEngine.CalculateSurfaceWaveAmplitude(
                        WaveType.Love, distance, _params.MomentMagnitude, frequency);

                    results.RayleighWaveAmplitude[i, j] = _waveEngine.CalculateSurfaceWaveAmplitude(
                        WaveType.Rayleigh, distance, _params.MomentMagnitude, frequency);
                }
            });
        }

        /// <summary>
        /// Calculate great circle distance
        /// </summary>
        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            double lat1Rad = lat1 * Math.PI / 180.0;
            double lon1Rad = lon1 * Math.PI / 180.0;
            double lat2Rad = lat2 * Math.PI / 180.0;
            double lon2Rad = lon2 * Math.PI / 180.0;

            double dlat = lat2Rad - lat1Rad;
            double dlon = lon2Rad - lon1Rad;

            double a = Math.Sin(dlat / 2) * Math.Sin(dlat / 2) +
                      Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                      Math.Sin(dlon / 2) * Math.Sin(dlon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return 6371.0 * c; // Earth radius in km
        }

        /// <summary>
        /// Report progress
        /// </summary>
        private void ReportProgress(int percentage, string message)
        {
            ProgressChanged?.Invoke(this, new SimulationProgressEventArgs
            {
                Percentage = percentage,
                Message = message
            });
        }
    }

    /// <summary>
    /// Event args for simulation progress
    /// </summary>
    public class SimulationProgressEventArgs : EventArgs
    {
        public int Percentage { get; set; }
        public string Message { get; set; } = "";
    }
}
