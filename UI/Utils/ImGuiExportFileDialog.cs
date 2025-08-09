// GeoscientistToolkit/UI/Utils/ImGuiExportFileDialog.cs
using ImGuiNET;
using System.Numerics;
using GeoscientistToolkit.Util;
using System.Collections.Generic;
using System.IO;
using System;
using System.Runtime.InteropServices;
using System.Linq;

namespace GeoscientistToolkit.UI.Utils
{
    // ADDED ENUM
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
        public bool IsOpen;
        public string SelectedPath { get; private set; } = "";
        public string CurrentDirectory { get; private set; }
        public string SelectedExtension { get; private set; } = "";

        private readonly string _id;
        private readonly string _title;
        private string _fileName = "";
        private string _selectedFileNameInList = "";
        private List<ExtensionOption> _extensionOptions = new List<ExtensionOption>();
        private int _selectedExtensionIndex = 0;
        private List<VolumeInfo> _availableVolumes = new List<VolumeInfo>();
        private float _drivePanelWidth = 180f;
        
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

        public class ExtensionOption
        {
            public string Extension { get; set; }
            public string Description { get; set; }

            public ExtensionOption(string extension, string description)
            {
                Extension = extension;
                Description = description;
            }

            public override string ToString() => $"{Description} (*{Extension})";
        }

        public ImGuiExportFileDialog(string id, string title = "Export File")
        {
            _id = id;
            _title = title;
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
            foreach (var (ext, desc) in options)
            {
                _extensionOptions.Add(new ExtensionOption(ext, desc));
            }
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
            RefreshDrives();
        }

        public bool Submit()
        {
            bool selectionMade = false;
            if (IsOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(900, 550), ImGuiCond.FirstUseEver);
                var center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

                if (ImGui.Begin(_title + "###" + _id, ref IsOpen,
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
                {
                    DrawPathNavigation();

                    float bottomBarHeight = ImGui.GetFrameHeightWithSpacing() * 2.5f + ImGui.GetStyle().ItemSpacing.Y * 3;
                    float contentHeight = ImGui.GetContentRegionAvail().Y - bottomBarHeight;

                    // Draw drive panel and file list side by side
                    if (ImGui.BeginTable("##ExportFileDialogLayout", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable))
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

                    DrawBottomControls(ref selectionMade);
                    ImGui.End();
                }
                if (_showCreateFolderPopup)
                {
                    DrawCreateFolderPopup();
                }
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
                            _selectedFileNameInList = "";
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
                        _showCreateFolderPopup = false;
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
        private void DrawFileList(Vector2 size)
        {
            if (ImGui.BeginChild("##FileList", size, ImGuiChildFlags.Border))
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(CurrentDirectory))
                    {
                        var dirName = Path.GetFileName(dir);
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.5f, 1.0f));
                        if (ImGui.Selectable($"[D] {dirName}", false, ImGuiSelectableFlags.AllowDoubleClick))
                        {
                            if (ImGui.IsMouseDoubleClicked(0))
                            {
                                CurrentDirectory = dir;
                                _selectedFileNameInList = "";
                            }
                        }
                        ImGui.PopStyleColor();
                    }

                    foreach (var file in Directory.GetFiles(CurrentDirectory))
                    {
                        var fileName = Path.GetFileName(file);
                        bool isSelected = _selectedFileNameInList == fileName;

                        if (ImGui.Selectable($"[F] {fileName}", isSelected))
                        {
                            _selectedFileNameInList = fileName;
                            _fileName = Path.GetFileNameWithoutExtension(fileName);
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

        private void DrawBottomControls(ref bool selectionMade)
        {
            ImGui.Separator();

            ImGui.Text("File name:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 120);
            ImGui.InputText("##FileNameInput", ref _fileName, 260);

            ImGui.Text("Save as type:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 120);
            if (_extensionOptions.Count > 0)
            {
                if (ImGui.BeginCombo("##ExtensionCombo", _extensionOptions[_selectedExtensionIndex].ToString()))
                {
                    for (int i = 0; i < _extensionOptions.Count; i++)
                    {
                        if (ImGui.Selectable(_extensionOptions[i].ToString(), _selectedExtensionIndex == i))
                        {
                            _selectedExtensionIndex = i;
                            SelectedExtension = _extensionOptions[i].Extension;
                        }
                    }
                    ImGui.EndCombo();
                }
            }

            ImGui.Spacing();
            float buttonPosX = ImGui.GetContentRegionAvail().X - 170;
            ImGui.SetCursorPosX(buttonPosX);

            if (ImGui.Button("Export", new Vector2(80, 0)))
            {
                selectionMade = HandleExport();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(80, 0)))
            {
                IsOpen = false;
            }

            string potentialPath = Path.Combine(CurrentDirectory, _fileName + SelectedExtension);
            if (File.Exists(potentialPath))
            {
                ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Warning: File will be overwritten!");
            }
        }

        private bool HandleExport()
        {
            if (string.IsNullOrWhiteSpace(_fileName))
                return false;

            string cleanFileName = _fileName.EndsWith(SelectedExtension, StringComparison.OrdinalIgnoreCase)
                ? _fileName.Substring(0, _fileName.Length - SelectedExtension.Length)
                : _fileName;

            SelectedPath = Path.Combine(CurrentDirectory, cleanFileName + SelectedExtension);

            Logger.Log($"[ImGuiExportFileDialog] Exporting to: {SelectedPath}");
            IsOpen = false;
            return true;
        }
    }
}