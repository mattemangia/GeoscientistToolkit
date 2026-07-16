namespace GAIA.Util;

/// <summary>OpenTK UI lifecycle operations shared by the shell and viewer windows.</summary>
public static class GraphicsRuntime
{
    public static bool IsOpenTk => OpenTkManager.IsInitialized;
    public static bool IsFullScreenSupported => OpenTkManager.MainWindow != null;
    public static bool IsFullScreen => OpenTkManager.MainWindow?.WindowState ==
                                       OpenTK.Windowing.Common.WindowState.Fullscreen;

    public static void ProcessMainThreadActions()
    {
        OpenTkManager.ProcessMainThreadActions();
    }

    public static void ToggleFullScreen()
    {
        OpenTkManager.ToggleFullScreen();
    }
}
