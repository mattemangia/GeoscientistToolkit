// GeoscientistToolkit/AddIns/AcousticSimulation/AcousticSimulationAddIn.cs (FIXED)

using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.AddIns.AcousticSimulation;

/// <summary>
///     Acoustic wave propagation simulation add-in for CT volume data.
///     Implements full elastodynamic physics with unified CPU/GPU compute via Silk.NET.
/// </summary>
public class AcousticSimulationAddIn : IAddIn
{
    private static AcousticSimulationTool _tool;
    public string Id => "com.geoscientisttoolkit.acousticsimulation";
    public string Name => "Acoustic Wave Simulation";
    public string Version => "2.0.0";
    public string Author => "GeoscientistToolkit Team";

    public string Description =>
        "Advanced acoustic/elastic wave propagation simulation with Vp/Vs analysis and real-time visualization.";

    public void Initialize()
    {
        _tool = new AcousticSimulationTool();
        Logger.Log($"[AcousticSimulation] Add-in initialized (v{Version})");
    }

    public void Shutdown()
    {
        _tool?.Dispose();
        _tool = null;
        Logger.Log("[AcousticSimulation] Add-in shutdown");
    }

    public IEnumerable<AddInMenuItem> GetMenuItems()
    {
        return null;
    }

    public IEnumerable<AddInTool> GetTools()
    {
        return new[] { _tool };
    }

    public IEnumerable<IDataImporter> GetDataImporters()
    {
        return null;
    }

    public IEnumerable<IDataExporter> GetDataExporters()
    {
        return null;
    }
}

#region Unified Simulator

/// <summary>
///     Unified acoustic simulator that can run on CPU or GPU using Silk.NET OpenCL
/// </summary>
internal class UnifiedAcousticSimulator : IDisposable
{
    // Physical constants
    private readonly float _lambda;
    private readonly float _mu;
    private readonly SimulationParameters _params;
    private readonly nint[] _stressBuffers = new nint[6]; // sxx, syy, szz, sxy, sxz, syz
    private readonly int _totalCells;
    private readonly nint[] _velocityBuffers = new nint[3]; // vx, vy, vz
    private CL _cl;
    private nint _commandQueue;
    private nint _context;
    private float[,,] _damage;
    private nint _damageBuffer;
    private nint _densityBuffer;
    private nint _device;
    private float _dt;

    // Buffers
    private nint _materialBuffer;
    private nint _program;
    private float[,,] _rho;
    private float[,,] _sxx, _syy, _szz, _sxy, _sxz, _syz;
    private bool _useGPU;

    // CPU fallback arrays
    private float[,,] _vx, _vy, _vz;

    public UnifiedAcousticSimulator(SimulationParameters parameters)
    {
        _params = parameters;

        // Calculate Lamé constants
        var E = _params.YoungsModulusMPa * 1e6f; // Convert to Pa
        _mu = E / (2.0f * (1.0f + _params.PoissonRatio));
        _lambda = E * _params.PoissonRatio / ((1 + _params.PoissonRatio) * (1 - 2 * _params.PoissonRatio));

        _totalCells = _params.Width * _params.Height * _params.Depth;

        // Initialize compute backend
        InitializeCompute();
    }

    // Progress tracking
    public float Progress { get; private set; }
    public int CurrentStep { get; private set; }

    public void Dispose()
    {
        if (_useGPU && _cl != null)
        {
            _cl.ReleaseMemObject(_materialBuffer);
            _cl.ReleaseMemObject(_densityBuffer);

            for (var i = 0; i < 3; i++)
                if (_velocityBuffers[i] != 0)
                    _cl.ReleaseMemObject(_velocityBuffers[i]);

            for (var i = 0; i < 6; i++)
                if (_stressBuffers[i] != 0)
                    _cl.ReleaseMemObject(_stressBuffers[i]);

            if (_damageBuffer != 0)
                _cl.ReleaseMemObject(_damageBuffer);

            if (_program != 0)
                _cl.ReleaseProgram(_program);

            if (_commandQueue != 0)
                _cl.ReleaseCommandQueue(_commandQueue);

            if (_context != 0)
                _cl.ReleaseContext(_context);
        }
    }

    public event EventHandler<SimulationProgressEventArgs> ProgressUpdated;
    public event EventHandler<WaveFieldUpdateEventArgs> WaveFieldUpdated;

