// GeoscientistToolkit/Business/Photogrammetry/OpenCLService.cs

using System;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit;

/// <summary>
/// OpenCL Service - manages OpenCL initialization and context
/// </summary>
public static class OpenCLService
{
    private static CL _cl;
    private static IntPtr _device;
    private static IntPtr _context;
    private static IntPtr _commandQueue;

    public static bool IsInitialized { get; private set; }

    public static CL Cl => _cl;
    public static IntPtr Device => _device;
    public static IntPtr Context => _context;
    public static IntPtr CommandQueue => _commandQueue;

    public static unsafe void Initialize()
    {
        if (IsInitialized) return;

        try
        {
            _cl = CL.GetApi();

            // Use centralized device manager to get the device from settings
            _device = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetComputeDevice();

            if (_device == 0)
            {
                Util.Logger.LogWarning("No OpenCL device available from OpenCLDeviceManager.");
                IsInitialized = false;
                return;
            }

            var deviceInfo = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetDeviceInfo();
            Util.Logger.Log($"[OpenCLService]: Using device: {deviceInfo.Name} ({deviceInfo.Vendor})");

            int err;

            // Fixed statement for device pointer for CreateContext
            fixed (IntPtr* pDevice = &_device)
            {
                _context = _cl.CreateContext(null, 1, pDevice, null, null, &err);
            }

            if (err != 0)
            {
                IsInitialized = false;
                return;
            }

            // Use QueueProperties overload (correct, not deprecated)
            QueueProperties queueProperties = 0;
            _commandQueue = _cl.CreateCommandQueueWithProperties(_context, _device, &queueProperties, &err);
            if (err != 0)
            {
                IsInitialized = false;
                return;
            }

            IsInitialized = true;
        }
        catch
        {
            IsInitialized = false;
        }
    }

    public static unsafe IntPtr CreateProgram(string source)
    {
        if (!IsInitialized) throw new InvalidOperationException("OpenCL not initialized");

        int err;
        
        // Use string overload of BuildProgram (not byte*)
        IntPtr program = _cl.CreateProgramWithSource(_context, 1, new[] { source }, null, &err);
        if (err != 0) throw new InvalidOperationException($"CreateProgramWithSource failed: {err}");

        // Fixed statement for device pointer for BuildProgram - use string overload
        fixed (IntPtr* pDevice = &_device)
        {
            err = _cl.BuildProgram(program, 1, pDevice, source, null, null);
        }
        
        if (err != 0)
        {
            nuint logSize = 0;
            _cl.GetProgramBuildInfo(program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);

            if (logSize > 0)
            {
                byte[] log = new byte[logSize];
                fixed (byte* pLog = log)
                {
                    _cl.GetProgramBuildInfo(program, _device, ProgramBuildInfo.BuildLog, logSize, pLog, null);
                }
                string logStr = System.Text.Encoding.ASCII.GetString(log);
                throw new InvalidOperationException($"Build failed:\n{logStr}");
            }

            throw new InvalidOperationException($"BuildProgram failed: {err}");
        }

        return program;
    }
}

/// <summary>
/// Extension methods for OpenCL error handling
/// </summary>
public static class OpenCLExtensions
{
    public static void Throw(this int errorCode)
    {
        if (errorCode != 0)
            throw new InvalidOperationException($"OpenCL error code: {errorCode}");
    }
}
