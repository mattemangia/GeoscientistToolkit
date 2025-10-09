// GeoscientistToolkit/Business/Image/SatelliteBandComposer.cs

using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.Image;

/// <summary>
///     Compose RGB images from satellite multispectral bands
/// </summary>
public class SatelliteBandComposer
{
    /// <summary>
    ///     Compose RGB image from individual band images
    /// </summary>
    public static ImageDataset ComposeRGB(
        List<ImageDataset> bands,
        BandComposite composition,
        ColorCorrection correction = null)
    {
        if (bands == null || bands.Count == 0)
            throw new ArgumentException("No bands provided");

        // Validate all bands have same dimensions
        var width = bands[0].Width;
        var height = bands[0].Height;

        if (bands.Any(b => b.Width != width || b.Height != height))
            throw new ArgumentException("All bands must have the same dimensions");

        Logger.Log($"Composing RGB image: {composition.Name}");
        Logger.Log($"Dimensions: {width}x{height}");

        // Create output image
        var rgbData = new byte[width * height * 4]; // RGBA

        // Get band data
        var redBand = GetBandData(bands, composition.RedBand);
        var greenBand = GetBandData(bands, composition.GreenBand);
        var blueBand = GetBandData(bands, composition.BlueBand);

        if (redBand == null || greenBand == null || blueBand == null)
            throw new ArgumentException("Required bands not found");

        // Apply correction if provided
        if (correction != null)
        {
            redBand = ApplyCorrection(redBand, correction);
            greenBand = ApplyCorrection(greenBand, correction);
            blueBand = ApplyCorrection(blueBand, correction);
        }

        // Combine bands into RGB
        for (var i = 0; i < width * height; i++)
        {
            rgbData[i * 4 + 0] = redBand[i]; // R
            rgbData[i * 4 + 1] = greenBand[i]; // G
            rgbData[i * 4 + 2] = blueBand[i]; // B
            rgbData[i * 4 + 3] = 255; // A
        }

        // Create output dataset
        var composedDataset = new ImageDataset($"Composed_{composition.Name}", "")
        {
            Width = width,
            Height = height,
            ImageData = rgbData
        };

        composedDataset.AddTag(ImageTag.Satellite);
        composedDataset.AddTag(ImageTag.Multispectral);

        Logger.Log("RGB composition complete");
        return composedDataset;
    }

    /// <summary>
    ///     Compose RGB from single multi-band image
    /// </summary>
    public static ImageDataset ComposeRGBFromMultiBand(
        byte[][,] allBands,
        BandComposite composition,
        ColorCorrection correction = null)
    {
        if (allBands == null || allBands.Length == 0)
            throw new ArgumentException("No bands provided");

        var height = allBands[0].GetLength(0);
        var width = allBands[0].GetLength(1);

        Logger.Log($"Composing RGB from multi-band image: {composition.Name}");

        // Extract required bands
        var redBand = ExtractBandAs1D(allBands, composition.RedBand, width, height);
        var greenBand = ExtractBandAs1D(allBands, composition.GreenBand, width, height);
        var blueBand = ExtractBandAs1D(allBands, composition.BlueBand, width, height);

        // Apply correction
        if (correction != null)
        {
            redBand = ApplyCorrection(redBand, correction);
            greenBand = ApplyCorrection(greenBand, correction);
            blueBand = ApplyCorrection(blueBand, correction);
        }

        // Combine into RGBA
        var rgbData = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgbData[i * 4 + 0] = redBand[i];
            rgbData[i * 4 + 1] = greenBand[i];
            rgbData[i * 4 + 2] = blueBand[i];
            rgbData[i * 4 + 3] = 255;
        }

        var composedDataset = new ImageDataset($"Composed_{composition.Name}", "")
        {
            Width = width,
            Height = height,
            ImageData = rgbData
        };

        composedDataset.AddTag(ImageTag.Satellite);

