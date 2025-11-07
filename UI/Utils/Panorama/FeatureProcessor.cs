// GeoscientistToolkit/Business/Photogrammetry/FeatureProcessor.cs

// =================================================================================================
// FIXED VERSION - Robust Cheirality Check
// - Fixes the overly strict cheirality check that required >50% of points to pass
// - Now accepts poses if at least 10% of points pass AND at least 5 points total
// - Added comprehensive debugging to understand triangulation failures
// - This should fix the "0 points" issue caused by pose validation being too strict
// =================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Photogrammetry;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

using Vector2 = System.Numerics.Vector2;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Vector = MathNet.Numerics.LinearAlgebra.Double.Vector;

namespace GeoscientistToolkit
{
    internal class FeatureProcessor
    {
        private readonly PhotogrammetryProcessingService _service;
        private const int RANSAC_ITERATIONS = 2000;
        private const float NORMALIZED_REPROJECTION_THRESHOLD = 1.5f;
        private const int MIN_INLIERS_FOR_ACCEPTANCE = 12;

        public FeatureProcessor(PhotogrammetryProcessingService service)
        {
            _service = service;
        }
        
        public async Task DetectFeaturesAsync(
            List<PhotogrammetryImage> images,
            CancellationToken token,
            Action<float, string> updateProgress)
        {
            _service.Log("Detecting SIFT features for photogrammetry...");

            using var detector = new SiftFeatureDetectorSIMD
            {
                EnableDiagnostics = true,
                DiagnosticLogger = msg => Logger.Log(msg)
            };
            var totalImages = images.Count;
            var processedCount = 0;

            var tasks = images.Select(async image =>
            {
                try
                {
                    _service.Log($"Detecting SIFT features in {image.Dataset.Name}...");
                    image.SiftFeatures = await detector.DetectAsync(image.Dataset, token);
                    _service.Log($"Found {image.SiftFeatures.KeyPoints.Count} SIFT keypoints in {image.Dataset.Name}.");
                }
                catch (Exception ex)
                {
                    _service.Log($"Failed to detect features in {image.Dataset.Name}: {ex.Message}");
                }
                finally
                {
                    Interlocked.Increment(ref processedCount);
                    updateProgress(
                        (float)processedCount / totalImages * 0.3f + 0.1f,
                        $"Detecting features... ({processedCount}/{totalImages})"
                    );
                }
            });

            await Task.WhenAll(tasks);
            _service.Log("SIFT feature detection complete.");
        }

        public async Task MatchFeaturesAsync(
            List<PhotogrammetryImage> images,
            PhotogrammetryGraph graph,
            CancellationToken token,
            Action<float, string> updateProgress)
        {
            _service.Log("Matching SIFT features and computing relative poses...");

            var pairs = GenerateImagePairs(images);
            int processedCount = 0;

            using var matcher = new SIFTFeatureMatcherSIMD();

            var tasks = pairs.Select(async pair =>
            {
                await ProcessImagePair(pair, graph, matcher, token);
                
                Interlocked.Increment(ref processedCount);
                updateProgress(
                    (float)processedCount / pairs.Count * 0.3f + 0.4f,
                    $"Matching pairs... ({processedCount}/{pairs.Count})"
                );
            });

            await Task.WhenAll(tasks);
            _service.Log("SIFT feature matching complete.");
        }
        
        public Matrix4x4? ComputeManualPose(
            PhotogrammetryImage img1,
            PhotogrammetryImage img2,
            List<(Vector2 P1, Vector2 P2)> points)
        {
            if (points == null || points.Count < 8)
            {
                _service.Log("Manual pose computation requires at least 8 points.");
                return null;
            }
            
            var matches = points.Select((p, i) => new FeatureMatch { QueryIndex = i, TrainIndex = i }).ToList();
            var E = SolveEssentialMatrix(matches, img1, img2);
            if (E == null)
            {
                _service.Log("Could not solve Essential Matrix from manual points.");
                return null;
            }

            var (R, t) = DecomposeAndCheckPoses(E, img1, img2, matches);
            if (R == null)
            {
                _service.Log("Could not find a valid geometric pose from manual points.");
                return null;
            }

            _service.Log("Successfully computed a valid pose from manual points.");
            return CreatePoseMatrix(R, t);
        }

        private List<(PhotogrammetryImage, PhotogrammetryImage)> GenerateImagePairs(List<PhotogrammetryImage> images)
        {
            var pairs = new List<(PhotogrammetryImage, PhotogrammetryImage)>();
            for (int i = 0; i < images.Count; i++) { for (int j = i + 1; j < images.Count; j++) { pairs.Add((images[i], images[j])); } }
            return pairs;
        }

