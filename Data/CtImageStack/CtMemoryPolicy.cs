using System.Diagnostics;
using GAIA.Util;

namespace GAIA.Data.CtImageStack;

/// <summary>Chooses managed RAM only when the dataset and its expected working buffers have safe headroom.</summary>
public static class CtMemoryPolicy
{
    private const long MinimumReserveBytes = 2L * 1024 * 1024 * 1024;
    private const double DatasetExpansionFactor = 1.35; // labels, slice buffers and renderer staging

    public static bool ShouldUseMemoryMapping(long persistedBytes)
    {
        var info = GC.GetGCMemoryInfo();
        var limit = info.TotalAvailableMemoryBytes > 0
            ? info.TotalAvailableMemoryBytes
            : info.HighMemoryLoadThresholdBytes;
        if (limit <= 0) return true;
        var processBytes = Process.GetCurrentProcess().WorkingSet64;
        var runtimeLoad = Math.Max(info.MemoryLoadBytes, processBytes);
        var reserve = Math.Max(MinimumReserveBytes, (long)(limit * .25));
        var safeAvailable = Math.Max(0, limit - runtimeLoad - reserve);
        var required = persistedBytes >= long.MaxValue / 2
            ? long.MaxValue
            : (long)Math.Ceiling(persistedBytes * DatasetExpansionFactor);
        var useMapping = required > safeAvailable || required > limit * .55;
        Logger.Log($"[CT Memory] Dataset {persistedBytes / 1048576.0:F0} MiB, " +
                   $"safe RAM {safeAvailable / 1048576.0:F0} MiB → {(useMapping ? "MMF" : "managed RAM")}");
        return useMapping;
    }

    public static bool IsUnderPressure()
    {
        var info = GC.GetGCMemoryInfo();
        return info.HighMemoryLoadThresholdBytes > 0 &&
               info.MemoryLoadBytes >= info.HighMemoryLoadThresholdBytes * .82;
    }
}
