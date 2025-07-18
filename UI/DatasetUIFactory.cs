// GeoscientistToolkit/UI/DatasetUIFactory.cs
// Creates UI components based on dataset type.

using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
    // Fallback implementations for unsupported features.
    internal class UnsupportedPropertiesRenderer : IDatasetPropertiesRenderer { public void Draw(Dataset dataset) { } }
    internal class UnsupportedTools : IDatasetTools { public void Draw(Dataset dataset) { ImGui.TextDisabled("No tools for this dataset type."); } }
    
    // This now needs a Dispose method to satisfy the interface.
    internal class UnsupportedViewer : IDatasetViewer 
    { 
        public void DrawToolbarControls() { } 
        public void DrawContent(ref float zoom, ref Vector2 pan) { ImGui.TextDisabled("Viewer not available."); }
        public void Dispose() { } // Empty implementation is fine here.
    }

    /// <summary>
    /// Creates UI components based on dataset type.
    /// </summary>
    public static class DatasetUIFactory
    {
        public static IDatasetViewer CreateViewer(Dataset dataset)
        {
            return dataset.Type switch
            {
                DatasetType.CtImageStack => new CtImageStackViewer(),
                DatasetType.SingleImage => new ImageViewer((ImageDataset)dataset),
                _ => new UnsupportedViewer()
            };
        }

        public static IDatasetPropertiesRenderer CreatePropertiesRenderer(Dataset dataset)
        {
            return dataset.Type switch
            {
                DatasetType.CtImageStack => new CtImageStackPropertiesRenderer(),
                DatasetType.SingleImage => new ImagePropertiesRenderer(),
                _ => new UnsupportedPropertiesRenderer()
            };
        }

        public static IDatasetTools CreateTools(Dataset dataset)
        {
            return dataset.Type switch
            {
                DatasetType.SingleImage => new ImageTools(),
                _ => new UnsupportedTools()
            };
        }
    }
}