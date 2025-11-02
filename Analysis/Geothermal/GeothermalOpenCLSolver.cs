// GeoscientistToolkit/Analysis/Geothermal/GeothermalOpenCLSolver.cs

using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     OpenCL accelerated geothermal simulation solver using Silk.NET OpenCL 1.2.
///     Provides GPU acceleration while maintaining identical results to CPU implementation.
/// </summary>
public class GeothermalOpenCLSolver : IDisposable
{
    private readonly CL _cl;
    private nint _boundaryConditionsKernel;
    private nint _conductivityBuffer;
    private nint _context;
    private nint _densityBuffer;
    private nint _device;
    private nint _dispersionBuffer;

    // Kernels
    private nint _heatTransferKernel;

    private bool _isInitialized;
    private nint _maxChangeBuffer;
    private nint _newTempBuffer;
    private int _nr, _nth, _nz;
    private nint _program;
    private nint _queue;
    private nint _rCoordBuffer;
    private nint _reductionKernel;
    private nint _specificHeatBuffer;

    // Buffers
    private nint _temperatureBuffer;
    private nint _temperatureOldBuffer;
    private nint _velocityBuffer;
    private nint _zCoordBuffer;

    public GeothermalOpenCLSolver()
    {
        _cl = CL.GetApi();
        IsAvailable = InitializeOpenCL();
    }

    public bool IsAvailable { get; }
    public string DeviceName { get; private set; }
    public string DeviceVendor { get; private set; }
    public ulong DeviceGlobalMemory { get; private set; }

    public void Dispose()
    {
        if (_maxChangeBuffer != 0) _cl.ReleaseMemObject(_maxChangeBuffer);
        if (_zCoordBuffer != 0) _cl.ReleaseMemObject(_zCoordBuffer);
        if (_rCoordBuffer != 0) _cl.ReleaseMemObject(_rCoordBuffer);
        if (_dispersionBuffer != 0) _cl.ReleaseMemObject(_dispersionBuffer);
        if (_velocityBuffer != 0) _cl.ReleaseMemObject(_velocityBuffer);
        if (_specificHeatBuffer != 0) _cl.ReleaseMemObject(_specificHeatBuffer);
        if (_densityBuffer != 0) _cl.ReleaseMemObject(_densityBuffer);
        if (_conductivityBuffer != 0) _cl.ReleaseMemObject(_conductivityBuffer);
        if (_newTempBuffer != 0) _cl.ReleaseMemObject(_newTempBuffer);
        if (_temperatureOldBuffer != 0) _cl.ReleaseMemObject(_temperatureOldBuffer);
        if (_temperatureBuffer != 0) _cl.ReleaseMemObject(_temperatureBuffer);

        if (_reductionKernel != 0) _cl.ReleaseKernel(_reductionKernel);
        if (_boundaryConditionsKernel != 0) _cl.ReleaseKernel(_boundaryConditionsKernel);
        if (_heatTransferKernel != 0) _cl.ReleaseKernel(_heatTransferKernel);

        if (_program != 0) _cl.ReleaseProgram(_program);
        if (_queue != 0) _cl.ReleaseCommandQueue(_queue);
        if (_context != 0) _cl.ReleaseContext(_context);

        _cl?.Dispose();
    }

