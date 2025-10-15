// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulationUI.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Loaders;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public class GeomechanicalSimulationUI : IDisposable
{
    private readonly ImGuiExportFileDialog _acousticFileDialog;
    private readonly GeomechanicalCalibrationManager _calibrationManager;
    private readonly GeomechanicalExportManager _exportManager;
    private readonly ProgressBarDialog _extentDialog;
    private readonly MohrCircleRenderer _mohrRenderer;
    private readonly ImGuiExportFileDialog _permeabilityFileDialog;
    private readonly ImGuiExportFileDialog _pnmFileDialog;
    private readonly ImGuiExportFileDialog _offloadDirectoryDialog;
    private readonly ProgressBarDialog _progressDialog;

    // Material selection
    private readonly HashSet<byte> _selectedMaterialIDs = new();
    private string _acousticDatasetPath = "";
    private bool _autoCropToSelection = true;
    private float _biotCoefficient = 0.8f;
    private CancellationTokenSource _cancellationTokenSource;
    private float _cohesion = 10f;

    // UI State
    private CtImageStackDataset _currentDataset;
    private float _damageThreshold = 0.8f;
    private float _density = 2700f;
    private float _dilationAngle = 10f;
    private bool _enableDamageEvolution = true;
    private bool _enableMultiMaterial;

    // Real-time visualization
    private bool _enableRealTimeViz = true;

    // Memory management for huge datasets
    private bool _enableOffloading = false;
    private string _offloadDirectory = "";

    // Failure criterion
    private int _failureCriterionIndex; // Mohr-Coulomb
    private float _frictionAngle = 30f;
    private float _hoekBrown_a = 0.5f;
    private float _hoekBrown_mb = 1.5f;

    // Hoek-Brown parameters
    private float _hoekBrown_mi = 10f;
    private float _hoekBrown_s = 0.004f;
    private bool _isCalculatingExtent;
    private bool _isSimulating;
    private GeomechanicalResults _lastResults;

    // Loading conditions
    private int _loadingModeIndex = 2; // Triaxial
    private int _maxIterations = 1000;
    private GeomechanicalParameters _params;
    private string _permeabilityCsvPath = "";

    // Integration
    private string _pnmDatasetPath = "";
    private float _poissonRatio = 0.25f;
    private float _porePressure = 10f;
    private int _selectedMaterialIndex;

    // Mohr circle viewer
    private bool _showMohrCircles;
    private float _sigma1 = 100f;
    private Vector3 _sigma1Direction = new(0, 0, 1);
    private float _sigma2 = 50f;
    private float _sigma3 = 20f;

    // Simulation extent
    private BoundingBox _simulationExtent;
    private float _tensileStrength = 5f;
    private float _tolerance = 1e-4f;

    // Computational settings
    private bool _useGPU = true;

    // Pore pressure
    private bool _usePorePressure;
    private float _vizUpdateInterval = 0.5f;

    // Material properties
    private float _youngModulus = 30000f;

    public GeomechanicalSimulationUI()
    {
        _calibrationManager = new GeomechanicalCalibrationManager();
        _mohrRenderer = new MohrCircleRenderer();
        _exportManager = new GeomechanicalExportManager();
        _progressDialog = new ProgressBarDialog("Geomechanical Simulation");
        _extentDialog = new ProgressBarDialog("Calculating Extent");

        _pnmFileDialog = new ImGuiExportFileDialog("PNMFile", "Select PNM Dataset");
        _pnmFileDialog.SetExtensions((".pnm", "PNM Dataset"));

        _permeabilityFileDialog = new ImGuiExportFileDialog("PermFile", "Select Permeability CSV");
        _permeabilityFileDialog.SetExtensions((".csv", "CSV File"));

        _acousticFileDialog = new ImGuiExportFileDialog("AcousticFile", "Select Acoustic Volume");
        _acousticFileDialog.SetExtensions((".acvol", "Acoustic Volume"));

        _offloadDirectoryDialog = new ImGuiExportFileDialog("OffloadDir", "Select Offload Directory");
        // For directory selection, we don't set any extensions - user will select/create folder and use current path
        _offloadDirectoryDialog.SetExtensions(Array.Empty<(string, string)>());

        // Set default offload directory
        _offloadDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeoscientistToolkit", "GeomechOffload");
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _mohrRenderer?.Dispose();
        _exportManager?.Dispose();
    }

    public void DrawPanel(CtImageStackDataset dataset)
    {
        if (dataset == null) return;

        _currentDataset = dataset;
        _progressDialog.Submit();
        _extentDialog.Submit();

        ImGui.Text($"Dataset: {dataset.Name}");
        ImGui.Text($"Dimensions: {dataset.Width} × {dataset.Height} × {dataset.Depth}");
        ImGui.Separator();

        // Material Selection
        DrawMaterialSelection(dataset);
        ImGui.Separator();

        // Data Integration
        if (ImGui.CollapsingHeader("Data Integration")) DrawDataIntegration(dataset);
        ImGui.Separator();

        // Calibration
        if (ImGui.CollapsingHeader("Laboratory Calibration"))
            _calibrationManager.DrawCalibrationUI(ref _youngModulus, ref _poissonRatio,
                ref _cohesion, ref _frictionAngle, ref _tensileStrength);
        ImGui.Separator();

        // Loading Conditions
        if (ImGui.CollapsingHeader("Loading Conditions", ImGuiTreeNodeFlags.DefaultOpen)) DrawLoadingConditions();
        ImGui.Separator();

        // Material Properties
        if (ImGui.CollapsingHeader("Material Properties", ImGuiTreeNodeFlags.DefaultOpen))
            DrawMaterialProperties(dataset);
        ImGui.Separator();

        // Pore Pressure
        if (ImGui.CollapsingHeader("Pore Pressure Effects")) DrawPorePressure();
        ImGui.Separator();

        // Failure Criterion
        if (ImGui.CollapsingHeader("Failure Criterion")) DrawFailureCriterion();
        ImGui.Separator();

        // Computational Settings
        if (ImGui.CollapsingHeader("Computational Settings")) DrawComputationalSettings();
        ImGui.Separator();

        // Memory Management (NEW)
        if (ImGui.CollapsingHeader("Memory Management")) DrawMemoryManagement();
        ImGui.Separator();

        // Visualization
        if (ImGui.CollapsingHeader("Real-Time Visualization"))
        {
            ImGui.Checkbox("Enable real-time 3D visualization", ref _enableRealTimeViz);
            if (_enableRealTimeViz) ImGui.DragFloat("Update interval (s)", ref _vizUpdateInterval, 0.1f, 0.1f, 5f);
        }

        ImGui.Separator();

        // Simulation Controls
        DrawSimulationControls(dataset);

        // Results
        if (_lastResults != null)
        {
            ImGui.Separator();
            DrawResults();
        }

        // File dialogs
        if (_pnmFileDialog.Submit())
            _pnmDatasetPath = _pnmFileDialog.SelectedPath;
        if (_permeabilityFileDialog.Submit())
            _permeabilityCsvPath = _permeabilityFileDialog.SelectedPath;
        if (_acousticFileDialog.Submit())
            _acousticDatasetPath = _acousticFileDialog.SelectedPath;
        
        // For directory selection, use CurrentDirectory when dialog is submitted
        if (_offloadDirectoryDialog.Submit())
            _offloadDirectory = _offloadDirectoryDialog.CurrentDirectory;

        // Mohr circle window
        if (_showMohrCircles && _lastResults?.MohrCircles != null) DrawMohrCircleWindow();
    }

    private void DrawMaterialSelection(CtImageStackDataset dataset)
    {
        ImGui.Text("Target Material(s):");

        var materials = dataset.Materials.Where(m => m.ID != 0).ToArray();
        if (materials.Length == 0)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No materials defined in dataset.");
            return;
        }

        ImGui.Checkbox("Enable Multi-Material Selection", ref _enableMultiMaterial);

        ImGui.BeginChild("MaterialList", new Vector2(-1, materials.Length * 25f + 10),
            ImGuiChildFlags.Border);

        foreach (var material in materials)
            if (_enableMultiMaterial)
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
                {
                    _selectedMaterialIDs.Clear();
                    _selectedMaterialIDs.Add(material.ID);
                    _simulationExtent = null;
                }
            }

        ImGui.EndChild();

        if (_autoCropToSelection && _selectedMaterialIDs.Any())
        {
            if (_simulationExtent != null)
            {
                var ext = _simulationExtent;
                ImGui.Text($"Simulation Extent: {ext.Width} × {ext.Height} × {ext.Depth}");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Extent not calculated");
            }

            if (ImGui.Button("Calculate Extent", new Vector2(-1, 0))) _ = CalculateExtentAsync(dataset);
        }

        ImGui.Checkbox("Auto-crop to selection", ref _autoCropToSelection);
    }

    private void DrawDataIntegration(CtImageStackDataset dataset)
    {
        ImGui.TextWrapped("Integrate data from PNM, permeability measurements, or acoustic simulations:");
        ImGui.Spacing();

        // PNM Dataset
        ImGui.Text("PNM Dataset:");
        ImGui.SameLine();
        if (ImGui.Button("Browse##PNM"))
            _pnmFileDialog.Open();
        ImGui.SameLine();
        if (ImGui.Button("Clear##PNM"))
            _pnmDatasetPath = "";

        if (!string.IsNullOrEmpty(_pnmDatasetPath))
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"✓ {Path.GetFileName(_pnmDatasetPath)}");
            ImGui.Indent();
            ImGui.TextWrapped("Will use: Porosity, permeability, pore size distribution");
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Permeability CSV
        ImGui.Text("Permeability CSV:");
        ImGui.SameLine();
        if (ImGui.Button("Browse##Perm"))
            _permeabilityFileDialog.Open();
        ImGui.SameLine();
        if (ImGui.Button("Clear##Perm"))
            _permeabilityCsvPath = "";

        if (!string.IsNullOrEmpty(_permeabilityCsvPath))
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"✓ {Path.GetFileName(_permeabilityCsvPath)}");
            ImGui.Indent();
            ImGui.TextWrapped("Expected columns: MaterialID, Permeability_mD, Porosity");
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Acoustic Volume
        ImGui.Text("Acoustic Volume:");
        ImGui.SameLine();
        if (ImGui.Button("Browse##Acoustic"))
            _acousticFileDialog.Open();
        ImGui.SameLine();
        if (ImGui.Button("Clear##Acoustic"))
            _acousticDatasetPath = "";

        if (!string.IsNullOrEmpty(_acousticDatasetPath))
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"✓ {Path.GetFileName(_acousticDatasetPath)}");
            ImGui.Indent();
            ImGui.TextWrapped("Will use: Vp, Vs, elastic moduli");
            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Material Library fallback
        ImGui.TextWrapped("If no data provided, properties will be sourced from the Material Library " +
                          "based on assigned physical materials.");
    }

    private void DrawLoadingConditions()
    {
        string[] modes = { "Uniaxial", "Biaxial", "Triaxial", "Custom" };
        ImGui.Combo("Loading Mode", ref _loadingModeIndex, modes, modes.Length);

        ImGui.Spacing();

        switch (_loadingModeIndex)
        {
            case 0: // Uniaxial
                ImGui.DragFloat("σ1 (MPa)", ref _sigma1, 1f, 0f, 1000f);
                _sigma2 = 0;
                _sigma3 = 0;
                break;

            case 1: // Biaxial
                ImGui.DragFloat("σ1 (MPa)", ref _sigma1, 1f, 0f, 1000f);
                ImGui.DragFloat("σ2 (MPa)", ref _sigma2, 1f, 0f, 1000f);
                _sigma3 = 0;
                break;

            case 2: // Triaxial
                ImGui.DragFloat("σ1 (Max principal, MPa)", ref _sigma1, 1f, 0f, 1000f);
                ImGui.DragFloat("σ2 (Intermediate, MPa)", ref _sigma2, 1f, 0f, 1000f);
                ImGui.DragFloat("σ3 (Confining, MPa)", ref _sigma3, 1f, 0f, 1000f);
                break;

            case 3: // Custom
                ImGui.DragFloat("σ1 (MPa)", ref _sigma1, 1f, 0f, 1000f);
                ImGui.DragFloat("σ2 (MPa)", ref _sigma2, 1f, 0f, 1000f);
                ImGui.DragFloat("σ3 (MPa)", ref _sigma3, 1f, 0f, 1000f);

                var dir = _sigma1Direction;
                if (ImGui.DragFloat3("σ1 Direction", ref dir)) _sigma1Direction = Vector3.Normalize(dir);
                break;
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1),
            "Positive = compression, Negative = tension");
    }

    private void DrawMaterialProperties(CtImageStackDataset dataset)
    {
        var primaryMaterial = dataset.Materials.FirstOrDefault(m => _selectedMaterialIDs.Contains(m.ID));

        if (primaryMaterial != null && !string.IsNullOrEmpty(primaryMaterial.PhysicalMaterialName))
        {
            var physMat = MaterialLibrary.Instance.Find(primaryMaterial.PhysicalMaterialName);
            if (physMat != null)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1),
                    $"Physical Material: {physMat.Name}");

                if (ImGui.Button("Apply Library Values"))
                {
                    _youngModulus = (float)(physMat.YoungModulus_GPa ?? 30) * 1000f;
                    _poissonRatio = (float)(physMat.PoissonRatio ?? 0.25);
                    _density = (float)(physMat.Density_kg_m3 ?? 2700);

                    // Estimate strength parameters from rock type
                    if (physMat.Name.Contains("Sandstone"))
                    {
                        _cohesion = 20f;
                        _frictionAngle = 35f;
                        _tensileStrength = 5f;
                    }
                    else if (physMat.Name.Contains("Limestone"))
                    {
                        _cohesion = 30f;
                        _frictionAngle = 40f;
                        _tensileStrength = 8f;
                    }
                    else if (physMat.Name.Contains("Granite"))
                    {
                        _cohesion = 50f;
                        _frictionAngle = 55f;
                        _tensileStrength = 15f;
                    }
                }

                ImGui.Separator();
            }
        }

        ImGui.DragFloat("Young's Modulus (MPa)", ref _youngModulus, 100f, 100f, 200000f);
        ImGui.DragFloat("Poisson's Ratio", ref _poissonRatio, 0.01f, 0f, 0.49f);
        ImGui.DragFloat("Density (kg/m³)", ref _density, 10f, 1000f, 5000f);
        ImGui.Spacing();
        ImGui.DragFloat("Cohesion (MPa)", ref _cohesion, 0.5f, 0f, 100f);
        ImGui.DragFloat("Friction Angle (°)", ref _frictionAngle, 1f, 0f, 70f);
        ImGui.DragFloat("Tensile Strength (MPa)", ref _tensileStrength, 0.5f, 0f, 50f);

        ImGui.Spacing();
        ImGui.Text("Derived Properties:");
        ImGui.Indent();

        var E = _youngModulus * 1e6f;
        var nu = _poissonRatio;
        var mu = E / (2 * (1 + nu));
        var K = E / (3 * (1 - 2 * nu));

        ImGui.Text($"Shear Modulus: {(mu / 1e6f):F0} MPa");
        ImGui.Text($"Bulk Modulus: {(K / 1e6f):F0} MPa");

        ImGui.Unindent();
    }

    private void DrawPorePressure()
    {
        ImGui.Checkbox("Include pore pressure effects", ref _usePorePressure);

        if (_usePorePressure)
        {
            ImGui.Indent();
            ImGui.DragFloat("Pore Pressure (MPa)", ref _porePressure, 0.5f, 0f, 100f);
            ImGui.DragFloat("Biot Coefficient", ref _biotCoefficient, 0.01f, 0f, 1f);

            ImGui.Spacing();
            var effectiveStress = _sigma3 - _biotCoefficient * _porePressure;
            ImGui.Text($"Effective σ3: {effectiveStress:F1} MPa");
            ImGui.Unindent();
        }
    }

    private void DrawFailureCriterion()
    {
        string[] criteria = { "Mohr-Coulomb", "Drucker-Prager", "Hoek-Brown", "Griffith" };
        ImGui.Combo("Failure Criterion", ref _failureCriterionIndex, criteria, criteria.Length);

        ImGui.Spacing();

        switch (_failureCriterionIndex)
        {
            case 0: // Mohr-Coulomb
                ImGui.TextWrapped("τ = c + σn·tan(φ)");
                ImGui.Text($"Using: c = {_cohesion} MPa, φ = {_frictionAngle}°");
                break;

            case 1: // Drucker-Prager
                ImGui.TextWrapped("√J2 = α·I1 + k");
                ImGui.Text($"Derived from: c = {_cohesion} MPa, φ = {_frictionAngle}°");
                break;

            case 2: // Hoek-Brown
                ImGui.TextWrapped("σ1 = σ3 + σci·(mb·σ3/σci + s)^a");
                ImGui.DragFloat("Material constant mi", ref _hoekBrown_mi, 0.5f, 1f, 30f);
                ImGui.DragFloat("Reduced constant mb", ref _hoekBrown_mb, 0.1f, 0.1f, 10f);
                ImGui.DragFloat("Rock mass parameter s", ref _hoekBrown_s, 0.001f, 0f, 1f);
                ImGui.DragFloat("Exponent a", ref _hoekBrown_a, 0.05f, 0.5f, 0.65f);
                break;

            case 3: // Griffith
                ImGui.TextWrapped("For tensile failure");
                ImGui.Text($"Using: T0 = {_tensileStrength} MPa");
                break;
        }

        ImGui.Spacing();
        ImGui.DragFloat("Dilation Angle (°)", ref _dilationAngle, 1f, 0f, _frictionAngle);
    }

    private void DrawComputationalSettings()
    {
        ImGui.Checkbox("Use GPU Acceleration (OpenCL)", ref _useGPU);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Significant speedup for large volumes");

        ImGui.DragInt("Max Iterations", ref _maxIterations, 10, 100, 10000);
        ImGui.DragFloat("Convergence Tolerance", ref _tolerance, 1e-5f, 1e-6f, 1e-2f, "%.1e");

        ImGui.Spacing();
        ImGui.Checkbox("Enable Damage Evolution", ref _enableDamageEvolution);
        if (_enableDamageEvolution)
        {
            ImGui.Indent();
            ImGui.DragFloat("Damage Threshold", ref _damageThreshold, 0.05f, 0.5f, 1f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Fraction of failure stress at which damage begins");
            ImGui.Unindent();
        }
    }

    private void DrawMemoryManagement()
    {
        ImGui.TextWrapped("For extremely large datasets (>32 GB), enable data offloading to disk:");
        ImGui.Spacing();

        ImGui.Checkbox("Enable Data Offloading", ref _enableOffloading);
        
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Offloads chunk data to disk when memory is constrained.\n" +
                           "Recommended for datasets requiring >16 GB RAM.\n" +
                           "May reduce performance but allows processing huge volumes.");
        }

        if (_enableOffloading)
        {
            ImGui.Indent();
            ImGui.Spacing();
            
            ImGui.Text("Offload Directory:");
            ImGui.SameLine();
            if (ImGui.Button("Browse##Offload"))
            {
                _offloadDirectoryDialog.Open("", _offloadDirectory);
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Navigate to the desired directory and click 'Export' to select it.\n" +
                               "You can create new folders using the 'New Folder' button.");
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Clear##Offload"))
            {
                ClearOffloadCache();
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Delete all temporary files in the offload directory.\n" +
                               "Safe to use when no simulation is running.");
            }
            
            ImGui.TextWrapped(_offloadDirectory);
            
            if (!string.IsNullOrEmpty(_offloadDirectory))
            {
                ImGui.Spacing();
                
                // Show directory info and cache size
                try
                {
                    if (Directory.Exists(_offloadDirectory))
                    {
                        var drive = new DriveInfo(Path.GetPathRoot(_offloadDirectory));
                        var freeSpace = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                        
                        // Calculate cache size
                        var cacheSize = CalculateDirectorySize(_offloadDirectory);
                        var cacheSizeGB = cacheSize / (1024.0 * 1024 * 1024);
                        
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 1f, 1), 
                            $"Available space: {freeSpace:F1} GB");
                        
                        if (cacheSize > 0)
                        {
                            ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1), 
                                $"Cache size: {cacheSizeGB:F2} GB");
                        }
                        
                        if (freeSpace < 50)
                        {
                            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), 
                                "⚠ Warning: Low disk space!");
                        }
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1, 1, 0, 1), 
                            "Directory will be created on simulation start");
                    }
                }
                catch (Exception ex)
                {
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), 
                        $"Unable to access directory: {ex.Message}");
                }
            }
            
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), 
                "Note: Temporary files are automatically cleaned up after simulation");
            
            ImGui.Unindent();
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), 
                "All data will be kept in RAM (faster, but requires more memory)");
        }
    }

    private void ClearOffloadCache()
    {
        if (string.IsNullOrEmpty(_offloadDirectory))
            return;

        if (_isSimulating)
        {
            Logger.LogWarning("[Geomechanics] Cannot clear cache while simulation is running");
            return;
        }

        try
        {
            if (Directory.Exists(_offloadDirectory))
            {
                var files = Directory.GetFiles(_offloadDirectory, "*.*", SearchOption.AllDirectories);
                var fileCount = files.Length;
                
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"[Geomechanics] Could not delete file {file}: {ex.Message}");
                    }
                }

                // Remove empty subdirectories
                var directories = Directory.GetDirectories(_offloadDirectory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length); // Delete deepest first

                foreach (var dir in directories)
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"[Geomechanics] Could not delete directory {dir}: {ex.Message}");
                    }
                }

                Logger.Log($"[Geomechanics] Cleared offload cache: {fileCount} file(s) deleted");
            }
            else
            {
                Logger.Log("[Geomechanics] Offload directory does not exist, nothing to clear");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Geomechanics] Failed to clear offload cache: {ex.Message}");
        }
    }

    private long CalculateDirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;

        try
        {
            var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            return files.Sum(file =>
            {
                try
                {
                    return new FileInfo(file).Length;
                }
                catch
                {
                    return 0;
                }
            });
        }
        catch
        {
            return 0;
        }
    }

    private void DrawSimulationControls(CtImageStackDataset dataset)
    {
        var canSimulate = _selectedMaterialIDs.Any() &&
                          (!_autoCropToSelection || _simulationExtent != null);

        if (_isSimulating)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Simulating...", new Vector2(-1, 0));
            ImGui.EndDisabled();

            if (ImGui.Button("Cancel", new Vector2(-1, 0))) _cancellationTokenSource?.Cancel();
        }
        else
        {
            if (!canSimulate) ImGui.BeginDisabled();

            if (ImGui.Button("Run Geomechanical Simulation", new Vector2(-1, 0))) _ = RunSimulationAsync(dataset);

            if (!canSimulate)
            {
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                {
                    if (!_selectedMaterialIDs.Any())
                        ImGui.SetTooltip("Select at least one material");
                    else
                        ImGui.SetTooltip("Calculate simulation extent first");
                }
            }
        }
    }

    private void DrawResults()
    {
        if (ImGui.CollapsingHeader("Simulation Results", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Computation Time: {_lastResults.ComputationTime.TotalSeconds:F2} s");
            ImGui.Text($"Iterations: {_lastResults.IterationsPerformed}");
            ImGui.Text($"Converged: {(_lastResults.Converged ? "Yes" : "No")}");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Stress Statistics:");
            ImGui.Indent();
            ImGui.Text($"Mean Stress: {(_lastResults.MeanStress / 1e6f):F2} MPa");
            ImGui.Text($"Max Shear Stress: {(_lastResults.MaxShearStress / 1e6f):F2} MPa");
            ImGui.Unindent();

            ImGui.Spacing();
            ImGui.Text("Failure Statistics:");
            ImGui.Indent();
            ImGui.Text($"Total Voxels: {_lastResults.TotalVoxels:N0}");
            ImGui.Text($"Failed Voxels: {_lastResults.FailedVoxels:N0}");
            ImGui.Text($"Failure Percentage: {_lastResults.FailedVoxelPercentage:F2}%");
            ImGui.Unindent();

            ImGui.Spacing();

            if (ImGui.Button("View Mohr Circles", new Vector2(-1, 0))) _showMohrCircles = true;

            ImGui.Spacing();
            _exportManager.DrawExportControls(_lastResults, _currentDataset);
        }
    }

    private void DrawMohrCircleWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Mohr Circles & Failure Analysis", ref _showMohrCircles))
            _mohrRenderer.Draw(_lastResults, _params);
        ImGui.End();
    }

    private async Task CalculateExtentAsync(CtImageStackDataset dataset)
    {
        if (_isCalculatingExtent) return;

        _isCalculatingExtent = true;
        _extentDialog.Open("Calculating material extent...");

        try
        {
            var extent = await Task.Run(() =>
            {
                int minX = dataset.Width, minY = dataset.Height, minZ = dataset.Depth;
                int maxX = -1, maxY = -1, maxZ = -1;
                var found = false;

                for (var z = 0; z < dataset.Depth; z++)
                {
                    if (_extentDialog.IsCancellationRequested)
                        return null;

                    var progress = (float)z / dataset.Depth;
                    _extentDialog.Update(progress, $"Scanning slice {z + 1}/{dataset.Depth}...");

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

                if (!found) return null;

                // Add buffer
                const int buffer = 5;
                minX = Math.Max(0, minX - buffer);
                minY = Math.Max(0, minY - buffer);
                minZ = Math.Max(0, minZ - buffer);
                maxX = Math.Min(dataset.Width - 1, maxX + buffer);
                maxY = Math.Min(dataset.Height - 1, maxY + buffer);
                maxZ = Math.Min(dataset.Depth - 1, maxZ + buffer);

                return new BoundingBox(minX, minY, minZ,
                    maxX - minX + 1, maxY - minY + 1, maxZ - minZ + 1);
            }, _extentDialog.CancellationToken);

            _simulationExtent = extent;
        }
        catch (OperationCanceledException)
        {
            Logger.Log("[Geomechanics] Extent calculation cancelled");
        }
        finally
        {
            _isCalculatingExtent = false;
            _extentDialog.Close();
        }
    }

    private async Task RunSimulationAsync(CtImageStackDataset dataset)
    {
        if (_isSimulating) return;

        _isSimulating = true;
        _cancellationTokenSource = new CancellationTokenSource();
        _progressDialog.Open("Preparing simulation...");

        try
        {
            // Ensure offload directory exists if offloading is enabled
            if (_enableOffloading)
            {
                if (string.IsNullOrEmpty(_offloadDirectory))
                {
                    _offloadDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GeoscientistToolkit", "GeomechOffload");
                }
                
                if (!Directory.Exists(_offloadDirectory))
                {
                    Directory.CreateDirectory(_offloadDirectory);
                }
            }

            // Build parameters
            var extent = _autoCropToSelection && _simulationExtent != null
                ? _simulationExtent
                : new BoundingBox(0, 0, 0, dataset.Width, dataset.Height, dataset.Depth);

            _params = new GeomechanicalParameters
            {
                Width = extent.Width,
                Height = extent.Height,
                Depth = extent.Depth,
                PixelSize = dataset.PixelSize,
                SimulationExtent = extent,
                SelectedMaterialIDs = new HashSet<byte>(_selectedMaterialIDs),
                YoungModulus = _youngModulus,
                PoissonRatio = _poissonRatio,
                Cohesion = _cohesion,
                FrictionAngle = _frictionAngle,
                TensileStrength = _tensileStrength,
                Density = _density,
                LoadingMode = (LoadingMode)_loadingModeIndex,
                Sigma1 = _sigma1,
                Sigma2 = _sigma2,
                Sigma3 = _sigma3,
                Sigma1Direction = _sigma1Direction,
                UsePorePressure = _usePorePressure,
                PorePressure = _porePressure,
                BiotCoefficient = _biotCoefficient,
                FailureCriterion = (FailureCriterion)_failureCriterionIndex,
                DilationAngle = _dilationAngle,
                HoekBrown_mi = _hoekBrown_mi,
                HoekBrown_mb = _hoekBrown_mb,
                HoekBrown_s = _hoekBrown_s,
                HoekBrown_a = _hoekBrown_a,
                UseGPU = _useGPU,
                MaxIterations = _maxIterations,
                Tolerance = _tolerance,
                EnableDamageEvolution = _enableDamageEvolution,
                DamageThreshold = _damageThreshold,
                EnableOffloading = _enableOffloading,
                OffloadDirectory = _offloadDirectory,
                PnmDatasetPath = _pnmDatasetPath,
                PermeabilityCsvPath = _permeabilityCsvPath,
                AcousticDatasetPath = _acousticDatasetPath,
                EnableRealTimeVisualization = _enableRealTimeViz,
                VisualizationUpdateInterval = _vizUpdateInterval
            };

            // Load integrated data
            await LoadIntegratedDataAsync(_params, dataset);

            // Extract volume data
            _progressDialog.Update(0.1f, "Extracting volume labels...");
            var labels = await ExtractLabelsAsync(dataset, extent);

            _progressDialog.Update(0.2f, "Extracting density field...");
            var density = await ExtractDensityAsync(dataset, extent);

            // Run simulation
            _progressDialog.Update(0.3f, "Running simulation...");

            var progress = new Progress<float>(p =>
            {
                var status = p < 0.4f ? "Initializing..." :
                    p < 0.8f ? "Equilibrating stress field..." :
                    p < 0.9f ? "Calculating principal stresses..." :
                    "Evaluating failure...";
                _progressDialog.Update(0.3f + p * 0.7f, status);
            });

            GeomechanicalResults results;

            if (_useGPU)
            {
                using var simulator = new GeomechanicalSimulatorGPU(_params);
                results = await Task.Run(() =>
                    simulator.Simulate(labels, density, progress, _cancellationTokenSource.Token));
            }
            else
            {
                var simulator = new GeomechanicalSimulatorCPU(_params);
                results = await Task.Run(() =>
                    simulator.Simulate(labels, density, progress, _cancellationTokenSource.Token));
            }

            _lastResults = results;

            // Update visualization
            if (_enableRealTimeViz) UpdateVisualization(dataset, results);

            _progressDialog.Close();
            Logger.Log($"[Geomechanics] Simulation complete: {results.FailedVoxelPercentage:F2}% failure");
        }
        catch (OperationCanceledException)
        {
            _progressDialog.Close();
            Logger.Log("[Geomechanics] Simulation cancelled");
        }
        catch (Exception ex)
        {
            _progressDialog.Close();
            Logger.LogError($"[Geomechanics] Simulation failed: {ex.Message}");
        }
        finally
        {
            _isSimulating = false;
        }
    }

    private async Task LoadIntegratedDataAsync(GeomechanicalParameters parameters,
        CtImageStackDataset dataset)
    {
        // PNM integration
        if (!string.IsNullOrEmpty(parameters.PnmDatasetPath))
            try
            {
                Logger.Log($"[Geomechanics] Loading PNM data from '{parameters.PnmDatasetPath}' for integration...");

                var pnmLoader = new PNMLoader { FilePath = parameters.PnmDatasetPath };
                var pnmDataset = await pnmLoader.LoadAsync(null) as PNMDataset;

                if (pnmDataset != null)
                {
                    double totalPoreVolume = pnmDataset.Pores.Sum(p => p.VolumePhysical);
                    var totalImageVolume = pnmDataset.ImageWidth * pnmDataset.ImageHeight * pnmDataset.ImageDepth *
                                           Math.Pow(pnmDataset.VoxelSize, 3);

                    if (totalImageVolume > 0)
                    {
                        var porosity = totalPoreVolume / totalImageVolume;
                        Logger.Log(
                            $"[Geomechanics] PNM data processed. Calculated Porosity: {porosity:P2}. Permeability: {pnmDataset.DarcyPermeability} mD.");
                    }
                }
                else
                {
                    Logger.LogWarning("[Geomechanics] Failed to cast loaded PNM dataset.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Geomechanics] Failed to load or process PNM data: {ex.Message}");
            }

        // Permeability CSV integration
        if (!string.IsNullOrEmpty(parameters.PermeabilityCsvPath))
            try
            {
                Logger.Log($"[Geomechanics] Loading permeability CSV from '{parameters.PermeabilityCsvPath}'...");
                if (!File.Exists(parameters.PermeabilityCsvPath))
                    throw new FileNotFoundException("CSV file not found.", parameters.PermeabilityCsvPath);

                var materialPropertiesFromCsv = new Dictionary<byte, (float permeability, float porosity)>();
                var lines = await File.ReadAllLinesAsync(parameters.PermeabilityCsvPath,
                    _cancellationTokenSource.Token);

                foreach (var line in lines.Skip(1))
                {
                    if (_cancellationTokenSource.IsCancellationRequested) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(',');
                    if (parts.Length >= 3 &&
                        byte.TryParse(parts[0].Trim(), out var materialId) &&
                        float.TryParse(parts[1].Trim(), out var permeability) &&
                        float.TryParse(parts[2].Trim(), out var porosity))
                        materialPropertiesFromCsv[materialId] = (permeability, porosity);
                }

                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    Logger.Log(
                        $"[Geomechanics] Successfully processed {materialPropertiesFromCsv.Count} records from permeability CSV.");

                    foreach (var selectedId in _selectedMaterialIDs)
                        if (materialPropertiesFromCsv.TryGetValue(selectedId, out var props))
                            Logger.Log(
                                $"[Geomechanics] Properties found for selected Material ID {selectedId}: Permeability={props.permeability} mD, Porosity={props.porosity:P2}.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Geomechanics] Failed to load or parse permeability CSV: {ex.Message}");
            }

        // Acoustic volume integration
        if (!string.IsNullOrEmpty(parameters.AcousticDatasetPath))
            try
            {
                Logger.Log($"[Geomechanics] Loading acoustic volume data from '{parameters.AcousticDatasetPath}'...");

                var acousticLoader = new AcousticVolumeLoader { DirectoryPath = parameters.AcousticDatasetPath };
                var acousticDataset = await acousticLoader.LoadAsync(null) as AcousticVolumeDataset;

                if (acousticDataset?.DensityData != null)
                {
                    Logger.Log(
                        "[Geomechanics] Acoustic data loaded. Calculating average elastic properties...");

                    var densityVolume = acousticDataset.DensityData;
                    var avgYoungsModulusPa = densityVolume.GetMeanYoungsModulus();
                    var avgPoissonRatio = densityVolume.GetMeanPoissonRatio();

                    if (avgYoungsModulusPa > 0)
                    {
                        var avgYoungsModulusMPa = avgYoungsModulusPa / 1e6f;

                        Logger.Log($"[Geomechanics] Overriding properties with acoustic data: " +
                                   $"Young's Modulus = {avgYoungsModulusMPa:F0} MPa, Poisson's Ratio = {avgPoissonRatio:F3}");

                        parameters.YoungModulus = avgYoungsModulusMPa;
                        _youngModulus = avgYoungsModulusMPa;

                        parameters.PoissonRatio = avgPoissonRatio;
                        _poissonRatio = avgPoissonRatio;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[Geomechanics] Failed to load or process acoustic data: {ex.Message}");
            }
    }


    private async Task<byte[,,]> ExtractLabelsAsync(CtImageStackDataset dataset, BoundingBox extent)
    {
        return await Task.Run(() =>
        {
            var labels = new byte[extent.Width, extent.Height, extent.Depth];

            Parallel.For(0, extent.Depth, z =>
            {
                for (var y = 0; y < extent.Height; y++)
                for (var x = 0; x < extent.Width; x++)
                    labels[x, y, z] = dataset.LabelData[
                        extent.MinX + x,
                        extent.MinY + y,
                        extent.MinZ + z];
            });

            return labels;
        });
    }

    private async Task<float[,,]> ExtractDensityAsync(CtImageStackDataset dataset, BoundingBox extent)
    {
        return await Task.Run(() =>
        {
            var density = new float[extent.Width, extent.Height, extent.Depth];
            var materialDensity = dataset.Materials.ToDictionary(m => m.ID, m => (float)m.Density * 1000f);

            Parallel.For(0, extent.Depth, z =>
            {
                for (var y = 0; y < extent.Height; y++)
                for (var x = 0; x < extent.Width; x++)
                {
                    var label = dataset.LabelData[extent.MinX + x, extent.MinY + y, extent.MinZ + z];
                    density[x, y, z] = materialDensity.GetValueOrDefault(label, 2700f);
                }
            });

            return density;
        });
    }

    private void UpdateVisualization(CtImageStackDataset dataset, GeomechanicalResults results)
    {
        var mask = new byte[dataset.Width * dataset.Height * dataset.Depth];
        var extent = results.Parameters.SimulationExtent;

        Parallel.For(0, extent.Depth, z =>
        {
            for (var y = 0; y < extent.Height; y++)
            for (var x = 0; x < extent.Width; x++)
            {
                var globalX = extent.MinX + x;
                var globalY = extent.MinY + y;
                var globalZ = extent.MinZ + z;

                var idx = (globalZ * dataset.Height + globalY) * dataset.Width + globalX;
                mask[idx] = results.DamageField[x, y, z];
            }
        });

        var color = new Vector4(1, 0, 0, 0.7f);
        CtImageStackTools.Update3DPreviewFromExternal(dataset, mask, color);
    }
}