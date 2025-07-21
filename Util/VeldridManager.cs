// GeoscientistToolkit/Util/VeldridManager.cs
using System;
using System.Collections.Concurrent;
using Veldrid;
using ImGuiNET;

namespace GeoscientistToolkit.Util
{
    /// <summary>
    /// A simple static class to hold global Veldrid resources.
    /// In a larger application, this might be a more formal service.
    /// </summary>
    public static class VeldridManager
    {
        // Core Veldrid objects - these must be set by Application.cs after creation
        public static GraphicsDevice GraphicsDevice { get; set; }
        public static ImGuiController ImGuiController { get; set; }
        
        // Convenience property for the ResourceFactory
        public static ResourceFactory Factory => GraphicsDevice?.ResourceFactory;

        // Thread-safe queue for actions that must run on the main thread
        private static readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

        // Track ImGuiControllers by their context
        private static readonly Dictionary<IntPtr, ImGuiController> _controllersByContext = new Dictionary<IntPtr, ImGuiController>();

        /// <summary>
        /// Registers an ImGuiController with its context
        /// </summary>
        public static void RegisterImGuiController(ImGuiController controller)
        {
            if (controller?.Context != IntPtr.Zero)
            {
                _controllersByContext[controller.Context] = controller;
            }
        }

        /// <summary>
        /// Gets the ImGuiController for the current ImGui context
        /// </summary>
        public static ImGuiController GetCurrentImGuiController()
        {
            var currentContext = ImGui.GetCurrentContext();
            if (_controllersByContext.TryGetValue(currentContext, out var controller))
            {
                return controller;
            }
            
            // Fallback to main controller
            return ImGuiController;
        }

        /// <summary>
        /// Enqueues an action to be executed on the main thread during the next frame.
        /// This is useful for OpenGL operations that must happen on the rendering thread.
        /// </summary>
        public static void ExecuteOnMainThread(Action action)
        {
            _mainThreadActions.Enqueue(action);
        }

        /// <summary>
        /// Processes all pending main thread actions. Called once per frame by MainWindow.
        /// </summary>
        public static void ProcessMainThreadActions()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error executing main thread action: {ex.Message}");
                }
            }
        }
    }
}