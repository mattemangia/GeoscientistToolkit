// GeoscientistToolkit/Data/Seismic/SeismicViewer.cs

using System.Numerics;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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
        _exportDialog.SetExtensions(new[] { ".png", ".jpg", ".tiff" });

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

        if (ImGui.Checkbox("Wiggle", ref _dataset.ShowWiggleTrace))
            _needsRedraw = true;

        ImGui.SameLine();
        if (ImGui.Checkbox("Variable Area", ref _dataset.ShowVariableArea))
            _needsRedraw = true;

        ImGui.SameLine();
        if (ImGui.Checkbox("Color Map", ref _dataset.ShowColorMap))
            _needsRedraw = true;

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Color map selection
        if (_dataset.ShowColorMap)
        {
            ImGui.Text("ColorMap:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("##ColorMap", ref _dataset.ColorMapIndex, _colorMapNames, _colorMapNames.Length))
                _needsRedraw = true;

            ImGui.SameLine();
        }

        // Gain control
        ImGui.Text("Gain:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.SliderFloat("##Gain", ref _dataset.GainValue, 0.1f, 10.0f, "%.1f"))
            _needsRedraw = true;

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
    }

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
            _exportDialog.Submit();

            if (_exportDialog.SubmitPressed())
            {
                ExportImage(_exportDialog.SelectedPath);
                _showExportDialog = false;
            }

            if (!_exportDialog.IsOpen)
            {
                _showExportDialog = false;
            }
        }

        var availableRegion = ImGui.GetContentRegionAvail();

        // Render seismic section
        if (_needsRedraw || _seismicTexture == null)
        {
            RenderSeismicSection((int)availableRegion.X, (int)availableRegion.Y);
            _needsRedraw = false;
        }

        if (_seismicTexture != null)
        {
            var imageSize = new Vector2(_renderedImageWidth, _renderedImageHeight);
            var scaledSize = imageSize * zoom;

            var cursorPos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();

            // Draw the seismic image
            drawList.AddImage(
                (IntPtr)_seismicTexture.TextureId,
                cursorPos + pan,
                cursorPos + pan + scaledSize
            );

            // Draw overlays
            if (_showPackageOverlays && _dataset.LinePackages.Count > 0)
            {
                DrawPackageOverlays(drawList, cursorPos, pan, scaledSize, imageSize);
            }

            // Handle mouse interaction for selection
            HandleMouseInteraction(cursorPos, pan, scaledSize, imageSize);

            // Create an invisible button for the entire image area for panning
            ImGui.SetCursorScreenPos(cursorPos);
            ImGui.InvisibleButton("SeismicCanvas", scaledSize);
        }
    }

    private void RenderSeismicSection(int width, int height)
    {
        try
        {
            var numTraces = _dataset.GetTraceCount();
            var numSamples = _dataset.GetSampleCount();

            if (numTraces == 0 || numSamples == 0)
                return;

            // Create image
            using var image = new Image<Rgba32>(numTraces, numSamples);

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
                    image[traceIdx, sampleIdx] = color;
                }
            }

            // Convert to bytes
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            var imageData = ms.ToArray();

            // Upload to GPU
            if (_seismicTexture != null)
            {
                _seismicTexture.Dispose();
            }

            _seismicTexture = TextureManager.LoadFromMemory(imageData);
            _renderedImageWidth = numTraces;
            _renderedImageHeight = numSamples;

            Logger.Log($"[SeismicViewer] Rendered seismic section: {numTraces}x{numSamples}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SeismicViewer] Error rendering seismic section: {ex.Message}");
        }
    }

    private Rgba32 GetColorForValue(float value, int colorMapIndex)
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

    private Rgba32 GetGrayscaleColor(float value)
    {
        var gray = (byte)(value * 255);
        return new Rgba32(gray, gray, gray, 255);
    }

    private Rgba32 GetSeismicColor(float value)
    {
        // Blue-White-Red colormap (common in seismic)
        if (value < 0.5f)
        {
            // Blue to white
            var t = value * 2.0f;
            var r = (byte)(t * 255);
            var g = (byte)(t * 255);
            var b = 255;
            return new Rgba32(r, g, b, 255);
        }
        else
        {
            // White to red
            var t = (value - 0.5f) * 2.0f;
            var r = 255;
            var g = (byte)((1.0f - t) * 255);
            var b = (byte)((1.0f - t) * 255);
            return new Rgba32(r, g, b, 255);
        }
    }

    private Rgba32 GetViridisColor(float value)
    {
        // Simplified Viridis approximation
        var r = (byte)(255 * Math.Clamp(0.267 + 1.000 * value - 0.267 * value * value, 0, 1));
        var g = (byte)(255 * Math.Clamp(0.005 + 1.400 * value - 0.500 * value * value, 0, 1));
        var b = (byte)(255 * Math.Clamp(0.329 + 1.114 * value - 1.443 * value * value, 0, 1));
        return new Rgba32(r, g, b, 255);
    }

    private Rgba32 GetJetColor(float value)
    {
        var r = (byte)(255 * Math.Clamp(1.5f - Math.Abs(4.0f * value - 3.0f), 0, 1));
        var g = (byte)(255 * Math.Clamp(1.5f - Math.Abs(4.0f * value - 2.0f), 0, 1));
        var b = (byte)(255 * Math.Clamp(1.5f - Math.Abs(4.0f * value - 1.0f), 0, 1));
        return new Rgba32(r, g, b, 255);
    }

    private Rgba32 GetHotColor(float value)
    {
        var r = (byte)(255 * Math.Clamp(3.0f * value, 0, 1));
        var g = (byte)(255 * Math.Clamp(3.0f * value - 1.0f, 0, 1));
        var b = (byte)(255 * Math.Clamp(3.0f * value - 2.0f, 0, 1));
        return new Rgba32(r, g, b, 255);
    }

    private Rgba32 GetCoolColor(float value)
    {
        var r = (byte)(255 * value);
        var g = (byte)(255 * (1.0f - value));
        var b = 255;
        return new Rgba32(r, g, b, 255);
    }

    private void DrawPackageOverlays(ImDrawListPtr drawList, Vector2 cursorPos, Vector2 pan, Vector2 scaledSize, Vector2 imageSize)
    {
        var scaleX = scaledSize.X / imageSize.X;
        var scaleY = scaledSize.Y / imageSize.Y;

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
            var scaleX = imageSize.X / scaledSize.X;
            var traceIndex = (int)(relativePos.X * scaleX);

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

            // Create high-resolution export image
            using var image = new Image<Rgba32>(numTraces, numSamples);

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
                    image[traceIdx, sampleIdx] = color;
                }
            }

            // Save based on extension
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".png")
                image.SaveAsPng(filePath);
            else if (extension == ".jpg" || extension == ".jpeg")
                image.SaveAsJpeg(filePath);
            else if (extension == ".tiff" || extension == ".tif")
                image.SaveAsTiff(filePath);
            else
                image.SaveAsPng(filePath);

            Logger.Log($"[SeismicViewer] Successfully exported image to: {filePath}");
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
