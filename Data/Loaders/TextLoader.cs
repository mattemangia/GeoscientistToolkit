// GeoscientistToolkit/Data/Loaders/TextLoader.cs

using GeoscientistToolkit.Data.Text;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
/// Loader for text documents (TXT, RTF)
/// </summary>
public class TextLoader : IDataLoader
{
    public string TextPath { get; set; } = "";

    public string Name => "Text Document";
    public string Description => "Import text files (TXT, RTF)";

    public bool CanImport => !string.IsNullOrEmpty(TextPath) && File.Exists(TextPath);

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(TextPath))
                return "Please select a text file";
            if (!File.Exists(TextPath))
                return "Selected file does not exist";

            var extension = Path.GetExtension(TextPath).ToLowerInvariant();
            if (extension != ".txt" && extension != ".rtf")
                return "Please select a TXT or RTF file";

            return "";
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        var lp = new LoaderProgress(progressReporter);

        return await Task.Run(() =>
        {
            try
            {
                lp.Report(0.1f, "Loading text file...");

                if (!File.Exists(TextPath))
                {
                    Logger.LogError($"Text file not found: {TextPath}");
                    return null;
                }

                var fileName = Path.GetFileNameWithoutExtension(TextPath);

                lp.Report(0.3f, "Creating dataset...");

                // Create dataset
                var dataset = new TextDataset(fileName, TextPath);

                lp.Report(0.5f, "Loading content...");

                // Load content
                dataset.Load();

                lp.Report(1.0f, "Complete");

                Logger.Log($"Loaded text file: {TextPath} ({dataset.CharacterCount} chars, {dataset.LineCount} lines)");

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load text file {TextPath}: {ex.Message}");
                return null;
            }
        });
    }

    public void Reset()
    {
        TextPath = "";
    }

    /// <summary>
    /// Load a text file into a TextDataset (direct method)
    /// </summary>
    private async Task<TextDataset> LoadFileAsync(string filePath, IProgress<float> progress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                progress?.Report(0.1f);

                if (!File.Exists(filePath))
                {
                    Logger.LogError($"Text file not found: {filePath}");
                    return null;
                }

                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath).ToLowerInvariant();

                // Validate file type
                if (extension != ".txt" && extension != ".rtf")
                {
                    Logger.LogError($"Unsupported text file format: {extension}");
                    return null;
                }

                progress?.Report(0.3f);

                // Create dataset
                var dataset = new TextDataset(fileName, filePath);

                progress?.Report(0.5f);

                // Load content
                dataset.Load();

                progress?.Report(1.0f);

                Logger.Log($"Loaded text file: {filePath} ({dataset.CharacterCount} chars, {dataset.LineCount} lines)");

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load text file {filePath}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// Create a new empty text dataset
    /// </summary>
    public TextDataset CreateNew(string name, string filePath)
    {
        try
        {
            var dataset = new TextDataset(name, filePath)
            {
                Content = "",
                Format = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
                LineCount = 0,
                CharacterCount = 0,
                WordCount = 0
            };

            // Create the file
            File.WriteAllText(filePath, "");

            Logger.Log($"Created new text file: {filePath}");

            return dataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create text file {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Create a text dataset from content (e.g., generated report)
    /// </summary>
    public TextDataset CreateFromContent(string name, string filePath, string content, string generatedBy = null)
    {
        try
        {
            var dataset = new TextDataset(name, filePath)
            {
                Content = content,
                Format = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
                LineCount = content.Split('\n').Length,
                CharacterCount = content.Length,
                WordCount = CountWords(content),
                IsGeneratedReport = !string.IsNullOrEmpty(generatedBy),
                GeneratedBy = generatedBy,
                GeneratedDate = DateTime.Now
            };

            // Save to file
            File.WriteAllText(filePath, content);

            Logger.Log($"Created text file from content: {filePath}");

            return dataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create text file from content {filePath}: {ex.Message}");
            return null;
        }
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length;
    }
}
