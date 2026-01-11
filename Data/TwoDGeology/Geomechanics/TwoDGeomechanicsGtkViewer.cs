// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/TwoDGeomechanicsGtkViewer.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Gtk;
using Cairo;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

/// <summary>
/// GTK-based viewer for 2D geomechanical simulations.
/// Provides full visualization and editing capabilities similar to the ImGui version.
/// </summary>
public class TwoDGeomechanicsGtkViewer : Gtk.Box
{
    #region Fields

    private readonly TwoDGeomechanicalSimulator _simulator;
    private readonly GeomechanicalMaterialLibrary2D _materials;
    private readonly PrimitiveManager2D _primitives;
    private readonly JointSetManager _jointSets;

    // UI Components
    private readonly DrawingArea _canvas;
    private Notebook _notebook;
    private TreeView _primitiveList;
    private TreeView _materialList;
    private TreeView _jointSetList;
    private readonly ListStore _primitiveStore;
    private readonly ListStore _materialStore;
    private readonly ListStore _jointSetStore;

    // Panels
    private Box _toolsPanel;
    private Box _propertiesPanel;
    private ComboBoxText _resultFieldCombo;
    private ComboBoxText _colorMapCombo;
    private Scale _deformationScale;
    private ProgressBar _progressBar;
    private Label _statusLabel;

    // Visualization state
    private ResultField2D _displayField = ResultField2D.DisplacementMagnitude;
    private ColorMapType _colorMapType = ColorMapType.Jet;
    private double _deformScale = 1.0;
    private bool _showMesh = true;
    private bool _showDeformed = true;
    private bool _showBCs = true;
    private bool _showLoads = true;
    private bool _showJoints = true;

    // Interaction state
    private GeomechanicsToolMode _currentMode = GeomechanicsToolMode.Select;
    private Vector2 _panOffset = Vector2.Zero;
    private float _zoom = 1.0f;
    private bool _isPanning;
    private Vector2 _lastMousePos;
    private readonly List<Vector2> _tempPoints = new();
    private bool _isDrawing;
    private Vector2 _drawStart;

    // Selection
    private GeometricPrimitive2D _selectedPrimitive;
    private int _selectedElementId = -1;
    private int _selectedNodeId = -1;

    // Simulation
    private CancellationTokenSource _simCts;
    private bool _isSimulating;

    // Color map for rendering
    private readonly ColorMap _colorMap = new();

    #endregion

    #region Constructor

    public TwoDGeomechanicsGtkViewer() : base(Orientation.Horizontal, 5)
    {
        _simulator = new TwoDGeomechanicalSimulator();
        _materials = _simulator.Mesh.Materials;
        _primitives = new PrimitiveManager2D();
        _jointSets = new JointSetManager();

        _materials.LoadDefaults();

        // Create stores
        _primitiveStore = new ListStore(typeof(string), typeof(string), typeof(int));
        _materialStore = new ListStore(typeof(string), typeof(int));
        _jointSetStore = new ListStore(typeof(string), typeof(int), typeof(int));

        // Left panel - Tools
        _toolsPanel = CreateToolsPanel();
        PackStart(_toolsPanel, false, false, 5);

        // Center - Canvas
        var canvasFrame = new Frame("Viewport");
        _canvas = new DrawingArea();
        _canvas.SetSizeRequest(800, 600);
        _canvas.Drawn += OnCanvasDrawn;
        _canvas.AddEvents((int)(Gdk.EventMask.ButtonPressMask |
                                 Gdk.EventMask.ButtonReleaseMask |
                                 Gdk.EventMask.PointerMotionMask |
                                 Gdk.EventMask.ScrollMask));
        _canvas.ButtonPressEvent += OnCanvasButtonPress;
        _canvas.ButtonReleaseEvent += OnCanvasButtonRelease;
        _canvas.MotionNotifyEvent += OnCanvasMotion;
        _canvas.ScrollEvent += OnCanvasScroll;

        canvasFrame.Add(_canvas);
        PackStart(canvasFrame, true, true, 5);

        // Right panel - Properties and visualization
        _propertiesPanel = CreatePropertiesPanel();
        PackEnd(_propertiesPanel, false, false, 5);

        // Status bar
        var statusBox = new Box(Orientation.Horizontal, 5);
        _statusLabel = new Label("Ready");
        _progressBar = new ProgressBar { Fraction = 0 };
        statusBox.PackStart(_statusLabel, true, true, 5);
        statusBox.PackEnd(_progressBar, false, false, 5);

        // Wire up events
        _simulator.OnStepCompleted += OnSimulationStep;
        _simulator.OnSimulationCompleted += OnSimulationCompleted;
        _simulator.OnMessage += OnSimulationMessage;

        ShowAll();
    }

    #endregion

    #region UI Creation

