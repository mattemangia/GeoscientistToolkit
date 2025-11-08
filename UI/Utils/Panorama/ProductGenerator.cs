// GeoscientistToolkit/Business/Photogrammetry/Products/ProductGenerator.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Photogrammetry;
using SkiaSharp;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Generates orthomosaics, DEMs, and other photogrammetric products
    /// </summary>
    internal class ProductGenerator
    {
        private readonly PhotogrammetryProcessingService _service;

        public ProductGenerator(PhotogrammetryProcessingService service)
        {
            _service = service;
        }

        public async Task BuildOrthomosaicAsync(
            PhotogrammetryPointCloud cloud,
            Data.Mesh3D.Mesh3DDataset mesh,
            List<PhotogrammetryImage> images,
            OrthomosaicOptions options,
            string outputPath,
            Action<float, string> updateProgress)
        {
            updateProgress(0, "Orthomosaic generation in progress...");

            try
            {
                await Task.Run(async () =>
                {
                    var rasterData = await GenerateOrthomosaicRaster(
                        cloud, mesh, images, options, updateProgress);

                    SaveRasterImage(rasterData.rgba, rasterData.width, rasterData.height, outputPath);
                    
                    _service.Log($"Orthomosaic saved to {outputPath} " +
                               $"({rasterData.width}x{rasterData.height}, GSD={rasterData.gsd:F4} m/px)");
                });

                updateProgress(1.0f, "Orthomosaic complete");
            }
            catch (Exception ex)
            {
                _service.Log($"Orthomosaic error: {ex.Message}");
                throw;
            }
        }

        public async Task BuildDEMAsync(
            PhotogrammetryPointCloud cloud,
            Data.Mesh3D.Mesh3DDataset mesh,
            DEMOptions options,
            string outputPath,
            Action<float, string> updateProgress)
        {
            updateProgress(0, "DEM generation in progress...");

            try
            {
                await Task.Run(async () =>
                {
                    var demData = await GenerateDEM(cloud, mesh, options, updateProgress);
                    
                    SaveRasterImage(demData.rgba, demData.width, demData.height, outputPath);
                    SaveDEMMetadata(outputPath, demData.minZ, demData.maxZ);
                    
                    _service.Log($"DEM saved to {outputPath} " +
                               $"({demData.width}x{demData.height}, res={demData.resolution:F4} m/px). " +
                               $"MinZ={demData.minZ:F3} MaxZ={demData.maxZ:F3}");
                });

                updateProgress(1.0f, "DEM complete");
            }
            catch (Exception ex)
            {
                _service.Log($"DEM error: {ex.Message}");
                throw;
            }
        }

        private async Task<(byte[] rgba, int width, int height, float gsd)> GenerateOrthomosaicRaster(
            PhotogrammetryPointCloud cloud,
            Data.Mesh3D.Mesh3DDataset mesh,
            List<PhotogrammetryImage> images,
            OrthomosaicOptions options,
            Action<float, string> updateProgress)
        {
            // Determine bounds
            var bounds = ComputeBounds(cloud, mesh, options.Source);
            
            float gsd = Math.Max(1e-6f, options.GroundSamplingDistance);
            int width = Math.Max(1, (int)Math.Ceiling((bounds.maxX - bounds.minX) / gsd));
            int height = Math.Max(1, (int)Math.Ceiling((bounds.maxY - bounds.minY) / gsd));

            // Limit maximum size
            (width, height, gsd) = LimitRasterSize(width, height, gsd, 16384);

            byte[] rgba = new byte[width * height * 4];

            // Prepare elevation sampler
            var elevationSampler = CreateElevationSampler(cloud, mesh, options.Source);

            // Generate orthomosaic pixels
            int reportEvery = Math.Max(1, height / 50);
            
            for (int j = 0; j < height; j++)
            {
                float y = bounds.minY + j * gsd + gsd * 0.5f;
                
                for (int i = 0; i < width; i++)
                {
                    float x = bounds.minX + i * gsd + gsd * 0.5f;
                    
                    if (elevationSampler.TrySampleZ(x, y, out float z))
                    {
                        var world = new Vector3(x, y, z);
                        var color = options.EnableBlending
                            ? SampleBlendedColor(world, images, options.MaxBlendImages)
                            : SampleBestColor(world, images);

                        int idx = (j * width + i) * 4;
                        rgba[idx] = (byte)Math.Clamp((int)(color.X * 255.0f), 0, 255);
                        rgba[idx + 1] = (byte)Math.Clamp((int)(color.Y * 255.0f), 0, 255);
                        rgba[idx + 2] = (byte)Math.Clamp((int)(color.Z * 255.0f), 0, 255);
                        rgba[idx + 3] = 255;
                    }
                    else
                    {
                        // Transparent pixel for no data
                        int idx = (j * width + i) * 4;
                        rgba[idx] = rgba[idx + 1] = rgba[idx + 2] = rgba[idx + 3] = 0;
                    }
                }

                if (j % reportEvery == 0)
                {
                    updateProgress(j / (float)height, $"Orthomosaic line {j}/{height}");
                    await Task.Yield();
                }
            }

            return (rgba, width, height, gsd);
        }

        private async Task<(byte[] rgba, int width, int height, float resolution, float minZ, float maxZ)> 
            GenerateDEM(
            PhotogrammetryPointCloud cloud,
            Data.Mesh3D.Mesh3DDataset mesh,
            DEMOptions options,
            Action<float, string> updateProgress)
        {
            // Determine bounds
            var useMesh = options.Source == DEMOptions.SourceData.Mesh && mesh != null;
            var bounds = ComputeBounds(cloud, mesh, 
                useMesh ? OrthomosaicOptions.SourceData.Mesh : OrthomosaicOptions.SourceData.PointCloud);

            float res = Math.Max(1e-6f, options.Resolution);
            int width = Math.Max(1, (int)Math.Ceiling((bounds.maxX - bounds.minX) / res));
            int height = Math.Max(1, (int)Math.Ceiling((bounds.maxY - bounds.minY) / res));

            // Limit maximum size
            (width, height, res) = LimitRasterSize(width, height, res, 16384);

            // Prepare elevation sampler
            var elevationSampler = CreateElevationSampler(
                cloud, mesh, 
                useMesh ? OrthomosaicOptions.SourceData.Mesh : OrthomosaicOptions.SourceData.PointCloud);

            // Generate elevation raster
            float[,] elevations = new float[height, width];
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;

            int reportEvery = Math.Max(1, height / 50);
            
            for (int j = 0; j < height; j++)
            {
                float y = bounds.minY + j * res + res * 0.5f;
                
                for (int i = 0; i < width; i++)
                {
                    float x = bounds.minX + i * res + res * 0.5f;
                    
                    if (elevationSampler.TrySampleZ(x, y, out float z))
                    {
                        elevations[j, i] = z;
                        if (z < minZ) minZ = z;
                        if (z > maxZ) maxZ = z;
                    }
                    else
                    {
                        elevations[j, i] = float.NaN;
                    }
                }

                if (j % reportEvery == 0)
                {
                    updateProgress(j / (float)height, $"DEM line {j}/{height}");
                    await Task.Yield();
                }
            }

            // Post-process elevation data
            if (options.FillHoles)
                FillElevationHoles(elevations);
            
            if (options.SmoothSurface)
                SmoothElevations(elevations);

            // Normalize range for visualization
            if (!float.IsFinite(minZ) || !float.IsFinite(maxZ) || Math.Abs(maxZ - minZ) < 1e-6f)
            {
                minZ = 0.0f;
                maxZ = 1.0f;
            }

            // Convert to grayscale image
            byte[] rgba = new byte[width * height * 4];
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    float z = elevations[j, i];
                    byte value = float.IsNaN(z) ? (byte)0 
                        : (byte)Math.Clamp((int)(255.0f * (z - minZ) / (maxZ - minZ)), 0, 255);

                    int idx = (j * width + i) * 4;
                    rgba[idx] = rgba[idx + 1] = rgba[idx + 2] = value;
                    rgba[idx + 3] = 255;
                }
            }

            return (rgba, width, height, res, minZ, maxZ);
        }

        private ElevationSampler CreateElevationSampler(
            PhotogrammetryPointCloud cloud,
            Data.Mesh3D.Mesh3DDataset mesh,
            OrthomosaicOptions.SourceData source)
        {
            if (source == OrthomosaicOptions.SourceData.Mesh && mesh != null)
            {
                return new MeshElevationSampler(mesh);
            }
            else if (cloud != null)
            {
                return new CloudElevationSampler(cloud);
            }
            
            throw new InvalidOperationException("No valid geometry source for elevation sampling");
        }

        private (float minX, float maxX, float minY, float maxY) ComputeBounds(
            PhotogrammetryPointCloud cloud,
            Data.Mesh3D.Mesh3DDataset mesh,
            OrthomosaicOptions.SourceData source)
        {
            List<Vector3> points;
            
            if (source == OrthomosaicOptions.SourceData.Mesh && mesh != null)
            {
                points = mesh.Vertices;
            }
            else if (cloud != null)
            {
                points = cloud.Points.Select(p => p.Position).ToList();
            }
            else
            {
                return (0, 1, 0, 1);
            }

            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;

            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            if (!float.IsFinite(minX) || !float.IsFinite(maxX) || 
                !float.IsFinite(minY) || !float.IsFinite(maxY))
            {
                return (0, 1, 0, 1);
            }

            return (minX, maxX, minY, maxY);
        }

        private (int width, int height, float resolution) LimitRasterSize(
            int width, int height, float resolution, int maxSide)
        {
            if (width > maxSide || height > maxSide)
            {
                float scale = Math.Min(maxSide / (float)width, maxSide / (float)height);
                width = (int)(width * scale);
                height = (int)(height * scale);
                resolution /= scale;
                
                _service.Log($"Raster resized to {width}x{height}; new resolution={resolution:F4} m/px");
            }

            return (width, height, resolution);
        }

        private Vector3 SampleBestColor(Vector3 world, List<PhotogrammetryImage> images)
        {
            Vector3 best = new Vector3(0.5f, 0.5f, 0.5f);
            float bestScore = float.NegativeInfinity;

            foreach (var image in images)
            {
                var proj = ProjectWorldToImage(world, image);
                
                if (IsValidProjection(proj, image))
                {
                    // Score based on distance to camera
                    Matrix4x4.Invert(image.GlobalPose, out var view);
                    var cameraPos = new Vector3(view.M41, view.M42, view.M43);
                    float dist = Vector3.Distance(cameraPos, world);
                    float score = -dist;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = SampleImageColor(image, (int)proj.X, (int)proj.Y);
                    }
                }
            }

            return best;
        }

        private Vector3 SampleBlendedColor(Vector3 world, List<PhotogrammetryImage> images, int maxImages = 3)
        {
            var candidates = new List<(float score, PhotogrammetryImage img, Vector2 px)>();

            foreach (var image in images)
            {
                var proj = ProjectWorldToImage(world, image);
                
                if (IsValidProjection(proj, image))
                {
                    Matrix4x4.Invert(image.GlobalPose, out var view);
                    var cameraPos = new Vector3(view.M41, view.M42, view.M43);
                    float dist = Vector3.Distance(cameraPos, world);
                    float score = 1.0f / (1e-4f + dist);
                    
                    candidates.Add((score, image, proj));
                }
            }

            if (candidates.Count == 0)
                return new Vector3(0.5f, 0.5f, 0.5f);

            // Sort by score and take best N
            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            int take = Math.Min(maxImages, candidates.Count);

            Vector3 sum = Vector3.Zero;
            float weightSum = 0;

            for (int i = 0; i < take; i++)
            {
                var c = candidates[i];
                var color = SampleImageColor(c.img, (int)c.px.X, (int)c.px.Y);
                float weight = c.score;
                sum += color * weight;
                weightSum += weight;
            }

            return weightSum > 0 ? sum / weightSum : new Vector3(0.5f, 0.5f, 0.5f);
        }

        private void FillElevationHoles(float[,] elevations)
        {
            int height = elevations.GetLength(0);
            int width = elevations.GetLength(1);

            // Simple nearest-neighbor hole filling
            for (int pass = 0; pass < 3; pass++)
            {
                var copy = (float[,])elevations.Clone();
                
                for (int j = 0; j < height; j++)
                {
                    for (int i = 0; i < width; i++)
                    {
                        if (float.IsNaN(copy[j, i]))
                        {
                            float sum = 0;
                            int count = 0;

                            // Check 4-neighborhood
                            if (i > 0 && !float.IsNaN(copy[j, i - 1])) { sum += copy[j, i - 1]; count++; }
                            if (i < width - 1 && !float.IsNaN(copy[j, i + 1])) { sum += copy[j, i + 1]; count++; }
                            if (j > 0 && !float.IsNaN(copy[j - 1, i])) { sum += copy[j - 1, i]; count++; }
                            if (j < height - 1 && !float.IsNaN(copy[j + 1, i])) { sum += copy[j + 1, i]; count++; }

                            if (count > 0)
                                elevations[j, i] = sum / count;
                        }
                    }
                }
            }
        }

        private void SmoothElevations(float[,] elevations)
        {
            int height = elevations.GetLength(0);
            int width = elevations.GetLength(1);
            var smoothed = new float[height, width];

            // Apply 3x3 box filter
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    float sum = 0;
                    int count = 0;

                    for (int dj = -1; dj <= 1; dj++)
                    {
                        for (int di = -1; di <= 1; di++)
                        {
                            int y = Math.Clamp(j + dj, 0, height - 1);
                            int x = Math.Clamp(i + di, 0, width - 1);
                            
                            if (!float.IsNaN(elevations[y, x]))
                            {
                                sum += elevations[y, x];
                                count++;
                            }
                        }
                    }

                    smoothed[j, i] = count > 0 ? sum / count : elevations[j, i];
                }
            }

            // Copy back
            Buffer.BlockCopy(smoothed, 0, elevations, 0, sizeof(float) * width * height);
        }

        private void SaveRasterImage(byte[] rgba, int width, int height, string path)
        {
            try
            {
                using var image = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                System.Buffer.BlockCopy(rgba, 0, image.Bytes, 0, rgba.Length);
                
                using var data = SKImage.FromBitmap(image);
                using var fs = System.IO.File.Open(path, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                
                var format = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? SKEncodedImageFormat.Png
                    : SKEncodedImageFormat.Jpeg;
                
                data.Encode(format, 95).SaveTo(fs);
            }
            catch (Exception ex)
            {
                _service.Log($"Failed to save raster image: {ex.Message}");
                throw;
            }
        }

               private void SaveDEMMetadata(string demPath, float minZ, float maxZ)
        {
            var metadataPath = demPath + ".minmax.txt";
            System.IO.File.WriteAllText(metadataPath, $"{minZ} {maxZ}");
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

        private Vector3 SampleImageColor(PhotogrammetryImage image, int x, int y)
        {
            if (image.Dataset.ImageData == null)
                return new Vector3(0.5f, 0.5f, 0.5f);

            x = Math.Clamp(x, 0, image.Dataset.Width - 1);
            y = Math.Clamp(y, 0, image.Dataset.Height - 1);

            int idx = (y * image.Dataset.Width + x) * 4;
            if (idx + 2 >= image.Dataset.ImageData.Length)
                return new Vector3(0.5f, 0.5f, 0.5f);

            return new Vector3(
                image.Dataset.ImageData[idx] / 255.0f,
                image.Dataset.ImageData[idx + 1] / 255.0f,
                image.Dataset.ImageData[idx + 2] / 255.0f);
        }

        /// <summary>
        /// Base class for elevation sampling
        /// </summary>
        private abstract class ElevationSampler
        {
            public abstract bool TrySampleZ(float x, float y, out float z);
        }

        /// <summary>
        /// Samples elevation from a point cloud using KNN interpolation
        /// </summary>
        private class CloudElevationSampler : ElevationSampler
        {
            private readonly List<Vector3> _points;
            private readonly int _knnK = 8;

            public CloudElevationSampler(PhotogrammetryPointCloud cloud)
            {
                _points = cloud.Points.Select(p => p.Position).ToList();
            }

            public override bool TrySampleZ(float x, float y, out float z)
            {
                z = 0;

                if (_points.Count == 0)
                    return false;

                // Find K nearest neighbors in XY plane
                var nearest = _points
                    .Select(p => (point: p, distSq: (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)))
                    .OrderBy(t => t.distSq)
                    .Take(_knnK)
                    .ToList();

                if (nearest.Count == 0)
                    return false;

                // Inverse distance weighted interpolation
                float sumWeights = 0;
                float sumZ = 0;

                foreach (var (point, distSq) in nearest)
                {
                    float weight = 1.0f / (1e-6f + MathF.Sqrt(distSq));
                    sumWeights += weight;
                    sumZ += point.Z * weight;
                }

                if (sumWeights > 0)
                {
                    z = sumZ / sumWeights;
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Samples elevation from a mesh using ray-casting
        /// </summary>
        private class MeshElevationSampler : ElevationSampler
        {
            private readonly Data.Mesh3D.Mesh3DDataset _mesh;
            private readonly List<Triangle> _triangles;

            public MeshElevationSampler(Data.Mesh3D.Mesh3DDataset mesh)
            {
                _mesh = mesh;
                _triangles = BuildTriangleList();
            }

            private List<Triangle> BuildTriangleList()
            {
                var triangles = new List<Triangle>();

                foreach (var face in _mesh.Faces)
                {
                    if (face.Length >= 3)
                    {
                        triangles.Add(new Triangle(
                            _mesh.Vertices[face[0]],
                            _mesh.Vertices[face[1]],
                            _mesh.Vertices[face[2]]));
                    }
                }

                return triangles;
            }

            public override bool TrySampleZ(float x, float y, out float z)
            {
                z = 0;

                // Cast ray downward from above and find first intersection
                foreach (var tri in _triangles)
                {
                    if (tri.ContainsXY(x, y))
                    {
                        z = tri.InterpolateZ(x, y);
                        return true;
                    }
                }

                return false;
            }

            private struct Triangle
            {
                public Vector3 V0, V1, V2;
                private float _minX, _maxX, _minY, _maxY;

                public Triangle(Vector3 v0, Vector3 v1, Vector3 v2)
                {
                    V0 = v0; V1 = v1; V2 = v2;
                    _minX = Math.Min(v0.X, Math.Min(v1.X, v2.X));
                    _maxX = Math.Max(v0.X, Math.Max(v1.X, v2.X));
                    _minY = Math.Min(v0.Y, Math.Min(v1.Y, v2.Y));
                    _maxY = Math.Max(v0.Y, Math.Max(v1.Y, v2.Y));
                }

                public bool ContainsXY(float x, float y)
                {
                    // Quick AABB rejection
                    if (x < _minX || x > _maxX || y < _minY || y > _maxY)
                        return false;

                    // Barycentric coordinates test
                    float det = (V1.Y - V2.Y) * (V0.X - V2.X) + (V2.X - V1.X) * (V0.Y - V2.Y);
                    if (Math.Abs(det) < 1e-8f)
                        return false;

                    float w0 = ((V1.Y - V2.Y) * (x - V2.X) + (V2.X - V1.X) * (y - V2.Y)) / det;
                    float w1 = ((V2.Y - V0.Y) * (x - V2.X) + (V0.X - V2.X) * (y - V2.Y)) / det;
                    float w2 = 1 - w0 - w1;

                    return w0 >= 0 && w1 >= 0 && w2 >= 0;
                }

                public float InterpolateZ(float x, float y)
                {
                    float det = (V1.Y - V2.Y) * (V0.X - V2.X) + (V2.X - V1.X) * (V0.Y - V2.Y);
                    float w0 = ((V1.Y - V2.Y) * (x - V2.X) + (V2.X - V1.X) * (y - V2.Y)) / det;
                    float w1 = ((V2.Y - V0.Y) * (x - V2.X) + (V0.X - V2.X) * (y - V2.Y)) / det;
                    float w2 = 1 - w0 - w1;

                    return w0 * V0.Z + w1 * V1.Z + w2 * V2.Z;
                }
            }
        }
    }
}