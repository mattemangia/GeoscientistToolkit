// GeoscientistToolkit/Data/PhysicoChem/BoundaryCondition.cs
//
// Boundary condition definitions for reactor simulations

using System;
using System.Collections.Generic;
using System.Globalization;
using NCalc;
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
        if (string.IsNullOrWhiteSpace(expr))
            return Value;

        try
        {
            var expression = new Expression(expr, EvaluateOptions.IgnoreCase);
            expression.Parameters["t"] = t;
            var result = expression.Evaluate();
            return Convert.ToDouble(result, CultureInfo.InvariantCulture);
        }
        catch (Exception)
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

    [JsonProperty]
    public GravityProperties Gravity { get; set; }

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

    // For sedimentation
    [JsonProperty]
    public SedimentationProperties Sedimentation { get; set; }

    // For turbulence
    [JsonProperty]
    public TurbulenceProperties Turbulence { get; set; }

    // For chemical reactions
    [JsonProperty]
    public ChemicalReactionProperties ChemicalReaction { get; set; }

    // For phase changes
    [JsonProperty]
    public PhaseChangeProperties PhaseChange { get; set; }

    // For electrokinetic effects
    [JsonProperty]
    public ElectrokineticProperties Electrokinetic { get; set; }

    // For biological processes
    [JsonProperty]
    public BiologicalProperties Biological { get; set; }

    // For acoustic/vibration forces
    [JsonProperty]
    public AcousticProperties Acoustic { get; set; }

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
                return CalculateGravityForce(x, y, z, density);

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

            case ForceType.Sedimentation:
                return CalculateSedimentationForce(x, y, z, density);

            case ForceType.Turbulence:
                return CalculateTurbulenceForce(x, y, z, t, density);

            case ForceType.ChemicalReaction:
                // Chemical reactions are handled separately in reactive transport solver
                return (0, 0, 0);

            case ForceType.PhaseChange:
                // Phase changes are handled separately in phase change solver
                return (0, 0, 0);

            case ForceType.Electrokinetic:
                return CalculateElectrokineticForce(x, y, z, density);

            case ForceType.Biological:
                // Biological processes are handled separately in biofilm/growth solver
                return (0, 0, 0);

            case ForceType.Acoustic:
                return CalculateAcousticForce(x, y, z, t, density);

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

    private (double, double, double) CalculateGravityForce(double x, double y, double z, double density)
    {
        // Enhanced gravity with spatial variations and planetary bodies
        if (Gravity == null)
        {
            // Use simple gravity vector
            return (GravityVector.X * density,
                    GravityVector.Y * density,
                    GravityVector.Z * density);
        }

        double g = Gravity.SurfaceGravity;

        // Apply planetary body preset if specified
        if (Gravity.UsePreset)
        {
            switch (Gravity.PlanetaryBody)
            {
                case PlanetaryBody.Earth:
                    g = 9.81;
                    break;
                case PlanetaryBody.Moon:
                    g = 1.62;
                    break;
                case PlanetaryBody.Mars:
                    g = 3.71;
                    break;
                case PlanetaryBody.Jupiter:
                    g = 24.79;
                    break;
                case PlanetaryBody.Venus:
                    g = 8.87;
                    break;
                case PlanetaryBody.Mercury:
                    g = 3.70;
                    break;
                case PlanetaryBody.Saturn:
                    g = 10.44;
                    break;
                case PlanetaryBody.Microgravity:
                    g = 1e-6;
                    break;
            }
        }

        // Apply altitude variation if enabled
        if (Gravity.EnableAltitudeVariation)
        {
            // g(h) = g0 * (R / (R + h))²
            double altitude = z - Gravity.ReferenceAltitude;
            double radiusPlusAltitude = Gravity.PlanetRadius + altitude;
            g *= (Gravity.PlanetRadius / radiusPlusAltitude) * (Gravity.PlanetRadius / radiusPlusAltitude);
        }

        // Apply latitude variation if enabled (Earth only)
        if (Gravity.EnableLatitudeVariation && Gravity.PlanetaryBody == PlanetaryBody.Earth)
        {
            // g(φ) = 9.78033(1 + 0.0053024sin²φ - 0.0000058sin²2φ)
            double lat = Gravity.Latitude * Math.PI / 180.0;
            double sinLat = Math.Sin(lat);
            double sin2Lat = Math.Sin(2 * lat);
            g = 9.78033 * (1.0 + 0.0053024 * sinLat * sinLat - 0.0000058 * sin2Lat * sin2Lat);
        }

        // Apply density anomaly if specified
        if (Gravity.EnableDensityAnomaly)
        {
            // Calculate distance from anomaly center
            double dx = x - Gravity.AnomalyCenter.X;
            double dy = y - Gravity.AnomalyCenter.Y;
            double dz = z - Gravity.AnomalyCenter.Z;
            double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            if (r < Gravity.AnomalyRadius)
            {
                // Gaussian perturbation
                double perturbation = Gravity.AnomalyStrength * Math.Exp(-r * r / (2 * Gravity.AnomalyRadius * Gravity.AnomalyRadius));
                g += perturbation;
            }
        }

        // Return force in the specified direction
        double fx = Gravity.Direction.X * g * density;
        double fy = Gravity.Direction.Y * g * density;
        double fz = Gravity.Direction.Z * g * density;

        return (fx, fy, fz);
    }

    private (double, double, double) CalculateSedimentationForce(double x, double y, double z, double density)
    {
        if (Sedimentation == null) return (0, 0, 0);

        // Stokes settling velocity: v = (2/9) * (ρp - ρf) * g * r² / μ
        double densityDifference = Sedimentation.ParticleDensity - density;
        double viscosity = Sedimentation.FluidViscosity;
        double particleRadius = Sedimentation.ParticleRadius;
        double g = 9.81;

        double stokesVelocity = (2.0 / 9.0) * densityDifference * g * particleRadius * particleRadius / viscosity;

        // Apply hindered settling correction for concentrated suspensions
        if (Sedimentation.VolumetricConcentration > 0.01)
        {
            // Richardson-Zaki correlation: v_hindered = v_stokes * (1 - C)^n
            double concentration = Sedimentation.VolumetricConcentration;
            double n = Sedimentation.HinderedSettlingExponent; // Typically 4.65 for Re << 1
            stokesVelocity *= Math.Pow(1.0 - concentration, n);
        }

        // Apply turbulent effects if enabled
        if (Sedimentation.EnableTurbulentDispersion)
        {
            // Add random dispersion component (simplified)
            // In practice, would need turbulence field
            double dispersivity = Sedimentation.TurbulentDispersivity;
            // For now, just reduce settling velocity
            stokesVelocity *= (1.0 - dispersivity);
        }

        // Drag force: F = -6πμrv (Stokes drag)
        double dragForce = 6 * Math.PI * viscosity * particleRadius * stokesVelocity;

        // Force is downward (negative z)
        return (0, 0, -dragForce);
    }

    private (double, double, double) CalculateTurbulenceForce(double x, double y, double z, double t, double density)
    {
        if (Turbulence == null) return (0, 0, 0);

        // Turbulence is typically handled in the solver, but we can add simplified effects here
        double fx = 0, fy = 0, fz = 0;

        switch (Turbulence.Model)
        {
            case TurbulenceModel.KOmegaSST:
            case TurbulenceModel.KEpsilon:
                // Reynolds stress approximation
                // τ_ij = -ρ u'_i u'_j ≈ μ_t (∂u_i/∂x_j + ∂u_j/∂x_i)
                // This would require velocity gradients from solver
                // For now, apply eddy viscosity concept
                double eddyViscosity = Turbulence.EddyViscosity;
                double turbulentKineticEnergy = Turbulence.TurbulentKineticEnergy;

                // Simplified turbulent force based on TKE gradient
                // F ≈ ρ * ∇(k)
                // In practice, this is handled by pressure-velocity coupling
                break;

            case TurbulenceModel.LES:
                // Large Eddy Simulation - subgrid scale stress
                // τ_ij = -2 ρ C_s² Δ² |S| S_ij
                double smagorinskyConstant = Turbulence.SmagorinskyConstant;
                double filterWidth = Turbulence.LESFilterWidth;
                // Would need strain rate tensor from solver
                break;

            case TurbulenceModel.DNS:
                // Direct Numerical Simulation - no turbulence model needed
                // All scales are resolved
                break;

            case TurbulenceModel.Laminar:
                // No turbulence
                break;
        }

        return (fx, fy, fz);
    }

    private (double, double, double) CalculateElectrokineticForce(double x, double y, double z, double density)
    {
        if (Electrokinetic == null) return (0, 0, 0);

        double fx = 0, fy = 0, fz = 0;

        // Electroosmotic flow: v_eo = (ε ζ / μ) E
        // where ε = permittivity, ζ = zeta potential, μ = viscosity, E = electric field
        if (Electrokinetic.EnableElectroosmosis)
        {
            double permittivity = Electrokinetic.Permittivity;
            double zetaPotential = Electrokinetic.ZetaPotential;
            double viscosity = Electrokinetic.FluidViscosity;

            double mobility = permittivity * zetaPotential / viscosity;

            fx = density * mobility * Electrokinetic.ElectricField.X;
            fy = density * mobility * Electrokinetic.ElectricField.Y;
            fz = density * mobility * Electrokinetic.ElectricField.Z;
        }

        // Electrophoretic force on charged particles: F = q E
        if (Electrokinetic.EnableElectrophoresis)
        {
            double charge = Electrokinetic.ParticleCharge;

            fx += charge * Electrokinetic.ElectricField.X;
            fy += charge * Electrokinetic.ElectricField.Y;
            fz += charge * Electrokinetic.ElectricField.Z;
        }

        // Dielectrophoretic force: F = 2πr³ε_m Re[K(ω)] ∇|E|²
        if (Electrokinetic.EnableDielectrophoresis)
        {
            double particleRadius = Electrokinetic.ParticleRadius;
            double mediumPermittivity = Electrokinetic.Permittivity;
            double clausius_mossotti = Electrokinetic.ClausiusMossottiFactor;

            // Would need electric field gradient from solver
            // For now, assume uniform field
            double depFactor = 2 * Math.PI * Math.Pow(particleRadius, 3) * mediumPermittivity * clausius_mossotti;

            // Simplified - would need ∇|E|²
            fx += depFactor * Electrokinetic.FieldGradient.X;
            fy += depFactor * Electrokinetic.FieldGradient.Y;
            fz += depFactor * Electrokinetic.FieldGradient.Z;
        }

        return (fx, fy, fz);
    }

    private (double, double, double) CalculateAcousticForce(double x, double y, double z, double t, double density)
    {
        if (Acoustic == null) return (0, 0, 0);

        double fx = 0, fy = 0, fz = 0;

        // Acoustic radiation force (Gor'kov potential)
        // F = -∇U, where U = V₀[f₁κ₀⟨p²⟩ - f₂(3/4ρ₀)⟨v²⟩]

        double frequency = Acoustic.Frequency;
        double amplitude = Acoustic.Amplitude;
        double wavelength = Acoustic.SpeedOfSound / frequency;
        double omega = 2 * Math.PI * frequency;
        double k = 2 * Math.PI / wavelength;

        switch (Acoustic.WaveType)
        {
            case AcousticWaveType.PlaneWave:
                // Plane wave: p = p₀ sin(kx - ωt)
                double phase = k * x - omega * t;
                double pressureAmplitude = amplitude;

                // Radiation force for small particle: F = (π p₀² r³ β)/(2λρc²) sin(2kx)
                double particleRadius = Acoustic.ParticleRadius;
                double compressibility = Acoustic.FluidCompressibility;

                fx = (Math.PI * pressureAmplitude * pressureAmplitude * Math.Pow(particleRadius, 3) * compressibility) /
                     (2 * wavelength * density * Acoustic.SpeedOfSound * Acoustic.SpeedOfSound) *
                     Math.Sin(2 * k * x);
                break;

            case AcousticWaveType.StandingWave:
                // Standing wave: p = 2p₀ sin(kx) cos(ωt)
                double axialForce = -(Math.PI * amplitude * amplitude * Math.Pow(Acoustic.ParticleRadius, 3)) /
                                    (wavelength * density * Acoustic.SpeedOfSound * Acoustic.SpeedOfSound) *
                                    Math.Sin(2 * k * x);

                fx = axialForce * Math.Cos(omega * t);
                break;

            case AcousticWaveType.Focused:
                // Focused acoustic beam - Gaussian profile
                double dx = x - Acoustic.FocusPoint.X;
                double dy = y - Acoustic.FocusPoint.Y;
                double dz = z - Acoustic.FocusPoint.Z;
                double r = Math.Sqrt(dx * dx + dy * dy);
                double beamRadius = Acoustic.BeamRadius;

                double intensity = amplitude * amplitude * Math.Exp(-2 * r * r / (beamRadius * beamRadius));
                double radialGradient = -4 * r / (beamRadius * beamRadius) * intensity;

                if (r > 1e-10)
                {
                    fx = radialGradient * (dx / r);
                    fy = radialGradient * (dy / r);
                }
                break;
        }

        // Add acoustic streaming contribution
        if (Acoustic.EnableAcousticStreaming)
        {
            // Acoustic streaming velocity: v_s ≈ (3α I)/(4ρω²)
            double attenuationCoeff = Acoustic.AttenuationCoefficient;
            double intensity = 0.5 * density * Acoustic.SpeedOfSound * amplitude * amplitude;
            double streamingVelocity = (3 * attenuationCoeff * intensity) / (4 * density * omega * omega);

            // Force from streaming
            fx += density * streamingVelocity * Acoustic.Direction.X;
            fy += density * streamingVelocity * Acoustic.Direction.Y;
            fz += density * streamingVelocity * Acoustic.Direction.Z;
        }

        return (fx, fy, fz);
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
    Sedimentation,     // Particle settling and sedimentation
    Turbulence,        // Turbulent flow effects
    ChemicalReaction,  // Chemical reaction kinetics
    PhaseChange,       // Phase transitions (boiling, freezing, etc.)
    Electrokinetic,    // Electroosmosis, electrophoresis
    Biological,        // Biological processes (biofilms, growth)
    Acoustic,          // Acoustic radiation and vibration forces
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
    PriestleyTaylor // Priestley-Taylor (simplified)
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

/// <summary>
/// Enhanced gravity properties with spatial variations and planetary bodies
/// </summary>
public class GravityProperties
{
    [JsonProperty]
    public double SurfaceGravity { get; set; } = 9.81; // m/s²

    [JsonProperty]
    public (double X, double Y, double Z) Direction { get; set; } = (0, 0, -1); // Unit vector

    [JsonProperty]
    public bool UsePreset { get; set; } = true;

    [JsonProperty]
    public PlanetaryBody PlanetaryBody { get; set; } = PlanetaryBody.Earth;

    // Altitude variation
    [JsonProperty]
    public bool EnableAltitudeVariation { get; set; } = false;

    [JsonProperty]
    public double PlanetRadius { get; set; } = 6.371e6; // m (Earth)

    [JsonProperty]
    public double ReferenceAltitude { get; set; } = 0.0; // m

    // Latitude variation (for Earth)
    [JsonProperty]
    public bool EnableLatitudeVariation { get; set; } = false;

    [JsonProperty]
    public double Latitude { get; set; } = 45.0; // degrees

    // Density anomalies (for geophysical simulations)
    [JsonProperty]
    public bool EnableDensityAnomaly { get; set; } = false;

    [JsonProperty]
    public (double X, double Y, double Z) AnomalyCenter { get; set; } = (0, 0, 0);

    [JsonProperty]
    public double AnomalyRadius { get; set; } = 1000.0; // m

    [JsonProperty]
    public double AnomalyStrength { get; set; } = 0.1; // m/s² (perturbation)
}

public enum PlanetaryBody
{
    Earth,
    Moon,
    Mars,
    Jupiter,
    Venus,
    Mercury,
    Saturn,
    Microgravity
}

/// <summary>
/// Sedimentation and particle settling properties
/// </summary>
public class SedimentationProperties
{
    [JsonProperty]
    public double ParticleDensity { get; set; } = 2650.0; // kg/m³ (quartz)

    [JsonProperty]
    public double ParticleRadius { get; set; } = 1e-5; // m (10 microns)

    [JsonProperty]
    public double FluidViscosity { get; set; } = 1e-3; // Pa·s (water)

    // Particle size distribution
    [JsonProperty]
    public bool UseDistribution { get; set; } = false;

    [JsonProperty]
    public double MeanRadius { get; set; } = 1e-5; // m

    [JsonProperty]
    public double StandardDeviation { get; set; } = 5e-6; // m

    // Hindered settling in concentrated suspensions
    [JsonProperty]
    public double VolumetricConcentration { get; set; } = 0.0; // 0-1

    [JsonProperty]
    public double HinderedSettlingExponent { get; set; } = 4.65; // Richardson-Zaki exponent

    // Turbulent dispersion
    [JsonProperty]
    public bool EnableTurbulentDispersion { get; set; } = false;

    [JsonProperty]
    public double TurbulentDispersivity { get; set; } = 0.1; // 0-1

    // Flocculation
    [JsonProperty]
    public bool EnableFlocculation { get; set; } = false;

    [JsonProperty]
    public double FlocculationRate { get; set; } = 1e-6; // 1/s
}

/// <summary>
/// Turbulence modeling properties
/// </summary>
public class TurbulenceProperties
{
    [JsonProperty]
    public TurbulenceModel Model { get; set; } = TurbulenceModel.KEpsilon;

    [JsonProperty]
    public double TurbulentKineticEnergy { get; set; } = 0.01; // m²/s²

    [JsonProperty]
    public double TurbulentDissipationRate { get; set; } = 0.001; // m²/s³

    [JsonProperty]
    public double EddyViscosity { get; set; } = 1e-3; // Pa·s

    [JsonProperty]
    public double SpecificDissipationRate { get; set; } = 1.0; // 1/s (for k-ω)

    // LES parameters
    [JsonProperty]
    public double SmagorinskyConstant { get; set; } = 0.17;

    [JsonProperty]
    public double LESFilterWidth { get; set; } = 0.01; // m

    // Wall functions
    [JsonProperty]
    public bool UseWallFunctions { get; set; } = true;

    [JsonProperty]
    public double WallYPlus { get; set; } = 30.0; // Dimensionless wall distance
}

public enum TurbulenceModel
{
    Laminar,      // No turbulence model
    KEpsilon,     // Standard k-ε model
    KOmegaSST,    // k-ω SST model
    LES,          // Large Eddy Simulation
    DNS           // Direct Numerical Simulation
}

/// <summary>
/// Chemical reaction kinetics properties
/// </summary>
public class ChemicalReactionProperties
{
    [JsonProperty]
    public string ReactionName { get; set; } = "Reaction";

    [JsonProperty]
    public List<ReactionSpecies> Reactants { get; set; } = new();

    [JsonProperty]
    public List<ReactionSpecies> Products { get; set; } = new();

    [JsonProperty]
    public ReactionType Type { get; set; } = ReactionType.Arrhenius;

    // Arrhenius kinetics: k = A * exp(-Ea / RT)
    [JsonProperty]
    public double PreExponentialFactor { get; set; } = 1e10; // 1/s or appropriate units

    [JsonProperty]
    public double ActivationEnergy { get; set; } = 50000.0; // J/mol

    [JsonProperty]
    public double ReactionOrder { get; set; } = 1.0;

    // Equilibrium
    [JsonProperty]
    public bool IsReversible { get; set; } = false;

    [JsonProperty]
    public double EquilibriumConstant { get; set; } = 1.0;

    // Catalysis
    [JsonProperty]
    public bool IsCatalyzed { get; set; } = false;

    [JsonProperty]
    public string CatalystName { get; set; } = "";

    [JsonProperty]
    public double CatalyticEfficiency { get; set; } = 1.0;

    // Surface reactions
    [JsonProperty]
    public bool IsSurfaceReaction { get; set; } = false;

    [JsonProperty]
    public double SurfaceArea { get; set; } = 1.0; // m²/m³
}

public class ReactionSpecies
{
    [JsonProperty]
    public string Name { get; set; }

    [JsonProperty]
    public double StoichiometricCoefficient { get; set; } = 1.0;
}

public enum ReactionType
{
    Arrhenius,           // Temperature-dependent Arrhenius
    ElementaryReaction,  // Elementary reaction
    Enzymatic,           // Michaelis-Menten kinetics
    Autocatalytic,       // Autocatalytic reaction
    ChainReaction        // Chain/radical reactions
}

/// <summary>
/// Phase change phenomena properties
/// </summary>
public class PhaseChangeProperties
{
    [JsonProperty]
    public PhaseChangeType Type { get; set; } = PhaseChangeType.Boiling;

    [JsonProperty]
    public double TransitionTemperature { get; set; } = 373.15; // K (water boiling)

    [JsonProperty]
    public double LatentHeat { get; set; } = 2.26e6; // J/kg (water vaporization)

    [JsonProperty]
    public double TransitionPressure { get; set; } = 101325.0; // Pa

    // Nucleation
    [JsonProperty]
    public bool EnableNucleation { get; set; } = true;

    [JsonProperty]
    public double NucleationSiteDensity { get; set; } = 1e6; // sites/m²

    [JsonProperty]
    public double ContactAngle { get; set; } = 90.0; // degrees

    // Stefan problem (moving boundary)
    [JsonProperty]
    public bool IsMovingBoundary { get; set; } = false;

    [JsonProperty]
    public double InterfaceVelocity { get; set; } = 0.0; // m/s

    // Supercooling/superheating
    [JsonProperty]
    public double Supercooling { get; set; } = 0.0; // K

    [JsonProperty]
    public double Superheating { get; set; } = 0.0; // K

    // Kinetics
    [JsonProperty]
    public double MassTransferCoefficient { get; set; } = 1e-4; // m/s
}

public enum PhaseChangeType
{
    Melting,        // Solid → Liquid
    Freezing,       // Liquid → Solid
    Boiling,        // Liquid → Gas
    Condensation,   // Gas → Liquid
    Sublimation,    // Solid → Gas
    Deposition      // Gas → Solid
}

/// <summary>
/// Electrokinetic effects properties
/// </summary>
public class ElectrokineticProperties
{
    [JsonProperty]
    public (double X, double Y, double Z) ElectricField { get; set; } = (0, 0, 0); // V/m

    [JsonProperty]
    public (double X, double Y, double Z) FieldGradient { get; set; } = (0, 0, 0); // V/m²

    [JsonProperty]
    public double Permittivity { get; set; } = 7.08e-10; // F/m (water)

    [JsonProperty]
    public double ZetaPotential { get; set; } = -0.025; // V

    [JsonProperty]
    public double FluidViscosity { get; set; } = 1e-3; // Pa·s

    // Electroosmosis
    [JsonProperty]
    public bool EnableElectroosmosis { get; set; } = true;

    [JsonProperty]
    public double ElectroosmoticMobility { get; set; } = 5e-8; // m²/(V·s)

    // Electrophoresis
    [JsonProperty]
    public bool EnableElectrophoresis { get; set; } = false;

    [JsonProperty]
    public double ParticleCharge { get; set; } = 1.6e-19; // C (elementary charge)

    [JsonProperty]
    public double ParticleRadius { get; set; } = 1e-6; // m

    // Dielectrophoresis
    [JsonProperty]
    public bool EnableDielectrophoresis { get; set; } = false;

    [JsonProperty]
    public double ClausiusMossottiFactor { get; set; } = 0.5; // -0.5 to 1.0

    // AC electrokinetics
    [JsonProperty]
    public bool IsACField { get; set; } = false;

    [JsonProperty]
    public double Frequency { get; set; } = 1e6; // Hz

    [JsonProperty]
    public double Conductivity { get; set; } = 5.5e-6; // S/m
}

/// <summary>
/// Biological processes properties
/// </summary>
public class BiologicalProperties
{
    [JsonProperty]
    public BiologicalProcessType Type { get; set; } = BiologicalProcessType.BacterialGrowth;

    // Monod kinetics: μ = μ_max * S / (K_s + S)
    [JsonProperty]
    public double MaxGrowthRate { get; set; } = 1e-5; // 1/s

    [JsonProperty]
    public double HalfSaturationConstant { get; set; } = 1e-3; // kg/m³

    [JsonProperty]
    public double YieldCoefficient { get; set; } = 0.5; // kg biomass / kg substrate

    [JsonProperty]
    public double DecayRate { get; set; } = 1e-6; // 1/s

    // Biofilm
    [JsonProperty]
    public bool EnableBiofilm { get; set; } = false;

    [JsonProperty]
    public double BiofilmThickness { get; set; } = 1e-4; // m (100 microns)

    [JsonProperty]
    public double BiofilmDensity { get; set; } = 50.0; // kg/m³

    [JsonProperty]
    public double DetachmentRate { get; set; } = 1e-7; // kg/(m²·s)

    // Substrate and products
    [JsonProperty]
    public string SubstrateName { get; set; } = "Glucose";

    [JsonProperty]
    public List<string> ProductNames { get; set; } = new();

    [JsonProperty]
    public List<string> InhibitorNames { get; set; } = new();

    // Environmental factors
    [JsonProperty]
    public double OptimalTemperature { get; set; } = 310.15; // K (37°C)

    [JsonProperty]
    public double OptimalPH { get; set; } = 7.0;

    [JsonProperty]
    public double TemperatureCoefficient { get; set; } = 1.07; // Q10 = 2 → θ = 1.07
}

public enum BiologicalProcessType
{
    BacterialGrowth,
    AlgalGrowth,
    Biodegradation,
    Fermentation,
    Nitrification,
    Denitrification,
    Methanogenesis
}

/// <summary>
/// Acoustic and vibration force properties
/// </summary>
public class AcousticProperties
{
    [JsonProperty]
    public AcousticWaveType WaveType { get; set; } = AcousticWaveType.PlaneWave;

    [JsonProperty]
    public double Frequency { get; set; } = 1e6; // Hz (1 MHz - ultrasound)

    [JsonProperty]
    public double Amplitude { get; set; } = 1e5; // Pa (pressure amplitude)

    [JsonProperty]
    public double SpeedOfSound { get; set; } = 1500.0; // m/s (in water)

    [JsonProperty]
    public (double X, double Y, double Z) Direction { get; set; } = (1, 0, 0);

    // Particle properties for radiation force
    [JsonProperty]
    public double ParticleRadius { get; set; } = 1e-6; // m

    [JsonProperty]
    public double FluidCompressibility { get; set; } = 4.5e-10; // 1/Pa (water)

    [JsonProperty]
    public double ParticleCompressibility { get; set; } = 2.5e-11; // 1/Pa (polystyrene)

    // Acoustic streaming
    [JsonProperty]
    public bool EnableAcousticStreaming { get; set; } = true;

    [JsonProperty]
    public double AttenuationCoefficient { get; set; } = 0.002; // Np/m

    // Focused beam
    [JsonProperty]
    public (double X, double Y, double Z) FocusPoint { get; set; } = (0, 0, 0);

    [JsonProperty]
    public double BeamRadius { get; set; } = 1e-3; // m

    // Cavitation
    [JsonProperty]
    public bool EnableCavitation { get; set; } = false;

    [JsonProperty]
    public double CavitationThreshold { get; set; } = 1e6; // Pa

    // Standing wave parameters
    [JsonProperty]
    public double NodeSpacing { get; set; } = 0.75e-3; // m (λ/2 at 1 MHz in water)
}

public enum AcousticWaveType
{
    PlaneWave,
    StandingWave,
    Focused,
    Spherical
}
