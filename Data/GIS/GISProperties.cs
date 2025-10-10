// GeoscientistToolkit/UI/GIS/GISProperties.cs (Updated)

using System.Numerics;
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
        foreach (var layer in gisDataset.Layers)
            totalFeatures += layer.Features.Count;
        ImGui.Text($"Total Features: {totalFeatures}");

        ImGui.Separator();

        // Tags Section
        DrawTagsSection(gisDataset);

        ImGui.Separator();
        DrawLayerOrdering(gisDataset);

        ImGui.Separator();
        // Projection Info
        ImGui.Text($"Projection: {gisDataset.Projection.Name}");
        ImGui.Text($"EPSG: {gisDataset.Projection.EPSG}");

        ImGui.Separator();

        // Basemap Info
        if (gisDataset.BasemapType != BasemapType.None)
        {
            ImGui.Text($"Basemap: {gisDataset.BasemapType}");
            if (!string.IsNullOrEmpty(gisDataset.BasemapPath))
                ImGui.Text($"Path: {gisDataset.BasemapPath}");
        }
        else
        {
            ImGui.TextDisabled("No basemap");
        }

        ImGui.Separator();

        // Bounds Info
        ImGui.Text("Bounds:");
        ImGui.Text($"  X: {gisDataset.Bounds.Min.X:F6} to {gisDataset.Bounds.Max.X:F6}");
        ImGui.Text($"  Y: {gisDataset.Bounds.Min.Y:F6} to {gisDataset.Bounds.Max.Y:F6}");
        ImGui.Text($"  Width: {gisDataset.Bounds.Width:F6}");
        ImGui.Text($"  Height: {gisDataset.Bounds.Height:F6}");

        ImGui.Separator();

        // Available Operations
        DrawAvailableOperations(gisDataset);
    }

    private void DrawTagsSection(GISDataset dataset)
    {
        if (ImGui.CollapsingHeader("Tags", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (dataset.Tags == GISTag.None)
            {
                ImGui.TextDisabled("No tags assigned");
            }
            else
            {
                // Display active tags by category
                var formatTags = dataset.Tags.GetFlags().Where(t => t.IsFormatTag()).ToList();
                var geometryTags = dataset.Tags.GetFlags().Where(t => t.IsGeometryTypeTag()).ToList();
                var analysisTags = dataset.Tags.GetFlags().Where(t => t.IsAnalysisTag()).ToList();
                var sourceTags = dataset.Tags.GetFlags().Where(t => t.IsSourceTag()).ToList();
                var otherTags = dataset.Tags.GetFlags()
                    .Where(t => !t.IsFormatTag() && !t.IsGeometryTypeTag() && !t.IsAnalysisTag() && !t.IsSourceTag())
                    .ToList();

                if (formatTags.Any())
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "Format:");
                    ImGui.SameLine();
                    ImGui.Text(string.Join(", ", formatTags.Select(t => t.GetDisplayName())));
                }

                if (geometryTags.Any())
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.5f, 1.0f, 1.0f), "Geometry:");
                    ImGui.SameLine();
                    ImGui.Text(string.Join(", ", geometryTags.Select(t => t.GetDisplayName())));
                }

                if (analysisTags.Any())
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), "Analysis:");
                    ImGui.SameLine();
                    ImGui.Text(string.Join(", ", analysisTags.Select(t => t.GetDisplayName())));
                }

                if (sourceTags.Any())
                {
                    ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f), "Source:");
                    ImGui.SameLine();
                    ImGui.Text(string.Join(", ", sourceTags.Select(t => t.GetDisplayName())));
                }

                if (otherTags.Any())
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1.0f), "Properties:");
                    ImGui.SameLine();
                    ImGui.Text(string.Join(", ", otherTags.Select(t => t.GetDisplayName())));
                }
            }

            ImGui.Spacing();

            // Suggested color scheme
            var suggestedScheme = dataset.Tags.GetColorScheme();
            if (suggestedScheme != "Default")
            {
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.5f, 1.0f), "Suggested Color Scheme:");
                ImGui.SameLine();
                ImGui.Text(suggestedScheme);
            }

            // Capabilities based on tags
            ImGui.Spacing();
            var capabilities = new List<string>();
            if (dataset.Tags.RequiresGeoreference())
                capabilities.Add("Requires Georeferencing");
            if (dataset.Tags.SupportsTerrainAnalysis())
                capabilities.Add("Terrain Analysis Available");
            if (dataset.Tags.SupportsAttributeData())
                capabilities.Add("Attribute Operations Available");

            if (capabilities.Any())
            {
                ImGui.TextColored(new Vector4(0.5f, 1.0f, 1.0f, 1.0f), "Capabilities:");
                foreach (var cap in capabilities) ImGui.BulletText(cap);
            }
        }
    }

    private void DrawAvailableOperations(GISDataset dataset)
    {
        if (ImGui.CollapsingHeader("Available Operations"))
        {
            var operations = dataset.GetAvailableOperations();

            if (operations.Length == 0)
            {
                ImGui.TextDisabled("No specialized operations available");
            }
            else
            {
                ImGui.Text($"{operations.Length} operations available:");
                ImGui.Spacing();

                if (ImGui.BeginChild("OperationsList", new Vector2(0, 150), ImGuiChildFlags.Border))
                    foreach (var operation in operations)
                    {
                        ImGui.BulletText(operation);

                        // Add tooltip for some operations
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text(GetOperationTooltip(operation));
                            ImGui.EndTooltip();
                        }
                    }

                ImGui.EndChild();

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                    "These operations are available based on the dataset's tags.");
            }
        }
    }

    private void DrawLayerOrdering(GISDataset dataset)
    {
        if (ImGui.CollapsingHeader("Display Order", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Layers are drawn bottom to top. Higher layers appear on top.");
            ImGui.Spacing();

            if (dataset.Layers.Count == 0)
            {
                ImGui.TextDisabled("No layers");
                return;
            }

            if (ImGui.BeginChild("LayerOrderList", new Vector2(0, 200), ImGuiChildFlags.Border))
            {
                for (var i = 0; i < dataset.Layers.Count; i++)
                {
                    var layer = dataset.Layers[i];
                    ImGui.PushID(i);

                    // Visibility checkbox
                    var visible = layer.IsVisible;
                    if (ImGui.Checkbox("##vis", ref visible)) layer.IsVisible = visible;

                    ImGui.SameLine();

                    // Layer color indicator
                    var colorSize = new Vector2(16, 16);
                    var cursorPos = ImGui.GetCursorScreenPos();
                    ImGui.GetWindowDrawList().AddRectFilled(
                        cursorPos,
                        cursorPos + colorSize,
                        ImGui.ColorConvertFloat4ToU32(layer.Color)
                    );
                    ImGui.GetWindowDrawList().AddRect(
                        cursorPos,
                        cursorPos + colorSize,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f))
                    );
                    ImGui.Dummy(colorSize);

                    ImGui.SameLine();

                    // Layer name
                    var textColor = layer.IsVisible
                        ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
                        : new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
                    ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                    ImGui.Text($"{layer.Name} ({layer.Features.Count} features)");
                    ImGui.PopStyleColor();

                    // Right-aligned buttons
                    ImGui.SameLine();
                    var availWidth = ImGui.GetContentRegionAvail().X;
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availWidth - 60);

                    // Move up button
                    if (i > 0)
                    {
                        if (ImGui.SmallButton("▲"))
                        {
                            var temp = dataset.Layers[i];
                            dataset.Layers[i] = dataset.Layers[i - 1];
                            dataset.Layers[i - 1] = temp;
                        }

                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move layer up (drawn later/on top)");
                    }
                    else
                    {
                        ImGui.Dummy(new Vector2(25, 0));
                    }

                    ImGui.SameLine();

                    // Move down button
                    if (i < dataset.Layers.Count - 1)
                    {
                        if (ImGui.SmallButton("▼"))
                        {
                            var temp = dataset.Layers[i];
                            dataset.Layers[i] = dataset.Layers[i + 1];
                            dataset.Layers[i + 1] = temp;
                        }

                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move layer down (drawn earlier/behind)");
                    }

                    ImGui.PopID();
                }

                ImGui.EndChild();
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Tip: Uncheck the visibility checkbox to hide a layer");
        }
    }

    private string GetOperationTooltip(string operation)
    {
        return operation switch
        {
            "Slope Analysis" => "Calculate slope angles from elevation data",
            "Aspect Analysis" => "Calculate the direction of slope faces",
            "Hillshade" => "Create shaded relief visualization",
            "Contour Generation" => "Generate contour lines from elevation data",
            "Buffer" => "Create buffer zones around features",
            "Clip" => "Extract features within a boundary",
            "Intersect" => "Find overlapping features",
            "Dissolve" => "Merge adjacent features",
            "NDVI Calculation" => "Calculate Normalized Difference Vegetation Index",
            "Classification" => "Classify features based on attributes",
            "Zonal Statistics" => "Calculate statistics within zones",
            "Watershed Delineation" => "Extract watershed boundaries",
            "Flow Direction" => "Calculate water flow direction",
            "Stereonet" => "Plot structural geology data on stereonet",
            "Rose Diagram" => "Visualize directional data distribution",
            "Point Counting" => "Perform modal analysis on thin sections",
            "Volume Calculation" => "Calculate volumes from 3D surfaces",
            _ => operation
        };
    }
}