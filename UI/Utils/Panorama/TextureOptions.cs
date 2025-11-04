// GeoscientistToolkit/Business/Photogrammetry/Options/TextureOptions.cs

namespace GeoscientistToolkit.Business.Photogrammetry
{
    /// <summary>
    /// Options for texture generation
    /// </summary>
    public class TextureOptions
    {
        /// <summary>
        /// Size of the texture atlas in pixels
        /// </summary>
        public int TextureSize { get; set; } = 4096;

        /// <summary>
        /// Whether to blend textures from multiple images
        /// </summary>
        public bool BlendTextures { get; set; } = true;

        /// <summary>
        /// Whether to apply color correction
        /// </summary>
        public bool ColorCorrection { get; set; } = true;

        /// <summary>
        /// JPEG quality if saving as JPEG
        /// </summary>
        public int JpegQuality { get; set; } = 95;
    }
}