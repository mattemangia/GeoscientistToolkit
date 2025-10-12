// GeoscientistToolkit/Data/CtImageStack/CtCombinedViewer.cs
// FIXED: Material color and opacity changes now update 2D slices immediately

using System.Numerics;
using GeoscientistToolkit.Analysis;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Analysis.RockCoreExtractor;
using GeoscientistToolkit.Analysis.TextureClassification;
using GeoscientistToolkit.Analysis.Transform;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.CtImageStack.Segmentation;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Tools;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.CtImageStack;

public class CtCombinedViewer : IDatasetViewer, IDisposable
{
    public enum SliceDisplayMode
    {
        Grayscale,
        ThermalField
    }

    public enum ViewModeEnum
    {
        Combined,
        SlicesOnly,
        VolumeOnly,
        XYOnly,
        XZOnly,
        YZOnly
    }

    private static readonly ProgressBarDialog _progressDialog = new("Loading 3D Viewer");

    // ADDED: Colormap data for 2D slices
    private static Vector3[,] _colormapData;
    private readonly List<(Vector2, Vector2)> _cachedIsocontoursXY = new();
    private readonly List<(Vector2, Vector2)> _cachedIsocontoursXZ = new();
    private readonly List<(Vector2, Vector2)> _cachedIsocontoursYZ = new();
    private readonly CtImageStackDataset _dataset;
    private readonly CtSegmentationIntegration _interactiveSegmentation;
    private readonly Dictionary<byte, float> _materialOpacity = new();
    private readonly Dictionary<byte, bool> _materialVisibility = new();
    private readonly CtRenderingPanel _renderingPanel;
    private (int slice, int numContours) _cachedKeyXY = (-1, -1);
    private (int slice, int numContours) _cachedKeyXZ = (-1, -1);
    private (int slice, int numContours) _cachedKeyYZ = (-1, -1);
    private bool _isInitialized;
    private bool _isPoppedOut;
    private bool _needsUpdateXY = true;
    private bool _needsUpdateXZ = true;
    private bool _needsUpdateYZ = true;
    private Vector2 _panXY = Vector2.Zero;
    private Vector2 _panXZ = Vector2.Zero;
    private Vector2 _panYZ = Vector2.Zero;
    private bool _renderingPanelOpen = true;
    private int _sliceX;
    private int _sliceY;
    private int _sliceZ;
    private StreamingCtVolumeDataset _streamingDataset;
    private TextureManager _textureXY;
    private TextureManager _textureXZ;
    private TextureManager _textureYZ;
    private float _windowLevel = 128;
    private float _windowWidth = 255;
    private float _zoomXY = 1.0f;
    private float _zoomXZ = 1.0f;
    private float _zoomYZ = 1.0f;

    public CtCombinedViewer(CtImageStackDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _dataset.Load();

        InitializeColormaps();

        CtSegmentationIntegration.Initialize(_dataset);
        _interactiveSegmentation = CtSegmentationIntegration.GetInstance(_dataset);

        _renderingPanel = new CtRenderingPanel(this, _dataset);
        _renderingPanelOpen = true;

        if (_dataset.VolumeData == null)
            Logger.LogWarning("[CtCombinedViewer] VolumeData not ready yet; viewer will update once loaded.");
        else
            Logger.Log($"[CtCombinedViewer] Dataset dimensions: {_dataset.Width}×{_dataset.Height}×{_dataset.Depth}");

        _sliceX = _dataset.Width / 2;
        _sliceY = _dataset.Height / 2;
        _sliceZ = _dataset.Depth / 2;

        foreach (var material in _dataset.Materials)
        {
            _materialVisibility[material.ID] = material.IsVisible;
            _materialOpacity[material.ID] = 1.0f;
        }

        ProjectManager.Instance.DatasetDataChanged += OnDatasetDataChanged;
        CalibrationIntegration.PreviewChanged += _ => { _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true; };
        CtImageStackTools.Preview3DChanged += OnPreview3DChanged;
        CtImageStackTools.PreviewChanged += OnGenericPreviewChanged;
        AcousticIntegration.OnPositionsChanged += OnAcousticPositionsChanged;

        _ = InitializeAsync();
    }

    public SliceDisplayMode CurrentSliceDisplayMode { get; set; } = SliceDisplayMode.Grayscale;
    public bool ShowThermalIsocontours { get; set; } = true;
    public int NumThermalIsocontours { get; set; } = 10;

    public CtVolume3DViewer VolumeViewer { get; private set; }
    public ViewModeEnum ViewMode { get; set; } = ViewModeEnum.Combined;

    public int SliceX
    {
        get => _sliceX;
        set
        {
            _sliceX = Math.Clamp(value, 0, _dataset.Width - 1);
            _needsUpdateYZ = true;
            UpdateVolumeViewerSlices();
        }
    }

    public int SliceY
    {
        get => _sliceY;
        set
        {
            _sliceY = Math.Clamp(value, 0, _dataset.Height - 1);
            _needsUpdateXZ = true;
            UpdateVolumeViewerSlices();
        }
    }

    public int SliceZ
    {
        get => _sliceZ;
        set
        {
            _sliceZ = Math.Clamp(value, 0, _dataset.Depth - 1);
            _needsUpdateXY = true;
            UpdateVolumeViewerSlices();
        }
    }

    public float WindowLevel
    {
        get => _windowLevel;
        set
        {
            _windowLevel = value;
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        }
    }

    public float WindowWidth
    {
        get => _windowWidth;
        set
        {
            _windowWidth = value;
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        }
    }

    public bool ShowCrosshairs { get; set; } = true;
    public bool SyncViews { get; set; } = true;
    public bool ShowScaleBar { get; set; } = true;
    public bool ShowCuttingPlanes { get; set; } = true;
    public bool ShowVolumeData { get; set; } = true;

    public float VolumeStepSize
    {
        get => VolumeViewer?.StepSize ?? 1.0f;
        set
        {
            if (VolumeViewer != null) VolumeViewer.StepSize = value;
        }
    }

    public float MinThreshold
    {
        get => VolumeViewer?.MinThreshold ?? 0.1f;
        set
        {
            if (VolumeViewer != null) VolumeViewer.MinThreshold = value;
        }
    }

    public float MaxThreshold
    {
        get => VolumeViewer?.MaxThreshold ?? 1.0f;
        set
        {
            if (VolumeViewer != null) VolumeViewer.MaxThreshold = value;
        }
    }

    public int ColorMapIndex
    {
        get => VolumeViewer?.ColorMapIndex ?? 0;
        set
        {
            if (VolumeViewer != null) VolumeViewer.ColorMapIndex = value;
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        }
    }

