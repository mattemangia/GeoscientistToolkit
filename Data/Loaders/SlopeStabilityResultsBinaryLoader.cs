// GeoscientistToolkit/Data/Loaders/SlopeStabilityResultsBinaryLoader.cs

using System;
using System.IO;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class SlopeStabilityResultsBinaryLoader : IDataLoader
{
    public string FilePath { get; set; } = "";
    public string Name => "Slope Stability Results (Binary)";
    public string Description => "Import slope stability results from a binary (.ssr) file.";
    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
    public string ValidationMessage => CanImport ? null : "Please select a valid slope stability results file.";

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Reading slope stability results..."));

                var results = SlopeStabilityResultsBinarySerializer.Read(FilePath);

                var datasetName = Path.GetFileNameWithoutExtension(FilePath);
                var dataset = new SlopeStabilityDataset
                {
                    Name = datasetName,
                    FilePath = FilePath,
                    Results = results,
                    HasResults = true
                };

                progressReporter?.Report((1.0f, "Slope stability results loaded successfully."));
                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SlopeStabilityResultsBinaryLoader] Failed to load results: {ex.Message}");
                throw new Exception("Failed to load or parse the slope stability results file.", ex);
            }
        });
    }

    public void Reset()
    {
        FilePath = "";
    }
}
