// GeoscientistToolkit/UI/ImportDataModal.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GeoscientistToolkit.UI
{
    public class ImportDataModal
    {
        public bool IsOpen;
        private readonly ImGuiFileDialog _fileDialog;
        private readonly ImGuiFileDialog _folderDialog;

        // State for the import options
        private int _selectedDatasetTypeIndex;
        private readonly string[] _datasetTypeNames = { "Single Image", "CT Image Stack" };

        // Single Image options
        private string _imagePath = "";
        private bool _specifyPixelSize;
        private float _pixelSize = 1.0f;
        private string _pixelUnit = "µm";

        // CT Image Stack options
        private string _folderPath = "";

        // Progress tracking
        private float _progress;
        private string _statusText = "";
        private Task _importTask;

        public ImportDataModal()
        {
            _fileDialog = new ImGuiFileDialog("ImportFileDialog", FileDialogType.OpenFile, "Select Image File");
            _folderDialog = new ImGuiFileDialog("ImportFolderDialog", FileDialogType.OpenDirectory, "Select Image Folder");
        }

        public void Open()
        {
            IsOpen = true;
            ResetState();
        }

        public void Submit()
        {
            // Handle file dialog submissions first - BEFORE checking if ImportDataModal is open
            if (_fileDialog.Submit())
            {
                _imagePath = _fileDialog.SelectedPath;
            }
            
            if (_folderDialog.Submit())
            {
                _folderPath = _folderDialog.SelectedPath;
            }

            // Only render the import window if neither file dialog is open
            if (IsOpen && !_fileDialog.IsOpen && !_folderDialog.IsOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(500, 380), ImGuiCond.FirstUseEver);
                var center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
                
                // Use Begin with ref IsOpen to properly handle the X button
                if (ImGui.Begin("Import Data", ref IsOpen, ImGuiWindowFlags.NoDocking))
                {
                    if (_importTask != null && !_importTask.IsCompleted)
                    {
                        DrawProgress();
                    }
                    else
                    {
                        DrawOptions();
                    }
                    ImGui.End();
                }
            }
        }

        private void DrawOptions()
        {
            ImGui.Combo("Dataset Type", ref _selectedDatasetTypeIndex, _datasetTypeNames, _datasetTypeNames.Length);
            ImGui.Separator();

            if (_selectedDatasetTypeIndex == 0) // Single Image
            {
                ImGui.InputText("Image File", ref _imagePath, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##Image"))
                {
                    _fileDialog.Open(null, new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" });
                }

                ImGui.Checkbox("Specify Pixel Size", ref _specifyPixelSize);
                if (_specifyPixelSize)
                {
                    ImGui.Indent();
                    ImGui.SetNextItemWidth(100);
                    ImGui.InputFloat("Pixel Size", ref _pixelSize, 0.1f, 1.0f, "%.2f");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(80);
                    ImGui.InputText("Unit", ref _pixelUnit, 10);
                    ImGui.Unindent();
                }
            }
            else // CT Image Stack
            {
                ImGui.InputText("Image Folder", ref _folderPath, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##Folder"))
                {
                    _folderDialog.Open(null, null);
                }
                ImGui.TextDisabled("CT Image Stack import is not yet implemented.");
            }

            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                IsOpen = false;
            }
            ImGui.SameLine();

            bool canImport = (_selectedDatasetTypeIndex == 0 && !string.IsNullOrEmpty(_imagePath)) ||
                             (_selectedDatasetTypeIndex == 1 && !string.IsNullOrEmpty(_folderPath));

            if (!canImport) ImGui.BeginDisabled();
            if (ImGui.Button("Import", new Vector2(120, 0)))
            {
                StartImport();
            }
            if (!canImport) ImGui.EndDisabled();
        }

        private void DrawProgress()
        {
            ImGui.Text("Importing, please wait...");
            ImGui.ProgressBar(_progress, new Vector2(-1, 0), $"{(_progress * 100):0.0}%");
            ImGui.TextDisabled(_statusText);

            if (_importTask.IsFaulted)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Error during import:");
                ImGui.TextWrapped(_importTask.Exception?.GetBaseException().Message);
                if (ImGui.Button("Close"))
                {
                    ResetState();
                }
            }
            else if (_importTask.IsCompletedSuccessfully)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Import successful!");
                if (ImGui.Button("Close"))
                {
                    IsOpen = false;
                }
            }
        }

        private void StartImport()
        {
            _importTask = Task.Run(() =>
            {
                try
                {
                    _progress = 0f;
                    _statusText = "Starting import...";
                    Logger.Log(_statusText); // Assuming a static logger

                    if (_selectedDatasetTypeIndex == 0) // Single Image
                    {
                        var fileName = Path.GetFileNameWithoutExtension(_imagePath);
                        _statusText = $"Reading image file: {Path.GetFileName(_imagePath)}";
                        _progress = 0.25f;

                        var image = SixLabors.ImageSharp.Image.Load<Rgba32>(_imagePath);
                        _progress = 0.75f;

                        var dataset = new ImageDataset(fileName, _imagePath)
                        {
                            Width = image.Width,
                            Height = image.Height,
                            BitDepth = image.PixelType.BitsPerPixel,
                            PixelSize = _specifyPixelSize ? _pixelSize : 0,
                            Unit = _specifyPixelSize ? _pixelUnit : ""
                        };
                        
                        // We are NOT storing the image data in RAM. It will be loaded on-demand in the viewer.
                        image.Dispose(); 
                        
                        ProjectManager.Instance.AddDataset(dataset);
                        _statusText = "Dataset added to project.";
                        _progress = 1.0f;
                    }
                    else
                    {
                        // Logic for CT Stack import
                        throw new NotImplementedException("CT Stack import is not yet implemented.");
                    }
                }
                catch (Exception ex)
                {
                    _statusText = $"Failed: {ex.Message}";
                    throw; // Re-throw to be caught by the task's IsFaulted state
                }
            });
        }
        
        private void ResetState()
        {
            _imagePath = "";
            _folderPath = "";
            _specifyPixelSize = false;
            _pixelSize = 1.0f;
            _pixelUnit = "µm";
            _importTask = null;
            _progress = 0;
            _statusText = "";
        }
    }
}