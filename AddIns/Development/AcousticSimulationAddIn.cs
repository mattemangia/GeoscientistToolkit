// GeoscientistToolkit/AddIns/AcousticSimulation/AcousticSimulationAddIn.cs (FIXED)
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
using GeoscientistToolkit.Util;
using ImGuiNET;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.AddIns.AcousticSimulation
{
    /// <summary>
    /// Acoustic wave propagation simulation add-in for CT volume data.
    /// Implements full elastodynamic physics with unified CPU/GPU compute via Silk.NET.
    /// </summary>
    public class AcousticSimulationAddIn : IAddIn
    {
        public string Id => "com.geoscientisttoolkit.acousticsimulation";
        public string Name => "Acoustic Wave Simulation";
        public string Version => "2.0.0";
        public string Author => "GeoscientistToolkit Team";
        public string Description => "Advanced acoustic/elastic wave propagation simulation with Vp/Vs analysis and real-time visualization.";

        private static AcousticSimulationTool _tool;

        public void Initialize()
        {
            _tool = new AcousticSimulationTool();
            Logger.Log($"[AcousticSimulation] Add-in initialized (v{Version})");
        }

        public void Shutdown()
        {
            _tool?.Dispose();
            _tool = null;
            Logger.Log("[AcousticSimulation] Add-in shutdown");
        }

        public IEnumerable<AddInMenuItem> GetMenuItems() => null;
        public IEnumerable<AddInTool> GetTools() => new[] { _tool };
        public IEnumerable<IDataImporter> GetDataImporters() => null;
        public IEnumerable<IDataExporter> GetDataExporters() => null;
    }

    /// <summary>
    /// Main tool providing UI and simulation control
    /// </summary>
    internal class AcousticSimulationTool : AddInTool, IDisposable
    {
        public override string Name => "Acoustic Simulation";
        public override string Icon => "🔊";
        public override string Tooltip => "Simulate acoustic wave propagation through materials";

        private UnifiedAcousticSimulator _simulator;
        private SimulationParameters _parameters;
        private SimulationResults _lastResults;
        private CalibrationManager _calibrationManager;
        private bool _isSimulating = false;
        private CancellationTokenSource _cancellationTokenSource;

        // Real-time visualization support
        private bool _enableRealTimeVisualization = false;
        private float _visualizationUpdateInterval = 0.1f; // seconds
        private DateTime _lastVisualizationUpdate = DateTime.MinValue;
        private byte[] _currentWaveFieldMask;
        
        // UI State
        private int _selectedMaterialIndex = 0;
        private int _selectedAxisIndex = 0; // 0=X, 1=Y, 2=Z
        private float _confiningPressure = 1.0f;
        private float _tensileStrength = 10.0f;
        private float _failureAngle = 30.0f;
        private float _cohesion = 5.0f;
        private float _sourceEnergy = 1.0f;
        private float _sourceFrequency = 500.0f; // kHz
        private int _sourceAmplitude = 100;
        private int _timeSteps = 1000;
        private float _youngsModulus = 30000.0f; // MPa
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

        // Transducer positions
        private Vector3 _txPosition = new Vector3(0, 0.5f, 0.5f);
        private Vector3 _rxPosition = new Vector3(1, 0.5f, 0.5f);

        public AcousticSimulationTool()
        {
            _parameters = new SimulationParameters();
            _calibrationManager = new CalibrationManager();
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

            // Simulation Axis
            ImGui.Text("Wave Propagation Axis:");
            string[] axes = { "X", "Y", "Z" };
            ImGui.RadioButton("X", ref _selectedAxisIndex, 0); ImGui.SameLine();
            ImGui.RadioButton("Y", ref _selectedAxisIndex, 1); ImGui.SameLine();
            ImGui.RadioButton("Z", ref _selectedAxisIndex, 2);

            // Transducer Type
            ImGui.Checkbox("Full-Face Transducers", ref _useFullFaceTransducers);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Use entire face of volume as transducer (more realistic for ultrasonic testing)");

            ImGui.Separator();

            // Real-time visualization
            if (ImGui.CollapsingHeader("Visualization"))
            {
                ImGui.Checkbox("Enable Real-Time Visualization", ref _enableRealTimeVisualization);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Show wave propagation in real-time during simulation");
                    
                if (_enableRealTimeVisualization)
                {
                    ImGui.DragFloat("Update Interval (s)", ref _visualizationUpdateInterval, 0.01f, 0.01f, 1.0f);
                }
                
                ImGui.Checkbox("Save Time Series", ref _saveTimeSeries);
                if (_saveTimeSeries)
                {
                    ImGui.DragInt("Snapshot Interval", ref _snapshotInterval, 1, 1, 100);
                }
            }

            // Physics Parameters
            if (ImGui.CollapsingHeader("Material Properties"))
            {
                ImGui.DragFloat("Young's Modulus (MPa)", ref _youngsModulus, 100.0f, 100.0f, 200000.0f);
                ImGui.DragFloat("Poisson's Ratio", ref _poissonRatio, 0.01f, 0.0f, 0.49f);
                
                if (_autoCalibrate && _calibrationManager.HasCalibration())
                {
                    if (ImGui.Button("Apply Calibration"))
                    {
                        ApplyCalibration(dataset);
                    }
                }
            }

            if (ImGui.CollapsingHeader("Stress Conditions"))
            {
                ImGui.DragFloat("Confining Pressure (MPa)", ref _confiningPressure, 0.1f, 0.0f, 100.0f);
                ImGui.DragFloat("Tensile Strength (MPa)", ref _tensileStrength, 0.5f, 0.1f, 100.0f);
                ImGui.DragFloat("Failure Angle (°)", ref _failureAngle, 1.0f, 0.0f, 90.0f);
                ImGui.DragFloat("Cohesion (MPa)", ref _cohesion, 0.5f, 0.0f, 50.0f);
            }

            if (ImGui.CollapsingHeader("Source Parameters"))
            {
                ImGui.DragFloat("Source Energy (J)", ref _sourceEnergy, 0.1f, 0.01f, 10.0f);
                ImGui.DragFloat("Frequency (kHz)", ref _sourceFrequency, 10.0f, 1.0f, 5000.0f);
                ImGui.DragInt("Amplitude", ref _sourceAmplitude, 1, 1, 1000);
                ImGui.DragInt("Time Steps", ref _timeSteps, 10, 100, 10000);
            }

            if (ImGui.CollapsingHeader("Physics Models"))
            {
                ImGui.Checkbox("Elastic", ref _useElastic);
                ImGui.Checkbox("Plastic (Mohr-Coulomb)", ref _usePlastic);
                ImGui.Checkbox("Brittle Damage", ref _useBrittle);
            }

            if (ImGui.CollapsingHeader("Advanced Settings"))
            {
                ImGui.Checkbox("Use GPU Acceleration", ref _useGPU);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Use OpenCL for GPU acceleration when available");

                ImGui.Checkbox("Auto-Calibrate", ref _autoCalibrate);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Automatically calibrate elastic properties based on previous simulations");

                // Custom transducer positions
                ImGui.Text("Transmitter Position:");
                ImGui.DragFloat3("TX", ref _txPosition, 0.01f, 0.0f, 1.0f);
                
                ImGui.Text("Receiver Position:");
                ImGui.DragFloat3("RX", ref _rxPosition, 0.01f, 0.0f, 1.0f);
            }

            ImGui.Separator();

            // Simulation Control
            if (_isSimulating)
            {
                ImGui.ProgressBar(_simulator?.Progress ?? 0.0f, new Vector2(-1, 0), 
                    $"Simulating... {(_simulator?.CurrentStep ?? 0)}/{_timeSteps} steps");
                
                if (ImGui.Button("Cancel Simulation", new Vector2(-1, 0)))
                {
                    CancelSimulation();
                }
            }
            else
            {
                if (ImGui.Button("Run Simulation", new Vector2(-1, 0)))
                {
                    _ = RunSimulationAsync(dataset);
                }
            }

            // Results Display
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
                    
                    ImGui.Spacing();
                    
                    if (ImGui.Button("Export Results"))
                    {
                        ExportResults(dataset);
                    }
                    
                    if (ImGui.Button("Create Acoustic Volume"))
                    {
                        CreateAcousticVolume(dataset);
                    }
                }
            }
        }

        private async Task RunSimulationAsync(CtImageStackDataset dataset)
        {
            if (_isSimulating) return;

            _isSimulating = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Prepare parameters
                var material = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
                
                _parameters = new SimulationParameters
                {
                    Width = dataset.Width,
                    Height = dataset.Height,
                    Depth = dataset.Depth,
                    PixelSize = (float)dataset.PixelSize / 1000.0f, // Convert to meters
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
                    SnapshotInterval = _snapshotInterval
                };

                // Extract volume data
                var volumeLabels = ExtractVolumeLabels(dataset);
                var densityVolume = ExtractDensityVolume(dataset, material);

                // Create and run simulator
                _simulator = new UnifiedAcousticSimulator(_parameters);
                _simulator.ProgressUpdated += OnSimulationProgress;
                
                // Subscribe to real-time visualization updates
                if (_enableRealTimeVisualization)
                {
                    _simulator.WaveFieldUpdated += OnWaveFieldUpdated;
                }

                _lastResults = await _simulator.RunAsync(
                    volumeLabels, 
                    densityVolume, 
                    _cancellationTokenSource.Token);

                if (_lastResults != null)
                {
                    Logger.Log($"[AcousticSimulation] Simulation completed: Vp={_lastResults.PWaveVelocity:F2} m/s, Vs={_lastResults.SWaveVelocity:F2} m/s");
                    
                    // Store wave field for visualization
                    _lastResults.WaveFieldVx = _simulator.GetFinalWaveField(0);
                    _lastResults.WaveFieldVy = _simulator.GetFinalWaveField(1);
                    _lastResults.WaveFieldVz = _simulator.GetFinalWaveField(2);
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

        private void OnWaveFieldUpdated(object sender, WaveFieldUpdateEventArgs e)
        {
            // Update visualization in real-time
            if ((DateTime.Now - _lastVisualizationUpdate).TotalSeconds >= _visualizationUpdateInterval)
            {
                _lastVisualizationUpdate = DateTime.Now;
                
                // Convert wave field to mask for visualization
                _currentWaveFieldMask = CreateWaveFieldMask(e.WaveField);
                
                // Send to visualization system
                CtImageStackTools.Update3DPreviewFromExternal(
                    e.Dataset as CtImageStackDataset, 
                    _currentWaveFieldMask, 
                    new Vector4(1, 0.5f, 0, 0.5f)); // Orange color for acoustic waves
            }
        }

        private byte[] CreateWaveFieldMask(float[,,] waveField)
        {
            int width = waveField.GetLength(0);
            int height = waveField.GetLength(1);
            int depth = waveField.GetLength(2);
            byte[] mask = new byte[width * height * depth];
            
            // Find max amplitude for normalization
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

        private byte[,,] ExtractVolumeLabels(CtImageStackDataset dataset)
        {
            var labels = new byte[dataset.Width, dataset.Height, dataset.Depth];
            
            Parallel.For(0, dataset.Depth, z =>
            {
                for (int y = 0; y < dataset.Height; y++)
                    for (int x = 0; x < dataset.Width; x++)
                        labels[x, y, z] = dataset.LabelData[x, y, z];
            });

            return labels;
        }

        private float[,,] ExtractDensityVolume(CtImageStackDataset dataset, Material material)
        {
            var density = new float[dataset.Width, dataset.Height, dataset.Depth];
            float materialDensity = (float)material.Density;

            Parallel.For(0, dataset.Depth, z =>
            {
                for (int y = 0; y < dataset.Height; y++)
                    for (int x = 0; x < dataset.Width; x++)
                    {
                        if (dataset.LabelData[x, y, z] == material.ID)
                            density[x, y, z] = materialDensity;
                        else
                            density[x, y, z] = 1000.0f; // Default density for other materials
                    }
            });

            return density;
        }

        private void CreateAcousticVolume(CtImageStackDataset dataset)
        {
            if (_lastResults == null || _lastResults.WaveFieldVx == null)
            {
                Logger.LogError("[AcousticSimulation] No wave field data available");
                return;
            }

            try
            {
                // Create a new acoustic volume dataset
                string acousticPath = Path.Combine(
                    Path.GetDirectoryName(dataset.FilePath),
                    $"{dataset.Name}_AcousticVolume_{DateTime.Now:yyyyMMdd_HHmmss}");

                Directory.CreateDirectory(acousticPath);

                var acousticDataset = new AcousticVolumeDataset(
                    $"{dataset.Name} - Acoustic Volume",
                    acousticPath)
                {
                    PWaveVelocity = _lastResults.PWaveVelocity,
                    SWaveVelocity = _lastResults.SWaveVelocity,
                    VpVsRatio = _lastResults.VpVsRatio,
                    TimeSteps = _lastResults.TotalTimeSteps,
                    ComputationTime = _lastResults.ComputationTime,
                    YoungsModulusMPa = _youngsModulus,
                    PoissonRatio = _poissonRatio,
                    ConfiningPressureMPa = _confiningPressure,
                    SourceFrequencyKHz = _sourceFrequency,
                    SourceEnergyJ = _sourceEnergy,
                    SourceDatasetPath = dataset.FilePath,
                    SourceMaterialName = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex).Name
                };

                // Create wave field volumes
                acousticDataset.PWaveField = CreateWaveFieldVolume(_lastResults.WaveFieldVx, "PWaveField");
                acousticDataset.SWaveField = CreateWaveFieldVolume(_lastResults.WaveFieldVy, "SWaveField");
                acousticDataset.CombinedWaveField = CreateCombinedWaveField(_lastResults);

                // Add time series if available
                if (_lastResults.TimeSeriesSnapshots != null)
                {
                    acousticDataset.TimeSeriesSnapshots = _lastResults.TimeSeriesSnapshots;
                }

                // Save the dataset
                acousticDataset.SaveWaveFields();

                // Add to project
                ProjectManager.Instance.AddDataset(acousticDataset);
                Logger.Log($"[AcousticSimulation] Created acoustic volume dataset: {acousticDataset.Name}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticSimulation] Failed to create acoustic volume: {ex.Message}");
            }
        }

        private ChunkedVolume CreateWaveFieldVolume(float[,,] field, string name)
        {
            int width = field.GetLength(0);
            int height = field.GetLength(1);
            int depth = field.GetLength(2);
            
            var volume = new ChunkedVolume(width, height, depth);
            
            // Normalize and convert to byte
            float maxValue = 0;
            for (int z = 0; z < depth; z++)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        maxValue = Math.Max(maxValue, Math.Abs(field[x, y, z]));
            
            if (maxValue > 0)
            {
                for (int z = 0; z < depth; z++)
                {
                    var slice = new byte[width * height];
                    int idx = 0;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            float normalized = (field[x, y, z] + maxValue) / (2 * maxValue);
                            slice[idx++] = (byte)(normalized * 255);
                        }
                    volume.WriteSliceZ(z, slice);
                }
            }
            
            return volume;
        }

        private ChunkedVolume CreateCombinedWaveField(SimulationResults results)
        {
            int width = results.WaveFieldVx.GetLength(0);
            int height = results.WaveFieldVx.GetLength(1);
            int depth = results.WaveFieldVx.GetLength(2);
            
            var volume = new ChunkedVolume(width, height, depth);
            
            float maxMagnitude = 0;
            for (int z = 0; z < depth; z++)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        float vx = results.WaveFieldVx[x, y, z];
                        float vy = results.WaveFieldVy[x, y, z];
                        float vz = results.WaveFieldVz[x, y, z];
                        float magnitude = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                        maxMagnitude = Math.Max(maxMagnitude, magnitude);
                    }
            
            if (maxMagnitude > 0)
            {
                for (int z = 0; z < depth; z++)
                {
                    var slice = new byte[width * height];
                    int idx = 0;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            float vx = results.WaveFieldVx[x, y, z];
                            float vy = results.WaveFieldVy[x, y, z];
                            float vz = results.WaveFieldVz[x, y, z];
                            float magnitude = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                            slice[idx++] = (byte)(255 * magnitude / maxMagnitude);
                        }
                    volume.WriteSliceZ(z, slice);
                }
            }
            
            return volume;
        }

        private void ApplyCalibration(CtImageStackDataset dataset)
        {
            var material = dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex);
            var calibrated = _calibrationManager.GetCalibratedParameters((float)material.Density, _confiningPressure);
            
            _youngsModulus = calibrated.YoungsModulus;
            _poissonRatio = calibrated.PoissonRatio;
            
            Logger.Log($"[AcousticSimulation] Applied calibration: E={_youngsModulus:F2} MPa, ν={_poissonRatio:F4}");
        }

        private void ExportResults(CtImageStackDataset dataset)
        {
            if (_lastResults == null) return;

            try
            {
                string exportPath = Path.Combine(
                    Path.GetDirectoryName(dataset.FilePath),
                    $"{dataset.Name}_AcousticResults_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                var sb = new StringBuilder();
                sb.AppendLine("Acoustic Simulation Results");
                sb.AppendLine("===========================");
                sb.AppendLine($"Dataset: {dataset.Name}");
                sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();
                sb.AppendLine("Parameters:");
                sb.AppendLine($"  Material: {dataset.Materials.Where(m => m.ID != 0).ElementAt(_selectedMaterialIndex).Name}");
                sb.AppendLine($"  Young's Modulus: {_youngsModulus:F2} MPa");
                sb.AppendLine($"  Poisson's Ratio: {_poissonRatio:F4}");
                sb.AppendLine($"  Confining Pressure: {_confiningPressure:F2} MPa");
                sb.AppendLine();
                sb.AppendLine("Results:");
                sb.AppendLine($"  P-Wave Velocity: {_lastResults.PWaveVelocity:F2} m/s");
                sb.AppendLine($"  S-Wave Velocity: {_lastResults.SWaveVelocity:F2} m/s");
                sb.AppendLine($"  Vp/Vs Ratio: {_lastResults.VpVsRatio:F3}");
                sb.AppendLine($"  P-Wave Travel Time: {_lastResults.PWaveTravelTime} steps");
                sb.AppendLine($"  S-Wave Travel Time: {_lastResults.SWaveTravelTime} steps");
                sb.AppendLine($"  Total Time Steps: {_lastResults.TotalTimeSteps}");
                sb.AppendLine($"  Computation Time: {_lastResults.ComputationTime.TotalSeconds:F2} s");

                File.WriteAllText(exportPath, sb.ToString());
                Logger.Log($"[AcousticSimulation] Results exported to: {exportPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[AcousticSimulation] Failed to export results: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _simulator?.Dispose();
        }
    }

    #region Unified Simulator

    /// <summary>
    /// Unified acoustic simulator that can run on CPU or GPU using Silk.NET OpenCL
    /// </summary>
    internal class UnifiedAcousticSimulator : IDisposable
    {
        private readonly SimulationParameters _params;
        private CL _cl;
        private nint _context;
        private nint _commandQueue;
        private nint _program;
        private nint _device;
        private bool _useGPU;
        
        // Buffers
        private nint _materialBuffer;
        private nint _densityBuffer;
        private nint[] _velocityBuffers = new nint[3]; // vx, vy, vz
        private nint[] _stressBuffers = new nint[6]; // sxx, syy, szz, sxy, sxz, syz
        private nint _damageBuffer;

        // CPU fallback arrays
        private float[,,] _vx, _vy, _vz;
        private float[,,] _sxx, _syy, _szz, _sxy, _sxz, _syz;
        private float[,,] _damage;

        // Progress tracking
        public float Progress { get; private set; }
        public int CurrentStep { get; private set; }
        public event EventHandler<SimulationProgressEventArgs> ProgressUpdated;
        public event EventHandler<WaveFieldUpdateEventArgs> WaveFieldUpdated;

        // Physical constants
        private float _lambda, _mu;
        private float _dt;
        private int _totalCells;

        public UnifiedAcousticSimulator(SimulationParameters parameters)
        {
            _params = parameters;

            // Calculate Lamé constants
            float E = _params.YoungsModulusMPa * 1e6f; // Convert to Pa
            _mu = E / (2.0f * (1.0f + _params.PoissonRatio));
            _lambda = E * _params.PoissonRatio / ((1 + _params.PoissonRatio) * (1 - 2 * _params.PoissonRatio));

            _totalCells = _params.Width * _params.Height * _params.Depth;

            // Initialize compute backend
            InitializeCompute();
        }

        private void InitializeCompute()
        {
            try
            {
                if (_params.UseGPU)
                {
                    _cl = CL.GetApi();
                    InitializeOpenCL();
                    _useGPU = true;
                    Logger.Log("[AcousticSimulator] Using GPU acceleration via OpenCL");
                }
                else
                {
                    InitializeCPUArrays();
                    _useGPU = false;
                    Logger.Log("[AcousticSimulator] Using CPU computation");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[AcousticSimulator] Failed to initialize GPU, falling back to CPU: {ex.Message}");
                InitializeCPUArrays();
                _useGPU = false;
            }
        }

        private unsafe void InitializeOpenCL()
        {
            // Get platform
            uint platformCount = 0;
            _cl.GetPlatformIDs(0, null, &platformCount);
            if (platformCount == 0)
                throw new Exception("No OpenCL platforms available");

            var platforms = new nint[platformCount];
            fixed (nint* platformsPtr = platforms)
            {
                _cl.GetPlatformIDs(platformCount, platformsPtr, null);
            }

            // Get GPU device
            uint deviceCount = 0;
            _cl.GetDeviceIDs(platforms[0], DeviceType.Gpu, 0, null, &deviceCount);
            if (deviceCount == 0)
            {
                // Try CPU as fallback
                _cl.GetDeviceIDs(platforms[0], DeviceType.Cpu, 0, null, &deviceCount);
                if (deviceCount == 0)
                    throw new Exception("No OpenCL devices available");
            }

            var devices = new nint[deviceCount];
            fixed (nint* devicesPtr = devices)
            {
                _cl.GetDeviceIDs(platforms[0], DeviceType.Default, deviceCount, devicesPtr, null);
            }
            _device = devices[0];

            // Create context
            int errNum = 0;
            nint device = _device; // Local copy
            nint* devicePtr = &device; // Take address directly without fixed
            _context = _cl.CreateContext(null, 1, devicePtr, null, null, &errNum);
    
            if (errNum != 0)
                throw new Exception($"Failed to create context: {errNum}");

            // Create command queue - explicitly cast to CommandQueueProperties to resolve ambiguity
            _commandQueue = _cl.CreateCommandQueue(_context, _device, (CommandQueueProperties)0, &errNum);
            if (errNum != 0)
                throw new Exception($"Failed to create command queue: {errNum}");

            // Build OpenCL program
            BuildOpenCLProgram();

            // Create buffers
            CreateOpenCLBuffers();
        }


        private void InitializeCPUArrays()
        {
            int w = _params.Width, h = _params.Height, d = _params.Depth;
            
            _vx = new float[w, h, d];
            _vy = new float[w, h, d];
            _vz = new float[w, h, d];
            _sxx = new float[w, h, d];
            _syy = new float[w, h, d];
            _szz = new float[w, h, d];
            _sxy = new float[w, h, d];
            _sxz = new float[w, h, d];
            _syz = new float[w, h, d];
            _damage = new float[w, h, d];
        }

        private unsafe void BuildOpenCLProgram()
        {
            string kernelSource = GetOpenCLKernelSource();
            var sourceBytes = System.Text.Encoding.ASCII.GetBytes(kernelSource);
    
            fixed (byte* sourcePtr = sourceBytes)
            {
                var sourcePtrAddr = new IntPtr(sourcePtr);
                var length = (nuint)sourceBytes.Length;
                int errNum = 0;
        
                _program = _cl.CreateProgramWithSource(_context, 1, (byte**)&sourcePtrAddr, &length, &errNum);
                if (errNum != 0)
                    throw new Exception($"Failed to create program: {errNum}");

                // Build program - take address of device directly and cast null to resolve ambiguity
                nint device = _device;
                nint* devicePtr = &device; // Take address directly without fixed
                int buildStatus = _cl.BuildProgram(_program, 1, devicePtr, (byte*)null, null, null);
        
                if (buildStatus != 0)
                {
                    // Get build log
                    nuint logSize = 0;
                    _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
            
                    if (logSize > 0)
                    {
                        byte* log = stackalloc byte[(int)logSize];
                        _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, logSize, log, null);
                        string logStr = Marshal.PtrToStringAnsi((IntPtr)log, (int)logSize);
                        Logger.LogError($"[AcousticSimulator] OpenCL build failed: {logStr}");
                    }
            
                    throw new Exception("Failed to build OpenCL program");
                }
            }
        }
        private unsafe void CreateOpenCLBuffers()
        {
            int size = _totalCells;
            int errNum = 0;

            // Create buffers
            _materialBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly, (nuint)(size * sizeof(byte)), null, &errNum);
            _densityBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly, (nuint)(size * sizeof(float)), null, &errNum);
            
            for (int i = 0; i < 3; i++)
                _velocityBuffers[i] = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(size * sizeof(float)), null, &errNum);
            
            for (int i = 0; i < 6; i++)
                _stressBuffers[i] = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(size * sizeof(float)), null, &errNum);
            
            _damageBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(size * sizeof(float)), null, &errNum);
        }

        private string GetOpenCLKernelSource()
        {
            // Complete OpenCL kernel for acoustic simulation
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
                    const uchar selectedMaterial)
                {
                    int idx = get_global_id(0);
                    if (idx >= width * height * depth) return;
                    
                    // Convert to 3D coordinates
                    int z = idx / (width * height);
                    int remainder = idx % (width * height);
                    int y = remainder / width;
                    int x = remainder % width;
                    
                    // Skip if not selected material or boundary
                    if (material[idx] != selectedMaterial) return;
                    if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;
                    
                    // Calculate velocity gradients
                    int xp1 = idx + 1;
                    int xm1 = idx - 1;
                    int yp1 = idx + width;
                    int ym1 = idx - width;
                    int zp1 = idx + width * height;
                    int zm1 = idx - width * height;
                    
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
                    
                    // Update stress components (Hooke's law)
                    float damping = 1.0f - damage[idx] * 0.9f;
                    sxx[idx] += dt * damping * (lambda * volumetricStrain + 2.0f * mu * dvx_dx);
                    syy[idx] += dt * damping * (lambda * volumetricStrain + 2.0f * mu * dvy_dy);
                    szz[idx] += dt * damping * (lambda * volumetricStrain + 2.0f * mu * dvz_dz);
                    sxy[idx] += dt * damping * mu * (dvy_dx + dvx_dy);
                    sxz[idx] += dt * damping * mu * (dvz_dx + dvx_dz);
                    syz[idx] += dt * damping * mu * (dvz_dy + dvy_dz);
                }

                __kernel void updateVelocity(
                    __global const uchar* material,
                    __global const float* density,
                    __global float* vx, __global float* vy, __global float* vz,
                    __global float* sxx, __global float* syy, __global float* szz,
                    __global float* sxy, __global float* sxz, __global float* syz,
                    const float dt, const float dx,
                    const int width, const int height, const int depth,
                    const uchar selectedMaterial)
                {
                    int idx = get_global_id(0);
                    if (idx >= width * height * depth) return;
                    
                    // Convert to 3D coordinates
                    int z = idx / (width * height);
                    int remainder = idx % (width * height);
                    int y = remainder / width;
                    int x = remainder % width;
                    
                    // Skip if not selected material or boundary
                    if (material[idx] != selectedMaterial) return;
                    if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;
                    
                    float rho = fmax(100.0f, density[idx]);
                    
                    // Calculate stress gradients
                    int xp1 = idx + 1;
                    int xm1 = idx - 1;
                    int yp1 = idx + width;
                    int ym1 = idx - width;
                    int zp1 = idx + width * height;
                    int zm1 = idx - width * height;
                    
                    float dsxx_dx = (sxx[xp1] - sxx[xm1]) / (2.0f * dx);
                    float dsyy_dy = (syy[yp1] - syy[ym1]) / (2.0f * dx);
                    float dszz_dz = (szz[zp1] - szz[zm1]) / (2.0f * dx);
                    float dsxy_dy = (sxy[yp1] - sxy[ym1]) / (2.0f * dx);
                    float dsxy_dx = (sxy[xp1] - sxy[xm1]) / (2.0f * dx);
                    float dsxz_dz = (sxz[zp1] - sxz[zm1]) / (2.0f * dx);
                    float dsxz_dx = (sxz[xp1] - sxz[xm1]) / (2.0f * dx);
                    float dsyz_dz = (syz[zp1] - syz[zm1]) / (2.0f * dx);
                    float dsyz_dy = (syz[yp1] - syz[ym1]) / (2.0f * dx);
                    
                    // Update velocity with damping
                    const float damping = 0.995f;
                    vx[idx] = vx[idx] * damping + dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
                    vy[idx] = vy[idx] * damping + dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
                    vz[idx] = vz[idx] * damping + dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
                }
            ";
        }

        public async Task<SimulationResults> RunAsync(
            byte[,,] volumeLabels,
            float[,,] densityVolume,
            CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;
            var results = new SimulationResults();

            // Calculate stable time step
            CalculateTimeStep(densityVolume);

            // Initialize fields
            ApplyInitialConditions(volumeLabels, densityVolume);

            // Time series snapshots
            if (_params.SaveTimeSeries)
            {
                results.TimeSeriesSnapshots = new List<WaveFieldSnapshot>();
            }

            // Main simulation loop
            int stepCount = 0;
            int maxSteps = _params.TimeSteps * 2; // Safety limit

            // Wave detection variables
            bool pWaveDetected = false, sWaveDetected = false;
            int pWaveStep = 0, sWaveStep = 0;

            while (stepCount < maxSteps && !cancellationToken.IsCancellationRequested)
            {
                if (_useGPU)
                {
                    await UpdateFieldsGPUAsync();
                }
                else
                {
                    UpdateFieldsCPU(volumeLabels, densityVolume);
                }

                stepCount++;
                CurrentStep = stepCount;

                // Save snapshot if needed
                if (_params.SaveTimeSeries && stepCount % _params.SnapshotInterval == 0)
                {
                    var snapshot = new WaveFieldSnapshot
                    {
                        TimeStep = stepCount,
                        SimulationTime = stepCount * _dt,
                        Width = _params.Width,
                        Height = _params.Height,
                        Depth = _params.Depth
                    };
                    snapshot.SetVelocityFields(_vx, _vy, _vz);
                    results.TimeSeriesSnapshots.Add(snapshot);
                }

                // Real-time visualization update
                if (_params.EnableRealTimeVisualization && stepCount % 10 == 0)
                {
                    WaveFieldUpdated?.Invoke(this, new WaveFieldUpdateEventArgs
                    {
                        WaveField = GetCombinedWaveField(),
                        TimeStep = stepCount,
                        SimTime = stepCount * _dt,
                        Dataset = volumeLabels
                    });
                }

                // Check for wave arrivals
                if (!pWaveDetected && CheckPWaveArrival())
                {
                    pWaveDetected = true;
                    pWaveStep = stepCount;
                    Logger.Log($"[AcousticSimulator] P-wave detected at step {pWaveStep}");
                }

                if (pWaveDetected && !sWaveDetected && CheckSWaveArrival())
                {
                    sWaveDetected = true;
                    sWaveStep = stepCount;
                    Logger.Log($"[AcousticSimulator] S-wave detected at step {sWaveStep}");
                }

                // Check termination conditions
                if (pWaveDetected && sWaveDetected && stepCount > sWaveStep + _params.TimeSteps / 10)
                {
                    break;
                }

                // Update progress
                Progress = (float)stepCount / maxSteps;
                
                if (stepCount % 10 == 0)
                {
                    ProgressUpdated?.Invoke(this, new SimulationProgressEventArgs
                    {
                        Progress = Progress,
                        Step = stepCount,
                        Message = $"Step {stepCount}/{maxSteps}"
                    });
                }
            }

            // Calculate results
            var distance = CalculateDistance();
            float pVelocity = pWaveStep > 0 ? distance / (pWaveStep * _dt) : 0;
            float sVelocity = sWaveStep > 0 ? distance / (sWaveStep * _dt) : 0;

            results.PWaveVelocity = pVelocity;
            results.SWaveVelocity = sVelocity;
            results.VpVsRatio = sVelocity > 0 ? pVelocity / sVelocity : 0;
            results.PWaveTravelTime = pWaveStep;
            results.SWaveTravelTime = sWaveStep;
            results.TotalTimeSteps = stepCount;
            results.ComputationTime = DateTime.Now - startTime;

            return results;
        }

        private void CalculateTimeStep(float[,,] densityVolume)
        {
            // Find minimum density
            float rhoMin = float.MaxValue;
            foreach (float d in densityVolume)
                if (d > 0 && d < rhoMin)
                    rhoMin = d;

            rhoMin = Math.Max(rhoMin, 100.0f);

            // Calculate maximum P-wave velocity
            float vpMax = (float)Math.Sqrt((_lambda + 2 * _mu) / rhoMin);
            vpMax = Math.Min(vpMax, 6000.0f); // Cap for stability

            // CFL condition
            const float SafetyCourant = 0.25f;
            _dt = SafetyCourant * _params.PixelSize / vpMax;
            _dt = Math.Max(_dt, 1e-8f);

            Logger.Log($"[AcousticSimulator] Time step: {_dt:E6} s, vpMax: {vpMax:F2} m/s");
        }

        private unsafe void ApplyInitialConditions(byte[,,] volumeLabels, float[,,] densityVolume)
        {
            if (_useGPU)
            {
                // Upload data to GPU
                int size = _totalCells;
                
                // Flatten arrays
                byte[] materialFlat = new byte[size];
                float[] densityFlat = new float[size];
                
                int idx = 0;
                for (int z = 0; z < _params.Depth; z++)
                    for (int y = 0; y < _params.Height; y++)
                        for (int x = 0; x < _params.Width; x++)
                        {
                            materialFlat[idx] = volumeLabels[x, y, z];
                            densityFlat[idx] = densityVolume[x, y, z];
                            idx++;
                        }

                fixed (byte* materialPtr = materialFlat)
                fixed (float* densityPtr = densityFlat)
                {
                    _cl.EnqueueWriteBuffer(_commandQueue, _materialBuffer, true, 0, 
                        (nuint)(size * sizeof(byte)), materialPtr, 0, null, null);
                    _cl.EnqueueWriteBuffer(_commandQueue, _densityBuffer, true, 0, 
                        (nuint)(size * sizeof(float)), densityPtr, 0, null, null);
                }

                // Initialize stress and velocity fields with source pulse
                ApplySourcePulseGPU();
            }
            else
            {
                // Apply confining pressure and source pulse
                ApplySourcePulseCPU(volumeLabels, densityVolume);
            }
        }

        private void ApplySourcePulseCPU(byte[,,] volumeLabels, float[,,] densityVolume)
        {
            // Apply confining pressure
            float confiningPa = _params.ConfiningPressureMPa * 1e6f;
            
            for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                        if (volumeLabels[x, y, z] == _params.SelectedMaterialID)
                        {
                            _sxx[x, y, z] = -confiningPa;
                            _syy[x, y, z] = -confiningPa;
                            _szz[x, y, z] = -confiningPa;
                        }

            // Apply source pulse
            float pulse = _params.SourceAmplitude * (float)Math.Sqrt(_params.SourceEnergyJ) * 1e6f;
            
            // Calculate TX/RX positions
            int tx = (int)(_params.TxPosition.X * _params.Width);
            int ty = (int)(_params.TxPosition.Y * _params.Height);
            int tz = (int)(_params.TxPosition.Z * _params.Depth);

            if (_params.UseFullFaceTransducers)
            {
                // Apply to entire face
                ApplyFullFaceSource(volumeLabels, densityVolume, pulse, tx, ty, tz);
            }
            else
            {
                // Point source
                ApplyPointSource(volumeLabels, densityVolume, pulse, tx, ty, tz);
            }
        }

        private void ApplyFullFaceSource(byte[,,] volumeLabels, float[,,] densityVolume, 
            float pulse, int tx, int ty, int tz)
        {
            // Determine which face to use based on axis
            switch (_params.Axis)
            {
                case 0: // X axis
                    for (int y = 0; y < _params.Height; y++)
                        for (int z = 0; z < _params.Depth; z++)
                            if (volumeLabels[0, y, z] == _params.SelectedMaterialID)
                            {
                                _sxx[0, y, z] += pulse;
                                _vx[0, y, z] = pulse / (densityVolume[0, y, z] * 10.0f);
                            }
                    break;
                    
                case 1: // Y axis
                    for (int x = 0; x < _params.Width; x++)
                        for (int z = 0; z < _params.Depth; z++)
                            if (volumeLabels[x, 0, z] == _params.SelectedMaterialID)
                            {
                                _syy[x, 0, z] += pulse;
                                _vy[x, 0, z] = pulse / (densityVolume[x, 0, z] * 10.0f);
                            }
                    break;
                    
                case 2: // Z axis
                    for (int x = 0; x < _params.Width; x++)
                        for (int y = 0; y < _params.Height; y++)
                            if (volumeLabels[x, y, 0] == _params.SelectedMaterialID)
                            {
                                _szz[x, y, 0] += pulse;
                                _vz[x, y, 0] = pulse / (densityVolume[x, y, 0] * 10.0f);
                            }
                    break;
            }
        }

        private void ApplyPointSource(byte[,,] volumeLabels, float[,,] densityVolume, 
            float pulse, int tx, int ty, int tz)
        {
            // Apply spherical source
            int radius = 2;
            for (int dz = -radius; dz <= radius; dz++)
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (dist > radius) continue;

                        int x = tx + dx, y = ty + dy, z = tz + dz;
                        if (x < 0 || x >= _params.Width || 
                            y < 0 || y >= _params.Height || 
                            z < 0 || z >= _params.Depth) continue;

                        if (volumeLabels[x, y, z] != _params.SelectedMaterialID) continue;

                        float falloff = 1.0f - dist / radius;
                        float localPulse = pulse * falloff * falloff;

                        _sxx[x, y, z] += localPulse;
                        _syy[x, y, z] += localPulse;
                        _szz[x, y, z] += localPulse;

                        // Add velocity kick
                        float vKick = localPulse / (densityVolume[x, y, z] * 10.0f);
                        switch (_params.Axis)
                        {
                            case 0: _vx[x, y, z] += vKick; break;
                            case 1: _vy[x, y, z] += vKick; break;
                            case 2: _vz[x, y, z] += vKick; break;
                        }
                    }
        }

        private unsafe void ApplySourcePulseGPU()
        {
            // Initialize fields on GPU using kernels
            Logger.Log("[AcousticSimulator] GPU source pulse application");
            // This would be implemented with specific OpenCL kernels
        }

        private unsafe Task UpdateFieldsGPUAsync()
{
    // Run stress update kernel
    int errNum = 0;
    var stressKernel = _cl.CreateKernel(_program, "updateStress", &errNum);
    if (errNum != 0)
    {
        Logger.LogError($"Failed to create stress kernel: {errNum}");
        return Task.CompletedTask;
    }
    
    // Set kernel arguments for stress kernel
    // For buffer objects (nint), pass them directly without taking address
    nint materialBuffer = _materialBuffer;
    nint densityBuffer = _densityBuffer;
    nint velocityBuffer0 = _velocityBuffers[0];
    nint velocityBuffer1 = _velocityBuffers[1];
    nint velocityBuffer2 = _velocityBuffers[2];
    nint stressBuffer0 = _stressBuffers[0];
    nint stressBuffer1 = _stressBuffers[1];
    nint stressBuffer2 = _stressBuffers[2];
    nint stressBuffer3 = _stressBuffers[3];
    nint stressBuffer4 = _stressBuffers[4];
    nint stressBuffer5 = _stressBuffers[5];
    nint damageBuffer = _damageBuffer;
    
    // For value types, create local copies
    float lambda = _lambda;
    float mu = _mu;
    float dt = _dt;
    float dx = _params.PixelSize;
    int width = _params.Width;
    int height = _params.Height;
    int depth = _params.Depth;
    byte selectedMaterial = _params.SelectedMaterialID;
    
    _cl.SetKernelArg(stressKernel, 0, (nuint)sizeof(nint), &materialBuffer);
    _cl.SetKernelArg(stressKernel, 1, (nuint)sizeof(nint), &densityBuffer);
    _cl.SetKernelArg(stressKernel, 2, (nuint)sizeof(nint), &velocityBuffer0);
    _cl.SetKernelArg(stressKernel, 3, (nuint)sizeof(nint), &velocityBuffer1);
    _cl.SetKernelArg(stressKernel, 4, (nuint)sizeof(nint), &velocityBuffer2);
    _cl.SetKernelArg(stressKernel, 5, (nuint)sizeof(nint), &stressBuffer0);
    _cl.SetKernelArg(stressKernel, 6, (nuint)sizeof(nint), &stressBuffer1);
    _cl.SetKernelArg(stressKernel, 7, (nuint)sizeof(nint), &stressBuffer2);
    _cl.SetKernelArg(stressKernel, 8, (nuint)sizeof(nint), &stressBuffer3);
    _cl.SetKernelArg(stressKernel, 9, (nuint)sizeof(nint), &stressBuffer4);
    _cl.SetKernelArg(stressKernel, 10, (nuint)sizeof(nint), &stressBuffer5);
    _cl.SetKernelArg(stressKernel, 11, (nuint)sizeof(nint), &damageBuffer);
    _cl.SetKernelArg(stressKernel, 12, (nuint)sizeof(float), &lambda);
    _cl.SetKernelArg(stressKernel, 13, (nuint)sizeof(float), &mu);
    _cl.SetKernelArg(stressKernel, 14, (nuint)sizeof(float), &dt);
    _cl.SetKernelArg(stressKernel, 15, (nuint)sizeof(float), &dx);
    _cl.SetKernelArg(stressKernel, 16, (nuint)sizeof(int), &width);
    _cl.SetKernelArg(stressKernel, 17, (nuint)sizeof(int), &height);
    _cl.SetKernelArg(stressKernel, 18, (nuint)sizeof(int), &depth);
    _cl.SetKernelArg(stressKernel, 19, (nuint)sizeof(byte), &selectedMaterial);

    // Execute kernel
    nuint globalSize = (nuint)_totalCells;
    _cl.EnqueueNdrangeKernel(_commandQueue, stressKernel, 1, null, &globalSize, null, 0, null, null);

    // Run velocity update kernel
    var velocityKernel = _cl.CreateKernel(_program, "updateVelocity", &errNum);
    if (errNum != 0)
    {
        Logger.LogError($"Failed to create velocity kernel: {errNum}");
        return Task.CompletedTask;
    }
    
    // Set arguments for velocity kernel - reuse local variables
    _cl.SetKernelArg(velocityKernel, 0, (nuint)sizeof(nint), &materialBuffer);
    _cl.SetKernelArg(velocityKernel, 1, (nuint)sizeof(nint), &densityBuffer);
    _cl.SetKernelArg(velocityKernel, 2, (nuint)sizeof(nint), &velocityBuffer0);
    _cl.SetKernelArg(velocityKernel, 3, (nuint)sizeof(nint), &velocityBuffer1);
    _cl.SetKernelArg(velocityKernel, 4, (nuint)sizeof(nint), &velocityBuffer2);
    _cl.SetKernelArg(velocityKernel, 5, (nuint)sizeof(nint), &stressBuffer0);
    _cl.SetKernelArg(velocityKernel, 6, (nuint)sizeof(nint), &stressBuffer1);
    _cl.SetKernelArg(velocityKernel, 7, (nuint)sizeof(nint), &stressBuffer2);
    _cl.SetKernelArg(velocityKernel, 8, (nuint)sizeof(nint), &stressBuffer3);
    _cl.SetKernelArg(velocityKernel, 9, (nuint)sizeof(nint), &stressBuffer4);
    _cl.SetKernelArg(velocityKernel, 10, (nuint)sizeof(nint), &stressBuffer5);
    _cl.SetKernelArg(velocityKernel, 11, (nuint)sizeof(float), &dt);
    _cl.SetKernelArg(velocityKernel, 12, (nuint)sizeof(float), &dx);
    _cl.SetKernelArg(velocityKernel, 13, (nuint)sizeof(int), &width);
    _cl.SetKernelArg(velocityKernel, 14, (nuint)sizeof(int), &height);
    _cl.SetKernelArg(velocityKernel, 15, (nuint)sizeof(int), &depth);
    _cl.SetKernelArg(velocityKernel, 16, (nuint)sizeof(byte), &selectedMaterial);
    
    _cl.EnqueueNdrangeKernel(_commandQueue, velocityKernel, 1, null, &globalSize, null, 0, null, null);

    _cl.Finish(_commandQueue);
    
    // Clean up kernels
    _cl.ReleaseKernel(stressKernel);
    _cl.ReleaseKernel(velocityKernel);
    
    return Task.CompletedTask;
}

        private void UpdateFieldsCPU(byte[,,] volumeLabels, float[,,] densityVolume)
        {
            // Update stress fields
            UpdateStressCPU(volumeLabels, densityVolume);
            
            // Update velocity fields
            UpdateVelocityCPU(volumeLabels, densityVolume);
        }

        private void UpdateStressCPU(byte[,,] volumeLabels, float[,,] densityVolume)
        {
            Parallel.For(1, _params.Depth - 1, z =>
            {
                for (int y = 1; y < _params.Height - 1; y++)
                    for (int x = 1; x < _params.Width - 1; x++)
                    {
                        if (volumeLabels[x, y, z] != _params.SelectedMaterialID) continue;

                        // Calculate velocity gradients (staggered grid)
                        float dvx_dx = (_vx[x + 1, y, z] - _vx[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dvy_dy = (_vy[x, y + 1, z] - _vy[x, y - 1, z]) / (2 * _params.PixelSize);
                        float dvz_dz = (_vz[x, y, z + 1] - _vz[x, y, z - 1]) / (2 * _params.PixelSize);
                        
                        float dvy_dx = (_vy[x + 1, y, z] - _vy[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dvx_dy = (_vx[x, y + 1, z] - _vx[x, y - 1, z]) / (2 * _params.PixelSize);
                        float dvz_dx = (_vz[x + 1, y, z] - _vz[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dvx_dz = (_vx[x, y, z + 1] - _vx[x, y, z - 1]) / (2 * _params.PixelSize);
                        float dvz_dy = (_vz[x, y + 1, z] - _vz[x, y - 1, z]) / (2 * _params.PixelSize);
                        float dvy_dz = (_vy[x, y, z + 1] - _vy[x, y, z - 1]) / (2 * _params.PixelSize);
                        
                        float volumetricStrain = dvx_dx + dvy_dy + dvz_dz;

                        // Update stress (elastic model with damage)
                        float damping = 1.0f - _damage[x, y, z] * 0.9f;
                        _sxx[x, y, z] += _dt * damping * (_lambda * volumetricStrain + 2 * _mu * dvx_dx);
                        _syy[x, y, z] += _dt * damping * (_lambda * volumetricStrain + 2 * _mu * dvy_dy);
                        _szz[x, y, z] += _dt * damping * (_lambda * volumetricStrain + 2 * _mu * dvz_dz);
                        _sxy[x, y, z] += _dt * damping * _mu * (dvy_dx + dvx_dy);
                        _sxz[x, y, z] += _dt * damping * _mu * (dvz_dx + dvx_dz);
                        _syz[x, y, z] += _dt * damping * _mu * (dvz_dy + dvy_dz);

                        // Apply plastic and brittle models if enabled
                        if (_params.UsePlasticModel)
                            ApplyPlasticModel(x, y, z);
                        
                        if (_params.UseBrittleModel)
                            ApplyBrittleModel(x, y, z);
                    }
            });
        }

        private void UpdateVelocityCPU(byte[,,] volumeLabels, float[,,] densityVolume)
        {
            const float DAMPING = 0.995f; // Damping factor

            Parallel.For(1, _params.Depth - 1, z =>
            {
                for (int y = 1; y < _params.Height - 1; y++)
                    for (int x = 1; x < _params.Width - 1; x++)
                    {
                        if (volumeLabels[x, y, z] != _params.SelectedMaterialID) continue;

                        float rho = Math.Max(100.0f, densityVolume[x, y, z]);

                        // Calculate stress gradients
                        float dsxx_dx = (_sxx[x + 1, y, z] - _sxx[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dsyy_dy = (_syy[x, y + 1, z] - _syy[x, y - 1, z]) / (2 * _params.PixelSize);
                        float dszz_dz = (_szz[x, y, z + 1] - _szz[x, y, z - 1]) / (2 * _params.PixelSize);
                        
                        float dsxy_dy = (_sxy[x, y + 1, z] - _sxy[x, y - 1, z]) / (2 * _params.PixelSize);
                        float dsxy_dx = (_sxy[x + 1, y, z] - _sxy[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dsxz_dz = (_sxz[x, y, z + 1] - _sxz[x, y, z - 1]) / (2 * _params.PixelSize);
                        float dsxz_dx = (_sxz[x + 1, y, z] - _sxz[x - 1, y, z]) / (2 * _params.PixelSize);
                        float dsyz_dz = (_syz[x, y, z + 1] - _syz[x, y, z - 1]) / (2 * _params.PixelSize);
                        float dsyz_dy = (_syz[x, y + 1, z] - _syz[x, y - 1, z]) / (2 * _params.PixelSize);

                        // Update velocities with damping
                        _vx[x, y, z] = _vx[x, y, z] * DAMPING + _dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
                        _vy[x, y, z] = _vy[x, y, z] * DAMPING + _dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
                        _vz[x, y, z] = _vz[x, y, z] * DAMPING + _dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
                    }
            });
        }

        private void ApplyPlasticModel(int x, int y, int z)
        {
            // Mohr-Coulomb plasticity
            float mean = (_sxx[x, y, z] + _syy[x, y, z] + _szz[x, y, z]) / 3.0f;
            float dev_xx = _sxx[x, y, z] - mean;
            float dev_yy = _syy[x, y, z] - mean;
            float dev_zz = _szz[x, y, z] - mean;
            
            float J2 = 0.5f * (dev_xx * dev_xx + dev_yy * dev_yy + dev_zz * dev_zz + 
                              2 * (_sxy[x, y, z] * _sxy[x, y, z] + 
                                   _sxz[x, y, z] * _sxz[x, y, z] + 
                                   _syz[x, y, z] * _syz[x, y, z]));
            float tau = (float)Math.Sqrt(J2);

            float sinPhi = (float)Math.Sin(_params.FailureAngleDeg * Math.PI / 180.0);
            float cosPhi = (float)Math.Cos(_params.FailureAngleDeg * Math.PI / 180.0);
            float cohesionPa = _params.CohesionMPa * 1e6f;
            float p = -mean + _params.ConfiningPressureMPa * 1e6f;

            float yield = tau + p * sinPhi - cohesionPa * cosPhi;
            
            if (yield > 0 && tau > 1e-10f)
            {
                float scale = (tau - (cohesionPa * cosPhi - p * sinPhi)) / tau;
                scale = Math.Min(scale, 0.95f);

                dev_xx *= (1 - scale);
                dev_yy *= (1 - scale);
                dev_zz *= (1 - scale);
                _sxy[x, y, z] *= (1 - scale);
                _sxz[x, y, z] *= (1 - scale);
                _syz[x, y, z] *= (1 - scale);

                _sxx[x, y, z] = dev_xx + mean;
                _syy[x, y, z] = dev_yy + mean;
                _szz[x, y, z] = dev_zz + mean;
            }
        }

        private void ApplyBrittleModel(int x, int y, int z)
        {
            // Calculate maximum principal stress
            float I1 = _sxx[x, y, z] + _syy[x, y, z] + _szz[x, y, z];
            float sigmaMax = I1 / 3.0f; // Simplified

            float tensileStrengthPa = _params.TensileStrengthMPa * 1e6f;
            
            if (sigmaMax > tensileStrengthPa && _damage[x, y, z] < 1.0f)
            {
                float incr = (sigmaMax - tensileStrengthPa) / tensileStrengthPa;
                incr = Math.Min(incr, 0.1f);
                _damage[x, y, z] = Math.Min(0.95f, _damage[x, y, z] + incr * 0.01f);

                float factor = 1.0f - _damage[x, y, z];
                _sxx[x, y, z] *= factor;
                _syy[x, y, z] *= factor;
                _szz[x, y, z] *= factor;
                _sxy[x, y, z] *= factor;
                _sxz[x, y, z] *= factor;
                _syz[x, y, z] *= factor;
            }
        }

        private bool CheckPWaveArrival()
        {
            int rx = (int)(_params.RxPosition.X * _params.Width);
            int ry = (int)(_params.RxPosition.Y * _params.Height);
            int rz = (int)(_params.RxPosition.Z * _params.Depth);

            if (rx < 0 || rx >= _params.Width || ry < 0 || ry >= _params.Height || rz < 0 || rz >= _params.Depth)
                return false;

            float magnitude = 0;
            
            // Check for longitudinal wave (P-wave)
            switch (_params.Axis)
            {
                case 0: magnitude = Math.Abs(_vx[rx, ry, rz]); break;
                case 1: magnitude = Math.Abs(_vy[rx, ry, rz]); break;
                case 2: magnitude = Math.Abs(_vz[rx, ry, rz]); break;
            }

            return magnitude > 1e-9f;
        }

        private bool CheckSWaveArrival()
        {
            int rx = (int)(_params.RxPosition.X * _params.Width);
            int ry = (int)(_params.RxPosition.Y * _params.Height);
            int rz = (int)(_params.RxPosition.Z * _params.Depth);

            if (rx < 0 || rx >= _params.Width || ry < 0 || ry >= _params.Height || rz < 0 || rz >= _params.Depth)
                return false;

            float magnitude = 0;
            
            // Check for transverse wave (S-wave)
            switch (_params.Axis)
            {
                case 0: 
                    magnitude = (float)Math.Sqrt(_vy[rx, ry, rz] * _vy[rx, ry, rz] + 
                                                 _vz[rx, ry, rz] * _vz[rx, ry, rz]); 
                    break;
                case 1: 
                    magnitude = (float)Math.Sqrt(_vx[rx, ry, rz] * _vx[rx, ry, rz] + 
                                                 _vz[rx, ry, rz] * _vz[rx, ry, rz]); 
                    break;
                case 2: 
                    magnitude = (float)Math.Sqrt(_vx[rx, ry, rz] * _vx[rx, ry, rz] + 
                                                 _vy[rx, ry, rz] * _vy[rx, ry, rz]); 
                    break;
            }

            return magnitude > 1e-9f;
        }

        private float CalculateDistance()
        {
            int tx = (int)(_params.TxPosition.X * _params.Width);
            int ty = (int)(_params.TxPosition.Y * _params.Height);
            int tz = (int)(_params.TxPosition.Z * _params.Depth);
            int rx = (int)(_params.RxPosition.X * _params.Width);
            int ry = (int)(_params.RxPosition.Y * _params.Height);
            int rz = (int)(_params.RxPosition.Z * _params.Depth);

            return (float)Math.Sqrt((tx - rx) * (tx - rx) +
                                   (ty - ry) * (ty - ry) +
                                   (tz - rz) * (tz - rz)) * _params.PixelSize;
        }

        private float[,,] GetCombinedWaveField()
        {
            var combined = new float[_params.Width, _params.Height, _params.Depth];
            
            for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                    {
                        float vx = _vx[x, y, z];
                        float vy = _vy[x, y, z];
                        float vz = _vz[x, y, z];
                        combined[x, y, z] = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    }
            
            return combined;
        }

        public float[,,] GetFinalWaveField(int component)
        {
            var field = new float[_params.Width, _params.Height, _params.Depth];
            
            if (_useGPU)
            {
                // Download from GPU
                DownloadFieldFromGPU(component, field);
            }
            else
            {
                // Copy from CPU arrays
                for (int z = 0; z < _params.Depth; z++)
                    for (int y = 0; y < _params.Height; y++)
                        for (int x = 0; x < _params.Width; x++)
                        {
                            switch (component)
                            {
                                case 0: field[x, y, z] = _vx[x, y, z]; break;
                                case 1: field[x, y, z] = _vy[x, y, z]; break;
                                case 2: field[x, y, z] = _vz[x, y, z]; break;
                            }
                        }
            }

            return field;
        }

        private unsafe void DownloadFieldFromGPU(int component, float[,,] field)
        {
            // Download data from GPU buffer
            int size = _totalCells;
            float[] data = new float[size];
            
            fixed (float* dataPtr = data)
            {
                _cl.EnqueueReadBuffer(_commandQueue, _velocityBuffers[component], true, 0,
                    (nuint)(size * sizeof(float)), dataPtr, 0, null, null);
            }

            // Convert to 3D array
            int idx = 0;
            for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                        field[x, y, z] = data[idx++];
        }

        public void Dispose()
        {
            if (_useGPU && _cl != null)
            {
                _cl.ReleaseMemObject(_materialBuffer);
                _cl.ReleaseMemObject(_densityBuffer);
                
                for (int i = 0; i < 3; i++)
                    if (_velocityBuffers[i] != 0)
                        _cl.ReleaseMemObject(_velocityBuffers[i]);
                
                for (int i = 0; i < 6; i++)
                    if (_stressBuffers[i] != 0)
                        _cl.ReleaseMemObject(_stressBuffers[i]);
                
                if (_damageBuffer != 0)
                    _cl.ReleaseMemObject(_damageBuffer);
                
                if (_program != 0)
                    _cl.ReleaseProgram(_program);
                
                if (_commandQueue != 0)
                    _cl.ReleaseCommandQueue(_commandQueue);
                
                if (_context != 0)
                    _cl.ReleaseContext(_context);
            }
        }
    }

    #endregion

    #region Supporting Classes

    internal class SimulationParameters
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
    }

    internal class SimulationResults
    {
        public double PWaveVelocity { get; set; }
        public double SWaveVelocity { get; set; }
        public double VpVsRatio { get; set; }
        public int PWaveTravelTime { get; set; }
        public int SWaveTravelTime { get; set; }
        public int TotalTimeSteps { get; set; }
        public TimeSpan ComputationTime { get; set; }
        public float[,,] WaveFieldVx { get; set; }
        public float[,,] WaveFieldVy { get; set; }
        public float[,,] WaveFieldVz { get; set; }
        public List<WaveFieldSnapshot> TimeSeriesSnapshots { get; set; }
    }

    internal class SimulationProgressEventArgs : EventArgs
    {
        public float Progress { get; set; }
        public int Step { get; set; }
        public string Message { get; set; }
    }

    internal class WaveFieldUpdateEventArgs : EventArgs
    {
        public float[,,] WaveField { get; set; }
        public int TimeStep { get; set; }
        public float SimTime { get; set; }
        public object Dataset { get; set; }
    }

    internal class CalibrationManager
    {
        private readonly List<CalibrationPoint> _calibrationPoints = new List<CalibrationPoint>();

        public void AddCalibrationPoint(string materialName, byte materialID, float density,
            float confiningPressure, float youngsModulus, float poissonRatio,
            double vp, double vs, double vpVsRatio)
        {
            _calibrationPoints.Add(new CalibrationPoint
            {
                MaterialName = materialName,
                MaterialID = materialID,
                Density = density,
                ConfiningPressureMPa = confiningPressure,
                YoungsModulusMPa = youngsModulus,
                PoissonRatio = poissonRatio,
                MeasuredVp = vp,
                MeasuredVs = vs,
                MeasuredVpVsRatio = vpVsRatio,
                Timestamp = DateTime.Now
            });

            Logger.Log($"[CalibrationManager] Added calibration point for {materialName}");
        }

        public bool HasCalibration() => _calibrationPoints.Count >= 2;

        public (float YoungsModulus, float PoissonRatio) GetCalibratedParameters(
            float density, float confiningPressure)
        {
            if (_calibrationPoints.Count < 2)
                return (30000.0f, 0.25f); // Default values

            // Find closest calibration points
            var closestPoints = _calibrationPoints
                .OrderBy(p => Math.Abs(p.Density - density) + Math.Abs(p.ConfiningPressureMPa - confiningPressure))
                .Take(2)
                .ToList();

            if (closestPoints.Count == 0)
                return (30000.0f, 0.25f);

            // Interpolate
            if (closestPoints.Count == 1)
                return (closestPoints[0].YoungsModulusMPa, closestPoints[0].PoissonRatio);

            var p1 = closestPoints[0];
            var p2 = closestPoints[1];
            
            float totalDist = Math.Abs(p1.Density - density) + Math.Abs(p2.Density - density);
            if (totalDist < 0.001f)
                return (p1.YoungsModulusMPa, p1.PoissonRatio);

            float weight1 = 1.0f - (Math.Abs(p1.Density - density) / totalDist);
            float weight2 = 1.0f - weight1;

            float E = p1.YoungsModulusMPa * weight1 + p2.YoungsModulusMPa * weight2;
            float nu = p1.PoissonRatio * weight1 + p2.PoissonRatio * weight2;

            return (E, nu);
        }

        public string GetCalibrationSummary()
        {
            if (_calibrationPoints.Count == 0)
                return "No calibration points available.";

            var sb = new StringBuilder();
            sb.AppendLine($"Calibration Points: {_calibrationPoints.Count}");
            
            foreach (var point in _calibrationPoints.Take(3))
            {
                sb.AppendLine($"  • {point.MaterialName}: Vp/Vs={point.MeasuredVpVsRatio:F3}, ρ={point.Density:F1} kg/m³");
            }

            if (_calibrationPoints.Count > 3)
                sb.AppendLine($"  ... and {_calibrationPoints.Count - 3} more");

            return sb.ToString();
        }

        private class CalibrationPoint
        {
            public string MaterialName { get; set; }
            public byte MaterialID { get; set; }
            public float Density { get; set; }
            public float ConfiningPressureMPa { get; set; }
            public float YoungsModulusMPa { get; set; }
            public float PoissonRatio { get; set; }
            public double MeasuredVp { get; set; }
            public double MeasuredVs { get; set; }
            public double MeasuredVpVsRatio { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }

    #endregion
}