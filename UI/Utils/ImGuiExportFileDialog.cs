// GeoscientistToolkit/UI/Utils/ImGuiExportFileDialog.cs
using ImGuiNET;
using System.Numerics;
using GeoscientistToolkit.Util;
using System.Collections.Generic;
using System.IO;
using System;

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
                    
                    float bottomBarHeight = ImGui.GetFrameHeightWithSpacing() * 2.5f + ImGui.GetStyle().ItemSpacing.Y * 3;
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