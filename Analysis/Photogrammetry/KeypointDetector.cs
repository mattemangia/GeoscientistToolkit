// GeoscientistToolkit/Analysis/Photogrammetry/KeypointDetector.cs

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Keypoint detection using SuperPoint (sparse features).
/// </summary>
public class KeypointDetector : IDisposable
{
    private InferenceSession _session;
    private readonly string _modelPath;
    private readonly bool _useGpu;
    private readonly float _confidenceThreshold;

    public class DetectedKeypoint
    {
        public Point2f Position { get; set; }
        public float Confidence { get; set; }
        public float[] Descriptor { get; set; }
    }

    public KeypointDetector(string modelPath, bool useGpu = false, float confidenceThreshold = 0.015f)
    {
        _modelPath = modelPath;
        _useGpu = useGpu;
        _confidenceThreshold = confidenceThreshold;
        InitializeSession();
    }

    private void InitializeSession()
    {
        var options = new SessionOptions();

        if (_useGpu)
        {
            try
            {
                options.AppendExecutionProvider_CUDA(0);
                Logger.Log("KeypointDetector: Using GPU acceleration (CUDA)");
            }
            catch
            {
                Logger.LogWarning("KeypointDetector: CUDA not available, falling back to CPU");
            }
        }

        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        try
        {
            _session = new InferenceSession(_modelPath, options);
            Logger.Log($"KeypointDetector: Loaded SuperPoint model from {_modelPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"KeypointDetector: Failed to load model: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Detect keypoints and compute descriptors.
    /// </summary>
    public List<DetectedKeypoint> DetectKeypoints(Mat inputImage)
    {
        if (inputImage == null || inputImage.Empty())
            throw new ArgumentException("Input image is null or empty");

        // Preprocess
        var preprocessed = PreprocessImage(inputImage);
        var tensor = CreateInputTensor(preprocessed);

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("image", tensor)
        };

        using var results = _session.Run(inputs);

        // Extract keypoints and descriptors
        var keypoints = ExtractKeypoints(results, inputImage.Size());

        preprocessed.Dispose();

        return keypoints;
    }

    private Mat PreprocessImage(Mat input)
    {
        Mat gray = new Mat();

        // Convert to grayscale if needed
        if (input.Channels() == 3)
            Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
        else
            gray = input.Clone();

        // Normalize to [0, 1]
        gray.ConvertTo(gray, MatType.CV_32FC1, 1.0 / 255.0);

        return gray;
    }

    private DenseTensor<float> CreateInputTensor(Mat preprocessed)
    {
        var height = preprocessed.Height;
        var width = preprocessed.Width;

        // SuperPoint expects [1, 1, H, W]
        var tensor = new DenseTensor<float>(new[] { 1, 1, height, width });

        unsafe
        {
            var ptr = (float*)preprocessed.DataPointer;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    tensor[0, 0, y, x] = ptr[y * width + x];
                }
            }
        }

        return tensor;
    }

    private List<DetectedKeypoint> ExtractKeypoints(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, Size imageSize)
    {
        var keypoints = new List<DetectedKeypoint>();

        // SuperPoint outputs: 'keypoints' (Nx2) and 'descriptors' (Nx256)
        var keypointsData = results.FirstOrDefault(r => r.Name == "keypoints")?.AsEnumerable<float>().ToArray();
        var descriptorsData = results.FirstOrDefault(r => r.Name == "descriptors")?.AsEnumerable<float>().ToArray();
        var scoresData = results.FirstOrDefault(r => r.Name == "scores")?.AsEnumerable<float>().ToArray();

        if (keypointsData == null || descriptorsData == null)
        {
            Logger.LogWarning("KeypointDetector: No keypoints or descriptors in model output");
            return keypoints;
        }

        int numKeypoints = keypointsData.Length / 2;
        int descriptorDim = 256; // SuperPoint descriptor dimension

        for (int i = 0; i < numKeypoints; i++)
        {
            float x = keypointsData[i * 2];
            float y = keypointsData[i * 2 + 1];
            float score = scoresData != null && i < scoresData.Length ? scoresData[i] : 1.0f;

            if (score < _confidenceThreshold)
                continue;

            // Extract descriptor
            var descriptor = new float[descriptorDim];
            Array.Copy(descriptorsData, i * descriptorDim, descriptor, 0, descriptorDim);

            keypoints.Add(new DetectedKeypoint
            {
                Position = new Point2f(x, y),
                Confidence = score,
                Descriptor = descriptor
            });
        }

        Logger.Log($"KeypointDetector: Detected {keypoints.Count} keypoints");
        return keypoints;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
