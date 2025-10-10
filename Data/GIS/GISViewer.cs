// GeoscientistToolkit/UI/GIS/GISViewer.cs

using System.Numerics;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.UI.GIS;

public class GISViewer : IDatasetViewer
{
    private readonly BasemapManager _basemapManager;
    private readonly CoordinateFormat _coordinateFormat = CoordinateFormat.DecimalDegrees;
    private readonly List<Vector2> _currentDrawing = new();

    // --- All existing fields are correct ---
    private readonly GISDataset _dataset;
    private readonly List<GISDataset> _datasets = new();
    private readonly GISDataset _primaryDataset;

    // --- FIX: Screenshot functionality fields corrected ---
    private readonly ImGuiExportFileDialog _screenshotDialog;
    private readonly Dictionary<string, TileData> _tileCache = new();
    private readonly Dictionary<string, TextureManager> _tileTextures = new();
    private GISLayer _activeLayer;
    private string _apiKey = "";
    private Vector2 _currentPan = Vector2.Zero;
    private Vector2 _currentScreenPos;
    private int _currentTileZoom = 5;
    private Vector2 _currentWorldPos;
    private float _currentZoom = 1.0f;
    private FeatureType _drawingType = FeatureType.Point;
    private EditMode _editMode = EditMode.None;
    private GeoTiffData _geoTiffData;
    private TextureManager _geoTiffTexture;
    private bool _isLoadingTiles;
    private Vector2 _lastMousePos;
    private bool _requestScreenshot;
    private string _screenshotPath;
    private Vector2 _screenshotRectMax; // Using Vector2 instead of the non-existent ImRect
    private Vector2 _screenshotRectMin; // Using Vector2 instead of the non-existent ImRect
    private GISFeature _selectedFeature;
    private int _selectedProviderIndex;
    private bool _showBasemapSettings;
    private bool _showCoordinates = true;
    private bool _showGrid = true;
    private bool _showNorthArrow = true;
    private bool _showScaleBar = true;
    private string _statusMessage = "";

    private Matrix3x2 _viewTransform = Matrix3x2.Identity;
    // --- END FIX ---

    public GISViewer(GISDataset dataset)
    {
        _datasets.Add(dataset);
        _primaryDataset = dataset;
        _activeLayer = dataset.Layers.FirstOrDefault(l => l.IsEditable);
        _basemapManager = BasemapManager.Instance;
        _basemapManager.Initialize(VeldridManager.GraphicsDevice);

        _screenshotDialog = new ImGuiExportFileDialog("GISScreenshot", "Save Screenshot");
        _screenshotDialog.SetExtensions((".bmp", "Bitmap Image"));

        if (dataset.BasemapType == BasemapType.GeoTIFF && !string.IsNullOrEmpty(dataset.BasemapPath))
            LoadGeoTiffBasemap(dataset.BasemapPath);
    }

    public GISViewer(List<GISDataset> datasets)
    {
        if (datasets == null || datasets.Count == 0)
            throw new ArgumentException("Must provide at least one dataset");

        _datasets.AddRange(datasets);
        _primaryDataset = datasets[0];

        // Find first editable layer across all datasets
        foreach (var ds in _datasets)
        {
            _activeLayer = ds.Layers.FirstOrDefault(l => l.IsEditable);
            if (_activeLayer != null) break;
        }

        _basemapManager = BasemapManager.Instance;
        _basemapManager.Initialize(VeldridManager.GraphicsDevice);

        _screenshotDialog = new ImGuiExportFileDialog("GISScreenshot", "Save Screenshot");
        _screenshotDialog.SetExtensions((".bmp", "Bitmap Image"));

        // Load basemap from first dataset that has one
        foreach (var ds in _datasets)
            if (ds.BasemapType == BasemapType.GeoTIFF && !string.IsNullOrEmpty(ds.BasemapPath))
            {
                LoadGeoTiffBasemap(ds.BasemapPath);
                break;
            }

        // Update bounds to encompass all datasets
        UpdateCombinedBounds();
    }