    /// <summary>
    ///     Initializes OpenCL context and compiles kernels.
    /// </summary>
    private unsafe bool InitializeOpenCL()
    {
        try
        {
            // Get platform
            uint numPlatforms;
            _cl.GetPlatformIDs(0, null, &numPlatforms);

            if (numPlatforms == 0)
            {
                Console.WriteLine("No OpenCL platforms found.");
                return false;
            }

            var platforms = new nint[numPlatforms];
            fixed (nint* platformsPtr = platforms)
            {
                _cl.GetPlatformIDs(numPlatforms, platformsPtr, null);
            }

            // Get device (prefer GPU)
            foreach (var platform in platforms)
            {
                uint numDevices;
                _cl.GetDeviceIDs(platform, DeviceType.Gpu, 0, null, &numDevices);

                if (numDevices > 0)
                {
                    var devices = new nint[numDevices];
                    fixed (nint* devicesPtr = devices)
                    {
                        _cl.GetDeviceIDs(platform, DeviceType.Gpu, numDevices, devicesPtr, null);
                    }

                    _device = devices[0];

                    // Get device info
                    nuint paramSize;
                    var nameBuffer = new byte[256];
                    fixed (byte* namePtr = nameBuffer)
                    {
                        _cl.GetDeviceInfo(_device, (uint)DeviceInfo.Name, 256, namePtr, &paramSize);
                        DeviceName = Encoding.UTF8.GetString(nameBuffer, 0, (int)paramSize - 1);

                        _cl.GetDeviceInfo(_device, (uint)DeviceInfo.Vendor, 256, namePtr, &paramSize);
                        DeviceVendor = Encoding.UTF8.GetString(nameBuffer, 0, (int)paramSize - 1);
                    }

                    ulong globalMem;
                    _cl.GetDeviceInfo(_device, (uint)DeviceInfo.GlobalMemSize, sizeof(ulong), &globalMem, null);
                    DeviceGlobalMemory = globalMem;

                    Console.WriteLine($"OpenCL Device: {DeviceName} ({DeviceVendor})");
                    Console.WriteLine($"Global Memory: {DeviceGlobalMemory / (1024 * 1024)} MB");

                    break;
                }
            }

            // Fallback to CPU if no GPU found
            if (_device == 0)
            {
                uint numDevices;
                _cl.GetDeviceIDs(platforms[0], DeviceType.Cpu, 0, null, &numDevices);

                if (numDevices > 0)
                {
                    var devices = new nint[numDevices];
                    fixed (nint* devicesPtr = devices)
                    {
                        _cl.GetDeviceIDs(platforms[0], DeviceType.Cpu, numDevices, devicesPtr, null);
                    }

                    _device = devices[0];
                    Console.WriteLine("Using OpenCL CPU device");
                }
                else
                {
                    Console.WriteLine("No OpenCL devices found.");
                    return false;
                }
            }

            // Create context
            int errCode;
            var device = _device;
            _context = _cl.CreateContext(null, 1, &device, null, null, &errCode);
            if (errCode != 0)
            {
                Console.WriteLine($"Failed to create OpenCL context: {errCode}");
                return false;
            }

            // Create command queue
            device = _device;
            _queue = _cl.CreateCommandQueue(_context, device, (CommandQueueProperties)0, &errCode);
            if (errCode != 0)
            {
                Console.WriteLine($"Failed to create command queue: {errCode}");
                return false;
            }

            // Compile kernels
            if (!CompileKernels())
            {
                Console.WriteLine("Failed to compile OpenCL kernels");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OpenCL initialization error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Compiles OpenCL kernels for heat transfer simulation.
    /// </summary>
    private unsafe bool CompileKernels()
    {
        var kernelSource = GetKernelSource();
        var sourcePtr = Marshal.StringToHGlobalAnsi(kernelSource);

        try
        {
            var sourceLen = (nuint)kernelSource.Length;
            int errCode;

            _program = _cl.CreateProgramWithSource(_context, 1, (byte**)&sourcePtr, &sourceLen, &errCode);
            if (errCode != 0)
            {
                Console.WriteLine($"Failed to create program: {errCode}");
                return false;
            }

            // Build program
            var device = _device;
            errCode = _cl.BuildProgram(_program, 1, &device, (byte*)null, null, null);
            if (errCode != 0)
            {
                // Get build log
                nuint logSize;
                _cl.GetProgramBuildInfo(_program, device, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);

                var log = new byte[logSize];
                fixed (byte* logPtr = log)
                {
                    _cl.GetProgramBuildInfo(_program, device, (uint)ProgramBuildInfo.BuildLog, logSize, logPtr, null);
                }

                Console.WriteLine($"Kernel build failed:\n{Encoding.UTF8.GetString(log)}");
                return false;
            }

            // Create kernels
            _heatTransferKernel = _cl.CreateKernel(_program, "heat_transfer_kernel", &errCode);
            if (errCode != 0)
            {
                Console.WriteLine($"Failed to create heat_transfer_kernel: {errCode}");
                return false;
            }

            _boundaryConditionsKernel = _cl.CreateKernel(_program, "boundary_conditions_kernel", &errCode);
            if (errCode != 0)
            {
                Console.WriteLine($"Failed to create boundary_conditions_kernel: {errCode}");
                return false;
            }

            _reductionKernel = _cl.CreateKernel(_program, "reduction_max_kernel", &errCode);
            if (errCode != 0)
            {
                Console.WriteLine($"Failed to create reduction_max_kernel: {errCode}");
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(sourcePtr);
        }
    }

    /// <summary>
    ///     Initializes GPU buffers for simulation data.
    /// </summary>
    public unsafe bool InitializeBuffers(GeothermalMesh mesh, GeothermalSimulationOptions options)
    {
        if (!IsAvailable)
            return false;

        _nr = mesh.RadialPoints;
        _nth = mesh.AngularPoints;
        _nz = mesh.VerticalPoints;

        try
        {
            int errCode;
            var totalSize = _nr * _nth * _nz;

            // Create buffers
            _temperatureBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "temperatureBuffer");

            _temperatureOldBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "temperatureOldBuffer");

            _newTempBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "newTempBuffer");

            _conductivityBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "conductivityBuffer");

            _densityBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "densityBuffer");

            _specificHeatBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "specificHeatBuffer");

