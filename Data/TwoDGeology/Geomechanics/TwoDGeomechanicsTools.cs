// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/TwoDGeomechanicsTools.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

/// <summary>
/// Current tool mode for geomechanical editing
/// </summary>
public enum GeomechanicsToolMode
{
    None,
    Select,
    DrawRectangle,
    DrawCircle,
    DrawPolygon,
    DrawJoint,
    DrawJointSet,
    DrawFault,
    DrawFoundation,
    DrawRetainingWall,
    DrawTunnel,
    DrawDam,
    DrawIndenter,
    ApplyForce,
    ApplyPressure,
    ApplyDisplacement,
    FixBoundary,
    MeasureDistance,
    MeasureAngle,
    ProbeResults
}

/// <summary>
/// ImGUI-based tools panel for 2D geomechanical simulation
/// </summary>
public class TwoDGeomechanicsTools
{
    #region Fields

    private readonly TwoDGeomechanicalSimulator _simulator;
    private readonly GeomechanicalRenderer2D _renderer;
    private readonly PrimitiveManager2D _primitives;
    private readonly JointSetManager _jointSets;
    private readonly GeomechanicalMaterialLibrary2D _materials;

    private GeomechanicsToolMode _currentMode = GeomechanicsToolMode.None;
    private GeometricPrimitive2D _selectedPrimitive;
    private JointSet2D _selectedJointSet;
    private Discontinuity2D _selectedJoint;
    private int _selectedElementId = -1;
    private int _selectedNodeId = -1;

    // Drawing state
    private readonly List<Vector2> _tempPoints = new();
    private bool _isDrawing;
    private Vector2 _drawStart;

    // Simulation state
    private CancellationTokenSource _simCts;
    private bool _isSimulating;

    // UI state
    private int _selectedMaterialIndex;
    private int _selectedColorMapIndex;
    private bool _showMaterialEditor;
    private bool _showJointSetEditor;
    private bool _showSimulationSetup;
    private bool _showResultsPanel = true;
    private bool _showMohrCircle;

    // New primitive parameters
    private float _newRectWidth = 5f;
    private float _newRectHeight = 3f;
    private float _newCircleRadius = 2f;
    private float _newJointSetSpacing = 1f;
    private float _newJointSetDip = 45f;
    private float _newJointSetVariability = 5f;
    private float _applyForceX;
    private float _applyForceY = -10000f;
    private float _applyPressure = 100000f;
    private float _applyDispX;
    private float _applyDispY = -0.01f;

    // Last mouse position
    private Vector2 _lastMouseWorldPos;

    #endregion

    #region Constructor

    public TwoDGeomechanicsTools(TwoDGeomechanicalSimulator simulator)
    {
        _simulator = simulator;
        _renderer = new GeomechanicalRenderer2D { Mesh = simulator.Mesh };
        _primitives = new PrimitiveManager2D();
        _jointSets = new JointSetManager();
        _materials = simulator.Mesh.Materials;

        _simulator.OnStepCompleted += OnSimulationStepCompleted;
        _simulator.OnSimulationCompleted += OnSimulationCompleted;
        _simulator.OnMessage += OnSimulationMessage;

        // Load default materials
        _materials.LoadDefaults();
    }

    #endregion

    #region Main UI Rendering

    /// <summary>
    /// Render the complete tools panel
    /// </summary>
    public void RenderToolsPanel()
    {
        ImGui.Text("2D Geomechanical Simulation");
        ImGui.Separator();

        RenderToolModeSection();
        ImGui.Separator();

        RenderPrimitivesSection();
        ImGui.Separator();

        RenderJointSetsSection();
        ImGui.Separator();

        RenderMaterialsSection();
        ImGui.Separator();

        RenderBoundaryConditionsSection();
        ImGui.Separator();

        RenderSimulationSection();
        ImGui.Separator();

        RenderVisualizationSection();
    }

    private void RenderToolModeSection()
    {
        ImGui.Text("Tools");

        var buttonSize = new Vector2(110, 0);

        // Row 1: Selection and basic shapes
        if (ToolButton("Select", GeomechanicsToolMode.Select, buttonSize)) { }
        ImGui.SameLine();
        if (ToolButton("Rectangle", GeomechanicsToolMode.DrawRectangle, buttonSize)) { }

        if (ToolButton("Circle", GeomechanicsToolMode.DrawCircle, buttonSize)) { }
        ImGui.SameLine();
        if (ToolButton("Polygon", GeomechanicsToolMode.DrawPolygon, buttonSize)) { }

        // Row 2: Joints
        if (ToolButton("Joint", GeomechanicsToolMode.DrawJoint, buttonSize)) { }
        ImGui.SameLine();
        if (ToolButton("Joint Set", GeomechanicsToolMode.DrawJointSet, buttonSize)) { }

        // Row 3: Engineering structures
        if (ToolButton("Foundation", GeomechanicsToolMode.DrawFoundation, buttonSize)) { }
        ImGui.SameLine();
        if (ToolButton("Ret. Wall", GeomechanicsToolMode.DrawRetainingWall, buttonSize)) { }

        if (ToolButton("Tunnel", GeomechanicsToolMode.DrawTunnel, buttonSize)) { }
        ImGui.SameLine();
        if (ToolButton("Dam", GeomechanicsToolMode.DrawDam, buttonSize)) { }

        if (ToolButton("Indenter", GeomechanicsToolMode.DrawIndenter, buttonSize)) { }
        ImGui.SameLine();
        if (ToolButton("Probe", GeomechanicsToolMode.ProbeResults, buttonSize)) { }

        // Row 4: Boundary conditions
        if (ToolButton("Force", GeomechanicsToolMode.ApplyForce, buttonSize)) { }
        ImGui.SameLine();
        if (ToolButton("Pressure", GeomechanicsToolMode.ApplyPressure, buttonSize)) { }

        if (ToolButton("Displacement", GeomechanicsToolMode.ApplyDisplacement, buttonSize)) { }
        ImGui.SameLine();
        if (ToolButton("Fix Boundary", GeomechanicsToolMode.FixBoundary, buttonSize)) { }

        // Current mode indicator
        ImGui.Text($"Mode: {_currentMode}");

        // Cancel button
        if (_currentMode != GeomechanicsToolMode.None)
        {
            if (ImGui.Button("Cancel (ESC)", new Vector2(-1, 0)))
            {
                CancelCurrentOperation();
            }
        }
    }

