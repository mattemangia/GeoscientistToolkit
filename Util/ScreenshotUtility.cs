// GeoscientistToolkit/Util/ScreenshotUtility.cs

using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using StbImageWriteSharp;
using Veldrid;

namespace GeoscientistToolkit.Util;

/// <summary>
///     Utility class for capturing screenshots from ImGui windows and Veldrid render targets
/// </summary>
public static class ScreenshotUtility
{
    public enum ImageFormat
    {
        PNG,
        JPEG,
        BMP,
        TGA
    }

    private static readonly Dictionary<IntPtr, Texture> _windowTextures = new();
    private static readonly List<DeferredScreenshot> _deferredCaptures = new();

    /// <summary>
    ///     Captures a screenshot of a Veldrid texture/render target
    /// </summary>
    public static bool CaptureTexture(Texture texture, string filePath, ImageFormat format = ImageFormat.PNG,
        int jpegQuality = 90)
    {
        if (texture == null || string.IsNullOrEmpty(filePath))
        {
            Logger.LogError("[Screenshot] Invalid texture or file path");
            return false;
        }

        try
        {
            var gd = VeldridManager.GraphicsDevice;
            var factory = VeldridManager.Factory;

            // Create staging texture for readback
            var stagingDesc = TextureDescription.Texture2D(
                texture.Width, texture.Height, 1, 1,
                texture.Format, TextureUsage.Staging);

            using (var stagingTexture = factory.CreateTexture(stagingDesc))
            using (var cl = factory.CreateCommandList())
            {
                // Copy render texture to staging
                cl.Begin();
                cl.CopyTexture(texture, stagingTexture);
                cl.End();
                gd.SubmitCommands(cl);
                gd.WaitForIdle();

                // Read pixel data
                var mappedResource = gd.Map(stagingTexture, MapMode.Read, 0);

                try
                {
                    var width = (int)texture.Width;
                    var height = (int)texture.Height;
                    var pixelSizeBytes = GetPixelSizeInBytes(texture.Format);
                    var rowPitch = (int)mappedResource.RowPitch;

                    // Handle different pixel formats
                    var imageData = ConvertToRGBA(mappedResource.Data, width, height,
                        rowPitch, texture.Format);

                    // Save the image
                    return SaveImage(imageData, width, height, filePath, format, jpegQuality);
                }
                finally
                {
                    gd.Unmap(stagingTexture, 0);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Screenshot] Failed to capture texture: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Captures an ImGui window by its name
    /// </summary>
    public static bool CaptureImGuiWindow(string windowName, string filePath, ImageFormat format = ImageFormat.PNG)
    {
        try
        {
            // Get window rect from ImGui internal state
            Vector2 windowPos, windowSize;
            if (!GetImGuiWindowRect(windowName, out windowPos, out windowSize))
            {
                Logger.LogError($"[Screenshot] Could not get window rect for '{windowName}'");
                return false;
            }

            // Capture the framebuffer region corresponding to the window's rectangle
            return CaptureFramebufferRegion(
                (int)windowPos.X, (int)windowPos.Y,
                (int)windowSize.X, (int)windowSize.Y,
                filePath, format);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Screenshot] Failed to capture ImGui window: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Gets the position and size of an ImGui window
    /// </summary>
    public static bool GetImGuiWindowRect(string windowName, out Vector2 pos, out Vector2 size)
    {
        pos = Vector2.Zero;
        size = Vector2.Zero;

        // Use Begin/End to access window properties. This is a common ImGui pattern.
        // It might bring the window to focus, but it's the most reliable way to get its state.
        if (!ImGui.Begin(windowName))
        {
            ImGui.End(); // Ensure End is called even if Begin returns false
            return false;
        }

        pos = ImGui.GetWindowPos();
        size = ImGui.GetWindowSize();
        ImGui.End();
        return true;
    }

    /// <summary>
    ///     Captures the entire framebuffer
    /// </summary>
    public static bool CaptureFullFramebuffer(string filePath, ImageFormat format = ImageFormat.PNG)
    {
        try
        {
            var gd = VeldridManager.GraphicsDevice;
            var swapchain = gd.MainSwapchain;

            // Get the backbuffer texture
            var backbuffer = swapchain.Framebuffer.ColorTargets[0].Target;

            return CaptureTexture(backbuffer, filePath, format);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Screenshot] Failed to capture framebuffer: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Captures a region of the main framebuffer
    /// </summary>
    public static bool CaptureFramebufferRegion(int x, int y, int width, int height,
        string filePath, ImageFormat format = ImageFormat.PNG)
    {
        // Clamp dimensions to be within the framebuffer
        var gd = VeldridManager.GraphicsDevice;
        var swapchain = gd.MainSwapchain;
        var backbuffer = swapchain.Framebuffer.ColorTargets[0].Target;

        if (x < 0)
        {
            width += x;
            x = 0;
        }

        if (y < 0)
        {
            height += y;
            y = 0;
        }

        if (x + width > backbuffer.Width) width = (int)backbuffer.Width - x;
        if (y + height > backbuffer.Height) height = (int)backbuffer.Height - y;

        if (width <= 0 || height <= 0)
        {
            Logger.LogError("[Screenshot] Invalid capture region dimensions.");
            return false;
        }

        try
        {
            var factory = gd.ResourceFactory;

            // 1. Create a destination texture for the cropped image
            var cropDesc = TextureDescription.Texture2D(
                (uint)width, (uint)height, 1, 1,
                backbuffer.Format, TextureUsage.Sampled);

            using (var croppedTexture = factory.CreateTexture(cropDesc))
            using (var cl = factory.CreateCommandList())
            {
                // 2. Begin a command list to copy the texture region
                cl.Begin();

                // 3. Copy the specified region from the backbuffer to our new texture
                cl.CopyTexture(
                    backbuffer,
                    (uint)x, (uint)y, 0,
                    0, 0,
                    croppedTexture,
                    0, 0, 0,
                    0, 0,
                    (uint)width, (uint)height, 1,
                    1);

                cl.End();
                gd.SubmitCommands(cl);
                gd.WaitForIdle(); // Ensure the copy operation is complete

                // 4. Use the existing CaptureTexture utility on the new, smaller texture
                return CaptureTexture(croppedTexture, filePath, format);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Screenshot] Failed to capture framebuffer region: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Defers a screenshot capture until after the current frame is rendered
    /// </summary>
    public static void DeferScreenshotCapture(Texture texture, string filePath,
        ImageFormat format = ImageFormat.PNG,
        int jpegQuality = 90,
        Action<bool> callback = null)
    {
        _deferredCaptures.Add(new DeferredScreenshot
        {
            FilePath = filePath,
            Texture = texture,
            Format = format,
            JpegQuality = jpegQuality,
            Callback = callback
        });
    }

    /// <summary>
    ///     Processes any deferred screenshot captures
    /// </summary>
    public static void ProcessDeferredCaptures()
    {
        if (_deferredCaptures.Count == 0) return;

        var captures = new List<DeferredScreenshot>(_deferredCaptures);
        _deferredCaptures.Clear();

        foreach (var capture in captures)
        {
            var success = CaptureTexture(capture.Texture, capture.FilePath, capture.Format, capture.JpegQuality);
            capture.Callback?.Invoke(success);
        }
    }

    private class DeferredScreenshot
    {
        public string FilePath { get; set; }
        public Texture Texture { get; set; }
        public ImageFormat Format { get; set; }
        public int JpegQuality { get; set; }
        public Action<bool> Callback { get; set; }
    }

    #region Helper Methods

    private static int GetPixelSizeInBytes(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.R8_G8_B8_A8_UNorm => 4,
            PixelFormat.B8_G8_R8_A8_UNorm => 4,
            PixelFormat.R8_G8_B8_A8_UNorm_SRgb => 4,
            PixelFormat.B8_G8_R8_A8_UNorm_SRgb => 4,
            PixelFormat.R32_G32_B32_A32_Float => 16,
            PixelFormat.R8_UNorm => 1,
            PixelFormat.R8_G8_UNorm => 2,
            _ => 4 // Default to RGBA
        };
    }

    private static unsafe byte[] ConvertToRGBA(IntPtr data, int width, int height,
        int rowPitch, PixelFormat format)
    {
        var result = new byte[width * height * 4];

        var srcPtr = (byte*)data.ToPointer();

        switch (format)
        {
            case PixelFormat.R8_G8_B8_A8_UNorm:
            case PixelFormat.R8_G8_B8_A8_UNorm_SRgb:
                // Direct copy
                for (var y = 0; y < height; y++)
                    Marshal.Copy(new IntPtr(srcPtr + y * rowPitch),
                        result, y * width * 4, width * 4);
                break;

            case PixelFormat.B8_G8_R8_A8_UNorm:
            case PixelFormat.B8_G8_R8_A8_UNorm_SRgb:
                // Swap R and B channels
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var srcIdx = y * rowPitch + x * 4;
                    var dstIdx = (y * width + x) * 4;

                    result[dstIdx + 0] = srcPtr[srcIdx + 2]; // R
                    result[dstIdx + 1] = srcPtr[srcIdx + 1]; // G
                    result[dstIdx + 2] = srcPtr[srcIdx + 0]; // B
                    result[dstIdx + 3] = srcPtr[srcIdx + 3]; // A
                }

                break;

            case PixelFormat.R8_UNorm:
                // Grayscale to RGBA
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var gray = srcPtr[y * rowPitch + x];
                    var dstIdx = (y * width + x) * 4;

                    result[dstIdx + 0] = gray;
                    result[dstIdx + 1] = gray;
                    result[dstIdx + 2] = gray;
                    result[dstIdx + 3] = 255;
                }

                break;

            default:
                // Fallback: copy what we can
                var bytesPerPixel = GetPixelSizeInBytes(format);
                var copySize = Math.Min(bytesPerPixel, 4);

                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var srcIdx = y * rowPitch + x * bytesPerPixel;
                    var dstIdx = (y * width + x) * 4;

                    for (var i = 0; i < copySize; i++) result[dstIdx + i] = srcPtr[srcIdx + i];

                    // Fill alpha if not present
                    if (copySize < 4) result[dstIdx + 3] = 255;
                }

                break;
        }

        return result;
    }

    private static bool SaveImage(byte[] imageData, int width, int height,
        string filePath, ImageFormat format, int jpegQuality = 90)
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                var writer = new ImageWriter();

                switch (format)
                {
                    case ImageFormat.PNG:
                        writer.WritePng(imageData, width, height,
                            ColorComponents.RedGreenBlueAlpha, stream);
                        break;

                    case ImageFormat.JPEG:
                        writer.WriteJpg(imageData, width, height,
                            ColorComponents.RedGreenBlueAlpha, stream, jpegQuality);
                        break;

                    case ImageFormat.BMP:
                        writer.WriteBmp(imageData, width, height,
                            ColorComponents.RedGreenBlueAlpha, stream);
                        break;

                    case ImageFormat.TGA:
                        writer.WriteTga(imageData, width, height,
                            ColorComponents.RedGreenBlueAlpha, stream);
                        break;

                    default:
                        throw new NotSupportedException($"Image format {format} not supported");
                }
            }

            Logger.Log($"[Screenshot] Saved to {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Screenshot] Failed to save image: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Batch Operations

    /// <summary>
    ///     Captures multiple screenshots with automatic naming
    /// </summary>
    public static void BatchCapture(Texture[] textures, string baseDirectory,
        string prefix = "capture", ImageFormat format = ImageFormat.PNG)
    {
        if (!Directory.Exists(baseDirectory)) Directory.CreateDirectory(baseDirectory);

        var extension = format switch
        {
            ImageFormat.PNG => ".png",
            ImageFormat.JPEG => ".jpg",
            ImageFormat.BMP => ".bmp",
            ImageFormat.TGA => ".tga",
            _ => ".png"
        };

        for (var i = 0; i < textures.Length; i++)
        {
            var fileName = $"{prefix}_{i:D4}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
            var filePath = Path.Combine(baseDirectory, fileName);

            CaptureTexture(textures[i], filePath, format);
        }
    }

    /// <summary>
    ///     Creates a timestamped filename
    /// </summary>
    public static string GenerateTimestampedFilename(string prefix = "screenshot",
        ImageFormat format = ImageFormat.PNG)
    {
        var extension = format switch
        {
            ImageFormat.PNG => ".png",
            ImageFormat.JPEG => ".jpg",
            ImageFormat.BMP => ".bmp",
            ImageFormat.TGA => ".tga",
            _ => ".png"
        };

        return $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
    }

    #endregion
}

/// <summary>
///     Helper class for managing screenshot sessions
/// </summary>
public class ScreenshotSession : IDisposable
{
    private readonly ScreenshotUtility.ImageFormat _defaultFormat;
    private readonly string _sessionDirectory;
    private int _captureIndex;

    public ScreenshotSession(string baseDirectory = null,
        ScreenshotUtility.ImageFormat defaultFormat = ScreenshotUtility.ImageFormat.PNG)
    {
        _defaultFormat = defaultFormat;

        if (string.IsNullOrEmpty(baseDirectory))
            baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "GeoscientistToolkit_Screenshots");

        _sessionDirectory = Path.Combine(baseDirectory,
            $"Session_{DateTime.Now:yyyyMMdd_HHmmss}");

        Directory.CreateDirectory(_sessionDirectory);

        Logger.Log($"[Screenshot] Session started in {_sessionDirectory}");
    }

    public void Dispose()
    {
        Logger.Log($"[Screenshot] Session ended. {_captureIndex} screenshots captured.");
    }

    public string Capture(Texture texture, string customName = null)
    {
        var fileName = customName ?? $"capture_{_captureIndex:D4}";
        var extension = _defaultFormat.ToString().ToLower();

        if (!fileName.EndsWith($".{extension}")) fileName += $".{extension}";

        var filePath = Path.Combine(_sessionDirectory, fileName);

        if (ScreenshotUtility.CaptureTexture(texture, filePath, _defaultFormat))
        {
            _captureIndex++;
            return filePath;
        }

        return null;
    }
}