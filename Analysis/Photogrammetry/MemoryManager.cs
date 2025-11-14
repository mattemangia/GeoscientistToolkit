// GeoscientistToolkit/Analysis/Photogrammetry/MemoryManager.cs

using System;
using System.Diagnostics;
using OpenCvSharp;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Manages memory usage for the photogrammetry pipeline to prevent OOM errors.
/// </summary>
public class MemoryManager
{
    private readonly KeyframeManager _keyframeManager;
    private bool _isEnabled = true;
    private long _memoryThresholdBytes = 2L * 1024 * 1024 * 1024; // 2 GB default
    private int _maxKeyframesInMemory = 50; // Keep only last N keyframes with images
    private int _minKeyframesInMemory = 10; // Always keep at least this many

    /// <summary>
    /// Gets or sets whether memory management is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>
    /// Gets or sets the memory threshold in bytes. When exceeded, cleanup will occur.
    /// </summary>
    public long MemoryThresholdBytes
    {
        get => _memoryThresholdBytes;
        set => _memoryThresholdBytes = Math.Max(512L * 1024 * 1024, value); // Minimum 512 MB
    }

    /// <summary>
    /// Gets or sets the maximum number of keyframes to keep in memory with full images.
    /// </summary>
    public int MaxKeyframesInMemory
    {
        get => _maxKeyframesInMemory;
        set => _maxKeyframesInMemory = Math.Max(_minKeyframesInMemory, value);
    }

    /// <summary>
    /// Gets the current process memory usage in bytes.
    /// </summary>
    public long CurrentMemoryUsage
    {
        get
        {
            using var proc = Process.GetCurrentProcess();
            return proc.WorkingSet64;
        }
    }

    /// <summary>
    /// Gets the current memory usage in megabytes.
    /// </summary>
    public double CurrentMemoryUsageMB => CurrentMemoryUsage / (1024.0 * 1024.0);

    /// <summary>
    /// Gets the memory threshold in megabytes.
    /// </summary>
    public double MemoryThresholdMB
    {
        get => _memoryThresholdBytes / (1024.0 * 1024.0);
        set => MemoryThresholdBytes = (long)(value * 1024 * 1024);
    }

    /// <summary>
    /// Gets the percentage of memory usage relative to threshold.
    /// </summary>
    public double MemoryUsagePercent => (CurrentMemoryUsage / (double)_memoryThresholdBytes) * 100.0;

    public MemoryManager(KeyframeManager keyframeManager)
    {
        _keyframeManager = keyframeManager ?? throw new ArgumentNullException(nameof(keyframeManager));
    }

    /// <summary>
    /// Checks memory usage and performs cleanup if necessary.
    /// </summary>
    /// <returns>True if cleanup was performed</returns>
    public bool CheckAndCleanup()
    {
        if (!_isEnabled)
            return false;

        long currentMemory = CurrentMemoryUsage;

        // Check if we need to cleanup
        bool needsCleanup = currentMemory > _memoryThresholdBytes ||
                           _keyframeManager.Keyframes.Count > _maxKeyframesInMemory;

        if (needsCleanup)
        {
            Logger.Log($"Memory cleanup triggered: {CurrentMemoryUsageMB:F1} MB / {MemoryThresholdMB:F1} MB " +
                      $"({_keyframeManager.Keyframes.Count} keyframes)");

            int cleanedCount = CleanupOldKeyframes();

            if (cleanedCount > 0)
            {
                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Logger.Log($"Cleaned up {cleanedCount} keyframes. " +
                          $"Memory usage: {CurrentMemoryUsageMB:F1} MB");

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes image data from old keyframes while keeping 3D points and pose.
    /// </summary>
    /// <returns>Number of keyframes cleaned</returns>
    private int CleanupOldKeyframes()
    {
        var keyframes = _keyframeManager.Keyframes;

        // Don't cleanup if we have too few keyframes
        if (keyframes.Count <= _minKeyframesInMemory)
            return 0;

        int targetCleanupCount = keyframes.Count - _maxKeyframesInMemory;
        if (targetCleanupCount <= 0)
            return 0;

        int cleanedCount = 0;

        // Cleanup oldest keyframes first (keep recent ones for tracking)
        for (int i = 0; i < targetCleanupCount && i < keyframes.Count; i++)
        {
            var kf = keyframes[i];

            // Dispose image and depth map to free memory
            if (kf.Image != null)
            {
                kf.Image.Dispose();
                kf.Image = null;
                cleanedCount++;
            }

            if (kf.DepthMap != null)
            {
                kf.DepthMap.Dispose();
                kf.DepthMap = null;
            }

            // Keep 3D points and pose for reconstruction
            // Keep keypoints for potential re-matching
        }

        return cleanedCount;
    }

    /// <summary>
    /// Forces immediate cleanup of old keyframes.
    /// </summary>
    /// <param name="keepCount">Number of recent keyframes to keep with images</param>
    /// <returns>Number of keyframes cleaned</returns>
    public int ForceCleanup(int keepCount = -1)
    {
        if (keepCount < 0)
            keepCount = _maxKeyframesInMemory;

        int oldMaxKeyframes = _maxKeyframesInMemory;
        _maxKeyframesInMemory = Math.Max(_minKeyframesInMemory, keepCount);

        int cleanedCount = CleanupOldKeyframes();

        _maxKeyframesInMemory = oldMaxKeyframes;

        if (cleanedCount > 0)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return cleanedCount;
    }

    /// <summary>
    /// Gets a status string describing current memory usage.
    /// </summary>
    public string GetStatusString()
    {
        return $"Memory: {CurrentMemoryUsageMB:F0} MB / {MemoryThresholdMB:F0} MB " +
               $"({MemoryUsagePercent:F0}%) | " +
               $"Keyframes: {_keyframeManager.Keyframes.Count}/{_maxKeyframesInMemory}";
    }

    /// <summary>
    /// Gets statistics about memory usage.
    /// </summary>
    public (long currentBytes, long thresholdBytes, int keyframeCount, int maxKeyframes) GetStats()
    {
        return (CurrentMemoryUsage, _memoryThresholdBytes, _keyframeManager.Keyframes.Count, _maxKeyframesInMemory);
    }
}
