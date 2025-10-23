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
    private TwoDGeologyViewer _viewer; // Reference to the viewer instance
    private bool _hasUnsavedChanges = false;

    public TwoDGeologyDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.TwoDGeology;
    }

    public CrossSection ProfileData { get; set; }
    
    public bool HasUnsavedChanges 
    { 
        get => _hasUnsavedChanges;
        set => _hasUnsavedChanges = value;
    }

    public object ToSerializableObject()
    {
        return new TwoDGeologyDatasetDTO
        {
            TypeName = nameof(TwoDGeologyDataset),
            Name = Name,
            FilePath = FilePath
        };
    }

    // Allow viewer to register itself with the dataset
    public void RegisterViewer(TwoDGeologyViewer viewer)
    {
        _viewer = viewer;
    }

    public TwoDGeologyViewer GetViewer()
    {
        return _viewer;
    }

    // Methods for the Flatten Tool to communicate with the viewer
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
        if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath)) 
            return new FileInfo(FilePath).Length;
        return 0;
    }

    public override void Load()
    {
        if (IsMissing || ProfileData != null)
            return;

        try
        {
            if (File.Exists(FilePath))
            {
                ProfileData = TwoDGeologySerializer.Read(FilePath);
                Logger.Log($"Loaded 2D Geology profile data for '{Name}'");
            }
            else
            {
                // Create new empty profile if file doesn't exist
                ProfileData = CreateDefaultProfile();
                Logger.Log($"Created new 2D Geology profile for '{Name}'");
                _hasUnsavedChanges = true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load 2D Geology profile from '{FilePath}': {ex.Message}");
            
            // Create empty profile on error
            ProfileData = CreateDefaultProfile();
            _hasUnsavedChanges = true;
        }
    }

    public void Save()
    {
        if (ProfileData == null)
        {
            Logger.LogWarning($"No profile data to save for '{Name}'");
            return;
        }

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            TwoDGeologySerializer.Write(FilePath, ProfileData);
            _hasUnsavedChanges = false;
            Logger.Log($"Saved 2D Geology profile to '{FilePath}'");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save 2D Geology profile to '{FilePath}': {ex.Message}");
            throw;
        }
    }

    public static TwoDGeologyDataset CreateEmpty(string name, string filePath)
    {
        var dataset = new TwoDGeologyDataset(name, filePath);
        dataset.ProfileData = CreateDefaultProfile();
        dataset._hasUnsavedChanges = true;
        
        Logger.Log($"Created empty 2D geology dataset: {name}");
        return dataset;
    }
    
    private static CrossSection CreateDefaultProfile()
    {
        var profile = new CrossSection
        {
            Profile = new GeologicalMapping.ProfileGenerator.TopographicProfile
            {
                Name = "Default Profile",
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

        // Generate default topography with some variation
        var numPoints = 50;
        var random = new Random(42); // Fixed seed for reproducibility
        
        for (var i = 0; i <= numPoints; i++)
        {
            var distance = i / (float)numPoints * profile.Profile.TotalDistance;
            
            // Create gentle hills using sine waves
            var baseElevation = 0f;
            baseElevation += 100f * MathF.Sin(distance / 2000f * MathF.PI);
            baseElevation += 50f * MathF.Sin(distance / 500f * MathF.PI);
            baseElevation += 25f * (float)(random.NextDouble() - 0.5); // Small random variation
            
            profile.Profile.Points.Add(new GeologicalMapping.ProfileGenerator.ProfilePoint
            {
                Position = new Vector2(distance, baseElevation),
                Distance = distance,
                Elevation = baseElevation,
                Features = new List<GeologicalMapping.GeologicalFeature>()
            });
        }
        
        // Add a default formation as an example
        var defaultFormation = new ProjectedFormation
        {
            Name = "Bedrock",
            Color = new Vector4(0.6f, 0.6f, 0.7f, 0.8f), // Gray color
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        // Create bedrock formation below topography
        for (var i = 0; i <= numPoints; i++)
        {
            var distance = i / (float)numPoints * profile.Profile.TotalDistance;
            var topElevation = profile.Profile.Points[i].Elevation - 100f; // 100m below surface
            var bottomElevation = topElevation - 500f; // 500m thick
            
            defaultFormation.TopBoundary.Add(new Vector2(distance, topElevation));
            defaultFormation.BottomBoundary.Add(new Vector2(distance, bottomElevation));
        }
        
        profile.Formations.Add(defaultFormation);
        
        return profile;
    }

    public override void Unload()
    {
        // Check for unsaved changes before unloading
        if (_hasUnsavedChanges)
        {
            Logger.LogWarning($"Unloading dataset '{Name}' with unsaved changes");
        }
        
        ProfileData = null;
        _viewer?.UndoRedo.Clear(); // Clear undo history on unload
    }
    
    // Mark dataset as modified when changes are made
    public void MarkAsModified()
    {
        _hasUnsavedChanges = true;
    }
}