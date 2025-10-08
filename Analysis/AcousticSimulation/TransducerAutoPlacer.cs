// GeoscientistToolkit/Analysis/AcousticSimulation/TransducerAutoPlacer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.AcousticSimulation
{
    /// <summary>
    /// Provides advanced logic for automatically placing transducers
    /// by finding the largest connected component of a set of materials.
    /// </summary>
    public class TransducerAutoPlacer
    {
        private readonly byte[,,] _labelData;
        private readonly ISet<byte> _materialIds;
        private readonly int _width, _height, _depth;
        private readonly BoundingBox _extent;
        private readonly CtImageStackDataset _originalDataset;
        /// <summary>
        /// Initializes a new instance of the TransducerAutoPlacer.
        /// </summary>
        /// <param name="dataset">The original dataset, used for pixel size metadata.</param>
        /// <param name="materialIds">A set of material IDs that are considered valid for placement and wave propagation.</param>
        public TransducerAutoPlacer(CtImageStackDataset originalDataset, ISet<byte> materialIds, BoundingBox extent, byte[,,] labelDataToSearch)
        {
            if (materialIds == null || !materialIds.Any())
            {
                throw new ArgumentException("At least one material ID must be provided.", nameof(materialIds));
            }
            if (labelDataToSearch == null)
            {
                throw new ArgumentNullException(nameof(labelDataToSearch), "Label data for searching cannot be null.");
            }

            _originalDataset = originalDataset;
            _materialIds = materialIds;
            _labelData = labelDataToSearch;
        
            // Set dimensions to the size of the data we are actually searching
            _width = labelDataToSearch.GetLength(0);
            _height = labelDataToSearch.GetLength(1);
            _depth = labelDataToSearch.GetLength(2);
        }

        /// <summary>
        /// Executes the full auto-placement process for a given propagation axis.
        /// </summary>
        /// <param name="axis">The propagation axis (0=X, 1=Y, 2=Z).</param>
        /// <returns>A tuple with normalized TX and RX positions, or null if placement fails.</returns>
        public (Vector3 tx, Vector3 rx)? PlaceTransducersForAxis(int axis)
        {
            var bounds = FindLargestIslandAndBounds();

            if (!bounds.HasValue)
            {
                Logger.LogError($"[AutoPlace] No valid material volume found for the selected material(s).");
                return null;
            }

            var (min, max) = bounds.Value;
            Logger.Log($"[AutoPlace] Largest connected component bounds: Min({min}), Max({max})");

            const int buffer = 3; // Place transducers 3 voxels inside the material bounds
            Vector3 txVoxel, rxVoxel;

            // Calculate initial placement based on axis
            switch (axis)
            {
                case 0: // X-Axis
                    {
                        float centerY = (min.Y + max.Y) / 2.0f;
                        float centerZ = (min.Z + max.Z) / 2.0f;
                        txVoxel = new Vector3(min.X + buffer, centerY, centerZ);
                        rxVoxel = new Vector3(max.X - buffer, centerY, centerZ);
                        if (rxVoxel.X - txVoxel.X < 10) { txVoxel.X = min.X + 1; rxVoxel.X = max.X - 1; }
                    }
                    break;
                case 1: // Y-Axis
                    {
                        float centerX = (min.X + max.X) / 2.0f;
                        float centerZ = (min.Z + max.Z) / 2.0f;
                        txVoxel = new Vector3(centerX, min.Y + buffer, centerZ);
                        rxVoxel = new Vector3(centerX, max.Y - buffer, centerZ);
                        if (rxVoxel.Y - txVoxel.Y < 10) { txVoxel.Y = min.Y + 1; rxVoxel.Y = max.Y - 1; }
                    }
                    break;
                case 2: // Z-Axis
                    {
                        float centerX = (min.X + max.X) / 2.0f;
                        float centerY = (min.Y + max.Y) / 2.0f;
                        txVoxel = new Vector3(centerX, centerY, min.Z + buffer);
                        rxVoxel = new Vector3(centerX, centerY, max.Z - buffer);
                        if (rxVoxel.Z - txVoxel.Z < 10) { txVoxel.Z = min.Z + 1; rxVoxel.Z = max.Z - 1; }
                    }
                    break;
                default:
                    return null;
            }

            // Verify and refine positions
            bool txValid = IsPositionInMaterial(txVoxel);
            bool rxValid = IsPositionInMaterial(rxVoxel);

            if (!txValid || !rxValid)
            {
                Logger.LogWarning($"[AutoPlace] Initial positions not in material. Searching for valid positions...");
                (txVoxel, rxVoxel) = FindValidTransducerPositions(min, max, axis);
                if (!IsPositionInMaterial(txVoxel) || !IsPositionInMaterial(rxVoxel))
                {
                    Logger.LogError($"[AutoPlace] Could not find valid positions within the selected material(s).");
                    return null;
                }
            }

            // Verify path and log results
            bool hasPath = HasPath(txVoxel, rxVoxel);
            if (!hasPath)
            {
                Logger.LogWarning($"[AutoPlace] No direct path found between TX and RX. The material(s) may be fragmented.");
            }

            // Normalize positions for the UI
            Vector3 txNormalized = new Vector3(
                Math.Clamp(txVoxel.X / _width, 0f, 1f),
                Math.Clamp(txVoxel.Y / _height, 0f, 1f),
                Math.Clamp(txVoxel.Z / _depth, 0f, 1f));

            Vector3 rxNormalized = new Vector3(
                Math.Clamp(rxVoxel.X / _width, 0f, 1f),
                Math.Clamp(rxVoxel.Y / _height, 0f, 1f),
                Math.Clamp(rxVoxel.Z / _depth, 0f, 1f));

            LogPlacementResults(txVoxel, rxVoxel, txNormalized, rxNormalized, hasPath);

            return (txNormalized, rxNormalized);
        }

        private void LogPlacementResults(Vector3 txVoxel, Vector3 rxVoxel, Vector3 txNorm, Vector3 rxNorm, bool hasPath)
        {
            float dx = (rxVoxel.X - txVoxel.X) * _originalDataset.PixelSize / 1000f;
            float dy = (rxVoxel.Y - txVoxel.Y) * _originalDataset.PixelSize / 1000f;
            float dz = (rxVoxel.Z - txVoxel.Z) * _originalDataset.SliceThickness / 1000f;
            float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

            Logger.Log($"[AutoPlace] Successfully placed transducers:");
            Logger.Log($"  TX: Voxel({txVoxel.X:F0},{txVoxel.Y:F0},{txVoxel.Z:F0}) -> Normalized({txNorm.X:F3},{txNorm.Y:F3},{txNorm.Z:F3})");
            Logger.Log($"  RX: Voxel({rxVoxel.X:F0},{rxVoxel.Y:F0},{rxVoxel.Z:F0}) -> Normalized({rxNorm.X:F3},{rxNorm.Y:F3},{rxNorm.Z:F3})");
            Logger.Log($"  Distance: {distance:F2} mm");
            Logger.Log($"  Path verified: {(hasPath ? "Yes" : "No (fragmented material)")}");
        }

        /// <summary>
        /// Finds the largest connected component (island) of the selected materials
        /// and returns its bounding box.
        /// </summary>
        /// <returns>A tuple containing the min and max voxel coordinates of the largest island, or null if not found.</returns>
        private (Vector3 min, Vector3 max)? FindLargestIslandAndBounds()
        {
            Logger.Log($"[AutoPlace] Starting search for largest island of material IDs [{string.Join(", ", _materialIds)}]");

            var components = FindAllConnectedComponents();

            if (components.Count == 0)
            {
                Logger.LogWarning($"[AutoPlace] No connected components found for the selected materials.");
                return null;
            }

            var largestComponent = components.OrderByDescending(c => c.Voxels.Count).First();
            Logger.Log($"[AutoPlace] Found {components.Count} islands. Largest has {largestComponent.Voxels.Count} voxels.");

            return CalculateBounds(largestComponent);
        }

        private List<ConnectedComponent> FindAllConnectedComponents()
        {
            var components = new List<ConnectedComponent>();
            var visited = new bool[_width, _height, _depth];

            for (int z = 0; z < _depth; z++)
            {
                for (int y = 0; y < _height; y++)
                {
                    for (int x = 0; x < _width; x++)
                    {
                        if (_materialIds.Contains(_labelData[x, y, z]) && !visited[x, y, z])
                        {
                            var component = FloodFill3D(x, y, z, visited);
                            if (component.Voxels.Count > 0)
                            {
                                components.Add(component);
                            }
                        }
                    }
                }
            }
            return components;
        }

        private ConnectedComponent FloodFill3D(int startX, int startY, int startZ, bool[,,] visited)
        {
            var component = new ConnectedComponent();
            var queue = new Queue<(int x, int y, int z)>();

            queue.Enqueue((startX, startY, startZ));
            visited[startX, startY, startZ] = true;

            int[] dx = { 1, -1, 0, 0, 0, 0 };
            int[] dy = { 0, 0, 1, -1, 0, 0 };
            int[] dz = { 0, 0, 0, 0, 1, -1 };

            while (queue.Count > 0)
            {
                var (x, y, z) = queue.Dequeue();
                component.Voxels.Add(new Vector3(x, y, z));

                for (int i = 0; i < 6; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];
                    int nz = z + dz[i];

                    if (nx >= 0 && nx < _width && ny >= 0 && ny < _height && nz >= 0 && nz < _depth && !visited[nx, ny, nz])
                    {
                        if (_materialIds.Contains(_labelData[nx, ny, nz]))
                        {
                            visited[nx, ny, nz] = true;
                            queue.Enqueue((nx, ny, nz));
                        }
                    }
                }
            }
            return component;
        }

        private (Vector3 min, Vector3 max)? CalculateBounds(ConnectedComponent component)
        {
            if (component.Voxels.Count == 0) return null;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var voxel in component.Voxels)
            {
                min = Vector3.Min(min, voxel);
                max = Vector3.Max(max, voxel);
            }
            return (min, max);
        }

        /// <summary>
        /// Verifies that there is a connected path between two points within the material set.
        /// </summary>
        private bool HasPath(Vector3 start, Vector3 end)
        {
            var startVoxel = new Vector3Int((int)start.X, (int)start.Y, (int)start.Z);
            var endVoxel = new Vector3Int((int)end.X, (int)end.Y, (int)end.Z);

            var openSet = new SortedSet<PathNode>(new PathNodeComparer());
            var closedSet = new HashSet<Vector3Int>();
            var startNode = new PathNode { Position = startVoxel, G = 0, H = Vector3Int.Distance(startVoxel, endVoxel), Parent = null };
            openSet.Add(startNode);

            while (openSet.Count > 0)
            {
                var current = openSet.Min;
                openSet.Remove(current);

                if (current.Position.Equals(endVoxel)) return true;

                closedSet.Add(current.Position);

                int[] dx = { 1, -1, 0, 0, 0, 0 };
                int[] dy = { 0, 0, 1, -1, 0, 0 };
                int[] dz = { 0, 0, 0, 0, 1, -1 };

                for (int i = 0; i < 6; i++)
                {
                    var neighbor = new Vector3Int(current.Position.X + dx[i], current.Position.Y + dy[i], current.Position.Z + dz[i]);

                    if (neighbor.X < 0 || neighbor.X >= _width || neighbor.Y < 0 || neighbor.Y >= _height || neighbor.Z < 0 || neighbor.Z >= _depth || closedSet.Contains(neighbor))
                        continue;
                    
                    if (!_materialIds.Contains(_labelData[neighbor.X, neighbor.Y, neighbor.Z]))
                        continue;

                    float g = current.G + 1;
                    float h = Vector3Int.Distance(neighbor, endVoxel);
                    
                    var existingNode = openSet.FirstOrDefault(n => n.Position.Equals(neighbor));
                    if (existingNode == null)
                    {
                        openSet.Add(new PathNode { Position = neighbor, G = g, H = h, Parent = current });
                    }
                    else if (g < existingNode.G)
                    {
                        openSet.Remove(existingNode);
                        existingNode.G = g;
                        existingNode.Parent = current;
                        openSet.Add(existingNode);
                    }
                }
            }
            return false;
        }

        private bool IsPositionInMaterial(Vector3 position)
        {
            int x = Math.Clamp((int)position.X, 0, _width - 1);
            int y = Math.Clamp((int)position.Y, 0, _height - 1);
            int z = Math.Clamp((int)position.Z, 0, _depth - 1);
            return _materialIds.Contains(_labelData[x, y, z]);
        }

        private (Vector3 tx, Vector3 rx) FindValidTransducerPositions(Vector3 min, Vector3 max, int axis)
        {
            Vector3 txPos = Vector3.Zero, rxPos = Vector3.Zero;

            switch (axis)
            {
                case 0: // X-axis
                    {
                        float centerY = (min.Y + max.Y) / 2.0f; float centerZ = (min.Z + max.Z) / 2.0f;
                        for (int x = (int)min.X; x <= (int)max.X; x++) { if (IsPositionInMaterial(new Vector3(x, centerY, centerZ))) { txPos = new Vector3(x, centerY, centerZ); break; } }
                        for (int x = (int)max.X; x >= (int)min.X; x--) { if (IsPositionInMaterial(new Vector3(x, centerY, centerZ))) { rxPos = new Vector3(x, centerY, centerZ); break; } }
                    }
                    break;
                case 1: // Y-axis
                    {
                        float centerX = (min.X + max.X) / 2.0f; float centerZ = (min.Z + max.Z) / 2.0f;
                        for (int y = (int)min.Y; y <= (int)max.Y; y++) { if (IsPositionInMaterial(new Vector3(centerX, y, centerZ))) { txPos = new Vector3(centerX, y, centerZ); break; } }
                        for (int y = (int)max.Y; y >= (int)min.Y; y--) { if (IsPositionInMaterial(new Vector3(centerX, y, centerZ))) { rxPos = new Vector3(centerX, y, centerZ); break; } }
                    }
                    break;
                case 2: // Z-axis
                    {
                        float centerX = (min.X + max.X) / 2.0f; float centerY = (min.Y + max.Y) / 2.0f;
                        for (int z = (int)min.Z; z <= (int)max.Z; z++) { if (IsPositionInMaterial(new Vector3(centerX, centerY, z))) { txPos = new Vector3(centerX, centerY, z); break; } }
                        for (int z = (int)max.Z; z >= (int)min.Z; z--) { if (IsPositionInMaterial(new Vector3(centerX, centerY, z))) { rxPos = new Vector3(centerX, centerY, z); break; } }
                    }
                    break;
            }
            return (txPos, rxPos);
        }

        private class ConnectedComponent
        {
            public List<Vector3> Voxels { get; } = new List<Vector3>();
        }

        private struct Vector3Int : IEquatable<Vector3Int>
        {
            public int X, Y, Z;
            public Vector3Int(int x, int y, int z) { X = x; Y = y; Z = z; }
            public static float Distance(Vector3Int a, Vector3Int b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
            public bool Equals(Vector3Int other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is Vector3Int other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        }

        private class PathNode
        {
            public Vector3Int Position { get; set; }
            public float G { get; set; }
            public float H { get; set; }
            public float F => G + H;
            public PathNode Parent { get; set; }
        }

        private class PathNodeComparer : IComparer<PathNode>
        {
            public int Compare(PathNode x, PathNode y)
            {
                if (x == null || y == null) return 0;
                int result = x.F.CompareTo(y.F);
                if (result == 0) result = x.Position.GetHashCode().CompareTo(y.Position.GetHashCode());
                return result;
            }
        }
    }
}