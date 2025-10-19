// GeoscientistToolkit/UI/MainWindow.cs

using System.Data;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.UI.GIS;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.UI.Windows;
using GeoscientistToolkit.Util;
using ImGuiNET;

// ADDED: For TableDataset and DataTable

// ADDED: To access the new editor window

namespace GeoscientistToolkit.UI;

public class MainWindow
{
    // ──────────────────────────────────────────────────────────────────────
    // Events
    // ──────────────────────────────────────────────────────────────────────
    /// <summary>
    ///     Event raised when the user confirms they want to exit the application.
    ///     Application.cs subscribes to this to know when to stop the main loop.
    /// </summary>
    public event Action OnExitConfirmed;

    // ──────────────────────────────────────────────────────────────────────
    // Fields & state
    // ──────────────────────────────────────────────────────────────────────
    private readonly DatasetPanel _datasets = new();
    private readonly PropertiesPanel _properties = new();
    private readonly LogPanel _log = new();
    private readonly ImportDataModal _import = new();
    private readonly ToolsPanel _tools = new();
    private readonly SystemInfoWindow _systemInfoWindow = new();
    private readonly SettingsWindow _settingsWindow = new();
    private readonly Volume3DDebugWindow _volume3DDebugWindow = new();
    private readonly StratigraphyCorrelationViewer _stratigraphyViewer = new();
    private readonly MaterialLibraryWindow _materialLibraryWindow = new();

    // ADDED: Instance of the compound library editor window
    private readonly CompoundLibraryEditorWindow _compoundLibraryEditorWindow = new();
    private readonly GeoScriptTerminalWindow _geoScriptTerminalWindow = new();
    private readonly ImGuiWindowScreenshotTool _screenshotTool;

    // File Dialogs
    private readonly ImGuiFileDialog
        _loadProjectDialog = new("LoadProjectDlg", FileDialogType.OpenFile, "Load Project");

    private readonly ImGuiFileDialog _saveProjectDialog =
        new("SaveProjectDlg", FileDialogType.SaveFile, "Save Project As");

    private readonly ImGuiExportFileDialog _createMeshDialog = new("CreateMeshDialog", "Create New 3D Model");

    private readonly List<DatasetViewPanel> _viewers = new();
    private readonly List<ThumbnailViewerPanel> _thumbnailViewers = new();
    private readonly ShapefileCreationDialog _shapefileCreationDialog = new();
    private bool _layoutBuilt;
    private bool _showDatasets = true;
    private bool _showProperties = true;
    private bool _showLog = true;
    private bool _showTools = true;
    private bool _showWelcome = true;
    private bool _saveAsMode;
    private bool _showAboutPopup;
    private bool _showUnsavedChangesPopup;
    private Action _pendingAction;

    // Window close handling
    private bool _showWindowCloseDialog;
    private bool _windowCloseDialogOpened;

    // Timers for auto-save and backup
    private float _autoSaveTimer;
    private float _autoBackupTimer;

    private Dataset? _selectedDataset;

    private readonly ProjectMetadataEditor _projectMetadataEditor = new();
    private readonly MetadataTableViewer _metadataTableViewer = new();

    public MainWindow()
    {
        // Subscribe to dataset removal events
        ProjectManager.Instance.DatasetRemoved += OnDatasetRemoved;
        // Subscribe to Create GIS Shapefile events
        _datasets.OnCreateShapefileFromTable += gis => _shapefileCreationDialog.OpenFromTable(gis);
        _datasets.OnCreateEmptyShapefile += gis => _shapefileCreationDialog.OpenEmpty(gis);
        // Initialize the screenshot tool
        _screenshotTool = new ImGuiWindowScreenshotTool();

        // Configure the create mesh dialog
        _createMeshDialog.SetExtensions(
            new ImGuiExportFileDialog.ExtensionOption(".obj", "Wavefront OBJ")
        );
    }

