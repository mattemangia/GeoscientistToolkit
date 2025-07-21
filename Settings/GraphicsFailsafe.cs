// GeoscientistToolkit/Settings/GraphicsFailsafe.cs
using System;
using System.IO;
using System.Text.Json;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Settings
{
    /// <summary>
    /// Manages failsafe graphics settings to prevent startup failures
    /// </summary>
    public static class GraphicsFailsafe
    {
        private static readonly string FailsafeFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeoscientistToolkit", ".graphics_failsafe");
        
        private static readonly string LastKnownGoodPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeoscientistToolkit", ".last_known_good_graphics");

        private class FailsafeData
        {
            public int FailureCount { get; set; }
            public DateTime LastFailure { get; set; }
            public string FailedBackend { get; set; }
            public string FailedGpu { get; set; }
        }

        private class LastKnownGoodSettings
        {
            public string Backend { get; set; }
            public string VisualizationGPU { get; set; }
            public string ComputeGPU { get; set; }
        }

        /// <summary>
        /// Check if we should use failsafe graphics settings
        /// </summary>
        public static bool ShouldUseFailsafe(out string reason)
        {
            reason = null;
            
            if (!File.Exists(FailsafeFilePath))
                return false;

            try
            {
                var json = File.ReadAllText(FailsafeFilePath);
                var failsafe = JsonSerializer.Deserialize<FailsafeData>(json);
                
                // If we've had 3+ failures in the last 5 minutes, use failsafe
                if (failsafe.FailureCount >= 3 && 
                    (DateTime.Now - failsafe.LastFailure).TotalMinutes < 5)
                {
                    reason = $"Graphics initialization failed {failsafe.FailureCount} times. Using safe settings.";
                    return true;
                }
                
                // Clear old failure data
                if ((DateTime.Now - failsafe.LastFailure).TotalMinutes > 5)
                {
                    File.Delete(FailsafeFilePath);
                }
            }
            catch
            {
                // Ignore errors reading failsafe file
            }
            
            return false;
        }

        /// <summary>
        /// Record a graphics initialization failure
        /// </summary>
        public static void RecordFailure(string backend, string gpu)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FailsafeFilePath));
                
                FailsafeData failsafe = null;
                if (File.Exists(FailsafeFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(FailsafeFilePath);
                        failsafe = JsonSerializer.Deserialize<FailsafeData>(json);
                    }
                    catch { }
                }
                
                if (failsafe == null)
                {
                    failsafe = new FailsafeData();
                }
                
                failsafe.FailureCount++;
                failsafe.LastFailure = DateTime.Now;
                failsafe.FailedBackend = backend;
                failsafe.FailedGpu = gpu;
                
                File.WriteAllText(FailsafeFilePath, JsonSerializer.Serialize(failsafe));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to record graphics failure: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear failure records after successful startup
        /// </summary>
        public static void RecordSuccess(HardwareSettings settings)
        {
            try
            {
                if (File.Exists(FailsafeFilePath))
                {
                    File.Delete(FailsafeFilePath);
                }
                
                // Save current settings as last known good
                Directory.CreateDirectory(Path.GetDirectoryName(LastKnownGoodPath));
                var lastKnownGood = new LastKnownGoodSettings
                {
                    Backend = settings.PreferredGraphicsBackend,
                    VisualizationGPU = settings.VisualizationGPU,
                    ComputeGPU = settings.ComputeGPU
                };
                
                File.WriteAllText(LastKnownGoodPath, JsonSerializer.Serialize(lastKnownGood));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to record graphics success: {ex.Message}");
            }
        }

        /// <summary>
        /// Get safe graphics settings
        /// </summary>
        public static HardwareSettings GetSafeSettings(HardwareSettings current)
        {
            var safe = current.Clone();
            
            // Try to load last known good settings
            if (File.Exists(LastKnownGoodPath))
            {
                try
                {
                    var json = File.ReadAllText(LastKnownGoodPath);
                    var lastGood = JsonSerializer.Deserialize<LastKnownGoodSettings>(json);
                    
                    safe.PreferredGraphicsBackend = lastGood.Backend;
                    safe.VisualizationGPU = lastGood.VisualizationGPU;
                    safe.ComputeGPU = lastGood.ComputeGPU;
                    
                    Logger.Log("Using last known good graphics settings");
                    return safe;
                }
                catch { }
            }
            
            // Fall back to most compatible settings
            safe.PreferredGraphicsBackend = "Auto";
            safe.VisualizationGPU = "Auto";
            safe.ComputeGPU = "Auto";
            safe.EnableVSync = true;
            safe.TargetFrameRate = 60;
            
            Logger.Log("Using default safe graphics settings");
            return safe;
        }

        /// <summary>
        /// Extension method to clone HardwareSettings
        /// </summary>
        private static HardwareSettings Clone(this HardwareSettings settings)
        {
            var json = JsonSerializer.Serialize(settings);
            return JsonSerializer.Deserialize<HardwareSettings>(json);
        }
    }
}