    private Box CreateToolsPanel()
    {
        var panel = new Box(Orientation.Vertical, 5) { WidthRequest = 200 };

        // Tool mode buttons
        var toolFrame = new Frame("Tools");
        var toolGrid = new Grid { RowSpacing = 2, ColumnSpacing = 2 };

        int row = 0;
        AddToolButton(toolGrid, "Select", GeomechanicsToolMode.Select, 0, row);
        AddToolButton(toolGrid, "Rectangle", GeomechanicsToolMode.DrawRectangle, 1, row++);
        AddToolButton(toolGrid, "Circle", GeomechanicsToolMode.DrawCircle, 0, row);
        AddToolButton(toolGrid, "Polygon", GeomechanicsToolMode.DrawPolygon, 1, row++);
        AddToolButton(toolGrid, "Joint", GeomechanicsToolMode.DrawJoint, 0, row);
        AddToolButton(toolGrid, "Joint Set", GeomechanicsToolMode.DrawJointSet, 1, row++);
        AddToolButton(toolGrid, "Foundation", GeomechanicsToolMode.DrawFoundation, 0, row);
        AddToolButton(toolGrid, "Ret. Wall", GeomechanicsToolMode.DrawRetainingWall, 1, row++);
        AddToolButton(toolGrid, "Tunnel", GeomechanicsToolMode.DrawTunnel, 0, row);
        AddToolButton(toolGrid, "Dam", GeomechanicsToolMode.DrawDam, 1, row++);
        AddToolButton(toolGrid, "Indenter", GeomechanicsToolMode.DrawIndenter, 0, row);
        AddToolButton(toolGrid, "Force", GeomechanicsToolMode.ApplyForce, 1, row++);
        AddToolButton(toolGrid, "Fix BC", GeomechanicsToolMode.FixBoundary, 0, row);
        AddToolButton(toolGrid, "Probe", GeomechanicsToolMode.ProbeResults, 1, row++);

        toolFrame.Add(toolGrid);
        panel.PackStart(toolFrame, false, false, 5);

        // Primitives list
        var primFrame = new Frame("Primitives");
        var primScroll = new ScrolledWindow { HeightRequest = 150 };
        _primitiveList = new TreeView(_primitiveStore);
        _primitiveList.AppendColumn("Name", new CellRendererText(), "text", 0);
        _primitiveList.AppendColumn("Type", new CellRendererText(), "text", 1);
        _primitiveList.Selection.Changed += OnPrimitiveSelectionChanged;
        primScroll.Add(_primitiveList);
        primFrame.Add(primScroll);
        panel.PackStart(primFrame, true, true, 5);

        // Quick BC buttons
        var bcFrame = new Frame("Boundary Conditions");
        var bcBox = new Box(Orientation.Vertical, 2);
        var fixBottomBtn = new Button("Fix Bottom");
        fixBottomBtn.Clicked += (s, e) => { _simulator.Mesh.FixBottom(); _canvas.QueueDraw(); };
        var fixLeftBtn = new Button("Fix Left");
        fixLeftBtn.Clicked += (s, e) => { _simulator.Mesh.FixLeft(); _canvas.QueueDraw(); };
        var fixRightBtn = new Button("Fix Right");
        fixRightBtn.Clicked += (s, e) => { _simulator.Mesh.FixRight(); _canvas.QueueDraw(); };
        bcBox.PackStart(fixBottomBtn, false, false, 2);
        bcBox.PackStart(fixLeftBtn, false, false, 2);
        bcBox.PackStart(fixRightBtn, false, false, 2);
        bcFrame.Add(bcBox);
        panel.PackStart(bcFrame, false, false, 5);

        // Simulation controls
        var simFrame = new Frame("Simulation");
        var simBox = new Box(Orientation.Vertical, 5);

        var genMeshBtn = new Button("Generate Mesh");
        genMeshBtn.Clicked += OnGenerateMesh;
        simBox.PackStart(genMeshBtn, false, false, 2);

        var runBtn = new Button("Run Simulation");
        runBtn.Clicked += OnRunSimulation;
        simBox.PackStart(runBtn, false, false, 2);

        var stopBtn = new Button("Stop");
        stopBtn.Clicked += OnStopSimulation;
        simBox.PackStart(stopBtn, false, false, 2);

        var resetBtn = new Button("Reset");
        resetBtn.Clicked += OnResetSimulation;
        simBox.PackStart(resetBtn, false, false, 2);

        simFrame.Add(simBox);
        panel.PackStart(simFrame, false, false, 5);

        return panel;
    }

    private void AddToolButton(Grid grid, string label, GeomechanicsToolMode mode, int col, int row)
    {
        var btn = new ToggleButton(label);
        btn.Clicked += (s, e) =>
        {
            _currentMode = mode;
            ClearDrawingState();
            UpdateToolButtonStates(grid, btn);
        };
        grid.Attach(btn, col, row, 1, 1);
    }

    private void UpdateToolButtonStates(Grid grid, ToggleButton activeBtn)
    {
        foreach (var child in grid.Children)
        {
            if (child is ToggleButton tb && tb != activeBtn)
            {
                tb.Active = false;
            }
        }
    }

