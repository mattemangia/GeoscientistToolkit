// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/GeometricPrimitives2D.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

/// <summary>
/// Types of geometric primitives
/// </summary>
public enum PrimitiveType2D
{
    // Basic shapes
    Rectangle,
    Circle,
    Ellipse,
    Triangle,
    Polygon,

    // Engineering structures
    Foundation,
    Footing,
    RetainingWall,
    Dam,
    Tunnel,
    Excavation,
    Embankment,
    Anchor,
    Pile,

    // Geological features
    Layer,
    Lens,
    Intrusion,
    Fault,
    Fold,

    // Test objects (penetration, loading)
    Indenter,
    Probe,
    Plate,

    // Custom
    UserDefined
}

/// <summary>
/// Behavior type for primitives
/// </summary>
public enum PrimitiveBehavior
{
    Deformable,         // Normal FEM element
    Rigid,              // Moves as rigid body
    Fixed,              // Fixed in place
    Prescribed,         // Prescribed displacement/velocity
    Contact             // Contact surface only
}

/// <summary>
/// Base class for all geometric primitives
/// </summary>
public abstract class GeometricPrimitive2D
{
    public int Id { get; set; }
    public string Name { get; set; }
    public PrimitiveType2D Type { get; protected set; }
    public PrimitiveBehavior Behavior { get; set; } = PrimitiveBehavior.Deformable;

    // Position and orientation
    public Vector2 Position { get; set; }           // Center/reference point
    public double Rotation { get; set; }            // Degrees CCW from horizontal

    // Material
    public int MaterialId { get; set; }

    // Boundary conditions
    public bool FixedX { get; set; }
    public bool FixedY { get; set; }
    public bool FixedRotation { get; set; }
    public Vector2? PrescribedVelocity { get; set; }
    public Vector2? PrescribedDisplacement { get; set; }
    public double? PrescribedAngularVelocity { get; set; }

    // Applied loads
    public Vector2 AppliedForce { get; set; }
    public double AppliedMoment { get; set; }
    public double AppliedPressure { get; set; }

    // Visualization
    public Vector4 Color { get; set; } = new(0.5f, 0.5f, 0.5f, 1f);
    public bool IsVisible { get; set; } = true;
    public bool ShowMesh { get; set; } = true;

    // Mesh generation
    public double MeshSize { get; set; } = 0.5;
    public bool UseQuadElements { get; set; } = false;

    // State
    public Vector2 CurrentPosition { get; set; }
    public double CurrentRotation { get; set; }
    public Vector2 TotalDisplacement { get; set; }
    public double TotalRotation { get; set; }
    public Vector2 ReactionForce { get; set; }
    public double ReactionMoment { get; set; }

    /// <summary>
    /// Get vertices of the primitive boundary
    /// </summary>
    public abstract List<Vector2> GetVertices(int resolution = 32);

    /// <summary>
    /// Get the boundary polygon for meshing
    /// </summary>
    public abstract List<Vector2> GetBoundaryPolygon(int resolution = 32);

    /// <summary>
    /// Check if a point is inside the primitive
    /// </summary>
    public abstract bool ContainsPoint(Vector2 point);

    /// <summary>
    /// Get bounding box
    /// </summary>
    public abstract (Vector2 min, Vector2 max) GetBoundingBox();

    /// <summary>
    /// Get area of the primitive
    /// </summary>
    public abstract double GetArea();

    /// <summary>
    /// Get perimeter/circumference
    /// </summary>
    public abstract double GetPerimeter();

    /// <summary>
    /// Generate mesh elements for this primitive
    /// </summary>
    public virtual void GenerateMesh(FEMMesh2D mesh)
    {
        var polygon = GetBoundaryPolygon();
        mesh.GeneratePolygonMesh(polygon, MaterialId, MeshSize);
    }

