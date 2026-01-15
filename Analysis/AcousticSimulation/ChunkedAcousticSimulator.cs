// GeoscientistToolkit/Analysis/AcousticSimulation/ChunkedAcousticSimulator.cs

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

/// <summary>
///     Main acoustic wave propagation simulator with chunked processing and GPU/CPU support.
/// </summary>
public class ChunkedAcousticSimulator : IDisposable
{
    private const long HUGE_DATASET_THRESHOLD_GB = 8;
    private const int MAX_LOADED_CHUNKS_SMALL = 999; // All chunks for small datasets
    private const float ARRIVAL_THRESHOLD = 0.05f;
    private readonly HashSet<int> _chunkAccessSet = new();
    private readonly List<WaveFieldChunk> _chunks = new();
    private readonly string _offloadPath;
    private readonly SimulationParameters _params;
    private readonly float _visualizationUpdateInterval = 0.1f;
    private long _availableSystemRamBytes;
    private float _baselineAmplitude;
    private Queue<int> _chunkAccessOrder = new(); // LRU tracking

    private long _currentMemoryUsageBytes;

    // Progress tracking
    private float[,,] _damageField;
    private bool _enableRealTimeVisualization;

    // Adaptive memory management
    private bool _isHugeDataset;

    // Simulation state tracking

    private IAcousticKernel _kernel;
    private DateTime _lastVisualizationUpdate = DateTime.MinValue;
    private byte[,,] _materialLabels;
    private int _maxLoadedChunks;
    private long _maxMemoryBudgetBytes;
    private float[,,] _maxPWaveMagnitude;
    private float[,,] _maxSWaveMagnitude;

    // Max velocity tracking (for final export)
    private float[,,] _maxVelocityMagnitude;
    private float[,,] _persistentPoissonRatio;
    private float[,,] _persistentYoungsModulus;
    private Vector3 _propagationDirection;

    // P/S wave detection
    private int _pWaveArrivalTime = -1;
    private Stopwatch _stopwatch;
    private int _sWaveArrivalTime = -1;

    // Time series storage
    private List<WaveFieldSnapshot> _timeSeriesSnapshots;

