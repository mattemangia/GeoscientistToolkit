// GeoscientistToolkit/Business/GeoScript/GeoScriptImageCommands.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptImageCommands;

#region Image Processing Commands

/// <summary>
/// BRIGHTNESS_CONTRAST command - Adjust brightness and contrast
/// Usage: BRIGHTNESS_CONTRAST brightness=50 contrast=1.5
/// Usage: BRIGHTNESS_CONTRAST brightness=50
/// Usage: BRIGHTNESS_CONTRAST contrast=1.5
/// </summary>
public class BrightnessContrastCommand : IGeoScriptCommand
{
    public string Name => "BRIGHTNESS_CONTRAST";
    public string HelpText => "Adjust brightness and contrast of an image dataset";
    public string Usage => "BRIGHTNESS_CONTRAST brightness=<-100 to 100> contrast=<0.1 to 3.0>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not ImageDataset imageDataset)
            throw new NotSupportedException("BRIGHTNESS_CONTRAST only works with image datasets");

        if (!imageDataset.ImageData != null)
            imageDataset.Load();

        var cmd = (CommandNode)node;

        // Parse parameters
        float brightness = ParseFloatParameter(cmd.FullText, "brightness", 0f);
        float contrast = ParseFloatParameter(cmd.FullText, "contrast", 1.0f);

        // Create output dataset
        var output = new ImageDataset(imageDataset.Name + "_adjusted", "")
        {
            Width = imageDataset.Width,
            Height = imageDataset.Height,
            BitDepth = imageDataset.BitDepth,
            PixelSize = imageDataset.PixelSize,
            Unit = imageDataset.Unit,
            ImageData = CloneImageData(imageDataset.ImageData)
        };

        ApplyBrightnessContrast(output.ImageData, brightness, contrast);

        Logger.Log($"Applied brightness={brightness}, contrast={contrast} to {imageDataset.Name}");
        return Task.FromResult<Dataset>(output);
    }

    private void ApplyBrightnessContrast(byte[] imageData, float brightness, float contrast)
    {
        float brightnessNorm = brightness / 100.0f;

        for (int i = 0; i < imageData.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                float value = imageData[i + c] / 255.0f;
                value = (value - 0.5f) * contrast + 0.5f;
                value += brightnessNorm;
                value = Math.Max(0, Math.Min(1, value));
                imageData[i + c] = (byte)(value * 255);
            }
        }
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }

    private byte[] CloneImageData(byte[] source)
    {
        var clone = new byte[source.Length];
        Array.Copy(source, clone, source.Length);
        return clone;
    }
}

/// <summary>
/// FILTER command - Apply various filters
/// Usage: FILTER type=gaussian size=5
/// Usage: FILTER type=median size=3
/// Usage: FILTER type=sobel
/// </summary>
public class FilterCommand : IGeoScriptCommand
{
    public string Name => "FILTER";
    public string HelpText => "Apply image filters (gaussian, median, mean, sobel, canny, bilateral, nlm, unsharp)";
    public string Usage => "FILTER type=<filterType> [size=<kernelSize>] [sigma=<value>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not ImageDataset imageDataset)
            throw new NotSupportedException("FILTER only works with image datasets");

        if (!imageDataset.ImageData != null)
            imageDataset.Load();

        var cmd = (CommandNode)node;

        string filterType = ParseStringParameter(cmd.FullText, "type", "gaussian").ToLower();
        int kernelSize = (int)ParseFloatParameter(cmd.FullText, "size", 5);

        var output = new ImageDataset(imageDataset.Name + $"_{filterType}", "")
        {
            Width = imageDataset.Width,
            Height = imageDataset.Height,
            BitDepth = imageDataset.BitDepth,
            PixelSize = imageDataset.PixelSize,
            Unit = imageDataset.Unit,
            ImageData = CloneImageData(imageDataset.ImageData)
        };

        switch (filterType)
        {
            case "gaussian":
            case "mean":
            case "box":
                ApplyMeanFilter(output.ImageData, imageDataset.Width, imageDataset.Height, kernelSize);
                break;
            case "median":
                ApplyMedianFilter(output.ImageData, imageDataset.Width, imageDataset.Height, kernelSize);
                break;
            case "sobel":
                ApplySobelFilter(output.ImageData, imageDataset.Width, imageDataset.Height);
                break;
            default:
                throw new ArgumentException($"Unsupported filter type: {filterType}");
        }