    private Box CreatePropertiesPanel()
    {
        var panel = new Box(Orientation.Vertical, 5) { WidthRequest = 250 };

        // Visualization controls
        var visFrame = new Frame("Visualization");
        var visBox = new Box(Orientation.Vertical, 5);

        // Result field selector
        var fieldBox = new Box(Orientation.Horizontal, 5);
        fieldBox.PackStart(new Label("Field:"), false, false, 5);
        _resultFieldCombo = new ComboBoxText();
        foreach (var field in Enum.GetValues<ResultField2D>())
        {
            _resultFieldCombo.AppendText(ResultFieldInfo.GetDisplayName(field));
        }
        _resultFieldCombo.Active = (int)_displayField;
        _resultFieldCombo.Changed += OnResultFieldChanged;
        fieldBox.PackStart(_resultFieldCombo, true, true, 5);
        visBox.PackStart(fieldBox, false, false, 2);

        // Color map selector
        var cmBox = new Box(Orientation.Horizontal, 5);
        cmBox.PackStart(new Label("Colors:"), false, false, 5);
        _colorMapCombo = new ComboBoxText();
        foreach (var cm in Enum.GetValues<ColorMapType>())
        {
            _colorMapCombo.AppendText(cm.ToString());
        }
        _colorMapCombo.Active = (int)_colorMapType;
        _colorMapCombo.Changed += OnColorMapChanged;
        cmBox.PackStart(_colorMapCombo, true, true, 5);
        visBox.PackStart(cmBox, false, false, 2);

        // Deformation scale
        var defBox = new Box(Orientation.Horizontal, 5);
        defBox.PackStart(new Label("Def. Scale:"), false, false, 5);
        _deformationScale = new Scale(Orientation.Horizontal, new Adjustment(1, 0.1, 1000, 0.1, 10, 0));
        _deformationScale.ValueChanged += OnDeformationScaleChanged;
        defBox.PackStart(_deformationScale, true, true, 5);
        visBox.PackStart(defBox, false, false, 2);

        // Checkboxes
        var showMeshCb = new CheckButton("Show Mesh") { Active = _showMesh };
        showMeshCb.Toggled += (s, e) => { _showMesh = showMeshCb.Active; _canvas.QueueDraw(); };
        visBox.PackStart(showMeshCb, false, false, 2);

        var showDefCb = new CheckButton("Show Deformed") { Active = _showDeformed };
        showDefCb.Toggled += (s, e) => { _showDeformed = showDefCb.Active; _canvas.QueueDraw(); };
        visBox.PackStart(showDefCb, false, false, 2);

        var showBCCb = new CheckButton("Show BCs") { Active = _showBCs };
        showBCCb.Toggled += (s, e) => { _showBCs = showBCCb.Active; _canvas.QueueDraw(); };
        visBox.PackStart(showBCCb, false, false, 2);

        var showJointsCb = new CheckButton("Show Joints") { Active = _showJoints };
        showJointsCb.Toggled += (s, e) => { _showJoints = showJointsCb.Active; _canvas.QueueDraw(); };
        visBox.PackStart(showJointsCb, false, false, 2);

        visFrame.Add(visBox);
        panel.PackStart(visFrame, false, false, 5);

        // Materials list
        var matFrame = new Frame("Materials");
        var matScroll = new ScrolledWindow { HeightRequest = 150 };
        _materialList = new TreeView(_materialStore);
        _materialList.AppendColumn("Material", new CellRendererText(), "text", 0);
        _materialList.Selection.Changed += OnMaterialSelectionChanged;
        UpdateMaterialList();
        matScroll.Add(_materialList);
        matFrame.Add(matScroll);
        panel.PackStart(matFrame, true, true, 5);

        // Joint sets list
        var jsFrame = new Frame("Joint Sets");
        var jsScroll = new ScrolledWindow { HeightRequest = 100 };
        _jointSetList = new TreeView(_jointSetStore);
        _jointSetList.AppendColumn("Name", new CellRendererText(), "text", 0);
        _jointSetList.AppendColumn("Joints", new CellRendererText(), "text", 1);
        jsScroll.Add(_jointSetList);
        jsFrame.Add(jsScroll);
        panel.PackStart(jsFrame, true, true, 5);

        // Presets
        var presetFrame = new Frame("Presets");
        var presetBox = new Box(Orientation.Vertical, 2);

        var bearingBtn = new Button("Bearing Capacity Test");
        bearingBtn.Clicked += (s, e) => LoadPreset("bearing");
        presetBox.PackStart(bearingBtn, false, false, 2);

        var indentBtn = new Button("Indentation Test");
        indentBtn.Clicked += (s, e) => LoadPreset("indent");
        presetBox.PackStart(indentBtn, false, false, 2);

        var retWallBtn = new Button("Retaining Wall");
        retWallBtn.Clicked += (s, e) => LoadPreset("retwall");
        presetBox.PackStart(retWallBtn, false, false, 2);

        presetFrame.Add(presetBox);
        panel.PackStart(presetFrame, false, false, 5);

        return panel;
    }

    private void UpdateMaterialList()
    {
        _materialStore.Clear();
        foreach (var mat in _materials.Materials.Values)
        {
            _materialStore.AppendValues(mat.Name, mat.Id);
        }
    }

    private void UpdatePrimitiveList()
    {
        _primitiveStore.Clear();
        foreach (var prim in _primitives.Primitives)
        {
            _primitiveStore.AppendValues(prim.Name, prim.Type.ToString(), prim.Id);
        }
    }

