// GeoscientistToolkit/Business/ProjectManager.cs
// This class is responsible for managing the application's project files.
// It handles creating, loading, and saving project data, including the list of datasets.

using System.Collections.Generic;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business
{
    public class ProjectManager
    {
        private static ProjectManager _instance;
        public static ProjectManager Instance => _instance ??= new ProjectManager();

        public List<Dataset> LoadedDatasets { get; } = new List<Dataset>();

        private ProjectManager() { }

        public void NewProject()
        {
            LoadedDatasets.Clear();
            // In a real app, you would also reset other project-related settings.
        }

        public void LoadProject(string filePath)
        {
            // In a real app, you would deserialize a project file here.
            // For now, we just clear the current datasets.
            LoadedDatasets.Clear();
        }

        public void SaveProject(string filePath)
        {
            // In a real app, you would serialize the project data to a file.
        }

        public void AddDataset(Dataset dataset)
        {
            LoadedDatasets.Add(dataset);
        }
    }
}