// GeoscientistToolkit/Analysis/Geothermal/GeothermalSimulationOptions.cs

using System.Numerics;
using GeoscientistToolkit.Data.Borehole;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     Defines the type of heat exchanger used in the borehole.
/// </summary>
public enum HeatExchangerType
{
    UTube,
    Coaxial
}

public enum FlowConfiguration
{
    CounterFlow,
    CounterFlowReversed,
    ParallelFlow
}

public enum BoundaryConditionType
{
    Dirichlet,
    Neumann,
    Robin,
    Adiabatic
}

/// <summary>
///     Holds all configuration options for a geothermal borehole simulation.
/// </summary>
public class GeothermalSimulationOptions
{
    public BoreholeDataset BoreholeDataset { get; set; }
    public HeatExchangerType HeatExchangerType { get; set; } = HeatExchangerType.UTube;
    public FlowConfiguration FlowConfiguration { get; set; } = FlowConfiguration.CounterFlow;
    public double PipeInnerDiameter { get; set; } = 0.032;
    public double PipeOuterDiameter { get; set; } = 0.040;
    public double PipeSpacing { get; set; } = 0.080;
    public double PipeThermalConductivity { get; set; } = 0.4;
    public double InnerPipeThermalConductivity { get; set; } = 45.0;
    public double GroutThermalConductivity { get; set; } = 2.0;
    public double FluidMassFlowRate { get; set; } = 0.5;
    public double FluidInletTemperature { get; set; } = 278.15;
    public double FluidSpecificHeat { get; set; } = 4186;
    public double FluidDensity { get; set; } = 1000;
    public double FluidViscosity { get; set; } = 0.001;
    public double FluidThermalConductivity { get; set; } = 0.6;
    public double? HvacSupplyTemperatureKelvin { get; set; } = null;
    public double? CompressorIsentropicEfficiency { get; set; } = null;
    public List<(double Depth, double Temperature)> InitialTemperatureProfile { get; set; } = new();
    public double SurfaceTemperature { get; set; } = 283.15;
    public double AverageGeothermalGradient { get; set; } = 0.035;
    public Dictionary<string, double> LayerThermalConductivities { get; set; } = new();
    public Dictionary<string, double> LayerSpecificHeats { get; set; } = new();
    public Dictionary<string, double> LayerDensities { get; set; } = new();
    public Dictionary<string, double> LayerPorosities { get; set; } = new();
    public Dictionary<string, double> LayerPermeabilities { get; set; } = new();
    public bool SimulateFractures { get; set; }
    public double FractureAperture { get; set; } = 0.001;
    public double FracturePermeability { get; set; } = 1e-8;
    public bool SimulateGroundwaterFlow { get; set; } = true;
    public Vector3 GroundwaterVelocity { get; set; } = new(0, 0, 0);
    public double GroundwaterTemperature { get; set; } = 283.15;
    public double HydraulicHeadTop { get; set; } = 0;
    public double HydraulicHeadBottom { get; set; } = -10;
    public double LongitudinalDispersivity { get; set; } = 0.5;
    public double TransverseDispersivity { get; set; } = 0.05;
    public double DomainRadius { get; set; } = 50;
    public double DomainExtension { get; set; } = 20;
    public int RadialGridPoints { get; set; } = 50;
    public int AngularGridPoints { get; set; } = 36;
    public int VerticalGridPoints { get; set; } = 100;
    public BoundaryConditionType OuterBoundaryCondition { get; set; } = BoundaryConditionType.Dirichlet;
    public double OuterBoundaryTemperature { get; set; } = 283.15;
    public double OuterBoundaryHeatFlux { get; set; } = 0;
    public BoundaryConditionType TopBoundaryCondition { get; set; } = BoundaryConditionType.Adiabatic;
    public double TopBoundaryTemperature { get; set; } = 283.15;
    public BoundaryConditionType BottomBoundaryCondition { get; set; } = BoundaryConditionType.Neumann;
    public double GeothermalHeatFlux { get; set; } = 0.065;
    public double SimulationTime { get; set; } = 31536000;
    public double TimeStep { get; set; } = 3600 * 6; // Set a reasonable MAX cap (e.g., 6 hours)
    public int SaveInterval { get; set; } = 1;
    public double ConvergenceTolerance { get; set; } = 5e-3;

