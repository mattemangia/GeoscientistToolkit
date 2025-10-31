// GeoscientistToolkit/Analysis/Geothermal/GeothermalSimulationOptions.cs

using System.Numerics;
using GeoscientistToolkit.Data.Borehole;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Defines the type of heat exchanger used in the borehole.
/// </summary>
public enum HeatExchangerType
{
    /// <summary>
    /// U-shaped tube heat exchanger with two pipes.
    /// </summary>
    UTube,
    
    /// <summary>
    /// Coaxial heat exchanger with inner and outer pipes.
    /// </summary>
    Coaxial
}

/// <summary>
/// Defines the flow configuration for the heat exchanger.
/// </summary>
public enum FlowConfiguration
{
    /// <summary>
    /// Fluid flows down through one pipe and up through another.
    /// </summary>
    CounterFlow,
    
    /// <summary>
    /// Fluid flows in the same direction (for testing).
    /// </summary>
    ParallelFlow
}

/// <summary>
/// Defines boundary condition types for the simulation domain.
/// </summary>
public enum BoundaryConditionType
{
    /// <summary>
    /// Fixed temperature boundary.
    /// </summary>
    Dirichlet,
    
    /// <summary>
    /// Fixed heat flux boundary.
    /// </summary>
    Neumann,
    
    /// <summary>
    /// Convective heat transfer boundary.
    /// </summary>
    Robin,
    
    /// <summary>
    /// No heat flux across boundary.
    /// </summary>
    Adiabatic
}

/// <summary>
/// Holds all configuration options for a geothermal borehole simulation.
/// </summary>
public class GeothermalSimulationOptions
{
    /// <summary>
    /// The borehole dataset to simulate.
    /// </summary>
    public BoreholeDataset BoreholeDataset { get; set; }
    
    /// <summary>
    /// Type of heat exchanger installed in the borehole.
    /// </summary>
    public HeatExchangerType HeatExchangerType { get; set; } = HeatExchangerType.UTube;
    
    /// <summary>
    /// Flow configuration of the heat exchanger.
    /// </summary>
    public FlowConfiguration FlowConfiguration { get; set; } = FlowConfiguration.CounterFlow;
    
    // Heat Exchanger Parameters
    
    /// <summary>
    /// Inner diameter of the heat exchanger pipes (m).
    /// </summary>
    public double PipeInnerDiameter { get; set; } = 0.032; // 32 mm
    
    /// <summary>
    /// Outer diameter of the heat exchanger pipes (m).
    /// </summary>
    public double PipeOuterDiameter { get; set; } = 0.040; // 40 mm
    
    /// <summary>
    /// For U-tube: spacing between pipe centers (m).
    /// For coaxial: outer pipe inner diameter (m).
    /// </summary>
    public double PipeSpacing { get; set; } = 0.080; // 80 mm
    
    /// <summary>
    /// Thermal conductivity of pipe material (W/m·K).
    /// </summary>
    public double PipeThermalConductivity { get; set; } = 0.4; // HDPE
    
    /// <summary>
    /// Thermal conductivity of grout/backfill material (W/m·K).
    /// </summary>
    public double GroutThermalConductivity { get; set; } = 2.0;
    
    // Fluid Properties
    
    /// <summary>
    /// Mass flow rate of heat carrier fluid (kg/s).
    /// </summary>
    public double FluidMassFlowRate { get; set; } = 0.5;
    
    /// <summary>
    /// Inlet temperature of heat carrier fluid (K).
    /// </summary>
    public double FluidInletTemperature { get; set; } = 283.15; // 10°C
    
    /// <summary>
    /// Specific heat capacity of fluid (J/kg·K).
    /// </summary>
    public double FluidSpecificHeat { get; set; } = 4186; // Water
    
    /// <summary>
    /// Density of fluid (kg/m³).
    /// </summary>
    public double FluidDensity { get; set; } = 1000; // Water
    
    /// <summary>
    /// Dynamic viscosity of fluid (Pa·s).
    /// </summary>
    public double FluidViscosity { get; set; } = 0.001; // Water at 20°C
    
    /// <summary>
    /// Thermal conductivity of fluid (W/m·K).
    /// </summary>
    public double FluidThermalConductivity { get; set; } = 0.6; // Water
    
    // HVAC & Performance Parameters
    
    /// <summary>
    /// The target supply temperature for the HVAC system (e.g., heated or chilled water loop) in Kelvin.
    /// Used for calculating the Coefficient of Performance (COP).
    /// If null, a default of 308.15 K (35°C) is used for heating calculations.
    /// </summary>
    public double? HvacSupplyTemperatureKelvin { get; set; } = null;

    /// <summary>
    /// The isentropic efficiency of the heat pump's compressor (0 to 1).
    /// Used for calculating a realistic Coefficient of Performance (COP).
    /// If null, a default efficiency of 0.6 (60%) is used.
    /// </summary>
    public double? CompressorIsentropicEfficiency { get; set; } = null;
    
    // Ground Properties
    
