// GeoscientistToolkit/UI/TableViewer.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class TableViewer : IDatasetViewer
    {
        private readonly TableDataset _dataset;
        private DataTable _dataTable;
        private string _searchFilter = "";
        private int _selectedRow = -1;
        private int _selectedColumn = -1;
        private bool _showStatistics = false;
        private Dictionary<int, bool> _sortAscending = new Dictionary<int, bool>();
        private int _currentSortColumn = -1;
        private List<DataRow> _filteredRows;
        private int _currentPage = 0;
        private int _rowsPerPage = 100;
        private readonly int[] _rowsPerPageOptions = { 25, 50, 100, 250, 500, 1000 };
        
        // Export dialog state
        private string _exportPath = "";
        private int _exportFormat = 0; // 0 = CSV, 1 = TSV
        private bool _includeHeaders = true;
        private bool _exportFiltered = false;
        private readonly ImGuiExportFileDialog _csvExportDialog;
        private readonly ImGuiExportFileDialog _tsvExportDialog;
        
        // Column width management
        private Dictionary<int, float> _columnWidths = new Dictionary<int, float>();
        private const float MIN_COLUMN_WIDTH = 50f;
        private const float DEFAULT_COLUMN_WIDTH = 120f;
        
        public TableViewer(TableDataset dataset)
        {
            _dataset = dataset;
            RefreshData();
            _csvExportDialog = new ImGuiExportFileDialog($"{_dataset.Name}_ExportCSV", "Export table to CSV");
            _csvExportDialog.SetExtensions((".csv", "CSV (Comma-separated values)"));

            _tsvExportDialog = new ImGuiExportFileDialog($"{_dataset.Name}_ExportTSV", "Export table to TSV");
            _tsvExportDialog.SetExtensions(
                (".tsv", "TSV (Tab-separated values)"),
                (".txt", "Text file"));
        }
        /// <summary>
        /// Opens native save-file dialogs (CSV / TSV) and calls ExportTable()
        /// when the user confirms.  Follows the same pattern as TableTools.
        /// </summary>
        private void HandleExportDialogs()
        {
            // ── CSV ────────────────────────────────────────────────────────────────────
            if (_csvExportDialog.Submit())
            {
                // Use comma as delimiter
                ExportTable(_csvExportDialog.SelectedPath, ",", _includeHeaders, _exportFiltered);
            }

            // ── TSV ────────────────────────────────────────────────────────────────────
            if (_tsvExportDialog.Submit())
            {
                // Use tab as delimiter
                ExportTable(_tsvExportDialog.SelectedPath, "\t", _includeHeaders, _exportFiltered);
            }
        }
        private void RefreshData()
        {
            _dataTable = _dataset.GetDataTable();
            if (_dataTable != null)
            {
                _filteredRows = _dataTable.Rows.Cast<DataRow>().ToList();
                InitializeColumnWidths();
            }
        }
        
        private void InitializeColumnWidths()
        {
            if (_dataTable == null) return;
            
            _columnWidths.Clear();
            for (int i = 0; i < _dataTable.Columns.Count; i++)
            {
                // Calculate initial width based on column name and sample data
                float nameWidth = ImGui.CalcTextSize(_dataTable.Columns[i].ColumnName).X + 20;
                float dataWidth = DEFAULT_COLUMN_WIDTH;
                
                // Sample first 10 rows to estimate width
                int samplesToCheck = Math.Min(10, _dataTable.Rows.Count);
                for (int row = 0; row < samplesToCheck; row++)
                {
                    string value = _dataTable.Rows[row][i]?.ToString() ?? "";
                    float valueWidth = ImGui.CalcTextSize(value).X + 20;
                    dataWidth = Math.Max(dataWidth, valueWidth);
                }
                
                _columnWidths[i] = Math.Max(nameWidth, Math.Min(dataWidth, 300f));
            }
        }
        
        public void DrawToolbarControls()
        {
            // ───────────────────────── SEARCH ─────────────────────────
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputTextWithHint("##Search", "Search…", ref _searchFilter, 256))
                ApplyFilter();

            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                _searchFilter = "";
                ApplyFilter();
            }

            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();

            // ──────────────────── STATISTICS TOGGLE ───────────────────
            ImGui.Checkbox("Show Statistics", ref _showStatistics);

            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();

            // ──────────────────────── EXPORTS ─────────────────────────
            if (ImGui.Button("Export CSV…"))
                _csvExportDialog.Open($"{_dataset.Name}_export");

            ImGui.SameLine();

            if (ImGui.Button("Export TSV…"))
                _tsvExportDialog.Open($"{_dataset.Name}_export");

            ImGui.SameLine();
            ImGui.Checkbox("Include headers", ref _includeHeaders);
            ImGui.SameLine();
            ImGui.Checkbox("Filtered only", ref _exportFiltered);

            // ───────────────────── PAGINATION ─────────────────────────
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();

            ImGui.Text("Rows per page:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            if (ImGui.BeginCombo("##RowsPerPage", _rowsPerPage.ToString()))
            {
                foreach (int option in _rowsPerPageOptions)
                {
                    if (ImGui.Selectable(option.ToString(), _rowsPerPage == option))
                    {
                        _rowsPerPage = option;
                        _currentPage = 0;
                    }
                }
                ImGui.EndCombo();
            }

            // ──────────────── HANDLE SAVE-FILE DIALOGS ────────────────
            HandleExportDialogs();
        }

        
        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            if (_dataTable == null || _dataTable.Columns.Count == 0)
            {
                ImGui.TextDisabled("No data to display");
                return;
            }
            
            var availableHeight = ImGui.GetContentRegionAvail().Y;
            
            if (_showStatistics)
            {
                DrawStatistics();
                ImGui.Separator();
                availableHeight = ImGui.GetContentRegionAvail().Y;
            }
            
            // Calculate table height
            float paginationHeight = 40f;
            float tableHeight = availableHeight - paginationHeight;
            
            // Draw table - Fixed ImGui API call
            ImGui.BeginChild("TableScrollRegion", new Vector2(0, tableHeight), 
                ImGuiChildFlags.Border);
            
            DrawTable();
            
            ImGui.EndChild();
            
            // Draw pagination
            DrawPagination();
        }
        
        private void DrawTable()
        {
            if (_filteredRows == null || _filteredRows.Count == 0)
            {
                ImGui.TextDisabled("No rows match the filter");
                return;
            }
            
            ImGuiTableFlags tableFlags = ImGuiTableFlags.Borders | 
                                         ImGuiTableFlags.RowBg | 
                                         ImGuiTableFlags.Resizable |
                                         ImGuiTableFlags.Reorderable |
                                         ImGuiTableFlags.Hideable |
                                         ImGuiTableFlags.Sortable |
                                         ImGuiTableFlags.ScrollX |
                                         ImGuiTableFlags.ScrollY |
                                         ImGuiTableFlags.SizingStretchSame;
            
            if (ImGui.BeginTable("DataTable", _dataTable.Columns.Count + 1, tableFlags))
            {
                // Setup columns
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.NoHide, 50f);
                
                for (int i = 0; i < _dataTable.Columns.Count; i++)
                {
                    var column = _dataTable.Columns[i];
                    float width = _columnWidths.ContainsKey(i) ? _columnWidths[i] : DEFAULT_COLUMN_WIDTH;
                    
                    ImGuiTableColumnFlags columnFlags = ImGuiTableColumnFlags.None;
                    if (column.DataType == typeof(double) || column.DataType == typeof(int) || column.DataType == typeof(float))
                    {
                        columnFlags |= ImGuiTableColumnFlags.PreferSortDescending;
                    }
                    
                    ImGui.TableSetupColumn(column.ColumnName, columnFlags, width);
                }
                
                ImGui.TableSetupScrollFreeze(1, 1); // Freeze first column and header row
                ImGui.TableHeadersRow();
                
                // Handle sorting - Fixed the Specs indexing
                unsafe
                {
                    ImGuiTableSortSpecsPtr sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs.NativePtr != null && sortSpecs.SpecsDirty)
                    {
                        // Raw pointer to the first ImGuiTableColumnSortSpecs struct
                        ImGuiTableColumnSortSpecs* specs = sortSpecs.Specs.NativePtr;

                        for (int i = 0; i < sortSpecs.SpecsCount; ++i)
                        {
                            ImGuiTableColumnSortSpecs* spec = &specs[i];

                            if (spec->ColumnIndex > 0)
                            {
                                int  dataColumnIndex = spec->ColumnIndex - 1;
                                bool ascending       = spec->SortDirection == ImGuiSortDirection.Ascending;
                                SortData(dataColumnIndex, ascending);
                            }
                        }

                        sortSpecs.SpecsDirty = false;
                    }
                }


                
                // Draw rows
                int startRow = _currentPage * _rowsPerPage;
                int endRow = Math.Min(startRow + _rowsPerPage, _filteredRows.Count);
                
                for (int row = startRow; row < endRow; row++)
                {
                    var dataRow = _filteredRows[row];
                    
                    ImGui.TableNextRow();
                    
                    // Row number column
                    ImGui.TableNextColumn();
                    ImGui.Text((row + 1).ToString());
                    
                    // Data columns
                    for (int col = 0; col < _dataTable.Columns.Count; col++)
                    {
                        ImGui.TableNextColumn();
                        
                        bool isSelected = _selectedRow == row && _selectedColumn == col;
                        
                        if (isSelected)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
                        }
                        
                        string cellValue = dataRow[col]?.ToString() ?? "";
                        
                        // Make cell selectable
                        ImGui.PushID($"cell_{row}_{col}");
                        if (ImGui.Selectable(cellValue, isSelected, ImGuiSelectableFlags.SpanAllColumns))
                        {
                            _selectedRow = row;
                            _selectedColumn = col;
                        }
                        ImGui.PopID();
                        
                        // Show tooltip for truncated text
                        if (ImGui.IsItemHovered() && ImGui.CalcTextSize(cellValue).X > _columnWidths.GetValueOrDefault(col, DEFAULT_COLUMN_WIDTH))
                        {
                            ImGui.SetTooltip(cellValue);
                        }
                        
                        // Context menu for cell
                        if (ImGui.BeginPopupContextItem($"cell_context_{row}_{col}"))
                        {
                            if (ImGui.MenuItem("Copy Cell"))
                            {
                                ImGui.SetClipboardText(cellValue);
                            }
                            if (ImGui.MenuItem("Copy Row"))
                            {
                                CopyRowToClipboard(dataRow);
                            }
                            if (ImGui.MenuItem("Copy Column"))
                            {
                                CopyColumnToClipboard(col);
                            }
                            ImGui.EndPopup();
                        }
                        
                        if (isSelected)
                        {
                            ImGui.PopStyleColor();
                        }
                    }
                }
                
                ImGui.EndTable();
            }
        }
        
        private void DrawStatistics()
        {
            if (_dataTable == null) return;
            
            // Fixed ImGui API call
            ImGui.BeginChild("Statistics", new Vector2(0, 200), ImGuiChildFlags.Border);
            
            if (ImGui.BeginTable("StatsTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Column");
                ImGui.TableSetupColumn("Type");
                ImGui.TableSetupColumn("Non-Null");
                ImGui.TableSetupColumn("Unique");
                ImGui.TableSetupColumn("Min");
                ImGui.TableSetupColumn("Max");
                ImGui.TableSetupColumn("Mean");
                ImGui.TableSetupColumn("Std Dev");
                ImGui.TableHeadersRow();
                
                foreach (DataColumn column in _dataTable.Columns)
                {
                    var stats = _dataset.GetColumnStatistics(column.ColumnName);
                    if (stats == null) continue;
                    
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(stats.ColumnName);
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(stats.DataType.Name);
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(stats.NonNullCount.ToString());
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(stats.UniqueCount?.Count.ToString() ?? "N/A");
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(stats.Min?.ToString("F2") ?? "N/A");
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(stats.Max?.ToString("F2") ?? "N/A");
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(stats.Mean?.ToString("F2") ?? "N/A");
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(stats.StdDev?.ToString("F2") ?? "N/A");
                }
                
                ImGui.EndTable();
            }
            
            ImGui.EndChild();
        }
        
        private void DrawPagination()
        {
            if (_filteredRows == null) return;
            
            int totalPages = (int)Math.Ceiling(_filteredRows.Count / (double)_rowsPerPage);
            
            ImGui.Separator();
            
            // Previous button
            if (_currentPage <= 0) ImGui.BeginDisabled();
            if (ImGui.Button("Previous"))
            {
                _currentPage--;
            }
            if (_currentPage <= 0) ImGui.EndDisabled();
            
            ImGui.SameLine();
            
            // Page info
            ImGui.Text($"Page {_currentPage + 1} of {Math.Max(1, totalPages)}");
            
            ImGui.SameLine();
            
            // Next button
            if (_currentPage >= totalPages - 1) ImGui.BeginDisabled();
            if (ImGui.Button("Next"))
            {
                _currentPage++;
            }
            if (_currentPage >= totalPages - 1) ImGui.EndDisabled();
            
            ImGui.SameLine();
            ImGui.Text($"| Showing {Math.Min(_filteredRows.Count, (_currentPage * _rowsPerPage) + 1)}-{Math.Min(_filteredRows.Count, (_currentPage + 1) * _rowsPerPage)} of {_filteredRows.Count} rows");
            
            if (_filteredRows.Count != _dataTable.Rows.Count)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), $"(filtered from {_dataTable.Rows.Count} total)");
            }
        }
        
        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(_searchFilter))
            {
                _filteredRows = _dataTable.Rows.Cast<DataRow>().ToList();
            }
            else
            {
                string filter = _searchFilter.ToLower();
                _filteredRows = new List<DataRow>();
                
                foreach (DataRow row in _dataTable.Rows)
                {
                    bool matches = false;
                    foreach (var item in row.ItemArray)
                    {
                        if (item != null && item.ToString().ToLower().Contains(filter))
                        {
                            matches = true;
                            break;
                        }
                    }
                    if (matches)
                    {
                        _filteredRows.Add(row);
                    }
                }
            }
            
            _currentPage = 0;
        }
        
        private void SortData(int columnIndex, bool ascending)
        {
            if (_dataTable == null || columnIndex < 0 || columnIndex >= _dataTable.Columns.Count)
                return;
            
            _currentSortColumn = columnIndex;
            _sortAscending[columnIndex] = ascending;
            
            var column = _dataTable.Columns[columnIndex];
            
            if (column.DataType == typeof(double) || column.DataType == typeof(int) || column.DataType == typeof(float))
            {
                _filteredRows = ascending
                    ? _filteredRows.OrderBy(r => r[columnIndex] == DBNull.Value ? double.MinValue : Convert.ToDouble(r[columnIndex])).ToList()
                    : _filteredRows.OrderByDescending(r => r[columnIndex] == DBNull.Value ? double.MinValue : Convert.ToDouble(r[columnIndex])).ToList();
            }
            else if (column.DataType == typeof(DateTime))
            {
                _filteredRows = ascending
                    ? _filteredRows.OrderBy(r => r[columnIndex] == DBNull.Value ? DateTime.MinValue : (DateTime)r[columnIndex]).ToList()
                    : _filteredRows.OrderByDescending(r => r[columnIndex] == DBNull.Value ? DateTime.MinValue : (DateTime)r[columnIndex]).ToList();
            }
            else
            {
                _filteredRows = ascending
                    ? _filteredRows.OrderBy(r => r[columnIndex]?.ToString() ?? "").ToList()
                    : _filteredRows.OrderByDescending(r => r[columnIndex]?.ToString() ?? "").ToList();
            }
            
            _currentPage = 0;
        }
        
        private void CopyRowToClipboard(DataRow row)
        {
            var values = row.ItemArray.Select(item => item?.ToString() ?? "");
            ImGui.SetClipboardText(string.Join("\t", values));
        }
        
        private void CopyColumnToClipboard(int columnIndex)
        {
            var values = _filteredRows.Select(row => row[columnIndex]?.ToString() ?? "");
            ImGui.SetClipboardText(string.Join("\n", values));
        }
        
        private void DrawExportPopup()
        {
            if (ImGui.BeginPopupModal("ExportTablePopup", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Export Table");
                ImGui.Separator();
                
                ImGui.InputTextWithHint("File Path", "path/to/export.csv", ref _exportPath, 260);
                ImGui.SameLine();
                if (ImGui.Button("Browse..."))
                {
                    // In a real implementation, you'd open a file save dialog here
                    _exportPath = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop),
                        $"{_dataset.Name}_export.csv");
                }
                
                ImGui.Combo("Format", ref _exportFormat, "CSV (Comma-separated)\0TSV (Tab-separated)\0");
                ImGui.Checkbox("Include Headers", ref _includeHeaders);
                ImGui.Checkbox("Export Filtered Rows Only", ref _exportFiltered);
                
                ImGui.Separator();
                
                if (ImGui.Button("Export", new Vector2(120, 0)))
                {
                    if (!string.IsNullOrEmpty(_exportPath))
                    {
                        ExportTable(_exportPath, _exportFormat == 1 ? "\t" : ",", _includeHeaders, _exportFiltered);
                        ImGui.CloseCurrentPopup();
                    }
                }
                
                ImGui.SameLine();
                
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.EndPopup();
            }
        }
        
        private void ExportTable(string path, string delimiter, bool includeHeaders, bool filteredOnly)
        {
            try
            {
                var rowsToExport = filteredOnly ? _filteredRows : _dataTable.Rows.Cast<DataRow>().ToList();
                
                using (var writer = new System.IO.StreamWriter(path, false, System.Text.Encoding.UTF8))
                {
                    // Write headers
                    if (includeHeaders)
                    {
                        var headers = _dataTable.Columns.Cast<DataColumn>()
                            .Select(col => EscapeCsvField(col.ColumnName, delimiter[0]));
                        writer.WriteLine(string.Join(delimiter, headers));
                    }
                    
                    // Write data
                    foreach (var row in rowsToExport)
                    {
                        var values = row.ItemArray.Select(field => EscapeCsvField(field?.ToString() ?? "", delimiter[0]));
                        writer.WriteLine(string.Join(delimiter, values));
                    }
                }
                
                Logger.Log($"Table exported to: {path}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export table: {ex.Message}");
            }
        }
        
        private string EscapeCsvField(string field, char delimiter)
        {
            if (field.Contains(delimiter) || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }
        
        public void Dispose()
        {
            _dataTable?.Dispose();
            _filteredRows?.Clear();
        }
    }
}