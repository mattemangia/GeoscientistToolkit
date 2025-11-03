// GeoscientistToolkit/Business/Panorama/OpenCLService.cs

using System;
using System.Text;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Business.Panorama;

/// <summary>
/// Manages a shared OpenCL context for GPU computations.
/// </summary>
internal static class OpenCLService
{
    public static CL Cl { get; private set; }
    public static IntPtr Platform { get; private set; }
    public static IntPtr Device { get; private set; }
    public static IntPtr Context { get; private set; }
    public static IntPtr CommandQueue { get; private set; }
    public static bool IsInitialized { get; private set; }
    private static readonly object _initLock = new();

    public static unsafe void Initialize()
    {
        lock (_initLock)
        {
            if (IsInitialized) return;

            Cl = CL.GetApi();
            int err;

            uint numPlatforms = 0;
            err = Cl.GetPlatformIDs(0, null, &numPlatforms);
            if (err != (int)CLEnum.Success || numPlatforms == 0)
            {
                // No OpenCL platforms found, we can't initialize.
                return;
            }

            var platforms = new IntPtr[numPlatforms];
            fixed (IntPtr* platformsPtr = platforms)
            {
                err = Cl.GetPlatformIDs(numPlatforms, platformsPtr, null);
                err.Throw();
            }
            Platform = platforms[0];

            uint numDevices = 0;
            err = Cl.GetDeviceIDs(Platform, DeviceType.Gpu, 0, null, &numDevices);
            if (err != (int)CLEnum.Success || numDevices == 0)
            {
                // Fallback to CPU if no GPU is found
                err = Cl.GetDeviceIDs(Platform, DeviceType.Cpu, 0, null, &numDevices);
                if (err != (int)CLEnum.Success || numDevices == 0)
                {
                    // No GPU or CPU OpenCL devices found.
                    return;
                }
                
                var cpuDevices = new IntPtr[numDevices];
                fixed (IntPtr* devicesPtr = cpuDevices)
                {
                    err = Cl.GetDeviceIDs(Platform, DeviceType.Cpu, numDevices, devicesPtr, null);
                    err.Throw();
                }
                Device = cpuDevices[0];
            }
            else
            {
                var gpuDevices = new IntPtr[numDevices];
                fixed (IntPtr* devicesPtr = gpuDevices)
                {
                    err = Cl.GetDeviceIDs(Platform, DeviceType.Gpu, numDevices, devicesPtr, null);
                    err.Throw();
                }
                Device = gpuDevices[0];
            }

            // --- Context and Command Queue ---
            var contextProperties = stackalloc IntPtr[] { (IntPtr)ContextProperties.Platform, Platform, 0 };

            // CORRECTED: The most robust way to pass a single device to CreateContext is via a fixed array.
            var devices = new[] { Device };
            fixed (IntPtr* devicePtr = devices)
            {
                Context = Cl.CreateContext(contextProperties, 1, devicePtr, null, null, &err);
                err.Throw();
            }

            CommandQueue = Cl.CreateCommandQueue(Context, Device, (CommandQueueProperties)0, &err);
            err.Throw();

            IsInitialized = true;
        }
    }

    public static unsafe IntPtr CreateProgram(string source)
    {
        int err;
        var program = Cl.CreateProgramWithSource(Context, 1, new[] { source }, null, &err);
        err.Throw();

        err = Cl.BuildProgram(program, 1, new[] { Device }, (string)null, null, null);
        if (err != (int)CLEnum.Success)
        {
            nuint logSize;
            // CORRECTED: The enum member is BuildLog, not Log.
            Cl.GetProgramBuildInfo(program, Device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
            byte[] log = new byte[logSize];
            fixed (byte* logPtr = log)
            {
                Cl.GetProgramBuildInfo(program, Device, ProgramBuildInfo.BuildLog, logSize, logPtr, null);
            }
            var errorString = $"OpenCL Build Error:\n{Encoding.UTF8.GetString(log)}";
            Logger.LogError(errorString);
            throw new Exception(errorString);
        }

        return program;
    }

    public static void Dispose()
    {
        if (!IsInitialized) return;
        Cl.ReleaseCommandQueue(CommandQueue);
        Cl.ReleaseContext(Context);
        IsInitialized = false;
    }
}

internal static class OpenCLErrorExtensions
{
    public static void Throw(this int err)
    {
        if (err != (int)CLEnum.Success)
        {
            throw new Exception($"OpenCL Error: {(CLEnum)err}");
        }
    }
}