// GeoscientistToolkit/Util/ImageLoader.cs

using BitMiracle.LibTiff.Classic;
using StbImageSharp;

namespace GeoscientistToolkit.Util;

public static class ImageLoader
{
    static ImageLoader()
    {
        // Configure StbImageSharp to use our desired defaults
        StbImage.stbi_set_flip_vertically_on_load(0); // Don't flip by default
    }

    /// <summary>
    ///     Load image information without loading pixel data
    /// </summary>
    public static ImageInfo LoadImageInfo(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".tif" || ext == ".tiff") return LoadTiffInfo(path);

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
    ///     Load full image with pixel data
    /// </summary>
    public static ImageInfo LoadImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".tif" || ext == ".tiff") return LoadTiff(path);

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
    ///     Load an image as color RGB/RGBA data.
    ///     Used for labeled volume import where colors represent materials.
    /// </summary>
    public static byte[] LoadColorImage(string path, out int width, out int height, out int channels)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".tif" || ext == ".tiff")
                // Use TIFF-specific loading
                using (var tiff = Tiff.Open(path, "r"))
                {
                    if (tiff == null)
                        throw new InvalidOperationException($"Failed to open TIFF file: {path}");

                    width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                    height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

                    // Read as RGBA
                    var raster = new int[width * height];
                    if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                        throw new InvalidOperationException("Failed to read TIFF image data");

                    // Convert int[] to byte[] RGBA
                    channels = 4;
                    var rgbaData = new byte[width * height * 4];
                    for (var i = 0; i < width * height; i++)
                    {
                        var pixel = raster[i];
                        rgbaData[i * 4] = (byte)((pixel >> 16) & 0xFF); // R
                        rgbaData[i * 4 + 1] = (byte)((pixel >> 8) & 0xFF); // G
                        rgbaData[i * 4 + 2] = (byte)(pixel & 0xFF); // B
                        rgbaData[i * 4 + 3] = (byte)((pixel >> 24) & 0xFF); // A
                    }

                    return rgbaData;
                }

            // Use StbImageSharp for other formats
            using (var stream = File.OpenRead(path))
            {
                StbImage.stbi_set_flip_vertically_on_load(0);
                var result = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                width = result.Width;
                height = result.Height;
                channels = 4; // RGBA

                return result.Data;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ImageLoader] Failed to load color image from {path}: {ex.Message}");
            width = height = channels = 0;
            return new byte[0];
        }
    }

    /// <summary>
    ///     Load a specific page from a multi-page TIFF as color data.
    ///     Used for labeled volume import where colors represent materials.
    /// </summary>
    public static byte[] LoadTiffPageAsColor(string path, int pageIndex, out int width, out int height,
        out int channels)
    {
        try
        {
            using (var tiff = Tiff.Open(path, "r"))
            {
                if (tiff == null)
                    throw new InvalidOperationException($"Failed to open TIFF file: {path}");

                // Navigate to the requested page
                if (!tiff.SetDirectory((short)pageIndex))
                    throw new ArgumentOutOfRangeException(nameof(pageIndex),
                        $"Page {pageIndex} does not exist in the TIFF file");

                width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
                channels = 4; // We'll always return RGBA

                // Read the image data as RGBA
                var raster = new int[width * height];
                if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                    throw new InvalidOperationException("Failed to read TIFF image data");

                // Convert int[] to byte[] RGBA
                var rgbaData = new byte[width * height * 4];
                for (var i = 0; i < width * height; i++)
                {
                    var pixel = raster[i];
                    // LibTiff stores as ABGR in int, we need RGBA in bytes
                    rgbaData[i * 4] = (byte)((pixel >> 16) & 0xFF); // R
                    rgbaData[i * 4 + 1] = (byte)((pixel >> 8) & 0xFF); // G
                    rgbaData[i * 4 + 2] = (byte)(pixel & 0xFF); // B
                    rgbaData[i * 4 + 3] = (byte)((pixel >> 24) & 0xFF); // A
                }

                return rgbaData;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ImageLoader] Failed to load TIFF page {pageIndex} as color from {path}: {ex.Message}");
            width = height = channels = 0;
            return new byte[0];
        }
    }

    /// <summary>
    ///     Load grayscale image data for CT stacks
    /// </summary>
    public static byte[] LoadGrayscaleImage(string path, out int width, out int height)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".tif" || ext == ".tiff") return LoadTiffAsGrayscale(path, out width, out height);

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

    /// <summary>
    ///     Check if a TIFF file contains multiple pages (for CT stacks)
    /// </summary>
    public static bool IsMultiPageTiff(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext != ".tif" && ext != ".tiff") return false;

        try
        {
            using (var tiff = Tiff.Open(path, "r"))
            {
                if (tiff == null) return false;

                var pageCount = 0;
                do
                {
                    pageCount++;
                    if (pageCount > 1) return true; // Early exit if we find more than one page
                } while (tiff.ReadDirectory());

                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Get the number of pages in a multi-page TIFF
    /// </summary>
    public static int GetTiffPageCount(string path)
    {
        using (var tiff = Tiff.Open(path, "r"))
        {
            if (tiff == null)
                throw new InvalidOperationException($"Failed to open TIFF file: {path}");

            var pageCount = 0;
            do
            {
                pageCount++;
            } while (tiff.ReadDirectory());

            return pageCount;
        }
    }

    /// <summary>
    ///     Load a specific page from a multi-page TIFF as grayscale
    /// </summary>
    public static byte[] LoadTiffPageAsGrayscale(string path, int pageIndex, out int width, out int height)
    {
        using (var tiff = Tiff.Open(path, "r"))
        {
            if (tiff == null)
                throw new InvalidOperationException($"Failed to open TIFF file: {path}");

            // Navigate to the requested page
            if (!tiff.SetDirectory((short)pageIndex))
                throw new ArgumentOutOfRangeException(nameof(pageIndex),
                    $"Page {pageIndex} does not exist in the TIFF file");

            width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            var bitsPerSample = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 8;
            var samplesPerPixel = tiff.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;

            var grayscaleData = new byte[width * height];

            // Check if it's already a grayscale image
            if (samplesPerPixel == 1)
            {
                // Direct grayscale reading
                if (bitsPerSample == 8)
                {
                    // 8-bit grayscale
                    var scanline = new byte[tiff.ScanlineSize()];
                    for (var row = 0; row < height; row++)
                    {
                        tiff.ReadScanline(scanline, row);
                        Array.Copy(scanline, 0, grayscaleData, row * width, width);
                    }
                }
                else if (bitsPerSample == 16)
                {
                    // 16-bit grayscale - read and convert to 8-bit
                    var scanline = new byte[tiff.ScanlineSize()];
                    for (var row = 0; row < height; row++)
                    {
                        tiff.ReadScanline(scanline, row);

                        // Convert 16-bit to 8-bit
                        for (var x = 0; x < width; x++)
                        {
                            var value16 = (ushort)(scanline[x * 2] | (scanline[x * 2 + 1] << 8));
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
                var raster = new int[width * height];

                if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                    throw new InvalidOperationException("Failed to read TIFF image data");

                // Convert int RGBA to grayscale
                for (var i = 0; i < width * height; i++)
                {
                    var pixel = raster[i];
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    // Standard grayscale conversion
                    grayscaleData[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                }
            }

            return grayscaleData;
        }
    }

    /// <summary>
    ///     Load all pages from a multi-page TIFF as a list of grayscale images
    /// </summary>
    public static List<byte[]> LoadAllTiffPagesAsGrayscale(string path, out int width, out int height,
        IProgress<float> progress = null)
    {
        var pages = new List<byte[]>();
        width = 0;
        height = 0;

        using (var tiff = Tiff.Open(path, "r"))
        {
            if (tiff == null)
                throw new InvalidOperationException($"Failed to open TIFF file: {path}");

            var pageIndex = 0;
            var totalPages = GetTiffPageCount(path);

            do
            {
                var pageWidth = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                var pageHeight = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

                // Set dimensions from first page
                if (pageIndex == 0)
                {
                    width = pageWidth;
                    height = pageHeight;
                }
                else if (pageWidth != width || pageHeight != height)
                {
                    throw new InvalidOperationException(
                        $"All pages must have the same dimensions. Page {pageIndex} has different dimensions.");
                }

                // Load this page
                var pageData = LoadTiffPageAsGrayscale(path, pageIndex, out _, out _);
                pages.Add(pageData);

                progress?.Report((float)(pageIndex + 1) / totalPages);
                pageIndex++;
            } while (tiff.ReadDirectory());
        }

        return pages;
    }

    private static ImageInfo LoadTiffInfo(string path)
    {
        using (var tiff = Tiff.Open(path, "r"))
        {
            if (tiff == null)
                throw new InvalidOperationException($"Failed to open TIFF file: {path}");

            var width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            var height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            var bitsPerSample = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 8;
            var samplesPerPixel = tiff.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;

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
        using (var tiff = Tiff.Open(path, "r"))
        {
            if (tiff == null)
                throw new InvalidOperationException($"Failed to open TIFF file: {path}");

            var width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            var height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            var bitsPerSample = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 8;
            var samplesPerPixel = tiff.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;

            // Read the image data - LibTiff expects int[] for RGBA
            var raster = new int[width * height];

            if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                throw new InvalidOperationException("Failed to read TIFF image data");

            // Convert int[] to byte[] RGBA
            var rgbaData = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++)
            {
                var pixel = raster[i];
                // LibTiff stores as ABGR in int, we need RGBA in bytes
                rgbaData[i * 4] = (byte)((pixel >> 16) & 0xFF); // R
                rgbaData[i * 4 + 1] = (byte)((pixel >> 8) & 0xFF); // G
                rgbaData[i * 4 + 2] = (byte)(pixel & 0xFF); // B
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
        using (var tiff = Tiff.Open(path, "r"))
        {
            if (tiff == null)
                throw new InvalidOperationException($"Failed to open TIFF file: {path}");

            width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            var bitsPerSample = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 8;
            var samplesPerPixel = tiff.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;

            var grayscaleData = new byte[width * height];

            // Check if it's already a grayscale image
            if (samplesPerPixel == 1)
            {
                // Direct grayscale reading
                if (bitsPerSample == 8)
                {
                    // 8-bit grayscale
                    var scanline = new byte[tiff.ScanlineSize()];
                    for (var row = 0; row < height; row++)
                    {
                        tiff.ReadScanline(scanline, row);
                        Array.Copy(scanline, 0, grayscaleData, row * width, width);
                    }
                }
                else if (bitsPerSample == 16)
                {
                    // 16-bit grayscale - read and convert to 8-bit
                    var scanline = new byte[tiff.ScanlineSize()];
                    for (var row = 0; row < height; row++)
                    {
                        tiff.ReadScanline(scanline, row);

                        // Convert 16-bit to 8-bit
                        for (var x = 0; x < width; x++)
                        {
                            var value16 = (ushort)(scanline[x * 2] | (scanline[x * 2 + 1] << 8));
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
                var raster = new int[width * height];

                if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                    throw new InvalidOperationException("Failed to read TIFF image data");

                // Convert int RGBA to grayscale
                for (var i = 0; i < width * height; i++)
                {
                    var pixel = raster[i];
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);

                    // Standard grayscale conversion
                    grayscaleData[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                }
            }

            return grayscaleData;
        }
    }

    private static byte[] ConvertToGrayscale(byte[] rgbaData, int width, int height)
    {
        var grayscale = new byte[width * height];

        for (var i = 0; i < width * height; i++)
        {
            var idx = i * 4;
            var r = rgbaData[idx];
            var g = rgbaData[idx + 1];
            var b = rgbaData[idx + 2];

            // Standard grayscale conversion
            grayscale[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
        }

        return grayscale;
    }

    /// <summary>
    ///     Create a thumbnail of the specified size
    /// </summary>
    public static ImageInfo CreateThumbnail(string path, int maxWidth, int maxHeight)
    {
        var fullImage = LoadImage(path);

        // Calculate thumbnail dimensions maintaining aspect ratio
        var scale = Math.Min((float)maxWidth / fullImage.Width, (float)maxHeight / fullImage.Height);
        var thumbWidth = (int)(fullImage.Width * scale);
        var thumbHeight = (int)(fullImage.Height * scale);

        // Simple nearest-neighbor downsampling
        var thumbData = new byte[thumbWidth * thumbHeight * 4];

        for (var y = 0; y < thumbHeight; y++)
        for (var x = 0; x < thumbWidth; x++)
        {
            var srcX = (int)(x / scale);
            var srcY = (int)(y / scale);
            var srcIdx = (srcY * fullImage.Width + srcX) * 4;
            var dstIdx = (y * thumbWidth + x) * 4;

            // Copy RGBA
            Array.Copy(fullImage.Data, srcIdx, thumbData, dstIdx, 4);
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
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" ||
               ext == ".bmp" || ext == ".tga" || ext == ".psd" ||
               ext == ".gif" || ext == ".hdr" || ext == ".pic" ||
               ext == ".pnm" || ext == ".ppm" || ext == ".pgm" ||
               ext == ".tif" || ext == ".tiff";
    }

    public class ImageInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Channels { get; set; }
        public int BitsPerChannel { get; set; }
        public byte[] Data { get; set; }
    }
}