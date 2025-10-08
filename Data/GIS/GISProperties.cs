// GeoscientistToolkit/UI/GIS/GISProperties.cs

using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS;

public class GISProperties : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not GISDataset gisDataset)
            return;

        ImGui.Text("Type: GIS Dataset");
        ImGui.Text($"Layers: {gisDataset.Layers.Count}");

        var totalFeatures = 0;
        foreach (var layer in gisDataset.Layers) totalFeatures += layer.Features.Count;
        ImGui.Text($"Total Features: {totalFeatures}");

        ImGui.Separator();

        ImGui.Text($"Projection: {gisDataset.Projection.Name}");
        ImGui.Text($"EPSG: {gisDataset.Projection.EPSG}");

        ImGui.Separator();

        if (gisDataset.BasemapType != BasemapType.None)
        {
            ImGui.Text($"Basemap: {gisDataset.BasemapType}");
            if (!string.IsNullOrEmpty(gisDataset.BasemapPath)) ImGui.Text($"Path: {gisDataset.BasemapPath}");
        }
        else
        {
            ImGui.TextDisabled("No basemap");
        }

        ImGui.Separator();

        ImGui.Text("Bounds:");
        ImGui.Text($"  X: {gisDataset.Bounds.Min.X:F6} to {gisDataset.Bounds.Max.X:F6}");
        ImGui.Text($"  Y: {gisDataset.Bounds.Min.Y:F6} to {gisDataset.Bounds.Max.Y:F6}");
        ImGui.Text($"  Width: {gisDataset.Bounds.Width:F6}");
        ImGui.Text($"  Height: {gisDataset.Bounds.Height:F6}");
    }
}