    private void UpdateJointSetList()
    {
        _jointSetStore.Clear();
        foreach (var set in _jointSets.JointSets)
        {
            _jointSetStore.AppendValues(set.Name, set.Id, set.Joints.Count);
        }
    }

    #endregion

    #region Canvas Rendering

    private void OnCanvasDrawn(object sender, DrawnArgs args)
    {
        var cr = args.Cr;
        var allocation = _canvas.Allocation;

        // Clear background
        cr.SetSourceRGB(0.95, 0.95, 0.95);
        cr.Paint();

        // Set up transform
        cr.Translate(allocation.Width / 2 + _panOffset.X, allocation.Height / 2 - _panOffset.Y);
        cr.Scale(_zoom, -_zoom); // Flip Y for standard coordinates

        // Draw grid
        DrawGrid(cr);

        // Draw primitives
        DrawPrimitives(cr);

        // Draw mesh
        if (_showMesh)
        {
            DrawMesh(cr);
        }

        // Draw results
        DrawResults(cr);

        // Draw joint sets
        if (_showJoints)
        {
            DrawJointSets(cr);
        }

        // Draw boundary conditions
        if (_showBCs)
        {
            DrawBoundaryConditions(cr);
        }

        // Draw loads
        if (_showLoads)
        {
            DrawLoads(cr);
        }

        // Draw drawing preview
        DrawPreview(cr);

        // Draw selection
        DrawSelection(cr);

        // Draw color bar (in screen space)
        cr.IdentityMatrix();
        DrawColorBar(cr, allocation);
    }

    private void DrawGrid(Context cr)
    {
        cr.SetSourceRGBA(0.8, 0.8, 0.8, 0.5);
        cr.LineWidth = 0.5 / _zoom;

        double gridSize = 1;
        while (gridSize * _zoom < 20) gridSize *= 2;
        while (gridSize * _zoom > 100) gridSize /= 2;

        double extent = 1000;
        for (double x = -extent; x <= extent; x += gridSize)
        {
            cr.MoveTo(x, -extent);
            cr.LineTo(x, extent);
        }
        for (double y = -extent; y <= extent; y += gridSize)
        {
            cr.MoveTo(-extent, y);
            cr.LineTo(extent, y);
        }
        cr.Stroke();

        // Axes
        cr.SetSourceRGB(0, 0, 0);
        cr.LineWidth = 1 / _zoom;
        cr.MoveTo(-extent, 0);
        cr.LineTo(extent, 0);
        cr.MoveTo(0, -extent);
        cr.LineTo(0, extent);
        cr.Stroke();
    }

    private void DrawPrimitives(Context cr)
    {
        foreach (var prim in _primitives.Primitives.Where(p => p.IsVisible))
        {
            var vertices = prim.GetVertices();
            if (vertices.Count < 2) continue;

            // Fill
            cr.SetSourceRGBA(prim.Color.X, prim.Color.Y, prim.Color.Z, prim.Color.W * 0.7);
            cr.MoveTo(vertices[0].X, vertices[0].Y);
            for (int i = 1; i < vertices.Count; i++)
            {
                cr.LineTo(vertices[i].X, vertices[i].Y);
            }
            cr.ClosePath();
            cr.Fill();

            // Outline
            cr.SetSourceRGB(0.2, 0.2, 0.2);
            cr.LineWidth = 2 / _zoom;
            cr.MoveTo(vertices[0].X, vertices[0].Y);
            for (int i = 1; i < vertices.Count; i++)
            {
                cr.LineTo(vertices[i].X, vertices[i].Y);
            }
            cr.ClosePath();
            cr.Stroke();
        }
    }

    private void DrawMesh(Context cr)
    {
        if (_simulator.Mesh.Elements.Count == 0) return;

        var nodes = _simulator.Mesh.Nodes.ToArray();
        cr.SetSourceRGBA(0.4, 0.4, 0.4, 0.6);
        cr.LineWidth = 0.5 / _zoom;

        foreach (var element in _simulator.Mesh.Elements)
        {
            for (int i = 0; i < element.NodeIds.Count; i++)
            {
                int j = (i + 1) % element.NodeIds.Count;
                var n1 = nodes[element.NodeIds[i]];
                var n2 = nodes[element.NodeIds[j]];

                var p1 = _showDeformed
                    ? n1.InitialPosition + n1.GetDisplacement() * (float)_deformScale
                    : n1.InitialPosition;
                var p2 = _showDeformed
                    ? n2.InitialPosition + n2.GetDisplacement() * (float)_deformScale
                    : n2.InitialPosition;

                cr.MoveTo(p1.X, p1.Y);
                cr.LineTo(p2.X, p2.Y);
            }
        }
        cr.Stroke();
    }

