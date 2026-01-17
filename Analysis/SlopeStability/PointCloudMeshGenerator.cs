// GeoscientistToolkit/Analysis/SlopeStability/PointCloudMeshGenerator.cs
// Point Cloud to Mesh Generation for Slope Stability Analysis
// Based on MATLAB code by Francesco Ottaviani (Università degli Studi di Urbino Carlo Bo)
// Translated and adapted for C# by the GeoscientistToolkit team

using System.Globalization;
using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.SlopeStability;

/// <summary>
/// Generates 3D meshes from point cloud data for slope stability analysis.
/// Implements Delaunay triangulation with mesh optimization and welding algorithms.
/// </summary>
public class PointCloudMeshGenerator
{
    /// <summary>
    /// Parameters for mesh generation
    /// </summary>
    public class MeshGenerationParameters
    {
        /// <summary>Grid step for downsampling (default 2.0)</summary>
        public float GridStep { get; set; } = 2.0f;

        /// <summary>Maximum edge length for triangle filtering</summary>
        public float MaxEdgeLength { get; set; } = 4.0f;

        /// <summary>Depth below minimum Z for solid bottom</summary>
        public float ZDeep { get; set; } = 20.0f;

        /// <summary>Distance between cutting planes</summary>
        public float PlaneDistance { get; set; } = 50.0f;

        /// <summary>Welding angle threshold (degrees)</summary>
        public float WeldingAngle { get; set; } = 40.0f;

        /// <summary>Peak height threshold</summary>
        public float PeakThreshold { get; set; } = 0.005f;

        /// <summary>Enable mesh welding/smoothing</summary>
        public bool EnableWelding { get; set; } = false;

        /// <summary>Enable surface interpolation</summary>
        public bool EnableInterpolation { get; set; } = true;

        /// <summary>Enable downsampling</summary>
        public bool EnableDownsampling { get; set; } = true;

        /// <summary>Interpolation method</summary>
        public InterpolationMethod Interpolation { get; set; } = InterpolationMethod.Nearest;

        /// <summary>Rotation angle around Z axis (degrees)</summary>
        public float RotationAngleDegrees { get; set; } = 0.0f;

        /// <summary>Translation vector to apply</summary>
        public Vector3 TranslationVector { get; set; } = Vector3.Zero;

        /// <summary>Translate mesh centroid to origin</summary>
        public bool TranslateToOrigin { get; set; } = false;

        /// <summary>Create solid mesh with bottom surface</summary>
        public bool CreateSolidMesh { get; set; } = true;

        /// <summary>Nodata value to filter out</summary>
        public float NodataValue { get; set; } = -9999.0f;
    }

    public enum InterpolationMethod
    {
        Nearest,
        Linear,
        Natural
    }

    /// <summary>
    /// Result of mesh generation
    /// </summary>
    public class MeshGenerationResult
    {
        public List<Vector3> Vertices { get; set; } = new();
        public List<int[]> Faces { get; set; } = new();
        public Vector3 BoundingBoxMin { get; set; }
        public Vector3 BoundingBoxMax { get; set; }
        public Vector3 Center { get; set; }
        public int OriginalPointCount { get; set; }
        public int FilteredPointCount { get; set; }
        public string StatusMessage { get; set; } = "";
        public bool Success { get; set; }
    }

    /// <summary>
    /// Point cloud data structure
    /// </summary>
    public class PointCloud
    {
        public List<Vector3> Points { get; set; } = new();
        public List<Vector4> Colors { get; set; } = new(); // RGBA if available
        public bool HasColors => Colors.Count > 0;

        public Vector3 Min { get; private set; }
        public Vector3 Max { get; private set; }
        public Vector3 Centroid { get; private set; }

