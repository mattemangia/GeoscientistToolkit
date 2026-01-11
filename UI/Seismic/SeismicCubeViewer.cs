// GeoscientistToolkit/UI/Seismic/SeismicCubeViewer.cs

using System.Numerics;
using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Seismic;

/// <summary>
/// Viewer for 3D seismic cubes - displays time slices, inline sections, and crossline sections
/// </summary>
public class SeismicCubeViewer : IDatasetViewer
{
    private SeismicCubeDataset? _dataset;
    private bool _needsRedraw = true;

    // View modes
    private CubeViewMode _viewMode = CubeViewMode.TimeSlice;
    private int _currentInline = 0;
    private int _currentCrossline = 0;
    private float _currentTimeMs = 0;

    // Display settings
    private float _gain = 1.0f;
    private int _colorMapIndex = 1; // Seismic colormap
    private bool _showGrid = true;
    private bool _showLineOverlay = true;
    private bool _showIntersections = true;
    private bool _showPackages = true;

    // Line display
    private string? _selectedLineId;

    // Image cache
    private byte[]? _cachedSliceImage;
    private int _cachedSliceWidth;
    private int _cachedSliceHeight;
    private uint _textureId;

    // Colormap presets
    private static readonly string[] _colorMapNames = { "Grayscale", "Seismic", "Viridis", "Jet", "Hot", "Cool" };

    public void SetDataset(SeismicCubeDataset dataset)
    {
        _dataset = dataset;
        _needsRedraw = true;

        // Initialize view parameters
        if (_dataset != null)
        {
            _currentInline = _dataset.GridParameters.InlineCount / 2;
            _currentCrossline = _dataset.GridParameters.CrosslineCount / 2;
            _currentTimeMs = _dataset.Bounds.MaxZ / 2;
        }
    }

    public void RequestRedraw()
    {
        _needsRedraw = true;
    }

