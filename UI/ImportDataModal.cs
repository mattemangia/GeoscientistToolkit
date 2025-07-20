// GeoscientistToolkit/UI/ImportDataModal.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class ImportDataModal
    {
        public bool IsOpen;
        private readonly ImGuiFileDialog _fileDialog;
        private readonly ImGuiFileDialog _folderDialog;
        private readonly ImageStackOrganizerDialog _organizerDialog;

        // State for the import options
        private int _selectedDatasetTypeIndex;
        private readonly string[] _datasetTypeNames = { "Single Image", "Generic Image Stack", "CT Image Stack" };

        // Single Image options
        private string _imagePath = "";
        private bool _specifyPixelSize;
        private float _pixelSize = 1.0f;
        private string _pixelUnit = "µm";

        // Generic Image Stack options
        private string _imageStackFolderPath = "";

        // CT Image Stack options
        private string _ctFolderPath = "";

        // Progress tracking
        private float _progress;
        private string _statusText = "";
        private Task _importTask;

        public ImportDataModal()
        {
            _fileDialog = new ImGuiFileDialog("ImportFileDialog", FileDialogType.OpenFile, "Select Image File");
            _folderDialog = new ImGuiFileDialog("ImportFolderDialog", FileDialogType.OpenDirectory, "Select Image Folder");
            _organizerDialog = new ImageStackOrganizerDialog();
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
                if (_selectedDatasetTypeIndex == 1) // Generic Image Stack
                {
                    _imageStackFolderPath = _folderDialog.SelectedPath;
                    // Open the organizer dialog
                    _organizerDialog.Open(_imageStackFolderPath);
                }
                else if (_selectedDatasetTypeIndex == 2) // CT Image Stack
                {
                    _ctFolderPath = _folderDialog.SelectedPath;
                }
            }
            
            // Handle the organizer dialog
            _organizerDialog.Submit();

            // Only render the import window if neither file dialog is open and organizer is not open
            if (IsOpen && !_fileDialog.IsOpen && !_folderDialog.IsOpen && !_organizerDialog.IsOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(500, 400), ImGuiCond.FirstUseEver);
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
                    _fileDialog.Open(null, GetSupportedExtensions());
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
            else if (_selectedDatasetTypeIndex == 1) // Generic Image Stack
            {
                ImGui.InputText("Image Folder", ref _imageStackFolderPath, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##Stack"))
                {
                    _folderDialog.Open(null, null);
                }
                
                ImGui.TextWrapped("Select a folder containing images. You'll be able to organize them into groups in the next step.");
                
                if (!string.IsNullOrEmpty(_imageStackFolderPath))
                {
                    // Count images in folder
                    int imageCount = CountImagesInFolder(_imageStackFolderPath);
                    ImGui.TextDisabled($"Found {imageCount} images in folder");
                }
            }
            else // CT Image Stack
            {
                ImGui.InputText("Image Folder", ref _ctFolderPath, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##CTFolder"))
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
                             (_selectedDatasetTypeIndex == 1 && !string.IsNullOrEmpty(_imageStackFolderPath)) ||
                             (_selectedDatasetTypeIndex == 2 && !string.IsNullOrEmpty(_ctFolderPath));

            if (!canImport) ImGui.BeginDisabled();
            
            string buttonText = _selectedDatasetTypeIndex == 1 ? "Organize..." : "Import";
            if (ImGui.Button(buttonText, new Vector2(120, 0)))
            {
                if (_selectedDatasetTypeIndex == 1)
                {
                    // Open organizer for Generic Image Stack
                    _organizerDialog.Open(_imageStackFolderPath);
                }
                else
                {
                    StartImport();
                }
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
                    Logger.Log(_statusText);

                    if (_selectedDatasetTypeIndex == 0) // Single Image
                    {
                        var fileName = Path.GetFileNameWithoutExtension(_imagePath);
                        _statusText = $"Reading image file: {Path.GetFileName(_imagePath)}";
                        _progress = 0.25f;

                        var imageInfo = ImageLoader.LoadImageInfo(_imagePath);
                        _progress = 0.75f;

                        var dataset = new ImageDataset(fileName, _imagePath)
                        {
                            Width = imageInfo.Width,
                            Height = imageInfo.Height,
                            BitDepth = imageInfo.BitsPerChannel * imageInfo.Channels,
                            PixelSize = _specifyPixelSize ? _pixelSize : 0,
                            Unit = _specifyPixelSize ? _pixelUnit : ""
                        };
                        
                        ProjectManager.Instance.AddDataset(dataset);
                        _statusText = "Dataset added to project.";
                        _progress = 1.0f;
                    }
                    else if (_selectedDatasetTypeIndex == 2) // CT Stack
                    {
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

        private string[] GetSupportedExtensions()
        {
            return new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", ".gif", ".hdr", ".pic", ".pnm", ".ppm", ".pgm" };
        }

        private int CountImagesInFolder(string folderPath)
        {
            try
            {
                return Directory.GetFiles(folderPath)
                    .Count(file => ImageLoader.IsSupportedImageFile(file));
            }
            catch
            {
                return 0;
            }
        }
        
        private void ResetState()
        {
            _imagePath = "";
            _imageStackFolderPath = "";
            _ctFolderPath = "";
            _specifyPixelSize = false;
            _pixelSize = 1.0f;
            _pixelUnit = "µm";
            _importTask = null;
            _progress = 0;
            _statusText = "";
        }
    }
}