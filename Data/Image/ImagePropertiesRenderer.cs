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
                // Ensure the dataset is loaded to generate histogram data
                if (image.HistogramLuminance == null)
                {
                    // Force load the dataset to generate histogram
                    image.Load();
                    
                    // If still no histogram after loading, show message
                    if (image.HistogramLuminance == null)
                    {
                        ImGui.TextDisabled("Histogram data not available.");
                        ImGui.TextWrapped("The image format may not support histogram generation.");
                        return;
                    }
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
                    
                    // Only show RGB tabs if the data is available
                    if (image.HistogramR != null)
                    {
                        if (ImGui.BeginTabItem("Red"))
                        {
                            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));
                            ImGui.PlotHistogram("##Red", ref image.HistogramR[0], image.HistogramR.Length, 0, null, 0.0f, float.MaxValue, plotSize);
                            ImGui.PopStyleColor();
                            ImGui.EndTabItem();
                        }
                    }
                    
                    if (image.HistogramG != null)
                    {
                        if (ImGui.BeginTabItem("Green"))
                        {
                            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.3f, 1.0f, 0.3f, 1.0f));
                            ImGui.PlotHistogram("##Green", ref image.HistogramG[0], image.HistogramG.Length, 0, null, 0.0f, float.MaxValue, plotSize);
                            ImGui.PopStyleColor();
                            ImGui.EndTabItem();
                        }
                    }
                    
                    if (image.HistogramB != null)
                    {
                        if (ImGui.BeginTabItem("Blue"))
                        {
                            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.3f, 0.3f, 1.0f, 1.0f));
                            ImGui.PlotHistogram("##Blue", ref image.HistogramB[0], image.HistogramB.Length, 0, null, 0.0f, float.MaxValue, plotSize);
                            ImGui.PopStyleColor();
                            ImGui.EndTabItem();
                        }
                    }
                    
                    ImGui.EndTabBar();
                }

                // Show histogram statistics
                if (image.HistogramLuminance != null)
                {
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                    
                    if (ImGui.TreeNode("Histogram Statistics"))
                    {
                        // Calculate min/max/mean for luminance
                        float min = float.MaxValue;
                        float max = float.MinValue;
                        float sum = 0;
                        int totalPixels = 0;
                        
                        for (int i = 0; i < image.HistogramLuminance.Length; i++)
                        {
                            float count = image.HistogramLuminance[i];
                            if (count > 0)
                            {
                                if (i < min) min = i;
                                if (i > max) max = i;
                                sum += i * count;
                                totalPixels += (int)count;
                            }
                        }
                        
                        float mean = totalPixels > 0 ? sum / totalPixels : 0;
                        
                        ImGui.Text($"Min: {min:F0}");
                        ImGui.Text($"Max: {max:F0}");
                        ImGui.Text($"Mean: {mean:F1}");
                        ImGui.Text($"Total Pixels: {totalPixels:N0}");
                        
                        ImGui.TreePop();
                    }
                }
            }
        }
    }
}