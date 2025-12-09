// GeoscientistToolkit/Data/Nerf/NerfGroup.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// A specialized group for organizing multiple NeRF datasets.
/// Supports combined scene rendering and batch operations.
/// </summary>
public class NerfGroup : Dataset, ISerializableDataset
{
    private readonly List<NerfDataset> _nerfDatasets = new();

    public NerfGroup(string name) : base(name, $"[NerfGroup:{name}]")
    {
        Type = DatasetType.Group;
    }

    public NerfGroup(string name, List<NerfDataset> datasets) : base(name, $"[NerfGroup:{name}]")
    {
        Type = DatasetType.Group;
        if (datasets != null)
        {
            _nerfDatasets.AddRange(datasets);
        }
    }

    /// <summary>
    /// Gets the list of NeRF datasets in this group.
    /// </summary>
    public IReadOnlyList<NerfDataset> NerfDatasets => _nerfDatasets.AsReadOnly();

    /// <summary>
    /// Gets all datasets including non-NeRF ones.
    /// </summary>
    public IReadOnlyList<Dataset> AllDatasets => _nerfDatasets.Cast<Dataset>().ToList().AsReadOnly();

    /// <summary>
    /// Total number of images across all NeRF datasets.
    /// </summary>
    public int TotalImageCount => _nerfDatasets.Sum(d => d.ImageCollection?.FrameCount ?? 0);

    /// <summary>
    /// Number of completed/trained NeRF models.
    /// </summary>
    public int TrainedModelCount => _nerfDatasets.Count(d => d.TrainingState == NerfTrainingState.Completed);

    /// <summary>
    /// Add a NeRF dataset to the group.
    /// </summary>
    public void AddDataset(NerfDataset dataset)
    {
        if (dataset != null && !_nerfDatasets.Contains(dataset))
        {
            _nerfDatasets.Add(dataset);
            Logger.Log($"Added '{dataset.Name}' to NeRF group '{Name}'");
        }
    }

    /// <summary>
    /// Remove a NeRF dataset from the group.
    /// </summary>
    public void RemoveDataset(NerfDataset dataset)
    {
        if (_nerfDatasets.Remove(dataset))
        {
            Logger.Log($"Removed '{dataset.Name}' from NeRF group '{Name}'");
        }
    }

    /// <summary>
    /// Check if a dataset is in this group.
    /// </summary>
    public bool Contains(NerfDataset dataset)
    {
        return _nerfDatasets.Contains(dataset);
    }

    /// <summary>
    /// Get combined scene bounds from all NeRF datasets.
    /// </summary>
    public (Vector3 center, float radius, Vector3 min, Vector3 max) GetCombinedSceneBounds()
    {
        if (_nerfDatasets.Count == 0)
        {
            return (Vector3.Zero, 1.0f, -Vector3.One, Vector3.One);
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var dataset in _nerfDatasets)
        {
            var collection = dataset.ImageCollection;
            if (collection == null) continue;

            min = Vector3.Min(min, collection.BoundingBoxMin);
            max = Vector3.Max(max, collection.BoundingBoxMax);
        }

        var center = (min + max) * 0.5f;
        var radius = Vector3.Distance(min, max) * 0.5f;

        return (center, Math.Max(radius, 0.1f), min, max);
    }

    /// <summary>
    /// Start training for all untrained datasets in the group.
    /// </summary>
    public async Task TrainAllAsync(IProgress<(int current, int total, string name)> progress = null)
    {
        var toTrain = _nerfDatasets
            .Where(d => d.TrainingState == NerfTrainingState.NotStarted ||
                       d.TrainingState == NerfTrainingState.Paused)
            .ToList();

        for (int i = 0; i < toTrain.Count; i++)
        {
            var dataset = toTrain[i];
            progress?.Report((i + 1, toTrain.Count, dataset.Name));

            try
            {
                var trainer = new NerfTrainer(dataset);
                trainer.StartTraining();

                // Wait for training to complete or fail
                while (dataset.TrainingState == NerfTrainingState.Training ||
                       dataset.TrainingState == NerfTrainingState.Preparing)
                {
                    await Task.Delay(100);
                }

                trainer.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Training failed for '{dataset.Name}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Export all trained models in the group.
    /// </summary>
    public void ExportAllModels(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        foreach (var dataset in _nerfDatasets.Where(d => d.ModelData != null))
        {
            var fileName = $"{SanitizeFileName(dataset.Name)}.nerfmodel";
            var filePath = Path.Combine(outputDirectory, fileName);
            dataset.SaveModel(filePath);
        }

        Logger.Log($"Exported {TrainedModelCount} models to: {outputDirectory}");
    }

    public override long GetSizeInBytes()
    {
        return _nerfDatasets.Sum(d => d.GetSizeInBytes());
    }

    public override void Load()
    {
        foreach (var dataset in _nerfDatasets)
        {
            dataset.Load();
        }
    }

    public override void Unload()
    {
        foreach (var dataset in _nerfDatasets)
        {
            dataset.Unload();
        }
    }

    public object ToSerializableObject()
    {
        return new NerfGroupDTO
        {
            TypeName = nameof(NerfGroup),
            Name = Name,
            FilePath = FilePath,
            Metadata = DatasetMetadata != null ? new DatasetMetadataDTO
            {
                SampleName = DatasetMetadata.SampleName,
                LocationName = DatasetMetadata.LocationName,
                Latitude = DatasetMetadata.Latitude,
                Longitude = DatasetMetadata.Longitude,
                Depth = DatasetMetadata.Depth,
                Notes = DatasetMetadata.Notes,
                CustomFields = DatasetMetadata.CustomFields
            } : new DatasetMetadataDTO(),
            NerfDatasets = _nerfDatasets
                .Select(d => d.ToSerializableObject() as NerfDatasetDTO)
                .Where(dto => dto != null)
                .ToList()
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// DTO for NerfGroup serialization.
/// </summary>
public class NerfGroupDTO : DatasetDTO
{
    public List<NerfDatasetDTO> NerfDatasets { get; set; } = new();
}
