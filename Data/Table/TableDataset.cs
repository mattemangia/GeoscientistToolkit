// GeoscientistToolkit/Data/Table/TableDataset.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Table
{
    public class TableDataset : Dataset, ISerializableDataset
    {
        // Table data storage
        private DataTable _dataTable;
        private readonly object _dataLock = new object();
        
        // Properties
        public int RowCount { get; private set; }
        public int ColumnCount { get; private set; }
        public List<string> ColumnNames { get; private set; } = new List<string>();
        public List<Type> ColumnTypes { get; private set; } = new List<Type>();
        public string SourceFormat { get; set; } // CSV, XLS, XLSX, Generated
        public string Delimiter { get; set; } = ","; // For CSV files
        public bool HasHeaders { get; set; } = true;
        public string Encoding { get; set; } = "UTF-8";
        
        // Statistics cache
        private Dictionary<string, ColumnStatistics> _columnStats = new Dictionary<string, ColumnStatistics>();
        
        public TableDataset(string name, string filePath) : base(name, filePath)
        {
            Type = DatasetType.Table;
            _dataTable = new DataTable(name);
        }
        
        // Constructor for generated tables (e.g., from analysis results)
        public TableDataset(string name, DataTable dataTable) : base(name, "")
        {
            Type = DatasetType.Table;
            _dataTable = dataTable ?? new DataTable(name);
            SourceFormat = "Generated";
            UpdateMetadata();
        }
        
        public override long GetSizeInBytes()
        {
            if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
            {
                return new FileInfo(FilePath).Length;
            }
            
            // Estimate size for generated tables
            lock (_dataLock)
            {
                if (_dataTable == null) return 0;
                
                long estimatedSize = 0;
                foreach (DataRow row in _dataTable.Rows)
                {
                    foreach (var item in row.ItemArray)
                    {
                        if (item is string str)
                            estimatedSize += str.Length * 2; // Unicode chars
                        else if (item is int || item is float)
                            estimatedSize += 4;
                        else if (item is double || item is long)
                            estimatedSize += 8;
                        else if (item is bool)
                            estimatedSize += 1;
                        else if (item is DateTime)
                            estimatedSize += 8;
                    }
                }
                return estimatedSize;
            }
        }
        
        public override void Load()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                // Generated table, already loaded
                return;
            }
            
            if (!File.Exists(FilePath))
            {
                Logger.LogError($"Table file not found: {FilePath}");
                IsMissing = true;
                return;
            }
            
            try
            {
                string extension = Path.GetExtension(FilePath).ToLower();
                
                lock (_dataLock)
                {
                    switch (extension)
                    {
                        case ".csv":
                        case ".tsv":
                        case ".txt":
                            LoadCsvFile();
                            SourceFormat = "CSV";
                            break;
                        case ".xls":
                        case ".xlsx":
                            LoadExcelFile();
                            SourceFormat = extension == ".xls" ? "XLS" : "XLSX";
                            break;
                        default:
                            throw new NotSupportedException($"File format '{extension}' is not supported for table datasets.");
                    }
                }
                
                UpdateMetadata();
                CalculateStatistics();
                Logger.Log($"Loaded table dataset: {Name} ({RowCount} rows, {ColumnCount} columns)");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load table dataset '{Name}': {ex.Message}");
                throw;
            }
        }
        
        private void LoadCsvFile()
        {
            _dataTable = new DataTable(Name);
            
            // Detect delimiter if TSV
            if (Path.GetExtension(FilePath).ToLower() == ".tsv")
                Delimiter = "\t";
            
            using (var reader = new StreamReader(FilePath, System.Text.Encoding.GetEncoding(Encoding)))
            {
                string headerLine = reader.ReadLine();
                if (string.IsNullOrEmpty(headerLine))
                {
                    throw new InvalidDataException("CSV file is empty");
                }
                
                // Auto-detect delimiter if not set
                if (Delimiter == ",")
                {
                    int commaCount = headerLine.Count(c => c == ',');
                    int tabCount = headerLine.Count(c => c == '\t');
                    int semicolonCount = headerLine.Count(c => c == ';');
                    
                    if (tabCount > commaCount && tabCount > semicolonCount)
                        Delimiter = "\t";
                    else if (semicolonCount > commaCount)
                        Delimiter = ";";
                }
                
                string[] headers = ParseCsvLine(headerLine, Delimiter[0]);
                
                // Create columns
                if (HasHeaders)
                {
                    foreach (string header in headers)
                    {
                        _dataTable.Columns.Add(header.Trim());
                    }
                }
                else
                {
                    for (int i = 0; i < headers.Length; i++)
                    {
                        _dataTable.Columns.Add($"Column{i + 1}");
                    }
                    // Add the first line as data if no headers
                    _dataTable.Rows.Add(headers);
                }
                
                // Read data rows
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    string[] values = ParseCsvLine(line, Delimiter[0]);
                    
                    // Ensure we have the right number of values
                    if (values.Length != _dataTable.Columns.Count)
                    {
                        // Pad or truncate as needed
                        Array.Resize(ref values, _dataTable.Columns.Count);
                    }
                    
                    _dataTable.Rows.Add(values);
                }
            }
            
            // Infer column types
            InferColumnTypes();
        }
        
        private void LoadExcelFile()
        {
            _dataTable = new DataTable(Name);
            
            
            
            using (var workbook = new XLWorkbook(FilePath))
            {
                var worksheet = workbook.Worksheet(1);
                var rows = worksheet.RangeUsed().RowsUsed();
                
                bool firstRow = true;
                foreach (var row in rows)
                {
                    if (firstRow && HasHeaders)
                    {
                        foreach (var cell in row.Cells())
                        {
                            _dataTable.Columns.Add(cell.Value.ToString());
                        }
                        firstRow = false;
                    }
                    else
                    {
                        var dataRow = _dataTable.NewRow();
                        int colIndex = 0;
                        foreach (var cell in row.Cells())
                        {
                            if (colIndex < _dataTable.Columns.Count)
                            {
                                dataRow[colIndex] = cell.Value.ToString();
                            }
                            colIndex++;
                        }
                        _dataTable.Rows.Add(dataRow);
                    }
                }
            }
            
        }
        
        private string[] ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var currentField = new System.Text.StringBuilder();
            
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                
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
        
        private void InferColumnTypes()
        {
            if (_dataTable.Rows.Count == 0) return;
            
            // Sample up to 100 rows to infer types
            int samplesToCheck = Math.Min(100, _dataTable.Rows.Count);
            var columnTypes = new Type[_dataTable.Columns.Count];
            
            for (int col = 0; col < _dataTable.Columns.Count; col++)
            {
                bool allInt = true;
                bool allDouble = true;
                bool allBool = true;
                bool allDateTime = true;
                
                for (int row = 0; row < samplesToCheck; row++)
                {
                    string value = _dataTable.Rows[row][col]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(value)) continue;
                    
                    if (!int.TryParse(value, out _)) allInt = false;
                    if (!double.TryParse(value, out _)) allDouble = false;
                    if (!bool.TryParse(value, out _)) allBool = false;
                    if (!DateTime.TryParse(value, out _)) allDateTime = false;
                }
                
                if (allBool)
                    columnTypes[col] = typeof(bool);
                else if (allInt)
                    columnTypes[col] = typeof(int);
                else if (allDouble)
                    columnTypes[col] = typeof(double);
                else if (allDateTime)
                    columnTypes[col] = typeof(DateTime);
                else
                    columnTypes[col] = typeof(string);
            }
            
            // Convert columns to inferred types
            var newTable = new DataTable(Name);
            for (int i = 0; i < _dataTable.Columns.Count; i++)
            {
                newTable.Columns.Add(_dataTable.Columns[i].ColumnName, columnTypes[i]);
            }
            
            foreach (DataRow oldRow in _dataTable.Rows)
            {
                var newRow = newTable.NewRow();
                for (int i = 0; i < _dataTable.Columns.Count; i++)
                {
                    string value = oldRow[i]?.ToString();
                    if (string.IsNullOrEmpty(value))
                    {
                        newRow[i] = DBNull.Value;
                    }
                    else
                    {
                        try
                        {
                            if (columnTypes[i] == typeof(int))
                                newRow[i] = int.Parse(value);
                            else if (columnTypes[i] == typeof(double))
                                newRow[i] = double.Parse(value);
                            else if (columnTypes[i] == typeof(bool))
                                newRow[i] = bool.Parse(value);
                            else if (columnTypes[i] == typeof(DateTime))
                                newRow[i] = DateTime.Parse(value);
                            else
                                newRow[i] = value;
                        }
                        catch
                        {
                            newRow[i] = value; // Keep as string if conversion fails
                        }
                    }
                }
                newTable.Rows.Add(newRow);
            }
            
            _dataTable = newTable;
        }
        
        private void UpdateMetadata()
        {
            lock (_dataLock)
            {
                if (_dataTable != null)
                {
                    RowCount = _dataTable.Rows.Count;
                    ColumnCount = _dataTable.Columns.Count;
                    
                    ColumnNames.Clear();
                    ColumnTypes.Clear();
                    
                    foreach (DataColumn column in _dataTable.Columns)
                    {
                        ColumnNames.Add(column.ColumnName);
                        ColumnTypes.Add(column.DataType);
                    }
                }
            }
        }
        
        private void CalculateStatistics()
        {
            lock (_dataLock)
            {
                _columnStats.Clear();
                
                foreach (DataColumn column in _dataTable.Columns)
                {
                    var stats = new ColumnStatistics
                    {
                        ColumnName = column.ColumnName,
                        DataType = column.DataType,
                        NonNullCount = 0,
                        UniqueCount = new HashSet<object>()
                    };
                    
                    if (column.DataType == typeof(double) || column.DataType == typeof(int) || column.DataType == typeof(float))
                    {
                        var numericValues = new List<double>();
                        
                        foreach (DataRow row in _dataTable.Rows)
                        {
                            if (row[column] != DBNull.Value && row[column] != null)
                            {
                                stats.NonNullCount++;
                                stats.UniqueCount.Add(row[column]);
                                
                                if (double.TryParse(row[column].ToString(), out double val))
                                {
                                    numericValues.Add(val);
                                }
                            }
                        }
                        
                        if (numericValues.Count > 0)
                        {
                            stats.Min = numericValues.Min();
                            stats.Max = numericValues.Max();
                            stats.Mean = numericValues.Average();
                            stats.StdDev = CalculateStdDev(numericValues, stats.Mean.Value);
                            
                            numericValues.Sort();
                            stats.Median = numericValues.Count % 2 == 0
                                ? (numericValues[numericValues.Count / 2 - 1] + numericValues[numericValues.Count / 2]) / 2
                                : numericValues[numericValues.Count / 2];
                        }
                    }
                    else
                    {
                        foreach (DataRow row in _dataTable.Rows)
                        {
                            if (row[column] != DBNull.Value && row[column] != null)
                            {
                                stats.NonNullCount++;
                                stats.UniqueCount.Add(row[column]);
                            }
                        }
                    }
                    
                    _columnStats[column.ColumnName] = stats;
                }
            }
        }
        
        private double CalculateStdDev(List<double> values, double mean)
        {
            if (values.Count <= 1) return 0;
            
            double sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }
        
        public override void Unload()
        {
            lock (_dataLock)
            {
                _dataTable?.Clear();
                _dataTable?.Dispose();
                _dataTable = null;
                _columnStats.Clear();
            }
        }
        
        public DataTable GetDataTable()
        {
            lock (_dataLock)
            {
                return _dataTable?.Copy();
            }
        }
        
        public ColumnStatistics GetColumnStatistics(string columnName)
        {
            return _columnStats.ContainsKey(columnName) ? _columnStats[columnName] : null;
        }
        
        public void SaveAsCsv(string filePath, string delimiter = ",", bool includeHeaders = true)
        {
            lock (_dataLock)
            {
                using (var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
                {
                    // Write headers
                    if (includeHeaders)
                    {
                        var headers = _dataTable.Columns.Cast<DataColumn>()
                            .Select(col => EscapeCsvField(col.ColumnName, delimiter[0]));
                        writer.WriteLine(string.Join(delimiter, headers));
                    }
                    
                    // Write data
                    foreach (DataRow row in _dataTable.Rows)
                    {
                        var values = row.ItemArray.Select(field => EscapeCsvField(field?.ToString() ?? "", delimiter[0]));
                        writer.WriteLine(string.Join(delimiter, values));
                    }
                }
            }
            
            Logger.Log($"Table saved to CSV: {filePath}");
        }
        
        private string EscapeCsvField(string field, char delimiter)
        {
            if (field.Contains(delimiter) || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }
        
        public object ToSerializableObject()
        {
            return new TableDatasetDTO
            {
                TypeName = nameof(TableDataset),
                Name = Name,
                FilePath = FilePath,
                SourceFormat = SourceFormat,
                Delimiter = Delimiter,
                HasHeaders = HasHeaders,
                Encoding = Encoding,
                RowCount = RowCount,
                ColumnCount = ColumnCount,
                ColumnNames = ColumnNames,
                ColumnTypes = ColumnTypes.Select(t => t.FullName).ToList()
                // Metadata will be handled by ProjectSerializer
            };
        }
    }
    
    public class ColumnStatistics
    {
        public string ColumnName { get; set; }
        public Type DataType { get; set; }
        public int NonNullCount { get; set; }
        public HashSet<object> UniqueCount { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Mean { get; set; }
        public double? Median { get; set; }
        public double? StdDev { get; set; }
    }
}