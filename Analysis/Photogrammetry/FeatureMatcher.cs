// GeoscientistToolkit/Analysis/Photogrammetry/FeatureMatcher.cs

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Feature matching using LightGlue or traditional methods.
/// </summary>
public class FeatureMatcher : IDisposable
{
    private InferenceSession _lightGlueSession;
    private readonly string _modelPath;
    private readonly bool _useGpu;
    private readonly bool _useLightGlue;

    public class FeatureMatch
    {
        public int Index1 { get; set; }
        public int Index2 { get; set; }
        public float Distance { get; set; }
        public Point2f Point1 { get; set; }
        public Point2f Point2 { get; set; }
    }

    public FeatureMatcher(string lightGlueModelPath = null, bool useGpu = false)
    {
        _modelPath = lightGlueModelPath;
        _useGpu = useGpu;
        _useLightGlue = !string.IsNullOrEmpty(lightGlueModelPath);

        if (_useLightGlue)
        {
            InitializeLightGlueSession();
        }
    }

    private void InitializeLightGlueSession()
    {
        var options = new SessionOptions();

        if (_useGpu)
        {
            try
            {
                options.AppendExecutionProvider_CUDA(0);
                Logger.Log("FeatureMatcher: Using GPU acceleration (CUDA)");
            }
            catch
            {
                Logger.LogWarning("FeatureMatcher: CUDA not available, falling back to CPU");
            }
        }

        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        try
        {
            _lightGlueSession = new InferenceSession(_modelPath, options);
            Logger.Log($"FeatureMatcher: Loaded LightGlue model from {_modelPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"FeatureMatcher: Failed to load LightGlue model: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Match features between two frames.
    /// </summary>
    public List<FeatureMatch> MatchFeatures(
        List<KeypointDetector.DetectedKeypoint> keypoints1,
        List<KeypointDetector.DetectedKeypoint> keypoints2,
        float ratioThreshold = 0.8f)
    {
        if (_useLightGlue && _lightGlueSession != null)
        {
            return MatchWithLightGlue(keypoints1, keypoints2);
        }
        else
        {
            return MatchWithBruteForce(keypoints1, keypoints2, ratioThreshold);
        }
    }

    private List<FeatureMatch> MatchWithLightGlue(
        List<KeypointDetector.DetectedKeypoint> keypoints1,
        List<KeypointDetector.DetectedKeypoint> keypoints2)
    {
        var matches = new List<FeatureMatch>();

        try
        {
            // Prepare inputs for LightGlue
            var desc1Tensor = CreateDescriptorTensor(keypoints1);
            var desc2Tensor = CreateDescriptorTensor(keypoints2);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("descriptors0", desc1Tensor),
                NamedOnnxValue.CreateFromTensor("descriptors1", desc2Tensor)
            };

            using var results = _lightGlueSession.Run(inputs);

            // Extract matches
            var matchIndices = results.FirstOrDefault(r => r.Name == "matches")?.AsEnumerable<long>().ToArray();
            var matchScores = results.FirstOrDefault(r => r.Name == "scores")?.AsEnumerable<float>().ToArray();

            if (matchIndices != null)
            {
                for (int i = 0; i < matchIndices.Length / 2; i++)
                {
                    int idx1 = (int)matchIndices[i * 2];
                    int idx2 = (int)matchIndices[i * 2 + 1];

                    if (idx1 >= 0 && idx1 < keypoints1.Count && idx2 >= 0 && idx2 < keypoints2.Count)
                    {
                        float score = matchScores != null && i < matchScores.Length ? matchScores[i] : 1.0f;

                        matches.Add(new FeatureMatch
                        {
                            Index1 = idx1,
                            Index2 = idx2,
                            Distance = 1.0f - score,
                            Point1 = keypoints1[idx1].Position,
                            Point2 = keypoints2[idx2].Position
                        });
                    }
                }
            }

            Logger.Log($"FeatureMatcher (LightGlue): Found {matches.Count} matches");
        }
        catch (Exception ex)
        {
            Logger.LogError($"FeatureMatcher: LightGlue matching failed: {ex.Message}");
            // Fallback to brute force
            return MatchWithBruteForce(keypoints1, keypoints2, 0.8f);
        }

        return matches;
    }

    private List<FeatureMatch> MatchWithBruteForce(
        List<KeypointDetector.DetectedKeypoint> keypoints1,
        List<KeypointDetector.DetectedKeypoint> keypoints2,
        float ratioThreshold)
    {
        var matches = new List<FeatureMatch>();

        for (int i = 0; i < keypoints1.Count; i++)
        {
            var desc1 = keypoints1[i].Descriptor;
            float bestDist = float.MaxValue;
            float secondBestDist = float.MaxValue;
            int bestIdx = -1;

            for (int j = 0; j < keypoints2.Count; j++)
            {
                var desc2 = keypoints2[j].Descriptor;
                float dist = ComputeL2Distance(desc1, desc2);

                if (dist < bestDist)
                {
                    secondBestDist = bestDist;
                    bestDist = dist;
                    bestIdx = j;
                }
                else if (dist < secondBestDist)
                {
                    secondBestDist = dist;
                }
            }

            // Lowe's ratio test
            if (bestIdx >= 0 && bestDist < ratioThreshold * secondBestDist)
            {
                matches.Add(new FeatureMatch
                {
                    Index1 = i,
                    Index2 = bestIdx,
                    Distance = bestDist,
                    Point1 = keypoints1[i].Position,
                    Point2 = keypoints2[bestIdx].Position
                });
            }
        }

        Logger.Log($"FeatureMatcher (Brute Force): Found {matches.Count} matches");
        return matches;
    }

    private DenseTensor<float> CreateDescriptorTensor(List<KeypointDetector.DetectedKeypoint> keypoints)
    {
        int numKeypoints = keypoints.Count;
        int descriptorDim = keypoints.Count > 0 ? keypoints[0].Descriptor.Length : 256;

        var tensor = new DenseTensor<float>(new[] { 1, numKeypoints, descriptorDim });

        for (int i = 0; i < numKeypoints; i++)
        {
            for (int j = 0; j < descriptorDim; j++)
            {
                tensor[0, i, j] = keypoints[i].Descriptor[j];
            }
        }

        return tensor;
    }

    private float ComputeL2Distance(float[] desc1, float[] desc2)
    {
        float sum = 0;
        for (int i = 0; i < Math.Min(desc1.Length, desc2.Length); i++)
        {
            float diff = desc1[i] - desc2[i];
            sum += diff * diff;
        }
        return MathF.Sqrt(sum);
    }

    public void Dispose()
    {
        _lightGlueSession?.Dispose();
    }
}