    private void InitializeCompute()
    {
        try
        {
            if (_params.UseGPU)
            {
                _cl = CL.GetApi();
                InitializeOpenCL();
                _useGPU = true;
                Logger.Log("[AcousticSimulator] Using GPU acceleration via OpenCL");
            }
            else
            {
                InitializeCPUArrays();
                _useGPU = false;
                Logger.Log("[AcousticSimulator] Using CPU computation");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[AcousticSimulator] Failed to initialize GPU, falling back to CPU: {ex.Message}");
            InitializeCPUArrays();
            _useGPU = false;
        }
    }

    private unsafe void InitializeOpenCL()
    {
        // Use centralized device manager to get the device from settings
        _device = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetComputeDevice();

        if (_device == 0)
        {
            Logger.LogWarning("No OpenCL device available from OpenCLDeviceManager.");
            throw new Exception("No OpenCL device available.");
        }

        var deviceInfo = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetDeviceInfo();
        Logger.Log($"[AcousticSimulator]: Using device: {deviceInfo.Name} ({deviceInfo.Vendor})");

        // Create context
        var errNum = 0;
        var device = _device; // Local copy
        var devicePtr = &device; // Take address directly without fixed
        _context = _cl.CreateContext(null, 1, devicePtr, null, null, &errNum);

        if (errNum != 0)
            throw new Exception($"Failed to create context: {errNum}");

        // Create command queue - explicitly cast to CommandQueueProperties to resolve ambiguity
        _commandQueue = _cl.CreateCommandQueue(_context, _device, (CommandQueueProperties)0, &errNum);
        if (errNum != 0)
            throw new Exception($"Failed to create command queue: {errNum}");

        // Build OpenCL program
        BuildOpenCLProgram();

        // Create buffers
        CreateOpenCLBuffers();
    }


    private void InitializeCPUArrays()
    {
        int w = _params.Width, h = _params.Height, d = _params.Depth;

        _vx = new float[w, h, d];
        _vy = new float[w, h, d];
        _vz = new float[w, h, d];
        _sxx = new float[w, h, d];
        _syy = new float[w, h, d];
        _szz = new float[w, h, d];
        _sxy = new float[w, h, d];
        _sxz = new float[w, h, d];
        _syz = new float[w, h, d];
        _damage = new float[w, h, d];
        _rho = new float[w, h, d];
    }

    private unsafe void BuildOpenCLProgram()
    {
        var kernelSource = GetOpenCLKernelSource();
        var sourceBytes = Encoding.ASCII.GetBytes(kernelSource);

        fixed (byte* sourcePtr = sourceBytes)
        {
            var sourcePtrAddr = new IntPtr(sourcePtr);
            var length = (nuint)sourceBytes.Length;
            var errNum = 0;

            _program = _cl.CreateProgramWithSource(_context, 1, (byte**)&sourcePtrAddr, &length, &errNum);
            if (errNum != 0)
                throw new Exception($"Failed to create program: {errNum}");

            // Build program - take address of device directly and cast null to resolve ambiguity
            var device = _device;
            var devicePtr = &device; // Take address directly without fixed
            var buildStatus = _cl.BuildProgram(_program, 1, devicePtr, (byte*)null, null, null);

            if (buildStatus != 0)
            {
                // Get build log
                nuint logSize = 0;
                _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);

                if (logSize > 0)
                {
                    var log = stackalloc byte[(int)logSize];
                    _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, logSize, log, null);
                    var logStr = Marshal.PtrToStringAnsi((IntPtr)log, (int)logSize);
                    Logger.LogError($"[AcousticSimulator] OpenCL build failed: {logStr}");
                }

                throw new Exception("Failed to build OpenCL program");
            }
        }
    }

    private unsafe void CreateOpenCLBuffers()
    {
        var size = _totalCells;
        var errNum = 0;

        // Create buffers
        _materialBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly, (nuint)(size * sizeof(byte)), null, &errNum);
        _densityBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly, (nuint)(size * sizeof(float)), null, &errNum);

        for (var i = 0; i < 3; i++)
            _velocityBuffers[i] =
                _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(size * sizeof(float)), null, &errNum);

        for (var i = 0; i < 6; i++)
            _stressBuffers[i] =
                _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(size * sizeof(float)), null, &errNum);

        _damageBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(size * sizeof(float)), null, &errNum);
    }

    private string GetOpenCLKernelSource()
    {
        // Complete OpenCL kernel for acoustic simulation
        return @"
                __kernel void updateStress(
                    __global const uchar* material,
                    __global const float* density,
                    __global float* vx, __global float* vy, __global float* vz,
                    __global float* sxx, __global float* syy, __global float* szz,
                    __global float* sxy, __global float* sxz, __global float* syz,
                    __global float* damage,
                    const float lambda, const float mu,
                    const float dt, const float dx,
                    const int width, const int height, const int depth,
                    const uchar selectedMaterial)
                {
                    int idx = get_global_id(0);
                    if (idx >= width * height * depth) return;
                    
                    // Convert to 3D coordinates
                    int z = idx / (width * height);
                    int remainder = idx % (width * height);
                    int y = remainder / width;
                    int x = remainder % width;
                    
                    // Skip if not selected material or boundary
                    if (material[idx] != selectedMaterial) return;
                    if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;
                    
                    // Calculate velocity gradients
                    int xp1 = idx + 1;
                    int xm1 = idx - 1;
                    int yp1 = idx + width;
                    int ym1 = idx - width;
                    int zp1 = idx + width * height;
                    int zm1 = idx - width * height;
                    
                    float dvx_dx = (vx[xp1] - vx[xm1]) / (2.0f * dx);
                    float dvy_dy = (vy[yp1] - vy[ym1]) / (2.0f * dx);
                    float dvz_dz = (vz[zp1] - vz[zm1]) / (2.0f * dx);
                    float dvy_dx = (vy[xp1] - vy[xm1]) / (2.0f * dx);
                    float dvx_dy = (vx[yp1] - vx[ym1]) / (2.0f * dx);
                    float dvz_dx = (vz[xp1] - vz[xm1]) / (2.0f * dx);
                    float dvx_dz = (vx[zp1] - vx[zm1]) / (2.0f * dx);
                    float dvz_dy = (vz[yp1] - vz[ym1]) / (2.0f * dx);
                    float dvy_dz = (vy[zp1] - vy[zm1]) / (2.0f * dx);
                    
                    float volumetricStrain = dvx_dx + dvy_dy + dvz_dz;
                    
                    // Update stress components (Hooke's law)
                    float damping = 1.0f - damage[idx] * 0.9f;
                    sxx[idx] += dt * damping * (lambda * volumetricStrain + 2.0f * mu * dvx_dx);
                    syy[idx] += dt * damping * (lambda * volumetricStrain + 2.0f * mu * dvy_dy);
                    szz[idx] += dt * damping * (lambda * volumetricStrain + 2.0f * mu * dvz_dz);
                    sxy[idx] += dt * damping * mu * (dvy_dx + dvx_dy);
                    sxz[idx] += dt * damping * mu * (dvz_dx + dvx_dz);
                    syz[idx] += dt * damping * mu * (dvz_dy + dvy_dz);
                }

                __kernel void updateVelocity(
                    __global const uchar* material,
                    __global const float* density,
                    __global float* vx, __global float* vy, __global float* vz,
                    __global float* sxx, __global float* syy, __global float* szz,
                    __global float* sxy, __global float* sxz, __global float* syz,
                    const float dt, const float dx,
                    const int width, const int height, const int depth,
                    const uchar selectedMaterial)
                {
                    int idx = get_global_id(0);
                    if (idx >= width * height * depth) return;
                    
                    // Convert to 3D coordinates
                    int z = idx / (width * height);
                    int remainder = idx % (width * height);
                    int y = remainder / width;
                    int x = remainder % width;
                    
                    // Skip if not selected material or boundary
                    if (material[idx] != selectedMaterial) return;
                    if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;
                    
                    float rho = fmax(100.0f, density[idx]);
                    
                    // Calculate stress gradients
                    int xp1 = idx + 1;
                    int xm1 = idx - 1;
                    int yp1 = idx + width;
                    int ym1 = idx - width;
                    int zp1 = idx + width * height;
                    int zm1 = idx - width * height;
                    
                    float dsxx_dx = (sxx[xp1] - sxx[xm1]) / (2.0f * dx);
                    float dsyy_dy = (syy[yp1] - syy[ym1]) / (2.0f * dx);
                    float dszz_dz = (szz[zp1] - szz[zm1]) / (2.0f * dx);
                    float dsxy_dy = (sxy[yp1] - sxy[ym1]) / (2.0f * dx);
                    float dsxy_dx = (sxy[xp1] - sxy[xm1]) / (2.0f * dx);
                    float dsxz_dz = (sxz[zp1] - sxz[zm1]) / (2.0f * dx);
                    float dsxz_dx = (sxz[xp1] - sxz[xm1]) / (2.0f * dx);
                    float dsyz_dz = (syz[zp1] - syz[zm1]) / (2.0f * dx);
                    float dsyz_dy = (syz[yp1] - syz[ym1]) / (2.0f * dx);
                    
                    // Update velocity with damping
                    const float damping = 0.995f;
                    vx[idx] = vx[idx] * damping + dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
                    vy[idx] = vy[idx] * damping + dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
                    vz[idx] = vz[idx] * damping + dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
                }
            ";
    }

    public async Task<SimulationResults> RunAsync(
        byte[,,] volumeLabels,
        float[,,] densityVolume,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.Now;
        var results = new SimulationResults();

        // Calculate stable time step
        CalculateTimeStep(densityVolume);

        // Initialize fields
        ApplyInitialConditions(volumeLabels, densityVolume);

        // Time series snapshots
        if (_params.SaveTimeSeries) results.TimeSeriesSnapshots = new List<WaveFieldSnapshot>();

        // Main simulation loop
        var stepCount = 0;
        var maxSteps = _params.TimeSteps * 2; // Safety limit

        // Wave detection variables
        bool pWaveDetected = false, sWaveDetected = false;
        int pWaveStep = 0, sWaveStep = 0;

        while (stepCount < maxSteps && !cancellationToken.IsCancellationRequested)
        {
            if (_useGPU)
                await UpdateFieldsGPUAsync();
            else
                UpdateFieldsCPU(volumeLabels, densityVolume);

            stepCount++;
            CurrentStep = stepCount;

            // Save snapshot if needed
            if (_params.SaveTimeSeries && stepCount % _params.SnapshotInterval == 0)
            {
                var snapshot = new WaveFieldSnapshot
                {
                    TimeStep = stepCount,
                    SimulationTime = stepCount * _dt,
                    Width = _params.Width,
                    Height = _params.Height,
                    Depth = _params.Depth
                };
                snapshot.SetVelocityFields(_vx, _vy, _vz);
                results.TimeSeriesSnapshots.Add(snapshot);
            }

            // Real-time visualization update
            if (_params.EnableRealTimeVisualization && stepCount % 10 == 0)
                WaveFieldUpdated?.Invoke(this, new WaveFieldUpdateEventArgs
                {
                    WaveField = GetCombinedWaveField(),
                    TimeStep = stepCount,
                    SimTime = stepCount * _dt,
                    Dataset = volumeLabels
                });

            // Check for wave arrivals
            if (!pWaveDetected && CheckPWaveArrival())
            {
                pWaveDetected = true;
                pWaveStep = stepCount;
                Logger.Log($"[AcousticSimulator] P-wave detected at step {pWaveStep}");
            }

            if (pWaveDetected && !sWaveDetected && CheckSWaveArrival())
            {
                sWaveDetected = true;
                sWaveStep = stepCount;
                Logger.Log($"[AcousticSimulator] S-wave detected at step {sWaveStep}");
            }

            // Check termination conditions
            if (pWaveDetected && sWaveDetected && stepCount > sWaveStep + _params.TimeSteps / 10) break;

            // Update progress
            Progress = (float)stepCount / maxSteps;

            if (stepCount % 10 == 0)
                ProgressUpdated?.Invoke(this, new SimulationProgressEventArgs
                {
                    Progress = Progress,
                    Step = stepCount,
                    Message = $"Step {stepCount}/{maxSteps}"
                });
        }

        // Calculate results
        var distance = CalculateDistance();
        var pVelocity = pWaveStep > 0 ? distance / (pWaveStep * _dt) : 0;
        var sVelocity = sWaveStep > 0 ? distance / (sWaveStep * _dt) : 0;

        results.PWaveVelocity = pVelocity;
        results.SWaveVelocity = sVelocity;
        results.VpVsRatio = sVelocity > 0 ? pVelocity / sVelocity : 0;
        results.PWaveTravelTime = pWaveStep;
        results.SWaveTravelTime = sWaveStep;
        results.TotalTimeSteps = stepCount;
        results.ComputationTime = DateTime.Now - startTime;

        return results;
    }

    private void CalculateTimeStep(float[,,] densityVolume)
    {
        // If we already have a cached density (CPU path), use it; else use the param.
        var rhoMin = float.MaxValue;
        if (_rho != null)
        {
            foreach (var r in _rho)
                if (r > 0 && r < rhoMin)
                    rhoMin = r;
        }
        else
        {
            foreach (var r in densityVolume)
                if (r > 0 && r < rhoMin)
                    rhoMin = r;
        }

        rhoMin = Math.Max(rhoMin, 100.0f);
        var vpMax = (float)Math.Sqrt((_lambda + 2 * _mu) / rhoMin);
        vpMax = Math.Min(vpMax, 6000.0f);
        const float SafetyCourant = 0.25f;
        _dt = Math.Max(SafetyCourant * _params.PixelSize / vpMax, 1e-8f);
        Logger.Log($"[AcousticSimulator] Time step: {_dt:E6} s, vpMax: {vpMax:F2} m/s");
    }

    public float[,,] GetDensityVolume()
    {
        // CPU: return our copy
        if (!_useGPU) return _rho;

        // GPU: download once and return
        var dst = new float[_params.Width, _params.Height, _params.Depth];
        DownloadDensityFromGPU(dst);
        return dst;
    }

    private unsafe void DownloadDensityFromGPU(float[,,] dst)
    {
        var size = _totalCells;
        var tmp = new float[size];
        fixed (float* p = tmp)
        {
            _cl.EnqueueReadBuffer(_commandQueue, _densityBuffer, true, 0,
                (nuint)(size * sizeof(float)), p, 0, null, null);
        }

        var idx = 0;
        for (var z = 0; z < _params.Depth; z++)
        for (var y = 0; y < _params.Height; y++)
        for (var x = 0; x < _params.Width; x++)
            dst[x, y, z] = tmp[idx++];
    }

    private unsafe void ApplyInitialConditions(byte[,,] volumeLabels, float[,,] densityVolume)
    {
        if (_useGPU)
        {
            // Upload data to GPU
            var size = _totalCells;

            // Flatten arrays
            var materialFlat = new byte[size];
            var densityFlat = new float[size];

            var idx = 0;
            for (var z = 0; z < _params.Depth; z++)
            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
            {
                materialFlat[idx] = volumeLabels[x, y, z];
                densityFlat[idx] = densityVolume[x, y, z];
                idx++;
            }

            fixed (byte* materialPtr = materialFlat)
            fixed (float* densityPtr = densityFlat)
            {
                _cl.EnqueueWriteBuffer(_commandQueue, _materialBuffer, true, 0,
                    (nuint)(size * sizeof(byte)), materialPtr, 0, null, null);
                _cl.EnqueueWriteBuffer(_commandQueue, _densityBuffer, true, 0,
                    (nuint)(size * sizeof(float)), densityPtr, 0, null, null);
            }

            // Initialize stress and velocity fields with source pulse
            ApplySourcePulseGPU();
        }
        else
        {
            // ADD: own a copy of density for the whole simulation
            for (var z = 0; z < _params.Depth; z++)
            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
                _rho[x, y, z] = MathF.Max(100f, densityVolume[x, y, z]);

            // Apply confining pressure and source pulse
            ApplySourcePulseCPU(volumeLabels, densityVolume);
        }
    }

    private void ApplySourcePulseCPU(byte[,,] volumeLabels, float[,,] densityVolume)
    {
        // Apply confining pressure
        var confiningPa = _params.ConfiningPressureMPa * 1e6f;

        for (var z = 0; z < _params.Depth; z++)
        for (var y = 0; y < _params.Height; y++)
        for (var x = 0; x < _params.Width; x++)
            if (volumeLabels[x, y, z] == _params.SelectedMaterialID)
            {
                _sxx[x, y, z] = -confiningPa;
                _syy[x, y, z] = -confiningPa;
                _szz[x, y, z] = -confiningPa;
            }

        // Apply source pulse
        var pulse = _params.SourceAmplitude * (float)Math.Sqrt(_params.SourceEnergyJ) * 1e6f;

        // Calculate TX/RX positions
        var tx = (int)(_params.TxPosition.X * _params.Width);
        var ty = (int)(_params.TxPosition.Y * _params.Height);
        var tz = (int)(_params.TxPosition.Z * _params.Depth);

        if (_params.UseFullFaceTransducers)
            // Apply to entire face
            ApplyFullFaceSource(volumeLabels, densityVolume, pulse, tx, ty, tz);
        else
            // Point source
            ApplyPointSource(volumeLabels, densityVolume, pulse, tx, ty, tz);
    }

    private void ApplyFullFaceSource(byte[,,] volumeLabels, float[,,] densityVolume,
        float pulse, int tx, int ty, int tz)
    {
        // Determine which face to use based on axis
        switch (_params.Axis)
        {
            case 0: // X axis
                for (var y = 0; y < _params.Height; y++)
                for (var z = 0; z < _params.Depth; z++)
                    if (volumeLabels[0, y, z] == _params.SelectedMaterialID)
                    {
                        _sxx[0, y, z] += pulse;
                        _vx[0, y, z] = pulse / (_rho[0, y, z] * 10.0f); // axis X face
                    }

                break;

            case 1: // Y axis
                for (var x = 0; x < _params.Width; x++)
                for (var z = 0; z < _params.Depth; z++)
                    if (volumeLabels[x, 0, z] == _params.SelectedMaterialID)
                    {
                        _syy[x, 0, z] += pulse;
                        _vy[x, 0, z] = pulse / (_rho[x, 0, z] * 10.0f);
                    }

                break;

            case 2: // Z axis
                for (var x = 0; x < _params.Width; x++)
                for (var y = 0; y < _params.Height; y++)
                    if (volumeLabels[x, y, 0] == _params.SelectedMaterialID)
                    {
                        _szz[x, y, 0] += pulse;
                        _vz[x, y, 0] = pulse / (_rho[x, y, 0] * 10.0f);
                    }

                break;
        }
    }

    private void ApplyPointSource(byte[,,] volumeLabels, float[,,] densityVolume,
        float pulse, int tx, int ty, int tz)
    {
        // Apply spherical source
        var radius = 2;
        for (var dz = -radius; dz <= radius; dz++)
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dist > radius) continue;

            int x = tx + dx, y = ty + dy, z = tz + dz;
            if (x < 0 || x >= _params.Width ||
                y < 0 || y >= _params.Height ||
                z < 0 || z >= _params.Depth) continue;

            if (volumeLabels[x, y, z] != _params.SelectedMaterialID) continue;

            var falloff = 1.0f - dist / radius;
            var localPulse = pulse * falloff * falloff;

            _sxx[x, y, z] += localPulse;
            _syy[x, y, z] += localPulse;
            _szz[x, y, z] += localPulse;

            // Add velocity kick
            var vKick = localPulse / (_rho[x, y, z] * 10.0f);
            switch (_params.Axis)
            {
                case 0: _vx[x, y, z] += vKick; break;
                case 1: _vy[x, y, z] += vKick; break;
                case 2: _vz[x, y, z] += vKick; break;
            }
        }
    }

    private void ApplySourcePulseGPU()
    {
        // Initialize fields on GPU using kernels
        Logger.Log("[AcousticSimulator] GPU source pulse application");
        // This would be implemented with specific OpenCL kernels
    }

    private unsafe Task UpdateFieldsGPUAsync()
    {
        // Run stress update kernel
        var errNum = 0;
        var stressKernel = _cl.CreateKernel(_program, "updateStress", &errNum);
        if (errNum != 0)
        {
            Logger.LogError($"Failed to create stress kernel: {errNum}");
            return Task.CompletedTask;
        }

        // Set kernel arguments for stress kernel
        // For buffer objects (nint), pass them directly without taking address
        var materialBuffer = _materialBuffer;
        var densityBuffer = _densityBuffer;
        var velocityBuffer0 = _velocityBuffers[0];
        var velocityBuffer1 = _velocityBuffers[1];
        var velocityBuffer2 = _velocityBuffers[2];
        var stressBuffer0 = _stressBuffers[0];
        var stressBuffer1 = _stressBuffers[1];
        var stressBuffer2 = _stressBuffers[2];
        var stressBuffer3 = _stressBuffers[3];
        var stressBuffer4 = _stressBuffers[4];
        var stressBuffer5 = _stressBuffers[5];
        var damageBuffer = _damageBuffer;

        // For value types, create local copies
        var lambda = _lambda;
        var mu = _mu;
        var dt = _dt;
        var dx = _params.PixelSize;
        var width = _params.Width;
        var height = _params.Height;
        var depth = _params.Depth;
        var selectedMaterial = _params.SelectedMaterialID;

        _cl.SetKernelArg(stressKernel, 0, (nuint)sizeof(nint), &materialBuffer);
        _cl.SetKernelArg(stressKernel, 1, (nuint)sizeof(nint), &densityBuffer);
        _cl.SetKernelArg(stressKernel, 2, (nuint)sizeof(nint), &velocityBuffer0);
        _cl.SetKernelArg(stressKernel, 3, (nuint)sizeof(nint), &velocityBuffer1);
        _cl.SetKernelArg(stressKernel, 4, (nuint)sizeof(nint), &velocityBuffer2);
        _cl.SetKernelArg(stressKernel, 5, (nuint)sizeof(nint), &stressBuffer0);
        _cl.SetKernelArg(stressKernel, 6, (nuint)sizeof(nint), &stressBuffer1);
        _cl.SetKernelArg(stressKernel, 7, (nuint)sizeof(nint), &stressBuffer2);
        _cl.SetKernelArg(stressKernel, 8, (nuint)sizeof(nint), &stressBuffer3);
        _cl.SetKernelArg(stressKernel, 9, (nuint)sizeof(nint), &stressBuffer4);
        _cl.SetKernelArg(stressKernel, 10, (nuint)sizeof(nint), &stressBuffer5);
        _cl.SetKernelArg(stressKernel, 11, (nuint)sizeof(nint), &damageBuffer);
        _cl.SetKernelArg(stressKernel, 12, sizeof(float), &lambda);
        _cl.SetKernelArg(stressKernel, 13, sizeof(float), &mu);
        _cl.SetKernelArg(stressKernel, 14, sizeof(float), &dt);
        _cl.SetKernelArg(stressKernel, 15, sizeof(float), &dx);
        _cl.SetKernelArg(stressKernel, 16, sizeof(int), &width);
        _cl.SetKernelArg(stressKernel, 17, sizeof(int), &height);
        _cl.SetKernelArg(stressKernel, 18, sizeof(int), &depth);
        _cl.SetKernelArg(stressKernel, 19, sizeof(byte), &selectedMaterial);

        // Execute kernel
        var globalSize = (nuint)_totalCells;
        _cl.EnqueueNdrangeKernel(_commandQueue, stressKernel, 1, null, &globalSize, null, 0, null, null);

        // Run velocity update kernel
        var velocityKernel = _cl.CreateKernel(_program, "updateVelocity", &errNum);
        if (errNum != 0)
        {
            Logger.LogError($"Failed to create velocity kernel: {errNum}");
            return Task.CompletedTask;
        }

        // Set arguments for velocity kernel - reuse local variables
        _cl.SetKernelArg(velocityKernel, 0, (nuint)sizeof(nint), &materialBuffer);
        _cl.SetKernelArg(velocityKernel, 1, (nuint)sizeof(nint), &densityBuffer);
        _cl.SetKernelArg(velocityKernel, 2, (nuint)sizeof(nint), &velocityBuffer0);
        _cl.SetKernelArg(velocityKernel, 3, (nuint)sizeof(nint), &velocityBuffer1);
        _cl.SetKernelArg(velocityKernel, 4, (nuint)sizeof(nint), &velocityBuffer2);
        _cl.SetKernelArg(velocityKernel, 5, (nuint)sizeof(nint), &stressBuffer0);
        _cl.SetKernelArg(velocityKernel, 6, (nuint)sizeof(nint), &stressBuffer1);
        _cl.SetKernelArg(velocityKernel, 7, (nuint)sizeof(nint), &stressBuffer2);
        _cl.SetKernelArg(velocityKernel, 8, (nuint)sizeof(nint), &stressBuffer3);
        _cl.SetKernelArg(velocityKernel, 9, (nuint)sizeof(nint), &stressBuffer4);
        _cl.SetKernelArg(velocityKernel, 10, (nuint)sizeof(nint), &stressBuffer5);
        _cl.SetKernelArg(velocityKernel, 11, sizeof(float), &dt);
        _cl.SetKernelArg(velocityKernel, 12, sizeof(float), &dx);
        _cl.SetKernelArg(velocityKernel, 13, sizeof(int), &width);
        _cl.SetKernelArg(velocityKernel, 14, sizeof(int), &height);
        _cl.SetKernelArg(velocityKernel, 15, sizeof(int), &depth);
        _cl.SetKernelArg(velocityKernel, 16, sizeof(byte), &selectedMaterial);

        _cl.EnqueueNdrangeKernel(_commandQueue, velocityKernel, 1, null, &globalSize, null, 0, null, null);

        _cl.Finish(_commandQueue);

        // Clean up kernels
        _cl.ReleaseKernel(stressKernel);
        _cl.ReleaseKernel(velocityKernel);

        return Task.CompletedTask;
    }

    private void UpdateFieldsCPU(byte[,,] volumeLabels, float[,,] densityVolume)
    {
        // Update stress fields
        UpdateStressCPU(volumeLabels, densityVolume);

        // Update velocity fields
        UpdateVelocityCPU(volumeLabels, densityVolume);
    }

    private void UpdateStressCPU(byte[,,] volumeLabels, float[,,] densityVolume)
    {
        Parallel.For(1, _params.Depth - 1, z =>
        {
            for (var y = 1; y < _params.Height - 1; y++)
            for (var x = 1; x < _params.Width - 1; x++)
            {
                if (volumeLabels[x, y, z] != _params.SelectedMaterialID) continue;

                // Calculate velocity gradients (staggered grid)
                var dvx_dx = (_vx[x + 1, y, z] - _vx[x - 1, y, z]) / (2 * _params.PixelSize);
                var dvy_dy = (_vy[x, y + 1, z] - _vy[x, y - 1, z]) / (2 * _params.PixelSize);
                var dvz_dz = (_vz[x, y, z + 1] - _vz[x, y, z - 1]) / (2 * _params.PixelSize);

                var dvy_dx = (_vy[x + 1, y, z] - _vy[x - 1, y, z]) / (2 * _params.PixelSize);
                var dvx_dy = (_vx[x, y + 1, z] - _vx[x, y - 1, z]) / (2 * _params.PixelSize);
                var dvz_dx = (_vz[x + 1, y, z] - _vz[x - 1, y, z]) / (2 * _params.PixelSize);
                var dvx_dz = (_vx[x, y, z + 1] - _vx[x, y, z - 1]) / (2 * _params.PixelSize);
                var dvz_dy = (_vz[x, y + 1, z] - _vz[x, y - 1, z]) / (2 * _params.PixelSize);
                var dvy_dz = (_vy[x, y, z + 1] - _vy[x, y, z - 1]) / (2 * _params.PixelSize);

                var volumetricStrain = dvx_dx + dvy_dy + dvz_dz;

                // Update stress (elastic model with damage)
                var damping = 1.0f - _damage[x, y, z] * 0.9f;
                _sxx[x, y, z] += _dt * damping * (_lambda * volumetricStrain + 2 * _mu * dvx_dx);
                _syy[x, y, z] += _dt * damping * (_lambda * volumetricStrain + 2 * _mu * dvy_dy);
                _szz[x, y, z] += _dt * damping * (_lambda * volumetricStrain + 2 * _mu * dvz_dz);
                _sxy[x, y, z] += _dt * damping * _mu * (dvy_dx + dvx_dy);
                _sxz[x, y, z] += _dt * damping * _mu * (dvz_dx + dvx_dz);
                _syz[x, y, z] += _dt * damping * _mu * (dvz_dy + dvy_dz);

                // Apply plastic and brittle models if enabled
                if (_params.UsePlasticModel)
                    ApplyPlasticModel(x, y, z);

                if (_params.UseBrittleModel)
                    ApplyBrittleModel(x, y, z);
            }
        });
    }

    private void UpdateVelocityCPU(byte[,,] volumeLabels, float[,,] densityVolume)
    {
        const float DAMPING = 0.995f; // Damping factor

        Parallel.For(1, _params.Depth - 1, z =>
        {
            for (var y = 1; y < _params.Height - 1; y++)
            for (var x = 1; x < _params.Width - 1; x++)
            {
                if (volumeLabels[x, y, z] != _params.SelectedMaterialID) continue;

                var rho = _rho[x, y, z];

                // Calculate stress gradients
                var dsxx_dx = (_sxx[x + 1, y, z] - _sxx[x - 1, y, z]) / (2 * _params.PixelSize);
                var dsyy_dy = (_syy[x, y + 1, z] - _syy[x, y - 1, z]) / (2 * _params.PixelSize);
                var dszz_dz = (_szz[x, y, z + 1] - _szz[x, y, z - 1]) / (2 * _params.PixelSize);

                var dsxy_dy = (_sxy[x, y + 1, z] - _sxy[x, y - 1, z]) / (2 * _params.PixelSize);
                var dsxy_dx = (_sxy[x + 1, y, z] - _sxy[x - 1, y, z]) / (2 * _params.PixelSize);
                var dsxz_dz = (_sxz[x, y, z + 1] - _sxz[x, y, z - 1]) / (2 * _params.PixelSize);
                var dsxz_dx = (_sxz[x + 1, y, z] - _sxz[x - 1, y, z]) / (2 * _params.PixelSize);
                var dsyz_dz = (_syz[x, y, z + 1] - _syz[x, y, z - 1]) / (2 * _params.PixelSize);
                var dsyz_dy = (_syz[x, y + 1, z] - _syz[x, y - 1, z]) / (2 * _params.PixelSize);

                // Update velocities with damping
                _vx[x, y, z] = _vx[x, y, z] * DAMPING + _dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
                _vy[x, y, z] = _vy[x, y, z] * DAMPING + _dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
                _vz[x, y, z] = _vz[x, y, z] * DAMPING + _dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
            }
        });
    }

    private void ApplyPlasticModel(int x, int y, int z)
    {
        // Mohr-Coulomb plasticity
        var mean = (_sxx[x, y, z] + _syy[x, y, z] + _szz[x, y, z]) / 3.0f;
        var dev_xx = _sxx[x, y, z] - mean;
        var dev_yy = _syy[x, y, z] - mean;
        var dev_zz = _szz[x, y, z] - mean;

        var J2 = 0.5f * (dev_xx * dev_xx + dev_yy * dev_yy + dev_zz * dev_zz +
                         2 * (_sxy[x, y, z] * _sxy[x, y, z] +
                              _sxz[x, y, z] * _sxz[x, y, z] +
                              _syz[x, y, z] * _syz[x, y, z]));
        var tau = (float)Math.Sqrt(J2);

        var sinPhi = (float)Math.Sin(_params.FailureAngleDeg * Math.PI / 180.0);
        var cosPhi = (float)Math.Cos(_params.FailureAngleDeg * Math.PI / 180.0);
        var cohesionPa = _params.CohesionMPa * 1e6f;
        var p = -mean + _params.ConfiningPressureMPa * 1e6f;

        var yield = tau + p * sinPhi - cohesionPa * cosPhi;

        if (yield > 0 && tau > 1e-10f)
        {
            var scale = (tau - (cohesionPa * cosPhi - p * sinPhi)) / tau;
            scale = Math.Min(scale, 0.95f);

            dev_xx *= 1 - scale;
            dev_yy *= 1 - scale;
            dev_zz *= 1 - scale;
            _sxy[x, y, z] *= 1 - scale;
            _sxz[x, y, z] *= 1 - scale;
            _syz[x, y, z] *= 1 - scale;

            _sxx[x, y, z] = dev_xx + mean;
            _syy[x, y, z] = dev_yy + mean;
            _szz[x, y, z] = dev_zz + mean;
        }
    }

    private void ApplyBrittleModel(int x, int y, int z)
    {
        // Calculate maximum principal stress
        var I1 = _sxx[x, y, z] + _syy[x, y, z] + _szz[x, y, z];
        var sigmaMax = I1 / 3.0f; // Simplified

        var tensileStrengthPa = _params.TensileStrengthMPa * 1e6f;

        if (sigmaMax > tensileStrengthPa && _damage[x, y, z] < 1.0f)
        {
            var incr = (sigmaMax - tensileStrengthPa) / tensileStrengthPa;
            incr = Math.Min(incr, 0.1f);
            _damage[x, y, z] = Math.Min(0.95f, _damage[x, y, z] + incr * 0.01f);

            var factor = 1.0f - _damage[x, y, z];
            _sxx[x, y, z] *= factor;
            _syy[x, y, z] *= factor;
            _szz[x, y, z] *= factor;
            _sxy[x, y, z] *= factor;
            _sxz[x, y, z] *= factor;
            _syz[x, y, z] *= factor;
        }
    }

    private bool CheckPWaveArrival()
    {
        var rx = (int)(_params.RxPosition.X * _params.Width);
        var ry = (int)(_params.RxPosition.Y * _params.Height);
        var rz = (int)(_params.RxPosition.Z * _params.Depth);

        if (rx < 0 || rx >= _params.Width || ry < 0 || ry >= _params.Height || rz < 0 || rz >= _params.Depth)
            return false;

        float magnitude = 0;

        // Check for longitudinal wave (P-wave)
        switch (_params.Axis)
        {
            case 0: magnitude = Math.Abs(_vx[rx, ry, rz]); break;
            case 1: magnitude = Math.Abs(_vy[rx, ry, rz]); break;
            case 2: magnitude = Math.Abs(_vz[rx, ry, rz]); break;
        }

        return magnitude > 1e-9f;
    }

    private bool CheckSWaveArrival()
    {
        var rx = (int)(_params.RxPosition.X * _params.Width);
        var ry = (int)(_params.RxPosition.Y * _params.Height);
        var rz = (int)(_params.RxPosition.Z * _params.Depth);

        if (rx < 0 || rx >= _params.Width || ry < 0 || ry >= _params.Height || rz < 0 || rz >= _params.Depth)
            return false;

        float magnitude = 0;

        // Check for transverse wave (S-wave)
        switch (_params.Axis)
        {
            case 0:
                magnitude = (float)Math.Sqrt(_vy[rx, ry, rz] * _vy[rx, ry, rz] +
                                             _vz[rx, ry, rz] * _vz[rx, ry, rz]);
                break;
            case 1:
                magnitude = (float)Math.Sqrt(_vx[rx, ry, rz] * _vx[rx, ry, rz] +
                                             _vz[rx, ry, rz] * _vz[rx, ry, rz]);
                break;
            case 2:
                magnitude = (float)Math.Sqrt(_vx[rx, ry, rz] * _vx[rx, ry, rz] +
                                             _vy[rx, ry, rz] * _vy[rx, ry, rz]);
                break;
        }

        return magnitude > 1e-9f;
    }

    private float CalculateDistance()
    {
        var tx = (int)(_params.TxPosition.X * _params.Width);
        var ty = (int)(_params.TxPosition.Y * _params.Height);
        var tz = (int)(_params.TxPosition.Z * _params.Depth);
        var rx = (int)(_params.RxPosition.X * _params.Width);
        var ry = (int)(_params.RxPosition.Y * _params.Height);
        var rz = (int)(_params.RxPosition.Z * _params.Depth);

        return (float)Math.Sqrt((tx - rx) * (tx - rx) +
                                (ty - ry) * (ty - ry) +
                                (tz - rz) * (tz - rz)) * _params.PixelSize;
    }

    private float[,,] GetCombinedWaveField()
    {
        var combined = new float[_params.Width, _params.Height, _params.Depth];

        for (var z = 0; z < _params.Depth; z++)
        for (var y = 0; y < _params.Height; y++)
        for (var x = 0; x < _params.Width; x++)
        {
            var vx = _vx[x, y, z];
            var vy = _vy[x, y, z];
            var vz = _vz[x, y, z];
            combined[x, y, z] = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
        }

        return combined;
    }

    public float[,,] GetFinalWaveField(int component)
    {
        var field = new float[_params.Width, _params.Height, _params.Depth];

        if (_useGPU)
            // Download from GPU
            DownloadFieldFromGPU(component, field);
        else
            // Copy from CPU arrays
            for (var z = 0; z < _params.Depth; z++)
            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
                switch (component)
                {
                    case 0: field[x, y, z] = _vx[x, y, z]; break;
                    case 1: field[x, y, z] = _vy[x, y, z]; break;
                    case 2: field[x, y, z] = _vz[x, y, z]; break;
                }

        return field;
    }

    private unsafe void DownloadFieldFromGPU(int component, float[,,] field)
    {
        // Download data from GPU buffer
        var size = _totalCells;
        var data = new float[size];

        fixed (float* dataPtr = data)
        {
            _cl.EnqueueReadBuffer(_commandQueue, _velocityBuffers[component], true, 0,
                (nuint)(size * sizeof(float)), dataPtr, 0, null, null);
        }

        // Convert to 3D array
        var idx = 0;
        for (var z = 0; z < _params.Depth; z++)
        for (var y = 0; y < _params.Height; y++)
        for (var x = 0; x < _params.Width; x++)
            field[x, y, z] = data[idx++];
    }
}

