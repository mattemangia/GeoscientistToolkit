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
                // Placeholder for histogram plot
                var region = ImGui.GetContentRegionAvail();
                ImGui.Text("Histogram plot (not implemented)");
                ImGui.Dummy(new Vector2(region.X, 100)); // Reserve space
            }
        }
    }
}