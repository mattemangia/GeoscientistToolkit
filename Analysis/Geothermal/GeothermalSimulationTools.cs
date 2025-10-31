// GeoscientistToolkit/Tools/GeothermalSimulationTool.cs

using System.Numerics;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Tools;

/// <summary>
/// ImGui tool for configuring and running geothermal simulations on borehole data.
/// </summary>
public class GeothermalSimulationTool : ITool
{
    private readonly Dictionary<string, BoreholeDataset> _boreholeDatasets;
    private BoreholeDataset _selectedDataset;
    private GeothermalSimulationOptions _options = new();
    private GeothermalSimulationResults _results;
    
    private bool _isSimulationRunning = false;
    private float _simulationProgress = 0f;
    private string _simulationMessage = "";
    private CancellationTokenSource _cancellationTokenSource;
    
    // UI state
    private int _selectedHeatExchangerType = 0;
    private int _selectedFlowConfig = 0;
    private bool _showAdvancedOptions = false;
    private bool _showResults = false;
    private bool _show3DVisualization = true;
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
    
    public GeothermalSimulationTool()
    {
        _boreholeDatasets = DatasetManager.GetDatasets<BoreholeDataset>();
    }
    
    public string Name => "Geothermal Simulation";
    public string Icon => FontAwesome.IconFireFlameSimple;
    public string Tooltip => "Run geothermal heat exchanger simulations on borehole data";
    
