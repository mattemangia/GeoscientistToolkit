// GeoscientistToolkit/Analysis/AcousticSimulation/ChunkedAcousticSimulator.cs

using System.Diagnostics;
using System.Numerics;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

/// <summary>
///     Main acoustic wave propagation simulator with chunked processing and GPU/CPU support.
/// </summary>
public class ChunkedAcousticSimulator : IDisposable
{
    private readonly SimulationParameters _params;
    private readonly List<WaveFieldChunk> _chunks = new();
    private readonly string _offloadPath;
    
    private IAcousticKernel _kernel;
    private float[,,] _damageField;
    private float[,,] _densityVolume;
    private float[,,] _youngsModulusVolume;
    private float[,,] _poissonRatioVolume;
    private byte[,,] _materialLabels;
    
    // Public accessors for exporter
    public float[,,] DensityVolume => _densityVolume;
    public float[,,] YoungsModulusVolume => _youngsModulusVolume;
    public float[,,] PoissonRatioVolume => _poissonRatioVolume;
    
    // Time series storage
    private List<WaveFieldSnapshot> _timeSeriesSnapshots;
    
    // Max velocity tracking (for final export)
    private float[,,] _maxVelocityMagnitude;
    
    // Adaptive memory management
    private bool _isHugeDataset;
    private const long HUGE_DATASET_THRESHOLD_GB = 8;
    private long _availableSystemRamBytes;
    private long _maxMemoryBudgetBytes;
    private long _currentMemoryUsageBytes;
    private Queue<int> _chunkAccessOrder = new(); // LRU tracking
    private HashSet<int> _chunkAccessSet = new();
    private const int MAX_LOADED_CHUNKS_SMALL = 999; // All chunks for small datasets
    private int _maxLoadedChunks;
    
    // Simulation state tracking
    private bool _isSimulating;
    
    // Progress tracking
    private int _currentStep;
    private Stopwatch _stopwatch;
    
    // P/S wave detection
    private int _pWaveArrivalTime = -1;
    private int _sWaveArrivalTime = -1;
    private float _baselineAmplitude;
    private const float ARRIVAL_THRESHOLD = 0.05f;
    
    public event EventHandler<SimulationProgressEventArgs> ProgressUpdated;
    public event EventHandler<WaveFieldUpdateEventArgs> WaveFieldUpdated;
    
    public float Progress => _params.TimeSteps > 0 ? (float)_currentStep / _params.TimeSteps : 0f;
    public int CurrentStep => _currentStep;
    public float CurrentMemoryUsageMB { get; private set; }
    public bool IsSimulating => _isSimulating; // Public accessor for UI

    public ChunkedAcousticSimulator(SimulationParameters parameters)
    {
        _params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        
        if (_params.EnableOffloading && !string.IsNullOrEmpty(_params.OffloadDirectory))
        {
            _offloadPath = Path.Combine(_params.OffloadDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(_offloadPath);
        }
        
        // Detect available system RAM
        DetectSystemMemory();
        
        // Adjust time step based on pixel size for stability
        CalculateAdaptiveTimeStep();
    }
    
    private void DetectSystemMemory()
    {
        try
        {
            // Get total physical memory
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            long totalPhysicalMemory = gcMemoryInfo.TotalAvailableMemoryBytes;
            
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
            Logger.Log($"[Simulator] Memory budget for simulation: {_maxMemoryBudgetBytes / (1024.0 * 1024 * 1024):F2} GB");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Simulator] Failed to detect system memory: {ex.Message}, using conservative 12GB budget");
            _maxMemoryBudgetBytes = 12L * 1024 * 1024 * 1024;
        }
    }

    public void SetPerVoxelMaterialProperties(float[,,] youngsModulus, float[,,] poissonRatio)
    {
        _youngsModulusVolume = youngsModulus;
        _poissonRatioVolume = poissonRatio;
    }

    public float[,,] GetDamageField() => _damageField;

    public async Task<SimulationResults> RunAsync(byte[,,] labels, float[,,] density, CancellationToken ct)
    {
        _materialLabels = labels;
        _densityVolume = density;
        _stopwatch = Stopwatch.StartNew();
        _currentStep = 0;
        _isSimulating = true; // Mark as simulating
        
        try
        {
            // Initialize kernel (CPU or GPU)
            InitializeKernel();
            
            // Initialize chunks
            InitializeChunks();
            
            // Initialize fields
            InitializeFields();
            
            // Apply source
            ApplyInitialSource();
            
            // Time stepping loop
            for (_currentStep = 0; _currentStep < _params.TimeSteps && !ct.IsCancellationRequested; _currentStep++)
            {
                await ProcessTimeStepAsync(ct);
                
                // Progress reporting
                if (_currentStep % 10 == 0)
                {
                    ReportProgress();
                    
                    // Trigger GC periodically to free memory
                    if (_currentStep % 100 == 0)
                    {
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                        UpdateMemoryUsage();
                    }
                }
                
                // Save snapshot if requested
                if (_params.SaveTimeSeries && _currentStep % _params.SnapshotInterval == 0)
                {
                    SaveSnapshot(_currentStep);
                }
                
                // Detect wave arrivals
                DetectWaveArrivals(_currentStep);
            }
            
            _stopwatch.Stop();
            
            // Assemble final results
            return await AssembleResultsAsync(ct);
        }
        finally
        {
            _isSimulating = false; // Mark as not simulating
            Cleanup();
        }
    }