    public void DrawToolbarControls()
    {
        ImGui.Dummy(new Vector2(0, 0));
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        if (!_isPoppedOut && _renderingPanel != null) _renderingPanel.Submit(ref _renderingPanelOpen);

        if (!_isInitialized)
        {
            _progressDialog.Submit();
            ImGui.Text("Loading 3D viewer...");
            return;
        }

        // Thermal visualization options
        if (_dataset.ThermalResults != null)
        {
            if (ImGui.CollapsingHeader("Thermal Visualization", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                var mode = (int)CurrentSliceDisplayMode;
                if (ImGui.Combo("Data Source", ref mode, "Grayscale\0Thermal Field\0"))
                {
                    CurrentSliceDisplayMode = (SliceDisplayMode)mode;
                    _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                    _cachedKeyXY = _cachedKeyXZ = _cachedKeyYZ = (-1, -1);
                }

                if (CurrentSliceDisplayMode == SliceDisplayMode.ThermalField)
                {
                    var showThermalIsocontours = ShowThermalIsocontours;
                    if (ImGui.Checkbox("Show Isocontours", ref showThermalIsocontours))
                    {
                        ShowThermalIsocontours = showThermalIsocontours;
                        _cachedKeyXY = _cachedKeyXZ = _cachedKeyYZ = (-1, -1);
                    }


                    if (ShowThermalIsocontours)
                    {
                        var numThermalIsocontours = NumThermalIsocontours;
                        if (ImGui.SliderInt("Contour Count", ref numThermalIsocontours, 2, 20))
                        {
                            NumThermalIsocontours = numThermalIsocontours;
                            _cachedKeyXY = _cachedKeyXZ = _cachedKeyYZ = (-1, -1);
                        }
                    }
                }

                ImGui.Unindent();
            }
        }
        else
        {
            CurrentSliceDisplayMode = SliceDisplayMode.Grayscale;
        }

        switch (ViewMode)
        {
            case ViewModeEnum.Combined:
                DrawCombinedView();
                break;
            case ViewModeEnum.SlicesOnly:
                DrawSlicesOnlyView();
                break;
            case ViewModeEnum.VolumeOnly:
                if (VolumeViewer != null)
                    VolumeViewer.DrawContent(ref zoom, ref pan);
                else
                    ImGui.Text("No 3D volume dataset available.");
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

        if (ImGui.BeginPopupContextWindow("ViewerContextMenu"))
        {
            if (ImGui.MenuItem("Open Rendering Panel", null, _renderingPanelOpen))
                _renderingPanelOpen = true;

            ImGui.Separator();

            if (ImGui.MenuItem("Reset Views")) ResetAllViews();

            if (ViewMode != ViewModeEnum.VolumeOnly && ImGui.MenuItem("Center Slices"))
            {
                _sliceX = _dataset.Width / 2;
                _sliceY = _dataset.Height / 2;
                _sliceZ = _dataset.Depth / 2;
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                _cachedKeyXY = _cachedKeyXZ = _cachedKeyYZ = (-1, -1);
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Combined View", null, ViewMode == ViewModeEnum.Combined))
                ViewMode = ViewModeEnum.Combined;
            if (ImGui.MenuItem("Slices Only", null, ViewMode == ViewModeEnum.SlicesOnly))
                ViewMode = ViewModeEnum.SlicesOnly;
            if (ImGui.MenuItem("3D Only", null, ViewMode == ViewModeEnum.VolumeOnly))
                ViewMode = ViewModeEnum.VolumeOnly;

            ImGui.EndPopup();
        }

        if (_isPoppedOut && _renderingPanelOpen && _renderingPanel != null)
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader("Rendering Controls", ImGuiTreeNodeFlags.DefaultOpen))
                _renderingPanel.DrawContentInline();
        }
    }

    public void Dispose()
    {
        ProjectManager.Instance.DatasetDataChanged -= OnDatasetDataChanged;
        CtImageStackTools.Preview3DChanged -= OnPreview3DChanged;
        CtImageStackTools.PreviewChanged -= OnGenericPreviewChanged;
        AcousticIntegration.OnPositionsChanged -= OnAcousticPositionsChanged;

        CtSegmentationIntegration.Cleanup(_dataset);
        RockCoreIntegration.UnregisterTool(_dataset);

        _renderingPanel?.Dispose();
        _textureXY?.Dispose();
        _textureXZ?.Dispose();
        _textureYZ?.Dispose();
        VolumeViewer?.Dispose();
    }

    private static void InitializeColormaps()
    {
        if (_colormapData != null) return;

        const int size = 256;
        const int numMaps = 4;
        _colormapData = new Vector3[numMaps, size];

        // Grayscale (map 0)
        for (var i = 0; i < size; i++)
        {
            var v = i / (float)(size - 1);
            _colormapData[0, i] = new Vector3(v, v, v);
        }

        // Hot (map 1)
        for (var i = 0; i < size; i++)
        {
            var t = i / (float)(size - 1);
            var r = Math.Min(1.0f, 3.0f * t);
            var g = Math.Clamp(3.0f * t - 1.0f, 0.0f, 1.0f);
            var b = Math.Clamp(3.0f * t - 2.0f, 0.0f, 1.0f);
            _colormapData[1, i] = new Vector3(r, g, b);
        }

        // Cool (map 2)
        for (var i = 0; i < size; i++)
        {
            var t = i / (float)(size - 1);
            _colormapData[2, i] = new Vector3(t, 1 - t, 1);
        }

        // Rainbow (map 3)
        for (var i = 0; i < size; i++)
        {
            var h = i / (float)(size - 1) * 0.7f;
            _colormapData[3, i] = HsvToRgb(h, 1.0f, 1.0f);
        }
    }

    private Vector3 ApplyColorMap(float normalizedIntensity, int colorMapIndex)
    {
        var mapIdx = Math.Clamp(colorMapIndex, 0, 3);
        var texelIdx = (int)(normalizedIntensity * 255);
        texelIdx = Math.Clamp(texelIdx, 0, 255);
        return _colormapData[mapIdx, texelIdx];
    }

    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        float r, g, b;
        var i = (int)(h * 6);
        var f = h * 6 - i;
        var p = v * (1 - s);
        var q = v * (1 - f * s);
        var t = v * (1 - (1 - f) * s);

        switch (i % 6)
        {
            case 0:
                r = v;
                g = t;
                b = p;
                break;
            case 1:
                r = q;
                g = v;
                b = p;
                break;
            case 2:
                r = p;
                g = v;
                b = t;
                break;
            case 3:
                r = p;
                g = q;
                b = v;
                break;
            case 4:
                r = t;
                g = p;
                b = v;
                break;
            default:
                r = v;
                g = p;
                b = q;
                break;
        }

        return new Vector3(r, g, b);
    }

    private void OnGenericPreviewChanged(Dataset dataset)
    {
        if (dataset == _dataset) _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
    }

    private void OnAcousticPositionsChanged()
    {
        _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
    }

    private void OnPreview3DChanged(CtImageStackDataset dataset, byte[] previewMask, Vector4 color)
    {
        if (dataset == _dataset)
        {
            _needsUpdateXY = true;
            _needsUpdateXZ = true;
            _needsUpdateYZ = true;
        }
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
                VolumeViewer = new CtVolume3DViewer(_streamingDataset);

                if (VolumeViewer != null)
                {
                    _progressDialog.Update(0.8f, "Configuring volume renderer...");
                    VolumeViewer.ShowGrayscale = ShowVolumeData;
                    VolumeViewer.StepSize = 2.0f;
                    VolumeViewer.MinThreshold = 0.05f;
                    VolumeViewer.MaxThreshold = 0.8f;
                    UpdateVolumeViewerSlices();
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
            if (dataset is StreamingCtVolumeDataset streaming && streaming.EditablePartner == _dataset)
                return streaming;

        return null;
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
        if (_materialVisibility.TryGetValue(id, out var visible))
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
            VolumeViewer?.SetMaterialVisibility(id, visible);
        }
    }

