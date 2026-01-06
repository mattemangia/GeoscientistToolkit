// GeoscientistToolkit/Analysis/NMR/T1T2Computation.cs

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.NMR;

/// <summary>
///     Computes 2D T1-T2 correlation maps from NMR decay data.
///     T1-T2 maps help differentiate fluids and pore types.
/// </summary>
public static class T1T2Computation
{
    /// <summary>
    ///     Computes a 2D T1-T2 correlation map using the decay curve and T2 distribution.
    /// </summary>
    public static void ComputeT1T2Map(NMRResults results, NMRSimulationConfig config)
    {
        if (!config.ComputeT1T2Map || results.T2Histogram == null)
        {
            Logger.Log("[T1T2Computation] T1-T2 computation not enabled or T2 data missing");
            return;
        }

        Logger.Log("[T1T2Computation] Computing T1-T2 correlation map...");

        // Create T1 bins (logarithmic spacing)
        var logT1Min = Math.Log10(config.T1MinMs);
        var logT1Max = Math.Log10(config.T1MaxMs);
        var logT1Step = (logT1Max - logT1Min) / config.T1BinCount;

        results.T1HistogramBins = new double[config.T1BinCount];
        for (var i = 0; i < config.T1BinCount; i++) results.T1HistogramBins[i] = Math.Pow(10, logT1Min + i * logT1Step);

        // Initialize 2D map
        results.T1T2Map = new double[config.T1BinCount, config.T2BinCount];

        // For each T2 component, estimate corresponding T1 using T1/T2 ratio
        // In porous media, T1 and T2 are correlated: T1 ≈ α × T2, where α typically ranges from 1.5 to 3
        var t1t2Ratio = config.T1T2Ratio;

        for (var t2Idx = 0; t2Idx < results.T2HistogramBins.Length; t2Idx++)
        {
            var t2Value = results.T2HistogramBins[t2Idx];
            var amplitude = results.T2Histogram[t2Idx];

            if (amplitude < 1e-6) continue; // Skip negligible amplitudes

            // Estimate T1 from T2
            var estimatedT1 = t2Value * t1t2Ratio;

            // Add some spread around the T1/T2 correlation line to simulate realistic distributions
            // This accounts for the fact that T1/T2 ratio varies slightly within a pore size distribution
            var t1Spread = Math.Log10(estimatedT1) * 0.2; // 20% spread in log space

            for (var t1Idx = 0; t1Idx < results.T1HistogramBins.Length; t1Idx++)
            {
                var t1Value = results.T1HistogramBins[t1Idx];

                // Gaussian distribution around the estimated T1
                var logT1Actual = Math.Log10(t1Value);
                var logT1Expected = Math.Log10(estimatedT1);
                var distance = Math.Abs(logT1Actual - logT1Expected);

                if (distance < t1Spread)
                {
                    // Gaussian weight
                    var weight = Math.Exp(-0.5 * Math.Pow(distance / (t1Spread / 3), 2));
                    results.T1T2Map[t1Idx, t2Idx] += amplitude * weight;
                }
            }
        }

        // Normalize the 2D map
        var totalAmplitude = 0.0;
        for (var i = 0; i < config.T1BinCount; i++)
        for (var j = 0; j < config.T2BinCount; j++)
            totalAmplitude += results.T1T2Map[i, j];

        if (totalAmplitude > 0)
            for (var i = 0; i < config.T1BinCount; i++)
            for (var j = 0; j < config.T2BinCount; j++)
                results.T1T2Map[i, j] /= totalAmplitude;

        // Compute 1D T1 projection
        results.T1Histogram = new double[config.T1BinCount];
        for (var i = 0; i < config.T1BinCount; i++)
        for (var j = 0; j < config.T2BinCount; j++)
            results.T1Histogram[i] += results.T1T2Map[i, j];

        results.HasT1T2Data = true;

        Logger.Log($"[T1T2Computation] T1-T2 map computed: {config.T1BinCount}x{config.T2BinCount} resolution");
    }

