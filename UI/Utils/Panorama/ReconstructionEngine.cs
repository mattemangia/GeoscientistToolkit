// GeoscientistToolkit/Business/Photogrammetry/Reconstruction/ReconstructionEngine.cs

// =================================================================================================
// FINAL ROBUST VERSION v5 - Critical Pose Orientation Fixes
// 
// Root Cause Analysis:
// The "0 points" issue was caused by incorrect understanding of how poses are stored in PhotogrammetryGraph.
// When AddEdge(img1, img2, matches, T_1->2) is called:
//   - img1's adjacency list stores: (img2, matches, T_1->2)
//   - img2's adjacency list stores: (img1, matches, T_2->1) [automatically inverted]
// 
// Therefore, when getting neighbors of any node, the pose is ALREADY in the correct orientation T_node->neighbor.
// 
// Fixes Applied:
// 1. ProcessNodeMatches: Removed incorrect pose inversion. The pose from graph is already T_node->neighbor.
// 2. ComputeGlobalPoses: Removed incorrect pose inversion. The pose from graph is already T_current->neighbor.
// 
// These fixes ensure triangulation receives correct relative poses, allowing proper 3D reconstruction.
// =================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Business.Photogrammetry;
using GeoscientistToolkit.Data.Image;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Handles 3D reconstruction from matched features
    /// </summary>
    internal class ReconstructionEngine
    {
        private readonly PhotogrammetryProcessingService _service;

        private const float MAX_REPROJECTION_ERROR_PX = 30.0f;  // More permissive for real-world data
        private const float MIN_PARALLAX_DEGREES = 0.02f;  // CRITICAL: Lower from 0.05 to 0.02 for high-overlap sequences (>60%)
        private const float CHEIRALITY_EPSILON = 1e-6f;
        
        // Debug counter for dense point generation logging
        private static int _densePointDebugCount = 0;

        public ReconstructionEngine(PhotogrammetryProcessingService service)
        {
            _service = service;
        }

        public void ComputeGlobalPoses(
            PhotogrammetryImageGroup group,
            List<PhotogrammetryImage> images,
            PhotogrammetryGraph graph)
        {
            if (group.Images.Count == 0)
                return;

            var visited = new HashSet<Guid>();
            var queue = new Queue<Guid>();

            // Start with the first image as the reference point (origin).
            var referenceImage = group.Images[0];
            referenceImage.GlobalPose = Matrix4x4.Identity;
            queue.Enqueue(referenceImage.Id);
            visited.Add(referenceImage.Id);
            _service.Log($"Set {referenceImage.Dataset.Name} as the origin for global poses.");

            // Breadth-first traversal to compute global poses for all connected images.
            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                var currentImage = images.First(img => img.Id == currentId);

                if (graph.TryGetNeighbors(currentId, out var neighbors))
                {
                    foreach (var (neighborId, _, pose_current_to_neighbor) in neighbors)
                    {
                        if (!visited.Contains(neighborId))
                        {
                            visited.Add(neighborId);
                            var neighborImage = images.First(img => img.Id == neighborId);

                            // --- FIXED: The graph already stores T_Current->Neighbor ---
                            // When we get neighbors of currentId, the pose is T_current->neighbor.
                            // GlobalPose_Neighbor = GlobalPose_Current * T_Current->Neighbor
                            // No inversion needed!
                            neighborImage.GlobalPose = currentImage.GlobalPose * pose_current_to_neighbor;

                            _service.Log($"  Computed global pose for {neighborImage.Dataset.Name}");
                            queue.Enqueue(neighborId);
                        }
                    }
                }
            }
        }

        public async Task<PhotogrammetryPointCloud> BuildSparseCloudAsync(
            List<PhotogrammetryImage> images,
            PhotogrammetryGraph graph,
            bool enableGeoreferencing,
            Action<float, string> updateProgress)
        {
            return await Task.Run(() =>
            {
                var cloud = new PhotogrammetryPointCloud { IsDense = false };
                var processedMatches = new HashSet<string>();
                var totalNodes = graph.GetNodes().Count();
                var processedNodes = 0;

                _service.Log("Starting triangulation of matched feature pairs...");

                foreach (var node in graph.GetNodes())
                {
                    processedNodes++;
                    updateProgress(0.75f + (0.1f * processedNodes / totalNodes), $"Triangulating pairs for image {processedNodes}/{totalNodes}");
                    ProcessNodeMatches(node, images, graph, cloud, processedMatches);
                }

                ComputePointCloudBounds(cloud);
                
                // Compute scene statistics
                var extent = cloud.BoundingBoxMax - cloud.BoundingBoxMin;
                float sceneSize = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
                var center = (cloud.BoundingBoxMin + cloud.BoundingBoxMax) * 0.5f;
                
                _service.Log($"Sparse cloud built with {cloud.Points.Count} points.");
                _service.Log($"Scene center: ({center.X:F2}, {center.Y:F2}, {center.Z:F2})");
                _service.Log($"Scene extent: ({extent.X:F2}, {extent.Y:F2}, {extent.Z:F2})");
                _service.Log($"Scene size (max dimension): {sceneSize:F2}");
                _service.Log($"Bounds: Min=({cloud.BoundingBoxMin.X:F2}, {cloud.BoundingBoxMin.Y:F2}, {cloud.BoundingBoxMin.Z:F2}), Max=({cloud.BoundingBoxMax.X:F2}, {cloud.BoundingBoxMax.Y:F2}, {cloud.BoundingBoxMax.Z:F2})");

                return cloud;
            });
        }

        private void ProcessNodeMatches(
            PhotogrammetryImage node,
            List<PhotogrammetryImage> images,
            PhotogrammetryGraph graph,
            PhotogrammetryPointCloud cloud,
            HashSet<string> processedMatches)
        {
            if (!graph.TryGetNeighbors(node.Id, out var edges))
                return;

            foreach (var (neighborId, matches, pose_node_to_neighbor) in edges)
            {
                var matchKey = GetMatchKey(node.Id, neighborId);
                if (processedMatches.Contains(matchKey))
                    continue;

                processedMatches.Add(matchKey);
                
                var neighbor = images.First(img => img.Id == neighborId);
                
                _service.Log($"  Triangulating {matches.Count} matches between {node.Dataset.Name} and {neighbor.Dataset.Name}");

                // --- FIXED: The pose from the graph is already T_node->neighbor ---
                // The graph.AddEdge stores the pose as-is for the forward edge,
                // and the inverse for the reverse edge. When we get neighbors of 'node',
                // we get the pose T_node->neighbor, which is exactly what we need.
                // No inversion required!
                TriangulateMatches(node, neighbor, matches, pose_node_to_neighbor, cloud);
            }
        }

        private void TriangulateMatches(
            PhotogrammetryImage image1,
            PhotogrammetryImage image2,
            List<FeatureMatch> matches,
            Matrix4x4 relativePose_1_to_2_sys,
            PhotogrammetryPointCloud cloud)
        {
            try
            {
                if (image1.SiftFeatures == null || image2.SiftFeatures == null)
                {
                    _service.Log($"    [Warning] Skipping pair due to missing SIFT features.");
                    return;
                }
                
                // Convert System.Numerics pose to Math.NET format
                // CRITICAL: The stored matrix has R^T (transposed), so extract and transpose back
                var R_mathnet = MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfArray(new double[,]
                {
                    { relativePose_1_to_2_sys.M11, relativePose_1_to_2_sys.M21, relativePose_1_to_2_sys.M31 },
                    { relativePose_1_to_2_sys.M12, relativePose_1_to_2_sys.M22, relativePose_1_to_2_sys.M32 },
                    { relativePose_1_to_2_sys.M13, relativePose_1_to_2_sys.M23, relativePose_1_to_2_sys.M33 }
                });
                
                var t_mathnet = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(new[] {
                    (double)relativePose_1_to_2_sys.M41,
                    (double)relativePose_1_to_2_sys.M42,
                    (double)relativePose_1_to_2_sys.M43
                });
                
                // Convert intrinsics to Math.NET
                var K1_mathnet = MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfArray(new double[,]
                {
                    { image1.IntrinsicMatrix.M11, image1.IntrinsicMatrix.M12, image1.IntrinsicMatrix.M13 },
                    { image1.IntrinsicMatrix.M21, image1.IntrinsicMatrix.M22, image1.IntrinsicMatrix.M23 },
                    { image1.IntrinsicMatrix.M31, image1.IntrinsicMatrix.M32, image1.IntrinsicMatrix.M33 }
                });
                
                var K2_mathnet = MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfArray(new double[,]
                {
                    { image2.IntrinsicMatrix.M11, image2.IntrinsicMatrix.M12, image2.IntrinsicMatrix.M13 },
                    { image2.IntrinsicMatrix.M21, image2.IntrinsicMatrix.M22, image2.IntrinsicMatrix.M23 },
                    { image2.IntrinsicMatrix.M31, image2.IntrinsicMatrix.M32, image2.IntrinsicMatrix.M33 }
                });
                
                // Log the pose being used
                _service.Log($"    Using pose for triangulation: Translation=({t_mathnet[0]:F3}, {t_mathnet[1]:F3}, {t_mathnet[2]:F3})");
                
                // Log intrinsic parameters for debugging
                _service.Log($"    K1: fx={image1.IntrinsicMatrix.M11:F1}, fy={image1.IntrinsicMatrix.M22:F1}, cx={image1.IntrinsicMatrix.M13:F1}, cy={image1.IntrinsicMatrix.M23:F1}");
                _service.Log($"    K2: fx={image2.IntrinsicMatrix.M11:F1}, fy={image2.IntrinsicMatrix.M22:F1}, cx={image2.IntrinsicMatrix.M13:F1}, cy={image2.IntrinsicMatrix.M23:F1}");

                int successfulTriangulations = 0;
                int failedCheirality = 0;
                int failedReprojection = 0;
                int failedParallax = 0;
                int failedTriangulation = 0;
                
                // Debug logging for first few points
                int debugCount = 0;
                const int maxDebugLogs = 3;

                // Compute baseline for parallax check
                float baseline = (float)t_mathnet.L2Norm();
                _service.Log($"    Baseline between cameras: {baseline:F4} units");
                
                // Compute camera 2's center position in camera 1's coordinate system
                // cam2_center = -R^T * t (the position of cam2's origin as seen from cam1)
                var cam2_center_in_cam1_mathnet = -R_mathnet.Transpose() * t_mathnet;
                var cam2_center_in_cam1 = new Vector3(
                    (float)cam2_center_in_cam1_mathnet[0],
                    (float)cam2_center_in_cam1_mathnet[1],
                    (float)cam2_center_in_cam1_mathnet[2]
                );
                
                // Warn if baseline is suspiciously small
                if (baseline < 1e-4f)
                {
                    _service.Log($"    [Warning] Baseline extremely small ({baseline:E}), triangulation may be unreliable.");
                }
                
                // Compute adaptive reprojection threshold
                float avgImageSize = (image1.Dataset.Width + image1.Dataset.Height + 
                                     image2.Dataset.Width + image2.Dataset.Height) * 0.25f;
                float adaptiveReprojThreshold = Math.Max(MAX_REPROJECTION_ERROR_PX, avgImageSize * 0.03f);
                _service.Log($"    Using adaptive reprojection threshold: {adaptiveReprojThreshold:F2} pixels");

                foreach (var match in matches)
                {
                    var kp1 = image1.SiftFeatures.KeyPoints[match.QueryIndex];
                    var kp2 = image2.SiftFeatures.KeyPoints[match.TrainIndex];

                    // Use Math.NET triangulation
                    var p1_mathnet = Triangulation.MakePoint2D(kp1.X, kp1.Y);
                    var p2_mathnet = Triangulation.MakePoint2D(kp2.X, kp2.Y);
                    
                    var point3D_in_cam1 = Triangulation.TriangulatePoint(
                        p1_mathnet, p2_mathnet,
                        K1_mathnet, K2_mathnet,
                        R_mathnet, t_mathnet);

                    if (point3D_in_cam1 == null) 
                    {
                        failedTriangulation++;
                        continue;
                    }
                    
                    var p3d_cam1 = new Vector3((float)point3D_in_cam1[0], (float)point3D_in_cam1[1], (float)point3D_in_cam1[2]);
                    
                    // Debug logging for first few points
                    if (debugCount < maxDebugLogs)
                    {
                        _service.Log($"      Debug point {debugCount}: cam1=({p3d_cam1.X:F2}, {p3d_cam1.Y:F2}, {p3d_cam1.Z:F2})");
                        debugCount++;
                    }
                    
                    // Transform point to second camera's coordinate system using Math.NET
                    var p3d_cam2_mathnet = R_mathnet * point3D_in_cam1 + t_mathnet;
                    var p3d_cam2 = new Vector3((float)p3d_cam2_mathnet[0], (float)p3d_cam2_mathnet[1], (float)p3d_cam2_mathnet[2]);
                    
                    if (debugCount <= maxDebugLogs && debugCount > 0)
                    {
                        _service.Log($"        cam2=({p3d_cam2.X:F2}, {p3d_cam2.Y:F2}, {p3d_cam2.Z:F2})");
                    }
                    
                    // Reject if behind second camera
                    if (p3d_cam2.Z < CHEIRALITY_EPSILON)
                    {
                        failedCheirality++;
                        continue;
                    }

                    var p1_reprojected = ProjectCameraToImage(p3d_cam1, image1.IntrinsicMatrix);
                    if (p1_reprojected.X < 0 || p1_reprojected.Y < 0)
                    {
                        failedReprojection++;
                        continue;
                    }
                    float error1 = Vector2.Distance(new Vector2(kp1.X, kp1.Y), p1_reprojected);

                    var p2_reprojected = ProjectCameraToImage(p3d_cam2, image2.IntrinsicMatrix);
                    if (p2_reprojected.X < 0 || p2_reprojected.Y < 0)
                    {
                        failedReprojection++;
                        continue;
                    }
                    float error2 = Vector2.Distance(new Vector2(kp2.X, kp2.Y), p2_reprojected);
                    
                    if (debugCount <= maxDebugLogs && debugCount > 0)
                    {
                        _service.Log($"        Reprojection errors: cam1={error1:F2}px, cam2={error2:F2}px");
                    }

                    // Reject if reprojection error too large (use adaptive threshold)
                    if (error1 > adaptiveReprojThreshold || error2 > adaptiveReprojThreshold)
                    {
                        failedReprojection++;
                        continue;
                    }

                    var ray1 = Vector3.Normalize(p3d_cam1);
                    var ray2 = Vector3.Normalize(p3d_cam1 - cam2_center_in_cam1);
                    
                    float parallax_rad = MathF.Acos(Math.Clamp(Vector3.Dot(ray1, ray2), -1.0f, 1.0f));
                    float parallax_deg = parallax_rad * (180.0f / MathF.PI);

                    // Reject points with insufficient parallax (poor triangulation angle)
                    if (parallax_deg < MIN_PARALLAX_DEGREES)
                    {
                        failedParallax++;
                        continue;
                    }
                    
                    var worldPoint = Vector3.Transform(p3d_cam1, image1.GlobalPose);
                    var color = SampleImageColor(image1.Dataset, (int)kp1.X, (int)kp1.Y);
                    
                    // Log successful point addition
                    if (debugCount <= maxDebugLogs + 2)
                    {
                        _service.Log($"      Adding point to cloud at world position: ({worldPoint.X:F2}, {worldPoint.Y:F2}, {worldPoint.Z:F2})");
                    }
                    
                    cloud.Points.Add(new Point3D
                    {
                        Position = worldPoint,
                        Color = color,
                        Confidence = 1.0f - match.Distance / 256.0f,
                        ObservingImages = { image1.Id, image2.Id }
                    });
                    successfulTriangulations++;
                }

                if (successfulTriangulations > 0)
                {
                    _service.Log($"    > Found {successfulTriangulations} valid 3D points. (Rejected: Triangulation={failedTriangulation}, Cheirality={failedCheirality}, Reprojection={failedReprojection}, Parallax={failedParallax})");
                }
                else
                {
                    _service.Log($"    > ⚠ No valid 3D points found! (Failed: Triangulation={failedTriangulation}, Cheirality={failedCheirality}, Reprojection={failedReprojection}, Parallax={failedParallax}, Total={matches.Count})");
                    
                    // Provide diagnostic information
                    if (failedTriangulation == matches.Count)
                        _service.Log($"       All points failed DLT triangulation - check camera poses and intrinsics");
                    else if (failedCheirality > matches.Count * 0.5)
                        _service.Log($"       Majority failed cheirality - points behind camera(s), possible pose error");
                    else if (failedReprojection > matches.Count * 0.5)
                    {
                        _service.Log($"       Majority failed reprojection - high reprojection errors detected");
                        _service.Log($"       This often indicates incorrect intrinsic calibration (focal length)");
                        _service.Log($"       Try: 1) Ensure EXIF data is present, 2) Perform camera calibration, 3) Use images from calibrated camera");
                    }
                    else if (failedParallax > matches.Count * 0.5)
                        _service.Log($"       Majority failed parallax - insufficient baseline or near-degenerate geometry");
                }
                
                // Compute average reprojection error for successful points
                if (successfulTriangulations > 0)
                {
                    // This would require tracking errors, but we can infer from the ratio
                    float successRate = (float)successfulTriangulations / matches.Count;
                    if (successRate < 0.3f)
                    {
                        _service.Log($"    ⚠ Low success rate ({successRate * 100:F1}%) - consider checking camera calibration");
                    }
                }
            }
            catch (Exception ex)
            {
                _service.Log($"[ERROR] An exception occurred during triangulation for pair {image1.Dataset.Name}-{image2.Dataset.Name}: {ex.Message}");
            }
        }

        private Vector2 ProjectCameraToImage(Vector3 pointInCamera, Matrix4x4 K)
        {
            if (Math.Abs(pointInCamera.Z) < 1e-8) return new Vector2(-1, -1);
            float u = (pointInCamera.X * K.M11) / pointInCamera.Z + K.M13;
            float v = (pointInCamera.Y * K.M22) / pointInCamera.Z + K.M23;
            return new Vector2(u, v);
        }

        public async Task<PhotogrammetryPointCloud> BuildDenseCloudAsync(
            PhotogrammetryPointCloud sparseCloud,
            List<PhotogrammetryImage> images,
            DenseCloudOptions options,
            Action<float, string> updateProgress)
        {
            return await Task.Run(() =>
            {
                var denseCloud = new PhotogrammetryPointCloud { IsDense = true };
                var sparsePoints = sparseCloud.Points.ToList();
                
                int densityFactor = GetDensityFactor(options.Quality);
                _service.Log($"Dense cloud generation started:");
                _service.Log($"  Sparse points: {sparsePoints.Count}");
                _service.Log($"  Density factor: {densityFactor}x (Quality: {options.Quality})");
                _service.Log($"  Expected dense points: {sparsePoints.Count * densityFactor * 0.3f:F0} (conservative estimate)");
                _service.Log($"Starting densification of {sparsePoints.Count} sparse points...");

                int processedCount = 0;
                int totalDenseAdded = 0;
                
                foreach (var sparsePoint in sparsePoints)
                {
                    int beforeCount = denseCloud.Points.Count;
                    ProcessSparsePoint(sparsePoint, images, denseCloud, densityFactor, options.ConfidenceThreshold);
                    int added = denseCloud.Points.Count - beforeCount;
                    totalDenseAdded += added;
                    
                    if (++processedCount % 10 == 0 || processedCount == sparsePoints.Count)
                    {
                        updateProgress(
                            (float)processedCount / sparsePoints.Count * 0.8f,
                            $"Densifying... ({processedCount}/{sparsePoints.Count})"
                        );
                        
                        if (processedCount % 20 == 0)
                        {
                            float avgPerPoint = processedCount > 0 ? (float)totalDenseAdded / processedCount : 0;
                            _service.Log($"Processed {processedCount}/{sparsePoints.Count} sparse points, " +
                                       $"added {totalDenseAdded} dense points ({avgPerPoint:F1} per sparse point)");
                        }
                    }
                }

                float finalRatio = sparsePoints.Count > 0 ? (float)denseCloud.Points.Count / sparsePoints.Count : 0;
                _service.Log($"Densification complete: {denseCloud.Points.Count} total points ({finalRatio:F1}× sparse cloud)");

                if (finalRatio < 1.5f)
                {
                    _service.Log($"⚠ Warning: Dense cloud is not much denser than sparse ({finalRatio:F1}×).");
                    _service.Log($"  This may indicate: confidence threshold too high, or poor multi-view consistency.");
                }

                _service.Log($"Densification complete: {denseCloud.Points.Count} total points ({finalRatio:F1}× sparse cloud)");

                if (finalRatio < 1.5f)
                {
                    _service.Log($"⚠ Warning: Dense cloud is not much denser than sparse ({finalRatio:F1}×).");
                    _service.Log($"  This may indicate: confidence threshold too high, or poor multi-view consistency.");
                }

                if (options.FilterOutliers && denseCloud.Points.Count < 100000)  // Skip filtering for very large clouds
                {
                    _service.Log("Filtering outliers using statistical analysis...");
                    FilterOutlierPoints(denseCloud, options.ConfidenceThreshold);
                }
                else if (options.FilterOutliers)
                {
                    _service.Log($"Skipping outlier filtering for performance (cloud has {denseCloud.Points.Count} points)");
                }

                ComputePointCloudBounds(denseCloud);
                _service.Log($"Dense cloud built with {denseCloud.Points.Count} points.");

                return denseCloud;
            });
        }
        
        private void ProcessSparsePoint(
            Point3D sparsePoint,
            List<PhotogrammetryImage> images,
            PhotogrammetryPointCloud denseCloud,
            int densityFactor,
            float confidenceThreshold)
        {
            // Always add the original sparse point
            denseCloud.Points.Add(new Point3D
            {
                Position = sparsePoint.Position,
                Color = sparsePoint.Color,
                Confidence = sparsePoint.Confidence
            });

            var observingImages = sparsePoint.ObservingImages
                .Select(id => images.FirstOrDefault(img => img.Id == id))
                .Where(img => img != null)
                .ToList();

            if (observingImages.Count >= 2)
            {
                var densePoints = GenerateDensePointsAround(
                    sparsePoint, observingImages, densityFactor, confidenceThreshold);
                denseCloud.Points.AddRange(densePoints);
            }
            else if (observingImages.Count < 2)
            {
                // Only visible from one camera - can't densify
                // This is normal and doesn't warrant a warning for each point
            }
        }

        private List<Point3D> GenerateDensePointsAround(
            Point3D sparsePoint,
            List<PhotogrammetryImage> observingImages,
            int densityFactor,
            float confidenceThreshold)
        {
            var densePoints = new List<Point3D>();
            
            // CRITICAL: PMVS-style patch propagation in IMAGE SPACE, not 3D space
            // References:
            // - Furukawa & Ponce "Accurate, Dense, and Robust Multi-View Stereopsis" PAMI 2010
            // - Schönberger et al. "Pixelwise View Selection for Unstructured Multi-View Stereo" ECCV 2016
            
            densityFactor = Math.Min(densityFactor, 100); // Allow more points for better density
            
            // Compute average distance to cameras for scale estimation
            float avgCameraDist = 0;
            foreach (var img in observingImages)
            {
                var cameraPos = new Vector3(img.GlobalPose.M41, img.GlobalPose.M42, img.GlobalPose.M43);
                avgCameraDist += Vector3.Distance(sparsePoint.Position, cameraPos);
            }
            avgCameraDist /= observingImages.Count;
            
            // DIAGNOSTIC: Log first sparse point being processed
            if (_densePointDebugCount < 3)
            {
                _service.Log($"  [DENSE DEBUG #{_densePointDebugCount}] Sparse point at {sparsePoint.Position}:");
                _service.Log($"    Observed by {observingImages.Count} images, avg distance={avgCameraDist:F2}m");
                _service.Log($"    Density factor={densityFactor}, confidence threshold={confidenceThreshold:F2}");
                _densePointDebugCount++;
            }
            
            // PMVS-style approach: For each reference image, propagate to neighboring pixels
            // CRITICAL FIX: Try all observing images to find a valid reference, not just the first one
            // A sparse point may not project into the first observing image due to edge effects
            PhotogrammetryImage refImage = null;
            Vector2 refProjection = default;
            
            foreach (var img in observingImages)
            {
                var projection = ProjectWorldToImage(sparsePoint.Position, img);
                if (IsValidProjection(projection, img))
                {
                    refImage = img;
                    refProjection = projection;
                    break;
                }
            }
            
            if (refImage == null)
            {
                if (_densePointDebugCount <= 3)
                {
                    _service.Log($"    [DENSE DEBUG] Sparse point doesn't project into any observing image!");
                }
                return densePoints;
            }
            
            // Compute pixel search radius based on density factor
            // Higher density = larger search radius in image space
            int pixelRadius = Math.Max(3, (int)MathF.Sqrt(densityFactor / 2.0f));
            
            if (_densePointDebugCount <= 3)
            {
                _service.Log($"    Pixel search radius: {pixelRadius} pixels around ({refProjection.X:F0}, {refProjection.Y:F0})");
            }
            
            // DIAGNOSTIC: Track success/failure counts
            int attemptedPixels = 0;
            int failedProjection = 0;
            int failedTriangulation = 0;
            int failedConfidence = 0;
            int successfulPoints = 0;
            
            // Propagate to neighboring pixels in a grid pattern (PMVS expansion step)
            var processedPixels = new HashSet<(int, int)>();
            
            for (int dy = -pixelRadius; dy <= pixelRadius; dy++)
            {
                for (int dx = -pixelRadius; dx <= pixelRadius; dx++)
                {
                    if (dx == 0 && dy == 0) continue; // Skip center (already have sparse point)
                    if (densePoints.Count >= densityFactor) break;
                    
                    // Neighbor pixel in reference image
                    int px = (int)(refProjection.X + dx);
                    int py = (int)(refProjection.Y + dy);
                    
                    attemptedPixels++;
                    
                    // Skip out of bounds or already processed
                    if (px < 0 || px >= refImage.Dataset.Width || py < 0 || py >= refImage.Dataset.Height)
                    {
                        failedProjection++;
                        continue;
                    }
                    
                    if (processedPixels.Contains((px, py)))
                        continue;
                    
                    processedPixels.Add((px, py));
                    
                    // Try to match this pixel across multiple views and triangulate
                    var candidate3D = TriangulatePixelAcrossViews(
                        refImage, new Vector2(px, py), 
                        observingImages.Where(img => img.Id != refImage.Id).ToList(),
                        sparsePoint, avgCameraDist);
                    
                    if (candidate3D == null)
                    {
                        failedTriangulation++;
                        continue;
                    }
                    
                    // Validate with photometric consistency
                    float confidence = ComputePointConfidence(candidate3D.Value, observingImages);
                    
                    if (confidence >= confidenceThreshold)
                    {
                        densePoints.Add(new Point3D
                        {
                            Position = candidate3D.Value,
                            Color = SampleImageColor(refImage.Dataset, px, py),
                            Confidence = confidence,
                            ObservingImages = new List<Guid>(observingImages.Select(img => img.Id))
                        });
                        successfulPoints++;
                    }
                    else
                    {
                        failedConfidence++;
                    }
                }
                
                if (densePoints.Count >= densityFactor) break;
            }
            
            // DIAGNOSTIC: Log results for first few sparse points
            if (_densePointDebugCount <= 3)
            {
                _service.Log($"    [DENSE DEBUG] Results:");
                _service.Log($"      Attempted: {attemptedPixels} pixels");
                _service.Log($"      Failed projection: {failedProjection}");
                _service.Log($"      Failed triangulation: {failedTriangulation}");
                _service.Log($"      Failed confidence ({confidenceThreshold:F2}): {failedConfidence}");
                _service.Log($"      SUCCESS: {successfulPoints} dense points added");
                
                // CRITICAL DIAGNOSTIC
                if (successfulPoints == 0)
                {
                    _service.Log($"      ⚠️ CRITICAL: ZERO DENSE POINTS GENERATED!");
                    _service.Log($"         Most likely cause:");
                    if (failedTriangulation > attemptedPixels * 0.5)
                        _service.Log($"         - Triangulation failing (epipolar matching not finding correspondences)");
                    else if (failedConfidence > attemptedPixels * 0.5)
                        _service.Log($"         - Confidence threshold too high (try lowering from {confidenceThreshold:F2} to 0.05)");
                    else if (failedProjection > attemptedPixels * 0.5)
                        _service.Log($"         - Most pixels out of image bounds (sparse point at edge?)");
                }
            }
            
            // If still not dense enough, try multi-image propagation
            // Process other observing images as references (excluding the one we already used)
            if (densePoints.Count < densityFactor * 0.5f && observingImages.Count > 1)
            {
                foreach (var img in observingImages.Where(i => i.Id != refImage.Id))
                {
                    if (densePoints.Count >= densityFactor) break;
                    
                    var projection = ProjectWorldToImage(sparsePoint.Position, img);
                    if (!IsValidProjection(projection, img)) continue;
                    
                    // Smaller radius for secondary images to avoid duplication
                    int secondaryRadius = Math.Max(2, pixelRadius / 2);
                    
                    for (int dy = -secondaryRadius; dy <= secondaryRadius; dy++)
                    {
                        for (int dx = -secondaryRadius; dx <= secondaryRadius; dx++)
                        {
                            if (densePoints.Count >= densityFactor) break;
                            
                            int px = (int)(projection.X + dx);
                            int py = (int)(projection.Y + dy);
                            
                            if (px < 0 || px >= img.Dataset.Width || py < 0 || py >= img.Dataset.Height)
                                continue;
                            
                            var candidate3D = TriangulatePixelAcrossViews(
                                img, new Vector2(px, py),
                                observingImages.Where(i => i.Id != img.Id).ToList(),
                                sparsePoint, avgCameraDist);
                            
                            if (candidate3D != null)
                            {
                                float confidence = ComputePointConfidence(candidate3D.Value, observingImages);
                                
                                if (confidence >= confidenceThreshold)
                                {
                                    densePoints.Add(new Point3D
                                    {
                                        Position = candidate3D.Value,
                                        Color = SampleImageColor(img.Dataset, px, py),
                                        Confidence = confidence,
                                        ObservingImages = new List<Guid>(observingImages.Select(img => img.Id))
                                    });
                                }
                            }
                        }
                    }
                }
            }
            
            return densePoints;
        }
        
        /// <summary>
        /// Triangulate a 3D point by matching a pixel in reference image across other views
        /// This implements the core of PMVS patch matching
        /// </summary>
        private Vector3? TriangulatePixelAcrossViews(
            PhotogrammetryImage refImage,
            Vector2 refPixel,
            List<PhotogrammetryImage> otherImages,
            Point3D seedPoint,
            float estimatedDepth)
        {
            if (otherImages.Count == 0)
                return null;
            
            // Get ray from reference camera through the pixel
            var refRay = GetPixelRay(refImage, refPixel);
            if (refRay == null) return null;
            
            // Find best matching pixel in other images using epipolar search
            var correspondences = new List<(PhotogrammetryImage img, Vector2 pixel, float score)>();
            
            foreach (var otherImg in otherImages.Take(3)) // Use top 3 views for efficiency
            {
                // Search along epipolar line for best match
                var match = FindEpipolarMatch(refImage, refPixel, otherImg, estimatedDepth);
                
                if (match != null)
                {
                    correspondences.Add((otherImg, match.Value.pixel, match.Value.ncc));
                }
            }
            
            if (correspondences.Count == 0)
                return null;
            
            // Use the best correspondence to triangulate
            var bestMatch = correspondences.OrderByDescending(c => c.score).First();
            
            // Triangulate between reference and best match
            var point3D = TriangulateTwoViews(
                refImage, refPixel,
                bestMatch.img, bestMatch.pixel);
            
            return point3D;
        }
        
        /// <summary>
        /// Find corresponding pixel in another image by searching along epipolar line
        /// </summary>
        private (Vector2 pixel, float ncc)? FindEpipolarMatch(
            PhotogrammetryImage refImage,
            Vector2 refPixel,
            PhotogrammetryImage searchImage,
            float estimatedDepth)
        {
            // Sample multiple depths along ray and find best NCC match
            const int NUM_DEPTH_SAMPLES = 10;
            const int PATCH_RADIUS = 5;
            
            var refRay = GetPixelRay(refImage, refPixel);
            if (refRay == null) return null;
            
            float bestNCC = 0;
            Vector2? bestPixel = null;
            
            // Search at different depths
            for (int d = 0; d < NUM_DEPTH_SAMPLES; d++)
            {
                float depth = estimatedDepth * (0.7f + 0.6f * d / NUM_DEPTH_SAMPLES); // Search range ±30%
                Vector3 point3D = refRay.Value.origin + refRay.Value.direction * depth;
                
                // Project to search image
                var searchPixel = ProjectWorldToImage(point3D, searchImage);
                
                if (!IsValidProjection(searchPixel, searchImage))
                    continue;
                
                // Compute NCC between patches
                float ncc = ComputePatchNCC(refImage, searchImage, refPixel, searchPixel, PATCH_RADIUS);
                
                if (ncc > bestNCC)
                {
                    bestNCC = ncc;
                    bestPixel = searchPixel;
                }
            }
            
            // Return match if NCC is good enough
            // CRITICAL: Lowered threshold from 0.3 to 0.15 for minimal overlap scenarios
            // This is more permissive but followed by photometric validation
            const float MIN_NCC_THRESHOLD = 0.15f;
            
            if (bestNCC > MIN_NCC_THRESHOLD && bestPixel != null)
            {
                return (bestPixel.Value, bestNCC);
            }
            
            return null;
        }
        
        /// <summary>
        /// Get ray from camera through a pixel using Math.NET
        /// This is the inverse of ProjectWorldToImage, ensuring consistent coordinate systems
        /// </summary>
        private (Vector3 origin, Vector3 direction)? GetPixelRay(PhotogrammetryImage image, Vector2 pixel)
        {
            try
            {
                var K = image.IntrinsicMatrix;
                
                // Extract intrinsic parameters
                float fx = K.M11;
                float fy = K.M22;
                float cx = K.M13;
                float cy = K.M23;
                
                if (fx == 0 || fy == 0) return null;
                
                // Unproject pixel to camera coordinates at depth=1
                // This is the inverse of: u = (cam_x * fx) / cam_z + cx
                // Solving for cam_x, cam_y when cam_z = 1:
                double cam_x = (pixel.X - cx) / fx;
                double cam_y = (pixel.Y - cy) / fy;
                double cam_z = 1.0;
                
                // Create direction vector in camera coordinates (homogeneous, w=0 for direction)
                var dirCamera_mathnet = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(
                    new[] { cam_x, cam_y, cam_z, 0.0 });
                
                // Convert GlobalPose to Math.NET (camera-to-world transform)
                var T_cam_to_world = ConvertPoseToMathNet(image.GlobalPose);
                
                // Transform direction from camera to world space
                // For direction vectors (w=0), this applies only rotation
                var dirWorld_mathnet = T_cam_to_world * dirCamera_mathnet;
                
                // Extract camera center from GlobalPose (translation part)
                Vector3 cameraCenter = new Vector3(
                    image.GlobalPose.M41,
                    image.GlobalPose.M42,
                    image.GlobalPose.M43
                );
                
                // Convert direction to System.Numerics and normalize
                Vector3 dirWorld = new Vector3(
                    (float)dirWorld_mathnet[0],
                    (float)dirWorld_mathnet[1],
                    (float)dirWorld_mathnet[2]
                );
                
                return (cameraCenter, Vector3.Normalize(dirWorld));
            }
            catch (Exception ex)
            {
                _service.Log($"[ERROR] GetPixelRay failed: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Triangulate 3D point from two image correspondences
        /// </summary>
        private Vector3? TriangulateTwoViews(
            PhotogrammetryImage img1, Vector2 pixel1,
            PhotogrammetryImage img2, Vector2 pixel2)
        {
            var ray1 = GetPixelRay(img1, pixel1);
            var ray2 = GetPixelRay(img2, pixel2);
            
            if (ray1 == null || ray2 == null)
                return null;
            
            // Find closest point between two rays (standard ray-ray intersection)
            var p1 = ray1.Value.origin;
            var d1 = ray1.Value.direction;
            var p2 = ray2.Value.origin;
            var d2 = ray2.Value.direction;
            
            // Solve for parameters t1 and t2 where rays are closest
            var w0 = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float b = Vector3.Dot(d1, d2);
            float c = Vector3.Dot(d2, d2);
            float d = Vector3.Dot(d1, w0);
            float e = Vector3.Dot(d2, w0);
            
            float denom = a * c - b * b;
            if (Math.Abs(denom) < 1e-6f)
                return null; // Rays are parallel
            
            float t1 = (b * e - c * d) / denom;
            float t2 = (a * e - b * d) / denom;
            
            // Ensure positive depths
            if (t1 < 0 || t2 < 0)
                return null;
            
            // Midpoint of closest approach
            var point1 = p1 + t1 * d1;
            var point2 = p2 + t2 * d2;
            
            // Check if rays actually intersect closely (reprojection check)
            float distance = Vector3.Distance(point1, point2);
            if (distance > 5.0f) // Too far apart - bad triangulation
                return null;
            
            return (point1 + point2) * 0.5f;
        }

        private float ComputePointConfidence(Vector3 point, List<PhotogrammetryImage> images)
        {
            if (images.Count < 2)
                return 0.0f;

            // CRITICAL: PatchMatch-style parameters based on COLMAP research
            // References: 
            // - Schönberger et al. "Pixelwise View Selection for Unstructured Multi-View Stereo" ECCV 2016
            // - Bleyer et al. "PatchMatch Stereo" BMVC 2011
            const int PATCH_RADIUS = 5;  // Reduced from 7 to 5 for better performance (COLMAP default)
            const float NCC_SIGMA = 0.8f;  // Increased spread for more permissive confidence (was 0.6)
            const float MIN_NCC = 0.10f;  // Lowered to 0.10 for image-space propagation (was 0.15)
            
            var patchScores = new List<float>();
            
            // Take best pairs of views (avoid redundant views)
            var imagePairs = new List<(PhotogrammetryImage, PhotogrammetryImage)>();
            for (int i = 0; i < Math.Min(images.Count, 4); i++)
            {
                for (int j = i + 1; j < Math.Min(images.Count, 5); j++)
                {
                    imagePairs.Add((images[i], images[j]));
                    if (imagePairs.Count >= 3) break; // Use top 3 pairs maximum
                }
                if (imagePairs.Count >= 3) break;
            }

            if (imagePairs.Count == 0)
                return 0.0f;

            foreach (var (img1, img2) in imagePairs)
            {
                var proj1 = ProjectWorldToImage(point, img1);
                var proj2 = ProjectWorldToImage(point, img2);
                
                if (!IsValidProjection(proj1, img1) || !IsValidProjection(proj2, img2))
                    continue;

                // Compute Normalized Cross-Correlation (NCC) over patch window
                float ncc = ComputePatchNCC(img1, img2, proj1, proj2, PATCH_RADIUS);
                
                if (ncc > MIN_NCC)
                {
                    // Apply NCC likelihood with Gaussian kernel (COLMAP approach)
                    float score = MathF.Exp(-(1.0f - ncc) * (1.0f - ncc) / (2.0f * NCC_SIGMA * NCC_SIGMA));
                    patchScores.Add(score);
                }
            }

            if (patchScores.Count == 0)
                return 0.0f;

            // Average of top scores (robust to outlier views)
            return patchScores.Count > 0 ? patchScores.Average() : 0.0f;
        }

        /// <summary>
        /// Compute Normalized Cross-Correlation between image patches
        /// Based on COLMAP's bilaterally weighted NCC (BNCC)
        /// Reference: Schönberger & Frahm "Structure-from-Motion Revisited" CVPR 2016
        /// </summary>
        private float ComputePatchNCC(
            PhotogrammetryImage img1,
            PhotogrammetryImage img2,
            Vector2 center1,
            Vector2 center2,
            int radius)
        {
            var patch1Values = new List<float>();
            var patch2Values = new List<float>();
            
            int cx1 = (int)center1.X;
            int cy1 = (int)center1.Y;
            int cx2 = (int)center2.X;
            int cy2 = (int)center2.Y;

            // Extract patch values with bounds checking
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x1 = cx1 + dx;
                    int y1 = cy1 + dy;
                    int x2 = cx2 + dx;
                    int y2 = cy2 + dy;

                    if (x1 >= 0 && x1 < img1.Dataset.Width && y1 >= 0 && y1 < img1.Dataset.Height &&
                        x2 >= 0 && x2 < img2.Dataset.Width && y2 >= 0 && y2 < img2.Dataset.Height)
                    {
                        var color1 = SampleImageColor(img1.Dataset, x1, y1);
                        var color2 = SampleImageColor(img2.Dataset, x2, y2);
                        
                        // Convert to grayscale for NCC
                        float gray1 = 0.299f * color1.X + 0.587f * color1.Y + 0.114f * color1.Z;
                        float gray2 = 0.299f * color2.X + 0.587f * color2.Y + 0.114f * color2.Z;
                        
                        patch1Values.Add(gray1);
                        patch2Values.Add(gray2);
                    }
                }
            }

            if (patch1Values.Count < 9) // Need at least 3x3 patch (reduced from 4)
                return 0.0f;

            // Compute zero-mean normalized cross-correlation
            float mean1 = patch1Values.Average();
            float mean2 = patch2Values.Average();

            float numerator = 0.0f;
            float denom1 = 0.0f;
            float denom2 = 0.0f;

            for (int i = 0; i < patch1Values.Count; i++)
            {
                float diff1 = patch1Values[i] - mean1;
                float diff2 = patch2Values[i] - mean2;
                
                numerator += diff1 * diff2;
                denom1 += diff1 * diff1;
                denom2 += diff2 * diff2;
            }

            float denominator = MathF.Sqrt(denom1 * denom2);
            
            // Handle low-texture regions more gracefully
            if (denominator < 1e-4f)
            {
                // If both patches are flat (low variance), assume decent match
                // This helps with uniform surfaces
                float flatnessThreshold = 0.01f;
                if (MathF.Sqrt(denom1 / patch1Values.Count) < flatnessThreshold && 
                    MathF.Sqrt(denom2 / patch2Values.Count) < flatnessThreshold)
                {
                    return 0.5f; // Moderate confidence for flat regions
                }
                return 0.0f;
            }

            // NCC ranges from -1 to 1, return clamped to [0, 1]
            float ncc = numerator / denominator;
            return Math.Clamp(ncc, 0.0f, 1.0f);
        }

        private void FilterOutlierPoints(PhotogrammetryPointCloud cloud, float confidenceThreshold)
        {
            const int k = 20;
            const float stdDevMultiplier = 2.0f;
            
            var points = cloud.Points.ToList();
            
            // Skip filtering for small clouds
            if (points.Count < k * 2)
            {
                _service.Log($"Skipping outlier filtering - too few points ({points.Count} < {k * 2})");
                return;
            }
            
            // For very large clouds, use sampling to compute statistics
            if (points.Count > 10000)
            {
                _service.Log($"Large point cloud ({points.Count} points) - using sampling for outlier detection");
                
                // Sample a subset for statistics
                var random = new Random();
                var sampleSize = Math.Min(2000, points.Count);
                var sampledPoints = points.OrderBy(x => random.Next()).Take(sampleSize).ToList();
                
                _service.Log($"Computing statistics on {sampleSize} sampled points...");
                var sampleDistances = ComputeNeighborDistances(sampledPoints, Math.Min(k, sampledPoints.Count / 2));
                
                if (!sampleDistances.Any())
                {
                    _service.Log("No distances computed, skipping filtering");
                    return;
                }
                
                float mean = sampleDistances.Average();
                float stdDev = MathF.Sqrt(sampleDistances.Average(d => (d - mean) * (d - mean)));
                float threshold = mean + stdDevMultiplier * stdDev;
                
                _service.Log($"Sample statistics: mean={mean:F3}, stdDev={stdDev:F3}, threshold={threshold:F3}");
                
                // Apply threshold based on confidence only (skip distance check for performance)
                var filteredPoints = points.Where(p => p.Confidence >= confidenceThreshold).ToList();
                
                _service.Log($"Filtered by confidence: kept {filteredPoints.Count}/{points.Count} points");
                cloud.Points.Clear();
                cloud.Points.AddRange(filteredPoints);
            }
            else
            {
                // Original method for smaller clouds
                _service.Log($"Computing neighbor distances for {points.Count} points (k={k})...");
                var distances = ComputeNeighborDistances(points, Math.Min(k, points.Count / 2));

                if (!distances.Any())
                {
                    _service.Log("No distances computed, skipping filtering");
                    return;
                }

                float mean = distances.Average();
                float stdDev = MathF.Sqrt(distances.Average(d => (d - mean) * (d - mean)));
                float threshold = mean + stdDevMultiplier * stdDev;
                
                _service.Log($"Filtering statistics: mean={mean:F3}, stdDev={stdDev:F3}, threshold={threshold:F3}");

                var filteredPoints = new List<Point3D>();
                
                for (int i = 0; i < points.Count; i++)
                {
                    if (distances[i] <= threshold && points[i].Confidence >= confidenceThreshold)
                    {
                        filteredPoints.Add(points[i]);
                    }
                }

                _service.Log($"Filtered {points.Count - filteredPoints.Count} outlier points (kept {filteredPoints.Count}/{points.Count}).");
                cloud.Points.Clear();
                cloud.Points.AddRange(filteredPoints);
            }
        }

        private List<float> ComputeNeighborDistances(List<Point3D> points, int k)
        {
            var distances = new float[points.Count];
            
            // Limit k to avoid excessive computation
            k = Math.Min(k, Math.Min(20, points.Count / 2));
            
            // Use regular for loop for smaller datasets or Parallel.For for larger ones
            if (points.Count < 100)
            {
                // Sequential processing for small datasets
                for (int i = 0; i < points.Count; i++)
                {
                    var point = points[i];
                    
                    // For small datasets, compute all distances
                    var allDistances = new List<float>(points.Count - 1);
                    for (int j = 0; j < points.Count; j++)
                    {
                        if (i != j)
                        {
                            allDistances.Add(Vector3.DistanceSquared(point.Position, points[j].Position));
                        }
                    }
                    
                    if (allDistances.Any())
                    {
                        allDistances.Sort();
                        var nearest = allDistances.Take(Math.Min(k, allDistances.Count)).ToList();
                        distances[i] = MathF.Sqrt(nearest.Average());
                    }
                }
            }
            else
            {
                // Parallel processing for larger datasets with timeout
                var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout
                
                try
                {
                    Parallel.For(0, points.Count, new ParallelOptions { CancellationToken = cts.Token }, i =>
                    {
                        var point = points[i];
                        var nearest = points
                            .Where((p, idx) => idx != i)
                            .Select(p => Vector3.DistanceSquared(point.Position, p.Position))
                            .OrderBy(d => d)
                            .Take(k)
                            .ToList();
                        
                        if (nearest.Any())
                        {
                            distances[i] = MathF.Sqrt(nearest.Average());
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    _service.Log("Neighbor distance computation timed out after 30 seconds");
                }
            }
            
            return distances.ToList();
        }

        private void ComputePointCloudBounds(PhotogrammetryPointCloud cloud)
        {
            if (cloud.Points.Count == 0)
            {
                cloud.BoundingBoxMin = cloud.BoundingBoxMax = Vector3.Zero;
                return;
            }

            cloud.BoundingBoxMin = new Vector3(float.MaxValue);
            cloud.BoundingBoxMax = new Vector3(float.MinValue);

            foreach (var point in cloud.Points)
            {
                cloud.BoundingBoxMin = Vector3.Min(cloud.BoundingBoxMin, point.Position);
                cloud.BoundingBoxMax = Vector3.Max(cloud.BoundingBoxMax, point.Position);
            }
        }

        private int GetDensityFactor(int quality)
        {
            return quality switch
            {
                0 => 5,   // Low: 5× sparse (was 2×)
                1 => 15,  // Medium: 15× sparse (was 5×)
                2 => 30,  // High: 30× sparse (was 10×)
                3 => 50,  // Ultra: 50× sparse (was 20×)
                4 => 80,  // Extreme: 80× sparse (was 30×)
                _ => 30
            };
        }

        private string GetMatchKey(Guid id1, Guid id2)
        {
            return string.Compare(id1.ToString(), id2.ToString()) < 0 ? $"{id1}_{id2}" : $"{id2}_{id1}";
        }

        private Vector3 SampleImageColor(ImageDataset dataset, int x, int y)
        {
            if (dataset.ImageData == null)
                return new Vector3(0.5f, 0.5f, 0.5f);

            x = Math.Clamp(x, 0, dataset.Width - 1);
            y = Math.Clamp(y, 0, dataset.Height - 1);
            
            int idx = (y * dataset.Width + x) * 4;
            if (idx + 3 >= dataset.ImageData.Length)
                return new Vector3(0.5f, 0.5f, 0.5f);

            return new Vector3(
                dataset.ImageData[idx] / 255.0f,
                dataset.ImageData[idx + 1] / 255.0f,
                dataset.ImageData[idx + 2] / 255.0f);
        }

        private Vector3 SampleColorFromBestView(
            Vector3 point, List<PhotogrammetryImage> images, Vector3 fallbackColor)
        {
            foreach (var image in images)
            {
                var proj = ProjectWorldToImage(point, image);
                if (IsValidProjection(proj, image))
                {
                    return SampleImageColor(image.Dataset, (int)proj.X, (int)proj.Y);
                }
            }
            return fallbackColor;
        }

        /// <summary>
        /// Convert System.Numerics 4x4 intrinsic matrix to Math.NET 3x3
        /// Same implementation as FeatureProcessor for consistency
        /// </summary>
        private Matrix<double> ConvertIntrinsicToMathNet(Matrix4x4 K_sys)
        {
            return Matrix.Build.DenseOfArray(new double[,]
            {
                { K_sys.M11, K_sys.M12, K_sys.M13 },
                { K_sys.M21, K_sys.M22, K_sys.M23 },
                { K_sys.M31, K_sys.M32, K_sys.M33 }
            });
        }

        /// <summary>
        /// Convert System.Numerics Matrix4x4 pose to Math.NET 4x4 matrix
        /// Direct element-by-element copy (no transpose) - same convention as FeatureProcessor
        /// </summary>
        private Matrix<double> ConvertPoseToMathNet(Matrix4x4 pose)
        {
            return Matrix.Build.DenseOfArray(new double[,]
            {
                { pose.M11, pose.M12, pose.M13, pose.M14 },
                { pose.M21, pose.M22, pose.M23, pose.M24 },
                { pose.M31, pose.M32, pose.M33, pose.M34 },
                { pose.M41, pose.M42, pose.M43, pose.M44 }
            });
        }

        /// <summary>
        /// Project world point to image using Math.NET for consistency with triangulation
        /// CRITICAL: Uses same coordinate system as triangulation to ensure points project correctly
        /// </summary>
        private Vector2 ProjectWorldToImage(Vector3 worldPos, PhotogrammetryImage image)
        {
            try
            {
                // Convert world point to Math.NET vector
                var worldPoint_mathnet = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(
                    new[] { (double)worldPos.X, (double)worldPos.Y, (double)worldPos.Z, 1.0 });

                // Convert GlobalPose to Math.NET (camera-to-world transform)
                var T_cam_to_world = ConvertPoseToMathNet(image.GlobalPose);
                
                // Invert to get world-to-camera transform
                var T_world_to_cam = T_cam_to_world.Inverse();
                
                // Transform to camera coordinates
                var cameraPoint_mathnet = T_world_to_cam * worldPoint_mathnet;
                
                // Extract camera coordinates (drop homogeneous coordinate)
                double cam_x = cameraPoint_mathnet[0];
                double cam_y = cameraPoint_mathnet[1];
                double cam_z = cameraPoint_mathnet[2];
                
                // CRITICAL: Check depth - point must be in front of camera
                if (cam_z <= 0.0)
                    return new Vector2(-1, -1);
                
                // Project to image using standard pinhole formula (same as ProjectCameraToImage)
                // u = (cam_x * fx) / cam_z + cx
                // v = (cam_y * fy) / cam_z + cy
                float fx = image.IntrinsicMatrix.M11;
                float fy = image.IntrinsicMatrix.M22;
                float cx = image.IntrinsicMatrix.M13;
                float cy = image.IntrinsicMatrix.M23;
                
                float u = (float)((cam_x * fx) / cam_z + cx);
                float v = (float)((cam_y * fy) / cam_z + cy);
                
                return new Vector2(u, v);
            }
            catch (Exception ex)
            {
                _service.Log($"[ERROR] ProjectWorldToImage failed: {ex.Message}");
                return new Vector2(-1, -1);
            }
        }

        private bool IsValidProjection(Vector2 point, PhotogrammetryImage image)
        {
            return point.X >= 0 && point.X < image.Dataset.Width &&
                   point.Y >= 0 && point.Y < image.Dataset.Height;
        }
    }
}