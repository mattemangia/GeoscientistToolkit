// GeoscientistToolkit/Analysis/Geothermal/GeothermalSimulationTools.cs

using System.Numerics;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;


namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// ImGui tool for configuring and running geothermal simulations on borehole data.
/// </summary>
public class GeothermalSimulationTools : IDatasetTools
{
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
    
    // Export file dialog
    private ImGuiExportFileDialog _exportDialog = new ImGuiExportFileDialog("geothermal_export", "Export Geothermal Results");
   
    
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
        if (ImGui.DragFloat("Inlet Temperature", ref inletTemp, 0.5f, 0f, 50f, "%.1f °C"))
            _options.FluidInletTemperature = inletTemp + 273.15;
        
        float specificHeat = (float)_options.FluidSpecificHeat;
        if (ImGui.DragFloat("Specific Heat", ref specificHeat, 10f, 1000f, 6000f, "%.0f J/kg·K"))
            _options.FluidSpecificHeat = specificHeat;
        
        float density = (float)_options.FluidDensity;
        if (ImGui.DragFloat("Density", ref density, 10f, 500f, 1500f, "%.0f kg/m³"))
            _options.FluidDensity = density;
        
        float viscosity = (float)_options.FluidViscosity;
        if (ImGui.DragFloat("Viscosity", ref viscosity, 0.0001f, 0.0001f, 0.01f, "%.4f Pa·s"))
            _options.FluidViscosity = viscosity;
        
