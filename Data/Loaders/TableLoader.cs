// GeoscientistToolkit/Data/Loaders/TableLoader.cs

using System.Data;
using System.Text;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class TableLoader : IDataLoader
{
    public enum FileFormat
    {
        AutoDetect,
        CSV,
        TSV
    }

    private string _delimiter = ",";
    private DataTable _preview;

    public string FilePath { get; set; } = "";
    public FileFormat Format { get; set; } = FileFormat.AutoDetect;
    public bool HasHeaders { get; set; } = true;
    public string Encoding { get; set; } = "UTF-8";

    public string Name => "Table/Spreadsheet (CSV/TSV)";
    public string Description => "Import tabular data from CSV, TSV, or text files";

    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(FilePath))
                return "Please select a table file";
            if (!File.Exists(FilePath))
                return "Selected file does not exist";
            return null;
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Loading table data..."));

                var fileName = Path.GetFileName(FilePath);
                var dataset = new TableDataset(Path.GetFileNameWithoutExtension(fileName), FilePath);

                progressReporter?.Report((0.3f, "Parsing table structure..."));

                // Set properties
                _delimiter = GetDelimiter();
                dataset.Delimiter = _delimiter;
                dataset.HasHeaders = HasHeaders;
                dataset.Encoding = Encoding;

                // Determine source format
                var extension = Path.GetExtension(FilePath).ToLower();
                if (extension == ".csv")
                    dataset.SourceFormat = "CSV";
                else if (extension == ".tsv" || extension == ".tab")
                    dataset.SourceFormat = "TSV";
                else
                    dataset.SourceFormat = "TXT";

                progressReporter?.Report((0.6f, "Loading data into memory..."));

                // Load the data
                dataset.Load();

                progressReporter?.Report((1.0f,
                    $"Table imported successfully! ({dataset.RowCount} rows, {dataset.ColumnCount} columns)"));

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[TableLoader] Error importing table: {ex}");
                throw new Exception($"Failed to import table: {ex.Message}", ex);
            }
        });
    }

    public void Reset()
    {
        FilePath = "";
        Format = FileFormat.AutoDetect;
        HasHeaders = true;
        Encoding = "UTF-8";
        _delimiter = ",";
        _preview = null;
    }

    public string GetDelimiter()
    {
        if (Format == FileFormat.CSV)
            return ",";
        if (Format == FileFormat.TSV)
            return "\t";

        // Auto-detect based on file extension
        var extension = Path.GetExtension(FilePath).ToLower();
        if (extension == ".tsv" || extension == ".tab")
            return "\t";
        return ",";
    }

    public TableInfo GetTableInfo()
    {
        if (!File.Exists(FilePath))
            return null;

        try
        {
            var fileInfo = new FileInfo(FilePath);
            _delimiter = GetDelimiter();

            return new TableInfo
            {
                FileName = Path.GetFileName(FilePath),
                FileSize = fileInfo.Length,
                DetectedDelimiter = _delimiter == "\t" ? "Tab-separated" : "Comma-separated",
                Format = Path.GetExtension(FilePath).ToUpper().TrimStart('.')
            };
        }
        catch (Exception ex)
        {
            Logger.LogError($"[TableLoader] Error reading table info: {ex.Message}");
            return null;
        }
    }

    public DataTable GetPreview(int maxRows = 5)
    {
        if (!CanImport)
            return null;

        try
        {
            _preview = new DataTable("Preview");
            _delimiter = GetDelimiter();

            var encoding = System.Text.Encoding.GetEncoding(Encoding);
            using (var reader = new StreamReader(FilePath, encoding))
            {
                var firstLine = reader.ReadLine();
                if (string.IsNullOrEmpty(firstLine))
                    return _preview;

                var headers = ParseCsvLine(firstLine, _delimiter[0]);

                // Create columns
                if (HasHeaders)
                {
                    foreach (var header in headers) _preview.Columns.Add(header.Trim());
                }
                else
                {
                    for (var i = 0; i < headers.Length; i++) _preview.Columns.Add($"Column{i + 1}");
                    // Add first line as data
                    _preview.Rows.Add(headers);
                }

                // Read up to maxRows more lines for preview
                string line;
                var linesRead = 0;
                while ((line = reader.ReadLine()) != null && linesRead < maxRows)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var values = ParseCsvLine(line, _delimiter[0]);

                    // Ensure we have the right number of values
                    if (values.Length != _preview.Columns.Count) Array.Resize(ref values, _preview.Columns.Count);

                    _preview.Rows.Add(values);
                    linesRead++;
                }
            }

            return _preview;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[TableLoader] Failed to load preview: {ex.Message}");
            return null;
        }
    }

    private string[] ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var inQuotes = false;
        var currentField = new StringBuilder();

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    currentField.Append('"');
                    i++; // Skip next quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }

        result.Add(currentField.ToString());
        return result.ToArray();
    }

    public class TableInfo
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string DetectedDelimiter { get; set; }
        public string Format { get; set; }
    }
}