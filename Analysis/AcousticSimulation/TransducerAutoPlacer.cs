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
    /// by finding the largest connected component of a material.
    /// </summary>
    public class TransducerAutoPlacer
    {
        private readonly CtImageStackDataset _dataset;
        private readonly byte _materialId;
        private readonly int _width, _height, _depth;

        public TransducerAutoPlacer(CtImageStackDataset dataset, byte materialId)
        {
            _dataset = dataset;
            _materialId = materialId;
            _width = dataset.Width;
            _height = dataset.Height;
            _depth = dataset.Depth;
        }

        /// <summary>
        /// Finds the largest connected component (island) of the selected material
        /// and returns its bounding box.
        /// </summary>
        /// <returns>A tuple containing the min and max voxel coordinates of the largest island, or null if not found.</returns>
        public (Vector3 min, Vector3 max)? FindLargestIslandAndBounds()
        {
            Logger.Log($"[AutoPlace] Starting search for largest island of material ID {_materialId}");
            
            // Find all connected components and their sizes
            var components = FindAllConnectedComponents();
            
            if (components.Count == 0)
            {
                Logger.LogWarning($"[AutoPlace] No connected components found for material ID {_materialId}.");
                return null;
            }

            // Find the largest component
            var largestComponent = components.OrderByDescending(c => c.Voxels.Count).First();
            Logger.Log($"[AutoPlace] Found {components.Count} islands. Largest has {largestComponent.Voxels.Count} voxels.");
            
            // Calculate bounds for the largest component
            return CalculateBounds(largestComponent);
        }

        private List<ConnectedComponent> FindAllConnectedComponents()
        {
            var components = new List<ConnectedComponent>();
            var visited = new bool[_width, _height, _depth];
            
            // Scan through the entire volume
            for (int z = 0; z < _depth; z++)
            {
                var labelSlice = new byte[_width * _height];
                _dataset.LabelData.ReadSliceZ(z, labelSlice);
                
                for (int y = 0; y < _height; y++)
                {
                    for (int x = 0; x < _width; x++)
                    {
                        int sliceIdx = y * _width + x;
                        if (labelSlice[sliceIdx] == _materialId && !visited[x, y, z])
                        {
                            // Start a new component from this voxel
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
            
            // 6-connectivity directions (face neighbors only)
            int[] dx = { 1, -1, 0, 0, 0, 0 };
            int[] dy = { 0, 0, 1, -1, 0, 0 };
            int[] dz = { 0, 0, 0, 0, 1, -1 };
            
            while (queue.Count > 0)
            {
                var (x, y, z) = queue.Dequeue();
                component.Voxels.Add(new Vector3(x, y, z));
                
                // Check all 6 neighbors
                for (int i = 0; i < 6; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];
                    int nz = z + dz[i];
                    
                    // Check bounds
                    if (nx >= 0 && nx < _width && 
                        ny >= 0 && ny < _height && 
                        nz >= 0 && nz < _depth && 
                        !visited[nx, ny, nz])
                    {
                        // Check if this voxel has the target material
                        if (_dataset.LabelData[nx, ny, nz] == _materialId)
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
            if (component.Voxels.Count == 0)
                return null;
            
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            
            foreach (var voxel in component.Voxels)
            {
                min = Vector3.Min(min, voxel);
                max = Vector3.Max(max, voxel);
            }
            
            Logger.Log($"[AutoPlace] Component bounds: Min({min.X:F0},{min.Y:F0},{min.Z:F0}) Max({max.X:F0},{max.Y:F0},{max.Z:F0})");
            
            return (min, max);
        }

        /// <summary>
        /// Verifies that there is a connected path between two points within the material.
        /// </summary>
        public bool HasPath(Vector3 start, Vector3 end)
        {
            var startVoxel = new Vector3Int((int)start.X, (int)start.Y, (int)start.Z);
            var endVoxel = new Vector3Int((int)end.X, (int)end.Y, (int)end.Z);
            
            // Simple A* pathfinding
            var openSet = new SortedSet<PathNode>(new PathNodeComparer());
            var closedSet = new HashSet<Vector3Int>();
            var startNode = new PathNode 
            { 
                Position = startVoxel, 
                G = 0, 
                H = Vector3Int.Distance(startVoxel, endVoxel),
                Parent = null
            };
            openSet.Add(startNode);
            
            while (openSet.Count > 0)
            {
                var current = openSet.Min;
                openSet.Remove(current);
                
                if (current.Position.Equals(endVoxel))
                    return true; // Path found
                
                closedSet.Add(current.Position);
                
                // Check 6 neighbors
                int[] dx = { 1, -1, 0, 0, 0, 0 };
                int[] dy = { 0, 0, 1, -1, 0, 0 };
                int[] dz = { 0, 0, 0, 0, 1, -1 };
                
                for (int i = 0; i < 6; i++)
                {
                    var neighbor = new Vector3Int(
                        current.Position.X + dx[i],
                        current.Position.Y + dy[i],
                        current.Position.Z + dz[i]
                    );
                    
                    // Check bounds and material
                    if (neighbor.X < 0 || neighbor.X >= _width ||
                        neighbor.Y < 0 || neighbor.Y >= _height ||
                        neighbor.Z < 0 || neighbor.Z >= _depth ||
                        closedSet.Contains(neighbor))
                        continue;
                    
                    if (_dataset.LabelData[neighbor.X, neighbor.Y, neighbor.Z] != _materialId)
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
            
            return false; // No path found
        }

        private class ConnectedComponent
        {
            public List<Vector3> Voxels { get; } = new List<Vector3>();
        }

        private struct Vector3Int : IEquatable<Vector3Int>
        {
            public int X, Y, Z;
            
            public Vector3Int(int x, int y, int z)
            {
                X = x; Y = y; Z = z;
            }
            
            public static float Distance(Vector3Int a, Vector3Int b)
            {
                int dx = a.X - b.X;
                int dy = a.Y - b.Y;
                int dz = a.Z - b.Z;
                return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            
            public bool Equals(Vector3Int other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is Vector3Int other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        }

        private class PathNode
        {
            public Vector3Int Position { get; set; }
            public float G { get; set; } // Cost from start
            public float H { get; set; } // Heuristic to end
            public float F => G + H; // Total score
            public PathNode Parent { get; set; }
        }

        private class PathNodeComparer : IComparer<PathNode>
        {
            public int Compare(PathNode x, PathNode y)
            {
                if (x == null || y == null) return 0;
                int result = x.F.CompareTo(y.F);
                if (result == 0)
                {
                    // Use position hash as tiebreaker for stable sorting
                    result = x.Position.GetHashCode().CompareTo(y.Position.GetHashCode());
                }
                return result;
            }
        }
    }
}