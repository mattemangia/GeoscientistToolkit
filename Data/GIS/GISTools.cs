// GeoscientistToolkit/UI/GIS/GISTools.cs

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.GIS.Tools;
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
                    },
                    new()
                    {
                        Name = "Raster Calculator",
                        Description = "Perform mathematical operations on raster layers using expressions.",
                        Tool = new RasterCalculatorTool()
                    },
                    new()
                    {
                        Name = "Georeference Raster",
                        Description = "Re-georeference or georeference raster layers with ground control points.",
                        Tool = new GeoreferenceTool()
                    },
                    new()
                    {
                        Name = "Hydrological Analysis",
                        Description = "GPU-accelerated rainfall simulation, River Runner, and water body tracking.",
                        Tool = new HydrologicalAnalysisToolEnhanced()
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
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
            if (ImGui.Button("Clear Basemap"))
            {
                gisDataset.ActiveBasemapLayerName = null;
            }
            ImGui.Separator();

            for (var i = 0; i < gisDataset.Layers.Count; i++)
            {
                var layer = gisDataset.Layers[i];
                ImGui.PushID(i);

                var isVisible = layer.IsVisible;
                if (ImGui.Checkbox($"##Vis{i}", ref isVisible))
                {
                    layer.IsVisible = isVisible;
                }
                ImGui.SameLine();
                if (layer.Type == LayerType.Raster)
                {
                    ImGui.Text("[R]");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Raster Layer");
                }
                else
                {
                    ImGui.Text("[V]");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Vector Layer");
                }

                ImGui.SameLine();
                ImGui.Text(layer.Name);

                ImGui.SameLine();
                var regionAvail = ImGui.GetContentRegionAvail().X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + regionAvail - 120);
                if (ImGui.SmallButton("Set as Basemap") && layer is GISRasterLayer)
                {
                    gisDataset.SetLayerAsBasemap(layer.Name);
                }

                ImGui.PopID();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Add New Layer:");
            ImGui.InputText("##NewLayerName", ref _newLayerName, 128);
            ImGui.SameLine();
            if (ImGui.Button("Add Vector Layer"))
            {
                var newLayer = new GISLayer
                {
                    Name = _newLayerName,
                    Type = LayerType.Vector,
                    IsVisible = true,
                    IsEditable = true,
                    Color = new Vector4(0.2f, 0.5f, 1.0f, 1.0f)
                };
                gisDataset.Layers.Add(newLayer);
                _newLayerName = "New Layer";
            }
        }
    }

    private class TagManagerTool : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;

            ImGui.Text("Current Tags:");
            ImGui.Separator();

            var currentTags = gisDataset.Tags.GetFlags().ToList();
            if (currentTags.Count == 0)
            {
                ImGui.TextDisabled("No tags assigned");
            }
            else
            {
                foreach (var tag in currentTags)
                {
                    ImGui.BulletText(tag.GetDisplayName());
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 80);
                    if (ImGui.SmallButton($"Remove##{tag}"))
                    {
                        gisDataset.Tags &= ~tag;
                        ProjectManager.Instance.HasUnsavedChanges = true;
                    }
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Add Tags:");

            if (ImGui.TreeNode("Format Tags"))
            {
                DrawTagCheckbox(gisDataset, GISTag.Shapefile);
                DrawTagCheckbox(gisDataset, GISTag.GeoJSON);
                DrawTagCheckbox(gisDataset, GISTag.KML);
                DrawTagCheckbox(gisDataset, GISTag.GeoTIFF);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Geometry Types"))
            {
                DrawTagCheckbox(gisDataset, GISTag.VectorData);
                DrawTagCheckbox(gisDataset, GISTag.RasterData);
                DrawTagCheckbox(gisDataset, GISTag.PointCloud);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Content Types"))
            {
                DrawTagCheckbox(gisDataset, GISTag.Topography);
                DrawTagCheckbox(gisDataset, GISTag.Geological);
                DrawTagCheckbox(gisDataset, GISTag.Satellite);
                DrawTagCheckbox(gisDataset, GISTag.Hydrography);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Analysis Types"))
            {
                DrawTagCheckbox(gisDataset, GISTag.DEM);
                DrawTagCheckbox(gisDataset, GISTag.Slope);
                DrawTagCheckbox(gisDataset, GISTag.Aspect);
                DrawTagCheckbox(gisDataset, GISTag.Hillshade);
                ImGui.TreePop();
            }
        }

        private void DrawTagCheckbox(GISDataset dataset, GISTag tag)
        {
            var hasTag = dataset.Tags.HasFlag(tag);
            if (ImGui.Checkbox(tag.GetDisplayName(), ref hasTag))
            {
                if (hasTag)
                    dataset.Tags |= tag;
                else
                    dataset.Tags &= ~tag;
                ProjectManager.Instance.HasUnsavedChanges = true;
            }
        }
    }

    private class ProjectionInfoTool : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;

            ImGui.Text("Projection Information:");
            ImGui.Separator();

            ImGui.Text($"EPSG: {gisDataset.Projection.EPSG}");
            ImGui.Text($"Name: {gisDataset.Projection.Name}");
            ImGui.Text($"Type: {gisDataset.Projection.Type}");

            ImGui.Spacing();
            ImGui.Text("Bounding Box:");
            ImGui.Separator();

            ImGui.Text($"Min: ({gisDataset.Bounds.Min.X:F6}, {gisDataset.Bounds.Min.Y:F6})");
            ImGui.Text($"Max: ({gisDataset.Bounds.Max.X:F6}, {gisDataset.Bounds.Max.Y:F6})");
            ImGui.Text($"Width: {gisDataset.Bounds.Width:F6}");
            ImGui.Text($"Height: {gisDataset.Bounds.Height:F6}");
            ImGui.Text($"Center: ({gisDataset.Bounds.Center.X:F6}, {gisDataset.Bounds.Center.Y:F6})");
        }
    }

    private class OperationsTool : IDatasetTools
    {
        private float _bufferDistance = 100.0f;

        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;

            ImGui.TextWrapped("Perform spatial operations based on dataset tags.");
            ImGui.Separator();
            ImGui.Spacing();

            // Buffer operation (available for vector data)
            if (gisDataset.HasTag(GISTag.VectorData))
            {
                ImGui.Text("Buffer Operation:");
                ImGui.SetNextItemWidth(200);
                ImGui.InputFloat("Distance", ref _bufferDistance, 1.0f, 10.0f);

                if (ImGui.Button("Create Buffer"))
                {
                    PerformBuffer(gisDataset, _bufferDistance);
                }

                ImGui.Spacing();
            }

            // Show available operations based on tags
            ImGui.Text("Available Operations:");
            var operations = gisDataset.Tags.GetAvailableOperations();
            foreach (var op in operations.Take(10))
            {
                ImGui.BulletText(op);
            }

            if (operations.Length > 10)
            {
                ImGui.TextDisabled($"... and {operations.Length - 10} more");
            }
        }

        private void PerformBuffer(GISDataset dataset, float distance)
        {
            try
            {
                var firstVectorLayer = dataset.Layers.FirstOrDefault(l => l.Type == LayerType.Vector);
                if (firstVectorLayer == null || firstVectorLayer.Features.Count == 0)
                {
                    Logger.LogWarning("No vector features to buffer.");
                    return;
                }

                var bufferedFeatures = new List<GISFeature>();
                foreach (var feature in firstVectorLayer.Features)
                {
                    var ntsGeometry = dataset.ConvertToNTSGeometry(feature);
                    if (ntsGeometry == null) continue;

                    var bufferedGeometry = GISOperationsImpl.BufferGeometry(ntsGeometry, distance);
                    if (bufferedGeometry == null || bufferedGeometry.IsEmpty) continue;

                    var bufferedFeature = GISDataset.ConvertNTSGeometry(bufferedGeometry, feature.Properties);
                    if (bufferedFeature != null)
                    {
                        bufferedFeatures.Add(bufferedFeature);
                    }
                }
                
                var newLayer = new GISLayer
                {
                    Features = bufferedFeatures,
                    Type = LayerType.Vector,
                    IsVisible = true,
                    Color = new Vector4(0.8f, 0.2f, 0.8f, 1.0f) 
                };
                
                newLayer.Name = $"{firstVectorLayer.Name}_Buffer_{distance}m";

                if (newLayer.Features.Count == 0)
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
                Logger.Log($"Exported to GeoJSON: {_geoJsonExportDialog.SelectedPath.ToString()}");
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
            Logger.Log($"Exporting attributes for layer '{layer.Name}' to CSV: {path.ToString()}");
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
                Logger.Log($"Successfully exported {layer.Features.Count.ToString()} records to {path.ToString()}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export layer attributes to CSV: {ex.Message.ToString()}");
            }
        }

        private void StartExportTask(Task exportTask)
        {
            _exportProgressDialog.Open("Starting export...");
            _exportCts = new CancellationTokenSource();
            exportTask.ContinueWith(t =>
            {
                if (t.IsCanceled) Logger.Log("Export was canceled.");
                else if (t.Exception != null) Logger.LogError($"Export failed: {t.Exception.InnerException?.Message?.ToString()}");
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