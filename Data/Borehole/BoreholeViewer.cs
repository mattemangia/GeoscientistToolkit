// GeoscientistToolkit/UI/Borehole/BoreholeViewer.cs

using System.Numerics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Borehole;

/// <summary>
///     Viewer for displaying borehole/well log data with lithology column and parameter tracks
/// </summary>
public class BoreholeViewer : IDatasetViewer
{
    private readonly BoreholeDataset _dataset;
    private readonly Vector4 _depthTextColor = new(0.7f, 0.7f, 0.7f, 1.0f);

    // Colors
    private readonly Vector4 _gridColor = new(0.3f, 0.3f, 0.3f, 0.5f);
    private readonly int _gridInterval = 10; // meters
    private readonly float _lithologyColumnWidth = 150f;
    private readonly Vector4 _textColor = new(0.9f, 0.9f, 0.9f, 1.0f);
    private readonly float _trackSpacing = 10f;
    private bool _autoScaleDepth = true;
    private float _depthEnd;
    private float _depthStart;
    private bool _showDepthGrid = true;
    private bool _showLithologyNames = true;
    private bool _showParameterValues = true;

    public BoreholeViewer(BoreholeDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));

        // Initialize depth range
        if (_dataset.LithologyUnits.Any())
        {
            _depthStart = _dataset.LithologyUnits.Min(u => u.DepthFrom);
            _depthEnd = _dataset.LithologyUnits.Max(u => u.DepthTo);
        }
        else
        {
            _depthStart = 0;
            _depthEnd = _dataset.TotalDepth;
        }
    }

    public void DrawToolbarControls()
    {
        // View controls
        ImGui.Text("Depth Range:");
        ImGui.SameLine();

        if (ImGui.Checkbox("Auto", ref _autoScaleDepth))
            if (_autoScaleDepth && _dataset.LithologyUnits.Any())
            {
                _depthStart = _dataset.LithologyUnits.Min(u => u.DepthFrom);
                _depthEnd = _dataset.LithologyUnits.Max(u => u.DepthTo);
            }

        if (!_autoScaleDepth)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.DragFloat("##StartDepth", ref _depthStart, 0.1f, 0, _dataset.TotalDepth, "%.1f m");

            ImGui.SameLine();
            ImGui.Text("to");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.DragFloat("##EndDepth", ref _depthEnd, 0.1f, _depthStart, _dataset.TotalDepth, "%.1f m");
        }

        ImGui.SameLine();
        ImGui.Separator();

        // Display options
        ImGui.SameLine();
        ImGui.Checkbox("Grid", ref _showDepthGrid);

        ImGui.SameLine();
        ImGui.Checkbox("Names", ref _showLithologyNames);

        ImGui.SameLine();
        ImGui.Checkbox("Values", ref _showParameterValues);

        ImGui.SameLine();
        ImGui.Separator();

        // Export options
        ImGui.SameLine();
        if (ImGui.Button("Export Log...")) Logger.Log("Export borehole log functionality to be implemented");
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        var drawList = ImGui.GetWindowDrawList();
        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = ImGui.GetContentRegionAvail();

        if (canvasSize.X <= 0 || canvasSize.Y <= 0)
            return;

        // Apply pan offset
        canvasPos += pan;

        // Calculate dimensions
        var depthRange = _depthEnd - _depthStart;
        if (depthRange <= 0)
            depthRange = 1;

        var pixelsPerMeter = (canvasSize.Y - 50) / depthRange * zoom; // Reserve 50px for header

        // Calculate visible tracks
        var visibleTracks = _dataset.ParameterTracks.Values.Where(t => t.IsVisible).ToList();
        var trackWidth = _dataset.TrackWidth;

        // Start drawing
        var currentX = canvasPos.X + 50; // Reserve 50px for depth scale
        var startY = canvasPos.Y + 30; // Reserve 30px for header

        // Draw depth scale
        DrawDepthScale(drawList, new Vector2(canvasPos.X, startY), canvasSize.Y - 30, pixelsPerMeter);

        // Draw lithology column
        DrawLithologyColumn(drawList, new Vector2(currentX, startY), _lithologyColumnWidth, canvasSize.Y - 30,
            pixelsPerMeter);
        currentX += _lithologyColumnWidth + _trackSpacing;

        // Draw parameter tracks
        foreach (var track in visibleTracks)
        {
            DrawParameterTrack(drawList, track, new Vector2(currentX, startY), trackWidth, canvasSize.Y - 30,
                pixelsPerMeter);
            currentX += trackWidth + _trackSpacing;
        }

        // Draw legend
        DrawLegend(drawList, canvasPos, canvasSize);

        // Handle mouse input for pan/zoom
        HandleInput(ref zoom, ref pan, canvasPos, canvasSize);
    }

    public void Dispose()
    {
        // Nothing to dispose
    }

    private void DrawDepthScale(ImDrawListPtr drawList, Vector2 pos, float height, float pixelsPerMeter)
    {
        var scaleWidth = 40f;

        // Draw scale background
        drawList.AddRectFilled(pos, pos + new Vector2(scaleWidth, height),
            ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 1.0f)));

        // Draw depth markers
        var interval = _gridInterval;
        var startDepth = (int)Math.Ceiling(_depthStart / interval) * interval;

        for (var depth = startDepth; depth <= _depthEnd; depth += interval)
        {
            var y = pos.Y + (depth - _depthStart) * pixelsPerMeter;

            if (y < pos.Y || y > pos.Y + height)
                continue;

            // Draw tick
            drawList.AddLine(
                new Vector2(pos.X + scaleWidth - 10, y),
                new Vector2(pos.X + scaleWidth, y),
                ImGui.GetColorU32(_textColor), 2f);

            // Draw depth text
            var depthText = $"{depth}m";
            var textSize = ImGui.CalcTextSize(depthText);
            drawList.AddText(
                new Vector2(pos.X + scaleWidth - textSize.X - 12, y - textSize.Y * 0.5f),
                ImGui.GetColorU32(_depthTextColor),
                depthText);
        }
    }

    private void DrawLithologyColumn(ImDrawListPtr drawList, Vector2 pos, float width, float height,
        float pixelsPerMeter)
    {
        // Draw column header
        var headerHeight = 25f;
        drawList.AddRectFilled(pos - new Vector2(0, headerHeight), pos + new Vector2(width, 0),
            ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f)));

        var headerText = "Lithology";
        var textSize = ImGui.CalcTextSize(headerText);
        drawList.AddText(
            pos - new Vector2(0, headerHeight) +
            new Vector2((width - textSize.X) * 0.5f, (headerHeight - textSize.Y) * 0.5f),
            ImGui.GetColorU32(_textColor),
            headerText);

        // Draw column border
        drawList.AddRect(pos, pos + new Vector2(width, height),
            ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)), 0, ImDrawFlags.None, 2f);

        // Draw lithology units
        foreach (var unit in _dataset.LithologyUnits)
        {
            var unitStartY = pos.Y + (unit.DepthFrom - _depthStart) * pixelsPerMeter;
            var unitEndY = pos.Y + (unit.DepthTo - _depthStart) * pixelsPerMeter;

            // Skip if outside visible range
            if (unitEndY < pos.Y || unitStartY > pos.Y + height)
                continue;

            // Clamp to visible area
            unitStartY = Math.Max(unitStartY, pos.Y);
            unitEndY = Math.Min(unitEndY, pos.Y + height);

            var unitHeight = unitEndY - unitStartY;

            if (unitHeight < 1)
                continue;

            // Draw unit background with pattern
            DrawLithologyPattern(drawList,
                new Vector2(pos.X, unitStartY),
                new Vector2(width, unitHeight),
                unit.Color,
                GetPatternForLithology(unit.LithologyType));

            // Draw unit border
            drawList.AddRect(
                new Vector2(pos.X, unitStartY),
                new Vector2(pos.X + width, unitEndY),
                ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)), 0, ImDrawFlags.None, 1f);

            // Draw unit name if space permits and option enabled
            if (_showLithologyNames && unitHeight > 20)
            {
                var unitText = unit.Name;
                var unitTextSize = ImGui.CalcTextSize(unitText);

                if (unitTextSize.Y < unitHeight - 4)
                    drawList.AddText(
                        new Vector2(pos.X + (width - unitTextSize.X) * 0.5f,
                            unitStartY + (unitHeight - unitTextSize.Y) * 0.5f),
                        ImGui.GetColorU32(new Vector4(0, 0, 0, 1)),
                        unitText);
            }
        }

        // Draw depth grid lines if enabled
        if (_showDepthGrid)
        {
            var interval = _gridInterval;
            var startDepth = (int)Math.Ceiling(_depthStart / interval) * interval;

            for (var depth = startDepth; depth <= _depthEnd; depth += interval)
            {
                var y = pos.Y + (depth - _depthStart) * pixelsPerMeter;

                if (y < pos.Y || y > pos.Y + height)
                    continue;

                drawList.AddLine(
                    new Vector2(pos.X, y),
                    new Vector2(pos.X + width, y),
                    ImGui.GetColorU32(_gridColor), 1f);
            }
        }
    }

    private void DrawParameterTrack(ImDrawListPtr drawList, ParameterTrack track, Vector2 pos, float width,
        float height, float pixelsPerMeter)
    {
        // Draw track header
        var headerHeight = 25f;
        drawList.AddRectFilled(pos - new Vector2(0, headerHeight), pos + new Vector2(width, 0),
            ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f)));

        var headerText = $"{track.Name}\n({track.Unit})";
        var textSize = ImGui.CalcTextSize(track.Name);
        drawList.AddText(
            pos - new Vector2(0, headerHeight) + new Vector2((width - textSize.X) * 0.5f, 2),
            ImGui.GetColorU32(_textColor),
            track.Name);

        var unitSize = ImGui.CalcTextSize($"({track.Unit})");
        drawList.AddText(
            pos - new Vector2(0, headerHeight) + new Vector2((width - unitSize.X) * 0.5f, 14),
            ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.6f, 1.0f)),
            $"({track.Unit})");

        // Draw track background
        drawList.AddRectFilled(pos, pos + new Vector2(width, height),
            ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));

        // Draw track border
        drawList.AddRect(pos, pos + new Vector2(width, height),
            ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)), 0, ImDrawFlags.None, 2f);

        // Draw scale labels
        var minText = track.MinValue.ToString("F2");
        var maxText = track.MaxValue.ToString("F2");

        drawList.AddText(
            pos + new Vector2(2, height - 15),
            ImGui.GetColorU32(_depthTextColor),
            minText);

        drawList.AddText(
            pos + new Vector2(2, 2),
            ImGui.GetColorU32(_depthTextColor),
            maxText);

        // Draw parameter curve
        if (track.Points.Count >= 2)
        {
            var points = track.Points.OrderBy(p => p.Depth).ToList();

            for (var i = 0; i < points.Count - 1; i++)
            {
                var p1 = points[i];
                var p2 = points[i + 1];

                var y1 = pos.Y + (p1.Depth - _depthStart) * pixelsPerMeter;
                var y2 = pos.Y + (p2.Depth - _depthStart) * pixelsPerMeter;

                // Skip if completely outside visible range
                if ((y1 < pos.Y && y2 < pos.Y) || (y1 > pos.Y + height && y2 > pos.Y + height))
                    continue;

                // Calculate X positions based on normalized values
                float x1, x2;

                if (track.IsLogarithmic)
                {
                    var logMin = (float)Math.Log10(Math.Max(track.MinValue, 0.001));
                    var logMax = (float)Math.Log10(track.MaxValue);
                    var logVal1 = (float)Math.Log10(Math.Max(p1.Value, 0.001));
                    var logVal2 = (float)Math.Log10(Math.Max(p2.Value, 0.001));

                    var norm1 = (logVal1 - logMin) / (logMax - logMin);
                    var norm2 = (logVal2 - logMin) / (logMax - logMin);

                    x1 = pos.X + norm1 * width;
                    x2 = pos.X + norm2 * width;
                }
                else
                {
                    var norm1 = (p1.Value - track.MinValue) / (track.MaxValue - track.MinValue);
                    var norm2 = (p2.Value - track.MinValue) / (track.MaxValue - track.MinValue);

                    x1 = pos.X + norm1 * width;
                    x2 = pos.X + norm2 * width;
                }

                // Clamp to track bounds
                x1 = Math.Clamp(x1, pos.X, pos.X + width);
                x2 = Math.Clamp(x2, pos.X, pos.X + width);

                // Draw line segment
                drawList.AddLine(
                    new Vector2(x1, y1),
                    new Vector2(x2, y2),
                    ImGui.GetColorU32(track.Color), 2f);

                // Draw value labels if enabled and space permits
                if (_showParameterValues && Math.Abs(y2 - y1) > 20)
                {
                    var valueText = p1.Value.ToString("F2");
                    var valueSize = ImGui.CalcTextSize(valueText);

                    if (x1 + valueSize.X < pos.X + width - 5)
                        drawList.AddText(
                            new Vector2(x1 + 3, y1 - valueSize.Y * 0.5f),
                            ImGui.GetColorU32(track.Color),
                            valueText);
                }
            }

            // Draw data points
            foreach (var point in points)
            {
                var y = pos.Y + (point.Depth - _depthStart) * pixelsPerMeter;

                if (y < pos.Y || y > pos.Y + height)
                    continue;

                float x;
                if (track.IsLogarithmic)
                {
                    var logMin = (float)Math.Log10(Math.Max(track.MinValue, 0.001));
                    var logMax = (float)Math.Log10(track.MaxValue);
                    var logVal = (float)Math.Log10(Math.Max(point.Value, 0.001));
                    var norm = (logVal - logMin) / (logMax - logMin);
                    x = pos.X + norm * width;
                }
                else
                {
                    var norm = (point.Value - track.MinValue) / (track.MaxValue - track.MinValue);
                    x = pos.X + norm * width;
                }

                x = Math.Clamp(x, pos.X, pos.X + width);

                drawList.AddCircleFilled(new Vector2(x, y), 3f, ImGui.GetColorU32(track.Color));
            }
        }

        // Draw depth grid lines if enabled
        if (_showDepthGrid)
        {
            var interval = _gridInterval;
            var startDepth = (int)Math.Ceiling(_depthStart / interval) * interval;

            for (var depth = startDepth; depth <= _depthEnd; depth += interval)
            {
                var y = pos.Y + (depth - _depthStart) * pixelsPerMeter;

                if (y < pos.Y || y > pos.Y + height)
                    continue;

                drawList.AddLine(
                    new Vector2(pos.X, y),
                    new Vector2(pos.X + width, y),
                    ImGui.GetColorU32(_gridColor), 1f);
            }
        }
    }

    private void DrawLegend(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
    {
        if (!_dataset.ShowLegend || !_dataset.LithologyUnits.Any())
            return;

        // Get unique lithology types
        var uniqueLithologies = _dataset.LithologyUnits
            .GroupBy(u => u.LithologyType)
            .Select(g => g.First())
            .ToList();

        var legendWidth = 200f;
        var legendHeight = 30f + uniqueLithologies.Count * 25f;
        var legendPos = canvasPos + new Vector2(canvasSize.X - legendWidth - 10, 10);

        // Draw legend background
        drawList.AddRectFilled(legendPos, legendPos + new Vector2(legendWidth, legendHeight),
            ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 0.9f)), 5f);

        drawList.AddRect(legendPos, legendPos + new Vector2(legendWidth, legendHeight),
            ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)), 5f, ImDrawFlags.None, 2f);

        // Draw legend title
        var titleText = "Legend";
        var titleSize = ImGui.CalcTextSize(titleText);
        drawList.AddText(
            legendPos + new Vector2((legendWidth - titleSize.X) * 0.5f, 5),
            ImGui.GetColorU32(_textColor),
            titleText);

        // Draw legend items
        var itemY = legendPos.Y + 25f;
        foreach (var unit in uniqueLithologies)
        {
            var swatchSize = 20f;
            var swatchPos = legendPos + new Vector2(10, itemY);

            // Draw lithology swatch with pattern
            DrawLithologyPattern(drawList, swatchPos, new Vector2(swatchSize, swatchSize),
                unit.Color, GetPatternForLithology(unit.LithologyType));

            drawList.AddRect(swatchPos, swatchPos + new Vector2(swatchSize, swatchSize),
                ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)));

            // Draw lithology name
            var nameText = unit.LithologyType;
            drawList.AddText(
                swatchPos + new Vector2(swatchSize + 10, (swatchSize - ImGui.CalcTextSize(nameText).Y) * 0.5f),
                ImGui.GetColorU32(_textColor),
                nameText);

            itemY += 25f;
        }
    }

    private void DrawLithologyPattern(ImDrawListPtr drawList, Vector2 pos, Vector2 size, Vector4 color,
        LithologyPattern pattern)
    {
        var colorU32 = ImGui.GetColorU32(color);
        var patternColorU32 = ImGui.GetColorU32(new Vector4(color.X * 0.7f, color.Y * 0.7f, color.Z * 0.7f, color.W));

        // Draw base color
        drawList.AddRectFilled(pos, pos + size, colorU32);

        // Draw pattern
        switch (pattern)
        {
            case LithologyPattern.Dots:
                for (var y = 0f; y < size.Y; y += 8)
                for (var x = 0f; x < size.X; x += 8)
                    drawList.AddCircleFilled(pos + new Vector2(x + 4, y + 4), 1.5f, patternColorU32);
                break;

            case LithologyPattern.HorizontalLines:
                for (var y = 0f; y < size.Y; y += 6)
                    drawList.AddLine(pos + new Vector2(0, y), pos + new Vector2(size.X, y), patternColorU32, 1.5f);
                break;

            case LithologyPattern.VerticalLines:
                for (var x = 0f; x < size.X; x += 6)
                    drawList.AddLine(pos + new Vector2(x, 0), pos + new Vector2(x, size.Y), patternColorU32, 1.5f);
                break;

            case LithologyPattern.Diagonal:
                for (var i = -size.Y; i < size.X + size.Y; i += 8)
                {
                    var p1 = pos + new Vector2(i, 0);
                    var p2 = pos + new Vector2(i + size.Y, size.Y);
                    drawList.AddLine(p1, p2, patternColorU32, 1.5f);
                }

                break;

            case LithologyPattern.Crosses:
                for (var y = 0f; y < size.Y; y += 10)
                for (var x = 0f; x < size.X; x += 10)
                {
                    var center = pos + new Vector2(x + 5, y + 5);
                    drawList.AddLine(center - new Vector2(3, 0), center + new Vector2(3, 0), patternColorU32, 1.5f);
                    drawList.AddLine(center - new Vector2(0, 3), center + new Vector2(0, 3), patternColorU32, 1.5f);
                }

                break;

            case LithologyPattern.Sand:
                var random = new Random(0);
                for (var i = 0; i < (int)(size.X * size.Y / 20); i++)
                {
                    var x = (float)random.NextDouble() * size.X;
                    var y = (float)random.NextDouble() * size.Y;
                    drawList.AddCircleFilled(pos + new Vector2(x, y), 1f, patternColorU32);
                }

                break;

            case LithologyPattern.Bricks:
                for (var y = 0f; y < size.Y; y += 10)
                {
                    var offset = (int)(y / 10) % 2 == 0 ? 0f : 15f;
                    for (var x = -15f; x < size.X; x += 30)
                        drawList.AddRect(pos + new Vector2(x + offset, y),
                            pos + new Vector2(x + offset + 28, y + 8), patternColorU32);
                }

                break;

            case LithologyPattern.Limestone:
                random = new Random(1);
                for (var i = 0; i < (int)(size.X * size.Y / 30); i++)
                {
                    var x = (float)random.NextDouble() * size.X;
                    var y = (float)random.NextDouble() * size.Y;
                    var r = (float)random.NextDouble() * 2 + 1;
                    drawList.AddCircle(pos + new Vector2(x, y), r, patternColorU32);
                }

                break;
        }
    }

    private LithologyPattern GetPatternForLithology(string lithologyType)
    {
        if (_dataset.LithologyPatterns.TryGetValue(lithologyType, out var pattern))
            return pattern;

        return LithologyPattern.Solid;
    }

    private void HandleInput(ref float zoom, ref Vector2 pan, Vector2 canvasPos, Vector2 canvasSize)
    {
        var io = ImGui.GetIO();

        if (!ImGui.IsWindowHovered())
            return;

        // Mouse wheel zoom
        if (io.MouseWheel != 0)
        {
            var zoomFactor = 1.1f;
            if (io.MouseWheel > 0)
                zoom *= zoomFactor;
            else
                zoom /= zoomFactor;

            zoom = Math.Clamp(zoom, 0.1f, 10f);
        }

        // Middle mouse button pan
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle)) pan += io.MouseDelta;

        // Reset view with 'R'
        if (ImGui.IsKeyPressed(ImGuiKey.R))
        {
            zoom = 1f;
            pan = Vector2.Zero;
        }
    }
}