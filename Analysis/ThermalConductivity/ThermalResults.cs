// GeoscientistToolkit/Analysis/ThermalConductivity/ThermalResults.cs

using GeoscientistToolkit.Data.Mesh3D;

namespace GeoscientistToolkit.Analysis.ThermalConductivity;

/// <summary>
/// Stores all outputs from a thermal conductivity simulation.
/// </summary>
public class ThermalResults
{
    public ThermalResults(ThermalOptions options)
    {
        Options = options;
    }

    /// <summary>
    /// A copy of the options that were used to generate these results.
    /// </summary>
    public ThermalOptions Options { get; }

    /// <summary>
    /// The final 3D steady-state temperature field, in Kelvin. Dimensions are [Width, Height, Depth].
    /// </summary>
    public float[,,] TemperatureField { get; set; }

    /// <summary>
    /// A dictionary containing 2D slices of the temperature field, keyed by direction ('X', 'Y', 'Z') and slice index.
    /// </summary>
    public Dictionary<(char, int), float[,]> TemperatureSlices { get; set; } = new();

    /// <summary>
    /// The calculated effective thermal conductivity of the entire volume, in W/(m·K).
    /// </summary>
    public double EffectiveConductivity { get; set; }

    /// <summary>
    /// Material conductivities used in the simulation, keyed by material ID
    /// </summary>
    public Dictionary<byte, double> MaterialConductivities { get; set; } = new();

    /// <summary>
    /// A dictionary of results from various analytical models for comparison, keyed by model name.
    /// </summary>
    public Dictionary<string, double> AnalyticalEstimates { get; set; } = new();

    /// <summary>
    /// A list of generated 3D mesh datasets representing temperature isosurfaces.
    /// </summary>
    public List<Mesh3DDataset> IsosurfaceMeshes { get; set; } = new();

    /// <summary>
    /// The total time taken for the simulation to complete.
    /// </summary>
    public TimeSpan ComputationTime { get; set; }

    /// <summary>
    /// The number of iterations the solver performed before stopping.
    /// </summary>
    public int IterationsPerformed { get; set; }

    /// <summary>
    /// The final maximum error (change in temperature) at the last iteration.
    /// </summary>
    public double FinalError { get; set; }
}