// GeoscientistToolkit/Util/ImageLoader.cs
using System;
using System.IO;
using StbImageSharp;
using BitMiracle.LibTiff.Classic;

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
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".tif" || ext == ".tiff")
            {
                return LoadTiffInfo(path);
            }

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
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".tif" || ext == ".tiff")
            {
                return LoadTiff(path);
            }

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
        /// Load grayscale image data for CT stacks
        /// </summary>
        public static byte[] LoadGrayscaleImage(string path, out int width, out int height)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".tif" || ext == ".tiff")
            {
                return LoadTiffAsGrayscale(path, out width, out height);
            }

            // For other formats, try to load as grayscale first
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var result = ImageResult.FromStream(stream, ColorComponents.Grey);
                    width = result.Width;
                    height = result.Height;
                    return result.Data;
                }
            }
            catch
            {
                // If grayscale loading fails, load as color and convert
                using (var stream = File.OpenRead(path))
                {
                    var result = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                    width = result.Width;
                    height = result.Height;
                    return ConvertToGrayscale(result.Data, width, height);
                }
            }
        }

        private static ImageInfo LoadTiffInfo(string path)
        {
            using (Tiff tiff = Tiff.Open(path, "r"))
            {
                if (tiff == null)
                    throw new InvalidOperationException($"Failed to open TIFF file: {path}");

                int width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                int height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
                int bitsPerSample = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 8;
                int samplesPerPixel = tiff.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;

                return new ImageInfo
                {
                    Width = width,
                    Height = height,
                    Channels = samplesPerPixel,
                    BitsPerChannel = bitsPerSample,
                    Data = null
                };
            }
        }

        private static ImageInfo LoadTiff(string path)
        {
            using (Tiff tiff = Tiff.Open(path, "r"))
            {
                if (tiff == null)
                    throw new InvalidOperationException($"Failed to open TIFF file: {path}");

                int width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                int height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
                int bitsPerSample = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 8;
                int samplesPerPixel = tiff.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;

                // Read the image data - LibTiff expects int[] for RGBA
                int[] raster = new int[width * height];

                if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                {
                    throw new InvalidOperationException("Failed to read TIFF image data");
                }

                // Convert int[] to byte[] RGBA
                byte[] rgbaData = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    int pixel = raster[i];
                    // LibTiff stores as ABGR in int, we need RGBA in bytes
                    rgbaData[i * 4] = (byte)((pixel >> 16) & 0xFF); // R
                    rgbaData[i * 4 + 1] = (byte)((pixel >> 8) & 0xFF);  // G
                    rgbaData[i * 4 + 2] = (byte)(pixel & 0xFF);         // B
                    rgbaData[i * 4 + 3] = (byte)((pixel >> 24) & 0xFF); // A
                }

                return new ImageInfo
                {
                    Width = width,
                    Height = height,
                    Channels = 4, // Always return as RGBA
                    BitsPerChannel = 8, // Normalized to 8-bit
                    Data = rgbaData
                };
            }
        }

        private static byte[] LoadTiffAsGrayscale(string path, out int width, out int height)
        {
            using (Tiff tiff = Tiff.Open(path, "r"))
            {
                if (tiff == null)
                    throw new InvalidOperationException($"Failed to open TIFF file: {path}");

                width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
                int bitsPerSample = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 8;
                int samplesPerPixel = tiff.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;

                byte[] grayscaleData = new byte[width * height];

                // Check if it's already a grayscale image
                if (samplesPerPixel == 1)
                {
                    // Direct grayscale reading
                    if (bitsPerSample == 8)
                    {
                        // 8-bit grayscale
                        byte[] scanline = new byte[tiff.ScanlineSize()];
                        for (int row = 0; row < height; row++)
                        {
                            tiff.ReadScanline(scanline, row);
                            Array.Copy(scanline, 0, grayscaleData, row * width, width);
                        }
                    }
                    else if (bitsPerSample == 16)
                    {
                        // 16-bit grayscale - read and convert to 8-bit
                        byte[] scanline = new byte[tiff.ScanlineSize()];
                        for (int row = 0; row < height; row++)
                        {
                            tiff.ReadScanline(scanline, row);

                            // Convert 16-bit to 8-bit
                            for (int x = 0; x < width; x++)
                            {
                                ushort value16 = (ushort)(scanline[x * 2] | (scanline[x * 2 + 1] << 8));
                                grayscaleData[row * width + x] = (byte)(value16 >> 8); // Take high byte
                            }
                        }
                    }
                    else
                    {
                        throw new NotSupportedException($"Unsupported bits per sample: {bitsPerSample}");
                    }
                }
                else
                {
                    // Color image - read as RGBA and convert to grayscale
                    int[] raster = new int[width * height];

                    if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                    {
                        throw new InvalidOperationException("Failed to read TIFF image data");
                    }

                    // Convert int RGBA to grayscale
                    for (int i = 0; i < width * height; i++)
                    {
                        int pixel = raster[i];
                        byte r = (byte)((pixel >> 16) & 0xFF);
                        byte g = (byte)((pixel >> 8) & 0xFF);
                        byte b = (byte)(pixel & 0xFF);

                        // Standard grayscale conversion
                        grayscaleData[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                    }
                }

                return grayscaleData;
            }
        }

        private static byte[] ConvertToGrayscale(byte[] rgbaData, int width, int height)
        {
            byte[] grayscale = new byte[width * height];

            for (int i = 0; i < width * height; i++)
            {
                int idx = i * 4;
                byte r = rgbaData[idx];
                byte g = rgbaData[idx + 1];
                byte b = rgbaData[idx + 2];

                // Standard grayscale conversion
                grayscale[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            }

            return grayscale;
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

            // Simple nearest-neighbor downsampling
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
                   ext == ".pnm" || ext == ".ppm" || ext == ".pgm" ||
                   ext == ".tif" || ext == ".tiff";
        }
    }
}