#endregion

#region Supporting Classes

public class SimulationResults
{
    public float[,,] DamageField { get; set; }
    public double PWaveVelocity { get; set; }
    public double SWaveVelocity { get; set; }
    public double VpVsRatio { get; set; }
    public int PWaveTravelTime { get; set; }
    public int SWaveTravelTime { get; set; }
    public int TotalTimeSteps { get; set; }
    public TimeSpan ComputationTime { get; set; }
    public float[,,] WaveFieldVx { get; set; }
    public float[,,] WaveFieldVy { get; set; }
    public float[,,] WaveFieldVz { get; set; }
    public List<WaveFieldSnapshot> TimeSeriesSnapshots { get; set; }
}

internal class SimulationProgressEventArgs : EventArgs
{
    public float Progress { get; set; }
    public int Step { get; set; }
    public string Message { get; set; }
}

internal class WaveFieldUpdateEventArgs : EventArgs
{
    public float[,,] WaveField { get; set; }
    public int TimeStep { get; set; }
    public float SimTime { get; set; }
    public object Dataset { get; set; }
}

internal class CalibrationManager
{
    private readonly List<CalibrationPoint> _calibrationPoints = new();

    public void AddCalibrationPoint(string materialName, byte materialID, float density,
        float confiningPressure, float youngsModulus, float poissonRatio,
        double vp, double vs, double vpVsRatio)
    {
        _calibrationPoints.Add(new CalibrationPoint
        {
            MaterialName = materialName,
            MaterialID = materialID,
            Density = density,
            ConfiningPressureMPa = confiningPressure,
            YoungsModulusMPa = youngsModulus,
            PoissonRatio = poissonRatio,
            MeasuredVp = vp,
            MeasuredVs = vs,
            MeasuredVpVsRatio = vpVsRatio,
            Timestamp = DateTime.Now
        });

        Logger.Log($"[CalibrationManager] Added calibration point for {materialName}");
    }

