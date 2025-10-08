// GeoscientistToolkit/Util/VeldridManager.cs

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;

namespace GeoscientistToolkit.Util;

/// <summary>
///     A simple static class to hold global Veldrid resources.
///     In a larger application, this might be a more formal service.
/// </summary>
public static class VeldridManager
{
    // Thread-safe queue for actions that must run on the main thread
    private static readonly ConcurrentQueue<Action> _mainThreadActions = new();

    // Track ImGuiControllers by their context
    private static readonly Dictionary<IntPtr, ImGuiController> _controllersByContext = new();

    // Core Veldrid objects - these must be set by Application.cs after creation
    public static GraphicsDevice GraphicsDevice { get; set; }
    public static ImGuiController ImGuiController { get; set; }

    // Convenience property for the ResourceFactory
    public static ResourceFactory Factory => GraphicsDevice?.ResourceFactory;

    public static Sdl2Window MainWindow { get; set; }

    public static bool IsFullScreenSupported => !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static bool IsFullScreen
    {
        get
        {
            var w = MainWindow;
            if (w == null) return false;
            var st = w.WindowState;
            return st == WindowState.BorderlessFullScreen || st == WindowState.FullScreen;
        }
    }

    /// <summary>
    ///     Registers an ImGuiController with its context
    /// </summary>
    public static void RegisterImGuiController(ImGuiController controller)
    {
        if (controller?.Context != IntPtr.Zero) _controllersByContext[controller.Context] = controller;
    }

    /// <summary>
    ///     Gets the ImGuiController for the current ImGui context
    /// </summary>
    public static ImGuiController GetCurrentImGuiController()
    {
        var currentContext = ImGui.GetCurrentContext();
        if (_controllersByContext.TryGetValue(currentContext, out var controller)) return controller;

        // Fallback to main controller
        return ImGuiController;
    }

    /// <summary>
    ///     Enqueues an action to be executed on the main thread during the next frame.
    ///     This is useful for OpenGL operations that must happen on the rendering thread.
    /// </summary>
    public static void ExecuteOnMainThread(Action action)
    {
        _mainThreadActions.Enqueue(action);
    }

    /// <summary>
    ///     Processes all pending main thread actions. Called once per frame by MainWindow.
    /// </summary>
    public static void ProcessMainThreadActions()
    {
        while (_mainThreadActions.TryDequeue(out var action))
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error executing main thread action: {ex.Message}");
            }
    }

    public static void ToggleFullScreen()
    {
        var w = MainWindow;
        if (w == null || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        // Borderless is safer (no mode switch) and works on Win/Linux
        w.WindowState = IsFullScreen ? WindowState.Normal : WindowState.BorderlessFullScreen;
    }
}