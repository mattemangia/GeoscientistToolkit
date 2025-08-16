// GeoscientistToolkit/Data/Image/ImageViewer.cs
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Image.Segmentation;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace GeoscientistToolkit.Data.Image
{
    public class ImageViewer : IDatasetViewer
    {
        private readonly ImageDataset _dataset;
        
        // Segmentation
        private TextureManager _segmentationTexture;
        private bool _showSegmentation = true;
        private float _segmentationOpacity = 0.5f;
        private ImageSegmentationToolsUI _segmentationTools;
        
        // Scale bar state
        private Vector2 _scaleBarRelativePos = new Vector2(0.85f, 0.9f);
        private bool _isDraggingScaleBar = false;
        private Vector2 _dragOffset;
        private bool _showScaleBarProperties = false;
        
        // Scale bar customization
        private float _scaleBarHeight = 8f;
        private float _scaleBarTargetWidth = 120f;
        private Vector4 _scaleBarColor = new Vector4(1, 1, 1, 1);
        private float _scaleBarFontSize = 1.0f;
        private bool _showScaleBarBackground = true;
        private Vector4 _scaleBarBgColor = new Vector4(0, 0, 0, 0.5f);
        private int _scaleBarPosition = 3;
        
        // Stored pixel size for editing
        private float _editablePixelSize;
        private string _editableUnit;
        private bool _pixelSizeChanged = false;

        public ImageViewer(ImageDataset dataset)
        {
            _dataset = dataset;
            _editablePixelSize = dataset.PixelSize;
            _editableUnit = dataset.Unit ?? "µm";
        }

        public void DrawToolbarControls() 
        {
            // Segmentation controls
            if (_dataset.HasSegmentation || _dataset.ImageData != null)
            {
                ImGui.SameLine();
                ImGui.Checkbox("Show Labels", ref _showSegmentation);
                
                if (_showSegmentation && _dataset.HasSegmentation)
                {
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(100);
                    ImGui.SliderFloat("Opacity", ref _segmentationOpacity, 0.0f, 1.0f, "%.2f");
                }
            }
            
            if (_dataset.PixelSize > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"Scale: {_editablePixelSize:F2} {_editableUnit}/pixel");
            }
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            // Get drawing context
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            var dl = ImGui.GetWindowDrawList();
            var io = ImGui.GetIO();

            // Draw background
            dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);

            // Set a clipping region
            dl.PushClipRect(canvasPos, canvasPos + canvasSize, true);

            // Calculate display dimensions
            float aspectRatio = (float)_dataset.Width / _dataset.Height;
            Vector2 displaySize = new Vector2(canvasSize.X, canvasSize.X / aspectRatio);
            if (displaySize.Y > canvasSize.Y)
            {
                displaySize = new Vector2(canvasSize.Y * aspectRatio, canvasSize.Y);
            }
            displaySize *= zoom;

            Vector2 imagePos = canvasPos + (canvasSize - displaySize) * 0.5f + pan;

            // Check if mouse is over the image area
            bool isMouseOverImage = io.MousePos.X >= canvasPos.X && io.MousePos.X <= canvasPos.X + canvasSize.X &&
                                   io.MousePos.Y >= canvasPos.Y && io.MousePos.Y <= canvasPos.Y + canvasSize.Y;

            // Handle mouse wheel zoom
            if (isMouseOverImage && Math.Abs(io.MouseWheel) > 0.0f)
            {
                float zoomDelta = io.MouseWheel * 0.1f;
                float newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
                
                if (Math.Abs(newZoom - zoom) > 0.001f)
                {
                    Vector2 mousePos = io.MousePos - canvasPos - canvasSize * 0.5f;
                    pan -= mousePos * (newZoom / zoom - 1.0f);
                    zoom = newZoom;
                }
            }

            // Handle panning with middle mouse button
            if (isMouseOverImage && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            {
                pan += io.MouseDelta;
            }

            // Draw the main image if available
            if (_dataset.ImageData != null)
            {
                TextureManager textureManager = GlobalPerformanceManager.Instance.TextureCache.GetTexture(_dataset.FilePath, () =>
                {
                    _dataset.Load();
                    if (_dataset.ImageData == null) return (null, 0);

                    var manager = TextureManager.CreateFromPixelData(
                        _dataset.ImageData,
                        (uint)_dataset.Width,
                        (uint)_dataset.Height);
                    
                    long size = (long)_dataset.Width * _dataset.Height * 4;
                    
                    _dataset.Unload();
                    return (manager, size);
                });

                if (textureManager != null && textureManager.IsValid)
                {
                    var textureId = textureManager.GetImGuiTextureId();
                    if (textureId != IntPtr.Zero)
                    {
                        dl.AddImage(textureId, imagePos, imagePos + displaySize);
                    }
                }
            }
            else if (_dataset.HasSegmentation)
            {
                // Draw a dark background if we only have segmentation
                dl.AddRectFilled(imagePos, imagePos + displaySize, 0xFF101010);
            }

            // Draw segmentation overlay if available
            if (_showSegmentation && _dataset.HasSegmentation)
            {
                DrawSegmentationOverlay(dl, imagePos, displaySize);
            }

            // Handle segmentation tool interactions
            if (_segmentationTools != null && isMouseOverImage)
            {
                HandleSegmentationToolInteraction(imagePos, displaySize, zoom);
            }

            // Draw scale bar
            if (_dataset.PixelSize > 0 || _pixelSizeChanged)
            {
                DrawScaleBar(dl, canvasPos, canvasSize, zoom);
            }

            // Pop the clipping region
            dl.PopClipRect();

            // Draw scale bar properties window
            DrawScaleBarProperties();
        }

        private void DrawSegmentationOverlay(ImDrawListPtr dl, Vector2 imagePos, Vector2 displaySize)
        {
            if (_dataset.Segmentation == null) return;

            // Create or update segmentation texture
            string segTexKey = _dataset.FilePath + "_segmentation";
            
            _segmentationTexture = GlobalPerformanceManager.Instance.TextureCache.GetTexture(segTexKey, () =>
            {
                var rgbaData = CreateSegmentationRGBA();
                if (rgbaData == null) return (null, 0);

                var manager = TextureManager.CreateFromPixelData(
                    rgbaData,
                    (uint)_dataset.Width,
                    (uint)_dataset.Height);
                
                long size = (long)_dataset.Width * _dataset.Height * 4;
                return (manager, size);
            });

            if (_segmentationTexture != null && _segmentationTexture.IsValid)
            {
                var textureId = _segmentationTexture.GetImGuiTextureId();
                if (textureId != IntPtr.Zero)
                {
                    // Draw with transparency
                    dl.AddImage(textureId, imagePos, imagePos + displaySize, 
                        Vector2.Zero, Vector2.One, 
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, _segmentationOpacity)));
                }
            }
        }

        private byte[] CreateSegmentationRGBA()
        {
            if (_dataset.Segmentation == null) return null;

            byte[] rgbaData = new byte[_dataset.Width * _dataset.Height * 4];
            
            for (int i = 0; i < _dataset.Segmentation.LabelData.Length; i++)
            {
                byte labelId = _dataset.Segmentation.LabelData[i];
                var material = _dataset.Segmentation.GetMaterial(labelId);
                
                int pixelIdx = i * 4;
                if (material != null && labelId != 0) // Don't render exterior (0)
                {
                    rgbaData[pixelIdx] = (byte)(material.Color.X * 255);
                    rgbaData[pixelIdx + 1] = (byte)(material.Color.Y * 255);
                    rgbaData[pixelIdx + 2] = (byte)(material.Color.Z * 255);
                    rgbaData[pixelIdx + 3] = (byte)(material.Color.W * 255);
                }
                else
                {
                    // Transparent for exterior or unknown labels
                    rgbaData[pixelIdx] = 0;
                    rgbaData[pixelIdx + 1] = 0;
                    rgbaData[pixelIdx + 2] = 0;
                    rgbaData[pixelIdx + 3] = 0;
                }
            }
            
            return rgbaData;
        }

        private void HandleSegmentationToolInteraction(Vector2 imagePos, Vector2 displaySize, float zoom)
        {
            var io = ImGui.GetIO();
            
            // Convert mouse position to image coordinates
            Vector2 relativePos = io.MousePos - imagePos;
            int imageX = (int)(relativePos.X / displaySize.X * _dataset.Width);
            int imageY = (int)(relativePos.Y / displaySize.Y * _dataset.Height);
            
            // Clamp to image bounds
            imageX = Math.Clamp(imageX, 0, _dataset.Width - 1);
            imageY = Math.Clamp(imageY, 0, _dataset.Height - 1);
            
            // Pass to segmentation tools
            bool isDragging = ImGui.IsMouseDragging(ImGuiMouseButton.Left);
            _segmentationTools?.HandleMouseClick(imageX, imageY, isDragging);
            
            // Invalidate segmentation texture if modified
            if (isDragging || ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                InvalidateSegmentationTexture();
            }
        }

        public void InvalidateSegmentationTexture()
        {
            if (_segmentationTexture != null)
            {
                string segTexKey = _dataset.FilePath + "_segmentation";
                GlobalPerformanceManager.Instance.TextureCache.ReleaseTexture(segTexKey);
                _segmentationTexture = null;
            }
        }

        public void SetSegmentationTools(ImageSegmentationToolsUI tools)
        {
            _segmentationTools = tools;
        }
        
        private void DrawScaleBar(ImDrawListPtr dl, Vector2 canvasPos, Vector2 canvasSize, float zoom)
        {
            var io = ImGui.GetIO();
            float pixelSize = _pixelSizeChanged ? _editablePixelSize : _dataset.PixelSize;
            string unit = _pixelSizeChanged ? _editableUnit : (_dataset.Unit ?? "µm");
            
            if (pixelSize <= 0) return;

            // Calculate scale bar dimensions
            float realWorldUnitsPerPixel = pixelSize / zoom;
            float barLengthInRealUnits = _scaleBarTargetWidth * realWorldUnitsPerPixel;

            // Find a nice round number
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(barLengthInRealUnits)));
            double mostSignificantDigit = Math.Round(barLengthInRealUnits / magnitude);

            if (mostSignificantDigit > 5) mostSignificantDigit = 10;
            else if (mostSignificantDigit > 2) mostSignificantDigit = 5;
            else if (mostSignificantDigit > 1) mostSignificantDigit = 2;
            
            float niceLengthInRealUnits = (float)(mostSignificantDigit * magnitude);
            float finalBarLengthPixels = niceLengthInRealUnits / realWorldUnitsPerPixel;

            string label = $"{niceLengthInRealUnits.ToString("G", CultureInfo.InvariantCulture)} {unit}";
            Vector2 textSize = ImGui.CalcTextSize(label) * _scaleBarFontSize;

            // Calculate position
            Vector2 margin = new Vector2(20, 20);
            Vector2 barPos;
            
            if (_scaleBarPosition < 4)
            {
                switch (_scaleBarPosition)
                {
                    case 0: // Top-left
                        _scaleBarRelativePos = new Vector2(0.05f, 0.05f);
                        break;
                    case 1: // Top-right
                        _scaleBarRelativePos = new Vector2(0.95f - finalBarLengthPixels / canvasSize.X, 0.05f);
                        break;
                    case 2: // Bottom-left
                        _scaleBarRelativePos = new Vector2(0.05f, 0.95f);
                        break;
                    case 3: // Bottom-right
                        _scaleBarRelativePos = new Vector2(0.95f - finalBarLengthPixels / canvasSize.X, 0.95f);
                        break;
                }
            }
            
            barPos = canvasPos + _scaleBarRelativePos * canvasSize;

            // Define rectangles
            Vector2 barStart = barPos;
            Vector2 barEnd = new Vector2(barStart.X + finalBarLengthPixels, barStart.Y + _scaleBarHeight);
            
            Vector2 clickAreaMin = new Vector2(barStart.X - 5, barStart.Y - textSize.Y - 8);
            Vector2 clickAreaMax = new Vector2(barEnd.X + 5, barEnd.Y + 5);

            // Check mouse interaction
            bool isMouseOverScaleBar = io.MousePos.X >= clickAreaMin.X && io.MousePos.X <= clickAreaMax.X &&
                                      io.MousePos.Y >= clickAreaMin.Y && io.MousePos.Y <= clickAreaMax.Y;

            // Handle dragging
            if (isMouseOverScaleBar && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _isDraggingScaleBar = true;
                _dragOffset = io.MousePos - barPos;
                _scaleBarPosition = 4;
            }

            if (_isDraggingScaleBar)
            {
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    Vector2 newPos = io.MousePos - _dragOffset - canvasPos;
                    _scaleBarRelativePos = newPos / canvasSize;
                    _scaleBarRelativePos.X = Math.Clamp(_scaleBarRelativePos.X, 0, 1);
                    _scaleBarRelativePos.Y = Math.Clamp(_scaleBarRelativePos.Y, 0, 1);
                }
                else
                {
                    _isDraggingScaleBar = false;
                }
            }

            // Handle right-click
            if (isMouseOverScaleBar && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                ImGui.OpenPopup("ScaleBarContext");
            }

            // Draw background
            if (_showScaleBarBackground)
            {
                uint bgColor = ImGui.ColorConvertFloat4ToU32(_scaleBarBgColor);
                dl.AddRectFilled(
                    clickAreaMin - new Vector2(3, 3),
                    clickAreaMax + new Vector2(3, 3),
                    bgColor,
                    3.0f
                );
            }

            // Draw scale bar
            uint barColor = ImGui.ColorConvertFloat4ToU32(_scaleBarColor);
            Vector2 textPos = new Vector2(
                barStart.X + (finalBarLengthPixels - textSize.X) * 0.5f,
                barStart.Y - textSize.Y - 4
            );

            if (!_showScaleBarBackground)
            {
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * _scaleBarFontSize, 
                          textPos + Vector2.One, 0x90000000, label);
            }
            
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * _scaleBarFontSize, 
                      textPos, barColor, label);
            dl.AddRectFilled(barStart, barEnd, barColor);

            if (isMouseOverScaleBar && !_isDraggingScaleBar)
            {
                dl.AddRect(clickAreaMin, clickAreaMax, 0x80FFFFFF, 2.0f);
            }

            // Context menu
            if (ImGui.BeginPopup("ScaleBarContext"))
            {
                if (ImGui.MenuItem("Properties..."))
                {
                    _showScaleBarProperties = true;
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Hide"))
                {
                    _dataset.PixelSize = 0;
                }
                ImGui.EndPopup();
            }
        }
        
        private void DrawScaleBarProperties()
        {
            if (!_showScaleBarProperties) return;

            ImGui.SetNextWindowSize(new Vector2(350, 400), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Scale Bar Properties", ref _showScaleBarProperties))
            {
                if (ImGui.CollapsingHeader("Measurement", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    if (ImGui.InputFloat("Pixel Size", ref _editablePixelSize, 0.01f, 0.1f, "%.3f"))
                    {
                        _pixelSizeChanged = true;
                    }
                    
                    if (ImGui.InputText("Unit", ref _editableUnit, 20))
                    {
                        _pixelSizeChanged = true;
                    }
                    
                    if (_pixelSizeChanged)
                    {
                        if (ImGui.Button("Apply Changes"))
                        {
                            _dataset.PixelSize = _editablePixelSize;
                            _dataset.Unit = _editableUnit;
                            _pixelSizeChanged = false;
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Revert"))
                        {
                            _editablePixelSize = _dataset.PixelSize;
                            _editableUnit = _dataset.Unit ?? "µm";
                            _pixelSizeChanged = false;
                        }
                    }
                    ImGui.Unindent();
                }

                if (ImGui.CollapsingHeader("Appearance", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    
                    ImGui.SliderFloat("Bar Height", ref _scaleBarHeight, 4.0f, 20.0f, "%.0f pixels");
                    ImGui.SliderFloat("Target Width", ref _scaleBarTargetWidth, 50.0f, 300.0f, "%.0f pixels");
                    ImGui.SliderFloat("Font Size", ref _scaleBarFontSize, 0.5f, 2.0f, "%.1fx");
                    
                    ImGui.ColorEdit4("Bar Color", ref _scaleBarColor);
                    
                    ImGui.Checkbox("Show Background", ref _showScaleBarBackground);
                    if (_showScaleBarBackground)
                    {
                        ImGui.ColorEdit4("Background Color", ref _scaleBarBgColor);
                    }
                    
                    ImGui.Unindent();
                }

                if (ImGui.CollapsingHeader("Position", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    
                    string[] positions = { "Top-Left", "Top-Right", "Bottom-Left", "Bottom-Right", "Custom" };
                    ImGui.Combo("Position", ref _scaleBarPosition, positions, positions.Length);
                    
                    if (_scaleBarPosition == 4)
                    {
                        float x = _scaleBarRelativePos.X * 100;
                        float y = _scaleBarRelativePos.Y * 100;
                        
                        if (ImGui.SliderFloat("X Position", ref x, 0, 100, "%.0f%%"))
                        {
                            _scaleBarRelativePos.X = x / 100.0f;
                        }
                        
                        if (ImGui.SliderFloat("Y Position", ref y, 0, 100, "%.0f%%"))
                        {
                            _scaleBarRelativePos.Y = y / 100.0f;
                        }
                    }
                    
                    ImGui.Unindent();
                }

                ImGui.Separator();
                if (ImGui.Button("Close", new Vector2(100, 0)))
                {
                    _showScaleBarProperties = false;
                }
            }
            ImGui.End();
        }
        
        public void Dispose()
        {
            // Release textures from the cache
            if (_dataset.FilePath != null)
            {
                GlobalPerformanceManager.Instance.TextureCache.ReleaseTexture(_dataset.FilePath);
            }
            
            InvalidateSegmentationTexture();
        }
    }
}