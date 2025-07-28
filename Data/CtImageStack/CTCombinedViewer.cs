// GeoscientistToolkit/Data/CtImageStack/CtCombinedViewer.cs
using System;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using System.Collections.Generic;
using System.Linq;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Combined viewer showing 3 orthogonal slices + 3D volume rendering
    /// </summary>
    public class CtCombinedViewer : IDatasetViewer, IDisposable
    {
        private readonly CtImageStackDataset _dataset;
        private StreamingCtVolumeDataset _streamingDataset;
        private CtVolume3DViewer _volumeViewer;
        private readonly CtRenderingPanel _renderingPanel;
        private bool _renderingPanelOpen = true;
        private bool _isInitialized = false;
        private static ProgressBarDialog _progressDialog = new ProgressBarDialog("Loading 3D Viewer");

        // View mode enum
        public enum ViewModeEnum { Combined, SlicesOnly, VolumeOnly, XYOnly, XZOnly, YZOnly }
        private ViewModeEnum _viewMode = ViewModeEnum.Combined;
        public ViewModeEnum ViewMode 
        { 
            get => _viewMode; 
            set => _viewMode = value; 
        }

        // Slice positions - now public properties
        private int _sliceX;
        private int _sliceY;
        private int _sliceZ;
        
        public int SliceX 
        { 
            get => _sliceX; 
            set { _sliceX = Math.Clamp(value, 0, _dataset.Width - 1); _needsUpdateYZ = true; UpdateVolumeViewerSlices(); }
        }
        public int SliceY 
        { 
            get => _sliceY; 
            set { _sliceY = Math.Clamp(value, 0, _dataset.Height - 1); _needsUpdateXZ = true; UpdateVolumeViewerSlices(); }
        }
        public int SliceZ 
        { 
            get => _sliceZ; 
            set { _sliceZ = Math.Clamp(value, 0, _dataset.Depth - 1); _needsUpdateXY = true; UpdateVolumeViewerSlices(); }
        }

        // Textures for each view
        private TextureManager _textureXY;
        private TextureManager _textureXZ;
        private TextureManager _textureYZ;
        private bool _needsUpdateXY = true;
        private bool _needsUpdateXZ = true;
        private bool _needsUpdateYZ = true;

        // Window/Level - public properties
        private float _windowLevel = 128;
        private float _windowWidth = 255;
        public float WindowLevel 
        { 
            get => _windowLevel; 
            set { _windowLevel = value; _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true; }
        }
        public float WindowWidth 
        { 
            get => _windowWidth; 
            set { _windowWidth = value; _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true; }
        }

        // View settings - public properties
        public bool ShowCrosshairs { get; set; } = true;
        public bool SyncViews { get; set; } = true;
        public bool ShowScaleBar { get; set; } = true;

        // Volume rendering settings
        public bool ShowVolumeData { get; set; } = true;
        public float VolumeStepSize 
        { 
            get => _volumeViewer?.StepSize ?? 1.0f;
            set { if (_volumeViewer != null) _volumeViewer.StepSize = value; }
        }
        public float MinThreshold 
        { 
            get => _volumeViewer?.MinThreshold ?? 0.1f;
            set { if (_volumeViewer != null) _volumeViewer.MinThreshold = value; }
        }
        public float MaxThreshold 
        { 
            get => _volumeViewer?.MaxThreshold ?? 1.0f;
            set { if (_volumeViewer != null) _volumeViewer.MaxThreshold = value; }
        }
        public int ColorMapIndex 
        { 
            get => _volumeViewer?.ColorMapIndex ?? 0;
            set { if (_volumeViewer != null) _volumeViewer.ColorMapIndex = value; }
        }

        // Material visibility and opacity tracking
        private Dictionary<byte, bool> _materialVisibility = new Dictionary<byte, bool>();
        private Dictionary<byte, float> _materialOpacity = new Dictionary<byte, float>();

        // Zoom and pan for each view - START WITH BETTER DEFAULTS
        private float _zoomXY = 1.0f;
        private float _zoomXZ = 1.0f;
        private float _zoomYZ = 1.0f;
        private Vector2 _panXY = Vector2.Zero;
        private Vector2 _panXZ = Vector2.Zero;
        private Vector2 _panYZ = Vector2.Zero;

        // Track if we're in a popped-out window
        private bool _isPoppedOut = false;

        public CtCombinedViewer(CtImageStackDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            _dataset.Load();
            
            // Ensure volume data is loaded
            if (_dataset.VolumeData == null)
            {
                Logger.LogError("[CtCombinedViewer] Dataset volume data is null!");
                return;
            }
            
            Logger.Log($"[CtCombinedViewer] Dataset dimensions: {_dataset.Width}x{_dataset.Height}x{_dataset.Depth}");
            
            _sliceX = _dataset.Width / 2;
            _sliceY = _dataset.Height / 2;
            _sliceZ = _dataset.Depth / 2;
            
            // Initialize material visibility/opacity
            foreach (var material in _dataset.Materials)
            {
                _materialVisibility[material.ID] = material.IsVisible;
                _materialOpacity[material.ID] = 1.0f;
            }
            
            // Create the rendering panel and set it to open
            _renderingPanel = new CtRenderingPanel(this, _dataset);
            CtImageStackTools.PreviewChanged += OnPreviewChanged;
            // Start async initialization
            _ = InitializeAsync();
        }

        // Add this method to be called by the parent viewer panel
        public void SetPoppedOutState(bool isPoppedOut)
        {
            _isPoppedOut = isPoppedOut;
        }

        private async Task InitializeAsync()
        {
            await Task.Run(() =>
            {
                _progressDialog.Update(0.1f, "Finding streaming dataset...");
                _streamingDataset = FindStreamingDataset();
                
                if (_streamingDataset != null)
                {
                    _progressDialog.Update(0.5f, "Creating 3D volume viewer...");
                    _volumeViewer = new CtVolume3DViewer(_streamingDataset);
                    
                    // Sync initial volume rendering settings
                    if (_volumeViewer != null)
                    {
                        _progressDialog.Update(0.8f, "Configuring volume renderer...");
                        _volumeViewer.ShowGrayscale = ShowVolumeData;
                        _volumeViewer.StepSize = 2.0f; // Start with a reasonable step size
                        _volumeViewer.MinThreshold = 0.05f; // Lower initial threshold
                        _volumeViewer.MaxThreshold = 0.8f; // More reasonable range
                        UpdateVolumeViewerSlices(); // Set initial slice positions
                    }
                }
                
                _progressDialog.Update(1.0f, "Complete!");
            });
            
            _isInitialized = true;
            _progressDialog.Close();
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
            ImGui.Dummy(new Vector2(0, 0));
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            // Only submit the rendering panel as a separate window if we're NOT popped out
            // When popped out, we'll draw its content inline
            if (!_isPoppedOut)
            {
                _renderingPanel.Submit(ref _renderingPanelOpen);
            }
            
            // Show progress dialog while loading
            if (!_isInitialized)
            {
                _progressDialog.Submit();
                ImGui.Text("Loading 3D viewer...");
                return;
            }
            
            switch (_viewMode)
            {
                case ViewModeEnum.Combined:
                    DrawCombinedView();
                    break;
                case ViewModeEnum.SlicesOnly:
                    DrawSlicesOnlyView();
                    break;
                case ViewModeEnum.VolumeOnly:
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
                case ViewModeEnum.XYOnly:
                    DrawSliceView(0, "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
                    break;
                case ViewModeEnum.XZOnly:
                    DrawSliceView(1, "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
                    break;
                case ViewModeEnum.YZOnly:
                    DrawSliceView(2, "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
                    break;
            }
            
            // Context menu for the viewer
            if (ImGui.BeginPopupContextWindow("ViewerContextMenu"))
            {
                if (ImGui.MenuItem("Open Rendering Panel", null, _renderingPanelOpen))
                {
                    _renderingPanelOpen = true;
                }
                
                ImGui.Separator();
                
                if (ImGui.MenuItem("Reset Views"))
                {
                    ResetAllViews();
                }
                
                if (_viewMode != ViewModeEnum.VolumeOnly)
                {
                    if (ImGui.MenuItem("Center Slices"))
                    {
                        _sliceX = _dataset.Width / 2;
                        _sliceY = _dataset.Height / 2;
                        _sliceZ = _dataset.Depth / 2;
                        _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                    }
                }
                
                ImGui.Separator();
                
                // Quick view mode selection
                if (ImGui.MenuItem("Combined View", null, _viewMode == ViewModeEnum.Combined))
                {
                    _viewMode = ViewModeEnum.Combined;
                }
                if (ImGui.MenuItem("Slices Only", null, _viewMode == ViewModeEnum.SlicesOnly))
                {
                    _viewMode = ViewModeEnum.SlicesOnly;
                }
                if (ImGui.MenuItem("3D Only", null, _viewMode == ViewModeEnum.VolumeOnly))
                {
                    _viewMode = ViewModeEnum.VolumeOnly;
                }
                
                ImGui.EndPopup();
            }
            
            // If we're popped out, draw the rendering panel content inline at the end
            if (_isPoppedOut && _renderingPanelOpen)
            {
                ImGui.Separator();
                if (ImGui.CollapsingHeader("Rendering Controls", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    // Draw the rendering panel content directly
                    _renderingPanel.DrawContentInline();
                }
            }
        }

        public void ZoomAllViews(float factor)
        {
            _zoomXY = Math.Clamp(_zoomXY * factor, 0.1f, 10.0f);
            _zoomXZ = Math.Clamp(_zoomXZ * factor, 0.1f, 10.0f);
            _zoomYZ = Math.Clamp(_zoomYZ * factor, 0.1f, 10.0f);
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        }

        public void FitToWindow()
        {
            _zoomXY = _zoomXZ = _zoomYZ = 1.0f;
            _panXY = _panXZ = _panYZ = Vector2.Zero;
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        }

        
        public bool GetMaterialVisibility(byte id)
        {
            if (_materialVisibility.TryGetValue(id, out bool visible))
                return visible;
            return false;
        }

        public void SetMaterialVisibility(byte id, bool visible)
        {
            _materialVisibility[id] = visible;
            var material = _dataset.Materials.FirstOrDefault(m => m.ID == id);
            if (material != null)
            {
                material.IsVisible = visible;
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                
                // Sync with volume viewer
                _volumeViewer?.SetMaterialVisibility(id, visible);
            }
        }

        public float GetMaterialOpacity(byte id)
        {
            if (_materialOpacity.TryGetValue(id, out float opacity))
                return opacity;
            return 1.0f;
        }

        public void SetMaterialOpacity(byte id, float opacity)
        {
            _materialOpacity[id] = opacity;
            _volumeViewer?.SetMaterialOpacity(id, opacity);
        }

        public void ResetAllViews()
        {
            _sliceX = _dataset.Width / 2;
            _sliceY = _dataset.Height / 2;
            _sliceZ = _dataset.Depth / 2;
            _zoomXY = _zoomXZ = _zoomYZ = 1.0f;
            _panXY = _panXZ = _panYZ = Vector2.Zero;
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            _windowLevel = 128;
            _windowWidth = 255;
            UpdateVolumeViewerSlices();
        }

        private void UpdateVolumeViewerSlices()
        {
            if (_volumeViewer != null && SyncViews)
            {
                // Ensure slice positions are valid normalized values (0-1)
                _volumeViewer.SlicePositions = new Vector3(
                    Math.Clamp((float)_sliceX / Math.Max(1, _dataset.Width - 1), 0f, 1f),
                    Math.Clamp((float)_sliceY / Math.Max(1, _dataset.Height - 1), 0f, 1f),
                    Math.Clamp((float)_sliceZ / Math.Max(1, _dataset.Depth - 1), 0f, 1f)
                );
                
                // Force update of the 3D viewer
                _volumeViewer.ShowSlices = SyncViews;
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
                
                // Sync volume viewer settings
                _volumeViewer.ShowGrayscale = ShowVolumeData;
                
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
                
                ImGui.BeginChild("XY_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(0, "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
                ImGui.EndChild();
                
                ImGui.Dummy(new Vector2(0, spacing));
                
                ImGui.BeginChild("XZ_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(1, "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
                ImGui.EndChild();
                
                ImGui.Dummy(new Vector2(0, spacing));
                
                ImGui.BeginChild("YZ_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(2, "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
                ImGui.EndChild();
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
                    case 0: SliceZ = slice; break;
                    case 1: SliceY = slice; break;
                    case 2: SliceX = slice; break;
                }
            }
            ImGui.SameLine();
            ImGui.Text($"{slice + 1}/{maxSlice + 1}");
            ImGui.Separator();
            
            // Draw the slice content
            var contentRegion = ImGui.GetContentRegionAvail();
            DrawSingleSlice(viewIndex, ref zoom, ref pan, ref needsUpdate, ref texture, contentRegion);
        }

        private void DrawSingleSlice(int viewIndex, ref float zoom, ref Vector2 pan, ref bool needsUpdate, ref TextureManager texture, Vector2 availableSize)
        {
            var io = ImGui.GetIO();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = availableSize;
            var dl = ImGui.GetWindowDrawList();
            
            ImGui.InvisibleButton($"canvas{viewIndex}", canvasSize);
            bool isHovered = ImGui.IsItemHovered();
            
            // Individual slice context menu
            if (ImGui.BeginPopupContextItem($"SliceContext{viewIndex}"))
            {
                if (ImGui.MenuItem("Open Rendering Panel"))
                {
                    _renderingPanelOpen = true;
                }
                
                ImGui.Separator();
                
                string sliceName = viewIndex switch { 0 => "Z", 1 => "Y", 2 => "X", _ => "" };
                if (ImGui.MenuItem($"Center {sliceName} Slice"))
                {
                    switch (viewIndex)
                    {
                        case 0: SliceZ = _dataset.Depth / 2; break;
                        case 1: SliceY = _dataset.Height / 2; break;
                        case 2: SliceX = _dataset.Width / 2; break;
                    }
                }
                
                if (ImGui.MenuItem("Reset Zoom"))
                {
                    zoom = 1.0f;
                    pan = Vector2.Zero;
                }
                
                ImGui.Separator();
                
                if (ImGui.MenuItem("Copy Position"))
                {
                    string posText = $"X:{_sliceX + 1} Y:{_sliceY + 1} Z:{_sliceZ + 1}";
                    ImGui.SetClipboardText(posText);
                }
                
                ImGui.EndPopup();
            }
            
            // Mouse interactions
            if (isHovered && io.MouseWheel != 0)
            {
                float zoomDelta = io.MouseWheel * 0.1f;
                float newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
                if (newZoom != zoom)
                {
                    Vector2 mousePos = io.MousePos - canvasPos - canvasSize * 0.5f;
                    pan -= mousePos * (newZoom / zoom - 1.0f);
                    zoom = newZoom;
                    if (SyncViews) 
                    { 
                        _zoomXY = _zoomXZ = _zoomYZ = zoom; 
                    }
                }
            }
            
            // --- FIX START: Changed from IsItemActive to IsItemHovered for panning ---
            if (isHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle)) 
            { 
                pan += io.MouseDelta; 
                if (SyncViews)
                {
                    _panXY = pan;
                    _panXZ = pan;
                    _panYZ = pan;
                }
            }
            // --- FIX END ---
            
            if (isHovered && io.MouseWheel != 0 && io.KeyCtrl)
            {
                switch (viewIndex)
                {
                    case 0: SliceZ = Math.Clamp(_sliceZ + (int)io.MouseWheel, 0, _dataset.Depth - 1); break;
                    case 1: SliceY = Math.Clamp(_sliceY + (int)io.MouseWheel, 0, _dataset.Height - 1); break;
                    case 2: SliceX = Math.Clamp(_sliceX + (int)io.MouseWheel, 0, _dataset.Width - 1); break;
                }
            }
            
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
                    imageSize = new Vector2(canvasSize.X, canvasSize.X / imageAspect);
                }
                else
                {
                    imageSize = new Vector2(canvasSize.Y * imageAspect, canvasSize.Y);
                }
                
                // Apply zoom to the image size
                imageSize *= zoom;
                
                // Center the image and apply pan
                Vector2 imagePos = canvasPos + canvasSize * 0.5f - imageSize * 0.5f + pan;
                
                // Ensure we draw within the canvas bounds
                dl.PushClipRect(canvasPos, canvasPos + canvasSize, true);
                dl.AddImage(texture.GetImGuiTextureId(), imagePos, imagePos + imageSize, Vector2.Zero, Vector2.One, 0xFFFFFFFF);
                
                if (ShowCrosshairs) 
                { 
                    DrawCrosshairs(dl, viewIndex, canvasPos, canvasSize, imagePos, imageSize, width, height); 
                }
                if (ShowScaleBar) 
                { 
                    DrawScaleBar(dl, canvasPos, canvasSize, zoom, width, height, viewIndex); 
                }
                
                dl.PopClipRect();
            }
        }

        private void UpdateTexture(int viewIndex, ref TextureManager texture)
        {
            if (_dataset.VolumeData == null) 
            { 
                Logger.LogError("[CtCombinedViewer] No volume data available"); 
                return; 
            }
            
            try
            {
                var (width, height) = GetImageDimensionsForView(viewIndex);
                
                Logger.Log($"[CtCombinedViewer] Updating texture for view {viewIndex}, dimensions: {width}x{height}");
                
                byte[] imageData = ExtractSliceData(viewIndex, width, height);
                ApplyWindowLevel(imageData);
                
                byte[] labelData = null;
                if (_dataset.LabelData != null) 
                { 
                    labelData = ExtractLabelSliceData(viewIndex, width, height); 
                }
                
                // Get preview data from the tools
                byte[] previewMask = null;
                Vector4 previewColor = new Vector4(1, 0, 0, 0.5f);
                
                if (viewIndex == 0) // XY view
                {
                    var (isActive, mask, color) = CtImageStackTools.GetPreviewData(_dataset, _sliceZ);
                    if (isActive)
                    {
                        previewMask = mask;
                        previewColor = color;
                    }
                }
                // Note: For XZ and YZ views, you'd need to implement similar preview generation
                // for those orientations in the tools
                
                byte[] rgbaData = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    byte value = imageData[i];
                    
                    // Check preview mask first
                    if (previewMask != null && previewMask[i] > 0)
                    {
                        rgbaData[i * 4] = (byte)(value * 0.5f + previewColor.X * 255 * 0.5f);
                        rgbaData[i * 4 + 1] = (byte)(value * 0.5f + previewColor.Y * 255 * 0.5f);
                        rgbaData[i * 4 + 2] = (byte)(value * 0.5f + previewColor.Z * 255 * 0.5f);
                        rgbaData[i * 4 + 3] = 255;
                    }
                    else if (labelData != null && labelData[i] > 0)
                    {
                        var material = _dataset.Materials.FirstOrDefault(m => m.ID == labelData[i]);
                        if (material != null && GetMaterialVisibility(material.ID))
                        {
                            float opacity = GetMaterialOpacity(material.ID);
                            rgbaData[i * 4] = (byte)(value * (1 - opacity) + material.Color.X * 255 * opacity);
                            rgbaData[i * 4 + 1] = (byte)(value * (1 - opacity) + material.Color.Y * 255 * opacity);
                            rgbaData[i * 4 + 2] = (byte)(value * (1 - opacity) + material.Color.Z * 255 * opacity);
                            rgbaData[i * 4 + 3] = 255;
                        }
                        else 
                        { 
                            rgbaData[i * 4] = value; 
                            rgbaData[i * 4 + 1] = value; 
                            rgbaData[i * 4 + 2] = value; 
                            rgbaData[i * 4 + 3] = 255; 
                        }
                    }
                    else 
                    { 
                        rgbaData[i * 4] = value; 
                        rgbaData[i * 4 + 1] = value; 
                        rgbaData[i * 4 + 2] = value; 
                        rgbaData[i * 4 + 3] = 255; 
                    }
                }
                
                texture?.Dispose();
                texture = TextureManager.CreateFromPixelData(rgbaData, (uint)width, (uint)height);
            }
            catch (Exception ex) 
            { 
                Logger.LogError($"[CtCombinedViewer] Error updating texture: {ex.Message}"); 
            }
        }

        private byte[] ExtractSliceData(int viewIndex, int width, int height)
        {
            byte[] data = new byte[width * height];
            var volume = _dataset.VolumeData;
            
            try
            {
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
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CtCombinedViewer] Error extracting slice data for view {viewIndex}: {ex.Message}");
            }
            
            return data;
        }
        private void OnPreviewChanged(CtImageStackDataset dataset)
        {
            if (dataset == _dataset)
            {
                // Mark all views as needing update
                _needsUpdateXY = true;
                _needsUpdateXZ = true;
                _needsUpdateYZ = true;
            }
        }
        private byte[] ExtractLabelSliceData(int viewIndex, int width, int height)
        {
            byte[] data = new byte[width * height];
            var labels = _dataset.LabelData;
            
            switch (viewIndex)
            {
                case 0: // XY
                    labels.ReadSliceZ(_sliceZ, data); 
                    break;
                case 1: // XZ
                    for (int z = 0; z < height; z++) 
                    { 
                        for (int x = 0; x < width; x++) 
                        { 
                            data[z * width + x] = labels[x, _sliceY, z]; 
                        } 
                    } 
                    break;
                case 2: // YZ
                    for (int z = 0; z < height; z++) 
                    { 
                        for (int y = 0; y < width; y++) 
                        { 
                            data[z * width + y] = labels[_sliceX, y, z]; 
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
            
            Parallel.For(0, data.Length, i => 
            { 
                float value = data[i]; 
                value = (value - min) / (max - min) * 255; 
                data[i] = (byte)Math.Clamp(value, 0, 255); 
            });
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

        private void UpdateCrosshairFromMouse(int viewIndex, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
        {
            var mousePos = ImGui.GetMousePos() - canvasPos - canvasSize * 0.5f - pan;
            var (width, height) = GetImageDimensionsForView(viewIndex);
            float imageAspect = (float)width / height;
            float canvasAspect = canvasSize.X / canvasSize.Y;
            
            Vector2 imageSize;
            if (imageAspect > canvasAspect)
            {
                imageSize = new Vector2(canvasSize.X, canvasSize.X / imageAspect);
            }
            else
            {
                imageSize = new Vector2(canvasSize.Y * imageAspect, canvasSize.Y);
            }
            imageSize *= zoom;
            
            float x = (mousePos.X + imageSize.X * 0.5f) / imageSize.X * width;
            float y = (mousePos.Y + imageSize.Y * 0.5f) / imageSize.Y * height;
            
            switch (viewIndex)
            {
                case 0: 
                    SliceX = Math.Clamp((int)x, 0, _dataset.Width - 1); 
                    SliceY = Math.Clamp((int)y, 0, _dataset.Height - 1); 
                    break;
                case 1: 
                    SliceX = Math.Clamp((int)x, 0, _dataset.Width - 1); 
                    SliceZ = Math.Clamp((int)y, 0, _dataset.Depth - 1); 
                    break;
                case 2: 
                    SliceY = Math.Clamp((int)x, 0, _dataset.Height - 1); 
                    SliceZ = Math.Clamp((int)y, 0, _dataset.Depth - 1); 
                    break;
            }
        }

        private void DrawCrosshairs(ImDrawListPtr dl, int viewIndex, Vector2 canvasPos, Vector2 canvasSize, Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
        {
            uint color = 0xFF00FF00;
            float x1, y1;
            
            switch (viewIndex)
            {
                case 0: 
                    x1 = (float)_sliceX / imageWidth; 
                    y1 = (float)_sliceY / imageHeight; 
                    break;
                case 1: 
                    x1 = (float)_sliceX / imageWidth; 
                    y1 = (float)_sliceZ / imageHeight; 
                    break;
                case 2: 
                    x1 = (float)_sliceY / imageWidth; 
                    y1 = (float)_sliceZ / imageHeight; 
                    break;
                default: 
                    return;
            }
            
            float screenX = imagePos.X + x1 * imageSize.X;
            float screenY = imagePos.Y + y1 * imageSize.Y;
            
            if (screenX >= imagePos.X && screenX <= imagePos.X + imageSize.X) 
            { 
                dl.AddLine(new Vector2(screenX, Math.Max(imagePos.Y, canvasPos.Y)), 
                          new Vector2(screenX, Math.Min(imagePos.Y + imageSize.Y, canvasPos.Y + canvasSize.Y)), 
                          color, 1.0f); 
            }
            if (screenY >= imagePos.Y && screenY <= imagePos.Y + imageSize.Y) 
            { 
                dl.AddLine(new Vector2(Math.Max(imagePos.X, canvasPos.X), screenY), 
                          new Vector2(Math.Min(imagePos.X + imageSize.X, canvasPos.X + canvasSize.X), screenY), 
                          color, 1.0f); 
            }
        }

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

        public void Dispose()
        {
            // Unsubscribe from preview changes
            CtImageStackTools.PreviewChanged -= OnPreviewChanged;
    
            _renderingPanel?.Dispose();
            _textureXY?.Dispose();
            _textureXZ?.Dispose();
            _textureYZ?.Dispose();
            _volumeViewer?.Dispose();
        }
    }
}