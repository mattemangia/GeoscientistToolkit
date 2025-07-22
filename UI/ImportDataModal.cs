// GeoscientistToolkit/UI/ImportDataModal.cs

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
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
        private float _ctPixelSize = 1.0f;
        private int _ctPixelSizeUnit = 0; // 0 = micrometers, 1 = millimeters
        private readonly string[] _pixelSizeUnits = { "µm", "mm" };
        private int _ctBinningFactor = 1;
        private bool _useMemoryMapping = false;

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
            // Handle file dialog submissions first
            if (_fileDialog.Submit())
            {
                _imagePath = _fileDialog.SelectedPath;
            }
            
            if (_folderDialog.Submit())
            {
                if (_selectedDatasetTypeIndex == 1) // Generic Image Stack
                {
                    _imageStackFolderPath = _folderDialog.SelectedPath;
                    _organizerDialog.Open(_imageStackFolderPath);
                }
                else if (_selectedDatasetTypeIndex == 2) // CT Image Stack
                {
                    _ctFolderPath = _folderDialog.SelectedPath;
                }
            }
            
            // Handle the organizer dialog
            _organizerDialog.Submit();

            // Only render the import window if dialogs are closed
            if (IsOpen && !_fileDialog.IsOpen && !_folderDialog.IsOpen && !_organizerDialog.IsOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);
                var center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
                
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
            ImGui.Text("Dataset Type:");
            ImGui.Combo("##DatasetType", ref _selectedDatasetTypeIndex, _datasetTypeNames, _datasetTypeNames.Length);
            ImGui.Separator();
            ImGui.Spacing();

            switch (_selectedDatasetTypeIndex)
            {
                case 0: // Single Image
                    DrawSingleImageOptions();
                    break;
                case 1: // Generic Image Stack
                    DrawGenericImageStackOptions();
                    break;
                case 2: // CT Image Stack
                    DrawCTImageStackOptions();
                    break;
            }

            ImGui.Separator();
            ImGui.Spacing();
            
            DrawButtons();
        }

        private void DrawSingleImageOptions()
        {
            ImGui.Text("Image File:");
            ImGui.InputText("##ImagePath", ref _imagePath, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##Image"))
            {
                _fileDialog.Open(null, GetSupportedExtensions());
            }

            ImGui.Spacing();
            ImGui.Checkbox("Specify Pixel Size", ref _specifyPixelSize);
            
            if (_specifyPixelSize)
            {
                ImGui.Indent();
                ImGui.Text("Pixel Size:");
                ImGui.SetNextItemWidth(100);
                ImGui.InputFloat("##PixelSize", ref _pixelSize, 0.1f, 1.0f, "%.2f");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                ImGui.InputText("Unit", ref _pixelUnit, 10);
                ImGui.Unindent();
            }
        }

        private void DrawGenericImageStackOptions()
        {
            ImGui.Text("Image Folder:");
            ImGui.InputText("##ImageStackPath", ref _imageStackFolderPath, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##Stack"))
            {
                _folderDialog.Open(null, null);
            }
            
            ImGui.Spacing();
            ImGui.TextWrapped("Select a folder containing images. You'll be able to organize them into groups in the next step.");
            
            if (!string.IsNullOrEmpty(_imageStackFolderPath))
            {
                int imageCount = CountImagesInFolder(_imageStackFolderPath);
                ImGui.TextDisabled($"Found {imageCount} images in folder");
            }
        }

        private void DrawCTImageStackOptions()
        {
            ImGui.Text("Image Folder:");
            ImGui.InputText("##CTPath", ref _ctFolderPath, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##CTFolder"))
            {
                _folderDialog.Open(null, null);
            }
            
            if (!string.IsNullOrEmpty(_ctFolderPath))
            {
                int imageCount = CountImagesInFolder(_ctFolderPath);
                ImGui.TextDisabled($"Found {imageCount} images in folder");
                
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                
                // Pixel size input
                ImGui.Text("Pixel Size:");
                ImGui.SetNextItemWidth(150);
                ImGui.InputFloat("##PixelSizeCT", ref _ctPixelSize, 0.1f, 1.0f, "%.3f");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                ImGui.Combo("##Unit", ref _ctPixelSizeUnit, _pixelSizeUnits, _pixelSizeUnits.Length);
                
                ImGui.Spacing();
                
                // Binning factor
                ImGui.Text("Binning Factor:");
                ImGui.SetNextItemWidth(150);
                int[] binningOptions = { 1, 2, 4, 8 };
                string[] binningLabels = { "1×1×1 (None)", "2×2×2", "4×4×4", "8×8×8" };
                int binningIndex = Array.IndexOf(binningOptions, _ctBinningFactor);
                if (ImGui.Combo("##Binning", ref binningIndex, binningLabels, binningLabels.Length))
                {
                    _ctBinningFactor = binningOptions[binningIndex];
                }
                
                if (_ctBinningFactor > 1)
                {
                    ImGui.Indent();
                    ImGui.TextWrapped($"Note: 3D binning will reduce volume by {_ctBinningFactor}× in all dimensions (X, Y, and Z)");
                    ImGui.Unindent();
                }
                
                ImGui.Spacing();
                
                // Memory mapping option
                ImGui.Checkbox("Use Memory-Mapped Files", ref _useMemoryMapping);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Enable for very large datasets (>4GB). Keeps data on disk instead of loading into RAM.");
                }
                
                if (_useMemoryMapping)
                {
                    ImGui.Indent();
                    ImGui.TextWrapped("Memory-mapped files allow handling of very large datasets but may be slower for some operations.");
                    ImGui.Unindent();
                }
            }
        }

        private void DrawButtons()
        {
            float buttonWidth = 120;
            
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                IsOpen = false;
            }
            
            ImGui.SameLine();

            bool canImport = CanImport();
            
            if (!canImport) ImGui.BeginDisabled();
            
            string buttonText = _selectedDatasetTypeIndex == 1 ? "Organize..." : "Import";
            if (ImGui.Button(buttonText, new Vector2(buttonWidth, 0)))
            {
                if (_selectedDatasetTypeIndex == 1)
                {
                    _organizerDialog.Open(_imageStackFolderPath);
                }
                else
                {
                    StartImport();
                }
            }
            
            if (!canImport) ImGui.EndDisabled();
        }

        private bool CanImport()
        {
            return _selectedDatasetTypeIndex switch
            {
                0 => !string.IsNullOrEmpty(_imagePath),
                1 => !string.IsNullOrEmpty(_imageStackFolderPath),
                2 => !string.IsNullOrEmpty(_ctFolderPath),
                _ => false
            };
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
            _importTask = _selectedDatasetTypeIndex switch
            {
                0 => ImportSingleImage(),
                2 => ImportCTStack(),
                _ => Task.CompletedTask
            };
        }

        private async Task ImportSingleImage()
        {
            try
            {
                _progress = 0f;
                _statusText = "Starting import...";
                Logger.Log(_statusText);

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
            catch (Exception ex)
            {
                _statusText = $"Failed: {ex.Message}";
                throw;
            }
        }

        private async Task ImportCTStack()
        {
            try
            {
                _statusText = "Loading CT image stack...";
                _progress = 0.1f;

                // Convert pixel size to meters
                double pixelSizeInMeters = _ctPixelSizeUnit == 0 
                    ? _ctPixelSize * 1e-6  // micrometers to meters
                    : _ctPixelSize * 1e-3; // millimeters to meters

                // Create progress reporter
                var progressReporter = new Progress<float>(p => 
                {
                    _progress = 0.1f + (p * 0.8f); // Map to 10%-90%
                    _statusText = $"Processing images... {(int)(p * 100)}%";
                });

                // Load the volume
                var folderName = Path.GetFileName(_ctFolderPath);
                var volume = await CTStackLoader.LoadCTStackAsync(
                    _ctFolderPath, 
                    pixelSizeInMeters, 
                    _ctBinningFactor,
                    _useMemoryMapping,
                    progressReporter,
                    folderName);

                _progress = 0.9f;
                _statusText = "Creating dataset...";

                // Create the dataset
                var dataset = new CtImageStackDataset(folderName, _ctFolderPath)
                {
                    Width = volume.Width,
                    Height = volume.Height,
                    Depth = volume.Depth,
                    PixelSize = (float)(pixelSizeInMeters * 1e6), // Store in micrometers
                    SliceThickness = (float)(pixelSizeInMeters * 1e6), // Assume isotropic
                    Unit = "µm",
                    BinningSize = _ctBinningFactor,
                    BitDepth = 8
                };

                // Add to project
                ProjectManager.Instance.AddDataset(dataset);
                
                _statusText = "CT stack loaded successfully!";
                _progress = 1.0f;
            }
            catch (Exception ex)
            {
                _statusText = $"Failed: {ex.Message}";
                throw;
            }
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
            _ctPixelSize = 1.0f;
            _ctPixelSizeUnit = 0;
            _ctBinningFactor = 1;
            _useMemoryMapping = false;
            _importTask = null;
            _progress = 0;
            _statusText = "";
        }
    }
}