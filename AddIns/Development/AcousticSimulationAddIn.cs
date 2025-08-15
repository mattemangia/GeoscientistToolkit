// GeoscientistToolkit/AddIns/AcousticSimulation/AcousticSimulationAddIn.cs (FIXED)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.AddIns;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.AddIns.AcousticSimulation
{
    /// <summary>
    /// Acoustic wave propagation simulation add-in for CT volume data.
    /// Implements full elastodynamic physics with unified CPU/GPU compute via Silk.NET.
    /// </summary>
    public class AcousticSimulationAddIn : IAddIn
    {
        public string Id => "com.geoscientisttoolkit.acousticsimulation";
        public string Name => "Acoustic Wave Simulation";
        public string Version => "2.0.0";
        public string Author => "GeoscientistToolkit Team";
        public string Description => "Advanced acoustic/elastic wave propagation simulation with Vp/Vs analysis and real-time visualization.";

        private static AcousticSimulationTool _tool;

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

        public IEnumerable<AddInMenuItem> GetMenuItems() => null;
        public IEnumerable<AddInTool> GetTools() => new[] { _tool };
        public IEnumerable<IDataImporter> GetDataImporters() => null;
        public IEnumerable<IDataExporter> GetDataExporters() => null;
    }

   

    #region Unified Simulator

    /// <summary>
    /// Unified acoustic simulator that can run on CPU or GPU using Silk.NET OpenCL
    /// </summary>
    internal class UnifiedAcousticSimulator : IDisposable
    {
        private readonly SimulationParameters _params;
        private CL _cl;
        private nint _context;
        private nint _commandQueue;
        private nint _program;
        private nint _device;
        private bool _useGPU;
        
        // Buffers
        private nint _materialBuffer;
        private nint _densityBuffer;
        private nint[] _velocityBuffers = new nint[3]; // vx, vy, vz
        private nint[] _stressBuffers = new nint[6]; // sxx, syy, szz, sxy, sxz, syz
        private nint _damageBuffer;

        // CPU fallback arrays
        private float[,,] _vx, _vy, _vz;
        private float[,,] _sxx, _syy, _szz, _sxy, _sxz, _syz;
        private float[,,] _damage;
        private float[,,] _rho;
        // Progress tracking
        public float Progress { get; private set; }
        public int CurrentStep { get; private set; }
        public event EventHandler<SimulationProgressEventArgs> ProgressUpdated;
        public event EventHandler<WaveFieldUpdateEventArgs> WaveFieldUpdated;

        // Physical constants
        private float _lambda, _mu;
        private float _dt;
        private int _totalCells;

        public UnifiedAcousticSimulator(SimulationParameters parameters)
        {
            _params = parameters;

            // Calculate Lamé constants
            float E = _params.YoungsModulusMPa * 1e6f; // Convert to Pa
            _mu = E / (2.0f * (1.0f + _params.PoissonRatio));
            _lambda = E * _params.PoissonRatio / ((1 + _params.PoissonRatio) * (1 - 2 * _params.PoissonRatio));

            _totalCells = _params.Width * _params.Height * _params.Depth;

            // Initialize compute backend
            InitializeCompute();
        }

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
            // Get platform
            uint platformCount = 0;
            _cl.GetPlatformIDs(0, null, &platformCount);
            if (platformCount == 0)
                throw new Exception("No OpenCL platforms available");

            var platforms = new nint[platformCount];
            fixed (nint* platformsPtr = platforms)
            {
                _cl.GetPlatformIDs(platformCount, platformsPtr, null);
            }

            // Get GPU device
            uint deviceCount = 0;
            _cl.GetDeviceIDs(platforms[0], DeviceType.Gpu, 0, null, &deviceCount);
            if (deviceCount == 0)
            {
                // Try CPU as fallback
                _cl.GetDeviceIDs(platforms[0], DeviceType.Cpu, 0, null, &deviceCount);
                if (deviceCount == 0)
                    throw new Exception("No OpenCL devices available");
            }

            var devices = new nint[deviceCount];
            fixed (nint* devicesPtr = devices)
            {
                _cl.GetDeviceIDs(platforms[0], DeviceType.Default, deviceCount, devicesPtr, null);
            }
            _device = devices[0];

            // Create context
            int errNum = 0;
            nint device = _device; // Local copy
            nint* devicePtr = &device; // Take address directly without fixed
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
            string kernelSource = GetOpenCLKernelSource();
            var sourceBytes = System.Text.Encoding.ASCII.GetBytes(kernelSource);
    
            fixed (byte* sourcePtr = sourceBytes)
            {
                var sourcePtrAddr = new IntPtr(sourcePtr);
                var length = (nuint)sourceBytes.Length;
                int errNum = 0;
        
                _program = _cl.CreateProgramWithSource(_context, 1, (byte**)&sourcePtrAddr, &length, &errNum);
                if (errNum != 0)
                    throw new Exception($"Failed to create program: {errNum}");

                // Build program - take address of device directly and cast null to resolve ambiguity
                nint device = _device;
                nint* devicePtr = &device; // Take address directly without fixed
                int buildStatus = _cl.BuildProgram(_program, 1, devicePtr, (byte*)null, null, null);
        
                if (buildStatus != 0)
                {
                    // Get build log
                    nuint logSize = 0;
                    _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
            
                    if (logSize > 0)
                    {
                        byte* log = stackalloc byte[(int)logSize];
                        _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, logSize, log, null);
                        string logStr = Marshal.PtrToStringAnsi((IntPtr)log, (int)logSize);
                        Logger.LogError($"[AcousticSimulator] OpenCL build failed: {logStr}");
                    }
            
                    throw new Exception("Failed to build OpenCL program");
                }
            }
        }
        private unsafe void CreateOpenCLBuffers()
        {
            int size = _totalCells;
            int errNum = 0;

            // Create buffers
            _materialBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly, (nuint)(size * sizeof(byte)), null, &errNum);
            _densityBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly, (nuint)(size * sizeof(float)), null, &errNum);
            
            for (int i = 0; i < 3; i++)
                _velocityBuffers[i] = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(size * sizeof(float)), null, &errNum);
            
            for (int i = 0; i < 6; i++)
                _stressBuffers[i] = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(size * sizeof(float)), null, &errNum);
            
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
            if (_params.SaveTimeSeries)
            {
                results.TimeSeriesSnapshots = new List<WaveFieldSnapshot>();
            }

            // Main simulation loop
            int stepCount = 0;
            int maxSteps = _params.TimeSteps * 2; // Safety limit

            // Wave detection variables
            bool pWaveDetected = false, sWaveDetected = false;
            int pWaveStep = 0, sWaveStep = 0;

            while (stepCount < maxSteps && !cancellationToken.IsCancellationRequested)
            {
                if (_useGPU)
                {
                    await UpdateFieldsGPUAsync();
                }
                else
                {
                    UpdateFieldsCPU(volumeLabels, densityVolume);
                }

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
                {
                    WaveFieldUpdated?.Invoke(this, new WaveFieldUpdateEventArgs
                    {
                        WaveField = GetCombinedWaveField(),
                        TimeStep = stepCount,
                        SimTime = stepCount * _dt,
                        Dataset = volumeLabels
                    });
                }

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
                if (pWaveDetected && sWaveDetected && stepCount > sWaveStep + _params.TimeSteps / 10)
                {
                    break;
                }

                // Update progress
                Progress = (float)stepCount / maxSteps;
                
                if (stepCount % 10 == 0)
                {
                    ProgressUpdated?.Invoke(this, new SimulationProgressEventArgs
                    {
                        Progress = Progress,
                        Step = stepCount,
                        Message = $"Step {stepCount}/{maxSteps}"
                    });
                }
            }

            // Calculate results
            var distance = CalculateDistance();
            float pVelocity = pWaveStep > 0 ? distance / (pWaveStep * _dt) : 0;
            float sVelocity = sWaveStep > 0 ? distance / (sWaveStep * _dt) : 0;

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
            float rhoMin = float.MaxValue;
            if (_rho != null)
            {
                foreach (var r in _rho)
                    if (r > 0 && r < rhoMin) rhoMin = r;
            }
            else
            {
                foreach (var r in densityVolume)
                    if (r > 0 && r < rhoMin) rhoMin = r;
            }

            rhoMin = Math.Max(rhoMin, 100.0f);
            float vpMax = (float)Math.Sqrt((_lambda + 2 * _mu) / rhoMin);
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
            int size = _totalCells;
            var tmp = new float[size];
            fixed (float* p = tmp)
            {
                _cl.EnqueueReadBuffer(_commandQueue, _densityBuffer, true, 0,
                    (nuint)(size * sizeof(float)), p, 0, null, null);
            }
            int idx = 0;
            for (int z = 0; z < _params.Depth; z++)
            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
                dst[x, y, z] = tmp[idx++];
        }
        private unsafe void ApplyInitialConditions(byte[,,] volumeLabels, float[,,] densityVolume)
        {
            if (_useGPU)
            {
                // Upload data to GPU
                int size = _totalCells;
                
                // Flatten arrays
                byte[] materialFlat = new byte[size];
                float[] densityFlat = new float[size];
                
                int idx = 0;
                for (int z = 0; z < _params.Depth; z++)
                    for (int y = 0; y < _params.Height; y++)
                        for (int x = 0; x < _params.Width; x++)
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
                for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                    _rho[x, y, z] = MathF.Max(100f, densityVolume[x, y, z]);

                // Apply confining pressure and source pulse
                ApplySourcePulseCPU(volumeLabels, densityVolume);
            }
        }

        private void ApplySourcePulseCPU(byte[,,] volumeLabels, float[,,] densityVolume)
        {
            // Apply confining pressure
            float confiningPa = _params.ConfiningPressureMPa * 1e6f;
            
            for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                        if (volumeLabels[x, y, z] == _params.SelectedMaterialID)
                        {
                            _sxx[x, y, z] = -confiningPa;
                            _syy[x, y, z] = -confiningPa;
                            _szz[x, y, z] = -confiningPa;
                        }

            // Apply source pulse
            float pulse = _params.SourceAmplitude * (float)Math.Sqrt(_params.SourceEnergyJ) * 1e6f;
            
            // Calculate TX/RX positions
            int tx = (int)(_params.TxPosition.X * _params.Width);
            int ty = (int)(_params.TxPosition.Y * _params.Height);
            int tz = (int)(_params.TxPosition.Z * _params.Depth);

            if (_params.UseFullFaceTransducers)
            {
                // Apply to entire face
                ApplyFullFaceSource(volumeLabels, densityVolume, pulse, tx, ty, tz);
            }
            else
            {
                // Point source
                ApplyPointSource(volumeLabels, densityVolume, pulse, tx, ty, tz);
            }
        }

        private void ApplyFullFaceSource(byte[,,] volumeLabels, float[,,] densityVolume, 
            float pulse, int tx, int ty, int tz)
        {
            // Determine which face to use based on axis
            switch (_params.Axis)
            {
                case 0: // X axis
                    for (int y = 0; y < _params.Height; y++)
                        for (int z = 0; z < _params.Depth; z++)
                            if (volumeLabels[0, y, z] == _params.SelectedMaterialID)
                            {
                                _sxx[0, y, z] += pulse;
                                _vx[0, y, z] = pulse / (_rho[0, y, z] * 10.0f);    // axis X face
                                
                            }
                    break;
                    
                case 1: // Y axis
                    for (int x = 0; x < _params.Width; x++)
                        for (int z = 0; z < _params.Depth; z++)
                            if (volumeLabels[x, 0, z] == _params.SelectedMaterialID)
                            {
                                _syy[x, 0, z] += pulse;
                                _vy[x, 0, z] = pulse / (_rho[x, 0, z] * 10.0f); 
                            }
                    break;
                    
                case 2: // Z axis
                    for (int x = 0; x < _params.Width; x++)
                        for (int y = 0; y < _params.Height; y++)
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
            int radius = 2;
            for (int dz = -radius; dz <= radius; dz++)
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (dist > radius) continue;

                        int x = tx + dx, y = ty + dy, z = tz + dz;
                        if (x < 0 || x >= _params.Width || 
                            y < 0 || y >= _params.Height || 
                            z < 0 || z >= _params.Depth) continue;

                        if (volumeLabels[x, y, z] != _params.SelectedMaterialID) continue;

                        float falloff = 1.0f - dist / radius;
                        float localPulse = pulse * falloff * falloff;

                        _sxx[x, y, z] += localPulse;
                        _syy[x, y, z] += localPulse;
                        _szz[x, y, z] += localPulse;

                        // Add velocity kick
                        float vKick = localPulse / (_rho[x, y, z] * 10.0f);
                        switch (_params.Axis)
                        {
                            case 0: _vx[x, y, z] += vKick; break;
                            case 1: _vy[x, y, z] += vKick; break;
                            case 2: _vz[x, y, z] += vKick; break;
                        }
                    }
        }

        private unsafe void ApplySourcePulseGPU()
        {
            // Initialize fields on GPU using kernels
            Logger.Log("[AcousticSimulator] GPU source pulse application");
            // This would be implemented with specific OpenCL kernels
        }

        private unsafe Task UpdateFieldsGPUAsync()
{
    // Run stress update kernel
    int errNum = 0;
    var stressKernel = _cl.CreateKernel(_program, "updateStress", &errNum);
    if (errNum != 0)
    {
        Logger.LogError($"Failed to create stress kernel: {errNum}");
        return Task.CompletedTask;
    }
    
    // Set kernel arguments for stress kernel
    // For buffer objects (nint), pass them directly without taking address
    nint materialBuffer = _materialBuffer;
    nint densityBuffer = _densityBuffer;
    nint velocityBuffer0 = _velocityBuffers[0];
    nint velocityBuffer1 = _velocityBuffers[1];
    nint velocityBuffer2 = _velocityBuffers[2];
    nint stressBuffer0 = _stressBuffers[0];
    nint stressBuffer1 = _stressBuffers[1];
    nint stressBuffer2 = _stressBuffers[2];
    nint stressBuffer3 = _stressBuffers[3];
    nint stressBuffer4 = _stressBuffers[4];
    nint stressBuffer5 = _stressBuffers[5];
    nint damageBuffer = _damageBuffer;
    
    // For value types, create local copies
    float lambda = _lambda;
    float mu = _mu;
    float dt = _dt;
    float dx = _params.PixelSize;
    int width = _params.Width;
    int height = _params.Height;
    int depth = _params.Depth;
    byte selectedMaterial = _params.SelectedMaterialID;
    
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
    _cl.SetKernelArg(stressKernel, 12, (nuint)sizeof(float), &lambda);
    _cl.SetKernelArg(stressKernel, 13, (nuint)sizeof(float), &mu);
    _cl.SetKernelArg(stressKernel, 14, (nuint)sizeof(float), &dt);
    _cl.SetKernelArg(stressKernel, 15, (nuint)sizeof(float), &dx);
    _cl.SetKernelArg(stressKernel, 16, (nuint)sizeof(int), &width);
    _cl.SetKernelArg(stressKernel, 17, (nuint)sizeof(int), &height);
    _cl.SetKernelArg(stressKernel, 18, (nuint)sizeof(int), &depth);
    _cl.SetKernelArg(stressKernel, 19, (nuint)sizeof(byte), &selectedMaterial);

    // Execute kernel
    nuint globalSize = (nuint)_totalCells;
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
    _cl.SetKernelArg(velocityKernel, 11, (nuint)sizeof(float), &dt);
    _cl.SetKernelArg(velocityKernel, 12, (nuint)sizeof(float), &dx);
    _cl.SetKernelArg(velocityKernel, 13, (nuint)sizeof(int), &width);
    _cl.SetKernelArg(velocityKernel, 14, (nuint)sizeof(int), &height);
    _cl.SetKernelArg(velocityKernel, 15, (nuint)sizeof(int), &depth);
    _cl.SetKernelArg(velocityKernel, 16, (nuint)sizeof(byte), &selectedMaterial);
    
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
                for (int y = 1; y < _params.Height - 1; y++)
                    for (int x = 1; x < _params.Width - 1; x++)
                    {
                        if (volumeLabels[x, y, z] != _params.SelectedMaterialID) continue;

                        // Calculate velocity gradients (staggered grid)
                        float dvx_dx = (_vx[x + 1, y, z] - _vx[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dvy_dy = (_vy[x, y + 1, z] - _vy[x, y - 1, z]) / (2 * _params.PixelSize);
                        float dvz_dz = (_vz[x, y, z + 1] - _vz[x, y, z - 1]) / (2 * _params.PixelSize);
                        
                        float dvy_dx = (_vy[x + 1, y, z] - _vy[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dvx_dy = (_vx[x, y + 1, z] - _vx[x, y - 1, z]) / (2 * _params.PixelSize);
                        float dvz_dx = (_vz[x + 1, y, z] - _vz[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dvx_dz = (_vx[x, y, z + 1] - _vx[x, y, z - 1]) / (2 * _params.PixelSize);
                        float dvz_dy = (_vz[x, y + 1, z] - _vz[x, y - 1, z]) / (2 * _params.PixelSize);
                        float dvy_dz = (_vy[x, y, z + 1] - _vy[x, y, z - 1]) / (2 * _params.PixelSize);
                        
                        float volumetricStrain = dvx_dx + dvy_dy + dvz_dz;

                        // Update stress (elastic model with damage)
                        float damping = 1.0f - _damage[x, y, z] * 0.9f;
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
                for (int y = 1; y < _params.Height - 1; y++)
                    for (int x = 1; x < _params.Width - 1; x++)
                    {
                        if (volumeLabels[x, y, z] != _params.SelectedMaterialID) continue;

                        float rho = _rho[x, y, z];

                        // Calculate stress gradients
                        float dsxx_dx = (_sxx[x + 1, y, z] - _sxx[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dsyy_dy = (_syy[x, y + 1, z] - _syy[x, y - 1, z]) / (2 * _params.PixelSize);
                        float dszz_dz = (_szz[x, y, z + 1] - _szz[x, y, z - 1]) / (2 * _params.PixelSize);
                        
                        float dsxy_dy = (_sxy[x, y + 1, z] - _sxy[x, y - 1, z]) / (2 * _params.PixelSize);
                        float dsxy_dx = (_sxy[x + 1, y, z] - _sxy[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dsxz_dz = (_sxz[x, y, z + 1] - _sxz[x, y, z - 1]) / (2 * _params.PixelSize);
                        float dsxz_dx = (_sxz[x + 1, y, z] - _sxz[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dsyz_dz = (_syz[x, y, z + 1] - _syz[x, y, z - 1]) / (2 * _params.PixelSize);
                        float dsyz_dy = (_syz[x, y + 1, z] - _syz[x, y - 1, z]) / (2 * _params.PixelSize);

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
            float mean = (_sxx[x, y, z] + _syy[x, y, z] + _szz[x, y, z]) / 3.0f;
            float dev_xx = _sxx[x, y, z] - mean;
            float dev_yy = _syy[x, y, z] - mean;
            float dev_zz = _szz[x, y, z] - mean;
            
            float J2 = 0.5f * (dev_xx * dev_xx + dev_yy * dev_yy + dev_zz * dev_zz + 
                              2 * (_sxy[x, y, z] * _sxy[x, y, z] + 
                                   _sxz[x, y, z] * _sxz[x, y, z] + 
                                   _syz[x, y, z] * _syz[x, y, z]));
            float tau = (float)Math.Sqrt(J2);

            float sinPhi = (float)Math.Sin(_params.FailureAngleDeg * Math.PI / 180.0);
            float cosPhi = (float)Math.Cos(_params.FailureAngleDeg * Math.PI / 180.0);
            float cohesionPa = _params.CohesionMPa * 1e6f;
            float p = -mean + _params.ConfiningPressureMPa * 1e6f;

            float yield = tau + p * sinPhi - cohesionPa * cosPhi;
            
            if (yield > 0 && tau > 1e-10f)
            {
                float scale = (tau - (cohesionPa * cosPhi - p * sinPhi)) / tau;
                scale = Math.Min(scale, 0.95f);

                dev_xx *= (1 - scale);
                dev_yy *= (1 - scale);
                dev_zz *= (1 - scale);
                _sxy[x, y, z] *= (1 - scale);
                _sxz[x, y, z] *= (1 - scale);
                _syz[x, y, z] *= (1 - scale);

                _sxx[x, y, z] = dev_xx + mean;
                _syy[x, y, z] = dev_yy + mean;
                _szz[x, y, z] = dev_zz + mean;
            }
        }

        private void ApplyBrittleModel(int x, int y, int z)
        {
            // Calculate maximum principal stress
            float I1 = _sxx[x, y, z] + _syy[x, y, z] + _szz[x, y, z];
            float sigmaMax = I1 / 3.0f; // Simplified

            float tensileStrengthPa = _params.TensileStrengthMPa * 1e6f;
            
            if (sigmaMax > tensileStrengthPa && _damage[x, y, z] < 1.0f)
            {
                float incr = (sigmaMax - tensileStrengthPa) / tensileStrengthPa;
                incr = Math.Min(incr, 0.1f);
                _damage[x, y, z] = Math.Min(0.95f, _damage[x, y, z] + incr * 0.01f);

                float factor = 1.0f - _damage[x, y, z];
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
            int rx = (int)(_params.RxPosition.X * _params.Width);
            int ry = (int)(_params.RxPosition.Y * _params.Height);
            int rz = (int)(_params.RxPosition.Z * _params.Depth);

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
            int rx = (int)(_params.RxPosition.X * _params.Width);
            int ry = (int)(_params.RxPosition.Y * _params.Height);
            int rz = (int)(_params.RxPosition.Z * _params.Depth);

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
            int tx = (int)(_params.TxPosition.X * _params.Width);
            int ty = (int)(_params.TxPosition.Y * _params.Height);
            int tz = (int)(_params.TxPosition.Z * _params.Depth);
            int rx = (int)(_params.RxPosition.X * _params.Width);
            int ry = (int)(_params.RxPosition.Y * _params.Height);
            int rz = (int)(_params.RxPosition.Z * _params.Depth);

            return (float)Math.Sqrt((tx - rx) * (tx - rx) +
                                   (ty - ry) * (ty - ry) +
                                   (tz - rz) * (tz - rz)) * _params.PixelSize;
        }

        private float[,,] GetCombinedWaveField()
        {
            var combined = new float[_params.Width, _params.Height, _params.Depth];
            
            for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                    {
                        float vx = _vx[x, y, z];
                        float vy = _vy[x, y, z];
                        float vz = _vz[x, y, z];
                        combined[x, y, z] = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    }
            
            return combined;
        }

        public float[,,] GetFinalWaveField(int component)
        {
            var field = new float[_params.Width, _params.Height, _params.Depth];
            
            if (_useGPU)
            {
                // Download from GPU
                DownloadFieldFromGPU(component, field);
            }
            else
            {
                // Copy from CPU arrays
                for (int z = 0; z < _params.Depth; z++)
                    for (int y = 0; y < _params.Height; y++)
                        for (int x = 0; x < _params.Width; x++)
                        {
                            switch (component)
                            {
                                case 0: field[x, y, z] = _vx[x, y, z]; break;
                                case 1: field[x, y, z] = _vy[x, y, z]; break;
                                case 2: field[x, y, z] = _vz[x, y, z]; break;
                            }
                        }
            }

            return field;
        }

        private unsafe void DownloadFieldFromGPU(int component, float[,,] field)
        {
            // Download data from GPU buffer
            int size = _totalCells;
            float[] data = new float[size];
            
            fixed (float* dataPtr = data)
            {
                _cl.EnqueueReadBuffer(_commandQueue, _velocityBuffers[component], true, 0,
                    (nuint)(size * sizeof(float)), dataPtr, 0, null, null);
            }

            // Convert to 3D array
            int idx = 0;
            for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                        field[x, y, z] = data[idx++];
        }

        public void Dispose()
        {
            if (_useGPU && _cl != null)
            {
                _cl.ReleaseMemObject(_materialBuffer);
                _cl.ReleaseMemObject(_densityBuffer);
                
                for (int i = 0; i < 3; i++)
                    if (_velocityBuffers[i] != 0)
                        _cl.ReleaseMemObject(_velocityBuffers[i]);
                
                for (int i = 0; i < 6; i++)
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
        private readonly List<CalibrationPoint> _calibrationPoints = new List<CalibrationPoint>();

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

        public bool HasCalibration() => _calibrationPoints.Count >= 2;

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
            
            float totalDist = Math.Abs(p1.Density - density) + Math.Abs(p2.Density - density);
            if (totalDist < 0.001f)
                return (p1.YoungsModulusMPa, p1.PoissonRatio);

            float weight1 = 1.0f - (Math.Abs(p1.Density - density) / totalDist);
            float weight2 = 1.0f - weight1;

            float E = p1.YoungsModulusMPa * weight1 + p2.YoungsModulusMPa * weight2;
            float nu = p1.PoissonRatio * weight1 + p2.PoissonRatio * weight2;

            return (E, nu);
        }

        public string GetCalibrationSummary()
        {
            if (_calibrationPoints.Count == 0)
                return "No calibration points available.";

            var sb = new StringBuilder();
            sb.AppendLine($"Calibration Points: {_calibrationPoints.Count}");
            
            foreach (var point in _calibrationPoints.Take(3))
            {
                sb.AppendLine($"  • {point.MaterialName}: Vp/Vs={point.MeasuredVpVsRatio:F3}, ρ={point.Density:F1} kg/m³");
            }

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
}