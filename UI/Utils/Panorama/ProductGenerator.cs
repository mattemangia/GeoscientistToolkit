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
            bool useAdvancedBlending = options.Blending == OrthomosaicOptions.BlendingMode.AngleWeighted ||
                                        options.Blending == OrthomosaicOptions.BlendingMode.Feathered;
            
            for (int j = 0; j < height; j++)
            {
                float y = bounds.minY + j * gsd + gsd * 0.5f;
                
                for (int i = 0; i < width; i++)
                {
                    float x = bounds.minX + i * gsd + gsd * 0.5f;
                    
                    if (elevationSampler.TrySampleZ(x, y, out float z))
                    {
                        var world = new Vector3(x, y, z);
                        Vector3 color;

                        if (useAdvancedBlending && options.EnableBlending)
                        {
                            // Estimate surface normal for angle-aware blending
                            var normal = EstimateSurfaceNormal(x, y, elevationSampler, gsd);
                            color = SampleBlendedColorAdvanced(world, normal, images, options);
                        }
                        else if (options.EnableBlending)
                        {
                            color = SampleBlendedColor(world, images, options.MaxBlendImages);
                        }
                        else
                        {
                            color = SampleBestColor(world, images);
                        }

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
                FillElevationHoles(elevations, options.HoleFillMethod);
            
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

        /// <summary>
        /// Advanced blending with angle awareness and feathering
        /// Reference: Lin et al. (2016) - Blending zone determination for aerial orthoimages
        /// </summary>
        private Vector3 SampleBlendedColorAdvanced(
            Vector3 world,
            Vector3 normal,
            List<PhotogrammetryImage> images,
            OrthomosaicOptions options)
        {
            var candidates = new List<(float score, PhotogrammetryImage img, Vector2 px, float angle)>();

            foreach (var image in images)
            {
                var proj = ProjectWorldToImage(world, image);
                
                if (IsValidProjection(proj, image))
                {
                    Matrix4x4.Invert(image.GlobalPose, out var view);
                    var cameraPos = new Vector3(view.M41, view.M42, view.M43);
                    var viewDir = Vector3.Normalize(world - cameraPos);
                    
                    // Angle between surface normal and viewing direction
                    float angle = Vector3.Dot(normal, -viewDir);
                    if (angle < 0) angle = 0; // Back-facing

                    float dist = Vector3.Distance(cameraPos, world);
                    
                    float score = 0;
                    switch (options.Blending)
                    {
                        case OrthomosaicOptions.BlendingMode.AngleWeighted:
                            // Combine distance and angle: prefer nadir views
                            score = angle / (1e-4f + dist);
                            break;
                        case OrthomosaicOptions.BlendingMode.Feathered:
                            // Feathered: strong preference for nadir, smooth falloff
                            score = MathF.Pow(angle, 2.0f) / (1e-4f + dist);
                            break;
                        case OrthomosaicOptions.BlendingMode.DistanceWeighted:
                        default:
                            score = 1.0f / (1e-4f + dist);
                            break;
                    }
                    
                    candidates.Add((score, image, proj, angle));
                }
            }

            if (candidates.Count == 0)
                return new Vector3(0.5f, 0.5f, 0.5f);

            // For "Best" mode, just return the single best image
            if (options.Blending == OrthomosaicOptions.BlendingMode.Best || !options.EnableBlending)
            {
                var best = candidates.OrderByDescending(c => c.score).First();
                return SampleImageColor(best.img, (int)best.px.X, (int)best.px.Y);
            }

            // Sort by score and take best N
            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            int take = Math.Min(options.MaxBlendImages, candidates.Count);

            Vector3 sum = Vector3.Zero;
            float weightSum = 0;

            for (int i = 0; i < take; i++)
            {
                var c = candidates[i];
                var color = SampleImageColor(c.img, (int)c.px.X, (int)c.px.Y);
                
                // Apply feathering for smooth transitions
                float weight = c.score;
                if (options.Blending == OrthomosaicOptions.BlendingMode.Feathered)
                {
                    // Additional feathering based on rank
                    float rankWeight = 1.0f - (i / (float)take) * 0.5f;
                    weight *= rankWeight;
                }

                sum += color * weight;
                weightSum += weight;
            }

            return weightSum > 0 ? sum / weightSum : new Vector3(0.5f, 0.5f, 0.5f);
        }

        /// <summary>
        /// Estimate surface normal at a point using neighboring elevation samples
        /// </summary>
        private Vector3 EstimateSurfaceNormal(float x, float y, ElevationSampler sampler, float gridSize = 1.0f)
        {
            // Sample neighbors to estimate gradient
            bool hasLeft = sampler.TrySampleZ(x - gridSize, y, out float zLeft);
            bool hasRight = sampler.TrySampleZ(x + gridSize, y, out float zRight);
            bool hasDown = sampler.TrySampleZ(x, y - gridSize, out float zDown);
            bool hasUp = sampler.TrySampleZ(x, y + gridSize, out float zUp);
            
            if (!sampler.TrySampleZ(x, y, out float zCenter))
                return new Vector3(0, 0, 1); // Default upward

            // Compute gradients
            float dx = 0, dy = 0;
            int count = 0;

            if (hasLeft && hasRight)
            {
                dx = (zRight - zLeft) / (2 * gridSize);
                count++;
            }
            else if (hasRight)
            {
                dx = (zRight - zCenter) / gridSize;
                count++;
            }
            else if (hasLeft)
            {
                dx = (zCenter - zLeft) / gridSize;
                count++;
            }

            if (hasUp && hasDown)
            {
                dy = (zUp - zDown) / (2 * gridSize);
                count++;
            }
            else if (hasUp)
            {
                dy = (zUp - zCenter) / gridSize;
                count++;
            }
            else if (hasDown)
            {
                dy = (zCenter - zDown) / gridSize;
                count++;
            }

            if (count == 0)
                return new Vector3(0, 0, 1);

            // Normal from cross product of tangent vectors
            Vector3 tangentX = new Vector3(1, 0, dx);
            Vector3 tangentY = new Vector3(0, 1, dy);
            Vector3 normal = Vector3.Cross(tangentX, tangentY);
            
            return Vector3.Normalize(normal);
        }

        /// <summary>
        /// Fills elevation holes using selected interpolation method
        /// Primary method: IDW - Reference: Shepard, D. (1968). "A two-dimensional interpolation function for irregularly-spaced data"
        /// Alternative methods: Bilinear interpolation, Priority-based propagation
        /// </summary>
        private void FillElevationHoles(float[,] elevations, DEMOptions.InterpolationMethod method = DEMOptions.InterpolationMethod.IDW)
        {
            int height = elevations.GetLength(0);
            int width = elevations.GetLength(1);

            switch (method)
            {
                case DEMOptions.InterpolationMethod.Bilinear:
                    FillHolesBilinear(elevations);
                    break;
                case DEMOptions.InterpolationMethod.PriorityQueue:
                    FillHolesPriorityQueue(elevations);
                    break;
                case DEMOptions.InterpolationMethod.IDW:
                default:
                    FillHolesIDW(elevations);
                    break;
            }
        }

        /// <summary>
        /// Fill holes using Inverse Distance Weighting
        /// </summary>
        private void FillHolesIDW(float[,] elevations)
        {
            int height = elevations.GetLength(0);
            int width = elevations.GetLength(1);
            int searchRadius = 8;
            float power = 2.0f;
            const float minDistance = 0.1f;

            // Multi-pass filling for large holes
            for (int pass = 0; pass < 5; pass++)
            {
                var copy = (float[,])elevations.Clone();
                int filledCount = 0;
                
                for (int j = 0; j < height; j++)
                {
                    for (int i = 0; i < width; i++)
                    {
                        if (!float.IsNaN(copy[j, i]))
                            continue;

                        // Collect valid neighbors within search radius using IDW
                        float weightedSum = 0;
                        float totalWeight = 0;
                        bool foundNeighbors = false;

                        for (int dj = -searchRadius; dj <= searchRadius; dj++)
                        {
                            for (int di = -searchRadius; di <= searchRadius; di++)
                            {
                                if (dj == 0 && di == 0) continue;
                                
                                int y = j + dj;
                                int x = i + di;

                                if (x < 0 || x >= width || y < 0 || y >= height)
                                    continue;

                                float value = copy[y, x];
                                if (float.IsNaN(value))
                                    continue;

                                float distance = MathF.Sqrt(di * di + dj * dj);
                                if (distance < minDistance)
                                    distance = minDistance;

                                float weight = 1.0f / MathF.Pow(distance, power);
                                
                                weightedSum += value * weight;
                                totalWeight += weight;
                                foundNeighbors = true;
                            }
                        }

                        if (foundNeighbors && totalWeight > 0)
                        {
                            elevations[j, i] = weightedSum / totalWeight;
                            filledCount++;
                        }
                    }
                }

                _service.Log($"DEM hole filling (IDW) pass {pass + 1}: filled {filledCount} pixels");
                
                if (filledCount == 0)
                    break;
            }
        }

        /// <summary>
        /// Fill holes using bilinear interpolation for smooth transitions
        /// </summary>
        private void FillHolesBilinear(float[,] elevations)
        {
            int height = elevations.GetLength(0);
            int width = elevations.GetLength(1);

            for (int pass = 0; pass < 5; pass++)
            {
                var copy = (float[,])elevations.Clone();
                int filledCount = 0;

                for (int j = 1; j < height - 1; j++)
                {
                    for (int i = 1; i < width - 1; i++)
                    {
                        if (!float.IsNaN(copy[j, i]))
                            continue;

                        // Check 4-connected neighbors
                        float sum = 0;
                        int count = 0;

                        if (!float.IsNaN(copy[j - 1, i])) { sum += copy[j - 1, i]; count++; }
                        if (!float.IsNaN(copy[j + 1, i])) { sum += copy[j + 1, i]; count++; }
                        if (!float.IsNaN(copy[j, i - 1])) { sum += copy[j, i - 1]; count++; }
                        if (!float.IsNaN(copy[j, i + 1])) { sum += copy[j, i + 1]; count++; }

                        if (count >= 2)
                        {
                            elevations[j, i] = sum / count;
                            filledCount++;
                        }
                    }
                }

                _service.Log($"DEM hole filling (Bilinear) pass {pass + 1}: filled {filledCount} pixels");
                
                if (filledCount == 0)
                    break;
            }
        }

        /// <summary>
        /// Priority queue-based hole filling for feature preservation
        /// Reference: Criminisi et al. (2004) - Region filling and object removal by exemplar-based inpainting
        /// </summary>
        private void FillHolesPriorityQueue(float[,] elevations)
        {
            int height = elevations.GetLength(0);
            int width = elevations.GetLength(1);

            // Find boundary pixels between valid and invalid regions
            var boundary = new System.Collections.Generic.PriorityQueue<(int x, int y), float>();

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    if (float.IsNaN(elevations[j, i]))
                    {
                        // Check if adjacent to valid pixel
                        bool isBoundary = false;
                        for (int dy = -1; dy <= 1 && !isBoundary; dy++)
                        {
                            for (int dx = -1; dx <= 1 && !isBoundary; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = i + dx;
                                int ny = j + dy;
                                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                {
                                    if (!float.IsNaN(elevations[ny, nx]))
                                        isBoundary = true;
                                }
                            }
                        }

                        if (isBoundary)
                        {
                            // Priority based on number of valid neighbors
                            int validCount = 0;
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    if (dx == 0 && dy == 0) continue;
                                    int nx = i + dx;
                                    int ny = j + dy;
                                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                    {
                                        if (!float.IsNaN(elevations[ny, nx]))
                                            validCount++;
                                    }
                                }
                            }
                            boundary.Enqueue((i, j), -validCount); // Negative for max-heap behavior
                        }
                    }
                }
            }

            int filled = 0;
            while (boundary.Count > 0)
            {
                var (x, y) = boundary.Dequeue();

                if (!float.IsNaN(elevations[y, x]))
                    continue;

                // Interpolate from valid neighbors
                float sum = 0;
                float weightSum = 0;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            if (!float.IsNaN(elevations[ny, nx]))
                            {
                                float dist = MathF.Sqrt(dx * dx + dy * dy);
                                float weight = 1.0f / dist;
                                sum += elevations[ny, nx] * weight;
                                weightSum += weight;
                            }
                        }
                    }
                }

                if (weightSum > 0)
                {
                    elevations[y, x] = sum / weightSum;
                    filled++;

                    // Add newly exposed boundary pixels
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                if (float.IsNaN(elevations[ny, nx]))
                                {
                                    // Count valid neighbors for priority
                                    int validCount = 0;
                                    for (int dy2 = -1; dy2 <= 1; dy2++)
                                    {
                                        for (int dx2 = -1; dx2 <= 1; dx2++)
                                        {
                                            int nx2 = nx + dx2;
                                            int ny2 = ny + dy2;
                                            if (nx2 >= 0 && nx2 < width && ny2 >= 0 && ny2 < height)
                                            {
                                                if (!float.IsNaN(elevations[ny2, nx2]))
                                                    validCount++;
                                            }
                                        }
                                    }
                                    if (validCount > 0)
                                        boundary.Enqueue((nx, ny), -validCount);
                                }
                            }
                        }
                    }
                }
            }

            _service.Log($"DEM hole filling (Priority Queue): filled {filled} pixels");
        }

        /// <summary>
        /// Smooths elevations using Gaussian filter to avoid terracing artifacts
        /// Reference: Kraus, K., & Pfeifer, N. (1998). "Determination of terrain models in wooded areas with airborne laser scanner data"
        /// ISPRS Journal of Photogrammetry and Remote Sensing, 53, 193-203
        /// </summary>
        private void SmoothElevations(float[,] elevations)
        {
            int height = elevations.GetLength(0);
            int width = elevations.GetLength(1);
            var smoothed = new float[height, width];

            // 5x5 Gaussian kernel (sigma = 1.0)
            float[,] kernel = new float[,]
            {
                { 1, 4, 7, 4, 1 },
                { 4, 16, 26, 16, 4 },
                { 7, 26, 41, 26, 7 },
                { 4, 16, 26, 16, 4 },
                { 1, 4, 7, 4, 1 }
            };
            float kernelSum = 273.0f; // Sum of all kernel weights

            // Apply Gaussian filter
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    float weightedSum = 0;
                    float totalWeight = 0;

                    for (int ky = -2; ky <= 2; ky++)
                    {
                        for (int kx = -2; kx <= 2; kx++)
                        {
                            int y = Math.Clamp(j + ky, 0, height - 1);
                            int x = Math.Clamp(i + kx, 0, width - 1);
                            
                            if (!float.IsNaN(elevations[y, x]))
                            {
                                float weight = kernel[ky + 2, kx + 2];
                                weightedSum += elevations[y, x] * weight;
                                totalWeight += weight;
                            }
                        }
                    }

                    smoothed[j, i] = totalWeight > 0 ? weightedSum / totalWeight : elevations[j, i];
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