// GeoscientistToolkit/Business/Photogrammetry/Options/TextureOptions.cs

namespace GeoscientistToolkit.Business.Photogrammetry
{
    /// <summary>
    /// Options for texture generation
    /// </summary>
    public class TextureOptions
    {
        public enum UVMethod
        {
            /// <summary>
            /// Simple box projection (fast, higher distortion)
            /// </summary>
            BoxProjection,
            /// <summary>
            /// Cylindrical projection for cylindrical objects
            /// </summary>
            Cylindrical,
            /// <summary>
            /// Spherical projection for sphere-like objects
            /// </summary>
            Spherical,
            /// <summary>
            /// Angle-preserving projection minimizing angular distortion
            /// Reference: Lévy et al. (2002) - Least Squares Conformal Maps
            /// </summary>
            Conformal
        }

        /// <summary>
        /// Size of the texture atlas in pixels
        /// </summary>
        public int TextureSize { get; set; } = 4096;

        /// <summary>
        /// UV parameterization method
        /// </summary>
        public UVMethod ParameterizationMethod { get; set; } = UVMethod.BoxProjection;

        /// <summary>
        /// Whether to blend textures from multiple images
        /// </summary>
        public bool BlendTextures { get; set; } = true;

        /// <summary>
        /// Whether to apply color correction
        /// </summary>
        public bool ColorCorrection { get; set; } = true;

        /// <summary>
        /// Apply seam hiding techniques at UV boundaries
        /// </summary>
        public bool HideSeams { get; set; } = true;

        /// <summary>
        /// JPEG quality if saving as JPEG
        /// </summary>
        public int JpegQuality { get; set; } = 95;
    }
}