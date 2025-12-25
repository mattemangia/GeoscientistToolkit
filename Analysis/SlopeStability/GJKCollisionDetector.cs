using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Professional collision detection using GJK (Gilbert-Johnson-Keerthi) algorithm.
    /// This replaces the simplified AABB approach with accurate convex hull collision detection.
    /// GJK is the industry standard used in physics engines like Bullet, PhysX, and Havok.
    /// </summary>
    public static class GJKCollisionDetector
    {
        private const float EPSILON = 1e-6f;
        private const int MAX_GJK_ITERATIONS = 32;
        private const int MAX_EPA_ITERATIONS = 32;

        /// <summary>
        /// Detects collision between two convex shapes using GJK algorithm.
        /// Returns true if shapes overlap, along with collision details.
        /// </summary>
        public static CollisionResult DetectCollision(
            List<Vector3> shapeA,
            List<Vector3> shapeB,
            Vector3 positionA,
            Vector3 positionB,
            Quaternion orientationA,
            Quaternion orientationB)
        {
            var result = new CollisionResult
            {
                IsColliding = false,
                PenetrationDepth = 0.0f,
                ContactNormal = Vector3.UnitZ,
                ContactPoint = Vector3.Zero
            };

            // Transform shapes to world space
            var worldShapeA = TransformVertices(shapeA, positionA, orientationA);
            var worldShapeB = TransformVertices(shapeB, positionB, orientationB);

            // Run GJK algorithm
            var simplex = new List<Vector3>();
            Vector3 direction = Vector3.Normalize(positionB - positionA);

            // Initial support point
            var support = GetSupportPoint(worldShapeA, worldShapeB, direction);
            simplex.Add(support);

            direction = -support;  // Search towards origin

            for (int iteration = 0; iteration < MAX_GJK_ITERATIONS; iteration++)
            {
                support = GetSupportPoint(worldShapeA, worldShapeB, direction);

                // If support point doesn't pass the origin, no collision
                if (Vector3.Dot(support, direction) < 0)
                {
                    return result;  // No collision
                }

                simplex.Add(support);

                if (ProcessSimplex(simplex, ref direction))
                {
                    // Collision detected! Now use EPA to find penetration depth
                    result.IsColliding = true;

                    var epaResult = EPA(worldShapeA, worldShapeB, simplex);
                    result.PenetrationDepth = epaResult.depth;
                    result.ContactNormal = epaResult.normal;

                    // Contact point is on the surface of shape A
                    result.ContactPoint = positionA + epaResult.normal * epaResult.depth / 2.0f;

                    return result;
                }
            }

            return result;  // No collision after max iterations
        }

        /// <summary>
        /// Minkowski difference support function.
        /// Support(A - B, d) = Support(A, d) - Support(B, -d)
        /// </summary>
        private static Vector3 GetSupportPoint(List<Vector3> shapeA, List<Vector3> shapeB, Vector3 direction)
        {
            Vector3 supportA = GetFarthestPointInDirection(shapeA, direction);
            Vector3 supportB = GetFarthestPointInDirection(shapeB, -direction);
            return supportA - supportB;
        }

        /// <summary>
        /// Finds the farthest point in a shape along a given direction.
        /// This is the core of the support function.
        /// </summary>
        private static Vector3 GetFarthestPointInDirection(List<Vector3> shape, Vector3 direction)
        {
            float maxDot = float.MinValue;
            Vector3 farthest = Vector3.Zero;

            foreach (var vertex in shape)
            {
                float dot = Vector3.Dot(vertex, direction);
                if (dot > maxDot)
                {
                    maxDot = dot;
                    farthest = vertex;
                }
            }

            return farthest;
        }

        /// <summary>
        /// Processes the simplex and updates the search direction.
        /// Returns true if the origin is contained in the simplex (collision detected).
        /// </summary>
        private static bool ProcessSimplex(List<Vector3> simplex, ref Vector3 direction)
        {
            switch (simplex.Count)
            {
                case 2:
                    return ProcessLine(simplex, ref direction);
                case 3:
                    return ProcessTriangle(simplex, ref direction);
                case 4:
                    return ProcessTetrahedron(simplex, ref direction);
                default:
                    throw new InvalidOperationException("Invalid simplex size");
            }
        }

        /// <summary>
        /// Processes a line simplex (2 points).
        /// </summary>
        private static bool ProcessLine(List<Vector3> simplex, ref Vector3 direction)
        {
            Vector3 A = simplex[1];
            Vector3 B = simplex[0];

            Vector3 AB = B - A;
            Vector3 AO = -A;

            if (SameDirection(AB, AO))
            {
                // Direction is perpendicular to AB towards origin
                direction = Vector3.Cross(Vector3.Cross(AB, AO), AB);
            }
            else
            {
                // Remove B, keep only A
                simplex.RemoveAt(0);
                direction = AO;
            }

            return false;
        }

        /// <summary>
        /// Processes a triangle simplex (3 points).
        /// </summary>
        private static bool ProcessTriangle(List<Vector3> simplex, ref Vector3 direction)
        {
            Vector3 A = simplex[2];
            Vector3 B = simplex[1];
            Vector3 C = simplex[0];

            Vector3 AB = B - A;
            Vector3 AC = C - A;
            Vector3 AO = -A;

            Vector3 ABC = Vector3.Cross(AB, AC);

            // Check if origin is on the side of edge AC
            Vector3 ABC_AC = Vector3.Cross(ABC, AC);
            if (SameDirection(ABC_AC, AO))
            {
                if (SameDirection(AC, AO))
                {
                    // Remove B
                    simplex.RemoveAt(1);
                    direction = Vector3.Cross(Vector3.Cross(AC, AO), AC);
                }
                else
                {
                    // Remove C, check AB edge
                    return ProcessLine(new List<Vector3> { B, A }, ref direction);
                }
            }
            else
            {
                // Check if origin is on the side of edge AB
                Vector3 AB_ABC = Vector3.Cross(AB, ABC);
                if (SameDirection(AB_ABC, AO))
                {
                    // Remove C, check AB edge
                    return ProcessLine(new List<Vector3> { B, A }, ref direction);
                }
                else
                {
                    // Origin is above or below the triangle
                    if (SameDirection(ABC, AO))
                    {
                        direction = ABC;
                    }
                    else
                    {
                        // Swap B and C to flip triangle
                        simplex[0] = B;
                        simplex[1] = C;
                        direction = -ABC;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Processes a tetrahedron simplex (4 points).
        /// </summary>
        private static bool ProcessTetrahedron(List<Vector3> simplex, ref Vector3 direction)
        {
            Vector3 A = simplex[3];
            Vector3 B = simplex[2];
            Vector3 C = simplex[1];
            Vector3 D = simplex[0];

            Vector3 AB = B - A;
            Vector3 AC = C - A;
            Vector3 AD = D - A;
            Vector3 AO = -A;

            Vector3 ABC = Vector3.Cross(AB, AC);
            Vector3 ACD = Vector3.Cross(AC, AD);
            Vector3 ADB = Vector3.Cross(AD, AB);

            // Check each face
            if (SameDirection(ABC, AO))
            {
                // Remove D
                simplex.RemoveAt(0);
                return ProcessTriangle(simplex, ref direction);
            }

            if (SameDirection(ACD, AO))
            {
                // Remove B
                simplex.RemoveAt(2);
                return ProcessTriangle(simplex, ref direction);
            }

            if (SameDirection(ADB, AO))
            {
                // Remove C
                simplex.RemoveAt(1);
                return ProcessTriangle(simplex, ref direction);
            }

            // Origin is inside tetrahedron - collision!
            return true;
        }

        /// <summary>
        /// Checks if two vectors point in the same direction.
        /// </summary>
        private static bool SameDirection(Vector3 a, Vector3 b)
        {
            return Vector3.Dot(a, b) > 0;
        }

        /// <summary>
        /// EPA (Expanding Polytope Algorithm) for finding penetration depth and normal.
        /// This is used after GJK detects a collision to find the exact contact information.
        /// </summary>
        private static (float depth, Vector3 normal) EPA(
            List<Vector3> shapeA,
            List<Vector3> shapeB,
            List<Vector3> simplex)
        {
            // Build initial polytope from simplex
            var polytope = new List<Vector3>(simplex);
            var faces = new List<EPAFace>();

            // Initialize faces (tetrahedron has 4 faces)
            if (polytope.Count == 4)
            {
                faces.Add(new EPAFace(0, 1, 2, polytope));
                faces.Add(new EPAFace(0, 3, 1, polytope));
                faces.Add(new EPAFace(0, 2, 3, polytope));
                faces.Add(new EPAFace(1, 3, 2, polytope));
            }
            else
            {
                // Handle degenerate case with triangle
                return (0.001f, Vector3.UnitZ);
            }

            int minIndex = 0;
            float minDistance = float.MaxValue;

            for (int iteration = 0; iteration < MAX_EPA_ITERATIONS; iteration++)
            {
                // Find closest face to origin
                for (int i = 0; i < faces.Count; i++)
                {
                    float distance = faces[i].Distance;
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        minIndex = i;
                    }
                }

                Vector3 searchDir = faces[minIndex].Normal;
                Vector3 support = GetSupportPoint(shapeA, shapeB, searchDir);

                float sDistance = Vector3.Dot(support, searchDir);

                // If support point is not significantly farther, we found the contact
                if (MathF.Abs(sDistance - minDistance) < EPSILON)
                {
                    return (minDistance + EPSILON, faces[minIndex].Normal);
                }

                // Add new support point to polytope and rebuild faces
                polytope.Add(support);

                // Remove faces that can see the new point
                var edges = new List<(int, int)>();
                var toRemove = new List<EPAFace>();

                foreach (var face in faces)
                {
                    if (Vector3.Dot(face.Normal, support - polytope[face.A]) > 0)
                    {
                        // Face is visible from new point
                        edges.Add((face.A, face.B));
                        edges.Add((face.B, face.C));
                        edges.Add((face.C, face.A));
                        toRemove.Add(face);
                    }
                }

                foreach (var face in toRemove)
                {
                    faces.Remove(face);
                }

                // Find unique edges (edges that appear only once)
                var uniqueEdges = new List<(int, int)>();
                for (int i = 0; i < edges.Count; i++)
                {
                    bool isUnique = true;
                    for (int j = 0; j < edges.Count; j++)
                    {
                        if (i != j && ((edges[i].Item1 == edges[j].Item2 && edges[i].Item2 == edges[j].Item1) ||
                                      (edges[i].Item1 == edges[j].Item1 && edges[i].Item2 == edges[j].Item2)))
                        {
                            isUnique = false;
                            break;
                        }
                    }
                    if (isUnique && !uniqueEdges.Contains(edges[i]))
                    {
                        uniqueEdges.Add(edges[i]);
                    }
                }

                // Create new faces with unique edges
                int newPointIndex = polytope.Count - 1;
                foreach (var edge in uniqueEdges)
                {
                    faces.Add(new EPAFace(edge.Item1, edge.Item2, newPointIndex, polytope));
                }

                minDistance = float.MaxValue;
            }

            // Fallback
            return (minDistance + EPSILON, faces[minIndex].Normal);
        }

        /// <summary>
        /// Transforms vertices to world space.
        /// </summary>
        private static List<Vector3> TransformVertices(
            List<Vector3> vertices,
            Vector3 position,
            Quaternion orientation)
        {
            var transformed = new List<Vector3>(vertices.Count);

            foreach (var vertex in vertices)
            {
                transformed.Add(Vector3.Transform(vertex, orientation) + position);
            }

            return transformed;
        }

        /// <summary>
        /// Represents a triangular face in the EPA polytope.
        /// </summary>
        private class EPAFace
        {
            public int A, B, C;
            public Vector3 Normal;
            public float Distance;

            public EPAFace(int a, int b, int c, List<Vector3> polytope)
            {
                A = a;
                B = b;
                C = c;

                Vector3 vA = polytope[a];
                Vector3 vB = polytope[b];
                Vector3 vC = polytope[c];

                Vector3 AB = vB - vA;
                Vector3 AC = vC - vA;

                Normal = Vector3.Normalize(Vector3.Cross(AB, AC));
                Distance = Vector3.Dot(Normal, vA);

                // Ensure normal points towards origin
                if (Distance < 0)
                {
                    Normal = -Normal;
                    Distance = -Distance;
                }
            }
        }
    }

    /// <summary>
    /// Result of a collision detection query.
    /// </summary>
    public class CollisionResult
    {
        public bool IsColliding { get; set; }
        public float PenetrationDepth { get; set; }
        public Vector3 ContactNormal { get; set; }  // Points from A to B
        public Vector3 ContactPoint { get; set; }
    }
}
