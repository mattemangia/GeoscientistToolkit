// GeoscientistToolkit/Data/Loaders/BoreholeBinaryLoader.cs

using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class BoreholeBinaryLoader : IDataLoader
{
    public string FilePath { get; set; } = "";
    public string Name => "Borehole Log (Binary)";
    public string Description => "Import a borehole dataset from a binary (.bhb) file.";
    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
    public string ValidationMessage => CanImport ? null : "Please select a valid borehole binary file.";

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Reading borehole binary file..."));

                var datasetName = Path.GetFileNameWithoutExtension(FilePath);
                var dataset = new BoreholeDataset(datasetName, FilePath);

                progressReporter?.Report((0.2f, "Parsing binary data..."));

                dataset.LoadFromBinaryFile(FilePath);

                progressReporter?.Report((1.0f, "Borehole dataset loaded successfully."));
                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[BoreholeBinaryLoader] Failed to load .bhb file: {ex.Message}");
                throw new Exception("Failed to load or parse the borehole binary file.", ex);
            }
        });
    }

    public void Reset()
    {
        FilePath = "";
    }
}