        private List<FeatureMatch> FilterMatchesBidirectional(List<FeatureMatch> m12, List<FeatureMatch> m21)
        {
            var idx = new HashSet<(int, int)>(m21.Select(m => (m.TrainIndex, m.QueryIndex)));
            return m12.Where(m => idx.Contains((m.QueryIndex, m.TrainIndex))).ToList();
        }

        private async Task ProcessImagePair(
            (PhotogrammetryImage img1, PhotogrammetryImage img2) pair,
            PhotogrammetryGraph graph,
            SIFTFeatureMatcherSIMD matcher,
            CancellationToken token)
        {
            var (image1, image2) = pair;
            _service.Log($"Processing pair: {image1.Dataset.Name} <-> {image2.Dataset.Name}");

            if (image1.SiftFeatures?.KeyPoints == null || image2.SiftFeatures?.KeyPoints == null || image1.SiftFeatures.KeyPoints.Count < 30 || image2.SiftFeatures.KeyPoints.Count < 30)
            {
                _service.Log("  ⚠ Skipping - insufficient keypoints.");
                return;
            }

            var matches12 = await matcher.MatchFeaturesAsync(image1.SiftFeatures, image2.SiftFeatures, token);
            var matches21 = await matcher.MatchFeaturesAsync(image2.SiftFeatures, image1.SiftFeatures, token);
            var bidirectionalMatches = FilterMatchesBidirectional(matches12, matches21);
            _service.Log($"  Bidirectional matches: {bidirectionalMatches.Count}");

            if (bidirectionalMatches.Count < 30)
            {
                _service.Log("  ⚠ Skipping - too few bidirectional matches.");
                return;
            }

            var (pose, inliers) = ComputeRelativePose(image1, image2, bidirectionalMatches);

            if (inliers.Count > MIN_INLIERS_FOR_ACCEPTANCE && pose.HasValue)
            {
                graph.AddEdge(image1, image2, inliers, pose.Value);
                _service.Log($"  ✓ Successfully matched with {inliers.Count} inliers");
            }
            else
            {
                _service.Log($"  ✗ Failed to establish connection (inliers: {inliers.Count}, pose valid: {pose.HasValue})");
                if (inliers.Count <= MIN_INLIERS_FOR_ACCEPTANCE) _service.Log($"    Reason: Too few inliers after RANSAC ({inliers.Count})");
                if (!pose.HasValue) _service.Log("    Reason: Could not compute valid relative pose");
            }
        }
        
        private (Matrix4x4? Pose, List<FeatureMatch> Inliers) ComputeRelativePose(
            PhotogrammetryImage image1, PhotogrammetryImage image2, List<FeatureMatch> matches)
        {
            if (matches.Count < 8) return (null, new List<FeatureMatch>());

            var k1 = image1.IntrinsicMatrix;
            var k2 = image2.IntrinsicMatrix;

            var normPts1 = matches.Select(m => NormalizePoint(image1.SiftFeatures.KeyPoints[m.QueryIndex], k1)).ToList();
            var normPts2 = matches.Select(m => NormalizePoint(image2.SiftFeatures.KeyPoints[m.TrainIndex], k2)).ToList();

            double avgFocal = (k1.M11 + k1.M22 + k2.M11 + k2.M22) * 0.25;
            double reprojThreshNorm = NORMALIZED_REPROJECTION_THRESHOLD / avgFocal;
            double reprojThreshNormSq = reprojThreshNorm * reprojThreshNorm;

            var random = new Random();
            List<FeatureMatch> bestInliers = new List<FeatureMatch>();
            Matrix<double> bestE = null;

            for (int iter = 0; iter < RANSAC_ITERATIONS; iter++)
            {
                var sampleIndices = Enumerable.Range(0, matches.Count).OrderBy(x => random.Next()).Take(8).ToArray();
                var sampleMatches = sampleIndices.Select(i => matches[i]).ToList();

                var E = SolveEssentialMatrix(sampleMatches, image1, image2);
                if (E == null) continue;
                
                var currentInliers = new List<FeatureMatch>();
                for (int i = 0; i < matches.Count; i++)
                {
                    if (SampsonDistance(normPts1[i], normPts2[i], E) < reprojThreshNormSq)
                    {
                        currentInliers.Add(matches[i]);
                    }
                }
                
                if (currentInliers.Count > bestInliers.Count)
                {
                    bestInliers = currentInliers;
                    bestE = E;
                }
            }
            
            if (bestInliers.Count > MIN_INLIERS_FOR_ACCEPTANCE)
            {
                _service.Log($"    RANSAC found {bestInliers.Count} inliers, re-estimating essential matrix...");
                bestE = SolveEssentialMatrix(bestInliers, image1, image2);
            }
            else
            {
                _service.Log($"    RANSAC failed: only {bestInliers.Count} inliers (need > {MIN_INLIERS_FOR_ACCEPTANCE})");
            }

            if (bestE == null)
            {
                return (null, new List<FeatureMatch>());
            }

            var (finalR, finalT) = DecomposeAndCheckPoses(bestE, image1, image2, bestInliers);

            if (finalR != null && finalT != null)
            {
                var pose = CreatePoseMatrix(finalR, finalT);
                return (pose, bestInliers);
            }
            
            return (null, new List<FeatureMatch>());
        }

