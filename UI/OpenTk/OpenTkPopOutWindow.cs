using System.Collections.Concurrent;
using System.Diagnostics;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using NumVector2 = System.Numerics.Vector2;

namespace GAIA.UI.OpenTk;

/// <summary>
/// Secondary native OS window that hosts a PRISM ImGui-only workspace.
/// </summary>
internal sealed class OpenTkPopOutWindow : GAIA.UI.IPopOutWindow
{
    public bool Exists => !_disposed && !_shouldReattach && !_window.IsExiting;
    public NativeWindow Window => _window;
    public bool ShouldReattach => _shouldReattach;
    public string LastError { get; private set; } = string.Empty;

    private readonly GameWindow _mainWindow;
    private readonly NativeWindow _window;
    private readonly string _frameId;
    private Action _bodyRenderer;
    private readonly ConcurrentQueue<char> _pendingChars = new();

    private volatile bool _shouldReattach;
    private bool _disposed;
    private ImGuiController? _imGuiController;

    public OpenTkPopOutWindow(string title, int x, int y, int width, int height)
        : this(GAIA.Util.OpenTkManager.MainWindow ??
                   throw new InvalidOperationException("OpenTK main window is not initialized."),
            title, $"DetachedPanel##{title}", new NumVector2(width, height), new NumVector2(x, y), () => { })
    {
    }

    public OpenTkPopOutWindow(
        GameWindow mainWindow,
        string title,
        string frameId,
        NumVector2 size,
        NumVector2 position,
        Action bodyRenderer)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(bodyRenderer);

        _mainWindow = mainWindow;
        _frameId = string.IsNullOrWhiteSpace(frameId) ? "OpenTkPopOutWindow" : frameId;
        _bodyRenderer = bodyRenderer;

        var clientW = (int)MathF.Max(720f, size.X);
        var clientH = (int)MathF.Max(540f, size.Y);

        var settings = new NativeWindowSettings
        {
            ClientSize = new Vector2i(clientW, clientH),
            Title = title,
            APIVersion = mainWindow.APIVersion,
            Profile = mainWindow.Profile,
            Flags = mainWindow.Flags,
            SharedContext = mainWindow.Context,
            StartFocused = true,
            StartVisible = true,
        };

        _window = new NativeWindow(settings);

        if (position.X > 0f || position.Y > 0f)
        {
            try { _window.Location = new Vector2i((int)position.X, (int)position.Y); }
            catch { }
        }

        _mainWindow.MakeCurrent();

        _window.Closing += OnClosing;
        _window.TextInput += OnTextInput;
    }

    public void PersistGeometry(out NumVector2 position, out NumVector2 size)
    {
        position = NumVector2.Zero;
        size = new NumVector2(1180f, 760f);
        if (_disposed) return;

        try
        {
            size = new NumVector2(_window.ClientSize.X, _window.ClientSize.Y);
            position = new NumVector2(_window.Location.X, _window.Location.Y);
        }
        catch
        {
        }
    }

    public void RenderFrame(float dt)
    {
        if (_disposed || _shouldReattach) return;

        IntPtr previousImGuiContext = ImGui.GetCurrentContext();
        try
        {
            _window.ProcessEvents(0.0);
            if (_window.IsExiting || _shouldReattach) return;

            _window.Context.MakeCurrent();

            _imGuiController ??= new ImGuiController(
                _window.ClientSize.X,
                _window.ClientSize.Y,
                _window.FramebufferSize.X,
                _window.FramebufferSize.Y);

            ImGui.SetCurrentContext(_imGuiController.Context);
            while (_pendingChars.TryDequeue(out var c))
            {
                _imGuiController.PressChar(c);
            }

            _imGuiController.Update(_window, dt);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, _window.FramebufferSize.X, _window.FramebufferSize.Y);
            GL.ClearColor(0.055f, 0.060f, 0.075f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            DrawWorkerFrame();

            _imGuiController.Render();
            _window.Context.SwapBuffers();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _shouldReattach = true;
            Debug.WriteLine($"OpenTkPopOutWindow render failed: {ex}");
        }
        finally
        {
            if (previousImGuiContext != IntPtr.Zero)
            {
                try { ImGui.SetCurrentContext(previousImGuiContext); } catch { }
            }
            try { _mainWindow.MakeCurrent(); } catch { }
        }
    }

    public void SetDrawCallback(Action callback) =>
        _bodyRenderer = callback ?? throw new ArgumentNullException(nameof(callback));

    public void ProcessFrame() => RenderFrame(1f / 60f);

    public void RequestClose()
    {
        _shouldReattach = true;
    }

    private void DrawWorkerFrame()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(NumVector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(io.DisplaySize, ImGuiCond.Always);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
                                     | ImGuiWindowFlags.NoMove
                                     | ImGuiWindowFlags.NoResize
                                     | ImGuiWindowFlags.NoSavedSettings
                                     | ImGuiWindowFlags.NoBringToFrontOnFocus
                                     | ImGuiWindowFlags.NoNavFocus
                                     | ImGuiWindowFlags.NoDocking;

        bool opened = ImGui.Begin(_frameId, flags);
        try
        {
            if (opened)
            {
                // Dataset viewers own framebuffer objects and VAOs created by the main
                // OpenGL context. Those objects are not shared between contexts, even
                // though textures and buffers are. Build the panel while the main
                // context is current so native viewers render into their valid targets;
                // the resulting shared textures can then be sampled by this window.
                _mainWindow.MakeCurrent();
                try
                {
                    _bodyRenderer();
                }
                finally
                {
                    _window.Context.MakeCurrent();
                }
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    private void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _shouldReattach = true;
    }

    private void OnTextInput(TextInputEventArgs e)
    {
        _pendingChars.Enqueue((char)e.Unicode);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            try
            {
                if (_window.WindowState == WindowState.Fullscreen)
                {
                    _window.WindowState = WindowState.Normal;
                    _window.ProcessEvents(0.05);
                }
            }
            catch { }
        }

        try { _window.Closing -= OnClosing; } catch { }
        try { _window.TextInput -= OnTextInput; } catch { }

        try
        {
            _window.Context.MakeCurrent();
            _imGuiController?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _imGuiController = null;
            try { _mainWindow.MakeCurrent(); } catch { }
        }

        try { _window.Dispose(); } catch { }
    }
}
