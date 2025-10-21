// GeoscientistToolkit/Data/Loaders/TwoDGeologyLoader.cs

using GeoscientistToolkit.Data.TwoDGeology;

namespace GeoscientistToolkit.Data.Loaders;

public class TwoDGeologyLoader : IDataLoader
{
    public string FilePath { get; set; }
    public string Name => "2D Geology Profile Loader";
    public string Description => "Loads a binary 2D geological profile file (.2dgeo).";
    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
    public string ValidationMessage => CanImport ? "Ready to import." : "Please select a valid .2dgeo file.";

    public Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return Task.Run(() =>
        {
            var progress = new LoaderProgress(progressReporter);
            progress.Report(0.1f, "Reading 2D geology file...");

            var datasetName = Path.GetFileNameWithoutExtension(FilePath);
            var dataset = new TwoDGeologyDataset(datasetName, FilePath);

            // Load is synchronous but we wrap it in Task.Run for the interface
            dataset.Load();

            progress.Report(1.0f, "Import complete.");
            return (Dataset)dataset;
        });
    }

    public void Reset()
    {
        FilePath = null;
    }
}