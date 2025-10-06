// GeoscientistToolkit/Analysis/AcousticSimulation/AcousticSimulationUI.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;
using DensityTool = GeoscientistToolkit.UI.Tools.DensityCalibrationTool;

namespace GeoscientistToolkit.Analysis.AcousticSimulation
{
    /// <summary>
    /// Static class to manage the state of interactive transducer placement across different viewers.
    /// </summary>
    internal static class AcousticIntegration
    {
        public static event Action OnPositionsChanged;
        public static bool IsPlacing { get; private set; }
        public static Vector3 TxPosition { get; private set; }
        public static Vector3 RxPosition { get; private set; }

        private static Dataset _targetDataset;
        private static string _placingWhich; // "TX" or "RX"

        public static void StartPlacement(Dataset target, string which, Vector3 currentTx, Vector3 currentRx)
        {
            IsPlacing = true;
            _targetDataset = target;
            _placingWhich = which;
            TxPosition = currentTx;
            RxPosition = currentRx;
        }

        public static void StopPlacement()
        {
            IsPlacing = false;
            _targetDataset = null;
            _placingWhich = null;
        }

        public static bool IsPlacingFor(Dataset d) => IsPlacing && _targetDataset == d;
        public static bool IsActiveFor(Dataset d) => _targetDataset == d;
        public static string GetPlacingTarget() => _placingWhich;

        public static void UpdatePosition(Vector3 newNormalizedPos)
        {
            if (!IsPlacing) return;

            if (_placingWhich == "TX")
                TxPosition = newNormalizedPos;
            else
                RxPosition = newNormalizedPos;

            OnPositionsChanged?.Invoke();
        }
    }


    /// <summary>
    /// Main UI panel for controlling and displaying acoustic simulations.
    /// </summary>
    public class AcousticSimulationUI : IDisposable
    {
        private ChunkedAcousticSimulator _simulator;
        private SimulationParameters _parameters;
        private readonly UnifiedCalibrationManager _calibrationManager;
        private readonly AcousticExportManager _exportManager;
        private readonly VelocityTomographyGenerator _tomographyGenerator;
        private bool _isSimulating = false;
        private CancellationTokenSource _cancellationTokenSource;
        private SimulationResults _lastResults;
        private DateTime _simulationStartTime = DateTime.MinValue;

        // Real-time visualization support
        private bool _enableRealTimeVisualization = false;
        private float _visualizationUpdateInterval = 0.1f;
        private DateTime _lastVisualizationUpdate = DateTime.MinValue;
        private byte[] _currentWaveFieldMask;
        
        // Memory management
        private bool _useChunkedProcessing = true;
        private int _chunkSizeMB = 512;
        private bool _enableOffloading = true;
        private string _offloadDirectory;
        
        // Tomography settings
        private bool _isTomographyWindowOpen = false;
        private int _tomographySliceAxis = 0;
        private int _tomographySliceIndex = 0;
        private TextureManager _tomographyTexture;
        private int _lastTomographySliceAxis = -1;
        private int _lastTomographySliceIndex = -1;
        private ImGuiExportFileDialog _tomographyExportDialog;
        
        // UI State
        private bool _enableMultiMaterialSelection = false;
        private readonly HashSet<byte> _selectedMaterialIDs = new HashSet<byte>();
        private int _selectedAxisIndex = 0;
        private float _confiningPressure = 1.0f;
        private float _failureAngle = 30.0f;
        private float _cohesion = 5.0f;
        private float _sourceEnergy = 1.0f;
        private float _sourceFrequency = 500.0f;
        private int _sourceAmplitude = 100;
        private int _timeSteps = 1000;
        private float _youngsModulus = 30000.0f;
        private float _poissonRatio = 0.25f;
        private bool _useElastic = true;
        private bool _usePlastic = false;
        private bool _useBrittle = false;
        private bool _useGPU = true;
        private bool _useFullFaceTransducers = false;
        private bool _autoCalibrate = false;
        private bool _saveTimeSeries = false;
        private int _snapshotInterval = 10;
        private Vector3 _txPosition = new Vector3(0, 0.5f, 0.5f);
        private Vector3 _rxPosition = new Vector3(1, 0.5f, 0.5f);
        private CtImageStackDataset _currentDataset;
        private bool _autoUpdateTomography = false;
        private DateTime _lastTomographyUpdate = DateTime.MinValue;
        private bool _tomographyWindowWasOpen = false;
        private ImGuiFileDialog _offloadDirectoryDialog;
        private bool _useRickerWavelet = true;
        private int _estimatedTimeSteps = 0;
        private bool _timeStepsDirty = true;

        // --- IMPROVEMENT: Detailed simulation state tracking ---
        private enum SimulationState { Idle, Preparing, Simulating, Completed, Failed, Cancelled }
        private SimulationState _currentState = SimulationState.Idle;
        private float _preparationProgress;
        private string _preparationStatus = "";
        private bool _preparationComplete;


        public AcousticSimulationUI()
        {
            _parameters = new SimulationParameters();
            _calibrationManager = new UnifiedCalibrationManager();
            _exportManager = new AcousticExportManager();
            _tomographyGenerator = new VelocityTomographyGenerator();
            _offloadDirectory = Path.Combine(Path.GetTempPath(), "AcousticSimulation");
            Directory.CreateDirectory(_offloadDirectory);
            AcousticIntegration.OnPositionsChanged += OnTransducerMoved;

            _tomographyExportDialog = new ImGuiExportFileDialog("TomographyExportDialog", "Export Tomography Image");
            _tomographyExportDialog.SetExtensions((".png", "PNG Image"));
            _offloadDirectoryDialog = new ImGuiFileDialog("OffloadDirDialog", FileDialogType.OpenDirectory, "Select Offload Directory");
        }

        private void OnTransducerMoved()
        {
            _txPosition = AcousticIntegration.TxPosition;
            _rxPosition = AcousticIntegration.RxPosition;
            _timeStepsDirty = true;
        }

        public void DrawPanel(CtImageStackDataset dataset)
{
    if (dataset == null) return;
    
    if (_currentDataset != dataset)
    {
        _timeStepsDirty = true;
    }
    _currentDataset = dataset;
    AcousticIntegration.StartPlacement(dataset, null, _txPosition, _rxPosition); // Ensure it's active for drawing

    // Draw the separate tomography window if it's open
    if (_isTomographyWindowOpen)
    {
        DrawTomographyWindow();
    }

    ImGui.Text($"Dataset: {dataset.Name}");
    ImGui.Text($"Dimensions: {dataset.Width} × {dataset.Height} × {dataset.Depth}");

    long volumeMemory = (long)dataset.Width * dataset.Height * dataset.Depth * 3 * sizeof(float) * 2;
    ImGui.Text($"Estimated Memory: {volumeMemory / (1024 * 1024)} MB");

    if (volumeMemory > 4L * 1024 * 1024 * 1024)
    {
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "⚠ Large dataset - chunked processing recommended");
    }

    ImGui.Separator();

    // Density Calibration Section
    if (ImGui.CollapsingHeader("Density Calibration", ImGuiTreeNodeFlags.DefaultOpen))
    {
        ImGui.Indent();
        ImGui.TextWrapped("Use the 'Density Calibration' tool in the Preprocessing category to calibrate material densities.");
        ImGui.TextWrapped("This simulation will use the calibrated densities stored in the dataset's materials.");

        bool isCalibrated = dataset.Materials.Any(m => m.ID != 0 && m.Density > 0);
        ImGui.Text("Status:");
        ImGui.SameLine();
        ImGui.TextColored(isCalibrated ? new Vector4(0,1,0,1) : new Vector4(1,1,0,1),
            isCalibrated ? "✓ Material densities appear to be set." : "⚠ Using default material densities.");
        ImGui.Unindent();
    }

    ImGui.Separator();

    // Calibration Manager Controls
    _calibrationManager.DrawCalibrationControls(ref _youngsModulus, ref _poissonRatio);
    ImGui.Separator();

    // Material Selection
    ImGui.Text("Target Material(s):");
    var materials = dataset.Materials.Where(m => m.ID != 0).ToArray();
    if (materials.Length == 0)
    {
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "No materials defined in dataset.");
        return;
    }