    public void DrawToolbarControls()
    {
        // Edit mode buttons...
        if (ImGui.Button("Select")) _editMode = EditMode.None;
        ImGui.SameLine();
        if (ImGui.Button("Point"))
        {
            _editMode = EditMode.Draw;
            _drawingType = FeatureType.Point;
        }

        ImGui.SameLine();
        if (ImGui.Button("Line"))
        {
            _editMode = EditMode.Draw;
            _drawingType = FeatureType.Line;
            _currentDrawing.Clear();
        }

        ImGui.SameLine();
        if (ImGui.Button("Polygon"))
        {
            _editMode = EditMode.Draw;
            _drawingType = FeatureType.Polygon;
            _currentDrawing.Clear();
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Layer selector...
        var editableLayers = _datasets.SelectMany(ds => ds.Layers.Where(l => l.IsEditable)).ToList();

        if (editableLayers.Count > 0)
        {
            ImGui.SetNextItemWidth(150);
            if (ImGui.BeginCombo("##Layer", _activeLayer?.Name ?? "Select Layer"))
            {
                foreach (var layer in editableLayers)
                    if (ImGui.Selectable(layer.Name, layer == _activeLayer))
                        _activeLayer = layer;

                ImGui.EndCombo();
            }
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // View options...
        ImGui.Checkbox("Grid", ref _showGrid);
        ImGui.SameLine();
        ImGui.Checkbox("Coords", ref _showCoordinates);
        ImGui.SameLine();
        ImGui.Checkbox("Scale", ref _showScaleBar);
        ImGui.SameLine();
        ImGui.Checkbox("North Arrow", ref _showNorthArrow);
        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Basemap and Screenshot buttons
        if (ImGui.Button("Basemap")) _showBasemapSettings = !_showBasemapSettings;
        ImGui.SameLine();
        if (ImGui.Button("Screenshot")) _screenshotDialog.Open($"{_primaryDataset.Name}_capture");
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        _currentZoom = zoom;
        _currentPan = pan;

        var drawList = ImGui.GetWindowDrawList();
        var canvas_pos = ImGui.GetCursorScreenPos();
        var canvas_size = ImGui.GetContentRegionAvail();

        var statusBarHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y * 2;
        canvas_size.Y -= statusBarHeight;

        if (canvas_size.X < 50.0f) canvas_size.X = 50.0f;
        if (canvas_size.Y < 50.0f) canvas_size.Y = 50.0f;

        // --- FIX: Handle screenshot dialog submission correctly ---
        if (_screenshotDialog.Submit())
        {
            _screenshotPath = _screenshotDialog.SelectedPath;
            _screenshotRectMin = canvas_pos; // Store the Min vector
            _screenshotRectMax = canvas_pos + canvas_size; // Store the Max vector
            _requestScreenshot = true;
        }
        // --- END FIX ---

        ImGui.InvisibleButton("GISCanvas", canvas_size);
        var io = ImGui.GetIO();
        var is_hovered = ImGui.IsItemHovered();

        _currentScreenPos = io.MousePos - canvas_pos;
        if (is_hovered) _currentWorldPos = ScreenToWorld(_currentScreenPos, canvas_pos, canvas_size, zoom, pan);
        HandleInput(io, ref zoom, ref pan, canvas_pos, canvas_size, is_hovered, ImGui.IsItemActive());
        _lastMousePos = io.MousePos;

        var center = canvas_pos + canvas_size * 0.5f;
        _viewTransform = Matrix3x2.CreateTranslation(-_primaryDataset.Center) * Matrix3x2.CreateScale(zoom) *
                         Matrix3x2.CreateTranslation(center + pan);

        drawList.PushClipRect(canvas_pos, canvas_pos + canvas_size, true);

        // Drawing operations...
        if (_dataset.BasemapType != BasemapType.None || _basemapManager.CurrentProvider != null)
            DrawBasemap(drawList, canvas_pos, canvas_size, zoom, pan);
        if (_showGrid) DrawGrid(drawList, canvas_pos, canvas_size, zoom, pan);
        foreach (var dataset in _datasets)
        foreach (var layer in dataset.Layers.Where(l => l.IsVisible && l.Type == LayerType.Vector))
            DrawLayer(drawList, layer, canvas_pos, canvas_size, zoom, pan);

        if (_currentDrawing.Count > 0) DrawCurrentDrawing(drawList, canvas_pos, canvas_size, zoom, pan);
        if (_showScaleBar) DrawScaleBar(drawList, canvas_pos, canvas_size, zoom, pan);
        if (_showNorthArrow) DrawNorthArrow(drawList, canvas_pos, canvas_size);

        drawList.PopClipRect();

        // --- FIX: Trigger screenshot capture after drawing is complete ---
        if (_requestScreenshot)
        {
            TakeScreenshot(_screenshotPath, _screenshotRectMin, _screenshotRectMax);
            _requestScreenshot = false;
        }
        // --- END FIX ---

        DrawStatusBar(canvas_pos + new Vector2(0, canvas_size.Y), new Vector2(canvas_size.X, statusBarHeight),
            is_hovered);
        if (_showBasemapSettings) DrawBasemapSettings();
    }

    private void UpdateCombinedBounds()
    {
        if (_datasets.Count == 0) return;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var dataset in _datasets)
        {
            if (dataset.Bounds.Min.X < minX) minX = dataset.Bounds.Min.X;
            if (dataset.Bounds.Min.Y < minY) minY = dataset.Bounds.Min.Y;
            if (dataset.Bounds.Max.X > maxX) maxX = dataset.Bounds.Max.X;
            if (dataset.Bounds.Max.Y > maxY) maxY = dataset.Bounds.Max.Y;
        }

        _primaryDataset.Bounds = new BoundingBox
        {
            Min = new Vector2(minX, minY),
            Max = new Vector2(maxX, maxY)
        };

        _primaryDataset.Center = (_primaryDataset.Bounds.Min + _primaryDataset.Bounds.Max) * 0.5f;
    }

    // --- FIX: Method signature and implementation corrected ---
    private void TakeScreenshot(string filePath, Vector2 rectMin, Vector2 rectMax)
    {
        var gd = VeldridManager.GraphicsDevice;
        if (gd == null) return;

        var cl = VeldridManager.Factory.CreateCommandList();

        // FIX 1: Use SwapchainFramebuffer, which is the correct API
        var sourceTexture = gd.SwapchainFramebuffer.ColorTargets[0].Target;

        // FIX 2: Correctly calculate the capture rectangle based on vectors
        var mainViewport = ImGui.GetMainViewport();
        var scaleX = gd.SwapchainFramebuffer.Width / mainViewport.Size.X;
        var scaleY = gd.SwapchainFramebuffer.Height / mainViewport.Size.Y;

        var captureRect = new Rectangle(
            (int)((rectMin.X - mainViewport.Pos.X) * scaleX),
            (int)((rectMin.Y - mainViewport.Pos.Y) * scaleY),
            (int)((rectMax.X - rectMin.X) * scaleX),
            (int)((rectMax.Y - rectMin.Y) * scaleY)
        );

        ImageExporter.SaveTexture(gd, cl, sourceTexture, captureRect, filePath);

        cl.Dispose();
    }
    // --- END FIX ---

    #region Unchanged Methods

    private void HandleInput(ImGuiIOPtr io, ref float zoom, ref Vector2 pan, Vector2 canvas_pos, Vector2 canvas_size,
        bool is_hovered, bool is_active)
    {
        if (is_hovered)
        {
            if (io.MouseWheel != 0)
            {
                var oldZoom = zoom;
                var zoomDelta = io.MouseWheel * 0.1f * zoom;
                zoom = Math.Max(0.1f, Math.Min(50.0f, zoom + zoomDelta));
                if (zoom != oldZoom)
                {
                    var zoomRatio = zoom / oldZoom;
                    var mouseWorld = ScreenToWorld(io.MousePos - canvas_pos, canvas_pos, canvas_size, oldZoom, pan);
                    var centerWorld = ScreenToWorld(canvas_size * 0.5f, canvas_pos, canvas_size, oldZoom, pan);
                    var diff = mouseWorld - centerWorld;
                    pan += diff * (zoomRatio - 1.0f) * oldZoom;
                }

                UpdateTileZoomLevel(zoom);
            }

            if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) ||
                (ImGui.IsMouseDragging(ImGuiMouseButton.Right) && _editMode != EditMode.Draw)) pan += io.MouseDelta;
            if (_editMode == EditMode.Draw && is_active && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                var worldPos = ScreenToWorld(io.MousePos - canvas_pos, canvas_pos, canvas_size, zoom, pan);
                HandleDrawClick(worldPos);
            }
        }
    }

    private void DrawStatusBar(Vector2 pos, Vector2 size, bool isHovered)
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));
        drawList.AddLine(pos, pos + new Vector2(size.X, 0), ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)));
        ImGui.SetCursorScreenPos(pos + new Vector2(5, 2));
        if (isHovered && _showCoordinates)
        {
            var coordText = FormatCoordinate(_currentWorldPos);
            ImGui.Text(coordText);
            ImGui.SameLine(200);
            ImGui.Text($"Zoom: {_currentZoom:F2}x");
            ImGui.SameLine(300);
            ImGui.Text($"Tile Level: {_currentTileZoom}");
            if (_dataset.Layers.Count > 0)
            {
                var totalFeatures = _dataset.Layers.Sum(l => l.Features.Count);
                ImGui.SameLine(400);
                ImGui.Text($"Features: {totalFeatures}");
            }

            if (_isLoadingTiles)
            {
                ImGui.SameLine(500);
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Loading tiles...");
            }
        }
        else
        {
            ImGui.Text("Hover over map to see coordinates");
        }

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.SameLine(600);
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), _statusMessage);
        }
    }

    private void DrawBasemapSettings()
    {
        ImGui.SetNextWindowSize(new Vector2(400, 300), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Basemap Settings", ref _showBasemapSettings))
        {
            ImGui.Text("Basemap Provider:");
            ImGui.SetNextItemWidth(250);
            var providers = BasemapManager.Providers;
            var providerNames = providers.Select(p => p.Name).ToArray();
            if (ImGui.Combo("##Provider", ref _selectedProviderIndex, providerNames, providerNames.Length))
            {
                _basemapManager.CurrentProvider = providers[_selectedProviderIndex];
                ClearTileCache();
            }

            var currentProvider = _basemapManager.CurrentProvider;
            if (currentProvider != null)
            {
                ImGui.Text($"Max Zoom: {currentProvider.MaxZoom}");
                ImGui.Text($"Attribution: {currentProvider.Attribution}");
                if (currentProvider.RequiresApiKey)
                {
                    ImGui.Separator();
                    ImGui.Text("API Key Required:");
                    ImGui.SetNextItemWidth(250);
                    if (ImGui.InputText("##ApiKey", ref _apiKey, 256, ImGuiInputTextFlags.Password))
                        _basemapManager.ApiKey = _apiKey;
                }
            }

            ImGui.Separator();
            if (ImGui.Button("Load GeoTIFF...")) _statusMessage = "GeoTIFF loading dialog would open here";
            if (_geoTiffData != null)
            {
                ImGui.Text($"GeoTIFF: {_geoTiffData.Width}x{_geoTiffData.Height}");
                ImGui.Text($"Bands: {_geoTiffData.BandCount}");
            }

            ImGui.Separator();
            var cacheSize = _basemapManager.GetCacheSize();
            ImGui.Text($"Tile Cache: {FormatBytes(cacheSize)}");
            ImGui.Text($"Cached Tiles: {_tileCache.Count}");
            if (ImGui.Button("Clear Cache"))
            {
                ClearTileCache();
                _basemapManager.ClearCache();
            }

            ImGui.End();
        }
    }

    private void DrawBasemap(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
    {
        if (_dataset.BasemapType == BasemapType.GeoTIFF && _geoTiffTexture != null)
            DrawGeoTiffBasemap(drawList, canvasPos, canvasSize, zoom, pan);
        else if (_dataset.BasemapType == BasemapType.TileServer || _basemapManager.CurrentProvider != null)
            DrawTileBasemap(drawList, canvasPos, canvasSize, zoom, pan);
    }

    private void DrawGeoTiffBasemap(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom,
        Vector2 pan)
    {
        if (_geoTiffData == null || _geoTiffTexture == null || !_geoTiffTexture.IsValid) return;
        var topLeft = new Vector2((float)_geoTiffData.OriginX, (float)_geoTiffData.OriginY);
        var bottomRight = new Vector2((float)(_geoTiffData.OriginX + _geoTiffData.PixelWidth * _geoTiffData.Width),
            (float)(_geoTiffData.OriginY + _geoTiffData.PixelHeight * _geoTiffData.Height));
        var screenTL = WorldToScreen(topLeft, canvasPos, canvasSize, zoom, pan);
        var screenBR = WorldToScreen(bottomRight, canvasPos, canvasSize, zoom, pan);
        var textureId = _geoTiffTexture.GetImGuiTextureId();
        if (textureId != IntPtr.Zero)
            drawList.AddImage(textureId, screenTL, screenBR, new Vector2(0, 0), new Vector2(1, 1),
                ImGui.GetColorU32(new Vector4(1, 1, 1, 0.8f)));
    }

    private void DrawTileBasemap(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
    {
        var topLeft = ScreenToWorld(Vector2.Zero, canvasPos, canvasSize, zoom, pan);
        var bottomRight = ScreenToWorld(canvasSize, canvasPos, canvasSize, zoom, pan);
        var bounds = new BoundingBox
        {
            Min = new Vector2(Math.Min(topLeft.X, bottomRight.X), Math.Min(topLeft.Y, bottomRight.Y)),
            Max = new Vector2(Math.Max(topLeft.X, bottomRight.X), Math.Max(topLeft.Y, bottomRight.Y))
        };
        var visibleTiles = _basemapManager.GetVisibleTiles(bounds, _currentTileZoom);
        Task.Run(() => LoadVisibleTiles(visibleTiles));
        foreach (var tileCoord in visibleTiles)
        {
            var tileKey = $"{tileCoord.Z}_{tileCoord.X}_{tileCoord.Y}";
            if (_tileTextures.TryGetValue(tileKey, out var textureManager) && textureManager.IsValid)
            {
                var tileTL = _basemapManager.TileToLatLon(tileCoord.X, tileCoord.Y, tileCoord.Z);
                var tileBR = _basemapManager.TileToLatLon(tileCoord.X + 1, tileCoord.Y + 1, tileCoord.Z);
                var screenTL = WorldToScreen(tileTL, canvasPos, canvasSize, zoom, pan);
                var screenBR = WorldToScreen(tileBR, canvasPos, canvasSize, zoom, pan);
                var textureId = textureManager.GetImGuiTextureId();
                if (textureId != IntPtr.Zero)
                    drawList.AddImage(textureId, screenTL, screenBR, new Vector2(0, 0), new Vector2(1, 1),
                        ImGui.GetColorU32(new Vector4(1, 1, 1, 1)));
            }
            else
            {
                var tileTL = _basemapManager.TileToLatLon(tileCoord.X, tileCoord.Y, tileCoord.Z);
                var tileBR = _basemapManager.TileToLatLon(tileCoord.X + 1, tileCoord.Y + 1, tileCoord.Z);
                var screenTL = WorldToScreen(tileTL, canvasPos, canvasSize, zoom, pan);
                var screenBR = WorldToScreen(tileBR, canvasPos, canvasSize, zoom, pan);
                drawList.AddRectFilled(screenTL, screenBR, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 0.5f)));
            }
        }
    }

    private async void LoadVisibleTiles(List<TileCoordinate> tiles)
    {
        _isLoadingTiles = true;
        foreach (var tile in tiles)
        {
            var tileKey = $"{tile.Z}_{tile.X}_{tile.Y}";
            if (_tileTextures.ContainsKey(tileKey)) continue;
            if (!_tileCache.TryGetValue(tileKey, out var tileData))
            {
                tileData = await _basemapManager.GetTileAsync(tile.X, tile.Y, tile.Z);
                if (tileData != null && tileData.ImageData != null) _tileCache[tileKey] = tileData;
            }

            if (tileData != null && tileData.ImageData != null)
                VeldridManager.ExecuteOnMainThread(() =>
                {
                    try
                    {
                        var textureManager = CreateTextureFromImageData(tileData.ImageData);
                        if (textureManager != null) _tileTextures[tileKey] = textureManager;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to create tile texture: {ex.Message}");
                    }
                });
        }

        _isLoadingTiles = false;
    }

    private TextureManager CreateTextureFromImageData(byte[] imageData)
    {
        try
        {
            var (pixelData, width, height) = ImageDecoder.DecodeImage(imageData);
            return TextureManager.CreateFromPixelData(pixelData, width, height);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to decode image, using placeholder: {ex.Message}");
            var (pixelData, width, height) = ImageDecoder.CreatePlaceholderImage(256, 256, 100, 100, 100);
            return TextureManager.CreateFromPixelData(pixelData, width, height);
        }
    }

    private void LoadGeoTiffBasemap(string path)
    {
        _geoTiffData = _basemapManager.LoadGeoTiff(path);
        if (_geoTiffData != null && _geoTiffData.Data != null)
            VeldridManager.ExecuteOnMainThread(() =>
            {
                try
                {
                    _geoTiffTexture = TextureManager.CreateFromPixelData(_geoTiffData.Data, (uint)_geoTiffData.Width,
                        (uint)_geoTiffData.Height);
                    _statusMessage = $"Loaded GeoTIFF: {_geoTiffData.Width}x{_geoTiffData.Height}";
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to create GeoTIFF texture: {ex.Message}");
                }
            });
    }

    private void UpdateTileZoomLevel(float mapZoom)
    {
        var newTileZoom = (int)(Math.Log2(mapZoom) + 5);
        newTileZoom = Math.Max(0, Math.Min(18, newTileZoom));
        if (newTileZoom != _currentTileZoom) _currentTileZoom = newTileZoom;
    }

    private void ClearTileCache()
    {
        foreach (var texture in _tileTextures.Values) texture?.Dispose();
        _tileTextures.Clear();
        _tileCache.Clear();
    }

    private string FormatCoordinate(Vector2 coord)
    {
        switch (_coordinateFormat)
        {
            case CoordinateFormat.DecimalDegrees: return $"Lon: {coord.X:F6}°, Lat: {coord.Y:F6}°";
            case CoordinateFormat.DegreesMinutesSeconds:
                var lonDMS = ConvertToDMS(coord.X, true);
                var latDMS = ConvertToDMS(coord.Y, false);
                return $"Lon: {lonDMS}, Lat: {latDMS}";
            case CoordinateFormat.DegreesMinutes:
                var lonDM = ConvertToDM(coord.X, true);
                var latDM = ConvertToDM(coord.Y, false);
                return $"Lon: {lonDM}, Lat: {latDM}";
            default: return $"X: {coord.X:F6}, Y: {coord.Y:F6}";
        }
    }

    private string ConvertToDMS(float decimalDegrees, bool isLongitude)
    {
        return CoordinateConverter.FormatDMS(decimalDegrees, isLongitude);
    }

    private string ConvertToDM(float decimalDegrees, bool isLongitude)
    {
        return CoordinateConverter.FormatDM(decimalDegrees, isLongitude);
    }

    private string GetCoordinateFormatName(CoordinateFormat format)
    {
        return format switch
        {
            CoordinateFormat.DecimalDegrees => "Decimal", CoordinateFormat.DegreesMinutesSeconds => "DMS",
            CoordinateFormat.DegreesMinutes => "DM", _ => "Unknown"
        };
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    private void DrawGrid(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
    {
        var gridColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
        var majorGridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f));
        var baseSpacing = 1.0f;
        var power = (float)Math.Floor(Math.Log10(10.0f / zoom));
        var spacing = (float)Math.Pow(10, power) * zoom;
        if (spacing < 20) spacing *= 10;
        if (spacing > 200) spacing /= 10;
        var center = canvasPos + canvasSize * 0.5f + pan;
        var startY = center.Y % spacing;
        var lineCount = 0;
        for (var y = startY; y < canvasSize.Y; y += spacing)
        {
            var color = lineCount % 5 == 0 ? majorGridColor : gridColor;
            drawList.AddLine(canvasPos + new Vector2(0, y), canvasPos + new Vector2(canvasSize.X, y), color);
            lineCount++;
        }

        var startX = center.X % spacing;
        lineCount = 0;
        for (var x = startX; x < canvasSize.X; x += spacing)
        {
            var color = lineCount % 5 == 0 ? majorGridColor : gridColor;
            drawList.AddLine(canvasPos + new Vector2(x, 0), canvasPos + new Vector2(x, canvasSize.Y), color);
            lineCount++;
        }
    }

    private void DrawLayer(ImDrawListPtr drawList, GISLayer layer, Vector2 canvasPos, Vector2 canvasSize, float zoom,
        Vector2 pan)
    {
        var color = ImGui.GetColorU32(layer.Color);
        foreach (var feature in layer.Features)
            DrawFeature(drawList, feature, canvasPos, canvasSize, zoom, pan, color, layer);
    }

    private void DrawFeature(ImDrawListPtr drawList, GISFeature feature, Vector2 canvasPos, Vector2 canvasSize,
        float zoom, Vector2 pan, uint color, GISLayer layer)
    {
        if (feature.Coordinates.Count == 0) return;
        var screenCoords = feature.Coordinates.Select(c => WorldToScreen(c, canvasPos, canvasSize, zoom, pan)).ToList();
        if (feature.IsSelected) color = ImGui.GetColorU32(new Vector4(1, 1, 0, 1));
        switch (feature.Type)
        {
            case FeatureType.Point:
                foreach (var coord in screenCoords)
                {
                    drawList.AddCircleFilled(coord, layer.PointSize, color);
                    if (feature.IsSelected)
                        drawList.AddCircle(coord, layer.PointSize + 2, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), 0,
                            2);
                }

                break;
            case FeatureType.Line:
                if (screenCoords.Count >= 2)
                    for (var i = 0; i < screenCoords.Count - 1; i++)
                        drawList.AddLine(screenCoords[i], screenCoords[i + 1], color, layer.LineWidth);

                break;
            case FeatureType.Polygon:
                if (screenCoords.Count >= 3)
                {
                    var fillColor = ImGui.GetColorU32(new Vector4(layer.Color.X, layer.Color.Y, layer.Color.Z,
                        layer.Color.W * 0.3f));
                    var coordArray = screenCoords.ToArray();
                    drawList.AddConvexPolyFilled(ref coordArray[0], screenCoords.Count, fillColor);
                    drawList.AddPolyline(ref coordArray[0], screenCoords.Count, color, ImDrawFlags.Closed,
                        layer.LineWidth);
                }

                break;
        }
    }

    private void DrawCurrentDrawing(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom,
        Vector2 pan)
    {
        if (_currentDrawing.Count == 0) return;
        var color = ImGui.GetColorU32(new Vector4(1, 0.5f, 0, 1));
        var screenCoords = _currentDrawing.Select(c => WorldToScreen(c, canvasPos, canvasSize, zoom, pan)).ToList();
        foreach (var coord in screenCoords) drawList.AddCircleFilled(coord, 3, color);
        if (_drawingType != FeatureType.Point && screenCoords.Count >= 2)
        {
            for (var i = 0; i < screenCoords.Count - 1; i++)
                drawList.AddLine(screenCoords[i], screenCoords[i + 1], color, 2);
            if (_drawingType == FeatureType.Polygon && screenCoords.Count >= 3)
            {
                var dashColor = ImGui.GetColorU32(new Vector4(1, 0.5f, 0, 0.5f));
                DrawDashedLine(drawList, screenCoords[screenCoords.Count - 1], screenCoords[0], dashColor, 2);
            }
        }
    }

    private void DrawDashedLine(ImDrawListPtr drawList, Vector2 start, Vector2 end, uint color, float thickness)
    {
        var dir = end - start;
        var length = dir.Length();
        dir = Vector2.Normalize(dir);
        var dashLength = 5.0f;
        var gapLength = 5.0f;
        float currentLength = 0;
        var drawing = true;
        while (currentLength < length)
        {
            var segmentLength = drawing ? dashLength : gapLength;
            if (currentLength + segmentLength > length) segmentLength = length - currentLength;
            if (drawing)
            {
                var segStart = start + dir * currentLength;
                var segEnd = start + dir * (currentLength + segmentLength);
                drawList.AddLine(segStart, segEnd, color, thickness);
            }

            currentLength += segmentLength;
            drawing = !drawing;
        }
    }

    private void DrawNorthArrow(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
    {
        var arrowPos = canvasPos + new Vector2(canvasSize.X - 50, 50);
        var arrowSize = 20f;
        var northColor = ImGui.GetColorU32(new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        var bodyColor = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);
        drawList.AddTriangleFilled(arrowPos, arrowPos + new Vector2(-arrowSize / 2, arrowSize),
            arrowPos + new Vector2(arrowSize / 2, arrowSize), northColor);
        drawList.AddRectFilled(arrowPos + new Vector2(-arrowSize / 4, arrowSize),
            arrowPos + new Vector2(arrowSize / 4, arrowSize * 2), bodyColor);
        var textSize = ImGui.CalcTextSize("N");
        drawList.AddText(arrowPos - new Vector2(textSize.X * 0.5f, textSize.Y), textColor, "N");
    }

    private void DrawScaleBar(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
    {
        var barStartPos = canvasPos + new Vector2(20, canvasSize.Y - 30);
        var maxBarWidth = 150f;
        var centerScreen = canvasPos + canvasSize / 2f;
        var p1World = ScreenToWorld(centerScreen - new Vector2(maxBarWidth / 2, 0) - canvasPos, canvasPos, canvasSize,
            zoom, pan);
        var p2World = ScreenToWorld(centerScreen + new Vector2(maxBarWidth / 2, 0) - canvasPos, canvasPos, canvasSize,
            zoom, pan);
        var worldDistance = HaversineDistance(p1World, p2World);
        var niceDistance = GetNiceScaleDistance(worldDistance);
        var barPixelWidth = (float)(maxBarWidth * (niceDistance / worldDistance));
        var scaleText = FormatDistance(niceDistance);
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);
        var textSize = ImGui.CalcTextSize(scaleText);
        drawList.AddText(barStartPos + new Vector2((barPixelWidth - textSize.X) / 2, -textSize.Y - 5), textColor,
            scaleText);
        drawList.AddLine(barStartPos, barStartPos + new Vector2(barPixelWidth, 0), textColor, 2f);
        drawList.AddLine(barStartPos, barStartPos - new Vector2(0, 5), textColor, 2f);
        drawList.AddLine(barStartPos + new Vector2(barPixelWidth, 0), barStartPos + new Vector2(barPixelWidth, -5),
            textColor, 2f);
    }

    private double GetNiceScaleDistance(double realDistance)
    {
        var exponent = Math.Floor(Math.Log10(realDistance));
        var powerOf10 = Math.Pow(10, exponent);
        var fraction = realDistance / powerOf10;
        double niceFraction;
        if (fraction < 1.5) niceFraction = 1;
        else if (fraction < 3) niceFraction = 2;
        else if (fraction < 7) niceFraction = 5;
        else niceFraction = 10;
        return niceFraction * powerOf10;
    }

    private string FormatDistance(double distanceKm)
    {
        if (distanceKm >= 1) return $"{distanceKm:G3} km";

        return $"{distanceKm * 1000:F0} m";
    }

    private double HaversineDistance(Vector2 point1, Vector2 point2)
    {
        const double R = 6371.0;
        var lat1 = point1.Y * (Math.PI / 180.0);
        var lon1 = point1.X * (Math.PI / 180.0);
        var lat2 = point2.Y * (Math.PI / 180.0);
        var lon2 = point2.X * (Math.PI / 180.0);
        var dLat = lat2 - lat1;
        var dLon = lon2 - lon1;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private Vector2 WorldToScreen(Vector2 worldPos, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
    {
        var center = canvasPos + canvasSize * 0.5f + pan;
        var offset = (worldPos - _primaryDataset.Center) * zoom;
        return center + new Vector2(offset.X, -offset.Y);
    }

    private Vector2 ScreenToWorld(Vector2 screenPos, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
    {
        var center = canvasSize * 0.5f + pan;
        var offset = screenPos - center;
        return _primaryDataset.Center + new Vector2(offset.X / zoom, -offset.Y / zoom);
    }

    private void HandleDrawClick(Vector2 worldPos)
    {
        if (_activeLayer == null || !_activeLayer.IsEditable)
        {
            _statusMessage = "No editable layer selected";
            return;
        }

        switch (_drawingType)
        {
            case FeatureType.Point:
                var pointFeature = new GISFeature
                    { Type = FeatureType.Point, Coordinates = new List<Vector2> { worldPos } };
                _dataset.AddFeature(_activeLayer, pointFeature);
                _statusMessage = $"Added point at {FormatCoordinate(worldPos)}";
                break;
            case FeatureType.Line:
                _currentDrawing.Add(worldPos);
                _statusMessage = $"Line: {_currentDrawing.Count} points";
                if (_currentDrawing.Count >= 2 && ImGui.IsKeyPressed(ImGuiKey.Enter)) FinishDrawing();
                break;
            case FeatureType.Polygon:
                _currentDrawing.Add(worldPos);
                _statusMessage = $"Polygon: {_currentDrawing.Count} vertices";
                if (_currentDrawing.Count >= 3 && ImGui.IsKeyPressed(ImGuiKey.Enter)) FinishDrawing();
                break;
        }
    }

    private void FinishDrawing()
    {
        if (_currentDrawing.Count == 0 || _activeLayer == null) return;
        var feature = new GISFeature { Type = _drawingType, Coordinates = new List<Vector2>(_currentDrawing) };
        _dataset.AddFeature(_activeLayer, feature);
        _statusMessage = $"Created {_drawingType} with {_currentDrawing.Count} points";
        _currentDrawing.Clear();
    }

    public void Dispose()
    {
        ClearTileCache();
        _geoTiffTexture?.Dispose();
    }

    private enum EditMode
    {
        None,
        Draw,
        Edit,
        Delete
    }

    private enum CoordinateFormat
    {
        DecimalDegrees,
        DegreesMinutesSeconds,
        DegreesMinutes
    }

    #endregion
}