        return composedDataset;
    }

    /// <summary>
    ///     Auto-detect satellite type and suggest appropriate band combination
    /// </summary>
    public static BandComposite DetectBandCombination(List<ImageDataset> bands)
    {
        var bandCount = bands.Count;

        // Check metadata for satellite info
        foreach (var band in bands)
            if (band.ImageMetadata.ContainsKey("satellite"))
            {
                var satellite = band.ImageMetadata["satellite"].ToString().ToLower();

                if (satellite.Contains("landsat 8") || satellite.Contains("landsat 9"))
                    return BandCombinations.Landsat8TrueColor;

                if (satellite.Contains("landsat 7"))
                    return BandCombinations.Landsat7TrueColor;

                if (satellite.Contains("sentinel-2") || satellite.Contains("sentinel2"))
                    return BandCombinations.Sentinel2TrueColor;

                if (satellite.Contains("modis"))
                    return BandCombinations.ModisTrueColor;
            }

        // Default to generic RGB (1,2,3)
        Logger.LogWarning("Could not detect satellite type, using generic RGB combination");
        return new BandComposite
        {
            Name = "Generic RGB",
            RedBand = 3,
            GreenBand = 2,
            BlueBand = 1
        };
    }

    private static byte[] GetBandData(List<ImageDataset> bands, int bandNumber)
    {
        // Find band by number (could be in metadata or filename)
        foreach (var band in bands)
            if (IsBandNumber(band, bandNumber))
                return ExtractGrayscaleData(band);

        // If not found by number, try by index
        if (bandNumber > 0 && bandNumber <= bands.Count) return ExtractGrayscaleData(bands[bandNumber - 1]);

        return null;
    }

    private static bool IsBandNumber(ImageDataset dataset, int number)
    {
        // Check metadata
        if (dataset.ImageMetadata.ContainsKey("band_number"))
            if (int.TryParse(dataset.ImageMetadata["band_number"].ToString(), out var bandNum))
                return bandNum == number;

        // Check filename (e.g., "LC08_B4.TIF" -> band 4)
        var name = Path.GetFileNameWithoutExtension(dataset.Name).ToUpper();
        return name.Contains($"B{number}") || name.Contains($"BAND{number}");
    }

    private static byte[] ExtractGrayscaleData(ImageDataset dataset)
    {
        if (dataset.ImageData == null)
            dataset.Load();

        var pixelCount = dataset.Width * dataset.Height;
        var grayscale = new byte[pixelCount];

        // Extract luminance from RGBA
        for (var i = 0; i < pixelCount; i++)
        {
            var idx = i * 4;
            var r = dataset.ImageData[idx + 0];
            var g = dataset.ImageData[idx + 1];
            var b = dataset.ImageData[idx + 2];

            // Use luminance formula
            grayscale[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
        }

        return grayscale;
    }

    private static byte[] ExtractBandAs1D(byte[][,] allBands, int bandIndex, int width, int height)
    {
        if (bandIndex < 0 || bandIndex >= allBands.Length)
            throw new ArgumentException($"Band index {bandIndex} out of range");

        var band = allBands[bandIndex];
        var data = new byte[width * height];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            data[y * width + x] = band[y, x];

        return data;
    }

    private static byte[] ApplyCorrection(byte[] band, ColorCorrection correction)
    {
        var corrected = new byte[band.Length];

        for (var i = 0; i < band.Length; i++)
        {
            var value = band[i] / 255f;

            // Apply brightness
            value += correction.Brightness;

            // Apply contrast
            value = (value - 0.5f) * correction.Contrast + 0.5f;

            // Apply gamma
            value = MathF.Pow(value, 1f / correction.Gamma);

            // Clamp
            corrected[i] = (byte)Math.Clamp(value * 255f, 0, 255);
        }

        return corrected;
    }

    /// <summary>
    ///     Predefined band combinations for common satellites
    /// </summary>
    public static class BandCombinations
    {
        // Landsat 8/9 OLI
        public static readonly BandComposite Landsat8TrueColor = new()
        {
            Name = "Landsat 8/9 - True Color",
            RedBand = 4, // Red
            GreenBand = 3, // Green
            BlueBand = 2 // Blue
        };

        public static readonly BandComposite Landsat8FalseColor = new()
        {
            Name = "Landsat 8/9 - False Color (Vegetation)",
            RedBand = 5, // NIR
            GreenBand = 4, // Red
            BlueBand = 3 // Green
        };

        public static readonly BandComposite Landsat8Agriculture = new()
        {
            Name = "Landsat 8/9 - Agriculture",
            RedBand = 6, // SWIR1
            GreenBand = 5, // NIR
            BlueBand = 2 // Blue
        };

        // Landsat 7 ETM+
        public static readonly BandComposite Landsat7TrueColor = new()
        {
            Name = "Landsat 7 - True Color",
            RedBand = 3, // Red
            GreenBand = 2, // Green
            BlueBand = 1 // Blue
        };

        // Sentinel-2
        public static readonly BandComposite Sentinel2TrueColor = new()
        {
            Name = "Sentinel-2 - True Color",
            RedBand = 4, // Red (B4)
            GreenBand = 3, // Green (B3)
            BlueBand = 2 // Blue (B2)
        };

        public static readonly BandComposite Sentinel2FalseColor = new()
        {
            Name = "Sentinel-2 - False Color (Vegetation)",
            RedBand = 8, // NIR (B8)
            GreenBand = 4, // Red (B4)
            BlueBand = 3 // Green (B3)
        };

        // MODIS
        public static readonly BandComposite ModisTrueColor = new()
        {
            Name = "MODIS - True Color",
            RedBand = 1, // Red
            GreenBand = 4, // Green
            BlueBand = 3 // Blue
        };

        public static List<BandComposite> GetAllCombinations()
        {
            return new List<BandComposite>
            {
                Landsat8TrueColor,
                Landsat8FalseColor,
                Landsat8Agriculture,
                Landsat7TrueColor,
                Sentinel2TrueColor,
                Sentinel2FalseColor,
                ModisTrueColor
            };
        }
    }
}

public class BandComposite
{
    public string Name { get; set; }
    public int RedBand { get; set; }
    public int GreenBand { get; set; }
    public int BlueBand { get; set; }
    public string Description { get; set; }
}

public class ColorCorrection
{
    public float Brightness { get; set; } = 0f; // -1 to 1
    public float Contrast { get; set; } = 1f; // 0 to 2
    public float Gamma { get; set; } = 1f; // 0.1 to 3
    public float Saturation { get; set; } = 1f; // 0 to 2
    public bool AutoBalance { get; set; } = false; // Histogram equalization
    public bool AutoContrast { get; set; } = false; // Stretch to full range

    public static ColorCorrection Default => new();
}