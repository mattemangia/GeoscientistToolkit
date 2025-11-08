// GeoscientistToolkit/Business/Photogrammetry/Options/OrthomosaicOptions.cs

namespace GeoscientistToolkit.Business.Photogrammetry
{
    /// <summary>
    /// Options for orthomosaic generation
    /// </summary>
    public class OrthomosaicOptions
    {
        public enum SourceData
        {
            PointCloud,
            Mesh
        }

        public enum BlendingMode
        {
            /// <summary>
            /// Use only the best image (fastest, but visible seams)
            /// </summary>
            Best,
            /// <summary>
            /// Distance-weighted blending from multiple images
            /// </summary>
            DistanceWeighted,
            /// <summary>
            /// Angle-aware blending considering surface normals
            /// </summary>
            AngleWeighted,
            /// <summary>
            /// Feathered blending with smooth transitions
            /// Reference: Lin et al. (2016) - Blending zone determination
            /// </summary>
            Feathered
        }

        /// <summary>
        /// Source geometry for elevation sampling
        /// </summary>
        public SourceData Source { get; set; } = SourceData.Mesh;

        /// <summary>
        /// Ground sampling distance in meters per pixel
        /// </summary>
        public float GroundSamplingDistance { get; set; } = 0.05f;

        /// <summary>
        /// Whether to blend colors from multiple images
        /// </summary>
        public bool EnableBlending { get; set; } = true;

        /// <summary>
        /// Blending mode for color composition
        /// </summary>
        public BlendingMode Blending { get; set; } = BlendingMode.AngleWeighted;

        /// <summary>
        /// Maximum number of images to blend per pixel
        /// </summary>
        public int MaxBlendImages { get; set; } = 3;

        /// <summary>
        /// Apply color balancing across images to minimize visual seams
        /// </summary>
        public bool ColorBalance { get; set; } = false;
    }
}