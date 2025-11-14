// GeoscientistToolkit/Analysis/Photogrammetry/DepthAwareRansac.cs

using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Photogrammetry;

/// <summary>
/// RANSAC with depth constraints for pose estimation.
/// </summary>
public class DepthAwareRansac
{
    private readonly double _reprojectionThreshold;
    private readonly int _maxIterations;
    private readonly double _confidence;

    public class PoseEstimationResult
    {
        public Mat RotationMatrix { get; set; }
        public Mat TranslationVector { get; set; }
        public Mat EssentialMatrix { get; set; }
        public List<int> InlierIndices { get; set; }
        public double Scale { get; set; }
        public int NumInliers => InlierIndices?.Count ?? 0;
    }

    public DepthAwareRansac(double reprojectionThreshold = 1.0, int maxIterations = 1000, double confidence = 0.99)
    {
        _reprojectionThreshold = reprojectionThreshold;
        _maxIterations = maxIterations;
        _confidence = confidence;
    }

    /// <summary>
    /// Estimate pose using Essential matrix with depth-aware RANSAC.
    /// </summary>
    public PoseEstimationResult EstimatePose(
        List<FeatureMatcher.FeatureMatch> matches,
        Mat depth1,
        Mat depth2,
        Mat cameraMatrix)
    {
        if (matches == null || matches.Count < 8)
        {
            Logger.LogWarning("DepthAwareRansac: Not enough matches for pose estimation");
            return null;
        }

        // Extract point correspondences
        var points1 = matches.Select(m => m.Point1).ToArray();
        var points2 = matches.Select(m => m.Point2).ToArray();

        // Estimate Essential matrix using RANSAC
        var E = Cv2.FindEssentialMat(points1, points2, cameraMatrix,
            FundamentalMatMethods.Ransac, _confidence, _reprojectionThreshold, out var mask);

        if (E.Empty())
        {
            Logger.LogWarning("DepthAwareRansac: Failed to estimate Essential matrix");
            return null;
        }

        // Recover pose from Essential matrix
        var R = new Mat();
        var t = new Mat();
        int numInliers = Cv2.RecoverPose(E, points1, points2, cameraMatrix, R, t, mask);

        if (numInliers < 8)
        {
            Logger.LogWarning($"DepthAwareRansac: Too few inliers ({numInliers})");
            return null;
        }

        // Extract inlier indices
        var inlierIndices = new List<int>();
        for (int i = 0; i < mask.Rows; i++)
        {
            if (mask.At<byte>(i) > 0)
                inlierIndices.Add(i);
        }

        // Align depth scale using inliers
        double scale = EstimateDepthScale(matches, inlierIndices, depth1, depth2, R, t, cameraMatrix);

        // Filter matches based on depth consistency
        var depthFilteredInliers = FilterByDepthConsistency(matches, inlierIndices, depth1, depth2, scale);

        Logger.Log($"DepthAwareRansac: {numInliers} geometric inliers, " +
                        $"{depthFilteredInliers.Count} after depth filtering, scale={scale:F3}");

        return new PoseEstimationResult
        {
            RotationMatrix = R,
            TranslationVector = t,
            EssentialMatrix = E,
            InlierIndices = depthFilteredInliers,
            Scale = scale
        };
    }

    private double EstimateDepthScale(
        List<FeatureMatcher.FeatureMatch> matches,
        List<int> inlierIndices,
        Mat depth1,
        Mat depth2,
        Mat R,
        Mat t,
        Mat K)
    {
        var scaleRatios = new List<double>();

        foreach (var idx in inlierIndices)
        {
            var match = matches[idx];
            var pt1 = match.Point1;
            var pt2 = match.Point2;

            // Get depth values
            float d1 = GetDepthAt(depth1, pt1);
            float d2 = GetDepthAt(depth2, pt2);

            if (d1 > 0 && d2 > 0)
            {
                // Triangulate to get 3D point distance
                var triangulated = TriangulatePoint(pt1, pt2, K, R, t);
                if (triangulated > 0)
                {
                    double ratio = triangulated / d1;
                    scaleRatios.Add(ratio);
                }
            }
        }

        if (scaleRatios.Count == 0)
        {
            Logger.LogWarning("DepthAwareRansac: No valid depth ratios, using default scale 1.0");
            return 1.0;
        }

        // Use median for robustness
        scaleRatios.Sort();
        double scale = scaleRatios[scaleRatios.Count / 2];

        return scale;
    }

    private List<int> FilterByDepthConsistency(
        List<FeatureMatcher.FeatureMatch> matches,
        List<int> inlierIndices,
        Mat depth1,
        Mat depth2,
        double scale,
        double depthErrorThreshold = 0.5)
    {
        var filtered = new List<int>();

        foreach (var idx in inlierIndices)
        {
            var match = matches[idx];
            float d1 = GetDepthAt(depth1, match.Point1);
            float d2 = GetDepthAt(depth2, match.Point2);

            if (d1 > 0 && d2 > 0)
            {
                double scaledD1 = d1 * scale;
                double error = Math.Abs(scaledD1 - d2) / Math.Max(scaledD1, d2);

                if (error < depthErrorThreshold)
                {
                    filtered.Add(idx);
                }
            }
            else
            {
                // Keep inliers without valid depth
                filtered.Add(idx);
            }
        }

        return filtered;
    }

    private float GetDepthAt(Mat depthMap, Point2f point)
    {
        int x = (int)Math.Round(point.X);
        int y = (int)Math.Round(point.Y);

        if (x >= 0 && x < depthMap.Width && y >= 0 && y < depthMap.Height)
        {
            return depthMap.At<float>(y, x);
        }

        return 0;
    }

    private double TriangulatePoint(Point2f pt1, Point2f pt2, Mat K, Mat R, Mat t)
    {
        // Simple triangulation approximation
        // For more accurate triangulation, use Cv2.TriangulatePoints

        // This is a simplified approach - just return baseline distance
        double tx = t.At<double>(0);
        double ty = t.At<double>(1);
        double tz = t.At<double>(2);
        double baseline = Math.Sqrt(tx * tx + ty * ty + tz * tz);

        return baseline;
    }
}
