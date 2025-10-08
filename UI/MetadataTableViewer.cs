// GeoscientistToolkit/UI/MetadataTableViewer.cs - Complete with GIS Integration

using System.Data;
using System.Numerics;
using ClosedXML.Excel;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public class MetadataTableViewer
{
    // Export dialogs
    private readonly ImGuiExportFileDialog _csvExportDialog;
    private readonly MetadataEditor _metadataEditor = new();
    private readonly ImGuiExportFileDialog _xlsxExportDialog;
    private bool _isOpen;
    private DataTable _metadataTable;
    private string _searchFilter = "";
    private int _selectedRow = -1;

    public MetadataTableViewer()
    {
        _csvExportDialog = new ImGuiExportFileDialog("MetadataCSVExport", "Export Metadata to CSV");
        _csvExportDialog.SetExtensions((".csv", "CSV (Comma-separated values)"));

        _xlsxExportDialog = new ImGuiExportFileDialog("MetadataXLSXExport", "Export Metadata to Excel");
        _xlsxExportDialog.SetExtensions((".xlsx", "Excel Workbook"));
    }

    public void Open()
    {
        BuildMetadataTable();
        _isOpen = true;
    }

    private void BuildMetadataTable()
    {
        _metadataTable = new DataTable("DatasetMetadata");

        // Define columns
        _metadataTable.Columns.Add("Dataset Name", typeof(string));
        _metadataTable.Columns.Add("Dataset Type", typeof(string));
        _metadataTable.Columns.Add("Sample Name", typeof(string));
        _metadataTable.Columns.Add("Location Name", typeof(string));
        _metadataTable.Columns.Add("Latitude", typeof(double));
        _metadataTable.Columns.Add("Longitude", typeof(double));
        _metadataTable.Columns.Add("Depth (m)", typeof(double));
        _metadataTable.Columns.Add("Size X", typeof(float));
        _metadataTable.Columns.Add("Size Y", typeof(float));
        _metadataTable.Columns.Add("Size Z", typeof(float));
        _metadataTable.Columns.Add("Size Unit", typeof(string));
        _metadataTable.Columns.Add("Collection Date", typeof(DateTime));
        _metadataTable.Columns.Add("Collector", typeof(string));
        _metadataTable.Columns.Add("Notes", typeof(string));

        // Populate with data from loaded datasets
        foreach (var dataset in ProjectManager.Instance.LoadedDatasets)
        {
            var meta = dataset.DatasetMetadata;
            var row = _metadataTable.NewRow();

            row["Dataset Name"] = dataset.Name;
            row["Dataset Type"] = dataset.Type.ToString();
            row["Sample Name"] = meta.SampleName ?? "";
            row["Location Name"] = meta.LocationName ?? "";
            row["Latitude"] = meta.Latitude ?? (object)DBNull.Value;
            row["Longitude"] = meta.Longitude ?? (object)DBNull.Value;
            row["Depth (m)"] = meta.Depth ?? (object)DBNull.Value;

            if (meta.Size.HasValue)
            {
                row["Size X"] = meta.Size.Value.X;
                row["Size Y"] = meta.Size.Value.Y;
                row["Size Z"] = meta.Size.Value.Z;
                row["Size Unit"] = meta.SizeUnit;
            }
            else
            {
                row["Size X"] = DBNull.Value;
                row["Size Y"] = DBNull.Value;
                row["Size Z"] = DBNull.Value;
                row["Size Unit"] = "";
            }

            row["Collection Date"] = meta.CollectionDate ?? (object)DBNull.Value;
            row["Collector"] = meta.Collector ?? "";
            row["Notes"] = meta.Notes ?? "";

            _metadataTable.Rows.Add(row);
        }
    }

    public void Submit()
    {
        if (!_isOpen) return;

        // Handle export dialogs
        if (_csvExportDialog.Submit()) ExportToCSV(_csvExportDialog.SelectedPath);

        if (_xlsxExportDialog.Submit()) ExportToExcel(_xlsxExportDialog.SelectedPath);

        // Submit metadata editor if open
        _metadataEditor.Submit();

        ImGui.SetNextWindowSize(new Vector2(1200, 600), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Dataset Metadata Table###MetadataTableViewer", ref _isOpen))
        {
            DrawToolbar();
            ImGui.Separator();
            DrawTable();
            ImGui.End();
        }
    }

    private void DrawToolbar()
    {
        // Search
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputTextWithHint("##Search", "Search...", ref _searchFilter, 256))
        {
            // Filter will be applied in DrawTable
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear")) _searchFilter = "";

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Refresh
        if (ImGui.Button("Refresh")) BuildMetadataTable();

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Export buttons
        if (ImGui.Button("Export CSV...")) _csvExportDialog.Open("metadata_export");

        ImGui.SameLine();
        if (ImGui.Button("Export Excel...")) _xlsxExportDialog.Open("metadata_export");

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // GIS Map creation
        if (ImGui.Button("Create GIS Map")) CreateGISFromMetadata();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Create a new GIS map with sample locations from metadata coordinates");

        ImGui.SameLine();

        // Statistics
        ImGui.Text($"| {_metadataTable.Rows.Count} datasets");

        // Count datasets with coordinates
        var datasetsWithCoords = 0;
        foreach (DataRow row in _metadataTable.Rows)
            if (row["Latitude"] != DBNull.Value && row["Longitude"] != DBNull.Value)
                datasetsWithCoords++;

        if (datasetsWithCoords > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
                $"({datasetsWithCoords} with coordinates)");
        }
    }

    private void DrawTable()
    {
        if (_metadataTable == null || _metadataTable.Columns.Count == 0)
        {
            ImGui.TextDisabled("No metadata to display");
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

        if (ImGui.BeginTable("MetadataTable", _metadataTable.Columns.Count, tableFlags))
        {
            // Setup columns
            foreach (DataColumn column in _metadataTable.Columns)
                ImGui.TableSetupColumn(column.ColumnName, ImGuiTableColumnFlags.None);

            ImGui.TableSetupScrollFreeze(1, 1); // Freeze first column and header row
            ImGui.TableHeadersRow();

            // Draw rows
            for (var row = 0; row < _metadataTable.Rows.Count; row++)
            {
                var dataRow = _metadataTable.Rows[row];

                // Apply search filter
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    var matches = false;
                    foreach (var item in dataRow.ItemArray)
                        if (item != null && item.ToString().Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            matches = true;
                            break;
                        }

                    if (!matches) continue;
                }

                ImGui.TableNextRow();

                for (var col = 0; col < _metadataTable.Columns.Count; col++)
                {
                    ImGui.TableNextColumn();

                    var cellValue = dataRow[col]?.ToString() ?? "";

                    // Special formatting for certain columns
                    if (_metadataTable.Columns[col].ColumnName == "Latitude" ||
                        _metadataTable.Columns[col].ColumnName == "Longitude")
                    {
                        if (dataRow[col] != DBNull.Value)
                        {
                            var val = Convert.ToDouble(dataRow[col]);
                            cellValue = val.ToString("F6");

                            // Add degree symbol and hemisphere
                            if (_metadataTable.Columns[col].ColumnName == "Latitude")
                                cellValue += $"° {(val >= 0 ? "N" : "S")}";
                            else
                                cellValue += $"° {(val >= 0 ? "E" : "W")}";
                        }
                    }
                    else if (_metadataTable.Columns[col].DataType == typeof(double) ||
                             _metadataTable.Columns[col].DataType == typeof(float))
                    {
                        if (dataRow[col] != DBNull.Value)
                        {
                            var val = Convert.ToDouble(dataRow[col]);
                            cellValue = val.ToString("F2");
                        }
                    }
                    else if (_metadataTable.Columns[col].DataType == typeof(DateTime))
                    {
                        if (dataRow[col] != DBNull.Value)
                        {
                            var dt = (DateTime)dataRow[col];
                            cellValue = dt.ToString("yyyy-MM-dd");
                        }
                    }

                    // Make first column (Dataset Name) clickable
                    if (col == 0)
                    {
                        if (ImGui.Selectable(cellValue, _selectedRow == row))
                        {
                            _selectedRow = row;

                            // Double-click to edit
                            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                            {
                                var datasetName = dataRow["Dataset Name"].ToString();
                                var dataset = ProjectManager.Instance.LoadedDatasets
                                    .FirstOrDefault(d => d.Name == datasetName);

                                if (dataset != null) _metadataEditor.Open(dataset);
                            }
                        }

                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Double-click to edit metadata");
                    }
                    else
                    {
                        ImGui.Text(cellValue);
                    }

                    // Show full text in tooltip for long values
                    if (ImGui.IsItemHovered() && cellValue.Length > 30) ImGui.SetTooltip(cellValue);
                }
            }

            ImGui.EndTable();
        }

        // Status bar
        ImGui.Separator();
        ImGui.Text("Double-click a dataset name to edit its metadata");
    }

    private void CreateGISFromMetadata()
    {
        // Get datasets with coordinates
        var datasetsWithCoords = ProjectManager.Instance.LoadedDatasets
            .Where(d => d.DatasetMetadata?.Latitude != null && d.DatasetMetadata?.Longitude != null)
            .ToList();

        if (datasetsWithCoords.Count == 0)
        {
            Logger.LogWarning("No datasets have coordinate metadata. Cannot create GIS map.");
            return;
        }

        // Create new GIS dataset
        var gisDataset = new GISDataset("Sample Locations Map", "");

        // Create layer from metadata
        var layer = gisDataset.CreateLayerFromMetadata(datasetsWithCoords);
        gisDataset.Layers.Clear(); // Remove default layer
        gisDataset.Layers.Add(layer);

        // Calculate bounds and center
        gisDataset.UpdateBounds();

        // Add to project
        ProjectManager.Instance.AddDataset(gisDataset);

        Logger.Log($"Created GIS map with {layer.Features.Count} sample locations");

        // Show success message
        ImGui.OpenPopup("GIS Map Created");
    }

    private void ExportToCSV(string path)
    {
        if (string.IsNullOrEmpty(path) || _metadataTable == null) return;

        try
        {
            using (var writer = new StreamWriter(path))
            {
                // Write headers
                var headers = _metadataTable.Columns.Cast<DataColumn>()
                    .Select(col => EscapeCSVField(col.ColumnName));
                writer.WriteLine(string.Join(",", headers));

                // Write data
                foreach (DataRow row in _metadataTable.Rows)
                {
                    var values = row.ItemArray.Select(field =>
                    {
                        if (field == DBNull.Value || field == null)
                            return "";
                        if (field is DateTime dt)
                            return EscapeCSVField(dt.ToString("yyyy-MM-dd"));
                        if (field is double d)
                            return d.ToString("F6");
                        if (field is float f)
                            return f.ToString("F6");
                        return EscapeCSVField(field.ToString());
                    });
                    writer.WriteLine(string.Join(",", values));
                }
            }

            Logger.Log($"Metadata exported to CSV: {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export metadata: {ex.Message}");
        }
    }

    private void ExportToExcel(string path)
    {
        if (string.IsNullOrEmpty(path) || _metadataTable == null) return;

        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Dataset Metadata");

                // Add headers with formatting
                for (var col = 0; col < _metadataTable.Columns.Count; col++)
                {
                    var cell = worksheet.Cell(1, col + 1);
                    cell.Value = _metadataTable.Columns[col].ColumnName;
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                }

                // Add data with appropriate formatting
                for (var row = 0; row < _metadataTable.Rows.Count; row++)
                for (var col = 0; col < _metadataTable.Columns.Count; col++)
                {
                    var value = _metadataTable.Rows[row][col];
                    var cell = worksheet.Cell(row + 2, col + 1);

                    if (value != DBNull.Value && value != null)
                    {
                        // Apply formatting based on column type
                        if (_metadataTable.Columns[col].ColumnName == "Latitude" ||
                            _metadataTable.Columns[col].ColumnName == "Longitude")
                        {
                            cell.Value = Convert.ToDouble(value);
                            cell.Style.NumberFormat.Format = "0.000000";
                        }
                        else if (_metadataTable.Columns[col].DataType == typeof(DateTime))
                        {
                            cell.Value = (DateTime)value;
                            cell.Style.DateFormat.Format = "yyyy-mm-dd";
                        }
                        else if (_metadataTable.Columns[col].DataType == typeof(double) ||
                                 _metadataTable.Columns[col].DataType == typeof(float))
                        {
                            cell.Value = Convert.ToDouble(value);
                            cell.Style.NumberFormat.Format = "0.00";
                        }
                        else
                        {
                            cell.Value = value.ToString();
                        }
                    }
                }

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                // Add filters
                worksheet.RangeUsed().SetAutoFilter();

                // Freeze the header row
                worksheet.SheetView.FreezeRows(1);

                // Add conditional formatting for coordinates
                var latColumn = worksheet.ColumnsUsed()
                    .FirstOrDefault(c => c.FirstCell().Value.ToString() == "Latitude");
                var lonColumn = worksheet.ColumnsUsed()
                    .FirstOrDefault(c => c.FirstCell().Value.ToString() == "Longitude");

                if (latColumn != null && lonColumn != null)
                    // Highlight rows with coordinates in light green
                    for (var row = 2; row <= worksheet.LastRowUsed().RowNumber(); row++)
                    {
                        var latCell = latColumn.Cell(row);
                        var lonCell = lonColumn.Cell(row);

                        if (!latCell.IsEmpty() && !lonCell.IsEmpty())
                            worksheet.Row(row).Style.Fill.BackgroundColor = XLColor.LightGreen;
                    }

                workbook.SaveAs(path);
            }

            Logger.Log($"Metadata exported to Excel: {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export metadata: {ex.Message}");
        }
    }

    private string EscapeCSVField(string field)
    {
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}