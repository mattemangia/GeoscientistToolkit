// GeoscientistToolkit/Business/Photogrammetry/Options/DenseCloudOptions.cs

namespace GeoscientistToolkit.Business.Photogrammetry
{
    /// <summary>
    /// Options for dense cloud generation
    /// </summary>
    public class DenseCloudOptions
    {
        /// <summary>
        /// Quality level (0=Low, 1=Medium, 2=High, 3=Ultra, 4=Extreme)
        /// </summary>
        public int Quality { get; set; } = 2;

        /// <summary>
        /// Minimum confidence threshold for points (lower = more permissive)
        /// </summary>
        /// <summary>
        /// Minimum confidence score for accepting a dense point.
        /// Based on COLMAP's filter_min_ncc parameter (default: 0.1-0.3)
        /// Reference: COLMAP FAQ recommends reducing this for weakly textured surfaces
        /// For high-overlap sequences (>60%), use lower threshold to accept more points
        /// With proper image-space patch propagation, 0.10 works well for most cases
        /// </summary>
        public float ConfidenceThreshold { get; set; } = 0.10f;  // CRITICAL: Lowered to 0.10f for image-space propagation

        /// <summary>
        /// Whether to filter outlier points
        /// </summary>
        public bool FilterOutliers { get; set; } = true;
    }
}