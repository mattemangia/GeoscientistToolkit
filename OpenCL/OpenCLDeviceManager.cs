// GeoscientistToolkit/OpenCL/OpenCLDeviceManager.cs
//
// ================================================================================================
// Centralized OpenCL Device Management
// ================================================================================================
// This class provides centralized management of OpenCL device selection across all modules
// in the GeoscientistToolkit application. It ensures that all OpenCL-accelerated components
// (geothermal simulations, NMR, acoustic, geomechanical, etc.) use the same device as
// configured in the application settings.
//
// IMPLEMENTATION NOTES:
// ------------------------------------------------------------------------------------------------
// 1. Reads device preference from Settings.Hardware.ComputeGPU
// 2. Supports "Auto" mode for automatic device selection
// 3. Supports specific device selection by name (e.g., "NVIDIA RTX 3070", "Apple M1")
// 4. Handles platform-specific considerations:
//    - macOS (including M1/M2/M3): Prefers Metal-based OpenCL on Apple Silicon
//    - Linux: Handles multiple OpenCL platforms (NVIDIA, AMD, Intel)
//    - Windows: Standard GPU selection with fallback to CPU
// 5. Thread-safe singleton pattern for device caching
// 6. Provides device enumeration for settings UI
// ================================================================================================

using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.OpenCL;

/// <summary>
///     Centralized manager for OpenCL device selection across the application.
///     Ensures all OpenCL modules use the device configured in settings.
/// </summary>
public static class OpenCLDeviceManager
{
    private static readonly object _lock = new object();
    private static CL _cl;
    private static nint _cachedDevice;
    private static string _cachedDeviceName;
    private static string _cachedDeviceVendor;
    private static ulong _cachedDeviceGlobalMemory;
    private static bool _isInitialized;
    private static List<OpenCLDeviceInfo> _availableDevices;

    /// <summary>
    ///     Gets the OpenCL device to use for compute operations based on application settings.
    ///     Returns 0 if no suitable device is available.
    /// </summary>
    public static unsafe nint GetComputeDevice()
    {
        lock (_lock)
        {
            if (_isInitialized)
                return _cachedDevice;

            _cl = CL.GetApi();
            _availableDevices = new List<OpenCLDeviceInfo>();

            try
            {
                Logger.Log("OpenCLDeviceManager: Initializing device selection...");

                // Get all available devices
                var devices = EnumerateAllDevices();
                _availableDevices = devices;

                if (devices.Count == 0)
                {
                    Logger.LogWarning("No OpenCL devices found.");
                    Logger.LogWarning("Please ensure GPU drivers with OpenCL support are installed.");
                    _isInitialized = true;
                    return 0;
                }

                // Get user preference from settings
                var settings = SettingsManager.Instance?.Settings?.Hardware;
                var preferredDeviceName = settings?.ComputeGPU ?? "Auto";

                Logger.Log($"OpenCLDeviceManager: User preference = '{preferredDeviceName}'");
                Logger.Log($"OpenCLDeviceManager: Found {devices.Count} OpenCL device(s)");

                // Select device based on preference
                OpenCLDeviceInfo selectedDevice = null;

                if (preferredDeviceName == "Auto")
                {
                    // Auto mode: Prefer GPU over CPU, consider platform-specific preferences
                    selectedDevice = SelectAutoDevice(devices);
                }
                else
                {
                    // Try to find device by name
                    selectedDevice = devices.FirstOrDefault(d =>
                        d.Name.Contains(preferredDeviceName, StringComparison.OrdinalIgnoreCase));

                    if (selectedDevice == null)
                    {
                        Logger.LogWarning($"Preferred device '{preferredDeviceName}' not found. Falling back to auto selection.");
                        selectedDevice = SelectAutoDevice(devices);
                    }
                }

                if (selectedDevice != null)
                {
                    _cachedDevice = selectedDevice.Device;
                    _cachedDeviceName = selectedDevice.Name;
                    _cachedDeviceVendor = selectedDevice.Vendor;
                    _cachedDeviceGlobalMemory = selectedDevice.GlobalMemory;

                    Logger.Log($"OpenCLDeviceManager: Selected device: {_cachedDeviceName} ({_cachedDeviceVendor})");
                    Logger.Log($"OpenCLDeviceManager: Global Memory: {_cachedDeviceGlobalMemory / (1024 * 1024)} MB");
                    Logger.Log($"OpenCLDeviceManager: Device Type: {selectedDevice.Type}");
                }
                else
                {
                    Logger.LogWarning("No suitable OpenCL device could be selected.");
                }

                _isInitialized = true;
                return _cachedDevice;
            }
            catch (Exception ex)
            {
                Logger.LogError($"OpenCLDeviceManager initialization error: {ex.Message}");
                _isInitialized = true;
                return 0;
            }
        }
    }

