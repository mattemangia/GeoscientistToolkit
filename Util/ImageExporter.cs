// GAIA/Util/ImageExporter.cs

using StbImageWriteSharp;

// Required for Marshal.Copy

namespace GAIA.Util;

public static class ImageExporter
{
    /// <summary>
    ///     Exports a grayscale slice to a file (PNG or TIF)
    /// </summary>
    public static void ExportGrayscaleSlice(byte[] data, int width, int height, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var writer = new ImageWriter();

        using var stream = File.Create(filePath);

        switch (extension)
        {
            case ".png":
                writer.WritePng(data, width, height, ColorComponents.Grey, stream);
                break;

            case ".tif":
            case ".tiff":
                // StbImageWrite doesn't support TIFF, so we'll save as PNG with .tif extension
                // For actual TIFF support, you'd need a different library like BitMiracle.LibTiff
                writer.WritePng(data, width, height, ColorComponents.Grey, stream);
                break;

            case ".bmp":
                writer.WriteBmp(data, width, height, ColorComponents.Grey, stream);
                break;

            case ".jpg":
            case ".jpeg":
                writer.WriteJpg(data, width, height, ColorComponents.Grey, stream, 95);
                break;

            default:
                throw new ArgumentException($"Unsupported image format: {extension}");
        }
    }

    /// <summary>
    ///     Saves an RGBA image to a file (convenience method)
    /// </summary>
    public static void SaveImage(byte[] data, int width, int height, string filePath)
    {
        ExportColorSlice(data, width, height, filePath);
    }

    /// <summary>
    ///     Exports a color slice (RGBA) to a file
    /// </summary>
    public static void ExportColorSlice(byte[] data, int width, int height, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var writer = new ImageWriter();

        using var stream = File.Create(filePath);

        switch (extension)
        {
            case ".png":
                writer.WritePng(data, width, height, ColorComponents.RedGreenBlueAlpha, stream);
                break;

            case ".bmp":
                writer.WriteBmp(data, width, height, ColorComponents.RedGreenBlueAlpha, stream);
                break;

            case ".jpg":
            case ".jpeg":
                writer.WriteJpg(data, width, height, ColorComponents.RedGreenBlueAlpha, stream, 95);
                break;

            default:
                throw new ArgumentException($"Unsupported image format: {extension}");
        }
    }

    /// <summary>
    ///     Exports a grayscale slice to a stream
    /// </summary>
    public static void ExportGrayscaleSliceToStream(byte[] data, int width, int height, Stream stream,
        string format = "png")
    {
        var writer = new ImageWriter();
        var formatLower = format.ToLowerInvariant().TrimStart('.');

        switch (formatLower)
        {
            case "png":
                writer.WritePng(data, width, height, ColorComponents.Grey, stream);
                break;

            case "bmp":
                writer.WriteBmp(data, width, height, ColorComponents.Grey, stream);
                break;

            case "jpg":
            case "jpeg":
                writer.WriteJpg(data, width, height, ColorComponents.Grey, stream, 95);
                break;

            default:
                throw new ArgumentException($"Unsupported image format: {format}");
        }
    }

}
