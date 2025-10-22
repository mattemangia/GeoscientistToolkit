// GeoscientistToolkit/Business/ProjectManager.cs

using System.IO.Compression;
using System.Numerics;
using GeoscientistToolkit.Analysis.NMR;
using GeoscientistToolkit.Analysis.ThermalConductivity;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;
using GeoscientistToolkit.Business.GIS;
// ADDED: To access CompoundLibrary and ChemicalCompound
using AcousticVolumeDatasetDTO = GeoscientistToolkit.Data.AcousticVolumeDatasetDTO;

namespace GeoscientistToolkit.Business;

/// <summary>
///     Manages the current project state, including loading, saving, and handling datasets.
///     Implements a singleton pattern.
/// </summary>
public class ProjectManager
{
    private static ProjectManager _instance;

    private ProjectManager()
    {
    }

    public static ProjectManager Instance => _instance ??= new ProjectManager();

    public List<Dataset> LoadedDatasets { get; } = new();
    public string ProjectName { get; set; } = "Untitled Project";
    public string ProjectPath { get; set; }
    public bool HasUnsavedChanges { get; set; }

    public ProjectMetadata ProjectMetadata { get; set; } = new();

    public event Action<Dataset> DatasetRemoved;
    public event Action<Dataset> DatasetDataChanged;

    public void NotifyDatasetDataChanged(Dataset dataset)
    {
        DatasetDataChanged?.Invoke(dataset);
        HasUnsavedChanges = true;
    }

    public void NewProject()
    {
        for (var i = LoadedDatasets.Count - 1; i >= 0; i--) LoadedDatasets[i].Unload();
        LoadedDatasets.Clear();

        // ADDED: Clear any user-defined compounds from the previous project.
        CompoundLibrary.Instance.ClearUserCompounds();

        ProjectName = "Untitled Project";
        ProjectPath = null;
        HasUnsavedChanges = false;

        ProjectMetadata = new ProjectMetadata();

        Logger.Log("Created new project");
    }

    public void AddDataset(Dataset dataset)
    {
        if (dataset == null || LoadedDatasets.Contains(dataset)) return;

        LoadedDatasets.Add(dataset);
        HasUnsavedChanges = true;
        Logger.Log($"Added dataset: {dataset.Name} ({dataset.Type})");
    }

    public void RemoveDataset(Dataset dataset)
    {
        if (dataset == null || !LoadedDatasets.Contains(dataset)) return;

        var partnersToRemove = new List<Dataset>();

        if (dataset is StreamingCtVolumeDataset streamingDs && streamingDs.EditablePartner != null)
        {
            if (LoadedDatasets.Contains(streamingDs.EditablePartner)) partnersToRemove.Add(streamingDs.EditablePartner);
        }
        else if (dataset is CtImageStackDataset editableDs)
        {
            var streamingPartner = LoadedDatasets
                .OfType<StreamingCtVolumeDataset>()
                .FirstOrDefault(s => s.EditablePartner == editableDs);
            if (streamingPartner != null) partnersToRemove.Add(streamingPartner);
        }

        LoadedDatasets.Remove(dataset);
        dataset.Unload();
        Logger.Log($"Removed dataset: {dataset.Name}");
        DatasetRemoved?.Invoke(dataset);
        HasUnsavedChanges = true;

        foreach (var partner in partnersToRemove)
            if (LoadedDatasets.Remove(partner))
            {
                partner.Unload();
                Logger.Log($"Removed linked partner dataset: {partner.Name}");
                DatasetRemoved?.Invoke(partner);
            }
    }

    public void SaveProject(string path = null)
    {
        path ??= ProjectPath;
        if (string.IsNullOrEmpty(path))
        {
            Logger.LogWarning("Save path is not specified. Cannot save project.");
            return;
        }

        // Save associated binary data and materials
        Logger.Log("Saving underlying binary data for all datasets...");
        foreach (var dataset in LoadedDatasets)
            if (dataset is CtImageStackDataset ctDataset)
            {
                ctDataset.SaveLabelData();
                ctDataset.SaveMaterials();
            }

        // MODIFIED: The serializer will now also save custom compounds from CompoundLibrary.Instance
        ProjectSerializer.SaveProject(this, path);
        ProjectPath = path;
        ProjectName = Path.GetFileNameWithoutExtension(path);
        HasUnsavedChanges = false;

        UpdateRecentProjects(path);
    }

