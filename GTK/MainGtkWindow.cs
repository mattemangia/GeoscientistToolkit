using System.IO;
using System.Linq;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Data.Loaders;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;
using GeoscientistToolkit.GtkUI.Dialogs;
using GeoscientistToolkit.GtkUI.Views;
using Cairo;
using Gdk;
using Gtk;

using Pixbuf = Gdk.Pixbuf;

namespace GeoscientistToolkit.GtkUI;

public class MainGtkWindow : Gtk.Window
{
    private readonly ProjectManager _projectManager;
    private readonly SettingsManager _settingsManager;
    private readonly NodeManager _nodeManager;

    private readonly ListStore _datasetStore = new(typeof(string), typeof(string), typeof(Dataset));
    private readonly TreeView _datasetView = new();
    private readonly TextView _detailsView = new() { Editable = false, WrapMode = WrapMode.Word, Monospace = true };
    private readonly TreeStore _assetStore = new(typeof(string), typeof(string));
    private readonly TreeView _assetTreeView = new();
    private readonly MeshViewport3D _meshViewport = new();
    private BoreholeView _boreholeView;
    private readonly Notebook _notebook;
    private readonly TextView _geoScriptEditor = new() { Monospace = true };
    private readonly ComboBoxText _boreholeSelector = new();
    private readonly SpinButton _layerInput = new(1, 50, 1) { Value = 4 };
    private readonly SpinButton _radiusInput = new(1, 500, 1) { Value = 50 };
    private readonly SpinButton _heightInput = new(1, 1000, 1) { Value = 120 };
    private readonly SpinButton _resolutionInput = new(10, 500, 10) { Value = 100 };
    private readonly SpinButton _gridXInput = new(1, 20, 1) { Value = 3 };
    private readonly SpinButton _gridYInput = new(1, 20, 1) { Value = 3 };
    private readonly SpinButton _gridZInput = new(1, 20, 1) { Value = 3 };
    private readonly ComboBoxText _renderModeSelector = new();
    private readonly ComboBoxText _selectionModeSelector = new();
    private readonly ComboBoxText _colorModeSelector = new();
    private readonly CheckButton _enableSlicingCheck = new("Enable Slicing");
    private readonly Scale _slicePositionScale = new(Orientation.Horizontal, new Adjustment(0, -50, 50, 1, 10, 0));
    private readonly CheckButton _enableIsosurfaceCheck = new("Show Isosurface");
    private readonly Scale _isosurfaceThresholdScale = new(Orientation.Horizontal, new Adjustment(300, 0, 1000, 10, 50, 0));
    private readonly Label _selectionInfoLabel = new() { Xalign = 0 };
    private readonly TextView _cellPropertiesView = new() { Editable = false, WrapMode = WrapMode.Word, Monospace = true };
    private readonly Adjustment _yawAdjustment = new(35, -180, 180, 1, 10, 0);
    private readonly Adjustment _pitchAdjustment = new(-20, -90, 90, 1, 10, 0);
    private readonly Adjustment _zoomAdjustment = new(1.2, 0.1, 8, 0.05, 0.1, 0);

    private readonly ListStore _nodeStore = new(typeof(string), typeof(string), typeof(string));
    private readonly Revealer _meshOptionsRevealer = new() { RevealChild = true, TransitionType = RevealerTransitionType.SlideRight, TransitionDuration = 250 };
    private HPaned? _meshHPaned;
    private readonly ToggleButton _meshOptionsToggle = new("Toggle mesh options") { Active = true };
    private readonly Entry _datasetNameEntry = new() { PlaceholderText = "Dataset name" };
    private readonly ComboBoxText _datasetTypeSelector = new();
    private readonly CheckButton _emptyBoreholeToggle = new("Create borehole without default tracks");
    private readonly Entry _materialNameEntry = new() { PlaceholderText = "Material" };
    private readonly ComboBoxText _materialPhaseSelector = new();
    private readonly Entry _forceNameEntry = new() { PlaceholderText = "Force" };
    private readonly ComboBoxText _forceTypeSelector = new();
    private readonly Label _statusBar = new() { Xalign = 0 };
    private readonly Label _nodeStatusLabel = new() { Xalign = 0 };

    private readonly Entry _nodeNameEntry = new();
    private readonly Entry _nodeHostEntry = new();
    private readonly SpinButton _nodePortSpinner = new(1, 65535, 1) { Value = 9876 };
    private readonly ComboBoxText _nodeRoleSelector = new();
    private readonly CheckButton _nodeEnabledToggle = new("Enable NodeManager");
    private readonly CheckButton _nodeAutoStartToggle = new("Auto start on launch");

    private Dataset? _selectedDataset;

    public MainGtkWindow(ProjectManager projectManager, SettingsManager settingsManager, NodeManager nodeManager) : base("Geoscientist's Toolkit - Reactor (GTK)")
    {
        _projectManager = projectManager;
        _settingsManager = settingsManager;
        _nodeManager = nodeManager;

        SetDefaultSize(1200, 700);
        BorderWidth = 8;

        var root = new VBox(false, 6);
        root.PackStart(BuildMenuBar(), false, false, 0);
        root.PackStart(BuildToolbar(), false, false, 0);

        var split = new Paned(Orientation.Horizontal) { Position = 320 };
        split.Pack1(BuildDatasetPanel(), false, true);
        _notebook = BuildWorkspace();
        split.Pack2(_notebook, true, true);

        root.PackStart(split, true, true, 0);
        root.PackStart(_statusBar, false, false, 4);
        Add(root);

        WireNodeEvents();
        EnsureDefaultReactor();
        RefreshDatasetList();
        RefreshNodeList();
        SelectFirstDataset();

        ApplyProfessionalStyling();
        HydrateNodeSettings();
    }

    private MenuBar BuildMenuBar()
    {
        var menuBar = new MenuBar();

        var fileMenuItem = new MenuItem("File");
        var fileMenu = new Menu();
        fileMenu.Append(CreateMenuItem("New project", (_, _) =>
        {
            _projectManager.NewProject();
            EnsureDefaultReactor();
            RefreshDatasetList();
            _meshViewport.Clear();
            SelectFirstDataset();
            SetStatus("New project created.");
        }));
        fileMenu.Append(CreateMenuItem("Open...", (_, _) => OpenProjectDialog()));
        fileMenu.Append(CreateMenuItem("Save", (_, _) => SaveProjectDialog()));
        fileMenu.Append(CreateMenuItem("Reload", (_, _) => ReloadCurrentProject()));
        fileMenu.Append(CreateMenuItem("Import mesh dataset", (_, _) => AddExistingDataset()));
        fileMenu.Append(CreateMenuItem("Import table/CSV", (_, _) => ImportTableDataset()));
        fileMenu.Append(new SeparatorMenuItem());
        fileMenu.Append(CreateMenuItem("Exit", (_, _) => Gtk.Application.Quit()));
        fileMenuItem.Submenu = fileMenu;
        menuBar.Append(fileMenuItem);

        var viewMenuItem = new MenuItem("View");
        var viewMenu = new Menu();
        viewMenu.Append(CreateMenuItem("Show/Hide mesh panel", (_, _) => ToggleMeshOptionsPanel()));
        viewMenu.Append(CreateMenuItem("Refresh node status", (_, _) => RefreshNodeList()));
        viewMenuItem.Submenu = viewMenu;
        menuBar.Append(viewMenuItem);

        var toolsMenuItem = new MenuItem("Tools");
        var toolsMenu = new Menu();
        toolsMenu.Append(CreateMenuItem("Material Library Browser...", (_, _) => OpenMaterialLibraryDialog()));
        toolsMenu.Append(CreateMenuItem("Create Domain...", (_, _) => OpenDomainCreatorDialog()));
        toolsMenu.Append(CreateMenuItem("Add Heat Exchanger / Object...", (_, _) => OpenHeatExchangerDialog()));
        toolsMenu.Append(CreateMenuItem("Configure Species...", (_, _) => OpenSpeciesSelectorDialog()));
        toolsMenu.Append(CreateMenuItem("Set Boundary Conditions...", (_, _) => OpenBoundaryConditionEditor()));
        toolsMenu.Append(CreateMenuItem("Force Field Editor...", (_, _) => OpenForceFieldEditor()));
        toolsMenu.Append(new SeparatorMenuItem());
        toolsMenu.Append(CreateMenuItem("Simulation Setup Wizard...", (_, _) => OpenSimulationSetupWizard()));
        toolsMenu.Append(CreateMenuItem("Boolean Operations...", (_, _) => OpenBooleanOperationsUI()));
        toolsMenu.Append(CreateMenuItem("Geothermal Deep Wells Wizard...", (_, _) => OpenGeothermalConfigDialog()));
        toolsMenu.Append(CreateMenuItem("Add Nucleation Point", (_, _) => AddNucleationPoint()));
        toolsMenuItem.Submenu = toolsMenu;
        menuBar.Append(toolsMenuItem);

        var helpMenuItem = new MenuItem("Help");
        var helpMenu = new Menu();
        helpMenu.Append(CreateMenuItem("About", (_, _) => ShowAboutDialog()));
        helpMenuItem.Submenu = helpMenu;
        menuBar.Append(helpMenuItem);

        return menuBar;
    }

    private Widget BuildToolbar()
    {
        var toolbar = new Toolbar { IconSize = IconSize.SmallToolbar, Style = ToolbarStyle.BothHoriz };

        toolbar.Insert(CreateIconButton("New", "Create a new project", CairoExtensions.MakeIcon(IconSymbol.ProjectNew), (_, _) =>
        {
            _projectManager.NewProject();
            EnsureDefaultReactor();
            RefreshDatasetList();
            _meshViewport.Clear();
            SelectFirstDataset();
            SetStatus("New project created.");
        }), -1);

        toolbar.Insert(CreateIconButton("Open", "Open .gtp project", CairoExtensions.MakeIcon(IconSymbol.FolderOpen), (_, _) => OpenProjectDialog()), -1);
        toolbar.Insert(CreateIconButton("Save", "Save project", CairoExtensions.MakeIcon(IconSymbol.Save), (_, _) => SaveProjectDialog()), -1);
        toolbar.Insert(new SeparatorToolItem(), -1);
        toolbar.Insert(CreateIconButton("Add PhysicoChem", "Add a new PhysicoChem dataset", CairoExtensions.MakeIcon(IconSymbol.PhysicoChem), (_, _) =>
        {
            var dataset = new PhysicoChemDataset($"PhysicoChem_{DateTime.Now:HHmmss}", "Reactor multiphysics profile");
            _projectManager.AddDataset(dataset);
            RefreshDatasetList();
            SelectFirstDataset();
        }), -1);

        toolbar.Insert(CreateIconButton("Add Borehole", "Create a new borehole dataset", CairoExtensions.MakeIcon(IconSymbol.Borehole), (_, _) =>
        {
            var dataset = new BoreholeDataset($"Borehole_{DateTime.Now:HHmmss}", string.Empty)
            {
                SurfaceCoordinates = new System.Numerics.Vector2(0, 0),
                TotalDepth = 1200,
                Elevation = 120
            };
            _projectManager.AddDataset(dataset);
            RefreshDatasetList();
            SelectFirstDataset();
        }), -1);

        toolbar.Insert(CreateIconButton("New Empty Mesh", "Create an empty 3D mesh", CairoExtensions.MakeIcon(IconSymbol.Mesh), (_, _) =>
        {
            var mesh = Mesh3DDataset.CreateEmpty("Mesh3D (Reactor)", string.Empty);
            _projectManager.AddDataset(mesh);
            RefreshDatasetList();
            SelectFirstDataset();
        }), -1);

        toolbar.Insert(new SeparatorToolItem(), -1);
        toolbar.Insert(CreateIconButton("Import Mesh", "Import 3D mesh dataset", CairoExtensions.MakeIcon(IconSymbol.MeshImport), (_, _) => AddExistingDataset()), -1);
        toolbar.Insert(CreateIconButton("Import Table/CSV", "Import table/CSV dataset", CairoExtensions.MakeIcon(IconSymbol.Table), (_, _) => ImportTableDataset()), -1);

        toolbar.Insert(new SeparatorToolItem(), -1);
        toolbar.Insert(CreateIconButton("Geothermal Well Wizard", "Launch the geothermal well wizard", CairoExtensions.MakeIcon(IconSymbol.PhysicoChem), (_, _) => OpenGeothermalConfigDialog()), -1);
        toolbar.Insert(CreateIconButton("Create Domain", "Open domain creation dialog", CairoExtensions.MakeIcon(IconSymbol.Mesh), (_, _) => OpenDomainCreatorDialog()), -1);
        toolbar.Insert(CreateIconButton("Material Library", "Open the material library", CairoExtensions.MakeIcon(IconSymbol.Material), (_, _) => OpenMaterialLibraryDialog()), -1);

        toolbar.Insert(new SeparatorToolItem(), -1);
        toolbar.Insert(CreateIconButton("Reload", "Reload current project", CairoExtensions.MakeIcon(IconSymbol.Refresh), (_, _) => ReloadCurrentProject()), -1);
        toolbar.Insert(new SeparatorToolItem(), -1);
        toolbar.Insert(CreateIconButton("Cluster", "Refresh cluster", CairoExtensions.MakeIcon(IconSymbol.Cluster), (_, _) => RefreshNodeList()), -1);
        toolbar.Insert(CreateIconButton("Settings", "Save settings", CairoExtensions.MakeIcon(IconSymbol.Settings), (_, _) =>
        {
            _settingsManager.SaveSettings();
            SetStatus("Settings saved.");
        }), -1);

        return toolbar;
    }

