// GeoscientistToolkit/Business/Photogrammetry/Options/DEMOptions.cs

namespace GeoscientistToolkit.Business.Photogrammetry
{
    /// <summary>
    /// Options for Digital Elevation Model generation
    /// </summary>
    public partial class DEMOptions
    {
        public enum SourceData
        {
            PointCloud,
            Mesh
        }

        public enum InterpolationMethod
        {
            /// <summary>
            /// Inverse Distance Weighting (Shepard, 1968)
            /// </summary>
            IDW,
            /// <summary>
            /// Bilinear interpolation for regular grids
            /// </summary>
            Bilinear,
            /// <summary>
            /// Priority-based propagation for feature preservation
            /// </summary>
            PriorityQueue
        }

        /// <summary>
        /// Source geometry for elevation data
        /// </summary>
        public SourceData Source { get; set; } = SourceData.Mesh;

        /// <summary>
        /// Spatial resolution in meters per pixel
        /// </summary>
        public float Resolution { get; set; } = 0.1f;

        /// <summary>
        /// Whether to fill holes in the elevation data
        /// </summary>
        public bool FillHoles { get; set; } = true;

        /// <summary>
        /// Method for hole filling interpolation
        /// </summary>
        public InterpolationMethod HoleFillMethod { get; set; } = InterpolationMethod.IDW;

        /// <summary>
        /// Whether to apply smoothing filter
        /// </summary>
        public bool SmoothSurface { get; set; } = true;
    }
}