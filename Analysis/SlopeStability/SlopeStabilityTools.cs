using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Mesh3D;
using ImGuiNET;
using System.Threading.Tasks;

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

        public SlopeStabilityTools(SlopeStabilityDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not SlopeStabilityDataset slopeDataset)
                return;

            // Tab bar
            string[] tabNames = new[] {
                "Joint Sets",
                "Materials",
                "Parameters",
                "Earthquakes",
                "Blocks",
                "Simulation"
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
                        }

                        ImGui.EndTabItem();
                    }
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawJointSetsTab()
        {
            ImGui.Text("Joint Sets Configuration");
            ImGui.Separator();

            // List of joint sets
            if (ImGui.BeginChild("JointSetsList", new Vector2(200, 0), true))
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
            if (ImGui.BeginChild("JointSetEditor"))
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

                    ImGui.InputText("Name", ref jointSet.Name, 100);

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
            if (ImGui.BeginChild("MaterialsList", new Vector2(200, 0), true))
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
            if (ImGui.BeginChild("MaterialEditor"))
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

                    ImGui.InputText("Name", ref material.Name, 100);

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

                        ImGui.Checkbox("Enable Plasticity", ref material.ConstitutiveModel.EnablePlasticity);
                        ImGui.Checkbox("Enable Brittle Failure", ref material.ConstitutiveModel.EnableBrittleFailure);

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
                ImGui.Checkbox("Use Custom Gravity Direction", ref param.UseCustomGravityDirection);

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

                ImGui.Checkbox("Adaptive Damping", ref param.UseAdaptiveDamping);

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
                ImGui.Checkbox("Use Multithreading", ref param.UseMultithreading);

                if (param.UseMultithreading)
                {
                    int numThreads = param.NumThreads;
                    if (ImGui.InputInt("Num Threads (0=auto)", ref numThreads))
                        param.NumThreads = Math.Max(numThreads, 0);
                }

                ImGui.Checkbox("Use SIMD", ref param.UseSIMD);
                ImGui.Checkbox("Include Rotation", ref param.IncludeRotation);
                ImGui.Checkbox("Include Fluid Pressure", ref param.IncludeFluidPressure);

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

                ImGui.Checkbox("Save Intermediate States", ref param.SaveIntermediateStates);
                ImGui.Checkbox("Compute Final State", ref param.ComputeFinalState);
            }
        }

        private void DrawEarthquakesTab()
        {
            ImGui.Text("Earthquake Loading");
            ImGui.Separator();

            ImGui.Checkbox("Enable Earthquake Loading", ref _dataset.Parameters.EnableEarthquakeLoading);

            if (!_dataset.Parameters.EnableEarthquakeLoading)
                return;

            // List of earthquakes
            if (ImGui.BeginChild("EarthquakesList", new Vector2(200, 0), true))
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
            if (ImGui.BeginChild("EarthquakeEditor"))
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
                        ImGui.Checkbox("Radial Propagation", ref eq.UseRadialPropagation);

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

                float targetSize = settings.TargetBlockSize;
                if (ImGui.InputFloat("Target Block Size (m)", ref targetSize))
                    settings.TargetBlockSize = Math.Max(targetSize, 0.1f);

                float minVolume = settings.MinimumBlockVolume;
                if (ImGui.InputFloat("Minimum Volume (m³)", ref minVolume))
                    settings.MinimumBlockVolume = Math.Max(minVolume, 0.0001f);

                ImGui.Checkbox("Remove Small Blocks", ref settings.RemoveSmallBlocks);
                ImGui.Checkbox("Merge Sliver Blocks", ref settings.MergeSliverBlocks);

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
            if (ImGui.BeginChild("BlocksList", new Vector2(0, 200), true))
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
                    // Open export dialog
                }
            }
        }

        private void GenerateBlocksFromMesh(string meshPath)
        {
            try
            {
                // Load mesh
                var mesh = new Mesh3DDataset(meshPath);
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
    }
}