    // ──────────────────────────────────────────────────────────────────────
    // Dataset removal handler
    // ──────────────────────────────────────────────────────────────────────
    private void OnDatasetRemoved(Dataset dataset)
    {
        // Close any viewers showing this dataset
        for (var i = _viewers.Count - 1; i >= 0; i--)
            if (_viewers[i].Dataset == dataset)
            {
                _viewers[i].Dispose();
                _viewers.RemoveAt(i);
            }

        // Close thumbnail viewers if the dataset is a group
        if (dataset is DatasetGroup group)
            for (var i = _thumbnailViewers.Count - 1; i >= 0; i--)
                if (_thumbnailViewers[i].Group == group)
                {
                    _thumbnailViewers[i].Dispose();
                    _thumbnailViewers.RemoveAt(i);
                }

        // Clear selection if the removed dataset was selected
        if (_selectedDataset == dataset) _selectedDataset = null;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Per-frame entry
    // ──────────────────────────────────────────────────────────────────────
    public void SubmitUI(bool windowCloseRequested = false)
    {
        VeldridManager.ProcessMainThreadActions();
        HandleTimers();

        // Handle window close request from Application.cs (X button was clicked)
        if (windowCloseRequested && !_windowCloseDialogOpened)
        {
            _showWindowCloseDialog = true;
            _windowCloseDialogOpened = true;
            Logger.Log("Window close requested - preparing to show ImGui dialog");
        }

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.WorkPos);
        ImGui.SetNextWindowSize(vp.WorkSize);
        ImGui.SetNextWindowViewport(vp.ID);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        const ImGuiWindowFlags rootFlags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar |
                                           ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize |
                                           ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus |
                                           ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.MenuBar;

        // Add project name to window title
        var windowTitle = "GeoscientistToolkit";
        if (!string.IsNullOrEmpty(ProjectManager.Instance.ProjectName))
        {
            windowTitle = $"GeoscientistToolkit - {ProjectManager.Instance.ProjectName}";
            if (ProjectManager.Instance.HasUnsavedChanges) windowTitle += " *";
        }

        ImGui.Begin(windowTitle + "###GeoscientistToolkit DockSpace", rootFlags);
        ImGui.PopStyleVar(3);

        SubmitMainMenu();

        var dockspaceId = ImGui.GetID("RootDockSpace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.None);

        if (!_layoutBuilt)
        {
            _showDatasets = true;
            _showProperties = true;
            _showLog = true;
            _showTools = true;
            _showWelcome = SettingsManager.Instance.Settings.Appearance.ShowWelcomeOnStartup;

            TryBuildDockLayout(dockspaceId, vp.WorkSize);
            _layoutBuilt = true;
        }

        // Panels
        if (_showDatasets) _datasets.Submit(ref _showDatasets, OnDatasetSelected, () => _import.Open());
        if (_showProperties) _properties.Submit(ref _showProperties, _selectedDataset);
        if (_showLog) _log.Submit(ref _showLog);
        if (_showTools) _tools.Submit(ref _showTools, _selectedDataset);

        // Viewers
        for (var i = _viewers.Count - 1; i >= 0; --i)
        {
            var open = true;
            _viewers[i].Submit(ref open);
            if (!open)
            {
                _viewers[i].Dispose();
                _viewers.RemoveAt(i);
            }
        }

        for (var i = _thumbnailViewers.Count - 1; i >= 0; --i)
        {
            var open = true;
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
        _volume3DDebugWindow.Submit();
        _projectMetadataEditor.Submit();
        _metadataTableViewer.Submit();
        _materialLibraryWindow.Submit();
        // ADDED: Draw the compound library editor window
        _compoundLibraryEditorWindow.Draw();
        _geoScriptTerminalWindow.Draw();
        _stratigraphyViewer.Draw();
        // Handle create mesh dialog
        HandleCreateMeshDialog();

        // The screenshot tool must be updated AFTER all other UI has been submitted
        _screenshotTool.PostUpdate();
        _shapefileCreationDialog.Submit();
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
        if (settings.Backup.EnableAutoBackup && settings.Backup.BackupInterval > 0 &&
            !string.IsNullOrEmpty(ProjectManager.Instance.ProjectPath))
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

        /*#if IMGUI_HAS_DOCK_BUILDER
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
        #endif*/
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

        // Display project name in menu bar
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
        if (!string.IsNullOrEmpty(ProjectManager.Instance.ProjectName))
        {
            var projectDisplay = $"Project: {ProjectManager.Instance.ProjectName}";
            if (ProjectManager.Instance.HasUnsavedChanges) projectDisplay += " *";
            ImGui.Text(projectDisplay);
            ImGui.Separator();
        }

        ImGui.PopStyleColor();

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
                    for (var i = 0; i < recentProjects.Count; i++)
                    {
                        var projectPath = recentProjects[i];
                        var projectName = Path.GetFileNameWithoutExtension(projectPath);

                        if (ImGui.MenuItem($"{i + 1}. {projectName}")) TryLoadRecentProject(projectPath);

                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(projectPath);
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

            // NEW: Add empty mesh creation
            if (ImGui.MenuItem("New 3D Model...")) OnCreateEmptyMesh();

            if (ImGui.MenuItem("New GIS Map..."))
            {
                var emptyGIS = new GISDataset("New Map", "")
                {
                    Tags = GISTag.Editable | GISTag.VectorData
                };
                ProjectManager.Instance.AddDataset(emptyGIS);
                Logger.Log("Created new empty GIS map");
            }

            if (ImGui.MenuItem("New Empty Table..."))
            {
                // Create a new empty DataTable
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var tableName = $"New_Table_{timestamp}";
                var newTable = new DataTable(tableName);

                // Add a default column so it's not completely empty
                newTable.Columns.Add("Column1", typeof(string));
                newTable.Rows.Add("Sample value");

                // Create a new TableDataset from the DataTable
                var newTableDataset = new TableDataset(tableName, newTable);

                // Add it to the project
                ProjectManager.Instance.AddDataset(newTableDataset);
                Logger.Log($"Created new empty table: {tableName}");

                // Optionally, select it
                OnDatasetSelected(newTableDataset);
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) TryExit();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            var canUndo = GlobalPerformanceManager.Instance.UndoManager.CanUndo;
            if (ImGui.MenuItem("Undo", "Ctrl+Z", false, canUndo)) GlobalPerformanceManager.Instance.UndoManager.Undo();

            var canRedo = GlobalPerformanceManager.Instance.UndoManager.CanRedo;
            if (ImGui.MenuItem("Redo", "Ctrl+Y", false, canRedo)) GlobalPerformanceManager.Instance.UndoManager.Redo();

            ImGui.Separator();
            if (ImGui.MenuItem("Settings...", "Ctrl+,")) _settingsWindow.Open();
            ImGui.Separator();
            if (ImGui.MenuItem("Material Library...")) _materialLibraryWindow.Open();
            // ADDED: Menu item to open the Compound Library Editor
            if (ImGui.MenuItem("Compound Library...")) _compoundLibraryEditorWindow.Show();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            ImGui.MenuItem("Datasets Panel", string.Empty, ref _showDatasets);
            ImGui.MenuItem("Properties Panel", string.Empty, ref _showProperties);
            ImGui.MenuItem("Log Panel", string.Empty, ref _showLog);
            ImGui.MenuItem("Tools Panel", string.Empty, ref _showTools);
            ImGui.Separator();

            // Full Screen toggle (disabled on macOS)
            var fsSupported = VeldridManager.IsFullScreenSupported;
            var isFs = VeldridManager.IsFullScreen;
            if (ImGui.MenuItem("Full Screen (F11)", string.Empty, isFs, fsSupported)) VeldridManager.ToggleFullScreen();

            if (ImGui.MenuItem("Reset Layout")) _layoutBuilt = false;
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Tools"))
        {
            if (ImGui.MenuItem("GeoScript Terminal")) _geoScriptTerminalWindow.Show();
            if (ImGui.MenuItem("Stratigraphy Correlation Viewer")) _stratigraphyViewer.Show();
            ImGui.Separator();
            if (ImGui.MenuItem("Screenshot Fullscreen")) _screenshotTool.TakeFullScreenshot();
            if (ImGui.MenuItem("Screenshot Window...")) _screenshotTool.StartSelection();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Metadata"))
        {
            if (ImGui.MenuItem("Edit Project Metadata...")) _projectMetadataEditor.Open();

            ImGui.Separator();

            if (ImGui.MenuItem("View All Dataset Metadata...")) _metadataTableViewer.Open();

            ImGui.Separator();

            if (ImGui.MenuItem("Export Metadata to CSV...")) _metadataTableViewer.Open();
            if (ImGui.MenuItem("Export Metadata to Excel...")) _metadataTableViewer.Open();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"))
        {
            if (ImGui.MenuItem("About")) _showAboutPopup = true;
            if (ImGui.MenuItem("System Info...")) _systemInfoWindow.Open(VeldridManager.GraphicsDevice);

            ImGui.Separator();
            if (ImGui.MenuItem("3D Volume Debug...")) _volume3DDebugWindow.Show();

            ImGui.EndMenu();
        }

        if (VeldridManager.IsFullScreenSupported)
        {
            // Space to the far right
            var frameH = ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2f;
            var icon = VeldridManager.IsFullScreen ? "🗗" : "⛶";

            var iconSize = ImGui.CalcTextSize(icon);
            var btnW = iconSize.X + ImGui.GetStyle().FramePadding.X * 2f;
            var posX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - btnW;

            ImGui.SameLine();
            ImGui.SetCursorPosX(posX);

            if (ImGui.Button(icon, new Vector2(btnW, frameH))) VeldridManager.ToggleFullScreen();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Toggle Full Screen (F11)");
        }

        // Keyboard shortcut (F11)
        if (ImGui.IsKeyPressed(ImGuiKey.F11)) VeldridManager.ToggleFullScreen();

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
        CheckForUnsavedChanges(() => _loadProjectDialog.Open(null, new[] { ".gtp" }));
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
            OnSaveProjectAs();
        else
            ProjectManager.Instance.SaveProject();
    }

    private void OnSaveProjectAs()
    {
        _saveAsMode = true;
        var defaultName = string.IsNullOrEmpty(ProjectManager.Instance.ProjectName)
            ? "NewProject"
            : ProjectManager.Instance.ProjectName;
        _saveProjectDialog.Open(null, new[] { ".gtp" }, defaultName);
    }

    private void OnCreateEmptyMesh()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var defaultName = $"NewModel_{timestamp}";

            // Get user's documents folder as default location
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var defaultPath = Path.Combine(documentsPath, "3D Models");

            // Create the directory if it doesn't exist
            if (!Directory.Exists(defaultPath))
                try
                {
                    Directory.CreateDirectory(defaultPath);
                }
                catch
                {
                    // If we can't create it, just use documents folder
                    defaultPath = documentsPath;
                }

            _createMeshDialog.Open(defaultName, defaultPath);
            Logger.Log("Opening create mesh dialog");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to open create mesh dialog: {ex.Message}");
        }
    }

    private void HandleCreateMeshDialog()
    {
        if (_createMeshDialog.Submit())
            try
            {
                var filePath = _createMeshDialog.SelectedPath;
                var name = Path.GetFileNameWithoutExtension(filePath);

                Logger.Log($"Creating empty mesh: {name} at {filePath}");

                var emptyMesh = Mesh3DDataset.CreateEmpty(name, filePath);

                if (emptyMesh == null)
                {
                    Logger.LogError("Failed to create empty mesh - CreateEmpty returned null");
                    return;
                }

                // Save the initial mesh to the selected location
                try
                {
                    emptyMesh.Save();
                    Logger.Log($"Saved initial mesh to {filePath}");
                }
                catch (Exception saveEx)
                {
                    Logger.LogError($"Failed to save initial mesh: {saveEx.Message}");
                }

                ProjectManager.Instance.AddDataset(emptyMesh);
                Logger.Log($"Created new empty 3D model: {name}");

                // Auto-select and open the new mesh
                OnDatasetSelected(emptyMesh);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to create empty mesh: {ex.Message}");
                Logger.LogError($"Stack trace: {ex.StackTrace}");
            }
    }

    private void TryExit()
    {
        CheckForUnsavedChanges(ConfirmExit);
    }

    /// <summary>
    ///     Called when exit has been confirmed (either from menu or window close dialog).
    ///     Raises the OnExitConfirmed event which Application.cs listens to.
    /// </summary>
    private void ConfirmExit()
    {
        Logger.Log("Exit confirmed - invoking OnExitConfirmed event");
        OnExitConfirmed?.Invoke();
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
            if (_thumbnailViewers.All(v => v.Group != group)) _thumbnailViewers.Add(new ThumbnailViewerPanel(group));
        }
        else
        {
            if (_viewers.All(v => v.Dataset != ds)) _viewers.Add(new DatasetViewPanel(ds));
        }
    }

    private void SubmitFileDialogs()
    {
        if (_loadProjectDialog.Submit()) OnLoadProject(_loadProjectDialog.SelectedPath);

        if (_saveProjectDialog.Submit())
            if (_saveAsMode)
            {
                var path = _saveProjectDialog.SelectedPath;

                // Ensure the path has the .gtp extension
                if (!path.EndsWith(".gtp", StringComparison.OrdinalIgnoreCase)) path += ".gtp";

                Logger.Log($"Saving project to: {path}");
                ProjectManager.Instance.SaveProject(path);
                _saveAsMode = false;
            }
    }

    private void SubmitPopups()
    {
        // Welcome once per session
        if (_showWelcome && SettingsManager.Instance.Settings.Appearance.ShowWelcomeOnStartup)
        {
            ImGui.OpenPopup("Welcome!");
            _showWelcome = false;
        }

        var welcome = true;
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("Welcome!", ref welcome, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Welcome to GeoscientistToolkit!");
            ImGui.Separator();
            ImGui.TextWrapped("Import data via File → Import Data. Use the 'Pop-Out' button to pop-out panels.");
            ImGui.Spacing();
            if (ImGui.Button("Let's go!", new Vector2(100, 0))) ImGui.CloseCurrentPopup();

            // Handle Enter/Escape to close
            if (ImGui.IsKeyReleased(ImGuiKey.Enter) || ImGui.IsKeyReleased(ImGuiKey.KeypadEnter) ||
                ImGui.IsKeyReleased(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        // ============================================================================
        // Window close dialog (when user clicks X button with unsaved changes)
        // ============================================================================
        if (_showWindowCloseDialog)
        {
            ImGui.OpenPopup("Close Application?###WindowCloseDialog");
            _showWindowCloseDialog = false;
        }

        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("Close Application?###WindowCloseDialog", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("⚠ Your project has unsaved changes.");
            ImGui.Text("Do you want to save before closing?");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Calculate button widths for consistent sizing
            var buttonWidth = 150f;
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var totalWidth = buttonWidth * 3 + spacing * 2;
            var cursorX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;
            if (cursorX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + cursorX);

            // Save & Close button
            if (ImGui.Button("Save & Close", new Vector2(buttonWidth, 0)))
            {
                Logger.Log("User chose 'Save & Close' from window close dialog");
                OnSaveProject();
                _windowCloseDialogOpened = false;
                ImGui.CloseCurrentPopup();
                ConfirmExit();
            }

            ImGui.SameLine();

            // Close Without Saving button
            if (ImGui.Button("Don't Save", new Vector2(buttonWidth, 0)))
            {
                Logger.Log("User chose 'Don't Save' from window close dialog");
                ProjectManager.Instance.HasUnsavedChanges = false;
                _windowCloseDialogOpened = false;
                ImGui.CloseCurrentPopup();
                ConfirmExit();
            }

            ImGui.SameLine();

            // Cancel button
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                Logger.Log("User cancelled window close");
                _windowCloseDialogOpened = false;
                ImGui.CloseCurrentPopup();
                // Don't call ConfirmExit() - user cancelled the close operation
            }

            // Keyboard shortcuts
            if (ImGui.IsKeyReleased(ImGuiKey.Enter) || ImGui.IsKeyReleased(ImGuiKey.KeypadEnter))
            {
                Logger.Log("User pressed Enter in window close dialog - saving and closing");
                OnSaveProject();
                _windowCloseDialogOpened = false;
                ImGui.CloseCurrentPopup();
                ConfirmExit();
            }

            if (ImGui.IsKeyReleased(ImGuiKey.Escape))
            {
                Logger.Log("User pressed Escape in window close dialog - cancelling");
                _windowCloseDialogOpened = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        // ============================================================================
        // Regular unsaved changes popup (from File→Exit or other menu actions)
        // ============================================================================
        if (_showUnsavedChangesPopup)
        {
            ImGui.OpenPopup("⚠ Unsaved Changes###RegularUnsavedChanges");
            _showUnsavedChangesPopup = false;
        }

        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("⚠ Unsaved Changes###RegularUnsavedChanges", ImGuiWindowFlags.AlwaysAutoResize))
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

        // ============================================================================
        // About popup
        // ============================================================================
        if (_showAboutPopup) ImGui.OpenPopup("About GeoscientistToolkit");

        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("About GeoscientistToolkit", ref _showAboutPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("GeoscientistToolkit – Preview Build");
            ImGui.Separator();
            ImGui.TextWrapped(
                "Open-source toolkit for geoscience data visualisation and analysis, built with Veldrid + ImGui.NET.");
            ImGui.Spacing();
            if (ImGui.Button("OK", new Vector2(100, 0)))
            {
                _showAboutPopup = false;
                ImGui.CloseCurrentPopup();
            }

            // Handle Enter/Escape to close
            if (ImGui.IsKeyReleased(ImGuiKey.Enter) || ImGui.IsKeyReleased(ImGuiKey.KeypadEnter) ||
                ImGui.IsKeyReleased(ImGuiKey.Escape))
            {
                _showAboutPopup = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}