    /// <summary>
    ///     Gets information about the currently selected device.
    /// </summary>
    public static (string Name, string Vendor, ulong GlobalMemory) GetDeviceInfo()
    {
        lock (_lock)
        {
            if (!_isInitialized)
                GetComputeDevice();

            return (_cachedDeviceName ?? "None", _cachedDeviceVendor ?? "None", _cachedDeviceGlobalMemory);
        }
    }

    /// <summary>
    ///     Gets all available OpenCL devices for display in settings UI.
    /// </summary>
    public static List<OpenCLDeviceInfo> GetAvailableDevices()
    {
        lock (_lock)
        {
            if (!_isInitialized)
                GetComputeDevice();

            return _availableDevices ?? new List<OpenCLDeviceInfo>();
        }
    }

    /// <summary>
    ///     Gets all GPU devices for multi-GPU parallelization if enabled in settings.
    ///     Returns a list with the single selected device if multi-GPU is disabled or only one GPU is available.
    /// </summary>
    public static List<OpenCLDeviceInfo> GetAllComputeDevices()
    {
        lock (_lock)
        {
            if (!_isInitialized)
                GetComputeDevice();

            var settings = SettingsManager.Instance?.Settings?.Hardware;
            var enableMultiGPU = settings?.EnableMultiGPUParallelization ?? false;

            if (!enableMultiGPU || _availableDevices == null)
            {
                // Multi-GPU disabled or not initialized - return single device
                if (_cachedDevice != 0 && _availableDevices != null)
                {
                    var singleDevice = _availableDevices.FirstOrDefault(d => d.Device == _cachedDevice);
                    if (singleDevice != null)
                        return new List<OpenCLDeviceInfo> { singleDevice };
                }
                return new List<OpenCLDeviceInfo>();
            }

            // Multi-GPU enabled - return all GPU devices
            var gpuDevices = _availableDevices.Where(d => d.Type == DeviceType.Gpu).ToList();

            if (gpuDevices.Count > 1)
            {
                Logger.Log($"OpenCLDeviceManager: Multi-GPU mode enabled with {gpuDevices.Count} GPUs");
                foreach (var gpu in gpuDevices)
                {
                    Logger.Log($"  - {gpu.Name} ({gpu.Vendor}) - {gpu.GlobalMemory / (1024 * 1024)} MB");
                }
            }
            else if (gpuDevices.Count == 1)
            {
                Logger.Log("OpenCLDeviceManager: Only one GPU available, using single-GPU mode");
            }

            return gpuDevices.Count > 0 ? gpuDevices : new List<OpenCLDeviceInfo> { _availableDevices.FirstOrDefault(d => d.Device == _cachedDevice) }.Where(d => d != null).ToList();
        }
    }

    /// <summary>
    ///     Checks if multi-GPU mode is enabled and multiple GPUs are available.
    /// </summary>
    public static bool IsMultiGPUEnabled()
    {
        lock (_lock)
        {
            var settings = SettingsManager.Instance?.Settings?.Hardware;
            var enableMultiGPU = settings?.EnableMultiGPUParallelization ?? false;

            if (!enableMultiGPU)
                return false;

            if (!_isInitialized)
                GetComputeDevice();

            var gpuDevices = _availableDevices?.Where(d => d.Type == DeviceType.Gpu).ToList();
            return gpuDevices != null && gpuDevices.Count > 1;
        }
    }