    /// <summary>
    /// Defines the initial ground temperature profile.
    /// If empty, the profile will be generated from SurfaceTemperature and AverageGeothermalGradient.
    /// List of (Depth in m, Temperature in K).
    /// </summary>
    public List<(double Depth, double Temperature)> InitialTemperatureProfile { get; set; } = new();

    /// <summary>
    /// Average surface temperature of the ground (K). Used if InitialTemperatureProfile is not set.
    /// </summary>
    public double SurfaceTemperature { get; set; } = 283.15; // 10°C

    /// <summary>
    /// Average geothermal gradient (K/m). Used if InitialTemperatureProfile is not set.
    /// </summary>
    public double AverageGeothermalGradient { get; set; } = 0.03; // 30 K/km

    /// <summary>
    /// Dictionary mapping geological layer names to their thermal conductivities (W/m·K).
    /// </summary>
    public Dictionary<string, double> LayerThermalConductivities { get; set; } = new();
    
    /// <summary>
    /// Dictionary mapping geological layer names to their specific heat capacities (J/kg·K).
    /// </summary>
    public Dictionary<string, double> LayerSpecificHeats { get; set; } = new();
    
    /// <summary>
    /// Dictionary mapping geological layer names to their densities (kg/m³).
    /// </summary>
    public Dictionary<string, double> LayerDensities { get; set; } = new();
    
    /// <summary>
    /// Dictionary mapping geological layer names to their porosities (0-1).
    /// </summary>
    public Dictionary<string, double> LayerPorosities { get; set; } = new();
    
    /// <summary>
    /// Dictionary mapping geological layer names to their permeabilities (m²).
    /// </summary>
    public Dictionary<string, double> LayerPermeabilities { get; set; } = new();
    
    // Fracture Properties
    
    /// <summary>
    /// Enable fracture flow simulation.
    /// </summary>
    public bool SimulateFractures { get; set; } = false;
    
    /// <summary>
    /// Fracture aperture (m).
    /// </summary>
    public double FractureAperture { get; set; } = 0.001; // 1 mm
    
    /// <summary>
    /// Fracture permeability (m²).
    /// </summary>
    public double FracturePermeability { get; set; } = 1e-8;
    
    // Groundwater Flow
    
    /// <summary>
    /// Enable coupled groundwater flow simulation.
    /// </summary>
    public bool SimulateGroundwaterFlow { get; set; } = true;
    
    /// <summary>
    /// Regional groundwater flow velocity (m/s).
    /// </summary>
    public Vector3 GroundwaterVelocity { get; set; } = new Vector3(0, 0, 0);
    
    /// <summary>
    /// Groundwater temperature (K).
    /// </summary>
    public double GroundwaterTemperature { get; set; } = 283.15; // 10°C
    
    /// <summary>
    /// Hydraulic head at top boundary (m).
    /// </summary>
    public double HydraulicHeadTop { get; set; } = 0;
    
    /// <summary>
    /// Hydraulic head at bottom boundary (m).
    /// </summary>
    public double HydraulicHeadBottom { get; set; } = -10;
    
    /// <summary>
    /// Longitudinal dispersivity length of the porous medium (m).
    /// Represents dispersion in the direction of flow.
    /// </summary>
    public double LongitudinalDispersivity { get; set; } = 0.5;

    /// <summary>
    /// Transverse dispersivity length of the porous medium (m).
    /// Represents dispersion perpendicular to the direction of flow.
    /// Typically a fraction (e.g., 1/10th) of the longitudinal dispersivity.
    /// </summary>
    public double TransverseDispersivity { get; set; } = 0.05;
    
    // Simulation Domain
    
    /// <summary>
    /// Radius of cylindrical simulation domain around borehole (m).
    /// </summary>
    public double DomainRadius { get; set; } = 50;
    
    /// <summary>
    /// Extend domain above/below borehole ends (m).
    /// </summary>
    public double DomainExtension { get; set; } = 20;
    
    /// <summary>
    /// Grid resolution in radial direction.
    /// </summary>
    public int RadialGridPoints { get; set; } = 50;
    
    /// <summary>
    /// Grid resolution in angular direction.
    /// </summary>
    public int AngularGridPoints { get; set; } = 36;
    
    /// <summary>
    /// Grid resolution in vertical direction.
    /// </summary>
    public int VerticalGridPoints { get; set; } = 100;
    
    // Boundary Conditions
    
    /// <summary>
    /// Boundary condition at outer radius.
    /// </summary>
    public BoundaryConditionType OuterBoundaryCondition { get; set; } = BoundaryConditionType.Dirichlet;
    
    /// <summary>
    /// Temperature at outer boundary (K) if Dirichlet.
    /// </summary>
    public double OuterBoundaryTemperature { get; set; } = 283.15;
    
    /// <summary>
    /// Heat flux at outer boundary (W/m²) if Neumann.
    /// </summary>
    public double OuterBoundaryHeatFlux { get; set; } = 0;
    
