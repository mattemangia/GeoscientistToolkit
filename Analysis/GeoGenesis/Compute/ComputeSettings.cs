// GAIA.GeoGenesis/Compute/ComputeSettings.cs
//
// Minimal compute/hardware configuration for the GeoGenesis engine. Replaces the
// GeoscientistToolkit.Settings.SettingsManager.Hardware section that OpenCLDeviceManager
// originally read from, so the module stays self-contained. Hosts can set these before the
// first OpenCL call; the defaults auto-select a device and keep multi-GPU off.

namespace GAIA.GeoGenesis.Compute;

/// <summary>
///     Process-wide hardware preferences for OpenCL-accelerated GeoGenesis kernels.
/// </summary>
public static class ComputeSettings
{
    /// <summary>
    ///     Preferred OpenCL compute device name, or "Auto" to let the device manager pick the
    ///     best available GPU (falling back to CPU). Matches the old Settings.Hardware.ComputeGPU.
    /// </summary>
    public static string ComputeGpu { get; set; } = "Auto";

    /// <summary>Enable splitting work across all available OpenCL devices.</summary>
    public static bool EnableMultiGpuParallelization { get; set; } = false;
}