    /// <summary>
    ///     Exports T1-T2 map data to CSV format.
    /// </summary>
    public static void ExportT1T2MapToCSV(NMRResults results, string filePath)
    {
        if (!results.HasT1T2Data || results.T1T2Map == null)
        {
            Logger.LogWarning("[T1T2Computation] No T1-T2 data to export");
            return;
        }

        using var writer = new StreamWriter(filePath);

        // Write header: T1 values as columns
        writer.Write("T2 \\ T1");
        for (var i = 0; i < results.T1HistogramBins.Length; i++) writer.Write($",{results.T1HistogramBins[i]:F3}");
        writer.WriteLine();

        // Write data: each row is a T2 value
        for (var j = 0; j < results.T2HistogramBins.Length; j++)
        {
            writer.Write($"{results.T2HistogramBins[j]:F3}");
            for (var i = 0; i < results.T1HistogramBins.Length; i++) writer.Write($",{results.T1T2Map[i, j]:E6}");
            writer.WriteLine();
        }

        Logger.Log($"[T1T2Computation] T1-T2 map exported to {filePath}");
    }

    /// <summary>
    ///     Renders T1-T2 correlation map as an image buffer (log-log scale).
    /// </summary>
    public static void RenderT1T2MapToBuffer(NMRResults results, byte[] buffer, int width, int height)
    {
        if (!results.HasT1T2Data || results.T1T2Map == null)
        {
            Logger.LogWarning("[T1T2Computation] No T1-T2 data to render");
            return;
        }

        var padding = 150;
        var plotWidth = width - padding * 2;
        var plotHeight = height - padding * 2;

        // Fill background
        for (var i = 0; i < width * height * 4; i += 4)
        {
            buffer[i] = 26; // R
            buffer[i + 1] = 26; // G
            buffer[i + 2] = 26; // B
            buffer[i + 3] = 255; // A
        }

        // Find max amplitude for color scaling
        var maxAmplitude = 0.0;
        for (var i = 0; i < results.T1T2Map.GetLength(0); i++)
        for (var j = 0; j < results.T1T2Map.GetLength(1); j++)
            maxAmplitude = Math.Max(maxAmplitude, results.T1T2Map[i, j]);

        if (maxAmplitude < 1e-10) return; // No data

        // Render 2D map as colored pixels
        var t1Count = results.T1T2Map.GetLength(0);
        var t2Count = results.T1T2Map.GetLength(1);

        var pixelWidth = plotWidth / t2Count;
        var pixelHeight = plotHeight / t1Count;

        for (var i = 0; i < t1Count; i++)
        for (var j = 0; j < t2Count; j++)
        {
            var amplitude = results.T1T2Map[i, j];
            if (amplitude < 1e-10) continue;

            // Normalize amplitude to 0-1
            var normalized = amplitude / maxAmplitude;

            // Use a color map (hot colormap: black -> red -> yellow -> white)
            var (r, g, b) = GetHotColor(normalized);

            var px = padding + j * pixelWidth;
            var py = padding + (t1Count - 1 - i) * pixelHeight; // Flip Y axis

            // Draw filled rectangle
            for (var dy = 0; dy < pixelHeight; dy++)
            for (var dx = 0; dx < pixelWidth; dx++)
            {
                var x = px + dx;
                var y = py + dy;
                if (x >= padding && x < padding + plotWidth && y >= padding && y < padding + plotHeight)
                {
                    var index = (y * width + x) * 4;
                    buffer[index] = r;
                    buffer[index + 1] = g;
                    buffer[index + 2] = b;
                    buffer[index + 3] = 255;
                }
            }
        }

        // Draw axes
        DrawLineInBuffer(buffer, width, padding, padding, padding, padding + plotHeight, 200, 200, 200);
        DrawLineInBuffer(buffer, width, padding, padding + plotHeight, padding + plotWidth, padding + plotHeight, 200,
            200, 200);

        // Draw T1/T2 correlation line (diagonal)
        var logT2Min = Math.Log10(results.T2HistogramBins[0]);
        var logT2Max = Math.Log10(results.T2HistogramBins[results.T2HistogramBins.Length - 1]);
        var logT1Min = Math.Log10(results.T1HistogramBins[0]);
        var logT1Max = Math.Log10(results.T1HistogramBins[results.T1HistogramBins.Length - 1]);

        for (var j = 0; j < t2Count - 1; j++)
        {
            var t2Value = results.T2HistogramBins[j];
            var t1Value = t2Value * 1.5; // T1/T2 ratio

            if (t1Value >= results.T1HistogramBins[0] &&
                t1Value <= results.T1HistogramBins[results.T1HistogramBins.Length - 1])
            {
                var x = padding + (int)((Math.Log10(t2Value) - logT2Min) / (logT2Max - logT2Min) * plotWidth);
                var y = padding + (int)((1.0 - (Math.Log10(t1Value) - logT1Min) / (logT1Max - logT1Min)) * plotHeight);

                SetPixel(buffer, width, x, y, 255, 255, 255);
                SetPixel(buffer, width, x + 1, y, 255, 255, 255);
                SetPixel(buffer, width, x, y + 1, 255, 255, 255);
            }
        }

        // Labels
        DrawTextInBuffer(buffer, width, width / 2 - 100, 50, "T1-T2 Correlation Map", 255, 255, 255);
        DrawTextInBuffer(buffer, width, width / 2 - 30, height - 80, "T2 (ms)", 255, 255, 255);
        DrawTextInBuffer(buffer, width, 40, height / 2, "T1 (ms)", 255, 255, 255);
    }