    /// <summary>
    /// Boundary condition at top.
    /// </summary>
    public BoundaryConditionType TopBoundaryCondition { get; set; } = BoundaryConditionType.Adiabatic;
    
    /// <summary>
    /// Temperature at top boundary (K) if Dirichlet.
    /// </summary>
    public double TopBoundaryTemperature { get; set; } = 283.15;
    
    /// <summary>
    /// Boundary condition at bottom.
    /// </summary>
    public BoundaryConditionType BottomBoundaryCondition { get; set; } = BoundaryConditionType.Neumann;
    
    /// <summary>
    /// Geothermal heat flux at bottom (W/m²) if Neumann.
    /// </summary>
    public double GeothermalHeatFlux { get; set; } = 0.065; // 65 mW/m²
    
    // Solver Options
    
    /// <summary>
    /// Simulation time duration (s).
    /// </summary>
    public double SimulationTime { get; set; } = 31536000; // 1 year
    
    /// <summary>
    /// Time step size (s).
    /// </summary>
    public double TimeStep { get; set; } = 3600; // 1 hour
    
    /// <summary>
    /// Save results every N time steps.
    /// </summary>
    public int SaveInterval { get; set; } = 24; // Daily
    
    /// <summary>
    /// Convergence tolerance for iterative solver.
    /// </summary>
    public double ConvergenceTolerance { get; set; } = 1e-6;
    
    /// <summary>
    /// Maximum iterations per time step.
    /// </summary>
    public int MaxIterationsPerStep { get; set; } = 1000;
    
    /// <summary>
    /// Enable SIMD optimizations.
    /// </summary>
    public bool UseSIMD { get; set; } = true;
    
    /// <summary>
    /// Enable GPU acceleration (placeholder for future).
    /// </summary>
    public bool UseGPU { get; set; } = false;
    
    // Visualization Options
    
    /// <summary>
    /// Generate 3D temperature isosurfaces.
    /// </summary>
    public bool Generate3DIsosurfaces { get; set; } = true;
    
    /// <summary>
    /// Temperature values for isosurfaces (K).
    /// </summary>
    public List<double> IsosurfaceTemperatures { get; set; } = new() { 285, 290, 295, 300, 305 };
    
    /// <summary>
    /// Generate streamlines for flow visualization.
    /// </summary>
    public bool GenerateStreamlines { get; set; } = true;
    
    /// <summary>
    /// Number of streamlines to generate.
    /// </summary>
    public int StreamlineCount { get; set; } = 50;
    
    /// <summary>
    /// Generate 2D slices if 3D not available.
    /// </summary>
    public bool Generate2DSlices { get; set; } = true;
    
    /// <summary>
    /// Slice positions along borehole (0-1).
    /// </summary>
    public List<double> SlicePositions { get; set; } = new() { 0.1, 0.5, 0.9 };
    
    /// <summary>
    /// Initialize with default values for a standard geothermal application.
    /// </summary>
    public void SetDefaultValues()
    {
        // Set typical layer properties if not specified
        if (!LayerThermalConductivities.Any())
        {
            LayerThermalConductivities = new Dictionary<string, double>
            {
                { "Soil", 1.5 },
                { "Clay", 1.2 },
                { "Sand", 2.0 },
                { "Gravel", 2.5 },
                { "Sandstone", 2.8 },
                { "Limestone", 2.9 },
                { "Granite", 3.0 },
                { "Basalt", 1.7 }
            };
        }
        
        if (!LayerSpecificHeats.Any())
        {
            LayerSpecificHeats = new Dictionary<string, double>
            {
                { "Soil", 1840 },
                { "Clay", 1380 },
                { "Sand", 830 },
                { "Gravel", 840 },
                { "Sandstone", 920 },
                { "Limestone", 810 },
                { "Granite", 790 },
                { "Basalt", 840 }
            };
        }
        
        if (!LayerDensities.Any())
        {
            LayerDensities = new Dictionary<string, double>
            {
                { "Soil", 1800 },
                { "Clay", 1900 },
                { "Sand", 2650 },
                { "Gravel", 2700 },
                { "Sandstone", 2500 },
                { "Limestone", 2700 },
                { "Granite", 2750 },
                { "Basalt", 2900 }
            };
        }
        
        if (!LayerPorosities.Any())
        {
            LayerPorosities = new Dictionary<string, double>
            {
                { "Soil", 0.4 },
                { "Clay", 0.45 },
                { "Sand", 0.35 },
                { "Gravel", 0.25 },
                { "Sandstone", 0.15 },
                { "Limestone", 0.1 },
                { "Granite", 0.01 },
                { "Basalt", 0.05 }
            };
        }
        
        if (!LayerPermeabilities.Any())
        {
            LayerPermeabilities = new Dictionary<string, double>
            {
                { "Soil", 1e-12 },
                { "Clay", 1e-15 },
                { "Sand", 1e-11 },
                { "Gravel", 1e-9 },
                { "Sandstone", 1e-13 },
                { "Limestone", 1e-14 },
                { "Granite", 1e-16 },
                { "Basalt", 1e-15 }
            };
        }
    }
}