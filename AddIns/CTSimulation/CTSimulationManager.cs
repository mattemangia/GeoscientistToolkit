// GeoscientistToolkit/AddIns/CtSimulation/CtSimulationManager.cs

using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.AddIns.CtSimulation;

/// <summary>
///     Manages CT simulation add-ins and their integration with viewers
/// </summary>
public class CtSimulationManager
{
    private static CtSimulationManager _instance;
    private readonly Dictionary<string, ICtSimulationProcessor> _activeProcessors = new();
    private readonly Dictionary<string, SimulationResult> _results = new();

    private readonly Dictionary<string, ICtSimulationAddIn> _simulations = new();

    private CtSimulationManager()
    {
        // Subscribe to add-in manager events
        AddInManager.Instance.AddInLoaded += OnAddInLoaded;
        AddInManager.Instance.AddInUnloaded += OnAddInUnloaded;
    }

    public static CtSimulationManager Instance => _instance ??= new CtSimulationManager();

    public event Action<string, SimulationResult> ResultAvailable;
    public event Action<string, float> ProgressUpdated;

    private void OnAddInLoaded(IAddIn addIn)
    {
        if (addIn is ICtSimulationAddIn ctSimulation)
        {
            _simulations[addIn.Id] = ctSimulation;
            Logger.Log($"Loaded CT simulation add-in: {addIn.Name}");
        }
    }

    private void OnAddInUnloaded(IAddIn addIn)
    {
        if (_simulations.ContainsKey(addIn.Id))
        {
            // Clean up any active processors
            if (_activeProcessors.TryGetValue(addIn.Id, out var processor))
            {
                processor.Dispose();
                _activeProcessors.Remove(addIn.Id);
            }

            _simulations.Remove(addIn.Id);
            _results.Remove(addIn.Id);
        }
    }

    /// <summary>
    ///     Gets available simulations for a dataset
    /// </summary>
    public IEnumerable<ICtSimulationAddIn> GetAvailableSimulations(CtImageStackDataset dataset)
    {
        return _simulations.Values.Where(sim =>
        {
            var caps = sim.GetCapabilities();
            // Check if simulation is compatible with dataset
            return dataset.VolumeData != null || !caps.HasFlag(SimulationCapabilities.VolumeModification);
        });
    }

    /// <summary>
    ///     Starts a simulation
    /// </summary>
    public async Task<SimulationResult> RunSimulationAsync(
        string simulationId,
        CtImageStackDataset dataset,
        SimulationParameters parameters)
    {
        if (!_simulations.TryGetValue(simulationId, out var simulation))
            throw new InvalidOperationException($"Simulation {simulationId} not found");

        // Create processor
        var processor = simulation.CreateProcessor(dataset);
        _activeProcessors[simulationId] = processor;

        // Initialize with data
        processor.Initialize(dataset.VolumeData, dataset.LabelData);

        // Subscribe to events
        processor.ProgressChanged += progress => { ProgressUpdated?.Invoke(simulationId, progress); };

        processor.IntermediateResultAvailable += result =>
        {
            _results[simulationId] = result;
            ResultAvailable?.Invoke(simulationId, result);
        };

        try
        {
            // Run simulation
            var result = await processor.RunAsync(parameters);
            _results[simulationId] = result;
            ResultAvailable?.Invoke(simulationId, result);
            return result;
        }
        finally
        {
            _activeProcessors.Remove(simulationId);
            processor.Dispose();
        }
    }

    /// <summary>
    ///     Gets the latest result for a simulation
    /// </summary>
    public SimulationResult GetResult(string simulationId)
    {
        return _results.TryGetValue(simulationId, out var result) ? result : null;
    }

    /// <summary>
    ///     Draws the simulation control panel
    /// </summary>
    public void DrawControlPanel()
    {
        if (ImGui.Begin("Simulations"))
            foreach (var sim in _simulations.Values)
                if (ImGui.CollapsingHeader(sim.Name))
                {
                    ImGui.Text($"Version: {sim.Version}");
                    ImGui.Text($"Author: {sim.Author}");
                    ImGui.TextWrapped(sim.Description);

                    var caps = sim.GetCapabilities();
                    ImGui.Text("Capabilities:");
                    ImGui.Indent();
                    foreach (var cap in Enum.GetValues<SimulationCapabilities>())
                        if (cap != SimulationCapabilities.None && caps.HasFlag(cap))
                            ImGui.BulletText(cap.ToString());

                    ImGui.Unindent();

                    if (_activeProcessors.TryGetValue(sim.Id, out var processor))
                    {
                        ImGui.Separator();
                        ImGui.Text($"Status: {processor.State}");
                        ImGui.ProgressBar(processor.Progress, new Vector2(-1, 0));

                        if (processor.State == SimulationState.Running)
                        {
                            if (ImGui.Button("Pause"))
                                processor.Pause();
                        }
                        else if (processor.State == SimulationState.Paused)
                        {
                            if (ImGui.Button("Resume"))
                                processor.Resume();
                        }
                    }
                }

        ImGui.End();
    }
}