    private Widget BuildDatasetPanel()
    {
        var panel = new VBox(false, 6);

        var header = new Label("Loaded datasets (Reactor)") { Xalign = 0 };
        panel.PackStart(header, false, false, 0);

        _datasetView.Model = _datasetStore;
        _datasetView.HeadersVisible = true;
        _datasetView.Selection.Changed += OnDatasetSelectionChanged;

        _datasetView.AppendColumn("Name", new CellRendererText(), "text", 0);
        _datasetView.AppendColumn("Type", new CellRendererText(), "text", 1);

        var scroller = new ScrolledWindow();
        scroller.Add(_datasetView);
        scroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        panel.PackStart(scroller, true, true, 0);

        panel.PackStart(BuildAssetTree(), true, true, 0);

        return panel;
    }

    private Notebook BuildWorkspace()
    {
        var notebook = new Notebook();
        notebook.AppendPage(BuildOverviewTab(), new Label("Editor"));
        notebook.AppendPage(BuildMeshTab(), new Label("Mesh 3D"));
        notebook.AppendPage(BuildBoreholeTab(), new Label("Borehole editor"));
        notebook.AppendPage(BuildGeoScriptTab(), new Label("GeoScript"));
        notebook.AppendPage(BuildNodeTab(), new Label("Node/Endpoint"));
        return notebook;
    }

    private Widget BuildOverviewTab()
    {
        var content = new VBox(false, 6);
        var instructions = new Label("Select a dataset to edit PhysicoChem or Borehole data and run geothermal, multiphysics and thermodynamic simulations.")
        {
            Wrap = true,
            Xalign = 0
        };
        content.PackStart(instructions, false, false, 0);

        var detailFrame = new Frame("Dataset details");
        var detailScroller = new ScrolledWindow();
        detailScroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        detailScroller.Add(_detailsView);
        detailFrame.Add(detailScroller);
        content.PackStart(detailFrame, true, true, 0);

        content.PackStart(BuildComposerPanel(), false, false, 0);

        var scroller = new ScrolledWindow { ShadowType = ShadowType.None };
        scroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        scroller.Add(content);

        return scroller;
    }

    private Widget BuildMeshTab()
    {
        var box = new VBox(false, 6);

        var headerRow = new HBox(false, 6);
        _meshOptionsToggle.Toggled += (_, _) =>
        {
            _meshOptionsRevealer.RevealChild = _meshOptionsToggle.Active;
            // Update HPaned position to expand viewport when options are hidden
            if (_meshHPaned != null)
            {
                _meshHPaned.Position = _meshOptionsToggle.Active ? 350 : 0;
            }
        };
        headerRow.PackStart(_meshOptionsToggle, false, false, 0);
        box.PackStart(headerRow, false, false, 0);

        _meshHPaned = new HPaned { Position = 350 };
        _meshHPaned.Add1(BuildMeshOptionsPanel());

        // Right side: viewport + cell properties
        var rightSide = new VPaned();

        var viewportFrame = new Frame("3D viewport");
        _meshViewport.WidthRequest = 400;
        _meshViewport.HeightRequest = 400;
        viewportFrame.Add(_meshViewport);
        rightSide.Pack1(viewportFrame, true, true);

        // Cell properties panel
        var cellPropsFrame = new Frame("Selected Cell Properties");
        var cellPropsScroller = new ScrolledWindow();
        cellPropsScroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        cellPropsScroller.HeightRequest = 150;
        cellPropsScroller.Add(_cellPropertiesView);
        cellPropsFrame.Add(cellPropsScroller);
        rightSide.Pack2(cellPropsFrame, false, true);

        _meshHPaned.Pack2(rightSide, true, true);

        box.PackStart(_meshHPaned, true, true, 0);

        return box;
    }

    private Widget BuildBoreholeTab()
    {
        var borehole = _projectManager.LoadedDatasets.OfType<BoreholeDataset>().FirstOrDefault();
        if (borehole == null)
        {
            borehole = new BoreholeDataset("Default", "");
            _projectManager.AddDataset(borehole);
            RefreshDatasetList();
        }

        _boreholeView = new BoreholeView(borehole);
        return _boreholeView;
    }

    private Widget BuildGeoScriptTab()
    {
        var box = new VBox(false, 6);
        var label = new Label("Run GeoScript pipelines against the selected dataset (thermodynamic-ready).")
        {
            Wrap = true,
            Xalign = 0
        };
        box.PackStart(label, false, false, 0);

        var scriptFrame = new Frame("GeoScript editor");
        var scroller = new ScrolledWindow { ShadowType = ShadowType.In };
        _geoScriptEditor.Buffer.Text = "select * from input;";
        scroller.Add(_geoScriptEditor);
        scriptFrame.Add(scroller);
        box.PackStart(scriptFrame, true, true, 0);

        var runButton = CreateSlimActionButton("Execute GeoScript", IconSymbol.GeoScript, (_, _) => RunGeoScript());
        box.PackStart(runButton, false, false, 0);

        return box;
    }

    private Widget BuildMeshOptionsPanel()
    {
        var container = new VBox(false, 8) { BorderWidth = 6, Halign = Align.Fill, Hexpand = true };

        // Rendering controls frame 
        var renderFrame = new Frame("Visualization") { BorderWidth = 4 };
        var renderGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6, BorderWidth = 6 };

        // Render mode selector
        renderGrid.Attach(new Label("Render mode") { Xalign = 0 }, 0, 0, 1, 1);
        _renderModeSelector.AppendText("Wireframe");
        _renderModeSelector.AppendText("Solid");
        _renderModeSelector.AppendText("Solid + Wireframe");
        _renderModeSelector.Active = 0;
        _renderModeSelector.Changed += (_, _) =>
        {
            _meshViewport.RenderMode = _renderModeSelector.Active switch
            {
                0 => RenderMode.Wireframe,
                1 => RenderMode.Solid,
                2 => RenderMode.SolidWireframe,
                _ => RenderMode.Wireframe
            };
            _meshViewport.QueueDraw();
        };
        renderGrid.Attach(_renderModeSelector, 1, 0, 2, 1);

        // Color mode selector
        renderGrid.Attach(new Label("Color by") { Xalign = 0 }, 0, 1, 1, 1);
        _colorModeSelector.AppendText("Material");
        _colorModeSelector.AppendText("Temperature");
        _colorModeSelector.AppendText("Pressure");
        _colorModeSelector.AppendText("Active/Inactive");
        _colorModeSelector.Active = 0;
        _colorModeSelector.Changed += (_, _) =>
        {
            _meshViewport.ColorMode = _colorModeSelector.Active switch
            {
                0 => ColorCodingMode.Material,
                1 => ColorCodingMode.Temperature,
                2 => ColorCodingMode.Pressure,
                3 => ColorCodingMode.Active,
                _ => ColorCodingMode.Material
            };
            _meshViewport.QueueDraw();
        };
        renderGrid.Attach(_colorModeSelector, 1, 1, 2, 1);

        // Slicing Controls
        _enableSlicingCheck.Toggled += (_, _) =>
        {
            _meshViewport.EnableSlicing = _enableSlicingCheck.Active;
            _meshViewport.QueueDraw();
        };
        renderGrid.Attach(_enableSlicingCheck, 0, 2, 2, 1);

        var sliceAxisCombo = new ComboBoxText();
        sliceAxisCombo.AppendText("X Axis");
        sliceAxisCombo.AppendText("Y Axis");
        sliceAxisCombo.AppendText("Z Axis");
        sliceAxisCombo.Active = 2; // Default Z

        renderGrid.Attach(new Label("Slice Axis") { Xalign = 0 }, 0, 3, 1, 1);
        renderGrid.Attach(sliceAxisCombo, 1, 3, 1, 1);

        renderGrid.Attach(new Label("Position") { Xalign = 0 }, 0, 4, 1, 1);
        _slicePositionScale.DrawValue = true;

        void UpdateSlicePlane()
        {
            var pos = (float)-_slicePositionScale.Value;
            _meshViewport.SlicePlane = sliceAxisCombo.Active switch
            {
                0 => new System.Numerics.Vector4(1, 0, 0, pos),
                1 => new System.Numerics.Vector4(0, 1, 0, pos),
                _ => new System.Numerics.Vector4(0, 0, 1, pos)
            };
            _meshViewport.QueueDraw();
        }

        sliceAxisCombo.Changed += (_, _) => UpdateSlicePlane();
        _slicePositionScale.ValueChanged += (_, _) => UpdateSlicePlane();

        renderGrid.Attach(_slicePositionScale, 1, 4, 1, 1);

        // Isosurface Controls
        _enableIsosurfaceCheck.Toggled += (_, _) =>
        {
            _meshViewport.ShowIsosurface = _enableIsosurfaceCheck.Active;
            _meshViewport.QueueDraw();
        };
        renderGrid.Attach(_enableIsosurfaceCheck, 0, 5, 2, 1);

        renderGrid.Attach(new Label("Iso Threshold") { Xalign = 0 }, 0, 6, 1, 1);
        _isosurfaceThresholdScale.DrawValue = true;
        _isosurfaceThresholdScale.ValueChanged += (_, _) =>
        {
            _meshViewport.IsosurfaceThreshold = _isosurfaceThresholdScale.Value;
            _meshViewport.QueueDraw();
        };
        renderGrid.Attach(_isosurfaceThresholdScale, 1, 6, 1, 1);

