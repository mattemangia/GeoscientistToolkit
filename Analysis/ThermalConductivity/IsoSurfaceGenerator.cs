// GeoscientistToolkit/Analysis/IsosurfaceGenerator.cs

using System.Numerics;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis;

/// <summary>
/// Generates 3D isosurfaces and 2D isocontours from scalar field data using the Surface Nets algorithm.
/// This method is an alternative to Marching Cubes and often produces meshes with better element quality.
/// </summary>
public static class IsosurfaceGenerator
{
    // Edge connections for a cube. 12 edges total.
    private static readonly int[,] _cubeEdges = {
        {0,1}, {1,2}, {2,3}, {3,0}, {4,5}, {5,6}, {6,7}, {7,4},
        {0,4}, {1,5}, {2,6}, {3,7}
    };

    // Voxel corner coordinates relative to the origin (0,0,0)
    private static readonly Vector3[] _cubeCorners = {
        new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
        new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)
    };

    /// <summary>
    /// Generates a 3D mesh (isosurface) from a 3D scalar field for a given isovalue.
    /// </summary>
    /// <param name="scalarField">The 3D array of scalar values.</param>
    /// <param name="isovalue">The threshold value to generate the surface at.</param>
    /// <param name="voxelSize">The physical size of a voxel for scaling.</param>
    /// <returns>A Mesh3DDataset containing the generated surface mesh.</returns>
    public static Mesh3DDataset GenerateIsosurface(float[,,] scalarField, float isovalue, Vector3 voxelSize)
    {
        var dimX = scalarField.GetLength(0);
        var dimY = scalarField.GetLength(1);
        var dimZ = scalarField.GetLength(2);

        var vertices = new List<Vector3>();
        var faces = new List<int[]>();
        var vertexGrid = new Dictionary<long, int>();

        var values = new float[8];
        var corners = new Vector3[8];

        for (int z = 0; z < dimZ - 1; z++)
        for (int y = 0; y < dimY - 1; y++)
        for (int x = 0; x < dimX - 1; x++)
        {
            // Get scalar values at the 8 corners of the current voxel
            int cubeCase = 0;
            for(int i = 0; i < 8; i++)
            {
                var cornerPos = new Vector3(x, y, z) + _cubeCorners[i];
                values[i] = scalarField[(int)cornerPos.X, (int)cornerPos.Y, (int)cornerPos.Z];
                corners[i] = cornerPos;
                if (values[i] < isovalue)
                {
                    cubeCase |= (1 << i);
                }
            }

            // Skip if cell is entirely inside or outside the surface
            if (cubeCase == 0 || cubeCase == 255)
            {
                continue;
            }

            // Find the point where the isosurface intersects the edges of the voxel
            var surfacePoint = Vector3.Zero;
            int edgeCount = 0;
            for(int i = 0; i < 12; i++)
            {
                int c1 = _cubeEdges[i, 0];
                int c2 = _cubeEdges[i, 1];

                if (((cubeCase >> c1) & 1) != ((cubeCase >> c2) & 1))
                {
                    float v1 = values[c1];
                    float v2 = values[c2];
                    
                    // Linear interpolation to find intersection point
                    float t = (isovalue - v1) / (v2 - v1);
                    surfacePoint += Vector3.Lerp(corners[c1], corners[c2], t);
                    edgeCount++;
                }
            }

            if (edgeCount > 0)
            {
                surfacePoint /= edgeCount;
            }
            
            long gridKey = x + (long)y * dimX + (long)z * dimX * dimY;
            vertexGrid[gridKey] = vertices.Count;
            vertices.Add(surfacePoint * voxelSize); // Scale vertex to physical size
            
            // Generate faces by connecting to adjacent voxels
            // Check in -X, -Y, -Z directions to connect to already processed voxels
            int[] dx = { -1, 0, 0 };
            int[] dy = { 0, -1, 0 };
            int[] dz = { 0, 0, -1 };
            
            for (int i = 0; i < 3; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];
                int nz = z + dz[i];
                long neighborKey = nx + (long)ny * dimX + (long)nz * dimX * dimY;

                if (vertexGrid.ContainsKey(neighborKey))
                {
                    // Found a neighbor, now find the two other common neighbors to form a quad
                    long key1 = (x + dx[i]) + (long)(y + dy[i]) * dimX + (long)z * dimX * dimY;
                    long key2 = (x + dx[i]) + (long)y * dimX + (long)(z + dz[i]) * dimX * dimY;
                    long key3 = x + (long)(y + dy[i]) * dimX + (long)(z + dz[i]) * dimX * dimY;

                    if (vertexGrid.ContainsKey(key1) && vertexGrid.ContainsKey(key3))
                    {
                         faces.Add(new[] { vertexGrid[gridKey], vertexGrid[key1], vertexGrid[neighborKey], vertexGrid[key3] });
                    }
                     if (vertexGrid.ContainsKey(key2) && vertexGrid.ContainsKey(key3))
                    {
                         faces.Add(new[] { vertexGrid[gridKey], vertexGrid[key2], vertexGrid[neighborKey], vertexGrid[key3] });
                    }
                }
            }
        }
        
        // Triangulate quads for the final mesh
        var triangles = new List<int[]>();
        foreach (var face in faces)
        {
            if (face.Length == 4)
            {
                triangles.Add(new[] { face[0], face[2], face[1] });
                triangles.Add(new[] { face[0], face[3], face[2] });
            }
        }

        var datasetName = $"Isosurface_{isovalue:F2}";
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{datasetName}.obj");
        
        return Mesh3DDataset.CreateFromData(datasetName, tempFilePath, vertices, triangles, 1.0f, "mm");
    }

    /// <summary>
    /// Generates 2D isocontours (lines) from a 2D scalar field for a given isovalue.
    /// </summary>
    /// <param name="scalarField">The 2D array of scalar values.</param>
    /// <param name="isovalue">The threshold value to generate the lines at.</param>
    /// <returns>A list of line segments, where each segment is a tuple of two Vector2 points.</returns>
    public static List<(Vector2, Vector2)> GenerateIsocontours(float[,] scalarField, float isovalue)
    {
        var lines = new List<(Vector2, Vector2)>();
        int dimX = scalarField.GetLength(0);
        int dimY = scalarField.GetLength(1);
        
        for (int y = 0; y < dimY - 1; y++)
        for (int x = 0; x < dimX - 1; x++)
        {
            var cellCorners = new Vector2[] { new(x, y), new(x + 1, y), new(x + 1, y + 1), new(x, y + 1) };
            var cellValues = new float[]
            {
                scalarField[x, y],
                scalarField[x + 1, y],
                scalarField[x + 1, y + 1],
                scalarField[x, y + 1]
            };

            int squareCase = 0;
            if (cellValues[0] < isovalue) squareCase |= 1;
            if (cellValues[1] < isovalue) squareCase |= 2;
            if (cellValues[2] < isovalue) squareCase |= 4;
            if (cellValues[3] < isovalue) squareCase |= 8;
            
            var linePoints = new List<Vector2>();

            void AddIntersection(int c1, int c2)
            {
                float v1 = cellValues[c1];
                float v2 = cellValues[c2];
                if ((v1 < isovalue) != (v2 < isovalue))
                {
                    float t = (isovalue - v1) / (v2 - v1);
                    linePoints.Add(Vector2.Lerp(cellCorners[c1], cellCorners[c2], t));
                }
            }
            
            AddIntersection(0, 1);
            AddIntersection(1, 2);
            AddIntersection(2, 3);
            AddIntersection(3, 0);

            if (linePoints.Count >= 2)
            {
                lines.Add((linePoints[0], linePoints[1]));
            }
            if (linePoints.Count == 4) // Ambiguous case, connect opposite pairs
            {
                 lines.Add((linePoints[2], linePoints[3]));
            }
        }

        return lines;
    }
}