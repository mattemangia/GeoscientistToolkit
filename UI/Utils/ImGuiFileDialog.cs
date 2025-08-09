// GeoscientistToolkit/UI/Utils/ImGuiFileDialog.cs
using ImGuiNET;
using System.Numerics;
using GeoscientistToolkit.Util;
using System.Runtime.InteropServices;
using System.Linq;

namespace GeoscientistToolkit.UI.Utils
{
    public class ImGuiFileDialog
    {
        public bool IsOpen;
        public string SelectedPath { get; private set; } = "";
        public string CurrentDirectory { get; private set; }

        private readonly FileDialogType _dialogType;
        private readonly string _id;
        private readonly string _title;
        private string _selectedFileName = "";
        private string[] _allowedExtensions = Array.Empty<string>();
        private List<VolumeInfo> _availableVolumes = new List<VolumeInfo>();
        private float _drivePanelWidth = 180f;
        private string _defaultExtension = "";

        // New folder creation fields
        private bool _showCreateFolderPopup = false;
        private string _newFolderName = "";
        private string _createFolderError = "";

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
            RefreshDrives();
        }

        private void RefreshDrives()
        {
            _availableVolumes.Clear();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: Use DriveInfo
                foreach (var drive in DriveInfo.GetDrives())
                {
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
                    catch { }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS: Common mount points
                AddUnixVolume("/", "Root");
                AddUnixVolume(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Home");

                // Check for mounted volumes
                if (Directory.Exists("/Volumes"))
                {
                    foreach (var dir in Directory.GetDirectories("/Volumes"))
                    {
                        AddUnixVolume(dir, Path.GetFileName(dir));
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: Common mount points
                AddUnixVolume("/", "Root");
                AddUnixVolume(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Home");

                // Check common mount directories
                string[] mountDirs = { "/media", "/mnt", "/run/media" };
                foreach (var mountDir in mountDirs)
                {
                    if (Directory.Exists(mountDir))
                    {
                        try
                        {
                            foreach (var dir in Directory.GetDirectories(mountDir))
                            {
                                AddUnixVolume(dir, Path.GetFileName(dir));
                            }
                        }
                        catch { }
                    }
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

                // Try to get disk space info (may not work on all systems)
                try
                {
                    var driveInfo = new DriveInfo(path);
                    if (driveInfo.IsReady)
                    {
                        volume.TotalBytes = driveInfo.TotalSize;
                        volume.AvailableBytes = driveInfo.AvailableFreeSpace;
                    }
                }
                catch { }

                _availableVolumes.Add(volume);
            }
            catch { }
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
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

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
            {
                _defaultExtension = _allowedExtensions[0];
            }

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

            RefreshDrives();
        }

        public bool Submit()
        {
            bool selectionMade = false;
            if (IsOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(800, 500), ImGuiCond.FirstUseEver);
                var center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

                if (ImGui.Begin(_title + "###" + _id, ref IsOpen,
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
                {
                    DrawPathNavigation();

                    float bottomBarHeight = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().WindowPadding.Y;
                    float contentHeight = ImGui.GetContentRegionAvail().Y - bottomBarHeight;

                    // Draw drive panel and file list side by side
                    if (ImGui.BeginTable("##FileDialogLayout", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable))
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
                if (_showCreateFolderPopup)
                {
                    DrawCreateFolderPopup();
                }
            }
            return selectionMade;
        }

        private void DrawPathNavigation()
        {
            string tempPath = CurrentDirectory;
            float upButtonWidth = 40f;
            float newFolderButtonWidth = 100f;

            float totalButtons = upButtonWidth + newFolderButtonWidth + ImGui.GetStyle().ItemSpacing.X * 2;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - totalButtons);

            if (ImGui.InputText("##Path", ref tempPath, 260))
            {
                if (Directory.Exists(tempPath))
                    CurrentDirectory = tempPath;
            }

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


        private void DrawDrivePanel(Vector2 size)
        {
            if (ImGui.BeginChild("##DrivePanel", size, ImGuiChildFlags.None))
            {
                ImGui.Text("Drives");
                ImGui.Separator();

                foreach (var volume in _availableVolumes)
                {
                    bool isCurrentDrive = CurrentDirectory.StartsWith(volume.Path,
                        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                        StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

                    if (isCurrentDrive)
                        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);

                    string buttonLabel = volume.DisplayName;
                    if (!string.IsNullOrEmpty(volume.VolumeLabel) && volume.VolumeLabel != volume.DisplayName)
                    {
                        buttonLabel = $"{volume.DisplayName} ({volume.VolumeLabel})";
                    }

                    if (ImGui.Button($"{buttonLabel}###{volume.Path}", new Vector2(-1, 0)))
                    {
                        if (volume.IsReady && Directory.Exists(volume.Path))
                        {
                            CurrentDirectory = volume.Path;
                            // Don't clear filename for save dialog
                            if (_dialogType != FileDialogType.SaveFile)
                            {
                                _selectedFileName = "";
                            }
                        }
                    }

                    if (isCurrentDrive)
                        ImGui.PopStyleColor();

                    // Show space info if available
                    if (volume.IsReady && volume.TotalBytes > 0)
                    {
                        float ratio = 1.0f - ((float)volume.AvailableBytes / volume.TotalBytes);
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
                if (ImGui.Button("Refresh Drives", new Vector2(-1, 0)))
                {
                    RefreshDrives();
                }

                ImGui.EndChild();
            }
        }

        private void DrawFileList(ref bool selectionMade, Vector2 size)
        {
            if (ImGui.BeginChild("##FileList", size, ImGuiChildFlags.Border))
            {
                try
                {
                    var directories = Directory.GetDirectories(CurrentDirectory).OrderBy(d => Path.GetFileName(d));
                    foreach (var dir in directories)
                    {
                        var dirName = Path.GetFileName(dir);

                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.5f, 1.0f));
                        if (ImGui.Selectable($"[D] {dirName}", false, ImGuiSelectableFlags.AllowDoubleClick))
                        {
                            if (ImGui.IsMouseDoubleClicked(0))
                            {
                                CurrentDirectory = dir;
                                // Don't clear filename for save dialog
                                if (_dialogType != FileDialogType.SaveFile)
                                {
                                    _selectedFileName = "";
                                }
                            }
                        }
                        ImGui.PopStyleColor();
                    }

                    if (_dialogType != FileDialogType.OpenDirectory)
                    {
                        var files = Directory.GetFiles(CurrentDirectory).OrderBy(f => Path.GetFileName(f));
                        foreach (var file in files)
                        {
                            var fileName = Path.GetFileName(file);

                            // For save dialog, show all files but highlight those with matching extensions
                            bool isAllowed = _allowedExtensions.Length == 0 ||
                                           _allowedExtensions.Contains(Path.GetExtension(file).ToLower());

                            // For open dialog, filter; for save dialog, show all
                            if (_dialogType == FileDialogType.OpenFile && !isAllowed) continue;

                            bool isSelected = _selectedFileName == fileName;
                            string filePrefix = "[F]";
                            string ext = Path.GetExtension(file).ToUpper();
                            if (!string.IsNullOrEmpty(ext) && ext.Length <= 5)
                            {
                                filePrefix = $"[{ext.TrimStart('.')}]";
                            }

                            // Dim non-matching files in save dialog
                            if (_dialogType == FileDialogType.SaveFile && !isAllowed)
                            {
                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
                            }

                            if (ImGui.Selectable($"{filePrefix} {fileName}", isSelected, ImGuiSelectableFlags.AllowDoubleClick))
                            {
                                _selectedFileName = fileName;
                                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                                {
                                    if (HandleSelection())
                                    {
                                        selectionMade = true;
                                    }
                                }
                            }

                            if (_dialogType == FileDialogType.SaveFile && !isAllowed)
                            {
                                ImGui.PopStyleColor();
                            }
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
                string filterText = "File type: ";
                if (_allowedExtensions.Length == 1)
                {
                    filterText += $"*{_allowedExtensions[0]}";
                }
                else
                {
                    filterText += string.Join(", ", _allowedExtensions.Select(e => $"*{e}"));
                }
                ImGui.Text(filterText);
            }

            var style = ImGui.GetStyle();
            Vector2 buttonSize = new Vector2(80, 0);
            float buttonsTotalWidth = (buttonSize.X * 2) + style.ItemSpacing.X;
            float buttonsPosX = ImGui.GetWindowContentRegionMax().X - buttonsTotalWidth;

            if (_dialogType != FileDialogType.OpenDirectory)
            {
                ImGui.Text("File name:");
                ImGui.SameLine();

                float inputTextWidth = buttonsPosX - ImGui.GetCursorPosX() - (style.ItemSpacing.X * 2);
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

            string buttonText = _dialogType == FileDialogType.SaveFile ? "Save" : "Select";
            bool canSelect = _dialogType == FileDialogType.OpenDirectory || !string.IsNullOrEmpty(_selectedFileName);

            if (!canSelect) ImGui.BeginDisabled();
            if (ImGui.Button(buttonText, buttonSize))
            {
                selectionMade = HandleSelection();
            }
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
                if (ImGui.InputText("##NewFolderName", ref _newFolderName, 256))
                {
                    _createFolderError = "";
                }

                if (!string.IsNullOrEmpty(_createFolderError))
                {
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), _createFolderError);
                }

                ImGui.Separator();

                if (ImGui.Button("Create", new Vector2(100, 0)))
                {
                    if (CreateNewFolder())
                    {
                        _showCreateFolderPopup = false;
                    }
                }

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
            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (_newFolderName.Any(c => invalidChars.Contains(c)))
            {
                _createFolderError = "Folder name contains invalid characters";
                return false;
            }

            string newFolderPath = Path.Combine(CurrentDirectory, _newFolderName);

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
                CurrentDirectory = newFolderPath;
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
            else if (_dialogType == FileDialogType.SaveFile)
            {
                if (!string.IsNullOrEmpty(_selectedFileName))
                {
                    // Ensure the filename has the correct extension
                    string fileName = _selectedFileName;
                    if (!string.IsNullOrEmpty(_defaultExtension))
                    {
                        string currentExt = Path.GetExtension(fileName).ToLower();
                        bool hasValidExt = _allowedExtensions.Contains(currentExt);

                        if (!hasValidExt)
                        {
                            // Add the default extension if no valid extension present
                            fileName = Path.GetFileNameWithoutExtension(fileName) + _defaultExtension;
                        }
                    }

                    SelectedPath = Path.Combine(CurrentDirectory, fileName);
                    Logger.Log($"Saving file: {SelectedPath}");

                    // Check if file exists and prompt for overwrite
                    if (File.Exists(SelectedPath))
                    {
                       
                        Logger.Log($"File will be overwritten: {SelectedPath}");
                    }

                    IsOpen = false;
                    return true;
                }
                return false;
            }
            else // OpenFile
            {
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
    }
}