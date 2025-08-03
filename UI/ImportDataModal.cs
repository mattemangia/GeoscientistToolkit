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
        private enum ImportState { Idle, ShowingOptions, Processing }
        private ImportState _currentState = ImportState.Idle;

        private readonly ImGuiFileDialog _folderDialog;
        private readonly ImGuiFileDialog _fileDialog;
        private readonly ImGuiFileDialog _ctFileDialog; // New dialog for CT TIFF files
        private readonly ImageStackOrganizerDialog _organizerDialog;

        private int _selectedDatasetTypeIndex = 0;
        private readonly string[] _datasetTypeNames = {
            "Single Image",
            "Image Folder (Group)",
            "CT Image Stack (Optimized for 3D Streaming)",
            "CT Image Stack (Legacy for 2D Editing)"
        };

        // Single image properties
        private string _singleImagePath = "";
        private float _singleImagePixelSize = 1.0f;
        private int _singleImagePixelSizeUnit = 0;
        private readonly string[] _pixelSizeUnits = { "µm", "mm" };
        private ImageDataset _pendingSingleImageDataset = null;

        // Image folder properties
        private string _imageFolderPath = "";
        private bool _shouldOpenOrganizer = false;

        // CT stack properties
        private string _ctPath = ""; // Can be either folder or TIFF file
        private bool _ctIsMultiPageTiff = false;
        private float _ctPixelSize = 1.0f;
        private int _ctPixelSizeUnit = 0;
        private int _ctBinningFactor = 1;
        private StreamingCtVolumeDataset _pendingStreamingDataset = null;
        private CtImageStackDataset _pendingLegacyDataset = null;

        private float _progress;
        private string _statusText = "";
        private Task _importTask;

        public ImportDataModal()
        {
            _folderDialog = new ImGuiFileDialog("ImportFolderDialog", FileDialogType.OpenDirectory, "Select Image Folder");
            _fileDialog = new ImGuiFileDialog("ImportFileDialog", FileDialogType.OpenFile, "Select Image File");
            _ctFileDialog = new ImGuiFileDialog("ImportCTFileDialog", FileDialogType.OpenFile, "Select Multi-Page TIFF File");
            _organizerDialog = new ImageStackOrganizerDialog();
        }

        public void Open()
        {
            ResetState();
            _currentState = ImportState.ShowingOptions;
        }

        public void Submit()
        {
            if (_currentState == ImportState.Idle) return;

            // Handle organizer dialog
            if (_organizerDialog.IsOpen)
            {
                _organizerDialog.Submit();
                if (!_organizerDialog.IsOpen)
                {
                    // Organizer closed, close the import modal too
                    _currentState = ImportState.Idle;
                }
                return;
            }

            // Check if we should open the organizer
            if (_shouldOpenOrganizer)
            {
                _shouldOpenOrganizer = false;
                _organizerDialog.Open(_imageFolderPath);
                return;
            }

            // Handle dialog submissions
            if (_folderDialog.Submit())
            {
                if (_selectedDatasetTypeIndex == 1) // Image Folder
                    _imageFolderPath = _folderDialog.SelectedPath;
                else // CT Stack
                {
                    _ctPath = _folderDialog.SelectedPath;
                    _ctIsMultiPageTiff = false;
                }
            }
            if (_fileDialog.Submit()) _singleImagePath = _fileDialog.SelectedPath;
            if (_ctFileDialog.Submit())
            {
                _ctPath = _ctFileDialog.SelectedPath;
                _ctIsMultiPageTiff = true;
            }

            ImGui.SetNextWindowSize(new Vector2(600, 550), ImGuiCond.FirstUseEver);
            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

            bool modalOpen = true;
            if (ImGui.Begin("Import Data", ref modalOpen, ImGuiWindowFlags.NoDocking))
            {
                if (_currentState == ImportState.Processing)
                {
                    DrawProgress();
                }
                else
                {
                    DrawOptions();
                }
                ImGui.End();
            }
            if (!modalOpen) _currentState = ImportState.Idle;
        }

        private void DrawOptions()
        {
            ImGui.Text("Select the type of data to import:");
            ImGui.Combo("##DatasetType", ref _selectedDatasetTypeIndex, _datasetTypeNames, _datasetTypeNames.Length);
            ImGui.Separator();
            ImGui.Spacing();

            // Draw appropriate options based on selected type
            switch (_selectedDatasetTypeIndex)
            {
                case 0: // Single Image
                    DrawSingleImageOptions();
                    break;
                case 1: // Image Folder (Group)
                    DrawImageFolderOptions();
                    break;
                case 2: // CT Image Stack (Optimized)
                case 3: // CT Image Stack (Legacy)
                    DrawCTImageStackOptions();
                    break;
            }

            ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetFrameHeightWithSpacing() * 1.5f);
            ImGui.Separator();
            DrawButtons();
        }

        private void DrawSingleImageOptions()
        {
            ImGui.Text("Image File:");
            ImGui.InputText("##ImagePath", ref _singleImagePath, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##ImageFile"))
            {
                // Set allowed extensions for image files
                string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", ".gif", ".hdr", ".pic", ".pnm", ".ppm", ".pgm", ".tif", ".tiff" };
                _fileDialog.Open(null, imageExtensions);
            }

            ImGui.Spacing();
            ImGui.Text("Pixel Size (optional):");
            ImGui.SetNextItemWidth(150);
            ImGui.InputFloat("##PixelSizeSingle", ref _singleImagePixelSize, 0.1f, 1.0f, "%.3f");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.Combo("##UnitSingle", ref _singleImagePixelSizeUnit, _pixelSizeUnits, _pixelSizeUnits.Length);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Set the physical size of each pixel. Leave as 1.0 if unknown.");
            }

            // Show image preview if available
            if (!string.IsNullOrEmpty(_singleImagePath) && File.Exists(_singleImagePath))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                try
                {
                    var info = ImageLoader.LoadImageInfo(_singleImagePath);
                    ImGui.Text("Image Information:");
                    ImGui.BulletText($"Resolution: {info.Width} x {info.Height}");
                    ImGui.BulletText($"File: {Path.GetFileName(_singleImagePath)}");
                    ImGui.BulletText($"Size: {new FileInfo(_singleImagePath).Length / 1024} KB");
                }
                catch (Exception ex)
                {
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error reading image: {ex.Message}");
                }
            }
        }

        private void DrawImageFolderOptions()
        {
            ImGui.Text("Image Folder:");
            ImGui.InputText("##ImageFolderPath", ref _imageFolderPath, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##ImageFolder"))
            {
                _folderDialog.Open();
            }

            ImGui.Spacing();
            ImGui.TextWrapped("This option allows you to load multiple images from a folder and organize them into groups.");

            // Show folder information if available
            if (!string.IsNullOrEmpty(_imageFolderPath) && Directory.Exists(_imageFolderPath))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                var (count, totalSize) = CountImagesInFolder(_imageFolderPath);
                ImGui.Text("Folder Information:");
                ImGui.BulletText($"Image Count: {count}");
                ImGui.BulletText($"Total Size: {totalSize / (1024 * 1024)} MB");
                ImGui.BulletText($"Folder: {Path.GetFileName(_imageFolderPath)}");

                if (count == 0)
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Warning: No supported image files found in this folder.");
                }
            }
        }

        private void DrawCTImageStackOptions()
        {
            ImGui.Text("CT Stack Source:");
            
            // Radio buttons for folder vs file selection
            bool isFolderMode = !_ctIsMultiPageTiff;
            if (ImGui.RadioButton("Image Folder", isFolderMode))
            {
                _ctIsMultiPageTiff = false;
                _ctPath = "";
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("Multi-Page TIFF File", !isFolderMode))
            {
                _ctIsMultiPageTiff = true;
                _ctPath = "";
            }

            ImGui.Spacing();

            // Show appropriate input based on selection
            if (_ctIsMultiPageTiff)
            {
                ImGui.Text("TIFF File:");
                ImGui.InputText("##CTFilePath", ref _ctPath, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##CTFile"))
                {
                    string[] tiffExtensions = { ".tif", ".tiff" };
                    _ctFileDialog.Open(null, tiffExtensions);
                }
            }
            else
            {
                ImGui.Text("Image Folder:");
                ImGui.InputText("##CTFolderPath", ref _ctPath, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##CTFolder"))
                {
                    _folderDialog.Open();
                }
            }

            ImGui.Spacing();
            ImGui.Text("Voxel Size:");
            ImGui.SetNextItemWidth(150);
            ImGui.InputFloat("##PixelSizeCT", ref _ctPixelSize, 0.1f, 1.0f, "%.3f");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.Combo("##Unit", ref _ctPixelSizeUnit, _pixelSizeUnits, _pixelSizeUnits.Length);

            ImGui.Spacing();

            ImGui.Text("3D Binning Factor:");
            ImGui.SetNextItemWidth(230);
            int[] binningOptions = { 1, 2, 4, 8 };
            string[] binningLabels = { "1×1×1 (None)", "2×2×2 (8x smaller)", "4×4×4 (64x smaller)", "8×8×8 (512x smaller)" };
            int binningIndex = Array.IndexOf(binningOptions, _ctBinningFactor);
            if (ImGui.Combo("##Binning", ref binningIndex, binningLabels, binningLabels.Length))
            {
                _ctBinningFactor = binningOptions[binningIndex];
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Reduces dataset size by averaging voxels. A factor of 2 makes the data 8 times smaller. This is a pre-processing step.");
            }

            // Show information if available
            if (!string.IsNullOrEmpty(_ctPath))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (_ctIsMultiPageTiff && File.Exists(_ctPath))
                {
                    try
                    {
                        if (ImageLoader.IsMultiPageTiff(_ctPath))
                        {
                            int pageCount = ImageLoader.GetTiffPageCount(_ctPath);
                            var info = ImageLoader.LoadImageInfo(_ctPath);
                            long fileSize = new FileInfo(_ctPath).Length;
                            
                            ImGui.Text("Multi-Page TIFF Information:");
                            ImGui.BulletText($"Pages (Slices): {pageCount}");
                            ImGui.BulletText($"Resolution: {info.Width} x {info.Height}");
                            ImGui.BulletText($"Total Size: {fileSize / (1024 * 1024)} MB");
                            ImGui.BulletText($"File: {Path.GetFileName(_ctPath)}");
                        }
                        else
                        {
                            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), 
                                "Warning: Selected TIFF file contains only one page. CT stacks require multiple pages.");
                        }
                    }
                    catch (Exception ex)
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error reading TIFF: {ex.Message}");
                    }
                }
                else if (!_ctIsMultiPageTiff && Directory.Exists(_ctPath))
                {
                    var (count, totalSize) = CountImagesInFolder(_ctPath);
                    ImGui.Text("Folder Information:");
                    ImGui.BulletText($"Image Count: {count}");
                    ImGui.BulletText($"Total Size: {totalSize / (1024 * 1024)} MB");
                }
            }
        }

        private void DrawButtons()
        {
            bool canImport = false;

            // Check if we can import based on selected type
            switch (_selectedDatasetTypeIndex)
            {
                case 0: // Single Image
                    canImport = !string.IsNullOrEmpty(_singleImagePath) && File.Exists(_singleImagePath);
                    break;
                case 1: // Image Folder
                    canImport = !string.IsNullOrEmpty(_imageFolderPath) && Directory.Exists(_imageFolderPath);
                    break;
                case 2: // CT Stack Optimized
                case 3: // CT Stack Legacy
                    if (_ctIsMultiPageTiff)
                    {
                        canImport = !string.IsNullOrEmpty(_ctPath) && File.Exists(_ctPath) && 
                                   ImageLoader.IsMultiPageTiff(_ctPath);
                    }
                    else
                    {
                        canImport = !string.IsNullOrEmpty(_ctPath) && Directory.Exists(_ctPath);
                    }
                    break;
            }

            if (ImGui.Button("Cancel", new Vector2(120, 0))) _currentState = ImportState.Idle;
            ImGui.SameLine();

            if (!canImport) ImGui.BeginDisabled();
            if (ImGui.Button("Import", new Vector2(120, 0))) HandleImportClick();
            if (!canImport) ImGui.EndDisabled();
        }

        private void HandleImportClick()
        {
            switch (_selectedDatasetTypeIndex)
            {
                case 0: // Single Image
                    _currentState = ImportState.Processing;
                    _importTask = ImportSingleImage();
                    break;
                case 1: // Image Folder - open organizer
                    _shouldOpenOrganizer = true;
                    break;
                case 2: // CT Stack Optimized
                    _currentState = ImportState.Processing;
                    _importTask = ConvertAndImportOptimizedCTStack();
                    break;
                case 3: // CT Stack Legacy
                    _currentState = ImportState.Processing;
                    _importTask = ImportLegacyCTStack();
                    break;
            }
        }

        private async Task ImportSingleImage()
        {
            _statusText = "Loading image...";
            _progress = 0.1f;

            await Task.Run(() =>
            {
                try
                {
                    var fileName = Path.GetFileName(_singleImagePath);
                    var dataset = new ImageDataset(Path.GetFileNameWithoutExtension(fileName), _singleImagePath);

                    _progress = 0.3f;
                    _statusText = "Reading image properties...";

                    // Load the image to get properties
                    var imageInfo = ImageLoader.LoadImageInfo(_singleImagePath);
                    dataset.Width = imageInfo.Width;
                    dataset.Height = imageInfo.Height;
                    dataset.BitDepth = imageInfo.BitsPerChannel * imageInfo.Channels;

                    // Set pixel size if provided
                    if (_singleImagePixelSize > 0)
                    {
                        dataset.PixelSize = _singleImagePixelSizeUnit == 0 ? _singleImagePixelSize : _singleImagePixelSize * 1000;
                        dataset.Unit = "µm";
                    }

                    _progress = 0.8f;
                    _statusText = "Adding to project...";

                    // Store dataset to be added when task completes
                    _pendingSingleImageDataset = dataset;

                    _progress = 1.0f;
                    _statusText = "Single image imported successfully!";
                }
                catch (Exception ex)
                {
                    _statusText = $"Error: {ex.Message}";
                    throw;
                }
            });
        }

        private void DrawProgress()
        {
            ImGui.Text(_statusText);
            ImGui.ProgressBar(_progress, new Vector2(-1, 0), $"{(_progress * 100):0}%");
            if (_importTask.IsFaulted)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Error during import:");
                ImGui.TextWrapped(_importTask.Exception?.GetBaseException().Message);
                if (ImGui.Button("Close")) ResetState();
            }
            else if (_importTask.IsCompletedSuccessfully)
            {
                // Add pending datasets to project manager now that we're back on UI thread
                if (_pendingSingleImageDataset != null)
                {
                    ProjectManager.Instance.AddDataset(_pendingSingleImageDataset);
                    _pendingSingleImageDataset = null;
                }

                if (_pendingLegacyDataset != null)
                {
                    ProjectManager.Instance.AddDataset(_pendingLegacyDataset);

                    // If there's also a streaming dataset, it's a partner
                    if (_pendingStreamingDataset != null)
                    {
                        _pendingStreamingDataset.EditablePartner = _pendingLegacyDataset;
                        ProjectManager.Instance.AddDataset(_pendingStreamingDataset);
                    }

                    _pendingLegacyDataset = null;
                    _pendingStreamingDataset = null;
                }

                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Import successful!");
                if (ImGui.Button("Close")) _currentState = ImportState.Idle;
            }
        }

        private async Task ConvertAndImportOptimizedCTStack()
{
    string name = _ctIsMultiPageTiff ? Path.GetFileNameWithoutExtension(_ctPath) : Path.GetFileName(_ctPath);
    string outputDir = _ctIsMultiPageTiff ? Path.GetDirectoryName(_ctPath) : _ctPath;
    string gvtFileName = _ctBinningFactor > 1 ? $"{name}_bin{_ctBinningFactor}.gvt" : $"{name}.gvt";
    string gvtPath = Path.Combine(outputDir, gvtFileName);

    // First load the volume data
    _statusText = "Loading volume data...";
    var binnedVolume = await CTStackLoader.LoadCTStackAsync(_ctPath, GetPixelSizeInMeters(), _ctBinningFactor, false,
        new Progress<float>(p => { _progress = p * 0.5f; _statusText = $"Step 1/2: Loading and processing images... {(int)(p * 100)}%"; }), name);

    // IMPORTANT: Ensure the volume is fully loaded before conversion
    if (binnedVolume == null || binnedVolume.Width == 0 || binnedVolume.Height == 0 || binnedVolume.Depth == 0)
    {
        throw new InvalidOperationException("Failed to load volume data from TIFF file");
    }

    // Log volume info to verify it's loaded
    Logger.Log($"[ImportDataModal] Loaded volume for conversion: {binnedVolume.Width}×{binnedVolume.Height}×{binnedVolume.Depth}");

    // Check if we need to create the .gvt file
    if (!File.Exists(gvtPath))
    {
        _statusText = "Converting to optimized format...";
        
        // Verify volume has actual data before conversion
        bool hasData = false;
        for (int z = 0; z < Math.Min(5, binnedVolume.Depth); z++)
        {
            for (int y = 0; y < Math.Min(5, binnedVolume.Height); y++)
            {
                for (int x = 0; x < Math.Min(5, binnedVolume.Width); x++)
                {
                    if (binnedVolume[x, y, z] > 0)
                    {
                        hasData = true;
                        break;
                    }
                }
                if (hasData) break;
            }
            if (hasData) break;
        }
        
        if (!hasData)
        {
            Logger.LogError("[ImportDataModal] Volume appears to be empty!");
        }
        
        await CtStackConverter.ConvertToStreamableFormat(binnedVolume, gvtPath,
            (p, s) => { _progress = 0.5f + (p * 0.5f); _statusText = $"Step 2/2: {s}"; });
    }
    else
    {
        _statusText = "Found existing optimized file. Loading..."; 
        _progress = 1.0f;
    }

    var legacyDataset = await CreateLegacyDatasetForEditing(binnedVolume);

    var streamingDataset = new StreamingCtVolumeDataset($"{name} (3D View)", gvtPath)
    {
        EditablePartner = legacyDataset
    };

    // Store datasets to be added when task completes
    _pendingStreamingDataset = streamingDataset;
    _pendingLegacyDataset = legacyDataset;

    _statusText = "Optimized dataset and editable partner added to project!";
}

        private async Task ImportLegacyCTStack()
        {
            await Task.Run(async () =>
            {
                string name = _ctIsMultiPageTiff ? Path.GetFileNameWithoutExtension(_ctPath) : Path.GetFileName(_ctPath);
                var progressReporter = new Progress<float>(p =>
                {
                    _progress = p;
                    _statusText = $"Processing images... {(int)(p * 100)}%";
                });

                // Load the volume from images or TIFF
                var volume = await CTStackLoader.LoadCTStackAsync(_ctPath, GetPixelSizeInMeters(), _ctBinningFactor, false, progressReporter, name);

                double pixelSizeMicrons = (_ctPixelSizeUnit == 0 ? _ctPixelSize : _ctPixelSize * 1000) * _ctBinningFactor;

                // Create the dataset object
                _pendingLegacyDataset = new CtImageStackDataset($"{name} (2D Edit & Segment)", _ctPath)
                {
                    Width = volume.Width,
                    Height = volume.Height,
                    Depth = volume.Depth,
                    PixelSize = (float)pixelSizeMicrons,
                    SliceThickness = (float)pixelSizeMicrons,
                    Unit = "µm",
                    BinningSize = _ctBinningFactor
                };

                _statusText = "Legacy dataset added to project!";
            });
        }

        private async Task<CtImageStackDataset> CreateLegacyDatasetForEditing(ChunkedVolume preloadedVolume)
        {
            string name = _ctIsMultiPageTiff ? Path.GetFileNameWithoutExtension(_ctPath) : Path.GetFileName(_ctPath);
            CtImageStackDataset dataset = null;

            await Task.Run(async () =>
            {
                var volume = preloadedVolume;
                if (volume == null)
                {
                    var progressReporter = new Progress<float>(p =>
                    {
                        _progress = p;
                        _statusText = $"Processing images... {(int)(p * 100)}%";
                    });
                    volume = await CTStackLoader.LoadCTStackAsync(_ctPath, GetPixelSizeInMeters(), _ctBinningFactor, false, progressReporter, name);
                }

                double pixelSizeMicrons = (_ctPixelSizeUnit == 0 ? _ctPixelSize : _ctPixelSize * 1000) * _ctBinningFactor;

                dataset = new CtImageStackDataset($"{name} (2D Edit & Segment)", _ctPath)
                {
                    Width = volume.Width,
                    Height = volume.Height,
                    Depth = volume.Depth,
                    PixelSize = (float)pixelSizeMicrons,
                    SliceThickness = (float)pixelSizeMicrons,
                    Unit = "µm",
                    BinningSize = _ctBinningFactor
                };
            });
            return dataset;
        }

        private double GetPixelSizeInMeters()
        {
            // Convert to meters based on unit selection
            return _ctPixelSizeUnit == 0 ? 
                _ctPixelSize * 1e-6 :  // micrometers to meters
                _ctPixelSize * 1e-3;   // millimeters to meters
        }

        private (int count, long totalSize) CountImagesInFolder(string folderPath)
        {
            try
            {
                var files = Directory.GetFiles(folderPath).Where(ImageLoader.IsSupportedImageFile).ToArray();
                return (files.Length, files.Sum(f => new FileInfo(f).Length));
            }
            catch { return (0, 0); }
        }

        private void ResetState()
        {
            _singleImagePath = "";
            _singleImagePixelSize = 1.0f;
            _singleImagePixelSizeUnit = 0;
            _imageFolderPath = "";
            _ctPath = "";
            _ctIsMultiPageTiff = false;
            _ctPixelSize = 1.0f;
            _ctPixelSizeUnit = 0;
            _ctBinningFactor = 1;
            _importTask = null;
            _progress = 0;
            _statusText = "";
            _currentState = ImportState.Idle;
            _pendingSingleImageDataset = null;
            _pendingStreamingDataset = null;
            _pendingLegacyDataset = null;
            _shouldOpenOrganizer = false;
        }
    }
}