// GeoscientistToolkit/UI/Utils/ImGuiFileDialog.cs
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI.Utils
{
    public enum FileDialogType { OpenFile, OpenDirectory }

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

        public ImGuiFileDialog(string id, FileDialogType type, string title = null)
        {
            _id = id;
            _dialogType = type;
            _title = title ?? (type == FileDialogType.OpenFile ? "Open File" : "Select Folder");
            CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public void Open(string startingPath = null, string[] allowedExtensions = null)
        {
            IsOpen = true;
            SelectedPath = "";
            _selectedFileName = "";
            _allowedExtensions = allowedExtensions ?? Array.Empty<string>();

            if (!string.IsNullOrEmpty(startingPath) && Directory.Exists(startingPath))
                CurrentDirectory = startingPath;
            else
                CurrentDirectory = Directory.GetCurrentDirectory();
        }

        public bool Submit()
        {
            bool selectionMade = false;
            if (IsOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
                var center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
                
                // Use a regular window instead of a modal
                if (ImGui.Begin(_title + "###" + _id, ref IsOpen, 
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
                {
                    DrawPathNavigation();
                    DrawFileList(ref selectionMade);
                    DrawBottomButtons(ref selectionMade);
                    ImGui.End();
                }
            }
            return selectionMade;
        }

        private void DrawPathNavigation()
        {
            string tempPath = CurrentDirectory;
            if (ImGui.InputText("##Path", ref tempPath, 260))
            {
                if (Directory.Exists(tempPath))
                {
                    CurrentDirectory = tempPath;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Up"))
            {
                var parent = Directory.GetParent(CurrentDirectory);
                if (parent != null)
                {
                    CurrentDirectory = parent.FullName;
                }
            }
            ImGui.Separator();
        }

        private void DrawFileList(ref bool selectionMade)
        {
            if (ImGui.BeginChild("##FileList", new Vector2(-1, -40), ImGuiChildFlags.Border))
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(CurrentDirectory))
                    {
                        var dirName = Path.GetFileName(dir);
                        bool isSelected = _dialogType == FileDialogType.OpenDirectory && _selectedFileName == dirName;
                        
                        // Use [D] for directories
                        if (ImGui.Selectable($"[D] {dirName}", isSelected, ImGuiSelectableFlags.AllowDoubleClick))
                        {
                            if (_dialogType == FileDialogType.OpenDirectory)
                            {
                                // Single click selects the directory
                                _selectedFileName = dirName;
                                
                                // Double click opens the directory
                                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                                {
                                    CurrentDirectory = dir;
                                    _selectedFileName = ""; // Reset file selection when changing directory
                                }
                            }
                            else
                            {
                                // For OpenFile mode, single click opens the directory
                                CurrentDirectory = dir;
                                _selectedFileName = ""; // Reset file selection when changing directory
                            }
                        }
                    }

                    if (_dialogType == FileDialogType.OpenFile)
                    {
                        foreach (var file in Directory.GetFiles(CurrentDirectory))
                        {
                            var fileName = Path.GetFileName(file);
                            bool isAllowed = _allowedExtensions.Length == 0 || 
                                           _allowedExtensions.Contains(Path.GetExtension(file).ToLower());

                            if (!isAllowed) continue;

                            bool isSelected = _selectedFileName == fileName;
                            // Use [F] for files or show the extension
                            string filePrefix = "[F]";
                            string ext = Path.GetExtension(file).ToUpper();
                            if (!string.IsNullOrEmpty(ext) && ext.Length <= 5)
                            {
                                filePrefix = $"[{ext.TrimStart('.')}]";
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
            if (_dialogType == FileDialogType.OpenFile)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.7f);
                ImGui.InputText("File name", ref _selectedFileName, 260);
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                IsOpen = false;
                SelectedPath = "";
            }

            ImGui.SameLine();
            bool canSelect = !string.IsNullOrEmpty(_selectedFileName) || _dialogType == FileDialogType.OpenDirectory;
            if (!canSelect) ImGui.BeginDisabled();
            if (ImGui.Button("Select"))
            {
                selectionMade = HandleSelection();
            }
            if (!canSelect) ImGui.EndDisabled();
        }
        
        private bool HandleSelection()
        {
            if (_dialogType == FileDialogType.OpenDirectory)
            {
                SelectedPath = CurrentDirectory;
                IsOpen = false;
                return true;
            }
            else // OpenFile
            {
                if (!string.IsNullOrEmpty(_selectedFileName))
                {
                    SelectedPath = Path.Combine(CurrentDirectory, _selectedFileName);
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