    public float GetMaterialOpacity(byte id)
    {
        if (_materialOpacity.TryGetValue(id, out var opacity))
            return opacity;
        return 1.0f;
    }

    public void SetMaterialOpacity(byte id, float opacity)
    {
        _materialOpacity[id] = opacity;
        _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        VolumeViewer?.SetMaterialOpacity(id, opacity);
    }

    public void NotifyMaterialColorChanged()
    {
        _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
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
        if (VolumeViewer != null && SyncViews)
        {
            VolumeViewer.SlicePositions = new Vector3(
                Math.Clamp((float)_sliceX / Math.Max(1, _dataset.Width - 1), 0f, 1f),
                Math.Clamp((float)_sliceY / Math.Max(1, _dataset.Height - 1), 0f, 1f),
                Math.Clamp((float)_sliceZ / Math.Max(1, _dataset.Depth - 1), 0f, 1f)
            );

            VolumeViewer.ShowSlices = SyncViews;
        }
    }

    private void DrawCombinedView()
    {
        var availableSize = ImGui.GetContentRegionAvail();
        var viewWidth = (availableSize.X - 2) / 2;
        var viewHeight = (availableSize.Y - 2) / 2;

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
        if (VolumeViewer != null)
        {
            ImGui.Text("3D Volume");
            ImGui.Separator();
            var contentSize = ImGui.GetContentRegionAvail();
            ImGui.BeginChild("3D_Content", contentSize);
            var dummyZoom = 1.0f;
            var dummyPan = Vector2.Zero;

            VolumeViewer.ShowGrayscale = ShowVolumeData;

            VolumeViewer.DrawContent(ref dummyZoom, ref dummyPan);
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
        var totalWidth = availableSize.X - spacing * 2;
        var viewWidth = totalWidth / 3;
        var viewHeight = availableSize.Y;

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

    private void DrawSliceView(int viewIndex, string title, ref float zoom, ref Vector2 pan, ref bool needsUpdate,
        ref TextureManager texture)
    {
        var slice = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 };
        var maxSlice = viewIndex switch
        {
            0 => _dataset.Depth - 1, 1 => _dataset.Height - 1, 2 => _dataset.Width - 1, _ => 0
        };

        if (SliceNavigationHelper.DrawSliceControls(title, ref slice, maxSlice, $"Slice{viewIndex}"))
            switch (viewIndex)
            {
                case 0: SliceZ = slice; break;
                case 1: SliceY = slice; break;
                case 2: SliceX = slice; break;
            }

        ImGui.Separator();

        var contentRegion = ImGui.GetContentRegionAvail();
        DrawSingleSlice(viewIndex, ref zoom, ref pan, ref needsUpdate, ref texture, contentRegion);
    }

    private (Vector2 pos, Vector2 size) GetImageDisplayMetrics(Vector2 canvasPos, Vector2 canvasSize, float zoom,
        Vector2 pan, int imageWidth, int imageHeight, int viewIndex)
    {
        float pixelWidth, pixelHeight;

        switch (viewIndex)
        {
            case 0:
                pixelWidth = _dataset.PixelSize;
                pixelHeight = _dataset.PixelSize;
                break;
            case 1:
                pixelWidth = _dataset.PixelSize;
                pixelHeight = _dataset.SliceThickness;
                break;
            case 2:
                pixelWidth = _dataset.PixelSize;
                pixelHeight = _dataset.SliceThickness;
                break;
            default:
                pixelWidth = 1.0f;
                pixelHeight = 1.0f;
                break;
        }

        if (pixelHeight <= 0) pixelHeight = pixelWidth;
        if (pixelWidth <= 0) pixelWidth = 1.0f;

        var imageAspect = imageWidth * pixelWidth / (imageHeight * pixelHeight);
        var canvasAspect = canvasSize.X / canvasSize.Y;

        Vector2 imageDisplaySize;
        if (imageAspect > canvasAspect)
            imageDisplaySize = new Vector2(canvasSize.X, canvasSize.X / imageAspect);
        else
            imageDisplaySize = new Vector2(canvasSize.Y * imageAspect, canvasSize.Y);

        imageDisplaySize *= zoom;
        var imageDisplayPos = canvasPos + (canvasSize - imageDisplaySize) * 0.5f + pan;

        return (imageDisplayPos, imageDisplaySize);
    }

    private Vector2 GetMousePosInImage(Vector2 mousePos, Vector2 imageDisplayPos, Vector2 imageDisplaySize,
        int imageWidth, int imageHeight)
    {
        var mouseRelativeToImage = mousePos - imageDisplayPos;

        return new Vector2(
            mouseRelativeToImage.X / imageDisplaySize.X * imageWidth,
            mouseRelativeToImage.Y / imageDisplaySize.Y * imageHeight
        );
    }

    private void DrawSingleSlice(int viewIndex, ref float zoom, ref Vector2 pan, ref bool needsUpdate,
        ref TextureManager texture, Vector2 availableSize)
    {
        var io = ImGui.GetIO();
        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = availableSize;
        var dl = ImGui.GetWindowDrawList();

        ImGui.InvisibleButton($"canvas{viewIndex}", canvasSize);
        var isHovered = ImGui.IsItemHovered();

        var (width, height) = GetImageDimensionsForView(viewIndex);
        var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height, viewIndex);

        var inputHandled = false;

        if (AcousticIntegration.IsPlacingFor(_dataset) && isHovered && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) ||
                                                                        ImGui.IsMouseDragging(ImGuiMouseButton.Left)))
        {
            var mousePosInImage = GetMousePosInImage(io.MousePos, imagePos, imageSize, width, height);
            var currentTx = AcousticIntegration.TxPosition;
            var currentRx = AcousticIntegration.RxPosition;

            var newPos = new Vector3();
            switch (viewIndex)
            {
                case 0:
                    newPos = new Vector3(mousePosInImage.X / width, mousePosInImage.Y / height,
                        (float)_sliceZ / _dataset.Depth);
                    break;
                case 1:
                    newPos = new Vector3(mousePosInImage.X / width, (float)_sliceY / _dataset.Height,
                        mousePosInImage.Y / height);
                    break;
                case 2:
                    newPos = new Vector3((float)_sliceX / _dataset.Width, mousePosInImage.X / width,
                        mousePosInImage.Y / height);
                    break;
            }

            AcousticIntegration.UpdatePosition(newPos);
            inputHandled = true;
        }

        if (isHovered && !inputHandled)
        {
            if (isHovered && ImGui.IsItemClicked(ImGuiMouseButton.Left)
                          && CalibrationIntegration.IsRegionSelectionEnabled(_dataset))
            {
                var io0 = ImGui.GetIO();
                var mousePosInImage = GetMousePosInImage(io0.MousePos, imagePos, imageSize, width, height);
                var vx = Math.Clamp((int)mousePosInImage.X, 0, width - 1);
                var vy = Math.Clamp((int)mousePosInImage.Y, 0, height - 1);

                switch (viewIndex)
                {
                    case 0: CalibrationIntegration.OnViewerClick(_dataset, "Z", _sliceZ, vx, vy); break;
                    case 1: CalibrationIntegration.OnViewerClick(_dataset, "Y", _sliceY, vx, vy); break;
                    case 2: CalibrationIntegration.OnViewerClick(_dataset, "X", _sliceX, vx, vy); break;
                }

                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                return;
            }

            inputHandled = RockCoreIntegration.HandleMouseInput(_dataset, io.MousePos,
                imagePos, imageSize, width, height, viewIndex,
                ImGui.IsItemClicked(ImGuiMouseButton.Left),
                ImGui.IsMouseDragging(ImGuiMouseButton.Left),
                ImGui.IsMouseReleased(ImGuiMouseButton.Left));

            if (!inputHandled)
                inputHandled = TransformIntegration.HandleMouseInput(_dataset, io.MousePos,
                    imagePos, imageSize, width, height, viewIndex,
                    ImGui.IsItemClicked(ImGuiMouseButton.Left),
                    ImGui.IsMouseDragging(ImGuiMouseButton.Left),
                    ImGui.IsMouseReleased(ImGuiMouseButton.Left));
            if (!inputHandled)
                inputHandled = TextureClassificationIntegration.HandleMouseInput(_dataset, io.MousePos,
                    imagePos, imageSize, width, height, viewIndex,
                    ImGui.IsItemClicked(ImGuiMouseButton.Left),
                    ImGui.IsMouseDragging(ImGuiMouseButton.Left),
                    ImGui.IsMouseReleased(ImGuiMouseButton.Left));
        }

        if (!inputHandled && _interactiveSegmentation != null && isHovered)
        {
            var mousePosInImage = GetMousePosInImage(io.MousePos, imagePos, imageSize, width, height);

            _interactiveSegmentation.HandleMouseInput(
                mousePosInImage,
                viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 },
                viewIndex,
                ImGui.IsItemClicked(ImGuiMouseButton.Left),
                ImGui.IsMouseDragging(ImGuiMouseButton.Left),
                ImGui.IsMouseReleased(ImGuiMouseButton.Left)
            );

            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) || ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        }

        if (ImGui.BeginPopupContextItem($"SliceContext{viewIndex}"))
        {
            if (ImGui.MenuItem("Open Rendering Panel")) _renderingPanelOpen = true;
            ImGui.Separator();
            var sliceName = viewIndex switch { 0 => "Z", 1 => "Y", 2 => "X", _ => "" };
            if (ImGui.MenuItem($"Center {sliceName} Slice"))
                switch (viewIndex)
                {
                    case 0: SliceZ = _dataset.Depth / 2; break;
                    case 1: SliceY = _dataset.Height / 2; break;
                    case 2: SliceX = _dataset.Width / 2; break;
                }

            if (ImGui.MenuItem("Reset Zoom"))
            {
                zoom = 1.0f;
                pan = Vector2.Zero;
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Copy Position"))
                ImGui.SetClipboardText($"X:{_sliceX + 1} Y:{_sliceY + 1} Z:{_sliceZ + 1}");
            ImGui.Separator();
            var showCuttingPlanes = ShowCuttingPlanes;
            if (ImGui.Checkbox("Show Cutting Planes", ref showCuttingPlanes)) ShowCuttingPlanes = showCuttingPlanes;
            ImGui.EndPopup();
        }

        if (isHovered && io.MouseWheel != 0)
        {
            var zoomDelta = io.MouseWheel * 0.1f;
            var newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
            if (newZoom != zoom)
            {
                var mouseCanvasPos = io.MousePos - canvasPos - canvasSize * 0.5f;
                pan -= mouseCanvasPos * (newZoom / zoom - 1.0f);
                zoom = newZoom;
                if (SyncViews) _zoomXY = _zoomXZ = _zoomYZ = zoom;
            }
        }

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

        if (isHovered && io.MouseWheel != 0 && io.KeyCtrl)
        {
            var wheel = (int)io.MouseWheel;
            switch (viewIndex)
            {
                case 0: SliceZ = Math.Clamp(_sliceZ + wheel, 0, _dataset.Depth - 1); break;
                case 1: SliceY = Math.Clamp(_sliceY + wheel, 0, _dataset.Height - 1); break;
                case 2: SliceX = Math.Clamp(_sliceX + wheel, 0, _dataset.Width - 1); break;
            }
        }

        if (!inputHandled && isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
            !(_interactiveSegmentation?.HasActiveSelection ?? false))
            UpdateCrosshairFromMouse(viewIndex, canvasPos, canvasSize, zoom, pan);

        dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);

        if (needsUpdate || texture == null || !texture.IsValid)
        {
            UpdateTexture(viewIndex, ref texture);
            needsUpdate = false;
        }

        dl.PushClipRect(canvasPos, canvasPos + canvasSize, true);

        if (texture != null && texture.IsValid)
        {
            dl.AddImage(texture.GetImGuiTextureId(), imagePos, imagePos + imageSize, Vector2.Zero, Vector2.One,
                0xFFFFFFFF);

            if (ShowCrosshairs)
                DrawCrosshairs(dl, viewIndex, canvasPos, canvasSize, imagePos, imageSize, width, height);
            if (ShowScaleBar) DrawScaleBar(dl, canvasPos, canvasSize, zoom, width, height, viewIndex);
            if (ShowCuttingPlanes && VolumeViewer != null)
                DrawCuttingPlanes(dl, viewIndex, canvasPos, canvasSize, imagePos, imageSize, width, height);

            if (AcousticIntegration.IsActiveFor(_dataset))
            {
                DrawTransducerMarkers(dl, viewIndex, imagePos, imageSize, width, height);
                if (AcousticIntegration.ShouldDrawExtent)
                    DrawSimulationExtent(dl, viewIndex, imagePos, imageSize, width, height);
            }

            RockCoreIntegration.DrawOverlay(_dataset, dl, viewIndex, imagePos, imageSize, width, height,
                _sliceX, _sliceY, _sliceZ);

            TransformIntegration.DrawOverlay(dl, _dataset, viewIndex, imagePos, imageSize, width, height);
            TextureClassificationIntegration.DrawOverlay(_dataset, dl, viewIndex, imagePos, imageSize,
                width, height, _sliceX, _sliceY, _sliceZ);
        }

        if (isHovered && _interactiveSegmentation?.ActiveTool is BrushTool brushTool)
        {
            var brushRadiusPixels = brushTool.BrushSize * (imageSize.X / width);
            dl.AddCircle(io.MousePos, brushRadiusPixels, 0xFF00FFFF, 12, 1.5f);
        }

        dl.PopClipRect();
    }

    private void DrawSimulationExtent(ImDrawListPtr dl, int viewIndex, Vector2 imagePos, Vector2 imageSize,
        int imageWidth, int imageHeight)
    {
        var extentNullable = AcousticIntegration.GetActiveExtent();
        if (!extentNullable.HasValue) return;

        var extent = extentNullable.Value;
        var color = 0xA0FF00FF;

        Vector2 rectMin = Vector2.Zero, rectMax = Vector2.Zero;

        switch (viewIndex)
        {
            case 0:
                if (_sliceZ >= extent.Min.Z && _sliceZ <= extent.Max.Z)
                {
                    rectMin = new Vector2(imagePos.X + (float)extent.Min.X / imageWidth * imageSize.X,
                        imagePos.Y + (float)extent.Min.Y / imageHeight * imageSize.Y);
                    rectMax = new Vector2(imagePos.X + (float)(extent.Max.X + 1) / imageWidth * imageSize.X,
                        imagePos.Y + (float)(extent.Max.Y + 1) / imageHeight * imageSize.Y);
                }

                break;
            case 1:
                if (_sliceY >= extent.Min.Y && _sliceY <= extent.Max.Y)
                {
                    rectMin = new Vector2(imagePos.X + (float)extent.Min.X / imageWidth * imageSize.X,
                        imagePos.Y + (float)extent.Min.Z / imageHeight * imageSize.Y);
                    rectMax = new Vector2(imagePos.X + (float)(extent.Max.X + 1) / imageWidth * imageSize.X,
                        imagePos.Y + (float)(extent.Max.Z + 1) / imageHeight * imageSize.Y);
                }

                break;
            case 2:
                if (_sliceX >= extent.Min.X && _sliceX <= extent.Max.X)
                {
                    rectMin = new Vector2(imagePos.X + (float)extent.Min.Y / imageWidth * imageSize.X,
                        imagePos.Y + (float)extent.Min.Z / imageHeight * imageSize.Y);
                    rectMax = new Vector2(imagePos.X + (float)(extent.Max.Y + 1) / imageWidth * imageSize.X,
                        imagePos.Y + (float)(extent.Max.Z + 1) / imageHeight * imageSize.Y);
                }

                break;
        }

        if (rectMin != rectMax) dl.AddRect(rectMin, rectMax, color, 0, ImDrawFlags.None, 2.0f);
    }

    private void DrawTransducerMarkers(ImDrawListPtr dl, int viewIndex, Vector2 imagePos, Vector2 imageSize,
        int imageWidth, int imageHeight)
    {
        var tx = AcousticIntegration.TxPosition;
        var rx = AcousticIntegration.RxPosition;

        DrawSingleTransducer(dl, viewIndex, imagePos, imageSize, imageWidth, imageHeight, tx, "TX", 0xFF00FFFF);
        DrawSingleTransducer(dl, viewIndex, imagePos, imageSize, imageWidth, imageHeight, rx, "RX", 0xFF00FF00);
    }

    private void DrawSingleTransducer(ImDrawListPtr dl, int viewIndex, Vector2 imagePos, Vector2 imageSize,
        int imageWidth, int imageHeight, Vector3 pos3D, string label, uint color)
    {
        float xNorm = -1, yNorm = -1;
        var onSlice = false;

        switch (viewIndex)
        {
            case 0:
                xNorm = pos3D.X;
                yNorm = pos3D.Y;
                onSlice = Math.Abs(_sliceZ - pos3D.Z * _dataset.Depth) < 1.5f;
                break;
            case 1:
                xNorm = pos3D.X;
                yNorm = pos3D.Z;
                onSlice = Math.Abs(_sliceY - pos3D.Y * _dataset.Height) < 1.5f;
                break;
            case 2:
                xNorm = pos3D.Y;
                yNorm = pos3D.Z;
                onSlice = Math.Abs(_sliceX - pos3D.X * _dataset.Width) < 1.5f;
                break;
        }

        if (xNorm >= 0 && yNorm >= 0)
        {
            var screenX = imagePos.X + xNorm * imageSize.X;
            var screenY = imagePos.Y + yNorm * imageSize.Y;
            var radius = onSlice ? 6.0f : 4.0f;
            var finalColor = onSlice ? color : (color & 0x00FFFFFF) | 0x80000000;

            dl.AddCircleFilled(new Vector2(screenX, screenY), radius, finalColor);
            dl.AddText(new Vector2(screenX + 8, screenY - 8), finalColor, label);
        }
    }

    private void UpdateTexture(int viewIndex, ref TextureManager texture)
    {
        if (_dataset.VolumeData == null && CurrentSliceDisplayMode == SliceDisplayMode.Grayscale)
        {
            Logger.LogError("[CtCombinedViewer] No volume data for grayscale mode");
            return;
        }

        if (_dataset.ThermalResults == null && CurrentSliceDisplayMode == SliceDisplayMode.ThermalField)
        {
            Logger.LogWarning("[CtCombinedViewer] No thermal results; switching to grayscale");
            CurrentSliceDisplayMode = SliceDisplayMode.Grayscale;
            return;
        }

        try
        {
            var (width, height) = GetImageDimensionsForView(viewIndex);
            var rgbaData = new byte[width * height * 4];
            var colorMapIndex = VolumeViewer?.ColorMapIndex ?? 1; // Default to Hot colormap

            // Generate base image
            if (CurrentSliceDisplayMode == SliceDisplayMode.ThermalField)
            {
                var thermalSlice = ExtractThermalSliceData2D(viewIndex, width, height);
                if (thermalSlice == null)
                {
                    Logger.LogError("[CtCombinedViewer] Failed to extract thermal slice");
                    return;
                }

                var labelSlice2D = ExtractLabelSliceData2D(viewIndex, width, height);
                if (labelSlice2D == null)
                {
                    Logger.LogError("[CtCombinedViewer] Failed to extract label slice for masking");
                    return;
                }

                var options = _dataset.ThermalResults.Options;
                var tempRange = options.TemperatureHot - options.TemperatureCold;
                if (tempRange < 1e-5) tempRange = 1.0;

                Parallel.For(0, height, y =>
                {
                    for (var x = 0; x < width; x++)
                    {
                        var i = y * width + x;

                        if (labelSlice2D[x, y] == 0)
                        {
                            rgbaData[i * 4] = 32;
                            rgbaData[i * 4 + 1] = 32;
                            rgbaData[i * 4 + 2] = 32;
                            rgbaData[i * 4 + 3] = 255;
                        }
                        else
                        {
                            var temp = thermalSlice[x, y];
                            var normalizedTemp = (temp - options.TemperatureCold) / tempRange;
                            var colorRgb = ApplyColorMap((float)Math.Clamp(normalizedTemp, 0.0, 1.0), colorMapIndex);

                            rgbaData[i * 4] = (byte)(colorRgb.X * 255);
                            rgbaData[i * 4 + 1] = (byte)(colorRgb.Y * 255);
                            rgbaData[i * 4 + 2] = (byte)(colorRgb.Z * 255);
                            rgbaData[i * 4 + 3] = 255;
                        }
                    }
                });

                if (ShowThermalIsocontours)
                    DrawThermalIsocontours(viewIndex, thermalSlice, labelSlice2D, rgbaData, width, height);
            }
            else // Grayscale Mode
            {
                var rawGrayscaleData = ExtractSliceData(viewIndex, width, height);
                var minWL = _windowLevel - _windowWidth / 2;
                var rangeWL = _windowWidth;
                if (rangeWL < 1e-5f) rangeWL = 255f;

                Parallel.For(0, height, y =>
                {
                    for (var x = 0; x < width; x++)
                    {
                        var i = y * width + x;
                        var rawValue = rawGrayscaleData[i];
                        var wlValue = (rawValue - minWL) / rangeWL;
                        var colorRgb = ApplyColorMap((float)Math.Clamp(wlValue, 0.0, 1.0), colorMapIndex);

                        rgbaData[i * 4] = (byte)(colorRgb.X * 255);
                        rgbaData[i * 4 + 1] = (byte)(colorRgb.Y * 255);
                        rgbaData[i * 4 + 2] = (byte)(colorRgb.Z * 255);
                        rgbaData[i * 4 + 3] = 255;
                    }
                });
            }

            // Apply overlays
            byte[] labelData = null;
            if (_dataset.LabelData != null) labelData = ExtractLabelSliceData(viewIndex, width, height);

            var currentSlice = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => -1 };
            var (isExternalPreviewActive, full3DPreviewMask, previewColor) = CtImageStackTools.GetPreviewData(_dataset);
            var externalPreviewMask = isExternalPreviewActive
                ? ExtractPreviewSlice(full3DPreviewMask, viewIndex, width, height)
                : null;
            var (is2DThresholdPreview, minThreshold, maxThreshold, thresholdColor) =
                CtImageStackTools.Get2DThresholdPreviewState();
            var rawGrayscaleForThreshold = is2DThresholdPreview && CurrentSliceDisplayMode == SliceDisplayMode.Grayscale
                ? ExtractSliceData(viewIndex, width, height)
                : null;
            var segmentationPreviewMask = _interactiveSegmentation?.GetPreviewMask(currentSlice, viewIndex);
            var committedSelectionMask = _interactiveSegmentation?.GetCommittedSelectionMask(currentSlice, viewIndex);
            var targetMaterial =
                _dataset.Materials.FirstOrDefault(m => m.ID == _interactiveSegmentation.TargetMaterialId);

            for (var i = 0; i < width * height; i++)
            {
                var baseColor = new Vector4(rgbaData[i * 4] / 255f, rgbaData[i * 4 + 1] / 255f,
                    rgbaData[i * 4 + 2] / 255f, 1.0f);

                if (labelData != null && labelData[i] > 0)
                {
                    var material = _dataset.Materials.FirstOrDefault(m => m.ID == labelData[i]);
                    if (material != null && GetMaterialVisibility(material.ID))
                    {
                        var opacity = GetMaterialOpacity(material.ID) * 0.4f;
                        var matColor = new Vector4(material.Color.X, material.Color.Y, material.Color.Z, 1.0f);
                        baseColor = Vector4.Lerp(baseColor, matColor, opacity);
                    }
                }

                if (committedSelectionMask != null && committedSelectionMask[i] > 0)
                {
                    var selColor = targetMaterial?.Color ?? new Vector4(0.8f, 0.8f, 0.0f, 1.0f);
                    baseColor = Vector4.Lerp(baseColor, new Vector4(selColor.X, selColor.Y, selColor.Z, 1.0f), 0.4f);
                }

                if (is2DThresholdPreview && rawGrayscaleForThreshold != null &&
                    CurrentSliceDisplayMode == SliceDisplayMode.Grayscale)
                {
                    var rawValue = rawGrayscaleForThreshold[i];
                    if (rawValue >= minThreshold && rawValue <= maxThreshold)
                    {
                        var tColorVec = new Vector4(thresholdColor.X, thresholdColor.Y, thresholdColor.Z, 1.0f);
                        baseColor = Vector4.Lerp(baseColor, tColorVec, 0.5f);
                    }
                }

                if (isExternalPreviewActive && externalPreviewMask != null && externalPreviewMask[i] > 0)
                {
                    var previewRgba = new Vector4(previewColor.X, previewColor.Y, previewColor.Z, 1.0f);
                    baseColor = Vector4.Lerp(baseColor, previewRgba, 0.5f);
                }

                if (segmentationPreviewMask != null && segmentationPreviewMask[i] > 0)
                {
                    var segColor = targetMaterial?.Color ?? new Vector4(1, 0, 0, 1);
                    baseColor = Vector4.Lerp(baseColor, new Vector4(segColor.X, segColor.Y, segColor.Z, 1.0f), 0.6f);
                }

                rgbaData[i * 4] = (byte)(baseColor.X * 255);
                rgbaData[i * 4 + 1] = (byte)(baseColor.Y * 255);
                rgbaData[i * 4 + 2] = (byte)(baseColor.Z * 255);
                rgbaData[i * 4 + 3] = 255;
            }

            texture?.Dispose();
            texture = TextureManager.CreateFromPixelData(rgbaData, (uint)width, (uint)height);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CtCombinedViewer] Error updating texture: {ex.Message}");
        }
    }

    private void DrawThermalIsocontours(int viewIndex, float[,] thermalSlice, byte[,] labelSlice, byte[] rgbaData,
        int width, int height)
    {
        var currentKey = (viewIndex == 0 ? _sliceZ : viewIndex == 1 ? _sliceY : _sliceX, NumThermalIsocontours);
        List<(Vector2, Vector2)> cachedContours = null;
        (int slice, int numContours) cachedKey = (-1, -1);

        switch (viewIndex)
        {
            case 0:
                cachedContours = _cachedIsocontoursXY;
                cachedKey = _cachedKeyXY;
                break;
            case 1:
                cachedContours = _cachedIsocontoursXZ;
                cachedKey = _cachedKeyXZ;
                break;
            case 2:
                cachedContours = _cachedIsocontoursYZ;
                cachedKey = _cachedKeyYZ;
                break;
        }

        if (currentKey != cachedKey || cachedContours.Count == 0)
        {
            cachedContours.Clear();

            var options = _dataset.ThermalResults.Options;
            var tempRange = options.TemperatureHot - options.TemperatureCold;

            for (var i = 1; i <= NumThermalIsocontours; i++)
            {
                var isovalue = options.TemperatureCold + i * tempRange / (NumThermalIsocontours + 1);
                // MODIFIED: Pass labelSlice to the generator
                var lines = IsosurfaceGenerator.GenerateIsocontours(thermalSlice, labelSlice, (float)isovalue);
                cachedContours.AddRange(lines);
            }

            switch (viewIndex)
            {
                case 0: _cachedKeyXY = currentKey; break;
                case 1: _cachedKeyXZ = currentKey; break;
                case 2: _cachedKeyYZ = currentKey; break;
            }
        }

        foreach (var (p1, p2) in cachedContours)
            DrawLineOnImage(rgbaData, width, height,
                (int)p1.X, (int)p1.Y, (int)p2.X, (int)p2.Y,
                255, 255, 255, 255);
    }

    private void DrawLineOnImage(byte[] imageData, int width, int height,
        int x0, int y0, int x1, int y1, byte r, byte g, byte b, byte a)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                var idx = (y0 * width + x0) * 4;
                imageData[idx + 0] = r;
                imageData[idx + 1] = g;
                imageData[idx + 2] = b;
                imageData[idx + 3] = a;
            }

            if (x0 == x1 && y0 == y1) break;

            var e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private byte[] ExtractPreviewSlice(byte[] full3DMask, int viewIndex, int sliceWidth, int sliceHeight)
    {
        if (full3DMask == null) return null;

        var sliceMask = new byte[sliceWidth * sliceHeight];
        var fullWidth = _dataset.Width;
        var fullHeight = _dataset.Height;

        try
        {
            switch (viewIndex)
            {
                case 0:
                    var offset = _sliceZ * fullWidth * fullHeight;
                    if (offset + sliceMask.Length <= full3DMask.Length)
                        Buffer.BlockCopy(full3DMask, offset, sliceMask, 0, sliceMask.Length);
                    break;
                case 1:
                    for (var z = 0; z < sliceHeight; z++)
                    for (var x = 0; x < sliceWidth; x++)
                        sliceMask[z * sliceWidth + x] =
                            full3DMask[z * fullWidth * fullHeight + _sliceY * fullWidth + x];
                    break;
                case 2:
                    for (var z = 0; z < sliceHeight; z++)
                    for (var y = 0; y < sliceWidth; y++)
                        sliceMask[z * sliceWidth + y] =
                            full3DMask[z * fullWidth * fullHeight + y * fullWidth + _sliceX];
                    break;
            }
        }
        catch (IndexOutOfRangeException ex)
        {
            Logger.LogError($"[CtCombinedViewer] Error extracting preview slice for view {viewIndex}: {ex.Message}");
            return null;
        }

        return sliceMask;
    }

    private byte[] ExtractSliceData(int viewIndex, int width, int height)
    {
        var data = new byte[width * height];
        var volume = _dataset.VolumeData;

        try
        {
            switch (viewIndex)
            {
                case 0: volume.ReadSliceZ(_sliceZ, data); break;
                case 1:
                    for (var z = 0; z < height; z++)
                    for (var x = 0; x < width; x++)
                        data[z * width + x] = volume[x, _sliceY, z];
                    break;
                case 2:
                    for (var z = 0; z < height; z++)
                    for (var y = 0; y < width; y++)
                        data[z * width + y] = volume[_sliceX, y, z];
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CtCombinedViewer] Error extracting slice data for view {viewIndex}: {ex.Message}");
        }

        return data;
    }

    private byte[] ExtractLabelSliceData(int viewIndex, int width, int height)
    {
        var data = new byte[width * height];
        var labels = _dataset.LabelData;

        switch (viewIndex)
        {
            case 0: labels.ReadSliceZ(_sliceZ, data); break;
            case 1:
                for (var z = 0; z < height; z++)
                for (var x = 0; x < width; x++)
                    data[z * width + x] = labels[x, _sliceY, z];
                break;
            case 2:
                for (var z = 0; z < height; z++)
                for (var y = 0; y < width; y++)
                    data[z * width + y] = labels[_sliceX, y, z];
                break;
        }

        return data;
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

    private void UpdateCrosshairFromMouse(int viewIndex, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
    {
        var (width, height) = GetImageDimensionsForView(viewIndex);
        var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height, viewIndex);
        var mousePosInImage = GetMousePosInImage(ImGui.GetMousePos(), imagePos, imageSize, width, height);

        switch (viewIndex)
        {
            case 0:
                SliceX = Math.Clamp((int)mousePosInImage.X, 0, _dataset.Width - 1);
                SliceY = Math.Clamp((int)mousePosInImage.Y, 0, _dataset.Height - 1);
                break;
            case 1:
                SliceX = Math.Clamp((int)mousePosInImage.X, 0, _dataset.Width - 1);
                SliceZ = Math.Clamp((int)mousePosInImage.Y, 0, _dataset.Depth - 1);
                break;
            case 2:
                SliceY = Math.Clamp((int)mousePosInImage.X, 0, _dataset.Height - 1);
                SliceZ = Math.Clamp((int)mousePosInImage.Y, 0, _dataset.Depth - 1);
                break;
        }
    }

    private void DrawCrosshairs(ImDrawListPtr dl, int viewIndex, Vector2 canvasPos, Vector2 canvasSize,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
    {
        var color = 0xFF00FF00;
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
            default: return;
        }

        var screenX = imagePos.X + x1 * imageSize.X;
        var screenY = imagePos.Y + y1 * imageSize.Y;

        if (screenX >= imagePos.X && screenX <= imagePos.X + imageSize.X)
            dl.AddLine(new Vector2(screenX, Math.Max(imagePos.Y, canvasPos.Y)),
                new Vector2(screenX, Math.Min(imagePos.Y + imageSize.Y, canvasPos.Y + canvasSize.Y)), color, 1.0f);
        if (screenY >= imagePos.Y && screenY <= imagePos.Y + imageSize.Y)
            dl.AddLine(new Vector2(Math.Max(imagePos.X, canvasPos.X), screenY),
                new Vector2(Math.Min(imagePos.X + imageSize.X, canvasPos.X + canvasSize.X), screenY), color, 1.0f);
    }

    private void DrawCuttingPlanes(ImDrawListPtr dl, int viewIndex, Vector2 canvasPos, Vector2 canvasSize,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
    {
        if (VolumeViewer == null) return;

        if (VolumeViewer.CutXEnabled && (viewIndex == 0 || viewIndex == 1))
        {
            var normalizedX = VolumeViewer.CutXPosition;
            var screenX = imagePos.X + normalizedX * imageSize.X;
            if (screenX >= imagePos.X && screenX <= imagePos.X + imageSize.X)
            {
                uint color = 0x6060FF60;
                dl.AddLine(new Vector2(screenX, Math.Max(imagePos.Y, canvasPos.Y)),
                    new Vector2(screenX, Math.Min(imagePos.Y + imageSize.Y, canvasPos.Y + canvasSize.Y)), color, 2.0f);
                DrawArrow(dl, new Vector2(screenX, imagePos.Y + imageSize.Y * 0.5f),
                    VolumeViewer.CutXForward ? new Vector2(10, 0) : new Vector2(-10, 0), color);
            }
        }

        if (VolumeViewer.CutYEnabled && (viewIndex == 0 || viewIndex == 2))
        {
            var normalizedY = VolumeViewer.CutYPosition;
            var screenY = imagePos.Y + (1.0f - normalizedY) * imageSize.Y;
            if (screenY >= imagePos.Y && screenY <= imagePos.Y + imageSize.Y)
            {
                uint color = 0x6060FF60;
                dl.AddLine(new Vector2(Math.Max(imagePos.X, canvasPos.X), screenY),
                    new Vector2(Math.Min(imagePos.X + imageSize.X, canvasPos.X + canvasSize.X), screenY), color, 2.0f);
                DrawArrow(dl, new Vector2(imagePos.X + imageSize.X * 0.5f, screenY),
                    VolumeViewer.CutYForward ? new Vector2(0, -10) : new Vector2(0, 10), color);
            }
        }

        if (VolumeViewer.CutZEnabled && (viewIndex == 1 || viewIndex == 2))
        {
            var normalizedZ = VolumeViewer.CutZPosition;
            uint color = 0x60FF6060;

            if (viewIndex == 1)
            {
                var screenPos = imagePos.Y + (1.0f - normalizedZ) * imageSize.Y;
                if (screenPos >= imagePos.Y && screenPos <= imagePos.Y + imageSize.Y)
                {
                    dl.AddLine(new Vector2(Math.Max(imagePos.X, canvasPos.X), screenPos),
                        new Vector2(Math.Min(imagePos.X + imageSize.X, canvasPos.X + canvasSize.X), screenPos), color,
                        2.0f);
                    DrawArrow(dl, new Vector2(imagePos.X + imageSize.X * 0.5f, screenPos),
                        VolumeViewer.CutZForward ? new Vector2(0, -10) : new Vector2(0, 10), color);
                }
            }
            else
            {
                var screenPos = imagePos.X + normalizedZ * imageSize.X;
                if (screenPos >= imagePos.X && screenPos <= imagePos.X + imageSize.X)
                {
                    dl.AddLine(new Vector2(screenPos, Math.Max(imagePos.Y, canvasPos.Y)),
                        new Vector2(screenPos, Math.Min(imagePos.Y + imageSize.Y, canvasPos.Y + canvasSize.Y)), color,
                        2.0f);
                    DrawArrow(dl, new Vector2(screenPos, imagePos.Y + imageSize.Y * 0.5f),
                        VolumeViewer.CutZForward ? new Vector2(10, 0) : new Vector2(-10, 0), color);
                }
            }
        }

        if (VolumeViewer.ClippingPlanes != null)
            foreach (var plane in VolumeViewer.ClippingPlanes.Where(p => p.Enabled))
                DrawClippingPlaneIntersection(dl, plane, viewIndex, canvasPos, canvasSize, imagePos, imageSize);
    }

    private void DrawClippingPlaneIntersection(ImDrawListPtr dl, ClippingPlane plane, int viewIndex,
        Vector2 canvasPos, Vector2 canvasSize, Vector2 imagePos, Vector2 imageSize)
    {
        var planeNormal = plane.Normal;
        uint color = 0x60FFFF60;

        switch (viewIndex)
        {
            case 0:
                if (Math.Abs(planeNormal.Z) > 0.1f) dl.AddLine(imagePos, imagePos + imageSize, color, 2.0f);
                break;
            case 1:
                if (Math.Abs(planeNormal.Y) > 0.1f)
                    dl.AddLine(imagePos + new Vector2(0, imageSize.Y), imagePos + new Vector2(imageSize.X, 0), color,
                        2.0f);
                break;
            case 2:
                if (Math.Abs(planeNormal.X) > 0.1f) dl.AddLine(imagePos, imagePos + imageSize, color, 2.0f);
                break;
        }
    }

    private void DrawArrow(ImDrawListPtr dl, Vector2 position, Vector2 direction, uint color)
    {
        var normalized = Vector2.Normalize(direction);
        var perpendicular = new Vector2(-normalized.Y, normalized.X);

        var tip = position + direction;
        var wing1 = tip - normalized * 8 + perpendicular * 4;
        var wing2 = tip - normalized * 8 - perpendicular * 4;

        dl.AddTriangleFilled(tip, wing1, wing2, color);
    }

    private float[,] ExtractThermalSliceData2D(int viewIndex, int width, int height)
    {
        var thermalField = _dataset.ThermalResults?.TemperatureField;
        if (thermalField == null) return null;

        var data = new float[width, height];

        try
        {
            switch (viewIndex)
            {
                case 0: // XY slice
                    if (_sliceZ >= thermalField.GetLength(2)) return null;
                    Parallel.For(0, height, y =>
                    {
                        if (y < thermalField.GetLength(1))
                            for (var x = 0; x < width && x < thermalField.GetLength(0); x++)
                                data[x, y] = thermalField[x, y, _sliceZ];
                    });
                    break;
                case 1: // XZ slice
                    if (_sliceY >= thermalField.GetLength(1)) return null;
                    Parallel.For(0, height, z =>
                    {
                        if (z < thermalField.GetLength(2))
                            for (var x = 0; x < width && x < thermalField.GetLength(0); x++)
                                data[x, z] = thermalField[x, _sliceY, z];
                    });
                    break;
                case 2: // YZ slice
                    if (_sliceX >= thermalField.GetLength(0)) return null;
                    Parallel.For(0, height, z =>
                    {
                        if (z < thermalField.GetLength(2))
                            for (var y = 0; y < width && y < thermalField.GetLength(1); y++)
                                data[y, z] = thermalField[_sliceX, y, z];
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CtCombinedViewer] Error extracting thermal slice: {ex.Message}");
            return null;
        }

        return data;
    }

    private byte[,] ExtractLabelSliceData2D(int viewIndex, int width, int height)
    {
        var labels = _dataset.LabelData;
        if (labels == null) return null;
        var data = new byte[width, height];
        try
        {
            switch (viewIndex)
            {
                case 0: // XY
                    for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                        data[x, y] = labels[x, y, _sliceZ];
                    break;
                case 1: // XZ
                    for (var z = 0; z < height; z++)
                    for (var x = 0; x < width; x++)
                        data[x, z] = labels[x, _sliceY, z];
                    break;
                case 2: // YZ
                    for (var z = 0; z < height; z++)
                    for (var y = 0; y < width; y++)
                        data[y, z] = labels[_sliceX, y, z];
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CtCombinedViewer] Error extracting 2D label slice: {ex.Message}");
            return null;
        }

        return data;
    }

    private void DrawColorScaleLegend(ImDrawListPtr dl, Vector2 pos, Vector2 size)
    {
        var results = _dataset.ThermalResults;
        if (results == null) return;

        var colorMapIndex = VolumeViewer?.ColorMapIndex ?? 0;

        // Draw color gradient
        var steps = 50;
        for (var i = 0; i < steps; i++)
        {
            var t = (float)i / (steps - 1);
            var colorRgb = ApplyColorMap(t, colorMapIndex);
            var color = new Vector4(colorRgb.X, colorRgb.Y, colorRgb.Z, 1.0f);

            var y1 = pos.Y + size.Y * (1.0f - t - 1.0f / steps);
            var y2 = pos.Y + size.Y * (1.0f - t);

            dl.AddRectFilled(new Vector2(pos.X, y1), new Vector2(pos.X + size.X, y2), ImGui.GetColorU32(color));
        }

        dl.AddRect(pos, pos + size, 0xFFFFFFFF);

        var tempHot = results.Options.TemperatureHot;
        var tempCold = results.Options.TemperatureCold;

        dl.AddText(new Vector2(pos.X + size.X + 5, pos.Y - 5), 0xFFFFFFFF, $"{tempHot:F0}K");
        dl.AddText(new Vector2(pos.X + size.X + 5, pos.Y + size.Y - 10), 0xFFFFFFFF, $"{tempCold:F0}K");
    }

    private void DrawScaleBar(ImDrawListPtr dl, Vector2 canvasPos, Vector2 canvasSize, float zoom, int imageWidth,
        int imageHeight, int viewIndex)
    {
        var pixelSizeInUnits = viewIndex switch
        {
            0 => _dataset.PixelSize,
            1 => _dataset.PixelSize,
            2 => _dataset.PixelSize,
            _ => _dataset.PixelSize
        };

        var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, Vector2.Zero, imageWidth,
            imageHeight, viewIndex);
        var scaleFactor = imageSize.X / imageWidth;
        float[] possibleLengths = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 };
        var unit = _dataset.Unit ?? "µm";

        var bestLength = possibleLengths[0];
        foreach (var length in possibleLengths)
            if (length / pixelSizeInUnits * scaleFactor <= 150) bestLength = length;
            else break;

        var barLengthPixels = bestLength / pixelSizeInUnits * scaleFactor;
        var barPos = canvasPos + new Vector2(canvasSize.X - barLengthPixels - 20, canvasSize.Y - 40);

        dl.AddRectFilled(barPos - new Vector2(5, 5), barPos + new Vector2(barLengthPixels + 5, 25), 0xAA000000, 3.0f);
        dl.AddLine(barPos, barPos + new Vector2(barLengthPixels, 0), 0xFFFFFFFF, 3.0f);
        dl.AddLine(barPos, barPos + new Vector2(0, 5), 0xFFFFFFFF, 3.0f);
        dl.AddLine(barPos + new Vector2(barLengthPixels, 0), barPos + new Vector2(barLengthPixels, 5), 0xFFFFFFFF,
            3.0f);

        var text = bestLength >= 1000 ? $"{bestLength / 1000:F0} mm" : $"{bestLength:F0} {unit}";
        var textSize = ImGui.CalcTextSize(text);
        var textPos = barPos + new Vector2((barLengthPixels - textSize.X) * 0.5f, 8);
        dl.AddText(textPos, 0xFFFFFFFF, text);
    }
}