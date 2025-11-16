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

    // Compositional flow properties
    [JsonProperty]
    public Dictionary<string, double> InletComposition { get; set; } = new();

    [JsonProperty]
    public double InletTemperature { get; set; } = 298.15; // K

    [JsonProperty]
    public double InletSalinity { get; set; } = 0.0; // ppt

    [JsonProperty]
    public double InletFlowRate { get; set; } = 0.0; // m³/s or kg/s

    [JsonProperty]
    public bool IsCompositional { get; set; } = false;

    // Phase properties
    [JsonProperty]
    public PhaseType InletPhase { get; set; } = PhaseType.Liquid;

    [JsonProperty]
    public double GasLiquidRatio { get; set; } = 0.0; // For multiphase inlets

    // Surface tension and interfacial properties
    [JsonProperty]
    public double SurfaceTension { get; set; } = 0.072; // N/m (water-air at 25°C)

    [JsonProperty]
    public double ContactAngle { get; set; } = 90.0; // degrees

    [JsonProperty]
    public bool EnableCapillaryEffects { get; set; } = false;

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
    MassFlux,
    Evaporation,      // Evaporation rate
    SurfaceLevel,     // Free surface level
    WaveAmplitude,    // Wave height
    Salinity          // Salinity
}

/// <summary>
/// Phase type for multiphase flows
/// </summary>
public enum PhaseType
{
    Liquid,
    Gas,
    Supercritical,
    TwoPhase,         // Liquid + Gas
    Solid,            // For particle-laden flows
    Mixture           // General mixture
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

    // For wave generation
    [JsonProperty]
    public WaveProperties Wave { get; set; }

    // For wind
    [JsonProperty]
    public WindProperties Wind { get; set; }

    // For underwater currents
    [JsonProperty]
    public CurrentProperties Current { get; set; }

    // For evaporation
    [JsonProperty]
    public EvaporationProperties Evaporation { get; set; }

    // For heat sources
    [JsonProperty]
    public HeatSourceProperties HeatSource { get; set; }

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

            case ForceType.Wave:
                return CalculateWaveForce(x, y, z, t, density);

            case ForceType.Wind:
                return CalculateWindForce(x, y, z, t, density);

            case ForceType.UnderwaterCurrent:
                return CalculateCurrentForce(x, y, z, t, density);

            case ForceType.Buoyancy:
                return CalculateBuoyancyForce(x, y, z, density);

            case ForceType.Coriolis:
                return CalculateCoriolisForce(x, y, z, density);

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

    private (double, double, double) CalculateWaveForce(double x, double y, double z, double t, double density)
    {
        if (Wave == null) return (0, 0, 0);

        double fx = 0, fy = 0, fz = 0;

        switch (Wave.Type)
        {
            case WaveType.Progressive:
                // Progressive wave: η = A * sin(kx - ωt)
                // Orbital velocity: u = Aω * cos(kx - ωt) * exp(kz)
                double k = 2 * Math.PI / Wave.Wavelength;
                double omega = 2 * Math.PI / Wave.Period;
                double phase = k * x - omega * t + Wave.PhaseShift;
                double depthFactor = Math.Exp(k * (z - Wave.WaterLevel));

                fx = density * Wave.Amplitude * omega * Math.Cos(phase) * depthFactor;
                fz = density * Wave.Amplitude * omega * Math.Sin(phase) * depthFactor;
                break;

            case WaveType.Standing:
                // Standing wave: η = A * cos(kx) * cos(ωt)
                double ks = 2 * Math.PI / Wave.Wavelength;
                double omegas = 2 * Math.PI / Wave.Period;

                fx = density * Wave.Amplitude * omegas * Math.Sin(ks * x) * Math.Sin(omegas * t);
                fz = density * Wave.Amplitude * omegas * Math.Cos(ks * x) * Math.Cos(omegas * t);
                break;

            case WaveType.Circular:
                // Circular waves from a point source
                double dx = x - Wave.SourceX;
                double dy = y - Wave.SourceY;
                double r = Math.Sqrt(dx * dx + dy * dy);

                if (r > 0.01)
                {
                    double kr = 2 * Math.PI / Wave.Wavelength;
                    double omegar = 2 * Math.PI / Wave.Period;
                    double amplitude = Wave.Amplitude / Math.Sqrt(r); // Amplitude decay with distance
                    double phaser = kr * r - omegar * t;

                    double radialForce = density * amplitude * omegar * Math.Cos(phaser);
                    fx = radialForce * (dx / r);
                    fy = radialForce * (dy / r);
                    fz = density * amplitude * omegar * Math.Sin(phaser);
                }
                break;
        }

        return (fx, fy, fz);
    }