    /// <summary>
    /// Apply boundary conditions to mesh nodes
    /// </summary>
    public virtual void ApplyBoundaryConditions(FEMMesh2D mesh)
    {
        foreach (var node in mesh.Nodes)
        {
            if (ContainsPoint(node.InitialPosition))
            {
                if (Behavior == PrimitiveBehavior.Fixed || Behavior == PrimitiveBehavior.Rigid)
                {
                    node.FixedX = FixedX || Behavior == PrimitiveBehavior.Fixed;
                    node.FixedY = FixedY || Behavior == PrimitiveBehavior.Fixed;
                }

                if (PrescribedDisplacement.HasValue)
                {
                    node.PrescribedUx = PrescribedDisplacement.Value.X;
                    node.PrescribedUy = PrescribedDisplacement.Value.Y;
                }
            }
        }
    }

    /// <summary>
    /// Transform a point by primitive's position and rotation
    /// </summary>
    protected Vector2 TransformPoint(Vector2 localPoint)
    {
        double rad = Rotation * Math.PI / 180;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);

        return new Vector2(
            Position.X + (float)(localPoint.X * cos - localPoint.Y * sin),
            Position.Y + (float)(localPoint.X * sin + localPoint.Y * cos)
        );
    }

    protected Vector2 InverseTransformPoint(Vector2 worldPoint)
    {
        double rad = -Rotation * Math.PI / 180;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);

        var local = worldPoint - Position;
        return new Vector2(
            (float)(local.X * cos - local.Y * sin),
            (float)(local.X * sin + local.Y * cos)
        );
    }

    public virtual GeometricPrimitive2D Clone()
    {
        return (GeometricPrimitive2D)MemberwiseClone();
    }
}

/// <summary>
/// Rectangle primitive
/// </summary>
public class RectanglePrimitive : GeometricPrimitive2D
{
    public double Width { get; set; } = 2.0;
    public double Height { get; set; } = 1.0;

    public RectanglePrimitive()
    {
        Type = PrimitiveType2D.Rectangle;
        Name = "Rectangle";
    }

    public override List<Vector2> GetVertices(int resolution = 32)
    {
        double hw = Width / 2;
        double hh = Height / 2;

        return new List<Vector2>
        {
            TransformPoint(new Vector2((float)-hw, (float)-hh)),
            TransformPoint(new Vector2((float)hw, (float)-hh)),
            TransformPoint(new Vector2((float)hw, (float)hh)),
            TransformPoint(new Vector2((float)-hw, (float)hh))
        };
    }

    public override List<Vector2> GetBoundaryPolygon(int resolution = 32) => GetVertices();

    public override bool ContainsPoint(Vector2 point)
    {
        var local = InverseTransformPoint(point);
        return Math.Abs(local.X) <= Width / 2 && Math.Abs(local.Y) <= Height / 2;
    }

    public override (Vector2 min, Vector2 max) GetBoundingBox()
    {
        var vertices = GetVertices();
        return (
            new Vector2(vertices.Min(v => v.X), vertices.Min(v => v.Y)),
            new Vector2(vertices.Max(v => v.X), vertices.Max(v => v.Y))
        );
    }

    public override double GetArea() => Width * Height;
    public override double GetPerimeter() => 2 * (Width + Height);

    public override void GenerateMesh(FEMMesh2D mesh)
    {
        int nx = Math.Max(2, (int)(Width / MeshSize));
        int ny = Math.Max(2, (int)(Height / MeshSize));

        var (min, _) = GetBoundingBox();
        mesh.GenerateRectangularMesh(min, Width, Height, nx, ny, MaterialId);
    }
}

/// <summary>
/// Circle primitive
/// </summary>
public class CirclePrimitive : GeometricPrimitive2D
{
    public double Radius { get; set; } = 1.0;

    public CirclePrimitive()
    {
        Type = PrimitiveType2D.Circle;
        Name = "Circle";
    }

    public override List<Vector2> GetVertices(int resolution = 32)
    {
        var vertices = new List<Vector2>();
        for (int i = 0; i < resolution; i++)
        {
            double angle = 2 * Math.PI * i / resolution;
            vertices.Add(TransformPoint(new Vector2(
                (float)(Radius * Math.Cos(angle)),
                (float)(Radius * Math.Sin(angle)))));
        }
        return vertices;
    }

