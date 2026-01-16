// GeoscientistToolkit/Application.cs

using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageSharp;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace GeoscientistToolkit;

public class Application
{
    private CommandList _commandList;
    private bool _confirmedExit;
    private GraphicsDevice _graphicsDevice;
    private ImGuiController _imGuiController;
    private LoadingScreen _loadingScreen;
    private MainWindow _mainWindow;
    private Sdl2Window _window;
    private bool _windowMoved;

    // Window close handling - we track whether window tried to close
    private bool _windowWantedToClose;

    public void Run()
    {
        // Create window and graphics device first for loading screen
        try
        {
            Environment.SetEnvironmentVariable("SDL_WINDOWS_DPI_AWARENESS", "permonitorv2");
        }
        catch
        {
            /* Ignore */
        }

        var windowCI = new WindowCreateInfo
        {
            X = 50,
            Y = 50,
            WindowWidth = 1700,
            WindowHeight = 950,
            WindowTitle = "GeoscientistToolkit"
        };

        // Use safer graphics options for initial creation on Windows
        GraphicsDeviceOptions basicGraphicsOptions;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            // Use more conservative options for Windows to avoid E_INVALIDARG
            basicGraphicsOptions = new GraphicsDeviceOptions(
                false, // Disable debug mode initially
                PixelFormat.D24_UNorm_S8_UInt, // Specify a common depth format
                true,
                ResourceBindingModel.Default, // Use Default instead of Improved
                preferStandardClipSpaceYDirection: false, // Don't force clip space changes
                preferDepthRangeZeroToOne: false); // Use platform defaults
        else
            basicGraphicsOptions = new GraphicsDeviceOptions(
                true,
                null,
                true,
                ResourceBindingModel.Improved,
                preferStandardClipSpaceYDirection: true,
                preferDepthRangeZeroToOne: true);

        try
        {
            // Try to create with platform default backend first
            var backend = GetPlatformDefaultBackend();

            // On Windows, try multiple backends if D3D11 fails
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                TryCreateWithWindowsFallbacks(windowCI, ref basicGraphicsOptions, backend);
            }
            else
            {
                TryCreateWithUnixFallbacks(windowCI, ref basicGraphicsOptions, backend);
            }

            VeldridManager.MainWindow = _window;
            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();

            // Set window icon
            SetWindowIcon();

            // Create minimal ImGui controller for splash and loading screens
            _imGuiController = new ImGuiController(
                _graphicsDevice,
                _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
                _window.Width, _window.Height);

            // Show splash screen with logo
            using (var splashScreen = new SplashScreen(_graphicsDevice, _commandList, _imGuiController, _window))
            {
                splashScreen.Show(2000); // Show for 2 seconds
            }

            // Create and show loading screen
            _loadingScreen = new LoadingScreen(_graphicsDevice, _commandList, _imGuiController, _window);
            _loadingScreen.UpdateStatus("Starting GeoscientistToolkit...", 0.0f);
        }
        catch (Exception ex)
        {
            // If we can't even create a basic window, show error and exit
            Logger.LogError($"Failed to create window: {ex.Message}");
            var troubleshooting = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Possible solutions:\n" +
                  "1. Update your graphics drivers\n" +
                  "2. Install Visual C++ Redistributables\n" +
                  "3. Try running in compatibility mode\n" +
                  "4. Ensure DirectX is up to date"
                : "Possible solutions:\n" +
                  "1. Update your graphics drivers\n" +
                  "2. Install Vulkan/OpenGL drivers for your GPU\n" +
                  "3. Ensure libvulkan and SDL2 dependencies are installed\n" +
                  "4. Try forcing the OpenGL backend in Settings";

            CrossPlatformMessageBox.Show(
                $"Failed to create application window.\n\nError: {ex.Message}\n\n{troubleshooting}",
                "Startup Error",
                MessageBoxType.Error);
            Environment.Exit(1);
            return;
        }

        // Now continue with normal initialization
        _loadingScreen.UpdateStatus("Loading settings...", 0.1f);
        SettingsManager.Instance.LoadSettings();
        var appSettings = SettingsManager.Instance.Settings;

        _loadingScreen.UpdateStatus("Initializing logger...", 0.15f);
        Logger.Initialize(appSettings.Logging);

        // Check if we should use failsafe mode
        _loadingScreen.UpdateStatus("Checking graphics configuration...", 0.2f);
        string failsafeReason;
        if (GraphicsFailsafe.ShouldUseFailsafe(out failsafeReason))
        {
            Logger.LogWarning($"Starting in graphics failsafe mode: {failsafeReason}");

            appSettings.Hardware = GraphicsFailsafe.GetSafeSettings(appSettings.Hardware);

            // Show user a warning about failsafe mode
            CrossPlatformMessageBox.Show(
                failsafeReason + "\n\nThe application will start with safe graphics settings. " +
                "You can change them in Settings after startup.",
                "Graphics Failsafe Mode",
                MessageBoxType.Warning);
        }

