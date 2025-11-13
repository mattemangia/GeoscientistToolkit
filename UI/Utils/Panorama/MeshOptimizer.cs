// GeoscientistToolkit/Business/Photogrammetry/MeshOptimizer.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.Mesh3D;

namespace GeoscientistToolkit.Business.Photogrammetry
{
    /// <summary>
    /// Mesh optimization using algorithms inspired by Gmsh's optimization techniques
    /// References:
    /// - Geuzaine, C., & Remacle, J.F. (2009). Gmsh: A 3-D finite element mesh generator with built-in 
    ///   pre- and post-processing facilities. International Journal for Numerical Methods in Engineering, 79(11), 1309-1331.
    /// - Freitag, L.A., & Ollivier-Gooch, C. (1997). Tetrahedral mesh improvement using swapping and smoothing.
    ///   International Journal for Numerical Methods in Engineering, 40(21), 3979-4002.
    /// - Vartziotis, D., & Himpel, B. (2014). Laplacian smoothing revisited. arXiv:1406.4333
    /// </summary>
    public class MeshOptimizer
    {
        private readonly PhotogrammetryProcessingService _service;
        private const float EPSILON = 1e-8f;
        private const float MIN_ANGLE_THRESHOLD = 5.0f; // degrees
        private const float MAX_ANGLE_THRESHOLD = 175.0f; // degrees

        public MeshOptimizer(PhotogrammetryProcessingService service)
        {
            _service = service;
        }

        #region Public API

        /// <summary>
        /// Optimizes mesh quality using a combination of techniques
        /// </summary>
        public async Task<Mesh3DDataset> OptimizeMeshAsync(
            Mesh3DDataset inputMesh,
            MeshOptimizationOptions options,
            Action<float, string> updateProgress)
        {
            _service.Log("Starting mesh optimization...");
            updateProgress(0, "Analyzing mesh quality...");

            return await Task.Run(() =>
            {
                var optimizer = new MeshOptimizationContext(inputMesh, _service);
                
                // Initial quality assessment
                var initialQuality = ComputeOverallQuality(optimizer);
                _service.Log($"Initial mesh quality: min angle = {initialQuality.MinAngle:F2}°, " +
                           $"max angle = {initialQuality.MaxAngle:F2}°, " +
                           $"avg aspect ratio = {initialQuality.AvgAspectRatio:F3}");

                int totalIterations = options.MaxIterations;
                int currentIteration = 0;

                // Multi-pass optimization following Gmsh's approach
                for (int pass = 0; pass < options.OptimizationPasses; pass++)
                {
                    _service.Log($"Optimization pass {pass + 1}/{options.OptimizationPasses}");

                    // Phase 1: Topology improvement via edge swapping (Delaunay refinement)
                    if (options.EnableEdgeSwapping)
                    {
                        currentIteration++;
                        updateProgress((float)currentIteration / totalIterations, 
                            $"Pass {pass + 1}: Edge swapping...");
                        
                        int swaps = PerformEdgeSwapping(optimizer, options);
                        _service.Log($"Performed {swaps} edge swaps");
                    }

                    // Phase 2: Vertex smoothing (Laplacian and optimization-based)
                    if (options.EnableLaplacianSmoothing)
                    {
                        for (int smooth = 0; smooth < options.SmoothingIterations; smooth++)
                        {
                            currentIteration++;
                            updateProgress((float)currentIteration / totalIterations,
                                $"Pass {pass + 1}: Smoothing iteration {smooth + 1}...");

                            PerformLaplacianSmoothing(optimizer, options);
                        }
                    }

                    // Phase 3: Local optimization for vertex positions
                    if (options.EnableVertexOptimization)
                    {
                        currentIteration++;
                        updateProgress((float)currentIteration / totalIterations,
                            $"Pass {pass + 1}: Vertex optimization...");
                        
                        int optimized = PerformVertexOptimization(optimizer, options);
                        _service.Log($"Optimized {optimized} vertices");
                    }

                    // Phase 4: Sliver and degenerate element removal
                    if (options.EnableSliverRemoval)
                    {
                        currentIteration++;
                        updateProgress((float)currentIteration / totalIterations,
                            $"Pass {pass + 1}: Sliver removal...");
                        
                        int removed = RemoveSlivers(optimizer, options);
                        if (removed > 0)
                            _service.Log($"Removed {removed} sliver elements");
                    }
                }

                // Final quality assessment
                var finalQuality = ComputeOverallQuality(optimizer);
                _service.Log($"Final mesh quality: min angle = {finalQuality.MinAngle:F2}°, " +
                           $"max angle = {finalQuality.MaxAngle:F2}°, " +
                           $"avg aspect ratio = {finalQuality.AvgAspectRatio:F3}");
                _service.Log($"Quality improvement: " +
                           $"min angle +{finalQuality.MinAngle - initialQuality.MinAngle:F2}°, " +
                           $"aspect ratio {((finalQuality.AvgAspectRatio / initialQuality.AvgAspectRatio - 1) * 100):F1}%");

                updateProgress(1.0f, "Optimization complete");
                return optimizer.ExportOptimizedMesh();
            });
        }

