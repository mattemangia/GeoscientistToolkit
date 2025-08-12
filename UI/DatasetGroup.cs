// GeoscientistToolkit/Data/DatasetGroup.cs
using System.Collections.Generic;
using System.Linq;

namespace GeoscientistToolkit.Data
{
    public class DatasetGroup : Dataset, ISerializableDataset
    {
        public List<Dataset> Datasets { get; private set; }
        
        public DatasetGroup(string name, List<Dataset> datasets) : base(name, string.Empty)
        {
            Type = DatasetType.Group;
            Datasets = new List<Dataset>(datasets);
            
            // Set the file path to a descriptive string
            FilePath = $"Group of {datasets.Count} datasets";
        }
        
        public override long GetSizeInBytes()
        {
            return Datasets.Sum(d => d.GetSizeInBytes());
        }
        
        public override void Load()
        {
            // Groups don't load directly - individual datasets are loaded when needed
        }
        
        public override void Unload()
        {
            // Groups don't unload directly - individual datasets are unloaded when needed
        }
        
        public void AddDataset(Dataset dataset)
        {
            if (!Datasets.Contains(dataset))
            {
                Datasets.Add(dataset);
            }
        }
        
        public void RemoveDataset(Dataset dataset)
        {
            Datasets.Remove(dataset);
        }
        
        public bool Contains(Dataset dataset)
        {
            return Datasets.Contains(dataset);
        }

        public object ToSerializableObject()
        {
            var dto = new DatasetGroupDTO
            {
                TypeName = nameof(DatasetGroup),
                Name = this.Name,
                FilePath = this.FilePath
                // Metadata will be handled by ProjectSerializer
            };

            foreach (var dataset in Datasets)
            {
                if (dataset is ISerializableDataset serializable)
                {
                    var childDto = (DatasetDTO)serializable.ToSerializableObject();
                    // Child metadata will be handled by ProjectSerializer
                    dto.Datasets.Add(childDto);
                }
            }

            return dto;
        }
    }
}