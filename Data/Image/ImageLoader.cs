// GeoscientistToolkit/Util/ImageLoader.cs
using System;
using System.IO;
using StbImageSharp;

namespace GeoscientistToolkit.Util
{
    public static class ImageLoader
    {
        static ImageLoader()
        {
            // Configure StbImageSharp to use our desired defaults
            StbImage.stbi_set_flip_vertically_on_load(0); // Don't flip by default
        }

        public class ImageInfo
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public int Channels { get; set; }
            public int BitsPerChannel { get; set; }
            public byte[] Data { get; set; }
        }

        /// <summary>
        /// Load image information without loading pixel data
        /// </summary>
        public static ImageInfo LoadImageInfo(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var info = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                return new ImageInfo
                {
                    Width = info.Width,
                    Height = info.Height,
                    Channels = 4, // We always request RGBA
                    BitsPerChannel = 8,
                    Data = null // Don't store data for info-only load
                };
            }
        }

        /// <summary>
        /// Load full image with pixel data
        /// </summary>
        public static ImageInfo LoadImage(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var result = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                return new ImageInfo
                {
                    Width = result.Width,
                    Height = result.Height,
                    Channels = 4, // We always request RGBA
                    BitsPerChannel = 8,
                    Data = result.Data
                };
            }
        }

        /// <summary>
        /// Create a thumbnail of the specified size
        /// </summary>
        public static ImageInfo CreateThumbnail(string path, int maxWidth, int maxHeight)
        {
            var fullImage = LoadImage(path);
            
            // Calculate thumbnail dimensions maintaining aspect ratio
            float scale = Math.Min((float)maxWidth / fullImage.Width, (float)maxHeight / fullImage.Height);
            int thumbWidth = (int)(fullImage.Width * scale);
            int thumbHeight = (int)(fullImage.Height * scale);
            
            // Simple nearest-neighbor downsampling (for better quality, consider a proper resampling algorithm)
            byte[] thumbData = new byte[thumbWidth * thumbHeight * 4];
            
            for (int y = 0; y < thumbHeight; y++)
            {
                for (int x = 0; x < thumbWidth; x++)
                {
                    int srcX = (int)(x / scale);
                    int srcY = (int)(y / scale);
                    int srcIdx = (srcY * fullImage.Width + srcX) * 4;
                    int dstIdx = (y * thumbWidth + x) * 4;
                    
                    // Copy RGBA
                    Array.Copy(fullImage.Data, srcIdx, thumbData, dstIdx, 4);
                }
            }
            
            return new ImageInfo
            {
                Width = thumbWidth,
                Height = thumbHeight,
                Channels = 4,
                BitsPerChannel = 8,
                Data = thumbData
            };
        }

        public static bool IsSupportedImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || 
                   ext == ".bmp" || ext == ".tga" || ext == ".psd" ||
                   ext == ".gif" || ext == ".hdr" || ext == ".pic" ||
                   ext == ".pnm" || ext == ".ppm" || ext == ".pgm";
        }
    }
}