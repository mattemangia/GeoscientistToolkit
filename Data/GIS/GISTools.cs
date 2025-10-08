// GeoscientistToolkit/UI/GIS/GISTools.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS;

public class GISTools : IDatasetTools
{
    private readonly ImGuiExportFileDialog _geoJsonExportDialog;
    private readonly ImGuiExportFileDialog _shpExportDialog;
    private string _newLayerName = "New Layer";
    private bool _showCreateFromMetadata = false;

    public GISTools()
    {
        _shpExportDialog = new ImGuiExportFileDialog("GISShpExport", "Export as Shapefile");
        _shpExportDialog.SetExtensions((".shp", "ESRI Shapefile"));

        _geoJsonExportDialog = new ImGuiExportFileDialog("GISGeoJsonExport", "Export as GeoJSON");
        _geoJsonExportDialog.SetExtensions((".geojson", "GeoJSON"));
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not GISDataset gisDataset)
            return;

        if (ImGui.CollapsingHeader("Layers", ImGuiTreeNodeFlags.DefaultOpen)) DrawLayerManager(gisDataset);

        if (ImGui.CollapsingHeader("Create from Metadata")) DrawCreateFromMetadata(gisDataset);

        if (ImGui.CollapsingHeader("Export")) DrawExportOptions(gisDataset);

        if (ImGui.CollapsingHeader("Projection")) DrawProjectionInfo(gisDataset);

        // Handle export dialogs
        if (_shpExportDialog.Submit()) gisDataset.SaveAsShapefile(_shpExportDialog.SelectedPath);

        if (_geoJsonExportDialog.Submit()) gisDataset.SaveAsGeoJSON(_geoJsonExportDialog.SelectedPath);
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
            if (ImGui.Checkbox("##Visible", ref visible)) layer.IsVisible = visible;

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
                    if (ImGui.ColorEdit4("Color", ref color)) layer.Color = color;

                    // Line width
                    var lineWidth = layer.LineWidth;
                    if (ImGui.SliderFloat("Line Width", ref lineWidth, 0.5f, 10.0f)) layer.LineWidth = lineWidth;

                    // Point size
                    var pointSize = layer.PointSize;
                    if (ImGui.SliderFloat("Point Size", ref pointSize, 1.0f, 20.0f)) layer.PointSize = pointSize;

                    // Editable
                    var editable = layer.IsEditable;
                    if (ImGui.Checkbox("Editable", ref editable)) layer.IsEditable = editable;
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

                if (datasetsWithCoords.Count > 5) ImGui.TextDisabled($"... and {datasetsWithCoords.Count - 5} more");
            }

            ImGui.EndChild();
        }
    }

    private void DrawExportOptions(GISDataset dataset)
    {
        if (ImGui.Button("Export as Shapefile...")) _shpExportDialog.Open(dataset.Name);

        if (ImGui.Button("Export as GeoJSON...")) _geoJsonExportDialog.Open(dataset.Name);

        if (ImGui.Button("Export Layer Attributes as CSV..."))
            // TODO: Implement CSV export of attributes
            Logger.Log("CSV export not yet implemented");
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