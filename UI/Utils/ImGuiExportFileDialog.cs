// GeoscientistToolkit/UI/Utils/ImGuiExportFileDialog.cs

using System.Numerics;
using System.Runtime.InteropServices;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Utils;

public enum FileDialogType
{
    OpenFile,
    SaveFile,
    OpenDirectory,
    SaveStack,
    SaveLabelStack
}

public class ImGuiExportFileDialog
{
    private readonly List<VolumeInfo> _availableVolumes = new();
    private readonly float _drivePanelWidth = 180f;
    private readonly List<ExtensionOption> _extensionOptions = new();

    private readonly string _id;
    private readonly string _title;

    // Context menu and operations
    private string _contextMenuTarget = "";
    private string _createFolderError = "";
    private bool _deleteTargetIsDirectory;
    private string _deleteTargetPath = "";
    private string _fileName = "";
    private bool _isContextMenuTargetDirectory;
    private string _newFolderName = "";
    private string _renameError = "";
    private string _renameNewName = "";
    private int _selectedExtensionIndex;
    private string _selectedFileNameInList = "";

    // Create folder popup
    private bool _showCreateFolderPopup;
    private bool _showDeleteConfirmation;
    private bool _showRenamePopup;
    public bool IsOpen;

    public ImGuiExportFileDialog(string id, string title = "Export File")
    {
        _id = id;
        _title = title;
        CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RefreshDrives();
    }

    public string SelectedPath { get; private set; } = "";
    public string CurrentDirectory { get; private set; }
    public string SelectedExtension { get; private set; } = "";

