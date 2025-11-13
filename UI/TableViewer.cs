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
using System.Linq;
using System.Collections.Generic;

namespace GeoscientistToolkit.UI;

public class TableViewer : IDatasetViewer, IDisposable
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

    // Cell editing state
    private (int Row, int Col) _editingCell = (-1, -1);
    private string _editBuffer = "";
    private bool _startEditing;

    // Export/dialog/filter state
    private List<DataRow> _filteredRows;
    private bool _includeHeaders = true;
    private int _rowsPerPage = 100;
    private string _searchFilter = "";
    private int _selectedColumn = -1;
    private int _selectedRow = -1;
    private bool _showStatistics;

    // Header/row context menu state
    private int _headerCtxColumn = -1;
    private bool _showRenameModal = false;
    private string _renameBuffer = "";
    private bool _addLeftRequested = false;
    private bool _addRightRequested = false;
    private bool _deleteColRequested = false;

    private int _rowCtxIndex = -1;
    private bool _insertAboveRequested = false;
    private bool _insertBelowRequested = false;
    private bool _deleteRowRequested = false;

    // ❗ Last-row deletion warning
    private bool _showLastRowWarning = false;

    public TableViewer(TableDataset dataset)
    {
        _dataset = dataset;
        _dataset.DataChanged += OnDatasetChanged; // live updates
        RefreshData();
        _csvExportDialog = new ImGuiExportFileDialog($"{_dataset.Name}_ExportCSV", "Export table to CSV");
        _csvExportDialog.SetExtensions((".csv", "CSV (Comma-separated values)"));

        _tsvExportDialog = new ImGuiExportFileDialog($"{_dataset.Name}_ExportTSV", "Export table to TSV");
        _tsvExportDialog.SetExtensions(
            (".tsv", "TSV (Tab-separated values)"),
            (".txt", "Text file"));
    }

    private void OnDatasetChanged(TableDataset _)
    {
        var filterBackup = _searchFilter;
        var pageBackup = _currentPage;

        RefreshData();
        _searchFilter = filterBackup;
        ApplyFilter();
        _currentPage = Math.Min(pageBackup, Math.Max(0, (int)Math.Ceiling(_filteredRows.Count / (double)_rowsPerPage) - 1));
    }

    public void DrawToolbarControls()
    {
        // SEARCH
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

        // STATISTICS TOGGLE
        ImGui.Checkbox("Show Statistics", ref _showStatistics);

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // EXPORTS
        if (ImGui.Button("Export CSV…"))
            _csvExportDialog.Open($"{_dataset.Name}_export");

        ImGui.SameLine();

        if (ImGui.Button("Export TSV…"))
            _tsvExportDialog.Open($"{_dataset.Name}_export");

        ImGui.SameLine();
        ImGui.Checkbox("Include headers", ref _includeHeaders);
        ImGui.SameLine();
        ImGui.Checkbox("Filtered only", ref _exportFiltered);

        // PAGINATION
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

        var paginationHeight = 40f;
        var tableHeight = availableHeight - paginationHeight;

        ImGui.BeginChild("TableScrollRegion", new Vector2(0, tableHeight), ImGuiChildFlags.Border);
        DrawTable();
        ImGui.EndChild();

        DrawPagination();

        ProcessHeaderContextActions();
        ProcessRowContextActions();

        // Render the fullscreen warning modal if needed
        RenderLastRowWarningModal();
    }

    public void Dispose()
    {
        _dataset.DataChanged -= OnDatasetChanged;
        _dataTable?.Dispose();
        _filteredRows?.Clear();
    }

    private void HandleExportDialogs()
    {
        if (_csvExportDialog.Submit())
            ExportTable(_csvExportDialog.SelectedPath, ",", _includeHeaders, _exportFiltered);

        if (_tsvExportDialog.Submit())
            ExportTable(_tsvExportDialog.SelectedPath, "\t", _includeHeaders, _exportFiltered);
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
            var nameWidth = ImGui.CalcTextSize(_dataTable.Columns[i].ColumnName).X + 20;
            var dataWidth = DEFAULT_COLUMN_WIDTH;

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

            ImGui.TableSetupScrollFreeze(1, 1);

            // Headers with right-click menus
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

            ImGui.TableSetColumnIndex(0);
            ImGui.TableHeader("#");

            for (int i = 0; i < _dataTable.Columns.Count; i++)
            {
                ImGui.TableSetColumnIndex(i + 1);
                var label = _dataTable.Columns[i].ColumnName;
                ImGui.TableHeader(label);

                // Right-click header => column operations
                if (ImGui.BeginPopupContextItem()) // bind to last header item
                {
                    _headerCtxColumn = i;

                    if (ImGui.MenuItem("Rename Column…"))
                    {
                        _renameBuffer = label;
                        _showRenameModal = true;
                        ImGui.CloseCurrentPopup();
                    }
                    if (ImGui.MenuItem("Add Column Left"))
                    {
                        _addLeftRequested = true;
                        ImGui.CloseCurrentPopup();
                    }
                    if (ImGui.MenuItem("Add Column Right"))
                    {
                        _addRightRequested = true;
                        ImGui.CloseCurrentPopup();
                    }
                    if (ImGui.MenuItem("Delete Column"))
                    {
                        _deleteColRequested = true;
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.EndPopup();
                }
            }

            // Handle sorting
            unsafe
            {
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.NativePtr != null && sortSpecs.SpecsDirty)
                {
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

                // Row index column (right-click shows row menu)
                ImGui.TableNextColumn();
                ImGui.PushID(row);
                bool dummySelected = _selectedRow == row && _selectedColumn == -1;
                if (ImGui.Selectable((row + 1).ToString(), dummySelected, ImGuiSelectableFlags.SpanAllColumns))
                {
                    _selectedRow = row;
                    _selectedColumn = -1;
                }
                ImGui.OpenPopupOnItemClick("RowMenu", ImGuiPopupFlags.MouseButtonRight);
                if (ImGui.BeginPopup("RowMenu"))
                {
                    _rowCtxIndex = row;
                    if (ImGui.MenuItem("Insert Row Above")) _insertAboveRequested = true;
                    if (ImGui.MenuItem("Insert Row Below")) _insertBelowRequested = true;
                    if (ImGui.MenuItem("Delete Row")) _deleteRowRequested = true;
                    ImGui.EndPopup();
                }
                ImGui.PopID();

                // Data columns
                for (var col = 0; col < _dataTable.Columns.Count; col++)
                {
                    ImGui.TableNextColumn();
                    ImGui.PushID($"cell_{row}_{col}");

                    bool isEditingThisCell = _editingCell.Row == row && _editingCell.Col == col;

                    if (isEditingThisCell)
                    {
                        if (_startEditing)
                        {
                            _editBuffer = dataRow[col]?.ToString() ?? "";
                            ImGui.SetKeyboardFocusHere();
                            _startEditing = false;
                        }

                        ImGui.SetNextItemWidth(-1);
                        bool enterPressed = ImGui.InputText("##edit", ref _editBuffer, 256, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

                        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                        {
                            StopEditing(false); // Cancel edit
                        }
                        else if (enterPressed || ImGui.IsItemDeactivated())
                        {
                            StopEditing(true); // Commit edit
                        }
                    }
                    else
                    {
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

                        if (ImGui.IsItemHovered() && ImGui.CalcTextSize(cellValue).X > _columnWidths.GetValueOrDefault(col, DEFAULT_COLUMN_WIDTH))
                            ImGui.SetTooltip(cellValue);

                        // Cell context menu — includes row actions
                        if (ImGui.BeginPopupContextItem())
                        {
                            if (ImGui.MenuItem("Copy Cell")) ImGui.SetClipboardText(cellValue);
                            if (ImGui.MenuItem("Copy Row")) CopyRowToClipboard(dataRow);
                            if (ImGui.MenuItem("Copy Column")) CopyColumnToClipboard(col);
                            ImGui.Separator();
                            _rowCtxIndex = row;
                            if (ImGui.MenuItem("Insert Row Above")) _insertAboveRequested = true;
                            if (ImGui.MenuItem("Insert Row Below")) _insertBelowRequested = true;
                            if (ImGui.MenuItem("Delete Row")) _deleteRowRequested = true;
                            ImGui.EndPopup();
                        }
                    }
                    ImGui.PopID();
                }
            }

            ImGui.EndTable();
        }

        // Rename modal
        if (_showRenameModal)
        {
            ImGui.OpenPopup("Rename Column");
            _showRenameModal = false;
        }
        if (ImGui.BeginPopupModal("Rename Column", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("New name", ref _renameBuffer, 128);
            ImGui.Separator();
            if (ImGui.Button("OK", new Vector2(100, 0)))
            {
                if (_headerCtxColumn >= 0 && _headerCtxColumn < _dataTable.Columns.Count && !string.IsNullOrWhiteSpace(_renameBuffer))
                {
                    var dt = _dataset.GetDataTable();
                    dt.Columns[_headerCtxColumn].ColumnName = _renameBuffer;
                    _dataset.UpdateDataTable(dt);
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawStatistics()
    {
        if (_dataTable == null) return;

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

        if (_currentPage <= 0) ImGui.BeginDisabled();
        if (ImGui.Button("Previous")) _currentPage--;
        if (_currentPage <= 0) ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.Text($"Page {_currentPage + 1} of {Math.Max(1, totalPages)}");

        ImGui.SameLine();

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
                if (includeHeaders)
                {
                    var headers = _dataTable.Columns.Cast<DataColumn>()
                        .Select(col => EscapeCsvField(col.ColumnName, delimiter[0]));
                    writer.WriteLine(string.Join(delimiter, headers));
                }

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

    // ───────────────────────────── Context actions ─────────────────────────────

    private void ProcessHeaderContextActions()
    {
        if (_headerCtxColumn < 0 || _headerCtxColumn >= (_dataTable?.Columns.Count ?? 0))
            return;

        if (_addLeftRequested || _addRightRequested || _deleteColRequested)
        {
            var dt = _dataset.GetDataTable();

            if (_deleteColRequested && dt.Columns.Count > 0)
            {
                dt.Columns.RemoveAt(_headerCtxColumn);
            }
            else
            {
                string baseName = "NewColumn";
                string name = baseName;
                int suffix = 1;
                while (dt.Columns.Contains(name))
                    name = $"{baseName}{suffix++}";

                var newCol = dt.Columns.Add(name, typeof(string));

                if (_addLeftRequested)
                    newCol.SetOrdinal(Math.Max(0, _headerCtxColumn));
                else if (_addRightRequested)
                    newCol.SetOrdinal(Math.Min(dt.Columns.Count - 1, _headerCtxColumn + 1));
            }

            _dataset.UpdateDataTable(dt);
        }

        _addLeftRequested = _addRightRequested = _deleteColRequested = false;
        _headerCtxColumn = -1;
    }

    private void ProcessRowContextActions()
    {
        if (_rowCtxIndex < 0 || _rowCtxIndex >= _filteredRows?.Count)
            return;

        if (_insertAboveRequested || _insertBelowRequested || _deleteRowRequested)
        {
            // Map the filtered row back to the original index using THIS viewer's _dataTable
            var dataRow = _filteredRows[_rowCtxIndex];
            int originalRowIndex = _dataTable.Rows.IndexOf(dataRow);

            if (originalRowIndex >= 0)
            {
                var dt = _dataset.GetDataTable();

                if (_deleteRowRequested)
                {
                    // 🔒 Prevent deleting the last remaining row
                    if (dt.Rows.Count <= 1)
                    {
                        _showLastRowWarning = true; // show modal instead of deleting
                    }
                    else
                    {
                        dt.Rows.RemoveAt(originalRowIndex);
                        _dataset.UpdateDataTable(dt);
                    }
                }
                else
                {
                    var newRow = dt.NewRow();
                    int insertIndex = originalRowIndex + (_insertBelowRequested ? 1 : 0);
                    insertIndex = Math.Clamp(insertIndex, 0, dt.Rows.Count);
                    dt.Rows.InsertAt(newRow, insertIndex);
                    _dataset.UpdateDataTable(dt);
                }
            }
        }

        _insertAboveRequested = _insertBelowRequested = _deleteRowRequested = false;
        _rowCtxIndex = -1;
    }

    // ───────────────────────────── Fullscreen warning modal ─────────────────────────────

    private void RenderLastRowWarningModal()
    {
        if (!_showLastRowWarning) return;

        // Open the popup if not already open
        ImGui.OpenPopup("Cannot delete last row");

        // Make it fullscreen and immovable
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Vector2(0, 0));
        ImGui.SetNextWindowSize(io.DisplaySize);

        // Red title bar
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.55f, 0.00f, 0.00f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.75f, 0.00f, 0.00f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.40f, 0.00f, 0.00f, 1.00f));

        if (ImGui.BeginPopupModal("Cannot delete last row",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.Dummy(new Vector2(0, 20));
            ImGui.SetCursorPosX((io.DisplaySize.X - ImGui.CalcTextSize("⚠  The last row cannot be deleted.").X) * 0.5f);
            ImGui.Text("⚠  The last row cannot be deleted.");

            ImGui.Dummy(new Vector2(0, 10));
            ImGui.SetCursorPosX((io.DisplaySize.X - ImGui.CalcTextSize("A table must contain at least one row.").X) * 0.5f);
            ImGui.TextDisabled("A table must contain at least one row.");

            ImGui.Dummy(new Vector2(0, 30));
            var buttonSize = new Vector2(120, 0);
            ImGui.SetCursorPosX((io.DisplaySize.X - buttonSize.X) * 0.5f);
            if (ImGui.Button("OK", buttonSize))
            {
                ImGui.CloseCurrentPopup();
                _showLastRowWarning = false;
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleColor(3);
    }
}
