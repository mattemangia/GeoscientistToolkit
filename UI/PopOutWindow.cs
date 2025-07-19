// GeoscientistToolkit/UI/PopOutWindow.cs
using System;
using System.Numerics;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.UI
{
    /// <summary>
    /// Represents a separate Veldrid window for popped-out panels
    /// </summary>
    public class PopOutWindow : IDisposable
    {
        private Sdl2Window _window;
        private GraphicsDevice _graphicsDevice;
        private CommandList _commandList;
        private ImGuiController _imGuiController;
        
        public bool Exists => _window?.Exists ?? false;

        public PopOutWindow(string title, int x, int y, int width, int height)
        {
            var windowCI = new WindowCreateInfo
            {
                X = x,
                Y = y,
                WindowWidth = width,
                WindowHeight = height,
                WindowTitle = title
            };

            _window = VeldridStartup.CreateWindow(windowCI);
            
            var graphicsDeviceOptions = new GraphicsDeviceOptions(
                debug: true,
                swapchainDepthFormat: null,
                syncToVerticalBlank: true,
                resourceBindingModel: ResourceBindingModel.Improved,
                preferStandardClipSpaceYDirection: true,
                preferDepthRangeZeroToOne: true);

            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(
                _window, 
                graphicsDeviceOptions, 
                VeldridManager.GraphicsDevice.BackendType);
            
            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
            
            _imGuiController = new ImGuiController(
                _graphicsDevice,
                _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
                _window.Width,
                _window.Height);
            
            // Configure ImGui for this window
            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable | ImGuiConfigFlags.DockingEnable;
            
            // Handle window resize
            _window.Resized += () =>
            {
                _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                _imGuiController.WindowResized(_window.Width, _window.Height);
            };
        }

        public void Render(Action drawUI)
        {
            if (!_window.Exists) return;
            
            var snapshot = _window.PumpEvents();
            if (!_window.Exists) return;
            
            _imGuiController.Update(1f / 60f, snapshot);
            
            // Set ImGui context for this window
            ImGui.SetCurrentContext(_imGuiController.Context);
            
            // Draw the UI
            drawUI();
            
            // Render
            _commandList.Begin();
            _commandList.SetFramebuffer(_graphicsDevice.MainSwapchain.Framebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.12f, 1.0f));
            _imGuiController.Render(_graphicsDevice, _commandList);
            _commandList.End();
            
            _graphicsDevice.SubmitCommands(_commandList);
            _graphicsDevice.SwapBuffers(_graphicsDevice.MainSwapchain);
        }

        public void Dispose()
        {
            _graphicsDevice?.WaitForIdle();
            _imGuiController?.Dispose();
            _commandList?.Dispose();
            _graphicsDevice?.Dispose();
            _window?.Close();
        }
    }
}