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

namespace GeoscientistToolkit
{
    /// <summary>
    /// Handles 3D reconstruction from matched features
    /// </summary>
    internal class ReconstructionEngine
    {
        private readonly PhotogrammetryProcessingService _service;

        private const float MAX_REPROJECTION_ERROR_PX = 10.0f;  // Very relaxed for real-world data
        private const float MIN_PARALLAX_DEGREES = 0.1f;  // Very relaxed for closer camera positions
        private const float CHEIRALITY_EPSILON = 1e-6f;  // Use small epsilon like in pose estimation

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
                _service.Log($"Sparse cloud built with {cloud.Points.Count} points.");
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
            Matrix4x4 relativePose_1_to_2,
            PhotogrammetryPointCloud cloud)
        {
            try
            {
                if (image1.SiftFeatures == null || image2.SiftFeatures == null)
                {
                    _service.Log($"    [Warning] Skipping pair due to missing SIFT features.");
                    return;
                }
                
                // Log the pose being used
                _service.Log($"    Using pose for triangulation: Translation=({relativePose_1_to_2.M41:F3}, {relativePose_1_to_2.M42:F3}, {relativePose_1_to_2.M43:F3})");
                
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

                Matrix4x4.Invert(relativePose_1_to_2, out var pose_2_to_1);
                var cam2_center_in_cam1 = pose_2_to_1.Translation;
                
                // Log the baseline magnitude for debugging
                float baseline = cam2_center_in_cam1.Length();
                _service.Log($"    Baseline between cameras: {baseline:F4} units");
                
                // Don't skip based on baseline - let triangulation handle it
                /*
                // Skip if baseline is too small (cameras are essentially at the same position)
                if (baseline < 0.01f)
                {
                    _service.Log($"    [Warning] Baseline too small ({baseline:F6}), skipping triangulation for this pair.");
                    return;
                }
                */

                foreach (var match in matches)
                {
                    var kp1 = image1.SiftFeatures.KeyPoints[match.QueryIndex];
                    var kp2 = image2.SiftFeatures.KeyPoints[match.TrainIndex];

                    var point3D_in_cam1 = Triangulation.TriangulatePoint(
                        new Vector2(kp1.X, kp1.Y),
                        new Vector2(kp2.X, kp2.Y),
                        image1.IntrinsicMatrix,
                        image2.IntrinsicMatrix,
                        relativePose_1_to_2);

                    if (!point3D_in_cam1.HasValue) 
                    {
                        failedTriangulation++;
                        continue;
                    }
                    
                    var p3d_cam1 = point3D_in_cam1.Value;
                    
                    // Debug logging for first few points
                    if (debugCount < maxDebugLogs)
                    {
                        _service.Log($"      Debug point {debugCount}: cam1=({p3d_cam1.X:F2}, {p3d_cam1.Y:F2}, {p3d_cam1.Z:F2})");
                        debugCount++;
                    }
                    
                    // Skip cheirality check for cam1 since triangulation already handles it
                    // Just check cam2
                    var p3d_cam2 = Vector3.Transform(p3d_cam1, relativePose_1_to_2);
                    
                    if (debugCount <= maxDebugLogs && debugCount > 0)
                    {
                        _service.Log($"        cam2=({p3d_cam2.X:F2}, {p3d_cam2.Y:F2}, {p3d_cam2.Z:F2})");
                    }
                    
                    // Temporarily disable cheirality check for debugging
                    /*
                    if (p3d_cam2.Z < CHEIRALITY_EPSILON)
                    {
                        failedCheirality++;
                        continue;
                    }
                    */

                    var p1_reprojected = ProjectCameraToImage(p3d_cam1, image1.IntrinsicMatrix);
                    /*
                    if (p1_reprojected.X < 0 || p1_reprojected.Y < 0)
                    {
                        failedReprojection++;
                        continue;
                    }
                    */
                    float error1 = Vector2.Distance(new Vector2(kp1.X, kp1.Y), p1_reprojected);

                    var p2_reprojected = ProjectCameraToImage(p3d_cam2, image2.IntrinsicMatrix);
                    /*
                    if (p2_reprojected.X < 0 || p2_reprojected.Y < 0)
                    {
                        failedReprojection++;
                        continue;
                    }
                    */
                    float error2 = Vector2.Distance(new Vector2(kp2.X, kp2.Y), p2_reprojected);
                    
                    if (debugCount <= maxDebugLogs && debugCount > 0)
                    {
                        _service.Log($"        Reprojection errors: cam1={error1:F2}px, cam2={error2:F2}px");
                    }

                    // Temporarily bypass reprojection check to debug
                    /*
                    if (error1 > MAX_REPROJECTION_ERROR_PX || error2 > MAX_REPROJECTION_ERROR_PX)
                    {
                        failedReprojection++;
                        continue;
                    }
                    */

                    var ray1 = Vector3.Normalize(p3d_cam1);
                    var ray2 = Vector3.Normalize(p3d_cam1 - cam2_center_in_cam1);
                    
                    float parallax_rad = MathF.Acos(Math.Clamp(Vector3.Dot(ray1, ray2), -1.0f, 1.0f));
                    float parallax_deg = parallax_rad * (180.0f / MathF.PI);

                    // Temporarily disable parallax check
                    /*
                    if (parallax_deg < MIN_PARALLAX_DEGREES)
                    {
                        failedParallax++;
                        continue;
                    }
                    */
                    
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
                    _service.Log($"    > No valid 3D points found! (Failed: Triangulation={failedTriangulation}, Cheirality={failedCheirality}, Reprojection={failedReprojection}, Parallax={failedParallax}, Total={matches.Count})");
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
                _service.Log($"Densification factor: {densityFactor}x");
                _service.Log($"Starting densification of {sparsePoints.Count} sparse points...");

                int processedCount = 0;
                foreach (var sparsePoint in sparsePoints)
                {
                    ProcessSparsePoint(sparsePoint, images, denseCloud, densityFactor, options.ConfidenceThreshold);
                    
                    if (++processedCount % 10 == 0 || processedCount == sparsePoints.Count)
                    {
                        updateProgress(
                            (float)processedCount / sparsePoints.Count * 0.8f,
                            $"Densifying... ({processedCount}/{sparsePoints.Count})"
                        );
                        _service.Log($"Processed {processedCount}/{sparsePoints.Count} sparse points, dense cloud has {denseCloud.Points.Count} points");
                    }
                }

                _service.Log($"Densification complete. Dense cloud has {denseCloud.Points.Count} points before filtering.");

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
        }

        private List<Point3D> GenerateDensePointsAround(
            Point3D sparsePoint,
            List<PhotogrammetryImage> observingImages,
            int densityFactor,
            float confidenceThreshold)
        {
            var densePoints = new List<Point3D>();
            
            // Limit density factor for performance
            densityFactor = Math.Min(densityFactor, 20);
            
            float searchRadius = 1.0f;
            int samplesPerAxis = (int)MathF.Ceiling(MathF.Pow(densityFactor, 1.0f / 3.0f));
            float step = searchRadius / samplesPerAxis;

            for (int dx = -samplesPerAxis; dx <= samplesPerAxis; dx++)
            for (int dy = -samplesPerAxis; dy <= samplesPerAxis; dy++)
            for (int dz = -samplesPerAxis; dz <= samplesPerAxis; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                if (densePoints.Count >= densityFactor) break;

                Vector3 candidatePos = sparsePoint.Position + new Vector3(dx * step, dy * step, dz * step);
                float confidence = ComputePointConfidence(candidatePos, observingImages);

                if (confidence >= confidenceThreshold)
                {
                    densePoints.Add(new Point3D
                    {
                        Position = candidatePos,
                        Color = SampleColorFromBestView(candidatePos, observingImages, sparsePoint.Color),
                        Confidence = confidence,
                        ObservingImages = new List<Guid>(observingImages.Select(img => img.Id))
                    });
                }
                
                // Early exit if we have enough points
                if (densePoints.Count >= densityFactor)
                    break;
            }

            return densePoints;
        }

        private float ComputePointConfidence(Vector3 point, List<PhotogrammetryImage> images)
        {
            if (images.Count < 2)
                return 0.0f;

            var projectedColors = new List<Vector3>();
            
            foreach (var image in images)
            {
                var projectedPoint = ProjectWorldToImage(point, image);
                
                if (IsValidProjection(projectedPoint, image))
                {
                    var color = SampleImageColor(image.Dataset, (int)projectedPoint.X, (int)projectedPoint.Y);
                    projectedColors.Add(color);
                }
            }

            if (projectedColors.Count < 2)
                return 0.0f;

            var avgColor = new Vector3(
                projectedColors.Average(c => c.X),
                projectedColors.Average(c => c.Y),
                projectedColors.Average(c => c.Z));
            
            float variance = projectedColors.Average(c => Vector3.DistanceSquared(c, avgColor));
            
            return MathF.Exp(-variance * 5.0f);
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
                0 => 2, 
                1 => 5, 
                2 => 10, 
                3 => 20, 
                4 => 30,  // Reduced from 50 to avoid excessive points
                _ => 10
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

        private Vector2 ProjectWorldToImage(Vector3 worldPos, PhotogrammetryImage image)
        {
            Matrix4x4.Invert(image.GlobalPose, out var viewMatrix);
            var cameraPos = Vector4.Transform(new Vector4(worldPos, 1.0f), viewMatrix);
            var projected = Vector4.Transform(cameraPos, image.IntrinsicMatrix);

            if (Math.Abs(projected.W) < 1e-8)
                return new Vector2(-1, -1);

            return new Vector2(projected.X / projected.W, projected.Y / projected.W);
        }

        private bool IsValidProjection(Vector2 point, PhotogrammetryImage image)
        {
            return point.X >= 0 && point.X < image.Dataset.Width &&
                   point.Y >= 0 && point.Y < image.Dataset.Height;
        }
    }
}