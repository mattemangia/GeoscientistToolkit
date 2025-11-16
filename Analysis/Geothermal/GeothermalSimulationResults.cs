// GeoscientistToolkit/Analysis/Geothermal/GeothermalSimulationResults.cs

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Data.Mesh3D;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     Stores all outputs from a geothermal borehole simulation.
/// </summary>
public class GeothermalSimulationResults
{
    /// <summary>
    ///     A copy of the options used to generate these results.
    /// </summary>
    public GeothermalSimulationOptions Options { get; set; }

    // Temperature Fields

    /// <summary>
    ///     3D temperature field at each saved time step.
    ///     Dictionary key is time in seconds, value is 3D array [r, theta, z].
    /// </summary>
    public Dictionary<double, float[,,]> TemperatureFields { get; set; } = new();

    /// <summary>
    ///     Final steady-state temperature field [r, theta, z].
    /// </summary>
    public float[,,] FinalTemperatureField { get; set; }

    /// <summary>
    ///     Fluid temperature along the heat exchanger at final time.
    /// </summary>
    public List<(double depth, double temperatureDown, double temperatureUp)> FluidTemperatureProfile { get; set; } =
        new();

    // Flow Fields

    /// <summary>
    ///     3D pressure field (Pa) [r, theta, z].
    /// </summary>
    public float[,,] PressureField { get; set; }

    /// <summary>
    ///     3D hydraulic head field (m) [r, theta, z].
    /// </summary>
    public float[,,] HydraulicHeadField { get; set; }

    /// <summary>
    ///     3D Darcy velocity field (m/s) [r, theta, z, component].
    /// </summary>
    public float[,,,] DarcyVelocityField { get; set; }

    /// <summary>
    ///     Streamline data for flow visualization.
    /// </summary>
    public List<List<Vector3>> Streamlines { get; set; } = new();

    // Heat Transfer Analysis

    /// <summary>
    ///     Total heat extraction rate over time (W).
    /// </summary>
    public List<(double time, double heatRate)> HeatExtractionRate { get; set; } = new();

    /// <summary>
    ///     Outlet fluid temperature over time (K).
    /// </summary>
    public List<(double time, double temperature)> OutletTemperature { get; set; } = new();

    /// <summary>
    ///     Coefficient of Performance (COP) over time.
    /// </summary>
    public List<(double time, double cop)> CoefficientOfPerformance { get; set; } = new();

    /// <summary>
    ///     Total extracted energy (J).
    /// </summary>
    public double TotalExtractedEnergy { get; set; }

    /// <summary>
    ///     Average heat extraction rate (W).
    /// </summary>
    public double AverageHeatExtractionRate { get; set; }

    // Transport Parameters

    /// <summary>
    ///     Péclet number field (dimensionless) [r, theta, z].
    /// </summary>
    public float[,,] PecletNumberField { get; set; }

    /// <summary>
    ///     Thermal dispersivity field (m) [r, theta, z].
    /// </summary>
    public float[,,] DispersivityField { get; set; }

    /// <summary>
    ///     Average Péclet number in aquifer zones.
    /// </summary>
    public double AveragePecletNumber { get; set; }

    /// <summary>
    ///     Calculated longitudinal dispersivity (m).
    /// </summary>
    public double LongitudinalDispersivity { get; set; }

    /// <summary>
    ///     Calculated transverse dispersivity (m).
    /// </summary>
    public double TransverseDispersivity { get; set; }

    // Performance Metrics

    /// <summary>
    ///     Borehole thermal resistance (m·K/W).
    /// </summary>
    public double BoreholeThermalResistance { get; set; }

    /// <summary>
    ///     Effective ground thermal conductivity (W/m·K).
    /// </summary>
    public double EffectiveGroundConductivity { get; set; }

    /// <summary>
    ///     Ground thermal diffusivity (m²/s).
    /// </summary>
    public double GroundThermalDiffusivity { get; set; }

    /// <summary>
    ///     Thermal influence radius at final time (m).
    /// </summary>
    public double ThermalInfluenceRadius { get; set; }

