// GeoscientistToolkit/Data/CtImageStack/CtCombinedViewer.cs
using System;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Combined viewer showing 3 orthogonal slices + 3D volume rendering
    /// </summary>
    public class CtCombinedViewer : IDatasetViewer, IDisposable
    {
        private readonly CtImageStackDataset _dataset;
        private readonly StreamingCtVolumeDataset _streamingDataset;
        private readonly CtVolume3DViewer _volumeViewer;

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

        // Window/Level
        private float _windowLevel = 128;
        private float _windowWidth = 255;

        // View settings
        private bool _showCrosshairs = true;
        private bool _syncViews = true;
        private bool _showScaleBar = true; // --- ADDED ---

        // Zoom and pan for each view
        private float _zoomXY = 1.0f;
        private float _zoomXZ = 1.0f;
        private float _zoomYZ = 1.0f;
        private Vector2 _panXY = Vector2.Zero;
        private Vector2 _panXZ = Vector2.Zero;
        private Vector2 _panYZ = Vector2.Zero;

        private enum ViewMode { Combined, SlicesOnly, VolumeOnly, XYOnly, XZOnly, YZOnly }
        private ViewMode _viewMode = ViewMode.Combined;

        public CtCombinedViewer(CtImageStackDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            _dataset.Load();
            _sliceX = _dataset.Width / 2;
            _sliceY = _dataset.Height / 2;
            _sliceZ = _dataset.Depth / 2;
            _streamingDataset = FindStreamingDataset();
            if (_streamingDataset != null)
            {
                _volumeViewer = new CtVolume3DViewer(_streamingDataset);
            }
        }

        private StreamingCtVolumeDataset FindStreamingDataset()
        {
            foreach (var dataset in ProjectManager.Instance.LoadedDatasets)
            {
                if (dataset is StreamingCtVolumeDataset streaming && streaming.EditablePartner == _dataset)
                {
                    return streaming;
                }
            }
            return null;
        }

        public void DrawToolbarControls()
        {
            string[] viewModes = { "Combined", "Slices Only", "3D Only", "XY (Axial) Only", "XZ (Coronal) Only", "YZ (Sagittal) Only" };
            int currentMode = (int)_viewMode;
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("View", ref currentMode, viewModes, viewModes.Length))
            {
                _viewMode = (ViewMode)currentMode;
            }

            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();

            switch (_viewMode)
            {
                case ViewMode.Combined:
                case ViewMode.SlicesOnly:
                case ViewMode.XYOnly:
                case ViewMode.XZOnly:
                case ViewMode.YZOnly:
                    ImGui.Text("W/L:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(80);
                    if (ImGui.DragFloat("##Window", ref _windowWidth, 1f, 1f, 255f, "W: %.0f"))
                    {
                        _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                    }
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(80);
                    if (ImGui.DragFloat("##Level", ref _windowLevel, 1f, 0f, 255f, "L: %.0f"))
                    {
                        _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                    }

                    ImGui.SameLine();
                    ImGui.Separator();
                    ImGui.SameLine();

                    ImGui.Checkbox("Crosshairs", ref _showCrosshairs);
                    ImGui.SameLine();
                    ImGui.Checkbox("Scale Bar", ref _showScaleBar); // --- ADDED ---
                    ImGui.SameLine();
                    ImGui.Checkbox("Sync Views", ref _syncViews);

                    ImGui.SameLine();
                    if (ImGui.Button("Reset Views"))
                    {
                        ResetViews();
                    }
                    break;

                case ViewMode.VolumeOnly:
                    _volumeViewer?.DrawToolbarControls();
                    break;
            }
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            switch (_viewMode)
            {
                case ViewMode.Combined:
                    DrawCombinedView();
                    break;
                case ViewMode.SlicesOnly:
                    DrawSlicesOnlyView();
                    break;
                case ViewMode.VolumeOnly:
                    if (_volumeViewer != null)
                    {
                        _volumeViewer.DrawContent(ref zoom, ref pan);
                    }
                    else
                    {
                        ImGui.Text("No 3D volume dataset available.");
                        ImGui.TextWrapped("To enable 3D viewing, import this dataset with the 'Optimized for 3D' option.");
                    }
                    break;
                case ViewMode.XYOnly:
                    DrawSliceView(0, "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
                    break;
                case ViewMode.XZOnly:
                    DrawSliceView(1, "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
                    break;
                case ViewMode.YZOnly:
                    DrawSliceView(2, "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
                    break;
            }
        }

        private void DrawCombinedView()
        {
            var availableSize = ImGui.GetContentRegionAvail();
            float viewWidth = (availableSize.X - 2) / 2;
            float viewHeight = (availableSize.Y - 2) / 2;

            ImGui.BeginChild("XY_View", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
            DrawSliceView(0, "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
            ImGui.EndChild();

            ImGui.SameLine(0, 2);

            ImGui.BeginChild("XZ_View", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
            DrawSliceView(1, "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
            ImGui.EndChild();

            ImGui.BeginChild("YZ_View", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
            DrawSliceView(2, "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
            ImGui.EndChild();

            ImGui.SameLine(0, 2);

            ImGui.BeginChild("3D_View", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
            if (_volumeViewer != null)
            {
                ImGui.Text("3D Volume");
                ImGui.Separator();
                var contentSize = ImGui.GetContentRegionAvail();
                ImGui.BeginChild("3D_Content", contentSize);
                var dummyZoom = 1.0f;
                var dummyPan = Vector2.Zero;
                _volumeViewer.DrawContent(ref dummyZoom, ref dummyPan);
                ImGui.EndChild();
            }
            else
            {
                ImGui.Text("3D Volume (Not Available)");
                ImGui.Separator();
                ImGui.TextWrapped("Import with 'Optimized for 3D' option to enable volume rendering.");
            }
            ImGui.EndChild();
        }

        private void DrawSlicesOnlyView()
        {
            var availableSize = ImGui.GetContentRegionAvail();
            float spacing = 4;
            float totalWidth = availableSize.X - spacing * 2;
            float viewWidth = totalWidth / 3;
            float viewHeight = availableSize.Y;

            if (viewWidth < 200)
            {
                viewWidth = availableSize.X;
                viewHeight = (availableSize.Y - spacing * 2) / 3;
                DrawSliceView(0, "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
                ImGui.Dummy(new Vector2(0, spacing));
                DrawSliceView(1, "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
                ImGui.Dummy(new Vector2(0, spacing));
                DrawSliceView(2, "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
            }
            else
            {
                ImGui.BeginChild("XY_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(0, "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
                ImGui.EndChild();
                ImGui.SameLine(0, spacing);
                ImGui.BeginChild("XZ_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(1, "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
                ImGui.EndChild();
                ImGui.SameLine(0, spacing);
                ImGui.BeginChild("YZ_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(2, "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
                ImGui.EndChild();
            }
        }

        private void DrawSliceView(int viewIndex, string title, ref float zoom, ref Vector2 pan, ref bool needsUpdate, ref TextureManager texture)
        {
            ImGui.Text(title);
            ImGui.SameLine();
            int slice = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 };
            int maxSlice = viewIndex switch { 0 => _dataset.Depth - 1, 1 => _dataset.Height - 1, 2 => _dataset.Width - 1, _ => 0 };
            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderInt($"##Slice{viewIndex}", ref slice, 0, maxSlice))
            {
                switch (viewIndex)
                {
                    case 0: _sliceZ = slice; needsUpdate = true; if (_syncViews && _volumeViewer != null) { _volumeViewer.SlicePositions = new Vector3((float)_sliceX / _dataset.Width, (float)_sliceY / _dataset.Height, (float)_sliceZ / _dataset.Depth); } break;
                    case 1: _sliceY = slice; needsUpdate = true; if (_syncViews && _volumeViewer != null) { _volumeViewer.SlicePositions = new Vector3((float)_sliceX / _dataset.Width, (float)_sliceY / _dataset.Height, (float)_sliceZ / _dataset.Depth); } break;
                    case 2: _sliceX = slice; needsUpdate = true; if (_syncViews && _volumeViewer != null) { _volumeViewer.SlicePositions = new Vector3((float)_sliceX / _dataset.Width, (float)_sliceY / _dataset.Height, (float)_sliceZ / _dataset.Depth); } break;
                }
            }
            ImGui.SameLine();
            ImGui.Text($"{slice + 1}/{maxSlice + 1}");
            ImGui.Separator();
            DrawSingleSlice(viewIndex, ref zoom, ref pan, ref needsUpdate, ref texture);
        }

        private void DrawSingleSlice(int viewIndex, ref float zoom, ref Vector2 pan, ref bool needsUpdate, ref TextureManager texture)
        {
            var io = ImGui.GetIO();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            var dl = ImGui.GetWindowDrawList();
            ImGui.InvisibleButton($"canvas{viewIndex}", canvasSize);
            bool isHovered = ImGui.IsItemHovered();
            bool isActive = ImGui.IsItemActive();
            if (isHovered && io.MouseWheel != 0)
            {
                float zoomDelta = io.MouseWheel * 0.1f;
                float newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
                if (newZoom != zoom)
                {
                    Vector2 mousePos = io.MousePos - canvasPos - canvasSize * 0.5f;
                    pan -= mousePos * (newZoom / zoom - 1.0f);
                    zoom = newZoom;
                    if (_syncViews) { _zoomXY = _zoomXZ = _zoomYZ = zoom; }
                }
            }
            if (isActive && ImGui.IsMouseDragging(ImGuiMouseButton.Middle)) { pan += io.MouseDelta; }
            if (isHovered && io.MouseWheel != 0 && io.KeyCtrl)
            {
                switch (viewIndex)
                {
                    case 0: _sliceZ = Math.Clamp(_sliceZ + (int)io.MouseWheel, 0, _dataset.Depth - 1); needsUpdate = true; break;
                    case 1: _sliceY = Math.Clamp(_sliceY + (int)io.MouseWheel, 0, _dataset.Height - 1); needsUpdate = true; break;
                    case 2: _sliceX = Math.Clamp(_sliceX + (int)io.MouseWheel, 0, _dataset.Width - 1); needsUpdate = true; break;
                }
            }
            if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { UpdateCrosshairFromMouse(viewIndex, canvasPos, canvasSize, zoom, pan); }
            dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);
            if (needsUpdate || texture == null || !texture.IsValid) { UpdateTexture(viewIndex, ref texture); needsUpdate = false; }
            if (texture != null && texture.IsValid)
            {
                var (width, height) = GetImageDimensionsForView(viewIndex);
                float imageAspect = (float)width / height;
                float canvasAspect = canvasSize.X / canvasSize.Y;
                Vector2 imageSize = (imageAspect > canvasAspect) ? new Vector2(canvasSize.X * zoom, canvasSize.X / imageAspect * zoom) : new Vector2(canvasSize.Y * imageAspect * zoom, canvasSize.Y * zoom);
                Vector2 imagePos = canvasPos + canvasSize * 0.5f - imageSize * 0.5f + pan;
                dl.AddImage(texture.GetImGuiTextureId(), imagePos, imagePos + imageSize, Vector2.Zero, Vector2.One, 0xFFFFFFFF);
                if (_showCrosshairs) { DrawCrosshairs(dl, viewIndex, canvasPos, canvasSize, imagePos, imageSize, width, height); }

                // --- MODIFIED: Call DrawScaleBar here ---
                if (_showScaleBar) { DrawScaleBar(dl, canvasPos, canvasSize, zoom, width, height, viewIndex); }
            }
        }

        private void UpdateTexture(int viewIndex, ref TextureManager texture)
        {
            if (_dataset.VolumeData == null) { Logger.Log("[CtCombinedViewer] No volume data available"); return; }
            try
            {
                var (width, height) = GetImageDimensionsForView(viewIndex);
                byte[] imageData = ExtractSliceData(viewIndex, width, height);
                ApplyWindowLevel(imageData);
                byte[] labelData = null;
                if (_dataset.LabelData != null) { labelData = ExtractLabelSliceData(viewIndex, width, height); }
                byte[] rgbaData = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    byte value = imageData[i];
                    if (labelData != null && labelData[i] > 0)
                    {
                        var material = _dataset.Materials.FirstOrDefault(m => m.ID == labelData[i]);
                        if (material != null && material.IsVisible)
                        {
                            rgbaData[i * 4] = (byte)(value * 0.5f + material.Color.X * 255 * 0.5f);
                            rgbaData[i * 4 + 1] = (byte)(value * 0.5f + material.Color.Y * 255 * 0.5f);
                            rgbaData[i * 4 + 2] = (byte)(value * 0.5f + material.Color.Z * 255 * 0.5f);
                            rgbaData[i * 4 + 3] = 255;
                        }
                        else { rgbaData[i * 4] = value; rgbaData[i * 4 + 1] = value; rgbaData[i * 4 + 2] = value; rgbaData[i * 4 + 3] = 255; }
                    }
                    else { rgbaData[i * 4] = value; rgbaData[i * 4 + 1] = value; rgbaData[i * 4 + 2] = value; rgbaData[i * 4 + 3] = 255; }
                }
                texture?.Dispose();
                texture = TextureManager.CreateFromPixelData(rgbaData, (uint)width, (uint)height);
            }
            catch (Exception ex) { Logger.Log($"[CtCombinedViewer] Error updating texture: {ex.Message}"); }
        }

        private byte[] ExtractSliceData(int viewIndex, int width, int height)
        {
            byte[] data = new byte[width * height];
            var volume = _dataset.VolumeData;
            switch (viewIndex)
            {
                case 0: for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) { data[y * width + x] = volume[x, y, _sliceZ]; } } break;
                case 1: for (int z = 0; z < height; z++) { for (int x = 0; x < width; x++) { data[z * width + x] = volume[x, _sliceY, z]; } } break;
                case 2: for (int z = 0; z < height; z++) { for (int y = 0; y < width; y++) { data[z * width + y] = volume[_sliceX, y, z]; } } break;
            }
            return data;
        }

        private byte[] ExtractLabelSliceData(int viewIndex, int width, int height)
        {
            byte[] data = new byte[width * height];
            var labels = _dataset.LabelData;
            switch (viewIndex)
            {
                case 0: labels.ReadSliceZ(_sliceZ, data); break;
                case 1: for (int z = 0; z < height; z++) { for (int x = 0; x < width; x++) { data[z * width + x] = labels[x, _sliceY, z]; } } break;
                case 2: for (int z = 0; z < height; z++) { for (int y = 0; y < width; y++) { data[z * width + y] = labels[_sliceX, y, z]; } } break;
            }
            return data;
        }

        private void ApplyWindowLevel(byte[] data)
        {
            float min = _windowLevel - _windowWidth / 2;
            float max = _windowLevel + _windowWidth / 2;
            Parallel.For(0, data.Length, i => { float value = data[i]; value = (value - min) / (max - min) * 255; data[i] = (byte)Math.Clamp(value, 0, 255); });
        }

        private (int width, int height) GetImageDimensionsForView(int viewIndex)
        {
            return viewIndex switch { 0 => (_dataset.Width, _dataset.Height), 1 => (_dataset.Width, _dataset.Depth), 2 => (_dataset.Height, _dataset.Depth), _ => (_dataset.Width, _dataset.Height) };
        }

        private void UpdateCrosshairFromMouse(int viewIndex, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
        {
            var mousePos = ImGui.GetMousePos() - canvasPos - canvasSize * 0.5f - pan;
            var (width, height) = GetImageDimensionsForView(viewIndex);
            float imageAspect = (float)width / height;
            float canvasAspect = canvasSize.X / canvasSize.Y;
            Vector2 imageSize = (imageAspect > canvasAspect) ? new Vector2(canvasSize.X * zoom, canvasSize.X / imageAspect * zoom) : new Vector2(canvasSize.Y * imageAspect * zoom, canvasSize.Y * zoom);
            float x = (mousePos.X + imageSize.X * 0.5f) / imageSize.X * width;
            float y = (mousePos.Y + imageSize.Y * 0.5f) / imageSize.Y * height;
            switch (viewIndex)
            {
                case 0: _sliceX = Math.Clamp((int)x, 0, _dataset.Width - 1); _sliceY = Math.Clamp((int)y, 0, _dataset.Height - 1); _needsUpdateXZ = _needsUpdateYZ = true; break;
                case 1: _sliceX = Math.Clamp((int)x, 0, _dataset.Width - 1); _sliceZ = Math.Clamp((int)y, 0, _dataset.Depth - 1); _needsUpdateXY = _needsUpdateYZ = true; break;
                case 2: _sliceY = Math.Clamp((int)x, 0, _dataset.Height - 1); _sliceZ = Math.Clamp((int)y, 0, _dataset.Depth - 1); _needsUpdateXY = _needsUpdateXZ = true; break;
            }
        }

        private void DrawCrosshairs(ImDrawListPtr dl, int viewIndex, Vector2 canvasPos, Vector2 canvasSize, Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
        {
            uint color = 0xFF00FF00;
            float x1, y1;
            switch (viewIndex)
            {
                case 0: x1 = (float)_sliceX / imageWidth; y1 = (float)_sliceY / imageHeight; break;
                case 1: x1 = (float)_sliceX / imageWidth; y1 = (float)_sliceZ / imageHeight; break;
                case 2: x1 = (float)_sliceY / imageWidth; y1 = (float)_sliceZ / imageHeight; break;
                default: return;
            }
            float screenX = imagePos.X + x1 * imageSize.X;
            float screenY = imagePos.Y + y1 * imageSize.Y;
            if (screenX >= imagePos.X && screenX <= imagePos.X + imageSize.X) { dl.AddLine(new Vector2(screenX, imagePos.Y), new Vector2(screenX, imagePos.Y + imageSize.Y), color, 1.0f); }
            if (screenY >= imagePos.Y && screenY <= imagePos.Y + imageSize.Y) { dl.AddLine(new Vector2(imagePos.X, screenY), new Vector2(imagePos.X + imageSize.X, screenY), color, 1.0f); }
        }

        // --- ADDED ---
        private void DrawScaleBar(ImDrawListPtr dl, Vector2 canvasPos, Vector2 canvasSize, float zoom, int imageWidth, int imageHeight, int viewIndex)
        {
            float pixelSizeInUnits = viewIndex switch
            {
                0 => _dataset.PixelSize,
                1 => (_dataset.PixelSize + _dataset.SliceThickness) / 2,
                2 => (_dataset.PixelSize + _dataset.SliceThickness) / 2,
                _ => _dataset.PixelSize
            };

            float scaleFactor = canvasSize.X / imageWidth * zoom;
            float[] possibleLengths = { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 };
            float bestLength = possibleLengths[0];
            foreach (float length in possibleLengths)
            {
                if (length / pixelSizeInUnits * scaleFactor <= 150) bestLength = length;
            }

            float barLengthPixels = bestLength / pixelSizeInUnits * scaleFactor;
            Vector2 barPos = canvasPos + new Vector2(canvasSize.X - barLengthPixels - 20, canvasSize.Y - 40);

            dl.AddRectFilled(barPos - new Vector2(5, 5), barPos + new Vector2(barLengthPixels + 5, 25), 0xAA000000, 3.0f);
            dl.AddLine(barPos, barPos + new Vector2(barLengthPixels, 0), 0xFFFFFFFF, 3.0f);
            dl.AddLine(barPos, barPos + new Vector2(0, 5), 0xFFFFFFFF, 3.0f);
            dl.AddLine(barPos + new Vector2(barLengthPixels, 0), barPos + new Vector2(barLengthPixels, 5), 0xFFFFFFFF, 3.0f);

            string text = bestLength >= 1000 ? $"{bestLength / 1000:F1} mm" : $"{bestLength:F0} {_dataset.Unit}";
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
            _textureXY?.Dispose();
            _textureXZ?.Dispose();
            _textureYZ?.Dispose();
            _volumeViewer?.Dispose();
        }
    }
}