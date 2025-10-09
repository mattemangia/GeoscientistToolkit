// GeoscientistToolkit/Analysis/AcousticSimulation/AcousticSimulationUI.cs

using System.Collections.Concurrent;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

/// <summary>
///     Static class to manage the state of interactive transducer placement across different viewers.
/// </summary>
internal static class AcousticIntegration
{
    private static Dataset _targetDataset;
    private static string _placingWhich; // "TX" or "RX"
    private static BoundingBox? _activeExtent;
    public static bool ShouldDrawExtent { get; private set; }

    public static bool IsPlacing { get; private set; }
    public static Vector3 TxPosition { get; private set; }
    public static Vector3 RxPosition { get; private set; }
    public static event Action OnPositionsChanged;

    /// <summary>
    ///     Configures the global state for placement and visualization overlays.
    /// </summary>
    public static void Configure(BoundingBox? extent, bool drawExtent)
    {
        _activeExtent = extent;
        ShouldDrawExtent = drawExtent;
    }

    public static BoundingBox? GetActiveExtent()
    {
        return _activeExtent;
    }

    /// <summary>
    ///     Updates the current positions for drawing markers without changing the placement state.
    /// </summary>
    public static void UpdateMarkerPositionsForDrawing(Dataset target, Vector3 currentTx, Vector3 currentRx)
    {
        _targetDataset = target;
        TxPosition = currentTx;
        RxPosition = currentRx;
    }

    /// <summary>
    ///     Initiates an interactive placement session for either the TX or RX.
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

    public static bool IsPlacingFor(Dataset d)
    {
        return IsPlacing && _targetDataset == d;
    }

    public static bool IsActiveFor(Dataset d)
    {
        return _targetDataset == d;
    }

    public static string GetPlacingTarget()
    {
        return _placingWhich;
    }

    public static void UpdatePosition(Vector3 newNormalizedPos)
    {
        if (!IsPlacing) return;

        var finalPos = newNormalizedPos;

        // If there's an active extent, clamp the new position to it.
        if (_activeExtent.HasValue && _targetDataset is CtImageStackDataset ctDataset)
        {
            var extent = _activeExtent.Value;

            var posVoxel = new Vector3(
                newNormalizedPos.X * ctDataset.Width,
                newNormalizedPos.Y * ctDataset.Height,
                newNormalizedPos.Z * ctDataset.Depth
            );

            posVoxel.X = Math.Clamp(posVoxel.X, extent.Min.X, extent.Max.X);
            posVoxel.Y = Math.Clamp(posVoxel.Y, extent.Min.Y, extent.Max.Y);
            posVoxel.Z = Math.Clamp(posVoxel.Z, extent.Min.Z, extent.Max.Z);

            finalPos = new Vector3(
                posVoxel.X / ctDataset.Width,
                posVoxel.Y / ctDataset.Height,
                posVoxel.Z / ctDataset.Depth
            );
        }

        if (_placingWhich == "TX")
            TxPosition = finalPos;
        else if (_placingWhich == "RX") // Explicitly check for RX
            RxPosition = finalPos;

        OnPositionsChanged?.Invoke();
    }
}

/// <summary>
///     Main UI panel for controlling and displaying acoustic simulations.
/// </summary>
public class AcousticSimulationUI : IDisposable
{
    private readonly ProgressBarDialog _autoPlaceDialog; // ADDED: Progress dialog for auto-placement
    private readonly UnifiedCalibrationManager _calibrationManager;
    private readonly AcousticExportManager _exportManager;
    private readonly ProgressBarDialog _extentCalculationDialog;
    private readonly ImGuiFileDialog _offloadDirectoryDialog;
    private readonly HashSet<byte> _selectedMaterialIDs = new();

    // --- NEW: Tomography viewer ---
    private readonly RealTimeTomographyViewer _tomographyViewer;
    private float _artificialDampingFactor = 0.2f;
    private bool _autoCalibrate;

    // --- IMPROVEMENT: Detailed simulation state tracking ---
    private bool _autoCropToSelection = true;
    private bool _autoUpdateTomography;
    private CancellationTokenSource _cancellationTokenSource;
    private int _chunkSizeMB = 512;
    private float _cohesion = 5.0f;
    private float _confiningPressure = 1.0f;
    private CtImageStackDataset _currentDataset;
    private SimulationState _currentState = SimulationState.Idle;

    // UI State
    private bool _enableMultiMaterialSelection;
    private bool _enableOffloading = true;

    // Real-time visualization support
    private bool _enableRealTimeVisualization;
    private int _estimatedTimeSteps;
    private float _failureAngle = 30.0f;
    private bool _isCalculatingExtent;
    private bool _isSimulating;
    private SimulationResults _lastResults;
    private DateTime _lastTomographyUpdate = DateTime.MinValue;
    private DateTime _lastVisualizationUpdate = DateTime.MinValue;
    private SimulationResults _liveResultsForTomography; // A full-sized buffer for tomography
    private string _offloadDirectory;
    private SimulationParameters _parameters;
    private float _poissonRatio = 0.25f;
    private bool _preparationComplete;
    private float _preparationProgress;
    private string _preparationStatus = "";
    private byte[] _realTimeVisualizationMask; // A full-sized buffer for progressive updates
    private Vector3 _rxPosition = new(1, 0.5f, 0.5f);
    private bool _saveTimeSeries;
    private int _selectedAxisIndex;
    private bool _showTomographyWindow;
    private BoundingBox? _simulationExtent;
    private DateTime _simulationStartTime = DateTime.MinValue;
    private ChunkedAcousticSimulator _simulator;
    private int _snapshotInterval = 10;
    private int _sourceAmplitude = 100;
    private float _sourceEnergy = 1.0f;
    private float _sourceFrequency = 500.0f;
    private int _timeSteps = 1000;
    private bool _timeStepsDirty = true;
    private Vector3 _txPosition = new(0, 0.5f, 0.5f);
    private bool _useBrittle;

    // Memory management
    private bool _useChunkedProcessing = true;
    private bool _useElastic = true;
    private bool _useFullFaceTransducers;
    private bool _useGPU = true;
    private bool _usePlastic;
    private bool _useRickerWavelet = true;
    private float _visualizationUpdateInterval = 0.1f;
    private float _youngsModulus = 30000.0f;


    public AcousticSimulationUI()
    {
        _parameters = new SimulationParameters();
        _calibrationManager = new UnifiedCalibrationManager();
        _exportManager = new AcousticExportManager();
        _tomographyViewer = new RealTimeTomographyViewer(); // Initialize the new viewer
        _offloadDirectory = Path.Combine(Path.GetTempPath(), "AcousticSimulation");
        _extentCalculationDialog = new ProgressBarDialog("Calculating Bounding Box");
        _autoPlaceDialog = new ProgressBarDialog("Auto-placing Transducers"); // ADDED
        Directory.CreateDirectory(_offloadDirectory);
        AcousticIntegration.OnPositionsChanged += OnTransducerMoved;

        _offloadDirectoryDialog =
            new ImGuiFileDialog("OffloadDirDialog", FileDialogType.OpenDirectory, "Select Offload Directory");
    }

