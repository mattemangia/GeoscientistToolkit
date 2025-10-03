// GeoscientistToolkit/Data/AcousticVolume/AcousticVolumeViewer.cs
using System;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.AcousticVolume;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.Data.AcousticVolume
{
    /// <summary>
    /// Viewer for acoustic simulation results with wave field visualization,
    /// time-series animation, and interactive analysis capabilities.
    /// </summary>
    public class AcousticVolumeViewer : IDatasetViewer, IDisposable
    {
        private readonly AcousticVolumeDataset _dataset;
        
        // Visualization mode
        private enum VisualizationMode
        {
            PWaveField,
            SWaveField,
            CombinedField,
            DamageField,
            TimeSeries
        }
        private VisualizationMode _currentMode = VisualizationMode.CombinedField;
        
        // Slice positions
        private int _sliceX;
        private int _sliceY;
        private int _sliceZ;
        
        // Textures for each view
        private TextureManager _textureXY;
        private TextureManager _textureXZ;
        private TextureManager _textureYZ;
        private bool _needsUpdateXY = true;
        private bool _needsUpdateXZ = true;
        private bool _needsUpdateYZ = true;

        // Zoom and pan for each view
        private float _zoomXY = 1.0f;
        private float _zoomXZ = 1.0f;
        private float _zoomYZ = 1.0f;
        private Vector2 _panXY = Vector2.Zero;
        private Vector2 _panXZ = Vector2.Zero;
        private Vector2 _panYZ = Vector2.Zero;
        
        // Time series animation
        private bool _isPlaying = false;
        private int _currentFrameIndex = 0;
        private float _animationSpeed = 1.0f;
        private DateTime _lastFrameTime = DateTime.MinValue;
        private float _frameInterval = 0.033f; // 30 FPS
        
        // Display settings
        private float _contrastMin = 0.0f;
        private float _contrastMax = 1.0f;
        private int _colorMapIndex = 0;
        private bool _showGrid = true;
        private bool _showInfo = true;
        private bool _showLegend = true;
        
        // Layout
        private enum Layout { Horizontal, Vertical, Grid2x2 }
        private Layout _layout = Layout.Grid2x2;
        
        // Color maps
        private readonly string[] _colorMapNames = { "Grayscale", "Jet", "Viridis", "Hot", "Cool", "Seismic" };

        // Line drawing state for FFT
        private Vector2 _lineStartPos;
        private bool _isDrawingLine = false;
        
        public AcousticVolumeViewer(AcousticVolumeDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            _dataset.Load();
            
            var initialVolume = _dataset.CombinedWaveField ?? _dataset.PWaveField;
            if (initialVolume != null)
            {
                _sliceX = initialVolume.Width / 2;
                _sliceY = initialVolume.Height / 2;
                _sliceZ = initialVolume.Depth / 2;
            }
            
            Logger.Log($"[AcousticVolumeViewer] Initialized viewer for: {_dataset.Name}");
        }
        
        public void DrawToolbarControls()
        {
            // Mode selection
            ImGui.Text("Mode:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            string[] modes = { "P-Wave", "S-Wave", "Combined", "Damage", "Time Series" };
            int modeIndex = (int)_currentMode;
            if (ImGui.Combo("##Mode", ref modeIndex, modes, modes.Length))
            {
                var newMode = (VisualizationMode)modeIndex;
                if (newMode == VisualizationMode.DamageField && _dataset.DamageField == null)
                {
                    Logger.LogWarning("[AcousticVolumeViewer] Damage field not available. Staying in previous mode.");
                }
                else
                {
                    _currentMode = newMode;
                    _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                }
            }
            
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            // Time series controls
            if (_currentMode == VisualizationMode.TimeSeries && _dataset.TimeSeriesSnapshots?.Count > 0)
            {
                if (ImGui.Button(_isPlaying ? "⏸" : "▶"))
                {
                    _isPlaying = !_isPlaying;
                    _lastFrameTime = DateTime.Now;
                }
                
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200);
                if (ImGui.SliderInt("Frame", ref _currentFrameIndex, 0, _dataset.TimeSeriesSnapshots.Count - 1))
                {
                    _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                }
                
                ImGui.SameLine();
                ImGui.Text($"{_currentFrameIndex + 1}/{_dataset.TimeSeriesSnapshots.Count}");
                
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.DragFloat("Speed", ref _animationSpeed, 0.01f, 0.1f, 10.0f);
            }
            
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            // Color map selection
            ImGui.Text("Colors:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("##ColorMap", ref _colorMapIndex, _colorMapNames, _colorMapNames.Length))
            {
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            }
            
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            // Display options
            ImGui.Checkbox("Grid", ref _showGrid);
            ImGui.SameLine();
            ImGui.Checkbox("Info", ref _showInfo);
            ImGui.SameLine();
            ImGui.Checkbox("Legend", ref _showLegend);
            
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
            // Update animation
            if (_isPlaying && _dataset.TimeSeriesSnapshots?.Count > 0)
            {
                UpdateAnimation();
            }

            // --- REFACTORED LAYOUT ---
            // This creates a persistent control panel on the right and a flexible view panel on the left.
            float controlPanelWidth = 300; 
            var availableSize = ImGui.GetContentRegionAvail();
            
            // Main view panel (left)
            ImGui.BeginChild("SliceViews", new Vector2(availableSize.X - controlPanelWidth - ImGui.GetStyle().ItemSpacing.X, availableSize.Y), ImGuiChildFlags.None);
            var viewPanelSize = ImGui.GetContentRegionAvail();
            switch (_layout)
            {
                case Layout.Horizontal: DrawHorizontalLayout(viewPanelSize); break;
                case Layout.Vertical: DrawVerticalLayout(viewPanelSize); break;
                case Layout.Grid2x2: DrawGrid2x2Layout(viewPanelSize); break;
            }
            ImGui.EndChild();

            ImGui.SameLine();

            // Control panel (right)
            ImGui.BeginChild("InfoAndControlPanel", new Vector2(controlPanelWidth, availableSize.Y), ImGuiChildFlags.Border);
            DrawInfoPanel();
            ImGui.EndChild();
            
            // Draw legend if enabled (it's a separate window)
            if (_showLegend)
            {
                DrawLegend();
            }
        }
        
        private void UpdateAnimation()
        {
            if ((DateTime.Now - _lastFrameTime).TotalSeconds >= _frameInterval / _animationSpeed)
            {
                _lastFrameTime = DateTime.Now;
                _currentFrameIndex = (_currentFrameIndex + 1) % _dataset.TimeSeriesSnapshots.Count;
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            }
        }
        
        private void DrawHorizontalLayout(Vector2 availableSize)
        {
            float viewWidth = (availableSize.X - (ImGui.GetStyle().ItemSpacing.X * 2)) / 3;
            DrawView(0, new Vector2(viewWidth, availableSize.Y), "XY (Axial)");
            ImGui.SameLine();
            DrawView(1, new Vector2(viewWidth, availableSize.Y), "XZ (Coronal)");
            ImGui.SameLine();
            DrawView(2, new Vector2(viewWidth, availableSize.Y), "YZ (Sagittal)");
        }
        
        private void DrawVerticalLayout(Vector2 availableSize)
        {
            float viewHeight = (availableSize.Y - (ImGui.GetStyle().ItemSpacing.Y * 2)) / 3;
            DrawView(0, new Vector2(availableSize.X, viewHeight), "XY (Axial)");
            DrawView(1, new Vector2(availableSize.X, viewHeight), "XZ (Coronal)");
            DrawView(2, new Vector2(availableSize.X, viewHeight), "YZ (Sagittal)");
        }
        
        private void DrawGrid2x2Layout(Vector2 availableSize)
        {
            float viewWidth = (availableSize.X - ImGui.GetStyle().ItemSpacing.X) / 2;
            float viewHeight = (availableSize.Y - ImGui.GetStyle().ItemSpacing.Y) / 2;
            
            DrawView(0, new Vector2(viewWidth, viewHeight), "XY (Axial)");
            ImGui.SameLine();
            DrawView(1, new Vector2(viewWidth, viewHeight), "XZ (Coronal)");
            
            DrawView(2, new Vector2(viewWidth, viewHeight), "YZ (Sagittal)");
            ImGui.SameLine();
            
            // Placeholder for the 4th view
            ImGui.BeginChild("PlaceholderView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
            var text = "3D View (Placeholder)";
            var textSize = ImGui.CalcTextSize(text);
            ImGui.SetCursorPos(ImGui.GetCursorPos() + (new Vector2(viewWidth, viewHeight) - textSize) * 0.5f);
            ImGui.TextDisabled(text);
            ImGui.EndChild();
        }
        
        private void DrawView(int viewIndex, Vector2 size, string title)
        {
            ImGui.BeginChild($"View{viewIndex}", size, ImGuiChildFlags.Border);
            ImGui.Text(title);
            ImGui.SameLine();
    
            ChunkedVolume currentVolume = GetCurrentVolume();
            if (currentVolume != null)
            {
                int slice = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 };
                int maxSlice = viewIndex switch { 0 => currentVolume.Depth - 1, 1 => currentVolume.Height - 1, 2 => currentVolume.Width - 1, _ => 0 };
                ImGui.SetNextItemWidth(150);
                if (ImGui.SliderInt($"##Slice{viewIndex}", ref slice, 0, maxSlice))
                {
                    switch (viewIndex) { case 0: _sliceZ = slice; _needsUpdateXY = true; break; case 1: _sliceY = slice; _needsUpdateXZ = true; break; case 2: _sliceX = slice; _needsUpdateYZ = true; break; }
                }
                ImGui.SameLine();
                ImGui.Text($"{slice + 1}/{maxSlice + 1}");
            }
    
            ImGui.Separator();
            DrawSliceView(viewIndex);
            ImGui.EndChild();
        }

        private void DrawSliceView(int viewIndex)
        {
            var io = ImGui.GetIO();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            var dl = ImGui.GetWindowDrawList();
            
            ImGui.InvisibleButton($"canvas{viewIndex}", canvasSize);
            bool isHovered = ImGui.IsItemHovered();
            
            // Handle zoom and pan
            float zoom = viewIndex switch { 0 => _zoomXY, 1 => _zoomXZ, _ => _zoomYZ };
            Vector2 pan = viewIndex switch { 0 => _panXY, 1 => _panXZ, _ => _panYZ };

            if (isHovered && io.MouseWheel != 0)
            {
                float zoomDelta = io.MouseWheel * 0.1f;
                float newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
                Vector2 mouseCanvasPos = io.MousePos - canvasPos - canvasSize * 0.5f;
                pan -= mouseCanvasPos * (newZoom / zoom - 1.0f);
                zoom = newZoom;
            }
            if (isHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle)) pan += io.MouseDelta;
            
            switch(viewIndex) { case 0: _zoomXY = zoom; _panXY = pan; break; case 1: _zoomXZ = zoom; _panXZ = pan; break; case 2: _zoomYZ = zoom; _panYZ = pan; break; }

            // --- INTERACTION LOGIC ---
            if (isHovered)
            {
                // Point Probing (on right-click or holding Ctrl)
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || (ImGui.IsMouseDown(ImGuiMouseButton.Left) && io.KeyCtrl))
                {
                    HandlePointProbing(io, canvasPos, canvasSize, zoom, pan, viewIndex);
                }

                // Line Drawing for FFT
                if (AcousticInteractionManager.InteractionMode == ViewerInteractionMode.DrawingLine)
                {
                    HandleLineDrawing(io, canvasPos, canvasSize, zoom, pan, viewIndex);
                }
            }
            
            // Update texture if needed
            bool needsUpdate = viewIndex switch { 0 => _needsUpdateXY, 1 => _needsUpdateXZ, _ => _needsUpdateYZ };
            TextureManager texture = viewIndex switch { 0 => _textureXY, 1 => _textureXZ, _ => _textureYZ };
            if (needsUpdate || texture == null || !texture.IsValid)
            {
                UpdateTexture(viewIndex, ref texture);
                switch(viewIndex) { case 0: _textureXY = texture; _needsUpdateXY = false; break; case 1: _textureXZ = texture; _needsUpdateXZ = false; break; case 2: _textureYZ = texture; _needsUpdateYZ = false; break; }
            }
            
            dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);
            
            if (texture != null && texture.IsValid && GetCurrentVolume() != null)
            {
                var (width, height) = GetImageDimensionsForView(viewIndex, GetCurrentVolume());
                var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height); // CORRECTED CALL
                
                dl.PushClipRect(canvasPos, canvasPos + canvasSize, true);
                dl.AddImage(texture.GetImGuiTextureId(), imagePos, imagePos + imageSize);
                if (_showGrid) DrawGrid(dl, imagePos, imageSize, 10);
                dl.PopClipRect();

                // Draw line in progress on top of the image
                if (_isDrawingLine && AcousticInteractionManager.LineViewIndex == viewIndex)
                {
                    dl.AddLine(_lineStartPos, io.MousePos, ImGui.GetColorU32(new Vector4(1, 1, 0, 0.8f)), 2.0f);
                }
            }
        }      

        /// <summary>
        /// Handles user interaction for probing data at a specific point in a slice.
        /// </summary>
        private void HandlePointProbing(ImGuiIOPtr io, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan, int viewIndex)
        {
            var volume = GetCurrentVolume();
            if (volume == null) return;
            
            var (width, height) = GetImageDimensionsForView(viewIndex, volume);
            var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height); // CORRECTED CALL
            Vector2 mouseImgCoord = (io.MousePos - imagePos) / imageSize;

            if (mouseImgCoord.X >= 0 && mouseImgCoord.X <= 1 && mouseImgCoord.Y >= 0 && mouseImgCoord.Y <= 1)
            {
                int x = (int)(mouseImgCoord.X * width);
                int y = (int)(mouseImgCoord.Y * height);

                // Convert 2D view coordinates to 3D volume coordinates
                var (volX, volY, volZ) = viewIndex switch
                {
                    0 => (x, y, _sliceZ),
                    1 => (x, _sliceY, y),
                    _ => (_sliceX, x, y)
                };
                
                // Clamp coordinates to be safe
                var baseVolume = _dataset.CombinedWaveField ?? _dataset.PWaveField;
                if (baseVolume == null) return;

                volX = Math.Clamp(volX, 0, baseVolume.Width - 1);
                volY = Math.Clamp(volY, 0, baseVolume.Height - 1);
                volZ = Math.Clamp(volZ, 0, baseVolume.Depth - 1);

                string tooltip = $"Voxel: ({volX}, {volY}, {volZ})\n";
                
                // Query calibrated density data if available
                if (_dataset.DensityData != null)
                {
                    tooltip += $"Density: {_dataset.DensityData.GetDensity(volX, volY, volZ):F0} kg/m³\n" +
                               $"Vp: {_dataset.DensityData.GetPWaveVelocity(volX, volY, volZ):F0} m/s\n" +
                               $"Vs: {_dataset.DensityData.GetSWaveVelocity(volX, volY, volZ):F0} m/s";
                }
                else { tooltip += "Density data not calibrated."; }

                // Query damage field if available
                if (_dataset.DamageField != null)
                {
                    tooltip += $"\nDamage: {_dataset.DamageField[volX, volY, volZ] / 255.0f:P1}";
                }

                ImGui.SetTooltip(tooltip);
            }
        }

        /// <summary>
        /// Handles the UI logic for drawing a line for FFT analysis.
        /// </summary>
        private void HandleLineDrawing(ImGuiIOPtr io, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan, int viewIndex)
        {
            ImGui.SetTooltip("Click and drag to define a line for analysis.\nPress ESC or 'Cancel' in Analysis tool to exit.");
            
            var (width, height) = GetImageDimensionsForView(viewIndex, GetCurrentVolume());
            var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height); // CORRECTED CALL

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _isDrawingLine = true;
                _lineStartPos = io.MousePos;
                AcousticInteractionManager.IsLineDefinitionActive = true;
                AcousticInteractionManager.LineViewIndex = viewIndex;
            }

            if (_isDrawingLine && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _isDrawingLine = false;
                
                // Convert screen-space start/end points to pixel coordinates within the slice image
                Vector2 startPixel = ((_lineStartPos - imagePos) / imageSize) * new Vector2(width, height);
                Vector2 endPixel = ((io.MousePos - imagePos) / imageSize) * new Vector2(width, height);
                
                int sliceIndex = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 };

                // Finalize the line and notify the analysis tool
                AcousticInteractionManager.FinalizeLine(sliceIndex, viewIndex, startPixel, endPixel);
            }
        }
        
        private void UpdateTexture(int viewIndex, ref TextureManager texture)
        {
            ChunkedVolume volume = GetCurrentVolume();
            if (volume == null) return;
            
            try
            {
                var (width, height) = GetImageDimensionsForView(viewIndex, volume);
                byte[] sliceData = ExtractSliceData(viewIndex, volume, width, height);
                
                byte[] rgbaData = ApplyColorMap(sliceData, width, height);
                
                texture?.Dispose();
                texture = TextureManager.CreateFromPixelData(rgbaData, (uint)width, (uint)height);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticVolumeViewer] Error updating texture: {ex.Message}");
            }
        }
        
        private byte[] ExtractSliceData(int viewIndex, ChunkedVolume volume, int width, int height)
        {
            byte[] data = new byte[width * height];
            
            switch (viewIndex)
            {
                case 0: volume.ReadSliceZ(_sliceZ, data); break;
                case 1: for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) data[z * width + x] = volume[x, _sliceY, z]; break;
                case 2: for (int z = 0; z < height; z++) for (int y = 0; y < width; y++) data[z * width + y] = volume[_sliceX, y, z]; break;
            }
            
            ApplyContrast(data);
            return data;
        }
        
        private void ApplyContrast(byte[] data)
        {
            float min = _contrastMin * 255;
            float max = _contrastMax * 255;
            float range = Math.Max(1e-5f, max - min);
            
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)Math.Clamp((data[i] - min) / range * 255, 0, 255);
            }
        }
        
        private byte[] ApplyColorMap(byte[] data, int width, int height)
        {
            byte[] rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                Vector4 color = GetColorFromMap(data[i] / 255f);
                rgba[i * 4 + 0] = (byte)(color.X * 255);
                rgba[i * 4 + 1] = (byte)(color.Y * 255);
                rgba[i * 4 + 2] = (byte)(color.Z * 255);
                rgba[i * 4 + 3] = 255;
            }
            return rgba;
        }
        
        private Vector4 GetColorFromMap(float value)
        {
            value = Math.Clamp(value, 0, 1);

            // Use a specific color map for damage to make it stand out
            if (_currentMode == VisualizationMode.DamageField)
            {
                return GetHotColor(value);
            }
            
            return _colorMapIndex switch
            {
                0 => new Vector4(value, value, value, 1),
                1 => GetJetColor(value),
                2 => GetViridisColor(value),
                3 => GetHotColor(value),
                4 => GetCoolColor(value),
                5 => GetSeismicColor(value),
                _ => new Vector4(value, value, value, 1)
            };
        }
        
        #region Color Map Functions
        private Vector4 GetJetColor(float v)
        {
            v = Math.Clamp(v, 0.0f, 1.0f);
            Vector4 c = new Vector4(1.0f, 1.0f, 1.0f, 1.0f); // Default white
        
            if (v < 0.125f) {
                c.X = 0.0f;
                c.Y = 0.0f;
                c.Z = 0.5f + 4.0f * v; // 0.5 -> 1 (dark blue to blue)
            } else if (v < 0.375f) {
                c.X = 0.0f;
                c.Y = 4.0f * (v - 0.125f); // 0 -> 1 (blue to cyan)
                c.Z = 1.0f;
            } else if (v < 0.625f) {
                c.X = 4.0f * (v - 0.375f); // 0 -> 1 (cyan to green to yellow)
                c.Y = 1.0f;
                c.Z = 1.0f - 4.0f * (v - 0.375f);
            } else if (v < 0.875f) {
                c.X = 1.0f;
                c.Y = 1.0f - 4.0f * (v - 0.625f); // 1 -> 0 (yellow to red)
                c.Z = 0.0f;
            } else {
                c.X = 1.0f - 4.0f * (v - 0.875f); // 1 -> 0.5 (red to dark red)
                c.Y = 0.0f;
                c.Z = 0.0f;
            }
            return new Vector4(c.X, c.Y, c.Z, 1.0f);
        }
        
        private Vector4 GetViridisColor(float t)
        {
            // Polynomial coefficients for Viridis
            float r = 0.26700f + t * (2.05282f - t * (29.25595f - t * (127.35689f - t * (214.53428f - t * 128.38883f))));
            float g = 0.00497f + t * (1.10748f + t * (4.29528f  - t * (4.93638f  + t * ( -7.42203f + t * 4.02493f))));
            float b = 0.32942f + t * (0.45984f - t * (5.58064f  + t * (27.20658f - t * (50.11327f + t * 28.18927f))));
            return new Vector4(Math.Clamp(r, 0.0f, 1.0f), Math.Clamp(g, 0.0f, 1.0f), Math.Clamp(b, 0.0f, 1.0f), 1.0f);
        }
        
        private Vector4 GetHotColor(float v)
        {
            v = Math.Clamp(v, 0.0f, 1.0f);
            float r = Math.Clamp(v / 0.4f, 0.0f, 1.0f);
            float g = Math.Clamp((v - 0.4f) / 0.4f, 0.0f, 1.0f);
            float b = Math.Clamp((v - 0.8f) / 0.2f, 0.0f, 1.0f);
            return new Vector4(r, g, b, 1.0f);
        }
        
        private Vector4 GetCoolColor(float v) => new Vector4(v, 1 - v, 1, 1);
        
        private Vector4 GetSeismicColor(float v)
        {
            v = Math.Clamp(v, 0.0f, 1.0f);
            // Diverging: Blue -> White -> Red
            // v = 0.0 -> Blue (0,0,1)
            // v = 0.5 -> White (1,1,1)
            // v = 1.0 -> Red (1,0,0)
            if (v < 0.5f)
            {
                float t = v * 2.0f; // remaps [0, 0.5] to [0, 1]
                return new Vector4(t, t, 1.0f, 1.0f); // Interpolate from Blue to White
            }
            else
            {
                float t = (v - 0.5f) * 2.0f; // remaps [0.5, 1] to [0, 1]
                return new Vector4(1.0f, 1.0f - t, 1.0f - t, 1.0f); // Interpolate from White to Red
            }
        }
        #endregion

        private void DrawInfoPanel()
        {
            ImGui.Text("Acoustic Simulation Results");
            ImGui.Separator();
            
            if (_showInfo)
            {
                ImGui.Text($"P-Wave Velocity: {_dataset.PWaveVelocity:F2} m/s");
                ImGui.Text($"S-Wave Velocity: {_dataset.SWaveVelocity:F2} m/s");
                ImGui.Spacing();
                ImGui.Text("Material Properties (Simulation Input):");
                ImGui.Indent();
                ImGui.Text($"Young's Modulus: {_dataset.YoungsModulusMPa:F0} MPa");
                ImGui.Text($"Poisson's Ratio: {_dataset.PoissonRatio:F3}");
                ImGui.Unindent();
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            
            ImGui.Text("Display Settings:");
            ImGui.SetNextItemWidth(-1);
            if(ImGui.DragFloatRange2("Contrast", ref _contrastMin, ref _contrastMax, 0.01f, 0.0f, 1.0f))
            {
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            }
            
            ImGui.Spacing();
            ImGui.Text("Layout:");
            if (ImGui.RadioButton("Horizontal", _layout == Layout.Horizontal)) _layout = Layout.Horizontal; ImGui.SameLine();
            if (ImGui.RadioButton("Vertical", _layout == Layout.Vertical)) _layout = Layout.Vertical; ImGui.SameLine();
            if (ImGui.RadioButton("2x2 Grid", _layout == Layout.Grid2x2)) _layout = Layout.Grid2x2;
        }
        
        private void DrawLegend()
        {
            ImGui.SetNextWindowPos(new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - 160, ImGui.GetWindowPos().Y + 50), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(150, 180), ImGuiCond.FirstUseEver);
            
            if (ImGui.Begin("Legend", ref _showLegend, ImGuiWindowFlags.AlwaysAutoResize))
            {
                string colorMapName = (_currentMode == VisualizationMode.DamageField) ? "Hot" : _colorMapNames[_colorMapIndex];
                ImGui.Text($"Color Map: {colorMapName}");
                ImGui.Separator();
                
                var drawList = ImGui.GetWindowDrawList();
                var pos = ImGui.GetCursorScreenPos();
                float width = 30, height = 100;
                
                for (int i = 0; i < height; i++)
                {
                    Vector4 color = GetColorFromMap(1.0f - (float)i / height);
                    drawList.AddRectFilled(new Vector2(pos.X, pos.Y + i), new Vector2(pos.X + width, pos.Y + i + 1), ImGui.GetColorU32(color));
                }
                
                ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y - 5)); ImGui.Text("High");
                ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height - 15)); ImGui.Text("Low");
            }
            ImGui.End();
        }
        
        private void DrawGrid(ImDrawListPtr dl, Vector2 pos, Vector2 size, int divisions)
        {
            uint color = 0x40FFFFFF;
            for (int i = 1; i < divisions; i++)
            {
                float x = pos.X + (size.X / divisions) * i;
                float y = pos.Y + (size.Y / divisions) * i;
                dl.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + size.Y), color, 0.5f);
                dl.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + size.X, y), color, 0.5f);
            }
        }
        
        private ChunkedVolume GetCurrentVolume()
        {
            if (_currentMode == VisualizationMode.TimeSeries && _dataset.TimeSeriesSnapshots?.Count > 0)
            {
                // Note: Time-series visualization is simplified and may not be performant for large datasets.
                var snapshot = _dataset.TimeSeriesSnapshots[_currentFrameIndex];
                var field = snapshot.GetVelocityField(0); // Show X component for now
                if (field == null) return null;
                
                var volume = new ChunkedVolume(snapshot.Width, snapshot.Height, snapshot.Depth);
                for (int z = 0; z < snapshot.Depth; z++)
                {
                    byte[] slice = new byte[snapshot.Width * snapshot.Height];
                    for (int y = 0; y < snapshot.Height; y++)
                        for (int x = 0; x < snapshot.Width; x++)
                            slice[y * snapshot.Width + x] = (byte)(Math.Clamp((field[x, y, z] + 1) * 127.5f, 0, 255));
                    volume.WriteSliceZ(z, slice);
                }
                return volume;
            }
            
            return _currentMode switch
            {
                VisualizationMode.PWaveField => _dataset.PWaveField,
                VisualizationMode.SWaveField => _dataset.SWaveField,
                VisualizationMode.CombinedField => _dataset.CombinedWaveField,
                VisualizationMode.DamageField => _dataset.DamageField,
                _ => _dataset.CombinedWaveField
            };
        }
        
        private (int width, int height) GetImageDimensionsForView(int viewIndex, ChunkedVolume volume)
        {
            if (volume == null) return (1, 1);
            return viewIndex switch { 0 => (volume.Width, volume.Height), 1 => (volume.Width, volume.Depth), _ => (volume.Height, volume.Depth) };
        }
        
        private (Vector2 pos, Vector2 size) GetImageDisplayMetrics(Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan, int imageWidth, int imageHeight)
        {
            float imageAspect = (float)imageWidth / imageHeight;
            float canvasAspect = canvasSize.X / canvasSize.Y;
            
            Vector2 imageDisplaySize = (imageAspect > canvasAspect) ? new Vector2(canvasSize.X, canvasSize.X / imageAspect) : new Vector2(canvasSize.Y * imageAspect, canvasSize.Y);
            
            imageDisplaySize *= zoom;
            Vector2 imageDisplayPos = canvasPos + (canvasSize - imageDisplaySize) * 0.5f + pan;
            return (imageDisplayPos, imageDisplaySize);
        }
        
        private void ResetViews()
        {
            var volume = GetCurrentVolume() ?? _dataset.CombinedWaveField;
            if (volume != null)
            {
                _sliceX = volume.Width / 2;
                _sliceY = volume.Height / 2;
                _sliceZ = volume.Depth / 2;
            }
            _zoomXY = _zoomXZ = _zoomYZ = 1.0f;
            _panXY = _panXZ = _panYZ = Vector2.Zero;
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            _contrastMin = 0.0f;
            _contrastMax = 1.0f;
        }
        
        public void Dispose()
        {
            _textureXY?.Dispose();
            _textureXZ?.Dispose();
            _textureYZ?.Dispose();
        }
    }
}