    private void CalculateAdaptiveTimeStep()
    {
        // CFL condition: dt <= h / (sqrt(3) * Vp_max)
        // Estimate maximum P-wave velocity from material properties
        float maxVp = 6000f; // Conservative estimate
        
        if (_params.YoungsModulusMPa > 0 && _params.PoissonRatio > 0)
        {
            float E = _params.YoungsModulusMPa * 1e6f;
            float nu = _params.PoissonRatio;
            float rho = 2700f; // Typical rock density kg/m³
            
            float mu = E / (2f * (1f + nu));
            float lambda = E * nu / ((1f + nu) * (1f - 2f * nu));
            maxVp = MathF.Sqrt((lambda + 2f * mu) / rho);
        }
        
        float h = _params.PixelSize; // Already in meters
        float dtMax = 0.5f * h / (1.732f * maxVp); // Safety factor 0.5
        
        _params.TimeStepSeconds = dtMax;
        Logger.Log($"[Simulator] Adaptive time step: {_params.TimeStepSeconds * 1e6f:F3} µs");
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
        long totalVoxels = (long)_params.Width * _params.Height * _params.Depth;
        long bytesPerVoxel = sizeof(float) * 9; // vx, vy, vz, σxx, σyy, σzz, σxy, σxz, σyz
        long estimatedMemoryBytes = totalVoxels * bytesPerVoxel;
        long estimatedMemoryGB = estimatedMemoryBytes / (1024L * 1024L * 1024L);
        
        // Determine strategy based on dataset size vs available RAM
        bool fitsInMemory = estimatedMemoryBytes < _maxMemoryBudgetBytes;
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
            Logger.Log("[Simulator] MEMORY-CONSTRAINED MODE: Dataset exceeds RAM - aggressive chunking with LRU offloading");
            _params.UseChunkedProcessing = true;
            _params.EnableOffloading = true;
            
            // Calculate how many chunks we can keep in memory
            long chunkMemory = (_params.Width * _params.Height * 32 * bytesPerVoxel); // 32 slices per chunk
            _maxLoadedChunks = Math.Max(3, (int)(_maxMemoryBudgetBytes / chunkMemory));
            Logger.Log($"[Simulator] Will keep max {_maxLoadedChunks} chunks in memory (LRU cache)");
        }
        
        // Calculate optimal chunk size
        long availableMemoryForChunks = fitsInMemory 
            ? _maxMemoryBudgetBytes 
            : _maxMemoryBudgetBytes / 2; // Leave room for other data
        
        long voxelsPerChunk = availableMemoryForChunks / (bytesPerVoxel * (_isHugeDataset ? 4 : 1));
        int slicesPerChunk = Math.Max(1, (int)(voxelsPerChunk / (_params.Width * _params.Height)));
        
        // For huge datasets, cap chunk size for better granularity
        if (_isHugeDataset && !fitsInMemory)
            slicesPerChunk = Math.Max(1, Math.Min(slicesPerChunk, 32));
        
        Logger.Log($"[Simulator] Creating chunks with {slicesPerChunk} slices each");
        
        for (int z = 0; z < _params.Depth; z += slicesPerChunk)
        {
            int depth = Math.Min(slicesPerChunk, _params.Depth - z);
            _chunks.Add(new WaveFieldChunk(z, depth, _params.Width, _params.Height));
        }
        
