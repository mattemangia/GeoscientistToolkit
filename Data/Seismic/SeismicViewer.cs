// GeoscientistToolkit/Data/Seismic/SeismicViewer.cs

using System.Numerics;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Seismic;

/// <summary>
/// Viewer for seismic datasets with wiggle trace, variable area, and color map display
/// </summary>
public class SeismicViewer : IDatasetViewer
{
    private readonly SeismicDataset _dataset;
    private readonly string[] _colorMapNames = { "Grayscale", "Seismic", "Viridis", "Jet", "Hot", "Cool" };
    private readonly ImGuiExportFileDialog _exportDialog;

    // Rendering state
    private TextureManager? _seismicTexture;
    private bool _needsRedraw = true;
    private int _renderedImageWidth;
    private int _renderedImageHeight;
    private int _originalDataWidth;  // Original trace count before downsampling
    private int _originalDataHeight; // Original sample count before downsampling

    // Display settings
    private float _amplitudeScale = 1.0f;
    private float _contrastMin = -1.0f;
    private float _contrastMax = 1.0f;
    private bool _autoContrast = true;
    private int _traceDisplayWidth = 2; // pixels per trace
    private bool _showPackageOverlays = true;
    private bool _showGrid = true;
    private bool _showTimescale = true;
    private bool _showTraceNumbers = true;

    // Interaction state
    private bool _isSelecting = false;
    private int _selectionStartTrace = -1;
    private int _selectionEndTrace = -1;
    private Vector2 _lastMousePos;

    // Export state
    private bool _showExportDialog = false;

    public SeismicViewer(SeismicDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _exportDialog = new ImGuiExportFileDialog("SeismicExport", "Export Seismic Image");
        _exportDialog.SetExtensions((".png", "PNG Image"), (".jpg", "JPEG Image"), (".tiff", "TIFF Image"));

        Logger.Log($"[SeismicViewer] Created viewer for: {_dataset.Name}");
    }