    /// <summary>
    ///     Pressure drawdown at borehole (Pa).
    /// </summary>
    public double PressureDrawdown { get; set; }

    // BTES (Borehole Thermal Energy Storage) Performance Metrics

    /// <summary>
    ///     Total energy charged into storage (J) - positive heat flow periods.
    /// </summary>
    public double BTESTotalEnergyCharged { get; set; }

    /// <summary>
    ///     Total energy discharged from storage (J) - negative heat flow periods.
    /// </summary>
    public double BTESTotalEnergyDischarged { get; set; }

    /// <summary>
    ///     Round-trip storage efficiency (%) = (discharged / charged) * 100.
    /// </summary>
    public double BTESStorageEfficiency { get; set; }

    /// <summary>
    ///     Number of complete charging/discharging cycles detected.
    /// </summary>
    public int BTESNumberOfCycles { get; set; }

    /// <summary>
    ///     Average charging power during charging periods (W).
    /// </summary>
    public double BTESAverageChargingPower { get; set; }

    /// <summary>
    ///     Average discharging power during discharging periods (W).
    /// </summary>
    public double BTESAverageDischargingPower { get; set; }

    /// <summary>
    ///     Peak charging power observed (W).
    /// </summary>
    public double BTESPeakChargingPower { get; set; }

    /// <summary>
    ///     Peak discharging power observed (W).
    /// </summary>
    public double BTESPeakDischargingPower { get; set; }

    /// <summary>
    ///     Temperature swing: maximum ground temperature minus minimum (K).
    /// </summary>
    public double BTESTemperatureSwing { get; set; }

    /// <summary>
    ///     Maximum ground temperature observed during simulation (K).
    /// </summary>
    public double BTESMaxGroundTemperature { get; set; }

    /// <summary>
    ///     Minimum ground temperature observed during simulation (K).
    /// </summary>
    public double BTESMinGroundTemperature { get; set; }

    /// <summary>
    ///     Seasonal Performance Factor: ratio of useful energy to input energy.
    /// </summary>
    public double BTESSeasonalPerformanceFactor { get; set; }

    /// <summary>
    ///     Average ground temperature at end of simulation (K).
    /// </summary>
    public double BTESFinalAverageGroundTemperature { get; set; }

    /// <summary>
    ///     Initial average ground temperature before BTES operation (K).
    /// </summary>
    public double BTESInitialAverageGroundTemperature { get; set; }

    /// <summary>
    ///     Net ground temperature change after all cycles (K).
    /// </summary>
    public double BTESNetGroundTemperatureChange { get; set; }

    /// <summary>
    ///     Thermal energy stored in ground at end of simulation (J).
    /// </summary>
    public double BTESStoredThermalEnergy { get; set; }

    /// <summary>
    ///     List of charging/discharging periods with start time, end time, and energy.
    /// </summary>
    public List<(double startTime, double endTime, double energy, string type)> BTESCyclePeriods { get; set; } = new();

    // Layer Analysis

    /// <summary>
    ///     Heat flux contribution from each geological layer.
    /// </summary>
    public Dictionary<string, double> LayerHeatFluxContributions { get; set; } = new();

    /// <summary>
    ///     Average temperature change in each layer.
    /// </summary>
    public Dictionary<string, double> LayerTemperatureChanges { get; set; } = new();

    /// <summary>
    ///     Groundwater flow rate through each layer (m³/s).
    /// </summary>
    public Dictionary<string, double> LayerFlowRates { get; set; } = new();

    // Visualization Data

    /// <summary>
    ///     Generated 3D temperature isosurfaces.
    /// </summary>
    public List<Mesh3DDataset> TemperatureIsosurfaces { get; set; } = new();

    /// <summary>
    ///     2D temperature slices at specified depths.
    /// </summary>
    public Dictionary<double, float[,]> TemperatureSlices { get; set; } = new();

    /// <summary>
    ///     2D pressure slices at specified depths.
    /// </summary>
    public Dictionary<double, float[,]> PressureSlices { get; set; } = new();

