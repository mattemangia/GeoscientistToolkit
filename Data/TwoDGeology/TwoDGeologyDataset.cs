// GeoscientistToolkit/Data/TwoDGeology/TwoDGeologyDataset.cs

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

    public CrossSection ProfileData { get; private set; }

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

    public override void Unload()
    {
        ProfileData = null;
        _viewer?.UndoRedo.Clear(); // Clear undo history on unload
    }
}