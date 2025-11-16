// GeoscientistToolkit/Analysis/Geothermal/MultiBoreholeGeothermalTools.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
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

// Coupled simulation parameters
private bool _useCoupledSimulation = false;
private bool _enableRegionalFlow = true;
private bool _enableThermalInterference = true;
private float _regionalHydraulicConductivity = 1e-5f; // m/s
private float _aquiferThickness = 50.0f; // m
private float _aquiferPorosity = 0.25f;
private float _anisotropyRatio = 10.0f;
private float _doubletFlowRate = 15.0f; // kg/s
private float _injectionTemperature = 12.0f; // °C
private Dictionary<string, string> _doubletPairs = new(); // injection -> production
private MultiBoreholeSimulationResults _coupledResults = null;

// Export functionality
private SubsurfaceGISDataset _createdSubsurfaceModel = null;
private ImGuiExportFileDialog _exportDialog = null;
private bool _isExporting = false;
private float _exportProgress = 0.0f;
private string _exportStatus = "";
private List<float> _depthSlices = new List<float> { 500, 1000, 1500, 2000, 2500, 3000 };
private float _newDepthSlice = 1000.0f;

// Export options
private bool _exportTemperature = true;
private bool _exportThermalConductivity = true;
private bool _exportPorosity = true;
private bool _exportPermeability = true;
private bool _exportConfidence = true;
private bool _exportHeatFlowMaps = true;
private bool _showExportPreview = false;

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

    if (ImGui.CollapsingHeader("2. Coupled Simulation Options"))
    {
        DrawCoupledSimulationOptions();
    }

    ImGui.Separator();

    if (ImGui.CollapsingHeader("3. Configure Doublet Pairs"))
    {
        DrawDoubletConfiguration();
    }

    ImGui.Separator();

    if (ImGui.CollapsingHeader("4. Run Simulations"))
    {
        DrawSimulationSection();
    }

    ImGui.Separator();

    if (ImGui.CollapsingHeader("5. Create Subsurface Model"))
    {
        DrawSubsurfaceModelSection();
    }

    ImGui.Separator();

    if (ImGui.CollapsingHeader("6. Export Geothermal Maps"))
    {
        DrawExportSection();
    }

    ImGui.Separator();

    if (ImGui.CollapsingHeader("Results"))
    {
        DrawResultsSection();
    }

    // Draw export dialog if open
    DrawExportDialog();
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

private void DrawCoupledSimulationOptions()
{
    ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.6f, 1.0f), 
        "Enable multi-borehole coupled simulation with aquifer flow and thermal interference");
    
    ImGui.Checkbox("Use Coupled Simulation (Aquifer + Thermal Interference)", ref _useCoupledSimulation);
    
    if (_useCoupledSimulation)
    {
        ImGui.Indent();
        
        ImGui.Separator();
        ImGui.Text("Regional Aquifer Flow:");
        ImGui.Checkbox("Enable regional groundwater flow from topography", ref _enableRegionalFlow);
        
        if (_enableRegionalFlow)
        {
            ImGui.Indent();
            ImGui.InputFloat("Hydraulic conductivity (m/s)", ref _regionalHydraulicConductivity);
            _regionalHydraulicConductivity = Math.Max(1e-7f, _regionalHydraulicConductivity);
            
            ImGui.InputFloat("Aquifer thickness (m)", ref _aquiferThickness);
            _aquiferThickness = Math.Clamp(_aquiferThickness, 10.0f, 500.0f);
            
            ImGui.InputFloat("Aquifer porosity (fraction)", ref _aquiferPorosity);
            _aquiferPorosity = Math.Clamp(_aquiferPorosity, 0.05f, 0.45f);
            
            ImGui.InputFloat("Anisotropy ratio (Kh/Kv)", ref _anisotropyRatio);
            _anisotropyRatio = Math.Clamp(_anisotropyRatio, 1.0f, 100.0f);
            
            // Display flow direction info
            double flowSpeed = _regionalHydraulicConductivity * 0.01 * 86400; // m/day for 1% gradient
            ImGui.TextDisabled($"Typical flow velocity: ~{flowSpeed:F2} m/day");
            ImGui.Unindent();
        }
        
        ImGui.Separator();
        ImGui.Text("Thermal Interference:");
        ImGui.Checkbox("Enable thermal interference between boreholes", ref _enableThermalInterference);
        
        if (_enableThermalInterference)
        {
            ImGui.TextDisabled("Calculates thermal plume overlap between nearby wells");
            ImGui.TextDisabled("Uses g-function approach (Eskilson & Claesson, 1988)");
        }
        
        ImGui.Unindent();
    }
    else
    {
        ImGui.TextDisabled("Independent simulation of each borehole (existing method)");
    }
}