        #endregion

        #region Edge Swapping (Delaunay Refinement)

        /// <summary>
        /// Performs edge swapping to improve mesh quality based on Delaunay criterion
        /// and angle optimization (Freitag & Ollivier-Gooch, 1997)
        /// </summary>
        private int PerformEdgeSwapping(MeshOptimizationContext context, MeshOptimizationOptions options)
        {
            int swapCount = 0;
            var edgeRegistry = BuildEdgeRegistry(context);

            // Iterate through all internal edges
            foreach (var kvp in edgeRegistry)
            {
                var edge = kvp.Key;
                var adjacentFaces = kvp.Value;

                // Only swap internal edges (shared by exactly 2 faces)
                if (adjacentFaces.Count != 2)
                    continue;

                int face1Idx = adjacentFaces[0];
                int face2Idx = adjacentFaces[1];

                if (ShouldSwapEdge(context, face1Idx, face2Idx, edge, options))
                {
                    PerformEdgeSwap(context, face1Idx, face2Idx, edge);
                    swapCount++;
                }
            }

            return swapCount;
        }

        /// <summary>
        /// Determines if an edge should be swapped based on quality criteria
        /// </summary>
        private bool ShouldSwapEdge(
            MeshOptimizationContext context,
            int face1Idx,
            int face2Idx,
            Edge edge,
            MeshOptimizationOptions options)
        {
            var face1 = context.GetFace(face1Idx);
            var face2 = context.GetFace(face2Idx);

            // Find the opposite vertices (not part of the edge)
            int oppositeVertex1 = FindOppositeVertex(face1, edge);
            int oppositeVertex2 = FindOppositeVertex(face2, edge);

            if (oppositeVertex1 == -1 || oppositeVertex2 == -1)
                return false;

            // Get current configuration quality
            float currentQuality = Math.Min(
                ComputeTriangleQuality(context, face1),
                ComputeTriangleQuality(context, face2));

            // Simulate swapped configuration
            var newFace1 = new int[] { edge.V1, oppositeVertex1, oppositeVertex2 };
            var newFace2 = new int[] { edge.V2, oppositeVertex1, oppositeVertex2 };

            // Check if swap creates valid triangles
            if (!IsValidTriangle(context, newFace1) || !IsValidTriangle(context, newFace2))
                return false;

            float newQuality = Math.Min(
                ComputeTriangleQuality(context, newFace1),
                ComputeTriangleQuality(context, newFace2));

            // Swap if it improves quality
            return newQuality > currentQuality * (1.0f + options.EdgeSwapQualityThreshold);
        }

