// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/JointSet2D.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

/// <summary>
/// Types of discontinuities in rock masses
/// </summary>
public enum DiscontinuityType
{
    Joint,              // Natural fracture with no displacement
    Fault,              // Fracture with displacement
    Bedding,            // Sedimentary layering plane
    Cleavage,           // Metamorphic foliation
    Schistosity,        // Aligned platy minerals
    Vein,               // Filled fracture
    Shear,              // Shear zone
    Tension,            // Mode I fracture
    UserDefined         // Custom discontinuity
}

/// <summary>
/// Persistence (continuity) type for joints
/// </summary>
public enum JointPersistence
{
    Continuous,         // Extends through entire model
    SemiContinuous,     // Partially penetrates
    Discontinuous       // Terminates within rock
}

/// <summary>
/// Represents a single discontinuity (joint, fault, etc.)
/// </summary>
public class Discontinuity2D
{
    public int Id { get; set; }
    public DiscontinuityType Type { get; set; } = DiscontinuityType.Joint;

    // Geometry
    public Vector2 StartPoint { get; set; }
    public Vector2 EndPoint { get; set; }
    public List<Vector2> Points { get; set; } = new();  // For curved discontinuities

    // Orientation
    public double DipAngle { get; set; }        // Degrees from horizontal
    public double StrikeAngle { get; set; }     // 2D: angle from X-axis

    // Mechanical properties
    public double Cohesion { get; set; } = 0;           // Pa
    public double FrictionAngle { get; set; } = 30;     // degrees
    public double TensileStrength { get; set; } = 0;    // Pa
    public double NormalStiffness { get; set; } = 1e10; // Pa/m
    public double ShearStiffness { get; set; } = 1e9;   // Pa/m
    public double DilationAngle { get; set; } = 0;      // degrees

    // Roughness (JRC - Joint Roughness Coefficient)
    public double JRC { get; set; } = 10;               // 0-20 scale

    // Aperture and infill
    public double Aperture { get; set; } = 0.001;       // m
    public bool HasInfill { get; set; }
    public string InfillMaterial { get; set; }

    // State
    public bool IsOpen { get; set; }
    public bool IsSliding { get; set; }
    public double CurrentNormalStress { get; set; }
    public double CurrentShearStress { get; set; }
    public double AccumulatedSlip { get; set; }

    // Visualization
    public Vector4 Color { get; set; } = new(0.2f, 0.2f, 0.8f, 1f);
    public bool IsVisible { get; set; } = true;

    // Parent joint set
    public int JointSetId { get; set; } = -1;

    public double Length => Points.Count > 1
        ? Points.Zip(Points.Skip(1), (a, b) => Vector2.Distance(a, b)).Sum()
        : Vector2.Distance(StartPoint, EndPoint);

    public Vector2 Direction => Vector2.Normalize(EndPoint - StartPoint);
    public Vector2 Normal => new(-Direction.Y, Direction.X);

    /// <summary>
    /// Get shear strength using Barton-Bandis criterion for rough joints
    /// τ = σn·tan(φb + JRC·log10(JCS/σn))
    /// </summary>
    public double GetShearStrength(double normalStress, double jointWallStrength = 100e6)
    {
        if (normalStress <= 0) return Cohesion;

        double phiB = FrictionAngle * Math.PI / 180;
        double jrcRad = JRC * Math.Log10(jointWallStrength / normalStress) * Math.PI / 180;

        return normalStress * Math.Tan(phiB + jrcRad) + Cohesion;
    }

    /// <summary>
    /// Check if joint is in failure state
    /// </summary>
    public bool CheckFailure(double normalStress, double shearStress)
    {
        // Tension failure
        if (normalStress > TensileStrength)
        {
            IsOpen = true;
            return true;
        }

        // Shear failure
        double shearStrength = GetShearStrength(normalStress);
        if (Math.Abs(shearStress) > shearStrength)
        {
            IsSliding = true;
            return true;
        }

        return false;
    }

    public Discontinuity2D Clone()
    {
        return new Discontinuity2D
        {
            Id = Id,
            Type = Type,
            StartPoint = StartPoint,
            EndPoint = EndPoint,
            Points = new List<Vector2>(Points),
            DipAngle = DipAngle,
            StrikeAngle = StrikeAngle,
            Cohesion = Cohesion,
            FrictionAngle = FrictionAngle,
            TensileStrength = TensileStrength,
            NormalStiffness = NormalStiffness,
            ShearStiffness = ShearStiffness,
            DilationAngle = DilationAngle,
            JRC = JRC,
            Aperture = Aperture,
            HasInfill = HasInfill,
            InfillMaterial = InfillMaterial,
            Color = Color,
            JointSetId = JointSetId
        };
    }
}

