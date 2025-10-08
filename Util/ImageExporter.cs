// GeoscientistToolkit/Util/ImageExporter.cs

using System.Runtime.InteropServices;
using Veldrid;

// Required for Marshal.Copy

namespace GeoscientistToolkit.Util;

public static class ImageExporter
{
    /// <summary>
    ///     Captures a region from a source texture and saves it as a BMP file.
    /// </summary>
    /// <param name="gd">The active GraphicsDevice.</param>
    /// <param name="cl">A command list to use for the copy operation.</param>
    /// <param name="source">The source texture (e.g., the swapchain framebuffer).</param>
    /// <param name="sourceRect">The rectangular region to capture from the source texture.</param>
    /// <param name="filePath">The path to save the BMP file.</param>
    public static void SaveTexture(GraphicsDevice gd, CommandList cl, Texture source, Rectangle sourceRect,
        string filePath)
    {
        if (gd == null || cl == null || source == null || string.IsNullOrEmpty(filePath))
        {
            Logger.LogError("[ImageExporter] Invalid arguments for SaveTexture.");
            return;
        }

        if (sourceRect.X < 0 || sourceRect.Y < 0 ||
            sourceRect.Width <= 0 || sourceRect.Height <= 0 ||
            sourceRect.Right > source.Width || sourceRect.Bottom > source.Height)
        {
            Logger.LogError("[ImageExporter] Invalid source rectangle for screenshot.");
            return;
        }

        Texture stagingTexture = null;
        try
        {
            stagingTexture = gd.ResourceFactory.CreateTexture(new TextureDescription(
                (uint)sourceRect.Width, (uint)sourceRect.Height, 1, 1, 1,
                source.Format, TextureUsage.Staging, TextureType.Texture2D));

            cl.Begin();
            cl.CopyTexture(
                source,
                (uint)sourceRect.X, (uint)sourceRect.Y, 0, 0, 0,
                stagingTexture,
                0, 0, 0, 0, 0,
                (uint)sourceRect.Width, (uint)sourceRect.Height, 1, 1);
            cl.End();

            gd.SubmitCommands(cl);
            gd.WaitForIdle();

            var mapped = gd.Map(stagingTexture, MapMode.Read);
            try
            {
                WriteBitmap(filePath, (int)stagingTexture.Width, (int)stagingTexture.Height, mapped);
                Logger.Log($"[ImageExporter] Screenshot saved to: {filePath}");
            }
            finally
            {
                gd.Unmap(stagingTexture);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ImageExporter] Failed to save texture: {ex.Message}");
        }
        finally
        {
            stagingTexture?.Dispose();
        }
    }

    /// <summary>
    ///     Writes raw pixel data to a 32-bit BMP file.
    ///     This method is marked as 'unsafe' to allow pointer operations.
    /// </summary>
    private static void WriteBitmap(string filePath, int width, int height, MappedResource mappedResource)
    {
        var stride = mappedResource.RowPitch;
        var bytesPerPixel = 4; // Assuming 32-bit format (BGRA8 or RGBA8)
        var dataSize = width * height * bytesPerPixel;

        using (var stream = new FileStream(filePath, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            // BMP File Header (14 bytes)
            writer.Write((ushort)0x4D42); // "BM"
            writer.Write(14 + 40 + dataSize); // File size
            writer.Write((ushort)0); // Reserved
            writer.Write((ushort)0); // Reserved
            writer.Write(14 + 40); // Pixel data offset

            // DIB Header (BITMAPINFOHEADER, 40 bytes)
            writer.Write(40); // Header size
            writer.Write(width);
            writer.Write(height);
            writer.Write((ushort)1); // Color planes
            writer.Write((ushort)(bytesPerPixel * 8)); // Bits per pixel
            writer.Write(0); // Compression method (0 = BI_RGB, uncompressed)
            writer.Write(dataSize); // Image size
            writer.Write(0); // Horizontal resolution
            writer.Write(0); // Vertical resolution
            writer.Write(0); // Colors in color palette
            writer.Write(0); // Important colors

            // Pixel Data
            // BMPs are stored bottom-to-top, so we write rows in reverse order.
            var rowBuffer = new byte[width * bytesPerPixel];
            for (var y = height - 1; y >= 0; y--)
            {
                // Calculate the pointer to the beginning of the current row in the source data.
                var sourcePtr = (IntPtr)(mappedResource.Data + y * stride);

                // *** THIS IS THE FIX ***
                // Copy a single row from the unmanaged memory (GPU) to our managed buffer.
                // This correctly reads the data and strips any padding.
                Marshal.Copy(sourcePtr, rowBuffer, 0, rowBuffer.Length);

                writer.Write(rowBuffer);
            }
        }
    }
}