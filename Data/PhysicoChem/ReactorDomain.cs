// GeoscientistToolkit/Data/PhysicoChem/ReactorDomain.cs
//
// Reactor domain definition with geometry, materials, and initial conditions

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Defines a spatial domain within the reactor with specific geometry,
/// material properties, and initial conditions.
/// </summary>
public class ReactorDomain
{
    [JsonProperty]
    public string Name { get; set; }

    [JsonProperty]
    public ReactorGeometry Geometry { get; set; }

    [JsonProperty]
    public MaterialProperties Material { get; set; }

    [JsonProperty]
    public InitialConditions InitialConditions { get; set; }

    [JsonProperty]
    public BooleanOp? BooleanOperation { get; set; }

    [JsonProperty]
    public List<ReactorDomain> ParentDomains { get; set; }

    [JsonProperty]
    public bool IsActive { get; set; } = true;

    [JsonProperty]
    public bool AllowInteraction { get; set; } = true; // Can reactants cross boundary?

    public ReactorDomain()
    {
    }

    public ReactorDomain(string name, ReactorGeometry geometry)
    {
        Name = name;
        Geometry = geometry;
    }
}

/// <summary>
/// Material properties for a domain
/// </summary>
public class MaterialProperties
{
    [JsonProperty]
    public double Porosity { get; set; } = 0.3;

    [JsonProperty]
    public double Permeability { get; set; } = 1e-12; // m²

    [JsonProperty]
    public double ThermalConductivity { get; set; } = 2.0; // W/(m·K)

    [JsonProperty]
    public double SpecificHeat { get; set; } = 1000.0; // J/(kg·K)

    [JsonProperty]
    public double Density { get; set; } = 2500.0; // kg/m³

    [JsonProperty]
    public string MineralComposition { get; set; } = "Quartz";

    [JsonProperty]
    public Dictionary<string, double> MineralFractions { get; set; } = new();
}

/// <summary>
/// Initial conditions for a domain
/// </summary>
public class InitialConditions
{
    [JsonProperty]
    public double Temperature { get; set; } = 298.15; // K

    [JsonProperty]
    public double Pressure { get; set; } = 101325.0; // Pa

    [JsonProperty]
    public Dictionary<string, double> Concentrations { get; set; } = new();

    [JsonProperty]
    public (double Vx, double Vy, double Vz) InitialVelocity { get; set; } = (0, 0, 0);

    [JsonProperty]
    public double LiquidSaturation { get; set; } = 1.0;

    [JsonProperty]
    public string FluidType { get; set; } = "Water";
}

/// <summary>
/// Reactor geometry definition with 2D-to-3D interpolation support
/// </summary>
public class ReactorGeometry
{
    [JsonProperty]
    public GeometryType Type { get; set; }

    [JsonProperty]
    public Interpolation2D3DMode InterpolationMode { get; set; } = Interpolation2D3DMode.Extrusion;

    // For primitive shapes
    [JsonProperty]
    public (double X, double Y, double Z) Center { get; set; }

    [JsonProperty]
    public (double Width, double Height, double Depth) Dimensions { get; set; }

    [JsonProperty]
    public double Radius { get; set; }

    [JsonProperty]
    public double InnerRadius { get; set; }

    [JsonProperty]
    public double Height { get; set; }

    // For 2D profile definition
    [JsonProperty]
    public List<(double X, double Y)> Profile2D { get; set; } = new();

    [JsonProperty]
    public double ExtrusionDepth { get; set; } = 1.0;

    [JsonProperty]
    public int RadialSegments { get; set; } = 36; // For cylindrical geometries

    // For custom 3D mesh
    [JsonProperty]
    public List<(double X, double Y, double Z)> CustomPoints { get; set; } = new();

    [JsonProperty]
    public string MeshFilePath { get; set; }

