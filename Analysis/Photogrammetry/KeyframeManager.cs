// GeoscientistToolkit/Analysis/Photogrammetry/KeyframeManager.cs

using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

using GeoscientistToolkit.Util;

// MathNet is used for Bundle Adjustment
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// Manages keyframes with 2.5D information (keypoints + depth).
/// </summary>
public class KeyframeManager
{
    private readonly List<Keyframe> _keyframes = new();
    private readonly int _keyframeInterval;
    private int _frameCounter = 0;

    // Camera intrinsics
    private Mat _cameraMatrix;
    private double _fx, _fy, _cx, _cy;

    public class Keyframe
    {
        public int FrameId { get; set; }
        public Mat Image { get; set; }
        public Mat DepthMap { get; set; }
        public List<KeypointDetector.DetectedKeypoint> Keypoints { get; set; }

        /// <summary>
        /// 3D points in World Coordinates corresponding to Keypoints.
        /// Aligned 1:1 with Keypoints. If depth is invalid, Point3f is marked invalid (NaN).
        /// </summary>
        public List<Point3f> Points3D { get; set; }

        public Matrix4x4 Pose { get; set; } // Camera pose (Camera to World)

        public DateTime Timestamp { get; set; }
    }

    public List<Keyframe> Keyframes => _keyframes;

    public KeyframeManager(int keyframeInterval = 10)
    {
        _keyframeInterval = keyframeInterval;
    }

    public void SetCameraIntrinsics(Mat cameraMatrix)
    {
        _cameraMatrix = cameraMatrix.Clone();
        _fx = _cameraMatrix.At<double>(0, 0);
        _fy = _cameraMatrix.At<double>(1, 1);
        _cx = _cameraMatrix.At<double>(0, 2);
        _cy = _cameraMatrix.At<double>(1, 2);
    }

