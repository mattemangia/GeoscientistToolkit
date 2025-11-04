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
        /// Whether to apply smoothing filter
        /// </summary>
        public bool SmoothSurface { get; set; } = true;
    }
}