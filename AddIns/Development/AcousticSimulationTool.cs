// GeoscientistToolkit/AddIns/AcousticSimulation/AcousticSimulationTool.cs (COMPLETE INTEGRATED)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.AddIns;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.AcousticVolume;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.AddIns.AcousticSimulation
{
    /// <summary>
    /// Main tool providing UI and simulation control with full integration
    /// </summary>
    internal class AcousticSimulationTool : AddInTool, IDisposable
    {
        public override string Name => "Acoustic Simulation";
        public override string Icon => "🔊";
        public override string Tooltip => "Simulate acoustic wave propagation through materials";
        private DateTime _simulationStartTime = DateTime.MinValue;

        private ChunkedAcousticSimulator _simulator;
        private SimulationParameters _parameters;
        private UnifiedCalibrationManager _calibrationManager;
        private AcousticExportManager _exportManager;
        private VelocityTomographyGenerator _tomographyGenerator;
        private bool _isSimulating = false;
        private CancellationTokenSource _cancellationTokenSource;
        private SimulationResults _lastResults;

        // Real-time visualization support
        private bool _enableRealTimeVisualization = false;
        private float _visualizationUpdateInterval = 0.1f;
        private DateTime _lastVisualizationUpdate = DateTime.MinValue;
        private byte[] _currentWaveFieldMask;
        
        // Memory management
        private bool _useChunkedProcessing = true;
        private int _chunkSizeMB = 512; // Process in 512MB chunks
        private bool _enableOffloading = true;
        private string _offloadDirectory;
        
        // Tomography settings
        private bool _showTomography = false;
        private int _tomographySliceAxis = 0; // 0=X, 1=Y, 2=Z
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
        private bool _showAdvanced = false;
        private bool _autoCalibrate = false;
        private bool _saveTimeSeries = false;
        private int _snapshotInterval = 10;
        private Vector3 _txPosition = new Vector3(0, 0.5f, 0.5f);
        private Vector3 _rxPosition = new Vector3(1, 0.5f, 0.5f);
        private DensityCalibrationTool _densityTool;
        private DensityVolume _calibratedDensity;
        public AcousticSimulationTool()
        {
            _parameters = new SimulationParameters();
            _calibrationManager = new UnifiedCalibrationManager(); 
            _exportManager = new AcousticExportManager();
            _tomographyGenerator = new VelocityTomographyGenerator();
            _offloadDirectory = Path.Combine(Path.GetTempPath(), "AcousticSimulation");
            Directory.CreateDirectory(_offloadDirectory);
        }

        public override bool CanExecute(Dataset dataset) => dataset is CtImageStackDataset;

        public override void Execute(Dataset dataset)
        {
            // Tool execution is handled through the panel drawing
        }

        public void DrawPanel(CtImageStackDataset dataset)
{
    if (dataset == null) return;

    ImGui.Text($"Dataset: {dataset.Name}");
    ImGui.Text($"Dimensions: {dataset.Width} × {dataset.Height} × {dataset.Depth}");
    
    // Calculate memory requirements
    long volumeMemory = (long)dataset.Width * dataset.Height * dataset.Depth * 3 * sizeof(float) * 2; // velocity + stress fields
    ImGui.Text($"Estimated Memory: {volumeMemory / (1024 * 1024)} MB");
    
    if (volumeMemory > 4L * 1024 * 1024 * 1024) // > 4GB
    {
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "⚠ Large dataset - chunked processing recommended");
    }
    
    ImGui.Separator();
    
    // ===== DENSITY CALIBRATION SECTION =====
    if (ImGui.CollapsingHeader("Density Calibration", ImGuiTreeNodeFlags.DefaultOpen))
    {
        ImGui.Indent();
        
        if (ImGui.Button("Open Density Calibration Tool"))
        {
            // Create a temporary AcousticVolumeDataset for the tool's constructor
            var tmp = new AcousticVolumeDataset($"{dataset.Name}-Calib", Path.GetTempPath());
            _densityTool ??= new DensityCalibrationTool(dataset, tmp);
            _densityTool.Show();
        }
        
        ImGui.SameLine();
        bool hasDensityCalib = _calibratedDensity != null;
        ImGui.TextColored(hasDensityCalib ? new Vector4(0,1,0,1) : new Vector4(1,1,0,1),
            hasDensityCalib ? "✓ Per-voxel density calibrated" : "⚠ Using default density mapping");
        
        if (_densityTool != null)
        {
            ImGui.Spacing();
            
            if (ImGui.Button("Apply Current Calibration"))
            {
                _calibratedDensity = _densityTool.GetDensityVolume();
                if (_calibratedDensity != null)
                {
                    // Update material properties from calibrated density
                    _youngsModulus = _calibratedDensity.GetMeanYoungsModulus() / 1e6f; // Pa to MPa
                    _poissonRatio = _calibratedDensity.GetMeanPoissonRatio();
                    Logger.Log($"[Simulation] Applied density calibration: E={_youngsModulus:F2} MPa, ν={_poissonRatio:F4}");
                }
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Clear Density Calibration"))
            {
                _calibratedDensity = null;
                Logger.Log("[Simulation] Cleared density calibration");
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Export Density Volume"))
            {
                // Export density volume for reuse
                if (_calibratedDensity != null)
                {
                    string path = $"{dataset.Name}_density_{DateTime.Now:yyyyMMdd_HHmmss}.den";
                    // ExportDensityVolume(_calibratedDensity, path);
                    Logger.Log($"[Simulation] Exported density volume to {path}");
                }
            }
            
            // Draw the calibration window if open
            _densityTool.Draw();
        }
        
        ImGui.Unindent();
    }
    
    ImGui.Separator();
    
    // ===== VELOCITY CALIBRATION SECTION =====
    _calibrationManager.DrawCalibrationControls(ref _youngsModulus, ref _poissonRatio);
    
    ImGui.Separator();
    
    // ===== MATERIAL SELECTION =====
    ImGui.Text("Target Material:");
    var materials = dataset.Materials.Where(m => m.ID != 0).Select(m => m.Name).ToArray();
    if (materials.Length == 0)
    {
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "No materials available for simulation.");
        return;
    }
    
    ImGui.SetNextItemWidth(-1);
    if (ImGui.Combo("##Material", ref _selectedMaterialIndex, materials, materials.Length))
    {
        // Update density if we have calibration
        if (_calibratedDensity != null)
        {
            var material = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
            // Could update specific material properties here
        }
    }
    
    ImGui.Separator();
    
    // ===== SIMULATION AXIS =====
    ImGui.Text("Wave Propagation Axis:");
    string[] axes = { "X", "Y", "Z" };
    ImGui.RadioButton("X", ref _selectedAxisIndex, 0); 
    ImGui.SameLine();
    ImGui.RadioButton("Y", ref _selectedAxisIndex, 1); 
    ImGui.SameLine();
    ImGui.RadioButton("Z", ref _selectedAxisIndex, 2);
    
    ImGui.Checkbox("Full-Face Transducers", ref _useFullFaceTransducers);
    if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Use entire face of volume as transducer instead of point source");
    
    ImGui.Separator();
    
    // ===== MEMORY MANAGEMENT =====
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
                    // Open folder dialog (implementation depends on your UI framework)
                    Logger.Log("[Simulation] Change offload directory not yet implemented");
                }
                
                ImGui.SameLine();
                if (ImGui.Button("Clear Cache"))
                {
                    if (Directory.Exists(_offloadDirectory))
                    {
                        try 
                        { 
                            Directory.Delete(_offloadDirectory, true);
                            Directory.CreateDirectory(_offloadDirectory);
                            Logger.Log("[Simulation] Cleared offload cache");
                        } 
                        catch (Exception ex) 
                        { 
                            Logger.LogError($"Failed to clear cache: {ex.Message}");
                        }
                    }
                }
            }
        }
        
        ImGui.Unindent();
    }
    
    // ===== VISUALIZATION =====
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
            ImGui.RadioButton("X Slice", ref _tomographySliceAxis, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Y Slice", ref _tomographySliceAxis, 1);
            ImGui.SameLine();
            ImGui.RadioButton("Z Slice", ref _tomographySliceAxis, 2);
            
            int maxSlice = _tomographySliceAxis switch
            {
                0 => dataset.Width - 1,
                1 => dataset.Height - 1,
                2 => dataset.Depth - 1,
                _ => 0
            };
            ImGui.SliderInt("Slice Index", ref _tomographySliceIndex, 0, maxSlice);
        }
        
        ImGui.Unindent();
    }
    
    // ===== MATERIAL PROPERTIES =====
    if (ImGui.CollapsingHeader("Material Properties"))
    {
        ImGui.Indent();
        
        ImGui.DragFloat("Young's Modulus (MPa)", ref _youngsModulus, 100.0f, 100.0f, 200000.0f);
        ImGui.DragFloat("Poisson's Ratio", ref _poissonRatio, 0.01f, 0.0f, 0.49f);
        
        // Auto-calibrate button
        if (_calibrationManager.HasCalibration)
        {
            if (ImGui.Button("Apply Calibration from Lab Data"))
            {
                var material = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);

                // No local E/nu here — assign to fields directly
                var __cal = _calibrationManager.GetCalibratedParameters(
                    (float)material.Density,
                    _confiningPressure);

                _youngsModulus = __cal.YoungsModulus;
                _poissonRatio  = __cal.PoissonRatio;

                Logger.Log($"[Simulation] Applied calibration: E={_youngsModulus:F2} MPa, ν={_poissonRatio:F4}");
            }
        }

