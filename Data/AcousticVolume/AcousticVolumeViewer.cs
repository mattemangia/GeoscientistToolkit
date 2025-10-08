// GeoscientistToolkit/Data/AcousticVolume/AcousticVolumeViewer.cs

using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.AcousticVolume;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.AcousticVolume;

/// <summary>
///     Viewer for acoustic simulation results with wave field visualization,
///     time-series animation, and interactive analysis capabilities.
///     Now with high-performance, direct snapshot rendering.
/// </summary>
public class AcousticVolumeViewer : IDatasetViewer, IDisposable
{
    private readonly float _animationSpeed = 1.0f;

    // Color maps
    private readonly string[] _colorMapNames = { "Grayscale", "Jet", "Viridis", "Hot", "Cool", "Seismic" };
    private readonly AcousticVolumeDataset _dataset;
    private readonly float _frameInterval = 0.033f; // 30 FPS
    private readonly ProgressBarDialog _loadingProgress;

    // Thread-safe queue for main thread graphics operations
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    // Cache for currently displayed slices to avoid reloading
    private readonly SliceCache _sliceCache;
    private int _colorMapIndex;
    private float _contrastMax = 1.0f;

    // Display settings
    private float _contrastMin;
    private int _currentFrameIndex;
    private VisualizationMode _currentMode = VisualizationMode.CombinedField;

    // Histogram cache for the 4th panel
    private float[] _histogramDataXY;
    private bool _isDrawingLine;

    // Loading state
    private bool _isInitialized;
    private bool _isLoading;

    // Time series animation
    private bool _isPlaying;
    private DateTime _lastFrameTime = DateTime.MinValue;
    private VisualizationMode _lastHistogramMode;
    private int _lastHistogramSliceZ = -1;
    private Layout _layout = Layout.Grid2x2;

    // Line drawing state for FFT
    private Vector2 _lineStartPos;
    private string _loadingError;
    private bool _needsUpdateXY = true;
    private bool _needsUpdateXZ = true;
    private bool _needsUpdateYZ = true;
    private Vector2 _panXY = Vector2.Zero;
    private Vector2 _panXZ = Vector2.Zero;
    private Vector2 _panYZ = Vector2.Zero;
    private bool _showGrid = true;
    private bool _showInfo = true;
    private bool _showLegend = true;

    // Slice positions
    private int _sliceX;
    private int _sliceY;
    private int _sliceZ;

    // Textures for each view
    private TextureManager _textureXY;
    private TextureManager _textureXZ;
    private TextureManager _textureYZ;
    private int _timeSeriesComponent; // 0=Magnitude, 1=Vx, 2=Vy, 3=Vz

    // Zoom and pan for each view
    private float _zoomXY = 1.0f;
    private float _zoomXZ = 1.0f;
    private float _zoomYZ = 1.0f;

    public AcousticVolumeViewer(AcousticVolumeDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _loadingProgress = new ProgressBarDialog("Loading Acoustic Volume");
        _sliceCache = new SliceCache();

        // Don't load synchronously - just set initial state
        _sliceX = 0;
        _sliceY = 0;
        _sliceZ = 0;

        // Automatically start initialization when viewer is created
        _ = InitializeAsync();

        Logger.Log($"[AcousticVolumeViewer] Created viewer for: {_dataset.Name}");
    }

    public void DrawToolbarControls()
    {
        // Check initialization status
        if (!_isInitialized)
        {
            ImGui.TextDisabled(_isLoading ? "Loading..." : "Initializing viewer...");
            return;
        }

        if (!string.IsNullOrEmpty(_loadingError))
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error: {_loadingError}");
            return;
        }

