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

            return await Task.Run(() =>
            {
                _service.Log($"Using {(sourceCloud.IsDense ? "dense" : "sparse")} cloud with {sourceCloud.Points.Count} points.");
                
                var vertices = sourceCloud.Points.Select(p => p.Position).ToList();
                var faces = GenerateMeshFaces(vertices, options.FaceCount);

                var mesh = Mesh3DDataset.CreateFromData(
                    "Photogrammetry_Mesh",
                    outputPath,
                    vertices,
                    faces,
                    1.0f,
                    "m");

                _service.Log($"Mesh generated with {mesh.VertexCount} vertices and {mesh.FaceCount} faces.");
                updateProgress(1.0f, "Mesh complete");
                
                return mesh;
            });
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

                var uvCoords = GenerateUVCoordinates(mesh);
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

            if (faces.Count < targetFaceCount / 10)
            {
                _service.Log("Ball-pivoting generated too few faces, using supplementary triangulation");
                faces.AddRange(GenerateSupplementaryFaces(vertices, faces, targetFaceCount));
            }

            _service.Log($"Generated {faces.Count} faces using ball-pivoting algorithm");
            return faces;
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
                return supplementary;

            // Generate simple triangles from unused vertices
            for (int i = 0; i < available.Count - 2 && supplementary.Count < targetCount - existingFaces.Count; i++)
            {
                for (int j = i + 1; j < available.Count - 1 && supplementary.Count < targetCount - existingFaces.Count; j++)
                {
                    for (int k = j + 1; k < available.Count && supplementary.Count < targetCount - existingFaces.Count; k++)
                    {
                        supplementary.Add(new[] { available[i], available[j], available[k] });
                    }
                }
            }

            return supplementary;
        }

        private List<Vector2> GenerateUVCoordinates(Mesh3DDataset mesh)
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

            // Apply color correction if requested
            if (options.ColorCorrection)
            {
                ApplyColorCorrection(resultBytes, options.TextureSize);
            }

            return resultBytes;
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

            var spatialIndex = BuildSpatialIndex(vertices, ballRadius);
            var used = new HashSet<int>();
            var edgeQueue = new Queue<(int v1, int v2)>();

            // Find seed triangle
            var seedTriangle = FindSeedTriangle(vertices, ballRadius);
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
                var pivotVertex = FindPivotVertex(v1, v2, vertices, used, ballRadius, spatialIndex);

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

            // Sample average distance
            int samples = Math.Min(100, vertices.Count / 2);
            float totalDistance = 0;
            var random = new Random();

            for (int i = 0; i < samples; i++)
            {
                int idx1 = random.Next(vertices.Count);
                int idx2 = random.Next(vertices.Count);
                
                if (idx1 != idx2)
                    totalDistance += Vector3.Distance(vertices[idx1], vertices[idx2]);
            }

            return totalDistance / samples / 10.0f;
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
            float ballRadius)
        {
            for (int i = 0; i < Math.Min(vertices.Count, 100); i++)
            {
                for (int j = i + 1; j < Math.Min(vertices.Count, 100); j++)
                {
                    if (Vector3.Distance(vertices[i], vertices[j]) < ballRadius * 2.0f)
                    {
                        for (int k = j + 1; k < Math.Min(vertices.Count, 100); k++)
                        {
                            if (Vector3.Distance(vertices[i], vertices[k]) < ballRadius * 2.0f &&
                                Vector3.Distance(vertices[j], vertices[k]) < ballRadius * 2.0f)
                            {
                                var cross = Vector3.Cross(
                                    vertices[j] - vertices[i],
                                    vertices[k] - vertices[i]);
                                
                                if (cross.Length() > 0.001f)
                                    return (i, j, k);
                            }
                        }
                    }
                }
            }

            return null;
        }

        private int FindPivotVertex(
            int v1, int v2,
            List<Vector3> vertices,
            HashSet<int> used,
            float ballRadius,
            Dictionary<(int, int, int), List<int>> spatialIndex)
        {
            Vector3 p1 = vertices[v1];
            Vector3 p2 = vertices[v2];
            var edgeCenter = (p1 + p2) * 0.5f;
            var edgeDir = Vector3.Normalize(p2 - p1);

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
                        
                        if (Vector3.Distance(candidate, p1) < ballRadius &&
                            Vector3.Distance(candidate, p2) < ballRadius)
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