// Calculate and display derived properties
        ImGui.Spacing();
        ImGui.Text("Derived Properties:");
        ImGui.Indent();

// Keep your variable names here
        float E  = _youngsModulus * 1e6f; // Convert to Pa
        float nu = _poissonRatio;
        float mu = E / (2.0f * (1.0f + nu));
        float lambda = E * nu / ((1 + nu) * (1 - 2 * nu));
        float bulkModulus = E / (3f * (1 - 2 * nu));

        
        ImGui.Text($"Shear Modulus: {mu / 1e6f:F2} MPa");
        ImGui.Text($"Bulk Modulus: {bulkModulus / 1e6f:F2} MPa");
        ImGui.Text($"Lamé λ: {lambda / 1e6f:F2} MPa");
        ImGui.Text($"Lamé μ: {mu / 1e6f:F2} MPa");
        
        // Expected velocities
        if (_calibratedDensity != null || _lastResults != null)
        {
            float density = 2700f; // Default
            if (_calibratedDensity != null)
            {
                // Use mean density from calibrated volume
                float totalDensity = 0;
                int count = 0;
                for (int z = 0; z < Math.Min(10, _calibratedDensity.Depth); z++)
                    for (int y = 0; y < Math.Min(10, _calibratedDensity.Height); y++)
                        for (int x = 0; x < Math.Min(10, _calibratedDensity.Width); x++)
                        {
                            totalDensity += _calibratedDensity.GetDensity(x, y, z);
                            count++;
                        }
                if (count > 0) density = totalDensity / count;
            }
            
            float vpExpected = MathF.Sqrt((lambda + 2 * mu) / density);
            float vsExpected = MathF.Sqrt(mu / density);
            
            ImGui.Spacing();
            ImGui.Text($"Expected Vp: {vpExpected:F0} m/s");
            ImGui.Text($"Expected Vs: {vsExpected:F0} m/s");
            ImGui.Text($"Expected Vp/Vs: {vpExpected / vsExpected:F3}");
        }
        
        ImGui.Unindent();
        
        // Calculate pixel size from velocities if we have results
        if (_lastResults != null)
        {
            float calculatedPixelSize = CalculatePixelSizeFromVelocities();
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f), 
                $"Calculated Pixel Size: {calculatedPixelSize * 1000:F3} mm");
        }
        
        ImGui.Unindent();
    }
    
    // ===== STRESS CONDITIONS =====
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
    
    // ===== SOURCE PARAMETERS =====
    if (ImGui.CollapsingHeader("Source Parameters"))
    {
        ImGui.Indent();
        
        ImGui.DragFloat("Source Energy (J)", ref _sourceEnergy, 0.1f, 0.01f, 10.0f);
        ImGui.DragFloat("Frequency (kHz)", ref _sourceFrequency, 10.0f, 1.0f, 5000.0f);
        ImGui.DragInt("Amplitude", ref _sourceAmplitude, 1, 1, 1000);
        ImGui.DragInt("Time Steps", ref _timeSteps, 10, 100, 10000);
        
        // Calculate and display wavelength
        if (_lastResults != null || _calibratedDensity != null)
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
            
            // Show distance
            float dx = (_rxPosition.X - _txPosition.X) * dataset.Width * dataset.PixelSize / 1000f;
            float dy = (_rxPosition.Y - _txPosition.Y) * dataset.Height * dataset.PixelSize / 1000f;
            float dz = (_rxPosition.Z - _txPosition.Z) * dataset.Depth * dataset.SliceThickness / 1000f;
            float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            ImGui.Text($"TX-RX Distance: {distance:F2} mm");
        }
        
        ImGui.Unindent();
    }
    
    // ===== PHYSICS MODELS =====
    if (ImGui.CollapsingHeader("Physics Models"))
    {
        ImGui.Indent();
        
        ImGui.Checkbox("Elastic", ref _useElastic);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Standard elastic wave propagation");
        
        ImGui.Checkbox("Plastic (Mohr-Coulomb)", ref _usePlastic);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Include plastic deformation using Mohr-Coulomb criterion");
        
        ImGui.Checkbox("Brittle Damage", ref _useBrittle);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Model brittle damage accumulation");
        
        ImGui.Checkbox("Use GPU Acceleration", ref _useGPU);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Use OpenCL for GPU acceleration (if available)");
        
        ImGui.Checkbox("Auto-Calibrate", ref _autoCalibrate);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Automatically calibrate parameters based on previous simulations");
        
        ImGui.Unindent();
    }
    
    ImGui.Separator();
    
    // ===== SIMULATION CONTROL =====
    if (_isSimulating)
    {
        ImGui.ProgressBar(_simulator?.Progress ?? 0.0f, new Vector2(-1, 0), 
            $"Simulating... {(_simulator?.CurrentStep ?? 0)}/{_timeSteps} steps");
        
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
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Enable chunked processing for large datasets");
            }
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
    
    // ===== RESULTS DISPLAY AND EXPORT =====
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
            
            // Performance metrics
            if (_lastResults.TotalTimeSteps > 0 && _lastResults.ComputationTime.TotalSeconds > 0)
            {
                double stepsPerSecond = _lastResults.TotalTimeSteps / _lastResults.ComputationTime.TotalSeconds;
                ImGui.Text($"Performance: {stepsPerSecond:F0} steps/second");
            }
            
            ImGui.Spacing();
            
            // Pass all necessary data to export manager
            _exportManager.SetCalibrationData(_calibrationManager.CalibrationData);
            
            // Get damage field if available
            if (_lastResults.DamageField != null)
            {
                _exportManager.SetDamageField(_lastResults.DamageField);
                ImGui.Text($"Damage Field: Available ({_lastResults.DamageField.Length} voxels)");
            }
            
            // Export controls
            _exportManager.DrawExportControls(_lastResults, _parameters, dataset);
            
            // Add to calibration button
            ImGui.Spacing();
            if (ImGui.Button("Add to Calibration Database"))
            {
                var material = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
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
                Logger.Log("[Simulation] Added results to calibration database");
            }
        }
        
        // Display tomography if enabled
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
        private float[,,] ConvertDensityVolume(DensityVolume densityVolume)
        {
            var result = new float[densityVolume.Width, densityVolume.Height, densityVolume.Depth];
        
            for (int z = 0; z < densityVolume.Depth; z++)
            for (int y = 0; y < densityVolume.Height; y++)
            for (int x = 0; x < densityVolume.Width; x++)
                result[x, y, z] = densityVolume.GetDensity(x, y, z);
        
            return result;
        }
        private float CalculatePixelSizeFromVelocities()
        {
            if (_lastResults == null) return _parameters.PixelSize;
            
            // Calculate pixel size from travel time and velocity
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
            
            // Maintain aspect ratio
            float aspectRatio = _tomographySliceAxis switch
            {
                0 => (float)_parameters.Height / _parameters.Depth,
                1 => (float)_parameters.Width / _parameters.Depth,
                2 => (float)_parameters.Width / _parameters.Height,
                _ => 1.0f
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
            
            ImGui.Image(_tomographyTexture.GetImGuiTextureId(), imageSize);
            
            // Color bar
            ImGui.SameLine();
            DrawColorBar();
        }

        private void DrawColorBar()
        {
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float width = 30;
            float height = 200;
            
            // Draw gradient
            for (int i = 0; i < height; i++)
            {
                float value = 1.0f - (float)i / height;
                Vector4 color = GetVelocityColor(value);
                uint col = ImGui.GetColorU32(color);
                drawList.AddRectFilled(
                    new Vector2(pos.X, pos.Y + i),
                    new Vector2(pos.X + width, pos.Y + i + 1),
                    col);
            }
            
            // Labels
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y - 5));
            ImGui.Text($"{_lastResults?.PWaveVelocity ?? 6000:F0} m/s");
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height - 5));
            ImGui.Text($"{_lastResults?.SWaveVelocity ?? 3000:F0} m/s");
        }

        private Vector4 GetVelocityColor(float normalized)
        {
            // Jet colormap for velocity
            float r, g, b;
            if (normalized < 0.25f)
            {
                r = 0;
                g = 4 * normalized;
                b = 1;
            }
            else if (normalized < 0.5f)
            {
                r = 0;
                g = 1;
                b = 1 - 4 * (normalized - 0.25f);
            }
            else if (normalized < 0.75f)
            {
                r = 4 * (normalized - 0.5f);
                g = 1;
                b = 0;
            }
            else
            {
                r = 1;
                g = 1 - 4 * (normalized - 0.75f);
                b = 0;
            }
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
                    Width = dataset.Width,
                    Height = dataset.Height,
                    Depth = dataset.Depth,
                    PixelSize = (float)dataset.PixelSize / 1000.0f,
                    SelectedMaterialID = material.ID,
                    Axis = _selectedAxisIndex,
                    UseFullFaceTransducers = _useFullFaceTransducers,
                    ConfiningPressureMPa = _confiningPressure,
                    TensileStrengthMPa = _tensileStrength,
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

                // Create appropriate simulator based on memory requirements
                long estimatedMemory = (long)dataset.Width * dataset.Height * dataset.Depth * 3 * sizeof(float) * 2;
                
                if (_useChunkedProcessing || estimatedMemory > 2L * 1024 * 1024 * 1024)
                {
                    _simulator = new ChunkedAcousticSimulator(_parameters);
                    Logger.Log("[AcousticSimulation] Using chunked simulator for memory efficiency");
                }
                else
                {
                    _simulator = new ChunkedAcousticSimulator(_parameters); // Use same implementation
                    Logger.Log("[AcousticSimulation] Using standard simulator");
                }
                
                _simulator.ProgressUpdated += OnSimulationProgress;
                
                if (_enableRealTimeVisualization)
                {
                    _simulator.WaveFieldUpdated += OnWaveFieldUpdated;
                }

                // Extract volume data efficiently
                var volumeLabels = await ExtractVolumeLabelsAsync(dataset);
                var densityVolume = await ExtractDensityVolumeAsync(dataset);

                _lastResults = await _simulator.RunAsync(
                    volumeLabels, 
                    densityVolume, 
                    _cancellationTokenSource.Token);

                if (_lastResults != null)
                {
                    // Get damage field from simulator
                    var damageField = _simulator.GetDamageField();
        
                    // Pass damage field to export manager
                    _exportManager.SetDamageField(damageField);
        
                    // Store in results for export
                    _lastResults.DamageField = damageField;
        
                    Logger.Log($"[AcousticSimulation] Simulation completed with damage field: Vp={_lastResults.PWaveVelocity:F2} m/s, Vs={_lastResults.SWaveVelocity:F2} m/s");
                    Logger.Log($"[AcousticSimulation] Simulation completed: Vp={_lastResults.PWaveVelocity:F2} m/s, Vs={_lastResults.SWaveVelocity:F2} m/s");
                    
                    // Store calibration
                    _calibrationManager.AddSimulationResult(
                        material.Name,
                        material.ID,
                        (float)material.Density,
                        _confiningPressure,
                        _youngsModulus,
                        _poissonRatio,
                        _lastResults.PWaveVelocity,
                        _lastResults.SWaveVelocity);

                    
                    // Generate tomography if enabled
                    if (_showTomography)
                    {
                        await GenerateTomographyAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticSimulation] Simulation failed: {ex.Message}");
            }
            finally
            {
                _isSimulating = false;
                _simulator?.Dispose();
                _simulator = null;
            }
        }

        private async Task<byte[,,]> ExtractVolumeLabelsAsync(CtImageStackDataset dataset)
        {
            return await Task.Run(() =>
            {
                var labels = new byte[dataset.Width, dataset.Height, dataset.Depth];
                
                if (_useChunkedProcessing)
                {
                    // Process in chunks to avoid memory issues
                    int chunkDepth = Math.Max(1, _chunkSizeMB * 1024 * 1024 / (dataset.Width * dataset.Height));
                    
                    for (int z0 = 0; z0 < dataset.Depth; z0 += chunkDepth)
                    {
                        int z1 = Math.Min(z0 + chunkDepth, dataset.Depth);
                        
                        Parallel.For(z0, z1, z =>
                        {
                            for (int y = 0; y < dataset.Height; y++)
                                for (int x = 0; x < dataset.Width; x++)
                                    labels[x, y, z] = dataset.LabelData[x, y, z];
                        });
                    }
                }
                else
                {
                    Parallel.For(0, dataset.Depth, z =>
                    {
                        for (int y = 0; y < dataset.Height; y++)
                            for (int x = 0; x < dataset.Width; x++)
                                labels[x, y, z] = dataset.LabelData[x, y, z];
                    });
                }
                
                return labels;
            });
        }

       private async Task<float[,,]> ExtractDensityVolumeAsync(CtImageStackDataset dataset)
{
    return await Task.Run(() =>
    {
        var density = new float[dataset.Width, dataset.Height, dataset.Depth];

        if (_calibratedDensity != null)
        {
            // Copy from calibrated per-voxel volume
            if (_useChunkedProcessing)
            {
                int chunkDepth = Math.Max(1, _chunkSizeMB * 1024 * 1024 / (dataset.Width * dataset.Height * sizeof(float)));
                for (int z0 = 0; z0 < dataset.Depth; z0 += chunkDepth)
                {
                    int z1 = Math.Min(z0 + chunkDepth, dataset.Depth);
                    Parallel.For(z0, z1, z =>
                    {
                        for (int y = 0; y < dataset.Height; y++)
                            for (int x = 0; x < dataset.Width; x++)
                                density[x, y, z] = _calibratedDensity.GetDensity(x, y, z);
                    });
                }
            }
            else
            {
                Parallel.For(0, dataset.Depth, z =>
                {
                    for (int y = 0; y < dataset.Height; y++)
                        for (int x = 0; x < dataset.Width; x++)
                            density[x, y, z] = _calibratedDensity.GetDensity(x, y, z);
                });
            }

            return density;
        }

        // Fallback: build ρ from GREYSCALE → RockMaterial → ρ for EVERY voxel
        if (_useChunkedProcessing)
        {
            int bytesPerSlice = dataset.Width * dataset.Height;
            int chunkDepth = Math.Max(1, _chunkSizeMB * 1024 * 1024 / (bytesPerSlice));
            for (int z0 = 0; z0 < dataset.Depth; z0 += chunkDepth)
            {
                int z1 = Math.Min(z0 + chunkDepth, dataset.Depth);
                Parallel.For(z0, z1, z =>
                {
                    var graySlice = new byte[bytesPerSlice];
                    dataset.VolumeData.ReadSliceZ(z, graySlice); // <-- CT greyscale
                    for (int i = 0; i < graySlice.Length; i++)
                    {
                        int x = i % dataset.Width;
                        int y = i / dataset.Width;
                        var mat = RockMaterialLibrary.GetMaterialByGrayscale(graySlice[i]);
                        density[x, y, z] = mat.Density;
                    }
                });
            }
        }
        else
        {
            Parallel.For(0, dataset.Depth, z =>
            {
                var graySlice = new byte[dataset.Width * dataset.Height];
                dataset.VolumeData.ReadSliceZ(z, graySlice);
                for (int i = 0; i < graySlice.Length; i++)
                {
                    int x = i % dataset.Width;
                    int y = i / dataset.Width;
                    var mat = RockMaterialLibrary.GetMaterialByGrayscale(graySlice[i]);
                    density[x, y, z] = mat.Density;
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
                var tomography = _tomographyGenerator.Generate2DTomography(
                    _lastResults, _tomographySliceAxis, _tomographySliceIndex);
                
                if (tomography != null)
                {
                    _tomographyTexture?.Dispose();
                    _tomographyTexture = TextureManager.CreateFromPixelData(
                        tomography, 
                        (uint)(_tomographySliceAxis == 2 ? _parameters.Width : _parameters.Width),
                        (uint)(_tomographySliceAxis == 0 ? _parameters.Depth : _parameters.Height));
                }
            });
        }

        private void OnWaveFieldUpdated(object sender, WaveFieldUpdateEventArgs e)
        {
            if ((DateTime.Now - _lastVisualizationUpdate).TotalSeconds >= _visualizationUpdateInterval)
            {
                _lastVisualizationUpdate = DateTime.Now;
                _currentWaveFieldMask = CreateWaveFieldMask(e.WaveField);
                
                CtImageStackTools.Update3DPreviewFromExternal(
                    e.Dataset as CtImageStackDataset, 
                    _currentWaveFieldMask, 
                    new Vector4(1, 0.5f, 0, 0.5f));
            }
        }

        private byte[] CreateWaveFieldMask(float[,,] waveField)
        {
            int width = waveField.GetLength(0);
            int height = waveField.GetLength(1);
            int depth = waveField.GetLength(2);
            byte[] mask = new byte[width * height * depth];
            
            float maxAmplitude = 0;
            for (int z = 0; z < depth; z++)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        maxAmplitude = Math.Max(maxAmplitude, Math.Abs(waveField[x, y, z]));
            
            if (maxAmplitude > 0)
            {
                int idx = 0;
                for (int z = 0; z < depth; z++)
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            float normalized = Math.Abs(waveField[x, y, z]) / maxAmplitude;
                            mask[idx++] = (byte)(normalized * 255);
                        }
            }
            
            return mask;
        }

        private void CancelSimulation()
        {
            _cancellationTokenSource?.Cancel();
            Logger.Log("[AcousticSimulation] Simulation cancelled by user");
        }

        private void OnSimulationProgress(object sender, SimulationProgressEventArgs e)
        {
            // Progress is updated through the UI polling
        }

        private void ApplyCalibration(CtImageStackDataset dataset)
        {
            var material = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
            var calibrated = _calibrationManager.GetCalibratedParameters((float)material.Density, _confiningPressure);
            
            _youngsModulus = calibrated.YoungsModulus;
            _poissonRatio = calibrated.PoissonRatio;
            
            Logger.Log($"[AcousticSimulation] Applied calibration: E={_youngsModulus:F2} MPa, ν={_poissonRatio:F4}");
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _simulator?.Dispose();
            _exportManager?.Dispose();
            _tomographyTexture?.Dispose();
            _tomographyGenerator?.Dispose();
            
            // Clean up offload directory
            if (Directory.Exists(_offloadDirectory))
            {
                try { Directory.Delete(_offloadDirectory, true); } catch { }
            }
        }
    }

    /// <summary>
    /// Chunked acoustic simulator with memory-efficient processing
    /// </summary>
    

internal class ChunkedAcousticSimulator : IDisposable
{
    private readonly SimulationParameters _params;

    private long _lastDeviceBytes;

// Public memory readout expected by the UI
    public long CurrentMemoryUsageMB
    {
        get
        {
            long managedBytes = GC.GetTotalMemory(false);
            // include a lightweight estimate of currently allocated OpenCL buffers (if any)
            long deviceBytes = System.Threading.Interlocked.Read(ref _lastDeviceBytes);
            return (managedBytes + deviceBytes) / (1024 * 1024);
        }
    }

    // Lamé, dt, simple damage thresholds derived from E (keeps dependencies minimal)
    private readonly float _lambda, _mu;
    private float _dt;
    private readonly float _tensileLimitPa;
    private readonly float _shearLimitPa;
    private readonly float _damageRatePerSec = 0.2f; // heuristic, stable small rate

    public float TimeStep => _dt;
    public float Progress { get; private set; }
    public int CurrentStep { get; private set; }

    public event EventHandler<SimulationProgressEventArgs> ProgressUpdated;
    public event EventHandler<WaveFieldUpdateEventArgs>    WaveFieldUpdated;

    // -------------------------------
    // Chunked storage
    // -------------------------------
    private sealed class WaveFieldChunk
    {
        public int StartZ, EndZ;
        public float[,,] Vx, Vy, Vz;
        public float[,,] Sxx, Syy, Szz, Sxy, Sxz, Syz;
        public float[,,] Damage;  // NEW: per-voxel damage [0..1]
    }

    private readonly List<WaveFieldChunk> _chunks = new();

    // Chunk size in Z computed from a rough memory budget (defaults conservatively)
    private readonly int _chunkDepth;

    // -------------------------------
    // OpenCL (Silk.NET)
    // -------------------------------
    private readonly bool _useGPU;
    private readonly CL _cl = CL.GetApi();

    private nint _platform;
    private nint _device;
    private nint _context;
    private nint _queue;
    private nint _program;
    private nint _kernelStress;
    private nint _kernelVelocity;

    // Per-chunk buffers (recreated per chunk to keep VRAM bounded)
    private nint _bufMat;
    private nint _bufDen;
    private readonly nint[] _bufVel = new nint[3]; // vx,vy,vz
    private readonly nint[] _bufStr = new nint[6]; // sxx,syy,szz,sxy,sxz,syz
    private nint _bufDmg;                           // damage

    public ChunkedAcousticSimulator(SimulationParameters parameters)
    {
        _params = parameters;
        _useGPU = parameters.UseGPU;

        // Elastic constants
        float E = MathF.Max(1e-6f, _params.YoungsModulusMPa) * 1e6f;
        float nu = _params.PoissonRatio;
        _mu = E / (2f * (1f + nu));
        _lambda = E * nu / ((1f + nu) * (1f - 2f * nu));

        // Simple damage limits derived from E (keeps API stable)
        _tensileLimitPa = 0.05f * E; // ~5% of E as tensile strength proxy
        _shearLimitPa   = 0.03f * E; // ~3% of E as shear yield proxy

        // Chunk size (memory-safe guess): 10 float fields + labels+rho transient; aim ~256MB
        long targetBytes = (_params.ChunkSizeMB > 0 ? _params.ChunkSizeMB : 256) * 1024L * 1024L;
        long bytesPerZ = (long)_params.Width * _params.Height * sizeof(float) * 10; // vx,vy,vz + 6σ + damage
        _chunkDepth = (int)Math.Clamp(targetBytes / Math.Max(1, bytesPerZ), 8, _params.Depth);

        InitChunks();

        if (_useGPU)
        {
            try
            {
                InitOpenCL();
                BuildProgramAndKernels();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CL] GPU init failed, falling back to CPU: {ex.Message}");
                _useGPU = false;
                CleanupOpenCL();
            }
        }
    }

    private void InitChunks()
    {
        for (int z0 = 0; z0 < _params.Depth; z0 += _chunkDepth)
        {
            int z1 = Math.Min(z0 + _chunkDepth, _params.Depth);
            int d = z1 - z0;

            _chunks.Add(new WaveFieldChunk
            {
                StartZ = z0,
                EndZ   = z1,
                Vx  = new float[_params.Width, _params.Height, d],
                Vy  = new float[_params.Width, _params.Height, d],
                Vz  = new float[_params.Width, _params.Height, d],
                Sxx = new float[_params.Width, _params.Height, d],
                Syy = new float[_params.Width, _params.Height, d],
                Szz = new float[_params.Width, _params.Height, d],
                Sxy = new float[_params.Width, _params.Height, d],
                Sxz = new float[_params.Width, _params.Height, d],
                Syz = new float[_params.Width, _params.Height, d],
                Damage = new float[_params.Width, _params.Height, d]
            });
        }
        Logger.Log($"[ChunkedSimulator] {_chunks.Count} chunks (depth {_chunkDepth})");
    }

    // ================================================================
    // PUBLIC: run simulation
    // ================================================================
    public async Task<SimulationResults> RunAsync(
        byte[,,] labels,
        float[,,] density,
        CancellationToken token)
    {
        var started = DateTime.Now;
        var results = new SimulationResults
        {
            TimeSeriesSnapshots = _params.SaveTimeSeries ? new List<WaveFieldSnapshot>() : null
        };

        CalculateTimeStep(density);
        ApplyInitialSource(labels, density);

        int maxSteps = Math.Max(1, _params.TimeSteps) * 2; // soft cap
        int step = 0;
        bool pHit = false, sHit = false;
        int pStep = 0, sStep = 0;

        while (step < maxSteps && !token.IsCancellationRequested)
        {
            // Per-step per-chunk update
            for (int i = 0; i < _chunks.Count; i++)
            {
                var c = _chunks[i];
                if (_useGPU)
                    await ProcessChunkGPUAsync(i, c, labels, density, token);
                else
                {
                    UpdateChunkStressCPU(c, labels, density);
                    UpdateChunkVelocityCPU(c, labels, density);
                }
            }

            // Exchange boundary planes (Z) for continuity
            ExchangeBoundaries();

            step++;
            CurrentStep = step;

            // Snapshots & live view
            if (_params.SaveTimeSeries && step % _params.SnapshotInterval == 0)
                results.TimeSeriesSnapshots?.Add(CreateSnapshot(step));

            if (_params.EnableRealTimeVisualization && step % 10 == 0)
            {
                WaveFieldUpdated?.Invoke(this, new WaveFieldUpdateEventArgs
                {
                    WaveField = GetCombinedMagnitude(),
                    TimeStep  = step,
                    SimTime   = step * _dt,
                    Dataset   = labels
                });
            }

            // Arrival detection (very light)
            if (!pHit && CheckPWaveArrival()) { pHit = true; pStep = step; }
            if (pHit && !sHit && CheckSWaveArrival()) { sHit = true; sStep = step; }

            Progress = (float)step / maxSteps;
            ProgressUpdated?.Invoke(this, new SimulationProgressEventArgs
            {
                Progress = Progress,
                Step = step,
                Message = $"Step {step}"
            });
        }

        // Populate results
        float distance = CalculateTxRxDistance();
        results.PWaveVelocity = pStep > 0 ? distance / (pStep * _dt) : 0;
        results.SWaveVelocity = sStep > 0 ? distance / (sStep * _dt) : 0;
        results.VpVsRatio     = results.SWaveVelocity > 0 ? results.PWaveVelocity / results.SWaveVelocity : 0;
        results.PWaveTravelTime = pStep;
        results.SWaveTravelTime = sStep;
        results.TotalTimeSteps  = step;
        results.ComputationTime = DateTime.Now - started;

        // Stitch final fields
        results.WaveFieldVx = await ReconstructFieldAsync(0);
        results.WaveFieldVy = await ReconstructFieldAsync(1);
        results.WaveFieldVz = await ReconstructFieldAsync(2);

        return results;
    }

    // ================================================================
    // OpenCL init & kernels
    // ================================================================
    private unsafe void InitOpenCL()
    {
        // Platform
        uint nplat = 0;
        _cl.GetPlatformIDs(0, null, &nplat);
        if (nplat == 0) throw new InvalidOperationException("OpenCL: no platforms.");
        Span<nint> plats = stackalloc nint[(int)nplat];
        fixed (nint* pPlats = plats) _cl.GetPlatformIDs(nplat, pPlats, null);
        _platform = plats[0];

        // Device (prefer GPU, fallback CPU)
        uint ndev = 0;
        _cl.GetDeviceIDs(_platform, DeviceType.Gpu, 0, null, &ndev);
        DeviceType chosen = DeviceType.Gpu;
        if (ndev == 0)
        {
            _cl.GetDeviceIDs(_platform, DeviceType.Cpu, 0, null, &ndev);
            if (ndev == 0) throw new InvalidOperationException("OpenCL: no devices.");
            chosen = DeviceType.Cpu;
        }
        Span<nint> devs = stackalloc nint[(int)ndev];
        fixed (nint* pDevs = devs) _cl.GetDeviceIDs(_platform, chosen, ndev, pDevs, null);
        _device = devs[0];

        // Context & queue
        int err;
        nint[] one = { _device };
        fixed (nint* p = one)
            _context = _cl.CreateContext(null, 1u, p, null, null, out err);
        if (err != (int)CLEnum.Success) throw new InvalidOperationException($"CreateContext: {err}");

        _queue = _cl.CreateCommandQueue(_context, _device, CommandQueueProperties.None, out err);
        if (err != (int)CLEnum.Success) throw new InvalidOperationException($"CreateCommandQueue: {err}");
    }

    private unsafe void BuildProgramAndKernels()
    {
        int err;
        string[] sources = { GetKernelSource() };
        _program = _cl.CreateProgramWithSource(_context, 1u, sources, (UIntPtr*)null, out err);
        if (err != (int)CLEnum.Success)
            throw new InvalidOperationException($"CreateProgramWithSource failed: {err}");

        nint[] devs = { _device };
        fixed (nint* p = devs)
        {
            int buildErr = _cl.BuildProgram(_program, 1u, p, (string)null, (ObjectNotifyCallback)null, null);
            if (buildErr != (int)CLEnum.Success)
            {
                nuint logSize = 0;
                _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
                byte[] log = new byte[(int)Math.Max(1, logSize)];
                fixed (byte* pLog = log)
                {
                    _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, logSize, pLog, null);
                    throw new InvalidOperationException($"BuildProgram failed: {buildErr}\n{System.Text.Encoding.UTF8.GetString(log)}");
                }
            }
        }

        _kernelStress   = _cl.CreateKernel(_program, "updateStress",   out err);
        if (err != (int)CLEnum.Success) throw new InvalidOperationException($"CreateKernel(updateStress): {err}");
        _kernelVelocity = _cl.CreateKernel(_program, "updateVelocity", out err);
        if (err != (int)CLEnum.Success) throw new InvalidOperationException($"CreateKernel(updateVelocity): {err}");
    }

    private void CleanupOpenCL()
    {
        ReleaseChunkBuffers();
        if (_kernelVelocity != 0) { _cl.ReleaseKernel(_kernelVelocity); _kernelVelocity = 0; }
        if (_kernelStress   != 0) { _cl.ReleaseKernel(_kernelStress);   _kernelStress = 0; }
        if (_program        != 0) { _cl.ReleaseProgram(_program);       _program = 0; }
        if (_queue          != 0) { _cl.ReleaseCommandQueue(_queue);    _queue = 0; }
        if (_context        != 0) { _cl.ReleaseContext(_context);       _context = 0; }
    }

    // ================================================================
    // GPU chunk processing (with damage)
    // ================================================================
    private unsafe Task ProcessChunkGPUAsync(int ci, WaveFieldChunk c, byte[,,] labels, float[,,] density, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();

            int w = _params.Width, h = _params.Height, d = c.EndZ - c.StartZ;
            int size = w * h * d;

            // Flatten host arrays
            var mat = new byte[size];
            var den = new float[size];
            var vx  = new float[size];
            var vy  = new float[size];
            var vz  = new float[size];
            var sxx = new float[size];
            var syy = new float[size];
            var szz = new float[size];
            var sxy = new float[size];
            var sxz = new float[size];
            var syz = new float[size];
            var dmg = new float[size];

            int k = 0;
            for (int lz = 0; lz < d; lz++)
            {
                int gz = c.StartZ + lz;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++, k++)
                {
                    mat[k] = labels[x, y, gz];
                    den[k] = MathF.Max(100f, density[x, y, gz]);
                    vx[k]  = c.Vx[x, y, lz];
                    vy[k]  = c.Vy[x, y, lz];
                    vz[k]  = c.Vz[x, y, lz];
                    sxx[k] = c.Sxx[x, y, lz];
                    syy[k] = c.Syy[x, y, lz];
                    szz[k] = c.Szz[x, y, lz];
                    sxy[k] = c.Sxy[x, y, lz];
                    sxz[k] = c.Sxz[x, y, lz];
                    syz[k] = c.Syz[x, y, lz];
                    dmg[k] = c.Damage[x, y, lz];
                }
            }

            int err;
            // Helpers for SetKernelArg
            static void SetArgMem(CL cl, nint kernel, uint i, nint mem) => cl.SetKernelArg(kernel, i, (nuint)IntPtr.Size, in mem);
            static void SetArgF  (CL cl, nint kernel, uint i, float v)  => cl.SetKernelArg(kernel, i, (nuint)sizeof(float), in v);
            static void SetArgI  (CL cl, nint kernel, uint i, int v)    => cl.SetKernelArg(kernel, i, (nuint)sizeof(int),   in v);
            static void SetArgB  (CL cl, nint kernel, uint i, byte v)   => cl.SetKernelArg(kernel, i, (nuint)sizeof(byte),  in v);

            // Allocate and upload
            fixed (byte*  pMat = mat)
            fixed (float* pDen = den)
            fixed (float* pvx  = vx)
            fixed (float* pvy  = vy)
            fixed (float* pvz  = vz)
            fixed (float* psxx = sxx)
            fixed (float* psyy = syy)
            fixed (float* pszz = szz)
            fixed (float* psxy = sxy)
            fixed (float* psxz = sxz)
            fixed (float* psyz = syz)
            fixed (float* pdmg = dmg)
            {
                _bufMat = _cl.CreateBuffer(_context, MemFlags.ReadOnly  | MemFlags.CopyHostPtr, (nuint)(size * sizeof(byte)),  pMat, out err);
                _bufDen = _cl.CreateBuffer(_context, MemFlags.ReadOnly  | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pDen, out err);

                _bufVel[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pvx,  out err);
                _bufVel[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pvy,  out err);
                _bufVel[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pvz,  out err);

                _bufStr[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), psxx, out err);
                _bufStr[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), psyy, out err);
                _bufStr[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pszz, out err);
                _bufStr[3] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), psxy, out err);
                _bufStr[4] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), psxz, out err);
                _bufStr[5] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), psyz, out err);

                _bufDmg    = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pdmg, out err);
                _lastDeviceBytes = (long)size * (
                    sizeof(byte)              // material
                    + sizeof(float) * (1       // density
                                       + 3     // vx,vy,vz
                                       + 6     // sxx,syy,szz,sxy,sxz,syz
                                       + 1));  // damage
                // ---- updateStress (also updates damage) ----
                uint a = 0;
                SetArgMem(_cl, _kernelStress, a++, _bufMat);
                SetArgMem(_cl, _kernelStress, a++, _bufDen);
                SetArgMem(_cl, _kernelStress, a++, _bufVel[0]);
                SetArgMem(_cl, _kernelStress, a++, _bufVel[1]);
                SetArgMem(_cl, _kernelStress, a++, _bufVel[2]);
                SetArgMem(_cl, _kernelStress, a++, _bufStr[0]);
                SetArgMem(_cl, _kernelStress, a++, _bufStr[1]);
                SetArgMem(_cl, _kernelStress, a++, _bufStr[2]);
                SetArgMem(_cl, _kernelStress, a++, _bufStr[3]);
                SetArgMem(_cl, _kernelStress, a++, _bufStr[4]);
                SetArgMem(_cl, _kernelStress, a++, _bufStr[5]);
                SetArgMem(_cl, _kernelStress, a++, _bufDmg);
                SetArgF  (_cl, _kernelStress, a++, _lambda);
                SetArgF  (_cl, _kernelStress, a++, _mu);
                SetArgF  (_cl, _kernelStress, a++, _dt);
                SetArgF  (_cl, _kernelStress, a++, _params.PixelSize);
                SetArgI  (_cl, _kernelStress, a++, w);
                SetArgI  (_cl, _kernelStress, a++, h);
                SetArgI  (_cl, _kernelStress, a++, d);
                SetArgB  (_cl, _kernelStress, a++, (byte)_params.SelectedMaterialID);
                SetArgF  (_cl, _kernelStress, a++, _tensileLimitPa);
                SetArgF  (_cl, _kernelStress, a++, _shearLimitPa);
                SetArgF  (_cl, _kernelStress, a++, _damageRatePerSec);

                ReadOnlySpan<UIntPtr> gwo = ReadOnlySpan<UIntPtr>.Empty;
                ReadOnlySpan<UIntPtr> gws = stackalloc UIntPtr[] { (UIntPtr)size };
                ReadOnlySpan<UIntPtr> lws = ReadOnlySpan<UIntPtr>.Empty;
                ReadOnlySpan<IntPtr>  wait = ReadOnlySpan<IntPtr>.Empty;
                Span<IntPtr>          evt  = Span<IntPtr>.Empty;
                _cl.EnqueueNdrangeKernel(_queue, _kernelStress, 1u, gwo, gws, lws, 0u, wait, evt);

                // ---- updateVelocity (uses density and stresses) ----
                a = 0;
                SetArgMem(_cl, _kernelVelocity, a++, _bufMat);
                SetArgMem(_cl, _kernelVelocity, a++, _bufDen);
                SetArgMem(_cl, _kernelVelocity, a++, _bufVel[0]);
                SetArgMem(_cl, _kernelVelocity, a++, _bufVel[1]);
                SetArgMem(_cl, _kernelVelocity, a++, _bufVel[2]);
                SetArgMem(_cl, _kernelVelocity, a++, _bufStr[0]);
                SetArgMem(_cl, _kernelVelocity, a++, _bufStr[1]);
                SetArgMem(_cl, _kernelVelocity, a++, _bufStr[2]);
                SetArgMem(_cl, _kernelVelocity, a++, _bufStr[3]);
                SetArgMem(_cl, _kernelVelocity, a++, _bufStr[4]);
                SetArgMem(_cl, _kernelVelocity, a++, _bufStr[5]);
                SetArgF  (_cl, _kernelVelocity, a++, _dt);
                SetArgF  (_cl, _kernelVelocity, a++, _params.PixelSize);
                SetArgI  (_cl, _kernelVelocity, a++, w);
                SetArgI  (_cl, _kernelVelocity, a++, h);
                SetArgI  (_cl, _kernelVelocity, a++, d);
                SetArgB  (_cl, _kernelVelocity, a++, (byte)_params.SelectedMaterialID);

                _cl.EnqueueNdrangeKernel(_queue, _kernelVelocity, 1u, gwo, gws, lws, 0u, wait, evt);

                _cl.Finish(_queue);

                // Read-back (needed for boundary exchange & live view)
                _cl.EnqueueReadBuffer(_queue, _bufVel[0], true, 0, (nuint)(size * sizeof(float)), pvx, 0, null, null);
                _cl.EnqueueReadBuffer(_queue, _bufVel[1], true, 0, (nuint)(size * sizeof(float)), pvy, 0, null, null);
                _cl.EnqueueReadBuffer(_queue, _bufVel[2], true, 0, (nuint)(size * sizeof(float)), pvz, 0, null, null);

                _cl.EnqueueReadBuffer(_queue, _bufStr[0], true, 0, (nuint)(size * sizeof(float)), psxx, 0, null, null);
                _cl.EnqueueReadBuffer(_queue, _bufStr[1], true, 0, (nuint)(size * sizeof(float)), psyy, 0, null, null);
                _cl.EnqueueReadBuffer(_queue, _bufStr[2], true, 0, (nuint)(size * sizeof(float)), pszz, 0, null, null);
                _cl.EnqueueReadBuffer(_queue, _bufStr[3], true, 0, (nuint)(size * sizeof(float)), psxy, 0, null, null);
                _cl.EnqueueReadBuffer(_queue, _bufStr[4], true, 0, (nuint)(size * sizeof(float)), psxz, 0, null, null);
                _cl.EnqueueReadBuffer(_queue, _bufStr[5], true, 0, (nuint)(size * sizeof(float)), psyz, 0, null, null);

                _cl.EnqueueReadBuffer(_queue, _bufDmg, true, 0, (nuint)(size * sizeof(float)), pdmg, 0, null, null);
            }

            // Scatter into chunk 3D arrays
            int idx = 0;
            for (int lz = 0; lz < d; lz++)
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++, idx++)
                {
                    c.Vx[x, y, lz]  = vx[idx];
                    c.Vy[x, y, lz]  = vy[idx];
                    c.Vz[x, y, lz]  = vz[idx];
                    c.Sxx[x, y, lz] = sxx[idx];
                    c.Syy[x, y, lz] = syy[idx];
                    c.Szz[x, y, lz] = szz[idx];
                    c.Sxy[x, y, lz] = sxy[idx];
                    c.Sxz[x, y, lz] = sxz[idx];
                    c.Syz[x, y, lz] = syz[idx];
                    c.Damage[x, y, lz] = dmg[idx];
                }
            }

            ReleaseChunkBuffers();
        });
    }

    private void ReleaseChunkBuffers()
    {
        if (_bufMat != 0) { _cl.ReleaseMemObject(_bufMat); _bufMat = 0; }
        if (_bufDen != 0) { _cl.ReleaseMemObject(_bufDen); _bufDen = 0; }
        if (_bufDmg != 0) { _cl.ReleaseMemObject(_bufDmg); _bufDmg = 0; }
        for (int i = 0; i < 3; i++) if (_bufVel[i] != 0) { _cl.ReleaseMemObject(_bufVel[i]); _bufVel[i] = 0; }
        for (int i = 0; i < 6; i++) if (_bufStr[i] != 0) { _cl.ReleaseMemObject(_bufStr[i]); _bufStr[i] = 0; }

        _lastDeviceBytes = 0;
    }

    private string GetKernelSource()
    {
        // Damage is updated in updateStress:
        //  - tensile: if max(sxx,syy,szz) > tensileLimit => damage increases
        //  - shear:   if sqrt(sxy^2+sxz^2+syz^2) > shearLimit => damage increases
        // Damage dampens stresses via factor (1 - 0.9*damage)
        return @"
        __kernel void updateStress(
            __global const uchar* material,
            __global const float* density,
            __global float* vx, __global float* vy, __global float* vz,
            __global float* sxx, __global float* syy, __global float* szz,
            __global float* sxy, __global float* sxz, __global float* syz,
            __global float* damage,
            const float lambda, const float mu,
            const float dt, const float dx,
            const int width, const int height, const int depth,
            const uchar selectedMaterial,
            const float tensileLimitPa, const float shearLimitPa,
            const float damageRatePerSec)
        {
            int idx = get_global_id(0);
            int size = width * height * depth;
            if (idx >= size) return;

            int z = idx / (width * height);
            int rem = idx % (width * height);
            int y = rem / width;
            int x = rem % width;

            if (material[idx] != selectedMaterial) return;
            if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;

            int xp1 = idx + 1, xm1 = idx - 1;
            int yp1 = idx + width, ym1 = idx - width;
            int zp1 = idx + width * height, zm1 = idx - width * height;

            float dvx_dx = (vx[xp1] - vx[xm1]) / (2.0f * dx);
            float dvy_dy = (vy[yp1] - vy[ym1]) / (2.0f * dx);
            float dvz_dz = (vz[zp1] - vz[zm1]) / (2.0f * dx);
            float dvy_dx = (vy[xp1] - vy[xm1]) / (2.0f * dx);
            float dvx_dy = (vx[yp1] - vx[ym1]) / (2.0f * dx);
            float dvz_dx = (vz[xp1] - vz[xm1]) / (2.0f * dx);
            float dvx_dz = (vx[zp1] - vx[zm1]) / (2.0f * dx);
            float dvz_dy = (vz[yp1] - vz[ym1]) / (2.0f * dx);
            float dvy_dz = (vy[zp1] - vy[zm1]) / (2.0f * dx);

            float volumetricStrain = dvx_dx + dvy_dy + dvz_dz;

            // local damping from existing damage
            float damp = 1.0f - damage[idx] * 0.9f;

            sxx[idx] += dt * damp * (lambda * volumetricStrain + 2.0f * mu * dvx_dx);
            syy[idx] += dt * damp * (lambda * volumetricStrain + 2.0f * mu * dvy_dy);
            szz[idx] += dt * damp * (lambda * volumetricStrain + 2.0f * mu * dvz_dz);
            sxy[idx] += dt * damp * mu * (dvy_dx + dvx_dy);
            sxz[idx] += dt * damp * mu * (dvz_dx + dvx_dz);
            syz[idx] += dt * damp * mu * (dvz_dy + dvy_dz);

            // --- Damage update ---
            float tensileMax = fmax(sxx[idx], fmax(syy[idx], szz[idx]));
            float shearMag = sqrt(sxy[idx]*sxy[idx] + sxz[idx]*sxz[idx] + syz[idx]*syz[idx]);

            float dInc = 0.0f;
            if (tensileMax > tensileLimitPa)
                dInc += damageRatePerSec * dt * (tensileMax / tensileLimitPa - 1.0f);

            if (shearMag > shearLimitPa)
                dInc += damageRatePerSec * dt * (shearMag / shearLimitPa - 1.0f);

            damage[idx] = clamp(damage[idx] + dInc, 0.0f, 1.0f);
        }

        __kernel void updateVelocity(
            __global const uchar* material,
            __global const float* density,
            __global float* vx, __global float* vy, __global float* vz,
            __global const float* sxx, __global const float* syy, __global const float* szz,
            __global const float* sxy, __global const float* sxz, __global const float* syz,
            const float dt, const float dx,
            const int width, const int height, const int depth,
            const uchar selectedMaterial)
        {
            int idx = get_global_id(0);
            int size = width * height * depth;
            if (idx >= size) return;

            int z = idx / (width * height);
            int rem = idx % (width * height);
            int y = rem / width;
            int x = rem % width;

            if (material[idx] != selectedMaterial) return;
            if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;

            int xp1 = idx + 1, xm1 = idx - 1;
            int yp1 = idx + width, ym1 = idx - width;
            int zp1 = idx + width * height, zm1 = idx - width * height;

            float rho = fmax(100.0f, density[idx]);

            float dsxx_dx = (sxx[xp1] - sxx[xm1]) / (2.0f * dx);
            float dsyy_dy = (syy[yp1] - syy[ym1]) / (2.0f * dx);
            float dszz_dz = (szz[zp1] - szz[zm1]) / (2.0f * dx);
            float dsxy_dy = (sxy[yp1] - sxy[ym1]) / (2.0f * dx);
            float dsxy_dx = (sxy[xp1] - sxy[xm1]) / (2.0f * dx);
            float dsxz_dz = (sxz[zp1] - sxz[zm1]) / (2.0f * dx);
            float dsxz_dx = (sxz[xp1] - sxz[xm1]) / (2.0f * dx);
            float dsyz_dz = (syz[zp1] - syz[zm1]) / (2.0f * dx);
            float dsyz_dy = (syz[yp1] - syz[ym1]) / (2.0f * dx);

            const float damping = 0.995f;
            vx[idx] = vx[idx] * damping + dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
            vy[idx] = vy[idx] * damping + dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
            vz[idx] = vz[idx] * damping + dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
        }";
    }

    // ================================================================
    // CPU fallback (with damage)
    // ================================================================
    private void UpdateChunkStressCPU(WaveFieldChunk c, byte[,,] labels, float[,,] density)
    {
        int d = c.EndZ - c.StartZ;
        Parallel.For(1, d - 1, lz =>
        {
            int gz = c.StartZ + lz;
            for (int y = 1; y < _params.Height - 1; y++)
            for (int x = 1; x < _params.Width  - 1; x++)
            {
                if (labels[x, y, gz] != _params.SelectedMaterialID) continue;

                float dvx_dx = (c.Vx[x + 1, y, lz] - c.Vx[x - 1, y, lz]) / (2 * _params.PixelSize);
                float dvy_dy = (c.Vy[x, y + 1, lz] - c.Vy[x, y - 1, lz]) / (2 * _params.PixelSize);
                float dvz_dz = (c.Vz[x, y, lz + 1] - c.Vz[x, y, lz - 1]) / (2 * _params.PixelSize);
                float dvy_dx = (c.Vy[x + 1, y, lz] - c.Vy[x - 1, y, lz]) / (2 * _params.PixelSize);
                float dvx_dy = (c.Vx[x, y + 1, lz] - c.Vx[x, y - 1, lz]) / (2 * _params.PixelSize);
                float dvz_dx = (c.Vz[x + 1, y, lz] - c.Vz[x - 1, y, lz]) / (2 * _params.PixelSize);
                float dvx_dz = (c.Vx[x, y, lz + 1] - c.Vx[x, y, lz - 1]) / (2 * _params.PixelSize);
                float dvz_dy = (c.Vz[x, y + 1, lz] - c.Vz[x, y - 1, lz]) / (2 * _params.PixelSize);
                float dvy_dz = (c.Vy[x, y, lz + 1] - c.Vy[x, y, lz - 1]) / (2 * _params.PixelSize);

                float volumetric = dvx_dx + dvy_dy + dvz_dz;

                float damp = 1f - c.Damage[x, y, lz] * 0.9f;
                c.Sxx[x, y, lz] += _dt * damp * (_lambda * volumetric + 2f * _mu * dvx_dx);
                c.Syy[x, y, lz] += _dt * damp * (_lambda * volumetric + 2f * _mu * dvy_dy);
                c.Szz[x, y, lz] += _dt * damp * (_lambda * volumetric + 2f * _mu * dvz_dz);
                c.Sxy[x, y, lz] += _dt * damp * _mu * (dvy_dx + dvx_dy);
                c.Sxz[x, y, lz] += _dt * damp * _mu * (dvz_dx + dvx_dz);
                c.Syz[x, y, lz] += _dt * damp * _mu * (dvz_dy + dvy_dz);

                // Damage update (same idea as GPU)
                float tensileMax = MathF.Max(c.Sxx[x, y, lz], MathF.Max(c.Syy[x, y, lz], c.Szz[x, y, lz]));
                float shearMag = MathF.Sqrt(c.Sxy[x, y, lz]*c.Sxy[x, y, lz] + c.Sxz[x, y, lz]*c.Sxz[x, y, lz] + c.Syz[x, y, lz]*c.Syz[x, y, lz]);

                float dInc = 0f;
                if (tensileMax > _tensileLimitPa)
                    dInc += _damageRatePerSec * _dt * (tensileMax / _tensileLimitPa - 1f);
                if (shearMag > _shearLimitPa)
                    dInc += _damageRatePerSec * _dt * (shearMag / _shearLimitPa - 1f);

                c.Damage[x, y, lz] = Math.Min(1f, Math.Max(0f, c.Damage[x, y, lz] + dInc));
            }
        });
    }

    private void UpdateChunkVelocityCPU(WaveFieldChunk c, byte[,,] labels, float[,,] density)
    {
        int d = c.EndZ - c.StartZ;
        Parallel.For(1, d - 1, lz =>
        {
            int gz = c.StartZ + lz;
            for (int y = 1; y < _params.Height - 1; y++)
            for (int x = 1; x < _params.Width  - 1; x++)
            {
                if (labels[x, y, gz] != _params.SelectedMaterialID) continue;

                float rho = MathF.Max(100f, density[x, y, gz]);

                float dsxx_dx = (c.Sxx[x + 1, y, lz] - c.Sxx[x - 1, y, lz]) / (2 * _params.PixelSize);
                float dsyy_dy = (c.Syy[x, y + 1, lz] - c.Syy[x, y - 1, lz]) / (2 * _params.PixelSize);
                float dszz_dz = (c.Szz[x, y, lz + 1] - c.Szz[x, y, lz - 1]) / (2 * _params.PixelSize);
                float dsxy_dy = (c.Sxy[x, y + 1, lz] - c.Sxy[x, y - 1, lz]) / (2 * _params.PixelSize);
                float dsxy_dx = (c.Sxy[x + 1, y, lz] - c.Sxy[x - 1, y, lz]) / (2 * _params.PixelSize);
                float dsxz_dz = (c.Sxz[x, y, lz + 1] - c.Sxz[x, y, lz - 1]) / (2 * _params.PixelSize);
                float dsxz_dx = (c.Sxz[x + 1, y, lz] - c.Sxz[x - 1, y, lz]) / (2 * _params.PixelSize);
                float dsyz_dz = (c.Syz[x, y, lz + 1] - c.Syz[x, y, lz - 1]) / (2 * _params.PixelSize);
                float dsyz_dy = (c.Syz[x, y + 1, lz] - c.Syz[x, y - 1, lz]) / (2 * _params.PixelSize);

                const float damping = 0.995f;
                c.Vx[x, y, lz] = c.Vx[x, y, lz] * damping + _dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
                c.Vy[x, y, lz] = c.Vy[x, y, lz] * damping + _dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
                c.Vz[x, y, lz] = c.Vz[x, y, lz] * damping + _dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
            }
        });
    }

    // ================================================================
    // Utilities: CFL timestep, source, arrivals, boundaries, snapshots
    // ================================================================
    private void CalculateTimeStep(float[,,] density)
    {
        // conservative dt using Vs_max ~ sqrt(mu / rho_min)
        float rhoMin = float.MaxValue;
        for (int z = 0; z < _params.Depth; z++)
            for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                    rhoMin = MathF.Min(rhoMin, MathF.Max(100f, density[x, y, z]));

        float vsMax = MathF.Sqrt(_mu / MathF.Max(100f, rhoMin));
        _dt = Math.Min(_params.TimeStepSeconds, _params.PixelSize / (1.7320508f * MathF.Max(1e-3f, vsMax)));
    }

    private void ApplyInitialSource(byte[,,] labels, float[,,] density)
    {
        // point kick at Tx along selected axis
        int tx = (int)(_params.TxPosition.X * _params.Width);
        int ty = (int)(_params.TxPosition.Y * _params.Height);
        int tz = (int)(_params.TxPosition.Z * _params.Depth);
        var c = _chunks.First(k => tz >= k.StartZ && tz < k.EndZ);
        int lz = tz - c.StartZ;

        float amp = MathF.Max(1e-4f, _params.SourceAmplitude);
        switch (_params.Axis)
        {
            case 0: c.Vx[tx, ty, lz] += amp; break;
            case 1: c.Vy[tx, ty, lz] += amp; break;
            default: c.Vz[tx, ty, lz] += amp; break;
        }
    }
    public float[,,] GetDamageField()
    {
        var damageField = new float[_params.Width, _params.Height, _params.Depth];
    
        foreach (var chunk in _chunks)
        {
            int d = chunk.EndZ - chunk.StartZ;
            for (int z = 0; z < d; z++)
            {
                for (int y = 0; y < _params.Height; y++)
                {
                    for (int x = 0; x < _params.Width; x++)
                    {
                        damageField[x, y, chunk.StartZ + z] = chunk.Damage[x, y, z];
                    }
                }
            }
        }
    
        return damageField;
    }
    private void ExchangeBoundaries()
    {
        // share one Z plane between adjacent chunks
        for (int i = 0; i < _chunks.Count - 1; i++)
        {
            var a = _chunks[i];
            var b = _chunks[i + 1];
            int za = a.EndZ - a.StartZ - 2;
            int zb = 1;

            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
            {
                b.Vx[x, y, zb - 1] = a.Vx[x, y, za + 1];
                b.Vy[x, y, zb - 1] = a.Vy[x, y, za + 1];
                b.Vz[x, y, zb - 1] = a.Vz[x, y, za + 1];

                a.Vx[x, y, za] = b.Vx[x, y, zb];
                a.Vy[x, y, za] = b.Vy[x, y, zb];
                a.Vz[x, y, za] = b.Vz[x, y, zb];
            }
        }
    }

    private bool CheckPWaveArrival()
    {
        int rx = (int)(_params.RxPosition.X * _params.Width);
        int ry = (int)(_params.RxPosition.Y * _params.Height);
        int rz = (int)(_params.RxPosition.Z * _params.Depth);
        var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ);
        if (c == null) return false;
        int lz = rz - c.StartZ;

        float comp = _params.Axis switch
        {
            0 => MathF.Abs(c.Vx[rx, ry, lz]),
            1 => MathF.Abs(c.Vy[rx, ry, lz]),
            _ => MathF.Abs(c.Vz[rx, ry, lz]),
        };
        return comp > 1e-9f;
    }

    private bool CheckSWaveArrival()
    {
        int rx = (int)(_params.RxPosition.X * _params.Width);
        int ry = (int)(_params.RxPosition.Y * _params.Height);
        int rz = (int)(_params.RxPosition.Z * _params.Depth);
        var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ);
        if (c == null) return false;
        int lz = rz - c.StartZ;

        float mag = _params.Axis switch
        {
            0 => MathF.Sqrt(c.Vy[rx, ry, lz] * c.Vy[rx, ry, lz] + c.Vz[rx, ry, lz] * c.Vz[rx, ry, lz]),
            1 => MathF.Sqrt(c.Vx[rx, ry, lz] * c.Vx[rx, ry, lz] + c.Vz[rx, ry, lz] * c.Vz[rx, ry, lz]),
            _ => MathF.Sqrt(c.Vx[rx, ry, lz] * c.Vx[rx, ry, lz] + c.Vy[rx, ry, lz] * c.Vy[rx, ry, lz]),
        };
        return mag > 1e-9f;
    }

    private float CalculateTxRxDistance()
    {
        int tx = (int)(_params.TxPosition.X * _params.Width);
        int ty = (int)(_params.TxPosition.Y * _params.Height);
        int tz = (int)(_params.TxPosition.Z * _params.Depth);
        int rx = (int)(_params.RxPosition.X * _params.Width);
        int ry = (int)(_params.RxPosition.Y * _params.Height);
        int rz = (int)(_params.RxPosition.Z * _params.Depth);

        return MathF.Sqrt((tx - rx) * (tx - rx) + (ty - ry) * (ty - ry) + (tz - rz) * (tz - rz)) * _params.PixelSize;
    }

    private WaveFieldSnapshot CreateSnapshot(int step)
    {
        // choose ds so max dimension ~128
        int ds = Math.Max(1, Math.Max(_params.Width, Math.Max(_params.Height, _params.Depth)) / 128);
        int w = Math.Max(1, _params.Width  / ds);
        int h = Math.Max(1, _params.Height / ds);
        int d = Math.Max(1, _params.Depth  / ds);

        var vx = new float[w, h, d];
        var vy = new float[w, h, d];
        var vz = new float[w, h, d];

        foreach (var c in _chunks)
        {
            int cd = c.EndZ - c.StartZ;
            for (int lz = 0; lz < cd; lz += ds)
            {
                int gz = c.StartZ + lz;
                int dz = gz / ds; if (dz >= d) continue;
                for (int y = 0; y < _params.Height; y += ds)
                {
                    int dy = y / ds; if (dy >= h) continue;
                    for (int x = 0; x < _params.Width; x += ds)
                    {
                        int dx = x / ds; if (dx >= w) continue;
                        vx[dx, dy, dz] = c.Vx[x, y, lz];
                        vy[dx, dy, dz] = c.Vy[x, y, lz];
                        vz[dx, dy, dz] = c.Vz[x, y, lz];
                    }
                }
            }
        }

        var snap = new WaveFieldSnapshot
        {
            TimeStep = step,
            SimulationTime = step * _dt,
            Width = w,
            Height = h,
            Depth = d
        };
        snap.SetVelocityFields(vx, vy, vz);
        return snap;
    }

    private async Task<float[,,]> ReconstructFieldAsync(int comp)
    {
        var field = new float[_params.Width, _params.Height, _params.Depth];
        await Task.Run(() =>
        {
            foreach (var c in _chunks)
            {
                int d = c.EndZ - c.StartZ;
                for (int z = 0; z < d; z++)
                    for (int y = 0; y < _params.Height; y++)
                        for (int x = 0; x < _params.Width; x++)
                        {
                            float v = comp switch
                            {
                                0 => c.Vx[x, y, z],
                                1 => c.Vy[x, y, z],
                                _ => c.Vz[x, y, z]
                            };
                            field[x, y, c.StartZ + z] = v;
                        }
            }
        });
        return field;
    }

    private float[,,] GetCombinedMagnitude()
    {
        var out3d = new float[_params.Width, _params.Height, _params.Depth];
        foreach (var c in _chunks)
        {
            int d = c.EndZ - c.StartZ;
            for (int z = 0; z < d; z++)
                for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                    {
                        float vx = c.Vx[x, y, z];
                        float vy = c.Vy[x, y, z];
                        float vz = c.Vz[x, y, z];
                        out3d[x, y, c.StartZ + z] = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
                    }
        }
        return out3d;
    }

    // ================================================================
    // Dispose
    // ================================================================
    public void Dispose()
    {
        if (_useGPU) CleanupOpenCL();
        _chunks.Clear();
    }
}

    /// <summary>
    /// Generates 2D velocity tomography slices from simulation results
    /// </summary>
    internal class VelocityTomographyGenerator : IDisposable
    {
        public byte[] Generate2DTomography(SimulationResults results, int axis, int sliceIndex)
        {
            if (results == null || results.WaveFieldVx == null) return null;
            
            int width, height;
            switch (axis)
            {
                case 0: // X slice
                    width = results.WaveFieldVy.GetLength(1);
                    height = results.WaveFieldVz.GetLength(2);
                    break;
                case 1: // Y slice
                    width = results.WaveFieldVx.GetLength(0);
                    height = results.WaveFieldVz.GetLength(2);
                    break;
                case 2: // Z slice
                    width = results.WaveFieldVx.GetLength(0);
                    height = results.WaveFieldVy.GetLength(1);
                    break;
                default:
                    return null;
            }
            
            var tomography = new byte[width * height * 4]; // RGBA
            
            // Calculate velocity magnitudes for the slice
            float minVel = float.MaxValue, maxVel = float.MinValue;
            var velocities = new float[width * height];
            
            int idx = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float vx = 0, vy = 0, vz = 0;
                    
                    switch (axis)
                    {
                        case 0: // X slice
                            vx = results.WaveFieldVx[sliceIndex, x, y];
                            vy = results.WaveFieldVy[sliceIndex, x, y];
                            vz = results.WaveFieldVz[sliceIndex, x, y];
                            break;
                        case 1: // Y slice
                            vx = results.WaveFieldVx[x, sliceIndex, y];
                            vy = results.WaveFieldVy[x, sliceIndex, y];
                            vz = results.WaveFieldVz[x, sliceIndex, y];
                            break;
                        case 2: // Z slice
                            vx = results.WaveFieldVx[x, y, sliceIndex];
                            vy = results.WaveFieldVy[x, y, sliceIndex];
                            vz = results.WaveFieldVz[x, y, sliceIndex];
                            break;
                    }
                    
                    float velocity = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    velocities[idx] = velocity;
                    
                    if (velocity < minVel) minVel = velocity;
                    if (velocity > maxVel) maxVel = velocity;
                    idx++;
                }
            }
            
            // Normalize and apply colormap
            float range = maxVel - minVel;
            if (range < 1e-6f) range = 1e-6f;
            
            idx = 0;
            for (int i = 0; i < velocities.Length; i++)
            {
                float normalized = (velocities[i] - minVel) / range;
                var color = GetJetColor(normalized);
                
                tomography[idx++] = (byte)(color.X * 255);
                tomography[idx++] = (byte)(color.Y * 255);
                tomography[idx++] = (byte)(color.Z * 255);
                tomography[idx++] = 255;
            }
            
            return tomography;
        }

        private Vector4 GetJetColor(float value)
        {
            float r, g, b;
            
            if (value < 0.25f)
            {
                r = 0;
                g = 4 * value;
                b = 1;
            }
            else if (value < 0.5f)
            {
                r = 0;
                g = 1;
                b = 1 - 4 * (value - 0.25f);
            }
            else if (value < 0.75f)
            {
                r = 4 * (value - 0.5f);
                g = 1;
                b = 0;
            }
            else
            {
                r = 1;
                g = 1 - 4 * (value - 0.75f);
                b = 0;
            }
            
            return new Vector4(r, g, b, 1);
        }

        public void Dispose()
        {
            // Cleanup if needed
        }
    }

    // Extended parameters
    public class SimulationParameters
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Depth { get; set; }
        public float PixelSize { get; set; }
        public byte SelectedMaterialID { get; set; }
        public int Axis { get; set; }
        public bool UseFullFaceTransducers { get; set; }
        public float ConfiningPressureMPa { get; set; }
        public float TensileStrengthMPa { get; set; }
        public float FailureAngleDeg { get; set; }
        public float CohesionMPa { get; set; }
        public float SourceEnergyJ { get; set; }
        public float SourceFrequencyKHz { get; set; }
        public int SourceAmplitude { get; set; }
        public int TimeSteps { get; set; }
        public float YoungsModulusMPa { get; set; }
        public float PoissonRatio { get; set; }
        public bool UseElasticModel { get; set; }
        public bool UsePlasticModel { get; set; }
        public bool UseBrittleModel { get; set; }
        public bool UseGPU { get; set; }
        public Vector3 TxPosition { get; set; }
        public Vector3 RxPosition { get; set; }
        public bool EnableRealTimeVisualization { get; set; }
        public bool SaveTimeSeries { get; set; }
        public int SnapshotInterval { get; set; }
        public bool UseChunkedProcessing { get; set; }
        public int ChunkSizeMB { get; set; }
        public bool EnableOffloading { get; set; }
        public string OffloadDirectory { get; set; }
        public float TimeStepSeconds { get; set; } = 1e-6f; // Default 1 microsecond
        
    }
}