    /// <summary>
    ///     DEFINITIVE FIX: Maximum iterations per time step. 30 was far too low for a complex
    ///     problem, causing the solver to "fail" prematurely and forcing the time step to shrink.
    ///     200 is a much more realistic value, giving the solver a chance to converge properly.
    /// </summary>
    public int MaxIterationsPerStep { get; set; } = 200;

    public bool UseSIMD { get; set; } = true;
    public bool UseGPU { get; set; } = false;
    public bool Generate3DIsosurfaces { get; set; } = true;
    public List<double> IsosurfaceTemperatures { get; set; } = new() { 285, 290, 295, 300, 305 };
    public bool GenerateStreamlines { get; set; } = true;
    public int StreamlineCount { get; set; } = 50;
    public bool Generate2DSlices { get; set; } = true;
    public List<double> SlicePositions { get; set; } = new() { 0.1, 0.5, 0.9 };

    public void SetDefaultValues()
    {
        if (!LayerThermalConductivities.Any()) LayerThermalConductivities = new Dictionary<string, double> { { "Soil", 1.5 }, { "Clay", 1.2 }, { "Sand", 2.0 }, { "Gravel", 2.5 }, { "Sandstone", 2.8 }, { "Limestone", 2.9 }, { "Granite", 3.0 }, { "Basalt", 1.7 } };
        if (!LayerSpecificHeats.Any()) LayerSpecificHeats = new Dictionary<string, double> { { "Soil", 1840 }, { "Clay", 1380 }, { "Sand", 830 }, { "Gravel", 840 }, { "Sandstone", 920 }, { "Limestone", 810 }, { "Granite", 790 }, { "Basalt", 840 } };
        if (!LayerDensities.Any()) LayerDensities = new Dictionary<string, double> { { "Soil", 1800 }, { "Clay", 1900 }, { "Sand", 2650 }, { "Gravel", 2700 }, { "Sandstone", 2500 }, { "Limestone", 2700 }, { "Granite", 2750 }, { "Basalt", 2900 } };
        if (!LayerPorosities.Any()) LayerPorosities = new Dictionary<string, double> { { "Soil", 0.4 }, { "Clay", 0.45 }, { "Sand", 0.35 }, { "Gravel", 0.25 }, { "Sandstone", 0.15 }, { "Limestone", 0.1 }, { "Granite", 0.01 }, { "Basalt", 0.05 } };
        if (!LayerPermeabilities.Any()) LayerPermeabilities = new Dictionary<string, double> { { "Soil", 1e-12 }, { "Clay", 1e-15 }, { "Sand", 1e-11 }, { "Gravel", 1e-9 }, { "Sandstone", 1e-13 }, { "Limestone", 1e-14 }, { "Granite", 1e-16 }, { "Basalt", 1e-15 } };
    }

    public void ApplyPreset(GeothermalSimulationPreset preset)
    {
        switch (preset)
        {
            case GeothermalSimulationPreset.ShallowGSHP: ApplyShallowGSHPPreset(); break;
            case GeothermalSimulationPreset.MediumDepthHeating: ApplyMediumDepthHeatingPreset(); break;
            case GeothermalSimulationPreset.DeepGeothermalProduction: ApplyDeepGeothermalProductionPreset(); break;
            case GeothermalSimulationPreset.EnhancedGeothermalSystem: ApplyEnhancedGeothermalSystemPreset(); break;
            case GeothermalSimulationPreset.AquiferThermalStorage: ApplyAquiferThermalStoragePreset(); break;
            case GeothermalSimulationPreset.ExplorationTest: ApplyExplorationTestPreset(); break;
            case GeothermalSimulationPreset.Custom: break;
        }
    }

