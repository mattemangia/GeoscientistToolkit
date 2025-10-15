// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalResults.cs

using System.Numerics;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public partial class GeomechanicalResults
{
    // Stress field (Pa)
    public float[,,] StressXX { get; set; }
    public float[,,] StressYY { get; set; }
    public float[,,] StressZZ { get; set; }
    public float[,,] StressXY { get; set; }
    public float[,,] StressXZ { get; set; }
    public float[,,] StressYZ { get; set; }

    // Strain field (dimensionless)
    public float[,,] StrainXX { get; set; }
    public float[,,] StrainYY { get; set; }
    public float[,,] StrainZZ { get; set; }
    public float[,,] StrainXY { get; set; }
    public float[,,] StrainXZ { get; set; }
    public float[,,] StrainYZ { get; set; }

    // Principal stresses (Pa)
    public float[,,] Sigma1 { get; set; }
    public float[,,] Sigma2 { get; set; }
    public float[,,] Sigma3 { get; set; }

    // Failure indicators
    public float[,,] FailureIndex { get; set; } // 0-1, 1 = failure
    public byte[,,] DamageField { get; set; } // 0-255, 255 = fully damaged
    public bool[,,] FractureField { get; set; } // True if fractured

    // Mohr circle data
    public List<MohrCircleData> MohrCircles { get; set; } = new();

    // Statistical data
    public float MeanStress { get; set; }
    public float VonMisesStress_Mean { get; set; }
    public float VonMisesStress_Max { get; set; }
    public float MaxShearStress { get; set; }
    public float VolumetricStrain { get; set; }
    public float FailedVoxelPercentage { get; set; }
    public int TotalVoxels { get; set; }
    public int FailedVoxels { get; set; }

    // Computation info
    public TimeSpan ComputationTime { get; set; }
    public int IterationsPerformed { get; set; }
    public float FinalError { get; set; }
    public bool Converged { get; set; }

    // Context for visualization
    public byte[,,] MaterialLabels { get; set; }
    public GeomechanicalParameters Parameters { get; set; }
}

public class MohrCircleData
{
    public string Location { get; set; }
    public Vector3 Position { get; set; }
    public float Sigma1 { get; set; }
    public float Sigma2 { get; set; }
    public float Sigma3 { get; set; }
    public float MaxShearStress { get; set; }
    public float NormalStressAtFailure { get; set; }
    public float ShearStressAtFailure { get; set; }
    public float FailureAngle { get; set; } // degrees from σ1
    public bool HasFailed { get; set; }
}