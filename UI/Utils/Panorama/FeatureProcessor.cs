// GeoscientistToolkit/Business/Photogrammetry/FeatureProcessor.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Business.Photogrammetry;
using GeoscientistToolkit.Business.Photogrammetry.Math;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Handles feature detection and matching for photogrammetry
    /// </summary>
    internal class FeatureProcessor
    {
        private readonly PhotogrammetryProcessingService _service;
        private const int RANSAC_ITERATIONS = 2000;
        private const float REPROJECTION_THRESHOLD = 1.5f;

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
            var manualMatches = points.Select((p, i) => new FeatureMatch
            {
                QueryIndex = i,
                TrainIndex = i
            }).ToList();

            var dummyFeatures1 = new DetectedFeatures
            {
                KeyPoints = points.Select(p => new KeyPoint { X = p.P1.X, Y = p.P1.Y }).ToList()
            };

            var dummyFeatures2 = new DetectedFeatures
            {
                KeyPoints = points.Select(p => new KeyPoint { X = p.P2.X, Y = p.P2.Y }).ToList()
            };

            var tempImg1 = new PhotogrammetryImage(img1.Dataset)
            {
                Features = dummyFeatures1,
                IntrinsicMatrix = img1.IntrinsicMatrix
            };

            var tempImg2 = new PhotogrammetryImage(img2.Dataset)
            {
                Features = dummyFeatures2,
                IntrinsicMatrix = img2.IntrinsicMatrix
            };

            var (pose, _) = ComputeRelativePose(tempImg1, tempImg2, manualMatches, 1, 999f);
            return pose;
        }

        private List<(PhotogrammetryImage, PhotogrammetryImage)> GenerateImagePairs(
            List<PhotogrammetryImage> images)
        {
            var pairs = new List<(PhotogrammetryImage, PhotogrammetryImage)>();
            
            for (int i = 0; i < images.Count; i++)
            {
                for (int j = i + 1; j < images.Count; j++)
                {
                    pairs.Add((images[i], images[j]));
                }
            }
            
            return pairs;
        }

        private async Task ProcessImagePair(
            (PhotogrammetryImage img1, PhotogrammetryImage img2) pair,
            PhotogrammetryGraph graph,
            SIFTFeatureMatcherSIMD matcher,
            CancellationToken token)
        {
            var (image1, image2) = pair;
            
            if (image1.SiftFeatures == null || image2.SiftFeatures == null)
                return;
            
            if (image1.SiftFeatures.KeyPoints.Count <= 50 || image2.SiftFeatures.KeyPoints.Count <= 50)
                return;

            var matches = await matcher.MatchFeaturesAsync(
                image1.SiftFeatures, image2.SiftFeatures, token);

            if (matches.Count <= 50)
                return;

            var (pose, inliers) = ComputeRelativePoseSift(image1, image2, matches);

            if (inliers.Count > 20 && pose.HasValue)
            {
                graph.AddEdge(image1, image2, inliers, pose.Value);
                _service.Log($"Found {inliers.Count} inlier matches between {image1.Dataset.Name} and {image2.Dataset.Name}.");
            }
        }

        private (Matrix4x4? Pose, List<FeatureMatch> Inliers) ComputeRelativePose(
            PhotogrammetryImage image1,
            PhotogrammetryImage image2,
            List<FeatureMatch> matches,
            int ransacIterations = RANSAC_ITERATIONS,
            float reprojectionThreshold = REPROJECTION_THRESHOLD)
        {
            var bestInliers = new List<FeatureMatch>();
            Matrix4x4? bestPose = null;
            var random = new Random();

            var points1 = matches.Select(m => image1.Features.KeyPoints[m.QueryIndex]).Select(kp => new KeyPoint { X = kp.X, Y = kp.Y }).ToList();
            var points2 = matches.Select(m => image2.Features.KeyPoints[m.TrainIndex]).Select(kp => new KeyPoint { X = kp.X, Y = kp.Y }).ToList();

            var k1 = image1.IntrinsicMatrix;
            var k2 = image2.IntrinsicMatrix;

            for (int iter = 0; iter < ransacIterations; iter++)
            {
                var sampleResult = ProcessRANSACSample(
                    matches, points1, points2, k1, k2, 
                    random, reprojectionThreshold);

                if (sampleResult.Inliers.Count > bestInliers.Count)
                {
                    if (VerifyPoseGeometry(sampleResult.Pose, sampleResult.Inliers, 
                                          image1, image2, k1, k2))
                    {
                        bestInliers = sampleResult.Inliers;
                        bestPose = sampleResult.Pose;
                    }
                }
            }

            return (bestPose, bestInliers);
        }

        private (Matrix4x4? Pose, List<FeatureMatch> Inliers) ProcessRANSACSample(
            List<FeatureMatch> matches,
            List<KeyPoint> points1,
            List<KeyPoint> points2,
            Matrix4x4 k1,
            Matrix4x4 k2,
            Random random,
            float threshold)
        {
            // Sample 8 random points
            var randomIndices = Enumerable.Range(0, matches.Count)
                .OrderBy(x => random.Next())
                .Take(8)
                .ToArray();

            var sample1 = randomIndices.Select(idx => new Vector2(points1[idx].X, points1[idx].Y)).ToArray();
            var sample2 = randomIndices.Select(idx => new Vector2(points2[idx].X, points2[idx].Y)).ToArray();

            // Estimate fundamental matrix
            var F = EstimateFundamentalMatrix(sample1, sample2);
            if (!F.HasValue)
                return (null, new List<FeatureMatch>());

            // Compute essential matrix
            var E = (k2.As3x3Transposed() * F.Value) * k1.As3x3();

            // Decompose essential matrix
            var poses = DecomposeEssentialMatrix(E);
            if (poses == null || poses.Count == 0)
                return (null, new List<FeatureMatch>());

            // Find best pose
            Matrix4x4? bestPose = null;
            var bestInliers = new List<FeatureMatch>();

            foreach (var pose in poses)
            {
                var inliers = ComputeInliers(matches, points1, points2, F.Value, threshold);
                if (inliers.Count > bestInliers.Count)
                {
                    bestInliers = inliers;
                    bestPose = pose;
                }
            }

            return (bestPose, bestInliers);
        }

        private List<FeatureMatch> ComputeInliers(
            List<FeatureMatch> matches,
            List<KeyPoint> points1,
            List<KeyPoint> points2,
            Matrix3x3 F,
            float threshold)
        {
            var inliers = new List<FeatureMatch>();
            float thresholdSq = threshold * threshold;

            for (int i = 0; i < matches.Count; i++)
            {
                float err = SampsonDistance(F,
                    points1[i].X, points1[i].Y,
                    points2[i].X, points2[i].Y);

                if (err < thresholdSq)
                {
                    inliers.Add(matches[i]);
                }
            }

            return inliers;
        }

        private float SampsonDistance(Matrix3x3 F, float x1, float y1, float x2, float y2)
        {
            var p1 = new Vector3(x1, y1, 1);
            var p2 = new Vector3(x2, y2, 1);

            float p2tFp1 = Vector3.Dot(p2, F * p1);
            var Fp1 = F * p1;
            var Ftp2 = Matrix3x3.Transpose(F) * p2;

            float denominator = Fp1.X * Fp1.X + Fp1.Y * Fp1.Y + 
                               Ftp2.X * Ftp2.X + Ftp2.Y * Ftp2.Y;

            if (Math.Abs(denominator) < 1e-8)
                return float.MaxValue;

            return (p2tFp1 * p2tFp1) / denominator;
        }

        private Matrix3x3? EstimateFundamentalMatrix(Vector2[] points1, Vector2[] points2)
        {
            if (points1.Length < 8)
                return null;

            // Build matrix A for 8-point algorithm
            var A = new double[points1.Length, 9];
            for (int i = 0; i < points1.Length; i++)
            {
                double x1 = points1[i].X, y1 = points1[i].Y;
                double x2 = points2[i].X, y2 = points2[i].Y;

                A[i, 0] = x2 * x1; A[i, 1] = x2 * y1; A[i, 2] = x2;
                A[i, 3] = y2 * x1; A[i, 4] = y2 * y1; A[i, 5] = y2;
                A[i, 6] = x1; A[i, 7] = y1; A[i, 8] = 1;
            }

            // Solve using SVD
            var svd = new SvdDecomposition(A, false, true);
            var V = svd.V;

            // Extract F from last column of V
            var F_vec = new float[9];
            for (int i = 0; i < 9; i++)
                F_vec[i] = (float)V[i, 8];

            var F = new Matrix3x3(F_vec);

            // Enforce rank-2 constraint
            var svdF = new SvdDecomposition(F, true, true);
            var Uf = svdF.U.ToMatrix3x3();
            var Sf = svdF.SingularValues;
            var Vf = svdF.V.ToMatrix3x3();

            var S_diag = Matrix3x3.CreateDiagonal((float)Sf[0], (float)Sf[1], 0);
            var F_rank2 = Uf * S_diag * Matrix3x3.Transpose(Vf);

            return F_rank2;
        }

        private List<Matrix4x4> DecomposeEssentialMatrix(Matrix3x3 E)
        {
            var svd = new SvdDecomposition(E, true, true);
            var U = svd.U.ToMatrix3x3();
            var Vt = Matrix3x3.Transpose(svd.V.ToMatrix3x3());

            // Translation vector
            var t = new Vector3(U[0, 2], U[1, 2], U[2, 2]);

            // W matrix for rotation extraction
            var W = new Matrix3x3(0, -1, 0, 1, 0, 0, 0, 0, 1);

            // Two possible rotations
            var R1 = U * W * Vt;
            var R2 = U * Matrix3x3.Transpose(W) * Vt;

            // Ensure proper rotation (det = 1)
            if (Matrix3x3.Determinant(R1) < 0) R1 = -R1;
            if (Matrix3x3.Determinant(R2) < 0) R2 = -R2;

            // Return all 4 possible combinations
            return new List<Matrix4x4>
            {
                MatrixExtensions.CreateFrom(R1, t),
                MatrixExtensions.CreateFrom(R1, -t),
                MatrixExtensions.CreateFrom(R2, t),
                MatrixExtensions.CreateFrom(R2, -t)
            };
        }

        private bool VerifyPoseGeometry(
            Matrix4x4? pose,
            List<FeatureMatch> inliers,
            PhotogrammetryImage image1,
            PhotogrammetryImage image2,
            Matrix4x4 k1,
            Matrix4x4 k2)
        {
            if (!pose.HasValue || inliers.Count == 0)
                return false;

            int pointsInFront = 0;
            int sampleCount = Math.Min(10, inliers.Count);

            for (int i = 0; i < sampleCount; i++)
            {
                var match = inliers[i];
                var p1 = new Vector2(
                    image1.Features.KeyPoints[match.QueryIndex].X,
                    image1.Features.KeyPoints[match.QueryIndex].Y);
                var p2 = new Vector2(
                    image2.Features.KeyPoints[match.TrainIndex].X,
                    image2.Features.KeyPoints[match.TrainIndex].Y);

                var p3D = Triangulation.TriangulatePoint(p1, p2, k1, k2, pose.Value);

                if (p3D.HasValue && p3D.Value.Z > 0)
                {
                    var p3DTransformed = Vector3.Transform(p3D.Value, pose.Value);
                    if (p3DTransformed.Z > 0)
                    {
                        pointsInFront++;
                    }
                }
            }

            return pointsInFront > sampleCount / 2;
        }
        
        private (Matrix4x4? Pose, List<FeatureMatch> Inliers) ComputeRelativePoseSift(
            PhotogrammetryImage image1,
            PhotogrammetryImage image2,
            List<FeatureMatch> matches,
            int ransacIterations = RANSAC_ITERATIONS,
            float reprojectionThreshold = REPROJECTION_THRESHOLD)
        {
            var bestInliers = new List<FeatureMatch>();
            Matrix4x4? bestPose = null;
            var random = new Random();

            var points1 = matches.Select(m => image1.SiftFeatures.KeyPoints[m.QueryIndex]).ToList();
            var points2 = matches.Select(m => image2.SiftFeatures.KeyPoints[m.TrainIndex]).ToList();

            var k1 = image1.IntrinsicMatrix;
            var k2 = image2.IntrinsicMatrix;

            for (int iter = 0; iter < ransacIterations; iter++)
            {
                var sampleResult = ProcessRANSACSample(
                    matches, points1, points2, k1, k2, 
                    random, reprojectionThreshold);

                if (sampleResult.Inliers.Count > bestInliers.Count)
                {
                    if (VerifyPoseGeometrySift(sampleResult.Pose, sampleResult.Inliers, 
                                          image1, image2, k1, k2))
                    {
                        bestInliers = sampleResult.Inliers;
                        bestPose = sampleResult.Pose;
                    }
                }
            }

            return (bestPose, bestInliers);
        }
        
        private bool VerifyPoseGeometrySift(
            Matrix4x4? pose,
            List<FeatureMatch> inliers,
            PhotogrammetryImage image1,
            PhotogrammetryImage image2,
            Matrix4x4 k1,
            Matrix4x4 k2)
        {
            if (!pose.HasValue || inliers.Count == 0)
                return false;

            int pointsInFront = 0;
            int sampleCount = Math.Min(10, inliers.Count);

            for (int i = 0; i < sampleCount; i++)
            {
                var match = inliers[i];
                var p1 = new Vector2(
                    image1.SiftFeatures.KeyPoints[match.QueryIndex].X,
                    image1.SiftFeatures.KeyPoints[match.QueryIndex].Y);
                var p2 = new Vector2(
                    image2.SiftFeatures.KeyPoints[match.TrainIndex].X,
                    image2.SiftFeatures.KeyPoints[match.TrainIndex].Y);

                var p3D = Triangulation.TriangulatePoint(p1, p2, k1, k2, pose.Value);

                if (p3D.HasValue && p3D.Value.Z > 0)
                {
                    var p3DTransformed = Vector3.Transform(p3D.Value, pose.Value);
                    if (p3DTransformed.Z > 0)
                    {
                        pointsInFront++;
                    }
                }
            }

            return pointsInFront > sampleCount / 2;
        }
    }
}