    public override List<Vector2> GetBoundaryPolygon(int resolution = 32) => GetVertices(resolution);

    public override bool ContainsPoint(Vector2 point)
    {
        return Vector2.Distance(point, Position) <= Radius;
    }

    public override (Vector2 min, Vector2 max) GetBoundingBox()
    {
        return (
            Position - new Vector2((float)Radius, (float)Radius),
            Position + new Vector2((float)Radius, (float)Radius)
        );
    }

    public override double GetArea() => Math.PI * Radius * Radius;
    public override double GetPerimeter() => 2 * Math.PI * Radius;

    public override void GenerateMesh(FEMMesh2D mesh)
    {
        int nRadial = Math.Max(3, (int)(Radius / MeshSize));
        int nCirc = Math.Max(8, (int)(2 * Math.PI * Radius / MeshSize));
        mesh.GenerateCircleMesh(Position, Radius, nRadial, nCirc, MaterialId);
    }
}

/// <summary>
/// Ellipse primitive
/// </summary>
public class EllipsePrimitive : GeometricPrimitive2D
{
    public double SemiAxisA { get; set; } = 2.0;  // Horizontal
    public double SemiAxisB { get; set; } = 1.0;  // Vertical

    public EllipsePrimitive()
    {
        Type = PrimitiveType2D.Ellipse;
        Name = "Ellipse";
    }

    public override List<Vector2> GetVertices(int resolution = 32)
    {
        var vertices = new List<Vector2>();
        for (int i = 0; i < resolution; i++)
        {
            double angle = 2 * Math.PI * i / resolution;
            vertices.Add(TransformPoint(new Vector2(
                (float)(SemiAxisA * Math.Cos(angle)),
                (float)(SemiAxisB * Math.Sin(angle)))));
        }
        return vertices;
    }

    public override List<Vector2> GetBoundaryPolygon(int resolution = 32) => GetVertices(resolution);

    public override bool ContainsPoint(Vector2 point)
    {
        var local = InverseTransformPoint(point);
        double normX = local.X / SemiAxisA;
        double normY = local.Y / SemiAxisB;
        return normX * normX + normY * normY <= 1;
    }

    public override (Vector2 min, Vector2 max) GetBoundingBox()
    {
        // Approximate for rotated ellipse
        double maxExtent = Math.Max(SemiAxisA, SemiAxisB);
        return (
            Position - new Vector2((float)maxExtent, (float)maxExtent),
            Position + new Vector2((float)maxExtent, (float)maxExtent)
        );
    }

    public override double GetArea() => Math.PI * SemiAxisA * SemiAxisB;
    public override double GetPerimeter() => 2 * Math.PI * Math.Sqrt((SemiAxisA * SemiAxisA + SemiAxisB * SemiAxisB) / 2);
}

/// <summary>
/// Polygon primitive for arbitrary shapes
/// </summary>
public class PolygonPrimitive : GeometricPrimitive2D
{
    public List<Vector2> LocalVertices { get; set; } = new();

    public PolygonPrimitive()
    {
        Type = PrimitiveType2D.Polygon;
        Name = "Polygon";
    }

    public override List<Vector2> GetVertices(int resolution = 32)
    {
        return LocalVertices.Select(v => TransformPoint(v)).ToList();
    }

    public override List<Vector2> GetBoundaryPolygon(int resolution = 32) => GetVertices();

    public override bool ContainsPoint(Vector2 point)
    {
        var local = InverseTransformPoint(point);
        return PointInPolygon(local, LocalVertices);
    }

    public override (Vector2 min, Vector2 max) GetBoundingBox()
    {
        var vertices = GetVertices();
        if (vertices.Count == 0) return (Vector2.Zero, Vector2.Zero);
        return (
            new Vector2(vertices.Min(v => v.X), vertices.Min(v => v.Y)),
            new Vector2(vertices.Max(v => v.X), vertices.Max(v => v.Y))
        );
    }

