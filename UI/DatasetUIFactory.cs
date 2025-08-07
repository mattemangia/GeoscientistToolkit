// GeoscientistToolkit/UI/DatasetUIFactory.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System;
using System.Numerics;

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

                Mesh3DDataset mesh3DDataset => new Mesh3DViewer(mesh3DDataset),

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
                Mesh3DDataset => new Mesh3DProperties(),
                _ => new DefaultPropertiesRenderer()
            };
        }

        public static IDatasetTools CreateTools(Dataset dataset)
        {
            return dataset switch
            {
                CtImageStackDataset => new CtImageStackTools(),
                StreamingCtVolumeDataset sds when sds.EditablePartner != null => new CtImageStackTools(),
                Mesh3DDataset => new Mesh3DTools(),
                _ => new DefaultTools()
            };
        }

        private class DefaultPropertiesRenderer : IDatasetPropertiesRenderer
        {
            public void Draw(Dataset dataset)
            {
                ImGui.TextDisabled("No properties available.");
            }
        }

        private class DefaultTools : IDatasetTools
        {
            public void Draw(Dataset dataset)
            {
                ImGui.TextDisabled("No tools available for this dataset type.");
            }
        }
    }
}
