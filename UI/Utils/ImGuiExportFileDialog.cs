// GeoscientistToolkit/UI/Utils/ImGuiExportFileDialog.cs
using ImGuiNET;
using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.UI.Utils
{
    public class ImGuiExportFileDialog
    {
        public bool IsOpen;
        public string SelectedPath { get; private set; } = "";
        public string CurrentDirectory { get; private set; }
        public string SelectedExtension { get; private set; } = "";

        private readonly string _id;
        private readonly string _title;
        private string _fileName = "";
        private string _selectedFileName = "";
        private List<ExtensionOption> _extensionOptions = new List<ExtensionOption>();
        private int _selectedExtensionIndex = 0;

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
        }

        /// <summary>
        /// Sets the available file extensions for export
        /// </summary>
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

        /// <summary>
        /// Convenience method to set extensions with just string pairs
        /// </summary>
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
            _selectedFileName = "";

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
                ImGui.SetNextWindowSize(new Vector2(700, 450), ImGuiCond.FirstUseEver);
                var center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
                
                if (ImGui.Begin(_title + "###" + _id, ref IsOpen, 
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
                {
                    DrawPathNavigation();
                    
                    // Calculate the height for the file list
                    float bottomBarHeight = (ImGui.GetFrameHeightWithSpacing() * 3) + (ImGui.GetStyle().ItemSpacing.Y * 2) + ImGui.GetStyle().WindowPadding.Y;
                    Vector2 fileListSize = new Vector2(0, -bottomBarHeight);

                    DrawFileList(fileListSize);
                    DrawBottomControls(ref selectionMade);
                    ImGui.End();
                }
            }
            return selectionMade;
        }

        private void DrawPathNavigation()
        {
            string tempPath = CurrentDirectory;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 50);
            if (ImGui.InputText("##Path", ref tempPath, 260))
            {
                if (Directory.Exists(tempPath))
                {
                    CurrentDirectory = tempPath;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Up", new Vector2(40, 0)))
            {
                var parent = Directory.GetParent(CurrentDirectory);
                if (parent != null)
                {
                    CurrentDirectory = parent.FullName;
                }
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
                        
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.5f, 1.0f)); // Yellow for directories
                        if (ImGui.Selectable($"[D] {dirName}", false))
                        {
                            CurrentDirectory = dir;
                            _selectedFileName = "";
                        }
                        ImGui.PopStyleColor();
                    }

                    // Draw files (to show existing files that might be overwritten)
                    foreach (var file in Directory.GetFiles(CurrentDirectory))
                    {
                        var fileName = Path.GetFileName(file);
                        bool isSelected = _selectedFileName == fileName;
                        
                        if (ImGui.Selectable($"[F] {fileName}", isSelected))
                        {
                            _selectedFileName = fileName;
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
            
            var style = ImGui.GetStyle();
            
            // File name input
            ImGui.AlignTextToFramePadding();
            ImGui.Text("File name:");
            ImGui.SameLine();
            float inputWidth = ImGui.GetContentRegionAvail().X * 0.6f;
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##FileNameInput", ref _fileName, 260);
            
            // Extension dropdown
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Save as type:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
            
            if (_extensionOptions.Count > 0)
            {
                if (ImGui.BeginCombo("##ExtensionCombo", _extensionOptions[_selectedExtensionIndex].ToString()))
                {
                    for (int i = 0; i < _extensionOptions.Count; i++)
                    {
                        bool isSelected = (_selectedExtensionIndex == i);
                        if (ImGui.Selectable(_extensionOptions[i].ToString(), isSelected))
                        {
                            _selectedExtensionIndex = i;
                            SelectedExtension = _extensionOptions[i].Extension;
                        }
                        
                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            
            // Buttons
            ImGui.Spacing();
            Vector2 buttonSize = new Vector2(80, 0);
            float buttonsTotalWidth = (buttonSize.X * 2) + style.ItemSpacing.X;
            float buttonsPosX = ImGui.GetWindowContentRegionMax().X - buttonsTotalWidth;
            
            ImGui.SetCursorPosX(buttonsPosX);
            
            if (ImGui.Button("Cancel", buttonSize))
            {
                IsOpen = false;
                SelectedPath = "";
            }

            ImGui.SameLine();
            
            bool canExport = !string.IsNullOrWhiteSpace(_fileName);
            if (!canExport) ImGui.BeginDisabled();
            if (ImGui.Button("Export", buttonSize))
            {
                selectionMade = HandleExport();
            }
            if (!canExport) ImGui.EndDisabled();
            
            // Show warning if file exists
            if (!string.IsNullOrWhiteSpace(_fileName) && _extensionOptions.Count > 0)
            {
                string potentialPath = Path.Combine(CurrentDirectory, _fileName + SelectedExtension);
                if (File.Exists(potentialPath))
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Warning: File already exists and will be overwritten!");
                }
            }
        }
        
        private bool HandleExport()
        {
            if (string.IsNullOrWhiteSpace(_fileName))
                return false;
                
            // Ensure the filename doesn't already have the extension
            string cleanFileName = _fileName;
            if (!string.IsNullOrEmpty(SelectedExtension) && cleanFileName.EndsWith(SelectedExtension))
            {
                cleanFileName = cleanFileName.Substring(0, cleanFileName.Length - SelectedExtension.Length);
            }
            
            // Build the full path
            SelectedPath = Path.Combine(CurrentDirectory, cleanFileName + SelectedExtension);
            
            // Check if we need to confirm overwrite
            if (File.Exists(SelectedPath))
            {
                // In a real implementation, you might want to show a confirmation dialog
                // For now, we'll just proceed
                Logger.Log($"Overwriting existing file: {SelectedPath}");
            }
            
            Logger.Log($"Exporting to: {SelectedPath}");
            IsOpen = false;
            return true;
        }
    }
}