    public override double GetArea()
    {
        if (LocalVertices.Count < 3) return 0;

        double area = 0;
        int j = LocalVertices.Count - 1;
        for (int i = 0; i < LocalVertices.Count; i++)
        {
            area += (LocalVertices[j].X + LocalVertices[i].X) *
                    (LocalVertices[j].Y - LocalVertices[i].Y);
            j = i;
        }
        return Math.Abs(area / 2);
    }

    public override double GetPerimeter()
    {
        if (LocalVertices.Count < 2) return 0;
        double perimeter = 0;
        for (int i = 0; i < LocalVertices.Count; i++)
        {
            int j = (i + 1) % LocalVertices.Count;
            perimeter += Vector2.Distance(LocalVertices[i], LocalVertices[j]);
        }
        return perimeter;
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
}

/// <summary>
/// Foundation/footing primitive
/// </summary>
public class FoundationPrimitive : RectanglePrimitive
{
    public double EmbedmentDepth { get; set; } = 0.5;
    public double BearingPressure { get; set; } = 100e3;  // Pa
    public bool IsStrip { get; set; } = true;  // Strip vs isolated

    public FoundationPrimitive()
    {
        Type = PrimitiveType2D.Foundation;
        Name = "Foundation";
        Color = new Vector4(0.6f, 0.6f, 0.6f, 1f);
        Behavior = PrimitiveBehavior.Rigid;
    }

    public override void ApplyBoundaryConditions(FEMMesh2D mesh)
    {
        base.ApplyBoundaryConditions(mesh);

        // Apply bearing pressure to bottom of foundation
        var bottomNodes = new List<int>();
        double bottomY = Position.Y - (float)(Height / 2);

        foreach (var node in mesh.Nodes)
        {
            if (ContainsPoint(node.InitialPosition) &&
                Math.Abs(node.InitialPosition.Y - bottomY) < MeshSize)
            {
                bottomNodes.Add(node.Id);
            }
        }

        // Distribute bearing load
        if (bottomNodes.Count > 0)
        {
            double forcePerNode = BearingPressure * Width / bottomNodes.Count;
            foreach (int nodeId in bottomNodes)
            {
                mesh.Nodes[nodeId].Fy -= forcePerNode;
            }
        }
    }

    /// <summary>
    /// Calculate ultimate bearing capacity (Terzaghi)
    /// </summary>
    public double CalculateBearingCapacity(GeomechanicalMaterial2D soil)
    {
        double phi = soil.FrictionAngle * Math.PI / 180;
        double c = soil.Cohesion;
        double gamma = soil.UnitWeight;
        double B = Width;
        double D = EmbedmentDepth;

        // Terzaghi bearing capacity factors
        double Nq = Math.Exp(Math.PI * Math.Tan(phi)) * Math.Pow(Math.Tan(Math.PI / 4 + phi / 2), 2);
        double Nc = (Nq - 1) / Math.Tan(phi);
        double Ngamma = 2 * (Nq + 1) * Math.Tan(phi);

        // Strip footing
        double qu = c * Nc + gamma * D * Nq + 0.5 * gamma * B * Ngamma;

        return qu;
    }
}

/// <summary>
/// Retaining wall primitive
/// </summary>
public class RetainingWallPrimitive : GeometricPrimitive2D
{
    public double Height { get; set; } = 5.0;
    public double TopWidth { get; set; } = 0.5;
    public double BaseWidth { get; set; } = 2.0;
    public double ToeLength { get; set; } = 0.5;
    public double HeelLength { get; set; } = 1.0;
    public double BaseThickness { get; set; } = 0.5;
    public bool HasCounterfort { get; set; } = false;

    public RetainingWallPrimitive()
    {
        Type = PrimitiveType2D.RetainingWall;
        Name = "Retaining Wall";
        Color = new Vector4(0.65f, 0.65f, 0.65f, 1f);
    }