    public void DrawToolbarControls()
    {
        if (_dataset.SegyData == null)
        {
            ImGui.TextDisabled("No seismic data loaded");
            return;
        }

        // Display mode toggles
        ImGui.Text("Display:");
        ImGui.SameLine();

        bool showWiggle = _dataset.ShowWiggleTrace;
        if (ImGui.Checkbox("Wiggle", ref showWiggle))
        {
            _dataset.ShowWiggleTrace = showWiggle;
            _needsRedraw = true;
        }

        ImGui.SameLine();
        bool showVariableArea = _dataset.ShowVariableArea;
        if (ImGui.Checkbox("Variable Area", ref showVariableArea))
        {
            _dataset.ShowVariableArea = showVariableArea;
            _needsRedraw = true;
        }

        ImGui.SameLine();
        bool showColorMap = _dataset.ShowColorMap;
        if (ImGui.Checkbox("Color Map", ref showColorMap))
        {
            _dataset.ShowColorMap = showColorMap;
            _needsRedraw = true;
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Color map selection
        if (_dataset.ShowColorMap)
        {
            ImGui.Text("ColorMap:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            int colorMapIndex = _dataset.ColorMapIndex;
            if (ImGui.Combo("##ColorMap", ref colorMapIndex, _colorMapNames, _colorMapNames.Length))
            {
                _dataset.ColorMapIndex = colorMapIndex;
                _needsRedraw = true;
            }

            ImGui.SameLine();
        }

        // Gain control
        ImGui.Text("Gain:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        float gainValue = _dataset.GainValue;
        if (ImGui.SliderFloat("##Gain", ref gainValue, 0.1f, 10.0f, "%.1f"))
        {
            _dataset.GainValue = gainValue;
            _needsRedraw = true;
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Overlay toggles
        if (ImGui.Checkbox("Packages", ref _showPackageOverlays))
            _needsRedraw = true;

        ImGui.SameLine();
        if (ImGui.Checkbox("Grid", ref _showGrid))
            _needsRedraw = true;

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Export button
        if (ImGui.Button("Export Image"))
        {
            _showExportDialog = true;
            _exportDialog.Open();
        }

        ImGui.SameLine();
        ImGui.Text($"| Traces: {_dataset.GetTraceCount()} | Samples: {_dataset.GetSampleCount()} | Duration: {_dataset.GetDurationSeconds():F2}s");

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Zoom controls
        ImGui.Text("Zoom:");
        ImGui.SameLine();
        if (ImGui.Button("-##ZoomOut"))
            _pendingZoomDelta = -0.2f;
        ImGui.SameLine();
        if (ImGui.Button("+##ZoomIn"))
            _pendingZoomDelta = 0.2f;
        ImGui.SameLine();
        if (ImGui.Button("Fit"))
            _fitToWindow = true;
        ImGui.SameLine();
        if (ImGui.Button("1:1"))
            _resetZoom = true;
    }

    // Pending zoom/pan actions from toolbar
    private float _pendingZoomDelta = 0;
    private bool _fitToWindow = false;
    private bool _resetZoom = false;

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        if (_dataset.SegyData == null || _dataset.SegyData.Traces.Count == 0)
        {
            ImGui.TextDisabled("No seismic data to display");
            return;
        }

        // Handle export dialog
        if (_showExportDialog)
        {
            bool selectionMade = _exportDialog.Submit();

            if (selectionMade)
            {
                ExportImage(_exportDialog.SelectedPath);
                _showExportDialog = false;
            }

            if (!_exportDialog.IsOpen)
            {
                _showExportDialog = false;
            }
        }

        // Wrap in a child window to capture mouse wheel and prevent parent scrolling
        var availableRegion = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("SeismicViewerCanvas", availableRegion, ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        availableRegion = ImGui.GetContentRegionAvail();
        var cursorPos = ImGui.GetCursorScreenPos();
        var io = ImGui.GetIO();

        // Render seismic section
        if (_needsRedraw || _seismicTexture == null)
        {
            RenderSeismicSection((int)availableRegion.X, (int)availableRegion.Y);
            _needsRedraw = false;
        }

        if (_seismicTexture != null && _seismicTexture.IsValid)
        {
            var imageSize = new Vector2(_renderedImageWidth, _renderedImageHeight);

            // Handle toolbar zoom actions
            if (_fitToWindow)
            {
                _fitToWindow = false;
                // Calculate zoom to fit the entire image in the available region
                var fitZoomX = availableRegion.X / imageSize.X;
                var fitZoomY = availableRegion.Y / imageSize.Y;
                zoom = Math.Min(fitZoomX, fitZoomY) * 0.95f; // 95% to leave some margin
                // Center the image
                var scaledSize = imageSize * zoom;
                pan = (availableRegion - scaledSize) * 0.5f;
            }

            if (_resetZoom)
            {
                _resetZoom = false;
                zoom = 1.0f;
                // Center the image
                var scaledSize = imageSize * zoom;
                pan = (availableRegion - scaledSize) * 0.5f;
            }

            if (_pendingZoomDelta != 0)
            {
                var oldZoom = zoom;
                zoom = Math.Clamp(zoom + _pendingZoomDelta * zoom, 0.01f, 50.0f);
                // Zoom towards center of view
                var center = availableRegion * 0.5f;
                pan = center - (center - pan) * (zoom / oldZoom);
                _pendingZoomDelta = 0;
            }

            var scaledSizeFinal = imageSize * zoom;
            var drawList = ImGui.GetWindowDrawList();

            // Clip to available region
            drawList.PushClipRect(cursorPos, cursorPos + availableRegion, true);

            // Draw background
            drawList.AddRectFilled(cursorPos, cursorPos + availableRegion, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));

            // Draw the seismic image
            var textureId = _seismicTexture.GetImGuiTextureId();
            if (textureId != IntPtr.Zero)
            {
                drawList.AddImage(
                    textureId,
                    cursorPos + pan,
                    cursorPos + pan + scaledSizeFinal
                );
            }

            // Draw grid overlay if enabled
            if (_showGrid)
            {
                DrawGridOverlay(drawList, cursorPos, pan, scaledSizeFinal, availableRegion);
            }

            // Draw overlays
            if (_showPackageOverlays && _dataset.LinePackages.Count > 0)
            {
                DrawPackageOverlays(drawList, cursorPos, pan, scaledSizeFinal, imageSize);
            }

            drawList.PopClipRect();

            // Create an invisible button for the entire canvas area for interaction
            ImGui.SetCursorScreenPos(cursorPos);
            ImGui.InvisibleButton("SeismicCanvas", availableRegion);
            var isHovered = ImGui.IsItemHovered();
            var isActive = ImGui.IsItemActive();

            // Handle mouse wheel zoom (zoom towards mouse position)
            if (isHovered && io.MouseWheel != 0)
            {
                var oldZoom = zoom;
                var zoomDelta = io.MouseWheel * 0.1f;
                var newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.01f, 50.0f);

                if (newZoom != oldZoom)
                {
                    // Zoom towards mouse position
                    var mousePos = io.MousePos - cursorPos;
                    var zoomRatio = newZoom / oldZoom;
                    pan = mousePos - (mousePos - pan) * zoomRatio;
                    zoom = newZoom;
                }
            }

            // Handle middle mouse button panning
            if (isHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            {
                pan += io.MouseDelta;
            }

            // Handle right mouse button panning (alternative)
            if (isHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Right) && !ImGui.IsKeyDown(ImGuiKey.LeftShift))
            {
                pan += io.MouseDelta;
            }

            // Handle mouse interaction for selection and tooltips
            HandleMouseInteraction(cursorPos, pan, scaledSizeFinal, imageSize);

            // Draw zoom indicator
            var zoomText = $"Zoom: {zoom * 100:F0}%";
            var textSize = ImGui.CalcTextSize(zoomText);
            var textPos = cursorPos + availableRegion - textSize - new Vector2(10, 10);
            drawList.AddRectFilled(textPos - new Vector2(5, 2), textPos + textSize + new Vector2(5, 2),
                ImGui.GetColorU32(new Vector4(0, 0, 0, 0.7f)), 3.0f);
            drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), zoomText);
        }

        ImGui.EndChild();
    }