    // Ensure at least one material is selected by default
    if (!_selectedMaterialIDs.Any() && materials.Any())
    {
        _selectedMaterialIDs.Add(materials.First().ID);
    }
    
    // Store the state before the ImGui call modifies it
    bool wasMultiMaterialEnabled = _enableMultiMaterialSelection;
    ImGui.Checkbox("Enable Multi-Material Selection", ref _enableMultiMaterialSelection);

    // If the user just disabled multi-material selection, ensure the selection set is valid for single-select mode (only one item)
    if (wasMultiMaterialEnabled && !_enableMultiMaterialSelection && _selectedMaterialIDs.Count > 1)
    {
        byte firstSelected = _selectedMaterialIDs.First();
        _selectedMaterialIDs.Clear();
        _selectedMaterialIDs.Add(firstSelected);
    }

    ImGui.BeginChild("MaterialList", new Vector2(-1, materials.Length * 25f + 10), ImGuiChildFlags.Border);
    foreach (var material in materials)
    {
        if (_enableMultiMaterialSelection)
        {
            bool isSelected = _selectedMaterialIDs.Contains(material.ID);
            if (ImGui.Checkbox(material.Name, ref isSelected))
            {
                if (isSelected)
                    _selectedMaterialIDs.Add(material.ID);
                else
                    _selectedMaterialIDs.Remove(material.ID);
            }
        }
        else
        {
            bool isSelected = _selectedMaterialIDs.Contains(material.ID);
            if (ImGui.RadioButton(material.Name, isSelected))
            {
                _selectedMaterialIDs.Clear();
                _selectedMaterialIDs.Add(material.ID);
            }
        }
    }
    ImGui.EndChild();
    ImGui.Separator();

    // Wave Propagation Axis
    ImGui.Text("Wave Propagation Axis:");
    
    bool axisChanged = false;
    axisChanged |= ImGui.RadioButton("X", ref _selectedAxisIndex, 0); ImGui.SameLine();
    axisChanged |= ImGui.RadioButton("Y", ref _selectedAxisIndex, 1); ImGui.SameLine();
    axisChanged |= ImGui.RadioButton("Z", ref _selectedAxisIndex, 2);

    if (axisChanged)
    {
        ApplyAxisPreset(_selectedAxisIndex);
        AcousticIntegration.StartPlacement(_currentDataset, null, _txPosition, _rxPosition);
        _timeStepsDirty = true;
    }

    ImGui.Checkbox("Full-Face Transducers", ref _useFullFaceTransducers);
    if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Use entire face of volume as transducer instead of point source");
    ImGui.Separator();

    // Memory Management
    if (ImGui.CollapsingHeader("Memory Management"))
    {
        ImGui.Indent();
        ImGui.Checkbox("Use Chunked Processing", ref _useChunkedProcessing);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Process large volumes in chunks to avoid memory issues");

        if (_useChunkedProcessing)
        {
            ImGui.DragInt("Chunk Size (MB)", ref _chunkSizeMB, 1, 64, 2048);
            ImGui.Checkbox("Enable Disk Offloading", ref _enableOffloading);
            if (_enableOffloading)
            {
                ImGui.Text($"Offload Dir: {_offloadDirectory}");
                if (ImGui.Button("Change Directory..."))
                {
                    _offloadDirectoryDialog.Open(_offloadDirectory);
                }
        
                ImGui.SameLine();
                if (ImGui.Button("Clear Cache"))
                {
                    if (Directory.Exists(_offloadDirectory))
                    {
                        try { Directory.Delete(_offloadDirectory, true); Directory.CreateDirectory(_offloadDirectory); Logger.Log("[Simulation] Cleared offload cache"); } 
                        catch (Exception ex) { Logger.LogError($"Failed to clear cache: {ex.Message}"); }
                    }
                }
            }
        }
        ImGui.Unindent();
    }

    // Visualization Options
    if (ImGui.CollapsingHeader("Visualization"))
    {
        ImGui.Indent();
        ImGui.Checkbox("Enable Real-Time 3D Visualization", ref _enableRealTimeVisualization);
        if (_enableRealTimeVisualization)
        {
            ImGui.DragFloat("Update Interval (s)", ref _visualizationUpdateInterval, 0.01f, 0.01f, 1.0f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How often to update the 3D visualization during simulation");
        }
        
        // Tomography window control with regeneration on reopen
        bool prevTomographyOpen = _isTomographyWindowOpen;
        ImGui.Checkbox("Show Velocity Tomography", ref _isTomographyWindowOpen);
        if (!prevTomographyOpen && _isTomographyWindowOpen && (_lastResults != null || _isSimulating))
        {
            // Window was just opened, regenerate tomography from final or live data
            _ = GenerateTomographyAsync();
        }

        ImGui.Separator();
        ImGui.Checkbox("Save Time Series", ref _saveTimeSeries);
        if (_saveTimeSeries)
        {
            ImGui.DragInt("Snapshot Interval", ref _snapshotInterval, 1, 1, 100);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Save a snapshot every N time steps");
        }
        ImGui.Unindent();
    }

    // Material Properties
    if (ImGui.CollapsingHeader("Material Properties"))
    {
        ImGui.Indent();

        // Show properties for the primary selected material
        var primaryMaterial = materials.FirstOrDefault(m => _selectedMaterialIDs.Contains(m.ID));
        if (primaryMaterial != null)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1.0f, 1), $"Primary Material: {primaryMaterial.Name}");

