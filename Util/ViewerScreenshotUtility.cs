// GeoscientistToolkit/Util/ViewerScreenshotUtility.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using StbImageWriteSharp;
using Veldrid;

namespace GeoscientistToolkit.Util
{
    /// <summary>
    /// Specialized screenshot utility for capturing viewer content including ImGui overlays
    /// </summary>
    public static class ViewerScreenshotUtility
    {
        // Temporary render target for composite capture
        private static Texture _compositeTexture;
        private static Framebuffer _compositeFramebuffer;
        private static CommandList _captureCommandList;
        private static Pipeline _blitPipeline;
        private static ResourceSet _blitResourceSet;
        private static DeviceBuffer _screenQuadVB;
        private static DeviceBuffer _screenQuadIB;
        private static bool _resourcesInitialized = false;

        /// <summary>
        /// Captures a viewer's content including all ImGui overlays
        /// This should be called AFTER the viewer has rendered its content and overlays
        /// </summary>
        public static bool CaptureViewerWithOverlays(
            Texture viewerRenderTexture,
            Vector2 viewerScreenPos,
            Vector2 viewerSize,
            string filePath,
            ScreenshotUtility.ImageFormat format = ScreenshotUtility.ImageFormat.PNG,
            int jpegQuality = 90)
        {
            try
            {
                var gd = VeldridManager.GraphicsDevice;
                var factory = VeldridManager.Factory;
                
                // Initialize resources if needed
                if (!_resourcesInitialized)
                {
                    InitializeResources(factory);
                }

                // Create or resize composite texture to match viewer size
                uint width = (uint)Math.Max(1, (int)viewerSize.X);
                uint height = (uint)Math.Max(1, (int)viewerSize.Y);
                
                if (_compositeTexture == null || _compositeTexture.Width != width || _compositeTexture.Height != height)
                {
                    _compositeTexture?.Dispose();
                    _compositeFramebuffer?.Dispose();
                    
                    _compositeTexture = factory.CreateTexture(
                        TextureDescription.Texture2D(
                            width, height, 1, 1,
                            PixelFormat.R8_G8_B8_A8_UNorm,
                            TextureUsage.RenderTarget | TextureUsage.Sampled));
                    
                    _compositeFramebuffer = factory.CreateFramebuffer(
                        new FramebufferDescription(null, _compositeTexture));
                }

                // Render the viewer content to composite texture
                RenderViewerToComposite(viewerRenderTexture);
                
                // Now render ImGui overlays on top
                RenderImGuiOverlaysToComposite(viewerScreenPos, viewerSize);
                
                // Finally capture the composite texture
                return ScreenshotUtility.CaptureTexture(_compositeTexture, filePath, format, jpegQuality);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ViewerScreenshot] Failed to capture viewer with overlays: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Alternative method: Deferred capture that happens next frame
        /// Call this to schedule a capture, then process it next frame
        /// </summary>
        public class DeferredCapture
        {
            public Texture SourceTexture { get; set; }
            public Vector2 ScreenPos { get; set; }
            public Vector2 Size { get; set; }
            public string FilePath { get; set; }
            public ScreenshotUtility.ImageFormat Format { get; set; }
            public Action<bool, string> Callback { get; set; }
            
            // For capturing specific ImGui windows
            public List<string> WindowIds { get; set; } = new List<string>();
            public bool CaptureAllOverlays { get; set; } = true;
        }

        private static Queue<DeferredCapture> _deferredCaptures = new Queue<DeferredCapture>();

        /// <summary>
        /// Schedules a viewer capture for the next frame
        /// This ensures ImGui content is fully rendered
        /// </summary>
        public static void ScheduleViewerCapture(
            Texture viewerRenderTexture,
            Vector2 viewerScreenPos,
            Vector2 viewerSize,
            string filePath,
            ScreenshotUtility.ImageFormat format = ScreenshotUtility.ImageFormat.PNG,
            Action<bool, string> callback = null)
        {
            _deferredCaptures.Enqueue(new DeferredCapture
            {
                SourceTexture = viewerRenderTexture,
                ScreenPos = viewerScreenPos,
                Size = viewerSize,
                FilePath = filePath,
                Format = format,
                Callback = callback
            });
        }
        
        /// <summary>
        /// Simpler version for scheduling capture without needing the texture reference
        /// </summary>
        public static void ScheduleRegionCapture(
            Vector2 viewerScreenPos,
            Vector2 viewerSize,
            string filePath,
            ScreenshotUtility.ImageFormat format = ScreenshotUtility.ImageFormat.PNG,
            Action<bool, string> callback = null)
        {
            _deferredCaptures.Enqueue(new DeferredCapture
            {
                SourceTexture = null, // We'll capture from main framebuffer
                ScreenPos = viewerScreenPos,
                Size = viewerSize,
                FilePath = filePath,
                Format = format,
                Callback = callback
            });
        }

        /// <summary>
        /// Process any pending deferred captures
        /// Call this at the END of your render frame, after ImGui.Render()
        /// </summary>
        public static void ProcessDeferredCaptures()
        {
            while (_deferredCaptures.Count > 0)
            {
                var capture = _deferredCaptures.Dequeue();
                
                // Use the main window region capture method
                bool success = CaptureMainWindowRegion(
                    capture.ScreenPos,
                    capture.Size,
                    capture.FilePath,
                    capture.Format);
                
                capture.Callback?.Invoke(success, capture.FilePath);
            }
        }

        private static void InitializeResources(ResourceFactory factory)
        {
            _captureCommandList = factory.CreateCommandList();
            
            // Create screen quad for blitting
            var vertices = new[]
            {
                new Vector2(-1, -1), new Vector2(0, 1),  // Bottom-left
                new Vector2(1, -1),  new Vector2(1, 1),  // Bottom-right
                new Vector2(-1, 1),  new Vector2(0, 0),  // Top-left
                new Vector2(1, 1),   new Vector2(1, 0),  // Top-right
            };
            
            var indices = new ushort[] { 0, 1, 2, 2, 1, 3 };
            
            _screenQuadVB = factory.CreateBuffer(
                new BufferDescription((uint)(vertices.Length * sizeof(float) * 2), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_screenQuadVB, 0, vertices);
            
            _screenQuadIB = factory.CreateBuffer(
                new BufferDescription((uint)(indices.Length * sizeof(ushort)), BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_screenQuadIB, 0, indices);
            
            _resourcesInitialized = true;
        }

        private static void RenderViewerToComposite(Texture viewerTexture)
        {
            // For now, we'll use a simple copy operation
            // In a more complete implementation, you'd set up a blit pipeline
            
            var gd = VeldridManager.GraphicsDevice;
            _captureCommandList.Begin();
            _captureCommandList.CopyTexture(
                viewerTexture, 0, 0, 0, 0, 0,
                _compositeTexture, 0, 0, 0, 0, 0,
                Math.Min(viewerTexture.Width, _compositeTexture.Width),
                Math.Min(viewerTexture.Height, _compositeTexture.Height),
                1, 1);
            _captureCommandList.End();
            gd.SubmitCommands(_captureCommandList);
            gd.WaitForIdle();
        }

        private static void RenderImGuiOverlaysToComposite(Vector2 screenPos, Vector2 size)
        {
            // This is the tricky part - we need to capture ImGui draw data
            // and render it to our composite texture
            
            // One approach is to use a secondary ImGui render pass
            // targeting our composite framebuffer
            
            // For now, this is a placeholder - the actual implementation
            // would need to hook into ImGui's rendering
        }

        /// <summary>
        /// Simplified method for capturing viewer content from the main window
        /// This reads directly from the main framebuffer where everything is already rendered
        /// Call this method with a slight delay after rendering to ensure content is ready
        /// </summary>
        public static bool CaptureMainWindowRegion(
            Vector2 viewerScreenPos,
            Vector2 viewerSize,
            string filePath,
            ScreenshotUtility.ImageFormat format = ScreenshotUtility.ImageFormat.PNG,
            int jpegQuality = 90)
        {
            try
            {
                var gd = VeldridManager.GraphicsDevice;
                var factory = VeldridManager.Factory;
                var swapchain = gd.MainSwapchain;
                
                // Get the current backbuffer/framebuffer
                var framebuffer = swapchain.Framebuffer;
                var backbuffer = framebuffer.ColorTargets[0].Target;
                
                // Calculate the region to capture
                uint srcX = (uint)Math.Max(0, (int)viewerScreenPos.X);
                uint srcY = (uint)Math.Max(0, (int)viewerScreenPos.Y);
                uint width = (uint)Math.Min(backbuffer.Width - srcX, (int)viewerSize.X);
                uint height = (uint)Math.Min(backbuffer.Height - srcY, (int)viewerSize.Y);
                
                if (width == 0 || height == 0)
                {
                    Logger.LogError("[ViewerScreenshot] Invalid capture region dimensions");
                    return false;
                }
                
                // Create staging texture for readback
                var stagingTexture = factory.CreateTexture(
                    TextureDescription.Texture2D(
                        width, height, 1, 1,
                        PixelFormat.R8_G8_B8_A8_UNorm,
                        TextureUsage.Staging));
                
                // Create command list if needed
                if (_captureCommandList == null)
                {
                    _captureCommandList = factory.CreateCommandList();
                }
                
                // Copy the region from backbuffer to staging
                _captureCommandList.Begin();
                
                // Note: We need to handle the coordinate system correctly
                // ImGui uses top-left origin, but the framebuffer might be flipped
                _captureCommandList.CopyTexture(
                    backbuffer, 
                    srcX, srcY, 0,  // source x, y, z
                    0, 0,           // source mip level, array layer
                    stagingTexture, 
                    0, 0, 0,        // dest x, y, z
                    0, 0,           // dest mip level, array layer
                    width, height,  // width, height
                    1, 1);          // depth, layer count
                
                _captureCommandList.End();
                gd.SubmitCommands(_captureCommandList);
                gd.WaitForIdle();
                
                // Read back the staging texture
                bool result = ReadAndSaveTexture(stagingTexture, filePath, format, jpegQuality);
                
                // Clean up
                stagingTexture.Dispose();
                
                return result;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ViewerScreenshot] Failed to capture main window region: {ex.Message}");
                return false;
            }
        }
        
        private static bool ReadAndSaveTexture(Texture texture, string filePath,
            ScreenshotUtility.ImageFormat format, int jpegQuality)
        {
            var gd = VeldridManager.GraphicsDevice;
            
            // Map the staging texture for reading
            var mappedResource = gd.Map(texture, MapMode.Read, 0);
            
            try
            {
                int width = (int)texture.Width;
                int height = (int)texture.Height;
                int rowPitch = (int)mappedResource.RowPitch;
                
                // Allocate buffer for final image data
                byte[] imageData = new byte[width * height * 4];
                
                unsafe
                {
                    byte* srcPtr = (byte*)mappedResource.Data.ToPointer();
                    
                    // Copy and potentially flip the image vertically
                    // (depending on the graphics API coordinate system)
                    for (int y = 0; y < height; y++)
                    {
                        // Note: We might need to flip Y depending on the backend
                        int srcY = y; // or (height - 1 - y) for flipped
                        
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = srcY * rowPitch + x * 4;
                            int dstIdx = (y * width + x) * 4;
                            
                            // Copy RGBA data
                            imageData[dstIdx + 0] = srcPtr[srcIdx + 0]; // R
                            imageData[dstIdx + 1] = srcPtr[srcIdx + 1]; // G
                            imageData[dstIdx + 2] = srcPtr[srcIdx + 2]; // B
                            imageData[dstIdx + 3] = srcPtr[srcIdx + 3]; // A
                        }
                    }
                }
                
                // Save the image
                return SaveImage(imageData, width, height, filePath, format, jpegQuality);
            }
            finally
            {
                gd.Unmap(texture, 0);
            }
        }

        private static bool CaptureRegionDirectly(Texture texture, string filePath, 
            ScreenshotUtility.ImageFormat format, int jpegQuality)
        {
            var gd = VeldridManager.GraphicsDevice;
            
            // Map and read the texture data
            var mappedResource = gd.Map(texture, MapMode.Read, 0);
            
            try
            {
                int width = (int)texture.Width;
                int height = (int)texture.Height;
                int rowPitch = (int)mappedResource.RowPitch;
                
                // Convert to RGBA
                byte[] imageData = new byte[width * height * 4];
                
                unsafe
                {
                    byte* srcPtr = (byte*)mappedResource.Data.ToPointer();
                    
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = y * rowPitch + x * 4;
                            int dstIdx = (y * width + x) * 4;
                            
                            // Assuming RGBA format
                            imageData[dstIdx + 0] = srcPtr[srcIdx + 0]; // R
                            imageData[dstIdx + 1] = srcPtr[srcIdx + 1]; // G
                            imageData[dstIdx + 2] = srcPtr[srcIdx + 2]; // B
                            imageData[dstIdx + 3] = srcPtr[srcIdx + 3]; // A
                        }
                    }
                }
                
                // Save the image
                return SaveImage(imageData, width, height, filePath, format, jpegQuality);
            }
            finally
            {
                gd.Unmap(texture, 0);
            }
        }

