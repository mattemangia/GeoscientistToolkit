// GeoscientistToolkit/Analysis/Photogrammetry/PhotogrammetryPipeline.cs

using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Numerics;

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Complete real-time photogrammetry pipeline.
/// </summary>
public class PhotogrammetryPipeline : IDisposable
{
    private DepthEstimator _depthEstimator;
    private KeypointDetector _keypointDetector;
    private FeatureMatcher _featureMatcher;
    private readonly DepthAwareRansac _ransac;
    private readonly KeyframeManager _keyframeManager;
    private readonly VideoCaptureManager _videoCapture;
    private readonly GeoreferencingManager _georeferencing;
    private readonly MemoryManager _memoryManager;

    // State
    private Mat _previousFrame;
    private Mat _previousDepth;
    private List<KeypointDetector.DetectedKeypoint> _previousKeypoints;
    private Matrix4x4 _currentPose = Matrix4x4.Identity;
    private Mat _cameraMatrix;
    private Mat _distCoeffs;
    private bool _isInitialized;

    // Statistics
    private int _frameCount;
    private double _processingTime;

    public class PipelineConfig
    {
        public string DepthModelPath { get; set; }
        public DepthEstimator.DepthModelType DepthModelType { get; set; } = DepthEstimator.DepthModelType.MiDaSSmall;
        public string SuperPointModelPath { get; set; }
        public string LightGlueModelPath { get; set; }
        public bool UseGpu { get; set; } = false;
        public int KeyframeInterval { get; set; } = 10;
        public int TargetWidth { get; set; } = 640;
        public int TargetHeight { get; set; } = 480;

        // Distortion coefficients (k1, k2, p1, p2, k3, etc.)
        public double[] DistortionCoefficients { get; set; }

        // Camera intrinsics (will be estimated if not provided)
        public double FocalLengthX { get; set; } = 500;
        public double FocalLengthY { get; set; } = 500;
        public double PrincipalPointX { get; set; } = 320;
        public double PrincipalPointY { get; set; } = 240;
    }

    public VideoCaptureManager VideoCapture => _videoCapture;
    public KeyframeManager KeyframeManager => _keyframeManager;
    public GeoreferencingManager Georeferencing => _georeferencing;
    public MemoryManager MemoryManager => _memoryManager;
    public int FrameCount => _frameCount;
    public double AverageProcessingTime => _frameCount > 0 ? _processingTime / _frameCount : 0;
    public bool IsInitialized => _isInitialized;

    public PhotogrammetryPipeline(PipelineConfig config)
    {
        _ransac = new DepthAwareRansac();
        _keyframeManager = new KeyframeManager(config.KeyframeInterval);
        _videoCapture = new VideoCaptureManager();
        _georeferencing = new GeoreferencingManager();
        _memoryManager = new MemoryManager(_keyframeManager);

        // Initialize camera matrix
        _cameraMatrix = new Mat(3, 3, MatType.CV_64FC1);
        _cameraMatrix.Set(0, 0, config.FocalLengthX);
        _cameraMatrix.Set(1, 1, config.FocalLengthY);
        _cameraMatrix.Set(0, 2, config.PrincipalPointX);
        _cameraMatrix.Set(1, 2, config.PrincipalPointY);
        _cameraMatrix.Set(2, 2, 1.0);

        // Initialize distortion coefficients if provided
        if (config.DistortionCoefficients != null && config.DistortionCoefficients.Length > 0)
        {
            _distCoeffs = new Mat(1, config.DistortionCoefficients.Length, MatType.CV_64FC1);
            for (int i = 0; i < config.DistortionCoefficients.Length; i++)
            {
                _distCoeffs.Set(0, i, config.DistortionCoefficients[i]);
            }
        }

        // Pass intrinsics to KeyframeManager
        _keyframeManager.SetCameraIntrinsics(_cameraMatrix);

        // Initialize models
        InitializeModels(config);
    }