private void DrawDoubletConfiguration()
{
    if (!_useCoupledSimulation)
    {
        ImGui.TextDisabled("Enable coupled simulation first to configure doublets");
        return;
    }
    
    ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.4f, 1.0f), 
        "Configure injection-production well pairs for closed-loop geothermal");
    
    ImGui.Separator();
    ImGui.Text("Doublet Parameters:");
    
    float injTemp = _injectionTemperature;
    if (ImGui.InputFloat("Injection temperature (°C)", ref injTemp))
    {
        _injectionTemperature = Math.Clamp(injTemp, 0.0f, 30.0f);
    }
    
    ImGui.InputFloat("Doublet flow rate (kg/s)", ref _doubletFlowRate);
    _doubletFlowRate = Math.Clamp(_doubletFlowRate, 1.0f, 50.0f);
    
    ImGui.TextDisabled($"Flow rate: ~{_doubletFlowRate:F1} L/s for water");
    
    ImGui.Separator();
    ImGui.Text("Configured Doublet Pairs:");
    
    if (_doubletPairs.Count == 0)
    {
        ImGui.TextDisabled("No doublet pairs configured yet");
    }
    else
    {
        var toRemove = new List<string>();
        foreach (var pair in _doubletPairs)
        {
            ImGui.BulletText($"{pair.Key} (INJ) → {pair.Value} (PROD)");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove##{pair.Key}"))
            {
                toRemove.Add(pair.Key);
            }
        }
        
        foreach (var key in toRemove)
        {
            _doubletPairs.Remove(key);
        }
    }
    
    ImGui.Separator();
    ImGui.Text("Add New Doublet:");
    
    if (_selectedBoreholes.Count >= 2)
    {
        var boreholeNames = _selectedBoreholes.Select(b => b.WellName).ToArray();
        
        int injectionIdx = -1;
        int productionIdx = -1;
        
        ImGui.Combo("Injection Well", ref injectionIdx, boreholeNames, boreholeNames.Length);
        ImGui.Combo("Production Well", ref productionIdx, boreholeNames, boreholeNames.Length);
        
        if (injectionIdx >= 0 && productionIdx >= 0 && injectionIdx != productionIdx)
        {
            if (ImGui.Button("Add Doublet Pair"))
            {
                string injWell = boreholeNames[injectionIdx];
                string prodWell = boreholeNames[productionIdx];
                
                if (!_doubletPairs.ContainsKey(injWell))
                {
                    _doubletPairs[injWell] = prodWell;
                    Logger.Log($"Added doublet pair: {injWell} (injection) -> {prodWell} (production)");
                }
            }
            
            // Show well spacing
            if (injectionIdx >= 0 && productionIdx >= 0)
            {
                var injBh = _selectedBoreholes[injectionIdx];
                var prodBh = _selectedBoreholes[productionIdx];
                double spacing = CalculateWellSpacing(injBh, prodBh);
                ImGui.TextDisabled($"Well spacing: {spacing:F0} m");
            }
        }
    }
    else
    {
        ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "Select at least 2 boreholes to configure doublets");
    }
}

