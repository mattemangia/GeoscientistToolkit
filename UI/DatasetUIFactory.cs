// GeoscientistToolkit/UI/DatasetUIFactory.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI;

namespace GeoscientistToolkit.UI
{
    /// <summary>
    /// Factory for creating dataset-specific UI components
    /// </summary>
    public static class DatasetUIFactory
    {
        /// <summary>
        /// Creates a viewer appropriate for the given dataset type
        /// </summary>
        public static IDatasetViewer CreateViewer(Dataset dataset)
        {
            return dataset switch
            {
                ImageDataset imageDataset => new ImageViewer(imageDataset),
                CtImageStackDataset ctDataset => new CtImageStackViewer(ctDataset),
                _ => throw new NotSupportedException($"No viewer available for dataset type: {dataset.GetType().Name}")
            };
        }
        
        /// <summary>
        /// Creates a properties renderer appropriate for the given dataset type
        /// </summary>
        public static IDatasetPropertiesRenderer CreatePropertiesRenderer(Dataset dataset)
        {
            return dataset switch
            {
                ImageDataset => new ImagePropertiesRenderer(),
                CtImageStackDataset => new CtImageStackPropertiesRenderer(),
                _ => new DefaultPropertiesRenderer()
            };
        }
        
        /// <summary>
        /// Creates tools appropriate for the given dataset type
        /// </summary>
        public static IDatasetTools CreateTools(Dataset dataset)
        {
            return dataset switch
            {
                ImageDataset => new ImageTools(),
                CtImageStackDataset => new CtImageStackTools(),
                _ => new DefaultTools()
            };
        }
    }
    
    // Default implementations
    internal class DefaultPropertiesRenderer : IDatasetPropertiesRenderer
    {
        public void Draw(Dataset dataset)
        {
            // Default implementation shows no additional properties
        }
    }
    
    internal class DefaultTools : IDatasetTools
    {
        public void Draw(Dataset dataset)
        {
            ImGuiNET.ImGui.TextDisabled("No tools available for this dataset type");
        }
    }
}