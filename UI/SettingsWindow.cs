// GeoscientistToolkit/UI/SettingsWindow.cs

using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public class SettingsWindow
{
    private readonly ImGuiFileDialog _backupPathDialog;

    // File dialogs
    private readonly ImGuiFileDialog _loadSettingsDialog;
    private readonly ImGuiFileDialog _logPathDialog;
    private readonly ImGuiFileDialog _saveSettingsDialog;

    // Photogrammetry file dialogs
    private readonly ImGuiFileDialog _depthModelDialog;
    private readonly ImGuiFileDialog _superPointModelDialog;
    private readonly ImGuiFileDialog _lightGlueModelDialog;
    private readonly ImGuiFileDialog _modelsDirectoryDialog;
    private readonly SettingsManager _settingsManager;
    private AppSettings _editingSettings;
    private bool _isOpen;
    private Action _pendingAction;
    private bool _restartRequired;

    // UI state
    private string _saveMessage = "";
    private float _saveMessageTimer;
    private SettingsCategory _selectedCategory = SettingsCategory.Appearance;
    private bool _showUnsavedChangesDialog;

    // Ollama state
    private List<string> _availableOllamaModels = new();
    private string _ollamaConnectionStatus;

    public SettingsWindow()
    {
        _settingsManager = SettingsManager.Instance;
        _loadSettingsDialog = new ImGuiFileDialog("LoadSettings", FileDialogType.OpenFile, "Load Settings");
        _saveSettingsDialog = new ImGuiFileDialog("SaveSettings", FileDialogType.OpenFile, "Save Settings");
        _logPathDialog = new ImGuiFileDialog("SelectLogPath", FileDialogType.OpenDirectory, "Select Log Directory");
        _backupPathDialog =
            new ImGuiFileDialog("SelectBackupPath", FileDialogType.OpenDirectory, "Select Backup Directory");

        // Photogrammetry dialogs
        _depthModelDialog = new ImGuiFileDialog("SelectDepthModel", FileDialogType.OpenFile, "Select Depth Model (ONNX)");
        _superPointModelDialog = new ImGuiFileDialog("SelectSuperPointModel", FileDialogType.OpenFile, "Select SuperPoint Model (ONNX)");
        _lightGlueModelDialog = new ImGuiFileDialog("SelectLightGlueModel", FileDialogType.OpenFile, "Select LightGlue Model (ONNX)");
        _modelsDirectoryDialog = new ImGuiFileDialog("SelectModelsDirectory", FileDialogType.OpenDirectory, "Select Models Directory");
    }

    public void Open()
    {
        _isOpen = true;
        // Create a copy of current settings for editing
        _editingSettings = _settingsManager.Settings.Clone();
        _saveMessage = "";
        _restartRequired = false;
    }

    public void Submit()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

        if (ImGui.Begin("Settings", ref _isOpen, ImGuiWindowFlags.NoCollapse))
        {
            DrawContent();
            ImGui.End();
        }

        // Handle file dialogs
        HandleFileDialogs();

        // Handle unsaved changes dialog
        if (_showUnsavedChangesDialog) DrawUnsavedChangesDialog();

        // Update save message timer
        if (_saveMessageTimer > 0) _saveMessageTimer -= ImGui.GetIO().DeltaTime;
    }

    private void DrawContent()
    {
        // Left panel with categories
        ImGui.BeginChild("CategoryList", new Vector2(180, -50), ImGuiChildFlags.Border);
        DrawCategoryList();
        ImGui.EndChild();

        ImGui.SameLine();

        // Right panel with settings
        ImGui.BeginChild("SettingsContent", new Vector2(0, -50), ImGuiChildFlags.Border);
        if (_restartRequired)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "A restart is required for some settings to take effect.");
            ImGui.Separator();
        }

        DrawSettingsContent();
        ImGui.EndChild();

        // Bottom buttons
        ImGui.Separator();
        DrawBottomButtons();
    }

    private void DrawCategoryList()
    {
        foreach (var category in Enum.GetValues<SettingsCategory>())
        {
            var displayName = GetCategoryDisplayName(category);
            if (ImGui.Selectable(displayName, _selectedCategory == category)) _selectedCategory = category;
        }
    }

    private void DrawSettingsContent()
    {
        ImGui.PushItemWidth(300);

        switch (_selectedCategory)
        {
            case SettingsCategory.Appearance: DrawAppearanceSettings(); break;
            case SettingsCategory.Hardware: DrawHardwareSettings(); break;
            case SettingsCategory.Logging: DrawLoggingSettings(); break;
            case SettingsCategory.Performance: DrawPerformanceSettings(); break;
            case SettingsCategory.FileAssociations: DrawFileAssociationSettings(); break;
            case SettingsCategory.Backup: DrawBackupSettings(); break;
            case SettingsCategory.Photogrammetry: DrawPhotogrammetrySettings(); break;
            case SettingsCategory.GIS: DrawGISSettings(); break;
            case SettingsCategory.Ollama: DrawOllamaSettings(); break;
        }

        ImGui.PopItemWidth();
    }

    private void DrawAppearanceSettings()
    {
        var appearance = _editingSettings.Appearance;
        ImGui.TextColored(new Vector4(0.26f, 0.59f, 0.98f, 1.0f), "Appearance Settings");
        ImGui.Separator();

        var theme = appearance.Theme;
        if (ImGui.BeginCombo("Theme", theme))
        {
            string[] themes = { "Dark", "Light", "Classic" };
            foreach (var t in themes)
                if (ImGui.Selectable(t, t == theme))
                    appearance.Theme = t;
            ImGui.EndCombo();
        }

        var uiScale = appearance.UIScale;
        if (ImGui.SliderFloat("UI Scale", ref uiScale, 0.5f, 2.0f, "%.2f")) appearance.UIScale = uiScale;

        var showWelcome = appearance.ShowWelcomeOnStartup;
        if (ImGui.Checkbox("Show Welcome on Startup", ref showWelcome)) appearance.ShowWelcomeOnStartup = showWelcome;

        var maxRecent = appearance.MaxRecentProjects;
        if (ImGui.InputInt("Max Recent Projects", ref maxRecent)) appearance.MaxRecentProjects = Math.Max(0, maxRecent);
    }

    private void DrawHardwareSettings()
    {
        var hardware = _editingSettings.Hardware;
        ImGui.TextColored(new Vector4(0.26f, 0.59f, 0.98f, 1.0f), "Hardware Settings");
        ImGui.Separator();

        var gpus = SettingsManager.GetAvailableGpuNames();

        var vizGpu = hardware.VisualizationGPU;
        if (ImGui.BeginCombo("Visualization GPU", vizGpu))
        {
            foreach (var gpu in gpus)
                if (ImGui.Selectable(gpu, gpu == vizGpu))
                {
                    hardware.VisualizationGPU = gpu;
                    _restartRequired = true;
                }

            ImGui.EndCombo();
        }

        HelpMarker("Preference for rendering. Requires restart. The actual device depends on the selected backend.");

        var computeGpu = hardware.ComputeGPU;
        if (ImGui.BeginCombo("Compute GPU", computeGpu))
        {
            foreach (var gpu in gpus)
                if (ImGui.Selectable(gpu, gpu == computeGpu))
                    hardware.ComputeGPU = gpu;

            ImGui.EndCombo();
        }

        HelpMarker("Preference for calculations. Takes effect immediately.");

        var backends = SettingsManager.GetAvailableBackends();
        var backend = hardware.PreferredGraphicsBackend;
        if (ImGui.BeginCombo("Graphics Backend", backend))
        {
            foreach (var b in backends)
                if (ImGui.Selectable(b, b == backend))
                {
                    hardware.PreferredGraphicsBackend = b;
                    _restartRequired = true;
                }

            ImGui.EndCombo();
        }

        HelpMarker("Requires restart.");

        var enableVSync = hardware.EnableVSync;
        if (ImGui.Checkbox("Enable VSync", ref enableVSync)) hardware.EnableVSync = enableVSync;

        var targetFps = hardware.TargetFrameRate;
        if (ImGui.InputInt("Target Frame Rate", ref targetFps))
            hardware.TargetFrameRate = Math.Clamp(targetFps, 30, 300);
    }

    private void DrawLoggingSettings()
    {
        var logging = _editingSettings.Logging;
        ImGui.TextColored(new Vector4(0.26f, 0.59f, 0.98f, 1.0f), "Logging Settings");
        ImGui.Separator();

        var logPath = logging.LogFilePath;
        if (ImGui.InputText("Log Directory", ref logPath, 512)) logging.LogFilePath = logPath;
        ImGui.SameLine();
        if (ImGui.Button("Browse...##LogPath")) _logPathDialog.Open(logging.LogFilePath);

        var logLevel = (int)logging.MinimumLogLevel;
        if (ImGui.Combo("Minimum Log Level", ref logLevel, "Trace\0Debug\0Information\0Warning\0Error\0Critical\0"))
            logging.MinimumLogLevel = (LogLevel)logLevel;

        var enableFile = logging.EnableFileLogging;
        if (ImGui.Checkbox("Enable File Logging", ref enableFile)) logging.EnableFileLogging = enableFile;

        var enableConsole = logging.EnableConsoleLogging;
        if (ImGui.Checkbox("Enable Console Logging", ref enableConsole)) logging.EnableConsoleLogging = enableConsole;
    }

    private void DrawPerformanceSettings()
    {
        var perf = _editingSettings.Performance;
        ImGui.TextColored(new Vector4(0.26f, 0.59f, 0.98f, 1.0f), "Performance Settings");
        ImGui.Separator();

        var cacheSize = perf.TextureCacheSize;
        if (ImGui.SliderInt("Texture Cache Size (MB)", ref cacheSize, 64, 8192)) perf.TextureCacheSize = cacheSize;
        HelpMarker("Memory for storing image textures. Takes effect immediately.");

        var undoSize = perf.UndoHistorySize;
        if (ImGui.SliderInt("Undo History Size", ref undoSize, 10, 200)) perf.UndoHistorySize = undoSize;
        HelpMarker("Number of actions you can undo. Takes effect immediately.");

        var lazyLoad = perf.EnableLazyLoading;
        if (ImGui.Checkbox("Enable Lazy Loading", ref lazyLoad)) perf.EnableLazyLoading = lazyLoad;
        HelpMarker("Load data only when needed to save memory.");

        var autoSave = perf.AutoSaveInterval;
        if (ImGui.InputInt("Auto-save Interval (minutes)", ref autoSave, 1)) perf.AutoSaveInterval = autoSave;
        HelpMarker("0 to disable.");
    }

    private void DrawFileAssociationSettings()
    {
        ImGui.TextColored(new Vector4(0.26f, 0.59f, 0.98f, 1.0f), "File Association Settings");
        ImGui.Separator();

        if (FileAssociation.IsAssociationSupported)
        {
            if (FileAssociation.IsAssociated())
            {
                ImGui.Text("✅ .gtp files are associated with this application.");
                if (ImGui.Button("Unregister File Type"))
                    try
                    {
                        FileAssociation.Unregister();
                    }
                    catch (Exception ex)
                    {
                        ShowSaveMessage($"Error: {ex.Message}");
                    }
            }
            else
            {
                ImGui.Text("❌ .gtp files are not associated.");
                if (ImGui.Button("Register File Type"))
                    try
                    {
                        FileAssociation.Register();
                    }
                    catch (Exception ex)
                    {
                        ShowSaveMessage($"Error: {ex.Message}");
                    }

                ImGui.SameLine();
                HelpMarker("May require administrator rights.");
            }
        }
        else
        {
            ImGui.TextDisabled("File association is only supported on Windows.");
        }

        ImGui.Separator();
        var autoLoad = _editingSettings.FileAssociations.AutoLoadLastProject;
        if (ImGui.Checkbox("Auto Load Last Project on Startup", ref autoLoad))
            _editingSettings.FileAssociations.AutoLoadLastProject = autoLoad;
    }

    private void DrawBackupSettings()
    {
        var backup = _editingSettings.Backup;
        ImGui.TextColored(new Vector4(0.26f, 0.59f, 0.98f, 1.0f), "Backup Settings");
        ImGui.Separator();

        var enableBackup = backup.EnableAutoBackup;
        if (ImGui.Checkbox("Enable Auto Backup", ref enableBackup)) backup.EnableAutoBackup = enableBackup;

        var backupInterval = backup.BackupInterval;
        if (ImGui.InputInt("Backup Interval (minutes)", ref backupInterval))
            backup.BackupInterval = Math.Max(1, backupInterval);

        var backupDir = backup.BackupDirectory;
        if (ImGui.InputText("Backup Directory", ref backupDir, 512)) backup.BackupDirectory = backupDir;
        ImGui.SameLine();
        if (ImGui.Button("Browse...##BackupPath")) _backupPathDialog.Open(backup.BackupDirectory);

        var maxBackups = backup.MaxBackupCount;
        if (ImGui.InputInt("Max Backups per Project", ref maxBackups)) backup.MaxBackupCount = Math.Max(1, maxBackups);

        var compress = backup.CompressBackups;
        if (ImGui.Checkbox("Compress Backups", ref compress)) backup.CompressBackups = compress;

        var backupOnClose = backup.BackupOnProjectClose;
        if (ImGui.Checkbox("Backup on Project Close", ref backupOnClose)) backup.BackupOnProjectClose = backupOnClose;
    }

    private void DrawPhotogrammetrySettings()
    {
        var photo = _editingSettings.Photogrammetry;
        ImGui.TextColored(new Vector4(0.26f, 0.59f, 0.98f, 1.0f), "Photogrammetry Settings");
        ImGui.Separator();

        // Model paths section
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Model Paths");
        ImGui.Spacing();

        var modelsDir = photo.ModelsDirectory;
        if (ImGui.InputText("Models Directory", ref modelsDir, 512)) photo.ModelsDirectory = modelsDir;
        ImGui.SameLine();
        if (ImGui.Button("Browse...##ModelsDir")) _modelsDirectoryDialog.Open(photo.ModelsDirectory);

        var depthPath = photo.DepthModelPath;
        if (ImGui.InputText("Depth Model (ONNX)", ref depthPath, 512)) photo.DepthModelPath = depthPath;
        ImGui.SameLine();
        if (ImGui.Button("Browse...##DepthModel")) _depthModelDialog.Open(Path.GetDirectoryName(photo.DepthModelPath), new[] { ".onnx" });

        var spPath = photo.SuperPointModelPath;
        if (ImGui.InputText("SuperPoint Model (ONNX)", ref spPath, 512)) photo.SuperPointModelPath = spPath;
        ImGui.SameLine();
        if (ImGui.Button("Browse...##SuperPoint")) _superPointModelDialog.Open(Path.GetDirectoryName(photo.SuperPointModelPath), new[] { ".onnx" });

        var lgPath = photo.LightGlueModelPath;
        if (ImGui.InputText("LightGlue Model (ONNX)", ref lgPath, 512)) photo.LightGlueModelPath = lgPath;
        ImGui.SameLine();
        if (ImGui.Button("Browse...##LightGlue")) _lightGlueModelDialog.Open(Path.GetDirectoryName(photo.LightGlueModelPath), new[] { ".onnx" });

        ImGui.Spacing();
        ImGui.Separator();

        // Model download section
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Download Models");
        ImGui.Spacing();

        if (ImGui.Button("Download MiDaS Small (Depth)", new Vector2(250, 0)))
        {
            DownloadModel("MiDaS Small", photo.MidasSmallUrl, Path.Combine(photo.ModelsDirectory, "midas_small.onnx"));
        }
        ImGui.SameLine();
        HelpMarker("Downloads MiDaS Small depth estimation model (~20 MB)");

        if (ImGui.Button("Download SuperPoint", new Vector2(250, 0)))
        {
            DownloadModel("SuperPoint", photo.SuperPointUrl, Path.Combine(photo.ModelsDirectory, "superpoint.onnx"));
        }
        ImGui.SameLine();
        HelpMarker("Downloads SuperPoint keypoint detection model (~5 MB)");

        if (!string.IsNullOrEmpty(photo.LightGlueUrl))
        {
            if (ImGui.Button("Download LightGlue", new Vector2(250, 0)))
            {
                DownloadModel("LightGlue", photo.LightGlueUrl, Path.Combine(photo.ModelsDirectory, "lightglue.onnx"));
            }
            ImGui.SameLine();
            HelpMarker("Downloads LightGlue feature matching model (~30 MB)");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Pipeline settings
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Pipeline Settings");
        ImGui.Spacing();

        var useGpu = photo.UseGpuAcceleration;
        if (ImGui.Checkbox("Use GPU Acceleration (CUDA)", ref useGpu)) photo.UseGpuAcceleration = useGpu;
        HelpMarker("Requires CUDA-capable GPU and CUDA Toolkit 11.x or 12.x");

        string[] depthModels = { "MiDaS Small (Fast)", "DPT Small (Balanced)", "ZoeDepth (Metric, Slow)" };
        var depthModelType = photo.DepthModelType;
        if (ImGui.Combo("Depth Model Type", ref depthModelType, depthModels, depthModels.Length))
            photo.DepthModelType = depthModelType;

        var kfInterval = photo.KeyframeInterval;
        if (ImGui.SliderInt("Keyframe Interval", ref kfInterval, 1, 30)) photo.KeyframeInterval = kfInterval;
        HelpMarker("Create a keyframe every N frames");

        var targetW = photo.TargetWidth;
        if (ImGui.SliderInt("Target Width", ref targetW, 320, 1920)) photo.TargetWidth = targetW;

        var targetH = photo.TargetHeight;
        if (ImGui.SliderInt("Target Height", ref targetH, 240, 1080)) photo.TargetHeight = targetH;

        ImGui.Spacing();
        ImGui.Separator();

        // Camera intrinsics
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Camera Intrinsics");
        ImGui.Spacing();

        var fxValue = photo.FocalLengthX;
        if (ImGui.DragFloat("Focal Length X", ref fxValue, 1.0f, 100, 2000)) photo.FocalLengthX = fxValue;

        var fyValue = photo.FocalLengthY;
        if (ImGui.DragFloat("Focal Length Y", ref fyValue, 1.0f, 100, 2000)) photo.FocalLengthY = fyValue;

        var pxValue = photo.PrincipalPointX;
        if (ImGui.DragFloat("Principal Point X", ref pxValue, 1.0f, 0, 2000)) photo.PrincipalPointX = pxValue;

        var pyValue = photo.PrincipalPointY;
        if (ImGui.DragFloat("Principal Point Y", ref pyValue, 1.0f, 0, 2000)) photo.PrincipalPointY = pyValue;

        if (ImGui.Button("Auto-estimate from resolution"))
        {
            photo.FocalLengthX = photo.TargetWidth * 0.8f;
            photo.FocalLengthY = photo.TargetHeight * 0.8f;
            photo.PrincipalPointX = photo.TargetWidth / 2.0f;
            photo.PrincipalPointY = photo.TargetHeight / 2.0f;
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Export settings
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Export Settings");
        ImGui.Spacing();

        string[] exportFormats = { "PLY", "XYZ", "OBJ" };
        var exportFormat = photo.DefaultExportFormat;
        var currentIdx = Array.IndexOf(exportFormats, exportFormat);
        if (currentIdx == -1) currentIdx = 0;
        if (ImGui.Combo("Default Export Format", ref currentIdx, exportFormats, exportFormats.Length))
            photo.DefaultExportFormat = exportFormats[currentIdx];

        var exportTextured = photo.ExportTexturedMesh;
        if (ImGui.Checkbox("Export Textured Mesh", ref exportTextured)) photo.ExportTexturedMesh = exportTextured;

        var exportCamera = photo.ExportCameraPath;
        if (ImGui.Checkbox("Export Camera Path", ref exportCamera)) photo.ExportCameraPath = exportCamera;
    }

    private void DownloadModel(string modelName, string url, string savePath)
    {
        if (string.IsNullOrEmpty(url))
        {
            Logger.LogError($"No download URL configured for {modelName}");
            return;
        }

        // Start download in background
        Task.Run(async () =>
        {
            try
            {
                Logger.Log($"Downloading {modelName} from {url}...");

                // Create directory if it doesn't exist
                var directory = Path.GetDirectoryName(savePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(10);

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var buffer = new byte[8192];
                var totalBytesRead = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = (float)totalBytesRead / totalBytes;
                        Logger.Log($"Downloading {modelName}: {progress * 100:F1}%");
                    }
                }

                Logger.Log($"Successfully downloaded {modelName} to {savePath}");

                // Auto-set path in settings if empty
                if (modelName.Contains("MiDaS") && string.IsNullOrEmpty(_editingSettings.Photogrammetry.DepthModelPath))
                {
                    _editingSettings.Photogrammetry.DepthModelPath = savePath;
                }
                else if (modelName.Contains("SuperPoint") && string.IsNullOrEmpty(_editingSettings.Photogrammetry.SuperPointModelPath))
                {
                    _editingSettings.Photogrammetry.SuperPointModelPath = savePath;
                }
                else if (modelName.Contains("LightGlue") && string.IsNullOrEmpty(_editingSettings.Photogrammetry.LightGlueModelPath))
                {
                    _editingSettings.Photogrammetry.LightGlueModelPath = savePath;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to download {modelName}: {ex.Message}");
            }
        });
    }

    private void DrawBottomButtons()
    {
        if (ImGui.Button("Restore Defaults"))
        {
            _editingSettings = AppSettings.CreateDefaults();
            ShowSaveMessage("Defaults loaded. Click OK or Apply to save.");
        }

        ImGui.SameLine();
        float buttonWidth = 80;
        var buttonsWidth = buttonWidth * 3 + ImGui.GetStyle().ItemSpacing.X * 2;
        ImGui.SameLine(ImGui.GetWindowWidth() - buttonsWidth - ImGui.GetStyle().WindowPadding.X);

        if (ImGui.Button("OK", new Vector2(buttonWidth, 0)))
        {
            ApplySettings();
            _isOpen = false;
        }

        ImGui.SameLine();
        if (ImGui.Button("Apply", new Vector2(buttonWidth, 0))) ApplySettings();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
        {
            if (HasUnsavedChanges())
            {
                _pendingAction = () => _isOpen = false;
                _showUnsavedChangesDialog = true;
            }
            else
            {
                _isOpen = false;
            }
        }

        if (!string.IsNullOrEmpty(_saveMessage) && _saveMessageTimer > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0, 1, 0, 1), _saveMessage);
        }
    }

    private void ApplySettings()
    {
        try
        {
            _settingsManager.UpdateSettings(_editingSettings);
            _settingsManager.SaveSettings();
            ShowSaveMessage("Settings saved successfully");
            _editingSettings = _settingsManager.Settings.Clone();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save settings: {ex.Message}");
            ShowSaveMessage("Failed to save settings");
        }
    }

    private void DrawUnsavedChangesDialog()
    {
        ImGui.OpenPopup("Unsaved Changes");
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("Unsaved Changes", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("You have unsaved changes. Do you want to apply them?");
            ImGui.Spacing();
            if (ImGui.Button("Apply", new Vector2(100, 0)))
            {
                ApplySettings();
                _pendingAction?.Invoke();
                _pendingAction = null;
                _showUnsavedChangesDialog = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Discard", new Vector2(100, 0)))
            {
                _pendingAction?.Invoke();
                _pendingAction = null;
                _showUnsavedChangesDialog = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                _pendingAction = null;
                _showUnsavedChangesDialog = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void HandleFileDialogs()
    {
        if (_logPathDialog.Submit()) _editingSettings.Logging.LogFilePath = _logPathDialog.SelectedPath;
        if (_backupPathDialog.Submit()) _editingSettings.Backup.BackupDirectory = _backupPathDialog.SelectedPath;

        // Photogrammetry dialogs
        if (_modelsDirectoryDialog.Submit()) _editingSettings.Photogrammetry.ModelsDirectory = _modelsDirectoryDialog.SelectedPath;
        if (_depthModelDialog.Submit()) _editingSettings.Photogrammetry.DepthModelPath = _depthModelDialog.SelectedPath;
        if (_superPointModelDialog.Submit()) _editingSettings.Photogrammetry.SuperPointModelPath = _superPointModelDialog.SelectedPath;
        if (_lightGlueModelDialog.Submit()) _editingSettings.Photogrammetry.LightGlueModelPath = _lightGlueModelDialog.SelectedPath;
    }

    private bool HasUnsavedChanges()
    {
        var currentJson = JsonSerializer.Serialize(_settingsManager.Settings);
        var editingJson = JsonSerializer.Serialize(_editingSettings);
        return currentJson != editingJson;
    }

    private string GetCategoryDisplayName(SettingsCategory category)
    {
        return Regex.Replace(category.ToString(), "(\\B[A-Z])", " $1");
    }

    private void DrawGISSettings()
    {
        var gis = _editingSettings.GIS;
        ImGui.TextColored(new Vector4(0.26f, 0.59f, 0.98f, 1.0f), "GIS Settings");
        ImGui.Separator();

        // Online Basemaps section
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Online Basemaps");
        ImGui.Spacing();

        var enableBasemaps = gis.EnableOnlineBasemaps;
        if (ImGui.Checkbox("Enable Online Basemaps", ref enableBasemaps))
            gis.EnableOnlineBasemaps = enableBasemaps;
        HelpMarker("Allow GIS viewer to download and display online basemap tiles");

        var autoLoad = gis.AutoLoadBasemaps;
        if (ImGui.Checkbox("Auto-load Basemaps on Startup", ref autoLoad))
            gis.AutoLoadBasemaps = autoLoad;
        HelpMarker("Automatically initialize basemaps when opening GIS datasets");

        ImGui.Spacing();
        ImGui.Text("Default Basemap Provider:");

        string[] providers = { "Satellite Imagery", "Topographic Map", "Elevation Map", "Physical/Terrain Map" };
        string[] providerIds = { "esri_imagery", "opentopomap", "esri_hillshade", "stamen_terrain" };

        var currentIndex = Array.IndexOf(providerIds, gis.DefaultBasemapProvider);
        if (currentIndex < 0) currentIndex = 0;

        if (ImGui.Combo("##DefaultProvider", ref currentIndex, providers, providers.Length))
            gis.DefaultBasemapProvider = providerIds[currentIndex];
        HelpMarker("The basemap that will be loaded by default");

        ImGui.Spacing();
        ImGui.Separator();

        // Quick access provider settings
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Basemap Providers");
        ImGui.Spacing();

        ImGui.Text("Satellite Imagery Provider:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1.0f), gis.SatelliteProvider);

        ImGui.Text("Topographic Map Provider:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1.0f), gis.TopographicProvider);

        ImGui.Text("Elevation Map Provider:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1.0f), gis.ElevationProvider);

        ImGui.Text("Physical/Terrain Provider:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1.0f), gis.PhysicalProvider);

        ImGui.Spacing();
        ImGui.Separator();

        // Cache and performance settings
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Cache & Performance");
        ImGui.Spacing();

        var defaultZoom = gis.DefaultTileZoom;
        if (ImGui.SliderInt("Default Tile Zoom Level", ref defaultZoom, 1, 15))
            gis.DefaultTileZoom = defaultZoom;
        HelpMarker("Initial zoom level for tile basemaps (1=world view, 15=detailed)");

        var maxCache = gis.MaxTileCacheSize;
        if (ImGui.SliderInt("Max Tile Cache Size (MB)", ref maxCache, 100, 2000))
            gis.MaxTileCacheSize = maxCache;
        HelpMarker("Maximum disk space for cached map tiles");

        var tileCacheDir = gis.TileCacheDirectory;
        ImGui.Text("Tile Cache Directory:");
        ImGui.TextWrapped(tileCacheDir);

        ImGui.Spacing();
        ImGui.Separator();

        // Display settings
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Default Display Options");
        ImGui.Spacing();

        var showGrid = gis.ShowGridByDefault;
        if (ImGui.Checkbox("Show Grid by Default", ref showGrid))
            gis.ShowGridByDefault = showGrid;

        var showScale = gis.ShowScaleBarByDefault;
        if (ImGui.Checkbox("Show Scale Bar by Default", ref showScale))
            gis.ShowScaleBarByDefault = showScale;

        var showNorth = gis.ShowNorthArrowByDefault;
        if (ImGui.Checkbox("Show North Arrow by Default", ref showNorth))
            gis.ShowNorthArrowByDefault = showNorth;

        var showCoords = gis.ShowCoordinatesByDefault;
        if (ImGui.Checkbox("Show Coordinates by Default", ref showCoords))
            gis.ShowCoordinatesByDefault = showCoords;

        var showAttribution = gis.ShowAttribution;
        if (ImGui.Checkbox("Show Basemap Attribution", ref showAttribution))
            gis.ShowAttribution = showAttribution;
        HelpMarker("Display attribution text for online basemap providers");
    }

    private void DrawOllamaSettings()
    {
        var ollama = _editingSettings.Ollama;
        ImGui.TextColored(new Vector4(0.26f, 0.59f, 0.98f, 1.0f), "Ollama LLM Integration");
        ImGui.Separator();

        // Enable/Disable Ollama
        var enabled = ollama.Enabled;
        if (ImGui.Checkbox("Enable Ollama Integration", ref enabled))
            ollama.Enabled = enabled;
        HelpMarker("Enable integration with local Ollama for AI-powered report generation");

        ImGui.Spacing();

        // Base URL
        ImGui.Text("Ollama Base URL:");
        var baseUrl = ollama.BaseUrl;
        if (ImGui.InputText("##BaseUrl", ref baseUrl, 256))
            ollama.BaseUrl = baseUrl;
        HelpMarker("URL of your Ollama server (default: http://localhost:11434)");

        ImGui.Spacing();

        // Test connection and fetch models
        if (ImGui.Button("Test Connection & Fetch Models"))
        {
            _ = TestOllamaConnectionAndFetchModels();
        }
        ImGui.SameLine();
        if (_ollamaConnectionStatus != null)
        {
            var color = _ollamaConnectionStatus.StartsWith("Success")
                ? new Vector4(0.2f, 0.8f, 0.2f, 1.0f)  // Green
                : new Vector4(0.8f, 0.2f, 0.2f, 1.0f); // Red
            ImGui.TextColored(color, _ollamaConnectionStatus);
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Model selection
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Model Selection");
        ImGui.Spacing();

        if (_availableOllamaModels.Count > 0)
        {
            var currentModel = ollama.SelectedModel;
            if (ImGui.BeginCombo("Selected Model", string.IsNullOrEmpty(currentModel) ? "Select a model..." : currentModel))
            {
                foreach (var model in _availableOllamaModels)
                {
                    var isSelected = model == currentModel;
                    if (ImGui.Selectable(model, isSelected))
                        ollama.SelectedModel = model;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            HelpMarker("Select which Ollama model to use for report generation");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "No models available. Click 'Test Connection & Fetch Models' first.");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Advanced settings
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1.0f), "Advanced Settings");
        ImGui.Spacing();

        var timeout = ollama.TimeoutSeconds;
        if (ImGui.SliderInt("Timeout (seconds)", ref timeout, 30, 600))
            ollama.TimeoutSeconds = timeout;
        HelpMarker("Maximum time to wait for LLM response");

        var maxTokens = ollama.MaxTokens;
        if (ImGui.SliderInt("Max Tokens", ref maxTokens, 512, 8192))
            ollama.MaxTokens = maxTokens;
        HelpMarker("Maximum length of generated report");

        var temperature = ollama.Temperature;
        if (ImGui.SliderFloat("Temperature", ref temperature, 0.0f, 2.0f, "%.2f"))
            ollama.Temperature = temperature;
        HelpMarker("Controls randomness (0.0 = deterministic, 2.0 = very creative)");
    }

    private async Task TestOllamaConnectionAndFetchModels()
    {
        _ollamaConnectionStatus = "Testing connection...";
        var baseUrl = _editingSettings.Ollama.BaseUrl;

        try
        {
            var ollamaService = Business.OllamaService.Instance;
            var connected = await ollamaService.TestConnectionAsync(baseUrl, 10);

            if (!connected)
            {
                _ollamaConnectionStatus = "Error: Cannot connect to Ollama";
                _availableOllamaModels.Clear();
                return;
            }

            var models = await ollamaService.GetAvailableModelsAsync(baseUrl, 10);

            if (models.Count == 0)
            {
                _ollamaConnectionStatus = "Success: Connected, but no models found";
                _availableOllamaModels.Clear();
            }
            else
            {
                _ollamaConnectionStatus = $"Success: Found {models.Count} model(s)";
                _availableOllamaModels = models;
            }
        }
        catch (Exception ex)
        {
            _ollamaConnectionStatus = $"Error: {ex.Message}";
            _availableOllamaModels.Clear();
        }
    }

    private void HelpMarker(string desc)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private void ShowSaveMessage(string message)
    {
        _saveMessage = message;
        _saveMessageTimer = 3.0f;
    }

    private enum SettingsCategory
    {
        Appearance,
        Hardware,
        Logging,
        Performance,
        FileAssociations,
        Backup,
        Photogrammetry,
        GIS,
        Ollama
    }
}