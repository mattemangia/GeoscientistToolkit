// GeoscientistToolkit/Data/CtImageStack/CtImageStackViewer.cs
// Multi-viewport CT viewer with synchronized crosshairs

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
        
        // Slice positions
        private int _sliceX; // Position in X (for YZ view)
        private int _sliceY; // Position in Y (for XZ view)
        private int _sliceZ; // Position in Z (for XY view)
        
        // Textures for each view
        private TextureManager _textureXY;
        private TextureManager _textureXZ;
        private TextureManager _textureYZ;
        private bool _needsUpdateXY = true;
        private bool _needsUpdateXZ = true;
        private bool _needsUpdateYZ = true;
        
        // Window/Level (shared across views)
        private float _windowLevel = 128;
        private float _windowWidth = 255;
        
        // View settings
        private bool _showScaleBar = true;
        private bool _showCrosshairs = true;
        private bool _syncViews = true;
        
        // Zoom and pan for each view
        private float _zoomXY = 1.0f;
        private float _zoomXZ = 1.0f;
        private float _zoomYZ = 1.0f;
        private Vector2 _panXY = Vector2.Zero;
        private Vector2 _panXZ = Vector2.Zero;
        private Vector2 _panYZ = Vector2.Zero;
        
        // Layout
        private enum Layout { Horizontal, Vertical, Grid2x2 }
        private Layout _layout = Layout.Grid2x2;
        
        public CtImageStackViewer(CtImageStackDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            
            // Ensure data is loaded
            _dataset.Load();
            CtImageStackTools.PreviewChanged += OnPreviewChanged;
            // Initialize to center slices
            _sliceX = _dataset.Width / 2;
            _sliceY = _dataset.Height / 2;
            _sliceZ = _dataset.Depth / 2;
        }
        private void OnPreviewChanged(CtImageStackDataset dataset)
        {
            if (dataset == _dataset)
            {
                _needsUpdateXY = true;
                _needsUpdateXZ = true;
                _needsUpdateYZ = true;
            }
        }
        public void DrawToolbarControls()
        {
            // Layout selection
            ImGui.Text("Layout:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            string[] layouts = { "Horizontal", "Vertical", "2×2 Grid" };
            int layoutIndex = (int)_layout;
            if (ImGui.Combo("##Layout", ref layoutIndex, layouts, layouts.Length))
            {
                _layout = (Layout)layoutIndex;
            }
            
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            // Window/Level controls
            ImGui.Text("W/L:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.DragFloat("##Window", ref _windowWidth, 1f, 1f, 255f, "W: %.0f"))
            {
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.DragFloat("##Level", ref _windowLevel, 1f, 0f, 255f, "L: %.0f"))
            {
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            }
            
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            // View options
            ImGui.Checkbox("Crosshairs", ref _showCrosshairs);
            ImGui.SameLine();
            ImGui.Checkbox("Scale Bar", ref _showScaleBar);
            ImGui.SameLine();
            ImGui.Checkbox("Sync Views", ref _syncViews);
            
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            // Reset button
            if (ImGui.Button("Reset Views"))
            {
                ResetViews();
            }
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            var availableSize = ImGui.GetContentRegionAvail();
            
            switch (_layout)
            {
                case Layout.Horizontal:
                    DrawHorizontalLayout(availableSize);
                    break;
                case Layout.Vertical:
                    DrawVerticalLayout(availableSize);
                    break;
                case Layout.Grid2x2:
                    DrawGrid2x2Layout(availableSize);
                    break;
            }
        }
        
        private void DrawHorizontalLayout(Vector2 availableSize)
        {
            float viewWidth = (availableSize.X - 4) / 3; // 2px spacing between views
            float viewHeight = availableSize.Y;
            
            // XY View
            DrawView(0, new Vector2(viewWidth, viewHeight), "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
            
            ImGui.SameLine(0, 2);
            
            // XZ View
            DrawView(1, new Vector2(viewWidth, viewHeight), "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
            
            ImGui.SameLine(0, 2);
            
            // YZ View
            DrawView(2, new Vector2(viewWidth, viewHeight), "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
        }
        
        private void DrawVerticalLayout(Vector2 availableSize)
        {
            float viewWidth = availableSize.X;
            float viewHeight = (availableSize.Y - 4) / 3;
            
            DrawView(0, new Vector2(viewWidth, viewHeight), "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
            DrawView(1, new Vector2(viewWidth, viewHeight), "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
            DrawView(2, new Vector2(viewWidth, viewHeight), "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
        }
        
        private void DrawGrid2x2Layout(Vector2 availableSize)
        {
            float viewWidth = (availableSize.X - 2) / 2;
            float viewHeight = (availableSize.Y - 2) / 2;
            
            // Top row
            DrawView(0, new Vector2(viewWidth, viewHeight), "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
            ImGui.SameLine(0, 2);
            DrawView(1, new Vector2(viewWidth, viewHeight), "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
            
            // Bottom row
            DrawView(2, new Vector2(viewWidth, viewHeight), "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
            ImGui.SameLine(0, 2);
            
            // 3D info panel in 4th quadrant
            Draw3DInfoPanel(new Vector2(viewWidth, viewHeight));
        }
        
        private void DrawView(int viewIndex, Vector2 size, string title, ref float zoom, ref Vector2 pan, 
            ref bool needsUpdate, ref TextureManager texture)
        {
            ImGui.BeginChild($"View{viewIndex}", size, ImGuiChildFlags.Border);
            
            // Title bar
            ImGui.Text(title);
            ImGui.SameLine();
            
            // Slice control
            int slice = viewIndex switch
            {
                0 => _sliceZ,
                1 => _sliceY,
                2 => _sliceX,
                _ => 0
            };
            
            int maxSlice = viewIndex switch
            {
                0 => _dataset.Depth - 1,
                1 => _dataset.Height - 1,
                2 => _dataset.Width - 1,
                _ => 0
            };
            
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt($"##Slice{viewIndex}", ref slice, 0, maxSlice))
            {
                switch (viewIndex)
                {
                    case 0: 
                        _sliceZ = slice; 
                        needsUpdate = true; 
                        break;
                    case 1: 
                        _sliceY = slice; 
                        needsUpdate = true; 
                        break;
                    case 2: 
                        _sliceX = slice; 
                        needsUpdate = true; 
                        break;
                }
            }
            
            ImGui.SameLine();
            ImGui.Text($"{slice + 1}/{maxSlice + 1}");
            
            // Draw the actual view content
            DrawSingleView(viewIndex, ref zoom, ref pan, ref needsUpdate, ref texture);
            
            ImGui.EndChild();
        }
        
        private void DrawSingleView(int viewIndex, ref float zoom, ref Vector2 pan, 
            ref bool needsUpdate, ref TextureManager texture)
        {
            var io = ImGui.GetIO();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            var dl = ImGui.GetWindowDrawList();
            
            // Create invisible button for mouse interaction
            ImGui.InvisibleButton($"canvas{viewIndex}", canvasSize);
            bool isHovered = ImGui.IsItemHovered();
            
            // Handle mouse wheel zoom
            if (isHovered && io.MouseWheel != 0)
            {
                float zoomDelta = io.MouseWheel * 0.1f;
                float newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
                
                if (newZoom != zoom)
                {
                    Vector2 mousePos = io.MousePos - canvasPos - canvasSize * 0.5f;
                    pan -= mousePos * (newZoom / zoom - 1.0f);
                    zoom = newZoom;
                    
                    // Sync zoom across views if enabled
                    if (_syncViews)
                    {
                        _zoomXY = _zoomXZ = _zoomYZ = zoom;
                    }
                }
            }
            
            // --- FIX START: Changed from IsItemActive to IsItemHovered for panning ---
            if (isHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            {
                pan += io.MouseDelta;
                
                // Sync pan across views if enabled
                if (_syncViews)
                {
                    _panXY = pan;
                    _panXZ = pan;
                    _panYZ = pan;
                }
            }
            // --- FIX END ---
            
            // Handle slice scrolling with Ctrl+Wheel
            if (isHovered && io.MouseWheel != 0 && io.KeyCtrl)
            {
                switch (viewIndex)
                {
                    case 0: // XY view - scroll Z
                        _sliceZ = Math.Clamp(_sliceZ + (int)io.MouseWheel, 0, _dataset.Depth - 1);
                        needsUpdate = true;
                        break;
                    case 1: // XZ view - scroll Y
                        _sliceY = Math.Clamp(_sliceY + (int)io.MouseWheel, 0, _dataset.Height - 1);
                        needsUpdate = true;
                        break;
                    case 2: // YZ view - scroll X
                        _sliceX = Math.Clamp(_sliceX + (int)io.MouseWheel, 0, _dataset.Width - 1);
                        needsUpdate = true;
                        break;
                }
            }
            
            // Handle click to set crosshair position
            if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                UpdateCrosshairFromMouse(viewIndex, canvasPos, canvasSize, zoom, pan);
            }
            
            // Draw background
            dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);
            
            // Update texture if needed
            if (needsUpdate || texture == null || !texture.IsValid)
            {
                UpdateTexture(viewIndex, ref texture);
                needsUpdate = false;
            }
            
            // Draw the image
            if (texture != null && texture.IsValid)
            {
                var (width, height) = GetImageDimensionsForView(viewIndex);
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
                dl.AddImage(texture.GetImGuiTextureId(), imagePos, imagePos + imageSize,
                    Vector2.Zero, Vector2.One, 0xFFFFFFFF);
                
                // Draw crosshairs
                if (_showCrosshairs)
                {
                    DrawCrosshairs(dl, viewIndex, canvasPos, canvasSize, imagePos, imageSize, width, height);
                }
                
                // Draw scale bar
                if (_showScaleBar)
                {
                    DrawScaleBar(dl, canvasPos, canvasSize, zoom, width, height, viewIndex);
                }
            }
        }
        
        private void Draw3DInfoPanel(Vector2 size)
        {
            ImGui.BeginChild("3DInfo", size, ImGuiChildFlags.Border);
            
            ImGui.Text("Volume Information");
            ImGui.Separator();
            
            ImGui.Text($"Dimensions: {_dataset.Width} × {_dataset.Height} × {_dataset.Depth}");
            ImGui.Text($"Voxel Size: {_dataset.PixelSize:F2} × {_dataset.PixelSize:F2} × {_dataset.SliceThickness:F2} {_dataset.Unit}");
            ImGui.Text($"Current Position:");
            ImGui.Indent();
            ImGui.Text($"X: {_sliceX + 1} / {_dataset.Width}");
            ImGui.Text($"Y: {_sliceY + 1} / {_dataset.Height}");
            ImGui.Text($"Z: {_sliceZ + 1} / {_dataset.Depth}");
            ImGui.Unindent();
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Mouse Controls:");
            ImGui.BulletText("Wheel: Zoom");
            ImGui.BulletText("Middle Drag: Pan");
            ImGui.BulletText("Ctrl+Wheel: Change slice");
            ImGui.BulletText("Left Click: Set crosshair");
            
            ImGui.EndChild();
        }
        
        private void UpdateCrosshairFromMouse(int viewIndex, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
        {
            var mousePos = ImGui.GetMousePos() - canvasPos - canvasSize * 0.5f - pan;
            var (width, height) = GetImageDimensionsForView(viewIndex);
            
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
            
            // Convert mouse position to image coordinates
            float x = (mousePos.X + imageSize.X * 0.5f) / imageSize.X * width;
            float y = (mousePos.Y + imageSize.Y * 0.5f) / imageSize.Y * height;
            
            // Update slice positions based on view
            switch (viewIndex)
            {
                case 0: // XY view
                    _sliceX = Math.Clamp((int)x, 0, _dataset.Width - 1);
                    _sliceY = Math.Clamp((int)y, 0, _dataset.Height - 1);
                    _needsUpdateXZ = _needsUpdateYZ = true;
                    break;
                case 1: // XZ view
                    _sliceX = Math.Clamp((int)x, 0, _dataset.Width - 1);
                    _sliceZ = Math.Clamp((int)y, 0, _dataset.Depth - 1);
                    _needsUpdateXY = _needsUpdateYZ = true;
                    break;
                case 2: // YZ view
                    _sliceY = Math.Clamp((int)x, 0, _dataset.Height - 1);
                    _sliceZ = Math.Clamp((int)y, 0, _dataset.Depth - 1);
                    _needsUpdateXY = _needsUpdateXZ = true;
                    break;
            }
        }
        
        private void DrawCrosshairs(ImDrawListPtr dl, int viewIndex, Vector2 canvasPos, Vector2 canvasSize,
            Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
        {
            uint color = 0xFF00FF00; // Green crosshairs
            
            float x1 = 0, y1 = 0, x2 = 0, y2 = 0;
            
            switch (viewIndex)
            {
                case 0: // XY view - show X and Y positions
                    x1 = (float)_sliceX / imageWidth;
                    y1 = (float)_sliceY / imageHeight;
                    break;
                case 1: // XZ view - show X and Z positions
                    x1 = (float)_sliceX / imageWidth;
                    y1 = (float)_sliceZ / imageHeight;
                    break;
                case 2: // YZ view - show Y and Z positions
                    x1 = (float)_sliceY / imageWidth;
                    y1 = (float)_sliceZ / imageHeight;
                    break;
            }
            
            // Convert to screen coordinates
            float screenX = imagePos.X + x1 * imageSize.X;
            float screenY = imagePos.Y + y1 * imageSize.Y;
            
            // Draw crosshairs (clipped to image bounds)
            if (screenX >= imagePos.X && screenX <= imagePos.X + imageSize.X)
            {
                dl.AddLine(new Vector2(screenX, imagePos.Y), new Vector2(screenX, imagePos.Y + imageSize.Y), color, 1.0f);
            }
            if (screenY >= imagePos.Y && screenY <= imagePos.Y + imageSize.Y)
            {
                dl.AddLine(new Vector2(imagePos.X, screenY), new Vector2(imagePos.X + imageSize.X, screenY), color, 1.0f);
            }
        }
        
        private void UpdateTexture(int viewIndex, ref TextureManager texture)
        {
            if (_dataset.VolumeData == null)
            {
                Logger.Log("[CtImageStackViewer] No volume data available");
                return;
            }
            
            try
            {
                var (width, height) = GetImageDimensionsForView(viewIndex);
                byte[] imageData = ExtractSliceData(viewIndex, width, height);
                
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
                texture?.Dispose();
                
                // Create new texture
                texture = TextureManager.CreateFromPixelData(rgbaData, (uint)width, (uint)height);
            }
            catch (Exception ex)
            {
                Logger.Log($"[CtImageStackViewer] Error updating texture: {ex.Message}");
            }
        }
        
        private byte[] ExtractSliceData(int viewIndex, int width, int height)
        {
            byte[] data = new byte[width * height];
            var volume = _dataset.VolumeData;
            
            switch (viewIndex)
            {
                case 0: // XY plane at Z = _sliceZ
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            data[y * width + x] = volume[x, y, _sliceZ];
                        }
                    }
                    break;
                    
                case 1: // XZ plane at Y = _sliceY
                    for (int z = 0; z < height; z++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            data[z * width + x] = volume[x, _sliceY, z];
                        }
                    }
                    break;
                    
                case 2: // YZ plane at X = _sliceX
                    for (int z = 0; z < height; z++)
                    {
                        for (int y = 0; y < width; y++)
                        {
                            data[z * width + y] = volume[_sliceX, y, z];
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
        
        private (int width, int height) GetImageDimensionsForView(int viewIndex)
        {
            return viewIndex switch
            {
                0 => (_dataset.Width, _dataset.Height),  // XY plane
                1 => (_dataset.Width, _dataset.Depth),   // XZ plane
                2 => (_dataset.Height, _dataset.Depth),  // YZ plane
                _ => (_dataset.Width, _dataset.Height)
            };
        }
        
        private void DrawScaleBar(ImDrawListPtr dl, Vector2 canvasPos, Vector2 canvasSize, 
            float zoom, int imageWidth, int imageHeight, int viewIndex)
        {
            // Calculate scale based on view
            float pixelSizeInUnits = viewIndex switch
            {
                0 => _dataset.PixelSize,  // XY view
                1 => (_dataset.PixelSize + _dataset.SliceThickness) / 2,  // XZ view (average)
                2 => (_dataset.PixelSize + _dataset.SliceThickness) / 2,  // YZ view (average)
                _ => _dataset.PixelSize
            };
            
            float scaleFactor = canvasSize.X / imageWidth * zoom;
            
            // Determine appropriate scale bar length
            float[] possibleLengths = { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 };
            float targetPixelLength = 100;
            
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
        
        private void ResetViews()
        {
            _sliceX = _dataset.Width / 2;
            _sliceY = _dataset.Height / 2;
            _sliceZ = _dataset.Depth / 2;
            _zoomXY = _zoomXZ = _zoomYZ = 1.0f;
            _panXY = _panXZ = _panYZ = Vector2.Zero;
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        }

        public void Dispose()
        {
            CtImageStackTools.PreviewChanged -= OnPreviewChanged;
            _textureXY?.Dispose();
            _textureXZ?.Dispose();
            _textureYZ?.Dispose();
        }
    }
}