        private Vector2 NormalizePoint(KeyPoint kp, Matrix4x4 K)
        {
            return new Vector2((kp.X - K.M13) / K.M11, (kp.Y - K.M23) / K.M22);
        }

        private double SampsonDistance(Vector2 p1, Vector2 p2, Matrix<double> E)
        {
            var p1h = Vector.Build.Dense(new double[] { p1.X, p1.Y, 1 });
            var p2h = Vector.Build.Dense(new double[] { p2.X, p2.Y, 1 });
            double p2t_E_p1 = p2h.DotProduct(E * p1h);
            var E_p1 = E * p1h;
            var Et_p2 = E.Transpose() * p2h;
            double mag1 = E_p1[0] * E_p1[0] + E_p1[1] * E_p1[1];
            double mag2 = Et_p2[0] * Et_p2[0] + Et_p2[1] * Et_p2[1];
            return (p2t_E_p1 * p2t_E_p1) / (mag1 + mag2 + 1e-12);
        }
        
        private Matrix<double> SolveEssentialMatrix(List<FeatureMatch> matches, PhotogrammetryImage image1, PhotogrammetryImage image2)
        {
            if (matches.Count < 8) return null;
            
            var points1 = matches.Select(m => NormalizePoint(image1.SiftFeatures.KeyPoints[m.QueryIndex], image1.IntrinsicMatrix)).ToList();
            var points2 = matches.Select(m => NormalizePoint(image2.SiftFeatures.KeyPoints[m.TrainIndex], image2.IntrinsicMatrix)).ToList();

            return SolveEssentialMatrix8Point(points1, points2);
        }

        private Matrix<double> SolveEssentialMatrix8Point(List<Vector2> points1, List<Vector2> points2)
        {
            if (points1.Count < 8) return null;
            var A = Matrix.Build.Dense(points1.Count, 9);
            for (int i = 0; i < points1.Count; i++)
            {
                double u1 = points1[i].X, v1 = points1[i].Y;
                double u2 = points2[i].X, v2 = points2[i].Y;
                A.SetRow(i, new double[] { u2 * u1, u2 * v1, u2, v2 * u1, v2 * v1, v2, u1, v1, 1 });
            }
            var svd = A.Svd(true);
            var E_vec = svd.VT.Row(8);
            var E_unconstrained = Matrix.Build.DenseOfRowMajor(3, 3, E_vec.AsArray());
            var svd_E = E_unconstrained.Svd(true);
            
            // Enforce the Essential matrix constraint: two equal non-zero singular values and one zero
            // Take the average of the first two singular values for better numerical stability
            double s = (svd_E.S[0] + svd_E.S[1]) * 0.5;
            var S_clean = Vector.Build.Dense(new[] { s, s, 0 });
            return svd_E.U * Matrix.Build.DiagonalOfDiagonalVector(S_clean) * svd_E.VT;
        }