    private void DrawGridOverlay(ImDrawListPtr drawList, Vector2 cursorPos, Vector2 pan, Vector2 scaledSize, Vector2 availableRegion)
    {
        if (_originalDataWidth == 0 || _originalDataHeight == 0)
            return;

        var gridColor = ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.3f));
        var textColor = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 0.8f));

        // Calculate grid spacing based on zoom level
        var traceSpacing = Math.Max(1, (int)(_originalDataWidth / 10)); // Aim for ~10 grid lines
        var sampleSpacing = Math.Max(1, (int)(_originalDataHeight / 10));

        // Round to nice numbers
        traceSpacing = GetNiceGridSpacing(traceSpacing);
        sampleSpacing = GetNiceGridSpacing(sampleSpacing);

        var scaleX = scaledSize.X / _originalDataWidth;
        var scaleY = scaledSize.Y / _originalDataHeight;

        // Draw vertical grid lines (traces)
        for (int t = 0; t <= _originalDataWidth; t += traceSpacing)
        {
            var x = cursorPos.X + pan.X + t * scaleX;
            if (x >= cursorPos.X && x <= cursorPos.X + availableRegion.X)
            {
                drawList.AddLine(new Vector2(x, cursorPos.Y), new Vector2(x, cursorPos.Y + availableRegion.Y), gridColor);
                // Draw trace number at top
                if (t > 0)
                {
                    var label = t.ToString();
                    drawList.AddText(new Vector2(x + 2, cursorPos.Y + pan.Y + 2), textColor, label);
                }
            }
        }

        // Draw horizontal grid lines (samples/time)
        for (int s = 0; s <= _originalDataHeight; s += sampleSpacing)
        {
            var y = cursorPos.Y + pan.Y + s * scaleY;
            if (y >= cursorPos.Y && y <= cursorPos.Y + availableRegion.Y)
            {
                drawList.AddLine(new Vector2(cursorPos.X, y), new Vector2(cursorPos.X + availableRegion.X, y), gridColor);
                // Draw sample number at left
                if (s > 0)
                {
                    var label = s.ToString();
                    drawList.AddText(new Vector2(cursorPos.X + pan.X + 2, y + 2), textColor, label);
                }
            }
        }
    }

    private int GetNiceGridSpacing(int rawSpacing)
    {
        // Round to nice numbers: 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, etc.
        var magnitude = (int)Math.Pow(10, Math.Floor(Math.Log10(rawSpacing)));
        var normalized = rawSpacing / (float)magnitude;

        if (normalized < 1.5f) return magnitude;
        if (normalized < 3.5f) return 2 * magnitude;
        if (normalized < 7.5f) return 5 * magnitude;
        return 10 * magnitude;
    }

    private void RenderSeismicSection(int width, int height)
    {
        try
        {
            var numTraces = _dataset.GetTraceCount();
            var numSamples = _dataset.GetSampleCount();

            if (numTraces == 0 || numSamples == 0)
                return;

            // Create RGBA byte array
            var pixelData = new byte[numTraces * numSamples * 4];

            // Get amplitude range
            var (minAmp, maxAmp, rms) = _dataset.GetAmplitudeStatistics();
            if (_autoContrast)
            {
                _contrastMin = minAmp;
                _contrastMax = maxAmp;
            }

            var amplitudeRange = _contrastMax - _contrastMin;
            if (amplitudeRange == 0) amplitudeRange = 1.0f;

            // Render each trace
            for (int traceIdx = 0; traceIdx < numTraces; traceIdx++)
            {
                var trace = _dataset.GetTrace(traceIdx);
                if (trace == null || trace.Samples.Length == 0)
                    continue;

                for (int sampleIdx = 0; sampleIdx < Math.Min(numSamples, trace.Samples.Length); sampleIdx++)
                {
                    var amplitude = trace.Samples[sampleIdx] * _dataset.GainValue;
                    var normalized = (amplitude - _contrastMin) / amplitudeRange;
                    normalized = Math.Clamp(normalized, 0.0f, 1.0f);

                    var color = GetColorForValue(normalized, _dataset.ColorMapIndex);

                    // Set pixel in RGBA format
                    var pixelIdx = (sampleIdx * numTraces + traceIdx) * 4;
                    pixelData[pixelIdx] = color.r;
                    pixelData[pixelIdx + 1] = color.g;
                    pixelData[pixelIdx + 2] = color.b;
                    pixelData[pixelIdx + 3] = color.a;
                }
            }

            // Upload to GPU (TextureManager will auto-downsample if needed)
            if (_seismicTexture != null)
            {
                _seismicTexture.Dispose();
            }

            _seismicTexture = TextureManager.CreateFromPixelData(pixelData, (uint)numTraces, (uint)numSamples);

            // Track both original data dimensions and actual texture dimensions
            _originalDataWidth = numTraces;
            _originalDataHeight = numSamples;
            _renderedImageWidth = (int)_seismicTexture.Width;
            _renderedImageHeight = (int)_seismicTexture.Height;

            if (_renderedImageWidth != numTraces || _renderedImageHeight != numSamples)
            {
                Logger.Log($"[SeismicViewer] Rendered seismic section: {numTraces}x{numSamples} (downsampled to {_renderedImageWidth}x{_renderedImageHeight})");
            }
            else
            {
                Logger.Log($"[SeismicViewer] Rendered seismic section: {numTraces}x{numSamples}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SeismicViewer] Error rendering seismic section: {ex.Message}");
        }
    }

    private (byte r, byte g, byte b, byte a) GetColorForValue(float value, int colorMapIndex)
    {
        // Ensure value is in [0, 1]
        value = Math.Clamp(value, 0.0f, 1.0f);

        return colorMapIndex switch
        {
            0 => GetGrayscaleColor(value),
            1 => GetSeismicColor(value),
            2 => GetViridisColor(value),
            3 => GetJetColor(value),
            4 => GetHotColor(value),
            5 => GetCoolColor(value),
            _ => GetGrayscaleColor(value)
        };
    }

    private (byte r, byte g, byte b, byte a) GetGrayscaleColor(float value)
    {
        var gray = (byte)(value * 255);
        return (gray, gray, gray, 255);
    }

    private (byte r, byte g, byte b, byte a) GetSeismicColor(float value)
    {
        // Blue-White-Red colormap (common in seismic)
        if (value < 0.5f)
        {
            // Blue to white
            var t = value * 2.0f;
            var r = (byte)(t * 255);
            var g = (byte)(t * 255);
            var b = (byte)255;
            return (r, g, b, 255);
        }
        else
        {
            // White to red
            var t = (value - 0.5f) * 2.0f;
            var r = (byte)255;
            var g = (byte)((1.0f - t) * 255);
            var b = (byte)((1.0f - t) * 255);
            return (r, g, b, 255);
        }
    }

    private (byte r, byte g, byte b, byte a) GetViridisColor(float value)
    {
        // Simplified Viridis approximation
        var r = (byte)(255 * Math.Clamp(0.267 + 1.000 * value - 0.267 * value * value, 0, 1));
        var g = (byte)(255 * Math.Clamp(0.005 + 1.400 * value - 0.500 * value * value, 0, 1));
        var b = (byte)(255 * Math.Clamp(0.329 + 1.114 * value - 1.443 * value * value, 0, 1));
        return (r, g, b, 255);
    }

    private (byte r, byte g, byte b, byte a) GetJetColor(float value)
    {
        var r = (byte)(255 * Math.Clamp(1.5f - Math.Abs(4.0f * value - 3.0f), 0, 1));
        var g = (byte)(255 * Math.Clamp(1.5f - Math.Abs(4.0f * value - 2.0f), 0, 1));
        var b = (byte)(255 * Math.Clamp(1.5f - Math.Abs(4.0f * value - 1.0f), 0, 1));
        return (r, g, b, 255);
    }

    private (byte r, byte g, byte b, byte a) GetHotColor(float value)
    {
        var r = (byte)(255 * Math.Clamp(3.0f * value, 0, 1));
        var g = (byte)(255 * Math.Clamp(3.0f * value - 1.0f, 0, 1));
        var b = (byte)(255 * Math.Clamp(3.0f * value - 2.0f, 0, 1));
        return (r, g, b, 255);
    }

    private (byte r, byte g, byte b, byte a) GetCoolColor(float value)
    {
        var r = (byte)(255 * value);
        var g = (byte)(255 * (1.0f - value));
        var b = (byte)255;
        return (r, g, b, 255);
    }

    private void DrawPackageOverlays(ImDrawListPtr drawList, Vector2 cursorPos, Vector2 pan, Vector2 scaledSize, Vector2 imageSize)
    {
        // Use original data dimensions for coordinate mapping (accounting for downsampling)
        var scaleX = scaledSize.X / _originalDataWidth;
        var scaleY = scaledSize.Y / _originalDataHeight;

        foreach (var package in _dataset.LinePackages)
        {
            if (!package.IsVisible)
                continue;

            var startX = cursorPos.X + pan.X + package.StartTrace * scaleX;
            var endX = cursorPos.X + pan.X + (package.EndTrace + 1) * scaleX;
            var topY = cursorPos.Y + pan.Y;
            var bottomY = cursorPos.Y + pan.Y + scaledSize.Y;

            var color = ImGui.ColorConvertFloat4ToU32(package.Color);

            // Draw vertical lines at start and end
            drawList.AddLine(new Vector2(startX, topY), new Vector2(startX, bottomY), color, 2.0f);
            drawList.AddLine(new Vector2(endX, topY), new Vector2(endX, bottomY), color, 2.0f);

            // Draw semi-transparent overlay
            var overlayColor = ImGui.ColorConvertFloat4ToU32(new Vector4(package.Color.X, package.Color.Y, package.Color.Z, 0.1f));
            drawList.AddRectFilled(new Vector2(startX, topY), new Vector2(endX, bottomY), overlayColor);

            // Draw package name at top
            var labelPos = new Vector2(startX + 5, topY + 5);
            drawList.AddText(labelPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), package.Name);
        }

        // Draw current selection
        if (_isSelecting && _selectionStartTrace >= 0 && _selectionEndTrace >= 0)
        {
            var startTrace = Math.Min(_selectionStartTrace, _selectionEndTrace);
            var endTrace = Math.Max(_selectionStartTrace, _selectionEndTrace);

            var startX = cursorPos.X + pan.X + startTrace * scaleX;
            var endX = cursorPos.X + pan.X + (endTrace + 1) * scaleX;
            var topY = cursorPos.Y + pan.Y;
            var bottomY = cursorPos.Y + pan.Y + scaledSize.Y;

            var selectionColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 0.3f));
            drawList.AddRectFilled(new Vector2(startX, topY), new Vector2(endX, bottomY), selectionColor);

            var borderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 1));
            drawList.AddRect(new Vector2(startX, topY), new Vector2(endX, bottomY), borderColor, 0, 0, 2.0f);
        }
    }

    private void HandleMouseInteraction(Vector2 cursorPos, Vector2 pan, Vector2 scaledSize, Vector2 imageSize)
    {
        var mousePos = ImGui.GetMousePos();
        var relativePos = mousePos - cursorPos - pan;

        // Check if mouse is over the image
        if (relativePos.X >= 0 && relativePos.X < scaledSize.X &&
            relativePos.Y >= 0 && relativePos.Y < scaledSize.Y)
        {
            // Map screen position to original data coordinates (accounting for downsampling)
            var normalizedX = relativePos.X / scaledSize.X;
            var traceIndex = (int)(normalizedX * _originalDataWidth);
            traceIndex = Math.Clamp(traceIndex, 0, _originalDataWidth - 1);

            // Show tooltip with trace info
            if (ImGui.IsMouseHoveringRect(cursorPos + pan, cursorPos + pan + scaledSize))
            {
                ImGui.BeginTooltip();
                ImGui.Text($"Trace: {traceIndex}");
                var trace = _dataset.GetTrace(traceIndex);
                if (trace != null)
                {
                    var (x, y) = trace.GetScaledSourceCoordinates();
                    ImGui.Text($"Source: ({x:F2}, {y:F2})");
                    ImGui.Text($"Ensemble: {trace.EnsembleNumber}");
                }
                ImGui.EndTooltip();
            }

            // Handle selection with Shift + Click + Drag
            if (ImGui.IsKeyDown(ImGuiKey.LeftShift) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _isSelecting = true;
                _selectionStartTrace = traceIndex;
                _selectionEndTrace = traceIndex;
            }

            if (_isSelecting && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _selectionEndTrace = traceIndex;
                _needsRedraw = true;
            }

            if (_isSelecting && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _isSelecting = false;
                // Selection is complete - will be used by SeismicTools to create packages
            }
        }
    }

    public (int startTrace, int endTrace) GetCurrentSelection()
    {
        if (_selectionStartTrace < 0 || _selectionEndTrace < 0)
            return (-1, -1);

        return (Math.Min(_selectionStartTrace, _selectionEndTrace),
                Math.Max(_selectionStartTrace, _selectionEndTrace));
    }

    public void ClearSelection()
    {
        _selectionStartTrace = -1;
        _selectionEndTrace = -1;
        _needsRedraw = true;
    }

    private void ExportImage(string filePath)
    {
        try
        {
            Logger.Log($"[SeismicViewer] Exporting seismic image to: {filePath}");

            var numTraces = _dataset.GetTraceCount();
            var numSamples = _dataset.GetSampleCount();

            // Create RGBA pixel data for export
            var pixelData = new byte[numTraces * numSamples * 4];

            // Render same as display
            var (minAmp, maxAmp, rms) = _dataset.GetAmplitudeStatistics();
            var amplitudeRange = maxAmp - minAmp;
            if (amplitudeRange == 0) amplitudeRange = 1.0f;

            for (int traceIdx = 0; traceIdx < numTraces; traceIdx++)
            {
                var trace = _dataset.GetTrace(traceIdx);
                if (trace == null) continue;

                for (int sampleIdx = 0; sampleIdx < Math.Min(numSamples, trace.Samples.Length); sampleIdx++)
                {
                    var amplitude = trace.Samples[sampleIdx] * _dataset.GainValue;
                    var normalized = (amplitude - minAmp) / amplitudeRange;
                    normalized = Math.Clamp(normalized, 0.0f, 1.0f);

                    var color = GetColorForValue(normalized, _dataset.ColorMapIndex);

                    var pixelIdx = (sampleIdx * numTraces + traceIdx) * 4;
                    pixelData[pixelIdx] = color.r;
                    pixelData[pixelIdx + 1] = color.g;
                    pixelData[pixelIdx + 2] = color.b;
                    pixelData[pixelIdx + 3] = color.a;
                }
            }

            // Use StbImageWrite to save the image
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var writer = new StbImageWriteSharp.ImageWriter();

            if (extension == ".png")
            {
                using var stream = File.Create(filePath);
                writer.WritePng(pixelData, numTraces, numSamples, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
                Logger.Log($"[SeismicViewer] Successfully exported PNG to: {filePath}");
            }
            else if (extension == ".jpg" || extension == ".jpeg")
            {
                using var stream = File.Create(filePath);
                writer.WriteJpg(pixelData, numTraces, numSamples, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream, 95);
                Logger.Log($"[SeismicViewer] Successfully exported JPEG to: {filePath}");
            }
            else if (extension == ".tiff" || extension == ".tif")
            {
                // TGA as fallback since StbImageWrite doesn't support TIFF
                var tgaPath = Path.ChangeExtension(filePath, ".tga");
                using var stream = File.Create(tgaPath);
                writer.WriteTga(pixelData, numTraces, numSamples, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
                Logger.LogWarning($"[SeismicViewer] TIFF not supported by StbImageWrite, saved as TGA instead: {tgaPath}");
            }
            else
            {
                // Default to PNG
                using var stream = File.Create(filePath);
                writer.WritePng(pixelData, numTraces, numSamples, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
                Logger.Log($"[SeismicViewer] Successfully exported PNG to: {filePath}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SeismicViewer] Error exporting image: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _seismicTexture?.Dispose();
        Logger.Log($"[SeismicViewer] Disposed viewer for: {_dataset.Name}");
    }
}
