// GeoscientistToolkit/Application.cs
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.Util;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.AddIns;
using GeoscientistToolkit.Business;
using System.Diagnostics;

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
        private LoadingScreen _loadingScreen;

        public void Run()
        {
            // Create window and graphics device first for loading screen
            try
            {
                Environment.SetEnvironmentVariable("SDL_WINDOWS_DPI_AWARENESS", "permonitorv2");
            }
            catch { /* Ignore */ }
            
            var windowCI = new WindowCreateInfo
            {
                X = 50, Y = 50,
                WindowWidth = 1280, WindowHeight = 720,
                WindowTitle = "GeoscientistToolkit"
            };
            
            // Basic graphics options for initial creation
            var basicGraphicsOptions = new GraphicsDeviceOptions(
                debug: true,
                swapchainDepthFormat: null,
                syncToVerticalBlank: true,
                resourceBindingModel: ResourceBindingModel.Improved,
                preferStandardClipSpaceYDirection: true,
                preferDepthRangeZeroToOne: true);
            
            try
            {
                // Create window and graphics device with default backend
                VeldridStartup.CreateWindowAndGraphicsDevice(
                    windowCI,
                    basicGraphicsOptions,
                    GetPlatformDefaultBackend(),
                    out _window,
                    out _graphicsDevice);
                
                _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
                
                // Create minimal ImGui controller for loading screen
                _imGuiController = new ImGuiController(
                    _graphicsDevice,
                    _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
                    _window.Width, _window.Height);
                
                // Create and show loading screen
                _loadingScreen = new LoadingScreen(_graphicsDevice, _commandList, _imGuiController, _window);
                _loadingScreen.UpdateStatus("Starting GeoscientistToolkit...", 0.0f);
            }
            catch (Exception ex)
            {
                // If we can't even create a basic window, show error and exit
                Logger.LogError($"Failed to create window: {ex.Message}");
                CrossPlatformMessageBox.Show(
                    $"Failed to create application window.\n\nError: {ex.Message}",
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
                
                // Corrected line: Pass the Hardware property to the method and assign the result back to it.
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
            {
                Enum.TryParse(hardwareSettings.PreferredGraphicsBackend, out preferredBackend);
            }
            else
            {
                preferredBackend = GetPlatformDefaultBackend();
            }
            
            // If user has specific graphics preferences, recreate the device
            if (hardwareSettings.PreferredGraphicsBackend != "Auto" || hardwareSettings.VisualizationGPU != "Auto")
            {
                _loadingScreen.UpdateStatus("Configuring graphics device...", 0.3f);
                
                var graphicsDeviceOptions = new GraphicsDeviceOptions(
                    debug: true,
                    swapchainDepthFormat: null,
                    syncToVerticalBlank: hardwareSettings.EnableVSync,
                    resourceBindingModel: ResourceBindingModel.Improved,
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
                        !_graphicsDevice.DeviceName.Contains(hardwareSettings.VisualizationGPU, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.LogWarning($"User preferred GPU '{hardwareSettings.VisualizationGPU}' but Veldrid selected '{_graphicsDevice.DeviceName}'. This may depend on the selected backend.");
                    }
                    
                    _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
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

            _loadingScreen.UpdateStatus("Loading add-ins...", 0.5f);
            AddInManager.Instance.Initialize();
            
            _loadingScreen.UpdateStatus("Initializing UI...", 0.6f);
            SettingsManager.Instance.SettingsChanged += OnSettingsChanged;
            _mainWindow = new MainWindow();

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable | ImGuiConfigFlags.DockingEnable;
            
            _loadingScreen.UpdateStatus("Applying theme...", 0.7f);
            ThemeManager.ApplyTheme(appSettings.Appearance);
            io.FontGlobalScale = appSettings.Appearance.UIScale;

            _window.Resized += () =>
            {
                _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                _imGuiController.WindowResized(_window.Width, _window.Height);
            };
            
            _window.Moved += (Point newPosition) => { _windowMoved = true; };
            
            _loadingScreen.UpdateStatus("Loading project...", 0.8f);
            string projectToLoad = Program.StartingProjectPath;
            if (string.IsNullOrEmpty(projectToLoad))
            {
                var fileAssocSettings = appSettings.FileAssociations;
                if (fileAssocSettings.AutoLoadLastProject && fileAssocSettings.RecentProjects.Count > 0)
                {
                    projectToLoad = fileAssocSettings.RecentProjects[0];
                }
            }

            if (!string.IsNullOrEmpty(projectToLoad) && File.Exists(projectToLoad))
            {
                try
                {
                    ProjectManager.Instance.LoadProject(projectToLoad);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to auto-load project: {ex.Message}");
                }
            }
            
            _loadingScreen.UpdateStatus("Starting application...", 1.0f);
            
            // Give a moment to see the completed loading screen
            Thread.Sleep(200);
            
            // Main application loop
            var clearColor = new Vector4(0.1f, 0.1f, 0.12f, 1.0f);
            var stopwatch = Stopwatch.StartNew();
            float frameTime = 0;

            while (_window.Exists)
            {
                if (!hardwareSettings.EnableVSync && hardwareSettings.TargetFrameRate > 0)
                {
                    float targetFrameTime = 1000f / hardwareSettings.TargetFrameRate;
                    if (frameTime < targetFrameTime)
                    {
                        Thread.Sleep((int)(targetFrameTime - frameTime));
                    }
                }
                
                frameTime = stopwatch.ElapsedMilliseconds;
                stopwatch.Restart();
                float deltaTime = frameTime / 1000f;

                InputSnapshot snapshot = _window.PumpEvents();
                if (!_window.Exists) { break; }

                if (_windowMoved)
                {
                    _windowMoved = false;
                    HandleDpiChange();
                }

                _imGuiController.Update(deltaTime, snapshot);
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
                
                BasePanel.ProcessAllPopOutWindows();
            }
            
            // Cleanup
            if (appSettings.Backup.BackupOnProjectClose && !string.IsNullOrEmpty(ProjectManager.Instance.ProjectPath))
            {
                ProjectManager.Instance.BackupProject();
            }

            if (SettingsManager.Instance.HasUnsavedChanges)
            {
                SettingsManager.Instance.SaveSettings();
            }

            GlobalPerformanceManager.Instance.Shutdown();
            AddInManager.Instance.Shutdown();

            _graphicsDevice.WaitForIdle();
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

        private void OnSettingsChanged(AppSettings settings)
        {
            if (_graphicsDevice != null)
            {
                _graphicsDevice.SyncToVerticalBlank = settings.Hardware.EnableVSync;
            }
            
            ThemeManager.ApplyTheme(settings.Appearance);
            ImGui.GetIO().FontGlobalScale = settings.Appearance.UIScale;
            
            Logger.Log("Runtime settings applied");
        }
    }
}