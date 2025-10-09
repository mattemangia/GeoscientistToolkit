// GeoscientistToolkit/Analysis/AcousticSimulation/AcousticSimulatorGPU.cs

using System.Text;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

/// <summary>
///     GPU-accelerated acoustic wave propagation using OpenCL via Silk.NET.
/// </summary>
public unsafe class AcousticSimulatorGPU : IAcousticKernel
{
    private readonly CL _cl;
    private readonly SimulationParameters _params;
    private nint _bufE, _bufNu, _bufRho;
    private nint _bufSxx, _bufSyy, _bufSzz;
    private nint _bufSxy, _bufSxz, _bufSyz;

    // Device buffers
    private nint _bufVx, _bufVy, _bufVz;
    private nint _commandQueue;

    private nint _context;
    private bool _initialized;
    private nint _kernelUpdateStress;
    private nint _kernelUpdateVelocity;
    private nint _program;

    private int _width, _height, _depth;

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

        var bufferSize = (nuint)(width * height * depth * sizeof(float));
        int error;

        // Create device buffers
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

        _initialized = true;
        Logger.Log($"[GPU] Allocated {bufferSize * 12 / (1024 * 1024)} MB of device memory");
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

        int error;

        // Upload data to device
        UploadBuffer(_bufVx, vx);
        UploadBuffer(_bufVy, vy);
        UploadBuffer(_bufVz, vz);
        UploadBuffer(_bufSxx, sxx);
        UploadBuffer(_bufSyy, syy);
        UploadBuffer(_bufSzz, szz);
        UploadBuffer(_bufSxy, sxy);
        UploadBuffer(_bufSxz, sxz);
        UploadBuffer(_bufSyz, syz);
        UploadBuffer(_bufE, E);
        UploadBuffer(_bufNu, nu);
        UploadBuffer(_bufRho, rho);

        // Set kernel arguments for stress update
        SetKernelArgs(_kernelUpdateStress, dt, dx);

        // Execute stress kernel
        var globalWorkSize = stackalloc nuint[3];
        globalWorkSize[0] = (nuint)_width;
        globalWorkSize[1] = (nuint)_height;
        globalWorkSize[2] = (nuint)_depth;

        error = _cl.EnqueueNdrangeKernel(_commandQueue, _kernelUpdateStress, 3, null, globalWorkSize, null, 0, null,
            null);
        CheckError(error, "EnqueueNDRangeKernel (stress)");

        // Set kernel arguments for velocity update
        SetKernelArgsVelocity(_kernelUpdateVelocity, dt, dx, dampingFactor);

        // Execute velocity kernel
        error = _cl.EnqueueNdrangeKernel(_commandQueue, _kernelUpdateVelocity, 3, null, globalWorkSize, null, 0, null,
            null);
        CheckError(error, "EnqueueNDRangeKernel (velocity)");

        // Download results
        DownloadBuffer(_bufVx, vx);
        DownloadBuffer(_bufVy, vy);
        DownloadBuffer(_bufVz, vz);
        DownloadBuffer(_bufSxx, sxx);
        DownloadBuffer(_bufSyy, syy);
        DownloadBuffer(_bufSzz, szz);
        DownloadBuffer(_bufSxy, sxy);
        DownloadBuffer(_bufSxz, sxz);
        DownloadBuffer(_bufSyz, syz);

