// GeoscientistToolkit/UI/GIS/ShapefileCreationDialog.cs

using System.Data;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS;

public class ShapefileCreationDialog
{
    private readonly List<string> _availableColumns = new();
    private readonly string[] _geometryTypes = { "Point", "Line", "Polygon" };

    // --- REMOVED: Save dialog is not needed for layer creation ---

    private bool _isOpen;
    private int _latColumnIndex = -1;
    private int _lonColumnIndex = -1;
    private CreationMode _mode = CreationMode.Empty;

    // Empty shapefile options
    private string _newLayerName = "New Layer";
    private int _selectedGeometryType;

    // From table options
    private TableDataset _selectedTable;
    private GISDataset _targetDataset;

    public void OpenEmpty(GISDataset targetDataset)
    {
        _isOpen = true;
        _targetDataset = targetDataset;
        _mode = CreationMode.Empty;
        _newLayerName = "New Layer";
    }

    public void OpenFromTable(GISDataset targetDataset)
    {
        _isOpen = true;
        _targetDataset = targetDataset;
        _mode = CreationMode.FromTable;
        _newLayerName = "Points from Table";

        // Find available tables
        _selectedTable = ProjectManager.Instance.LoadedDatasets
            .OfType<TableDataset>()
            .FirstOrDefault();

        UpdateAvailableColumns();
    }

