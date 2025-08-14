// GeoscientistToolkit/Data/AcousticVolume/AcousticVolumeViewer.cs
using System;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.Data.AcousticVolume
{
    /// <summary>
    /// Viewer for acoustic simulation results with wave field visualization
    /// and time-series animation capabilities
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
        
        public AcousticVolumeViewer(AcousticVolumeDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            _dataset.Load();
            
            if (_dataset.CombinedWaveField != null)
            {
                _sliceX = _dataset.CombinedWaveField.Width / 2;
                _sliceY = _dataset.CombinedWaveField.Height / 2;
                _sliceZ = _dataset.CombinedWaveField.Depth / 2;
            }
            
            Logger.Log($"[AcousticVolumeViewer] Initialized viewer for: {_dataset.Name}");
        }
        
        public void DrawToolbarControls()
        {
            // Mode selection
            ImGui.Text("Mode:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            string[] modes = { "P-Wave", "S-Wave", "Combined", "Time Series" };
            int modeIndex = (int)_currentMode;
            if (ImGui.Combo("##Mode", ref modeIndex, modes, modes.Length))
            {
                _currentMode = (VisualizationMode)modeIndex;
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
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
            
            // Draw main content based on layout
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
            
            // Draw legend if enabled
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
                _currentFrameIndex++;
                
                if (_currentFrameIndex >= _dataset.TimeSeriesSnapshots.Count)
                {
                    _currentFrameIndex = 0;
                }
                
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            }
        }
        
        private void DrawHorizontalLayout(Vector2 availableSize)
        {
            float viewWidth = (availableSize.X - 4) / 3;
            float viewHeight = availableSize.Y;
            
            DrawView(0, new Vector2(viewWidth, viewHeight), "XY (Axial)");
            ImGui.SameLine(0, 2);
            DrawView(1, new Vector2(viewWidth, viewHeight), "XZ (Coronal)");
            ImGui.SameLine(0, 2);
            DrawView(2, new Vector2(viewWidth, viewHeight), "YZ (Sagittal)");
        }
        
        private void DrawVerticalLayout(Vector2 availableSize)
        {
            float viewWidth = availableSize.X;
            float viewHeight = (availableSize.Y - 4) / 3;
            
            DrawView(0, new Vector2(viewWidth, viewHeight), "XY (Axial)");
            DrawView(1, new Vector2(viewWidth, viewHeight), "XZ (Coronal)");
            DrawView(2, new Vector2(viewWidth, viewHeight), "YZ (Sagittal)");
        }
        
        private void DrawGrid2x2Layout(Vector2 availableSize)
        {
            float viewWidth = (availableSize.X - 2) / 2;
            float viewHeight = (availableSize.Y - 2) / 2;
            
            DrawView(0, new Vector2(viewWidth, viewHeight), "XY (Axial)");
            ImGui.SameLine(0, 2);
            DrawView(1, new Vector2(viewWidth, viewHeight), "XZ (Coronal)");
            
            DrawView(2, new Vector2(viewWidth, viewHeight), "YZ (Sagittal)");
            ImGui.SameLine(0, 2);
            
            // Info panel in the fourth quadrant
            ImGui.BeginChild("InfoPanel", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
            DrawInfoPanel();
            ImGui.EndChild();
        }
        
        private void DrawView(int viewIndex, Vector2 size, string title)
        {
            ImGui.BeginChild($"View{viewIndex}", size, ImGuiChildFlags.Border);
    
            ImGui.Text(title);
            ImGui.SameLine();
    
            // Slice control
            ChunkedVolume currentVolume = GetCurrentVolume();
            if (currentVolume != null)
            {
                int slice = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 };
                int maxSlice = viewIndex switch 
                { 
                    0 => currentVolume.Depth - 1, 
                    1 => currentVolume.Height - 1, 
                    2 => currentVolume.Width - 1, 
                    _ => 0 
                };
        
                ImGui.SetNextItemWidth(150);
                if (ImGui.SliderInt($"##Slice{viewIndex}", ref slice, 0, maxSlice))
                {
                    switch (viewIndex)
                    {
                        case 0: 
                            _sliceZ = slice; 
                            _needsUpdateXY = true; 
                            break;
                        case 1: 
                            _sliceY = slice; 
                            _needsUpdateXZ = true; 
                            break;
                        case 2: 
                            _sliceX = slice; 
                            _needsUpdateYZ = true; 
                            break;
                    }
                }
        
                ImGui.SameLine();
                ImGui.Text($"{slice + 1}/{maxSlice + 1}");
            }
    
            ImGui.Separator();
    
            // Draw the actual slice
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
    
    // Handle zoom and pan - use if-else instead of switch expression with ref
    float zoom;
    Vector2 pan;
    
    if (viewIndex == 0)
    {
        zoom = _zoomXY;
        pan = _panXY;
    }
    else if (viewIndex == 1)
    {
        zoom = _zoomXZ;
        pan = _panXZ;
    }
    else
    {
        zoom = _zoomYZ;
        pan = _panYZ;
    }
    
    if (isHovered && io.MouseWheel != 0)
    {
        float zoomDelta = io.MouseWheel * 0.1f;
        float newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
        
        if (newZoom != zoom)
        {
            Vector2 mouseCanvasPos = io.MousePos - canvasPos - canvasSize * 0.5f;
            pan -= mouseCanvasPos * (newZoom / zoom - 1.0f);
            zoom = newZoom;
            
            // Update the appropriate field
            if (viewIndex == 0)
            {
                _zoomXY = zoom;
                _panXY = pan;
            }
            else if (viewIndex == 1)
            {
                _zoomXZ = zoom;
                _panXZ = pan;
            }
            else
            {
                _zoomYZ = zoom;
                _panYZ = pan;
            }
        }
    }
    
    if (isHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
    {
        pan += io.MouseDelta;
        
        // Update the appropriate field
        if (viewIndex == 0)
        {
            _panXY = pan;
        }
        else if (viewIndex == 1)
        {
            _panXZ = pan;
        }
        else
        {
            _panYZ = pan;
        }
    }
    
    // Update texture if needed
    bool needsUpdate;
    TextureManager texture;
    
    if (viewIndex == 0)
    {
        needsUpdate = _needsUpdateXY;
        texture = _textureXY;
    }
    else if (viewIndex == 1)
    {
        needsUpdate = _needsUpdateXZ;
        texture = _textureXZ;
    }
    else
    {
        needsUpdate = _needsUpdateYZ;
        texture = _textureYZ;
    }
    
    if (needsUpdate || texture == null || !texture.IsValid)
    {
        UpdateTexture(viewIndex, ref texture);
        
        // Update the appropriate field
        if (viewIndex == 0)
        {
            _textureXY = texture;
            _needsUpdateXY = false;
        }
        else if (viewIndex == 1)
        {
            _textureXZ = texture;
            _needsUpdateXZ = false;
        }
        else
        {
            _textureYZ = texture;
            _needsUpdateYZ = false;
        }
    }
    
    // Draw background
    dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);
    
    // Draw texture
    if (texture != null && texture.IsValid)
    {
        ChunkedVolume volume = GetCurrentVolume();
        if (volume != null)
        {
            var (width, height) = GetImageDimensionsForView(viewIndex, volume);
            var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height);
            
            dl.PushClipRect(canvasPos, canvasPos + canvasSize, true);
            
            // Draw the wave field image
            dl.AddImage(texture.GetImGuiTextureId(), imagePos, imagePos + imageSize, 
                Vector2.Zero, Vector2.One, 0xFFFFFFFF);
            
            // Draw grid if enabled
            if (_showGrid)
            {
                DrawGrid(dl, imagePos, imageSize, 10);
            }
            
            dl.PopClipRect();
        }
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
                
                // Apply color map
                byte[] rgbaData = ApplyColorMap(sliceData, width, height);
                
                // Create or update texture
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
                case 0: // XY slice
                    volume.ReadSliceZ(_sliceZ, data);
                    break;
                    
                case 1: // XZ slice
                    for (int z = 0; z < height; z++)
                        for (int x = 0; x < width; x++)
                            data[z * width + x] = volume[x, _sliceY, z];
                    break;
                    
                case 2: // YZ slice
                    for (int z = 0; z < height; z++)
                        for (int y = 0; y < width; y++)
                            data[z * width + y] = volume[_sliceX, y, z];
                    break;
            }
            
            // Apply contrast
            ApplyContrast(data);
            
            return data;
        }
        
        private void ApplyContrast(byte[] data)
        {
            float min = _contrastMin * 255;
            float max = _contrastMax * 255;
            float range = max - min;
            if (range < 1e-5f) range = 1e-5f;
            
            for (int i = 0; i < data.Length; i++)
            {
                float value = (data[i] - min) / range * 255;
                data[i] = (byte)Math.Clamp(value, 0, 255);
            }
        }
        
        private byte[] ApplyColorMap(byte[] data, int width, int height)
        {
            byte[] rgba = new byte[width * height * 4];
            
            for (int i = 0; i < width * height; i++)
            {
                Vector4 color = GetColorFromMap(data[i] / 255f);
                rgba[i * 4] = (byte)(color.X * 255);
                rgba[i * 4 + 1] = (byte)(color.Y * 255);
                rgba[i * 4 + 2] = (byte)(color.Z * 255);
                rgba[i * 4 + 3] = 255;
            }
            
            return rgba;
        }
        
        private Vector4 GetColorFromMap(float value)
        {
            value = Math.Clamp(value, 0, 1);
            
            return _colorMapIndex switch
            {
                0 => new Vector4(value, value, value, 1), // Grayscale
                1 => GetJetColor(value),
                2 => GetViridisColor(value),
                3 => GetHotColor(value),
                4 => GetCoolColor(value),
                5 => GetSeismicColor(value),
                _ => new Vector4(value, value, value, 1)
            };
        }
        
        private Vector4 GetJetColor(float value)
        {
            float r, g, b;
            
            if (value < 0.25f)
            {
                r = 0;
                g = 4 * value;
                b = 1;
            }
            else if (value < 0.5f)
            {
                r = 0;
                g = 1;
                b = 1 - 4 * (value - 0.25f);
            }
            else if (value < 0.75f)
            {
                r = 4 * (value - 0.5f);
                g = 1;
                b = 0;
            }
            else
            {
                r = 1;
                g = 1 - 4 * (value - 0.75f);
                b = 0;
            }
            
            return new Vector4(r, g, b, 1);
        }
        
        private Vector4 GetViridisColor(float value)
        {
            // Simplified Viridis approximation
            float r = 0.267f + value * (0.003f + value * (0.5f + value * 0.23f));
            float g = 0.0049f + value * (1.1f - value * 0.5f);
            float b = 0.329f + value * (0.45f - value * 0.82f);
            return new Vector4(r, g, b, 1);
        }
        
        private Vector4 GetHotColor(float value)
        {
            float r = Math.Min(1.0f, value * 2.5f);
            float g = Math.Max(0, Math.Min(1.0f, value * 2.5f - 0.5f));
            float b = Math.Max(0, value * 2.5f - 1.5f);
            return new Vector4(r, g, b, 1);
        }
        
        private Vector4 GetCoolColor(float value)
        {
            float r = value;
            float g = 1 - value;
            float b = 1;
            return new Vector4(r, g, b, 1);
        }
        
        private Vector4 GetSeismicColor(float value)
        {
            // Blue to white to red
            if (value < 0.5f)
            {
                float t = value * 2;
                return new Vector4(t, t, 1, 1);
            }
            else
            {
                float t = (value - 0.5f) * 2;
                return new Vector4(1, 1 - t, 1 - t, 1);
            }
        }
        
        private void DrawInfoPanel()
        {
            ImGui.Text("Acoustic Simulation Results");
            ImGui.Separator();
            
            if (_showInfo)
            {
                ImGui.Text($"P-Wave Velocity: {_dataset.PWaveVelocity:F2} m/s");
                ImGui.Text($"S-Wave Velocity: {_dataset.SWaveVelocity:F2} m/s");
                ImGui.Text($"Vp/Vs Ratio: {_dataset.VpVsRatio:F3}");
                ImGui.Spacing();
                
                ImGui.Text("Material Properties:");
                ImGui.Indent();
                ImGui.Text($"Young's Modulus: {_dataset.YoungsModulusMPa:F0} MPa");
                ImGui.Text($"Poisson's Ratio: {_dataset.PoissonRatio:F3}");
                ImGui.Text($"Confining Pressure: {_dataset.ConfiningPressureMPa:F1} MPa");
                ImGui.Unindent();
                ImGui.Spacing();
                
                ImGui.Text("Source Parameters:");
                ImGui.Indent();
                ImGui.Text($"Frequency: {_dataset.SourceFrequencyKHz:F0} kHz");
                ImGui.Text($"Energy: {_dataset.SourceEnergyJ:F2} J");
                ImGui.Unindent();
                ImGui.Spacing();
                
                ImGui.Text($"Time Steps: {_dataset.TimeSteps}");
                ImGui.Text($"Computation Time: {_dataset.ComputationTime.TotalSeconds:F1} s");
                
                if (_dataset.TimeSeriesSnapshots?.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.Text($"Time Series Frames: {_dataset.TimeSeriesSnapshots.Count}");
                    
                    if (_currentMode == VisualizationMode.TimeSeries)
                    {
                        var currentSnapshot = _dataset.TimeSeriesSnapshots[_currentFrameIndex];
                        ImGui.Text($"Current Time: {currentSnapshot.SimulationTime:F6} s");
                    }
                }
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            
            // Contrast controls
            ImGui.Text("Display Settings:");
            ImGui.SetNextItemWidth(-1);
            ImGui.DragFloatRange2("Contrast", ref _contrastMin, ref _contrastMax, 0.01f, 0.0f, 1.0f);
            
            ImGui.Spacing();
            
            // Layout selector
            ImGui.Text("Layout:");
            if (ImGui.RadioButton("Horizontal", _layout == Layout.Horizontal)) _layout = Layout.Horizontal;
            if (ImGui.RadioButton("Vertical", _layout == Layout.Vertical)) _layout = Layout.Vertical;
            if (ImGui.RadioButton("2x2 Grid", _layout == Layout.Grid2x2)) _layout = Layout.Grid2x2;
        }
        
        private void DrawLegend()
        {
            ImGui.SetNextWindowPos(new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - 160, 
                ImGui.GetWindowPos().Y + 50), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(150, 200), ImGuiCond.FirstUseEver);
            
            if (ImGui.Begin("Legend", ref _showLegend, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text($"Color Map: {_colorMapNames[_colorMapIndex]}");
                ImGui.Separator();
                
                // Draw color bar
                var drawList = ImGui.GetWindowDrawList();
                var pos = ImGui.GetCursorScreenPos();
                float width = 30;
                float height = 100;
                
                for (int i = 0; i < height; i++)
                {
                    float value = 1.0f - (float)i / height;
                    Vector4 color = GetColorFromMap(value);
                    uint col = ImGui.GetColorU32(color);
                    drawList.AddRectFilled(
                        new Vector2(pos.X, pos.Y + i),
                        new Vector2(pos.X + width, pos.Y + i + 1),
                        col);
                }
                
                // Draw labels
                ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y - 5));
                ImGui.Text("High");
                ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height - 5));
                ImGui.Text("Low");
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
            if (_currentMode == VisualizationMode.TimeSeries && 
                _dataset.TimeSeriesSnapshots?.Count > 0 &&
                _currentFrameIndex < _dataset.TimeSeriesSnapshots.Count)
            {
                // For time series, we need to create a temporary volume from the snapshot
                // This is simplified - in production you might cache these
                var snapshot = _dataset.TimeSeriesSnapshots[_currentFrameIndex];
                var field = snapshot.GetVelocityField(0); // Get X component for now
                
                if (field != null)
                {
                    var volume = new ChunkedVolume(snapshot.Width, snapshot.Height, snapshot.Depth);
                    
                    // Convert float field to byte volume
                    for (int z = 0; z < snapshot.Depth; z++)
                    {
                        byte[] slice = new byte[snapshot.Width * snapshot.Height];
                        int idx = 0;
                        
                        for (int y = 0; y < snapshot.Height; y++)
                            for (int x = 0; x < snapshot.Width; x++)
                            {
                                float value = field[x, y, z];
                                // Normalize to byte range
                                slice[idx++] = (byte)(Math.Clamp((value + 1) * 127.5f, 0, 255));
                            }
                        
                        volume.WriteSliceZ(z, slice);
                    }
                    
                    return volume;
                }
            }
            
            return _currentMode switch
            {
                VisualizationMode.PWaveField => _dataset.PWaveField,
                VisualizationMode.SWaveField => _dataset.SWaveField,
                VisualizationMode.CombinedField => _dataset.CombinedWaveField,
                _ => _dataset.CombinedWaveField
            };
        }
        
        private (int width, int height) GetImageDimensionsForView(int viewIndex, ChunkedVolume volume)
        {
            if (volume == null) return (1, 1);
            
            return viewIndex switch
            {
                0 => (volume.Width, volume.Height),
                1 => (volume.Width, volume.Depth),
                2 => (volume.Height, volume.Depth),
                _ => (volume.Width, volume.Height)
            };
        }
        
        private (Vector2 pos, Vector2 size) GetImageDisplayMetrics(Vector2 canvasPos, Vector2 canvasSize, 
            float zoom, Vector2 pan, int imageWidth, int imageHeight)
        {
            float imageAspect = (float)imageWidth / imageHeight;
            float canvasAspect = canvasSize.X / canvasSize.Y;
            
            Vector2 imageDisplaySize;
            if (imageAspect > canvasAspect)
                imageDisplaySize = new Vector2(canvasSize.X, canvasSize.X / imageAspect);
            else
                imageDisplaySize = new Vector2(canvasSize.Y * imageAspect, canvasSize.Y);
            
            imageDisplaySize *= zoom;
            Vector2 imageDisplayPos = canvasPos + (canvasSize - imageDisplaySize) * 0.5f + pan;
            
            return (imageDisplayPos, imageDisplaySize);
        }
        
        private void ResetViews()
        {
            ChunkedVolume volume = GetCurrentVolume();
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