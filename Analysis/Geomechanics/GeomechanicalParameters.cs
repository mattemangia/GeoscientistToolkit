// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalParameters.cs

using System.Numerics;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public enum LoadingMode
{
    Uniaxial, // σ1 only
    Biaxial, // σ1, σ2
    Triaxial, // σ1, σ2, σ3
    Custom // User-defined stress tensor
}

public enum FailureCriterion
{
    MohrCoulomb,
    DruckerPrager,
    HoekBrown,
    Griffith
}

public partial class GeomechanicalParameters
{
    // Dataset info
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }
    public float PixelSize { get; set; }
    public BoundingBox SimulationExtent { get; set; }
    public HashSet<byte> SelectedMaterialIDs { get; set; } = new();

    // Material properties (base values)
    public float YoungModulus { get; set; } = 30000f; // MPa
    public float PoissonRatio { get; set; } = 0.25f;
    public float Cohesion { get; set; } = 10f; // MPa
    public float FrictionAngle { get; set; } = 30f; // degrees
    public float TensileStrength { get; set; } = 5f; // MPa
    public float Density { get; set; } = 2700f; // kg/m³

    // Loading conditions
    public LoadingMode LoadingMode { get; set; } = LoadingMode.Triaxial;
    public float Sigma1 { get; set; } = 100f; // MPa (max principal stress)
    public float Sigma2 { get; set; } = 50f; // MPa (intermediate)
    public float Sigma3 { get; set; } = 20f; // MPa (min principal/confining)
    public Vector3 Sigma1Direction { get; set; } = new(0, 0, 1); // Z-axis default

    public bool EnablePlasticity { get; set; } = false;

    // Body forces (gravity/custom acceleration)
    public bool ApplyGravity { get; set; } = false;
    public Vector3 GravityAcceleration { get; set; } = new(0, 0, -9.81f); // m/s²

    // Pore pressure effects
    public bool UsePorePressure { get; set; }
    public float PorePressure { get; set; } = 10f; // MPa
    public float BiotCoefficient { get; set; } = 0.8f;

    // Failure criterion
    public FailureCriterion FailureCriterion { get; set; } = FailureCriterion.MohrCoulomb;
    public float DilationAngle { get; set; } = 10f; // degrees

    // Hoek-Brown parameters (if used)
    public float HoekBrown_mi { get; set; } = 10f;
    public float HoekBrown_mb { get; set; } = 1.5f;
    public float HoekBrown_s { get; set; } = 0.004f;
    public float HoekBrown_a { get; set; } = 0.5f;

    // Computational settings
    public bool UseGPU { get; set; } = true;
    public int MaxIterations { get; set; } = 1000;
    public float Tolerance { get; set; } = 1e-4f;

    public bool EnableDamageEvolution { get; set; }
    public float DamageThreshold { get; set; } = 0.001f; // Strain at which damage initiates
    public float DamageCriticalStrain { get; set; } = 0.01f; // Strain at complete failure
    public float DamageEvolutionRate { get; set; } = 100f; // Controls damage growth speed
    public DamageModel DamageModel { get; set; } = DamageModel.Exponential;
    public bool ApplyDamageToStiffness { get; set; } = true; // Degrade material stiffness

    public float PlasticHardeningModulus { get; set; } = 1000f; // MPa

    // Memory management for huge datasets
    public bool EnableOffloading { get; set; } = false;
    public string OffloadDirectory { get; set; } = "";

    // Integration options
    public string PnmDatasetPath { get; set; }
    public string PermeabilityCsvPath { get; set; }
    public string AcousticDatasetPath { get; set; }

    // Real-time visualization
    public bool EnableRealTimeVisualization { get; set; } = true;
    public float VisualizationUpdateInterval { get; set; } = 0.5f; // seconds
}

public class BoundingBox
{
    public BoundingBox(int minX, int minY, int minZ, int width, int height, int depth)
    {
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        Width = width;
        Height = height;
        Depth = depth;
    }

    public int MinX { get; set; }
    public int MinY { get; set; }
    public int MinZ { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }

    public Vector3 Min => new(MinX, MinY, MinZ);
    public Vector3 Max => new(MinX + Width, MinY + Height, MinZ + Depth);
}