    private static (byte r, byte g, byte b) GetHotColor(double value)
    {
        // Hot colormap: black (0) -> red -> yellow -> white (1)
        value = Math.Clamp(value, 0, 1);

        byte r, g, b;

        if (value < 0.33)
        {
            // Black to red
            var t = value / 0.33;
            r = (byte)(t * 255);
            g = 0;
            b = 0;
        }
        else if (value < 0.66)
        {
            // Red to yellow
            var t = (value - 0.33) / 0.33;
            r = 255;
            g = (byte)(t * 255);
            b = 0;
        }
        else
        {
            // Yellow to white
            var t = (value - 0.66) / 0.34;
            r = 255;
            g = 255;
            b = (byte)(t * 255);
        }

        return (r, g, b);
    }

    // Helper functions (same as in NMRAnalysisTool)
    private static void DrawLineInBuffer(byte[] buffer, int width, int x0, int y0, int x1, int y1, byte r, byte g,
        byte b)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            SetPixel(buffer, width, x0, y0, r, g, b);
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

    private static void SetPixel(byte[] buffer, int width, int x, int y, byte r, byte g, byte b)
    {
        var height = buffer.Length / (width * 4);
        if (x < 0 || x >= width || y < 0 || y >= height) return;

        var index = (y * width + x) * 4;
        buffer[index] = r;
        buffer[index + 1] = g;
        buffer[index + 2] = b;
        buffer[index + 3] = 255;
    }

