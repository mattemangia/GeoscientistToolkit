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
        private Action _drawCallback;
        private IntPtr _mainContext;
        private bool _isDisposed = false;
        
        public bool Exists => _window?.Exists ?? false;
        public IntPtr ImGuiContext => _imGuiController?.Context ?? IntPtr.Zero;

        public PopOutWindow(string title, int x, int y, int width, int height)
        {
            // Store the main context before creating new one
            _mainContext = ImGui.GetCurrentContext();
            
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
                debug: false,
                swapchainDepthFormat: null,
                syncToVerticalBlank: true,
                resourceBindingModel: ResourceBindingModel.Improved,
                preferStandardClipSpaceYDirection: true,
                preferDepthRangeZeroToOne: true);

            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(
                _window, 
                graphicsDeviceOptions, 
                VeldridManager.GraphicsDevice.BackendType); // Use the same backend as main window
            
            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
            
            // Create ImGuiController which will create its own context
            _imGuiController = new ImGuiController(
                _graphicsDevice,
                _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
                _window.Width,
                _window.Height);
            
            // Restore main context after creating the controller
            ImGui.SetCurrentContext(_mainContext);
            
            // Handle window resize
            _window.Resized += () =>
            {
                if (_isDisposed) return;
                
                _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                
                // Ensure we use the pop-out context for resize
                var prevContext = ImGui.GetCurrentContext();
                ImGui.SetCurrentContext(_imGuiController.Context);
                _imGuiController.WindowResized(_window.Width, _window.Height);
                ImGui.SetCurrentContext(prevContext);
            };
        }

        public void SetDrawCallback(Action callback)
        {
            _drawCallback = callback;
        }

        public void ProcessFrame()
        {
            if (_isDisposed || !_window.Exists || _drawCallback == null) return;
            
            var snapshot = _window.PumpEvents();
            if (!_window.Exists) return;
            
            // CRITICAL: Set the correct ImGui context for this window
            var previousContext = ImGui.GetCurrentContext();
            ImGui.SetCurrentContext(_imGuiController.Context);
            
            try
            {
                _imGuiController.Update(1f / 60f, snapshot);
                
                // Draw the UI - context is already set correctly
                _drawCallback();
                
                // Render
                _commandList.Begin();
                _commandList.SetFramebuffer(_graphicsDevice.MainSwapchain.Framebuffer);
                _commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.12f, 1.0f));
                _imGuiController.Render(_graphicsDevice, _commandList);
                _commandList.End();
                
                _graphicsDevice.SubmitCommands(_commandList);
                _graphicsDevice.SwapBuffers(_graphicsDevice.MainSwapchain);
            }
            catch (Exception ex)
            {
                // Log error but don't crash the application
                Console.WriteLine($"Error in PopOutWindow.ProcessFrame: {ex.Message}");
            }
            finally
            {
                // Always restore the previous context
                if (previousContext != IntPtr.Zero)
                {
                    ImGui.SetCurrentContext(previousContext);
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            _graphicsDevice?.WaitForIdle();
            
            // Dispose in correct context
            if (_imGuiController != null && _imGuiController.Context != IntPtr.Zero)
            {
                var prevContext = ImGui.GetCurrentContext();
                ImGui.SetCurrentContext(_imGuiController.Context);
                _imGuiController.Dispose();
                ImGui.SetCurrentContext(prevContext);
            }
            
            _commandList?.Dispose();
            _graphicsDevice?.Dispose();
            _window?.Close();
        }
    }
}