    public bool HasCalibration()
    {
        return _calibrationPoints.Count >= 2;
    }

    public (float YoungsModulus, float PoissonRatio) GetCalibratedParameters(
        float density, float confiningPressure)
    {
        if (_calibrationPoints.Count < 2)
            return (30000.0f, 0.25f); // Default values

        // Find closest calibration points
        var closestPoints = _calibrationPoints
            .OrderBy(p => Math.Abs(p.Density - density) + Math.Abs(p.ConfiningPressureMPa - confiningPressure))
            .Take(2)
            .ToList();

        if (closestPoints.Count == 0)
            return (30000.0f, 0.25f);

        // Interpolate
        if (closestPoints.Count == 1)
            return (closestPoints[0].YoungsModulusMPa, closestPoints[0].PoissonRatio);

        var p1 = closestPoints[0];
        var p2 = closestPoints[1];

        var totalDist = Math.Abs(p1.Density - density) + Math.Abs(p2.Density - density);
        if (totalDist < 0.001f)
            return (p1.YoungsModulusMPa, p1.PoissonRatio);

        var weight1 = 1.0f - Math.Abs(p1.Density - density) / totalDist;
        var weight2 = 1.0f - weight1;

        var E = p1.YoungsModulusMPa * weight1 + p2.YoungsModulusMPa * weight2;
        var nu = p1.PoissonRatio * weight1 + p2.PoissonRatio * weight2;

        return (E, nu);
    }

    public string GetCalibrationSummary()
    {
        if (_calibrationPoints.Count == 0)
            return "No calibration points available.";

        var sb = new StringBuilder();
        sb.AppendLine($"Calibration Points: {_calibrationPoints.Count}");

        foreach (var point in _calibrationPoints.Take(3))
            sb.AppendLine($"  • {point.MaterialName}: Vp/Vs={point.MeasuredVpVsRatio:F3}, ρ={point.Density:F1} kg/m³");

        if (_calibrationPoints.Count > 3)
            sb.AppendLine($"  ... and {_calibrationPoints.Count - 3} more");

        return sb.ToString();
    }

    private class CalibrationPoint
    {
        public string MaterialName { get; set; }
        public byte MaterialID { get; set; }
        public float Density { get; set; }
        public float ConfiningPressureMPa { get; set; }
        public float YoungsModulusMPa { get; set; }
        public float PoissonRatio { get; set; }
        public double MeasuredVp { get; set; }
        public double MeasuredVs { get; set; }
        public double MeasuredVpVsRatio { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

#endregion