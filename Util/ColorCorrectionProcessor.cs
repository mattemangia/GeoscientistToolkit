// GeoscientistToolkit/Business/Image/ColorCorrectionProcessor.cs

using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.Image;

/// <summary>
///     Advanced color correction and enhancement for satellite imagery
/// </summary>
public static class ColorCorrectionProcessor
{
    /// <summary>
    ///     Apply complete color correction pipeline
    /// </summary>
    public static ImageDataset ApplyCorrection(ImageDataset source, ColorCorrection correction)
    {
        Logger.Log($"Applying color correction to {source.Name}");

        if (source.ImageData == null)
            source.Load();

        var corrected = new byte[source.ImageData.Length];
        Array.Copy(source.ImageData, corrected, source.ImageData.Length);

        // Apply corrections in order
        if (correction.AutoBalance) corrected = AutoWhiteBalance(corrected, source.Width, source.Height);

        if (correction.AutoContrast) corrected = AutoContrast(corrected, source.Width, source.Height);

        corrected = AdjustBrightnessContrastGamma(corrected, source.Width, source.Height,
            correction.Brightness, correction.Contrast, correction.Gamma);

        if (Math.Abs(correction.Saturation - 1f) > 0.01f)
            corrected = AdjustSaturation(corrected, source.Width, source.Height, correction.Saturation);

        var result = new ImageDataset($"{source.Name}_Corrected", "")
        {
            Width = source.Width,
            Height = source.Height,
            ImageData = corrected,
            PixelSize = source.PixelSize,
            Unit = source.Unit
        };

        result.Tags = source.Tags;

        Logger.Log("Color correction complete");
        return result;
    }

    /// <summary>
    ///     Automatic white balance using gray world assumption
    /// </summary>
    public static byte[] AutoWhiteBalance(byte[] data, int width, int height)
    {
        var pixelCount = width * height;

        // Calculate average of each channel
        double avgR = 0, avgG = 0, avgB = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;
            avgR += data[idx + 0];
            avgG += data[idx + 1];
            avgB += data[idx + 2];
        }

        avgR /= pixelCount;
        avgG /= pixelCount;
        avgB /= pixelCount;

        // Calculate overall average (gray)
        var avgGray = (avgR + avgG + avgB) / 3.0;

        // Calculate correction factors
        var factorR = avgGray / avgR;
        var factorG = avgGray / avgG;
        var factorB = avgGray / avgB;

        Logger.Log($"White balance factors: R={factorR:F3}, G={factorG:F3}, B={factorB:F3}");

        // Apply correction
        var corrected = new byte[data.Length];

        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;