    private void DrawResults(Context cr)
    {
        if (_simulator.Results == null) return;

        var values = GetFieldValues();
        if (values == null || values.Length == 0) return;

        // Update color map range
        double min = values.Min();
        double max = values.Max();
        _colorMap.MinValue = min;
        _colorMap.MaxValue = max;

        var nodes = _simulator.Mesh.Nodes.ToArray();

        foreach (var element in _simulator.Mesh.Elements)
        {
            if (element.Id >= values.Length) continue;

            var vertices = new List<Vector2>();
            foreach (int nodeId in element.NodeIds)
            {
                var pos = _showDeformed
                    ? nodes[nodeId].InitialPosition + nodes[nodeId].GetDisplacement() * (float)_deformScale
                    : nodes[nodeId].InitialPosition;
                vertices.Add(pos);
            }

            if (vertices.Count < 3) continue;

            var color = _colorMap.GetColorForValue(values[element.Id]);
            cr.SetSourceRGBA(color.X, color.Y, color.Z, color.W);

            cr.MoveTo(vertices[0].X, vertices[0].Y);
            for (int i = 1; i < vertices.Count; i++)
            {
                cr.LineTo(vertices[i].X, vertices[i].Y);
            }
            cr.ClosePath();
            cr.Fill();
        }
    }

    private double[] GetFieldValues()
    {
        if (_simulator.Results == null) return null;

        return _displayField switch
        {
            ResultField2D.DisplacementX => _simulator.Results.DisplacementX,
            ResultField2D.DisplacementY => _simulator.Results.DisplacementY,
            ResultField2D.DisplacementMagnitude => _simulator.Results.DisplacementMagnitude,
            ResultField2D.StressXX => _simulator.Results.StressXX,
            ResultField2D.StressYY => _simulator.Results.StressYY,
            ResultField2D.StressXY => _simulator.Results.StressXY,
            ResultField2D.Sigma1 => _simulator.Results.Sigma1,
            ResultField2D.Sigma2 => _simulator.Results.Sigma2,
            ResultField2D.VonMisesStress => _simulator.Results.VonMisesStress,
            ResultField2D.MaxShearStress => _simulator.Results.MaxShearStress,
            ResultField2D.StrainMagnitude => GetStrainMagnitude(),
            ResultField2D.PlasticStrain => _simulator.Results.PlasticStrain,
            ResultField2D.YieldIndex => _simulator.Results.YieldIndex,
            _ => null
        };
    }

    private double[] GetStrainMagnitude()
    {
        if (_simulator.Results?.StrainXX == null || _simulator.Results.StrainYY == null || _simulator.Results.StrainXY == null)
            return null;

        int count = _simulator.Results.StrainXX.Length;
        var magnitude = new double[count];
        for (int i = 0; i < count; i++)
        {
            double exx = _simulator.Results.StrainXX[i];
            double eyy = _simulator.Results.StrainYY[i];
            double exy = _simulator.Results.StrainXY[i];
            magnitude[i] = Math.Sqrt(exx * exx + eyy * eyy + 2.0 * exy * exy);
        }

        return magnitude;
    }

    private void DrawJointSets(Context cr)
    {
        foreach (var set in _jointSets.JointSets.Where(s => s.IsVisible))
        {
            cr.SetSourceRGBA(set.Color.X, set.Color.Y, set.Color.Z, set.Color.W);
            cr.LineWidth = 2 / _zoom;

            foreach (var joint in set.Joints)
            {
                if (joint.Points.Count >= 2)
                {
                    cr.MoveTo(joint.Points[0].X, joint.Points[0].Y);
                    for (int i = 1; i < joint.Points.Count; i++)
                    {
                        cr.LineTo(joint.Points[i].X, joint.Points[i].Y);
                    }
                }
                else
                {
                    cr.MoveTo(joint.StartPoint.X, joint.StartPoint.Y);
                    cr.LineTo(joint.EndPoint.X, joint.EndPoint.Y);
                }
            }
            cr.Stroke();
        }
    }

    private void DrawBoundaryConditions(Context cr)
    {
        foreach (var node in _simulator.Mesh.Nodes)
        {
            if (!node.FixedX && !node.FixedY) continue;

            float size = 10 / _zoom;
            var pos = node.InitialPosition;

            if (node.FixedX && node.FixedY)
            {
                // Fixed - filled triangle
                cr.SetSourceRGB(0.2, 0.6, 0.2);
                cr.MoveTo(pos.X - size, pos.Y);
                cr.LineTo(pos.X + size, pos.Y);
                cr.LineTo(pos.X, pos.Y - size * 1.5);
                cr.ClosePath();
                cr.Fill();
            }
            else
            {
                // Roller - circle
                cr.SetSourceRGB(0.6, 0.6, 0.2);
                cr.Arc(pos.X, pos.Y, size / 2, 0, 2 * Math.PI);
                cr.Stroke();
            }
        }
    }

