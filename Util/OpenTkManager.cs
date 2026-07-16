using System.Collections.Concurrent;
using GAIA.UI.OpenTk;
using OpenTK.Windowing.Desktop;
using OpenTkImGuiController = GAIA.UI.OpenTk.ImGuiController;

namespace GAIA.Util;

/// <summary>Shared OpenTK application state during and after the renderer migration.</summary>
public static class OpenTkManager
{
    private static readonly ConcurrentQueue<Action> MainThreadActions = new();

    public static GameWindow MainWindow { get; internal set; }
    public static OpenTkImGuiController ImGuiController { get; internal set; }
    public static bool IsInitialized => MainWindow != null && ImGuiController != null;

    public static void ExecuteOnMainThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        MainThreadActions.Enqueue(action);
    }

    public static void ProcessMainThreadActions()
    {
        while (MainThreadActions.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Logger.LogError($"OpenTK main-thread action failed: {ex.Message}"); }
        }
    }

    public static void ToggleFullScreen()
    {
        if (MainWindow == null) return;
        MainWindow.WindowState = MainWindow.WindowState == OpenTK.Windowing.Common.WindowState.Fullscreen
            ? OpenTK.Windowing.Common.WindowState.Normal
            : OpenTK.Windowing.Common.WindowState.Fullscreen;
    }
}