    public void Dispose()
    {
        AcousticIntegration.OnPositionsChanged -= OnTransducerMoved;
        if (_currentDataset != null && AcousticIntegration.IsActiveFor(_currentDataset))
            AcousticIntegration.StopPlacement();

        AcousticIntegration.Configure(null, false);

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _simulator?.Dispose();
        _exportManager?.Dispose();
        _tomographyViewer?.Dispose();

        // Clean up offload directory on exit
        if (Directory.Exists(_offloadDirectory))
            try
            {
                Directory.Delete(_offloadDirectory, true);
                Logger.Log("[UI] Cleaned up offload cache on exit");
            }
            catch
            {
                // Ignore cleanup errors on exit
            }
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

        _extentCalculationDialog.Submit();
        _autoPlaceDialog.Submit();

        if (_currentDataset != dataset) _timeStepsDirty = true;
        _currentDataset = dataset;

        AcousticIntegration.UpdateMarkerPositionsForDrawing(dataset, _txPosition, _rxPosition);
        AcousticIntegration.Configure(_simulationExtent, _autoCropToSelection && _simulationExtent.HasValue);

        _tomographyViewer.Draw();

        ImGui.Text($"Dataset: {dataset.Name}");
        ImGui.Text($"Dimensions: {dataset.Width} × {dataset.Height} × {dataset.Depth}");

        var volumeMemory = (long)dataset.Width * dataset.Height * dataset.Depth * 3 * sizeof(float) * 2;
        ImGui.Text($"Estimated Memory: {volumeMemory / (1024 * 1024)} MB");

        if (_autoCropToSelection)
        {
            var extentText = "Simulation Extent: ";
            if (_isCalculatingExtent)
            {
                extentText += "Calculating...";
            }
            else if (_simulationExtent.HasValue)
            {
                var extent = _simulationExtent.Value;
                extentText += $"{extent.Width} × {extent.Height} × {extent.Depth}";
            }
            else if (_selectedMaterialIDs.Any())
            {
                extentText += "Not yet calculated. Select material(s) and calculate.";
            }
            else
            {
                extentText += "N/A (no material selected).";
            }

            ImGui.Text(extentText);
        }

        if (volumeMemory > 4L * 1024 * 1024 * 1024)
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "[!] Large dataset - chunked processing recommended");

        ImGui.Separator();

        // Density Calibration Section
        if (ImGui.CollapsingHeader("Density Calibration", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            ImGui.TextWrapped(
                "Use the 'Density Calibration' tool in the Preprocessing category to calibrate material densities.");
            ImGui.TextWrapped("This simulation will use the calibrated densities stored in the dataset's materials.");

            var isCalibrated = dataset.Materials.Any(m => m.ID != 0 && m.Density > 0);
            ImGui.Text("Status:");
            ImGui.SameLine();
            ImGui.TextColored(isCalibrated ? new Vector4(0, 1, 0, 1) : new Vector4(1, 1, 0, 1),
                isCalibrated
                    ? "[OK] Material densities appear to be set."
                    : "[WARNING] Using default material densities.");
            ImGui.Unindent();
        }

        ImGui.Separator();

        // Material Selection
        ImGui.Text("Target Material(s):");
        var materials = dataset.Materials.Where(m => m.ID != 0).ToArray();
        if (materials.Length == 0)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No materials defined in dataset.");
            return;
        }

        var prevSelection = new HashSet<byte>(_selectedMaterialIDs);
        var wasMultiMaterialEnabled = _enableMultiMaterialSelection;
        ImGui.Checkbox("Enable Multi-Material Selection", ref _enableMultiMaterialSelection);

        if (wasMultiMaterialEnabled && !_enableMultiMaterialSelection && _selectedMaterialIDs.Count > 1)
        {
            var firstSelected = _selectedMaterialIDs.First();
            _selectedMaterialIDs.Clear();
            _selectedMaterialIDs.Add(firstSelected);
            _simulationExtent = null;
        }

        ImGui.BeginChild("MaterialList", new Vector2(-1, materials.Length * 25f + 10), ImGuiChildFlags.Border);
        foreach (var material in materials)
            if (_enableMultiMaterialSelection)
            {
                var isSelected = _selectedMaterialIDs.Contains(material.ID);
                if (ImGui.Checkbox(material.Name, ref isSelected))
                {
                    if (isSelected)
                        _selectedMaterialIDs.Add(material.ID);
                    else
                        _selectedMaterialIDs.Remove(material.ID);
                    _simulationExtent = null;
                }
            }
            else
            {
                var isSelected = _selectedMaterialIDs.Contains(material.ID);
                if (ImGui.RadioButton(material.Name, isSelected))
                    if (!_selectedMaterialIDs.Contains(material.ID))
                    {
                        _selectedMaterialIDs.Clear();
                        _selectedMaterialIDs.Add(material.ID);
                        _simulationExtent = null;
                    }
            }

        ImGui.EndChild();

        var selectionChanged = !prevSelection.SetEquals(_selectedMaterialIDs);
        if (!_enableMultiMaterialSelection && selectionChanged && _selectedMaterialIDs.Any())
            if (_autoCropToSelection && !_isCalculatingExtent)
            {
                _isCalculatingExtent = true;
                _extentCalculationDialog.Open("Calculating material bounding box...");
                _ = CalculateSimulationExtentAsync(dataset);
            }

        if (_enableMultiMaterialSelection)
        {
            ImGui.Spacing();
            var canCalculate = _autoCropToSelection && _selectedMaterialIDs.Any();
            if (!canCalculate) ImGui.BeginDisabled();

            if (ImGui.Button("Calculate/Refresh Simulation Extent", new Vector2(-1, 0)))
                if (!_isCalculatingExtent)
                {
                    _simulationExtent = null;
                    _isCalculatingExtent = true;
                    _extentCalculationDialog.Open("Calculating material bounding box...");
                    _ = CalculateSimulationExtentAsync(dataset);
                }

            if (!canCalculate) ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
            {
                if (!_selectedMaterialIDs.Any())
                    ImGui.SetTooltip("Select at least one material to enable extent calculation.");
                else if (!_autoCropToSelection)
                    ImGui.SetTooltip("Enable 'Auto-Crop to Selection' to use this feature.");
                else ImGui.SetTooltip("Calculates the bounding box for the currently selected materials.");
            }
        }

        ImGui.Separator();

        var controlsDisabled = (_autoCropToSelection && _selectedMaterialIDs.Any() && !_simulationExtent.HasValue) ||
                               _isCalculatingExtent;
        if (controlsDisabled) ImGui.BeginDisabled();

        // Calibration Manager Controls
        _calibrationManager.DrawCalibrationControls(ref _youngsModulus, ref _poissonRatio);
        ImGui.Separator();

        // Wave Propagation Axis
        ImGui.Text("Wave Propagation Axis:");

        var axisChanged = false;
        axisChanged |= ImGui.RadioButton("X", ref _selectedAxisIndex, 0);
        ImGui.SameLine();
        axisChanged |= ImGui.RadioButton("Y", ref _selectedAxisIndex, 1);
        ImGui.SameLine();
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
                    if (ImGui.Button("Change Directory...")) _offloadDirectoryDialog.Open(_offloadDirectory);

                    ImGui.SameLine();

                    if (_simulator != null)
                    {
                        var (totalChunks, loadedChunks, offloadedChunks, cacheSizeBytes) = _simulator.GetCacheStats();
                        ImGui.Separator();
                        ImGui.Text("Cache Status:");
                        ImGui.Indent();
                        ImGui.Text($"Total Chunks: {totalChunks}");
                        ImGui.Text($"In Memory: {loadedChunks}");
                        ImGui.Text($"Offloaded: {offloadedChunks}");
                        ImGui.Text($"Disk Cache: {cacheSizeBytes / (1024.0 * 1024.0):F2} MB");
                        ImGui.Unindent();
                    }

                    var canClearCache = _simulator != null && !_simulator.IsSimulating;

                    if (!canClearCache) ImGui.BeginDisabled();

                    if (ImGui.Button("Clear Cache"))
                        if (_simulator != null)
                        {
                            var success = _simulator.ClearOffloadCache();
                            if (success)
                                Logger.Log("[UI] Successfully cleared offload cache");
                            else
                                Logger.LogError("[UI] Failed to clear cache - simulation may be running");
                        }

                    if (!canClearCache)
                    {
                        ImGui.EndDisabled();
                        if (ImGui.IsItemHovered())
                        {
                            if (_simulator?.IsSimulating == true)
                                ImGui.SetTooltip("Cannot clear cache while simulation is running");
                            else
                                ImGui.SetTooltip("Run a simulation first to generate cache");
                        }
                    }
                    else if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(
                            "Clears disk cache and resets LRU state.\nFrees up disk space but next simulation will be slower.");
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

