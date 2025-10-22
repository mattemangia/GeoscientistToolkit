// GeoscientistToolkit/Data/TwoDGeology/TwoDGeologyDataset.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.UI.GIS;
using GeoscientistToolkit.Util;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;

namespace GeoscientistToolkit.Data.TwoDGeology;

/// <summary>
///     Represents a 2D geological profile dataset, saved as a binary file.
/// </summary>
public class TwoDGeologyDataset : Dataset, ISerializableDataset
{
    private TwoDGeologyViewer _viewer; // NEW: Reference to the viewer instance

    public TwoDGeologyDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.TwoDGeology;
    }

    public CrossSection ProfileData { get; set; }

    public object ToSerializableObject()
    {
        return new TwoDGeologyDatasetDTO
        {
            TypeName = nameof(TwoDGeologyDataset),
            Name = Name,
            FilePath = FilePath
        };
    }

    // NEW: Allow viewer to register itself with the dataset
    public void RegisterViewer(TwoDGeologyViewer viewer)
    {
        _viewer = viewer;
    }

    public TwoDGeologyViewer GetViewer()
    {
        return _viewer;
    }

    // NEW: Methods for the Flatten Tool to communicate with the viewer
    public void SetRestorationData(CrossSection data)
    {
        _viewer?.SetRestorationData(data);
    }

    public void ClearRestorationData()
    {
        _viewer?.ClearRestorationData();
    }


    public override long GetSizeInBytes()
    {
        if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath)) return new FileInfo(FilePath).Length;
        return 0;
    }

    public override void Load()
    {
        if (IsMissing || ProfileData != null)
            return;

        try
        {
            ProfileData = TwoDGeologySerializer.Read(FilePath);
            Logger.Log($"Loaded 2D Geology profile data for '{Name}'");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load 2D Geology profile from '{FilePath}': {ex.Message}");
            IsMissing = true;
        }
    }

    public static TwoDGeologyDataset CreateEmpty(string name, string filePath)
    {
        var dataset = new TwoDGeologyDataset(name, filePath);

        // Create a default empty cross-section with minimal structure
        dataset.ProfileData = new CrossSection
        {
            Profile = new GeologicalMapping.ProfileGenerator.TopographicProfile
            {
                Name = name,
                TotalDistance = 10000f, // Default 10km profile
                MinElevation = -2000f,
                MaxElevation = 1000f,
                StartPoint = new Vector2(0, 0),
                EndPoint = new Vector2(10000f, 0),
                CreatedAt = DateTime.Now,
                VerticalExaggeration = 2.0f,
                Points = new List<GeologicalMapping.ProfileGenerator.ProfilePoint>()
            },
            VerticalExaggeration = 2.0f,
            Formations = new List<ProjectedFormation>(),
            Faults = new List<GeologicalMapping.CrossSectionGenerator.ProjectedFault>()
        };

        // Generate default flat topography at sea level
        var numPoints = 50;
        for (var i = 0; i <= numPoints; i++)
        {
            var distance = i / (float)numPoints * 10000f;
            dataset.ProfileData.Profile.Points.Add(new GeologicalMapping.ProfileGenerator.ProfilePoint
            {
                Position = new Vector2(distance, 0),
                Distance = distance,
                Elevation = 0,
                Features = new List<GeologicalMapping.GeologicalFeature>()
            });
        }

        Logger.Log($"Created empty 2D geology dataset: {name}");
        return dataset;
    }

    public override void Unload()
    {
        ProfileData = null;
        _viewer?.UndoRedo.Clear(); // Clear undo history on unload
    }
}