    private (double, double, double) CalculateWindForce(double x, double y, double z, double t, double density)
    {
        if (Wind == null) return (0, 0, 0);

        // Wind force only applies above water surface
        if (z < Wind.SurfaceLevel)
            return (0, 0, 0);

        // Wind velocity varies with height: v(z) = v_ref * (z/z_ref)^α (power law)
        double heightAboveSurface = z - Wind.SurfaceLevel;
        double velocityRatio = Math.Pow(heightAboveSurface / Wind.ReferenceHeight, Wind.ShearExponent);
        double windSpeed = Wind.Speed * velocityRatio;

        // Add gusts if enabled
        if (Wind.EnableGusts)
        {
            double gustPhase = 2 * Math.PI * t / Wind.GustPeriod;
            windSpeed *= (1 + Wind.GustIntensity * Math.Sin(gustPhase));
        }

        // Wind direction
        double windAngle = Wind.Direction * Math.PI / 180.0; // Convert to radians
        double vx = windSpeed * Math.Cos(windAngle);
        double vy = windSpeed * Math.Sin(windAngle);

        // Drag force: F = 0.5 * ρ_air * Cd * A * v²
        double airDensity = 1.225; // kg/m³
        double dragCoeff = Wind.DragCoefficient;
        double dragForce = 0.5 * airDensity * dragCoeff * windSpeed * windSpeed;

        double fx = dragForce * Math.Cos(windAngle);
        double fy = dragForce * Math.Sin(windAngle);

        return (fx, fy, 0);
    }

    private (double, double, double) CalculateCurrentForce(double x, double y, double z, double t, double density)
    {
        if (Current == null) return (0, 0, 0);

        // Current only applies below water surface
        if (z > Current.SurfaceLevel)
            return (0, 0, 0);

        double fx = 0, fy = 0, fz = 0;

        switch (Current.Type)
        {
            case CurrentType.Uniform:
                // Uniform current
                fx = density * Current.VelocityX;
                fy = density * Current.VelocityY;
                fz = density * Current.VelocityZ;
                break;

            case CurrentType.DepthVarying:
                // Current varies with depth (exponential decay)
                double depthBelowSurface = Current.SurfaceLevel - z;
                double depthFactor = Math.Exp(-depthBelowSurface / Current.CharacteristicDepth);

                fx = density * Current.VelocityX * depthFactor;
                fy = density * Current.VelocityY * depthFactor;
                fz = density * Current.VelocityZ * depthFactor;
                break;

            case CurrentType.Tidal:
                // Tidal current (sinusoidal)
                double tidalPhase = 2 * Math.PI * t / Current.TidalPeriod;
                double tidalFactor = Math.Sin(tidalPhase);

                fx = density * Current.VelocityX * tidalFactor;
                fy = density * Current.VelocityY * tidalFactor;
                break;

            case CurrentType.Vortical:
                // Underwater vortex
                double dxc = x - Current.CenterX;
                double dyc = y - Current.CenterY;
                double rc = Math.Sqrt(dxc * dxc + dyc * dyc);

                if (rc > 0.01)
                {
                    // Tangential velocity for vortex
                    double vtheta = Current.RotationRate * rc;
                    fx = -density * vtheta * (dyc / rc);
                    fy = density * vtheta * (dxc / rc);
                }
                break;
        }

        return (fx, fy, fz);
    }

    private (double, double, double) CalculateBuoyancyForce(double x, double y, double z, double density)
    {
        // Buoyancy: F = ρ * g * V (upward)
        double g = 9.81;
        double fz = density * g;
        return (0, 0, fz);
    }

