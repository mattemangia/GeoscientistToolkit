// GeoscientistToolkit/Util/ImageDecoder.cs

using StbImageSharp;

namespace GeoscientistToolkit.Util;

/// <summary>
///     Image decoder using StbImageSharp for PNG, JPEG, BMP, TGA, PSD, GIF support
/// </summary>
public static class ImageDecoder
{
    static ImageDecoder()
    {
        // Configure StbImage to use standard coordinate system (top-left origin)
        StbImage.stbi_set_flip_vertically_on_load(0);
    }

    public static (byte[] pixelData, uint width, uint height) DecodePng(byte[] pngData)
    {
        return DecodeImageInternal(pngData);
    }

    public static (byte[] pixelData, uint width, uint height) DecodeJpeg(byte[] jpegData)
    {
        return DecodeImageInternal(jpegData);
    }

    public static (byte[] pixelData, uint width, uint height) DecodeImage(byte[] imageData)
    {
        return DecodeImageInternal(imageData);
    }

    private static (byte[] pixelData, uint width, uint height) DecodeImageInternal(byte[] imageData)
    {
        try
        {
            // Use StbImageSharp to decode the image
            // Request RGBA format (4 components)
            var result = ImageResult.FromMemory(imageData, ColorComponents.RedGreenBlueAlpha);

            if (result == null)
            {
                Logger.LogWarning("Failed to decode image, using placeholder");
                return CreatePlaceholderImage(256, 256, 128, 128, 128);
            }

            // StbImageSharp returns data in the format we need (RGBA)
            return (result.Data, (uint)result.Width, (uint)result.Height);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to decode image: {ex.Message}, using placeholder");
            return CreatePlaceholderImage(256, 256, 100, 100, 100);
        }
    }

    public static (byte[] pixelData, uint width, uint height) LoadImageFromFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Logger.LogWarning($"Image file not found: {path}");
                return CreatePlaceholderImage(256, 256, 64, 64, 64);
            }

            var imageData = File.ReadAllBytes(path);
            return DecodeImageInternal(imageData);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load image from file: {ex.Message}");
            return CreatePlaceholderImage(256, 256, 64, 64, 64);
        }
    }

    public static (byte[] pixelData, uint width, uint height) CreatePlaceholderImage(
        uint width, uint height, byte r, byte g, byte b)
    {
        var pixelData = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = (y * (int)width + x) * 4;

            // Create checkerboard pattern
            var isLight = (x / 32 + y / 32) % 2 == 0;

            pixelData[idx] = isLight ? r : (byte)(r / 2);
            pixelData[idx + 1] = isLight ? g : (byte)(g / 2);
            pixelData[idx + 2] = isLight ? b : (byte)(b / 2);
            pixelData[idx + 3] = 255;
        }

        return (pixelData, width, height);
    }

    /// <summary>
    ///     Get image information without fully decoding
    /// </summary>
    public static (int width, int height, int components) GetImageInfo(byte[] imageData)
    {
        if (imageData == null || imageData.Length == 0)
            return (0, 0, 0);

        try
        {
            using var ms = new MemoryStream(imageData, false);
            var info = ImageInfo.FromStream(ms); // metadata-only

            if (info.HasValue)
            {
                var ii = info.Value;
                var comps = ii.ColorComponents == ColorComponents.Default ? 0 : (int)ii.ColorComponents;
                return (ii.Width, ii.Height, comps);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to get image info: {ex.Message}");
        }

        return (0, 0, 0);
    }
}

/// <summary>
///     Coordinate conversion utilities
/// </summary>
public static class CoordinateConverter
{
    public static DMS ToDMS(double decimalDegrees)
    {
        var isNegative = decimalDegrees < 0;
        var absValue = Math.Abs(decimalDegrees);

        var degrees = (int)absValue;
        var minutesDecimal = (absValue - degrees) * 60;
        var minutes = (int)minutesDecimal;
        var seconds = (minutesDecimal - minutes) * 60;

        return new DMS
        {
            Degrees = isNegative ? -degrees : degrees,
            Minutes = minutes,
            Seconds = seconds,
            IsNegative = isNegative
        };
    }

    public static double FromDMS(int degrees, int minutes, double seconds)
    {
        var result = Math.Abs(degrees) + minutes / 60.0 + seconds / 3600.0;
        return degrees < 0 ? -result : result;
    }

    public static string FormatDMS(double decimalDegrees, bool isLongitude)
    {
        var dms = ToDMS(decimalDegrees);
        var direction = "";

        if (isLongitude)
            direction = dms.IsNegative ? "W" : "E";
        else
            direction = dms.IsNegative ? "S" : "N";

        return $"{Math.Abs(dms.Degrees)}°{dms.Minutes:00}'{dms.Seconds:00.00}\"{direction}";
    }

    public static string FormatDM(double decimalDegrees, bool isLongitude)
    {
        var isNegative = decimalDegrees < 0;
        var absValue = Math.Abs(decimalDegrees);

        var degrees = (int)absValue;
        var minutes = (absValue - degrees) * 60;

        var direction = "";
        if (isLongitude)
            direction = isNegative ? "W" : "E";
        else
            direction = isNegative ? "S" : "N";

        return $"{degrees}°{minutes:00.0000}'{direction}";
    }

    public static string FormatDecimal(double decimalDegrees, bool isLongitude)
    {
        var suffix = "°";
        return $"{decimalDegrees:F6}{suffix}";
    }
}

public struct DMS
{
    public int Degrees { get; set; }
    public int Minutes { get; set; }
    public double Seconds { get; set; }
    public bool IsNegative { get; set; }
}