    /// <summary>
    /// Check if a point is inside this geometry
    /// </summary>
    public bool ContainsPoint(double x, double y, double z)
    {
        switch (Type)
        {
            case GeometryType.Box:
                return IsInsideBox(x, y, z);

            case GeometryType.Sphere:
                return IsInsideSphere(x, y, z);

            case GeometryType.Cylinder:
                return IsInsideCylinder(x, y, z);

            case GeometryType.Cone:
                return IsInsideCone(x, y, z);

            case GeometryType.Custom2D:
                return IsInside2DExtrusion(x, y, z);

            default:
                return false;
        }
    }

    private bool IsInsideBox(double x, double y, double z)
    {
        double halfW = Dimensions.Width / 2.0;
        double halfH = Dimensions.Height / 2.0;
        double halfD = Dimensions.Depth / 2.0;

        return Math.Abs(x - Center.X) <= halfW &&
               Math.Abs(y - Center.Y) <= halfH &&
               Math.Abs(z - Center.Z) <= halfD;
    }

    private bool IsInsideSphere(double x, double y, double z)
    {
        double dx = x - Center.X;
        double dy = y - Center.Y;
        double dz = z - Center.Z;
        double r2 = dx * dx + dy * dy + dz * dz;

        return r2 <= Radius * Radius;
    }

    private bool IsInsideCylinder(double x, double y, double z)
    {
        // Cylinder along Z-axis
        double dx = x - Center.X;
        double dy = y - Center.Y;
        double r2 = dx * dx + dy * dy;

        bool inRadius = r2 <= Radius * Radius;
        bool inHeight = Math.Abs(z - Center.Z) <= Height / 2.0;

        if (InnerRadius > 0)
        {
            bool outsideInner = r2 >= InnerRadius * InnerRadius;
            return inRadius && outsideInner && inHeight;
        }

        return inRadius && inHeight;
    }

    private bool IsInsideCone(double x, double y, double z)
    {
        // Cone with apex at top, base at bottom
        double dx = x - Center.X;
        double dy = y - Center.Y;
        double dz = z - Center.Z;

        double heightFraction = (dz + Height / 2.0) / Height;
        if (heightFraction < 0 || heightFraction > 1) return false;

        double r2 = dx * dx + dy * dy;
        double maxRadius = Radius * (1.0 - heightFraction);

        return r2 <= maxRadius * maxRadius;
    }

    private bool IsInside2DExtrusion(double x, double y, double z)
    {
        if (Profile2D.Count < 3) return false;

        // Check Z bounds for extrusion
        if (Math.Abs(z - Center.Z) > ExtrusionDepth / 2.0)
            return false;

        // Point-in-polygon test for 2D profile
        return IsPointInPolygon(x, y, Profile2D);
    }

    private bool IsPointInPolygon(double x, double y, List<(double X, double Y)> polygon)
    {
        bool inside = false;
        int n = polygon.Count;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = polygon[i].X, yi = polygon[i].Y;
            double xj = polygon[j].X, yj = polygon[j].Y;

            bool intersect = ((yi > y) != (yj > y)) &&
                            (x < (xj - xi) * (y - yi) / (yj - yi) + xi);
            if (intersect)
                inside = !inside;
        }

        return inside;
    }
}

/// <summary>
/// Types of reactor geometries
/// </summary>
public enum GeometryType
{
    Box,
    Sphere,
    Cylinder,
    Cone,
    Torus,
    Parallelepiped,
    Custom2D,
    Custom3D,
    FromMesh
}

/// <summary>
/// 2D to 3D interpolation modes
/// </summary>
public enum Interpolation2D3DMode
{
    /// <summary>
    /// Linear extrusion of 2D profile along Z-axis
    /// </summary>
    Extrusion,

    /// <summary>
    /// Rotation of 2D profile around Z-axis (axisymmetric)
    /// </summary>
    Revolution,

    /// <summary>
    /// Interpret as vertical profile and interpolate horizontally
    /// </summary>
    VerticalProfile
}