    private void DrawLoads(Context cr)
    {
        double maxForce = _simulator.Mesh.Nodes.Max(n => Math.Sqrt(n.Fx * n.Fx + n.Fy * n.Fy));
        if (maxForce < 1e-10) return;

        cr.SetSourceRGB(0.8, 0.2, 0.2);
        cr.LineWidth = 2 / _zoom;

        foreach (var node in _simulator.Mesh.Nodes)
        {
            double fx = node.Fx;
            double fy = node.Fy;
            double mag = Math.Sqrt(fx * fx + fy * fy);
            if (mag < maxForce * 0.01) continue;

            double arrowLength = 30 / _zoom * (mag / maxForce);
            var dir = new Vector2((float)(fx / mag), (float)(fy / mag));
            var end = node.InitialPosition + dir * (float)arrowLength;

            cr.MoveTo(node.InitialPosition.X, node.InitialPosition.Y);
            cr.LineTo(end.X, end.Y);
            cr.Stroke();

            // Arrow head
            var perpDir = new Vector2(-dir.Y, dir.X);
            double headSize = 5 / _zoom;
            cr.MoveTo(end.X, end.Y);
            cr.LineTo(end.X - dir.X * headSize + perpDir.X * headSize / 2,
                      end.Y - dir.Y * headSize + perpDir.Y * headSize / 2);
            cr.LineTo(end.X - dir.X * headSize - perpDir.X * headSize / 2,
                      end.Y - dir.Y * headSize - perpDir.Y * headSize / 2);
            cr.ClosePath();
            cr.Fill();
        }
    }

    private void DrawPreview(Context cr)
    {
        if (!_isDrawing && _tempPoints.Count == 0) return;

        cr.SetSourceRGBA(1, 1, 0, 0.8);
        cr.LineWidth = 2 / _zoom;

        for (int i = 0; i < _tempPoints.Count; i++)
        {
            cr.Arc(_tempPoints[i].X, _tempPoints[i].Y, 5 / _zoom, 0, 2 * Math.PI);
            cr.Fill();

            if (i > 0)
            {
                cr.MoveTo(_tempPoints[i - 1].X, _tempPoints[i - 1].Y);
                cr.LineTo(_tempPoints[i].X, _tempPoints[i].Y);
                cr.Stroke();
            }
        }
    }

    private void DrawSelection(Context cr)
    {
        cr.SetSourceRGB(0, 1, 1);
        cr.LineWidth = 3 / _zoom;

        if (_selectedPrimitive != null)
        {
            var vertices = _selectedPrimitive.GetVertices();
            if (vertices.Count > 0)
            {
                cr.MoveTo(vertices[0].X, vertices[0].Y);
                for (int i = 1; i < vertices.Count; i++)
                {
                    cr.LineTo(vertices[i].X, vertices[i].Y);
                }
                cr.ClosePath();
                cr.Stroke();
            }
        }

        if (_selectedElementId >= 0 && _selectedElementId < _simulator.Mesh.Elements.Count)
        {
            var element = _simulator.Mesh.Elements[_selectedElementId];
            var nodes = _simulator.Mesh.Nodes.ToArray();

            var first = nodes[element.NodeIds[0]].InitialPosition;
            cr.MoveTo(first.X, first.Y);
            for (int i = 1; i < element.NodeIds.Count; i++)
            {
                var p = nodes[element.NodeIds[i]].InitialPosition;
                cr.LineTo(p.X, p.Y);
            }
            cr.ClosePath();
            cr.Stroke();
        }
    }

    private void DrawColorBar(Context cr, Gdk.Rectangle allocation)
    {
        if (_simulator.Results == null) return;

        double barWidth = 20;
        double barHeight = 200;
        double barX = allocation.Width - barWidth - 30;
        double barY = 30;

        // Draw gradient
        for (int i = 0; i < barHeight; i++)
        {
            double t = 1.0 - i / barHeight;
            var color = _colorMap.GetColor(t);
            cr.SetSourceRGB(color.X, color.Y, color.Z);
            cr.Rectangle(barX, barY + i, barWidth, 1);
            cr.Fill();
        }

        // Draw border
        cr.SetSourceRGB(0, 0, 0);
        cr.LineWidth = 1;
        cr.Rectangle(barX, barY, barWidth, barHeight);
        cr.Stroke();

        // Draw labels
        cr.SetSourceRGB(0, 0, 0);
        cr.MoveTo(barX + barWidth + 5, barY + 12);
        cr.ShowText($"{_colorMap.MaxValue:G4}");
        cr.MoveTo(barX + barWidth + 5, barY + barHeight);
        cr.ShowText($"{_colorMap.MinValue:G4}");
    }

    #endregion

    #region Event Handlers

    private void OnCanvasButtonPress(object sender, ButtonPressEventArgs args)
    {
        var worldPos = ScreenToWorld(new Vector2((float)args.Event.X, (float)args.Event.Y));

        if (args.Event.Button == 1) // Left click
        {
            HandleLeftClick(worldPos);
        }
        else if (args.Event.Button == 2) // Middle click
        {
            _isPanning = true;
            _lastMousePos = new Vector2((float)args.Event.X, (float)args.Event.Y);
        }
        else if (args.Event.Button == 3) // Right click
        {
            HandleRightClick(worldPos);
        }
    }

    private void OnCanvasButtonRelease(object sender, ButtonReleaseEventArgs args)
    {
        if (args.Event.Button == 2)
        {
            _isPanning = false;
        }
    }

    private void OnCanvasMotion(object sender, MotionNotifyEventArgs args)
    {
        var screenPos = new Vector2((float)args.Event.X, (float)args.Event.Y);

        if (_isPanning)
        {
            var delta = screenPos - _lastMousePos;
            _panOffset += new Vector2(delta.X, -delta.Y);
            _lastMousePos = screenPos;
            _canvas.QueueDraw();
        }
    }