    public ChunkedAcousticSimulator(SimulationParameters parameters)
    {
        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));

        if (_params.EnableOffloading && !string.IsNullOrEmpty(_params.OffloadDirectory))
        {
            _offloadPath = Path.Combine(_params.OffloadDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(_offloadPath);
        }

        DetectSystemMemory();
        CalculateAdaptiveTimeStep();
    }

    // Public accessors for exporter
    public float[,,] DensityVolume { get; private set; }

    public float[,,] YoungsModulusVolume { get; private set; }

    public float[,,] PoissonRatioVolume { get; private set; }
    public float Progress => _params.TimeSteps > 0 ? (float)CurrentStep / _params.TimeSteps : 0f;
    public int CurrentStep { get; private set; }
    public float CurrentMemoryUsageMB { get; private set; }
    public bool IsSimulating { get; private set; }

    public void Dispose()
    {
        Cleanup();
    }

    public event EventHandler<SimulationProgressEventArgs> ProgressUpdated;
    public event EventHandler<WaveFieldUpdateEventArgs> WaveFieldUpdated;

    private void DetectSystemMemory()
    {
        try
        {
            // Get total physical memory
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            var totalPhysicalMemory = gcMemoryInfo.TotalAvailableMemoryBytes;

            // If GC doesn't provide info, estimate conservatively
            if (totalPhysicalMemory <= 0)
            {
                totalPhysicalMemory = 16L * 1024 * 1024 * 1024; // Assume 16GB
                Logger.LogWarning("[Simulator] Could not detect system RAM, assuming 16GB");
            }

            _availableSystemRamBytes = totalPhysicalMemory;

            // Use 75% of available RAM for simulation (leave room for OS and other processes)
            _maxMemoryBudgetBytes = (long)(totalPhysicalMemory * 0.75);

            Logger.Log($"[Simulator] System RAM detected: {totalPhysicalMemory / (1024.0 * 1024 * 1024):F2} GB");
            Logger.Log(
                $"[Simulator] Memory budget for simulation: {_maxMemoryBudgetBytes / (1024.0 * 1024 * 1024):F2} GB");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                $"[Simulator] Failed to detect system memory: {ex.Message}, using conservative 12GB budget");
            _maxMemoryBudgetBytes = 12L * 1024 * 1024 * 1024;
        }
    }

    public void SetPerVoxelMaterialProperties(float[,,] youngsModulus, float[,,] poissonRatio)
    {
        YoungsModulusVolume = youngsModulus;
        PoissonRatioVolume = poissonRatio;

        // FIX: Create persistent copies for damage modifications
        _persistentYoungsModulus = (float[,,])youngsModulus.Clone();
        _persistentPoissonRatio = (float[,,])poissonRatio.Clone();
    }

    public float[,,] GetDamageField()
    {
        return _damageField;
    }

    public async Task<SimulationResults> RunAsync(byte[,,] labels, float[,,] density, CancellationToken ct)
    {
        _materialLabels = labels;
        DensityVolume = density;
        _stopwatch = Stopwatch.StartNew();
        CurrentStep = 0;
        IsSimulating = true;

        try
        {
            InitializeKernel();
            InitializeChunks();
            InitializeFields();
            ApplyInitialSource();

            for (CurrentStep = 0; CurrentStep < _params.TimeSteps && !ct.IsCancellationRequested; CurrentStep++)
            {
                await ProcessTimeStepAsync(ct);

                if (CurrentStep % 10 == 0)
                {
                    ReportProgress();
                    if (CurrentStep % 100 == 0)
                    {
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                        UpdateMemoryUsage();
                    }
                }

                if (_params.SaveTimeSeries && CurrentStep % _params.SnapshotInterval == 0)
                    SaveSnapshot(CurrentStep);

                DetectWaveArrivals(CurrentStep);
            }

            _stopwatch.Stop();
            return await AssembleResultsAsync(ct);
        }
        finally
        {
            IsSimulating = false;
            Cleanup();
        }
    }

    private void CalculateAdaptiveTimeStep()
    {
        // Estimate maximum P-wave velocity
        var maxVp = 6000f;
        if (_params.YoungsModulusMPa > 0 && _params.PoissonRatio > 0)
        {
            var E = _params.YoungsModulusMPa * 1e6f;
            var nu = _params.PoissonRatio;
            var rho = 2700f;
            var mu = E / (2f * (1f + nu));
            var lambda = E * nu / ((1f + nu) * (1f - 2f * nu));
            maxVp = MathF.Sqrt((lambda + 2f * mu) / rho);
        }

        var h = _params.PixelSize; // meters
        var h_um = h * 1e6f; // microns for logging

        // ADAPTIVE CFL FACTOR based on pixel size
        // Smaller pixels = more aggressive (need fewer steps)
        // Larger pixels = more conservative (can afford more steps)
        float cflFactor;
        string regime;

        if (h < 5e-6f) // < 5 microns - MICRO-CT regime
        {
            cflFactor = 0.95f;
            regime = "Ultra-fine (micro-CT)";
        }
        else if (h < 20e-6f) // < 20 microns
        {
            cflFactor = 0.90f;
            regime = "Very fine";
        }
        else if (h < 50e-6f) // < 50 microns
        {
            cflFactor = 0.85f;
            regime = "Fine";
        }
        else if (h < 200e-6f) // < 200 microns
        {
            cflFactor = 0.75f;
            regime = "Standard";
        }
        else if (h < 1e-3f) // < 1 mm
        {
            cflFactor = 0.65f;
            regime = "Coarse";
        }
        else if (h < 10e-3f) // < 10 mm
        {
            cflFactor = 0.50f;
            regime = "Very coarse";
        }
        else // >= 10 mm - Medical CT regime
        {
            cflFactor = 0.35f;
            regime = "Medical CT";
        }

        if (RuntimeInformation.IsOSPlatform(
                OSPlatform.OSX))
        {
            var originalCfl = cflFactor;
            cflFactor = Math.Min(cflFactor + 0.05f, 0.98f); // Boost by 0.05, max 0.98

            if (Math.Abs(cflFactor - originalCfl) > 0.001f)
            {
                Logger.Log($"[Simulator] Mac detected - CFL boosted from {originalCfl:F3} to {cflFactor:F3}");
                Logger.Log($"[Simulator] This reduces time steps by ~{(1.0f - originalCfl / cflFactor) * 100f:F1}%");
            }
        }

        var dtMax = cflFactor * h / (1.732f * maxVp);
        _params.TimeStepSeconds = dtMax;

        // Calculate simulation metrics
        var totalSimTime = _params.TimeSteps * dtMax;
        var expectedTravelTime = CalculateExpectedWaveTravelTime();
        var coverageRatio = totalSimTime / expectedTravelTime;

        Logger.Log("[Simulator] ═══════════════════════════════════════");
        Logger.Log("[Simulator] ADAPTIVE TIME STEP CALCULATION");
        Logger.Log("[Simulator] ───────────────────────────────────────");
        Logger.Log($"[Simulator] Pixel size: {h_um:F3} μm ({h * 1000f:F6} mm)");
        Logger.Log($"[Simulator] Resolution regime: {regime}");
        Logger.Log($"[Simulator] CFL safety factor: {cflFactor:F3}");
        Logger.Log($"[Simulator] Max P-wave velocity: {maxVp:F0} m/s");
        Logger.Log("[Simulator] ───────────────────────────────────────");
        Logger.Log($"[Simulator] Time step (dt): {dtMax * 1e9f:F3} ns");
        Logger.Log($"[Simulator] Total steps: {_params.TimeSteps:N0}");
        Logger.Log($"[Simulator] Total sim time: {totalSimTime * 1e6f:F3} μs");
        Logger.Log("[Simulator] ───────────────────────────────────────");
        Logger.Log($"[Simulator] Expected P-wave arrival: {expectedTravelTime * 1e6f:F3} μs");
        Logger.Log($"[Simulator] Coverage ratio: {coverageRatio:F2}x travel time");

        if (coverageRatio < 1.5f)
        {
            var recommendedSteps = (int)(expectedTravelTime * 2.5f / dtMax);
            Logger.LogWarning("[Simulator] Warning: LOW COVERAGE - waves may not reach receiver!");
            Logger.LogWarning($"[Simulator] Recommended: {recommendedSteps:N0} steps");
        }
        else if (coverageRatio > 10f)
        {
            var optimalSteps = (int)(expectedTravelTime * 3.0f / dtMax);
            Logger.LogWarning($"[Simulator] ℹ High coverage - could reduce to ~{optimalSteps:N0} steps");
        }
        else
        {
            Logger.Log("[Simulator] Coverage appears adequate");
        }

        Logger.Log("[Simulator] ═══════════════════════════════════════");
    }


    private void InitializeKernel()
    {
        if (_params.UseGPU)
        {
            try
            {
                _kernel = new AcousticSimulatorGPU(_params);
                Logger.Log("[Simulator] Using GPU acceleration");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Simulator] GPU init failed, falling back to CPU: {ex.Message}");
                _kernel = new AcousticSimulatorCPU(_params);
            }
        }
        else
        {
            _kernel = new AcousticSimulatorCPU(_params);
            Logger.Log("[Simulator] Using CPU implementation");
        }

        _kernel.Initialize(_params.Width, _params.Height, _params.Depth);
    }

    private void InitializeChunks()
    {
        // Calculate dataset size
        var totalVoxels = (long)_params.Width * _params.Height * _params.Depth;
        long bytesPerVoxel = sizeof(float) * 9; // vx, vy, vz, σxx, σyy, σzz, σxy, σxz, σyz
        var estimatedMemoryBytes = totalVoxels * bytesPerVoxel;
        var estimatedMemoryGB = estimatedMemoryBytes / (1024L * 1024L * 1024L);

        // Determine strategy based on dataset size vs available RAM
        var fitsInMemory = estimatedMemoryBytes < _maxMemoryBudgetBytes;
        _isHugeDataset = estimatedMemoryGB >= HUGE_DATASET_THRESHOLD_GB;

        Logger.Log($"[Simulator] Dataset size: {estimatedMemoryGB} GB ({totalVoxels:N0} voxels)");
        Logger.Log($"[Simulator] Available memory budget: {_maxMemoryBudgetBytes / (1024.0 * 1024 * 1024):F2} GB");
        Logger.Log($"[Simulator] Fits in memory: {fitsInMemory}, Huge dataset: {_isHugeDataset}");

        // Strategy selection
        if (fitsInMemory && !_isHugeDataset)
        {
            // FAST PATH: Entire dataset fits in RAM
            Logger.Log("[Simulator] FAST MODE: Entire dataset fits in RAM - no chunking or offloading");
            _chunks.Add(new WaveFieldChunk(0, _params.Depth, _params.Width, _params.Height));
            _maxLoadedChunks = MAX_LOADED_CHUNKS_SMALL;
            _params.UseChunkedProcessing = false;
            _params.EnableOffloading = false;
            return;
        }

        if (fitsInMemory && _isHugeDataset)
        {
            // MEDIUM PATH: Large but fits in RAM - chunk for cache efficiency but don't offload
            Logger.Log("[Simulator] MEDIUM MODE: Large dataset fits in RAM - chunking for cache but no offloading");
            _params.UseChunkedProcessing = true;
            _params.EnableOffloading = false;
            _maxLoadedChunks = MAX_LOADED_CHUNKS_SMALL;
        }
        else
        {
            // SLOW PATH: Doesn't fit in RAM - aggressive chunking and smart offloading
            Logger.Log(
                "[Simulator] MEMORY-CONSTRAINED MODE: Dataset exceeds RAM - aggressive chunking with LRU offloading");
            _params.UseChunkedProcessing = true;
            _params.EnableOffloading = true;

            // Calculate how many chunks we can keep in memory
            var chunkMemory = _params.Width * _params.Height * 32 * bytesPerVoxel; // 32 slices per chunk
            _maxLoadedChunks = Math.Max(3, (int)(_maxMemoryBudgetBytes / chunkMemory));
            Logger.Log($"[Simulator] Will keep max {_maxLoadedChunks} chunks in memory (LRU cache)");
        }

        // Calculate optimal chunk size
        var availableMemoryForChunks = fitsInMemory
            ? _maxMemoryBudgetBytes
            : _maxMemoryBudgetBytes / 2; // Leave room for other data

        var voxelsPerChunk = availableMemoryForChunks / (bytesPerVoxel * (_isHugeDataset ? 4 : 1));
        var slicesPerChunk = Math.Max(1, (int)(voxelsPerChunk / (_params.Width * _params.Height)));

        // For huge datasets, cap chunk size for better granularity
        if (_isHugeDataset && !fitsInMemory)
            slicesPerChunk = Math.Max(1, Math.Min(slicesPerChunk, 32));

        Logger.Log($"[Simulator] Creating chunks with {slicesPerChunk} slices each");

        for (var z = 0; z < _params.Depth; z += slicesPerChunk)
        {
            var depth = Math.Min(slicesPerChunk, _params.Depth - z);
            _chunks.Add(new WaveFieldChunk(z, depth, _params.Width, _params.Height));
        }

        Logger.Log(
            $"[Simulator] Created {_chunks.Count} chunks for volume {_params.Width}x{_params.Height}x{_params.Depth}");
        Logger.Log(
            $"[Simulator] Est. memory per chunk: {slicesPerChunk * _params.Width * _params.Height * bytesPerVoxel / (1024 * 1024)} MB");
    }

    private void InitializeFields()
    {
        // Allocate tracking arrays - sized to SIMULATION dimensions
        _maxPWaveMagnitude = new float[_params.Width, _params.Height, _params.Depth];
        _maxSWaveMagnitude = new float[_params.Width, _params.Height, _params.Depth];
        _maxVelocityMagnitude = new float[_params.Width, _params.Height, _params.Depth];

        Logger.Log(
            $"[Simulator] Allocated tracking arrays for simulation extent: {_params.Width}x{_params.Height}x{_params.Depth}");
        Logger.Log(
            $"[Simulator] P-wave tracking: {_params.Width * _params.Height * _params.Depth * sizeof(float) / (1024 * 1024)} MB");
        Logger.Log(
            $"[Simulator] S-wave tracking: {_params.Width * _params.Height * _params.Depth * sizeof(float) / (1024 * 1024)} MB");
        Logger.Log(
            $"[Simulator] Combined tracking: {_params.Width * _params.Height * _params.Depth * sizeof(float) / (1024 * 1024)} MB");
        Logger.Log(
            $"[Simulator] Total tracking overhead: {_params.Width * _params.Height * _params.Depth * sizeof(float) * 3 / (1024 * 1024)} MB");

        // Calculate propagation direction
        var txX = (int)(_params.TxPosition.X * _params.Width);
        var txY = (int)(_params.TxPosition.Y * _params.Height);
        var txZ = (int)(_params.TxPosition.Z * _params.Depth);
        var rxX = (int)(_params.RxPosition.X * _params.Width);
        var rxY = (int)(_params.RxPosition.Y * _params.Height);
        var rxZ = (int)(_params.RxPosition.Z * _params.Depth);

        var dx = rxX - txX;
        var dy = rxY - txY;
        var dz = rxZ - txZ;
        var dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        if (dist > 1e-6f)
        {
            _propagationDirection = new Vector3(dx / dist, dy / dist, dz / dist);
            Logger.Log(
                $"[Simulator] Propagation direction: ({_propagationDirection.X:F3}, {_propagationDirection.Y:F3}, {_propagationDirection.Z:F3})");
        }
        else
        {
            _propagationDirection = new Vector3(1, 0, 0);
            Logger.LogWarning("[Simulator] TX and RX too close, using default propagation direction");
        }

        if (_params.UseBrittleModel)
        {
            _damageField = new float[_params.Width, _params.Height, _params.Depth];
            Logger.Log($"[Simulator] Damage field initialized: {_params.Width}x{_params.Height}x{_params.Depth}");
        }

        if (_params.SaveTimeSeries)
        {
            _timeSeriesSnapshots = new List<WaveFieldSnapshot>();
            Logger.Log($"[Simulator] Time series enabled (interval: {_params.SnapshotInterval})");
        }

        LoadChunk(_chunks[0]);
        Logger.Log("[Simulator] First chunk loaded and ready");
    }


    private void ApplyInitialSource()
    {
        var chunk = _chunks[0];
        var txX = (int)(_params.TxPosition.X * _params.Width);
        var txY = (int)(_params.TxPosition.Y * _params.Height);
        var txZ = (int)(_params.TxPosition.Z * _params.Depth);

        Logger.Log($"[Source] Applying initial source at voxel position ({txX}, {txY}, {txZ})");
        Logger.Log(
            $"[Source] Normalized position: ({_params.TxPosition.X:F3}, {_params.TxPosition.Y:F3}, {_params.TxPosition.Z:F3})");
        Logger.Log($"[Source] Mode: {(_params.UseFullFaceTransducers ? "Full-Face Transducer" : "Point Source")}");
        Logger.Log($"[Source] Wavelet: {(_params.UseRickerWavelet ? "Ricker" : "Sinusoidal")}");
        Logger.Log($"[Source] Energy: {_params.SourceEnergyJ} J, Frequency: {_params.SourceFrequencyKHz} kHz");

        if (_params.UseFullFaceTransducers)
            ApplyFullFaceSource(chunk, txX, txY, txZ);
        else
            ApplyPointSource(chunk, txX, txY, txZ);

        // Calculate baseline amplitude at receiver for wave detection
        var rxX = (int)(_params.RxPosition.X * _params.Width);
        var rxY = (int)(_params.RxPosition.Y * _params.Height);
        var rxZ = (int)(_params.RxPosition.Z * _params.Depth);
        _baselineAmplitude = CalculateAmplitudeAt(chunk, rxX, rxY, rxZ);

        Logger.Log("[Source] Initial source applied successfully");
        Logger.Log($"[Receiver] Position: voxel ({rxX}, {rxY}, {rxZ}), baseline amplitude: {_baselineAmplitude:E3}");

        // Calculate expected travel distance
        var dx = (rxX - txX) * _params.PixelSize;
        var dy = (rxY - txY) * _params.PixelSize;
        var dz = (rxZ - txZ) * _params.PixelSize;
        var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        Logger.Log($"[Source] TX-RX distance: {distance * 1000:F2} mm");
    }

    private void ApplyPointSource(WaveFieldChunk chunk, int x, int y, int z)
    {
        if (z < chunk.StartZ || z >= chunk.StartZ + chunk.Depth)
            return;

        var localZ = z - chunk.StartZ;

        // Calculate amplitude from source energy
        var density = DensityVolume?[x, y, z] ?? 2700f;
        var voxelVolume = MathF.Pow(_params.PixelSize, 3);
        var energyPerVoxel = _params.SourceEnergyJ / (voxelVolume * 1e9f);
        var amplitude = MathF.Sqrt(2f * energyPerVoxel / density);
        amplitude *= _params.SourceAmplitude / 100f;

        if (_params.UseRickerWavelet)
        {
            var freq = _params.SourceFrequencyKHz * 1000f;
            var t = 1.0f / freq;
            amplitude *= RickerWavelet(0, freq, t);
        }

        // FIX: Generate realistic wave with both P and S components
        // Real transducers create both longitudinal and transverse motion

        // Calculate expected Vp/Vs ratio for the material
        var E = _params.YoungsModulusMPa * 1e6f;
        var nu = _params.PoissonRatio;
        var mu = E / (2f * (1f + nu));
        var lambda = E * nu / ((1f + nu) * (1f - 2f * nu));
        var vp = MathF.Sqrt((lambda + 2f * mu) / density);
        var vs = MathF.Sqrt(mu / density);
        var vpVsRatio = vs > 0 ? vp / vs : 1.732f; // Default sqrt(3) for typical rocks

        // Amplitude partition: P-wave gets ~70%, S-wave gets ~30% of energy
        // This is typical for a finite-size transducer
        var pWaveAmplitude = amplitude * 0.85f;
        var sWaveAmplitude = amplitude * 0.35f; // S-waves have smaller amplitude

        // Apply based on propagation axis
        switch (_params.Axis)
        {
            case 0: // X-Axis propagation
                chunk.Vx[x, y, localZ] = pWaveAmplitude; // P-wave (longitudinal)
                chunk.Vy[x, y, localZ] = sWaveAmplitude * 0.7f; // S-wave (transverse)
                chunk.Vz[x, y, localZ] = sWaveAmplitude * 0.7f; // S-wave (transverse)
                break;
            case 1: // Y-Axis propagation
                chunk.Vy[x, y, localZ] = pWaveAmplitude; // P-wave
                chunk.Vx[x, y, localZ] = sWaveAmplitude * 0.7f; // S-wave
                chunk.Vz[x, y, localZ] = sWaveAmplitude * 0.7f; // S-wave
                break;
            case 2: // Z-Axis propagation
                chunk.Vz[x, y, localZ] = pWaveAmplitude; // P-wave
                chunk.Vx[x, y, localZ] = sWaveAmplitude * 0.7f; // S-wave
                chunk.Vy[x, y, localZ] = sWaveAmplitude * 0.7f; // S-wave
                break;
        }

        Logger.Log($"[Source] Applied multi-mode source at ({x},{y},{z})");
        Logger.Log($"[Source] P-wave amplitude: {pWaveAmplitude:E3}, S-wave amplitude: {sWaveAmplitude:E3}");
        Logger.Log($"[Source] Expected Vp/Vs ratio: {vpVsRatio:F3}");
    }

    private void ApplyFullFaceSource(WaveFieldChunk chunk, int txX, int txY, int txZ)
    {
        // Calculate amplitude from source energy distributed over face
        var faceArea = _params.Axis switch
        {
            0 => _params.Height * _params.Depth,
            1 => _params.Width * _params.Depth,
            2 => _params.Width * _params.Height,
            _ => 1
        };

        var density = 2700f;
        var voxelVolume = MathF.Pow(_params.PixelSize, 3);
        var totalVolume = faceArea * voxelVolume;
        var energyPerVoxel = _params.SourceEnergyJ / faceArea;
        var amplitude = MathF.Sqrt(2f * energyPerVoxel / (density * voxelVolume));
        amplitude *= _params.SourceAmplitude / 100f;

        if (_params.UseRickerWavelet)
        {
            var freq = _params.SourceFrequencyKHz * 1000f;
            var t = 1.0f / freq;
            amplitude *= RickerWavelet(0, freq, t);
        }

        // FIX: Add transverse components for S-wave generation
        var pWaveAmplitude = amplitude * 0.85f;
        var sWaveAmplitude = amplitude * 0.35f;

        // Apply to entire face with mixed modes
        switch (_params.Axis)
        {
            case 0: // YZ face at X
                for (var y = 0; y < _params.Height; y++)
                for (var z = 0; z < chunk.Depth; z++)
                    if (txX >= 0 && txX < _params.Width)
                    {
                        chunk.Vx[txX, y, z] = pWaveAmplitude;
                        chunk.Vy[txX, y, z] = sWaveAmplitude * 0.7f;
                        chunk.Vz[txX, y, z] = sWaveAmplitude * 0.7f;
                    }

                break;
            case 1: // XZ face at Y
                for (var x = 0; x < _params.Width; x++)
                for (var z = 0; z < chunk.Depth; z++)
                    if (txY >= 0 && txY < _params.Height)
                    {
                        chunk.Vy[x, txY, z] = pWaveAmplitude;
                        chunk.Vx[x, txY, z] = sWaveAmplitude * 0.7f;
                        chunk.Vz[x, txY, z] = sWaveAmplitude * 0.7f;
                    }

                break;
            case 2: // XY face at Z
                if (txZ >= chunk.StartZ && txZ < chunk.StartZ + chunk.Depth)
                {
                    var localZ = txZ - chunk.StartZ;
                    for (var x = 0; x < _params.Width; x++)
                    for (var y = 0; y < _params.Height; y++)
                    {
                        chunk.Vz[x, y, localZ] = pWaveAmplitude;
                        chunk.Vx[x, y, localZ] = sWaveAmplitude * 0.7f;
                        chunk.Vy[x, y, localZ] = sWaveAmplitude * 0.7f;
                    }
                }

                break;
        }
    }

    private float RickerWavelet(float t, float freq, float tpeak)
    {
        var arg = MathF.PI * freq * (t - tpeak);
        var arg2 = arg * arg;
        return (1f - 2f * arg2) * MathF.Exp(-arg2);
    }

    private async Task ProcessTimeStepAsync(CancellationToken ct)
    {
        var shouldReportProgress = CurrentStep % 50 == 0;
        var shouldUpdateVisualization = _params.EnableRealTimeVisualization &&
                                        (DateTime.Now - _lastVisualizationUpdate).TotalSeconds >=
                                        _visualizationUpdateInterval;
        var shouldLogActivity = CurrentStep % 500 == 0;
        var shouldTriggerGC = CurrentStep % 500 == 0;

        for (var chunkIdx = 0; chunkIdx < _chunks.Count; chunkIdx++)
        {
            ct.ThrowIfCancellationRequested();

            var chunk = _chunks[chunkIdx];

            if (chunk.IsOffloaded)
                LoadChunkWithLRU(chunkIdx);
            else
                TrackChunkAccess(chunkIdx);

            // FIX: Use persistent material properties that can be modified by damage
            var (E, nu, rho) = ExtractChunkMaterialProperties(chunk, true);

            if (_params.UseElasticModel)
                _kernel.UpdateWaveField(
                    chunk.Vx, chunk.Vy, chunk.Vz,
                    chunk.Sxx, chunk.Syy, chunk.Szz,
                    chunk.Sxy, chunk.Sxz, chunk.Syz,
                    E, nu, rho,
                    _params.TimeStepSeconds,
                    _params.PixelSize,
                    _params.ArtificialDampingFactor);

            if (_params.UsePlasticModel)
                ApplyPlasticity(chunk, E, nu, rho);

            // FIX: Apply damage and persist changes
            if (_params.UseBrittleModel)
                ApplyDamageFixed(chunk, E, nu);

            UpdateMaxVelocity(chunk);
            ApplyBoundaryConditions(chunk);

            if (CurrentStep < 100)
                ApplyContinuousSource(chunk, CurrentStep);

            if (shouldUpdateVisualization)
            {
                NotifyWaveFieldUpdate(chunk, CurrentStep);
                if (chunkIdx == _chunks.Count - 1)
                    _lastVisualizationUpdate = DateTime.Now;
            }

            if (_params.EnableOffloading && chunkIdx % 5 == 0 && _currentMemoryUsageBytes > _maxMemoryBudgetBytes)
            {
                var safeZone = Math.Min(5, _chunks.Count / 10);
                if (chunkIdx > safeZone && chunkIdx < _chunks.Count - safeZone)
                    EvictLRUChunksIfNeeded();
            }
        }

        if (CurrentStep % 100 == 0)
            RunDiagnostics(CurrentStep);

        if (shouldReportProgress)
            ReportProgress();

        if (shouldTriggerGC)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
            UpdateMemoryUsage();
        }

        if (_params.SaveTimeSeries && CurrentStep % _params.SnapshotInterval == 0)
            SaveSnapshot(CurrentStep);

        DetectWaveArrivals(CurrentStep);

        if (shouldLogActivity)
            LogWaveActivity();
    }

    private void TrackChunkAccess(int chunkIdx)
    {
        // Update LRU tracking
        if (_chunkAccessSet.Contains(chunkIdx))
        {
            // Remove old position in queue (will be re-added at end)
            var tempQueue = new Queue<int>();
            while (_chunkAccessOrder.Count > 0)
            {
                var idx = _chunkAccessOrder.Dequeue();
                if (idx != chunkIdx)
                    tempQueue.Enqueue(idx);
            }

            _chunkAccessOrder = tempQueue;
        }
        else
        {
            _chunkAccessSet.Add(chunkIdx);
        }

        // Add to end (most recently used)
        _chunkAccessOrder.Enqueue(chunkIdx);
    }

    private void LoadChunkWithLRU(int chunkIdx)
    {
        // Check if we need to evict before loading
        var loadedCount = _chunks.Count(c => !c.IsOffloaded);
        if (loadedCount >= _maxLoadedChunks) EvictLRUChunksIfNeeded();

        LoadChunk(_chunks[chunkIdx]);
        TrackChunkAccess(chunkIdx);

        // Update memory usage
        _currentMemoryUsageBytes = _chunks.Where(c => !c.IsOffloaded).Sum(c => c.MemorySize);
    }

    private void EvictLRUChunksIfNeeded()
    {
        // Find least recently used chunks to evict
        while (_chunkAccessOrder.Count > 0 &&
               (_chunks.Count(c => !c.IsOffloaded) > _maxLoadedChunks ||
                _currentMemoryUsageBytes > _maxMemoryBudgetBytes))
        {
            var lruChunkIdx = _chunkAccessOrder.Dequeue();
            _chunkAccessSet.Remove(lruChunkIdx);

            var chunk = _chunks[lruChunkIdx];
            if (!chunk.IsOffloaded)
            {
                OffloadChunk(chunk);
                _currentMemoryUsageBytes -= chunk.MemorySize;

                if (_isHugeDataset && CurrentStep % 50 == 0)
                    Logger.Log(
                        $"[Memory] Evicted chunk {lruChunkIdx} (LRU), current usage: {_currentMemoryUsageBytes / (1024 * 1024)} MB");
            }
        }
    }

    private (float[,,] E, float[,,] nu, float[,,] rho) ExtractChunkMaterialProperties(
        WaveFieldChunk chunk, bool usePersistent = false)
    {
        var chunkWidth = chunk.Vx.GetLength(0);
        var chunkHeight = chunk.Vx.GetLength(1);
        var chunkDepth = chunk.Vx.GetLength(2);

        var E = new float[chunkWidth, chunkHeight, chunkDepth];
        var nu = new float[chunkWidth, chunkHeight, chunkDepth];
        var rho = new float[chunkWidth, chunkHeight, chunkDepth];

        // Get the dimensions of the persistent property arrays
        var propWidth = _persistentYoungsModulus?.GetLength(0) ?? _params.Width;
        var propHeight = _persistentYoungsModulus?.GetLength(1) ?? _params.Height;
        var propDepth = _persistentYoungsModulus?.GetLength(2) ?? _params.Depth;

        Parallel.For(0, chunkDepth, z =>
        {
            var localZ = chunk.StartZ + z;

            for (var y = 0; y < chunkHeight; y++)
            for (var x = 0; x < chunkWidth; x++)
            {
                // BOUNDS CHECK: Ensure we're within the persistent property arrays
                if (x >= propWidth || y >= propHeight || localZ >= propDepth)
                {
                    // Out of bounds - use defaults
                    E[x, y, z] = _params.YoungsModulusMPa * 1e6f;
                    nu[x, y, z] = _params.PoissonRatio;
                    rho[x, y, z] = 2700f;
                    continue;
                }

                // Use persistent properties if damage is enabled
                if (usePersistent && _params.UseBrittleModel && _persistentYoungsModulus != null)
                {
                    E[x, y, z] = _persistentYoungsModulus[x, y, localZ] * 1e6f;
                    nu[x, y, z] = _persistentPoissonRatio[x, y, localZ];
                }
                else if (YoungsModulusVolume != null && PoissonRatioVolume != null)
                {
                    // Access from full volume with offset
                    var offsetX = _params.SimulationExtent?.Min.X ?? 0;
                    var offsetY = _params.SimulationExtent?.Min.Y ?? 0;
                    var offsetZ = _params.SimulationExtent?.Min.Z ?? 0;

                    var globalX = offsetX + x;
                    var globalY = offsetY + y;
                    var globalZ = offsetZ + localZ;

                    var fullWidth = YoungsModulusVolume.GetLength(0);
                    var fullHeight = YoungsModulusVolume.GetLength(1);
                    var fullDepth = YoungsModulusVolume.GetLength(2);

                    if (globalX < fullWidth && globalY < fullHeight && globalZ < fullDepth)
                    {
                        E[x, y, z] = YoungsModulusVolume[globalX, globalY, globalZ] * 1e6f;
                        nu[x, y, z] = PoissonRatioVolume[globalX, globalY, globalZ];
                    }
                    else
                    {
                        E[x, y, z] = _params.YoungsModulusMPa * 1e6f;
                        nu[x, y, z] = _params.PoissonRatio;
                    }
                }
                else
                {
                    E[x, y, z] = _params.YoungsModulusMPa * 1e6f;
                    nu[x, y, z] = _params.PoissonRatio;
                }

                // Get density
                if (DensityVolume != null)
                {
                    var offsetX = _params.SimulationExtent?.Min.X ?? 0;
                    var offsetY = _params.SimulationExtent?.Min.Y ?? 0;
                    var offsetZ = _params.SimulationExtent?.Min.Z ?? 0;

                    var globalX = offsetX + x;
                    var globalY = offsetY + y;
                    var globalZ = offsetZ + localZ;

                    var fullWidth = DensityVolume.GetLength(0);
                    var fullHeight = DensityVolume.GetLength(1);
                    var fullDepth = DensityVolume.GetLength(2);

                    if (globalX < fullWidth && globalY < fullHeight && globalZ < fullDepth)
                        rho[x, y, z] = DensityVolume[globalX, globalY, globalZ];
                    else
                        rho[x, y, z] = 2700f;
                }
                else
                {
                    rho[x, y, z] = 2700f;
                }
            }
        });

        return (E, nu, rho);
    }

    private void ApplyPlasticity(WaveFieldChunk chunk, float[,,] E, float[,,] nu, float[,,] rho)
    {
        var confiningStress = _params.ConfiningPressureMPa * 1e6f;
        var cohesion = _params.CohesionMPa * 1e6f;
        var frictionAngle = _params.FailureAngleDeg * MathF.PI / 180f;
        var sinPhi = MathF.Sin(frictionAngle);

        Parallel.For(0, chunk.Depth, z =>
        {
            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
            {
                var sxx = chunk.Sxx[x, y, z];
                var syy = chunk.Syy[x, y, z];
                var szz = chunk.Szz[x, y, z];

                // Mean stress
                var p = -(sxx + syy + szz) / 3f + confiningStress;

                // Deviatoric stress
                var sxx_dev = sxx + p;
                var syy_dev = syy + p;
                var szz_dev = szz + p;

                var q = MathF.Sqrt(1.5f * (sxx_dev * sxx_dev + syy_dev * syy_dev + szz_dev * szz_dev));

                // Mohr-Coulomb yield function
                var F = q - (cohesion + p * sinPhi);

                if (F > 0)
                {
                    // Plastic correction
                    var factor = (cohesion + p * sinPhi) / (q + 1e-10f);
                    chunk.Sxx[x, y, z] = sxx_dev * factor - p;
                    chunk.Syy[x, y, z] = syy_dev * factor - p;
                    chunk.Szz[x, y, z] = szz_dev * factor - p;
                }
            }
        });
    }

    private void ApplyDamageFixed(WaveFieldChunk chunk, float[,,] E, float[,,] nu)
    {
        var tensileStrength = 2e6f; // 2 MPa

        // CRITICAL: When using simulation extent, all tracking arrays (_damageField, _persistentYoungsModulus)
        // are sized to the SIMULATION dimensions, not the full dataset dimensions!
        // So we must use LOCAL coordinates within the simulation extent.

        var simWidth = _damageField?.GetLength(0) ?? _params.Width;
        var simHeight = _damageField?.GetLength(1) ?? _params.Height;
        var simDepth = _damageField?.GetLength(2) ?? _params.Depth;

        Parallel.For(0, chunk.Depth, z =>
        {
            // LOCAL coordinates within the simulation extent
            var localZ = chunk.StartZ + z;

            // Bounds check
            if (localZ < 0 || localZ >= simDepth)
                return;

            for (var y = 0; y < _params.Height; y++)
            {
                if (y < 0 || y >= simHeight)
                    continue;

                for (var x = 0; x < _params.Width; x++)
                {
                    if (x < 0 || x >= simWidth)
                        continue;

                    var sxx = chunk.Sxx[x, y, z];
                    var syy = chunk.Syy[x, y, z];
                    var szz = chunk.Szz[x, y, z];

                    var maxStress = Math.Max(sxx, Math.Max(syy, szz));

                    if (maxStress > tensileStrength && _damageField != null)
                    {
                        var damageIncrement = (maxStress - tensileStrength) / (tensileStrength * 10f);
                        var newDamage = Math.Min(1f, _damageField[x, y, localZ] + damageIncrement);
                        _damageField[x, y, localZ] = newDamage;

                        // Update persistent material properties
                        if (_persistentYoungsModulus != null && _persistentPoissonRatio != null)
                        {
                            var reduction = 1f - newDamage * 0.8f;
                            _persistentYoungsModulus[x, y, localZ] *= reduction;
                            E[x, y, z] = _persistentYoungsModulus[x, y, localZ] * 1e6f;
                        }

                        // Log first damage occurrence (reduced frequency)
                        if (newDamage > 0.1f && CurrentStep % 500 == 0)
                            Logger.Log($"[Damage] Voxel ({x},{y},{localZ}): " +
                                       $"Damage={newDamage:F3}, Stress={maxStress / 1e6f:F2} MPa");
                    }
                }
            }
        });
    }

    private void ApplyBoundaryConditions(WaveFieldChunk chunk)
    {
        // Absorbing boundary conditions (simple damping)
        var damping = 0.95f;
        var boundary = 3;

        // X boundaries
        for (var z = 0; z < chunk.Depth; z++)
        for (var y = 0; y < _params.Height; y++)
        for (var b = 0; b < boundary; b++)
        {
            if (b < _params.Width)
            {
                chunk.Vx[b, y, z] *= damping;
                chunk.Vy[b, y, z] *= damping;
                chunk.Vz[b, y, z] *= damping;
            }

            if (_params.Width - 1 - b >= 0)
            {
                chunk.Vx[_params.Width - 1 - b, y, z] *= damping;
                chunk.Vy[_params.Width - 1 - b, y, z] *= damping;
                chunk.Vz[_params.Width - 1 - b, y, z] *= damping;
            }
        }

        // Y boundaries
        for (var z = 0; z < chunk.Depth; z++)
        for (var x = 0; x < _params.Width; x++)
        for (var b = 0; b < boundary; b++)
        {
            if (b < _params.Height)
            {
                chunk.Vx[x, b, z] *= damping;
                chunk.Vy[x, b, z] *= damping;
                chunk.Vz[x, b, z] *= damping;
            }

            if (_params.Height - 1 - b >= 0)
            {
                chunk.Vx[x, _params.Height - 1 - b, z] *= damping;
                chunk.Vy[x, _params.Height - 1 - b, z] *= damping;
                chunk.Vz[x, _params.Height - 1 - b, z] *= damping;
            }
        }

        // Z boundaries (only if at volume edges)
        if (chunk.StartZ < boundary)
            for (var z = 0; z < Math.Min(boundary, chunk.Depth); z++)
            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
            {
                chunk.Vx[x, y, z] *= damping;
                chunk.Vy[x, y, z] *= damping;
                chunk.Vz[x, y, z] *= damping;
            }

        if (chunk.StartZ + chunk.Depth > _params.Depth - boundary)
        {
            var startZ = Math.Max(0, _params.Depth - boundary - chunk.StartZ);
            for (var z = startZ; z < chunk.Depth; z++)
            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
            {
                chunk.Vx[x, y, z] *= damping;
                chunk.Vy[x, y, z] *= damping;
                chunk.Vz[x, y, z] *= damping;
            }
        }
    }

    private void ApplyContinuousSource(WaveFieldChunk chunk, int timeStep)
    {
        var txX = (int)(_params.TxPosition.X * _params.Width);
        var txY = (int)(_params.TxPosition.Y * _params.Height);
        var txZ = (int)(_params.TxPosition.Z * _params.Depth);

        if (txZ < chunk.StartZ || txZ >= chunk.StartZ + chunk.Depth)
            return;

        var t = timeStep * _params.TimeStepSeconds;
        var freq = _params.SourceFrequencyKHz * 1000f;
        var tpeak = 1.0f / freq;

        // Calculate amplitude from energy
        var density = DensityVolume?[txX, txY, txZ] ?? 2700f;
        var voxelVolume = MathF.Pow(_params.PixelSize, 3);
        var energyPerVoxel = _params.SourceEnergyJ / (voxelVolume * 1e9f);
        var amplitude = MathF.Sqrt(2f * energyPerVoxel / density);
        amplitude *= _params.SourceAmplitude / 100f;

        if (_params.UseRickerWavelet)
            amplitude *= RickerWavelet(t, freq, tpeak);
        else
            amplitude *= MathF.Sin(2f * MathF.PI * freq * t);

        var localZ = txZ - chunk.StartZ;

        if (_params.UseFullFaceTransducers)
        {
            var faceArea = _params.Axis switch
            {
                0 => _params.Height * _params.Depth,
                1 => _params.Width * _params.Depth,
                2 => _params.Width * _params.Height,
                _ => 1
            };
            amplitude /= MathF.Sqrt(faceArea); // Distribute energy

            switch (_params.Axis)
            {
                case 0:
                    for (var y = 0; y < _params.Height; y++)
                    for (var z = 0; z < chunk.Depth; z++)
                        chunk.Vx[txX, y, z] += amplitude;
                    break;
                case 1:
                    for (var x = 0; x < _params.Width; x++)
                    for (var z = 0; z < chunk.Depth; z++)
                        chunk.Vy[x, txY, z] += amplitude;
                    break;
                case 2:
                    for (var x = 0; x < _params.Width; x++)
                    for (var y = 0; y < _params.Height; y++)
                        chunk.Vz[x, y, localZ] += amplitude;
                    break;
            }
        }
        else
        {
            switch (_params.Axis)
            {
                case 0: chunk.Vx[txX, txY, localZ] += amplitude; break;
                case 1: chunk.Vy[txX, txY, localZ] += amplitude; break;
                case 2: chunk.Vz[txX, txY, localZ] += amplitude; break;
            }
        }
    }

    private float CalculateAmplitudeAt(WaveFieldChunk chunk, int x, int y, int z)
    {
        if (z < chunk.StartZ || z >= chunk.StartZ + chunk.Depth)
            return 0f;

        var localZ = z - chunk.StartZ;
        var vx = chunk.Vx[x, y, localZ];
        var vy = chunk.Vy[x, y, localZ];
        var vz = chunk.Vz[x, y, localZ];
        return MathF.Sqrt(vx * vx + vy * vy + vz * vz);
    }

    private void UpdateMaxVelocity(WaveFieldChunk chunk)
    {
        var simWidth = _maxVelocityMagnitude?.GetLength(0) ?? _params.Width;
        var simHeight = _maxVelocityMagnitude?.GetLength(1) ?? _params.Height;
        var simDepth = _maxVelocityMagnitude?.GetLength(2) ?? _params.Depth;

        Parallel.For(0, chunk.Depth, z =>
        {
            var localZ = chunk.StartZ + z;
            if (localZ < 0 || localZ >= simDepth) return;

            for (var y = 0; y < _params.Height; y++)
            {
                if (y < 0 || y >= simHeight) continue;

                for (var x = 0; x < _params.Width; x++)
                {
                    if (x < 0 || x >= simWidth) continue;

                    // FIX: Skip source region to prevent saturation
                    if (IsNearSource(x, y, localZ, 8)) continue;

                    var vx = chunk.Vx[x, y, z];
                    var vy = chunk.Vy[x, y, z];
                    var vz = chunk.Vz[x, y, z];

                    var magnitude = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
                    if (magnitude > _maxVelocityMagnitude[x, y, localZ])
                        _maxVelocityMagnitude[x, y, localZ] = magnitude;

                    // Decompose into P-wave (longitudinal) and S-wave (transverse)
                    var longitudinal = vx * _propagationDirection.X +
                                       vy * _propagationDirection.Y +
                                       vz * _propagationDirection.Z;
                    var pWaveMag = MathF.Abs(longitudinal);

                    if (pWaveMag > _maxPWaveMagnitude[x, y, localZ])
                        _maxPWaveMagnitude[x, y, localZ] = pWaveMag;

                    // S-wave: perpendicular components
                    var transverseX = vx - longitudinal * _propagationDirection.X;
                    var transverseY = vy - longitudinal * _propagationDirection.Y;
                    var transverseZ = vz - longitudinal * _propagationDirection.Z;
                    var sWaveMag = MathF.Sqrt(transverseX * transverseX +
                                              transverseY * transverseY +
                                              transverseZ * transverseZ);

                    if (sWaveMag > _maxSWaveMagnitude[x, y, localZ])
                        _maxSWaveMagnitude[x, y, localZ] = sWaveMag;
                }
            }
        });
    }

    private void LogWaveActivity()
    {
        // Log wave propagation statistics for debugging
        var zeroVoxels = 0;
        var activeVoxels = 0;
        float maxVel = 0;
        float avgVel = 0;
        var totalVoxels = 0;

        const float ACTIVE_THRESHOLD = 1e-8f;

        foreach (var chunk in _chunks)
        {
            if (chunk.IsOffloaded) continue;

            for (var z = 0; z < chunk.Depth; z++)
            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
            {
                var vx = chunk.Vx[x, y, z];
                var vy = chunk.Vy[x, y, z];
                var vz = chunk.Vz[x, y, z];
                var mag = MathF.Sqrt(vx * vx + vy * vy + vz * vz);

                totalVoxels++;
                if (mag < 1e-15f)
                    zeroVoxels++;
                else if (mag > ACTIVE_THRESHOLD)
                    activeVoxels++;

                avgVel += mag;
                maxVel = Math.Max(maxVel, mag);
            }
        }

        if (totalVoxels > 0)
            avgVel /= totalVoxels;

        Logger.Log(
            $"[Wave Activity] Step {CurrentStep}: Active={activeVoxels} ({100f * activeVoxels / totalVoxels:F2}%), " +
            $"Zero={zeroVoxels} ({100f * zeroVoxels / totalVoxels:F2}%), " +
            $"MaxVel={maxVel:E3}, AvgVel={avgVel:E3}");
    }

    private void DetectWaveArrivals(int timeStep)
    {
        var rxZ = (int)(_params.RxPosition.Z * _params.Depth);
        var rxChunk = _chunks.FirstOrDefault(c => rxZ >= c.StartZ && rxZ < c.StartZ + c.Depth);

        if (rxChunk == null || rxChunk.IsOffloaded)
            return;

        var rxX = (int)(_params.RxPosition.X * _params.Width);
        var rxY = (int)(_params.RxPosition.Y * _params.Height);
        var localZ = rxZ - rxChunk.StartZ;

        // Get velocity components at receiver
        var vx = rxChunk.Vx[rxX, rxY, localZ];
        var vy = rxChunk.Vy[rxX, rxY, localZ];
        var vz = rxChunk.Vz[rxX, rxY, localZ];
        var totalAmplitude = MathF.Sqrt(vx * vx + vy * vy + vz * vz);

        // Log amplitude periodically for debugging
        if (timeStep % 50 == 0 && totalAmplitude > 1e-15f)
            Logger.Log($"[Wave Detection] Step {timeStep}: RX amplitude = {totalAmplitude:E3}");

        // Detect P-wave (first arrival above threshold)
        if (_pWaveArrivalTime < 0 && totalAmplitude > _baselineAmplitude + ARRIVAL_THRESHOLD)
        {
            _pWaveArrivalTime = timeStep;
            var travelTime = timeStep * _params.TimeStepSeconds * 1e6f;
            Logger.Log($"[Wave Detection] P-WAVE ARRIVAL at step {timeStep} ({travelTime:F2} μs)");
            Logger.Log($"[Wave Detection] P-wave amplitude: {totalAmplitude:E3}");
        }

        // FIX: Proper S-wave detection based on propagation axis
        if (_pWaveArrivalTime > 0 && _sWaveArrivalTime < 0 && timeStep > _pWaveArrivalTime + 50)
        {
            // Calculate propagation direction (TX -> RX)
            var txX = (int)(_params.TxPosition.X * _params.Width);
            var txY = (int)(_params.TxPosition.Y * _params.Height);
            var txZ = (int)(_params.TxPosition.Z * _params.Depth);

            var dx = rxX - txX;
            var dy = rxY - txY;
            var dz = rxZ - txZ;
            var dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

            if (dist > 1e-6f)
            {
                // Normalized propagation direction
                var propDirX = dx / dist;
                var propDirY = dy / dist;
                var propDirZ = dz / dist;

                // Calculate longitudinal (P-wave) component
                var longitudinal = vx * propDirX + vy * propDirY + vz * propDirZ;

                // Calculate transverse (S-wave) components
                var transverseX = vx - longitudinal * propDirX;
                var transverseY = vy - longitudinal * propDirY;
                var transverseZ = vz - longitudinal * propDirZ;
                var transverseMag = MathF.Sqrt(transverseX * transverseX +
                                               transverseY * transverseY +
                                               transverseZ * transverseZ);

                // FIX: Detect S-wave when transverse motion exceeds threshold
                // AND is significantly different from P-wave time
                var sWaveThreshold = ARRIVAL_THRESHOLD * 0.1f; // Lower threshold for S-wave

                if (transverseMag > sWaveThreshold)
                {
                    _sWaveArrivalTime = timeStep;
                    var travelTime = timeStep * _params.TimeStepSeconds * 1e6f;
                    Logger.Log($"[Wave Detection] S-WAVE ARRIVAL at step {timeStep} ({travelTime:F2} μs)");
                    Logger.Log($"[Wave Detection] S-wave transverse amplitude: {transverseMag:E3}");
                    Logger.Log($"[Wave Detection] Longitudinal component: {Math.Abs(longitudinal):E3}");
                    Logger.Log($"[Wave Detection] Travel time difference: " +
                               $"{(_sWaveArrivalTime - _pWaveArrivalTime) * _params.TimeStepSeconds * 1e6f:F2} μs");
                }
            }
        }
    }

    private bool IsNearSource(int x, int y, int z, int excludeRadius = 5)
    {
        var txX = (int)(_params.TxPosition.X * _params.Width);
        var txY = (int)(_params.TxPosition.Y * _params.Height);
        var txZ = (int)(_params.TxPosition.Z * _params.Depth);

        var dx = x - txX;
        var dy = y - txY;
        var dz = z - txZ;
        var distSq = dx * dx + dy * dy + dz * dz;

        return distSq <= excludeRadius * excludeRadius;
    }

    private void SaveSnapshot(int timeStep)
    {
        // For huge datasets, only save current max velocities (not full field)
        if (_isHugeDataset)
        {
            var snapshot = new WaveFieldSnapshot
            {
                TimeStep = timeStep,
                SimulationTime = timeStep * _params.TimeStepSeconds,
                MaxVelocityField = (float[,,])_maxVelocityMagnitude.Clone()
            };
            _timeSeriesSnapshots.Add(snapshot);
            Logger.Log($"[Simulator] Saved lightweight snapshot {_timeSeriesSnapshots.Count} (max velocity only)");
            return;
        }

        // For normal datasets, save full wave field
        var fullSnapshot = new WaveFieldSnapshot
        {
            TimeStep = timeStep,
            SimulationTime = timeStep * _params.TimeStepSeconds
        };

        // Combine all chunks into full volume
        var combined = new float[_params.Width, _params.Height, _params.Depth];

        foreach (var chunk in _chunks)
        {
            if (chunk.IsOffloaded)
                LoadChunk(chunk);

            for (var z = 0; z < chunk.Depth; z++)
            {
                var globalZ = chunk.StartZ + z;
                for (var y = 0; y < _params.Height; y++)
                for (var x = 0; x < _params.Width; x++)
                {
                    var vx = chunk.Vx[x, y, z];
                    var vy = chunk.Vy[x, y, z];
                    var vz = chunk.Vz[x, y, z];
                    combined[x, y, globalZ] = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
                }
            }
        }

        fullSnapshot.VelocityField = combined;
        _timeSeriesSnapshots.Add(fullSnapshot);
        Logger.Log($"[Simulator] Saved full snapshot {_timeSeriesSnapshots.Count}");
    }

    private void NotifyWaveFieldUpdate(WaveFieldChunk chunk, int timeStep)
    {
        // VALIDATION: Ensure chunk coordinates are correct
        if (chunk.StartZ < 0 || chunk.StartZ >= _params.Depth)
        {
            Logger.LogError($"[CRITICAL] Chunk has invalid StartZ: {chunk.StartZ} (max: {_params.Depth})");
            return;
        }

        if (chunk.StartZ + chunk.Depth > _params.Depth)
        {
            Logger.LogError(
                $"[CRITICAL] Chunk exceeds volume bounds: StartZ={chunk.StartZ}, Depth={chunk.Depth}, MaxDepth={_params.Depth}");
            return;
        }

        WaveFieldUpdated?.Invoke(this, new WaveFieldUpdateEventArgs
        {
            ChunkVelocityFields = (chunk.Vx, chunk.Vy, chunk.Vz),
            ChunkStartZ = chunk.StartZ,
            ChunkDepth = chunk.Depth,
            TimeStep = timeStep,
            SimTime = timeStep * _params.TimeStepSeconds
        });
    }

    private void ReportProgress()
    {
        ProgressUpdated?.Invoke(this, new SimulationProgressEventArgs
        {
            Progress = Progress,
            Step = CurrentStep,
            Message = $"Step {CurrentStep}/{_params.TimeSteps}"
        });
    }

    private void UpdateMemoryUsage()
    {
        _currentMemoryUsageBytes = _chunks.Where(c => !c.IsOffloaded).Sum(c => c.MemorySize);
        CurrentMemoryUsageMB = _currentMemoryUsageBytes / (1024f * 1024f);

        var loadedChunks = _chunks.Count(c => !c.IsOffloaded);

        if (_isHugeDataset || loadedChunks < _chunks.Count)
            Logger.Log($"[Memory] Current: {CurrentMemoryUsageMB:F0} MB ({loadedChunks}/{_chunks.Count} chunks), " +
                       $"Budget: {_maxMemoryBudgetBytes / (1024 * 1024)} MB, " +
                       $"Usage: {100f * _currentMemoryUsageBytes / _maxMemoryBudgetBytes:F1}%");
    }

    private void LoadChunk(WaveFieldChunk chunk)
    {
        if (!chunk.IsOffloaded)
            return;

        if (string.IsNullOrEmpty(_offloadPath))
        {
            // Re-initialize
            chunk.Initialize();
            chunk.IsOffloaded = false;
            return;
        }

        var chunkFile = Path.Combine(_offloadPath, $"chunk_{chunk.StartZ}.dat");
        if (File.Exists(chunkFile))
        {
            chunk.LoadFromFile(chunkFile);
            chunk.IsOffloaded = false;
        }
    }

    private void OffloadChunk(WaveFieldChunk chunk)
    {
        if (chunk.IsOffloaded || string.IsNullOrEmpty(_offloadPath))
            return;

        var chunkFile = Path.Combine(_offloadPath, $"chunk_{chunk.StartZ}.dat");
        chunk.SaveToFile(chunkFile);
        chunk.Dispose();
        chunk.IsOffloaded = true;
    }

    private async Task<SimulationResults> AssembleResultsAsync(CancellationToken ct)
    {
        Logger.Log("[Simulator] Assembling final results...");

        var pVelocity = CalculateVelocity(_pWaveArrivalTime);
        var sVelocity = CalculateVelocity(_sWaveArrivalTime);

        Logger.Log("[Simulator] ═══════════════════════════════════════");
        Logger.Log("[Simulator] Using maximum P-wave, S-wave, and combined magnitudes");
        Logger.Log("[Simulator] This allows post-simulation Vp/Vs analysis");

        var finalResults = new SimulationResults
        {
            // FIX: Return all three fields separately
            WaveFieldVx = _maxPWaveMagnitude, // P-wave max
            WaveFieldVy = _maxSWaveMagnitude, // S-wave max
            WaveFieldVz = _maxVelocityMagnitude, // Combined max
            PWaveVelocity = pVelocity,
            SWaveVelocity = sVelocity,
            VpVsRatio = sVelocity > 0 ? pVelocity / sVelocity : 0,
            PWaveTravelTime = _pWaveArrivalTime,
            SWaveTravelTime = _sWaveArrivalTime,
            TotalTimeSteps = CurrentStep,
            ComputationTime = _stopwatch.Elapsed,
            DamageField = _damageField,
            TimeSeriesSnapshots = _timeSeriesSnapshots,
            Context = _materialLabels
        };

        var maxPWave = _maxPWaveMagnitude.Cast<float>().Max();
        var maxSWave = _maxSWaveMagnitude.Cast<float>().Max();
        var maxCombined = _maxVelocityMagnitude.Cast<float>().Max();
        var activePWave = _maxPWaveMagnitude.Cast<float>().Count(v => v > 1e-10f);
        var activeSWave = _maxSWaveMagnitude.Cast<float>().Count(v => v > 1e-10f);

        Logger.Log(
            $"[Simulator] Results: Vp={pVelocity:F2} m/s, Vs={sVelocity:F2} m/s, Vp/Vs={finalResults.VpVsRatio:F3}");
        Logger.Log($"[Simulator] Max P-wave magnitude: {maxPWave:E3} m/s");
        Logger.Log($"[Simulator] Max S-wave magnitude: {maxSWave:E3} m/s");
        Logger.Log($"[Simulator] Max combined magnitude: {maxCombined:E3} m/s");
        Logger.Log($"[Simulator] Active P-wave voxels: {activePWave:N0}");
        Logger.Log($"[Simulator] Active S-wave voxels: {activeSWave:N0}");
        Logger.Log("[Simulator] WaveFieldVx = max P-wave, WaveFieldVy = max S-wave, WaveFieldVz = max combined");
        Logger.Log("[Simulator] ═══════════════════════════════════════");

        return finalResults;
    }

    private float CalculateExpectedWaveTravelTime()
    {
        var dx = (_params.RxPosition.X - _params.TxPosition.X) * _params.Width * _params.PixelSize;
        var dy = (_params.RxPosition.Y - _params.TxPosition.Y) * _params.Height * _params.PixelSize;
        var dz = (_params.RxPosition.Z - _params.TxPosition.Z) * _params.Depth * _params.PixelSize;
        var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        var maxVp = 6000f;
        if (_params.YoungsModulusMPa > 0 && _params.PoissonRatio > 0)
        {
            var E = _params.YoungsModulusMPa * 1e6f;
            var nu = _params.PoissonRatio;
            var rho = 2700f;
            var mu = E / (2f * (1f + nu));
            var lambda = E * nu / ((1f + nu) * (1f - 2f * nu));
            maxVp = MathF.Sqrt((lambda + 2f * mu) / rho);
        }

        return distance / maxVp;
    }

    private int CountActiveVoxels(float[,,] vx, float[,,] vy, float[,,] vz)
    {
        var count = 0;
        const float threshold = 1e-10f;

        Parallel.For(0, _params.Depth, z =>
        {
            var localCount = 0;
            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
            {
                var mag = MathF.Sqrt(vx[x, y, z] * vx[x, y, z] +
                                     vy[x, y, z] * vy[x, y, z] +
                                     vz[x, y, z] * vz[x, y, z]);
                if (mag > threshold)
                    localCount++;
            }

            Interlocked.Add(ref count, localCount);
        });

        return count;
    }

    private double CalculateVelocity(int arrivalTimeStep)
    {
        if (arrivalTimeStep <= 0)
            return 0;

        // Calculate distance
        var dx = (_params.RxPosition.X - _params.TxPosition.X) * _params.Width * _params.PixelSize;
        var dy = (_params.RxPosition.Y - _params.TxPosition.Y) * _params.Height * _params.PixelSize;
        var dz = (_params.RxPosition.Z - _params.TxPosition.Z) * _params.Depth * _params.PixelSize;
        var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        // Calculate time
        double travelTime = arrivalTimeStep * _params.TimeStepSeconds;

        // Velocity = distance / time
        return travelTime > 0 ? distance / travelTime : 0;
    }

    private void Cleanup()
    {
        _kernel?.Dispose();

        foreach (var chunk in _chunks)
            chunk.Dispose();

        if (!string.IsNullOrEmpty(_offloadPath) && Directory.Exists(_offloadPath))
            try
            {
                Directory.Delete(_offloadPath, true);
            }
            catch
            {
                /* Ignore cleanup errors */
            }
    }

    /// <summary>
    ///     Clears the offload cache and resets LRU state. Can only be called when not simulating.
    /// </summary>
    public bool ClearOffloadCache()
    {
        // Don't allow clearing during simulation
        if (IsSimulating)
        {
            Logger.LogWarning("[Simulator] Cannot clear cache while simulation is running");
            return false;
        }

        try
        {
            // Clear LRU tracking
            _chunkAccessOrder.Clear();
            _chunkAccessSet.Clear();
            _currentMemoryUsageBytes = 0;

            // Mark all chunks as not offloaded (they're in memory or will be re-initialized)
            foreach (var chunk in _chunks)
                if (chunk.IsOffloaded)
                {
                    chunk.IsOffloaded = false;
                    chunk.Initialize(); // Re-initialize empty chunk
                }

            // Delete cache directory
            if (!string.IsNullOrEmpty(_offloadPath) && Directory.Exists(_offloadPath))
            {
                Directory.Delete(_offloadPath, true);
                Directory.CreateDirectory(_offloadPath);
                Logger.Log("[Simulator] Offload cache cleared and LRU state reset");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Simulator] Failed to clear cache: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Gets the current cache statistics for display in UI.
    /// </summary>
    public (int totalChunks, int loadedChunks, int offloadedChunks, long cacheSizeBytes) GetCacheStats()
    {
        var total = _chunks.Count;
        var loaded = _chunks.Count(c => !c.IsOffloaded);
        var offloaded = _chunks.Count(c => c.IsOffloaded);

        long cacheSize = 0;
        if (!string.IsNullOrEmpty(_offloadPath) && Directory.Exists(_offloadPath))
            try
            {
                var files = Directory.GetFiles(_offloadPath, "*.dat");
                cacheSize = files.Sum(f => new FileInfo(f).Length);
            }
            catch
            {
                /* Ignore errors */
            }

        return (total, loaded, offloaded, cacheSize);
    }

    #region debug

    private void ValidateChunkContinuity()
    {
        Logger.Log("[Chunk Validation] Checking chunk boundaries...");

        for (var i = 0; i < _chunks.Count - 1; i++)
        {
            var chunk1 = _chunks[i];
            var chunk2 = _chunks[i + 1];

            if (chunk1.IsOffloaded || chunk2.IsOffloaded) continue;

            // Check if chunks are adjacent
            if (chunk2.StartZ != chunk1.StartZ + chunk1.Depth)
            {
                Logger.LogError($"[Chunk Validation] Gap between chunks {i} and {i + 1}!");
                Logger.LogError($"  Chunk {i}: Z={chunk1.StartZ} to {chunk1.StartZ + chunk1.Depth - 1}");
                Logger.LogError($"  Chunk {i + 1}: Z={chunk2.StartZ} to {chunk2.StartZ + chunk2.Depth - 1}");
            }

            // Check boundary values match (sample a few points)
            var boundaryZ1 = chunk1.Depth - 1;
            var boundaryZ2 = 0;

            float maxDiscrepancy = 0;
            var samplePoints = 10;

            for (var sample = 0; sample < samplePoints; sample++)
            {
                var x = sample * _params.Width / samplePoints;
                var y = sample * _params.Height / samplePoints;

                var vx1 = chunk1.Vx[x, y, boundaryZ1];
                var vx2 = chunk2.Vx[x, y, boundaryZ2];

                // Note: Boundaries WON'T match exactly due to the way chunks are processed
                // But they should be similar in magnitude
                var diff = MathF.Abs(vx1 - vx2);
                maxDiscrepancy = MathF.Max(maxDiscrepancy, diff);
            }

            if (maxDiscrepancy > 1e-3f)
            {
                Logger.LogWarning($"[Chunk Validation] Large discrepancy at boundary {i}/{i + 1}: {maxDiscrepancy:E3}");
                Logger.LogWarning("  This can cause visual artifacts but may not affect physics if chunks overlap");
            }
        }
    }

    private void CheckEnergyConservation(int timeStep)
    {
        if (timeStep % 200 != 0) return;

        float totalKineticEnergy = 0;
        float totalStrainEnergy = 0;
        long voxelCount = 0;

        foreach (var chunk in _chunks)
        {
            if (chunk.IsOffloaded) continue;

            for (var z = 0; z < chunk.Depth; z++)
            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
            {
                // Kinetic energy: 0.5 * rho * v^2
                var vx = chunk.Vx[x, y, z];
                var vy = chunk.Vy[x, y, z];
                var vz = chunk.Vz[x, y, z];
                var vMag2 = vx * vx + vy * vy + vz * vz;

                var rho = DensityVolume?[x, y, chunk.StartZ + z] ?? 2700f;
                totalKineticEnergy += 0.5f * rho * vMag2;

                // Strain energy: 0.5 * sigma * epsilon (simplified)
                var sxx = chunk.Sxx[x, y, z];
                var syy = chunk.Syy[x, y, z];
                var szz = chunk.Szz[x, y, z];
                totalStrainEnergy += 0.5f * (sxx * sxx + syy * syy + szz * szz) / 1e9f; // Normalized

                voxelCount++;
            }
        }

        var totalEnergy = totalKineticEnergy + totalStrainEnergy;
        var sourceEnergy = _params.SourceEnergyJ;

        Logger.Log($"[Energy Check] Step {timeStep}:");
        Logger.Log($"  Kinetic Energy: {totalKineticEnergy:E3} J");
        Logger.Log($"  Strain Energy: {totalStrainEnergy:E3} J");
        Logger.Log($"  Total Energy: {totalEnergy:E3} J");
        Logger.Log($"  Source Energy: {sourceEnergy:E3} J");
        Logger.Log($"  Energy Ratio: {totalEnergy / sourceEnergy:F3}");

        if (totalEnergy > sourceEnergy * 10)
            Logger.LogWarning("  Warning: ENERGY GROWING - Possible numerical instability!");
        else if (totalEnergy < sourceEnergy * 0.01f && timeStep > 100)
            Logger.LogWarning("  Warning: ENERGY TOO LOW - Wave may be over-damped");
    }

    public void RunDiagnostics(int timeStep)
    {
        DiagnoseWaveFrontVelocity(timeStep);

        if (timeStep == 500) ValidateChunkContinuity();

        CheckEnergyConservation(timeStep);
    }

    private void DiagnoseWaveFrontVelocity(int timeStep)
    {
        if (timeStep % 100 != 0) return; // Check every 100 steps

        var rxX = (int)(_params.RxPosition.X * _params.Width);
        var rxY = (int)(_params.RxPosition.Y * _params.Height);
        var rxZ = (int)(_params.RxPosition.Z * _params.Depth);

        var txX = (int)(_params.TxPosition.X * _params.Width);
        var txY = (int)(_params.TxPosition.Y * _params.Height);
        var txZ = (int)(_params.TxPosition.Z * _params.Depth);

        // Find the wave front (max velocity point between TX and RX)
        float maxVel = 0;
        int frontX = txX, frontY = txY, frontZ = txZ;

        // Sample along the TX-RX line
        for (float t = 0; t <= 1.0f; t += 0.05f)
        {
            var x = (int)(txX + (rxX - txX) * t);
            var y = (int)(txY + (rxY - txY) * t);
            var z = (int)(txZ + (rxZ - txZ) * t);

            // Find chunk containing this point
            var chunk = _chunks.FirstOrDefault(c => z >= c.StartZ && z < c.StartZ + c.Depth);
            if (chunk == null || chunk.IsOffloaded) continue;

            var localZ = z - chunk.StartZ;
            if (localZ < 0 || localZ >= chunk.Depth) continue;

            var vx = chunk.Vx[x, y, localZ];
            var vy = chunk.Vy[x, y, localZ];
            var vz = chunk.Vz[x, y, localZ];
            var vel = MathF.Sqrt(vx * vx + vy * vy + vz * vz);

            if (vel > maxVel)
            {
                maxVel = vel;
                frontX = x;
                frontY = y;
                frontZ = z;
            }
        }

        if (maxVel > 1e-10f)
        {
            // Calculate distance traveled
            var dx = (frontX - txX) * _params.PixelSize;
            var dy = (frontY - txY) * _params.PixelSize;
            var dz = (frontZ - txZ) * _params.PixelSize;
            var distanceTraveled = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

            // Calculate apparent velocity
            var timeElapsed = timeStep * _params.TimeStepSeconds;
            var apparentVelocity = distanceTraveled / timeElapsed;

            Logger.Log($"[Wave Diagnostic] Step {timeStep}:");
            Logger.Log($"  Wave front at ({frontX},{frontY},{frontZ}), distance: {distanceTraveled * 1000:F2} mm");
            Logger.Log($"  Max velocity at front: {maxVel:E3} m/s");
            Logger.Log($"  Apparent wave velocity: {apparentVelocity:F0} m/s");
            //Logger.Log($"  Expected Vp: {CalculateExpectedVp():F0} m/s");
            //Logger.Log($"  Progress: {100 * apparentVelocity / CalculateExpectedVp():F1}% of theoretical");
        }
    }

    #endregion
}
