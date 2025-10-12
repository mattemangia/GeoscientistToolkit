// GeoscientistToolkit/Analysis/IsosurfaceGenerator.cs

using System.Numerics;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.VolumeData; // Added for ILabelVolumeData

namespace GeoscientistToolkit.Analysis;

/// <summary>
///     Generates 3D isosurfaces and 2D isocontours from scalar field data using the Surface Nets algorithm.
///     This method is an alternative to Marching Cubes and often produces meshes with better element quality.
/// </summary>
public static class IsosurfaceGenerator
{
    // Edge connections for a cube. 12 edges total.
    private static readonly int[,] _cubeEdges =
    {
        { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 }, { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
        { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
    };

    // Voxel corner coordinates relative to the origin (0,0,0)
    private static readonly Vector3[] _cubeCorners =
    {
        new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
        new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)
    };

    /// <summary>
    ///     Generates a 3D mesh (isosurface) from a 3D scalar field for a given isovalue.
    ///     This operation is performed asynchronously to avoid blocking the UI thread.
    ///     MODIFIED: Now requires labelData to exclude voxels with material ID 0.
    /// </summary>
    /// <param name="scalarField">The 3D array of scalar values.</param>
    /// <param name="labelData">The 3D volume of material IDs. Voxels with ID 0 will be excluded.</param>
    /// <param name="isovalue">The threshold value to generate the surface at.</param>
    /// <param name="voxelSize">The physical size of a voxel for scaling.</param>
    /// <param name="progress">A progress reporter for updating the UI.</param>
    /// <param name="token">A cancellation token to stop the operation.</param>
    /// <returns>A Task that resolves to a Mesh3DDataset containing the generated surface mesh.</returns>
    public static async Task<Mesh3DDataset> GenerateIsosurfaceAsync(
        float[,,] scalarField,
        ILabelVolumeData labelData, // MODIFIED: Added labelData parameter
        float isovalue,
        Vector3 voxelSize,
        IProgress<(float progress, string message)> progress,
        CancellationToken token)
    {
        var dimX = scalarField.GetLength(0);
        var dimY = scalarField.GetLength(1);
        var dimZ = scalarField.GetLength(2);

        // Run the core, CPU-intensive logic on a background thread
        var (vertices, faces) = await Task.Run(() =>
        {
            progress?.Report((0.0f, "Initializing..."));

            var localVertices = new List<Vector3>();
            var localFaces = new List<int[]>();
            var vertexGrid = new Dictionary<long, int>();

            var values = new float[8];
            var corners = new Vector3[8];

            // Main loop through the volume
            for (var z = 0; z < dimZ - 1; z++)
            {
                // Check for cancellation at the start of each slice
                token.ThrowIfCancellationRequested();

                // Report progress periodically
                if (z % 10 == 0 || z == dimZ - 2)
                {
                    var p = (float)z / (dimZ - 1);
                    progress?.Report((p * 0.9f, $"Processing slice {z}/{dimZ - 1}..."));
                }

                for (var y = 0; y < dimY - 1; y++)
                for (var x = 0; x < dimX - 1; x++)
                {
                    // --- MODIFICATION START: Check for void voxels (Material ID 0) ---
                    // A cube is skipped if any of its 8 corners are in a void region.
                    // This prevents generating surfaces in or connected to excluded areas.
                    bool containsVoid = false;
                    for (int i = 0; i < 8; i++)
                    {
                        var cornerPos = new Vector3(x, y, z) + _cubeCorners[i];
                        if (labelData[(int)cornerPos.X, (int)cornerPos.Y, (int)cornerPos.Z] == 0)
                        {
                            containsVoid = true;
                            break;
                        }
                    }
                    if (containsVoid)
                    {
                        continue; // Skip this cube entirely
                    }
                    // --- MODIFICATION END ---
                    
                    var cubeCase = 0;
                    for (var i = 0; i < 8; i++)
                    {
                        var cornerPos = new Vector3(x, y, z) + _cubeCorners[i];
                        values[i] = scalarField[(int)cornerPos.X, (int)cornerPos.Y, (int)cornerPos.Z];
                        corners[i] = cornerPos;
                        if (values[i] < isovalue) cubeCase |= 1 << i;
                    }

                    if (cubeCase == 0 || cubeCase == 255) continue;

                    var surfacePoint = Vector3.Zero;
                    var edgeCount = 0;
                    for (var i = 0; i < 12; i++)
                    {
                        var c1 = _cubeEdges[i, 0];
                        var c2 = _cubeEdges[i, 1];

                        if (((cubeCase >> c1) & 1) != ((cubeCase >> c2) & 1))
                        {
                            var t = (isovalue - values[c1]) / (values[c2] - values[c1]);
                            surfacePoint += Vector3.Lerp(corners[c1], corners[c2], t);
                            edgeCount++;
                        }
                    }

                    if (edgeCount > 0) surfacePoint /= edgeCount;

                    var gridKey = x + (long)y * dimX + (long)z * dimX * dimY;
                    vertexGrid[gridKey] = localVertices.Count;
                    localVertices.Add(surfacePoint * voxelSize);

                    // --- CORRECTED FACE GENERATION LOGIC ---
                    // Generate faces by connecting to already processed voxels in negative directions.

                    // Quad on the XY plane (connects along -X and -Y)
                    if (x > 0 && y > 0)
                    {
                        var key_xm = x - 1 + (long)y * dimX + (long)z * dimX * dimY;
                        var key_ym = x + (long)(y - 1) * dimX + (long)z * dimX * dimY;
                        var key_xm_ym = x - 1 + (long)(y - 1) * dimX + (long)z * dimX * dimY;
                        if (vertexGrid.TryGetValue(key_xm, out var v_xm) &&
                            vertexGrid.TryGetValue(key_ym, out var v_ym) &&
                            vertexGrid.TryGetValue(key_xm_ym, out var v_xm_ym))
                            localFaces.Add(new[] { vertexGrid[gridKey], v_ym, v_xm_ym, v_xm });
                    }

                    // Quad on the XZ plane (connects along -X and -Z)
                    if (x > 0 && z > 0)
                    {
                        var key_xm = x - 1 + (long)y * dimX + (long)z * dimX * dimY;
                        var key_zm = x + (long)y * dimX + (long)(z - 1) * dimX * dimY;
                        var key_xm_zm = x - 1 + (long)y * dimX + (long)(z - 1) * dimX * dimY;
                        if (vertexGrid.TryGetValue(key_xm, out var v_xm) &&
                            vertexGrid.TryGetValue(key_zm, out var v_zm) &&
                            vertexGrid.TryGetValue(key_xm_zm, out var v_xm_zm))
                            localFaces.Add(new[] { vertexGrid[gridKey], v_xm, v_xm_zm, v_zm });
                    }

                    // Quad on the YZ plane (connects along -Y and -Z)
                    if (y > 0 && z > 0)
                    {
                        var key_ym = x + (long)(y - 1) * dimX + (long)z * dimX * dimY;
                        var key_zm = x + (long)y * dimX + (long)(z - 1) * dimX * dimY;
                        var key_ym_zm = x + (long)(y - 1) * dimX + (long)(z - 1) * dimX * dimY;
                        if (vertexGrid.TryGetValue(key_ym, out var v_ym) &&
                            vertexGrid.TryGetValue(key_zm, out var v_zm) &&
                            vertexGrid.TryGetValue(key_ym_zm, out var v_ym_zm))
                            localFaces.Add(new[] { vertexGrid[gridKey], v_zm, v_ym_zm, v_ym });
                    }
                }
            }

            return (localVertices, localFaces);
        }, token);

        token.ThrowIfCancellationRequested();
        progress?.Report((0.9f, "Triangulating faces..."));

        var triangles = new List<int[]>();
        foreach (var face in faces)
            if (face.Length == 4)
            {
                triangles.Add(new[] { face[0], face[2], face[1] });
                triangles.Add(new[] { face[0], face[3], face[2] });
            }

        progress?.Report((0.95f, "Creating mesh dataset..."));

        var datasetName = $"Isosurface_{isovalue:F2}";
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{datasetName}.obj");

        var dataset = Mesh3DDataset.CreateFromData(datasetName, tempFilePath, vertices, triangles, 1.0f, "mm");

        progress?.Report((1.0f, "Mesh generation complete."));
        return dataset;
    }

    /// <summary>
    ///     Generates 2D isocontours (lines) from a 2D scalar field for a given isovalue.
    ///     MODIFIED: Now requires labelSlice to exclude pixels with material ID 0.
    /// </summary>
    /// <param name="scalarField">The 2D array of scalar values.</param>
    /// <param name="labelSlice">The 2D array of material IDs. Pixels with ID 0 will be excluded.</param>
    /// <param name="isovalue">The threshold value to generate the lines at.</param>
    /// <returns>A list of line segments, where each segment is a tuple of two Vector2 points.</returns>
    public static List<(Vector2, Vector2)> GenerateIsocontours(float[,] scalarField, byte[,] labelSlice, float isovalue)
    {
        var lines = new List<(Vector2, Vector2)>();
        var dimX = scalarField.GetLength(0);
        var dimY = scalarField.GetLength(1);

        for (var y = 0; y < dimY - 1; y++)
        for (var x = 0; x < dimX - 1; x++)
        {
            // --- MODIFICATION START: Check for void pixels (Material ID 0) ---
            // Skip this cell if any of its 4 corners are in a void.
            if (labelSlice[x, y] == 0 ||
                labelSlice[x + 1, y] == 0 ||
                labelSlice[x + 1, y + 1] == 0 ||
                labelSlice[x, y + 1] == 0)
            {
                continue;
            }
            // --- MODIFICATION END ---
            
            var cellCorners = new Vector2[] { new(x, y), new(x + 1, y), new(x + 1, y + 1), new(x, y + 1) };
            var cellValues = new[]
            {
                scalarField[x, y],
                scalarField[x + 1, y],
                scalarField[x + 1, y + 1],
                scalarField[x, y + 1]
            };

            var squareCase = 0;
            if (cellValues[0] < isovalue) squareCase |= 1;
            if (cellValues[1] < isovalue) squareCase |= 2;
            if (cellValues[2] < isovalue) squareCase |= 4;
            if (cellValues[3] < isovalue) squareCase |= 8;

            var linePoints = new List<Vector2>();

            void AddIntersection(int c1, int c2)
            {
                var v1 = cellValues[c1];
                var v2 = cellValues[c2];
                if (v1 < isovalue != v2 < isovalue)
                {
                    var t = (isovalue - v1) / (v2 - v1);
                    linePoints.Add(Vector2.Lerp(cellCorners[c1], cellCorners[c2], t));
                }
            }

            AddIntersection(0, 1);
            AddIntersection(1, 2);
            AddIntersection(2, 3);
            AddIntersection(3, 0);

            if (linePoints.Count >= 2) lines.Add((linePoints[0], linePoints[1]));
            if (linePoints.Count == 4) // Ambiguous case, connect opposite pairs
                lines.Add((linePoints[2], linePoints[3]));
        }

        return lines;
    }
}