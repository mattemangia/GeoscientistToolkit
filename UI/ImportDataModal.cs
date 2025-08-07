// Updated GeoscientistToolkit/UI/ImportDataModal.cs
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
using GeoscientistToolkit.Data.Mesh3D;
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
        private readonly ImGuiFileDialog _ctFileDialog;
        private readonly ImGuiFileDialog _mesh3DDialog;
        private readonly ImGuiFileDialog _labeledVolumeDialog;
        private readonly ImageStackOrganizerDialog _organizerDialog;

        private int _selectedDatasetTypeIndex = 0;
        private readonly string[] _datasetTypeNames = {
            "Single Image",
            "Image Folder (Group)",
            "CT Image Stack (Optimized for 3D Streaming)",
            "CT Image Stack (Legacy for 2D Editing)",
            "3D Object (OBJ/STL)",
            "Labeled Volume Stack (Color-coded Materials)"
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
        private string _ctPath = "";
        private bool _ctIsMultiPageTiff = false;
        private float _ctPixelSize = 1.0f;
        private int _ctPixelSizeUnit = 0;
        private int _ctBinningFactor = 1;
        private StreamingCtVolumeDataset _pendingStreamingDataset = null;
        private CtImageStackDataset _pendingLegacyDataset = null;

        // 3D Object properties
        private string _mesh3DPath = "";
        private float _mesh3DScale = 1.0f;
        private Mesh3DDataset _pendingMesh3DDataset = null;

        // Labeled Volume properties
        private string _labeledVolumePath = "";
        private bool _labeledIsMultiPageTiff = false;
        private float _labeledPixelSize = 1.0f;
        private int _labeledPixelSizeUnit = 0;
        private CtImageStackDataset _pendingLabeledDataset = null;

        private float _progress;
        private string _statusText = "";
        private Task _importTask;

        public ImportDataModal()
        {
            _folderDialog = new ImGuiFileDialog("ImportFolderDialog", FileDialogType.OpenDirectory, "Select Image Folder");
            _fileDialog = new ImGuiFileDialog("ImportFileDialog", FileDialogType.OpenFile, "Select Image File");
            _ctFileDialog = new ImGuiFileDialog("ImportCTFileDialog", FileDialogType.OpenFile, "Select Multi-Page TIFF File");
            _mesh3DDialog = new ImGuiFileDialog("Import3DDialog", FileDialogType.OpenFile, "Select 3D Model File");
            _labeledVolumeDialog = new ImGuiFileDialog("ImportLabeledDialog", FileDialogType.OpenFile, "Select Labeled Volume File");
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
                else if (_selectedDatasetTypeIndex == 5) // Labeled Volume
                {
                    _labeledVolumePath = _folderDialog.SelectedPath;
                    _labeledIsMultiPageTiff = false;
                }
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
            if (_mesh3DDialog.Submit()) _mesh3DPath = _mesh3DDialog.SelectedPath;
            if (_labeledVolumeDialog.Submit())
            {
                _labeledVolumePath = _labeledVolumeDialog.SelectedPath;
                _labeledIsMultiPageTiff = true;
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
                case 4: // 3D Object
                    Draw3DObjectOptions();
                    break;
                case 5: // Labeled Volume Stack
                    DrawLabeledVolumeOptions();
                    break;
            }

            ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetFrameHeightWithSpacing() * 1.5f);
            ImGui.Separator();
            DrawButtons();
        }

        private void DrawLabeledVolumeOptions()
        {
            ImGui.TextWrapped("Import a stack of labeled images where each unique color represents a different material. " +
                            "This will automatically create materials and generate both label and grayscale volumes.");
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.Text("Labeled Stack Source:");
            
            bool isFolderMode = !_labeledIsMultiPageTiff;
            if (ImGui.RadioButton("Image Folder##Labeled", isFolderMode))
            {
                _labeledIsMultiPageTiff = false;
                _labeledVolumePath = "";
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("Multi-Page TIFF File##Labeled", !isFolderMode))
            {
                _labeledIsMultiPageTiff = true;
                _labeledVolumePath = "";
            }

            ImGui.Spacing();

            if (_labeledIsMultiPageTiff)
            {
                ImGui.Text("TIFF File:");
                ImGui.InputText("##LabeledFilePath", ref _labeledVolumePath, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##LabeledFile"))
                {
                    string[] tiffExtensions = { ".tif", ".tiff" };
                    _labeledVolumeDialog.Open(null, tiffExtensions);
                }
            }
            else
            {
                ImGui.Text("Image Folder:");
                ImGui.InputText("##LabeledFolderPath", ref _labeledVolumePath, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##LabeledFolder"))
                {
                    _folderDialog.Open();
                }
            }

            ImGui.Spacing();
            ImGui.Text("Voxel Size:");
            ImGui.SetNextItemWidth(150);
            ImGui.InputFloat("##PixelSizeLabeled", ref _labeledPixelSize, 0.1f, 1.0f, "%.3f");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.Combo("##UnitLabeled", ref _labeledPixelSizeUnit, _pixelSizeUnits, _pixelSizeUnits.Length);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Physical size of each voxel in the volume.");
            }

            if (!string.IsNullOrEmpty(_labeledVolumePath))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (_labeledIsMultiPageTiff && File.Exists(_labeledVolumePath))
                {
                    try
                    {
                        if (ImageLoader.IsMultiPageTiff(_labeledVolumePath))
                        {
                            int pageCount = ImageLoader.GetTiffPageCount(_labeledVolumePath);
                            var info = ImageLoader.LoadImageInfo(_labeledVolumePath);
                            long fileSize = new FileInfo(_labeledVolumePath).Length;
                            
                            ImGui.Text("Multi-Page TIFF Information:");
                            ImGui.BulletText($"Pages (Slices): {pageCount}");
                            ImGui.BulletText($"Resolution: {info.Width} x {info.Height}");
                            ImGui.BulletText($"Total Size: {fileSize / (1024 * 1024)} MB");
                            ImGui.BulletText($"File: {Path.GetFileName(_labeledVolumePath)}");
                            
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), 
                                "✓ Ready to import. Unique colors will be identified as materials.");
                        }
                        else
                        {
                            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), 
                                "Warning: Selected TIFF file contains only one page.");
                        }
                    }
                    catch (Exception ex)
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error reading TIFF: {ex.Message}");
                    }
                }
                else if (!_labeledIsMultiPageTiff && Directory.Exists(_labeledVolumePath))
                {
                    var (count, totalSize) = CountImagesInFolder(_labeledVolumePath);
                    ImGui.Text("Folder Information:");
                    ImGui.BulletText($"Image Count: {count}");
                    ImGui.BulletText($"Total Size: {totalSize / (1024 * 1024)} MB");
                    
                    if (count > 0)
                    {
                        ImGui.Spacing();
                        ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), 
                            "✓ Ready to import. Unique colors will be identified as materials.");
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), 
                            "Warning: No supported image files found in this folder.");
                    }
                }
            }
        }

        private void DrawSingleImageOptions()
        {
            ImGui.Text("Image File:");
            ImGui.InputText("##ImagePath", ref _singleImagePath, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##ImageFile"))
            {
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
                ImGui.SetTooltip("Reduces dataset size by averaging voxels. A factor of 2 makes the data 8 times smaller.");
            }

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

        private void Draw3DObjectOptions()
        {
            ImGui.Text("3D Model File:");
            ImGui.InputText("##3DPath", ref _mesh3DPath, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##3DFile"))
            {
                string[] modelExtensions = { ".obj", ".stl" };
                _mesh3DDialog.Open(null, modelExtensions);
            }

            ImGui.Spacing();
            ImGui.Text("Scale Factor:");
            ImGui.SetNextItemWidth(150);
            ImGui.InputFloat("##3DScale", ref _mesh3DScale, 0.1f, 1.0f, "%.3f");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Scale factor to apply when loading the model. 1.0 = original size.");
            }

            if (!string.IsNullOrEmpty(_mesh3DPath) && File.Exists(_mesh3DPath))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                try
                {
                    var fileInfo = new FileInfo(_mesh3DPath);
                    string extension = fileInfo.Extension.ToLower();
                    
                    ImGui.Text("Model Information:");
                    ImGui.BulletText($"File: {Path.GetFileName(_mesh3DPath)}");
                    ImGui.BulletText($"Format: {extension.ToUpper().TrimStart('.')}");
                    ImGui.BulletText($"Size: {fileInfo.Length / 1024} KB");
                    
                    if (extension == ".obj" || extension == ".stl")
                    {
                        ImGui.BulletText("Supported format ✓");
                        ImGui.TextColored(new Vector4(0, 1, 0, 1), "Ready to import");
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), "Unsupported format");
                    }
                }
                catch (Exception ex)
                {
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error reading file: {ex.Message}");
                }
            }
        }

        private void DrawButtons()
        {
            bool canImport = false;

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
                case 4: // 3D Object
                    canImport = !string.IsNullOrEmpty(_mesh3DPath) && File.Exists(_mesh3DPath);
                    break;
                case 5: // Labeled Volume
                    if (_labeledIsMultiPageTiff)
                    {
                        canImport = !string.IsNullOrEmpty(_labeledVolumePath) && File.Exists(_labeledVolumePath) && 
                                   ImageLoader.IsMultiPageTiff(_labeledVolumePath);
                    }
                    else
                    {
                        canImport = !string.IsNullOrEmpty(_labeledVolumePath) && Directory.Exists(_labeledVolumePath);
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
                case 4: // 3D Object
                    _currentState = ImportState.Processing;
                    _importTask = Import3DObject();
                    break;
                case 5: // Labeled Volume
                    _currentState = ImportState.Processing;
                    _importTask = ImportLabeledVolume();
                    break;
            }
        }

        private async Task ImportLabeledVolume()
        {
            _statusText = "Loading labeled volume...";
            _progress = 0.1f;

            await Task.Run(async () =>
            {
                try
                {
                    string name = _labeledIsMultiPageTiff ? 
                        Path.GetFileNameWithoutExtension(_labeledVolumePath) : 
                        Path.GetFileName(_labeledVolumePath);

                    double pixelSizeMeters = _labeledPixelSizeUnit == 0 ? 
                        _labeledPixelSize * 1e-6 :  // micrometers to meters
                        _labeledPixelSize * 1e-3;   // millimeters to meters

                    // Load the labeled volume
                    var progressReporter = new Progress<float>(p =>
                    {
                        _progress = p;
                        _statusText = $"Processing labeled images... {(int)(p * 100)}%";
                    });

                    var (grayscaleVolume, labelVolume, materials) = await LabeledVolumeLoader.LoadLabeledVolumeAsync(
                        _labeledVolumePath, 
                        pixelSizeMeters, 
                        false, // Don't use memory mapping for now
                        progressReporter,
                        name);

                    _progress = 0.9f;
                    _statusText = "Creating dataset...";

                    // Create the dataset
                    double pixelSizeMicrons = _labeledPixelSizeUnit == 0 ? _labeledPixelSize : _labeledPixelSize * 1000;
                    
                    _pendingLabeledDataset = new CtImageStackDataset($"{name} (Labeled)", _labeledVolumePath)
                    {
                        Width = grayscaleVolume.Width,
                        Height = grayscaleVolume.Height,
                        Depth = grayscaleVolume.Depth,
                        PixelSize = (float)pixelSizeMicrons,
                        SliceThickness = (float)pixelSizeMicrons,
                        Unit = "µm",
                        BinningSize = 1
                    };

                    // Assign the materials to the dataset
                    _pendingLabeledDataset.Materials = materials;

                    // Save materials to file so they persist
                    _pendingLabeledDataset.SaveMaterials();

                    _progress = 1.0f;
                    _statusText = $"Labeled volume imported successfully! Found {materials.Count} unique materials.";
                }
                catch (Exception ex)
                {
                    _statusText = $"Error: {ex.Message}";
                    Logger.LogError($"[ImportDataModal] Error importing labeled volume: {ex}");
                    throw;
                }
            });
        }

        private async Task Import3DObject()
        {
            _statusText = "Loading 3D model...";
            _progress = 0.1f;

            await Task.Run(() =>
            {
                try
                {
                    var fileName = Path.GetFileName(_mesh3DPath);
                    var dataset = new Mesh3DDataset(Path.GetFileNameWithoutExtension(fileName), _mesh3DPath);
                    dataset.Scale = _mesh3DScale;

                    _progress = 0.3f;
                    _statusText = "Reading model geometry...";

                    dataset.Load();

                    _progress = 0.8f;
                    _statusText = "Adding to project...";

                    _pendingMesh3DDataset = dataset;

                    _progress = 1.0f;
                    _statusText = $"3D model imported successfully! ({dataset.VertexCount} vertices, {dataset.FaceCount} faces)";
                }
                catch (Exception ex)
                {
                    _statusText = $"Error: {ex.Message}";
                    throw;
                }
            });
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

                    var imageInfo = ImageLoader.LoadImageInfo(_singleImagePath);
                    dataset.Width = imageInfo.Width;
                    dataset.Height = imageInfo.Height;
                    dataset.BitDepth = imageInfo.BitsPerChannel * imageInfo.Channels;

                    if (_singleImagePixelSize > 0)
                    {
                        dataset.PixelSize = _singleImagePixelSizeUnit == 0 ? _singleImagePixelSize : _singleImagePixelSize * 1000;
                        dataset.Unit = "µm";
                    }

                    _progress = 0.8f;
                    _statusText = "Adding to project...";

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

                if (_pendingMesh3DDataset != null)
                {
                    ProjectManager.Instance.AddDataset(_pendingMesh3DDataset);
                    _pendingMesh3DDataset = null;
                }

                if (_pendingLegacyDataset != null)
                {
                    ProjectManager.Instance.AddDataset(_pendingLegacyDataset);

                    if (_pendingStreamingDataset != null)
                    {
                        _pendingStreamingDataset.EditablePartner = _pendingLegacyDataset;
                        ProjectManager.Instance.AddDataset(_pendingStreamingDataset);
                    }

                    _pendingLegacyDataset = null;
                    _pendingStreamingDataset = null;
                }

                if (_pendingLabeledDataset != null)
                {
                    ProjectManager.Instance.AddDataset(_pendingLabeledDataset);
                    _pendingLabeledDataset = null;
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

            _statusText = "Loading volume data...";
            var binnedVolume = await CTStackLoader.LoadCTStackAsync(_ctPath, GetPixelSizeInMeters(), _ctBinningFactor, false,
                new Progress<float>(p => { _progress = p * 0.5f; _statusText = $"Step 1/2: Loading and processing images... {(int)(p * 100)}%"; }), name);

            if (binnedVolume == null || binnedVolume.Width == 0 || binnedVolume.Height == 0 || binnedVolume.Depth == 0)
            {
                throw new InvalidOperationException("Failed to load volume data from TIFF file");
            }

            Logger.Log($"[ImportDataModal] Loaded volume for conversion: {binnedVolume.Width}×{binnedVolume.Height}×{binnedVolume.Depth}");

            if (!File.Exists(gvtPath))
            {
                _statusText = "Converting to optimized format...";
                
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

                var volume = await CTStackLoader.LoadCTStackAsync(_ctPath, GetPixelSizeInMeters(), _ctBinningFactor, false, progressReporter, name);

                double pixelSizeMicrons = (_ctPixelSizeUnit == 0 ? _ctPixelSize : _ctPixelSize * 1000) * _ctBinningFactor;

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
            _mesh3DPath = "";
            _mesh3DScale = 1.0f;
            _labeledVolumePath = "";
            _labeledIsMultiPageTiff = false;
            _labeledPixelSize = 1.0f;
            _labeledPixelSizeUnit = 0;
            _importTask = null;
            _progress = 0;
            _statusText = "";
            _currentState = ImportState.Idle;
            _pendingSingleImageDataset = null;
            _pendingStreamingDataset = null;
            _pendingLegacyDataset = null;
            _pendingMesh3DDataset = null;
            _pendingLabeledDataset = null;
            _shouldOpenOrganizer = false;
        }
    }
}