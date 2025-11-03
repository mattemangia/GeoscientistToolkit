// GeoscientistToolkit/Data/GIS/SubsurfaceGISDatasetExtensions.cs

using System.Text.Json;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.GIS;

/// <summary>
/// Extension methods for saving and exporting SubsurfaceGISDataset
/// </summary>
public static class SubsurfaceGISDatasetExtensions
{
    /// <summary>
    /// Save the subsurface GIS dataset to a file
    /// </summary>
    /// <param name="dataset">The dataset to save</param>
    /// <param name="filePath">The file path to save to (should end with .subgis)</param>
    public static void SaveToFile(this SubsurfaceGISDataset dataset, string filePath)
    {
        if (dataset == null)
        {
            throw new ArgumentNullException(nameof(dataset));
        }

        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        try
        {
            Logger.Log($"Saving subsurface GIS dataset to: {filePath}");

            // Convert to DTO
            var dto = dataset.ToSerializableObject() as SubsurfaceGISDatasetDTO;

            if (dto == null)
            {
                throw new InvalidOperationException("Failed to convert dataset to DTO");
            }

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            var jsonText = JsonSerializer.Serialize(dto, options);

            // Write to file
            File.WriteAllText(filePath, jsonText);

            Logger.Log($"Successfully saved subsurface GIS dataset");
            Logger.Log($"  - File size: {new FileInfo(filePath).Length / 1024} KB");
            Logger.Log($"  - Voxels: {dataset.VoxelGrid.Count}");
            Logger.Log($"  - Layer boundaries: {dataset.LayerBoundaries.Count}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save subsurface GIS dataset: {ex.Message}");
            throw;
        }
    }

    

    /// <summary>
    /// Export voxel data to CSV format for external analysis
    /// </summary>
    /// <param name="dataset">The dataset to export</param>
    /// <param name="filePath">The CSV file path</param>
    public static void ExportVoxelsToCsv(this SubsurfaceGISDataset dataset, string filePath)
    {
        if (dataset == null)
        {
            throw new ArgumentNullException(nameof(dataset));
        }

        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        try
        {
            Logger.Log($"Exporting voxel data to CSV: {filePath}");

            using var writer = new StreamWriter(filePath);

            // Write header
            var headers = new List<string> { "X", "Y", "Z", "Lithology", "Confidence" };
            
            // Add parameter headers
            var allParameterNames = dataset.VoxelGrid
                .SelectMany(v => v.Parameters.Keys)
                .Distinct()
                .OrderBy(k => k)
                .ToList();
            
            headers.AddRange(allParameterNames);
            writer.WriteLine(string.Join(",", headers));

            // Write data rows
            foreach (var voxel in dataset.VoxelGrid)
            {
                var row = new List<string>
                {
                    voxel.Position.X.ToString("F3"),
                    voxel.Position.Y.ToString("F3"),
                    voxel.Position.Z.ToString("F3"),
                    $"\"{voxel.LithologyType}\"",
                    voxel.Confidence.ToString("F3")
                };

                // Add parameter values
                foreach (var paramName in allParameterNames)
                {
                    if (voxel.Parameters.TryGetValue(paramName, out var value))
                    {
                        row.Add(value.ToString("F3"));
                    }
                    else
                    {
                        row.Add("");
                    }
                }

                writer.WriteLine(string.Join(",", row));
            }

            Logger.Log($"Successfully exported {dataset.VoxelGrid.Count} voxels to CSV");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export voxels to CSV: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Export layer boundaries to CSV format
    /// </summary>
    /// <param name="dataset">The dataset to export</param>
    /// <param name="filePath">The CSV file path</param>
    public static void ExportLayerBoundariesToCsv(this SubsurfaceGISDataset dataset, string filePath)
    {
        if (dataset == null)
        {
            throw new ArgumentNullException(nameof(dataset));
        }

        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        try
        {
            Logger.Log($"Exporting layer boundaries to CSV: {filePath}");

            using var writer = new StreamWriter(filePath);

            // Write header
            writer.WriteLine("LayerName,X,Y,Z");

            // Write data rows
            foreach (var boundary in dataset.LayerBoundaries)
            {
                foreach (var point in boundary.Points)
                {
                    writer.WriteLine($"\"{boundary.LayerName}\",{point.X:F3},{point.Y:F3},{point.Z:F3}");
                }
            }

            var totalPoints = dataset.LayerBoundaries.Sum(b => b.Points.Count);
            Logger.Log($"Successfully exported {dataset.LayerBoundaries.Count} layer boundaries ({totalPoints} points) to CSV");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export layer boundaries to CSV: {ex.Message}");
            throw;
        }
    }
}