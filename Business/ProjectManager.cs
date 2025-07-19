// GeoscientistToolkit/Business/ProjectManager.cs
using GeoscientistToolkit.Data;
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
            if (string.IsNullOrEmpty(path)) return;
            
            // TODO: Implement project serialization
            ProjectPath = path;
            HasUnsavedChanges = false;
            Logger.Log($"Project saved to: {path}");
        }

        public void LoadProject(string path)
        {
            if (!File.Exists(path)) return;
            
            // TODO: Implement project deserialization
            ProjectPath = path;
            HasUnsavedChanges = false;
            Logger.Log($"Project loaded from: {path}");
        }
    }
}