        // Mode selection
        ImGui.Text("Mode:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        string[] modes = { "P-Wave", "S-Wave", "Combined", "Damage", "Time Series" };
        var modeIndex = (int)_currentMode;
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
                _sliceCache.Clear(); // Clear cache when changing modes
            }
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Time series controls
        if (_currentMode == VisualizationMode.TimeSeries)
        {
            if (_dataset.TimeSeriesSnapshots?.Count > 0)
            {
                if (ImGui.Button(_isPlaying ? "⏸" : "▶"))
                {
                    _isPlaying = !_isPlaying;
                    _lastFrameTime = DateTime.Now;
                }

                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                if (ImGui.SliderInt("Frame", ref _currentFrameIndex, 0, _dataset.TimeSeriesSnapshots.Count - 1))
                    _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;

                ImGui.SameLine();
                ImGui.Text($"{_currentFrameIndex + 1}/{_dataset.TimeSeriesSnapshots.Count}");

                ImGui.SameLine();
                ImGui.SetNextItemWidth(120);
                string[] components = { "Magnitude", "Vx", "Vy", "Vz" };
                if (ImGui.Combo("Component", ref _timeSeriesComponent, components, components.Length))
                    _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            }
            else
            {
                ImGui.TextDisabled("No time series data loaded.");
            }
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Color map selection
        ImGui.Text("Colors:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.Combo("##ColorMap", ref _colorMapIndex, _colorMapNames, _colorMapNames.Length))
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;

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
        if (ImGui.Button("Reset Views")) ResetViews();
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        // Process any pending graphics operations that were queued from background threads
        while (_mainThreadActions.TryDequeue(out var action)) action?.Invoke();

        // Handle loading/progress dialog
        if (_isLoading) _loadingProgress.Submit();

        if (!_isInitialized)
        {
            if (_isLoading)
                ImGui.TextDisabled("Loading acoustic volume data...");
            else if (!string.IsNullOrEmpty(_loadingError))
                ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error: {_loadingError}");
            else
                ImGui.TextDisabled("Initializing...");
            return;
        }

        // Update animation
        if (_isPlaying && _currentMode == VisualizationMode.TimeSeries && _dataset.TimeSeriesSnapshots?.Count > 0)
            UpdateAnimation();

        // Main layout
        float controlPanelWidth = 300;
        var availableSize = ImGui.GetContentRegionAvail();

        // Main view panel (left)
        ImGui.BeginChild("SliceViews",
            new Vector2(availableSize.X - controlPanelWidth - ImGui.GetStyle().ItemSpacing.X, availableSize.Y),
            ImGuiChildFlags.None);
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
        ImGui.BeginChild("InfoAndControlPanel", new Vector2(controlPanelWidth, availableSize.Y),
            ImGuiChildFlags.Border);
        DrawInfoPanel();
        ImGui.EndChild();

        // Draw legend if enabled (it's a separate window)
        if (_showLegend) DrawLegend();
    }

    public void Dispose()
    {
        _textureXY?.Dispose();
        _textureXZ?.Dispose();
        _textureYZ?.Dispose();
        _sliceCache?.Clear();
        _loadingProgress?.Close();
    }

    /// <summary>
    ///     Asynchronously initializes the viewer by loading metadata and preparing volumes.
    /// </summary>
    private async Task InitializeAsync()
    {
        if (_isInitialized || _isLoading) return;

        _isLoading = true;
        _loadingProgress.Open("Loading acoustic volume metadata...");

        try
        {
            await Task.Run(async () =>
            {
                _loadingProgress.Update(0.1f, "Loading metadata...");

                // Load only metadata initially, not the full volumes
                if (!Directory.Exists(_dataset.FilePath))
                    throw new DirectoryNotFoundException($"Dataset directory not found: {_dataset.FilePath}");

                // Load metadata files
                var metadataPath = Path.Combine(_dataset.FilePath, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    _loadingProgress.Update(0.2f, "Reading metadata...");
                    var json = File.ReadAllText(metadataPath);
                    var metadata = JsonSerializer.Deserialize<AcousticMetadata>(json);
                    ApplyMetadata(metadata);
                }

                // Convert existing volumes to lazy-loading if needed
                _loadingProgress.Update(0.3f, "Preparing wave field volumes...");
                await ConvertToLazyVolumesAsync();

                // Get dimensions from the first available volume
                var initialVolume = GetCurrentVolume();
                if (initialVolume != null)
                {
                    _sliceX = initialVolume.Width / 2;
                    _sliceY = initialVolume.Height / 2;
                    _sliceZ = initialVolume.Depth / 2;
                }

                _loadingProgress.Update(1.0f, "Ready!");
            });

            _isInitialized = true;
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            Logger.Log("[AcousticVolumeViewer] Initialized successfully");
        }
        catch (Exception ex)
        {
            _loadingError = ex.Message;
            Logger.LogError($"[AcousticVolumeViewer] Initialization failed: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            _loadingProgress.Close();
        }
    }

    private void ApplyMetadata(AcousticMetadata metadata)
    {
        _dataset.PWaveVelocity = metadata.PWaveVelocity;
        _dataset.SWaveVelocity = metadata.SWaveVelocity;
        _dataset.VpVsRatio = metadata.VpVsRatio;
        _dataset.TimeSteps = metadata.TimeSteps;
        _dataset.ComputationTime = TimeSpan.FromSeconds(metadata.ComputationTimeSeconds);
        _dataset.YoungsModulusMPa = metadata.YoungsModulusMPa;
        _dataset.PoissonRatio = metadata.PoissonRatio;
        _dataset.ConfiningPressureMPa = metadata.ConfiningPressureMPa;
        _dataset.SourceFrequencyKHz = metadata.SourceFrequencyKHz;
        _dataset.SourceEnergyJ = metadata.SourceEnergyJ;
        _dataset.SourceDatasetPath = metadata.SourceDatasetPath;
        _dataset.SourceMaterialName = metadata.SourceMaterialName;
    }

    /// <summary>
    ///     Converts file-based volumes to lazy-loading volumes if not already loaded.
    /// </summary>
    private async Task ConvertToLazyVolumesAsync()
    {
        // Check for P-Wave field
        if (_dataset.PWaveField == null)
        {
            var pWavePath = Path.Combine(_dataset.FilePath, "PWaveField.bin");
            if (File.Exists(pWavePath)) _dataset.PWaveField = await LoadLazyVolumeAsync(pWavePath, "P-Wave field");
        }

        // Check for S-Wave field
        if (_dataset.SWaveField == null)
        {
            var sWavePath = Path.Combine(_dataset.FilePath, "SWaveField.bin");
            if (File.Exists(sWavePath)) _dataset.SWaveField = await LoadLazyVolumeAsync(sWavePath, "S-Wave field");
        }

        // Check for Combined field
        if (_dataset.CombinedWaveField == null)
        {
            var combinedPath = Path.Combine(_dataset.FilePath, "CombinedField.bin");
            if (File.Exists(combinedPath))
                _dataset.CombinedWaveField = await LoadLazyVolumeAsync(combinedPath, "Combined field");
        }

        // Check for Damage field
        if (_dataset.DamageField == null)
        {
            var damagePath = Path.Combine(_dataset.FilePath, "DamageField.bin");
            if (File.Exists(damagePath)) _dataset.DamageField = await LoadLazyVolumeAsync(damagePath, "Damage field");
        }
    }

    private async Task<ChunkedVolume> LoadLazyVolumeAsync(string path, string fieldName)
    {
        _loadingProgress.Update(0.5f, $"Loading {fieldName}...");

        // Create a lazy-loading wrapper
        var lazyVolume = await LazyChunkedVolume.CreateAsync(path);

        // For compatibility with existing code, we need to return a ChunkedVolume
        // We'll create a wrapper that uses lazy loading internally
        return new LazyChunkedVolumeAdapter(lazyVolume);
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
        var viewWidth = (availableSize.X - ImGui.GetStyle().ItemSpacing.X * 2) / 3;
        DrawView(0, new Vector2(viewWidth, availableSize.Y), "XY (Axial)");
        ImGui.SameLine();
        DrawView(1, new Vector2(viewWidth, availableSize.Y), "XZ (Coronal)");
        ImGui.SameLine();
        DrawView(2, new Vector2(viewWidth, availableSize.Y), "YZ (Sagittal)");
    }

    private void DrawVerticalLayout(Vector2 availableSize)
    {
        var viewHeight = (availableSize.Y - ImGui.GetStyle().ItemSpacing.Y * 2) / 3;
        DrawView(0, new Vector2(availableSize.X, viewHeight), "XY (Axial)");
        DrawView(1, new Vector2(availableSize.X, viewHeight), "XZ (Coronal)");
        DrawView(2, new Vector2(availableSize.X, viewHeight), "YZ (Sagittal)");
    }

    private void DrawGrid2x2Layout(Vector2 availableSize)
    {
        var viewWidth = (availableSize.X - ImGui.GetStyle().ItemSpacing.X) / 2;
        var viewHeight = (availableSize.Y - ImGui.GetStyle().ItemSpacing.Y) / 2;

        DrawView(0, new Vector2(viewWidth, viewHeight), "XY (Axial)");
        ImGui.SameLine();
        DrawView(1, new Vector2(viewWidth, viewHeight), "XZ (Coronal)");

        DrawView(2, new Vector2(viewWidth, viewHeight), "YZ (Sagittal)");
        ImGui.SameLine();

        // 4th view is now a histogram
        DrawHistogramPanel(new Vector2(viewWidth, viewHeight));
    }

    private void DrawView(int viewIndex, Vector2 size, string title)
    {
        ImGui.BeginChild($"View{viewIndex}", size, ImGuiChildFlags.Border);
        ImGui.Text(title);
        ImGui.SameLine();

        var (hasVolume, width, height, depth) = GetCurrentVolumeDimensions();
        if (hasVolume)
        {
            var slice = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 };
            var maxSlice = viewIndex switch { 0 => depth - 1, 1 => height - 1, 2 => width - 1, _ => 0 };
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt($"##Slice{viewIndex}", ref slice, 0, maxSlice))
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
        var isHovered = ImGui.IsItemHovered();

        // Handle zoom and pan
        var zoom = viewIndex switch { 0 => _zoomXY, 1 => _zoomXZ, _ => _zoomYZ };
        var pan = viewIndex switch { 0 => _panXY, 1 => _panXZ, _ => _panYZ };

        if (isHovered && io.MouseWheel != 0)
        {
            var zoomDelta = io.MouseWheel * 0.1f;
            var newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
            var mouseCanvasPos = io.MousePos - canvasPos - canvasSize * 0.5f;
            pan -= mouseCanvasPos * (newZoom / zoom - 1.0f);
            zoom = newZoom;
        }

        if (isHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle)) pan += io.MouseDelta;

        switch (viewIndex)
        {
            case 0:
                _zoomXY = zoom;
                _panXY = pan;
                break;
            case 1:
                _zoomXZ = zoom;
                _panXZ = pan;
                break;
            case 2:
                _zoomYZ = zoom;
                _panYZ = pan;
                break;
        }

        // Interaction logic
        if (isHovered)
        {
            // Point Probing (on right-click or holding Ctrl)
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) ||
                (ImGui.IsMouseDown(ImGuiMouseButton.Left) && io.KeyCtrl))
                HandlePointProbing(io, canvasPos, canvasSize, zoom, pan, viewIndex);