    public override List<Vector2> GetVertices(int resolution = 32)
    {
        // Trapezoidal stem + base
        var vertices = new List<Vector2>
        {
            TransformPoint(new Vector2((float)(-ToeLength), 0)),
            TransformPoint(new Vector2((float)(BaseWidth - ToeLength), 0)),
            TransformPoint(new Vector2((float)(BaseWidth - ToeLength), (float)BaseThickness)),
            TransformPoint(new Vector2((float)(TopWidth - ToeLength), (float)(Height + BaseThickness))),
            TransformPoint(new Vector2((float)(-ToeLength), (float)(Height + BaseThickness))),
            TransformPoint(new Vector2((float)(-ToeLength), (float)BaseThickness))
        };

        return vertices;
    }

    public override List<Vector2> GetBoundaryPolygon(int resolution = 32) => GetVertices();

    public override bool ContainsPoint(Vector2 point)
    {
        var local = InverseTransformPoint(point);
        var polygon = new List<Vector2>
        {
            new((float)(-ToeLength), 0),
            new((float)(BaseWidth - ToeLength), 0),
            new((float)(BaseWidth - ToeLength), (float)BaseThickness),
            new((float)(TopWidth - ToeLength), (float)(Height + BaseThickness)),
            new((float)(-ToeLength), (float)(Height + BaseThickness)),
            new((float)(-ToeLength), (float)BaseThickness)
        };

        return PointInPolygon(local, polygon);
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

    public override (Vector2 min, Vector2 max) GetBoundingBox()
    {
        var vertices = GetVertices();
        return (
            new Vector2(vertices.Min(v => v.X), vertices.Min(v => v.Y)),
            new Vector2(vertices.Max(v => v.X), vertices.Max(v => v.Y))
        );
    }

    public override double GetArea()
    {
        // Trapezoid stem + base
        double stemArea = 0.5 * (TopWidth + BaseWidth) * Height;
        double baseArea = BaseWidth * BaseThickness;
        return stemArea + baseArea;
    }

    public override double GetPerimeter()
    {
        var vertices = GetVertices();
        double perimeter = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            int j = (i + 1) % vertices.Count;
            perimeter += Vector2.Distance(vertices[i], vertices[j]);
        }
        return perimeter;
    }

    /// <summary>
    /// Calculate active earth pressure (Rankine)
    /// </summary>
    public double CalculateActiveForce(GeomechanicalMaterial2D backfill)
    {
        double phi = backfill.FrictionAngle * Math.PI / 180;
        double gamma = backfill.UnitWeight;
        double Ka = Math.Pow(Math.Tan(Math.PI / 4 - phi / 2), 2);
        return 0.5 * Ka * gamma * Height * Height;
    }
}

/// <summary>
/// Dam primitive (gravity dam cross-section)
/// </summary>
public class DamPrimitive : GeometricPrimitive2D
{
    public double Height { get; set; } = 30.0;
    public double CrestWidth { get; set; } = 5.0;
    public double BaseWidth { get; set; } = 25.0;
    public double UpstreamSlope { get; set; } = 0.1;    // H:V
    public double DownstreamSlope { get; set; } = 0.7;  // H:V
    public double WaterLevel { get; set; } = 28.0;      // From base
    public bool HasGallery { get; set; } = false;

    public DamPrimitive()
    {
        Type = PrimitiveType2D.Dam;
        Name = "Gravity Dam";
        Color = new Vector4(0.7f, 0.7f, 0.7f, 1f);
    }

    public override List<Vector2> GetVertices(int resolution = 32)
    {
        double usWidth = UpstreamSlope * Height;
        double dsWidth = DownstreamSlope * Height;

        return new List<Vector2>
        {
            TransformPoint(new Vector2(0, 0)),  // Upstream toe
            TransformPoint(new Vector2((float)BaseWidth, 0)),  // Downstream toe
            TransformPoint(new Vector2((float)(BaseWidth - dsWidth), (float)Height)),  // DS crest
            TransformPoint(new Vector2((float)(usWidth + CrestWidth), (float)Height)),  // Crest
            TransformPoint(new Vector2((float)usWidth, (float)Height)),  // US crest
            TransformPoint(new Vector2(0, 0))  // Back to toe
        };
    }

