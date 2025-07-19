// GeoscientistToolkit/UI/Interfaces/IDatasetViewer.cs
using System.Numerics;

namespace GeoscientistToolkit.UI.Interfaces
{
    /// <summary>
    /// Interface for dataset-specific viewers.
    /// </summary>
    public interface IDatasetViewer : IDisposable
    {
        /// <summary>
        /// Draws viewer-specific controls in the toolbar.
        /// </summary>
        void DrawToolbarControls();

        /// <summary>
        /// Draws the main content of the viewer.
        /// </summary>
        /// <param name="zoom">Reference to the zoom level, can be modified by the viewer.</param>
        /// <param name="pan">Reference to the pan offset, can be modified by the viewer.</param>
        void DrawContent(ref float zoom, ref Vector2 pan);
    }
}