    public void DrawToolbarControls()
    {
        if (_dataset == null) return;

        // View mode selector
        ImGui.Text("View:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        var modeNames = Enum.GetNames<CubeViewMode>();
        int modeIndex = (int)_viewMode;
        if (ImGui.Combo("##ViewMode", ref modeIndex, modeNames, modeNames.Length))
        {
            _viewMode = (CubeViewMode)modeIndex;
            _needsRedraw = true;
        }

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        // Slice position
        switch (_viewMode)
        {
            case CubeViewMode.TimeSlice:
                ImGui.Text("Time:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                float maxTime = _dataset.Bounds.MaxZ;
                if (ImGui.SliderFloat("##TimeSlice", ref _currentTimeMs, 0, maxTime, "%.1f ms"))
                {
                    _needsRedraw = true;
                }
                break;

            case CubeViewMode.Inline:
                ImGui.Text("Inline:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                int maxInline = _dataset.GridParameters.InlineCount - 1;
                if (ImGui.SliderInt("##Inline", ref _currentInline, 0, maxInline))
                {
                    _needsRedraw = true;
                }
                break;

            case CubeViewMode.Crossline:
                ImGui.Text("Crossline:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                int maxCrossline = _dataset.GridParameters.CrosslineCount - 1;
                if (ImGui.SliderInt("##Crossline", ref _currentCrossline, 0, maxCrossline))
                {
                    _needsRedraw = true;
                }
                break;

            case CubeViewMode.Lines3D:
                ImGui.Text("3D Line View");
                break;
        }

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        // Gain control
        ImGui.Text("Gain:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.SliderFloat("##Gain", ref _gain, 0.1f, 10f, "%.1fx"))
        {
            _needsRedraw = true;
        }

        ImGui.SameLine();

        // Color map
        ImGui.SetNextItemWidth(100);
        if (ImGui.Combo("##ColorMap", ref _colorMapIndex, _colorMapNames, _colorMapNames.Length))
        {
            _needsRedraw = true;
        }

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        // Display toggles
        if (ImGui.Checkbox("Grid", ref _showGrid))
        {
            _needsRedraw = true;
        }

        ImGui.SameLine();

        if (ImGui.Checkbox("Lines", ref _showLineOverlay))
        {
            _needsRedraw = true;
        }

        ImGui.SameLine();

        if (ImGui.Checkbox("Intersections", ref _showIntersections))
        {
            _needsRedraw = true;
        }
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        if (_dataset == null)
        {
            ImGui.TextDisabled("No seismic cube loaded");
            return;
        }

        var contentRegion = ImGui.GetContentRegionAvail();

        // Draw cube info panel
        DrawCubeInfoPanel();

        ImGui.Separator();

        // Draw main viewer area
        var viewerSize = new Vector2(contentRegion.X, contentRegion.Y - 80);
        if (ImGui.BeginChild("CubeViewer", viewerSize, ImGuiChildFlags.Border))
        {
            switch (_viewMode)
            {
                case CubeViewMode.TimeSlice:
                    DrawTimeSlice(ref zoom, ref pan);
                    break;

                case CubeViewMode.Inline:
                    DrawInlineSection(ref zoom, ref pan);
                    break;

                case CubeViewMode.Crossline:
                    DrawCrosslineSection(ref zoom, ref pan);
                    break;

                case CubeViewMode.Lines3D:
                    Draw3DLineView(ref zoom, ref pan);
                    break;
            }
        }
        ImGui.EndChild();
    }

    private void DrawCubeInfoPanel()
    {
        if (_dataset == null) return;

        var stats = _dataset.GetStatistics();

        ImGui.Columns(4, "CubeInfo", false);

        ImGui.Text($"Lines: {stats.LineCount}");
        ImGui.NextColumn();
        ImGui.Text($"Intersections: {stats.IntersectionCount}");
        ImGui.NextColumn();
        ImGui.Text($"Packages: {stats.PackageCount}");
        ImGui.NextColumn();
        ImGui.Text($"Total Traces: {stats.TotalTraces}");

        ImGui.Columns(1);
    }

    private void DrawTimeSlice(ref float zoom, ref Vector2 pan)
    {
        if (_dataset == null) return;

        var region = ImGui.GetContentRegionAvail();
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Get or build the time slice
        if (_dataset.RegularizedVolume == null)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Building regularized volume...");
            if (ImGui.Button("Build Volume"))
            {
                _dataset.BuildRegularizedVolume();
                _needsRedraw = true;
            }
            return;
        }

        var slice = _dataset.GetTimeSlice(_currentTimeMs);
        if (slice == null)
        {
            ImGui.TextDisabled("No data at this time");
            return;
        }

        // Draw slice as colored rectangles
        int nx = slice.GetLength(0);
        int ny = slice.GetLength(1);

        float cellWidth = (region.X * zoom) / nx;
        float cellHeight = (region.Y * zoom) / ny;

        // Find amplitude range for normalization
        float minAmp = float.MaxValue, maxAmp = float.MinValue;
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                float val = slice[i, j] * _gain;
                if (val < minAmp) minAmp = val;
                if (val > maxAmp) maxAmp = val;
            }
        }

        float range = maxAmp - minAmp;
        if (range < 1e-10f) range = 1f;

        // Draw cells
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                float val = slice[i, j] * _gain;
                float norm = (val - minAmp) / range;

                var color = GetColorFromMap(norm, _colorMapIndex);
                var p1 = new Vector2(cursorPos.X + i * cellWidth + pan.X, cursorPos.Y + j * cellHeight + pan.Y);
                var p2 = new Vector2(p1.X + cellWidth, p1.Y + cellHeight);

                drawList.AddRectFilled(p1, p2, color);
            }
        }

        // Draw line locations overlay
        if (_showLineOverlay)
        {
            DrawLineOverlayOnTimeSlice(drawList, cursorPos, region, zoom, pan);
        }

        // Draw intersection markers
        if (_showIntersections)
        {
            DrawIntersectionMarkers(drawList, cursorPos, region, zoom, pan);
        }

        // Draw grid
        if (_showGrid)
        {
            DrawGrid(drawList, cursorPos, region, zoom, pan, nx, ny);
        }

        // Handle mouse interaction
        HandleMouseInteraction(cursorPos, region, zoom, pan);
    }

    private void DrawInlineSection(ref float zoom, ref Vector2 pan)
    {
        if (_dataset?.RegularizedVolume == null)
        {
            DrawBuildVolumePrompt();
            return;
        }

        var section = _dataset.GetInlineSection(_currentInline);
        if (section == null)
        {
            ImGui.TextDisabled("No data at this inline");
            return;
        }

        DrawSection(section, ref zoom, ref pan, "Crossline", "Time (ms)");
    }