        /// <summary>
        /// Executes the edge swap operation
        /// </summary>
        private void PerformEdgeSwap(
            MeshOptimizationContext context,
            int face1Idx,
            int face2Idx,
            Edge edge)
        {
            var face1 = context.GetFace(face1Idx);
            var face2 = context.GetFace(face2Idx);

            int oppositeVertex1 = FindOppositeVertex(face1, edge);
            int oppositeVertex2 = FindOppositeVertex(face2, edge);

            // Create new faces
            context.SetFace(face1Idx, new int[] { edge.V1, oppositeVertex1, oppositeVertex2 });
            context.SetFace(face2Idx, new int[] { edge.V2, oppositeVertex1, oppositeVertex2 });
        }

        #endregion

        #region Laplacian Smoothing

        /// <summary>
        /// Performs Laplacian smoothing with feature preservation
        /// Based on weighted Laplacian approach (Vartziotis & Himpel, 2014)
        /// </summary>
        private void PerformLaplacianSmoothing(
            MeshOptimizationContext context,
            MeshOptimizationOptions options)
        {
            var newPositions = new Vector3[context.VertexCount];
            var neighborLists = BuildNeighborLists(context);

            // Compute new positions using weighted Laplacian
            for (int i = 0; i < context.VertexCount; i++)
            {
                if (context.IsVertexFixed(i))
                {
                    newPositions[i] = context.GetVertex(i);
                    continue;
                }

                var neighbors = neighborLists[i];
                if (neighbors.Count == 0)
                {
                    newPositions[i] = context.GetVertex(i);
                    continue;
                }

                Vector3 laplacian = ComputeWeightedLaplacian(context, i, neighbors, options);
                Vector3 currentPos = context.GetVertex(i);

                // Apply damping factor to prevent over-smoothing and mesh shrinkage
                newPositions[i] = currentPos + laplacian * options.LaplacianDampingFactor;
            }

            // Update vertex positions
            for (int i = 0; i < context.VertexCount; i++)
            {
                if (!context.IsVertexFixed(i))
                {
                    context.SetVertex(i, newPositions[i]);
                }
            }
        }

        /// <summary>
        /// Computes weighted Laplacian for a vertex based on surrounding element quality
        /// </summary>
        private Vector3 ComputeWeightedLaplacian(
            MeshOptimizationContext context,
            int vertexIdx,
            List<int> neighbors,
            MeshOptimizationOptions options)
        {
            Vector3 currentPos = context.GetVertex(vertexIdx);
            Vector3 weightedSum = Vector3.Zero;
            float totalWeight = 0;

            switch (options.LaplacianWeightingScheme)
            {
                case LaplacianWeighting.Uniform:
                    // Simple uniform weighting (classic Laplacian)
                    foreach (int neighborIdx in neighbors)
                    {
                        weightedSum += context.GetVertex(neighborIdx);
                        totalWeight += 1.0f;
                    }
                    break;

                case LaplacianWeighting.InverseDistance:
                    // Distance-based weighting (closer neighbors have more influence)
                    foreach (int neighborIdx in neighbors)
                    {
                        Vector3 neighborPos = context.GetVertex(neighborIdx);
                        float distance = Vector3.Distance(currentPos, neighborPos);
                        float weight = 1.0f / (distance + EPSILON);
                        weightedSum += neighborPos * weight;
                        totalWeight += weight;
                    }
                    break;

                case LaplacianWeighting.Cotangent:
                    // Cotangent weighting (more geometrically accurate)
                    weightedSum = ComputeCotangentLaplacian(context, vertexIdx, neighbors);
                    totalWeight = 1.0f;
                    return weightedSum - currentPos; // Already centered
            }

            if (totalWeight < EPSILON)
                return Vector3.Zero;

            Vector3 centroid = weightedSum / totalWeight;
            return centroid - currentPos;
        }

