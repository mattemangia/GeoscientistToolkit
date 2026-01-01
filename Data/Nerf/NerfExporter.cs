// GeoscientistToolkit/Data/Nerf/NerfExporter.cs

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// Export functionality for NeRF datasets including mesh, texture, and point cloud export.
/// </summary>
public class NerfExporter
{
    private readonly NerfDataset _dataset;
    private readonly NerfModelData _model;

    public NerfExporter(NerfDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _model = dataset.ModelData;
    }

    #region Point Cloud Export

    /// <summary>
    /// Export point cloud by sampling the NeRF density field.
    /// </summary>
    public async Task ExportPointCloudAsync(string outputPath, PointCloudExportSettings settings, IProgress<float> progress = null)
    {
        if (_model == null)
        {
            Logger.LogError("No trained model available for export");
            return;
        }

        var extension = Path.GetExtension(outputPath).ToLower();
        var points = await SamplePointCloudAsync(settings, progress);

        Logger.Log($"Sampled {points.Count} points from NeRF");

        switch (extension)
        {
            case ".ply":
                ExportPointCloudPLY(outputPath, points);
                break;
            case ".xyz":
                ExportPointCloudXYZ(outputPath, points);
                break;
            case ".obj":
                ExportPointCloudOBJ(outputPath, points);
                break;
            default:
                Logger.LogWarning($"Unsupported format: {extension}, defaulting to PLY");
                ExportPointCloudPLY(Path.ChangeExtension(outputPath, ".ply"), points);
                break;
        }
    }

    private async Task<List<ColoredPoint>> SamplePointCloudAsync(PointCloudExportSettings settings, IProgress<float> progress)
    {
        var points = new List<ColoredPoint>();
        var bounds = _dataset.ImageCollection;

        Vector3 min = bounds?.BoundingBoxMin ?? -Vector3.One;
        Vector3 max = bounds?.BoundingBoxMax ?? Vector3.One;

        // Expand bounds slightly
        var center = (min + max) * 0.5f;
        var extent = (max - min) * 0.5f * settings.BoundsScale;
        min = center - extent;
        max = center + extent;

        float stepX = (max.X - min.X) / settings.ResolutionX;
        float stepY = (max.Y - min.Y) / settings.ResolutionY;
        float stepZ = (max.Z - min.Z) / settings.ResolutionZ;

        int totalSamples = settings.ResolutionX * settings.ResolutionY * settings.ResolutionZ;
        int sampledCount = 0;

        await Task.Run(() =>
        {
            var rng = new Random(42);

            for (int x = 0; x < settings.ResolutionX; x++)
            {
                for (int y = 0; y < settings.ResolutionY; y++)
                {
                    for (int z = 0; z < settings.ResolutionZ; z++)
                    {
                        // Add jitter for better coverage
                        float jitterX = settings.UseJitter ? (float)(rng.NextDouble() - 0.5) * stepX : 0;
                        float jitterY = settings.UseJitter ? (float)(rng.NextDouble() - 0.5) * stepY : 0;
                        float jitterZ = settings.UseJitter ? (float)(rng.NextDouble() - 0.5) * stepZ : 0;

                        var position = new Vector3(
                            min.X + (x + 0.5f) * stepX + jitterX,
                            min.Y + (y + 0.5f) * stepY + jitterY,
                            min.Z + (z + 0.5f) * stepZ + jitterZ
                        );

                        // Sample random view direction for color
                        var viewDir = Vector3.Normalize(new Vector3(
                            (float)(rng.NextDouble() * 2 - 1),
                            (float)(rng.NextDouble() * 2 - 1),
                            (float)(rng.NextDouble() * 2 - 1)
                        ));

                        var (density, color) = _model.Query(position, viewDir);

                        if (density >= settings.DensityThreshold)
                        {
                            points.Add(new ColoredPoint
                            {
                                Position = position,
                                Color = color,
                                Density = density
                            });
                        }

                        sampledCount++;
                        if (sampledCount % 10000 == 0)
                        {
                            progress?.Report((float)sampledCount / totalSamples);
                        }
                    }
                }
            }
        });

        progress?.Report(1.0f);
        return points;
    }