    public void BackupProject()
    {
        var settings = SettingsManager.Instance.Settings.Backup;
        if (string.IsNullOrEmpty(ProjectPath) || !settings.EnableAutoBackup) return;

        try
        {
            Directory.CreateDirectory(settings.BackupDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"{ProjectName}_{timestamp}.bak";
            var backupFilePath = Path.Combine(settings.BackupDirectory, backupFileName);

            if (settings.CompressBackups)
            {
                backupFilePath += ".gz";
                using var backupFileStream = File.Create(backupFilePath);
                using var compressionStream = new GZipStream(backupFileStream, CompressionMode.Compress);

                var tempPath = Path.GetTempFileName();
                ProjectSerializer.SaveProject(this, tempPath);

                using (var tempFileStream = File.OpenRead(tempPath))
                {
                    tempFileStream.CopyTo(compressionStream);
                }

                File.Delete(tempPath);
            }
            else
            {
                ProjectSerializer.SaveProject(this, backupFilePath);
            }

            Logger.Log($"Project backed up to {backupFilePath}");

            var backupFiles = new DirectoryInfo(settings.BackupDirectory)
                .GetFiles($"{ProjectName}_*.bak*")
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            while (backupFiles.Count > settings.MaxBackupCount)
            {
                var fileToDelete = backupFiles.Last();
                fileToDelete.Delete();
                backupFiles.Remove(fileToDelete);
                Logger.Log($"Removed old backup: {fileToDelete.Name}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create project backup: {ex.Message}");
        }
    }

    public void LoadProject(string path)
    {
        if (!File.Exists(path))
        {
            Logger.LogError($"Project file not found: {path}");
            RemoveFromRecentProjects(path);
            return;
        }

        var projectDto = ProjectSerializer.LoadProject(path);
        if (projectDto == null) return;

        NewProject();

        ProjectName = projectDto.ProjectName;
        ProjectPath = path;

        if (projectDto.ProjectMetadata != null)
            ProjectMetadata = ConvertFromProjectMetadataDTO(projectDto.ProjectMetadata);

        // ADDED: Restore custom compounds from the project file
        if (projectDto.CustomCompounds != null && projectDto.CustomCompounds.Count > 0)
        {
            foreach (var compoundDto in projectDto.CustomCompounds)
            {
                var compound = ConvertFromChemicalCompoundDTO(compoundDto);
                CompoundLibrary.Instance.AddOrUpdate(compound);
            }

            Logger.Log($"Restored {projectDto.CustomCompounds.Count} custom compounds from project.");
        }

        var createdDatasets = new Dictionary<string, Dataset>();
        var streamingDtos = new List<StreamingCtVolumeDatasetDTO>();

        // PASS 1: Create all non-streaming datasets.
        foreach (var datasetDto in projectDto.Datasets)
            if (datasetDto is StreamingCtVolumeDatasetDTO sDto)
            {
                streamingDtos.Add(sDto);
            }
            else
            {
                var dataset = CreateDatasetFromDTO(datasetDto, null);
                if (dataset != null)
                {
                    createdDatasets[dataset.FilePath] = dataset;
                    // Loading logic is now handled inside CreateDatasetFromDTO
                }
            }

        // PASS 2: Create streaming datasets and link partners.
        foreach (var sDto in streamingDtos)
        {
            var dataset = CreateDatasetFromDTO(sDto, createdDatasets);
            if (dataset != null)
            {
                createdDatasets[sDto.FilePath] = dataset;

                if (!dataset.IsMissing)
                    try
                    {
                        dataset.Load();
                        Logger.Log($"Loaded data for streaming dataset: {dataset.Name}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to load data for streaming dataset '{dataset.Name}': {ex.Message}");
                    }
            }
        }

        LoadedDatasets.AddRange(createdDatasets.Values);

        HasUnsavedChanges = false;
        Logger.Log($"Project '{ProjectName}' loaded from: {path}");
        UpdateRecentProjects(path);
    }

    private Dataset CreateDatasetFromDTO(DatasetDTO dto, IReadOnlyDictionary<string, Dataset> partners)
    {
        Dataset dataset = null;

        switch (dto)
        {
            case StreamingCtVolumeDatasetDTO sDto:
                var streamingDataset = new StreamingCtVolumeDataset(sDto.Name, sDto.FilePath)
                {
                    IsMissing = !File.Exists(sDto.FilePath)
                };
                if (streamingDataset.IsMissing)
                {
                    Logger.LogWarning($"Source file not found for streaming dataset: {sDto.Name} at {sDto.FilePath}");
                }
                else if (partners != null && partners.TryGetValue(sDto.PartnerFilePath, out var partner))
                {
                    streamingDataset.EditablePartner = partner as CtImageStackDataset;
                    if (streamingDataset.EditablePartner == null)
                        Logger.LogError(
                            $"Could not link streaming dataset '{sDto.Name}'. Partner at '{sDto.PartnerFilePath}' is not an editable CtImageStackDataset.");
                }
                else
                {
                    Logger.LogError(
                        $"Could not find editable partner for streaming dataset '{sDto.Name}' at path '{sDto.PartnerFilePath}'.");
                }

                dataset = streamingDataset;
                break;

            case BoreholeDatasetDTO boreholeDto:
            {
                var boreholeDataset = new BoreholeDataset(boreholeDto.Name, boreholeDto.FilePath)
                {
                    WellName = boreholeDto.WellName,
                    Field = boreholeDto.Field,
                    TotalDepth = boreholeDto.TotalDepth,
                    WellDiameter = boreholeDto.WellDiameter,
                    SurfaceCoordinates = boreholeDto.SurfaceCoordinates,
                    Elevation = boreholeDto.Elevation,
                    DepthScaleFactor = boreholeDto.DepthScaleFactor,
                    ShowGrid = boreholeDto.ShowGrid,
                    ShowLegend = boreholeDto.ShowLegend,
                    TrackWidth = boreholeDto.TrackWidth,
                    IsMissing = !File.Exists(boreholeDto.FilePath)
                };

                // Restore lithology units
                if (boreholeDto.LithologyUnits != null)
                    foreach (var unitDto in boreholeDto.LithologyUnits)
                    {
                        var unit = new LithologyUnit
                        {
                            ID = unitDto.ID,
                            Name = unitDto.Name,
                            LithologyType = unitDto.LithologyType,
                            DepthFrom = unitDto.DepthFrom,
                            DepthTo = unitDto.DepthTo,
                            Color = unitDto.Color,
                            Description = unitDto.Description,
                            GrainSize = unitDto.GrainSize,
                            Parameters = unitDto.Parameters ?? new Dictionary<string, float>(),
                            ParameterSources = unitDto.ParameterSources ?? new Dictionary<string, ParameterSource>()
                        };
                        boreholeDataset.LithologyUnits.Add(unit);
                    }

                // Restore parameter tracks
                if (boreholeDto.ParameterTracks != null)
                    foreach (var kvp in boreholeDto.ParameterTracks)
                    {
                        var trackDto = kvp.Value;
                        var track = new ParameterTrack
                        {
                            Name = trackDto.Name,
                            Unit = trackDto.Unit,
                            MinValue = trackDto.MinValue,
                            MaxValue = trackDto.MaxValue,
                            IsLogarithmic = trackDto.IsLogarithmic,
                            Color = trackDto.Color,
                            IsVisible = trackDto.IsVisible,
                            Points = trackDto.Points ?? new List<ParameterPoint>()
                        };
                        boreholeDataset.ParameterTracks[kvp.Key] = track;
                    }

                if (boreholeDataset.IsMissing)
                    Logger.LogWarning(
                        $"Source file not found for Borehole dataset: {boreholeDto.Name} at {boreholeDto.FilePath}");

                dataset = boreholeDataset;
                break;
            }

            case Mesh3DDatasetDTO mesh3DDto:
                var mesh3DDataset = new Mesh3DDataset(mesh3DDto.Name, mesh3DDto.FilePath)
                {
                    FileFormat = mesh3DDto.FileFormat,
                    Scale = mesh3DDto.Scale,
                    VertexCount = mesh3DDto.VertexCount,
                    FaceCount = mesh3DDto.FaceCount,
                    BoundingBoxMin = mesh3DDto.BoundingBoxMin,
                    BoundingBoxMax = mesh3DDto.BoundingBoxMax,
                    Center = mesh3DDto.Center,
                    IsMissing = !File.Exists(mesh3DDto.FilePath)
                };
                if (mesh3DDataset.IsMissing)
                    Logger.LogWarning($"Source file not found for 3D model: {mesh3DDto.Name} at {mesh3DDto.FilePath}");
                dataset = mesh3DDataset;
                break;

            case TableDatasetDTO tableDto:
                var tableDataset = new TableDataset(tableDto.Name, tableDto.FilePath)
                {
                    SourceFormat = tableDto.SourceFormat,
                    Delimiter = tableDto.Delimiter,
                    HasHeaders = tableDto.HasHeaders,
                    Encoding = tableDto.Encoding,
                    IsMissing = !File.Exists(tableDto.FilePath)
                };
                if (tableDataset.IsMissing)
                    Logger.LogWarning($"Source file not found for table: {tableDto.Name} at {tableDto.FilePath}");
                dataset = tableDataset;
                break;

            case CtImageStackDatasetDTO ctDto:
                var ctDataset = new CtImageStackDataset(ctDto.Name, ctDto.FilePath)
                {
                    PixelSize = ctDto.PixelSize,
                    SliceThickness = ctDto.SliceThickness,
                    Unit = ctDto.Unit,
                    BinningSize = ctDto.BinningSize,
                    IsMissing = !(Directory.Exists(ctDto.FilePath) || File.Exists(ctDto.FilePath))
                };

                if (ctDto.Materials != null && ctDto.Materials.Count > 0)
                {
                    ctDataset.Materials.Clear();
                    foreach (var matDto in ctDto.Materials)
                    {
                        var material = new Material(matDto.ID, matDto.Name, matDto.Color)
                        {
                            MinValue = matDto.MinValue,
                            MaxValue = matDto.MaxValue,
                            IsVisible = matDto.IsVisible,
                            IsExterior = matDto.IsExterior,
                            Density = matDto.Density,
                            // --- FIX ---
                            // Restore the link to the physical material library
                            PhysicalMaterialName = matDto.PhysicalMaterialName
                        };
                        ctDataset.Materials.Add(material);
                    }

                    Logger.Log($"Restored {ctDataset.Materials.Count} materials for dataset: {ctDto.Name}");
                }

                // --- NEW: Deserialize simulation results ---
                if (ctDto.NmrResults != null)
                {
                    var nr = new NMRResults(ctDto.NmrResults.TotalSteps > 0 ? ctDto.NmrResults.TotalSteps : 0)
                    {
                        TimePoints = ctDto.NmrResults.TimePoints,
                        Magnetization = ctDto.NmrResults.Magnetization,
                        T2Histogram = ctDto.NmrResults.T2Histogram,
                        T2HistogramBins = ctDto.NmrResults.T2HistogramBins,
                        T1Histogram = ctDto.NmrResults.T1Histogram,
                        T1HistogramBins = ctDto.NmrResults.T1HistogramBins,
                        HasT1T2Data = ctDto.NmrResults.HasT1T2Data,
                        PoreSizes = ctDto.NmrResults.PoreSizes,
                        PoreSizeDistribution = ctDto.NmrResults.PoreSizeDistribution,
                        MeanT2 = ctDto.NmrResults.MeanT2,
                        GeometricMeanT2 = ctDto.NmrResults.GeometricMeanT2,
                        T2PeakValue = ctDto.NmrResults.T2PeakValue,
                        NumberOfWalkers = ctDto.NmrResults.NumberOfWalkers,
                        TotalSteps = ctDto.NmrResults.TotalSteps,
                        TimeStep = ctDto.NmrResults.TimeStep,
                        PoreMaterial = ctDto.NmrResults.PoreMaterial,
                        MaterialRelaxivities = ctDto.NmrResults.MaterialRelaxivities,
                        ComputationTime = TimeSpan.FromSeconds(ctDto.NmrResults.ComputationTimeSeconds),
                        ComputationMethod = ctDto.NmrResults.ComputationMethod
                    };

                    if (nr.HasT1T2Data && ctDto.NmrResults.T1T2MapData != null)
                    {
                        var t1Count = ctDto.NmrResults.T1T2Map_T1Count;
                        var t2Count = ctDto.NmrResults.T1T2Map_T2Count;
                        if (t1Count > 0 && t2Count > 0)
                        {
                            nr.T1T2Map = new double[t1Count, t2Count];
                            Buffer.BlockCopy(ctDto.NmrResults.T1T2MapData, 0, nr.T1T2Map, 0,
                                t1Count * t2Count * sizeof(double));
                        }
                    }

                    ctDataset.NmrResults = nr;
                    Logger.Log($"Restored NMR results for dataset: {ctDto.Name}");
                }

                if (ctDto.ThermalResults != null)
                {
                    var tr = new ThermalResults
                    {
                        EffectiveConductivity = ctDto.ThermalResults.EffectiveConductivity,
                        MaterialConductivities = ctDto.ThermalResults.MaterialConductivities,
                        AnalyticalEstimates = ctDto.ThermalResults.AnalyticalEstimates,
                        ComputationTime = TimeSpan.FromSeconds(ctDto.ThermalResults.ComputationTimeSeconds),
                        IterationsPerformed = ctDto.ThermalResults.IterationsPerformed,
                        FinalError = ctDto.ThermalResults.FinalError
                    };

                    if (ctDto.ThermalResults.TemperatureFieldData != null)
                    {
                        var w = ctDto.ThermalResults.TempField_W;
                        var h = ctDto.ThermalResults.TempField_H;
                        var d = ctDto.ThermalResults.TempField_D;
                        if (w > 0 && h > 0 && d > 0)
                        {
                            tr.TemperatureField = new float[w, h, d];
                            Buffer.BlockCopy(ctDto.ThermalResults.TemperatureFieldData, 0, tr.TemperatureField, 0,
                                w * h * d * sizeof(float));
                        }
                    }

                    ctDataset.ThermalResults = tr;
                    Logger.Log($"Restored Thermal results for dataset: {ctDto.Name}");
                }


                if (ctDataset.IsMissing)
                    Logger.LogWarning($"Source folder or file not found for dataset: {ctDto.Name} at {ctDto.FilePath}");
                dataset = ctDataset;
                break;

            case AcousticVolumeDatasetDTO acousticDto:
                var acousticDataset = new AcousticVolumeDataset(acousticDto.Name, acousticDto.FilePath)
                {
                    PWaveVelocity = acousticDto.PWaveVelocity,
                    SWaveVelocity = acousticDto.SWaveVelocity,
                    VpVsRatio = acousticDto.VpVsRatio,
                    TimeSteps = acousticDto.TimeSteps,
                    ComputationTime = TimeSpan.FromSeconds(acousticDto.ComputationTimeSeconds),
                    YoungsModulusMPa = acousticDto.YoungsModulusMPa,
                    PoissonRatio = acousticDto.PoissonRatio,
                    ConfiningPressureMPa = acousticDto.ConfiningPressureMPa,
                    SourceFrequencyKHz = acousticDto.SourceFrequencyKHz,
                    SourceEnergyJ = acousticDto.SourceEnergyJ,
                    SourceDatasetPath = acousticDto.SourceDatasetPath,
                    SourceMaterialName = acousticDto.SourceMaterialName,
                    IsMissing = !Directory.Exists(acousticDto.FilePath)
                };

                if (acousticDataset.IsMissing)
                    Logger.LogWarning(
                        $"Source folder not found for acoustic dataset: {acousticDto.Name} at {acousticDto.FilePath}");

                dataset = acousticDataset;
                break;

            case ImageDatasetDTO imgDto:
                var imgDataset = new ImageDataset(imgDto.Name, imgDto.FilePath)
                {
                    PixelSize = imgDto.PixelSize,
                    Unit = imgDto.Unit,
                    IsMissing = !File.Exists(imgDto.FilePath)
                };
                if (imgDataset.IsMissing)
                    Logger.LogWarning($"Source file not found for dataset: {imgDataset.Name} at {imgDataset.FilePath}");
                dataset = imgDataset;
                break;

            case PNMDatasetDTO pnmDto:
            {
                var pnmDataset = new PNMDataset(pnmDto.Name, pnmDto.FilePath)
                {
                    IsMissing = !File.Exists(pnmDto.FilePath)
                };

                pnmDataset.ImportFromDTO(pnmDto);

                if (pnmDataset.IsMissing)
                    Logger.LogWarning(
                        $"Source file not found for PNM dataset: {pnmDto.Name} at {pnmDto.FilePath}. Data was restored from project file.");

                dataset = pnmDataset;
                break;
            }
            case TwoDGeologyDatasetDTO geo2dDto:
            {
                var geo2dDataset = new TwoDGeologyDataset(geo2dDto.Name, geo2dDto.FilePath)
                {
                    IsMissing = !File.Exists(geo2dDto.FilePath)
                };
                if (geo2dDataset.IsMissing)
                    Logger.LogWarning(
                        $"Source file not found for 2D Geology dataset: {geo2dDto.Name} at {geo2dDto.FilePath}");
                dataset = geo2dDataset;
                break;
            }

            case GISDatasetDTO gisDto:
            {
                var gisDataset = new GISDataset(gisDto.Name, gisDto.FilePath)
                {
                    BasemapPath = gisDto.BasemapPath,
                    Center = gisDto.Center,
                    DefaultZoom = gisDto.DefaultZoom,
                    Tags = (GISTag)gisDto.Tags,
                    IsMissing = !string.IsNullOrEmpty(gisDto.FilePath) && !File.Exists(gisDto.FilePath)
                };
            
                if (Enum.TryParse<BasemapType>(gisDto.BasemapType, out var basemapType))
                    gisDataset.BasemapType = basemapType;
            
                // Restore layers and features from DTO if they exist
                if (gisDto.Layers.Any(l => l.Features.Any()))
                {
                    gisDataset.Layers.Clear(); // Remove the default layer
                    foreach (var layerDto in gisDto.Layers)
                    {
                        var layer = new GISLayer
                        {
                            Name = layerDto.Name,
                            Type = Enum.TryParse<LayerType>(layerDto.Type, out var lType) ? lType : LayerType.Vector,
                            IsVisible = layerDto.IsVisible,
                            IsEditable = layerDto.IsEditable,
                            Color = layerDto.Color
                        };
            
                        foreach (var featureDto in layerDto.Features)
                        {
                            GISFeature feature;
                            // Check if it's a geological feature by looking at the nullable type
                            if (featureDto.GeologicalType.HasValue)
                            {
                                var geoFeature = new GeologicalMapping.GeologicalFeature
                                {
                                    GeologicalType = featureDto.GeologicalType.Value,
                                    Strike = featureDto.Strike,
                                    Dip = featureDto.Dip,
                                    DipDirection = featureDto.DipDirection,
                                    Plunge = featureDto.Plunge,
                                    Trend = featureDto.Trend,
                                    FormationName = featureDto.FormationName,
                                    BoreholeName = featureDto.BoreholeName,
                                    LithologyCode = featureDto.LithologyCode,
                                    AgeCode = featureDto.AgeCode,
                                    Description = featureDto.Description,
                                    Thickness = featureDto.Thickness,
                                    Displacement = featureDto.Displacement,
                                    MovementSense = featureDto.MovementSense,
                                    IsInferred = featureDto.IsInferred ?? false,
                                    IsCovered = featureDto.IsCovered ?? false
                                };
                                feature = geoFeature;
                            }
                            else
                            {
                                feature = new GISFeature();
                            }
            
                            // Populate base properties
                            feature.Type = featureDto.Type;
                            feature.Coordinates = featureDto.Coordinates;
                            feature.Properties = featureDto.Properties;
                            feature.Id = featureDto.Id;
            
                            layer.Features.Add(feature);
                        }
                        gisDataset.Layers.Add(layer);
                    }
                    gisDataset.UpdateBounds(); // Update bounds from restored features
                    Logger.Log($"Restored {gisDataset.Layers.Sum(l=>l.Features.Count)} features from project file for: {gisDataset.Name}");
                }
                else if (!gisDataset.IsMissing)
                {
                    // Only load from file if no features were serialized in the project
                    try
                    {
                        gisDataset.Load();
                        Logger.Log($"Loaded data for dataset from source file: {gisDataset.Name}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to load data for dataset '{gisDataset.Name}': {ex.Message}");
                    }
                }
            
                if (gisDataset.IsMissing)
                    Logger.LogWarning($"Source file not found for GIS dataset: {gisDto.Name} at {gisDto.FilePath}");
                
                dataset = gisDataset;
                break;
            }

            case DatasetGroupDTO groupDto:
                var childDatasets = new List<Dataset>();
                foreach (var childDto in groupDto.Datasets)
                {
                    var childDataset = CreateDatasetFromDTO(childDto, partners);
                    if (childDataset != null) childDatasets.Add(childDataset);
                }

                dataset = new DatasetGroup(groupDto.Name, childDatasets);
                break;

            default:
                Logger.LogError($"Unknown dataset type '{dto.TypeName}' encountered during project load.");
                return null;
        }

        if (dataset != null && dto.Metadata != null)
            dataset.DatasetMetadata = ConvertFromDatasetMetadataDTO(dto.Metadata);

        return dataset;
    }
/// <summary>
/// Creates a new, empty 2D Geology Profile, saves its initial file, and adds it to the current project.
/// </summary>
/// <returns>The newly created Dataset, or null if creation failed.</returns>
public Dataset CreateNew2DGeologyProfile()
{
    try
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var profileName = $"CrossSection_{timestamp}";

        // Get user's documents folder as default location
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var defaultPath = Path.Combine(documentsPath, "2D Geology");

        // Create the directory if it doesn't exist
        if (!Directory.Exists(defaultPath))
        {
            try
            {
                Directory.CreateDirectory(defaultPath);
            }
            catch
            {
                // If we can't create it, just use documents folder
                defaultPath = documentsPath;
            }
        }

        var filePath = Path.Combine(defaultPath, $"{profileName}.2dgeo");

        // --- All the logic that was in MainWindow is now here ---
        var twoDGeo = TwoDGeologyDataset.CreateEmpty(profileName, filePath);

        if (twoDGeo == null)
        {
            Logger.LogError("Failed to create empty 2D geology profile (CreateEmpty returned null)");
            return null;
        }

        // Save the initial profile to the selected location
        try
        {
            TwoDGeologySerializer.Write(filePath, twoDGeo.ProfileData);
            Logger.Log($"Saved initial 2D geology profile to {filePath}");
        }
        catch (Exception saveEx)
        {
            Logger.LogError($"Failed to save initial 2D geology profile: {saveEx.Message}");
            // We can still proceed, the dataset is in memory
        }

        // Add to project (this class's responsibility)
        this.AddDataset(twoDGeo);
        Logger.Log($"Created new 2D geology profile: {profileName}");

        // Return the created dataset so the UI can select it
        return twoDGeo;
    }
    catch (Exception ex)
    {
        Logger.LogError($"Failed to create empty 2D geology profile: {ex.Message}");
        Logger.LogError($"Stack trace: {ex.StackTrace}");
        return null;
    }
}
    private DatasetMetadata ConvertFromDatasetMetadataDTO(DatasetMetadataDTO dto)
    {
        if (dto == null) return new DatasetMetadata();

        var meta = new DatasetMetadata
        {
            SampleName = dto.SampleName,
            LocationName = dto.LocationName,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            Depth = dto.Depth,
            SizeUnit = dto.SizeUnit,
            CollectionDate = dto.CollectionDate,
            Collector = dto.Collector,
            Notes = dto.Notes,
            CustomFields = new Dictionary<string, string>(dto.CustomFields ?? new Dictionary<string, string>())
        };

        if (dto.SizeX.HasValue && dto.SizeY.HasValue && dto.SizeZ.HasValue)
            meta.Size = new Vector3(dto.SizeX.Value, dto.SizeY.Value, dto.SizeZ.Value);

        return meta;
    }

    // ADDED: Helper to convert a ChemicalCompoundDTO to a ChemicalCompound model
    private ChemicalCompound ConvertFromChemicalCompoundDTO(ChemicalCompoundDTO dto)
    {
        if (dto == null) return null;

        return new ChemicalCompound
        {
            Name = dto.Name,
            ChemicalFormula = dto.ChemicalFormula,
            Phase = dto.Phase,
            CrystalSystem = dto.CrystalSystem,
            GibbsFreeEnergyFormation_kJ_mol = dto.GibbsFreeEnergyFormation_kJ_mol,
            EnthalpyFormation_kJ_mol = dto.EnthalpyFormation_kJ_mol,
            Entropy_J_molK = dto.Entropy_J_molK,
            HeatCapacity_J_molK = dto.HeatCapacity_J_molK,
            MolarVolume_cm3_mol = dto.MolarVolume_cm3_mol,
            MolecularWeight_g_mol = dto.MolecularWeight_g_mol,
            Density_g_cm3 = dto.Density_g_cm3,
            LogKsp_25C = dto.LogKsp_25C,
            Solubility_g_100mL_25C = dto.Solubility_g_100mL_25C,
            DissolutionEnthalpy_kJ_mol = dto.DissolutionEnthalpy_kJ_mol,
            ActivationEnergy_Dissolution_kJ_mol = dto.ActivationEnergy_Dissolution_kJ_mol,
            ActivationEnergy_Precipitation_kJ_mol = dto.ActivationEnergy_Precipitation_kJ_mol,
            RateConstant_Dissolution_mol_m2_s = dto.RateConstant_Dissolution_mol_m2_s,
            RateConstant_Precipitation_mol_m2_s = dto.RateConstant_Precipitation_mol_m2_s,
            ReactionOrder_Dissolution = dto.ReactionOrder_Dissolution,
            SpecificSurfaceArea_m2_g = dto.SpecificSurfaceArea_m2_g,
            HeatCapacityPolynomial_a_b_c_d = dto.HeatCapacityPolynomial_a_b_c_d,
            TemperatureRange_K = dto.TemperatureRange_K,
            IonicCharge = dto.IonicCharge,
            ActivityCoefficientParams = dto.ActivityCoefficientParams,
            IonicConductivity_S_cm2_mol = dto.IonicConductivity_S_cm2_mol,
            RefractiveIndex = dto.RefractiveIndex,
            MohsHardness = dto.MohsHardness,
            Color = dto.Color,
            Cleavage = dto.Cleavage,
            Synonyms = dto.Synonyms,
            Notes = dto.Notes,
            Sources = dto.Sources,
            CustomParams = dto.CustomParams,
            IsUserCompound = true // Mark as a user-defined compound
        };
    }

    private ProjectMetadata ConvertFromProjectMetadataDTO(ProjectMetadataDTO dto)
    {
        if (dto == null) return new ProjectMetadata();

        return new ProjectMetadata
        {
            Organisation = dto.Organisation,
            Department = dto.Department,
            Year = dto.Year,
            Expedition = dto.Expedition,
            Author = dto.Author,
            ProjectDescription = dto.ProjectDescription,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            FundingSource = dto.FundingSource,
            License = dto.License,
            CustomFields = new Dictionary<string, string>(dto.CustomFields ?? new Dictionary<string, string>())
        };
    }

    private void UpdateRecentProjects(string projectPath)
    {
        var settings = SettingsManager.Instance.Settings;
        var recentProjects = settings.FileAssociations.RecentProjects;
        recentProjects.RemoveAll(p => string.Equals(p, projectPath, StringComparison.OrdinalIgnoreCase));
        recentProjects.Insert(0, projectPath);
        var maxRecent = settings.Appearance.MaxRecentProjects;
        while (recentProjects.Count > maxRecent) recentProjects.RemoveAt(recentProjects.Count - 1);
        SettingsManager.Instance.SaveSettings();
    }

    private void RemoveFromRecentProjects(string projectPath)
    {
        var recentProjects = SettingsManager.Instance.Settings.FileAssociations.RecentProjects;
        recentProjects.RemoveAll(p => string.Equals(p, projectPath, StringComparison.OrdinalIgnoreCase));
        SettingsManager.Instance.SaveSettings();
    }

    public static List<string> GetRecentProjects()
    {
        var settings = SettingsManager.Instance.Settings;
        var existingProjects = settings.FileAssociations.RecentProjects
            .Where(File.Exists)
            .ToList();

        if (existingProjects.Count != settings.FileAssociations.RecentProjects.Count)
        {
            settings.FileAssociations.RecentProjects = existingProjects;
            SettingsManager.Instance.SaveSettings();
        }

        return existingProjects;
    }

    public void ExportMetadataToCSV(string filePath)
    {
        try
        {
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine(
                    "Dataset Name,Dataset Type,Sample Name,Location Name,Latitude,Longitude,Depth (m),Size X,Size Y,Size Z,Size Unit,Collection Date,Collector,Notes");

                foreach (var dataset in LoadedDatasets)
                {
                    var meta = dataset.DatasetMetadata;
                    writer.WriteLine($"{EscapeCSV(dataset.Name)}," +
                                     $"{dataset.Type}," +
                                     $"{EscapeCSV(meta?.SampleName ?? "")}," +
                                     $"{EscapeCSV(meta?.LocationName ?? "")}," +
                                     $"{meta?.Latitude?.ToString("F6") ?? ""}," +
                                     $"{meta?.Longitude?.ToString("F6") ?? ""}," +
                                     $"{meta?.Depth?.ToString("F2") ?? ""}," +
                                     $"{meta?.Size?.X.ToString("F2") ?? ""}," +
                                     $"{meta?.Size?.Y.ToString("F2") ?? ""}," +
                                     $"{meta?.Size?.Z.ToString("F2") ?? ""}," +
                                     $"{EscapeCSV(meta?.SizeUnit ?? "")}," +
                                     $"{meta?.CollectionDate?.ToString("yyyy-MM-dd") ?? ""}," +
                                     $"{EscapeCSV(meta?.Collector ?? "")}," +
                                     $"{EscapeCSV(meta?.Notes ?? "")}");
                }
            }

            Logger.Log($"Metadata exported to CSV: {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export metadata to CSV: {ex.Message}");
        }
    }