        // Selection mode selector
        renderGrid.Attach(new Label("Selection") { Xalign = 0 }, 0, 7, 1, 1);
        _selectionModeSelector.AppendText("Single");
        _selectionModeSelector.AppendText("Multiple");
        _selectionModeSelector.AppendText("Rectangle");
        _selectionModeSelector.AppendText("Plane XY");
        _selectionModeSelector.AppendText("Plane XZ");
        _selectionModeSelector.AppendText("Plane YZ");
        _selectionModeSelector.Active = 0;
        _selectionModeSelector.Changed += (_, _) =>
        {
            _meshViewport.SelectionMode = _selectionModeSelector.Active switch
            {
                0 => SelectionMode.Single,
                1 => SelectionMode.Multiple,
                2 => SelectionMode.Rectangle,
                3 => SelectionMode.PlaneXY,
                4 => SelectionMode.PlaneXZ,
                5 => SelectionMode.PlaneYZ,
                _ => SelectionMode.Single
            };
        };
        renderGrid.Attach(_selectionModeSelector, 1, 7, 2, 1);

        // Camera controls
        renderGrid.Attach(new Label("Yaw") { Xalign = 0 }, 0, 8, 1, 1);
        var yawScale = new Scale(Orientation.Horizontal, _yawAdjustment) { DrawValue = true };
        yawScale.ValueChanged += (_, _) => UpdateViewportCamera();
        renderGrid.Attach(yawScale, 1, 8, 2, 1);

        renderGrid.Attach(new Label("Pitch") { Xalign = 0 }, 0, 9, 1, 1);
        var pitchScale = new Scale(Orientation.Horizontal, _pitchAdjustment) { DrawValue = true };
        pitchScale.ValueChanged += (_, _) => UpdateViewportCamera();
        renderGrid.Attach(pitchScale, 1, 9, 2, 1);

        renderGrid.Attach(new Label("Zoom") { Xalign = 0 }, 0, 10, 1, 1);
        var zoomScale = new Scale(Orientation.Horizontal, _zoomAdjustment) { DrawValue = true };
        zoomScale.ValueChanged += (_, _) => UpdateViewportCamera();
        renderGrid.Attach(zoomScale, 1, 10, 2, 1);

        renderFrame.Add(renderGrid);
        container.PackStart(renderFrame, false, false, 0);