    private (double, double, double) CalculateCoriolisForce(double x, double y, double z, double density)
    {
        // Coriolis force: F = -2m * Ω × v
        // Simplified for Earth rotation at mid-latitudes
        double omega = 7.2921e-5; // Earth's rotation rate (rad/s)
        double latitude = 45.0 * Math.PI / 180.0; // Default mid-latitude
        double f = 2 * omega * Math.Sin(latitude); // Coriolis parameter

        // Assuming velocity from previous time step (would need to be passed in)
        // For now, return zero - would be calculated in solver with velocity field
        return (0, 0, 0);
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
    Wave,              // Surface and body waves
    Wind,              // Atmospheric wind forces
    UnderwaterCurrent, // Subsurface water currents
    Buoyancy,          // Buoyancy forces from density differences
    Coriolis,          // Coriolis effect for rotating reference frames
    Pressure,          // Pressure gradient forces
    Custom
}

/// <summary>
/// Wave properties for wave force generation
/// </summary>
public class WaveProperties
{
    [JsonProperty]
    public WaveType Type { get; set; } = WaveType.Progressive;

    [JsonProperty]
    public double Amplitude { get; set; } = 0.1; // m

    [JsonProperty]
    public double Wavelength { get; set; } = 2.0; // m

    [JsonProperty]
    public double Period { get; set; } = 2.0; // s

    [JsonProperty]
    public double WaterLevel { get; set; } = 0.0; // m

    [JsonProperty]
    public double PhaseShift { get; set; } = 0.0; // radians

    // For circular waves
    [JsonProperty]
    public double SourceX { get; set; } = 0.0;

    [JsonProperty]
    public double SourceY { get; set; } = 0.0;

    // Chemical composition of incoming wave water
    [JsonProperty]
    public Dictionary<string, double> WaterComposition { get; set; } = new();

    [JsonProperty]
    public double WaterTemperature { get; set; } = 298.15; // K

    [JsonProperty]
    public double WaterSalinity { get; set; } = 0.0; // ppt
}

public enum WaveType
{
    Progressive,  // Traveling wave
    Standing,     // Standing wave (resonance)
    Circular,     // Circular waves from point source
    Solitary,     // Solitary wave (tsunami-like)
    Irregular     // Irregular/random waves
}

/// <summary>
/// Wind properties for atmospheric forcing
/// </summary>
public class WindProperties
{
    [JsonProperty]
    public double Speed { get; set; } = 5.0; // m/s

    [JsonProperty]
    public double Direction { get; set; } = 0.0; // degrees (0 = East, 90 = North)

    [JsonProperty]
    public double SurfaceLevel { get; set; } = 0.0; // m

    [JsonProperty]
    public double ReferenceHeight { get; set; } = 10.0; // m

    [JsonProperty]
    public double ShearExponent { get; set; } = 0.143; // Power law exponent (1/7 for neutral atmosphere)

    [JsonProperty]
    public double DragCoefficient { get; set; } = 0.001; // Wind drag coefficient

    // Gusts
    [JsonProperty]
    public bool EnableGusts { get; set; } = false;

    [JsonProperty]
    public double GustIntensity { get; set; } = 0.3; // Fraction of base speed

    [JsonProperty]
    public double GustPeriod { get; set; } = 10.0; // s

    // Moisture and evaporation
    [JsonProperty]
    public double RelativeHumidity { get; set; } = 0.5; // 0-1

    [JsonProperty]
    public double AirTemperature { get; set; } = 293.15; // K
}

/// <summary>
/// Underwater current properties
/// </summary>
public class CurrentProperties
{
    [JsonProperty]
    public CurrentType Type { get; set; } = CurrentType.Uniform;

    [JsonProperty]
    public double VelocityX { get; set; } = 0.0; // m/s

    [JsonProperty]
    public double VelocityY { get; set; } = 0.0; // m/s

    [JsonProperty]
    public double VelocityZ { get; set; } = 0.0; // m/s

    [JsonProperty]
    public double SurfaceLevel { get; set; } = 0.0; // m

    [JsonProperty]
    public double CharacteristicDepth { get; set; } = 10.0; // m (for depth-varying)

    // For tidal currents
    [JsonProperty]
    public double TidalPeriod { get; set; } = 12.42 * 3600; // s (semi-diurnal tide)

    // For vortical currents
    [JsonProperty]
    public double CenterX { get; set; } = 0.0;

    [JsonProperty]
    public double CenterY { get; set; } = 0.0;

