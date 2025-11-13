// GeoscientistToolkit/Data/Loaders/SubsurfaceGISLoader.cs

using System.Text.Json;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
/// Loader for Subsurface GIS Dataset files (.subgis format)
/// </summary>
public class SubsurfaceGISLoader : IDataLoader
{
    public string FilePath { get; set; }

    public string Name => "Subsurface GIS Dataset";
    
    public string Description => "Load 3D subsurface geological models with voxel grids and layer boundaries (.subgis)";
    
    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
    
    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(FilePath))
                return "Please select a .subgis file";
            if (!File.Exists(FilePath))
                return "File does not exist";
            return "Ready to import";
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progress)
    {
        if (!CanImport)
        {
            throw new InvalidOperationException("Cannot import: invalid file path");
        }

        progress?.Report((0f, "Loading subsurface GIS data..."));

        try
        {
            // Read the JSON file
            var jsonText = await File.ReadAllTextAsync(FilePath);
            
            progress?.Report((0.3f, "Parsing JSON data..."));

            // Deserialize to DTO
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                IncludeFields = true
            };

            SubsurfaceGISDatasetDTO dto = JsonSerializer.Deserialize<Data.SubsurfaceGISDatasetDTO>(jsonText, options);

            if (dto == null)
            {
                throw new InvalidDataException("Failed to deserialize subsurface GIS data");
            }

            progress?.Report((0.6f, "Creating subsurface dataset..."));

            // Create the dataset from DTO
            var dataset = new SubsurfaceGISDataset(dto);

            progress?.Report((0.9f, "Finalizing..."));

            Logger.Log($"Successfully loaded subsurface GIS dataset: {dataset.Name}");
            Logger.Log($"  - Voxels: {dataset.VoxelGrid.Count}");
            Logger.Log($"  - Layer boundaries: {dataset.LayerBoundaries.Count}");
            Logger.Log($"  - Source boreholes: {dataset.SourceBoreholeNames.Count}");
            Logger.Log($"  - Grid resolution: {dataset.GridResolutionX}x{dataset.GridResolutionY}x{dataset.GridResolutionZ}");

            progress?.Report((1f, "Complete!"));

            return dataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load subsurface GIS dataset: {ex.Message}");
            throw new InvalidDataException($"Failed to load subsurface GIS dataset: {ex.Message}", ex);
        }
    }

    public void Reset()
    {
        FilePath = null;
    }

    public object GetFileInfo()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(FilePath);
        
        return new
        {
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
            LastModified = fileInfo.LastWriteTime,
            IsValid = fileInfo.Extension.Equals(".subgis", StringComparison.OrdinalIgnoreCase) ||
                     fileInfo.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
        };
    }
}