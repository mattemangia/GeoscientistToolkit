using GeoscientistToolkit.Analysis.AcousticSimulation;

namespace GeoscientistToolkit.Api;

/// <summary>
///     Provides access to 3D acoustic velocity simulation workflows.
/// </summary>
public class AcousticVolumeSimulationApi
{
    /// <summary>
    ///     Runs a 3D acoustic simulation using voxel labels and density volumes.
    /// </summary>
    /// <param name="labels">Material label volume.</param>
    /// <param name="density">Density volume (kg/m³).</param>
    /// <param name="parameters">Simulation parameters.</param>
    /// <param name="youngsModulus">Optional per-voxel Young's modulus volume (MPa).</param>
    /// <param name="poissonRatio">Optional per-voxel Poisson ratio volume.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SimulationResults> RunVelocitySimulationAsync(
        byte[,,] labels,
        float[,,] density,
        SimulationParameters parameters,
        float[,,] youngsModulus = null,
        float[,,] poissonRatio = null,
        CancellationToken cancellationToken = default)
    {
        if (labels == null) throw new ArgumentNullException(nameof(labels));
        if (density == null) throw new ArgumentNullException(nameof(density));
        if (parameters == null) throw new ArgumentNullException(nameof(parameters));
        if (labels.GetLength(0) != density.GetLength(0)
            || labels.GetLength(1) != density.GetLength(1)
            || labels.GetLength(2) != density.GetLength(2))
            throw new ArgumentException("Label and density volumes must have identical dimensions.");

        if ((youngsModulus != null && poissonRatio == null) || (youngsModulus == null && poissonRatio != null))
            throw new ArgumentException("Young's modulus and Poisson ratio volumes must be provided together.");

        if (youngsModulus != null && (labels.GetLength(0) != youngsModulus.GetLength(0)
                                      || labels.GetLength(1) != youngsModulus.GetLength(1)
                                      || labels.GetLength(2) != youngsModulus.GetLength(2)))
            throw new ArgumentException("Material property volumes must match the label dimensions.");

        using var simulator = new ChunkedAcousticSimulator(parameters);

        if (youngsModulus != null && poissonRatio != null)
        {
            simulator.SetPerVoxelMaterialProperties(youngsModulus, poissonRatio);
        }

        return await simulator.RunAsync(labels, density, cancellationToken);
    }
}