        _loadingScreen.UpdateStatus("Initializing performance manager...", 0.25f);
        GlobalPerformanceManager.Instance.Initialize(appSettings);

        // Check if we need to recreate graphics device with user preferences
        var hardwareSettings = appSettings.Hardware;
        GraphicsBackend preferredBackend;
        if (hardwareSettings.PreferredGraphicsBackend != "Auto")
            Enum.TryParse(hardwareSettings.PreferredGraphicsBackend, out preferredBackend);
        else
            preferredBackend = _graphicsDevice.BackendType; // Keep current working backend

        // Only recreate if user has specific preferences different from current
        if ((hardwareSettings.PreferredGraphicsBackend != "Auto" && preferredBackend != _graphicsDevice.BackendType) ||
            hardwareSettings.VisualizationGPU != "Auto")
        {
            _loadingScreen.UpdateStatus("Configuring graphics device...", 0.3f);

            // Create options based on what's currently working
            GraphicsDeviceOptions graphicsDeviceOptions;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && preferredBackend == GraphicsBackend.Direct3D11)
                graphicsDeviceOptions = new GraphicsDeviceOptions(
                    false,
                    PixelFormat.D24_UNorm_S8_UInt,
                    hardwareSettings.EnableVSync,
                    ResourceBindingModel.Default,
                    preferStandardClipSpaceYDirection: false,
                    preferDepthRangeZeroToOne: false);
            else
                graphicsDeviceOptions = new GraphicsDeviceOptions(
                    true,
                    null,
                    hardwareSettings.EnableVSync,
                    ResourceBindingModel.Improved,
                    preferStandardClipSpaceYDirection: true,
                    preferDepthRangeZeroToOne: true);