/// <summary>
/// Represents a set of parallel or statistically distributed joints
/// </summary>
public class JointSet2D
{
    public int Id { get; set; }
    public string Name { get; set; } = "Joint Set";
    public DiscontinuityType Type { get; set; } = DiscontinuityType.Joint;

    // Orientation parameters
    public double MeanDipAngle { get; set; } = 45;          // degrees
    public double DipAngleStdDev { get; set; } = 5;         // degrees
    public double MeanStrikeAngle { get; set; } = 0;        // degrees
    public double StrikeAngleStdDev { get; set; } = 5;      // degrees

    // Spacing parameters
    public double MeanSpacing { get; set; } = 1.0;          // m
    public double SpacingStdDev { get; set; } = 0.2;        // m
    public bool UniformSpacing { get; set; } = true;

    // Persistence parameters
    public JointPersistence Persistence { get; set; } = JointPersistence.SemiContinuous;
    public double MeanPersistence { get; set; } = 5.0;      // m (length)
    public double PersistenceStdDev { get; set; } = 1.0;    // m
    public double TerminationProbability { get; set; } = 0.3;

    // Mechanical properties (inherited by individual joints)
    public double Cohesion { get; set; } = 0;
    public double FrictionAngle { get; set; } = 30;
    public double TensileStrength { get; set; } = 0;
    public double NormalStiffness { get; set; } = 1e10;
    public double ShearStiffness { get; set; } = 1e9;
    public double DilationAngle { get; set; } = 0;
    public double JRC { get; set; } = 10;

    // Generated joints
    public List<Discontinuity2D> Joints { get; } = new();

    // Visualization
    public Vector4 Color { get; set; } = new(0.3f, 0.3f, 0.9f, 1f);
    public bool IsVisible { get; set; } = true;

    private Random _random = new();

    /// <summary>
    /// Generate joints within a rectangular region
    /// </summary>
    public void GenerateInRegion(Vector2 minBound, Vector2 maxBound, int? seed = null)
    {
        if (seed.HasValue)
            _random = new Random(seed.Value);

        Joints.Clear();

        double width = maxBound.X - minBound.X;
        double height = maxBound.Y - minBound.Y;

        // Determine number of joints based on spacing
        double diagonalLength = Math.Sqrt(width * width + height * height);
        int numJoints = (int)(diagonalLength / MeanSpacing);

        double dipRad = MeanDipAngle * Math.PI / 180;
        Vector2 direction = new((float)Math.Cos(dipRad), (float)Math.Sin(dipRad));
        Vector2 perpendicular = new(-direction.Y, direction.X);

        // Generate joints perpendicular to the set orientation
        double currentOffset = 0;
        int jointId = 0;

        while (currentOffset < diagonalLength)
        {
            // Spacing with variability
            double spacing = UniformSpacing
                ? MeanSpacing
                : MeanSpacing + _random.NextGaussian() * SpacingStdDev;
            spacing = Math.Max(spacing, 0.01);

            currentOffset += spacing;

            // Dip angle with variability
            double dip = MeanDipAngle + _random.NextGaussian() * DipAngleStdDev;
            double dipRadVar = dip * Math.PI / 180;
            Vector2 jointDir = new((float)Math.Cos(dipRadVar), (float)Math.Sin(dipRadVar));

            // Calculate joint position along perpendicular
            Vector2 basePoint = minBound + perpendicular * (float)currentOffset;

            // Determine joint length based on persistence
            double jointLength;
            if (Persistence == JointPersistence.Continuous)
            {
                jointLength = diagonalLength * 2;
            }
            else
            {
                jointLength = MeanPersistence + _random.NextGaussian() * PersistenceStdDev;
                jointLength = Math.Max(jointLength, 0.1);
            }

            // Create joint
            Vector2 start = basePoint - jointDir * (float)(jointLength / 2);
            Vector2 end = basePoint + jointDir * (float)(jointLength / 2);

            // Clip to region bounds
            ClipToRegion(ref start, ref end, minBound, maxBound);

            if (Vector2.Distance(start, end) > 0.01)
            {
                var joint = new Discontinuity2D
                {
                    Id = jointId++,
                    Type = Type,
                    StartPoint = start,
                    EndPoint = end,
                    DipAngle = dip,
                    Cohesion = Cohesion,
                    FrictionAngle = FrictionAngle,
                    TensileStrength = TensileStrength,
                    NormalStiffness = NormalStiffness,
                    ShearStiffness = ShearStiffness,
                    DilationAngle = DilationAngle,
                    JRC = JRC,
                    Color = Color,
                    JointSetId = Id
                };

                joint.Points.Add(start);
                joint.Points.Add(end);

                Joints.Add(joint);
            }
        }
    }