    private void InitializeModels(PipelineConfig config)
    {
        try
        {
            if (!string.IsNullOrEmpty(config.DepthModelPath))
            {
                _depthEstimator = new DepthEstimator(config.DepthModelPath, config.DepthModelType, config.UseGpu);
                Logger.Log("PhotogrammetryPipeline: Depth estimator initialized");
            }
            else
            {
                Logger.LogWarning("PhotogrammetryPipeline: No depth model specified, depth estimation disabled");
            }

            if (!string.IsNullOrEmpty(config.SuperPointModelPath))
            {
                _keypointDetector = new KeypointDetector(config.SuperPointModelPath, config.UseGpu);
                Logger.Log("PhotogrammetryPipeline: Keypoint detector initialized");
            }
            else
            {
                Logger.LogWarning("PhotogrammetryPipeline: No SuperPoint model specified, using traditional features");
            }

            _featureMatcher = new FeatureMatcher(config.LightGlueModelPath, config.UseGpu);
            Logger.Log("PhotogrammetryPipeline: Feature matcher initialized");

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"PhotogrammetryPipeline: Failed to initialize models: {ex.Message}");
            _isInitialized = false;
        }
    }

    /// <summary>
    /// Process a single frame through the pipeline.
    /// </summary>
    public ProcessingResult ProcessFrame(Mat inputFrame)
    {
        if (!_isInitialized)
        {
            Logger.LogWarning("PhotogrammetryPipeline: Pipeline not initialized");
            return null;
        }

        var startTime = DateTime.Now;
        var result = new ProcessingResult();

        try
        {
            // 1. Preprocess & undistort
            var preprocessed = PreprocessFrame(inputFrame);
            result.PreprocessedFrame = preprocessed;

            // 2. Depth estimation
            Mat depthMap = null;
            if (_depthEstimator != null)
            {
                depthMap = _depthEstimator.EstimateDepth(preprocessed);
                result.DepthMap = depthMap;
            }

            // 3. Keypoint detection
            List<KeypointDetector.DetectedKeypoint> keypoints = null;
            if (_keypointDetector != null)
            {
                keypoints = _keypointDetector.DetectKeypoints(preprocessed);
                result.Keypoints = keypoints;
            }
            else
            {
                // Fallback: use traditional features (e.g., ORB)
                keypoints = DetectTraditionalFeatures(preprocessed);
                result.Keypoints = keypoints;
            }

            // 4. Feature matching and pose estimation (if we have a previous frame)
            if (_previousFrame != null && _previousKeypoints != null && keypoints != null)
            {
                var matches = _featureMatcher.MatchFeatures(_previousKeypoints, keypoints);
                result.Matches = matches;

                if (matches.Count >= 8 && depthMap != null && _previousDepth != null)
                {
                    // 5. RANSAC with depth constraints
                    var poseResult = _ransac.EstimatePose(matches, _previousDepth, depthMap, _cameraMatrix);

                    if (poseResult != null)
                    {
                        result.PoseEstimation = poseResult;

                        // Update current pose
                        UpdatePose(poseResult);

                        // 6. Keyframe management
                        if (_keyframeManager.ShouldCreateKeyframe())
                        {
                            var keyframe = _keyframeManager.AddKeyframe(
                                preprocessed,
                                depthMap,
                                keypoints,
                                _currentPose,
                                poseResult.Scale);

                            result.NewKeyframe = keyframe;

                            // Optionally perform bundle adjustment in background
                            _keyframeManager.PerformBundleAdjustment();
                        }
                    }
                }
            }

            // Update state for next frame
            _previousFrame?.Dispose();
            _previousFrame = preprocessed.Clone();

            _previousDepth?.Dispose();
            _previousDepth = depthMap?.Clone();

            _previousKeypoints = keypoints;

            _frameCount++;
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            _processingTime += elapsed;

            result.ProcessingTimeMs = elapsed;
            result.Success = true;

            // Check memory and cleanup if needed
            _memoryManager.CheckAndCleanup();
        }
        catch (Exception ex)
        {
            Logger.LogError($"PhotogrammetryPipeline: Frame processing failed: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private Mat PreprocessFrame(Mat input)
    {
        Mat processed = new Mat();

        // Resize if needed
        Cv2.Resize(input, processed, new Size(_cameraMatrix.At<double>(0, 2) * 2, _cameraMatrix.At<double>(1, 2) * 2));

        // Undistort if coefficients are known
        if (_distCoeffs != null)
        {
            Mat undistorted = new Mat();
            Cv2.Undistort(processed, undistorted, _cameraMatrix, _distCoeffs);
            processed.Dispose();
            processed = undistorted;
        }

        return processed;
    }

    private List<KeypointDetector.DetectedKeypoint> DetectTraditionalFeatures(Mat image)
    {
        // Fallback: use ORB features
        var orb = ORB.Create(1000);
        var descriptors = new Mat();
        orb.DetectAndCompute(image, null, out var cvKeypoints, descriptors);

        var keypoints = new List<KeypointDetector.DetectedKeypoint>();

        if (cvKeypoints != null && descriptors.Height > 0)
        {
            for (int i = 0; i < cvKeypoints.Length; i++)
            {
                var kp = cvKeypoints[i];
                var desc = new float[descriptors.Width];

                for (int j = 0; j < descriptors.Width; j++)
                {
                    desc[j] = descriptors.At<byte>(i, j);
                }

                keypoints.Add(new KeypointDetector.DetectedKeypoint
                {
                    Position = kp.Pt,
                    Confidence = kp.Response,
                    Descriptor = desc
                });
            }
        }

        descriptors?.Dispose();
        orb.Dispose();

        return keypoints;
    }

    private void UpdatePose(DepthAwareRansac.PoseEstimationResult poseResult)
    {
        // Convert rotation and translation to Matrix4x4
        var R = poseResult.RotationMatrix;
        var t = poseResult.TranslationVector;

        var pose = new Matrix4x4(
            (float)R.At<double>(0, 0), (float)R.At<double>(0, 1), (float)R.At<double>(0, 2), (float)t.At<double>(0),
            (float)R.At<double>(1, 0), (float)R.At<double>(1, 1), (float)R.At<double>(1, 2), (float)t.At<double>(1),
            (float)R.At<double>(2, 0), (float)R.At<double>(2, 1), (float)R.At<double>(2, 2), (float)t.At<double>(2),
            0, 0, 0, 1
        );

        // Accumulate pose (multiply with previous)
        _currentPose = pose * _currentPose;
    }

    public void Reset()
    {
        _previousFrame?.Dispose();
        _previousFrame = null;
        _previousDepth?.Dispose();
        _previousDepth = null;
        _previousKeypoints = null;
        _currentPose = Matrix4x4.Identity;
        _frameCount = 0;
        _processingTime = 0;

        _keyframeManager.Clear();

        Logger.Log("PhotogrammetryPipeline: Reset complete");
    }

    public void Dispose()
    {
        _depthEstimator?.Dispose();
        _keypointDetector?.Dispose();
        _featureMatcher?.Dispose();
        _keyframeManager?.Dispose();
        _videoCapture?.Dispose();
        _previousFrame?.Dispose();
        _previousDepth?.Dispose();
        _cameraMatrix?.Dispose();
        _distCoeffs?.Dispose();
    }

    public class ProcessingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public Mat PreprocessedFrame { get; set; }
        public Mat DepthMap { get; set; }
        public List<KeypointDetector.DetectedKeypoint> Keypoints { get; set; }
        public List<FeatureMatcher.FeatureMatch> Matches { get; set; }
        public DepthAwareRansac.PoseEstimationResult PoseEstimation { get; set; }
        public KeyframeManager.Keyframe NewKeyframe { get; set; }
        public double ProcessingTimeMs { get; set; }
    }
}
