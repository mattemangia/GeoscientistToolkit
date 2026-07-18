// GAIA/Util/SimpleTiffWriter.cs

namespace GAIA.Util;

/// <summary>
///     A simple TIFF writer for 8-bit grayscale and RGBA images.
///     Supports uncompressed TIFF format only.
/// </summary>
public static class SimpleTiffWriter
{
    // TIFF constants
    private const ushort TIFF_MAGIC_LITTLE_ENDIAN = 0x4949; // "II"
    private const ushort TIFF_VERSION = 42;

    // TIFF tag constants
    private const ushort TAG_IMAGE_WIDTH = 256;
    private const ushort TAG_IMAGE_HEIGHT = 257;
    private const ushort TAG_BITS_PER_SAMPLE = 258;
    private const ushort TAG_COMPRESSION = 259;
    private const ushort TAG_PHOTOMETRIC_INTERPRETATION = 262;
    private const ushort TAG_STRIP_OFFSETS = 273;
    private const ushort TAG_SAMPLES_PER_PIXEL = 277;
    private const ushort TAG_ROWS_PER_STRIP = 278;
    private const ushort TAG_STRIP_BYTE_COUNTS = 279;
    private const ushort TAG_X_RESOLUTION = 282;
    private const ushort TAG_Y_RESOLUTION = 283;
    private const ushort TAG_RESOLUTION_UNIT = 296;

    // TIFF type constants
    private const ushort TYPE_BYTE = 1;
    private const ushort TYPE_SHORT = 3;
    private const ushort TYPE_LONG = 4;
    private const ushort TYPE_RATIONAL = 5;

    // Photometric interpretation constants
    private const ushort PHOTOMETRIC_MINISBLACK = 1;
    private const ushort PHOTOMETRIC_RGB = 2;

    public static void WriteTiff(string filePath, byte[] rgbaData, int width, int height, bool isGrayscale = false)
    {
        using (var stream = File.Create(filePath))
        {
            WriteTiff(stream, rgbaData, width, height, isGrayscale);
        }
    }

    /// <summary>
    ///     Writes raw 8-bit single-channel grayscale data (one byte per pixel) as an uncompressed TIFF.
    /// </summary>
    public static void WriteTiffGrayscale8(string filePath, byte[] grayData, int width, int height)
    {
        using (var stream = File.Create(filePath))
        {
            WriteTiffCore(stream, grayData, width, height, 1);
        }
    }

    public static void WriteTiff(Stream stream, byte[] rgbaData, int width, int height, bool isGrayscale = false)
    {
        var samplesPerPixel = isGrayscale ? 1 : 4;

        // Convert RGBA to target format if needed
        byte[] imageData;
        if (isGrayscale)
        {
            // Convert RGBA to grayscale
            imageData = new byte[width * height];
            for (var i = 0; i < width * height; i++)
            {
                // Use simple average for grayscale conversion
                int r = rgbaData[i * 4];
                int g = rgbaData[i * 4 + 1];
                int b = rgbaData[i * 4 + 2];
                imageData[i] = (byte)((r + g + b) / 3);
            }
        }
        else
        {
            imageData = rgbaData;
        }

        WriteTiffCore(stream, imageData, width, height, samplesPerPixel);
    }

    private static void WriteTiffCore(Stream stream, byte[] imageData, int width, int height, int samplesPerPixel)
    {
        using (var writer = new BinaryWriter(stream))
        {
            var isGrayscale = samplesPerPixel == 1;
            var bytesPerRow = width * samplesPerPixel;
            var imageDataSize = height * bytesPerRow;

            // Write TIFF header
            writer.Write(TIFF_MAGIC_LITTLE_ENDIAN);
            writer.Write(TIFF_VERSION);
            writer.Write((uint)8); // Offset to first IFD

            // Write IFD (Image File Directory)
            var tagCount = 12; // Number of directory entries
            writer.Write((ushort)tagCount);

            // IFD offset = 8 (header) + 2 (entry count) + (12 * entry size) + 4 (next IFD offset)
            var dataOffset = (uint)(8 + 2 + tagCount * 12 + 4);

            // Out-of-line data layout after the IFD: the RGBA bits-per-sample array (8 bytes,
            // only when samplesPerPixel == 4), then the resolution rational (8 bytes), then
            // the image data.
            var bitsArrayBytes = samplesPerPixel == 4 ? 8u : 0u;
            var resolutionOffset = dataOffset + bitsArrayBytes;
            var stripOffset = resolutionOffset + 8;

            // Write directory entries
            WriteDirectoryEntry(writer, TAG_IMAGE_WIDTH, TYPE_LONG, 1, (uint)width);
            WriteDirectoryEntry(writer, TAG_IMAGE_HEIGHT, TYPE_LONG, 1, (uint)height);
            WriteDirectoryEntry(writer, TAG_BITS_PER_SAMPLE, TYPE_SHORT, (uint)samplesPerPixel,
                samplesPerPixel == 1 ? 8u : dataOffset);
            WriteDirectoryEntry(writer, TAG_COMPRESSION, TYPE_SHORT, 1, 1); // No compression
            WriteDirectoryEntry(writer, TAG_PHOTOMETRIC_INTERPRETATION, TYPE_SHORT, 1,
                isGrayscale ? PHOTOMETRIC_MINISBLACK : PHOTOMETRIC_RGB);
            WriteDirectoryEntry(writer, TAG_STRIP_OFFSETS, TYPE_LONG, 1, stripOffset);
            WriteDirectoryEntry(writer, TAG_SAMPLES_PER_PIXEL, TYPE_SHORT, 1, (uint)samplesPerPixel);
            WriteDirectoryEntry(writer, TAG_ROWS_PER_STRIP, TYPE_LONG, 1, (uint)height);
            WriteDirectoryEntry(writer, TAG_STRIP_BYTE_COUNTS, TYPE_LONG, 1, (uint)imageDataSize);
            WriteDirectoryEntry(writer, TAG_X_RESOLUTION, TYPE_RATIONAL, 1, resolutionOffset);
            WriteDirectoryEntry(writer, TAG_Y_RESOLUTION, TYPE_RATIONAL, 1, resolutionOffset);
            WriteDirectoryEntry(writer, TAG_RESOLUTION_UNIT, TYPE_SHORT, 1, 2); // Inches

            // Write next IFD offset (0 = no more IFDs)
            writer.Write((uint)0);

            // Write data values that didn't fit in directory entries
            if (samplesPerPixel == 4)
            {
                // Bits per sample for RGBA
                writer.Write((ushort)8); // R
                writer.Write((ushort)8); // G
                writer.Write((ushort)8); // B
                writer.Write((ushort)8); // A
            }

            // Write resolution (72 DPI as rational)
            writer.Write((uint)72); // Numerator
            writer.Write((uint)1); // Denominator

            // Write image data
            writer.Write(imageData);
        }
    }

    private static void WriteDirectoryEntry(BinaryWriter writer, ushort tag, ushort type, uint count,
        uint valueOrOffset)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(valueOrOffset);
    }
}