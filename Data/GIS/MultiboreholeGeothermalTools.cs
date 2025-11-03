// GeoscientistToolkit/Analysis/Geothermal/MultiBoreholeGeothermalTools.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Linq;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// Tools for running geothermal simulations on multiple boreholes and creating subsurface models
/// </summary>
public class MultiBoreholeGeothermalTools : IDatasetTools
{
private List<BoreholeDataset> _selectedBoreholes = new();
private Dictionary<string, GeothermalSimulationResults> _simulationResults = new();
private Dictionary<string, bool> _boreholeSelection = new();
private bool _isRunningSimulations = false;
private float _simulationProgress = 0.0f;
private string _simulationStatus = "";
private CancellationTokenSource _cancellationTokenSource;

// Subsurface model parameters
private int _gridResolutionX = 30;
private int _gridResolutionY = 30;
private int _gridResolutionZ = 50;
private float _interpolationRadius = 500.0f;
private int _interpolationMethod = 0; // IDW
private GISDataset _selectedHeightmap = null;

public void Draw(Dataset dataset)
{
    if (dataset is not DatasetGroup group)
    {
        ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "This tool requires a DatasetGroup of boreholes.");
        return;
    }

    var boreholesInGroup = group.Datasets.OfType<BoreholeDataset>().ToList();
    
    ImGui.TextColored(new Vector4(0.4f, 0.7f, 1.0f, 1.0f), "Multi-Borehole Geothermal Analysis");
    ImGui.Separator();

    if (ImGui.CollapsingHeader("1. Select Boreholes", ImGuiTreeNodeFlags.DefaultOpen))
    {
        DrawBoreholeSelection(boreholesInGroup);
    }

    ImGui.Separator();

    if (ImGui.CollapsingHeader("2. Run Simulations"))
    {
        DrawSimulationSection();
    }

    ImGui.Separator();

    if (ImGui.CollapsingHeader("3. Create Subsurface Model"))
    {
        DrawSubsurfaceModelSection();
    }

    ImGui.Separator();

    if (ImGui.CollapsingHeader("Results"))
    {
        DrawResultsSection();
    }
}

private void DrawBoreholeSelection(List<BoreholeDataset> allBoreholes)
{
    if (allBoreholes.Count == 0)
    {
        ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "No boreholes found in this group.");
        return;
    }

    ImGui.Text($"Found {allBoreholes.Count} borehole(s) in this group:");

    foreach (var borehole in allBoreholes)
    {
        if (!_boreholeSelection.ContainsKey(borehole.WellName))
        {
            _boreholeSelection[borehole.WellName] = false;
        }

        bool isSelected = _boreholeSelection[borehole.WellName];
        if (ImGui.Checkbox($"{borehole.WellName}##select", ref isSelected))
        {
            _boreholeSelection[borehole.WellName] = isSelected;
            UpdateSelectedBoreholes(allBoreholes);
        }

        ImGui.SameLine();
        ImGui.Text($"- Depth: {borehole.TotalDepth:F0}m, Elevation: {borehole.Elevation:F1}m");
    }

    ImGui.Separator();
    if (ImGui.Button("Select All"))
    {
        foreach (var key in _boreholeSelection.Keys.ToList())
        {
            _boreholeSelection[key] = true;
        }
        UpdateSelectedBoreholes(allBoreholes);
    }

    ImGui.SameLine();
    if (ImGui.Button("Clear All"))
    {
        foreach (var key in _boreholeSelection.Keys.ToList())
        {
            _boreholeSelection[key] = false;
        }
        UpdateSelectedBoreholes(allBoreholes);
    }

    ImGui.Text($"Selected: {_selectedBoreholes.Count} borehole(s)");
}

private void UpdateSelectedBoreholes(List<BoreholeDataset> allBoreholes)
{
    _selectedBoreholes = allBoreholes
        .Where(b => _boreholeSelection.ContainsKey(b.WellName) && _boreholeSelection[b.WellName])
        .ToList();
}

private void DrawSimulationSection()
{
    if (_selectedBoreholes.Count == 0)
    {
        ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "Please select boreholes first.");
        return;
    }

    ImGui.Text($"Ready to simulate {_selectedBoreholes.Count} borehole(s)");

    if (_isRunningSimulations)
    {
        ImGui.ProgressBar(_simulationProgress, new Vector2(-1, 0), _simulationStatus);

        if (ImGui.Button("Cancel"))
        {
            _cancellationTokenSource?.Cancel();
        }
    }
    else
    {
        if (ImGui.Button("Run Simulations on All Selected Boreholes"))
        {
            StartSimulations();
        }
    }

    if (_simulationResults.Count > 0)
    {
        ImGui.Separator();
        ImGui.Text($"Completed simulations: {_simulationResults.Count}");

        foreach (var result in _simulationResults)
        {
            ImGui.BulletText($"{result.Key}: {(result.Value != null ? "Success" : "Failed")}");
        }
    }
}

