// GeoscientistToolkit/Business/ProjectManager.cs
// Complete implementation with dataset management functionality

using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business
{
    public class ProjectManager
    {
        private static ProjectManager _instance;
        private readonly List<Dataset> _loadedDatasets = new();
        private string _projectName = "Untitled Project";
        private string _projectPath = "";
        private bool _hasUnsavedChanges = false;

        // Singleton instance
        public static ProjectManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ProjectManager();
                }
                return _instance;
            }
        }

        // Properties
        public IReadOnlyList<Dataset> LoadedDatasets => _loadedDatasets;
        public string ProjectName => _projectName;
        public string ProjectPath => _projectPath;
        public bool HasUnsavedChanges => _hasUnsavedChanges;

        // Events
        public event EventHandler<Dataset> DatasetAdded;
        public event EventHandler<Dataset> DatasetRemoved;
        public event EventHandler ProjectChanged;

        private ProjectManager()
        {
            // Private constructor for singleton
        }

        public void NewProject()
        {
            // Clear all datasets
            _loadedDatasets.Clear();
            _projectName = "Untitled Project";
            _projectPath = "";
            _hasUnsavedChanges = false;
            
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AddDataset(Dataset dataset)
        {
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));

            if (_loadedDatasets.Any(d => d.Name == dataset.Name))
            {
                // Handle duplicate names by appending a number
                int counter = 1;
                string baseName = dataset.Name;
                while (_loadedDatasets.Any(d => d.Name == dataset.Name))
                {
                    dataset.Name = $"{baseName} ({counter})";
                    counter++;
                }
            }

            _loadedDatasets.Add(dataset);
            _hasUnsavedChanges = true;
            
            DatasetAdded?.Invoke(this, dataset);
        }

        public void RemoveDataset(Dataset dataset)
        {
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));

            if (_loadedDatasets.Remove(dataset))
            {
                _hasUnsavedChanges = true;
                DatasetRemoved?.Invoke(this, dataset);
                
                // Dispose of the dataset if it implements IDisposable
                if (dataset is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        public Dataset GetDatasetByName(string name)
        {
            return _loadedDatasets.FirstOrDefault(d => d.Name == name);
        }

        public void LoadProject(string projectPath)
        {
            // TODO: Implement project loading from file
            // This would deserialize the project file and load all referenced datasets
            throw new NotImplementedException("Project loading is not yet implemented");
        }

        public void SaveProject(string projectPath = null)
        {
            if (string.IsNullOrEmpty(projectPath) && string.IsNullOrEmpty(_projectPath))
            {
                throw new InvalidOperationException("No project path specified");
            }

            projectPath = projectPath ?? _projectPath;
            
            // TODO: Implement project saving to file
            // This would serialize the project structure and dataset references
            throw new NotImplementedException("Project saving is not yet implemented");
        }

        public void CloseProject()
        {
            // Dispose of all datasets
            foreach (var dataset in _loadedDatasets)
            {
                if (dataset is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            
            _loadedDatasets.Clear();
            _projectName = "Untitled Project";
            _projectPath = "";
            _hasUnsavedChanges = false;
            
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}