    private void OnCanvasScroll(object sender, ScrollEventArgs args)
    {
        float zoomFactor = args.Event.Direction == Gdk.ScrollDirection.Up ? 1.1f : 0.9f;
        _zoom *= zoomFactor;
        _zoom = Math.Clamp(_zoom, 0.01f, 100f);
        _canvas.QueueDraw();
    }

    private void HandleLeftClick(Vector2 worldPos)
    {
        switch (_currentMode)
        {
            case GeomechanicsToolMode.Select:
                SelectAt(worldPos);
                break;
            case GeomechanicsToolMode.DrawRectangle:
                PlaceRectangle(worldPos);
                break;
            case GeomechanicsToolMode.DrawCircle:
                PlaceCircle(worldPos);
                break;
            case GeomechanicsToolMode.DrawPolygon:
                _tempPoints.Add(worldPos);
                _isDrawing = true;
                break;
            case GeomechanicsToolMode.DrawJoint:
                _tempPoints.Add(worldPos);
                _isDrawing = true;
                break;
            case GeomechanicsToolMode.DrawFoundation:
                PlaceFoundation(worldPos);
                break;
            case GeomechanicsToolMode.FixBoundary:
                FixBoundaryAt(worldPos);
                break;
            case GeomechanicsToolMode.ProbeResults:
                ProbeAt(worldPos);
                break;
        }
        _canvas.QueueDraw();
    }

    private void HandleRightClick(Vector2 worldPos)
    {
        if (_currentMode == GeomechanicsToolMode.DrawPolygon && _tempPoints.Count >= 3)
        {
            FinishPolygon();
        }
        else if (_currentMode == GeomechanicsToolMode.DrawJoint && _tempPoints.Count >= 2)
        {
            FinishJoint();
        }
        else
        {
            ClearDrawingState();
        }
        _canvas.QueueDraw();
    }

    private void OnResultFieldChanged(object sender, EventArgs args)
    {
        _displayField = (ResultField2D)_resultFieldCombo.Active;
        _canvas.QueueDraw();
    }

    private void OnColorMapChanged(object sender, EventArgs args)
    {
        _colorMapType = (ColorMapType)_colorMapCombo.Active;
        _colorMap.Type = _colorMapType;
        _canvas.QueueDraw();
    }

    private void OnDeformationScaleChanged(object sender, EventArgs args)
    {
        _deformScale = _deformationScale.Value;
        _canvas.QueueDraw();
    }

    private void OnPrimitiveSelectionChanged(object sender, EventArgs args)
    {
        var selection = _primitiveList.Selection;
        if (selection.GetSelected(out var model, out var iter))
        {
            int id = (int)model.GetValue(iter, 2);
            _selectedPrimitive = _primitives.GetPrimitive(id);
            _canvas.QueueDraw();
        }
    }

    private void OnMaterialSelectionChanged(object sender, EventArgs args)
    {
        // Material selection changed
    }

    private void OnGenerateMesh(object sender, EventArgs args)
    {
        _simulator.Mesh.Clear();
        _primitives.GenerateAllMeshes(_simulator.Mesh);
        _primitives.ApplyAllBoundaryConditions(_simulator.Mesh);
        _jointSets.InsertIntoMesh(_simulator.Mesh);

        _statusLabel.Text = $"Mesh: {_simulator.Mesh.Nodes.Count} nodes, {_simulator.Mesh.Elements.Count} elements";
        _canvas.QueueDraw();
    }

    private async void OnRunSimulation(object sender, EventArgs args)
    {
        if (_isSimulating) return;

        _isSimulating = true;
        _simCts = new CancellationTokenSource();

        try
        {
            await _simulator.RunAsync(_simCts.Token);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _isSimulating = false;
        }
    }

    private void OnStopSimulation(object sender, EventArgs args)
    {
        _simCts?.Cancel();
        _isSimulating = false;
    }

    private void OnResetSimulation(object sender, EventArgs args)
    {
        _simulator.Mesh.Reset();
        _simulator.InitializeResults();
        _canvas.QueueDraw();
    }

    private void OnSimulationStep(SimulationState2D state)
    {
        GLib.Idle.Add(() =>
        {
            _progressBar.Fraction = state.TotalSteps > 0 ? (double)state.CurrentStep / state.TotalSteps : 0;
            _statusLabel.Text = $"Step {state.CurrentStep}/{state.TotalSteps} - Residual: {state.ResidualNorm:E3}";
            _canvas.QueueDraw();
            return false;
        });
    }

    private void OnSimulationCompleted(SimulationResults2D results)
    {
        GLib.Idle.Add(() =>
        {
            _isSimulating = false;
            _progressBar.Fraction = 1;
            _statusLabel.Text = "Simulation completed";
            _canvas.QueueDraw();
            return false;
        });
    }

    private void OnSimulationMessage(string message)
    {
        GLib.Idle.Add(() =>
        {
            _statusLabel.Text = message;
            return false;
        });
    }

    #endregion

    #region Helper Methods

