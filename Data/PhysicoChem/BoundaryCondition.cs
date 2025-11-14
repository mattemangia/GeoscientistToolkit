// GeoscientistToolkit/Data/PhysicoChem/BoundaryCondition.cs
//
// Boundary condition definitions for reactor simulations

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GeoscientistToolkit.Data.PhysicoChem;

/// <summary>
/// Boundary condition definition for reactor simulations
/// </summary>
public class BoundaryCondition
{
    [JsonProperty]
    public string Name { get; set; }

    [JsonProperty]
    public BoundaryType Type { get; set; }

    [JsonProperty]
    public BoundaryLocation Location { get; set; }

    [JsonProperty]
    public BoundaryVariable Variable { get; set; }

    // For fixed value (Dirichlet) BC
    [JsonProperty]
    public double Value { get; set; }

    // For flux (Neumann) BC
    [JsonProperty]
    public double FluxValue { get; set; }

    // For time-dependent BC
    [JsonProperty]
    public bool IsTimeDependendent { get; set; }

    [JsonProperty]
    public string TimeExpression { get; set; } // e.g., "300 + 10*sin(t/100)"

    // For species-specific BC
    [JsonProperty]
    public string SpeciesName { get; set; }

    // For custom boundary region
    [JsonProperty]
    public Func<double, double, double, bool> CustomRegion { get; set; }

    [JsonProperty]
    public (double X, double Y, double Z) CustomRegionCenter { get; set; }

    [JsonProperty]
    public double CustomRegionRadius { get; set; }

    // Enable/disable flag
    [JsonProperty]
    public bool IsActive { get; set; } = true;

    public BoundaryCondition()
    {
    }

    public BoundaryCondition(string name, BoundaryType type, BoundaryLocation location)
    {
        Name = name;
        Type = type;
        Location = location;
    }

    /// <summary>
    /// Evaluate time-dependent BC at given time
    /// </summary>
    public double EvaluateAtTime(double time)
    {
        if (!IsTimeDependendent)
            return Value;

        // Simple expression evaluator (can be enhanced)
        return EvaluateExpression(TimeExpression, time);
    }

    private double EvaluateExpression(string expr, double t)
    {
        // Very simple parser - can be replaced with NCalc or similar
        try
        {
            expr = expr.Replace("t", t.ToString("F6"));
            // This is a placeholder - in production use a proper expression evaluator
            return Value; // Fallback to static value
        }
        catch
        {
            return Value;
        }
    }

    /// <summary>
    /// Check if a point is on this boundary
    /// </summary>
    public bool IsOnBoundary(double x, double y, double z, (double X, double Y, double Z) domainSize)
    {
        const double tolerance = 1e-6;

        switch (Location)
        {
            case BoundaryLocation.XMin:
                return Math.Abs(x) < tolerance;
            case BoundaryLocation.XMax:
                return Math.Abs(x - domainSize.X) < tolerance;
            case BoundaryLocation.YMin:
                return Math.Abs(y) < tolerance;
            case BoundaryLocation.YMax:
                return Math.Abs(y - domainSize.Y) < tolerance;
            case BoundaryLocation.ZMin:
                return Math.Abs(z) < tolerance;
            case BoundaryLocation.ZMax:
                return Math.Abs(z - domainSize.Z) < tolerance;
            case BoundaryLocation.Custom:
                if (CustomRegion != null)
                    return CustomRegion(x, y, z);
                else
                {
                    // Check if within custom region radius
                    double dx = x - CustomRegionCenter.X;
                    double dy = y - CustomRegionCenter.Y;
                    double dz = z - CustomRegionCenter.Z;
                    return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= CustomRegionRadius;
                }
            default:
                return false;
        }
    }
}

/// <summary>
/// Types of boundary conditions
/// </summary>
public enum BoundaryType
{
    /// <summary>
    /// Fixed value (Dirichlet) - e.g., T = 300 K
    /// </summary>
    FixedValue,

    /// <summary>
    /// Fixed flux (Neumann) - e.g., dT/dn = 10 W/m²
    /// </summary>
    FixedFlux,

    /// <summary>
    /// Zero flux (insulated/no-flow)
    /// </summary>
    ZeroFlux,

    /// <summary>
    /// Convective (Robin) - e.g., -k*dT/dn = h*(T - T_ambient)
    /// </summary>
    Convective,

    /// <summary>
    /// Periodic (wrap-around)
    /// </summary>
    Periodic,

    /// <summary>
    /// Free (open boundary)
    /// </summary>
    Open,

    /// <summary>
    /// No-slip wall (for flow)
    /// </summary>
    NoSlipWall,

    /// <summary>
    /// Free-slip wall (for flow)
    /// </summary>
    FreeSlipWall,

    /// <summary>
    /// Inlet (specified velocity/composition)
    /// </summary>
    Inlet,

    /// <summary>
    /// Outlet (zero gradient)
    /// </summary>
    Outlet,

    /// <summary>
    /// Interactive (can be disabled to allow reactant mixing)
    /// </summary>
    Interactive,

    /// <summary>
    /// Custom user-defined
    /// </summary>
    Custom
}

/// <summary>
/// Location of boundary in domain
/// </summary>
public enum BoundaryLocation
{
    XMin,
    XMax,
    YMin,
    YMax,
    ZMin,
    ZMax,
    AllFaces,
    Custom
}

