// GAIA/UI/Utils/ImGuiFileDialog.cs

using System.Numerics;
using System.Runtime.InteropServices;
using GAIA.Util;
using ImGuiNET;

namespace GAIA.UI.Utils;

public class ImGuiFileDialog
{
    private readonly FileDialogType _dialogType;
    private readonly float _drivePanelWidth = 180f;
    private readonly string _id;
    private readonly string _title;
    private string[] _allowedExtensions = Array.Empty<string>();
    private string _createFolderError = "";
    private string _defaultExtension = "";
    private string _newFolderName = "";
    private string _selectedFileName = "";
    private string _cachedListDirectory;
    private (string Path, bool IsDirectory)[] _cachedEntries = Array.Empty<(string, bool)>();
    private bool _fileListDirty = true;

    // New folder creation fields
    private bool _showCreateFolderPopup;
    public bool IsOpen;

    public ImGuiFileDialog(string id, FileDialogType type, string title = null)
    {
        _id = id;
        _dialogType = type;
        _title = title ?? type switch
        {
            FileDialogType.OpenFile => "Open File",
            FileDialogType.SaveFile => "Save File",
            FileDialogType.OpenDirectory => "Select Folder",
            _ => "File Dialog"
        };
        CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public string SelectedPath { get; private set; } = "";
    public string CurrentDirectory { get; private set; }

    public void Open(string startingPath = null, string[] allowedExtensions = null, string defaultFileName = null)
    {
        IsOpen = true;
        SelectedPath = "";
        _selectedFileName = defaultFileName ?? "";
        _allowedExtensions = allowedExtensions ?? Array.Empty<string>();
        _newFolderName = "";
        _createFolderError = "";
        _showCreateFolderPopup = false;

        // Set default extension for save dialog
        if (_dialogType == FileDialogType.SaveFile && _allowedExtensions.Length > 0)
            _defaultExtension = _allowedExtensions[0];

        if (!string.IsNullOrEmpty(startingPath))
        {
            if (File.Exists(startingPath))
            {
                CurrentDirectory = Path.GetDirectoryName(startingPath);
                _selectedFileName = Path.GetFileName(startingPath);
            }
            else if (Directory.Exists(startingPath))
            {
                CurrentDirectory = startingPath;
            }
            else
            {
                CurrentDirectory = Directory.GetCurrentDirectory();
            }
        }
        else
        {
            CurrentDirectory = Directory.GetCurrentDirectory();
        }

        FileDialogVolumes.Refresh();
        _fileListDirty = true;
    }

    private void ChangeDirectory(string path)
    {
        if (string.Equals(CurrentDirectory, path, StringComparison.Ordinal)) return;
        CurrentDirectory = path;
        _fileListDirty = true;
    }

    private void EnsureFileListCache()
    {
        if (!_fileListDirty && string.Equals(_cachedListDirectory, CurrentDirectory, StringComparison.Ordinal)) return;
        var directories = Directory.EnumerateDirectories(CurrentDirectory)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => (path, true));
        IEnumerable<(string Path, bool IsDirectory)> files = Array.Empty<(string, bool)>();
        if (_dialogType != FileDialogType.OpenDirectory)
            files = Directory.EnumerateFiles(CurrentDirectory)
                .Where(path => _dialogType != FileDialogType.OpenFile || _allowedExtensions.Length == 0 ||
                               _allowedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => (path, false));
        _cachedEntries = directories.Concat(files).ToArray();
        _cachedListDirectory = CurrentDirectory;
        _fileListDirty = false;
    }