    /// <summary>
    /// Generate joints within a polygonal region
    /// </summary>
    public void GenerateInPolygon(List<Vector2> polygon, int? seed = null)
    {
        if (polygon.Count < 3) return;

        // Get bounding box
        float minX = polygon.Min(p => p.X);
        float maxX = polygon.Max(p => p.X);
        float minY = polygon.Min(p => p.Y);
        float maxY = polygon.Max(p => p.Y);

        // Generate in bounding box first
        GenerateInRegion(new Vector2(minX, minY), new Vector2(maxX, maxY), seed);

        // Filter joints to only those within polygon
        var validJoints = new List<Discontinuity2D>();
        foreach (var joint in Joints)
        {
            var clipped = ClipJointToPolygon(joint, polygon);
            if (clipped != null && clipped.Length > 0.01)
            {
                validJoints.Add(clipped);
            }
        }

        Joints.Clear();
        Joints.AddRange(validJoints);
    }

    /// <summary>
    /// Generate conjugate joint set (two sets at symmetric angles)
    /// Common in tectonic settings
    /// </summary>
    public static (JointSet2D set1, JointSet2D set2) CreateConjugate(
        double bisectorAngle, double conjugateAngle, double spacing)
    {
        var set1 = new JointSet2D
        {
            Name = "Conjugate Set 1",
            MeanDipAngle = bisectorAngle + conjugateAngle / 2,
            MeanSpacing = spacing,
            Color = new Vector4(0.9f, 0.3f, 0.3f, 1f)
        };

        var set2 = new JointSet2D
        {
            Name = "Conjugate Set 2",
            MeanDipAngle = bisectorAngle - conjugateAngle / 2,
            MeanSpacing = spacing,
            Color = new Vector4(0.3f, 0.3f, 0.9f, 1f)
        };

        return (set1, set2);
    }

    private void ClipToRegion(ref Vector2 start, ref Vector2 end, Vector2 min, Vector2 max)
    {
        // Cohen-Sutherland line clipping
        const int INSIDE = 0, LEFT = 1, RIGHT = 2, BOTTOM = 4, TOP = 8;

        int ComputeCode(Vector2 p)
        {
            int code = INSIDE;
            if (p.X < min.X) code |= LEFT;
            else if (p.X > max.X) code |= RIGHT;
            if (p.Y < min.Y) code |= BOTTOM;
            else if (p.Y > max.Y) code |= TOP;
            return code;
        }

        int code1 = ComputeCode(start);
        int code2 = ComputeCode(end);

        while (true)
        {
            if ((code1 | code2) == 0)
            {
                break; // Both inside
            }
            else if ((code1 & code2) != 0)
            {
                start = end = Vector2.Zero; // Both outside same region
                break;
            }
            else
            {
                int codeOut = code1 != 0 ? code1 : code2;
                Vector2 p = Vector2.Zero;

                if ((codeOut & TOP) != 0)
                {
                    p.X = start.X + (end.X - start.X) * (max.Y - start.Y) / (end.Y - start.Y);
                    p.Y = max.Y;
                }
                else if ((codeOut & BOTTOM) != 0)
                {
                    p.X = start.X + (end.X - start.X) * (min.Y - start.Y) / (end.Y - start.Y);
                    p.Y = min.Y;
                }
                else if ((codeOut & RIGHT) != 0)
                {
                    p.Y = start.Y + (end.Y - start.Y) * (max.X - start.X) / (end.X - start.X);
                    p.X = max.X;
                }
                else if ((codeOut & LEFT) != 0)
                {
                    p.Y = start.Y + (end.Y - start.Y) * (min.X - start.X) / (end.X - start.X);
                    p.X = min.X;
                }

                if (codeOut == code1)
                {
                    start = p;
                    code1 = ComputeCode(start);
                }
                else
                {
                    end = p;
                    code2 = ComputeCode(end);
                }
            }
        }
    }