/// <summary>
/// Physical variable the BC applies to
/// </summary>
public enum BoundaryVariable
{
    Temperature,
    Pressure,
    Velocity,
    VelocityX,
    VelocityY,
    VelocityZ,
    Concentration,
    HeatFlux,
    MassFlux
}

/// <summary>
/// Nucleation site for mineral precipitation
/// </summary>
public class NucleationSite
{
    [JsonProperty]
    public string Name { get; set; }

    [JsonProperty]
    public (double X, double Y, double Z) Position { get; set; }

    [JsonProperty]
    public string MineralType { get; set; }

    [JsonProperty]
    public double NucleationRate { get; set; } // nuclei/s

    [JsonProperty]
    public double InitialRadius { get; set; } = 1e-6; // m (1 micron)

    [JsonProperty]
    public double ActivationEnergy { get; set; } = 50000.0; // J/mol

    [JsonProperty]
    public double CriticalSupersaturation { get; set; } = 1.5;

    [JsonProperty]
    public bool IsActive { get; set; } = true;

    public NucleationSite()
    {
    }

    public NucleationSite(string name, (double X, double Y, double Z) position, string mineralType)
    {
        Name = name;
        Position = position;
        MineralType = mineralType;
    }

    /// <summary>
    /// Calculate nucleation rate based on supersaturation
    /// Using Classical Nucleation Theory (CNT)
    /// </summary>
    public double CalculateNucleationRate(double supersaturation, double temperature)
    {
        if (supersaturation < CriticalSupersaturation)
            return 0.0;

        const double R = 8.314; // J/(mol·K)
        const double k_B = 1.380649e-23; // J/K

        // Simplified CNT: J = A * exp(-ΔG*/kT)
        // ΔG* ∝ 1/(ln(S))²
        double lnS = Math.Log(supersaturation);
        if (lnS <= 0) return 0.0;

        double deltaG_star = ActivationEnergy / (lnS * lnS);
        double J = NucleationRate * Math.Exp(-deltaG_star / (R * temperature));

        return J;
    }
}

/// <summary>
/// Force field definition (gravity, vortex, custom)
/// </summary>
public class ForceField
{
    [JsonProperty]
    public string Name { get; set; }

    [JsonProperty]
    public ForceType Type { get; set; }

    [JsonProperty]
    public bool IsActive { get; set; } = true;

    // For gravity
    [JsonProperty]
    public (double X, double Y, double Z) GravityVector { get; set; } = (0, 0, -9.81);

    // For vortex
    [JsonProperty]
    public (double X, double Y, double Z) VortexCenter { get; set; }

    [JsonProperty]
    public (double X, double Y, double Z) VortexAxis { get; set; } = (0, 0, 1);

    [JsonProperty]
    public double VortexStrength { get; set; } = 1.0; // rad/s

    [JsonProperty]
    public double VortexRadius { get; set; } = 1.0; // m

    // For custom force
    [JsonProperty]
    public Func<double, double, double, double, (double Fx, double Fy, double Fz)> CustomForce { get; set; }

    // For time-dependent forces
    [JsonProperty]
    public bool IsTimeDependendent { get; set; }

    public ForceField()
    {
    }

    public ForceField(string name, ForceType type)
    {
        Name = name;
        Type = type;
    }

    /// <summary>
    /// Calculate force at a given position and time
    /// </summary>
    public (double Fx, double Fy, double Fz) CalculateForce(double x, double y, double z, double t, double density)
    {
        switch (Type)
        {
            case ForceType.Gravity:
                return (GravityVector.X * density,
                        GravityVector.Y * density,
                        GravityVector.Z * density);

            case ForceType.Vortex:
                return CalculateVortexForce(x, y, z, density);

            case ForceType.Centrifugal:
                return CalculateCentrifugalForce(x, y, z, density);

            case ForceType.Custom:
                if (CustomForce != null)
                    return CustomForce(x, y, z, t);
                return (0, 0, 0);

            default:
                return (0, 0, 0);
        }
    }

    private (double, double, double) CalculateVortexForce(double x, double y, double z, double density)
    {
        // Vortex force: F = ρ * ω² * r (centripetal)
        double dx = x - VortexCenter.X;
        double dy = y - VortexCenter.Y;
        double dz = z - VortexCenter.Z;

        // Project onto plane perpendicular to vortex axis
        double r = Math.Sqrt(dx * dx + dy * dy);

        if (r < 1e-10) return (0, 0, 0);

        // Tangential velocity: v = ω * r
        double omega = VortexStrength;
        double v_tangential = omega * r;

        // Centripetal acceleration: a = v²/r = ω² * r
        double a_centripetal = omega * omega * r;

        // Force directed toward vortex center
        double fx = -a_centripetal * density * (dx / r);
        double fy = -a_centripetal * density * (dy / r);

        return (fx, fy, 0);
    }

    private (double, double, double) CalculateCentrifugalForce(double x, double y, double z, double density)
    {
        // Similar to vortex but outward
        var (fx, fy, fz) = CalculateVortexForce(x, y, z, density);
        return (-fx, -fy, -fz);
    }
}

/// <summary>
/// Types of force fields
/// </summary>
public enum ForceType
{
    Gravity,
    Vortex,
    Centrifugal,
    ElectricField,
    MagneticField,
    Custom
}