    public void SetCameraIntrinsics(double fx, double fy, double cx, double cy)
    {
        _fx = fx;
        _fy = fy;
        _cx = cx;
        _cy = cy;
        // _cameraMatrix remains null if set this way, but BA uses scalars.
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
        var points3D = new List<Point3f>(keypoints.Count);

        bool hasIntrinsics = _fx != 0 && _fy != 0;

        for (int i = 0; i < keypoints.Count; i++)
        {
            var kp = keypoints[i];
            int x = (int)Math.Round(kp.Position.X);
            int y = (int)Math.Round(kp.Position.Y);

            bool isValid = false;
            float Zc = 0;
            float Xc = 0;
            float Yc = 0;

            if (x >= 0 && x < depthMap.Width && y >= 0 && y < depthMap.Height)
            {
                float depth = depthMap.At<float>(y, x);
                if (depth > 0)
                {
                    float scaledDepth = (float)(depth * depthScale);

                    if (hasIntrinsics)
                    {
                        // Unproject to Camera Space
                        Zc = scaledDepth;
                        Xc = (kp.Position.X - (float)_cx) * Zc / (float)_fx;
                        Yc = (kp.Position.Y - (float)_cy) * Zc / (float)_fy;
                        isValid = true;
                    }
                    else
                    {
                        // Fallback (Legacy behavior)
                        Xc = kp.Position.X;
                        Yc = kp.Position.Y;
                        Zc = scaledDepth;
                        isValid = true;
                    }
                }
            }

            if (isValid)
            {
                if (hasIntrinsics)
                {
                    // Transform to World Space
                    var localPos = new Vector3(Xc, Yc, Zc);
                    var worldPos = Vector3.Transform(localPos, pose);
                    points3D.Add(new Point3f(worldPos.X, worldPos.Y, worldPos.Z));
                }
                else
                {
                    // Store local/raw
                    points3D.Add(new Point3f(Xc, Yc, Zc));
                }
            }
            else
            {
                // Add invalid point to maintain alignment
                points3D.Add(new Point3f(float.NaN, float.NaN, float.NaN));
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
        int validCount = points3D.Count(p => !float.IsNaN(p.Z));
        Logger.Log($"KeyframeManager: Added keyframe {keyframe.FrameId} with {validCount} valid 3D points");

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
            // match.Index1 is index in previous keypoints (keyframe.Keypoints)
            if (match.Index1 < keyframe.Points3D.Count)
            {
                var pt3D = keyframe.Points3D[match.Index1];

                // Check if point is valid (not NaN)
                if (!float.IsNaN(pt3D.Z))
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
    /// Perform full Bundle Adjustment to refine camera poses and 3D points.
    /// </summary>
    public void PerformBundleAdjustment()
    {
        if (_keyframes.Count < 2)
            return;

        if (_fx == 0 || _fy == 0)
        {
             Logger.LogWarning("KeyframeManager: Cannot perform BA without intrinsics.");
             return;
        }

        Logger.Log($"KeyframeManager: Starting bundle adjustment on {_keyframes.Count} keyframes...");

        try
        {
            // 1. Build the graph (Keyframes + MapPoints)
            var (mapPoints, observations) = BuildGraph();

            if (mapPoints.Count == 0)
            {
                Logger.LogWarning("KeyframeManager: No map points found for BA.");
                return;
            }

            Logger.Log($"KeyframeManager: Graph built. {mapPoints.Count} points, {observations.Count} observations.");

            // 2. Optimization Loop (Alternating Optimization)
            OptimizeGraph(mapPoints, observations);

            // 3. Update Keyframes
            UpdateKeyframes(mapPoints);

            Logger.Log("KeyframeManager: Bundle adjustment completed.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"KeyframeManager: BA failed: {ex.Message}");
        }
    }

    // --- Helper Classes for BA ---

    private class MapPoint
    {
        public int Id { get; set; }
        public Vector3 Position { get; set; } // World Coordinate
        public List<Observation> Observations { get; set; } = new();
    }

    private class Observation
    {
        public int KeyframeIndex { get; set; }
        public int KeypointIndex { get; set; } // Index in Keyframe.Keypoints
        public Vector2 Pixel { get; set; }
    }

    // --- Graph Building ---

    private (List<MapPoint>, List<Observation>) BuildGraph()
    {
        var mapPoints = new List<MapPoint>();
        var observations = new List<Observation>();
        var keypointToMapPoint = new Dictionary<(int kfIdx, int kpIdx), MapPoint>();

        // Iterate through consecutive keyframes to find matches
        for (int i = 0; i < _keyframes.Count - 1; i++)
        {
            var kf1 = _keyframes[i];
            var kf2 = _keyframes[i + 1];

            // Match features between kf1 and kf2
            var matches = MatchFeatures(kf1, kf2);

            foreach (var match in matches)
            {
                // match.Index1 in kf1, match.Index2 in kf2

                // Check if kf1 point is already in a MapPoint
                MapPoint mp = null;
                if (keypointToMapPoint.TryGetValue((i, match.Index1), out var existingMp))
                {
                    mp = existingMp;
                }
                else
                {
                    // Create new MapPoint
                    var p3d = kf1.Points3D[match.Index1];
                    if (float.IsNaN(p3d.Z)) continue; // Skip if invalid

                    mp = new MapPoint
                    {
                        Id = mapPoints.Count,
                        Position = new Vector3(p3d.X, p3d.Y, p3d.Z)
                    };
                    mapPoints.Add(mp);

                    var obs1 = new Observation { KeyframeIndex = i, KeypointIndex = match.Index1, Pixel = kf1.Keypoints[match.Index1].Position.ToVector2() };
                    mp.Observations.Add(obs1);
                    observations.Add(obs1);
                    keypointToMapPoint[(i, match.Index1)] = mp;
                }

                if (!keypointToMapPoint.ContainsKey((i + 1, match.Index2)))
                {
                    var obs2 = new Observation { KeyframeIndex = i + 1, KeypointIndex = match.Index2, Pixel = kf2.Keypoints[match.Index2].Position.ToVector2() };
                    mp.Observations.Add(obs2);
                    observations.Add(obs2);
                    keypointToMapPoint[(i + 1, match.Index2)] = mp;
                }
            }
        }

        return (mapPoints, observations);
    }

    private List<FeatureMatcher.FeatureMatch> MatchFeatures(Keyframe kf1, Keyframe kf2)
    {
        var matches = new List<FeatureMatcher.FeatureMatch>();
        var descriptors1 = kf1.Keypoints;
        var descriptors2 = kf2.Keypoints;
        float threshold = 0.5f;

        // Brute force with cross-check
        for (int i = 0; i < descriptors1.Count; i++)
        {
            var d1 = descriptors1[i].Descriptor;
            if (d1 == null) continue;

            int bestJ = -1;
            float minDist = float.MaxValue;

            for (int j = 0; j < descriptors2.Count; j++)
            {
                var d2 = descriptors2[j].Descriptor;
                if (d2 == null) continue;

                float dist = L2DistanceSq(d1, d2);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestJ = j;
                }
            }

            if (minDist < threshold && bestJ != -1)
            {
                int bestI = -1;
                float minDistBack = float.MaxValue;
                var d2 = descriptors2[bestJ].Descriptor;

                for (int k = 0; k < descriptors1.Count; k++)
                {
                    var d1k = descriptors1[k].Descriptor;
                    if (d1k == null) continue;
                    float dist = L2DistanceSq(d2, d1k);
                    if (dist < minDistBack)
                    {
                        minDistBack = dist;
                        bestI = k;
                    }
                }

                if (bestI == i)
                {
                    matches.Add(new FeatureMatcher.FeatureMatch
                    {
                        Index1 = i,
                        Index2 = bestJ,
                        Point1 = kf1.Keypoints[i].Position,
                        Point2 = kf2.Keypoints[bestJ].Position
                    });
                }
            }
        }

        return matches;
    }

    private float L2DistanceSq(float[] d1, float[] d2)
    {
        float sum = 0;
        int len = Math.Min(d1.Length, d2.Length);
        for (int i = 0; i < len; i++)
        {
            float diff = d1[i] - d2[i];
            sum += diff * diff;
        }
        return sum;
    }

    // --- Optimization ---

    private void OptimizeGraph(List<MapPoint> mapPoints, List<Observation> observations)
    {
        int iterations = 10;
        var poses = _keyframes.Select(k => k.Pose).ToArray();

        for (int iter = 0; iter < iterations; iter++)
        {
            foreach (var mp in mapPoints)
            {
                mp.Position = OptimizePoint(mp, poses);
            }

            for (int k = 0; k < poses.Length; k++)
            {
                var obsForFrame = observations.Where(o => o.KeyframeIndex == k).ToList();
                if (obsForFrame.Count < 4) continue;
                poses[k] = OptimizePose(k, poses[k], obsForFrame, mapPoints);
            }
        }

        for (int i = 0; i < _keyframes.Count; i++)
        {
            _keyframes[i].Pose = poses[i];
        }
    }

    private Vector3 OptimizePoint(MapPoint mp, Matrix4x4[] poses)
    {
        var currentPos = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(new double[] { mp.Position.X, mp.Position.Y, mp.Position.Z });
        double lambda = 0.001;

        for (int it = 0; it < 5; it++)
        {
            var H = Matrix<double>.Build.Dense(3, 3);
            var b = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(3);

            foreach (var obs in mp.Observations)
            {
                var pose = poses[obs.KeyframeIndex];
                if (!Matrix4x4.Invert(pose, out var viewMat)) continue;

                var worldPos = new Vector3((float)currentPos[0], (float)currentPos[1], (float)currentPos[2]);
                var camPos = Vector3.Transform(worldPos, viewMat);

                if (camPos.Z <= 0) continue;

                float u = (float)(_fx * camPos.X / camPos.Z + _cx);
                float v = (float)(_fy * camPos.Y / camPos.Z + _cy);

                double ex = u - obs.Pixel.X;
                double ey = v - obs.Pixel.Y;

                double z_inv = 1.0 / camPos.Z;
                double z_sq = z_inv * z_inv;

                double du_dXc = _fx * z_inv;
                double du_dZc = -_fx * camPos.X * z_sq;
                double dv_dYc = _fy * z_inv;
                double dv_dZc = -_fy * camPos.Y * z_sq;

                double r11 = viewMat.M11, r12 = viewMat.M21, r13 = viewMat.M31;
                double r21 = viewMat.M12, r22 = viewMat.M22, r23 = viewMat.M32;
                double r31 = viewMat.M13, r32 = viewMat.M23, r33 = viewMat.M33;

                double j11 = du_dXc * r11 + du_dZc * r31;
                double j12 = du_dXc * r12 + du_dZc * r32;
                double j13 = du_dXc * r13 + du_dZc * r33;

                double j21 = dv_dYc * r21 + dv_dZc * r31;
                double j22 = dv_dYc * r22 + dv_dZc * r32;
                double j23 = dv_dYc * r23 + dv_dZc * r33;

                var J_row1 = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(new[] { j11, j12, j13 });
                var J_row2 = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(new[] { j21, j22, j23 });

                H += J_row1.OuterProduct(J_row1);
                H += J_row2.OuterProduct(J_row2);
                b -= J_row1 * ex;
                b -= J_row2 * ey;
            }

            for (int i = 0; i < 3; i++) H[i, i] += lambda;

            try
            {
                var delta = H.Solve(b);
                currentPos += delta;
            }
            catch { break; }
        }

        return new Vector3((float)currentPos[0], (float)currentPos[1], (float)currentPos[2]);
    }

    private Matrix4x4 OptimizePose(int kfIndex, Matrix4x4 currentPose, List<Observation> observations, List<MapPoint> mapPoints)
    {
        var pose = currentPose;
        double lambda = 0.001;

        for (int it = 0; it < 5; it++)
        {
            Matrix4x4.Invert(pose, out var viewMat);
            var H = Matrix<double>.Build.Dense(6, 6);
            var b = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(6);

            foreach (var obs in observations)
            {
                var mapPoint = mapPoints.FirstOrDefault(m => m.Observations.Contains(obs));
                if (mapPoint == null) continue;

                var worldPos = mapPoint.Position;
                var camPos = Vector3.Transform(worldPos, viewMat);

                if (camPos.Z <= 0) continue;

                float u = (float)(_fx * camPos.X / camPos.Z + _cx);
                float v = (float)(_fy * camPos.Y / camPos.Z + _cy);

                double ex = u - obs.Pixel.X;
                double ey = v - obs.Pixel.Y;

                double X = camPos.X;
                double Y = camPos.Y;
                double Z = camPos.Z;
                double Z2 = Z*Z;
                double fx = _fx;
                double fy = _fy;

                double j1_tx = fx / Z;
                double j1_ty = 0;
                double j1_tz = -fx * X / Z2;
                double j1_rx = -fx * X * Y / Z2;
                double j1_ry = fx * (1 + X * X / Z2);
                double j1_rz = -fx * Y / Z;

                double j2_tx = 0;
                double j2_ty = fy / Z;
                double j2_tz = -fy * Y / Z2;
                double j2_rx = -fy * (1 + Y * Y / Z2);
                double j2_ry = fy * X * Y / Z2;
                double j2_rz = fy * X / Z;

                var J_row1 = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(new[] { j1_tx, j1_ty, j1_tz, j1_rx, j1_ry, j1_rz });
                var J_row2 = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(new[] { j2_tx, j2_ty, j2_tz, j2_rx, j2_ry, j2_rz });

                H += J_row1.OuterProduct(J_row1);
                H += J_row2.OuterProduct(J_row2);
                b -= J_row1 * ex;
                b -= J_row2 * ey;
            }

            for (int i = 0; i < 6; i++) H[i, i] += lambda;

            try
            {
                var delta = H.Solve(b);
                var deltaMat = BuildUpdateMatrix((float)delta[0], (float)delta[1], (float)delta[2], (float)delta[3], (float)delta[4], (float)delta[5]);

                Matrix4x4.Invert(pose, out var currentView);
                var newView = deltaMat * currentView;
                Matrix4x4.Invert(newView, out pose);
            }
            catch { break; }
        }

        return pose;
    }

    private Matrix4x4 BuildUpdateMatrix(float tx, float ty, float tz, float rx, float ry, float rz)
    {
        var rot = Matrix4x4.CreateFromYawPitchRoll(ry, rx, rz);
        rot.Translation = new Vector3(tx, ty, tz);
        return rot;
    }

    private void UpdateKeyframes(List<MapPoint> mapPoints)
    {
        foreach (var mp in mapPoints)
        {
            foreach (var obs in mp.Observations)
            {
                var kf = _keyframes[obs.KeyframeIndex];
                var p3d = kf.Points3D[obs.KeypointIndex];
                if (!float.IsNaN(p3d.Z))
                {
                     kf.Points3D[obs.KeypointIndex] = new Point3f(mp.Position.X, mp.Position.Y, mp.Position.Z);
                }
            }
        }
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
        _cameraMatrix?.Dispose();
    }
}

public static class ExtensionMethods
{
    public static Vector2 ToVector2(this Point2f p) => new Vector2(p.X, p.Y);
}