    /// <summary>
    ///     2D velocity magnitude slices at specified depths.
    /// </summary>
    public Dictionary<double, float[,]> VelocityMagnitudeSlices { get; set; } = new();

    /// <summary>
    ///     3D mesh representing the simulation domain.
    /// </summary>
    public Mesh3DDataset DomainMesh { get; set; }

    /// <summary>
    ///     3D mesh representing the borehole and heat exchanger.
    /// </summary>
    public Mesh3DDataset BoreholeMesh { get; set; }

    // Computational Information

    /// <summary>
    ///     Total simulation time elapsed.
    /// </summary>
    public TimeSpan ComputationTime { get; set; }

    /// <summary>
    ///     Number of time steps computed.
    /// </summary>
    public int TimeStepsComputed { get; set; }

    /// <summary>
    ///     Average iterations per time step.
    /// </summary>
    public double AverageIterationsPerStep { get; set; }

    /// <summary>
    ///     Final convergence error.
    /// </summary>
    public double FinalConvergenceError { get; set; }

    /// <summary>
    ///     Memory usage peak (MB).
    /// </summary>
    public double PeakMemoryUsage { get; set; }

    // Thermodynamics and Geochemistry Results

    /// <summary>
    ///     Generated pore network model for precipitation calculations
    /// </summary>
    public Data.Pnm.PNMDataset PoreNetworkModel { get; set; }

    /// <summary>
    ///     Precipitation concentration field [r, theta, z] in mol/m³
    /// </summary>
    public Dictionary<string, float[,,]> PrecipitationFields { get; set; } = new();

    /// <summary>
    ///     Dissolution concentration field [r, theta, z] in mol/m³
    /// </summary>
    public Dictionary<string, float[,,]> DissolutionFields { get; set; } = new();

    // ===== Multiphase Flow Results =====

    /// <summary>
    ///     Water saturation field [r, theta, z] (0-1)
    /// </summary>
    public float[,,] WaterSaturationField { get; set; }

    /// <summary>
    ///     Gas saturation field [r, theta, z] (0-1)
    /// </summary>
    public float[,,] GasSaturationField { get; set; }

    /// <summary>
    ///     Liquid CO2 saturation field [r, theta, z] (0-1)
    /// </summary>
    public float[,,] CO2SaturationField { get; set; }

    /// <summary>
    ///     Brine density field with salinity effects [r, theta, z] (kg/m³)
    /// </summary>
    public float[,,] BrineDensityField { get; set; }

    /// <summary>
    ///     Dissolved CO2 concentration field [r, theta, z] (mass fraction)
    /// </summary>
    public float[,,] DissolvedCO2Field { get; set; }

    /// <summary>
    ///     Salinity field [r, theta, z] (mass fraction of NaCl)
    /// </summary>
    public float[,,] SalinityField { get; set; }

    /// <summary>
    ///     Capillary pressure field [r, theta, z] (Pa)
    /// </summary>
    public float[,,] CapillaryPressureField { get; set; }

    /// <summary>
    ///     Water relative permeability field [r, theta, z] (0-1)
    /// </summary>
    public float[,,] RelativePermeabilityWaterField { get; set; }

    /// <summary>
    ///     Gas relative permeability field [r, theta, z] (0-1)
    /// </summary>
    public float[,,] RelativePermeabilityGasField { get; set; }

    /// <summary>
    ///     Saturation history over time
    /// </summary>
    public Dictionary<double, (float[,,] water, float[,,] gas, float[,,] co2)> SaturationHistory { get; set; } = new();

    // ===== Adaptive Mesh Refinement Results =====

    /// <summary>
    ///     Refinement level field [r, theta, z] (0 = base mesh, 1-3 = refined)
    /// </summary>
    public int[,,] RefinementLevelField { get; set; }

    /// <summary>
    ///     Temperature gradient magnitude field [r, theta, z] (K/m)
    /// </summary>
    public float[,,] TemperatureGradientField { get; set; }

    /// <summary>
    ///     Number of refinement operations performed
    /// </summary>
    public int TotalRefinementOperations { get; set; }

