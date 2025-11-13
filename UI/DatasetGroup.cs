// GeoscientistToolkit/Data/DatasetGroup.cs

using System.Collections.Generic;
using System.Linq;

namespace GeoscientistToolkit.Data;

public class DatasetGroup : Dataset, ISerializableDataset
{
    public DatasetGroup(string name, List<Dataset> datasets) : base(name, string.Empty)
    {
        Type = DatasetType.Group;
        Datasets = new List<Dataset>(datasets ?? new List<Dataset>());

        // Set the file path to a descriptive string that won't be mistaken for a real file
        FilePath = $"[Group:{Name}]";
    }

    public List<Dataset> Datasets { get; }

    public object ToSerializableObject()
    {
        var dto = new DatasetGroupDTO
        {
            TypeName = nameof(DatasetGroup),
            Name = Name,
            FilePath = FilePath
        };

        foreach (var dataset in Datasets)
        {
            if (dataset is ISerializableDataset serializable)
            {
                // The ProjectSerializer will handle attaching metadata to this child DTO
                var childDto = (DatasetDTO)serializable.ToSerializableObject();
                dto.Datasets.Add(childDto);
            }
        }
        return dto;
    }

    public override long GetSizeInBytes()
    {
        return Datasets.Sum(d => d.GetSizeInBytes());
    }

    public override void Load()
    {
        // Groups don't load data directly; their child datasets are loaded individually.
    }

    public override void Unload()
    {
        // Child datasets are managed and unloaded by the ProjectManager.
    }

    public void AddDataset(Dataset dataset)
    {
        if (!Datasets.Contains(dataset)) Datasets.Add(dataset);
    }

    public void RemoveDataset(Dataset dataset)
    {
        Datasets.Remove(dataset);
    }

    public bool Contains(Dataset dataset)
    {
        return Datasets.Contains(dataset);
    }
}