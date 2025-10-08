// GeoscientistToolkit/AddIns/CtSimulation/ICtSimulationAddIn.cs

using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using Veldrid;

namespace GeoscientistToolkit.AddIns.CtSimulation;

/// <summary>
///     Extended interface for CT simulation add-ins
/// </summary>
public interface ICtSimulationAddIn : IAddIn
{
    /// <summary>
    ///     Gets simulation capabilities
    /// </summary>
    SimulationCapabilities GetCapabilities();

    /// <summary>
    ///     Creates a simulation processor for the given dataset
    /// </summary>
    ICtSimulationProcessor CreateProcessor(CtImageStackDataset dataset);

    /// <summary>
    ///     Gets custom UI panels for the simulation
    /// </summary>
    IEnumerable<ISimulationPanel> GetPanels();
}

/// <summary>
///     Defines what the simulation can do
/// </summary>
[Flags]
public enum SimulationCapabilities
{
    None = 0,
    VolumeModification = 1 << 0,
    MaterialAnalysis = 1 << 1,
    FlowSimulation = 1 << 2,
    StructuralAnalysis = 1 << 3,
    Segmentation = 1 << 4,
    Registration = 1 << 5,
    Reconstruction = 1 << 6,
    RealTime = 1 << 7,
    RequiresGPU = 1 << 8
}

/// <summary>
///     Interface for simulation processors
/// </summary>
public interface ICtSimulationProcessor : IDisposable
{
    string Name { get; }
    SimulationState State { get; }
    float Progress { get; }

    /// <summary>
    ///     Initializes the processor with volume data
    /// </summary>
    void Initialize(ChunkedVolume volumeData, ChunkedLabelVolume labelData);

    /// <summary>
    ///     Gets default parameters for this simulation
    /// </summary>
    SimulationParameters CreateDefaultParameters();

    /// <summary>
    ///     Runs the simulation
    /// </summary>
    Task<SimulationResult> RunAsync(SimulationParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets intermediate results for real-time visualization
    /// </summary>
    SimulationResult GetIntermediateResult();

    /// <summary>
    ///     Pauses the simulation
    /// </summary>
    void Pause();

    /// <summary>
    ///     Resumes the simulation
    /// </summary>
    void Resume();

    /// <summary>
    ///     Event raised when progress changes
    /// </summary>
    event Action<float> ProgressChanged;

    /// <summary>
    ///     Event raised when intermediate results are available
    /// </summary>
    event Action<SimulationResult> IntermediateResultAvailable;
}

public enum SimulationState
{
    Idle,
    Initializing,
    Running,
    Paused,
    Completed,
    Failed
}

/// <summary>
///     Base class for simulation parameters
/// </summary>
public abstract class SimulationParameters
{
    public Vector3 ROIStart { get; set; }
    public Vector3 ROIEnd { get; set; }
    public bool UseFullVolume { get; set; } = true;
    public int MaxIterations { get; set; } = 1000;
    public float Tolerance { get; set; } = 1e-6f;
    public bool EnableGPU { get; set; } = true;
}

/// <summary>
///     Base class for simulation results
/// </summary>
public abstract class SimulationResult
{
    public DateTime Timestamp { get; set; }
    public TimeSpan ComputationTime { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    ///     Gets a 3D texture for visualization (if applicable)
    /// </summary>
    public virtual Texture Get3DTexture(ResourceFactory factory)
    {
        return null;
    }

    /// <summary>
    ///     Gets scalar field data
    /// </summary>
    public virtual float[,,] GetScalarField()
    {
        return null;
    }

    /// <summary>
    ///     Gets vector field data
    /// </summary>
    public virtual Vector3[,,] GetVectorField()
    {
        return null;
    }

    /// <summary>
    ///     Gets modified label data
    /// </summary>
    public virtual byte[,,] GetLabelData()
    {
        return null;
    }

    /// <summary>
    ///     Exports results to file
    /// </summary>
    public abstract void Export(string filePath);
}

/// <summary>
///     Interface for custom UI panels
/// </summary>
public interface ISimulationPanel
{
    string Title { get; }
    void Draw();
    void SetProcessor(ICtSimulationProcessor processor);
}