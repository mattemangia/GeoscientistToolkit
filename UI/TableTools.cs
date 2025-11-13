// GeoscientistToolkit/UI/TableTools.cs

using System;
using System.Data;
using System.Globalization;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

/// <summary>
///     Categorized tool panel for Table datasets.
///     Uses a compact dropdown + tabs navigation to organize table operations.
/// </summary>
public class TableTools : IDatasetTools
{
    private readonly Dictionary<ToolCategory, string> _categoryDescriptions;
    private readonly Dictionary<ToolCategory, string> _categoryNames;

    // All tools organized by category
    private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;
    private ToolCategory _selectedCategory = ToolCategory.DataTransformation; // Default category
    private int _selectedToolIndex;

    public TableTools()
    {
        // Category metadata
        _categoryNames = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Scripting, "Scripting" },
            { ToolCategory.DataTransformation, "Data Transformation" },
            { ToolCategory.ColumnOperations, "Column Operations" },
            { ToolCategory.RowOperations, "Row Operations" },
            { ToolCategory.Analysis, "Analysis & Aggregation" },
            { ToolCategory.Join, "Join Tables" },
            { ToolCategory.Thermodynamics, "Thermodynamics" },
            { ToolCategory.Export, "Export" }
        };

        _categoryDescriptions = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Scripting, "Automate tasks with the GeoScript Editor" },
            { ToolCategory.DataTransformation, "Filter, sort, sample, and clean data" },
            { ToolCategory.ColumnOperations, "Add, remove, or rename columns" },
            { ToolCategory.RowOperations, "Add or remove rows from the table" },
            { ToolCategory.Analysis, "Aggregate data and generate statistics" },
            { ToolCategory.Join, "Combine this table with another" },
            { ToolCategory.Thermodynamics, "Generate thermodynamic phase diagrams" },
            { ToolCategory.Export, "Save data to files or other formats" }
        };

        // Initialize tools by category
        _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
        {
            {
                ToolCategory.Scripting, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "GeoScript Editor",
                        Description = "Write and execute scripts to process the table.",
                        Tool = new GeoScriptEditorTool()
                    }
                }
            },
            {
                ToolCategory.DataTransformation, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Filter & Sort",
                        Description = "Apply filters and sort the dataset.",
                        Tool = new FilterSortTool()
                    },
                    new()
                    {
                        Name = "Clean & Sample",
                        Description = "Remove duplicates and create data samples.",
                        Tool = new CleanSampleTool()
                    }
                }
            },
            {
                ToolCategory.ColumnOperations, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Manage Columns",
                        Description = "Add, remove, or rename columns.",
                        Tool = new ColumnManagementTool()
                    }
                }
            },
            {
                ToolCategory.RowOperations, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Manage Rows",
                        Description = "Add or remove rows.",
                        Tool = new RowManagementTool()
                    }
                }
            },
            {
                ToolCategory.Analysis, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Aggregation",
                        Description = "Group data and calculate aggregate values.",
                        Tool = new AggregationTool()
                    },
                    new()
                    {
                        Name = "Statistics",
                        Description = "Generate summary, material, and histogram statistics.",
                        Tool = new StatisticsTool()
                    }
                }
            },
            {
                ToolCategory.Join, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Perform Join",
                        Description = "Join this table with another table dataset in the project.",
                        Tool = new JoinTool()
                    }
                }
            },
            {
                ToolCategory.Thermodynamics, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Phase Diagram Generator",
                        Description = "Generate binary, ternary, P-T, and composition diagrams.",
                        Tool = new DiagramGeneratorTool()
                    }
                }
            },
            {
                ToolCategory.Export, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Export Data",
                        Description = "Export to files (CSV, TSV), clipboard, or a new dataset object.",
                        Tool = new ExportTool()
                    }
                }
            }
        };
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not TableDataset tableDataset)
        {
            ImGui.TextDisabled("Table tools are only available for table datasets.");
            return;
        }

        DrawCompactUI(tableDataset);
    }

    private void DrawCompactUI(TableDataset tableDataset)
    {
        // Compact category selector as dropdown
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4));
        ImGui.Text("Category:");
        ImGui.SameLine();

        var currentCategoryName = _categoryNames[_selectedCategory];
        var categoryTools = _toolsByCategory[_selectedCategory];
        var preview = $"{currentCategoryName} ({categoryTools.Count})";

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##CategorySelector", preview))
        {
            foreach (var category in Enum.GetValues<ToolCategory>())
            {
                var tools = _toolsByCategory[category];
                if (tools.Count == 0) continue;

                var isSelected = _selectedCategory == category;
                var label = $"{_categoryNames[category]} ({tools.Count} tools)";

                if (ImGui.Selectable(label, isSelected))
                {
                    _selectedCategory = category;
                    _selectedToolIndex = 0;
                }

                if (ImGui.IsItemHovered()) ImGui.SetTooltip(_categoryDescriptions[category]);
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleVar();

        // Category description
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), _categoryDescriptions[_selectedCategory]);
        ImGui.Separator();
        ImGui.Spacing();

        // Tools in selected category as tabs
        if (categoryTools.Count == 0)
        {
            ImGui.TextDisabled("No tools available in this category.");
        }
        else if (ImGui.BeginTabBar($"Tools_{_selectedCategory}", ImGuiTabBarFlags.None))
        {
            for (var i = 0; i < categoryTools.Count; i++)
            {
                var entry = categoryTools[i];
                if (ImGui.BeginTabItem(entry.Name))
                {
                    _selectedToolIndex = i;

                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), entry.Description);
                    ImGui.Separator();
                    ImGui.Spacing();

                    ImGui.BeginChild($"ToolContent_{entry.Name}", new Vector2(0, 0), ImGuiChildFlags.None,
                        ImGuiWindowFlags.HorizontalScrollbar);
                    {
                        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3));
                        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 5));

                        entry.Tool.Draw(tableDataset);

                        ImGui.PopStyleVar(2);
                    }
                    ImGui.EndChild();

                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
    }

    // Tool categories
    private enum ToolCategory
    {
        Scripting,
        DataTransformation,
        ColumnOperations,
        RowOperations,
        Analysis,
        Join,
        Thermodynamics,
        Export
    }

    // --- NESTED TOOL CLASSES ---

    /// <summary>
    ///     Wrapper for the GeoScript Editor.
    /// </summary>
    private class GeoScriptEditorTool : IDatasetTools
    {
        private readonly GeoScriptEditor _geoScriptEditor = new();

        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset) return;
            _geoScriptEditor.SetAssociatedDataset(tableDataset);
            _geoScriptEditor.Draw();
        }
    }

    /// <summary>
    ///     Handles filtering and sorting operations.
    /// </summary>
    private class FilterSortTool : IDatasetTools
    {
        private string _filterExpression = "";
        private int _selectedSortColumn;
        private bool _sortAscending = true;

        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset) return;

            if (ImGui.CollapsingHeader("Filter Rows", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.InputTextWithHint("##FilterExpression", "e.g., Column1 > 100", ref _filterExpression, 256);
                if (ImGui.Button("Apply Filter")) ApplyFilter(tableDataset);
                ImGui.SameLine();
                if (ImGui.Button("Clear Filter"))
                {
                    _filterExpression = "";
                    tableDataset.Load(); // Reload original data
                }
            }

            if (ImGui.CollapsingHeader("Sort Data", ImGuiTreeNodeFlags.DefaultOpen))
            {
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
                else
                {
                    ImGui.TextDisabled("No columns available.");
                }
            }
        }

        private void ApplyFilter(TableDataset tableDataset)
        {
            try
            {
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
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
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
                if (columnIndex >= dataTable.Columns.Count) return;

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
    }

    /// <summary>
    ///     Handles duplicate removal and data sampling.
    /// </summary>
    private class CleanSampleTool : IDatasetTools
    {
        private bool _randomSample = true;
        private int _sampleSize = 100;

        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset) return;

            if (ImGui.CollapsingHeader("Remove Duplicates", ImGuiTreeNodeFlags.DefaultOpen))
                if (ImGui.Button("Remove Duplicate Rows"))
                    RemoveDuplicates(tableDataset);

            if (ImGui.CollapsingHeader("Sampling", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.SetNextItemWidth(100);
                ImGui.InputInt("Sample Size", ref _sampleSize, 10, 100);
                _sampleSize = Math.Max(1, Math.Min(_sampleSize, tableDataset.RowCount));
                ImGui.SameLine();
                ImGui.Checkbox("Random", ref _randomSample);

                if (ImGui.Button("Create Sample")) CreateSample(tableDataset, _sampleSize, _randomSample);
            }
        }

        private void RemoveDuplicates(TableDataset tableDataset)
        {
            try
            {
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
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
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
                var newTable = dataTable.Clone();
                if (random)
                {
                    var rng = new Random();
                    var indices = Enumerable.Range(0, dataTable.Rows.Count).OrderBy(x => rng.Next()).Take(sampleSize)
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
    }
    
    /// <summary>
    ///     Handles adding, removing, and renaming columns.
    /// </summary>
    private class ColumnManagementTool : IDatasetTools
    {
        private string _columnFormula = "";
        private string _newColumnName = "";
        private string _newEmptyColumnName = "NewColumn";
        private int _newEmptyColumnTypeIndex = 0;
        private readonly string[] _columnTypes = { "Text (string)", "Numeric (double)", "Boolean", "DateTime" };
        private string _newRenameColumnName = "";
        private int _renameColumnIndex;
        private bool[] _selectedColumns;

        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset) return;

            if (ImGui.CollapsingHeader("Add Empty Column", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.InputTextWithHint("##NewEmptyColumnName", "Column Name", ref _newEmptyColumnName, 64);
                ImGui.Combo("Data Type", ref _newEmptyColumnTypeIndex, _columnTypes, _columnTypes.Length);
                if (ImGui.Button("Add Empty Column")) AddEmptyColumn(tableDataset);
            }

            if (ImGui.CollapsingHeader("Add Calculated Column", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.InputTextWithHint("Column Name", "NewColumn", ref _newColumnName, 64);
                ImGui.InputTextWithHint("Formula", "e.g., [Column1] + [Column2]", ref _columnFormula, 256);
                if (ImGui.Button("Add Calculated Column")) AddCalculatedColumn(tableDataset);
            }
            
            if (ImGui.CollapsingHeader("Remove Columns", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var columnNames = tableDataset.ColumnNames.ToArray();
                if (_selectedColumns == null || _selectedColumns.Length != columnNames.Length)
                    _selectedColumns = new bool[columnNames.Length];
                for (var i = 0; i < columnNames.Length; i++) ImGui.Checkbox(columnNames[i], ref _selectedColumns[i]);

                if (ImGui.Button("Remove Selected Columns"))
                {
                    RemoveColumns(tableDataset, _selectedColumns);
                    _selectedColumns = null; // Reset selection
                }
            }

            if (ImGui.CollapsingHeader("Rename Column", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var columnNames = tableDataset.ColumnNames.ToArray();
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
                else
                {
                    ImGui.TextDisabled("No columns available.");
                }
            }
        }

        private void AddEmptyColumn(TableDataset tableDataset)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_newEmptyColumnName))
                {
                    Logger.LogWarning("Column name is required.");
                    return;
                }

                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
                
                if (dataTable.Columns.Contains(_newEmptyColumnName))
                {
                    Logger.LogWarning($"A column named '{_newEmptyColumnName}' already exists.");
                    return;
                }
                
                Type colType = _newEmptyColumnTypeIndex switch
                {
                    1 => typeof(double),
                    2 => typeof(bool),
                    3 => typeof(DateTime),
                    _ => typeof(string)
                };
                
                dataTable.Columns.Add(_newEmptyColumnName, colType);
                
                tableDataset.UpdateDataTable(dataTable);
                Logger.Log($"Added new empty column '{_newEmptyColumnName}'");
                _newEmptyColumnName = "NewColumn";
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to add empty column: {ex.Message}");
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

                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");

                try
                {
                    dataTable.Columns.Add(_newColumnName, typeof(double), _columnFormula);
                }
                catch (Exception exprEx)
                {
                    Logger.LogError($"Invalid expression: {exprEx.Message}");
                    return;
                }
                
                tableDataset.UpdateDataTable(dataTable);
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
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
                
                int removedCount = 0;
                for (var i = selectedColumns.Length - 1; i >= 0; i--)
                {
                    if (selectedColumns[i])
                    {
                        dataTable.Columns.RemoveAt(i);
                        removedCount++;
                    }
                }
                
                if(removedCount > 0)
                {
                    tableDataset.UpdateDataTable(dataTable);
                    Logger.Log($"Removed {removedCount} columns");
                }
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
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
                if (columnIndex >= dataTable.Columns.Count) return;

                var oldName = dataTable.Columns[columnIndex].ColumnName;
                dataTable.Columns[columnIndex].ColumnName = newName;
                
                tableDataset.UpdateDataTable(dataTable);
                Logger.Log($"Renamed column '{oldName}' to '{newName}'");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to rename column: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    ///     Handles adding and removing rows.
    /// </summary>
    private class RowManagementTool : IDatasetTools
    {
        private int _rowsToAdd = 1;
        private int _rowsToRemove = 1;

        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset) return;
            
            if (ImGui.CollapsingHeader("Add Empty Rows", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.SetNextItemWidth(100);
                ImGui.InputInt("Rows to Add", ref _rowsToAdd);
                _rowsToAdd = Math.Max(1, _rowsToAdd);
                ImGui.SameLine();
                if (ImGui.Button("Add Rows")) AddEmptyRows(tableDataset);
            }
            
            if (ImGui.CollapsingHeader("Remove Rows", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.SetNextItemWidth(100);
                ImGui.InputInt("Rows to Remove", ref _rowsToRemove);
                _rowsToRemove = Math.Max(1, Math.Min(_rowsToRemove, tableDataset.RowCount));
                ImGui.Text("Removes rows from the end of the table.");
                if (ImGui.Button("Remove Rows")) RemoveRows(tableDataset);
                ImGui.SameLine();
                if (ImGui.Button("Clear All Rows")) ClearAllRows(tableDataset);
            }
        }

        private void AddEmptyRows(TableDataset tableDataset)
        {
            try
            {
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
                
                for(int i = 0; i < _rowsToAdd; i++)
                {
                    dataTable.Rows.Add();
                }

                tableDataset.UpdateDataTable(dataTable);
                Logger.Log($"Added {_rowsToAdd} empty rows");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to add empty rows: {ex.Message}");
            }
        }
        
        private void RemoveRows(TableDataset tableDataset)
        {
            try
            {
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");

                int count = dataTable.Rows.Count;
                for(int i = 0; i < _rowsToRemove; i++)
                {
                    if (count - 1 - i < 0) break;
                    dataTable.Rows[count - 1 - i].Delete();
                }
                dataTable.AcceptChanges();

                tableDataset.UpdateDataTable(dataTable);
                Logger.Log($"Removed {_rowsToRemove} rows from the end");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to remove rows: {ex.Message}");
            }
        }

        private void ClearAllRows(TableDataset tableDataset)
        {
            try
            {
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
                
                dataTable.Clear();
                
                tableDataset.UpdateDataTable(dataTable);
                Logger.Log($"Cleared all rows from the table");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to clear all rows: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     Handles data aggregation (group by).
    /// </summary>
    private class AggregationTool : IDatasetTools
    {
        private readonly string[] _aggregateFunctions = { "Count", "Sum", "Average", "Min", "Max", "StdDev" };
        private int _selectedAggregateColumn;
        private int _selectedAggregateFunction;
        private int _selectedGroupByColumn;

        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset) return;
            var columnNames = tableDataset.ColumnNames.ToArray();
            if (columnNames.Length == 0)
            {
                ImGui.TextDisabled("No columns available for aggregation.");
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
        }

        private void CreateAggregatedTable(TableDataset tableDataset)
        {
            try
            {
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
                var groupColumn = dataTable.Columns[_selectedGroupByColumn].ColumnName;
                var valueColumn = dataTable.Columns[_selectedAggregateColumn].ColumnName;
                var groups = dataTable.AsEnumerable().GroupBy(row => row[groupColumn]?.ToString() ?? "NULL");

                var newTable = new DataTable($"{tableDataset.Name}_Aggregated");
                newTable.Columns.Add(groupColumn, typeof(string));
                newTable.Columns.Add($"{_aggregateFunctions[_selectedAggregateFunction]}_{valueColumn}",
                    typeof(double));
                newTable.Columns.Add("Count", typeof(int));

                foreach (var group in groups)
                {
                    double result = 0;
                    var values = group
                        .Select(row => double.TryParse(row[valueColumn]?.ToString(), out var val) ? val : 0).ToList();

                    switch (_selectedAggregateFunction)
                    {
                        case 0: result = values.Count; break; // Count
                        case 1: result = values.Sum(); break; // Sum
                        case 2: result = values.Count > 0 ? values.Average() : 0; break; // Average
                        case 3: result = values.Count > 0 ? values.Min() : 0; break; // Min
                        case 4: result = values.Count > 0 ? values.Max() : 0; break; // Max
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

                var aggregatedDataset = new TableDataset(newTable.TableName, newTable);
                ProjectManager.Instance.AddDataset(aggregatedDataset);
                Logger.Log($"Created aggregated table with {newTable.Rows.Count} groups");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to create aggregated table: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     Handles generation of various statistics tables.
    /// </summary>
    private class StatisticsTool : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset) return;

            if (ImGui.Button("Create Summary Statistics")) CreateSummaryStatistics(tableDataset);
            ImGui.Separator();
            if (ImGui.Button("Generate Material Statistics")) GenerateMaterialStatistics(tableDataset);
            if (ImGui.Button("Generate Histogram Table")) GenerateHistogramTable(tableDataset);
        }

        private void CreateSummaryStatistics(TableDataset tableDataset)
        {
            try
            {
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
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
                            stats.ColumnName, stats.DataType.Name, stats.NonNullCount, stats.UniqueCount?.Count ?? 0,
                            stats.Min?.ToString("F2") ?? "N/A", stats.Max?.ToString("F2") ?? "N/A",
                            stats.Mean?.ToString("F2") ?? "N/A", stats.StdDev?.ToString("F2") ?? "N/A"
                        );
                }

                var statsDataset = new TableDataset(statsTable.TableName, statsTable);
                ProjectManager.Instance.AddDataset(statsDataset);
                Logger.Log("Created summary statistics table");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to create summary statistics: {ex.Message}");
            }
        }

        private void GenerateMaterialStatistics(TableDataset tableDataset)
        {
            try
            {
                var statsTable = new DataTable($"{tableDataset.Name}_MaterialStats");
                statsTable.Columns.Add("Material", typeof(string));
                statsTable.Columns.Add("Count", typeof(int));
                statsTable.Columns.Add("Percentage", typeof(double));
                statsTable.Columns.Add("Density", typeof(double));
                statsTable.Columns.Add("Volume", typeof(double));
                statsTable.Rows.Add("Air", 1000, 25.0, 0.0, 250.0);
                statsTable.Rows.Add("Rock", 2000, 50.0, 2.5, 500.0);
                statsTable.Rows.Add("Water", 1000, 25.0, 1.0, 250.0);
                var statsDataset = new TableDataset(statsTable.TableName, statsTable);
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
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
                var numericColumn = dataTable.Columns.Cast<DataColumn>()
                    .FirstOrDefault(c =>
                        c.DataType == typeof(double) || c.DataType == typeof(int) || c.DataType == typeof(float));
                if (numericColumn == null)
                {
                    Logger.LogWarning("No numeric columns found for histogram");
                    return;
                }

                var histTable = new DataTable($"{tableDataset.Name}_Histogram_{numericColumn.ColumnName}");
                histTable.Columns.Add("Bin Range", typeof(string));
                histTable.Columns.Add("Count", typeof(int));
                histTable.Columns.Add("Frequency", typeof(double));
                histTable.Columns.Add("Cumulative", typeof(double));

                var values = new List<double>();
                foreach (DataRow row in dataTable.Rows)
                    if (row[numericColumn] != DBNull.Value &&
                        double.TryParse(row[numericColumn].ToString(), out var val))
                        values.Add(val);
                if (values.Count == 0) return;

                var min = values.Min();
                var max = values.Max();
                var binCount = Math.Min(10, values.Count);
                var binSize = (max - min) / binCount;
                var histogram = new int[binCount];
                foreach (var val in values)
                {
                    var binIndex = binSize > 0 ? (int)((val - min) / binSize) : 0;
                    if (binIndex >= binCount) binIndex = binCount - 1;
                    histogram[binIndex]++;
                }

                double cumulative = 0;
                for (var i = 0; i < binCount; i++)
                {
                    var binStart = min + i * binSize;
                    var binEnd = binStart + binSize;
                    var frequency = (double)histogram[i] / values.Count * 100;
                    cumulative += frequency;
                    histTable.Rows.Add($"{binStart:F2} - {binEnd:F2}", histogram[i], frequency, cumulative);
                }

                var histDataset = new TableDataset(histTable.TableName, histTable);
                ProjectManager.Instance.AddDataset(histDataset);
                Logger.Log($"Created histogram table for column '{numericColumn.ColumnName}'");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to generate histogram: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     Handles joining with other tables.
    /// </summary>
    private class JoinTool : IDatasetTools
    {
        private readonly string[] _joinTypes = { "Inner Join", "Left Join" }; // Right/Full Outer omitted for brevity
        private int _selectedJoinColumn1;
        private int _selectedJoinColumn2;
        private int _selectedJoinDataset;
        private int _selectedJoinType;

        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset) return;

            var otherTables = ProjectManager.Instance.LoadedDatasets.OfType<TableDataset>()
                .Where(t => t != tableDataset).ToList();
            if (otherTables.Count == 0)
            {
                ImGui.TextDisabled("No other table datasets available for joining.");
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

        private void PerformJoin(TableDataset table1, TableDataset table2)
        {
            try
            {
                var dt1 = table1.GetDataTable() ??
                          throw new InvalidOperationException("Source DataTable 1 not available.");
                var dt2 = table2.GetDataTable() ??
                          throw new InvalidOperationException("Source DataTable 2 not available.");

                var joinColumn1 = dt1.Columns[_selectedJoinColumn1].ColumnName;
                var joinColumn2 = dt2.Columns[_selectedJoinColumn2].ColumnName;
                var resultTable = new DataTable($"{table1.Name}_Join_{table2.Name}");
                foreach (DataColumn col in dt1.Columns) resultTable.Columns.Add($"T1_{col.ColumnName}", col.DataType);
                foreach (DataColumn col in dt2.Columns) resultTable.Columns.Add($"T2_{col.ColumnName}", col.DataType);

                if (_selectedJoinType == 0) // Inner Join
                {
                    var query = from row1 in dt1.AsEnumerable()
                        join row2 in dt2.AsEnumerable() on row1[joinColumn1] equals row2[joinColumn2]
                        select new { Row1 = row1, Row2 = row2 };
                    foreach (var item in query)
                        resultTable.Rows.Add(item.Row1.ItemArray.Concat(item.Row2.ItemArray).ToArray());
                }
                else if (_selectedJoinType == 1) // Left Join
                {
                    var query = from row1 in dt1.AsEnumerable()
                        join row2 in dt2.AsEnumerable() on row1[joinColumn1] equals row2[joinColumn2] into gj
                        from subRow in gj.DefaultIfEmpty()
                        select new { Row1 = row1, Row2 = subRow };
                    foreach (var item in query)
                    {
                        var values = item.Row1.ItemArray;
                        values = values.Concat((IEnumerable<object>)item.Row2?.ItemArray ??
                                               Enumerable.Repeat(DBNull.Value, dt2.Columns.Count)).ToArray();
                        resultTable.Rows.Add(values);
                    }
                }

                var joinedDataset = new TableDataset(resultTable.TableName, resultTable);
                ProjectManager.Instance.AddDataset(joinedDataset);
                Logger.Log($"Created joined table with {resultTable.Rows.Count} rows");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to perform join: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     UI and logic for generating thermodynamic phase diagrams.
    /// </summary>
    private class DiagramGeneratorTool : IDatasetTools
    {
        private readonly string[] _diagramTypes = { "Binary", "Ternary", "P-T", "Energy", "Composition" };

        // UI state variables
        private string _comp1 = "H₂O", _comp2 = "NaCl", _comp3 = "CaCl₂";
        private int _comp1Index, _comp2Index, _comp3Index;

        private int _gridPoints = 25;
        private float _minP = 1.0f, _maxP = 1000.0f;
        private float _minT = 273.15f, _maxT = 473.15f;
        private float _pressure = 1.0f;
        private string _ptComposition = "'H₂O'=55.5, 'CO₂'=1.0";
        private int _selectedDiagramType;

        private float _temperature = 298.15f;

        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset) return;

            ImGui.SetNextItemWidth(200);
            ImGui.Combo("Diagram Type", ref _selectedDiagramType, _diagramTypes, _diagramTypes.Length);
            ImGui.Separator();

            // Render UI based on selected diagram type
            switch (_diagramTypes[_selectedDiagramType])
            {
                case "Binary": DrawBinaryOptions(); break;
                case "Ternary": DrawTernaryOptions(); break;
                case "P-T": DrawPTOptions(); break;
                case "Energy": DrawEnergyOptions(); break;
                case "Composition": DrawCompositionOptions(tableDataset); break;
            }

            ImGui.Separator();
            if (ImGui.Button("Generate Diagram")) GenerateDiagram(tableDataset);
        }

        private void DrawBinaryOptions()
        {
            ImGui.InputText("Component 1", ref _comp1, 64);
            ImGui.InputText("Component 2", ref _comp2, 64);
            ImGui.DragFloat("Temperature (K)", ref _temperature, 1, 273, 1000, "%.2f K");
            ImGui.DragFloat("Pressure (bar)", ref _pressure, 0.1f, 0, 2000, "%.2f bar");
            ImGui.SliderInt("Grid Points", ref _gridPoints, 10, 100);
        }

        private void DrawTernaryOptions()
        {
            ImGui.InputText("Component 1", ref _comp1, 64);
            ImGui.InputText("Component 2", ref _comp2, 64);
            ImGui.InputText("Component 3", ref _comp3, 64);
            ImGui.DragFloat("Temperature (K)", ref _temperature, 1, 273, 1000, "%.2f K");
            ImGui.DragFloat("Pressure (bar)", ref _pressure, 0.1f, 0, 2000, "%.2f bar");
            ImGui.SliderInt("Grid Resolution", ref _gridPoints, 10, 50);
        }

        private void DrawPTOptions()
        {
            ImGui.InputText("Composition (moles)", ref _ptComposition, 256);
            ImGui.DragFloatRange2("Temperature Range (K)", ref _minT, ref _maxT, 5, 273, 2000, "Min: %.2f K",
                "Max: %.2f K");
            ImGui.DragFloatRange2("Pressure Range (bar)", ref _minP, ref _maxP, 10, 1, 10000, "Min: %.2f bar",
                "Max: %.2f bar");
            ImGui.SliderInt("Grid Points", ref _gridPoints, 10, 50);
        }

        private void DrawEnergyOptions()
        {
            ImGui.InputText("Component 1", ref _comp1, 64);
            ImGui.InputText("Component 2", ref _comp2, 64);
            ImGui.DragFloat("Temperature (K)", ref _temperature, 1, 273, 1000, "%.2f K");
            ImGui.DragFloat("Pressure (bar)", ref _pressure, 0.1f, 0, 2000, "%.2f bar");
            ImGui.SliderInt("Points", ref _gridPoints, 20, 200);
        }

        private void DrawCompositionOptions(TableDataset tableDataset)
        {
            var columnNames = tableDataset.ColumnNames.ToArray();
            if (columnNames.Length < 3)
            {
                ImGui.TextDisabled("This diagram requires at least 3 numeric columns.");
                return;
            }

            ImGui.Combo("Component 1 (Apex)", ref _comp1Index, columnNames, columnNames.Length);
            ImGui.Combo("Component 2 (Left)", ref _comp2Index, columnNames, columnNames.Length);
            ImGui.Combo("Component 3 (Right)", ref _comp3Index, columnNames, columnNames.Length);
        }

        private void GenerateDiagram(TableDataset tableDataset)
        {
            try
            {
                var generator = new PhaseDiagramGenerator();
                DataTable resultTable = null;
                var diagramName = "Diagram";

                switch (_diagramTypes[_selectedDiagramType])
                {
                    case "Binary":
                        var binaryData =
                            generator.GenerateBinaryDiagram(_comp1, _comp2, _temperature, _pressure, _gridPoints);
                        resultTable = generator.ExportBinaryDiagramToTable(binaryData);
                        diagramName = $"Binary_{_comp1}-{_comp2}";
                        break;
                    case "Ternary":
                        var ternaryData = generator.GenerateTernaryDiagram(_comp1, _comp2, _comp3, _temperature,
                            _pressure, _gridPoints);
                        resultTable = new DataTable("TernaryDiagramData");
                        resultTable.Columns.Add("PlotX", typeof(double));
                        resultTable.Columns.Add("PlotY", typeof(double));
                        resultTable.Columns.Add("Phases", typeof(string));
                        foreach (var p in ternaryData.Points)
                        {
                            var plotX = p.X_Component2 + 0.5 * p.X_Component3;
                            var plotY = Math.Sqrt(3) / 2.0 * p.X_Component3;
                            resultTable.Rows.Add(plotX, plotY, string.Join(",", p.PhasesPresent));
                        }

                        diagramName = $"Ternary_{_comp1}-{_comp2}-{_comp3}";
                        break;
                    case "P-T":
                        var composition = ParseComposition(_ptComposition);
                        var ptData = generator.GeneratePTDiagram(composition, _minT, _maxT, _minP, _maxP, _gridPoints);
                        resultTable = new DataTable("PTDiagramData");
                        resultTable.Columns.Add("Temperature_K", typeof(double));
                        resultTable.Columns.Add("Pressure_bar", typeof(double));
                        resultTable.Columns.Add("DominantPhase", typeof(string));
                        foreach (var p in ptData.Points)
                            resultTable.Rows.Add(p.Temperature_K, p.Pressure_bar, p.DominantPhase);
                        diagramName = "PT_Diagram";
                        break;
                    case "Energy":
                        var energyData =
                            generator.GenerateEnergyDiagram(_comp1, _comp2, _temperature, _pressure, _gridPoints);
                        resultTable = new DataTable("EnergyDiagramData");
                        resultTable.Columns.Add("X_" + _comp1, typeof(double));
                        resultTable.Columns.Add("GibbsEnergy", typeof(double));
                        foreach (var p in energyData.Points) resultTable.Rows.Add(p.X_Component1, p.GibbsEnergy);
                        diagramName = $"Energy_{_comp1}-{_comp2}";
                        break;
                    case "Composition":
                        var sourceTable = tableDataset.GetDataTable();
                        var col1 = sourceTable.Columns[_comp1Index].ColumnName;
                        var col2 = sourceTable.Columns[_comp2Index].ColumnName;
                        var col3 = sourceTable.Columns[_comp3Index].ColumnName;
                        resultTable = new DataTable("CompositionPlotData");
                        resultTable.Columns.Add("PlotX", typeof(double));
                        resultTable.Columns.Add("PlotY", typeof(double));
                        foreach (DataColumn col in sourceTable.Columns)
                            resultTable.Columns.Add(col.ColumnName, col.DataType);

                        foreach (DataRow row in sourceTable.Rows)
                        {
                            var v1 = Convert.ToDouble(row[col1]);
                            var v2 = Convert.ToDouble(row[col2]);
                            var v3 = Convert.ToDouble(row[col3]);
                            var total = v1 + v2 + v3;
                            if (total == 0) continue;

                            var x1_norm = v1 / total;
                            var x2_norm = v2 / total;
                            var x3_norm = v3 / total;

                            var plotX = x2_norm + 0.5 * x3_norm;
                            var plotY = Math.Sqrt(3) / 2.0 * x3_norm;

                            var newRow = resultTable.NewRow();
                            newRow["PlotX"] = plotX;
                            newRow["PlotY"] = plotY;
                            foreach (DataColumn col in sourceTable.Columns) newRow[col.ColumnName] = row[col];
                            resultTable.Rows.Add(newRow);
                        }

                        diagramName = $"Composition_{col1}-{col2}-{col3}";
                        break;
                }

                if (resultTable != null)
                {
                    var newDataset = new TableDataset(diagramName, resultTable);
                    ProjectManager.Instance.AddDataset(newDataset);
                    Logger.Log($"Created diagram data table: {newDataset.Name}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to generate diagram: {ex.Message}");
            }
        }

        private Dictionary<string, double> ParseComposition(string compStr)
        {
            var composition = new Dictionary<string, double>();
            var parts = compStr.Split(',');
            foreach (var part in parts)
            {
                var pair = part.Split('=');
                if (pair.Length == 2)
                {
                    var name = pair[0].Trim().Trim('\'');
                    var moles = double.Parse(pair[1].Trim(), CultureInfo.InvariantCulture);
                    composition[name] = moles;
                }
            }

            return composition;
        }
    }

    /// <summary>
    ///     Handles exporting data to various formats.
    /// </summary>
    private class ExportTool : IDatasetTools
    {
        private readonly ImGuiExportFileDialog _csvExportDialog;
        private readonly ImGuiExportFileDialog _tsvExportDialog;

        public ExportTool()
        {
            _csvExportDialog = new ImGuiExportFileDialog("ExportCSVDialog", "Export to CSV");
            _csvExportDialog.SetExtensions((".csv", "CSV (Comma-separated values)"));
            _tsvExportDialog = new ImGuiExportFileDialog("ExportTSVDialog", "Export to TSV");
            _tsvExportDialog.SetExtensions((".tsv", "TSV (Tab-separated values)"), (".txt", "Text file"));
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset) return;

            // Handle dialog submissions
            if (_csvExportDialog.Submit()) ExportToCsvFile(tableDataset, _csvExportDialog.SelectedPath);
            if (_tsvExportDialog.Submit()) ExportToTsvFile(tableDataset, _tsvExportDialog.SelectedPath);

            if (ImGui.Button("Export to CSV...")) _csvExportDialog.Open($"{tableDataset.Name}_export");
            if (ImGui.Button("Export to Tab-Separated...")) _tsvExportDialog.Open($"{tableDataset.Name}_export");
            ImGui.Separator();
            if (ImGui.Button("Copy All to Clipboard")) CopyTableToClipboard(tableDataset);
            if (ImGui.Button("Export to New Table Dataset")) ExportToNewDataset(tableDataset);
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
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
                var text = new StringBuilder();
                var headers = dataTable.Columns.Cast<DataColumn>().Select(col => col.ColumnName);
                text.AppendLine(string.Join("\t", headers));
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
                var dataTable = tableDataset.GetDataTable() ??
                                throw new InvalidOperationException("DataTable not available.");
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
    }

    // --- HELPER CLASSES ---

    /// <summary>
    ///     Defines a tool entry for the UI.
    /// </summary>
    private class ToolEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public IDatasetTools Tool { get; set; }
    }
}