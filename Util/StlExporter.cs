// GeoscientistToolkit/Util/StlExporter.cs

using System.Globalization;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Mesh3D;

namespace GeoscientistToolkit.Util;

/// <summary>
///     Exports a 3D model from volume data to an STL file using a Greedy Meshing algorithm.
///     This algorithm is designed to produce a mesh with a low polygon count by merging adjacent coplanar faces.
/// </summary>
public static class StlExporter
{
    /// <summary>
    ///     Asynchronously exports the visible portion of a dataset to an STL file.
    /// </summary>
    /// <param name="dataset">The dataset containing the full-resolution volume data.</param>
    /// <param name="viewer">The combined viewer instance containing the visibility and clipping settings.</param>
    /// <param name="filePath">The path to save the STL file.</param>
    /// <param name="onProgress">An action to report progress updates.</param>
    public static async Task ExportVisibleToStlAsync(
        CtImageStackDataset dataset,
        CtCombinedViewer viewer,
        string filePath,
        Action<float, string> onProgress)
    {
        await Task.Run(() =>
        {
            try
            {
                onProgress?.Invoke(0.0f, "Initializing export...");

                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();

                var volume = dataset.LabelData;
                if (volume == null)
                    throw new InvalidOperationException("Label data is not loaded and is required for STL export.");

                var width = volume.Width;
                var height = volume.Height;
                var depth = volume.Depth;

                Logger.Log($"[StlExporter] Starting export: Volume dimensions {width}x{height}x{depth}");

                // Count visible materials
                var visibleMaterials = dataset.Materials
                    .Where(m => m.ID != 0 && viewer.GetMaterialVisibility(m.ID))
                    .ToList();

                Logger.Log($"[StlExporter] Found {visibleMaterials.Count} visible materials");
                if (visibleMaterials.Count == 0)
                    throw new InvalidOperationException(
                        "No visible materials found. Please ensure at least one material is visible.");

                // Iterate over each of the 3 axes (X, Y, Z)
                for (var d = 0; d < 3; ++d)
                {
                    var u = (d + 1) % 3;
                    var v = (d + 2) % 3;

                    var x = new int[3];
                    var q = new int[3];
                    q[d] = 1;

                    // Get the size along each axis
                    int[] dims = { width, height, depth };
                    var maxD = dims[d];
                    var maxU = dims[u];
                    var maxV = dims[v];

                    float totalIterations = maxD + 1;
                    float currentIteration = 0;

                    // Sweep a plane across the volume for the current axis
                    for (x[d] = -1; x[d] < maxD; ++x[d])
                    {
                        onProgress?.Invoke(
                            0.1f + 0.8f * (d * totalIterations + currentIteration) / (totalIterations * 3),
                            $"Processing Axis {d + 1}/3, Slice {x[d] + 2}/{maxD + 1}");

                        var n = 0;
                        var sliceMask = new byte[maxU * maxV];

                        // Create a 2D mask of the surface on the current slice
                        for (x[v] = 0; x[v] < maxV; ++x[v])
                        for (x[u] = 0; x[u] < maxU; ++x[u])
                        {
                            var isVoxel1Solid = IsVoxelVisible(dataset, viewer, x[0], x[1], x[2]);
                            var isVoxel2Solid = IsVoxelVisible(dataset, viewer, x[0] + q[0], x[1] + q[1], x[2] + q[2]);

                            sliceMask[n++] = isVoxel1Solid != isVoxel2Solid
                                ? isVoxel1Solid ? (byte)1 : (byte)2 // 1 for entering, 2 for exiting face
                                : (byte)0;
                        }

                        currentIteration++;

                        n = 0;
                        // Generate quads from the 2D mask using a greedy approach
                        for (var j = 0; j < maxV; ++j)
                        for (var i = 0; i < maxU;)
                            if (sliceMask[n] != 0)
                            {
                                int w, h;
                                var currentFace = sliceMask[n];

                                // Calculate width of the quad
                                for (w = 1; i + w < maxU && sliceMask[n + w] == currentFace; ++w)
                                {
                                }

                                // Calculate height of the quad
                                var done = false;
                                for (h = 1; j + h < maxV; ++h)
                                {
                                    for (var k = 0; k < w; ++k)
                                        if (sliceMask[n + k + h * maxU] != currentFace)
                                        {
                                            done = true;
                                            break;
                                        }

                                    if (done) break;
                                }

                                x[u] = i;
                                x[v] = j;

                                var du = new int[3];
                                var dv = new int[3];
                                du[u] = w;
                                dv[v] = h;

                                // Add the two triangles that form the quad
                                var normal = new Vector3(q[0], q[1], q[2]);
                                if (currentFace == 2) normal *= -1; // Flip normal for exiting faces

                                var v1 = new Vector3(x[0], x[1], x[2]);
                                var v2 = new Vector3(x[0] + du[0], x[1] + du[1], x[2] + du[2]);
                                var v3 = new Vector3(x[0] + du[0] + dv[0], x[1] + du[1] + dv[1], x[2] + du[2] + dv[2]);
                                var v4 = new Vector3(x[0] + dv[0], x[1] + dv[1], x[2] + dv[2]);

                                // First triangle
                                vertices.Add(v1);
                                vertices.Add(v2);
                                vertices.Add(v3);
                                normals.Add(normal);
                                normals.Add(normal);
                                normals.Add(normal);

                                // Second triangle
                                vertices.Add(v1);
                                vertices.Add(v3);
                                vertices.Add(v4);
                                normals.Add(normal);
                                normals.Add(normal);
                                normals.Add(normal);

                                // Zero out the mask for the area covered by the new quad
                                for (var l = 0; l < h; ++l)
                                for (var k = 0; k < w; ++k)
                                    sliceMask[n + k + l * maxU] = 0;

                                i += w;
                                n += w;
                            }
                            else
                            {
                                i++;
                                n++;
                            }
                    }
                }

                Logger.Log($"[StlExporter] Generated {vertices.Count / 3} triangles");

                if (vertices.Count == 0)
                    throw new InvalidOperationException(
                        "No triangles were generated. Check material visibility and clipping planes.");

                onProgress?.Invoke(0.95f, "Writing STL file...");
                WriteStlFile(filePath, vertices, normals, dataset.PixelSize);
                onProgress?.Invoke(1.0f, $"Export complete! {vertices.Count / 3} triangles exported.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[StlExporter] Export failed: {ex.Message}");
                onProgress?.Invoke(1.0f, $"Error: {ex.Message}");
                throw;
            }
        });
    }

