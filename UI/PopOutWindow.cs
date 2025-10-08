// GeoscientistToolkit/UI/PopOutWindow.cs (Fixed to share GraphicsDevice)

using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace GeoscientistToolkit.UI;

/// <summary>
///     Represents a separate Veldrid window for popped-out panels
/// </summary>
public class PopOutWindow : IDisposable
{
    private readonly CommandList _commandList;
    private readonly ImGuiController _imGuiController;
    private readonly IntPtr _mainContext;
    private readonly Swapchain _swapchain;
    private readonly Sdl2Window _window;
    private Action _drawCallback;
    private bool _isDisposed;

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

        // Create a swapchain for this window using the SHARED graphics device
        var swapchainDesc = new SwapchainDescription(
            VeldridStartup.GetSwapchainSource(_window),
            (uint)width,
            (uint)height,
            null,
            true,
            false);

        // Use the existing graphics device from VeldridManager
        var graphicsDevice = VeldridManager.GraphicsDevice;
        _swapchain = graphicsDevice.ResourceFactory.CreateSwapchain(swapchainDesc);

        _commandList = graphicsDevice.ResourceFactory.CreateCommandList();

        // Create ImGuiController which will create its own context
        _imGuiController = new ImGuiController(
            graphicsDevice,
            _swapchain.Framebuffer.OutputDescription,
            _window.Width,
            _window.Height);

        // Register this controller with VeldridManager
        VeldridManager.RegisterImGuiController(_imGuiController);

        // Restore main context after creating the controller
        ImGui.SetCurrentContext(_mainContext);

        // Handle window resize
        _window.Resized += () =>
        {
            if (_isDisposed) return;

            _swapchain.Resize((uint)_window.Width, (uint)_window.Height);

            // Ensure we use the pop-out context for resize
            var prevContext = ImGui.GetCurrentContext();
            ImGui.SetCurrentContext(_imGuiController.Context);
            _imGuiController.WindowResized(_window.Width, _window.Height);
            ImGui.SetCurrentContext(prevContext);
        };
    }

    public bool Exists => _window?.Exists ?? false;
    public IntPtr ImGuiContext => _imGuiController?.Context ?? IntPtr.Zero;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        var graphicsDevice = VeldridManager.GraphicsDevice;
        graphicsDevice?.WaitForIdle();

        // Dispose in correct context
        if (_imGuiController != null && _imGuiController.Context != IntPtr.Zero)
        {
            var prevContext = ImGui.GetCurrentContext();
            ImGui.SetCurrentContext(_imGuiController.Context);
            _imGuiController.Dispose();
            ImGui.SetCurrentContext(prevContext);
        }

        _commandList?.Dispose();
        _swapchain?.Dispose();
        _window?.Close();
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
            var graphicsDevice = VeldridManager.GraphicsDevice;

            _imGuiController.Update(1f / 60f, snapshot);

            // Draw the UI - context is already set correctly
            _drawCallback();

            // Render
            _commandList.Begin();
            _commandList.SetFramebuffer(_swapchain.Framebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.12f, 1.0f));
            _imGuiController.Render(graphicsDevice, _commandList);
            _commandList.End();

            graphicsDevice.SubmitCommands(_commandList);
            graphicsDevice.SwapBuffers(_swapchain);
        }
        catch (Exception ex)
        {
            // Log error but don't crash the application
            Logger.Log($"Error in PopOutWindow.ProcessFrame: {ex.Message}");
        }
        finally
        {
            // Always restore the previous context
            if (previousContext != IntPtr.Zero) ImGui.SetCurrentContext(previousContext);
        }
    }
}