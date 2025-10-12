// GeoscientistToolkit/Util/ScreenshotUtility.cs

using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using StbImageWriteSharp;
using Veldrid;

namespace GeoscientistToolkit.Util;

/// <summary>
///     Utility class for capturing screenshots from ImGui windows and Veldrid render targets.
///     Note: Due to Metal/Vulkan limitations, screenshots must be taken BEFORE presenting the frame.
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

    private static readonly List<DeferredScreenshot> _deferredCaptures = new();
    private static Texture _lastFrameCapture;
    private static Framebuffer _captureFramebuffer;
    private static bool _shouldCaptureNextFrame;
    private static string _nextCapturePath;
    private static ImageFormat _nextCaptureFormat;
    private static int _captureX, _captureY, _captureWidth, _captureHeight;
    private static bool _captureFullFrame;

    /// <summary>
    ///     Call this at the START of your frame, before any rendering.
    ///     This prepares the capture render target if a screenshot is requested.
    /// </summary>
    public static void BeginFrame()
    {
        if (!_shouldCaptureNextFrame) return;

        var gd = VeldridManager.GraphicsDevice;
        var factory = gd.ResourceFactory;
        var swapchain = gd.MainSwapchain;

        var width = swapchain.Framebuffer.Width;
        var height = swapchain.Framebuffer.Height;

        // Recreate capture target if needed
        if (_lastFrameCapture == null ||
            _lastFrameCapture.Width != width ||
            _lastFrameCapture.Height != height)
        {
            _captureFramebuffer?.Dispose();
            _lastFrameCapture?.Dispose();

            var textureDesc = TextureDescription.Texture2D(
                width, height, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.RenderTarget | TextureUsage.Sampled);

            _lastFrameCapture = factory.CreateTexture(textureDesc);
            _captureFramebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, _lastFrameCapture));
        }
    }

    /// <summary>
    ///     Call this AFTER rendering your frame but BEFORE presenting.
    ///     This copies the backbuffer to our capture texture.
    /// </summary>
    public static void EndFrame(CommandList cl)
    {
        if (!_shouldCaptureNextFrame) return;

        try
        {
            var gd = VeldridManager.GraphicsDevice;
            var swapchain = gd.MainSwapchain;

            // Try to copy from swapchain backbuffer to our capture texture
            // This may fail on Metal - if it does, we'll handle it in ProcessDeferredCaptures
            try
            {
                var backbuffer = swapchain.Framebuffer.ColorTargets[0].Target;
                cl.CopyTexture(backbuffer, _lastFrameCapture);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Screenshot] Could not copy from backbuffer (expected on Metal): {ex.Message}");
                // Mark that we couldn't capture
                _shouldCaptureNextFrame = false;
                _deferredCaptures.Add(new DeferredScreenshot
                {
                    FilePath = _nextCapturePath,
                    Texture = null, // Signal that capture failed
                    Format = _nextCaptureFormat,
                    CaptureRect = new Rectangle(_captureX, _captureY, _captureWidth, _captureHeight),
                    IsFullFrame = _captureFullFrame
                });
                return;
            }

            // Mark that we've captured this frame
            _deferredCaptures.Add(new DeferredScreenshot
            {
                FilePath = _nextCapturePath,
                Texture = _lastFrameCapture,
                Format = _nextCaptureFormat,
                CaptureRect = new Rectangle(_captureX, _captureY, _captureWidth, _captureHeight),
                IsFullFrame = _captureFullFrame
            });

            _shouldCaptureNextFrame = false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Screenshot] EndFrame failed: {ex.Message}");
            _shouldCaptureNextFrame = false;
        }
    }

    /// <summary>
    ///     Captures a screenshot of a Veldrid texture/render target.
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
            if (!GetImGuiWindowRect(windowName, out var windowPos, out var windowSize))
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

        // Use Begin/End to access window properties
        if (!ImGui.Begin(windowName))
        {
            ImGui.End();
            return false;
        }

        pos = ImGui.GetWindowPos();
        size = ImGui.GetWindowSize();
        ImGui.End();
        return true;
    }

    /// <summary>
    ///     Captures the entire framebuffer.
    ///     NOTE: On Metal backend, this will show an error message to the user.
    /// </summary>
    public static bool CaptureFullFramebuffer(string filePath, ImageFormat format = ImageFormat.PNG)
    {
        var gd = VeldridManager.GraphicsDevice;

        // Check if we're on Metal backend
        if (gd.BackendType == GraphicsBackend.Metal)
        {
            Logger.LogError("[Screenshot] Full framebuffer capture is not supported on Metal backend.");
            Logger.LogError("[Screenshot] This is a limitation of the Metal graphics API.");
            Logger.LogError("[Screenshot] Please use the window screenshot tool to capture specific regions instead.");
            return false;
        }

        _shouldCaptureNextFrame = true;
        _nextCapturePath = filePath;
        _nextCaptureFormat = format;
        _captureFullFrame = true;
        _captureX = 0;
        _captureY = 0;
        _captureWidth = (int)gd.MainSwapchain.Framebuffer.Width;
        _captureHeight = (int)gd.MainSwapchain.Framebuffer.Height;

        Logger.Log("[Screenshot] Full framebuffer capture will occur on next frame");
        return true;
    }

    /// <summary>
    ///     Captures a region of the main framebuffer.
    ///     NOTE: On Metal backend, this will show an error message to the user.
    /// </summary>
    public static bool CaptureFramebufferRegion(int x, int y, int width, int height,
        string filePath, ImageFormat format = ImageFormat.PNG)
    {
        var gd = VeldridManager.GraphicsDevice;

        // Check if we're on Metal backend
        if (gd.BackendType == GraphicsBackend.Metal)
        {
            Logger.LogError("[Screenshot] Framebuffer capture is not supported on Metal backend (macOS).");
            Logger.LogError("[Screenshot] This is a limitation of how Metal handles swapchain textures.");
            Logger.LogError("[Screenshot] Possible workarounds:");
            Logger.LogError("[Screenshot]   1. Use Windows or Linux for screenshot functionality");
            Logger.LogError("[Screenshot]   2. Render to an offscreen texture first, then capture that");
            Logger.LogError("[Screenshot]   3. Use macOS's built-in Cmd+Shift+4 screenshot tool");
            return false;
        }

        var backbuffer = gd.MainSwapchain.Framebuffer.ColorTargets[0].Target;

        // Clamp dimensions
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

        _shouldCaptureNextFrame = true;
        _nextCapturePath = filePath;
        _nextCaptureFormat = format;
        _captureFullFrame = false;
        _captureX = x;
        _captureY = y;
        _captureWidth = width;
        _captureHeight = height;

        Logger.Log($"[Screenshot] Region capture will occur on next frame: {width}x{height} at ({x},{y})");
        return true;
    }

    /// <summary>
    ///     Processes any deferred screenshot captures.
    ///     Call this after WaitForIdle() to ensure the GPU has finished.
    /// </summary>
    public static void ProcessDeferredCaptures()
    {
        if (_deferredCaptures.Count == 0) return;

        var captures = new List<DeferredScreenshot>(_deferredCaptures);
        _deferredCaptures.Clear();

        foreach (var capture in captures)
        {
            if (capture.Texture == null)
            {
                Logger.LogError($"[Screenshot] Capture failed for {capture.FilePath}");
                capture.Callback?.Invoke(false);
                continue;
            }

            try
            {
                byte[] imageData;
                int finalWidth, finalHeight;

                if (capture.IsFullFrame)
                {
                    // Capture the entire texture
                    imageData = ReadTextureData(capture.Texture, out finalWidth, out finalHeight);
                }
                else
                {
                    // Capture a region
                    var fullData = ReadTextureData(capture.Texture, out var fullWidth, out var fullHeight);
                    if (fullData == null)
                    {
                        Logger.LogError($"[Screenshot] Failed to read texture data for {capture.FilePath}");
                        capture.Callback?.Invoke(false);
                        continue;
                    }

                    // Crop the region
                    var rect = capture.CaptureRect;
                    imageData = new byte[rect.Width * rect.Height * 4];
                    for (var row = 0; row < rect.Height; row++)
                    {
                        var srcY = rect.Y + row;
                        var srcOffset = (srcY * fullWidth + rect.X) * 4;
                        var dstOffset = row * rect.Width * 4;
                        Buffer.BlockCopy(fullData, srcOffset, imageData, dstOffset, rect.Width * 4);
                    }

                    finalWidth = rect.Width;
                    finalHeight = rect.Height;
                }

                var success = SaveImage(imageData, finalWidth, finalHeight, capture.FilePath, capture.Format,
                    capture.JpegQuality);
                capture.Callback?.Invoke(success);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Screenshot] Failed to process capture: {ex.Message}");
                capture.Callback?.Invoke(false);
            }
        }
    }

    private static byte[] ReadTextureData(Texture texture, out int width, out int height)
    {
        var gd = VeldridManager.GraphicsDevice;
        var factory = gd.ResourceFactory;

        width = (int)texture.Width;
        height = (int)texture.Height;

        var stagingDesc = TextureDescription.Texture2D(
            texture.Width, texture.Height, 1, 1,
            texture.Format, TextureUsage.Staging);

        using (var stagingTexture = factory.CreateTexture(stagingDesc))
        using (var cl = factory.CreateCommandList())
        {
            cl.Begin();
            cl.CopyTexture(texture, stagingTexture);
            cl.End();
            gd.SubmitCommands(cl);
            gd.WaitForIdle();

            var mapped = gd.Map(stagingTexture, MapMode.Read, 0);
            try
            {
                return ConvertToRGBA(mapped.Data, width, height, (int)mapped.RowPitch, texture.Format);
            }
            finally
            {
                gd.Unmap(stagingTexture, 0);
            }
        }
    }

    /// <summary>
    ///     Cleanup method to dispose of capture resources
    /// </summary>
    public static void Cleanup()
    {
        _captureFramebuffer?.Dispose();
        _captureFramebuffer = null;
        _lastFrameCapture?.Dispose();
        _lastFrameCapture = null;
    }

    private struct Rectangle
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;

        public Rectangle(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    private class DeferredScreenshot
    {
        public string FilePath { get; set; }
        public Texture Texture { get; set; }
        public ImageFormat Format { get; set; }
        public int JpegQuality { get; } = 90;
        public Action<bool> Callback { get; set; }
        public Rectangle CaptureRect { get; set; }
        public bool IsFullFrame { get; set; }
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
            _ => 4
        };
    }

    private static unsafe byte[] ConvertToRGBA(IntPtr data, int width, int height,
        int rowPitch, PixelFormat format)
    {
        var result = new byte[width * height * 4];
        var srcPtr = (byte*)data.ToPointer();

        if (rowPitch == width * 4 &&
            (format == PixelFormat.R8_G8_B8_A8_UNorm || format == PixelFormat.R8_G8_B8_A8_UNorm_SRgb))
        {
            Marshal.Copy(data, result, 0, result.Length);
            return result;
        }

        for (var y = 0; y < height; y++)
        {
            var srcRowOffset = y * rowPitch;
            var dstRowOffset = y * width * 4;

            switch (format)
            {
                case PixelFormat.R8_G8_B8_A8_UNorm:
                case PixelFormat.R8_G8_B8_A8_UNorm_SRgb:
                    Marshal.Copy(new IntPtr(srcPtr + srcRowOffset), result, dstRowOffset, width * 4);
                    break;

                case PixelFormat.B8_G8_R8_A8_UNorm:
                case PixelFormat.B8_G8_R8_A8_UNorm_SRgb:
                    for (var x = 0; x < width; x++)
                    {
                        var srcIdx = srcRowOffset + x * 4;
                        var dstIdx = dstRowOffset + x * 4;
                        result[dstIdx + 0] = srcPtr[srcIdx + 2]; // R
                        result[dstIdx + 1] = srcPtr[srcIdx + 1]; // G
                        result[dstIdx + 2] = srcPtr[srcIdx + 0]; // B
                        result[dstIdx + 3] = srcPtr[srcIdx + 3]; // A
                    }

                    break;

                case PixelFormat.R8_UNorm:
                    for (var x = 0; x < width; x++)
                    {
                        var gray = srcPtr[srcRowOffset + x];
                        var dstIdx = dstRowOffset + x * 4;
                        result[dstIdx + 0] = gray;
                        result[dstIdx + 1] = gray;
                        result[dstIdx + 2] = gray;
                        result[dstIdx + 3] = 255;
                    }

                    break;

                default:
                    var bytesPerPixel = GetPixelSizeInBytes(format);
                    var copySize = Math.Min(bytesPerPixel, 4);
                    for (var x = 0; x < width; x++)
                    {
                        var srcIdx = srcRowOffset + x * bytesPerPixel;
                        var dstIdx = dstRowOffset + x * 4;
                        for (var i = 0; i < copySize; i++) result[dstIdx + i] = srcPtr[srcIdx + i];
                        if (copySize < 4) result[dstIdx + 3] = 255;
                    }

                    break;
            }
        }

        return result;
    }

    private static bool SaveImage(byte[] imageData, int width, int height,
        string filePath, ImageFormat format, int jpegQuality = 90)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                var writer = new ImageWriter();
                var components = ColorComponents.RedGreenBlueAlpha;
                switch (format)
                {
                    case ImageFormat.PNG: writer.WritePng(imageData, width, height, components, stream); break;
                    case ImageFormat.JPEG:
                        writer.WriteJpg(imageData, width, height, components, stream, jpegQuality); break;
                    case ImageFormat.BMP: writer.WriteBmp(imageData, width, height, components, stream); break;
                    case ImageFormat.TGA: writer.WriteTga(imageData, width, height, components, stream); break;
                    default: throw new NotSupportedException($"Image format {format} not supported");
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

    public static void BatchCapture(Texture[] textures, string baseDirectory,
        string prefix = "capture", ImageFormat format = ImageFormat.PNG)
    {
        if (!Directory.Exists(baseDirectory)) Directory.CreateDirectory(baseDirectory);

        var extension = format.ToString().ToLower();

        for (var i = 0; i < textures.Length; i++)
        {
            var fileName = $"{prefix}_{i:D4}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
            var filePath = Path.Combine(baseDirectory, fileName);
            CaptureTexture(textures[i], filePath, format);
        }
    }

    public static string GenerateTimestampedFilename(string prefix = "screenshot",
        ImageFormat format = ImageFormat.PNG)
    {
        var extension = format.ToString().ToLower();
        return $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
    }

    #endregion
}

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