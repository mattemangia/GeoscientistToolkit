// GeoscientistToolkit/Analysis/AcousticSimulation/IAcousticKernel.cs

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

/// <summary>
///     Interface for acoustic wave propagation kernels (CPU or GPU).
/// </summary>
public interface IAcousticKernel : IDisposable
{
    /// <summary>
    ///     Initializes the kernel with volume dimensions.
    /// </summary>
    void Initialize(int width, int height, int depth);
    
    /// <summary>
    ///     Updates the wave field for one time step using elastic wave equations.
    /// </summary>
    /// <param name="vx">Velocity field X component</param>
    /// <param name="vy">Velocity field Y component</param>
    /// <param name="vz">Velocity field Z component</param>
    /// <param name="sxx">Normal stress XX component</param>
    /// <param name="syy">Normal stress YY component</param>
    /// <param name="szz">Normal stress ZZ component</param>
    /// <param name="sxy">Shear stress XY component</param>
    /// <param name="sxz">Shear stress XZ component</param>
    /// <param name="syz">Shear stress YZ component</param>
    /// <param name="youngsModulus">Young's modulus field (Pa)</param>
    /// <param name="poissonRatio">Poisson's ratio field</param>
    /// <param name="density">Density field (kg/m³)</param>
    /// <param name="dt">Time step (seconds)</param>
    /// <param name="dx">Spatial step (meters)</param>
    /// <param name="dampingFactor">Artificial damping factor (0-1)</param>
    void UpdateWaveField(
        float[,,] vx, float[,,] vy, float[,,] vz,
        float[,,] sxx, float[,,] syy, float[,,] szz,
        float[,,] sxy, float[,,] sxz, float[,,] syz,
        float[,,] youngsModulus, float[,,] poissonRatio, float[,,] density,
        float dt, float dx, float dampingFactor);
}