        /// <summary>
        /// Computes cotangent-weighted Laplacian (more accurate for curved surfaces)
        /// </summary>
        private Vector3 ComputeCotangentLaplacian(
            MeshOptimizationContext context,
            int vertexIdx,
            List<int> neighbors)
        {
            Vector3 currentPos = context.GetVertex(vertexIdx);
            Vector3 laplacian = Vector3.Zero;
            float totalWeight = 0;

            // For each neighbor, compute cotangent weight based on opposite angles
            for (int i = 0; i < neighbors.Count; i++)
            {
                int neighbor = neighbors[i];
                Vector3 neighborPos = context.GetVertex(neighbor);

                // Find triangles containing this edge
                var sharedFaces = FindSharedFaces(context, vertexIdx, neighbor);
                float cotangentSum = 0;

                foreach (int faceIdx in sharedFaces)
                {
                    var face = context.GetFace(faceIdx);
                    int oppositeVertex = FindOppositeVertexInTriangle(face, vertexIdx, neighbor);
                    
                    if (oppositeVertex != -1)
                    {
                        Vector3 oppositePos = context.GetVertex(oppositeVertex);
                        float cotangent = ComputeCotangentAngle(oppositePos, currentPos, neighborPos);
                        cotangentSum += cotangent;
                    }
                }

                float weight = Math.Max(cotangentSum, EPSILON);
                laplacian += (neighborPos - currentPos) * weight;
                totalWeight += weight;
            }

            if (totalWeight > EPSILON)
                laplacian /= totalWeight;

            return currentPos + laplacian;
        }

        /// <summary>
        /// Computes cotangent of angle at vertex A for triangle (A, B, C)
        /// </summary>
        private float ComputeCotangentAngle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;

            float dot = Vector3.Dot(ab, ac);
            Vector3 cross = Vector3.Cross(ab, ac);
            float crossLength = cross.Length();

            if (crossLength < EPSILON)
                return 0;

            return dot / crossLength;
        }

        #endregion

        #region Vertex Optimization

        /// <summary>
        /// Performs local vertex optimization to maximize minimum quality in surrounding elements
        /// Based on optimization-based smoothing (Freitag & Plassmann, 2000)
        /// </summary>
        private int PerformVertexOptimization(
            MeshOptimizationContext context,
            MeshOptimizationOptions options)
        {
            int optimizedCount = 0;

            for (int vertexIdx = 0; vertexIdx < context.VertexCount; vertexIdx++)
            {
                if (context.IsVertexFixed(vertexIdx))
                    continue;

                // Get all faces adjacent to this vertex
                var adjacentFaces = FindAdjacentFaces(context, vertexIdx);
                if (adjacentFaces.Count == 0)
                    continue;

                // Compute current minimum quality
                float currentMinQuality = float.MaxValue;
                foreach (int faceIdx in adjacentFaces)
                {
                    float quality = ComputeTriangleQuality(context, context.GetFace(faceIdx));
                    currentMinQuality = Math.Min(currentMinQuality, quality);
                }

                // Try to find better position using gradient-free optimization
                Vector3 currentPos = context.GetVertex(vertexIdx);
                Vector3 optimizedPos = OptimizeVertexPosition(
                    context, vertexIdx, adjacentFaces, currentPos, options);

                // Update if improvement found
                context.SetVertex(vertexIdx, optimizedPos);
                
                float newMinQuality = float.MaxValue;
                foreach (int faceIdx in adjacentFaces)
                {
                    float quality = ComputeTriangleQuality(context, context.GetFace(faceIdx));
                    newMinQuality = Math.Min(newMinQuality, quality);
                }

                if (newMinQuality > currentMinQuality)
                {
                    optimizedCount++;
                }
                else
                {
                    // Revert if no improvement
                    context.SetVertex(vertexIdx, currentPos);
                }
            }

            return optimizedCount;
        }

