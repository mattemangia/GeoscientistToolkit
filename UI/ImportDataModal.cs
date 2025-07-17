// GeoscientistToolkit/UI/ImportDataModal.cs
// This is the corrected version that changes the IsOpen property to a field
// to make it compatible with ImGui's 'ref' parameter usage.

using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;
using System.IO;


namespace GeoscientistToolkit.UI
{
    public class ImportDataModal
    {
        //
        
        // Change the auto-property to a public field.
        //
        public bool IsOpen; // Was: public bool IsOpen { get; private set; }

        private DatasetType _selectedType = DatasetType.CtImageStack;
        private int _binningSize = 1;
        private bool _loadInMemory = true;
        private float _pixelSize = 1.0f;
        private int _pixelUnit = 0; // 0 for micrometers, 1 for millimeters
        private string _filePath = "";

        public void Open()
        {
            IsOpen = true;
            // Reset fields when opening
            _selectedType = DatasetType.CtImageStack;
            _binningSize = 1;
            _loadInMemory = true;
            _pixelSize = 1.0f;
            _pixelUnit = 0;
            _filePath = "";
            ImGui.OpenPopup("Import Data");
        }

        public void Submit()
        {
            Vector2 center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            
            // Now that 'IsOpen' is a field, it can be passed by reference without error.
            if (ImGui.BeginPopupModal("Import Data", ref IsOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Select Dataset Type:");
                if (ImGui.BeginCombo("##DatasetType", _selectedType.ToString()))
                {
                    foreach (DatasetType type in Enum.GetValues(typeof(DatasetType)))
                    {
                        bool isSelected = (_selectedType == type);
                        if (ImGui.Selectable(type.ToString(), isSelected))
                        {
                            _selectedType = type;
                        }
                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                    ImGui.EndCombo();
                }

                ImGui.Separator();

                switch (_selectedType)
                {
                    case DatasetType.CtImageStack:
                        RenderCtImageStackOptions();
                        break;
                    case DatasetType.CtBinaryFile:
                        RenderCtBinaryOptions();
                        break;
                    // Add cases for other dataset types here
                    default:
                        RenderGenericFileOptions();
                        break;
                }

                ImGui.Separator();

                if (ImGui.Button("Import") && !string.IsNullOrEmpty(_filePath))
                {
                    string datasetName = Path.GetFileNameWithoutExtension(_filePath);
                    
                    var newDataset = new CtImageStackDataset(datasetName, _filePath)
                    {
                        BinningSize = _binningSize,
                        LoadFullInMemory = _loadInMemory,
                        PixelSize = _pixelSize,
                        Unit = _pixelUnit == 0 ? "micrometers" : "millimeters"
                    };
                    ProjectManager.Instance.AddDataset(newDataset);
                    IsOpen = false; // This still works fine.
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    IsOpen = false; // And this works fine.
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private void RenderCtImageStackOptions()
        {
            ImGui.Text("Image Sequence Folder:");
            ImGui.InputText("##FolderPath", ref _filePath, 256);
            ImGui.SameLine();
            if (ImGui.Button("Browse..."))
            {
                var selectedFolder = Dialogs.SelectFolderDialog("Select Image Sequence Folder");
                if (!string.IsNullOrEmpty(selectedFolder))
                {
                    _filePath = selectedFolder;
                }
            }

            ImGui.InputInt("Binning Size", ref _binningSize);
            ImGui.Checkbox("Load Full in Memory", ref _loadInMemory);
            ImGui.InputFloat("Pixel Size", ref _pixelSize);
            ImGui.SameLine();
            ImGui.Combo("##unit", ref _pixelUnit, "micrometers\0millimeters\0");
        }
        
        private void RenderCtBinaryOptions()
        {
            ImGui.Text("Binary File:");
            ImGui.InputText("##FilePath", ref _filePath, 256);
            ImGui.SameLine();
            if (ImGui.Button("Browse..."))
            {
                var selectedFile = Dialogs.OpenFileDialog("Select Binary CT File", new[] { "*.bin", "*.raw" }, "Binary Files");
                if (!string.IsNullOrEmpty(selectedFile))
                {
                    _filePath = selectedFile;
                }
            }
            
            ImGui.Checkbox("Load Full in Memory", ref _loadInMemory);
        }

        private void RenderGenericFileOptions()
        {
            ImGui.Text("Dataset File:");
            ImGui.InputText("##FilePath", ref _filePath, 256);
            ImGui.SameLine();
            if (ImGui.Button("Browse..."))
            {
                var selectedFile = Dialogs.OpenFileDialog("Select File", new[] { "*.*" }, "All Files");
                if (!string.IsNullOrEmpty(selectedFile))
                {
                    _filePath = selectedFile;
                }
            }
        }
    }
}