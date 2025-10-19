using System.Drawing;
using System.Numerics;
using GeoscientistToolkit.Business.Stratigraphies;
using GeoscientistToolkit.UI.Windows;
using GeoscientistToolkit.Util;
using StbImageWriteSharp;

namespace GeoscientistToolkit.UI.Utils;

// Data structure to pass settings from the viewer to the exporter
public class StratigraphyExportSettings
{
    public List<(IStratigraphy strat, int index)> VisibleStratigraphies { get; init; }
    public List<List<StratigraphicUnit>> ColumnUnits { get; init; }
    public List<StratigraphyCorrelationViewer.OrogenicEvent> OrogenicEvents { get; init; }
    public double MaxAge { get; init; }
    public float ZoomLevel { get; init; }
    public float ColumnWidth { get; init; }
    public float AgeScale { get; init; }
    public bool ShowCorrelationLines { get; init; }
    public bool ShowAgeScale { get; init; }
    public StratigraphicLevel DisplayLevel { get; init; }
}

/// <summary>
///     Handles the creation of a PNG image from stratigraphy data.
///     Renders the chart from scratch onto a pixel buffer.
/// </summary>
public class StratigraphyImageExporter
{
    // A very simple 5x7 pixel embedded bitmap font
    private static readonly Dictionary<char, byte[]> SimpleFont = new()
    {
        { 'A', new byte[] { 0x7C, 0x12, 0x11, 0x12, 0x7C } }, { 'B', new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x36 } },
        { 'C', new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x22 } }, { 'D', new byte[] { 0x7F, 0x41, 0x41, 0x22, 0x1C } },
        { 'E', new byte[] { 0x7F, 0x49, 0x49, 0x41, 0x41 } }, { 'F', new byte[] { 0x7F, 0x09, 0x09, 0x01, 0x01 } },
        { 'G', new byte[] { 0x3E, 0x41, 0x49, 0x49, 0x7A } }, { 'H', new byte[] { 0x7F, 0x08, 0x08, 0x08, 0x7F } },
        { 'I', new byte[] { 0x00, 0x41, 0x7F, 0x41, 0x00 } }, { 'J', new byte[] { 0x20, 0x40, 0x41, 0x3F, 0x01 } },
        { 'K', new byte[] { 0x7F, 0x08, 0x14, 0x22, 0x41 } }, { 'L', new byte[] { 0x7F, 0x40, 0x40, 0x40, 0x40 } },
        { 'M', new byte[] { 0x7F, 0x02, 0x0C, 0x02, 0x7F } }, { 'N', new byte[] { 0x7F, 0x04, 0x08, 0x10, 0x7F } },
        { 'O', new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x3E } }, { 'P', new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x06 } },
        { 'Q', new byte[] { 0x3E, 0x41, 0x51, 0x21, 0x5E } }, { 'R', new byte[] { 0x7F, 0x09, 0x19, 0x29, 0x46 } },
        { 'S', new byte[] { 0x46, 0x49, 0x49, 0x49, 0x31 } }, { 'T', new byte[] { 0x01, 0x01, 0x7F, 0x01, 0x01 } },
        { 'U', new byte[] { 0x3F, 0x40, 0x40, 0x40, 0x3F } }, { 'V', new byte[] { 0x1F, 0x20, 0x40, 0x20, 0x1F } },
        { 'W', new byte[] { 0x3F, 0x40, 0x38, 0x40, 0x3F } }, { 'X', new byte[] { 0x63, 0x14, 0x08, 0x14, 0x63 } },
        { 'Y', new byte[] { 0x07, 0x08, 0x70, 0x08, 0x07 } }, { 'Z', new byte[] { 0x61, 0x51, 0x49, 0x45, 0x43 } },
        { '0', new byte[] { 0x3E, 0x51, 0x49, 0x45, 0x3E } }, { '1', new byte[] { 0x00, 0x42, 0x7F, 0x40, 0x00 } },
        { '2', new byte[] { 0x42, 0x61, 0x51, 0x49, 0x46 } }, { '3', new byte[] { 0x21, 0x41, 0x45, 0x4B, 0x31 } },
        { '4', new byte[] { 0x18, 0x14, 0x12, 0x7F, 0x10 } }, { '5', new byte[] { 0x27, 0x45, 0x45, 0x45, 0x39 } },
        { '6', new byte[] { 0x3C, 0x4A, 0x49, 0x49, 0x30 } }, { '7', new byte[] { 0x01, 0x71, 0x09, 0x05, 0x03 } },
        { '8', new byte[] { 0x36, 0x49, 0x49, 0x49, 0x36 } }, { '9', new byte[] { 0x06, 0x49, 0x49, 0x29, 0x1E } },
        { ' ', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 } }, { '.', new byte[] { 0x00, 0x60, 0x60, 0x00, 0x00 } },
        { '-', new byte[] { 0x08, 0x08, 0x08, 0x08, 0x08 } }, { '[', new byte[] { 0x00, 0x7F, 0x41, 0x41, 0x00 } },
        { ']', new byte[] { 0x00, 0x41, 0x41, 0x7F, 0x00 } }
    };

    private readonly StratigraphyExportSettings _settings;
    private int _height;
    private byte[] _pixelBuffer;
    private int _width;

    public StratigraphyImageExporter(StratigraphyExportSettings settings)
    {
        _settings = settings;
    }

    public void Export(string filePath)
    {
        try
        {
            // --- 1. Calculate Image Dimensions ---
            var zoomedColumnWidth = _settings.ColumnWidth * _settings.ZoomLevel;
            var zoomedAgeScale = _settings.AgeScale * _settings.ZoomLevel;
            var headerHeight = 80f * _settings.ZoomLevel;
            var leftMargin = 70f;
            var rightMargin = 50f;
            var topMargin = 40f;
            var bottomMargin = 40f;
            var colSpacing = 10 * _settings.ZoomLevel;

            var contentHeight = (float)_settings.MaxAge * zoomedAgeScale;
            _width = (int)(leftMargin + _settings.VisibleStratigraphies.Count * (zoomedColumnWidth + colSpacing) -
                colSpacing + rightMargin);
            _height = (int)(topMargin + headerHeight + contentHeight + bottomMargin);
            _pixelBuffer = new byte[_width * _height * 4]; // 4 channels (RGBA)

            // --- 2. Render Image to Buffer ---
            Clear(255, 255, 255, 255); // White background

            var contentStartY = topMargin + headerHeight;
            var currentX = leftMargin;
            var columnPositions = new List<float>();

            // Draw Headers
            foreach (var (strat, _) in _settings.VisibleStratigraphies)
            {
                FillRect((int)currentX, (int)topMargin, (int)zoomedColumnWidth, (int)headerHeight, 51, 51, 64, 255);
                DrawRect((int)currentX, (int)topMargin, (int)zoomedColumnWidth, (int)headerHeight, 102, 102, 128, 255,
                    2);

                var textLines = WrapTextSimple(strat.Name, (int)(zoomedColumnWidth - 10));
                var textY = topMargin + 10;
                foreach (var line in textLines)
                {
                    DrawText(line, (int)(currentX + (zoomedColumnWidth - line.Length * 6) / 2), (int)textY, 255, 255,
                        255);
                    textY += 14;
                }

                var langText = $"[{strat.LanguageCode}]";
                DrawText(langText, (int)(currentX + (zoomedColumnWidth - langText.Length * 6) / 2),
                    (int)(topMargin + headerHeight - 20), 179, 179, 179);
                currentX += zoomedColumnWidth + colSpacing;
            }

            // Draw Columns
            currentX = leftMargin;
            for (var col = 0; col < _settings.VisibleStratigraphies.Count; col++)
            {
                columnPositions.Add(currentX);
                var units = _settings.ColumnUnits[col];
                foreach (var unit in units)
                {
                    // Younger age (EndAge) is at the top (smaller Y value)
                    var yStart = contentStartY + (float)unit.EndAge * zoomedAgeScale;
// Older age (StartAge) is at the bottom (larger Y value)
                    var yEnd = contentStartY + (float)unit.StartAge * zoomedAgeScale;
                    var height = Math.Max(1, yEnd - yStart);

                    var color = Color.FromArgb(unit.Color.ToArgb());
                    FillRect((int)currentX, (int)yStart, (int)zoomedColumnWidth, (int)height, color.R, color.G, color.B,
                        255);
                    DrawRect((int)currentX, (int)yStart, (int)zoomedColumnWidth, (int)height, 0, 0, 0, 255, 1);

                    if (height > 25 * _settings.ZoomLevel)
                    {
                        var text = unit.Name;
                        if (text.Length * 6 > zoomedColumnWidth - 10) text = unit.Code;
                        if (text.Length * 6 < zoomedColumnWidth - 10)
                        {
                            var textX = (int)(currentX + (zoomedColumnWidth - text.Length * 6) / 2);
                            var textY = (int)(yStart + (height - 7) / 2);
                            DrawText(text, textX + 1, textY + 1, 0, 0, 0); // Shadow
                            DrawText(text, textX, textY, 255, 255, 255);
                        }
                    }
                }

                currentX += zoomedColumnWidth + colSpacing;
            }

            // Draw Correlation Lines
            if (_settings.ShowCorrelationLines && _settings.ColumnUnits.Count > 1)
                DrawCorrelationLines(contentStartY, columnPositions, zoomedColumnWidth, zoomedAgeScale);

            // Draw Orogenic Events
            DrawOrogenicEvents(contentStartY, zoomedAgeScale);

            // Draw Age Scale
            if (_settings.ShowAgeScale) DrawAgeScale(contentStartY, contentHeight, zoomedAgeScale);

            // --- 3. Save Buffer to PNG ---
            using var stream = new MemoryStream();
            var writer = new ImageWriter();
            writer.WritePng(_pixelBuffer, _width, _height, ColorComponents.RedGreenBlueAlpha, stream);
            File.WriteAllBytes(filePath, stream.ToArray());
        }
        catch (Exception ex)
        {
            Logger.LogError($"[StratigraphyImageExporter] Failed to export: {ex.Message}");
            throw; // Re-throw to be caught by the UI
        }
    }

    // --- Drawing Primitives ---

    private void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height) return;
        var index = (y * _width + x) * 4;
        _pixelBuffer[index] = r;
        _pixelBuffer[index + 1] = g;
        _pixelBuffer[index + 2] = b;
        _pixelBuffer[index + 3] = a;
    }

    private void Clear(byte r, byte g, byte b, byte a)
    {
        for (var i = 0; i < _pixelBuffer.Length; i += 4)
        {
            _pixelBuffer[i] = r;
            _pixelBuffer[i + 1] = g;
            _pixelBuffer[i + 2] = b;
            _pixelBuffer[i + 3] = a;
        }
    }

    private void FillRect(int x, int y, int w, int h, byte r, byte g, byte b, byte a)
    {
        for (var j = 0; j < h; j++)
        for (var i = 0; i < w; i++)
            SetPixel(x + i, y + j, r, g, b, a);
    }

    private void DrawRect(int x, int y, int w, int h, byte r, byte g, byte b, byte a, int thickness)
    {
        FillRect(x, y, w, thickness, r, g, b, a); // Top
        FillRect(x, y + h - thickness, w, thickness, r, g, b, a); // Bottom
        FillRect(x, y, thickness, h, r, g, b, a); // Left
        FillRect(x + w - thickness, y, thickness, h, r, g, b, a); // Right
    }

    private void DrawLine(int x1, int y1, int x2, int y2, byte r, byte g, byte b, byte a)
    {
        int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
        int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
        int err = dx + dy, e2;

        while (true)
        {
            SetPixel(x1, y1, r, g, b, a);
            if (x1 == x2 && y1 == y2) break;
            e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x1 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y1 += sy;
            }
        }
    }

    private void DrawDashedLine(int x1, int y1, int x2, int y2, byte r, byte g, byte b, byte a, int dashLength)
    {
        var p1 = new Vector2(x1, y1);
        var p2 = new Vector2(x2, y2);
        var dir = Vector2.Normalize(p2 - p1);
        var length = Vector2.Distance(p1, p2);

        for (float traveled = 0; traveled < length; traveled += dashLength * 2)
        {
            var start = p1 + dir * traveled;
            var end = p1 + dir * Math.Min(traveled + dashLength, length);
            DrawLine((int)start.X, (int)start.Y, (int)end.X, (int)end.Y, r, g, b, a);
        }
    }

    private void DrawText(string text, int x, int y, byte r, byte g, byte b)
    {
        var currentX = x;
        foreach (var c in text.ToUpper())
        {
            if (SimpleFont.TryGetValue(c, out var charData))
                for (var i = 0; i < 5; i++) // Character width
                for (var j = 0; j < 7; j++) // Character height
                    if ((charData[i] & (1 << j)) != 0)
                        SetPixel(currentX + i, y + j, r, g, b, 255);

            currentX += 6; // Character width + 1 pixel spacing
        }
    }

    // --- High-Level Drawing Logic ---

    private void DrawCorrelationLines(float contentStartY, List<float> columnPositions, float zoomedColumnWidth,
        float zoomedAgeScale)
    {
        for (var col1 = 0; col1 < _settings.ColumnUnits.Count - 1; col1++)
        for (var col2 = col1 + 1; col2 < _settings.ColumnUnits.Count; col2++)
        {
            var units1 = _settings.ColumnUnits[col1];
            var units2 = _settings.ColumnUnits[col2];

            foreach (var unit1 in units1.Where(u => !string.IsNullOrEmpty(u.InternationalCorrelationCode)))
            {
                var codes1 = unit1.InternationalCorrelationCode.Split(',');
                foreach (var unit2 in units2.Where(u => !string.IsNullOrEmpty(u.InternationalCorrelationCode)))
                {
                    var codes2 = unit2.InternationalCorrelationCode.Split(',');
                    if (codes1.Any(c1 => codes2.Any(c2 => c1.Trim() == c2.Trim())))
                    {
                        var y1 = contentStartY + (float)unit1.StartAge * zoomedAgeScale;
                        var y2 = contentStartY + (float)unit2.StartAge * zoomedAgeScale;
                        var x1 = columnPositions[col1] + zoomedColumnWidth;
                        var x2 = columnPositions[col2];
                        DrawDashedLine((int)x1, (int)y1, (int)x2, (int)y2, 128, 128, 179, 200,
                            (int)(10 * _settings.ZoomLevel));
                    }
                }
            }
        }
    }

    private void DrawOrogenicEvents(float contentStartY, float zoomedAgeScale)
    {
        foreach (var ev in _settings.OrogenicEvents)
        {
            if (ev.EndAge > _settings.MaxAge) continue;

            // The top of the event region is the younger age (EndAge)
            var yTop = contentStartY + (float)ev.EndAge * zoomedAgeScale;
            // The bottom of the region and the start line is the older age (StartAge)
            var yBottom = contentStartY + (float)ev.StartAge * zoomedAgeScale;

            var r = (byte)(ev.Color.X * 255);
            var g = (byte)(ev.Color.Y * 255);
            var b = (byte)(ev.Color.Z * 255);

            // Draw distinctive line at the start of the event (older boundary)
            FillRect(0, (int)yBottom, _width, 3, r, g, b, 255);

            // Draw label near the start line
            DrawText(ev.Name, 5, (int)yBottom - 15, r, g, b);

            // Draw zone for the duration
            if (yBottom - yTop > 5)
                // The drawing starts at yTop, and the height is the difference
                FillRect(0, (int)yTop, _width, (int)(yBottom - yTop), r, g, b, 25);
        }
    }

    private void DrawAgeScale(float contentStartY, float contentHeight, float zoomedAgeScale)
    {
        var scaleX = 5;
        var scaleWidth = 50;
        FillRect(scaleX, (int)contentStartY, scaleWidth, (int)contentHeight, 38, 38, 38, 255);

        var step = CalculateAgeStep(_settings.MaxAge);
        for (double age = 0; age <= _settings.MaxAge; age += step)
        {
            var y = contentStartY + (float)age * zoomedAgeScale;
            FillRect(scaleX + scaleWidth - 10, (int)y - 1, 10, 2, 255, 255, 255, 255);
            DrawText($"{age:F0}", scaleX + 5, (int)y - 4, 255, 255, 255);
        }

        DrawText("Ma", scaleX + 15, (int)contentStartY - 20, 0, 0, 0);
    }

    private List<string> WrapTextSimple(string text, int maxWidthInPixels)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        var words = text.Split(' ');
        var currentLine = "";

        foreach (var word in words)
        {
            var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            if (testLine.Length * 6 > maxWidthInPixels && !string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }

        if (!string.IsNullOrEmpty(currentLine)) lines.Add(currentLine);
        return lines;
    }

    private double CalculateAgeStep(double maxAge)
    {
        if (maxAge <= 10) return 1;
        if (maxAge <= 50) return 5;
        if (maxAge <= 100) return 10;
        if (maxAge <= 500) return 50;
        if (maxAge <= 1000) return 100;
        return 500;
    }
}