    /// <summary>
    ///     Number of coarsening operations performed
    /// </summary>
    public int TotalCoarseningOperations { get; set; }

    /// <summary>
    ///     Refinement history over time
    /// </summary>
    public List<(double time, int refinedCells, int coarsenedCells)> RefinementHistory { get; set; } = new();

    // ===== Time-Varying Boundary Conditions Results =====

    /// <summary>
    ///     Outdoor temperature history (°C)
    /// </summary>
    public List<(double time, float temperature)> OutdoorTemperatureHistory { get; set; } = new();

    /// <summary>
    ///     Load demand history (W)
    /// </summary>
    public List<(double time, float load)> LoadDemandHistory { get; set; } = new();

    /// <summary>
    ///     Actual mass flow rate history (kg/s)
    /// </summary>
    public List<(double time, float flowRate)> ActualFlowRateHistory { get; set; } = new();

    /// <summary>
    ///     Inlet temperature history (°C)
    /// </summary>
    public List<(double time, float temperature)> InletTemperatureHistory { get; set; } = new();

    // ===== Enhanced HVAC Results =====

    /// <summary>
    ///     Instantaneous COP accounting for part-load and temperature effects
    /// </summary>
    public List<(double time, float cop)> InstantaneousCOP { get; set; } = new();

    /// <summary>
    ///     Part-load ratio history (0-1)
    /// </summary>
    public List<(double time, float plr)> PartLoadRatioHistory { get; set; } = new();

    /// <summary>
    ///     Compressor power history (W)
    /// </summary>
    public List<(double time, float power)> CompressorPowerHistory { get; set; } = new();

    /// <summary>
    ///     Seasonal Performance Factor (SPF)
    /// </summary>
    public float SeasonalPerformanceFactor { get; set; }

    /// <summary>
    ///     Seasonal COP for heating mode (SCOP)
    /// </summary>
    public float SeasonalCOPHeating { get; set; }

    /// <summary>
    ///     Seasonal Energy Efficiency Ratio for cooling mode (SEER, BTU/Wh)
    /// </summary>
    public float SeasonalEER { get; set; }

    /// <summary>
    ///     Total heat delivered to building (J)
    /// </summary>
    public double TotalHeatDelivered { get; set; }

    /// <summary>
    ///     Total work input including auxiliaries (J)
    /// </summary>
    public double TotalWorkInput { get; set; }

    /// <summary>
    ///     Total heating hours
    /// </summary>
    public double TotalHeatingHours { get; set; }

    /// <summary>
    ///     Total cooling hours
    /// </summary>
    public double TotalCoolingHours { get; set; }

    /// <summary>
    ///     Entering water temperature history (°C)
    /// </summary>
    public List<(double time, float temperature)> EnteringWaterTemperatureHistory { get; set; } = new();

    /// <summary>
    ///     Supply temperature to building history (°C)
    /// </summary>
    public List<(double time, float temperature)> SupplyTemperatureHistory { get; set; } = new();

    // ===== Fractured Media Results =====

    /// <summary>
    ///     Matrix temperature field for dual-continuum model [r, theta, z] (K)
    /// </summary>
    public float[,,] MatrixTemperatureField { get; set; }

    /// <summary>
    ///     Fracture temperature field for dual-continuum model [r, theta, z] (K)
    /// </summary>
    public float[,,] FractureTemperatureField { get; set; }

    /// <summary>
    ///     Matrix pressure field [r, theta, z] (Pa)
    /// </summary>
    public float[,,] MatrixPressureField { get; set; }

    /// <summary>
    ///     Fracture pressure field [r, theta, z] (Pa)
    /// </summary>
    public float[,,] FracturePressureField { get; set; }

    /// <summary>
    ///     Fracture aperture field [r, theta, z] (m)
    /// </summary>
    public float[,,] FractureApertureField { get; set; }

    /// <summary>
    ///     Fracture permeability field [r, theta, z] (m²)
    /// </summary>
    public float[,,] FracturePermeabilityField { get; set; }