            // Line Drawing
            if (AcousticInteractionManager.InteractionMode == ViewerInteractionMode.DrawingLine)
                HandleLineDrawing(io, canvasPos, canvasSize, zoom, pan, viewIndex);

            // Point Selection
            if (AcousticInteractionManager.InteractionMode == ViewerInteractionMode.SelectingPoint)
                HandlePointSelection(dl, io, canvasPos, canvasSize, zoom, pan, viewIndex);
        }

        // Update texture if needed
        var needsUpdate = viewIndex switch { 0 => _needsUpdateXY, 1 => _needsUpdateXZ, _ => _needsUpdateYZ };
        var texture = viewIndex switch { 0 => _textureXY, 1 => _textureXZ, _ => _textureYZ };
        if (needsUpdate || texture == null || !texture.IsValid)
        {
            _ = UpdateTextureAsync(viewIndex);
            switch (viewIndex)
            {
                case 0: _needsUpdateXY = false; break;
                case 1: _needsUpdateXZ = false; break;
                case 2: _needsUpdateYZ = false; break;
            }
        }

        dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);

        // Re-fetch texture for drawing as the async task may have updated it
        texture = viewIndex switch { 0 => _textureXY, 1 => _textureXZ, _ => _textureYZ };

        if (texture != null && texture.IsValid)
        {
            var (width, height) = GetImageDimensionsForView(viewIndex);
            var (imagePos, imageSize) = GetImageDisplayMetrics(
                canvasPos, canvasSize, zoom, pan, width, height, viewIndex);

            dl.PushClipRect(canvasPos, canvasPos + canvasSize, true);
            dl.AddImage(texture.GetImGuiTextureId(), imagePos, imagePos + imageSize);
            if (_showGrid) DrawGrid(dl, imagePos, imageSize, 10);
            dl.PopClipRect();

            // Draw line in progress on top of the image
            if (_isDrawingLine && AcousticInteractionManager.LineViewIndex == viewIndex)
                dl.AddLine(_lineStartPos, io.MousePos, ImGui.GetColorU32(new Vector4(1, 1, 0, 0.8f)), 2.0f);
        }
    }

    /// <summary>
    ///     Handles user interaction for probing data at a specific point in a slice.
    /// </summary>
    private void HandlePointProbing(ImGuiIOPtr io, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan,
        int viewIndex)
    {
        var (hasVolume, volWidth, volHeight, volDepth) = GetCurrentVolumeDimensions();
        if (!hasVolume) return;

        var (width, height) = GetImageDimensionsForView(viewIndex);
        var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height, viewIndex);
        var mouseImgCoord = (io.MousePos - imagePos) / imageSize;

        if (mouseImgCoord.X >= 0 && mouseImgCoord.X <= 1 && mouseImgCoord.Y >= 0 && mouseImgCoord.Y <= 1)
        {
            var x = (int)(mouseImgCoord.X * width);
            var y = (int)(mouseImgCoord.Y * height);

            // Convert 2D view coordinates to 3D volume coordinates
            var (volX, volY, volZ) = viewIndex switch
            {
                0 => (x, y, _sliceZ),
                1 => (x, _sliceY, y),
                _ => (_sliceX, x, y)
            };

            // Ensure density data is available
            if (_dataset.DensityData == null)
                // Load density data if not loaded
                _ = LoadDensityDataAsync();

            volX = Math.Clamp(volX, 0, volWidth - 1);
            volY = Math.Clamp(volY, 0, volHeight - 1);
            volZ = Math.Clamp(volZ, 0, volDepth - 1);

            var tooltip = $"Voxel: ({volX}, {volY}, {volZ})\n";

            if (_dataset.DensityData != null)
                tooltip += $"Density: {_dataset.DensityData.GetDensity(volX, volY, volZ):F0} kg/m³\n" +
                           $"Vp: {_dataset.DensityData.GetPWaveVelocity(volX, volY, volZ):F0} m/s\n" +
                           $"Vs: {_dataset.DensityData.GetSWaveVelocity(volX, volY, volZ):F0} m/s";
            else
                tooltip += "Density data not calibrated.";

            if (_dataset.DamageField != null)
                tooltip += $"\nDamage: {_dataset.DamageField[volX, volY, volZ] / 255.0f:P1}";

            ImGui.SetTooltip(tooltip);
        }
    }

    /// <summary>
    ///     Asynchronously loads density data if needed.
    /// </summary>
    private async Task LoadDensityDataAsync()
    {
        if (_dataset.DensityData != null) return;

        await Task.Run(() =>
        {
            var densityPath = Path.Combine(_dataset.FilePath, "Density.bin");
            var youngsPath = Path.Combine(_dataset.FilePath, "YoungsModulus.bin");
            var poissonPath = Path.Combine(_dataset.FilePath, "PoissonRatio.bin");

            if (File.Exists(densityPath) &&
                File.Exists(youngsPath) &&
                File.Exists(poissonPath))
                try
                {
                    var density = LoadRawFloatField(densityPath);
                    var youngs = LoadRawFloatField(youngsPath);
                    var poisson = LoadRawFloatField(poissonPath);

                    if (density != null && youngs != null && poisson != null)
                    {
                        _dataset.DensityData = new DensityVolume(density, youngs, poisson);
                        Logger.Log("[AcousticVolumeViewer] Loaded calibrated material properties.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[AcousticVolumeViewer] Failed to load material properties: {ex.Message}");
                }
        });
    }

    private float[,,] LoadRawFloatField(string path)
    {
        using (var reader = new BinaryReader(File.OpenRead(path)))
        {
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            var depth = reader.ReadInt32();

            var field = new float[width, height, depth];
            var buffer = reader.ReadBytes(field.Length * sizeof(float));
            Buffer.BlockCopy(buffer, 0, field, 0, buffer.Length);
            return field;
        }
    }

    /// <summary>
    ///     Handles the UI logic for drawing a line for analysis.
    /// </summary>
    private void HandleLineDrawing(ImGuiIOPtr io, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan,
        int viewIndex)
    {
        ImGui.SetTooltip(
            "Click and drag to define a line for analysis.\nPress ESC or 'Cancel' in Analysis tool to exit.");

        var (width, height) = GetImageDimensionsForView(viewIndex);

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

            var (imagePos, imageSize) =
                GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height, viewIndex);
            var startPixel = (_lineStartPos - imagePos) / imageSize * new Vector2(width, height);
            var endPixel = (io.MousePos - imagePos) / imageSize * new Vector2(width, height);

            var sliceIndex = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 };

            AcousticInteractionManager.FinalizeLine(sliceIndex, viewIndex, startPixel, endPixel);
        }
    }

    /// <summary>
    ///     Handles the UI logic for selecting a single point for analysis.
    /// </summary>
    private void HandlePointSelection(ImDrawListPtr dl, ImGuiIOPtr io, Vector2 canvasPos, Vector2 canvasSize,
        float zoom, Vector2 pan, int viewIndex)
    {
        ImGui.SetTooltip("Click to select a point for analysis.");
        dl.AddCircle(io.MousePos, 5, ImGui.GetColorU32(new Vector4(1, 0, 1, 0.8f)), 12, 2.0f);

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var (width, height) = GetImageDimensionsForView(viewIndex);
            var (imagePos, imageSize) =
                GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height, viewIndex);
            var mouseImgCoord = (io.MousePos - imagePos) / imageSize;

            if (mouseImgCoord.X >= 0 && mouseImgCoord.X <= 1 && mouseImgCoord.Y >= 0 && mouseImgCoord.Y <= 1)
            {
                var x = (int)(mouseImgCoord.X * width);
                var y = (int)(mouseImgCoord.Y * height);

                var (volX, volY, volZ) = viewIndex switch
                {
                    0 => (x, y, _sliceZ),
                    1 => (x, _sliceY, y),
                    _ => (_sliceX, x, y)
                };

                var (hasVolume, w, h, d) = GetCurrentVolumeDimensions();
                if (hasVolume)
                {
                    var finalPoint = new Vector3(
                        Math.Clamp(volX, 0, w - 1),
                        Math.Clamp(volY, 0, h - 1),
                        Math.Clamp(volZ, 0, d - 1)
                    );
                    AcousticInteractionManager.FinalizePoint(finalPoint);
                }
            }
        }
    }

    /// <summary>
    ///     Asynchronously prepares slice texture data and queues the final GPU operations for the main thread.
    /// </summary>
    private async Task UpdateTextureAsync(int viewIndex)
    {
        try
        {
            var (width, height) = GetImageDimensionsForView(viewIndex);
            var cacheKey =
                $"{_currentMode}_{viewIndex}_{GetSliceIndexForView(viewIndex)}_{_contrastMin:F3}_{_contrastMax:F3}";
            if (_currentMode == VisualizationMode.TimeSeries)
                cacheKey += $"_frame{_currentFrameIndex}_comp{_timeSeriesComponent}";

            var sliceData = _sliceCache.GetOrLoad(cacheKey, () =>
            {
                return _currentMode == VisualizationMode.TimeSeries
                    ? ExtractSliceDataFromSnapshot(viewIndex)
                    : ExtractSliceDataFromVolume(viewIndex, width, height);
            });

            var rgbaData = await Task.Run(() => ApplyColorMap(sliceData, width, height));

            _mainThreadActions.Enqueue(() =>
            {
                try
                {
                    var newTexture = TextureManager.CreateFromPixelData(rgbaData, (uint)width, (uint)height);

                    TextureManager oldTexture = null;
                    switch (viewIndex)
                    {
                        case 0:
                            oldTexture = _textureXY;
                            _textureXY = newTexture;
                            break;
                        case 1:
                            oldTexture = _textureXZ;
                            _textureXZ = newTexture;
                            break;
                        case 2:
                            oldTexture = _textureYZ;
                            _textureYZ = newTexture;
                            break;
                    }

                    oldTexture?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        $"[AcousticVolumeViewer] Failed to create/swap texture on main thread: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AcousticVolumeViewer] Error preparing texture update: {ex.Message}");
        }
    }

    private int GetSliceIndexForView(int viewIndex)
    {
        return viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 };
    }

    private byte[] ExtractSliceDataFromVolume(int viewIndex, int width, int height)
    {
        var volume = GetCurrentVolume();
        if (volume == null) return null;

        var data = new byte[width * height];

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

        ApplyContrast(data);
        return data;
    }

    private byte[] ExtractSliceDataFromSnapshot(int viewIndex)
    {
        if (_dataset.TimeSeriesSnapshots == null || _dataset.TimeSeriesSnapshots.Count <= _currentFrameIndex)
            return null;

        var snapshot = _dataset.TimeSeriesSnapshots[_currentFrameIndex];
        var (width, height) = GetImageDimensionsForView(viewIndex);
        var slice = new byte[width * height];

        // Magnitude component requires expensive calculation
        if (_timeSeriesComponent == 0) // Magnitude
        {
            var vxf = snapshot.GetVelocityField(0);
            var vyf = snapshot.GetVelocityField(1);
            var vzf = snapshot.GetVelocityField(2);
            if (vxf == null || vyf == null || vzf == null) return null;

            var magnitudeSlice = new float[width * height];
            float maxMag = 0;

            // Calculate magnitude for the slice and find max
            for (var j = 0; j < height; j++)
            for (var i = 0; i < width; i++)
            {
                var (x, y, z) = Get3DCoords(viewIndex, i, j);
                var mag = MathF.Sqrt(vxf[x, y, z] * vxf[x, y, z] + vyf[x, y, z] * vyf[x, y, z] +
                                     vzf[x, y, z] * vzf[x, y, z]);
                magnitudeSlice[j * width + i] = mag;
                if (mag > maxMag) maxMag = mag;
            }

            // Normalize and convert to byte slice
            if (maxMag > 0)
                for (var i = 0; i < magnitudeSlice.Length; i++)
                    slice[i] = (byte)(Math.Clamp(magnitudeSlice[i] / maxMag, 0, 1) * 255);

            return slice;
        }

        // Optimized path for single components (Vx, Vy, Vz)
        var compressedData = snapshot.GetCompressedVelocityField(_timeSeriesComponent - 1);
        if (compressedData == null) return null;

        int volW = snapshot.Width, volH = snapshot.Height, volD = snapshot.Depth;

        for (var j = 0; j < height; j++)
        for (var i = 0; i < width; i++)
        {
            var (x, y, z) = Get3DCoords(viewIndex, i, j);
            var sourceIndex = z * volW * volH + y * volW + x;
            slice[j * width + i] = compressedData[sourceIndex];
        }

        return slice;
    }

    private (int x, int y, int z) Get3DCoords(int viewIndex, int i, int j)
    {
        return viewIndex switch
        {
            0 => (i, j, _sliceZ), // XY view
            1 => (i, _sliceY, j), // XZ view
            _ => (_sliceX, i, j) // YZ view
        };
    }

    private void ApplyContrast(byte[] data)
    {
        var min = _contrastMin * 255;
        var max = _contrastMax * 255;
        var range = Math.Max(1e-5f, max - min);

        for (var i = 0; i < data.Length; i++) data[i] = (byte)Math.Clamp((data[i] - min) / range * 255, 0, 255);
    }

    private byte[] ApplyColorMap(byte[] data, int width, int height)
    {
        if (data == null) return new byte[width * height * 4];

        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            var color = GetColorFromMap(data[i] / 255f);
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

        if (_currentMode == VisualizationMode.DamageField) return GetHotColor(value);
        if (_currentMode == VisualizationMode.TimeSeries) return GetSeismicColor(value);

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

    private void DrawInfoPanel()
    {
        ImGui.Text("Acoustic Simulation Results");
        ImGui.Separator();

        if (_showInfo && _dataset != null)
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
        if (ImGui.DragFloatRange2("Contrast", ref _contrastMin, ref _contrastMax, 0.01f, 0.0f, 1.0f))
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;

        ImGui.Spacing();
        ImGui.Text("Layout:");
        if (ImGui.RadioButton("Horizontal", _layout == Layout.Horizontal)) _layout = Layout.Horizontal;
        ImGui.SameLine();
        if (ImGui.RadioButton("Vertical", _layout == Layout.Vertical)) _layout = Layout.Vertical;
        ImGui.SameLine();
        if (ImGui.RadioButton("2x2 Grid", _layout == Layout.Grid2x2)) _layout = Layout.Grid2x2;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Cache Status:");
        ImGui.Text($"Cached Slices: {_sliceCache.Count}/{SliceCache.MAX_CACHE_SIZE}");
        if (ImGui.Button("Clear Cache"))
        {
            _sliceCache.Clear();
            Logger.Log("[AcousticVolumeViewer] Slice cache cleared");
        }
    }

    private void DrawHistogramPanel(Vector2 size)
    {
        ImGui.BeginChild("HistogramView", size, ImGuiChildFlags.Border);
        ImGui.Text("Slice Histogram (XY View)");
        ImGui.Separator();

        var (hasVolume, width, height, depth) = GetCurrentVolumeDimensions();
        if (!hasVolume)
        {
            ImGui.TextDisabled("No data available.");
            ImGui.EndChild();
            return;
        }

        // Check if histogram needs recalculation (slice index or mode changed)
        if (_lastHistogramSliceZ != _sliceZ || _lastHistogramMode != _currentMode || _histogramDataXY == null)
        {
            var (imgWidth, imgHeight) = GetImageDimensionsForView(0);
            var cacheKey = $"{_currentMode}_0_{_sliceZ}_{_contrastMin:F3}_{_contrastMax:F3}";
            var sliceData = _sliceCache.GetOrLoad(cacheKey, () => ExtractSliceDataFromVolume(0, imgWidth, imgHeight));

            var bins = new int[256];
            if (sliceData != null)
                for (var i = 0; i < sliceData.Length; i++)
                    bins[sliceData[i]]++;

            _histogramDataXY = bins.Select(b => (float)b).ToArray();
            _lastHistogramSliceZ = _sliceZ;
            _lastHistogramMode = _currentMode;
        }

        if (_histogramDataXY != null && _histogramDataXY.Length > 0)
            ImGui.PlotHistogram("##slice_histo", ref _histogramDataXY[0], _histogramDataXY.Length, 0,
                null, 0, _histogramDataXY.Max(),
                new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y));

        ImGui.EndChild();
    }

    private void DrawLegend()
    {
        ImGui.SetNextWindowPos(
            new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - 160, ImGui.GetWindowPos().Y + 50),
            ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(150, 180), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Legend", ref _showLegend, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var colorMapName = _colorMapNames[_colorMapIndex];
            if (_currentMode == VisualizationMode.DamageField) colorMapName = "Hot";
            if (_currentMode == VisualizationMode.TimeSeries) colorMapName = "Seismic";

            ImGui.Text($"Color Map: {colorMapName}");
            ImGui.Separator();

            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float width = 30, height = 100;

            for (var i = 0; i < height; i++)
            {
                var color = GetColorFromMap(1.0f - i / height);
                drawList.AddRectFilled(new Vector2(pos.X, pos.Y + i), new Vector2(pos.X + width, pos.Y + i + 1),
                    ImGui.GetColorU32(color));
            }

            var highLabel = "High";
            var lowLabel = "Low";
            if (_currentMode == VisualizationMode.TimeSeries)
            {
                highLabel = "Positive";
                lowLabel = "Negative";
                ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height / 2 - 10));
                ImGui.Text("Zero");
            }

            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y - 5));
            ImGui.Text(highLabel);
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height - 15));
            ImGui.Text(lowLabel);
        }

        ImGui.End();
    }

    private void DrawGrid(ImDrawListPtr dl, Vector2 pos, Vector2 size, int divisions)
    {
        uint color = 0x40FFFFFF;
        for (var i = 1; i < divisions; i++)
        {
            var x = pos.X + size.X / divisions * i;
            var y = pos.Y + size.Y / divisions * i;
            dl.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + size.Y), color, 0.5f);
            dl.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + size.X, y), color, 0.5f);
        }
    }

    private ChunkedVolume GetCurrentVolume()
    {
        // This method is now only for static volume modes.
        return _currentMode switch
        {
            VisualizationMode.PWaveField => _dataset.PWaveField,
            VisualizationMode.SWaveField => _dataset.SWaveField,
            VisualizationMode.CombinedField => _dataset.CombinedWaveField,
            VisualizationMode.DamageField => _dataset.DamageField,
            _ => _dataset.CombinedWaveField // Default fallback
        };
    }

    private (bool hasVolume, int width, int height, int depth) GetCurrentVolumeDimensions()
    {
        if (_currentMode == VisualizationMode.TimeSeries)
        {
            if (_dataset.TimeSeriesSnapshots?.Count > 0)
            {
                var snapshot = _dataset.TimeSeriesSnapshots[_currentFrameIndex];
                return (true, snapshot.Width, snapshot.Height, snapshot.Depth);
            }

            return (false, 0, 0, 0);
        }

        var volume = GetCurrentVolume();
        if (volume != null) return (true, volume.Width, volume.Height, volume.Depth);
        return (false, 0, 0, 0);
    }

    private (int width, int height) GetImageDimensionsForView(int viewIndex)
    {
        var (hasVolume, volWidth, volHeight, volDepth) = GetCurrentVolumeDimensions();
        if (!hasVolume) return (1, 1);

        return viewIndex switch
        {
            0 => (volWidth, volHeight),
            1 => (volWidth, volDepth),
            _ => (volHeight, volDepth)
        };
    }

    private (Vector2 pos, Vector2 size) GetImageDisplayMetrics(Vector2 canvasPos, Vector2 canvasSize, float zoom,
        Vector2 pan, int imageWidth, int imageHeight, int viewIndex)
    {
        // This method assumes the dataset has properties to correct for anisotropic voxels.
        // The AcousticVolumeDataset uses a single isotropic 'VoxelSize'.
        float pixelWidth, pixelHeight;

        switch (viewIndex)
        {
            case 0: // XY View
                pixelWidth = (float)_dataset.VoxelSize;
                pixelHeight = (float)_dataset.VoxelSize;
                break;
            case 1: // XZ View
            case 2: // YZ View
                pixelWidth = (float)_dataset.VoxelSize;
                // For an isotropic volume, the "slice thickness" is the same as the pixel size.
                pixelHeight = (float)_dataset.VoxelSize;
                break;
            default:
                pixelWidth = 1.0f;
                pixelHeight = 1.0f;
                break;
        }

        // Handle case where properties might be zero or invalid
        if (pixelHeight <= 0) pixelHeight = pixelWidth;
        if (pixelWidth <= 0) pixelWidth = 1.0f;

        // Calculate the physical aspect ratio of the slice
        var imageAspect = imageWidth * pixelWidth / (imageHeight * pixelHeight);
        var canvasAspect = canvasSize.X / canvasSize.Y;

        // Fit the image within the canvas while preserving its aspect ratio
        var imageDisplaySize = imageAspect > canvasAspect
            ? new Vector2(canvasSize.X, canvasSize.X / imageAspect) // Letterboxed
            : new Vector2(canvasSize.Y * imageAspect, canvasSize.Y); // Pillarboxed

        imageDisplaySize *= zoom;
        var imageDisplayPos = canvasPos + (canvasSize - imageDisplaySize) * 0.5f + pan;
        return (imageDisplayPos, imageDisplaySize);
    }

    private void ResetViews()
    {
        var (hasVolume, width, height, depth) = GetCurrentVolumeDimensions();
        if (hasVolume)
        {
            _sliceX = width / 2;
            _sliceY = height / 2;
            _sliceZ = depth / 2;
        }

        _zoomXY = _zoomXZ = _zoomYZ = 1.0f;
        _panXY = _panXZ = _panYZ = Vector2.Zero;
        _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        _contrastMin = 0.0f;
        _contrastMax = 1.0f;
    }

    // Visualization mode
    private enum VisualizationMode
    {
        PWaveField,
        SWaveField,
        CombinedField,
        DamageField,
        TimeSeries
    }

    // Layout
    private enum Layout
    {
        Horizontal,
        Vertical,
        Grid2x2
    }

    #region Color Map Functions

    private Vector4 GetJetColor(float v)
    {
        v = Math.Clamp(v, 0.0f, 1.0f);
        if (v < 0.125f) return new Vector4(0, 0, 0.5f + 4 * v, 1);
        if (v < 0.375f) return new Vector4(0, 4 * (v - 0.125f), 1, 1);
        if (v < 0.625f) return new Vector4(4 * (v - 0.375f), 1, 1 - 4 * (v - 0.375f), 1);
        if (v < 0.875f) return new Vector4(1, 1 - 4 * (v - 0.625f), 0, 1);
        return new Vector4(1 - 4 * (v - 0.875f), 0, 0, 1);
    }

    private Vector4 GetViridisColor(float t)
    {
        var r = 0.26700f + t * (2.05282f - t * (29.25595f - t * (127.35689f - t * (214.53428f - t * 128.38883f))));
        var g = 0.00497f + t * (1.10748f + t * (4.29528f - t * (4.93638f + t * (-7.42203f + t * 4.02493f))));
        var b = 0.32942f + t * (0.45984f - t * (5.58064f + t * (27.20658f - t * (50.11327f + t * 28.18927f))));
        return new Vector4(Math.Clamp(r, 0.0f, 1.0f), Math.Clamp(g, 0.0f, 1.0f), Math.Clamp(b, 0.0f, 1.0f), 1.0f);
    }

    private Vector4 GetHotColor(float v)
    {
        v = Math.Clamp(v, 0.0f, 1.0f);
        var r = Math.Clamp(v / 0.4f, 0.0f, 1.0f);
        var g = Math.Clamp((v - 0.4f) / 0.4f, 0.0f, 1.0f);
        var b = Math.Clamp((v - 0.8f) / 0.2f, 0.0f, 1.0f);
        return new Vector4(r, g, b, 1.0f);
    }

    private Vector4 GetCoolColor(float v)
    {
        return new Vector4(v, 1 - v, 1, 1);
    }

    private Vector4 GetSeismicColor(float v)
    {
        // Blue -> White -> Red (bipolar map for signed data)
        v = Math.Clamp(v, 0.0f, 1.0f);
        if (v < 0.5f)
        {
            var t = v * 2.0f; // 0 -> 1
            return new Vector4(t, t, 1.0f, 1.0f); // Blue to White
        }
        else
        {
            var t = (v - 0.5f) * 2.0f; // 0 -> 1
            return new Vector4(1.0f, 1.0f - t, 1.0f - t, 1.0f); // White to Red
        }
    }

    #endregion
}

