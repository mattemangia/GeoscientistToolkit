// GeoscientistToolkit/Analysis/ThermalConductivity/ThermalOptions.cs

using GeoscientistToolkit.Data.CtImageStack;

namespace GeoscientistToolkit.Analysis.ThermalConductivity;

/// <summary>
///     Defines the direction of the primary heat flux for the simulation.
/// </summary>
public enum HeatFlowDirection
{
    X,
    Y,
    Z
}

/// <summary>
///     Specifies the numerical solver backend to use for the simulation.
/// </summary>
public enum SolverBackend
{
    CSharp_Parallel,
    CSharp_SIMD_AVX2,
    OpenCL
}

/// <summary>
///     Holds all user-configurable settings for a thermal conductivity simulation.
/// </summary>
public class ThermalOptions
{
    /// <summary>
    ///     The input dataset containing the segmented material volume.
    /// </summary>
    public CtImageStackDataset Dataset { get; set; }

    /// <summary>
    ///     A dictionary mapping material IDs (from the dataset) to their thermal conductivity in W/(m·K).
    /// </summary>
    public Dictionary<byte, double> MaterialConductivities { get; set; } = new();

    /// <summary>
    ///     The temperature applied to the "hot" boundary plane, in Kelvin.
    /// </summary>
    public double TemperatureHot { get; set; } = 373.15; // 100 °C

    /// <summary>
    ///     The temperature applied to the "cold" boundary plane, in Kelvin.
    /// </summary>
    public double TemperatureCold { get; set; } = 273.15; // 0 °C

    /// <summary>
    ///     The principal axis along which the heat flow is simulated.
    /// </summary>
    public HeatFlowDirection HeatFlowDirection { get; set; } = HeatFlowDirection.Z;

    /// <summary>
    ///     The numerical solver backend to use for the computation.
    /// </summary>
    public SolverBackend SolverBackend { get; set; } = SolverBackend.CSharp_Parallel;

    /// <summary>
    ///     The convergence criterion for the iterative solver. The simulation stops when the maximum
    ///     change in temperature between iterations falls below this value.
    /// </summary>
    public double ConvergenceTolerance { get; set; } = 1e-6;

    /// <summary>
    ///     The maximum number of iterations to perform before stopping the simulation, even if
    ///     the convergence tolerance has not been met.
    /// </summary>
    public int MaxIterations { get; set; } = 20000;

    /// <summary>
    //   Successive Over-Relaxation (SOR) factor. Values between 1.0 and 2.0 can accelerate convergence.
    //  1.0 = Gauss-Seidel method.
    /// </summary>
    public double SorFactor { get; set; } = 1.8;

    /// <summary>
    ///     A list of temperature values (in Kelvin) for which to generate isosurfaces.
    /// </summary>
    public List<double> IsosurfaceValues { get; set; } = new();
}