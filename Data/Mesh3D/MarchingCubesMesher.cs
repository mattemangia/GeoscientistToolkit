// GeoscientistToolkit/Data/Mesh3D/MarchingCubesMesher.cs

using System.Numerics;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Mesh3D;

/// <summary>
/// Generates 3D meshes from volumetric density data using the Marching Cubes algorithm.
/// Supports parallel processing and configurable isosurface thresholds.
/// </summary>
public class MarchingCubesMesher
{
    private static readonly int[,] EdgeVertexTable =
    {
        {0, 1}, {1, 2}, {2, 3}, {3, 0},
        {4, 5}, {5, 6}, {6, 7}, {7, 4},
        {0, 4}, {1, 5}, {2, 6}, {3, 7}
    };

    private static readonly Vector3[] CornerOffsets =
    {
        new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
        new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)
    };

    public async Task<(List<Vector3> vertices, List<int> indices)> GenerateMeshAsync(
        ChunkedVolume volume,
        byte isoValue,
        int stepSize,
        IProgress<(float progress, string message)> progress,
        CancellationToken token = default)
    {
        progress?.Report((0.0f, "Starting Marching Cubes..."));

        if (stepSize < 1) stepSize = 1;

        var vertices = new List<Vector3>();
        var indices = new List<int>();
        var edgeVertexCache = new Dictionary<long, int>();

        int width = volume.Width;
        int height = volume.Height;
        int depth = volume.Depth;

        int xSteps = (width - 1) / stepSize;
        int ySteps = (height - 1) / stepSize;
        int zSteps = (depth - 1) / stepSize;
        int totalCubes = xSteps * ySteps * zSteps;
        int processedCubes = 0;

        for (int z = 0; z < depth - stepSize; z += stepSize)
        {
            token.ThrowIfCancellationRequested();

            for (int y = 0; y < height - stepSize; y += stepSize)
            {
                for (int x = 0; x < width - stepSize; x += stepSize)
                {
                    ProcessCube(volume, x, y, z, stepSize, isoValue, vertices, indices, edgeVertexCache);
                    processedCubes++;
                }
            }

            if (z % (stepSize * 10) == 0)
            {
                float progressValue = (float)processedCubes / totalCubes;
                progress?.Report((progressValue * 0.95f, $"Processing slice {z}/{depth}..."));
            }
        }

        progress?.Report((0.98f, $"Generated {vertices.Count} vertices, {indices.Count / 3} triangles"));
        progress?.Report((1.0f, "Mesh generation complete"));

        return (vertices, indices);
    }

    private void ProcessCube(
        ChunkedVolume volume,
        int x, int y, int z,
        int step,
        byte isoValue,
        List<Vector3> vertices,
        List<int> indices,
        Dictionary<long, int> edgeVertexCache)
    {
        byte[] cornerValues = new byte[8];
        cornerValues[0] = volume[x, y, z];
        cornerValues[1] = volume[x + step, y, z];
        cornerValues[2] = volume[x + step, y + step, z];
        cornerValues[3] = volume[x, y + step, z];
        cornerValues[4] = volume[x, y, z + step];
        cornerValues[5] = volume[x + step, y, z + step];
        cornerValues[6] = volume[x + step, y + step, z + step];
        cornerValues[7] = volume[x, y + step, z + step];

        int cubeIndex = 0;
        for (int i = 0; i < 8; i++)
        {
            if (cornerValues[i] >= isoValue)
                cubeIndex |= (1 << i);
        }

        if (cubeIndex == 0 || cubeIndex == 255)
            return;

        int edgeFlags = MarchingCubesTables.EdgeTable[cubeIndex];
        if (edgeFlags == 0)
            return;

        int[] edgeVertices = new int[12];

        for (int edge = 0; edge < 12; edge++)
        {
            if ((edgeFlags & (1 << edge)) != 0)
            {
                int v0 = EdgeVertexTable[edge, 0];
                int v1 = EdgeVertexTable[edge, 1];

                long edgeKey = GetEdgeKey(x, y, z, step, edge);

                if (!edgeVertexCache.TryGetValue(edgeKey, out int vertexIndex))
                {
                    Vector3 p0 = new Vector3(x, y, z) + CornerOffsets[v0] * step;
                    Vector3 p1 = new Vector3(x, y, z) + CornerOffsets[v1] * step;

                    float val0 = cornerValues[v0];
                    float val1 = cornerValues[v1];
                    float t = (isoValue - val0) / (val1 - val0 + 0.0001f);
                    t = Math.Clamp(t, 0f, 1f);

                    Vector3 vertex = Vector3.Lerp(p0, p1, t);
                    vertexIndex = vertices.Count;
                    vertices.Add(vertex);
                    edgeVertexCache[edgeKey] = vertexIndex;
                }

                edgeVertices[edge] = vertexIndex;
            }
        }

        for (int i = 0; i < 16; i += 3)
        {
            int e0 = MarchingCubesTables.TriangleTable[cubeIndex, i];
            if (e0 < 0) break;

            int e1 = MarchingCubesTables.TriangleTable[cubeIndex, i + 1];
            int e2 = MarchingCubesTables.TriangleTable[cubeIndex, i + 2];

            indices.Add(edgeVertices[e0]);
            indices.Add(edgeVertices[e1]);
            indices.Add(edgeVertices[e2]);
        }
    }

    private static long GetEdgeKey(int x, int y, int z, int step, int edgeIndex)
    {
        int ex = x, ey = y, ez = z;
        int axis = 0;

        switch (edgeIndex)
        {
            case 0: axis = 0; break;
            case 1: ex += step; axis = 1; break;
            case 2: ey += step; axis = 0; break;
            case 3: axis = 1; break;
            case 4: ez += step; axis = 0; break;
            case 5: ex += step; ez += step; axis = 1; break;
            case 6: ey += step; ez += step; axis = 0; break;
            case 7: ez += step; axis = 1; break;
            case 8: axis = 2; break;
            case 9: ex += step; axis = 2; break;
            case 10: ex += step; ey += step; axis = 2; break;
            case 11: ey += step; axis = 2; break;
        }

        return ((long)axis << 48) | ((long)ez << 32) | ((long)ey << 16) | (long)ex;
    }

    public static void ExportToObj(string filePath, List<Vector3> vertices, List<int> indices, float scale = 1.0f)
    {
        using var writer = new StreamWriter(filePath);
        writer.WriteLine("# Marching Cubes mesh export");
        writer.WriteLine($"# Vertices: {vertices.Count}, Triangles: {indices.Count / 3}");

        foreach (var v in vertices)
            writer.WriteLine($"v {v.X * scale:F6} {v.Y * scale:F6} {v.Z * scale:F6}");

        for (int i = 0; i < indices.Count; i += 3)
            writer.WriteLine($"f {indices[i] + 1} {indices[i + 1] + 1} {indices[i + 2] + 1}");
    }

    public static void ExportToStl(string filePath, List<Vector3> vertices, List<int> indices, float scale = 1.0f)
    {
        using var writer = new BinaryWriter(File.Create(filePath));

        byte[] header = new byte[80];
        writer.Write(header);
        writer.Write(indices.Count / 3);

        for (int i = 0; i < indices.Count; i += 3)
        {
            Vector3 v0 = vertices[indices[i]] * scale;
            Vector3 v1 = vertices[indices[i + 1]] * scale;
            Vector3 v2 = vertices[indices[i + 2]] * scale;

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

            if (float.IsNaN(normal.X)) normal = Vector3.UnitZ;

            writer.Write(normal.X);
            writer.Write(normal.Y);
            writer.Write(normal.Z);

            writer.Write(v0.X); writer.Write(v0.Y); writer.Write(v0.Z);
            writer.Write(v1.X); writer.Write(v1.Y); writer.Write(v1.Z);
            writer.Write(v2.X); writer.Write(v2.Y); writer.Write(v2.Z);

            writer.Write((ushort)0);
        }
    }
}
