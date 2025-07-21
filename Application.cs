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

        public void Run()
        {
            SettingsManager.Instance.LoadSettings();
            var appSettings = SettingsManager.Instance.Settings;

            Logger.Initialize(appSettings.Logging);
            
            // Check if we should use failsafe mode
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
            
            GlobalPerformanceManager.Instance.Initialize(appSettings);
            
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

            var hardwareSettings = appSettings.Hardware;
            var graphicsDeviceOptions = new GraphicsDeviceOptions(
                debug: true,
                swapchainDepthFormat: null,
                syncToVerticalBlank: hardwareSettings.EnableVSync,
                resourceBindingModel: ResourceBindingModel.Improved,
                preferStandardClipSpaceYDirection: true,
                preferDepthRangeZeroToOne: true);

            GraphicsBackend preferredBackend;
            if (hardwareSettings.PreferredGraphicsBackend != "Auto")
            {
                Enum.TryParse(hardwareSettings.PreferredGraphicsBackend, out preferredBackend);
            }
            else
            {
                preferredBackend = GetPlatformDefaultBackend();
            }
            
            // Try to create graphics device with error handling
            bool graphicsInitialized = false;
            Exception lastException = null;
            
            try
            {
                Logger.Log($"Attempting to initialize graphics with backend: {preferredBackend}");
                
                VeldridStartup.CreateWindowAndGraphicsDevice(
                    windowCI,
                    graphicsDeviceOptions,
                    preferredBackend,
                    out _window,
                    out _graphicsDevice);
                
                graphicsInitialized = true;
                
                // Log the actual device Veldrid chose for user feedback
                Logger.Log($"Veldrid initialized successfully on device: {_graphicsDevice.DeviceName}");
                if (hardwareSettings.VisualizationGPU != "Auto" && 
                    !_graphicsDevice.DeviceName.Contains(hardwareSettings.VisualizationGPU, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogWarning($"User preferred GPU '{hardwareSettings.VisualizationGPU}' but Veldrid selected '{_graphicsDevice.DeviceName}'. This may depend on the selected backend.");
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                Logger.LogError($"Failed to initialize graphics with backend {preferredBackend}: {ex.Message}");
                
                // Record the failure
                GraphicsFailsafe.RecordFailure(preferredBackend.ToString(), hardwareSettings.VisualizationGPU);
                
                // Try fallback to most compatible settings
                if (!GraphicsFailsafe.ShouldUseFailsafe(out _))
                {
                    // Not yet in failsafe mode, try one more time with Auto settings
                    try
                    {
                        Logger.Log("Attempting graphics initialization with Auto settings...");
                        preferredBackend = GetPlatformDefaultBackend();
                        
                        VeldridStartup.CreateWindowAndGraphicsDevice(
                            windowCI,
                            graphicsDeviceOptions,
                            preferredBackend,
                            out _window,
                            out _graphicsDevice);
                        
                        graphicsInitialized = true;
                        Logger.Log($"Veldrid initialized successfully on fallback device: {_graphicsDevice.DeviceName}");
                    }
                    catch (Exception fallbackEx)
                    {
                        lastException = fallbackEx;
                        Logger.LogError($"Fallback graphics initialization also failed: {fallbackEx.Message}");
                        GraphicsFailsafe.RecordFailure("Auto", "Auto");
                    }
                }
            }
            
            if (!graphicsInitialized)
            {
                // Graphics initialization failed completely
                Logger.LogError("Failed to initialize graphics device. The application cannot start.");
                
                CrossPlatformMessageBox.Show(
                    $"Failed to initialize graphics device.\n\n" +
                    $"Error: {lastException?.Message}\n\n" +
                    $"Please try:\n" +
                    $"1. Updating your graphics drivers\n" +
                    $"2. Restarting the application (it will use safe settings after 3 failures)\n" +
                    $"3. Checking that your GPU supports the selected backend ({preferredBackend})",
                    "Graphics Initialization Failed",
                    MessageBoxType.Error);
                
                Environment.Exit(1);
                return;
            }
            
            // Graphics initialized successfully - record success
            GraphicsFailsafe.RecordSuccess(hardwareSettings);

            _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
            _imGuiController = new ImGuiController(
                _graphicsDevice,
                _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
                _window.Width, _window.Height);
                
            VeldridManager.GraphicsDevice = _graphicsDevice;
            VeldridManager.ImGuiController = _imGuiController;
            VeldridManager.RegisterImGuiController(_imGuiController);

            AddInManager.Instance.Initialize();
            SettingsManager.Instance.SettingsChanged += OnSettingsChanged;
            _mainWindow = new MainWindow();

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable | ImGuiConfigFlags.DockingEnable;
            
            ThemeManager.ApplyTheme(appSettings.Appearance);
            io.FontGlobalScale = appSettings.Appearance.UIScale;

            _window.Resized += () =>
            {
                _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                _imGuiController.WindowResized(_window.Width, _window.Height);
            };
            
            _window.Moved += (Point newPosition) => { _windowMoved = true; };
            
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