        public void CalculateBounds()
        {
            if (Points.Count == 0)
            {
                Min = Max = Centroid = Vector3.Zero;
                return;
            }

            Min = new Vector3(float.MaxValue);
            Max = new Vector3(float.MinValue);

            Vector3 sum = Vector3.Zero;
            foreach (var p in Points)
            {
                Min = Vector3.Min(Min, p);
                Max = Vector3.Max(Max, p);
                sum += p;
            }

            Centroid = sum / Points.Count;
        }
    }

    private readonly MeshGenerationParameters _params;
    private Action<string, float> _progressCallback;

    public PointCloudMeshGenerator(MeshGenerationParameters parameters = null)
    {
        _params = parameters ?? new MeshGenerationParameters();
    }

    /// <summary>
    /// Sets a callback for progress updates
    /// </summary>
    public void SetProgressCallback(Action<string, float> callback)
    {
        _progressCallback = callback;
    }

    private void ReportProgress(string message, float progress)
    {
        _progressCallback?.Invoke(message, progress);
        Logger.Log($"[PointCloudMeshGenerator] {message} ({progress:P0})");
    }

    /// <summary>
    /// Loads point cloud from file (XYZ, TXT, CSV formats)
    /// </summary>
    public PointCloud LoadPointCloud(string filePath)
    {
        ReportProgress("Loading point cloud...", 0.0f);

        var cloud = new PointCloud();
        var lines = File.ReadAllLines(filePath);
        var culture = CultureInfo.InvariantCulture;

        int lineCount = 0;
        int totalLines = lines.Length;

        foreach (var line in lines)
        {
            lineCount++;
            if (lineCount % 10000 == 0)
                ReportProgress($"Loading point cloud ({lineCount}/{totalLines})...", (float)lineCount / totalLines * 0.1f);

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
                continue;

            var parts = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 3)
            {
                if (float.TryParse(parts[0], NumberStyles.Float, culture, out var x) &&
                    float.TryParse(parts[1], NumberStyles.Float, culture, out var y) &&
                    float.TryParse(parts[2], NumberStyles.Float, culture, out var z))
                {
                    cloud.Points.Add(new Vector3(x, y, z));

                    // Check for RGB values (columns 4, 5, 6)
                    if (parts.Length >= 6)
                    {
                        if (float.TryParse(parts[3], NumberStyles.Float, culture, out var r) &&
                            float.TryParse(parts[4], NumberStyles.Float, culture, out var g) &&
                            float.TryParse(parts[5], NumberStyles.Float, culture, out var b))
                        {
                            // Normalize if values > 1
                            if (r > 1 || g > 1 || b > 1)
                            {
                                r /= 255f;
                                g /= 255f;
                                b /= 255f;
                            }
                            cloud.Colors.Add(new Vector4(r, g, b, 1.0f));
                        }
                    }
                }
            }
        }

        cloud.CalculateBounds();
        ReportProgress($"Loaded {cloud.Points.Count} points", 0.1f);

