// GeoscientistToolkit/Analysis/SlopeStability/Block2D.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Represents a 2D rigid block (polygon) for slope stability analysis.
    /// Adapted from 3D Block class for 2D cross-section analysis.
    /// </summary>
    public class Block2D
    {
        #region Properties

        // Identification
        public int Id { get; set; }
        public string Name { get; set; }

        // Geometry - 2D polygon vertices (CCW winding)
        public List<Vector2> Vertices { get; set; }

        // Area and geometric properties (per unit depth into section)
        public float Area { get; set; }
        public Vector2 Centroid { get; set; }
        public float MomentOfInertia { get; set; }  // About centroid, for rotation

        // Material properties
        public int MaterialId { get; set; }

        // Joint set information
        public List<int> JointSetIds { get; set; }
        public List<int> BoundingJointIndices { get; set; }  // Which joint bounds each edge

        // Physics state
        public Vector2 Position { get; set; }           // Current centroid position
        public Vector2 Velocity { get; set; }           // Linear velocity
        public Vector2 Acceleration { get; set; }       // Linear acceleration
        public float Rotation { get; set; }             // Rotation angle (radians)
        public float AngularVelocity { get; set; }      // Rotation rate
        public float AngularAcceleration { get; set; }  // Angular acceleration

        // Forces (accumulated during simulation step)
        public Vector2 Force { get; set; }
        public float Torque { get; set; }

        // Tracking
        public Vector2 InitialPosition { get; set; }
        public Vector2 TotalDisplacement { get; set; }
        public float MaxDisplacement { get; set; }
        public List<Vector2> DisplacementHistory { get; set; }

        // Flags
        public bool IsActive { get; set; }              // Participates in simulation
        public bool IsRemovable { get; set; }           // Can be removed from slope
        public bool HasFailed { get; set; }             // Exceeded failure criteria
        public bool IsFixed { get; set; }               // Fixed in place (boundary condition)

        // Contacts
        public HashSet<int> ContactingBlockIds { get; set; }

        #endregion

        #region Constructor

        public Block2D()
        {
            Vertices = new List<Vector2>();
            JointSetIds = new List<int>();
            BoundingJointIndices = new List<int>();
            DisplacementHistory = new List<Vector2>();
            ContactingBlockIds = new HashSet<int>();

            Position = Vector2.Zero;
            Velocity = Vector2.Zero;
            Acceleration = Vector2.Zero;
            Force = Vector2.Zero;

            IsActive = true;
            IsRemovable = false;
            HasFailed = false;
            IsFixed = false;
        }

        #endregion

        #region Geometry Methods

        /// <summary>
        /// Calculate geometric properties (area, centroid, moment of inertia).
        /// </summary>
        public void CalculateProperties()
        {
            if (Vertices.Count < 3)
            {
                Area = 0;
                Centroid = Vector2.Zero;
                MomentOfInertia = 0;
                return;
            }

            // Calculate area and centroid using polygon formula
            float area = 0;
            Vector2 centroid = Vector2.Zero;

            for (int i = 0; i < Vertices.Count; i++)
            {
                var v1 = Vertices[i];
                var v2 = Vertices[(i + 1) % Vertices.Count];

                float cross = v1.X * v2.Y - v2.X * v1.Y;
                area += cross;
                centroid.X += (v1.X + v2.X) * cross;
                centroid.Y += (v1.Y + v2.Y) * cross;
            }

            area *= 0.5f;
            Area = Math.Abs(area);

            if (Area > 0)
            {
                centroid /= (6.0f * area);
                Centroid = centroid;
            }
            else
            {
                Centroid = Vertices[0];
            }

            // Calculate moment of inertia about centroid
            // For a polygon: I = (1/12) * sum(cross product of consecutive vertices)
            MomentOfInertia = 0;
            for (int i = 0; i < Vertices.Count; i++)
            {
                var v1 = Vertices[i] - Centroid;
                var v2 = Vertices[(i + 1) % Vertices.Count] - Centroid;

                float cross = v1.X * v2.Y - v1.Y * v2.X;
                float term = (v1.LengthSquared() + Vector2.Dot(v1, v2) + v2.LengthSquared()) * cross;
                MomentOfInertia += term;
            }
            MomentOfInertia = Math.Abs(MomentOfInertia) / 12.0f;

            Position = Centroid;
            InitialPosition = Centroid;
        }

        /// <summary>
        /// Get current vertices with rotation applied.
        /// </summary>
        public List<Vector2> GetTransformedVertices()
        {
            if (Math.Abs(Rotation) < 0.0001f && Vector2.Distance(Position, Centroid) < 0.0001f)
                return new List<Vector2>(Vertices);

            var result = new List<Vector2>();
            float cos = MathF.Cos(Rotation);
            float sin = MathF.Sin(Rotation);

            foreach (var v in Vertices)
            {
                // Translate to origin
                var local = v - Centroid;

                // Rotate
                var rotated = new Vector2(
                    local.X * cos - local.Y * sin,
                    local.X * sin + local.Y * cos
                );

                // Translate to position
                result.Add(rotated + Position);
            }

            return result;
        }

        /// <summary>
        /// Get the bounding box of the block.
        /// </summary>
        public (Vector2 min, Vector2 max) GetBounds()
        {
            var transformed = GetTransformedVertices();
            if (transformed.Count == 0)
                return (Vector2.Zero, Vector2.Zero);

            var min = transformed[0];
            var max = transformed[0];

            foreach (var v in transformed)
            {
                min.X = Math.Min(min.X, v.X);
                min.Y = Math.Min(min.Y, v.Y);
                max.X = Math.Max(max.X, v.X);
                max.Y = Math.Max(max.Y, v.Y);
            }

            return (min, max);
        }

        /// <summary>
        /// Get edges of the polygon (as line segments).
        /// </summary>
        public List<(Vector2 p1, Vector2 p2)> GetEdges()
        {
            var transformed = GetTransformedVertices();
            var edges = new List<(Vector2, Vector2)>();

            for (int i = 0; i < transformed.Count; i++)
            {
                edges.Add((transformed[i], transformed[(i + 1) % transformed.Count]));
            }

            return edges;
        }

        /// <summary>
        /// Check if a point is inside the block (transformed).
        /// </summary>
        public bool ContainsPoint(Vector2 point)
        {
            var transformed = GetTransformedVertices();
            if (transformed.Count < 3) return false;

            // Ray casting algorithm
            int intersections = 0;
            for (int i = 0; i < transformed.Count; i++)
            {
                var v1 = transformed[i];
                var v2 = transformed[(i + 1) % transformed.Count];

                if ((v1.Y > point.Y) != (v2.Y > point.Y))
                {
                    float xIntersection = v1.X + (point.Y - v1.Y) * (v2.X - v1.X) / (v2.Y - v1.Y);
                    if (point.X < xIntersection)
                        intersections++;
                }
            }

            return (intersections % 2) == 1;
        }

        #endregion

        #region Physics Methods

        /// <summary>
        /// Reset forces for next simulation step.
        /// </summary>
        public void ResetForces()
        {
            Force = Vector2.Zero;
            Torque = 0;
        }

        /// <summary>
        /// Add a force at a specific point (generates torque).
        /// </summary>
        public void AddForceAtPoint(Vector2 force, Vector2 point)
        {
            Force += force;

            // Calculate torque: r × F (in 2D: cross product gives scalar)
            var r = point - Position;
            Torque += r.X * force.Y - r.Y * force.X;
        }

        /// <summary>
        /// Update displacement tracking.
        /// </summary>
        public void UpdateDisplacement()
        {
            TotalDisplacement = Position - InitialPosition;
            float displacement = TotalDisplacement.Length();

            if (displacement > MaxDisplacement)
                MaxDisplacement = displacement;

            // Record history
            DisplacementHistory.Add(TotalDisplacement);
        }

        /// <summary>
        /// Get mass (per unit depth into section).
        /// </summary>
        public float GetMass(float density)
        {
            return Area * density;  // kg (assuming unit depth = 1m)
        }

        /// <summary>
        /// Get moment of inertia scaled by mass.
        /// </summary>
        public float GetMassedMomentOfInertia(float density)
        {
            return MomentOfInertia * density;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Clone this block.
        /// </summary>
        public Block2D Clone()
        {
            return new Block2D
            {
                Id = Id,
                Name = Name,
                Vertices = new List<Vector2>(Vertices),
                Area = Area,
                Centroid = Centroid,
                MomentOfInertia = MomentOfInertia,
                MaterialId = MaterialId,
                JointSetIds = new List<int>(JointSetIds),
                BoundingJointIndices = new List<int>(BoundingJointIndices),
                Position = Position,
                Velocity = Velocity,
                Acceleration = Acceleration,
                Rotation = Rotation,
                AngularVelocity = AngularVelocity,
                AngularAcceleration = AngularAcceleration,
                Force = Force,
                Torque = Torque,
                InitialPosition = InitialPosition,
                TotalDisplacement = TotalDisplacement,
                MaxDisplacement = MaxDisplacement,
                DisplacementHistory = new List<Vector2>(DisplacementHistory),
                IsActive = IsActive,
                IsRemovable = IsRemovable,
                HasFailed = HasFailed,
                IsFixed = IsFixed,
                ContactingBlockIds = new HashSet<int>(ContactingBlockIds)
            };
        }

        public override string ToString()
        {
            return $"Block2D {Id}: {Vertices.Count} vertices, Area={Area:F2}m², Pos=({Position.X:F2}, {Position.Y:F2})";
        }

        #endregion
    }
}
