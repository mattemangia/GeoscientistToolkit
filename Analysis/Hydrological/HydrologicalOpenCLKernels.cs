// GeoscientistToolkit/Analysis/Hydrological/HydrologicalOpenCLKernels.cs
//
// GPU-accelerated hydrological computations using OpenCL 1.2
// Provides massive parallelization for water flow, accumulation, and temporal simulations
//

using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.OpenCL;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Hydrological;

/// <summary>
/// OpenCL-accelerated hydrological computations for large-scale water flow simulations
/// </summary>
public unsafe class HydrologicalOpenCLKernels : IDisposable
{
    private readonly CL _cl;
    private readonly nint _device;
    private readonly nint _context;
    private readonly nint _queue;
    private readonly nint _program;

    private nint _flowDirectionKernel;
    private nint _flowAccumulationKernel;
    private nint _rainfallSimulationKernel;
    private nint _drainageKernel;
    private nint _waterBodyDetectionKernel;

    private bool _disposed;

    public HydrologicalOpenCLKernels()
    {
        _cl = CL.GetApi();
        _device = OpenCLDeviceManager.GetComputeDevice();

        if (_device == 0)
        {
            Logger.LogWarning("No OpenCL device available, GPU acceleration disabled");
            return;
        }

        try
        {
            // Create context
            int error;
            _context = _cl.CreateContext(null, 1, &_device, null, null, &error);
            CheckError(error, "CreateContext");

            // Create command queue
            _queue = _cl.CreateCommandQueue(_context, _device, (CommandQueueProperties)0, &error);
            CheckError(error, "CreateCommandQueue");

            // Compile kernels
            var source = GetKernelSource();
            var sourcePtr = (byte*)Marshal.StringToHGlobalAnsi(source);
            var sourceLength = (nuint)source.Length;

            _program = _cl.CreateProgramWithSource(_context, 1, &sourcePtr, &sourceLength, &error);
            Marshal.FreeHGlobal((nint)sourcePtr);
            CheckError(error, "CreateProgramWithSource");

            error = _cl.BuildProgram(_program, 1, &_device, (byte*)null, null, null);
            if (error != (int)ErrorCodes.Success)
            {
                // Get build log
                nuint logSize;
                _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);
                var logBuffer = stackalloc byte[(int)logSize];
                _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.BuildLog, logSize, logBuffer, null);
                var buildLog = Marshal.PtrToStringAnsi((nint)logBuffer);
                Logger.LogError($"OpenCL build error: {buildLog}");
                throw new Exception("Failed to build OpenCL program");
            }

            // Create kernels
            _flowDirectionKernel = _cl.CreateKernel(_program, "calculate_flow_direction", &error);
            CheckError(error, "CreateKernel flow_direction");

            _flowAccumulationKernel = _cl.CreateKernel(_program, "calculate_flow_accumulation", &error);
            CheckError(error, "CreateKernel flow_accumulation");

            _rainfallSimulationKernel = _cl.CreateKernel(_program, "apply_rainfall", &error);
            CheckError(error, "CreateKernel rainfall");

            _drainageKernel = _cl.CreateKernel(_program, "simulate_drainage", &error);
            CheckError(error, "CreateKernel drainage");

            _waterBodyDetectionKernel = _cl.CreateKernel(_program, "detect_water_bodies", &error);
            CheckError(error, "CreateKernel water_bodies");