    private bool ToolButton(string label, GeomechanicsToolMode mode, Vector2 size)
    {
        bool isSelected = _currentMode == mode;
        if (isSelected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.8f, 1f));
        }

        bool clicked = ImGui.Button(label, size);
        if (clicked)
        {
            _currentMode = mode;
            ClearDrawingState();
        }

        if (isSelected)
        {
            ImGui.PopStyleColor();
        }

        return clicked;
    }

    private void RenderPrimitivesSection()
    {
        if (ImGui.CollapsingHeader("Primitives", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Count: {_primitives.Primitives.Count}");

            // Primitive list
            if (ImGui.BeginListBox("##primitives", new Vector2(-1, 100)))
            {
                foreach (var prim in _primitives.Primitives)
                {
                    bool isSelected = _selectedPrimitive == prim;
                    if (ImGui.Selectable($"{prim.Name} ({prim.Type})", isSelected))
                    {
                        _selectedPrimitive = prim;
                    }
                }
                ImGui.EndListBox();
            }

            // Selected primitive properties
            if (_selectedPrimitive != null)
            {
                ImGui.Text($"Selected: {_selectedPrimitive.Name}");

                var pos = _selectedPrimitive.Position;
                if (ImGui.DragFloat2("Position", ref pos, 0.1f))
                {
                    _selectedPrimitive.Position = pos;
                }

                float rotation = (float)_selectedPrimitive.Rotation;
                if (ImGui.DragFloat("Rotation", ref rotation, 1f, -180, 180))
                {
                    _selectedPrimitive.Rotation = rotation;
                }

                // Material selection
                var matNames = _materials.Materials.Values.Select(m => m.Name).ToArray();
                int matIndex = _materials.Materials.Values.ToList().FindIndex(m => m.Id == _selectedPrimitive.MaterialId);
                if (ImGui.Combo("Material", ref matIndex, matNames, matNames.Length))
                {
                    _selectedPrimitive.MaterialId = _materials.Materials.Values.ElementAt(matIndex).Id;
                }

                // Behavior
                int behaviorIndex = (int)_selectedPrimitive.Behavior;
                if (ImGui.Combo("Behavior", ref behaviorIndex, Enum.GetNames<PrimitiveBehavior>(), Enum.GetValues<PrimitiveBehavior>().Length))
                {
                    _selectedPrimitive.Behavior = (PrimitiveBehavior)behaviorIndex;
                }

                if (ImGui.Button("Delete", new Vector2(-1, 0)))
                {
                    _primitives.RemovePrimitive(_selectedPrimitive.Id);
                    _selectedPrimitive = null;
                }
            }

            // Shape parameters (when in drawing mode)
            if (_currentMode == GeomechanicsToolMode.DrawRectangle)
            {
                ImGui.DragFloat("Width", ref _newRectWidth, 0.1f, 0.1f, 100f);
                ImGui.DragFloat("Height", ref _newRectHeight, 0.1f, 0.1f, 100f);
            }
            else if (_currentMode == GeomechanicsToolMode.DrawCircle)
            {
                ImGui.DragFloat("Radius", ref _newCircleRadius, 0.1f, 0.1f, 50f);
            }

            // Presets
            if (ImGui.Button("Load Preset...", new Vector2(-1, 0)))
            {
                ImGui.OpenPopup("PresetPopup");
            }

            if (ImGui.BeginPopup("PresetPopup"))
            {
                if (ImGui.MenuItem("Bearing Capacity Test"))
                {
                    foreach (var p in PrimitiveManager2D.Presets.CreateBearingCapacityTest())
                    {
                        _primitives.AddPrimitive(p);
                    }
                }
                if (ImGui.MenuItem("Indentation Test"))
                {
                    foreach (var p in PrimitiveManager2D.Presets.CreateIndentationTest())
                    {
                        _primitives.AddPrimitive(p);
                    }
                }
                if (ImGui.MenuItem("Retaining Wall Analysis"))
                {
                    foreach (var p in PrimitiveManager2D.Presets.CreateRetainingWallAnalysis())
                    {
                        _primitives.AddPrimitive(p);
                    }
                }
                ImGui.EndPopup();
            }
        }
    }

    private void RenderJointSetsSection()
    {
        if (ImGui.CollapsingHeader("Joint Sets"))
        {
            ImGui.Text($"Sets: {_jointSets.JointSets.Count}");

            // Joint set list
            if (ImGui.BeginListBox("##jointsets", new Vector2(-1, 80)))
            {
                foreach (var set in _jointSets.JointSets)
                {
                    bool isSelected = _selectedJointSet == set;
                    if (ImGui.Selectable($"{set.Name} ({set.Joints.Count} joints)", isSelected))
                    {
                        _selectedJointSet = set;
                    }
                }
                ImGui.EndListBox();
            }

            // Joint set parameters
            if (_currentMode == GeomechanicsToolMode.DrawJointSet || _selectedJointSet != null)
            {
                ImGui.DragFloat("Spacing (m)", ref _newJointSetSpacing, 0.1f, 0.1f, 10f);
                ImGui.DragFloat("Dip Angle", ref _newJointSetDip, 1f, 0f, 90f);
                ImGui.DragFloat("Variability", ref _newJointSetVariability, 0.5f, 0f, 20f);

                if (_selectedJointSet != null)
                {
                    float friction = (float)_selectedJointSet.FrictionAngle;
                    if (ImGui.DragFloat("Friction Angle", ref friction, 0.5f, 0f, 60f))
                    {
                        _selectedJointSet.FrictionAngle = friction;
                    }

                    float cohesion = (float)(_selectedJointSet.Cohesion / 1000);
                    if (ImGui.DragFloat("Cohesion (kPa)", ref cohesion, 1f, 0f, 1000f))
                    {
                        _selectedJointSet.Cohesion = cohesion * 1000;
                    }

                    var color = _selectedJointSet.Color;
                    if (ImGui.ColorEdit4("Color", ref color))
                    {
                        _selectedJointSet.Color = color;
                    }

                    if (ImGui.Button("Regenerate", new Vector2(-1, 0)))
                    {
                        var (min, max) = _simulator.Mesh.GetBoundingBox();
                        _selectedJointSet.MeanSpacing = _newJointSetSpacing;
                        _selectedJointSet.MeanDipAngle = _newJointSetDip;
                        _selectedJointSet.DipAngleStdDev = _newJointSetVariability;
                        _selectedJointSet.GenerateInRegion(min, max);
                    }

                    if (ImGui.Button("Delete Set", new Vector2(-1, 0)))
                    {
                        _jointSets.RemoveJointSet(_selectedJointSet.Id);
                        _selectedJointSet = null;
                    }
                }
            }

            // Preset joint sets
            if (ImGui.Button("Add Vertical Joints", new Vector2(-1, 0)))
            {
                var set = JointSetManager.Presets.CreateVerticalJoints(_newJointSetSpacing);
                var (min, max) = _simulator.Mesh.GetBoundingBox();
                set.GenerateInRegion(min, max);
                _jointSets.AddJointSet(set);
            }

            if (ImGui.Button("Add Bedding Planes", new Vector2(-1, 0)))
            {
                var set = JointSetManager.Presets.CreateBeddingPlanes(_newJointSetSpacing, _newJointSetDip);
                var (min, max) = _simulator.Mesh.GetBoundingBox();
                set.GenerateInRegion(min, max);
                _jointSets.AddJointSet(set);
            }

            if (ImGui.Button("Add Conjugate Pair", new Vector2(-1, 0)))
            {
                var (set1, set2) = JointSetManager.Presets.CreateTectonicConjugate(_newJointSetDip);
                var (min, max) = _simulator.Mesh.GetBoundingBox();
                set1.MeanSpacing = _newJointSetSpacing;
                set2.MeanSpacing = _newJointSetSpacing;
                set1.GenerateInRegion(min, max);
                set2.GenerateInRegion(min, max);
                _jointSets.AddJointSet(set1);
                _jointSets.AddJointSet(set2);
            }
        }
    }

    private void RenderMaterialsSection()
    {
        if (ImGui.CollapsingHeader("Materials"))
        {
            var matNames = _materials.Materials.Values.Select(m => m.Name).ToArray();
            ImGui.Combo("Selected", ref _selectedMaterialIndex, matNames, matNames.Length);

            if (_selectedMaterialIndex >= 0 && _selectedMaterialIndex < _materials.Materials.Count)
            {
                var mat = _materials.Materials.Values.ElementAt(_selectedMaterialIndex);

                // Quick edit
                float E = (float)(mat.YoungModulus / 1e9);
                if (ImGui.DragFloat("E (GPa)", ref E, 0.1f, 0.001f, 500f))
                {
                    mat.YoungModulus = E * 1e9;
                }

                float nu = (float)mat.PoissonRatio;
                if (ImGui.DragFloat("ν", ref nu, 0.01f, 0.0f, 0.49f))
                {
                    mat.PoissonRatio = nu;
                }

                float c = (float)(mat.Cohesion / 1e6);
                if (ImGui.DragFloat("c (MPa)", ref c, 0.1f, 0f, 100f))
                {
                    mat.Cohesion = c * 1e6;
                }

                float phi = (float)mat.FrictionAngle;
                if (ImGui.DragFloat("φ (°)", ref phi, 0.5f, 0f, 60f))
                {
                    mat.FrictionAngle = phi;
                }

                // Failure criterion
                int critIndex = (int)mat.FailureCriterion;
                if (ImGui.Combo("Criterion", ref critIndex, Enum.GetNames<FailureCriterion2D>(), Enum.GetValues<FailureCriterion2D>().Length))
                {
                    mat.FailureCriterion = (FailureCriterion2D)critIndex;
                }

                // Curved Mohr-Coulomb
                if (mat.FailureCriterion == FailureCriterion2D.CurvedMohrCoulomb)
                {
                    bool useCurved = mat.UseCurvedMohrCoulomb;
                    if (ImGui.Checkbox("Use Curved Envelope", ref useCurved))
                    {
                        mat.UseCurvedMohrCoulomb = useCurved;
                    }

                    if (mat.UseCurvedMohrCoulomb)
                    {
                        float A = (float)mat.CurvedMC_A;
                        if (ImGui.DragFloat("A coefficient", ref A, 0.01f, 0.1f, 2f))
                        {
                            mat.CurvedMC_A = A;
                        }

                        float B = (float)mat.CurvedMC_B;
                        if (ImGui.DragFloat("B exponent", ref B, 0.01f, 0.3f, 1f))
                        {
                            mat.CurvedMC_B = B;
                        }
                    }
                }

                var color = mat.Color;
                if (ImGui.ColorEdit4("Color", ref color))
                {
                    mat.Color = color;
                }
            }

            if (ImGui.Button("Material Editor...", new Vector2(-1, 0)))
            {
                _showMaterialEditor = true;
            }
        }
    }

    private void RenderBoundaryConditionsSection()
    {
        if (ImGui.CollapsingHeader("Boundary Conditions"))
        {
            // Applied force input
            if (_currentMode == GeomechanicsToolMode.ApplyForce)
            {
                ImGui.DragFloat("Force X (N)", ref _applyForceX, 100f);
                ImGui.DragFloat("Force Y (N)", ref _applyForceY, 100f);
                ImGui.TextWrapped("Click on nodes to apply force");
            }

            // Applied pressure input
            if (_currentMode == GeomechanicsToolMode.ApplyPressure)
            {
                ImGui.DragFloat("Pressure (Pa)", ref _applyPressure, 1000f);
                ImGui.TextWrapped("Click on edge to apply pressure");
            }

            // Applied displacement input
            if (_currentMode == GeomechanicsToolMode.ApplyDisplacement)
            {
                ImGui.DragFloat("Disp X (m)", ref _applyDispX, 0.001f);
                ImGui.DragFloat("Disp Y (m)", ref _applyDispY, 0.001f);
                ImGui.TextWrapped("Click on nodes to prescribe displacement");
            }

            // Quick boundary fixes
            ImGui.Separator();
            ImGui.Text("Quick Boundary Fixes:");

            if (ImGui.Button("Fix Bottom", new Vector2(-1, 0)))
            {
                _simulator.Mesh.FixBottom();
            }
            if (ImGui.Button("Fix Left", new Vector2(-1, 0)))
            {
                _simulator.Mesh.FixLeft();
            }
            if (ImGui.Button("Fix Right", new Vector2(-1, 0)))
            {
                _simulator.Mesh.FixRight();
            }
            if (ImGui.Button("Roller Left/Right", new Vector2(-1, 0)))
            {
                _simulator.Mesh.FixLeft();
                _simulator.Mesh.FixRight();
            }

            // Gravity
            bool applyGravity = _simulator.ApplyGravity;
            if (ImGui.Checkbox("Apply Gravity", ref applyGravity))
            {
                _simulator.ApplyGravity = applyGravity;
            }
        }
    }

    private void RenderSimulationSection()
    {
        if (ImGui.CollapsingHeader("Simulation", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Analysis type
            int analysisIndex = (int)_simulator.AnalysisType;
            if (ImGui.Combo("Analysis", ref analysisIndex, Enum.GetNames<AnalysisType2D>(), Enum.GetValues<AnalysisType2D>().Length))
            {
                _simulator.AnalysisType = (AnalysisType2D)analysisIndex;
            }

            // Solver type
            int solverIndex = (int)_simulator.SolverType;
            if (ImGui.Combo("Solver", ref solverIndex, Enum.GetNames<SolverType2D>(), Enum.GetValues<SolverType2D>().Length))
            {
                _simulator.SolverType = (SolverType2D)solverIndex;
            }

            // Load steps
            int loadSteps = _simulator.NumLoadSteps;
            if (ImGui.DragInt("Load Steps", ref loadSteps, 1, 1, 100))
            {
                _simulator.NumLoadSteps = loadSteps;
            }

            // Dynamic parameters (if applicable)
            if (_simulator.AnalysisType == AnalysisType2D.Dynamic ||
                _simulator.AnalysisType == AnalysisType2D.ImplicitDynamic)
            {
                double dt = _simulator.TimeStep;
                if (ImGui.InputDouble("Time Step (s)", ref dt, 0.0001, 0.001))
                {
                    _simulator.TimeStep = dt;
                }

                double totalTime = _simulator.TotalTime;
                if (ImGui.InputDouble("Total Time (s)", ref totalTime, 0.1, 1))
                {
                    _simulator.TotalTime = totalTime;
                }
            }

            ImGui.Separator();

            // Mesh operations
            if (ImGui.Button("Generate Mesh", new Vector2(-1, 0)))
            {
                GenerateMesh();
            }

            if (ImGui.Button("Check Mesh Quality", new Vector2(-1, 0)))
            {
                _simulator.CheckMeshQuality();
            }

            ImGui.Separator();

            // Run/Stop buttons
            if (_isSimulating)
            {
                if (ImGui.Button("Stop Simulation", new Vector2(-1, 0)))
                {
                    StopSimulation();
                }

                // Progress
                var state = _simulator.State;
                float progress = state.TotalSteps > 0 ? (float)state.CurrentStep / state.TotalSteps : 0;
                ImGui.ProgressBar(progress, new Vector2(-1, 0), $"Step {state.CurrentStep}/{state.TotalSteps}");

                ImGui.Text($"Residual: {state.ResidualNorm:E3}");
                ImGui.Text($"Max Disp: {state.MaxDisplacement:E3} m");
                ImGui.Text($"Plastic Elements: {state.NumPlasticElements}");
                ImGui.Text($"Failed Elements: {state.NumFailedElements}");
            }
            else
            {
                if (ImGui.Button("Run Simulation", new Vector2(-1, 0)))
                {
                    RunSimulation();
                }
            }

            ImGui.Separator();

            if (ImGui.Button("Reset", new Vector2(-1, 0)))
            {
                _simulator.Mesh.Reset();
                _simulator.InitializeResults();
                _renderer.Results = _simulator.Results;
            }
        }
    }

    private void RenderVisualizationSection()
    {
        if (ImGui.CollapsingHeader("Visualization", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Display field
            var fieldNames = ResultFieldInfo.GetAllFields()
                .Select(f => ResultFieldInfo.GetDisplayName(f)).ToArray();
            int fieldIndex = (int)_renderer.DisplayField;
            if (ImGui.Combo("Field", ref fieldIndex, fieldNames, fieldNames.Length))
            {
                _renderer.DisplayField = (ResultField2D)fieldIndex;
            }

            // Color map
            int colorMapIndex = (int)_renderer.ColorMap.Type;
            if (ImGui.Combo("Color Map", ref colorMapIndex, Enum.GetNames<ColorMapType>(), Enum.GetValues<ColorMapType>().Length))
            {
                _renderer.ColorMap.Type = (ColorMapType)colorMapIndex;
            }

            // Auto scale
            bool autoScale = _renderer.ColorMap.AutoScale;
            if (ImGui.Checkbox("Auto Scale", ref autoScale))
            {
                _renderer.ColorMap.AutoScale = autoScale;
            }

            if (!autoScale)
            {
                double minVal = _renderer.ColorMap.MinValue;
                double maxVal = _renderer.ColorMap.MaxValue;
                if (ImGui.InputDouble("Min", ref minVal))
                {
                    _renderer.ColorMap.MinValue = minVal;
                }
                if (ImGui.InputDouble("Max", ref maxVal))
                {
                    _renderer.ColorMap.MaxValue = maxVal;
                }
            }

            // Display options
            bool showMesh = _renderer.ShowMesh;
            if (ImGui.Checkbox("Show Mesh", ref showMesh))
            {
                _renderer.ShowMesh = showMesh;
            }

            bool showDeformed = _renderer.ShowDeformed;
            if (ImGui.Checkbox("Show Deformed", ref showDeformed))
            {
                _renderer.ShowDeformed = showDeformed;
            }

            if (showDeformed)
            {
                float defScale = _renderer.DeformationScale;
                if (ImGui.DragFloat("Def. Scale", ref defScale, 0.1f, 0.1f, 1000f))
                {
                    _renderer.DeformationScale = defScale;
                }
            }

            bool showBC = _renderer.ShowBoundaryConditions;
            if (ImGui.Checkbox("Show BCs", ref showBC))
            {
                _renderer.ShowBoundaryConditions = showBC;
            }

            bool showLoads = _renderer.ShowLoads;
            if (ImGui.Checkbox("Show Loads", ref showLoads))
            {
                _renderer.ShowLoads = showLoads;
            }

            // Vector display
            int vectorMode = (int)_renderer.VectorMode;
            if (ImGui.Combo("Vectors", ref vectorMode, Enum.GetNames<VectorDisplayMode>(), Enum.GetValues<VectorDisplayMode>().Length))
            {
                _renderer.VectorMode = (VectorDisplayMode)vectorMode;
            }

            // Mohr circle
            ImGui.Checkbox("Show Mohr Circle", ref _showMohrCircle);
            if (_showMohrCircle && _selectedElementId >= 0)
            {
                _renderer.ShowMohrCircle = true;
                _renderer.MohrCircleElementId = _selectedElementId;
            }
            else
            {
                _renderer.ShowMohrCircle = false;
            }
        }
    }

    #endregion

    #region Material Editor Window

    public void RenderMaterialEditorWindow()
    {
        if (!_showMaterialEditor) return;

        if (ImGui.Begin("Material Editor", ref _showMaterialEditor))
        {
            // Material list
            if (ImGui.BeginChild("MaterialList", new Vector2(150, 0), ImGuiChildFlags.Border))
            {
                foreach (var mat in _materials.Materials.Values)
                {
                    if (ImGui.Selectable(mat.Name, mat.Id == _selectedMaterialIndex + 1))
                    {
                        _selectedMaterialIndex = _materials.Materials.Values.ToList().IndexOf(mat);
                    }
                }
            }
            ImGui.EndChild();

            ImGui.SameLine();

            // Material properties
            if (ImGui.BeginChild("MaterialProps", Vector2.Zero))
            {
                if (_selectedMaterialIndex >= 0 && _selectedMaterialIndex < _materials.Materials.Count)
                {
                    var mat = _materials.Materials.Values.ElementAt(_selectedMaterialIndex);
                    RenderMaterialProperties(mat);
                }
            }
            ImGui.EndChild();
        }
        ImGui.End();
    }

    private void RenderMaterialProperties(GeomechanicalMaterial2D mat)
    {
        string name = mat.Name;
        if (ImGui.InputText("Name", ref name, 256))
        {
            mat.Name = name;
        }

        ImGui.Separator();
        ImGui.Text("Elastic Properties");

        float E = (float)(mat.YoungModulus / 1e9);
        if (ImGui.DragFloat("Young's Modulus (GPa)", ref E, 0.1f, 0.001f, 500f))
        {
            mat.YoungModulus = E * 1e9;
        }

        float nu = (float)mat.PoissonRatio;
        if (ImGui.DragFloat("Poisson's Ratio", ref nu, 0.01f, 0.0f, 0.49f))
        {
            mat.PoissonRatio = nu;
        }

        float density = (float)mat.Density;
        if (ImGui.DragFloat("Density (kg/m³)", ref density, 10f, 100f, 10000f))
        {
            mat.Density = density;
        }

        // Display derived quantities
        ImGui.TextDisabled($"Shear Modulus G = {mat.ShearModulus / 1e9:F2} GPa");
        ImGui.TextDisabled($"Bulk Modulus K = {mat.BulkModulus / 1e9:F2} GPa");

        ImGui.Separator();
        ImGui.Text("Strength Properties");

        float c = (float)(mat.Cohesion / 1e6);
        if (ImGui.DragFloat("Cohesion (MPa)", ref c, 0.1f, 0f, 100f))
        {
            mat.Cohesion = c * 1e6;
        }

        float phi = (float)mat.FrictionAngle;
        if (ImGui.DragFloat("Friction Angle (°)", ref phi, 0.5f, 0f, 60f))
        {
            mat.FrictionAngle = phi;
        }

        float psi = (float)mat.DilationAngle;
        if (ImGui.DragFloat("Dilation Angle (°)", ref psi, 0.5f, 0f, 30f))
        {
            mat.DilationAngle = psi;
        }

        float T = (float)(mat.TensileStrength / 1e6);
        if (ImGui.DragFloat("Tensile Strength (MPa)", ref T, 0.1f, 0f, 50f))
        {
            mat.TensileStrength = T * 1e6;
        }

        ImGui.TextDisabled($"UCS ≈ {mat.UCS / 1e6:F1} MPa");

        ImGui.Separator();
        ImGui.Text("Failure Criterion");

        int critIndex = (int)mat.FailureCriterion;
        if (ImGui.Combo("Type", ref critIndex, Enum.GetNames<FailureCriterion2D>(), Enum.GetValues<FailureCriterion2D>().Length))
        {
            mat.FailureCriterion = (FailureCriterion2D)critIndex;
        }

        if (mat.FailureCriterion == FailureCriterion2D.CurvedMohrCoulomb)
        {
            ImGui.Separator();
            ImGui.Text("Curved Mohr-Coulomb Parameters");
            ImGui.TextWrapped("τ = A(σn + T)^B");

            bool useCurved = mat.UseCurvedMohrCoulomb;
            if (ImGui.Checkbox("Enable Curved Envelope", ref useCurved))
            {
                mat.UseCurvedMohrCoulomb = useCurved;
            }

            float A = (float)mat.CurvedMC_A;
            if (ImGui.DragFloat("A coefficient", ref A, 0.01f, 0.1f, 2f))
            {
                mat.CurvedMC_A = A;
            }

            float B = (float)mat.CurvedMC_B;
            if (ImGui.DragFloat("B exponent", ref B, 0.01f, 0.3f, 1f))
            {
                mat.CurvedMC_B = B;
            }
        }

        if (mat.FailureCriterion == FailureCriterion2D.HoekBrown)
        {
            ImGui.Separator();
            ImGui.Text("Hoek-Brown Parameters");

            float mi = (float)mat.HB_mi;
            if (ImGui.DragFloat("mi", ref mi, 0.5f, 1f, 35f))
            {
                mat.HB_mi = mi;
            }

            float GSI = (float)mat.GSI;
            if (ImGui.DragFloat("GSI", ref GSI, 1f, 10f, 100f))
            {
                mat.GSI = GSI;
            }

            float D = (float)mat.DisturbanceFactor;
            if (ImGui.DragFloat("Disturbance D", ref D, 0.05f, 0f, 1f))
            {
                mat.DisturbanceFactor = D;
            }

            if (ImGui.Button("Calculate mb, s, a from GSI"))
            {
                mat.CalculateHoekBrownFromGSI();
            }

            ImGui.TextDisabled($"mb = {mat.HB_mb:F3}, s = {mat.HB_s:F4}, a = {mat.HB_a:F3}");
        }

        ImGui.Separator();
        var color = mat.Color;
        if (ImGui.ColorEdit4("Display Color", ref color))
        {
            mat.Color = color;
        }
    }

    #endregion

    #region Mouse and Keyboard Input

    public void HandleMouseInput(Vector2 worldPos, bool leftClick, bool rightClick, bool isDragging)
    {
        _lastMouseWorldPos = worldPos;

        switch (_currentMode)
        {
            case GeomechanicsToolMode.Select:
                if (leftClick) SelectAt(worldPos);
                break;

            case GeomechanicsToolMode.DrawRectangle:
                if (leftClick) PlaceRectangle(worldPos);
                break;

            case GeomechanicsToolMode.DrawCircle:
                if (leftClick) PlaceCircle(worldPos);
                break;

            case GeomechanicsToolMode.DrawPolygon:
                if (leftClick) AddPolygonPoint(worldPos);
                if (rightClick) FinishPolygon();
                break;

            case GeomechanicsToolMode.DrawJoint:
                if (leftClick) AddJointPoint(worldPos);
                if (rightClick) FinishJoint();
                break;

            case GeomechanicsToolMode.DrawJointSet:
                if (leftClick && !_isDrawing) { _drawStart = worldPos; _isDrawing = true; }
                if (rightClick && _isDrawing) FinishJointSetRegion(worldPos);
                break;

            case GeomechanicsToolMode.DrawFoundation:
                if (leftClick) PlaceFoundation(worldPos);
                break;

            case GeomechanicsToolMode.DrawRetainingWall:
                if (leftClick) PlaceRetainingWall(worldPos);
                break;

            case GeomechanicsToolMode.DrawTunnel:
                if (leftClick) PlaceTunnel(worldPos);
                break;

            case GeomechanicsToolMode.DrawDam:
                if (leftClick) PlaceDam(worldPos);
                break;

            case GeomechanicsToolMode.DrawIndenter:
                if (leftClick) PlaceIndenter(worldPos);
                break;

            case GeomechanicsToolMode.ApplyForce:
                if (leftClick) ApplyForceAtPosition(worldPos);
                break;

            case GeomechanicsToolMode.ApplyDisplacement:
                if (leftClick) ApplyDisplacementAtPosition(worldPos);
                break;

            case GeomechanicsToolMode.FixBoundary:
                if (leftClick) FixBoundaryAtPosition(worldPos);
                break;

            case GeomechanicsToolMode.ProbeResults:
                if (leftClick) ProbeResultsAtPosition(worldPos);
                break;
        }
    }

    public void HandleKeyboardInput()
    {
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            CancelCurrentOperation();
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && _selectedPrimitive != null)
        {
            _primitives.RemovePrimitive(_selectedPrimitive.Id);
            _selectedPrimitive = null;
        }
    }

    #endregion

    #region Tool Operations

    private void SelectAt(Vector2 pos)
    {
        // Try to select primitive
        var prim = _primitives.GetPrimitiveAt(pos);
        if (prim != null)
        {
            _selectedPrimitive = prim;
            _selectedJointSet = null;
            _selectedJoint = null;
            return;
        }

        // Try to select element
        var nodes = _simulator.Mesh.Nodes.ToArray();
        for (int e = 0; e < _simulator.Mesh.Elements.Count; e++)
        {
            var element = _simulator.Mesh.Elements[e];
            if (element.GetArea(nodes) > 0)
            {
                var centroid = element.GetCentroid(nodes);
                if (Vector2.Distance(pos, centroid) < 1.0f)
                {
                    _selectedElementId = e;
                    _selectedPrimitive = null;
                    return;
                }
            }
        }

        // Try to select node
        foreach (var node in _simulator.Mesh.Nodes)
        {
            if (Vector2.Distance(pos, node.InitialPosition) < 0.5f)
            {
                _selectedNodeId = node.Id;
                return;
            }
        }

        // Clear selection
        _selectedPrimitive = null;
        _selectedElementId = -1;
        _selectedNodeId = -1;
    }

    private void PlaceRectangle(Vector2 pos)
    {
        var rect = new RectanglePrimitive
        {
            Position = pos,
            Width = _newRectWidth,
            Height = _newRectHeight,
            MaterialId = _materials.Materials.Values.First().Id,
            Name = $"Rectangle {_primitives.Primitives.Count + 1}"
        };
        _primitives.AddPrimitive(rect);
    }

    private void PlaceCircle(Vector2 pos)
    {
        var circle = new CirclePrimitive
        {
            Position = pos,
            Radius = _newCircleRadius,
            MaterialId = _materials.Materials.Values.First().Id,
            Name = $"Circle {_primitives.Primitives.Count + 1}"
        };
        _primitives.AddPrimitive(circle);
    }

    private void AddPolygonPoint(Vector2 pos)
    {
        _tempPoints.Add(pos);
        _isDrawing = true;
    }

    private void FinishPolygon()
    {
        if (_tempPoints.Count >= 3)
        {
            var poly = new PolygonPrimitive
            {
                Position = Vector2.Zero,
                LocalVertices = new List<Vector2>(_tempPoints),
                MaterialId = _materials.Materials.Values.First().Id,
                Name = $"Polygon {_primitives.Primitives.Count + 1}"
            };
            _primitives.AddPrimitive(poly);
        }
        ClearDrawingState();
    }

    private void AddJointPoint(Vector2 pos)
    {
        _tempPoints.Add(pos);
        _isDrawing = true;
    }

    private void FinishJoint()
    {
        if (_tempPoints.Count >= 2)
        {
            // Create manual joint set with single joint
            var set = new JointSet2D
            {
                Name = $"Manual Joint {_jointSets.JointSets.Count + 1}",
                MeanSpacing = 100, // Large so only one joint
                FrictionAngle = 30,
                Cohesion = 0
            };

            var joint = new Discontinuity2D
            {
                StartPoint = _tempPoints[0],
                EndPoint = _tempPoints[^1],
                Points = new List<Vector2>(_tempPoints),
                FrictionAngle = set.FrictionAngle,
                Cohesion = set.Cohesion
            };
            set.Joints.Add(joint);

            _jointSets.AddJointSet(set);
        }
        ClearDrawingState();
    }

    private void FinishJointSetRegion(Vector2 endPos)
    {
        var set = new JointSet2D
        {
            Name = $"Joint Set {_jointSets.JointSets.Count + 1}",
            MeanDipAngle = _newJointSetDip,
            DipAngleStdDev = _newJointSetVariability,
            MeanSpacing = _newJointSetSpacing
        };

        var min = new Vector2(Math.Min(_drawStart.X, endPos.X), Math.Min(_drawStart.Y, endPos.Y));
        var max = new Vector2(Math.Max(_drawStart.X, endPos.X), Math.Max(_drawStart.Y, endPos.Y));
        set.GenerateInRegion(min, max);

        _jointSets.AddJointSet(set);
        ClearDrawingState();
    }

    private void PlaceFoundation(Vector2 pos)
    {
        var foundation = new FoundationPrimitive
        {
            Position = pos,
            Width = _newRectWidth,
            Height = 0.5,
            MaterialId = _materials.Materials.Values.FirstOrDefault(m => m.Name == "Concrete")?.Id ?? 1,
            Name = $"Foundation {_primitives.Primitives.Count + 1}"
        };
        _primitives.AddPrimitive(foundation);
    }

    private void PlaceRetainingWall(Vector2 pos)
    {
        var wall = new RetainingWallPrimitive
        {
            Position = pos,
            Height = 5,
            MaterialId = _materials.Materials.Values.FirstOrDefault(m => m.Name == "Concrete")?.Id ?? 1,
            Name = $"Retaining Wall {_primitives.Primitives.Count + 1}"
        };
        _primitives.AddPrimitive(wall);
    }

    private void PlaceTunnel(Vector2 pos)
    {
        var tunnel = new TunnelPrimitive
        {
            Position = pos,
            Width = 10,
            Height = 8,
            Name = $"Tunnel {_primitives.Primitives.Count + 1}"
        };
        _primitives.AddPrimitive(tunnel);
    }

    private void PlaceDam(Vector2 pos)
    {
        var dam = new DamPrimitive
        {
            Position = pos,
            Height = 30,
            MaterialId = _materials.Materials.Values.FirstOrDefault(m => m.Name == "Concrete")?.Id ?? 1,
            Name = $"Dam {_primitives.Primitives.Count + 1}"
        };
        _primitives.AddPrimitive(dam);
    }

    private void PlaceIndenter(Vector2 pos)
    {
        var indenter = new IndenterPrimitive
        {
            Position = pos,
            Width = 0.5,
            Height = 1.0,
            MaterialId = _materials.Materials.Values.FirstOrDefault(m => m.Name == "Steel")?.Id ?? 1,
            Name = $"Indenter {_primitives.Primitives.Count + 1}"
        };
        _primitives.AddPrimitive(indenter);
    }

    private void ApplyForceAtPosition(Vector2 pos)
    {
        // Find nearest node
        int nearestNode = -1;
        float minDist = float.MaxValue;

        foreach (var node in _simulator.Mesh.Nodes)
        {
            float dist = Vector2.Distance(pos, node.InitialPosition);
            if (dist < minDist)
            {
                minDist = dist;
                nearestNode = node.Id;
            }
        }

        if (nearestNode >= 0 && minDist < 2.0f)
        {
            _simulator.Mesh.ApplyNodalForce(nearestNode, _applyForceX, _applyForceY);
            Logger.Log($"Applied force ({_applyForceX}, {_applyForceY}) N to node {nearestNode}");
        }
    }

    private void ApplyDisplacementAtPosition(Vector2 pos)
    {
        int nearestNode = -1;
        float minDist = float.MaxValue;

        foreach (var node in _simulator.Mesh.Nodes)
        {
            float dist = Vector2.Distance(pos, node.InitialPosition);
            if (dist < minDist)
            {
                minDist = dist;
                nearestNode = node.Id;
            }
        }

        if (nearestNode >= 0 && minDist < 2.0f)
        {
            _simulator.Mesh.ApplyPrescribedDisplacement(nearestNode, _applyDispX, _applyDispY);
            Logger.Log($"Applied prescribed displacement ({_applyDispX}, {_applyDispY}) m to node {nearestNode}");
        }
    }

    private void FixBoundaryAtPosition(Vector2 pos)
    {
        int nearestNode = -1;
        float minDist = float.MaxValue;

        foreach (var node in _simulator.Mesh.Nodes)
        {
            float dist = Vector2.Distance(pos, node.InitialPosition);
            if (dist < minDist)
            {
                minDist = dist;
                nearestNode = node.Id;
            }
        }

        if (nearestNode >= 0 && minDist < 2.0f)
        {
            _simulator.Mesh.FixNode(nearestNode, true, true);
            Logger.Log($"Fixed node {nearestNode}");
        }
    }

    private void ProbeResultsAtPosition(Vector2 pos)
    {
        // Find nearest element
        var nodes = _simulator.Mesh.Nodes.ToArray();
        int nearestElement = -1;
        float minDist = float.MaxValue;

        foreach (var element in _simulator.Mesh.Elements)
        {
            var centroid = element.GetCentroid(nodes);
            float dist = Vector2.Distance(pos, centroid);
            if (dist < minDist)
            {
                minDist = dist;
                nearestElement = element.Id;
            }
        }

        if (nearestElement >= 0)
        {
            _selectedElementId = nearestElement;
            _renderer.MohrCircleElementId = nearestElement;

            if (_simulator.Results != null && nearestElement < _simulator.Results.StressXX.Length)
            {
                var r = _simulator.Results;
                Logger.Log($"Element {nearestElement}:");
                Logger.Log($"  σxx = {r.StressXX[nearestElement] / 1e6:F3} MPa");
                Logger.Log($"  σyy = {r.StressYY[nearestElement] / 1e6:F3} MPa");
                Logger.Log($"  τxy = {r.StressXY[nearestElement] / 1e6:F3} MPa");
                Logger.Log($"  σ1 = {r.Sigma1[nearestElement] / 1e6:F3} MPa");
                Logger.Log($"  σ2 = {r.Sigma2[nearestElement] / 1e6:F3} MPa");
                Logger.Log($"  Von Mises = {r.VonMisesStress[nearestElement] / 1e6:F3} MPa");
            }
        }
    }

    private void CancelCurrentOperation()
    {
        _currentMode = GeomechanicsToolMode.None;
        ClearDrawingState();
    }

    private void ClearDrawingState()
    {
        _tempPoints.Clear();
        _isDrawing = false;
    }

    #endregion

    #region Mesh and Simulation

    private void GenerateMesh()
    {
        _simulator.Mesh.Clear();

        // Generate mesh for all primitives
        _primitives.GenerateAllMeshes(_simulator.Mesh);

        // Apply boundary conditions from primitives
        _primitives.ApplyAllBoundaryConditions(_simulator.Mesh);

        // Insert joints
        _jointSets.InsertIntoMesh(_simulator.Mesh);

        Logger.Log($"Generated mesh: {_simulator.Mesh.Nodes.Count} nodes, {_simulator.Mesh.Elements.Count} elements");
    }

    private async void RunSimulation()
    {
        _isSimulating = true;
        _simCts = new CancellationTokenSource();

        try
        {
            _renderer.Results = _simulator.Results;
            await _simulator.RunAsync(_simCts.Token);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Simulation error: {ex.Message}");
        }
        finally
        {
            _isSimulating = false;
        }
    }

    private void StopSimulation()
    {
        _simCts?.Cancel();
        _isSimulating = false;
    }

    private void OnSimulationStepCompleted(SimulationState2D state)
    {
        _renderer.Results = _simulator.Results;
    }

    private void OnSimulationCompleted(SimulationResults2D results)
    {
        _isSimulating = false;
        _renderer.Results = results;
        Logger.Log("Simulation completed");
    }

    private void OnSimulationMessage(string message)
    {
        Logger.Log(message);
    }

    #endregion

    #region Rendering

    public void RenderOverlay(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        // Render mesh and results
        _renderer.Mesh = _simulator.Mesh;
        _renderer.Render(drawList, worldToScreen);

        // Render primitives
        RenderPrimitives(drawList, worldToScreen);

        // Render joint sets
        RenderJointSets(drawList, worldToScreen);

        // Render drawing preview
        RenderDrawingPreview(drawList, worldToScreen);

        // Render selection highlight
        RenderSelection(drawList, worldToScreen);
    }

    private void RenderPrimitives(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        foreach (var prim in _primitives.Primitives.Where(p => p.IsVisible))
        {
            var vertices = prim.GetVertices();
            if (vertices.Count < 2) continue;

            uint color = ImGui.ColorConvertFloat4ToU32(prim.Color);
            uint outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 1f));

            // Draw filled polygon
            var screenVerts = vertices.Select(v => worldToScreen(v)).ToArray();
            if (vertices.Count == 3)
            {
                drawList.AddTriangleFilled(screenVerts[0], screenVerts[1], screenVerts[2], color);
            }
            else if (vertices.Count == 4)
            {
                drawList.AddQuadFilled(screenVerts[0], screenVerts[1], screenVerts[2], screenVerts[3], color);
            }
            else
            {
                // Triangulate for drawing
                for (int i = 1; i < vertices.Count - 1; i++)
                {
                    drawList.AddTriangleFilled(screenVerts[0], screenVerts[i], screenVerts[i + 1], color);
                }
            }

            // Draw outline
            for (int i = 0; i < vertices.Count; i++)
            {
                int j = (i + 1) % vertices.Count;
                drawList.AddLine(screenVerts[i], screenVerts[j], outlineColor, 2);
            }
        }
    }

    private void RenderJointSets(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        foreach (var set in _jointSets.JointSets.Where(s => s.IsVisible))
        {
            uint jointColor = ImGui.ColorConvertFloat4ToU32(set.Color);

            foreach (var joint in set.Joints)
            {
                if (joint.Points.Count >= 2)
                {
                    for (int i = 0; i < joint.Points.Count - 1; i++)
                    {
                        drawList.AddLine(
                            worldToScreen(joint.Points[i]),
                            worldToScreen(joint.Points[i + 1]),
                            jointColor, 2);
                    }
                }
                else
                {
                    drawList.AddLine(
                        worldToScreen(joint.StartPoint),
                        worldToScreen(joint.EndPoint),
                        jointColor, 2);
                }
            }
        }
    }

    private void RenderDrawingPreview(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        if (!_isDrawing && _tempPoints.Count == 0) return;

        uint previewColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 0.8f));

        // Draw temp points
        for (int i = 0; i < _tempPoints.Count; i++)
        {
            drawList.AddCircleFilled(worldToScreen(_tempPoints[i]), 5, previewColor);
            if (i > 0)
            {
                drawList.AddLine(
                    worldToScreen(_tempPoints[i - 1]),
                    worldToScreen(_tempPoints[i]),
                    previewColor, 2);
            }
        }

        // Draw line to cursor
        if (_tempPoints.Count > 0)
        {
            drawList.AddLine(
                worldToScreen(_tempPoints[^1]),
                worldToScreen(_lastMouseWorldPos),
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 0.5f)), 1);
        }

        // Draw region for joint set
        if (_currentMode == GeomechanicsToolMode.DrawJointSet && _isDrawing)
        {
            var minScreen = worldToScreen(new Vector2(
                Math.Min(_drawStart.X, _lastMouseWorldPos.X),
                Math.Min(_drawStart.Y, _lastMouseWorldPos.Y)));
            var maxScreen = worldToScreen(new Vector2(
                Math.Max(_drawStart.X, _lastMouseWorldPos.X),
                Math.Max(_drawStart.Y, _lastMouseWorldPos.Y)));

            drawList.AddRect(minScreen, maxScreen, previewColor, 0, ImDrawFlags.None, 2);
        }
    }

    private void RenderSelection(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        uint selectColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 1f, 1f));

        // Highlight selected primitive
        if (_selectedPrimitive != null)
        {
            var vertices = _selectedPrimitive.GetVertices();
            var screenVerts = vertices.Select(v => worldToScreen(v)).ToArray();

            for (int i = 0; i < vertices.Count; i++)
            {
                int j = (i + 1) % vertices.Count;
                drawList.AddLine(screenVerts[i], screenVerts[j], selectColor, 3);
            }
        }

        // Highlight selected element
        if (_selectedElementId >= 0 && _selectedElementId < _simulator.Mesh.Elements.Count)
        {
            var element = _simulator.Mesh.Elements[_selectedElementId];
            var nodes = _simulator.Mesh.Nodes.ToArray();

            for (int i = 0; i < element.NodeIds.Count; i++)
            {
                int j = (i + 1) % element.NodeIds.Count;
                var p1 = nodes[element.NodeIds[i]].InitialPosition;
                var p2 = nodes[element.NodeIds[j]].InitialPosition;
                drawList.AddLine(worldToScreen(p1), worldToScreen(p2), selectColor, 3);
            }
        }

        // Highlight selected node
        if (_selectedNodeId >= 0 && _selectedNodeId < _simulator.Mesh.Nodes.Count)
        {
            var node = _simulator.Mesh.Nodes[_selectedNodeId];
            drawList.AddCircle(worldToScreen(node.InitialPosition), 8, selectColor, 16, 3);
        }
    }

    public void RenderColorBar(ImDrawListPtr drawList, Vector2 position, Vector2 size)
    {
        _renderer.RenderColorBar(drawList, position, size);
    }

    public void RenderMohrCircle(ImDrawListPtr drawList, Vector2 center, float radius)
    {
        if (_showMohrCircle)
        {
            _renderer.RenderMohrCircle(drawList, center, radius);
        }
    }

    #endregion

    #region Public Properties

    public GeomechanicalRenderer2D Renderer => _renderer;
    public PrimitiveManager2D Primitives => _primitives;
    public JointSetManager JointSets => _jointSets;
    public GeomechanicsToolMode CurrentMode => _currentMode;
    public bool IsSimulating => _isSimulating;
    public GeometricPrimitive2D SelectedPrimitive => _selectedPrimitive;
    public int SelectedElementId => _selectedElementId;

    #endregion
}
