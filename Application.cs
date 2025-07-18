// GeoscientistToolkit/Application.cs
// Updated with multi-display support and DPI awareness

using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit
{
    public class Application
    {
        private Sdl2Window _window;
        private GraphicsDevice _graphicsDevice;
        private CommandList _commandList;
        private ImGuiController _imGuiController;
        private MainWindow _mainWindow;
        private bool _windowMoved = false;

        public void Run()
        {
            // Try to enable high DPI support (may not work on all platforms)
            try
            {
                // This is a Windows-specific hint, but SDL ignores it on other platforms
                Environment.SetEnvironmentVariable("SDL_WINDOWS_DPI_AWARENESS", "permonitorv2");
            }
            catch { /* Ignore if it fails */ }

            // Create window with support for being moved between monitors
            var windowCI = new WindowCreateInfo
            {
                X = 50,
                Y = 50,
                WindowWidth = 1280,
                WindowHeight = 720,
                WindowTitle = "GeoscientistToolkit",
                
            };

            var graphicsDeviceOptions = new GraphicsDeviceOptions(
                debug: true,
                swapchainDepthFormat: null,
                syncToVerticalBlank: true,
                resourceBindingModel: ResourceBindingModel.Improved,
                preferStandardClipSpaceYDirection: true,
                preferDepthRangeZeroToOne: true);

            // Set preferred graphics backend based on platform
            GraphicsBackend preferredBackend;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                preferredBackend = GraphicsBackend.Direct3D11;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                preferredBackend = GraphicsBackend.Metal;
            }
            else
            {
                preferredBackend = GraphicsBackend.Vulkan;
            }

            VeldridStartup.CreateWindowAndGraphicsDevice(
                windowCI,
                graphicsDeviceOptions,
                preferredBackend,
                out _window,
                out _graphicsDevice);

            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
            _imGuiController = new ImGuiController(
                _graphicsDevice,
                _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
                _window.Width,
                _window.Height);
            // ================================================================= //
            // == VeldridManager Initialization                               == //
            // ================================================================= //
            // 2. INITIALIZE THE MANAGER AFTER THE OBJECTS ARE CREATED
            VeldridManager.GraphicsDevice = _graphicsDevice;
            VeldridManager.ImGuiController = _imGuiController;
            // ================================================================= //

            _mainWindow = new MainWindow();

            // Set up ImGui configuration for multi-display
            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable | ImGuiConfigFlags.DockingEnable;
            io.ConfigViewportsNoTaskBarIcon = false;
            io.ConfigViewportsNoDecoration = false;
            io.ConfigViewportsNoDefaultParent = false;
            io.ConfigDockingTransparentPayload = true;

            // Handle window resize
            _window.Resized += () =>
            {
                _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                _imGuiController.WindowResized(_window.Width, _window.Height);
            };

            // Handle window move (for multi-monitor DPI changes)
            _window.Moved += (Point newPosition) =>
            {
                _windowMoved = true;
            };

            // Background color
            var clearColor = new Vector4(0.1f, 0.1f, 0.12f, 1.0f);

            // Main application loop
            while (_window.Exists)
            {
                InputSnapshot snapshot = _window.PumpEvents();
                if (!_window.Exists) { break; }

                if (_windowMoved)
                {
                    _windowMoved = false;
                    HandleDpiChange();
                }

                _imGuiController.Update(1f / 60f, snapshot);

                _mainWindow.SubmitUI();

                _commandList.Begin();
                _commandList.SetFramebuffer(_graphicsDevice.MainSwapchain.Framebuffer);
                _commandList.ClearColorTarget(0, new RgbaFloat(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));
                _imGuiController.Render(_graphicsDevice, _commandList);
                _commandList.End();

                _graphicsDevice.SubmitCommands(_commandList);
                _graphicsDevice.SwapBuffers(_graphicsDevice.MainSwapchain);

                if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
                {
                    ImGui.UpdatePlatformWindows();
                }
            }

            // Cleanup
            _graphicsDevice.WaitForIdle();
            _imGuiController.Dispose();
            _commandList.Dispose();
            _graphicsDevice.Dispose();
        }

        private void HandleDpiChange()
        {
            var io = ImGui.GetIO();
            _imGuiController.WindowResized(_window.Width, _window.Height);
        }
    }
}