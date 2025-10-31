// GeoscientistToolkit/UI/TableViewer.cs

using System;
using System.Data;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public class TableViewer : IDatasetViewer
{
    private const float MIN_COLUMN_WIDTH = 50f;
    private const float DEFAULT_COLUMN_WIDTH = 120f;

    // Column width management
    private readonly Dictionary<int, float> _columnWidths = new();
    private readonly ImGuiExportFileDialog _csvExportDialog;
    private readonly TableDataset _dataset;
    private readonly int[] _rowsPerPageOptions = { 25, 50, 100, 250, 500, 1000 };
    private readonly Dictionary<int, bool> _sortAscending = new();
    private readonly ImGuiExportFileDialog _tsvExportDialog;
    private int _currentPage;
    private int _currentSortColumn = -1;
    private DataTable _dataTable;
    private bool _exportFiltered;
    private int _exportFormat; // 0 = CSV, 1 = TSV

    // Cell editing state
    private (int Row, int Col) _editingCell = (-1, -1);
    private string _editBuffer = "";
    private bool _startEditing;


    // Export dialog state
    private string _exportPath = "";
    private List<DataRow> _filteredRows;
    private bool _includeHeaders = true;
    private int _rowsPerPage = 100;
    private string _searchFilter = "";
    private int _selectedColumn = -1;
    private int _selectedRow = -1;
    private bool _showStatistics;

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
            foreach (var option in _rowsPerPageOptions)
                if (ImGui.Selectable(option.ToString(), _rowsPerPage == option))
                {
                    _rowsPerPage = option;
                    _currentPage = 0;
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
        var paginationHeight = 40f;
        var tableHeight = availableHeight - paginationHeight;

        // Draw table - Fixed ImGui API call
        ImGui.BeginChild("TableScrollRegion", new Vector2(0, tableHeight),
            ImGuiChildFlags.Border);

        DrawTable();

        ImGui.EndChild();

        // Draw pagination
        DrawPagination();
    }

    public void Dispose()
    {
        _dataTable?.Dispose();
        _filteredRows?.Clear();
    }

    /// <summary>
    ///     Checks for and handles submissions from the CSV and TSV file dialogs.
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
        for (var i = 0; i < _dataTable.Columns.Count; i++)
        {
            // Calculate initial width based on column name and sample data
            var nameWidth = ImGui.CalcTextSize(_dataTable.Columns[i].ColumnName).X + 20;
            var dataWidth = DEFAULT_COLUMN_WIDTH;

            // Sample first 10 rows to estimate width
            var samplesToCheck = Math.Min(10, _dataTable.Rows.Count);
            for (var row = 0; row < samplesToCheck; row++)
            {
                var value = _dataTable.Rows[row][i]?.ToString() ?? "";
                var valueWidth = ImGui.CalcTextSize(value).X + 20;
                dataWidth = Math.Max(dataWidth, valueWidth);
            }

            _columnWidths[i] = Math.Max(nameWidth, Math.Min(dataWidth, 300f));
        }
    }

    private void StopEditing(bool commitChange)
    {
        if (commitChange)
        {
            // Find the original row index in the unfiltered _dataTable
            var dataRow = _filteredRows[_editingCell.Row];
            var originalRowIndex = _dataTable.Rows.IndexOf(dataRow);

            if (originalRowIndex != -1)
                _dataset.UpdateCellValue(originalRowIndex, _editingCell.Col, _editBuffer);
        }
        _editingCell = (-1, -1);
    }

    private void DrawTable()
    {
        if (_filteredRows == null || _filteredRows.Count == 0)
        {
            ImGui.TextDisabled("No rows match the filter");
            return;
        }

        var tableFlags = ImGuiTableFlags.Borders |
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
            ImGui.TableSetupColumn("#",
                ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.NoHide, 50f);

            for (var i = 0; i < _dataTable.Columns.Count; i++)
            {
                var column = _dataTable.Columns[i];
                var width = _columnWidths.ContainsKey(i) ? _columnWidths[i] : DEFAULT_COLUMN_WIDTH;

                var columnFlags = ImGuiTableColumnFlags.None;
                if (column.DataType == typeof(double) || column.DataType == typeof(int) ||
                    column.DataType == typeof(float)) columnFlags |= ImGuiTableColumnFlags.PreferSortDescending;

                ImGui.TableSetupColumn(column.ColumnName, columnFlags, width);
            }

            ImGui.TableSetupScrollFreeze(1, 1); // Freeze first column and header row
            ImGui.TableHeadersRow();

            // Handle sorting - Fixed the Specs indexing
            unsafe
            {
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.NativePtr != null && sortSpecs.SpecsDirty)
                {
                    // Raw pointer to the first ImGuiTableColumnSortSpecs struct
                    var specs = sortSpecs.Specs.NativePtr;

                    for (var i = 0; i < sortSpecs.SpecsCount; ++i)
                    {
                        var spec = &specs[i];

                        if (spec->ColumnIndex > 0)
                        {
                            var dataColumnIndex = spec->ColumnIndex - 1;
                            var ascending = spec->SortDirection == ImGuiSortDirection.Ascending;
                            SortData(dataColumnIndex, ascending);
                        }
                    }

                    sortSpecs.SpecsDirty = false;
                }
            }


            // Draw rows
            var startRow = _currentPage * _rowsPerPage;
            var endRow = Math.Min(startRow + _rowsPerPage, _filteredRows.Count);

            for (var row = startRow; row < endRow; row++)
            {
                var dataRow = _filteredRows[row];

                ImGui.TableNextRow();

                // Row number column
                ImGui.TableNextColumn();
                ImGui.Text((row + 1).ToString());

                // Data columns
                for (var col = 0; col < _dataTable.Columns.Count; col++)
                {
                    ImGui.TableNextColumn();
                    ImGui.PushID($"cell_{row}_{col}");

                    bool isEditingThisCell = _editingCell.Row == row && _editingCell.Col == col;

                    if (isEditingThisCell)
                    {
                        // This block executes when the cell is in edit mode.
                        if (_startEditing)
                        {
                            _editBuffer = dataRow[col]?.ToString() ?? "";
                            ImGui.SetKeyboardFocusHere();
                            _startEditing = false;
                        }

                        ImGui.SetNextItemWidth(-1);
                        bool enterPressed = ImGui.InputText("##edit", ref _editBuffer, 256, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

                        // --- FIX START ---
                        // The corrected logic for stopping the edit.
                        // We check for Escape first to cancel.
                        // Then, we check if Enter was pressed OR if the input box lost focus (was deactivated).
                        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                        {
                            StopEditing(false); // Cancel edit
                        }
                        else if (enterPressed || ImGui.IsItemDeactivated())
                        {
                            StopEditing(true); // Commit edit
                        }
                        // --- FIX END ---
                    }
                    else
                    {
                        // This block executes for a normal, non-editing cell.
                        var isSelected = _selectedRow == row && _selectedColumn == col;
                        var cellValue = dataRow[col]?.ToString() ?? "";

                        if (ImGui.Selectable(cellValue, isSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
                        {
                            _selectedRow = row;
                            _selectedColumn = col;
                            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                            {
                                _editingCell = (row, col);
                                _startEditing = true;
                            }
                        }

                        // Show tooltip for truncated text
                        if (ImGui.IsItemHovered() && ImGui.CalcTextSize(cellValue).X > _columnWidths.GetValueOrDefault(col, DEFAULT_COLUMN_WIDTH))
                            ImGui.SetTooltip(cellValue);

                        // Context menu for cell
                        if (ImGui.BeginPopupContextItem())
                        {
                            if (ImGui.MenuItem("Copy Cell")) ImGui.SetClipboardText(cellValue);
                            if (ImGui.MenuItem("Copy Row")) CopyRowToClipboard(dataRow);
                            if (ImGui.MenuItem("Copy Column")) CopyColumnToClipboard(col);
                            ImGui.EndPopup();
                        }
                    }
                    ImGui.PopID();
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

        if (ImGui.BeginTable("StatsTable", 8,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
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

        var totalPages = (int)Math.Ceiling(_filteredRows.Count / (double)_rowsPerPage);

        ImGui.Separator();

        // Previous button
        if (_currentPage <= 0) ImGui.BeginDisabled();
        if (ImGui.Button("Previous")) _currentPage--;
        if (_currentPage <= 0) ImGui.EndDisabled();

        ImGui.SameLine();

        // Page info
        ImGui.Text($"Page {_currentPage + 1} of {Math.Max(1, totalPages)}");

        ImGui.SameLine();

        // Next button
        if (_currentPage >= totalPages - 1) ImGui.BeginDisabled();
        if (ImGui.Button("Next")) _currentPage++;
        if (_currentPage >= totalPages - 1) ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.Text(
            $"| Showing {Math.Min(_filteredRows.Count, _currentPage * _rowsPerPage + 1)}-{Math.Min(_filteredRows.Count, (_currentPage + 1) * _rowsPerPage)} of {_filteredRows.Count} rows");

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
            var filter = _searchFilter.ToLower();
            _filteredRows = new List<DataRow>();

            foreach (DataRow row in _dataTable.Rows)
            {
                var matches = false;
                foreach (var item in row.ItemArray)
                    if (item != null && item.ToString().ToLower().Contains(filter))
                    {
                        matches = true;
                        break;
                    }

                if (matches) _filteredRows.Add(row);
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
            _filteredRows = ascending
                ? _filteredRows.OrderBy(r =>
                    r[columnIndex] == DBNull.Value ? double.MinValue : Convert.ToDouble(r[columnIndex])).ToList()
                : _filteredRows.OrderByDescending(r =>
                    r[columnIndex] == DBNull.Value ? double.MinValue : Convert.ToDouble(r[columnIndex])).ToList();
        else if (column.DataType == typeof(DateTime))
            _filteredRows = ascending
                ? _filteredRows
                    .OrderBy(r => r[columnIndex] == DBNull.Value ? DateTime.MinValue : (DateTime)r[columnIndex])
                    .ToList()
                : _filteredRows.OrderByDescending(r =>
                    r[columnIndex] == DBNull.Value ? DateTime.MinValue : (DateTime)r[columnIndex]).ToList();
        else
            _filteredRows = ascending
                ? _filteredRows.OrderBy(r => r[columnIndex]?.ToString() ?? "").ToList()
                : _filteredRows.OrderByDescending(r => r[columnIndex]?.ToString() ?? "").ToList();

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

    private void ExportTable(string path, string delimiter, bool includeHeaders, bool filteredOnly)
    {
        try
        {
            var rowsToExport = filteredOnly ? _filteredRows : _dataTable.Rows.Cast<DataRow>().ToList();

            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
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
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}