        float fluidCond = (float)_options.FluidThermalConductivity;
        if (ImGui.DragFloat("Fluid Conductivity", ref fluidCond, 0.01f, 0.1f, 2f, "%.2f W/m·K"))
            _options.FluidThermalConductivity = fluidCond;
    }
    
    private void RenderGroundPropertiesConfig()
    {
        ImGui.Text("Layer Properties:");
        ImGui.Separator();
        
        if (ImGui.BeginTable("LayerProperties", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Layer");
            ImGui.TableSetupColumn("k (W/m·K)");
            ImGui.TableSetupColumn("Cp (J/kg·K)");
            ImGui.TableSetupColumn("ρ (kg/m³)");
            ImGui.TableSetupColumn("φ");
            ImGui.TableSetupColumn("K (m²)");
            ImGui.TableHeadersRow();
            
            var layersToEdit = _options.LayerThermalConductivities.Keys.ToList();
            foreach (var layer in layersToEdit)
            {
                ImGui.TableNextRow();
                
                ImGui.TableSetColumnIndex(0);
                ImGui.Text(layer);
                
                ImGui.TableSetColumnIndex(1);
                float k = (float)_options.LayerThermalConductivities[layer];
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat($"##k_{layer}", ref k, 0.1f, 0.1f, 10f, "%.1f"))
                    _options.LayerThermalConductivities[layer] = k;
                
                ImGui.TableSetColumnIndex(2);
                float cp = (float)_options.LayerSpecificHeats.GetValueOrDefault(layer, 1000);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat($"##cp_{layer}", ref cp, 10f, 100f, 3000f, "%.0f"))
                    _options.LayerSpecificHeats[layer] = cp;
                
                ImGui.TableSetColumnIndex(3);
                float rho = (float)_options.LayerDensities.GetValueOrDefault(layer, 2500);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat($"##rho_{layer}", ref rho, 10f, 1000f, 5000f, "%.0f"))
                    _options.LayerDensities[layer] = rho;
                
                ImGui.TableSetColumnIndex(4);
                float phi = (float)_options.LayerPorosities.GetValueOrDefault(layer, 0.1);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat($"##phi_{layer}", ref phi, 0.01f, 0f, 1f, "%.2f"))
                    _options.LayerPorosities[layer] = phi;
                
                ImGui.TableSetColumnIndex(5);
                float perm = (float)_options.LayerPermeabilities.GetValueOrDefault(layer, 1e-14);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat($"##K_{layer}", ref perm, 1e-15f, 1e-18f, 1e-10f, "%.2e"))
                    _options.LayerPermeabilities[layer] = perm;
            }
            
            ImGui.EndTable();
        }
        
        ImGui.Separator();
        ImGui.Text("Add New Layer:");
        
        ImGui.InputText("Layer Name", ref _newLayerName, 100);
        ImGui.DragFloat("Conductivity", ref _newLayerConductivity, 0.1f, 0.1f, 10f, "%.1f W/m·K");
        ImGui.DragFloat("Specific Heat", ref _newLayerSpecificHeat, 10f, 100f, 3000f, "%.0f J/kg·K");
        ImGui.DragFloat("Density", ref _newLayerDensity, 10f, 1000f, 5000f, "%.0f kg/m³");
        ImGui.DragFloat("Porosity", ref _newLayerPorosity, 0.01f, 0f, 1f, "%.2f");
        ImGui.DragFloat("Permeability", ref _newLayerPermeability, 1e-15f, 1e-18f, 1e-10f, "%.2e m²");
        
        if (ImGui.Button("Add Layer") && !string.IsNullOrWhiteSpace(_newLayerName))
        {
            _options.LayerThermalConductivities[_newLayerName] = _newLayerConductivity;
            _options.LayerSpecificHeats[_newLayerName] = _newLayerSpecificHeat;
            _options.LayerDensities[_newLayerName] = _newLayerDensity;
            _options.LayerPorosities[_newLayerName] = _newLayerPorosity;
            _options.LayerPermeabilities[_newLayerName] = _newLayerPermeability;
            _newLayerName = "";
        }
    }
    
    private void RenderFlowConfig()
    {
        bool simulateGroundwaterFlow = _options.SimulateGroundwaterFlow;
        if (ImGui.Checkbox("Simulate Groundwater Flow", ref simulateGroundwaterFlow))
            _options.SimulateGroundwaterFlow = simulateGroundwaterFlow;
        
        if (_options.SimulateGroundwaterFlow)
        {
            ImGui.Separator();
            ImGui.Text("Regional Groundwater Flow:");
            
            var velocity = _options.GroundwaterVelocity;
            float vx = velocity.X * 1e6f; // Convert to µm/s for better UI scaling
            float vy = velocity.Y * 1e6f;
            float vz = velocity.Z * 1e6f;
            
            if (ImGui.DragFloat("Velocity X (µm/s)", ref vx, 0.1f, -100f, 100f, "%.1f"))
                _options.GroundwaterVelocity = new Vector3(vx / 1e6f, velocity.Y, velocity.Z);
            
            if (ImGui.DragFloat("Velocity Y (µm/s)", ref vy, 0.1f, -100f, 100f, "%.1f"))
                _options.GroundwaterVelocity = new Vector3(velocity.X, vy / 1e6f, velocity.Z);
            
            if (ImGui.DragFloat("Velocity Z (µm/s)", ref vz, 0.1f, -100f, 100f, "%.1f"))
                _options.GroundwaterVelocity = new Vector3(velocity.X, velocity.Y, vz / 1e6f);
            
            float gwTemp = (float)(_options.GroundwaterTemperature - 273.15);
            if (ImGui.DragFloat("Groundwater Temperature", ref gwTemp, 0.5f, 0f, 50f, "%.1f °C"))
                _options.GroundwaterTemperature = gwTemp + 273.15;
            
            float headTop = (float)_options.HydraulicHeadTop;
            if (ImGui.DragFloat("Hydraulic Head (Top)", ref headTop, 0.5f, -100f, 100f, "%.1f m"))
                _options.HydraulicHeadTop = headTop;
            
            float headBottom = (float)_options.HydraulicHeadBottom;
            if (ImGui.DragFloat("Hydraulic Head (Bottom)", ref headBottom, 0.5f, -100f, 100f, "%.1f m"))
                _options.HydraulicHeadBottom = headBottom;
                
            ImGui.Separator();
            ImGui.Text("Dispersion Properties:");
            
            float longDisp = (float)_options.LongitudinalDispersivity;
            if (ImGui.DragFloat("Longitudinal Dispersivity", ref longDisp, 0.01f, 0f, 10f, "%.2f m"))
                _options.LongitudinalDispersivity = longDisp;

            float transDisp = (float)_options.TransverseDispersivity;
            if (ImGui.DragFloat("Transverse Dispersivity", ref transDisp, 0.01f, 0f, 5f, "%.2f m"))
                _options.TransverseDispersivity = transDisp;
        }
        
        ImGui.Separator();
        
        bool simulateFractures = _options.SimulateFractures;
        if (ImGui.Checkbox("Simulate Fractures", ref simulateFractures))
            _options.SimulateFractures = simulateFractures;
        
        if (_options.SimulateFractures)
        {
            float fractureAperture = (float)(_options.FractureAperture * 1000);
            if (ImGui.DragFloat("Fracture Aperture", ref fractureAperture, 0.01f, 0.1f, 10f, "%.2f mm"))
                _options.FractureAperture = fractureAperture / 1000;
            
            float fracturePerm = (float)_options.FracturePermeability;
            if (ImGui.DragFloat("Fracture Permeability", ref fracturePerm, 1e-9f, 1e-12f, 1e-6f, "%.2e m²"))
                _options.FracturePermeability = fracturePerm;
        }
    }
    
    private void RenderBoundaryConfig()
    {
        ImGui.Text("Simulation Domain:");
        
        float domainRadius = (float)_options.DomainRadius;
        if (ImGui.DragFloat("Domain Radius", ref domainRadius, 1f, 10f, 500f, "%.0f m"))
            _options.DomainRadius = domainRadius;
        
        float domainExtension = (float)_options.DomainExtension;
        if (ImGui.DragFloat("Domain Extension", ref domainExtension, 1f, 0f, 100f, "%.0f m"))
            _options.DomainExtension = domainExtension;
        
        ImGui.Separator();
        ImGui.Text("Outer Boundary:");
        
        int outerBC = (int)_options.OuterBoundaryCondition;
        string[] bcTypes = { "Dirichlet", "Neumann", "Robin", "Adiabatic" };
        if (ImGui.Combo("Boundary Type##Outer", ref outerBC, bcTypes, bcTypes.Length))
            _options.OuterBoundaryCondition = (BoundaryConditionType)outerBC;
        
        if (_options.OuterBoundaryCondition == BoundaryConditionType.Dirichlet)
        {
            float outerTemp = (float)(_options.OuterBoundaryTemperature - 273.15);
            if (ImGui.DragFloat("Temperature##Outer", ref outerTemp, 0.5f, 0f, 50f, "%.1f °C"))
                _options.OuterBoundaryTemperature = outerTemp + 273.15;
        }
        else if (_options.OuterBoundaryCondition == BoundaryConditionType.Neumann)
        {
            float outerFlux = (float)_options.OuterBoundaryHeatFlux;
            if (ImGui.DragFloat("Heat Flux##Outer", ref outerFlux, 0.001f, -0.5f, 0.5f, "%.3f W/m²"))
                _options.OuterBoundaryHeatFlux = outerFlux;
        }
        
        ImGui.Separator();
        ImGui.Text("Top Boundary:");
        
        int topBC = (int)_options.TopBoundaryCondition;
        if (ImGui.Combo("Boundary Type##Top", ref topBC, bcTypes, bcTypes.Length))
            _options.TopBoundaryCondition = (BoundaryConditionType)topBC;
        
        if (_options.TopBoundaryCondition == BoundaryConditionType.Dirichlet)
        {
            float topTemp = (float)(_options.TopBoundaryTemperature - 273.15);
            if (ImGui.DragFloat("Temperature##Top", ref topTemp, 0.5f, 0f, 50f, "%.1f °C"))
                _options.TopBoundaryTemperature = topTemp + 273.15;
        }
        
        ImGui.Separator();
        ImGui.Text("Bottom Boundary:");
        
        int bottomBC = (int)_options.BottomBoundaryCondition;
        if (ImGui.Combo("Boundary Type##Bottom", ref bottomBC, bcTypes, bcTypes.Length))
            _options.BottomBoundaryCondition = (BoundaryConditionType)bottomBC;
        
        float geothermalFlux = (float)(_options.GeothermalHeatFlux * 1000);
        if (ImGui.DragFloat("Geothermal Heat Flux", ref geothermalFlux, 1f, 0f, 200f, "%.0f mW/m²"))
            _options.GeothermalHeatFlux = geothermalFlux / 1000;
    }
    
    private void RenderSolverConfig()
    {
        ImGui.Text("Time Settings:");
        
        float simTime = (float)(_options.SimulationTime / 86400);
        if (ImGui.DragFloat("Simulation Time", ref simTime, 1f, 1f, 3650f, "%.0f days"))
            _options.SimulationTime = simTime * 86400;
        
        float timeStep = (float)(_options.TimeStep / 3600);
        if (ImGui.DragFloat("Time Step", ref timeStep, 0.1f, 0.1f, 24f, "%.1f hours"))
            _options.TimeStep = timeStep * 3600;
        
        int saveInterval = _options.SaveInterval;
        if (ImGui.DragInt("Save Interval", ref saveInterval, 1, 1, 1000, "%d steps"))
            _options.SaveInterval = saveInterval;
        
        ImGui.Separator();
        ImGui.Text("Grid Resolution:");
        
        int radialGridPoints = _options.RadialGridPoints;
        if (ImGui.DragInt("Radial Points", ref radialGridPoints, 1, 10, 200))
            _options.RadialGridPoints = radialGridPoints;
        
        int angularGridPoints = _options.AngularGridPoints;
        if (ImGui.DragInt("Angular Points", ref angularGridPoints, 1, 8, 72))
            _options.AngularGridPoints = angularGridPoints;
        
        int verticalGridPoints = _options.VerticalGridPoints;
        if (ImGui.DragInt("Vertical Points", ref verticalGridPoints, 1, 20, 500))
            _options.VerticalGridPoints = verticalGridPoints;
        
        ImGui.Separator();
        ImGui.Text("Solver Settings:");
        
        float tolerance = (float)Math.Log10(_options.ConvergenceTolerance);
        if (ImGui.DragFloat("Convergence Tolerance", ref tolerance, 0.1f, -10f, -3f, "1e%.0f"))
            _options.ConvergenceTolerance = Math.Pow(10, tolerance);
        
        int maxIter = _options.MaxIterationsPerStep;
        if (ImGui.DragInt("Max Iterations", ref maxIter, 10, 100, 10000))
            _options.MaxIterationsPerStep = maxIter;
        
        bool useSIMD = _options.UseSIMD;
        if (ImGui.Checkbox("Use SIMD Optimizations", ref useSIMD))
            _options.UseSIMD = useSIMD;
        
        bool useGPU = _options.UseGPU;
        if (ImGui.Checkbox("Use GPU Acceleration", ref useGPU))
            _options.UseGPU = useGPU;
        
        if (_options.UseGPU)
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "GPU acceleration not yet implemented");
        }
        
        ImGui.Separator();
        ImGui.Text("Performance Calculation:");

        float hvacTemp = (float)((_options.HvacSupplyTemperatureKelvin ?? 308.15) - 273.15);
        if (ImGui.DragFloat("HVAC Supply Temperature", ref hvacTemp, 0.5f, 5f, 60f, "%.1f °C"))
            _options.HvacSupplyTemperatureKelvin = hvacTemp + 273.15;
            
        float compressorEff = (float)(_options.CompressorIsentropicEfficiency ?? 0.6) * 100f;
        if (ImGui.DragFloat("Compressor Efficiency", ref compressorEff, 1f, 30f, 95f, "%.0f %%"))
            _options.CompressorIsentropicEfficiency = compressorEff / 100.0;
    }
    
    private void RenderVisualizationConfig()
    {
        ImGui.Text("3D Visualization:");
        
        bool generate3D = _options.Generate3DIsosurfaces;
        if (ImGui.Checkbox("Generate 3D Isosurfaces", ref generate3D))
            _options.Generate3DIsosurfaces = generate3D;
        
        if (_options.Generate3DIsosurfaces)
        {
            ImGui.Text("Isosurface Temperatures:");
            
            for (int i = 0; i < _options.IsosurfaceTemperatures.Count; i++)
            {
                float temp = (float)(_options.IsosurfaceTemperatures[i] - 273.15);
                ImGui.PushID(i);
                if (ImGui.DragFloat("##iso", ref temp, 0.5f, 0f, 100f, "%.1f °C"))
                    _options.IsosurfaceTemperatures[i] = temp + 273.15;
                ImGui.SameLine();
                if (ImGui.Button("X"))
                {
                    _options.IsosurfaceTemperatures.RemoveAt(i);
                    i--;
                }
                ImGui.PopID();
            }
            
            ImGui.DragFloat("New Isosurface", ref _newIsosurfaceTemp, 0.5f, 0f, 100f, "%.1f °C");
            ImGui.SameLine();
            if (ImGui.Button("Add"))
            {
                _options.IsosurfaceTemperatures.Add(_newIsosurfaceTemp + 273.15);
            }
        }
        
        ImGui.Separator();
        
        bool generateStreamlines = _options.GenerateStreamlines;
        if (ImGui.Checkbox("Generate Streamlines", ref generateStreamlines))
            _options.GenerateStreamlines = generateStreamlines;
        
        if (_options.GenerateStreamlines)
        {
            int streamlineCount = _options.StreamlineCount;
            if (ImGui.DragInt("Streamline Count", ref streamlineCount, 1, 10, 200))
                _options.StreamlineCount = streamlineCount;
        }
        
        ImGui.Separator();
        
        bool generate2D = _options.Generate2DSlices;
        if (ImGui.Checkbox("Generate 2D Slices", ref generate2D))
            _options.Generate2DSlices = generate2D;
    }
    
    private void RenderSimulationProgress()
    {
        ImGui.Text("Simulation Running...");
        ImGui.ProgressBar(_simulationProgress, new Vector2(-1, 0), $"{_simulationProgress * 100:F0}%");
        ImGui.Text(_simulationMessage);
        
        ImGui.Spacing();
        
        if (ImGui.Button("Cancel Simulation"))
        {
            _cancellationTokenSource?.Cancel();
        }
    }
    
    private void RenderResults()
    {
        if (ImGui.Button("< Back to Configuration"))
        {
            _showResults = false;
            return;
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Export Results"))
        {
            _exportDialog.SetExtensions((".csv", "CSV Files"), (".txt", "Text Files"));
            _exportDialog.Open("geothermal_results", null);
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
            
            if (ImGui.BeginTabItem("Performance"))
            {
                RenderPerformanceResults();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Temperature"))
            {
                RenderTemperatureResults();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Flow"))
            {
                RenderFlowResults();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Layers"))
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
    
    private void RenderPerformanceResults()
    {
        ImGui.Text("Thermal Performance:");
        ImGui.Separator();
        
        ImGui.Text($"Average Heat Extraction: {_results.AverageHeatExtractionRate:F0} W");
        ImGui.Text($"Total Energy Extracted: {_results.TotalExtractedEnergy / 1e9:F2} GJ");

        if (_results.CoefficientOfPerformance.Any())
        {
            double averageCop = _results.CoefficientOfPerformance.Select(item => item.cop).Where(c => !double.IsInfinity(c)).Average();
            ImGui.Text($"Average Coefficient of Performance: {averageCop:F2}");
        }

        ImGui.Text($"Borehole Thermal Resistance: {_results.BoreholeThermalResistance:F3} m·K/W");
        ImGui.Text($"Effective Ground Conductivity: {_results.EffectiveGroundConductivity:F2} W/m·K");
        ImGui.Text($"Thermal Influence Radius: {_results.ThermalInfluenceRadius:F1} m");
        
        ImGui.Spacing();
        ImGui.Text("Computational Performance:");
        ImGui.Separator();
        
        ImGui.Text($"Computation Time: {_results.ComputationTime.TotalMinutes:F1} minutes");
        ImGui.Text($"Time Steps: {_results.TimeStepsComputed}");
        ImGui.Text($"Average Iterations/Step: {_results.AverageIterationsPerStep:F1}");
        ImGui.Text($"Final Convergence Error: {_results.FinalConvergenceError:E2}");
        ImGui.Text($"Peak Memory Usage: {_results.PeakMemoryUsage:F0} MB");
    }
    
    private void RenderTemperatureResults()
    {
        if (_results.FluidTemperatureProfile.Any())
        {
            ImGui.Text("Fluid Temperature Profile:");
            
            if (ImGui.BeginTable("TempProfile", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Depth (m)");
                ImGui.TableSetupColumn("Down (°C)");
                ImGui.TableSetupColumn("Up (°C)");
                ImGui.TableHeadersRow();
                
                foreach (var point in _results.FluidTemperatureProfile)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text($"{point.depth:F1}");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text($"{point.temperatureDown - 273.15:F2}");
                    ImGui.TableSetColumnIndex(2);
                    ImGui.Text($"{point.temperatureUp - 273.15:F2}");
                }
                
                ImGui.EndTable();
            }
        }
        
        ImGui.Separator();
        
        if (_results.OutletTemperature.Any())
        {
            ImGui.Text("Outlet Temperature Over Time:");
            var lastTemp = _results.OutletTemperature.Last();
            ImGui.Text($"Final Outlet Temperature: {lastTemp.temperature - 273.15:F1} °C");
            ImGui.Text($"at t = {lastTemp.time / 86400:F1} days");
        }
    }
    
    private void RenderFlowResults()
    {
        if (_options.SimulateGroundwaterFlow)
        {
            ImGui.Text("Groundwater Flow Analysis:");
            ImGui.Separator();
            
            ImGui.Text($"Average Péclet Number: {_results.AveragePecletNumber:F2}");
            ImGui.Text($"Longitudinal Dispersivity: {_results.LongitudinalDispersivity:F3} m");
            ImGui.Text($"Transverse Dispersivity: {_results.TransverseDispersivity:F3} m");
            ImGui.Text($"Pressure Drawdown: {_results.PressureDrawdown:F0} Pa");
            
            ImGui.Spacing();
            
            if (_results.Streamlines.Any())
            {
                ImGui.Text($"Generated {_results.Streamlines.Count} streamlines");
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Groundwater flow not simulated");
        }
    }
    
    private void RenderLayerResults()
    {
        ImGui.Text("Layer Contributions:");
        ImGui.Separator();
        
        if (ImGui.BeginTable("LayerResults", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Layer");
            ImGui.TableSetupColumn("Heat Flux (%)");
            ImGui.TableSetupColumn("Temp Change (K)");
            ImGui.TableSetupColumn("Flow Rate (m³/s)");
            ImGui.TableHeadersRow();
            
            foreach (var layer in _results.LayerHeatFluxContributions.OrderByDescending(l => l.Value))
            {
                ImGui.TableNextRow();
                
                ImGui.TableSetColumnIndex(0);
                ImGui.Text(layer.Key);
                
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{layer.Value:F1}");
                
                ImGui.TableSetColumnIndex(2);
                var tempChange = _results.LayerTemperatureChanges.GetValueOrDefault(layer.Key, 0);
                ImGui.Text($"{tempChange:F2}");
                
                ImGui.TableSetColumnIndex(3);
                var flowRate = _results.LayerFlowRates.GetValueOrDefault(layer.Key, 0);
                ImGui.Text($"{flowRate:E2}");
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
            ImGui.Text($"{_results.TemperatureSlices.Count} slices available");
        }
    }
    
    private void InitializeLayerProperties(BoreholeDataset boreholeDataset)
    {
        // Initialize properties for layers in the borehole
        foreach (var layer in boreholeDataset.LithologyUnits)
        {
            var layerName = layer.LithologyType ?? "Unknown";
            
            if (!_options.LayerThermalConductivities.ContainsKey(layerName))
            {
                // Use defaults if available
                _options.SetDefaultValues();
            }
        }
    }
    
    private void StartSimulation()
    {
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
                var mesh = GeothermalMeshGenerator.GenerateCylindricalMesh(_options.BoreholeDataset, _options);
                Logger.Log("Mesh generation complete.");
                
                // Create solver
                var progress = new Progress<(float progress, string message)>(update =>
                {
                    _simulationProgress = update.progress;
                    _simulationMessage = update.message;
                });
                
                Logger.Log("Initializing simulation solver...");
                var solver = new GeothermalSimulationSolver(_options, mesh, progress, _cancellationTokenSource.Token);
                
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
            Logger.Log($"Exporting simulation results to '{basePath}'...");
            _results.ExportToCSV(basePath);
            
            // Also save the summary report
            var reportPath = Path.ChangeExtension(basePath, ".txt");
            File.WriteAllText(reportPath, _results.GenerateSummaryReport());
            
            Logger.Log($"Results successfully exported to: {basePath} and {reportPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export results: {ex.Message}");
        }
    }
    
    private void GenerateReport()
    {
        if (_results == null)
        {
            Logger.LogWarning("Cannot generate report, no simulation results available.");
            return;
        }

        try
        {
            // Create a detailed report with a more descriptive name
            var reportPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"GeothermalReport_{_options.BoreholeDataset.WellName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            );
            
            Logger.Log($"Generating summary report at: {reportPath}");
            File.WriteAllText(reportPath, _results.GenerateSummaryReport());
            Logger.Log("Summary report generated successfully.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to generate report: {ex.Message}");
        }
    }
    
    private void UpdateVisualization()
    {
        // Update visualization would need access to a 3D viewer
        // This is a placeholder for the actual implementation
        _visualizationMeshes.Clear();
        
        if (_selectedIsosurface < _results.TemperatureIsosurfaces.Count)
        {
            _visualizationMeshes.Add(_results.TemperatureIsosurfaces[_selectedIsosurface]);
        }
    }
    
    private void ShowAllIsosurfaces()
    {
        _visualizationMeshes.Clear();
        _visualizationMeshes.AddRange(_results.TemperatureIsosurfaces);
    }
    
    private void ShowBoreholeMesh()
    {
        if (_results.BoreholeMesh != null)
        {
            _visualizationMeshes.Add(_results.BoreholeMesh);
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
    }
}