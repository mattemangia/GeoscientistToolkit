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

        /// <summary>
        /// Updates the current positions for drawing markers without changing the placement state.
        /// </summary>
        public static void UpdateMarkerPositionsForDrawing(Dataset target, Vector3 currentTx, Vector3 currentRx)
        {
            _targetDataset = target;
            TxPosition = currentTx;
            RxPosition = currentRx;
        }

        /// <summary>
        /// Initiates an interactive placement session for either the TX or RX.
        /// </summary>
        public static void StartPlacement(Dataset target, string which)
        {
            IsPlacing = true;
            _targetDataset = target;
            _placingWhich = which;
        }


        public static void StopPlacement()
        {
            IsPlacing = false;
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
            else if (_placingWhich == "RX") // Explicitly check for RX
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
        
        // --- NEW: Tomography viewer ---
        private readonly RealTimeTomographyViewer _tomographyViewer;
        private bool _showTomographyWindow = false;

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
            _tomographyViewer = new RealTimeTomographyViewer(); // Initialize the new viewer
            _offloadDirectory = Path.Combine(Path.GetTempPath(), "AcousticSimulation");
            Directory.CreateDirectory(_offloadDirectory);
            AcousticIntegration.OnPositionsChanged += OnTransducerMoved;
            
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
            
            // --- FIX: Use the corrected method to update marker positions for drawing ---
            // This ensures the current TX/RX positions are known to viewers without changing placement state.
            AcousticIntegration.UpdateMarkerPositionsForDrawing(dataset, _txPosition, _rxPosition);

            // --- Call the new viewer's draw method ---
            _tomographyViewer.Draw();

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
                AcousticIntegration.UpdateMarkerPositionsForDrawing(_currentDataset, _txPosition, _rxPosition);
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
                
                // Control to show the new viewer window
                if (ImGui.Checkbox("Show Velocity Tomography", ref _showTomographyWindow))
                {
                    if (_showTomographyWindow)
                    {
                        _tomographyViewer.Show();
                    }
                }
                
                ImGui.Checkbox("Auto-update during simulation", ref _autoUpdateTomography);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Automatically update tomography view while simulation is running");

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
                            AcousticIntegration.StartPlacement(dataset, "TX");
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
                            AcousticIntegration.StartPlacement(dataset, "RX");
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

                // --- Live update hook for the tomography viewer ---
                if (_showTomographyWindow && _autoUpdateTomography && (DateTime.Now - _lastTomographyUpdate).TotalSeconds > 1.0)
                {
                    _ = UpdateLiveTomographyAsync();
                    _lastTomographyUpdate = DateTime.Now;
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
                        ImGui.Text($"Damage Field: Available ({_lastResults.DamageField.Length} voxels)");
                    }
            
                    _exportManager.DrawExportControls(_lastResults, _parameters, dataset, _lastResults.DamageField);
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
        
        private async Task UpdateLiveTomographyAsync()
        {
            if (_simulator == null) return;

            var liveResults = new SimulationResults
            {
                WaveFieldVx = await _simulator.ReconstructFieldAsync(0),
                WaveFieldVy = await _simulator.ReconstructFieldAsync(1),
                WaveFieldVz = await _simulator.ReconstructFieldAsync(2),
                PWaveVelocity = _lastResults?.PWaveVelocity ?? 6000.0,
                SWaveVelocity = _lastResults?.SWaveVelocity ?? 3000.0,
                VpVsRatio = _lastResults?.VpVsRatio ?? 2.0
            };

            var dimensions = new Vector3(_parameters.Width, _parameters.Height, _parameters.Depth);
            _tomographyViewer.UpdateLiveData(liveResults, dimensions);
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
            AcousticIntegration.UpdateMarkerPositionsForDrawing(dataset, _txPosition, _rxPosition);
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
                
                CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);

                _parameters = new SimulationParameters
                {
                    Width = dataset.Width,
                    Height = dataset.Height,
                    Depth = dataset.Depth,
                    PixelSize = (float)dataset.PixelSize / 1000.0f,
                    SelectedMaterialIDs = new HashSet<byte>(_selectedMaterialIDs),
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
                if (_enableRealTimeVisualization)
                {
                    _simulator.WaveFieldUpdated += OnWaveFieldUpdated;
                }
                
                _simulator.SetPerVoxelMaterialProperties(youngsModulusVolume, poissonRatioVolume);
                
                _preparationStatus = "Pre-computation completed.";
                _preparationProgress = 1.0f;
                _preparationComplete = true;
                await Task.Delay(1000, _cancellationTokenSource.Token);

                _currentState = SimulationState.Simulating;
                
                _lastResults = await _simulator.RunAsync(volumeLabels, densityVolume, _cancellationTokenSource.Token);
                
                if (_lastResults != null)
                {
                    _currentState = SimulationState.Completed;
            
                    var damageField = _simulator.GetDamageField();
            
                    _exportManager.SetDamageField(damageField);
                    _exportManager.SetMaterialPropertyVolumes(densityVolume, youngsModulusVolume, poissonRatioVolume);
                    _lastResults.DamageField = damageField;
            
                    Logger.Log($"[AcousticSimulation] Simulation completed: Vp={_lastResults.PWaveVelocity:F2} m/s, Vs={_lastResults.SWaveVelocity:F2} m/s");
                    
                    if (_selectedMaterialIDs.Count == 1)
                    {
                        var material = dataset.Materials.First(m => m.ID == _selectedMaterialIDs.First());
                        _calibrationManager.AddSimulationResult(
                            material.Name, material.ID, (float)material.Density, _confiningPressure,
                            _youngsModulus, _poissonRatio, _lastResults.PWaveVelocity, _lastResults.SWaveVelocity
                        );
                    }
                    else if (_selectedMaterialIDs.Count > 1)
                    {
                        Logger.Log($"[AcousticSimulation] Multi-material simulation completed with {_selectedMaterialIDs.Count} materials. Calibration not added (single material only).");
                    }
                    
                    // --- Set final data on the tomography viewer ---
                    _tomographyViewer.SetFinalData(_lastResults, new Vector3(_parameters.Width, _parameters.Height, _parameters.Depth));
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
                
                var materialProps = new Dictionary<byte, (float E, float Nu)>();
                
                foreach (var material in dataset.Materials)
                {
                    if (material.ID == 0) continue;

                    float E = _youngsModulus;
                    float Nu = _poissonRatio;

                    if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                    {
                        var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                        if (physMat != null)
                        {
                            E = (float)(physMat.YoungModulus_GPa ?? _youngsModulus) * 1000f;
                            Nu = (float)(physMat.PoissonRatio ?? _poissonRatio);
                        }
                    }
                    
                    materialProps[material.ID] = (E, Nu);
                }
                
                Parallel.For(0, dataset.Depth, z =>
                {
                    for (int y = 0; y < dataset.Height; y++)
                    {
                        for (int x = 0; x < dataset.Width; x++)
                        {
                            byte label = dataset.LabelData[x, y, z];
                            
                            if (_selectedMaterialIDs.Contains(label) && materialProps.TryGetValue(label, out var props))
                            {
                                youngsModulus[x, y, z] = props.E;
                                poissonRatio[x, y, z] = props.Nu;
                            }
                            else
                            {
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

                var density = new float[dataset.Width, dataset.Height, dataset.Depth];
                
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
            _tomographyViewer?.Dispose();
            if (Directory.Exists(_offloadDirectory)) { try { Directory.Delete(_offloadDirectory, true); } catch { } }
        }
    }
}