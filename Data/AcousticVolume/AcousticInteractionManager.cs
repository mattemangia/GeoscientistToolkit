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
        DrawingLine,
        /// <summary>
        /// The user is selecting a single point for analysis.
        /// </summary>
        SelectingPoint
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
        
        // --- NEW: Properties for a single point ---
        public static bool IsPointDefinitionActive { get; set; } = false;
        public static Vector3 SelectedPoint { get; set; } // Using Vector3 for 3D volume coordinates
        public static bool HasNewPoint { get; set; } = false;


        /// <summary>
        /// Initiates the line drawing mode in the viewer.
        /// </summary>
        public static void StartLineDrawing()
        {
            InteractionMode = ViewerInteractionMode.DrawingLine;
            HasNewLine = false;
            IsPointDefinitionActive = false; // Ensure other modes are off
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

        /// <summary>
        /// Initiates the point selection mode in the viewer.
        /// </summary>
        public static void StartPointSelection()
        {
            InteractionMode = ViewerInteractionMode.SelectingPoint;
            HasNewPoint = false;
            IsLineDefinitionActive = false; // Ensure other modes are off
        }

        /// <summary>
        /// Cancels the point selection mode.
        /// </summary>
        public static void CancelPointSelection()
        {
            InteractionMode = ViewerInteractionMode.None;
            IsPointDefinitionActive = false;
        }

        /// <summary>
        /// Finalizes the point coordinates and notifies that a new point is available.
        /// </summary>
        public static void FinalizePoint(Vector3 pointInVolume)
        {
            SelectedPoint = pointInVolume;
            InteractionMode = ViewerInteractionMode.None;
            IsPointDefinitionActive = false;
            HasNewPoint = true;
        }
    }
}