        private static bool SaveImage(byte[] imageData, int width, int height,
            string filePath, ScreenshotUtility.ImageFormat format, int jpegQuality)
        {
            try
            {
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
                        case ScreenshotUtility.ImageFormat.PNG:
                            writer.WritePng(imageData, width, height,
                                ColorComponents.RedGreenBlueAlpha, stream);
                            break;
                        case ScreenshotUtility.ImageFormat.JPEG:
                            writer.WriteJpg(imageData, width, height,
                                ColorComponents.RedGreenBlueAlpha, stream, jpegQuality);
                            break;
                        case ScreenshotUtility.ImageFormat.BMP:
                            writer.WriteBmp(imageData, width, height,
                                ColorComponents.RedGreenBlueAlpha, stream);
                            break;
                        case ScreenshotUtility.ImageFormat.TGA:
                            writer.WriteTga(imageData, width, height,
                                ColorComponents.RedGreenBlueAlpha, stream);
                            break;
                    }
                }
                
                Logger.Log($"[ViewerScreenshot] Saved to {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ViewerScreenshot] Failed to save image: {ex.Message}");
                return false;
            }
        }

        public static void Dispose()
        {
            _compositeTexture?.Dispose();
            _compositeFramebuffer?.Dispose();
            _captureCommandList?.Dispose();
            _blitPipeline?.Dispose();
            _blitResourceSet?.Dispose();
            _screenQuadVB?.Dispose();
            _screenQuadIB?.Dispose();
            _resourcesInitialized = false;
        }
    }
}