    public override List<Vector2> GetBoundaryPolygon(int resolution = 32)
    {
        var vertices = GetVertices();
        vertices.RemoveAt(vertices.Count - 1); // Remove duplicate
        return vertices;
    }

    public override bool ContainsPoint(Vector2 point)
    {
        var local = InverseTransformPoint(point);
        var polygon = GetBoundaryPolygon().Select(v => InverseTransformPoint(v)).ToList();
        return PointInPolygonCheck(local, polygon);
    }

    private bool PointInPolygonCheck(Vector2 point, List<Vector2> polygon)
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

    public override (Vector2 min, Vector2 max) GetBoundingBox()
    {
        var vertices = GetVertices();
        return (
            new Vector2(vertices.Min(v => v.X), vertices.Min(v => v.Y)),
            new Vector2(vertices.Max(v => v.X), vertices.Max(v => v.Y))
        );
    }

    public override double GetArea()
    {
        return 0.5 * (CrestWidth + BaseWidth) * Height;
    }

    public override double GetPerimeter()
    {
        var vertices = GetBoundaryPolygon();
        double perimeter = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            int j = (i + 1) % vertices.Count;
            perimeter += Vector2.Distance(vertices[i], vertices[j]);
        }
        return perimeter;
    }

    /// <summary>
    /// Calculate hydrostatic pressure distribution
    /// </summary>
    public double GetHydrostaticForce(double waterDensity = 1000)
    {
        return 0.5 * waterDensity * 9.81 * WaterLevel * WaterLevel;
    }
}

/// <summary>
/// Tunnel/excavation primitive
/// </summary>
public class TunnelPrimitive : GeometricPrimitive2D
{
    public double Width { get; set; } = 10.0;
    public double Height { get; set; } = 8.0;
    public double ArchRadius { get; set; } = 5.0;
    public bool HasInvert { get; set; } = true;
    public double LiningThickness { get; set; } = 0.3;

    public TunnelPrimitive()
    {
        Type = PrimitiveType2D.Tunnel;
        Name = "Tunnel";
        Color = new Vector4(0.2f, 0.2f, 0.2f, 0.5f);
    }

    public override List<Vector2> GetVertices(int resolution = 32)
    {
        var vertices = new List<Vector2>();

        // Left wall
        vertices.Add(TransformPoint(new Vector2((float)(-Width / 2), 0)));

        // Arch (semicircle or partial)
        int archPoints = resolution / 2;
        for (int i = 0; i <= archPoints; i++)
        {
            double angle = Math.PI * i / archPoints;
            double x = ArchRadius * Math.Cos(angle);
            double y = Height - ArchRadius + ArchRadius * Math.Sin(angle);
            vertices.Add(TransformPoint(new Vector2((float)x, (float)y)));
        }

        // Right wall
        vertices.Add(TransformPoint(new Vector2((float)(Width / 2), 0)));

        // Invert if present
        if (HasInvert)
        {
            for (int i = archPoints; i >= 0; i--)
            {
                double angle = Math.PI * i / archPoints;
                double x = -ArchRadius * Math.Cos(angle);
                double y = -ArchRadius * Math.Sin(angle) * 0.3; // Flatter invert
                vertices.Add(TransformPoint(new Vector2((float)x, (float)y)));
            }
        }

        return vertices;
    }

    public override List<Vector2> GetBoundaryPolygon(int resolution = 32) => GetVertices(resolution);

    public override bool ContainsPoint(Vector2 point)
    {
        var local = InverseTransformPoint(point);

        // Simple approximation: rectangle with arch
        if (Math.Abs(local.X) > Width / 2) return false;
        if (local.Y < 0 || local.Y > Height) return false;

        // Check arch region
        if (local.Y > Height - ArchRadius)
        {
            double distFromCenter = Math.Sqrt(local.X * local.X +
                Math.Pow(local.Y - (Height - ArchRadius), 2));
            return distFromCenter <= ArchRadius;
        }

        return true;
    }

