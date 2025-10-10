// GeoscientistToolkit/UI/GIS/GISTools.cs

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

/// <summary>
///     Categorized tool panel for GIS datasets.
///     Uses a compact dropdown + tabs navigation to organize GIS operations.
/// </summary>
public class GISTools : IDatasetTools
{
    private readonly Dictionary<ToolCategory, string> _categoryDescriptions;
    private readonly Dictionary<ToolCategory, string> _categoryNames;

    // The export tool is held separately to allow its dialogs to be processed every frame.
    private readonly ExportTool _exportTool;

    // All tools organized by category
    private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;
    private ToolCategory _selectedCategory = ToolCategory.Layers; // Default category
    private int _selectedToolIndex;

    public GISTools()
    {
        _exportTool = new ExportTool();

        // Category metadata
        _categoryNames = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Scripting, "Scripting" },
            { ToolCategory.Layers, "Layers" },
            { ToolCategory.Properties, "Properties & Tags" },
            { ToolCategory.Operations, "Operations" },
            { ToolCategory.Export, "Export" }
        };

        _categoryDescriptions = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Scripting, "Automate tasks with the GeoScript Editor" },
            { ToolCategory.Layers, "Manage and style vector and raster layers" },
            { ToolCategory.Properties, "View projection info and manage descriptive tags" },
            { ToolCategory.Operations, "Perform spatial analysis and generate new layers" },
            { ToolCategory.Export, "Save data to GIS formats like Shapefile or GeoTIFF" }
        };

        // Initialize tools by category
        _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
        {
            {
                ToolCategory.Scripting, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "GeoScript Editor", Description = "Write and execute scripts to process GIS data.",
                        Tool = new GeoScriptEditorTool()
                    }
                }
            },
            {
                ToolCategory.Layers, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Layer Manager", Description = "Control layer visibility, styling, and properties.",
                        Tool = new LayerManagerTool()
                    }
                }
            },
            {
                ToolCategory.Properties, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Tag Management",
                        Description = "Assign categorical tags to enable specialized operations.",
                        Tool = new TagManagerTool()
                    },
                    new()
                    {
                        Name = "Projection Info", Description = "View the dataset's coordinate system and bounds.",
                        Tool = new ProjectionInfoTool()
                    }
                }
            },
            {
                ToolCategory.Operations, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Spatial Operations",
                        Description = "Perform spatial operations like buffering based on tags.",
                        Tool = new OperationsTool()
                    },
                    new()
                    {
                        Name = "Create From Metadata",
                        Description = "Create a new point layer from other datasets' coordinates.",
                        Tool = new CreateFromMetadataTool()
                    }
                }
            },
            {
                ToolCategory.Export, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Export Data",
                        Description = "Export the dataset or individual layers to various file formats.",
                        Tool = _exportTool
                    }
                }
            }
        };
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not GISDataset gisDataset)
        {
            ImGui.TextDisabled("GIS tools are only available for GIS datasets.");
            return;
        }

        // Handle export dialog submissions every frame, regardless of the active tab.
        _exportTool.HandleDialogSubmissions(gisDataset);

        DrawCompactUI(gisDataset);
    }

    private void DrawCompactUI(GISDataset gisDataset)
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
                    entry.Tool.Draw(gisDataset);
                    ImGui.EndChild();

                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
    }

    // --- TOOL CATEGORIES & DEFINITIONS ---
    private enum ToolCategory
    {
        Scripting,
        Layers,
        Properties,
        Operations,
        Export
    }

    private class ToolEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public IDatasetTools Tool { get; set; }
    }

    // --- NESTED TOOL CLASSES ---

    private class GeoScriptEditorTool : IDatasetTools
    {
        private readonly GeoScriptEditor _geoScriptEditor = new();

        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            _geoScriptEditor.SetAssociatedDataset(gisDataset);
            _geoScriptEditor.Draw();
        }
    }

    private class LayerManagerTool : IDatasetTools
    {
        private string _newLayerName = "New Layer";

        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            ImGui.Text($"Layers: {gisDataset.Layers.Count}");
            ImGui.Separator();
            for (var i = 0; i < gisDataset.Layers.Count; i++)
            {
                var layer = gisDataset.Layers[i];
                ImGui.PushID(i);
                var visible = layer.IsVisible;
                if (ImGui.Checkbox("##Visible", ref visible)) layer.IsVisible = visible;
                ImGui.SameLine();
                if (ImGui.TreeNode(layer.Name))
                {
                    ImGui.Text($"Type: {layer.Type}");
                    ImGui.Text($"Features: {layer.Features.Count}");
                    if (layer.Type == LayerType.Vector)
                    {
                        var color = layer.Color;
                        if (ImGui.ColorEdit4("Color", ref color)) layer.Color = color;
                        var lineWidth = layer.LineWidth;
                        if (ImGui.SliderFloat("Line Width", ref lineWidth, 0.5f, 10.0f)) layer.LineWidth = lineWidth;
                        var pointSize = layer.PointSize;
                        if (ImGui.SliderFloat("Point Size", ref pointSize, 1.0f, 20.0f)) layer.PointSize = pointSize;
                        var editable = layer.IsEditable;
                        if (ImGui.Checkbox("Editable", ref editable)) layer.IsEditable = editable;
                    }

                    ImGui.TreePop();
                }

                ImGui.PopID();
            }

            ImGui.Separator();
            ImGui.Text("Add Layer:");
            ImGui.SetNextItemWidth(150);
            ImGui.InputText("##NewLayerName", ref _newLayerName, 64);
            ImGui.SameLine();
            if (ImGui.Button("Add"))
            {
                var newLayer = new GISLayer
                {
                    Name = _newLayerName, Type = LayerType.Vector, IsVisible = true, IsEditable = true,
                    Color = new Vector4(Random.Shared.NextSingle(), Random.Shared.NextSingle(),
                        Random.Shared.NextSingle(), 1.0f)
                };
                gisDataset.Layers.Add(newLayer);
                _newLayerName = $"Layer {gisDataset.Layers.Count + 1}";
                Logger.Log($"Added new layer: {newLayer.Name}");
            }
        }
    }

    private class TagManagerTool : IDatasetTools
    {
        private readonly Dictionary<string, bool> _tagCategoryExpanded =
            new() { { "Format", true }, { "Geometry", true }, { "Purpose", true } };

        private bool _showTagPicker;
        private string _tagSearchFilter = "";

        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            ImGui.Text("Active Tags:");
            if (gisDataset.Tags == GISTag.None)
            {
                ImGui.TextDisabled("No tags assigned");
            }
            else
            {
                var activeTags = gisDataset.Tags.GetFlags().ToList();
                var toRemove = new List<GISTag>();
                foreach (var tag in activeTags)
                {
                    ImGui.PushID(tag.GetHashCode());
                    ImGui.PushStyleColor(ImGuiCol.Button, GetTagColor(tag));
                    if (ImGui.Button($"{tag.GetDisplayName()} ×")) toRemove.Add(tag);
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(tag.GetCategoryDescription());
                    ImGui.SameLine();
                    ImGui.PopID();
                }

                ImGui.NewLine();
                foreach (var tag in toRemove) gisDataset.RemoveTag(tag);
            }

            ImGui.Separator();
            if (ImGui.Button("Add Tags...")) _showTagPicker = true;
            ImGui.SameLine();
            if (ImGui.Button("Auto-Detect Tags")) AutoDetectTags(gisDataset);
            ImGui.SameLine();
            if (ImGui.Button("Clear All Tags")) gisDataset.ClearTags();
            if (_showTagPicker) DrawTagPickerWindow(gisDataset);
        }

        private void DrawTagPickerWindow(GISDataset dataset)
        {
            ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Add Tags", ref _showTagPicker))
            {
                ImGui.InputText("Search", ref _tagSearchFilter, 256);
                ImGui.Separator();
                if (ImGui.BeginChild("TagCategories"))
                {
                    DrawTagCategory(dataset, "Format",
                        new[]
                        {
                            GISTag.Shapefile, GISTag.GeoJSON, GISTag.KML, GISTag.KMZ, GISTag.GeoTIFF, GISTag.GeoPackage,
                            GISTag.FileGDB
                        });
                    DrawTagCategory(dataset, "Geometry",
                        new[] { GISTag.VectorData, GISTag.RasterData, GISTag.PointCloud, GISTag.TIN });
                    DrawTagCategory(dataset, "Purpose",
                        new[]
                        {
                            GISTag.Topography, GISTag.Basemap, GISTag.LandRegister, GISTag.Cadastral, GISTag.Satellite,
                            GISTag.Aerial, GISTag.Geological, GISTag.GeologicalMap, GISTag.StructuralData,
                            GISTag.Geophysical, GISTag.Administrative, GISTag.Infrastructure, GISTag.Hydrography,
                            GISTag.Vegetation, GISTag.LandUse, GISTag.Bathymetry, GISTag.Seismic
                        });
                    DrawTagCategory(dataset, "Analysis",
                        new[]
                        {
                            GISTag.DEM, GISTag.DSM, GISTag.DTM, GISTag.Slope, GISTag.Aspect, GISTag.Hillshade,
                            GISTag.Contours, GISTag.Watershed, GISTag.FlowDirection
                        });
                    DrawTagCategory(dataset, "Properties",
                        new[]
                        {
                            GISTag.Georeferenced, GISTag.Projected, GISTag.MultiLayer, GISTag.Editable, GISTag.Cached,
                            GISTag.Validated, GISTag.Cleaned, GISTag.Attributed, GISTag.Styled, GISTag.TimeSeries,
                            GISTag.Multispectral, GISTag.ThreeDimensional
                        });
                    DrawTagCategory(dataset, "Source",
                        new[]
                        {
                            GISTag.Survey, GISTag.RemoteSensing, GISTag.Generated, GISTag.Imported, GISTag.OpenData,
                            GISTag.Commercial, GISTag.FieldData, GISTag.LiDAR, GISTag.UAV, GISTag.GPS
                        });
                }

                ImGui.EndChild();
                ImGui.End();
            }
        }

        private void DrawTagCategory(GISDataset dataset, string categoryName, GISTag[] tags)
        {
            if (!_tagCategoryExpanded.TryGetValue(categoryName, out var isExpanded)) isExpanded = false;
            if (ImGui.CollapsingHeader(categoryName, ref isExpanded))
            {
                _tagCategoryExpanded[categoryName] = true;
                foreach (var tag in tags)
                {
                    if (!string.IsNullOrEmpty(_tagSearchFilter) && !tag.GetDisplayName()
                            .Contains(_tagSearchFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    var hasTag = dataset.HasTag(tag);
                    if (ImGui.Checkbox($"##{tag}", ref hasTag))
                    {
                        if (hasTag) dataset.AddTag(tag);
                        else dataset.RemoveTag(tag);
                    }

                    ImGui.SameLine();
                    ImGui.Text(tag.GetDisplayName());
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(tag.GetCategoryDescription());
                }
            }
            else
            {
                _tagCategoryExpanded[categoryName] = false;
            }
        }

        private void AutoDetectTags(GISDataset dataset)
        {
            var recommended = GISTagExtensions.GetRecommendedTags(dataset.FilePath ?? "",
                dataset.Layers.FirstOrDefault()?.Type ?? LayerType.Vector);
            var addedCount = 0;

            // Correctly iterate and add tags
            foreach (var tag in recommended)
                if (!dataset.HasTag(tag))
                {
                    dataset.AddTag(tag);
                    addedCount++;
                }

            if (dataset.Layers.Any(l => l.Features.Any(f => f.Properties.Count > 0)) &&
                !dataset.HasTag(GISTag.Attributed))
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

        private Vector4 GetTagColor(GISTag tag)
        {
            if (tag.IsFormatTag()) return new Vector4(0.5f, 0.8f, 1.0f, 1.0f);
            if (tag.IsGeometryTypeTag()) return new Vector4(0.8f, 0.5f, 1.0f, 1.0f);
            if (tag.IsAnalysisTag()) return new Vector4(1.0f, 0.8f, 0.3f, 1.0f);
            if (tag.IsSourceTag()) return new Vector4(0.5f, 1.0f, 0.5f, 1.0f);
            return new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
        }
    }

    private class ProjectionInfoTool : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            ImGui.Text($"Projection: {gisDataset.Projection.Name}");
            ImGui.Text($"EPSG: {gisDataset.Projection.EPSG}");
            ImGui.Text($"Type: {gisDataset.Projection.Type}");
            ImGui.Spacing();
            ImGui.Text("Bounds:");
            ImGui.Text($"Min: ({gisDataset.Bounds.Min.X:F6}, {gisDataset.Bounds.Min.Y:F6})");
            ImGui.Text($"Max: ({gisDataset.Bounds.Max.X:F6}, {gisDataset.Bounds.Max.Y:F6})");
            ImGui.Text($"Center: ({gisDataset.Center.X:F6}, {gisDataset.Center.Y:F6})");
        }
    }

    private class OperationsTool : IDatasetTools
    {
        private float _bufferDistance = 10.0f;
        private bool _showBufferPopup;

        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            var operations = gisDataset.GetAvailableOperations();
            if (operations.Length == 0)
            {
                ImGui.TextDisabled("Add tags to unlock specialized operations.");
                return;
            }

            var categories = new Dictionary<string, List<string>>
            {
                { "Terrain Analysis", new List<string>() }, { "Vector Operations", new List<string>() },
                { "Other", new List<string>() }
            };
            foreach (var op in operations)
                if (op.Contains("Slope") || op.Contains("Aspect") || op.Contains("Hillshade"))
                    categories["Terrain Analysis"].Add(op);
                else if (op.Contains("Buffer") || op.Contains("Clip") || op.Contains("Intersect"))
                    categories["Vector Operations"].Add(op);
                else categories["Other"].Add(op);
            foreach (var category in categories.Where(c => c.Value.Any()))
                if (ImGui.TreeNode(category.Key))
                {
                    foreach (var operation in category.Value)
                    {
                        if (ImGui.Button(operation)) ExecuteOperation(gisDataset, operation);
                        if (ImGui.IsItemHovered() && (operation.Contains("Slope") || operation.Contains("Aspect") ||
                                                      operation.Contains("Hillshade")))
                            ImGui.SetTooltip("Note: Requires a DEM loaded as raster data.");
                    }

                    ImGui.TreePop();
                }

            if (_showBufferPopup)
            {
                ImGui.OpenPopup("Buffer Operation");
                if (ImGui.BeginPopupModal("Buffer Operation", ref _showBufferPopup, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.InputFloat("Buffer Distance", ref _bufferDistance);
                    if (ImGui.Button("Apply"))
                    {
                        PerformBufferOperation(gisDataset, _bufferDistance);
                        _showBufferPopup = false;
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Cancel")) _showBufferPopup = false;
                    ImGui.EndPopup();
                }
            }
        }

        private void ExecuteOperation(GISDataset dataset, string operationName)
        {
            if (operationName == "Buffer")
            {
                _bufferDistance = 10.0f;
                _showBufferPopup = true;
            }
            else
            {
                Logger.LogWarning($"Operation '{operationName}' is not fully implemented.");
            }
        }

        private void PerformBufferOperation(GISDataset dataset, float distance)
        {
            try
            {
                var newLayer = new GISLayer { Name = $"{dataset.Name} Buffered ({distance})", IsVisible = true };
                foreach (var feature in dataset.Layers.Where(l => l.Type == LayerType.Vector)
                             .SelectMany(l => l.Features))
                {
                    var ntsGeom = dataset.ConvertToNTSGeometry(feature);
                    if (ntsGeom == null) continue;
                    var bufferedGeom = GISOperationsImpl.BufferGeometry(ntsGeom, distance);
                    var newFeature = GISDataset.ConvertNTSGeometry(bufferedGeom, feature.Properties);
                    if (newFeature != null) newLayer.Features.Add(newFeature);
                }

                if (!newLayer.Features.Any())
                {
                    Logger.LogWarning("Buffer operation resulted in no features.");
                    return;
                }

                var newDataset = new GISDataset(newLayer.Name, "")
                    { Tags = dataset.Tags | GISTag.Generated, Projection = dataset.Projection };
                newDataset.Layers.Clear();
                newDataset.Layers.Add(newLayer);
                newDataset.UpdateBounds();
                ProjectManager.Instance.AddDataset(newDataset);
                Logger.Log($"Created buffered dataset '{newDataset.Name}' with {newLayer.Features.Count} features.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to perform buffer operation: {ex.Message}");
            }
        }
    }

    private class CreateFromMetadataTool : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            ImGui.TextWrapped("Create a new point layer from the coordinates in other loaded datasets' metadata.");
            var datasetsWithCoords = ProjectManager.Instance.LoadedDatasets
                .Where(d => d.DatasetMetadata?.Latitude != null && d.DatasetMetadata?.Longitude != null).ToList();
            if (datasetsWithCoords.Count == 0)
            {
                ImGui.TextDisabled("No other datasets have coordinate metadata.");
            }
            else
            {
                ImGui.Text($"Found {datasetsWithCoords.Count} datasets with coordinates.");
                if (ImGui.Button("Create Points Layer"))
                {
                    var layer = gisDataset.CreateLayerFromMetadata(datasetsWithCoords);
                    gisDataset.Layers.Add(layer);
                    gisDataset.AddTag(GISTag.FieldData | GISTag.GPS);
                    Logger.Log($"Created layer '{layer.Name}' with {layer.Features.Count} sample points.");
                }

                ImGui.Spacing();
                ImGui.Text("Preview:");
                if (ImGui.BeginChild("MetadataPreview", new Vector2(0, 150), ImGuiChildFlags.Border))
                {
                    foreach (var ds in datasetsWithCoords.Take(5))
                    {
                        var meta = ds.DatasetMetadata;
                        ImGui.BulletText($"{ds.Name} (Lat: {meta.Latitude:F4}, Lon: {meta.Longitude:F4})");
                    }

                    if (datasetsWithCoords.Count > 5)
                        ImGui.TextDisabled($"... and {datasetsWithCoords.Count - 5} more");
                }

                ImGui.EndChild();
            }
        }
    }

    private class ExportTool : IDatasetTools
    {
        private readonly ImGuiExportFileDialog _csvExportDialog;
        private readonly ProgressBarDialog _exportProgressDialog;
        private readonly ImGuiExportFileDialog _geoJsonExportDialog;
        private readonly ImGuiExportFileDialog _geoTiffExportDialog;
        private readonly ImGuiExportFileDialog _shpExportDialog;
        private CancellationTokenSource _exportCts;
        private int _selectedLayerIndexForExport;

        public ExportTool()
        {
            _shpExportDialog = new ImGuiExportFileDialog("GISShpExport", "Export as Shapefile");
            _shpExportDialog.SetExtensions((".shp", "ESRI Shapefile"));

            _geoJsonExportDialog = new ImGuiExportFileDialog("GISGeoJsonExport", "Export as GeoJSON");
            _geoJsonExportDialog.SetExtensions((".geojson", "GeoJSON"));

            _csvExportDialog = new ImGuiExportFileDialog("GISCsvExport", "Export Attributes as CSV");
            _csvExportDialog.SetExtensions((".csv", "Comma-Separated Values"));

            _geoTiffExportDialog = new ImGuiExportFileDialog("GISGeoTiffExport", "Export as GeoTIFF");
            _geoTiffExportDialog.SetExtensions((".tif", "Tagged Image File Format"));

            _exportProgressDialog = new ProgressBarDialog("Exporting GIS Data");
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            if (ImGui.Button("Export as Shapefile...")) _shpExportDialog.Open(gisDataset.Name);
            if (ImGui.Button("Export as GeoJSON...")) _geoJsonExportDialog.Open(gisDataset.Name);
            if (ImGui.Button("Export as GeoTIFF...")) _geoTiffExportDialog.Open(gisDataset.Name);
            ImGui.Separator();
            ImGui.Text("Export Layer Attributes to CSV:");
            var vectorLayers = gisDataset.Layers.Where(l => l.Type == LayerType.Vector).ToList();
            if (vectorLayers.Any())
            {
                var layerNames = vectorLayers.Select(l => l.Name).ToArray();
                ImGui.Combo("Layer##CSVExport", ref _selectedLayerIndexForExport, layerNames, layerNames.Length);
                ImGui.SameLine();
                if (ImGui.Button("Export CSV..."))
                    _csvExportDialog.Open(
                        $"{gisDataset.Name}_{vectorLayers[_selectedLayerIndexForExport].Name}_attributes");
            }
            else
            {
                ImGui.TextDisabled("No vector layers to export.");
            }
        }

        public void HandleDialogSubmissions(GISDataset dataset)
        {
            _exportProgressDialog.Submit();
            if (_exportProgressDialog.IsCancellationRequested) _exportCts?.Cancel();
            if (_shpExportDialog.Submit())
                StartExportTask(GISExporter.ExportToShapefileAsync(dataset, _shpExportDialog.SelectedPath,
                    CreateProgressHandler(), _exportCts.Token));
            if (_geoTiffExportDialog.Submit())
                StartExportTask(GISExporter.ExportToGeoTiffAsync(dataset, _geoTiffExportDialog.SelectedPath,
                    CreateProgressHandler(), _exportCts.Token));
            if (_geoJsonExportDialog.Submit())
            {
                dataset.SaveAsGeoJSON(_geoJsonExportDialog.SelectedPath);
                Logger.Log($"Exported to GeoJSON: {_geoJsonExportDialog.SelectedPath}");
            }

            if (_csvExportDialog.Submit())
            {
                var vectorLayers = dataset.Layers.Where(l => l.Type == LayerType.Vector).ToList();
                if (vectorLayers.Count > _selectedLayerIndexForExport)
                    SaveLayerAsCsv(vectorLayers[_selectedLayerIndexForExport], _csvExportDialog.SelectedPath);
            }
        }

        private void SaveLayerAsCsv(GISLayer layer, string path)
        {
            Logger.Log($"Exporting attributes for layer '{layer.Name}' to CSV: {path}");
            try
            {
                var headers = layer.Features.SelectMany(f => f.Properties.Keys).Distinct().OrderBy(h => h).ToList();
                var csv = new StringBuilder();
                csv.AppendLine(string.Join(",", headers.Select(h => $"\"{h.Replace("\"", "\"\"")}\"")));
                foreach (var feature in layer.Features)
                {
                    var row = headers.Select(header =>
                    {
                        if (feature.Properties.TryGetValue(header, out var value) && value != null)
                        {
                            var cellValue = value.ToString().Replace("\"", "\"\"");
                            return cellValue.Contains(',') || cellValue.Contains('"') ? $"\"{cellValue}\"" : cellValue;
                        }

                        return "";
                    });
                    csv.AppendLine(string.Join(",", row));
                }

                File.WriteAllText(path, csv.ToString());
                Logger.Log($"Successfully exported {layer.Features.Count} records to {path}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export layer attributes to CSV: {ex.Message}");
            }
        }

        private void StartExportTask(Task exportTask)
        {
            _exportProgressDialog.Open("Starting export...");
            _exportCts = new CancellationTokenSource();
            exportTask.ContinueWith(t =>
            {
                if (t.IsCanceled) Logger.Log("Export was canceled.");
                else if (t.Exception != null) Logger.LogError($"Export failed: {t.Exception.InnerException?.Message}");
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
    }
}