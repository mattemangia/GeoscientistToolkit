// GeoscientistToolkit/Data/Loaders/PhysicoChemLoader.cs

using System.Text.Json;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class PhysicoChemLoader : IDataLoader
{
    public string FilePath { get; set; } = "";
    public string Name => "PhysicoChem Reactor";
    public string Description => "Import a PhysicoChem reactor dataset from a JSON file";
    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
    public string ValidationMessage => CanImport ? null : "Please select a valid PhysicoChem JSON file.";

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Reading PhysicoChem file..."));

                var jsonString = File.ReadAllText(FilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dto = JsonSerializer.Deserialize<PhysicoChemDatasetDTO>(jsonString, options);

                if (dto == null)
                    throw new InvalidDataException("Failed to deserialize PhysicoChem file.");

                progressReporter?.Report((0.3f, "Creating dataset..."));

                // Create dataset with name from file
                var dataset = new PhysicoChemDataset(
                    Path.GetFileNameWithoutExtension(FilePath),
                    FilePath
                );

                progressReporter?.Report((0.5f, "Importing domains and conditions..."));

                // Import all data from DTO
                dataset.ImportFromDTO(dto);

                progressReporter?.Report((0.9f, "Finalizing dataset..."));

                // Set metadata
                if (!string.IsNullOrEmpty(dto.Description))
                {
                    dataset.Description = dto.Description;
                }

                progressReporter?.Report((1.0f, "PhysicoChem dataset loaded successfully."));

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PhysicoChemLoader] Failed to load PhysicoChem file: {ex.Message}");
                throw new Exception("Failed to load or parse the PhysicoChem file.", ex);
            }
        });
    }

    public void Reset()
    {
        FilePath = "";
    }
}