    private void DrawCrosslineSection(ref float zoom, ref Vector2 pan)
    {
        if (_dataset?.RegularizedVolume == null)
        {
            DrawBuildVolumePrompt();
            return;
        }

        var section = _dataset.GetCrosslineSection(_currentCrossline);
        if (section == null)
        {
            ImGui.TextDisabled("No data at this crossline");
            return;
        }

        DrawSection(section, ref zoom, ref pan, "Inline", "Time (ms)");
    }

    private void DrawSection(float[,] section, ref float zoom, ref Vector2 pan, string xLabel, string yLabel)
    {
        var region = ImGui.GetContentRegionAvail();
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        int nx = section.GetLength(0);
        int ny = section.GetLength(1);

        float cellWidth = (region.X * zoom) / nx;
        float cellHeight = (region.Y * zoom) / ny;

        // Find amplitude range
        float minAmp = float.MaxValue, maxAmp = float.MinValue;
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                float val = section[i, j] * _gain;
                if (val < minAmp) minAmp = val;
                if (val > maxAmp) maxAmp = val;
            }
        }

        float range = maxAmp - minAmp;
        if (range < 1e-10f) range = 1f;

        // Draw cells
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                float val = section[i, j] * _gain;
                float norm = (val - minAmp) / range;

                var color = GetColorFromMap(norm, _colorMapIndex);
                var p1 = new Vector2(cursorPos.X + i * cellWidth + pan.X, cursorPos.Y + j * cellHeight + pan.Y);
                var p2 = new Vector2(p1.X + cellWidth, p1.Y + cellHeight);

                drawList.AddRectFilled(p1, p2, color);
            }
        }

        // Draw axis labels
        drawList.AddText(new Vector2(cursorPos.X + region.X / 2 - 30, cursorPos.Y + region.Y - 20),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), xLabel);
    }

    private void Draw3DLineView(ref float zoom, ref Vector2 pan)
    {
        if (_dataset == null) return;

        var region = ImGui.GetContentRegionAvail();
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Draw lines in map view
        var bounds = _dataset.Bounds;
        float scaleX = region.X / Math.Max(1, bounds.Width);
        float scaleY = region.Y / Math.Max(1, bounds.Height);
        float scale = Math.Min(scaleX, scaleY) * 0.9f * zoom;

        Vector2 offset = new Vector2(
            cursorPos.X + region.X / 2 - bounds.Width * scale / 2 + pan.X,
            cursorPos.Y + region.Y / 2 - bounds.Height * scale / 2 + pan.Y
        );

        // Draw lines
        foreach (var line in _dataset.Lines)
        {
            if (!line.IsVisible) continue;

            var p1 = new Vector2(
                offset.X + (line.Geometry.StartPoint.X - bounds.MinX) * scale,
                offset.Y + (line.Geometry.StartPoint.Y - bounds.MinY) * scale
            );
            var p2 = new Vector2(
                offset.X + (line.Geometry.EndPoint.X - bounds.MinX) * scale,
                offset.Y + (line.Geometry.EndPoint.Y - bounds.MinY) * scale
            );

            var lineColor = ImGui.GetColorU32(line.Color);
            drawList.AddLine(p1, p2, lineColor, 3.0f);

            // Draw line label
            var midPoint = (p1 + p2) / 2;
            drawList.AddText(midPoint, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), line.Name);

            // Highlight selected line
            if (line.Id == _selectedLineId)
            {
                drawList.AddLine(p1, p2, ImGui.GetColorU32(new Vector4(1, 1, 0, 1)), 5.0f);
            }
        }

        // Draw intersection points
        if (_showIntersections)
        {
            foreach (var intersection in _dataset.Intersections)
            {
                var p = new Vector2(
                    offset.X + (intersection.IntersectionPoint.X - bounds.MinX) * scale,
                    offset.Y + (intersection.IntersectionPoint.Y - bounds.MinY) * scale
                );

                uint color = intersection.NormalizationApplied
                    ? ImGui.GetColorU32(new Vector4(0, 1, 0, 1))
                    : ImGui.GetColorU32(new Vector4(1, 0, 0, 1));

                drawList.AddCircleFilled(p, 8, color);
                drawList.AddCircle(p, 8, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), 12, 2);

                // Show intersection info on hover
                if (Vector2.Distance(ImGui.GetMousePos(), p) < 15)
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Intersection: {intersection.Line1Name} x {intersection.Line2Name}");
                    ImGui.Text($"Angle: {intersection.IntersectionAngle:F1}°");
                    ImGui.Text($"Tie Quality: {intersection.TieQuality:F2}");
                    if (intersection.IsPerpendicular)
                        ImGui.TextColored(new Vector4(0, 1, 0, 1), "Perpendicular");
                    ImGui.EndTooltip();
                }
            }
        }
    }

    private void DrawBuildVolumePrompt()
    {
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Regularized volume not built yet.");
        ImGui.Text("Build the volume to view slices and sections.");
        if (ImGui.Button("Build Regularized Volume"))
        {
            _dataset?.BuildRegularizedVolume();
            _needsRedraw = true;
        }
    }

    private void DrawLineOverlayOnTimeSlice(ImDrawListPtr drawList, Vector2 cursorPos, Vector2 region, float zoom, Vector2 pan)
    {
        if (_dataset == null) return;

        var bounds = _dataset.Bounds;
        var grid = _dataset.GridParameters;

        foreach (var line in _dataset.Lines)
        {
            if (!line.IsVisible) continue;

            // Convert line coordinates to grid/screen coordinates
            float x1 = (line.Geometry.StartPoint.X - bounds.MinX) / bounds.Width * region.X * zoom + pan.X;
            float y1 = (line.Geometry.StartPoint.Y - bounds.MinY) / bounds.Height * region.Y * zoom + pan.Y;
            float x2 = (line.Geometry.EndPoint.X - bounds.MinX) / bounds.Width * region.X * zoom + pan.X;
            float y2 = (line.Geometry.EndPoint.Y - bounds.MinY) / bounds.Height * region.Y * zoom + pan.Y;

            var p1 = new Vector2(cursorPos.X + x1, cursorPos.Y + y1);
            var p2 = new Vector2(cursorPos.X + x2, cursorPos.Y + y2);

            drawList.AddLine(p1, p2, ImGui.GetColorU32(line.Color), 2.0f);
        }
    }

    private void DrawIntersectionMarkers(ImDrawListPtr drawList, Vector2 cursorPos, Vector2 region, float zoom, Vector2 pan)
    {
        if (_dataset == null) return;

        var bounds = _dataset.Bounds;

        foreach (var intersection in _dataset.Intersections)
        {
            float x = (intersection.IntersectionPoint.X - bounds.MinX) / bounds.Width * region.X * zoom + pan.X;
            float y = (intersection.IntersectionPoint.Y - bounds.MinY) / bounds.Height * region.Y * zoom + pan.Y;

            var p = new Vector2(cursorPos.X + x, cursorPos.Y + y);

            uint color = intersection.NormalizationApplied
                ? ImGui.GetColorU32(new Vector4(0, 1, 0, 1))
                : ImGui.GetColorU32(new Vector4(1, 0.5f, 0, 1));

            drawList.AddCircleFilled(p, 6, color);
        }
    }

    private void DrawGrid(ImDrawListPtr drawList, Vector2 cursorPos, Vector2 region, float zoom, Vector2 pan, int nx, int ny)
    {
        uint gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f));
        int gridStep = 10;

        float cellWidth = (region.X * zoom) / nx;
        float cellHeight = (region.Y * zoom) / ny;

        for (int i = 0; i <= nx; i += gridStep)
        {
            var p1 = new Vector2(cursorPos.X + i * cellWidth + pan.X, cursorPos.Y + pan.Y);
            var p2 = new Vector2(cursorPos.X + i * cellWidth + pan.X, cursorPos.Y + ny * cellHeight + pan.Y);
            drawList.AddLine(p1, p2, gridColor);
        }

        for (int j = 0; j <= ny; j += gridStep)
        {
            var p1 = new Vector2(cursorPos.X + pan.X, cursorPos.Y + j * cellHeight + pan.Y);
            var p2 = new Vector2(cursorPos.X + nx * cellWidth + pan.X, cursorPos.Y + j * cellHeight + pan.Y);
            drawList.AddLine(p1, p2, gridColor);
        }
    }

    private void HandleMouseInteraction(Vector2 cursorPos, Vector2 region, float zoom, Vector2 pan)
    {
        if (!ImGui.IsWindowHovered()) return;

        var mousePos = ImGui.GetMousePos();
        var relPos = mousePos - cursorPos - pan;

        // Show coordinates on hover
        if (_dataset != null)
        {
            float x = _dataset.Bounds.MinX + (relPos.X / (region.X * zoom)) * _dataset.Bounds.Width;
            float y = _dataset.Bounds.MinY + (relPos.Y / (region.Y * zoom)) * _dataset.Bounds.Height;

            ImGui.BeginTooltip();
            ImGui.Text($"X: {x:F1} m, Y: {y:F1} m");
            ImGui.Text($"Time: {_currentTimeMs:F1} ms");
            ImGui.EndTooltip();
        }
    }

    private static uint GetColorFromMap(float value, int colorMapIndex)
    {
        value = Math.Clamp(value, 0f, 1f);

        byte r, g, b;

        switch (colorMapIndex)
        {
            case 0: // Grayscale
                byte gray = (byte)(value * 255);
                return ImGui.GetColorU32(new Vector4(gray / 255f, gray / 255f, gray / 255f, 1));

            case 1: // Seismic (blue-white-red)
                if (value < 0.5f)
                {
                    float t = value * 2;
                    r = (byte)(t * 255);
                    g = (byte)(t * 255);
                    b = 255;
                }
                else
                {
                    float t = (value - 0.5f) * 2;
                    r = 255;
                    g = (byte)((1 - t) * 255);
                    b = (byte)((1 - t) * 255);
                }
                return ImGui.GetColorU32(new Vector4(r / 255f, g / 255f, b / 255f, 1));

            case 2: // Viridis
                if (value < 0.25f)
                {
                    float t = value * 4;
                    r = (byte)(68 + t * (72 - 68));
                    g = (byte)(1 + t * (40 - 1));
                    b = (byte)(84 + t * (120 - 84));
                }
                else if (value < 0.5f)
                {
                    float t = (value - 0.25f) * 4;
                    r = (byte)(72 - t * 40);
                    g = (byte)(40 + t * 80);
                    b = (byte)(120 + t * 20);
                }
                else if (value < 0.75f)
                {
                    float t = (value - 0.5f) * 4;
                    r = (byte)(32 + t * 100);
                    g = (byte)(120 + t * 60);
                    b = (byte)(140 - t * 50);
                }
                else
                {
                    float t = (value - 0.75f) * 4;
                    r = (byte)(132 + t * 120);
                    g = (byte)(180 + t * 50);
                    b = (byte)(90 - t * 70);
                }
                return ImGui.GetColorU32(new Vector4(r / 255f, g / 255f, b / 255f, 1));

            case 3: // Jet
                if (value < 0.125f)
                {
                    r = 0; g = 0; b = (byte)(128 + value * 8 * 127);
                }
                else if (value < 0.375f)
                {
                    r = 0; g = (byte)((value - 0.125f) * 4 * 255); b = 255;
                }
                else if (value < 0.625f)
                {
                    r = (byte)((value - 0.375f) * 4 * 255); g = 255; b = (byte)(255 - (value - 0.375f) * 4 * 255);
                }
                else if (value < 0.875f)
                {
                    r = 255; g = (byte)(255 - (value - 0.625f) * 4 * 255); b = 0;
                }
                else
                {
                    r = (byte)(255 - (value - 0.875f) * 8 * 127); g = 0; b = 0;
                }
                return ImGui.GetColorU32(new Vector4(r / 255f, g / 255f, b / 255f, 1));

            case 4: // Hot
                if (value < 0.33f)
                {
                    r = (byte)(value * 3 * 255); g = 0; b = 0;
                }
                else if (value < 0.67f)
                {
                    r = 255; g = (byte)((value - 0.33f) * 3 * 255); b = 0;
                }
                else
                {
                    r = 255; g = 255; b = (byte)((value - 0.67f) * 3 * 255);
                }
                return ImGui.GetColorU32(new Vector4(r / 255f, g / 255f, b / 255f, 1));

            case 5: // Cool
                r = (byte)(value * 255);
                g = (byte)((1 - value) * 255);
                b = 255;
                return ImGui.GetColorU32(new Vector4(r / 255f, g / 255f, b / 255f, 1));

            default:
                return ImGui.GetColorU32(new Vector4(value, value, value, 1));
        }
    }

    public void Dispose()
    {
        _cachedSliceImage = null;
    }
}

/// <summary>
/// View modes for the seismic cube viewer
/// </summary>
public enum CubeViewMode
{
    TimeSlice,
    Inline,
    Crossline,
    Lines3D
}