        private (Matrix<double> R, MathNet.Numerics.LinearAlgebra.Vector<double> t) DecomposeAndCheckPoses(
            Matrix<double> E, 
            PhotogrammetryImage image1, 
            PhotogrammetryImage image2,
            List<FeatureMatch> inlierMatches)
        {
            _service.Log($"    Decomposing essential matrix for {inlierMatches.Count} inliers...");
            
            var svd = E.Svd(true);
            var U = svd.U;
            var Vt = svd.VT;
            
            // Log singular values for debugging
            _service.Log($"      Essential matrix singular values: [{svd.S[0]:F4}, {svd.S[1]:F4}, {svd.S[2]:F4}]");
            
            var W = Matrix.Build.DenseOfArray(new double[,] { { 0, -1, 0 }, { 1, 0, 0 }, { 0, 0, 1 } });
            
            var R1 = U * W * Vt;
            if (R1.Determinant() < 0) R1 = -R1;
            
            var R2 = U * W.Transpose() * Vt;
            if (R2.Determinant() < 0) R2 = -R2;
            
            var t1 = U.Column(2);
            var t2 = -t1;
            
            // Don't normalize translation - keep the scale from Essential matrix

            var poses = new[] { (R1, t1), (R1, t2), (R2, t1), (R2, t2) };
            (Matrix<double> R, MathNet.Numerics.LinearAlgebra.Vector<double> t) bestPose = (null, null);
            int maxInFront = -1;

            foreach (var (R_candidate, t_candidate) in poses)
            {
                int inFrontCount = 0;
                int triangulationFailures = 0;
                int behindCamera1 = 0;
                int behindCamera2 = 0;
                
                // The decomposition [R|t] gives the transformation from camera 1 to camera 2.
                var pose_matrix_cam1_to_cam2 = CreatePoseMatrix(R_candidate, t_candidate);

                foreach (var match in inlierMatches)
                {
                    var kp1 = image1.SiftFeatures.KeyPoints[match.QueryIndex];
                    var kp2 = image2.SiftFeatures.KeyPoints[match.TrainIndex];
                    
                    var p3d_cam1 = Triangulation.TriangulatePoint(
                        new Vector2(kp1.X, kp1.Y), 
                        new Vector2(kp2.X, kp2.Y), 
                        image1.IntrinsicMatrix, 
                        image2.IntrinsicMatrix, 
                        pose_matrix_cam1_to_cam2);

                    // --- START OF FIX ---
                    // Cheirality Check: The triangulated point must be in front of both cameras.
                    if (!p3d_cam1.HasValue)
                    {
                        triangulationFailures++;
                        continue;
                    }
                    
                    // 1. Check if the point is in front of the first camera (Z > 0).
                    if (p3d_cam1.Value.Z <= 0)
                    {
                        behindCamera1++;
                        continue;
                    }
                    
                    // 2. Transform the point into the second camera's coordinate system.
                    var p3d_cam2 = Vector3.Transform(p3d_cam1.Value, pose_matrix_cam1_to_cam2);
                    
                    // 3. Check if the transformed point is also in front of the second camera.
                    if (p3d_cam2.Z <= 0)
                    {
                        behindCamera2++;
                        continue;
                    }
                    
                    inFrontCount++;
                    // --- END OF FIX ---
                }
                
                // Log details for first pose candidate for debugging
                if (poses[0].Equals((R_candidate, t_candidate)))
                {
                    _service.Log($"      Pose candidate 1: {inFrontCount} in front, {triangulationFailures} tri-failed, {behindCamera1} behind cam1, {behindCamera2} behind cam2");
                }
                
                if (inFrontCount > maxInFront)
                {
                    maxInFront = inFrontCount;
                    bestPose = (R_candidate, t_candidate);
                }
            }
            
            // A valid pose must have some inlier points in front of both cameras.
            // With real-world data, we can't expect all points to triangulate perfectly.
            // Accept if at least 10% of points pass AND at least 5 points total
            int minPercent = Math.Max(3, inlierMatches.Count / 10);  // At least 10% but minimum 3 points
            int minAbsolute = 5;  // Absolute minimum
            int minRequiredPoints = Math.Max(minPercent, minAbsolute);
            
            _service.Log($"    Cheirality check: {maxInFront}/{inlierMatches.Count} points in front of both cameras (need >= {minRequiredPoints})");
            
            if (maxInFront >= minRequiredPoints)
            {
                _service.Log($"    ✓ Pose decomposition successful with {maxInFront} valid points");
                return bestPose;
            }

            _service.Log($"    ✗ Pose decomposition failed: only {maxInFront} points passed cheirality check (need >= {minRequiredPoints})");
            return (null, null);
        }

        private Matrix4x4 CreatePoseMatrix(Matrix<double> R, MathNet.Numerics.LinearAlgebra.Vector<double> t)
        {
            return new Matrix4x4(
                (float)R[0, 0], (float)R[0, 1], (float)R[0, 2], 0,
                (float)R[1, 0], (float)R[1, 1], (float)R[1, 2], 0,
                (float)R[2, 0], (float)R[2, 1], (float)R[2, 2], 0,
                (float)t[0], (float)t[1], (float)t[2], 1
            );
        }
    }
}