            try
            {
                // Dispose current resources
                _imGuiController.Dispose();
                _commandList.Dispose();
                _graphicsDevice.Dispose();

                Logger.Log($"Attempting to initialize graphics with backend: {preferredBackend}");

                // Recreate with preferred settings
                VeldridStartup.CreateWindowAndGraphicsDevice(
                    windowCI,
                    graphicsDeviceOptions,
                    preferredBackend,
                    out _window,
                    out _graphicsDevice);

                Logger.Log($"Veldrid initialized successfully on device: {_graphicsDevice.DeviceName}");
                if (hardwareSettings.VisualizationGPU != "Auto" &&
                    !_graphicsDevice.DeviceName.Contains(hardwareSettings.VisualizationGPU,
                        StringComparison.OrdinalIgnoreCase))
                    Logger.LogWarning(
                        $"User preferred GPU '{hardwareSettings.VisualizationGPU}' but Veldrid selected '{_graphicsDevice.DeviceName}'. This may depend on the selected backend.");

                _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();

                // Set window icon again after recreation
                SetWindowIcon();

                _imGuiController = new ImGuiController(
                    _graphicsDevice,
                    _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
                    _window.Width, _window.Height);

                // Recreate loading screen with new controller
                _loadingScreen = new LoadingScreen(_graphicsDevice, _commandList, _imGuiController, _window);

                GraphicsFailsafe.RecordSuccess(hardwareSettings);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to initialize graphics with backend {preferredBackend}: {ex.Message}");
                GraphicsFailsafe.RecordFailure(preferredBackend.ToString(), hardwareSettings.VisualizationGPU);

                // Keep using the basic device we already have
                Logger.Log("Continuing with default graphics configuration");
            }
        }

        _loadingScreen.UpdateStatus("Setting up Veldrid manager...", 0.4f);
        VeldridManager.GraphicsDevice = _graphicsDevice;
        VeldridManager.ImGuiController = _imGuiController;
        VeldridManager.RegisterImGuiController(_imGuiController);

        _loadingScreen.UpdateStatus("Initializing UI...", 0.6f);
        SettingsManager.Instance.SettingsChanged += OnSettingsChanged;
        _mainWindow = new MainWindow();

        // Subscribe to exit confirmation from MainWindow
        _mainWindow.OnExitConfirmed += () => _confirmedExit = true;

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable | ImGuiConfigFlags.DockingEnable;

        _loadingScreen.UpdateStatus("Applying theme...", 0.7f);
        ThemeManager.ApplyTheme(appSettings.Appearance);
        _imGuiController.SetUIScale(appSettings.Appearance.UIScale);

        _window.Resized += () =>
        {
            _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
            _imGuiController.WindowResized(_window.Width, _window.Height);
        };

        _window.Moved += newPosition => { _windowMoved = true; };

        _loadingScreen.UpdateStatus("Loading project...", 0.8f);
        var projectToLoad = Program.StartingProjectPath;
        if (string.IsNullOrEmpty(projectToLoad))
        {
            var fileAssocSettings = appSettings.FileAssociations;
            if (fileAssocSettings.AutoLoadLastProject && fileAssocSettings.RecentProjects.Count > 0)
                projectToLoad = fileAssocSettings.RecentProjects[0];
        }

        if (!string.IsNullOrEmpty(projectToLoad) && File.Exists(projectToLoad))
            try
            {
                ProjectManager.Instance.LoadProject(projectToLoad);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to auto-load project: {ex.Message}");
            }

        _loadingScreen.UpdateStatus("Starting application...", 1.0f);

        // Give a moment to see the completed loading screen
        Thread.Sleep(200);

        // Main application loop
        var clearColor = new Vector4(0.1f, 0.1f, 0.12f, 1.0f);
        var stopwatch = Stopwatch.StartNew();
        float frameTime = 0;

        var wasWindowExisting = true;

        while (_window.Exists || _windowWantedToClose)
        {
            if (!hardwareSettings.EnableVSync && hardwareSettings.TargetFrameRate > 0)
            {
                var targetFrameTime = 1000f / hardwareSettings.TargetFrameRate;
                if (frameTime < targetFrameTime) Thread.Sleep((int)(targetFrameTime - frameTime));
            }

            frameTime = stopwatch.ElapsedMilliseconds;
            stopwatch.Restart();
            var deltaTime = frameTime / 1000f;

            // Check if window wanted to close this frame
            if (wasWindowExisting && !_window.Exists)
            {
                // Window just tried to close
                if (ProjectManager.Instance.HasUnsavedChanges)
                {
                    // We have unsaved changes - we need to ask the user
                    // Set flag so MainWindow will show the dialog
                    _windowWantedToClose = true;
                    Logger.Log("Window close detected with unsaved changes - showing dialog");
                }
                else
                {
                    // No unsaved changes, exit normally
                    break;
                }
            }

            wasWindowExisting = _window.Exists;

            // If we confirmed exit, stop the loop
            if (_confirmedExit)
            {
                Logger.Log("Exit confirmed by user");
                break;
            }

            // Only pump events and render if window still exists
            if (_window.Exists)
            {
                var snapshot = _window.PumpEvents();

                if (_windowMoved)
                {
                    _windowMoved = false;
                    HandleDpiChange();
                }

                _imGuiController.Update(deltaTime, snapshot);

                // Pass the window close request to MainWindow so it can show the dialog
                _mainWindow.SubmitUI(_windowWantedToClose);

                // SCREENSHOT FIX: Prepare screenshot capture BEFORE rendering
                ScreenshotUtility.BeginFrame();

                _commandList.Begin();
                _commandList.SetFramebuffer(_graphicsDevice.MainSwapchain.Framebuffer);
                _commandList.ClearColorTarget(0, new RgbaFloat(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));
                _imGuiController.Render(_graphicsDevice, _commandList);

                // SCREENSHOT FIX: Copy backbuffer to capture texture BEFORE ending the command list
                ScreenshotUtility.EndFrame(_commandList);

                _commandList.End();

                _graphicsDevice.SubmitCommands(_commandList);
                _graphicsDevice.SwapBuffers(_graphicsDevice.MainSwapchain);

                if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0) ImGui.UpdatePlatformWindows();

                BasePanel.ProcessAllPopOutWindows();
            }
            else if (_windowWantedToClose)
            {
                // Window was closed but we're showing dialog - just wait a bit
                Thread.Sleep(16); // ~60fps
            }
        }

        // Cleanup
        if (appSettings.Backup.BackupOnProjectClose && !string.IsNullOrEmpty(ProjectManager.Instance.ProjectPath))
            ProjectManager.Instance.BackupProject();

        if (SettingsManager.Instance.HasUnsavedChanges) SettingsManager.Instance.SaveSettings();

        GlobalPerformanceManager.Instance.Shutdown();

        _graphicsDevice.WaitForIdle();

        // SCREENSHOT FIX: Cleanup screenshot resources
        ScreenshotUtility.Cleanup();

        _imGuiController.Dispose();
        _commandList.Dispose();
        _graphicsDevice.Dispose();
    }

    private void HandleDpiChange()
    {
        _imGuiController.WindowResized(_window.Width, _window.Height);
    }

    private GraphicsBackend GetPlatformDefaultBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return GraphicsBackend.Direct3D11;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return GraphicsBackend.Metal;
        return GraphicsBackend.Vulkan;
    }

    private void TryCreateWithWindowsFallbacks(
        WindowCreateInfo windowCI,
        ref GraphicsDeviceOptions basicGraphicsOptions,
        GraphicsBackend backend)
    {
        try
        {
            VeldridStartup.CreateWindowAndGraphicsDevice(
                windowCI,
                basicGraphicsOptions,
                backend,
                out _window,
                out _graphicsDevice);
        }
        catch (Exception d3dEx)
        {
            Logger.LogError($"Failed to create D3D11 device: {d3dEx.Message}. Trying Vulkan...");

            // Try Vulkan as fallback
            try
            {
                backend = GraphicsBackend.Vulkan;
                VeldridStartup.CreateWindowAndGraphicsDevice(
                    windowCI,
                    basicGraphicsOptions,
                    backend,
                    out _window,
                    out _graphicsDevice);
            }
            catch (Exception vkEx)
            {
                Logger.LogError($"Failed to create Vulkan device: {vkEx.Message}. Trying OpenGL...");

                // Last resort: OpenGL
                backend = GraphicsBackend.OpenGL;
                basicGraphicsOptions = new GraphicsDeviceOptions(
                    false,
                    PixelFormat.D24_UNorm_S8_UInt,
                    true,
                    ResourceBindingModel.Default,
                    preferStandardClipSpaceYDirection: false,
                    preferDepthRangeZeroToOne: false);

                VeldridStartup.CreateWindowAndGraphicsDevice(
                    windowCI,
                    basicGraphicsOptions,
                    backend,
                    out _window,
                    out _graphicsDevice);
            }
        }
    }

    private void TryCreateWithUnixFallbacks(
        WindowCreateInfo windowCI,
        ref GraphicsDeviceOptions basicGraphicsOptions,
        GraphicsBackend backend)
    {
        try
        {
            VeldridStartup.CreateWindowAndGraphicsDevice(
                windowCI,
                basicGraphicsOptions,
                backend,
                out _window,
                out _graphicsDevice);
        }
        catch (Exception vkEx)
        {
            Logger.LogError($"Failed to create Vulkan device: {vkEx.Message}. Trying OpenGL...");

            backend = GraphicsBackend.OpenGL;
            basicGraphicsOptions = new GraphicsDeviceOptions(
                true,
                null,
                true,
                ResourceBindingModel.Improved,
                preferStandardClipSpaceYDirection: true,
                preferDepthRangeZeroToOne: true);

            VeldridStartup.CreateWindowAndGraphicsDevice(
                windowCI,
                basicGraphicsOptions,
                backend,
                out _window,
                out _graphicsDevice);
        }
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        if (_graphicsDevice != null) _graphicsDevice.SyncToVerticalBlank = settings.Hardware.EnableVSync;

        ThemeManager.ApplyTheme(settings.Appearance);
        _imGuiController.SetUIScale(settings.Appearance.UIScale);

        Logger.Log("Runtime settings applied");
    }

    private void SetWindowIcon()
    {
        try
        {
            // Load embedded image resource
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "GeoscientistToolkit.image.png";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Logger.LogWarning($"Could not find embedded resource '{resourceName}' for window icon");
                return;
            }

            // Load image using StbImageSharp
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            // Get SDL window handle
            var sdlWindowHandle = _window.SdlWindowHandle;

            // Create SDL surface from the image data
            unsafe
            {
                fixed (byte* pixelPtr = image.Data)
                {
                    // Create an SDL_Surface from the raw pixel data
                    var surface = SDL_CreateRGBSurfaceFrom(
                        (IntPtr)pixelPtr,
                        image.Width,
                        image.Height,
                        32, // bits per pixel (RGBA = 32)
                        image.Width * 4, // pitch (bytes per row)
                        0x000000FF, // R mask
                        0x0000FF00, // G mask
                        0x00FF0000, // B mask
                        0xFF000000  // A mask
                    );

                    if (surface != IntPtr.Zero)
                    {
                        // Set the window icon
                        SDL_SetWindowIcon(sdlWindowHandle, surface);

                        // Free the surface
                        SDL_FreeSurface(surface);

                        Logger.Log("Window icon set successfully");
                    }
                    else
                    {
                        Logger.LogWarning("Failed to create SDL surface for window icon");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error setting window icon: {ex.Message}");
        }
    }

    // SDL2 interop for setting window icon
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_CreateRGBSurfaceFrom(
        IntPtr pixels,
        int width,
        int height,
        int depth,
        int pitch,
        uint Rmask,
        uint Gmask,
        uint Bmask,
        uint Amask);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_SetWindowIcon(IntPtr window, IntPtr icon);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_FreeSurface(IntPtr surface);
}