    private static void DrawTextInBuffer(byte[] buffer, int width, int x, int y, string text, byte r, byte g, byte b)
    {
        const int charWidth = 6;
        const int charHeight = 8;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var charX = x + i * charWidth;

            // Get bitmap for character
            var bitmap = GetCharBitmap(ch);

            for (var dy = 0; dy < charHeight; dy++)
            {
                byte row = bitmap[dy];
                for (var dx = 0; dx < charWidth - 1; dx++)
                {
                    if ((row & (0x80 >> dx)) != 0)
                        SetPixel(buffer, width, charX + dx, y + dy, r, g, b);
                }
            }
        }
    }

    /// <summary>
    /// Returns 8-byte bitmap for a character (5x7 font with 1 pixel spacing).
    /// Each byte represents a row, MSB first.
    /// </summary>
    private static byte[] GetCharBitmap(char ch)
    {
        return ch switch
        {
            '0' => new byte[] { 0x70, 0x88, 0x98, 0xA8, 0xC8, 0x88, 0x70, 0x00 },
            '1' => new byte[] { 0x20, 0x60, 0x20, 0x20, 0x20, 0x20, 0x70, 0x00 },
            '2' => new byte[] { 0x70, 0x88, 0x08, 0x10, 0x20, 0x40, 0xF8, 0x00 },
            '3' => new byte[] { 0xF8, 0x10, 0x20, 0x10, 0x08, 0x88, 0x70, 0x00 },
            '4' => new byte[] { 0x10, 0x30, 0x50, 0x90, 0xF8, 0x10, 0x10, 0x00 },
            '5' => new byte[] { 0xF8, 0x80, 0xF0, 0x08, 0x08, 0x88, 0x70, 0x00 },
            '6' => new byte[] { 0x30, 0x40, 0x80, 0xF0, 0x88, 0x88, 0x70, 0x00 },
            '7' => new byte[] { 0xF8, 0x08, 0x10, 0x20, 0x40, 0x40, 0x40, 0x00 },
            '8' => new byte[] { 0x70, 0x88, 0x88, 0x70, 0x88, 0x88, 0x70, 0x00 },
            '9' => new byte[] { 0x70, 0x88, 0x88, 0x78, 0x08, 0x10, 0x60, 0x00 },
            'A' => new byte[] { 0x70, 0x88, 0x88, 0xF8, 0x88, 0x88, 0x88, 0x00 },
            'B' => new byte[] { 0xF0, 0x88, 0x88, 0xF0, 0x88, 0x88, 0xF0, 0x00 },
            'C' => new byte[] { 0x70, 0x88, 0x80, 0x80, 0x80, 0x88, 0x70, 0x00 },
            'D' => new byte[] { 0xE0, 0x90, 0x88, 0x88, 0x88, 0x90, 0xE0, 0x00 },
            'E' => new byte[] { 0xF8, 0x80, 0x80, 0xF0, 0x80, 0x80, 0xF8, 0x00 },
            'F' => new byte[] { 0xF8, 0x80, 0x80, 0xF0, 0x80, 0x80, 0x80, 0x00 },
            'G' => new byte[] { 0x70, 0x88, 0x80, 0xB8, 0x88, 0x88, 0x70, 0x00 },
            'H' => new byte[] { 0x88, 0x88, 0x88, 0xF8, 0x88, 0x88, 0x88, 0x00 },
            'I' => new byte[] { 0x70, 0x20, 0x20, 0x20, 0x20, 0x20, 0x70, 0x00 },
            'J' => new byte[] { 0x38, 0x10, 0x10, 0x10, 0x10, 0x90, 0x60, 0x00 },
            'K' => new byte[] { 0x88, 0x90, 0xA0, 0xC0, 0xA0, 0x90, 0x88, 0x00 },
            'L' => new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0xF8, 0x00 },
            'M' => new byte[] { 0x88, 0xD8, 0xA8, 0xA8, 0x88, 0x88, 0x88, 0x00 },
            'N' => new byte[] { 0x88, 0xC8, 0xA8, 0x98, 0x88, 0x88, 0x88, 0x00 },
            'O' => new byte[] { 0x70, 0x88, 0x88, 0x88, 0x88, 0x88, 0x70, 0x00 },
            'P' => new byte[] { 0xF0, 0x88, 0x88, 0xF0, 0x80, 0x80, 0x80, 0x00 },
            'Q' => new byte[] { 0x70, 0x88, 0x88, 0x88, 0xA8, 0x90, 0x68, 0x00 },
            'R' => new byte[] { 0xF0, 0x88, 0x88, 0xF0, 0xA0, 0x90, 0x88, 0x00 },
            'S' => new byte[] { 0x70, 0x88, 0x80, 0x70, 0x08, 0x88, 0x70, 0x00 },
            'T' => new byte[] { 0xF8, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x00 },
            'U' => new byte[] { 0x88, 0x88, 0x88, 0x88, 0x88, 0x88, 0x70, 0x00 },
            'V' => new byte[] { 0x88, 0x88, 0x88, 0x88, 0x50, 0x50, 0x20, 0x00 },
            'W' => new byte[] { 0x88, 0x88, 0x88, 0xA8, 0xA8, 0xD8, 0x88, 0x00 },
            'X' => new byte[] { 0x88, 0x88, 0x50, 0x20, 0x50, 0x88, 0x88, 0x00 },
            'Y' => new byte[] { 0x88, 0x88, 0x50, 0x20, 0x20, 0x20, 0x20, 0x00 },
            'Z' => new byte[] { 0xF8, 0x08, 0x10, 0x20, 0x40, 0x80, 0xF8, 0x00 },
            'a' => new byte[] { 0x00, 0x00, 0x70, 0x08, 0x78, 0x88, 0x78, 0x00 },
            'b' => new byte[] { 0x80, 0x80, 0xB0, 0xC8, 0x88, 0x88, 0xF0, 0x00 },
            'c' => new byte[] { 0x00, 0x00, 0x70, 0x80, 0x80, 0x88, 0x70, 0x00 },
            'd' => new byte[] { 0x08, 0x08, 0x68, 0x98, 0x88, 0x88, 0x78, 0x00 },
            'e' => new byte[] { 0x00, 0x00, 0x70, 0x88, 0xF8, 0x80, 0x70, 0x00 },
            'f' => new byte[] { 0x30, 0x48, 0x40, 0xE0, 0x40, 0x40, 0x40, 0x00 },
            'g' => new byte[] { 0x00, 0x00, 0x78, 0x88, 0x78, 0x08, 0x70, 0x00 },
            'h' => new byte[] { 0x80, 0x80, 0xB0, 0xC8, 0x88, 0x88, 0x88, 0x00 },
            'i' => new byte[] { 0x20, 0x00, 0x60, 0x20, 0x20, 0x20, 0x70, 0x00 },
            'j' => new byte[] { 0x10, 0x00, 0x30, 0x10, 0x10, 0x90, 0x60, 0x00 },
            'k' => new byte[] { 0x80, 0x80, 0x90, 0xA0, 0xC0, 0xA0, 0x90, 0x00 },
            'l' => new byte[] { 0x60, 0x20, 0x20, 0x20, 0x20, 0x20, 0x70, 0x00 },
            'm' => new byte[] { 0x00, 0x00, 0xD0, 0xA8, 0xA8, 0xA8, 0xA8, 0x00 },
            'n' => new byte[] { 0x00, 0x00, 0xB0, 0xC8, 0x88, 0x88, 0x88, 0x00 },
            'o' => new byte[] { 0x00, 0x00, 0x70, 0x88, 0x88, 0x88, 0x70, 0x00 },
            'p' => new byte[] { 0x00, 0x00, 0xF0, 0x88, 0xF0, 0x80, 0x80, 0x00 },
            'q' => new byte[] { 0x00, 0x00, 0x78, 0x88, 0x78, 0x08, 0x08, 0x00 },
            'r' => new byte[] { 0x00, 0x00, 0xB0, 0xC8, 0x80, 0x80, 0x80, 0x00 },
            's' => new byte[] { 0x00, 0x00, 0x78, 0x80, 0x70, 0x08, 0xF0, 0x00 },
            't' => new byte[] { 0x40, 0x40, 0xE0, 0x40, 0x40, 0x48, 0x30, 0x00 },
            'u' => new byte[] { 0x00, 0x00, 0x88, 0x88, 0x88, 0x98, 0x68, 0x00 },
            'v' => new byte[] { 0x00, 0x00, 0x88, 0x88, 0x88, 0x50, 0x20, 0x00 },
            'w' => new byte[] { 0x00, 0x00, 0x88, 0xA8, 0xA8, 0xA8, 0x50, 0x00 },
            'x' => new byte[] { 0x00, 0x00, 0x88, 0x50, 0x20, 0x50, 0x88, 0x00 },
            'y' => new byte[] { 0x00, 0x00, 0x88, 0x88, 0x78, 0x08, 0x70, 0x00 },
            'z' => new byte[] { 0x00, 0x00, 0xF8, 0x10, 0x20, 0x40, 0xF8, 0x00 },
            ' ' => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            '-' => new byte[] { 0x00, 0x00, 0x00, 0xF8, 0x00, 0x00, 0x00, 0x00 },
            '+' => new byte[] { 0x00, 0x20, 0x20, 0xF8, 0x20, 0x20, 0x00, 0x00 },
            '.' => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x60, 0x60, 0x00 },
            ',' => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x30, 0x30, 0x20, 0x40 },
            ':' => new byte[] { 0x00, 0x60, 0x60, 0x00, 0x60, 0x60, 0x00, 0x00 },
            '/' => new byte[] { 0x08, 0x08, 0x10, 0x20, 0x40, 0x80, 0x80, 0x00 },
            '(' => new byte[] { 0x10, 0x20, 0x40, 0x40, 0x40, 0x20, 0x10, 0x00 },
            ')' => new byte[] { 0x40, 0x20, 0x10, 0x10, 0x10, 0x20, 0x40, 0x00 },
            '[' => new byte[] { 0x70, 0x40, 0x40, 0x40, 0x40, 0x40, 0x70, 0x00 },
            ']' => new byte[] { 0x70, 0x10, 0x10, 0x10, 0x10, 0x10, 0x70, 0x00 },
            '=' => new byte[] { 0x00, 0x00, 0xF8, 0x00, 0xF8, 0x00, 0x00, 0x00 },
            _ => new byte[] { 0xF8, 0x88, 0x88, 0x88, 0x88, 0x88, 0xF8, 0x00 } // Default box
        };
    }
}