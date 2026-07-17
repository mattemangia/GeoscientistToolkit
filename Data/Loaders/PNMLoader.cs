// GAIA/Data/Loaders/PNMLoader.cs

using System.Text.Json;
using GAIA.Data.Pnm;
using GAIA.Util;

namespace GAIA.Data.Loaders;

public class PNMLoader : IDataLoader
{
    public PNMLoader() { }
    public PNMLoader(string filePath) => FilePath = filePath ?? "";

    public string FilePath { get; set; } = "";
    public string Name => "Pore Network Model";
    public string Description => "Import a Pore Network Model from a JSON file";
    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
    public string ValidationMessage => CanImport ? null : "Please select a valid PNM JSON file.";

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Reading PNM file..."));

                var jsonString = File.ReadAllText(FilePath);
                var dto = JsonSerializer.Deserialize<PNMDatasetDTO>(jsonString, PnmJson.Indented);

                if (dto == null)
                    throw new InvalidDataException("Failed to deserialize PNM file.");

                progressReporter?.Report((0.5f, "Creating dataset..."));

                // Create dataset and import via DTO helper (this calls InitializeFromCurrentLists()).
                var dataset = new PNMDataset(Path.GetFileNameWithoutExtension(FilePath), FilePath);
                dataset.ImportFromDTO(dto);

                // If any dataset-level initialization beyond bounds, put it here.
                // ImportFromDTO() already rebuilt visible/original lists and calculated bounds.
                progressReporter?.Report((1.0f, "PNM dataset loaded successfully."));

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PNMLoader] Failed to load PNM file: {ex.Message}");
                throw new Exception("Failed to load or parse the PNM file.", ex);
            }
        });
    }

    public void Reset()
    {
        FilePath = "";
    }
}
