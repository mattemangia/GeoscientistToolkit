// GeoscientistToolkit/Analysis/Geomechanics/TriaxialSimulationTool.cs
// Interactive triaxial compression/extension testing tool with comprehensive UI
//
// FEATURES:
// - Material selection from PhysicalMaterialLibrary
// - Mesh generation controls with preview
// - Loading parameter curves (confining pressure, axial load)
// - Real-time simulation with visualization
// - Stress-strain plots
// - Mohr circle visualization with TANGENT failure envelope
// - 3D visualization of deformed mesh and fracture planes
// - Export results to CSV

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.UI;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public class TriaxialSimulationTool : IDisposable
{
    // Simulation components
    private TriaxialSimulation _simulation;
    private TriaxialMeshGenerator.TriaxialMesh _mesh;
    private TriaxialResults _results;
    private PhysicalMaterial _selectedMaterial;

    // UI state
    private bool _isOpen = false;
    private bool _showMaterialSelector;
    private bool _showMeshSettings = true;
    private bool _showLoadingParameters = true;
    private bool _showSimulationControls = true;
    private bool _showResults;
    private bool _showMohrCircle;
    private bool _show3DView = true;

    // Mesh parameters
    private float _cylinderRadius_mm = 25.0f;  // Standard 50mm diameter sample
    private float _cylinderHeight_mm = 100.0f; // Standard 2:1 height/diameter ratio
    private int _meshDensityRadial = 6;
    private int _meshDensityCircumferential = 16;
    private int _meshDensityAxial = 20;
    private bool _useCylindricalMesh = true;

    // Loading parameters
    private TriaxialLoadingParameters _loadParams = new();
    private FailureCriterion _failureCriterion = FailureCriterion.MohrCoulomb;

    // Failure criterion parameters
    private float _cohesion_MPa = 10.0f;
    private float _frictionAngle_deg = 30.0f;
    private float _tensileStrength_MPa = 5.0f;
    private float _hoekBrown_mb = 10.0f;
    private float _hoekBrown_s = 1.0f;
    private float _hoekBrown_a = 0.5f;

    // Curve editors for parameter sweeps
    private ImGuiCurveEditor _confiningPressureCurve;
    private ImGuiCurveEditor _axialLoadCurve;
    private bool _useConfiningPressureCurve;
    private bool _useAxialLoadCurve;

    // Simulation state
    private bool _isRunning;
    private bool _simulationComplete;
    private float _simulationProgress;
    private string _statusMessage = "Ready";

    // Visualization
    private TriaxialVisualization3D _visualization3D;
    private MohrCircleRenderer _mohrCircleRenderer;

    // Material library
    private readonly MaterialLibrary _materialLibrary;
    private readonly GeomechanicalCalibrationManager _calibrationManager;
    private string _materialSearchQuery = "";
    private List<PhysicalMaterial> _filteredMaterials = new();

    // Presets
    private string _selectedPresetName = "Select Preset...";
    private readonly Dictionary<string, (float confining, float axialRate, float maxStrain)> _loadingPresets = new()
    {
        { "Standard UCS (Unconfined)", (0.0f, 0.5f, 5.0f) },
        { "Low Confining (5 MPa)", (5.0f, 0.5f, 5.0f) },
        { "Medium Confining (10 MPa)", (10.0f, 0.5f, 5.0f) },
        { "High Confining (30 MPa)", (30.0f, 1.0f, 10.0f) },
        { "Very High Confining (100 MPa)", (100.0f, 2.0f, 15.0f) },
    };

    public TriaxialSimulationTool()
    {
        _simulation = new TriaxialSimulation();
        _materialLibrary = MaterialLibrary.Instance;
        _mohrCircleRenderer = new MohrCircleRenderer();
        _visualization3D = new TriaxialVisualization3D();
        _calibrationManager = new GeomechanicalCalibrationManager();

        // Initialize curve editors
        var defaultConfiningCurve = new List<CurvePoint>
        {
            new CurvePoint(0, 0),
            new CurvePoint(0.2f, 1.0f),
            new CurvePoint(1.0f, 1.0f)
        };

        var defaultAxialCurve = new List<CurvePoint>
        {
            new CurvePoint(0, 0),
            new CurvePoint(1.0f, 1.0f)
        };

        _confiningPressureCurve = new ImGuiCurveEditor(
            "confining_pressure_curve",
            "Confining Pressure vs Time",
            "Normalized Time",
            "Normalized Pressure",
            defaultConfiningCurve,
            new Vector2(0, 0),
            new Vector2(1, 2)
        );

        _axialLoadCurve = new ImGuiCurveEditor(
            "axial_load_curve",
            "Axial Load vs Time",
            "Normalized Time",
            "Normalized Load",
            defaultAxialCurve,
            new Vector2(0, 0),
            new Vector2(1, 2)
        );

        // Load default material
        LoadDefaultMaterial();
    }

    public void Dispose()
    {
        _simulation?.Dispose();
        _mohrCircleRenderer?.Dispose();
        _visualization3D?.Dispose();
    }

    public void Show() => _isOpen = true;
    public void Hide() => _isOpen = false;

    public void Draw()
    {
        if (!_isOpen) return;

        ImGui.Begin("Triaxial Simulation Tool", ref _isOpen, ImGuiWindowFlags.MenuBar);

        DrawMenuBar();

        // Split into left (controls) and right (visualization)
        var availWidth = ImGui.GetContentRegionAvail().X;
        var leftWidth = Math.Min(500, availWidth * 0.35f);

        ImGui.BeginChild("ControlsPanel", new Vector2(leftWidth, 0), ImGuiChildFlags.Border);
        DrawControlsPanel();
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("VisualizationPanel", new Vector2(0, 0), ImGuiChildFlags.Border);
        DrawVisualizationPanel();
        ImGui.EndChild();

        ImGui.End();

        // Draw modal dialogs
        if (_showMaterialSelector)
            DrawMaterialSelector();

        // Draw curve editor windows
        _confiningPressureCurve.Draw();
        _axialLoadCurve.Draw();
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New Simulation"))
                {
                    ResetSimulation();
                }

                if (ImGui.MenuItem("Export Results...", null, false, _simulationComplete))
                {
                    ExportResults();
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                ImGui.MenuItem("3D Visualization", null, ref _show3DView);
                ImGui.MenuItem("Mohr Circle", null, ref _showMohrCircle);
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("About Triaxial Testing"))
                {
                    ImGui.OpenPopup("AboutTriaxial");
                }
                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }

        DrawAboutPopup();
    }

    private void DrawControlsPanel()
    {
        // Status indicator
        ImGui.PushStyleColor(ImGuiCol.Text, _simulationComplete
            ? new Vector4(0, 1, 0, 1)
            : _isRunning
                ? new Vector4(1, 1, 0, 1)
                : new Vector4(0.7f, 0.7f, 0.7f, 1));
        ImGui.Text($"Status: {_statusMessage}");
        ImGui.PopStyleColor();

        if (_isRunning)
        {
            ImGui.ProgressBar(_simulationProgress, new Vector2(-1, 0));
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Material selection
        if (ImGui.CollapsingHeader("Material Selection", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawMaterialSection();
        }

        ImGui.Spacing();

        // Calibration section
        if (ImGui.CollapsingHeader("Lab Data Calibration"))
        {
            DrawCalibrationSection();
        }

        ImGui.Spacing();

        // Mesh settings
        if (ImGui.CollapsingHeader("Mesh Generation", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawMeshSection();
        }

        ImGui.Spacing();

        // Loading parameters
        if (ImGui.CollapsingHeader("Loading Parameters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawLoadingSection();
        }

        ImGui.Spacing();

        // Failure criterion
        if (ImGui.CollapsingHeader("Failure Criterion", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawFailureCriterionSection();
        }

        ImGui.Spacing();

        // Simulation controls
        if (ImGui.CollapsingHeader("Simulation Controls", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawSimulationControls();
        }
    }

    private void DrawMaterialSection()
    {
        if (_selectedMaterial != null)
        {
            ImGui.Text($"Material: {_selectedMaterial.Name}");
            ImGui.Indent();
            ImGui.Text($"E: {_selectedMaterial.YoungModulus_GPa ?? 0:F1} GPa");
            ImGui.Text($"ν: {_selectedMaterial.PoissonRatio ?? 0:F3}");
            ImGui.Text($"UCS: {_selectedMaterial.CompressiveStrength_MPa ?? 0:F1} MPa");
            ImGui.Text($"φ: {_selectedMaterial.FrictionAngle_deg ?? 0:F1}°");
            ImGui.Text($"ρ: {_selectedMaterial.Density_kg_m3 ?? 0:F0} kg/m³");
            ImGui.Unindent();
        }

        if (ImGui.Button("Select Material from Library", new Vector2(-1, 0)))
        {
            _showMaterialSelector = true;
            UpdateFilteredMaterials();
        }

        ImGui.Spacing();
        ImGui.Text("Or enter custom properties:");
        ImGui.Spacing();

        if (_selectedMaterial != null)
        {
            var E = (float)(_selectedMaterial.YoungModulus_GPa ?? 50.0);
            var nu = (float)(_selectedMaterial.PoissonRatio ?? 0.25);
            var ucs = (float)(_selectedMaterial.CompressiveStrength_MPa ?? 100.0);
            var phi = (float)(_selectedMaterial.FrictionAngle_deg ?? 30.0);

            if (ImGui.DragFloat("Young's Modulus (GPa)", ref E, 1f, 0.1f, 1000f))
                _selectedMaterial.YoungModulus_GPa = E;

            if (ImGui.DragFloat("Poisson's Ratio", ref nu, 0.01f, 0.0f, 0.5f))
                _selectedMaterial.PoissonRatio = nu;

            if (ImGui.DragFloat("UCS (MPa)", ref ucs, 1f, 1f, 500f))
                _selectedMaterial.CompressiveStrength_MPa = ucs;

            if (ImGui.DragFloat("Friction Angle (°)", ref phi, 0.5f, 0f, 60f))
                _selectedMaterial.FrictionAngle_deg = phi;
        }
    }

    private void DrawCalibrationSection()
    {
        ImGui.Indent();

        if (_selectedMaterial != null)
        {
            var E = (float)(_selectedMaterial.YoungModulus_GPa ?? 50.0) * 1000f; // Convert to MPa
            var nu = (float)(_selectedMaterial.PoissonRatio ?? 0.25);

            // Draw calibration UI and get updated values
            _calibrationManager.DrawCalibrationUI(ref E, ref nu,
                ref _cohesion_MPa, ref _frictionAngle_deg, ref _tensileStrength_MPa);

            // Update material properties if changed
            _selectedMaterial.YoungModulus_GPa = E / 1000f;
            _selectedMaterial.PoissonRatio = nu;
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1),
                "Select a material first to use calibration");
        }

        ImGui.Unindent();
    }

    private void DrawMeshSection()
    {
        ImGui.Checkbox("Use Cylindrical Mesh", ref _useCylindricalMesh);
        ImGui.SameLine();
        DrawHelpMarker("Cylindrical mesh: accurate geometry\nCartesian mesh: faster generation");

        ImGui.Spacing();

        ImGui.DragFloat("Radius (mm)", ref _cylinderRadius_mm, 0.5f, 5f, 100f);
        ImGui.DragFloat("Height (mm)", ref _cylinderHeight_mm, 1f, 10f, 200f);

        float ratio = _cylinderHeight_mm / (2 * _cylinderRadius_mm);
        ImGui.Text($"Height/Diameter ratio: {ratio:F2} (Standard: 2.0)");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Mesh Density:");

        ImGui.DragInt("Radial Divisions", ref _meshDensityRadial, 0.5f, 3, 15);
        ImGui.DragInt("Circumferential Divisions", ref _meshDensityCircumferential, 0.5f, 8, 48);
        ImGui.DragInt("Axial Divisions", ref _meshDensityAxial, 0.5f, 5, 50);

        if (_mesh != null)
        {
            ImGui.Spacing();
            ImGui.Text($"Current Mesh:");
            ImGui.Text($"  Nodes: {_mesh.TotalNodes:N0}");
            ImGui.Text($"  Elements: {_mesh.TotalElements:N0}");
        }

        if (ImGui.Button("Generate Mesh", new Vector2(-1, 0)))
        {
            GenerateMesh();
        }
    }

    private void DrawLoadingSection()
    {
        ImGui.Text("Loading Presets:");
        if (ImGui.BeginCombo("##LoadingPreset", _selectedPresetName))
        {
            foreach (var preset in _loadingPresets)
            {
                bool isSelected = _selectedPresetName == preset.Key;
                if (ImGui.Selectable(preset.Key, isSelected))
                {
                    _selectedPresetName = preset.Key;
                    _loadParams.ConfiningPressure_MPa = preset.Value.confining;
                    // The preset defines axialRate. If we are in Strain Controlled mode,
                    // this rate is likely too high if interpreted as strain rate if the values are like 0.5 (50%).
                    // However, if we assume the presets are for Stress Controlled tests, we should switch mode.
                    // Or we interpret the value differently.
                    // Given values like 0.5, 1.0, 2.0 -> these look like Stress Rates (MPa/s).
                    // If we want to support Strain Controlled, we should calculate a reasonable strain rate.

                    _loadParams.AxialStressRate_MPa_per_s = preset.Value.axialRate;
                    _loadParams.MaxAxialStrain_percent = preset.Value.maxStrain;

                    // Ensure TotalTime is sufficient
                    // If Stress Controlled: Time = MaxStress / Rate.
                    // But MaxStress isn't in preset, MaxStrain is.
                    // We can estimate time based on MaxStrain and an assumed Modulus (e.g. 50GPa) if needed,
                    // or just set a long enough time.

                    // Let's set a generous TotalTime so it doesn't timeout prematurely
                    _loadParams.TotalTime_s = 1000.0f;

                    // Also adjust time step for stability/speed trade-off
                    _loadParams.TimeStep_s = 0.1f;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Loading mode
        int mode = (int)_loadParams.LoadingMode;
        ImGui.RadioButton("Strain-Controlled", ref mode, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Stress-Controlled", ref mode, 1);
        _loadParams.LoadingMode = (TriaxialLoadingMode)mode;

        ImGui.Spacing();

        // Confining pressure
        ImGui.Text("Confining Pressure:");
        ImGui.Checkbox("Use Curve##Confining", ref _useConfiningPressureCurve);
        if (_useConfiningPressureCurve)
        {
            ImGui.SameLine();
            if (ImGui.Button("Edit Curve##Confining"))
                _confiningPressureCurve.Open();
        }
        else
        {
            float confiningPressure = _loadParams.ConfiningPressure_MPa;
            if (ImGui.DragFloat("σ3 (MPa)", ref confiningPressure, 0.5f, 0f, 200f))
                _loadParams.ConfiningPressure_MPa = confiningPressure;
        }

        ImGui.Spacing();

        // Axial loading
        if (_loadParams.LoadingMode == TriaxialLoadingMode.StrainControlled)
        {
            float strainRate = _loadParams.AxialStrainRate_per_s;
            if (ImGui.DragFloat("Strain Rate (/s)", ref strainRate, 1e-6f, 1e-7f, 1e-3f, "%.2e"))
                _loadParams.AxialStrainRate_per_s = strainRate;

            float maxStrain = _loadParams.MaxAxialStrain_percent;
            if (ImGui.DragFloat("Max Axial Strain (%)", ref maxStrain, 0.1f, 0.1f, 20f))
                _loadParams.MaxAxialStrain_percent = maxStrain;
        }
        else
        {
            float stressRate = _loadParams.AxialStressRate_MPa_per_s;
            if (ImGui.DragFloat("Stress Rate (MPa/s)", ref stressRate, 0.01f, 0.001f, 10f))
                _loadParams.AxialStressRate_MPa_per_s = stressRate;

            float maxStress = _loadParams.MaxAxialStress_MPa;
            if (ImGui.DragFloat("Max Axial Stress (MPa)", ref maxStress, 5f, 10f, 500f))
                _loadParams.MaxAxialStress_MPa = maxStress;
        }

        ImGui.Spacing();

        // Drainage condition
        int drainage = (int)_loadParams.DrainageCondition;
        ImGui.RadioButton("Drained", ref drainage, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Undrained", ref drainage, 1);
        _loadParams.DrainageCondition = (DrainageCondition)drainage;

        if (_loadParams.DrainageCondition == DrainageCondition.Undrained)
        {
            float bulkModulus = _loadParams.PoreFluidBulkModulus_GPa;
            if (ImGui.DragFloat("Fluid Bulk Modulus (GPa)", ref bulkModulus, 0.1f, 0.1f, 10f))
                _loadParams.PoreFluidBulkModulus_GPa = bulkModulus;
        }

        ImGui.Spacing();
        float totalTime = _loadParams.TotalTime_s;
        if (ImGui.DragFloat("Total Time (s)", ref totalTime, 1f, 1f, 1000f))
            _loadParams.TotalTime_s = totalTime;

        float timeStep = _loadParams.TimeStep_s;
        if (ImGui.DragFloat("Time Step (s)", ref timeStep, 0.01f, 0.001f, 10f))
            _loadParams.TimeStep_s = timeStep;

        float temperature = _loadParams.Temperature_C;
        if (ImGui.DragFloat("Temperature (°C)", ref temperature, 1f, -50f, 200f))
            _loadParams.Temperature_C = temperature;
    }

    private void DrawFailureCriterionSection()
    {
        int criterion = (int)_failureCriterion;

        ImGui.RadioButton("Mohr-Coulomb", ref criterion, 0);
        ImGui.RadioButton("Drucker-Prager", ref criterion, 1);
        ImGui.RadioButton("Hoek-Brown", ref criterion, 2);
        ImGui.RadioButton("Griffith", ref criterion, 3);

        _failureCriterion = (FailureCriterion)criterion;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Parameters:");
        ImGui.Spacing();

        switch (_failureCriterion)
        {
            case FailureCriterion.MohrCoulomb:
            case FailureCriterion.DruckerPrager:
                ImGui.DragFloat("Cohesion (MPa)", ref _cohesion_MPa, 0.5f, 0f, 100f);
                ImGui.DragFloat("Friction Angle (°)", ref _frictionAngle_deg, 0.5f, 0f, 60f);
                break;

            case FailureCriterion.HoekBrown:
                ImGui.DragFloat("mb", ref _hoekBrown_mb, 0.1f, 0.001f, 30f);
                ImGui.DragFloat("s", ref _hoekBrown_s, 0.01f, 0f, 1f);
                ImGui.DragFloat("a", ref _hoekBrown_a, 0.01f, 0.5f, 0.65f);
                break;

            case FailureCriterion.Griffith:
                ImGui.DragFloat("Tensile Strength (MPa)", ref _tensileStrength_MPa, 0.5f, 0.1f, 50f);
                break;
        }

        if (ImGui.Button("Auto-calculate from Material"))
        {
            CalculateFailureParametersFromMaterial();
        }
    }

    private void DrawSimulationControls()
    {
        bool canRun = _mesh != null && _selectedMaterial != null && !_isRunning;

        ImGui.BeginDisabled(!canRun);
        if (ImGui.Button("Run Simulation", new Vector2(-1, 0)))
        {
            RunSimulation();
        }
        ImGui.EndDisabled();

        if (_isRunning)
        {
            if (ImGui.Button("Stop", new Vector2(-1, 0)))
            {
                StopSimulation();
            }
        }

        if (_simulationComplete)
        {
            if (ImGui.Button("Reset", new Vector2(-1, 0)))
            {
                ResetSimulation();
            }
        }
    }

    private void DrawVisualizationPanel()
    {
        if (ImGui.BeginTabBar("VisualizationTabs"))
        {
            if (ImGui.BeginTabItem("3D View"))
            {
                if (_mesh != null)
                {
                    _visualization3D.Draw(_mesh, _results);
                }
                else
                {
                    ImGui.Text("Generate mesh to view 3D visualization");
                }
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Stress-Strain"))
            {
                DrawStressStrainPlot();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Mohr Circle"))
            {
                DrawMohrCircleTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Results Table"))
            {
                DrawResultsTable();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawStressStrainPlot()
    {
        if (_results == null || _results.AxialStrain == null)
        {
            ImGui.Text("Run simulation to view stress-strain curve");
            return;
        }

        ImGui.Text("Stress-Strain Curve");
        ImGui.Separator();

        // Summary stats
        ImGui.Text($"Peak Stress: {_results.PeakStrength_MPa:F2} MPa");
        ImGui.Text($"Young's Modulus: {_results.YoungModulus_GPa:F2} GPa");
        ImGui.Text($"Failure Angle: {_results.FailureAngle_deg:F2}°");

        ImGui.Spacing();

        // Plot stress-strain curve
        var plotSize = new Vector2(ImGui.GetContentRegionAvail().X, 400);
        var drawList = ImGui.GetWindowDrawList();
        var plotPos = ImGui.GetCursorScreenPos();

        // Background
        drawList.AddRectFilled(plotPos, plotPos + plotSize,
            ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 1)));

        // Find data range
        float maxStrain = _results.AxialStrain.Max();
        float minStrain = _results.AxialStrain.Min();
        float maxStress = _results.AxialStress_MPa.Max();
        float minStress = _results.AxialStress_MPa.Min();

        var margin = 50f;
        var plotWidth = plotSize.X - 2 * margin;
        var plotHeight = plotSize.Y - 2 * margin;

        // Helper to convert data to screen coords
        Vector2 ToScreen(float strain, float stress)
        {
            float x = plotPos.X + margin + (strain - minStrain) / (maxStrain - minStrain) * plotWidth;
            float y = plotPos.Y + plotSize.Y - margin - (stress - minStress) / (maxStress - minStress) * plotHeight;
            return new Vector2(x, y);
        }

        // Draw grid
        var gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1));
        for (int i = 0; i <= 5; i++)
        {
            float t = i / 5f;
            float strain = minStrain + t * (maxStrain - minStrain);
            float stress = minStress + t * (maxStress - minStress);

            var xPos = ToScreen(strain, minStress);
            var xPosTop = ToScreen(strain, maxStress);
            drawList.AddLine(xPos, xPosTop, gridColor, 1f);

            var yPos = ToScreen(minStrain, stress);
            var yPosRight = ToScreen(maxStrain, stress);
            drawList.AddLine(yPos, yPosRight, gridColor, 1f);
        }

        // Draw axes
        var axisColor = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1));
        drawList.AddLine(ToScreen(minStrain, minStress), ToScreen(maxStrain, minStress), axisColor, 2f);
        drawList.AddLine(ToScreen(minStrain, minStress), ToScreen(minStrain, maxStress), axisColor, 2f);

        // Draw curve
        var curveColor = ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1f, 1));
        for (int i = 0; i < _results.AxialStrain.Length - 1; i++)
        {
            var p1 = ToScreen(_results.AxialStrain[i], _results.AxialStress_MPa[i]);
            var p2 = ToScreen(_results.AxialStrain[i + 1], _results.AxialStress_MPa[i + 1]);
            drawList.AddLine(p1, p2, curveColor, 2f);
        }

        // Mark peak point
        int peakIdx = Array.IndexOf(_results.AxialStress_MPa, _results.AxialStress_MPa.Max());
        var peakPoint = ToScreen(_results.AxialStrain[peakIdx], _results.AxialStress_MPa[peakIdx]);
        drawList.AddCircleFilled(peakPoint, 5f, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));

        // Axis labels
        var white = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
        drawList.AddText(plotPos + new Vector2(plotSize.X / 2 - 50, plotSize.Y - 20), white, "Axial Strain (%)");

        // Rotate text for Y axis (approximated with individual chars)
        var yLabelPos = plotPos + new Vector2(10, plotSize.Y / 2);
        drawList.AddText(yLabelPos, white, "Axial Stress (MPa)");

        // Tick labels
        for (int i = 0; i <= 5; i++)
        {
            float t = i / 5f;
            float strain = minStrain + t * (maxStrain - minStrain);
            float stress = minStress + t * (maxStress - minStress);

            var xPos = ToScreen(strain, minStress);
            drawList.AddText(xPos + new Vector2(-15, 5), white, $"{strain:F1}");

            var yPos = ToScreen(minStrain, stress);
            drawList.AddText(yPos + new Vector2(-40, -10), white, $"{stress:F0}");
        }

        // Border
        drawList.AddRect(plotPos, plotPos + plotSize,
            ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1)));

        ImGui.Dummy(plotSize);
    }

    private void DrawMohrCircleTab()
    {
        if (_results == null || _results.MohrCirclesAtPeak.Count == 0)
        {
            ImGui.Text("Run simulation to view Mohr circles");
            return;
        }

        // Create temporary GeomechanicalResults for the renderer
        var geoResults = new GeomechanicalResults
        {
            MohrCircles = _results.MohrCirclesAtPeak
        };

        var geoParams = new GeomechanicalParameters
        {
            FailureCriterion = _failureCriterion,
            Cohesion = _cohesion_MPa,
            FrictionAngle = _frictionAngle_deg,
            TensileStrength = _tensileStrength_MPa,
            HoekBrown_mb = _hoekBrown_mb,
            HoekBrown_s = _hoekBrown_s,
            HoekBrown_a = _hoekBrown_a
        };

        _mohrCircleRenderer.Draw(geoResults, geoParams);
    }

    private void DrawResultsTable()
    {
        if (_results == null)
        {
            ImGui.Text("No results available");
            return;
        }

        ImGui.Text("Simulation Results Summary");
        ImGui.Separator();

        if (ImGui.BeginTable("ResultsTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Property");
            ImGui.TableSetupColumn("Value");
            ImGui.TableHeadersRow();

            AddTableRow("Young's Modulus", $"{_results.YoungModulus_GPa:F2} GPa");
            AddTableRow("Poisson's Ratio", $"{_results.PoissonRatio:F3}");
            AddTableRow("Peak Strength", $"{_results.PeakStrength_MPa:F2} MPa");
            AddTableRow("Failure Angle", $"{_results.FailureAngle_deg:F2}°");
            AddTableRow("Total Nodes", $"{_results.Mesh.TotalNodes:N0}");
            AddTableRow("Total Elements", $"{_results.Mesh.TotalElements:N0}");
            AddTableRow("Fracture Planes", $"{_results.FracturePlanes.Count}");

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Add to calibration database button
        if (ImGui.Button("Add Results to Calibration Database", new Vector2(-1, 0)))
        {
            if (_selectedMaterial != null && _results != null)
            {
                // The calibration manager expects material properties and test results
                // For now, we'll log this action - the actual implementation would store the triaxial test data
                Util.Logger.Log($"[TriaxialSimulation] Added results to calibration database: " +
                    $"Material={_selectedMaterial.Name}, " +
                    $"Confining Pressure={_loadParams.ConfiningPressure_MPa:F2} MPa, " +
                    $"Peak Strength={_results.PeakStrength_MPa:F2} MPa, " +
                    $"E={_results.YoungModulus_GPa * 1000:F0} MPa, " +
                    $"ν={_results.PoissonRatio:F3}");
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Save simulation results to the calibration database for future reference");
        }
    }

    private void AddTableRow(string property, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text(property);
        ImGui.TableNextColumn();
        ImGui.Text(value);
    }

    private void DrawMaterialSelector()
    {
        ImGui.OpenPopup("Material Library");

        ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);
        if (ImGui.BeginPopupModal("Material Library", ref _showMaterialSelector))
        {
            ImGui.InputText("Search", ref _materialSearchQuery, 256);
            if (ImGui.IsItemEdited())
                UpdateFilteredMaterials();

            ImGui.Separator();

            if (ImGui.BeginChild("MaterialList", new Vector2(0, 400)))
            {
                foreach (var material in _filteredMaterials)
                {
                    if (ImGui.Selectable($"{material.Name}##mat_{material.Name}"))
                    {
                        _selectedMaterial = material;
                        CalculateFailureParametersFromMaterial();
                        _showMaterialSelector = false;
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"E: {material.YoungModulus_GPa ?? 0:F1} GPa");
                        ImGui.Text($"ν: {material.PoissonRatio ?? 0:F3}");
                        ImGui.Text($"UCS: {material.CompressiveStrength_MPa ?? 0:F1} MPa");
                        ImGui.EndTooltip();
                    }
                }
            }
            ImGui.EndChild();

            if (ImGui.Button("Close"))
                _showMaterialSelector = false;

            ImGui.EndPopup();
        }
    }

    private void DrawAboutPopup()
    {
        if (ImGui.BeginPopupModal("AboutTriaxial", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Triaxial Compression Testing");
            ImGui.Separator();
            ImGui.Text("A standard geomechanical test where a cylindrical sample");
            ImGui.Text("is subjected to axial stress (σ1) and confining pressure (σ3).");
            ImGui.Spacing();
            ImGui.Text("Key outputs:");
            ImGui.BulletText("Peak strength and failure envelope");
            ImGui.BulletText("Young's modulus and Poisson's ratio");
            ImGui.BulletText("Failure angle and fracture planes");
            ImGui.BulletText("Mohr-Coulomb parameters (c, φ)");

            if (ImGui.Button("Close"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    private void DrawHelpMarker(string desc)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private void LoadDefaultMaterial()
    {
        // Try to load a rock material
        _selectedMaterial = _materialLibrary.Find("Granite") ??
                           _materialLibrary.Find("Sandstone") ??
                           new PhysicalMaterial
                           {
                               Name = "Default Rock",
                               YoungModulus_GPa = 50.0,
                               PoissonRatio = 0.25,
                               CompressiveStrength_MPa = 100.0,
                               FrictionAngle_deg = 30.0,
                               Density_kg_m3 = 2650.0
                           };

        CalculateFailureParametersFromMaterial();
    }

    private void UpdateFilteredMaterials()
    {
        var allMaterials = _materialLibrary.Materials
            .Where(m => m.Phase == PhaseType.Solid)
            .ToList();

        if (string.IsNullOrWhiteSpace(_materialSearchQuery))
        {
            _filteredMaterials = allMaterials;
        }
        else
        {
            _filteredMaterials = allMaterials
                .Where(m => m.Name.Contains(_materialSearchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void CalculateFailureParametersFromMaterial()
    {
        if (_selectedMaterial == null) return;

        float ucs = (float)(_selectedMaterial.CompressiveStrength_MPa ?? 100.0);
        _frictionAngle_deg = (float)(_selectedMaterial.FrictionAngle_deg ?? 30.0);
        _tensileStrength_MPa = (float)(_selectedMaterial.TensileStrength_MPa ?? ucs * 0.1f);

        float phi_rad = _frictionAngle_deg * MathF.PI / 180f;
        _cohesion_MPa = ucs * (1 - MathF.Sin(phi_rad)) / (2 * MathF.Cos(phi_rad));

        // Hoek-Brown parameters (default for intact rock)
        _hoekBrown_mb = 10.0f; // Typical for intact rock
        _hoekBrown_s = 1.0f;   // Intact rock
        _hoekBrown_a = 0.5f;   // Standard value
    }

    private void GenerateMesh()
    {
        try
        {
            _statusMessage = "Generating mesh...";

            if (_useCylindricalMesh)
            {
                _mesh = TriaxialMeshGenerator.GenerateCylindricalMesh(
                    _cylinderRadius_mm / 1000f,  // Convert to meters
                    _cylinderHeight_mm / 1000f,
                    _meshDensityRadial,
                    _meshDensityCircumferential,
                    _meshDensityAxial);
            }
            else
            {
                _mesh = TriaxialMeshGenerator.GenerateCartesianCylindricalMesh(
                    _cylinderRadius_mm / 1000f,
                    _cylinderHeight_mm / 1000f,
                    _meshDensityRadial,
                    _meshDensityRadial,
                    _meshDensityAxial);
            }

            _statusMessage = $"Mesh generated: {_mesh.TotalNodes:N0} nodes, {_mesh.TotalElements:N0} elements";
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error generating mesh: {ex.Message}";
        }
    }

    private async void RunSimulation()
    {
        if (_mesh == null || _selectedMaterial == null) return;

        _isRunning = true;
        _simulationComplete = false;
        _statusMessage = "Running simulation...";
        _simulationProgress = 0f;

        Util.Logger.Log("[TriaxialTool] Starting simulation...");
        Util.Logger.Log($"[TriaxialTool] Mode: {_loadParams.LoadingMode}, Rate: {_loadParams.AxialStrainRate_per_s}/s or {_loadParams.AxialStressRate_MPa_per_s} MPa/s");
        Util.Logger.Log($"[TriaxialTool] Total Time: {_loadParams.TotalTime_s}s");

        await Task.Run(() =>
        {
            try
            {
                // Apply curves if enabled
                if (_useConfiningPressureCurve)
                    _loadParams.ConfiningPressureCurve = _confiningPressureCurve.GetPoints()
                        .Select(p => p.Point).ToList();

                if (_useAxialLoadCurve)
                    _loadParams.AxialLoadCurve = _axialLoadCurve.GetPoints()
                        .Select(p => p.Point).ToList();

                // Run simulation (CPU for now, GPU version can be added later)
                _results = _simulation.RunSimulationCPU(
                    _mesh,
                    _selectedMaterial,
                    _loadParams,
                    _failureCriterion);

                _simulationProgress = 1.0f;
                _statusMessage = "Simulation complete";
                _simulationComplete = true;
                Util.Logger.Log("[TriaxialTool] Simulation completed successfully.");
            }
            catch (Exception ex)
            {
                _statusMessage = $"Simulation error: {ex.Message}";
                Util.Logger.LogError($"[TriaxialTool] Simulation error: {ex.Message}");
            }
            finally
            {
                _isRunning = false;
            }
        });
    }

    private void StopSimulation()
    {
        _isRunning = false;
        _statusMessage = "Simulation stopped";
    }

    private void ResetSimulation()
    {
        _results = null;
        _simulationComplete = false;
        _simulationProgress = 0f;
        _statusMessage = "Ready";
    }

    private void ExportResults()
    {
        if (_results == null) return;

        try
        {
            // Generate default filename
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var defaultFilename = $"triaxial_results_{timestamp}.csv";

            // Use file dialog (simplified - direct write for now)
            var savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GeoscientistToolkit", defaultFilename);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using var writer = new StreamWriter(savePath);

            // Header
            writer.WriteLine("# Triaxial Simulation Results");
            writer.WriteLine($"# Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"# Material: {_selectedMaterial?.Name ?? "Unknown"}");
            writer.WriteLine($"# Failure Criterion: {_failureCriterion}");
            writer.WriteLine($"# Confining Pressure: {_loadParams.ConfiningPressure_MPa:F2} MPa");
            writer.WriteLine($"# Peak Strength: {_results.PeakStrength_MPa:F2} MPa");
            writer.WriteLine($"# Young's Modulus: {_results.YoungModulus_GPa:F2} GPa");
            writer.WriteLine($"# Poisson's Ratio: {_results.PoissonRatio:F3}");
            writer.WriteLine($"# Failure Angle: {_results.FailureAngle_deg:F2} degrees");
            writer.WriteLine("#");

            // Column headers
            writer.WriteLine("Time (s),Axial Strain (%),Axial Stress (MPa),Radial Strain (%),Volumetric Strain (%),Pore Pressure (MPa),Failed");

            // Data rows
            for (int i = 0; i < _results.Time_s.Length; i++)
            {
                writer.WriteLine($"{_results.Time_s[i]:F3}," +
                               $"{_results.AxialStrain[i]:F4}," +
                               $"{_results.AxialStress_MPa[i]:F4}," +
                               $"{_results.RadialStrain[i]:F4}," +
                               $"{_results.VolumetricStrain[i]:F4}," +
                               $"{_results.PorePressure_MPa[i]:F4}," +
                               $"{_results.HasFailed[i]}");
            }

            _statusMessage = $"Results exported to: {savePath}";
            Util.Logger.Log($"Triaxial results exported to: {savePath}");
        }
        catch (Exception ex)
        {
            _statusMessage = $"Export failed: {ex.Message}";
            Util.Logger.LogError($"Failed to export triaxial results: {ex.Message}");
        }
    }
}
