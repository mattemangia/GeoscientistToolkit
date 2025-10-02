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
        private int _selectedMaterialIndex = 0;
        private int _selectedAxisIndex = 0;
        private float _confiningPressure = 1.0f;
        private float _tensileStrength = 10.0f;
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
        }

        private void OnTransducerMoved()
        {
            _txPosition = AcousticIntegration.TxPosition;
            _rxPosition = AcousticIntegration.RxPosition;
        }

        public void DrawPanel(CtImageStackDataset dataset)
{
    if (dataset == null) return;
    
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
    ImGui.Text("Target Material:");
    var materials = dataset.Materials.Where(m => m.ID != 0).Select(m => m.Name).ToArray();
    if (materials.Length == 0)
    {
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "No materials available for simulation.");
        return;
    }

    ImGui.SetNextItemWidth(-1);
    ImGui.Combo("##Material", ref _selectedMaterialIndex, materials, materials.Length);
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
                    Logger.Log("[Simulation] Change offload directory not yet implemented");
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
        if (!prevTomographyOpen && _isTomographyWindowOpen && _lastResults != null)
        {
            // Window was just opened, regenerate tomography
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

        // Show properties for selected material
        var selectedMaterial = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1.0f, 1), $"Selected Material: {selectedMaterial.Name}");

        if (!string.IsNullOrEmpty(selectedMaterial.PhysicalMaterialName))
        {
            var physMat = MaterialLibrary.Instance.Find(selectedMaterial.PhysicalMaterialName);
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

        ImGui.Separator();

        // Allow manual override
        ImGui.Text("Simulation Parameters (override material library):");
        ImGui.DragFloat("Young's Modulus (MPa)", ref _youngsModulus, 100.0f, 100.0f, 200000.0f);
        ImGui.DragFloat("Poisson's Ratio", ref _poissonRatio, 0.01f, 0.0f, 0.49f);

        if (_calibrationManager.HasCalibration)
        {
            if (ImGui.Button("Apply Calibration from Lab Data"))
            {
                var material = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
                var (__calE, __calNu) = _calibrationManager.GetCalibratedParameters((float)material.Density, _confiningPressure);
                _youngsModulus = __calE;
                _poissonRatio  = __calNu;
                Logger.Log($"[Simulation] Applied calibration: E={_youngsModulus:F2} MPa, ν={_poissonRatio:F4}");
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

        if (_lastResults != null)
        {
            // Get density from the currently selected material for the estimation
            float density = (float)selectedMaterial.Density; // in g/cm³
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
        ImGui.DragFloat("Tensile Strength (MPa)", ref _tensileStrength, 0.5f, 0.1f, 100.0f);
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
        ImGui.DragFloat("Source Energy (J)", ref _sourceEnergy, 0.1f, 0.01f, 10.0f);
        ImGui.DragFloat("Frequency (kHz)", ref _sourceFrequency, 10.0f, 1.0f, 5000.0f);
        ImGui.DragInt("Amplitude", ref _sourceAmplitude, 1, 1, 1000);
        ImGui.DragInt("Time Steps", ref _timeSteps, 10, 100, 10000);

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
                ImGui.SetTooltip("Automatically place TX/RX on opposite sides of the selected material's largest connected component.");

            float dx = (_rxPosition.X - _txPosition.X) * dataset.Width * dataset.PixelSize / 1000f;
            float dy = (_rxPosition.Y - _txPosition.Y) * dataset.Height * dataset.PixelSize / 1000f;
            float dz = (_rxPosition.Z - _txPosition.Z) * dataset.Depth * dataset.SliceThickness / 1000f;
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
        bool canSimulate = materials.Length > 0;
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
                _exportManager.SetDamageField(_lastResults.DamageField);
                ImGui.Text($"Damage Field: Available ({_lastResults.DamageField.Length} voxels)");
            }
    
            _exportManager.DrawExportControls(_lastResults, _parameters, dataset);
            ImGui.Spacing();

            if (ImGui.Button("Add to Calibration Database"))
            {
                var material = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
                _calibrationManager.AddSimulationResult(material.Name, material.ID, (float)material.Density, 
                    _confiningPressure, _youngsModulus, _poissonRatio, 
                    _lastResults.PWaveVelocity, _lastResults.SWaveVelocity);
                Logger.Log("[Simulation] Added results to calibration database");
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
}
private void ExportTomographyImage()
{
    if (_lastResults == null)
    {
        Logger.LogWarning("[Tomography] Cannot export, no simulation results available.");
        return;
    }
    
    string filename = $"Tomography_{(_tomographySliceAxis == 0 ? "X" : _tomographySliceAxis == 1 ? "Y" : "Z")}_{_tomographySliceIndex}_{DateTime.Now:yyyyMMdd_HHmmss}";
    _tomographyExportDialog.Open(filename);
}

private void SaveTomographyImage(string path)
{
    if (_lastResults == null) return;

    // 1. Get dimensions
    int width = _tomographySliceAxis switch { 0 => _parameters.Height, 1 => _parameters.Width, _ => _parameters.Width };
    int height = _tomographySliceAxis switch { 0 => _parameters.Depth, 1 => _parameters.Depth, _ => _parameters.Height };
    
    // 2. Regenerate the pixel data
    var rgbaData = _tomographyGenerator.Generate2DTomography(_lastResults, _tomographySliceAxis, _tomographySliceIndex);
    if (rgbaData == null)
    {
        Logger.LogError("[Tomography] Failed to generate pixel data for export.");
        return;
    }
    
    // 3. Save to PNG using StbImageWriteSharp
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
    var material = dataset.Materials.Where(m => m.ID != 0).ElementAtOrDefault(_selectedMaterialIndex);
    if (material == null)
    {
        Logger.LogWarning("[AutoPlace] No material selected.");
        return;
    }

    Logger.Log($"[AutoPlace] Finding optimal transducer positions for material '{material.Name}' (ID: {material.ID})...");

    var autoPlacer = new TransducerAutoPlacer(dataset, material.ID);
    var bounds = await Task.Run(() => autoPlacer.FindLargestIslandAndBounds());

    if (!bounds.HasValue)
    {
        Logger.LogError($"[AutoPlace] Material ID {material.ID} not found or has no volume in the dataset.");
        return;
    }

    var (min, max) = bounds.Value;
    Logger.Log($"[AutoPlace] Largest connected component bounds: Min({min}), Max({max})");

    // Calculate the extent along each axis
    float xExtent = max.X - min.X;
    float yExtent = max.Y - min.Y;
    float zExtent = max.Z - min.Z;

    // Place transducers along the selected axis with a small buffer to ensure they're inside the material
    const int buffer = 3; // Place transducers 3 voxels inside the material bounds
    Vector3 txVoxel, rxVoxel;

    switch (_selectedAxisIndex)
    {
        case 0: // X-Axis - Place along X, centered in Y and Z
            {
                float centerY = (min.Y + max.Y) / 2.0f;
                float centerZ = (min.Z + max.Z) / 2.0f;
                txVoxel = new Vector3(min.X + buffer, centerY, centerZ);
                rxVoxel = new Vector3(max.X - buffer, centerY, centerZ);
                
                // Ensure we have enough distance
                if (rxVoxel.X - txVoxel.X < 10)
                {
                    Logger.LogWarning($"[AutoPlace] Material too narrow along X-axis. Minimum placement distance.");
                    txVoxel.X = min.X + 1;
                    rxVoxel.X = max.X - 1;
                }
            }
            break;
            
        case 1: // Y-Axis - Place along Y, centered in X and Z
            {
                float centerX = (min.X + max.X) / 2.0f;
                float centerZ = (min.Z + max.Z) / 2.0f;
                txVoxel = new Vector3(centerX, min.Y + buffer, centerZ);
                rxVoxel = new Vector3(centerX, max.Y - buffer, centerZ);
                
                // Ensure we have enough distance
                if (rxVoxel.Y - txVoxel.Y < 10)
                {
                    Logger.LogWarning($"[AutoPlace] Material too narrow along Y-axis. Minimum placement distance.");
                    txVoxel.Y = min.Y + 1;
                    rxVoxel.Y = max.Y - 1;
                }
            }
            break;
            
        case 2: // Z-Axis - Place along Z, centered in X and Y
            {
                float centerX = (min.X + max.X) / 2.0f;
                float centerY = (min.Y + max.Y) / 2.0f;
                txVoxel = new Vector3(centerX, centerY, min.Z + buffer);
                rxVoxel = new Vector3(centerX, centerY, max.Z - buffer);
                
                // Ensure we have enough distance
                if (rxVoxel.Z - txVoxel.Z < 10)
                {
                    Logger.LogWarning($"[AutoPlace] Material too narrow along Z-axis. Minimum placement distance.");
                    txVoxel.Z = min.Z + 1;
                    rxVoxel.Z = max.Z - 1;
                }
            }
            break;
            
        default:
            return;
    }

    // Verify that both positions are actually within the material
    bool txValid = IsPositionInMaterial(dataset, txVoxel, material.ID);
    bool rxValid = IsPositionInMaterial(dataset, rxVoxel, material.ID);

    if (!txValid || !rxValid)
    {
        Logger.LogWarning($"[AutoPlace] Initial positions not in material. Attempting to find valid positions...");
        
        // Try to find valid positions by searching inward from the bounds
        (txVoxel, rxVoxel) = await Task.Run(() => 
            FindValidTransducerPositions(dataset, material.ID, min, max, _selectedAxisIndex));
        
        txValid = IsPositionInMaterial(dataset, txVoxel, material.ID);
        rxValid = IsPositionInMaterial(dataset, rxVoxel, material.ID);
        
        if (!txValid || !rxValid)
        {
            Logger.LogError($"[AutoPlace] Could not find valid positions within the material.");
            return;
        }
    }

    // Verify there's a path between TX and RX
    Logger.Log($"[AutoPlace] Verifying path between TX and RX...");
    bool hasPath = await Task.Run(() => autoPlacer.HasPath(txVoxel, rxVoxel));
    
    if (!hasPath)
    {
        Logger.LogWarning($"[AutoPlace] No direct path found between TX and RX. The material may be fragmented.");
        // Continue anyway - the user can adjust manually if needed
    }

    // Normalize positions for the UI (0-1 range)
    _txPosition = new Vector3(
        Math.Clamp(txVoxel.X / dataset.Width, 0f, 1f),
        Math.Clamp(txVoxel.Y / dataset.Height, 0f, 1f),
        Math.Clamp(txVoxel.Z / dataset.Depth, 0f, 1f));

    _rxPosition = new Vector3(
        Math.Clamp(rxVoxel.X / dataset.Width, 0f, 1f),
        Math.Clamp(rxVoxel.Y / dataset.Height, 0f, 1f),
        Math.Clamp(rxVoxel.Z / dataset.Depth, 0f, 1f));

    // Update the integration
    AcousticIntegration.StartPlacement(dataset, null, _txPosition, _rxPosition);
    OnTransducerMoved();
    
    // Calculate and log the distance
    float dx = (rxVoxel.X - txVoxel.X) * dataset.PixelSize / 1000f;
    float dy = (rxVoxel.Y - txVoxel.Y) * dataset.PixelSize / 1000f;
    float dz = (rxVoxel.Z - txVoxel.Z) * dataset.SliceThickness / 1000f;
    float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    
    Logger.Log($"[AutoPlace] Successfully placed transducers:");
    Logger.Log($"  TX: Voxel({txVoxel.X:F0},{txVoxel.Y:F0},{txVoxel.Z:F0}) -> Normalized({_txPosition.X:F3},{_txPosition.Y:F3},{_txPosition.Z:F3})");
    Logger.Log($"  RX: Voxel({rxVoxel.X:F0},{rxVoxel.Y:F0},{rxVoxel.Z:F0}) -> Normalized({_rxPosition.X:F3},{_rxPosition.Y:F3},{_rxPosition.Z:F3})");
    Logger.Log($"  Distance: {distance:F2} mm");
    Logger.Log($"  Path verified: {(hasPath ? "Yes" : "No (fragmented material)")}");
}
        private bool IsPositionInMaterial(CtImageStackDataset dataset, Vector3 position, byte materialId)
{
    int x = Math.Clamp((int)position.X, 0, dataset.Width - 1);
    int y = Math.Clamp((int)position.Y, 0, dataset.Height - 1);
    int z = Math.Clamp((int)position.Z, 0, dataset.Depth - 1);
    return dataset.LabelData[x, y, z] == materialId;
}

private (Vector3 tx, Vector3 rx) FindValidTransducerPositions(
    CtImageStackDataset dataset, byte materialId, Vector3 min, Vector3 max, int axis)
{
    Vector3 txPos = new Vector3();
    Vector3 rxPos = new Vector3();
    
    // Search for valid positions by scanning inward from the bounds
    switch (axis)
    {
        case 0: // X-axis
            {
                float centerY = (min.Y + max.Y) / 2.0f;
                float centerZ = (min.Z + max.Z) / 2.0f;
                
                // Find TX position (searching from min.X inward)
                for (int x = (int)min.X; x <= (int)max.X; x++)
                {
                    if (IsPositionInMaterial(dataset, new Vector3(x, centerY, centerZ), materialId))
                    {
                        txPos = new Vector3(x, centerY, centerZ);
                        break;
                    }
                }
                
                // Find RX position (searching from max.X inward)
                for (int x = (int)max.X; x >= (int)min.X; x--)
                {
                    if (IsPositionInMaterial(dataset, new Vector3(x, centerY, centerZ), materialId))
                    {
                        rxPos = new Vector3(x, centerY, centerZ);
                        break;
                    }
                }
            }
            break;
            
        case 1: // Y-axis
            {
                float centerX = (min.X + max.X) / 2.0f;
                float centerZ = (min.Z + max.Z) / 2.0f;
                
                // Find TX position (searching from min.Y inward)
                for (int y = (int)min.Y; y <= (int)max.Y; y++)
                {
                    if (IsPositionInMaterial(dataset, new Vector3(centerX, y, centerZ), materialId))
                    {
                        txPos = new Vector3(centerX, y, centerZ);
                        break;
                    }
                }
                
                // Find RX position (searching from max.Y inward)
                for (int y = (int)max.Y; y >= (int)min.Y; y--)
                {
                    if (IsPositionInMaterial(dataset, new Vector3(centerX, y, centerZ), materialId))
                    {
                        rxPos = new Vector3(centerX, y, centerZ);
                        break;
                    }
                }
            }
            break;
            
        case 2: // Z-axis
            {
                float centerX = (min.X + max.X) / 2.0f;
                float centerY = (min.Y + max.Y) / 2.0f;
                
                // Find TX position (searching from min.Z inward)
                for (int z = (int)min.Z; z <= (int)max.Z; z++)
                {
                    if (IsPositionInMaterial(dataset, new Vector3(centerX, centerY, z), materialId))
                    {
                        txPos = new Vector3(centerX, centerY, z);
                        break;
                    }
                }
                
                // Find RX position (searching from max.Z inward)
                for (int z = (int)max.Z; z >= (int)min.Z; z--)
                {
                    if (IsPositionInMaterial(dataset, new Vector3(centerX, centerY, z), materialId))
                    {
                        rxPos = new Vector3(centerX, centerY, z);
                        break;
                    }
                }
            }
            break;
    }
    
    return (txPos, rxPos);
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

        private void DrawTomographyWindow()
{
    ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
    if (ImGui.Begin("Velocity Tomography", ref _isTomographyWindowOpen))
    {
        if (_lastResults == null)
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
        SaveTomographyImage(_tomographyExportDialog.SelectedPath);
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

    // --- MODIFICATION: Set up and report pre-computation state ---
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

        var material = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
        
        _parameters = new SimulationParameters
        {
            Width = dataset.Width, Height = dataset.Height, Depth = dataset.Depth,
            PixelSize = (float)dataset.PixelSize / 1000.0f,
            SelectedMaterialID = material.ID, Axis = _selectedAxisIndex, UseFullFaceTransducers = _useFullFaceTransducers,
            ConfiningPressureMPa = _confiningPressure, TensileStrengthMPa = _tensileStrength, 
            FailureAngleDeg = _failureAngle, CohesionMPa = _cohesion,
            SourceEnergyJ = _sourceEnergy, SourceFrequencyKHz = _sourceFrequency, 
            SourceAmplitude = _sourceAmplitude, TimeSteps = _timeSteps,
            YoungsModulusMPa = _youngsModulus, PoissonRatio = _poissonRatio,
            UseElasticModel = _useElastic, UsePlasticModel = _usePlastic, UseBrittleModel = _useBrittle, UseGPU = _useGPU,
            TxPosition = _txPosition, RxPosition = _rxPosition, EnableRealTimeVisualization = _enableRealTimeVisualization,
            SaveTimeSeries = _saveTimeSeries, SnapshotInterval = _snapshotInterval,
            UseChunkedProcessing = _useChunkedProcessing, ChunkSizeMB = _chunkSizeMB, 
            EnableOffloading = _enableOffloading, OffloadDirectory = _offloadDirectory
        };

        // --- MODIFICATION: Update progress during data extraction ---
        _preparationStatus = "Extracting volume labels...";
        _preparationProgress = 0.1f;
        var volumeLabels = await ExtractVolumeLabelsAsync(dataset);
        
        _preparationStatus = "Extracting density volume...";
        _preparationProgress = 0.4f;
        var densityVolume = await ExtractDensityVolumeAsync(dataset, dataset.VolumeData);
        
        _preparationStatus = "Extracting material properties...";
        _preparationProgress = 0.7f;
        var (youngsModulusVolume, poissonRatioVolume) = await ExtractMaterialPropertiesVolumeAsync(dataset);

        _preparationStatus = "Initializing simulator...";
        _preparationProgress = 0.9f;
        _simulator = new ChunkedAcousticSimulator(_parameters);
        
        _simulator.ProgressUpdated += OnSimulationProgress;
        if (_enableRealTimeVisualization) { _simulator.WaveFieldUpdated += OnWaveFieldUpdated; }
        
        _simulator.SetPerVoxelMaterialProperties(youngsModulusVolume, poissonRatioVolume);
        
        // --- MODIFICATION: Mark preparation as complete and pause briefly ---
        _preparationStatus = "Pre-computation completed.";
        _preparationProgress = 1.0f;
        _preparationComplete = true;
        await Task.Delay(1000, _cancellationTokenSource.Token); // Give user a moment to see the message

        _currentState = SimulationState.Simulating;
        
        _lastResults = await _simulator.RunAsync(volumeLabels, densityVolume, _cancellationTokenSource.Token);
        
        if (_lastResults != null)
        {
            _currentState = SimulationState.Completed;
            var damageField = _simulator.GetDamageField();
            _exportManager.SetDamageField(damageField);
            _lastResults.DamageField = damageField;
            Logger.Log($"[AcousticSimulation] Simulation completed with damage field: Vp={_lastResults.PWaveVelocity:F2} m/s, Vs={_lastResults.SWaveVelocity:F2} m/s");
            
            _calibrationManager.AddSimulationResult(material.Name, material.ID, (float)material.Density, _confiningPressure, _youngsModulus, _poissonRatio, _lastResults.PWaveVelocity, _lastResults.SWaveVelocity);
            
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
        _currentState = SimulationState.Failed;
    }
    finally 
    { 
        _isSimulating = false; 
        if (_currentState == SimulationState.Simulating || _currentState == SimulationState.Preparing)
        {
            _currentState = SimulationState.Idle; // Reset state if it ends unexpectedly
        }
        _simulator?.Dispose(); 
        _simulator = null;
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
        
        if (_useChunkedProcessing)
        {
            int chunkDepth = Math.Max(1, _chunkSizeMB * 1024 * 1024 / (dataset.Width * dataset.Height * sizeof(float) * 2));
            for (int z0 = 0; z0 < dataset.Depth; z0 += chunkDepth)
            {
                int z1 = Math.Min(z0 + chunkDepth, dataset.Depth);
                Parallel.For(z0, z1, z => 
                {
                    var labelSlice = new byte[dataset.Width * dataset.Height];
                    dataset.LabelData.ReadSliceZ(z, labelSlice);
                    
                    for (int i = 0; i < labelSlice.Length; i++)
                    {
                        int x = i % dataset.Width;
                        int y = i / dataset.Width;
                        byte label = labelSlice[i];
                        
                        var material = dataset.Materials.FirstOrDefault(m => m.ID == label);
                        if (material != null && !string.IsNullOrEmpty(material.PhysicalMaterialName))
                        {
                            var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                            if (physMat != null)
                            {
                                youngsModulus[x, y, z] = (float)(physMat.YoungModulus_GPa ?? _youngsModulus) * 1000f; // Convert GPa to MPa
                                poissonRatio[x, y, z] = (float)(physMat.PoissonRatio ?? _poissonRatio);
                            }
                            else
                            {
                                youngsModulus[x, y, z] = _youngsModulus;
                                poissonRatio[x, y, z] = _poissonRatio;
                            }
                        }
                        else
                        {
                            youngsModulus[x, y, z] = _youngsModulus;
                            poissonRatio[x, y, z] = _poissonRatio;
                        }
                    }
                });
            }
        }
        else
        {
            Parallel.For(0, dataset.Depth, z => 
            {
                var labelSlice = new byte[dataset.Width * dataset.Height];
                dataset.LabelData.ReadSliceZ(z, labelSlice);
                
                for (int i = 0; i < labelSlice.Length; i++)
                {
                    int x = i % dataset.Width;
                    int y = i / dataset.Width;
                    byte label = labelSlice[i];
                    
                    var material = dataset.Materials.FirstOrDefault(m => m.ID == label);
                    if (material != null && !string.IsNullOrEmpty(material.PhysicalMaterialName))
                    {
                        var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                        if (physMat != null)
                        {
                            youngsModulus[x, y, z] = (float)(physMat.YoungModulus_GPa ?? _youngsModulus) * 1000f;
                            poissonRatio[x, y, z] = (float)(physMat.PoissonRatio ?? _poissonRatio);
                        }
                        else
                        {
                            youngsModulus[x, y, z] = _youngsModulus;
                            poissonRatio[x, y, z] = _poissonRatio;
                        }
                    }
                    else
                    {
                        youngsModulus[x, y, z] = _youngsModulus;
                        poissonRatio[x, y, z] = _poissonRatio;
                    }
                }
            });
        }
        return (youngsModulus, poissonRatio);
    });
}
        private async Task<byte[,,]> ExtractVolumeLabelsAsync(CtImageStackDataset dataset)
        {
            return await Task.Run(() =>
            {
                var labels = new byte[dataset.Width, dataset.Height, dataset.Depth];
                if (_useChunkedProcessing)
                {
                    int chunkDepth = Math.Max(1, _chunkSizeMB * 1024 * 1024 / (dataset.Width * dataset.Height));
                    for (int z0 = 0; z0 < dataset.Depth; z0 += chunkDepth)
                    {
                        int z1 = Math.Min(z0 + chunkDepth, dataset.Depth);
                        Parallel.For(z0, z1, z => { for (int y = 0; y < dataset.Height; y++) for (int x = 0; x < dataset.Width; x++) labels[x, y, z] = dataset.LabelData[x, y, z]; });
                    }
                }
                else { Parallel.For(0, dataset.Depth, z => { for (int y = 0; y < dataset.Height; y++) for (int x = 0; x < dataset.Width; x++) labels[x, y, z] = dataset.LabelData[x, y, z]; }); }
                return labels;
            });
        }

        private async Task<float[,,]> ExtractDensityVolumeAsync(CtImageStackDataset dataset, IGrayscaleVolumeData grayscaleVolume)
{
    return await Task.Run(() =>
    {
        // Step 1: Calculate the average grayscale value for each material.
        // This is done by summing up the grayscale values and counting the voxels for each material ID.
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
                if (label == 0) continue; // Skip exterior/background material

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
        
        // Pre-fetch material densities and convert them from g/cm^3 to kg/m^3 for the physics engine.
        var materialDensityMap = dataset.Materials
            .ToDictionary(m => m.ID, m => (float)m.Density * 1000.0f); 

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
                    density[x, y, z] = 1.225f; // Assign density of air for the exterior
                    continue;
                }

                float avgDensity = materialDensityMap[label];
                
                // If we have stats for this material, calculate the voxel-specific density.
                if (avgGrayscaleMap.TryGetValue(label, out float avgGray) && avgGray > 1.0f) // Avoid division by zero
                {
                    // Core logic: Scale the average density by the ratio of the voxel's grayscale to the material's average grayscale.
                    density[x, y, z] = avgDensity * (graySlice[i] / avgGray);
                }
                else
                {
                    // Fallback to homogeneous density if no voxels were found for this material.
                    density[x, y, z] = avgDensity;
                }
                
                // Ensure a minimum density to prevent simulation instability.
                density[x, y, z] = Math.Max(100f, density[x, y, z]); // Minimum of 100 kg/m^3
            }
        }
        
        Logger.Log("[AcousticSimulation] Generated heterogeneous density map based on grayscale values.");
        return density;
    });
}

        private async Task GenerateTomographyAsync()
        {
            if (_lastResults == null) return;
            await Task.Run(() =>
            {
                var tomography = _tomographyGenerator.Generate2DTomography(_lastResults, _tomographySliceAxis, _tomographySliceIndex);
                if (tomography != null)
                {
                    _tomographyTexture?.Dispose();
                    
                    int width = _tomographySliceAxis switch { 0 => _parameters.Height, 1 => _parameters.Width, _ => _parameters.Width };
                    int height = _tomographySliceAxis switch { 0 => _parameters.Depth, 1 => _parameters.Depth, _ => _parameters.Height };
                    
                    _tomographyTexture = TextureManager.CreateFromPixelData(tomography, (uint)width, (uint)height);
                }
            });
        }

        private void OnWaveFieldUpdated(object sender, WaveFieldUpdateEventArgs e)
        {
            if ((DateTime.Now - _lastVisualizationUpdate).TotalSeconds >= _visualizationUpdateInterval)
            {
                _lastVisualizationUpdate = DateTime.Now;
                _currentWaveFieldMask = CreateWaveFieldMask(e.WaveField);
                // The third parameter (color) is now a bright orange for better visibility
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