// GeoscientistToolkit/Analysis/AcousticSimulation/AcousticSimulatorGPU.cs
// FIXED VERSION - Correct memory layout mapping

using System.Text;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

public unsafe class AcousticSimulatorGPU : IAcousticKernel
{
    private readonly CL _cl;
    private readonly SimulationParameters _params;
    private nint _bufE, _bufNu, _bufRho;
    private nint _bufSxx, _bufSyy, _bufSzz;
    private nint _bufSxy, _bufSxz, _bufSyz;

    private nint _bufVx, _bufVy, _bufVz;

    private nint _commandQueue;
    private nint _context;
    private nint _device;
    private List<nint> _devices; // Multi-GPU support: all devices in context
    private List<nint> _queues; // Multi-GPU support: command queue per device
    private bool _multiGPUMode;
    private nuint _currentBufferSize;

    private int _currentBufferWidth, _currentBufferHeight, _currentBufferDepth;

    private bool _initialized;
    private nint _kernelUpdateStress;
    private nint _kernelUpdateVelocity;
    private nint _program;

    private int _width, _height, _depth;

    private string DeviceName;
    private string DeviceVendor;
    private nuint DeviceGlobalMemory;

    public AcousticSimulatorGPU(SimulationParameters parameters)
    {
        _params = parameters;
        _cl = CL.GetApi();
        InitializeOpenCL();
    }

    public void Initialize(int width, int height, int depth)
    {
        _width = width;
        _height = height;
        _depth = depth;
        _initialized = true;
        Logger.Log($"[GPU] Initialized for volume {width}x{height}x{depth}");
    }

    public void UpdateWaveField(
        float[,,] vx, float[,,] vy, float[,,] vz,
        float[,,] sxx, float[,,] syy, float[,,] szz,
        float[,,] sxy, float[,,] sxz, float[,,] syz,
        float[,,] E, float[,,] nu, float[,,] rho,
        float dt, float dx, float dampingFactor)
    {
        if (!_initialized)
            throw new InvalidOperationException("GPU kernel not initialized");

        var chunkWidth = vx.GetLength(0);
        var chunkHeight = vx.GetLength(1);
        var chunkDepth = vx.GetLength(2);

        EnsureBuffersAllocated(chunkWidth, chunkHeight, chunkDepth);

        // Upload material properties (must be done for every chunk)
        UploadBuffer(_bufE, E);
        UploadBuffer(_bufNu, nu);
        UploadBuffer(_bufRho, rho);

        // Upload wave fields
        UploadBuffer(_bufVx, vx);
        UploadBuffer(_bufVy, vy);
        UploadBuffer(_bufVz, vz);
        UploadBuffer(_bufSxx, sxx);
        UploadBuffer(_bufSyy, syy);
        UploadBuffer(_bufSzz, szz);
        UploadBuffer(_bufSxy, sxy);
        UploadBuffer(_bufSxz, sxz);
        UploadBuffer(_bufSyz, syz);

        // Execute stress update kernel
        SetKernelArgs(_kernelUpdateStress, dt, dx, chunkWidth, chunkHeight, chunkDepth);

        var globalWorkSize = stackalloc nuint[3];
        globalWorkSize[0] = (nuint)chunkWidth;
        globalWorkSize[1] = (nuint)chunkHeight;
        globalWorkSize[2] = (nuint)chunkDepth;

        var error = _cl.EnqueueNdrangeKernel(_commandQueue, _kernelUpdateStress, 3, null, globalWorkSize, null, 0, null,
            null);
        CheckError(error, "EnqueueNDRangeKernel (stress)");

        // Execute velocity update kernel
        SetKernelArgsVelocity(_kernelUpdateVelocity, dt, dx, dampingFactor, chunkWidth, chunkHeight, chunkDepth);

        error = _cl.EnqueueNdrangeKernel(_commandQueue, _kernelUpdateVelocity, 3, null, globalWorkSize, null, 0, null,
            null);
        CheckError(error, "EnqueueNDRangeKernel (velocity)");

        // Download results (blocking reads ensure completion)
        DownloadBuffer(_bufVx, vx);
        DownloadBuffer(_bufVy, vy);
        DownloadBuffer(_bufVz, vz);
        DownloadBuffer(_bufSxx, sxx);
        DownloadBuffer(_bufSyy, syy);
        DownloadBuffer(_bufSzz, szz);
        DownloadBuffer(_bufSxy, sxy);
        DownloadBuffer(_bufSxz, sxz);
        DownloadBuffer(_bufSyz, syz);
    }

