using System;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Represents a set of parallel discontinuities (joints) in the rock mass.
    /// Based on 3DEC joint set methodology.
    ///
    /// <para><b>Academic References:</b></para>
    ///
    /// <para>Joint set characterization and orientation:</para>
    /// <para>Priest, S. D. (1993). Discontinuity analysis for rock engineering. Chapman &amp; Hall.
    /// ISBN: 978-0412476006</para>
    ///
    /// <para>Joint Roughness Coefficient (JRC):</para>
    /// <para>Barton, N. (1973). Review of a new shear-strength criterion for rock joints.
    /// Engineering Geology, 7(4), 287-332. https://doi.org/10.1016/0013-7952(73)90013-6</para>
    ///
    /// <para>Barton, N., &amp; Choubey, V. (1977). The shear strength of rock joints in theory and
    /// practice. Rock Mechanics, 10(1-2), 1-54. https://doi.org/10.1007/BF01261801</para>
    ///
    /// <para>Joint persistence and trace length:</para>
    /// <para>Dershowitz, W. S., &amp; Einstein, H. H. (1988). Characterizing rock joint geometry with
    /// joint system models. Rock Mechanics and Rock Engineering, 21(1), 21-51.
    /// https://doi.org/10.1007/BF01019674</para>
    ///
    /// <para>Joint stiffness parameters:</para>
    /// <para>Bandis, S. C., Lumsden, A. C., &amp; Barton, N. R. (1983). Fundamentals of rock joint
    /// deformation. International Journal of Rock Mechanics and Mining Sciences &amp; Geomechanics
    /// Abstracts, 20(6), 249-268. https://doi.org/10.1016/0148-9062(83)90595-8</para>
    /// </summary>
    public class JointSet
    {
        // Identification
        public int Id { get; set; }
        public string Name { get; set; }

        // Geometric properties (orientation)
        public float Dip { get; set; }              // degrees (0-90)
        public float DipDirection { get; set; }     // degrees (0-360), azimuth

        // Spacing properties
        public float Spacing { get; set; }          // meters, distance between parallel joints
        public float SpacingStdDev { get; set; }    // standard deviation for stochastic spacing

        // Extent properties
        public float Persistence { get; set; }      // 0-1, fraction of area that is actually jointed
        public float TraceLength { get; set; }      // meters, length of joint traces (for stochastic)

        // Mechanical properties
        public float NormalStiffness { get; set; }  // Pa/m, kn
        public float ShearStiffness { get; set; }   // Pa/m, ks
        public float Cohesion { get; set; }         // Pa
        public float FrictionAngle { get; set; }    // degrees
        public float TensileStrength { get; set; }  // Pa
        public float Dilation { get; set; }         // degrees, dilation angle

        // Joint surface properties
        public float Roughness { get; set; }        // JRC (Joint Roughness Coefficient), 0-20
        public float Aperture { get; set; }         // meters, initial opening

        // Generation mode
        public JointGenerationMode GenerationMode { get; set; }
        public int Seed { get; set; }               // Random seed for stochastic generation

        // Visualization
        public Vector4 Color { get; set; }

        public JointSet()
        {
            Name = "Joint Set";
            Dip = 45.0f;
            DipDirection = 0.0f;
            Spacing = 1.0f;
            SpacingStdDev = 0.1f;
            Persistence = 1.0f;
            TraceLength = 10.0f;
            NormalStiffness = 1e9f;     // 1 GPa/m
            ShearStiffness = 1e8f;      // 100 MPa/m
            Cohesion = 0.0f;
            FrictionAngle = 30.0f;
            TensileStrength = 0.0f;
            Dilation = 0.0f;
            Roughness = 5.0f;
            Aperture = 0.001f;          // 1 mm
            GenerationMode = JointGenerationMode.Deterministic;
            Seed = 12345;
            Color = new Vector4(1.0f, 0.5f, 0.0f, 0.5f);
        }

        /// <summary>
        /// Gets the normal vector of the joint plane.
        /// The normal points "up-dip" following geological convention.
        /// </summary>
        public Vector3 GetNormal()
        {
            // Convert dip and dip direction to normal vector
            // Dip direction is measured clockwise from North (Y-axis in our system)
            // Dip is measured from horizontal

            float dipRad = Dip * MathF.PI / 180.0f;
            float dipDirRad = DipDirection * MathF.PI / 180.0f;

            // Normal vector components
            float nx = MathF.Sin(dipDirRad) * MathF.Sin(dipRad);
            float ny = MathF.Cos(dipDirRad) * MathF.Sin(dipRad);
            float nz = MathF.Cos(dipRad);

            return Vector3.Normalize(new Vector3(nx, ny, nz));
        }

        /// <summary>
        /// Gets the strike vector (horizontal vector perpendicular to dip direction).
        /// </summary>
        public Vector3 GetStrike()
        {
            float dipDirRad = DipDirection * MathF.PI / 180.0f;

            // Strike is 90 degrees counterclockwise from dip direction
            float strikeRad = dipDirRad - MathF.PI / 2.0f;

            float sx = MathF.Sin(strikeRad);
            float sy = MathF.Cos(strikeRad);
            float sz = 0.0f;

            return Vector3.Normalize(new Vector3(sx, sy, sz));
        }

        /// <summary>
        /// Gets the dip vector (vector pointing down-dip in the plane).
        /// </summary>
        public Vector3 GetDipVector()
        {
            Vector3 normal = GetNormal();
            Vector3 up = new Vector3(0, 0, 1);

            // Dip vector is perpendicular to both normal and vertical
            Vector3 dipVec = Vector3.Cross(Vector3.Cross(up, normal), normal);
            return Vector3.Normalize(dipVec);
        }

        /// <summary>
        /// Calculates the signed distance from a point to the joint plane passing through origin.
        /// </summary>
        public float DistanceToPlane(Vector3 point, Vector3 planePoint)
        {
            Vector3 normal = GetNormal();
            return Vector3.Dot(point - planePoint, normal);
        }

        /// <summary>
        /// Projects a point onto the joint plane.
        /// </summary>
        public Vector3 ProjectOntoPlane(Vector3 point, Vector3 planePoint)
        {
            Vector3 normal = GetNormal();
            float distance = DistanceToPlane(point, planePoint);
            return point - distance * normal;
        }

        /// <summary>
        /// Generates plane positions for this joint set within a bounding box.
        /// Returns a list of points on each parallel plane.
        /// </summary>
        public Vector3[] GeneratePlanePositions(Vector3 boundingBoxMin, Vector3 boundingBoxMax)
        {
            Vector3 normal = GetNormal();
            Vector3 boxCenter = (boundingBoxMin + boundingBoxMax) * 0.5f;
            Vector3 boxSize = boundingBoxMax - boundingBoxMin;

            // Maximum dimension to ensure we cover the entire box
            float maxDim = MathF.Max(boxSize.X, MathF.Max(boxSize.Y, boxSize.Z));
            float diagonal = boxSize.Length();

            // Number of planes needed
            int numPlanes = (int)MathF.Ceiling(diagonal / Spacing) + 2;

            // Start position (shifted to negative side)
            float startOffset = -(numPlanes / 2.0f) * Spacing;

            var planePositions = new Vector3[numPlanes];

            var random = GenerationMode == JointGenerationMode.Stochastic
                ? new Random(Seed)
                : null;

            for (int i = 0; i < numPlanes; i++)
            {
                float offset = startOffset + i * Spacing;

                // Add stochastic variation if enabled
                if (GenerationMode == JointGenerationMode.Stochastic && random != null)
                {
                    offset += (float)(random.NextDouble() * 2.0 - 1.0) * SpacingStdDev;
                }

                planePositions[i] = boxCenter + normal * offset;
            }

            return planePositions;
        }

        /// <summary>
        /// Calculates the shear strength at a given normal stress using Mohr-Coulomb criterion.
        /// </summary>
        public float CalculateShearStrength(float normalStress)
        {
            // τ = c + σn * tan(φ)
            float frictionRad = FrictionAngle * MathF.PI / 180.0f;
            return Cohesion + normalStress * MathF.Tan(frictionRad);
        }
    }

    /// <summary>
    /// Joint generation mode (deterministic or stochastic).
    /// </summary>
    public enum JointGenerationMode
    {
        Deterministic,  // Regular spacing
        Stochastic     // Random spacing with statistical distribution
    }
}