    private void ApplyShallowGSHPPreset()
    {
        HeatExchangerType = HeatExchangerType.UTube;
        FlowConfiguration = FlowConfiguration.CounterFlow;
        PipeInnerDiameter = 0.032; PipeOuterDiameter = 0.040; PipeSpacing = 0.080;
        PipeThermalConductivity = 0.4; InnerPipeThermalConductivity = 0.4; GroutThermalConductivity = 2.0;
        FluidMassFlowRate = 0.5; FluidInletTemperature = 283.15;
        SurfaceTemperature = 283.15; AverageGeothermalGradient = 0.025; GeothermalHeatFlux = 0.060;
        DomainRadius = 30; DomainExtension = 10;
        RadialGridPoints = 40; AngularGridPoints = 24; VerticalGridPoints = 80;
        SimulateGroundwaterFlow = true; GroundwaterVelocity = new Vector3(1e-7f, 0, 0);
        SimulationTime = 86400 * 30; TimeStep = 3600 * 1; SaveInterval = 24;
    }

    private void ApplyMediumDepthHeatingPreset()
    {
        HeatExchangerType = HeatExchangerType.UTube;
        FlowConfiguration = FlowConfiguration.CounterFlow;
        PipeInnerDiameter = 0.065; PipeOuterDiameter = 0.075; PipeSpacing = 0.150;
        PipeThermalConductivity = 0.4; InnerPipeThermalConductivity = 0.4; GroutThermalConductivity = 2.2;
        FluidMassFlowRate = 3.0; FluidInletTemperature = 288.15;
        SurfaceTemperature = 285.15; AverageGeothermalGradient = 0.030; GeothermalHeatFlux = 0.065;
        DomainRadius = 75; DomainExtension = 20;
        RadialGridPoints = 50; AngularGridPoints = 32; VerticalGridPoints = 120;
        SimulateGroundwaterFlow = true; GroundwaterVelocity = new Vector3(5e-8f, 0, 0);
        SimulationTime = 86400 * 180; TimeStep = 3600 * 2; SaveInterval = 12;
    }

    private void ApplyDeepGeothermalProductionPreset()
    {
        HeatExchangerType = HeatExchangerType.Coaxial;
        FlowConfiguration = FlowConfiguration.CounterFlowReversed;
        PipeInnerDiameter = 0.125; PipeOuterDiameter = 0.220; PipeSpacing = 0.200;
        PipeThermalConductivity = 45.0; InnerPipeThermalConductivity = 0.01; GroutThermalConductivity = 2.5;
        FluidMassFlowRate = 15.0; FluidInletTemperature = 293.15;
        FluidViscosity = 0.0005; FluidThermalConductivity = 0.65;
        SurfaceTemperature = 288.15; AverageGeothermalGradient = 0.035; GeothermalHeatFlux = 0.075;
        DomainRadius = 150; DomainExtension = 50;
        RadialGridPoints = 60; AngularGridPoints = 36; VerticalGridPoints = 150;
        SimulateGroundwaterFlow = true; GroundwaterVelocity = new Vector3(1e-8f, 0, 0);
        SimulationTime = 86400 * 365 * 5; TimeStep = 3600 * 6; SaveInterval = 4;
    }

    private void ApplyEnhancedGeothermalSystemPreset()
    {
        HeatExchangerType = HeatExchangerType.Coaxial;
        FlowConfiguration = FlowConfiguration.CounterFlowReversed;
        PipeInnerDiameter = 0.150; PipeOuterDiameter = 0.250; PipeSpacing = 0.230;
        PipeThermalConductivity = 45.0; InnerPipeThermalConductivity = 0.01; GroutThermalConductivity = 2.8;
        FluidMassFlowRate = 30.0; FluidInletTemperature = 313.15;
        FluidDensity = 950; FluidViscosity = 0.0003; FluidThermalConductivity = 0.68;
        SurfaceTemperature = 288.15; AverageGeothermalGradient = 0.040; GeothermalHeatFlux = 0.085;
        SimulateFractures = true; FractureAperture = 0.005; FracturePermeability = 1e-10;
        DomainRadius = 200; DomainExtension = 100;
        RadialGridPoints = 70; AngularGridPoints = 36; VerticalGridPoints = 180;
        SimulateGroundwaterFlow = true; GroundwaterVelocity = new Vector3(5e-7f, 0, 0);
        LongitudinalDispersivity = 10.0; TransverseDispersivity = 1.0;
        SimulationTime = 86400 * 365 * 10; TimeStep = 3600 * 12; SaveInterval = 2;
    }

