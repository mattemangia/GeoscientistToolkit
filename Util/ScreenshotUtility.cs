// GeoscientistToolkit/Util/ScreenshotUtility.cs
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using StbImageWriteSharp;
using Veldrid;

namespace GeoscientistToolkit.Util
{
    /// <summary>
    /// Utility class for capturing screenshots from ImGui windows and Veldrid render targets
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

        /// <summary>
        /// Captures a screenshot of a Veldrid texture/render target
        /// </summary>
        public static bool CaptureTexture(Texture texture, string filePath, ImageFormat format = ImageFormat.PNG, int jpegQuality = 90)
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
                    MappedResource mappedResource = gd.Map(stagingTexture, MapMode.Read, 0);
                    
                    try
                    {
                        int width = (int)texture.Width;
                        int height = (int)texture.Height;
                        int pixelSizeBytes = GetPixelSizeInBytes(texture.Format);
                        int rowPitch = (int)mappedResource.RowPitch;
                        
                        // Handle different pixel formats
                        byte[] imageData = ConvertToRGBA(mappedResource.Data, width, height, 
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
        /// Captures the current ImGui drawlist content (experimental - requires framebuffer access)
        /// </summary>
        public static bool CaptureImGuiWindow(string windowName, string filePath, ImageFormat format = ImageFormat.PNG)
        {
            // This would require accessing the underlying framebuffer that ImGui is rendering to
            // For now, we'll provide a helper that captures the main framebuffer region
            
            if (!ImGui.Begin(windowName))
            {
                ImGui.End();
                Logger.LogError($"[Screenshot] Window '{windowName}' not found");
                return false;
            }

            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            ImGui.End();

            // Capture main framebuffer region
            return CaptureFramebufferRegion(
                (int)windowPos.X, (int)windowPos.Y,
                (int)windowSize.X, (int)windowSize.Y,
                filePath, format);
        }

        /// <summary>
        /// Captures a region of the main framebuffer
        /// </summary>
        public static bool CaptureFramebufferRegion(int x, int y, int width, int height, 
                                                   string filePath, ImageFormat format = ImageFormat.PNG)
        {
            try
            {
                var gd = VeldridManager.GraphicsDevice;
                var swapchain = gd.MainSwapchain;
                
                // Get the backbuffer texture
                var backbuffer = swapchain.Framebuffer.ColorTargets[0].Target;
                
                // For now, capture the entire framebuffer
                // (Region capture would require additional render-to-texture setup)
                return CaptureTexture(backbuffer, filePath, format);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Screenshot] Failed to capture framebuffer region: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Shows a file save dialog and captures a screenshot
        /// </summary>
        public static void ShowScreenshotDialog(Texture texture, string defaultName = "screenshot")
        {
            var dialog = new GeoscientistToolkit.UI.Utils.ImGuiExportFileDialog(
                "ScreenshotDialog", "Save Screenshot");
            
            dialog.SetExtensions(
                (".png", "PNG Image"),
                (".jpg", "JPEG Image"),
                (".bmp", "Bitmap Image"),
                (".tga", "TGA Image")
            );
            
            dialog.Open(defaultName);
            
            // This would typically be handled in the UI update loop
            // Store the dialog reference for processing in the next frame
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
            byte[] result = new byte[width * height * 4];
            
            byte* srcPtr = (byte*)data.ToPointer();
            
            switch (format)
            {
                case PixelFormat.R8_G8_B8_A8_UNorm:
                case PixelFormat.R8_G8_B8_A8_UNorm_SRgb:
                    // Direct copy
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(new IntPtr(srcPtr + y * rowPitch), 
                                   result, y * width * 4, width * 4);
                    }
                    break;
                    
                case PixelFormat.B8_G8_R8_A8_UNorm:
                case PixelFormat.B8_G8_R8_A8_UNorm_SRgb:
                    // Swap R and B channels
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = y * rowPitch + x * 4;
                            int dstIdx = (y * width + x) * 4;
                            
                            result[dstIdx + 0] = srcPtr[srcIdx + 2]; // R
                            result[dstIdx + 1] = srcPtr[srcIdx + 1]; // G
                            result[dstIdx + 2] = srcPtr[srcIdx + 0]; // B
                            result[dstIdx + 3] = srcPtr[srcIdx + 3]; // A
                        }
                    }
                    break;
                    
                case PixelFormat.R8_UNorm:
                    // Grayscale to RGBA
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            byte gray = srcPtr[y * rowPitch + x];
                            int dstIdx = (y * width + x) * 4;
                            
                            result[dstIdx + 0] = gray;
                            result[dstIdx + 1] = gray;
                            result[dstIdx + 2] = gray;
                            result[dstIdx + 3] = 255;
                        }
                    }
                    break;
                    