    /// <summary>
    ///     Matrix-fracture heat transfer rate [r, theta, z] (W/m³)
    /// </summary>
    public float[,,] MatrixFractureHeatTransferField { get; set; }

    /// <summary>
    ///     Matrix-fracture mass transfer rate [r, theta, z] (kg/s/m³)
    /// </summary>
    public float[,,] MatrixFractureMassTransferField { get; set; }

    /// <summary>
    ///     Average matrix-fracture temperature difference (K)
    /// </summary>
    public double AverageMatrixFractureTempDifference { get; set; }

    /// <summary>
    ///     Total heat transfer between matrix and fractures (W)
    /// </summary>
    public double TotalMatrixFractureHeatTransfer { get; set; }

    /// <summary>
    ///     Pore/throat radius change over time due to precipitation/dissolution
    /// </summary>
    public Dictionary<int, double> PoreRadiusHistory { get; set; } = new();  // PoreID -> final radius

    /// <summary>
    ///     Permeability evolution over time due to pore radius changes
    /// </summary>
    public List<(double time, double averagePermeability)> PermeabilityEvolution { get; set; } = new();

    /// <summary>
    ///     Total moles of minerals precipitated by species
    /// </summary>
    public Dictionary<string, double> TotalPrecipitation_mol { get; set; } = new();

    /// <summary>
    ///     Total moles of minerals dissolved by species
    /// </summary>
    public Dictionary<string, double> TotalDissolution_mol { get; set; } = new();

    /// <summary>
    ///     pH evolution over time
    /// </summary>
    public List<(double time, double pH)> PHEvolution { get; set; } = new();

    /// <summary>
    ///     Saturation index evolution for different minerals over time
    /// </summary>
    public Dictionary<string, List<(double time, double saturationIndex)>> SaturationIndexEvolution { get; set; } = new();

    // ===== Geomechanics Results =====

    /// <summary>
    ///     Von Mises stress field [r, theta, z] (Pa) - indicates failure potential
    /// </summary>
    public float[,,] VonMisesStressField { get; set; }

    /// <summary>
    ///     Normal stress XX component [r, theta, z] (Pa)
    /// </summary>
    public float[,,] StressXXField { get; set; }

    /// <summary>
    ///     Normal stress YY component [r, theta, z] (Pa)
    /// </summary>
    public float[,,] StressYYField { get; set; }

    /// <summary>
    ///     Normal stress ZZ component [r, theta, z] (Pa)
    /// </summary>
    public float[,,] StressZZField { get; set; }

    /// <summary>
    ///     Volumetric displacement field [r, theta, z] (m) - ground deformation
    /// </summary>
    public float[,,] DisplacementField { get; set; }

    /// <summary>
    ///     Maximum von Mises stress in domain (Pa)
    /// </summary>
    public double MaxVonMisesStress { get; set; }

    /// <summary>
    ///     Maximum ground displacement (m)
    /// </summary>
    public double MaxDisplacement { get; set; }

    /// <summary>
    ///     Von Mises stress history over time
    /// </summary>
    public List<(double time, double maxStress)> StressHistory { get; set; } = new();

    /// <summary>
    ///     Displacement history over time
    /// </summary>
    public List<(double time, double maxDisplacement)> DisplacementHistory { get; set; } = new();

    /// <summary>
    ///     2D Von Mises stress slices at specified depths (for visualization)
    /// </summary>
    public Dictionary<double, float[,]> StressSlices { get; set; } = new();

    /// <summary>
    ///     2D displacement slices at specified depths (for visualization)
    /// </summary>
    public Dictionary<double, float[,]> DisplacementSlices { get; set; } = new();

    // Summary Report

    /// <summary>
    ///     Generate a summary report of the simulation results.
    /// </summary>
    public string GenerateSummaryReport()
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== Geothermal Simulation Results Summary ===");
        sb.AppendLine();

