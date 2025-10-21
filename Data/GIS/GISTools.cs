// GeoscientistToolkit/UI/GIS/GISTools.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Business.GIS.RasterTools;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
///     Categorized tool panel for GIS datasets, using a compact dropdown and tab navigation.
/// </summary>
public class GISTools : IDatasetTools
{
    private readonly Dictionary<ToolCategory, string> _categoryDescriptions;
    private readonly Dictionary<ToolCategory, string> _categoryNames;
    private readonly ExportTool _exportTool;
    private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;
    private ToolCategory _selectedCategory = ToolCategory.Layers;
    private int _selectedToolIndex;

    public GISTools()
    {
        _exportTool = new ExportTool();

        _categoryNames = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Layers, "Layers & Symbology" },
            { ToolCategory.Properties, "Properties & Tags" },
            { ToolCategory.VectorOperations, "Vector Operations" },
            { ToolCategory.RasterAnalysis, "Raster Analysis" },
            { ToolCategory.Export, "Export" }
        };

        _categoryDescriptions = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Layers, "Manage and style vector and raster layers." },
            { ToolCategory.Properties, "View projection info and manage descriptive tags." },
            { ToolCategory.VectorOperations, "Perform spatial analysis on vector features." },
            { ToolCategory.RasterAnalysis, "Analyze raster data to derive new information." },
            { ToolCategory.Export, "Save data to standard GIS formats." }
        };

        _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
        {
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
                ToolCategory.VectorOperations, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Spatial Operations",
                        Description = "Perform spatial operations like buffering based on tags.",
                        Tool = new VectorOperationsTool()
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
                ToolCategory.RasterAnalysis, new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Raster Calculator", Description = "Perform map algebra on raster layers.",
                        Tool = new RasterCalculatorTool()
                    },
                    new()
                    {
                        Name = "Isolines Creator", Description = "Generate contour lines from raster data.",
                        Tool = new IsolinesCreatorTool()
                    },
                    new()
                    {
                        Name = "TIN Creator", Description = "Generate a Triangulated Irregular Network.",
                        Tool = new TINCreatorTool()
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

        _exportTool.HandleDialogSubmissions(gisDataset);
        DrawCompactUI(gisDataset);
    }

    private void DrawCompactUI(GISDataset gisDataset)
    {
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
                if (ImGui.Selectable($"{_categoryNames[category]} ({tools.Count} tools)",
                        _selectedCategory == category))
                {
                    _selectedCategory = category;
                    _selectedToolIndex = 0;
                }

                if (ImGui.IsItemHovered()) ImGui.SetTooltip(_categoryDescriptions[category]);
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleVar();

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), _categoryDescriptions[_selectedCategory]);
        ImGui.Separator();
        ImGui.Spacing();

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

                    ImGui.BeginChild($"ToolContent_{entry.Name}", default, ImGuiChildFlags.None,
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
        Layers,
        Properties,
        VectorOperations,
        RasterAnalysis,
        Export
    }

    private class ToolEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public IDatasetTools Tool { get; set; }
    }

    // --- NESTED TOOL CLASSES (Implementations from the original GISTools) ---

    private class LayerManagerTool : IDatasetTools
    {
        private string _newLayerName = "New Layer";

        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            ImGui.Text($"Layers: {gisDataset.Layers.Count}");
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
            if (ImGui.Button("Clear Basemap")) gisDataset.ActiveBasemapLayerName = null;
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
                    if (layer.Type == LayerType.Vector)
                    {
                        ImGui.Text($"Features: {layer.Features.Count}");
                        var color = layer.Color;
                        if (ImGui.ColorEdit4("Color", ref color)) layer.Color = color;
                        var lineWidth = layer.LineWidth;
                        if (ImGui.SliderFloat("Line Width", ref lineWidth, 0.5f, 10.0f)) layer.LineWidth = lineWidth;
                        var pointSize = layer.PointSize;
                        if (ImGui.SliderFloat("Point Size", ref pointSize, 1.0f, 20.0f)) layer.PointSize = pointSize;
                        var editable = layer.IsEditable;
                        if (ImGui.Checkbox("Editable", ref editable)) layer.IsEditable = editable;
                    }
                    else if (layer is GISRasterLayer rasterLayer)
                    {
                        ImGui.Text($"Resolution: {rasterLayer.Width}x{rasterLayer.Height}");
                        if (ImGui.Button("Set as Basemap"))
                        {
                            gisDataset.ActiveBasemapLayerName = rasterLayer.Name;
                            Logger.Log($"Set '{rasterLayer.Name}' as active basemap.");
                        }
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
                    if (ImGui.Button($"{tag.GetDisplayName()} ×")) toRemove.Add(tag);
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
            if (_showTagPicker) DrawTagPickerWindow(gisDataset);
        }

        private void DrawTagPickerWindow(GISDataset dataset)
        {
            ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Add Tags to Dataset", ref _showTagPicker))
            {
                ImGui.InputTextWithHint("##TagSearch", "Search tags...", ref _tagSearchFilter, 100);
                ImGui.Separator();

                var allTags = Enum.GetValues<GISTag>().Where(t => t != GISTag.None).ToList();
                var filteredTags = string.IsNullOrWhiteSpace(_tagSearchFilter)
                    ? allTags
                    : allTags.Where(t =>
                        t.GetDisplayName().Contains(_tagSearchFilter, StringComparison.OrdinalIgnoreCase)).ToList();

                var formatTags = filteredTags.Where(t => t.IsFormatTag()).ToList();
                var geometryTags = filteredTags.Where(t => t.IsGeometryTypeTag()).ToList();
                var analysisTags = filteredTags.Where(t => t.IsAnalysisTag()).ToList();
                var sourceTags = filteredTags.Where(t => t.IsSourceTag()).ToList();
                var otherTags = filteredTags.Except(formatTags).Except(geometryTags).Except(analysisTags)
                    .Except(sourceTags).ToList();

                if (ImGui.BeginChild("TagList"))
                {
                    DrawTagCategory("Format", formatTags, dataset);
                    DrawTagCategory("Geometry", geometryTags, dataset);
                    DrawTagCategory("Analysis", analysisTags, dataset);
                    DrawTagCategory("Source", sourceTags, dataset);
                    DrawTagCategory("Other", otherTags, dataset);
                    ImGui.EndChild();
                }

                ImGui.End();
            }
        }

        private void DrawTagCategory(string name, List<GISTag> tags, GISDataset dataset)
        {
            if (!tags.Any()) return;

            if (ImGui.CollapsingHeader(name, ImGuiTreeNodeFlags.DefaultOpen))
                foreach (var tag in tags)
                {
                    var isEnabled = !dataset.HasTag(tag);
                    if (!isEnabled) ImGui.BeginDisabled();

                    if (ImGui.Button($"+ {tag.GetDisplayName()}")) dataset.AddTag(tag);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(tag.GetCategoryDescription());

                    if (!isEnabled) ImGui.EndDisabled();
                }
        }

        private void AutoDetectTags(GISDataset dataset)
        {
            var recommended = GISTagExtensions.GetRecommendedTags(dataset.FilePath ?? "",
                dataset.Layers.FirstOrDefault()?.Type ?? LayerType.Vector);
            foreach (var tag in recommended)
                if (!dataset.HasTag(tag))
                    dataset.AddTag(tag);
        }
    }

    private class ProjectionInfoTool : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            ImGui.Text($"Projection: {gisDataset.Projection.Name} ({gisDataset.Projection.EPSG})");
            ImGui.Text($"Bounds Min: ({gisDataset.Bounds.Min.X:F4}, {gisDataset.Bounds.Min.Y:F4})");
            ImGui.Text($"Bounds Max: ({gisDataset.Bounds.Max.X:F4}, {gisDataset.Bounds.Max.Y:F4})");
        }
    }

    private class VectorOperationsTool : IDatasetTools
    {
        private float _bufferDistance = 10.0f;

        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            var operations = gisDataset.GetAvailableOperations()
                .Where(op => op is "Buffer" or "Clip" or "Intersect" or "Dissolve").ToList();
            if (!operations.Any())
            {
                ImGui.TextDisabled("Add vector data tags to enable operations.");
                return;
            }

            ImGui.InputFloat("Buffer Distance", ref _bufferDistance);
            if (ImGui.Button("Buffer")) PerformBufferOperation(gisDataset, _bufferDistance);
        }

        private void PerformBufferOperation(GISDataset dataset, float distance)
        {
            var activeLayer = dataset.Layers.FirstOrDefault(l => l.Type == LayerType.Vector && l.Features.Any());
            if (activeLayer == null)
            {
                Logger.LogError("No vector layer with features found to buffer.");
                return;
            }

            try
            {
                var bufferedFeatures = new List<GISFeature>();
                foreach (var feature in activeLayer.Features)
                {
                    var ntsGeom = dataset.ConvertToNTSGeometry(feature);
                    if (ntsGeom == null) continue;

                    var bufferedGeom = GISOperationsImpl.BufferGeometry(ntsGeom, distance);
                    if (bufferedGeom == null) continue;

                    var bufferedFeature = GISDataset.ConvertNTSGeometry(bufferedGeom, feature.Properties);
                    if (bufferedFeature != null) bufferedFeatures.Add(bufferedFeature);
                }

                if (bufferedFeatures.Count > 0)
                {
                    var newDataset = dataset.CloneWithFeatures(bufferedFeatures, $"_buffer_{distance}");
                    ProjectManager.Instance.AddDataset(newDataset);
                    Logger.Log($"Successfully buffered {bufferedFeatures.Count} features.");
                }
                else
                {
                    Logger.LogWarning("Buffer operation resulted in no features.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Buffer operation failed: {ex.Message}");
            }
        }
    }

    private class CreateFromMetadataTool : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            ImGui.TextWrapped("Create a point layer from other datasets' metadata.");
            var datasetsWithCoords = ProjectManager.Instance.LoadedDatasets
                .Where(d => d.DatasetMetadata?.Latitude != null && d.DatasetMetadata?.Longitude != null).ToList();
            if (ImGui.Button($"Create Points ({datasetsWithCoords.Count})"))
            {
                var layer = gisDataset.CreateLayerFromMetadata(datasetsWithCoords);
                gisDataset.Layers.Add(layer);
                gisDataset.AddTag(GISTag.FieldData);
            }
        }
    }

    private class ExportTool : IDatasetTools
    {
        private readonly ProgressBarDialog _exportProgressDialog;
        private readonly ImGuiExportFileDialog _geoTiffExportDialog;
        private readonly ImGuiExportFileDialog _shpExportDialog;
        private CancellationTokenSource _exportCts;

        public ExportTool()
        {
            _shpExportDialog = new ImGuiExportFileDialog("GISShpExport", "Export as Shapefile");
            _shpExportDialog.SetExtensions((".shp", "ESRI Shapefile"));
            _geoTiffExportDialog = new ImGuiExportFileDialog("GISGeoTiffExport", "Export as GeoTIFF");
            _geoTiffExportDialog.SetExtensions((".tif", "Tagged Image File Format"));
            _exportProgressDialog = new ProgressBarDialog("Exporting GIS Data");
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not GISDataset gisDataset) return;
            if (ImGui.Button("Export as Shapefile...")) _shpExportDialog.Open(gisDataset.Name);
            if (ImGui.Button("Export as GeoTIFF...")) _geoTiffExportDialog.Open(gisDataset.Name);
        }

        public void HandleDialogSubmissions(GISDataset dataset)
        {
            _exportProgressDialog.Submit();
            if (_shpExportDialog.Submit())
            {
                _exportCts = new CancellationTokenSource();
                StartExportTask(GISExporter.ExportToShapefileAsync(dataset, _shpExportDialog.SelectedPath,
                    CreateProgressHandler(), _exportCts.Token));
            }

            if (_geoTiffExportDialog.Submit())
            {
                _exportCts = new CancellationTokenSource();
                StartExportTask(GISExporter.ExportToGeoTiffAsync(dataset, _geoTiffExportDialog.SelectedPath,
                    CreateProgressHandler(), _exportCts.Token));
            }
        }

        private void StartExportTask(Task exportTask)
        {
            _exportProgressDialog.Open("Starting export...");
            exportTask.ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    _exportProgressDialog.Update(1.0f, "Export Canceled.");
                }
                else if (t.IsFaulted)
                {
                    _exportProgressDialog.Update(1.0f, "Export Failed!");
                    Logger.LogError($"Export failed: {t.Exception?.InnerException?.Message}");
                }
                else
                {
                    _exportProgressDialog.Update(1.0f, "Export Complete!");
                }

                // Keep the dialog open for a moment to show the final status
                Task.Delay(1500).ContinueWith(_ => _exportProgressDialog.Close(),
                    TaskScheduler.FromCurrentSynchronizationContext());
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private IProgress<(float p, string msg)> CreateProgressHandler()
        {
            return new Progress<(float p, string msg)>(value => _exportProgressDialog.Update(value.p, value.msg));
        }
    }
}