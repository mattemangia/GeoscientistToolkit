// GeoscientistToolkit/Data/Text/TextDataset.cs

using System.Text;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Text;

/// <summary>
/// Dataset for handling text documents (TXT, RTF)
/// </summary>
public class TextDataset : Dataset, ISerializableDataset
{
    public TextDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.Text;
    }

    // Text properties
    public string Content { get; set; }
    public string Format { get; set; } // "txt" or "rtf"
    public int LineCount { get; set; }
    public int CharacterCount { get; set; }
    public int WordCount { get; set; }
    public Encoding FileEncoding { get; set; } = Encoding.UTF8;

    // Optional metadata for generated reports
    public string GeneratedBy { get; set; } // e.g., "Ollama:llama2"
    public DateTime? GeneratedDate { get; set; }
    public bool IsGeneratedReport { get; set; }

    public object ToSerializableObject()
    {
        return new TextDatasetDTO
        {
            TypeName = nameof(TextDataset),
            Name = Name,
            FilePath = FilePath,
            Metadata = this.DatasetMetadata != null ? new DatasetMetadataDTO
            {
                SampleName = this.DatasetMetadata.SampleName,
                LocationName = this.DatasetMetadata.LocationName,
                Latitude = this.DatasetMetadata.Latitude,
                Longitude = this.DatasetMetadata.Longitude,
                CoordinatesX = this.DatasetMetadata.Coordinates?.X,
                CoordinatesY = this.DatasetMetadata.Coordinates?.Y,
                Depth = this.DatasetMetadata.Depth,
                SizeX = this.DatasetMetadata.Size?.X,
                SizeY = this.DatasetMetadata.Size?.Y,
                SizeZ = this.DatasetMetadata.Size?.Z,
                SizeUnit = this.DatasetMetadata.SizeUnit,
                CollectionDate = this.DatasetMetadata.CollectionDate,
                Collector = this.DatasetMetadata.Collector,
                Notes = this.DatasetMetadata.Notes,
                CustomFields = this.DatasetMetadata.CustomFields
            } : new DatasetMetadataDTO(),
            Format = Format,
            LineCount = LineCount,
            CharacterCount = CharacterCount,
            WordCount = WordCount,
            GeneratedBy = GeneratedBy,
            GeneratedDate = GeneratedDate,
            IsGeneratedReport = IsGeneratedReport
        };
    }

    public override long GetSizeInBytes()
    {
        if (File.Exists(FilePath))
            return new FileInfo(FilePath).Length;
        return 0;
    }

    public override void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                IsMissing = true;
                Logger.LogWarning($"Text file not found: {FilePath}");
                return;
            }

            var extension = Path.GetExtension(FilePath).ToLowerInvariant();
            Format = extension.TrimStart('.');

            // Detect encoding
            FileEncoding = DetectEncoding(FilePath);

            // Read content
            if (extension == ".rtf")
            {
                // For RTF files, we'll store the raw RTF content
                // A proper viewer would parse this, but we keep it simple
                Content = File.ReadAllText(FilePath, FileEncoding);
                Logger.Log($"Loaded RTF file: {FilePath}");
            }
            else
            {
                // Plain text
                Content = File.ReadAllText(FilePath, FileEncoding);
                Logger.Log($"Loaded text file: {FilePath}");
            }

            // Calculate statistics
            CharacterCount = Content.Length;
            LineCount = Content.Split('\n').Length;
            WordCount = CountWords(Content);

            IsMissing = false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load text file {FilePath}: {ex.Message}");
            IsMissing = true;
        }
    }

    public override void Unload()
    {
        // Text is usually small, but we can unload for memory optimization
        Content = null;
        GC.Collect();
    }

    /// <summary>
    /// Save the current content to file
    /// </summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, Content, FileEncoding);
            DateModified = DateTime.Now;
            Logger.Log($"Saved text file: {FilePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save text file {FilePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Export to a new file
    /// </summary>
    public void Export(string targetPath)
    {
        try
        {
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.WriteAllText(targetPath, Content, FileEncoding);
            Logger.Log($"Exported text to: {targetPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export text to {targetPath}: {ex.Message}");
        }
    }

    private static Encoding DetectEncoding(string filePath)
    {
        // Simple encoding detection
        var bytes = File.ReadAllBytes(filePath);

        // Check for BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // Default to UTF-8
        return Encoding.UTF8;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length;
    }
}