        sb.AppendLine("Simulation Configuration:");
        sb.AppendLine($"  - Heat Exchanger Type: {Options.HeatExchangerType}");
        sb.AppendLine($"  - Borehole Depth: {Options.BoreholeDataset.TotalDepth:F1} m");
        sb.AppendLine($"  - Simulation Time: {Options.SimulationTime / 86400:F1} days");
        sb.AppendLine($"  - Domain Radius: {Options.DomainRadius:F1} m");
        sb.AppendLine();

        sb.AppendLine("Thermal Performance:");
        sb.AppendLine($"  - Average Heat Extraction Rate: {AverageHeatExtractionRate:F0} W");
        sb.AppendLine($"  - Total Extracted Energy: {TotalExtractedEnergy / 1e9:F2} GJ");
        sb.AppendLine($"  - Final Outlet Temperature: {OutletTemperature.LastOrDefault().temperature - 273.15:F1} °C");
        sb.AppendLine($"  - Borehole Thermal Resistance: {BoreholeThermalResistance:F3} m·K/W");
        sb.AppendLine($"  - Thermal Influence Radius: {ThermalInfluenceRadius:F1} m");
        sb.AppendLine();

        // BTES-specific metrics
        if (Options.EnableBTESMode)
        {
            sb.AppendLine("=== BTES (Borehole Thermal Energy Storage) Performance ===");
            sb.AppendLine();

            sb.AppendLine("Energy Storage Metrics:");
            sb.AppendLine($"  - Total Energy Charged: {BTESTotalEnergyCharged / 1e9:F2} GJ ({BTESTotalEnergyCharged / 3.6e9:F2} MWh)");
            sb.AppendLine($"  - Total Energy Discharged: {BTESTotalEnergyDischarged / 1e9:F2} GJ ({BTESTotalEnergyDischarged / 3.6e9:F2} MWh)");
            sb.AppendLine($"  - Storage Efficiency: {BTESStorageEfficiency:F1} %");
            sb.AppendLine($"  - Net Energy Balance: {(BTESTotalEnergyCharged - BTESTotalEnergyDischarged) / 1e9:F2} GJ");
            sb.AppendLine($"  - Thermal Energy in Ground: {BTESStoredThermalEnergy / 1e9:F2} GJ");
            sb.AppendLine();

            sb.AppendLine("Power Performance:");
            sb.AppendLine($"  - Average Charging Power: {BTESAverageChargingPower / 1e3:F1} kW");
            sb.AppendLine($"  - Peak Charging Power: {BTESPeakChargingPower / 1e3:F1} kW");
            sb.AppendLine($"  - Average Discharging Power: {BTESAverageDischargingPower / 1e3:F1} kW");
            sb.AppendLine($"  - Peak Discharging Power: {BTESPeakDischargingPower / 1e3:F1} kW");
            sb.AppendLine($"  - Seasonal Performance Factor: {BTESSeasonalPerformanceFactor:F2}");
            sb.AppendLine();

            sb.AppendLine("Thermal Behavior:");
            sb.AppendLine($"  - Number of Charge/Discharge Cycles: {BTESNumberOfCycles}");
            sb.AppendLine($"  - Temperature Swing: {BTESTemperatureSwing:F1} K");
            sb.AppendLine($"  - Maximum Ground Temperature: {BTESMaxGroundTemperature - 273.15:F1} °C");
            sb.AppendLine($"  - Minimum Ground Temperature: {BTESMinGroundTemperature - 273.15:F1} °C");
            sb.AppendLine($"  - Initial Average Ground Temp: {BTESInitialAverageGroundTemperature - 273.15:F1} °C");
            sb.AppendLine($"  - Final Average Ground Temp: {BTESFinalAverageGroundTemperature - 273.15:F1} °C");
            sb.AppendLine($"  - Net Ground Temperature Change: {BTESNetGroundTemperatureChange:F2} K");
            sb.AppendLine();

            if (BTESCyclePeriods.Any())
            {
                sb.AppendLine("Charging/Discharging Periods:");
                foreach (var period in BTESCyclePeriods.Take(10)) // Show first 10 periods
                {
                    var days1 = period.startTime / 86400;
                    var days2 = period.endTime / 86400;
                    var energyGJ = period.energy / 1e9;
                    sb.AppendLine($"  - {period.type}: Days {days1:F1}-{days2:F1}, Energy: {energyGJ:F2} GJ");
                }
                if (BTESCyclePeriods.Count > 10)
                    sb.AppendLine($"  ... and {BTESCyclePeriods.Count - 10} more periods");
                sb.AppendLine();
            }
        }