    private Vector2 ScreenToWorld(Vector2 screenPos)
    {
        var allocation = _canvas.Allocation;
        float x = (screenPos.X - allocation.Width / 2 - _panOffset.X) / _zoom;
        float y = -(screenPos.Y - allocation.Height / 2 + _panOffset.Y) / _zoom;
        return new Vector2(x, y);
    }

    private void SelectAt(Vector2 pos)
    {
        var prim = _primitives.GetPrimitiveAt(pos);
        _selectedPrimitive = prim;
        _selectedElementId = -1;

        if (prim == null)
        {
            var nodes = _simulator.Mesh.Nodes.ToArray();
            for (int e = 0; e < _simulator.Mesh.Elements.Count; e++)
            {
                var element = _simulator.Mesh.Elements[e];
                var centroid = element.GetCentroid(nodes);
                if (Vector2.Distance(pos, centroid) < 1.0f)
                {
                    _selectedElementId = e;
                    break;
                }
            }
        }
    }

    private void PlaceRectangle(Vector2 pos)
    {
        var rect = new RectanglePrimitive
        {
            Position = pos,
            Width = 5,
            Height = 3,
            MaterialId = _materials.Materials.Values.First().Id,
            Name = $"Rectangle {_primitives.Primitives.Count + 1}"
        };
        _primitives.AddPrimitive(rect);
        UpdatePrimitiveList();
    }

    private void PlaceCircle(Vector2 pos)
    {
        var circle = new CirclePrimitive
        {
            Position = pos,
            Radius = 2,
            MaterialId = _materials.Materials.Values.First().Id,
            Name = $"Circle {_primitives.Primitives.Count + 1}"
        };
        _primitives.AddPrimitive(circle);
        UpdatePrimitiveList();
    }

    private void FinishPolygon()
    {
        if (_tempPoints.Count >= 3)
        {
            var poly = new PolygonPrimitive
            {
                LocalVertices = new List<Vector2>(_tempPoints),
                MaterialId = _materials.Materials.Values.First().Id,
                Name = $"Polygon {_primitives.Primitives.Count + 1}"
            };
            _primitives.AddPrimitive(poly);
            UpdatePrimitiveList();
        }
        ClearDrawingState();
    }

    private void FinishJoint()
    {
        if (_tempPoints.Count >= 2)
        {
            var set = new JointSet2D
            {
                Name = $"Manual Joint {_jointSets.JointSets.Count + 1}",
                MeanSpacing = 100
            };

            var joint = new Discontinuity2D
            {
                StartPoint = _tempPoints[0],
                EndPoint = _tempPoints[^1],
                Points = new List<Vector2>(_tempPoints)
            };
            set.Joints.Add(joint);
            _jointSets.AddJointSet(set);
            UpdateJointSetList();
        }
        ClearDrawingState();
    }

    private void PlaceFoundation(Vector2 pos)
    {
        var foundation = new FoundationPrimitive
        {
            Position = pos,
            Width = 3,
            Height = 0.5,
            MaterialId = _materials.Materials.Values.FirstOrDefault(m => m.Name == "Concrete")?.Id ?? 1,
            Name = $"Foundation {_primitives.Primitives.Count + 1}"
        };
        _primitives.AddPrimitive(foundation);
        UpdatePrimitiveList();
    }

    private void FixBoundaryAt(Vector2 pos)
    {
        foreach (var node in _simulator.Mesh.Nodes)
        {
            if (Vector2.Distance(pos, node.InitialPosition) < 1.0f)
            {
                node.FixedX = true;
                node.FixedY = true;
                break;
            }
        }
    }

    private void ProbeAt(Vector2 pos)
    {
        var nodes = _simulator.Mesh.Nodes.ToArray();
        for (int e = 0; e < _simulator.Mesh.Elements.Count; e++)
        {
            var element = _simulator.Mesh.Elements[e];
            var centroid = element.GetCentroid(nodes);
            if (Vector2.Distance(pos, centroid) < 1.0f)
            {
                _selectedElementId = e;
                if (_simulator.Results != null && e < _simulator.Results.StressXX.Length)
                {
                    var r = _simulator.Results;
                    _statusLabel.Text = $"E{e}: σ1={r.Sigma1[e]/1e6:F2} MPa, σ2={r.Sigma2[e]/1e6:F2} MPa, VM={r.VonMisesStress[e]/1e6:F2} MPa";
                }
                break;
            }
        }
    }

    private void ClearDrawingState()
    {
        _tempPoints.Clear();
        _isDrawing = false;
    }

    private void LoadPreset(string preset)
    {
        _primitives.Clear();

        List<GeometricPrimitive2D> prims = preset switch
        {
            "bearing" => PrimitiveManager2D.Presets.CreateBearingCapacityTest(),
            "indent" => PrimitiveManager2D.Presets.CreateIndentationTest(),
            "retwall" => PrimitiveManager2D.Presets.CreateRetainingWallAnalysis(),
            _ => new List<GeometricPrimitive2D>()
        };

        foreach (var p in prims)
        {
            _primitives.AddPrimitive(p);
        }

        UpdatePrimitiveList();
        _canvas.QueueDraw();
    }

    #endregion
}
