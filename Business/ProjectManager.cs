// GeoscientistToolkit/Business/ProjectManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business
{
    /// <summary>
    /// Manages the current project and its datasets.
    /// </summary>
    public class ProjectManager
    {
        private static ProjectManager _instance;
        public static ProjectManager Instance => _instance ??= new ProjectManager();

        public List<Dataset> LoadedDatasets { get; } = new List<Dataset>();
        public string ProjectName { get; set; } = "Untitled Project";
        public string ProjectPath { get; set; }
        public bool HasUnsavedChanges { get; set; }

        /// <summary>
        /// Fired when a dataset is removed from the project.
        /// </summary>
        public event Action<Dataset> DatasetRemoved;

        private ProjectManager() { }

        public void NewProject()
        {
            // Clear all loaded datasets
            foreach (var dataset in LoadedDatasets)
            {
                dataset.Unload();
                // No need to fire event here, as the whole project is cleared
            }
            LoadedDatasets.Clear();
            
            ProjectName = "Untitled Project";
            ProjectPath = null;
            HasUnsavedChanges = false;
            
            Logger.Log("Created new project");
        }

        public void AddDataset(Dataset dataset)
        {
            if (dataset == null) return;
            
            LoadedDatasets.Add(dataset);
            HasUnsavedChanges = true;
            Logger.Log($"Added dataset: {dataset.Name} ({dataset.Type})");
        }

        public void RemoveDataset(Dataset dataset)
        {
            if (dataset == null) return;
            
            bool removed = LoadedDatasets.Remove(dataset);

            if (removed)
            {
                dataset.Unload();
                HasUnsavedChanges = true;
                Logger.Log($"Removed dataset: {dataset.Name}");
                
                // Invoke the event to notify listeners
                DatasetRemoved?.Invoke(dataset);
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
            
            // Update recent projects list
            UpdateRecentProjects(path);
        }

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
                    using StreamWriter writer = new StreamWriter(compressionStream);
                    
                    var tempPath = Path.GetTempFileName();
                    ProjectSerializer.SaveProject(this, tempPath);
                    writer.Write(File.ReadAllText(tempPath));
                    File.Delete(tempPath);
                }
                else
                {
                    File.Copy(ProjectPath, backupFilePath, true);
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


        public void LoadProject(string path)
        {
            if (!File.Exists(path))
            {
                Logger.LogError($"Project file not found: {path}");
                var recentProjects = SettingsManager.Instance.Settings.FileAssociations.RecentProjects;
                recentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                SettingsManager.Instance.SaveSettings();
                return;
            }
            
            var projectDto = ProjectSerializer.LoadProject(path);
            if (projectDto == null) return;

            NewProject(); // Clear current project before loading

            ProjectName = projectDto.ProjectName;
            ProjectPath = path;

            foreach (var datasetDto in projectDto.Datasets)
            {
                var dataset = CreateDatasetFromDTO(datasetDto);
                if (dataset != null)
                {
                    LoadedDatasets.Add(dataset);
                }
            }

            HasUnsavedChanges = false;
            Logger.Log($"Project '{ProjectName}' loaded from: {path}");
            
            // Update recent projects list
            UpdateRecentProjects(path);
        }

        private Dataset CreateDatasetFromDTO(DatasetDTO dto)
        {
            switch (dto)
            {
                case ImageDatasetDTO imgDto:
                    var imgDataset = new ImageDataset(imgDto.Name, imgDto.FilePath)
                    {
                        PixelSize = imgDto.PixelSize,
                        Unit = imgDto.Unit,
                        IsMissing = !File.Exists(imgDto.FilePath)
                    };
                    if (imgDataset.IsMissing) Logger.LogWarning($"Source file not found for dataset: {imgDataset.Name} at {imgDataset.FilePath}");
                    return imgDataset;
                
                case CtImageStackDatasetDTO ctDto:
                    var ctDataset = new CtImageStackDataset(ctDto.Name, ctDto.FilePath)
                    {
                        PixelSize = ctDto.PixelSize,
                        SliceThickness = ctDto.SliceThickness,
                        Unit = ctDto.Unit,
                        BinningSize = ctDto.BinningSize,
                        IsMissing = !Directory.Exists(ctDto.FilePath)
                    };
                    if (ctDataset.IsMissing) Logger.LogWarning($"Source folder not found for dataset: {ctDataset.Name} at {ctDataset.FilePath}");
                    return ctDataset;
                    
                case DatasetGroupDTO groupDto:
                    var childDatasets = new List<Dataset>();
                    foreach (var childDto in groupDto.Datasets)
                    {
                        var childDataset = CreateDatasetFromDTO(childDto);
                        if (childDataset != null)
                        {
                            childDatasets.Add(childDataset);
                        }
                    }
                    return new DatasetGroup(groupDto.Name, childDatasets);

                default:
                    Logger.LogError($"Unknown dataset type '{dto.TypeName}' encountered during project load.");
                    return null;
            }
        }

        /// <summary>
        /// Updates the recent projects list in settings
        /// </summary>
        private void UpdateRecentProjects(string projectPath)
        {
            var settings = SettingsManager.Instance.Settings;
            var recentProjects = settings.FileAssociations.RecentProjects;
            
            // Remove the path if it already exists (to move it to the top)
            recentProjects.RemoveAll(p => string.Equals(p, projectPath, StringComparison.OrdinalIgnoreCase));
            
            // Add to the beginning of the list
            recentProjects.Insert(0, projectPath);
            
            // Limit the list size based on settings
            int maxRecent = settings.Appearance.MaxRecentProjects;
            if (maxRecent < 0) maxRecent = 10; // safety check
            while (recentProjects.Count > maxRecent)
            {
                recentProjects.RemoveAt(recentProjects.Count - 1);
            }
            
            // Save the updated settings
            SettingsManager.Instance.SaveSettings();
        }

        /// <summary>
        /// Gets the list of recent projects from settings
        /// </summary>
        public static List<string> GetRecentProjects()
        {
            var settings = SettingsManager.Instance.Settings;
            // Filter out projects that no longer exist
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