        if (Options.SimulateGroundwaterFlow)
        {
            sb.AppendLine("Groundwater Flow:");
            sb.AppendLine($"  - Average Péclet Number: {AveragePecletNumber:F2}");
            sb.AppendLine($"  - Longitudinal Dispersivity: {LongitudinalDispersivity:F3} m");
            sb.AppendLine($"  - Transverse Dispersivity: {TransverseDispersivity:F3} m");
            sb.AppendLine($"  - Pressure Drawdown: {PressureDrawdown:F0} Pa");
            sb.AppendLine();
        }

        // Geomechanics results
        if (Options.EnableGeomechanics)
        {
            sb.AppendLine("=== Geomechanics (Stress & Deformation) ===");
            sb.AppendLine();

            sb.AppendLine("Stress Analysis:");
            sb.AppendLine($"  - Maximum von Mises Stress: {MaxVonMisesStress / 1e6:F2} MPa");
            sb.AppendLine($"  - Maximum Displacement: {MaxDisplacement * 1000:F3} mm");
            sb.AppendLine();

            // Evaluate stress level relative to typical rock strength
            double rockStrengthMPa = 50.0; // Typical tensile strength for granite ~5-25 MPa
            double stressMPa = MaxVonMisesStress / 1e6;
            double stressRatio = stressMPa / rockStrengthMPa;

            sb.AppendLine("Risk Assessment:");
            if (stressRatio < 0.1)
                sb.AppendLine($"  - Stress Level: LOW ({stressRatio * 100:F1}% of typical rock strength)");
            else if (stressRatio < 0.5)
                sb.AppendLine($"  - Stress Level: MODERATE ({stressRatio * 100:F1}% of typical rock strength)");
            else if (stressRatio < 1.0)
                sb.AppendLine($"  - Stress Level: HIGH ({stressRatio * 100:F1}% of typical rock strength)");
            else
                sb.AppendLine($"  - Stress Level: CRITICAL ({stressRatio * 100:F1}% of typical rock strength) - FAILURE RISK!");

            sb.AppendLine($"  - Displacement: {(MaxDisplacement < 0.001 ? "Negligible" : "Measurable")}");
            sb.AppendLine();
        }

        sb.AppendLine("Layer Contributions:");
        foreach (var layer in LayerHeatFluxContributions.OrderByDescending(l => l.Value))
            sb.AppendLine($"  - {layer.Key}: {Math.Round(layer.Value, 1):F1} heat flux");
        sb.AppendLine();

        sb.AppendLine("Computational Performance:");
        sb.AppendLine($"  - Total Computation Time: {ComputationTime.TotalMinutes:F1} minutes");
        sb.AppendLine($"  - Time Steps Computed: {TimeStepsComputed}");
        sb.AppendLine($"  - Average Iterations/Step: {AverageIterationsPerStep:F1}");
        sb.AppendLine($"  - Peak Memory Usage: {PeakMemoryUsage:F0} MB");

