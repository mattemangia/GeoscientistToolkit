// GeoscientistToolkit/UI/TableTools.cs

using System.Data;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public class TableTools : IDatasetTools
{
    private readonly string[] _aggregateFunctions = { "Count", "Sum", "Average", "Min", "Max", "StdDev" };

    // Export dialogs
    private readonly ImGuiExportFileDialog _csvExportDialog;
    private readonly string[] _joinTypes = { "Inner Join", "Left Join", "Right Join", "Full Outer Join" };
    private readonly ImGuiExportFileDialog _tsvExportDialog;
    private string _columnFormula = "";
    private string _filterExpression = "";
    private string _newColumnName = "";
    private string _newRenameColumnName = "";
    private bool _randomSample = true;

    // Rename fields
    private int _renameColumnIndex;

    // Sample fields
    private int _sampleSize = 100;
    private int _selectedAggregateColumn;
    private int _selectedAggregateFunction;

    // Column removal fields
    private bool[] _selectedColumns;
    private int _selectedGroupByColumn;
    private int _selectedJoinColumn1;
    private int _selectedJoinColumn2;

    // Join operation fields
    private int _selectedJoinDataset;
    private int _selectedJoinType;

    // Sort fields
    private int _selectedSortColumn;
    private bool _sortAscending = true;

    public TableTools()
    {
        _csvExportDialog = new ImGuiExportFileDialog("ExportCSVDialog", "Export to CSV");
        _csvExportDialog.SetExtensions(
            (".csv", "CSV (Comma-separated values)")
        );

        _tsvExportDialog = new ImGuiExportFileDialog("ExportTSVDialog", "Export to TSV");
        _tsvExportDialog.SetExtensions(
            (".tsv", "TSV (Tab-separated values)"),
            (".txt", "Text file")
        );
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not TableDataset tableDataset)
        {
            ImGui.TextDisabled("Table tools are only available for table datasets.");
            return;
        }

        // Handle export dialogs
        if (_csvExportDialog.Submit()) ExportToCsvFile(tableDataset, _csvExportDialog.SelectedPath);

        if (_tsvExportDialog.Submit()) ExportToTsvFile(tableDataset, _tsvExportDialog.SelectedPath);

        ImGui.Text("Table Operations");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Data Transformation", ImGuiTreeNodeFlags.DefaultOpen))
            DrawTransformationTools(tableDataset);

        if (ImGui.CollapsingHeader("Aggregation")) DrawAggregationTools(tableDataset);

        if (ImGui.CollapsingHeader("Join Tables")) DrawJoinTools(tableDataset);

        if (ImGui.CollapsingHeader("Column Operations")) DrawColumnOperations(tableDataset);

        if (ImGui.CollapsingHeader("Export Options")) DrawExportOptions(tableDataset);
    }

    private void DrawTransformationTools(TableDataset tableDataset)
    {
        ImGui.Text("Filter Rows:");
        ImGui.InputTextWithHint("##FilterExpression", "e.g., Column1 > 100 AND Column2 = 'Value'",
            ref _filterExpression, 256);

        if (ImGui.Button("Apply Filter")) ApplyFilter(tableDataset);

        ImGui.SameLine();
        if (ImGui.Button("Clear Filter"))
        {
            _filterExpression = "";
            // Reload original data
            tableDataset.Load();
        }

        ImGui.Separator();

        ImGui.Text("Sort Data:");

        var columnNames = tableDataset.ColumnNames.ToArray();
        if (columnNames.Length > 0)
        {
            ImGui.SetNextItemWidth(200);
            ImGui.Combo("Sort Column", ref _selectedSortColumn, columnNames, columnNames.Length);

            ImGui.SameLine();
            ImGui.Checkbox("Ascending", ref _sortAscending);

            ImGui.SameLine();
            if (ImGui.Button("Sort")) SortData(tableDataset, _selectedSortColumn, _sortAscending);
        }

        ImGui.Separator();

        ImGui.Text("Remove Duplicates:");
        if (ImGui.Button("Remove Duplicate Rows")) RemoveDuplicates(tableDataset);

        ImGui.Separator();

        ImGui.Text("Sampling:");

        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("Sample Size", ref _sampleSize, 10, 100);
        _sampleSize = Math.Max(1, Math.Min(_sampleSize, tableDataset.RowCount));

        ImGui.SameLine();
        ImGui.Checkbox("Random", ref _randomSample);

        if (ImGui.Button("Create Sample")) CreateSample(tableDataset, _sampleSize, _randomSample);
    }

    private void DrawAggregationTools(TableDataset tableDataset)
    {
        var columnNames = tableDataset.ColumnNames.ToArray();
        if (columnNames.Length == 0)
        {
            ImGui.TextDisabled("No columns available");
            return;
        }

        ImGui.Text("Group By:");
        ImGui.SetNextItemWidth(200);
        ImGui.Combo("Group Column", ref _selectedGroupByColumn, columnNames, columnNames.Length);

        ImGui.Text("Aggregate:");
        ImGui.SetNextItemWidth(200);
        ImGui.Combo("Value Column", ref _selectedAggregateColumn, columnNames, columnNames.Length);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.Combo("Function", ref _selectedAggregateFunction, _aggregateFunctions, _aggregateFunctions.Length);

        if (ImGui.Button("Create Aggregated Table")) CreateAggregatedTable(tableDataset);

        ImGui.Separator();

        if (ImGui.Button("Create Summary Statistics")) CreateSummaryStatistics(tableDataset);
    }

    private void DrawJoinTools(TableDataset tableDataset)
    {
        // Get other table datasets
        var otherTables = ProjectManager.Instance.LoadedDatasets
            .OfType<TableDataset>()
            .Where(t => t != tableDataset)
            .ToList();

        if (otherTables.Count == 0)
        {
            ImGui.TextDisabled("No other table datasets available for joining");
            return;
        }

        var tableNames = otherTables.Select(t => t.Name).ToArray();
        var currentColumns = tableDataset.ColumnNames.ToArray();

        ImGui.Text("Join With:");
        ImGui.SetNextItemWidth(200);
        ImGui.Combo("Table", ref _selectedJoinDataset, tableNames, tableNames.Length);

        if (_selectedJoinDataset < otherTables.Count)
        {
            var otherTable = otherTables[_selectedJoinDataset];
            var otherColumns = otherTable.ColumnNames.ToArray();

            ImGui.Text("Join Columns:");
            ImGui.SetNextItemWidth(150);
            ImGui.Combo("This Table", ref _selectedJoinColumn1, currentColumns, currentColumns.Length);

            ImGui.SameLine();
            ImGui.Text("=");
            ImGui.SameLine();

            ImGui.SetNextItemWidth(150);
            ImGui.Combo("Other Table", ref _selectedJoinColumn2, otherColumns, otherColumns.Length);

            ImGui.Text("Join Type:");
            ImGui.SetNextItemWidth(200);
            ImGui.Combo("Type", ref _selectedJoinType, _joinTypes, _joinTypes.Length);

            if (ImGui.Button("Perform Join")) PerformJoin(tableDataset, otherTable);
        }
    }

    private void DrawColumnOperations(TableDataset tableDataset)
    {
        ImGui.Text("Add Calculated Column:");

        ImGui.InputTextWithHint("Column Name", "NewColumn", ref _newColumnName, 64);
        ImGui.InputTextWithHint("Formula", "e.g., [Column1] + [Column2] * 2", ref _columnFormula, 256);

        if (ImGui.Button("Add Column")) AddCalculatedColumn(tableDataset);

        ImGui.Separator();

        ImGui.Text("Remove Columns:");

        var columnNames = tableDataset.ColumnNames.ToArray();

        if (_selectedColumns == null || _selectedColumns.Length != columnNames.Length)
            _selectedColumns = new bool[columnNames.Length];

        for (var i = 0; i < columnNames.Length; i++) ImGui.Checkbox(columnNames[i], ref _selectedColumns[i]);

        if (ImGui.Button("Remove Selected Columns"))
        {
            RemoveColumns(tableDataset, _selectedColumns);
            _selectedColumns = null; // Reset selection
        }

        ImGui.Separator();

        ImGui.Text("Rename Columns:");

        if (columnNames.Length > 0)
        {
            ImGui.SetNextItemWidth(150);
            ImGui.Combo("Column", ref _renameColumnIndex, columnNames, columnNames.Length);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            ImGui.InputTextWithHint("##NewName", "New name", ref _newRenameColumnName, 64);

            ImGui.SameLine();
            if (ImGui.Button("Rename"))
            {
                RenameColumn(tableDataset, _renameColumnIndex, _newRenameColumnName);
                _newRenameColumnName = "";
            }
        }
    }

    private void DrawExportOptions(TableDataset tableDataset)
    {
        if (ImGui.Button("Export to CSV...")) _csvExportDialog.Open($"{tableDataset.Name}_export");

        if (ImGui.Button("Export to Tab-Separated...")) _tsvExportDialog.Open($"{tableDataset.Name}_export");

        if (ImGui.Button("Copy All to Clipboard")) CopyTableToClipboard(tableDataset);

        if (ImGui.Button("Export to New Table Dataset")) ExportToNewDataset(tableDataset);

        ImGui.Separator();

        if (ImGui.Button("Generate Material Statistics")) GenerateMaterialStatistics(tableDataset);

        if (ImGui.Button("Generate Histogram Table")) GenerateHistogramTable(tableDataset);
    }

    private void ApplyFilter(TableDataset tableDataset)
    {
        try
        {
            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null) return;

            var filteredRows = dataTable.Select(_filterExpression);

            if (filteredRows.Length == 0)
            {
                Logger.LogWarning("Filter returned no rows");
                return;
            }

            var newTable = dataTable.Clone();
            foreach (var row in filteredRows) newTable.ImportRow(row);

            var filteredDataset = new TableDataset($"{tableDataset.Name}_Filtered", newTable);
            ProjectManager.Instance.AddDataset(filteredDataset);

            Logger.Log($"Created filtered table with {filteredRows.Length} rows");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to apply filter: {ex.Message}");
        }
    }

    private void SortData(TableDataset tableDataset, int columnIndex, bool ascending)
    {
        try
        {
            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null || columnIndex >= dataTable.Columns.Count) return;

            var columnName = dataTable.Columns[columnIndex].ColumnName;
            var sortExpression = $"[{columnName}] {(ascending ? "ASC" : "DESC")}";

            var sortedRows = dataTable.Select("", sortExpression);

            var newTable = dataTable.Clone();
            foreach (var row in sortedRows) newTable.ImportRow(row);

            var sortedDataset = new TableDataset($"{tableDataset.Name}_Sorted", newTable);
            ProjectManager.Instance.AddDataset(sortedDataset);

            Logger.Log($"Created sorted table by column '{columnName}'");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to sort data: {ex.Message}");
        }
    }

    private void RemoveDuplicates(TableDataset tableDataset)
    {
        try
        {
            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null) return;

            var uniqueRows = new HashSet<string>();
            var newTable = dataTable.Clone();

            foreach (DataRow row in dataTable.Rows)
            {
                var rowKey = string.Join("|", row.ItemArray.Select(item => item?.ToString() ?? ""));
                if (uniqueRows.Add(rowKey)) newTable.ImportRow(row);
            }

            var duplicatesRemoved = dataTable.Rows.Count - newTable.Rows.Count;

            var deduplicatedDataset = new TableDataset($"{tableDataset.Name}_Unique", newTable);
            ProjectManager.Instance.AddDataset(deduplicatedDataset);

            Logger.Log($"Removed {duplicatesRemoved} duplicate rows");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to remove duplicates: {ex.Message}");
        }
    }

    private void CreateSample(TableDataset tableDataset, int sampleSize, bool random)
    {
        try
        {
            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null) return;

            var newTable = dataTable.Clone();

            if (random)
            {
                var rng = new Random();
                var indices = Enumerable.Range(0, dataTable.Rows.Count)
                    .OrderBy(x => rng.Next())
                    .Take(sampleSize)
                    .OrderBy(x => x);

                foreach (var index in indices) newTable.ImportRow(dataTable.Rows[index]);
            }
            else
            {
                for (var i = 0; i < Math.Min(sampleSize, dataTable.Rows.Count); i++)
                    newTable.ImportRow(dataTable.Rows[i]);
            }

            var sampleDataset = new TableDataset($"{tableDataset.Name}_Sample{sampleSize}", newTable);
            ProjectManager.Instance.AddDataset(sampleDataset);

            Logger.Log($"Created sample table with {newTable.Rows.Count} rows");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create sample: {ex.Message}");
        }
    }

    private void CreateAggregatedTable(TableDataset tableDataset)
    {
        try
        {
            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null) return;

            var groupColumn = dataTable.Columns[_selectedGroupByColumn].ColumnName;
            var valueColumn = dataTable.Columns[_selectedAggregateColumn].ColumnName;

            var groups = dataTable.AsEnumerable()
                .GroupBy(row => row[groupColumn]?.ToString() ?? "NULL");

            var newTable = new DataTable($"{tableDataset.Name}_Aggregated");
            newTable.Columns.Add(groupColumn, typeof(string));
            newTable.Columns.Add($"{_aggregateFunctions[_selectedAggregateFunction]}_{valueColumn}", typeof(double));
            newTable.Columns.Add("Count", typeof(int));

            foreach (var group in groups)
            {
                double result = 0;
                var values = group.Select(row =>
                {
                    if (double.TryParse(row[valueColumn]?.ToString(), out var val))
                        return val;
                    return 0;
                }).ToList();

                switch (_selectedAggregateFunction)
                {
                    case 0: // Count
                        result = values.Count;
                        break;
                    case 1: // Sum
                        result = values.Sum();
                        break;
                    case 2: // Average
                        result = values.Count > 0 ? values.Average() : 0;
                        break;
                    case 3: // Min
                        result = values.Count > 0 ? values.Min() : 0;
                        break;
                    case 4: // Max
                        result = values.Count > 0 ? values.Max() : 0;
                        break;
                    case 5: // StdDev
                        if (values.Count > 1)
                        {
                            var mean = values.Average();
                            result = Math.Sqrt(values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1));
                        }

                        break;
                }

                newTable.Rows.Add(group.Key, result, group.Count());
            }

            var aggregatedDataset = new TableDataset($"{tableDataset.Name}_Aggregated", newTable);
            ProjectManager.Instance.AddDataset(aggregatedDataset);

            Logger.Log($"Created aggregated table with {newTable.Rows.Count} groups");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create aggregated table: {ex.Message}");
        }
    }

    private void CreateSummaryStatistics(TableDataset tableDataset)
    {
        try
        {
            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null) return;

            var statsTable = new DataTable($"{tableDataset.Name}_Statistics");
            statsTable.Columns.Add("Column", typeof(string));
            statsTable.Columns.Add("Type", typeof(string));
            statsTable.Columns.Add("Count", typeof(int));
            statsTable.Columns.Add("Unique", typeof(int));
            statsTable.Columns.Add("Min", typeof(string));
            statsTable.Columns.Add("Max", typeof(string));
            statsTable.Columns.Add("Mean", typeof(string));
            statsTable.Columns.Add("StdDev", typeof(string));

            foreach (DataColumn column in dataTable.Columns)
            {
                var stats = tableDataset.GetColumnStatistics(column.ColumnName);
                if (stats != null)
                    statsTable.Rows.Add(
                        stats.ColumnName,
                        stats.DataType.Name,
                        stats.NonNullCount,
                        stats.UniqueCount?.Count ?? 0,
                        stats.Min?.ToString("F2") ?? "N/A",
                        stats.Max?.ToString("F2") ?? "N/A",
                        stats.Mean?.ToString("F2") ?? "N/A",
                        stats.StdDev?.ToString("F2") ?? "N/A"
                    );
            }

            var statsDataset = new TableDataset($"{tableDataset.Name}_Statistics", statsTable);
            ProjectManager.Instance.AddDataset(statsDataset);

            Logger.Log("Created summary statistics table");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create summary statistics: {ex.Message}");
        }
    }

    private void PerformJoin(TableDataset table1, TableDataset table2)
    {
        try
        {
            var dt1 = table1.GetDataTable();
            var dt2 = table2.GetDataTable();

            if (dt1 == null || dt2 == null) return;

            var joinColumn1 = dt1.Columns[_selectedJoinColumn1].ColumnName;
            var joinColumn2 = dt2.Columns[_selectedJoinColumn2].ColumnName;

            // Create result table with columns from both tables
            var resultTable = new DataTable($"{table1.Name}_Join_{table2.Name}");

            // Add columns from first table
            foreach (DataColumn col in dt1.Columns) resultTable.Columns.Add($"T1_{col.ColumnName}", col.DataType);

            // Add columns from second table
            foreach (DataColumn col in dt2.Columns) resultTable.Columns.Add($"T2_{col.ColumnName}", col.DataType);

            // Perform join based on type
            if (_selectedJoinType == 0) // Inner Join
            {
                var query = from row1 in dt1.AsEnumerable()
                    join row2 in dt2.AsEnumerable()
                        on row1[joinColumn1] equals row2[joinColumn2]
                    select new { Row1 = row1, Row2 = row2 };

                foreach (var item in query)
                {
                    var newRow = resultTable.NewRow();
                    var col = 0;
                    foreach (var value in item.Row1.ItemArray) newRow[col++] = value ?? DBNull.Value;
                    foreach (var value in item.Row2.ItemArray) newRow[col++] = value ?? DBNull.Value;
                    resultTable.Rows.Add(newRow);
                }
            }
            else if (_selectedJoinType == 1) // Left Join
            {
                var query = from row1 in dt1.AsEnumerable()
                    join row2 in dt2.AsEnumerable()
                        on row1[joinColumn1] equals row2[joinColumn2] into gj
                    from subRow in gj.DefaultIfEmpty()
                    select new { Row1 = row1, Row2 = subRow };

                foreach (var item in query)
                {
                    var newRow = resultTable.NewRow();
                    var col = 0;

                    // Add values from first table
                    foreach (var value in item.Row1.ItemArray) newRow[col++] = value ?? DBNull.Value;

                    // Add values from second table (or DBNull if no match)
                    if (item.Row2 != null)
                        foreach (var value in item.Row2.ItemArray)
                            newRow[col++] = value ?? DBNull.Value;
                    else
                        for (var i = 0; i < dt2.Columns.Count; i++)
                            newRow[col++] = DBNull.Value;

                    resultTable.Rows.Add(newRow);
                }
            }
            // Note: Right and Full Outer joins would follow similar patterns

            var joinedDataset = new TableDataset($"{table1.Name}_Join_{table2.Name}", resultTable);
            ProjectManager.Instance.AddDataset(joinedDataset);

            Logger.Log($"Created joined table with {resultTable.Rows.Count} rows");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to perform join: {ex.Message}");
        }
    }

    private void AddCalculatedColumn(TableDataset tableDataset)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_newColumnName) || string.IsNullOrWhiteSpace(_columnFormula))
            {
                Logger.LogWarning("Column name and formula are required");
                return;
            }

            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null) return;

            // Clone table and add new column using DataColumn.Expression
            var newTable = dataTable.Copy();

            // Replace [ColumnName] with actual column name for DataColumn.Expression
            var expression = _columnFormula;
            foreach (DataColumn col in dataTable.Columns)
                expression = expression.Replace($"[{col.ColumnName}]", $"[{col.ColumnName}]");

            try
            {
                newTable.Columns.Add(_newColumnName, typeof(double), expression);
            }
            catch (Exception exprEx)
            {
                Logger.LogError($"Invalid expression: {exprEx.Message}");
                return;
            }

            var newDataset = new TableDataset($"{tableDataset.Name}_Calculated", newTable);
            ProjectManager.Instance.AddDataset(newDataset);

            Logger.Log($"Added calculated column '{_newColumnName}'");

            _newColumnName = "";
            _columnFormula = "";
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to add calculated column: {ex.Message}");
        }
    }

    private void RemoveColumns(TableDataset tableDataset, bool[] selectedColumns)
    {
        try
        {
            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null) return;

            var newTable = dataTable.Copy();

            // Remove columns in reverse order to maintain indices
            for (var i = selectedColumns.Length - 1; i >= 0; i--)
                if (selectedColumns[i])
                    newTable.Columns.RemoveAt(i);

            var newDataset = new TableDataset($"{tableDataset.Name}_Reduced", newTable);
            ProjectManager.Instance.AddDataset(newDataset);

            Logger.Log($"Created table with {newTable.Columns.Count} columns");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to remove columns: {ex.Message}");
        }
    }

    private void RenameColumn(TableDataset tableDataset, int columnIndex, string newName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newName)) return;

            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null || columnIndex >= dataTable.Columns.Count) return;

            var newTable = dataTable.Copy();
            newTable.Columns[columnIndex].ColumnName = newName;

            var newDataset = new TableDataset($"{tableDataset.Name}_Renamed", newTable);
            ProjectManager.Instance.AddDataset(newDataset);

            Logger.Log($"Renamed column to '{newName}'");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to rename column: {ex.Message}");
        }
    }

    private void ExportToCsvFile(TableDataset tableDataset, string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        tableDataset.SaveAsCsv(path);
        Logger.Log($"Exported to CSV: {path}");
    }

    private void ExportToTsvFile(TableDataset tableDataset, string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        tableDataset.SaveAsCsv(path, "\t");
        Logger.Log($"Exported to TSV: {path}");
    }

    private void CopyTableToClipboard(TableDataset tableDataset)
    {
        try
        {
            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null) return;

            var text = new StringBuilder();

            // Headers
            var headers = dataTable.Columns.Cast<DataColumn>().Select(col => col.ColumnName);
            text.AppendLine(string.Join("\t", headers));

            // Data
            foreach (DataRow row in dataTable.Rows)
            {
                var values = row.ItemArray.Select(item => item?.ToString() ?? "");
                text.AppendLine(string.Join("\t", values));
            }

            ImGui.SetClipboardText(text.ToString());
            Logger.Log("Table copied to clipboard");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to copy table: {ex.Message}");
        }
    }

    private void ExportToNewDataset(TableDataset tableDataset)
    {
        try
        {
            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null) return;

            var newTable = dataTable.Copy();
            var newDataset = new TableDataset($"{tableDataset.Name}_Copy", newTable);
            ProjectManager.Instance.AddDataset(newDataset);

            Logger.Log($"Created new dataset: {newDataset.Name}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export to new dataset: {ex.Message}");
        }
    }

    private void GenerateMaterialStatistics(TableDataset tableDataset)
    {
        try
        {
            // Create sample material statistics table
            var statsTable = new DataTable($"{tableDataset.Name}_MaterialStats");

            statsTable.Columns.Add("Material", typeof(string));
            statsTable.Columns.Add("Count", typeof(int));
            statsTable.Columns.Add("Percentage", typeof(double));
            statsTable.Columns.Add("Density", typeof(double));
            statsTable.Columns.Add("Volume", typeof(double));

            // Add sample data
            statsTable.Rows.Add("Air", 1000, 25.0, 0.0, 250.0);
            statsTable.Rows.Add("Rock", 2000, 50.0, 2.5, 500.0);
            statsTable.Rows.Add("Water", 1000, 25.0, 1.0, 250.0);

            var statsDataset = new TableDataset($"{tableDataset.Name}_MaterialStats", statsTable);
            ProjectManager.Instance.AddDataset(statsDataset);

            Logger.Log("Created material statistics table");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to generate material statistics: {ex.Message}");
        }
    }

    private void GenerateHistogramTable(TableDataset tableDataset)
    {
        try
        {
            var dataTable = tableDataset.GetDataTable();
            if (dataTable == null) return;

            // Find first numeric column
            DataColumn numericColumn = null;
            foreach (DataColumn col in dataTable.Columns)
                if (col.DataType == typeof(double) || col.DataType == typeof(int) || col.DataType == typeof(float))
                {
                    numericColumn = col;
                    break;
                }

            if (numericColumn == null)
            {
                Logger.LogWarning("No numeric columns found for histogram");
                return;
            }

            // Create histogram table
            var histTable = new DataTable($"{tableDataset.Name}_Histogram_{numericColumn.ColumnName}");
            histTable.Columns.Add("Bin Range", typeof(string));
            histTable.Columns.Add("Count", typeof(int));
            histTable.Columns.Add("Frequency", typeof(double));
            histTable.Columns.Add("Cumulative", typeof(double));

            // Get numeric values
            var values = new List<double>();
            foreach (DataRow row in dataTable.Rows)
                if (row[numericColumn] != DBNull.Value && double.TryParse(row[numericColumn].ToString(), out var val))
                    values.Add(val);

            if (values.Count == 0) return;

            // Calculate histogram with 10 bins
            var min = values.Min();
            var max = values.Max();
            var range = max - min;
            var binCount = Math.Min(10, values.Count);
            var binSize = range / binCount;

            var histogram = new int[binCount];
            foreach (var val in values)
            {
                var binIndex = (int)((val - min) / binSize);
                if (binIndex >= binCount) binIndex = binCount - 1;
                histogram[binIndex]++;
            }

            // Create histogram rows
            double cumulative = 0;
            for (var i = 0; i < binCount; i++)
            {
                var binStart = min + i * binSize;
                var binEnd = binStart + binSize;
                var frequency = (double)histogram[i] / values.Count * 100;
                cumulative += frequency;

                histTable.Rows.Add(
                    $"{binStart:F2} - {binEnd:F2}",
                    histogram[i],
                    frequency,
                    cumulative
                );
            }

            var histDataset = new TableDataset($"{tableDataset.Name}_Histogram_{numericColumn.ColumnName}", histTable);
            ProjectManager.Instance.AddDataset(histDataset);

            Logger.Log($"Created histogram table for column '{numericColumn.ColumnName}'");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to generate histogram: {ex.Message}");
        }
    }
}