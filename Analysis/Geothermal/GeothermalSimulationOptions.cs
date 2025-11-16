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
    public float HeatExchangerDepth { get; set; }

    public double HeatExchangerEndFeatherMeters { get; set; } = 10.0;

    // ===== BTES (Borehole Thermal Energy Storage) Parameters =====

    /// <summary>
    ///     Enable BTES mode: applies seasonal energy curve to fluid inlet temperature
    /// </summary>
    public bool EnableBTESMode { get; set; } = false;

    /// <summary>
    ///     Seasonal energy curve for BTES (365 values, one per day).
    ///     Positive values = heat injection (charging), Negative values = heat extraction (discharging).
    ///     Units: kWh/day
    /// </summary>
    public List<double> SeasonalEnergyCurve { get; set; } = new();

    /// <summary>
    ///     Annual total energy to store in BTES mode (MWh/year)
    /// </summary>
    public double BTESAnnualEnergyStorage { get; set; } = 1000.0;

    /// <summary>
    ///     Peak to average ratio for seasonal curve generation
    /// </summary>
    public double BTESSeasonalPeakRatio { get; set; } = 2.5;

    /// <summary>
    ///     Save all time frames (for animation), not just every SaveInterval
    /// </summary>
    public bool SaveAllTimeFrames { get; set; } = false;

    /// <summary>
    ///     Base temperature for BTES heat injection (K) - temperature when charging
    /// </summary>
    public double BTESChargingTemperature { get; set; } = 313.15; // 40°C

    /// <summary>
    ///     Base temperature for BTES heat extraction (K) - temperature when discharging
    /// </summary>
    public double BTESDischargingTemperature { get; set; } = 278.15; // 5°C

    /// <summary>
    ///     Apply random variations to the seasonal curve to simulate realistic weather fluctuations
    /// </summary>
    public bool BTESApplyRandomVariations { get; set; } = false;

    /// <summary>
    ///     Magnitude of random variations (0-1, fraction of daily energy)
    /// </summary>
    public double BTESRandomVariationMagnitude { get; set; } = 0.15; // 15% variation

    /// <summary>
    ///     Random seed for reproducible variations (0 = random seed)
    /// </summary>
    public int BTESRandomSeed { get; set; } = 0;

    // ===== Thermodynamics and Geochemistry Parameters =====

    /// <summary>
    ///     Enable thermodynamic simulation for fluid-rock interactions
    /// </summary>
    public bool EnableThermodynamics { get; set; } = false;

    /// <summary>
    ///     Fluid composition for thermodynamic calculations (ions, compounds like CO2, minerals)
    /// </summary>
    public List<FluidCompositionEntry> FluidComposition { get; set; } = new();

    /// <summary>
    ///     Generate pore network model from borehole lithology for precipitation calculations
    /// </summary>
    public bool GeneratePoreNetworkModel { get; set; } = true;

    /// <summary>
    ///     PNM generation mode: Conservative (1 erosion) or Aggressive (3 erosions)
    /// </summary>
    public PoreNetworkGenerationMode PnmGenerationMode { get; set; } = PoreNetworkGenerationMode.Conservative;

    /// <summary>
    ///     Number of erosion passes for PNM generation (1-3)
    /// </summary>
    public int PnmErosionPasses { get; set; } = 1;

    /// <summary>
    ///     Enable precipitation/dissolution visualization in 2D slices
    /// </summary>
    public bool EnablePrecipitationVisualization { get; set; } = true;

    /// <summary>
    ///     Time step for thermodynamic calculations (seconds)
    /// </summary>
    public double ThermodynamicTimeStep { get; set; } = 3600.0; // 1 hour

    /// <summary>
    ///     Minimum precipitation threshold for visualization (mol/m³)
    /// </summary>
    public double PrecipitationVisualizationThreshold { get; set; } = 1e-6;

    // ===== Multiphase Flow Parameters =====

    /// <summary>
    ///     Enable multiphase flow simulation (water-steam-CO2)
    /// </summary>
    public bool EnableMultiphaseFlow { get; set; } = false;

    /// <summary>
    ///     Multiphase fluid type (WaterOnly, WaterSteam, WaterCO2, WaterSteamCO2)
    /// </summary>
    public MultiphaseFluidType MultiphaseFluidType { get; set; } = MultiphaseFluidType.WaterCO2;

    /// <summary>
    ///     Initial salinity (mass fraction of NaCl)
    /// </summary>
    public float InitialSalinity { get; set; } = 0.035f; // 3.5% (seawater)

    /// <summary>
    ///     Residual water saturation for relative permeability
    /// </summary>
    public float ResidualWaterSaturation { get; set; } = 0.2f;

    /// <summary>
    ///     Residual gas saturation for relative permeability
    /// </summary>
    public float ResidualGasSaturation { get; set; } = 0.05f;

    // ===== Adaptive Mesh Refinement Parameters =====

    /// <summary>
    ///     Enable adaptive mesh refinement
    /// </summary>
    public bool EnableAdaptiveMeshRefinement { get; set; } = false;

    /// <summary>
    ///     Maximum refinement level (0 = base mesh, 1-3 = refined)
    /// </summary>
    public int MaxRefinementLevel { get; set; } = 2;

    /// <summary>
    ///     Temperature gradient threshold for refinement (K/m)
    /// </summary>
    public float TemperatureGradientThreshold { get; set; } = 5.0f;

    /// <summary>
    ///     Pressure gradient threshold for refinement (Pa/m)
    /// </summary>
    public float PressureGradientThreshold { get; set; } = 1e5f;

    /// <summary>
    ///     Refinement interval (refine every N timesteps)
    /// </summary>
    public int RefinementInterval { get; set; } = 10;

    // ===== Time-Varying Boundary Conditions Parameters =====

    /// <summary>
    ///     Enable time-varying boundary conditions
    /// </summary>
    public bool EnableTimeVaryingBC { get; set; } = false;

    /// <summary>
    ///     Use seasonal variation in temperature and load
    /// </summary>
    public bool UseSeasonalVariation { get; set; } = true;

    /// <summary>
    ///     Use daily load variation
    /// </summary>
    public bool UseDailyVariation { get; set; } = true;

    /// <summary>
    ///     Average outdoor temperature for seasonal variation (°C)
    /// </summary>
    public float AverageOutdoorTemp { get; set; } = 10.0f;

    /// <summary>
    ///     Seasonal temperature amplitude (°C)
    /// </summary>
    public float SeasonalTempAmplitude { get; set; } = 15.0f;

    /// <summary>
    ///     Peak heating load (W)
    /// </summary>
    public float PeakHeatingLoad { get; set; } = 50000.0f;

    /// <summary>
    ///     Peak cooling load (W)
    /// </summary>
    public float PeakCoolingLoad { get; set; } = 30000.0f;

    // ===== Enhanced HVAC Parameters =====

    /// <summary>
    ///     Enable enhanced HVAC calculations
    /// </summary>
    public bool EnableEnhancedHVAC { get; set; } = false;

    /// <summary>
    ///     Carnot efficiency (fraction of ideal Carnot COP)
    /// </summary>
    public float CarnotEfficiency { get; set; } = 0.45f;

    /// <summary>
    ///     Building UA value (heat loss coefficient, W/K)
    /// </summary>
    public float BuildingUAValue { get; set; } = 300.0f;

    /// <summary>
    ///     Design indoor temperature (°C)
    /// </summary>
    public float DesignIndoorTemp { get; set; } = 20.0f;

    /// <summary>
    ///     Design supply temperature (°C)
    /// </summary>
    public float DesignSupplyTemp { get; set; } = 35.0f;

    // ===== Fractured Media Parameters (Enhanced) =====

    /// <summary>
    ///     Enable dual-continuum fractured media model
    /// </summary>
    public bool EnableDualContinuumFractures { get; set; } = false;

    /// <summary>
    ///     Fracture spacing (m)
    /// </summary>
    public float FractureSpacing { get; set; } = 1.0f;

    /// <summary>
    ///     Fracture density (fractures/m)
    /// </summary>
    public float FractureDensity { get; set; } = 3.0f;

    /// <summary>
    ///     Matrix permeability (m²)
    /// </summary>
    public float MatrixPermeability { get; set; } = 1e-18f;

    // ===== ORC (Organic Rankine Cycle) Power Generation Parameters =====

    /// <summary>
    ///     Enable ORC power generation simulation
    /// </summary>
    public bool EnableORCSimulation { get; set; } = false;

    /// <summary>
    ///     Use GPU (OpenCL) acceleration for ORC calculations
    /// </summary>
    public bool UseORCGPU { get; set; } = false;

    /// <summary>
    ///     ORC evaporator pressure (Pa)
    /// </summary>
    public float ORCEvaporatorPressure { get; set; } = 1.5e6f; // 15 bar

    /// <summary>
    ///     ORC condenser temperature (K)
    /// </summary>
    public float ORCCondenserTemperature { get; set; } = 303.15f; // 30°C

    /// <summary>
    ///     ORC turbine isentropic efficiency (0-1)
    /// </summary>
    public float ORCTurbineEfficiency { get; set; } = 0.85f; // 85%

    /// <summary>
    ///     ORC pump isentropic efficiency (0-1)
    /// </summary>
    public float ORCPumpEfficiency { get; set; } = 0.75f; // 75%

    /// <summary>
    ///     ORC generator efficiency (0-1)
    /// </summary>
    public float ORCGeneratorEfficiency { get; set; } = 0.95f; // 95%

    /// <summary>
    ///     Minimum pinch point temperature in evaporator (K)
    /// </summary>
    public float ORCMinPinchPoint { get; set; } = 10.0f; // 10K

    /// <summary>
    ///     Superheat degrees (K)
    /// </summary>
    public float ORCSuperheat { get; set; } = 5.0f; // 5K

    /// <summary>
    ///     Maximum ORC working fluid mass flow rate (kg/s)
    /// </summary>
    public float ORCMaxMassFlowRate { get; set; } = 100.0f;

    /// <summary>
    ///     ORC working fluid name (from ORCFluidLibrary)
    /// </summary>
    public string ORCWorkingFluid { get; set; } = "R245fa";

    // ===== Economic Analysis Parameters =====

    /// <summary>
    ///     Enable economic analysis for geothermal power project
    /// </summary>
    public bool EnableEconomicAnalysis { get; set; } = false;

    /// <summary>
    ///     Project lifetime for economic analysis (years)
    /// </summary>
    public int EconomicProjectLifetime { get; set; } = 30;

    /// <summary>
    ///     Electricity price (USD/MWh)
    /// </summary>
    public float ElectricityPrice { get; set; } = 80.0f;

    /// <summary>
    ///     Discount rate for NPV calculations (fraction, e.g., 0.08 = 8%)
    /// </summary>
    public float DiscountRate { get; set; } = 0.08f;

    /// <summary>
    ///     Number of production wells
    /// </summary>
    public int NumberOfProductionWells { get; set; } = 1;

    /// <summary>
    ///     Number of injection wells
    /// </summary>
    public int NumberOfInjectionWells { get; set; } = 1;

    /// <summary>
    ///     Drilling cost per meter (USD/m)
    /// </summary>
    public float DrillingCostPerMeter { get; set; } = 1500.0f;

    /// <summary>
    ///     Power plant specific cost (USD/kW)
    /// </summary>
    public float PowerPlantSpecificCost { get; set; } = 3000.0f;

    /// <summary>
    ///     Annual O&M cost as percentage of capital cost
    /// </summary>
    public float AnnualOMPercentage { get; set; } = 0.03f; // 3%

    /// <summary>
    ///     Capacity factor for economic analysis (0-1)
    /// </summary>
    public float EconomicCapacityFactor { get; set; } = 0.90f; // 90%

    /// <summary>
    ///     Initialize default seasonal curve for BTES mode.
    ///     Creates a sinusoidal curve with charging in summer and discharging in winter.
    /// </summary>
    public void InitializeDefaultSeasonalCurve()
    {
        SeasonalEnergyCurve.Clear();

        // Initialize random generator with seed (or time-based if seed is 0)
        Random random = BTESRandomSeed > 0 ? new Random(BTESRandomSeed) : new Random();

        // Create a default seasonal curve (365 days)
        // Summer (charging): positive values
        // Winter (discharging): negative values
        for (int day = 0; day < 365; day++)
        {
            // Sine wave with offset: charges in summer (day 120-270), discharges in winter
            double dayAngle = (day - 195) * 2 * Math.PI / 365.0; // Peak at day 195 (mid-July)
            double baseEnergy = Math.Sin(dayAngle) * BTESSeasonalPeakRatio * BTESAnnualEnergyStorage * 1000 / 365;

            double dailyEnergy = baseEnergy;

            // Apply random variations if enabled
            if (BTESApplyRandomVariations)
            {
                // Generate smooth variations using multiple sine waves (weather patterns)
                double shortTermVariation = Math.Sin(day * 2 * Math.PI / 7.0) * 0.3;  // Weekly variation
                double mediumTermVariation = Math.Sin(day * 2 * Math.PI / 30.0) * 0.5; // Monthly variation
                double randomNoise = (random.NextDouble() - 0.5) * 2.0; // Daily random noise

                // Combine variations
                double totalVariation = (shortTermVariation + mediumTermVariation + randomNoise) / 3.0;

                // Apply variation (scaled by magnitude parameter)
                dailyEnergy *= (1.0 + totalVariation * BTESRandomVariationMagnitude);
            }

            SeasonalEnergyCurve.Add(dailyEnergy);
        }
    }

    public void SetDefaultValues()
    {
        if (!LayerThermalConductivities.Any())
            LayerThermalConductivities = new Dictionary<string, double>
            {
                { "Soil", 1.5 }, { "Clay", 1.2 }, { "Sand", 2.0 }, { "Gravel", 2.5 }, { "Sandstone", 2.8 },
                { "Limestone", 2.9 }, { "Granite", 3.0 }, { "Basalt", 1.7 }
            };
        if (!LayerSpecificHeats.Any())
            LayerSpecificHeats = new Dictionary<string, double>
            {
                { "Soil", 1840 }, { "Clay", 1380 }, { "Sand", 830 }, { "Gravel", 840 }, { "Sandstone", 920 },
                { "Limestone", 810 }, { "Granite", 790 }, { "Basalt", 840 }
            };
        if (!LayerDensities.Any())
            LayerDensities = new Dictionary<string, double>
            {
                { "Soil", 1800 }, { "Clay", 1900 }, { "Sand", 2650 }, { "Gravel", 2700 }, { "Sandstone", 2500 },
                { "Limestone", 2700 }, { "Granite", 2750 }, { "Basalt", 2900 }
            };
        if (!LayerPorosities.Any())
            LayerPorosities = new Dictionary<string, double>
            {
                { "Soil", 0.4 }, { "Clay", 0.45 }, { "Sand", 0.35 }, { "Gravel", 0.25 }, { "Sandstone", 0.15 },
                { "Limestone", 0.1 }, { "Granite", 0.01 }, { "Basalt", 0.05 }
            };
        if (!LayerPermeabilities.Any())
            LayerPermeabilities = new Dictionary<string, double>
            {
                { "Soil", 1e-12 }, { "Clay", 1e-15 }, { "Sand", 1e-11 }, { "Gravel", 1e-9 }, { "Sandstone", 1e-13 },
                { "Limestone", 1e-14 }, { "Granite", 1e-16 }, { "Basalt", 1e-15 }
            };
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
            case GeothermalSimulationPreset.BTESThermalBattery: ApplyBTESThermalBatteryPreset(); break;
            case GeothermalSimulationPreset.ExplorationTest: ApplyExplorationTestPreset(); break;
            case GeothermalSimulationPreset.Custom: break;
        }
    }

    private void ApplyShallowGSHPPreset()
    {
        HeatExchangerType = HeatExchangerType.UTube;
        FlowConfiguration = FlowConfiguration.CounterFlow;
        PipeInnerDiameter = 0.032;
        PipeOuterDiameter = 0.040;
        PipeSpacing = 0.080;
        PipeThermalConductivity = 0.4;
        InnerPipeThermalConductivity = 0.4;
        GroutThermalConductivity = 2.0;
        FluidMassFlowRate = 0.5;
        FluidInletTemperature = 283.15;
        SurfaceTemperature = 283.15;
        AverageGeothermalGradient = 0.025;
        GeothermalHeatFlux = 0.060;
        DomainRadius = 30;
        DomainExtension = 10;
        RadialGridPoints = 40;
        AngularGridPoints = 24;
        VerticalGridPoints = 80;
        SimulateGroundwaterFlow = true;
        GroundwaterVelocity = new Vector3(1e-7f, 0, 0);
        SimulationTime = 86400 * 30;
        TimeStep = 3600 * 1;
        SaveInterval = 24;
    }

    private void ApplyMediumDepthHeatingPreset()
    {
        HeatExchangerType = HeatExchangerType.UTube;
        FlowConfiguration = FlowConfiguration.CounterFlow;
        PipeInnerDiameter = 0.065;
        PipeOuterDiameter = 0.075;
        PipeSpacing = 0.150;
        PipeThermalConductivity = 0.4;
        InnerPipeThermalConductivity = 0.4;
        GroutThermalConductivity = 2.2;
        FluidMassFlowRate = 3.0;
        FluidInletTemperature = 288.15;
        SurfaceTemperature = 285.15;
        AverageGeothermalGradient = 0.030;
        GeothermalHeatFlux = 0.065;
        DomainRadius = 75;
        DomainExtension = 20;
        RadialGridPoints = 50;
        AngularGridPoints = 32;
        VerticalGridPoints = 120;
        SimulateGroundwaterFlow = true;
        GroundwaterVelocity = new Vector3(5e-8f, 0, 0);
        SimulationTime = 86400 * 180;
        TimeStep = 3600 * 2;
        SaveInterval = 12;
    }

    private void ApplyDeepGeothermalProductionPreset()
    {
        HeatExchangerType = HeatExchangerType.Coaxial;
        FlowConfiguration = FlowConfiguration.CounterFlowReversed;
        PipeInnerDiameter = 0.125;
        PipeOuterDiameter = 0.220;
        PipeSpacing = 0.200;
        PipeThermalConductivity = 45.0;
        InnerPipeThermalConductivity = 0.01;
        GroutThermalConductivity = 2.5;
        FluidMassFlowRate = 15.0;
        FluidInletTemperature = 293.15;
        FluidViscosity = 0.0005;
        FluidThermalConductivity = 0.65;
        SurfaceTemperature = 288.15;
        AverageGeothermalGradient = 0.035;
        GeothermalHeatFlux = 0.075;
        DomainRadius = 150;
        DomainExtension = 50;
        RadialGridPoints = 60;
        AngularGridPoints = 36;
        VerticalGridPoints = 150;
        SimulateGroundwaterFlow = true;
        GroundwaterVelocity = new Vector3(1e-8f, 0, 0);
        SimulationTime = 86400 * 365 * 5;
        TimeStep = 3600 * 6;
        SaveInterval = 4;
    }

    private void ApplyEnhancedGeothermalSystemPreset()
    {
        HeatExchangerType = HeatExchangerType.Coaxial;
        FlowConfiguration = FlowConfiguration.CounterFlowReversed;
        PipeInnerDiameter = 0.150;
        PipeOuterDiameter = 0.250;
        PipeSpacing = 0.230;
        PipeThermalConductivity = 45.0;
        InnerPipeThermalConductivity = 0.01;
        GroutThermalConductivity = 2.8;
        FluidMassFlowRate = 30.0;
        FluidInletTemperature = 313.15;
        FluidDensity = 950;
        FluidViscosity = 0.0003;
        FluidThermalConductivity = 0.68;
        SurfaceTemperature = 288.15;
        AverageGeothermalGradient = 0.040;
        GeothermalHeatFlux = 0.085;
        SimulateFractures = true;
        FractureAperture = 0.005;
        FracturePermeability = 1e-10;
        DomainRadius = 200;
        DomainExtension = 100;
        RadialGridPoints = 70;
        AngularGridPoints = 36;
        VerticalGridPoints = 180;
        SimulateGroundwaterFlow = true;
        GroundwaterVelocity = new Vector3(5e-7f, 0, 0);
        LongitudinalDispersivity = 10.0;
        TransverseDispersivity = 1.0;
        SimulationTime = 86400 * 365 * 10;
        TimeStep = 3600 * 12;
        SaveInterval = 2;
    }

    private void ApplyAquiferThermalStoragePreset()
    {
        HeatExchangerType = HeatExchangerType.UTube;
        FlowConfiguration = FlowConfiguration.CounterFlow;
        PipeInnerDiameter = 0.080;
        PipeOuterDiameter = 0.090;
        PipeSpacing = 0.180;
        PipeThermalConductivity = 0.4;
        InnerPipeThermalConductivity = 0.4;
        GroutThermalConductivity = 2.5;
        FluidMassFlowRate = 5.0;
        FluidInletTemperature = 303.15;
        SurfaceTemperature = 285.15;
        AverageGeothermalGradient = 0.025;
        GeothermalHeatFlux = 0.060;
        DomainRadius = 100;
        DomainExtension = 30;
        RadialGridPoints = 55;
        AngularGridPoints = 36;
        VerticalGridPoints = 100;
        SimulateGroundwaterFlow = true;
        GroundwaterVelocity = new Vector3(5e-6f, 0, 0);
        LongitudinalDispersivity = 5.0;
        TransverseDispersivity = 0.5;
        SimulationTime = 86400 * 180;
        TimeStep = 3600 * 3;
        SaveInterval = 8;
    }

    private void ApplyBTESThermalBatteryPreset()
    {
        // BTES (Borehole Thermal Energy Storage) - Seasonal thermal battery
        HeatExchangerType = HeatExchangerType.Coaxial; // Coaxial is preferred for BTES
        FlowConfiguration = FlowConfiguration.CounterFlowReversed;
        PipeInnerDiameter = 0.100;
        PipeOuterDiameter = 0.160;
        PipeSpacing = 0.150;
        PipeThermalConductivity = 45.0; // Steel pipe for good heat transfer
        InnerPipeThermalConductivity = 0.03; // Insulated inner pipe
        GroutThermalConductivity = 2.5;
        FluidMassFlowRate = 3.0;
        FluidInletTemperature = 285.15; // This will be overridden by seasonal curve
        SurfaceTemperature = 285.15;
        AverageGeothermalGradient = 0.025;
        GeothermalHeatFlux = 0.060;
        DomainRadius = 80; // Larger domain for thermal storage
        DomainExtension = 25;
        RadialGridPoints = 50;
        AngularGridPoints = 32;
        VerticalGridPoints = 100;
        SimulateGroundwaterFlow = true;
        GroundwaterVelocity = new Vector3(1e-7f, 0, 0); // Low groundwater flow
        SimulationTime = 86400 * 365 * 5; // 5 years to see multiple seasonal cycles
        TimeStep = 3600 * 6; // 6 hour time step
        SaveInterval = 1; // Save every step for visualization
        ConvergenceTolerance = 5e-3;
        MaxIterationsPerStep = 200;

        // Enable BTES mode
        EnableBTESMode = true;
        SaveAllTimeFrames = true;
        BTESAnnualEnergyStorage = 1000.0; // 1 GWh/year
        BTESSeasonalPeakRatio = 2.5;
        BTESChargingTemperature = 313.15; // 40°C for charging
        BTESDischargingTemperature = 278.15; // 5°C for discharging

        // Initialize seasonal curve
        InitializeDefaultSeasonalCurve();
    }

    private void ApplyExplorationTestPreset()
    {
        HeatExchangerType = HeatExchangerType.UTube;
        FlowConfiguration = FlowConfiguration.CounterFlow;
        PipeInnerDiameter = 0.050;
        PipeOuterDiameter = 0.063;
        PipeSpacing = 0.125;
        PipeThermalConductivity = 0.4;
        InnerPipeThermalConductivity = 0.4;
        GroutThermalConductivity = 2.0;
        FluidMassFlowRate = 1.5;
        FluidInletTemperature = 288.15;
        SurfaceTemperature = 285.15;
        AverageGeothermalGradient = 0.030;
        GeothermalHeatFlux = 0.065;
        DomainRadius = 50;
        DomainExtension = 20;
        RadialGridPoints = 35;
        AngularGridPoints = 24;
        VerticalGridPoints = 60;
        SimulateGroundwaterFlow = true;
        GroundwaterVelocity = new Vector3(1e-7f, 0, 0);
        SimulationTime = 86400 * 7;
        TimeStep = 30 * 60;
        SaveInterval = 48;
        ConvergenceTolerance = 2e-3;
        MaxIterationsPerStep = 100;
    }

    public static string GetPresetDescription(GeothermalSimulationPreset preset)
    {
        return preset switch
        {
            GeothermalSimulationPreset.Custom => "Custom user-defined parameters",
            GeothermalSimulationPreset.ShallowGSHP =>
                "Shallow GSHP (50-200m): Low-flow U-Tube for residential heating/cooling.",
            GeothermalSimulationPreset.MediumDepthHeating =>
                "Medium Depth (500-1500m): Medium-flow U-Tube for district heating.",
            GeothermalSimulationPreset.DeepGeothermalProduction =>
                "Deep Production (2-5km): High-flow Coaxial (VIT) for utility-scale heat/power.",
            GeothermalSimulationPreset.EnhancedGeothermalSystem =>
                "EGS (3-6km): Very high-flow Coaxial (VIT) with fracture flow for high-temp power.",
            GeothermalSimulationPreset.AquiferThermalStorage =>
                "ATES (50-300m): High-flow U-Tube for seasonal energy storage in aquifers.",
            GeothermalSimulationPreset.BTESThermalBattery =>
                "BTES (50-300m): Borehole Thermal Energy Storage with seasonal charging/discharging cycles for long-term heat storage.",
            GeothermalSimulationPreset.ExplorationTest =>
                "Quick Test (any depth): Coarse grid, 7-day run for rapid feasibility assessment.",
            _ => "Unknown preset"
        };
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
    BTESThermalBattery,
    ExplorationTest,
    CCUSCarbonateStorage
}

public enum PoreNetworkGenerationMode
{
    Conservative = 1,
    Aggressive = 3
}

public class FluidCompositionEntry
{
    public string SpeciesName { get; set; }
    public double Concentration_mol_L { get; set; }
    public string Units { get; set; } = "mol/L";
    public string Notes { get; set; }
}