    public bool Submit()
    {
        var selectionMade = false;
        if (IsOpen)
        {
            ImGui.SetNextWindowSize(new Vector2(800, 500), ImGuiCond.FirstUseEver);
            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

            if (ImGui.Begin(_title + "###" + _id, ref IsOpen,
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
            {
                DrawPathNavigation();

                var bottomBarHeight = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().WindowPadding.Y;
                var contentHeight = ImGui.GetContentRegionAvail().Y - bottomBarHeight;

                // Draw drive panel and file list side by side
                if (ImGui.BeginTable("##FileDialogLayout", 2,
                        ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable))
                {
                    ImGui.TableSetupColumn("##DrivePanel", ImGuiTableColumnFlags.WidthFixed, _drivePanelWidth);
                    ImGui.TableSetupColumn("##FileList", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    DrawDrivePanel(new Vector2(0, contentHeight));

                    ImGui.TableNextColumn();
                    DrawFileList(ref selectionMade, new Vector2(0, contentHeight));

                    ImGui.EndTable();
                }

                DrawBottomButtons(ref selectionMade);
                ImGui.End();
            }

            // Handle create folder popup
            if (_showCreateFolderPopup) DrawCreateFolderPopup();
        }

        return selectionMade;
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
                ChangeDirectory(tempPath);

        ImGui.SameLine();
        if (ImGui.Button("Up", new Vector2(upButtonWidth, 0)))
        {
            var parent = Directory.GetParent(CurrentDirectory);
            if (parent != null)
                ChangeDirectory(parent.FullName);
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


    private void DrawDrivePanel(Vector2 size)
    {
        if (ImGui.BeginChild("##DrivePanel", size, ImGuiChildFlags.None))
        {
            ImGui.Text("Drives");
            ImGui.Separator();

            foreach (var volume in FileDialogVolumes.Current)
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
                        ChangeDirectory(volume.Path);
                        // Don't clear filename for save dialog
                        if (_dialogType != FileDialogType.SaveFile) _selectedFileName = "";
                    }

                if (isCurrentDrive)
                    ImGui.PopStyleColor();

                // Show space info if available
                if (volume.Probing)
                {
                    ImGui.TextDisabled("Checking...");
                }
                else if (volume.IsReady && volume.TotalBytes > 0)
                {
                    var ratio = 1.0f - (float)volume.AvailableBytes / volume.TotalBytes;
                    ImGui.ProgressBar(ratio, new Vector2(-1, 4), "");

                    ImGui.Text($"{FileDialogVolumes.FormatBytes(volume.AvailableBytes)} free");
                    ImGui.Text($"of {FileDialogVolumes.FormatBytes(volume.TotalBytes)}");
                }
                else if (!volume.IsReady)
                {
                    ImGui.TextDisabled("Not ready");
                }

                ImGui.Spacing();
            }

            // Refresh button
            ImGui.Separator();
            if (ImGui.Button("Refresh", new Vector2(-1, 0)))
            {
                FileDialogVolumes.Refresh(true);
                _fileListDirty = true;
            }

            ImGui.EndChild();
        }
    }

    private unsafe void DrawFileList(ref bool selectionMade, Vector2 size)
    {
        if (ImGui.BeginChild("##FileList", size, ImGuiChildFlags.Border))
        {
            try
            {
                EnsureFileListCache();
                var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                clipper.Begin(_cachedEntries.Length);
                while (clipper.Step())
                for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                {
                    var entry = _cachedEntries[index];
                    var fileName = Path.GetFileName(entry.Path);
                    if (entry.IsDirectory)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.5f, 1.0f));
                        if (ImGui.Selectable($"[D] {fileName}", false, ImGuiSelectableFlags.AllowDoubleClick) &&
                            ImGui.IsMouseDoubleClicked(0))
                        {
                            ChangeDirectory(entry.Path);
                            if (_dialogType != FileDialogType.SaveFile) _selectedFileName = "";
                        }
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        var isAllowed = _allowedExtensions.Length == 0 ||
                                        _allowedExtensions.Contains(Path.GetExtension(entry.Path).ToLowerInvariant());
                        var isSelected = _selectedFileName == fileName;
                        var filePrefix = "[F]";
                        var ext = Path.GetExtension(entry.Path).ToUpperInvariant();
                        if (!string.IsNullOrEmpty(ext) && ext.Length <= 5) filePrefix = $"[{ext.TrimStart('.')}]";
                        if (_dialogType == FileDialogType.SaveFile && !isAllowed)
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
                        if (ImGui.Selectable($"{filePrefix} {fileName}", isSelected,
                                ImGuiSelectableFlags.AllowDoubleClick))
                        {
                            _selectedFileName = fileName;
                            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                                if (HandleSelection())
                                    selectionMade = true;
                        }

                        if (_dialogType == FileDialogType.SaveFile && !isAllowed) ImGui.PopStyleColor();
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

    private void DrawBottomButtons(ref bool selectionMade)
    {
        ImGui.Separator();

        // Show file type filter for save/open file dialogs
        if (_dialogType != FileDialogType.OpenDirectory && _allowedExtensions.Length > 0)
        {
            var filterText = "File type: ";
            if (_allowedExtensions.Length == 1)
                filterText += $"*{_allowedExtensions[0]}";
            else
                filterText += string.Join(", ", _allowedExtensions.Select(e => $"*{e}"));
            ImGui.Text(filterText);
        }

        var style = ImGui.GetStyle();
        var buttonSize = new Vector2(80, 0);
        var buttonsTotalWidth = buttonSize.X * 2 + style.ItemSpacing.X;
        var buttonsPosX = ImGui.GetWindowContentRegionMax().X - buttonsTotalWidth;

        if (_dialogType != FileDialogType.OpenDirectory)
        {
            ImGui.Text("File name:");
            ImGui.SameLine();

            var inputTextWidth = buttonsPosX - ImGui.GetCursorPosX() - style.ItemSpacing.X * 2;
            ImGui.SetNextItemWidth(inputTextWidth);

            if (ImGui.InputText("##FileNameInput", ref _selectedFileName, 260))
            {
                // User is typing
            }

            ImGui.SameLine();
        }
        else
        {
            ImGui.Text($"Current folder: {Path.GetFileName(CurrentDirectory)}");
            ImGui.SetCursorPosX(buttonsPosX);
        }

        ImGui.SetCursorPosX(buttonsPosX);

        if (ImGui.Button("Cancel", buttonSize))
        {
            IsOpen = false;
            SelectedPath = "";
        }

        ImGui.SameLine();

        var buttonText = _dialogType == FileDialogType.SaveFile ? "Save" : "Select";
        var canSelect = _dialogType == FileDialogType.OpenDirectory || !string.IsNullOrEmpty(_selectedFileName);

        if (!canSelect) ImGui.BeginDisabled();
        if (ImGui.Button(buttonText, buttonSize)) selectionMade = HandleSelection();
        if (!canSelect) ImGui.EndDisabled();
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

        // Check for invalid characters
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
            Logger.Log($"[ImGuiFileDialog] Created new folder: {newFolderPath}");

            // Navigate to the new folder
            ChangeDirectory(newFolderPath);
            _selectedFileName = "";

            return true;
        }
        catch (Exception ex)
        {
            _createFolderError = $"Error: {ex.Message}";
            Logger.LogError($"[ImGuiFileDialog] Failed to create folder: {ex.Message}");
            return false;
        }
    }

    private bool HandleSelection()
    {
        if (_dialogType == FileDialogType.OpenDirectory)
        {
            SelectedPath = CurrentDirectory;
            Logger.Log($"Selected directory: {CurrentDirectory}");
            if (Directory.Exists(SelectedPath))
            {
                IsOpen = false;
                return true;
            }

            return false;
        }

        if (_dialogType == FileDialogType.SaveFile)
        {
            if (!string.IsNullOrEmpty(_selectedFileName))
            {
                // Ensure the filename has the correct extension
                var fileName = _selectedFileName;
                if (!string.IsNullOrEmpty(_defaultExtension))
                {
                    var currentExt = Path.GetExtension(fileName).ToLower();
                    var hasValidExt = _allowedExtensions.Contains(currentExt);

                    if (!hasValidExt)
                        // Add the default extension if no valid extension present
                        fileName = Path.GetFileNameWithoutExtension(fileName) + _defaultExtension;
                }

                SelectedPath = Path.Combine(CurrentDirectory, fileName);
                Logger.Log($"Saving file: {SelectedPath}");

                // Check if file exists and prompt for overwrite
                if (File.Exists(SelectedPath)) Logger.Log($"File will be overwritten: {SelectedPath}");

                IsOpen = false;
                return true;
            }

            return false;
        }

        // OpenFile
        if (!string.IsNullOrEmpty(_selectedFileName))
        {
            SelectedPath = Path.Combine(CurrentDirectory, _selectedFileName);
            Logger.Log($"Opening file: {SelectedPath}");
            if (File.Exists(SelectedPath))
            {
                IsOpen = false;
                return true;
            }
        }

        return false;
    }
}
