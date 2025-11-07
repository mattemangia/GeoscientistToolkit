// GeoscientistToolkit/Business/Photogrammetry/Reconstruction/ReconstructionEngine.cs

// =================================================================================================
// FINAL ROBUST VERSION v4
// - Fixes the definitive root cause of the "0 points" issue.
//   1. Corrects `ComputeGlobalPoses` to use the inverse of the stored relative pose,
//      ensuring the global camera poses are geometrically correct.
//   2. Corrects `ProcessNodeMatches` to use the relative pose from the graph directly
//      (in the correct orientation) instead of incorrectly recalculating it.
// - This ensures the correct geometric data is fed to the triangulation engine.
// =================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

        private const float MAX_REPROJECTION_ERROR_PX = 2.0f;
        private const float MIN_PARALLAX_DEGREES = 1.0f;
        private const float CHEIRALITY_EPSILON = 1e-6f;

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
                    foreach (var (neighborId, _, relativePose_neighbor_to_current) in neighbors)
                    {
                        if (!visited.Contains(neighborId))
                        {
                            visited.Add(neighborId);
                            var neighborImage = images.First(img => img.Id == neighborId);

                            // --- START OF FIX ---
                            // The graph stores the pose T_Neighbor->Current.
                            // To find the neighbor's global pose, we need T_Current->Neighbor.
                            // GlobalPose_Neighbor = GlobalPose_Current * T_Current->Neighbor
                            // T_Current->Neighbor is the inverse of T_Neighbor->Current.
                            Matrix4x4.Invert(relativePose_neighbor_to_current, out var pose_current_to_neighbor);
                            neighborImage.GlobalPose = currentImage.GlobalPose * pose_current_to_neighbor;
                            // --- END OF FIX ---

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

            foreach (var (neighborId, matches, pose_neighbor_to_node) in edges)
            {
                var matchKey = GetMatchKey(node.Id, neighborId);
                if (processedMatches.Contains(matchKey))
                    continue;

                processedMatches.Add(matchKey);
                
                var neighbor = images.First(img => img.Id == neighborId);
                
                _service.Log($"  Triangulating {matches.Count} matches between {node.Dataset.Name} and {neighbor.Dataset.Name}");

                // --- START OF FIX ---
                // Stop recalculating the pose from global poses. Use the one from the graph.
                // The triangulation function needs the pose T_Node->Neighbor.
                // The graph stores T_Neighbor->Node for the edge Node->Neighbor.
                // Therefore, we must invert the stored pose.
                Matrix4x4.Invert(pose_neighbor_to_node, out var relativePose_node_to_neighbor);
                // --- END OF FIX ---

                TriangulateMatches(node, neighbor, matches, relativePose_node_to_neighbor, cloud);
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

                int successfulTriangulations = 0;
                int failedCheirality = 0;
                int failedReprojection = 0;
                int failedParallax = 0;

                Matrix4x4.Invert(relativePose_1_to_2, out var pose_2_to_1);
                var cam2_center_in_cam1 = pose_2_to_1.Translation;

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

                    if (!point3D_in_cam1.HasValue) continue;
                    
                    var p3d_cam1 = point3D_in_cam1.Value;
                    if (p3d_cam1.Z < CHEIRALITY_EPSILON)
                    {
                        failedCheirality++;
                        continue;
                    }
                    var p3d_cam2 = Vector3.Transform(p3d_cam1, relativePose_1_to_2);
                    if (p3d_cam2.Z < CHEIRALITY_EPSILON)
                    {
                        failedCheirality++;
                        continue;
                    }

                    var p1_reprojected = ProjectCameraToImage(p3d_cam1, image1.IntrinsicMatrix);
                    float error1 = Vector2.DistanceSquared(new Vector2(kp1.X, kp1.Y), p1_reprojected);

                    var p2_reprojected = ProjectCameraToImage(p3d_cam2, image2.IntrinsicMatrix);
                    float error2 = Vector2.DistanceSquared(new Vector2(kp2.X, kp2.Y), p2_reprojected);

                    if (error1 > (MAX_REPROJECTION_ERROR_PX * MAX_REPROJECTION_ERROR_PX) || 
                        error2 > (MAX_REPROJECTION_ERROR_PX * MAX_REPROJECTION_ERROR_PX))
                    {
                        failedReprojection++;
                        continue;
                    }

                    var ray1 = Vector3.Normalize(p3d_cam1);
                    var ray2 = Vector3.Normalize(p3d_cam1 - cam2_center_in_cam1);
                    
                    float parallax_rad = MathF.Acos(Math.Clamp(Vector3.Dot(ray1, ray2), -1.0f, 1.0f));
                    float parallax_deg = parallax_rad * (180.0f / MathF.PI);

                    if (parallax_deg < MIN_PARALLAX_DEGREES)
                    {
                        failedParallax++;
                        continue;
                    }
                    
                    var worldPoint = Vector3.Transform(p3d_cam1, image1.GlobalPose);
                    var color = SampleImageColor(image1.Dataset, (int)kp1.X, (int)kp1.Y);
                    
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
                    _service.Log($"    > Found {successfulTriangulations} valid 3D points. (Rejected: Cheirality={failedCheirality}, Reprojection={failedReprojection}, Parallax={failedParallax})");
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

                int processedCount = 0;
                foreach (var sparsePoint in sparsePoints)
                {
                    ProcessSparsePoint(sparsePoint, images, denseCloud, densityFactor, options.ConfidenceThreshold);
                    
                    if (++processedCount % 100 == 0)
                    {
                        updateProgress(
                            (float)processedCount / sparsePoints.Count,
                            $"Densifying... ({processedCount}/{sparsePoints.Count})"
                        );
                    }
                }

                if (options.FilterOutliers)
                {
                    _service.Log("Filtering outliers using statistical analysis...");
                    FilterOutlierPoints(denseCloud, options.ConfidenceThreshold);
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
            if (points.Count < k) return;

            var distances = ComputeNeighborDistances(points, k);

            if (!distances.Any())
                return;

            float mean = distances.Average();
            float stdDev = MathF.Sqrt(distances.Average(d => (d - mean) * (d - mean)));
            float threshold = mean + stdDevMultiplier * stdDev;

            var filteredPoints = new List<Point3D>();
            
            for (int i = 0; i < points.Count; i++)
            {
                if (distances[i] <= threshold && points[i].Confidence >= confidenceThreshold)
                {
                    filteredPoints.Add(points[i]);
                }
            }

            _service.Log($"Filtered {points.Count - filteredPoints.Count} outlier points.");
            cloud.Points.Clear();
            cloud.Points.AddRange(filteredPoints);
        }

        private List<float> ComputeNeighborDistances(List<Point3D> points, int k)
        {
            var distances = new List<float>(points.Count);
            
            Parallel.For(0, points.Count, i =>
            {
                var point = points[i];
                var nearest = points
                    .Select(p => Vector3.DistanceSquared(point.Position, p.Position))
                    .OrderBy(d => d)
                    .Skip(1) // Skip self
                    .Take(k)
                    .ToList();
                
                if (nearest.Any())
                {
                    distances[i] = MathF.Sqrt(nearest.Average());
                }
            });
            
            return distances;
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
                0 => 2, 1 => 5, 2 => 10, 3 => 20, 4 => 50, _ => 10
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