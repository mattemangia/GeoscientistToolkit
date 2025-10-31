// GeoscientistToolkit/Analysis/Geothermal/GeothermalSimulationTools.cs

using System.Numerics;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.UI.Visualization;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using System.IO;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// ImGui tool for configuring and running geothermal simulations on borehole data.
/// </summary>
public class GeothermalSimulationTools : IDatasetTools, IDisposable
{
    private GeothermalSimulationOptions _options = new();
    private GeothermalSimulationResults _results;
    private GeothermalVisualization3D _visualization3D;
    
    private bool _isSimulationRunning = false;
    private float _simulationProgress = 0f;
    private string _simulationMessage = "";
    private CancellationTokenSource _cancellationTokenSource;
    
    // UI state
    private int _selectedHeatExchangerType = 0;
    private int _selectedFlowConfig = 0;
    private bool _showAdvancedOptions = false;
    private bool _showResults = false;
    private bool _show3DVisualization = false;
    private int _selectedResultTab = 0;
    
    // Material property editing
    private string _newLayerName = "";
    private float _newLayerConductivity = 2.5f;
    private float _newLayerSpecificHeat = 900f;
    private float _newLayerDensity = 2650f;
    private float _newLayerPorosity = 0.1f;
    private float _newLayerPermeability = 1e-14f;
    
    // Visualization
    private List<Mesh3DDataset> _visualizationMeshes = new();
    private int _selectedIsosurface = 0;
    private float _newIsosurfaceTemp = 20f;
    
    // Export file dialog
    private ImGuiExportFileDialog _exportDialog = new ImGuiExportFileDialog("geothermal_export", "Export Geothermal Results");
    
    // Graphics device reference for 3D visualization
    private GraphicsDevice _graphicsDevice;
   
    public GeothermalSimulationTools(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }
    
