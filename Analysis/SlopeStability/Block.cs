using System;
using System.Collections.Generic;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Represents a rigid block in the discrete element simulation.
    /// Each block has geometric properties, physical properties, and state variables.
    /// </summary>
    public class Block
    {
        // Identification
        public int Id { get; set; }
        public string Name { get; set; }

        // Geometric properties
        public List<Vector3> Vertices { get; set; }
        public List<int[]> Faces { get; set; }
        public float Volume { get; set; }
        public Vector3 Centroid { get; set; }
        public Matrix4x4 InertiaTensor { get; set; }

        // Physical properties
        public float Density { get; set; }  // kg/m³
        public float Mass { get; set; }     // kg
        public int MaterialId { get; set; }

        // State variables (current)
        public Vector3 Position { get; set; }           // m
        public Vector3 Velocity { get; set; }           // m/s
        public Vector3 Acceleration { get; set; }       // m/s²
        public Quaternion Orientation { get; set; }
        public Vector3 AngularVelocity { get; set; }    // rad/s
        public Vector3 AngularAcceleration { get; set; } // rad/s²

        // Forces and torques (accumulated each timestep)
        public Vector3 ForceAccumulator { get; set; }
        public Vector3 TorqueAccumulator { get; set; }

        // Displacement tracking
        public Vector3 InitialPosition { get; set; }
        public Vector3 TotalDisplacement { get; set; }
        public float MaxDisplacement { get; set; }

        // Boundary conditions
        public bool IsFixed { get; set; }
        public Vector3 FixedDOF { get; set; }  // 1.0 = fixed, 0.0 = free for each axis

        // Contacts
        public List<int> ContactingBlockIds { get; set; }

        // Visualization
        public Vector4 Color { get; set; }

        public Block()
        {
            Vertices = new List<Vector3>();
            Faces = new List<int[]>();
            Position = Vector3.Zero;
            Velocity = Vector3.Zero;
            Acceleration = Vector3.Zero;
            Orientation = Quaternion.Identity;
            AngularVelocity = Vector3.Zero;
            AngularAcceleration = Vector3.Zero;
            ForceAccumulator = Vector3.Zero;
            TorqueAccumulator = Vector3.Zero;
            InitialPosition = Vector3.Zero;
            TotalDisplacement = Vector3.Zero;
            MaxDisplacement = 0.0f;
            IsFixed = false;
            FixedDOF = Vector3.Zero;
            ContactingBlockIds = new List<int>();
            Color = new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
            InertiaTensor = Matrix4x4.Identity;
        }

        /// <summary>
        /// Calculates the geometric properties of the block (volume, centroid, inertia tensor).
        /// Uses the divergence theorem for accurate volume calculation of arbitrary polyhedra.
        /// </summary>
        public void CalculateGeometricProperties()
        {
            if (Vertices.Count == 0 || Faces.Count == 0)
                return;

            // Calculate volume and centroid using divergence theorem
            float volumeSum = 0.0f;
            Vector3 centroidSum = Vector3.Zero;

            foreach (var face in Faces)
            {
                if (face.Length < 3) continue;

                // Triangulate the face (fan triangulation from first vertex)
                for (int i = 1; i < face.Length - 1; i++)
                {
                    Vector3 v0 = Vertices[face[0]];
                    Vector3 v1 = Vertices[face[i]];
                    Vector3 v2 = Vertices[face[i + 1]];

                    // Signed volume of tetrahedron formed with origin
                    float tetraVolume = Vector3.Dot(v0, Vector3.Cross(v1, v2)) / 6.0f;
                    volumeSum += tetraVolume;

                    // Centroid contribution
                    centroidSum += tetraVolume * (v0 + v1 + v2) / 4.0f;
                }
            }

            Volume = Math.Abs(volumeSum);
            Centroid = Volume > 1e-10f ? centroidSum / volumeSum : Vector3.Zero;

            // Calculate mass
            Mass = Volume * Density;

            // Calculate inertia tensor (simplified box approximation for performance)
            CalculateInertiaTensor();
        }

        /// <summary>
        /// Calculates the inertia tensor for the block.
        /// Uses a simplified bounding box approximation for computational efficiency.
        /// For more accuracy, use tetrahedral decomposition (not implemented for performance).
        /// </summary>
        private void CalculateInertiaTensor()
        {
            if (Vertices.Count == 0)
            {
                InertiaTensor = Matrix4x4.Identity;
                return;
            }

            // Calculate bounding box dimensions
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var v in Vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            Vector3 size = max - min;
            float dx = size.X;
            float dy = size.Y;
            float dz = size.Z;

            // Inertia tensor for a box with mass M and dimensions (dx, dy, dz)
            float Ixx = Mass * (dy * dy + dz * dz) / 12.0f;
            float Iyy = Mass * (dx * dx + dz * dz) / 12.0f;
            float Izz = Mass * (dx * dx + dy * dy) / 12.0f;

            InertiaTensor = new Matrix4x4(
                Ixx, 0, 0, 0,
                0, Iyy, 0, 0,
                0, 0, Izz, 0,
                0, 0, 0, 1
            );
        }

        /// <summary>
        /// Applies a force to the block at a specific point.
        /// This generates both linear force and torque.
        /// </summary>
        public void ApplyForce(Vector3 force, Vector3 applicationPoint)
        {
            ForceAccumulator += force;

            // Calculate torque: τ = r × F
            Vector3 r = applicationPoint - Position;
            TorqueAccumulator += Vector3.Cross(r, force);
        }

        /// <summary>
        /// Clears force and torque accumulators. Call this at the beginning of each timestep.
        /// </summary>
        public void ClearForces()
        {
            ForceAccumulator = Vector3.Zero;
            TorqueAccumulator = Vector3.Zero;
        }

        /// <summary>
        /// Gets the current world-space position of a vertex.
        /// </summary>
        public Vector3 GetWorldVertex(int vertexIndex)
        {
            if (vertexIndex < 0 || vertexIndex >= Vertices.Count)
                return Vector3.Zero;

            Vector3 localVertex = Vertices[vertexIndex] - Centroid;
            return Vector3.Transform(localVertex, Orientation) + Position;
        }

        /// <summary>
        /// Gets all vertices in world space.
        /// </summary>
        public List<Vector3> GetWorldVertices()
        {
            var worldVertices = new List<Vector3>(Vertices.Count);
            foreach (var vertex in Vertices)
            {
                Vector3 localVertex = vertex - Centroid;
                worldVertices.Add(Vector3.Transform(localVertex, Orientation) + Position);
            }
            return worldVertices;
        }

        /// <summary>
        /// Calculates the axis-aligned bounding box in world space.
        /// </summary>
        public (Vector3 min, Vector3 max) GetAABB()
        {
            if (Vertices.Count == 0)
                return (Vector3.Zero, Vector3.Zero);

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var vertex in Vertices)
            {
                Vector3 worldVertex = GetWorldVertex(Vertices.IndexOf(vertex));
                min = Vector3.Min(min, worldVertex);
                max = Vector3.Max(max, worldVertex);
            }

            return (min, max);
        }
    }
}