            if (!string.IsNullOrEmpty(primaryMaterial.PhysicalMaterialName))
            {
                var physMat = MaterialLibrary.Instance.Find(primaryMaterial.PhysicalMaterialName);
                if (physMat != null)
                {
                    ImGui.Text("Properties from Material Library:");
                    ImGui.Indent();
                    ImGui.Text($"Density: {physMat.Density_kg_m3 / 1000.0:F3} g/cm³");
                    ImGui.Text($"Young's Modulus: {physMat.YoungModulus_GPa:F0} GPa");
                    ImGui.Text($"Poisson's Ratio: {physMat.PoissonRatio:F3}");
                    if (physMat.Vp_m_s.HasValue)
                        ImGui.Text($"P-wave Velocity: {physMat.Vp_m_s:F0} m/s");
                    if (physMat.Vs_m_s.HasValue)
                        ImGui.Text($"S-wave Velocity: {physMat.Vs_m_s:F0} m/s");
                    ImGui.Unindent();
                }
            }
        }
        
        ImGui.Separator();

        // Allow manual override
        ImGui.Text("Simulation Parameters (override material library):");
        ImGui.DragFloat("Young's Modulus (MPa)", ref _youngsModulus, 100.0f, 100.0f, 200000.0f);
        ImGui.DragFloat("Poisson's Ratio", ref _poissonRatio, 0.01f, 0.0f, 0.49f);

        if (_calibrationManager.HasCalibration)
        {
            if (ImGui.Button("Apply Calibration from Lab Data"))
            {
                if (primaryMaterial != null)
                {
                    var (__calE, __calNu) = _calibrationManager.GetCalibratedParameters((float)primaryMaterial.Density, _confiningPressure);
                    _youngsModulus = __calE;
                    _poissonRatio  = __calNu;
                    Logger.Log($"[Simulation] Applied calibration: E={_youngsModulus:F2} MPa, ν={_poissonRatio:F4}");
                }
            }
        }

        ImGui.Spacing();
        ImGui.Text("Derived Properties:");
        ImGui.Indent();
        float E  = _youngsModulus * 1e6f;
        float nu = _poissonRatio;
        float mu = E / (2.0f * (1.0f + nu));
        float lambda = E * nu / ((1 + nu) * (1 - 2 * nu));
        float bulkModulus = E / (3f * (1 - 2 * nu));
        ImGui.Text($"Shear Modulus: {mu / 1e6f:F2} MPa");
        ImGui.Text($"Bulk Modulus: {bulkModulus / 1e6f:F2} MPa");
        ImGui.Text($"Lamé λ: {lambda / 1e6f:F2} MPa");
        ImGui.Text($"Lamé μ: {mu / 1e6f:F2} MPa");

        if (_lastResults != null && primaryMaterial != null)
        {
            // Get density from the currently selected material for the estimation
            float density = (float)primaryMaterial.Density; // in g/cm³
            if(density <= 0) density = 2.7f; // fallback
            density *= 1000f; // convert to kg/m³
    
            float vpExpected = MathF.Sqrt((lambda + 2 * mu) / density);
            float vsExpected = MathF.Sqrt(mu / density);
    
            ImGui.Spacing();
            ImGui.Text($"Expected Vp: {vpExpected:F0} m/s");
            ImGui.Text($"Expected Vs: {vsExpected:F0} m/s");
            ImGui.Text($"Expected Vp/Vs: {vpExpected / vsExpected:F3}");
        }
        ImGui.Unindent();

        if (_lastResults != null)
        {
            float calculatedPixelSize = CalculatePixelSizeFromVelocities();
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f), $"Calculated Pixel Size: {calculatedPixelSize * 1000:F3} mm");
        }
        ImGui.Unindent();
    }

    // Stress Conditions
    if (ImGui.CollapsingHeader("Stress Conditions"))
    {
        ImGui.Indent();
        ImGui.DragFloat("Confining Pressure (MPa)", ref _confiningPressure, 0.1f, 0.0f, 100.0f);
        ImGui.DragFloat("Failure Angle (°)", ref _failureAngle, 1.0f, 0.0f, 90.0f);
        ImGui.DragFloat("Cohesion (MPa)", ref _cohesion, 0.5f, 0.0f, 50.0f);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Material cohesion for Mohr-Coulomb plasticity model");
        ImGui.Unindent();
    }

    // Source Parameters
    if (ImGui.CollapsingHeader("Source Parameters"))
    {
        ImGui.Indent();
        ImGui.Checkbox("Use Ricker Wavelet", ref _useRickerWavelet);
        ImGui.DragFloat("Source Energy (J)", ref _sourceEnergy, 0.1f, 0.01f, 10.0f);
        ImGui.DragFloat("Frequency (kHz)", ref _sourceFrequency, 10.0f, 1.0f, 5000.0f);
        ImGui.DragInt("Amplitude", ref _sourceAmplitude, 1, 1, 1000);
        
        if (_timeStepsDirty && dataset != null)
        {
            _estimatedTimeSteps = CalculateEstimatedTimeSteps(dataset);
            _timeSteps = _estimatedTimeSteps;
            _timeStepsDirty = false;
        }
        ImGui.DragInt("Time Steps", ref _timeSteps, 10, 100, 50000);
        ImGui.SameLine();
        if (ImGui.Button("Auto"))
        {
            _timeSteps = CalculateEstimatedTimeSteps(dataset);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Automatically estimate steps based on distance and material properties.\nEstimated: {_estimatedTimeSteps}");

        if (_lastResults != null)
        {
            float vp = (float)(_lastResults?.PWaveVelocity ?? 5000.0);
            float wavelength = vp / (_sourceFrequency * 1000f);
            ImGui.Text($"P-Wave Wavelength: {wavelength * 1000:F2} mm");
        }

        // Transducer placement controls
        if (!_useFullFaceTransducers)
        {
            ImGui.Spacing();
            ImGui.Text("Transducer Positions (normalized 0-1):");
            
            ImGui.Text($"TX: ({_txPosition.X:F3}, {_txPosition.Y:F3}, {_txPosition.Z:F3})");
            ImGui.Text($"RX: ({_rxPosition.X:F3}, {_rxPosition.Y:F3}, {_rxPosition.Z:F3})");

            bool isPlacingTx = AcousticIntegration.IsPlacing && AcousticIntegration.GetPlacingTarget() == "TX";
            bool isPlacingRx = AcousticIntegration.IsPlacing && AcousticIntegration.GetPlacingTarget() == "RX";

            // TX Button
            if (isPlacingTx) 
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.8f, 0.4f, 1.0f));
                if (ImGui.Button("Stop Placing TX"))
                {
                    AcousticIntegration.StopPlacement();
                }
                ImGui.PopStyleColor();
            }
            else
            {
                if (ImGui.Button("Place TX"))
                {
                    AcousticIntegration.StopPlacement();
                    AcousticIntegration.StartPlacement(dataset, "TX", _txPosition, _rxPosition);
                }
            }

            ImGui.SameLine();
            
            // RX Button
            if (isPlacingRx)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.8f, 0.4f, 1.0f));
                if (ImGui.Button("Stop Placing RX"))
                {
                    AcousticIntegration.StopPlacement();
                }
                ImGui.PopStyleColor();
            }
            else
            {
                if (ImGui.Button("Place RX"))
                {
                    AcousticIntegration.StopPlacement();
                    AcousticIntegration.StartPlacement(dataset, "RX", _txPosition, _rxPosition);
                }
            }

            if (AcousticIntegration.IsPlacing)
            {
                ImGui.TextColored(new Vector4(1,1,0,1), $"Placing {AcousticIntegration.GetPlacingTarget()}... Click in a viewer to place. Right-click to rotate 3D view.");
            }
            
            if (ImGui.Button("Auto-place Transducers"))
            {
                AutoPlaceTransducers(dataset);
            }
            if(ImGui.IsItemHovered()) 
                ImGui.SetTooltip("Automatically place TX/RX on opposite sides of the selected material(s) largest connected component.");

            float dx = (_rxPosition.X - _txPosition.X) * dataset.Width * (float)dataset.PixelSize / 1000f;
            float dy = (_rxPosition.Y - _txPosition.Y) * dataset.Height * (float)dataset.PixelSize / 1000f;
            float dz = (_rxPosition.Z - _txPosition.Z) * dataset.Depth * (float)dataset.SliceThickness / 1000f;
            float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            ImGui.Text($"TX-RX Distance: {distance:F2} mm");
        }
        ImGui.Unindent();
    }

    // Physics Models
    if (ImGui.CollapsingHeader("Physics Models"))
    {
        ImGui.Indent();
        ImGui.Checkbox("Elastic", ref _useElastic);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Standard elastic wave propagation");
        ImGui.Checkbox("Plastic (Mohr-Coulomb)", ref _usePlastic);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Include plastic deformation using Mohr-Coulomb criterion");
        ImGui.Checkbox("Brittle Damage", ref _useBrittle);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Model brittle damage accumulation");
        ImGui.Checkbox("Use GPU Acceleration", ref _useGPU);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Use OpenCL for GPU acceleration (if available)");
        ImGui.Checkbox("Auto-Calibrate", ref _autoCalibrate);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Automatically calibrate parameters based on previous simulations");
        ImGui.Unindent();
    }

    ImGui.Separator();

    // --- IMPROVEMENT: State-aware simulation controls ---
    if (_currentState == SimulationState.Preparing)
    {
        ImGui.ProgressBar(_preparationProgress, new Vector2(-1, 0), _preparationStatus);
        if (_preparationComplete)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "✓ Pre-computation completed. Starting simulation...");
        }
        if (ImGui.Button("Cancel", new Vector2(-1, 0)))
        {
            CancelSimulation();
        }
    }
    else if (_currentState == SimulationState.Simulating)
    {
        ImGui.ProgressBar(_simulator?.Progress ?? 0.0f, new Vector2(-1, 0), $"Simulating... {(_simulator?.CurrentStep ?? 0)}/{_parameters.TimeSteps} steps");
        if (_simulator != null)
        {
            ImGui.Text($"Memory Usage: {_simulator.CurrentMemoryUsageMB:F0} MB");
            ImGui.Text($"Time Elapsed: {(DateTime.Now - _simulationStartTime).TotalSeconds:F1} s");
        }
        if (ImGui.Button("Cancel Simulation", new Vector2(-1, 0)))
        {
            CancelSimulation();
        }
    }
    else // Idle, Completed, Failed, Cancelled states
    {
        bool canSimulate = _selectedMaterialIDs.Any();
        if (!canSimulate)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Run Simulation", new Vector2(-1, 0)))
        {
            _simulationStartTime = DateTime.Now;
            _ = RunSimulationAsync(dataset);
        }

        if (!canSimulate)
        {
            ImGui.EndDisabled();
            if(ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("You must select at least one material to run a simulation.");
            }
        }
    }

    // Simulation Results
    if (_lastResults != null)
    {
        ImGui.Separator();
        if (ImGui.CollapsingHeader("Simulation Results", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"P-Wave Velocity: {_lastResults.PWaveVelocity:F2} m/s");
            ImGui.Text($"S-Wave Velocity: {_lastResults.SWaveVelocity:F2} m/s");
            ImGui.Text($"Vp/Vs Ratio: {_lastResults.VpVsRatio:F3}");
            ImGui.Text($"Total Time Steps: {_lastResults.TotalTimeSteps}");
            ImGui.Text($"Computation Time: {_lastResults.ComputationTime.TotalSeconds:F2} s");
    
            if (_lastResults.TotalTimeSteps > 0 && _lastResults.ComputationTime.TotalSeconds > 0)
            {
                double stepsPerSecond = _lastResults.TotalTimeSteps / _lastResults.ComputationTime.TotalSeconds;
                ImGui.Text($"Performance: {stepsPerSecond:F0} steps/second");
            }
    
            ImGui.Spacing();
            _exportManager.SetCalibrationData(_calibrationManager.CalibrationData);
            if (_lastResults.DamageField != null)
            {
                // This was the original location of the SetDamageField call. 
                // It is moved to RunSimulationAsync to avoid being overwritten.
                ImGui.Text($"Damage Field: Available ({_lastResults.DamageField.Length} voxels)");
            }
    
            // --- FIX START ---
            // Pass the damage field from the results directly to the export controls.
            _exportManager.DrawExportControls(_lastResults, _parameters, dataset, _lastResults.DamageField);
            // --- FIX END ---
            ImGui.Spacing();

            bool canAddToCalibration = _selectedMaterialIDs.Count == 1;
            if (!canAddToCalibration) ImGui.BeginDisabled();
            
            if (ImGui.Button("Add to Calibration Database"))
            {
                if(canAddToCalibration)
                {
                    var material = dataset.Materials.First(m => m.ID == _selectedMaterialIDs.First());
                    _calibrationManager.AddSimulationResult(material.Name, material.ID, (float)material.Density, 
                        _confiningPressure, _youngsModulus, _poissonRatio, 
                        _lastResults.PWaveVelocity, _lastResults.SWaveVelocity);
                    Logger.Log("[Simulation] Added results to calibration database");
                }
            }
            if (!canAddToCalibration)
            {
                ImGui.EndDisabled();
                if(ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Can only add to calibration when a single material is simulated.");
                }
            }
        }
        
        // Debug Information
        if (ImGui.CollapsingHeader("Debug Information"))
        {
            ImGui.Indent();
            DrawWaveDebugInfo("TX", _txPosition);
            ImGui.Separator();
            DrawWaveDebugInfo("RX", _rxPosition);
            ImGui.Separator();
            DrawWaveDebugInfo("Midpoint", (_txPosition + _rxPosition) * 0.5f);
            ImGui.Unindent();
        }
    }

    if (_offloadDirectoryDialog.Submit())
    {
        _offloadDirectory = _offloadDirectoryDialog.SelectedPath;
        Logger.Log($"[Simulation] Offload directory changed to: {_offloadDirectory}");
        Directory.CreateDirectory(_offloadDirectory);
    }
}
private void ExportTomographyImage()
{
    // Allow exporting if results are available OR a simulation is currently running
    if (_lastResults == null && !_isSimulating)
    {
        Logger.LogWarning("[Tomography] Cannot export, no simulation data available.");
        return;
    }
    
    string filename = $"Tomography_{(_tomographySliceAxis == 0 ? "X" : _tomographySliceAxis == 1 ? "Y" : "Z")}_{_tomographySliceIndex}_{DateTime.Now:yyyyMMdd_HHmmss}";
    _tomographyExportDialog.Open(filename);
}