    private string EscapeCSV(string field)
    {
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }

    public string GetProjectSummary()
    {
        var summary = $"Project: {ProjectName}\n";
        summary += $"Datasets: {LoadedDatasets.Count}\n";

        if (ProjectMetadata != null)
        {
            if (!string.IsNullOrEmpty(ProjectMetadata.Organisation))
                summary += $"Organisation: {ProjectMetadata.Organisation}\n";
            if (!string.IsNullOrEmpty(ProjectMetadata.Author))
                summary += $"Author: {ProjectMetadata.Author}\n";
            if (ProjectMetadata.Year.HasValue)
                summary += $"Year: {ProjectMetadata.Year}\n";
        }

        var datasetsByType = LoadedDatasets.GroupBy(d => d.Type);
        foreach (var group in datasetsByType) summary += $"  {group.Key}: {group.Count()}\n";

        return summary;
    }

    public List<string> ValidateMetadata()
    {
        var issues = new List<string>();

        foreach (var dataset in LoadedDatasets)
        {
            var meta = dataset.DatasetMetadata;
            if (meta == null) continue;

            if (meta.Latitude.HasValue && (meta.Latitude < -90 || meta.Latitude > 90))
                issues.Add($"Dataset '{dataset.Name}': Invalid latitude {meta.Latitude}");

            if (meta.Longitude.HasValue && (meta.Longitude < -180 || meta.Longitude > 180))
                issues.Add($"Dataset '{dataset.Name}': Invalid longitude {meta.Longitude}");

            if (string.IsNullOrWhiteSpace(meta.SampleName))
                issues.Add($"Dataset '{dataset.Name}': Missing sample name");
        }

        return issues;
    }
}