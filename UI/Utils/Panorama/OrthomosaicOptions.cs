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
        /// Maximum number of images to blend per pixel
        /// </summary>
        public int MaxBlendImages { get; set; } = 3;
    }
}