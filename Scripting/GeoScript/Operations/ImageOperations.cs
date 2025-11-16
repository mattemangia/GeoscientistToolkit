using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GeoscientistToolkit.Scripting.GeoScript.Operations
{
    /// <summary>
    /// Base class for image operations
    /// </summary>
    public abstract class ImageOperationBase : IOperation
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract Dictionary<string, string> Parameters { get; }
        public abstract Dataset Execute(Dataset inputDataset, List<object> parameters);

        public virtual bool CanApplyTo(DatasetType type)
        {
            return type == DatasetType.SingleImage || type == DatasetType.CtImageStack;
        }

        protected byte[] CloneImageData(byte[] source)
        {
            var clone = new byte[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }
    }

    /// <summary>
    /// BRIGHTNESS_CONTRAST operation
    /// Syntax: BRIGHTNESS_CONTRAST brightness, contrast
    /// Examples:
    ///   BRIGHTNESS_CONTRAST 128, 256  (both)
    ///   BRIGHTNESS_CONTRAST 128,      (brightness only)
    ///   BRIGHTNESS_CONTRAST ,256      (contrast only)
    /// </summary>
    public class BrightnessContrastOperation : ImageOperationBase
    {
        public override string Name => "BRIGHTNESS_CONTRAST";
        public override string Description => "Adjust brightness and contrast of an image";

        public override Dictionary<string, string> Parameters => new()
        {
            { "brightness", "Brightness adjustment (-100 to +100, or omit with comma)" },
            { "contrast", "Contrast adjustment (0.1 to 3.0, or omit)" }
        };

        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not ImageDataset imageDataset)
                throw new InvalidOperationException($"BRIGHTNESS_CONTRAST can only be applied to image datasets");

            if (imageDataset.ImageData == null)
                imageDataset.Load();

            // Parse parameters
            float? brightness = null;
            float? contrast = null;

            if (parameters.Count > 0 && parameters[0] != null)
                brightness = Convert.ToSingle(parameters[0]);

            if (parameters.Count > 1 && parameters[1] != null)
                contrast = Convert.ToSingle(parameters[1]);

            if (!brightness.HasValue && !contrast.HasValue)
                throw new ArgumentException("At least one of brightness or contrast must be specified");

            // Create output dataset
            var output = new ImageDataset(inputDataset.Name + "_adjusted", "")
            {
                Width = imageDataset.Width,
                Height = imageDataset.Height,
                BitDepth = imageDataset.BitDepth,
                PixelSize = imageDataset.PixelSize,
                Unit = imageDataset.Unit,
                ImageData = CloneImageData(imageDataset.ImageData)
            };

            // Apply adjustments
            ApplyBrightnessContrast(output.ImageData, brightness ?? 0, contrast ?? 1.0f);

            return output;
        }

        private void ApplyBrightnessContrast(byte[] imageData, float brightness, float contrast)
        {
            // Normalize brightness to -1.0 to 1.0 range
            float brightnessNorm = brightness / 100.0f;

            for (int i = 0; i < imageData.Length; i += 4)
            {
                // Apply to RGB channels (skip alpha)
                for (int c = 0; c < 3; c++)
                {
                    float value = imageData[i + c] / 255.0f;

                    // Apply contrast
                    value = (value - 0.5f) * contrast + 0.5f;

                    // Apply brightness
                    value += brightnessNorm;

                    // Clamp
                    value = Math.Max(0, Math.Min(1, value));

                    imageData[i + c] = (byte)(value * 255);
                }
            }
        }
    }

    /// <summary>
    /// THRESHOLD operation
    /// Syntax: THRESHOLD minValue, maxValue
    /// Example: THRESHOLD 100, 200
    /// </summary>
    public class ThresholdOperation : ImageOperationBase
    {
        public override string Name => "THRESHOLD";
        public override string Description => "Apply threshold to create a segmentation mask";

        public override Dictionary<string, string> Parameters => new()
        {
            { "minValue", "Minimum threshold value (0-255)" },
            { "maxValue", "Maximum threshold value (0-255)" }
        };

        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not ImageDataset imageDataset)
                throw new InvalidOperationException("THRESHOLD can only be applied to image datasets");

            if (imageDataset.ImageData == null)
                imageDataset.Load();

            if (parameters.Count < 2)
                throw new ArgumentException("THRESHOLD requires two parameters: minValue and maxValue");

            int minValue = Convert.ToInt32(parameters[0]);
            int maxValue = Convert.ToInt32(parameters[1]);

            // Create output dataset
            var output = new ImageDataset(inputDataset.Name + "_threshold", "")
            {
                Width = imageDataset.Width,
                Height = imageDataset.Height,
                BitDepth = imageDataset.BitDepth,
                PixelSize = imageDataset.PixelSize,
                Unit = imageDataset.Unit,
                ImageData = new byte[imageDataset.ImageData.Length]
            };

            // Apply threshold
            ApplyThreshold(imageDataset.ImageData, output.ImageData, minValue, maxValue);

            return output;
        }

        private void ApplyThreshold(byte[] input, byte[] output, int minValue, int maxValue)
        {
            for (int i = 0; i < input.Length; i += 4)
            {
                // Convert to grayscale
                int gray = (int)(0.299f * input[i] + 0.587f * input[i + 1] + 0.114f * input[i + 2]);

                byte value = (gray >= minValue && gray <= maxValue) ? (byte)255 : (byte)0;

                output[i] = value;     // R
                output[i + 1] = value; // G
                output[i + 2] = value; // B
                output[i + 3] = 255;   // A
            }
        }
    }

    /// <summary>
    /// BINARIZE operation
    /// Syntax: BINARIZE threshold
    /// Example: BINARIZE 128
    /// </summary>
    public class BinarizeOperation : ImageOperationBase
    {
        public override string Name => "BINARIZE";
        public override string Description => "Convert image to binary (black/white) using threshold";

        public override Dictionary<string, string> Parameters => new()
        {
            { "threshold", "Threshold value (0-255, or 'auto' for automatic Otsu thresholding)" }
        };

        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not ImageDataset imageDataset)
                throw new InvalidOperationException("BINARIZE can only be applied to image datasets");

            if (imageDataset.ImageData == null)
                imageDataset.Load();

            if (parameters.Count < 1)
                throw new ArgumentException("BINARIZE requires a threshold parameter");

            int threshold;
            if (parameters[0] is string str && str.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                threshold = CalculateOtsuThreshold(imageDataset.ImageData);
            }
            else
            {
                threshold = Convert.ToInt32(parameters[0]);
            }

            // Create output dataset
            var output = new ImageDataset(inputDataset.Name + "_binary", "")
            {
                Width = imageDataset.Width,
                Height = imageDataset.Height,
                BitDepth = 8,
                PixelSize = imageDataset.PixelSize,
                Unit = imageDataset.Unit,
                ImageData = new byte[imageDataset.ImageData.Length]
            };

            // Apply binarization
            ApplyBinarization(imageDataset.ImageData, output.ImageData, threshold);

            return output;
        }

        private void ApplyBinarization(byte[] input, byte[] output, int threshold)
        {
            for (int i = 0; i < input.Length; i += 4)
            {
                // Convert to grayscale
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
            // Calculate histogram
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
            int wF = 0;
            float varMax = 0;
            int threshold = 0;

            for (int i = 0; i < 256; i++)
            {
                wB += histogram[i];
                if (wB == 0) continue;

                wF = total - wB;
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
    }

    /// <summary>
    /// FILTER operation
    /// Syntax: FILTER filterType, parameter1, parameter2, ...
    /// Examples:
    ///   FILTER gaussian, 5
    ///   FILTER median, 3
    ///   FILTER bilateral, 5, 75, 75
    /// </summary>
    public class FilterOperation : ImageOperationBase
    {
        public override string Name => "FILTER";
        public override string Description => "Apply various filters (gaussian, median, mean, bilateral, sobel, canny, unsharp, nlm)";

        public override Dictionary<string, string> Parameters => new()
        {
            { "filterType", "Type of filter: gaussian, median, mean, bilateral, sobel, canny, unsharp, nlm" },
            { "parameters", "Filter-specific parameters (varies by filter type)" }
        };

        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not ImageDataset imageDataset)
                throw new InvalidOperationException("FILTER can only be applied to image datasets");

            if (imageDataset.ImageData == null)
                imageDataset.Load();

            if (parameters.Count < 1)
                throw new ArgumentException("FILTER requires at least a filter type parameter");

            string filterType = parameters[0].ToString().ToLower();

            // Create output dataset
            var output = new ImageDataset(inputDataset.Name + $"_{filterType}", "")
            {
                Width = imageDataset.Width,
                Height = imageDataset.Height,
                BitDepth = imageDataset.BitDepth,
                PixelSize = imageDataset.PixelSize,
                Unit = imageDataset.Unit,
                ImageData = CloneImageData(imageDataset.ImageData)
            };

            // Apply filter based on type
            switch (filterType)
            {
                case "gaussian":
                    int gaussianKernel = parameters.Count > 1 ? Convert.ToInt32(parameters[1]) : 5;
                    ApplyGaussianFilter(output.ImageData, imageDataset.Width, imageDataset.Height, gaussianKernel);
                    break;

                case "median":
                    int medianKernel = parameters.Count > 1 ? Convert.ToInt32(parameters[1]) : 3;
                    ApplyMedianFilter(output.ImageData, imageDataset.Width, imageDataset.Height, medianKernel);
                    break;

                case "mean":
                case "box":
                    int meanKernel = parameters.Count > 1 ? Convert.ToInt32(parameters[1]) : 3;
                    ApplyMeanFilter(output.ImageData, imageDataset.Width, imageDataset.Height, meanKernel);
                    break;

                case "sobel":
                    ApplySobelFilter(output.ImageData, imageDataset.Width, imageDataset.Height);
                    break;

                default:
                    throw new ArgumentException($"Unknown filter type: {filterType}. Supported: gaussian, median, mean, sobel");
            }

            return output;
        }

        private void ApplyGaussianFilter(byte[] imageData, int width, int height, int kernelSize)
        {
            // Simple box blur approximation for now
            // In a real implementation, you'd use proper Gaussian kernel
            ApplyMeanFilter(imageData, width, height, kernelSize);
        }

        private void ApplyMedianFilter(byte[] imageData, int width, int height, int kernelSize)
        {
            byte[] temp = CloneImageData(imageData);
            int radius = kernelSize / 2;

            for (int y = radius; y < height - radius; y++)
            {
                for (int x = radius; x < width - radius; x++)
                {
                    for (int c = 0; c < 3; c++) // RGB channels
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

        private void ApplyMeanFilter(byte[] imageData, int width, int height, int kernelSize)
        {
            byte[] temp = CloneImageData(imageData);
            int radius = kernelSize / 2;

            for (int y = radius; y < height - radius; y++)
            {
                for (int x = radius; x < width - radius; x++)
                {
                    for (int c = 0; c < 3; c++) // RGB channels
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

        private void ApplySobelFilter(byte[] imageData, int width, int height)
        {
            byte[] temp = CloneImageData(imageData);

            // Sobel kernels
            int[,] sobelX = new int[,] { { -1, 0, 1 }, { -2, 0, 2 }, { -1, 0, 1 } };
            int[,] sobelY = new int[,] { { -1, -2, -1 }, { 0, 0, 0 }, { 1, 2, 1 } };

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    // Convert to grayscale first
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
    }

    /// <summary>
    /// Additional helper operations
    /// </summary>
    public class GrayscaleOperation : ImageOperationBase
    {
        public override string Name => "GRAYSCALE";
        public override string Description => "Convert image to grayscale";
        public override Dictionary<string, string> Parameters => new();

        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not ImageDataset imageDataset)
                throw new InvalidOperationException("GRAYSCALE can only be applied to image datasets");

            if (imageDataset.ImageData == null)
                imageDataset.Load();

            var output = new ImageDataset(inputDataset.Name + "_gray", "")
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

            return output;
        }
    }

    public class InvertOperation : ImageOperationBase
    {
        public override string Name => "INVERT";
        public override string Description => "Invert image colors";
        public override Dictionary<string, string> Parameters => new();

        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not ImageDataset imageDataset)
                throw new InvalidOperationException("INVERT can only be applied to image datasets");

            if (imageDataset.ImageData == null)
                imageDataset.Load();

            var output = new ImageDataset(inputDataset.Name + "_inverted", "")
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

            return output;
        }
    }

    public class NormalizeOperation : ImageOperationBase
    {
        public override string Name => "NORMALIZE";
        public override string Description => "Normalize image to full intensity range";
        public override Dictionary<string, string> Parameters => new();

        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not ImageDataset imageDataset)
                throw new InvalidOperationException("NORMALIZE can only be applied to image datasets");

            if (imageDataset.ImageData == null)
                imageDataset.Load();

            var output = new ImageDataset(inputDataset.Name + "_normalized", "")
            {
                Width = imageDataset.Width,
                Height = imageDataset.Height,
                BitDepth = imageDataset.BitDepth,
                PixelSize = imageDataset.PixelSize,
                Unit = imageDataset.Unit,
                ImageData = CloneImageData(imageDataset.ImageData)
            };

            // Find min/max for each channel
            byte minR = 255, maxR = 0;
            byte minG = 255, maxG = 0;
            byte minB = 255, maxB = 0;

            for (int i = 0; i < output.ImageData.Length; i += 4)
            {
                minR = Math.Min(minR, output.ImageData[i]);
                maxR = Math.Max(maxR, output.ImageData[i]);
                minG = Math.Min(minG, output.ImageData[i + 1]);
                maxG = Math.Max(maxG, output.ImageData[i + 1]);
                minB = Math.Min(minB, output.ImageData[i + 2]);
                maxB = Math.Max(maxB, output.ImageData[i + 2]);
            }

            // Normalize
            for (int i = 0; i < output.ImageData.Length; i += 4)
            {
                if (maxR > minR)
                    output.ImageData[i] = (byte)(((output.ImageData[i] - minR) * 255) / (maxR - minR));
                if (maxG > minG)
                    output.ImageData[i + 1] = (byte)(((output.ImageData[i + 1] - minG) * 255) / (maxG - minG));
                if (maxB > minB)
                    output.ImageData[i + 2] = (byte)(((output.ImageData[i + 2] - minB) * 255) / (maxB - minB));
            }

            return output;
        }
    }

    public class HistogramEqualizeOperation : ImageOperationBase
    {
        public override string Name => "HISTOGRAM_EQUALIZE";
        public override string Description => "Equalize histogram for better contrast distribution";
        public override Dictionary<string, string> Parameters => new();

        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not ImageDataset imageDataset)
                throw new InvalidOperationException("HISTOGRAM_EQUALIZE can only be applied to image datasets");

            if (imageDataset.ImageData == null)
                imageDataset.Load();

            var output = new ImageDataset(inputDataset.Name + "_equalized", "")
            {
                Width = imageDataset.Width,
                Height = imageDataset.Height,
                BitDepth = imageDataset.BitDepth,
                PixelSize = imageDataset.PixelSize,
                Unit = imageDataset.Unit,
                ImageData = CloneImageData(imageDataset.ImageData)
            };

            // Simple histogram equalization on luminance
            int[] histogram = new int[256];
            int totalPixels = output.ImageData.Length / 4;

            // Calculate histogram
            for (int i = 0; i < output.ImageData.Length; i += 4)
            {
                int gray = (int)(0.299f * output.ImageData[i] + 0.587f * output.ImageData[i + 1] + 0.114f * output.ImageData[i + 2]);
                histogram[gray]++;
            }

            // Calculate CDF
            int[] cdf = new int[256];
            cdf[0] = histogram[0];
            for (int i = 1; i < 256; i++)
                cdf[i] = cdf[i - 1] + histogram[i];

            // Normalize CDF
            byte[] lut = new byte[256];
            for (int i = 0; i < 256; i++)
                lut[i] = (byte)((cdf[i] * 255) / totalPixels);

            // Apply equalization
            for (int i = 0; i < output.ImageData.Length; i += 4)
            {
                int gray = (int)(0.299f * output.ImageData[i] + 0.587f * output.ImageData[i + 1] + 0.114f * output.ImageData[i + 2]);
                byte newValue = lut[gray];

                output.ImageData[i] = newValue;
                output.ImageData[i + 1] = newValue;
                output.ImageData[i + 2] = newValue;
            }

            return output;
        }
    }

    // Placeholder operations for transformation
    public class ResizeOperation : ImageOperationBase
    {
        public override string Name => "RESIZE";
        public override string Description => "Resize image to specified dimensions";
        public override Dictionary<string, string> Parameters => new() { { "width", "New width" }, { "height", "New height" } };
        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            throw new NotImplementedException("RESIZE operation requires System.Drawing.Common or SkiaSharp integration");
        }
    }

    public class CropOperation : ImageOperationBase
    {
        public override string Name => "CROP";
        public override string Description => "Crop image to specified region";
        public override Dictionary<string, string> Parameters => new() { { "x", "X coordinate" }, { "y", "Y coordinate" }, { "width", "Width" }, { "height", "Height" } };
        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            throw new NotImplementedException("CROP operation requires additional implementation");
        }
    }

    public class RotateOperation : ImageOperationBase
    {
        public override string Name => "ROTATE";
        public override string Description => "Rotate image by specified angle";
        public override Dictionary<string, string> Parameters => new() { { "angle", "Rotation angle in degrees" } };
        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            throw new NotImplementedException("ROTATE operation requires additional implementation");
        }
    }

    public class FlipOperation : ImageOperationBase
    {
        public override string Name => "FLIP";
        public override string Description => "Flip image horizontally or vertically";
        public override Dictionary<string, string> Parameters => new() { { "direction", "horizontal or vertical" } };
        public override Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            throw new NotImplementedException("FLIP operation requires additional implementation");
        }
    }
}
