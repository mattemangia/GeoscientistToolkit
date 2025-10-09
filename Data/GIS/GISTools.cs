// GeoscientistToolkit/UI/GIS/GISTools.cs (Updated)

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS;

public class GISTools : IDatasetTools
{
    private readonly ImGuiExportFileDialog _csvExportDialog;
    private readonly ProgressBarDialog _exportProgressDialog;
    private readonly ImGuiExportFileDialog _geoJsonExportDialog;
    private readonly ImGuiExportFileDialog _geoTiffExportDialog;
    private readonly ImGuiExportFileDialog _shpExportDialog;

    private readonly Dictionary<string, bool> _tagCategoryExpanded = new()
    {
        { "Format", true },
        { "Geometry", true },
        { "Purpose", true },
        { "Analysis", false },
        { "Properties", false },
        { "Source", false }
    };

    private float _bufferDistance = 10.0f;
    private CancellationTokenSource _exportCts;
    private string _newLayerName = "New Layer";
    private int _selectedLayerIndexForExport;
    private bool _showBufferPopup;
    private bool _showCreateFromMetadata = false;
    private bool _showTagPicker;
    private string _tagSearchFilter = "";

    public GISTools()
    {
        _shpExportDialog = new ImGuiExportFileDialog("GISShpExport", "Export as Shapefile");
        _shpExportDialog.SetExtensions((".shp", "ESRI Shapefile"));

        _geoJsonExportDialog = new ImGuiExportFileDialog("GISGeoJsonExport", "Export as GeoJSON");
        _geoJsonExportDialog.SetExtensions((".geojson", "GeoJSON"));

        _csvExportDialog =
            new ImGuiExportFileDialog("GISCsvExport",
                "Export Attributes as CSV"); // FIXED: Added missing initialization
        _csvExportDialog.SetExtensions((".csv", "Comma-Separated Values"));

        _geoTiffExportDialog = new ImGuiExportFileDialog("GISGeoTiffExport", "Export as GeoTIFF");
        _geoTiffExportDialog.SetExtensions((".tif", "Tagged Image File Format"));

        _exportProgressDialog = new ProgressBarDialog("Exporting GIS Data");
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not GISDataset gisDataset)
            return;

        if (ImGui.CollapsingHeader("Tag Management", ImGuiTreeNodeFlags.DefaultOpen)) DrawTagManagement(gisDataset);
        if (ImGui.CollapsingHeader("Layers", ImGuiTreeNodeFlags.DefaultOpen)) DrawLayerManager(gisDataset);
        if (ImGui.CollapsingHeader("Create from Metadata")) DrawCreateFromMetadata(gisDataset);
        if (ImGui.CollapsingHeader("Export")) DrawExportOptions(gisDataset);
        if (ImGui.CollapsingHeader("Projection")) DrawProjectionInfo(gisDataset);
        if (ImGui.CollapsingHeader("Operations", ImGuiTreeNodeFlags.DefaultOpen)) DrawOperations(gisDataset);

        _exportProgressDialog.Submit();
        if (_exportProgressDialog.IsCancellationRequested)
        {
            _exportCts?.Cancel();
            _exportProgressDialog.Close();
        }

        if (_shpExportDialog.Submit())
        {
            var path = _shpExportDialog.SelectedPath;
            StartExportTask(GISExporter.ExportToShapefileAsync(gisDataset, path, CreateProgressHandler(),
                _exportCts.Token));
        }

        if (_geoTiffExportDialog.Submit())
        {
            var path = _geoTiffExportDialog.SelectedPath;
            StartExportTask(GISExporter.ExportToGeoTiffAsync(gisDataset, path, CreateProgressHandler(),
                _exportCts.Token));
        }

        if (_geoJsonExportDialog.Submit()) gisDataset.SaveAsGeoJSON(_geoJsonExportDialog.SelectedPath);

