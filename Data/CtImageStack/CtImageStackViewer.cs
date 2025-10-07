// GeoscientistToolkit/Data/CtImageStack/CtImageStackViewer.cs
// Multi-viewport CT viewer with synchronized crosshairs

using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;
using Veldrid;
using System.Linq;
using System;
using GeoscientistToolkit.Analysis.RockCoreExtractor;
using GeoscientistToolkit.Analysis.Transform;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.UI.Utils;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackViewer : IDatasetViewer
    {
        private readonly CtImageStackDataset _dataset;

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

        private readonly CtSegmentationIntegration _segmentationManager;
        private Material _selectedMaterialForSegmentation;
        private bool _showSegmentationWindow = false;

        public CtImageStackViewer(CtImageStackDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));

            _dataset.Load();
            ProjectManager.Instance.DatasetDataChanged += OnDatasetDataChanged;
            CtImageStackTools.PreviewChanged += OnPreviewChanged;

            _sliceX = _dataset.Width / 2;
            _sliceY = _dataset.Height / 2;
            _sliceZ = _dataset.Depth / 2;

            CtSegmentationIntegration.Initialize(_dataset);
            _segmentationManager = CtSegmentationIntegration.GetInstance(_dataset);
            _selectedMaterialForSegmentation = _dataset.Materials.FirstOrDefault(m => m.ID != 0);
        }

        private void OnDatasetDataChanged(Dataset dataset)
        {
            if (dataset == _dataset)
            {
                _needsUpdateXY = true;
                _needsUpdateXZ = true;
                _needsUpdateYZ = true;
            }
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
            ImGui.Text("Layout:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            string[] layouts = { "Horizontal", "Vertical", "2×2 Grid" };
            int layoutIndex = (int)_layout;
            if (ImGui.Combo("##Layout", ref layoutIndex, layouts, layouts.Length))
            {
                _layout = (Layout)layoutIndex;
            }

            ImGui.SameLine(); ImGui.Separator(); ImGui.SameLine();

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

            ImGui.SameLine(); ImGui.Separator(); ImGui.SameLine();

            if (ImGui.Button("Segmentation"))
            {
                _showSegmentationWindow = !_showSegmentationWindow;
            }

            ImGui.SameLine(); ImGui.Separator(); ImGui.SameLine();

            ImGui.Checkbox("Crosshairs", ref _showCrosshairs);
            ImGui.SameLine();
            ImGui.Checkbox("Scale Bar", ref _showScaleBar);
            ImGui.SameLine();
            ImGui.Checkbox("Sync Views", ref _syncViews);

            ImGui.SameLine(); ImGui.Separator(); ImGui.SameLine();

            if (ImGui.Button("Reset Views"))
            {
                ResetViews();
            }
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            DrawSegmentationToolWindow();

            var availableSize = ImGui.GetContentRegionAvail();

            switch (_layout)
            {
                case Layout.Horizontal: DrawHorizontalLayout(availableSize); break;
                case Layout.Vertical: DrawVerticalLayout(availableSize); break;
                case Layout.Grid2x2: DrawGrid2x2Layout(availableSize); break;
            }
        }

        private void DrawSegmentationToolWindow()
        {
            if (!_showSegmentationWindow || _segmentationManager == null) return;

            ImGui.SetNextWindowSize(new Vector2(350, 450), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Segmentation Tools##SimpleViewer", ref _showSegmentationWindow))
            {
                string preview = _selectedMaterialForSegmentation?.Name ?? "Select a material...";
                if (ImGui.BeginCombo("Target Material", preview))
                {
                    foreach (var mat in _dataset.Materials.Where(m => m.ID != 0))
                    {
                        if (ImGui.Selectable(mat.Name, mat == _selectedMaterialForSegmentation))
                        {
                            _selectedMaterialForSegmentation = mat;
                        }
                    }
                    ImGui.EndCombo();
                }

                ImGui.Separator();

                _segmentationManager.DrawSegmentationControls(_selectedMaterialForSegmentation);
            }
            ImGui.End();
        }

        private void DrawHorizontalLayout(Vector2 availableSize)
        {
            float viewWidth = (availableSize.X - 4) / 3;
            float viewHeight = availableSize.Y;

            DrawView(0, new Vector2(viewWidth, viewHeight), "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
            ImGui.SameLine(0, 2);
            DrawView(1, new Vector2(viewWidth, viewHeight), "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
            ImGui.SameLine(0, 2);
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

            DrawView(0, new Vector2(viewWidth, viewHeight), "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
            ImGui.SameLine(0, 2);
            DrawView(1, new Vector2(viewWidth, viewHeight), "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);

            DrawView(2, new Vector2(viewWidth, viewHeight), "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
            ImGui.SameLine(0, 2);

            Draw3DInfoPanel(new Vector2(viewWidth, viewHeight));
        }

        private void DrawView(int viewIndex, Vector2 size, string title, ref float zoom, ref Vector2 pan,
            ref bool needsUpdate, ref TextureManager texture)
        {
            ImGui.BeginChild($"View{viewIndex}", size, ImGuiChildFlags.Border);

            // Use the enhanced slice navigation controls
            int slice = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 };
            int maxSlice = viewIndex switch { 0 => _dataset.Depth - 1, 1 => _dataset.Height - 1, 2 => _dataset.Width - 1, _ => 0 };

            if (SliceNavigationHelper.DrawSliceControls(title, ref slice, maxSlice, $"View{viewIndex}"))
            {
                switch (viewIndex)
                {
                    case 0: _sliceZ = slice; needsUpdate = true; break;
                    case 1: _sliceY = slice; needsUpdate = true; break;
                    case 2: _sliceX = slice; needsUpdate = true; break;
                }
            }

            DrawSingleView(viewIndex, ref zoom, ref pan, ref needsUpdate, ref texture);

            ImGui.EndChild();
        }

        private (Vector2 pos, Vector2 size) GetImageDisplayMetrics(Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan, int imageWidth, int imageHeight, int viewIndex)
        {
            float pixelWidth, pixelHeight;

            switch (viewIndex)
            {
                case 0: // XY View
                    pixelWidth = _dataset.PixelSize;
                    pixelHeight = _dataset.PixelSize;
                    break;
                case 1: // XZ View
                    pixelWidth = _dataset.PixelSize;
                    pixelHeight = _dataset.SliceThickness;
                    break;
                case 2: // YZ View
                    pixelWidth = _dataset.PixelSize;
                    pixelHeight = _dataset.SliceThickness;
                    break;
                default:
                    pixelWidth = 1.0f;
                    pixelHeight = 1.0f;
                    break;
            }

            // Handle cases where slice thickness might be zero or invalid
            if (pixelHeight <= 0) pixelHeight = pixelWidth;
            if (pixelWidth <= 0) pixelWidth = 1.0f;

            float imageAspect = (imageWidth * pixelWidth) / (imageHeight * pixelHeight);
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

        private Vector2 GetMousePosInImage(Vector2 mousePos, Vector2 imageDisplayPos, Vector2 imageDisplaySize, int imageWidth, int imageHeight)
        {
            Vector2 mouseRelativeToImage = mousePos - imageDisplayPos;

            return new Vector2(
                (mouseRelativeToImage.X / imageDisplaySize.X) * imageWidth,
                (mouseRelativeToImage.Y / imageDisplaySize.Y) * imageHeight
            );
        }

        private void DrawSingleView(int viewIndex, ref float zoom, ref Vector2 pan,
    ref bool needsUpdate, ref TextureManager texture)
{
    var io = ImGui.GetIO();
    var canvasPos = ImGui.GetCursorScreenPos();
    var canvasSize = ImGui.GetContentRegionAvail();
    var dl = ImGui.GetWindowDrawList();

    ImGui.InvisibleButton($"canvas{viewIndex}", canvasSize);
    bool isHovered = ImGui.IsItemHovered();

    var (width, height) = GetImageDimensionsForView(viewIndex);
    var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height, viewIndex);

    // -------- Overlay-first input (RockCore, Transform) --------
    bool inputHandled = false;
    if (isHovered)
    {
        // Rock Core first
        inputHandled = RockCoreIntegration.HandleMouseInput(_dataset, io.MousePos,
            imagePos, imageSize, width, height, viewIndex,
            ImGui.IsItemClicked(ImGuiMouseButton.Left),
            ImGui.IsMouseDragging(ImGuiMouseButton.Left),
            ImGui.IsMouseReleased(ImGuiMouseButton.Left));

        // Then Transform
        if (!inputHandled)
        {
            inputHandled = TransformIntegration.HandleMouseInput(_dataset, io.MousePos,
                imagePos, imageSize, width, height, viewIndex,
                ImGui.IsItemClicked(ImGuiMouseButton.Left),
                ImGui.IsMouseDragging(ImGuiMouseButton.Left),
                ImGui.IsMouseReleased(ImGuiMouseButton.Left));
        }
    }

    // -------- Viewer interactions (zoom/pan/slice) --------
    if (isHovered && io.MouseWheel != 0)
    {
        float zoomDelta = io.MouseWheel * 0.1f;
        float newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
        if (newZoom != zoom)
        {
            Vector2 mouseCanvasPos = io.MousePos - canvasPos - canvasSize * 0.5f;
            pan -= mouseCanvasPos * (newZoom / zoom - 1.0f);
            zoom = newZoom;
            if (_syncViews) { _zoomXY = _zoomXZ = _zoomYZ = zoom; }
        }
    }

    if (isHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
    {
        pan += io.MouseDelta;
        if (_syncViews)
        {
            _panXY = pan; _panXZ = pan; _panYZ = pan;
        }
    }

    if (!inputHandled && isHovered && io.MouseWheel != 0 && io.KeyCtrl)
    {
        int wheel = (int)io.MouseWheel;
        switch (viewIndex)
        {
            case 0: _sliceZ = Math.Clamp(_sliceZ + wheel, 0, _dataset.Depth - 1); needsUpdate = true; break;
            case 1: _sliceY = Math.Clamp(_sliceY + wheel, 0, _dataset.Height - 1); needsUpdate = true; break;
            case 2: _sliceX = Math.Clamp(_sliceX + wheel, 0, _dataset.Width - 1); needsUpdate = true; break;
        }
    }

    if (!inputHandled && isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
    {
        UpdateCrosshairFromMouse(viewIndex, canvasPos, canvasSize, zoom, pan);
    }

    // -------- Draw background & slice --------
    dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);

    if (needsUpdate || texture == null || !texture.IsValid)
    {
        UpdateTexture(viewIndex, ref texture);
        needsUpdate = false;
    }

    dl.PushClipRect(canvasPos, canvasPos + canvasSize, true);

    if (texture != null && texture.IsValid)
    {
        dl.AddImage(texture.GetImGuiTextureId(), imagePos, imagePos + imageSize,
            Vector2.Zero, Vector2.One, 0xFFFFFFFF);

        if (_showCrosshairs)
            DrawCrosshairs(dl, viewIndex, canvasPos, canvasSize, imagePos, imageSize, width, height);
        if (_showScaleBar)
            DrawScaleBar(dl, canvasPos, canvasSize, zoom, width, height, viewIndex);

        // -------- Draw overlays (now also in the simple viewer) --------
        RockCoreIntegration.DrawOverlay(_dataset, dl, viewIndex, imagePos, imageSize, width, height,
            _sliceX, _sliceY, _sliceZ);
        TransformIntegration.DrawOverlay(dl, _dataset, viewIndex, imagePos, imageSize, width, height);
    }

    dl.PopClipRect();
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
            ImGui.BulletText("Left Click: Set crosshair / Use Tool");
            
            // Add quick navigation buttons
            ImGui.Separator();
            ImGui.Text("Quick Navigation:");
            if (ImGui.Button("Go to Center"))
            {
                _sliceX = _dataset.Width / 2;
                _sliceY = _dataset.Height / 2;
                _sliceZ = _dataset.Depth / 2;
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            }

            ImGui.EndChild();
        }

        private void UpdateCrosshairFromMouse(int viewIndex, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
        {
            var (width, height) = GetImageDimensionsForView(viewIndex);
            var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height, viewIndex);
            var mousePosInImage = GetMousePosInImage(ImGui.GetMousePos(), imagePos, imageSize, width, height);

            switch (viewIndex)
            {
                case 0: // XY view
                    _sliceX = Math.Clamp((int)mousePosInImage.X, 0, _dataset.Width - 1);
                    _sliceY = Math.Clamp((int)mousePosInImage.Y, 0, _dataset.Height - 1);
                    _needsUpdateXZ = _needsUpdateYZ = true;
                    break;
                case 1: // XZ view
                    _sliceX = Math.Clamp((int)mousePosInImage.X, 0, _dataset.Width - 1);
                    _sliceZ = Math.Clamp((int)mousePosInImage.Y, 0, _dataset.Depth - 1);
                    _needsUpdateXY = _needsUpdateYZ = true;
                    break;
                case 2: // YZ view
                    _sliceY = Math.Clamp((int)mousePosInImage.X, 0, _dataset.Height - 1);
                    _sliceZ = Math.Clamp((int)mousePosInImage.Y, 0, _dataset.Depth - 1);
                    _needsUpdateXY = _needsUpdateXZ = true;
                    break;
            }
        }

        private void DrawCrosshairs(ImDrawListPtr dl, int viewIndex, Vector2 canvasPos, Vector2 canvasSize,
            Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
        {
            uint color = 0xFF00FF00;
            float x1 = 0, y1 = 0;

            switch (viewIndex)
            {
                case 0: x1 = (float)_sliceX / imageWidth; y1 = (float)_sliceY / imageHeight; break;
                case 1: x1 = (float)_sliceX / imageWidth; y1 = (float)_sliceZ / imageHeight; break;
                case 2: x1 = (float)_sliceY / imageWidth; y1 = (float)_sliceZ / imageHeight; break;
            }

            float screenX = imagePos.X + x1 * imageSize.X;
            float screenY = imagePos.Y + y1 * imageSize.Y;

            if (screenX >= imagePos.X && screenX <= imagePos.X + imageSize.X)
            {
                dl.AddLine(new Vector2(screenX, Math.Max(imagePos.Y, canvasPos.Y)), new Vector2(screenX, Math.Min(imagePos.Y + imageSize.Y, canvasPos.Y + canvasSize.Y)), color, 1.0f);
            }
            if (screenY >= imagePos.Y && screenY <= imagePos.Y + imageSize.Y)
            {
                dl.AddLine(new Vector2(Math.Max(imagePos.X, canvasPos.X), screenY), new Vector2(Math.Min(imagePos.X + imageSize.X, canvasPos.X + canvasSize.X), screenY), color, 1.0f);
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
                ApplyWindowLevel(imageData);

                int currentSlice = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => -1 };
                byte[] segmentationPreviewMask = _segmentationManager?.GetPreviewMask(currentSlice, viewIndex);
                byte[] committedSelectionMask = _segmentationManager?.GetCommittedSelectionMask(currentSlice, viewIndex);

                byte[] rgbaData = new byte[width * height * 4];
                var targetMaterial = _selectedMaterialForSegmentation;

                for (int i = 0; i < width * height; i++)
                {
                    // 1. Base Layer: Grayscale image
                    byte value = imageData[i];
                    Vector4 finalColor = new Vector4(value / 255f, value / 255f, value / 255f, 1.0f);

                    // 2. Committed Selection Layer
                    if (committedSelectionMask != null && committedSelectionMask[i] > 0)
                    {
                        var selColor = targetMaterial?.Color ?? new Vector4(0.8f, 0.8f, 0.0f, 1.0f);
                        float opacity = 0.4f;
                        finalColor = Vector4.Lerp(finalColor, new Vector4(selColor.X, selColor.Y, selColor.Z, 1.0f), opacity);
                    }

                    // 3. Live Tool Preview Layer (Topmost overlay)
                    if (segmentationPreviewMask != null && segmentationPreviewMask[i] > 0)
                    {
                        var segColor = targetMaterial?.Color ?? new Vector4(1, 0, 0, 1);
                        float opacity = 0.6f;
                        finalColor = Vector4.Lerp(finalColor, new Vector4(segColor.X, segColor.Y, segColor.Z, 1.0f), opacity);
                    }

                    rgbaData[i * 4] = (byte)(finalColor.X * 255);
                    rgbaData[i * 4 + 1] = (byte)(finalColor.Y * 255);
                    rgbaData[i * 4 + 2] = (byte)(finalColor.Z * 255);
                    rgbaData[i * 4 + 3] = 255;
                }

                texture?.Dispose();
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
                case 0: volume.ReadSliceZ(_sliceZ, data); break;
                case 1: for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) data[z * width + x] = volume[x, _sliceY, z]; break;
                case 2: for (int z = 0; z < height; z++) for (int y = 0; y < width; y++) data[z * width + y] = volume[_sliceX, y, z]; break;
            }

            return data;
        }

        private void ApplyWindowLevel(byte[] data)
        {
            float min = _windowLevel - _windowWidth / 2;
            float max = _windowLevel + _windowWidth / 2;
            float range = max - min;
            if (range < 1e-5) range = 1e-5f;

            for (int i = 0; i < data.Length; i++)
            {
                float value = (data[i] - min) / range * 255;
                data[i] = (byte)Math.Clamp(value, 0, 255);
            }
        }

        private (int width, int height) GetImageDimensionsForView(int viewIndex)
        {
            return viewIndex switch
            {
                0 => (_dataset.Width, _dataset.Height),
                1 => (_dataset.Width, _dataset.Depth),
                2 => (_dataset.Height, _dataset.Depth),
                _ => (_dataset.Width, _dataset.Height)
            };
        }

        private void DrawScaleBar(ImDrawListPtr dl, Vector2 canvasPos, Vector2 canvasSize,
            float zoom, int imageWidth, int imageHeight, int viewIndex)
        {
            float pixelSizeInUnits = viewIndex switch
            {
                0 => _dataset.PixelSize,
                1 => _dataset.PixelSize,
                2 => _dataset.PixelSize, // The width of the YZ view corresponds to the dataset's Height, which uses PixelSize
                _ => _dataset.PixelSize
            };

            var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, Vector2.Zero, imageWidth, imageHeight, viewIndex);
            float scaleFactor = imageSize.X / imageWidth;
            float[] possibleLengths = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 };
            string unit = _dataset.Unit ?? "µm";

            float bestLength = possibleLengths[0];
            foreach (float length in possibleLengths)
            {
                if (length / pixelSizeInUnits * scaleFactor <= 150) bestLength = length; else break;
            }

            float barLengthPixels = bestLength / pixelSizeInUnits * scaleFactor;
            Vector2 barPos = canvasPos + new Vector2(canvasSize.X - barLengthPixels - 20, canvasSize.Y - 40);

            dl.AddRectFilled(barPos - new Vector2(5, 5), barPos + new Vector2(barLengthPixels + 5, 25), 0xAA000000, 3.0f);
            dl.AddLine(barPos, barPos + new Vector2(barLengthPixels, 0), 0xFFFFFFFF, 3.0f);
            dl.AddLine(barPos, barPos + new Vector2(0, 5), 0xFFFFFFFF, 3.0f);
            dl.AddLine(barPos + new Vector2(barLengthPixels, 0), barPos + new Vector2(barLengthPixels, 5), 0xFFFFFFFF, 3.0f);

            string text = bestLength >= 1000 ? $"{bestLength / 1000:F0} mm" : $"{bestLength:F0} {unit}";
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
            ProjectManager.Instance.DatasetDataChanged -= OnDatasetDataChanged;
            CtImageStackTools.PreviewChanged -= OnPreviewChanged;

            CtSegmentationIntegration.Cleanup(_dataset);

            _textureXY?.Dispose();
            _textureXZ?.Dispose();
            _textureYZ?.Dispose();
        }
    }
}