    private void ExportPointCloudPLY(string path, List<ColoredPoint> points)
    {
        using var writer = new StreamWriter(path);

        // PLY header
        writer.WriteLine("ply");
        writer.WriteLine("format ascii 1.0");
        writer.WriteLine($"element vertex {points.Count}");
        writer.WriteLine("property float x");
        writer.WriteLine("property float y");
        writer.WriteLine("property float z");
        writer.WriteLine("property uchar red");
        writer.WriteLine("property uchar green");
        writer.WriteLine("property uchar blue");
        writer.WriteLine("end_header");

        // Vertex data
        foreach (var p in points)
        {
            int r = (int)Math.Clamp(p.Color.X * 255, 0, 255);
            int g = (int)Math.Clamp(p.Color.Y * 255, 0, 255);
            int b = (int)Math.Clamp(p.Color.Z * 255, 0, 255);
            writer.WriteLine($"{p.Position.X:F6} {p.Position.Y:F6} {p.Position.Z:F6} {r} {g} {b}");
        }

        Logger.Log($"Exported point cloud to: {path}");
    }

    private void ExportPointCloudXYZ(string path, List<ColoredPoint> points)
    {
        using var writer = new StreamWriter(path);

        foreach (var p in points)
        {
            int r = (int)Math.Clamp(p.Color.X * 255, 0, 255);
            int g = (int)Math.Clamp(p.Color.Y * 255, 0, 255);
            int b = (int)Math.Clamp(p.Color.Z * 255, 0, 255);
            writer.WriteLine($"{p.Position.X:F6} {p.Position.Y:F6} {p.Position.Z:F6} {r} {g} {b}");
        }

        Logger.Log($"Exported point cloud to: {path}");
    }

    private void ExportPointCloudOBJ(string path, List<ColoredPoint> points)
    {
        using var writer = new StreamWriter(path);

        writer.WriteLine("# NeRF Point Cloud Export");
        writer.WriteLine($"# {points.Count} points");

        foreach (var p in points)
        {
            writer.WriteLine($"v {p.Position.X:F6} {p.Position.Y:F6} {p.Position.Z:F6} {p.Color.X:F4} {p.Color.Y:F4} {p.Color.Z:F4}");
        }

        Logger.Log($"Exported point cloud to: {path}");
    }

    #endregion

    #region Mesh Export

    /// <summary>
    /// Export mesh using Marching Cubes algorithm on the NeRF density field.
    /// </summary>
    public async Task ExportMeshAsync(string outputPath, MeshExportSettings settings, IProgress<float> progress = null)
    {
        if (_model == null)
        {
            Logger.LogError("No trained model available for export");
            return;
        }

        Logger.Log($"Extracting mesh with Marching Cubes at resolution {settings.Resolution}...");

        // Sample density field
        progress?.Report(0.1f);
        var densityGrid = await SampleDensityGridAsync(settings, new Progress<float>(p => progress?.Report(0.1f + p * 0.4f)));

        // Run Marching Cubes
        progress?.Report(0.5f);
        var (vertices, triangles, colors) = MarchingCubes(densityGrid, settings);

        Logger.Log($"Generated mesh with {vertices.Count} vertices and {triangles.Count} triangles");

        if (vertices.Count == 0)
        {
            Logger.LogWarning("No mesh generated - try lowering the density threshold");
            return;
        }

        // Optionally bake texture
        byte[] textureData = null;
        int textureWidth = 0, textureHeight = 0;
        string texturePath = null;

        if (settings.BakeTexture && settings.TextureResolution > 0)
        {
            progress?.Report(0.7f);
            (textureData, textureWidth, textureHeight) = await BakeTextureAsync(vertices, triangles, settings);
            texturePath = Path.ChangeExtension(outputPath, ".png");
        }

        // Export
        progress?.Report(0.9f);
        var extension = Path.GetExtension(outputPath).ToLower();

        switch (extension)
        {
            case ".obj":
                ExportMeshOBJ(outputPath, vertices, triangles, colors, settings.BakeTexture ? texturePath : null);
                break;
            case ".ply":
                ExportMeshPLY(outputPath, vertices, triangles, colors);
                break;
            case ".stl":
                ExportMeshSTL(outputPath, vertices, triangles);
                break;
            default:
                Logger.LogWarning($"Unsupported format: {extension}, defaulting to OBJ");
                outputPath = Path.ChangeExtension(outputPath, ".obj");
                ExportMeshOBJ(outputPath, vertices, triangles, colors, settings.BakeTexture ? texturePath : null);
                break;
        }

        // Save texture if baked
        if (textureData != null && !string.IsNullOrEmpty(texturePath))
        {
            SaveTexture(texturePath, textureData, textureWidth, textureHeight);
        }

        progress?.Report(1.0f);
    }

