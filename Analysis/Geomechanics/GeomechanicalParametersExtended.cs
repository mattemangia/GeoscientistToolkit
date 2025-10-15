// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalParametersExtended.cs
// Extension to GeomechanicalParameters for geothermal and fluid injection

using System.Numerics;

namespace GeoscientistToolkit.Analysis.Geomechanics;

// Add these properties to the existing GeomechanicalParameters class
public partial class GeomechanicalParameters
{
    // ========== GEOTHERMAL SIMULATION ==========
    public bool EnableGeothermal { get; set; } = false;
    public float SurfaceTemperature { get; set; } = 15f; // °C
    public float GeothermalGradient { get; set; } = 25f; // °C/km
    public float ThermalExpansionCoefficient { get; set; } = 10e-6f; // 1/K (typical for rock)
    public bool CalculateEnergyPotential { get; set; } = true;

    // ========== FLUID INJECTION SIMULATION ==========
    public bool EnableFluidInjection { get; set; } = false;
    public float FluidViscosity { get; set; } = 1e-3f; // Pa·s (water)
    public float FluidDensity { get; set; } = 1000f; // kg/m³
    public float InitialPorePressure { get; set; } = 10f; // MPa (hydrostatic)
    public float InjectionPressure { get; set; } = 50f; // MPa
    public float InjectionRate { get; set; } = 0.1f; // m³/s
    public Vector3 InjectionLocation { get; set; } = new(0.5f, 0.5f, 0.5f); // Normalized coordinates
    public int InjectionRadius { get; set; } = 5; // voxels
    public float MaxSimulationTime { get; set; } = 3600f; // seconds (1 hour)
    public float FluidTimeStep { get; set; } = 1f; // seconds

    // ========== FRACTURE MECHANICS ==========
    public bool EnableFractureFlow { get; set; } = true;
    public float FractureAperture_Coefficient { get; set; } = 1e-6f; // m/MPa (stress-dependent opening)
    public float MinimumFractureAperture { get; set; } = 1e-6f; // m (1 micron)
    public float FractureToughness { get; set; } = 1.0f; // MPa·m^0.5 (Mode I)
    public float CriticalStrainEnergy { get; set; } = 100f; // J/m² (Gc)

    // ========== AQUIFER INTERACTION ==========
    public bool EnableAquifer { get; set; } = false;
    public float AquiferPressure { get; set; } = 15f; // MPa (boundary condition)
    public float AquiferPermeability { get; set; } = 1e-15f; // m² (10 mD for sandstone)
    public float RockPermeability { get; set; } = 1e-18f; // m² (0.01 mD for tight rock)
    public float Porosity { get; set; } = 0.10f; // fraction

    // ========== POROELASTIC COUPLING ==========
    public bool EnablePoroelasticity { get; set; } = true;
    public float SkemptonCoefficient { get; set; } = 0.7f; // B parameter (0-1)

    // ========== SIMULATION CONTROL ==========
    public int FluidIterationsPerMechanicalStep { get; set; } = 10;
    public bool UseSequentialCoupling { get; set; } = true; // vs. fully coupled
}

// Extension to GeomechanicalResults for geothermal and fluid injection
public partial class GeomechanicalResults
{
    // ========== GEOTHERMAL FIELDS ==========
    public float[,,] TemperatureField { get; set; } // °C
    public float GeothermalEnergyPotential { get; set; } // MWh (recoverable)
    public float AverageThermalGradient { get; set; } // °C/km (measured from simulation)
    public Dictionary<string, float> ThermalStatistics { get; set; } = new();

    // ========== FLUID PRESSURE FIELDS ==========
    public float[,,] PressureField { get; set; } // Pa
    public float[,,] FluidSaturation { get; set; } // 0-1 (fraction)
    public float[,,] FluidVelocityX { get; set; } // m/s
    public float[,,] FluidVelocityY { get; set; }
    public float[,,] FluidVelocityZ { get; set; }
    public float MaxFluidPressure { get; set; } // Pa
    public float MinFluidPressure { get; set; } // Pa
    public float TotalFluidInjected { get; set; } // m³

    // ========== FRACTURE NETWORK ==========
    public float[,,] FractureAperture { get; set; } // m (local fracture opening)
    public bool[,,] FractureConnectivity { get; set; } // Connected to injection point
    public List<FractureSegment> FractureNetwork { get; set; } = new();
    public float TotalFractureVolume { get; set; } // m³
    public float FracturePermeability { get; set; } // m² (effective)
    public int FractureVoxelCount { get; set; }

    // ========== COUPLED RESULTS ==========
    public float[,,] EffectiveStressXX { get; set; } // Pa (σ - αP)
    public float[,,] EffectiveStressYY { get; set; }
    public float[,,] EffectiveStressZZ { get; set; }
    public float PeakInjectionPressure { get; set; } // MPa
    public float BreakdownPressure { get; set; } // MPa (when first fracture forms)
    public float PropagationPressure { get; set; } // MPa (sustained fracture growth)

    // ========== TIME SERIES DATA ==========
    public List<float> TimePoints { get; set; } = new();
    public List<float> InjectionPressureHistory { get; set; } = new();
    public List<float> FlowRateHistory { get; set; } = new();
    public List<float> FractureVolumeHistory { get; set; } = new();
    public List<float> EnergyExtractionHistory { get; set; } = new(); // MW
}

public class FractureSegment
{
    public Vector3 Start { get; set; }
    public Vector3 End { get; set; }
    public float Aperture { get; set; } // m
    public float Permeability { get; set; } // m²
    public float Pressure { get; set; } // Pa
    public float Temperature { get; set; } // °C
    public bool IsConnectedToInjection { get; set; }
}