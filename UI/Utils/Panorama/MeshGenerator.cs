// GeoscientistToolkit/Business/Photogrammetry/MeshGenerator.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.Photogrammetry;
using GeoscientistToolkit.Data.Mesh3D;
using SkiaSharp;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Handles mesh generation and texturing
    /// </summary>
    internal class MeshGenerator
    {
        private readonly PhotogrammetryProcessingService _service;

        public MeshGenerator(PhotogrammetryProcessingService service)
        {
            _service = service;
        }

        public async Task<Mesh3DDataset> BuildMeshAsync(
            PhotogrammetryPointCloud sourceCloud,
            MeshOptions options,
            string outputPath,
            Action<float, string> updateProgress)
        {
            _service.Log("Building 3D mesh from point cloud...");
            updateProgress(0, "Mesh generation in progress...");

            return await Task.Run(async () =>
            {
                try
                {
                    _service.Log($"Using {(sourceCloud.IsDense ? "dense" : "sparse")} cloud with {sourceCloud.Points.Count} points.");
                    
                    // Phase 1: Generate initial mesh (0-70%)
                    updateProgress(0, "Generating mesh from point cloud...");
                    var vertices = sourceCloud.Points.Select(p => p.Position).ToList();
                    var faces = GenerateMeshFaces(vertices, options.FaceCount);

                    var mesh = Mesh3DDataset.CreateFromData(
                        "Photogrammetry_Mesh",
                        outputPath,
                        vertices,
                        faces,
                        1.0f,
                        "m");

                    _service.Log($"Initial mesh: {mesh.VertexCount} vertices, {mesh.FaceCount} faces");
                    updateProgress(0.7f, "Initial mesh generated");

                    // Phase 2: Optimize mesh quality if enabled (70-100%)
                    if (options.OptimizeMesh)
                    {
                        try
                        {
                            _service.Log("Optimizing mesh quality...");
                            var optimizer = new MeshOptimizer(_service);
                            var optimizationOptions = GetOptimizationOptions(options.OptimizationQuality);
                            
                            var optimizedMesh = await optimizer.OptimizeMeshAsync(
                                mesh,
                                optimizationOptions,
                                (progress, status) =>
                                {
                                    // Map optimizer progress from 70% to 100%
                                    float overallProgress = 0.7f + (progress * 0.3f);
                                    updateProgress(overallProgress, status);
                                });

                            _service.Log($"Optimized mesh: {optimizedMesh.VertexCount} vertices, {optimizedMesh.FaceCount} faces");
                            mesh = optimizedMesh;
                        }
                        catch (Exception ex)
                        {
                            _service.Log($"Warning: Mesh optimization failed: {ex.Message}");
                            _service.Log("Continuing with unoptimized mesh.");
                            // Continue with unoptimized mesh
                        }
                    }

                    updateProgress(1.0f, "Mesh complete");
                    _service.Log($"Final mesh: {mesh.VertexCount} vertices, {mesh.FaceCount} faces");
                    
                    return mesh;
                }
                catch (Exception ex)
                {
                    _service.Log($"Error during mesh generation: {ex.Message}");
                    _service.Log($"Stack trace: {ex.StackTrace}");
                    throw;
                }
            });
        }

        /// <summary>
        /// Gets mesh optimization options based on quality profile
        /// </summary>
        private MeshOptimizationOptions GetOptimizationOptions(OptimizationQuality quality)
        {
            switch (quality)
            {
                case OptimizationQuality.Fast:
                    return new MeshOptimizationOptions
                    {
                        OptimizationPasses = 1,
                        MaxIterations = 5,
                        EnableEdgeSwapping = true,
                        EnableLaplacianSmoothing = true,
                        SmoothingIterations = 2,
                        LaplacianDampingFactor = 0.6f,
                        LaplacianWeightingScheme = LaplacianWeighting.Uniform,
                        EnableVertexOptimization = false,
                        EnableSliverRemoval = true,
                        SliverQualityThreshold = 0.05f
                    };

                case OptimizationQuality.Balanced:
                    return new MeshOptimizationOptions
                    {
                        OptimizationPasses = 2,
                        MaxIterations = 10,
                        EnableEdgeSwapping = true,
                        EdgeSwapQualityThreshold = 0.01f,
                        EnableLaplacianSmoothing = true,
                        SmoothingIterations = 5,
                        LaplacianDampingFactor = 0.5f,
                        LaplacianWeightingScheme = LaplacianWeighting.InverseDistance,
                        EnableVertexOptimization = true,
                        OptimizationStepSize = 0.1f,
                        OptimizationSubIterations = 5,
                        EnableSliverRemoval = true,
                        SliverQualityThreshold = 0.1f
                    };

                case OptimizationQuality.High:
                    return new MeshOptimizationOptions
                    {
                        OptimizationPasses = 3,
                        MaxIterations = 20,
                        EnableEdgeSwapping = true,
                        EdgeSwapQualityThreshold = 0.005f,
                        EnableLaplacianSmoothing = true,
                        SmoothingIterations = 10,
                        LaplacianDampingFactor = 0.3f,
                        LaplacianWeightingScheme = LaplacianWeighting.Cotangent,
                        EnableVertexOptimization = true,
                        OptimizationStepSize = 0.05f,
                        OptimizationSubIterations = 15,
                        EnableSliverRemoval = true,
                        SliverQualityThreshold = 0.15f
                    };

                default:
                    return new MeshOptimizationOptions(); // Default balanced
            }
        }

        public async Task BuildTextureAsync(
            Mesh3DDataset mesh,
            List<PhotogrammetryImage> images,
            TextureOptions options,
            string outputPath,
            Action<float, string> updateProgress)
        {
            _service.Log("Building texture for mesh...");
            updateProgress(0, "Texture generation in progress...");

            await Task.Run(() =>
            {
                _service.Log($"Generating texture atlas ({options.TextureSize}x{options.TextureSize})...");
                _service.Log($"UV parameterization method: {options.ParameterizationMethod}");

                var uvCoords = GenerateUVCoordinates(mesh, options);
                var textureAtlas = CreateTextureAtlas(
                    mesh, uvCoords, images, options, updateProgress);

                string texturePath = Path.ChangeExtension(outputPath, ".png");
                SaveTextureImage(textureAtlas, options.TextureSize, texturePath);

                mesh.TextureCoordinates = uvCoords;
                mesh.TexturePath = texturePath;
                mesh.Save();

                _service.Log($"Texture generated and saved to {texturePath}");
                updateProgress(1.0f, "Texture complete");
            });
        }

        private List<int[]> GenerateMeshFaces(List<Vector3> vertices, int targetFaceCount)
        {
            var faces = new List<int[]>();
            if (vertices.Count < 3)
                return faces;

            // Use ball-pivoting algorithm for mesh generation
            var triangulator = new BallPivotingTriangulator(_service);
            faces = triangulator.Triangulate(vertices, targetFaceCount);

            _service.Log($"Ball-pivoting generated {faces.Count} faces (target: {targetFaceCount})");

            // Validate and filter out degenerate faces
            var validFaces = FilterDegenerateFaces(faces, vertices);
            _service.Log($"After filtering: {validFaces.Count} valid faces (removed {faces.Count - validFaces.Count} degenerate)");

            if (validFaces.Count < Math.Min(targetFaceCount / 10, 100))
            {
                _service.Log("Too few valid faces, using supplementary Delaunay-like triangulation");
                validFaces.AddRange(GenerateSupplementaryFaces(vertices, validFaces, targetFaceCount));
            }

            _service.Log($"Final mesh: {validFaces.Count} faces");
            return validFaces;
        }

        /// <summary>
        /// Filters out degenerate triangles (zero area, extreme aspect ratios, etc.)
        /// </summary>
        private List<int[]> FilterDegenerateFaces(List<int[]> faces, List<Vector3> vertices)
        {
            var validFaces = new List<int[]>();
            const float MIN_AREA = 1e-6f;  // Minimum triangle area
            const float MAX_EDGE_RATIO = 100.0f;  // Maximum ratio between longest and shortest edge

            foreach (var face in faces)
            {
                if (face.Length < 3)
                    continue;

                var v0 = vertices[face[0]];
                var v1 = vertices[face[1]];
                var v2 = vertices[face[2]];

                // Compute edge lengths
                float e01 = Vector3.Distance(v0, v1);
                float e12 = Vector3.Distance(v1, v2);
                float e20 = Vector3.Distance(v2, v0);

                // Skip if any edge is too small
                if (e01 < 1e-6f || e12 < 1e-6f || e20 < 1e-6f)
                    continue;

                // Compute triangle area
                var cross = Vector3.Cross(v1 - v0, v2 - v0);
                float area = cross.Length() * 0.5f;

                // Skip zero-area triangles
                if (area < MIN_AREA)
                    continue;

                // Compute aspect ratio
                float maxEdge = Math.Max(e01, Math.Max(e12, e20));
                float minEdge = Math.Min(e01, Math.Min(e12, e20));
                float ratio = maxEdge / minEdge;

                // Skip slivers (very elongated triangles)
                if (ratio > MAX_EDGE_RATIO)
                    continue;

                validFaces.Add(face);
            }

            return validFaces;
        }

        private List<int[]> GenerateSupplementaryFaces(
            List<Vector3> vertices,
            List<int[]> existingFaces,
            int targetCount)
        {
            var supplementary = new List<int[]>();
            var used = new HashSet<int>();

            foreach (var face in existingFaces)
            {
                foreach (var vertex in face)
                    used.Add(vertex);
            }

            var available = Enumerable.Range(0, vertices.Count)
                .Where(i => !used.Contains(i))
                .ToList();

            if (available.Count < 3)
            {
                _service.Log($"  Cannot generate supplementary faces: only {available.Count} unused vertices");
                return supplementary;
            }

            _service.Log($"  Generating supplementary faces from {available.Count} unused vertices");

            // Use a more intelligent greedy approach: connect nearest neighbors
            // This creates a better mesh than brute force enumeration
            var addedEdges = new HashSet<(int, int)>();

            // Build a k-NN graph (k=6 nearest neighbors)
            int k = Math.Min(6, available.Count - 1);
            for (int i = 0; i < available.Count && supplementary.Count < targetCount - existingFaces.Count; i++)
            {
                var vi = available[i];
                var neighbors = available
                    .Where(vj => vj != vi)
                    .OrderBy(vj => Vector3.Distance(vertices[vi], vertices[vj]))
                    .Take(k)
                    .ToList();

                // Try to form triangles with nearest neighbors
                for (int j = 0; j < neighbors.Count - 1 && supplementary.Count < targetCount - existingFaces.Count; j++)
                {
                    for (int k_idx = j + 1; k_idx < neighbors.Count && supplementary.Count < targetCount - existingFaces.Count; k_idx++)
                    {
                        int vj = neighbors[j];
                        int vk = neighbors[k_idx];

                        // Check if this triangle would be valid
                        var v0 = vertices[vi];
                        var v1 = vertices[vj];
                        var v2 = vertices[vk];

                        // Compute triangle normal and area
                        var cross = Vector3.Cross(v1 - v0, v2 - v0);
                        float area = cross.Length() * 0.5f;

                        // Only add if it has reasonable area
                        if (area > 1e-5f)
                        {
                            // Check if we haven't already added a conflicting face
                            var edge01 = (Math.Min(vi, vj), Math.Max(vi, vj));
                            var edge12 = (Math.Min(vj, vk), Math.Max(vj, vk));
                            var edge20 = (Math.Min(vk, vi), Math.Max(vk, vi));

                            if (!addedEdges.Contains(edge01) || !addedEdges.Contains(edge12) || !addedEdges.Contains(edge20))
                            {
                                supplementary.Add(new[] { vi, vj, vk });
                                addedEdges.Add(edge01);
                                addedEdges.Add(edge12);
                                addedEdges.Add(edge20);
                            }
                        }
                    }
                }
            }

            _service.Log($"  Generated {supplementary.Count} supplementary faces");
            return supplementary;
        }

        private List<Vector2> GenerateUVCoordinates(Mesh3DDataset mesh, TextureOptions options)
        {
            switch (options.ParameterizationMethod)
            {
                case TextureOptions.UVMethod.Cylindrical:
                    return GenerateUVCylindrical(mesh);
                case TextureOptions.UVMethod.Spherical:
                    return GenerateUVSpherical(mesh);
                case TextureOptions.UVMethod.Conformal:
                    return GenerateUVConformal(mesh);
                case TextureOptions.UVMethod.BoxProjection:
                default:
                    return GenerateUVBoxProjection(mesh);
            }
        }

        /// <summary>
        /// Simple box projection - fast but higher distortion
        /// </summary>
        private List<Vector2> GenerateUVBoxProjection(Mesh3DDataset mesh)
        {
            var uvCoords = new List<Vector2>();
            var min = mesh.BoundingBoxMin;
            var max = mesh.BoundingBoxMax;
            var range = max - min;

            // Avoid division by zero
            if (range.X < 1e-6f) range.X = 1;
            if (range.Y < 1e-6f) range.Y = 1;
            if (range.Z < 1e-6f) range.Z = 1;

            // Simple box projection
            foreach (var vertex in mesh.Vertices)
            {
                var normalized = (vertex - min) / range;
                float u = normalized.X;
                float v = normalized.Y;
                uvCoords.Add(new Vector2(Math.Clamp(u, 0, 1), Math.Clamp(v, 0, 1)));
            }

            _service.Log($"Generated {uvCoords.Count} UV coordinates using box projection.");
            return uvCoords;
        }

        /// <summary>
        /// Cylindrical projection for cylindrical objects
        /// </summary>
        private List<Vector2> GenerateUVCylindrical(Mesh3DDataset mesh)
        {
            var uvCoords = new List<Vector2>();
            var center = (mesh.BoundingBoxMin + mesh.BoundingBoxMax) * 0.5f;
            var min = mesh.BoundingBoxMin;
            var max = mesh.BoundingBoxMax;
            float heightRange = max.Z - min.Z;
            if (heightRange < 1e-6f) heightRange = 1;

            foreach (var vertex in mesh.Vertices)
            {
                // Cylindrical coordinates
                var relative = vertex - center;
                float angle = MathF.Atan2(relative.Y, relative.X);
                float u = (angle + MathF.PI) / (2 * MathF.PI); // [0, 1]
                float v = (vertex.Z - min.Z) / heightRange; // [0, 1]
                
                uvCoords.Add(new Vector2(Math.Clamp(u, 0, 1), Math.Clamp(v, 0, 1)));
            }

            _service.Log($"Generated {uvCoords.Count} UV coordinates using cylindrical projection.");
            return uvCoords;
        }

        /// <summary>
        /// Spherical projection for sphere-like objects
        /// </summary>
        private List<Vector2> GenerateUVSpherical(Mesh3DDataset mesh)
        {
            var uvCoords = new List<Vector2>();
            var center = (mesh.BoundingBoxMin + mesh.BoundingBoxMax) * 0.5f;

            foreach (var vertex in mesh.Vertices)
            {
                var relative = Vector3.Normalize(vertex - center);
                
                // Spherical coordinates
                float theta = MathF.Atan2(relative.Y, relative.X); // Azimuth [-π, π]
                float phi = MathF.Asin(Math.Clamp(relative.Z, -1, 1)); // Elevation [-π/2, π/2]
                
                float u = (theta + MathF.PI) / (2 * MathF.PI); // [0, 1]
                float v = (phi + MathF.PI / 2) / MathF.PI; // [0, 1]
                
                uvCoords.Add(new Vector2(Math.Clamp(u, 0, 1), Math.Clamp(v, 0, 1)));
            }

            _service.Log($"Generated {uvCoords.Count} UV coordinates using spherical projection.");
            return uvCoords;
        }

        /// <summary>
        /// Conformal (angle-preserving) parameterization using simplified LSCM approach
        /// Reference: Lévy et al. (2002) - "Least Squares Conformal Maps for Automatic Texture Atlas Generation"
        /// ACM Transactions on Graphics (TOG), 21(3):362-371
        /// 
        /// Note: This is a simplified implementation. Full LSCM requires solving a sparse linear system.
        /// For production use, consider using established libraries like libigl or OpenMesh.
        /// </summary>
        private List<Vector2> GenerateUVConformal(Mesh3DDataset mesh)
        {
            _service.Log("Generating conformal UV parameterization (simplified LSCM)...");
            
            // For simplicity, we use an iterative relaxation approach
            // Full LSCM would solve: minimize ∫|∂u/∂x - ∂v/∂y|² + |∂u/∂y + ∂v/∂x|² dA
            
            var uvCoords = new List<Vector2>();
            int vertexCount = mesh.Vertices.Count;
            
            // Initialize with box projection
            var min = mesh.BoundingBoxMin;
            var max = mesh.BoundingBoxMax;
            var range = max - min;
            if (range.X < 1e-6f) range.X = 1;
            if (range.Y < 1e-6f) range.Y = 1;

            for (int i = 0; i < vertexCount; i++)
            {
                var normalized = (mesh.Vertices[i] - min) / range;
                uvCoords.Add(new Vector2(
                    Math.Clamp(normalized.X, 0, 1),
                    Math.Clamp(normalized.Y, 0, 1)));
            }

            // Build adjacency information
            var neighbors = new List<HashSet<int>>();
            for (int i = 0; i < vertexCount; i++)
                neighbors.Add(new HashSet<int>());

            foreach (var face in mesh.Faces)
            {
                for (int i = 0; i < face.Length; i++)
                {
                    int v1 = face[i];
                    int v2 = face[(i + 1) % face.Length];
                    neighbors[v1].Add(v2);
                    neighbors[v2].Add(v1);
                }
            }

            // Iterative relaxation to minimize angle distortion
            int iterations = 50;
            float lambda = 0.5f; // Relaxation factor
            
            for (int iter = 0; iter < iterations; iter++)
            {
                var newUVs = new List<Vector2>(uvCoords);
                
                for (int i = 0; i < vertexCount; i++)
                {
                    if (neighbors[i].Count == 0)
                        continue;

                    // Compute weighted average of neighbors (angle-based weighting)
                    Vector2 sum = Vector2.Zero;
                    float weightSum = 0;

                    foreach (var neighborIdx in neighbors[i])
                    {
                        Vector3 edge3D = mesh.Vertices[neighborIdx] - mesh.Vertices[i];
                        float edgeLength = edge3D.Length();
                        if (edgeLength < 1e-6f) continue;

                        // Weight by inverse edge length (conformal property)
                        float weight = 1.0f / edgeLength;
                        sum += uvCoords[neighborIdx] * weight;
                        weightSum += weight;
                    }

                    if (weightSum > 0)
                    {
                        Vector2 target = sum / weightSum;
                        newUVs[i] = Vector2.Lerp(uvCoords[i], target, lambda);
                    }
                }

                uvCoords = newUVs;
            }

            // Normalize to [0, 1] range
            float minU = float.MaxValue, maxU = float.MinValue;
            float minV = float.MaxValue, maxV = float.MinValue;

            foreach (var uv in uvCoords)
            {
                if (uv.X < minU) minU = uv.X;
                if (uv.X > maxU) maxU = uv.X;
                if (uv.Y < minV) minV = uv.Y;
                if (uv.Y > maxV) maxV = uv.Y;
            }

            float rangeU = maxU - minU;
            float rangeV = maxV - minV;
            if (rangeU < 1e-6f) rangeU = 1;
            if (rangeV < 1e-6f) rangeV = 1;

            for (int i = 0; i < uvCoords.Count; i++)
            {
                uvCoords[i] = new Vector2(
                    (uvCoords[i].X - minU) / rangeU,
                    (uvCoords[i].Y - minV) / rangeV);
            }

            _service.Log($"Generated {uvCoords.Count} UV coordinates using conformal parameterization.");
            return uvCoords;
        }

        private byte[] CreateTextureAtlas(
            Mesh3DDataset mesh,
            List<Vector2> uvCoords,
            List<PhotogrammetryImage> images,
            TextureOptions options,
            Action<float, string> updateProgress)
        {
            var texture = new float[options.TextureSize * options.TextureSize * 4];
            var weights = new float[options.TextureSize * options.TextureSize];

            for (int faceIdx = 0; faceIdx < mesh.Faces.Count; faceIdx++)
            {
                ProcessFaceTexturing(
                    mesh, faceIdx, uvCoords, images,
                    texture, weights, options);

                if (faceIdx % 100 == 0)
                {
                    updateProgress(
                        (float)faceIdx / mesh.Faces.Count,
                        $"Texturing... ({faceIdx}/{mesh.Faces.Count})");
                }
            }

            return FinalizeTexture(texture, weights, options);
        }

        private void ProcessFaceTexturing(
            Mesh3DDataset mesh,
            int faceIdx,
            List<Vector2> uvCoords,
            List<PhotogrammetryImage> images,
            float[] texture,
            float[] weights,
            TextureOptions options)
        {
            var face = mesh.Faces[faceIdx];
            if (face.Length < 3)
                return;

            Vector3 v0 = mesh.Vertices[face[0]];
            Vector3 v1 = mesh.Vertices[face[1]];
            Vector3 v2 = mesh.Vertices[face[2]];

            var normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
            var faceCenter = (v0 + v1 + v2) / 3.0f;

            // Find best image for this face
            var bestImage = FindBestImageForFace(faceCenter, normal, images);
            if (bestImage.image == null || bestImage.score < 0.1f)
                return;

            Vector2 uv0 = uvCoords[face[0]];
            Vector2 uv1 = uvCoords[face[1]];
            Vector2 uv2 = uvCoords[face[2]];

            RasterizeTriangle(
                texture, weights, options.TextureSize,
                uv0, uv1, uv2, v0, v1, v2,
                bestImage.image, bestImage.score, options.BlendTextures);
        }

        private (PhotogrammetryImage image, float score) FindBestImageForFace(
            Vector3 faceCenter,
            Vector3 faceNormal,
            List<PhotogrammetryImage> images)
        {
            PhotogrammetryImage bestImage = null;
            float bestScore = -1;

            foreach (var image in images)
            {
                Matrix4x4.Invert(image.GlobalPose, out var viewMatrix);
                var cameraForward = Vector3.Normalize(
                    new Vector3(viewMatrix.M31, viewMatrix.M32, viewMatrix.M33));
                
                float score = Math.Max(0, Vector3.Dot(faceNormal, -cameraForward));

                if (score > bestScore)
                {
                    bestScore = score;
                    bestImage = image;
                }
            }

            return (bestImage, bestScore);
        }

        private void RasterizeTriangle(
            float[] texture,
            float[] weights,
            int textureSize,
            Vector2 uv0, Vector2 uv1, Vector2 uv2,
            Vector3 v0, Vector3 v1, Vector3 v2,
            PhotogrammetryImage image,
            float blendWeight,
            bool blend)
        {
            // Convert UV coordinates to texture space
            int x0 = (int)(uv0.X * (textureSize - 1));
            int y0 = (int)(uv0.Y * (textureSize - 1));
            int x1 = (int)(uv1.X * (textureSize - 1));
            int y1 = (int)(uv1.Y * (textureSize - 1));
            int x2 = (int)(uv2.X * (textureSize - 1));
            int y2 = (int)(uv2.Y * (textureSize - 1));

            // Compute bounding box
            int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2)));
            int maxX = Math.Min(textureSize - 1, Math.Max(x0, Math.Max(x1, x2)));
            int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2)));
            int maxY = Math.Min(textureSize - 1, Math.Max(y0, Math.Max(y1, y2)));

            // Rasterize triangle
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var barycentric = ComputeBarycentricCoordinates(
                        x, y, x0, y0, x1, y1, x2, y2);

                    if (barycentric.w0 >= 0 && barycentric.w1 >= 0 && barycentric.w2 >= 0)
                    {
                        // Interpolate world position
                        var worldPos = v0 * barycentric.w0 + 
                                      v1 * barycentric.w1 + 
                                      v2 * barycentric.w2;

                        // Project to image and sample color
                        var color = SampleImageColor(worldPos, image);

                        int pixelIdx = (y * textureSize + x) * 4;
                        float finalWeight = blend ? blendWeight : 1.0f;

                        texture[pixelIdx] += color.X * 255 * finalWeight;
                        texture[pixelIdx + 1] += color.Y * 255 * finalWeight;
                        texture[pixelIdx + 2] += color.Z * 255 * finalWeight;
                        weights[y * textureSize + x] += finalWeight;
                    }
                }
            }
        }

        private (float w0, float w1, float w2) ComputeBarycentricCoordinates(
            int x, int y,
            int x0, int y0,
            int x1, int y1,
            int x2, int y2)
        {
            float denom = ((y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2));
            
            if (Math.Abs(denom) < 1e-6f)
                return (-1, -1, -1);

            float w0 = ((y1 - y2) * (x - x2) + (x2 - x1) * (y - y2)) / denom;
            float w1 = ((y2 - y0) * (x - x2) + (x0 - x2) * (y - y2)) / denom;
            float w2 = 1 - w0 - w1;

            return (w0, w1, w2);
        }

        private Vector3 SampleImageColor(Vector3 worldPos, PhotogrammetryImage image)
        {
            var imagePos = ProjectWorldToImage(worldPos, image);

            if (imagePos.X >= 0 && imagePos.X < image.Dataset.Width &&
                imagePos.Y >= 0 && imagePos.Y < image.Dataset.Height)
            {
                return SampleImagePixel(image.Dataset, (int)imagePos.X, (int)imagePos.Y);
            }

            return new Vector3(0.5f, 0.5f, 0.5f);
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

        private Vector3 SampleImagePixel(Data.Image.ImageDataset dataset, int x, int y)
        {
            if (dataset.ImageData == null)
                return new Vector3(0.5f, 0.5f, 0.5f);

            x = Math.Clamp(x, 0, dataset.Width - 1);
            y = Math.Clamp(y, 0, dataset.Height - 1);

            int idx = (y * dataset.Width + x) * 4;
            if (idx + 2 >= dataset.ImageData.Length)
                return new Vector3(0.5f, 0.5f, 0.5f);

            return new Vector3(
                dataset.ImageData[idx] / 255.0f,
                dataset.ImageData[idx + 1] / 255.0f,
                dataset.ImageData[idx + 2] / 255.0f);
        }

        private byte[] FinalizeTexture(
            float[] texture,
            float[] weights,
            TextureOptions options)
        {
            var resultBytes = new byte[options.TextureSize * options.TextureSize * 4];

            // Normalize by weights
            for (int i = 0; i < options.TextureSize * options.TextureSize; i++)
            {
                if (weights[i] > 0)
                {
                    resultBytes[i * 4] = (byte)Math.Clamp(texture[i * 4] / weights[i], 0, 255);
                    resultBytes[i * 4 + 1] = (byte)Math.Clamp(texture[i * 4 + 1] / weights[i], 0, 255);
                    resultBytes[i * 4 + 2] = (byte)Math.Clamp(texture[i * 4 + 2] / weights[i], 0, 255);
                    resultBytes[i * 4 + 3] = 255;
                }
            }

            // Apply seam hiding if requested
            if (options.HideSeams)
            {
                HideTextureSeams(resultBytes, weights, options.TextureSize);
            }

            // Apply color correction if requested
            if (options.ColorCorrection)
            {
                ApplyColorCorrection(resultBytes, options.TextureSize);
            }

            return resultBytes;
        }

        /// <summary>
        /// Hide texture seams by dilating valid pixels into empty regions
        /// Reference: Inzerillo et al. (2018) - "High Quality Texture Mapping Process Aimed at 
        /// the Optimization of 3D Structured Light Models"
        /// </summary>
        private void HideTextureSeams(byte[] texture, float[] weights, int textureSize)
        {
            _service.Log("Applying seam hiding...");

            // Find pixels near UV seams (empty pixels adjacent to valid ones)
            var needsFilling = new List<(int x, int y)>();

            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    int idx = y * textureSize + x;
                    
                    // Empty pixel
                    if (weights[idx] == 0)
                    {
                        // Check if adjacent to valid pixel
                        bool nearValid = false;
                        for (int dy = -1; dy <= 1 && !nearValid; dy++)
                        {
                            for (int dx = -1; dx <= 1 && !nearValid; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx >= 0 && nx < textureSize && ny >= 0 && ny < textureSize)
                                {
                                    if (weights[ny * textureSize + nx] > 0)
                                        nearValid = true;
                                }
                            }
                        }

                        if (nearValid)
                            needsFilling.Add((x, y));
                    }
                }
            }

            // Fill seam pixels with average of nearby valid pixels
            foreach (var (x, y) in needsFilling)
            {
                float r = 0, g = 0, b = 0;
                int count = 0;

                // Sample in a 3x3 neighborhood
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx >= 0 && nx < textureSize && ny >= 0 && ny < textureSize)
                        {
                            int nIdx = ny * textureSize + nx;
                            if (weights[nIdx] > 0)
                            {
                                r += texture[nIdx * 4];
                                g += texture[nIdx * 4 + 1];
                                b += texture[nIdx * 4 + 2];
                                count++;
                            }
                        }
                    }
                }

                if (count > 0)
                {
                    int idx = y * textureSize + x;
                    texture[idx * 4] = (byte)(r / count);
                    texture[idx * 4 + 1] = (byte)(g / count);
                    texture[idx * 4 + 2] = (byte)(b / count);
                    texture[idx * 4 + 3] = 255;
                }
            }

            _service.Log($"Seam hiding complete, filled {needsFilling.Count} pixels.");
        }

        private void ApplyColorCorrection(byte[] texture, int textureSize)
        {
            // Find min/max for each channel
            byte minR = 255, maxR = 0;
            byte minG = 255, maxG = 0;
            byte minB = 255, maxB = 0;

            for (int i = 0; i < textureSize * textureSize; i++)
            {
                minR = Math.Min(minR, texture[i * 4]);
                maxR = Math.Max(maxR, texture[i * 4]);
                minG = Math.Min(minG, texture[i * 4 + 1]);
                maxG = Math.Max(maxG, texture[i * 4 + 1]);
                minB = Math.Min(minB, texture[i * 4 + 2]);
                maxB = Math.Max(maxB, texture[i * 4 + 2]);
            }

            // Apply histogram stretching
            for (int i = 0; i < textureSize * textureSize; i++)
            {
                if (maxR > minR)
                    texture[i * 4] = (byte)(255 * (texture[i * 4] - minR) / (maxR - minR));
                if (maxG > minG)
                    texture[i * 4 + 1] = (byte)(255 * (texture[i * 4 + 1] - minG) / (maxG - minG));
                if (maxB > minB)
                    texture[i * 4 + 2] = (byte)(255 * (texture[i * 4 + 2] - minB) / (maxB - minB));
            }
        }

        private void SaveTextureImage(byte[] texture, int size, string path)
        {
            using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            System.Runtime.InteropServices.Marshal.Copy(texture, 0, bitmap.GetPixels(), texture.Length);

            using var image = SKImage.FromBitmap(bitmap);
            using var stream = File.Create(path);

            var format = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => SKEncodedImageFormat.Png,
                ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                _ => SKEncodedImageFormat.Png
            };

            image.Encode(format, 95).SaveTo(stream);
        }
    }

    /// <summary>
    /// Ball-pivoting algorithm for mesh triangulation
    /// </summary>
    internal class BallPivotingTriangulator
    {
        private readonly PhotogrammetryProcessingService _service;

        public BallPivotingTriangulator(PhotogrammetryProcessingService service)
        {
            _service = service;
        }

        public List<int[]> Triangulate(List<Vector3> vertices, int targetFaceCount)
        {
            var faces = new List<int[]>();
            
            float ballRadius = EstimateBallRadius(vertices);
            _service.Log($"Using ball radius: {ballRadius:F3} for mesh generation");

            // Maximum edge length should be limited to prevent large degenerate faces
            float maxEdgeLength = ballRadius * 3.0f;
            _service.Log($"Maximum edge length: {maxEdgeLength:F3}");

            var spatialIndex = BuildSpatialIndex(vertices, ballRadius);
            var used = new HashSet<int>();
            var edgeQueue = new Queue<(int v1, int v2)>();

            // Find seed triangle
            var seedTriangle = FindSeedTriangle(vertices, ballRadius, maxEdgeLength);
            if (seedTriangle.HasValue)
            {
                faces.Add(new[] { seedTriangle.Value.v1, seedTriangle.Value.v2, seedTriangle.Value.v3 });
                used.Add(seedTriangle.Value.v1);
                used.Add(seedTriangle.Value.v2);
                used.Add(seedTriangle.Value.v3);
                
                edgeQueue.Enqueue((seedTriangle.Value.v1, seedTriangle.Value.v2));
                edgeQueue.Enqueue((seedTriangle.Value.v2, seedTriangle.Value.v3));
                edgeQueue.Enqueue((seedTriangle.Value.v3, seedTriangle.Value.v1));
            }

            // Expand from edges
            while (edgeQueue.Count > 0 && faces.Count < targetFaceCount)
            {
                var (v1, v2) = edgeQueue.Dequeue();
                var pivotVertex = FindPivotVertex(v1, v2, vertices, used, ballRadius, maxEdgeLength, spatialIndex);

                if (pivotVertex >= 0)
                {
                    faces.Add(new[] { v1, v2, pivotVertex });
                    used.Add(pivotVertex);

                    if (!IsEdgeInMesh(v2, pivotVertex, faces))
                        edgeQueue.Enqueue((v2, pivotVertex));
                    if (!IsEdgeInMesh(pivotVertex, v1, faces))
                        edgeQueue.Enqueue((pivotVertex, v1));
                }
            }

            return faces;
        }

        private float EstimateBallRadius(List<Vector3> vertices)
        {
            if (vertices.Count < 2)
                return 1.0f;

            // Compute bounding box to understand scene scale
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var v in vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            var extent = max - min;
            float sceneSize = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));

            // Sample nearest neighbor distances more thoroughly
            int samples = Math.Min(200, vertices.Count);
            var nearestDistances = new List<float>();
            var random = new Random(42); // Use fixed seed for reproducibility

            for (int i = 0; i < samples; i++)
            {
                int idx = i < vertices.Count ? i : random.Next(vertices.Count);
                var point = vertices[idx];

                // Find k nearest neighbors (k=5) instead of just the nearest
                var distances = new List<float>();
                for (int j = 0; j < vertices.Count; j++)
                {
                    if (j != idx)
                    {
                        float dist = Vector3.Distance(point, vertices[j]);
                        distances.Add(dist);
                    }
                }

                if (distances.Count > 0)
                {
                    distances.Sort();
                    // Take median of 5 nearest neighbors for robustness
                    int k = Math.Min(5, distances.Count);
                    nearestDistances.Add(distances[k / 2]);
                }
            }

            if (nearestDistances.Count == 0)
                return sceneSize / 50.0f; // Fallback

            // Use median instead of average for robustness against outliers
            nearestDistances.Sort();
            float medianNearestDist = nearestDistances[nearestDistances.Count / 2];

            // Ball radius should be 2-3x the median nearest neighbor distance
            // IMPROVED: Use more conservative scaling
            float radius = medianNearestDist * 2.0f;

            // Clamp to reasonable bounds
            float minRadius = sceneSize / 500.0f;
            float maxRadius = sceneSize / 20.0f;
            radius = Math.Clamp(radius, minRadius, maxRadius);

            _service.Log($"Scene size: {sceneSize:F3}, Median nearest dist: {medianNearestDist:F5}, Ball radius: {radius:F5}");
            _service.Log($"Radius bounds: [{minRadius:F5}, {maxRadius:F5}]");

            return radius;
        }

        private Dictionary<(int, int, int), List<int>> BuildSpatialIndex(
            List<Vector3> vertices,
            float cellSize)
        {
            var index = new Dictionary<(int, int, int), List<int>>();

            for (int i = 0; i < vertices.Count; i++)
            {
                var cell = (
                    (int)(vertices[i].X / cellSize),
                    (int)(vertices[i].Y / cellSize),
                    (int)(vertices[i].Z / cellSize));

                if (!index.ContainsKey(cell))
                    index[cell] = new List<int>();
                
                index[cell].Add(i);
            }

            return index;
        }

        private (int v1, int v2, int v3)? FindSeedTriangle(
            List<Vector3> vertices,
            float ballRadius,
            float maxEdgeLength)
        {
            // Improved seed triangle finding: try to find a well-conditioned triangle
            // near the center of the point cloud
            var center = Vector3.Zero;
            foreach (var v in vertices)
                center += v;
            center /= vertices.Count;

            // Find vertices closest to center
            var centralIndices = vertices
                .Select((v, idx) => (idx, dist: Vector3.Distance(v, center)))
                .OrderBy(x => x.dist)
                .Take(Math.Min(200, vertices.Count))
                .Select(x => x.idx)
                .ToList();

            _service.Log($"  Searching for seed triangle among {centralIndices.Count} central vertices...");

            // Try to find a good quality triangle (not too flat, reasonable size)
            (int, int, int)? bestSeed = null;
            float bestQuality = 0;

            for (int ii = 0; ii < Math.Min(centralIndices.Count, 50); ii++)
            {
                int i = centralIndices[ii];
                for (int jj = ii + 1; jj < Math.Min(centralIndices.Count, 50); jj++)
                {
                    int j = centralIndices[jj];
                    float edge1 = Vector3.Distance(vertices[i], vertices[j]);

                    if (edge1 > 1e-6f && edge1 < maxEdgeLength && edge1 < ballRadius * 2.0f)
                    {
                        for (int kk = jj + 1; kk < Math.Min(centralIndices.Count, 50); kk++)
                        {
                            int k = centralIndices[kk];
                            float edge2 = Vector3.Distance(vertices[i], vertices[k]);
                            float edge3 = Vector3.Distance(vertices[j], vertices[k]);

                            if (edge2 > 1e-6f && edge3 > 1e-6f &&
                                edge2 < maxEdgeLength && edge3 < maxEdgeLength &&
                                edge2 < ballRadius * 2.0f && edge3 < ballRadius * 2.0f)
                            {
                                var cross = Vector3.Cross(
                                    vertices[j] - vertices[i],
                                    vertices[k] - vertices[i]);

                                float area = cross.Length() * 0.5f;

                                if (area > 1e-5f)
                                {
                                    // Compute triangle quality (aspect ratio)
                                    float maxEdge = Math.Max(edge1, Math.Max(edge2, edge3));
                                    float minEdge = Math.Min(edge1, Math.Min(edge2, edge3));
                                    float quality = minEdge / maxEdge; // Higher is better (equilateral = 1.0)

                                    if (quality > bestQuality)
                                    {
                                        bestQuality = quality;
                                        bestSeed = (i, j, k);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (bestSeed.HasValue)
            {
                _service.Log($"  Found seed triangle with quality {bestQuality:F3}");
                return bestSeed;
            }

            _service.Log($"  [Warning] No suitable seed triangle found");
            return null;
        }

        private int FindPivotVertex(
            int v1, int v2,
            List<Vector3> vertices,
            HashSet<int> used,
            float ballRadius,
            float maxEdgeLength,
            Dictionary<(int, int, int), List<int>> spatialIndex)
        {
            Vector3 p1 = vertices[v1];
            Vector3 p2 = vertices[v2];
            var edgeCenter = (p1 + p2) * 0.5f;
            var edgeDir = Vector3.Normalize(p2 - p1);
            
            // Check edge length - if too long, don't expand from it
            if (Vector3.Distance(p1, p2) > maxEdgeLength)
                return -1;

            var cell = (
                (int)(edgeCenter.X / ballRadius),
                (int)(edgeCenter.Y / ballRadius),
                (int)(edgeCenter.Z / ballRadius));

            float bestAngle = float.MaxValue;
            int bestVertex = -1;

            // Search neighboring cells
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var checkCell = (cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz);
                
                if (spatialIndex.TryGetValue(checkCell, out var candidates))
                {
                    foreach (var candidateIdx in candidates)
                    {
                        if (candidateIdx == v1 || candidateIdx == v2 || used.Contains(candidateIdx))
                            continue;

                        var candidate = vertices[candidateIdx];
                        
                        float dist1 = Vector3.Distance(candidate, p1);
                        float dist2 = Vector3.Distance(candidate, p2);
                        
                        // Check all edges of potential triangle
                        if (dist1 < ballRadius && dist2 < ballRadius &&
                            dist1 < maxEdgeLength && dist2 < maxEdgeLength)
                        {
                            float angle = Math.Abs(Vector3.Dot(
                                Vector3.Normalize(candidate - edgeCenter), edgeDir));
                            
                            if (angle < bestAngle)
                            {
                                bestAngle = angle;
                                bestVertex = candidateIdx;
                            }
                        }
                    }
                }
            }

            return bestVertex;
        }

        private bool IsEdgeInMesh(int v1, int v2, List<int[]> faces)
        {
            foreach (var face in faces)
            {
                for (int i = 0; i < face.Length; i++)
                {
                    int next = (i + 1) % face.Length;
                    if ((face[i] == v1 && face[next] == v2) ||
                        (face[i] == v2 && face[next] == v1))
                        return true;
                }
            }
            return false;
        }
    }
}