        Logger.Log($"Applied {filterType} filter to {imageDataset.Name}");
        return Task.FromResult<Dataset>(output);
    }

    private void ApplyMeanFilter(byte[] imageData, int width, int height, int kernelSize)
    {
        byte[] temp = CloneImageData(imageData);
        int radius = kernelSize / 2;

        for (int y = radius; y < height - radius; y++)
        {
            for (int x = radius; x < width - radius; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    int count = 0;

                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int px = x + kx;
                            int py = y + ky;
                            int idx = (py * width + px) * 4 + c;
                            sum += temp[idx];
                            count++;
                        }
                    }

                    int outputIdx = (y * width + x) * 4 + c;
                    imageData[outputIdx] = (byte)(sum / count);
                }
            }
        }
    }

    private void ApplyMedianFilter(byte[] imageData, int width, int height, int kernelSize)
    {
        byte[] temp = CloneImageData(imageData);
        int radius = kernelSize / 2;

        for (int y = radius; y < height - radius; y++)
        {
            for (int x = radius; x < width - radius; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    List<byte> values = new List<byte>();

                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int px = x + kx;
                            int py = y + ky;
                            int idx = (py * width + px) * 4 + c;
                            values.Add(temp[idx]);
                        }
                    }

                    values.Sort();
                    int outputIdx = (y * width + x) * 4 + c;
                    imageData[outputIdx] = values[values.Count / 2];
                }
            }
        }
    }

    private void ApplySobelFilter(byte[] imageData, int width, int height)
    {
        byte[] temp = CloneImageData(imageData);
        int[,] sobelX = new int[,] { { -1, 0, 1 }, { -2, 0, 2 }, { -1, 0, 1 } };
        int[,] sobelY = new int[,] { { -1, -2, -1 }, { 0, 0, 0 }, { 1, 2, 1 } };

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int idx = (y * width + x) * 4;
                int gray = (int)(0.299f * temp[idx] + 0.587f * temp[idx + 1] + 0.114f * temp[idx + 2]);

                int gx = 0, gy = 0;

                for (int ky = -1; ky <= 1; ky++)
                {
                    for (int kx = -1; kx <= 1; kx++)
                    {
                        int px = x + kx;
                        int py = y + ky;
                        int pidx = (py * width + px) * 4;
                        int pgray = (int)(0.299f * temp[pidx] + 0.587f * temp[pidx + 1] + 0.114f * temp[pidx + 2]);

                        gx += pgray * sobelX[ky + 1, kx + 1];
                        gy += pgray * sobelY[ky + 1, kx + 1];
                    }
                }

                int magnitude = (int)Math.Sqrt(gx * gx + gy * gy);
                magnitude = Math.Min(255, magnitude);

                imageData[idx] = (byte)magnitude;
                imageData[idx + 1] = (byte)magnitude;
                imageData[idx + 2] = (byte)magnitude;
            }
        }
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }

    private byte[] CloneImageData(byte[] source)
    {
        var clone = new byte[source.Length];
        Array.Copy(source, clone, source.Length);
        return clone;
    }
}

/// <summary>
/// THRESHOLD command - Apply threshold segmentation
/// Usage: THRESHOLD min=100 max=200
/// </summary>
public class ThresholdCommand : IGeoScriptCommand
{
    public string Name => "THRESHOLD";
    public string HelpText => "Apply threshold segmentation to create a binary mask";
    public string Usage => "THRESHOLD min=<0-255> max=<0-255>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not ImageDataset imageDataset)
            throw new NotSupportedException("THRESHOLD only works with image datasets");

        if (!imageDataset.ImageData != null)
            imageDataset.Load();

        var cmd = (CommandNode)node;

        int minValue = (int)ParseFloatParameter(cmd.FullText, "min", 0);
        int maxValue = (int)ParseFloatParameter(cmd.FullText, "max", 255);

        var output = new ImageDataset(imageDataset.Name + "_threshold", "")
        {
            Width = imageDataset.Width,
            Height = imageDataset.Height,
            BitDepth = 8,
            PixelSize = imageDataset.PixelSize,
            Unit = imageDataset.Unit,
            ImageData = new byte[imageDataset.ImageData.Length]
        };

        ApplyThreshold(imageDataset.ImageData, output.ImageData, minValue, maxValue);