private async Task SaveTomographyImageAsync(string path)
{
    SimulationResults resultsToUse = null;

    // Part 1: Get data (can be async). This happens on the UI thread.
    if (_isSimulating && _simulator != null)
    {
        Logger.Log("[Tomography] Exporting live snapshot from ongoing simulation...");
        resultsToUse = new SimulationResults
        {
            WaveFieldVx = await _simulator.ReconstructFieldAsync(0),
            WaveFieldVy = await _simulator.ReconstructFieldAsync(1),
            WaveFieldVz = await _simulator.ReconstructFieldAsync(2),
            PWaveVelocity = _lastResults?.PWaveVelocity ?? 6000.0, // Use last good values for color bar consistency
            SWaveVelocity = _lastResults?.SWaveVelocity ?? 3000.0,
            VpVsRatio = _lastResults?.VpVsRatio ?? 2.0
        };
    }
    else if (_lastResults != null)
    {
        Logger.Log("[Tomography] Exporting final simulation results...");
        resultsToUse = _lastResults;
    }

    if (resultsToUse == null)
    {
        Logger.LogError("[Tomography] No data available to export.");
        return;
    }
    
    // Part 2: Process and save data (CPU and I/O bound). Offload to a background thread to keep UI responsive.
    await Task.Run(() =>
    {
        int width = _tomographySliceAxis switch { 0 => _parameters.Height, 1 => _parameters.Width, _ => _parameters.Width };
        int height = _tomographySliceAxis switch { 0 => _parameters.Depth, 1 => _parameters.Depth, _ => _parameters.Height };
        
        var rgbaData = _tomographyGenerator.Generate2DTomography(resultsToUse, _tomographySliceAxis, _tomographySliceIndex);
        if (rgbaData == null)
        {
            Logger.LogError("[Tomography] Failed to generate pixel data for export.");
            return;
        }
        
        try
        {
            using (var stream = File.Create(path))
            {
                var writer = new ImageWriter();
                writer.WritePng(rgbaData, width, height, ColorComponents.RedGreenBlueAlpha, stream);
            }
            Logger.Log($"[Tomography] Successfully exported tomography image to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Tomography] Failed to export tomography image: {ex.Message}");
        }
    });
}

