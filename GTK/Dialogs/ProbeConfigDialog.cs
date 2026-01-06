using System;
using System.Collections.Generic;
using System.Linq;
using Gtk;
using Cairo;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.GTK.Dialogs
{
    /// <summary>
    /// GTK dialog for configuring and visualizing simulation probes.
    /// Supports point, line, and plane probes with time-series graphs and 2D colormaps.
    /// </summary>
    public class ProbeConfigDialog : Dialog
    {
        private ProbeManager _probeManager;
        private SimulationProbe? _selectedProbe;

        // UI elements
        private TreeView _probeTreeView;
        private ListStore _probeListStore;
        private Notebook _mainNotebook;

        // Probe details
        private Entry _nameEntry;
        private CheckButton _activeCheck;
        private ComboBoxText _variableCombo;
        private ColorButton _colorButton;

        // Point probe controls
        private SpinButton _pointXSpin, _pointYSpin, _pointZSpin;

        // Line probe controls
        private SpinButton _lineStartXSpin, _lineStartYSpin, _lineStartZSpin;
        private SpinButton _lineEndXSpin, _lineEndYSpin, _lineEndZSpin;

        // Plane probe controls
        private SpinButton _planeCenterXSpin, _planeCenterYSpin, _planeCenterZSpin;
        private SpinButton _planeWidthSpin, _planeHeightSpin;
        private ComboBoxText _planeOrientationCombo;

        // Visualization
        private DrawingArea _chartArea;
        private DrawingArea _colormapArea;
        private ComboBoxText _chartProbeCombo;
        private Scale _timeRangeScale;
        private ComboBoxText _colormapCombo;

        // Chart settings
        private List<string> _chartProbeIds = new();
        private double _chartTimeRange = 100;

        public ProbeManager ProbeManagerResult => _probeManager;

        public ProbeConfigDialog(Window parent, ProbeManager? existingManager = null)
            : base("Probe Configuration", parent,
                  DialogFlags.Modal | DialogFlags.DestroyWithParent,
                  "Close", ResponseType.Close,
                  "Export Data", ResponseType.Apply)
        {
            _probeManager = existingManager ?? new ProbeManager();
            SetDefaultSize(1000, 700);
            BuildUI();
            RefreshProbeList();
        }

        private void BuildUI()
        {
            var mainBox = new HBox(false, 10) { BorderWidth = 10 };
            ContentArea.PackStart(mainBox, true, true, 0);

            // Left panel - Probe list
            var leftPanel = new VBox(false, 5);
            leftPanel.SetSizeRequest(250, -1);
            mainBox.PackStart(leftPanel, false, false, 0);

            leftPanel.PackStart(new Label("Probes") { Halign = Align.Start }, false, false, 0);

            // Probe list
            var scrolledList = new ScrolledWindow();
            scrolledList.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
            scrolledList.SetSizeRequest(-1, 200);

            _probeListStore = new ListStore(typeof(string), typeof(string), typeof(string), typeof(string));
            _probeTreeView = new TreeView(_probeListStore);
            _probeTreeView.AppendColumn("Type", new CellRendererText(), "text", 0);
            _probeTreeView.AppendColumn("Name", new CellRendererText(), "text", 1);
            _probeTreeView.AppendColumn("Points", new CellRendererText(), "text", 2);
            _probeTreeView.Selection.Changed += OnProbeSelectionChanged;

            scrolledList.Add(_probeTreeView);
            leftPanel.PackStart(scrolledList, true, true, 0);

            // Add probe buttons
            var addButtonBox = new HBox(true, 5);
            var addPointBtn = new Button("+ Point");
            addPointBtn.Clicked += (s, e) => AddNewProbe(0);
            var addLineBtn = new Button("+ Line");
            addLineBtn.Clicked += (s, e) => AddNewProbe(1);
            var addPlaneBtn = new Button("+ Plane");
            addPlaneBtn.Clicked += (s, e) => AddNewProbe(2);
            addButtonBox.PackStart(addPointBtn, true, true, 0);
            addButtonBox.PackStart(addLineBtn, true, true, 0);
            addButtonBox.PackStart(addPlaneBtn, true, true, 0);
            leftPanel.PackStart(addButtonBox, false, false, 0);

            var deleteBtn = new Button("Delete Selected");
            deleteBtn.Clicked += OnDeleteProbe;
            leftPanel.PackStart(deleteBtn, false, false, 0);

            // Probe details
            leftPanel.PackStart(new Separator(Orientation.Horizontal), false, false, 5);
            leftPanel.PackStart(new Label("Probe Details") { Halign = Align.Start }, false, false, 0);

            var detailsFrame = new Frame();
            var detailsBox = new VBox(false, 5) { BorderWidth = 5 };
            detailsFrame.Add(detailsBox);

            // Name
            var nameBox = new HBox(false, 5);
            nameBox.PackStart(new Label("Name:") { WidthRequest = 60 }, false, false, 0);
            _nameEntry = new Entry();
            _nameEntry.Changed += OnProbePropertyChanged;
            nameBox.PackStart(_nameEntry, true, true, 0);
            detailsBox.PackStart(nameBox, false, false, 0);

            // Active
            _activeCheck = new CheckButton("Active");
            _activeCheck.Toggled += OnProbePropertyChanged;
            detailsBox.PackStart(_activeCheck, false, false, 0);

            // Variable
            var varBox = new HBox(false, 5);
            varBox.PackStart(new Label("Variable:") { WidthRequest = 60 }, false, false, 0);
            _variableCombo = new ComboBoxText();
            foreach (var name in Enum.GetNames(typeof(ProbeVariable)))
                _variableCombo.AppendText(name);
            _variableCombo.Active = 0;
            _variableCombo.Changed += OnProbePropertyChanged;
            varBox.PackStart(_variableCombo, true, true, 0);
            detailsBox.PackStart(varBox, false, false, 0);

            // Color
            var colorBox = new HBox(false, 5);
            colorBox.PackStart(new Label("Color:") { WidthRequest = 60 }, false, false, 0);
            _colorButton = new ColorButton();
            _colorButton.ColorSet += OnProbePropertyChanged;
            colorBox.PackStart(_colorButton, false, false, 0);
            detailsBox.PackStart(colorBox, false, false, 0);

            leftPanel.PackStart(detailsFrame, false, false, 0);

            // Position controls (dynamically shown based on probe type)
            leftPanel.PackStart(CreatePositionControls(), false, false, 0);

            // Right panel - Visualization notebook
            _mainNotebook = new Notebook();
            mainBox.PackStart(_mainNotebook, true, true, 0);

            // Tab 1: Mesh View with Probes
            _mainNotebook.AppendPage(CreateMeshViewTab(), new Label("Mesh View"));

            // Tab 2: Time Charts
            _mainNotebook.AppendPage(CreateChartTab(), new Label("Time Charts"));

            // Tab 3: 2D Colormap
            _mainNotebook.AppendPage(CreateColormapTab(), new Label("2D Cross-Section"));

            ContentArea.ShowAll();
        }

        private Widget CreatePositionControls()
        {
            var stack = new Stack();
            stack.TransitionType = StackTransitionType.SlideLeftRight;

            // Point probe position
            var pointBox = new VBox(false, 5) { BorderWidth = 5 };
            var pointFrame = new Frame("Position");
            var pointGrid = new Grid { RowSpacing = 5, ColumnSpacing = 5, MarginStart = 5, MarginEnd = 5, MarginTop = 5, MarginBottom = 5 };

            _pointXSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };
            _pointYSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };
            _pointZSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };

            _pointXSpin.ValueChanged += OnProbePropertyChanged;
            _pointYSpin.ValueChanged += OnProbePropertyChanged;
            _pointZSpin.ValueChanged += OnProbePropertyChanged;

            pointGrid.Attach(new Label("X:"), 0, 0, 1, 1);
            pointGrid.Attach(_pointXSpin, 1, 0, 1, 1);
            pointGrid.Attach(new Label("Y:"), 0, 1, 1, 1);
            pointGrid.Attach(_pointYSpin, 1, 1, 1, 1);
            pointGrid.Attach(new Label("Z:"), 0, 2, 1, 1);
            pointGrid.Attach(_pointZSpin, 1, 2, 1, 1);

            pointFrame.Add(pointGrid);
            pointBox.PackStart(pointFrame, false, false, 0);
            stack.AddNamed(pointBox, "point");

            // Line probe position
            var lineBox = new VBox(false, 5) { BorderWidth = 5 };
            var lineFrame = new Frame("Start/End Points");
            var lineGrid = new Grid { RowSpacing = 5, ColumnSpacing = 5, MarginStart = 5, MarginEnd = 5, MarginTop = 5, MarginBottom = 5 };

            _lineStartXSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };
            _lineStartYSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };
            _lineStartZSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };
            _lineEndXSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };
            _lineEndYSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };
            _lineEndZSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };

            _lineStartXSpin.ValueChanged += OnProbePropertyChanged;
            _lineStartYSpin.ValueChanged += OnProbePropertyChanged;
            _lineStartZSpin.ValueChanged += OnProbePropertyChanged;
            _lineEndXSpin.ValueChanged += OnProbePropertyChanged;
            _lineEndYSpin.ValueChanged += OnProbePropertyChanged;
            _lineEndZSpin.ValueChanged += OnProbePropertyChanged;

            lineGrid.Attach(new Label("Start X:"), 0, 0, 1, 1);
            lineGrid.Attach(_lineStartXSpin, 1, 0, 1, 1);
            lineGrid.Attach(new Label("Start Y:"), 0, 1, 1, 1);
            lineGrid.Attach(_lineStartYSpin, 1, 1, 1, 1);
            lineGrid.Attach(new Label("Start Z:"), 0, 2, 1, 1);
            lineGrid.Attach(_lineStartZSpin, 1, 2, 1, 1);
            lineGrid.Attach(new Label("End X:"), 2, 0, 1, 1);
            lineGrid.Attach(_lineEndXSpin, 3, 0, 1, 1);
            lineGrid.Attach(new Label("End Y:"), 2, 1, 1, 1);
            lineGrid.Attach(_lineEndYSpin, 3, 1, 1, 1);
            lineGrid.Attach(new Label("End Z:"), 2, 2, 1, 1);
            lineGrid.Attach(_lineEndZSpin, 3, 2, 1, 1);

            lineFrame.Add(lineGrid);
            lineBox.PackStart(lineFrame, false, false, 0);
            stack.AddNamed(lineBox, "line");

            // Plane probe position
            var planeBox = new VBox(false, 5) { BorderWidth = 5 };
            var planeFrame = new Frame("Plane Definition");
            var planeGrid = new Grid { RowSpacing = 5, ColumnSpacing = 5, MarginStart = 5, MarginEnd = 5, MarginTop = 5, MarginBottom = 5 };

            _planeCenterXSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };
            _planeCenterYSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };
            _planeCenterZSpin = new SpinButton(-1000, 1000, 0.1) { Digits = 2 };
            _planeWidthSpin = new SpinButton(0.1, 1000, 0.1) { Digits = 2, Value = 1 };
            _planeHeightSpin = new SpinButton(0.1, 1000, 0.1) { Digits = 2, Value = 1 };
            _planeOrientationCombo = new ComboBoxText();
            _planeOrientationCombo.AppendText("XY");
            _planeOrientationCombo.AppendText("XZ");
            _planeOrientationCombo.AppendText("YZ");
            _planeOrientationCombo.Active = 0;

            _planeCenterXSpin.ValueChanged += OnProbePropertyChanged;
            _planeCenterYSpin.ValueChanged += OnProbePropertyChanged;
            _planeCenterZSpin.ValueChanged += OnProbePropertyChanged;
            _planeWidthSpin.ValueChanged += OnProbePropertyChanged;
            _planeHeightSpin.ValueChanged += OnProbePropertyChanged;
            _planeOrientationCombo.Changed += OnProbePropertyChanged;

            planeGrid.Attach(new Label("Center X:"), 0, 0, 1, 1);
            planeGrid.Attach(_planeCenterXSpin, 1, 0, 1, 1);
            planeGrid.Attach(new Label("Center Y:"), 0, 1, 1, 1);
            planeGrid.Attach(_planeCenterYSpin, 1, 1, 1, 1);
            planeGrid.Attach(new Label("Center Z:"), 0, 2, 1, 1);
            planeGrid.Attach(_planeCenterZSpin, 1, 2, 1, 1);
            planeGrid.Attach(new Label("Width:"), 2, 0, 1, 1);
            planeGrid.Attach(_planeWidthSpin, 3, 0, 1, 1);
            planeGrid.Attach(new Label("Height:"), 2, 1, 1, 1);
            planeGrid.Attach(_planeHeightSpin, 3, 1, 1, 1);
            planeGrid.Attach(new Label("Orientation:"), 2, 2, 1, 1);
            planeGrid.Attach(_planeOrientationCombo, 3, 2, 1, 1);

            planeFrame.Add(planeGrid);
            planeBox.PackStart(planeFrame, false, false, 0);
            stack.AddNamed(planeBox, "plane");

            // Default state when no probe is selected
            stack.AddNamed(new Label("Select a probe to edit"), "empty");
            stack.VisibleChildName = "empty";

            return stack;
        }

        private Widget CreateMeshViewTab()
        {
            var vbox = new VBox(false, 5) { BorderWidth = 5 };

            var drawingArea = new DrawingArea();
            drawingArea.SetSizeRequest(500, 400);
            drawingArea.Drawn += OnMeshViewDrawn;

            // Enable mouse events
            drawingArea.AddEvents((int)(Gdk.EventMask.ButtonPressMask | Gdk.EventMask.ButtonReleaseMask | Gdk.EventMask.PointerMotionMask));
            drawingArea.ButtonPressEvent += OnMeshViewButtonPress;

            vbox.PackStart(drawingArea, true, true, 0);

            var infoLabel = new Label("Click to select probe. Shift+click to add point probe.");
            vbox.PackStart(infoLabel, false, false, 0);

            return vbox;
        }

        private Widget CreateChartTab()
        {
            var vbox = new VBox(false, 5) { BorderWidth = 5 };

            // Chart controls
            var controlBox = new HBox(false, 10);

            controlBox.PackStart(new Label("Add Probe to Chart:"), false, false, 0);
            _chartProbeCombo = new ComboBoxText();
            _chartProbeCombo.Changed += OnChartProbeComboChanged;
            controlBox.PackStart(_chartProbeCombo, false, false, 0);

            var addToChartBtn = new Button("Add");
            addToChartBtn.Clicked += OnAddToChart;
            controlBox.PackStart(addToChartBtn, false, false, 0);

            var clearChartBtn = new Button("Clear Chart");
            clearChartBtn.Clicked += (s, e) => { _chartProbeIds.Clear(); _chartArea?.QueueDraw(); };
            controlBox.PackStart(clearChartBtn, false, false, 0);

            controlBox.PackStart(new Label("Time Range:"), false, false, 0);
            _timeRangeScale = new Scale(Orientation.Horizontal, 10, 1000, 10);
            _timeRangeScale.Value = 100;
            _timeRangeScale.ValueChanged += (s, e) => { _chartTimeRange = _timeRangeScale.Value; _chartArea?.QueueDraw(); };
            _timeRangeScale.SetSizeRequest(150, -1);
            controlBox.PackStart(_timeRangeScale, false, false, 0);

            vbox.PackStart(controlBox, false, false, 0);

            // Chart area
            _chartArea = new DrawingArea();
            _chartArea.SetSizeRequest(500, 400);
            _chartArea.Drawn += OnChartDrawn;
            vbox.PackStart(_chartArea, true, true, 0);

            // Export button
            var exportBtn = new Button("Export Chart as PNG");
            exportBtn.Clicked += OnExportChartPNG;
            vbox.PackStart(exportBtn, false, false, 0);

            return vbox;
        }

        private Widget CreateColormapTab()
        {
            var vbox = new VBox(false, 5) { BorderWidth = 5 };

            // Colormap controls
            var controlBox = new HBox(false, 10);

            controlBox.PackStart(new Label("Colormap:"), false, false, 0);
            _colormapCombo = new ComboBoxText();
            _colormapCombo.AppendText("Jet");
            _colormapCombo.AppendText("Viridis");
            _colormapCombo.AppendText("Inferno");
            _colormapCombo.AppendText("Grayscale");
            _colormapCombo.Active = 0;
            _colormapCombo.Changed += (s, e) => _colormapArea?.QueueDraw();
            controlBox.PackStart(_colormapCombo, false, false, 0);

            vbox.PackStart(controlBox, false, false, 0);

            // Colormap area
            _colormapArea = new DrawingArea();
            _colormapArea.SetSizeRequest(500, 400);
            _colormapArea.Drawn += OnColormapDrawn;
            vbox.PackStart(_colormapArea, true, true, 0);

            // Export button
            var exportBtn = new Button("Export Colormap as PNG");
            exportBtn.Clicked += OnExportColormapPNG;
            vbox.PackStart(exportBtn, false, false, 0);

            return vbox;
        }

        private void RefreshProbeList()
        {
            _probeListStore.Clear();
            _chartProbeCombo?.Clear();

            foreach (var probe in _probeManager.PointProbes)
            {
                _probeListStore.AppendValues("Point", probe.Name, probe.History.Count.ToString(), probe.Id);
                _chartProbeCombo?.AppendText(probe.Name);
            }
            foreach (var probe in _probeManager.LineProbes)
            {
                _probeListStore.AppendValues("Line", probe.Name, probe.History.Count.ToString(), probe.Id);
                _chartProbeCombo?.AppendText(probe.Name);
            }
            foreach (var probe in _probeManager.PlaneProbes)
            {
                _probeListStore.AppendValues("Plane", probe.Name, probe.History.Count.ToString(), probe.Id);
                _chartProbeCombo?.AppendText(probe.Name);
            }
        }

        private void OnProbeSelectionChanged(object? sender, EventArgs e)
        {
            if (_probeTreeView.Selection.GetSelected(out var model, out var iter))
            {
                var id = (string)model.GetValue(iter, 3);
                _selectedProbe = _probeManager.GetProbe(id);
                UpdateProbeDetails();
            }
        }

        private void UpdateProbeDetails()
        {
            if (_selectedProbe == null)
            {
                _nameEntry.Text = "";
                return;
            }

            // Prevent recursive updates
            _nameEntry.Changed -= OnProbePropertyChanged;
            _activeCheck.Toggled -= OnProbePropertyChanged;
            _variableCombo.Changed -= OnProbePropertyChanged;

            _nameEntry.Text = _selectedProbe.Name;
            _activeCheck.Active = _selectedProbe.IsActive;
            _variableCombo.Active = (int)_selectedProbe.Variable;

            var colorVec = ColorFromUint(_selectedProbe.Color);
            _colorButton.Rgba = colorVec;

            // Show appropriate position controls
            var stack = GetPositionStack();
            if (stack != null)
            {
                if (_selectedProbe is PointProbe pp)
                {
                    stack.VisibleChildName = "point";
                    _pointXSpin.Value = pp.X;
                    _pointYSpin.Value = pp.Y;
                    _pointZSpin.Value = pp.Z;
                }
                else if (_selectedProbe is LineProbe lp)
                {
                    stack.VisibleChildName = "line";
                    _lineStartXSpin.Value = lp.StartX;
                    _lineStartYSpin.Value = lp.StartY;
                    _lineStartZSpin.Value = lp.StartZ;
                    _lineEndXSpin.Value = lp.EndX;
                    _lineEndYSpin.Value = lp.EndY;
                    _lineEndZSpin.Value = lp.EndZ;
                }
                else if (_selectedProbe is PlaneProbe plp)
                {
                    stack.VisibleChildName = "plane";
                    _planeCenterXSpin.Value = plp.CenterX;
                    _planeCenterYSpin.Value = plp.CenterY;
                    _planeCenterZSpin.Value = plp.CenterZ;
                    _planeWidthSpin.Value = plp.Width;
                    _planeHeightSpin.Value = plp.Height;
                    _planeOrientationCombo.Active = (int)plp.Orientation;
                }
            }

            _nameEntry.Changed += OnProbePropertyChanged;
            _activeCheck.Toggled += OnProbePropertyChanged;
            _variableCombo.Changed += OnProbePropertyChanged;
        }

        private Stack? GetPositionStack()
        {
            // Find the stack widget in the left panel
            foreach (var child in ContentArea.Children)
            {
                if (child is HBox hbox)
                {
                    foreach (var c in hbox.Children)
                    {
                        if (c is VBox vbox)
                        {
                            foreach (var v in vbox.Children)
                            {
                                if (v is Stack stack)
                                    return stack;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private void OnProbePropertyChanged(object? sender, EventArgs e)
        {
            if (_selectedProbe == null) return;

            _selectedProbe.Name = _nameEntry.Text;
            _selectedProbe.IsActive = _activeCheck.Active;
            _selectedProbe.Variable = (ProbeVariable)_variableCombo.Active;
            _selectedProbe.Color = UintFromColor(_colorButton.Rgba);

            if (_selectedProbe is PointProbe pp)
            {
                pp.X = _pointXSpin.Value;
                pp.Y = _pointYSpin.Value;
                pp.Z = _pointZSpin.Value;
            }
            else if (_selectedProbe is LineProbe lp)
            {
                lp.StartX = _lineStartXSpin.Value;
                lp.StartY = _lineStartYSpin.Value;
                lp.StartZ = _lineStartZSpin.Value;
                lp.EndX = _lineEndXSpin.Value;
                lp.EndY = _lineEndYSpin.Value;
                lp.EndZ = _lineEndZSpin.Value;
            }
            else if (_selectedProbe is PlaneProbe plp)
            {
                plp.CenterX = _planeCenterXSpin.Value;
                plp.CenterY = _planeCenterYSpin.Value;
                plp.CenterZ = _planeCenterZSpin.Value;
                plp.Width = _planeWidthSpin.Value;
                plp.Height = _planeHeightSpin.Value;
                plp.Orientation = (ProbePlaneOrientation)_planeOrientationCombo.Active;
            }

            RefreshProbeList();
        }

        private void AddNewProbe(int type)
        {
            SimulationProbe probe;
            if (type == 0)
            {
                probe = _probeManager.AddPointProbe(0, 0, 0, $"Point {_probeManager.PointProbes.Count + 1}");
            }
            else if (type == 1)
            {
                probe = _probeManager.AddLineProbe(-1, 0, 0, 1, 0, 0, $"Line {_probeManager.LineProbes.Count + 1}");
            }
            else
            {
                probe = _probeManager.AddPlaneProbe(0, 0, 0, 2, 2, ProbePlaneOrientation.XY, $"Plane {_probeManager.PlaneProbes.Count + 1}");
            }

            _selectedProbe = probe;
            RefreshProbeList();
            UpdateProbeDetails();
        }

        private void OnDeleteProbe(object? sender, EventArgs e)
        {
            if (_selectedProbe != null)
            {
                _probeManager.RemoveProbe(_selectedProbe.Id);
                _chartProbeIds.Remove(_selectedProbe.Id);
                _selectedProbe = null;
                RefreshProbeList();
            }
        }

        private void OnMeshViewDrawn(object o, DrawnArgs args)
        {
            var cr = args.Cr;
            var area = (DrawingArea)o;
            var width = area.AllocatedWidth;
            var height = area.AllocatedHeight;

            // Background
            cr.SetSourceRGB(0.1, 0.1, 0.12);
            cr.Rectangle(0, 0, width, height);
            cr.Fill();

            // Mesh boundary (simplified)
            double scale = Math.Min(width, height) * 0.4;
            double cx = width / 2.0;
            double cy = height / 2.0;

            cr.SetSourceRGB(0.5, 0.5, 0.5);
            cr.Rectangle(cx - scale, cy - scale, scale * 2, scale * 2);
            cr.Stroke();

            // Draw probes
            foreach (var probe in _probeManager.AllProbes)
            {
                bool isSelected = probe == _selectedProbe;
                var color = ColorFromUint(probe.Color);

                if (probe is PointProbe pp)
                {
                    double x = cx + pp.X * scale / 5;
                    double y = cy - pp.Y * scale / 5;

                    cr.SetSourceRGBA(color.Red, color.Green, color.Blue, probe.IsActive ? 1 : 0.5);
                    cr.Arc(x, y, isSelected ? 8 : 5, 0, 2 * Math.PI);
                    cr.Fill();

                    if (isSelected)
                    {
                        cr.SetSourceRGB(1, 1, 1);
                        cr.Arc(x, y, 10, 0, 2 * Math.PI);
                        cr.Stroke();
                    }
                }
                else if (probe is LineProbe lp)
                {
                    double x1 = cx + lp.StartX * scale / 5;
                    double y1 = cy - lp.StartY * scale / 5;
                    double x2 = cx + lp.EndX * scale / 5;
                    double y2 = cy - lp.EndY * scale / 5;

                    cr.SetSourceRGBA(color.Red, color.Green, color.Blue, probe.IsActive ? 1 : 0.5);
                    cr.LineWidth = isSelected ? 4 : 2;
                    cr.MoveTo(x1, y1);
                    cr.LineTo(x2, y2);
                    cr.Stroke();

                    cr.Arc(x1, y1, 4, 0, 2 * Math.PI);
                    cr.Fill();
                    cr.Arc(x2, y2, 4, 0, 2 * Math.PI);
                    cr.Fill();
                }
                else if (probe is PlaneProbe plp)
                {
                    double pcx = cx + plp.CenterX * scale / 5;
                    double pcy = cy - plp.CenterY * scale / 5;
                    double pw = plp.Width * scale / 5;
                    double ph = plp.Height * scale / 5;

                    cr.SetSourceRGBA(color.Red, color.Green, color.Blue, 0.3);
                    cr.Rectangle(pcx - pw / 2, pcy - ph / 2, pw, ph);
                    cr.Fill();

                    cr.SetSourceRGBA(color.Red, color.Green, color.Blue, probe.IsActive ? 1 : 0.5);
                    cr.LineWidth = isSelected ? 3 : 1;
                    cr.Rectangle(pcx - pw / 2, pcy - ph / 2, pw, ph);
                    cr.Stroke();
                }
            }
        }

        private void OnMeshViewButtonPress(object o, ButtonPressEventArgs args)
        {
            var area = (DrawingArea)o;
            var width = area.AllocatedWidth;
            var height = area.AllocatedHeight;
            double scale = Math.Min(width, height) * 0.4;
            double cx = width / 2.0;
            double cy = height / 2.0;

            double worldX = (args.Event.X - cx) * 5 / scale;
            double worldY = -(args.Event.Y - cy) * 5 / scale;

            if ((args.Event.State & Gdk.ModifierType.ShiftMask) != 0)
            {
                // Add new point probe at clicked location
                var probe = _probeManager.AddPointProbe(worldX, worldY, 0, $"Point {_probeManager.PointProbes.Count + 1}");
                _selectedProbe = probe;
                RefreshProbeList();
                UpdateProbeDetails();
            }
            else
            {
                // Select probe at clicked location
                SelectProbeAt(args.Event.X, args.Event.Y, cx, cy, scale);
            }

            area.QueueDraw();
        }

        private void SelectProbeAt(double mx, double my, double cx, double cy, double scale)
        {
            double minDist = double.MaxValue;
            SimulationProbe? closest = null;

            foreach (var probe in _probeManager.AllProbes)
            {
                double dist = double.MaxValue;

                if (probe is PointProbe pp)
                {
                    double x = cx + pp.X * scale / 5;
                    double y = cy - pp.Y * scale / 5;
                    dist = Math.Sqrt(Math.Pow(mx - x, 2) + Math.Pow(my - y, 2));
                }
                else if (probe is LineProbe lp)
                {
                    double x1 = cx + lp.StartX * scale / 5;
                    double y1 = cy - lp.StartY * scale / 5;
                    double x2 = cx + lp.EndX * scale / 5;
                    double y2 = cy - lp.EndY * scale / 5;
                    dist = DistanceToLine(mx, my, x1, y1, x2, y2);
                }
                else if (probe is PlaneProbe plp)
                {
                    double pcx = cx + plp.CenterX * scale / 5;
                    double pcy = cy - plp.CenterY * scale / 5;
                    double pw = plp.Width * scale / 5;
                    double ph = plp.Height * scale / 5;

                    if (mx >= pcx - pw / 2 && mx <= pcx + pw / 2 &&
                        my >= pcy - ph / 2 && my <= pcy + ph / 2)
                    {
                        dist = 0;
                    }
                }

                if (dist < minDist && dist < 20)
                {
                    minDist = dist;
                    closest = probe;
                }
            }

            _selectedProbe = closest;
            UpdateProbeDetails();
        }

        private double DistanceToLine(double px, double py, double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) return Math.Sqrt(Math.Pow(px - x1, 2) + Math.Pow(py - y1, 2));

            double t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / (len * len), 0, 1);
            double projX = x1 + t * dx;
            double projY = y1 + t * dy;
            return Math.Sqrt(Math.Pow(px - projX, 2) + Math.Pow(py - projY, 2));
        }

        private void OnChartProbeComboChanged(object? sender, EventArgs e)
        {
            // Nothing needed here, handled by Add button
        }

        private void OnAddToChart(object? sender, EventArgs e)
        {
            if (_chartProbeCombo.Active < 0) return;

            var probes = _probeManager.AllProbes.ToList();
            if (_chartProbeCombo.Active < probes.Count)
            {
                var probe = probes[_chartProbeCombo.Active];
                if (!_chartProbeIds.Contains(probe.Id))
                {
                    _chartProbeIds.Add(probe.Id);
                    _chartArea?.QueueDraw();
                }
            }
        }

        private void OnChartDrawn(object o, DrawnArgs args)
        {
            var cr = args.Cr;
            var area = (DrawingArea)o;
            var width = area.AllocatedWidth;
            var height = area.AllocatedHeight;

            // Background
            cr.SetSourceRGB(0.1, 0.1, 0.12);
            cr.Rectangle(0, 0, width, height);
            cr.Fill();

            // Margins
            double marginLeft = 60;
            double marginRight = 20;
            double marginTop = 20;
            double marginBottom = 40;

            double plotX = marginLeft;
            double plotY = marginTop;
            double plotW = width - marginLeft - marginRight;
            double plotH = height - marginTop - marginBottom;

            // Plot background
            cr.SetSourceRGB(0.15, 0.15, 0.15);
            cr.Rectangle(plotX, plotY, plotW, plotH);
            cr.Fill();

            // Grid
            cr.SetSourceRGB(0.25, 0.25, 0.25);
            cr.LineWidth = 1;
            for (int i = 0; i <= 10; i++)
            {
                double x = plotX + plotW * i / 10;
                double y = plotY + plotH * i / 10;
                cr.MoveTo(x, plotY);
                cr.LineTo(x, plotY + plotH);
                cr.Stroke();
                cr.MoveTo(plotX, y);
                cr.LineTo(plotX + plotW, y);
                cr.Stroke();
            }

            // Determine Y range
            double yMin = double.MaxValue, yMax = double.MinValue;
            double maxTime = 0;

            foreach (var id in _chartProbeIds)
            {
                var probe = _probeManager.GetProbe(id);
                if (probe?.History.Count > 0)
                {
                    foreach (var pt in probe.History)
                    {
                        yMin = Math.Min(yMin, pt.Value);
                        yMax = Math.Max(yMax, pt.Value);
                        maxTime = Math.Max(maxTime, pt.Time);
                    }
                }
            }

            if (yMin >= yMax) { yMin = 0; yMax = 100; }
            double minTime = Math.Max(0, maxTime - _chartTimeRange);

            // Draw data
            foreach (var id in _chartProbeIds)
            {
                var probe = _probeManager.GetProbe(id);
                if (probe?.History.Count > 1)
                {
                    var color = ColorFromUint(probe.Color);
                    cr.SetSourceRGBA(color.Red, color.Green, color.Blue, 1);
                    cr.LineWidth = 2;

                    bool first = true;
                    foreach (var pt in probe.History)
                    {
                        if (pt.Time < minTime) continue;

                        double x = plotX + (pt.Time - minTime) / _chartTimeRange * plotW;
                        double y = plotY + plotH - (pt.Value - yMin) / (yMax - yMin) * plotH;
                        y = Math.Clamp(y, plotY, plotY + plotH);

                        if (first)
                        {
                            cr.MoveTo(x, y);
                            first = false;
                        }
                        else
                        {
                            cr.LineTo(x, y);
                        }
                    }
                    cr.Stroke();
                }
            }

            // Axes labels
            cr.SetSourceRGB(1, 1, 1);
            cr.SelectFontFace("Sans", Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
            cr.SetFontSize(12);

            for (int i = 0; i <= 5; i++)
            {
                double val = yMin + (yMax - yMin) * (5 - i) / 5;
                double y = plotY + plotH * i / 5;
                cr.MoveTo(5, y + 4);
                cr.ShowText($"{val:G4}");
            }

            for (int i = 0; i <= 5; i++)
            {
                double t = _chartTimeRange * i / 5;
                double x = plotX + plotW * i / 5;
                cr.MoveTo(x - 10, height - 10);
                cr.ShowText($"{t:F0}");
            }

            // Legend
            int legendY = 30;
            foreach (var id in _chartProbeIds)
            {
                var probe = _probeManager.GetProbe(id);
                if (probe != null)
                {
                    var color = ColorFromUint(probe.Color);
                    cr.SetSourceRGBA(color.Red, color.Green, color.Blue, 1);
                    cr.Rectangle(width - 140, legendY, 15, 10);
                    cr.Fill();

                    cr.SetSourceRGB(1, 1, 1);
                    cr.MoveTo(width - 120, legendY + 10);
                    cr.ShowText(probe.Name);

                    legendY += 20;
                }
            }
        }

        private void OnColormapDrawn(object o, DrawnArgs args)
        {
            var cr = args.Cr;
            var area = (DrawingArea)o;
            var width = area.AllocatedWidth;
            var height = area.AllocatedHeight;

            // Background
            cr.SetSourceRGB(0.1, 0.1, 0.12);
            cr.Rectangle(0, 0, width, height);
            cr.Fill();

            // Find plane probe with data
            var planeProbe = _selectedProbe as PlaneProbe ?? _probeManager.PlaneProbes.FirstOrDefault();
            if (planeProbe == null || planeProbe.FieldHistory.Count == 0)
            {
                cr.SetSourceRGB(0.5, 0.5, 0.5);
                cr.MoveTo(width / 2 - 80, height / 2);
                cr.ShowText("No plane probe data available");
                return;
            }

            var fieldData = planeProbe.FieldHistory[^1];
            double margin = 40;
            double mapW = width - margin * 2 - 50;
            double mapH = height - margin * 2;

            double cellW = mapW / fieldData.ResolutionX;
            double cellH = mapH / fieldData.ResolutionY;

            for (int i = 0; i < fieldData.ResolutionX; i++)
            {
                for (int j = 0; j < fieldData.ResolutionY; j++)
                {
                    double val = fieldData.Values[i, j];
                    double normalized = (val - fieldData.MinValue) / (fieldData.MaxValue - fieldData.MinValue + 1e-10);
                    var color = GetJetColor(normalized);

                    cr.SetSourceRGBA(color.Red, color.Green, color.Blue, 1);
                    cr.Rectangle(margin + i * cellW, margin + (fieldData.ResolutionY - 1 - j) * cellH, cellW, cellH);
                    cr.Fill();
                }
            }

            // Colorbar
            double barX = width - margin - 30;
            double barW = 20;
            for (int i = 0; i < mapH; i++)
            {
                double t = 1 - i / mapH;
                var color = GetJetColor(t);
                cr.SetSourceRGBA(color.Red, color.Green, color.Blue, 1);
                cr.Rectangle(barX, margin + i, barW, 1);
                cr.Fill();
            }

            cr.SetSourceRGB(1, 1, 1);
            cr.Rectangle(barX, margin, barW, mapH);
            cr.Stroke();

            cr.MoveTo(barX + barW + 5, margin + 10);
            cr.ShowText($"{fieldData.MaxValue:G4}");
            cr.MoveTo(barX + barW + 5, margin + mapH);
            cr.ShowText($"{fieldData.MinValue:G4}");
        }

        private Gdk.RGBA GetJetColor(double t)
        {
            t = Math.Clamp(t, 0, 1);

            double r, g, b;
            if (t < 0.125)
            {
                r = 0; g = 0; b = 0.5 + t * 4;
            }
            else if (t < 0.375)
            {
                r = 0; g = (t - 0.125) * 4; b = 1;
            }
            else if (t < 0.625)
            {
                r = (t - 0.375) * 4; g = 1; b = 1 - (t - 0.375) * 4;
            }
            else if (t < 0.875)
            {
                r = 1; g = 1 - (t - 0.625) * 4; b = 0;
            }
            else
            {
                r = 1 - (t - 0.875) * 2; g = 0; b = 0;
            }

            return new Gdk.RGBA { Red = r, Green = g, Blue = b, Alpha = 1 };
        }

        private void OnExportChartPNG(object? sender, EventArgs e)
        {
            var dialog = new FileChooserDialog(
                "Export Chart as PNG",
                this,
                FileChooserAction.Save,
                "Cancel", ResponseType.Cancel,
                "Save", ResponseType.Accept);

            dialog.CurrentName = "chart.png";
            var filter = new FileFilter();
            filter.AddPattern("*.png");
            filter.Name = "PNG Image";
            dialog.AddFilter(filter);

            if (dialog.Run() == (int)ResponseType.Accept)
            {
                ExportChartToPNG(dialog.Filename);
            }
            dialog.Destroy();
        }

        private void OnExportColormapPNG(object? sender, EventArgs e)
        {
            var dialog = new FileChooserDialog(
                "Export Colormap as PNG",
                this,
                FileChooserAction.Save,
                "Cancel", ResponseType.Cancel,
                "Save", ResponseType.Accept);

            dialog.CurrentName = "colormap.png";
            var filter = new FileFilter();
            filter.AddPattern("*.png");
            filter.Name = "PNG Image";
            dialog.AddFilter(filter);

            if (dialog.Run() == (int)ResponseType.Accept)
            {
                ExportColormapToPNG(dialog.Filename);
            }
            dialog.Destroy();
        }

        private void ExportChartToPNG(string filename)
        {
            int width = 800;
            int height = 600;

            using var surface = new ImageSurface(Format.ARGB32, width, height);
            using var cr = new Context(surface);

            // Draw chart to surface (reusing chart drawing logic)
            // Background
            cr.SetSourceRGB(0.1, 0.1, 0.12);
            cr.Rectangle(0, 0, width, height);
            cr.Fill();

            // Similar to OnChartDrawn but to this context
            double marginLeft = 60, marginRight = 20, marginTop = 20, marginBottom = 40;
            double plotX = marginLeft, plotY = marginTop;
            double plotW = width - marginLeft - marginRight;
            double plotH = height - marginTop - marginBottom;

            cr.SetSourceRGB(0.15, 0.15, 0.15);
            cr.Rectangle(plotX, plotY, plotW, plotH);
            cr.Fill();

            // Determine Y range and draw data
            double yMin = 0, yMax = 100, maxTime = _chartTimeRange;
            foreach (var id in _chartProbeIds)
            {
                var probe = _probeManager.GetProbe(id);
                if (probe?.History.Count > 0)
                {
                    yMin = probe.History.Min(p => p.Value);
                    yMax = probe.History.Max(p => p.Value);
                    maxTime = probe.History.Max(p => p.Time);
                }
            }

            double minTime = Math.Max(0, maxTime - _chartTimeRange);

            foreach (var id in _chartProbeIds)
            {
                var probe = _probeManager.GetProbe(id);
                if (probe?.History.Count > 1)
                {
                    var color = ColorFromUint(probe.Color);
                    cr.SetSourceRGBA(color.Red, color.Green, color.Blue, 1);
                    cr.LineWidth = 2;

                    bool first = true;
                    foreach (var pt in probe.History.Where(p => p.Time >= minTime))
                    {
                        double x = plotX + (pt.Time - minTime) / _chartTimeRange * plotW;
                        double y = plotY + plotH - (pt.Value - yMin) / (yMax - yMin + 1e-10) * plotH;

                        if (first) { cr.MoveTo(x, y); first = false; }
                        else cr.LineTo(x, y);
                    }
                    cr.Stroke();
                }
            }

            surface.WriteToPng(filename);
        }

        private void ExportColormapToPNG(string filename)
        {
            var planeProbe = _selectedProbe as PlaneProbe ?? _probeManager.PlaneProbes.FirstOrDefault();
            if (planeProbe?.FieldHistory.Count == 0) return;

            var fieldData = planeProbe!.FieldHistory[^1];
            int width = fieldData.ResolutionX * 8;
            int height = fieldData.ResolutionY * 8;

            using var surface = new ImageSurface(Format.ARGB32, width, height);
            using var cr = new Context(surface);

            double cellW = (double)width / fieldData.ResolutionX;
            double cellH = (double)height / fieldData.ResolutionY;

            for (int i = 0; i < fieldData.ResolutionX; i++)
            {
                for (int j = 0; j < fieldData.ResolutionY; j++)
                {
                    double val = fieldData.Values[i, j];
                    double normalized = (val - fieldData.MinValue) / (fieldData.MaxValue - fieldData.MinValue + 1e-10);
                    var color = GetJetColor(normalized);

                    cr.SetSourceRGBA(color.Red, color.Green, color.Blue, 1);
                    cr.Rectangle(i * cellW, (fieldData.ResolutionY - 1 - j) * cellH, cellW, cellH);
                    cr.Fill();
                }
            }

            surface.WriteToPng(filename);
        }

        private Gdk.RGBA ColorFromUint(uint color)
        {
            return new Gdk.RGBA
            {
                Blue = ((color >> 0) & 0xFF) / 255.0,
                Green = ((color >> 8) & 0xFF) / 255.0,
                Red = ((color >> 16) & 0xFF) / 255.0,
                Alpha = ((color >> 24) & 0xFF) / 255.0
            };
        }

        private uint UintFromColor(Gdk.RGBA color)
        {
            uint r = (uint)(color.Red * 255) & 0xFF;
            uint g = (uint)(color.Green * 255) & 0xFF;
            uint b = (uint)(color.Blue * 255) & 0xFF;
            uint a = (uint)(color.Alpha * 255) & 0xFF;
            return (a << 24) | (r << 16) | (g << 8) | b;
        }
    }
}