        _cl.Finish(_commandQueue);
    }

    public void Dispose()
    {
        if (_bufVx != 0) _cl.ReleaseMemObject(_bufVx);
        if (_bufVy != 0) _cl.ReleaseMemObject(_bufVy);
        if (_bufVz != 0) _cl.ReleaseMemObject(_bufVz);
        if (_bufSxx != 0) _cl.ReleaseMemObject(_bufSxx);
        if (_bufSyy != 0) _cl.ReleaseMemObject(_bufSyy);
        if (_bufSzz != 0) _cl.ReleaseMemObject(_bufSzz);
        if (_bufSxy != 0) _cl.ReleaseMemObject(_bufSxy);
        if (_bufSxz != 0) _cl.ReleaseMemObject(_bufSxz);
        if (_bufSyz != 0) _cl.ReleaseMemObject(_bufSyz);
        if (_bufE != 0) _cl.ReleaseMemObject(_bufE);
        if (_bufNu != 0) _cl.ReleaseMemObject(_bufNu);
        if (_bufRho != 0) _cl.ReleaseMemObject(_bufRho);

        if (_kernelUpdateStress != 0) _cl.ReleaseKernel(_kernelUpdateStress);
        if (_kernelUpdateVelocity != 0) _cl.ReleaseKernel(_kernelUpdateVelocity);
        if (_program != 0) _cl.ReleaseProgram(_program);
        if (_commandQueue != 0) _cl.ReleaseCommandQueue(_commandQueue);
        if (_context != 0) _cl.ReleaseContext(_context);
    }

    private void InitializeOpenCL()
    {
        int error;

        // Get platform
        uint numPlatforms;
        _cl.GetPlatformIDs(0, null, &numPlatforms);

        if (numPlatforms == 0)
            throw new Exception("No OpenCL platforms found");

        var platforms = stackalloc nint[(int)numPlatforms];
        _cl.GetPlatformIDs(numPlatforms, platforms, null);

        // Get device
        uint numDevices;
        _cl.GetDeviceIDs(platforms[0], DeviceType.Gpu, 0, null, &numDevices);

        if (numDevices == 0)
            _cl.GetDeviceIDs(platforms[0], DeviceType.All, 0, null, &numDevices);

        if (numDevices == 0)
            throw new Exception("No OpenCL devices found");

        var devices = stackalloc nint[(int)numDevices];
        _cl.GetDeviceIDs(platforms[0], DeviceType.Gpu, numDevices, devices, null);

        if (devices[0] == 0)
            _cl.GetDeviceIDs(platforms[0], DeviceType.All, numDevices, devices, null);

        // Create context
        _context = _cl.CreateContext(null, 1, devices, null, null, &error);
        CheckError(error, "CreateContext");

        // Create command queue with explicit cast
        _commandQueue = _cl.CreateCommandQueue(_context, devices[0], (CommandQueueProperties)0, &error);
        CheckError(error, "CreateCommandQueue");

        // Build program
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

        // Build with null options
        error = _cl.BuildProgram(_program, 0, null, (byte*)null, null, null);

        if (error != 0)
        {
            // Get build log
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

        // Create kernels
        _kernelUpdateStress = _cl.CreateKernel(_program, "update_stress", &error);
        CheckError(error, "CreateKernel (stress)");

        _kernelUpdateVelocity = _cl.CreateKernel(_program, "update_velocity", &error);
        CheckError(error, "CreateKernel (velocity)");
    }

    private string GetKernelSource()
    {
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
    
    int idx = x + y * width + z * width * height;
    
    // Get material properties
    float E_local = E[idx];
    float nu_local = clamp(nu[idx], 0.01f, 0.49f);
    
    // Lamé parameters
    float mu = E_local / (2.0f * (1.0f + nu_local));
    float lambda = E_local * nu_local / ((1.0f + nu_local) * (1.0f - 2.0f * nu_local));
    
    // Indices for neighbors
    int idx_xp = (x + 1) + y * width + z * width * height;
    int idx_xm = (x - 1) + y * width + z * width * height;
    int idx_yp = x + (y + 1) * width + z * width * height;
    int idx_ym = x + (y - 1) * width + z * width * height;
    int idx_zp = x + y * width + (z + 1) * width * height;
    int idx_zm = x + y * width + (z - 1) * width * height;
    
    // Velocity gradients
    float dvx_dx = (vx[idx_xp] - vx[idx_xm]) / (2.0f * dx);
    float dvy_dy = (vy[idx_yp] - vy[idx_ym]) / (2.0f * dx);
    float dvz_dz = (vz[idx_zp] - vz[idx_zm]) / (2.0f * dx);
    
    // Update normal stresses
    sxx[idx] += dt * ((lambda + 2.0f * mu) * dvx_dx + lambda * (dvy_dy + dvz_dz));
    syy[idx] += dt * ((lambda + 2.0f * mu) * dvy_dy + lambda * (dvx_dx + dvz_dz));
    szz[idx] += dt * ((lambda + 2.0f * mu) * dvz_dz + lambda * (dvx_dx + dvy_dy));
    
    // Shear strains
    float dvx_dy = (vx[idx_yp] - vx[idx_ym]) / (2.0f * dx);
    float dvy_dx = (vy[idx_xp] - vy[idx_xm]) / (2.0f * dx);
    float dvx_dz = (vx[idx_zp] - vx[idx_zm]) / (2.0f * dx);
    float dvz_dx = (vz[idx_xp] - vz[idx_xm]) / (2.0f * dx);
    float dvy_dz = (vy[idx_zp] - vy[idx_zm]) / (2.0f * dx);
    float dvz_dy = (vz[idx_yp] - vz[idx_ym]) / (2.0f * dx);
    
    // Update shear stresses
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
    
    int idx = x + y * width + z * width * height;
    
    float rho_local = max(rho[idx], 1.0f);
    
    // Indices for neighbors
    int idx_xp = (x + 1) + y * width + z * width * height;
    int idx_xm = (x - 1) + y * width + z * width * height;
    int idx_yp = x + (y + 1) * width + z * width * height;
    int idx_ym = x + (y - 1) * width + z * width * height;
    int idx_zp = x + y * width + (z + 1) * width * height;
    int idx_zm = x + y * width + (z - 1) * width * height;
    
    // Stress gradients
    float dsxx_dx = (sxx[idx_xp] - sxx[idx_xm]) / (2.0f * dx);
    float dsxy_dy = (sxy[idx_yp] - sxy[idx_ym]) / (2.0f * dx);
    float dsxz_dz = (sxz[idx_zp] - sxz[idx_zm]) / (2.0f * dx);
    
    float dsxy_dx = (sxy[idx_xp] - sxy[idx_xm]) / (2.0f * dx);
    float dsyy_dy = (syy[idx_yp] - syy[idx_ym]) / (2.0f * dx);
    float dsyz_dz = (syz[idx_zp] - syz[idx_zm]) / (2.0f * dx);
    
    float dsxz_dx = (sxz[idx_xp] - sxz[idx_xm]) / (2.0f * dx);
    float dsyz_dy = (syz[idx_yp] - syz[idx_ym]) / (2.0f * dx);
    float dszz_dz = (szz[idx_zp] - szz[idx_zm]) / (2.0f * dx);
    
    // Accelerations
    float ax = (dsxx_dx + dsxy_dy + dsxz_dz) / rho_local;
    float ay = (dsxy_dx + dsyy_dy + dsyz_dz) / rho_local;
    float az = (dsxz_dx + dsyz_dy + dszz_dz) / rho_local;
    
    // Update velocities with damping
    float damping = 1.0f - dampingFactor * dt;
    vx[idx] = vx[idx] * damping + dt * ax;
    vy[idx] = vy[idx] * damping + dt * ay;
    vz[idx] = vz[idx] * damping + dt * az;
}
";
    }

    private void SetKernelArgs(nint kernel, float dt, float dx)
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
        SetKernelArg(kernel, arg++, _width);
        SetKernelArg(kernel, arg++, _height);
        SetKernelArg(kernel, arg++, _depth);
    }

    private void SetKernelArgsVelocity(nint kernel, float dt, float dx, float damping)
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
        SetKernelArg(kernel, arg++, _width);
        SetKernelArg(kernel, arg++, _height);
        SetKernelArg(kernel, arg++, _depth);
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