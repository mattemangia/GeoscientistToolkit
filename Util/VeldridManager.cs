// GeoscientistToolkit/Util/VeldridManager.cs
using System;
using System.Collections.Concurrent;
using Veldrid;

namespace GeoscientistToolkit.Util
{
    /// <summary>
    /// A simple static class to hold global Veldrid resources.
    /// In a larger application, this might be a more formal service.
    /// </summary>
    public static class VeldridManager
    {
        public static GraphicsDevice GraphicsDevice { get; set; }
        public static ResourceFactory Factory => GraphicsDevice.ResourceFactory;
        public static ImGuiController ImGuiController { get; set; }

        private static readonly ConcurrentQueue<Action> MainThreadActions = new ConcurrentQueue<Action>();

        /// <summary>
        /// Queues an action to be executed on the main UI thread during the next frame.
        /// </summary>
        public static void ExecuteOnMainThread(Action action)
        {
            MainThreadActions.Enqueue(action);
        }

        /// <summary>
        /// Executes all queued main-thread actions. Should be called once per frame in the main loop.
        /// </summary>
        public static void ProcessMainThreadActions()
        {
            while (MainThreadActions.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }
    }
}