                default:
                    // Fallback: copy what we can
                    int bytesPerPixel = GetPixelSizeInBytes(format);
                    int copySize = Math.Min(bytesPerPixel, 4);
                    
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = y * rowPitch + x * bytesPerPixel;
                            int dstIdx = (y * width + x) * 4;
                            
                            for (int i = 0; i < copySize; i++)
                            {
                                result[dstIdx + i] = srcPtr[srcIdx + i];
                            }
                            
                            // Fill alpha if not present
                            if (copySize < 4)
                            {
                                result[dstIdx + 3] = 255;
                            }
                        }
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
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

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
        /// Captures multiple screenshots with automatic naming
        /// </summary>
        public static void BatchCapture(Texture[] textures, string baseDirectory, 
                                       string prefix = "capture", ImageFormat format = ImageFormat.PNG)
        {
            if (!Directory.Exists(baseDirectory))
            {
                Directory.CreateDirectory(baseDirectory);
            }

            string extension = format switch
            {
                ImageFormat.PNG => ".png",
                ImageFormat.JPEG => ".jpg",
                ImageFormat.BMP => ".bmp",
                ImageFormat.TGA => ".tga",
                _ => ".png"
            };

            for (int i = 0; i < textures.Length; i++)
            {
                string fileName = $"{prefix}_{i:D4}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
                string filePath = Path.Combine(baseDirectory, fileName);
                
                CaptureTexture(textures[i], filePath, format);
            }
        }

        /// <summary>
        /// Creates a timestamped filename
        /// </summary>
        public static string GenerateTimestampedFilename(string prefix = "screenshot", 
                                                        ImageFormat format = ImageFormat.PNG)
        {
            string extension = format switch
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
    /// Helper class for managing screenshot sessions
    /// </summary>
    public class ScreenshotSession : IDisposable
    {
        private readonly string _sessionDirectory;
        private int _captureIndex = 0;
        private readonly ScreenshotUtility.ImageFormat _defaultFormat;

        public ScreenshotSession(string baseDirectory = null, ScreenshotUtility.ImageFormat defaultFormat = ScreenshotUtility.ImageFormat.PNG)
        {
            _defaultFormat = defaultFormat;
            
            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "GeoscientistToolkit_Screenshots");
            }
            
            _sessionDirectory = Path.Combine(baseDirectory, 
                $"Session_{DateTime.Now:yyyyMMdd_HHmmss}");
            
            Directory.CreateDirectory(_sessionDirectory);
            
            Logger.Log($"[Screenshot] Session started in {_sessionDirectory}");
        }

        public string Capture(Texture texture, string customName = null)
        {
            string fileName = customName ?? $"capture_{_captureIndex:D4}";
            string extension = _defaultFormat.ToString().ToLower();
            
            if (!fileName.EndsWith($".{extension}"))
            {
                fileName += $".{extension}";
            }
            
            string filePath = Path.Combine(_sessionDirectory, fileName);
            
            if (ScreenshotUtility.CaptureTexture(texture, filePath, _defaultFormat))
            {
                _captureIndex++;
                return filePath;
            }
            
            return null;
        }

        public void Dispose()
        {
            Logger.Log($"[Screenshot] Session ended. {_captureIndex} screenshots captured.");
        }
    }
}