    public void Dispose()
    {
        if (_commandQueue != 0)
            _cl.Finish(_commandQueue);

        ReleaseBuffers();

        if (_kernelUpdateStress != 0) _cl.ReleaseKernel(_kernelUpdateStress);
        if (_kernelUpdateVelocity != 0) _cl.ReleaseKernel(_kernelUpdateVelocity);
        if (_program != 0) _cl.ReleaseProgram(_program);

        // Multi-GPU: Release all command queues
        if (_queues != null)
        {
            foreach (var queue in _queues)
            {
                if (queue != 0) _cl.ReleaseCommandQueue(queue);
            }
        }
        if (_commandQueue != 0) _cl.ReleaseCommandQueue(_commandQueue);
        if (_context != 0) _cl.ReleaseContext(_context);
    }

    private void EnsureBuffersAllocated(int width, int height, int depth)
    {
        var bufferSize = (nuint)(width * height * depth * sizeof(float));

        if (_currentBufferSize == bufferSize &&
            _currentBufferWidth == width &&
            _currentBufferHeight == height &&
            _currentBufferDepth == depth)
            return;

        ReleaseBuffers();

        int error;

        _bufVx = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Vx");
        _bufVy = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Vy");
        _bufVz = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Vz");
        _bufSxx = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Sxx");
        _bufSyy = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Syy");
        _bufSzz = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Szz");
        _bufSxy = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Sxy");
        _bufSxz = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Sxz");
        _bufSyz = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Syz");
        _bufE = _cl.CreateBuffer(_context, MemFlags.ReadOnly, bufferSize, null, &error);
        CheckError(error, "CreateBuffer E");
        _bufNu = _cl.CreateBuffer(_context, MemFlags.ReadOnly, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Nu");
        _bufRho = _cl.CreateBuffer(_context, MemFlags.ReadOnly, bufferSize, null, &error);
        CheckError(error, "CreateBuffer Rho");

        _currentBufferWidth = width;
        _currentBufferHeight = height;
        _currentBufferDepth = depth;
        _currentBufferSize = bufferSize;

        Logger.Log($"[GPU] Allocated {bufferSize * 12 / (1024 * 1024)} MB for chunk {width}x{height}x{depth}");
    }

    private void ReleaseBuffers()
    {
        if (_bufVx != 0)
        {
            _cl.ReleaseMemObject(_bufVx);
            _bufVx = 0;
        }

        if (_bufVy != 0)
        {
            _cl.ReleaseMemObject(_bufVy);
            _bufVy = 0;
        }

        if (_bufVz != 0)
        {
            _cl.ReleaseMemObject(_bufVz);
            _bufVz = 0;
        }

        if (_bufSxx != 0)
        {
            _cl.ReleaseMemObject(_bufSxx);
            _bufSxx = 0;
        }

        if (_bufSyy != 0)
        {
            _cl.ReleaseMemObject(_bufSyy);
            _bufSyy = 0;
        }

        if (_bufSzz != 0)
        {
            _cl.ReleaseMemObject(_bufSzz);
            _bufSzz = 0;
        }

        if (_bufSxy != 0)
        {
            _cl.ReleaseMemObject(_bufSxy);
            _bufSxy = 0;
        }

        if (_bufSxz != 0)
        {
            _cl.ReleaseMemObject(_bufSxz);
            _bufSxz = 0;
        }

        if (_bufSyz != 0)
        {
            _cl.ReleaseMemObject(_bufSyz);
            _bufSyz = 0;
        }

        if (_bufE != 0)
        {
            _cl.ReleaseMemObject(_bufE);
            _bufE = 0;
        }

        if (_bufNu != 0)
        {
            _cl.ReleaseMemObject(_bufNu);
            _bufNu = 0;
        }

        if (_bufRho != 0)
        {
            _cl.ReleaseMemObject(_bufRho);
            _bufRho = 0;
        }

        _currentBufferSize = 0;
    }

    private void InitializeOpenCL()
    {
        int error;

        Logger.Log("AcousticSimulatorGPU: Initializing OpenCL...");

        // Check if multi-GPU mode is enabled
        _multiGPUMode = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.IsMultiGPUEnabled();

        if (_multiGPUMode)
        {
            // Multi-GPU mode: get all GPU devices
            var deviceInfos = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetAllComputeDevices();

            if (deviceInfos == null || deviceInfos.Count == 0)
            {
                Logger.LogWarning("No OpenCL devices available from OpenCLDeviceManager.");
                throw new Exception("OpenCL device not available");
            }

            _devices = deviceInfos.Select(d => d.Device).ToList();
            _device = _devices[0]; // Primary device for reporting

            DeviceName = string.Join(", ", deviceInfos.Select(d => d.Name));
            DeviceVendor = string.Join(", ", deviceInfos.Select(d => d.Vendor).Distinct());
            DeviceGlobalMemory = (nuint)deviceInfos.Sum(d => (long)d.GlobalMemory);

            Logger.Log($"AcousticSimulatorGPU: Multi-GPU mode with {_devices.Count} devices:");
            foreach (var info in deviceInfos)
            {
                Logger.Log($"  - {info.Name} ({info.Vendor}) - {info.GlobalMemory / (1024 * 1024)} MB");
            }

            // Create context with all devices
            int errCode;
            fixed (nint* devicesPtr = _devices.ToArray())
            {
                _context = _cl.CreateContext(null, (uint)_devices.Count, devicesPtr, null, null, &errCode);
            }
            if (errCode != 0)
            {
                Logger.LogError($"Failed to create multi-device OpenCL context: {errCode}");
                throw new Exception($"Failed to create multi-device OpenCL context: {errCode}");
            }

            // Create command queue for each device
            _queues = new List<nint>();
            foreach (var device in _devices)
            {
                var dev = device;
                var queue = _cl.CreateCommandQueue(_context, dev, (CommandQueueProperties)0, &errCode);
                if (errCode != 0)
                {
                    Logger.LogError($"Failed to create command queue for device: {errCode}");
                    throw new Exception($"Failed to create command queue for device: {errCode}");
                }
                _queues.Add(queue);
            }

            // Use first queue as primary
            _commandQueue = _queues[0];
        }
        else
        {
            // Single GPU mode
            _device = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetComputeDevice();

            if (_device == 0)
            {
                Logger.LogWarning("No OpenCL device available from OpenCLDeviceManager.");
                throw new Exception("OpenCL device not available");
            }

            // Get device info from the centralized manager
            var deviceInfo = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetDeviceInfo();
            DeviceName = deviceInfo.Name;
            DeviceVendor = deviceInfo.Vendor;
            DeviceGlobalMemory = (nuint)deviceInfo.GlobalMemory;

            Logger.Log($"AcousticSimulatorGPU: Using device: {DeviceName} ({DeviceVendor})");
            Logger.Log($"AcousticSimulatorGPU: Global Memory: {DeviceGlobalMemory / (1024 * 1024)} MB");

            // Create context with the device from centralized manager
            var device = _device;
            _context = _cl.CreateContext(null, 1, &device, null, null, &error);
            CheckError(error, "CreateContext");

            // Create command queue with the device from centralized manager
            _commandQueue = _cl.CreateCommandQueue(_context, device, (CommandQueueProperties)0, &error);
            CheckError(error, "CreateCommandQueue");
        }

        BuildProgram();
        Logger.Log("[GPU] OpenCL initialized successfully");
    }

    private void BuildProgram()
    {
        var kernelSource = GetKernelSource();
        var sourceBytes = Encoding.UTF8.GetBytes(kernelSource);

        int error;
        fixed (byte* sourcePtr = sourceBytes)
        {
            var lengths = stackalloc nuint[1];
            lengths[0] = (nuint)sourceBytes.Length;
            var sourcePtrs = stackalloc byte*[1];
            sourcePtrs[0] = sourcePtr;
            _program = _cl.CreateProgramWithSource(_context, 1, sourcePtrs, lengths, &error);
            CheckError(error, "CreateProgramWithSource");
        }

        // Build program for all devices in context
        if (_multiGPUMode && _devices != null)
        {
            fixed (nint* devicesPtr = _devices.ToArray())
            {
                error = _cl.BuildProgram(_program, (uint)_devices.Count, devicesPtr, (byte*)null, null, null);
            }
        }
        else
        {
            var device = _device;
            error = _cl.BuildProgram(_program, 1, &device, (byte*)null, null, null);
        }

        if (error != 0)
        {
            nuint logSize;
            _cl.GetProgramBuildInfo(_program, 0, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);
            var log = new byte[logSize];
            fixed (byte* logPtr = log)
            {
                _cl.GetProgramBuildInfo(_program, 0, (uint)ProgramBuildInfo.BuildLog, logSize, logPtr, null);
            }

            var logString = Encoding.UTF8.GetString(log);
            throw new Exception($"OpenCL build failed: {logString}");
        }

        _kernelUpdateStress = _cl.CreateKernel(_program, "update_stress", &error);
        CheckError(error, "CreateKernel (stress)");
        _kernelUpdateVelocity = _cl.CreateKernel(_program, "update_velocity", &error);
        CheckError(error, "CreateKernel (velocity)");
    }

    private string GetKernelSource()
    {
        // FIXED: Corrected indexing to match C# array layout [x,y,z]
        // C# stores as: x*(H*D) + y*D + z (x varies slowest)
        return @"
__kernel void update_stress(
    __global float* vx, __global float* vy, __global float* vz,
    __global float* sxx, __global float* syy, __global float* szz,
    __global float* sxy, __global float* sxz, __global float* syz,
    __global const float* E, __global const float* nu, __global const float* rho,
    float dt, float dx, int width, int height, int depth)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);
    
    if (x <= 0 || x >= width - 1 || y <= 0 || y >= height - 1 || z <= 0 || z >= depth - 1)
        return;
    
    // FIXED: Match C# array layout [x,y,z]
    int idx = x * (height * depth) + y * depth + z;
    
    float E_local = E[idx];
    float nu_local = clamp(nu[idx], 0.01f, 0.49f);
    
    float mu = E_local / (2.0f * (1.0f + nu_local));
    float lambda = E_local * nu_local / ((1.0f + nu_local) * (1.0f - 2.0f * nu_local));
    
    // Neighbor indices
    int idx_xp = (x + 1) * (height * depth) + y * depth + z;
    int idx_xm = (x - 1) * (height * depth) + y * depth + z;
    int idx_yp = x * (height * depth) + (y + 1) * depth + z;
    int idx_ym = x * (height * depth) + (y - 1) * depth + z;
    int idx_zp = x * (height * depth) + y * depth + (z + 1);
    int idx_zm = x * (height * depth) + y * depth + (z - 1);
    
    float inv_2dx = 1.0f / (2.0f * dx);
    float dvx_dx = (vx[idx_xp] - vx[idx_xm]) * inv_2dx;
    float dvy_dy = (vy[idx_yp] - vy[idx_ym]) * inv_2dx;
    float dvz_dz = (vz[idx_zp] - vz[idx_zm]) * inv_2dx;
    
    float lambda_2mu = lambda + 2.0f * mu;
    sxx[idx] += dt * (lambda_2mu * dvx_dx + lambda * (dvy_dy + dvz_dz));
    syy[idx] += dt * (lambda_2mu * dvy_dy + lambda * (dvx_dx + dvz_dz));
    szz[idx] += dt * (lambda_2mu * dvz_dz + lambda * (dvx_dx + dvy_dy));
    
    float dvx_dy = (vx[idx_yp] - vx[idx_ym]) * inv_2dx;
    float dvy_dx = (vy[idx_xp] - vy[idx_xm]) * inv_2dx;
    float dvx_dz = (vx[idx_zp] - vx[idx_zm]) * inv_2dx;
    float dvz_dx = (vz[idx_xp] - vz[idx_xm]) * inv_2dx;
    float dvy_dz = (vy[idx_zp] - vy[idx_zm]) * inv_2dx;
    float dvz_dy = (vz[idx_yp] - vz[idx_ym]) * inv_2dx;
    
    sxy[idx] += dt * mu * (dvx_dy + dvy_dx);
    sxz[idx] += dt * mu * (dvx_dz + dvz_dx);
    syz[idx] += dt * mu * (dvy_dz + dvz_dy);
}

__kernel void update_velocity(
    __global float* vx, __global float* vy, __global float* vz,
    __global const float* sxx, __global const float* syy, __global const float* szz,
    __global const float* sxy, __global const float* sxz, __global const float* syz,
    __global const float* E, __global const float* nu, __global const float* rho,
    float dt, float dx, float dampingFactor, int width, int height, int depth)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);
    
    if (x <= 0 || x >= width - 1 || y <= 0 || y >= height - 1 || z <= 0 || z >= depth - 1)
        return;
    
    // FIXED: Match C# array layout [x,y,z]
    int idx = x * (height * depth) + y * depth + z;
    
    float rho_local = max(rho[idx], 1.0f);
    float inv_rho = 1.0f / rho_local;
    
    // Neighbor indices
    int idx_xp = (x + 1) * (height * depth) + y * depth + z;
    int idx_xm = (x - 1) * (height * depth) + y * depth + z;
    int idx_yp = x * (height * depth) + (y + 1) * depth + z;
    int idx_ym = x * (height * depth) + (y - 1) * depth + z;
    int idx_zp = x * (height * depth) + y * depth + (z + 1);
    int idx_zm = x * (height * depth) + y * depth + (z - 1);
    
    float inv_2dx = 1.0f / (2.0f * dx);
    float dsxx_dx = (sxx[idx_xp] - sxx[idx_xm]) * inv_2dx;
    float dsxy_dy = (sxy[idx_yp] - sxy[idx_ym]) * inv_2dx;
    float dsxz_dz = (sxz[idx_zp] - sxz[idx_zm]) * inv_2dx;
    
    float dsxy_dx = (sxy[idx_xp] - sxy[idx_xm]) * inv_2dx;
    float dsyy_dy = (syy[idx_yp] - syy[idx_ym]) * inv_2dx;
    float dsyz_dz = (syz[idx_zp] - syz[idx_zm]) * inv_2dx;
    
    float dsxz_dx = (sxz[idx_xp] - sxz[idx_xm]) * inv_2dx;
    float dsyz_dy = (syz[idx_yp] - syz[idx_ym]) * inv_2dx;
    float dszz_dz = (szz[idx_zp] - szz[idx_zm]) * inv_2dx;
    
    float ax = (dsxx_dx + dsxy_dy + dsxz_dz) * inv_rho;
    float ay = (dsxy_dx + dsyy_dy + dsyz_dz) * inv_rho;
    float az = (dsxz_dx + dsyz_dy + dszz_dz) * inv_rho;
    
    float damping = 1.0f - dampingFactor * dt;
    vx[idx] = vx[idx] * damping + dt * ax;
    vy[idx] = vy[idx] * damping + dt * ay;
    vz[idx] = vz[idx] * damping + dt * az;
}
";
    }

    private void SetKernelArgs(nint kernel, float dt, float dx, int width, int height, int depth)
    {
        var arg = 0;
        SetKernelArg(kernel, arg++, _bufVx);
        SetKernelArg(kernel, arg++, _bufVy);
        SetKernelArg(kernel, arg++, _bufVz);
        SetKernelArg(kernel, arg++, _bufSxx);
        SetKernelArg(kernel, arg++, _bufSyy);
        SetKernelArg(kernel, arg++, _bufSzz);
        SetKernelArg(kernel, arg++, _bufSxy);
        SetKernelArg(kernel, arg++, _bufSxz);
        SetKernelArg(kernel, arg++, _bufSyz);
        SetKernelArg(kernel, arg++, _bufE);
        SetKernelArg(kernel, arg++, _bufNu);
        SetKernelArg(kernel, arg++, _bufRho);
        SetKernelArg(kernel, arg++, dt);
        SetKernelArg(kernel, arg++, dx);
        SetKernelArg(kernel, arg++, width);
        SetKernelArg(kernel, arg++, height);
        SetKernelArg(kernel, arg++, depth);
    }

    private void SetKernelArgsVelocity(nint kernel, float dt, float dx, float damping, int width, int height, int depth)
    {
        var arg = 0;
        SetKernelArg(kernel, arg++, _bufVx);
        SetKernelArg(kernel, arg++, _bufVy);
        SetKernelArg(kernel, arg++, _bufVz);
        SetKernelArg(kernel, arg++, _bufSxx);
        SetKernelArg(kernel, arg++, _bufSyy);
        SetKernelArg(kernel, arg++, _bufSzz);
        SetKernelArg(kernel, arg++, _bufSxy);
        SetKernelArg(kernel, arg++, _bufSxz);
        SetKernelArg(kernel, arg++, _bufSyz);
        SetKernelArg(kernel, arg++, _bufE);
        SetKernelArg(kernel, arg++, _bufNu);
        SetKernelArg(kernel, arg++, _bufRho);
        SetKernelArg(kernel, arg++, dt);
        SetKernelArg(kernel, arg++, dx);
        SetKernelArg(kernel, arg++, damping);
        SetKernelArg(kernel, arg++, width);
        SetKernelArg(kernel, arg++, height);
        SetKernelArg(kernel, arg++, depth);
    }

    private void SetKernelArg<T>(nint kernel, int index, T value) where T : unmanaged
    {
        var error = _cl.SetKernelArg(kernel, (uint)index, (nuint)sizeof(T), &value);
        CheckError(error, $"SetKernelArg {index}");
    }

    private void UploadBuffer(nint buffer, float[,,] data)
    {
        fixed (float* ptr = data)
        {
            var size = (nuint)(data.Length * sizeof(float));
            var error = _cl.EnqueueWriteBuffer(_commandQueue, buffer, true, 0, size, ptr, 0, null, null);
            CheckError(error, "EnqueueWriteBuffer");
        }
    }

    private void DownloadBuffer(nint buffer, float[,,] data)
    {
        fixed (float* ptr = data)
        {
            var size = (nuint)(data.Length * sizeof(float));
            var error = _cl.EnqueueReadBuffer(_commandQueue, buffer, true, 0, size, ptr, 0, null, null);
            CheckError(error, "EnqueueReadBuffer");
        }
    }

    private void CheckError(int error, string operation)
    {
        if (error != 0)
            throw new Exception($"OpenCL error in {operation}: {error}");
    }
}