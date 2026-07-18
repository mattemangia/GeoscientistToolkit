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
    private readonly bool _graphicsSelfTest;

    public OpenTkApplication(bool graphicsSelfTest = false)
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            // Fallback size when the user un-maximizes the window.
            ClientSize = new Vector2i(1700, 950),
            WindowState = WindowState.Maximized,
            // Plain hyphen: the em dash renders as a white box in some Linux title bars.
            Title = "GAIA - Geoscience Analysis, Imaging & Automation",
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            Flags = ContextFlags.ForwardCompatible,
            StartFocused = true,
            StartVisible = true
        })
    {
        _graphicsSelfTest = graphicsSelfTest;
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        EnsureRequiredOpenGlVersion();

        // Settings first: the controller bakes its font atlas as it is constructed, so the font
        // size and UI scale have to be known before that. This is why there is no "Loading
        // settings..." step below - it has to happen before anything can be drawn at all.
        SettingsManager.Instance.LoadSettings();
        var settings = SettingsManager.Instance.Settings;
        UiScaling.Configure(settings.Appearance, GetMonitorContentScale());

        _imGui = new OpenTkImGuiController(ClientSize.X, ClientSize.Y, FramebufferSize.X, FramebufferSize.Y);
        OpenTkManager.MainWindow = this;
        OpenTkManager.ImGuiController = _imGui;

        // Shared by the splash and loading screens, released once startup is done.
        using var logo = new StartupLogo();

        // Splash screen with logo (2 s)
        new SplashScreen(logo).Show(2000, RenderStartupFrame);

        // Loading screen with logo, progress + host diagnostics
        var loading = new LoadingScreen(logo);
        ShowLoading(loading, "Starting GAIA...", 0.0f);

        ShowLoading(loading, "Initializing logger...", 0.15f);
        Logger.Initialize(settings.Logging);

        ShowLoading(loading, "Checking graphics configuration...", 0.2f);
        ShowLoading(loading, "Initializing performance manager...", 0.25f);
        GlobalPerformanceManager.Instance.Initialize(settings);

        ShowLoading(loading, "Configuring render context...", 0.3f);
        VSync = settings.Hardware.EnableVSync ? VSyncMode.On : VSyncMode.Off;

        ShowLoading(loading, "Initializing UI...", 0.6f);
        _mainWindow = new MainWindow();
        _mainWindow.OnExitConfirmed += ConfirmExit;

        ShowLoading(loading, "Applying theme...", 0.7f);
        ThemeManager.ApplyTheme(settings.Appearance);

        ShowLoading(loading, "Loading project...", 0.8f);
        if (!string.IsNullOrWhiteSpace(Program.StartingProjectPath) && File.Exists(Program.StartingProjectPath))
            ProjectManager.Instance.LoadProject(Program.StartingProjectPath);

        ShowLoading(loading, "Starting application...", 1.0f);

        TextInput += OnTextInput;
        Logger.Log("GAIA OpenTK host initialized.");
        if (_graphicsSelfTest)
        {
            UI.Diagnostics.OpenTkGraphicsSelfTest.RunOrThrow();
            Logger.Log("GAIA OpenTK graphics self-test passed.");
            _exitConfirmed = true;
        }
    }

    /// <summary>
    ///     Renders a single frame during startup (splash/loading). Pumps input events so the
    ///     window stays responsive, draws the supplied ImGui callback, then swaps buffers.
    /// </summary>
    private void RenderStartupFrame(Action draw)
    {
        ProcessEvents(0.0);
        _imGui.Update(this, 1f / 60f);
        draw();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
        GL.ClearColor(0.1f, 0.1f, 0.12f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _imGui.Render();
        SwapBuffers();
    }

    private void ShowLoading(LoadingScreen loading, string status, float progress)
    {
        loading.UpdateStatus(status, progress);
        RenderStartupFrame(loading.Draw);
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
        ViewerScreenshotUtility.ProcessDeferredCaptures();
        ScreenshotUtility.ProcessDeferredCaptures();
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

    /// <summary>
    ///     The monitor's DPI content scale, 1.0 at 96 DPI. ImGui rasterises its atlas in physical
    ///     pixels, so without this the whole UI shrinks as the display scaling grows. Falls back to
    ///     1.0 rather than guessing when the monitor cannot be queried.
    /// </summary>
    private float GetMonitorContentScale()
    {
        try
        {
            if (TryGetCurrentMonitorScale(out var horizontal, out _) && horizontal > 0f) return horizontal;
        }
        catch
        {
        }

        return 1f;
    }

    private static void EnsureRequiredOpenGlVersion()
    {
        var major = GL.GetInteger(GetPName.MajorVersion);
        var minor = GL.GetInteger(GetPName.MinorVersion);
        if (major > 3 || major == 3 && minor >= 3) return;
        throw new NotSupportedException($"OpenGL 3.3 is required; active context is {GL.GetString(StringName.Version)}.");
    }
}