        /// <summary>
        /// Optimizes vertex position using Nelder-Mead simplex method
        /// </summary>
        private Vector3 OptimizeVertexPosition(
            MeshOptimizationContext context,
            int vertexIdx,
            List<int> adjacentFaces,
            Vector3 initialPos,
            MeshOptimizationOptions options)
        {
            // Use simple gradient descent for efficiency
            Vector3 currentPos = initialPos;
            float stepSize = options.OptimizationStepSize;

            for (int iter = 0; iter < options.OptimizationSubIterations; iter++)
            {
                Vector3 gradient = ComputeQualityGradient(
                    context, vertexIdx, adjacentFaces, currentPos);

                // Take step in direction of gradient
                Vector3 newPos = currentPos + gradient * stepSize;

                // Check if improvement
                context.SetVertex(vertexIdx, newPos);
                float newMinQuality = float.MaxValue;
                foreach (int faceIdx in adjacentFaces)
                {
                    float quality = ComputeTriangleQuality(context, context.GetFace(faceIdx));
                    newMinQuality = Math.Min(newMinQuality, quality);
                }

                context.SetVertex(vertexIdx, currentPos);
                float currentMinQuality = float.MaxValue;
                foreach (int faceIdx in adjacentFaces)
                {
                    float quality = ComputeTriangleQuality(context, context.GetFace(faceIdx));
                    currentMinQuality = Math.Min(currentMinQuality, quality);
                }

                if (newMinQuality > currentMinQuality)
                {
                    currentPos = newPos;
                    stepSize *= 1.1f; // Accelerate if improving
                }
                else
                {
                    stepSize *= 0.5f; // Reduce step if not improving
                }

                if (stepSize < EPSILON)
                    break;
            }

            return currentPos;
        }

        /// <summary>
        /// Computes gradient of quality metric with respect to vertex position
        /// </summary>
        private Vector3 ComputeQualityGradient(
            MeshOptimizationContext context,
            int vertexIdx,
            List<int> adjacentFaces,
            Vector3 pos)
        {
            const float h = 1e-4f; // Finite difference step

            Vector3 gradient = Vector3.Zero;

            // Compute numerical gradient using central differences
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 offset = Vector3.Zero;
                if (axis == 0) offset.X = h;
                else if (axis == 1) offset.Y = h;
                else offset.Z = h;

                // Forward difference
                context.SetVertex(vertexIdx, pos + offset);
                float qualityPlus = float.MaxValue;
                foreach (int faceIdx in adjacentFaces)
                {
                    float q = ComputeTriangleQuality(context, context.GetFace(faceIdx));
                    qualityPlus = Math.Min(qualityPlus, q);
                }

                // Backward difference
                context.SetVertex(vertexIdx, pos - offset);
                float qualityMinus = float.MaxValue;
                foreach (int faceIdx in adjacentFaces)
                {
                    float q = ComputeTriangleQuality(context, context.GetFace(faceIdx));
                    qualityMinus = Math.Min(qualityMinus, q);
                }

                // Central difference
                float derivative = (qualityPlus - qualityMinus) / (2.0f * h);

                if (axis == 0) gradient.X = derivative;
                else if (axis == 1) gradient.Y = derivative;
                else gradient.Z = derivative;
            }

