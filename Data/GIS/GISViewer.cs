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
    private readonly CoordinateFormat _coordinateFormat = CoordinateFormat.DecimalDegrees;
    protected readonly List<Vector2> _currentDrawing = new();

    protected readonly GISDataset _dataset;
    protected readonly List<GISDataset> _datasets = new();
    private readonly GISDataset _primaryDataset;

    private readonly ImGuiExportFileDialog _screenshotDialog;
    private readonly Dictionary<string, TileData> _tileCache = new();
    private readonly Dictionary<string, TextureManager> _tileTextures = new();

    private GISLayer _activeLayer;
    private string _apiKey = "";
    private BasemapManager _basemapManager; // Lazy initialization
    private Vector2 _currentPan = Vector2.Zero;
    private Vector2 _currentScreenPos;
    private int _currentTileZoom = 5;
    private Vector2 _currentWorldPos;
    public float _currentZoom = 1.0f;
    private FeatureType _drawingType = FeatureType.Point;
    private EditMode _editMode = EditMode.None;
    private bool _gdalErrorDialogOpened;
    private string _gdalErrorMessage;
    private bool _isLoadingTiles;
    private Vector2 _lastMousePos;
    private bool _requestScreenshot;
    private string _screenshotPath;
    private Vector2 _screenshotRectMax;
    private Vector2 _screenshotRectMin;
    private GISFeature _selectedFeature;
    private int _selectedProviderIndex;
    private bool _showBasemapSettings;
    private bool _showCoordinates = true;

    // GDAL Error Dialog
    private bool _showGdalErrorDialog;
    private bool _showGrid = true;
    private bool _showNorthArrow = true;
    private bool _showScaleBar = true;
    private string _statusMessage = "";
    private Matrix3x2 _viewTransform = Matrix3x2.Identity;
    
    // --- NEW: Texture cache for raster layers ---
    private readonly Dictionary<GISRasterLayer, TextureManager> _rasterLayerTextures = new();


    public GISViewer(GISDataset dataset)
    {
        _datasets.Add(dataset);
        _dataset = dataset;
        _primaryDataset = dataset;
        _activeLayer = dataset.Layers.FirstOrDefault(l => l.IsEditable);

        _screenshotDialog = new ImGuiExportFileDialog("GISScreenshot", "Save Screenshot");
        _screenshotDialog.SetExtensions((".bmp", "Bitmap Image"));
    }

    public GISViewer(List<GISDataset> datasets)
    {
        if (datasets == null || datasets.Count == 0)
            throw new ArgumentException("Must provide at least one dataset");

        _datasets.AddRange(datasets);
        _primaryDataset = datasets[0];
        _dataset = datasets[0];

        // Find first editable layer across all datasets
        foreach (var ds in _datasets)
        {
            _activeLayer = ds.Layers.FirstOrDefault(l => l.IsEditable);
            if (_activeLayer != null) break;
        }

        _screenshotDialog = new ImGuiExportFileDialog("GISScreenshot", "Save Screenshot");
        _screenshotDialog.SetExtensions((".bmp", "Bitmap Image"));
        
        UpdateCombinedBounds();
    }

    public virtual void DrawToolbarControls()
    {
        // Edit mode buttons
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

        // Layer selector
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

        // View options
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
        if (ImGui.Button("Basemap"))
        {
            _showBasemapSettings = !_showBasemapSettings;
            // Try to initialize when user wants to use basemap
            if (_showBasemapSettings && _basemapManager == null) InitializeBasemapManager();
        }

        ImGui.SameLine();
        if (ImGui.Button("Screenshot")) _screenshotDialog.Open($"{_primaryDataset.Name}_capture");
    }

    public virtual void DrawContent(ref float zoom, ref Vector2 pan)
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
    
        // Handle screenshot dialog
        if (_screenshotDialog.Submit())
        {
            _screenshotPath = _screenshotDialog.SelectedPath;
            _screenshotRectMin = canvas_pos;
            _screenshotRectMax = canvas_pos + canvas_size;
            _requestScreenshot = true;
        }
    
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
    
        // 1. Draw online tile basemap if active
        if (_basemapManager != null && _basemapManager.CurrentProvider != null)
        {
            DrawTileBasemap(drawList, canvas_pos, canvas_size, zoom, pan);
        }
    
        // 2. Find and draw the designated raster basemap layer
        GISLayer basemapLayer = null;
        foreach (var dataset in _datasets)
        {
            if (!string.IsNullOrEmpty(dataset.ActiveBasemapLayerName))
            {
                basemapLayer = dataset.Layers.FirstOrDefault(l => l.Name == dataset.ActiveBasemapLayerName && l is GISRasterLayer);
                if (basemapLayer != null)
                {
                    DrawRasterLayer(drawList, (GISRasterLayer)basemapLayer, canvas_pos, canvas_size, zoom, pan);
                    break; // Assume only one active basemap at a time
                }
            }
        }
    
        if (_showGrid) DrawGrid(drawList, canvas_pos, canvas_size, zoom, pan);
    
        // 3. Draw all other visible layers, skipping the one used as basemap
        foreach (var dataset in _datasets)
        {
            foreach (var layer in dataset.Layers.Where(l => l.IsVisible && l != basemapLayer))
            {
                if (layer is GISRasterLayer rasterLayer)
                {
                    DrawRasterLayer(drawList, rasterLayer, canvas_pos, canvas_size, zoom, pan);
                }
                else if (layer.Type == LayerType.Vector)
                {
                    DrawVectorLayer(drawList, layer, canvas_pos, canvas_size, zoom, pan);
                }
            }
        }
    
        if (_currentDrawing.Count > 0) DrawCurrentDrawing(drawList, canvas_pos, canvas_size, zoom, pan);
        if (_showScaleBar) DrawScaleBar(drawList, canvas_pos, canvas_size, zoom, pan);
        if (_showNorthArrow) DrawNorthArrow(drawList, canvas_pos, canvas_size);
    
        drawList.PopClipRect();
    
        // Take screenshot if requested
        if (_requestScreenshot)
        {
            TakeScreenshot(_screenshotPath, _screenshotRectMin, _screenshotRectMax);
            _requestScreenshot = false;
        }
    
        DrawStatusBar(canvas_pos + new Vector2(0, canvas_size.Y), new Vector2(canvas_size.X, statusBarHeight),
            is_hovered);
    
        if (_showBasemapSettings) DrawBasemapSettings();
    
        // Draw GDAL error dialog
        DrawGdalErrorDialog();
    }

    public void Dispose()
    {
        ClearTileCache();
        
        // --- NEW: Dispose raster layer textures ---
        foreach (var texture in _rasterLayerTextures.Values)
        {
            texture.Dispose();
        }
        _rasterLayerTextures.Clear();
    }

    private bool InitializeBasemapManager()
    {
        if (_basemapManager != null)
            return true;

        try
        {
            _basemapManager = BasemapManager.Instance;
            _basemapManager.Initialize(VeldridManager.GraphicsDevice);
            return true;
        }
        catch (TypeInitializationException ex)
        {
            // GDAL native libraries not found
            _gdalErrorMessage = "GDAL libraries are not installed or cannot be loaded.\n\n" +
                                "Basemap features (GeoTIFF, tile servers) will not be available.\n\n" +
                                "You can still use the GIS viewer for vector data.\n\n" +
                                $"Technical details: {ex.InnerException?.Message ?? ex.Message}";
            _showGdalErrorDialog = true;
            Logger.LogWarning($"BasemapManager initialization failed: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _gdalErrorMessage = $"Failed to initialize basemap manager:\n\n{ex.Message}\n\n" +
                                "Basemap features will not be available.";
            _showGdalErrorDialog = true;
            Logger.LogError($"Failed to initialize BasemapManager: {ex.Message}");
            return false;
        }
    }

    private void DrawGdalErrorDialog()
    {
        if (_showGdalErrorDialog && !_gdalErrorDialogOpened)
        {
            ImGui.OpenPopup("GDAL Error###GdalErrorDialog");
            _gdalErrorDialogOpened = true;
        }

        // Custom red title bar style
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.6f, 0.1f, 0.1f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(500, 0), ImGuiCond.Appearing);

        if (ImGui.BeginPopupModal("GDAL Error###GdalErrorDialog", ref _showGdalErrorDialog,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize))
        {
            // Warning icon and title
            ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]); // Default font
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.6f, 0.0f, 1.0f));
            ImGui.Text("⚠");
            ImGui.PopStyleColor();
            ImGui.PopFont();

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.8f, 1.0f));
            ImGui.Text("Basemap Libraries Missing");
            ImGui.PopStyleColor();

            ImGui.Separator();
            ImGui.Spacing();

            // Error message
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextWrapped(_gdalErrorMessage ?? "Unknown error");
            ImGui.PopTextWrapPos();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Center the OK button
            var buttonWidth = 120f;
            var cursorX = (ImGui.GetContentRegionAvail().X - buttonWidth) * 0.5f;
            if (cursorX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + cursorX);

            if (ImGui.Button("OK", new Vector2(buttonWidth, 30)))
            {
                _showGdalErrorDialog = false;
                _gdalErrorDialogOpened = false;
                ImGui.CloseCurrentPopup();
            }

            // Allow Enter or Escape to close
            if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter) ||
                ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _showGdalErrorDialog = false;
                _gdalErrorDialogOpened = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleColor(2); // Pop title bar colors

        // Reset flag when dialog is closed
        if (!_showGdalErrorDialog && _gdalErrorDialogOpened) _gdalErrorDialogOpened = false;
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

    private void TakeScreenshot(string filePath, Vector2 rectMin, Vector2 rectMax)
    {
        var gd = VeldridManager.GraphicsDevice;
        if (gd == null) return;

        var cl = VeldridManager.Factory.CreateCommandList();

        var sourceTexture = gd.SwapchainFramebuffer.ColorTargets[0].Target;

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
                (ImGui.IsMouseDragging(ImGuiMouseButton.Right) && _editMode != EditMode.Draw))
                pan += io.MouseDelta;

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
            if (_basemapManager == null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.6f, 0.0f, 1.0f));
                ImGui.Text("⚠");
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 0.7f, 0.3f, 1),
                    "Basemap functionality is not available");
                ImGui.Spacing();
                ImGui.TextWrapped("GDAL libraries are required for basemap features. " +
                                  "Vector data editing still works normally.");
                ImGui.End();
                return;
            }

            ImGui.Text("Online Basemap Provider:");
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

    private void DrawRasterLayer(ImDrawListPtr drawList, GISRasterLayer layer, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
    {
        if (!_rasterLayerTextures.TryGetValue(layer, out var textureManager))
        {
            // --- NEW: Create texture on demand for the raster layer ---
            try
            {
                var pixelData = layer.GetPixelData();
                // Convert float[,] to byte[] RGBA
                var byteData = new byte[layer.Width * layer.Height * 4];
                for (int y = 0; y < layer.Height; y++)
                {
                    for (int x = 0; x < layer.Width; x++)
                    {
                        var val = (byte)Math.Clamp(pixelData[x, y], 0, 255);
                        var index = (y * layer.Width + x) * 4;
                        byteData[index] = val;     // R
                        byteData[index + 1] = val; // G
                        byteData[index + 2] = val; // B
                        byteData[index + 3] = 255; // A
                    }
                }
                textureManager = TextureManager.CreateFromPixelData(byteData, (uint)layer.Width, (uint)layer.Height);
                _rasterLayerTextures[layer] = textureManager;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to create texture for raster layer '{layer.Name}': {ex.Message}");
                _rasterLayerTextures[layer] = null; // Mark as failed to avoid retrying
                return;
            }
        }
    
        if (textureManager == null || !textureManager.IsValid) return;
    
        var screenTL = WorldToScreen(layer.Bounds.Min, canvasPos, canvasSize, zoom, pan);
        var screenBR = WorldToScreen(layer.Bounds.Max, canvasPos, canvasSize, zoom, pan);
    
        var textureId = textureManager.GetImGuiTextureId();
        if (textureId != IntPtr.Zero)
            drawList.AddImage(textureId, screenTL, screenBR, new Vector2(0, 1), new Vector2(1, 0),
                ImGui.GetColorU32(new Vector4(1, 1, 1, 0.8f)));
    }

    private void DrawTileBasemap(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
    {
        if (_basemapManager == null)
            return;

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
        if (_basemapManager == null)
            return;

        _isLoadingTiles = true;
        foreach (var tile in tiles)
        {
            var tileKey = $"{tile.Z}_{tile.X}_{tile.Y}";
            if (_tileTextures.ContainsKey(tileKey)) continue;

            if (!_tileCache.TryGetValue(tileKey, out var tileData))
            {
                tileData = await _basemapManager.GetTileAsync(tile.X, tile.Y, tile.Z);
                if (tileData != null && tileData.ImageData != null)
                    _tileCache[tileKey] = tileData;
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
            case CoordinateFormat.DecimalDegrees:
                return $"Lon: {coord.X:F6}°, Lat: {coord.Y:F6}°";
            case CoordinateFormat.DegreesMinutesSeconds:
                var lonDMS = CoordinateConverter.FormatDMS(coord.X, true);
                var latDMS = CoordinateConverter.FormatDMS(coord.Y, false);
                return $"Lon: {lonDMS}, Lat: {latDMS}";
            case CoordinateFormat.DegreesMinutes:
                var lonDM = CoordinateConverter.FormatDM(coord.X, true);
                var latDM = CoordinateConverter.FormatDM(coord.Y, false);
                return $"Lon: {lonDM}, Lat: {latDM}";
            default:
                return $"X: {coord.X:F6}, Y: {coord.Y:F6}";
        }
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

    private void DrawVectorLayer(ImDrawListPtr drawList, GISLayer layer, Vector2 canvasPos, Vector2 canvasSize, float zoom,
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

        foreach (var coord in screenCoords)
            drawList.AddCircleFilled(coord, 3, color);

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

    protected void DrawDashedLine(ImDrawListPtr drawList, Vector2 start, Vector2 end, uint color, float thickness)
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

    protected Vector2 WorldToScreen(Vector2 worldPos, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
    {
        var center = canvasPos + canvasSize * 0.5f + pan;
        var offset = (worldPos - _primaryDataset.Center) * zoom;
        return center + new Vector2(offset.X, -offset.Y);
    }

    protected Vector2 ScreenToWorld(Vector2 screenPos, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
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
                {
                    Type = FeatureType.Point,
                    Coordinates = new List<Vector2> { worldPos }
                };
                _dataset.AddFeature(_activeLayer, pointFeature);
                _statusMessage = $"Added point at {FormatCoordinate(worldPos)}";
                break;

            case FeatureType.Line:
                _currentDrawing.Add(worldPos);
                _statusMessage = $"Line: {_currentDrawing.Count} points";
                if (_currentDrawing.Count >= 2 && ImGui.IsKeyPressed(ImGuiKey.Enter))
                    FinishDrawing();
                break;

            case FeatureType.Polygon:
                _currentDrawing.Add(worldPos);
                _statusMessage = $"Polygon: {_currentDrawing.Count} vertices";
                if (_currentDrawing.Count >= 3 && ImGui.IsKeyPressed(ImGuiKey.Enter))
                    FinishDrawing();
                break;
        }
    }

    private void FinishDrawing()
    {
        if (_currentDrawing.Count == 0 || _activeLayer == null) return;

        var feature = new GISFeature
        {
            Type = _drawingType,
            Coordinates = new List<Vector2>(_currentDrawing)
        };
        _dataset.AddFeature(_activeLayer, feature);
        _statusMessage = $"Created {_drawingType} with {_currentDrawing.Count} points";
        _currentDrawing.Clear();
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
}