    [JsonProperty]
    public double RotationRate { get; set; } = 0.1; // rad/s

    // Chemical composition of current
    [JsonProperty]
    public Dictionary<string, double> CurrentComposition { get; set; } = new();

    [JsonProperty]
    public double CurrentTemperature { get; set; } = 298.15; // K

    [JsonProperty]
    public double CurrentSalinity { get; set; } = 35.0; // ppt (typical seawater)
}

public enum CurrentType
{
    Uniform,        // Constant velocity
    DepthVarying,   // Velocity varies with depth
    Tidal,          // Tidal oscillations
    Vortical,       // Circular/vortex current
    Stratified      // Density-driven currents
}

/// <summary>
/// Evaporation properties for phase change
/// </summary>
public class EvaporationProperties
{
    [JsonProperty]
    public bool IsActive { get; set; } = false;

    [JsonProperty]
    public double SurfaceLevel { get; set; } = 0.0; // m

    [JsonProperty]
    public EvaporationModel Model { get; set; } = EvaporationModel.Penman;

    // Environmental conditions
    [JsonProperty]
    public double AirTemperature { get; set; } = 293.15; // K

    [JsonProperty]
    public double RelativeHumidity { get; set; } = 0.5; // 0-1

    [JsonProperty]
    public double WindSpeed { get; set; } = 2.0; // m/s

    [JsonProperty]
    public double SolarRadiation { get; set; } = 200.0; // W/m²

    // Empirical coefficients
    [JsonProperty]
    public double MassTransferCoefficient { get; set; } = 0.0013; // m/s

    [JsonProperty]
    public double LatentHeatOfVaporization { get; set; } = 2.45e6; // J/kg

    // Maximum evaporation rate
    [JsonProperty]
    public double MaxEvaporationRate { get; set; } = 1.0e-5; // kg/(m²·s)

    // Salinity effects
    [JsonProperty]
    public bool AccountForSalinity { get; set; } = false;
}

public enum EvaporationModel
{
    Simple,          // Constant rate
    Dalton,          // Dalton's law (mass transfer)
    Penman,          // Penman equation (energy balance)
    PenmanMonteith,  // Penman-Monteith (with surface resistance)
    Priestley Taylor // Priestley-Taylor (simplified)
}

/// <summary>
/// Heat source properties
/// </summary>
public class HeatSourceProperties
{
    [JsonProperty]
    public HeatSourceType Type { get; set; } = HeatSourceType.Volumetric;

    [JsonProperty]
    public double PowerDensity { get; set; } = 1000.0; // W/m³ or W/m²

    [JsonProperty]
    public double Temperature { get; set; } = 373.15; // K (for fixed temperature source)

    // Spatial distribution
    [JsonProperty]
    public (double X, double Y, double Z) Location { get; set; } = (0, 0, 0);

    [JsonProperty]
    public double Radius { get; set; } = 0.5; // m (for localized sources)

    // Time dependence
    [JsonProperty]
    public bool IsTimeDependendent { get; set; } = false;

    [JsonProperty]
    public double StartTime { get; set; } = 0.0; // s

    [JsonProperty]
    public double EndTime { get; set; } = double.MaxValue; // s

    [JsonProperty]
    public double CyclePeriod { get; set; } = 0.0; // s (0 = no cycling)

    // Radiation
    [JsonProperty]
    public double Emissivity { get; set; } = 0.9; // 0-1

    [JsonProperty]
    public double ViewFactor { get; set; } = 1.0; // 0-1

    // Solar radiation
    [JsonProperty]
    public double SolarIntensity { get; set; } = 1000.0; // W/m² (peak)

    [JsonProperty]
    public double SolarAngle { get; set; } = 0.0; // degrees from vertical

    [JsonProperty]
    public bool DiurnalCycle { get; set; } = false;
}

public enum HeatSourceType
{
    Volumetric,      // Heat generation throughout volume (W/m³)
    Surface,         // Heat flux at surface (W/m²)
    Point,           // Point heat source (W)
    FixedTemperature,// Isothermal boundary
    Solar,           // Solar radiation
    Radiative,       // Radiative heat transfer
    Convective,      // Convective heat transfer
    Geothermal       // Geothermal heat from below
}