    private void ApplyAquiferThermalStoragePreset()
    {
        HeatExchangerType = HeatExchangerType.UTube;
        FlowConfiguration = FlowConfiguration.CounterFlow;
        PipeInnerDiameter = 0.080; PipeOuterDiameter = 0.090; PipeSpacing = 0.180;
        PipeThermalConductivity = 0.4; InnerPipeThermalConductivity = 0.4; GroutThermalConductivity = 2.5;
        FluidMassFlowRate = 5.0; FluidInletTemperature = 303.15;
        SurfaceTemperature = 285.15; AverageGeothermalGradient = 0.025; GeothermalHeatFlux = 0.060;
        DomainRadius = 100; DomainExtension = 30;
        RadialGridPoints = 55; AngularGridPoints = 36; VerticalGridPoints = 100;
        SimulateGroundwaterFlow = true; GroundwaterVelocity = new Vector3(5e-6f, 0, 0);
        LongitudinalDispersivity = 5.0; TransverseDispersivity = 0.5;
        SimulationTime = 86400 * 180; TimeStep = 3600 * 3; SaveInterval = 8;
    }

    private void ApplyExplorationTestPreset()
    {
        HeatExchangerType = HeatExchangerType.UTube;
        FlowConfiguration = FlowConfiguration.CounterFlow;
        PipeInnerDiameter = 0.050; PipeOuterDiameter = 0.063; PipeSpacing = 0.125;
        PipeThermalConductivity = 0.4; InnerPipeThermalConductivity = 0.4; GroutThermalConductivity = 2.0;
        FluidMassFlowRate = 1.5; FluidInletTemperature = 288.15;
        SurfaceTemperature = 285.15; AverageGeothermalGradient = 0.030; GeothermalHeatFlux = 0.065;
        DomainRadius = 50; DomainExtension = 20;
        RadialGridPoints = 35; AngularGridPoints = 24; VerticalGridPoints = 60;
        SimulateGroundwaterFlow = true; GroundwaterVelocity = new Vector3(1e-7f, 0, 0);
        SimulationTime = 86400 * 7; TimeStep = 30 * 60; SaveInterval = 48;
        ConvergenceTolerance = 2e-3; MaxIterationsPerStep = 100;
    }

    public static string GetPresetDescription(GeothermalSimulationPreset preset)
    {
        return preset switch { GeothermalSimulationPreset.Custom => "Custom user-defined parameters", GeothermalSimulationPreset.ShallowGSHP => "Shallow GSHP (50-200m): Low-flow U-Tube for residential heating/cooling.", GeothermalSimulationPreset.MediumDepthHeating => "Medium Depth (500-1500m): Medium-flow U-Tube for district heating.", GeothermalSimulationPreset.DeepGeothermalProduction => "Deep Production (2-5km): High-flow Coaxial (VIT) for utility-scale heat/power.", GeothermalSimulationPreset.EnhancedGeothermalSystem => "EGS (3-6km): Very high-flow Coaxial (VIT) with fracture flow for high-temp power.", GeothermalSimulationPreset.AquiferThermalStorage => "ATES (50-300m): High-flow U-Tube for seasonal energy storage in aquifers.", GeothermalSimulationPreset.ExplorationTest => "Quick Test (any depth): Coarse grid, 7-day run for rapid feasibility assessment.", _ => "Unknown preset" };
    }
}
public enum GeothermalSimulationPreset
{
    Custom,
    ShallowGSHP,
    MediumDepthHeating,
    DeepGeothermalProduction,
    EnhancedGeothermalSystem,
    AquiferThermalStorage,
    ExplorationTest
}