        Logger.Log($"[Simulator] Created {_chunks.Count} chunks for volume {_params.Width}x{_params.Height}x{_params.Depth}");
        Logger.Log($"[Simulator] Est. memory per chunk: {slicesPerChunk * _params.Width * _params.Height * bytesPerVoxel / (1024 * 1024)} MB");
    }

    private void InitializeFields()
    {
        // Initialize max velocity tracking for export
        _maxVelocityMagnitude = new float[_params.Width, _params.Height, _params.Depth];
        Logger.Log($"[Simulator] Allocated max velocity tracking array: {(_params.Width * _params.Height * _params.Depth * sizeof(float)) / (1024 * 1024)} MB");
        
        // Initialize damage field if brittle model is enabled
        if (_params.UseBrittleModel)
        {
            _damageField = new float[_params.Width, _params.Height, _params.Depth];
            Logger.Log("[Simulator] Damage field initialized");
        }
        
        // Initialize time series list if requested
        if (_params.SaveTimeSeries)
        {
            _timeSeriesSnapshots = new List<WaveFieldSnapshot>();
            Logger.Log($"[Simulator] Time series snapshots enabled (interval: {_params.SnapshotInterval})");
        }
        
        // Load or initialize first chunk
        LoadChunk(_chunks[0]);
        Logger.Log("[Simulator] First chunk loaded and ready");
    }

    private void ApplyInitialSource()
    {
        var chunk = _chunks[0];
        int txX = (int)(_params.TxPosition.X * _params.Width);
        int txY = (int)(_params.TxPosition.Y * _params.Height);
        int txZ = (int)(_params.TxPosition.Z * _params.Depth);
        
        Logger.Log($"[Source] Applying initial source at voxel position ({txX}, {txY}, {txZ})");
        Logger.Log($"[Source] Normalized position: ({_params.TxPosition.X:F3}, {_params.TxPosition.Y:F3}, {_params.TxPosition.Z:F3})");
        Logger.Log($"[Source] Mode: {(_params.UseFullFaceTransducers ? "Full-Face Transducer" : "Point Source")}");
        Logger.Log($"[Source] Wavelet: {(_params.UseRickerWavelet ? "Ricker" : "Sinusoidal")}");
        Logger.Log($"[Source] Energy: {_params.SourceEnergyJ} J, Frequency: {_params.SourceFrequencyKHz} kHz");
        
        if (_params.UseFullFaceTransducers)
        {
            ApplyFullFaceSource(chunk, txX, txY, txZ);
        }
        else
        {
            ApplyPointSource(chunk, txX, txY, txZ);
        }
        
        // Calculate baseline amplitude at receiver for wave detection
        int rxX = (int)(_params.RxPosition.X * _params.Width);
        int rxY = (int)(_params.RxPosition.Y * _params.Height);
        int rxZ = (int)(_params.RxPosition.Z * _params.Depth);
        _baselineAmplitude = CalculateAmplitudeAt(chunk, rxX, rxY, rxZ);
        
        Logger.Log($"[Source] Initial source applied successfully");
        Logger.Log($"[Receiver] Position: voxel ({rxX}, {rxY}, {rxZ}), baseline amplitude: {_baselineAmplitude:E3}");
        
        // Calculate expected travel distance
        float dx = (rxX - txX) * _params.PixelSize;
        float dy = (rxY - txY) * _params.PixelSize;
        float dz = (rxZ - txZ) * _params.PixelSize;
        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        Logger.Log($"[Source] TX-RX distance: {distance * 1000:F2} mm");
    }

    private void ApplyPointSource(WaveFieldChunk chunk, int x, int y, int z)
    {
        if (z < chunk.StartZ || z >= chunk.StartZ + chunk.Depth)
            return;
        
        int localZ = z - chunk.StartZ;
        
        // Calculate amplitude from source energy
        // Energy density = 0.5 * ρ * v² * Volume
        // Solving for v: v = sqrt(2 * Energy / (ρ * Volume))
        float density = _densityVolume?[x, y, z] ?? 2700f;
        float voxelVolume = MathF.Pow(_params.PixelSize, 3); // m³
        
        // Use SourceEnergyJ to determine initial amplitude
        float energyPerVoxel = _params.SourceEnergyJ / (voxelVolume * 1e9f); // Scale for voxel
        float amplitude = MathF.Sqrt(2f * energyPerVoxel / density);
        
        // Apply amplitude scaling factor from UI
        amplitude *= _params.SourceAmplitude / 100f;
        
        if (_params.UseRickerWavelet)
        {
            float freq = _params.SourceFrequencyKHz * 1000f;
            float t = 1.0f / freq; // Peak time
            amplitude *= RickerWavelet(0, freq, t);
        }
        
        // Apply to velocity field based on axis
        switch (_params.Axis)
        {
            case 0: chunk.Vx[x, y, localZ] = amplitude; break;
            case 1: chunk.Vy[x, y, localZ] = amplitude; break;
            case 2: chunk.Vz[x, y, localZ] = amplitude; break;
        }
    }

    private void ApplyFullFaceSource(WaveFieldChunk chunk, int txX, int txY, int txZ)
    {
        // Calculate amplitude from source energy distributed over face
        int faceArea = _params.Axis switch
        {
            0 => _params.Height * _params.Depth,
            1 => _params.Width * _params.Depth,
            2 => _params.Width * _params.Height,
            _ => 1
        };
        
        float density = 2700f; // Average density
        float voxelVolume = MathF.Pow(_params.PixelSize, 3);
        float totalVolume = faceArea * voxelVolume;
        
        // Energy per unit area
        float energyPerVoxel = _params.SourceEnergyJ / faceArea;
        float amplitude = MathF.Sqrt(2f * energyPerVoxel / (density * voxelVolume));
        amplitude *= _params.SourceAmplitude / 100f;
        
        if (_params.UseRickerWavelet)
        {
            float freq = _params.SourceFrequencyKHz * 1000f;
            float t = 1.0f / freq;
            amplitude *= RickerWavelet(0, freq, t);
        }
        
        // Apply to entire face
        switch (_params.Axis)
        {
            case 0: // YZ face
                for (int y = 0; y < _params.Height; y++)
                for (int z = 0; z < chunk.Depth; z++)
                    if (txX >= 0 && txX < _params.Width)
                        chunk.Vx[txX, y, z] = amplitude;
                break;
            case 1: // XZ face
                for (int x = 0; x < _params.Width; x++)
                for (int z = 0; z < chunk.Depth; z++)
                    if (txY >= 0 && txY < _params.Height)
                        chunk.Vy[x, txY, z] = amplitude;
                break;
            case 2: // XY face
                if (txZ >= chunk.StartZ && txZ < chunk.StartZ + chunk.Depth)
                {
                    int localZ = txZ - chunk.StartZ;
                    for (int x = 0; x < _params.Width; x++)
                    for (int y = 0; y < _params.Height; y++)
                        chunk.Vz[x, y, localZ] = amplitude;
                }
                break;
        }
    }

    private float RickerWavelet(float t, float freq, float tpeak)
    {
        float arg = MathF.PI * freq * (t - tpeak);
        float arg2 = arg * arg;
        return (1f - 2f * arg2) * MathF.Exp(-arg2);
    }

    private async Task ProcessTimeStepAsync(CancellationToken ct)
    {
        for (int chunkIdx = 0; chunkIdx < _chunks.Count; chunkIdx++)
        {
            ct.ThrowIfCancellationRequested();
            
            var chunk = _chunks[chunkIdx];
            
            // Load chunk if offloaded (using LRU cache)
            if (chunk.IsOffloaded)
            {
                LoadChunkWithLRU(chunkIdx);
            }
            else
            {
                // Track access for LRU
                TrackChunkAccess(chunkIdx);
            }
            
            // Get material properties for this chunk
            var (E, nu, rho) = ExtractChunkMaterialProperties(chunk);
            
            // Run physics kernel (only if elastic model enabled)
            if (_params.UseElasticModel)
            {
                _kernel.UpdateWaveField(
                    chunk.Vx, chunk.Vy, chunk.Vz,
                    chunk.Sxx, chunk.Syy, chunk.Szz,
                    chunk.Sxy, chunk.Sxz, chunk.Syz,
                    E, nu, rho,
                    _params.TimeStepSeconds,
                    _params.PixelSize,
                    _params.ArtificialDampingFactor);
            }
            
            // Apply physics models
            if (_params.UsePlasticModel)
                ApplyPlasticity(chunk, E, nu, rho);
            
            if (_params.UseBrittleModel)
                ApplyDamage(chunk, E, nu);
            
            // Update max velocity tracking
            UpdateMaxVelocity(chunk);
            
            // Apply boundary conditions
            ApplyBoundaryConditions(chunk);
            
            // Apply source at current time
            if (_currentStep < 100) // Source duration
                ApplyContinuousSource(chunk, _currentStep);
            
            // Notify for visualization (throttled for huge datasets)
            bool shouldVisualize = _params.EnableRealTimeVisualization && 
                                   (!_isHugeDataset || chunkIdx % Math.Max(1, _chunks.Count / 8) == 0);
            
            if (shouldVisualize)
            {
                NotifyWaveFieldUpdate(chunk, _currentStep);
            }
            
            // Smart LRU-based offloading (only if memory constrained)
            if (_params.EnableOffloading && _currentMemoryUsageBytes > _maxMemoryBudgetBytes)
            {
                EvictLRUChunksIfNeeded();
            }
        }
        
        // Log wave activity periodically
        if (_currentStep % 100 == 0)
        {
            LogWaveActivity();
        }
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
        int loadedCount = _chunks.Count(c => !c.IsOffloaded);
        if (loadedCount >= _maxLoadedChunks)
        {
            EvictLRUChunksIfNeeded();
        }
        
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
                
                if (_isHugeDataset && _currentStep % 50 == 0)
                    Logger.Log($"[Memory] Evicted chunk {lruChunkIdx} (LRU), current usage: {_currentMemoryUsageBytes / (1024 * 1024)} MB");
            }
        }
    }

    private (float[,,] E, float[,,] nu, float[,,] rho) ExtractChunkMaterialProperties(WaveFieldChunk chunk)
    {
        var E = new float[_params.Width, _params.Height, chunk.Depth];
        var nu = new float[_params.Width, _params.Height, chunk.Depth];
        var rho = new float[_params.Width, _params.Height, chunk.Depth];
        
        Parallel.For(0, chunk.Depth, z =>
        {
            int globalZ = chunk.StartZ + z;
            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
            {
                if (_youngsModulusVolume != null && _poissonRatioVolume != null)
                {
                    E[x, y, z] = _youngsModulusVolume[x, y, globalZ] * 1e6f; // MPa to Pa
                    nu[x, y, z] = _poissonRatioVolume[x, y, globalZ];
                }
                else
                {
                    E[x, y, z] = _params.YoungsModulusMPa * 1e6f;
                    nu[x, y, z] = _params.PoissonRatio;
                }
                
                rho[x, y, z] = _densityVolume?[x, y, globalZ] ?? 2700f;
            }
        });
        
        return (E, nu, rho);
    }

    private void ApplyPlasticity(WaveFieldChunk chunk, float[,,] E, float[,,] nu, float[,,] rho)
    {
        float confiningStress = _params.ConfiningPressureMPa * 1e6f;
        float cohesion = _params.CohesionMPa * 1e6f;
        float frictionAngle = _params.FailureAngleDeg * MathF.PI / 180f;
        float sinPhi = MathF.Sin(frictionAngle);
        
        Parallel.For(0, chunk.Depth, z =>
        {
            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
            {
                float sxx = chunk.Sxx[x, y, z];
                float syy = chunk.Syy[x, y, z];
                float szz = chunk.Szz[x, y, z];
                
                // Mean stress
                float p = -(sxx + syy + szz) / 3f + confiningStress;
                
                // Deviatoric stress
                float sxx_dev = sxx + p;
                float syy_dev = syy + p;
                float szz_dev = szz + p;
                
                float q = MathF.Sqrt(1.5f * (sxx_dev * sxx_dev + syy_dev * syy_dev + szz_dev * szz_dev));
                
                // Mohr-Coulomb yield function
                float F = q - (cohesion + p * sinPhi);
                
                if (F > 0)
                {
                    // Plastic correction
                    float factor = (cohesion + p * sinPhi) / (q + 1e-10f);
                    chunk.Sxx[x, y, z] = sxx_dev * factor - p;
                    chunk.Syy[x, y, z] = syy_dev * factor - p;
                    chunk.Szz[x, y, z] = szz_dev * factor - p;
                }
            }
        });
    }

    private void ApplyDamage(WaveFieldChunk chunk, float[,,] E, float[,,] nu)
    {
        float tensileStrength = 10e6f; // 10 MPa typical
        
        Parallel.For(0, chunk.Depth, z =>
        {
            int globalZ = chunk.StartZ + z;
            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
            {
                float sxx = chunk.Sxx[x, y, z];
                float syy = chunk.Syy[x, y, z];
                float szz = chunk.Szz[x, y, z];
                
                // Maximum principal stress
                float maxStress = Math.Max(sxx, Math.Max(syy, szz));
                
                if (maxStress > tensileStrength && _damageField != null)
                {
                    float damage = (maxStress - tensileStrength) / (tensileStrength * 10f);
                    _damageField[x, y, globalZ] = Math.Min(1f, _damageField[x, y, globalZ] + damage * 0.01f);
                    
                    // Reduce stiffness
                    float reduction = 1f - _damageField[x, y, globalZ] * 0.5f;
                    E[x, y, z] *= reduction;
                }
            }
        });
    }

    private void ApplyBoundaryConditions(WaveFieldChunk chunk)
    {
        // Absorbing boundary conditions (simple damping)
        float damping = 0.95f;
        int boundary = 3;
        
        // X boundaries
        for (int z = 0; z < chunk.Depth; z++)
        for (int y = 0; y < _params.Height; y++)
        for (int b = 0; b < boundary; b++)
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
        for (int z = 0; z < chunk.Depth; z++)
        for (int x = 0; x < _params.Width; x++)
        for (int b = 0; b < boundary; b++)
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
        {
            for (int z = 0; z < Math.Min(boundary, chunk.Depth); z++)
            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
            {
                chunk.Vx[x, y, z] *= damping;
                chunk.Vy[x, y, z] *= damping;
                chunk.Vz[x, y, z] *= damping;
            }
        }
        
        if (chunk.StartZ + chunk.Depth > _params.Depth - boundary)
        {
            int startZ = Math.Max(0, _params.Depth - boundary - chunk.StartZ);
            for (int z = startZ; z < chunk.Depth; z++)
            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
            {
                chunk.Vx[x, y, z] *= damping;
                chunk.Vy[x, y, z] *= damping;
                chunk.Vz[x, y, z] *= damping;
            }
        }
    }

    private void ApplyContinuousSource(WaveFieldChunk chunk, int timeStep)
    {
        int txX = (int)(_params.TxPosition.X * _params.Width);
        int txY = (int)(_params.TxPosition.Y * _params.Height);
        int txZ = (int)(_params.TxPosition.Z * _params.Depth);
        
        if (txZ < chunk.StartZ || txZ >= chunk.StartZ + chunk.Depth)
            return;
        
        float t = timeStep * _params.TimeStepSeconds;
        float freq = _params.SourceFrequencyKHz * 1000f;
        float tpeak = 1.0f / freq;
        
        // Calculate amplitude from energy
        float density = _densityVolume?[txX, txY, txZ] ?? 2700f;
        float voxelVolume = MathF.Pow(_params.PixelSize, 3);
        float energyPerVoxel = _params.SourceEnergyJ / (voxelVolume * 1e9f);
        float amplitude = MathF.Sqrt(2f * energyPerVoxel / density);
        amplitude *= _params.SourceAmplitude / 100f;
        
        if (_params.UseRickerWavelet)
            amplitude *= RickerWavelet(t, freq, tpeak);
        else
            amplitude *= MathF.Sin(2f * MathF.PI * freq * t);
        
        int localZ = txZ - chunk.StartZ;
        
        if (_params.UseFullFaceTransducers)
        {
            int faceArea = _params.Axis switch
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
                    for (int y = 0; y < _params.Height; y++)
                    for (int z = 0; z < chunk.Depth; z++)
                        chunk.Vx[txX, y, z] += amplitude;
                    break;
                case 1:
                    for (int x = 0; x < _params.Width; x++)
                    for (int z = 0; z < chunk.Depth; z++)
                        chunk.Vy[x, txY, z] += amplitude;
                    break;
                case 2:
                    for (int x = 0; x < _params.Width; x++)
                    for (int y = 0; y < _params.Height; y++)
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
        
        int localZ = z - chunk.StartZ;
        float vx = chunk.Vx[x, y, localZ];
        float vy = chunk.Vy[x, y, localZ];
        float vz = chunk.Vz[x, y, localZ];
        return MathF.Sqrt(vx * vx + vy * vy + vz * vz);
    }

    private void UpdateMaxVelocity(WaveFieldChunk chunk)
    {
        // Track maximum velocity magnitude at each voxel for final export
        Parallel.For(0, chunk.Depth, z =>
        {
            int globalZ = chunk.StartZ + z;
            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
            {
                float vx = chunk.Vx[x, y, z];
                float vy = chunk.Vy[x, y, z];
                float vz = chunk.Vz[x, y, z];
                float magnitude = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
                
                // Update max if current is larger
                if (magnitude > _maxVelocityMagnitude[x, y, globalZ])
                    _maxVelocityMagnitude[x, y, globalZ] = magnitude;
            }
        });
    }

    private void LogWaveActivity()
    {
        // Log wave propagation statistics for debugging
        int zeroVoxels = 0;
        int activeVoxels = 0;
        float maxVel = 0;
        float avgVel = 0;
        int totalVoxels = 0;
        
        const float ACTIVE_THRESHOLD = 1e-8f;
        
        foreach (var chunk in _chunks)
        {
            if (chunk.IsOffloaded) continue;
            
            for (int z = 0; z < chunk.Depth; z++)
            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
            {
                float vx = chunk.Vx[x, y, z];
                float vy = chunk.Vy[x, y, z];
                float vz = chunk.Vz[x, y, z];
                float mag = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
                
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
        
        Logger.Log($"[Wave Activity] Step {_currentStep}: Active={activeVoxels} ({100f * activeVoxels / totalVoxels:F2}%), " +
                   $"Zero={zeroVoxels} ({100f * zeroVoxels / totalVoxels:F2}%), " +
                   $"MaxVel={maxVel:E3}, AvgVel={avgVel:E3}");
    }

    private void DetectWaveArrivals(int timeStep)
    {
        // Find receiver chunk
        int rxZ = (int)(_params.RxPosition.Z * _params.Depth);
        var rxChunk = _chunks.FirstOrDefault(c => rxZ >= c.StartZ && rxZ < c.StartZ + c.Depth);
        
        if (rxChunk == null || rxChunk.IsOffloaded)
            return;
        
        int rxX = (int)(_params.RxPosition.X * _params.Width);
        int rxY = (int)(_params.RxPosition.Y * _params.Height);
        
        float amplitude = CalculateAmplitudeAt(rxChunk, rxX, rxY, rxZ);
        
        // Log amplitude periodically for debugging
        if (timeStep % 50 == 0 && amplitude > 1e-15f)
        {
            Logger.Log($"[Wave Detection] Step {timeStep}: RX amplitude = {amplitude:E3} (threshold: {_baselineAmplitude + ARRIVAL_THRESHOLD:E3})");
        }
        
        // Detect P-wave (first arrival above threshold)
        if (_pWaveArrivalTime < 0 && amplitude > _baselineAmplitude + ARRIVAL_THRESHOLD)
        {
            _pWaveArrivalTime = timeStep;
            float travelTime = timeStep * _params.TimeStepSeconds * 1e6f; // microseconds
            Logger.Log($"[Wave Detection] P-WAVE ARRIVAL at step {timeStep} ({travelTime:F2} µs)");
            Logger.Log($"[Wave Detection] P-wave amplitude: {amplitude:E3}");
        }
        
        // Detect S-wave (secondary arrival with different characteristics)
        if (_pWaveArrivalTime > 0 && _sWaveArrivalTime < 0 && timeStep > _pWaveArrivalTime + 50)
        {
            // Look for transverse motion
            float transverse = MathF.Abs(rxChunk.Vy[rxX, rxY, rxZ - rxChunk.StartZ]);
            if (transverse > ARRIVAL_THRESHOLD * 0.5f)
            {
                _sWaveArrivalTime = timeStep;
                float travelTime = timeStep * _params.TimeStepSeconds * 1e6f; // microseconds
                Logger.Log($"[Wave Detection] S-WAVE ARRIVAL at step {timeStep} ({travelTime:F2} µs)");
                Logger.Log($"[Wave Detection] S-wave transverse amplitude: {transverse:E3}");
                Logger.Log($"[Wave Detection] Travel time difference: {(_sWaveArrivalTime - _pWaveArrivalTime) * _params.TimeStepSeconds * 1e6f:F2} µs");
            }
        }
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
            
            for (int z = 0; z < chunk.Depth; z++)
            {
                int globalZ = chunk.StartZ + z;
                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                    float vx = chunk.Vx[x, y, z];
                    float vy = chunk.Vy[x, y, z];
                    float vz = chunk.Vz[x, y, z];
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
            Step = _currentStep,
            Message = $"Step {_currentStep}/{_params.TimeSteps}"
        });
    }

    private void UpdateMemoryUsage()
    {
        _currentMemoryUsageBytes = _chunks.Where(c => !c.IsOffloaded).Sum(c => c.MemorySize);
        CurrentMemoryUsageMB = _currentMemoryUsageBytes / (1024f * 1024f);
        
        int loadedChunks = _chunks.Count(c => !c.IsOffloaded);
        
        if (_isHugeDataset || loadedChunks < _chunks.Count)
        {
            Logger.Log($"[Memory] Current: {CurrentMemoryUsageMB:F0} MB ({loadedChunks}/{_chunks.Count} chunks), " +
                      $"Budget: {_maxMemoryBudgetBytes / (1024 * 1024)} MB, " +
                      $"Usage: {100f * _currentMemoryUsageBytes / _maxMemoryBudgetBytes:F1}%");
        }
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
        
        string chunkFile = Path.Combine(_offloadPath, $"chunk_{chunk.StartZ}.dat");
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
        
        string chunkFile = Path.Combine(_offloadPath, $"chunk_{chunk.StartZ}.dat");
        chunk.SaveToFile(chunkFile);
        chunk.Dispose();
        chunk.IsOffloaded = true;
    }

    private async Task<SimulationResults> AssembleResultsAsync(CancellationToken ct)
    {
        Logger.Log("[Simulator] Assembling final results...");
        
        // For huge datasets, avoid loading all chunks - use max velocity field
        if (_isHugeDataset && _currentMemoryUsageBytes > _maxMemoryBudgetBytes / 2)
        {
            Logger.Log("[Simulator] Using max velocity field for huge dataset (avoiding full load)");
            
            // Calculate velocities from arrival times
            double pVelocity = CalculateVelocity(_pWaveArrivalTime);
            double sVelocity = CalculateVelocity(_sWaveArrivalTime);
            
            // Use max velocity as final wave field
            var results = new SimulationResults
            {
                WaveFieldVx = _maxVelocityMagnitude, // Combined into single field
                WaveFieldVy = new float[_params.Width, _params.Height, _params.Depth],
                WaveFieldVz = new float[_params.Width, _params.Height, _params.Depth],
                PWaveVelocity = pVelocity,
                SWaveVelocity = sVelocity,
                VpVsRatio = sVelocity > 0 ? pVelocity / sVelocity : 0,
                PWaveTravelTime = _pWaveArrivalTime,
                SWaveTravelTime = _sWaveArrivalTime,
                TotalTimeSteps = _currentStep,
                ComputationTime = _stopwatch.Elapsed,
                DamageField = _damageField,
                TimeSeriesSnapshots = _timeSeriesSnapshots,
                Context = _materialLabels
            };
            
            Logger.Log($"[Simulator] Results: Vp={pVelocity:F2} m/s, Vs={sVelocity:F2} m/s, Vp/Vs={results.VpVsRatio:F3}");
            Logger.Log($"[Simulator] Max velocity range: {_maxVelocityMagnitude.Cast<float>().Min():E3} to {_maxVelocityMagnitude.Cast<float>().Max():E3} m/s");
            
            return results;
        }
        
        // For normal/medium datasets, assemble full fields
        Logger.Log("[Simulator] Assembling full wave fields...");
        
        // Check if all chunks are already loaded (FAST PATH)
        int offloadedCount = _chunks.Count(c => c.IsOffloaded);
        if (offloadedCount > 0)
        {
            Logger.Log($"[Simulator] Loading {offloadedCount} offloaded chunks...");
            for (int i = 0; i < _chunks.Count; i++)
            {
                if (_chunks[i].IsOffloaded)
                {
                    LoadChunk(_chunks[i]);
                    if (i % 10 == 0)
                        Logger.Log($"[Simulator] Loaded chunk {i + 1}/{offloadedCount}");
                }
            }
        }
        else
        {
            Logger.Log("[Simulator] All chunks already in memory (FAST PATH)");
        }
        
        // Create final wave field volumes
        var vx = new float[_params.Width, _params.Height, _params.Depth];
        var vy = new float[_params.Width, _params.Height, _params.Depth];
        var vz = new float[_params.Width, _params.Height, _params.Depth];
        
        await Task.Run(() =>
        {
            Logger.Log("[Simulator] Copying chunk data to final arrays...");
            Parallel.ForEach(_chunks, chunk =>
            {
                for (int z = 0; z < chunk.Depth; z++)
                {
                    int globalZ = chunk.StartZ + z;
                    for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                    {
                        vx[x, y, globalZ] = chunk.Vx[x, y, z];
                        vy[x, y, globalZ] = chunk.Vy[x, y, z];
                        vz[x, y, globalZ] = chunk.Vz[x, y, z];
                    }
                }
            });
        }, ct);
        
        // Calculate velocities from arrival times
        double pWaveVelocity = CalculateVelocity(_pWaveArrivalTime);
        double sWaveVelocity = CalculateVelocity(_sWaveArrivalTime);
        
        var finalResults = new SimulationResults
        {
            WaveFieldVx = vx,
            WaveFieldVy = vy,
            WaveFieldVz = vz,
            PWaveVelocity = pWaveVelocity,
            SWaveVelocity = sWaveVelocity,
            VpVsRatio = sWaveVelocity > 0 ? pWaveVelocity / sWaveVelocity : 0,
            PWaveTravelTime = _pWaveArrivalTime,
            SWaveTravelTime = _sWaveArrivalTime,
            TotalTimeSteps = _currentStep,
            ComputationTime = _stopwatch.Elapsed,
            DamageField = _damageField,
            TimeSeriesSnapshots = _timeSeriesSnapshots,
            Context = _materialLabels
        };
        
        Logger.Log($"[Simulator] Results: Vp={pWaveVelocity:F2} m/s, Vs={sWaveVelocity:F2} m/s, Vp/Vs={finalResults.VpVsRatio:F3}");
        Logger.Log($"[Simulator] Active voxels in final field: {CountActiveVoxels(vx, vy, vz)}");
        
        return finalResults;
    }
    
    private int CountActiveVoxels(float[,,] vx, float[,,] vy, float[,,] vz)
    {
        int count = 0;
        const float threshold = 1e-10f;
        
        Parallel.For(0, _params.Depth, z =>
        {
            int localCount = 0;
            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
            {
                float mag = MathF.Sqrt(vx[x, y, z] * vx[x, y, z] + 
                                      vy[x, y, z] * vy[x, y, z] + 
                                      vz[x, y, z] * vz[x, y, z]);
                if (mag > threshold)
                    localCount++;
            }
            System.Threading.Interlocked.Add(ref count, localCount);
        });
        
        return count;
    }

    private double CalculateVelocity(int arrivalTimeStep)
    {
        if (arrivalTimeStep <= 0)
            return 0;
        
        // Calculate distance
        float dx = (_params.RxPosition.X - _params.TxPosition.X) * _params.Width * _params.PixelSize;
        float dy = (_params.RxPosition.Y - _params.TxPosition.Y) * _params.Height * _params.PixelSize;
        float dz = (_params.RxPosition.Z - _params.TxPosition.Z) * _params.Depth * _params.PixelSize;
        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        
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
        {
            try
            {
                Directory.Delete(_offloadPath, true);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    public void Dispose()
    {
        Cleanup();
    }
    
    /// <summary>
    ///     Clears the offload cache and resets LRU state. Can only be called when not simulating.
    /// </summary>
    public bool ClearOffloadCache()
    {
        // Don't allow clearing during simulation
        if (_isSimulating)
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
            {
                if (chunk.IsOffloaded)
                {
                    chunk.IsOffloaded = false;
                    chunk.Initialize(); // Re-initialize empty chunk
                }
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
        int total = _chunks.Count;
        int loaded = _chunks.Count(c => !c.IsOffloaded);
        int offloaded = _chunks.Count(c => c.IsOffloaded);
        
        long cacheSize = 0;
        if (!string.IsNullOrEmpty(_offloadPath) && Directory.Exists(_offloadPath))
        {
            try
            {
                var files = Directory.GetFiles(_offloadPath, "*.dat");
                cacheSize = files.Sum(f => new FileInfo(f).Length);
            }
            catch { /* Ignore errors */ }
        }
        
        return (total, loaded, offloaded, cacheSize);
    }
}