private void ExportAllTomographySlices()
{
    if (_lastResults == null) return;
    
    Task.Run(async () =>
    {
        Logger.Log("[Tomography] Starting export of all slices...");
        string exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
            $"Tomography_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(exportDir);
        
        // Export all X slices
        for (int i = 0; i < _currentDataset.Width; i++)
        {
            var tomography = _tomographyGenerator.Generate2DTomography(_lastResults, 0, i);
            if (tomography != null)
            {
                string path = Path.Combine(exportDir, $"X_slice_{i:D4}.bin");
                File.WriteAllBytes(path, tomography);
            }
        }
        
        // Export all Y slices  
        for (int i = 0; i < _currentDataset.Height; i++)
        {
            var tomography = _tomographyGenerator.Generate2DTomography(_lastResults, 1, i);
            if (tomography != null)
            {
                string path = Path.Combine(exportDir, $"Y_slice_{i:D4}.bin");
                File.WriteAllBytes(path, tomography);
            }
        }
        
        // Export all Z slices
        for (int i = 0; i < _currentDataset.Depth; i++)
        {
            var tomography = _tomographyGenerator.Generate2DTomography(_lastResults, 2, i);
            if (tomography != null)
            {
                string path = Path.Combine(exportDir, $"Z_slice_{i:D4}.bin");
                File.WriteAllBytes(path, tomography);
            }
        }
        
        Logger.Log($"[Tomography] Exported all slices to {exportDir}");
    });
}
        private void ApplyAxisPreset(int axis)
        {
            switch (axis)
            {
                case 0: // X-Axis
                    _txPosition = new Vector3(0.0f, 0.5f, 0.5f);
                    _rxPosition = new Vector3(1.0f, 0.5f, 0.5f);
                    break;
                case 1: // Y-Axis
                    _txPosition = new Vector3(0.5f, 0.0f, 0.5f);
                    _rxPosition = new Vector3(0.5f, 1.0f, 0.5f);
                    break;
                case 2: // Z-Axis
                    _txPosition = new Vector3(0.5f, 0.5f, 0.0f);
                    _rxPosition = new Vector3(0.5f, 0.5f, 1.0f);
                    break;
            }
        }
        
        private void DrawWaveDebugInfo(string label, Vector3 normalizedPos)
        {
            if (_lastResults?.WaveFieldVx == null)
            {
                ImGui.Text($"{label}: No wave field data.");
                return;
            }

            int w = _lastResults.WaveFieldVx.GetLength(0);
            int h = _lastResults.WaveFieldVx.GetLength(1);
            int d = _lastResults.WaveFieldVx.GetLength(2);

            int x = (int)Math.Clamp(normalizedPos.X * w, 0, w - 1);
            int y = (int)Math.Clamp(normalizedPos.Y * h, 0, h - 1);
            int z = (int)Math.Clamp(normalizedPos.Z * d, 0, d - 1);

            float vx = _lastResults.WaveFieldVx[x, y, z];
            float vy = _lastResults.WaveFieldVy[x, y, z];
            float vz = _lastResults.WaveFieldVz[x, y, z];
            var velocity = new Vector3(vx, vy, vz);

            ImGui.Text($"{label} at [{x}, {y}, {z}]");
            ImGui.Indent();
            ImGui.Text($"Velocity Vector: <{vx:G3}, {vy:G3}, {vz:G3}>");
            ImGui.Text($"Total Magnitude: {velocity.Length():G4}");

            // Calculate P and S components relative to TX->RX vector
            if (label != "Midpoint")
            {
                Vector3 txVoxel = new Vector3(_txPosition.X * w, _txPosition.Y * h, _txPosition.Z * d);
                Vector3 rxVoxel = new Vector3(_rxPosition.X * w, _rxPosition.Y * h, _rxPosition.Z * d);
                Vector3 pathDir = Vector3.Normalize(rxVoxel - txVoxel);

                if (pathDir.LengthSquared() > 0.1f)
                {
                    float p_component_mag = Vector3.Dot(velocity, pathDir);
                    Vector3 p_component_vec = pathDir * p_component_mag;
                    Vector3 s_component_vec = velocity - p_component_vec;

                    ImGui.Text($"P-Wave Component: {p_component_mag:G4}");
                    ImGui.Text($"S-Wave Component: {s_component_vec.Length():G4}");
                }
            }
            ImGui.Unindent();
        }
        
        private async void AutoPlaceTransducers(CtImageStackDataset dataset)
        {
            if (!_selectedMaterialIDs.Any())
            {
                Logger.LogWarning("[AutoPlace] No materials selected for auto-placement.");
                return;
            }
        
            Logger.Log($"[AutoPlace] Finding optimal transducer positions for material IDs [{string.Join(", ", _selectedMaterialIDs)}]...");
        
            // 1. Create the placer with the set of selected materials.
            var autoPlacer = new TransducerAutoPlacer(dataset, _selectedMaterialIDs);
        
            // 2. Call the new public method, which encapsulates all logic.
            var placementResult = await Task.Run(() => autoPlacer.PlaceTransducersForAxis(_selectedAxisIndex));
        
            if (!placementResult.HasValue)
            {
                Logger.LogError($"[AutoPlace] Failed to automatically place transducers. The selected material(s) might be too small, fragmented, or not present along the chosen axis.");
                return;
            }
        
            // 3. Update UI state with the results.
            _txPosition = placementResult.Value.tx;
            _rxPosition = placementResult.Value.rx;
        
            // Update the integration helper for visual feedback in the viewers.
            AcousticIntegration.StartPlacement(dataset, null, _txPosition, _rxPosition);
            OnTransducerMoved();
        }
        
        private float CalculatePixelSizeFromVelocities()
        {
            if (_lastResults == null) return _parameters.PixelSize;
            float distance = CalculateDistance();
            float timeP = _lastResults.PWaveTravelTime * _parameters.TimeStepSeconds;
            float calculatedDistance = (float)_lastResults.PWaveVelocity * timeP;
            if (calculatedDistance > 0 && distance > 0)
            {
                float pixelCount = distance / _parameters.PixelSize;
                return calculatedDistance / pixelCount;
            }
            return _parameters.PixelSize;
        }

        private float CalculateDistance()
        {
            float dx = (_rxPosition.X - _txPosition.X) * _parameters.Width;
            float dy = (_rxPosition.Y - _txPosition.Y) * _parameters.Height;
            float dz = (_rxPosition.Z - _txPosition.Z) * _parameters.Depth;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) * _parameters.PixelSize;
        }

        private int CalculateEstimatedTimeSteps(CtImageStackDataset dataset)
{
    if (dataset == null || !_selectedMaterialIDs.Any()) return 1000;

    try
    {
        // --- STEP 1: Find average and maximum P-wave velocities among selected materials ---
        float vp_avg = 5000f; // Fallback average velocity
        float vp_max = 5000f; // Fallback maximum velocity
        bool firstMaterial = true;

        var calculatedVps = new List<float>();

        foreach (byte materialId in _selectedMaterialIDs)
        {
            var material = dataset.Materials.FirstOrDefault(m => m.ID == materialId);
            if (material == null) continue;

            float density_g_cm3 = (float)(material.Density);
            if (density_g_cm3 <= 0) density_g_cm3 = 2.7f; // fallback density
            float density_kg_m3 = density_g_cm3 * 1000f;

            // Start with UI overrides, then check material library
            float e_MPa = _youngsModulus;
            float nu = _poissonRatio;
            
            if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
            {
                var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                if (physMat != null)
                {
                    // Use library values if available, converting GPa to MPa for Young's Modulus
                    e_MPa = (float)(physMat.YoungModulus_GPa ?? (_youngsModulus / 1000.0)) * 1000f;
                    nu = (float)(physMat.PoissonRatio ?? _poissonRatio);
                }
            }

            float e_Pa = e_MPa * 1e6f;
            
            // Check for invalid material properties to avoid NaN
            if (e_Pa <= 0 || nu <= -1.0f || nu >= 0.5f) continue;

            float mu = e_Pa / (2.0f * (1.0f + nu));
            float lambda = e_Pa * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));
            float current_vp = MathF.Sqrt((lambda + 2.0f * mu) / density_kg_m3);
            
            if (float.IsNaN(current_vp) || current_vp <= 100) continue;

            calculatedVps.Add(current_vp);

            if (firstMaterial)
            {
                vp_avg = current_vp; // Use the first selected material for average travel time
                firstMaterial = false;
            }
        }

        if (calculatedVps.Any())
        {
            vp_max = calculatedVps.Max();
        }


        // --- STEP 2: Calculate physical distance between transducers ---
        float pixelSizeM = (float)dataset.PixelSize / 1000f;
        float sliceThicknessM = (float)dataset.SliceThickness / 1000f;
        float dx = (_rxPosition.X - _txPosition.X) * dataset.Width * pixelSizeM;
        float dy = (_rxPosition.Y - _txPosition.Y) * dataset.Height * pixelSizeM;
        float dz = (_rxPosition.Z - _txPosition.Z) * dataset.Depth * sliceThicknessM;
        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance < pixelSizeM) distance = pixelSizeM; // Avoid zero distance

        // --- STEP 3: Estimate the simulation time step (dt) using the worst-case velocity ---
        // CFL condition: dt <= h / (sqrt(3) * Vp_max), with a safety factor of 0.5
        float dt_est = 0.5f * (pixelSizeM / (1.732f * vp_max));

        // --- STEP 4: Calculate P-wave travel time and corresponding steps ---
        if (dt_est < 1e-12f) return 2000; // Avoid division by zero, return a safe default
        float travelTime = distance / vp_avg;
        int p_wave_steps = (int)(travelTime / dt_est);

        // --- STEP 5: Add a generous buffer to allow S-wave to arrive and see reflections ---
        // We assume Vp/Vs is ~1.8, and add an extra 25% margin.
        // Total multiplier = 1.8 (for S-wave) * 1.25 (margin) = 2.25
        int total_steps = (int)(p_wave_steps * 2.25);
        
        // Clamp to a reasonable range to prevent extreme values
        return Math.Clamp(total_steps, 500, 50000);
    }
    catch
    {
        return 2000; // Fallback on any error
    }
}

        private void DrawTomographyWindow()
{
    ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
    if (ImGui.Begin("Velocity Tomography", ref _isTomographyWindowOpen))
    {
        if (_lastResults == null && !_isSimulating)
        {
            ImGui.Text("No simulation results available. Run a simulation first.");
            ImGui.End();
            return;
        }

        // Update button - regenerate tomography with current results
        if (ImGui.Button("Update Tomography", new Vector2(-1, 0)))
        {
            _ = GenerateTomographyAsync();
            Logger.Log("[Tomography] Manually updating tomography view");
        }
        
        ImGui.Separator();

        // --- Tomography Controls ---
        ImGui.Text("Tomography Slice:");
        ImGui.RadioButton("X Slice", ref _tomographySliceAxis, 0); ImGui.SameLine();
        ImGui.RadioButton("Y Slice", ref _tomographySliceAxis, 1); ImGui.SameLine();
        ImGui.RadioButton("Z Slice", ref _tomographySliceAxis, 2);

        int maxSlice = _tomographySliceAxis switch
        {
            0 => _currentDataset.Width - 1,
            1 => _currentDataset.Height - 1,
            2 => _currentDataset.Depth - 1,
            _ => 0
        };
        ImGui.SliderInt("Slice Index", ref _tomographySliceIndex, 0, maxSlice);

        // --- Regenerate texture if controls change ---
        if (_tomographySliceIndex != _lastTomographySliceIndex || _tomographySliceAxis != _lastTomographySliceAxis)
        {
            _ = GenerateTomographyAsync();
            _lastTomographySliceIndex = _tomographySliceIndex;
            _lastTomographySliceAxis = _tomographySliceAxis;
        }
        
        // Auto-update checkbox
        ImGui.Checkbox("Auto-update during simulation", ref _autoUpdateTomography);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Automatically update tomography view while simulation is running");
        
        ImGui.Separator();

        // Show current state info
        if (_tomographyTexture != null && _tomographyTexture.IsValid)
        {
            ImGui.Text($"Current View: {(_tomographySliceAxis == 0 ? "YZ" : _tomographySliceAxis == 1 ? "XZ" : "XY")} plane at slice {_tomographySliceIndex}");
            if (_lastResults != null)
            {
                ImGui.Text($"Vp: {_lastResults.PWaveVelocity:F0} m/s | Vs: {_lastResults.SWaveVelocity:F0} m/s | Vp/Vs: {_lastResults.VpVsRatio:F3}");
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Generating tomography...");
        }
        
        ImGui.Separator();

        // --- Tomography View ---
        if (_tomographyTexture != null && _tomographyTexture.IsValid)
        {
            var availableSize = ImGui.GetContentRegionAvail();
            availableSize.Y -= 40; // Space for color bar
            if (availableSize.X > 50 && availableSize.Y > 50)
            {
                var imagePos = ImGui.GetCursorScreenPos();
                float aspectRatio = _tomographySliceAxis switch
                {
                    0 => (float)_parameters.Height / _parameters.Depth,
                    1 => (float)_parameters.Width / _parameters.Depth,
                    _ => (float)_parameters.Width / _parameters.Height
                };
                Vector2 imageSize;
                if (availableSize.X / availableSize.Y > aspectRatio)
                {
                    imageSize = new Vector2(availableSize.Y * aspectRatio, availableSize.Y);
                }
                else
                {
                    imageSize = new Vector2(availableSize.X, availableSize.X / aspectRatio);
                }

                // Draw image
                ImGui.Image(_tomographyTexture.GetImGuiTextureId(), imageSize);
                var drawList = ImGui.GetWindowDrawList();

                // --- Draw Wave Propagation Path ---
                DrawWavePathOverlay(drawList, imagePos, imageSize);
                
                // Show cursor position info when hovering
                if (ImGui.IsItemHovered())
                {
                    var mousePos = ImGui.GetMousePos();
                    var relPos = (mousePos - imagePos) / imageSize;
                    if (relPos.X >= 0 && relPos.X <= 1 && relPos.Y >= 0 && relPos.Y <= 1)
                    {
                        int pixelX = (int)(relPos.X * (_tomographySliceAxis == 0 ? _parameters.Height : _parameters.Width));
                        int pixelY = (int)(relPos.Y * (_tomographySliceAxis == 2 ? _parameters.Height : _parameters.Depth));
                        ImGui.SetTooltip($"Position: ({pixelX}, {pixelY})");
                    }
                }
            }
            DrawColorBar();
        }
        else
        {
            ImGui.Text("Generating tomography...");
            if (ImGui.Button("Retry Generation"))
            {
                _ = GenerateTomographyAsync();
            }
        }
        
        ImGui.Separator();
        
        // Export options
        if (ImGui.Button("Export Tomography Image"))
        {
            ExportTomographyImage();
        }
        ImGui.SameLine();
        if (ImGui.Button("Export All Slices"))
        {
            ExportAllTomographySlices();
        }
    }
    ImGui.End();

    if (_tomographyExportDialog.Submit())
    {
        // Fire-and-forget the async save method to avoid blocking the UI
        _ = SaveTomographyImageAsync(_tomographyExportDialog.SelectedPath);
    }
    
    // Auto-update during simulation
    if (_autoUpdateTomography && _isSimulating && _simulator != null)
    {
        if ((DateTime.Now - _lastTomographyUpdate).TotalSeconds > 1.0) // Update every second
        {
            _ = GenerateTomographyAsync();
            _lastTomographyUpdate = DateTime.Now;
        }
    }
}

        private void DrawWavePathOverlay(ImDrawListPtr drawList, Vector2 imagePos, Vector2 imageSize)
        {
            if (_parameters == null) return;
            
            // Get TX and RX positions in normalized 2D coordinates for the current slice
            Vector2 txPos2D = GetSliceCoordinates(_txPosition);
            Vector2 rxPos2D = GetSliceCoordinates(_rxPosition);

            // Convert to screen coordinates
            var txScreen = imagePos + txPos2D * imageSize;
            var rxScreen = imagePos + rxPos2D * imageSize;

            // Draw the full path
            drawList.AddLine(txScreen, rxScreen, 0x80FFFFFF, 1.5f);
            drawList.AddCircleFilled(txScreen, 5, 0xFF00FFFF, 12); // TX marker
            drawList.AddText(txScreen + new Vector2(8, -8), 0xFF00FFFF, "TX");
            drawList.AddCircleFilled(rxScreen, 5, 0xFF00FF00, 12); // RX marker
            drawList.AddText(rxScreen + new Vector2(8, -8), 0xFF00FF00, "RX");

            // Draw current wave position if simulating
            if (_isSimulating && _simulator != null && _lastResults != null && _lastResults.PWaveTravelTime > 0)
            {
                float progress = (float)_simulator.CurrentStep / _lastResults.PWaveTravelTime;
                progress = Math.Clamp(progress, 0.0f, 1.0f);
                var wavePosScreen = Vector2.Lerp(txScreen, rxScreen, progress);
                drawList.AddCircle(wavePosScreen, 8, 0xFF0080FF, 12, 2.0f);
            }
        }

        private Vector2 GetSliceCoordinates(Vector3 pos3D)
        {
            return _tomographySliceAxis switch
            {
                0 => new Vector2(pos3D.Y, pos3D.Z), // X-slice shows YZ plane
                1 => new Vector2(pos3D.X, pos3D.Z), // Y-slice shows XZ plane
                _ => new Vector2(pos3D.X, pos3D.Y), // Z-slice shows XY plane
            };
        }

        private void DrawColorBar()
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            var region = ImGui.GetContentRegionAvail();
            pos.X += region.X / 4; // Center it a bit
            float width = region.X / 2;
            float height = 20;

            for (int i = 0; i < width; i++)
            {
                float value = (float)i / width;
                Vector4 color = GetVelocityColor(value);
                uint col = ImGui.GetColorU32(color);
                drawList.AddRectFilled(new Vector2(pos.X + i, pos.Y), new Vector2(pos.X + i + 1, pos.Y + height), col);
            }
            ImGui.SetCursorScreenPos(new Vector2(pos.X - 50, pos.Y));
            ImGui.Text($"{_lastResults?.SWaveVelocity ?? 3000:F0} m/s");
            ImGui.SameLine(pos.X + width + 10);
            ImGui.Text($"{_lastResults?.PWaveVelocity ?? 6000:F0} m/s");
        }

        private Vector4 GetVelocityColor(float normalized)
        {
            float r, g, b;
            if (normalized < 0.25f) { r = 0; g = 4 * normalized; b = 1; }
            else if (normalized < 0.5f) { r = 0; g = 1; b = 1 - 4 * (normalized - 0.25f); }
            else if (normalized < 0.75f) { r = 4 * (normalized - 0.5f); g = 1; b = 0; }
            else { r = 1; g = 1 - 4 * (normalized - 0.75f); b = 0; }
            return new Vector4(r, g, b, 1);
        }

        private async Task RunSimulationAsync(CtImageStackDataset dataset)
{
    if (_isSimulating) return;
    _isSimulating = true;
    _cancellationTokenSource = new CancellationTokenSource();

    _currentState = SimulationState.Preparing;
    _preparationComplete = false;
    _preparationProgress = 0.0f;
    _preparationStatus = "Starting pre-computation...";

    try
    {
        _lastResults = null;
        if (_tomographyTexture != null)
        {
            _tomographyTexture.Dispose();
            _tomographyTexture = null;
        }
        CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);

        // Initialize simulation parameters with multi-material support
        _parameters = new SimulationParameters
        {
            Width = dataset.Width,
            Height = dataset.Height,
            Depth = dataset.Depth,
            PixelSize = (float)dataset.PixelSize / 1000.0f,
            
            // MULTI-MATERIAL SUPPORT: Use HashSet for multiple materials
            SelectedMaterialIDs = new HashSet<byte>(_selectedMaterialIDs),
            
            // Legacy single material (for backwards compatibility - set first material)
            SelectedMaterialID = _selectedMaterialIDs.FirstOrDefault(),
            
            Axis = _selectedAxisIndex,
            UseFullFaceTransducers = _useFullFaceTransducers,
            ConfiningPressureMPa = _confiningPressure,
            FailureAngleDeg = _failureAngle,
            CohesionMPa = _cohesion,
            SourceEnergyJ = _sourceEnergy,
            SourceFrequencyKHz = _sourceFrequency,
            SourceAmplitude = _sourceAmplitude,
            TimeSteps = _timeSteps,
            YoungsModulusMPa = _youngsModulus,
            PoissonRatio = _poissonRatio,
            UseElasticModel = _useElastic,
            UsePlasticModel = _usePlastic,
            UseBrittleModel = _useBrittle,
            UseGPU = _useGPU,
            UseRickerWavelet = _useRickerWavelet,
            TxPosition = _txPosition,
            RxPosition = _rxPosition,
            EnableRealTimeVisualization = _enableRealTimeVisualization,
            SaveTimeSeries = _saveTimeSeries,
            SnapshotInterval = _snapshotInterval,
            UseChunkedProcessing = _useChunkedProcessing,
            ChunkSizeMB = _chunkSizeMB,
            EnableOffloading = _enableOffloading,
            OffloadDirectory = _offloadDirectory
        };

        // Extract volume labels
        _preparationStatus = "Extracting volume labels...";
        _preparationProgress = 0.1f;
        var volumeLabels = await ExtractVolumeLabelsAsync(dataset);
        
        // Extract density volume with heterogeneous grayscale-based density
        _preparationStatus = "Extracting density volume...";
        _preparationProgress = 0.4f;
        var densityVolume = await ExtractDensityVolumeAsync(dataset, dataset.VolumeData);
        
        // Extract per-voxel material properties for heterogeneous simulations
        _preparationStatus = "Extracting material properties...";
        _preparationProgress = 0.7f;
        var (youngsModulusVolume, poissonRatioVolume) = await ExtractMaterialPropertiesVolumeAsync(dataset);

        // Initialize the simulator
        _preparationStatus = "Initializing simulator...";
        _preparationProgress = 0.9f;
        _simulator = new ChunkedAcousticSimulator(_parameters);
        
        // Register event handlers
        _simulator.ProgressUpdated += OnSimulationProgress;
        if (_enableRealTimeVisualization)
        {
            _simulator.WaveFieldUpdated += OnWaveFieldUpdated;
        }
        
        // Set per-voxel material properties (enables multi-material with different properties)
        _simulator.SetPerVoxelMaterialProperties(youngsModulusVolume, poissonRatioVolume);
        
        _preparationStatus = "Pre-computation completed.";
        _preparationProgress = 1.0f;
        _preparationComplete = true;
        await Task.Delay(1000, _cancellationTokenSource.Token);

        // Run the simulation
        _currentState = SimulationState.Simulating;
        
        _lastResults = await _simulator.RunAsync(volumeLabels, densityVolume, _cancellationTokenSource.Token);
        
        if (_lastResults != null)
        {
            _currentState = SimulationState.Completed;
    
            var damageField = _simulator.GetDamageField();
    
            // --- MODIFIED SECTION START ---
            _exportManager.SetDamageField(damageField);
            _exportManager.SetMaterialPropertyVolumes(densityVolume, youngsModulusVolume, poissonRatioVolume);
            _lastResults.DamageField = damageField;
            // --- MODIFIED SECTION END ---
    
            Logger.Log($"[AcousticSimulation] Simulation completed: Vp={_lastResults.PWaveVelocity:F2} m/s, Vs={_lastResults.SWaveVelocity:F2} m/s");
            
            // Add to calibration if single material selected
            if (_selectedMaterialIDs.Count == 1)
            {
                var material = dataset.Materials.First(m => m.ID == _selectedMaterialIDs.First());
                _calibrationManager.AddSimulationResult(
                    material.Name,
                    material.ID,
                    (float)material.Density,
                    _confiningPressure,
                    _youngsModulus,
                    _poissonRatio,
                    _lastResults.PWaveVelocity,
                    _lastResults.SWaveVelocity
                );
            }
            else if (_selectedMaterialIDs.Count > 1)
            {
                Logger.Log($"[AcousticSimulation] Multi-material simulation completed with {_selectedMaterialIDs.Count} materials. Calibration not added (single material only).");
            }
            
            // Generate initial tomography view
            await GenerateTomographyAsync();
        }
    }
    catch (OperationCanceledException)
    {
        Logger.Log("[AcousticSimulation] Simulation was cancelled.");
        _currentState = SimulationState.Cancelled;
    }
    catch (Exception ex)
    {
        Logger.LogError($"[AcousticSimulation] Simulation failed: {ex.Message}");
        Logger.LogError($"Stack trace: {ex.StackTrace}");
        _currentState = SimulationState.Failed;
    }
    finally
    {
        _isSimulating = false;
        if (_currentState == SimulationState.Simulating || _currentState == SimulationState.Preparing)
        {
            _currentState = SimulationState.Idle;
        }
        _simulator?.Dispose();
        _simulator = null;
        
        // Clear 3D preview overlay
        if (dataset != null)
        {
            CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);
        }
    }
}