    private async Task<float[,,]> SampleDensityGridAsync(MeshExportSettings settings, IProgress<float> progress)
    {
        var bounds = _dataset.ImageCollection;
        Vector3 min = bounds?.BoundingBoxMin ?? -Vector3.One;
        Vector3 max = bounds?.BoundingBoxMax ?? Vector3.One;

        // Expand bounds slightly
        var center = (min + max) * 0.5f;
        var extent = (max - min) * 0.5f * settings.BoundsScale;
        min = center - extent;
        max = center + extent;

        int res = settings.Resolution;
        var grid = new float[res, res, res];

        float stepX = (max.X - min.X) / (res - 1);
        float stepY = (max.Y - min.Y) / (res - 1);
        float stepZ = (max.Z - min.Z) / (res - 1);

        await Task.Run(() =>
        {
            Parallel.For(0, res, x =>
            {
                var viewDir = Vector3.UnitZ; // Fixed view direction for density

                for (int y = 0; y < res; y++)
                {
                    for (int z = 0; z < res; z++)
                    {
                        var position = new Vector3(
                            min.X + x * stepX,
                            min.Y + y * stepY,
                            min.Z + z * stepZ
                        );

                        var (density, _) = _model.Query(position, viewDir);
                        grid[x, y, z] = density;
                    }
                }

                progress?.Report((float)(x + 1) / res);
            });
        });

        // Store grid bounds for later use
        _gridMin = min;
        _gridMax = max;

        return grid;
    }

    private Vector3 _gridMin, _gridMax;

    private (List<Vector3> vertices, List<int> triangles, List<Vector3> colors) MarchingCubes(
        float[,,] grid, MeshExportSettings settings)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var colors = new List<Vector3>();
        var vertexMap = new Dictionary<long, int>();

        int res = settings.Resolution;
        float threshold = settings.DensityThreshold;

        Vector3 gridSize = _gridMax - _gridMin;
        Vector3 cellSize = gridSize / (res - 1);

