using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Security.Principal;
using System.Linq;
using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.Installer.Services;
using GeoscientistToolkit.Installer.Utilities;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace GeoscientistToolkit.Installer;

internal sealed class InstallerImGuiApp
{
    private readonly InstallerSettings _settings;
    private readonly ManifestLoader _manifestLoader;
    private readonly UpdateService _updateService;
    private readonly InstallPlanBuilder _planBuilder;
    private readonly ArchiveInstallService _archiveInstallService;

    private InstallerManifest? _manifest;
    private InstallMetadata? _metadata;
    private bool _hasUpdate;
    private string _installPath;
    private bool _createDesktopShortcut = true;
    private bool _isElevated;
    private bool _installationCompleted;
    private bool _installStarted;
    private bool _shouldClose;

    private int _selectedPackageIndex;
    private IReadOnlyList<RuntimeComponent> _currentComponents = Array.Empty<RuntimeComponent>();
    private readonly Dictionary<string, bool> _componentSelections = new(StringComparer.OrdinalIgnoreCase);

    private readonly ImGuiFileDialog _directoryDialog;

    private float _progress;
    private string _status = "Waiting...";
    private readonly ConcurrentQueue<string> _pendingLogs = new();
    private readonly List<string> _logLines = new();
    private readonly object _stateLock = new();

    private InstallerStep _step = InstallerStep.Welcome;

    public InstallerImGuiApp(
        InstallerSettings settings,
        ManifestLoader manifestLoader,
        UpdateService updateService,
        InstallPlanBuilder planBuilder,
        ArchiveInstallService archiveInstallService)
    {
        _settings = settings;
        _manifestLoader = manifestLoader;
        _updateService = updateService;
        _planBuilder = planBuilder;
        _archiveInstallService = archiveInstallService;
        _installPath = ExpandPath(settings.DefaultInstallRoot);
        _isElevated = IsRunningAsAdministrator();
        _directoryDialog = new ImGuiFileDialog("install-directory", FileDialogType.OpenDirectory, "Select Installation Folder");
    }

    public void Run()
    {
        LoadState();

        var windowCreateInfo = new WindowCreateInfo
        {
            X = 100,
            Y = 100,
            WindowWidth = 1100,
            WindowHeight = 720,
            WindowTitle = $"{_settings.ProductName} Installer"
        };

        var graphicsDeviceOptions = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: null,
            syncToVerticalBlank: true,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferStandardClipSpaceYDirection: true,
            preferDepthRangeZeroToOne: true);

        var backend = OperatingSystem.IsWindows()
            ? GraphicsBackend.Direct3D11
            : OperatingSystem.IsMacOS()
                ? GraphicsBackend.Metal
                : GraphicsBackend.Vulkan;

        Sdl2Window window;
        GraphicsDevice graphicsDevice;

        try
        {
            VeldridStartup.CreateWindowAndGraphicsDevice(
                windowCreateInfo,
                graphicsDeviceOptions,
                backend,
                out window,
                out graphicsDevice);
        }
        catch
        {
            VeldridStartup.CreateWindowAndGraphicsDevice(
                windowCreateInfo,
                graphicsDeviceOptions,
                GraphicsBackend.OpenGL,
                out window,
                out graphicsDevice);
        }

        VeldridManager.MainWindow = window;
        VeldridManager.GraphicsDevice = graphicsDevice;

        using var commandList = graphicsDevice.ResourceFactory.CreateCommandList();
        using var imGuiController = new ImGuiController(
            graphicsDevice,
            graphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
            window.Width,
            window.Height);

        VeldridManager.ImGuiController = imGuiController;
        VeldridManager.RegisterImGuiController(imGuiController);

        window.Resized += () =>
        {
            graphicsDevice.MainSwapchain.Resize((uint)window.Width, (uint)window.Height);
            imGuiController.WindowResized(window.Width, window.Height);
        };

        var stopwatch = Stopwatch.StartNew();

        while (window.Exists && !_shouldClose)
        {
            var snapshot = window.PumpEvents();
            if (!window.Exists)
            {
                break;
            }

            var deltaSeconds = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();

            imGuiController.Update(deltaSeconds, snapshot);
            DrawUi();

            commandList.Begin();
            commandList.SetFramebuffer(graphicsDevice.MainSwapchain.Framebuffer);
            commandList.ClearColorTarget(0, RgbaFloat.Black);
            imGuiController.Render(graphicsDevice, commandList);
            commandList.End();
            graphicsDevice.SubmitCommands(commandList);
            graphicsDevice.SwapBuffers(graphicsDevice.MainSwapchain);
        }