private async Task<(float[,,] youngsModulus, float[,,] poissonRatio)> ExtractMaterialPropertiesVolumeAsync(CtImageStackDataset dataset)
{
    return await Task.Run(() =>
    {
        var youngsModulus = new float[dataset.Width, dataset.Height, dataset.Depth];
        var poissonRatio = new float[dataset.Width, dataset.Height, dataset.Depth];
        
        // Create fast lookup dictionary for material properties
        var materialProps = new Dictionary<byte, (float E, float Nu)>();
        
        foreach (var material in dataset.Materials)
        {
            if (material.ID == 0) continue;

            float E = _youngsModulus;  // Default from UI
            float Nu = _poissonRatio;  // Default from UI

            // If material has a physical material assigned, use its properties
            if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
            {
                var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                if (physMat != null)
                {
                    // Convert from GPa to MPa
                    E = (float)(physMat.YoungModulus_GPa ?? _youngsModulus) * 1000f;
                    Nu = (float)(physMat.PoissonRatio ?? _poissonRatio);
                }
            }
            
            materialProps[material.ID] = (E, Nu);
        }
        
        // Fill the volume with material properties
        Parallel.For(0, dataset.Depth, z =>
        {
            for (int y = 0; y < dataset.Height; y++)
            {
                for (int x = 0; x < dataset.Width; x++)
                {
                    byte label = dataset.LabelData[x, y, z];
                    
                    // If the material is in our selected set, use its properties
                    if (_selectedMaterialIDs.Contains(label) && materialProps.TryGetValue(label, out var props))
                    {
                        youngsModulus[x, y, z] = props.E;
                        poissonRatio[x, y, z] = props.Nu;
                    }
                    else
                    {
                        // For non-selected materials (barriers), use default properties
                        // These won't be processed anyway due to material filtering
                        youngsModulus[x, y, z] = _youngsModulus;
                        poissonRatio[x, y, z] = _poissonRatio;
                    }
                }
            }
        });

        Logger.Log($"[AcousticSimulation] Generated heterogeneous material property volumes for {materialProps.Count} materials.");
        return (youngsModulus, poissonRatio);
    });
}
        private async Task<byte[,,]> ExtractVolumeLabelsAsync(CtImageStackDataset dataset)
        {
            return await Task.Run(() =>
            {
                var labels = new byte[dataset.Width, dataset.Height, dataset.Depth];
                Parallel.For(0, dataset.Depth, z => { for (int y = 0; y < dataset.Height; y++) for (int x = 0; x < dataset.Width; x++) labels[x, y, z] = dataset.LabelData[x, y, z]; });
                return labels;
            });
        }

        private async Task<float[,,]> ExtractDensityVolumeAsync(CtImageStackDataset dataset, IGrayscaleVolumeData grayscaleVolume)
{
    return await Task.Run(() =>
    {
        // Step 1: Calculate the average grayscale value for each material.
        var materialStats = new Dictionary<byte, (long grayscaleSum, long voxelCount)>();

        for (int z = 0; z < dataset.Depth; z++)
        {
            var labelSlice = new byte[dataset.Width * dataset.Height];
            var graySlice = new byte[dataset.Width * dataset.Height];
            dataset.LabelData.ReadSliceZ(z, labelSlice);
            grayscaleVolume.ReadSliceZ(z, graySlice);

            for (int i = 0; i < labelSlice.Length; i++)
            {
                byte label = labelSlice[i];
                if (label == 0) continue; 

                if (!materialStats.ContainsKey(label))
                {
                    materialStats[label] = (0, 0);
                }
                var stats = materialStats[label];
                stats.grayscaleSum += graySlice[i];
                stats.voxelCount++;
                materialStats[label] = stats;
            }
        }

        var avgGrayscaleMap = new Dictionary<byte, float>();
        foreach (var item in materialStats)
        {
            if (item.Value.voxelCount > 0)
            {
                avgGrayscaleMap[item.Key] = (float)item.Value.grayscaleSum / item.Value.voxelCount;
            }
        }

        // Step 2: Create the final heterogeneous density volume.
        var density = new float[dataset.Width, dataset.Height, dataset.Depth];
        
        var materialDensityMap = dataset.Materials
            .ToDictionary(m => m.ID, m => (float)m.Density * 1000.0f); // g/cm^3 to kg/m^3

        for (int z = 0; z < dataset.Depth; z++)
        {
            var labelSlice = new byte[dataset.Width * dataset.Height];
            var graySlice = new byte[dataset.Width * dataset.Height];
            dataset.LabelData.ReadSliceZ(z, labelSlice);
            grayscaleVolume.ReadSliceZ(z, graySlice);

            for (int i = 0; i < labelSlice.Length; i++)
            {
                int x = i % dataset.Width;
                int y = i / dataset.Width;
                byte label = labelSlice[i];

                if (label == 0 || !materialDensityMap.ContainsKey(label))
                {
                    density[x, y, z] = 1.225f; // Density of air
                    continue;
                }

                float avgDensity = materialDensityMap[label];
                
                if (avgGrayscaleMap.TryGetValue(label, out float avgGray) && avgGray > 1.0f)
                {
                    density[x, y, z] = avgDensity * (graySlice[i] / avgGray);
                }
                else
                {
                    density[x, y, z] = avgDensity;
                }
                
                density[x, y, z] = Math.Max(100f, density[x, y, z]); // Minimum density
            }
        }
        
        Logger.Log("[AcousticSimulation] Generated heterogeneous density map based on grayscale values.");
        return density;
    });
}

        private async Task GenerateTomographyAsync()
        {
            SimulationResults resultsToUse = _lastResults;

            // If a simulation is running, get a live snapshot of the wave fields.
            if (_isSimulating && _simulator != null)
            {
                // Reconstruct the fields from the simulator's current state.
                resultsToUse = new SimulationResults
                {
                    WaveFieldVx = await _simulator.ReconstructFieldAsync(0),
                    WaveFieldVy = await _simulator.ReconstructFieldAsync(1),
                    WaveFieldVz = await _simulator.ReconstructFieldAsync(2),
                    PWaveVelocity = _lastResults?.PWaveVelocity ?? 6000.0,
                    SWaveVelocity = _lastResults?.SWaveVelocity ?? 3000.0,
                    VpVsRatio = _lastResults?.VpVsRatio ?? 2.0
                };
            }

            if (resultsToUse == null) return;

            // --- FIX START: Perform CPU-bound work in the background ---
            (byte[] tomography, int width, int height) tomogData = await Task.Run(() =>
            {
                var pixelData = _tomographyGenerator.Generate2DTomography(resultsToUse, _tomographySliceAxis, _tomographySliceIndex);
                if (pixelData == null) return (null, 0, 0);

                int w = _tomographySliceAxis switch { 0 => _parameters.Height, 1 => _parameters.Width, _ => _parameters.Width };
                int h = _tomographySliceAxis switch { 0 => _parameters.Depth, 1 => _parameters.Depth, _ => _parameters.Height };
        
                return (pixelData, w, h);
            });
            // --- FIX END: Background work is complete ---


            // --- FIX START: Update UI resources safely on the main thread ---
            if (tomogData.tomography != null)
            {
                // Now that we are back on the UI thread, dispose the old texture
                _tomographyTexture?.Dispose();
        
                // And create the new one
                _tomographyTexture = TextureManager.CreateFromPixelData(tomogData.tomography, (uint)tomogData.width, (uint)tomogData.height);
            }
            // --- FIX END: UI update is complete ---
        }

        private void OnWaveFieldUpdated(object sender, WaveFieldUpdateEventArgs e)
        {
            if ((DateTime.Now - _lastVisualizationUpdate).TotalSeconds >= _visualizationUpdateInterval)
            {
                _lastVisualizationUpdate = DateTime.Now;
                _currentWaveFieldMask = CreateWaveFieldMask(e.WaveField);
                CtImageStackTools.Update3DPreviewFromExternal(_currentDataset, _currentWaveFieldMask, new Vector4(1.0f, 0.5f, 0.0f, 0.5f));
            }
        }

        private byte[] CreateWaveFieldMask(float[,,] waveField)
        {
            int width = waveField.GetLength(0); int height = waveField.GetLength(1); int depth = waveField.GetLength(2);
            byte[] mask = new byte[width * height * depth];
            float maxAmplitude = 0;
            for (int z = 0; z < depth; z++) for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) maxAmplitude = Math.Max(maxAmplitude, Math.Abs(waveField[x, y, z]));
            if (maxAmplitude > 0)
            {
                int idx = 0;
                for (int z = 0; z < depth; z++) for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) { float normalized = Math.Abs(waveField[x, y, z]) / maxAmplitude; mask[idx++] = (byte)(normalized * 255); }
            }
            return mask;
        }

        private void CancelSimulation()
        {
            _cancellationTokenSource?.Cancel();
            Logger.Log("[AcousticSimulation] Simulation cancelled by user");
        }

        private void OnSimulationProgress(object sender, SimulationProgressEventArgs e) { /* UI polls progress directly */ }

        public void Dispose()
        {
            AcousticIntegration.OnPositionsChanged -= OnTransducerMoved;
            if (AcousticIntegration.IsActiveFor(_currentDataset))
            {
                AcousticIntegration.StopPlacement();
            }

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _simulator?.Dispose();
            _exportManager?.Dispose();
            _tomographyTexture?.Dispose();
            _tomographyGenerator?.Dispose();
            if (Directory.Exists(_offloadDirectory)) { try { Directory.Delete(_offloadDirectory, true); } catch { } }
        }
    }
}