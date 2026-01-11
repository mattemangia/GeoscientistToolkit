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

    // Editing helpers
    private readonly SnappingSystem _snapping;
    private readonly TransformHandleSystem _transformHandles;
    private readonly UndoRedoManager _undoRedo;
    private readonly DeformationPreview _deformationPreview;
    private readonly MeasurementSystem _measurement;
    private readonly EditContext _editContext;

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
    private PrimitiveState _dragStartState;

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
    private bool _showSnappingOptions;
    private bool _showCoordinates = true;

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
    private Vector2 _lastSnappedPos;
    private SnapResult _lastSnapResult;

    // Zoom level for handle sizing
    private float _currentZoom = 1f;

    #endregion

    #region Constructor

    public TwoDGeomechanicsTools(TwoDGeomechanicalSimulator simulator)
    {
        _simulator = simulator;
        _renderer = new GeomechanicalRenderer2D { Mesh = simulator.Mesh };
        _primitives = new PrimitiveManager2D();
        _jointSets = new JointSetManager();
        _materials = simulator.Mesh.Materials;

        // Initialize editing helpers
        _snapping = new SnappingSystem
        {
            GridSpacing = 0.5f,
            SnapTolerance = 0.3f,
            AngleSnapIncrement = 15f,
            EnabledModes = SnapMode.Grid | SnapMode.Node | SnapMode.Vertex | SnapMode.Angle
        };

        _transformHandles = new TransformHandleSystem();
        _deformationPreview = new DeformationPreview();
        _measurement = new MeasurementSystem();

        // Create edit context for undo/redo
        _editContext = new EditContext
        {
            Primitives = _primitives,
            JointSets = _jointSets,
            Mesh = simulator.Mesh,
            Simulator = simulator
        };
        _undoRedo = new UndoRedoManager(_editContext);

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

        RenderUndoRedoSection();
        ImGui.Separator();

        RenderToolModeSection();
        ImGui.Separator();

        RenderSnappingSection();
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
        ImGui.Separator();

        RenderCoordinatesSection();
    }

    private void RenderUndoRedoSection()
    {
        var buttonSize = new Vector2(80, 0);

        // Undo button
        bool canUndo = _undoRedo.CanUndo;
        if (!canUndo) ImGui.BeginDisabled();
        if (ImGui.Button("Undo", buttonSize))
        {
            _undoRedo.Undo();
        }
        if (ImGui.IsItemHovered() && canUndo)
        {
            ImGui.SetTooltip($"Undo: {_undoRedo.UndoDescription}");
        }
        if (!canUndo) ImGui.EndDisabled();

        ImGui.SameLine();

        // Redo button
        bool canRedo = _undoRedo.CanRedo;
        if (!canRedo) ImGui.BeginDisabled();
        if (ImGui.Button("Redo", buttonSize))
        {
            _undoRedo.Redo();
        }
        if (ImGui.IsItemHovered() && canRedo)
        {
            ImGui.SetTooltip($"Redo: {_undoRedo.RedoDescription}");
        }
        if (!canRedo) ImGui.EndDisabled();

        // Keyboard shortcuts hint
        ImGui.TextDisabled("Ctrl+Z / Ctrl+Y");
    }

    private void RenderSnappingSection()
    {
        if (ImGui.CollapsingHeader("Snapping"))
        {
            bool snapEnabled = _snapping.IsEnabled;
            if (ImGui.Checkbox("Enable Snapping", ref snapEnabled))
            {
                _snapping.IsEnabled = snapEnabled;
            }

            if (_snapping.IsEnabled)
            {
                // Grid snapping
                bool gridSnap = _snapping.EnabledModes.HasFlag(SnapMode.Grid);
                if (ImGui.Checkbox("Grid", ref gridSnap))
                {
                    _snapping.EnabledModes = gridSnap
                        ? _snapping.EnabledModes | SnapMode.Grid
                        : _snapping.EnabledModes & ~SnapMode.Grid;
                }

                if (gridSnap)
                {
                    ImGui.SameLine();
                    float gridSize = _snapping.GridSpacing;
                    ImGui.SetNextItemWidth(80);
                    if (ImGui.DragFloat("##gridsize", ref gridSize, 0.1f, 0.1f, 10f))
                    {
                        _snapping.GridSpacing = gridSize;
                    }
                }

                // Node snapping
                bool nodeSnap = _snapping.EnabledModes.HasFlag(SnapMode.Node);
                if (ImGui.Checkbox("Nodes", ref nodeSnap))
                {
                    _snapping.EnabledModes = nodeSnap
                        ? _snapping.EnabledModes | SnapMode.Node
                        : _snapping.EnabledModes & ~SnapMode.Node;
                }

                ImGui.SameLine();

                // Vertex snapping
                bool vertexSnap = _snapping.EnabledModes.HasFlag(SnapMode.Vertex);
                if (ImGui.Checkbox("Vertices", ref vertexSnap))
                {
                    _snapping.EnabledModes = vertexSnap
                        ? _snapping.EnabledModes | SnapMode.Vertex
                        : _snapping.EnabledModes & ~SnapMode.Vertex;
                }

                // Edge snapping
                bool edgeSnap = _snapping.EnabledModes.HasFlag(SnapMode.Edge);
                if (ImGui.Checkbox("Edges", ref edgeSnap))
                {
                    _snapping.EnabledModes = edgeSnap
                        ? _snapping.EnabledModes | SnapMode.Edge
                        : _snapping.EnabledModes & ~SnapMode.Edge;
                }

                ImGui.SameLine();

                // Center snapping
                bool centerSnap = _snapping.EnabledModes.HasFlag(SnapMode.Center);
                if (ImGui.Checkbox("Centers", ref centerSnap))
                {
                    _snapping.EnabledModes = centerSnap
                        ? _snapping.EnabledModes | SnapMode.Center
                        : _snapping.EnabledModes & ~SnapMode.Center;
                }

                // Midpoint snapping
                bool midpointSnap = _snapping.EnabledModes.HasFlag(SnapMode.Midpoint);
                if (ImGui.Checkbox("Midpoints", ref midpointSnap))
                {
                    _snapping.EnabledModes = midpointSnap
                        ? _snapping.EnabledModes | SnapMode.Midpoint
                        : _snapping.EnabledModes & ~SnapMode.Midpoint;
                }

                ImGui.SameLine();

                // Angle snapping
                bool angleSnap = _snapping.EnabledModes.HasFlag(SnapMode.Angle);
                if (ImGui.Checkbox("Angles", ref angleSnap))
                {
                    _snapping.EnabledModes = angleSnap
                        ? _snapping.EnabledModes | SnapMode.Angle
                        : _snapping.EnabledModes & ~SnapMode.Angle;
                }

                if (angleSnap)
                {
                    float angleInc = _snapping.AngleSnapIncrement;
                    ImGui.SetNextItemWidth(100);
                    if (ImGui.DragFloat("Angle Increment", ref angleInc, 1f, 1f, 45f))
                    {
                        _snapping.AngleSnapIncrement = angleInc;
                    }
                }

                // Snap tolerance
                float tolerance = _snapping.SnapTolerance;
                if (ImGui.DragFloat("Tolerance", ref tolerance, 0.05f, 0.1f, 2f))
                {
                    _snapping.SnapTolerance = tolerance;
                }

                // Show current snap status
                if (_lastSnapResult.Snapped)
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Snap: {_lastSnapResult.Description}");
                }
            }
        }
    }

    private void RenderCoordinatesSection()
    {
        if (ImGui.CollapsingHeader("Coordinates & Measurement", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Checkbox("Show Coordinates", ref _showCoordinates);

            if (_showCoordinates)
            {
                ImGui.Text($"Mouse: {MeasurementSystem.FormatCoordinate(_lastMouseWorldPos)}");

                if (_lastSnapResult.Snapped)
                {
                    ImGui.Text($"Snapped: {MeasurementSystem.FormatCoordinate(_lastSnappedPos)}");
                }
            }

            // Measurement tools
            ImGui.Separator();
            ImGui.Text("Measurement:");

            var buttonSize = new Vector2(70, 0);

            int measureType = (int)_measurement.MeasureType;
            if (ImGui.Combo("Type", ref measureType, new[] { "Distance", "Polyline", "Angle", "Area", "Point" }, 5))
            {
                _measurement.MeasureType = (MeasurementType)measureType;
            }

            if (_measurement.IsMeasuring)
            {
                switch (_measurement.MeasureType)
                {
                    case MeasurementType.Distance:
                        ImGui.Text($"Distance: {MeasurementSystem.FormatDistance(_measurement.GetDistance())}");
                        break;
                    case MeasurementType.Polyline:
                        ImGui.Text($"Length: {MeasurementSystem.FormatDistance(_measurement.GetPolylineLength())}");
                        ImGui.Text($"Points: {_measurement.MeasurePoints.Count}");
                        break;
                    case MeasurementType.Angle:
                        if (_measurement.MeasurePoints.Count >= 3)
                        {
                            ImGui.Text($"Angle: {MeasurementSystem.FormatAngle(_measurement.GetAngle())}");
                        }
                        else
                        {
                            ImGui.Text($"Points: {_measurement.MeasurePoints.Count}/3");
                        }
                        break;
                    case MeasurementType.Area:
                        if (_measurement.MeasurePoints.Count >= 3)
                        {
                            ImGui.Text($"Area: {MeasurementSystem.FormatArea(_measurement.GetArea())}");
                        }
                        ImGui.Text($"Points: {_measurement.MeasurePoints.Count}");
                        break;
                }

                if (ImGui.Button("Clear", buttonSize))
                {
                    _measurement.ClearMeasurement();
                }
            }
        }
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

    public void HandleMouseInput(Vector2 worldPos, bool leftClick, bool rightClick, bool isDragging, float zoom = 1f)
    {
        _lastMouseWorldPos = worldPos;
        _currentZoom = zoom;
        _measurement.CurrentPosition = worldPos;

        // Apply snapping
        _lastSnapResult = _snapping.Snap(worldPos, _simulator.Mesh, _primitives);
        _lastSnappedPos = _lastSnapResult.Snapped ? _lastSnapResult.Position : worldPos;
        Vector2 snappedPos = _lastSnappedPos;

        // Handle transform handles for selected primitive
        if (_currentMode == GeomechanicsToolMode.Select && _selectedPrimitive != null)
        {
            // Update hover state
            _transformHandles.UpdateHover(worldPos, _selectedPrimitive, zoom);

            if (leftClick && !_transformHandles.IsDragging)
            {
                // Start dragging if clicking on a handle
                _transformHandles.BeginDrag(worldPos, _selectedPrimitive, zoom);
                if (_transformHandles.IsDragging)
                {
                    _dragStartState = new PrimitiveState(_selectedPrimitive);
                    return;
                }
            }

            if (_transformHandles.IsDragging)
            {
                if (isDragging)
                {
                    _transformHandles.UpdateDrag(worldPos, _selectedPrimitive, _snapping);
                }
                else
                {
                    // End drag - record for undo
                    if (_dragStartState != null)
                    {
                        var op = new ModifyPrimitiveOperation(_selectedPrimitive, _dragStartState);
                        _undoRedo.RecordOperation(op);
                        _dragStartState = null;
                    }
                    _transformHandles.EndDrag();
                }
                return;
            }
        }

        // Handle measurement mode
        if (_currentMode == GeomechanicsToolMode.MeasureDistance || _currentMode == GeomechanicsToolMode.MeasureAngle)
        {
            if (leftClick)
            {
                if (!_measurement.IsMeasuring)
                {
                    _measurement.StartMeasurement(snappedPos);
                }
                else
                {
                    _measurement.AddPoint(snappedPos);
                }
            }
            if (rightClick)
            {
                _measurement.EndMeasurement();
            }
            return;
        }

        switch (_currentMode)
        {
            case GeomechanicsToolMode.Select:
                if (leftClick) SelectAt(worldPos);
                break;

            case GeomechanicsToolMode.DrawRectangle:
                if (leftClick) PlaceRectangle(snappedPos);
                break;

            case GeomechanicsToolMode.DrawCircle:
                if (leftClick) PlaceCircle(snappedPos);
                break;

            case GeomechanicsToolMode.DrawPolygon:
                if (leftClick) AddPolygonPoint(snappedPos);
                if (rightClick) FinishPolygon();
                break;

            case GeomechanicsToolMode.DrawJoint:
                if (leftClick) AddJointPoint(snappedPos);
                if (rightClick) FinishJoint();
                break;

            case GeomechanicsToolMode.DrawJointSet:
                if (leftClick && !_isDrawing) { _drawStart = snappedPos; _isDrawing = true; }
                if (rightClick && _isDrawing) FinishJointSetRegion(snappedPos);
                break;

            case GeomechanicsToolMode.DrawFoundation:
                if (leftClick) PlaceFoundation(snappedPos);
                break;

            case GeomechanicsToolMode.DrawRetainingWall:
                if (leftClick) PlaceRetainingWall(snappedPos);
                break;

            case GeomechanicsToolMode.DrawTunnel:
                if (leftClick) PlaceTunnel(snappedPos);
                break;

            case GeomechanicsToolMode.DrawDam:
                if (leftClick) PlaceDam(snappedPos);
                break;

            case GeomechanicsToolMode.DrawIndenter:
                if (leftClick) PlaceIndenter(snappedPos);
                break;

            case GeomechanicsToolMode.ApplyForce:
                if (leftClick) ApplyForceAtPosition(snappedPos);
                break;

            case GeomechanicsToolMode.ApplyDisplacement:
                if (leftClick) ApplyDisplacementAtPosition(snappedPos);
                break;

            case GeomechanicsToolMode.FixBoundary:
                if (leftClick) FixBoundaryAtPosition(snappedPos);
                break;

            case GeomechanicsToolMode.ProbeResults:
                if (leftClick) ProbeResultsAtPosition(worldPos);
                break;
        }
    }

    public void HandleKeyboardInput()
    {
        bool ctrlPressed = ImGui.GetIO().KeyCtrl;
        bool shiftPressed = ImGui.GetIO().KeyShift;

        // Escape - cancel current operation
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            if (_transformHandles.IsDragging)
            {
                _transformHandles.CancelDrag(_selectedPrimitive);
                _dragStartState = null;
            }
            else
            {
                CancelCurrentOperation();
            }
        }

        // Delete - delete selected primitive
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && _selectedPrimitive != null)
        {
            var op = new RemovePrimitiveOperation(_selectedPrimitive);
            _undoRedo.RecordOperation(op);
            _primitives.RemovePrimitive(_selectedPrimitive.Id);
            _selectedPrimitive = null;
        }

        // Ctrl+Z - Undo
        if (ctrlPressed && ImGui.IsKeyPressed(ImGuiKey.Z) && !shiftPressed)
        {
            _undoRedo.Undo();
        }

        // Ctrl+Y or Ctrl+Shift+Z - Redo
        if ((ctrlPressed && ImGui.IsKeyPressed(ImGuiKey.Y)) ||
            (ctrlPressed && shiftPressed && ImGui.IsKeyPressed(ImGuiKey.Z)))
        {
            _undoRedo.Redo();
        }

        // G - Toggle grid snap
        if (ImGui.IsKeyPressed(ImGuiKey.G) && !ctrlPressed)
        {
            _snapping.EnabledModes ^= SnapMode.Grid;
        }

        // N - Toggle node snap
        if (ImGui.IsKeyPressed(ImGuiKey.N) && !ctrlPressed)
        {
            _snapping.EnabledModes ^= SnapMode.Node;
        }

        // V - Toggle vertex snap
        if (ImGui.IsKeyPressed(ImGuiKey.V) && !ctrlPressed)
        {
            _snapping.EnabledModes ^= SnapMode.Vertex;
        }

        // A - Toggle angle snap
        if (ImGui.IsKeyPressed(ImGuiKey.A) && !ctrlPressed)
        {
            _snapping.EnabledModes ^= SnapMode.Angle;
        }

        // S - Toggle snapping on/off
        if (ImGui.IsKeyPressed(ImGuiKey.S) && !ctrlPressed)
        {
            _snapping.IsEnabled = !_snapping.IsEnabled;
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
        // Render grid if enabled
        if (_snapping.IsEnabled && _snapping.EnabledModes.HasFlag(SnapMode.Grid))
        {
            RenderGrid(drawList, worldToScreen);
        }

        // Render mesh and results
        _renderer.Mesh = _simulator.Mesh;
        _renderer.Render(drawList, worldToScreen);

        // Render primitives (with deformation preview if active)
        RenderPrimitives(drawList, worldToScreen);

        // Render joint sets
        RenderJointSets(drawList, worldToScreen);

        // Render drawing preview
        RenderDrawingPreview(drawList, worldToScreen);

        // Render selection highlight and transform handles
        RenderSelection(drawList, worldToScreen);

        // Render snap indicator
        RenderSnapIndicator(drawList, worldToScreen);

        // Render measurement overlay
        RenderMeasurement(drawList, worldToScreen);

        // Render coordinate display
        if (_showCoordinates)
        {
            RenderCoordinateDisplay(drawList, worldToScreen);
        }
    }

    private void RenderGrid(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        var (min, max) = _simulator.Mesh.GetBoundingBox();

        // Extend grid beyond mesh bounds
        float padding = _snapping.GridSpacing * 5;
        min -= new Vector2(padding, padding);
        max += new Vector2(padding, padding);

        // Snap bounds to grid
        min = new Vector2(
            MathF.Floor(min.X / _snapping.GridSpacing) * _snapping.GridSpacing,
            MathF.Floor(min.Y / _snapping.GridSpacing) * _snapping.GridSpacing);
        max = new Vector2(
            MathF.Ceiling(max.X / _snapping.GridSpacing) * _snapping.GridSpacing,
            MathF.Ceiling(max.Y / _snapping.GridSpacing) * _snapping.GridSpacing);

        uint gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 0.3f));
        uint majorColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 0.4f));

        int majorInterval = 5;

        // Vertical lines
        int i = 0;
        for (float x = min.X; x <= max.X; x += _snapping.GridSpacing)
        {
            var p1 = worldToScreen(new Vector2(x, min.Y));
            var p2 = worldToScreen(new Vector2(x, max.Y));
            uint color = (i % majorInterval == 0) ? majorColor : gridColor;
            drawList.AddLine(p1, p2, color, 1);
            i++;
        }

        // Horizontal lines
        i = 0;
        for (float y = min.Y; y <= max.Y; y += _snapping.GridSpacing)
        {
            var p1 = worldToScreen(new Vector2(min.X, y));
            var p2 = worldToScreen(new Vector2(max.X, y));
            uint color = (i % majorInterval == 0) ? majorColor : gridColor;
            drawList.AddLine(p1, p2, color, 1);
            i++;
        }
    }

    private void RenderSnapIndicator(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        if (!_snapping.IsEnabled || !_lastSnapResult.Snapped) return;

        var screenPos = worldToScreen(_lastSnappedPos);

        uint snapColor = _lastSnapResult.SnapType switch
        {
            SnapMode.Grid => ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 1f, 0.8f)),
            SnapMode.Node => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.5f, 0f, 0.9f)),
            SnapMode.Vertex => ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 0f, 0.9f)),
            SnapMode.Edge => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 0.8f)),
            SnapMode.Center => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0f, 1f, 0.9f)),
            SnapMode.Midpoint => ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 1f, 0.9f)),
            SnapMode.Intersection => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0f, 0f, 0.9f)),
            _ => ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.8f))
        };

        // Draw snap marker
        float markerSize = 8;
        switch (_lastSnapResult.SnapType)
        {
            case SnapMode.Grid:
                // Small cross
                drawList.AddLine(screenPos - new Vector2(markerSize, 0), screenPos + new Vector2(markerSize, 0), snapColor, 2);
                drawList.AddLine(screenPos - new Vector2(0, markerSize), screenPos + new Vector2(0, markerSize), snapColor, 2);
                break;
            case SnapMode.Node:
            case SnapMode.Vertex:
                // Square
                drawList.AddRect(screenPos - new Vector2(markerSize, markerSize), screenPos + new Vector2(markerSize, markerSize), snapColor, 0, ImDrawFlags.None, 2);
                break;
            case SnapMode.Center:
                // Circle with cross
                drawList.AddCircle(screenPos, markerSize, snapColor, 16, 2);
                drawList.AddLine(screenPos - new Vector2(markerSize * 0.7f, 0), screenPos + new Vector2(markerSize * 0.7f, 0), snapColor, 1);
                drawList.AddLine(screenPos - new Vector2(0, markerSize * 0.7f), screenPos + new Vector2(0, markerSize * 0.7f), snapColor, 1);
                break;
            case SnapMode.Midpoint:
                // Triangle
                drawList.AddTriangle(screenPos + new Vector2(0, -markerSize),
                                     screenPos + new Vector2(-markerSize, markerSize),
                                     screenPos + new Vector2(markerSize, markerSize),
                                     snapColor, 2);
                break;
            case SnapMode.Edge:
                // Perpendicular mark
                drawList.AddLine(screenPos - new Vector2(markerSize, markerSize), screenPos + new Vector2(markerSize, -markerSize), snapColor, 2);
                break;
            case SnapMode.Intersection:
                // X mark
                drawList.AddLine(screenPos - new Vector2(markerSize, markerSize), screenPos + new Vector2(markerSize, markerSize), snapColor, 2);
                drawList.AddLine(screenPos - new Vector2(-markerSize, markerSize), screenPos + new Vector2(-markerSize, -markerSize), snapColor, 2);
                break;
            default:
                drawList.AddCircleFilled(screenPos, 4, snapColor);
                break;
        }
    }

    private void RenderMeasurement(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        if (_measurement.MeasurePoints.Count == 0) return;

        uint lineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0f, 1f));
        uint pointColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.5f, 0f, 1f));
        uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));

        // Draw points
        foreach (var p in _measurement.MeasurePoints)
        {
            var screenPos = worldToScreen(p);
            drawList.AddCircleFilled(screenPos, 5, pointColor);
        }

        // Draw lines
        for (int i = 1; i < _measurement.MeasurePoints.Count; i++)
        {
            var p1 = worldToScreen(_measurement.MeasurePoints[i - 1]);
            var p2 = worldToScreen(_measurement.MeasurePoints[i]);
            drawList.AddLine(p1, p2, lineColor, 2);

            // Draw distance label
            var mid = (p1 + p2) / 2;
            float dist = Vector2.Distance(_measurement.MeasurePoints[i - 1], _measurement.MeasurePoints[i]);
            drawList.AddText(mid + new Vector2(5, -15), textColor, MeasurementSystem.FormatDistance(dist, 2));
        }

        // Draw line to cursor if measuring
        if (_measurement.IsMeasuring && _measurement.MeasurePoints.Count > 0)
        {
            var lastPoint = worldToScreen(_measurement.MeasurePoints[^1]);
            var cursorPos = worldToScreen(_lastSnappedPos);
            uint previewColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0f, 0.5f));
            drawList.AddLine(lastPoint, cursorPos, previewColor, 1);
        }
    }

    private void RenderCoordinateDisplay(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        var screenPos = worldToScreen(_lastMouseWorldPos);

        // Display coordinates near cursor
        string coordText = MeasurementSystem.FormatCoordinate(_lastSnappedPos);
        uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.9f, 0.8f));
        uint bgColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.5f));

        var textPos = screenPos + new Vector2(15, 10);
        var textSize = ImGui.CalcTextSize(coordText);

        // Background
        drawList.AddRectFilled(textPos - new Vector2(2, 2), textPos + textSize + new Vector2(4, 2), bgColor, 3);

        // Text
        drawList.AddText(textPos, textColor, coordText);
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

            // Draw transform handles
            RenderTransformHandles(drawList, worldToScreen);
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

    private void RenderTransformHandles(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        if (_selectedPrimitive == null || _currentMode != GeomechanicsToolMode.Select) return;

        var handles = _transformHandles.GetHandles(_selectedPrimitive, _currentZoom);

        foreach (var handle in handles)
        {
            var screenPos = worldToScreen(handle.Position);
            bool isHovered = _transformHandles.HoveredHandle == handle.Type;
            bool isActive = _transformHandles.ActiveHandle == handle.Type;

            uint handleColor = isActive
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0f, 1f))
                : isHovered
                    ? ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 1f, 1f))
                    : ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.8f));

            uint fillColor = isActive
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0f, 0.5f))
                : isHovered
                    ? ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 1f, 0.3f))
                    : ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 0.5f));

            float size = handle.Size * _currentZoom;

            switch (handle.Type)
            {
                case HandleType.Move:
                    // Cross arrows for move handle
                    drawList.AddCircleFilled(screenPos, size, fillColor);
                    drawList.AddCircle(screenPos, size, handleColor, 16, 2);
                    // Arrow indicators
                    drawList.AddLine(screenPos - new Vector2(size * 0.6f, 0), screenPos + new Vector2(size * 0.6f, 0), handleColor, 2);
                    drawList.AddLine(screenPos - new Vector2(0, size * 0.6f), screenPos + new Vector2(0, size * 0.6f), handleColor, 2);
                    break;

                case HandleType.ScaleTopLeft:
                case HandleType.ScaleTopRight:
                case HandleType.ScaleBottomLeft:
                case HandleType.ScaleBottomRight:
                    // Square handles for corner scale
                    drawList.AddRectFilled(screenPos - new Vector2(size, size), screenPos + new Vector2(size, size), fillColor);
                    drawList.AddRect(screenPos - new Vector2(size, size), screenPos + new Vector2(size, size), handleColor, 0, ImDrawFlags.None, 2);
                    break;

                case HandleType.ScaleTop:
                case HandleType.ScaleBottom:
                    // Horizontal bar for vertical scale
                    drawList.AddRectFilled(screenPos - new Vector2(size * 1.5f, size * 0.6f), screenPos + new Vector2(size * 1.5f, size * 0.6f), fillColor);
                    drawList.AddRect(screenPos - new Vector2(size * 1.5f, size * 0.6f), screenPos + new Vector2(size * 1.5f, size * 0.6f), handleColor, 0, ImDrawFlags.None, 2);
                    break;

                case HandleType.ScaleLeft:
                case HandleType.ScaleRight:
                    // Vertical bar for horizontal scale
                    drawList.AddRectFilled(screenPos - new Vector2(size * 0.6f, size * 1.5f), screenPos + new Vector2(size * 0.6f, size * 1.5f), fillColor);
                    drawList.AddRect(screenPos - new Vector2(size * 0.6f, size * 1.5f), screenPos + new Vector2(size * 0.6f, size * 1.5f), handleColor, 0, ImDrawFlags.None, 2);
                    break;

                case HandleType.RotateTopLeft:
                case HandleType.RotateTopRight:
                case HandleType.RotateBottomLeft:
                case HandleType.RotateBottomRight:
                    // Circular handles for rotation
                    drawList.AddCircleFilled(screenPos, size * 0.8f, fillColor);
                    drawList.AddCircle(screenPos, size * 0.8f, handleColor, 16, 2);
                    // Rotation arc indicator
                    float startAngle = handle.Type switch
                    {
                        HandleType.RotateTopLeft => MathF.PI * 0.75f,
                        HandleType.RotateTopRight => MathF.PI * 0.25f,
                        HandleType.RotateBottomRight => -MathF.PI * 0.25f,
                        HandleType.RotateBottomLeft => -MathF.PI * 0.75f,
                        _ => 0
                    };
                    for (int i = 0; i < 8; i++)
                    {
                        float a1 = startAngle - MathF.PI * 0.25f * i / 8;
                        float a2 = startAngle - MathF.PI * 0.25f * (i + 1) / 8;
                        var p1 = screenPos + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * size * 1.5f;
                        var p2 = screenPos + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * size * 1.5f;
                        drawList.AddLine(p1, p2, handleColor, 1);
                    }
                    break;
            }
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