    /// <summary>
    ///     Checks if a voxel at a given coordinate is visible based on dataset properties and viewer settings.
    /// </summary>
    private static bool IsVoxelVisible(CtImageStackDataset dataset, CtCombinedViewer viewer, int x, int y, int z)
    {
        var width = dataset.Width;
        var height = dataset.Height;
        var depth = dataset.Depth;

        // Boundary check
        if (x < 0 || y < 0 || z < 0 || x >= width || y >= height || z >= depth) return false;

        // Check material visibility
        var materialId = dataset.LabelData[x, y, z];
        if (materialId == 0) // Exterior material
            return false;

        if (!viewer.GetMaterialVisibility(materialId)) return false;

        // Voxel position normalized to [0, 1] for clipping checks
        var pos = new Vector3(
            (float)x / (width - 1),
            (float)y / (height - 1),
            (float)z / (depth - 1)
        );

        var volumeViewer = viewer.VolumeViewer;
        if (volumeViewer != null)
        {
            // Check axis-aligned cutting planes
            if (volumeViewer.CutXEnabled &&
                (pos.X - volumeViewer.CutXPosition) * (volumeViewer.CutXForward ? 1 : -1) > 0) return false;
            if (volumeViewer.CutYEnabled &&
                (pos.Y - volumeViewer.CutYPosition) * (volumeViewer.CutYForward ? 1 : -1) > 0) return false;
            if (volumeViewer.CutZEnabled &&
                (pos.Z - volumeViewer.CutZPosition) * (volumeViewer.CutZForward ? 1 : -1) > 0) return false;

            // Check arbitrary clipping planes
            foreach (var plane in volumeViewer.ClippingPlanes)
                if (plane.Enabled)
                {
                    var planeDist = Vector3.Dot(pos - new Vector3(0.5f), plane.Normal) - (plane.Distance - 0.5f);
                    if (plane.Mirror ? planeDist < 0.0 : planeDist > 0.0) return false;
                }
        }

        return true;
    }