            if (ImGui.Checkbox("Show Velocity Tomography", ref _showTomographyWindow))
                if (_showTomographyWindow)
                    _tomographyViewer.Show();

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
            ImGui.Text("Simulation Parameters (override material library):");
            ImGui.DragFloat("Young's Modulus (MPa)", ref _youngsModulus, 100.0f, 100.0f, 200000.0f);
            ImGui.DragFloat("Poisson's Ratio", ref _poissonRatio, 0.01f, 0.0f, 0.49f);

            if (_calibrationManager.HasCalibration)
                if (ImGui.Button("Apply Calibration from Lab Data"))
                    if (primaryMaterial != null)
                    {
                        var (calE, calNu) =
                            _calibrationManager.GetCalibratedParameters((float)primaryMaterial.Density,
                                _confiningPressure);
                        _youngsModulus = calE;
                        _poissonRatio = calNu;
                        Logger.Log(
                            $"[Simulation] Applied calibration: E={_youngsModulus:F2} MPa, ν={_poissonRatio:F4}");
                    }

            ImGui.Spacing();
            ImGui.Text("Derived Properties:");
            ImGui.Indent();
            var E = _youngsModulus * 1e6f;
            var nu = _poissonRatio;
            var mu = E / (2.0f * (1.0f + nu));
            var lambda = E * nu / ((1 + nu) * (1 - 2 * nu));
            var bulkModulus = E / (3f * (1 - 2 * nu));
            ImGui.Text($"Shear Modulus: {mu / 1e6f:F2} MPa");
            ImGui.Text($"Bulk Modulus: {bulkModulus / 1e6f:F2} MPa");
            ImGui.Text($"Lamé λ: {lambda / 1e6f:F2} MPa");
            ImGui.Text($"Lamé μ: {mu / 1e6f:F2} MPa");

            if (_lastResults != null && primaryMaterial != null)
            {
                var density = (float)primaryMaterial.Density;
                if (density <= 0) density = 2.7f;
                density *= 1000f;

                var vpExpected = MathF.Sqrt((lambda + 2 * mu) / density);
                var vsExpected = MathF.Sqrt(mu / density);

                ImGui.Spacing();
                ImGui.Text($"Expected Vp: {vpExpected:F0} m/s");
                ImGui.Text($"Expected Vs: {vsExpected:F0} m/s");
                ImGui.Text($"Expected Vp/Vs: {vpExpected / vsExpected:F3}");
            }

            ImGui.Unindent();

