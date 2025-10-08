// GeoscientistToolkit/Business/GlobalPerformanceManager.cs

using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

public sealed class GlobalPerformanceManager
{
    private GlobalPerformanceManager()
    {
    }

    public static GlobalPerformanceManager Instance { get; } = new();

    public string VisualizationAdapterName { get; private set; }
    public string ComputeAdapterName { get; private set; }

    public TextureCacheManager TextureCache { get; private set; }
    public UndoManager UndoManager { get; private set; }

    public void Initialize(AppSettings settings)
    {
        VisualizationAdapterName = settings.Hardware.VisualizationGPU;
        ComputeAdapterName = settings.Hardware.ComputeGPU;

        TextureCache = new TextureCacheManager(settings.Performance.TextureCacheSize * 1024L * 1024L);
        UndoManager = new UndoManager(settings.Performance.UndoHistorySize);

        SettingsManager.Instance.SettingsChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(AppSettings newSettings)
    {
        TextureCache.UpdateCacheSize(newSettings.Performance.TextureCacheSize * 1024L * 1024L);
        UndoManager.UpdateHistoryLimit(newSettings.Performance.UndoHistorySize);
        ComputeAdapterName = newSettings.Hardware.ComputeGPU;
        Logger.Log("Global performance managers updated with new settings.");
    }

    public void Shutdown()
    {
        TextureCache?.Dispose();
    }
}