// GeoscientistToolkit/UI/SettingsWindow.cs
using System;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class SettingsWindow
    {
        private bool _isOpen;
        private SettingsCategory _selectedCategory = SettingsCategory.Appearance;
        private AppSettings _editingSettings;
        private readonly SettingsManager _settingsManager;
        
        // File dialogs
        private readonly ImGuiFileDialog _loadSettingsDialog;
        private readonly ImGuiFileDialog _saveSettingsDialog;
        private readonly ImGuiFileDialog _logPathDialog;
        private readonly ImGuiFileDialog _backupPathDialog;
        
        // UI state
        private string _saveMessage = "";
        private float _saveMessageTimer = 0f;
        private bool _showUnsavedChangesDialog = false;
        private Action _pendingAction;
        private bool _restartRequired = false;

        private enum SettingsCategory
        {
            Appearance,
            Hardware,
            Logging,
            Performance,
            FileAssociations,
            Backup
        }

        public SettingsWindow()
        {
            _settingsManager = SettingsManager.Instance;
            _loadSettingsDialog = new ImGuiFileDialog("LoadSettings", FileDialogType.OpenFile, "Load Settings");
            _saveSettingsDialog = new ImGuiFileDialog("SaveSettings", FileDialogType.OpenFile, "Save Settings");
            _logPathDialog = new ImGuiFileDialog("SelectLogPath", FileDialogType.OpenDirectory, "Select Log Directory");
            _backupPathDialog = new ImGuiFileDialog("SelectBackupPath", FileDialogType.OpenDirectory, "Select Backup Directory");
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
            if (_showUnsavedChangesDialog)
            {
                DrawUnsavedChangesDialog();
            }

            // Update save message timer
            if (_saveMessageTimer > 0)
            {
                _saveMessageTimer -= ImGui.GetIO().DeltaTime;
            }
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
            foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
            {
                string displayName = GetCategoryDisplayName(category);
                if (ImGui.Selectable(displayName, _selectedCategory == category))
                {
                    _selectedCategory = category;
                }
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
                foreach (var t in themes) { if (ImGui.Selectable(t, t == theme)) appearance.Theme = t; }
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
                foreach(var gpu in gpus) { if(ImGui.Selectable(gpu, gpu == vizGpu)) { hardware.VisualizationGPU = gpu; _restartRequired = true; } }
                ImGui.EndCombo();
            }
            HelpMarker("Preference for rendering. Requires restart. The actual device depends on the selected backend.");

            var computeGpu = hardware.ComputeGPU;
            if (ImGui.BeginCombo("Compute GPU", computeGpu))
            {
                foreach(var gpu in gpus) { if(ImGui.Selectable(gpu, gpu == computeGpu)) { hardware.ComputeGPU = gpu; } }
                ImGui.EndCombo();
            }
            HelpMarker("Preference for calculations. Takes effect immediately.");
            
            var backends = SettingsManager.GetAvailableBackends();
            var backend = hardware.PreferredGraphicsBackend;
            if (ImGui.BeginCombo("Graphics Backend", backend))
            {
                foreach (var b in backends) { if (ImGui.Selectable(b, b == backend)) { hardware.PreferredGraphicsBackend = b; _restartRequired = true; } }
                ImGui.EndCombo();
            }
            HelpMarker("Requires restart.");

            var enableVSync = hardware.EnableVSync;
            if (ImGui.Checkbox("Enable VSync", ref enableVSync)) hardware.EnableVSync = enableVSync;

            var targetFps = hardware.TargetFrameRate;
            if (ImGui.InputInt("Target Frame Rate", ref targetFps)) hardware.TargetFrameRate = Math.Clamp(targetFps, 30, 300);
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

            int logLevel = (int)logging.MinimumLogLevel;
            if (ImGui.Combo("Minimum Log Level", ref logLevel, "Trace\0Debug\0Information\0Warning\0Error\0Critical\0"))
            {
                logging.MinimumLogLevel = (LogLevel)logLevel;
            }

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
                    {
                        try { FileAssociation.Unregister(); } catch (Exception ex) { ShowSaveMessage($"Error: {ex.Message}");}
                    }
                }
                else
                {
                    ImGui.Text("❌ .gtp files are not associated.");
                    if (ImGui.Button("Register File Type"))
                    {
                        try { FileAssociation.Register(); } catch(Exception ex) { ShowSaveMessage($"Error: {ex.Message}"); }
                    }
                    ImGui.SameLine(); HelpMarker("May require administrator rights.");
                }
            }
            else
            {
                ImGui.TextDisabled("File association is only supported on Windows.");
            }
            
            ImGui.Separator();
            var autoLoad = _editingSettings.FileAssociations.AutoLoadLastProject;
            if (ImGui.Checkbox("Auto Load Last Project on Startup", ref autoLoad)) _editingSettings.FileAssociations.AutoLoadLastProject = autoLoad;
        }
        
        private void DrawBackupSettings()
        {
            var backup = _editingSettings.Backup;
            ImGui.TextColored(new Vector4(0.26f, 0.59f, 0.98f, 1.0f), "Backup Settings");
            ImGui.Separator();

            var enableBackup = backup.EnableAutoBackup;
            if (ImGui.Checkbox("Enable Auto Backup", ref enableBackup)) backup.EnableAutoBackup = enableBackup;

            var backupInterval = backup.BackupInterval;
            if (ImGui.InputInt("Backup Interval (minutes)", ref backupInterval)) backup.BackupInterval = Math.Max(1, backupInterval);

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

        private void DrawBottomButtons()
        {
            if (ImGui.Button("Restore Defaults"))
            {
                 _editingSettings = AppSettings.CreateDefaults();
                 ShowSaveMessage("Defaults loaded. Click OK or Apply to save.");
            }

            ImGui.SameLine();
            float buttonWidth = 80;
            float buttonsWidth = buttonWidth * 3 + ImGui.GetStyle().ItemSpacing.X * 2;
            ImGui.SameLine(ImGui.GetWindowWidth() - buttonsWidth - ImGui.GetStyle().WindowPadding.X);

            if (ImGui.Button("OK", new Vector2(buttonWidth, 0))) { ApplySettings(); _isOpen = false; }
            ImGui.SameLine();
            if (ImGui.Button("Apply", new Vector2(buttonWidth, 0))) { ApplySettings(); }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                if (HasUnsavedChanges()) { _pendingAction = () => _isOpen = false; _showUnsavedChangesDialog = true; }
                else { _isOpen = false; }
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
                if (ImGui.Button("Apply", new Vector2(100, 0))) { ApplySettings(); _pendingAction?.Invoke(); _pendingAction = null; _showUnsavedChangesDialog = false; ImGui.CloseCurrentPopup(); }
                ImGui.SameLine();
                if (ImGui.Button("Discard", new Vector2(100, 0))) { _pendingAction?.Invoke(); _pendingAction = null; _showUnsavedChangesDialog = false; ImGui.CloseCurrentPopup(); }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(100, 0))) { _pendingAction = null; _showUnsavedChangesDialog = false; ImGui.CloseCurrentPopup(); }
                ImGui.EndPopup();
            }
        }

        private void HandleFileDialogs()
        {
            if (_logPathDialog.Submit()) _editingSettings.Logging.LogFilePath = _logPathDialog.SelectedPath;
            if (_backupPathDialog.Submit()) _editingSettings.Backup.BackupDirectory = _backupPathDialog.SelectedPath;
        }

        private bool HasUnsavedChanges()
        {
            var currentJson = System.Text.Json.JsonSerializer.Serialize(_settingsManager.Settings);
            var editingJson = System.Text.Json.JsonSerializer.Serialize(_editingSettings);
            return currentJson != editingJson;
        }

        private string GetCategoryDisplayName(SettingsCategory category)
        {
            return System.Text.RegularExpressions.Regex.Replace(category.ToString(), "(\\B[A-Z])", " $1");
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
    }
}