    /// <summary>
    ///     Writes the mesh data to an STL file in ASCII format.
    /// </summary>
    private static void WriteStlFile(string filePath, List<Vector3> vertices, List<Vector3> normals, float pixelSize)
    {
        var sb = new StringBuilder();
        sb.AppendLine("solid GeoscientistToolkitExport");

        // Convert voxel coordinates to physical units
        var scale = pixelSize / 1000.0f; // Convert to mm if pixelSize is in micrometers

        for (var i = 0; i < vertices.Count; i += 3)
        {
            var n = normals[i];
            var v1 = vertices[i] * scale;
            var v2 = vertices[i + 1] * scale;
            var v3 = vertices[i + 2] * scale;

            sb.AppendLine($"  facet normal {n.X:E6} {n.Y:E6} {n.Z:E6}");
            sb.AppendLine("    outer loop");
            sb.AppendLine($"      vertex {v1.X:E6} {v1.Y:E6} {v1.Z:E6}");
            sb.AppendLine($"      vertex {v2.X:E6} {v2.Y:E6} {v2.Z:E6}");
            sb.AppendLine($"      vertex {v3.X:E6} {v3.Y:E6} {v3.Z:E6}");
            sb.AppendLine("    endloop");
            sb.AppendLine("  endfacet");
        }

        sb.AppendLine("endsolid GeoscientistToolkitExport");
        File.WriteAllText(filePath, sb.ToString());

        Logger.Log($"[StlExporter] STL file written to: {filePath}");
    }
}

public static class MeshExporter
{
    /// <summary>
    ///     Exports a Mesh3DDataset to an ASCII STL file.
    /// </summary>
    /// <param name="mesh">The mesh dataset to export.</param>
    /// <param name="filePath">The path where the STL file will be saved.</param>
    public static void ExportToStl(Mesh3DDataset mesh, string filePath)
    {
        if (mesh == null || mesh.VertexCount == 0 || mesh.FaceCount == 0)
        {
            Logger.LogWarning("[MeshExporter] Mesh is null or empty, cannot export to STL.");
            return;
        }

        try
        {
            var sb = new StringBuilder();
            // Use InvariantCulture for consistent number formatting
            var culture = CultureInfo.InvariantCulture;

            sb.AppendLine($"solid {Path.GetFileNameWithoutExtension(filePath)}");

            foreach (var face in mesh.Faces)
            {
                if (face.Length < 3) continue; // Skip invalid faces

                // Assuming triangular faces
                var v1 = mesh.Vertices[face[0]];
                var v2 = mesh.Vertices[face[1]];
                var v3 = mesh.Vertices[face[2]];

                // Calculate the normal of the triangle
                var normal = Vector3.Normalize(Vector3.Cross(v2 - v1, v3 - v1));

                sb.AppendLine(
                    $"  facet normal {normal.X.ToString("E6", culture)} {normal.Y.ToString("E6", culture)} {normal.Z.ToString("E6", culture)}");
                sb.AppendLine("    outer loop");
                sb.AppendLine(
                    $"      vertex {v1.X.ToString("E6", culture)} {v1.Y.ToString("E6", culture)} {v1.Z.ToString("E6", culture)}");
                sb.AppendLine(
                    $"      vertex {v2.X.ToString("E6", culture)} {v2.Y.ToString("E6", culture)} {v2.Z.ToString("E6", culture)}");
                sb.AppendLine(
                    $"      vertex {v3.X.ToString("E6", culture)} {v3.Y.ToString("E6", culture)} {v3.Z.ToString("E6", culture)}");
                sb.AppendLine("    endloop");
                sb.AppendLine("  endfacet");
            }

            sb.AppendLine($"endsolid {Path.GetFileNameWithoutExtension(filePath)}");

            File.WriteAllText(filePath, sb.ToString());
            Logger.Log($"[MeshExporter] Successfully exported mesh '{mesh.Name}' to {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MeshExporter] Failed to export STL file: {ex.Message}");
        }
    }
}