// GeoscientistToolkit/Analysis/Photogrammetry/KeyframeManager.cs

using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Manages keyframes with 2.5D information (keypoints + depth).
/// </summary>
public class KeyframeManager
{
    private readonly List<Keyframe> _keyframes = new();
    private readonly int _keyframeInterval;
    private int _frameCounter = 0;

    public class Keyframe
    {
        public int FrameId { get; set; }
        public Mat Image { get; set; }
        public Mat DepthMap { get; set; }
        public List<KeypointDetector.DetectedKeypoint> Keypoints { get; set; }
        public List<Point3f> Points3D { get; set; } // 3D sparse cloud
        public Matrix4x4 Pose { get; set; } // Camera pose (world to camera)
        public DateTime Timestamp { get; set; }
    }

    public List<Keyframe> Keyframes => _keyframes;

    public KeyframeManager(int keyframeInterval = 10)
    {
        _keyframeInterval = keyframeInterval;
    }

    /// <summary>
    /// Check if current frame should be promoted to keyframe.
    /// </summary>
    public bool ShouldCreateKeyframe()
    {
        _frameCounter++;
        return _frameCounter % _keyframeInterval == 0;
    }

    /// <summary>
    /// Create and add a new keyframe.
    /// </summary>
    public Keyframe AddKeyframe(
        Mat image,
        Mat depthMap,
        List<KeypointDetector.DetectedKeypoint> keypoints,
        Matrix4x4 pose,
        double depthScale)
    {
        // Create 3D points from keypoints and depth
        var points3D = new List<Point3f>();

        foreach (var kp in keypoints)
        {
            int x = (int)Math.Round(kp.Position.X);
            int y = (int)Math.Round(kp.Position.Y);

            if (x >= 0 && x < depthMap.Width && y >= 0 && y < depthMap.Height)
            {
                float depth = depthMap.At<float>(y, x);
                if (depth > 0)
                {
                    float scaledDepth = (float)(depth * depthScale);
                    points3D.Add(new Point3f(kp.Position.X, kp.Position.Y, scaledDepth));
                }
            }
        }

        var keyframe = new Keyframe
        {
            FrameId = _frameCounter,
            Image = image.Clone(),
            DepthMap = depthMap.Clone(),
            Keypoints = keypoints,
            Points3D = points3D,
            Pose = pose,
            Timestamp = DateTime.Now
        };

        _keyframes.Add(keyframe);
        Logger.Log($"KeyframeManager: Added keyframe {keyframe.FrameId} with {points3D.Count} 3D points");

        return keyframe;
    }

    /// <summary>
    /// Estimate pose using PnP against the most recent keyframe.
    /// </summary>
    public bool EstimatePoseFromKeyframe(
        List<FeatureMatcher.FeatureMatch> matches,
        Keyframe keyframe,
        Mat cameraMatrix,
        out Mat rvec,
        out Mat tvec)
    {
        rvec = null;
        tvec = null;

        if (keyframe == null || matches.Count < 4)
            return false;

        // Extract 3D-2D correspondences
        var objectPoints = new List<Point3f>();
        var imagePoints = new List<Point2f>();

        foreach (var match in matches)
        {
            if (match.Index1 < keyframe.Points3D.Count)
            {
                var pt3D = keyframe.Points3D[match.Index1];
                if (pt3D.Z > 0)
                {
                    objectPoints.Add(pt3D);
                    imagePoints.Add(match.Point2);
                }
            }
        }

        if (objectPoints.Count < 4)
            return false;

        try
        {
            // Solve PnP with RANSAC
            rvec = new Mat();
            tvec = new Mat();
            var inliers = new Mat();

            Cv2.SolvePnPRansac(
                InputArray.Create(objectPoints.ToArray()),
                InputArray.Create(imagePoints.ToArray()),
                cameraMatrix,
                null, // No distortion
                rvec,
                tvec,
                false,
                100, // iterations
                8.0f, // reprojection error
                0.99, // confidence
                inliers);

            // Check if PnP succeeded by verifying output matrices are valid
            if (!rvec.Empty() && !tvec.Empty() && inliers.Height >= 4)
            {
                Logger.Log($"KeyframeManager: PnP solved with {inliers.Height} inliers");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"KeyframeManager: PnP failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Get the most recent keyframe.
    /// </summary>
    public Keyframe GetLastKeyframe()
    {
        return _keyframes.Count > 0 ? _keyframes[^1] : null;
    }

    /// <summary>
    /// Simple bundle adjustment (Gauss-Newton optimization).
    /// This is a simplified version - for production use a proper BA library.
    /// </summary>
    public void PerformBundleAdjustment()
    {
        if (_keyframes.Count < 2)
            return;

        // Simplified BA: just log that it would happen
        Logger.Log($"KeyframeManager: Would perform bundle adjustment on {_keyframes.Count} keyframes");

        // In a real implementation, you would:
        // 1. Build the optimization problem (reprojection errors)
        // 2. Use Levenberg-Marquardt or similar to optimize camera poses and 3D points
        // 3. Update keyframe poses and 3D points
    }

    public void Clear()
    {
        foreach (var kf in _keyframes)
        {
            kf.Image?.Dispose();
            kf.DepthMap?.Dispose();
        }
        _keyframes.Clear();
        _frameCounter = 0;
    }

    public void Dispose()
    {
        Clear();
    }
}
