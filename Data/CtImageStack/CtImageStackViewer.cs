// GeoscientistToolkit/Data/CtImageStack/CtImageStackViewer.cs
// Viewer for CT image stacks with scale bar functionality

using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;
using Veldrid;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackViewer : IDatasetViewer
    {
        private readonly CtImageStackDataset _dataset;
        private int _currentSlice = 0;
        private int _viewMode = 0; // 0=XY (Axial), 1=XZ (Coronal), 2=YZ (Sagittal)
        private TextureManager _currentTexture;
        private bool _showScaleBar = true;
        private float _windowLevel = 128;
        private float _windowWidth = 255;
        private bool _needsTextureUpdate = true;
        
        public CtImageStackViewer(CtImageStackDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            
            // Ensure data is loaded
            _dataset.Load();
            
            // Initialize to middle slice
            _currentSlice = GetDepthForView() / 2;
        }
        
        public void DrawToolbarControls()
        {
            // View mode selection
            string[] modes = { "XY", "XZ", "YZ" };
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("##ViewMode", ref _viewMode, modes, modes.Length))
            {
                _currentSlice = GetDepthForView() / 2;
                _needsTextureUpdate = true;
            }
            
            ImGui.SameLine();
            
            // Slice slider
            int maxSlice = GetDepthForView() - 1;
            ImGui.SetNextItemWidth(200);
            if (ImGui.SliderInt("Slice", ref _currentSlice, 0, maxSlice))
            {
                _needsTextureUpdate = true;
            }
            
            ImGui.SameLine();
            ImGui.Text($"{_currentSlice + 1}/{maxSlice + 1}");
            
            ImGui.SameLine();
            ImGui.Checkbox("Scale Bar", ref _showScaleBar);
            
            // Window/Level controls
            ImGui.SameLine();
            ImGui.Text("W/L:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.DragFloat("##Window", ref _windowWidth, 1f, 1f, 255f, "W: %.0f"))
            {
                _needsTextureUpdate = true;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.DragFloat("##Level", ref _windowLevel, 1f, 0f, 255f, "L: %.0f"))
            {
                _needsTextureUpdate = true;
            }
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            var io = ImGui.GetIO();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            var dl = ImGui.GetWindowDrawList();

            // Create invisible button for mouse interaction
            ImGui.InvisibleButton("ct_canvas", canvasSize);
            bool isHovered = ImGui.IsItemHovered();

            // Handle mouse wheel zoom
            if (isHovered && io.MouseWheel != 0)
            {
                float zoomDelta = io.MouseWheel * 0.1f;
                float newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
                
                // Zoom towards mouse position
                if (newZoom != zoom)
                {
                    Vector2 mousePos = io.MousePos - canvasPos - canvasSize * 0.5f;
                    pan -= mousePos * (newZoom / zoom - 1.0f);
                    zoom = newZoom;
                }
            }

            // Handle slice scrolling with Ctrl+MouseWheel
            if (isHovered && io.MouseWheel != 0 && io.KeyCtrl)
            {
                _currentSlice = Math.Clamp(_currentSlice + (int)io.MouseWheel, 0, GetDepthForView() - 1);
                _needsTextureUpdate = true;
            }

            // Handle panning with middle mouse button
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            {
                pan += io.MouseDelta;
            }

            // Draw background
            dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);
            
            // Update texture if needed
            if (_needsTextureUpdate || _currentTexture == null || !_currentTexture.IsValid)
            {
                UpdateTexture();
            }
            
            // Draw the CT image
            if (_currentTexture != null && _currentTexture.IsValid)
            {
                var (width, height) = GetImageDimensionsForView();
                float imageAspect = (float)width / height;
                float canvasAspect = canvasSize.X / canvasSize.Y;
                
                Vector2 imageSize;
                if (imageAspect > canvasAspect)
                {
                    imageSize = new Vector2(canvasSize.X * zoom, canvasSize.X / imageAspect * zoom);
                }
                else
                {
                    imageSize = new Vector2(canvasSize.Y * imageAspect * zoom, canvasSize.Y * zoom);
                }
                
                Vector2 imagePos = canvasPos + canvasSize * 0.5f - imageSize * 0.5f + pan;
                
                // Draw the image
                dl.AddImage(_currentTexture.GetImGuiTextureId(), imagePos, imagePos + imageSize,
                    Vector2.Zero, Vector2.One, 0xFFFFFFFF);
                
                // Draw scale bar if enabled
                if (_showScaleBar)
                {
                    DrawScaleBar(dl, canvasPos, canvasSize, zoom, width, height);
                }
                
                // Draw orientation markers
                DrawOrientationMarkers(dl, canvasPos, canvasSize);
            }
            else
            {
                // Show loading or error message
                string text = "Loading slice...";
                var textSize = ImGui.CalcTextSize(text);
                var textPos = canvasPos + (canvasSize - textSize) * 0.5f;
                dl.AddText(textPos, 0xFFFFFFFF, text);
            }
        }

        private void UpdateTexture()
        {
            if (_dataset.VolumeData == null)
            {
                Logger.Log("[CtImageStackViewer] No volume data available");
                return;
            }
            
            try
            {
                var (width, height) = GetImageDimensionsForView();
                byte[] imageData = ExtractSliceData(width, height);
                
                // Apply window/level
                ApplyWindowLevel(imageData);
                
                // Convert to RGBA
                byte[] rgbaData = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    byte value = imageData[i];
                    rgbaData[i * 4] = value;
                    rgbaData[i * 4 + 1] = value;
                    rgbaData[i * 4 + 2] = value;
                    rgbaData[i * 4 + 3] = 255;
                }
                
                // Dispose old texture
                _currentTexture?.Dispose();
                
                // Create new texture
                _currentTexture = TextureManager.CreateFromPixelData(rgbaData, (uint)width, (uint)height);
                _needsTextureUpdate = false;
            }
            catch (Exception ex)
            {
                Logger.Log($"[CtImageStackViewer] Error updating texture: {ex.Message}");
            }
        }
        
        private byte[] ExtractSliceData(int width, int height)
        {
            byte[] data = new byte[width * height];
            var volume = _dataset.VolumeData;
            
            switch (_viewMode)
            {
                case 0: // XY plane
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            data[y * width + x] = volume[x, y, _currentSlice];
                        }
                    }
                    break;
                    
                case 1: // XZ plane
                    for (int z = 0; z < height; z++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            data[z * width + x] = volume[x, _currentSlice, z];
                        }
                    }
                    break;
                    
                case 2: // YZ plane
                    for (int z = 0; z < height; z++)
                    {
                        for (int y = 0; y < width; y++)
                        {
                            data[z * width + y] = volume[_currentSlice, y, z];
                        }
                    }
                    break;
            }
            
            return data;
        }
        
        private void ApplyWindowLevel(byte[] data)
        {
            float min = _windowLevel - _windowWidth / 2;
            float max = _windowLevel + _windowWidth / 2;
            
            for (int i = 0; i < data.Length; i++)
            {
                float value = data[i];
                value = (value - min) / (max - min) * 255;
                data[i] = (byte)Math.Clamp(value, 0, 255);
            }
        }
        
        private (int width, int height) GetImageDimensionsForView()
        {
            return _viewMode switch
            {
                0 => (_dataset.Width, _dataset.Height),  // XY plane
                1 => (_dataset.Width, _dataset.Depth),   // XZ plane
                2 => (_dataset.Height, _dataset.Depth),  // YZ plane
                _ => (_dataset.Width, _dataset.Height)
            };
        }
        
        private int GetDepthForView()
        {
            return _viewMode switch
            {
                0 => _dataset.Depth,   // XY plane - depth is Z
                1 => _dataset.Height,  // XZ plane - depth is Y
                2 => _dataset.Width,   // YZ plane - depth is X
                _ => _dataset.Depth
            };
        }
        
        private void DrawScaleBar(ImDrawListPtr dl, Vector2 canvasPos, Vector2 canvasSize, 
            float zoom, int imageWidth, int imageHeight)
        {
            // Calculate scale
            float pixelSizeInUnits = _dataset.PixelSize;
            float scaleFactor = canvasSize.X / imageWidth * zoom;
            
            // Determine appropriate scale bar length
            float[] possibleLengths = { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 }; // in dataset units
            float targetPixelLength = 100; // Target length in screen pixels
            
            float bestLength = possibleLengths[0];
            foreach (float length in possibleLengths)
            {
                float pixelLength = length / pixelSizeInUnits * scaleFactor;
                if (pixelLength <= 150)
                    bestLength = length;
            }
            
            // Calculate actual pixel length
            float barLengthPixels = bestLength / pixelSizeInUnits * scaleFactor;
            
            // Position (bottom-right corner)
            Vector2 barPos = canvasPos + new Vector2(canvasSize.X - barLengthPixels - 20, canvasSize.Y - 40);
            
            // Draw background
            dl.AddRectFilled(barPos - new Vector2(5, 5), 
                barPos + new Vector2(barLengthPixels + 5, 25), 
                0xAA000000, 3.0f);
            
            // Draw scale bar
            dl.AddLine(barPos, barPos + new Vector2(barLengthPixels, 0), 0xFFFFFFFF, 3.0f);
            dl.AddLine(barPos, barPos + new Vector2(0, 5), 0xFFFFFFFF, 3.0f);
            dl.AddLine(barPos + new Vector2(barLengthPixels, 0), 
                barPos + new Vector2(barLengthPixels, 5), 0xFFFFFFFF, 3.0f);
            
            // Draw text
            string text = bestLength >= 1000 
                ? $"{bestLength / 1000:F1} mm" 
                : $"{bestLength:F0} {_dataset.Unit}";
            
            Vector2 textSize = ImGui.CalcTextSize(text);
            Vector2 textPos = barPos + new Vector2((barLengthPixels - textSize.X) * 0.5f, 8);
            dl.AddText(textPos, 0xFFFFFFFF, text);
        }
        
        private void DrawOrientationMarkers(ImDrawListPtr dl, Vector2 canvasPos, Vector2 canvasSize)
        {
            uint color = 0xAAFFFFFF;
            float offset = 20;
            
            switch (_viewMode)
            {
                case 0: // XY plane (looking down Z axis)
                    dl.AddText(canvasPos + new Vector2(canvasSize.X / 2, offset), color, "Y");
                    dl.AddText(canvasPos + new Vector2(offset, canvasSize.Y / 2), color, "X");
                    dl.AddText(canvasPos + new Vector2(canvasSize.X - offset - 10, canvasSize.Y / 2), color, "X");
                    dl.AddText(canvasPos + new Vector2(canvasSize.X / 2, canvasSize.Y - offset - 10), color, "Y");
                    break;
                    
                case 1: // XZ plane (looking down Y axis)
                    dl.AddText(canvasPos + new Vector2(canvasSize.X / 2, offset), color, "Z");
                    dl.AddText(canvasPos + new Vector2(offset, canvasSize.Y / 2), color, "X");
                    dl.AddText(canvasPos + new Vector2(canvasSize.X - offset - 10, canvasSize.Y / 2), color, "X");
                    dl.AddText(canvasPos + new Vector2(canvasSize.X / 2, canvasSize.Y - offset - 10), color, "Z");
                    break;
                    
                case 2: // YZ plane (looking down X axis)
                    dl.AddText(canvasPos + new Vector2(canvasSize.X / 2, offset), color, "Z");
                    dl.AddText(canvasPos + new Vector2(offset, canvasSize.Y / 2), color, "Y");
                    dl.AddText(canvasPos + new Vector2(canvasSize.X - offset - 10, canvasSize.Y / 2), color, "Y");
                    dl.AddText(canvasPos + new Vector2(canvasSize.X / 2, canvasSize.Y - offset - 10), color, "Z");
                    break;
            }
        }

        public void Dispose()
        {
            _currentTexture?.Dispose();
            _currentTexture = null;
        }
    }
}