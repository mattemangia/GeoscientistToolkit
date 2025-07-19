// GeoscientistToolkit/Data/CtImageStack/CtImageStackPropertiesRenderer.cs
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackPropertiesRenderer : IDatasetPropertiesRenderer
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ct) return;

            if (ImGui.CollapsingHeader("CT Stack Properties", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                
                if (ct.Width > 0 && ct.Height > 0 && ct.Depth > 0)
                {
                    PropertiesPanel.DrawProperty("Dimensions", $"{ct.Width} × {ct.Height} × {ct.Depth}");
                    PropertiesPanel.DrawProperty("Total Slices", ct.Depth.ToString());
                }
                
                if (ct.PixelSize > 0)
                {
                    PropertiesPanel.DrawProperty("Pixel Size", $"{ct.PixelSize:F3} {ct.Unit}");
                    PropertiesPanel.DrawProperty("Slice Thickness", $"{ct.SliceThickness:F3} {ct.Unit}");
                }
                
                if (ct.BinningSize > 0)
                {
                    PropertiesPanel.DrawProperty("Binning", $"{ct.BinningSize}×{ct.BinningSize}");
                }
                
                PropertiesPanel.DrawProperty("Bit Depth", $"{ct.BitDepth}-bit");
                
                ImGui.Unindent();
            }

            if (ImGui.CollapsingHeader("Acquisition Info"))
            {
                ImGui.Indent();
                ImGui.TextDisabled("Not yet implemented");
                ImGui.Unindent();
            }
        }
    }
}