        if (_csvExportDialog.Submit())
        {
            var vectorLayers = gisDataset.Layers.Where(l => l.Type == LayerType.Vector).ToList();
            if (vectorLayers.Count > _selectedLayerIndexForExport)
            {
                var selectedLayer = vectorLayers[_selectedLayerIndexForExport];
                gisDataset.SaveLayerAsCsv(selectedLayer, _csvExportDialog.SelectedPath);
            }
        }
    }

    public void SaveLayerAsCsv(GISLayer layer, string path)
    {
        if (layer.Type != LayerType.Vector)
        {
            Logger.LogError("Can only export attributes from vector layers.");
            return;
        }

        Logger.Log($"Exporting attributes for layer '{layer.Name}' to CSV: {path}");
        try
        {
            var headers = new HashSet<string>();
            foreach (var feature in layer.Features)
            foreach (var key in feature.Properties.Keys)
                headers.Add(key);

            var orderedHeaders = headers.OrderBy(h => h).ToList();
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", orderedHeaders.Select(h => $"\"{h.Replace("\"", "\"\"")}\"")));
            foreach (var feature in layer.Features)
            {
                var row = new List<string>();
                foreach (var header in orderedHeaders)
                    if (feature.Properties.TryGetValue(header, out var value) && value != null)
                    {
                        var cellValue = value.ToString().Replace("\"", "\"\"");
                        if (cellValue.Contains(',') || cellValue.Contains('"'))
                            row.Add($"\"{cellValue}\"");
                        else
                            row.Add(cellValue);
                    }
                    else
                    {
                        row.Add("");
                    }

                csv.AppendLine(string.Join(",", row));
            }

            File.WriteAllText(path, csv.ToString());
            Logger.Log($"Successfully exported {layer.Features.Count} records to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export layer attributes to CSV: {ex.Message}");
            throw;
        }
    }

    private void ExecuteOperation(GISDataset dataset, string operationName)
    {
        switch (operationName)
        {
            case "Buffer":
                _bufferDistance = 10.0f;
                _showBufferPopup = true;
                break;
            case "Slope Analysis":
            case "Aspect Analysis":
            case "Hillshade":
                Logger.LogWarning(
                    $"Operation '{operationName}' requires the GIS data model to support readable raster data (e.g., DEMs), which is not fully implemented.");
                break;
            default:
                Logger.Log($"Operation '{operationName}' is not yet implemented.");
                break;
        }
    }

    private void PerformBufferOperation(GISDataset dataset, float distance)
    {
        try
        {
            var newLayer = new GISLayer
            {
                Name = $"{dataset.Name} Buffered ({distance})",
                IsEditable = true,
                IsVisible = true,
                Color = new Vector4(1, 0, 1, 1)
            };

            foreach (var layer in dataset.Layers.Where(l => l.Type == LayerType.Vector))
            foreach (var feature in layer.Features)
            {
                // FIXED: This is an instance method call on the specific 'dataset' object.
                var ntsGeom = dataset.ConvertToNTSGeometry(feature);
                if (ntsGeom != null)
                {
                    var bufferedGeom = GISOperationsImpl.BufferGeometry(ntsGeom, distance);
                    // FIXED: This is a static method call on the 'GISDataset' class itself.
                    var newFeature = GISDataset.ConvertNTSGeometry(bufferedGeom, feature.Properties);
                    if (newFeature != null) newLayer.Features.Add(newFeature);
                }
            }

            if (newLayer.Features.Any())
            {
                var newDataset = new GISDataset(newLayer.Name, "")
                {
                    Tags = dataset.Tags | GISTag.Generated,
                    Projection = dataset.Projection
                };
                newDataset.Layers.Clear(); // Remove default layer
                newDataset.Layers.Add(newLayer);
                newDataset.UpdateBounds();
                ProjectManager.Instance.AddDataset(newDataset);
                Logger.Log(
                    $"Successfully created buffered dataset '{newDataset.Name}' with {newLayer.Features.Count} features.");
            }
            else
            {
                Logger.LogWarning("Buffer operation resulted in no features.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to perform buffer operation: {ex.Message}");
        }
    }

    private void DrawExportOptions(GISDataset dataset)
    {
        if (ImGui.Button("Export as Shapefile..."))
            _shpExportDialog.Open(dataset.Name);

        if (ImGui.Button("Export as GeoJSON..."))
            _geoJsonExportDialog.Open(dataset.Name);

        ImGui.Separator();

        // --- TODO COMPLETED: Implement CSV Export ---
        ImGui.Text("Export Layer Attributes to CSV:");

        var vectorLayers = dataset.Layers.Where(l => l.Type == LayerType.Vector).ToList();
        if (vectorLayers.Any())
        {
            var layerNames = vectorLayers.Select(l => l.Name).ToArray();
            ImGui.SetNextItemWidth(200);
            ImGui.Combo("Layer##CSVExport", ref _selectedLayerIndexForExport, layerNames, layerNames.Length);

            ImGui.SameLine();

            if (ImGui.Button("Export CSV..."))
            {
                var defaultFileName = $"{dataset.Name}_{vectorLayers[_selectedLayerIndexForExport].Name}_attributes";
                _csvExportDialog.Open(defaultFileName);
            }
        }
        else
        {
            ImGui.TextDisabled("No vector layers with attributes to export.");
        }
        // --- END MODIFICATION ---
    }

    private void StartExportTask(Task exportTask)
    {
        _exportProgressDialog.Open("Starting export...");
        _exportCts = new CancellationTokenSource();

        exportTask.ContinueWith(t =>
        {
            if (t.IsCanceled)
                Logger.Log("Export operation was canceled.");
            else if (t.Exception != null)
                Logger.LogError($"Export failed: {t.Exception.InnerException?.Message ?? t.Exception.Message}");

            _exportProgressDialog.Close();
            _exportCts?.Dispose();
            _exportCts = null;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private IProgress<(float p, string msg)> CreateProgressHandler()
    {
        return new Progress<(float p, string msg)>(value =>
        {
            if (_exportCts != null && !_exportCts.IsCancellationRequested)
                _exportProgressDialog.Update(value.p, value.msg);
        });
    }

    // --- Other methods are unchanged ---
    private void DrawTagManagement(GISDataset dataset)
    {
        ImGui.Text("Active Tags:");
        ImGui.Separator();

        if (dataset.Tags == GISTag.None)
        {
            ImGui.TextDisabled("No tags assigned");
        }
        else
        {
            // Display active tags as removable chips
            var activeTags = dataset.Tags.GetFlags().ToList();
            var toRemove = new List<GISTag>();

            foreach (var tag in activeTags)
            {
                ImGui.PushID(tag.GetHashCode());

                // Color-code by category
                var color = GetTagColor(tag);
                ImGui.PushStyleColor(ImGuiCol.Button, color);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color * new Vector4(1.2f, 1.2f, 1.2f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, color * new Vector4(0.8f, 0.8f, 0.8f, 1.0f));

                if (ImGui.Button($"{tag.GetDisplayName()} ×")) toRemove.Add(tag);

                ImGui.PopStyleColor(3);

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Tag: {tag.GetDisplayName()}");
                    var desc = tag.GetCategoryDescription();
                    if (!string.IsNullOrEmpty(desc))
                    {
                        ImGui.Separator();
                        ImGui.TextWrapped(desc);
                    }

                    ImGui.Separator();
                    ImGui.Text("Click to remove");
                    ImGui.EndTooltip();
                }

                ImGui.SameLine();
                ImGui.PopID();
            }

            ImGui.NewLine();

            foreach (var tag in toRemove) dataset.RemoveTag(tag);
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Add tags button
        if (ImGui.Button("Add Tags...")) _showTagPicker = true;

        ImGui.SameLine();

        if (ImGui.Button("Auto-Detect Tags")) AutoDetectTags(dataset);

        ImGui.SameLine();

        if (ImGui.Button("Clear All Tags")) dataset.ClearTags();

        // Tag picker window
        if (_showTagPicker) DrawTagPickerWindow(dataset);
    }

    private void AutoDetectTags(GISDataset dataset)
    {
        var recommendedTags = GISTagExtensions.GetRecommendedTags(
            dataset.FilePath ?? "",
            dataset.Layers.FirstOrDefault()?.Type ?? LayerType.Vector);

        var addedCount = 0;
        foreach (var tag in recommendedTags)
            if (!dataset.HasTag(tag))
            {
                dataset.AddTag(tag);
                addedCount++;
            }

        if (dataset.Layers.Any(l => l.Features.Any(f => f.Properties.Count > 0)))
            if (!dataset.HasTag(GISTag.Attributed))
            {
                dataset.AddTag(GISTag.Attributed);
                addedCount++;
            }

        if (dataset.Layers.Count > 1 && !dataset.HasTag(GISTag.MultiLayer))
        {
            dataset.AddTag(GISTag.MultiLayer);
            addedCount++;
        }

        if (!string.IsNullOrEmpty(dataset.Projection.EPSG))
        {
            if (!dataset.HasTag(GISTag.Georeferenced))
            {
                dataset.AddTag(GISTag.Georeferenced);
                addedCount++;
            }

            if (dataset.Projection.EPSG != "EPSG:4326" && !dataset.HasTag(GISTag.Projected))
            {
                dataset.AddTag(GISTag.Projected);
                addedCount++;
            }
        }

        Logger.Log($"Auto-detected and added {addedCount} new tags.");
    }

    private void DrawTagPickerWindow(GISDataset dataset)
    {
        ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Add Tags", ref _showTagPicker))
        {
            // Search filter
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("Search", ref _tagSearchFilter, 256);
            ImGui.SameLine();
            if (ImGui.Button("Clear")) _tagSearchFilter = "";

            ImGui.Separator();

            if (ImGui.BeginChild("TagCategories"))
            {
                DrawTagCategory(dataset, "Format", new[]
                {
                    GISTag.Shapefile, GISTag.GeoJSON, GISTag.KML, GISTag.KMZ,
                    GISTag.GeoTIFF, GISTag.GeoPackage, GISTag.FileGDB
                });

                DrawTagCategory(dataset, "Geometry", new[]
                {
                    GISTag.VectorData, GISTag.RasterData, GISTag.PointCloud, GISTag.TIN
                });

                DrawTagCategory(dataset, "Purpose", new[]
                {
                    GISTag.Topography, GISTag.Basemap, GISTag.LandRegister, GISTag.Cadastral,
                    GISTag.Satellite, GISTag.Aerial, GISTag.Geological, GISTag.GeologicalMap,
                    GISTag.StructuralData, GISTag.Geophysical, GISTag.Administrative,
                    GISTag.Infrastructure, GISTag.Hydrography, GISTag.Vegetation,
                    GISTag.LandUse, GISTag.Bathymetry, GISTag.Seismic
                });

                DrawTagCategory(dataset, "Analysis", new[]
                {
                    GISTag.DEM, GISTag.DSM, GISTag.DTM, GISTag.Slope, GISTag.Aspect,
                    GISTag.Hillshade, GISTag.Contours, GISTag.Watershed, GISTag.FlowDirection
                });

                DrawTagCategory(dataset, "Properties", new[]
                {
                    GISTag.Georeferenced, GISTag.Projected, GISTag.MultiLayer, GISTag.Editable,
                    GISTag.Cached, GISTag.Validated, GISTag.Cleaned, GISTag.Attributed,
                    GISTag.Styled, GISTag.TimeSeries, GISTag.Multispectral, GISTag.ThreeDimensional
                });

                DrawTagCategory(dataset, "Source", new[]
                {
                    GISTag.Survey, GISTag.RemoteSensing, GISTag.Generated, GISTag.Imported,
                    GISTag.OpenData, GISTag.Commercial, GISTag.FieldData, GISTag.LiDAR,
                    GISTag.UAV, GISTag.GPS
                });
            }

            ImGui.EndChild();

            ImGui.End();
        }
    }

    private void DrawTagCategory(GISDataset dataset, string categoryName, GISTag[] tags)
    {
        if (!_tagCategoryExpanded.ContainsKey(categoryName))
            _tagCategoryExpanded[categoryName] = false;

        var isExpanded = _tagCategoryExpanded[categoryName];

        if (ImGui.CollapsingHeader(categoryName, ref isExpanded))
        {
            _tagCategoryExpanded[categoryName] = true;

            foreach (var tag in tags)
            {
                var displayName = tag.GetDisplayName();

                // Apply search filter
                if (!string.IsNullOrEmpty(_tagSearchFilter) &&
                    !displayName.Contains(_tagSearchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var hasTag = dataset.HasTag(tag);
                if (ImGui.Checkbox($"##{tag}", ref hasTag))
                {
                    if (hasTag)
                        dataset.AddTag(tag);
                    else
                        dataset.RemoveTag(tag);
                }

                ImGui.SameLine();
                ImGui.Text(displayName);

                if (ImGui.IsItemHovered())
                {
                    var desc = tag.GetCategoryDescription();
                    if (!string.IsNullOrEmpty(desc))
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextWrapped(desc);
                        ImGui.EndTooltip();
                    }
                }
            }

            ImGui.Spacing();
        }
        else
        {
            _tagCategoryExpanded[categoryName] = false;
        }
    }

    private Vector4 GetTagColor(GISTag tag)
    {
        if (tag.IsFormatTag())
            return new Vector4(0.5f, 0.8f, 1.0f, 1.0f);

        if (tag.IsGeometryTypeTag())
            return new Vector4(0.8f, 0.5f, 1.0f, 1.0f);

        if (tag.IsAnalysisTag())
            return new Vector4(1.0f, 0.8f, 0.3f, 1.0f);

        if (tag.IsSourceTag())
            return new Vector4(0.5f, 1.0f, 0.5f, 1.0f);

        return new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
    }

    private void DrawOperations(GISDataset dataset)
    {
        var operations = dataset.GetAvailableOperations();

        if (operations.Length == 0)
        {
            ImGui.TextDisabled("No specialized operations available");
            ImGui.TextWrapped("Add tags to unlock specialized operations for this dataset.");
            return;
        }

        ImGui.TextWrapped($"Available operations based on tags ({operations.Length} total):");
        ImGui.Spacing();

        var categories = new Dictionary<string, List<string>>
        {
            { "Terrain Analysis", new List<string>() }, { "Vector Operations", new List<string>() },
            { "Raster Operations", new List<string>() },
            { "Analysis", new List<string>() }, { "Export/Transform", new List<string>() },
            { "Other", new List<string>() }
        };

        foreach (var op in operations)
            if (op.Contains("Slope") || op.Contains("Aspect") || op.Contains("Hillshade") || op.Contains("Terrain") ||
                op.Contains("Elevation")) categories["Terrain Analysis"].Add(op);
            else if (op.Contains("Buffer") || op.Contains("Clip") || op.Contains("Intersect") || op.Contains("Union") ||
                     op.Contains("Dissolve")) categories["Vector Operations"].Add(op);
            else if (op.Contains("Raster") || op.Contains("Resample") || op.Contains("Mosaic"))
                categories["Raster Operations"].Add(op);
            else if (op.Contains("Analysis") || op.Contains("Statistics") || op.Contains("Calculation"))
                categories["Analysis"].Add(op);
            else if (op.Contains("Export") || op.Contains("Transform") || op.Contains("Convert"))
                categories["Export/Transform"].Add(op);
            else categories["Other"].Add(op);

        foreach (var category in categories.Where(c => c.Value.Any()))
            if (ImGui.TreeNode(category.Key))
            {
                foreach (var operation in category.Value)
                {
                    if (ImGui.Button($"{operation}##op")) ExecuteOperation(dataset, operation);

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"Execute: {operation}");
                        if (operation is "Slope Analysis" or "Aspect Analysis" or "Hillshade")
                            ImGui.TextDisabled("Note: Requires a DEM loaded as raster data, not just a basemap.");
                        ImGui.EndTooltip();
                    }
                }

                ImGui.TreePop();
            }

        if (_showBufferPopup)
        {
            ImGui.OpenPopup("Buffer Operation");
            if (ImGui.BeginPopupModal("Buffer Operation", ref _showBufferPopup, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Enter buffer distance (in map units):");
                ImGui.SetNextItemWidth(150);
                ImGui.InputFloat("##bufferdist", ref _bufferDistance);

                if (ImGui.Button("Apply Buffer"))
                {
                    PerformBufferOperation(dataset, _bufferDistance);
                    _showBufferPopup = false;
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel")) _showBufferPopup = false;
                ImGui.EndPopup();
            }
        }
    }

    private void DrawLayerManager(GISDataset dataset)
    {
        ImGui.Text($"Layers: {dataset.Layers.Count}");
        ImGui.Separator();

        // Layer list
        for (var i = 0; i < dataset.Layers.Count; i++)
        {
            var layer = dataset.Layers[i];
            ImGui.PushID(i);

            // Visibility checkbox
            var visible = layer.IsVisible;
            if (ImGui.Checkbox("##Visible", ref visible))
                layer.IsVisible = visible;

            ImGui.SameLine();

            // Layer name
            if (ImGui.TreeNode(layer.Name))
            {
                ImGui.Text($"Type: {layer.Type}");
                ImGui.Text($"Features: {layer.Features.Count}");

                if (layer.Type == LayerType.Vector)
                {
                    // Layer color
                    var color = layer.Color;
                    if (ImGui.ColorEdit4("Color", ref color))
                        layer.Color = color;

                    // Line width
                    var lineWidth = layer.LineWidth;
                    if (ImGui.SliderFloat("Line Width", ref lineWidth, 0.5f, 10.0f))
                        layer.LineWidth = lineWidth;

                    // Point size
                    var pointSize = layer.PointSize;
                    if (ImGui.SliderFloat("Point Size", ref pointSize, 1.0f, 20.0f))
                        layer.PointSize = pointSize;

                    // Editable
                    var editable = layer.IsEditable;
                    if (ImGui.Checkbox("Editable", ref editable))
                        layer.IsEditable = editable;
                }

                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        ImGui.Separator();

        // Add new layer
        ImGui.Text("Add Layer:");
        ImGui.SetNextItemWidth(150);
        ImGui.InputText("##NewLayerName", ref _newLayerName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add"))
        {
            var newLayer = new GISLayer
            {
                Name = _newLayerName,
                Type = LayerType.Vector,
                IsVisible = true,
                IsEditable = true,
                Color = new Vector4(
                    Random.Shared.NextSingle(),
                    Random.Shared.NextSingle(),
                    Random.Shared.NextSingle(),
                    1.0f)
            };
            dataset.Layers.Add(newLayer);
            _newLayerName = $"Layer {dataset.Layers.Count}";
            Logger.Log($"Added new layer: {newLayer.Name}");
        }
    }

    private void DrawCreateFromMetadata(GISDataset dataset)
    {
        ImGui.TextWrapped("Create a new layer with points from dataset metadata coordinates.");
        ImGui.Spacing();

        // Count datasets with coordinates
        var datasetsWithCoords = ProjectManager.Instance.LoadedDatasets
            .Where(d => d.DatasetMetadata?.Latitude != null && d.DatasetMetadata?.Longitude != null)
            .ToList();

        ImGui.Text($"Datasets with coordinates: {datasetsWithCoords.Count}");

        if (datasetsWithCoords.Count == 0)
        {
            ImGui.TextDisabled("No datasets have coordinate metadata.");
        }
        else
        {
            if (ImGui.Button("Create Sample Points Layer"))
            {
                var layer = dataset.CreateLayerFromMetadata(datasetsWithCoords);
                dataset.Layers.Add(layer);
                dataset.AddTag(GISTag.FieldData);
                dataset.AddTag(GISTag.GPS);
                Logger.Log($"Created layer with {layer.Features.Count} sample points");
            }

            ImGui.Spacing();
            ImGui.Text("Preview:");
            ImGui.Separator();

            if (ImGui.BeginChild("MetadataPreview", new Vector2(0, 150), ImGuiChildFlags.Border))
            {
                foreach (var ds in datasetsWithCoords.Take(5))
                {
                    var meta = ds.DatasetMetadata;
                    ImGui.BulletText($"{ds.Name}");
                    ImGui.Indent();
                    ImGui.Text($"Lat: {meta.Latitude:F6}, Lon: {meta.Longitude:F6}");
                    if (!string.IsNullOrEmpty(meta.LocationName))
                        ImGui.Text($"Location: {meta.LocationName}");
                    ImGui.Unindent();
                }

                if (datasetsWithCoords.Count > 5)
                    ImGui.TextDisabled($"... and {datasetsWithCoords.Count - 5} more");
            }

            ImGui.EndChild();
        }
    }

    private void DrawProjectionInfo(GISDataset dataset)
    {
        ImGui.Text($"Projection: {dataset.Projection.Name}");
        ImGui.Text($"EPSG: {dataset.Projection.EPSG}");
        ImGui.Text($"Type: {dataset.Projection.Type}");

        ImGui.Spacing();
        ImGui.Text("Bounds:");
        ImGui.Text($"Min: ({dataset.Bounds.Min.X:F6}, {dataset.Bounds.Min.Y:F6})");
        ImGui.Text($"Max: ({dataset.Bounds.Max.X:F6}, {dataset.Bounds.Max.Y:F6})");
        ImGui.Text($"Center: ({dataset.Center.X:F6}, {dataset.Center.Y:F6})");
    }
}