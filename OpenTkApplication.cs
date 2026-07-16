using System.Diagnostics;
using GAIA.Business;
using GAIA.Network;
using GAIA.Settings;
using GAIA.UI;
using GAIA.UI.OpenTk;
using GAIA.Util;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTkImGuiController = GAIA.UI.OpenTk.ImGuiController;

namespace GAIA;

/// <summary>OpenTK host used during the renderer migration and by the final GAIA shell.</summary>
internal sealed class OpenTkApplication : GameWindow
{
    private OpenTkImGuiController _imGui;
    private MainWindow _mainWindow;
    private bool _exitConfirmed;
    private bool _closeRequested;
    private bool _shutdown;

    public OpenTkApplication()
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            ClientSize = new Vector2i(1700, 950),
            Title = "GAIA — Geoscience Analysis, Imaging & Automation",
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            Flags = ContextFlags.ForwardCompatible,
            StartFocused = true,
            StartVisible = true
        })
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        EnsureRequiredOpenGlVersion();

        SettingsManager.Instance.LoadSettings();
        var settings = SettingsManager.Instance.Settings;
        Logger.Initialize(settings.Logging);
        GlobalPerformanceManager.Instance.Initialize(settings);

        VSync = settings.Hardware.EnableVSync ? VSyncMode.On : VSyncMode.Off;
        _imGui = new OpenTkImGuiController(ClientSize.X, ClientSize.Y, FramebufferSize.X, FramebufferSize.Y);
        OpenTkManager.MainWindow = this;
        OpenTkManager.ImGuiController = _imGui;

        ThemeManager.ApplyTheme(settings.Appearance);
        _mainWindow = new MainWindow();
        _mainWindow.OnExitConfirmed += ConfirmExit;

        if (!string.IsNullOrWhiteSpace(Program.StartingProjectPath) && File.Exists(Program.StartingProjectPath))
            ProjectManager.Instance.LoadProject(Program.StartingProjectPath);

        TextInput += OnTextInput;
        Logger.Log("GAIA OpenTK host initialized.");
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        OpenTkManager.ProcessMainThreadActions();
        _imGui.Update(this, (float)Math.Max(args.Time, 1e-6));

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
        GL.ClearColor(0.1f, 0.1f, 0.12f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _mainWindow.SubmitUI(_closeRequested);
        _imGui.Render();
        BasePanel.ProcessAllPopOutWindows();
        SwapBuffers();

        if (_exitConfirmed) Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitConfirmed && ProjectManager.Instance.HasUnsavedChanges)
        {
            e.Cancel = true;
            _closeRequested = true;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnUnload()
    {
        if (_shutdown) return;
        _shutdown = true;
        TextInput -= OnTextInput;
        try
        {
            NodeManager.Instance.Stop();
            _mainWindow?.Dispose();
            ProjectManager.Instance.Shutdown();
            GlobalPerformanceManager.Instance.Shutdown();
            _imGui?.Dispose();
        }
        finally
        {
            OpenTkManager.ImGuiController = null;
            OpenTkManager.MainWindow = null;
        }
        base.OnUnload();
    }

    private void ConfirmExit()
    {
        _exitConfirmed = true;
        _closeRequested = false;
    }

    private void OnTextInput(TextInputEventArgs e) => _imGui?.PressChar((char)e.Unicode);

    private static void EnsureRequiredOpenGlVersion()
    {
        var major = GL.GetInteger(GetPName.MajorVersion);
        var minor = GL.GetInteger(GetPName.MinorVersion);
        if (major > 3 || major == 3 && minor >= 3) return;
        throw new NotSupportedException($"OpenGL 3.3 is required; active context is {GL.GetString(StringName.Version)}.");
    }
}
