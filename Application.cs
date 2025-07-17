// GeoscientistToolkit/Application.cs
// This class manages the main application window, graphics device, and the main render loop.
// It integrates Veldrid for rendering and ImGui.NET for the user interface.

using System.Numerics;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using GeoscientistToolkit.UI;

namespace GeoscientistToolkit
{
    public class Application
    {
        private Sdl2Window _window;
        private GraphicsDevice _graphicsDevice;
        private CommandList _commandList;
        private ImGuiController _imGuiController;
        private MainWindow _mainWindow;

        public void Run()
        {
            VeldridStartup.CreateWindowAndGraphicsDevice(
                new WindowCreateInfo(50, 50, 1280, 720, WindowState.Normal, "GeoscientistToolkit"),
                new GraphicsDeviceOptions(true, null, true, ResourceBindingModel.Improved, true, true),
                out _window,
                out _graphicsDevice);

            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
            _imGuiController = new ImGuiController(_graphicsDevice, _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription, _window.Width, _window.Height);
            _mainWindow = new MainWindow();

            _window.Resized += () =>
            {
                _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                _imGuiController.WindowResized(_window.Width, _window.Height);
            };

            var clearColor = new Vector4(0.1f, 0.1f, 0.12f, 1.0f);

            while (_window.Exists)
            {
                InputSnapshot snapshot = _window.PumpEvents();
                if (!_window.Exists) { break; }

                _imGuiController.Update(1f / 60f, snapshot);

                _mainWindow.SubmitUI();

                _commandList.Begin();
                _commandList.SetFramebuffer(_graphicsDevice.MainSwapchain.Framebuffer);
                _commandList.ClearColorTarget(0, new RgbaFloat(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));
                _imGuiController.Render(_graphicsDevice, _commandList);
                _commandList.End();

                _graphicsDevice.SubmitCommands(_commandList);
                _graphicsDevice.SwapBuffers(_graphicsDevice.MainSwapchain);
            }

            _graphicsDevice.WaitForIdle();
            _imGuiController.Dispose();
            _commandList.Dispose();
            _graphicsDevice.Dispose();
        }
    }
}