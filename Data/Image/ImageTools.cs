// GeoscientistToolkit/Data/Image/ImageTools.cs
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.Data.Image
{
    public class ImageTools : IDatasetTools
    {
        private float _brightness = 0;
        private float _contrast = 1;

        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset) return;

            ImGui.Text("Image Adjustments");
            ImGui.Separator();

            ImGui.SliderFloat("Brightness", ref _brightness, -1.0f, 1.0f);
            ImGui.SliderFloat("Contrast", ref _contrast, 0.0f, 2.0f);

            if (ImGui.Button("Apply", new Vector2(-1, 0)))
            {
                // TODO: Apply adjustments
            }
        }
    }
}