using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Mesh3D;
using ImGuiNET;
using System.Threading.Tasks;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// GUI tools for slope stability analysis configuration and simulation.
    /// </summary>
    public class SlopeStabilityTools : IDatasetTools
    {
        private readonly SlopeStabilityDataset _dataset;

        // UI state
        private int _selectedTab = 0;
        private int _selectedJointSetIdx = -1;
        private int _selectedMaterialIdx = -1;
        private int _selectedEarthquakeIdx = -1;

        // Joint set editor
        private JointSet _editingJointSet = new JointSet();
        private bool _isEditingJointSet = false;

        // Material editor
        private SlopeStabilityMaterial _editingMaterial = new SlopeStabilityMaterial();
        private bool _isEditingMaterial = false;

        // Earthquake editor
        private EarthquakeLoad _editingEarthquake = new EarthquakeLoad();
        private bool _isEditingEarthquake = false;

        // Block generation
        private bool _showBlockGenDialog = false;
        private string _meshPath = "";
        private List<string> _availableMeshes = new List<string>();

        // Simulation
        private bool _isSimulationRunning = false;
        private float _simulationProgress = 0.0f;
        private string _simulationStatus = "";
        private Task _simulationTask = null;

        // Export
        private readonly ImGuiExportFileDialog _resultsExportDialog;

        // 2D Viewer
        private SlopeStability2DViewer _2dViewer = null;
        private bool _show2DViewer = false;
        private float _sectionViewZoom = 1.0f;
        private Vector2 _sectionViewPan = Vector2.Zero;

        public SlopeStabilityTools(SlopeStabilityDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            _2dViewer = new SlopeStability2DViewer(_dataset);
            _resultsExportDialog = new ImGuiExportFileDialog("SlopeStabilityResultsExport", "Export Results");
            _resultsExportDialog.SetExtensions(
                new ImGuiExportFileDialog.ExtensionOption(".csv", "CSV (Comma-separated values)"),
                new ImGuiExportFileDialog.ExtensionOption(".vtk", "VTK (Visualization Toolkit)"),
                new ImGuiExportFileDialog.ExtensionOption(".json", "JSON Results"),
                new ImGuiExportFileDialog.ExtensionOption(".ssr", "Slope Stability Results (Binary)")
            );
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not SlopeStabilityDataset slopeDataset)
                return;

            // Render 2D viewer if open
            if (_show2DViewer && _2dViewer != null)
            {
                if (ImGui.Begin("2D Section View", ref _show2DViewer))
                {
                    _2dViewer.DrawToolbarControls();
                    ImGui.Separator();
                    _2dViewer.DrawContent(ref _sectionViewZoom, ref _sectionViewPan);
                }
                ImGui.End();
            }

            // Tab bar
            string[] tabNames = new[] {
                "Joint Sets",
                "Materials",
                "Parameters",
                "Earthquakes",
                "Blocks",
                "Simulation",
                "Views"
            };

            if (ImGui.BeginTabBar("SlopeStabilityTabs"))
            {
                for (int i = 0; i < tabNames.Length; i++)
                {
                    if (ImGui.BeginTabItem(tabNames[i]))
                    {
                        _selectedTab = i;

                        switch (i)
                        {
                            case 0: DrawJointSetsTab(); break;
                            case 1: DrawMaterialsTab(); break;
                            case 2: DrawParametersTab(); break;
                            case 3: DrawEarthquakesTab(); break;
                            case 4: DrawBlocksTab(); break;
                            case 5: DrawSimulationTab(); break;
                            case 6: DrawViewsTab(); break;
                        }

                        ImGui.EndTabItem();
                    }
                }

                ImGui.EndTabBar();
            }

            HandleResultsExportDialog();
        }

        private void DrawJointSetsTab()
        {
            ImGui.Text("Joint Sets Configuration");
            ImGui.Separator();

            // List of joint sets
            if (ImGui.BeginChild("JointSetsList", new Vector2(200, 0), ImGuiChildFlags.Border))
            {
                for (int i = 0; i < _dataset.JointSets.Count; i++)
                {
                    var jointSet = _dataset.JointSets[i];
                    bool isSelected = i == _selectedJointSetIdx;

                    if (ImGui.Selectable($"{jointSet.Name}##{i}", isSelected))
                    {
                        _selectedJointSetIdx = i;
                    }
                }

                ImGui.EndChild();
            }

            ImGui.SameLine();

            // Joint set editor
            if (ImGui.BeginChild("JointSetEditor", Vector2.Zero, ImGuiChildFlags.Border))
            {
                if (ImGui.Button("Add New Joint Set"))
                {
                    var newJointSet = new JointSet
                    {
                        Id = _dataset.JointSets.Count,
                        Name = $"Joint Set {_dataset.JointSets.Count + 1}"
                    };
                    _dataset.JointSets.Add(newJointSet);
                    _selectedJointSetIdx = _dataset.JointSets.Count - 1;
                }

                if (_selectedJointSetIdx >= 0 && _selectedJointSetIdx < _dataset.JointSets.Count)
                {
                    var jointSet = _dataset.JointSets[_selectedJointSetIdx];

                    ImGui.Separator();

                    var jointSetName = jointSet.Name;
                    if (ImGui.InputText("Name", ref jointSetName, 100))
                    {
                        jointSet.Name = jointSetName;
                    }

                    // Orientation
                    if (ImGui.TreeNode("Orientation"))
                    {
                        float dip = jointSet.Dip;
                        if (ImGui.SliderFloat("Dip (°)", ref dip, 0.0f, 90.0f))
                            jointSet.Dip = dip;

                        float dipDir = jointSet.DipDirection;
                        if (ImGui.SliderFloat("Dip Direction (°)", ref dipDir, 0.0f, 360.0f))
                            jointSet.DipDirection = dipDir;

                        ImGui.TreePop();
                    }

                    // Spacing
                    if (ImGui.TreeNode("Spacing"))
                    {
                        float spacing = jointSet.Spacing;
                        if (ImGui.InputFloat("Spacing (m)", ref spacing))
                            jointSet.Spacing = Math.Max(spacing, 0.001f);

                        float spacingStdDev = jointSet.SpacingStdDev;
                        if (ImGui.InputFloat("Std Dev (m)", ref spacingStdDev))
                            jointSet.SpacingStdDev = Math.Max(spacingStdDev, 0.0f);

                        float persistence = jointSet.Persistence;
                        if (ImGui.SliderFloat("Persistence", ref persistence, 0.0f, 1.0f))
                            jointSet.Persistence = persistence;

                        ImGui.TreePop();
                    }

                    // Mechanical properties
                    if (ImGui.TreeNode("Mechanical Properties"))
                    {
                        float kn = jointSet.NormalStiffness / 1e9f;  // Display in GPa/m
                        if (ImGui.InputFloat("Normal Stiffness (GPa/m)", ref kn))
                            jointSet.NormalStiffness = kn * 1e9f;

                        float ks = jointSet.ShearStiffness / 1e9f;
                        if (ImGui.InputFloat("Shear Stiffness (GPa/m)", ref ks))
                            jointSet.ShearStiffness = ks * 1e9f;

                        float cohesion = jointSet.Cohesion / 1e6f;  // Display in MPa
                        if (ImGui.InputFloat("Cohesion (MPa)", ref cohesion))
                            jointSet.Cohesion = cohesion * 1e6f;

                        float friction = jointSet.FrictionAngle;
                        if (ImGui.SliderFloat("Friction Angle (°)", ref friction, 0.0f, 60.0f))
                            jointSet.FrictionAngle = friction;

                        float tensile = jointSet.TensileStrength / 1e6f;
                        if (ImGui.InputFloat("Tensile Strength (MPa)", ref tensile))
                            jointSet.TensileStrength = tensile * 1e6f;

                        float dilation = jointSet.Dilation;
                        if (ImGui.SliderFloat("Dilation (°)", ref dilation, 0.0f, 30.0f))
                            jointSet.Dilation = dilation;

                        ImGui.TreePop();
                    }

                    // Generation mode
                    if (ImGui.TreeNode("Generation Mode"))
                    {
                        int genMode = (int)jointSet.GenerationMode;
                        string[] modes = Enum.GetNames(typeof(JointGenerationMode));

                        if (ImGui.Combo("Mode", ref genMode, modes, modes.Length))
                            jointSet.GenerationMode = (JointGenerationMode)genMode;

                        if (jointSet.GenerationMode == JointGenerationMode.Stochastic)
                        {
                            int seed = jointSet.Seed;
                            if (ImGui.InputInt("Random Seed", ref seed))
                                jointSet.Seed = seed;
                        }

                        ImGui.TreePop();
                    }

                    ImGui.Separator();

                    if (ImGui.Button("Delete Joint Set"))
                    {
                        _dataset.JointSets.RemoveAt(_selectedJointSetIdx);
                        _selectedJointSetIdx = -1;
                    }
                }

                ImGui.EndChild();
            }
        }

        private void DrawMaterialsTab()
        {
            ImGui.Text("Materials Configuration");
            ImGui.Separator();

            // List of materials
            if (ImGui.BeginChild("MaterialsList", new Vector2(200, 0), ImGuiChildFlags.Border))
            {
                for (int i = 0; i < _dataset.Materials.Count; i++)
                {
                    var material = _dataset.Materials[i];
                    bool isSelected = i == _selectedMaterialIdx;

                    if (ImGui.Selectable($"{material.Name}##{i}", isSelected))
                    {
                        _selectedMaterialIdx = i;
                    }
                }

                ImGui.EndChild();
            }

            ImGui.SameLine();

            // Material editor
            if (ImGui.BeginChild("MaterialEditor", Vector2.Zero, ImGuiChildFlags.Border))
            {
                if (ImGui.Button("Add New Material"))
                {
                    var newMaterial = new SlopeStabilityMaterial
                    {
                        Id = _dataset.Materials.Count,
                        Name = $"Material {_dataset.Materials.Count + 1}"
                    };
                    _dataset.Materials.Add(newMaterial);
                    _selectedMaterialIdx = _dataset.Materials.Count - 1;
                }

                ImGui.SameLine();

                // Preset dropdown
                string[] presets = Enum.GetNames(typeof(MaterialPreset));
                if (ImGui.BeginCombo("Load Preset", "Select..."))
                {
                    for (int i = 0; i < presets.Length; i++)
                    {
                        if (ImGui.Selectable(presets[i]))
                        {
                            var preset = (MaterialPreset)i;
                            var presetMaterial = SlopeStabilityMaterial.CreatePreset(preset);
                            presetMaterial.Id = _dataset.Materials.Count;
                            _dataset.Materials.Add(presetMaterial);
                            _selectedMaterialIdx = _dataset.Materials.Count - 1;
                        }
                    }
                    ImGui.EndCombo();
                }

                if (_selectedMaterialIdx >= 0 && _selectedMaterialIdx < _dataset.Materials.Count)
                {
                    var material = _dataset.Materials[_selectedMaterialIdx];

                    ImGui.Separator();

                    var materialName = material.Name;
                    if (ImGui.InputText("Name", ref materialName, 100))
                    {
                        material.Name = materialName;
                    }

                    // Physical properties
                    if (ImGui.TreeNode("Physical Properties"))
                    {
                        float density = material.Density;
                        if (ImGui.InputFloat("Density (kg/m³)", ref density))
                            material.Density = Math.Max(density, 1.0f);

                        ImGui.TreePop();
                    }

                    // Elastic properties
                    if (ImGui.TreeNode("Elastic Properties"))
                    {
                        float youngModulus = material.YoungModulus / 1e9f;  // GPa
                        if (ImGui.InputFloat("Young's Modulus (GPa)", ref youngModulus))
                        {
                            material.YoungModulus = youngModulus * 1e9f;
                            material.ConstitutiveModel.YoungModulus = material.YoungModulus;
                            material.ConstitutiveModel.UpdateDerivedProperties();
                        }

                        float poissonRatio = material.PoissonRatio;
                        if (ImGui.SliderFloat("Poisson's Ratio", ref poissonRatio, 0.0f, 0.5f))
                        {
                            material.PoissonRatio = poissonRatio;
                            material.ConstitutiveModel.PoissonRatio = poissonRatio;
                            material.ConstitutiveModel.UpdateDerivedProperties();
                        }

                        ImGui.TreePop();
                    }

                    // Strength properties
                    if (ImGui.TreeNode("Strength Properties"))
                    {
                        float cohesion = material.Cohesion / 1e6f;  // MPa
                        if (ImGui.InputFloat("Cohesion (MPa)", ref cohesion))
                        {
                            material.Cohesion = cohesion * 1e6f;
                            material.ConstitutiveModel.Cohesion = material.Cohesion;
                        }

                        float friction = material.FrictionAngle;
                        if (ImGui.SliderFloat("Friction Angle (°)", ref friction, 0.0f, 60.0f))
                        {
                            material.FrictionAngle = friction;
                            material.ConstitutiveModel.FrictionAngle = friction;
                        }

                        float tensile = material.TensileStrength / 1e6f;
                        if (ImGui.InputFloat("Tensile Strength (MPa)", ref tensile))
                        {
                            material.TensileStrength = tensile * 1e6f;
                            material.ConstitutiveModel.TensileStrength = material.TensileStrength;
                        }

                        float compressive = material.CompressiveStrength / 1e6f;
                        if (ImGui.InputFloat("Compressive Strength (MPa)", ref compressive))
                            material.CompressiveStrength = compressive * 1e6f;

                        ImGui.TreePop();
                    }

                    // Constitutive model
                    if (ImGui.TreeNode("Constitutive Model"))
                    {
                        int modelType = (int)material.ConstitutiveModel.ModelType;
                        string[] modelTypes = Enum.GetNames(typeof(ConstitutiveModelType));

                        if (ImGui.Combo("Model Type", ref modelType, modelTypes, modelTypes.Length))
                            material.ConstitutiveModel.ModelType = (ConstitutiveModelType)modelType;

                        int failureCrit = (int)material.ConstitutiveModel.FailureCriterion;
                        string[] failureCriteria = Enum.GetNames(typeof(FailureCriterionType));

                        if (ImGui.Combo("Failure Criterion", ref failureCrit, failureCriteria, failureCriteria.Length))
                            material.ConstitutiveModel.FailureCriterion = (FailureCriterionType)failureCrit;

                        var enablePlasticity = material.ConstitutiveModel.EnablePlasticity;
                        if (ImGui.Checkbox("Enable Plasticity", ref enablePlasticity))
                        {
                            material.ConstitutiveModel.EnablePlasticity = enablePlasticity;
                        }

                        var enableBrittleFailure = material.ConstitutiveModel.EnableBrittleFailure;
                        if (ImGui.Checkbox("Enable Brittle Failure", ref enableBrittleFailure))
                        {
                            material.ConstitutiveModel.EnableBrittleFailure = enableBrittleFailure;
                        }

                        ImGui.TreePop();
                    }

                    ImGui.Separator();

                    if (ImGui.Button("Delete Material"))
                    {
                        _dataset.Materials.RemoveAt(_selectedMaterialIdx);
                        _selectedMaterialIdx = -1;
                    }
                }

                ImGui.EndChild();
            }
        }

        private void DrawParametersTab()
        {
            ImGui.Text("Simulation Parameters");
            ImGui.Separator();

            var param = _dataset.Parameters;

            // Time control
            if (ImGui.CollapsingHeader("Time Control", ImGuiTreeNodeFlags.DefaultOpen))
            {
                float timeStep = param.TimeStep * 1000.0f;  // Display in ms
                if (ImGui.InputFloat("Time Step (ms)", ref timeStep))
                    param.TimeStep = Math.Max(timeStep / 1000.0f, 0.0001f);

                float totalTime = param.TotalTime;
                if (ImGui.InputFloat("Total Time (s)", ref totalTime))
                    param.TotalTime = Math.Max(totalTime, 0.1f);

                int maxIter = param.MaxIterations;
                if (ImGui.InputInt("Max Iterations", ref maxIter))
                    param.MaxIterations = Math.Max(maxIter, 100);

                ImGui.Text($"Estimated Steps: {param.GetNumSteps()}");
            }

            // Gravity and Slope Angle
            if (ImGui.CollapsingHeader("Gravity & Slope Angle", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var useCustomGravity = param.UseCustomGravityDirection;
                if (ImGui.Checkbox("Use Custom Gravity Direction", ref useCustomGravity))
                {
                    param.UseCustomGravityDirection = useCustomGravity;
                }

                if (param.UseCustomGravityDirection)
                {
                    Vector3 gravity = param.Gravity;
                    if (ImGui.InputFloat3("Gravity (m/s²)", ref gravity))
                        param.Gravity = gravity;
                }
                else
                {
                    float slopeAngle = param.SlopeAngle;
                    if (ImGui.SliderFloat("Slope Angle (°)", ref slopeAngle, 0.0f, 90.0f))
                    {
                        param.SlopeAngle = slopeAngle;
                        param.UpdateGravityFromSlopeAngle();
                    }

                    ImGui.Text($"Gravity: ({param.Gravity.X:F2}, {param.Gravity.Y:F2}, {param.Gravity.Z:F2}) m/s²");
                    ImGui.TextDisabled("Slope angle automatically tilts gravity for instability analysis");
                }
            }

            // Simulation mode
            if (ImGui.CollapsingHeader("Simulation Mode", ImGuiTreeNodeFlags.DefaultOpen))
            {
                int mode = (int)param.Mode;
                string[] modes = Enum.GetNames(typeof(SimulationMode));

                if (ImGui.Combo("Mode", ref mode, modes, modes.Length))
                    param.Mode = (SimulationMode)mode;
            }

            // Damping
            if (ImGui.CollapsingHeader("Damping"))
            {
                float localDamping = param.LocalDamping;
                if (ImGui.SliderFloat("Local Damping", ref localDamping, 0.0f, 1.0f))
                    param.LocalDamping = localDamping;

                var useAdaptiveDamping = param.UseAdaptiveDamping;
                if (ImGui.Checkbox("Adaptive Damping", ref useAdaptiveDamping))
                {
                    param.UseAdaptiveDamping = useAdaptiveDamping;
                }

                float viscousDamping = param.ViscousDamping;
                if (ImGui.InputFloat("Viscous Damping", ref viscousDamping))
                    param.ViscousDamping = Math.Max(viscousDamping, 0.0f);
            }

            // Boundary conditions
            if (ImGui.CollapsingHeader("Boundary Conditions"))
            {
                int boundaryMode = (int)param.BoundaryMode;
                string[] modes = Enum.GetNames(typeof(BoundaryConditionMode));

                if (ImGui.Combo("Boundary Mode", ref boundaryMode, modes, modes.Length))
                    param.BoundaryMode = (BoundaryConditionMode)boundaryMode;

                Vector3 dof = param.AllowedDisplacementDOF;
                bool[] dofBool = new bool[] { dof.X > 0.5f, dof.Y > 0.5f, dof.Z > 0.5f };

                ImGui.Checkbox("Allow X displacement", ref dofBool[0]);
                ImGui.Checkbox("Allow Y displacement", ref dofBool[1]);
                ImGui.Checkbox("Allow Z displacement", ref dofBool[2]);

                param.AllowedDisplacementDOF = new Vector3(
                    dofBool[0] ? 1.0f : 0.0f,
                    dofBool[1] ? 1.0f : 0.0f,
                    dofBool[2] ? 1.0f : 0.0f);
            }

            // Advanced options
            if (ImGui.CollapsingHeader("Advanced Options"))
            {
                var useMultithreading = param.UseMultithreading;
                if (ImGui.Checkbox("Use Multithreading", ref useMultithreading))
                {
                    param.UseMultithreading = useMultithreading;
                }

                if (param.UseMultithreading)
                {
                    int numThreads = param.NumThreads;
                    if (ImGui.InputInt("Num Threads (0=auto)", ref numThreads))
                        param.NumThreads = Math.Max(numThreads, 0);
                }

                var useSimd = param.UseSIMD;
                if (ImGui.Checkbox("Use SIMD", ref useSimd))
                {
                    param.UseSIMD = useSimd;
                }

                var includeRotation = param.IncludeRotation;
                if (ImGui.Checkbox("Include Rotation", ref includeRotation))
                {
                    param.IncludeRotation = includeRotation;
                }

                var includeFluidPressure = param.IncludeFluidPressure;
                if (ImGui.Checkbox("Include Fluid Pressure", ref includeFluidPressure))
                {
                    param.IncludeFluidPressure = includeFluidPressure;
                }

                if (param.IncludeFluidPressure)
                {
                    float waterTable = param.WaterTableZ;
                    if (ImGui.InputFloat("Water Table Z (m)", ref waterTable))
                        param.WaterTableZ = waterTable;
                }
            }

            // Output control
            if (ImGui.CollapsingHeader("Output Control"))
            {
                int outputFreq = param.OutputFrequency;
                if (ImGui.InputInt("Output Frequency", ref outputFreq))
                    param.OutputFrequency = Math.Max(outputFreq, 1);

                var saveIntermediate = param.SaveIntermediateStates;
                if (ImGui.Checkbox("Save Intermediate States", ref saveIntermediate))
                {
                    param.SaveIntermediateStates = saveIntermediate;
                }

                var computeFinal = param.ComputeFinalState;
                if (ImGui.Checkbox("Compute Final State", ref computeFinal))
                {
                    param.ComputeFinalState = computeFinal;
                }
            }
        }

        private void DrawEarthquakesTab()
        {
            ImGui.Text("Earthquake Loading");
            ImGui.Separator();

            var enableEarthquakeLoading = _dataset.Parameters.EnableEarthquakeLoading;
            if (ImGui.Checkbox("Enable Earthquake Loading", ref enableEarthquakeLoading))
            {
                _dataset.Parameters.EnableEarthquakeLoading = enableEarthquakeLoading;
            }

            if (!_dataset.Parameters.EnableEarthquakeLoading)
                return;

            // List of earthquakes
            if (ImGui.BeginChild("EarthquakesList", new Vector2(200, 0), ImGuiChildFlags.Border))
            {
                for (int i = 0; i < _dataset.Parameters.EarthquakeLoads.Count; i++)
                {
                    var eq = _dataset.Parameters.EarthquakeLoads[i];
                    bool isSelected = i == _selectedEarthquakeIdx;

                    if (ImGui.Selectable($"M{eq.Magnitude:F1}##{i}", isSelected))
                    {
                        _selectedEarthquakeIdx = i;
                    }
                }

                ImGui.EndChild();
            }

            ImGui.SameLine();

            // Earthquake editor
            if (ImGui.BeginChild("EarthquakeEditor", Vector2.Zero, ImGuiChildFlags.Border))
            {
                if (ImGui.Button("Add New Earthquake"))
                {
                    var newEq = new EarthquakeLoad
                    {
                        Epicenter = Vector3.Zero,
                        Magnitude = 5.0f
                    };
                    newEq.UpdateGroundMotionFromMagnitude();
                    _dataset.Parameters.EarthquakeLoads.Add(newEq);
                    _selectedEarthquakeIdx = _dataset.Parameters.EarthquakeLoads.Count - 1;
                }

                if (_selectedEarthquakeIdx >= 0 && _selectedEarthquakeIdx < _dataset.Parameters.EarthquakeLoads.Count)
                {
                    var eq = _dataset.Parameters.EarthquakeLoads[_selectedEarthquakeIdx];

                    ImGui.Separator();

                    // Basic properties
                    float magnitude = eq.Magnitude;
                    if (ImGui.SliderFloat("Magnitude (Mw)", ref magnitude, 3.0f, 9.0f))
                    {
                        eq.Magnitude = magnitude;
                        eq.UpdateGroundMotionFromMagnitude();
                    }

                    Vector3 epicenter = eq.Epicenter;
                    if (ImGui.InputFloat3("Epicenter (m)", ref epicenter))
                        eq.Epicenter = epicenter;

                    float depth = eq.Depth;
                    if (ImGui.InputFloat("Focal Depth (m)", ref depth))
                        eq.Depth = Math.Max(depth, 0.0f);

                    float duration = eq.Duration;
                    if (ImGui.InputFloat("Duration (s)", ref duration))
                        eq.Duration = Math.Max(duration, 0.1f);

                    float startTime = eq.StartTime;
                    if (ImGui.InputFloat("Start Time (s)", ref startTime))
                        eq.StartTime = Math.Max(startTime, 0.0f);

                    // Ground motion (read-only, auto-calculated from magnitude)
                    ImGui.Text($"PGA: {eq.PeakGroundAcceleration:F2} m/s²");
                    ImGui.Text($"PGV: {eq.PeakGroundVelocity:F3} m/s");
                    ImGui.Text($"PGD: {eq.PeakGroundDisplacement:F4} m");

                    // Advanced
                    if (ImGui.TreeNode("Advanced"))
                    {
                        var useRadialPropagation = eq.UseRadialPropagation;
                        if (ImGui.Checkbox("Radial Propagation", ref useRadialPropagation))
                        {
                            eq.UseRadialPropagation = useRadialPropagation;
                        }

                        int timeFunc = (int)eq.TimeFunction;
                        string[] timeFuncs = Enum.GetNames(typeof(EarthquakeTimeFunction));

                        if (ImGui.Combo("Time Function", ref timeFunc, timeFuncs, timeFuncs.Length))
                            eq.TimeFunction = (EarthquakeTimeFunction)timeFunc;

                        if (ImGui.Button("Generate Synthetic Accelerogram"))
                        {
                            eq.GenerateSyntheticAccelerogram();
                        }

                        ImGui.TreePop();
                    }

                    ImGui.Separator();

                    if (ImGui.Button("Delete Earthquake"))
                    {
                        _dataset.Parameters.EarthquakeLoads.RemoveAt(_selectedEarthquakeIdx);
                        _selectedEarthquakeIdx = -1;
                    }
                }

                ImGui.EndChild();
            }
        }

        private void DrawBlocksTab()
        {
            ImGui.Text("Block Generation");
            ImGui.Separator();

            ImGui.Text($"Current Blocks: {_dataset.Blocks.Count}");

            if (ImGui.Button("Generate Blocks from Mesh"))
            {
                _showBlockGenDialog = true;
            }

            if (_showBlockGenDialog)
            {
                ImGui.Separator();

                ImGui.InputText("Mesh Path", ref _meshPath, 500);

                if (ImGui.Button("Browse..."))
                {
                    // Open file dialog (would need ImGuiFileDialog integration)
                }

                ImGui.Text("Block Generation Settings:");

                var settings = _dataset.BlockGenSettings;

                // Quality/Coarseness presets for quick testing
                ImGui.Text("Quality Preset:");
                ImGui.SameLine();
                if (ImGui.Button("Very Coarse (Fast)"))
                {
                    settings.TargetBlockSize = 5.0f;
                    settings.MinimumBlockVolume = 10.0f;
                }
                ImGui.SameLine();
                if (ImGui.Button("Coarse"))
                {
                    settings.TargetBlockSize = 3.0f;
                    settings.MinimumBlockVolume = 2.0f;
                }
                ImGui.SameLine();
                if (ImGui.Button("Medium"))
                {
                    settings.TargetBlockSize = 1.5f;
                    settings.MinimumBlockVolume = 0.5f;
                }
                if (ImGui.Button("Fine"))
                {
                    settings.TargetBlockSize = 1.0f;
                    settings.MinimumBlockVolume = 0.1f;
                }
                ImGui.SameLine();
                if (ImGui.Button("Very Fine (Slow)"))
                {
                    settings.TargetBlockSize = 0.5f;
                    settings.MinimumBlockVolume = 0.01f;
                }

                ImGui.Separator();

                float targetSize = settings.TargetBlockSize;
                if (ImGui.InputFloat("Target Block Size (m)", ref targetSize))
                    settings.TargetBlockSize = Math.Max(targetSize, 0.1f);

                ImGui.TextWrapped("Tip: Use 'Very Coarse' or 'Coarse' for quick testing and debugging");

                ImGui.Separator();

                float minVolume = settings.MinimumBlockVolume;
                if (ImGui.InputFloat("Minimum Volume (m³)", ref minVolume))
                    settings.MinimumBlockVolume = Math.Max(minVolume, 0.0001f);

                float maxVolume = settings.MaximumBlockVolume;
                if (ImGui.InputFloat("Maximum Volume (m³)", ref maxVolume))
                    settings.MaximumBlockVolume = Math.Max(maxVolume, minVolume);

                var removeSmallBlocks = settings.RemoveSmallBlocks;
                if (ImGui.Checkbox("Remove Small Blocks", ref removeSmallBlocks))
                {
                    settings.RemoveSmallBlocks = removeSmallBlocks;
                }

                var removeLargeBlocks = settings.RemoveLargeBlocks;
                if (ImGui.Checkbox("Remove Large Blocks", ref removeLargeBlocks))
                {
                    settings.RemoveLargeBlocks = removeLargeBlocks;
                }

                var mergeSliverBlocks = settings.MergeSliverBlocks;
                if (ImGui.Checkbox("Merge Sliver Blocks", ref mergeSliverBlocks))
                {
                    settings.MergeSliverBlocks = mergeSliverBlocks;
                }

                if (ImGui.Button("Generate") && !string.IsNullOrEmpty(_meshPath))
                {
                    GenerateBlocksFromMesh(_meshPath);
                    _showBlockGenDialog = false;
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    _showBlockGenDialog = false;
                }
            }

            ImGui.Separator();

            ImGui.Text("Block Assignment:");

            // Show blocks with material assignment
            if (ImGui.BeginChild("BlocksList", new Vector2(0, 200), ImGuiChildFlags.Border))
            {
                ImGui.Columns(3, "BlockColumns");
                ImGui.Separator();
                ImGui.Text("Block ID"); ImGui.NextColumn();
                ImGui.Text("Material"); ImGui.NextColumn();
                ImGui.Text("Fixed"); ImGui.NextColumn();
                ImGui.Separator();

                for (int i = 0; i < Math.Min(_dataset.Blocks.Count, 100); i++)
                {
                    var block = _dataset.Blocks[i];

                    ImGui.Text($"{block.Id}"); ImGui.NextColumn();

                    // Material combo
                    int currentMat = block.MaterialId;
                    string[] matNames = _dataset.Materials.Select(m => m.Name).ToArray();

                    if (matNames.Length > 0 && ImGui.Combo($"##mat{i}", ref currentMat, matNames, matNames.Length))
                    {
                        block.MaterialId = currentMat;
                        block.Density = _dataset.Materials[currentMat].Density;
                    }

                    ImGui.NextColumn();

                    bool isFixed = block.IsFixed;
                    if (ImGui.Checkbox($"##fix{i}", ref isFixed))
                        block.IsFixed = isFixed;

                    ImGui.NextColumn();
                }

                if (_dataset.Blocks.Count > 100)
                {
                    ImGui.Text($"... and {_dataset.Blocks.Count - 100} more blocks");
                }

                ImGui.Columns(1);
                ImGui.EndChild();
            }

            ImGui.Separator();

            // Block filtering section
            if (ImGui.CollapsingHeader("Filter Blocks"))
            {
                ImGui.TextWrapped("Clean up the mesh by removing unwanted blocks before simulation to prevent messy results.");

                ImGui.Spacing();

                // Show statistics first
                if (_dataset.Blocks.Count > 0 && ImGui.Button("Show Block Statistics"))
                {
                    var stats = BlockFilter.GetStatistics(_dataset.Blocks);
                    Console.WriteLine(stats.GetSummary());
                    _simulationStatus = stats.GetSummary();
                }

                ImGui.Spacing();
                ImGui.Separator();

                // Quick filters
                ImGui.Text("Quick Filters:");

                if (ImGui.Button("Remove Small Blocks (<0.001 m³)"))
                {
                    _dataset.Blocks = BlockFilter.FilterByVolume(_dataset.Blocks, 0.001f, float.MaxValue, true, false);
                }
                ImGui.SameLine();

                if (ImGui.Button("Remove Large Blocks (>100 m³)"))
                {
                    _dataset.Blocks = BlockFilter.FilterByVolume(_dataset.Blocks, 0.0f, 100.0f, false, true);
                }

                if (ImGui.Button("Remove Slivers (Aspect Ratio >10)"))
                {
                    _dataset.Blocks = BlockFilter.FilterByAspectRatio(_dataset.Blocks, 10.0f);
                }
                ImGui.SameLine();

                if (ImGui.Button("Remove Degenerate Blocks"))
                {
                    _dataset.Blocks = BlockFilter.FilterDegenerateGeometry(_dataset.Blocks);
                }

                ImGui.Spacing();
                ImGui.Separator();

                // Custom filter
                ImGui.Text("Custom Volume Filter:");

                float filterMinVol = 0.001f;
                float filterMaxVol = 1000.0f;
                ImGui.InputFloat("Min Volume (m³)##filter", ref filterMinVol);
                ImGui.InputFloat("Max Volume (m³)##filter", ref filterMaxVol);

                if (ImGui.Button("Apply Custom Filter"))
                {
                    _dataset.Blocks = BlockFilter.FilterByVolume(_dataset.Blocks, filterMinVol, filterMaxVol, true, true);
                }
            }
        }

        private void DrawSimulationTab()
        {
            ImGui.Text("Simulation Control");
            ImGui.Separator();

            if (!_isSimulationRunning)
            {
                if (ImGui.Button("Run Simulation", new Vector2(200, 40)))
                {
                    RunSimulation();
                }

                ImGui.SameLine();

                if (ImGui.Button("Validate Setup"))
                {
                    ValidateSetup();
                }
            }
            else
            {
                ImGui.ProgressBar(_simulationProgress, new Vector2(400, 30), $"{_simulationProgress * 100:F0}%");
                ImGui.Text(_simulationStatus);

                if (ImGui.Button("Cancel"))
                {
                    // Cancel simulation
                }
            }

            ImGui.Separator();

            // Results display
            if (_dataset.HasResults)
            {
                ImGui.Text("Simulation Results");
                ImGui.Separator();

                var results = _dataset.Results;

                ImGui.Text($"Status: {(results.Converged ? "Converged" : "Completed")}");
                ImGui.Text($"Total Steps: {results.TotalSteps}");
                ImGui.Text($"Computation Time: {results.ComputationTimeSeconds:F2} s");
                ImGui.Text($"Max Displacement: {results.MaxDisplacement:F4} m");
                ImGui.Text($"Mean Displacement: {results.MeanDisplacement:F4} m");
                ImGui.Text($"Failed Blocks: {results.NumFailedBlocks} / {results.BlockResults.Count}");
                ImGui.Text($"Sliding Contacts: {results.NumSlidingContacts}");
                ImGui.Text($"Opened Joints: {results.NumOpenedJoints}");

                ImGui.Separator();

                if (ImGui.Button("Export Results"))
                {
                    var defaultName = $"{_dataset.Name}_results";
                    _resultsExportDialog.Open(defaultName);
                }
            }
        }

        private void HandleResultsExportDialog()
        {
            if (_resultsExportDialog.Submit())
            {
                try
                {
                    ExportResults(_resultsExportDialog.SelectedPath);
                    _simulationStatus = $"Exported results to {_resultsExportDialog.SelectedPath}";
                }
                catch (Exception ex)
                {
                    _simulationStatus = $"Export failed: {ex.Message}";
                    Logger.LogError($"[SlopeStabilityTools] Export failed: {ex}");
                }
            }
        }

        private void ExportResults(string path)
        {
            if (!_dataset.HasResults || _dataset.Results == null)
                throw new InvalidOperationException("No results available to export.");

            var extension = Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".csv":
                    ExportResultsCsv(path);
                    break;
                case ".vtk":
                    ExportResultsVtk(path);
                    break;
                case ".json":
                    ExportResultsJson(path);
                    break;
                case ".ssr":
                    SlopeStabilityResultsBinarySerializer.Write(path, _dataset.Results);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported export format: {extension}");
            }
        }

        private void ExportResultsCsv(string path)
        {
            using var writer = new StreamWriter(path);

            writer.WriteLine("BlockID,InitialX,InitialY,InitialZ,FinalX,FinalY,FinalZ," +
                             "DisplacementX,DisplacementY,DisplacementZ,DisplacementMag," +
                             "VelocityX,VelocityY,VelocityZ,VelocityMag,Mass,IsFixed,HasFailed");

            foreach (var blockResult in _dataset.Results.BlockResults)
            {
                writer.WriteLine($"{blockResult.BlockId}," +
                                 $"{blockResult.InitialPosition.X},{blockResult.InitialPosition.Y},{blockResult.InitialPosition.Z}," +
                                 $"{blockResult.FinalPosition.X},{blockResult.FinalPosition.Y},{blockResult.FinalPosition.Z}," +
                                 $"{blockResult.Displacement.X},{blockResult.Displacement.Y},{blockResult.Displacement.Z}," +
                                 $"{blockResult.Displacement.Length()}," +
                                 $"{blockResult.Velocity.X},{blockResult.Velocity.Y},{blockResult.Velocity.Z}," +
                                 $"{blockResult.Velocity.Length()}," +
                                 $"{blockResult.Mass},{blockResult.IsFixed},{blockResult.HasFailed}");
            }
        }

        private void ExportResultsVtk(string path)
        {
            using var writer = new StreamWriter(path);

            writer.WriteLine("# vtk DataFile Version 3.0");
            writer.WriteLine("Slope Stability Results");
            writer.WriteLine("ASCII");
            writer.WriteLine("DATASET POLYDATA");

            writer.WriteLine($"POINTS {_dataset.Results.BlockResults.Count} float");
            foreach (var blockResult in _dataset.Results.BlockResults)
            {
                writer.WriteLine($"{blockResult.FinalPosition.X} {blockResult.FinalPosition.Y} {blockResult.FinalPosition.Z}");
            }

            writer.WriteLine($"POINT_DATA {_dataset.Results.BlockResults.Count}");
            writer.WriteLine("SCALARS DisplacementMagnitude float 1");
            writer.WriteLine("LOOKUP_TABLE default");
            foreach (var blockResult in _dataset.Results.BlockResults)
            {
                writer.WriteLine($"{blockResult.Displacement.Length()}");
            }

            writer.WriteLine("VECTORS Displacement float");
            foreach (var blockResult in _dataset.Results.BlockResults)
            {
                writer.WriteLine($"{blockResult.Displacement.X} {blockResult.Displacement.Y} {blockResult.Displacement.Z}");
            }
        }

        private void ExportResultsJson(string path)
        {
            var dto = _dataset.ToDTO();
            var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
        }

        private void GenerateBlocksFromMesh(string meshPath)
        {
            try
            {
                // Load mesh
                var meshName = Path.GetFileNameWithoutExtension(meshPath);
                var mesh = new Mesh3DDataset(meshName, meshPath);
                mesh.Load();

                // Generate blocks
                var generator = new BlockGenerator(_dataset.BlockGenSettings);

                _simulationStatus = "Generating blocks...";
                _isSimulationRunning = true;

                Task.Run(() =>
                {
                    var blocks = generator.GenerateBlocks(mesh, _dataset.JointSets, progress =>
                    {
                        _simulationProgress = progress;
                    });

                    _dataset.Blocks = blocks;
                    _dataset.SourceMeshPath = meshPath;

                    _isSimulationRunning = false;
                    _simulationStatus = $"Generated {blocks.Count} blocks successfully.";
                });
            }
            catch (Exception ex)
            {
                _simulationStatus = $"Error: {ex.Message}";
                _isSimulationRunning = false;
            }
        }

        private void RunSimulation()
        {
            _isSimulationRunning = true;
            _simulationProgress = 0.0f;
            _simulationStatus = "Starting simulation...";

            _simulationTask = Task.Run(() =>
            {
                try
                {
                    var simulator = new SlopeStabilitySimulator(_dataset, _dataset.Parameters);

                    var results = simulator.RunSimulation(
                        progress => _simulationProgress = progress,
                        status => _simulationStatus = status);

                    _dataset.Results = results;
                    _dataset.HasResults = true;

                    _simulationStatus = "Simulation completed successfully.";
                }
                catch (Exception ex)
                {
                    _simulationStatus = $"Simulation error: {ex.Message}";
                }
                finally
                {
                    _isSimulationRunning = false;
                }
            });
        }

        private void ValidateSetup()
        {
            var errors = new List<string>();

            if (_dataset.Blocks.Count == 0)
                errors.Add("No blocks generated. Generate blocks first.");

            if (_dataset.Materials.Count == 0)
                errors.Add("No materials defined.");

            if (_dataset.JointSets.Count == 0)
                errors.Add("Warning: No joint sets defined.");

            if (errors.Count > 0)
            {
                _simulationStatus = "Validation errors:\n" + string.Join("\n", errors);
            }
            else
            {
                _simulationStatus = "Setup is valid. Ready to run simulation.";
            }
        }

        private void DrawViewsTab()
        {
            ImGui.Text("Visualization Options");
            ImGui.Separator();

            ImGui.TextWrapped("This panel provides access to different visualization modes for analyzing slope stability results.");

            ImGui.Spacing();
            ImGui.Spacing();

            // 3D Viewer info
            if (ImGui.CollapsingHeader("3D Viewer", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextWrapped("The 3D viewer shows the complete slope geometry with blocks, joint planes, and simulation results in three dimensions.");
                ImGui.Text("The 3D viewer is always available in the main viewport.");
            }

            ImGui.Spacing();

            // 2D Section Viewer
            if (ImGui.CollapsingHeader("2D Section Viewer", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextWrapped("The 2D section viewer provides cross-sectional views through the slope, similar to professional software like RocFall and Slide.");

                ImGui.Spacing();

                ImGui.Text("Features:");
                ImGui.BulletText("Multiple predefined section planes (along-strike, along-dip, plan view)");
                ImGui.BulletText("Custom section plane definition");
                ImGui.BulletText("Joint trace visualization");
                ImGui.BulletText("Displacement vector display");
                ImGui.BulletText("Color-coded stability factors");
                ImGui.BulletText("Water table visualization");

                ImGui.Spacing();
                ImGui.Separator();

                bool isEnabled = _dataset.Blocks.Count > 0;

                if (!isEnabled)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.5f, 0.0f, 1.0f));
                    ImGui.Text("Generate blocks first to enable 2D section view");
                    ImGui.PopStyleColor();
                }

                ImGui.BeginDisabled(!isEnabled);

                if (ImGui.Button(_show2DViewer ? "Close 2D Section Viewer" : "Open 2D Section Viewer"))
                {
                    _show2DViewer = !_show2DViewer;
                }

                ImGui.EndDisabled();

                if (_show2DViewer)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "(Active)");
                }
            }

            ImGui.Spacing();

            // Future viewers
            if (ImGui.CollapsingHeader("Additional Views"))
            {
                ImGui.TextWrapped("Future visualization options may include:");
                ImGui.BulletText("Stereonet analysis for joint orientation");
                ImGui.BulletText("Time-series graphs for monitoring points");
                ImGui.BulletText("Force chain visualization");
                ImGui.BulletText("Principal stress trajectories");
            }
        }
    }
}
