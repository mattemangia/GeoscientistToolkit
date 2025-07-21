// GeoscientistToolkit/Data/Image/ImageTools.cs
using GeoscientistToolkit.Business;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.Data.Image
{
    // A concrete command for changing brightness.
    // In a real app, this would modify shader values or pixel data.
    public class AdjustBrightnessCommand : ICommand
    {
        private readonly ImageDataset _target;
        private readonly float _newBrightness;
        private readonly float _oldBrightness;

        // This static property is a stand-in for a real property on the viewer/image
        // that would control a shader uniform or image processing effect.
        public static float CurrentBrightness { get; private set; } = 0;

        public AdjustBrightnessCommand(ImageDataset target, float newBrightness)
        {
            _target = target;
            _newBrightness = newBrightness;
            _oldBrightness = CurrentBrightness; // Store the old value before changing
        }

        public void Execute()
        {
            CurrentBrightness = _newBrightness;
            Logger.Log($"Applied brightness {CurrentBrightness:F2} to {_target.Name}");
            // In a real implementation, you would trigger a re-render or update a shader uniform here.
        }

        public void UnExecute()
        {
            CurrentBrightness = _oldBrightness;
            Logger.Log($"Reverted brightness to {CurrentBrightness:F2} for {_target.Name}");
            // Revert the shader uniform or image processing effect.
        }
    }


    public class ImageTools : IDatasetTools
    {
        private float _brightness = AdjustBrightnessCommand.CurrentBrightness;
        private float _contrast = 1;

        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset imageDataset) return;

            // Sync the slider with the actual current value
            _brightness = AdjustBrightnessCommand.CurrentBrightness;

            ImGui.Text("Image Adjustments");
            ImGui.Separator();

            if(ImGui.SliderFloat("Brightness", ref _brightness, -1.0f, 1.0f))
            {
                // This would be for live-preview, not using the command pattern
            }

            ImGui.SliderFloat("Contrast", ref _contrast, 0.0f, 2.0f);

            // This button uses the command pattern for a discrete, undoable action
            if (ImGui.Button("Apply Brightness", new Vector2(-1, 0)))
            {
                var command = new AdjustBrightnessCommand(imageDataset, _brightness);
                GlobalPerformanceManager.Instance.UndoManager.Do(command);
            }
        }
    }
}