    /// <summary>
    ///     Resets the device cache. Call this when settings change to force device reselection.
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _isInitialized = false;
            _cachedDevice = 0;
            _cachedDeviceName = null;
            _cachedDeviceVendor = null;
            _cachedDeviceGlobalMemory = 0;
            _availableDevices = null;
        }
    }

    /// <summary>
    ///     Enumerates all available OpenCL devices across all platforms.
    /// </summary>
    private static unsafe List<OpenCLDeviceInfo> EnumerateAllDevices()
    {
        var result = new List<OpenCLDeviceInfo>();

        try
        {
            // Get all platforms
            uint numPlatforms;
            _cl.GetPlatformIDs(0, null, &numPlatforms);

            if (numPlatforms == 0)
                return result;

            var platforms = new nint[numPlatforms];
            fixed (nint* platformsPtr = platforms)
            {
                _cl.GetPlatformIDs(numPlatforms, platformsPtr, null);
            }

            // For each platform, enumerate devices
            foreach (var platform in platforms)
            {
                var platformName = GetPlatformName(platform);

                // Try to get GPU devices first
                var gpuDevices = GetDevicesOfType(platform, DeviceType.Gpu, platformName);
                result.AddRange(gpuDevices);

                // Then get CPU devices
                var cpuDevices = GetDevicesOfType(platform, DeviceType.Cpu, platformName);
                result.AddRange(cpuDevices);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error enumerating OpenCL devices: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    ///     Gets devices of a specific type from a platform.
    /// </summary>
    private static unsafe List<OpenCLDeviceInfo> GetDevicesOfType(nint platform, DeviceType deviceType, string platformName)
    {
        var result = new List<OpenCLDeviceInfo>();

        try
        {
            uint numDevices;
            _cl.GetDeviceIDs(platform, deviceType, 0, null, &numDevices);

            if (numDevices == 0)
                return result;

            var devices = new nint[numDevices];
            fixed (nint* devicesPtr = devices)
            {
                _cl.GetDeviceIDs(platform, deviceType, numDevices, devicesPtr, null);
            }

            foreach (var device in devices)
            {
                var deviceInfo = GetDeviceInformation(device, platform, platformName, deviceType);
                if (deviceInfo != null)
                    result.Add(deviceInfo);
            }
        }
        catch
        {
            // Platform doesn't support this device type, which is normal
        }

        return result;
    }

    /// <summary>
    ///     Gets detailed information about an OpenCL device.
    /// </summary>
    private static unsafe OpenCLDeviceInfo GetDeviceInformation(nint device, nint platform, string platformName, DeviceType deviceType)
    {
        try
        {
            nuint paramSize;
            var nameBuffer = new byte[256];

            // Get device name
            string deviceName;
            fixed (byte* namePtr = nameBuffer)
            {
                _cl.GetDeviceInfo(device, (uint)DeviceInfo.Name, 256, namePtr, &paramSize);
                deviceName = Encoding.UTF8.GetString(nameBuffer, 0, (int)paramSize - 1).Trim();
            }

            // Get device vendor
            string vendor;
            fixed (byte* namePtr = nameBuffer)
            {
                _cl.GetDeviceInfo(device, (uint)DeviceInfo.Vendor, 256, namePtr, &paramSize);
                vendor = Encoding.UTF8.GetString(nameBuffer, 0, (int)paramSize - 1).Trim();
            }

            // Get global memory size
            ulong globalMem;
            _cl.GetDeviceInfo(device, (uint)DeviceInfo.GlobalMemSize, sizeof(ulong), &globalMem, null);

            // Get max compute units
            uint computeUnits;
            _cl.GetDeviceInfo(device, (uint)DeviceInfo.MaxComputeUnits, sizeof(uint), &computeUnits, null);

            // Get max clock frequency
            uint maxClockFreq;
            _cl.GetDeviceInfo(device, (uint)DeviceInfo.MaxClockFrequency, sizeof(uint), &maxClockFreq, null);

            return new OpenCLDeviceInfo
            {
                Device = device,
                Platform = platform,
                PlatformName = platformName,
                Name = deviceName,
                Vendor = vendor,
                Type = deviceType,
                GlobalMemory = globalMem,
                ComputeUnits = computeUnits,
                MaxClockFrequency = maxClockFreq
            };
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error getting device information: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Gets the name of an OpenCL platform.
    /// </summary>
    private static unsafe string GetPlatformName(nint platform)
    {
        try
        {
            nuint paramSize;
            var nameBuffer = new byte[256];

            fixed (byte* namePtr = nameBuffer)
            {
                _cl.GetPlatformInfo(platform, (uint)PlatformInfo.Name, 256, namePtr, &paramSize);
                return Encoding.UTF8.GetString(nameBuffer, 0, (int)paramSize - 1).Trim();
            }
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    ///     Selects the best device automatically based on platform-specific heuristics.
    /// </summary>
    private static OpenCLDeviceInfo SelectAutoDevice(List<OpenCLDeviceInfo> devices)
    {
        if (devices.Count == 0)
            return null;

        // Platform-specific selection logic
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: Prefer Apple GPU (M1/M2/M3) or AMD GPU
            var appleGpu = devices.FirstOrDefault(d =>
                d.Type == DeviceType.Gpu &&
                (d.Vendor.Contains("Apple", StringComparison.OrdinalIgnoreCase) ||
                 d.Name.Contains("Apple", StringComparison.OrdinalIgnoreCase) ||
                 d.PlatformName.Contains("Apple", StringComparison.OrdinalIgnoreCase)));

            if (appleGpu != null)
            {
                Logger.Log("OpenCLDeviceManager: Detected macOS - selecting Apple GPU");
                return appleGpu;
            }

            // Fallback to any GPU
            var anyGpu = devices.FirstOrDefault(d => d.Type == DeviceType.Gpu);
            if (anyGpu != null)
                return anyGpu;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux: Prefer NVIDIA, then AMD, then Intel GPU
            var nvidiaGpu = devices.FirstOrDefault(d =>
                d.Type == DeviceType.Gpu &&
                d.Vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));

            if (nvidiaGpu != null)
            {
                Logger.Log("OpenCLDeviceManager: Detected Linux - selecting NVIDIA GPU");
                return nvidiaGpu;
            }

            var amdGpu = devices.FirstOrDefault(d =>
                d.Type == DeviceType.Gpu &&
                (d.Vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                 d.Vendor.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)));

            if (amdGpu != null)
            {
                Logger.Log("OpenCLDeviceManager: Detected Linux - selecting AMD GPU");
                return amdGpu;
            }

            var intelGpu = devices.FirstOrDefault(d =>
                d.Type == DeviceType.Gpu &&
                d.Vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase));

            if (intelGpu != null)
            {
                Logger.Log("OpenCLDeviceManager: Detected Linux - selecting Intel GPU");
                return intelGpu;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: Prefer discrete GPU (NVIDIA/AMD) over integrated
            var discreteGpu = devices
                .Where(d => d.Type == DeviceType.Gpu)
                .OrderByDescending(d => d.GlobalMemory) // Discrete GPUs typically have more memory
                .FirstOrDefault();

            if (discreteGpu != null)
            {
                Logger.Log("OpenCLDeviceManager: Detected Windows - selecting discrete GPU");
                return discreteGpu;
            }
        }

        // Generic fallback: Prefer any GPU, then CPU
        var gpu = devices.FirstOrDefault(d => d.Type == DeviceType.Gpu);
        if (gpu != null)
        {
            Logger.Log("OpenCLDeviceManager: Selecting first available GPU");
            return gpu;
        }

        var cpu = devices.FirstOrDefault(d => d.Type == DeviceType.Cpu);
        if (cpu != null)
        {
            Logger.Log("OpenCLDeviceManager: No GPU found, falling back to CPU");
            return cpu;
        }

        // Last resort: return first device
        Logger.LogWarning("OpenCLDeviceManager: Using first available device as last resort");
        return devices[0];
    }
}

/// <summary>
///     Information about an available OpenCL device.
/// </summary>
public class OpenCLDeviceInfo
{
    public nint Device { get; set; }
    public nint Platform { get; set; }
    public string PlatformName { get; set; }
    public string Name { get; set; }
    public string Vendor { get; set; }
    public DeviceType Type { get; set; }
    public ulong GlobalMemory { get; set; }
    public uint ComputeUnits { get; set; }
    public uint MaxClockFrequency { get; set; }

    public string DisplayName => $"{Name} ({Vendor}) - {GlobalMemory / (1024 * 1024)} MB";
    public string TypeString => Type == DeviceType.Gpu ? "GPU" : "CPU";
}
