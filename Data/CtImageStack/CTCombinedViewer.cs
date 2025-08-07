// GeoscientistToolkit/Data/CtImageStack/CtCombinedViewer.cs
using System;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.AddIns;
using GeoscientistToolkit.AddIns.CtSimulation;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Combined viewer showing 3 orthogonal slices + 3D volume rendering
    /// Enhanced with cutting plane visualization, real-time segmentation preview, and simulation support
    /// </summary>
    public class CtCombinedViewer : IDatasetViewer, IDisposable
    {
        private readonly CtImageStackDataset _dataset;
        private StreamingCtVolumeDataset _streamingDataset;

        public CtVolume3DViewer VolumeViewer { get; private set; }

        private readonly CtRenderingPanel _renderingPanel;
        private bool _renderingPanelOpen = true;
        private bool _isInitialized = false;
        private static ProgressBarDialog _progressDialog = new ProgressBarDialog("Loading 3D Viewer");

        // View mode enum
        public enum ViewModeEnum { Combined, SlicesOnly, VolumeOnly, XYOnly, XZOnly, YZOnly }
        private ViewModeEnum _viewMode = ViewModeEnum.Combined;
        public ViewModeEnum ViewMode
        {
            get => _viewMode;
            set => _viewMode = value;
        }

        // Slice positions - now public properties
        private int _sliceX;
        private int _sliceY;
        private int _sliceZ;

        public int SliceX
        {
            get => _sliceX;
            set { _sliceX = Math.Clamp(value, 0, _dataset.Width - 1); _needsUpdateYZ = true; UpdateVolumeViewerSlices(); }
        }
        public int SliceY
        {
            get => _sliceY;
            set { _sliceY = Math.Clamp(value, 0, _dataset.Height - 1); _needsUpdateXZ = true; UpdateVolumeViewerSlices(); }
        }
        public int SliceZ
        {
            get => _sliceZ;
            set { _sliceZ = Math.Clamp(value, 0, _dataset.Depth - 1); _needsUpdateXY = true; UpdateVolumeViewerSlices(); }
        }

        // Textures for each view
        private TextureManager _textureXY;
        private TextureManager _textureXZ;
        private TextureManager _textureYZ;
        private bool _needsUpdateXY = true;
        private bool _needsUpdateXZ = true;
        private bool _needsUpdateYZ = true;

        // Window/Level - public properties
        private float _windowLevel = 128;
        private float _windowWidth = 255;
        public float WindowLevel
        {
            get => _windowLevel;
            set { _windowLevel = value; _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true; }
        }
        public float WindowWidth
        {
            get => _windowWidth;
            set { _windowWidth = value; _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true; }
        }

        // View settings - public properties
        public bool ShowCrosshairs { get; set; } = true;
        public bool SyncViews { get; set; } = true;
        public bool ShowScaleBar { get; set; } = true;
        public bool ShowCuttingPlanes { get; set; } = true;

        // Volume rendering settings
        public bool ShowVolumeData { get; set; } = true;
        public float VolumeStepSize
        {
            get => VolumeViewer?.StepSize ?? 1.0f;
            set { if (VolumeViewer != null) VolumeViewer.StepSize = value; }
        }
        public float MinThreshold
        {
            get => VolumeViewer?.MinThreshold ?? 0.1f;
            set { if (VolumeViewer != null) VolumeViewer.MinThreshold = value; }
        }
        public float MaxThreshold
        {
            get => VolumeViewer?.MaxThreshold ?? 1.0f;
            set { if (VolumeViewer != null) VolumeViewer.MaxThreshold = value; }
        }
        public int ColorMapIndex
        {
            get => VolumeViewer?.ColorMapIndex ?? 0;
            set { if (VolumeViewer != null) VolumeViewer.ColorMapIndex = value; }
        }

        // Material visibility and opacity tracking
        private Dictionary<byte, bool> _materialVisibility = new Dictionary<byte, bool>();
        private Dictionary<byte, float> _materialOpacity = new Dictionary<byte, float>();

        // Zoom and pan for each view
        private float _zoomXY = 1.0f;
        private float _zoomXZ = 1.0f;
        private float _zoomYZ = 1.0f;
        private Vector2 _panXY = Vector2.Zero;
        private Vector2 _panXZ = Vector2.Zero;
        private Vector2 _panYZ = Vector2.Zero;

        // Track if we're in a popped-out window
        private bool _isPoppedOut = false;
        private readonly CtSegmentationIntegration _interactiveSegmentation;

        // ===== SIMULATION INTEGRATION =====
        private bool _simulationPanelOpen = false;
        private readonly Dictionary<string, ICtSimulationAddIn> _availableSimulations = new();
        private readonly Dictionary<string, ICtSimulationProcessor> _activeProcessors = new();
        private readonly Dictionary<string, SimulationResult> _simulationResults = new();
        private readonly Dictionary<string, bool> _simulationPanelExpanded = new();
        private readonly Dictionary<string, SimulationParameters> _simulationParameters = new();
        private string _activeSimulationOverlay = null;
        private float _overlayOpacity = 0.5f;
        private bool _showSimulationOverlay = false;

        public CtCombinedViewer(CtImageStackDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));

            _dataset.Load();

            CtSegmentationIntegration.Initialize(_dataset);
            _interactiveSegmentation = CtSegmentationIntegration.GetInstance(_dataset);

            _renderingPanel = new CtRenderingPanel(this, _dataset);
            _renderingPanelOpen = true;

            if (_dataset.VolumeData == null)
            {
                Logger.LogWarning("[CtCombinedViewer] VolumeData not ready yet; viewer will update once loaded.");
            }
            else
            {
                Logger.Log($"[CtCombinedViewer] Dataset dimensions: {_dataset.Width}×{_dataset.Height}×{_dataset.Depth}");
            }

            _sliceX = _dataset.Width / 2;
            _sliceY = _dataset.Height / 2;
            _sliceZ = _dataset.Depth / 2;

            foreach (var material in _dataset.Materials)
            {
                _materialVisibility[material.ID] = material.IsVisible;
                _materialOpacity[material.ID] = 1.0f;
            }

            ProjectManager.Instance.DatasetDataChanged += OnDatasetDataChanged;
            CtImageStackTools.PreviewChanged += OnPreviewChanged;

            // Initialize simulation support
            InitializeSimulationSupport();

            _ = InitializeAsync();
        }

        private void InitializeSimulationSupport()
        {
            try
            {
                // Check for available simulation add-ins through AddInManager
                var addInManager = AddInManager.Instance;
                
                // Subscribe to add-in events
                addInManager.AddInLoaded += OnAddInLoaded;
                addInManager.AddInUnloaded += OnAddInUnloaded;
                
                // Check already loaded add-ins
                foreach (var addIn in addInManager.LoadedAddIns.Values)
                {
                    if (addIn is ICtSimulationAddIn ctSimulation)
                    {
                        RegisterSimulation(ctSimulation);
                    }
                }

                if (_availableSimulations.Count > 0)
                {
                    Logger.Log($"[CtCombinedViewer] Found {_availableSimulations.Count} simulation add-ins");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[CtCombinedViewer] Simulation support initialization: {ex.Message}");
            }
        }

        private void OnAddInLoaded(IAddIn addIn)
        {
            if (addIn is ICtSimulationAddIn ctSimulation)
            {
                RegisterSimulation(ctSimulation);
            }
        }

        private void OnAddInUnloaded(IAddIn addIn)
        {
            if (addIn is ICtSimulationAddIn ctSimulation)
            {
                UnregisterSimulation(ctSimulation);
            }
        }

        private void RegisterSimulation(ICtSimulationAddIn simulation)
        {
            _availableSimulations[simulation.Id] = simulation;
            _simulationPanelExpanded[simulation.Id] = false;
    
            // Create default parameters for this simulation
            try
            {
                var processor = simulation.CreateProcessor(_dataset);
                if (processor != null)
                {
                    var defaultParams = processor.CreateDefaultParameters() ?? new GenericSimulationParameters();
                    _simulationParameters[simulation.Id] = defaultParams;
                    processor.Dispose();
                }
                else
                {
                    _simulationParameters[simulation.Id] = new GenericSimulationParameters();
                }
            }
            catch
            {
                _simulationParameters[simulation.Id] = new GenericSimulationParameters();
            }
    
            Logger.Log($"[CtCombinedViewer] Registered simulation: {simulation.Name}");
        }

        private void UnregisterSimulation(ICtSimulationAddIn simulation)
        {
            // Clean up any active processor
            if (_activeProcessors.TryGetValue(simulation.Id, out var processor))
            {
                processor.Dispose();
                _activeProcessors.Remove(simulation.Id);
            }
            
            _availableSimulations.Remove(simulation.Id);
            _simulationResults.Remove(simulation.Id);
            _simulationPanelExpanded.Remove(simulation.Id);
            _simulationParameters.Remove(simulation.Id);
            
            if (_activeSimulationOverlay == simulation.Id)
            {
                _activeSimulationOverlay = null;
                _showSimulationOverlay = false;
            }
        }

        private void OnDatasetDataChanged(Dataset dataset)
        {
            if (dataset == _dataset)
            {
                _needsUpdateXY = true;
                _needsUpdateXZ = true;
                _needsUpdateYZ = true;
            }
        }

        private void OnPreviewChanged(CtImageStackDataset dataset)
        {
            if (dataset == _dataset)
            {
                _needsUpdateXY = true;
                _needsUpdateXZ = true;
                _needsUpdateYZ = true;
            }
        }

        public void SetPoppedOutState(bool isPoppedOut)
        {
            _isPoppedOut = isPoppedOut;
        }

        private async Task InitializeAsync()
        {
            await Task.Run(() =>
            {
                _progressDialog.Update(0.1f, "Finding streaming dataset...");
                _streamingDataset = FindStreamingDataset();

                if (_streamingDataset != null)
                {
                    _progressDialog.Update(0.5f, "Creating 3D volume viewer...");
                    VolumeViewer = new CtVolume3DViewer(_streamingDataset);

                    if (VolumeViewer != null)
                    {
                        _progressDialog.Update(0.8f, "Configuring volume renderer...");
                        VolumeViewer.ShowGrayscale = ShowVolumeData;
                        VolumeViewer.StepSize = 2.0f;
                        VolumeViewer.MinThreshold = 0.05f;
                        VolumeViewer.MaxThreshold = 0.8f;
                        UpdateVolumeViewerSlices();
                    }
                }

                _progressDialog.Update(1.0f, "Complete!");
            });

            _isInitialized = true;
            _progressDialog.Close();
        }

        private StreamingCtVolumeDataset FindStreamingDataset()
        {
            foreach (var dataset in ProjectManager.Instance.LoadedDatasets)
            {
                if (dataset is StreamingCtVolumeDataset streaming && streaming.EditablePartner == _dataset)
                {
                    return streaming;
                }
            }
            return null;
        }

        public void DrawToolbarControls()
        {
            // Simulation button in toolbar
            if (_availableSimulations.Count > 0)
            {
                if (ImGui.Button("🧪 Simulations"))
                {
                    _simulationPanelOpen = !_simulationPanelOpen;
                }
                ImGui.SameLine();
                
                if (_activeProcessors.Count > 0)
                {
                    ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), 
                        $"({_activeProcessors.Count} active)");
                    ImGui.SameLine();
                }
            }
            
            ImGui.Dummy(new Vector2(0, 0));
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            if (!_isPoppedOut && _renderingPanel != null)
            {
                _renderingPanel.Submit(ref _renderingPanelOpen);
            }

            // Draw simulation panel if open
            if (_simulationPanelOpen && _availableSimulations.Count > 0)
            {
                DrawSimulationPanel();
            }

            if (!_isInitialized)
            {
                _progressDialog.Submit();
                ImGui.Text("Loading 3D viewer…");
                return;
            }

            switch (_viewMode)
            {
                case ViewModeEnum.Combined:
                    DrawCombinedView();
                    break;
                case ViewModeEnum.SlicesOnly:
                    DrawSlicesOnlyView();
                    break;
                case ViewModeEnum.VolumeOnly:
                    if (VolumeViewer != null)
                    {
                        VolumeViewer.DrawContent(ref zoom, ref pan);
                    }
                    else
                    {
                        ImGui.Text("No 3-D volume dataset available.");
                        ImGui.TextWrapped("Import with the Optimized for 3D option to enable volume rendering.");
                    }
                    break;
                case ViewModeEnum.XYOnly:
                    DrawSliceView(0, "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
                    break;
                case ViewModeEnum.XZOnly:
                    DrawSliceView(1, "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
                    break;
                case ViewModeEnum.YZOnly:
                    DrawSliceView(2, "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
                    break;
            }

            if (ImGui.BeginPopupContextWindow("ViewerContextMenu"))
            {
                if (ImGui.MenuItem("Open Rendering Panel", null, _renderingPanelOpen))
                    _renderingPanelOpen = true;
                    
                if (_availableSimulations.Count > 0 && ImGui.MenuItem("Open Simulation Panel", null, _simulationPanelOpen))
                    _simulationPanelOpen = true;

                ImGui.Separator();

                if (ImGui.MenuItem("Reset Views")) ResetAllViews();

                if (_viewMode != ViewModeEnum.VolumeOnly &&
                    ImGui.MenuItem("Center Slices"))
                {
                    _sliceX = _dataset.Width / 2;
                    _sliceY = _dataset.Height / 2;
                    _sliceZ = _dataset.Depth / 2;
                    _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Combined View", null, _viewMode == ViewModeEnum.Combined)) _viewMode = ViewModeEnum.Combined;
                if (ImGui.MenuItem("Slices Only", null, _viewMode == ViewModeEnum.SlicesOnly)) _viewMode = ViewModeEnum.SlicesOnly;
                if (ImGui.MenuItem("3D Only", null, _viewMode == ViewModeEnum.VolumeOnly)) _viewMode = ViewModeEnum.VolumeOnly;

                ImGui.EndPopup();
            }

            if (_isPoppedOut && _renderingPanelOpen && _renderingPanel != null)
            {
                ImGui.Separator();
                if (ImGui.CollapsingHeader("Rendering Controls", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    _renderingPanel.DrawContentInline();
                }
            }
        }

        private void DrawSimulationPanel()
        {
            ImGui.SetNextWindowSize(new Vector2(400, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("CT Simulation Tools", ref _simulationPanelOpen))
            {
                ImGui.Text($"Dataset: {_dataset.Name}");
                ImGui.Text($"Dimensions: {_dataset.Width}×{_dataset.Height}×{_dataset.Depth}");
                ImGui.Separator();

                // Overlay controls
                if (_simulationResults.Count > 0)
                {
                    ImGui.Text("Visualization:");
                    ImGui.Checkbox("Show Overlay", ref _showSimulationOverlay);
                    
                    if (_showSimulationOverlay)
                    {
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(100);
                        ImGui.SliderFloat("Opacity", ref _overlayOpacity, 0.0f, 1.0f);
                        
                        ImGui.SetNextItemWidth(200);
                        if (ImGui.BeginCombo("Active Overlay", _activeSimulationOverlay ?? "None"))
                        {
                            if (ImGui.Selectable("None", _activeSimulationOverlay == null))
                            {
                                _activeSimulationOverlay = null;
                            }
                            
                            foreach (var (simId, result) in _simulationResults)
                            {
                                if (ImGui.Selectable(simId, _activeSimulationOverlay == simId))
                                {
                                    _activeSimulationOverlay = simId;
                                    _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                                }
                            }
                            ImGui.EndCombo();
                        }
                    }
                    ImGui.Separator();
                }

                // List all available simulations
                ImGui.Text("Available Simulations:");
                
                foreach (var simulation in _availableSimulations.Values)
                {
                    bool isExpanded = _simulationPanelExpanded[simulation.Id];
                    var headerFlags = isExpanded ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
                    
                    if (ImGui.CollapsingHeader($"{simulation.Name}###{simulation.Id}", headerFlags))
                    {
                        _simulationPanelExpanded[simulation.Id] = true;
                        ImGui.Indent();
                        
                        // Simulation info
                        ImGui.TextWrapped(simulation.Description);
                        ImGui.Text($"Version: {simulation.Version}");
                        ImGui.Text($"Author: {simulation.Author}");
                        
                        // Capabilities
                        var caps = simulation.GetCapabilities();
                        ImGui.Text("Capabilities:");
                        ImGui.Indent();
                        if (caps.HasFlag(SimulationCapabilities.VolumeModification))
                            ImGui.BulletText("Volume Modification");
                        if (caps.HasFlag(SimulationCapabilities.MaterialAnalysis))
                            ImGui.BulletText("Material Analysis");
                        if (caps.HasFlag(SimulationCapabilities.FlowSimulation))
                            ImGui.BulletText("Flow Simulation");
                        if (caps.HasFlag(SimulationCapabilities.StructuralAnalysis))
                            ImGui.BulletText("Structural Analysis");
                        if (caps.HasFlag(SimulationCapabilities.RealTime))
                            ImGui.BulletText("Real-time Preview");
                        if (caps.HasFlag(SimulationCapabilities.RequiresGPU))
                            ImGui.BulletText("GPU Accelerated");
                        ImGui.Unindent();
                        
                        ImGui.Separator();
                        
                        // Check if processor is active
                        if (_activeProcessors.TryGetValue(simulation.Id, out var processor))
                        {
                            // Show active processor status
                            ImGui.Text($"Status: {processor.State}");
                            ImGui.ProgressBar(processor.Progress, new Vector2(-1, 0), $"{(processor.Progress * 100):F1}%");
                            
                            if (processor.State == SimulationState.Running)
                            {
                                if (ImGui.Button($"Pause###{simulation.Id}"))
                                {
                                    processor.Pause();
                                }
                                ImGui.SameLine();
                                if (ImGui.Button($"Cancel###{simulation.Id}"))
                                {
                                    CancelSimulation(simulation.Id);
                                }
                            }
                            else if (processor.State == SimulationState.Paused)
                            {
                                if (ImGui.Button($"Resume###{simulation.Id}"))
                                {
                                    processor.Resume();
                                }
                                ImGui.SameLine();
                                if (ImGui.Button($"Cancel###{simulation.Id}"))
                                {
                                    CancelSimulation(simulation.Id);
                                }
                            }
                        }
                        else
                        {
                            // Draw custom panels from the simulation
                            var panels = simulation.GetPanels();
                            if (panels != null)
                            {
                                foreach (var panel in panels)
                                {
                                    if (ImGui.TreeNode($"{panel.Title}###{simulation.Id}_{panel.Title}"))
                                    {
                                        panel.Draw();
                                        ImGui.TreePop();
                                    }
                                }
                            }
                            
                            // Parameter controls (if we have them)
                            if (_simulationParameters.TryGetValue(simulation.Id, out var parameters))
                            {
                                DrawSimulationParameters(simulation.Id, parameters);
                            }
                            
                            // Run button
                            if (ImGui.Button($"Run {simulation.Name}###{simulation.Id}"))
                            {
                                _ = RunSimulationAsync(simulation.Id);
                            }
                            
                            // Show results if available
                            if (_simulationResults.TryGetValue(simulation.Id, out var result))
                            {
                                ImGui.SameLine();
                                if (ImGui.Button($"Clear Results###{simulation.Id}"))
                                {
                                    _simulationResults.Remove(simulation.Id);
                                    if (_activeSimulationOverlay == simulation.Id)
                                    {
                                        _activeSimulationOverlay = null;
                                    }
                                    _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                                }
                                
                                ImGui.Separator();
                                ImGui.Text("Results:");
                                ImGui.Indent();
                                ImGui.Text($"Computed: {result.Timestamp:HH:mm:ss}");
                                ImGui.Text($"Time: {result.ComputationTime.TotalSeconds:F2}s");
                                
                                // Display metadata
                                foreach (var (key, value) in result.Metadata)
                                {
                                    ImGui.Text($"{key}: {value}");
                                }
                                
                                if (ImGui.Button($"Export###{simulation.Id}"))
                                {
                                    ExportSimulationResult(simulation.Id, result);
                                }
                                ImGui.Unindent();
                            }
                        }
                        
                        ImGui.Unindent();
                    }
                    else
                    {
                        _simulationPanelExpanded[simulation.Id] = false;
                    }
                }
                
                if (_availableSimulations.Count == 0)
                {
                    ImGui.TextWrapped("No simulation add-ins are currently loaded. Place simulation DLLs in the AddIns folder and restart.");
                }
            }
            ImGui.End();
        }

        private void DrawSimulationParameters(string simulationId, SimulationParameters parameters)
{
    ImGui.Text("Parameters:");
    ImGui.Indent();
    
    // Common parameters - use local variables for ImGui
    bool useFullVolume = parameters.UseFullVolume;
    if (ImGui.Checkbox($"Use Full Volume###{simulationId}", ref useFullVolume))
    {
        parameters.UseFullVolume = useFullVolume;
    }
    
    if (!parameters.UseFullVolume)
    {
        Vector3 roiStart = parameters.ROIStart;
        if (ImGui.DragFloat3($"ROI Start###{simulationId}", ref roiStart, 1.0f, 0, _dataset.Width))
        {
            parameters.ROIStart = roiStart;
        }
        
        Vector3 roiEnd = parameters.ROIEnd;
        if (ImGui.DragFloat3($"ROI End###{simulationId}", ref roiEnd, 1.0f, 0, _dataset.Width))
        {
            parameters.ROIEnd = roiEnd;
        }
    }
    
    int maxIterations = parameters.MaxIterations;
    if (ImGui.SliderInt($"Max Iterations###{simulationId}", ref maxIterations, 10, 10000))
    {
        parameters.MaxIterations = maxIterations;
    }
    
    float tolerance = parameters.Tolerance;
    if (ImGui.InputFloat($"Tolerance###{simulationId}", ref tolerance, 1e-8f, 1e-6f, "%.2e"))
    {
        parameters.Tolerance = tolerance;
    }
    
    bool enableGPU = parameters.EnableGPU;
    if (ImGui.Checkbox($"Enable GPU###{simulationId}", ref enableGPU))
    {
        parameters.EnableGPU = enableGPU;
    }
    
    // Custom parameters (use reflection for dynamic properties)
    var type = parameters.GetType();
    if (type != typeof(SimulationParameters) && type != typeof(GenericSimulationParameters))
    {
        ImGui.Separator();
        ImGui.Text("Specific Parameters:");
        
        foreach (var prop in type.GetProperties())
        {
            if (prop.DeclaringType == typeof(SimulationParameters))
                continue;
            
            // Check if property is writable
            if (!prop.CanWrite)
                continue;
                
            if (prop.PropertyType == typeof(float))
            {
                float value = (float)prop.GetValue(parameters);
                if (ImGui.InputFloat($"{prop.Name}###{simulationId}_{prop.Name}", ref value))
                {
                    prop.SetValue(parameters, value);
                }
            }
            else if (prop.PropertyType == typeof(int))
            {
                int value = (int)prop.GetValue(parameters);
                if (ImGui.InputInt($"{prop.Name}###{simulationId}_{prop.Name}", ref value))
                {
                    prop.SetValue(parameters, value);
                }
            }
            else if (prop.PropertyType == typeof(bool))
            {
                bool value = (bool)prop.GetValue(parameters);
                if (ImGui.Checkbox($"{prop.Name}###{simulationId}_{prop.Name}", ref value))
                {
                    prop.SetValue(parameters, value);
                }
            }
            else if (prop.PropertyType == typeof(Vector3))
            {
                Vector3 value = (Vector3)prop.GetValue(parameters);
                if (ImGui.DragFloat3($"{prop.Name}###{simulationId}_{prop.Name}", ref value))
                {
                    prop.SetValue(parameters, value);
                }
            }
            else if (prop.PropertyType == typeof(Vector2))
            {
                Vector2 value = (Vector2)prop.GetValue(parameters);
                if (ImGui.DragFloat2($"{prop.Name}###{simulationId}_{prop.Name}", ref value))
                {
                    prop.SetValue(parameters, value);
                }
            }
        }
    }
    
    ImGui.Unindent();
}
        private async Task RunSimulationAsync(string simulationId)
        {
            if (!_availableSimulations.TryGetValue(simulationId, out var simulation))
                return;
                
            try
            {
                var processor = simulation.CreateProcessor(_dataset);
                if (processor == null)
                {
                    Logger.LogError($"Failed to create processor for {simulationId}");
                    return;
                }
                
                _activeProcessors[simulationId] = processor;
                
                // Initialize processor
                processor.Initialize(_dataset.VolumeData, _dataset.LabelData);
                
                // Subscribe to events
                processor.ProgressChanged += progress =>
                {
                    // Progress is handled in UI
                };
                
                processor.IntermediateResultAvailable += result =>
                {
                    _simulationResults[simulationId] = result;
                    if (_showSimulationOverlay && _activeSimulationOverlay == simulationId)
                    {
                        _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                    }
                };
                
                // Get parameters
                var parameters = _simulationParameters[simulationId] ?? new GenericSimulationParameters();
                
                // Run simulation
                var result = await processor.RunAsync(parameters);
                
                // Store result
                _simulationResults[simulationId] = result;
                
                // Update display
                if (_showSimulationOverlay && _activeSimulationOverlay == simulationId)
                {
                    _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                }
                
                Logger.Log($"Simulation {simulationId} completed successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Simulation {simulationId} failed: {ex.Message}");
            }
            finally
            {
                if (_activeProcessors.TryGetValue(simulationId, out var processor))
                {
                    processor.Dispose();
                    _activeProcessors.Remove(simulationId);
                }
            }
        }

        private void CancelSimulation(string simulationId)
        {
            if (_activeProcessors.TryGetValue(simulationId, out var processor))
            {
                processor.Dispose();
                _activeProcessors.Remove(simulationId);
                Logger.Log($"Simulation {simulationId} cancelled");
            }
        }

        private void ExportSimulationResult(string simulationId, SimulationResult result)
        {
            // Open file dialog for export
            var filename = $"{_dataset.Name}_{simulationId}_{DateTime.Now:yyyyMMdd_HHmmss}.dat";
            // TODO: Use file dialog
            result.Export(filename);
            Logger.Log($"Exported simulation results to {filename}");
        }

        // Rest of the existing methods remain the same...
        
        public void ZoomAllViews(float factor)
        {
            _zoomXY = Math.Clamp(_zoomXY * factor, 0.1f, 10.0f);
            _zoomXZ = Math.Clamp(_zoomXZ * factor, 0.1f, 10.0f);
            _zoomYZ = Math.Clamp(_zoomYZ * factor, 0.1f, 10.0f);
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        }

        public void FitToWindow()
        {
            _zoomXY = _zoomXZ = _zoomYZ = 1.0f;
            _panXY = _panXZ = _panYZ = Vector2.Zero;
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
        }

        public bool GetMaterialVisibility(byte id)
        {
            if (_materialVisibility.TryGetValue(id, out bool visible))
                return visible;
            return false;
        }

        public void SetMaterialVisibility(byte id, bool visible)
        {
            _materialVisibility[id] = visible;
            var material = _dataset.Materials.FirstOrDefault(m => m.ID == id);
            if (material != null)
            {
                material.IsVisible = visible;
                _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;

                VolumeViewer?.SetMaterialVisibility(id, visible);
            }
        }

        public float GetMaterialOpacity(byte id)
        {
            if (_materialOpacity.TryGetValue(id, out float opacity))
                return opacity;
            return 1.0f;
        }

        public void SetMaterialOpacity(byte id, float opacity)
        {
            _materialOpacity[id] = opacity;
            VolumeViewer?.SetMaterialOpacity(id, opacity);
        }

        public void ResetAllViews()
        {
            _sliceX = _dataset.Width / 2;
            _sliceY = _dataset.Height / 2;
            _sliceZ = _dataset.Depth / 2;
            _zoomXY = _zoomXZ = _zoomYZ = 1.0f;
            _panXY = _panXZ = _panYZ = Vector2.Zero;
            _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
            _windowLevel = 128;
            _windowWidth = 255;
            UpdateVolumeViewerSlices();
        }

        private void UpdateVolumeViewerSlices()
        {
            if (VolumeViewer != null && SyncViews)
            {
                VolumeViewer.SlicePositions = new Vector3(
                    Math.Clamp((float)_sliceX / Math.Max(1, _dataset.Width - 1), 0f, 1f),
                    Math.Clamp((float)_sliceY / Math.Max(1, _dataset.Height - 1), 0f, 1f),
                    Math.Clamp((float)_sliceZ / Math.Max(1, _dataset.Depth - 1), 0f, 1f)
                );

                VolumeViewer.ShowSlices = SyncViews;
            }
        }

        private void DrawCombinedView()
        {
            var availableSize = ImGui.GetContentRegionAvail();
            float viewWidth = (availableSize.X - 2) / 2;
            float viewHeight = (availableSize.Y - 2) / 2;

            ImGui.BeginChild("XY_View", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
            DrawSliceView(0, "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
            ImGui.EndChild();

            ImGui.SameLine(0, 2);

            ImGui.BeginChild("XZ_View", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
            DrawSliceView(1, "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
            ImGui.EndChild();

            ImGui.BeginChild("YZ_View", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
            DrawSliceView(2, "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
            ImGui.EndChild();

            ImGui.SameLine(0, 2);

            ImGui.BeginChild("3D_View", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
            if (VolumeViewer != null)
            {
                ImGui.Text("3D Volume");
                ImGui.Separator();
                var contentSize = ImGui.GetContentRegionAvail();
                ImGui.BeginChild("3D_Content", contentSize);
                var dummyZoom = 1.0f;
                var dummyPan = Vector2.Zero;

                VolumeViewer.ShowGrayscale = ShowVolumeData;

                VolumeViewer.DrawContent(ref dummyZoom, ref dummyPan);
                ImGui.EndChild();
            }
            else
            {
                ImGui.Text("3D Volume (Not Available)");
                ImGui.Separator();
                ImGui.TextWrapped("Import with 'Optimized for 3D' option to enable volume rendering.");
            }
            ImGui.EndChild();
        }

        private void DrawSlicesOnlyView()
        {
            var availableSize = ImGui.GetContentRegionAvail();
            float spacing = 4;
            float totalWidth = availableSize.X - spacing * 2;
            float viewWidth = totalWidth / 3;
            float viewHeight = availableSize.Y;

            if (viewWidth < 200)
            {
                viewWidth = availableSize.X;
                viewHeight = (availableSize.Y - spacing * 2) / 3;

                ImGui.BeginChild("XY_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(0, "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
                ImGui.EndChild();

                ImGui.Dummy(new Vector2(0, spacing));

                ImGui.BeginChild("XZ_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(1, "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
                ImGui.EndChild();

                ImGui.Dummy(new Vector2(0, spacing));

                ImGui.BeginChild("YZ_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(2, "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
                ImGui.EndChild();
            }
            else
            {
                ImGui.BeginChild("XY_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(0, "XY (Axial)", ref _zoomXY, ref _panXY, ref _needsUpdateXY, ref _textureXY);
                ImGui.EndChild();
                ImGui.SameLine(0, spacing);
                ImGui.BeginChild("XZ_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(1, "XZ (Coronal)", ref _zoomXZ, ref _panXZ, ref _needsUpdateXZ, ref _textureXZ);
                ImGui.EndChild();
                ImGui.SameLine(0, spacing);
                ImGui.BeginChild("YZ_SliceView", new Vector2(viewWidth, viewHeight), ImGuiChildFlags.Border);
                DrawSliceView(2, "YZ (Sagittal)", ref _zoomYZ, ref _panYZ, ref _needsUpdateYZ, ref _textureYZ);
                ImGui.EndChild();
            }
        }

        private void DrawSliceView(int viewIndex, string title, ref float zoom, ref Vector2 pan, ref bool needsUpdate, ref TextureManager texture)
        {
            ImGui.Text(title);
            ImGui.SameLine();
            int slice = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 };
            int maxSlice = viewIndex switch { 0 => _dataset.Depth - 1, 1 => _dataset.Height - 1, 2 => _dataset.Width - 1, _ => 0 };
            ImGui.SetNextItemWidth(120);
            if (ImGui.SliderInt($"##Slice{viewIndex}", ref slice, 0, maxSlice))
            {
                switch (viewIndex)
                {
                    case 0: SliceZ = slice; break;
                    case 1: SliceY = slice; break;
                    case 2: SliceX = slice; break;
                }
            }
            ImGui.SameLine();
            ImGui.Text($"{slice + 1}/{maxSlice + 1}");
            ImGui.Separator();

            var contentRegion = ImGui.GetContentRegionAvail();
            DrawSingleSlice(viewIndex, ref zoom, ref pan, ref needsUpdate, ref texture, contentRegion);
        }

        private (Vector2 pos, Vector2 size) GetImageDisplayMetrics(Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan, int imageWidth, int imageHeight)
        {
            float imageAspect = (float)imageWidth / imageHeight;
            float canvasAspect = canvasSize.X / canvasSize.Y;

            Vector2 imageDisplaySize;
            if (imageAspect > canvasAspect)
                imageDisplaySize = new Vector2(canvasSize.X, canvasSize.X / imageAspect);
            else
                imageDisplaySize = new Vector2(canvasSize.Y * imageAspect, canvasSize.Y);

            imageDisplaySize *= zoom;
            Vector2 imageDisplayPos = canvasPos + (canvasSize - imageDisplaySize) * 0.5f + pan;

            return (imageDisplayPos, imageDisplaySize);
        }

        private Vector2 GetMousePosInImage(Vector2 mousePos, Vector2 imageDisplayPos, Vector2 imageDisplaySize, int imageWidth, int imageHeight)
        {
            Vector2 mouseRelativeToImage = mousePos - imageDisplayPos;

            return new Vector2(
                (mouseRelativeToImage.X / imageDisplaySize.X) * imageWidth,
                (mouseRelativeToImage.Y / imageDisplaySize.Y) * imageHeight
            );
        }

        private void DrawSingleSlice(int viewIndex, ref float zoom, ref Vector2 pan, ref bool needsUpdate, ref TextureManager texture, Vector2 availableSize)
        {
            var io = ImGui.GetIO();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = availableSize;
            var dl = ImGui.GetWindowDrawList();

            ImGui.InvisibleButton($"canvas{viewIndex}", canvasSize);
            bool isHovered = ImGui.IsItemHovered();

            var (width, height) = GetImageDimensionsForView(viewIndex);
            var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height);

            if (_interactiveSegmentation != null && isHovered)
            {
                var mousePosInImage = GetMousePosInImage(io.MousePos, imagePos, imageSize, width, height);

                _interactiveSegmentation.HandleMouseInput(
                    mousePosInImage,
                    viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => 0 },
                    viewIndex,
                    ImGui.IsItemClicked(ImGuiMouseButton.Left),
                    ImGui.IsMouseDragging(ImGuiMouseButton.Left),
                    ImGui.IsMouseReleased(ImGuiMouseButton.Left)
                );

                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) || ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    _needsUpdateXY = _needsUpdateXZ = _needsUpdateYZ = true;
                }
            }

            if (ImGui.BeginPopupContextItem($"SliceContext{viewIndex}"))
            {
                if (ImGui.MenuItem("Open Rendering Panel")) _renderingPanelOpen = true;
                if (_availableSimulations.Count > 0 && ImGui.MenuItem("Open Simulation Panel")) _simulationPanelOpen = true;
                ImGui.Separator();
                string sliceName = viewIndex switch { 0 => "Z", 1 => "Y", 2 => "X", _ => "" };
                if (ImGui.MenuItem($"Center {sliceName} Slice"))
                {
                    switch (viewIndex)
                    {
                        case 0: SliceZ = _dataset.Depth / 2; break;
                        case 1: SliceY = _dataset.Height / 2; break;
                        case 2: SliceX = _dataset.Width / 2; break;
                    }
                }
                if (ImGui.MenuItem("Reset Zoom")) { zoom = 1.0f; pan = Vector2.Zero; }
                ImGui.Separator();
                if (ImGui.MenuItem("Copy Position")) ImGui.SetClipboardText($"X:{_sliceX + 1} Y:{_sliceY + 1} Z:{_sliceZ + 1}");
                ImGui.Separator();
                bool showCuttingPlanes = ShowCuttingPlanes;
                if (ImGui.Checkbox("Show Cutting Planes", ref showCuttingPlanes)) ShowCuttingPlanes = showCuttingPlanes;
                ImGui.EndPopup();
            }

            if (isHovered && io.MouseWheel != 0)
            {
                float zoomDelta = io.MouseWheel * 0.1f;
                float newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
                if (newZoom != zoom)
                {
                    Vector2 mouseCanvasPos = io.MousePos - canvasPos - canvasSize * 0.5f;
                    pan -= mouseCanvasPos * (newZoom / zoom - 1.0f);
                    zoom = newZoom;
                    if (SyncViews) { _zoomXY = _zoomXZ = _zoomYZ = zoom; }
                }
            }

            if (isHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            {
                pan += io.MouseDelta;
                if (SyncViews) { _panXY = pan; _panXZ = pan; _panYZ = pan; }
            }

            if (isHovered && io.MouseWheel != 0 && io.KeyCtrl)
            {
                int wheel = (int)io.MouseWheel;
                switch (viewIndex)
                {
                    case 0: SliceZ = Math.Clamp(_sliceZ + wheel, 0, _dataset.Depth - 1); break;
                    case 1: SliceY = Math.Clamp(_sliceY + wheel, 0, _dataset.Height - 1); break;
                    case 2: SliceX = Math.Clamp(_sliceX + wheel, 0, _dataset.Width - 1); break;
                }
            }

            if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !(_interactiveSegmentation?.HasActiveSelection ?? false))
            {
                UpdateCrosshairFromMouse(viewIndex, canvasPos, canvasSize, zoom, pan);
            }

            dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);

            if (needsUpdate || texture == null || !texture.IsValid)
            {
                UpdateTexture(viewIndex, ref texture);
                needsUpdate = false;
            }

            dl.PushClipRect(canvasPos, canvasPos + canvasSize, true);

            if (texture != null && texture.IsValid)
            {
                // 1. Draw the composited texture (base image + materials + overlays)
                dl.AddImage(texture.GetImGuiTextureId(), imagePos, imagePos + imageSize, Vector2.Zero, Vector2.One, 0xFFFFFFFF);

                // 2. Draw UI elements on top
                if (ShowCrosshairs) DrawCrosshairs(dl, viewIndex, canvasPos, canvasSize, imagePos, imageSize, width, height);
                if (ShowScaleBar) DrawScaleBar(dl, canvasPos, canvasSize, zoom, width, height, viewIndex);
                if (ShowCuttingPlanes && VolumeViewer != null) DrawCuttingPlanes(dl, viewIndex, canvasPos, canvasSize, imagePos, imageSize, width, height);
            }

            // 3. Draw live tool cursor on the very top
            if (isHovered && _interactiveSegmentation?.ActiveTool is Segmentation.BrushTool brushTool)
            {
                float brushRadiusPixels = brushTool.BrushSize * (imageSize.X / width);
                dl.AddCircle(io.MousePos, brushRadiusPixels, 0xFF00FFFF, 12, 1.5f);
            }

            dl.PopClipRect();
        }

        private void UpdateTexture(int viewIndex, ref TextureManager texture)
        {
            if (_dataset.VolumeData == null)
            {
                Logger.LogError("[CtCombinedViewer] No volume data available");
                return;
            }

            try
            {
                var (width, height) = GetImageDimensionsForView(viewIndex);
                byte[] imageData = ExtractSliceData(viewIndex, width, height);
                ApplyWindowLevel(imageData);

                byte[] labelData = null;
                if (_dataset.LabelData != null)
                {
                    labelData = ExtractLabelSliceData(viewIndex, width, height);
                }

                int currentSlice = viewIndex switch { 0 => _sliceZ, 1 => _sliceY, 2 => _sliceX, _ => -1 };

                // Get preview data
                var (is2DPreviewActive, full3DPreviewMask, previewColor) = CtImageStackTools.GetPreviewData(_dataset);
                byte[] thresholdPreviewMask = ExtractPreviewSlice(full3DPreviewMask, viewIndex, width, height);

                byte[] segmentationPreviewMask = _interactiveSegmentation?.GetPreviewMask(currentSlice, viewIndex);
                byte[] committedSelectionMask = _interactiveSegmentation?.GetCommittedSelectionMask(currentSlice, viewIndex);

                // Get simulation overlay if active
                byte[] simulationOverlay = null;
                Vector4 simulationColor = Vector4.One;
                if (_showSimulationOverlay && _activeSimulationOverlay != null)
                {
                    simulationOverlay = GetSimulationOverlay(viewIndex, width, height);
                    // Use a colormap for simulation data
                    simulationColor = new Vector4(1, 0.5f, 0, 1); // Orange for example
                }

                byte[] rgbaData = new byte[width * height * 4];
                var targetMaterial = _dataset.Materials.FirstOrDefault(m => m.ID == _interactiveSegmentation.TargetMaterialId);

                for (int i = 0; i < width * height; i++)
                {
                    byte value = imageData[i];
                    Vector4 finalColor = new Vector4(value / 255f, value / 255f, value / 255f, 1.0f);

                    if (labelData != null && labelData[i] > 0)
                    {
                        var material = _dataset.Materials.FirstOrDefault(m => m.ID == labelData[i]);
                        if (material != null && GetMaterialVisibility(material.ID))
                        {
                            float opacity = GetMaterialOpacity(material.ID);
                            Vector4 matColor = new Vector4(material.Color.X, material.Color.Y, material.Color.Z, 1.0f);
                            finalColor = Vector4.Lerp(finalColor, matColor, opacity);
                        }
                    }

                    if (committedSelectionMask != null && committedSelectionMask[i] > 0)
                    {
                        var selColor = targetMaterial?.Color ?? new Vector4(0.8f, 0.8f, 0.0f, 1.0f);
                        float opacity = 0.4f;
                        finalColor = Vector4.Lerp(finalColor, new Vector4(selColor.X, selColor.Y, selColor.Z, 1.0f), opacity);
                    }

                    if (is2DPreviewActive && thresholdPreviewMask != null && thresholdPreviewMask[i] > 0)
                    {
                        float opacity = 0.5f;
                        Vector4 previewRgba = new Vector4(previewColor.X, previewColor.Y, previewColor.Z, 1.0f);
                        finalColor = Vector4.Lerp(finalColor, previewRgba, opacity);
                    }

                    if (segmentationPreviewMask != null && segmentationPreviewMask[i] > 0)
                    {
                        var segColor = targetMaterial?.Color ?? new Vector4(1, 0, 0, 1);
                        float opacity = 0.6f;
                        finalColor = Vector4.Lerp(finalColor, new Vector4(segColor.X, segColor.Y, segColor.Z, 1.0f), opacity);
                    }

                    // Apply simulation overlay
                    if (simulationOverlay != null && simulationOverlay[i] > 0)
                    {
                        float simValue = simulationOverlay[i] / 255f;
                        Vector4 simColor = simulationColor * simValue;
                        finalColor = Vector4.Lerp(finalColor, simColor, _overlayOpacity);
                    }

                    rgbaData[i * 4] = (byte)(finalColor.X * 255);
                    rgbaData[i * 4 + 1] = (byte)(finalColor.Y * 255);
                    rgbaData[i * 4 + 2] = (byte)(finalColor.Z * 255);
                    rgbaData[i * 4 + 3] = 255;
                }

                texture?.Dispose();
                texture = TextureManager.CreateFromPixelData(rgbaData, (uint)width, (uint)height);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CtCombinedViewer] Error updating texture: {ex.Message}");
            }
        }

        private byte[] GetSimulationOverlay(int viewIndex, int width, int height)
        {
            if (!_simulationResults.TryGetValue(_activeSimulationOverlay, out var result))
                return null;
                
            var scalarField = result.GetScalarField();
            if (scalarField == null)
                return null;
                
            byte[] overlay = new byte[width * height];
            
            try
            {
                switch (viewIndex)
                {
                    case 0: // XY slice
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                if (x < scalarField.GetLength(0) && y < scalarField.GetLength(1) && _sliceZ < scalarField.GetLength(2))
                                {
                                    float value = scalarField[x, y, _sliceZ];
                                    overlay[y * width + x] = (byte)(Math.Clamp(value * 255, 0, 255));
                                }
                            }
                        }
                        break;
                        
                    case 1: // XZ slice
                        for (int z = 0; z < height; z++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                if (x < scalarField.GetLength(0) && _sliceY < scalarField.GetLength(1) && z < scalarField.GetLength(2))
                                {
                                    float value = scalarField[x, _sliceY, z];
                                    overlay[z * width + x] = (byte)(Math.Clamp(value * 255, 0, 255));
                                }
                            }
                        }
                        break;
                        
                    case 2: // YZ slice
                        for (int z = 0; z < height; z++)
                        {
                            for (int y = 0; y < width; y++)
                            {
                                if (_sliceX < scalarField.GetLength(0) && y < scalarField.GetLength(1) && z < scalarField.GetLength(2))
                                {
                                    float value = scalarField[_sliceX, y, z];
                                    overlay[z * width + y] = (byte)(Math.Clamp(value * 255, 0, 255));
                                }
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CtCombinedViewer] Error extracting simulation overlay: {ex.Message}");
                return null;
            }
            
            return overlay;
        }

        // Rest of the existing methods remain unchanged...
        private byte[] ExtractPreviewSlice(byte[] full3DMask, int viewIndex, int sliceWidth, int sliceHeight)
        {
            if (full3DMask == null) return null;

            byte[] sliceMask = new byte[sliceWidth * sliceHeight];
            int fullWidth = _dataset.Width;
            int fullHeight = _dataset.Height;

            try
            {
                switch (viewIndex)
                {
                    case 0: // XY View
                        int offset = _sliceZ * fullWidth * fullHeight;
                        if (offset + sliceMask.Length <= full3DMask.Length)
                        {
                            Buffer.BlockCopy(full3DMask, offset, sliceMask, 0, sliceMask.Length);
                        }
                        break;
                    case 1: // XZ View
                        for (int z = 0; z < sliceHeight; z++)
                        {
                            for (int x = 0; x < sliceWidth; x++)
                            {
                                sliceMask[z * sliceWidth + x] = full3DMask[z * fullWidth * fullHeight + _sliceY * fullWidth + x];
                            }
                        }
                        break;
                    case 2: // YZ View
                        for (int z = 0; z < sliceHeight; z++)
                        {
                            for (int y = 0; y < sliceWidth; y++)
                            {
                                sliceMask[z * sliceWidth + y] = full3DMask[z * fullWidth * fullHeight + y * fullWidth + _sliceX];
                            }
                        }
                        break;
                }
            }
            catch (IndexOutOfRangeException ex)
            {
                Logger.LogError($"[CtCombinedViewer] Error extracting preview slice for view {viewIndex}: {ex.Message}");
                return null;
            }

            return sliceMask;
        }

        private byte[] ExtractSliceData(int viewIndex, int width, int height)
        {
            byte[] data = new byte[width * height];
            var volume = _dataset.VolumeData;

            try
            {
                switch (viewIndex)
                {
                    case 0: volume.ReadSliceZ(_sliceZ, data); break;
                    case 1: for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) data[z * width + x] = volume[x, _sliceY, z]; break;
                    case 2: for (int z = 0; z < height; z++) for (int y = 0; y < width; y++) data[z * width + y] = volume[_sliceX, y, z]; break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CtCombinedViewer] Error extracting slice data for view {viewIndex}: {ex.Message}");
            }

            return data;
        }

        private byte[] ExtractLabelSliceData(int viewIndex, int width, int height)
        {
            byte[] data = new byte[width * height];
            var labels = _dataset.LabelData;

            switch (viewIndex)
            {
                case 0: labels.ReadSliceZ(_sliceZ, data); break;
                case 1: for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) data[z * width + x] = labels[x, _sliceY, z]; break;
                case 2: for (int z = 0; z < height; z++) for (int y = 0; y < width; y++) data[z * width + y] = labels[_sliceX, y, z]; break;
            }
            return data;
        }

        private void ApplyWindowLevel(byte[] data)
        {
            float min = _windowLevel - _windowWidth / 2;
            float max = _windowLevel + _windowWidth / 2;
            float range = max - min;
            if (range < 1e-5) range = 1e-5f;

            Parallel.For(0, data.Length, i =>
            {
                float value = (data[i] - min) / range * 255;
                data[i] = (byte)Math.Clamp(value, 0, 255);
            });
        }

        private (int width, int height) GetImageDimensionsForView(int viewIndex)
        {
            return viewIndex switch
            {
                0 => (_dataset.Width, _dataset.Height),
                1 => (_dataset.Width, _dataset.Depth),
                2 => (_dataset.Height, _dataset.Depth),
                _ => (_dataset.Width, _dataset.Height)
            };
        }

        private void UpdateCrosshairFromMouse(int viewIndex, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan)
        {
            var (width, height) = GetImageDimensionsForView(viewIndex);
            var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, pan, width, height);
            var mousePosInImage = GetMousePosInImage(ImGui.GetMousePos(), imagePos, imageSize, width, height);

            switch (viewIndex)
            {
                case 0:
                    SliceX = Math.Clamp((int)mousePosInImage.X, 0, _dataset.Width - 1);
                    SliceY = Math.Clamp((int)mousePosInImage.Y, 0, _dataset.Height - 1);
                    break;
                case 1:
                    SliceX = Math.Clamp((int)mousePosInImage.X, 0, _dataset.Width - 1);
                    SliceZ = Math.Clamp((int)mousePosInImage.Y, 0, _dataset.Depth - 1);
                    break;
                case 2:
                    SliceY = Math.Clamp((int)mousePosInImage.X, 0, _dataset.Height - 1);
                    SliceZ = Math.Clamp((int)mousePosInImage.Y, 0, _dataset.Depth - 1);
                    break;
            }
        }

        private void DrawCrosshairs(ImDrawListPtr dl, int viewIndex, Vector2 canvasPos, Vector2 canvasSize, Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
        {
            uint color = 0xFF00FF00;
            float x1, y1;

            switch (viewIndex)
            {
                case 0: x1 = (float)_sliceX / imageWidth; y1 = (float)_sliceY / imageHeight; break;
                case 1: x1 = (float)_sliceX / imageWidth; y1 = (float)_sliceZ / imageHeight; break;
                case 2: x1 = (float)_sliceY / imageWidth; y1 = (float)_sliceZ / imageHeight; break;
                default: return;
            }

            float screenX = imagePos.X + x1 * imageSize.X;
            float screenY = imagePos.Y + y1 * imageSize.Y;

            if (screenX >= imagePos.X && screenX <= imagePos.X + imageSize.X)
            {
                dl.AddLine(new Vector2(screenX, Math.Max(imagePos.Y, canvasPos.Y)), new Vector2(screenX, Math.Min(imagePos.Y + imageSize.Y, canvasPos.Y + canvasSize.Y)), color, 1.0f);
            }
            if (screenY >= imagePos.Y && screenY <= imagePos.Y + imageSize.Y)
            {
                dl.AddLine(new Vector2(Math.Max(imagePos.X, canvasPos.X), screenY), new Vector2(Math.Min(imagePos.X + imageSize.X, canvasPos.X + canvasSize.X), screenY), color, 1.0f);
            }
        }

        private void DrawCuttingPlanes(ImDrawListPtr dl, int viewIndex, Vector2 canvasPos, Vector2 canvasSize,
            Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
        {
            if (VolumeViewer == null) return;

            if (VolumeViewer.CutXEnabled && (viewIndex == 0 || viewIndex == 1))
            {
                float normalizedX = VolumeViewer.CutXPosition;
                float screenX = imagePos.X + normalizedX * imageSize.X;
                if (screenX >= imagePos.X && screenX <= imagePos.X + imageSize.X)
                {
                    uint color = 0x6060FF60;
                    dl.AddLine(new Vector2(screenX, Math.Max(imagePos.Y, canvasPos.Y)), new Vector2(screenX, Math.Min(imagePos.Y + imageSize.Y, canvasPos.Y + canvasSize.Y)), color, 2.0f);
                    DrawArrow(dl, new Vector2(screenX, imagePos.Y + imageSize.Y * 0.5f), VolumeViewer.CutXForward ? new Vector2(10, 0) : new Vector2(-10, 0), color);
                }
            }

            if (VolumeViewer.CutYEnabled && (viewIndex == 0 || viewIndex == 2))
            {
                float normalizedY = VolumeViewer.CutYPosition;
                float screenY = imagePos.Y + (1.0f - normalizedY) * imageSize.Y;
                if (screenY >= imagePos.Y && screenY <= imagePos.Y + imageSize.Y)
                {
                    uint color = 0x6060FF60;
                    dl.AddLine(new Vector2(Math.Max(imagePos.X, canvasPos.X), screenY), new Vector2(Math.Min(imagePos.X + imageSize.X, canvasPos.X + canvasSize.X), screenY), color, 2.0f);
                    DrawArrow(dl, new Vector2(imagePos.X + imageSize.X * 0.5f, screenY), VolumeViewer.CutYForward ? new Vector2(0, -10) : new Vector2(0, 10), color);
                }
            }

            if (VolumeViewer.CutZEnabled && (viewIndex == 1 || viewIndex == 2))
            {
                float normalizedZ = VolumeViewer.CutZPosition;
                uint color = 0x60FF6060;

                if (viewIndex == 1) // XZ view
                {
                    float screenPos = imagePos.Y + (1.0f - normalizedZ) * imageSize.Y;
                    if (screenPos >= imagePos.Y && screenPos <= imagePos.Y + imageSize.Y)
                    {
                        dl.AddLine(new Vector2(Math.Max(imagePos.X, canvasPos.X), screenPos), new Vector2(Math.Min(imagePos.X + imageSize.X, canvasPos.X + canvasSize.X), screenPos), color, 2.0f);
                        DrawArrow(dl, new Vector2(imagePos.X + imageSize.X * 0.5f, screenPos), VolumeViewer.CutZForward ? new Vector2(0, -10) : new Vector2(0, 10), color);
                    }
                }
                else // YZ view
                {
                    float screenPos = imagePos.X + normalizedZ * imageSize.X;
                    if (screenPos >= imagePos.X && screenPos <= imagePos.X + imageSize.X)
                    {
                        dl.AddLine(new Vector2(screenPos, Math.Max(imagePos.Y, canvasPos.Y)), new Vector2(screenPos, Math.Min(imagePos.Y + imageSize.Y, canvasPos.Y + canvasSize.Y)), color, 2.0f);
                        DrawArrow(dl, new Vector2(screenPos, imagePos.Y + imageSize.Y * 0.5f), VolumeViewer.CutZForward ? new Vector2(10, 0) : new Vector2(-10, 0), color);
                    }
                }
            }

            if (VolumeViewer.ClippingPlanes != null)
            {
                foreach (var plane in VolumeViewer.ClippingPlanes.Where(p => p.Enabled))
                {
                    DrawClippingPlaneIntersection(dl, plane, viewIndex, canvasPos, canvasSize, imagePos, imageSize);
                }
            }
        }

        private void DrawClippingPlaneIntersection(ImDrawListPtr dl, ClippingPlane plane, int viewIndex,
            Vector2 canvasPos, Vector2 canvasSize, Vector2 imagePos, Vector2 imageSize)
        {
            Vector3 planeNormal = plane.Normal;
            uint color = 0x60FFFF60;

            switch (viewIndex)
            {
                case 0: if (Math.Abs(planeNormal.Z) > 0.1f) dl.AddLine(imagePos, imagePos + imageSize, color, 2.0f); break;
                case 1: if (Math.Abs(planeNormal.Y) > 0.1f) dl.AddLine(imagePos + new Vector2(0, imageSize.Y), imagePos + new Vector2(imageSize.X, 0), color, 2.0f); break;
                case 2: if (Math.Abs(planeNormal.X) > 0.1f) dl.AddLine(imagePos, imagePos + imageSize, color, 2.0f); break;
            }
        }

        private void DrawArrow(ImDrawListPtr dl, Vector2 position, Vector2 direction, uint color)
        {
            Vector2 normalized = Vector2.Normalize(direction);
            Vector2 perpendicular = new Vector2(-normalized.Y, normalized.X);

            Vector2 tip = position + direction;
            Vector2 wing1 = tip - normalized * 8 + perpendicular * 4;
            Vector2 wing2 = tip - normalized * 8 - perpendicular * 4;

            dl.AddTriangleFilled(tip, wing1, wing2, color);
        }

        private void DrawScaleBar(ImDrawListPtr dl, Vector2 canvasPos, Vector2 canvasSize, float zoom, int imageWidth, int imageHeight, int viewIndex)
        {
            float pixelSizeInUnits = viewIndex switch
            {
                0 => _dataset.PixelSize,
                1 => _dataset.PixelSize,
                2 => _dataset.SliceThickness,
                _ => _dataset.PixelSize
            };

            var (imagePos, imageSize) = GetImageDisplayMetrics(canvasPos, canvasSize, zoom, Vector2.Zero, imageWidth, imageHeight);
            float scaleFactor = imageSize.X / imageWidth;
            float[] possibleLengths = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 };
            string unit = _dataset.Unit ?? "µm";

            float bestLength = possibleLengths[0];
            foreach (float length in possibleLengths)
            {
                if (length / pixelSizeInUnits * scaleFactor <= 150) bestLength = length; else break;
            }

            float barLengthPixels = bestLength / pixelSizeInUnits * scaleFactor;
            Vector2 barPos = canvasPos + new Vector2(canvasSize.X - barLengthPixels - 20, canvasSize.Y - 40);

            dl.AddRectFilled(barPos - new Vector2(5, 5), barPos + new Vector2(barLengthPixels + 5, 25), 0xAA000000, 3.0f);
            dl.AddLine(barPos, barPos + new Vector2(barLengthPixels, 0), 0xFFFFFFFF, 3.0f);
            dl.AddLine(barPos, barPos + new Vector2(0, 5), 0xFFFFFFFF, 3.0f);
            dl.AddLine(barPos + new Vector2(barLengthPixels, 0), barPos + new Vector2(barLengthPixels, 5), 0xFFFFFFFF, 3.0f);

            string text = bestLength >= 1000 ? $"{bestLength / 1000:F0} mm" : $"{bestLength:F0} {unit}";
            Vector2 textSize = ImGui.CalcTextSize(text);
            Vector2 textPos = barPos + new Vector2((barLengthPixels - textSize.X) * 0.5f, 8);
            dl.AddText(textPos, 0xFFFFFFFF, text);
        }

        public void Dispose()
        {
            ProjectManager.Instance.DatasetDataChanged -= OnDatasetDataChanged;
            CtImageStackTools.PreviewChanged -= OnPreviewChanged;

            // Clean up simulation resources
            foreach (var processor in _activeProcessors.Values)
            {
                processor?.Dispose();
            }
            _activeProcessors.Clear();
            
            // Unsubscribe from add-in events
            AddInManager.Instance.AddInLoaded -= OnAddInLoaded;
            AddInManager.Instance.AddInUnloaded -= OnAddInUnloaded;

            CtSegmentationIntegration.Cleanup(_dataset);

            _renderingPanel?.Dispose();
            _textureXY?.Dispose();
            _textureXZ?.Dispose();
            _textureYZ?.Dispose();
            VolumeViewer?.Dispose();
        }

        // Generic simulation parameters for add-ins that don't provide their own
        internal class GenericSimulationParameters : SimulationParameters
        {
            public GenericSimulationParameters()
            {
                UseFullVolume = true;
                MaxIterations = 1000;
                Tolerance = 1e-6f;
                EnableGPU = true;
            }
        }
    }
}