    public override (Vector2 min, Vector2 max) GetBoundingBox()
    {
        return (
            Position - new Vector2((float)(Width / 2), 0),
            Position + new Vector2((float)(Width / 2), (float)Height)
        );
    }

    public override double GetArea()
    {
        double rectArea = Width * (Height - ArchRadius);
        double archArea = Math.PI * ArchRadius * ArchRadius / 2;
        return rectArea + archArea;
    }

    public override double GetPerimeter()
    {
        return 2 * (Height - ArchRadius) + Width + Math.PI * ArchRadius;
    }
}

/// <summary>
/// Indenter/probe for penetration tests
/// </summary>
public class IndenterPrimitive : GeometricPrimitive2D
{
    public double Width { get; set; } = 0.5;
    public double Height { get; set; } = 1.0;
    public double TipAngle { get; set; } = 60;  // Degrees (full angle)
    public bool IsFlat { get; set; } = false;

    public IndenterPrimitive()
    {
        Type = PrimitiveType2D.Indenter;
        Name = "Indenter";
        Color = new Vector4(0.3f, 0.3f, 0.4f, 1f);
        Behavior = PrimitiveBehavior.Rigid;
    }

    public override List<Vector2> GetVertices(int resolution = 32)
    {
        var vertices = new List<Vector2>();

        if (IsFlat)
        {
            // Flat punch
            vertices.Add(TransformPoint(new Vector2((float)(-Width / 2), 0)));
            vertices.Add(TransformPoint(new Vector2((float)(Width / 2), 0)));
            vertices.Add(TransformPoint(new Vector2((float)(Width / 2), (float)Height)));
            vertices.Add(TransformPoint(new Vector2((float)(-Width / 2), (float)Height)));
        }
        else
        {
            // Wedge/cone indenter
            double tipHeight = (Width / 2) / Math.Tan(TipAngle * Math.PI / 360);
            vertices.Add(TransformPoint(new Vector2(0, 0)));  // Tip
            vertices.Add(TransformPoint(new Vector2((float)(Width / 2), (float)tipHeight)));
            vertices.Add(TransformPoint(new Vector2((float)(Width / 2), (float)Height)));
            vertices.Add(TransformPoint(new Vector2((float)(-Width / 2), (float)Height)));
            vertices.Add(TransformPoint(new Vector2((float)(-Width / 2), (float)tipHeight)));
        }

        return vertices;
    }

    public override List<Vector2> GetBoundaryPolygon(int resolution = 32) => GetVertices();

    public override bool ContainsPoint(Vector2 point)
    {
        var local = InverseTransformPoint(point);
        if (local.Y < 0 || local.Y > Height) return false;
        if (Math.Abs(local.X) > Width / 2) return false;

        if (!IsFlat)
        {
            double tipHeight = (Width / 2) / Math.Tan(TipAngle * Math.PI / 360);
            if (local.Y < tipHeight)
            {
                double allowedX = local.Y * Math.Tan(TipAngle * Math.PI / 360);
                return Math.Abs(local.X) <= allowedX;
            }
        }

        return true;
    }

    public override (Vector2 min, Vector2 max) GetBoundingBox()
    {
        return (
            Position - new Vector2((float)(Width / 2), 0),
            Position + new Vector2((float)(Width / 2), (float)Height)
        );
    }

    public override double GetArea()
    {
        if (IsFlat)
            return Width * Height;

        double tipHeight = (Width / 2) / Math.Tan(TipAngle * Math.PI / 360);
        return 0.5 * Width * tipHeight + Width * (Height - tipHeight);
    }

    public override double GetPerimeter()
    {
        var vertices = GetVertices();
        double perimeter = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            int j = (i + 1) % vertices.Count;
            perimeter += Vector2.Distance(vertices[i], vertices[j]);
        }
        return perimeter;
    }
}

/// <summary>
/// Manager for primitives in a simulation
/// </summary>
public class PrimitiveManager2D
{
    public List<GeometricPrimitive2D> Primitives { get; } = new();
    private int _nextId = 1;

    public int AddPrimitive(GeometricPrimitive2D primitive)
    {
        primitive.Id = _nextId++;
        Primitives.Add(primitive);
        return primitive.Id;
    }