            corrected[idx + 0] = ClampByte(data[idx + 0] * factorR);
            corrected[idx + 1] = ClampByte(data[idx + 1] * factorG);
            corrected[idx + 2] = ClampByte(data[idx + 2] * factorB);
            corrected[idx + 3] = data[idx + 3]; // Preserve alpha
        }

        return corrected;
    }

    /// <summary>
    ///     Automatic contrast stretch to full 0-255 range
    /// </summary>
    public static byte[] AutoContrast(byte[] data, int width, int height)
    {
        var pixelCount = width * height;

        // Find min/max for each channel
        byte minR = 255, minG = 255, minB = 255;
        byte maxR = 0, maxG = 0, maxB = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;

            minR = Math.Min(minR, data[idx + 0]);
            minG = Math.Min(minG, data[idx + 1]);
            minB = Math.Min(minB, data[idx + 2]);

            maxR = Math.Max(maxR, data[idx + 0]);
            maxG = Math.Max(maxG, data[idx + 1]);
            maxB = Math.Max(maxB, data[idx + 2]);
        }

        Logger.Log($"Auto-contrast range: R=[{minR},{maxR}], G=[{minG},{maxG}], B=[{minB},{maxB}]");

        // Apply stretch
        var corrected = new byte[data.Length];

        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;

            corrected[idx + 0] = StretchValue(data[idx + 0], minR, maxR);
            corrected[idx + 1] = StretchValue(data[idx + 1], minG, maxG);
            corrected[idx + 2] = StretchValue(data[idx + 2], minB, maxB);
            corrected[idx + 3] = data[idx + 3];
        }

        return corrected;
    }

    /// <summary>
    ///     Histogram equalization for improved contrast
    /// </summary>
    public static byte[] HistogramEqualization(byte[] data, int width, int height)
    {
        var pixelCount = width * height;

        // Calculate histogram for luminance
        var histogram = new int[256];

        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;
            var r = data[idx + 0];
            var g = data[idx + 1];
            var b = data[idx + 2];

            // Calculate luminance
            var lum = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            histogram[lum]++;
        }

        // Calculate cumulative distribution function (CDF)
        var cdf = new int[256];
        cdf[0] = histogram[0];

        for (var i = 1; i < 256; i++) cdf[i] = cdf[i - 1] + histogram[i];

        // Normalize CDF
        var cdfMin = cdf.First(c => c > 0);
        var lookupTable = new byte[256];

        for (var i = 0; i < 256; i++) lookupTable[i] = (byte)((cdf[i] - cdfMin) * 255.0 / (pixelCount - cdfMin));

        // Apply equalization
        var corrected = new byte[data.Length];

        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;
            var r = data[idx + 0];
            var g = data[idx + 1];
            var b = data[idx + 2];

            // Calculate original luminance
            var oldLum = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            var newLum = lookupTable[oldLum];

            // Scale RGB by luminance ratio
            var ratio = oldLum > 0 ? newLum / (float)oldLum : 1f;

            corrected[idx + 0] = ClampByte(r * ratio);
            corrected[idx + 1] = ClampByte(g * ratio);
            corrected[idx + 2] = ClampByte(b * ratio);
            corrected[idx + 3] = data[idx + 3];
        }

        return corrected;
    }

    /// <summary>
    ///     Adjust brightness, contrast, and gamma
    /// </summary>
    public static byte[] AdjustBrightnessContrastGamma(byte[] data, int width, int height,
        float brightness, float contrast, float gamma)
    {
        var pixelCount = width * height;
        var corrected = new byte[data.Length];

        // Pre-calculate gamma lookup table for performance
        var gammaTable = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var normalized = i / 255f;
            var gammaCorrected = MathF.Pow(normalized, 1f / gamma);
            gammaTable[i] = (byte)(gammaCorrected * 255f);
        }

        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;

            for (var c = 0; c < 3; c++)
            {
                var value = data[idx + c] / 255f;

                // Apply brightness
                value += brightness;

                // Apply contrast (pivot around 0.5)
                value = (value - 0.5f) * contrast + 0.5f;

                // Apply gamma using lookup table
                var gammaCorrectedValue = ClampInt((int)(value * 255f), 0, 255);
                corrected[idx + c] = gammaTable[gammaCorrectedValue];
            }

            corrected[idx + 3] = data[idx + 3]; // Preserve alpha
        }

        return corrected;
    }

    /// <summary>
    ///     Adjust color saturation
    /// </summary>
    public static byte[] AdjustSaturation(byte[] data, int width, int height, float saturation)
    {
        var pixelCount = width * height;
        var corrected = new byte[data.Length];

        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;

            var r = data[idx + 0];
            var g = data[idx + 1];
            var b = data[idx + 2];

            // Calculate luminance (grayscale)
            var lum = 0.299f * r + 0.587f * g + 0.114f * b;

            // Interpolate between grayscale and original color
            corrected[idx + 0] = ClampByte(lum + (r - lum) * saturation);
            corrected[idx + 1] = ClampByte(lum + (g - lum) * saturation);
            corrected[idx + 2] = ClampByte(lum + (b - lum) * saturation);
            corrected[idx + 3] = data[idx + 3];
        }

        return corrected;
    }

    /// <summary>
    ///     Apply atmospheric haze reduction (dehaze)
    /// </summary>
    public static byte[] Dehaze(byte[] data, int width, int height, float strength = 0.5f)
    {
        // Dark channel prior algorithm (simplified)
        var pixelCount = width * height;
        var corrected = new byte[data.Length];

        // Estimate atmospheric light (brightest pixels)
        var topPercentCount = pixelCount / 100; // Top 1%
        var brightnesses = new List<(int index, float brightness)>();

        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;
            var brightness = (data[idx] + data[idx + 1] + data[idx + 2]) / 3f;
            brightnesses.Add((i, brightness));
        }

        var atmosphericLight = brightnesses.OrderByDescending(b => b.brightness)
            .Take(topPercentCount)
            .Average(b => b.brightness);

        Logger.Log($"Estimated atmospheric light: {atmosphericLight:F1}");

        // Apply dehaze
        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;

            for (var c = 0; c < 3; c++)
            {
                float original = data[idx + c];
                var transmission = 1 - strength * (1 - original / atmosphericLight);
                var dehazed = (original - atmosphericLight * (1 - transmission)) / Math.Max(transmission, 0.1f);

                corrected[idx + c] = ClampByte(dehazed);
            }

            corrected[idx + 3] = data[idx + 3];
        }

        return corrected;
    }

    /// <summary>
    ///     Sharpen image using unsharp mask
    /// </summary>
    public static byte[] Sharpen(byte[] data, int width, int height, float amount = 1f, float radius = 1f)
    {
        // Simple 3x3 sharpening kernel
        float[,] kernel =
        {
            { 0, -1, 0 },
            { -1, 5, -1 },
            { 0, -1, 0 }
        };

        // Scale kernel by amount
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            kernel[i, j] *= amount;

        return ApplyConvolution(data, width, height, kernel);
    }

    /// <summary>
    ///     Apply convolution kernel to image
    /// </summary>
    private static byte[] ApplyConvolution(byte[] data, int width, int height, float[,] kernel)
    {
        var kernelSize = kernel.GetLength(0);
        var offset = kernelSize / 2;

        var result = new byte[data.Length];
        Array.Copy(data, result, data.Length); // Copy original first

        for (var y = offset; y < height - offset; y++)
        for (var x = offset; x < width - offset; x++)
        for (var c = 0; c < 3; c++)
        {
            float sum = 0;

            for (var ky = 0; ky < kernelSize; ky++)
            for (var kx = 0; kx < kernelSize; kx++)
            {
                var px = x + kx - offset;
                var py = y + ky - offset;
                var idx = (py * width + px) * 4 + c;

                sum += data[idx] * kernel[ky, kx];
            }

            var resultIdx = (y * width + x) * 4 + c;
            result[resultIdx] = ClampByte(sum);
        }

        return result;
    }

    private static byte StretchValue(byte value, byte min, byte max)
    {
        if (max == min)
            return value;

        return (byte)((value - min) * 255.0 / (max - min));
    }

    private static byte ClampByte(double value)
    {
        return (byte)Math.Clamp((int)value, 0, 255);
    }

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Clamp(value, min, max);
    }
}