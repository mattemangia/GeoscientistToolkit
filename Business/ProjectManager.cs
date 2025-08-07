// GeoscientistToolkit/Business/ProjectManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business
{
    /// <summary>
    /// Manages the current project state, including loading, saving, and handling datasets.
    /// Implements a singleton pattern.
    /// </summary>
    public class ProjectManager
    {
        private static ProjectManager _instance;
        public static ProjectManager Instance => _instance ??= new ProjectManager();

        public List<Dataset> LoadedDatasets { get; } = new List<Dataset>();
        public string ProjectName { get; set; } = "Untitled Project";
        public string ProjectPath { get; set; }
        public bool HasUnsavedChanges { get; set; }

        public event Action<Dataset> DatasetRemoved;
        public event Action<Dataset> DatasetDataChanged;

        public void NotifyDatasetDataChanged(Dataset dataset) => DatasetDataChanged?.Invoke(dataset);

        private ProjectManager() { }

        public void NewProject()
        {
            for (int i = LoadedDatasets.Count - 1; i >= 0; i--)
            {
                LoadedDatasets[i].Unload();
            }
            LoadedDatasets.Clear();

            ProjectName = "Untitled Project";
            ProjectPath = null;
            HasUnsavedChanges = false;

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
                if (LoadedDatasets.Contains(streamingDs.EditablePartner))
                {
                    partnersToRemove.Add(streamingDs.EditablePartner);
                }
            }
            else if (dataset is CtImageStackDataset editableDs)
            {
                var streamingPartner = LoadedDatasets
                    .OfType<StreamingCtVolumeDataset>()
                    .FirstOrDefault(s => s.EditablePartner == editableDs);
                if (streamingPartner != null)
                {
                    partnersToRemove.Add(streamingPartner);
                }
            }

            LoadedDatasets.Remove(dataset);
            dataset.Unload();
            Logger.Log($"Removed dataset: {dataset.Name}");
            DatasetRemoved?.Invoke(dataset);
            HasUnsavedChanges = true;

            foreach (var partner in partnersToRemove)
            {
                if (LoadedDatasets.Remove(partner))
                {
                    partner.Unload();
                    Logger.Log($"Removed linked partner dataset: {partner.Name}");
                    DatasetRemoved?.Invoke(partner);
                }
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

            ProjectSerializer.SaveProject(this, path);
            ProjectPath = path;
            ProjectName = Path.GetFileNameWithoutExtension(path);
            HasUnsavedChanges = false;

            UpdateRecentProjects(path);
        }

        // --- BACKUPPROJECT METHOD RESTORED ---
        public void BackupProject()
        {
            var settings = SettingsManager.Instance.Settings.Backup;
            if (string.IsNullOrEmpty(ProjectPath) || !settings.EnableAutoBackup) return;

            try
            {
                Directory.CreateDirectory(settings.BackupDirectory);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFileName = $"{ProjectName}_{timestamp}.bak";
                string backupFilePath = Path.Combine(settings.BackupDirectory, backupFileName);

                if (settings.CompressBackups)
                {
                    backupFilePath += ".gz";
                    using FileStream backupFileStream = File.Create(backupFilePath);
                    using GZipStream compressionStream = new GZipStream(backupFileStream, CompressionMode.Compress);

                    // Create a temporary file for the serialized project
                    var tempPath = Path.GetTempFileName();
                    ProjectSerializer.SaveProject(this, tempPath);

                    // Copy the contents of the temporary file to the compression stream
                    using (var tempFileStream = File.OpenRead(tempPath))
                    {
                        tempFileStream.CopyTo(compressionStream);
                    }
                    File.Delete(tempPath);
                }
                else
                {
                    // For non-compressed backup, we just save a new copy with a timestamp
                    ProjectSerializer.SaveProject(this, backupFilePath);
                }

                Logger.Log($"Project backed up to {backupFilePath}");

                // Clean up old backups
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
        // --- END OF RESTORED METHOD ---

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

            var createdDatasets = new Dictionary<string, Dataset>();
            var streamingDtos = new List<StreamingCtVolumeDatasetDTO>();

            // PASS 1: Create all non-streaming datasets.
            foreach (var datasetDto in projectDto.Datasets)
            {
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
                }
            }

            LoadedDatasets.AddRange(createdDatasets.Values);

            HasUnsavedChanges = false;
            Logger.Log($"Project '{ProjectName}' loaded from: {path}");
            UpdateRecentProjects(path);
        }

        // --- MODIFIED ---
        private Dataset CreateDatasetFromDTO(DatasetDTO dto, IReadOnlyDictionary<string, Dataset> partners)
        {
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
                        {
                            Logger.LogError($"Could not link streaming dataset '{sDto.Name}'. Partner at '{sDto.PartnerFilePath}' is not an editable CtImageStackDataset.");
                        }
                    }
                    else
                    {
                        Logger.LogError($"Could not find editable partner for streaming dataset '{sDto.Name}' at path '{sDto.PartnerFilePath}'.");
                    }
                    return streamingDataset;
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
                    return mesh3DDataset;


                case CtImageStackDatasetDTO ctDto:
                    var ctDataset = new CtImageStackDataset(ctDto.Name, ctDto.FilePath)
                    {
                        PixelSize = ctDto.PixelSize,
                        SliceThickness = ctDto.SliceThickness,
                        Unit = ctDto.Unit,
                        BinningSize = ctDto.BinningSize,
                        IsMissing = !Directory.Exists(ctDto.FilePath)
                    };

                    // --- DESERIALIZATION LOGIC FOR MATERIALS ---
                    if (ctDto.Materials != null && ctDto.Materials.Count > 0)
                    {
                        // Clear the default materials and load from the file to ensure a perfect state restoration.
                        ctDataset.Materials.Clear();
                        foreach (var matDto in ctDto.Materials)
                        {
                            var material = new Material(matDto.ID, matDto.Name, matDto.Color)
                            {
                                MinValue = matDto.MinValue,
                                MaxValue = matDto.MaxValue,
                                IsVisible = matDto.IsVisible,
                                IsExterior = matDto.IsExterior,
                                Density = matDto.Density
                            };
                            ctDataset.Materials.Add(material);
                        }
                    }
                    // --- END OF DESERIALIZATION LOGIC ---

                    if (ctDataset.IsMissing) Logger.LogWarning($"Source folder not found for dataset: {ctDto.Name} at {ctDto.FilePath}");
                    return ctDataset;

                case ImageDatasetDTO imgDto:
                    var imgDataset = new ImageDataset(imgDto.Name, imgDto.FilePath)
                    {
                        PixelSize = imgDto.PixelSize,
                        Unit = imgDto.Unit,
                        IsMissing = !File.Exists(imgDto.FilePath)
                    };
                    if (imgDataset.IsMissing) Logger.LogWarning($"Source file not found for dataset: {imgDataset.Name} at {imgDataset.FilePath}");
                    return imgDataset;

                case DatasetGroupDTO groupDto:
                    var childDatasets = new List<Dataset>();
                    foreach (var childDto in groupDto.Datasets)
                    {
                        if (partners != null && partners.TryGetValue(childDto.FilePath, out var child))
                        {
                            childDatasets.Add(child);
                        }
                    }
                    return new DatasetGroup(groupDto.Name, childDatasets);

                default:
                    Logger.LogError($"Unknown dataset type '{dto.TypeName}' encountered during project load.");
                    return null;
            }
        }

        private void UpdateRecentProjects(string projectPath)
        {
            var settings = SettingsManager.Instance.Settings;
            var recentProjects = settings.FileAssociations.RecentProjects;
            recentProjects.RemoveAll(p => string.Equals(p, projectPath, StringComparison.OrdinalIgnoreCase));
            recentProjects.Insert(0, projectPath);
            int maxRecent = settings.Appearance.MaxRecentProjects;
            while (recentProjects.Count > maxRecent)
            {
                recentProjects.RemoveAt(recentProjects.Count - 1);
            }
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
    }
}