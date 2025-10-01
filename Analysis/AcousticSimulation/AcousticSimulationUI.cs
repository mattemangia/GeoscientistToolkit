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
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using DensityTool = GeoscientistToolkit.UI.Tools.DensityCalibrationTool;

namespace GeoscientistToolkit.Analysis.AcousticSimulation
{
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
        private bool _showTomography = false;
        private int _tomographySliceAxis = 0;
        private int _tomographySliceIndex = 0;
        private TextureManager _tomographyTexture;
        
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

        public AcousticSimulationUI()
        {
            _parameters = new SimulationParameters();
            _calibrationManager = new UnifiedCalibrationManager();
            _exportManager = new AcousticExportManager();
            _tomographyGenerator = new VelocityTomographyGenerator();
            _offloadDirectory = Path.Combine(Path.GetTempPath(), "AcousticSimulation");
            Directory.CreateDirectory(_offloadDirectory);
        }

        public void DrawPanel(CtImageStackDataset dataset)
        {
            if (dataset == null) return;

            ImGui.Text($"Dataset: {dataset.Name}");
            ImGui.Text($"Dimensions: {dataset.Width} × {dataset.Height} × {dataset.Depth}");
    
            long volumeMemory = (long)dataset.Width * dataset.Height * dataset.Depth * 3 * sizeof(float) * 2;
            ImGui.Text($"Estimated Memory: {volumeMemory / (1024 * 1024)} MB");
    
            if (volumeMemory > 4L * 1024 * 1024 * 1024)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "⚠ Large dataset - chunked processing recommended");
            }
    
            ImGui.Separator();
    
            if (ImGui.CollapsingHeader("Density Calibration", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                ImGui.TextWrapped("Use the 'Density Calibration' tool in the Preprocessing category to calibrate material densities.");
                ImGui.TextWrapped("This simulation will use the calibrated densities stored in the dataset's materials.");

                // Let's check if any material (other than default) has a non-zero density.
                bool isCalibrated = dataset.Materials.Any(m => m.ID != 0 && m.Density > 0);
                ImGui.Text("Status:");
                ImGui.SameLine();
                ImGui.TextColored(isCalibrated ? new Vector4(0,1,0,1) : new Vector4(1,1,0,1),
                    isCalibrated ? "✓ Material densities appear to be set." : "⚠ Using default material densities.");
                ImGui.Unindent();
            }
    
            ImGui.Separator();
    
            _calibrationManager.DrawCalibrationControls(ref _youngsModulus, ref _poissonRatio);
            ImGui.Separator();

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

            ImGui.Text("Wave Propagation Axis:");
            ImGui.RadioButton("X", ref _selectedAxisIndex, 0); ImGui.SameLine();
            ImGui.RadioButton("Y", ref _selectedAxisIndex, 1); ImGui.SameLine();
            ImGui.RadioButton("Z", ref _selectedAxisIndex, 2);
    
            ImGui.Checkbox("Full-Face Transducers", ref _useFullFaceTransducers);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Use entire face of volume as transducer instead of point source");
            ImGui.Separator();

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

            if (ImGui.CollapsingHeader("Visualization"))
            {
                ImGui.Indent();
                ImGui.Checkbox("Enable Real-Time Visualization", ref _enableRealTimeVisualization);
                if (_enableRealTimeVisualization)
                {
                    ImGui.DragFloat("Update Interval (s)", ref _visualizationUpdateInterval, 0.01f, 0.01f, 1.0f);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("How often to update the 3D visualization during simulation");
                }
        
                ImGui.Checkbox("Save Time Series", ref _saveTimeSeries);
                if (_saveTimeSeries)
                {
                    ImGui.DragInt("Snapshot Interval", ref _snapshotInterval, 1, 1, 100);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Save a snapshot every N time steps");
                }
                ImGui.Separator();
        
                ImGui.Checkbox("Show Velocity Tomography", ref _showTomography);
                if (_showTomography && _lastResults != null)
                {
                    ImGui.Text("Tomography Slice:");
                    ImGui.RadioButton("X Slice", ref _tomographySliceAxis, 0); ImGui.SameLine();
                    ImGui.RadioButton("Y Slice", ref _tomographySliceAxis, 1); ImGui.SameLine();
                    ImGui.RadioButton("Z Slice", ref _tomographySliceAxis, 2);
            
                    int maxSlice = _tomographySliceAxis switch { 0 => dataset.Width - 1, 1 => dataset.Height - 1, 2 => dataset.Depth - 1, _ => 0 };
                    ImGui.SliderInt("Slice Index", ref _tomographySliceIndex, 0, maxSlice);
                }
                ImGui.Unindent();
            }

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
        
                if (!_useFullFaceTransducers)
                {
                    ImGui.Spacing();
                    ImGui.Text("Transmitter Position (normalized 0-1):");
                    ImGui.DragFloat3("TX", ref _txPosition, 0.01f, 0.0f, 1.0f);
                    ImGui.Text("Receiver Position (normalized 0-1):");
                    ImGui.DragFloat3("RX", ref _rxPosition, 0.01f, 0.0f, 1.0f);
                    float dx = (_rxPosition.X - _txPosition.X) * dataset.Width * dataset.PixelSize / 1000f;
                    float dy = (_rxPosition.Y - _txPosition.Y) * dataset.Height * dataset.PixelSize / 1000f;
                    float dz = (_rxPosition.Z - _txPosition.Z) * dataset.Depth * dataset.SliceThickness / 1000f;
                    float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    ImGui.Text($"TX-RX Distance: {distance:F2} mm");
                }
                ImGui.Unindent();
            }

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
    
            if (_isSimulating)
            {
                ImGui.ProgressBar(_simulator?.Progress ?? 0.0f, new Vector2(-1, 0), $"Simulating... {(_simulator?.CurrentStep ?? 0)}/{_timeSteps} steps");
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
            else
            {
                bool canSimulate = materials.Length > 0 && (!_useChunkedProcessing || volumeMemory < 8L * 1024 * 1024 * 1024);
                if (!canSimulate)
                {
                    ImGui.BeginDisabled();
                    if (volumeMemory >= 8L * 1024 * 1024 * 1024)
                        ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Enable chunked processing for large datasets");
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
                        _calibrationManager.AddSimulationResult(material.Name, material.ID, (float)material.Density, _confiningPressure, _youngsModulus, _poissonRatio, _lastResults.PWaveVelocity, _lastResults.SWaveVelocity);
                        Logger.Log("[Simulation] Added results to calibration database");
                    }
                }
        
                if (_showTomography && _tomographyTexture != null)
                {
                    ImGui.Separator();
                    if (ImGui.CollapsingHeader("Velocity Tomography"))
                    {
                        DrawTomographyView();
                    }
                }
            }
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

        private void DrawTomographyView()
        {
            if (_tomographyTexture == null || !_tomographyTexture.IsValid) return;
            var availableSize = ImGui.GetContentRegionAvail();
            if (availableSize.X < 100 || availableSize.Y < 100) return;
            float aspectRatio = _tomographySliceAxis switch { 0 => (float)_parameters.Height / _parameters.Depth, 1 => (float)_parameters.Width / _parameters.Depth, 2 => (float)_parameters.Width / _parameters.Height, _ => 1.0f };
            Vector2 imageSize;
            if (availableSize.X / availableSize.Y > aspectRatio) { imageSize = new Vector2(availableSize.Y * aspectRatio, availableSize.Y); }
            else { imageSize = new Vector2(availableSize.X, availableSize.X / aspectRatio); }
            ImGui.Image(_tomographyTexture.GetImGuiTextureId(), imageSize);
            ImGui.SameLine();
            DrawColorBar();
        }

        private void DrawColorBar()
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float width = 30; float height = 200;
            for (int i = 0; i < height; i++)
            {
                float value = 1.0f - (float)i / height;
                Vector4 color = GetVelocityColor(value);
                uint col = ImGui.GetColorU32(color);
                drawList.AddRectFilled(new Vector2(pos.X, pos.Y + i), new Vector2(pos.X + width, pos.Y + i + 1), col);
            }
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y - 5));
            ImGui.Text($"{_lastResults?.PWaveVelocity ?? 6000:F0} m/s");
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height - 5));
            ImGui.Text($"{_lastResults?.SWaveVelocity ?? 3000:F0} m/s");
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
    try
    {
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

        long estimatedMemory = (long)dataset.Width * dataset.Height * dataset.Depth * 3 * sizeof(float) * 2;
        if (_useChunkedProcessing || estimatedMemory > 2L * 1024 * 1024 * 1024)
        {
            _simulator = new ChunkedAcousticSimulator(_parameters);
            Logger.Log("[AcousticSimulation] Using chunked simulator for memory efficiency");
        }
        else
        {
            _simulator = new ChunkedAcousticSimulator(_parameters);
            Logger.Log("[AcousticSimulation] Using standard simulator");
        }

        _simulator.ProgressUpdated += OnSimulationProgress;
        if (_enableRealTimeVisualization) { _simulator.WaveFieldUpdated += OnWaveFieldUpdated; }
        
        var volumeLabels = await ExtractVolumeLabelsAsync(dataset);
        var densityVolume = await ExtractDensityVolumeAsync(dataset);
        
        // NEW: Extract material properties volume but pass them differently
        // We'll modify the simulator to use per-voxel properties internally if available
        var (youngsModulusVolume, poissonRatioVolume) = await ExtractMaterialPropertiesVolumeAsync(dataset);
        
        // Store the per-voxel properties in the simulator before running
        _simulator.SetPerVoxelMaterialProperties(youngsModulusVolume, poissonRatioVolume);
        
        // CORRECT: Use the original 3-parameter call
        _lastResults = await _simulator.RunAsync(volumeLabels, densityVolume, _cancellationTokenSource.Token);
        
        if (_lastResults != null)
        {
            var damageField = _simulator.GetDamageField();
            _exportManager.SetDamageField(damageField);
            _lastResults.DamageField = damageField;
            Logger.Log($"[AcousticSimulation] Simulation completed with damage field: Vp={_lastResults.PWaveVelocity:F2} m/s, Vs={_lastResults.SWaveVelocity:F2} m/s");
            
            _calibrationManager.AddSimulationResult(material.Name, material.ID, (float)material.Density, _confiningPressure, _youngsModulus, _poissonRatio, _lastResults.PWaveVelocity, _lastResults.SWaveVelocity);
            
            if (_showTomography) { await GenerateTomographyAsync(); }
        }
    }
    catch (Exception ex) { Logger.LogError($"[AcousticSimulation] Simulation failed: {ex.Message}"); }
    finally { _isSimulating = false; _simulator?.Dispose(); _simulator = null; }
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

        private async Task<float[,,]> ExtractDensityVolumeAsync(CtImageStackDataset dataset)
{
    return await Task.Run(() =>
    {
        var density = new float[dataset.Width, dataset.Height, dataset.Depth];
        
        // NEW: Use material properties from labels and material library
        if (_useChunkedProcessing)
        {
            int chunkDepth = Math.Max(1, _chunkSizeMB * 1024 * 1024 / (dataset.Width * dataset.Height * sizeof(float)));
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
                        
                        // Get material properties for this label
                        var material = dataset.Materials.FirstOrDefault(m => m.ID == label);
                        if (material != null)
                        {
                            // Use density from material library if linked, otherwise from material definition
                            if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                            {
                                var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                                if (physMat != null && physMat.Density_kg_m3.HasValue)
                                    density[x, y, z] = (float)(physMat.Density_kg_m3.Value / 1000.0); // Convert kg/m³ to g/cm³
                                else
                                    density[x, y, z] = (float)material.Density;
                            }
                            else
                            {
                                density[x, y, z] = (float)material.Density;
                            }
                        }
                        else
                        {
                            // Default density for unlabeled voxels
                            density[x, y, z] = 0f;
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
                    
                    // Get material properties for this label
                    var material = dataset.Materials.FirstOrDefault(m => m.ID == label);
                    if (material != null)
                    {
                        if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                        {
                            var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                            if (physMat != null && physMat.Density_kg_m3.HasValue)
                                density[x, y, z] = (float)(physMat.Density_kg_m3.Value / 1000.0);
                            else
                                density[x, y, z] = (float)material.Density;
                        }
                        else
                        {
                            density[x, y, z] = (float)material.Density;
                        }
                    }
                    else
                    {
                        density[x, y, z] = 0f;
                    }
                }
            });
        }
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
                    _tomographyTexture = TextureManager.CreateFromPixelData(tomography, (uint)(_tomographySliceAxis == 2 ? _parameters.Width : _parameters.Width), (uint)(_tomographySliceAxis == 0 ? _parameters.Depth : _parameters.Height));
                }
            });
        }

        private void OnWaveFieldUpdated(object sender, WaveFieldUpdateEventArgs e)
        {
            if ((DateTime.Now - _lastVisualizationUpdate).TotalSeconds >= _visualizationUpdateInterval)
            {
                _lastVisualizationUpdate = DateTime.Now;
                _currentWaveFieldMask = CreateWaveFieldMask(e.WaveField);
                CtImageStackTools.Update3DPreviewFromExternal(e.Dataset as CtImageStackDataset, _currentWaveFieldMask, new Vector4(1, 0.5f, 0, 0.5f));
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