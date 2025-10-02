// GeoscientistToolkit/UI/AcousticVolume/AcousticInteractionManager.cs
using System.Numerics;

namespace GeoscientistToolkit.UI.AcousticVolume
{
    /// <summary>
    /// Defines the current interactive mode of the Acoustic Volume Viewer.
    /// </summary>
    public enum ViewerInteractionMode
    {
        /// <summary>
        /// Standard navigation (zoom/pan).
        /// </summary>
        None,
        /// <summary>
        /// The user is actively drawing a line for analysis.
        /// </summary>
        DrawingLine
    }

    /// <summary>
    /// A static class to manage state and communication for interactions between
    /// the AcousticVolumeViewer and various analysis tools.
    /// </summary>
    public static class AcousticInteractionManager
    {
        public static ViewerInteractionMode InteractionMode { get; set; } = ViewerInteractionMode.None;

        // Properties for the defined line
        public static bool IsLineDefinitionActive { get; set; } = false;
        public static int LineSliceIndex { get; set; } = -1;
        public static int LineViewIndex { get; set; } = -1; // 0=XY, 1=XZ, 2=YZ
        public static Vector2 LineStartPoint { get; set; }
        public static Vector2 LineEndPoint { get; set; }
        public static bool HasNewLine { get; set; } = false;

        /// <summary>
        /// Initiates the line drawing mode in the viewer.
        /// </summary>
        public static void StartLineDrawing()
        {
            InteractionMode = ViewerInteractionMode.DrawingLine;
            HasNewLine = false;
        }

        /// <summary>
        /// Cancels the line drawing mode.
        /// </summary>
        public static void CancelLineDrawing()
        {
            InteractionMode = ViewerInteractionMode.None;
            IsLineDefinitionActive = false;
        }

        /// <summary>
        /// Finalizes the line coordinates and notifies that a new line is available.
        /// </summary>
        public static void FinalizeLine(int sliceIndex, int viewIndex, Vector2 start, Vector2 end)
        {
            LineSliceIndex = sliceIndex;
            LineViewIndex = viewIndex;
            LineStartPoint = start;
            LineEndPoint = end;
            InteractionMode = ViewerInteractionMode.None;
            IsLineDefinitionActive = false;
            HasNewLine = true;
        }
    }
}