    private void RefreshDrives()
    {
        _availableVolumes.Clear();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: Use DriveInfo
            foreach (var drive in DriveInfo.GetDrives())
                try
                {
                    var volume = new VolumeInfo
                    {
                        Path = drive.RootDirectory.FullName,
                        DisplayName = drive.Name.TrimEnd('\\'),
                        DriveType = drive.DriveType,
                        IsReady = drive.IsReady
                    };

                    if (drive.IsReady)
                    {
                        volume.VolumeLabel = string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                        volume.TotalBytes = drive.TotalSize;
                        volume.AvailableBytes = drive.AvailableFreeSpace;
                    }
                    else
                    {
                        volume.VolumeLabel = GetDriveTypeLabel(drive.DriveType);
                    }

                    _availableVolumes.Add(volume);
                }
                catch
                {
                }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: Common mount points
            AddUnixVolume("/", "Root");
            AddUnixVolume(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Home");

            // Check for mounted volumes
            if (Directory.Exists("/Volumes"))
                foreach (var dir in Directory.GetDirectories("/Volumes"))
                    AddUnixVolume(dir, Path.GetFileName(dir));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux: Common mount points
            AddUnixVolume("/", "Root");
            AddUnixVolume(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Home");

            // Check common mount directories
            string[] mountDirs = { "/media", "/mnt", "/run/media" };
            foreach (var mountDir in mountDirs)
                if (Directory.Exists(mountDir))
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(mountDir))
                            AddUnixVolume(dir, Path.GetFileName(dir));
                    }
                    catch
                    {
                    }
        }
    }

    private void AddUnixVolume(string path, string name)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            var volume = new VolumeInfo
            {
                Path = path,
                DisplayName = name,
                VolumeLabel = name,
                DriveType = DriveType.Fixed,
                IsReady = true
            };

            // Try to get disk space info
            try
            {
                var driveInfo = new DriveInfo(path);
                if (driveInfo.IsReady)
                {
                    volume.TotalBytes = driveInfo.TotalSize;
                    volume.AvailableBytes = driveInfo.AvailableFreeSpace;
                }
            }
            catch
            {
            }

            _availableVolumes.Add(volume);
        }
        catch
        {
        }
    }

    private string GetDriveTypeLabel(DriveType type)
    {
        return type switch
        {
            DriveType.Removable => "Removable",
            DriveType.Fixed => "Local Disk",
            DriveType.Network => "Network Drive",
            DriveType.CDRom => "CD/DVD",
            DriveType.Ram => "RAM Disk",
            _ => "Unknown"
        };
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    public void SetExtensions(params ExtensionOption[] options)
    {
        _extensionOptions.Clear();
        _extensionOptions.AddRange(options);
        if (_extensionOptions.Count > 0)
        {
            _selectedExtensionIndex = 0;
            SelectedExtension = _extensionOptions[0].Extension;
        }
    }

    public void SetExtensions(params (string extension, string description)[] options)
    {
        _extensionOptions.Clear();
        foreach (var (ext, desc) in options) _extensionOptions.Add(new ExtensionOption(ext, desc));
        if (_extensionOptions.Count > 0)
        {
            _selectedExtensionIndex = 0;
            SelectedExtension = _extensionOptions[0].Extension;
        }
    }

    public void Open(string defaultFileName = "", string startingPath = null)
    {
        IsOpen = true;
        SelectedPath = "";
        _fileName = defaultFileName;
        _selectedFileNameInList = "";

        if (!string.IsNullOrEmpty(startingPath) && Directory.Exists(startingPath))
            CurrentDirectory = startingPath;
        else
            CurrentDirectory = Directory.GetCurrentDirectory();

        _showCreateFolderPopup = false;
        _newFolderName = "";
        _createFolderError = "";
        _showRenamePopup = false;
        _showDeleteConfirmation = false;
        RefreshDrives();
    }

    public bool Submit()
    {
        var selectionMade = false;
        if (IsOpen)
        {
            // Increased window size to prevent scrollbars
            ImGui.SetNextWindowSize(new Vector2(950, 650), ImGuiCond.FirstUseEver);
            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

            if (ImGui.Begin(_title + "###" + _id, ref IsOpen,
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
            {
                DrawPathNavigation();

                // More accurate calculation for bottom controls height
                // Path nav: 1 row + separator
                // Bottom controls: separator + 3 input rows + warning/empty line + button row
                var pathNavHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2;
                var bottomControlsHeight = ImGui.GetFrameHeightWithSpacing() * 5 + ImGui.GetStyle().ItemSpacing.Y * 6;
                var totalReservedHeight = bottomControlsHeight + ImGui.GetStyle().WindowPadding.Y * 2;

                // Calculate available height for the file browser
                var availableHeight = ImGui.GetContentRegionAvail().Y;
                var contentHeight = Math.Max(200, availableHeight - totalReservedHeight);

                // Draw drive panel and file list side by side
                if (ImGui.BeginTable("##ExportFileDialogLayout", 2,
                        ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable))
                {
                    ImGui.TableSetupColumn("##DrivePanel", ImGuiTableColumnFlags.WidthFixed, _drivePanelWidth);
                    ImGui.TableSetupColumn("##FileList", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    DrawDrivePanel(new Vector2(0, contentHeight));

                    ImGui.TableNextColumn();
                    DrawFileList(new Vector2(0, contentHeight));

                    ImGui.EndTable();
                }

                // Add small spacing before bottom controls
                ImGui.Dummy(new Vector2(0, ImGui.GetStyle().ItemSpacing.Y));
                DrawBottomControls(ref selectionMade);

                ImGui.End();
            }

            // Draw popups (these can be outside since they're modal)
            if (_showCreateFolderPopup)
                DrawCreateFolderPopup();

            if (_showRenamePopup)
                DrawRenamePopup();

            if (_showDeleteConfirmation)
                DrawDeleteConfirmation();
        }

        return selectionMade;
    }

    private void DrawDrivePanel(Vector2 size)
    {
        if (ImGui.BeginChild("##DrivePanel", size, ImGuiChildFlags.None))
        {
            ImGui.Text("Drives");
            ImGui.Separator();

            foreach (var volume in _availableVolumes)
            {
                var isCurrentDrive = CurrentDirectory.StartsWith(volume.Path,
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal);

                if (isCurrentDrive)
                    ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);

                var buttonLabel = volume.DisplayName;
                if (!string.IsNullOrEmpty(volume.VolumeLabel) && volume.VolumeLabel != volume.DisplayName)
                    buttonLabel = $"{volume.DisplayName} ({volume.VolumeLabel})";

                if (ImGui.Button($"{buttonLabel}###{volume.Path}", new Vector2(-1, 0)))
                    if (volume.IsReady && Directory.Exists(volume.Path))
                    {
                        CurrentDirectory = volume.Path;
                        _selectedFileNameInList = "";
                    }

                if (isCurrentDrive)
                    ImGui.PopStyleColor();

                // Show space info if available
                if (volume.IsReady && volume.TotalBytes > 0)
                {
                    var ratio = 1.0f - (float)volume.AvailableBytes / volume.TotalBytes;
                    ImGui.ProgressBar(ratio, new Vector2(-1, 4), "");

                    ImGui.Text($"{FormatBytes(volume.AvailableBytes)} free");
                    ImGui.Text($"of {FormatBytes(volume.TotalBytes)}");
                }
                else if (!volume.IsReady)
                {
                    ImGui.TextDisabled("Not ready");
                }

                ImGui.Spacing();
            }

            // Refresh button
            ImGui.Separator();
            if (ImGui.Button("Refresh Drives", new Vector2(-1, 0))) RefreshDrives();

            ImGui.EndChild();
        }
    }

    private void DrawPathNavigation()
    {
        var tempPath = CurrentDirectory;
        var upButtonWidth = 40f;
        var newFolderButtonWidth = 100f;

        var totalButtons = upButtonWidth + newFolderButtonWidth + ImGui.GetStyle().ItemSpacing.X * 2;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - totalButtons);

        if (ImGui.InputText("##Path", ref tempPath, 260))
            if (Directory.Exists(tempPath))
                CurrentDirectory = tempPath;

        ImGui.SameLine();
        if (ImGui.Button("Up", new Vector2(upButtonWidth, 0)))
        {
            var parent = Directory.GetParent(CurrentDirectory);
            if (parent != null)
                CurrentDirectory = parent.FullName;
        }

        ImGui.SameLine();
        if (ImGui.Button("New Folder", new Vector2(newFolderButtonWidth, 0)))
        {
            _showCreateFolderPopup = true;
            _newFolderName = "New Folder";
            _createFolderError = "";
        }

        ImGui.Separator();
    }

    private void DrawFileList(Vector2 size)
    {
        if (ImGui.BeginChild("##FileList", size, ImGuiChildFlags.Border))
        {
            try
            {
                // Draw directories
                foreach (var dir in Directory.GetDirectories(CurrentDirectory))
                {
                    var dirName = Path.GetFileName(dir);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.5f, 1.0f));

                    var selectableId = $"[D] {dirName}###{dir}";
                    if (ImGui.Selectable(selectableId, false, ImGuiSelectableFlags.AllowDoubleClick))
                        if (ImGui.IsMouseDoubleClicked(0))
                        {
                            CurrentDirectory = dir;
                            _selectedFileNameInList = "";
                        }

                    // Handle right-click context menu - using more reliable detection
                    if (ImGui.BeginPopupContextItem($"##ctx_{dir}"))
                    {
                        _contextMenuTarget = dir;
                        _isContextMenuTargetDirectory = true;
                        DrawContextMenuItems();
                        ImGui.EndPopup();
                    }

                    ImGui.PopStyleColor();
                }

                // Draw files
                foreach (var file in Directory.GetFiles(CurrentDirectory))
                {
                    var fileName = Path.GetFileName(file);
                    var isSelected = _selectedFileNameInList == fileName;

                    var selectableId = $"[F] {fileName}###{file}";
                    if (ImGui.Selectable(selectableId, isSelected))
                    {
                        _selectedFileNameInList = fileName;
                        _fileName = Path.GetFileNameWithoutExtension(fileName);
                    }

                    // Handle right-click context menu - using more reliable detection
                    if (ImGui.BeginPopupContextItem($"##ctx_{file}"))
                    {
                        _contextMenuTarget = file;
                        _isContextMenuTargetDirectory = false;
                        DrawContextMenuItems();
                        ImGui.EndPopup();
                    }
                }
            }
            catch (Exception ex)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error: {ex.Message}");
            }

            ImGui.EndChild();
        }
    }

    private void DrawContextMenuItems()
    {
        var targetName = Path.GetFileName(_contextMenuTarget);
        ImGui.Text(_isContextMenuTargetDirectory ? $"Folder: {targetName}" : $"File: {targetName}");
        ImGui.Separator();

        if (ImGui.MenuItem("Select"))
            if (!_isContextMenuTargetDirectory)
            {
                _selectedFileNameInList = targetName;
                _fileName = Path.GetFileNameWithoutExtension(targetName);
            }

        if (ImGui.MenuItem("Rename"))
        {
            _renameNewName = targetName;
            _renameError = "";
            _showRenamePopup = true;
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Delete"))
        {
            _deleteTargetPath = _contextMenuTarget;
            _deleteTargetIsDirectory = _isContextMenuTargetDirectory;
            _showDeleteConfirmation = true;
        }
    }

    private void DrawRenamePopup()
    {
        ImGui.OpenPopup("Rename Item");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Rename Item", ref _showRenamePopup,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.Text($"Rename {(_isContextMenuTargetDirectory ? "folder" : "file")}:");
            ImGui.Text(Path.GetFileName(_contextMenuTarget));
            ImGui.Separator();

            ImGui.Text("New name:");
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("##RenameInput", ref _renameNewName, 256)) _renameError = "";

            if (!string.IsNullOrEmpty(_renameError)) ImGui.TextColored(new Vector4(1, 0, 0, 1), _renameError);

            ImGui.Separator();

            if (ImGui.Button("Rename", new Vector2(100, 0)))
                if (PerformRename())
                    _showRenamePopup = false;

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                _showRenamePopup = false;
                _renameError = "";
            }

            ImGui.EndPopup();
        }
    }

    private bool PerformRename()
    {
        if (string.IsNullOrWhiteSpace(_renameNewName))
        {
            _renameError = "Name cannot be empty";
            return false;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        if (_renameNewName.Any(c => invalidChars.Contains(c)))
        {
            _renameError = "Name contains invalid characters";
            return false;
        }

        var newPath = Path.Combine(Path.GetDirectoryName(_contextMenuTarget), _renameNewName);

        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            _renameError = "An item with this name already exists";
            return false;
        }

        try
        {
            if (_isContextMenuTargetDirectory)
            {
                Directory.Move(_contextMenuTarget, newPath);
                Logger.Log($"[ImGuiExportFileDialog] Renamed folder: {_contextMenuTarget} -> {newPath}");
            }
            else
            {
                File.Move(_contextMenuTarget, newPath);
                Logger.Log($"[ImGuiExportFileDialog] Renamed file: {_contextMenuTarget} -> {newPath}");

                // Update selection if renamed file was selected
                if (_selectedFileNameInList == Path.GetFileName(_contextMenuTarget))
                {
                    _selectedFileNameInList = _renameNewName;
                    _fileName = Path.GetFileNameWithoutExtension(_renameNewName);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _renameError = $"Error: {ex.Message}";
            Logger.LogError($"[ImGuiExportFileDialog] Failed to rename: {ex.Message}");
            return false;
        }
    }

    private void DrawDeleteConfirmation()
    {
        ImGui.OpenPopup("Delete Confirmation");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Delete Confirmation", ref _showDeleteConfirmation,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        {
            var itemName = Path.GetFileName(_deleteTargetPath);
            var itemType = _deleteTargetIsDirectory ? "folder" : "file";

            ImGui.Text($"Are you sure you want to delete this {itemType}?");
            ImGui.Separator();

            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), itemName);

            if (_deleteTargetIsDirectory)
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f),
                    "Warning: This will delete the folder and all its contents!");

            ImGui.Separator();

            if (ImGui.Button("Delete", new Vector2(100, 0)))
            {
                PerformDelete();
                _showDeleteConfirmation = false;
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(100, 0))) _showDeleteConfirmation = false;

            ImGui.EndPopup();
        }
    }

    private void PerformDelete()
    {
        try
        {
            if (_deleteTargetIsDirectory)
            {
                Directory.Delete(_deleteTargetPath, true); // true = recursive delete
                Logger.Log($"[ImGuiExportFileDialog] Deleted folder: {_deleteTargetPath}");
            }
            else
            {
                File.Delete(_deleteTargetPath);
                Logger.Log($"[ImGuiExportFileDialog] Deleted file: {_deleteTargetPath}");

                // Clear selection if deleted file was selected
                if (_selectedFileNameInList == Path.GetFileName(_deleteTargetPath))
                {
                    _selectedFileNameInList = "";
                    _fileName = "";
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ImGuiExportFileDialog] Failed to delete: {ex.Message}");
        }
    }

    private void DrawCreateFolderPopup()
    {
        ImGui.OpenPopup("Create New Folder");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Create New Folder", ref _showCreateFolderPopup,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        {
            ImGui.Text("Enter folder name:");
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("##NewFolderName", ref _newFolderName, 256)) _createFolderError = "";

            if (!string.IsNullOrEmpty(_createFolderError))
                ImGui.TextColored(new Vector4(1, 0, 0, 1), _createFolderError);

            ImGui.Separator();

            if (ImGui.Button("Create", new Vector2(100, 0)))
                if (CreateNewFolder())
                    _showCreateFolderPopup = false;

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                _showCreateFolderPopup = false;
                _createFolderError = "";
            }

            ImGui.EndPopup();
        }
    }

    private bool CreateNewFolder()
    {
        if (string.IsNullOrWhiteSpace(_newFolderName))
        {
            _createFolderError = "Folder name cannot be empty";
            return false;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        if (_newFolderName.Any(c => invalidChars.Contains(c)))
        {
            _createFolderError = "Folder name contains invalid characters";
            return false;
        }

        var newFolderPath = Path.Combine(CurrentDirectory, _newFolderName);

        if (Directory.Exists(newFolderPath))
        {
            _createFolderError = "A folder with this name already exists";
            return false;
        }

        try
        {
            Directory.CreateDirectory(newFolderPath);
            Logger.Log($"[ImGuiExportFileDialog] Created new folder: {newFolderPath}");

            // Navigate into the new folder and clear selection
            CurrentDirectory = newFolderPath;
            _selectedFileNameInList = "";
            return true;
        }
        catch (Exception ex)
        {
            _createFolderError = $"Error: {ex.Message}";
            Logger.LogError($"[ImGuiExportFileDialog] Failed to create folder: {ex.Message}");
            return false;
        }
    }

    private void DrawBottomControls(ref bool selectionMade)
    {
        ImGui.Separator();

        // Create a child region to ensure consistent layout
        var controlsHeight = ImGui.GetFrameHeightWithSpacing() * 4.5f;

        // File name input row
        ImGui.AlignTextToFramePadding();
        ImGui.Text("File name:");
        ImGui.SameLine();
        var labelWidth = ImGui.CalcTextSize("Save as type:").X + ImGui.GetStyle().ItemSpacing.X * 2;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - labelWidth);
        ImGui.InputText("##FileNameInput", ref _fileName, 260);

        // Extension combo row
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Save as type:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (_extensionOptions.Count > 0)
            if (ImGui.BeginCombo("##ExtensionCombo", _extensionOptions[_selectedExtensionIndex].ToString()))
            {
                for (var i = 0; i < _extensionOptions.Count; i++)
                    if (ImGui.Selectable(_extensionOptions[i].ToString(), _selectedExtensionIndex == i))
                    {
                        _selectedExtensionIndex = i;
                        SelectedExtension = _extensionOptions[i].Extension;
                    }

                ImGui.EndCombo();
            }

        // Warning for file overwrite or empty line for consistent spacing
        var potentialPath = Path.Combine(CurrentDirectory, _fileName + SelectedExtension);
        if (File.Exists(potentialPath))
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Warning: File will be overwritten!");
        else
            ImGui.Dummy(new Vector2(0, ImGui.GetTextLineHeight())); // Maintain consistent spacing

        // Buttons row - properly aligned to the right
        var buttonWidth = 85f;
        var buttonsWidth = buttonWidth * 2 + ImGui.GetStyle().ItemSpacing.X;
        var availWidth = ImGui.GetContentRegionAvail().X;

        ImGui.Dummy(new Vector2(availWidth - buttonsWidth, 0));
        ImGui.SameLine();

        if (ImGui.Button("Export", new Vector2(buttonWidth, 0))) selectionMade = HandleExport();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0))) IsOpen = false;
    }

    private bool HandleExport()
    {
        if (string.IsNullOrWhiteSpace(_fileName))
            return false;

        var cleanFileName = _fileName.EndsWith(SelectedExtension, StringComparison.OrdinalIgnoreCase)
            ? _fileName.Substring(0, _fileName.Length - SelectedExtension.Length)
            : _fileName;

        SelectedPath = Path.Combine(CurrentDirectory, cleanFileName + SelectedExtension);

        Logger.Log($"[ImGuiExportFileDialog] Exporting to: {SelectedPath}");
        IsOpen = false;
        return true;
    }

    private class VolumeInfo
    {
        public string Path { get; set; }
        public string DisplayName { get; set; }
        public string VolumeLabel { get; set; }
        public DriveType DriveType { get; set; }
        public long TotalBytes { get; set; }
        public long AvailableBytes { get; set; }
        public bool IsReady { get; set; }
    }

    public class ExtensionOption
    {
        public ExtensionOption(string extension, string description)
        {
            Extension = extension;
            Description = description;
        }

        public string Extension { get; set; }
        public string Description { get; set; }

        public override string ToString()
        {
            return $"{Description} (*{Extension})";
        }
    }
}