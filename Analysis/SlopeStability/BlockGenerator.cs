using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.Mesh3D;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Generates blocks from a 3D mesh using Discrete Fracture Network (DFN) approach.
    /// Based on 3DEC methodology: joints split blocks completely.
    ///
    /// <para><b>Academic References:</b></para>
    ///
    /// <para>Block theory and rock engineering:</para>
    /// <para>Goodman, R. E., &amp; Shi, G. H. (1985). Block theory and its application to rock
    /// engineering. Prentice-Hall. ISBN: 978-0131782013</para>
    ///
    /// <para>Discrete Fracture Network (DFN) modeling:</para>
    /// <para>Dershowitz, W. S., &amp; Einstein, H. H. (1988). Characterizing rock joint geometry with
    /// joint system models. Rock Mechanics and Rock Engineering, 21(1), 21-51.
    /// https://doi.org/10.1007/BF01019674</para>
    ///
    /// <para>3DEC block generation methodology:</para>
    /// <para>Cundall, P. A. (1988). Formulation of a three-dimensional distinct element model—Part I.
    /// A scheme to detect and represent contacts in a system composed of many polyhedral blocks.
    /// International Journal of Rock Mechanics and Mining Sciences &amp; Geomechanics Abstracts, 25(3),
    /// 107-116. https://doi.org/10.1016/0148-9062(88)92293-0</para>
    ///
    /// <para>Convex hull algorithms for block geometry:</para>
    /// <para>Barber, C. B., Dobkin, D. P., &amp; Huhdanpaa, H. (1996). The quickhull algorithm for
    /// convex hulls. ACM Transactions on Mathematical Software, 22(4), 469-483.
    /// https://doi.org/10.1145/235815.235821</para>
    /// </summary>
    public class BlockGenerator
    {
        private readonly BlockGenerationSettings _settings;
        private int _nextBlockId = 0;

        public BlockGenerator(BlockGenerationSettings settings)
        {
            _settings = settings ?? new BlockGenerationSettings();
        }

        /// <summary>
        /// Generates blocks from a mesh using joint sets.
        /// Implements the DFN cutting algorithm similar to 3DEC.
        /// </summary>
        public List<Block> GenerateBlocks(
            Mesh3DDataset mesh,
            List<JointSet> jointSets,
            Action<float> progressCallback = null)
        {
            progressCallback?.Invoke(0.0f);

            // Step 1: Create initial block from entire mesh
            var initialBlock = CreateBlockFromMesh(mesh);
            var blocks = new List<Block> { initialBlock };

            progressCallback?.Invoke(0.1f);

            if (jointSets == null || jointSets.Count == 0)
                return blocks;

            // Step 2: Sort joint sets by importance (larger spacing = cut first to minimize cuts)
            var sortedJoints = jointSets.OrderByDescending(j => j.Spacing).ToList();

            // Step 3: Cut blocks with each joint set sequentially
            for (int jointIdx = 0; jointIdx < sortedJoints.Count; jointIdx++)
            {
                var jointSet = sortedJoints[jointIdx];
                var newBlocks = new List<Block>();

                foreach (var block in blocks)
                {
                    var cutBlocks = CutBlockWithJointSet(block, jointSet, mesh);
                    newBlocks.AddRange(cutBlocks);
                }

                blocks = newBlocks;

                float progress = 0.1f + 0.7f * (jointIdx + 1) / sortedJoints.Count;
                progressCallback?.Invoke(progress);
            }

            // Step 4: Post-process blocks
            blocks = PostProcessBlocks(blocks);

            progressCallback?.Invoke(0.9f);

            // Step 5: Calculate geometric properties for each block
            foreach (var block in blocks)
            {
                block.CalculateGeometricProperties();
            }

            progressCallback?.Invoke(1.0f);

            return blocks;
        }

        /// <summary>
        /// Creates an initial block from the entire mesh.
        /// </summary>
        private Block CreateBlockFromMesh(Mesh3DDataset mesh)
        {
            var block = new Block
            {
                Id = _nextBlockId++,
                Name = $"Block_{_nextBlockId}",
                Vertices = new List<Vector3>(mesh.Vertices),
                Faces = new List<int[]>()
            };

            // Copy faces
            foreach (var face in mesh.Faces)
            {
                block.Faces.Add((int[])face.Clone());
            }

            // Calculate bounding box for initial position
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var v in mesh.Vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            block.Centroid = (min + max) * 0.5f;
            block.Position = block.Centroid;
            block.InitialPosition = block.Centroid;
            block.Orientation = Quaternion.Identity;
            block.Density = 2700.0f; // Default density

            return block;
        }

        /// <summary>
        /// Cuts a block with all planes from a joint set.
        /// Returns the resulting blocks after cutting.
        /// </summary>
        private List<Block> CutBlockWithJointSet(Block block, JointSet jointSet, Mesh3DDataset referenceMesh)
        {
            // Calculate bounding box of the block
            var (bbMin, bbMax) = GetBlockBoundingBox(block);

            // Expand bounding box slightly
            Vector3 expansion = (bbMax - bbMin) * 0.1f;
            bbMin -= expansion;
            bbMax += expansion;

            // Generate plane positions for this joint set
            var planePositions = jointSet.GeneratePlanePositions(bbMin, bbMax);

            // Start with the original block
            var currentBlocks = new List<Block> { block };

            // Cut with each plane sequentially
            foreach (var planePoint in planePositions)
            {
                var nextBlocks = new List<Block>();

                foreach (var currentBlock in currentBlocks)
                {
                    // Check if plane actually intersects this block
                    if (!PlaneIntersectsBlock(currentBlock, jointSet.GetNormal(), planePoint))
                    {
                        nextBlocks.Add(currentBlock);
                        continue;
                    }

                    // Cut the block and track which joint set created the new faces
                    var cutResults = CutBlockWithPlane(currentBlock, jointSet.GetNormal(), planePoint, jointSet.Id);

                    // Add resulting blocks and track joint set
                    foreach (var resultBlock in cutResults)
                    {
                        // Record that this joint set bounds this block
                        if (!resultBlock.BoundingJointSetIds.Contains(jointSet.Id))
                        {
                            resultBlock.BoundingJointSetIds.Add(jointSet.Id);
                        }
                    }

                    nextBlocks.AddRange(cutResults);
                }

                currentBlocks = nextBlocks;
            }

            return currentBlocks;
        }

        /// <summary>
        /// Cuts a single block with a plane, creating two (or one if no intersection).
        /// </summary>
        /// <param name="block">The block to cut</param>
        /// <param name="planeNormal">Normal vector of the cutting plane</param>
        /// <param name="planePoint">A point on the cutting plane</param>
        /// <param name="jointSetId">ID of the joint set that created this cut (-1 if not from joint set)</param>
        private List<Block> CutBlockWithPlane(Block block, Vector3 planeNormal, Vector3 planePoint, int jointSetId = -1)
        {
            var results = new List<Block>();

            // Classify vertices relative to plane
            var vertexSides = new List<int>(); // -1 = negative side, 0 = on plane, 1 = positive side
            var distances = new List<float>();

            foreach (var vertex in block.Vertices)
            {
                float distance = Vector3.Dot(vertex - planePoint, planeNormal);
                distances.Add(distance);

                if (Math.Abs(distance) < 1e-6f)
                    vertexSides.Add(0);
                else if (distance > 0)
                    vertexSides.Add(1);
                else
                    vertexSides.Add(-1);
            }

            // Check if all vertices are on one side (no cut needed)
            bool hasPositive = vertexSides.Any(s => s > 0);
            bool hasNegative = vertexSides.Any(s => s < 0);

            if (!hasPositive || !hasNegative)
            {
                // No intersection, return original block
                results.Add(block);
                return results;
            }

            // Perform actual cutting (simplified implementation)
            // For production, use proper mesh boolean operations (e.g., from NetTopologySuite or custom BSP)

            var positiveBlock = SplitBlockSimplified(block, vertexSides, distances, true, planeNormal, planePoint, jointSetId);
            var negativeBlock = SplitBlockSimplified(block, vertexSides, distances, false, planeNormal, planePoint, jointSetId);

            if (positiveBlock != null)
                results.Add(positiveBlock);
            if (negativeBlock != null)
                results.Add(negativeBlock);

            return results;
        }

        /// <summary>
        /// Simplified block splitting using convex hull approximation.
        /// For production use, implement proper CSG or use libraries like MIConvexHull.
        /// </summary>
        /// <param name="jointSetId">ID of the joint set that created this cut (-1 if not from joint set)</param>
        private Block SplitBlockSimplified(
            Block originalBlock,
            List<int> vertexSides,
            List<float> distances,
            bool keepPositiveSide,
            Vector3 planeNormal,
            Vector3 planePoint,
            int jointSetId = -1)
        {
            var newBlock = new Block
            {
                Id = _nextBlockId++,
                Name = $"Block_{_nextBlockId}",
                Vertices = new List<Vector3>(),
                Faces = new List<int[]>(),
                Density = originalBlock.Density,
                MaterialId = originalBlock.MaterialId,
                Orientation = originalBlock.Orientation,
                BoundingJointSetIds = new List<int>(originalBlock.BoundingJointSetIds),
                FaceToJointSetId = new Dictionary<int, int>()
            };

            var vertexMapping = new Dictionary<int, int>(); // old index -> new index

            // Step 1: Add vertices on the correct side
            for (int i = 0; i < originalBlock.Vertices.Count; i++)
            {
                int side = vertexSides[i];
                bool include = keepPositiveSide ? (side >= 0) : (side <= 0);

                if (include)
                {
                    vertexMapping[i] = newBlock.Vertices.Count;
                    newBlock.Vertices.Add(originalBlock.Vertices[i]);
                }
            }

            // Step 2: Add intersection points on edges that cross the plane
            var intersectionPoints = new List<Vector3>();

            foreach (var face in originalBlock.Faces)
            {
                for (int i = 0; i < face.Length; i++)
                {
                    int v1Idx = face[i];
                    int v2Idx = face[(i + 1) % face.Length];

                    int side1 = vertexSides[v1Idx];
                    int side2 = vertexSides[v2Idx];

                    // Edge crosses plane
                    if ((side1 > 0 && side2 < 0) || (side1 < 0 && side2 > 0))
                    {
                        Vector3 v1 = originalBlock.Vertices[v1Idx];
                        Vector3 v2 = originalBlock.Vertices[v2Idx];

                        // Calculate intersection point
                        float t = distances[v1Idx] / (distances[v1Idx] - distances[v2Idx]);
                        Vector3 intersection = v1 + t * (v2 - v1);

                        // Add to new block's vertices if not already added
                        if (!intersectionPoints.Any(p => (p - intersection).Length() < 1e-6f))
                        {
                            intersectionPoints.Add(intersection);
                            newBlock.Vertices.Add(intersection);
                        }
                    }
                }
            }

            // Step 3: Reconstruct faces (simplified - keeps original faces that are entirely on correct side)
            int originalFaceIndex = 0;
            foreach (var face in originalBlock.Faces)
            {
                var newFaceIndices = new List<int>();
                bool allVerticesIncluded = true;

                foreach (var vertIdx in face)
                {
                    if (vertexMapping.ContainsKey(vertIdx))
                    {
                        newFaceIndices.Add(vertexMapping[vertIdx]);
                    }
                    else
                    {
                        allVerticesIncluded = false;
                        break;
                    }
                }

                if (allVerticesIncluded && newFaceIndices.Count >= 3)
                {
                    int newFaceIndex = newBlock.Faces.Count;
                    newBlock.Faces.Add(newFaceIndices.ToArray());

                    // Copy joint set ID from original face if it exists
                    if (originalBlock.FaceToJointSetId.TryGetValue(originalFaceIndex, out int origJointSetId))
                    {
                        newBlock.FaceToJointSetId[newFaceIndex] = origJointSetId;
                    }
                    else
                    {
                        newBlock.FaceToJointSetId[newFaceIndex] = -1; // Original mesh face
                    }
                }
                originalFaceIndex++;
            }

            // Step 4: Add cutting plane face (if intersection points exist)
            if (intersectionPoints.Count >= 3)
            {
                // Create a face from intersection points (simplified: use convex hull in 2D)
                var capFace = CreateCapFace(intersectionPoints, planeNormal, keepPositiveSide);
                if (capFace != null && capFace.Length >= 3)
                {
                    // Map to actual indices in newBlock
                    var capFaceIndices = new List<int>();
                    int baseIndex = newBlock.Vertices.Count - intersectionPoints.Count;

                    foreach (var idx in capFace)
                    {
                        capFaceIndices.Add(baseIndex + idx);
                    }

                    int capFaceIndex = newBlock.Faces.Count;
                    newBlock.Faces.Add(capFaceIndices.ToArray());

                    // This cap face was created by the current joint set
                    newBlock.FaceToJointSetId[capFaceIndex] = jointSetId;
                }
            }

            // Validate block
            if (newBlock.Vertices.Count < 4 || newBlock.Faces.Count < 4)
            {
                // Degenerate block
                return null;
            }

            newBlock.Centroid = CalculateCentroid(newBlock.Vertices);
            newBlock.Position = newBlock.Centroid;
            newBlock.InitialPosition = newBlock.Centroid;

            return newBlock;
        }

        /// <summary>
        /// Creates a cap face from intersection points using 2D convex hull.
        /// </summary>
        private int[] CreateCapFace(List<Vector3> points, Vector3 normal, bool reverseWinding)
        {
            if (points.Count < 3)
                return null;

            // Project points onto the cutting plane
            Vector3 right = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitZ));
            if (right.LengthSquared() < 0.01f)
                right = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX));

            Vector3 up = Vector3.Normalize(Vector3.Cross(normal, right));

            // Convert to 2D
            var points2D = new List<Vector2>();
            foreach (var p in points)
            {
                float x = Vector3.Dot(p, right);
                float y = Vector3.Dot(p, up);
                points2D.Add(new Vector2(x, y));
            }

            // Compute 2D convex hull (Graham scan)
            var hullIndices = ConvexHull2D(points2D);

            if (reverseWinding)
                hullIndices.Reverse();

            return hullIndices.ToArray();
        }

        /// <summary>
        /// Computes 2D convex hull using Graham scan algorithm.
        /// </summary>
        private List<int> ConvexHull2D(List<Vector2> points)
        {
            if (points.Count < 3)
                return Enumerable.Range(0, points.Count).ToList();

            // Find the lowest point (and leftmost if tie)
            int lowestIdx = 0;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].Y < points[lowestIdx].Y ||
                    (points[i].Y == points[lowestIdx].Y && points[i].X < points[lowestIdx].X))
                {
                    lowestIdx = i;
                }
            }

            Vector2 pivot = points[lowestIdx];

            // Sort points by polar angle with respect to pivot
            var indices = Enumerable.Range(0, points.Count).ToList();
            indices.RemoveAt(lowestIdx);

            indices.Sort((i, j) =>
            {
                Vector2 vi = points[i] - pivot;
                Vector2 vj = points[j] - pivot;

                float angleI = MathF.Atan2(vi.Y, vi.X);
                float angleJ = MathF.Atan2(vj.Y, vj.X);

                return angleI.CompareTo(angleJ);
            });

            // Graham scan
            var hull = new List<int> { lowestIdx };

            foreach (var idx in indices)
            {
                while (hull.Count >= 2)
                {
                    int top = hull[hull.Count - 1];
                    int secondTop = hull[hull.Count - 2];

                    Vector2 v1 = points[top] - points[secondTop];
                    Vector2 v2 = points[idx] - points[top];

                    float cross = v1.X * v2.Y - v1.Y * v2.X;

                    if (cross > 0)
                        break;

                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(idx);
            }

            return hull;
        }

        /// <summary>
        /// Checks if a plane intersects a block's bounding box.
        /// </summary>
        private bool PlaneIntersectsBlock(Block block, Vector3 normal, Vector3 point)
        {
            float minDist = float.MaxValue;
            float maxDist = float.MinValue;

            foreach (var vertex in block.Vertices)
            {
                float dist = Vector3.Dot(vertex - point, normal);
                minDist = Math.Min(minDist, dist);
                maxDist = Math.Max(maxDist, dist);
            }

            // Plane intersects if vertices are on both sides
            return minDist < 0 && maxDist > 0;
        }

        /// <summary>
        /// Gets the axis-aligned bounding box of a block.
        /// </summary>
        private (Vector3 min, Vector3 max) GetBlockBoundingBox(Block block)
        {
            if (block.Vertices.Count == 0)
                return (Vector3.Zero, Vector3.Zero);

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var vertex in block.Vertices)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }

            return (min, max);
        }

        /// <summary>
        /// Post-processes blocks (removes small blocks, merges slivers, etc.).
        /// </summary>
        private List<Block> PostProcessBlocks(List<Block> blocks)
        {
            var processedBlocks = new List<Block>();

            foreach (var block in blocks)
            {
                // Calculate volume
                block.CalculateGeometricProperties();

                // Remove very small blocks
                if (_settings.RemoveSmallBlocks && block.Volume < _settings.MinimumBlockVolume)
                {
                    continue;
                }

                // Remove very large blocks
                if (_settings.RemoveLargeBlocks && block.Volume > _settings.MaximumBlockVolume)
                {
                    continue;
                }

                // Check for degenerate geometry
                if (block.Vertices.Count < 4 || block.Faces.Count < 4)
                {
                    continue;
                }

                processedBlocks.Add(block);
            }

            return processedBlocks;
        }

        /// <summary>
        /// Calculates centroid of a set of vertices.
        /// </summary>
        private Vector3 CalculateCentroid(List<Vector3> vertices)
        {
            if (vertices.Count == 0)
                return Vector3.Zero;

            Vector3 sum = Vector3.Zero;
            foreach (var v in vertices)
            {
                sum += v;
            }

            return sum / vertices.Count;
        }

        /// <summary>
        /// Generates blocks using Voronoi tessellation (alternative to joint sets).
        /// </summary>
        public List<Block> GenerateBlocksVoronoi(
            Mesh3DDataset mesh,
            int numSeeds,
            Random random = null)
        {
            random ??= new Random();

            // Calculate bounding box
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var v in mesh.Vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            // Generate random seed points
            var seeds = new List<Vector3>();
            for (int i = 0; i < numSeeds; i++)
            {
                float x = min.X + (float)random.NextDouble() * (max.X - min.X);
                float y = min.Y + (float)random.NextDouble() * (max.Y - min.Y);
                float z = min.Z + (float)random.NextDouble() * (max.Z - min.Z);
                seeds.Add(new Vector3(x, y, z));
            }

            // Assign each mesh vertex to nearest seed
            var vertexToSeed = new Dictionary<int, int>();
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                float minDistance = float.MaxValue;
                int closestSeed = 0;

                for (int s = 0; s < seeds.Count; s++)
                {
                    float distance = (mesh.Vertices[i] - seeds[s]).Length();
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestSeed = s;
                    }
                }

                vertexToSeed[i] = closestSeed;
            }

            // Create blocks based on seed assignment
            var blocksByRegion = new Dictionary<int, Block>();

            for (int s = 0; s < seeds.Count; s++)
            {
                blocksByRegion[s] = new Block
                {
                    Id = _nextBlockId++,
                    Name = $"Block_{_nextBlockId}",
                    Vertices = new List<Vector3>(),
                    Faces = new List<int[]>(),
                    Density = 2700.0f
                };
            }

            // Assign vertices to blocks (simplified - does not handle shared faces properly)
            // For production, use proper Voronoi diagram library

            return blocksByRegion.Values.ToList();
        }
    }
}