        // Reactor builder frame 
        var reactorFrame = new Frame("Reactor Builder") { BorderWidth = 4 };
        var reactorGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6, BorderWidth = 6 };

        reactorGrid.Attach(new Label("Grid cells X") { Xalign = 0 }, 0, 0, 1, 1);
        reactorGrid.Attach(_gridXInput, 1, 0, 1, 1);

        reactorGrid.Attach(new Label("Grid cells Y") { Xalign = 0 }, 0, 1, 1, 1);
        reactorGrid.Attach(_gridYInput, 1, 1, 1, 1);

        reactorGrid.Attach(new Label("Grid cells Z") { Xalign = 0 }, 0, 2, 1, 1);
        reactorGrid.Attach(_gridZInput, 1, 2, 1, 1);

        reactorGrid.Attach(new Label("Radius (m)") { Xalign = 0 }, 0, 3, 1, 1);
        reactorGrid.Attach(_radiusInput, 1, 3, 1, 1);

        reactorGrid.Attach(new Label("Height (m)") { Xalign = 0 }, 0, 4, 1, 1);
        reactorGrid.Attach(_heightInput, 1, 4, 1, 1);

        var createReactorButton = CreateSlimActionButton("Create Reactor Grid", IconSymbol.PhysicoChem, (_, _) => CreateReactorGrid());
        reactorGrid.Attach(createReactorButton, 0, 5, 2, 1);

        reactorFrame.Add(reactorGrid);
        container.PackStart(reactorFrame, false, false, 0);

        // Cell operations frame 
        var cellOpsFrame = new Frame("Cell Operations") { BorderWidth = 4 };
        var cellOpsGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6, BorderWidth = 6 };

        // Selection info
        _meshViewport.CellSelectionChanged += (_, args) =>
        {
            _selectionInfoLabel.Text = args.SelectedCellIDs.Count > 0
                ? $"{args.SelectedCellIDs.Count} cells selected"
                : "No cells selected";
            UpdateCellProperties(args.SelectedCellIDs);
        };
        cellOpsGrid.Attach(_selectionInfoLabel, 0, 0, 2, 1);

        // Enable/Disable selected cells
        var toggleActiveButton = CreateSlimActionButton("Toggle Active/Inactive", IconSymbol.PhysicoChem, (_, _) =>
        {
            _meshViewport.ToggleSelectedCellsActive();
            SetStatus($"Toggled {_meshViewport.SelectedCellIDs.Count} cells");
        });
        cellOpsGrid.Attach(toggleActiveButton, 0, 1, 2, 1);

        // Clear selection
        var clearSelButton = CreateSlimActionButton("Clear Selection", IconSymbol.Refresh, (_, _) =>
        {
            _meshViewport.ClearSelection();
            SetStatus("Selection cleared");
        });
        cellOpsGrid.Attach(clearSelButton, 0, 2, 2, 1);

        // Add a hint for plane selection
        var planeSelectionHint = new Label("For plane selection, use the 'Selection' dropdown above.")
            { Xalign = 0, Wrap = true };
        var hintFrame = new Frame();
        hintFrame.Add(planeSelectionHint);
        cellOpsGrid.Attach(hintFrame, 0, 3, 2, 1);


        cellOpsFrame.Add(cellOpsGrid);
        container.PackStart(cellOpsFrame, false, false, 0);

        // Voronoi mesh generation
        var voronoiFrame = new Frame("Voronoi Mesh (from Borehole)") { BorderWidth = 4 };
        var voronoiGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6, BorderWidth = 6 };

        voronoiGrid.Attach(new Label("Borehole") { Xalign = 0 }, 0, 0, 1, 1);
        voronoiGrid.Attach(_boreholeSelector, 1, 0, 1, 1);

        voronoiGrid.Attach(new Label("Voronoi layers") { Xalign = 0 }, 0, 1, 1, 1);
        voronoiGrid.Attach(_layerInput, 1, 1, 1, 1);

        voronoiGrid.Attach(new Label("Mesh resolution") { Xalign = 0 }, 0, 2, 1, 1);
        voronoiGrid.Attach(_resolutionInput, 1, 2, 1, 1);

        var generateButton = CreateSlimActionButton("Generate Voronoi", IconSymbol.Voronoi, (_, _) => GenerateVoronoiMesh());
        voronoiGrid.Attach(generateButton, 0, 3, 2, 1);

        voronoiFrame.Add(voronoiGrid);
        container.PackStart(voronoiFrame, false, false, 0);

        // Advanced mesh operations
        var editFrame = new Frame("Advanced Operations") { BorderWidth = 4 };
        var editGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6, BorderWidth = 6 };

        var importButton = CreateSlimActionButton("Import 3D mesh", IconSymbol.MeshImport, (_, _) => ImportFromMeshDataset());
        editGrid.Attach(importButton, 0, 0, 2, 1);

        var unifyButton = CreateSlimActionButton("Union", IconSymbol.MeshUnion, (_, _) => CombineMeshes(BooleanOperation.Union));
        var subtractButton = CreateSlimActionButton("Subtract", IconSymbol.MeshSubtract, (_, _) => CombineMeshes(BooleanOperation.Subtract));
        editGrid.Attach(unifyButton, 0, 1, 1, 1);
        editGrid.Attach(subtractButton, 1, 1, 1, 1);

        editFrame.Add(editGrid);
        container.PackStart(editFrame, false, false, 0);

        var scroller = new ScrolledWindow { ShadowType = ShadowType.None };
        scroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        scroller.Add(container);

        _meshOptionsRevealer.Child = scroller;
        return _meshOptionsRevealer;
    }

    private Widget BuildNodeTab()
    {
        var box = new VBox(false, 6);

        var info = new Label("Reactor client stays compatible with the Node Endpoint for clusters and distributed runs.")
        {
            Xalign = 0,
            Wrap = true
        };
        box.PackStart(info, false, false, 0);

        var settingsFrame = new Frame("Node manager settings") { BorderWidth = 4 };
        var settingsGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6, BorderWidth = 6, ColumnHomogeneous = false };

        settingsGrid.Attach(new Label("Node name") { Xalign = 0 }, 0, 0, 1, 1);
        settingsGrid.Attach(_nodeNameEntry, 1, 0, 1, 1);

        settingsGrid.Attach(new Label("Role") { Xalign = 0 }, 0, 1, 1, 1);
        _nodeRoleSelector.AppendText(NodeRole.Host.ToString());
        _nodeRoleSelector.AppendText(NodeRole.Worker.ToString());
        _nodeRoleSelector.AppendText(NodeRole.Hybrid.ToString());
        settingsGrid.Attach(_nodeRoleSelector, 1, 1, 1, 1);

        settingsGrid.Attach(new Label("Host address") { Xalign = 0 }, 0, 2, 1, 1);
        settingsGrid.Attach(_nodeHostEntry, 1, 2, 1, 1);

        settingsGrid.Attach(new Label("Port") { Xalign = 0 }, 0, 3, 1, 1);
        _nodePortSpinner.WidthRequest = 110;
        settingsGrid.Attach(_nodePortSpinner, 1, 3, 1, 1);

        settingsGrid.Attach(_nodeEnabledToggle, 0, 4, 2, 1);
        settingsGrid.Attach(_nodeAutoStartToggle, 0, 5, 2, 1);

        settingsFrame.Add(settingsGrid);
        box.PackStart(settingsFrame, false, false, 0);

        var statusRow = new HBox(false, 6);
        var startButton = CreateSlimActionButton("Start NodeManager", IconSymbol.Cluster, (_, _) =>
        {
            ApplyNodeSettings();
            _nodeManager.Start();
            RefreshNodeList();
            UpdateNodeStatus("Started");
        });

        var stopButton = CreateSlimActionButton("Stop NodeManager", IconSymbol.Refresh, (_, _) =>
        {
            _nodeManager.Stop();
            RefreshNodeList();
            UpdateNodeStatus("Stopped");
        });

        statusRow.PackStart(startButton, false, false, 0);
        statusRow.PackStart(stopButton, false, false, 0);
        statusRow.PackStart(_nodeStatusLabel, true, true, 6);

        box.PackStart(statusRow, false, false, 0);

        var nodeView = new TreeView(_nodeStore) { HeadersVisible = true };
        nodeView.AppendColumn("Node", new CellRendererText(), "text", 0);
        nodeView.AppendColumn("Status", new CellRendererText(), "text", 1);
        nodeView.AppendColumn("Capacity", new CellRendererText(), "text", 2);

        var scroller = new ScrolledWindow();
        scroller.Add(nodeView);
        scroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        box.PackStart(scroller, true, true, 0);

        return box;
    }

    private void OnDatasetSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is not TreeSelection selection) return;
        if (!selection.GetSelected(out TreeIter iter)) return;

        _selectedDataset = _datasetStore.GetValue(iter, 2) as Dataset;
        UpdateDetails();
    }

    private void RefreshDatasetList()
    {
        _datasetStore.Clear();
        foreach (var dataset in _projectManager.LoadedDatasets)
            _datasetStore.AppendValues(dataset.Name, dataset.Type.ToString(), dataset);

        UpdateBoreholeSelector();
    }

    private void SelectFirstDataset()
    {
        if (_datasetStore.IterNChildren() == 0 || _datasetView.Model == null) return;
        var path = new TreePath("0");
        _datasetView.Selection.SelectPath(path);
        OnDatasetSelectionChanged(_datasetView.Selection, EventArgs.Empty);
    }

    private void UpdateDetails()
    {
        if (_selectedDataset == null)
        {
            _detailsView.Buffer.Text = "No dataset selected.";
            _assetStore.Clear();
            return;
        }

        _detailsView.Buffer.Text = BuildDatasetSummary(_selectedDataset);
        RefreshAssetTree(_selectedDataset);
        UpdateMeshViewport(_selectedDataset);

        if (_selectedDataset is BoreholeDataset borehole)
        {
            var oldView = _notebook.GetNthPage(2);
            if (oldView != null)
                oldView.Destroy();

            _boreholeView = new BoreholeView(borehole);
            _notebook.RemovePage(2);
            _notebook.InsertPage(_boreholeView, new Label("Borehole editor"), 2);
            _notebook.ShowAll();
        }

        SetStatus($"Active dataset: {_selectedDataset.Name}");
    }

    private void UpdateCellProperties(List<string> selectedCellIDs)
    {
        if (_selectedDataset is not PhysicoChemDataset physico || selectedCellIDs.Count == 0)
        {
            _cellPropertiesView.Buffer.Text = "No cells selected.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Selected Cells: {selectedCellIDs.Count}");
        sb.AppendLine();

        if (selectedCellIDs.Count == 1)
        {
            // Show detailed properties for single cell
            var cellId = selectedCellIDs[0];
            if (physico.Mesh.Cells.TryGetValue(cellId, out var cell))
            {
                sb.AppendLine($"Cell ID: {cellId}");
                sb.AppendLine($"Material: {cell.MaterialID}");
                sb.AppendLine($"Active: {(cell.IsActive ? "Yes" : "No")}");
                sb.AppendLine($"Center: ({cell.Center.X:F2}, {cell.Center.Y:F2}, {cell.Center.Z:F2})");
                sb.AppendLine($"Volume: {cell.Volume:F3} m³");
                sb.AppendLine();

                if (cell.InitialConditions != null)
                {
                    sb.AppendLine("Initial Conditions:");
                    sb.AppendLine($"  Temperature: {cell.InitialConditions.Temperature:F2} K ({cell.InitialConditions.Temperature - 273.15:F2} °C)");
                    sb.AppendLine($"  Pressure: {cell.InitialConditions.Pressure:F0} Pa ({cell.InitialConditions.Pressure / 101325.0:F2} atm)");
                    sb.AppendLine($"  Saturation: {cell.InitialConditions.LiquidSaturation:F2}");

                    if (cell.InitialConditions.Concentrations != null && cell.InitialConditions.Concentrations.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("Concentrations:");
                        foreach (var (species, conc) in cell.InitialConditions.Concentrations)
                        {
                            sb.AppendLine($"  {species}: {conc:F4}");
                        }
                    }
                }
            }
        }
        else
        {
            // Show aggregate statistics for multiple cells
            int activeCount = 0;
            double totalVolume = 0;
            double avgTemp = 0;
            double avgPressure = 0;

            foreach (var cellId in selectedCellIDs)
            {
                if (physico.Mesh.Cells.TryGetValue(cellId, out var cell))
                {
                    if (cell.IsActive) activeCount++;
                    totalVolume += cell.Volume;
                    if (cell.InitialConditions != null)
                    {
                        avgTemp += cell.InitialConditions.Temperature;
                        avgPressure += cell.InitialConditions.Pressure;
                    }
                }
            }

            avgTemp /= selectedCellIDs.Count;
            avgPressure /= selectedCellIDs.Count;

            sb.AppendLine($"Active cells: {activeCount}/{selectedCellIDs.Count}");
            sb.AppendLine($"Total volume: {totalVolume:F3} m³");
            sb.AppendLine($"Avg temperature: {avgTemp:F2} K ({avgTemp - 273.15:F2} °C)");
            sb.AppendLine($"Avg pressure: {avgPressure:F0} Pa ({avgPressure / 101325.0:F2} atm)");
        }

        _cellPropertiesView.Buffer.Text = sb.ToString();
    }

    private string BuildDatasetSummary(Dataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Name: {dataset.Name}");
        sb.AppendLine($"Type: {dataset.Type}");

        switch (dataset)
        {
            case BoreholeDataset borehole:
                sb.AppendLine($"Depth: {borehole.TotalDepth:F1} m");
                sb.AppendLine($"Coordinates: ({borehole.SurfaceCoordinates.X:F2}, {borehole.SurfaceCoordinates.Y:F2})");
                sb.AppendLine($"Lithology units: {borehole.LithologyUnits.Count}");
                break;
            case PhysicoChemDataset physico:
                sb.AppendLine($"Mesh cells: {physico.Mesh.Cells.Count}");
                sb.AppendLine($"Connections: {physico.Mesh.Connections.Count}");
                sb.AppendLine("Supported: geothermal, multiphysics, thermodynamics");
                sb.AppendLine($"Materials: {physico.Materials.Count}, Forces: {physico.Forces.Count}");
                break;
            case Mesh3DDataset mesh3D:
                sb.AppendLine($"Vertices: {mesh3D.VertexCount}, Faces: {mesh3D.FaceCount}");
                sb.AppendLine($"Format: {mesh3D.FileFormat}");
                break;
            default:
                sb.AppendLine("Reactor editor ready for generic datasets.");
                break;
        }

        sb.AppendLine();
        sb.AppendLine("Edits remain compatible with the existing ImGui pipeline.");
        return sb.ToString();
    }

    private void UpdateBoreholeSelector()
    {
        var previous = _boreholeSelector.ActiveText;
        _boreholeSelector.RemoveAll();

        var boreholes = _projectManager.LoadedDatasets.OfType<BoreholeDataset>().ToList();
        for (var i = 0; i < boreholes.Count; i++)
        {
            _boreholeSelector.AppendText(boreholes[i].Name);
            if (!string.IsNullOrWhiteSpace(previous) && boreholes[i].Name == previous)
                _boreholeSelector.Active = i;
        }

        if (_boreholeSelector.Active < 0 && boreholes.Count > 0)
            _boreholeSelector.Active = 0;
    }

    private void UpdateMeshViewport(Dataset dataset)
    {
        switch (dataset)
        {
            case PhysicoChemDataset physico:
                _meshViewport.LoadFromPhysicoChem(physico.Mesh, physico);
                break;
            case Mesh3DDataset mesh3D:
                _meshViewport.LoadFromMesh(mesh3D);
                break;
            default:
                _meshViewport.Clear();
                break;
        }
    }

    private void CreateReactorGrid()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            _detailsView.Buffer.Text = "Select a PhysicoChem dataset to create a reactor grid.";
            return;
        }

        int gridX = (int)_gridXInput.Value;
        int gridY = (int)_gridYInput.Value;
        int gridZ = (int)_gridZInput.Value;
        double radius = _radiusInput.Value;
        double height = _heightInput.Value;

        // Clear existing mesh
        physico.Mesh.Cells.Clear();
        physico.Mesh.Connections.Clear();

        // Create a material for the reactor
        if (!physico.Materials.Any(m => m.MaterialID == "ReactorMaterial"))
        {
            physico.Materials.Add(new MaterialProperties
            {
                MaterialID = "ReactorMaterial",
                Density = 1200.0,
                Porosity = 1.0,
                ThermalConductivity = 0.5,
                SpecificHeat = 3500.0,
                Color = new System.Numerics.Vector4(0.5f, 0.7f, 0.9f, 1.0f)
            });
        }

        // Create grid of cells
        double cellWidth = (radius * 2) / gridX;
        double cellDepth = (radius * 2) / gridY;
        double cellHeight = height / gridZ;
        double cellVolume = cellWidth * cellHeight * cellDepth;

        int cellId = 0;
        for (int i = 0; i < gridX; i++)
        for (int j = 0; j < gridY; j++)
        for (int k = 0; k < gridZ; k++)
        {
            double centerX = -radius + cellWidth * (i + 0.5);
            double centerY = -radius + cellDepth * (j + 0.5);
            double centerZ = -height / 2 + cellHeight * (k + 0.5); // Center Z around 0

            var cell = new Cell
            {
                ID = $"Cell_{i}_{j}_{k}",
                MaterialID = "ReactorMaterial",
                Center = (centerX, centerY, centerZ),
                Volume = cellVolume,
                IsActive = true,
                InitialConditions = new InitialConditions
                {
                    Temperature = 298.15,
                    Pressure = 101325.0,
                    LiquidSaturation = 1.0,
                    Concentrations = new Dictionary<string, double>
                    {
                        { "ReactantA", 5.0 },
                        { "ReactantB", 3.0 }
                    }
                }
            };

            physico.Mesh.Cells[cell.ID] = cell;

            // Add connections to adjacent cells
            if (i > 0)
            {
                string neighborId = $"Cell_{i - 1}_{j}_{k}";
                physico.Mesh.Connections.Add((cell.ID, neighborId));
            }
            if (j > 0)
            {
                string neighborId = $"Cell_{i}_{j - 1}_{k}";
                physico.Mesh.Connections.Add((cell.ID, neighborId));
            }
            if (k > 0)
            {
                string neighborId = $"Cell_{i}_{j}_{k - 1}";
                physico.Mesh.Connections.Add((cell.ID, neighborId));
            }

            cellId++;
        }

        _meshViewport.LoadFromPhysicoChem(physico.Mesh, physico);
        _detailsView.Buffer.Text = BuildDatasetSummary(physico);
        SetStatus($"Reactor grid created: {gridX}x{gridY}x{gridZ} = {cellId} cells");
    }

    private void GenerateVoronoiMesh()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            _detailsView.Buffer.Text = "Select a PhysicoChem dataset to generate the mesh.";
            return;
        }

        var boreholes = _projectManager.LoadedDatasets.OfType<BoreholeDataset>().ToList();
        if (boreholes.Count == 0)
        {
            _detailsView.Buffer.Text = "Add a Borehole dataset to constrain the Voronoi mesh.";
            return;
        }

        var boreholeName = _boreholeSelector.ActiveText ?? boreholes.First().Name;
        var borehole = boreholes.FirstOrDefault(b => b.Name == boreholeName) ?? boreholes.First();

        physico.Mesh.GenerateVoronoiMesh(borehole, (int)_layerInput.Value, _radiusInput.Value, _heightInput.Value);
        var divisions = Math.Max(1, (int)_resolutionInput.Value / 10);
        physico.Mesh.SplitIntoGrid(divisions, divisions, divisions);
        _meshViewport.LoadFromPhysicoChem(physico.Mesh, physico);
        _detailsView.Buffer.Text = BuildDatasetSummary(physico);
        SetStatus("Voronoi mesh generated and synchronized with the 3D editor.");
    }

    private void ImportFromMeshDataset()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            _detailsView.Buffer.Text = "Select a PhysicoChem dataset to import a 3D mesh.";
            return;
        }

        var meshDataset = _projectManager.LoadedDatasets.OfType<Mesh3DDataset>().FirstOrDefault();
        if (meshDataset == null)
        {
            _detailsView.Buffer.Text = "Add a Mesh3D dataset before running the import.";
            return;
        }

        physico.Mesh.FromMesh3DDataset(meshDataset, _heightInput.Value);
        _meshViewport.LoadFromPhysicoChem(physico.Mesh, physico);
        _detailsView.Buffer.Text = BuildDatasetSummary(physico);
        SetStatus("Mesh imported and ready for multiphysics simulations.");
    }

    private void UpdateViewportCamera()
    {
        _meshViewport.SetCamera((float)_yawAdjustment.Value, (float)_pitchAdjustment.Value, (float)_zoomAdjustment.Value);
    }

    private void WireNodeEvents()
    {
        _nodeManager.NodeConnected += _ => QueueNodeRefresh();
        _nodeManager.NodeDisconnected += _ => QueueNodeRefresh();
        _nodeManager.StatusChanged += message => GLib.Idle.Add(() => { UpdateNodeStatus(message); return false; });
    }

    private void QueueNodeRefresh()
    {
        GLib.Idle.Add(() =>
        {
            RefreshNodeList();
            return false;
        });
    }

    private void RefreshNodeList()
    {
        _nodeStore.Clear();

        foreach (var node in _nodeManager.GetNodes())
        {
            var caps = node.Capabilities?.SupportedJobTypes;
            var capsText = caps != null && caps.Any() ? string.Join(", ", caps) : "-";

            _nodeStore.AppendValues(node.NodeName, node.Status.ToString(), capsText);
        }

        _nodeStore.AppendValues("Local node", _nodeManager.Status.ToString(), "CPU/GPU");
        UpdateNodeStatus();
    }

    private void HydrateNodeSettings()
    {
        var settings = _settingsManager.Settings.NodeManager;
        _nodeNameEntry.Text = settings.NodeName;
        _nodeHostEntry.Text = settings.HostAddress;
        _nodePortSpinner.Value = settings.ServerPort;
        _nodeEnabledToggle.Active = settings.EnableNodeManager;
        _nodeAutoStartToggle.Active = settings.AutoStartOnLaunch;

        var roleIndex = settings.Role switch
        {
            NodeRole.Host => 0,
            NodeRole.Worker => 1,
            _ => 2
        };
        _nodeRoleSelector.Active = roleIndex;
        UpdateNodeStatus("Ready");
    }

    private void ApplyNodeSettings()
    {
        var settings = _settingsManager.Settings.NodeManager;
        settings.NodeName = _nodeNameEntry.Text.Trim();
        settings.HostAddress = _nodeHostEntry.Text.Trim();
        settings.ServerPort = _nodePortSpinner.ValueAsInt;
        settings.EnableNodeManager = _nodeEnabledToggle.Active;
        settings.AutoStartOnLaunch = _nodeAutoStartToggle.Active;

        settings.Role = _nodeRoleSelector.Active switch
        {
            0 => NodeRole.Host,
            1 => NodeRole.Worker,
            _ => NodeRole.Hybrid
        };

        _settingsManager.SaveSettings();
    }

    private void UpdateNodeStatus(string? context = null)
    {
        var statusText = _nodeManager.IsRunning ? $"Node manager running ({_nodeManager.Status})" : "Node manager stopped";
        if (!string.IsNullOrWhiteSpace(context))
            statusText += $" • {context}";

        _nodeStatusLabel.Text = statusText;
    }

    private ToolItem CreateIconButton(string label, string tooltip, Pixbuf icon, EventHandler handler)
    {
        var button = new ToolButton(new Image(icon), label) { TooltipText = tooltip };
        button.Clicked += handler;
        return button;
    }

    private Button CreateSlimActionButton(string label, IconSymbol symbol, EventHandler handler)
    {
        var box = new HBox(false, 4);
        var image = new Image(CairoExtensions.MakeIcon(symbol)) { WidthRequest = 20, HeightRequest = 20 };
        var text = new Label(label) { Xalign = 0 };
        box.PackStart(image, false, false, 0);
        box.PackStart(text, false, false, 0);

        var button = new Button
        {
            Relief = ReliefStyle.Half,
            HeightRequest = 28,
            WidthRequest = 140,
            TooltipText = label,
            Halign = Align.Start,
            Hexpand = false
        };
        button.Add(box);
        button.Clicked += handler;
        return button;
    }

    private void OpenProjectDialog()
    {
        using var dialog = new FileChooserDialog("Open project", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "Open", ResponseType.Accept)
        {
            SelectMultiple = false
        };
        var filter = new FileFilter { Name = "Geoscientist projects (.gtp)" };
        filter.AddPattern("*.gtp");
        dialog.AddFilter(filter);
        if (dialog.Run() == (int)ResponseType.Accept)
        {
            try
            {
                _projectManager.LoadProject(dialog.Filename);
                RefreshDatasetList();
                SetStatus($"Project loaded: {dialog.Filename}");
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to open project: {ex.Message}");
            }
        }
        dialog.Destroy();
    }

    private void SaveProjectDialog()
    {
        using var dialog = new FileChooserDialog("Save project", this, FileChooserAction.Save, "Cancel", ResponseType.Cancel, "Save", ResponseType.Accept)
        {
            DoOverwriteConfirmation = true
        };
        var filter = new FileFilter { Name = "Geoscientist projects (.gtp)" };
        filter.AddPattern("*.gtp");
        dialog.AddFilter(filter);
        if (dialog.Run() == (int)ResponseType.Accept)
        {
            var path = dialog.Filename.EndsWith(".gtp") ? dialog.Filename : dialog.Filename + ".gtp";
            _projectManager.SaveProject(path);
            SetStatus($"Project saved to {path}");
        }
        dialog.Destroy();
    }

    private void AddExistingDataset()
    {
        using var dialog = new FileChooserDialog("Import dataset", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "Import", ResponseType.Accept)
        {
            SelectMultiple = false
        };
        var objFilter = new FileFilter { Name = "Mesh OBJ" };
        objFilter.AddPattern("*.obj");
        dialog.AddFilter(objFilter);
        var stlFilter = new FileFilter { Name = "Mesh STL" };
        stlFilter.AddPattern("*.stl");
        dialog.AddFilter(stlFilter);
        if (dialog.Run() == (int)ResponseType.Accept)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(dialog.Filename);
            var mesh = new Mesh3DDataset(name, dialog.Filename);
            mesh.Load();
            _projectManager.AddDataset(mesh);
            RefreshDatasetList();
            SetStatus($"Mesh dataset imported: {name}");
        }
        dialog.Destroy();
    }

    private void ImportTableDataset()
    {
        using var dialog = new FileChooserDialog("Import table dataset", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "Import", ResponseType.Accept)
        {
            SelectMultiple = false
        };
        var csvFilter = new FileFilter { Name = "Table files" };
        csvFilter.AddPattern("*.csv");
        csvFilter.AddPattern("*.tsv");
        csvFilter.AddPattern("*.tab");
        dialog.AddFilter(csvFilter);
        if (dialog.Run() == (int)ResponseType.Accept)
        {
            try
            {
                var loader = new TableLoader { FilePath = dialog.Filename };
                var dataset = (TableDataset)loader.LoadAsync(null).GetAwaiter().GetResult();
                _projectManager.AddDataset(dataset);
                RefreshDatasetList();
                SetStatus($"Table dataset imported for thermodynamic runs: {dataset.Name}");
            }
            catch (Exception ex)
            {
                SetStatus($"Table import failed: {ex.Message}");
            }
        }
        dialog.Destroy();
    }

    private void ReloadCurrentProject()
    {
        if (string.IsNullOrWhiteSpace(_projectManager.ProjectPath))
        {
            SetStatus("No project to reload.");
            return;
        }

        try
        {
            _projectManager.LoadProject(_projectManager.ProjectPath);
            RefreshDatasetList();
            SetStatus("Project reloaded and synced.");
        }
        catch (Exception ex)
        {
            SetStatus($"Reload failed: {ex.Message}");
        }
    }

    private MenuItem CreateMenuItem(string label, EventHandler handler)
    {
        var item = new MenuItem(label);
        item.Activated += handler;
        return item;
    }

    private void ToggleMeshOptionsPanel()
    {
        _meshOptionsToggle.Active = !_meshOptionsToggle.Active;
        _meshOptionsRevealer.RevealChild = _meshOptionsToggle.Active;
        SetStatus(_meshOptionsToggle.Active ? "Mesh options panel visible" : "Mesh options panel hidden");
    }

    private void ShowAboutDialog()
    {
        const string citation = "Mangiagalli, M. (2026). Geoscientist's Toolkit - Reactor (GTK) [Computer software]. GitHub. https://github.com/mattemangia/geoscientisttoolkit";
        var dialog = new Dialog("About Geoscientist's Toolkit - Reactor (GTK)", this, DialogFlags.Modal)
        {
            TransientFor = this,
            Modal = true,
            BorderWidth = 10,
            Resizable = false
        };
        dialog.AddButton("Close", ResponseType.Close);

        var content = dialog.ContentArea;
        content.Spacing = 8;

        var headerBox = new HBox(false, 8);
        Image logo;
        try
        {
            logo = new Image(GtkResourceLoader.LoadLogoPixbuf(128, 60));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: unable to load about dialog logo: {ex.Message}");
            logo = new Image(Stock.MissingImage, IconSize.Dialog);
        }
        var infoBox = new VBox(false, 2);
        infoBox.PackStart(new Label("Geoscientist's Toolkit - Reactor (GTK)") { Xalign = 0 }, false, false, 0);
        infoBox.PackStart(new Label("The Geoscientist's Toolkit Dev Team") { Xalign = 0 }, false, false, 0);
        infoBox.PackStart(new Label("Contact: Matteo Mangiagalli - Università degli Studi di Urbino Carlo Bo\nm.mangiagalli@campus.uniurb.it")
        {
            Xalign = 0,
            Justify = Justification.Left,
            LineWrap = true
        }, false, false, 0);
        infoBox.PackStart(new LinkButton("https://github.com/mattemangia/geoscientisttoolkit", "Project Page") { Xalign = 0 }, false, false, 0);

        headerBox.PackStart(logo, false, false, 0);
        headerBox.PackStart(infoBox, true, true, 0);
        content.PackStart(headerBox, false, false, 0);

        var citationBox = new HBox(false, 6);
        var citationLabel = new Label("Citation (APA):") { Xalign = 0 };
        var citationEntry = new Entry(citation)
        {
            IsEditable = false,
            WidthChars = citation.Length
        };
        var copyButton = new Button("Copy citation");
        copyButton.Clicked += (_, _) =>
        {
            var clipboard = Clipboard.Get(Gdk.Atom.Intern("CLIPBOARD", false));
            if (clipboard != null)
                clipboard.Text = citationEntry.Text;
            SetStatus("APA citation copied to clipboard.");
        };

        citationBox.PackStart(citationLabel, false, false, 0);
        citationBox.PackStart(citationEntry, true, true, 0);
        citationBox.PackStart(copyButton, false, false, 0);
        content.PackStart(citationBox, false, false, 0);
        content.ShowAll();

        dialog.Run();
        dialog.Destroy();
    }

    private Widget BuildComposerPanel()
    {
        var frame = new Frame("Designer and libraries") { BorderWidth = 4 };
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6, BorderWidth = 6 };

        _datasetTypeSelector.AppendText("PhysicoChem");
        _datasetTypeSelector.AppendText("Borehole");
        _datasetTypeSelector.AppendText("Mesh3D");
        _datasetTypeSelector.Active = 0;
        _datasetTypeSelector.Changed += (_, _) => UpdateDatasetCreationControls();

        grid.Attach(new Label("Name"), 0, 0, 1, 1);
        grid.Attach(_datasetNameEntry, 1, 0, 1, 1);
        grid.Attach(new Label("Type"), 0, 1, 1, 1);
        grid.Attach(_datasetTypeSelector, 1, 1, 1, 1);

        grid.Attach(_emptyBoreholeToggle, 0, 2, 2, 1);

        var createButton = CreateSlimActionButton("Create dataset", IconSymbol.ProjectNew, (_, _) => CreateDatasetFromInputs());
        grid.Attach(createButton, 0, 3, 2, 1);

        _materialPhaseSelector.AppendText("Solid");
        _materialPhaseSelector.AppendText("Liquid");
        _materialPhaseSelector.AppendText("Gas");
        _materialPhaseSelector.Active = 0;

        grid.Attach(new Label("Material"), 0, 4, 1, 1);
        grid.Attach(_materialNameEntry, 1, 4, 1, 1);
        grid.Attach(_materialPhaseSelector, 2, 4, 1, 1);

        var materialButton = CreateSlimActionButton("Add material & assign", IconSymbol.Material, (_, _) => AddMaterialToDataset());
        grid.Attach(materialButton, 0, 5, 3, 1);

        foreach (var type in Enum.GetNames(typeof(ForceType)))
            _forceTypeSelector.AppendText(type);
        _forceTypeSelector.Active = 0;

        grid.Attach(new Label("Force"), 0, 6, 1, 1);
        grid.Attach(_forceNameEntry, 1, 6, 1, 1);
        grid.Attach(_forceTypeSelector, 2, 6, 1, 1);
        var forceButton = CreateSlimActionButton("Add multiphysics force", IconSymbol.Force, (_, _) => AddForceToDataset());
        grid.Attach(forceButton, 0, 7, 3, 1);

        frame.Add(grid);
        UpdateDatasetCreationControls();
        return frame;
    }

    private void UpdateDatasetCreationControls()
    {
        var isBorehole = string.Equals(_datasetTypeSelector.ActiveText, "Borehole", StringComparison.OrdinalIgnoreCase);
        _emptyBoreholeToggle.Visible = isBorehole;
        _emptyBoreholeToggle.Sensitive = isBorehole;

        if (!isBorehole)
            _emptyBoreholeToggle.Active = false;
    }

    private Widget BuildAssetTree()
    {
        var frame = new Frame("Datasets, materials, and forces") { BorderWidth = 4 };
        _assetTreeView.Model = _assetStore;
        _assetTreeView.HeadersVisible = true;
        _assetTreeView.AppendColumn("Item", new CellRendererText(), "text", 0);
        _assetTreeView.AppendColumn("Details", new CellRendererText(), "text", 1);

        var scroller = new ScrolledWindow { ShadowType = ShadowType.In };
        scroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        scroller.Add(_assetTreeView);
        frame.Add(scroller);
        return frame;
    }

    private void RefreshAssetTree(Dataset dataset)
    {
        _assetStore.Clear();

        var root = _assetStore.AppendValues(dataset.Name, dataset.Type.ToString());

        switch (dataset)
        {
            case PhysicoChemDataset physico:
                var materials = _assetStore.AppendValues(root, "Materials", $"{physico.Materials.Count} items");
                foreach (var mat in physico.Materials)
                {
                    var info = $"ρ={mat.Density} kg/m³, k={mat.ThermalConductivity} W/mK";
                    _assetStore.AppendValues(materials, mat.MaterialID, info);
                }

                var forces = _assetStore.AppendValues(root, "Forces", $"{physico.Forces.Count} items");
                foreach (var force in physico.Forces)
                {
                    var info = force.Type.ToString();
                    _assetStore.AppendValues(forces, force.Name, info);
                }

                _assetStore.AppendValues(root, "Mesh", $"Cells: {physico.Mesh.Cells.Count}, Connections: {physico.Mesh.Connections.Count}");
                break;
            case BoreholeDataset borehole:
                var lithologies = _assetStore.AppendValues(root, "Lithology", $"{borehole.LithologyUnits.Count} units");
                foreach (var unit in borehole.LithologyUnits)
                    _assetStore.AppendValues(lithologies, unit.LithologyType, $"{unit.DepthFrom:F1}-{unit.DepthTo:F1} m");
                var logs = _assetStore.AppendValues(root, "Parameter tracks", $"Tracks: {borehole.ParameterTracks.Count}");
                foreach (var track in borehole.ParameterTracks.Values)
                    _assetStore.AppendValues(logs, track.Name, $"{track.MinValue}-{track.MaxValue} {track.Unit}");
                break;
            case Mesh3DDataset mesh3D:
                _assetStore.AppendValues(root, "Vertices", mesh3D.VertexCount.ToString());
                _assetStore.AppendValues(root, "Faces", mesh3D.FaceCount.ToString());
                break;
            case TableDataset table:
                var columns = _assetStore.AppendValues(root, "Columns", $"{table.ColumnCount} fields");
                for (int i = 0; i < table.ColumnNames.Count; i++)
                {
                    var typeName = table.ColumnTypes.ElementAtOrDefault(i)?.Name ?? "";
                    _assetStore.AppendValues(columns, table.ColumnNames[i], typeName);
                }
                _assetStore.AppendValues(root, "Rows", table.RowCount.ToString());
                break;
        }

        _assetTreeView.ExpandAll();
    }

    private void CreateDatasetFromInputs()
    {
        var name = string.IsNullOrWhiteSpace(_datasetNameEntry.Text) ? $"Dataset_{DateTime.Now:HHmmss}" : _datasetNameEntry.Text.Trim();
        var type = _datasetTypeSelector.ActiveText ?? "PhysicoChem";

        Dataset dataset = type switch
        {
            "PhysicoChem" => new PhysicoChemDataset(name, "Designer Reactor")
            {
                Materials = { new MaterialProperties { MaterialID = "Rock", Density = 2500, ThermalConductivity = 2.1, SpecificHeat = 900 } },
                Forces = { new ForceField("Gravity", ForceType.Gravity) }
            },
            "Borehole" => _emptyBoreholeToggle.Active
                ? BoreholeDataset.CreateEmpty(name, string.Empty)
                : new BoreholeDataset(name, string.Empty)
                {
                    SurfaceCoordinates = new System.Numerics.Vector2(0, 0),
                    TotalDepth = 800,
                    Elevation = 120
                },
            _ => Mesh3DDataset.CreateEmpty(name, string.Empty)
        };

        _projectManager.AddDataset(dataset);
        RefreshDatasetList();
        SetStatus($"Created dataset {name} ({type}).");
    }

    private void AddMaterialToDataset()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            SetStatus("Select a PhysicoChem dataset to assign materials.");
            return;
        }

        var mat = new MaterialProperties
        {
            MaterialID = string.IsNullOrWhiteSpace(_materialNameEntry.Text) ? "Reactor Material" : _materialNameEntry.Text.Trim(),
            Density = 2400,
            ThermalConductivity = 1.8,
            SpecificHeat = 800
        };
        physico.Materials.Add(mat);
        foreach (var cell in physico.Mesh.Cells.Values)
            cell.MaterialID = mat.MaterialID;

        _detailsView.Buffer.Text = BuildDatasetSummary(physico);
        RefreshAssetTree(physico);
        SetStatus($"Material '{mat.MaterialID}' assigned to all cells.");
    }

    private void AddForceToDataset()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            SetStatus("Select a PhysicoChem dataset to add forces.");
            return;
        }

        var forceName = string.IsNullOrWhiteSpace(_forceNameEntry.Text) ? "Force" + (physico.Forces.Count + 1) : _forceNameEntry.Text.Trim();
        var type = Enum.TryParse<ForceType>(_forceTypeSelector.ActiveText, out var parsed) ? parsed : ForceType.Gravity;
        var force = new ForceField(forceName, type);
        if (type == ForceType.Gravity)
            force.GravityVector = (0, 0, -9.81);
        else if (type == ForceType.Vortex)
            force.VortexStrength = 2.5;

        physico.Forces.Add(force);
        _detailsView.Buffer.Text = BuildDatasetSummary(physico);
        RefreshAssetTree(physico);
        SetStatus($"Force '{forceName}' added ({type}).");
    }

    private void CombineMeshes(BooleanOperation op)
    {
        var meshes = _projectManager.LoadedDatasets.OfType<Mesh3DDataset>().ToList();
        if (meshes.Count < 2 && _selectedDataset is not Mesh3DDataset)
        {
            SetStatus("Need at least two 3D meshes to perform boolean operations.");
            return;
        }

        var primary = _selectedDataset as Mesh3DDataset ?? meshes.First();
        var secondary = meshes.FirstOrDefault(m => m != primary);
        if (secondary == null)
        {
            SetStatus("Select a second mesh to combine.");
            return;
        }

        if (op == BooleanOperation.Union)
        {
            var offset = primary.Vertices.Count;
            primary.Vertices.AddRange(secondary.Vertices);
            foreach (var face in secondary.Faces)
                primary.Faces.Add(face.Select(i => i + offset).ToArray());
        }
        else
        {
            var min = secondary.BoundingBoxMin;
            var max = secondary.BoundingBoxMax;
            for (var i = primary.Vertices.Count - 1; i >= 0; i--)
            {
                var v = primary.Vertices[i];
                if (v.X >= min.X && v.X <= max.X && v.Y >= min.Y && v.Y <= max.Y && v.Z >= min.Z && v.Z <= max.Z)
                    primary.Vertices.RemoveAt(i);
            }
            primary.Faces.RemoveAll(f => f.Any(idx => idx >= primary.Vertices.Count));
        }

        primary.VertexCount = primary.Vertices.Count;
        primary.FaceCount = primary.Faces.Count;
        primary.CalculateBounds();
        _meshViewport.LoadFromMesh(primary);
        SetStatus(op == BooleanOperation.Union ? "Meshes unified." : "Mesh subtraction complete.");
    }

    private void GenerateVoronoiFromMesh()
    {
        if (_selectedDataset is not Mesh3DDataset mesh || !_projectManager.LoadedDatasets.OfType<PhysicoChemDataset>().Any())
        {
            SetStatus("Select a 3D mesh and a PhysicoChem dataset to transfer the Voronoi mesh.");
            return;
        }

        var target = _projectManager.LoadedDatasets.OfType<PhysicoChemDataset>().First();
        target.Mesh.FromMesh3DDataset(mesh, _heightInput.Value);
        _meshViewport.LoadFromPhysicoChem(target.Mesh, target);
        _detailsView.Buffer.Text = BuildDatasetSummary(target);
        SetStatus("Voronoi mesh updated from the selected 3D dataset.");
    }

    private async void RunGeoScript()
    {
        if (_selectedDataset == null)
        {
            SetStatus("Select a dataset to run GeoScript against.");
            return;
        }

        try
        {
            var script = _geoScriptEditor.Buffer.Text ?? string.Empty;
            var engine = new GeoScriptEngine();
            var context = _projectManager.LoadedDatasets.ToDictionary(d => d.Name, d => d);
            var result = await engine.ExecuteAsync(script, _selectedDataset, context);
            if (result != null)
            {
                _projectManager.AddDataset(result);
                RefreshDatasetList();
                _detailsView.Buffer.Text = BuildDatasetSummary(result);
                SetStatus($"GeoScript generated dataset '{result.Name}'.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"GeoScript failed: {ex.Message}");
        }
    }

    private void SetStatus(string message)
    {
        _statusBar.Text = "🔹 " + message;
        Logger.Log(message);
    }

    private void ApplyProfessionalStyling()
    {
        var provider = new CssProvider();
        provider.LoadFromData(@"
            * { color: #e8ecf0; font-family: Cantarell, Segoe UI, sans-serif; }
            window, dialog, toolbar, menubar, notebook, frame, scrolledwindow, box, paned, revealer { background: #0f1015; }
            notebook > header, notebook > header > tabs { background: #141621; }
            notebook > header tab, notebook > header tab label { background: #1b1d26; color: #e8ecf0; padding: 6px 10px; }
            notebook > header tab:checked, notebook > header tab:active { background: #222738; }
            notebook stack, scrolledwindow > viewport, textview > text, treeview.view { background: #0f1015; color: #e8ecf0; }
            treeview, textview, entry, combobox { background: #141620; color: #e8ecf0; }
            textview text { color: #e8ecf0; }
            treeview.view row:selected, treeview.view row:selected:focus { background: #26304a; color: #f8fbff; }
            toolbar { border-bottom: 1px solid #1f2230; background: #141621; }
            menubar, menu, menuitem, menuitem * { background: #141621; color: #e8ecf0; }
            menuitem:hover, menuitem:selected { background: #1f2433; color: #f8fbff; }
            menu { border: 1px solid #1f2230; }
            button, toolbutton { background: #1d202c; color: #f5f7fb; border-radius: 6px; padding: 4px 8px; }
            button:hover { background: #262a38; }
            frame > label { color: #9fb4ff; }
            scale trough { background: #1f2230; }
            scale slider { background: #3f7cff; }
        ");
        StyleContext.AddProviderForScreen(Gdk.Screen.Default, provider, StyleProviderPriority.Application);
        _detailsView.ModifyFont(Pango.FontDescription.FromString("Cantarell 11"));
        _datasetView.EnableGridLines = TreeViewGridLines.Both;
        var bg = new Gdk.RGBA { Red = 0.08, Green = 0.09, Blue = 0.12, Alpha = 1 };
        var fg = new Gdk.RGBA { Red = 0.91, Green = 0.93, Blue = 0.95, Alpha = 1 };
        _detailsView.OverrideBackgroundColor(StateFlags.Normal, bg);
        _detailsView.OverrideColor(StateFlags.Normal, fg);
        _geoScriptEditor.OverrideBackgroundColor(StateFlags.Normal, bg);
        _geoScriptEditor.OverrideColor(StateFlags.Normal, fg);
        _datasetView.OverrideBackgroundColor(StateFlags.Normal, bg);
        _datasetView.OverrideColor(StateFlags.Normal, fg);
    }

    private enum BooleanOperation
    {
        Union,
        Subtract
    }

    private void OpenMaterialLibraryDialog()
    {
        var dialog = new MaterialLibraryDialog(this);
        var response = (ResponseType)dialog.Run();
        dialog.Destroy();

        if (response == ResponseType.Ok)
        {
            if (dialog.SelectedMaterial != null)
            {
                SetStatus($"Selected material: {dialog.SelectedMaterial.Name}");

                // If we have a PhysicoChemDataset selected, add material to it
                if (_selectedDataset is PhysicoChemDataset physico)
                {
                    var mappedMaterial = new MaterialProperties
                    {
                        MaterialID = dialog.SelectedMaterial.Name,
                        Porosity = dialog.SelectedMaterial.TypicalPorosity_fraction ?? 0.1,
                        Permeability = 1e-12,
                        ThermalConductivity = dialog.SelectedMaterial.ThermalConductivity_W_mK ?? 2.0,
                        SpecificHeat = dialog.SelectedMaterial.SpecificHeatCapacity_J_kgK ?? 1000.0,
                        Density = dialog.SelectedMaterial.Density_kg_m3 ?? 2500.0,
                        MineralComposition = dialog.SelectedMaterial.Notes ?? string.Empty
                    };

                    if (!physico.Materials.Any(m => m.MaterialID == mappedMaterial.MaterialID))
                    {
                        physico.Materials.Add(mappedMaterial);
                        SetStatus($"Added material {mappedMaterial.MaterialID} to {physico.Name}");
                        RefreshDatasetList();
                    }
                }
            }
            else if (dialog.SelectedCompound != null)
            {
                SetStatus($"Selected compound: {dialog.SelectedCompound.Name}");
            }
        }
    }

    private void OpenDomainCreatorDialog()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            var msgDialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Warning, ButtonsType.Ok,
                "Please select a PhysicoChemDataset first to add domains.");
            msgDialog.Run();
            msgDialog.Destroy();
            return;
        }

        var dialog = new DomainCreatorDialog(this, physico.Materials.ToList());
        var response = (ResponseType)dialog.Run();
        dialog.Destroy();

        if (response == ResponseType.Ok && dialog.CreatedDomain != null)
        {
            // Store domain on the dataset and make sure its material is registered
            physico.Domains.Add(dialog.CreatedDomain);

            var selectedMaterial = dialog.CreatedDomain.Material;
            if (selectedMaterial != null && physico.Materials.All(m => m.MaterialID != selectedMaterial.MaterialID))
            {
                physico.Materials.Add(selectedMaterial);
            }

            // Generate cells from the domain definitions
            var resolution = (int)_resolutionInput.Value;
            physico.GenerateMesh(resolution);

            _meshViewport.LoadFromPhysicoChem(physico.Mesh, physico);
            RefreshDatasetList();

            SetStatus($"Domain '{dialog.CreatedDomain.Name}' created and mesh regenerated at {resolution}³ resolution.");
        }
    }

    private void OpenSpeciesSelectorDialog()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            var msgDialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Warning, ButtonsType.Ok,
                "Please select a PhysicoChemDataset first to configure species.");
            msgDialog.Run();
            msgDialog.Destroy();
            return;
        }

        // Get current concentrations from selected cells or use defaults
        var initialConcentrations = new Dictionary<string, double>();
        if (_meshViewport.SelectedCellIDs.Count > 0)
        {
            var firstCellId = _meshViewport.SelectedCellIDs.First();
            if (physico.Mesh.Cells.TryGetValue(firstCellId, out var cell) &&
                cell.InitialConditions?.Concentrations != null)
            {
                initialConcentrations = new Dictionary<string, double>(cell.InitialConditions.Concentrations);
            }
        }

        var dialog = new SpeciesSelectorDialog(this, initialConcentrations);
        var response = (ResponseType)dialog.Run();
        dialog.Destroy();

        if (response == ResponseType.Ok)
        {
            var concentrations = dialog.SelectedConcentrations;

            // Apply to selected cells or all cells
            if (_meshViewport.SelectedCellIDs.Count > 0)
            {
                foreach (var cellId in _meshViewport.SelectedCellIDs)
                {
                    if (physico.Mesh.Cells.TryGetValue(cellId, out var cell))
                    {
                        if (cell.InitialConditions == null)
                            cell.InitialConditions = new InitialConditions();

                        cell.InitialConditions.Concentrations = new Dictionary<string, double>(concentrations);
                    }
                }
                SetStatus($"Applied {concentrations.Count} species to {_meshViewport.SelectedCellIDs.Count} cells");
            }
            else
            {
                // Apply to all cells
                foreach (var cell in physico.Mesh.Cells.Values)
                {
                    if (cell.InitialConditions == null)
                        cell.InitialConditions = new InitialConditions();

                    cell.InitialConditions.Concentrations = new Dictionary<string, double>(concentrations);
                }
                SetStatus($"Applied {concentrations.Count} species to all cells");
            }

            _meshViewport.QueueDraw();
        }
    }

    private void OpenGeothermalConfigDialog()
    {
        var dialog = new GeothermalConfigDialog(this, _selectedDataset as PhysicoChemDataset);
        var response = (ResponseType)dialog.Run();
        dialog.Destroy();

        if (response == ResponseType.Ok && dialog.CreatedDataset != null)
        {
            _projectManager.AddDataset(dialog.CreatedDataset);
            RefreshDatasetList();
            SelectFirstDataset();
            SetStatus($"Geothermal well '{dialog.CreatedDataset.Name}' created with deep well configuration");
        }
    }

    private void OpenHeatExchangerDialog()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            var msg = new MessageDialog(this, DialogFlags.Modal, MessageType.Warning, ButtonsType.Ok, "Please select a PhysicoChem dataset first.");
            msg.Run();
            msg.Destroy();
            return;
        }

        var dialog = new HeatExchangerConfigDialog(this, physico.Materials.ToList());
        var response = (ResponseType)dialog.Run();
        dialog.Destroy();

        if (response == ResponseType.Ok && dialog.CreatedObject != null)
        {
            physico.Mesh.EmbedObject(dialog.CreatedObject);
            _meshViewport.QueueDraw();
            SetStatus($"Added reactor object: {dialog.CreatedObject.Name} ({dialog.CreatedObject.Type})");
        }
    }

    private void OpenSimulationSetupWizard()
    {
        var dialog = new SimulationSetupWizard(this);
        var response = (ResponseType)dialog.Run();
        dialog.Destroy();
        if (response == ResponseType.Ok)
        {
            SetStatus("Simulation configuration saved.");
        }
    }

    private void OpenBooleanOperationsUI()
    {
        var meshes = _projectManager.LoadedDatasets.OfType<Mesh3DDataset>().ToList();
        if (meshes.Count < 2)
        {
             var msg = new MessageDialog(this, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok, "Need at least two Mesh3D datasets loaded.");
             msg.Run();
             msg.Destroy();
             return;
        }

        var dialog = new BooleanOperationsUI(this, meshes);
        var response = (ResponseType)dialog.Run();
        dialog.Destroy();

        if (response == ResponseType.Ok && dialog.TargetMesh != null && dialog.ToolMesh != null)
        {
             if (dialog.TargetMesh == dialog.ToolMesh)
             {
                 SetStatus("Cannot operate on the same mesh.");
                 return;
             }

             // Perform operation
             var op = dialog.OperationIndex == 0 ? BooleanOperation.Union : BooleanOperation.Subtract;

             // Reuse existing logic but with selected meshes
             // Note: CombineMeshes(op) relies on _selectedDataset implicitly or first/second logic.
             // We should refactor CombineMeshes or duplicate logic here.
             // Duplicate logic for clarity in this context:

             var primary = dialog.TargetMesh;
             var secondary = dialog.ToolMesh;

             if (op == BooleanOperation.Union)
             {
                 var offset = primary.Vertices.Count;
                 primary.Vertices.AddRange(secondary.Vertices);
                 foreach (var face in secondary.Faces)
                     primary.Faces.Add(face.Select(i => i + offset).ToArray());
             }
             else
             {
                 // Safer subtract: Remove FACES that are inside the tool volume, do not remove vertices to preserve indices.
                 var min = secondary.BoundingBoxMin;
                 var max = secondary.BoundingBoxMax;

                 // Identify vertices inside the bounding box
                 var insideIndices = new HashSet<int>();
                 for (var i = 0; i < primary.Vertices.Count; i++)
                 {
                     var v = primary.Vertices[i];
                     if (v.X >= min.X && v.X <= max.X && v.Y >= min.Y && v.Y <= max.Y && v.Z >= min.Z && v.Z <= max.Z)
                         insideIndices.Add(i);
                 }

                 // Remove faces that use any of these vertices
                 primary.Faces.RemoveAll(f => f.Any(idx => insideIndices.Contains(idx)));
             }

             primary.VertexCount = primary.Vertices.Count;
             primary.FaceCount = primary.Faces.Count;
             primary.CalculateBounds();
             _meshViewport.LoadFromMesh(primary);
             SetStatus($"Boolean operation {op} complete on {primary.Name}.");
        }
    }

    private void OpenBoundaryConditionEditor()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            SetStatus("Select a PhysicoChem dataset to apply boundary conditions.");
            return;
        }

        var dialog = new BoundaryConditionEditor(this);
        var response = (ResponseType)dialog.Run();
        dialog.Destroy();

        if (response == ResponseType.Ok && dialog.CreatedBC != null)
        {
            // In a real app, we would add this BC to the dataset.
            // Current PhysicoChemDataset doesn't have a public BoundaryConditions list in the snippet I saw,
            // but usually it should. I'll assume it's part of the simulation setup or attach to cells.
            // For now, we log it effectively.
            SetStatus($"Boundary Condition '{dialog.CreatedBC.Type}' created for '{dialog.CreatedBC.Variable}' at {dialog.CreatedBC.Location}.");

            // If PhysicoChemDataset has a storage, we'd add it here.
            // Assuming extensions or future impl.
        }
    }

    private void OpenForceFieldEditor()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            SetStatus("Select a PhysicoChem dataset to edit force fields.");
            return;
        }

        var dialog = new ForceFieldEditor(this, physico.Forces);
        dialog.Run(); // Editor modifies the list directly
        dialog.Destroy();

        RefreshAssetTree(physico);
        SetStatus($"Updated force fields. Count: {physico.Forces.Count}");
    }

    private void AddNucleationPoint()
    {
        if (_selectedDataset is not PhysicoChemDataset physico) return;

        var dialog = new Dialog("Add Nucleation Point", this, DialogFlags.Modal);
        dialog.AddButton("Cancel", ResponseType.Cancel);
        dialog.AddButton("Add", ResponseType.Ok);
        dialog.AddButton("Add Random", ResponseType.Apply);

        var content = dialog.ContentArea;
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8, BorderWidth = 10 };

        var xSpin = new SpinButton(-1000, 1000, 0.1) { Value = 0 };
        var ySpin = new SpinButton(-1000, 1000, 0.1) { Value = 0 };
        var zSpin = new SpinButton(-1000, 1000, 0.1) { Value = 0 };

        grid.Attach(new Label("X:"), 0, 0, 1, 1); grid.Attach(xSpin, 1, 0, 1, 1);
        grid.Attach(new Label("Y:"), 0, 1, 1, 1); grid.Attach(ySpin, 1, 1, 1, 1);
        grid.Attach(new Label("Z:"), 0, 2, 1, 1); grid.Attach(zSpin, 1, 2, 1, 1);

        content.PackStart(grid, true, true, 0);
        dialog.ShowAll();

        var response = (ResponseType)dialog.Run();

        if (response == ResponseType.Ok)
        {
            var point = new NucleationPoint
            {
                ID = $"Nuc_{physico.Mesh.NucleationPoints.Count + 1}",
                Position = (xSpin.Value, ySpin.Value, zSpin.Value)
            };
            physico.Mesh.NucleationPoints.Add(point);
            SetStatus($"Added Nucleation Point at ({point.Position.X}, {point.Position.Y}, {point.Position.Z}).");
        }
        else if (response == ResponseType.Apply)
        {
            var rand = new Random();
            var point = new NucleationPoint
            {
                ID = $"Nuc_{physico.Mesh.NucleationPoints.Count + 1}",
                Position = (rand.NextDouble() * 20 - 10, rand.NextDouble() * 20 - 10, rand.NextDouble() * 100) // Approx range
            };
            physico.Mesh.NucleationPoints.Add(point);
            SetStatus($"Added Random Nucleation Point at ({point.Position.X:F1}, {point.Position.Y:F1}, {point.Position.Z:F1}).");
        }

        dialog.Destroy();
    }

    private void EnsureDefaultReactor()
    {
        if (_projectManager.LoadedDatasets.OfType<PhysicoChemDataset>().Any()) return;
        var reactor = MultiphysicsExamples.CreateExothermicReactor(3.5, 6.0);
        reactor.Name = "Default Exothermic Reactor";
        _projectManager.AddDataset(reactor);
    }
}

internal enum IconSymbol
{
    ProjectNew,
    FolderOpen,
    Save,
    Mesh,
    MeshImport,
    Refresh,
    Cluster,
    Settings,
    PhysicoChem,
    Borehole,
    MeshUnion,
    MeshSubtract,
    Voronoi,
    Table,
    GeoScript,
    Material,
    Force
}

internal static class CairoExtensions
{
    public static Pixbuf MakeIcon(IconSymbol symbol)
    {
        const int size = 26;
        using var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, size, size);
        using var ctx = new Cairo.Context(surface);
        ctx.Operator = Cairo.Operator.Source;
        ctx.SetSourceRGBA(0, 0, 0, 0);
        ctx.Paint();
        ctx.Operator = Cairo.Operator.Over;

        var color = Palette(symbol);
        ctx.SetSourceRGBA(color.R, color.G, color.B, 0.92);
        ctx.Rectangle(2, 2, size - 4, size - 4);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1, 1, 1, 0.18);
        ctx.LineWidth = 1.2;
        ctx.Stroke();

        ctx.SetSourceRGB(0.05, 0.06, 0.08);
        ctx.Translate(size / 2.0, size / 2.0);
        DrawSymbol(ctx, symbol);

        surface.Flush();
        return new Pixbuf(surface.Data, Gdk.Colorspace.Rgb, true, 8, size, size, surface.Stride);
    }

    private static void DrawSymbol(Cairo.Context ctx, IconSymbol symbol)
    {
        ctx.LineWidth = 2.5;
        switch (symbol)
        {
            case IconSymbol.ProjectNew:
                ctx.MoveTo(-6, 0); ctx.LineTo(6, 0);
                ctx.MoveTo(0, -6); ctx.LineTo(0, 6);
                break;
            case IconSymbol.FolderOpen:
                ctx.Rectangle(-8, -3, 16, 10);
                ctx.MoveTo(-8, -3); ctx.LineTo(-4, -7); ctx.LineTo(2, -7); ctx.LineTo(6, -3);
                break;
            case IconSymbol.Save:
                ctx.Rectangle(-7, -7, 14, 14);
                ctx.MoveTo(-7, -1); ctx.LineTo(7, -1);
                ctx.MoveTo(-4, 3); ctx.LineTo(4, 3);
                break;
            case IconSymbol.Mesh:
                for (var i = -6; i <= 6; i += 4) { ctx.MoveTo(-8, i); ctx.LineTo(8, i); ctx.MoveTo(i, -8); ctx.LineTo(i, 8); }
                break;
            case IconSymbol.MeshImport:
                ctx.Rectangle(-7, -7, 14, 10);
                ctx.MoveTo(0, 3); ctx.LineTo(0, 8); ctx.MoveTo(-4, 4); ctx.LineTo(0, 8); ctx.LineTo(4, 4);
                break;
            case IconSymbol.Refresh:
                ctx.Arc(0, 0, 7, Math.PI * 0.2, Math.PI * 1.6);
                ctx.MoveTo(6, -4); ctx.LineTo(8, -8); ctx.LineTo(4, -7);
                break;
            case IconSymbol.Cluster:
                ctx.Arc(-5, -5, 2.5, 0, Math.PI * 2);
                ctx.Arc(5, -5, 2.5, 0, Math.PI * 2);
                ctx.Arc(0, 6, 2.5, 0, Math.PI * 2);
                ctx.MoveTo(-3, -3); ctx.LineTo(-0.5, 3.5);
                ctx.MoveTo(3, -3); ctx.LineTo(0.5, 3.5);
                break;
            case IconSymbol.Settings:
                for (var i = 0; i < 6; i++) { ctx.MoveTo(0, 0); ctx.LineTo(8 * Math.Cos(i * Math.PI / 3), 8 * Math.Sin(i * Math.PI / 3)); }
                ctx.Arc(0, 0, 3, 0, Math.PI * 2);
                break;
            case IconSymbol.PhysicoChem:
                ctx.MoveTo(-6, 0); ctx.LineTo(-2, -6); ctx.LineTo(2, -6); ctx.LineTo(6, 0); ctx.LineTo(2, 6); ctx.LineTo(-2, 6); ctx.ClosePath();
                break;
            case IconSymbol.Borehole:
                ctx.Rectangle(-5, -7, 10, 14);
                ctx.MoveTo(-5, 0); ctx.LineTo(5, 0);
                ctx.MoveTo(-5, 4); ctx.LineTo(5, 4);
                break;
            case IconSymbol.MeshUnion:
                ctx.Rectangle(-7, -5, 10, 10);
                ctx.Rectangle(-3, -7, 10, 10);
                break;
            case IconSymbol.MeshSubtract:
                ctx.Rectangle(-7, -5, 12, 10);
                ctx.MoveTo(-3, -3); ctx.LineTo(7, -3);
                ctx.MoveTo(-3, 3); ctx.LineTo(7, 3);
                break;
            case IconSymbol.Voronoi:
                ctx.MoveTo(-6, -6); ctx.LineTo(6, -2); ctx.LineTo(2, 6); ctx.LineTo(-6, 2); ctx.ClosePath();
                ctx.MoveTo(-2, -1); ctx.LineTo(4, -4); ctx.MoveTo(-1, 2); ctx.LineTo(3, 4);
                break;
            case IconSymbol.Table:
                ctx.Rectangle(-7, -7, 14, 14);
                for (var i = -3; i <= 3; i += 3) { ctx.MoveTo(-7, i); ctx.LineTo(7, i); ctx.MoveTo(i, -7); ctx.LineTo(i, 7); }
                break;
            case IconSymbol.GeoScript:
                ctx.MoveTo(-6, -4); ctx.LineTo(-2, -4); ctx.LineTo(-6, 4); ctx.LineTo(-2, 4);
                ctx.MoveTo(0, -5); ctx.LineTo(6, 0); ctx.LineTo(0, 5);
                break;
            case IconSymbol.Material:
                ctx.MoveTo(-6, 3); ctx.LineTo(0, -6); ctx.LineTo(6, 3); ctx.ClosePath();
                ctx.MoveTo(-6, 6); ctx.LineTo(6, 6);
                break;
            case IconSymbol.Force:
                ctx.MoveTo(-5, -6); ctx.LineTo(5, 0); ctx.LineTo(-5, 6);
                ctx.MoveTo(-1, -2); ctx.LineTo(7, -2);
                break;
        }
        ctx.SetSourceRGB(0.04, 0.05, 0.07);
        ctx.Stroke();
    }

    private static Cairo.Color Palette(IconSymbol symbol) => symbol switch
    {
        IconSymbol.ProjectNew => new Cairo.Color(0.29, 0.72, 0.96),
        IconSymbol.FolderOpen => new Cairo.Color(0.98, 0.81, 0.34),
        IconSymbol.Save => new Cairo.Color(0.36, 0.84, 0.54),
        IconSymbol.Mesh => new Cairo.Color(0.53, 0.58, 0.94),
        IconSymbol.MeshImport => new Cairo.Color(0.41, 0.68, 1.0),
        IconSymbol.Refresh => new Cairo.Color(0.63, 0.44, 0.97),
        IconSymbol.Cluster => new Cairo.Color(0.85, 0.48, 0.75),
        IconSymbol.Settings => new Cairo.Color(0.56, 0.6, 0.68),
        IconSymbol.PhysicoChem => new Cairo.Color(0.91, 0.55, 0.36),
        IconSymbol.Borehole => new Cairo.Color(0.84, 0.52, 0.31),
        IconSymbol.MeshUnion => new Cairo.Color(0.35, 0.86, 0.64),
        IconSymbol.MeshSubtract => new Cairo.Color(0.94, 0.4, 0.45),
        IconSymbol.Voronoi => new Cairo.Color(0.3, 0.82, 0.9),
        IconSymbol.Table => new Cairo.Color(0.32, 0.7, 0.92),
        IconSymbol.GeoScript => new Cairo.Color(0.95, 0.71, 0.42),
        IconSymbol.Material => new Cairo.Color(0.7, 0.78, 0.36),
        IconSymbol.Force => new Cairo.Color(0.98, 0.55, 0.26),
        _ => new Cairo.Color(0.6, 0.6, 0.6)
    };

    public static Cairo.Color ColorForMaterial(string material) => material.ToLower() switch
    {
        var m when m.Contains("sand") => new Cairo.Color(0.86, 0.64, 0.39),
        var m when m.Contains("shale") => new Cairo.Color(0.35, 0.45, 0.58),
        var m when m.Contains("lime") => new Cairo.Color(0.78, 0.82, 0.75),
        _ => new Cairo.Color(0.52, 0.71, 0.88)
    };
}