            return gradient;
        }

        #endregion

        #region Sliver Removal

        /// <summary>
        /// Removes sliver elements (nearly degenerate triangles)
        /// </summary>
        private int RemoveSlivers(MeshOptimizationContext context, MeshOptimizationOptions options)
        {
            var sliversToRemove = new List<int>();

            // Identify slivers based on quality threshold
            for (int faceIdx = 0; faceIdx < context.FaceCount; faceIdx++)
            {
                var face = context.GetFace(faceIdx);
                float quality = ComputeTriangleQuality(context, face);

                if (quality < options.SliverQualityThreshold)
                {
                    sliversToRemove.Add(faceIdx);
                }
            }

            // Remove slivers (mark for deletion)
            foreach (int faceIdx in sliversToRemove)
            {
                context.MarkFaceForDeletion(faceIdx);
            }

            context.CompactMesh();
            return sliversToRemove.Count;
        }

        #endregion

        #region Quality Metrics

        /// <summary>
        /// Computes triangle quality using normalized aspect ratio metric
        /// Quality ranges from 0 (degenerate) to 1 (equilateral)
        /// Based on: Shewchuk, J.R. (2002). What is a good linear element?
        /// </summary>
        private float ComputeTriangleQuality(MeshOptimizationContext context, int[] face)
        {
            if (face.Length != 3)
                return 0;

            Vector3 v0 = context.GetVertex(face[0]);
            Vector3 v1 = context.GetVertex(face[1]);
            Vector3 v2 = context.GetVertex(face[2]);

            // Edge vectors
            Vector3 e0 = v1 - v0;
            Vector3 e1 = v2 - v1;
            Vector3 e2 = v0 - v2;

            // Edge lengths
            float l0 = e0.Length();
            float l1 = e1.Length();
            float l2 = e2.Length();

            // Area using cross product
            Vector3 cross = Vector3.Cross(e0, -e2);
            float area = 0.5f * cross.Length();

            if (area < EPSILON)
                return 0;

            // Sum of squared edge lengths
            float sumSquaredLengths = l0 * l0 + l1 * l1 + l2 * l2;

            // Normalized quality metric (equilateral triangle = 1)
            // Q = 4√3 * A / (l₀² + l₁² + l₂²)
            float quality = (4.0f * MathF.Sqrt(3.0f) * area) / sumSquaredLengths;

            return Math.Clamp(quality, 0, 1);
        }

        /// <summary>
        /// Computes overall mesh quality statistics
        /// </summary>
        private MeshQualityMetrics ComputeOverallQuality(MeshOptimizationContext context)
        {
            var metrics = new MeshQualityMetrics();
            
            float minAngle = float.MaxValue;
            float maxAngle = float.MinValue;
            float sumAspectRatio = 0;
            int validFaces = 0;

            for (int faceIdx = 0; faceIdx < context.FaceCount; faceIdx++)
            {
                var face = context.GetFace(faceIdx);
                if (face.Length != 3)
                    continue;

                Vector3 v0 = context.GetVertex(face[0]);
                Vector3 v1 = context.GetVertex(face[1]);
                Vector3 v2 = context.GetVertex(face[2]);

                // Compute angles
                var angles = ComputeTriangleAngles(v0, v1, v2);
                minAngle = Math.Min(minAngle, angles.Min());
                maxAngle = Math.Max(maxAngle, angles.Max());

                // Compute aspect ratio
                float aspectRatio = ComputeAspectRatio(v0, v1, v2);
                sumAspectRatio += aspectRatio;

                validFaces++;
            }

            metrics.MinAngle = minAngle * (180.0f / MathF.PI);
            metrics.MaxAngle = maxAngle * (180.0f / MathF.PI);
            metrics.AvgAspectRatio = validFaces > 0 ? sumAspectRatio / validFaces : 0;

            return metrics;
        }

        /// <summary>
        /// Computes all three angles of a triangle
        /// </summary>
        private float[] ComputeTriangleAngles(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            Vector3 e0 = Vector3.Normalize(v1 - v0);
            Vector3 e1 = Vector3.Normalize(v2 - v1);
            Vector3 e2 = Vector3.Normalize(v0 - v2);

            float angle0 = MathF.Acos(Math.Clamp(Vector3.Dot(e0, -e2), -1, 1));
            float angle1 = MathF.Acos(Math.Clamp(Vector3.Dot(e1, -e0), -1, 1));
            float angle2 = MathF.Acos(Math.Clamp(Vector3.Dot(e2, -e1), -1, 1));

            return new float[] { angle0, angle1, angle2 };
        }

        /// <summary>
        /// Computes aspect ratio (ratio of longest edge to shortest altitude)
        /// </summary>
        private float ComputeAspectRatio(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            float l0 = Vector3.Distance(v0, v1);
            float l1 = Vector3.Distance(v1, v2);
            float l2 = Vector3.Distance(v2, v0);

            float maxEdge = Math.Max(l0, Math.Max(l1, l2));

            Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
            float area = 0.5f * cross.Length();

            if (area < EPSILON)
                return float.MaxValue;

            float minAltitude = 2.0f * area / maxEdge;

            return maxEdge / minAltitude;
        }

        #endregion

        #region Utility Methods

        private Dictionary<Edge, List<int>> BuildEdgeRegistry(MeshOptimizationContext context)
        {
            var registry = new Dictionary<Edge, List<int>>();

            for (int faceIdx = 0; faceIdx < context.FaceCount; faceIdx++)
            {
                var face = context.GetFace(faceIdx);
                if (face.Length != 3)
                    continue;

                for (int i = 0; i < 3; i++)
                {
                    int v1 = face[i];
                    int v2 = face[(i + 1) % 3];
                    var edge = new Edge(v1, v2);

                    if (!registry.ContainsKey(edge))
                        registry[edge] = new List<int>();
                    
                    registry[edge].Add(faceIdx);
                }
            }

            return registry;
        }

        private List<int>[] BuildNeighborLists(MeshOptimizationContext context)
        {
            var neighbors = new List<int>[context.VertexCount];
            for (int i = 0; i < context.VertexCount; i++)
                neighbors[i] = new List<int>();

            for (int faceIdx = 0; faceIdx < context.FaceCount; faceIdx++)
            {
                var face = context.GetFace(faceIdx);
                if (face.Length != 3)
                    continue;

                for (int i = 0; i < 3; i++)
                {
                    int v1 = face[i];
                    int v2 = face[(i + 1) % 3];

                    if (!neighbors[v1].Contains(v2))
                        neighbors[v1].Add(v2);
                    if (!neighbors[v2].Contains(v1))
                        neighbors[v2].Add(v1);
                }
            }

            return neighbors;
        }

        private List<int> FindAdjacentFaces(MeshOptimizationContext context, int vertexIdx)
        {
            var adjacent = new List<int>();

            for (int faceIdx = 0; faceIdx < context.FaceCount; faceIdx++)
            {
                var face = context.GetFace(faceIdx);
                if (face.Contains(vertexIdx))
                    adjacent.Add(faceIdx);
            }

            return adjacent;
        }

        private List<int> FindSharedFaces(MeshOptimizationContext context, int v1, int v2)
        {
            var faces1 = FindAdjacentFaces(context, v1);
            var faces2 = FindAdjacentFaces(context, v2);
            return faces1.Intersect(faces2).ToList();
        }

        private int FindOppositeVertex(int[] face, Edge edge)
        {
            foreach (int v in face)
            {
                if (v != edge.V1 && v != edge.V2)
                    return v;
            }
            return -1;
        }

        private int FindOppositeVertexInTriangle(int[] face, int v1, int v2)
        {
            if (face.Length != 3)
                return -1;

            foreach (int v in face)
            {
                if (v != v1 && v != v2)
                    return v;
            }
            return -1;
        }

        private bool IsValidTriangle(MeshOptimizationContext context, int[] face)
        {
            if (face.Length != 3)
                return false;

            Vector3 v0 = context.GetVertex(face[0]);
            Vector3 v1 = context.GetVertex(face[1]);
            Vector3 v2 = context.GetVertex(face[2]);

            Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
            return cross.Length() > EPSILON;
        }

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Edge representation (order-independent)
    /// </summary>
    internal struct Edge : IEquatable<Edge>
    {
        public int V1 { get; }
        public int V2 { get; }

        public Edge(int v1, int v2)
        {
            if (v1 < v2)
            {
                V1 = v1;
                V2 = v2;
            }
            else
            {
                V1 = v2;
                V2 = v1;
            }
        }

        public bool Equals(Edge other)
        {
            return V1 == other.V1 && V2 == other.V2;
        }

        public override bool Equals(object obj)
        {
            return obj is Edge edge && Equals(edge);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(V1, V2);
        }
    }

    /// <summary>
    /// Mesh optimization context maintaining working state
    /// </summary>
    internal class MeshOptimizationContext
    {
        private List<Vector3> _vertices;
        private List<int[]> _faces;
        private HashSet<int> _fixedVertices;
        private HashSet<int> _deletedFaces;
        private readonly PhotogrammetryProcessingService _service;
        private readonly Mesh3DDataset _originalMesh;

        public int VertexCount => _vertices.Count;
        public int FaceCount => _faces.Count;

        public MeshOptimizationContext(Mesh3DDataset mesh, PhotogrammetryProcessingService service)
        {
            _service = service;
            _originalMesh = mesh;
            _vertices = new List<Vector3>(mesh.Vertices);
            _faces = new List<int[]>(mesh.Faces.Select(f => (int[])f.Clone()));
            _fixedVertices = new HashSet<int>();
            _deletedFaces = new HashSet<int>();

            // Mark boundary vertices as fixed (simplified - assumes all vertices can move)
            // In production, would detect actual boundary
        }

        public Vector3 GetVertex(int idx) => _vertices[idx];
        public void SetVertex(int idx, Vector3 pos) => _vertices[idx] = pos;
        public int[] GetFace(int idx) => _faces[idx];
        public void SetFace(int idx, int[] face) => _faces[idx] = face;
        public bool IsVertexFixed(int idx) => _fixedVertices.Contains(idx);
        public void MarkFaceForDeletion(int idx) => _deletedFaces.Add(idx);

        public void CompactMesh()
        {
            if (_deletedFaces.Count == 0)
                return;

            var newFaces = new List<int[]>();
            for (int i = 0; i < _faces.Count; i++)
            {
                if (!_deletedFaces.Contains(i))
                    newFaces.Add(_faces[i]);
            }

            _faces = newFaces;
            _deletedFaces.Clear();

            _service.Log($"Compacted mesh: removed {_deletedFaces.Count} faces");
        }

        public Mesh3DDataset ExportOptimizedMesh()
        {
            // Create optimized mesh with same scale as original (1.0f for default, meters for unit)
            var optimizedPath = _originalMesh.FilePath.Replace(".obj", "_optimized.obj");
            
            return Mesh3DDataset.CreateFromData(
                _originalMesh.Name + "_optimized",
                optimizedPath,
                _vertices,
                _faces,
                1.0f,  // voxelSize - default scale
                "m");  // unit - meters
        }
    }

    /// <summary>
    /// Mesh quality metrics
    /// </summary>
    public struct MeshQualityMetrics
    {
        public float MinAngle { get; set; }
        public float MaxAngle { get; set; }
        public float AvgAspectRatio { get; set; }
    }

    /// <summary>
    /// Mesh optimization options
    /// </summary>
    public class MeshOptimizationOptions
    {
        public int OptimizationPasses { get; set; } = 3;
        public int MaxIterations { get; set; } = 10;
        
        public bool EnableEdgeSwapping { get; set; } = true;
        public float EdgeSwapQualityThreshold { get; set; } = 0.01f;
        
        public bool EnableLaplacianSmoothing { get; set; } = true;
        public int SmoothingIterations { get; set; } = 5;
        public float LaplacianDampingFactor { get; set; } = 0.5f;
        public LaplacianWeighting LaplacianWeightingScheme { get; set; } = LaplacianWeighting.Cotangent;
        
        public bool EnableVertexOptimization { get; set; } = true;
        public float OptimizationStepSize { get; set; } = 0.1f;
        public int OptimizationSubIterations { get; set; } = 10;
        
        public bool EnableSliverRemoval { get; set; } = true;
        public float SliverQualityThreshold { get; set; } = 0.1f;
    }

    /// <summary>
    /// Laplacian weighting schemes
    /// </summary>
    public enum LaplacianWeighting
    {
        Uniform,           // Simple average (classic Laplacian)
        InverseDistance,   // Distance-weighted
        Cotangent          // Cotangent-weighted (more accurate for curved surfaces)
    }

    #endregion
}