        return cloud;
    }

    /// <summary>
    /// Filters point cloud removing nodata values and invalid points
    /// </summary>
    public PointCloud FilterPointCloud(PointCloud input)
    {
        ReportProgress("Filtering point cloud...", 0.15f);

        var filtered = new PointCloud();

        for (int i = 0; i < input.Points.Count; i++)
        {
            var p = input.Points[i];

            // Skip nodata values
            if (p.Z <= _params.NodataValue || !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                continue;

            filtered.Points.Add(p);
            if (input.HasColors && i < input.Colors.Count)
                filtered.Colors.Add(input.Colors[i]);
        }

        filtered.CalculateBounds();
        ReportProgress($"Filtered to {filtered.Points.Count} points", 0.2f);

        return filtered;
    }

    /// <summary>
    /// Downsamples point cloud using grid averaging
    /// </summary>
    public PointCloud DownsamplePointCloud(PointCloud input)
    {
        if (!_params.EnableDownsampling)
            return input;

        ReportProgress("Downsampling point cloud...", 0.25f);

        var gridSize = _params.GridStep;
        var buckets = new Dictionary<(int, int, int), (Vector3 sum, int count, Vector4 colorSum)>();

        foreach (var p in input.Points)
        {
            var key = (
                (int)MathF.Floor(p.X / gridSize),
                (int)MathF.Floor(p.Y / gridSize),
                (int)MathF.Floor(p.Z / gridSize)
            );

            if (!buckets.ContainsKey(key))
                buckets[key] = (Vector3.Zero, 0, Vector4.Zero);

            var (sum, count, colorSum) = buckets[key];
            buckets[key] = (sum + p, count + 1, colorSum);
        }

        var result = new PointCloud();
        foreach (var kvp in buckets)
        {
            var avg = kvp.Value.sum / kvp.Value.count;
            result.Points.Add(avg);
        }

        result.CalculateBounds();
        ReportProgress($"Downsampled to {result.Points.Count} points", 0.3f);

        return result;
    }

    /// <summary>
    /// Applies rotation around Z axis
    /// </summary>
    public PointCloud ApplyRotation(PointCloud input)
    {
        if (MathF.Abs(_params.RotationAngleDegrees) < 0.001f)
            return input;

        ReportProgress("Applying rotation...", 0.35f);

        var angleRad = _params.RotationAngleDegrees * MathF.PI / 180.0f;
        var cos = MathF.Cos(angleRad);
        var sin = MathF.Sin(angleRad);

        var result = new PointCloud();

        foreach (var p in input.Points)
        {
            var rotated = new Vector3(
                p.X * cos - p.Y * sin,
                p.X * sin + p.Y * cos,
                p.Z
            );
            result.Points.Add(rotated);
        }

        if (input.HasColors)
            result.Colors.AddRange(input.Colors);

        result.CalculateBounds();
        return result;
    }

    /// <summary>
    /// Removes duplicate XY points, averaging Z values
    /// </summary>
    private (List<Vector3> vertices, Dictionary<(float, float), int> xyToIndex) DeduplicateXY(List<Vector3> points)
    {
        var buckets = new Dictionary<(int, int), (Vector3 sum, int count)>();
        var tolerance = _params.GridStep * 0.1f; // Use fraction of grid step as tolerance

        foreach (var p in points)
        {
            var key = ((int)MathF.Round(p.X / tolerance), (int)MathF.Round(p.Y / tolerance));

            if (!buckets.ContainsKey(key))
                buckets[key] = (Vector3.Zero, 0);

            var (sum, count) = buckets[key];
            buckets[key] = (sum + p, count + 1);
        }

        var result = new List<Vector3>();
        var xyToIndex = new Dictionary<(float, float), int>();

        foreach (var kvp in buckets)
        {
            var avg = kvp.Value.sum / kvp.Value.count;
            var key = (avg.X, avg.Y);
            if (!xyToIndex.ContainsKey(key))
            {
                xyToIndex[key] = result.Count;
                result.Add(avg);
            }
        }

        return (result, xyToIndex);
    }

    /// <summary>
    /// Performs Delaunay triangulation on XY points
    /// </summary>
    private List<int[]> DelaunayTriangulation(List<Vector3> vertices)
    {
        ReportProgress("Performing Delaunay triangulation...", 0.4f);

        if (vertices.Count < 3)
            return new List<int[]>();

        var triangles = new List<int[]>();

        // Use Bowyer-Watson algorithm for Delaunay triangulation
        // First, create super-triangle that encompasses all points
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var v in vertices)
        {
            minX = MathF.Min(minX, v.X);
            maxX = MathF.Max(maxX, v.X);
            minY = MathF.Min(minY, v.Y);
            maxY = MathF.Max(maxY, v.Y);
        }

        float dx = maxX - minX;
        float dy = maxY - minY;
        float dmax = MathF.Max(dx, dy) * 2;
        float midX = (minX + maxX) / 2;
        float midY = (minY + maxY) / 2;

        // Super-triangle vertices (indices will be vertices.Count, vertices.Count+1, vertices.Count+2)
        var p0 = new Vector3(midX - dmax * 2, midY - dmax, 0);
        var p1 = new Vector3(midX, midY + dmax * 2, 0);
        var p2 = new Vector3(midX + dmax * 2, midY - dmax, 0);

        var allPoints = new List<Vector3>(vertices) { p0, p1, p2 };
        int superTriStart = vertices.Count;

        // Start with super-triangle
        var tris = new List<(int a, int b, int c)> { (superTriStart, superTriStart + 1, superTriStart + 2) };

        // Add points one by one
        for (int i = 0; i < vertices.Count; i++)
        {
            if (i % 1000 == 0)
                ReportProgress($"Triangulating ({i}/{vertices.Count})...", 0.4f + 0.2f * i / vertices.Count);

            var p = allPoints[i];
            var badTriangles = new List<(int a, int b, int c)>();

            foreach (var tri in tris)
            {
                if (IsPointInCircumcircle(p, allPoints[tri.a], allPoints[tri.b], allPoints[tri.c]))
                    badTriangles.Add(tri);
            }

            // Find boundary polygon
            var polygon = new List<(int, int)>();
            foreach (var tri in badTriangles)
            {
                var edges = new[] { (tri.a, tri.b), (tri.b, tri.c), (tri.c, tri.a) };
                foreach (var edge in edges)
                {
                    bool shared = false;
                    foreach (var other in badTriangles)
                    {
                        if (tri.Equals(other)) continue;
                        var otherEdges = new[] { (other.a, other.b), (other.b, other.c), (other.c, other.a) };
                        foreach (var oe in otherEdges)
                        {
                            if ((edge.Item1 == oe.Item1 && edge.Item2 == oe.Item2) ||
                                (edge.Item1 == oe.Item2 && edge.Item2 == oe.Item1))
                            {
                                shared = true;
                                break;
                            }
                        }
                        if (shared) break;
                    }
                    if (!shared)
                        polygon.Add(edge);
                }
            }

            // Remove bad triangles
            foreach (var bt in badTriangles)
                tris.Remove(bt);

            // Create new triangles
            foreach (var edge in polygon)
                tris.Add((edge.Item1, edge.Item2, i));
        }

        // Remove triangles that share vertices with super-triangle
        foreach (var tri in tris)
        {
            if (tri.a >= superTriStart || tri.b >= superTriStart || tri.c >= superTriStart)
                continue;

            triangles.Add(new[] { tri.a, tri.b, tri.c });
        }

        ReportProgress($"Created {triangles.Count} triangles", 0.6f);
        return triangles;
    }

    private bool IsPointInCircumcircle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        float ax = a.X - p.X;
        float ay = a.Y - p.Y;
        float bx = b.X - p.X;
        float by = b.Y - p.Y;
        float cx = c.X - p.X;
        float cy = c.Y - p.Y;

        float det = (ax * ax + ay * ay) * (bx * cy - cx * by) -
                    (bx * bx + by * by) * (ax * cy - cx * ay) +
                    (cx * cx + cy * cy) * (ax * by - bx * ay);

        // Counter-clockwise orientation check
        float orient = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        if (orient > 0)
            return det > 0;
        else
            return det < 0;
    }

    /// <summary>
    /// Filters triangles by maximum edge length
    /// </summary>
    private List<int[]> FilterByEdgeLength(List<int[]> faces, List<Vector3> vertices)
    {
        ReportProgress("Filtering triangles by edge length...", 0.65f);

        var maxLen = _params.MaxEdgeLength;
        var result = new List<int[]>();

        foreach (var face in faces)
        {
            var v0 = vertices[face[0]];
            var v1 = vertices[face[1]];
            var v2 = vertices[face[2]];

            float e1 = Vector2.Distance(new Vector2(v0.X, v0.Y), new Vector2(v1.X, v1.Y));
            float e2 = Vector2.Distance(new Vector2(v1.X, v1.Y), new Vector2(v2.X, v2.Y));
            float e3 = Vector2.Distance(new Vector2(v2.X, v2.Y), new Vector2(v0.X, v0.Y));

            if (e1 <= maxLen && e2 <= maxLen && e3 <= maxLen)
                result.Add(face);
        }

        ReportProgress($"Filtered to {result.Count} triangles", 0.7f);
        return result;
    }

    /// <summary>
    /// Creates solid mesh with bottom and sides
    /// </summary>
    private (List<Vector3> vertices, List<int[]> faces) CreateSolidMesh(List<Vector3> topVertices, List<int[]> topFaces)
    {
        if (!_params.CreateSolidMesh)
            return (topVertices, topFaces);

        ReportProgress("Creating solid mesh...", 0.75f);

        // Find minimum Z
        float minZ = float.MaxValue;
        foreach (var v in topVertices)
            minZ = MathF.Min(minZ, v.Z);

        float bottomZ = minZ - _params.ZDeep;

        // Create combined vertex list (top + bottom)
        var allVertices = new List<Vector3>(topVertices);
        int n = topVertices.Count;

        // Add bottom vertices (same XY, flat Z)
        foreach (var v in topVertices)
            allVertices.Add(new Vector3(v.X, v.Y, bottomZ));

        // Combine faces
        var allFaces = new List<int[]>(topFaces);

        // Bottom faces (reversed winding for proper normals)
        foreach (var face in topFaces)
        {
            allFaces.Add(new[] { face[2] + n, face[1] + n, face[0] + n });
        }

        // Find boundary edges
        var edgeCount = new Dictionary<(int, int), int>();
        foreach (var face in topFaces)
        {
            var edges = new[] {
                (MathF.Min(face[0], face[1]), MathF.Max(face[0], face[1])),
                (MathF.Min(face[1], face[2]), MathF.Max(face[1], face[2])),
                (MathF.Min(face[2], face[0]), MathF.Max(face[2], face[0]))
            };

            foreach (var e in edges)
            {
                var key = ((int)e.Item1, (int)e.Item2);
                if (!edgeCount.ContainsKey(key))
                    edgeCount[key] = 0;
                edgeCount[key]++;
            }
        }

        // Boundary edges are those used by only one face
        var boundaryEdges = edgeCount.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();

        // Create side faces
        foreach (var edge in boundaryEdges)
        {
            int v1 = edge.Item1;
            int v2 = edge.Item2;
            int v1b = v1 + n;
            int v2b = v2 + n;

            // Two triangles for each side quad
            allFaces.Add(new[] { v1, v1b, v2b });
            allFaces.Add(new[] { v1, v2b, v2 });
        }

        ReportProgress($"Created solid mesh with {allFaces.Count} faces", 0.85f);
        return (allVertices, allFaces);
    }

    /// <summary>
    /// Generates mesh from point cloud file
    /// </summary>
    public MeshGenerationResult GenerateFromFile(string inputPath)
    {
        var result = new MeshGenerationResult();

        try
        {
            // Load and process point cloud
            var cloud = LoadPointCloud(inputPath);
            result.OriginalPointCount = cloud.Points.Count;

            if (cloud.Points.Count < 3)
            {
                result.Success = false;
                result.StatusMessage = "Not enough points in point cloud (minimum 3 required)";
                return result;
            }

            // Filter
            cloud = FilterPointCloud(cloud);

            // Downsample
            cloud = DownsamplePointCloud(cloud);

            // Apply rotation
            cloud = ApplyRotation(cloud);

            // Translate to origin if requested
            if (_params.TranslateToOrigin)
            {
                var translation = -cloud.Centroid;
                var translated = new PointCloud();
                foreach (var p in cloud.Points)
                    translated.Points.Add(p + translation);
                translated.Colors = cloud.Colors;
                translated.CalculateBounds();
                cloud = translated;
            }

            result.FilteredPointCount = cloud.Points.Count;

            // Deduplicate XY
            var (vertices, _) = DeduplicateXY(cloud.Points);

            if (vertices.Count < 3)
            {
                result.Success = false;
                result.StatusMessage = "Not enough unique XY points after deduplication";
                return result;
            }

            // Delaunay triangulation
            var faces = DelaunayTriangulation(vertices);

            // Filter by edge length
            faces = FilterByEdgeLength(faces, vertices);

            if (faces.Count == 0)
            {
                result.Success = false;
                result.StatusMessage = "No valid triangles after filtering";
                return result;
            }

            // Create solid mesh
            var (solidVertices, solidFaces) = CreateSolidMesh(vertices, faces);

            result.Vertices = solidVertices;
            result.Faces = solidFaces;

            // Calculate bounds
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var v in result.Vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            result.BoundingBoxMin = min;
            result.BoundingBoxMax = max;
            result.Center = (min + max) * 0.5f;

            result.Success = true;
            result.StatusMessage = $"Successfully generated mesh with {result.Vertices.Count} vertices and {result.Faces.Count} faces";

            ReportProgress("Mesh generation complete", 1.0f);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.StatusMessage = $"Error generating mesh: {ex.Message}";
            Logger.LogError($"[PointCloudMeshGenerator] {ex}");
        }

        return result;
    }

    /// <summary>
    /// Generates mesh from already loaded point cloud data
    /// </summary>
    public MeshGenerationResult GenerateFromPoints(List<Vector3> points)
    {
        var cloud = new PointCloud { Points = points };
        cloud.CalculateBounds();

        return GenerateFromCloud(cloud);
    }

    /// <summary>
    /// Generates mesh from point cloud object
    /// </summary>
    public MeshGenerationResult GenerateFromCloud(PointCloud cloud)
    {
        var result = new MeshGenerationResult();
        result.OriginalPointCount = cloud.Points.Count;

        try
        {
            if (cloud.Points.Count < 3)
            {
                result.Success = false;
                result.StatusMessage = "Not enough points (minimum 3 required)";
                return result;
            }

            // Filter
            cloud = FilterPointCloud(cloud);

            // Downsample
            cloud = DownsamplePointCloud(cloud);

            // Apply rotation
            cloud = ApplyRotation(cloud);

            // Translate to origin if requested
            if (_params.TranslateToOrigin)
            {
                var translation = -cloud.Centroid;
                var translated = new PointCloud();
                foreach (var p in cloud.Points)
                    translated.Points.Add(p + translation);
                translated.Colors = cloud.Colors;
                translated.CalculateBounds();
                cloud = translated;
            }

            result.FilteredPointCount = cloud.Points.Count;

            // Deduplicate XY
            var (vertices, _) = DeduplicateXY(cloud.Points);

            if (vertices.Count < 3)
            {
                result.Success = false;
                result.StatusMessage = "Not enough unique XY points";
                return result;
            }

            // Triangulate
            var faces = DelaunayTriangulation(vertices);

            // Filter
            faces = FilterByEdgeLength(faces, vertices);

            if (faces.Count == 0)
            {
                result.Success = false;
                result.StatusMessage = "No valid triangles after filtering";
                return result;
            }

            // Create solid mesh
            var (solidVertices, solidFaces) = CreateSolidMesh(vertices, faces);

            result.Vertices = solidVertices;
            result.Faces = solidFaces;

            // Calculate bounds
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var v in result.Vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            result.BoundingBoxMin = min;
            result.BoundingBoxMax = max;
            result.Center = (min + max) * 0.5f;

            result.Success = true;
            result.StatusMessage = $"Generated mesh: {result.Vertices.Count} vertices, {result.Faces.Count} faces";

            ReportProgress("Complete", 1.0f);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.StatusMessage = $"Error: {ex.Message}";
            Logger.LogError($"[PointCloudMeshGenerator] {ex}");
        }

        return result;
    }
}
