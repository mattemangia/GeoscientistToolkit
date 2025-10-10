// GeoscientistToolkit/Analysis/NMR/NMRResults.cs

using System.Numerics;

namespace GeoscientistToolkit.Analysis.NMR;

/// <summary>
///     Contains the results from an NMR simulation including decay curves and T2 distribution.
/// </summary>
public class NMRResults
{
    public NMRResults(int timeSteps)
    {
        TimePoints = new double[timeSteps];
        Magnetization = new double[timeSteps];
    }

    // Raw decay data
    public double[] TimePoints { get; set; }
    public double[] Magnetization { get; set; }

    // T2 distribution
    public double[] T2Values { get; set; }
    public double[] T2Amplitudes { get; set; }
    public double[] T2Histogram { get; set; }
    public double[] T2HistogramBins { get; set; }

    // T1 distribution (for 2D T1-T2 maps)
    public double[] T1Histogram { get; set; }
    public double[] T1HistogramBins { get; set; }

    // 2D T1-T2 correlation map
    public double[,] T1T2Map { get; set; } // [T1_index, T2_index]
    public bool HasT1T2Data { get; set; }

    // Pore size distribution (derived from T2)
    public double[] PoreSizes { get; set; }
    public double[] PoreSizeDistribution { get; set; }

    // Statistics
    public double MeanT2 { get; set; }
    public double GeometricMeanT2 { get; set; }
    public double T2PeakValue { get; set; }
    public double TotalPorosity { get; set; }

    // Simulation parameters
    public int NumberOfWalkers { get; set; }
    public int TotalSteps { get; set; }
    public double TimeStep { get; set; }
    public string PoreMaterial { get; set; }
    public Dictionary<string, double> MaterialRelaxivities { get; set; }

    // Computation info
    public TimeSpan ComputationTime { get; set; }
    public string ComputationMethod { get; set; } // "SIMD" or "OpenCL"
}

/// <summary>
///     Configuration for NMR simulation.
/// </summary>
public class NMRSimulationConfig
{
    public int NumberOfWalkers { get; set; } = 10000;
    public int NumberOfSteps { get; set; } = 1000;
    public double TimeStepMs { get; set; } = 0.01; // milliseconds
    public double DiffusionCoefficient { get; set; } = 2.0e-9; // m^2/s for water at room temp
    public double VoxelSize { get; set; } = 1e-6; // meters (1 micron)
    public byte PoreMaterialID { get; set; } = 1;

    // Material relaxivities (1/s per meter of surface)
    public Dictionary<byte, MaterialRelaxivityConfig> MaterialRelaxivities { get; set; } = new();

    // T2 spectrum computation
    public int T2BinCount { get; set; } = 64;
    public double T2MinMs { get; set; } = 0.1;
    public double T2MaxMs { get; set; } = 10000;

    // T1-T2 2D NMR settings
    public bool ComputeT1T2Map { get; set; } = false;
    public int T1BinCount { get; set; } = 64;
    public double T1MinMs { get; set; } = 1.0;
    public double T1MaxMs { get; set; } = 10000;
    public double T1T2Ratio { get; set; } = 1.5; // Typical T1/T2 ratio for porous media

    // Random walk parameters
    public int RandomSeed { get; set; } = 42;
    public bool UseOpenCL { get; set; } = false;
}

public class MaterialRelaxivityConfig
{
    public string MaterialName { get; set; }
    public double SurfaceRelaxivity { get; set; } = 10.0; // micrometers/second
    public Vector4 Color { get; set; }
}