// GeoscientistToolkit/UI/Utils/ImGuiFileDialog.cs
using ImGuiNET;
using System.Numerics;
using GeoscientistToolkit.Util;

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
                
                if (ImGui.Begin(_title + "###" + _id, ref IsOpen, 
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
                {
                    DrawPathNavigation();
                    
                    float bottomBarHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y;
                    Vector2 fileListSize = new Vector2(0, -bottomBarHeight);

                    DrawFileList(ref selectionMade, fileListSize);
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

        private void DrawFileList(ref bool selectionMade, Vector2 size)
        {
            if (ImGui.BeginChild("##FileList", size, ImGuiChildFlags.Border))
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(CurrentDirectory))
                    {
                        var dirName = Path.GetFileName(dir);
                        
                        if (ImGui.Selectable($"[D] {dirName}", false, ImGuiSelectableFlags.AllowDoubleClick))
                        {
                            CurrentDirectory = dir;
                            _selectedFileName = "";
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
            ImGui.Separator();
            
            var style = ImGui.GetStyle();
            Vector2 buttonSize = new Vector2(80, 0);
            float buttonsTotalWidth = (buttonSize.X * 2) + style.ItemSpacing.X;
            float buttonsPosX = ImGui.GetWindowContentRegionMax().X - buttonsTotalWidth;

            if (_dialogType == FileDialogType.OpenFile)
            {
                // FIX: Manually render the label, then calculate the input box width.
                // This prevents the label from overlapping the buttons.
                ImGui.Text("File name");
                ImGui.SameLine();

                float inputTextWidth = buttonsPosX - ImGui.GetCursorPosX() - (style.ItemSpacing.X * 2);
                ImGui.SetNextItemWidth(inputTextWidth);
                ImGui.InputText("##FileNameInput", ref _selectedFileName, 260);
                ImGui.SameLine();
            }
            else // OpenDirectory mode
            {
                // Push the cursor to the right for button alignment.
                ImGui.SetCursorPosX(buttonsPosX);
            }
            
            // Explicitly set cursor position for buttons in both cases to ensure alignment.
            ImGui.SetCursorPosX(buttonsPosX);

            if (ImGui.Button("Cancel", buttonSize))
            {
                IsOpen = false;
                SelectedPath = "";
            }

            ImGui.SameLine();
            
            bool canSelect = _dialogType == FileDialogType.OpenDirectory || !string.IsNullOrEmpty(_selectedFileName);
            if (!canSelect) ImGui.BeginDisabled();
            if (ImGui.Button("Select", buttonSize))
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
                Logger.Log("Opening directory "+CurrentDirectory);
                if (Directory.Exists(SelectedPath))
                {
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
                    Logger.Log("Opening file "+SelectedPath);
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