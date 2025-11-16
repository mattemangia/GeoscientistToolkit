// GeoscientistToolkit/Data/Loaders/DualPNMLoader.cs

using System.Text.Json;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class DualPNMLoader : IDataLoader
{
    public Dataset Load(string filePath)
    {
        try
        {
            var jsonString = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var dto = JsonSerializer.Deserialize<DualPNMDatasetDTO>(jsonString, options);

            if (dto == null)
            {
                Logger.LogError($"Failed to deserialize Dual PNM dataset from: {filePath}");
                return null;
            }

            var dataset = new DualPNMDataset(dto.Name ?? Path.GetFileNameWithoutExtension(filePath), filePath);
            dataset.ImportFromDTO(dto);

            Logger.Log($"Loaded Dual PNM dataset: {dataset.Name}");
            Logger.Log($"  Macro: {dataset.Pores.Count} pores, {dataset.Throats.Count} throats");
            Logger.Log($"  Micro: {dataset.TotalMicroPoreCount} pores, {dataset.TotalMicroThroatCount} throats");
            Logger.Log($"  Combined permeability: {dataset.Coupling.CombinedPermeability:F3} mD");

            return dataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading Dual PNM dataset from {filePath}: {ex.Message}");
            return null;
        }
    }

    public bool CanLoad(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLower();

        // Support both .dualpnm.json and .json extensions
        if (extension == ".json")
        {
            // Check if it's a dual PNM file by looking at the content
            try
            {
                var jsonString = File.ReadAllText(filePath);
                return jsonString.Contains("\"TypeName\"") &&
                       (jsonString.Contains("DualPNMDataset") || jsonString.Contains("DualPNM"));
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    public List<string> GetSupportedExtensions()
    {
        return new List<string> { ".json", ".dualpnm.json" };
    }
}