private void DrawSubsurfaceModelSection()
{
    if (_simulationResults.Count == 0)
    {
        ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "Please run simulations first.");
        return;
    }

    ImGui.Text("Grid Resolution:");
    ImGui.InputInt("X Resolution##gridx", ref _gridResolutionX);
    _gridResolutionX = Math.Clamp(_gridResolutionX, 10, 100);

    ImGui.InputInt("Y Resolution##gridy", ref _gridResolutionY);
    _gridResolutionY = Math.Clamp(_gridResolutionY, 10, 100);

    ImGui.InputInt("Z Resolution##gridz", ref _gridResolutionZ);
    _gridResolutionZ = Math.Clamp(_gridResolutionZ, 10, 200);

    ImGui.Separator();
    ImGui.Text("Interpolation Settings:");
    ImGui.InputFloat("Interpolation Radius (m)", ref _interpolationRadius);
    _interpolationRadius = Math.Max(1.0f, _interpolationRadius);

    string[] methods = { "Inverse Distance Weighted", "Nearest Neighbor", "Kriging", "Natural Neighbor" };
    ImGui.Combo("Method", ref _interpolationMethod, methods, methods.Length);

    ImGui.Separator();
    ImGui.Text("Optional Heightmap:");

    var allGIS = ProjectManager.Instance.LoadedDatasets
        .OfType<GISDataset>()
        .Where(g => g.Layers.Any(l => l is GISRasterLayer))
        .ToList();

    if (allGIS.Count > 0)
    {
        string[] gisNames = allGIS.Select(g => g.Name).ToArray();
        int selectedIdx = _selectedHeightmap != null ? allGIS.IndexOf(_selectedHeightmap) : -1;

        if (ImGui.Combo("Heightmap Dataset", ref selectedIdx, gisNames, gisNames.Length))
        {
            _selectedHeightmap = selectedIdx >= 0 ? allGIS[selectedIdx] : null;
        }
    }
    else
    {
        ImGui.TextDisabled("No GIS raster datasets available");
    }

    ImGui.Separator();

    if (ImGui.Button("Create 3D Subsurface Geothermal Model"))
    {
        CreateSubsurfaceModel();
    }
}

private void DrawResultsSection()
{
    if (_simulationResults.Count == 0)
    {
        ImGui.Text("No results yet.");
        return;
    }

    foreach (var kvp in _simulationResults)
    {
        if (ImGui.TreeNode(kvp.Key))
        {
            var result = kvp.Value;

            if (result != null)
            {
                var energyKWh = result.TotalExtractedEnergy / 3.6e6; // Joules to kWh
                var avgCop = result.CoefficientOfPerformance.Any() ? result.CoefficientOfPerformance.Average(c => c.cop) : 0;

                ImGui.Text($"Total Energy Output: {energyKWh:F0} kWh");
                ImGui.Text($"Average COP: {avgCop:F2}");
                ImGui.Text($"Average Heat Output: {result.AverageHeatExtractionRate / 1000:F1} kW");

                if (result.FinalTemperatureField != null)
                {
                    var temps = result.FinalTemperatureField.Cast<float>().Select(t => t - 273.15f); // K to C
                    var avgTemp = temps.Average();
                    var maxTemp = temps.Max();
                    var minTemp = temps.Min();

                    ImGui.Text($"Temperature Range: {minTemp:F1}°C - {maxTemp:F1}°C (avg: {avgTemp:F1}°C)");
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Simulation failed");
            }

            ImGui.TreePop();
        }
    }
}

private void StartSimulations()
{
    _isRunningSimulations = true;
    _simulationProgress = 0.0f;
    _simulationResults.Clear();
    _cancellationTokenSource = new CancellationTokenSource();

    Logger.Log($"Starting simulations on {_selectedBoreholes.Count} boreholes...");

    // Run simulations in background
    Task.Run(() =>
    {
        try
        {
            _simulationResults = SubsurfaceGeothermalTools.RunSimulationsOnBoreholes(
                _selectedBoreholes,
                (status, progress) =>
                {
                    _simulationStatus = status;
                    _simulationProgress = progress;
                }
            );

            _simulationStatus = "Completed";
            _simulationProgress = 1.0f;
            Logger.Log("All simulations completed");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Simulation error: {ex.Message}");
            _simulationStatus = $"Error: {ex.Message}";
        }
        finally
        {
            _isRunningSimulations = false;
        }
    }, _cancellationTokenSource.Token);
}

private void CreateSubsurfaceModel()
{
    try
    {
        Logger.Log("Creating 3D subsurface geothermal model...");

        // Get heightmap layer if selected
        GISRasterLayer heightmapLayer = null;
        if (_selectedHeightmap != null)
        {
            heightmapLayer = _selectedHeightmap.Layers
                .OfType<GISRasterLayer>()
                .FirstOrDefault();
        }

        // Create the subsurface model
        var subsurfaceModel = SubsurfaceGeothermalTools.CreateSubsurfaceModel(
            _selectedBoreholes,
            _simulationResults,
            heightmapLayer,
            _gridResolutionX,
            _gridResolutionY,
            _gridResolutionZ
        );

        // Set interpolation parameters
        subsurfaceModel.InterpolationRadius = _interpolationRadius;
        subsurfaceModel.Method = (InterpolationMethod)_interpolationMethod;

        // Add to project
        ProjectManager.Instance.AddDataset(subsurfaceModel);

        Logger.Log($"Subsurface model created with {subsurfaceModel.VoxelGrid.Count} voxels");
        Logger.Log($"Model bounds: {subsurfaceModel.GridOrigin} to {subsurfaceModel.GridOrigin + subsurfaceModel.GridSize}");
    }
    catch (Exception ex)
    {
        Logger.LogError($"Failed to create subsurface model: {ex.Message}");
    }
}

}