private double CalculateWellSpacing(BoreholeDataset bh1, BoreholeDataset bh2)
{
    double lat1 = bh1.DatasetMetadata.Latitude ?? 0;
    double lon1 = bh1.DatasetMetadata.Longitude ?? 0;
    double lat2 = bh2.DatasetMetadata.Latitude ?? 0;
    double lon2 = bh2.DatasetMetadata.Longitude ?? 0;
    
    double metersPerDegreeLat = 111111.0;
    double avgLat = (lat1 + lat2) / 2.0;
    double metersPerDegreeLon = 111111.0 * Math.Cos(avgLat * Math.PI / 180.0);
    
    double dx = (lon2 - lon1) * metersPerDegreeLon;
    double dy = (lat2 - lat1) * metersPerDegreeLat;
    
    return Math.Sqrt(dx * dx + dy * dy);
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

    // Show coupled simulation results if available
    if (_coupledResults != null)
    {
        ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.6f, 1.0f), "=== COUPLED SIMULATION RESULTS ===");
        ImGui.Separator();
        
        // System-level metrics
        if (ImGui.TreeNode("System Performance"))
        {
            ImGui.Text($"Total Energy Extracted: {_coupledResults.TotalEnergyExtracted / 1e9:F2} GJ");
            ImGui.Text($"System Average COP: {_coupledResults.SystemAverageCOP:F2}");
            if (_coupledResults.SystemLifetime > 0)
            {
                double lifetimeYears = _coupledResults.SystemLifetime / (365.25 * 24 * 3600);
                ImGui.Text($"System Lifetime: {lifetimeYears:F1} years");
            }
            ImGui.TreePop();
        }
        
        // Regional groundwater flow
        if (_coupledResults.RegionalFlowVelocities.Count > 0 && ImGui.TreeNode("Regional Groundwater Flow"))
        {
            foreach (var kvp in _coupledResults.RegionalFlowVelocities)
            {
                var vel = kvp.Value;
                double speed = vel.Length() * 86400; // m/day
                double angle = Math.Atan2(vel.Y, vel.X) * 180 / Math.PI;
                ImGui.BulletText($"{kvp.Key}: {speed:F3} m/day at {angle:F1}°");
            }
            ImGui.TreePop();
        }
        
        // Thermal breakthrough times
        if (_coupledResults.ThermalBreakthroughTimes.Count > 0 && ImGui.TreeNode("Thermal Breakthrough"))
        {
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.4f, 1.0f), 
                "Time until production temperature drops by 1K");
            
            foreach (var kvp in _coupledResults.ThermalBreakthroughTimes)
            {
                double years = kvp.Value / (365.25 * 24 * 3600);
                var color = years > 25 ? new Vector4(0.4f, 1.0f, 0.4f, 1.0f) : // Good (>25 years)
                           years > 15 ? new Vector4(1.0f, 0.8f, 0.4f, 1.0f) : // Moderate (15-25 years)
                           new Vector4(1.0f, 0.4f, 0.4f, 1.0f); // Poor (<15 years)
                
                ImGui.TextColored(color, $"{kvp.Key}: {years:F1} years");
            }
            ImGui.TreePop();
        }
        
        // Optimal well spacing
        if (_coupledResults.OptimalWellSpacing.Count > 0 && ImGui.TreeNode("Optimal Well Spacing"))
        {
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), 
                "Recommended spacing for 30-year lifetime");
            
            foreach (var kvp in _coupledResults.OptimalWellSpacing)
            {
                ImGui.BulletText($"{kvp.Key}: {kvp.Value:F0} m");
            }
            ImGui.TreePop();
        }
        
        // Thermal interference factors
        if (_coupledResults.ThermalInterferenceFactors.Count > 0 && ImGui.TreeNode("Thermal Interference"))
        {
            ImGui.TextDisabled("Thermal coupling between boreholes (0-1)");
            
            var displayedPairs = new HashSet<string>();
            foreach (var kvp in _coupledResults.ThermalInterferenceFactors)
            {
                var (bh1, bh2) = kvp.Key;
                string pairKey = string.Compare(bh1, bh2) < 0 ? $"{bh1}-{bh2}" : $"{bh2}-{bh1}";
                
                if (!displayedPairs.Contains(pairKey) && kvp.Value > 0.05) // Only show significant interference
                {
                    displayedPairs.Add(pairKey);
                    var color = kvp.Value > 0.5 ? new Vector4(1.0f, 0.4f, 0.4f, 1.0f) : // High interference
                               kvp.Value > 0.2 ? new Vector4(1.0f, 0.8f, 0.4f, 1.0f) : // Moderate
                               new Vector4(0.6f, 0.8f, 1.0f, 1.0f); // Low
                    
                    ImGui.TextColored(color, $"{bh1} <-> {bh2}: {kvp.Value:F3}");
                }
            }
            ImGui.TreePop();
        }
        
        ImGui.Separator();
    }

    // Individual borehole results
    ImGui.Text("Individual Borehole Results:");
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
                
                // Show if this is part of a doublet
                if (_doubletPairs.ContainsKey(kvp.Key))
                {
                    ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), 
                        $"INJECTION WELL → {_doubletPairs[kvp.Key]}");
                }
                else if (_doubletPairs.ContainsValue(kvp.Key))
                {
                    var injWell = _doubletPairs.First(p => p.Value == kvp.Key).Key;
                    ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.4f, 1.0f), 
                        $"PRODUCTION WELL ← {injWell}");
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
    Logger.Log($"Mode: {(_useCoupledSimulation ? "COUPLED (Aquifer + Interference)" : "INDEPENDENT")}");

    // Run simulations in background
    Task.Run(() =>
    {
        try
        {
            if (_useCoupledSimulation)
            {
                // NEW: Run coupled multi-borehole simulation with aquifer flow and thermal interference
                Logger.Log("=== RUNNING COUPLED MULTI-BOREHOLE SIMULATION ===");
                
                var config = new MultiBoreholeSimulationConfig
                {
                    Boreholes = _selectedBoreholes,
                    DoubletPairs = new Dictionary<string, string>(_doubletPairs),
                    EnableRegionalFlow = _enableRegionalFlow,
                    EnableThermalInterference = _enableThermalInterference,
                    HeightmapLayer = _selectedHeightmap?.Layers.OfType<GISRasterLayer>().FirstOrDefault(),
                    RegionalHydraulicConductivity = _regionalHydraulicConductivity,
                    AquiferThickness = _aquiferThickness,
                    AquiferPorosity = _aquiferPorosity,
                    AnisotropyRatio = _anisotropyRatio,
                    SimulationDuration = 30 * 365.25 * 24 * 3600, // 30 years for doublet systems
                    InjectionTemperature = _injectionTemperature + 273.15, // Convert to Kelvin
                    DoubletFlowRate = _doubletFlowRate
                };
                
                _coupledResults = MultiBoreholeCoupledSimulation.RunCoupledSimulation(
                    config,
                    (status, progress) =>
                    {
                        _simulationStatus = status;
                        _simulationProgress = progress;
                    }
                );
                
                // Transfer individual results to the existing dictionary for compatibility
                _simulationResults = _coupledResults.IndividualResults;
                
                _simulationStatus = "Coupled simulation completed";
                _simulationProgress = 1.0f;
                Logger.Log("=== COUPLED SIMULATION COMPLETED ===");
                
                // Log key coupled results
                if (_coupledResults.ThermalBreakthroughTimes.Count > 0)
                {
                    Logger.Log("THERMAL BREAKTHROUGH TIMES:");
                    foreach (var kvp in _coupledResults.ThermalBreakthroughTimes)
                    {
                        double years = kvp.Value / (365.25 * 24 * 3600);
                        Logger.Log($"  {kvp.Key}: {years:F1} years");
                    }
                }
                
                if (_coupledResults.OptimalWellSpacing.Count > 0)
                {
                    Logger.Log("OPTIMAL WELL SPACING RECOMMENDATIONS:");
                    foreach (var kvp in _coupledResults.OptimalWellSpacing)
                    {
                        Logger.Log($"  {kvp.Key}: {kvp.Value:F0} m");
                    }
                }
            }
            else
            {
                // EXISTING: Run independent simulations (original method)
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

        // Store reference for export
        _createdSubsurfaceModel = subsurfaceModel;

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

private void DrawExportSection()
{
    if (_createdSubsurfaceModel == null)
    {
        ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "Please create a subsurface model first.");
        return;
    }

    ImGui.TextColored(new Vector4(0.4f, 0.7f, 1.0f, 1.0f),
        "Export subsurface geothermal data for visualization and analysis");

    ImGui.Separator();

    // Model statistics
    if (ImGui.TreeNode("Subsurface Model Statistics"))
    {
        ImGui.Text($"Voxels: {_createdSubsurfaceModel.VoxelGrid.Count:N0}");
        ImGui.Text($"Grid: {_createdSubsurfaceModel.GridResolutionX}×{_createdSubsurfaceModel.GridResolutionY}×{_createdSubsurfaceModel.GridResolutionZ}");
        ImGui.Text($"Bounds: {_createdSubsurfaceModel.GridSize.X:F0}×{_createdSubsurfaceModel.GridSize.Y:F0}×{_createdSubsurfaceModel.GridSize.Z:F0} m");
        ImGui.Text($"Source boreholes: {_createdSubsurfaceModel.SourceBoreholeNames.Count}");

        // Temperature statistics if available
        var temps = _createdSubsurfaceModel.VoxelGrid
            .Where(v => v.Parameters.ContainsKey("Temperature"))
            .Select(v => v.Parameters["Temperature"])
            .ToList();

        if (temps.Count > 0)
        {
            ImGui.Text($"Temperature range: {temps.Min():F1}°C - {temps.Max():F1}°C (avg: {temps.Average():F1}°C)");
        }

        ImGui.TreePop();
    }

    ImGui.Separator();

    // 3D VTK Export
    if (ImGui.TreeNode("3D Model Export (VTK)"))
    {
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "VTK Structured Grid");
        ImGui.TextDisabled("Compatible with ParaView, Blender, VisIt, MayaVi");

        ImGui.Separator();
        ImGui.Text("Fields to export:");
        ImGui.Checkbox("Temperature", ref _exportTemperature);
        ImGui.SameLine();
        ImGui.Checkbox("Thermal Conductivity", ref _exportThermalConductivity);
        ImGui.Checkbox("Porosity", ref _exportPorosity);
        ImGui.SameLine();
        ImGui.Checkbox("Permeability", ref _exportPermeability);
        ImGui.Checkbox("Confidence", ref _exportConfidence);

        ImGui.Separator();
        if (ImGui.Button("Export 3D Model to VTK...", new Vector2(-1, 0)))
        {
            if (_exportDialog == null)
            {
                _exportDialog = new ImGuiExportFileDialog("export_vtk", "Export 3D Model to VTK");
                _exportDialog.SetExtensions((".vtk", "VTK Structured Grid"));
            }
            _exportDialog.IsOpen = true;
        }

        ImGui.TreePop();
    }

    ImGui.Separator();

    // 2D GeoTIFF Export
    if (ImGui.TreeNode("Geothermal Potential Maps (GeoTIFF)"))
    {
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "2D Horizontal Depth Slices");
        ImGui.TextDisabled("Compatible with QGIS, ArcGIS, GDAL, Python");

        ImGui.Separator();
        ImGui.Text("Manage depth slices (meters below surface):");

        // Display and edit existing slices
        var toRemove = new List<int>();
        for (int i = 0; i < _depthSlices.Count; i++)
        {
            ImGui.PushID(i);
            float depth = _depthSlices[i];
            if (ImGui.InputFloat($"##depth{i}", ref depth, 0, 0, "%.0f m"))
            {
                _depthSlices[i] = Math.Max(0, depth);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("×"))
            {
                toRemove.Add(i);
            }
            ImGui.PopID();

            if ((i + 1) % 3 != 0 && i < _depthSlices.Count - 1)
                ImGui.SameLine();
        }

        // Remove marked slices
        for (int i = toRemove.Count - 1; i >= 0; i--)
        {
            _depthSlices.RemoveAt(toRemove[i]);
        }

        // Add new slice
        ImGui.Separator();
        ImGui.InputFloat("New depth slice", ref _newDepthSlice, 100, 500, "%.0f m");
        ImGui.SameLine();
        if (ImGui.Button("Add Slice"))
        {
            if (_newDepthSlice > 0 && !_depthSlices.Contains(_newDepthSlice))
            {
                _depthSlices.Add(_newDepthSlice);
                _depthSlices.Sort();
            }
        }

        ImGui.Separator();
        ImGui.Text("Export options:");
        ImGui.Checkbox("Export heat flow maps", ref _exportHeatFlowMaps);
        ImGui.TextDisabled("In addition to temperature maps");

        ImGui.Separator();
        if (ImGui.Button("Export Geothermal Maps to GeoTIFF", new Vector2(-1, 0)))
        {
            ExportGeothermalMaps();
        }
        ImGui.TextDisabled($"Will export {_depthSlices.Count} depth slices × {(_exportHeatFlowMaps ? 2 : 1)} map types");

        ImGui.TreePop();
    }

    ImGui.Separator();

    // CSV/ASCII Export
    if (ImGui.TreeNode("Point Cloud Export (CSV)"))
    {
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Raw Voxel Data");
        ImGui.TextDisabled("CSV format for Excel, Python, R, etc.");

        ImGui.Separator();
        if (ImGui.Button("Export Voxel Data to CSV...", new Vector2(-1, 0)))
        {
            ExportToCSV();
        }
        ImGui.TextDisabled($"Exports {_createdSubsurfaceModel.VoxelGrid.Count:N0} voxels with all properties");

        ImGui.TreePop();
    }

    // Export status
    if (_isExporting)
    {
        ImGui.Separator();
        ImGui.ProgressBar(_exportProgress, new Vector2(-1, 0), _exportStatus);
    }
}

private void DrawExportDialog()
{
    if (_exportDialog != null && _exportDialog.Submit())
    {
        // User selected a file
        var path = _exportDialog.SelectedPath;

        if (!string.IsNullOrEmpty(path))
        {
            // Ensure .vtk extension
            if (!path.EndsWith(".vtk", StringComparison.OrdinalIgnoreCase))
            {
                path += ".vtk";
            }

            ExportToVTK(path);
        }
    }
}

private async void ExportToVTK(string path)
{
    if (_createdSubsurfaceModel == null)
    {
        Logger.LogError("No subsurface model to export");
        return;
    }

    _isExporting = true;
    _exportProgress = 0.0f;
    _exportStatus = "Exporting to VTK...";

    try
    {
        var progress = new Progress<(float, string)>(p =>
        {
            _exportProgress = p.Item1;
            _exportStatus = p.Item2;
        });

        // Create export options based on user selections
        var exportOptions = new VTKExportOptions
        {
            ExportTemperature = _exportTemperature,
            ExportThermalConductivity = _exportThermalConductivity,
            ExportPorosity = _exportPorosity,
            ExportPermeability = _exportPermeability,
            ExportConfidence = _exportConfidence
        };

        await SubsurfaceExporter.ExportToVTKAsync(_createdSubsurfaceModel, path, exportOptions, progress);

        Logger.Log($"Successfully exported 3D model to: {path}");
        _exportStatus = $"Export complete: {path}";
    }
    catch (Exception ex)
    {
        Logger.LogError($"Failed to export VTK: {ex.Message}");
        _exportStatus = $"Export failed: {ex.Message}";
    }
    finally
    {
        _isExporting = false;
    }
}

private async void ExportGeothermalMaps()
{
    if (_createdSubsurfaceModel == null)
    {
        Logger.LogError("No subsurface model to export");
        return;
    }

    if (_depthSlices.Count == 0)
    {
        Logger.LogError("No depth slices configured");
        return;
    }

    _isExporting = true;
    _exportProgress = 0.0f;
    _exportStatus = "Generating geothermal maps...";

    try
    {
        // Generate maps
        var maps = SubsurfaceExporter.GenerateGeothermalPotentialMaps(
            _createdSubsurfaceModel,
            _depthSlices.ToArray());

        // Create output directory in user's Documents folder
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var outputDir = Path.Combine(documentsPath, "GeoscientistToolkit", "GeothermalMaps", timestamp);
        Directory.CreateDirectory(outputDir);

        // Export each map
        int totalMaps = maps.Count * (_exportHeatFlowMaps ? 2 : 1);
        int exportedMaps = 0;

        foreach (var kvp in maps)
        {
            var depth = kvp.Key;
            var map = kvp.Value;

            // Export temperature map
            _exportStatus = $"Exporting temperature map at {depth}m depth...";
            _exportProgress = (float)exportedMaps / totalMaps;

            var tempPath = Path.Combine(outputDir, $"temperature_{depth}m.tif");
            var progress = new Progress<(float, string)>(p => { _exportStatus = p.Item2; });
            await SubsurfaceExporter.ExportGeothermalMapToGeoTiffAsync(
                map, tempPath, GeothermalMapType.Temperature, progress);
            exportedMaps++;

            // Export heat flow map if requested
            if (_exportHeatFlowMaps)
            {
                _exportStatus = $"Exporting heat flow map at {depth}m depth...";
                _exportProgress = (float)exportedMaps / totalMaps;

                var heatFlowPath = Path.Combine(outputDir, $"heatflow_{depth}m.tif");
                await SubsurfaceExporter.ExportGeothermalMapToGeoTiffAsync(
                    map, heatFlowPath, GeothermalMapType.HeatFlow, progress);
                exportedMaps++;
            }
        }

        _exportProgress = 1.0f;
        _exportStatus = $"Exported {exportedMaps} maps to {outputDir}";
        Logger.Log(_exportStatus);
        Logger.Log($"  Temperature maps: {maps.Count}");
        if (_exportHeatFlowMaps)
            Logger.Log($"  Heat flow maps: {maps.Count}");
    }
    catch (Exception ex)
    {
        Logger.LogError($"Failed to export geothermal maps: {ex.Message}");
        _exportStatus = $"Export failed: {ex.Message}";
    }
    finally
    {
        _isExporting = false;
    }
}

private async void ExportToCSV()
{
    if (_createdSubsurfaceModel == null)
    {
        Logger.LogError("No subsurface model to export");
        return;
    }

    _isExporting = true;
    _exportProgress = 0.0f;
    _exportStatus = "Exporting to CSV...";

    try
    {
        // Create output directory in user's Documents folder
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var outputDir = Path.Combine(documentsPath, "GeoscientistToolkit", "Exports");
        Directory.CreateDirectory(outputDir);

        // Generate filename with timestamp to avoid overwriting
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"subsurface_voxels_{timestamp}.csv";
        var path = Path.Combine(outputDir, fileName);

        var progress = new Progress<(float, string)>(p =>
        {
            _exportProgress = p.Item1;
            _exportStatus = p.Item2;
        });

        await SubsurfaceExporter.ExportToCSVAsync(_createdSubsurfaceModel, path, progress);

        _exportProgress = 1.0f;
        _exportStatus = $"Exported to {path}";
        Logger.Log(_exportStatus);
    }
    catch (Exception ex)
    {
        Logger.LogError($"Failed to export CSV: {ex.Message}");
        _exportStatus = $"Export failed: {ex.Message}";
    }
    finally
    {
        _isExporting = false;
    }
}

}