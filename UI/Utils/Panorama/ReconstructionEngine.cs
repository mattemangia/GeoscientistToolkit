// GeoscientistToolkit/Business/Photogrammetry/Reconstruction/ReconstructionEngine.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Business.Photogrammetry;
using GeoscientistToolkit.Business.Photogrammetry.Math;
using GeoscientistToolkit.Data.Image;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Handles 3D reconstruction from matched features
    /// </summary>
    internal class ReconstructionEngine
    {
        private readonly PhotogrammetryProcessingService _service;

        public ReconstructionEngine(PhotogrammetryProcessingService service)
        {
            _service = service;
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

                foreach (var node in graph.GetNodes())
                {
                    ProcessNodeMatches(node, images, graph, cloud, processedMatches);
                }

                ComputePointCloudBounds(cloud);
                _service.Log($"Sparse cloud built with {cloud.Points.Count} points.");

                return cloud;
            });
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

        public void ComputeGlobalPoses(
            PhotogrammetryImageGroup group,
            List<PhotogrammetryImage> images,
            PhotogrammetryGraph graph)
        {
            if (group.Images.Count == 0)
                return;

            var visited = new HashSet<Guid>();
            var queue = new Queue<Guid>();

            // Start with first image as reference
            var referenceImage = group.Images[0];
            referenceImage.GlobalPose = Matrix4x4.Identity;
            queue.Enqueue(referenceImage.Id);
            visited.Add(referenceImage.Id);

            // Breadth-first traversal to compute global poses
            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                var currentImage = images.First(img => img.Id == currentId);

                if (graph.TryGetNeighbors(currentId, out var neighbors))
                {
                    foreach (var (neighborId, _, relativePose) in neighbors)
                    {
                        if (!visited.Contains(neighborId))
                        {
                            visited.Add(neighborId);
                            var neighborImage = images.First(img => img.Id == neighborId);

                            // Compute global pose from relative pose
                            neighborImage.GlobalPose = currentImage.GlobalPose * relativePose;

                            queue.Enqueue(neighborId);
                        }
                    }
                }
            }
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

            foreach (var (neighborId, matches, pose) in edges)
            {
                var matchKey = GetMatchKey(node.Id, neighborId);
                if (processedMatches.Contains(matchKey))
                    continue;

                processedMatches.Add(matchKey);
                
                var neighbor = images.First(img => img.Id == neighborId);
                
                Matrix4x4.Invert(node.GlobalPose, out var invNodePose);
                var relativePose = neighbor.GlobalPose * invNodePose;

                TriangulateMatches(node, neighbor, matches, relativePose, cloud);
            }
        }

        private void TriangulateMatches(
            PhotogrammetryImage image1,
            PhotogrammetryImage image2,
            List<FeatureMatch> matches,
            Matrix4x4 relativePose,
            PhotogrammetryPointCloud cloud)
        {
            foreach (var match in matches)
            {
                var kp1 = image1.Features.KeyPoints[match.QueryIndex];
                var kp2 = image2.Features.KeyPoints[match.TrainIndex];

                var point3D = Triangulation.TriangulatePoint(
                    new Vector2(kp1.X, kp1.Y),
                    new Vector2(kp2.X, kp2.Y),
                    image1.IntrinsicMatrix,
                    image2.IntrinsicMatrix,
                    relativePose);

                if (point3D.HasValue && point3D.Value.Z > 0)
                {
                    var worldPoint = Vector3.Transform(point3D.Value, image1.GlobalPose);
                    var color = SampleImageColor(image1.Dataset, (int)kp1.X, (int)kp1.Y);
                    
                    cloud.Points.Add(new Point3D
                    {
                        Position = worldPoint,
                        Color = color,
                        Confidence = 1.0f - match.Distance / 256.0f,
                        ObservingImages = { image1.Id, image2.Id }
                    });
                }
            }
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
                if (densePoints.Count >= densityFactor)
                    break;

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

            // Compute color consistency
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
            var distances = ComputeNeighborDistances(points, k);

            if (!distances.Any())
                return;

            float mean = distances.Average();
            float stdDev = MathF.Sqrt(distances.Average(d => (d - mean) * (d - mean)));
            float threshold = mean + stdDevMultiplier * stdDev;

            var filteredPoints = new List<Point3D>();
            
            for (int i = 0; i < points.Count && i < distances.Count; i++)
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
            var distances = new List<float>();
            
            foreach (var point in points)
            {
                var nearest = points
                    .Where(p => p != point)
                    .Select(p => Vector3.Distance(point.Position, p.Position))
                    .OrderBy(d => d)
                    .Take(k)
                    .ToList();
                
                if (nearest.Any())
                    distances.Add(nearest.Average());
            }
            
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
                0 => 2,
                1 => 5,
                2 => 10,
                3 => 20,
                4 => 50,
                _ => 10
            };
        }

        private string GetMatchKey(Guid id1, Guid id2)
        {
            return string.Compare(id1.ToString(), id2.ToString()) < 0
                ? $"{id1}_{id2}"
                : $"{id2}_{id1}";
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
            Vector3 point,
            List<PhotogrammetryImage> images,
            Vector3 fallbackColor)
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