// GeoscientistToolkit/Data/Image/ImageViewer.cs
// A fully functional dataset viewer for single images, using Veldrid for GPU texture management.
// It supports panning, zooming, and displays a dynamic scale bar if pixel size is specified.

using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Globalization;
using System.Numerics;
using Veldrid;

namespace GeoscientistToolkit.Data.Image
{
    public class ImageViewer : IDatasetViewer
    {
        private readonly ImageDataset _dataset;

        // Veldrid resources
        private TextureView _textureView;
        private IntPtr _textureId = IntPtr.Zero;

        public ImageViewer(ImageDataset dataset)
        {
            _dataset = dataset;
        }

        /// <summary>
        /// Draws viewer-specific controls in the toolbar. None are needed for this viewer.
        /// </summary>
        public void DrawToolbarControls() { }

        /// <summary>
        /// Draws the main content of the viewer.
        /// </summary>
        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            // Lazily create the GPU texture on the first draw call.
            if (_textureId == IntPtr.Zero)
            {
                CreateDeviceTexture();
            }

            // If texture creation failed, display an error and exit.
            if (_textureId == IntPtr.Zero)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.2f, 0.2f, 1.0f), "Error: Could not create GPU texture for image.");
                return;
            }

            // Get drawing context
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            var dl = ImGui.GetWindowDrawList();

            // Draw a dark background for the viewport.
            dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);

            // Calculate the image's aspect ratio to maintain proportions.
            float aspectRatio = (float)_dataset.Width / _dataset.Height;

            // Fit image to the canvas while maintaining aspect ratio.
            Vector2 displaySize = new Vector2(canvasSize.X, canvasSize.X / aspectRatio);
            if (displaySize.Y > canvasSize.Y)
            {
                displaySize = new Vector2(canvasSize.Y * aspectRatio, canvasSize.Y);
            }

            // Apply zoom to the calculated display size.
            displaySize *= zoom;

            // Center the image within the canvas and apply the user's pan vector.
            Vector2 imagePos = canvasPos + (canvasSize - displaySize) * 0.5f + pan;

            // Add the image to ImGui's draw list.
            dl.AddImage(_textureId, imagePos, imagePos + displaySize);

            // If the dataset has a defined pixel size, draw the scale bar.
            if (_dataset.PixelSize > 0)
            {
                DrawScaleBar(dl, canvasPos, canvasSize, zoom);
            }
        }

        /// <summary>
        /// Loads the image from disk, creates a Veldrid texture, uploads the pixel data,
        /// and frees the CPU-side memory.
        /// </summary>
        private void CreateDeviceTexture()
        {
            // 1. Load the image file from disk into RAM.
            _dataset.Load();
            if (_dataset.ImageData == null)
            {
                // Logger.Log("Failed to load image from disk."); // Optional logging
                return;
            }
            var image = _dataset.ImageData;

            // 2. Create a Veldrid Texture on the GPU.
            Texture texture = VeldridManager.Factory.CreateTexture(TextureDescription.Texture2D(
                (uint)image.Width, (uint)image.Height, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

            // 3. Copy pixel data from ImageSharp to a byte array.
            byte[] pixelData = new byte[4 * image.Width * image.Height];
            image.CopyPixelDataTo(pixelData);

            // 4. Upload the byte array to the GPU Texture.
            VeldridManager.GraphicsDevice.UpdateTexture(
                texture,
                pixelData,
                0, 0, 0, (uint)image.Width, (uint)image.Height, 1, 0, 0);

            // 5. Create a TextureView and bind it for ImGui to use.
            _textureView = VeldridManager.Factory.CreateTextureView(texture);
            _textureId = VeldridManager.ImGuiController.GetOrCreateImGuiBinding(VeldridManager.Factory, _textureView);

            // 6. Free the RAM copy of the image now that it's on the GPU.
            _dataset.Unload();
        }

        /// <summary>
        /// Calculates and draws a dynamic scale bar in the bottom-right corner of the viewport.
        /// </summary>
        private void DrawScaleBar(ImDrawListPtr dl, Vector2 canvasPos, Vector2 canvasSize, float zoom)
        {
            // --- Configuration ---
            const float barHeight = 8f;
            const float textPadding = 4f;
            Vector2 margin = new Vector2(20, 20);
            uint barColor = 0xFFFFFFFF; // White

            // --- Calculation ---
            // Calculate the size of one screen pixel in the dataset's real-world units (e.g., micrometers).
            float realWorldUnitsPerPixel = _dataset.PixelSize / zoom;
            
            // Aim for a scale bar that is roughly 120 pixels wide on screen.
            float targetBarLengthPixels = 120f;
            float barLengthInRealUnits = targetBarLengthPixels * realWorldUnitsPerPixel;

            // Find a "nice" round number for the label (e.g., 1, 2, 5, 10, 20, 50...).
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(barLengthInRealUnits)));
            double mostSignificantDigit = Math.Round(barLengthInRealUnits / magnitude);

            if (mostSignificantDigit > 5) mostSignificantDigit = 10;
            else if (mostSignificantDigit > 2) mostSignificantDigit = 5;
            else if (mostSignificantDigit > 1) mostSignificantDigit = 2;
            
            float niceLengthInRealUnits = (float)(mostSignificantDigit * magnitude);

            // Convert this "nice" real-world length back into the exact number of pixels it should occupy on screen.
            float finalBarLengthPixels = niceLengthInRealUnits / realWorldUnitsPerPixel;

            string label = $"{niceLengthInRealUnits.ToString("G", CultureInfo.InvariantCulture)} {_dataset.Unit}";
            Vector2 textSize = ImGui.CalcTextSize(label);

            // --- Drawing ---
            // Position the bar in the bottom-right corner.
            Vector2 barStart = new Vector2(
                canvasPos.X + canvasSize.X - margin.X - finalBarLengthPixels,
                canvasPos.Y + canvasSize.Y - margin.Y - barHeight
            );
            Vector2 barEnd = new Vector2(barStart.X + finalBarLengthPixels, barStart.Y + barHeight);
            
            // Center the text above the bar.
            Vector2 textPos = new Vector2(
                barStart.X + (finalBarLengthPixels - textSize.X) * 0.5f,
                barStart.Y - textSize.Y - textPadding
            );

            // Draw a drop shadow for the text for better readability on any background.
            dl.AddText(textPos + Vector2.One, 0x90000000, label); 
            // Draw the main text and the filled rectangle for the bar.
            dl.AddText(textPos, barColor, label);
            dl.AddRectFilled(barStart, barEnd, barColor);
        }
        
        /// <summary>
        /// Cleans up unmanaged Veldrid resources to prevent memory leaks.
        /// </summary>
        public void Dispose()
        {
            if (_textureView != null)
            {
                // Unregister from ImGui, then dispose the Veldrid objects.
                VeldridManager.ImGuiController.RemoveImGuiBinding(_textureView);
                _textureView.Target.Dispose(); // Dispose the underlying Texture
                _textureView.Dispose();
                _textureView = null;
                _textureId = IntPtr.Zero;
            }
        }
    }
}