        for (int x = 0; x < res - 1; x++)
        {
            for (int y = 0; y < res - 1; y++)
            {
                for (int z = 0; z < res - 1; z++)
                {
                    // Get cube corner densities
                    float[] cornerDensities = new float[8];
                    cornerDensities[0] = grid[x, y, z];
                    cornerDensities[1] = grid[x + 1, y, z];
                    cornerDensities[2] = grid[x + 1, y + 1, z];
                    cornerDensities[3] = grid[x, y + 1, z];
                    cornerDensities[4] = grid[x, y, z + 1];
                    cornerDensities[5] = grid[x + 1, y, z + 1];
                    cornerDensities[6] = grid[x + 1, y + 1, z + 1];
                    cornerDensities[7] = grid[x, y + 1, z + 1];

                    // Calculate cube index
                    int cubeIndex = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (cornerDensities[i] >= threshold)
                            cubeIndex |= (1 << i);
                    }

                    // Skip if cube is entirely inside or outside
                    if (cubeIndex == 0 || cubeIndex == 255)
                        continue;

                    // Get corner positions
                    Vector3[] cornerPositions = new Vector3[8];
                    cornerPositions[0] = _gridMin + new Vector3(x, y, z) * cellSize;
                    cornerPositions[1] = _gridMin + new Vector3(x + 1, y, z) * cellSize;
                    cornerPositions[2] = _gridMin + new Vector3(x + 1, y + 1, z) * cellSize;
                    cornerPositions[3] = _gridMin + new Vector3(x, y + 1, z) * cellSize;
                    cornerPositions[4] = _gridMin + new Vector3(x, y, z + 1) * cellSize;
                    cornerPositions[5] = _gridMin + new Vector3(x + 1, y, z + 1) * cellSize;
                    cornerPositions[6] = _gridMin + new Vector3(x + 1, y + 1, z + 1) * cellSize;
                    cornerPositions[7] = _gridMin + new Vector3(x, y + 1, z + 1) * cellSize;

                    for (int i = 0; i < 16; i += 3)
                    {
                        int e0 = MarchingCubesTables.TriangleTable[cubeIndex, i];
                        if (e0 < 0) break;

                        int[] tri = new int[3];

                        for (int j = 0; j < 3; j++)
                        {
                            int edgeIndex = MarchingCubesTables.TriangleTable[cubeIndex, i + j];
                            int v0 = MarchingCubesTables.EdgeVertices[edgeIndex, 0];
                            int v1 = MarchingCubesTables.EdgeVertices[edgeIndex, 1];

                            // Interpolate vertex position along edge
                            float t = (threshold - cornerDensities[v0]) / (cornerDensities[v1] - cornerDensities[v0]);
                            t = Math.Clamp(t, 0f, 1f);

                            var vertexPos = Vector3.Lerp(cornerPositions[v0], cornerPositions[v1], t);

                            // Create unique key for vertex deduplication
                            long key = GetVertexKey(x, y, z, edgeIndex, res);

                            if (!vertexMap.TryGetValue(key, out int vertexIndex))
                            {
                                vertexIndex = vertices.Count;
                                vertices.Add(vertexPos);

                                // Query color from NeRF
                                var viewDir = Vector3.Normalize(vertexPos - (_gridMin + _gridMax) * 0.5f);
                                var (_, color) = _model.Query(vertexPos, viewDir);
                                colors.Add(color);

                                vertexMap[key] = vertexIndex;
                            }

                            tri[j] = vertexIndex;
                        }

                        triangles.Add(tri[0]);
                        triangles.Add(tri[1]);
                        triangles.Add(tri[2]);
                    }
                }
            }
        }

        return (vertices, triangles, colors);
    }

    private static long GetVertexKey(int x, int y, int z, int edge, int res)
    {
        // Create unique key based on cell position and edge
        return ((long)x * res * res + (long)y * res + z) * 12 + edge;
    }

    private async Task<(byte[] data, int width, int height)> BakeTextureAsync(
        List<Vector3> vertices, List<int> triangles, MeshExportSettings settings)
    {
        int texWidth = settings.TextureResolution;
        int texHeight = settings.TextureResolution;
        var textureData = new byte[texWidth * texHeight * 3];

        // Simple UV unwrap: project to dominant axis per triangle
        // This is a simplified approach - a real implementation would use proper UV unwrapping

        await Task.Run(() =>
        {
            var rng = new Random(42);

            // Fill texture with sampled colors from mesh vertices
            for (int i = 0; i < triangles.Count; i += 3)
            {
                var v0 = vertices[triangles[i]];
                var v1 = vertices[triangles[i + 1]];
                var v2 = vertices[triangles[i + 2]];

                // Calculate triangle center
                var center = (v0 + v1 + v2) / 3f;

                // Sample color at center
                var viewDir = Vector3.Normalize(center - (_gridMin + _gridMax) * 0.5f);
                var (_, color) = _model.Query(center, viewDir);

                // Simple mapping: scatter triangles across texture
                int baseU = (i / 3 * 17) % texWidth;
                int baseV = (i / 3 * 31) % texHeight;

                // Fill a small region
                int size = Math.Max(1, texWidth / 64);
                for (int du = 0; du < size; du++)
                {
                    for (int dv = 0; dv < size; dv++)
                    {
                        int u = (baseU + du) % texWidth;
                        int v = (baseV + dv) % texHeight;
                        int idx = (v * texWidth + u) * 3;

                        textureData[idx] = (byte)Math.Clamp(color.X * 255, 0, 255);
                        textureData[idx + 1] = (byte)Math.Clamp(color.Y * 255, 0, 255);
                        textureData[idx + 2] = (byte)Math.Clamp(color.Z * 255, 0, 255);
                    }
                }
            }
        });

        return (textureData, texWidth, texHeight);
    }

    private void ExportMeshOBJ(string path, List<Vector3> vertices, List<int> triangles,
        List<Vector3> colors, string texturePath = null)
    {
        var mtlPath = Path.ChangeExtension(path, ".mtl");
        var mtlName = Path.GetFileNameWithoutExtension(path);

        using (var writer = new StreamWriter(path))
        {
            writer.WriteLine("# NeRF Mesh Export");
            writer.WriteLine($"# {vertices.Count} vertices, {triangles.Count / 3} triangles");

            if (!string.IsNullOrEmpty(texturePath))
            {
                writer.WriteLine($"mtllib {Path.GetFileName(mtlPath)}");
            }

            // Vertices with colors
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                var c = i < colors.Count ? colors[i] : Vector3.One;
                writer.WriteLine($"v {v.X:F6} {v.Y:F6} {v.Z:F6} {c.X:F4} {c.Y:F4} {c.Z:F4}");
            }

            // Generate simple UV coordinates (planar projection)
            var bounds = GetBounds(vertices);
            var size = bounds.max - bounds.min;

            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                float u = (v.X - bounds.min.X) / size.X;
                float vCoord = (v.Y - bounds.min.Y) / size.Y;
                writer.WriteLine($"vt {u:F6} {vCoord:F6}");
            }

            // Calculate vertex normals
            var normals = CalculateVertexNormals(vertices, triangles);
            foreach (var n in normals)
            {
                writer.WriteLine($"vn {n.X:F6} {n.Y:F6} {n.Z:F6}");
            }

            if (!string.IsNullOrEmpty(texturePath))
            {
                writer.WriteLine($"usemtl {mtlName}");
            }

            // Faces (1-indexed)
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int i0 = triangles[i] + 1;
                int i1 = triangles[i + 1] + 1;
                int i2 = triangles[i + 2] + 1;
                writer.WriteLine($"f {i0}/{i0}/{i0} {i1}/{i1}/{i1} {i2}/{i2}/{i2}");
            }
        }

        // Write material file if texture is used
        if (!string.IsNullOrEmpty(texturePath))
        {
            using var mtlWriter = new StreamWriter(mtlPath);
            mtlWriter.WriteLine($"newmtl {mtlName}");
            mtlWriter.WriteLine("Ka 0.1 0.1 0.1");
            mtlWriter.WriteLine("Kd 0.8 0.8 0.8");
            mtlWriter.WriteLine("Ks 0.0 0.0 0.0");
            mtlWriter.WriteLine("Ns 10.0");
            mtlWriter.WriteLine("illum 2");
            mtlWriter.WriteLine($"map_Kd {Path.GetFileName(texturePath)}");
        }

        Logger.Log($"Exported mesh to: {path}");
    }

    private void ExportMeshPLY(string path, List<Vector3> vertices, List<int> triangles, List<Vector3> colors)
    {
        using var writer = new StreamWriter(path);

        // PLY header
        writer.WriteLine("ply");
        writer.WriteLine("format ascii 1.0");
        writer.WriteLine($"element vertex {vertices.Count}");
        writer.WriteLine("property float x");
        writer.WriteLine("property float y");
        writer.WriteLine("property float z");
        writer.WriteLine("property uchar red");
        writer.WriteLine("property uchar green");
        writer.WriteLine("property uchar blue");
        writer.WriteLine($"element face {triangles.Count / 3}");
        writer.WriteLine("property list uchar int vertex_indices");
        writer.WriteLine("end_header");

        // Vertices
        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            var c = i < colors.Count ? colors[i] : Vector3.One;
            int r = (int)Math.Clamp(c.X * 255, 0, 255);
            int g = (int)Math.Clamp(c.Y * 255, 0, 255);
            int b = (int)Math.Clamp(c.Z * 255, 0, 255);
            writer.WriteLine($"{v.X:F6} {v.Y:F6} {v.Z:F6} {r} {g} {b}");
        }

        // Faces
        for (int i = 0; i < triangles.Count; i += 3)
        {
            writer.WriteLine($"3 {triangles[i]} {triangles[i + 1]} {triangles[i + 2]}");
        }

        Logger.Log($"Exported mesh to: {path}");
    }

    private void ExportMeshSTL(string path, List<Vector3> vertices, List<int> triangles)
    {
        using var writer = new StreamWriter(path);

        writer.WriteLine("solid nerf_mesh");

        for (int i = 0; i < triangles.Count; i += 3)
        {
            var v0 = vertices[triangles[i]];
            var v1 = vertices[triangles[i + 1]];
            var v2 = vertices[triangles[i + 2]];

            // Calculate face normal
            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

            writer.WriteLine($"  facet normal {normal.X:F6} {normal.Y:F6} {normal.Z:F6}");
            writer.WriteLine("    outer loop");
            writer.WriteLine($"      vertex {v0.X:F6} {v0.Y:F6} {v0.Z:F6}");
            writer.WriteLine($"      vertex {v1.X:F6} {v1.Y:F6} {v1.Z:F6}");
            writer.WriteLine($"      vertex {v2.X:F6} {v2.Y:F6} {v2.Z:F6}");
            writer.WriteLine("    endloop");
            writer.WriteLine("  endfacet");
        }

        writer.WriteLine("endsolid nerf_mesh");

        Logger.Log($"Exported mesh to: {path}");
    }

    private static List<Vector3> CalculateVertexNormals(List<Vector3> vertices, List<int> triangles)
    {
        var normals = new Vector3[vertices.Count];

        // Accumulate face normals for each vertex
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];

            var v0 = vertices[i0];
            var v1 = vertices[i1];
            var v2 = vertices[i2];

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var faceNormal = Vector3.Cross(edge1, edge2);

            normals[i0] += faceNormal;
            normals[i1] += faceNormal;
            normals[i2] += faceNormal;
        }

        // Normalize
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.Normalize(normals[i]);
            if (float.IsNaN(normals[i].X))
                normals[i] = Vector3.UnitY;
        }

        return normals.ToList();
    }

    private static (Vector3 min, Vector3 max) GetBounds(List<Vector3> vertices)
    {
        if (vertices.Count == 0)
            return (Vector3.Zero, Vector3.One);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var v in vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        return (min, max);
    }

    #endregion

    #region Texture Export

    /// <summary>
    /// Export texture atlas from the NeRF model.
    /// </summary>
    public async Task ExportTextureAsync(string outputPath, TextureExportSettings settings, IProgress<float> progress = null)
    {
        if (_model == null)
        {
            Logger.LogError("No trained model available for export");
            return;
        }

        int width = settings.Resolution;
        int height = settings.Resolution;
        var textureData = new byte[width * height * 3];

        await Task.Run(() =>
        {
            var bounds = _dataset.ImageCollection;
            Vector3 center = bounds?.SceneCenter ?? Vector3.Zero;
            float radius = bounds?.SceneRadius ?? 1.0f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Convert UV to spherical coordinates
                    float u = (float)x / width;
                    float v = (float)y / height;

                    float theta = u * 2 * MathF.PI;
                    float phi = v * MathF.PI;

                    // Direction on unit sphere
                    var direction = new Vector3(
                        MathF.Sin(phi) * MathF.Cos(theta),
                        MathF.Cos(phi),
                        MathF.Sin(phi) * MathF.Sin(theta)
                    );

                    // Ray from center outward
                    var position = center + direction * radius * 0.5f;
                    var viewDir = -direction;

                    var (density, color) = _model.Query(position, viewDir);

                    int idx = (y * width + x) * 3;
                    textureData[idx] = (byte)Math.Clamp(color.X * 255, 0, 255);
                    textureData[idx + 1] = (byte)Math.Clamp(color.Y * 255, 0, 255);
                    textureData[idx + 2] = (byte)Math.Clamp(color.Z * 255, 0, 255);
                }

                progress?.Report((float)(y + 1) / height);
            }
        });

        SaveTexture(outputPath, textureData, width, height);
    }

    private void SaveTexture(string path, byte[] data, int width, int height)
    {
        try
        {
            using var stream = File.Create(path);
            var writer = new StbImageWriteSharp.ImageWriter();
            writer.WritePng(data, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlue, stream);
            Logger.Log($"Exported texture to: {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save texture: {ex.Message}");
        }
    }

    #endregion
}

#region Export Settings

/// <summary>
/// Settings for point cloud export.
/// </summary>
public class PointCloudExportSettings
{
    public int ResolutionX { get; set; } = 128;
    public int ResolutionY { get; set; } = 128;
    public int ResolutionZ { get; set; } = 128;
    public float DensityThreshold { get; set; } = 10f;
    public float BoundsScale { get; set; } = 1.2f;
    public bool UseJitter { get; set; } = true;
}

/// <summary>
/// Settings for mesh export.
/// </summary>
public class MeshExportSettings
{
    public int Resolution { get; set; } = 128;
    public float DensityThreshold { get; set; } = 10f;
    public float BoundsScale { get; set; } = 1.2f;
    public bool BakeTexture { get; set; } = true;
    public int TextureResolution { get; set; } = 1024;
    public bool SmoothNormals { get; set; } = true;
}

/// <summary>
/// Settings for texture export.
/// </summary>
public class TextureExportSettings
{
    public int Resolution { get; set; } = 1024;
}

/// <summary>
/// Colored point for point cloud export.
/// </summary>
public struct ColoredPoint
{
    public Vector3 Position { get; set; }
    public Vector3 Color { get; set; }
    public float Density { get; set; }
}

#endregion