        Logger.Log($"Applied threshold [{minValue}, {maxValue}] to {imageDataset.Name}");
        return Task.FromResult<Dataset>(output);
    }

    private void ApplyThreshold(byte[] input, byte[] output, int minValue, int maxValue)
    {
        for (int i = 0; i < input.Length; i += 4)
        {
            int gray = (int)(0.299f * input[i] + 0.587f * input[i + 1] + 0.114f * input[i + 2]);
            byte value = (gray >= minValue && gray <= maxValue) ? (byte)255 : (byte)0;

            output[i] = value;
            output[i + 1] = value;
            output[i + 2] = value;
            output[i + 3] = 255;
        }
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// BINARIZE command - Convert to binary image
/// Usage: BINARIZE threshold=128
/// Usage: BINARIZE threshold=auto
/// </summary>
public class BinarizeCommand : IGeoScriptCommand
{
    public string Name => "BINARIZE";
    public string HelpText => "Convert image to binary (black/white) using threshold";
    public string Usage => "BINARIZE threshold=<0-255 or 'auto'>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not ImageDataset imageDataset)
            throw new NotSupportedException("BINARIZE only works with image datasets");

        if (!imageDataset.ImageData != null)
            imageDataset.Load();

        var cmd = (CommandNode)node;

        var thresholdStr = ParseStringParameter(cmd.FullText, "threshold", "128");
        int threshold;

        if (thresholdStr.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            threshold = CalculateOtsuThreshold(imageDataset.ImageData);
            Logger.Log($"Auto-calculated Otsu threshold: {threshold}");
        }
        else
        {
            threshold = int.Parse(thresholdStr);
        }

        var output = new ImageDataset(imageDataset.Name + "_binary", "")
        {
            Width = imageDataset.Width,
            Height = imageDataset.Height,
            BitDepth = 8,
            PixelSize = imageDataset.PixelSize,
            Unit = imageDataset.Unit,
            ImageData = new byte[imageDataset.ImageData.Length]
        };

        ApplyBinarization(imageDataset.ImageData, output.ImageData, threshold);

        Logger.Log($"Binarized {imageDataset.Name} with threshold={threshold}");
        return Task.FromResult<Dataset>(output);
    }

    private void ApplyBinarization(byte[] input, byte[] output, int threshold)
    {
        for (int i = 0; i < input.Length; i += 4)
        {
            int gray = (int)(0.299f * input[i] + 0.587f * input[i + 1] + 0.114f * input[i + 2]);
            byte value = gray >= threshold ? (byte)255 : (byte)0;

            output[i] = value;
            output[i + 1] = value;
            output[i + 2] = value;
            output[i + 3] = 255;
        }
    }

    private int CalculateOtsuThreshold(byte[] imageData)
    {
        int[] histogram = new int[256];
        for (int i = 0; i < imageData.Length; i += 4)
        {
            int gray = (int)(0.299f * imageData[i] + 0.587f * imageData[i + 1] + 0.114f * imageData[i + 2]);
            histogram[gray]++;
        }

        int total = imageData.Length / 4;
        float sum = 0;
        for (int i = 0; i < 256; i++)
            sum += i * histogram[i];

        float sumB = 0;
        int wB = 0;
        float varMax = 0;
        int threshold = 0;

        for (int i = 0; i < 256; i++)
        {
            wB += histogram[i];
            if (wB == 0) continue;

            int wF = total - wB;
            if (wF == 0) break;

            sumB += i * histogram[i];
            float mB = sumB / wB;
            float mF = (sum - sumB) / wF;
            float varBetween = (float)wB * (float)wF * (mB - mF) * (mB - mF);

            if (varBetween > varMax)
            {
                varMax = varBetween;
                threshold = i;
            }
        }

        return threshold;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GRAYSCALE command - Convert to grayscale
/// Usage: GRAYSCALE
/// </summary>
public class GrayscaleCommand : IGeoScriptCommand
{
    public string Name => "GRAYSCALE";
    public string HelpText => "Convert image to grayscale";
    public string Usage => "GRAYSCALE";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not ImageDataset imageDataset)
            throw new NotSupportedException("GRAYSCALE only works with image datasets");

        if (!imageDataset.ImageData != null)
            imageDataset.Load();

        var output = new ImageDataset(imageDataset.Name + "_gray", "")
        {
            Width = imageDataset.Width,
            Height = imageDataset.Height,
            BitDepth = 8,
            PixelSize = imageDataset.PixelSize,
            Unit = imageDataset.Unit,
            ImageData = CloneImageData(imageDataset.ImageData)
        };

        for (int i = 0; i < output.ImageData.Length; i += 4)
        {
            byte gray = (byte)(0.299f * output.ImageData[i] + 0.587f * output.ImageData[i + 1] + 0.114f * output.ImageData[i + 2]);
            output.ImageData[i] = gray;
            output.ImageData[i + 1] = gray;
            output.ImageData[i + 2] = gray;
        }

        Logger.Log($"Converted {imageDataset.Name} to grayscale");
        return Task.FromResult<Dataset>(output);
    }

    private byte[] CloneImageData(byte[] source)
    {
        var clone = new byte[source.Length];
        Array.Copy(source, clone, source.Length);
        return clone;
    }
}

