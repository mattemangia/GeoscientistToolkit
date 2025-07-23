// GeoscientistToolkit/UI/DatasetUIFactory.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.UI.Interfaces;
using System;

namespace GeoscientistToolkit.UI
{
    public static class DatasetUIFactory
    {
        public static IDatasetViewer CreateViewer(Dataset dataset)
        {
            return dataset switch
            {
                // The .gvt file opens the high-performance 3D viewer. This is now correct.
                StreamingCtVolumeDataset streamingDataset => new CtVolume3DViewer(streamingDataset),

                // The legacy .bin dataset opens the 2D slice-by-slice viewer for editing.
                CtImageStackDataset ctDataset => new CtImageStackViewer(ctDataset),

                ImageDataset imageDataset => new ImageViewer(imageDataset),
                _ => throw new NotSupportedException($"No viewer available for dataset type: {dataset.GetType().Name}")
            };
        }
        
        public static IDatasetPropertiesRenderer CreatePropertiesRenderer(Dataset dataset)
        {
            return dataset switch
            {
                ImageDataset => new ImagePropertiesRenderer(),
                CtImageStackDataset or StreamingCtVolumeDataset => new CtImageStackPropertiesRenderer(),
                _ => new DefaultPropertiesRenderer()
            };
        }
        
        public static IDatasetTools CreateTools(Dataset dataset)
        {
            return dataset switch
            {
                // Tools for segmentation ONLY apply to the editable CtImageStackDataset.
                CtImageStackDataset => new CtImageStackTools(),

                // The StreamingCtVolumeDataset's controls are built into its viewer panel.
                _ => new DefaultTools()
            };
        }
    }
    
    // Default implementations are unchanged
    internal class DefaultPropertiesRenderer : IDatasetPropertiesRenderer
    {
        public void Draw(Dataset dataset) { }
    }
    
    internal class DefaultTools : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            ImGuiNET.ImGui.TextDisabled("No tools available for this dataset type.");
        }
    }
}