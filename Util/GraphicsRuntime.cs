namespace GAIA.Util;

/// <summary>Backend-neutral UI lifecycle operations used while GAIA migrates to OpenTK.</summary>
public static class GraphicsRuntime
{
    public static bool IsOpenTk => OpenTkManager.IsInitialized;
    public static bool IsFullScreenSupported => IsOpenTk || VeldridManager.IsFullScreenSupported;
    public static bool IsFullScreen => IsOpenTk
        ? OpenTkManager.MainWindow?.WindowState == OpenTK.Windowing.Common.WindowState.Fullscreen
        : VeldridManager.IsFullScreen;

    public static void ProcessMainThreadActions()
    {
        if (IsOpenTk) OpenTkManager.ProcessMainThreadActions();
        else VeldridManager.ProcessMainThreadActions();
    }

    public static void ToggleFullScreen()
    {
        if (IsOpenTk) OpenTkManager.ToggleFullScreen();
        else VeldridManager.ToggleFullScreen();
    }
}