    private Discontinuity2D ClipJointToPolygon(Discontinuity2D joint, List<Vector2> polygon)
    {
        // Simplified - just check if midpoint is inside
        var mid = (joint.StartPoint + joint.EndPoint) / 2;
        if (!PointInPolygon(mid, polygon))
            return null;

        var clipped = joint.Clone();

        // Proper clipping would use Sutherland-Hodgman algorithm
        // For now, just check endpoints and adjust
        if (!PointInPolygon(clipped.StartPoint, polygon))
        {
            var intersection = FindPolygonIntersection(mid, clipped.StartPoint, polygon);
            if (intersection.HasValue) clipped.StartPoint = intersection.Value;
        }

        if (!PointInPolygon(clipped.EndPoint, polygon))
        {
            var intersection = FindPolygonIntersection(mid, clipped.EndPoint, polygon);
            if (intersection.HasValue) clipped.EndPoint = intersection.Value;
        }

        clipped.Points.Clear();
        clipped.Points.Add(clipped.StartPoint);
        clipped.Points.Add(clipped.EndPoint);

        return clipped;
    }

    private bool PointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        int j = polygon.Count - 1;

        for (int i = 0; i < polygon.Count; i++)
        {
            if ((polygon[i].Y < point.Y && polygon[j].Y >= point.Y ||
                 polygon[j].Y < point.Y && polygon[i].Y >= point.Y) &&
                (polygon[i].X + (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) *
                 (polygon[j].X - polygon[i].X) < point.X))
            {
                inside = !inside;
            }
            j = i;
        }

        return inside;
    }

    private Vector2? FindPolygonIntersection(Vector2 inside, Vector2 outside, List<Vector2> polygon)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            int j = (i + 1) % polygon.Count;
            var intersection = LineIntersection(inside, outside, polygon[i], polygon[j]);
            if (intersection.HasValue)
                return intersection;
        }
        return null;
    }

    private Vector2? LineIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        float d1x = a2.X - a1.X;
        float d1y = a2.Y - a1.Y;
        float d2x = b2.X - b1.X;
        float d2y = b2.Y - b1.Y;

        float cross = d1x * d2y - d1y * d2x;
        if (Math.Abs(cross) < 1e-10) return null;

        float t = ((b1.X - a1.X) * d2y - (b1.Y - a1.Y) * d2x) / cross;
        float u = ((b1.X - a1.X) * d1y - (b1.Y - a1.Y) * d1x) / cross;

        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
        {
            return new Vector2(a1.X + t * d1x, a1.Y + t * d1y);
        }

        return null;
    }

    public JointSet2D Clone()
    {
        var clone = new JointSet2D
        {
            Id = Id,
            Name = Name,
            Type = Type,
            MeanDipAngle = MeanDipAngle,
            DipAngleStdDev = DipAngleStdDev,
            MeanStrikeAngle = MeanStrikeAngle,
            StrikeAngleStdDev = StrikeAngleStdDev,
            MeanSpacing = MeanSpacing,
            SpacingStdDev = SpacingStdDev,
            UniformSpacing = UniformSpacing,
            Persistence = Persistence,
            MeanPersistence = MeanPersistence,
            PersistenceStdDev = PersistenceStdDev,
            TerminationProbability = TerminationProbability,
            Cohesion = Cohesion,
            FrictionAngle = FrictionAngle,
            TensileStrength = TensileStrength,
            NormalStiffness = NormalStiffness,
            ShearStiffness = ShearStiffness,
            DilationAngle = DilationAngle,
            JRC = JRC,
            Color = Color,
            IsVisible = IsVisible
        };

        foreach (var joint in Joints)
        {
            clone.Joints.Add(joint.Clone());
        }

        return clone;
    }
}

/// <summary>
/// Manager for multiple joint sets in a model
/// </summary>
public class JointSetManager
{
    public List<JointSet2D> JointSets { get; } = new();
    private int _nextId = 1;

    public int AddJointSet(JointSet2D set)
    {
        set.Id = _nextId++;
        JointSets.Add(set);
        return set.Id;
    }

    public void RemoveJointSet(int id)
    {
        JointSets.RemoveAll(s => s.Id == id);
    }

    public JointSet2D GetJointSet(int id)
    {
        return JointSets.FirstOrDefault(s => s.Id == id);
    }

    public IEnumerable<Discontinuity2D> GetAllJoints()
    {
        return JointSets.Where(s => s.IsVisible).SelectMany(s => s.Joints);
    }

