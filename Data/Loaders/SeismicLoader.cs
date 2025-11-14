// GeoscientistToolkit/Data/Loaders/SeismicLoader.cs

using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
/// Loader for SEG-Y seismic datasets
/// </summary>
public class SeismicLoader : IDataLoader
{
    public string FilePath { get; set; } = "";

    public string Name => "Seismic Dataset (SEG-Y)";

    public string Description => "Load seismic reflection/refraction data from SEG-Y format files";

    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(FilePath))
                return "Please select a SEG-Y file";

            if (!File.Exists(FilePath))
                return "Selected file does not exist";

            return "";
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        try
        {
            progressReporter?.Report((0.0f, "Starting SEG-Y import..."));
            Logger.Log($"[SeismicLoader] Loading SEG-Y file: {FilePath}");

            // Parse the SEG-Y file
            progressReporter?.Report((0.1f, "Parsing SEG-Y file..."));
            var segyData = await SegyParser.ParseAsync(FilePath, progressReporter);

            if (segyData == null || segyData.Traces.Count == 0)
            {
                throw new Exception("Failed to parse SEG-Y file or file contains no traces");
            }

            // Create the dataset
            progressReporter?.Report((0.95f, "Creating dataset..."));
            var name = Path.GetFileNameWithoutExtension(FilePath);
            var dataset = new SeismicDataset(name, FilePath)
            {
                SegyData = segyData
            };

            // Extract metadata from SEG-Y header
            if (segyData.Header != null)
            {
                dataset.LineNumber = segyData.Header.LineNumber.ToString();

                // Parse textual header for additional info
                ParseTextualHeader(dataset, segyData.Header.TextualHeader);
            }

            // Create a default "Full Section" package
            if (segyData.Traces.Count > 0)
            {
                var fullSectionPackage = new SeismicLinePackage
                {
                    Name = "Full Section",
                    StartTrace = 0,
                    EndTrace = segyData.Traces.Count - 1,
                    IsVisible = true,
                    Color = new System.Numerics.Vector4(0.2f, 0.8f, 1.0f, 1.0f)
                };
                dataset.AddLinePackage(fullSectionPackage);
            }

            progressReporter?.Report((1.0f, $"Loaded {segyData.Traces.Count} traces successfully!"));
            Logger.Log($"[SeismicLoader] Successfully loaded {segyData.Traces.Count} traces from {FilePath}");

            return dataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SeismicLoader] Error loading SEG-Y file: {ex.Message}");
            progressReporter?.Report((0.0f, $"Error: {ex.Message}"));
            throw;
        }
    }

    public void Reset()
    {
        FilePath = "";
    }

    private void ParseTextualHeader(SeismicDataset dataset, string textualHeader)
    {
        if (string.IsNullOrEmpty(textualHeader))
            return;

        // Try to extract common information from textual header
        var lines = textualHeader.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var upper = line.ToUpperInvariant();

            // Look for survey name
            if (upper.Contains("SURVEY") || upper.Contains("CLIENT"))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2)
                {
                    dataset.SurveyName = parts[1].Trim();
                }
            }

            // Look for processing history
            if (upper.Contains("PROCESS") || upper.Contains("MIGRATION"))
            {
                if (upper.Contains("STACK"))
                    dataset.IsStack = true;
                if (upper.Contains("MIGRAT"))
                    dataset.IsMigrated = true;

                if (!string.IsNullOrEmpty(dataset.ProcessingHistory))
                    dataset.ProcessingHistory += "\n";
                dataset.ProcessingHistory += line.Trim();
            }

            // Look for data type
            if (upper.Contains("AMPLITUDE") || upper.Contains("VELOCITY") || upper.Contains("DEPTH"))
            {
                if (upper.Contains("VELOCITY"))
                    dataset.DataType = "velocity";
                else if (upper.Contains("DEPTH"))
                    dataset.DataType = "depth";
                else
                    dataset.DataType = "amplitude";
            }
        }
    }
}
