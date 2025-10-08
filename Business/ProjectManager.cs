// GeoscientistToolkit/Business/ProjectManager.cs

using System.IO.Compression;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;
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
    }

    public void NewProject()
    {
        for (var i = LoadedDatasets.Count - 1; i >= 0; i--) LoadedDatasets[i].Unload();
        LoadedDatasets.Clear();

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

                    if (!dataset.IsMissing)
                        try
                        {
                            dataset.Load();
                            Logger.Log($"Loaded data for dataset: {dataset.Name}");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"Failed to load data for dataset '{dataset.Name}': {ex.Message}");
                        }
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

            case GISDatasetDTO gisDto:
            {
                var gisDataset = new GISDataset(gisDto.Name, gisDto.FilePath)
                {
                    BasemapPath = gisDto.BasemapPath,
                    Center = gisDto.Center,
                    DefaultZoom = gisDto.DefaultZoom,
                    IsMissing = !File.Exists(gisDto.FilePath)
                };
                if (Enum.TryParse<BasemapType>(gisDto.BasemapType, out var basemapType))
                    gisDataset.BasemapType = basemapType;
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