    public void RemovePrimitive(int id)
    {
        Primitives.RemoveAll(p => p.Id == id);
    }

    public GeometricPrimitive2D GetPrimitive(int id)
    {
        return Primitives.FirstOrDefault(p => p.Id == id);
    }

    public GeometricPrimitive2D GetPrimitiveAt(Vector2 point)
    {
        // Return topmost primitive containing point
        for (int i = Primitives.Count - 1; i >= 0; i--)
        {
            if (Primitives[i].ContainsPoint(point))
                return Primitives[i];
        }
        return null;
    }

    public void GenerateAllMeshes(FEMMesh2D mesh)
    {
        foreach (var primitive in Primitives.Where(p => p.IsVisible))
        {
            primitive.GenerateMesh(mesh);
        }
    }

    public void ApplyAllBoundaryConditions(FEMMesh2D mesh)
    {
        foreach (var primitive in Primitives)
        {
            primitive.ApplyBoundaryConditions(mesh);
        }
    }

    public void Clear()
    {
        Primitives.Clear();
        _nextId = 1;
    }

    /// <summary>
    /// Create preset configurations
    /// </summary>
    public static class Presets
    {
        public static List<GeometricPrimitive2D> CreateBearingCapacityTest(double footingWidth = 2, double soilDepth = 10)
        {
            return new List<GeometricPrimitive2D>
            {
                new RectanglePrimitive
                {
                    Name = "Soil",
                    Position = new Vector2((float)(footingWidth * 2), (float)(-soilDepth / 2)),
                    Width = footingWidth * 8,
                    Height = soilDepth,
                    Color = new Vector4(0.6f, 0.5f, 0.3f, 1f)
                },
                new FoundationPrimitive
                {
                    Name = "Footing",
                    Position = new Vector2((float)(footingWidth * 2), 0.25f),
                    Width = footingWidth,
                    Height = 0.5,
                    Behavior = PrimitiveBehavior.Rigid,
                    FixedX = true,
                    BearingPressure = 200e3
                }
            };
        }

        public static List<GeometricPrimitive2D> CreateIndentationTest(double indenterWidth = 0.5, double specimenSize = 10)
        {
            return new List<GeometricPrimitive2D>
            {
                new RectanglePrimitive
                {
                    Name = "Specimen",
                    Position = new Vector2((float)(specimenSize / 2), (float)(-specimenSize / 2)),
                    Width = specimenSize,
                    Height = specimenSize,
                    Color = new Vector4(0.7f, 0.6f, 0.4f, 1f)
                },
                new IndenterPrimitive
                {
                    Name = "Indenter",
                    Position = new Vector2((float)(specimenSize / 2), 0.5f),
                    Width = indenterWidth,
                    Height = 1.0,
                    TipAngle = 60,
                    Behavior = PrimitiveBehavior.Prescribed,
                    PrescribedDisplacement = new Vector2(0, -0.1f)
                }
            };
        }

        public static List<GeometricPrimitive2D> CreateRetainingWallAnalysis(double wallHeight = 5)
        {
            return new List<GeometricPrimitive2D>
            {
                new RectanglePrimitive
                {
                    Name = "Foundation Soil",
                    Position = new Vector2(5, -2.5f),
                    Width = 15,
                    Height = 5,
                    Color = new Vector4(0.5f, 0.4f, 0.3f, 1f)
                },
                new RectanglePrimitive
                {
                    Name = "Backfill",
                    Position = new Vector2(8, (float)(wallHeight / 2)),
                    Width = 6,
                    Height = wallHeight,
                    Color = new Vector4(0.6f, 0.5f, 0.35f, 1f)
                },
                new RetainingWallPrimitive
                {
                    Name = "Retaining Wall",
                    Position = new Vector2(3, 0),
                    Height = wallHeight,
                    BaseWidth = 2.5,
                    TopWidth = 0.5,
                    Behavior = PrimitiveBehavior.Deformable
                }
            };
        }
    }
}