/// <summary>
///     Cache for slice data to avoid re-extracting from volumes.
/// </summary>
internal class SliceCache
{
    public const int MAX_CACHE_SIZE = 64; // Cache up to 64 slices
    private readonly Dictionary<string, byte[]> _cache = new();
    private readonly LinkedList<string> _lru = new();

    public int Count => _cache.Count;

    public byte[] GetOrLoad(string key, Func<byte[]> loader)
    {
        if (_cache.TryGetValue(key, out var data))
        {
            _lru.Remove(key);
            _lru.AddFirst(key);
            return data;
        }

        data = loader();
        if (data == null) return null;

        _cache[key] = data;
        _lru.AddFirst(key);

        while (_lru.Count > MAX_CACHE_SIZE)
        {
            var oldest = _lru.Last.Value;
            _lru.RemoveLast();
            _cache.Remove(oldest);
        }

        return data;
    }

    public void Clear()
    {
        _cache.Clear();
        _lru.Clear();
    }
}

/// <summary>
///     Adapter to make LazyChunkedVolume compatible with ChunkedVolume interface.
/// </summary>
internal class LazyChunkedVolumeAdapter : ChunkedVolume
{
    private readonly LazyChunkedVolume _lazyVolume;

    public LazyChunkedVolumeAdapter(LazyChunkedVolume lazyVolume)
        : base(lazyVolume.Width, lazyVolume.Height, lazyVolume.Depth)
    {
        _lazyVolume = lazyVolume;
        PixelSize = lazyVolume.PixelSize;
    }

    public new byte this[int x, int y, int z]
    {
        get => _lazyVolume[x, y, z];
        set => throw new NotSupportedException("Lazy volumes are read-only");
    }

    public new void ReadSliceZ(int z, byte[] buffer)
    {
        _lazyVolume.ReadSliceZ(z, buffer);
    }

    public new void WriteSliceZ(int z, byte[] data)
    {
        throw new NotSupportedException("Lazy volumes are read-only");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _lazyVolume?.Dispose();
        base.Dispose(disposing);
    }
}