        graphicsDevice.WaitForIdle();
        VeldridManager.UnregisterImGuiController(imGuiController);
        graphicsDevice.Dispose();
        window.Close();
    }

    private void DrawUi()
    {
        ApplyPendingLogs();

        // Handle directory dialog
        if (_directoryDialog.Submit())
        {
            _installPath = _directoryDialog.SelectedPath;
        }

        ImGui.SetNextWindowSize(new Vector2(1000, 680), ImGuiCond.FirstUseEver);
        ImGui.Begin($"{_settings.ProductName} Installer", ImGuiWindowFlags.NoCollapse);

        DrawStepHeader();

        ImGui.Separator();

        switch (_step)
        {
            case InstallerStep.Welcome:
                DrawWelcomeStep();
                break;
            case InstallerStep.Options:
                DrawOptionsStep();
                break;
            case InstallerStep.Review:
                DrawReviewStep();
                break;
            case InstallerStep.Install:
                DrawInstallStep();
                break;
        }

        ImGui.End();
    }

    private void DrawStepHeader()
    {
        ImGui.Text($"{_settings.ProductName} Installer");
        ImGui.SameLine();
        ImGui.TextDisabled($"Step {(int)_step + 1} / 4");

        var statusText = _hasUpdate ? "Update available" : "Up to date";
        if (_manifest != null)
        {
            ImGui.Text($"Manifest version: {_manifest.Version} · {statusText}");
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.6f, 0.2f, 1), "Manifest unavailable");
        }
    }

    private void DrawWelcomeStep()
    {
        ImGui.TextWrapped("Welcome to the Geoscientist's Toolkit installer.");

        if (_metadata is not null)
        {
            ImGui.Text($"Installed version: {_metadata.Version}");
        }

        if (_manifest?.Prerequisites.Any() == true)
        {
            ImGui.Separator();
            ImGui.Text("Prerequisites:");
            foreach (var prereq in _manifest.Prerequisites)
            {
                ImGui.BulletText(prereq.Description);
            }
        }

        DrawNavigationButtons(canGoBack: false, canGoNext: true);
    }

    private void DrawOptionsStep()
    {
        var packages = _manifest?.Packages ?? new List<RuntimePackage>();
        if (packages.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1f), "No packages available.");
            DrawNavigationButtons(canGoBack: true, canGoNext: false);
            return;
        }

        ImGui.Text("Select package:");
        if (ImGui.BeginListBox("##packages", new Vector2(-1, 160)))
        {
            for (var i = 0; i < packages.Count; i++)
            {
                var pkg = packages[i];
                var label = $"{pkg.Description ?? pkg.PackageId} ({pkg.RuntimeIdentifier})";
                var selected = i == _selectedPackageIndex;
                if (ImGui.Selectable(label, selected))
                {
                    _selectedPackageIndex = i;
                    RefreshComponentOptions();
                }
                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndListBox();
        }

        ImGui.Spacing();
        ImGui.Text("Install path:");
        var browseButtonWidth = 80f;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - browseButtonWidth - ImGui.GetStyle().ItemSpacing.X);
        ImGui.InputText("##install-path", ref _installPath, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse...", new Vector2(browseButtonWidth, 0)))
        {
            _directoryDialog.Open(ExpandPath(_installPath));
        }

        ImGui.Spacing();
        ImGui.Text("Components:");
        if (_currentComponents.Count == 0)
        {
            ImGui.TextDisabled("No components configured.");
        }
        else
        {
            foreach (var component in _currentComponents)
            {
                var selected = _componentSelections.TryGetValue(component.Id, out var isSelected) && isSelected;
                if (ImGui.Checkbox(component.DisplayName, ref selected))
                {
                    _componentSelections[component.Id] = selected;
                }
            }
        }

        ImGui.Spacing();
        ImGui.Checkbox("Create desktop shortcut", ref _createDesktopShortcut);

        DrawNavigationButtons(canGoBack: true, canGoNext: true);
    }

    private void DrawReviewStep()
    {
        var package = GetSelectedPackage();
        var runtime = package?.RuntimeIdentifier ?? "unknown";
        var packageLabel = package?.Description ?? package?.PackageId ?? "Unknown package";
        var selectedComponents = GetSelectedComponentIds();
        var componentNames = _currentComponents.Count == 0
            ? "No components"
            : string.Join(", ", _currentComponents
                .Where(c => selectedComponents.Contains(c.Id, StringComparer.OrdinalIgnoreCase))
                .Select(c => c.DisplayName));

        ImGui.Text("Review your selections:");
        ImGui.Separator();
        ImGui.Text($"Package: {packageLabel}");
        ImGui.Text($"Runtime: {runtime}");
        ImGui.Text($"Install path: {ExpandPath(_installPath)}");
        ImGui.TextWrapped($"Components: {componentNames}");
        ImGui.Text($"Desktop shortcut: {(_createDesktopShortcut ? "Yes" : "No")}");

        if (OperatingSystem.IsWindows())
        {
            ImGui.Text($"Administrator privileges: {(_isElevated ? "enabled" : "not enabled")}");
            if (!_isElevated)
            {
                ImGui.Spacing();
                if (ImGui.Button("Request administrator privileges"))
                {
                    RequestElevation();
                }
            }
        }

        DrawNavigationButtons(canGoBack: true, canGoNext: true, nextLabel: "Install");
    }

    private void DrawInstallStep()
    {
        if (!_installStarted)
        {
            _installStarted = true;
            StartInstallation();
        }

        float progress;
        string status;
        lock (_stateLock)
        {
            progress = _progress;
            status = _status;
        }

        ImGui.Text(status);
        ImGui.ProgressBar(progress, new Vector2(-1, 0));

        ImGui.Separator();
        ImGui.Text("Activity log:");
        ImGui.BeginChild("log", new Vector2(0, 300), ImGuiChildFlags.Border);
        foreach (var line in _logLines)
        {
            ImGui.TextUnformatted(line);
        }
        ImGui.EndChild();

        if (_installationCompleted)
        {
            if (ImGui.Button("Finish"))
            {
                _shouldClose = true;
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("Installing...");
            ImGui.EndDisabled();
        }
    }

    private void DrawNavigationButtons(bool canGoBack, bool canGoNext, string nextLabel = "Next")
    {
        ImGui.Spacing();
        ImGui.Separator();

        if (canGoBack)
        {
            if (ImGui.Button("Back"))
            {
                _step = _step switch
                {
                    InstallerStep.Options => InstallerStep.Welcome,
                    InstallerStep.Review => InstallerStep.Options,
                    InstallerStep.Install => InstallerStep.Review,
                    _ => _step
                };
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("Back");
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel"))
        {
            _shouldClose = true;
        }

        ImGui.SameLine();

        if (canGoNext)
        {
            if (ImGui.Button(nextLabel))
            {
                _step = _step switch
                {
                    InstallerStep.Welcome => InstallerStep.Options,
                    InstallerStep.Options => InstallerStep.Review,
                    InstallerStep.Review => InstallerStep.Install,
                    _ => _step
                };
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button(nextLabel);
            ImGui.EndDisabled();
        }
    }

    private void LoadState()
    {
        _metadata = _updateService.TryLoadExistingMetadata(_installPath);
        var updateInfo = _updateService.CheckForUpdatesAsync(_metadata).GetAwaiter().GetResult();
        _manifest = updateInfo.manifest;
        _hasUpdate = updateInfo.hasUpdate;

        if (_metadata is not null && _manifest is not null)
        {
            var index = _manifest.Packages.FindIndex(p =>
                string.Equals(p.PackageId, _metadata.PackageId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.RuntimeIdentifier, _metadata.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _selectedPackageIndex = index;
            }
            _installPath = _metadata.InstallPath;
            _createDesktopShortcut = _metadata.CreateDesktopShortcut;
            if (_metadata.Components.Count > 0)
            {
                _componentSelections.Clear();
                foreach (var component in _metadata.Components)
                {
                    _componentSelections[component] = true;
                }
            }
        }
        else if (_manifest is not null)
        {
            var defaultIndex = _manifest.Packages.FindIndex(p =>
                string.Equals(p.PackageId, "imgui", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.RuntimeIdentifier, RuntimeHelper.GetCurrentRuntimeIdentifier(), StringComparison.OrdinalIgnoreCase));
            _selectedPackageIndex = defaultIndex >= 0 ? defaultIndex : 0;
        }

        RefreshComponentOptions();
    }

    private void RefreshComponentOptions()
    {
        if (_manifest is null || _manifest.Packages.Count == 0)
        {
            _currentComponents = Array.Empty<RuntimeComponent>();
            return;
        }

        var package = GetSelectedPackage();
        _currentComponents = (IReadOnlyList<RuntimeComponent>?)package?.Components ?? Array.Empty<RuntimeComponent>();

        var knownIds = new HashSet<string>(_currentComponents.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var toRemove = _componentSelections.Keys.Where(k => !knownIds.Contains(k)).ToList();
        foreach (var key in toRemove)
        {
            _componentSelections.Remove(key);
        }

        foreach (var component in _currentComponents)
        {
            if (!_componentSelections.ContainsKey(component.Id))
            {
                _componentSelections[component.Id] = component.DefaultSelected;
            }
        }
    }

    private RuntimePackage? GetSelectedPackage()
    {
        if (_manifest is null || _manifest.Packages.Count == 0)
        {
            return null;
        }

        var index = Math.Clamp(_selectedPackageIndex, 0, _manifest.Packages.Count - 1);
        return _manifest.Packages[index];
    }

    private IReadOnlyCollection<string> GetSelectedComponentIds()
    {
        if (_currentComponents.Count == 0)
        {
            return Array.Empty<string>();
        }

        var selected = _currentComponents
            .Where(component => _componentSelections.TryGetValue(component.Id, out var value) ? value : component.DefaultSelected)
            .Select(component => component.Id)
            .ToList();

        return selected;
    }

    private void StartInstallation()
    {
        if (_installationCompleted)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteInstallationAsync().ConfigureAwait(false);
                UpdateProgress(1f, "Installation completed successfully!");
                _installationCompleted = true;
            }
            catch (Exception ex)
            {
                UpdateProgress(_progress, $"Error: {ex.Message}");
                Log($"[ERROR] {ex}");
                _installationCompleted = true;
            }
        });
    }

    private async Task ExecuteInstallationAsync()
    {
        if (_manifest is null)
        {
            throw new InvalidOperationException("Manifest not loaded.");
        }

        var package = GetSelectedPackage()
            ?? throw new InvalidOperationException("No package selected.");
        var componentIds = GetSelectedComponentIds();
        var plan = _planBuilder.CreatePlan(
            _manifest,
            package,
            ExpandPath(_installPath),
            componentIds,
            _createDesktopShortcut);

        UpdateProgress(0.05f, "Preparing folders");
        if (Directory.Exists(plan.InstallPath))
        {
            Directory.Delete(plan.InstallPath, true);
        }
        Directory.CreateDirectory(plan.InstallPath);

        string payloadPath;
        if (plan.Package.Transport == PackageTransport.Archive)
        {
            UpdateProgress(0.15f, "Downloading package");
            payloadPath = await _archiveInstallService.DownloadAndExtractAsync(plan.Package, Log).ConfigureAwait(false);
        }
        else
        {
            throw new NotSupportedException("The selected package does not provide a compressed archive.");
        }

        UpdateProgress(0.6f, "Copying components");
        foreach (var component in plan.Components)
        {
            var source = string.IsNullOrWhiteSpace(component.RelativePath) || component.RelativePath == "."
                ? payloadPath
                : Path.Combine(payloadPath, component.RelativePath);
            var target = Path.Combine(plan.InstallPath, component.TargetSubdirectory ?? string.Empty);
            CopyDirectory(source, target);
        }

        UpdateProgress(0.8f, "Creating launchers");
        CreateLaunchers(plan);

        if (plan.CreateDesktopShortcut)
        {
            UpdateProgress(0.85f, "Creating desktop shortcut");
            CreateDesktopShortcut(plan);
        }

        UpdateProgress(0.9f, "Writing metadata");
        await _updateService.SaveMetadataAsync(plan).ConfigureAwait(false);
    }

    private void UpdateProgress(float percentage, string status)
    {
        lock (_stateLock)
        {
            _progress = percentage;
            _status = status;
        }
    }

    private void Log(string message)
    {
        _pendingLogs.Enqueue(message);
    }

    private void ApplyPendingLogs()
    {
        while (_pendingLogs.TryDequeue(out var message))
        {
            _logLines.Add(message);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Source path not found: {source}");
        }

        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            var targetDir = Path.Combine(destination, relative);
            Directory.CreateDirectory(targetDir);
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, true);
        }
    }

    private void CreateLaunchers(InstallPlan plan)
    {
        var launchComponent = plan.Components.FirstOrDefault(c => !string.IsNullOrEmpty(c.EntryExecutable));
        if (launchComponent is null || string.IsNullOrEmpty(launchComponent.EntryExecutable))
        {
            return;
        }

        var executableRelative = Path.Combine(launchComponent.TargetSubdirectory ?? string.Empty, launchComponent.EntryExecutable);
        var executableFull = Path.Combine(plan.InstallPath, executableRelative);
        if (!File.Exists(executableFull))
        {
            return;
        }

        var scriptBase = GetLauncherBaseName(plan.Package.PackageId);
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(plan.InstallPath, $"{scriptBase}.cmd");
            File.WriteAllText(script, $"@echo off\r\n\"%~dp0{executableRelative}\" %*\r\n");
        }
        else
        {
            var script = Path.Combine(plan.InstallPath, $"{scriptBase}.sh");
            var relativePath = executableRelative.Replace('\\', '/');
            File.WriteAllText(script, $"#!/bin/sh\nDIR=\"$(cd \"$(dirname \"$0\")\" && pwd)\"\n\"$DIR/{relativePath}\" \"$@\"\n");
            MakeExecutable(script);
        }
    }

    private static string GetLauncherBaseName(string packageId)
    {
        return packageId.ToLowerInvariant() switch
        {
            "gtk" => "geoscientist-toolkit-gtk",
            "imgui" => "geoscientist-toolkit-imgui",
            "node-endpoint" => "geoscientist-toolkit-node-endpoint",
            "node-server" => "geoscientist-toolkit-node-server",
            _ => "geoscientist-toolkit"
        };
    }

    private void CreateDesktopShortcut(InstallPlan plan)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            return;
        }

        var shortcutComponent = plan.Components.FirstOrDefault(c => c.SupportsDesktopShortcut && !string.IsNullOrEmpty(c.EntryExecutable));
        if (shortcutComponent is null)
        {
            return;
        }

        var executableRelative = Path.Combine(shortcutComponent.TargetSubdirectory ?? string.Empty, shortcutComponent.EntryExecutable);
        var executableFull = Path.Combine(plan.InstallPath, executableRelative);
        if (!File.Exists(executableFull))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            CreateWindowsShortcut(desktop, executableFull);
        }
        else if (OperatingSystem.IsLinux())
        {
            CreateDesktopFile(desktop, executableFull);
        }
        else if (OperatingSystem.IsMacOS())
        {
            CreateMacShortcut(desktop, executableFull);
        }
    }

    private void CreateWindowsShortcut(string desktop, string target)
    {
        var shortcut = Path.Combine(desktop, $"{_settings.ProductName}.lnk");
        var workingDirectory = Path.GetDirectoryName(target) ?? desktop;
        var psScript = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut(\"{shortcut}\"); $s.TargetPath = \"{target}\"; $s.WorkingDirectory = \"{workingDirectory}\"; $s.Save();";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoLogo -NoProfile -WindowStyle Hidden -Command \"{psScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();
        }
        catch (Exception ex)
        {
            Log($"Unable to create Windows shortcut: {ex.Message}");
        }
    }

    private static void CreateDesktopFile(string desktop, string target)
    {
        var shortcut = Path.Combine(desktop, "geoscientist-toolkit.desktop");
        var content = $"[Desktop Entry]\nType=Application\nName=Geoscientist's Toolkit\nExec=\"{target}\"\nTerminal=false\n";
        File.WriteAllText(shortcut, content);
        MakeExecutable(shortcut);
    }

    private static void CreateMacShortcut(string desktop, string target)
    {
        var shortcut = Path.Combine(desktop, "GeoscientistToolkit.command");
        File.WriteAllText(shortcut, $"#!/bin/bash\n\"{target}\" \"$@\"\n");
        MakeExecutable(shortcut);
    }

    private static void MakeExecutable(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();
        }
        catch
        {
            // ignore
        }
    }

    private void RequestElevation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var currentProcessPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentProcessPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = currentProcessPath,
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                Verb = "runas"
            });
            _shouldClose = true;
        }
        catch (Win32Exception ex)
        {
            Log($"Elevation cancelled or failed: {ex.Message}");
        }
    }

    private static string ExpandPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path ?? string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, expanded[2..]);
        }

        return Path.GetFullPath(expanded);
    }

    private static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private enum InstallerStep
    {
        Welcome,
        Options,
        Review,
        Install
    }
}
