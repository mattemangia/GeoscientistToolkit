// GeoscientistToolkit/Data/Image/ImagePropertiesRenderer.cs
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.Data.Image
{
    public class ImagePropertiesRenderer : IDatasetPropertiesRenderer
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset image) return;

            if (ImGui.CollapsingHeader("Image Properties", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                PropertiesPanel.DrawProperty("Resolution", $"{image.Width} x {image.Height}");
                PropertiesPanel.DrawProperty("Bit Depth", $"{image.BitDepth}-bit");
                ImGui.Unindent();
            }

            if (ImGui.CollapsingHeader("Histogram", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Check if histogram data is available. Loading is now automatic on selection.
                if (image.HistogramLuminance == null)
                {
                    ImGui.TextDisabled("Histogram data not available.");
                    return;
                }

                var plotSize = new Vector2(ImGui.GetContentRegionAvail().X, 100);

                if (ImGui.BeginTabBar("HistogramTabs"))
                {
                    if (ImGui.BeginTabItem("Luminance"))
                    {
                        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                        ImGui.PlotHistogram("##Luminance", ref image.HistogramLuminance[0], image.HistogramLuminance.Length, 0, null, 0.0f, float.MaxValue, plotSize);
                        ImGui.PopStyleColor();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Red"))
                    {
                        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));
                        ImGui.PlotHistogram("##Red", ref image.HistogramR[0], image.HistogramR.Length, 0, null, 0.0f, float.MaxValue, plotSize);
                        ImGui.PopStyleColor();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Green"))
                    {
                        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.3f, 1.0f, 0.3f, 1.0f));
                        ImGui.PlotHistogram("##Green", ref image.HistogramG[0], image.HistogramG.Length, 0, null, 0.0f, float.MaxValue, plotSize);
                        ImGui.PopStyleColor();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Blue"))
                    {
                        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.3f, 0.3f, 1.0f, 1.0f));
                        ImGui.PlotHistogram("##Blue", ref image.HistogramB[0], image.HistogramB.Length, 0, null, 0.0f, float.MaxValue, plotSize);
                        ImGui.PopStyleColor();
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
            }
        }
    }
}