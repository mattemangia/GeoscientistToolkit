// GeoscientistToolkit/UI/ImportDataModal.cs

using System.Data;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Loaders;
using GeoscientistToolkit.UI.Utils;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public class ImportDataModal
{
    private readonly ImGuiFileDialog _acousticDialog;
    private readonly AcousticVolumeLoader _acousticVolumeLoader;
    private readonly CTStackLoaderWrapper _ctStackLoader;

    private readonly string[] _datasetTypeNames =
    {
        "Single Image",
        "Image Folder (Group)",
        "CT Image Stack (Optimized for 3D Streaming)",
        "CT Image Stack (Legacy for 2D Editing)",
        "3D Object (OBJ/STL)",
        "Labeled Volume Stack (Color-coded Materials)",
        "Table/Spreadsheet (CSV/TSV)",
        "GIS Map (Vectorial Shapefile/Raster GeoJSON/Points KML)",
        "Acoustic Volume (Simulation Results)",
        "Segmentation/Labels (Standalone)",
        "Pore Network Model (PNM)",
        "Dual Pore Network Model (Dual PNM)",
        "Borehole Log (Binary)",
        "Borehole Log (LAS Format)",
        "2D Geology Profile (.2dgeo)",
        "Subsurface GIS Model (.subgis)",
        "Seismic Dataset (SEG-Y)",
        "PhysicoChem Reactor",
        "TOUGH2 Input File",
        "Video File (MP4/AVI/MOV)",
        "Audio File (WAV/MP3/OGG)",
        "Text Document (TXT/RTF)",
        "Slope Stability Results (Binary)"
    };

    private readonly ImGuiFileDialog _fileDialog;

    private readonly ImGuiFileDialog _folderDialog;
    private readonly ImGuiFileDialog _gisDialog;
    private readonly GISLoader _gisLoader;
    private readonly ImageFolderLoader _imageFolderLoader;
    private readonly LabeledVolumeLoaderWrapper _labeledVolumeLoader;
    private readonly ImGuiFileDialog _mesh3DDialog;
    private readonly Mesh3DLoader _mesh3DLoader;
    private readonly ImageStackOrganizerDialog _organizerDialog;

    private readonly string[] _pixelSizeUnits = { "µm", "mm" };
    private readonly ImGuiFileDialog _pnmDialog; // Added for PNM
    private readonly PNMLoader _pnmLoader; // Added for PNM
    private readonly ImGuiFileDialog _dualPnmDialog; // Added for Dual PNM
    private readonly DualPNMLoader _dualPnmLoader; // Added for Dual PNM
    private readonly ImGuiFileDialog _segmentationDialog;
    private readonly SegmentationLoader _segmentationLoader;

    // Loaders
    private readonly SingleImageLoader _singleImageLoader;
    private readonly ImGuiFileDialog _tableDialog;
    private readonly TableLoader _tableLoader;
    private readonly ImGuiFileDialog _tiffDialog;
    private readonly BoreholeBinaryLoader _boreholeBinaryLoader;
    private readonly ImGuiFileDialog _boreholeBinaryDialog;
    private readonly TwoDGeologyLoader _twoDGeologyLoader;
    private readonly ImGuiFileDialog _twoDGeologyDialog;
    private readonly SubsurfaceGISLoader _subsurfaceGisLoader;
    private readonly ImGuiFileDialog _subsurfaceGisDialog;
    private readonly LASLoader _lasLoader;
    private readonly ImGuiFileDialog _lasDialog;
    private readonly SeismicLoader _seismicLoader;
    private readonly ImGuiFileDialog _seismicDialog;
    private readonly PhysicoChemLoader _physicoChemLoader;
    private readonly ImGuiFileDialog _physicoChemDialog;
    private readonly Tough2Loader _tough2Loader;
    private readonly ImGuiFileDialog _tough2Dialog;
    private readonly VideoLoader _videoLoader;
    private readonly ImGuiFileDialog _videoDialog;
    private readonly AudioLoader _audioLoader;
    private readonly ImGuiFileDialog _audioDialog;
    private readonly TextLoader _textLoader;
    private readonly ImGuiFileDialog _textDialog;
    private readonly SlopeStabilityResultsBinaryLoader _slopeResultsLoader;
    private readonly ImGuiFileDialog _slopeResultsDialog;
    private ImportState _currentState = ImportState.Idle;
    private Task<Dataset> _importTask;
    private Dataset _pendingDataset;
    private float _progress;

    private int _selectedDatasetTypeIndex;
    private bool _shouldOpenOrganizer;
    private string _statusText = "";

    public ImportDataModal()
    {
        // Initialize dialogs
        _folderDialog = new ImGuiFileDialog("ImportFolderDialog", FileDialogType.OpenDirectory, "Select Folder");
        _fileDialog = new ImGuiFileDialog("ImportFileDialog", FileDialogType.OpenFile, "Select File");
        _tiffDialog = new ImGuiFileDialog("ImportTiffDialog", FileDialogType.OpenFile, "Select TIFF File");
        _mesh3DDialog = new ImGuiFileDialog("Import3DDialog", FileDialogType.OpenFile, "Select 3D Model");
        _tableDialog = new ImGuiFileDialog("ImportTableDialog", FileDialogType.OpenFile, "Select Table File");
        _gisDialog = new ImGuiFileDialog("ImportGISDialog", FileDialogType.OpenFile, "Select GIS File");
        _acousticDialog = new ImGuiFileDialog("ImportAcousticDialog", FileDialogType.OpenDirectory,
            "Select Acoustic Volume Directory");
        _segmentationDialog = new ImGuiFileDialog("ImportSegmentationDialog", FileDialogType.OpenFile,
            "Select Segmentation File");
        _pnmDialog =
            new ImGuiFileDialog("ImportPNMDialog", FileDialogType.OpenFile, "Select PNM File"); // Added for PNM
        _dualPnmDialog =
            new ImGuiFileDialog("ImportDualPNMDialog", FileDialogType.OpenFile, "Select Dual PNM File"); // Added for Dual PNM
        _boreholeBinaryDialog = new ImGuiFileDialog("ImportBoreholeBinaryDialog", FileDialogType.OpenFile, "Select Borehole Binary File");
        _lasDialog = new ImGuiFileDialog("ImportLASDialog", FileDialogType.OpenFile, "Select LAS Log File");
        _twoDGeologyDialog = new ImGuiFileDialog("Import2DGeologyDialog", FileDialogType.OpenFile, "Select 2D Geology File");
        _subsurfaceGisDialog = new ImGuiFileDialog("ImportSubsurfaceGISDialog", FileDialogType.OpenFile, "Select Subsurface GIS File");
        _seismicDialog = new ImGuiFileDialog("ImportSeismicDialog", FileDialogType.OpenFile, "Select SEG-Y File");
        _physicoChemDialog = new ImGuiFileDialog("ImportPhysicoChemDialog", FileDialogType.OpenFile, "Select PhysicoChem File");
        _tough2Dialog = new ImGuiFileDialog("ImportTough2Dialog", FileDialogType.OpenFile, "Select TOUGH2 File");
        _videoDialog = new ImGuiFileDialog("ImportVideoDialog", FileDialogType.OpenFile, "Select Video File");
        _audioDialog = new ImGuiFileDialog("ImportAudioDialog", FileDialogType.OpenFile, "Select Audio File");
        _textDialog = new ImGuiFileDialog("ImportTextDialog", FileDialogType.OpenFile, "Select Text File");
        _slopeResultsDialog = new ImGuiFileDialog("ImportSlopeResultsDialog", FileDialogType.OpenFile,
            "Select Slope Stability Results");
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
        _segmentationLoader = new SegmentationLoader();
        _pnmLoader = new PNMLoader(); // Added for PNM
        _dualPnmLoader = new DualPNMLoader(); // Added for Dual PNM
        _boreholeBinaryLoader = new BoreholeBinaryLoader();
        _lasLoader = new LASLoader();
        _twoDGeologyLoader = new TwoDGeologyLoader();
        _subsurfaceGisLoader = new SubsurfaceGISLoader();
        _seismicLoader = new SeismicLoader();
        _physicoChemLoader = new PhysicoChemLoader();
        _tough2Loader = new Tough2Loader();
        _videoLoader = new VideoLoader();
        _audioLoader = new AudioLoader();
        _textLoader = new TextLoader();
        _slopeResultsLoader = new SlopeStabilityResultsBinaryLoader();
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
            if (!_organizerDialog.IsOpen) _currentState = ImportState.Idle;
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

        var modalOpen = true;
        if (ImGui.Begin("Import Data", ref modalOpen, ImGuiWindowFlags.NoDocking))
        {
            if (_currentState == ImportState.Processing)
                DrawProgress();
            else
                DrawOptions();
            ImGui.End();
        }

        if (!modalOpen) _currentState = ImportState.Idle;
    }

    private void HandleDialogSubmissions()
    {
        if (_folderDialog.Submit())
            switch (_selectedDatasetTypeIndex)
            {
                case 1: // Image Folder
                    _imageFolderLoader.FolderPath = _folderDialog.SelectedPath;
                    break;
                case 2: // CT Stack Optimized
                case 3: // CT Stack Legacy
                    if (!_ctStackLoader.IsMultiPageTiff) _ctStackLoader.SourcePath = _folderDialog.SelectedPath;
                    break;
                case 5: // Labeled Volume
                    if (!_labeledVolumeLoader.IsMultiPageTiff)
                        _labeledVolumeLoader.SourcePath = _folderDialog.SelectedPath;
                    break;
            }

        if (_acousticDialog.Submit()) _acousticVolumeLoader.DirectoryPath = _acousticDialog.SelectedPath;

        if (_segmentationDialog.Submit()) _segmentationLoader.SegmentationPath = _segmentationDialog.SelectedPath;

        if (_fileDialog.Submit()) _singleImageLoader.ImagePath = _fileDialog.SelectedPath;
        
        if (_boreholeBinaryDialog.Submit()) _boreholeBinaryLoader.FilePath = _boreholeBinaryDialog.SelectedPath;
        
        if (_lasDialog.Submit()) _lasLoader.FilePath = _lasDialog.SelectedPath;

        if (_tiffDialog.Submit())
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

        if (_mesh3DDialog.Submit()) _mesh3DLoader.ModelPath = _mesh3DDialog.SelectedPath;

        if (_tableDialog.Submit()) _tableLoader.FilePath = _tableDialog.SelectedPath;

        if (_gisDialog.Submit()) _gisLoader.FilePath = _gisDialog.SelectedPath;

        // Added for PNM
        if (_pnmDialog.Submit()) _pnmLoader.FilePath = _pnmDialog.SelectedPath;

        // Added for Dual PNM
        if (_dualPnmDialog.Submit()) _dualPnmLoader.FilePath = _dualPnmDialog.SelectedPath;

        // Added for PhysicoChem
        if (_physicoChemDialog.Submit()) _physicoChemLoader.FilePath = _physicoChemDialog.SelectedPath;

        // Added for TOUGH2
        if (_tough2Dialog.Submit()) _tough2Loader.FilePath = _tough2Dialog.SelectedPath;

        // Added for 2D Geology
        if (_twoDGeologyDialog.Submit()) _twoDGeologyLoader.FilePath = _twoDGeologyDialog.SelectedPath;

        // Added for Subsurface GIS
        if (_subsurfaceGisDialog.Submit()) _subsurfaceGisLoader.FilePath = _subsurfaceGisDialog.SelectedPath;

        // Added for Seismic
        if (_seismicDialog.Submit()) _seismicLoader.FilePath = _seismicDialog.SelectedPath;

        // Added for Video
        if (_videoDialog.Submit()) _videoLoader.VideoPath = _videoDialog.SelectedPath;

        // Added for Audio
        if (_audioDialog.Submit()) _audioLoader.AudioPath = _audioDialog.SelectedPath;
        if (_textDialog.Submit()) _textLoader.TextPath = _textDialog.SelectedPath;
        if (_slopeResultsDialog.Submit()) _slopeResultsLoader.FilePath = _slopeResultsDialog.SelectedPath;
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
            case 9: // Segmentation/Labels
                DrawSegmentationOptions();
                break;
            case 10: // PNM
                DrawPNMOptions();
                break;
            case 11: // Dual PNM
                DrawDualPNMOptions();
                break;
            case 12: // Borehole Binary
                DrawBoreholeBinaryOptions();
                break;
            case 13: // LAS Log Data
                DrawLASOptions();
                break;
            case 14: // 2D Geology Profile
                DrawTwoDGeologyOptions();
                break;
            case 15: // Subsurface GIS Model
                DrawSubsurfaceGISOptions();
                break;
            case 16: // Seismic Dataset (SEG-Y)
                DrawSeismicOptions();
                break;
            case 17: // PhysicoChem Reactor
                DrawPhysicoChemOptions();
                break;
            case 18: // TOUGH2 Input File
                DrawTough2Options();
                break;
            case 19: // Video File
                DrawVideoOptions();
                break;
            case 20: // Audio File
                DrawAudioOptions();
                break;
            case 21: // Text Document
                DrawTextOptions();
                break;
            case 22: // Slope Stability Results
                DrawSlopeStabilityResultsOptions();
                break;
        }

        ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetFrameHeightWithSpacing() * 1.5f);
        ImGui.Separator();
        DrawButtons();
    }
    
    private void DrawBoreholeBinaryOptions()
    {
        ImGui.TextWrapped("Import a borehole log dataset from a custom binary format (.bhb). " +
                          "This format contains all well information, lithology units, and parameter tracks.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Borehole Binary File (.bhb):");
        var path = _boreholeBinaryLoader.FilePath ?? "";
        ImGui.InputText("##BoreholePath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##BoreholeFile"))
        {
            string[] bhbExtensions = { ".bhb" };
            _boreholeBinaryDialog.Open(null, bhbExtensions);
        }

        if (!string.IsNullOrEmpty(_boreholeBinaryLoader.FilePath) && File.Exists(_boreholeBinaryLoader.FilePath))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("File Information:");
            var info = new FileInfo(_boreholeBinaryLoader.FilePath);
            ImGui.BulletText($"File: {info.Name}");
            ImGui.BulletText($"Size: {info.Length / 1024} KB");
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import borehole dataset");
        }
    }

    private void DrawLASOptions()
    {
        ImGui.TextWrapped("Import well log data from LAS (Log ASCII Standard) format files. " +
                          "LAS files contain curves such as gamma ray, resistivity, density, and other well logging measurements.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("LAS File (.las):");
        var path = _lasLoader.FilePath ?? "";
        ImGui.InputText("##LASPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##LASFile"))
        {
            string[] lasExtensions = { ".las", ".LAS" };
            _lasDialog.Open(null, lasExtensions);
        }

        if (_lasLoader.ParsedData != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (_lasLoader.CanImport)
            {
                ImGui.Text("LAS File Information:");
                
                var lasData = _lasLoader.ParsedData;
                
                // Version info
                ImGui.BulletText($"Version: {lasData.Version}");
                ImGui.BulletText($"Wrap Mode: {(lasData.Wrap ? "Yes" : "No")}");
                
                // Well information
                if (lasData.WellInfo.TryGetValue("WELL", out var wellName))
                    ImGui.BulletText($"Well: {wellName}");
                
                if (lasData.WellInfo.TryGetValue("FLD", out var field))
                    ImGui.BulletText($"Field: {field}");
                
                if (lasData.WellInfo.TryGetValue("LOC", out var location))
                    ImGui.BulletText($"Location: {location}");
                
                // Data statistics
                ImGui.Spacing();
                ImGui.Text("Log Data:");
                ImGui.BulletText($"Curves: {lasData.Curves.Count}");
                ImGui.BulletText($"Data Points: {lasData.DataRows.Count}");
                
                // Show some curve names
                if (lasData.Curves.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.Text("Available Curves:");
                    var curvesToShow = Math.Min(8, lasData.Curves.Count);
                    for (int i = 0; i < curvesToShow; i++)
                    {
                        var curve = lasData.Curves[i];
                        var curveText = string.IsNullOrEmpty(curve.Unit) 
                            ? curve.Mnemonic 
                            : $"{curve.Mnemonic} ({curve.Unit})";
                        ImGui.BulletText(curveText);
                    }
                    
                    if (lasData.Curves.Count > curvesToShow)
                        ImGui.BulletText($"... and {lasData.Curves.Count - curvesToShow} more");
                }
                
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), _lasLoader.ValidationMessage);
            }
            else
            {
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f), _lasLoader.ValidationMessage);
            }
        }
    }

    private void DrawTwoDGeologyOptions()
    {
        ImGui.TextWrapped("Import or create a 2D geological cross-section profile (.2dgeo format). " +
                          "This displays geological formations, faults, and topography in a cross-sectional view.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("2D Geology File (.2dgeo):");
        var path = _twoDGeologyLoader.FilePath ?? "";
        ImGui.InputText("##2DGeoPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##2DGeoFile"))
        {
            string[] geoExtensions = { ".2dgeo" };
            _twoDGeologyDialog.Open(null, geoExtensions);
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Note: You can also create a new empty 2D geology profile from the GIS Tools menu.");

        if (!string.IsNullOrEmpty(_twoDGeologyLoader.FilePath) && File.Exists(_twoDGeologyLoader.FilePath))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("File Information:");
            var info = new FileInfo(_twoDGeologyLoader.FilePath);
            ImGui.BulletText($"File: {info.Name}");
            ImGui.BulletText($"Size: {info.Length / 1024} KB");
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import 2D geology profile");
        }
    }

    private void DrawSubsurfaceGISOptions()
    {
        ImGui.TextWrapped("Import a 3D subsurface geological model (.subgis format). " +
                          "This contains voxel grids with lithology, layer boundaries, and interpolated properties " +
                          "from borehole data including geothermal simulation results.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Subsurface GIS File (.subgis):");
        var path = _subsurfaceGisLoader.FilePath ?? "";
        ImGui.InputText("##SubsurfaceGISPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##SubsurfaceGISFile"))
        {
            string[] subsurfaceExtensions = { ".subgis", ".json" };
            _subsurfaceGisDialog.Open(null, subsurfaceExtensions);
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Note: Subsurface GIS models are typically generated from borehole datasets " +
                          "using the Multi-Borehole Tools. You can create them from the Tools panel when " +
                          "multiple boreholes are loaded.");

        if (!string.IsNullOrEmpty(_subsurfaceGisLoader.FilePath) && File.Exists(_subsurfaceGisLoader.FilePath))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("File Information:");
            var info = new FileInfo(_subsurfaceGisLoader.FilePath);
            ImGui.BulletText($"File: {info.Name}");
            ImGui.BulletText($"Size: {info.Length / 1024} KB");
            
            var fileInfo = _subsurfaceGisLoader.GetFileInfo();
            if (fileInfo != null)
            {
                var infoObj = fileInfo as dynamic;
                if (infoObj != null)
                {
                    try
                    {
                        ImGui.BulletText($"Valid: {(infoObj.IsValid ? "Yes" : "No")}");
                    }
                    catch
                    {
                        // If dynamic access fails, just show basic info
                    }
                }
            }
            
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import subsurface GIS model");
        }
    }

    private void DrawSeismicOptions()
    {
        ImGui.TextWrapped("Import seismic reflection/refraction data from SEG-Y format files. " +
                          "SEG-Y is the standard format for storing seismic data with trace headers and sample data.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("SEG-Y File (.sgy, .segy):");
        var path = _seismicLoader.FilePath ?? "";
        ImGui.InputText("##SeismicPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##SeismicFile"))
        {
            string[] seismicExtensions = { ".sgy", ".segy", ".seg-y" };
            _seismicDialog.Open(null, seismicExtensions);
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Features:");
        ImGui.BulletText("Supports both IBM and IEEE floating point formats");
        ImGui.BulletText("Wiggle trace and variable area display");
        ImGui.BulletText("Multiple color maps for visualization");
        ImGui.BulletText("Create and manage line packages for trace grouping");
        ImGui.BulletText("Semi-automatic package detection tools");
        ImGui.BulletText("Export seismic sections as images");

        if (!string.IsNullOrEmpty(_seismicLoader.FilePath) && File.Exists(_seismicLoader.FilePath))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("File Information:");
            var info = new FileInfo(_seismicLoader.FilePath);
            ImGui.BulletText($"File: {info.Name}");
            ImGui.BulletText($"Size: {info.Length / (1024 * 1024)} MB");

            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import SEG-Y dataset");
        }
    }

    private void DrawPhysicoChemOptions()
    {
        ImGui.TextWrapped("Import a PhysicoChem reactor simulation dataset. This contains reactor geometry, " +
                          "material properties, boundary conditions, force fields, and simulation results.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("PhysicoChem File (.physicochem):");
        var path = _physicoChemLoader.FilePath ?? "";
        ImGui.InputText("##PhysicoChemPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##PhysicoChemFile"))
        {
            string[] pcExtensions = { ".physicochem", ".json" };
            _physicoChemDialog.Open(null, pcExtensions);
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Features:");
        ImGui.BulletText("3D reactor domain visualization with multiple geometries");
        ImGui.BulletText("Material properties and initial conditions");
        ImGui.BulletText("Boundary conditions (Fixed Value, Flux, Inlet/Outlet)");
        ImGui.BulletText("Force fields (Gravity, Vortex, Centrifugal)");
        ImGui.BulletText("Nucleation sites for mineral precipitation");
        ImGui.BulletText("Multiphysics simulation (Reactive Transport, Heat Transfer, Flow)");
        ImGui.BulletText("GPU-accelerated solver with OpenCL");

        if (!string.IsNullOrEmpty(_physicoChemLoader.FilePath) && File.Exists(_physicoChemLoader.FilePath))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("File Information:");
            var info = new FileInfo(_physicoChemLoader.FilePath);
            ImGui.BulletText($"File: {info.Name}");
            ImGui.BulletText($"Size: {info.Length / 1024} KB");

            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import PhysicoChem dataset");
        }
    }

    private void DrawTough2Options()
    {
        ImGui.TextWrapped("Import a TOUGH2 multiphysics subsurface flow simulation input file. TOUGH2 is a widely used " +
                          "numerical simulator for fluid and heat flow in porous and fractured media.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("TOUGH2 Input File:");
        var path = _tough2Loader.FilePath ?? "";
        ImGui.InputText("##Tough2Path", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##Tough2File"))
        {
            string[] tough2Extensions = { ".dat", ".inp", ".tough2", ".txt" };
            _tough2Dialog.Open(null, tough2Extensions);
        }

        ImGui.Spacing();
        ImGui.TextWrapped("TOUGH2 file contains:");
        ImGui.BulletText("ROCKS - Material properties (porosity, permeability, density)");
        ImGui.BulletText("ELEME - Mesh element definitions with coordinates");
        ImGui.BulletText("CONNE - Element connections (grid topology)");
        ImGui.BulletText("INCON - Initial conditions (pressure, temperature, saturation)");
        ImGui.BulletText("GENER - Sources and sinks (boundary conditions)");
        ImGui.BulletText("PARAM - Simulation parameters (time steps, convergence)");

        ImGui.Spacing();
        ImGui.TextWrapped("The imported data will be converted to a PhysicoChemDataset with equivalent domains, " +
                          "materials, boundary conditions, and simulation parameters.");

        if (!string.IsNullOrEmpty(_tough2Loader.FilePath) && File.Exists(_tough2Loader.FilePath))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("File Information:");
            var info = new FileInfo(_tough2Loader.FilePath);
            ImGui.BulletText($"File: {info.Name}");
            ImGui.BulletText($"Size: {info.Length / 1024} KB");

            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import TOUGH2 dataset");
        }
    }

    private void DrawVideoOptions()
    {
        ImGui.TextWrapped("Import video files for analysis and visualization. Supports MP4, AVI, MOV, MKV, and other common formats.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Video File:");
        var path = _videoLoader.VideoPath ?? "";
        ImGui.InputText("##VideoPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##VideoFile"))
        {
            string[] videoExtensions = { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".flv", ".wmv", ".m4v" };
            _videoDialog.Open(null, videoExtensions);
        }

        ImGui.Spacing();
        bool generateThumbnail = _videoLoader.GenerateThumbnail;
        if (ImGui.Checkbox("Generate Thumbnail", ref generateThumbnail))
        {
            _videoLoader.GenerateThumbnail = generateThumbnail;
        }

        if (!string.IsNullOrEmpty(_videoLoader.VideoPath) && File.Exists(_videoLoader.VideoPath))
        {
            var videoInfo = _videoLoader.GetVideoInfo();
            if (videoInfo != null)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.Text("Video Information:");
                ImGui.BulletText($"File: {videoInfo.FileName}");
                ImGui.BulletText($"Resolution: {videoInfo.Width}x{videoInfo.Height}");
                ImGui.BulletText($"Duration: {videoInfo.DurationSeconds:F2} seconds");
                ImGui.BulletText($"Frame Rate: {videoInfo.FrameRate:F2} fps");
                ImGui.BulletText($"Codec: {videoInfo.Codec}");
                ImGui.BulletText($"Format: {videoInfo.Format}");
                ImGui.BulletText($"Audio: {(videoInfo.HasAudio ? "Yes" : "No")}");
                ImGui.BulletText($"File Size: {videoInfo.FileSize / 1024.0 / 1024.0:F2} MB");
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import video");
            }
        }
    }

    private void DrawAudioOptions()
    {
        ImGui.TextWrapped("Import audio files for analysis and visualization. Supports WAV, MP3, OGG, FLAC, and other common formats.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Audio File:");
        var path = _audioLoader.AudioPath ?? "";
        ImGui.InputText("##AudioPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##AudioFile"))
        {
            string[] audioExtensions = { ".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma" };
            _audioDialog.Open(null, audioExtensions);
        }

        ImGui.Spacing();
        bool generateWaveform = _audioLoader.GenerateWaveform;
        if (ImGui.Checkbox("Generate Waveform", ref generateWaveform))
        {
            _audioLoader.GenerateWaveform = generateWaveform;
        }

        if (!string.IsNullOrEmpty(_audioLoader.AudioPath) && File.Exists(_audioLoader.AudioPath))
        {
            var audioInfo = _audioLoader.GetAudioInfo();
            if (audioInfo != null)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.Text("Audio Information:");
                ImGui.BulletText($"File: {audioInfo.FileName}");
                ImGui.BulletText($"Sample Rate: {audioInfo.SampleRate} Hz");
                ImGui.BulletText($"Channels: {audioInfo.Channels}");
                ImGui.BulletText($"Bit Depth: {audioInfo.BitsPerSample} bits");
                ImGui.BulletText($"Duration: {audioInfo.DurationSeconds:F2} seconds");
                ImGui.BulletText($"Encoding: {audioInfo.Encoding}");
                ImGui.BulletText($"Format: {audioInfo.Format}");
                ImGui.BulletText($"File Size: {audioInfo.FileSize / 1024.0 / 1024.0:F2} MB");
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import audio");
            }
        }
    }

    private void DrawTextOptions()
    {
        ImGui.TextWrapped("Import text documents for viewing and editing. Supports plain text (TXT) and rich text format (RTF).");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Text File:");
        var path = _textLoader.TextPath ?? "";
        ImGui.InputText("##TextPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##TextFile"))
        {
            string[] textExtensions = { ".txt", ".rtf" };
            _textDialog.Open(null, textExtensions);
        }

        if (!string.IsNullOrEmpty(_textLoader.TextPath) && File.Exists(_textLoader.TextPath))
        {
            var fileInfo = new FileInfo(_textLoader.TextPath);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("File Information:");
            ImGui.BulletText($"File: {fileInfo.Name}");
            ImGui.BulletText($"Format: {Path.GetExtension(_textLoader.TextPath).TrimStart('.').ToUpperInvariant()}");
            ImGui.BulletText($"File Size: {fileInfo.Length / 1024.0:F2} KB");
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import text document");
        }
    }

    private void DrawSlopeStabilityResultsOptions()
    {
        ImGui.TextWrapped("Import slope stability simulation results from a binary file (.ssr). " +
                          "This lets you reopen previously exported results without rerunning the simulation.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Slope Stability Results File (.ssr):");
        var path = _slopeResultsLoader.FilePath ?? "";
        ImGui.InputText("##SlopeResultsPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##SlopeResultsFile"))
        {
            string[] extensions = { ".ssr" };
            _slopeResultsDialog.Open(null, extensions);
        }

        if (!string.IsNullOrEmpty(_slopeResultsLoader.FilePath) && File.Exists(_slopeResultsLoader.FilePath))
        {
            var info = new FileInfo(_slopeResultsLoader.FilePath);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("File Information:");
            ImGui.BulletText($"File: {info.Name}");
            ImGui.BulletText($"Size: {info.Length / 1024} KB");
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import slope stability results");
        }
    }

    private void DrawPNMOptions()
    {
        ImGui.TextWrapped("Import a Pore Network Model, typically generated from CT data. " +
                          "This visualizes pores as spheres and throats as sticks in a 3D view.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("PNM File (.pnm.json):");
        var path = _pnmLoader.FilePath ?? "";
        ImGui.InputText("##PNMPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##PNMFile"))
        {
            string[] pnmExtensions = { ".pnm.json", ".json" };
            _pnmDialog.Open(null, pnmExtensions);
        }

        if (!string.IsNullOrEmpty(_pnmLoader.FilePath) && File.Exists(_pnmLoader.FilePath))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("File Information:");
            var info = new FileInfo(_pnmLoader.FilePath);
            ImGui.BulletText($"File: {info.Name}");
            ImGui.BulletText($"Size: {info.Length / 1024} KB");
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import PNM dataset");
        }
    }

    private void DrawDualPNMOptions()
    {
        ImGui.TextWrapped("Import a Dual Pore Network Model that combines macro-scale (CT) and micro-scale (SEM) pore networks. " +
                          "Based on the dual porosity approach from FOUBERT, DE BOEVER et al.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Dual PNM File (.dualpnm.json or .json):");
        var path = _dualPnmLoader.FilePath ?? "";
        ImGui.InputText("##DualPNMPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##DualPNMFile"))
        {
            string[] dualPnmExtensions = { ".dualpnm.json", ".json" };
            _dualPnmDialog.Open(null, dualPnmExtensions);
        }

        ImGui.Spacing();
        ImGui.TextWrapped("Features:");
        ImGui.BulletText("Macro-pore network from CT scans");
        ImGui.BulletText("Micro-pore networks from SEM images");
        ImGui.BulletText("Dual porosity coupling (Parallel/Series/Mass Transfer)");
        ImGui.BulletText("Combined permeability calculations");
        ImGui.BulletText("All standard PNM simulations supported");

        if (!string.IsNullOrEmpty(_dualPnmLoader.FilePath) && File.Exists(_dualPnmLoader.FilePath))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("File Information:");
            var info = new FileInfo(_dualPnmLoader.FilePath);
            ImGui.BulletText($"File: {info.Name}");
            ImGui.BulletText($"Size: {info.Length / 1024} KB");
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import Dual PNM dataset");
        }
    }

    private void DrawSegmentationOptions()
    {
        ImGui.TextWrapped("Import a standalone segmentation/label image. This can be used to load " +
                          "segmentation data without requiring the original image. The segmentation " +
                          "will be displayed with the labeled regions visible.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Segmentation File:");
        var path = _segmentationLoader.SegmentationPath ?? "";
        ImGui.InputText("##SegmentationPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##SegmentationFile"))
        {
            string[] segmentationExtensions = { ".png", ".tiff", ".tif", ".labels.png", ".labels.tiff" };
            _segmentationDialog.Open(null, segmentationExtensions);
        }

        ImGui.Spacing();
        ImGui.Text("Dataset Name:");
        ImGui.SetNextItemWidth(300);
        var name = _segmentationLoader.DatasetName ?? "";
        if (ImGui.InputText("##SegmentationName", ref name, 256)) _segmentationLoader.DatasetName = name;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Optional: Leave empty to use the file name");

        var info = _segmentationLoader.GetSegmentationInfo();
        if (info != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("Segmentation Information:");
            ImGui.BulletText($"File: {info.FileName}");
            ImGui.BulletText($"Resolution: {info.Width} x {info.Height}");
            ImGui.BulletText($"Size: {info.FileSize / 1024} KB");

            if (info.HasMaterialsFile)
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f),
                    "✓ Material definitions found (.materials.json)");
            else
                ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f),
                    "⚠ No material definitions found - colors will be used to generate materials");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f),
                "✓ Ready to import segmentation");
        }
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
        if (ImGui.Button("Browse...##AcousticFolder")) _acousticDialog.Open();

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
                DrawCheckmark(info.HasDamageField, "Damage Field");

                if (info.HasTimeSeries)
                    DrawCheckmark(true, $"Time Series ({info.TimeSeriesFrameCount} frames)");
                else
                    DrawCheckmark(false, "Time Series");

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f),
                    "✓ Ready to import acoustic volume");
            }
            else
            {
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.0f, 1.0f),
                    "⚠ Invalid acoustic volume directory");

                if (!string.IsNullOrEmpty(info.ErrorMessage)) ImGui.TextWrapped($"Error: {info.ErrorMessage}");

                ImGui.Spacing();
                ImGui.Text("Expected structure:");
                ImGui.BulletText("metadata.json (required)");
                ImGui.BulletText("PWaveField.bin, SWaveField.bin, or CombinedField.bin");
                ImGui.BulletText("DamageField.bin (optional)");
                ImGui.BulletText("TimeSeries/ folder with snapshot_*.bin files (optional)");
            }
        }
    }

    private void DrawSingleImageOptions()
    {
        ImGui.Text("Image File:");
        var path = _singleImageLoader.ImagePath ?? "";
        ImGui.InputText("##ImagePath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##ImageFile"))
        {
            string[] imageExtensions =
            {
                ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd",
                ".gif", ".hdr", ".pic", ".pnm", ".ppm", ".pgm", ".tif", ".tiff"
            };
            _fileDialog.Open(null, imageExtensions);
        }

        ImGui.Spacing();
        ImGui.Text("Pixel Size (optional):");
        ImGui.SetNextItemWidth(150);
        var pixelSize = _singleImageLoader.PixelSize;
        ImGui.InputFloat("##PixelSizeSingle", ref pixelSize, 0.1f, 1.0f, "%.3f");
        _singleImageLoader.PixelSize = pixelSize;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        var unitIndex = (int)_singleImageLoader.Unit;
        if (ImGui.Combo("##UnitSingle", ref unitIndex, _pixelSizeUnits, _pixelSizeUnits.Length))
            _singleImageLoader.Unit = (PixelSizeUnit)unitIndex;

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Set the physical size of each pixel. Leave as 1.0 if unknown.");

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
        var path = _imageFolderLoader.FolderPath ?? "";
        ImGui.InputText("##ImageFolderPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##ImageFolder")) _folderDialog.Open();

        ImGui.Spacing();
        ImGui.TextWrapped(
            "This option allows you to load multiple images from a folder and organize them into groups.");

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

        var isFolderMode = !_ctStackLoader.IsMultiPageTiff;
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
            var path = _ctStackLoader.SourcePath ?? "";
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
            var path = _ctStackLoader.SourcePath ?? "";
            ImGui.InputText("##CTFolderPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##CTFolder")) _folderDialog.Open();
        }

        ImGui.Spacing();
        ImGui.Text("Voxel Size:");
        ImGui.SetNextItemWidth(150);
        var pixelSize = _ctStackLoader.PixelSize;
        ImGui.InputFloat("##PixelSizeCT", ref pixelSize, 0.1f, 1.0f, "%.3f");
        _ctStackLoader.PixelSize = pixelSize;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        var unitIndex = (int)_ctStackLoader.Unit;
        if (ImGui.Combo("##Unit", ref unitIndex, _pixelSizeUnits, _pixelSizeUnits.Length))
            _ctStackLoader.Unit = (PixelSizeUnit)unitIndex;

        ImGui.Spacing();
        ImGui.Text("3D Binning Factor:");
        ImGui.SetNextItemWidth(230);
        int[] binningOptions = { 1, 2, 4, 8 };
        string[] binningLabels =
            { "1×1×1 (None)", "2×2×2 (8x smaller)", "4×4×4 (64x smaller)", "8×8×8 (512x smaller)" };
        var binningIndex = Array.IndexOf(binningOptions, _ctStackLoader.BinningFactor);
        if (ImGui.Combo("##Binning", ref binningIndex, binningLabels, binningLabels.Length))
            _ctStackLoader.BinningFactor = binningOptions[binningIndex];
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Reduces dataset size by averaging voxels. A factor of 2 makes the data 8 times smaller.");

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
        var path = _mesh3DLoader.ModelPath ?? "";
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
        var scale = _mesh3DLoader.Scale;
        ImGui.InputFloat("##3DScale", ref scale, 0.1f, 1.0f, "%.3f");
        _mesh3DLoader.Scale = scale;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Scale factor to apply when loading the model. 1.0 = original size.");

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

            if (info.IsSupported) ImGui.TextColored(new Vector4(0, 1, 0, 1), "✓ Ready to import");
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

        var isFolderMode = !_labeledVolumeLoader.IsMultiPageTiff;
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
            var path = _labeledVolumeLoader.SourcePath ?? "";
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
            var path = _labeledVolumeLoader.SourcePath ?? "";
            ImGui.InputText("##LabeledFolderPath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("Browse...##LabeledFolder")) _folderDialog.Open();
        }

        ImGui.Spacing();
        ImGui.Text("Voxel Size:");
        ImGui.SetNextItemWidth(150);
        var pixelSize = _labeledVolumeLoader.PixelSize;
        ImGui.InputFloat("##PixelSizeLabeled", ref pixelSize, 0.1f, 1.0f, "%.3f");
        _labeledVolumeLoader.PixelSize = pixelSize;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        var unitIndex = (int)_labeledVolumeLoader.Unit;
        if (ImGui.Combo("##UnitLabeled", ref unitIndex, _pixelSizeUnits, _pixelSizeUnits.Length))
            _labeledVolumeLoader.Unit = (PixelSizeUnit)unitIndex;

        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Physical size of each voxel in the volume.");

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
        ImGui.TextWrapped(
            "Import tabular data from CSV, TSV, or text files. The data will be loaded into a table dataset " +
            "where you can view, filter, sort, and analyze the data.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Table File:");
        var path = _tableLoader.FilePath ?? "";
        ImGui.InputText("##TablePath", ref path, 260, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine();
        if (ImGui.Button("Browse...##TableFile"))
        {
            string[] tableExtensions = { ".csv", ".tsv", ".txt", ".tab", ".dat" };
            _tableDialog.Open(null, tableExtensions);
        }

        ImGui.Spacing();

        ImGui.Text("File Format:");
        var format = (int)_tableLoader.Format;
        ImGui.RadioButton("Auto-detect", ref format, 0);
        ImGui.SameLine();
        ImGui.RadioButton("CSV (comma)", ref format, 1);
        ImGui.SameLine();
        ImGui.RadioButton("TSV (tab)", ref format, 2);
        _tableLoader.Format = (TableLoader.FileFormat)format;

        var info = _tableLoader.GetTableInfo();
        if (info != null) ImGui.Text($"Detected: {info.DetectedDelimiter}");

        ImGui.Spacing();
        var hasHeaders = _tableLoader.HasHeaders;
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
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX |
                        ImGuiTableFlags.ScrollY))
                {
                    foreach (DataColumn col in preview.Columns) ImGui.TableSetupColumn(col.ColumnName);
                    ImGui.TableHeadersRow();

                    var rowsToShow = Math.Min(5, preview.Rows.Count);
                    for (var i = 0; i < rowsToShow; i++)
                    {
                        ImGui.TableNextRow();
                        for (var j = 0; j < preview.Columns.Count; j++)
                        {
                            ImGui.TableNextColumn();
                            ImGui.Text(preview.Rows[i][j]?.ToString() ?? "");
                        }
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();

                ImGui.Text(
                    $"Preview showing {Math.Min(5, preview.Rows.Count)} of {preview.Rows.Count} rows, {preview.Columns.Count} columns");
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f), "✓ Ready to import");
            }
        }
    }

    private void DrawGISOptions()
    {
        ImGui.TextWrapped("Import GIS data (Vectorial / Raster) or create a new empty map for editing.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Create empty or load file
        var createEmpty = _gisLoader.CreateEmpty;
        if (ImGui.RadioButton("Create Empty Map", createEmpty))
        {
            _gisLoader.CreateEmpty = true;
            _gisLoader.FilePath = "";
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Load GIS File", !createEmpty)) _gisLoader.CreateEmpty = false;

        ImGui.Spacing();

        if (_gisLoader.CreateEmpty)
        {
            ImGui.Text("Map Name:");
            ImGui.SetNextItemWidth(300);
            var name = _gisLoader.DatasetName ?? "New Map";
            if (ImGui.InputText("##MapName", ref name, 256)) _gisLoader.DatasetName = name;

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
                    ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1.0f),
                        "✓ Ready to import");
            }
        }
    }

    private void DrawCheckmark(bool hasComponent, string label)
    {
        if (hasComponent)
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "✓");
        else
            ImGui.TextColored(new Vector4(1.0f, 0.0f, 0.0f, 1.0f), "✗");
        ImGui.SameLine();
        ImGui.Text(label);
    }

    private void DrawButtons()
    {
        var canImport = GetCurrentLoader()?.CanImport ?? false;

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
            9 => _segmentationLoader,
            10 => _pnmLoader, // Added for PNM
            11 => _dualPnmLoader, // Added for Dual PNM
            12 => _boreholeBinaryLoader,
            13 => _lasLoader,
            14 => _twoDGeologyLoader,
            15 => _subsurfaceGisLoader,
            16 => _seismicLoader,
            17 => _physicoChemLoader,
            18 => _tough2Loader, // Added for TOUGH2
            19 => _videoLoader, // Added for Video
            20 => _audioLoader, // Added for Audio
            21 => _textLoader,
            22 => _slopeResultsLoader,
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
        ImGui.ProgressBar(_progress, new Vector2(-1, 0), $"{_progress * 100:0}%");

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
        _segmentationLoader.Reset();
        _pnmLoader.Reset(); // Added for PNM
        _dualPnmLoader.Reset(); // Added for Dual PNM
        _boreholeBinaryLoader.Reset();
        _lasLoader.Reset();
        _twoDGeologyLoader.Reset();
        _subsurfaceGisLoader.Reset();
        _seismicLoader.Reset();
        _physicoChemLoader.Reset();
        _videoLoader.Reset(); // Added for Video
        _audioLoader.Reset(); // Added for Audio
        _textLoader.Reset();
        _slopeResultsLoader.Reset();

        // Reset state
        _importTask = null;
        _progress = 0;
        _statusText = "";
        _currentState = ImportState.Idle;
        _pendingDataset = null;
        _shouldOpenOrganizer = false;
    }

    private enum ImportState
    {
        Idle,
        ShowingOptions,
        Processing
    }
}
