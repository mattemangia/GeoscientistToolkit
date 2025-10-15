// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulationUI.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Loaders;
using GeoscientistToolkit.Data.Materials;
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
    private readonly FluidGeothermalVisualizationRenderer _fluidGeothermalRenderer;
    private readonly MohrCircleRenderer _mohrRenderer;
    private readonly ImGuiExportFileDialog _offloadDirectoryDialog;
    private readonly ImGuiExportFileDialog _permeabilityFileDialog;
    private readonly ImGuiExportFileDialog _pnmFileDialog;
    private readonly ProgressBarDialog _progressDialog;

    // Material selection
    private readonly HashSet<byte> _selectedMaterialIDs = new();
    private string _acousticDatasetPath = "";
    private float _aquiferPermeability = 1e-15f;
    private float _aquiferPressure = 15f;
    private bool _autoCropToSelection = true;
    private float _biotCoefficient = 0.8f;
    private CancellationTokenSource _cancellationTokenSource;
    private float _cohesion = 10f;

    // UI State
    private CtImageStackDataset _currentDataset;
    private float _damageThreshold = 0.8f;
    private float _density = 2700f;
    private float _dilationAngle = 10f;

    private bool _enableAquifer;
    private bool _enableDamageEvolution = true;

    private bool _enableFluidInjection;

    private bool _enableFractureFlow = true;

    private bool _enableGeothermal;
    private bool _enableMultiMaterial;

    // Memory management for huge datasets
    private bool _enableOffloading;

    // Real-time visualization
    private bool _enableRealTimeViz = true;

    // Failure criterion
    private int _failureCriterionIndex; // Mohr-Coulomb
    private float _fluidDensity = 1000f;
    private float _fluidTimeStep = 1f;
    private float _fluidViscosity = 1e-3f;
    private float _fractureApertureCoeff = 1e-6f;
    private float _frictionAngle = 30f;
    private float _geothermalGradient = 25f;
    private float _hoekBrown_a = 0.5f;
    private float _hoekBrown_mb = 1.5f;

    // Hoek-Brown parameters
    private float _hoekBrown_mi = 10f;
    private float _hoekBrown_s = 0.004f;
    private Vector3 _injectionLocation = new(0.5f, 0.5f, 0.5f);
    private float _injectionPressure = 50f;
    private int _injectionRadius = 5;
    private float _injectionRate = 0.1f;
    private bool _isCalculatingExtent;
    private bool _isSimulating;
    private GeomechanicalResults _lastResults;

    // Loading conditions
    private int _loadingModeIndex = 2; // Triaxial

    // Material library browser
    private string _materialLibrarySearchFilter = "";

    private int _maxIterations = 1000;
    private float _maxSimTime = 3600f;
    private float _minFractureAperture = 1e-6f;
    private string _offloadDirectory = "";
    private GeomechanicalParameters _params;
    private string _permeabilityCsvPath = "";

    // Integration
    private string _pnmDatasetPath = "";
    private float _poissonRatio = 0.25f;
    private float _porePressure = 10f;
    private float _porosity = 0.10f;
    private float _rockPermeability = 1e-18f;
    private PhysicalMaterial _selectedLibraryMaterial;
    private int _selectedMaterialIndex;
    private bool _showFluidTimeSeries;
    private bool _showMaterialLibraryBrowser;

    // Mohr circle viewer
    private bool _showMohrCircles;
    private float _sigma1 = 100f;
    private Vector3 _sigma1Direction = new(0, 0, 1);
    private float _sigma2 = 50f;
    private float _sigma3 = 20f;

    // Simulation extent
    private BoundingBox _simulationExtent;
    private float _surfaceTemperature = 15f;
    private float _tensileStrength = 5f;
    private float _thermalExpansion = 10e-6f;
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
        _fluidGeothermalRenderer = new FluidGeothermalVisualizationRenderer();
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
        _fluidGeothermalRenderer?.Dispose();
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

        // Geothermal Settings
        if (ImGui.CollapsingHeader("Geothermal Settings")) DrawGeothermalSettings();
        ImGui.Separator();

        // Fluid Injection Settings (FIXED - was duplicating "Geothermal Settings")
        if (ImGui.CollapsingHeader("Fluid Injection Settings")) DrawFluidInjectionSettings();
        ImGui.Separator();

        // Memory Management
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
        DrawFluidTimeSeriesWindow();

        // Material library browser
        if (_showMaterialLibraryBrowser) DrawMaterialLibraryBrowser();
    }

    private void DrawFluidGeothermalVisualizationWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);

        if (ImGui.BeginPopupModal("FluidGeothermalViz", ImGuiWindowFlags.None))
        {
            _fluidGeothermalRenderer.DrawVisualization(_lastResults, _params);

            ImGui.Spacing();
            ImGui.Separator();

            if (ImGui.Button("Close", new Vector2(-1, 0)))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
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

        // Button to browse material library
        if (ImGui.Button("Browse Material Library", new Vector2(-1, 0))) _showMaterialLibraryBrowser = true;

        ImGui.Spacing();
        ImGui.Separator();

        if (primaryMaterial != null && !string.IsNullOrEmpty(primaryMaterial.PhysicalMaterialName))
        {
            var physMat = MaterialLibrary.Instance.Find(primaryMaterial.PhysicalMaterialName);
            if (physMat != null)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1),
                    $"Physical Material: {physMat.Name}");

                if (ImGui.Button("Apply Library Values")) ApplyLibraryMaterial(physMat);

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

        ImGui.Text($"Shear Modulus: {mu / 1e6f:F0} MPa");
        ImGui.Text($"Bulk Modulus: {K / 1e6f:F0} MPa");

        ImGui.Unindent();
    }

    private void DrawMaterialLibraryBrowser()
    {
        ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);
        var isOpen = true;

        if (ImGui.Begin("Material Library Browser##Geomech", ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.TextWrapped("Select a material to apply its properties to the simulation.");
            ImGui.Separator();

            // Search filter
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##search", "Search materials...", ref _materialLibrarySearchFilter, 256);
            ImGui.Spacing();

            // Two-column layout
            if (ImGui.BeginTable("MatLibTable", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Materials", ImGuiTableColumnFlags.WidthFixed, 300);
                ImGui.TableSetupColumn("Properties", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                // Material list
                if (ImGui.BeginChild("MatLibList", new Vector2(0, -80), ImGuiChildFlags.Border))
                {
                    var materials = MaterialLibrary.Instance.Materials
                        .Where(m => string.IsNullOrEmpty(_materialLibrarySearchFilter) ||
                                    m.Name.Contains(_materialLibrarySearchFilter, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(m => m.Phase)
                        .ThenBy(m => m.Name)
                        .ToList();

                    var currentPhase = "";
                    foreach (var mat in materials)
                    {
                        if (mat.Phase.ToString() != currentPhase)
                        {
                            currentPhase = mat.Phase.ToString();
                            ImGui.SeparatorText(currentPhase);
                        }

                        var isSelected = _selectedLibraryMaterial == mat;
                        if (ImGui.Selectable($"{mat.Name}##{mat.Name}", isSelected)) _selectedLibraryMaterial = mat;
                    }
                }

                ImGui.EndChild();

                ImGui.TableNextColumn();

                // Property details
                if (ImGui.BeginChild("MatLibProps", new Vector2(0, -80), ImGuiChildFlags.Border))
                {
                    if (_selectedLibraryMaterial != null)
                    {
                        var mat = _selectedLibraryMaterial;

                        ImGui.TextColored(new Vector4(0.5f, 1, 1, 1), mat.Name);
                        ImGui.TextDisabled($"Phase: {mat.Phase}");

                        if (!string.IsNullOrEmpty(mat.Notes))
                        {
                            ImGui.Spacing();
                            ImGui.PushTextWrapPos();
                            ImGui.TextWrapped(mat.Notes);
                            ImGui.PopTextWrapPos();
                        }

                        ImGui.Spacing();
                        ImGui.SeparatorText("Mechanical Properties");

                        if (ImGui.BeginTable("Props", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                        {
                            ImGui.TableSetupColumn("Property");
                            ImGui.TableSetupColumn("Value");
                            ImGui.TableHeadersRow();

                            void AddRow(string name, string value)
                            {
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                ImGui.Text(name);
                                ImGui.TableNextColumn();
                                ImGui.Text(value);
                            }

                            if (mat.Density_kg_m3.HasValue)
                                AddRow("Density", $"{mat.Density_kg_m3:F0} kg/m³");
                            if (mat.YoungModulus_GPa.HasValue)
                                AddRow("Young's Modulus", $"{mat.YoungModulus_GPa * 1000:F0} MPa");
                            if (mat.PoissonRatio.HasValue)
                                AddRow("Poisson Ratio", $"{mat.PoissonRatio:F3}");
                            if (mat.CompressiveStrength_MPa.HasValue)
                                AddRow("Compressive Strength", $"{mat.CompressiveStrength_MPa:F1} MPa");
                            if (mat.TensileStrength_MPa.HasValue)
                                AddRow("Tensile Strength", $"{mat.TensileStrength_MPa:F1} MPa");
                            if (mat.FrictionAngle_deg.HasValue)
                                AddRow("Friction Angle", $"{mat.FrictionAngle_deg:F1}°");
                            if (mat.MohsHardness.HasValue)
                                AddRow("Mohs Hardness", $"{mat.MohsHardness:F1}");

                            ImGui.EndTable();
                        }

                        // Preview what will be applied
                        ImGui.Spacing();
                        ImGui.SeparatorText("Will Apply");
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 1, 0.5f, 1));
                        if (mat.YoungModulus_GPa.HasValue)
                            ImGui.BulletText($"Young's Modulus: {mat.YoungModulus_GPa * 1000:F0} MPa");
                        if (mat.PoissonRatio.HasValue)
                            ImGui.BulletText($"Poisson Ratio: {mat.PoissonRatio:F3}");
                        if (mat.Density_kg_m3.HasValue)
                            ImGui.BulletText($"Density: {mat.Density_kg_m3:F0} kg/m³");
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        ImGui.TextDisabled("Select a material to view properties");
                    }
                }

                ImGui.EndChild();

                ImGui.EndTable();
            }

            ImGui.Separator();

            // Action buttons
            ImGui.BeginDisabled(_selectedLibraryMaterial == null);
            if (ImGui.Button("Apply Material Properties", new Vector2(-130, 0)))
                if (_selectedLibraryMaterial != null)
                {
                    ApplyLibraryMaterial(_selectedLibraryMaterial);
                    _showMaterialLibraryBrowser = false;
                }

            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Close", new Vector2(120, 0))) _showMaterialLibraryBrowser = false;
        }

        ImGui.End();

        if (!isOpen) _showMaterialLibraryBrowser = false;
    }

    private void ApplyLibraryMaterial(PhysicalMaterial physMat)
    {
        // Apply mechanical properties
        if (physMat.YoungModulus_GPa.HasValue)
        {
            _youngModulus = (float)(physMat.YoungModulus_GPa.Value * 1000); // GPa to MPa
            Logger.Log($"[Geomechanics] Applied Young's Modulus: {_youngModulus:F0} MPa");
        }

        if (physMat.PoissonRatio.HasValue)
        {
            _poissonRatio = (float)physMat.PoissonRatio.Value;
            Logger.Log($"[Geomechanics] Applied Poisson Ratio: {_poissonRatio:F3}");
        }

        if (physMat.Density_kg_m3.HasValue)
        {
            _density = (float)physMat.Density_kg_m3.Value;
            Logger.Log($"[Geomechanics] Applied Density: {_density:F0} kg/m³");
        }

        // Estimate strength parameters from rock type
        if (physMat.Name.Contains("Sandstone", StringComparison.OrdinalIgnoreCase))
        {
            _cohesion = 20f;
            _frictionAngle = 35f;
            _tensileStrength = 5f;
            Logger.Log("[Geomechanics] Applied typical sandstone strength parameters");
        }
        else if (physMat.Name.Contains("Limestone", StringComparison.OrdinalIgnoreCase))
        {
            _cohesion = 30f;
            _frictionAngle = 40f;
            _tensileStrength = 8f;
            Logger.Log("[Geomechanics] Applied typical limestone strength parameters");
        }
        else if (physMat.Name.Contains("Granite", StringComparison.OrdinalIgnoreCase))
        {
            _cohesion = 50f;
            _frictionAngle = 55f;
            _tensileStrength = 15f;
            Logger.Log("[Geomechanics] Applied typical granite strength parameters");
        }
        else if (physMat.Name.Contains("Shale", StringComparison.OrdinalIgnoreCase))
        {
            _cohesion = 15f;
            _frictionAngle = 25f;
            _tensileStrength = 2f;
            Logger.Log("[Geomechanics] Applied typical shale strength parameters");
        }
        else if (physMat.Name.Contains("Basalt", StringComparison.OrdinalIgnoreCase))
        {
            _cohesion = 40f;
            _frictionAngle = 50f;
            _tensileStrength = 15f;
            Logger.Log("[Geomechanics] Applied typical basalt strength parameters");
        }
        else
        {
            // Use library values if available
            if (physMat.CompressiveStrength_MPa.HasValue)
                // Estimate cohesion from UCS (rough approximation)
                _cohesion = (float)(physMat.CompressiveStrength_MPa.Value * 0.25);

            if (physMat.FrictionAngle_deg.HasValue) _frictionAngle = (float)physMat.FrictionAngle_deg.Value;

            if (physMat.TensileStrength_MPa.HasValue) _tensileStrength = (float)physMat.TensileStrength_MPa.Value;
        }

        Logger.Log($"[Geomechanics] Applied properties from library material: {physMat.Name}");
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
        ImGui.Text("Failure Criterion:");

        string[] criteria = { "Mohr-Coulomb", "Drucker-Prager", "Hoek-Brown", "Griffith" };

        // Calculate this once at the top since multiple cases need it
        var phi_rad = _frictionAngle * MathF.PI / 180f;

        // Make the combo box more prominent
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##FailureCriterion", ref _failureCriterionIndex, criteria, criteria.Length))
            Logger.Log($"[Geomechanics] Failure criterion changed to: {criteria[_failureCriterionIndex]}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Visual indicator of selected criterion
        var selectedColor = new Vector4(0.3f, 0.8f, 1f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Text, selectedColor);
        ImGui.Text($"Selected: {criteria[_failureCriterionIndex]}");
        ImGui.PopStyleColor();

        ImGui.Spacing();

        switch (_failureCriterionIndex)
        {
            case 0: // Mohr-Coulomb
                ImGui.BeginChild("MohrCoulombParams", new Vector2(0, 120), ImGuiChildFlags.Border);
                ImGui.TextWrapped("Linear failure envelope in τ-σ space:");
                ImGui.TextColored(new Vector4(1f, 1f, 0.7f, 1f), "τ = c + σn·tan(φ)");
                ImGui.Spacing();
                ImGui.Text("Using parameters:");
                ImGui.BulletText($"Cohesion: {_cohesion} MPa");
                ImGui.BulletText($"Friction Angle: {_frictionAngle}°");
                ImGui.Spacing();
                ImGui.TextWrapped("Best for: Rocks, soils, general geomaterials");
                ImGui.EndChild();
                break;

            case 1: // Drucker-Prager
                ImGui.BeginChild("DruckerPragerParams", new Vector2(0, 150), ImGuiChildFlags.Border);
                ImGui.TextWrapped("Smooth conical surface in principal stress space:");
                ImGui.TextColored(new Vector4(1f, 1f, 0.7f, 1f), "√J₂ = α·I₁ + k");
                ImGui.Spacing();
                ImGui.Text("Derived from:");
                ImGui.BulletText($"Cohesion: {_cohesion} MPa");
                ImGui.BulletText($"Friction Angle: {_frictionAngle}°");
                ImGui.Spacing();
                var alpha_dp = 2 * MathF.Sin(phi_rad) / (3 - MathF.Sin(phi_rad));
                var k_dp = 6 * _cohesion * MathF.Cos(phi_rad) / (3 - MathF.Sin(phi_rad));
                ImGui.Text($"α = {alpha_dp:F3}, k = {k_dp:F2} MPa");
                ImGui.Spacing();
                ImGui.TextWrapped("Best for: Smooth 3D stress states, metal plasticity");
                ImGui.EndChild();
                break;

            case 2: // Hoek-Brown
                ImGui.BeginChild("HoekBrownParams", new Vector2(0, 250), ImGuiChildFlags.Border);
                ImGui.TextWrapped("Non-linear empirical criterion for rock masses:");
                ImGui.TextColored(new Vector4(1f, 1f, 0.7f, 1f), "σ₁ = σ₃ + σci·(mb·σ₃/σci + s)^a");
                ImGui.Spacing();

                ImGui.Text("Rock Mass Parameters:");
                ImGui.Separator();
                ImGui.DragFloat("Material constant mi", ref _hoekBrown_mi, 0.5f, 1f, 30f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Intact rock: 5-10 (weak), 10-20 (medium), 20-30 (strong)");

                ImGui.DragFloat("Reduced constant mb", ref _hoekBrown_mb, 0.1f, 0.1f, 10f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("mb = mi · exp((GSI-100)/(28-14D))");

                ImGui.DragFloat("Rock mass parameter s", ref _hoekBrown_s, 0.001f, 0f, 1f, "%.4f");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Intact: s=1, Heavily fractured: s→0");

                ImGui.DragFloat("Exponent a", ref _hoekBrown_a, 0.01f, 0.5f, 0.65f, "%.3f");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Intact: a=0.5, Disturbed: a=0.65");

                ImGui.Spacing();
                var ucs_hb = 2 * _cohesion * MathF.Cos(phi_rad) / (1 - MathF.Sin(phi_rad));
                ImGui.Text($"Implied UCS: {ucs_hb:F1} MPa");
                ImGui.TextWrapped("Best for: Fractured rock masses, tunneling");
                ImGui.EndChild();
                break;

            case 3: // Griffith
                ImGui.BeginChild("GriffithParams", new Vector2(0, 140), ImGuiChildFlags.Border);
                ImGui.TextWrapped("Tensile crack propagation criterion:");
                ImGui.TextColored(new Vector4(1f, 1f, 0.7f, 1f), "(σ₁-σ₃)² = 8T₀(σ₁+σ₃)");
                ImGui.Spacing();
                ImGui.Text("Using parameter:");
                ImGui.BulletText($"Tensile Strength: {_tensileStrength} MPa");
                ImGui.Spacing();
                ImGui.TextWrapped("Parabolic envelope, accounts for crack closure in compression");
                ImGui.Spacing();
                ImGui.TextWrapped("Best for: Brittle materials, tensile failure, low confining pressure");
                ImGui.EndChild();
                break;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Common parameter (applies to all)
        ImGui.Text("Post-Failure Behavior:");
        ImGui.DragFloat("Dilation Angle (°)", ref _dilationAngle, 1f, 0f, _frictionAngle);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Volume expansion angle during shear failure (0° = no dilation, typically φ/3 to φ)");

        ImGui.Spacing();

        // Visual comparison helper
        if (ImGui.TreeNode("Criterion Comparison Guide"))
        {
            ImGui.TextWrapped("Quick Selection Guide:");
            ImGui.Spacing();

            ImGui.BulletText("Mohr-Coulomb: Most common, simple, conservative");
            ImGui.BulletText("Drucker-Prager: Smooth surface, better for numerical stability");
            ImGui.BulletText("Hoek-Brown: Rock masses with joints/fractures");
            ImGui.BulletText("Griffith: Brittle materials, tensile-dominated failure");

            ImGui.TreePop();
        }
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
            ImGui.SetTooltip("Offloads chunk data to disk when memory is constrained.\n" +
                             "Recommended for datasets requiring >16 GB RAM.\n" +
                             "May reduce performance but allows processing huge volumes.");

        if (_enableOffloading)
        {
            ImGui.Indent();
            ImGui.Spacing();

            ImGui.Text("Offload Directory:");
            ImGui.SameLine();
            if (ImGui.Button("Browse##Offload")) _offloadDirectoryDialog.Open("", _offloadDirectory);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Navigate to the desired directory and click 'Export' to select it.\n" +
                                 "You can create new folders using the 'New Folder' button.");

            ImGui.SameLine();
            if (ImGui.Button("Clear##Offload")) ClearOffloadCache();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Delete all temporary files in the offload directory.\n" +
                                 "Safe to use when no simulation is running.");

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
                            ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1),
                                $"Cache size: {cacheSizeGB:F2} GB");

                        if (freeSpace < 50)
                            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1),
                                "⚠ Warning: Low disk space!");
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
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"[Geomechanics] Could not delete file {file}: {ex.Message}");
                    }

                // Remove empty subdirectories
                var directories = Directory.GetDirectories(_offloadDirectory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length); // Delete deepest first

                foreach (var dir in directories)
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"[Geomechanics] Could not delete directory {dir}: {ex.Message}");
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
            ImGui.Text($"Mean Stress: {_lastResults.MeanStress / 1e6f:F2} MPa");
            ImGui.Text($"Max Shear Stress: {_lastResults.MaxShearStress / 1e6f:F2} MPa");
            ImGui.Unindent();

            ImGui.Spacing();
            ImGui.Text("Failure Statistics:");
            ImGui.Indent();
            ImGui.Text($"Total Voxels: {_lastResults.TotalVoxels:N0}");
            ImGui.Text($"Failed Voxels: {_lastResults.FailedVoxels:N0}");
            ImGui.Text($"Failure Percentage: {_lastResults.FailedVoxelPercentage:F2}%");
            ImGui.Unindent();

            // Add geothermal and fluid results
            DrawFluidGeothermalResults();

            ImGui.Spacing();

            if (ImGui.Button("View Mohr Circles", new Vector2(-1, 0)))
                _showMohrCircles = true;

            // Add button for fluid/geothermal visualization if data exists
            if (_lastResults.TemperatureField != null || _lastResults.PressureField != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Fluid/Thermal Viz", new Vector2(-1, 0))) ImGui.OpenPopup("FluidGeothermalViz");

                DrawFluidGeothermalVisualizationWindow();
            }

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
                    _offloadDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GeoscientistToolkit", "GeomechOffload");

                if (!Directory.Exists(_offloadDirectory))
                    Directory.CreateDirectory(_offloadDirectory);
            }

            // Build parameters
            var extent = _autoCropToSelection && _simulationExtent != null
                ? _simulationExtent
                : new BoundingBox(0, 0, 0, dataset.Width, dataset.Height, dataset.Depth);

            _params = new GeomechanicalParameters
            {
                // Basic geometry
                Width = extent.Width,
                Height = extent.Height,
                Depth = extent.Depth,
                PixelSize = dataset.PixelSize,
                SimulationExtent = extent,
                SelectedMaterialIDs = new HashSet<byte>(_selectedMaterialIDs),

                // Material properties
                YoungModulus = _youngModulus,
                PoissonRatio = _poissonRatio,
                Cohesion = _cohesion,
                FrictionAngle = _frictionAngle,
                TensileStrength = _tensileStrength,
                Density = _density,

                // Loading conditions
                LoadingMode = (LoadingMode)_loadingModeIndex,
                Sigma1 = _sigma1,
                Sigma2 = _sigma2,
                Sigma3 = _sigma3,
                Sigma1Direction = _sigma1Direction,

                // Pore pressure
                UsePorePressure = _usePorePressure,
                PorePressure = _porePressure,
                BiotCoefficient = _biotCoefficient,

                // Failure criterion
                FailureCriterion = (FailureCriterion)_failureCriterionIndex,
                DilationAngle = _dilationAngle,

                // Hoek-Brown parameters
                HoekBrown_mi = _hoekBrown_mi,
                HoekBrown_mb = _hoekBrown_mb,
                HoekBrown_s = _hoekBrown_s,
                HoekBrown_a = _hoekBrown_a,

                // Computational settings
                UseGPU = _useGPU,
                MaxIterations = _maxIterations,
                Tolerance = _tolerance,
                EnableDamageEvolution = _enableDamageEvolution,
                DamageThreshold = _damageThreshold,
                EnablePlasticity = false, // Can be added to UI if needed

                // Memory management
                EnableOffloading = _enableOffloading,
                OffloadDirectory = _offloadDirectory,

                // Data integration
                PnmDatasetPath = _pnmDatasetPath,
                PermeabilityCsvPath = _permeabilityCsvPath,
                AcousticDatasetPath = _acousticDatasetPath,

                // Visualization
                EnableRealTimeVisualization = _enableRealTimeViz,
                VisualizationUpdateInterval = _vizUpdateInterval,

                // ========== GEOTHERMAL PARAMETERS ==========
                EnableGeothermal = _enableGeothermal,
                SurfaceTemperature = _surfaceTemperature,
                GeothermalGradient = _geothermalGradient,
                ThermalExpansionCoefficient = _thermalExpansion,
                CalculateEnergyPotential = true,

                // ========== FLUID INJECTION PARAMETERS ==========
                EnableFluidInjection = _enableFluidInjection,
                FluidViscosity = _fluidViscosity,
                FluidDensity = _fluidDensity,
                InitialPorePressure = _usePorePressure ? _porePressure : 10f,
                InjectionPressure = _injectionPressure,
                InjectionRate = _injectionRate,
                InjectionLocation = _injectionLocation,
                InjectionRadius = _injectionRadius,
                MaxSimulationTime = _maxSimTime,
                FluidTimeStep = _fluidTimeStep,

                // ========== FRACTURE MECHANICS ==========
                EnableFractureFlow = _enableFractureFlow,
                FractureAperture_Coefficient = _fractureApertureCoeff,
                MinimumFractureAperture = _minFractureAperture,
                FractureToughness = 1.0f, // Can add to UI
                CriticalStrainEnergy = 100f, // Can add to UI

                // ========== AQUIFER INTERACTION ==========
                EnableAquifer = _enableAquifer,
                AquiferPressure = _aquiferPressure,
                AquiferPermeability = _aquiferPermeability,
                RockPermeability = _rockPermeability,
                Porosity = _porosity,

                // ========== POROELASTIC COUPLING ==========
                EnablePoroelasticity = true,
                SkemptonCoefficient = 0.7f,

                // ========== SIMULATION CONTROL ==========
                FluidIterationsPerMechanicalStep = 10,
                UseSequentialCoupling = true
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
                    p < 0.95f ? "Evaluating failure..." :
                    "Finalizing...";
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
            if (_enableRealTimeViz)
                UpdateVisualization(dataset, results);

            _progressDialog.Close();
            Logger.Log($"[Geomechanics] Simulation complete: {results.FailedVoxelPercentage:F2}% failure");

            if (results.BreakdownPressure > 0)
                Logger.Log($"[Geomechanics] Breakdown pressure: {results.BreakdownPressure:F1} MPa");

            if (results.GeothermalEnergyPotential > 0)
                Logger.Log($"[Geomechanics] Geothermal energy potential: {results.GeothermalEnergyPotential:F2} MWh");
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

    private void DrawFluidGeothermalResults()
    {
        if (_lastResults == null) return;

        // Geothermal Results
        if (_lastResults.TemperatureField != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Geothermal Results:");
            ImGui.Indent();
            ImGui.Text($"Average Gradient: {_lastResults.AverageThermalGradient:F1} °C/km");
            ImGui.Text($"Energy Potential: {_lastResults.GeothermalEnergyPotential:F2} MWh");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Recoverable thermal energy assuming 10% extraction efficiency");
            ImGui.Unindent();
        }

        // Fluid Injection Results
        if (_lastResults.PressureField != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Hydraulic Fracturing Results:");
            ImGui.Indent();
            ImGui.Text($"Breakdown Pressure: {_lastResults.BreakdownPressure:F1} MPa");
            ImGui.Text($"Propagation Pressure: {_lastResults.PropagationPressure:F1} MPa");
            ImGui.Text($"Peak Injection Pressure: {_lastResults.PeakInjectionPressure:F1} MPa");
            ImGui.Spacing();
            ImGui.Text(
                $"Pressure Range: {_lastResults.MinFluidPressure / 1e6f:F1} - {_lastResults.MaxFluidPressure / 1e6f:F1} MPa");
            ImGui.Text($"Total Fracture Volume: {_lastResults.TotalFractureVolume * 1e6:F2} cm³");
            ImGui.Text($"Fracture Voxel Count: {_lastResults.FractureVoxelCount:N0}");
            ImGui.Text($"Fracture Network Segments: {_lastResults.FractureNetwork.Count:N0}");
            ImGui.Unindent();

            // Plot time series
            if (_lastResults.TimePoints != null && _lastResults.TimePoints.Count > 10)
            {
                ImGui.Spacing();
                if (ImGui.Button("View Pressure/Flow History", new Vector2(-1, 0))) _showFluidTimeSeries = true;
            }
        }
    }

    private void DrawFluidTimeSeriesWindow()
    {
        if (!_showFluidTimeSeries || _lastResults == null) return;

        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Fluid Injection History", ref _showFluidTimeSeries))
        {
            if (_lastResults.TimePoints != null && _lastResults.TimePoints.Count > 0)
            {
                ImGui.Text($"Simulation Time: {_lastResults.TimePoints.Last():F1} seconds");
                ImGui.Text($"Data Points: {_lastResults.TimePoints.Count}");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Plot injection pressure
                if (_lastResults.InjectionPressureHistory.Count > 0)
                {
                    ImGui.Text("Injection Pressure vs Time:");
                    var values = _lastResults.InjectionPressureHistory.ToArray();
                    ImGui.PlotLines("##PressureHistory", ref values[0], values.Length, 0,
                        $"Max: {values.Max():F1} MPa", 0f, values.Max() * 1.2f, new Vector2(-1, 150));
                }

                ImGui.Spacing();

                // Plot fracture volume
                if (_lastResults.FractureVolumeHistory.Count > 0)
                {
                    ImGui.Text("Fracture Volume vs Time:");
                    var values = _lastResults.FractureVolumeHistory.ToArray();
                    ImGui.PlotLines("##FractureVolHistory", ref values[0], values.Length, 0,
                        $"Final: {values.Last() * 1e6:F2} cm³", 0f, values.Max() * 1.2f, new Vector2(-1, 150));
                }

                ImGui.Spacing();

                // Plot flow rate
                if (_lastResults.FlowRateHistory.Count > 0)
                {
                    ImGui.Text("Flow Rate vs Time:");
                    var values = _lastResults.FlowRateHistory.ToArray();
                    ImGui.PlotLines("##FlowRateHistory", ref values[0], values.Length, 0,
                        $"Avg: {values.Average():F4} m³/s", 0f, values.Max() * 1.2f, new Vector2(-1, 150));
                }

                // Geothermal energy extraction
                if (_enableGeothermal && _lastResults.EnergyExtractionHistory.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.Text("Energy Extraction Rate vs Time:");
                    var values = _lastResults.EnergyExtractionHistory.ToArray();
                    ImGui.PlotLines("##EnergyHistory", ref values[0], values.Length, 0,
                        $"Avg: {values.Average():F2} MW", 0f, values.Max() * 1.2f, new Vector2(-1, 150));
                }
            }
            else
            {
                ImGui.Text("No time series data available.");
            }
        }

        ImGui.End();
    }

    private void DrawGeothermalSettings()
    {
        ImGui.Checkbox("Enable Geothermal Gradient", ref _enableGeothermal);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Simulates temperature increasing with depth and thermal effects on rock");

        if (_enableGeothermal)
        {
            ImGui.Indent();
            ImGui.Spacing();

            ImGui.DragFloat("Surface Temperature (°C)", ref _surfaceTemperature, 1f, -20f, 50f);
            ImGui.DragFloat("Geothermal Gradient (°C/km)", ref _geothermalGradient, 1f, 10f, 100f);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Typical values: 25-30 °C/km (normal crust), 40-80 °C/km (geothermal areas)");

            ImGui.DragFloat("Thermal Expansion Coef. (1/K)", ref _thermalExpansion, 1e-7f, 1e-6f, 50e-6f, "%.2e");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1),
                "Calculates thermal stress and geothermal energy potential");
            ImGui.Unindent();
        }
    }

    private void DrawFluidInjectionSettings()
    {
        ImGui.Checkbox("Enable Fluid Injection", ref _enableFluidInjection);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Simulates fluid injection, pressure diffusion, and hydraulic fracturing");

        if (_enableFluidInjection)
        {
            ImGui.Indent();
            ImGui.Spacing();

            // Fluid Properties
            if (ImGui.TreeNode("Fluid Properties"))
            {
                ImGui.DragFloat("Viscosity (Pa·s)", ref _fluidViscosity, 1e-4f, 1e-4f, 1e-1f, "%.2e");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Water ≈ 1e-3 Pa·s, Oil ≈ 1e-2 Pa·s");

                ImGui.DragFloat("Density (kg/m³)", ref _fluidDensity, 10f, 800f, 1200f);
                ImGui.TreePop();
            }

            // Injection Parameters
            if (ImGui.TreeNode("Injection Parameters"))
            {
                ImGui.DragFloat("Injection Pressure (MPa)", ref _injectionPressure, 1f, 10f, 200f);
                ImGui.DragFloat("Injection Rate (m³/s)", ref _injectionRate, 0.01f, 0.001f, 1f, "%.3f");

                ImGui.Spacing();
                ImGui.Text("Injection Location (normalized 0-1):");
                ImGui.Indent();
                var loc = _injectionLocation;
                ImGui.DragFloat("X", ref loc.X, 0.01f, 0f, 1f);
                ImGui.DragFloat("Y", ref loc.Y, 0.01f, 0f, 1f);
                ImGui.DragFloat("Z", ref loc.Z, 0.01f, 0f, 1f);
                _injectionLocation = loc;
                ImGui.Unindent();

                ImGui.DragInt("Injection Radius (voxels)", ref _injectionRadius, 1, 1, 20);

                ImGui.Spacing();
                ImGui.DragFloat("Max Simulation Time (s)", ref _maxSimTime, 100f, 60f, 86400f);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("1 hour = 3600s, 1 day = 86400s");

                ImGui.DragFloat("Fluid Time Step (s)", ref _fluidTimeStep, 0.1f, 0.1f, 10f);
                ImGui.TreePop();
            }

            // Fracture Mechanics
            if (ImGui.TreeNode("Fracture Mechanics"))
            {
                ImGui.Checkbox("Enable Fracture Flow", ref _enableFractureFlow);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Fluid flows preferentially through fractures (enhanced permeability)");

                if (_enableFractureFlow)
                {
                    ImGui.Indent();
                    ImGui.DragFloat("Aperture Coefficient (m/MPa)", ref _fractureApertureCoeff, 1e-7f, 1e-7f, 1e-5f,
                        "%.2e");
                    ImGui.DragFloat("Min Aperture (m)", ref _minFractureAperture, 1e-7f, 1e-7f, 1e-5f, "%.2e");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Typical: 1-10 µm for induced fractures");
                    ImGui.Unindent();
                }

                ImGui.TreePop();
            }

            // Rock Properties
            if (ImGui.TreeNode("Porous Media Properties"))
            {
                ImGui.DragFloat("Rock Permeability (m²)", ref _rockPermeability, 1e-19f, 1e-21f, 1e-12f, "%.2e");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("1e-18 m² ≈ 1 mD (typical tight rock)\n1e-15 m² ≈ 1000 mD (sandstone)");

                ImGui.DragFloat("Porosity", ref _porosity, 0.01f, 0.01f, 0.40f, "%.2f");
                ImGui.TreePop();
            }

            // Aquifer Boundary
            if (ImGui.TreeNode("Aquifer Interaction"))
            {
                ImGui.Checkbox("Enable Aquifer Boundary", ref _enableAquifer);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Exterior voxels act as constant-pressure reservoir (infinite aquifer)");

                if (_enableAquifer)
                {
                    ImGui.Indent();
                    ImGui.DragFloat("Aquifer Pressure (MPa)", ref _aquiferPressure, 1f, 5f, 50f);
                    ImGui.DragFloat("Aquifer Permeability (m²)", ref _aquiferPermeability, 1e-16f, 1e-18f, 1e-12f,
                        "%.2e");
                    ImGui.Unindent();
                }

                ImGui.TreePop();
            }

            ImGui.Spacing();
            ImGui.TextWrapped("⚠ Note: Fluid simulation adds significant computation time.");
            ImGui.TextWrapped("Fractures form when effective stress (σ - αP) exceeds failure criterion.");

            ImGui.Unindent();
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