/// <summary>
/// INVERT command - Invert image colors
/// Usage: INVERT
/// </summary>
public class InvertCommand : IGeoScriptCommand
{
    public string Name => "INVERT";
    public string HelpText => "Invert image colors";
    public string Usage => "INVERT";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not ImageDataset imageDataset)
            throw new NotSupportedException("INVERT only works with image datasets");

        if (!imageDataset.ImageData != null)
            imageDataset.Load();

        var output = new ImageDataset(imageDataset.Name + "_inverted", "")
        {
            Width = imageDataset.Width,
            Height = imageDataset.Height,
            BitDepth = imageDataset.BitDepth,
            PixelSize = imageDataset.PixelSize,
            Unit = imageDataset.Unit,
            ImageData = CloneImageData(imageDataset.ImageData)
        };

        for (int i = 0; i < output.ImageData.Length; i += 4)
        {
            output.ImageData[i] = (byte)(255 - output.ImageData[i]);
            output.ImageData[i + 1] = (byte)(255 - output.ImageData[i + 1]);
            output.ImageData[i + 2] = (byte)(255 - output.ImageData[i + 2]);
        }

        Logger.Log($"Inverted {imageDataset.Name}");
        return Task.FromResult<Dataset>(output);
    }

    private byte[] CloneImageData(byte[] source)
    {
        var clone = new byte[source.Length];
        Array.Copy(source, clone, source.Length);
        return clone;
    }
}

/// <summary>
/// NORMALIZE command - Normalize image intensity
/// Usage: NORMALIZE
/// </summary>
public class NormalizeCommand : IGeoScriptCommand
{
    public string Name => "NORMALIZE";
    public string HelpText => "Normalize image to full intensity range";
    public string Usage => "NORMALIZE";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not ImageDataset imageDataset)
            throw new NotSupportedException("NORMALIZE only works with image datasets");

        if (!imageDataset.ImageData != null)
            imageDataset.Load();

        var output = new ImageDataset(imageDataset.Name + "_normalized", "")
        {
            Width = imageDataset.Width,
            Height = imageDataset.Height,
            BitDepth = imageDataset.BitDepth,
            PixelSize = imageDataset.PixelSize,
            Unit = imageDataset.Unit,
            ImageData = CloneImageData(imageDataset.ImageData)
        };

        byte minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;

        for (int i = 0; i < output.ImageData.Length; i += 4)
        {
            minR = Math.Min(minR, output.ImageData[i]);
            maxR = Math.Max(maxR, output.ImageData[i]);
            minG = Math.Min(minG, output.ImageData[i + 1]);
            maxG = Math.Max(maxG, output.ImageData[i + 1]);
            minB = Math.Min(minB, output.ImageData[i + 2]);
            maxB = Math.Max(maxB, output.ImageData[i + 2]);
        }

        for (int i = 0; i < output.ImageData.Length; i += 4)
        {
            if (maxR > minR)
                output.ImageData[i] = (byte)(((output.ImageData[i] - minR) * 255) / (maxR - minR));
            if (maxG > minG)
                output.ImageData[i + 1] = (byte)(((output.ImageData[i + 1] - minG) * 255) / (maxG - minG));
            if (maxB > minB)
                output.ImageData[i + 2] = (byte)(((output.ImageData[i + 2] - minB) * 255) / (maxB - minB));
        }

        Logger.Log($"Normalized {imageDataset.Name}");
        return Task.FromResult<Dataset>(output);
    }

    private byte[] CloneImageData(byte[] source)
    {
        var clone = new byte[source.Length];
        Array.Copy(source, clone, source.Length);
        return clone;
    }
}

#endregion
