// GeoscientistToolkit/UI/ImportDataModal.cs - Updated with Acoustic Volume Import
using System;
using System.Data;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Loaders;
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
        private readonly ImGuiFileDialog _tiffDialog;
        private readonly ImGuiFileDialog _mesh3DDialog;
        private readonly ImGuiFileDialog _tableDialog;
        private readonly ImGuiFileDialog _gisDialog;
        private readonly ImGuiFileDialog _acousticDialog;
        private readonly ImageStackOrganizerDialog _organizerDialog;

        // Loaders
        private readonly SingleImageLoader _singleImageLoader;
        private readonly ImageFolderLoader _imageFolderLoader;
        private readonly CTStackLoaderWrapper _ctStackLoader;
        private readonly Mesh3DLoader _mesh3DLoader;
        private readonly LabeledVolumeLoaderWrapper _labeledVolumeLoader;
        private readonly TableLoader _tableLoader;
        private readonly GISLoader _gisLoader;
        private readonly AcousticVolumeLoader _acousticVolumeLoader;

        private int _selectedDatasetTypeIndex = 0;
        private readonly string[] _datasetTypeNames = {
            "Single Image",
            "Image Folder (Group)",
            "CT Image Stack (Optimized for 3D Streaming)",
            "CT Image Stack (Legacy for 2D Editing)",
            "3D Object (OBJ/STL)",
            "Labeled Volume Stack (Color-coded Materials)",
            "Table/Spreadsheet (CSV/TSV)",
            "GIS Map (Shapefile/GeoJSON/KML)",
            "Acoustic Volume (Simulation Results)"
        };

        private readonly string[] _pixelSizeUnits = { "µm", "mm" };
        private bool _shouldOpenOrganizer = false;
        private float _progress;
        private string _statusText = "";
        private Task<Dataset> _importTask;
        private Dataset _pendingDataset;

        public ImportDataModal()
        {
            // Initialize dialogs
            _folderDialog = new ImGuiFileDialog("ImportFolderDialog", FileDialogType.OpenDirectory, "Select Folder");
            _fileDialog = new ImGuiFileDialog("ImportFileDialog", FileDialogType.OpenFile, "Select File");
            _tiffDialog = new ImGuiFileDialog("ImportTiffDialog", FileDialogType.OpenFile, "Select TIFF File");
            _mesh3DDialog = new ImGuiFileDialog("Import3DDialog", FileDialogType.OpenFile, "Select 3D Model");
            _tableDialog = new ImGuiFileDialog("ImportTableDialog", FileDialogType.OpenFile, "Select Table File");
            _gisDialog = new ImGuiFileDialog("ImportGISDialog", FileDialogType.OpenFile, "Select GIS File");
            _acousticDialog = new ImGuiFileDialog("ImportAcousticDialog", FileDialogType.OpenDirectory, "Select Acoustic Volume Directory");
            _organizerDialog = new ImageStackOrganizerDialog();

            // Initialize loaders
            _singleImageLoader = new SingleImageLoader();
            _imageFolderLoader = new ImageFolderLoader();
            _ctStackLoader = new CTStackLoaderWrapper();
            _mesh3DLoader = new Mesh3DLoader();
            _labeledVolumeLoader = new LabeledVolumeLoaderWrapper();
            _tableLoader = new TableLoader();
            _gisLoader = new GISLoader();
            _acousticVolumeLoader = new AcousticVolumeLoader();
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
                _organizerDialog.Open(_imageFolderLoader.FolderPath);
                return;
            }

            // Handle dialog submissions
            HandleDialogSubmissions();

            ImGui.SetNextWindowSize(new Vector2(700, 600), ImGuiCond.FirstUseEver);
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

        private void HandleDialogSubmissions()
        {
            if (_folderDialog.Submit())
            {
                switch (_selectedDatasetTypeIndex)
                {
                    case 1: // Image Folder
                        _imageFolderLoader.FolderPath = _folderDialog.SelectedPath;
                        break;
                    case 2: // CT Stack Optimized
                    case 3: // CT Stack Legacy
                        if (!_ctStackLoader.IsMultiPageTiff)
                        {
                            _ctStackLoader.SourcePath = _folderDialog.SelectedPath;
                        }
                        break;
                    case 5: // Labeled Volume
                        if (!_labeledVolumeLoader.IsMultiPageTiff)
                        {
                            _labeledVolumeLoader.SourcePath = _folderDialog.SelectedPath;
                        }
                        break;
                }
            }

            if (_acousticDialog.Submit())
            {
                _acousticVolumeLoader.DirectoryPath = _acousticDialog.SelectedPath;
            }

            if (_fileDialog.Submit())
            {
                _singleImageLoader.ImagePath = _fileDialog.SelectedPath;
            }

            if (_tiffDialog.Submit())
            {
                switch (_selectedDatasetTypeIndex)
                {
                    case 2: // CT Stack Optimized
                    case 3: // CT Stack Legacy
                        _ctStackLoader.SourcePath = _tiffDialog.SelectedPath;
                        _ctStackLoader.IsMultiPageTiff = true;
                        break;
                    case 5: // Labeled Volume
                        _labeledVolumeLoader.SourcePath = _tiffDialog.SelectedPath;
                        _labeledVolumeLoader.IsMultiPageTiff = true;
                        break;
                }
            }

            if (_mesh3DDialog.Submit())
            {
                _mesh3DLoader.ModelPath = _mesh3DDialog.SelectedPath;
            }

            if (_tableDialog.Submit())
            {
                _tableLoader.FilePath = _tableDialog.SelectedPath;
            }
            
            if (_gisDialog.Submit())
            {
                _gisLoader.FilePath = _gisDialog.SelectedPath;
            }
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
                    _ctStackLoader.Mode = CTStackLoaderWrapper.LoadMode.OptimizedFor3D;
                    DrawCTImageStackOptions();
                    break;
                case 3: // CT Image Stack (Legacy)
                    _ctStackLoader.Mode = CTStackLoaderWrapper.LoadMode.LegacyFor2D;
                    DrawCTImageStackOptions();
                    break;
                case 4: // 3D Object
                    Draw3DObjectOptions();
                    break;
                case 5: // Labeled Volume Stack
                    DrawLabeledVolumeOptions();
                    break;
                case 6: // Table/Spreadsheet
                    DrawTableOptions();
                    break;
                case 7: // GIS Map
                    DrawGISOptions();
                    break;
                case 8: // Acoustic Volume
                    DrawAcousticVolumeOptions();
                    break;
            }

            ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetFrameHeightWithSpacing() * 1.5f);
            ImGui.Separator();
            DrawButtons();
        }

        private void DrawAcousticVolumeOptions()
        {
            ImGui.TextWrapped("Import acoustic simulation results containing wave field data and time series. " +
                            "These datasets are typically exported from acoustic simulations and contain P-wave, S-wave, " +
                            "and combined wave field data along with time-series snapshots.");
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.Text("Acoustic Volume Directory:");
            var path = _acousticVolumeLoader.DirectoryPath ?? "";
            ImGui.InputText("##AcousticPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##AcousticFolder"))
            {
                _acousticDialog.Open();
            }
            
            var info = _acousticVolumeLoader.GetVolumeInfo();
            if (info != null && !string.IsNullOrEmpty(info.DirectoryName))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                
                if (info.IsValid)
                {
                    ImGui.Text("Volume Information:");
                    ImGui.BulletText($"Directory: {info.DirectoryName}");
                    ImGui.BulletText($"Total Size: {info.TotalSize / (1024 * 1024)} MB");
                    
                    ImGui.Spacing();
                    ImGui.Text("Available Data:");
                    
                    DrawCheckmark(info.HasMetadata, "Metadata");
                    DrawCheckmark(info.HasPWaveField, "P-Wave Field");
                    DrawCheckmark(info.HasSWaveField, "S-Wave Field");
                    DrawCheckmark(info.HasCombinedField, "Combined Wave Field");
                    
                    if (info.HasTimeSeries)
                    {
                        DrawCheckmark(true, $"Time Series ({info.TimeSeriesFrameCount} frames)");
                    }
                    else
                    {
                        DrawCheckmark(false, "Time Series");
                    }
                    
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), 
                        "✓ Ready to import acoustic volume");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), 
                        "⚠ Invalid acoustic volume directory");
                    
                    if (!string.IsNullOrEmpty(info.ErrorMessage))
                    {
                        ImGui.TextWrapped($"Error: {info.ErrorMessage}");
                    }
                    
                    ImGui.Spacing();
                    ImGui.Text("Expected structure:");
                    ImGui.BulletText("metadata.json (required)");
                    ImGui.BulletText("PWaveField.bin, SWaveField.bin, or CombinedField.bin");
                    ImGui.BulletText("TimeSeries/ folder with snapshot_*.bin files (optional)");
                }
            }
        }

        private void DrawSingleImageOptions()
        {
            ImGui.Text("Image File:");
            var path = _singleImageLoader.ImagePath;
            ImGui.InputText("##ImagePath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##ImageFile"))
            {
                string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", 
                    ".gif", ".hdr", ".pic", ".pnm", ".ppm", ".pgm", ".tif", ".tiff" };
                _fileDialog.Open(null, imageExtensions);
            }

            ImGui.Spacing();
            ImGui.Text("Pixel Size (optional):");
            ImGui.SetNextItemWidth(150);
            float pixelSize = _singleImageLoader.PixelSize;
            ImGui.InputFloat("##PixelSizeSingle", ref pixelSize, 0.1f, 1.0f, "%.3f");
            _singleImageLoader.PixelSize = pixelSize;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            int unitIndex = (int)_singleImageLoader.Unit;
            if (ImGui.Combo("##UnitSingle", ref unitIndex, _pixelSizeUnits, _pixelSizeUnits.Length))
            {
                _singleImageLoader.Unit = (PixelSizeUnit)unitIndex;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Set the physical size of each pixel. Leave as 1.0 if unknown.");
            }

            var info = _singleImageLoader.GetImageInfo();
            if (info != null)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.Text("Image Information:");
                ImGui.BulletText($"Resolution: {info.Width} x {info.Height}");
                ImGui.BulletText($"File: {info.FileName}");
                ImGui.BulletText($"Size: {info.FileSize / 1024} KB");
            }
        }

        private void DrawImageFolderOptions()
        {
            ImGui.Text("Image Folder:");
            var path = _imageFolderLoader.FolderPath;
            ImGui.InputText("##ImageFolderPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##ImageFolder"))
            {
                _folderDialog.Open();
            }

            ImGui.Spacing();
            ImGui.TextWrapped("This option allows you to load multiple images from a folder and organize them into groups.");

            var info = _imageFolderLoader.GetFolderInfo();
            if (info.ImageCount > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.Text("Folder Information:");
                ImGui.BulletText($"Image Count: {info.ImageCount}");
                ImGui.BulletText($"Total Size: {info.TotalSize / (1024 * 1024)} MB");
                ImGui.BulletText($"Folder: {info.FolderName}");
            }
        }

        private void DrawCTImageStackOptions()
        {
            ImGui.Text("CT Stack Source:");
            
            bool isFolderMode = !_ctStackLoader.IsMultiPageTiff;
            if (ImGui.RadioButton("Image Folder", isFolderMode))
            {
                _ctStackLoader.IsMultiPageTiff = false;
                _ctStackLoader.SourcePath = "";
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("Multi-Page TIFF File", !isFolderMode))
            {
                _ctStackLoader.IsMultiPageTiff = true;
                _ctStackLoader.SourcePath = "";
            }

            ImGui.Spacing();

            if (_ctStackLoader.IsMultiPageTiff)
            {
                ImGui.Text("TIFF File:");
                var path = _ctStackLoader.SourcePath;
                ImGui.InputText("##CTFilePath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##CTFile"))
                {
                    string[] tiffExtensions = { ".tif", ".tiff" };
                    _tiffDialog.Open(null, tiffExtensions);
                }
            }
            else
            {
                ImGui.Text("Image Folder:");
                var path = _ctStackLoader.SourcePath;
                ImGui.InputText("##CTFolderPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##CTFolder"))
                {
                    _folderDialog.Open();
                }
            }

            ImGui.Spacing();
            ImGui.Text("Voxel Size:");
            ImGui.SetNextItemWidth(150);
            float pixelSize = _ctStackLoader.PixelSize;
            ImGui.InputFloat("##PixelSizeCT", ref pixelSize, 0.1f, 1.0f, "%.3f");
            _ctStackLoader.PixelSize = pixelSize;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            int unitIndex = (int)_ctStackLoader.Unit;
            if (ImGui.Combo("##Unit", ref unitIndex, _pixelSizeUnits, _pixelSizeUnits.Length))
            {
                _ctStackLoader.Unit = (PixelSizeUnit)unitIndex;
            }

            ImGui.Spacing();
            ImGui.Text("3D Binning Factor:");
            ImGui.SetNextItemWidth(230);
            int[] binningOptions = { 1, 2, 4, 8 };
            string[] binningLabels = { "1×1×1 (None)", "2×2×2 (8x smaller)", "4×4×4 (64x smaller)", "8×8×8 (512x smaller)" };
            int binningIndex = Array.IndexOf(binningOptions, _ctStackLoader.BinningFactor);
            if (ImGui.Combo("##Binning", ref binningIndex, binningLabels, binningLabels.Length))
            {
                _ctStackLoader.BinningFactor = binningOptions[binningIndex];
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Reduces dataset size by averaging voxels. A factor of 2 makes the data 8 times smaller.");
            }

            var info = _ctStackLoader.GetStackInfo();
            if (info.SliceCount > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.Text("Stack Information:");
                ImGui.BulletText($"Slices: {info.SliceCount}");
                if (info.Width > 0)
                    ImGui.BulletText($"Resolution: {info.Width} x {info.Height}");
                ImGui.BulletText($"Total Size: {info.TotalSize / (1024 * 1024)} MB");
                if (!string.IsNullOrEmpty(info.FileName))
                    ImGui.BulletText($"File: {info.FileName}");
            }
        }

        private void Draw3DObjectOptions()
        {
            ImGui.Text("3D Model File:");
            var path = _mesh3DLoader.ModelPath;
            ImGui.InputText("##3DPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##3DFile"))
            {
                string[] modelExtensions = { ".obj", ".stl" };
                _mesh3DDialog.Open(null, modelExtensions);
            }

            ImGui.Spacing();
            ImGui.Text("Scale Factor:");
            ImGui.SetNextItemWidth(150);
            float scale = _mesh3DLoader.Scale;
            ImGui.InputFloat("##3DScale", ref scale, 0.1f, 1.0f, "%.3f");
            _mesh3DLoader.Scale = scale;
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Scale factor to apply when loading the model. 1.0 = original size.");
            }

            var info = _mesh3DLoader.GetModelInfo();
            if (info != null)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.Text("Model Information:");
                ImGui.BulletText($"File: {info.FileName}");
                ImGui.BulletText($"Format: {info.Format}");
                ImGui.BulletText($"Size: {info.FileSize / 1024} KB");
                
                if (info.IsSupported)
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "✓ Ready to import");
                }
            }
        }

        private void DrawLabeledVolumeOptions()
        {
            ImGui.TextWrapped("Import a stack of labeled images where each unique color represents a different material. " +
                            "This will automatically create materials and generate both label and grayscale volumes.");
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.Text("Labeled Stack Source:");
            
            bool isFolderMode = !_labeledVolumeLoader.IsMultiPageTiff;
            if (ImGui.RadioButton("Image Folder##Labeled", isFolderMode))
            {
                _labeledVolumeLoader.IsMultiPageTiff = false;
                _labeledVolumeLoader.SourcePath = "";
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("Multi-Page TIFF File##Labeled", !isFolderMode))
            {
                _labeledVolumeLoader.IsMultiPageTiff = true;
                _labeledVolumeLoader.SourcePath = "";
            }

            ImGui.Spacing();

            if (_labeledVolumeLoader.IsMultiPageTiff)
            {
                ImGui.Text("TIFF File:");
                var path = _labeledVolumeLoader.SourcePath;
                ImGui.InputText("##LabeledFilePath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##LabeledFile"))
                {
                    string[] tiffExtensions = { ".tif", ".tiff" };
                    _tiffDialog.Open(null, tiffExtensions);
                }
            }
            else
            {
                ImGui.Text("Image Folder:");
                var path = _labeledVolumeLoader.SourcePath;
                ImGui.InputText("##LabeledFolderPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##LabeledFolder"))
                {
                    _folderDialog.Open();
                }
            }

            ImGui.Spacing();
            ImGui.Text("Voxel Size:");
            ImGui.SetNextItemWidth(150);
            float pixelSize = _labeledVolumeLoader.PixelSize;
            ImGui.InputFloat("##PixelSizeLabeled", ref pixelSize, 0.1f, 1.0f, "%.3f");
            _labeledVolumeLoader.PixelSize = pixelSize;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            int unitIndex = (int)_labeledVolumeLoader.Unit;
            if (ImGui.Combo("##UnitLabeled", ref unitIndex, _pixelSizeUnits, _pixelSizeUnits.Length))
            {
                _labeledVolumeLoader.Unit = (PixelSizeUnit)unitIndex;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Physical size of each voxel in the volume.");
            }

            var info = _labeledVolumeLoader.GetVolumeInfo();
            if (info.IsReady)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.Text("Volume Information:");
                ImGui.BulletText($"Slices: {info.SliceCount}");
                if (info.Width > 0)
                    ImGui.BulletText($"Resolution: {info.Width} x {info.Height}");
                ImGui.BulletText($"Total Size: {info.TotalSize / (1024 * 1024)} MB");
                if (!string.IsNullOrEmpty(info.FileName))
                    ImGui.BulletText($"File: {info.FileName}");
                
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), 
                    "✓ Ready to import. Unique colors will be identified as materials.");
            }
        }

        private void DrawTableOptions()
        {
            ImGui.TextWrapped("Import tabular data from CSV, TSV, or text files. The data will be loaded into a table dataset " +
                            "where you can view, filter, sort, and analyze the data.");
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.Text("Table File:");
            var path = _tableLoader.FilePath;
            ImGui.InputText("##TablePath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##TableFile"))
            {
                string[] tableExtensions = { ".csv", ".tsv", ".txt", ".tab", ".dat" };
                _tableDialog.Open(null, tableExtensions);
            }

            ImGui.Spacing();
            
            ImGui.Text("File Format:");
            int format = (int)_tableLoader.Format;
            ImGui.RadioButton("Auto-detect", ref format, 0);
            ImGui.SameLine();
            ImGui.RadioButton("CSV (comma)", ref format, 1);
            ImGui.SameLine();
            ImGui.RadioButton("TSV (tab)", ref format, 2);
            _tableLoader.Format = (TableLoader.FileFormat)format;
            
            var info = _tableLoader.GetTableInfo();
            if (info != null)
            {
                ImGui.Text($"Detected: {info.DetectedDelimiter}");
            }
            
            ImGui.Spacing();
            bool hasHeaders = _tableLoader.HasHeaders;
            ImGui.Checkbox("First row contains headers", ref hasHeaders);
            _tableLoader.HasHeaders = hasHeaders;
            
            ImGui.Text("Text Encoding:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            if (ImGui.BeginCombo("##Encoding", _tableLoader.Encoding))
            {
                if (ImGui.Selectable("UTF-8", _tableLoader.Encoding == "UTF-8"))
                    _tableLoader.Encoding = "UTF-8";
                if (ImGui.Selectable("ASCII", _tableLoader.Encoding == "ASCII"))
                    _tableLoader.Encoding = "ASCII";
                if (ImGui.Selectable("UTF-16", _tableLoader.Encoding == "UTF-16"))
                    _tableLoader.Encoding = "UTF-16";
                if (ImGui.Selectable("Windows-1252", _tableLoader.Encoding == "Windows-1252"))
                    _tableLoader.Encoding = "Windows-1252";
                ImGui.EndCombo();
            }

            if (info != null)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.Text("File Information:");
                ImGui.BulletText($"File: {info.FileName}");
                ImGui.BulletText($"Size: {info.FileSize / 1024} KB");
                
                // Show preview
                var preview = _tableLoader.GetPreview();
                if (preview != null && preview.Rows.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.Text("Data Preview:");
                    
                    ImGui.BeginChild("TablePreview", new Vector2(0, 150), ImGuiChildFlags.Border);
                    
                    if (ImGui.BeginTable("PreviewTable", preview.Columns.Count, 
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY))
                    {
                        foreach (DataColumn col in preview.Columns)
                        {
                            ImGui.TableSetupColumn(col.ColumnName);
                        }
                        ImGui.TableHeadersRow();
                        
                        int rowsToShow = Math.Min(5, preview.Rows.Count);
                        for (int i = 0; i < rowsToShow; i++)
                        {
                            ImGui.TableNextRow();
                            for (int j = 0; j < preview.Columns.Count; j++)
                            {
                                ImGui.TableNextColumn();
                                ImGui.Text(preview.Rows[i][j]?.ToString() ?? "");
                            }
                        }
                        
                        ImGui.EndTable();
                    }
                    
                    ImGui.EndChild();
                    
                    ImGui.Text($"Preview showing {Math.Min(5, preview.Rows.Count)} of {preview.Rows.Count} rows, {preview.Columns.Count} columns");
                    ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import");
                }
            }
        }
        
        private void DrawGISOptions()
        {
            ImGui.TextWrapped("Import GIS data or create a new empty map for editing.");
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Create empty or load file
            bool createEmpty = _gisLoader.CreateEmpty;
            if (ImGui.RadioButton("Create Empty Map", createEmpty))
            {
                _gisLoader.CreateEmpty = true;
                _gisLoader.FilePath = "";
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("Load GIS File", !createEmpty))
            {
                _gisLoader.CreateEmpty = false;
            }
            
            ImGui.Spacing();
            
            if (_gisLoader.CreateEmpty)
            {
                ImGui.Text("Map Name:");
                ImGui.SetNextItemWidth(300);
                string name = _gisLoader.DatasetName ?? "New Map";
                if (ImGui.InputText("##MapName", ref name, 256))
                {
                    _gisLoader.DatasetName = name;
                }
                
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), 
                    "✓ Ready to create empty map");
            }
            else
            {
                ImGui.Text("GIS File:");
                var path = _gisLoader.FilePath ?? "";
                ImGui.InputText("##GISPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse...##GISFile"))
                {
                    string[] gisExtensions = { ".shp", ".geojson", ".json", ".kml", ".kmz", ".tif", ".tiff" };
                    _gisDialog.Open(null, gisExtensions);
                }
                
                var info = _gisLoader.GetFileInfo();
                if (info != null && !string.IsNullOrEmpty(info.FileName))
                {
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                    ImGui.Text("File Information:");
                    ImGui.BulletText($"File: {info.FileName}");
                    ImGui.BulletText($"Type: {info.Type}");
                    ImGui.BulletText($"Size: {info.FileSize / 1024} KB");
                    
                    if (info.Type == "Shapefile")
                    {
                        ImGui.Spacing();
                        ImGui.Text("Shapefile Components:");
                        DrawCheckmark(info.HasShx, ".shx (Index)");
                        DrawCheckmark(info.HasDbf, ".dbf (Attributes)");
                        DrawCheckmark(info.HasPrj, ".prj (Projection)");
                    }
                    
                    if (info.IsValid)
                    {
                        ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), 
                            "✓ Ready to import");
                    }
                }
            }
        }
        
        private void DrawCheckmark(bool hasComponent, string label)
        {
            if (hasComponent)
            {
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "✓");
            }
            else
            {
                ImGui.TextColored(new Vector4(1.0f, 0.0f, 0.0f, 1.0f), "✗");
            }
            ImGui.SameLine();
            ImGui.Text(label);
        }

        private void DrawButtons()
        {
            bool canImport = GetCurrentLoader()?.CanImport ?? false;

            if (ImGui.Button("Cancel", new Vector2(120, 0))) 
                _currentState = ImportState.Idle;
                
            ImGui.SameLine();

            if (!canImport) ImGui.BeginDisabled();
            if (ImGui.Button("Import", new Vector2(120, 0))) 
                HandleImportClick();
            if (!canImport) ImGui.EndDisabled();
        }

        private IDataLoader GetCurrentLoader()
        {
            return _selectedDatasetTypeIndex switch
            {
                0 => _singleImageLoader,
                1 => _imageFolderLoader,
                2 or 3 => _ctStackLoader,
                4 => _mesh3DLoader,
                5 => _labeledVolumeLoader,
                6 => _tableLoader,
                7 => _gisLoader,
                8 => _acousticVolumeLoader,
                _ => null
            };
        }

        private void HandleImportClick()
        {
            var loader = GetCurrentLoader();
            if (loader == null) return;

            if (_selectedDatasetTypeIndex == 1) // Image Folder - open organizer
            {
                _shouldOpenOrganizer = true;
                return;
            }

            _currentState = ImportState.Processing;
            var progress = new Progress<(float progress, string message)>(p =>
            {
                _progress = p.progress;
                _statusText = p.message;
            });

            _importTask = loader.LoadAsync(progress);
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
                // Handle special case for CT Stack Optimized mode
                if (_selectedDatasetTypeIndex == 2 && _ctStackLoader.StreamingDataset != null)
                {
                    ProjectManager.Instance.AddDataset(_ctStackLoader.LegacyDataset);
                    ProjectManager.Instance.AddDataset(_ctStackLoader.StreamingDataset);
                }
                else if (_importTask.Result != null)
                {
                    ProjectManager.Instance.AddDataset(_importTask.Result);
                }

                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Import successful!");
                if (ImGui.Button("Close")) _currentState = ImportState.Idle;
            }
        }

        private void ResetState()
        {
            // Reset all loaders
            _singleImageLoader.Reset();
            _imageFolderLoader.Reset();
            _ctStackLoader.Reset();
            _mesh3DLoader.Reset();
            _labeledVolumeLoader.Reset();
            _tableLoader.Reset();
            _gisLoader.Reset();
            _acousticVolumeLoader.Reset();

            // Reset state
            _importTask = null;
            _progress = 0;
            _statusText = "";
            _currentState = ImportState.Idle;
            _pendingDataset = null;
            _shouldOpenOrganizer = false;
        }
    }
}