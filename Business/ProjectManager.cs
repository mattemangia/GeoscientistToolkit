// GeoscientistToolkit/Business/ProjectManager.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
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

        private ProjectManager() { }

        public void NewProject()
        {
            // Clear all loaded datasets
            foreach (var dataset in LoadedDatasets)
            {
                dataset.Unload();
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
            
            dataset.Unload();
            LoadedDatasets.Remove(dataset);
            HasUnsavedChanges = true;
            Logger.Log($"Removed dataset: {dataset.Name}");
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
        }

        public void LoadProject(string path)
        {
            if (!File.Exists(path))
            {
                Logger.LogError($"Project file not found: {path}");
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
    }
}