    public void Draw(Dataset dataset)
    {
        if (dataset is not BoreholeDataset boreholeDataset)
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "This tool requires a borehole dataset!");
            return;
        }
        
        // Initialize options if needed
        if (_options.BoreholeDataset != boreholeDataset)
        {
            _options.BoreholeDataset = boreholeDataset;
            _options.SetDefaultValues();
            InitializeLayerProperties(boreholeDataset);
        }
        
        ImGui.Separator();
        
        // Show dataset info
        ImGui.Text($"Well: {boreholeDataset.WellName}");
        ImGui.Text($"Depth: {boreholeDataset.TotalDepth:F1} m");
        ImGui.Text($"Diameter: {boreholeDataset.WellDiameter * 1000:F0} mm");
        ImGui.Text($"Lithology Units: {boreholeDataset.LithologyUnits.Count}");
        
        // Validate borehole data
        if (boreholeDataset.TotalDepth <= 0)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Error: Invalid borehole depth!");
            return;
        }
        
        if (boreholeDataset.LithologyUnits.Count == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "Warning: No lithology units defined. Using default properties.");
        }
        
        ImGui.Separator();
        
        if (_isSimulationRunning)
        {
            RenderSimulationProgress();
        }
        else if (_results != null && _showResults)
        {
            RenderResults();
        }
        else
        {
            RenderConfiguration();
        }
        
        // Handle export dialog
        if (_exportDialog.IsOpen)
        {
            if (_exportDialog.Submit())
            {
                var selectedPath = _exportDialog.SelectedPath;
                ExportResults(selectedPath);
            }
        }
    }
    
    private void InitializeLayerProperties(BoreholeDataset boreholeDataset)
    {
        // Initialize with borehole-specific lithology if available
        foreach (var unit in boreholeDataset.LithologyUnits)
        {
            var name = unit.RockType ?? "Unknown";
            
            if (!_options.LayerThermalConductivities.ContainsKey(name))
            {
                // Try to get from unit parameters, otherwise use defaults
                var conductivity = unit.Parameters.TryGetValue("Thermal Conductivity", out var tc) 
                    ? tc : 2.5;
                _options.LayerThermalConductivities[name] = conductivity;
            }
            
            if (!_options.LayerSpecificHeats.ContainsKey(name))
            {
                var specificHeat = unit.Parameters.TryGetValue("Specific Heat", out var sh) 
                    ? sh : 900;
                _options.LayerSpecificHeats[name] = specificHeat;
            }
            
            if (!_options.LayerDensities.ContainsKey(name))
            {
                var density = unit.Parameters.TryGetValue("Density", out var d) 
                    ? d : 2650;
                _options.LayerDensities[name] = density;
            }
            
            if (!_options.LayerPorosities.ContainsKey(name))
            {
                var porosity = unit.Parameters.TryGetValue("Porosity", out var p) 
                    ? p / 100.0 : 0.1; // Convert from percentage if needed
                _options.LayerPorosities[name] = porosity;
            }
            
            if (!_options.LayerPermeabilities.ContainsKey(name))
            {
                var permeability = unit.Parameters.TryGetValue("Permeability", out var k) 
                    ? k : 1e-14;
                _options.LayerPermeabilities[name] = permeability;
            }
        }
    }
    
    private void RenderConfiguration()
    {
        ImGui.Text("Simulation Configuration");
        ImGui.Separator();
        
        if (ImGui.Button("Run Simulation", new Vector2(200, 30)))
        {
            StartSimulation();
        }
        
        ImGui.SameLine();
        if (_results != null && ImGui.Button("Show Results", new Vector2(200, 30)))
        {
            _showResults = true;
        }
        
        ImGui.Separator();
        
        // Heat Exchanger Configuration
        if (ImGui.CollapsingHeader("Heat Exchanger Configuration"))
        {
            ImGui.Combo("Type", ref _selectedHeatExchangerType, "U-Tube\0Coaxial\0");
            _options.HeatExchangerType = (HeatExchangerType)_selectedHeatExchangerType;
            
            ImGui.Combo("Flow", ref _selectedFlowConfig, "Counter Flow\0Parallel Flow\0");
            _options.FlowConfiguration = (FlowConfiguration)_selectedFlowConfig;
            
            var pipeInnerDiam = (float)(_options.PipeInnerDiameter * 1000);
            if (ImGui.SliderFloat("Pipe Inner Diameter (mm)", ref pipeInnerDiam, 20, 100))
                _options.PipeInnerDiameter = pipeInnerDiam / 1000;
            
            var pipeOuterDiam = (float)(_options.PipeOuterDiameter * 1000);
            if (ImGui.SliderFloat("Pipe Outer Diameter (mm)", ref pipeOuterDiam, 25, 120))
                _options.PipeOuterDiameter = pipeOuterDiam / 1000;
            
            var pipeSpacing = (float)(_options.PipeSpacing * 1000);
            if (ImGui.SliderFloat("Pipe Spacing (mm)", ref pipeSpacing, 50, 200))
                _options.PipeSpacing = pipeSpacing / 1000;
            
            var pipeConductivity = (float)_options.PipeThermalConductivity;
            if (ImGui.SliderFloat("Pipe Conductivity (W/m·K)", ref pipeConductivity, 0.1f, 1.0f))
                _options.PipeThermalConductivity = pipeConductivity;
            
            var groutConductivity = (float)_options.GroutThermalConductivity;
            if (ImGui.SliderFloat("Grout Conductivity (W/m·K)", ref groutConductivity, 1.0f, 3.0f))
                _options.GroutThermalConductivity = groutConductivity;
        }
        
        // Fluid Properties
        if (ImGui.CollapsingHeader("Fluid Properties"))
        {
            var massFlow = (float)_options.FluidMassFlowRate;
            if (ImGui.SliderFloat("Mass Flow Rate (kg/s)", ref massFlow, 0.1f, 2.0f))
                _options.FluidMassFlowRate = massFlow;
            
            var inletTemp = (float)(_options.FluidInletTemperature - 273.15);
            if (ImGui.SliderFloat("Inlet Temperature (°C)", ref inletTemp, 0, 30))
                _options.FluidInletTemperature = inletTemp + 273.15;
            
            var specificHeat = (float)_options.FluidSpecificHeat;
            if (ImGui.InputFloat("Specific Heat (J/kg·K)", ref specificHeat))
                _options.FluidSpecificHeat = specificHeat;
            
            var density = (float)_options.FluidDensity;
            if (ImGui.InputFloat("Density (kg/m³)", ref density))
                _options.FluidDensity = density;
            
            var viscosity = (float)(_options.FluidViscosity * 1000);
            if (ImGui.InputFloat("Viscosity (mPa·s)", ref viscosity))
                _options.FluidViscosity = viscosity / 1000;
            
            var thermalCond = (float)_options.FluidThermalConductivity;
            if (ImGui.InputFloat("Thermal Conductivity (W/m·K)", ref thermalCond))
                _options.FluidThermalConductivity = thermalCond;
        }
        
        // Ground Properties
        if (ImGui.CollapsingHeader("Ground Properties"))
        {
            var surfaceTemp = (float)(_options.SurfaceTemperature - 273.15);
            if (ImGui.SliderFloat("Surface Temperature (°C)", ref surfaceTemp, -10, 30))
                _options.SurfaceTemperature = surfaceTemp + 273.15;
            
            var geoGradient = (float)(_options.AverageGeothermalGradient * 1000);
            if (ImGui.SliderFloat("Geothermal Gradient (K/km)", ref geoGradient, 10, 50))
                _options.AverageGeothermalGradient = geoGradient / 1000;
            
            var geoFlux = (float)(_options.GeothermalHeatFlux * 1000);
            if (ImGui.SliderFloat("Geothermal Heat Flux (mW/m²)", ref geoFlux, 40, 100))
                _options.GeothermalHeatFlux = geoFlux / 1000;
            
            // Layer properties editor
            ImGui.Separator();
            ImGui.Text("Layer Properties:");
            
            if (ImGui.BeginTable("LayerProps", 6))
            {
                ImGui.TableSetupColumn("Layer");
                ImGui.TableSetupColumn("k (W/m·K)");
                ImGui.TableSetupColumn("Cp (J/kg·K)");
                ImGui.TableSetupColumn("ρ (kg/m³)");
                ImGui.TableSetupColumn("φ (-)");
                ImGui.TableSetupColumn("K (m²)");
                ImGui.TableHeadersRow();
                
                foreach (var layer in _options.LayerThermalConductivities.Keys.ToList())
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(layer);
                    
                    ImGui.TableNextColumn();
                    var k = (float)_options.LayerThermalConductivities[layer];
                    if (ImGui.InputFloat($"##k_{layer}", ref k, 0, 0, "%.2f"))
                        _options.LayerThermalConductivities[layer] = k;
                    
                    ImGui.TableNextColumn();
                    var cp = (float)_options.LayerSpecificHeats.GetValueOrDefault(layer, 900);
                    if (ImGui.InputFloat($"##cp_{layer}", ref cp, 0, 0, "%.0f"))
                        _options.LayerSpecificHeats[layer] = cp;
                    
                    ImGui.TableNextColumn();
                    var rho = (float)_options.LayerDensities.GetValueOrDefault(layer, 2650);
                    if (ImGui.InputFloat($"##rho_{layer}", ref rho, 0, 0, "%.0f"))
                        _options.LayerDensities[layer] = rho;
                    
                    ImGui.TableNextColumn();
                    var phi = (float)_options.LayerPorosities.GetValueOrDefault(layer, 0.1);
                    if (ImGui.InputFloat($"##phi_{layer}", ref phi, 0, 0, "%.3f"))
                        _options.LayerPorosities[layer] = phi;
                    
                    ImGui.TableNextColumn();
                    var perm = (float)_options.LayerPermeabilities.GetValueOrDefault(layer, 1e-14);
                    var permLog = MathF.Log10(perm);
                    if (ImGui.InputFloat($"##perm_{layer}", ref permLog, 0, 0, "%.1f"))
                        _options.LayerPermeabilities[layer] = MathF.Pow(10, permLog);
                    ImGui.SetItemTooltip($"Permeability: {perm:E2} m²");
                }
                
                // Add new layer row
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.InputText("##newlayer", ref _newLayerName, 50);
                
                ImGui.TableNextColumn();
                ImGui.InputFloat("##newk", ref _newLayerConductivity, 0, 0, "%.2f");
                
                ImGui.TableNextColumn();
                ImGui.InputFloat("##newcp", ref _newLayerSpecificHeat, 0, 0, "%.0f");
                
                ImGui.TableNextColumn();
                ImGui.InputFloat("##newrho", ref _newLayerDensity, 0, 0, "%.0f");
                
                ImGui.TableNextColumn();
                ImGui.InputFloat("##newphi", ref _newLayerPorosity, 0, 0, "%.3f");
                
                ImGui.TableNextColumn();
                if (ImGui.Button("Add"))
                {
                    if (!string.IsNullOrEmpty(_newLayerName))
                    {
                        _options.LayerThermalConductivities[_newLayerName] = _newLayerConductivity;
                        _options.LayerSpecificHeats[_newLayerName] = _newLayerSpecificHeat;
                        _options.LayerDensities[_newLayerName] = _newLayerDensity;
                        _options.LayerPorosities[_newLayerName] = _newLayerPorosity;
                        _options.LayerPermeabilities[_newLayerName] = _newLayerPermeability;
                        _newLayerName = "";
                    }
                }
                
                ImGui.EndTable();
            }
        }
        
        // Groundwater Flow
        if (ImGui.CollapsingHeader("Groundwater Flow"))
        {
            var simulateGroundwaterFlow = _options.SimulateGroundwaterFlow;
            if (ImGui.Checkbox("Simulate Groundwater Flow", ref simulateGroundwaterFlow))
                _options.SimulateGroundwaterFlow = simulateGroundwaterFlow;

            if (_options.SimulateGroundwaterFlow)
            {
                var gwVel = _options.GroundwaterVelocity;
                var gwSpeed = gwVel.Length() * 86400; // m/day
                var gwDir = gwVel.Length() > 0 ? Vector3.Normalize(gwVel) : Vector3.UnitX;
                
                if (ImGui.SliderFloat("Flow Speed (m/day)", ref gwSpeed, 0, 10))
                {
                    _options.GroundwaterVelocity = gwDir * gwSpeed / 86400;
                }
                
                var azimuth = MathF.Atan2(gwDir.Y, gwDir.X) * 180 / MathF.PI;
                if (ImGui.SliderFloat("Flow Direction (°)", ref azimuth, -180, 180))
                {
                    var rad = azimuth * MathF.PI / 180;
                    gwDir = new Vector3(MathF.Cos(rad), MathF.Sin(rad), 0);
                    _options.GroundwaterVelocity = gwDir * gwSpeed / 86400;
                }
                
                var gwTemp = (float)(_options.GroundwaterTemperature - 273.15);
                if (ImGui.SliderFloat("Groundwater Temperature (°C)", ref gwTemp, 0, 30))
                    _options.GroundwaterTemperature = gwTemp + 273.15;
                
                var headTop = (float)_options.HydraulicHeadTop;
                if (ImGui.InputFloat("Hydraulic Head Top (m)", ref headTop))
                    _options.HydraulicHeadTop = headTop;
                
                var headBottom = (float)_options.HydraulicHeadBottom;
                if (ImGui.InputFloat("Hydraulic Head Bottom (m)", ref headBottom))
                    _options.HydraulicHeadBottom = headBottom;
            }
        }
        
        // Advanced Options
        ImGui.Checkbox("Show Advanced Options", ref _showAdvancedOptions);
        
        if (_showAdvancedOptions)
        {
            if (ImGui.CollapsingHeader("Simulation Domain"))
            {
                var domainRadius = (float)_options.DomainRadius;
                if (ImGui.SliderFloat("Domain Radius (m)", ref domainRadius, 20, 200))
                    _options.DomainRadius = domainRadius;
                
                var domainExt = (float)_options.DomainExtension;
                if (ImGui.SliderFloat("Domain Extension (m)", ref domainExt, 0, 50))
                    _options.DomainExtension = domainExt;

                var radialGridPoints = _options.RadialGridPoints;
                if (ImGui.SliderInt("Radial Grid Points", ref radialGridPoints, 20, 100))
                    _options.RadialGridPoints = radialGridPoints;

                var angularGridPoints = _options.AngularGridPoints;
                if (ImGui.SliderInt("Angular Grid Points", ref angularGridPoints, 12, 72))
                    _options.AngularGridPoints = angularGridPoints;

                var verticalGridPoints = _options.VerticalGridPoints;
                if (ImGui.SliderInt("Vertical Grid Points", ref verticalGridPoints, 50, 200))
                    _options.VerticalGridPoints = verticalGridPoints;
            }
            
            if (ImGui.CollapsingHeader("Solver Settings"))
            {
                var simTime = (float)(_options.SimulationTime / 86400);
                if (ImGui.InputFloat("Simulation Time (days)", ref simTime))
                    _options.SimulationTime = simTime * 86400;
                
                var timeStep = (float)(_options.TimeStep / 3600);
                if (ImGui.InputFloat("Time Step (hours)", ref timeStep))
                    _options.TimeStep = timeStep * 3600;

                var saveInterval = _options.SaveInterval;
                if (ImGui.InputInt("Save Interval", ref saveInterval))
                    _options.SaveInterval = saveInterval;

                var tolerance = (float)_options.ConvergenceTolerance;
                if (ImGui.InputFloat("Convergence Tolerance", ref tolerance, 0, 0, "%.0e"))
                    _options.ConvergenceTolerance = tolerance;

                var maxIterationsPerStep = _options.MaxIterationsPerStep;
                if (ImGui.InputInt("Max Iterations/Step", ref maxIterationsPerStep))
                    _options.MaxIterationsPerStep = maxIterationsPerStep;

                var useSIMD = _options.UseSIMD;
                if (ImGui.Checkbox("Use SIMD", ref useSIMD))
                    _options.UseSIMD = useSIMD;
            }
        }
    }
    
    private void RenderSimulationProgress()
    {
        ImGui.Text("Simulation Running...");
        ImGui.ProgressBar(_simulationProgress, new Vector2(-1, 0), _simulationMessage);
        
        if (ImGui.Button("Cancel"))
        {
            _cancellationTokenSource?.Cancel();
        }
    }
    
    private void RenderResults()
    {
        ImGui.Text("Simulation Results");
        ImGui.Separator();
        
        if (ImGui.Button("Back to Configuration"))
        {
            _showResults = false;
            _show3DVisualization = false;
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Export Results..."))
        {
            _exportDialog.Open();
        }
        
        ImGui.SameLine();
        if (ImGui.Button("3D Visualization"))
        {
            _show3DVisualization = !_show3DVisualization;
            if (_show3DVisualization && _visualization3D == null)
            {
                InitializeVisualization();
            }
        }
        
        if (_show3DVisualization && _visualization3D != null)
        {
            Render3DVisualization();
            return;
        }
        
        // Result tabs
        if (ImGui.BeginTabBar("ResultTabs"))
        {
            if (ImGui.BeginTabItem("Summary"))
            {
                RenderSummaryTab();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Thermal Performance"))
            {
                RenderThermalPerformanceTab();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Flow Analysis"))
            {
                RenderFlowAnalysisTab();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Layer Analysis"))
            {
                RenderLayerAnalysisTab();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Visualization"))
            {
                RenderVisualizationTab();
                ImGui.EndTabItem();
            }
            
            ImGui.EndTabBar();
        }
    }
    
    private void RenderSummaryTab()
    {
        ImGui.TextWrapped(_results.GenerateSummaryReport());
    }
    
    private void RenderThermalPerformanceTab()
    {
        // Heat extraction plot
        if (_results.HeatExtractionRate.Any())
        {
            var times = _results.HeatExtractionRate.Select(h => (float)(h.time / 86400)).ToArray();
            var rates = _results.HeatExtractionRate.Select(h => (float)h.heatRate).ToArray();
            
            ImGui.PlotLines("Heat Extraction Rate (W)", ref rates[0], rates.Length,
                0, "", rates.Min() * 0.9f, rates.Max() * 1.1f, new Vector2(0, 200));
        }
        
        // Outlet temperature plot
        if (_results.OutletTemperature.Any())
        {
            var times = _results.OutletTemperature.Select(t => (float)(t.time / 86400)).ToArray();
            var temps = _results.OutletTemperature.Select(t => (float)(t.temperature - 273.15)).ToArray();
            
            ImGui.PlotLines("Outlet Temperature (°C)", ref temps[0], temps.Length,
                0, "", temps.Min() * 0.9f, temps.Max() * 1.1f, new Vector2(0, 200));
        }
        
        // COP plot
        if (_results.CoefficientOfPerformance.Any())
        {
            var times = _results.CoefficientOfPerformance.Select(c => (float)(c.time / 86400)).ToArray();
            var cops = _results.CoefficientOfPerformance.Select(c => (float)c.cop).ToArray();
            
            ImGui.PlotLines("Coefficient of Performance", ref cops[0], cops.Length,
                0, "", 0, cops.Max() * 1.1f, new Vector2(0, 200));
        }
        
        ImGui.Separator();
        ImGui.Text($"Total Extracted Energy: {_results.TotalExtractedEnergy / 1e9:F2} GJ");
        ImGui.Text($"Average Heat Rate: {_results.AverageHeatExtractionRate:F0} W");
        ImGui.Text($"Borehole Thermal Resistance: {_results.BoreholeThermalResistance:F3} m·K/W");
        ImGui.Text($"Thermal Influence Radius: {_results.ThermalInfluenceRadius:F1} m");
    }
    
    private void RenderFlowAnalysisTab()
    {
        if (!_options.SimulateGroundwaterFlow)
        {
            ImGui.Text("Groundwater flow simulation was not enabled.");
            return;
        }
        
        ImGui.Text($"Average Péclet Number: {_results.AveragePecletNumber:F2}");
        ImGui.Text($"Longitudinal Dispersivity: {_results.LongitudinalDispersivity:F3} m");
        ImGui.Text($"Transverse Dispersivity: {_results.TransverseDispersivity:F3} m");
        ImGui.Text($"Pressure Drawdown: {_results.PressureDrawdown:F0} Pa");
        
        ImGui.Separator();
        
        // Flow rates by layer
        if (_results.LayerFlowRates.Any())
        {
            ImGui.Text("Layer Flow Rates:");
            if (ImGui.BeginTable("FlowRates", 2))
            {
                ImGui.TableSetupColumn("Layer");
                ImGui.TableSetupColumn("Flow Rate (m³/s)");
                ImGui.TableHeadersRow();
                
                foreach (var kvp in _results.LayerFlowRates.OrderByDescending(l => l.Value))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(kvp.Key);
                    ImGui.TableNextColumn();
                    ImGui.Text($"{kvp.Value:E3}");
                }
                
                ImGui.EndTable();
            }
        }
    }
    
    private void RenderLayerAnalysisTab()
    {
        // Heat flux contributions
        if (_results.LayerHeatFluxContributions.Any())
        {
            ImGui.Text("Heat Flux Contributions:");
            if (ImGui.BeginTable("HeatFlux", 3))
            {
                ImGui.TableSetupColumn("Layer");
                ImGui.TableSetupColumn("Contribution (%)");
                ImGui.TableSetupColumn("Temp Change (K)");
                ImGui.TableHeadersRow();
                
                foreach (var layer in _results.LayerHeatFluxContributions.Keys)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(layer);
                    
                    ImGui.TableNextColumn();
                    var flux = _results.LayerHeatFluxContributions[layer];
                    ImGui.Text($"{flux:F1}%");
                    
                    ImGui.TableNextColumn();
                    var tempChange = _results.LayerTemperatureChanges.GetValueOrDefault(layer, 0);
                    ImGui.Text($"{tempChange:F2}");
                }
                
                ImGui.EndTable();
            }
        }
    }
    
    private void RenderVisualizationTab()
    {
        ImGui.Text("Visualization Options:");
        
        // Temperature isosurfaces
        if (_results.TemperatureIsosurfaces.Any())
        {
            ImGui.Text($"Temperature Isosurfaces: {_results.TemperatureIsosurfaces.Count}");
            ImGui.SliderInt("Selected Isosurface", ref _selectedIsosurface, 
                0, _results.TemperatureIsosurfaces.Count - 1);
            
            if (ImGui.Button("View Single Isosurface"))
            {
                ViewSingleIsosurface();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("View All Isosurfaces"))
            {
                ViewAllIsosurfaces();
            }
        }
        
        // Temperature slices
        if (_results.TemperatureSlices.Any())
        {
            ImGui.Text($"Temperature Slices: {_results.TemperatureSlices.Count}");
            
            foreach (var slice in _results.TemperatureSlices)
            {
                if (ImGui.Button($"View Slice at {slice.Key:F1} m"))
                {
                    // View slice implementation
                }
            }
        }
        
        // Add new isosurface
        ImGui.Separator();
        ImGui.InputFloat("New Isosurface Temp (°C)", ref _newIsosurfaceTemp);
        if (ImGui.Button("Generate Isosurface"))
        {
            GenerateNewIsosurface(_newIsosurfaceTemp + 273.15f);
        }
        
        // Export options
        ImGui.Separator();
        if (ImGui.Button("Export 3D Mesh"))
        {
            Export3DMesh();
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Export Streamlines"))
        {
            ExportStreamlines();
        }
    }
    
    private void InitializeVisualization()
    {
        if (_visualization3D == null && _graphicsDevice != null)
        {
            _visualization3D = new GeothermalVisualization3D(_graphicsDevice);
            
            if (_results != null && _mesh != null)
            {
                _visualization3D.LoadResults(_results, _mesh);
            }
        }
    }
    
    private void Render3DVisualization()
    {
        if (_visualization3D == null) return;
        
        // Create a child window for the 3D view
        var availableSize = ImGui.GetContentRegionAvail();
        var viewSize = new Vector2(availableSize.X * 0.7f, availableSize.Y);
        var controlSize = new Vector2(availableSize.X * 0.3f - 10, availableSize.Y);
        
        // 3D View
        ImGui.BeginChild("3DView", viewSize, ImGuiChildFlags.Border);
        {
            var viewportSize = ImGui.GetContentRegionAvail();
            _visualization3D.Resize((uint)viewportSize.X, (uint)viewportSize.Y);
            _visualization3D.Render();
            
            // Display the rendered texture
            var textureId = _visualization3D.GetRenderTargetImGuiBinding();
            ImGui.Image(textureId, viewportSize);
            
            // Handle mouse input
            if (ImGui.IsItemHovered())
            {
                var mousePos = ImGui.GetMousePos() - ImGui.GetItemRectMin();
                var leftButton = ImGui.IsMouseDown(ImGuiMouseButton.Left);
                var rightButton = ImGui.IsMouseDown(ImGuiMouseButton.Right);
                
                _visualization3D.HandleMouseInput(mousePos, leftButton, rightButton);
                
                var wheel = ImGui.GetIO().MouseWheel;
                if (Math.Abs(wheel) > 0.01f)
                {
                    _visualization3D.HandleMouseWheel(wheel);
                }
            }
        }
        ImGui.EndChild();
        
        ImGui.SameLine();
        
        // Controls
        ImGui.BeginChild("3DControls", controlSize, ImGuiChildFlags.Border);
        {
            _visualization3D.RenderControls();
        }
        ImGui.EndChild();
    }
    
    private GeothermalMesh _mesh;
    
    private void StartSimulation()
    {
        // Validate that we have valid borehole data
        if (_options.BoreholeDataset == null || _options.BoreholeDataset.TotalDepth <= 0)
        {
            Logger.LogError("Cannot start simulation: Invalid or empty borehole dataset.");
            return;
        }
        
        Logger.Log("Starting geothermal simulation...");
        _isSimulationRunning = true;
        _simulationProgress = 0f;
        _simulationMessage = "Initializing...";
        _showResults = false;
        
        _cancellationTokenSource = new CancellationTokenSource();
        
        // Run simulation asynchronously to prevent UI hang
        Task.Run(async () =>
        {
            try
            {
                // Create mesh
                _simulationMessage = "Generating mesh...";
                Logger.Log("Generating computational mesh...");
                _mesh = GeothermalMeshGenerator.GenerateCylindricalMesh(_options.BoreholeDataset, _options);
                
                // Validate mesh
                if (_mesh == null || _mesh.RadialPoints == 0 || _mesh.AngularPoints == 0 || _mesh.VerticalPoints == 0)
                {
                    throw new InvalidOperationException("Failed to generate valid mesh. Check borehole data.");
                }
                
                Logger.Log($"Mesh generation complete: {_mesh.RadialPoints}x{_mesh.AngularPoints}x{_mesh.VerticalPoints} cells.");
                
                // Create solver
                var progress = new Progress<(float progress, string message)>(update =>
                {
                    _simulationProgress = update.progress;
                    _simulationMessage = update.message;
                });
                
                Logger.Log("Initializing simulation solver...");
                var solver = new GeothermalSimulationSolver(_options, _mesh, progress, _cancellationTokenSource.Token);
                
                // Run simulation
                Logger.Log("Executing simulation time stepping...");
                _results = await solver.RunSimulationAsync();
                
                _isSimulationRunning = false;
                _showResults = true;
                Logger.Log("Geothermal simulation completed successfully.");
            }
            catch (OperationCanceledException)
            {
                _simulationMessage = "Simulation cancelled";
                Logger.LogWarning("Geothermal simulation was cancelled by the user.");
                _isSimulationRunning = false;
            }
            catch (Exception ex)
            {
                _simulationMessage = $"Error: {ex.Message}";
                Logger.LogError($"An error occurred during the geothermal simulation: {ex.Message}");
                Logger.LogError($"Stack Trace: {ex.StackTrace}");
                _isSimulationRunning = false;
            }
        });
    }
    
    private void ExportResults(string basePath)
    {
        if (_results == null)
        {
            Logger.LogWarning("Attempted to export results, but no results are available.");
            return;
        }
        
        try
        {
            _results.ExportToCSV(basePath);
            Logger.Log($"Results exported to {basePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export results: {ex.Message}");
        }
    }
    
    private void ViewSingleIsosurface()
    {
        _visualizationMeshes.Clear();
        if (_selectedIsosurface < _results.TemperatureIsosurfaces.Count)
        {
            _visualizationMeshes.Add(_results.TemperatureIsosurfaces[_selectedIsosurface]);
        }
    }
    
    private void ViewAllIsosurfaces()
    {
        _visualizationMeshes.Clear();
        _visualizationMeshes.AddRange(_results.TemperatureIsosurfaces);
    }
    
    private void GenerateNewIsosurface(float temperature)
    {
        // Implementation for generating new isosurface at specified temperature
        Logger.Log($"Generating isosurface at {temperature - 273.15:F1}°C");
        
        if (_results?.BoreholeMesh != null)
        {
            _visualizationMeshes.Add(_results.BoreholeMesh);
        }
    }
    
    private void Export3DMesh()
    {
        // Export visualization meshes to file
        Logger.Log("Exporting 3D mesh...");
    }
    
    private void ExportStreamlines()
    {
        if (_results?.Streamlines == null || !_results.Streamlines.Any())
        {
            Logger.LogWarning("No streamlines to export.");
            return;
        }
        
        // Create a mesh from streamlines
        var streamlineMesh = Mesh3DDataset.CreateEmpty("Streamlines", Path.Combine(Path.GetTempPath(), "streamlines_export.obj"));
        streamlineMesh.Vertices.Clear(); // Clear default cube
        streamlineMesh.Faces.Clear();
        streamlineMesh.Normals.Clear();

        foreach (var streamline in _results.Streamlines)
        {
            int baseVertexIndex = streamlineMesh.Vertices.Count;
            for (int i = 0; i < streamline.Count; i++)
            {
                streamlineMesh.Vertices.Add(streamline[i]);
                streamlineMesh.Normals.Add(Vector3.UnitZ); // Add a dummy normal
            }
            
            for (int i = 0; i < streamline.Count - 1; i++)
            {
                // Create a degenerate triangle to represent a line segment
                streamlineMesh.Faces.Add(new int[] { baseVertexIndex + i, baseVertexIndex + i + 1, baseVertexIndex + i + 1 });
            }
        }
        
        streamlineMesh.VertexCount = streamlineMesh.Vertices.Count;
        streamlineMesh.FaceCount = streamlineMesh.Faces.Count;
        _visualizationMeshes.Add(streamlineMesh);
        Logger.Log("Streamlines exported to visualization.");
    }
    
    public void Dispose()
    {
        _visualization3D?.Dispose();
        _cancellationTokenSource?.Dispose();
    }
}