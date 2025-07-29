// GeoscientistToolkit/Util/StlExporter.cs
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace GeoscientistToolkit.Util
{
    /// <summary>
    /// Exports a 3D model from volume data to an STL file using a Greedy Meshing algorithm.
    /// This algorithm is designed to produce a mesh with a low polygon count by merging adjacent coplanar faces.
    /// </summary>
    public static class StlExporter
    {
        /// <summary>
        /// Asynchronously exports the visible portion of a dataset to an STL file.
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

                    var volume = dataset.LabelData; // Primarily use label data for materials
                    if (volume == null)
                    {
                        throw new InvalidOperationException("Label data is not loaded and is required for STL export.");
                    }

                    int width = volume.Width;
                    int height = volume.Height;
                    int depth = volume.Depth;
                    var mask = new bool[width * height * depth];

                    // Iterate over each of the 3 axes (X, Y, Z)
                    for (int d = 0; d < 3; ++d)
                    {
                        int u = (d + 1) % 3;
                        int v = (d + 2) % 3;

                        var x = new int[3];
                        var q = new int[3];
                        q[d] = 1;

                        float totalIterations = depth;
                        float currentIteration = 0;

                        // Sweep a plane across the volume for the current axis
                        for (x[d] = -1; x[d] < depth; ++x[d])
                        {
                            onProgress?.Invoke(0.1f + (0.8f * (d * totalIterations + currentIteration) / (totalIterations * 3)),
                                $"Processing Axis {d + 1}/3, Slice {x[d] + 2}/{depth + 1}");

                            int n = 0;
                            var sliceMask = new byte[width * height];

                            // Create a 2D mask of the surface on the current slice
                            for (x[v] = 0; x[v] < height; ++x[v])
                            {
                                for (x[u] = 0; x[u] < width; ++x[u])
                                {
                                    bool isVoxel1Solid = IsVoxelVisible(dataset, viewer, x[0], x[1], x[2]);
                                    bool isVoxel2Solid = IsVoxelVisible(dataset, viewer, x[0] + q[0], x[1] + q[1], x[2] + q[2]);

                                    sliceMask[n++] = (isVoxel1Solid != isVoxel2Solid)
                                        ? (isVoxel1Solid ? (byte)1 : (byte)2) // 1 for entering, 2 for exiting face
                                        : (byte)0;
                                }
                            }
                            currentIteration++;

                            n = 0;
                            // Generate quads from the 2D mask using a greedy approach
                            for (int j = 0; j < height; ++j)
                            {
                                for (int i = 0; i < width; )
                                {
                                    if (sliceMask[n] != 0)
                                    {
                                        int w, h;
                                        byte currentFace = sliceMask[n];

                                        // Calculate width of the quad
                                        for (w = 1; i + w < width && sliceMask[n + w] == currentFace; ++w) { }

                                        // Calculate height of the quad
                                        bool done = false;
                                        for (h = 1; j + h < height; ++h)
                                        {
                                            for (int k = 0; k < w; ++k)
                                            {
                                                if (sliceMask[n + k + h * width] != currentFace)
                                                {
                                                    done = true;
                                                    break;
                                                }
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
                                        Vector3 normal = new Vector3(q[0], q[1], q[2]);
                                        if (currentFace == 2) normal *= -1; // Flip normal for exiting faces

                                        var v1 = new Vector3(x[0], x[1], x[2]);
                                        var v2 = new Vector3(x[0] + du[0], x[1] + du[1], x[2] + du[2]);
                                        var v3 = new Vector3(x[0] + du[0] + dv[0], x[1] + du[1] + dv[1], x[2] + du[2] + dv[2]);
                                        var v4 = new Vector3(x[0] + dv[0], x[1] + dv[1], x[2] + dv[2]);
                                        
                                        vertices.Add(v1); vertices.Add(v2); vertices.Add(v3);
                                        normals.Add(normal); normals.Add(normal); normals.Add(normal);
                                        
                                        vertices.Add(v1); vertices.Add(v3); vertices.Add(v4);
                                        normals.Add(normal); normals.Add(normal); normals.Add(normal);
                                        
                                        // Zero out the mask for the area covered by the new quad
                                        for (int l = 0; l < h; ++l)
                                        {
                                            for (int k = 0; k < w; ++k)
                                            {
                                                sliceMask[n + k + l * width] = 0;
                                            }
                                        }
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
                        }
                    }

                    onProgress?.Invoke(0.95f, "Writing STL file...");
                    WriteStlFile(filePath, vertices, normals);
                    onProgress?.Invoke(1.0f, "Export complete!");
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
        /// Checks if a voxel at a given coordinate is visible based on dataset properties and viewer settings.
        /// </summary>
        private static bool IsVoxelVisible(CtImageStackDataset dataset, CtCombinedViewer viewer, int x, int y, int z)
        {
            int width = dataset.Width;
            int height = dataset.Height;
            int depth = dataset.Depth;

            // Boundary check
            if (x < 0 || y < 0 || z < 0 || x >= width || y >= height || z >= depth)
            {
                return false;
            }

            // Check material visibility
            byte materialId = dataset.LabelData[x, y, z];
            if (materialId == 0 || !viewer.GetMaterialVisibility(materialId))
            {
                return false;
            }

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
                if (volumeViewer.CutXEnabled && (pos.X - volumeViewer.CutXPosition) * (volumeViewer.CutXForward ? 1 : -1) > 0) return false;
                if (volumeViewer.CutYEnabled && (pos.Y - volumeViewer.CutYPosition) * (volumeViewer.CutYForward ? 1 : -1) > 0) return false;
                if (volumeViewer.CutZEnabled && (pos.Z - volumeViewer.CutZPosition) * (volumeViewer.CutZForward ? 1 : -1) > 0) return false;

                // Check arbitrary clipping planes
                foreach (var plane in volumeViewer.ClippingPlanes)
                {
                    if (plane.Enabled)
                    {
                        float planeDist = Vector3.Dot(pos - new Vector3(0.5f), plane.Normal) - (plane.Distance - 0.5f);
                        if (plane.Mirror ? planeDist < 0.0 : planeDist > 0.0)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Writes the mesh data to an STL file in ASCII format.
        /// </summary>
        private static void WriteStlFile(string filePath, List<Vector3> vertices, List<Vector3> normals)
        {
            var sb = new StringBuilder();
            sb.AppendLine("solid GeoscientistToolkitExport");

            for (int i = 0; i < vertices.Count; i += 3)
            {
                Vector3 n = normals[i];
                sb.AppendLine($"  facet normal {n.X} {n.Y} {n.Z}");
                sb.AppendLine("    outer loop");
                sb.AppendLine($"      vertex {vertices[i].X} {vertices[i].Y} {vertices[i].Z}");
                sb.AppendLine($"      vertex {vertices[i+1].X} {vertices[i+1].Y} {vertices[i+1].Z}");
                sb.AppendLine($"      vertex {vertices[i+2].X} {vertices[i+2].Y} {vertices[i+2].Z}");
                sb.AppendLine("    endloop");
                sb.AppendLine("  endfacet");
            }

            sb.AppendLine("endsolid GeoscientistToolkitExport");
            File.WriteAllText(filePath, sb.ToString());
        }
    }
}