            Logger.Log("HydrologicalOpenCL: GPU acceleration initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to initialize OpenCL: {ex.Message}");
            Dispose();
        }
    }

    public bool IsAvailable => _device != 0 && _context != 0;

    /// <summary>
    /// Calculate D8 flow direction using GPU
    /// </summary>
    public byte[,] CalculateFlowDirection(float[,] elevation)
    {
        if (!IsAvailable) throw new InvalidOperationException("OpenCL not available");

        int rows = elevation.GetLength(0);
        int cols = elevation.GetLength(1);
        var flowDir = new byte[rows, cols];

        fixed (float* elevPtr = elevation)
        fixed (byte* flowDirPtr = flowDir)
        {
            int error;

            // Create buffers
            var elevBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(rows * cols * sizeof(float)), elevPtr, &error);
            CheckError(error, "CreateBuffer elevation");

            var flowDirBuffer = _cl.CreateBuffer(_context, MemFlags.WriteOnly,
                (nuint)(rows * cols * sizeof(byte)), null, &error);
            CheckError(error, "CreateBuffer flowDir");

            try
            {
                // Set kernel arguments
                _cl.SetKernelArg(_flowDirectionKernel, 0, (nuint)sizeof(nint), &elevBuffer);
                _cl.SetKernelArg(_flowDirectionKernel, 1, (nuint)sizeof(nint), &flowDirBuffer);
                _cl.SetKernelArg(_flowDirectionKernel, 2, (nuint)sizeof(int), &rows);
                _cl.SetKernelArg(_flowDirectionKernel, 3, (nuint)sizeof(int), &cols);

                // Execute kernel
                nuint globalSize = (nuint)(rows * cols);
                nuint localSize = 256; // Work group size
                error = _cl.EnqueueNdrangeKernel(_queue, _flowDirectionKernel, 1, null, &globalSize, &localSize, 0, null, null);
                CheckError(error, "EnqueueNDRangeKernel");

                // Read results
                error = _cl.EnqueueReadBuffer(_queue, flowDirBuffer, true, UIntPtr.Zero, (nuint)(rows * cols * sizeof(byte)), flowDirPtr, 0, null, null);
                CheckError(error, "EnqueueReadBuffer");

                _cl.Finish(_queue);
            }
            finally
            {
                _cl.ReleaseMemObject(elevBuffer);
                _cl.ReleaseMemObject(flowDirBuffer);
            }
        }

        return flowDir;
    }

    /// <summary>
    /// Simulate water drainage with rainfall over time using GPU
    /// </summary>
    public (float[,] waterDepth, float[] volumeHistory) SimulateRainfallDrainage(
        float[,] elevation,
        byte[,] flowDirection,
        float[] rainfallByTimeStep,
        float drainageRate,
        float infiltrationRate)
    {
        if (!IsAvailable) throw new InvalidOperationException("OpenCL not available");

        int rows = elevation.GetLength(0);
        int cols = elevation.GetLength(1);
        int timeSteps = rainfallByTimeStep.Length;

        var waterDepth = new float[rows, cols];
        var volumeHistory = new float[timeSteps];

        fixed (float* elevPtr = elevation)
        fixed (byte* flowDirPtr = flowDirection)
        fixed (float* waterPtr = waterDepth)
        fixed (float* rainfallPtr = rainfallByTimeStep)
        {
            int error;

            // Create buffers
            var elevBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(rows * cols * sizeof(float)), elevPtr, &error);
            CheckError(error, "CreateBuffer elevation");

            var flowDirBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(rows * cols * sizeof(byte)), flowDirPtr, &error);
            CheckError(error, "CreateBuffer flowDir");

            var waterBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(rows * cols * sizeof(float)), null, &error);
            CheckError(error, "CreateBuffer water");

            var waterTempBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(rows * cols * sizeof(float)), null, &error);
            CheckError(error, "CreateBuffer waterTemp");

            try
            {
                // Initialize water depth to zero
                float zero = 0f;
                error = _cl.EnqueueFillBuffer(_queue, waterBuffer, &zero, (nuint)sizeof(float), 0,
                    (nuint)(rows * cols * sizeof(float)), 0, null, null);
                CheckError(error, "EnqueueFillBuffer");

                nuint globalSize = (nuint)(rows * cols);
                nuint localSize = 256;

                // Simulate each time step
                for (int t = 0; t < timeSteps; t++)
                {
                    float rainfall = rainfallPtr[t];

                    // Apply rainfall
                    _cl.SetKernelArg(_rainfallSimulationKernel, 0, (nuint)sizeof(nint), &waterBuffer);
                    _cl.SetKernelArg(_rainfallSimulationKernel, 1, (nuint)sizeof(int), &rows);
                    _cl.SetKernelArg(_rainfallSimulationKernel, 2, (nuint)sizeof(int), &cols);
                    _cl.SetKernelArg(_rainfallSimulationKernel, 3, (nuint)sizeof(float), &rainfall);

                    error = _cl.EnqueueNdrangeKernel(_queue, _rainfallSimulationKernel, 1, null, &globalSize, &localSize, 0, null, null);
                    CheckError(error, "EnqueueNDRangeKernel rainfall");

                    // Simulate drainage
                    _cl.SetKernelArg(_drainageKernel, 0, (nuint)sizeof(nint), &waterBuffer);
                    _cl.SetKernelArg(_drainageKernel, 1, (nuint)sizeof(nint), &waterTempBuffer);
                    _cl.SetKernelArg(_drainageKernel, 2, (nuint)sizeof(nint), &flowDirBuffer);
                    _cl.SetKernelArg(_drainageKernel, 3, (nuint)sizeof(int), &rows);
                    _cl.SetKernelArg(_drainageKernel, 4, (nuint)sizeof(int), &cols);
                    _cl.SetKernelArg(_drainageKernel, 5, (nuint)sizeof(float), &drainageRate);
                    _cl.SetKernelArg(_drainageKernel, 6, (nuint)sizeof(float), &infiltrationRate);

                    error = _cl.EnqueueNdrangeKernel(_queue, _drainageKernel, 1, null, &globalSize, &localSize, 0, null, null);
                    CheckError(error, "EnqueueNDRangeKernel drainage");

                    // Copy temp buffer back to water buffer
                    error = _cl.EnqueueCopyBuffer(_queue, waterTempBuffer, waterBuffer, 0, 0,
                        (nuint)(rows * cols * sizeof(float)), 0, null, null);
                    CheckError(error, "EnqueueCopyBuffer");

                    // Calculate total volume for this timestep
                    var tempWater = new float[rows * cols];
                    fixed (float* tempPtr = tempWater)
                    {
                        error = _cl.EnqueueReadBuffer(_queue, waterBuffer, true, UIntPtr.Zero,
                            (nuint)(rows * cols * sizeof(float)), tempPtr, 0, null, null);
                        CheckError(error, "EnqueueReadBuffer");
                    }

                    float totalVolume = 0f;
                    for (int i = 0; i < tempWater.Length; i++)
                        totalVolume += tempWater[i];

                    volumeHistory[t] = totalVolume;
                }

                // Read final water depth
                error = _cl.EnqueueReadBuffer(_queue, waterBuffer, true, UIntPtr.Zero,
                    (nuint)(rows * cols * sizeof(float)), waterPtr, 0, null, null);
                CheckError(error, "EnqueueReadBuffer final");

                _cl.Finish(_queue);
            }
            finally
            {
                _cl.ReleaseMemObject(elevBuffer);
                _cl.ReleaseMemObject(flowDirBuffer);
                _cl.ReleaseMemObject(waterBuffer);
                _cl.ReleaseMemObject(waterTempBuffer);
            }
        }

        return (waterDepth, volumeHistory);
    }

    private void CheckError(int error, string operation)
    {
        if (error != (int)ErrorCodes.Success)
        {
            throw new Exception($"OpenCL error in {operation}: {error}");
        }
    }

    private string GetKernelSource()
    {
        return @"
// D8 Flow Direction Kernel
__kernel void calculate_flow_direction(
    __global const float* elevation,
    __global uchar* flow_direction,
    const int rows,
    const int cols)
{
    int idx = get_global_id(0);
    int row = idx / cols;
    int col = idx % cols;

    if (row <= 0 || row >= rows - 1 || col <= 0 || col >= cols - 1) {
        flow_direction[idx] = 0;
        return;
    }

    float elev = elevation[idx];
    float max_slope = 0.0f;
    uchar direction = 0;

    // D8 directions: E, SE, S, SW, W, NW, N, NE
    int dx[8] = {1, 1, 0, -1, -1, -1, 0, 1};
    int dy[8] = {0, 1, 1, 1, 0, -1, -1, -1};
    uchar dir_codes[8] = {1, 2, 4, 8, 16, 32, 64, 128};
    float distances[8] = {1.0f, 1.414f, 1.0f, 1.414f, 1.0f, 1.414f, 1.0f, 1.414f};

    for (int i = 0; i < 8; i++) {
        int new_row = row + dy[i];
        int new_col = col + dx[i];

        if (new_row >= 0 && new_row < rows && new_col >= 0 && new_col < cols) {
            int neighbor_idx = new_row * cols + new_col;
            float drop = elev - elevation[neighbor_idx];
            float slope = drop / distances[i];

            if (slope > max_slope) {
                max_slope = slope;
                direction = dir_codes[i];
            }
        }
    }

    flow_direction[idx] = direction;
}

// Apply Rainfall Kernel
__kernel void apply_rainfall(
    __global float* water_depth,
    const int rows,
    const int cols,
    const float rainfall_amount)
{
    int idx = get_global_id(0);
    if (idx < rows * cols) {
        water_depth[idx] += rainfall_amount;
    }
}

// Drainage Simulation Kernel
__kernel void simulate_drainage(
    __global const float* water_in,
    __global float* water_out,
    __global const uchar* flow_direction,
    const int rows,
    const int cols,
    const float drainage_rate,
    const float infiltration_rate)
{
    int idx = get_global_id(0);
    int row = idx / cols;
    int col = idx % cols;

    if (row <= 0 || row >= rows - 1 || col <= 0 || col >= cols - 1) {
        water_out[idx] = water_in[idx] * (1.0f - infiltration_rate);
        return;
    }

    float water = water_in[idx];
    float drain_amount = water * drainage_rate;
    float remaining = water - drain_amount;

    // Apply infiltration
    remaining *= (1.0f - infiltration_rate);

    uchar dir = flow_direction[idx];

    // Target cell calculation based on direction
    int target_row = row;
    int target_col = col;

    if (dir == 1) target_col += 1;       // E
    else if (dir == 2) { target_row += 1; target_col += 1; }  // SE
    else if (dir == 4) target_row += 1;   // S
    else if (dir == 8) { target_row += 1; target_col -= 1; }  // SW
    else if (dir == 16) target_col -= 1;  // W
    else if (dir == 32) { target_row -= 1; target_col -= 1; } // NW
    else if (dir == 64) target_row -= 1;  // N
    else if (dir == 128) { target_row -= 1; target_col += 1; } // NE

    water_out[idx] = remaining;

    // Add drained water to target cell (atomic for thread safety)
    if (target_row >= 0 && target_row < rows && target_col >= 0 && target_col < cols) {
        int target_idx = target_row * cols + target_col;
        atomic_add_float(&water_out[target_idx], drain_amount);
    }
}

// Atomic add for floats (OpenCL 1.2 compatible)
inline void atomic_add_float(__global float* addr, float val) {
    union {
        unsigned int u32;
        float        f32;
    } next, expected, current;
    current.f32 = *addr;
    do {
        expected.f32 = current.f32;
        next.f32 = expected.f32 + val;
        current.u32 = atomic_cmpxchg((__global unsigned int*)addr, expected.u32, next.u32);
    } while (current.u32 != expected.u32);
}

// Water Body Detection Kernel
__kernel void detect_water_bodies(
    __global const float* water_depth,
    __global const float* elevation,
    __global int* water_body_id,
    const int rows,
    const int cols,
    const float min_depth)
{
    int idx = get_global_id(0);
    int row = idx / cols;
    int col = idx % cols;

    if (row < 0 || row >= rows || col < 0 || col >= cols) return;

    // Mark cells with sufficient water depth
    if (water_depth[idx] >= min_depth) {
        water_body_id[idx] = -1; // Unassigned water body
    } else {
        water_body_id[idx] = 0; // No water
    }
}
";
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_flowDirectionKernel != 0) _cl.ReleaseKernel(_flowDirectionKernel);
        if (_flowAccumulationKernel != 0) _cl.ReleaseKernel(_flowAccumulationKernel);
        if (_rainfallSimulationKernel != 0) _cl.ReleaseKernel(_rainfallSimulationKernel);
        if (_drainageKernel != 0) _cl.ReleaseKernel(_drainageKernel);
        if (_waterBodyDetectionKernel != 0) _cl.ReleaseKernel(_waterBodyDetectionKernel);
        if (_program != 0) _cl.ReleaseProgram(_program);
        if (_queue != 0) _cl.ReleaseCommandQueue(_queue);
        if (_context != 0) _cl.ReleaseContext(_context);

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
