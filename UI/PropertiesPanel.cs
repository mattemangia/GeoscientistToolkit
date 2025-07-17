// GeoscientistToolkit/UI/PropertiesPanel.cs
// This class implements the UI panel that displays the properties of the currently selected dataset.
// The content of this panel is context-sensitive to the selected dataset.

using GeoscientistToolkit.Data;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class PropertiesPanel
    {
        public void Submit(ref bool pOpen, Dataset selectedDataset)
        {
            if (ImGui.Begin("Properties", ref pOpen))
            {
                if (selectedDataset != null)
                {
                    ImGui.Text($"Name: {selectedDataset.Name}");
                    ImGui.Text($"Type: {selectedDataset.Type}");
                    ImGui.Text($"Path: {selectedDataset.FilePath}");

                    // Display specific properties for CT Image Stacks
                    if (selectedDataset is CtImageStackDataset ctDataset)
                    {
                        ImGui.Separator();
                        ImGui.Text("CT Image Stack Properties");
                        ImGui.Text($"Binning: {ctDataset.BinningSize}");
                        ImGui.Text($"In Memory: {ctDataset.LoadFullInMemory}");
                        ImGui.Text($"Pixel Size: {ctDataset.PixelSize} {ctDataset.Unit}");
                    }
                }
                else
                {
                    ImGui.Text("No dataset selected.");
                }
            }
            ImGui.End();
        }
    }
}