    public void Render()
    {
        if (!_boreholeDatasets.Any())
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "No borehole datasets loaded!");
            ImGui.Text("Please load a borehole dataset first.");
            return;
        }
        
        // Dataset selection
        if (ImGui.BeginCombo("Borehole Dataset", _selectedDataset?.Name ?? "Select dataset..."))
        {
            foreach (var dataset in _boreholeDatasets.Values)
            {
                bool isSelected = dataset == _selectedDataset;
                if (ImGui.Selectable(dataset.Name, isSelected))
                {
                    _selectedDataset = dataset;
                    _options.BoreholeDataset = dataset;
                    _options.SetDefaultValues();
                    InitializeLayerProperties();
                }
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        
        if (_selectedDataset == null) return;
        
        ImGui.Separator();
        
        // Show dataset info
        ImGui.Text($"Depth: {_selectedDataset.TotalDepth:F1} m");
        ImGui.Text($"Diameter: {_selectedDataset.Diameter:F0} mm");
        ImGui.Text($"Layers: {_selectedDataset.Lithology.Count}");
        ImGui.Text($"Water Table: {_selectedDataset.WaterTableDepth:F1} m");
        
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
    }
    
    private void RenderConfiguration()
    {
        if (ImGui.BeginTabBar("ConfigTabs"))
        {
            if (ImGui.BeginTabItem("Heat Exchanger"))
            {
                RenderHeatExchangerConfig();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Ground Properties"))
            {
                RenderGroundPropertiesConfig();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Flow & Transport"))
            {
                RenderFlowConfig();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Boundary Conditions"))
            {
                RenderBoundaryConfig();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Solver"))
            {
                RenderSolverConfig();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Visualization"))
            {
                RenderVisualizationConfig();
                ImGui.EndTabItem();
            }
            
            ImGui.EndTabBar();
        }
        
        ImGui.Separator();
        
        // Run simulation button
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0.7f, 0, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0.8f, 0, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0.6f, 0, 1));
        
        if (ImGui.Button("Run Simulation", new Vector2(200, 40)))
        {
            StartSimulation();
        }
        
        ImGui.PopStyleColor(3);
        
        ImGui.SameLine();
        
        if (_results != null && ImGui.Button("View Previous Results"))
        {
            _showResults = true;
        }
    }
    
    private void RenderHeatExchangerConfig()
    {
        ImGui.Text("Heat Exchanger Type:");
        ImGui.RadioButton("U-Tube", ref _selectedHeatExchangerType, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Coaxial", ref _selectedHeatExchangerType, 1);
        _options.HeatExchangerType = (HeatExchangerType)_selectedHeatExchangerType;
        
        ImGui.Spacing();
        
        ImGui.Text("Flow Configuration:");
        ImGui.RadioButton("Counter Flow", ref _selectedFlowConfig, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Parallel Flow", ref _selectedFlowConfig, 1);
        _options.FlowConfiguration = (FlowConfiguration)_selectedFlowConfig;
        
        ImGui.Separator();
        ImGui.Text("Pipe Dimensions:");
        
        float innerDiam = (float)(_options.PipeInnerDiameter * 1000);
        if (ImGui.DragFloat("Inner Diameter (mm)", ref innerDiam, 0.5f, 10f, 100f))
            _options.PipeInnerDiameter = innerDiam / 1000;
        
        float outerDiam = (float)(_options.PipeOuterDiameter * 1000);
        if (ImGui.DragFloat("Outer Diameter (mm)", ref outerDiam, 0.5f, 15f, 120f))
            _options.PipeOuterDiameter = outerDiam / 1000;
        
        if (_options.HeatExchangerType == HeatExchangerType.UTube)
        {
            float spacing = (float)(_options.PipeSpacing * 1000);
            if (ImGui.DragFloat("Pipe Spacing (mm)", ref spacing, 1f, 50f, 200f))
                _options.PipeSpacing = spacing / 1000;
        }
        else
        {
            float outerInner = (float)(_options.PipeSpacing * 1000);
            if (ImGui.DragFloat("Outer Pipe Inner Dia (mm)", ref outerInner, 1f, 50f, 200f))
                _options.PipeSpacing = outerInner / 1000;
        }
        
        ImGui.Separator();
        ImGui.Text("Material Properties:");
        
        float pipeCond = (float)_options.PipeThermalConductivity;
        if (ImGui.DragFloat("Pipe Thermal Conductivity", ref pipeCond, 0.01f, 0.1f, 2f, "%.2f W/m·K"))
            _options.PipeThermalConductivity = pipeCond;
        
        float groutCond = (float)_options.GroutThermalConductivity;
        if (ImGui.DragFloat("Grout Thermal Conductivity", ref groutCond, 0.1f, 0.5f, 5f, "%.1f W/m·K"))
            _options.GroutThermalConductivity = groutCond;
        
        ImGui.Separator();
        ImGui.Text("Heat Carrier Fluid:");
        
        float massFlow = (float)_options.FluidMassFlowRate;
        if (ImGui.DragFloat("Mass Flow Rate", ref massFlow, 0.01f, 0.1f, 2f, "%.2f kg/s"))
            _options.FluidMassFlowRate = massFlow;
        
        float inletTemp = (float)(_options.FluidInletTemperature - 273.15);
        if (ImGui.DragFloat("Inlet Temperature", ref inletTemp, 0.5f, -10f, 30f, "%.1f °C"))
            _options.FluidInletTemperature = inletTemp + 273.15;
        
        if (ImGui.CollapsingHeader("Advanced Fluid Properties"))
        {
            float cp = (float)_options.FluidSpecificHeat;
            if (ImGui.InputFloat("Specific Heat", ref cp, 10f, 100f, "%.0f J/kg·K"))
                _options.FluidSpecificHeat = cp;
            
            float density = (float)_options.FluidDensity;
            if (ImGui.InputFloat("Density", ref density, 10f, 100f, "%.0f kg/m³"))
                _options.FluidDensity = density;
            
            float visc = (float)(_options.FluidViscosity * 1000);
            if (ImGui.InputFloat("Viscosity", ref visc, 0.1f, 1f, "%.1f mPa·s"))
                _options.FluidViscosity = visc / 1000;
            
            float fluidCond = (float)_options.FluidThermalConductivity;
            if (ImGui.InputFloat("Thermal Conductivity", ref fluidCond, 0.01f, 0.1f, "%.2f W/m·K"))
                _options.FluidThermalConductivity = fluidCond;
        }
    }
    
    private void RenderGroundPropertiesConfig()
    {
        ImGui.Text("Geological Layer Properties:");
        ImGui.Spacing();
        
        if (ImGui.BeginTable("LayerProperties", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Layer");
            ImGui.TableSetupColumn("λ (W/m·K)");
            ImGui.TableSetupColumn("cp (J/kg·K)");
            ImGui.TableSetupColumn("ρ (kg/m³)");
            ImGui.TableSetupColumn("φ (-)");
            ImGui.TableSetupColumn("k (m²)");
            ImGui.TableHeadersRow();
            
            var layersToRemove = new List<string>();
            
            foreach (var layerName in _options.LayerThermalConductivities.Keys)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(layerName);
                
                ImGui.TableNextColumn();
                float lambda = (float)_options.LayerThermalConductivities[layerName];
                if (ImGui.DragFloat($"##lambda_{layerName}", ref lambda, 0.01f, 0.1f, 10f, "%.2f"))
                    _options.LayerThermalConductivities[layerName] = lambda;
                
                ImGui.TableNextColumn();
                float cp = (float)_options.LayerSpecificHeats[layerName];
                if (ImGui.DragFloat($"##cp_{layerName}", ref cp, 10f, 100f, 5000f, "%.0f"))
                    _options.LayerSpecificHeats[layerName] = cp;
                
                ImGui.TableNextColumn();
                float rho = (float)_options.LayerDensities[layerName];
                if (ImGui.DragFloat($"##rho_{layerName}", ref rho, 10f, 1000f, 4000f, "%.0f"))
                    _options.LayerDensities[layerName] = rho;
                
                ImGui.TableNextColumn();
                float phi = (float)_options.LayerPorosities[layerName];
                if (ImGui.DragFloat($"##phi_{layerName}", ref phi, 0.01f, 0f, 0.7f, "%.2f"))
                    _options.LayerPorosities[layerName] = phi;
                
                ImGui.TableNextColumn();
                float k = (float)Math.Log10(_options.LayerPermeabilities[layerName]);
                if (ImGui.DragFloat($"##k_{layerName}", ref k, 0.1f, -20f, -8f, "1e%.0f"))
                    _options.LayerPermeabilities[layerName] = Math.Pow(10, k);
                
                ImGui.SameLine();
                if (ImGui.SmallButton($"X##del_{layerName}"))
                    layersToRemove.Add(layerName);
            }
            
            // Remove marked layers
            foreach (var layer in layersToRemove)
            {
                _options.LayerThermalConductivities.Remove(layer);
                _options.LayerSpecificHeats.Remove(layer);
                _options.LayerDensities.Remove(layer);
                _options.LayerPorosities.Remove(layer);
                _options.LayerPermeabilities.Remove(layer);
            }
            
            ImGui.EndTable();
        }
        
        ImGui.Separator();
        ImGui.Text("Add New Layer Type:");
        
        ImGui.InputText("Layer Name", ref _newLayerName, 50);
        ImGui.DragFloat("Thermal Conductivity", ref _newLayerConductivity, 0.01f, 0.1f, 10f, "%.2f W/m·K");
        ImGui.DragFloat("Specific Heat", ref _newLayerSpecificHeat, 10f, 100f, 5000f, "%.0f J/kg·K");
        ImGui.DragFloat("Density", ref _newLayerDensity, 10f, 1000f, 4000f, "%.0f kg/m³");
        ImGui.DragFloat("Porosity", ref _newLayerPorosity, 0.01f, 0f, 0.7f, "%.2f");
        float logPerm = MathF.Log10(_newLayerPermeability);
        if (ImGui.DragFloat("Permeability", ref logPerm, 0.1f, -20f, -8f, "1e%.0f m²"))
            _newLayerPermeability = MathF.Pow(10, logPerm);
        
        if (ImGui.Button("Add Layer Type") && !string.IsNullOrWhiteSpace(_newLayerName))
        {
            _options.LayerThermalConductivities[_newLayerName] = _newLayerConductivity;
            _options.LayerSpecificHeats[_newLayerName] = _newLayerSpecificHeat;
            _options.LayerDensities[_newLayerName] = _newLayerDensity;
            _options.LayerPorosities[_newLayerName] = _newLayerPorosity;
            _options.LayerPermeabilities[_newLayerName] = _newLayerPermeability;
            _newLayerName = "";
        }
        
        ImGui.Separator();
        
        ImGui.Text("Fracture Properties:");
        ImGui.Checkbox("Simulate Fractures", ref _options.SimulateFractures);
        
        if (_options.SimulateFractures)
        {
            float aperture = (float)(_options.FractureAperture * 1000);
            if (ImGui.DragFloat("Fracture Aperture", ref aperture, 0.1f, 0.1f, 10f, "%.1f mm"))
                _options.FractureAperture = aperture / 1000;
            
            float fracPerm = (float)Math.Log10(_options.FracturePermeability);
            if (ImGui.DragFloat("Fracture Permeability", ref fracPerm, 0.1f, -12f, -6f, "1e%.0f m²"))
                _options.FracturePermeability = Math.Pow(10, fracPerm);
        }
    }
    
    private void RenderFlowConfig()
    {
        ImGui.Checkbox("Simulate Groundwater Flow", ref _options.SimulateGroundwaterFlow);
        
        if (_options.SimulateGroundwaterFlow)
        {
            ImGui.Separator();
            ImGui.Text("Regional Groundwater Flow:");
            
            var gwVelocity = _options.GroundwaterVelocity;
            float vx = gwVelocity.X * 1e6f; // Convert to mm/s for display
            float vy = gwVelocity.Y * 1e6f;
            float vz = gwVelocity.Z * 1e6f;
            
            if (ImGui.DragFloat("Vx", ref vx, 0.01f, -10f, 10f, "%.2f mm/s"))
                gwVelocity.X = vx * 1e-6f;
            
            if (ImGui.DragFloat("Vy", ref vy, 0.01f, -10f, 10f, "%.2f mm/s"))
                gwVelocity.Y = vy * 1e-6f;
            
            if (ImGui.DragFloat("Vz", ref vz, 0.01f, -10f, 10f, "%.2f mm/s"))
                gwVelocity.Z = vz * 1e-6f;
            
            _options.GroundwaterVelocity = gwVelocity;
            
            float gwTemp = (float)(_options.GroundwaterTemperature - 273.15);
            if (ImGui.DragFloat("Groundwater Temperature", ref gwTemp, 0.5f, 0f, 30f, "%.1f °C"))
                _options.GroundwaterTemperature = gwTemp + 273.15;
            
            ImGui.Separator();
            ImGui.Text("Hydraulic Head:");
            
            float headTop = (float)_options.HydraulicHeadTop;
            if (ImGui.DragFloat("Head at Top", ref headTop, 0.1f, -50f, 50f, "%.1f m"))
                _options.HydraulicHeadTop = headTop;
            
            float headBottom = (float)_options.HydraulicHeadBottom;
            if (ImGui.DragFloat("Head at Bottom", ref headBottom, 0.1f, -100f, 0f, "%.1f m"))
                _options.HydraulicHeadBottom = headBottom;
            
            var gradient = (headBottom - headTop) / (_selectedDataset.TotalDepth + 2 * _options.DomainExtension);
            ImGui.Text($"Hydraulic Gradient: {gradient:F4} m/m");
            
            if (_options.SimulateFractures)
            {
                ImGui.Separator();
                ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "Fracture Flow Active");
                ImGui.Text($"Detected {_selectedDataset.Fractures?.Count ?? 0} fractures");
            }
        }
        else
        {
            ImGui.TextDisabled("Enable groundwater flow to access flow settings");
        }
    }
    
    private void RenderBoundaryConfig()
    {
        ImGui.Text("Outer Radial Boundary:");
        int outerBC = (int)_options.OuterBoundaryCondition;
        ImGui.RadioButton("Fixed Temperature", ref outerBC, 0);
        ImGui.RadioButton("Fixed Heat Flux", ref outerBC, 1);
        ImGui.RadioButton("Convective", ref outerBC, 2);
        ImGui.RadioButton("Adiabatic", ref outerBC, 3);
        _options.OuterBoundaryCondition = (BoundaryConditionType)outerBC;
        
        if (_options.OuterBoundaryCondition == BoundaryConditionType.Dirichlet)
        {
            float outerTemp = (float)(_options.OuterBoundaryTemperature - 273.15);
            if (ImGui.DragFloat("Outer Temperature", ref outerTemp, 0.5f, -10f, 40f, "%.1f °C"))
                _options.OuterBoundaryTemperature = outerTemp + 273.15;
        }
        else if (_options.OuterBoundaryCondition == BoundaryConditionType.Neumann)
        {
            float outerFlux = (float)_options.OuterBoundaryHeatFlux;
            if (ImGui.DragFloat("Outer Heat Flux", ref outerFlux, 0.1f, -100f, 100f, "%.1f W/m²"))
                _options.OuterBoundaryHeatFlux = outerFlux;
        }
        
        ImGui.Separator();
        ImGui.Text("Top Boundary:");
        int topBC = (int)_options.TopBoundaryCondition;
        ImGui.RadioButton("Fixed Temperature##top", ref topBC, 0);
        ImGui.RadioButton("Fixed Heat Flux##top", ref topBC, 1);
        ImGui.RadioButton("Adiabatic##top", ref topBC, 3);
        _options.TopBoundaryCondition = (BoundaryConditionType)topBC;
        
        if (_options.TopBoundaryCondition == BoundaryConditionType.Dirichlet)
        {
            float topTemp = (float)(_options.TopBoundaryTemperature - 273.15);
            if (ImGui.DragFloat("Top Temperature", ref topTemp, 0.5f, -10f, 40f, "%.1f °C"))
                _options.TopBoundaryTemperature = topTemp + 273.15;
        }
        
        ImGui.Separator();
        ImGui.Text("Bottom Boundary:");
        int bottomBC = (int)_options.BottomBoundaryCondition;
        ImGui.RadioButton("Fixed Temperature##bottom", ref bottomBC, 0);
        ImGui.RadioButton("Geothermal Heat Flux##bottom", ref bottomBC, 1);
        ImGui.RadioButton("Adiabatic##bottom", ref bottomBC, 3);
        _options.BottomBoundaryCondition = (BoundaryConditionType)bottomBC;
        
        if (_options.BottomBoundaryCondition == BoundaryConditionType.Neumann)
        {
            float geoFlux = (float)(_options.GeothermalHeatFlux * 1000);
            if (ImGui.DragFloat("Geothermal Heat Flux", ref geoFlux, 0.5f, 0f, 200f, "%.1f mW/m²"))
                _options.GeothermalHeatFlux = geoFlux / 1000;
        }
    }
    
    private void RenderSolverConfig()
    {
        ImGui.Text("Simulation Domain:");
        
        float domainRadius = (float)_options.DomainRadius;
        if (ImGui.DragFloat("Domain Radius", ref domainRadius, 1f, 10f, 200f, "%.0f m"))
            _options.DomainRadius = domainRadius;
        
        float domainExt = (float)_options.DomainExtension;
        if (ImGui.DragFloat("Vertical Extension", ref domainExt, 1f, 0f, 100f, "%.0f m"))
            _options.DomainExtension = domainExt;
        
        ImGui.Separator();
        ImGui.Text("Grid Resolution:");
        
        ImGui.DragInt("Radial Points", ref _options.RadialGridPoints, 1f, 20, 100);
        ImGui.DragInt("Angular Points", ref _options.AngularGridPoints, 1f, 12, 72);
        ImGui.DragInt("Vertical Points", ref _options.VerticalGridPoints, 1f, 50, 200);
        
        var totalCells = _options.RadialGridPoints * _options.AngularGridPoints * _options.VerticalGridPoints;
        ImGui.Text($"Total Grid Cells: {totalCells:N0}");
        
        ImGui.Separator();
        ImGui.Text("Time Stepping:");
        
        float simDays = (float)(_options.SimulationTime / 86400);
        if (ImGui.DragFloat("Simulation Time", ref simDays, 1f, 1f, 3650f, "%.0f days"))
            _options.SimulationTime = simDays * 86400;
        
        float dtHours = (float)(_options.TimeStep / 3600);
        if (ImGui.DragFloat("Time Step", ref dtHours, 0.1f, 0.1f, 24f, "%.1f hours"))
            _options.TimeStep = dtHours * 3600;
        
        ImGui.DragInt("Save Interval", ref _options.SaveInterval, 1f, 1, 100);
        
        var totalSteps = (int)(_options.SimulationTime / _options.TimeStep);
        var savedSteps = totalSteps / _options.SaveInterval;
        ImGui.Text($"Total Time Steps: {totalSteps}");
        ImGui.Text($"Saved Time Steps: {savedSteps}");
        
        ImGui.Separator();
        ImGui.Text("Convergence:");
        
        float tolerance = (float)Math.Log10(_options.ConvergenceTolerance);
        if (ImGui.DragFloat("Convergence Tolerance", ref tolerance, 0.1f, -10f, -3f, "1e%.0f"))
            _options.ConvergenceTolerance = Math.Pow(10, tolerance);
        
        ImGui.DragInt("Max Iterations/Step", ref _options.MaxIterationsPerStep, 10f, 100, 5000);
        
        ImGui.Separator();
        ImGui.Text("Performance:");
        
        ImGui.Checkbox("Use SIMD Optimizations", ref _options.UseSIMD);
        
        ImGui.BeginDisabled(true);
        ImGui.Checkbox("Use GPU Acceleration", ref _options.UseGPU);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled("(Not yet implemented)");
    }
    
    private void RenderVisualizationConfig()
    {
        ImGui.Checkbox("Generate 3D Temperature Isosurfaces", ref _options.Generate3DIsosurfaces);
        
        if (_options.Generate3DIsosurfaces)
        {
            ImGui.Text("Isosurface Temperatures:");
            
            for (int i = 0; i < _options.IsosurfaceTemperatures.Count; i++)
            {
                float temp = (float)(_options.IsosurfaceTemperatures[i] - 273.15);
                if (ImGui.DragFloat($"##iso{i}", ref temp, 0.5f, -10f, 100f, "%.1f °C"))
                    _options.IsosurfaceTemperatures[i] = temp + 273.15;
                
                ImGui.SameLine();
                if (ImGui.SmallButton($"X##deliso{i}"))
                {
                    _options.IsosurfaceTemperatures.RemoveAt(i);
                    break;
                }
            }
            
            ImGui.InputFloat("New Temperature", ref _newIsosurfaceTemp, 1f, 5f, "%.1f °C");
            ImGui.SameLine();
            if (ImGui.Button("Add"))
            {
                _options.IsosurfaceTemperatures.Add(_newIsosurfaceTemp + 273.15);
            }
        }
        
        ImGui.Separator();
        
        ImGui.Checkbox("Generate Flow Streamlines", ref _options.GenerateStreamlines);
        
        if (_options.GenerateStreamlines)
        {
            ImGui.DragInt("Number of Streamlines", ref _options.StreamlineCount, 1f, 10, 200);
        }
        
        ImGui.Separator();
        
        ImGui.Checkbox("Generate 2D Slices", ref _options.Generate2DSlices);
        
        if (_options.Generate2DSlices)
        {
            ImGui.Text("Slice Depths (% of total):");
            
            for (int i = 0; i < _options.SlicePositions.Count; i++)
            {
                float pos = (float)(_options.SlicePositions[i] * 100);
                if (ImGui.DragFloat($"##slice{i}", ref pos, 0.5f, 0f, 100f, "%.1f %%"))
                    _options.SlicePositions[i] = pos / 100;
                
                ImGui.SameLine();
                if (ImGui.SmallButton($"X##delslice{i}"))
                {
                    _options.SlicePositions.RemoveAt(i);
                    break;
                }
            }
            
            if (ImGui.Button("Add Slice"))
            {
                _options.SlicePositions.Add(0.5);
            }
        }
    }
    
    private void RenderSimulationProgress()
    {
        ImGui.Text("Simulation Running...");
        ImGui.ProgressBar(_simulationProgress, new Vector2(-1, 0), _simulationMessage);
        
        ImGui.Spacing();
        
        if (ImGui.Button("Cancel Simulation"))
        {
            _cancellationTokenSource?.Cancel();
        }
    }
    
    private void RenderResults()
    {
        if (ImGui.Button("Back to Configuration"))
        {
            _showResults = false;
            return;
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Export Results"))
        {
            ExportResults();
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Generate Report"))
        {
            GenerateReport();
        }
        
        ImGui.Separator();
        
        if (ImGui.BeginTabBar("ResultsTabs"))
        {
            if (ImGui.BeginTabItem("Summary"))
            {
                RenderSummaryResults();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Thermal Performance"))
            {
                RenderThermalResults();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Flow Analysis"))
            {
                RenderFlowResults();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Layer Analysis"))
            {
                RenderLayerResults();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Visualization"))
            {
                RenderVisualizationResults();
                ImGui.EndTabItem();
            }
            
            ImGui.EndTabBar();
        }
    }
    
    private void RenderSummaryResults()
    {
        ImGui.TextWrapped(_results.GenerateSummaryReport());
    }
    
    private void RenderThermalResults()
    {
        // Heat extraction plot
        if (ImPlot.BeginPlot("Heat Extraction Rate", new Vector2(-1, 300)))
        {
            var times = _results.HeatExtractionRate.Select(h => h.time / 86400.0).ToArray();
            var heatRates = _results.HeatExtractionRate.Select(h => h.heatRate).ToArray();
            
            ImPlot.PlotLine("Heat Rate", ref times[0], ref heatRates[0], times.Length);
            
            ImPlot.SetupAxes("Time (days)", "Heat Rate (W)");
            ImPlot.EndPlot();
        }
        
        // Temperature profile plot
        if (ImPlot.BeginPlot("Fluid Temperature Profile", new Vector2(-1, 300)))
        {
            var depths = _results.FluidTemperatureProfile.Select(p => p.depth).ToArray();
            var tempDown = _results.FluidTemperatureProfile.Select(p => p.temperatureDown - 273.15).ToArray();
            var tempUp = _results.FluidTemperatureProfile.Select(p => p.temperatureUp - 273.15).ToArray();
            
            ImPlot.PlotLine("Downward Flow", ref depths[0], ref tempDown[0], depths.Length);
            ImPlot.PlotLine("Upward Flow", ref depths[0], ref tempUp[0], depths.Length);
            
            ImPlot.SetupAxes("Depth (m)", "Temperature (°C)");
            ImPlot.EndPlot();
        }
        
        ImGui.Text($"Borehole Thermal Resistance: {_results.BoreholeThermalResistance:F3} m·K/W");
        ImGui.Text($"Thermal Influence Radius: {_results.ThermalInfluenceRadius:F1} m");
        ImGui.Text($"Effective Ground Conductivity: {_results.EffectiveGroundConductivity:F2} W/m·K");
        ImGui.Text($"Ground Thermal Diffusivity: {_results.GroundThermalDiffusivity:E3} m²/s");
    }
    
    private void RenderFlowResults()
    {
        if (!_options.SimulateGroundwaterFlow)
        {
            ImGui.TextDisabled("Groundwater flow was not simulated");
            return;
        }
        
        ImGui.Text($"Average Péclet Number: {_results.AveragePecletNumber:F2}");
        ImGui.Text($"Longitudinal Dispersivity: {_results.LongitudinalDispersivity:F3} m");
        ImGui.Text($"Transverse Dispersivity: {_results.TransverseDispersivity:F3} m");
        ImGui.Text($"Pressure Drawdown: {_results.PressureDrawdown:F0} Pa");
        
        // Flow interpretation
        ImGui.Separator();
        ImGui.Text("Flow Regime:");
        if (_results.AveragePecletNumber < 1)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "Diffusion-dominated");
            ImGui.TextWrapped("Heat transfer is primarily by conduction. Groundwater flow has minimal impact.");
        }
        else if (_results.AveragePecletNumber < 10)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Mixed regime");
            ImGui.TextWrapped("Both advection and diffusion are important for heat transfer.");
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Advection-dominated");
            ImGui.TextWrapped("Heat transfer is significantly enhanced by groundwater flow.");
        }
    }
    
    private void RenderLayerResults()
    {
        if (ImGui.BeginTable("LayerContributions", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Layer");
            ImGui.TableSetupColumn("Heat Flux (%)");
            ImGui.TableSetupColumn("Temp Change (K)");
            ImGui.TableSetupColumn("Flow Rate (m³/s)");
            ImGui.TableHeadersRow();
            
            foreach (var layer in _results.LayerHeatFluxContributions.OrderByDescending(l => l.Value))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(layer.Key);
                
                ImGui.TableNextColumn();
                ImGui.Text($"{layer.Value:F1}%");
                
                ImGui.TableNextColumn();
                var tempChange = _results.LayerTemperatureChanges.GetValueOrDefault(layer.Key, 0);
                ImGui.Text($"{tempChange:F2}");
                
                ImGui.TableNextColumn();
                var flowRate = _results.LayerFlowRates.GetValueOrDefault(layer.Key, 0);
                ImGui.Text($"{flowRate:E3}");
            }
            
            ImGui.EndTable();
        }
    }
    
    private void RenderVisualizationResults()
    {
        ImGui.Checkbox("Show 3D Visualization", ref _show3DVisualization);
        
        if (_show3DVisualization && _results.TemperatureIsosurfaces.Any())
        {
            ImGui.Text("Temperature Isosurfaces:");
            
            if (ImGui.BeginCombo("Select Isosurface", 
                _selectedIsosurface < _results.TemperatureIsosurfaces.Count ? 
                _results.TemperatureIsosurfaces[_selectedIsosurface].Name : "None"))
            {
                for (int i = 0; i < _results.TemperatureIsosurfaces.Count; i++)
                {
                    bool isSelected = i == _selectedIsosurface;
                    if (ImGui.Selectable(_results.TemperatureIsosurfaces[i].Name, isSelected))
                    {
                        _selectedIsosurface = i;
                        UpdateVisualization();
                    }
                }
                ImGui.EndCombo();
            }
            
            if (ImGui.Button("Show All Isosurfaces"))
            {
                ShowAllIsosurfaces();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Show Borehole"))
            {
                ShowBoreholeMesh();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Show Streamlines"))
            {
                ShowStreamlines();
            }
        }
        
        // 2D slice viewer
        if (_results.TemperatureSlices.Any())
        {
            ImGui.Separator();
            ImGui.Text("2D Slices:");
            
            // Temperature slice display
            var firstSlice = _results.TemperatureSlices.First();
            var sliceData = firstSlice.Value;
            var nr = sliceData.GetLength(0);
            var nth = sliceData.GetLength(1);
            
            // Create heatmap texture
            // ... (implement heatmap visualization)
        }
    }
    
    private void InitializeLayerProperties()
    {
        // Initialize properties for layers in the borehole
        foreach (var layer in _selectedDataset.Lithology)
        {
            var layerName = layer.RockType ?? "Unknown";
            
            if (!_options.LayerThermalConductivities.ContainsKey(layerName))
            {
                // Use defaults if available
                _options.SetDefaultValues();
            }
        }
    }
    
    private void StartSimulation()
    {
        _isSimulationRunning = true;
        _simulationProgress = 0f;
        _simulationMessage = "Initializing...";
        _showResults = false;
        
        _cancellationTokenSource = new CancellationTokenSource();
        
        Task.Run(async () =>
        {
            try
            {
                // Create mesh
                _simulationMessage = "Generating mesh...";
                var mesh = GeothermalMeshGenerator.GenerateCylindricalMesh(_options.BoreholeDataset, _options);
                
                // Create solver
                var progress = new Progress<(float progress, string message)>(update =>
                {
                    _simulationProgress = update.progress;
                    _simulationMessage = update.message;
                });
                
                var solver = new GeothermalSimulationSolver(_options, mesh, progress, _cancellationTokenSource.Token);
                
                // Run simulation
                _results = await solver.RunSimulationAsync();
                
                _isSimulationRunning = false;
                _showResults = true;
            }
            catch (OperationCanceledException)
            {
                _simulationMessage = "Simulation cancelled";
                _isSimulationRunning = false;
            }
            catch (Exception ex)
            {
                _simulationMessage = $"Error: {ex.Message}";
                _isSimulationRunning = false;
                Console.WriteLine($"Simulation error: {ex}");
            }
        });
    }
    
    private void ExportResults()
    {
        if (_results == null) return;
        
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"GeothermalResults_{DateTime.Now:yyyyMMdd_HHmmss}"
        );
        
        _results.ExportToCSV(basePath);
        
        // Also save the summary report
        File.WriteAllText($"{basePath}_report.txt", _results.GenerateSummaryReport());
        
        Console.WriteLine($"Results exported to: {basePath}");
    }
    
    private void GenerateReport()
    {
        // Create a detailed PDF report
        // ... (implement PDF generation)
    }
    
    private void UpdateVisualization()
    {
        _visualizationMeshes.Clear();
        
        if (_selectedIsosurface < _results.TemperatureIsosurfaces.Count)
        {
            _visualizationMeshes.Add(_results.TemperatureIsosurfaces[_selectedIsosurface]);
        }
        
        // Update the 3D viewer
        foreach (var viewer in ToolManager.GetTools<Mesh3DViewer>())
        {
            viewer.ClearMeshes();
            foreach (var mesh in _visualizationMeshes)
            {
                viewer.AddMesh(mesh);
            }
        }
    }
    
    private void ShowAllIsosurfaces()
    {
        _visualizationMeshes.Clear();
        _visualizationMeshes.AddRange(_results.TemperatureIsosurfaces);
        
        // Update viewer
        foreach (var viewer in ToolManager.GetTools<Mesh3DViewer>())
        {
            viewer.ClearMeshes();
            foreach (var mesh in _visualizationMeshes)
            {
                viewer.AddMesh(mesh);
            }
        }
    }
    
    private void ShowBoreholeMesh()
    {
        if (_results.BoreholeMesh != null)
        {
            _visualizationMeshes.Add(_results.BoreholeMesh);
            
            foreach (var viewer in ToolManager.GetTools<Mesh3DViewer>())
            {
                viewer.AddMesh(_results.BoreholeMesh);
            }
        }
    }
    
    private void ShowStreamlines()
    {
        if (!_results.Streamlines.Any()) return;
        
        // Convert streamlines to a line mesh
        var vertices = new List<Vector3>();
        var lines = new List<int[]>();
        
        foreach (var streamline in _results.Streamlines)
        {
            var startIdx = vertices.Count;
            vertices.AddRange(streamline);
            
            for (int i = 0; i < streamline.Count - 1; i++)
            {
                lines.Add(new[] { startIdx + i, startIdx + i + 1 });
            }
        }
        
        var streamlineMesh = Mesh3DDataset.CreateFromData(
            "Streamlines",
            Path.Combine(Path.GetTempPath(), "streamlines.obj"),
            vertices,
            lines,
            1.0f,
            "m"
        );
        
        _visualizationMeshes.Add(streamlineMesh);
        
        foreach (var viewer in ToolManager.GetTools<Mesh3DViewer>())
        {
            viewer.AddMesh(streamlineMesh);
        }
    }
    
    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}