    public void Submit()
    {
        if (!_isOpen) return;

        // --- REMOVED: Save dialog handling ---

        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.Begin("Create New Layer", ref _isOpen, ImGuiWindowFlags.NoCollapse))
        {
            if (_mode == CreationMode.Empty)
                DrawEmptyShapefileOptions();
            else
                DrawFromTableOptions();

            ImGui.End();
        }
    }

    private void DrawEmptyShapefileOptions()
    {
        ImGui.TextWrapped("Create a new empty vector layer in the current dataset.");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Layer Name:");
        ImGui.SetNextItemWidth(300);
        ImGui.InputText("##LayerName", ref _newLayerName, 256);

        ImGui.Spacing();
        ImGui.Text("Geometry Type:");
        ImGui.SetNextItemWidth(200);
        ImGui.Combo("##GeometryType", ref _selectedGeometryType, _geometryTypes, _geometryTypes.Length);

        ImGui.Spacing();
        ImGui.Separator();

        if (ImGui.Button("Create Layer", new Vector2(150, 0))) CreateEmptyLayer();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(100, 0))) _isOpen = false;
    }

    private void DrawFromTableOptions()
    {
        ImGui.TextWrapped("Create a new point layer from a table by selecting coordinate columns.");
        ImGui.Separator();
        ImGui.Spacing();

        // Table selection
        ImGui.Text("Source Table:");
        var tables = ProjectManager.Instance.LoadedDatasets
            .OfType<TableDataset>()
            .ToList();

        if (tables.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No table datasets loaded");
        }
        else
        {
            ImGui.SetNextItemWidth(300);
            if (ImGui.BeginCombo("##TableSelect", _selectedTable?.Name ?? "Select table..."))
            {
                foreach (var table in tables)
                    if (ImGui.Selectable(table.Name, table == _selectedTable))
                    {
                        _selectedTable = table;
                        UpdateAvailableColumns();
                    }

                ImGui.EndCombo();
            }
        }

        if (_selectedTable != null && _availableColumns.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Coordinate Columns:");

            // Longitude column
            ImGui.Text("Longitude (X):");
            ImGui.SetNextItemWidth(200);
            if (ImGui.BeginCombo("##LonColumn",
                    _lonColumnIndex >= 0 ? _availableColumns[_lonColumnIndex] : "Select..."))
            {
                for (var i = 0; i < _availableColumns.Count; i++)
                    if (ImGui.Selectable(_availableColumns[i], i == _lonColumnIndex))
                        _lonColumnIndex = i;

                ImGui.EndCombo();
            }

            // Latitude column
            ImGui.Text("Latitude (Y):");
            ImGui.SetNextItemWidth(200);
            if (ImGui.BeginCombo("##LatColumn",
                    _latColumnIndex >= 0 ? _availableColumns[_latColumnIndex] : "Select..."))
            {
                for (var i = 0; i < _availableColumns.Count; i++)
                    if (ImGui.Selectable(_availableColumns[i], i == _latColumnIndex))
                        _latColumnIndex = i;

                ImGui.EndCombo();
            }

            ImGui.Spacing();
            ImGui.Text("New Layer Name:");
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("##LayerNameFromTable", ref _newLayerName, 256);
        }

        ImGui.Spacing();
        ImGui.Separator();

        var canCreate = _selectedTable != null && _lonColumnIndex >= 0 && _latColumnIndex >= 0;

        if (!canCreate) ImGui.BeginDisabled();

        if (ImGui.Button("Create Layer", new Vector2(150, 0))) CreateLayerFromTable();


        if (!canCreate) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(100, 0))) _isOpen = false;
    }

    private void UpdateAvailableColumns()
    {
        _availableColumns.Clear();
        _lonColumnIndex = -1;
        _latColumnIndex = -1;

        var dataTable = _selectedTable?.GetDataTable();
        if (dataTable == null) return;


        foreach (var column in dataTable.Columns.Cast<DataColumn>()) _availableColumns.Add(column.ColumnName);

        // Try to auto-detect coordinate columns
        for (var i = 0; i < _availableColumns.Count; i++)
        {
            var colName = _availableColumns[i].ToLower();

            if (_lonColumnIndex < 0 && (colName.Contains("lon") || colName.Contains("x") || colName.Contains("east")))
                _lonColumnIndex = i;

            if (_latColumnIndex < 0 && (colName.Contains("lat") || colName.Contains("y") || colName.Contains("north")))
                _latColumnIndex = i;
        }
    }

    private void CreateEmptyLayer()
    {
        try
        {
            // --- MODIFIED: Creates a layer, does not save a file ---
            var featureType = _selectedGeometryType switch
            {
                0 => FeatureType.Point,
                1 => FeatureType.Line,
                2 => FeatureType.Polygon,
                _ => FeatureType.Point
            };

            var layer = new GISLayer
            {
                Name = _newLayerName,
                Type = LayerType.Vector,
                IsVisible = true,
                IsEditable = true,
                Color = new Vector4(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle(),
                    1.0f)
            };

            _targetDataset.Layers.Add(layer);

            Logger.Log($"Created empty layer: {_newLayerName}");
            _isOpen = false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create empty layer: {ex.Message}");
        }
    }

    private void CreateLayerFromTable()
    {
        // --- MODIFIED: Creates a layer, does not save a file ---
        var dataTable = _selectedTable?.GetDataTable();
        if (dataTable == null) return;

        try
        {
            var layer = new GISLayer
            {
                Name = _newLayerName,
                Type = LayerType.Vector,
                IsVisible = true,
                IsEditable = true,
                Color = new Vector4(1.0f, 0.2f, 0.2f, 1.0f)
            };

            var lonColName = _availableColumns[_lonColumnIndex];
            var latColName = _availableColumns[_latColumnIndex];

            foreach (DataRow row in dataTable.Rows)
                try
                {
                    var lon = Convert.ToDouble(row[lonColName]);
                    var lat = Convert.ToDouble(row[latColName]);

                    var feature = new GISFeature
                    {
                        Type = FeatureType.Point,
                        Coordinates = new List<Vector2> { new((float)lon, (float)lat) },
                        Properties = new Dictionary<string, object>()
                    };


                    foreach (DataColumn col in dataTable.Columns)
                        if (col.ColumnName != lonColName && col.ColumnName != latColName)
                            feature.Properties[col.ColumnName] = row[col] ?? "";

                    layer.Features.Add(feature);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Skipped row due to invalid coordinates: {ex.Message}");
                }

            _targetDataset.Layers.Add(layer);
            _targetDataset.UpdateBounds();

            Logger.Log($"Created layer from table with {layer.Features.Count} points");
            _isOpen = false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create layer from table: {ex.Message}");
        }
    }

    private enum CreationMode
    {
        Empty,
        FromTable
    }
}