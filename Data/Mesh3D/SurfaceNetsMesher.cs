// GeoscientistToolkit/Data/Mesh3D/SurfaceNetsMesher.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Mesh3D
{
    /// <summary>
    /// Creates a 3D mesh from volumetric label data using a parallelized Surface Nets algorithm.
    /// This method is robust, watertight, and preserves sharp features.
    /// </summary>
    public class SurfaceNetsMesher
    {
        // Edge axis constants for key generation
        private const int X_AXIS = 0;
        private const int Y_AXIS = 1;
        private const int Z_AXIS = 2;

        public async Task<(List<Vector3> vertices, List<int[]> faces)> GenerateMeshAsync(
            ChunkedLabelVolume labels,
            byte materialId,
            int downsamplingFactor,
            IProgress<(float progress, string message)> progress,
            CancellationToken token)
        {
            progress?.Report((0.0f, "Starting mesh generation..."));

            if (downsamplingFactor < 1) downsamplingFactor = 1;

            // Step 1: Find all unique vertices on material boundaries in parallel
            progress?.Report((0.05f, "Phase 1/3: Finding surface edge crossings..."));
            var edgeVertices = await FindEdgeVerticesAsync(labels, materialId, downsamplingFactor, token);

            if (edgeVertices.Count == 0)
            {
                Logger.LogWarning("No edge vertices found for the selected material. The material might be empty or not form a surface.");
                return (new List<Vector3>(), new List<int[]>());
            }
            token.ThrowIfCancellationRequested();

            // Step 2: Create a lookup from edge key to vertex index for fast access
            progress?.Report((0.4f, "Phase 2/3: Building vertex map..."));
            var vertexList = edgeVertices.Values.ToList();
            var vertexMap = edgeVertices.Keys.Select((k, i) => new { Key = k, Index = i }).ToDictionary(p => p.Key, p => p.Index);
            edgeVertices.Clear(); // Free memory
            token.ThrowIfCancellationRequested();

            // Step 3: Generate faces (quads) for each voxel on the boundary in parallel
            progress?.Report((0.5f, "Phase 3/3: Generating mesh faces..."));
            var faces = await GenerateFacesAsync(labels, materialId, downsamplingFactor, vertexMap, progress, token);
            token.ThrowIfCancellationRequested();

            progress?.Report((0.95f, "Finalizing mesh..."));
            return (vertexList, faces.ToList());
        }

        private Task<ConcurrentDictionary<long, Vector3>> FindEdgeVerticesAsync(
            ChunkedLabelVolume labels, byte materialId, int ds, CancellationToken token)
        {
            var edgeVertices = new ConcurrentDictionary<long, Vector3>();

            return (Task<ConcurrentDictionary<long, Vector3>>)Task.Run(() =>
            {
                Parallel.For(0, labels.Depth - ds, new ParallelOptions { CancellationToken = token }, z =>
                {
                    for (int y = 0; y < labels.Height - ds; y += ds)
                    {
                        for (int x = 0; x < labels.Width - ds; x += ds)
                        {
                            bool p = labels[x, y, z] == materialId;
                            
                            // Check against X+1 neighbor
                            if (p != (labels[x + ds, y, z] == materialId))
                            {
                                long key = GetEdgeKey(x, y, z, X_AXIS);
                                edgeVertices.TryAdd(key, new Vector3(x + ds / 2.0f, y, z));
                            }

                            // Check against Y+1 neighbor
                            if (p != (labels[x, y + ds, z] == materialId))
                            {
                                long key = GetEdgeKey(x, y, z, Y_AXIS);
                                edgeVertices.TryAdd(key, new Vector3(x, y + ds / 2.0f, z));
                            }

                            // Check against Z+1 neighbor
                            if (p != (labels[x, y, z + ds] == materialId))
                            {
                                long key = GetEdgeKey(x, y, z, Z_AXIS);
                                edgeVertices.TryAdd(key, new Vector3(x, y, z + ds / 2.0f));
                            }
                        }
                    }
                });
            }, token);
        }

        private Task<ConcurrentBag<int[]>> GenerateFacesAsync(
            ChunkedLabelVolume labels, byte materialId, int ds, Dictionary<long, int> vertexMap, 
            IProgress<(float progress, string message)> progress, CancellationToken token)
        {
            var faces = new ConcurrentBag<int[]>();
            long processedSlices = 0;

            return Task.Run(() =>
            {
                Parallel.For(ds, labels.Depth - ds, new ParallelOptions { CancellationToken = token }, z =>
                {
                    for (int y = ds; y < labels.Height - ds; y += ds)
                    {
                        for (int x = ds; x < labels.Width - ds; x += ds)
                        {
                            if (labels[x, y, z] != materialId)
                                continue;

                            // Check -X face
                            if (labels[x - ds, y, z] != materialId)
                            {
                                int v1 = vertexMap[GetEdgeKey(x - ds, y - ds, z - ds, Y_AXIS)];
                                int v2 = vertexMap[GetEdgeKey(x - ds, y,      z - ds, Y_AXIS)];
                                int v3 = vertexMap[GetEdgeKey(x - ds, y,      z,      Y_AXIS)];
                                int v4 = vertexMap[GetEdgeKey(x - ds, y - ds, z,      Y_AXIS)];
                                faces.Add(new[] { v1, v2, v3 });
                                faces.Add(new[] { v1, v3, v4 });
                            }

                            // Check -Y face
                            if (labels[x, y - ds, z] != materialId)
                            {
                                int v1 = vertexMap[GetEdgeKey(x - ds, y - ds, z - ds, X_AXIS)];
                                int v2 = vertexMap[GetEdgeKey(x,      y - ds, z - ds, X_AXIS)];
                                int v3 = vertexMap[GetEdgeKey(x,      y - ds, z,      X_AXIS)];
                                int v4 = vertexMap[GetEdgeKey(x - ds, y - ds, z,      X_AXIS)];
                                faces.Add(new[] { v4, v3, v2 });
                                faces.Add(new[] { v4, v2, v1 });
                            }

                            // Check -Z face
                            if (labels[x, y, z - ds] != materialId)
                            {
                                int v1 = vertexMap[GetEdgeKey(x - ds, y - ds, z - ds, Z_AXIS)];
                                int v2 = vertexMap[GetEdgeKey(x,      y - ds, z - ds, Z_AXIS)];
                                int v3 = vertexMap[GetEdgeKey(x,      y,      z - ds, Z_AXIS)];
                                int v4 = vertexMap[GetEdgeKey(x - ds, y,      z - ds, Z_AXIS)];
                                faces.Add(new[] { v1, v2, v3 });
                                faces.Add(new[] { v1, v3, v4 });
                            }
                        }
                    }

                    long currentSlice = Interlocked.Increment(ref processedSlices);
                    if (currentSlice % 20 == 0)
                    {
                        progress?.Report((0.5f + 0.45f * (currentSlice / (float)(labels.Depth - 2 * ds)), 
                            $"Generating mesh faces... ({currentSlice}/{labels.Depth - 2 * ds})"));
                    }
                });
                return faces;
            }, token);
        }

        /// <summary>
        /// Creates a unique 64-bit key for a voxel edge based on its minimum coordinate and axis.
        /// This ensures each edge has one and only one key, regardless of which neighbor finds it.
        /// Pack coordinates (20 bits each) and axis (2 bits) -> 62 bits total. Max dimension: 2^20 = ~1,000,000
        /// </summary>
        private long GetEdgeKey(int x, int y, int z, int axis)
        {
            return ((long)x << 42) | ((long)y << 22) | ((long)z << 2) | (long)axis;
        }
    }
}