            _velocityBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * 3 * sizeof(float)), null, &errCode);
            CheckError(errCode, "velocityBuffer");

            _dispersionBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "dispersionBuffer");

            _rCoordBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(_nr * sizeof(float)), null, &errCode);
            CheckError(errCode, "rCoordBuffer");

            _zCoordBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(_nz * sizeof(float)), null, &errCode);
            CheckError(errCode, "zCoordBuffer");

            _maxChangeBuffer = _cl.CreateBuffer(_context, MemFlags.WriteOnly,
                256 * sizeof(float), null, &errCode);
            CheckError(errCode, "maxChangeBuffer");

            // Upload mesh data to GPU
            UploadMeshData(mesh);

            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize OpenCL buffers: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Uploads mesh data to GPU.
    /// </summary>
    private unsafe void UploadMeshData(GeothermalMesh mesh)
    {
        var totalSize = _nr * _nth * _nz;

        // Flatten 3D arrays to 1D for GPU upload
        var conductivity = new float[totalSize];
        var density = new float[totalSize];
        var specificHeat = new float[totalSize];

        var idx = 0;
        for (var i = 0; i < _nr; i++)
        for (var j = 0; j < _nth; j++)
        for (var k = 0; k < _nz; k++)
        {
            conductivity[idx] = mesh.ThermalConductivities[i, j, k];
            density[idx] = mesh.Densities[i, j, k];
            specificHeat[idx] = mesh.SpecificHeats[i, j, k];
            idx++;
        }

        // Upload data to GPU
        fixed (float* conductivityPtr = conductivity)
        {
            _cl.EnqueueWriteBuffer(_queue, _conductivityBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), conductivityPtr, 0, null, null);
        }

        fixed (float* densityPtr = density)
        {
            _cl.EnqueueWriteBuffer(_queue, _densityBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), densityPtr, 0, null, null);
        }

        fixed (float* specificHeatPtr = specificHeat)
        {
            _cl.EnqueueWriteBuffer(_queue, _specificHeatBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), specificHeatPtr, 0, null, null);
        }

        fixed (float* rPtr = mesh.R)
        {
            _cl.EnqueueWriteBuffer(_queue, _rCoordBuffer, true, 0,
                (nuint)(_nr * sizeof(float)), rPtr, 0, null, null);
        }

        fixed (float* zPtr = mesh.Z)
        {
            _cl.EnqueueWriteBuffer(_queue, _zCoordBuffer, true, 0,
                (nuint)(_nz * sizeof(float)), zPtr, 0, null, null);
        }
    }

    /// <summary>
    ///     Solves heat transfer on GPU for one iteration.
    ///     Returns maximum temperature change.
    /// </summary>
    public unsafe float SolveHeatTransferGPU(
        float[,,] temperature,
        float[,,,] velocity,
        float[,,] dispersion,
        float dt,
        bool simulateGroundwater)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("OpenCL solver not initialized");

        var totalSize = _nr * _nth * _nz;

        // Upload current temperature to GPU
        var tempFlat = FlattenArray(temperature);
        fixed (float* tempPtr = tempFlat)
        {
            _cl.EnqueueWriteBuffer(_queue, _temperatureBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), tempPtr, 0, null, null);
        }

        // Upload velocity and dispersion if groundwater flow is enabled
        if (simulateGroundwater)
        {
            var velocityFlat = FlattenVelocityArray(velocity);
            var dispersionFlat = FlattenArray(dispersion);

            fixed (float* velPtr = velocityFlat)
            {
                _cl.EnqueueWriteBuffer(_queue, _velocityBuffer, true, 0,
                    (nuint)(totalSize * 3 * sizeof(float)), velPtr, 0, null, null);
            }

            fixed (float* dispPtr = dispersionFlat)
            {
                _cl.EnqueueWriteBuffer(_queue, _dispersionBuffer, true, 0,
                    (nuint)(totalSize * sizeof(float)), dispPtr, 0, null, null);
            }
        }

        // CRITICAL FIX: Zero out maxChangeBuffer before each iteration
        // Without this, old values accumulate and convergence never occurs
        var zeroBuffer = new float[256];
        fixed (float* zeroPtr = zeroBuffer)
        {
            _cl.EnqueueWriteBuffer(_queue, _maxChangeBuffer, true, 0,
                256 * sizeof(float), zeroPtr, 0, null, null);
        }

        // Set kernel arguments
        var argIdx = 0;
        var tempBuffer = _temperatureBuffer;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        tempBuffer = _newTempBuffer;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        tempBuffer = _conductivityBuffer;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        tempBuffer = _densityBuffer;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        tempBuffer = _specificHeatBuffer;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        tempBuffer = _velocityBuffer;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        tempBuffer = _dispersionBuffer;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        tempBuffer = _rCoordBuffer;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        tempBuffer = _zCoordBuffer;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        tempBuffer = _maxChangeBuffer;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, sizeof(float), &dt);
        var tempInt = _nr;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, sizeof(int), &tempInt);
        tempInt = _nth;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, sizeof(int), &tempInt);
        tempInt = _nz;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, sizeof(int), &tempInt);
        var gwFlow = simulateGroundwater ? 1 : 0;
        _cl.SetKernelArg(_heatTransferKernel, (uint)argIdx++, sizeof(int), &gwFlow);

        // Execute kernel
        var globalWorkSize = (nuint)((_nr - 2) * _nth * (_nz - 2));
        _cl.EnqueueNdrangeKernel(_queue, _heatTransferKernel, 1, null, &globalWorkSize, null, 0, null, null);

        // Execute reduction to find max change
        var reductionSize = (nuint)256;
        argIdx = 0;
        tempBuffer = _maxChangeBuffer;
        _cl.SetKernelArg(_reductionKernel, (uint)argIdx++, (nuint)sizeof(nint), &tempBuffer);
        _cl.SetKernelArg(_reductionKernel, (uint)argIdx++, 256 * sizeof(float), null); // local memory
        tempInt = totalSize;
        _cl.SetKernelArg(_reductionKernel, (uint)argIdx++, sizeof(int), &tempInt);

        _cl.EnqueueNdrangeKernel(_queue, _reductionKernel, 1, null, &reductionSize, &reductionSize, 0, null, null);

        // Read back max change
        var maxChanges = new float[256];
        fixed (float* maxChangePtr = maxChanges)
        {
            _cl.EnqueueReadBuffer(_queue, _maxChangeBuffer, true, 0,
                256 * sizeof(float), maxChangePtr, 0, null, null);
        }

        var maxChange = maxChanges.Max();

        // Read back new temperature to CPU
        var newTempFlat = new float[totalSize];
        fixed (float* newTempPtr = newTempFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _newTempBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), newTempPtr, 0, null, null);
        }

        // Copy back to 3D array
        UnflattenArray(newTempFlat, temperature);

        return maxChange;
    }

    /// <summary>
    ///     Flattens 3D temperature array for GPU upload.
    /// </summary>
    private float[] FlattenArray(float[,,] array)
    {
        var result = new float[_nr * _nth * _nz];
        var idx = 0;

        for (var i = 0; i < _nr; i++)
        for (var j = 0; j < _nth; j++)
        for (var k = 0; k < _nz; k++)
            result[idx++] = array[i, j, k];

        return result;
    }

    /// <summary>
    ///     Flattens 4D velocity array for GPU upload.
    /// </summary>
    private float[] FlattenVelocityArray(float[,,,] array)
    {
        var result = new float[_nr * _nth * _nz * 3];
        var idx = 0;

        for (var i = 0; i < _nr; i++)
        for (var j = 0; j < _nth; j++)
        for (var k = 0; k < _nz; k++)
        {
            result[idx++] = array[i, j, k, 0]; // vr
            result[idx++] = array[i, j, k, 1]; // vtheta
            result[idx++] = array[i, j, k, 2]; // vz
        }

        return result;
    }

    /// <summary>
    ///     Unflattens 1D array back to 3D.
    /// </summary>
    private void UnflattenArray(float[] flat, float[,,] array)
    {
        var idx = 0;

        for (var i = 0; i < _nr; i++)
        for (var j = 0; j < _nth; j++)
        for (var k = 0; k < _nz; k++)
            array[i, j, k] = flat[idx++];
    }

    /// <summary>
    ///     Gets the OpenCL kernel source code.
    /// </summary>
    private string GetKernelSource()
    {
        return @"
// OpenCL 1.2 kernels for geothermal heat transfer simulation
// Must produce IDENTICAL results to CPU implementation

#define IDX(i, j, k, nr, nth, nz) ((i) * (nth) * (nz) + (j) * (nz) + (k))

// Utility function to get global index
inline int get_idx(int i, int j, int k, int nth, int nz) {
    return i * nth * nz + j * nz + k;
}

// Main heat transfer kernel - implements the same algorithm as CPU SolveHeatTransferSinglePoint
__kernel void heat_transfer_kernel(
    __global const float* temperature,      // Current temperature field
    __global float* newTemp,                // Output new temperature
    __global const float* conductivity,     // Thermal conductivity
    __global const float* density,          // Density
    __global const float* specificHeat,     // Specific heat capacity
    __global const float* velocity,         // Velocity field (vr, vtheta, vz)
    __global const float* dispersion,       // Dispersion coefficient
    __global const float* r_coord,          // Radial coordinates
    __global const float* z_coord,          // Vertical coordinates
    __global float* maxChange,              // Output max temperature change
    const float dt,                         // Time step
    const int nr,                          // Radial points
    const int nth,                         // Angular points
    const int nz,                          // Vertical points
    const int simulateGroundwater          // Enable groundwater flow
)
{
    const int gid = get_global_id(0);
    const int total_inner = (nr - 2) * nth * (nz - 2);
    
    if (gid >= total_inner) return;
    
    // Decode global index to (i, j, k)
    const int nz_inner = nz - 2;
    const int k_offset = gid % nz_inner + 1;
    const int j = (gid / nz_inner) % nth;
    const int i = gid / (nz_inner * nth) + 1;
    
    const int k = k_offset;
    
    // Get radial coordinate (with safety check)
    const float r = fmax(0.01f, r_coord[i]);
    
    // Get material properties with clamping (same as CPU)
    const int idx = get_idx(i, j, k, nth, nz);
    const float lambda = clamp(conductivity[idx], 0.1f, 10.0f);
    const float rho = clamp(density[idx], 500.0f, 5000.0f);
    const float cp = clamp(specificHeat[idx], 100.0f, 5000.0f);
    const float alpha_thermal = lambda / (rho * cp);
    
    const float T_old = temperature[idx];
    
    // Calculate grid spacings
    const int jm = (j - 1 + nth) % nth;
    const int jp = (j + 1) % nth;
    
    const float dr_m = fmax(0.001f, r_coord[i] - r_coord[i - 1]);
    const float dr_p = fmax(0.001f, r_coord[i + 1] - r_coord[i]);
    const float dth = 2.0f * M_PI_F / nth;
    const float dz_m = fmax(0.001f, fabs(z_coord[k] - z_coord[k - 1]));
    const float dz_p = fmax(0.001f, fabs(z_coord[k + 1] - z_coord[k]));
    
    // Temperature at neighbors
    const float T_rm = temperature[get_idx(i - 1, j, k, nth, nz)];
    const float T_rp = temperature[get_idx(i + 1, j, k, nth, nz)];
    const float T_zm = temperature[get_idx(i, j, k - 1, nth, nz)];
    const float T_zp = temperature[get_idx(i, j, k + 1, nth, nz)];
    const float T_thm = temperature[get_idx(i, jm, k, nth, nz)];
    const float T_thp = temperature[get_idx(i, jp, k, nth, nz)];
    
    // Laplacian calculation (IDENTICAL to CPU)
    const float d2T_dr2 = (T_rp - 2.0f * T_old + T_rm) / (dr_m * dr_p);
    const float dT_dr = (T_rp - T_rm) / (dr_p + dr_m);
    const float d2T_dth2 = (T_thp - 2.0f * T_old + T_thm) / (r * r * dth * dth);
    const float d2T_dz2 = (T_zp - 2.0f * T_old + T_zm) / (dz_m * dz_p);
    
    const float laplacian = d2T_dr2 + dT_dr / r + d2T_dth2 + d2T_dz2;
    
    // Advection term (if groundwater flow enabled)
    float advection = 0.0f;
    if (simulateGroundwater) {
        const int vel_idx = idx * 3;
        const float vr = velocity[vel_idx];
        const float vth = velocity[vel_idx + 1];
        const float vz = velocity[vel_idx + 2];
        
        // Upwind differencing for stability (IDENTICAL to CPU)
        const float dT_dr_adv = (vr >= 0.0f) ? 
            (T_old - T_rm) / dr_m : 
            (T_rp - T_old) / dr_p;
        
        const float dT_dth = (T_thp - T_thm) / (2.0f * r * dth);
        
        const float dT_dz_adv = (vz >= 0.0f) ? 
            (T_old - T_zm) / dz_m : 
            (T_zp - T_old) / dz_p;
        
        advection = -(vr * dT_dr_adv + vth * dT_dth + vz * dT_dz_adv);
    }
    
    // Thermal dispersion term
    float dispersion_term = 0.0f;
    if (simulateGroundwater && dispersion[idx] > 0.0f) {
        dispersion_term = dispersion[idx] * laplacian;
    }
    
    // Update temperature with limiting (IDENTICAL to CPU)
    float dT = dt * (alpha_thermal * laplacian + dispersion_term + advection);
    dT = clamp(dT, -5.0f, 5.0f);  // Limit to 5K change
    
    const float T_new = clamp(T_old + dT, 273.0f, 473.0f);  // Physical bounds
    
    newTemp[idx] = T_new;
    
    // Store absolute change for reduction
    maxChange[gid % 256] = fmax(maxChange[gid % 256], fabs(dT));
}

// Boundary conditions kernel
__kernel void boundary_conditions_kernel(
    __global float* temperature,
    const int nr,
    const int nth,
    const int nz,
    const int boundaryType,      // 0=Dirichlet, 1=Neumann, 2=Adiabatic
    const float boundaryValue
)
{
    const int gid = get_global_id(0);
    
    // Apply to outer radial boundary
    if (gid < nth * nz) {
        const int j = gid / nz;
        const int k = gid % nz;
        const int idx = get_idx(nr - 1, j, k, nth, nz);
        
        if (boundaryType == 0) {  // Dirichlet
            temperature[idx] = boundaryValue;
        }
        else if (boundaryType == 2) {  // Adiabatic
            temperature[idx] = temperature[get_idx(nr - 2, j, k, nth, nz)];
        }
    }
}

// Parallel reduction to find maximum value
__kernel void reduction_max_kernel(
    __global float* values,
    __local float* scratch,
    const int n
)
{
    const int lid = get_local_id(0);
    const int gid = get_global_id(0);
    
    // Load data into local memory
    scratch[lid] = (gid < n) ? values[gid] : 0.0f;
    
    barrier(CLK_LOCAL_MEM_FENCE);
    
    // Parallel reduction
    for (int offset = get_local_size(0) / 2; offset > 0; offset >>= 1) {
        if (lid < offset) {
            scratch[lid] = fmax(scratch[lid], scratch[lid + offset]);
        }
        barrier(CLK_LOCAL_MEM_FENCE);
    }
    
    // Write result
    if (lid == 0) {
        values[get_group_id(0)] = scratch[0];
    }
}
";
    }

    private void CheckError(int errCode, string operation)
    {
        if (errCode != 0) throw new InvalidOperationException($"OpenCL error during {operation}: {errCode}");
    }
}