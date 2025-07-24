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
                StreamingCtVolumeDataset streamingDataset =>
                    streamingDataset.EditablePartner != null
                        ? new CtCombinedViewer(streamingDataset.EditablePartner)
                        : new CtVolume3DViewer(streamingDataset),

                CtImageStackDataset ctDataset => new CtImageStackViewer(ctDataset),

                ImageDataset imageDataset => new ImageViewer(imageDataset),

                DatasetGroup => throw new InvalidOperationException("Cannot open a DatasetGroup in a viewer. Please open individual datasets."),

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

        // --- MODIFIED ---
        public static IDatasetTools CreateTools(Dataset dataset)
        {
            // This logic determines which tools panel to show based on the active dataset.
            return dataset switch
            {
                // If the active dataset is the editable stack, show the segmentation tools.
                CtImageStackDataset => new CtImageStackTools(),

                // If the active dataset is the 3D view, check its partner.
                // If the partner is an editable stack, show the segmentation tools for that partner.
                StreamingCtVolumeDataset sds when sds.EditablePartner != null => new CtImageStackTools(),

                // For any other dataset type, show the default empty tools panel.
                _ => new DefaultTools()
            };
        }
    }

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