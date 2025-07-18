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

            if (ImGui.CollapsingHeader("CT Image Stack Properties", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                PropertiesPanel.DrawProperty("Binning Size", ct.BinningSize.ToString());
                PropertiesPanel.DrawProperty("Load Mode", ct.LoadFullInMemory ? "In Memory" : "On Demand");
                PropertiesPanel.DrawProperty("Pixel Size", $"{ct.PixelSize} {ct.Unit}");
                
                if (ct.Width > 0)
                {
                    PropertiesPanel.DrawProperty("Dimensions", $"{ct.Width} × {ct.Height} × {ct.Depth}");
                    PropertiesPanel.DrawProperty("Bit Depth", $"{ct.BytesPerPixel * 8}-bit");
                }
                ImGui.Unindent();
            }
        }
    }
}