            if (_lastResults != null)
            {
                var calculatedPixelSize = CalculatePixelSizeFromVelocities();
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f),
                    $"Calculated Pixel Size: {calculatedPixelSize * 1000:F3} mm");
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

            ImGui.DragInt("Time Steps", ref _timeSteps, 10, 100, 100000);
            ImGui.SameLine();
            if (ImGui.Button("Auto"))
            {
                _timeSteps = CalculateEstimatedTimeSteps(dataset);
                _timeStepsDirty = false;
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    $"Automatically estimate steps based on distance and material properties.\nEstimated: {_estimatedTimeSteps}");

            // NEW: Show performance estimate
            ShowPerformanceEstimate(dataset);

            if (_lastResults != null)
            {
                var vp = (float)(_lastResults?.PWaveVelocity ?? 5000.0);
                var wavelength = vp / (_sourceFrequency * 1000f);
                ImGui.Text($"P-Wave Wavelength: {wavelength * 1000:F2} mm");
            }

            if (!_useFullFaceTransducers)
            {
                ImGui.Spacing();
                ImGui.Text("Transducer Positions (normalized 0-1):");

                ImGui.Text($"TX: ({_txPosition.X:F3}, {_txPosition.Y:F3}, {_txPosition.Z:F3})");
                ImGui.Text($"RX: ({_rxPosition.X:F3}, {_rxPosition.Y:F3}, {_rxPosition.Z:F3})");

                var isPlacingTx = AcousticIntegration.IsPlacing && AcousticIntegration.GetPlacingTarget() == "TX";
                var isPlacingRx = AcousticIntegration.IsPlacing && AcousticIntegration.GetPlacingTarget() == "RX";

                if (isPlacingTx)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.8f, 0.4f, 1.0f));
                    if (ImGui.Button("Stop Placing TX")) AcousticIntegration.StopPlacement();
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

                if (isPlacingRx)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.8f, 0.4f, 1.0f));
                    if (ImGui.Button("Stop Placing RX")) AcousticIntegration.StopPlacement();
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
                    ImGui.TextColored(new Vector4(1, 1, 0, 1),
                        $"Placing {AcousticIntegration.GetPlacingTarget()}... Click in a viewer to place. Right-click to rotate 3D view.");

                if (ImGui.Button("Auto-place Transducers"))
                    _ = AutoPlaceTransducersAsync(dataset);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(
                        "Automatically place TX/RX on opposite sides of the selected material(s) largest connected component.");

                var dx = (_rxPosition.X - _txPosition.X) * dataset.Width * dataset.PixelSize / 1000f;
                var dy = (_rxPosition.Y - _txPosition.Y) * dataset.Height * dataset.PixelSize / 1000f;
                var dz = (_rxPosition.Z - _txPosition.Z) * dataset.Depth * dataset.SliceThickness / 1000f;
                var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
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
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Automatically calibrate parameters based on previous simulations");

            ImGui.Separator();
            ImGui.DragFloat("Artificial Damping Factor", ref _artificialDampingFactor, 0.01f, 0.0f, 1.0f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    "Controls the strength of the numerical damping used to suppress high-frequency noise and prevent instability.\nIncrease this value if you see strange energy build-up.\nDefault: 0.2");

            ImGui.Unindent();
        }

        if (controlsDisabled) ImGui.EndDisabled();

        ImGui.Separator();

        // NEW: Pixel size validation and warnings
        ValidateAndWarnAboutPixelSize(dataset);

        // Simulation controls
        if (_currentState == SimulationState.Preparing)
        {
            ImGui.ProgressBar(_preparationProgress, new Vector2(-1, 0), _preparationStatus);
            if (_preparationComplete)
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "[OK] Pre-computation completed. Starting simulation...");
            if (ImGui.Button("Cancel", new Vector2(-1, 0))) CancelSimulation();
        }
        else if (_currentState == SimulationState.Simulating)
        {
            ImGui.ProgressBar(_simulator?.Progress ?? 0.0f, new Vector2(-1, 0),
                $"Simulating... {_simulator?.CurrentStep ?? 0}/{_parameters.TimeSteps} steps");
            if (_simulator != null)
            {
                ImGui.Text($"Memory Usage: {_simulator.CurrentMemoryUsageMB:F0} MB");
                ImGui.Text($"Time Elapsed: {(DateTime.Now - _simulationStartTime).TotalSeconds:F1} s");
            }

            if (ImGui.Button("Cancel Simulation", new Vector2(-1, 0))) CancelSimulation();

            if (_showTomographyWindow && _autoUpdateTomography &&
                (DateTime.Now - _lastTomographyUpdate).TotalSeconds > 1.0)
            {
                UpdateLiveTomography();
                _lastTomographyUpdate = DateTime.Now;
            }
        }
        else
        {
            var canSimulate = _selectedMaterialIDs.Any() && !controlsDisabled;
            if (!canSimulate) ImGui.BeginDisabled();

            if (ImGui.Button("Run Simulation", new Vector2(-1, 0)))
            {
                _simulationStartTime = DateTime.Now;
                _ = RunSimulationAsync(dataset);
            }

            if (!canSimulate)
            {
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                {
                    if (!_selectedMaterialIDs.Any())
                        ImGui.SetTooltip("You must select at least one material to run a simulation.");
                    else if (controlsDisabled)
                        ImGui.SetTooltip(
                            "The simulation extent has not been calculated. Select material(s) and calculate the extent before running.");
                }
            }
        }

        ImGui.Separator();

        if (ImGui.Checkbox("Auto-Crop to Selection", ref _autoCropToSelection))
        {
            _simulationExtent = null;
            AcousticIntegration.Configure(null, false);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Automatically calculate the bounding box of the selected material(s) and run the simulation only within that extent.");

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
                    var stepsPerSecond = _lastResults.TotalTimeSteps / _lastResults.ComputationTime.TotalSeconds;
                    ImGui.Text($"Performance: {stepsPerSecond:F0} steps/second");
                }

                ImGui.Spacing();
                _exportManager.SetCalibrationData(_calibrationManager.CalibrationData);
                if (_lastResults.DamageField != null)
                    ImGui.Text($"Damage Field: Available ({_lastResults.DamageField.Length} voxels)");

                _exportManager.DrawExportControls(_lastResults, _parameters, dataset, _lastResults.DamageField);
                ImGui.Spacing();

                var canAddToCalibration = _selectedMaterialIDs.Count == 1;
                if (!canAddToCalibration) ImGui.BeginDisabled();

                if (ImGui.Button("Add to Calibration Database"))
                    if (canAddToCalibration)
                    {
                        var material = dataset.Materials.First(m => m.ID == _selectedMaterialIDs.First());
                        _calibrationManager.AddSimulationResult(material.Name, material.ID, (float)material.Density,
                            _confiningPressure, _youngsModulus, _poissonRatio,
                            _lastResults.PWaveVelocity, _lastResults.SWaveVelocity);
                        Logger.Log("[Simulation] Added results to calibration database");
                    }

                if (!canAddToCalibration)
                {
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Can only add to calibration when a single material is simulated.");
                }
            }

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

    private void ValidateAndWarnAboutPixelSize(CtImageStackDataset dataset)
    {
        if (dataset == null) return;

        var pixelSizeUm = dataset.PixelSize;

        if (pixelSizeUm < 20f)
        {
            ImGui.Separator();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.5f, 0, 1));
            ImGui.TextWrapped($"[WARNING] VERY SMALL PIXEL SIZE DETECTED: {pixelSizeUm:F2} um");
            ImGui.PopStyleColor();

            ImGui.Indent();
            ImGui.TextWrapped("Small pixel sizes require:");
            ImGui.BulletText("MANY time steps (expect 20,000-30,000 for accurate results)");
            ImGui.BulletText("Use the 'Auto' button to calculate recommended steps");
            ImGui.BulletText("Expect longer computation times");
            ImGui.BulletText("Consider using GPU acceleration if available");

            if (_timeSteps > 0)
            {
                var pixelSizeM = pixelSizeUm / 1_000_000f;

                // Adaptive CFL
                float cflFactor;
                if (pixelSizeM < 5e-6f) cflFactor = 0.95f;
                else if (pixelSizeM < 20e-6f) cflFactor = 0.90f;
                else cflFactor = 0.85f;

                var dt = cflFactor * pixelSizeM / (1.732f * 6000f);
                var totalSimTime = _timeSteps * dt;

                ImGui.Spacing();
                ImGui.Text("Current settings:");
                ImGui.BulletText($"Time step: ~{dt * 1e9f:F2} nanoseconds");
                ImGui.BulletText($"Total simulation time: ~{totalSimTime * 1e6f:F2} microseconds");
                ImGui.BulletText($"Steps: {_timeSteps:N0}");

                var dx = (_rxPosition.X - _txPosition.X) * dataset.Width * pixelSizeM;
                var dy = (_rxPosition.Y - _txPosition.Y) * dataset.Height * pixelSizeM;
                var dz = (_rxPosition.Z - _txPosition.Z) * dataset.Depth * dataset.SliceThickness / 1_000_000f;
                var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                var expectedTravelTime = distance / 5000f;

                ImGui.Spacing();
                ImGui.Text("Wave travel:");
                ImGui.BulletText($"TX-RX distance: {distance * 1000f:F2} mm");
                ImGui.BulletText($"Expected P-wave arrival: ~{expectedTravelTime * 1e6f:F2} us");

                if (totalSimTime < expectedTravelTime * 1.5f)
                {
                    var recommendedSteps = (int)(expectedTravelTime * 2.5f / dt);
                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.2f, 0.2f, 1));
                    ImGui.TextWrapped("[ERROR] WARNING: Simulation too short! Wave may not reach receiver.");
                    ImGui.TextWrapped($"Recommended: {recommendedSteps:N0} steps (click 'Auto' button)");
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 1, 0, 1));
                    ImGui.TextWrapped("[OK] Simulation time appears adequate");
                    ImGui.PopStyleColor();
                }
            }

            ImGui.Unindent();
            ImGui.Separator();
        }
        else if (pixelSizeUm < 50f)
        {
            ImGui.Separator();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 0, 1));
            ImGui.TextWrapped($"[!] Small pixel size: {pixelSizeUm:F2} um");
            ImGui.TextWrapped(
                "Consider using the 'Auto' button for time steps and enable GPU acceleration if available.");
            ImGui.PopStyleColor();
            ImGui.Separator();
        }
    }


    private void UpdateLiveTomography()
    {
        if (_simulator == null || _currentDataset == null || _liveResultsForTomography == null) return;

        var expectedVp = 6000.0f;
        var expectedVs = 3000.0f;

        var primaryMaterial = _currentDataset.Materials.FirstOrDefault(m => _selectedMaterialIDs.Contains(m.ID));
        if (primaryMaterial != null)
        {
            var density_g_cm3 = (float)primaryMaterial.Density;
            if (density_g_cm3 <= 0) density_g_cm3 = 2.7f;
            var density_kg_m3 = density_g_cm3 * 1000f;

            var e_Pa = _youngsModulus * 1e6f;
            var nu = _poissonRatio;

            if (e_Pa > 0 && nu > -1.0f && nu < 0.5f)
            {
                var mu = e_Pa / (2.0f * (1.0f + nu));
                var lambda = e_Pa * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));
                expectedVp = MathF.Sqrt((lambda + 2.0f * mu) / density_kg_m3);
                expectedVs = MathF.Sqrt(mu / density_kg_m3);
            }
        }

        _liveResultsForTomography.PWaveVelocity = expectedVp;
        _liveResultsForTomography.SWaveVelocity = expectedVs;
        _liveResultsForTomography.VpVsRatio = expectedVs > 0 ? expectedVp / expectedVs : 0;

        // --- FIX ---
        // The dimensions vector MUST match the dimensions of the data being passed.
        // _liveResultsForTomography holds full-size data, so we must pass its full dimensions.
        var dimensions = new Vector3(
            _liveResultsForTomography.WaveFieldVx.GetLength(0),
            _liveResultsForTomography.WaveFieldVx.GetLength(1),
            _liveResultsForTomography.WaveFieldVx.GetLength(2)
        );

        var volumeLabels = (byte[,,])_liveResultsForTomography.Context;

        _tomographyViewer.UpdateLiveData(_liveResultsForTomography, dimensions, volumeLabels, _selectedMaterialIDs);
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

        var w = _lastResults.WaveFieldVx.GetLength(0);
        var h = _lastResults.WaveFieldVx.GetLength(1);
        var d = _lastResults.WaveFieldVx.GetLength(2);

        var x = (int)Math.Clamp(normalizedPos.X * w, 0, w - 1);
        var y = (int)Math.Clamp(normalizedPos.Y * h, 0, h - 1);
        var z = (int)Math.Clamp(normalizedPos.Z * d, 0, d - 1);

        var vx = _lastResults.WaveFieldVx[x, y, z];
        var vy = _lastResults.WaveFieldVy[x, y, z];
        var vz = _lastResults.WaveFieldVz[x, y, z];
        var velocity = new Vector3(vx, vy, vz);

        ImGui.Text($"{label} at [{x}, {y}, {z}]");
        ImGui.Indent();
        ImGui.Text($"Velocity Vector: <{vx:G3}, {vy:G3}, {vz:G3}>");
        ImGui.Text($"Total Magnitude: {velocity.Length():G4}");

        // Calculate P and S components relative to TX->RX vector
        if (label != "Midpoint")
        {
            var txVoxel = new Vector3(_txPosition.X * w, _txPosition.Y * h, _txPosition.Z * d);
            var rxVoxel = new Vector3(_rxPosition.X * w, _rxPosition.Y * h, _rxPosition.Z * d);
            var pathDir = Vector3.Normalize(rxVoxel - txVoxel);

            if (pathDir.LengthSquared() > 0.1f)
            {
                var p_component_mag = Vector3.Dot(velocity, pathDir);
                var p_component_vec = pathDir * p_component_mag;
                var s_component_vec = velocity - p_component_vec;

                ImGui.Text($"P-Wave Component: {p_component_mag:G4}");
                ImGui.Text($"S-Wave Component: {s_component_vec.Length():G4}");
            }
        }

        ImGui.Unindent();
    }

    private async Task AutoPlaceTransducersAsync(CtImageStackDataset dataset)
    {
        if (!_selectedMaterialIDs.Any())
        {
            Logger.LogWarning("[AutoPlace] No materials selected for auto-placement.");
            return;
        }

        _autoPlaceDialog.Open("Preparing for auto-placement...");

        (Vector3 tx, Vector3 rx)? placementResult = null;

        try
        {
            byte[,,] labelsToSearch;
            BoundingBox extent;

            if (_autoCropToSelection && _simulationExtent.HasValue)
            {
                extent = _simulationExtent.Value;
                labelsToSearch = await GetCroppedLabelsAsync(dataset, extent);
            }
            else
            {
                extent = new BoundingBox(0, 0, 0, dataset.Width, dataset.Height, dataset.Depth);
                labelsToSearch = await GetCroppedLabelsAsync(dataset, extent);
            }

            var progress = new Progress<(float, string)>(value => _autoPlaceDialog.Update(value.Item1, value.Item2));

            placementResult = await Task.Run(() =>
            {
                var autoPlacer =
                    new TransducerAutoPlacer(dataset, _selectedMaterialIDs, extent, labelsToSearch, progress);
                return autoPlacer.PlaceTransducersForAxis(_selectedAxisIndex, _autoPlaceDialog.CancellationToken);
            }, _autoPlaceDialog.CancellationToken);

            if (placementResult.HasValue)
            {
                var crop = extent;
                _txPosition = new Vector3(
                    (placementResult.Value.tx.X * crop.Width + crop.Min.X) / dataset.Width,
                    (placementResult.Value.tx.Y * crop.Height + crop.Min.Y) / dataset.Height,
                    (placementResult.Value.tx.Z * crop.Depth + crop.Min.Z) / dataset.Depth);
                _rxPosition = new Vector3(
                    (placementResult.Value.rx.X * crop.Width + crop.Min.X) / dataset.Width,
                    (placementResult.Value.rx.Y * crop.Height + crop.Min.Y) / dataset.Height,
                    (placementResult.Value.rx.Z * crop.Depth + crop.Min.Z) / dataset.Depth);

                AcousticIntegration.UpdateMarkerPositionsForDrawing(dataset, _txPosition, _rxPosition);
                OnTransducerMoved();
            }
            else
            {
                if (!_autoPlaceDialog.IsCancellationRequested)
                    Logger.LogError(
                        "[AutoPlace] Failed to automatically place transducers. The selected material(s) might be too small, fragmented, or not present along the chosen axis.");
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("[AutoPlace] Auto-placement was cancelled by the user.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AutoPlace] An error occurred during auto-placement: {ex.Message}");
        }
        finally
        {
            if (_autoPlaceDialog.IsActive) _autoPlaceDialog.Close();
        }
    }

    private float CalculatePixelSizeFromVelocities()
    {
        if (_lastResults == null) return _parameters.PixelSize;
        var distance = CalculateDistance();
        var timeP = _lastResults.PWaveTravelTime * _parameters.TimeStepSeconds;
        var calculatedDistance = (float)_lastResults.PWaveVelocity * timeP;
        if (calculatedDistance > 0 && distance > 0)
        {
            var pixelCount = distance / _parameters.PixelSize;
            return calculatedDistance / pixelCount;
        }

        return _parameters.PixelSize;
    }

    private float CalculateDistance()
    {
        var dx = (_rxPosition.X - _txPosition.X) * _parameters.Width;
        var dy = (_rxPosition.Y - _txPosition.Y) * _parameters.Height;
        var dz = (_rxPosition.Z - _txPosition.Z) * _parameters.Depth;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) * _parameters.PixelSize;
    }

    private int CalculateEstimatedTimeSteps(CtImageStackDataset dataset)
    {
        if (dataset == null || !_selectedMaterialIDs.Any()) return 1000;

        try
        {
            // Calculate P-wave velocities
            var vp_avg = 5000f;
            var vp_max = 5000f;
            var calculatedVps = new List<float>();

            foreach (var materialId in _selectedMaterialIDs)
            {
                var material = dataset.Materials.FirstOrDefault(m => m.ID == materialId);
                if (material == null) continue;

                var density_kg_m3 = (float)material.Density * 1000f;
                if (density_kg_m3 <= 0) density_kg_m3 = 2700f;

                var e_MPa = _youngsModulus;
                var nu = _poissonRatio;

                if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                {
                    var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                    if (physMat != null)
                    {
                        e_MPa = (float)(physMat.YoungModulus_GPa ?? _youngsModulus / 1000.0) * 1000f;
                        nu = (float)(physMat.PoissonRatio ?? _poissonRatio);
                    }
                }

                var e_Pa = e_MPa * 1e6f;
                if (e_Pa <= 0 || nu <= -1.0f || nu >= 0.5f) continue;

                var mu = e_Pa / (2.0f * (1.0f + nu));
                var lambda = e_Pa * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));
                var current_vp = MathF.Sqrt((lambda + 2.0f * mu) / density_kg_m3);

                if (!float.IsNaN(current_vp) && current_vp > 100)
                {
                    calculatedVps.Add(current_vp);
                    if (calculatedVps.Count == 1) vp_avg = current_vp;
                }
            }

            if (calculatedVps.Any()) vp_max = calculatedVps.Max();

            // Calculate distance
            var pixelSizeM = dataset.PixelSize / 1_000_000f;
            var sliceThicknessM = dataset.SliceThickness / 1_000_000f;
            var dx = (_rxPosition.X - _txPosition.X) * dataset.Width * pixelSizeM;
            var dy = (_rxPosition.Y - _txPosition.Y) * dataset.Height * pixelSizeM;
            var dz = (_rxPosition.Z - _txPosition.Z) * dataset.Depth * sliceThicknessM;
            var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance < pixelSizeM) distance = pixelSizeM;

            // ADAPTIVE CFL FACTOR - matches simulator
            float cflFactor;
            if (pixelSizeM < 5e-6f)
                cflFactor = 0.95f;
            else if (pixelSizeM < 20e-6f)
                cflFactor = 0.90f;
            else if (pixelSizeM < 50e-6f)
                cflFactor = 0.85f;
            else if (pixelSizeM < 200e-6f)
                cflFactor = 0.75f;
            else if (pixelSizeM < 1e-3f)
                cflFactor = 0.65f;
            else if (pixelSizeM < 10e-3f)
                cflFactor = 0.50f;
            else
                cflFactor = 0.35f;

            var dt_est = cflFactor * pixelSizeM / (1.732f * vp_max);

            if (dt_est < 1e-12f)
            {
                Logger.LogWarning($"[UI] Time step extremely small ({dt_est:E2}s). Using default.");
                return 2000;
            }

            // Calculate required time
            var travelTime = distance / vp_avg;
            var requiredSimTime = travelTime * 2.5f; // Buffer for S-wave + reflections
            var required_steps = (int)(requiredSimTime / dt_est);

            // Apply reasonable limits
            var clamped_steps = Math.Clamp(required_steps, 500, 100000);

            // Detailed logging for extreme cases
            if (pixelSizeM < 50e-6f || pixelSizeM > 1e-3f)
            {
                Logger.Log("[UI] ═══════════════════════════════════════");
                Logger.Log("[UI] AUTO TIME STEP CALCULATION");
                Logger.Log($"[UI] Pixel size: {pixelSizeM * 1e6f:F2} μm");
                Logger.Log($"[UI] Adaptive CFL factor: {cflFactor:F3}");
                Logger.Log($"[UI] Time step: {dt_est * 1e9f:F2} ns");
                Logger.Log($"[UI] TX-RX distance: {distance * 1000f:F2} mm");
                Logger.Log($"[UI] P-wave travel time: {travelTime * 1e6f:F2} μs");
                Logger.Log($"[UI] Total sim time: {requiredSimTime * 1e6f:F2} μs");
                Logger.Log($"[UI] Calculated steps: {required_steps:N0}");
                Logger.Log($"[UI] Final steps: {clamped_steps:N0}");
                Logger.Log("[UI] ═══════════════════════════════════════");
            }

            return clamped_steps;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[UI] Error calculating time steps: {ex.Message}");
            return 2000;
        }
    }

    private void ShowPerformanceEstimate(CtImageStackDataset dataset)
    {
        if (dataset == null || _timeSteps <= 0) return;

        var pixelSizeM = dataset.PixelSize / 1_000_000f;
        var voxelCount = dataset.Width * dataset.Height * dataset.Depth;

        // Estimate based on typical performance
        // GPU: ~100-1000 Mvoxels/sec depending on size
        // CPU: ~10-50 Mvoxels/sec
        float voxelsPerSecond;
        if (_useGPU)
        {
            // GPU performance scales with problem size
            if (voxelCount < 1e6) // < 1M voxels
                voxelsPerSecond = 50e6f;
            else if (voxelCount < 10e6) // < 10M
                voxelsPerSecond = 200e6f;
            else
                voxelsPerSecond = 500e6f;
        }
        else
        {
            // CPU performance
            voxelsPerSecond = 20e6f;
        }

        var voxelUpdatesTotal = (long)voxelCount * _timeSteps;
        var estimatedSeconds = voxelUpdatesTotal / voxelsPerSecond;

        ImGui.Spacing();
        ImGui.Text("Performance Estimate:");
        ImGui.Indent();
        ImGui.Text($"Total voxel updates: {voxelUpdatesTotal:E2}");
        ImGui.Text($"Estimated time: {FormatDuration(estimatedSeconds)}");

        if (!_useGPU && estimatedSeconds > 300)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 0, 1));
            ImGui.TextWrapped("⚠️ Consider enabling GPU acceleration for faster results!");
            ImGui.PopStyleColor();
        }

        ImGui.Unindent();
    }

    private string FormatDuration(float seconds)
    {
        if (seconds < 60)
            return $"{seconds:F1} seconds";
        if (seconds < 3600)
            return $"{seconds / 60:F1} minutes";
        return $"{seconds / 3600:F1} hours";
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

            var extent = _autoCropToSelection && _simulationExtent.HasValue
                ? _simulationExtent.Value
                : new BoundingBox(0, 0, 0, dataset.Width, dataset.Height, dataset.Depth);

            CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);

            // --- FIX START: Clamp transducer positions to the simulation extent ---
            // Calculate the local normalized positions first
            var txPosLocalNormalized = new Vector3(
                (_txPosition.X * dataset.Width - extent.Min.X) / extent.Width,
                (_txPosition.Y * dataset.Height - extent.Min.Y) / extent.Height,
                (_txPosition.Z * dataset.Depth - extent.Min.Z) / extent.Depth);

            var rxPosLocalNormalized = new Vector3(
                (_rxPosition.X * dataset.Width - extent.Min.X) / extent.Width,
                (_rxPosition.Y * dataset.Height - extent.Min.Y) / extent.Height,
                (_rxPosition.Z * dataset.Depth - extent.Min.Z) / extent.Depth);

            // Clamp the local positions to the valid [0, 1] range.
            txPosLocalNormalized.X = Math.Clamp(txPosLocalNormalized.X, 0.0f, 1.0f);
            txPosLocalNormalized.Y = Math.Clamp(txPosLocalNormalized.Y, 0.0f, 1.0f);
            txPosLocalNormalized.Z = Math.Clamp(txPosLocalNormalized.Z, 0.0f, 1.0f);

            rxPosLocalNormalized.X = Math.Clamp(rxPosLocalNormalized.X, 0.0f, 1.0f);
            rxPosLocalNormalized.Y = Math.Clamp(rxPosLocalNormalized.Y, 0.0f, 1.0f);
            rxPosLocalNormalized.Z = Math.Clamp(rxPosLocalNormalized.Z, 0.0f, 1.0f);
            // --- FIX END ---

            _parameters = new SimulationParameters
            {
                Width = extent.Width,
                Height = extent.Height,
                Depth = extent.Depth,
                PixelSize = dataset.PixelSize / 1_000_000.0f,
                SimulationExtent = extent,
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
                // Use the clamped, local normalized positions
                TxPosition = txPosLocalNormalized,
                RxPosition = rxPosLocalNormalized,
                EnableRealTimeVisualization = _enableRealTimeVisualization,
                SaveTimeSeries = _saveTimeSeries,
                SnapshotInterval = _snapshotInterval,
                UseChunkedProcessing = _useChunkedProcessing,
                ChunkSizeMB = _chunkSizeMB,
                EnableOffloading = _enableOffloading,
                OffloadDirectory = _offloadDirectory,
                ArtificialDampingFactor = _artificialDampingFactor
            };

            _preparationStatus = "Extracting volume labels...";
            _preparationProgress = 0.1f;
            var volumeLabels = await GetCroppedLabelsAsync(dataset, extent);

            _preparationStatus = "Extracting density volume...";
            _preparationProgress = 0.4f;
            var densityVolume = await ExtractDensityVolumeAsync(dataset, dataset.VolumeData, extent);

            _preparationStatus = "Extracting material properties...";
            _preparationProgress = 0.7f;
            var (youngsModulusVolume, poissonRatioVolume) = await ExtractMaterialPropertiesVolumeAsync(dataset, extent);

            _preparationStatus = "Initializing simulator...";
            _preparationProgress = 0.9f;
            _simulator = new ChunkedAcousticSimulator(_parameters);

            _simulator.ProgressUpdated += OnSimulationProgress;
            if (_enableRealTimeVisualization || _autoUpdateTomography)
            {
                // CRASH FIX: Allocate visualization buffers based on the FULL dataset dimensions, not the cropped simulation dimensions.
                _realTimeVisualizationMask = new byte[(long)dataset.Width * dataset.Height * dataset.Depth];
                _liveResultsForTomography = new SimulationResults
                {
                    WaveFieldVx = new float[dataset.Width, dataset.Height, dataset.Depth],
                    WaveFieldVy = new float[dataset.Width, dataset.Height, dataset.Depth],
                    WaveFieldVz = new float[dataset.Width, dataset.Height, dataset.Depth],
                    Context = await GetCroppedLabelsAsync(dataset,
                        new BoundingBox(0, 0, 0, dataset.Width, dataset.Height, dataset.Depth))
                };

                _simulator.WaveFieldUpdated += OnSimulationChunkUpdated;
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

                Logger.Log(
                    $"[AcousticSimulation] Simulation completed: Vp={_lastResults.PWaveVelocity:F2} m/s, Vs={_lastResults.SWaveVelocity:F2} m/s");

                if (_selectedMaterialIDs.Count == 1)
                {
                    var material = dataset.Materials.First(m => m.ID == _selectedMaterialIDs.First());
                    _calibrationManager.AddSimulationResult(
                        material.Name, material.ID, (float)material.Density, _confiningPressure,
                        _youngsModulus, _poissonRatio, _lastResults.PWaveVelocity, _lastResults.SWaveVelocity
                    );
                }

                // FIX: Set final tomography data with correct dimensions
                var finalDimensions = new Vector3(_parameters.Width, _parameters.Height, _parameters.Depth);

                // Note: _lastResults now contains:
                // WaveFieldVx = max P-wave
                // WaveFieldVy = max S-wave  
                // WaveFieldVz = max combined
                // For tomography, we'll show the combined field by default
                _tomographyViewer.SetFinalData(_lastResults, finalDimensions, volumeLabels,
                    new HashSet<byte>(_selectedMaterialIDs));
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
                _currentState = SimulationState.Idle;
            _simulator?.Dispose();
            _simulator = null;

            if (dataset != null) CtImageStackTools.Update3DPreviewFromExternal(dataset, null, Vector4.Zero);
        }
    }

    private async Task<(float[,,] youngsModulus, float[,,] poissonRatio)> ExtractMaterialPropertiesVolumeAsync(
        CtImageStackDataset dataset, BoundingBox extent)
    {
        return await Task.Run(() =>
        {
            var youngsModulus = new float[extent.Width, extent.Height, extent.Depth];
            var poissonRatio = new float[extent.Width, extent.Height, extent.Depth];

            var materialProps = new Dictionary<byte, (float E, float Nu)>();

            // Pre-calculate properties for all materials defined in the dataset
            foreach (var material in dataset.Materials)
            {
                // Default to user-override values from the UI
                var E = _youngsModulus;
                var Nu = _poissonRatio;

                // If material has a physical library entry, use it
                if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                {
                    var physMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                    if (physMat != null)
                    {
                        E = (float)(physMat.YoungModulus_GPa ?? _youngsModulus / 1000.0) * 1000f;
                        Nu = (float)(physMat.PoissonRatio ?? _poissonRatio);
                    }
                }

                materialProps[material.ID] = (E, Nu);
            }

            // Define physically realistic properties for the background medium (air/water/pores)
            const float backgroundE = 1.0f; // Very low stiffness (1 MPa)
            const float backgroundNu = 0.3f; // Poisson's ratio for fluid-like medium

            Parallel.For(0, extent.Depth, z_local =>
            {
                var z_global = extent.Min.Z + z_local;
                for (var y_local = 0; y_local < extent.Height; y_local++)
                {
                    var y_global = extent.Min.Y + y_local;
                    for (var x_local = 0; x_local < extent.Width; x_local++)
                    {
                        var x_global = extent.Min.X + x_local;
                        var label = dataset.LabelData[x_global, y_global, z_global];

                        if (materialProps.TryGetValue(label, out var props))
                        {
                            youngsModulus[x_local, y_local, z_local] = props.E;
                            poissonRatio[x_local, y_local, z_local] = props.Nu;
                        }
                        else
                        {
                            youngsModulus[x_local, y_local, z_local] = backgroundE;
                            poissonRatio[x_local, y_local, z_local] = backgroundNu;
                        }
                    }
                }
            });

            Logger.Log("[AcousticSimulation] Generated realistic heterogeneous material property volumes.");
            return (youngsModulus, poissonRatio);
        });
    }

    private async Task CalculateSimulationExtentAsync(CtImageStackDataset dataset)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                int minX = dataset.Width, minY = dataset.Height, minZ = dataset.Depth;
                int maxX = -1, maxY = -1, maxZ = -1;
                var found = false;

                for (var z = 0; z < dataset.Depth; z++)
                {
                    if (_extentCalculationDialog.IsCancellationRequested)
                        return null;

                    var progress = (float)z / dataset.Depth;
                    _extentCalculationDialog.Update(progress, $"Scanning slice {z + 1}/{dataset.Depth}...");

                    for (var y = 0; y < dataset.Height; y++)
                    for (var x = 0; x < dataset.Width; x++)
                        if (_selectedMaterialIDs.Contains(dataset.LabelData[x, y, z]))
                        {
                            found = true;
                            minX = Math.Min(minX, x);
                            minY = Math.Min(minY, y);
                            minZ = Math.Min(minZ, z);
                            maxX = Math.Max(maxX, x);
                            maxY = Math.Max(maxY, y);
                            maxZ = Math.Max(maxZ, z);
                        }
                }

                if (!found) return (BoundingBox?)null;

                const int buffer = 5;
                minX = Math.Max(0, minX - buffer);
                minY = Math.Max(0, minY - buffer);
                minZ = Math.Max(0, minZ - buffer);
                maxX = Math.Min(dataset.Width - 1, maxX + buffer);
                maxY = Math.Min(dataset.Height - 1, maxY + buffer);
                maxZ = Math.Min(dataset.Depth - 1, maxZ + buffer);

                return new BoundingBox(minX, minY, minZ, maxX - minX + 1, maxY - minY + 1, maxZ - minZ + 1);
            }, _extentCalculationDialog.CancellationToken);

            _simulationExtent = result;
            AcousticIntegration.Configure(_simulationExtent, _autoCropToSelection && _simulationExtent.HasValue);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("[AcousticSimulationUI] Bounding box calculation was cancelled.");
            _simulationExtent = null;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AcousticSimulationUI] Error calculating simulation extent: {ex.Message}");
            _simulationExtent = null;
        }
        finally
        {
            _isCalculatingExtent = false;
            if (_extentCalculationDialog.IsActive) _extentCalculationDialog.Close();
        }
    }

    private async Task<byte[,,]> ExtractVolumeLabelsAsync(CtImageStackDataset dataset)
    {
        var fullExtent = new BoundingBox(0, 0, 0, dataset.Width, dataset.Height, dataset.Depth);
        return await GetCroppedLabelsAsync(dataset, fullExtent);
    }

    private async Task<float[,,]> ExtractDensityVolumeAsync(CtImageStackDataset dataset,
        IGrayscaleVolumeData grayscaleVolume, BoundingBox extent)
    {
        return await Task.Run(() =>
        {
            Logger.Log("[AcousticSimulation] Generating heterogeneous density volume from grayscale data...");

            // --- Step 1: Pre-calculate the average grayscale value for each material ID ---
            var grayscaleStats = new ConcurrentDictionary<byte, (double sum, long count)>();

            // FIX: Explicitly cast dataset.Depth to int to resolve ambiguous invocation
            Parallel.For(extent.Min.Z, extent.Max.Z + 1, z =>
            {
                // FIX: Read grayscale data into a byte[] array, as indicated by the compiler error.
                var graySlice = new byte[dataset.Width * dataset.Height];
                var labelSlice = new byte[dataset.Width * dataset.Height];
                grayscaleVolume.ReadSliceZ(z, graySlice);
                dataset.LabelData.ReadSliceZ(z, labelSlice);

                for (var i = 0; i < labelSlice.Length; i++)
                {
                    var label = labelSlice[i];
                    if (label == 0) continue; // Skip background

                    grayscaleStats.AddOrUpdate(label,
                        (graySlice[i], 1),
                        (key, existing) => (existing.sum + graySlice[i], existing.count + 1));
                }
            });

            var avgGrayscaleMap = grayscaleStats.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.count > 0 ? kvp.Value.sum / kvp.Value.count : 0.0
            );

            // --- Step 2: Create the heterogeneous density field ---
            var density = new float[extent.Width, extent.Height, extent.Depth];

            var materialDensityMap = dataset.Materials
                .ToDictionary(m => m.ID, m => (m.Density > 0 ? (float)m.Density : 1.0f) * 1000.0f); // g/cm³ to kg/m³

            const float backgroundDensity = 1.225f; // kg/m³ for air/unlabeled voxels

            Parallel.For(0, extent.Depth, z_local =>
            {
                var z_global = extent.Min.Z + z_local;
                var graySlice = new byte[dataset.Width * dataset.Height];
                var labelSlice = new byte[dataset.Width * dataset.Height];
                grayscaleVolume.ReadSliceZ(z_global, graySlice);
                dataset.LabelData.ReadSliceZ(z_global, labelSlice);

                for (var y_local = 0; y_local < extent.Height; y_local++)
                {
                    var y_global = extent.Min.Y + y_local;
                    for (var x_local = 0; x_local < extent.Width; x_local++)
                    {
                        var x_global = extent.Min.X + x_local;

                        var slice_idx = y_global * dataset.Width + x_global;
                        var label = labelSlice[slice_idx];

                        if (materialDensityMap.TryGetValue(label, out var meanMaterialDensity) &&
                            avgGrayscaleMap.TryGetValue(label, out var avgGrayscale) &&
                            avgGrayscale > 1e-6)
                        {
                            // Scale the material's mean density by the voxel's relative grayscale intensity.
                            // This makes brighter parts of a material denser than darker parts.
                            float grayscaleValue = graySlice[slice_idx];
                            density[x_local, y_local, z_local] =
                                meanMaterialDensity * (float)(grayscaleValue / avgGrayscale);
                        }
                        else if (materialDensityMap.ContainsKey(label))
                        {
                            // Fallback for materials with no grayscale variation (or pure black)
                            density[x_local, y_local, z_local] = materialDensityMap[label];
                        }
                        else
                        {
                            // Assign background density to unlabeled voxels
                            density[x_local, y_local, z_local] = backgroundDensity;
                        }

                        density[x_local, y_local, z_local] = Math.Max(1.0f, density[x_local, y_local, z_local]);
                    }
                }
            });

            Logger.Log("[AcousticSimulation] Generated robust heterogeneous density map.");
            return density;
        });
    }

    private async Task<byte[,,]> GetCroppedLabelsAsync(CtImageStackDataset dataset, BoundingBox extent)
    {
        return await Task.Run(() =>
        {
            var labels = new byte[extent.Width, extent.Height, extent.Depth];
            Parallel.For(0, extent.Depth, z =>
            {
                for (var y = 0; y < extent.Height; y++)
                for (var x = 0; x < extent.Width; x++)
                    labels[x, y, z] = dataset.LabelData[extent.Min.X + x, extent.Min.Y + y, extent.Min.Z + z];
            });
            return labels;
        });
    }

    private void OnSimulationChunkUpdated(object sender, WaveFieldUpdateEventArgs e)
    {
        if (_isSimulating)
        {
            // Update data for live tomography
            if (_showTomographyWindow && _autoUpdateTomography && _liveResultsForTomography != null)
                UpdateTomographyData(e);

            // Update data for 3D visualization preview
            if (_enableRealTimeVisualization &&
                (DateTime.Now - _lastVisualizationUpdate).TotalSeconds >= _visualizationUpdateInterval)
            {
                _lastVisualizationUpdate = DateTime.Now;
                Update3DVisualizationMask(e);
                CtImageStackTools.Update3DPreviewFromExternal(_currentDataset, _realTimeVisualizationMask,
                    new Vector4(1.0f, 0.5f, 0.0f, 0.5f));
            }
        }
    }

    private void UpdateTomographyData(WaveFieldUpdateEventArgs e)
    {
        var (vx, vy, vz) = e.ChunkVelocityFields;
        var simExtent = _parameters.SimulationExtent.Value;

        // CRITICAL: Use actual chunk dimensions, not parameters
        var chunkWidth = vx.GetLength(0);
        var chunkHeight = vx.GetLength(1);
        var chunkDepth = vx.GetLength(2);

        for (var z_chunk = 0; z_chunk < chunkDepth; z_chunk++)
        {
            var z_global = simExtent.Min.Z + e.ChunkStartZ + z_chunk;

            // Validate bounds
            if (z_global < 0 || z_global >= _liveResultsForTomography.WaveFieldVx.GetLength(2))
            {
                Logger.LogError($"[Tomography] Z out of bounds: {z_global}");
                continue;
            }

            for (var y_chunk = 0; y_chunk < chunkHeight; y_chunk++)
            {
                var y_global = simExtent.Min.Y + y_chunk;
                if (y_global >= _liveResultsForTomography.WaveFieldVx.GetLength(1)) continue;

                for (var x_chunk = 0; x_chunk < chunkWidth; x_chunk++)
                {
                    var x_global = simExtent.Min.X + x_chunk;
                    if (x_global >= _liveResultsForTomography.WaveFieldVx.GetLength(0)) continue;

                    _liveResultsForTomography.WaveFieldVx[x_global, y_global, z_global] = vx[x_chunk, y_chunk, z_chunk];
                    _liveResultsForTomography.WaveFieldVy[x_global, y_global, z_global] = vy[x_chunk, y_chunk, z_chunk];
                    _liveResultsForTomography.WaveFieldVz[x_global, y_global, z_global] = vz[x_chunk, y_chunk, z_chunk];
                }
            }
        }
    }

    private void Update3DVisualizationMask(WaveFieldUpdateEventArgs e)
    {
        var (vx, vy, vz) = e.ChunkVelocityFields;
        var (chunkMask, _) = CreateWaveFieldMaskForChunk(vx, vy, vz);

        var simExtent = _parameters.SimulationExtent.Value;
        var chunkWidth = vx.GetLength(0);
        var chunkHeight = vx.GetLength(1);

        long fullWidth = _currentDataset.Width;
        var full_wh = fullWidth * _currentDataset.Height;

        for (var z_chunk = 0; z_chunk < e.ChunkDepth; z_chunk++)
        {
            long z_global = simExtent.Min.Z + e.ChunkStartZ + z_chunk;
            var dest_z_offset = z_global * full_wh;

            for (var y_chunk = 0; y_chunk < chunkHeight; y_chunk++)
            {
                long y_global = simExtent.Min.Y + y_chunk;
                var dest_yx_offset = dest_z_offset + y_global * fullWidth + simExtent.Min.X;
                var src_yx_offset = (long)z_chunk * chunkWidth * chunkHeight + (long)y_chunk * chunkWidth;

                Buffer.BlockCopy(chunkMask, (int)src_yx_offset, _realTimeVisualizationMask, (int)dest_yx_offset,
                    chunkWidth);
            }
        }
    }


    private (byte[], float) CreateWaveFieldMaskForChunk(float[,,] vx, float[,,] vy, float[,,] vz)
    {
        var width = vx.GetLength(0);
        var height = vx.GetLength(1);
        var depth = vz.GetLength(2);
        var mask = new byte[width * height * depth];
        float maxAmplitude = 0;

        for (var z = 0; z < depth; z++)
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var magSq = vx[x, y, z] * vx[x, y, z] + vy[x, y, z] * vy[x, y, z] + vz[x, y, z] * vz[x, y, z];
            maxAmplitude = Math.Max(maxAmplitude, magSq);
        }

        maxAmplitude = MathF.Sqrt(maxAmplitude);

        if (maxAmplitude > 1e-9f)
        {
            var idx = 0;
            for (var z = 0; z < depth; z++)
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var mag = MathF.Sqrt(vx[x, y, z] * vx[x, y, z] + vy[x, y, z] * vy[x, y, z] + vz[x, y, z] * vz[x, y, z]);
                var normalized = mag / maxAmplitude;
                mask[idx++] = (byte)(Math.Clamp(normalized, 0f, 1f) * 255);
            }
        }

        return (mask, maxAmplitude);
    }

    private void CancelSimulation()
    {
        _cancellationTokenSource?.Cancel();
        Logger.Log("[AcousticSimulation] Simulation cancelled by user");
    }

    private void OnSimulationProgress(object sender, SimulationProgressEventArgs e)
    {
        /* UI polls progress directly */
    }

    private enum SimulationState
    {
        Idle,
        Preparing,
        Simulating,
        Completed,
        Failed,
        Cancelled
    }
}