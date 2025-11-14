// GeoscientistToolkit/Analysis/Photogrammetry/DepthEstimator.cs

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Util;


namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Depth estimation using ONNX models (MiDaS, DPT, ZoeDepth).
/// </summary>
public class DepthEstimator : IDisposable
{
    private InferenceSession _session;
    private readonly string _modelPath;
    private readonly DepthModelType _modelType;
    private readonly bool _useGpu;

    public enum DepthModelType
    {
        MiDaSSmall,    // Fast, relative depth
        DPTSmall,      // Medium speed, relative depth
        ZoeDepth       // Slower, metric-aware depth
    }

    public DepthEstimator(string modelPath, DepthModelType modelType, bool useGpu = false)
    {
        _modelPath = modelPath;
        _modelType = modelType;
        _useGpu = useGpu;
        InitializeSession();
    }

    private void InitializeSession()
    {
        var options = new SessionOptions();

        if (_useGpu)
        {
            try
            {
                // Try to use CUDA if available
                options.AppendExecutionProvider_CUDA(0);
                Logger.Log("DepthEstimator: Using GPU acceleration (CUDA)");
            }
            catch
            {
                Logger.LogWarning("DepthEstimator: CUDA not available, falling back to CPU");
            }
        }

        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        try
        {
            _session = new InferenceSession(_modelPath, options);
            Logger.Log($"DepthEstimator: Loaded model {_modelType} from {_modelPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"DepthEstimator: Failed to load model: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Estimate depth from an input image.
    /// </summary>
    /// <param name="inputImage">Input BGR image</param>
    /// <returns>Depth map (single channel float32)</returns>
    public Mat EstimateDepth(Mat inputImage)
    {
        if (inputImage == null || inputImage.Empty())
            throw new ArgumentException("Input image is null or empty");

        // Preprocess image
        var preprocessed = PreprocessImage(inputImage);

        // Create input tensor
        var inputTensor = CreateInputTensor(preprocessed);

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor)
        };

        using var results = _session.Run(inputs);
        var output = results.First().AsEnumerable<float>().ToArray();

        // Post-process to create depth map
        var depthMap = PostprocessDepth(output, inputImage.Size());

        preprocessed.Dispose();

        return depthMap;
    }

    private Mat PreprocessImage(Mat input)
    {
        Mat processed = new Mat();

        // Resize to model input size (typically 384x384 for MiDaS small)
        var targetSize = GetModelInputSize();
        Cv2.Resize(input, processed, targetSize);

        // Convert BGR to RGB
        Cv2.CvtColor(processed, processed, ColorConversionCodes.BGR2RGB);

        // Normalize to [0, 1]
        processed.ConvertTo(processed, MatType.CV_32FC3, 1.0 / 255.0);

        return processed;
    }

    private DenseTensor<float> CreateInputTensor(Mat preprocessed)
    {
        var height = preprocessed.Height;
        var width = preprocessed.Width;

        // Create tensor with shape [1, 3, H, W] (NCHW format)
        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

        // Fill tensor with image data
        unsafe
        {
            var ptr = (float*)preprocessed.DataPointer;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = (y * width + x) * 3;
                    // RGB to CHW format
                    tensor[0, 0, y, x] = ptr[pixelIndex + 0]; // R
                    tensor[0, 1, y, x] = ptr[pixelIndex + 1]; // G
                    tensor[0, 2, y, x] = ptr[pixelIndex + 2]; // B
                }
            }
        }

        return tensor;
    }

    private Mat PostprocessDepth(float[] depthData, Size originalSize)
    {
        var modelSize = GetModelInputSize();

        // Create depth map from output
        Mat depthMap = new Mat(modelSize.Height, modelSize.Width, MatType.CV_32FC1);

        unsafe
        {
            var ptr = (float*)depthMap.DataPointer;
            for (int i = 0; i < depthData.Length; i++)
            {
                ptr[i] = depthData[i];
            }
        }

        // Resize back to original size
        Mat resized = new Mat();
        Cv2.Resize(depthMap, resized, originalSize);

        depthMap.Dispose();

        return resized;
    }

    private Size GetModelInputSize()
    {
        return _modelType switch
        {
            DepthModelType.MiDaSSmall => new Size(384, 384),
            DepthModelType.DPTSmall => new Size(384, 384),
            DepthModelType.ZoeDepth => new Size(512, 384),
            _ => new Size(384, 384)
        };
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