    /// <summary>
    /// Insert joints into FEM mesh as interface elements
    /// </summary>
    public void InsertIntoMesh(FEMMesh2D mesh)
    {
        foreach (var joint in GetAllJoints())
        {
            // Find mesh edges that intersect the joint
            // Create interface elements along the joint trace
            InsertJointAsInterface(mesh, joint);
        }
    }

    private void InsertJointAsInterface(FEMMesh2D mesh, Discontinuity2D joint)
    {
        // Find elements that the joint passes through
        var nodes = mesh.Nodes.ToArray();
        var intersectedElements = new List<FEMElement2D>();

        foreach (var element in mesh.Elements)
        {
            if (JointIntersectsElement(joint, element, nodes))
            {
                intersectedElements.Add(element);
            }
        }

        // For each intersected element, create interface nodes and elements
        foreach (var element in intersectedElements)
        {
            CreateInterfaceForElement(mesh, joint, element);
        }
    }

    private bool JointIntersectsElement(Discontinuity2D joint, FEMElement2D element, FEMNode2D[] nodes)
    {
        // Get element edges
        var vertices = element.NodeIds.Select(id => nodes[id].InitialPosition).ToList();

        // Check if joint segment intersects any edge
        for (int i = 0; i < vertices.Count; i++)
        {
            int j = (i + 1) % vertices.Count;
            if (SegmentsIntersect(joint.StartPoint, joint.EndPoint, vertices[i], vertices[j]))
                return true;
        }

        return false;
    }

    private bool SegmentsIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        float d1 = Cross(b1, b2, a1);
        float d2 = Cross(b1, b2, a2);
        float d3 = Cross(a1, a2, b1);
        float d4 = Cross(a1, a2, b2);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
            ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

        return false;
    }

    private void CreateInterfaceForElement(FEMMesh2D mesh, Discontinuity2D joint, FEMElement2D element)
    {
        // This would typically involve:
        // 1. Finding intersection points of joint with element edges
        // 2. Creating new nodes at intersection points
        // 3. Splitting the element along the joint
        // 4. Creating interface element between the split parts

        // Simplified: just mark element as having ubiquitous joint orientation
        var material = mesh.Materials.GetMaterial(element.MaterialId);
        if (material != null)
        {
            material.HasUbiquitousJoints = true;
            material.JointDipAngle = joint.DipAngle;
            material.JointCohesion = joint.Cohesion;
            material.JointFrictionAngle = joint.FrictionAngle;
        }
    }

    /// <summary>
    /// Create common geological joint set patterns
    /// </summary>
    public static class Presets
    {
        public static JointSet2D CreateVerticalJoints(double spacing = 2.0)
        {
            return new JointSet2D
            {
                Name = "Vertical Joints",
                MeanDipAngle = 90,
                DipAngleStdDev = 3,
                MeanSpacing = spacing,
                FrictionAngle = 35,
                JRC = 8
            };
        }

        public static JointSet2D CreateBeddingPlanes(double spacing = 0.5, double dip = 15)
        {
            return new JointSet2D
            {
                Name = "Bedding Planes",
                Type = DiscontinuityType.Bedding,
                MeanDipAngle = dip,
                DipAngleStdDev = 2,
                MeanSpacing = spacing,
                Persistence = JointPersistence.Continuous,
                FrictionAngle = 25,
                Cohesion = 50000,
                JRC = 5
            };
        }

        public static JointSet2D CreateSchistosity(double spacing = 0.2, double dip = 60)
        {
            return new JointSet2D
            {
                Name = "Schistosity",
                Type = DiscontinuityType.Schistosity,
                MeanDipAngle = dip,
                DipAngleStdDev = 5,
                MeanSpacing = spacing,
                SpacingStdDev = 0.05,
                Persistence = JointPersistence.Continuous,
                FrictionAngle = 22,
                JRC = 3
            };
        }

        public static (JointSet2D, JointSet2D) CreateTectonicConjugate(double sigma1Direction = 0)
        {
            // Conjugate faults typically form at ~30° to σ1
            return JointSet2D.CreateConjugate(sigma1Direction, 60, 5.0);
        }
    }
}

/// <summary>
/// Extension methods for Random to generate Gaussian distribution
/// </summary>
internal static class RandomExtensions
{
    public static double NextGaussian(this Random random)
    {
        // Box-Muller transform
        double u1 = 1.0 - random.NextDouble();
        double u2 = 1.0 - random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
