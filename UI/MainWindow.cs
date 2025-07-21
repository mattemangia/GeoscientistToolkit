// GeoscientistToolkit/UI/MainWindow.cs — Fixed to properly handle dataset removal
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using GeoscientistToolkit.Settings;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class MainWindow
    {
        // ──────────────────────────────────────────────────────────────────────
        // Fields & state
        // ──────────────────────────────────────────────────────────────────────
        private readonly DatasetPanel    _datasets   = new();
        private readonly PropertiesPanel _properties = new();
        private readonly LogPanel        _log        = new();
        private readonly ImportDataModal _import     = new();
        private readonly ToolsPanel      _tools      = new();
        private readonly SystemInfoWindow _systemInfoWindow = new();
        private readonly SettingsWindow _settingsWindow = new();
        
        // File Dialogs
        private readonly ImGuiFileDialog _loadProjectDialog = new("LoadProjectDlg", FileDialogType.OpenFile, "Load Project");
        private readonly ImGuiFileDialog _saveProjectDialog = new("SaveProjectDlg", FileDialogType.OpenFile, "Save Project As");

        private readonly List<DatasetViewPanel> _viewers = new();
        private readonly List<ThumbnailViewerPanel> _thumbnailViewers = new();

        private bool _layoutBuilt;
        private bool _showDatasets   = true;
        private bool _showProperties = true;
        private bool _showLog        = true;
        private bool _showTools      = true;
        private bool _showWelcome    = true;
        private bool _saveAsMode = false;
        private bool _showAboutPopup = false; 
        private bool _showUnsavedChangesPopup = false;
        private Action _pendingAction;

        // Timers for auto-save and backup
        private float _autoSaveTimer = 0f;
        private float _autoBackupTimer = 0f;

        private Dataset? _selectedDataset;

        public MainWindow()
        {
            // Subscribe to dataset removal events
            ProjectManager.Instance.DatasetRemoved += OnDatasetRemoved;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Dataset removal handler
        // ──────────────────────────────────────────────────────────────────────
        private void OnDatasetRemoved(Dataset dataset)
        {
            // Close any viewers showing this dataset
            for (int i = _viewers.Count - 1; i >= 0; i--)
            {
                if (_viewers[i].Dataset == dataset)
                {
                    _viewers[i].Dispose();
                    _viewers.RemoveAt(i);
                }
            }
            
            // Close thumbnail viewers if the dataset is a group
            if (dataset is DatasetGroup group)
            {
                for (int i = _thumbnailViewers.Count - 1; i >= 0; i--)
                {
                    if (_thumbnailViewers[i].Group == group)
                    {
                        _thumbnailViewers[i].Dispose();
                        _thumbnailViewers.RemoveAt(i);
                    }
                }
            }
            
            // Clear selection if the removed dataset was selected
            if (_selectedDataset == dataset)
            {
                _selectedDataset = null;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Per-frame entry
        // ──────────────────────────────────────────────────────────────────────
        public void SubmitUI()
        {
            VeldridManager.ProcessMainThreadActions();
            HandleTimers();

            var vp = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(vp.WorkPos);
            ImGui.SetNextWindowSize(vp.WorkSize);
            ImGui.SetNextWindowViewport(vp.ID);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,  Vector2.Zero);

            const ImGuiWindowFlags rootFlags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar |
                                               ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize |
                                               ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus |
                                               ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.MenuBar;

            ImGui.Begin("GeoscientistToolkit DockSpace", rootFlags);
            ImGui.PopStyleVar(3);

            SubmitMainMenu();

            uint dockspaceId = ImGui.GetID("RootDockSpace");
            ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.None);

            if (!_layoutBuilt)
            {
                _showDatasets   = true;
                _showProperties = true;
                _showLog        = true;
                _showTools      = true;
                _showWelcome = SettingsManager.Instance.Settings.Appearance.ShowWelcomeOnStartup;

                TryBuildDockLayout(dockspaceId, vp.WorkSize);
                _layoutBuilt = true;
            }

            // Panels
            if (_showDatasets)   _datasets.Submit(ref _showDatasets, OnDatasetSelected, () => _import.Open());
            if (_showProperties) _properties.Submit(ref _showProperties, _selectedDataset);
            if (_showLog) _log.Submit(ref _showLog);
            if (_showTools)      _tools.Submit(ref _showTools, _selectedDataset);

            // Viewers
            for (int i = _viewers.Count - 1; i >= 0; --i)
            {
                bool open = true;
                _viewers[i].Submit(ref open);
                if (!open)
                {
                    _viewers[i].Dispose();
                    _viewers.RemoveAt(i);
                }
            }

            for (int i = _thumbnailViewers.Count - 1; i >= 0; --i)
            {
                bool open = true;
                var viewer = _thumbnailViewers[i];
                viewer.Submit(ref open, OnDatasetSelected);
                if (!open)
                {
                    viewer.Dispose();
                    _thumbnailViewers.RemoveAt(i);
                }
            }

            // Other UI
            _import.Submit();
            SubmitFileDialogs();
            SubmitPopups();
            _systemInfoWindow.Submit();
            _settingsWindow.Submit();

            ImGui.End();
        }
        
        private void HandleTimers()
        {
            var io = ImGui.GetIO();
            var settings = SettingsManager.Instance.Settings;

            // Auto-save timer
            if (settings.Performance.AutoSaveInterval > 0 && !string.IsNullOrEmpty(ProjectManager.Instance.ProjectPath))
            {
                _autoSaveTimer += io.DeltaTime;
                if (_autoSaveTimer >= settings.Performance.AutoSaveInterval * 60)
                {
                    if (ProjectManager.Instance.HasUnsavedChanges)
                    {
                        Logger.Log("Auto-saving project...");
                        ProjectManager.Instance.SaveProject();
                    }
                    _autoSaveTimer = 0f;
                }
            }
            
            // Auto-backup timer
            if (settings.Backup.EnableAutoBackup && settings.Backup.BackupInterval > 0 && !string.IsNullOrEmpty(ProjectManager.Instance.ProjectPath))
            {
                _autoBackupTimer += io.DeltaTime;
                if (_autoBackupTimer >= settings.Backup.BackupInterval * 60)
                {
                    Logger.Log("Auto-backing up project...");
                    ProjectManager.Instance.BackupProject();
                    _autoBackupTimer = 0f;
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // DockBuilder (conditional)
        // ──────────────────────────────────────────────────────────────────────
        private static void TryBuildDockLayout(uint rootId, Vector2 size)
        {
            var io = ImGui.GetIO();
            if ((io.ConfigFlags & ImGuiConfigFlags.DockingEnable) == 0)
                io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

#if IMGUI_HAS_DOCK_BUILDER
            ImGui.DockBuilderRemoveNode(rootId);
            ImGui.DockBuilderAddNode(rootId, ImGuiDockNodeFlags.DockSpace);
            ImGui.DockBuilderSetNodeSize(rootId, size);

            uint left   = ImGui.DockBuilderSplitNode(rootId,       ImGuiDir.Left,  0.20f, out uint rem1);
            uint right  = ImGui.DockBuilderSplitNode(rem1,         ImGuiDir.Right, 0.25f, out uint rem2);
            uint bottom = ImGui.DockBuilderSplitNode(rem2,         ImGuiDir.Down,  0.25f, out uint center);
            uint right_top = ImGui.DockBuilderSplitNode(right, ImGuiDir.Up, 0.6f, out uint right_bottom);

            ImGui.DockBuilderDockWindow("Datasets",   left);
            ImGui.DockBuilderDockWindow("Properties", right_top);
            ImGui.DockBuilderDockWindow("Tools",      right_bottom);
            ImGui.DockBuilderDockWindow("Log",        bottom);
            ImGui.DockBuilderDockWindow("Thumbnails:*", center);

            ImGui.DockBuilderFinish(rootId);
#else
            DockBuilderStub.WarnOnce();
#endif
        }

#if !IMGUI_HAS_DOCK_BUILDER
        private static class DockBuilderStub
        {
            private static bool _warned;
            public static void WarnOnce()
            {
                if (_warned) return;
                _warned = true;
                System.Diagnostics.Debug.WriteLine("[MainWindow] DockBuilder API not available. " +
                                                  "Panels will float — upgrade to a docking build and define IMGUI_HAS_DOCK_BUILDER.");
            }
        }
#endif

        // ──────────────────────────────────────────────────────────────────────
        // Menu-bar
        // ──────────────────────────────────────────────────────────────────────
        private void SubmitMainMenu()
        {
            if (!ImGui.BeginMenuBar()) return;

            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New Project")) TryOnNewProject();
                if (ImGui.MenuItem("Load Project...")) TryOnLoadProject();
                if (ImGui.MenuItem("Save Project")) OnSaveProject();
                if (ImGui.MenuItem("Save Project As...")) OnSaveProjectAs();
                ImGui.Separator();
                
                // Recent Projects submenu
                if (ImGui.BeginMenu("Recent Projects"))
                {
                    var recentProjects = ProjectManager.GetRecentProjects();
                    if (recentProjects.Count == 0)
                    {
                        ImGui.MenuItem("No recent projects", false);
                    }
                    else
                    {
                        for (int i = 0; i < recentProjects.Count; i++)
                        {
                            var projectPath = recentProjects[i];
                            var projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath);
                            
                            if (ImGui.MenuItem($"{i + 1}. {projectName}"))
                            {
                                TryLoadRecentProject(projectPath);
                            }
                            
                            if (ImGui.IsItemHovered())
                            {
                                ImGui.SetTooltip(projectPath);
                            }
                        }
                        
                        ImGui.Separator();
                        if (ImGui.MenuItem("Clear Recent Projects"))
                        {
                            SettingsManager.Instance.Settings.FileAssociations.RecentProjects.Clear();
                            SettingsManager.Instance.SaveSettings();
                        }
                    }
                    ImGui.EndMenu();
                }
                
                ImGui.Separator();
                if (ImGui.MenuItem("Import Data...")) _import.Open();
                ImGui.Separator();
                if (ImGui.MenuItem("Exit")) TryExit();
                ImGui.EndMenu();
            }
            
            if (ImGui.BeginMenu("Edit"))
            {
                bool canUndo = GlobalPerformanceManager.Instance.UndoManager.CanUndo;
                if (ImGui.MenuItem("Undo", "Ctrl+Z", false, canUndo))
                {
                    GlobalPerformanceManager.Instance.UndoManager.Undo();
                }

                bool canRedo = GlobalPerformanceManager.Instance.UndoManager.CanRedo;
                if (ImGui.MenuItem("Redo", "Ctrl+Y", false, canRedo))
                {
                    GlobalPerformanceManager.Instance.UndoManager.Redo();
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Settings...", "Ctrl+,")) _settingsWindow.Open();
                ImGui.Separator();
                ImGui.MenuItem("Tools Panel", string.Empty, ref _showTools);
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                ImGui.MenuItem("Datasets Panel",   string.Empty, ref _showDatasets);
                ImGui.MenuItem("Properties Panel", string.Empty, ref _showProperties);
                ImGui.MenuItem("Log Panel",        string.Empty, ref _showLog);
                ImGui.Separator();
                if (ImGui.MenuItem("Reset Layout")) _layoutBuilt = false;
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("About")) _showAboutPopup = true;
                if (ImGui.MenuItem("System Info..."))
                {
                    _systemInfoWindow.Open(VeldridManager.GraphicsDevice);
                }
                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Pop-ups & callbacks
        // ──────────────────────────────────────────────────────────────────────
        private void TryOnNewProject()
        {
            CheckForUnsavedChanges(OnNewProject);
        }

        private void OnNewProject()
        {
            ProjectManager.Instance.NewProject();
            _selectedDataset = null;
            _viewers.ForEach(v => v.Dispose());
            _viewers.Clear();
            _thumbnailViewers.ForEach(v => v.Dispose());
            _thumbnailViewers.Clear();
        }
        
        private void TryOnLoadProject()
        {
            CheckForUnsavedChanges(() => _loadProjectDialog.Open(null, new [] { ".gtp" }));
        }
        
        private void TryLoadRecentProject(string projectPath)
        {
            CheckForUnsavedChanges(() => OnLoadProject(projectPath));
        }

        private void OnLoadProject(string path)
        {
            ProjectManager.Instance.LoadProject(path);
            _viewers.ForEach(v => v.Dispose());
            _viewers.Clear();
            _thumbnailViewers.ForEach(v => v.Dispose());
            _thumbnailViewers.Clear();
            _selectedDataset = null;
        }

        private void OnSaveProject()
        {
            if (string.IsNullOrEmpty(ProjectManager.Instance.ProjectPath))
            {
                OnSaveProjectAs();
            }
            else
            {
                ProjectManager.Instance.SaveProject();
            }
        }

        private void OnSaveProjectAs()
        {
            _saveAsMode = true;
            _saveProjectDialog.Open(null, new [] { ".gtp" });
        }
        
        private void TryExit()
        {
            CheckForUnsavedChanges(() => Environment.Exit(0));
        }

        private void CheckForUnsavedChanges(Action actionToPerform)
        {
            if (ProjectManager.Instance.HasUnsavedChanges)
            {
                _pendingAction = actionToPerform;
                _showUnsavedChangesPopup = true;
            }
            else
            {
                actionToPerform();
            }
        }

        private void OnDatasetSelected(Dataset ds)
        {
            _selectedDataset = ds;
            
            // Force load the dataset to ensure histogram data is available
            _selectedDataset?.Load();

            if (ds is DatasetGroup group)
            {
                if (_thumbnailViewers.All(v => v.Group != group))
                {
                    _thumbnailViewers.Add(new ThumbnailViewerPanel(group));
                }
            }
            else
            {
                if (_viewers.All(v => v.Dataset != ds))
                {
                    _viewers.Add(new DatasetViewPanel(ds));
                }
            }
        }
        
        private void SubmitFileDialogs()
        {
            if (_loadProjectDialog.Submit())
            {
                OnLoadProject(_loadProjectDialog.SelectedPath);
            }

            if (_saveProjectDialog.Submit())
            {
                if (_saveAsMode)
                {
                    string path = _saveProjectDialog.SelectedPath;
                    if (!path.EndsWith(".gtp", StringComparison.OrdinalIgnoreCase))
                    {
                        path += ".gtp";
                    }
                    ProjectManager.Instance.SaveProject(path);
                    _saveAsMode = false;
                }
            }
        }

        private void SubmitPopups()
        {
            // Welcome once per session - check settings
            if (_showWelcome && SettingsManager.Instance.Settings.Appearance.ShowWelcomeOnStartup)
            {
                ImGui.OpenPopup("Welcome!");
                _showWelcome = false;
            }

            bool welcome = true;
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            if (ImGui.BeginPopupModal("Welcome!", ref welcome, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Welcome to GeoscientistToolkit!");
                ImGui.Separator();
                ImGui.TextWrapped("Import data via File → Import Data. Use the 🔲 button to pop-out panels.");
                ImGui.Spacing();
                if (ImGui.Button("Let's go!", new Vector2(100, 0))) ImGui.CloseCurrentPopup();

                // Handle Enter/Escape to close
                if (ImGui.IsKeyReleased(ImGuiKey.Enter) || ImGui.IsKeyReleased(ImGuiKey.KeypadEnter) || ImGui.IsKeyReleased(ImGuiKey.Escape))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
            
            // Unsaved changes popup
            if (_showUnsavedChangesPopup)
            {
                ImGui.OpenPopup("Unsaved Changes");
                _showUnsavedChangesPopup = false;
            }

            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            if (ImGui.BeginPopupModal("Unsaved Changes", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Your project has unsaved changes. Do you want to save them?");
                ImGui.Spacing();
                
                if (ImGui.Button("Save", new Vector2(100, 0)))
                {
                    OnSaveProject();
                    _pendingAction?.Invoke();
                    _pendingAction = null;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Don't Save", new Vector2(100, 0)))
                {
                    ProjectManager.Instance.HasUnsavedChanges = false;
                    _pendingAction?.Invoke();
                    _pendingAction = null;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(100, 0)))
                {
                    _pendingAction = null;
                    ImGui.CloseCurrentPopup();
                }

                // Handle Enter for "Save" and Escape for "Cancel"
                if (ImGui.IsKeyReleased(ImGuiKey.Enter) || ImGui.IsKeyReleased(ImGuiKey.KeypadEnter))
                {
                    OnSaveProject();
                    _pendingAction?.Invoke();
                    _pendingAction = null;
                    ImGui.CloseCurrentPopup();
                }
                
                if (ImGui.IsKeyReleased(ImGuiKey.Escape))
                {
                    _pendingAction = null;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            // About popup
            if (_showAboutPopup)
            {
                ImGui.OpenPopup("About GeoscientistToolkit");
            }
            
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            if (ImGui.BeginPopupModal("About GeoscientistToolkit", ref _showAboutPopup, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("GeoscientistToolkit – Preview Build");
                ImGui.Separator();
                ImGui.TextWrapped("Open-source toolkit for geoscience data visualisation and analysis, built with Veldrid + ImGui.NET.");
                ImGui.Spacing();
                if (ImGui.Button("OK", new Vector2(100, 0)))
                {
                    _showAboutPopup = false;
                    ImGui.CloseCurrentPopup();
                }

                // Handle Enter/Escape to close
                if (ImGui.IsKeyReleased(ImGuiKey.Enter) || ImGui.IsKeyReleased(ImGuiKey.KeypadEnter) || ImGui.IsKeyReleased(ImGuiKey.Escape))
                {
                     _showAboutPopup = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }
    }
}