        return sb.ToString();
    }

    /// <summary>
    ///     Export results to CSV format for further analysis.
    /// </summary>
    public void ExportToCSV(string basePath)
    {
        // Export time series data
        using (var writer = new StreamWriter($"{basePath}_timeseries.csv"))
        {
            writer.WriteLine("Time_s,Time_days,HeatRate_W,OutletTemp_C,COP");

            for (var i = 0; i < HeatExtractionRate.Count; i++)
            {
                var time = HeatExtractionRate[i].time;
                var heat = HeatExtractionRate[i].heatRate;
                var temp = i < OutletTemperature.Count ? OutletTemperature[i].temperature - 273.15 : 0;
                var cop = i < CoefficientOfPerformance.Count ? CoefficientOfPerformance[i].cop : 0;

                writer.WriteLine($"{time},{time / 86400:F2},{heat:F1},{temp:F2},{cop:F2}");
            }
        }

        // Export fluid temperature profile
        using (var writer = new StreamWriter($"{basePath}_fluid_profile.csv"))
        {
            writer.WriteLine("Depth_m,TempDown_C,TempUp_C");

            foreach (var point in FluidTemperatureProfile)
                writer.WriteLine(
                    $"{point.depth:F1},{point.temperatureDown - 273.15:F2},{point.temperatureUp - 273.15:F2}");
        }

        // Export layer analysis
        using (var writer = new StreamWriter($"{basePath}_layers.csv"))
        {
            writer.WriteLine("Layer,HeatFlux_%,TempChange_K,FlowRate_m3/s");

            foreach (var layer in LayerHeatFluxContributions.Keys)
            {
                var flux = LayerHeatFluxContributions.GetValueOrDefault(layer, 0);
                var temp = LayerTemperatureChanges.GetValueOrDefault(layer, 0);
                var flow = LayerFlowRates.GetValueOrDefault(layer, 0);

                writer.WriteLine($"{layer},{flux:F1},{temp:F2},{flow:E3}");
            }
        }

        // Export BTES-specific metrics if available
        if (Options.EnableBTESMode)
        {
            using (var writer = new StreamWriter($"{basePath}_btes_metrics.csv"))
            {
                writer.WriteLine("Metric,Value,Unit");
                writer.WriteLine($"Total Energy Charged,{BTESTotalEnergyCharged / 1e9:F4},GJ");
                writer.WriteLine($"Total Energy Discharged,{BTESTotalEnergyDischarged / 1e9:F4},GJ");
                writer.WriteLine($"Storage Efficiency,{BTESStorageEfficiency:F2},%");
                writer.WriteLine($"Number of Cycles,{BTESNumberOfCycles},cycles");
                writer.WriteLine($"Average Charging Power,{BTESAverageChargingPower / 1e3:F2},kW");
                writer.WriteLine($"Average Discharging Power,{BTESAverageDischargingPower / 1e3:F2},kW");
                writer.WriteLine($"Peak Charging Power,{BTESPeakChargingPower / 1e3:F2},kW");
                writer.WriteLine($"Peak Discharging Power,{BTESPeakDischargingPower / 1e3:F2},kW");
                writer.WriteLine($"Temperature Swing,{BTESTemperatureSwing:F2},K");
                writer.WriteLine($"Max Ground Temperature,{BTESMaxGroundTemperature - 273.15:F2},C");
                writer.WriteLine($"Min Ground Temperature,{BTESMinGroundTemperature - 273.15:F2},C");
                writer.WriteLine($"Initial Avg Ground Temp,{BTESInitialAverageGroundTemperature - 273.15:F2},C");
                writer.WriteLine($"Final Avg Ground Temp,{BTESFinalAverageGroundTemperature - 273.15:F2},C");
                writer.WriteLine($"Net Ground Temp Change,{BTESNetGroundTemperatureChange:F3},K");
                writer.WriteLine($"Stored Thermal Energy,{BTESStoredThermalEnergy / 1e9:F4},GJ");
                writer.WriteLine($"Seasonal Performance Factor,{BTESSeasonalPerformanceFactor:F3},");
            }

            // Export BTES charging/discharging periods
            using (var writer = new StreamWriter($"{basePath}_btes_periods.csv"))
            {
                writer.WriteLine("StartTime_days,EndTime_days,Duration_days,Energy_GJ,Type");

                foreach (var period in BTESCyclePeriods)
                {
                    var startDays = period.startTime / 86400;
                    var endDays = period.endTime / 86400;
                    var duration = endDays - startDays;
                    var energyGJ = period.energy / 1e9;

                    writer.WriteLine($"{startDays:F2},{endDays:F2},{duration:F2},{energyGJ:F4},{period.type}");
                }
            }
        }
    }
}