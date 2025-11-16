// GeoscientistToolkit/Data/Loaders/DualPNMLoader.cs

using System.Text.Json;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class DualPNMLoader : IDataLoader
{
    public string FilePath { get; set; } = "";
    public string Name => "Dual Pore Network Model";
    public string Description => "Import a Dual Pore Network Model from a JSON file";
    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
    public string ValidationMessage => CanImport ? null : "Please select a valid Dual PNM JSON file.";

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Reading Dual PNM file..."));

                var jsonString = File.ReadAllText(FilePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var dto = JsonSerializer.Deserialize<DualPNMDatasetDTO>(jsonString, options);

                if (dto == null)
                    throw new InvalidDataException("Failed to deserialize Dual PNM file.");

                progressReporter?.Report((0.5f, "Creating dataset..."));

                var dataset = new DualPNMDataset(dto.Name ?? Path.GetFileNameWithoutExtension(FilePath), FilePath);
                dataset.ImportFromDTO(dto);

                progressReporter?.Report((0.9f, "Finalizing dataset..."));

                Logger.Log($"Loaded Dual PNM dataset: {dataset.Name}");
                Logger.Log($"  Macro: {dataset.Pores.Count} pores, {dataset.Throats.Count} throats");
                Logger.Log($"  Micro: {dataset.TotalMicroPoreCount} pores, {dataset.TotalMicroThroatCount} throats");
                Logger.Log($"  Combined permeability: {dataset.Coupling.CombinedPermeability:F3} mD");

                progressReporter?.Report((1.0f, "Dual PNM dataset loaded successfully."));

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DualPNMLoader] Failed to load Dual PNM file: {ex.Message}");
                throw new Exception("Failed to load or parse the Dual PNM file.", ex);
            }
        });
    }

    public void Reset()
    {
        FilePath = "";
    }
}
