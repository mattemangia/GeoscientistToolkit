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
        /// Minimum confidence threshold for points
        /// </summary>
        public float ConfidenceThreshold { get; set; } = 0.5f;

        /// <summary>
        /// Whether to filter outlier points
        /// </summary>
        public bool FilterOutliers { get; set; } = true;
    }
}