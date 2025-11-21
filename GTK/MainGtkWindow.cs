using System.IO;
using System.Linq;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;
using Cairo;
using Gtk;

using Pixbuf = Gdk.Pixbuf;

namespace GeoscientistToolkit.GtkUI;

public class MainGtkWindow : Window
{
    private readonly ProjectManager _projectManager;
    private readonly SettingsManager _settingsManager;
    private readonly NodeManager _nodeManager;

    private readonly ListStore _datasetStore = new(typeof(string), typeof(string), typeof(Dataset));
    private readonly TextView _detailsView = new() { Editable = false, WrapMode = WrapMode.Word };    
    private readonly MeshViewport3D _meshViewport = new();
    private readonly ComboBoxText _boreholeSelector = new();
    private readonly SpinButton _layerInput = new(1, 50, 1) { Value = 4 };
    private readonly SpinButton _radiusInput = new(1, 500, 1) { Value = 50 };
    private readonly SpinButton _heightInput = new(1, 1000, 1) { Value = 120 };
    private readonly SpinButton _resolutionInput = new(10, 500, 10) { Value = 100 };
    private readonly Adjustment _yawAdjustment = new(35, -180, 180, 1, 10, 0);
    private readonly Adjustment _pitchAdjustment = new(-20, -90, 90, 1, 10, 0);
    private readonly Adjustment _zoomAdjustment = new(1.2, 0.1, 8, 0.05, 0.1, 0);

    private readonly ListStore _nodeStore = new(typeof(string), typeof(string), typeof(string));
    private readonly Revealer _meshOptionsRevealer = new() { RevealChild = true, TransitionType = RevealerTransitionType.SlideRight, TransitionDuration = 250 };
    private readonly Entry _datasetNameEntry = new() { PlaceholderText = "Nome dataset" };
    private readonly ComboBoxText _datasetTypeSelector = new();
    private readonly Entry _materialNameEntry = new() { PlaceholderText = "Materiale" };
    private readonly ComboBoxText _materialPhaseSelector = new();
    private readonly Entry _forceNameEntry = new() { PlaceholderText = "Forza" };
    private readonly ComboBoxText _forceTypeSelector = new();
    private readonly Label _statusBar = new() { Xalign = 0 };

    private Dataset? _selectedDataset;

    public MainGtkWindow(ProjectManager projectManager, SettingsManager settingsManager, NodeManager nodeManager) : base("GeoscientistToolkit GTK Edition")
    {
        _projectManager = projectManager;
        _settingsManager = settingsManager;
        _nodeManager = nodeManager;

        SetDefaultSize(1400, 900);
        BorderWidth = 8;

        var root = new VBox(false, 6);
        root.PackStart(BuildToolbar(), false, false, 0);

        var split = new Paned(Orientation.Horizontal) { Position = 320 };
        split.Pack1(BuildDatasetPanel(), false, false);
        split.Pack2(BuildWorkspace(), true, false);

        root.PackStart(split, true, true, 0);
        root.PackStart(_statusBar, false, false, 4);
        Add(root);

        WireNodeEvents();
        RefreshDatasetList();
        RefreshNodeList();

        ApplyProfessionalStyling();
    }

    private Widget BuildToolbar()
    {
        var toolbar = new Toolbar { IconSize = IconSize.LargeToolbar, Style = ToolbarStyle.BothHoriz };

        toolbar.Insert(CreateIconButton("Nuovo", "Crea un nuovo progetto", CairoExtensions.MakeIcon(CairoExtensions.Accent), (_, _) =>
        {
            _projectManager.NewProject();
            EnsureDefaultReactor();
            RefreshDatasetList();
            _meshViewport.Clear();
            SetStatus("Nuovo progetto creato.");
        }), -1);

        toolbar.Insert(CreateIconButton("Apri", "Apri progetto .gtp", CairoExtensions.MakeIcon(CairoExtensions.Info), (_, _) => OpenProjectDialog()), -1);
        toolbar.Insert(CreateIconButton("Salva", "Salva progetto", CairoExtensions.MakeIcon(CairoExtensions.Success), (_, _) => SaveProjectDialog()), -1);
        toolbar.Insert(new SeparatorToolItem(), -1);
        toolbar.Insert(CreateIconButton("Dataset", "Aggiungi dataset da file", CairoExtensions.MakeIcon(CairoExtensions.Warning), (_, _) => AddExistingDataset()), -1);
        toolbar.Insert(CreateIconButton("Reload", "Ricarica progetto corrente", CairoExtensions.MakeIcon(CairoExtensions.Accent2), (_, _) => ReloadCurrentProject()), -1);
        toolbar.Insert(new SeparatorToolItem(), -1);
        toolbar.Insert(CreateIconButton("Cluster", "Aggiorna cluster", CairoExtensions.MakeIcon(CairoExtensions.Primary), (_, _) => RefreshNodeList()), -1);
        toolbar.Insert(CreateIconButton("Settings", "Salva impostazioni", CairoExtensions.MakeIcon(CairoExtensions.Muted), (_, _) =>
        {
            _settingsManager.SaveSettings();
            SetStatus("Impostazioni salvate.");
        }), -1);

        return toolbar;
    }

    private Widget BuildDatasetPanel()
    {
        var panel = new VBox(false, 6);

        var header = new Label("Dataset aperti (GTK edition)") { Xalign = 0 };
        panel.PackStart(header, false, false, 0);

        var datasetView = new TreeView(_datasetStore);
        datasetView.HeadersVisible = true;
        datasetView.Selection.Changed += OnDatasetSelectionChanged;

        datasetView.AppendColumn("Nome", new CellRendererText(), "text", 0);
        datasetView.AppendColumn("Tipo", new CellRendererText(), "text", 1);

        var scroller = new ScrolledWindow();
        scroller.Add(datasetView);
        scroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        scroller.HeightRequest = 260;
        panel.PackStart(scroller, false, true, 0);

        var buttonRow = new VBox(false, 4);

        var addPhysicoButton = new Button("Aggiungi PhysicoChem");
        addPhysicoButton.Clicked += (_, _) =>
        {
            var dataset = new PhysicoChemDataset($"PhysicoChem_{DateTime.Now:HHmmss}", "Profilo multiphysics GTK");
            _projectManager.AddDataset(dataset);
            RefreshDatasetList();
        };

        var addBoreholeButton = new Button("Aggiungi Borehole");
        addBoreholeButton.Clicked += (_, _) =>
        {
            var dataset = new BoreholeDataset($"Borehole_{DateTime.Now:HHmmss}", string.Empty)
            {
                SurfaceCoordinates = new System.Numerics.Vector2(0, 0),
                TotalDepth = 1200,
                Elevation = 120
            };
            _projectManager.AddDataset(dataset);
            RefreshDatasetList();
        };

        var addMeshButton = new Button("Crea mesh vuota");
        addMeshButton.Clicked += (_, _) =>
        {
            var mesh = Mesh3DDataset.CreateEmpty("Mesh3D (GTK)", string.Empty);
            _projectManager.AddDataset(mesh);
            RefreshDatasetList();
        };

        buttonRow.PackStart(addPhysicoButton, false, false, 0);
        buttonRow.PackStart(addBoreholeButton, false, false, 0);
        buttonRow.PackStart(addMeshButton, false, false, 0);

        var importDataset = new Button("Importa dataset esistente");
        importDataset.Clicked += (_, _) => AddExistingDataset();
        buttonRow.PackStart(importDataset, false, false, 0);

        panel.PackStart(buttonRow, false, false, 0);

        return panel;
    }

    private Notebook BuildWorkspace()
    {
        var notebook = new Notebook();
        notebook.AppendPage(BuildOverviewTab(), new Label("Editor"));
        notebook.AppendPage(BuildMeshTab(), new Label("Mesh 3D"));
        notebook.AppendPage(BuildNodeTab(), new Label("Node/Endpoint"));
        return notebook;
    }

    private Widget BuildOverviewTab()
    {
        var box = new VBox(false, 6);
        var instructions = new Label("Seleziona un dataset per modificare PhysicoChem o Borehole e lanciare simulazioni geotermiche, multifisiche e termodinamiche.")
        {
            Wrap = true,
            Xalign = 0
        };
        box.PackStart(instructions, false, false, 0);

        var detailFrame = new Frame("Dettagli dataset");
        var detailScroller = new ScrolledWindow();
        detailScroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        detailScroller.Add(_detailsView);
        detailFrame.Add(detailScroller);
        box.PackStart(detailFrame, true, true, 0);

        box.PackStart(BuildComposerPanel(), false, false, 0);

        return box;
    }

    private Widget BuildMeshTab()
    {
        var box = new VBox(false, 6);

        var headerRow = new HBox(false, 6);
        var toggleOptions = new ToggleButton("Mostra/Nascondi pannello opzioni") { Active = true };
        toggleOptions.Toggled += (_, _) => _meshOptionsRevealer.RevealChild = toggleOptions.Active;
        headerRow.PackStart(toggleOptions, false, false, 0);
        box.PackStart(headerRow, false, false, 0);

        var hbox = new HPaned { Position = 420 };
        hbox.Add1(BuildMeshOptionsPanel());

        var viewportFrame = new Frame("Visualizzazione 3D stile PetraSim/COMSOL");
        viewportFrame.Add(_meshViewport);
        hbox.Add2(viewportFrame);

        box.PackStart(hbox, true, true, 0);

        return box;
    }

    private Widget BuildMeshOptionsPanel()
    {
        var container = new VBox(false, 4);

        var controls = new Grid { ColumnSpacing = 8, RowSpacing = 6, BorderWidth = 4 };
        controls.Attach(new Label("Borehole"), 0, 0, 1, 1);
        controls.Attach(_boreholeSelector, 1, 0, 2, 1);

        controls.Attach(new Label("Layer Voronoi"), 0, 1, 1, 1);
        controls.Attach(_layerInput, 1, 1, 1, 1);

        controls.Attach(new Label("Raggio (m)"), 0, 2, 1, 1);
        controls.Attach(_radiusInput, 1, 2, 1, 1);

        controls.Attach(new Label("Altezza (m)"), 0, 3, 1, 1);
        controls.Attach(_heightInput, 1, 3, 1, 1);

        controls.Attach(new Label("Risoluzione mesh"), 0, 4, 1, 1);
        controls.Attach(_resolutionInput, 1, 4, 1, 1);

        var generateButton = new Button("Genera mesh Voronoi (compatibile ImGui)");
        generateButton.Clicked += (_, _) => GenerateVoronoiMesh();
        controls.Attach(generateButton, 0, 5, 3, 1);

        var importButton = new Button("Importa da mesh 3D");
        importButton.Clicked += (_, _) => ImportFromMeshDataset();
        controls.Attach(importButton, 0, 6, 3, 1);

        controls.Attach(new Label("Yaw"), 0, 7, 1, 1);
        var yawScale = new Scale(Orientation.Horizontal, _yawAdjustment) { DrawValue = true };
        yawScale.ValueChanged += (_, _) => UpdateViewportCamera();
        controls.Attach(yawScale, 1, 7, 2, 1);

        controls.Attach(new Label("Pitch"), 0, 8, 1, 1);
        var pitchScale = new Scale(Orientation.Horizontal, _pitchAdjustment) { DrawValue = true };
        pitchScale.ValueChanged += (_, _) => UpdateViewportCamera();
        controls.Attach(pitchScale, 1, 8, 2, 1);

        controls.Attach(new Label("Zoom"), 0, 9, 1, 1);
        var zoomScale = new Scale(Orientation.Horizontal, _zoomAdjustment) { DrawValue = true };
        zoomScale.ValueChanged += (_, _) => UpdateViewportCamera();
        controls.Attach(zoomScale, 1, 9, 2, 1);

        container.PackStart(controls, false, false, 0);

        var editFrame = new Frame("Editor mesh avanzato") { BorderWidth = 4 };
        var editGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6, BorderWidth = 6 };
        var unifyButton = new Button("Unisci mesh");
        unifyButton.Clicked += (_, _) => CombineMeshes(BooleanOperation.Union);
        var subtractButton = new Button("Sottrai mesh");
        subtractButton.Clicked += (_, _) => CombineMeshes(BooleanOperation.Subtract);
        var voronoiButton = new Button("Voronoi da mesh selezionata");
        voronoiButton.Clicked += (_, _) => GenerateVoronoiFromMesh();
        editGrid.Attach(unifyButton, 0, 0, 1, 1);
        editGrid.Attach(subtractButton, 1, 0, 1, 1);
        editGrid.Attach(voronoiButton, 0, 1, 2, 1);
        editFrame.Add(editGrid);
        container.PackStart(editFrame, false, false, 0);

        _meshOptionsRevealer.Child = container;
        return _meshOptionsRevealer;
    }

    private Widget BuildNodeTab()
    {
        var box = new VBox(false, 6);

        var info = new Label("Il client GTK mantiene piena compatibilità con il Node Endpoint per cluster e simulazioni distribuite.")
        {
            Xalign = 0,
            Wrap = true
        };
        box.PackStart(info, false, false, 0);

        var statusRow = new HBox(false, 6);
        var startButton = new Button("Avvia NodeManager");
        startButton.Clicked += (_, _) =>
        {
            _nodeManager.Start();
            RefreshNodeList();
        };

        var stopButton = new Button("Ferma NodeManager");
        stopButton.Clicked += (_, _) =>
        {
            _nodeManager.Stop();
            RefreshNodeList();
        };

        statusRow.PackStart(startButton, false, false, 0);
        statusRow.PackStart(stopButton, false, false, 0);

        box.PackStart(statusRow, false, false, 0);

        var nodeView = new TreeView(_nodeStore) { HeadersVisible = true };
        nodeView.AppendColumn("Nodo", new CellRendererText(), "text", 0);
        nodeView.AppendColumn("Stato", new CellRendererText(), "text", 1);
        nodeView.AppendColumn("Capacità", new CellRendererText(), "text", 2);

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

    private void UpdateDetails()
    {
        if (_selectedDataset == null)
        {
            _detailsView.Buffer.Text = "Nessun dataset selezionato.";
            return;
        }

        _detailsView.Buffer.Text = BuildDatasetSummary(_selectedDataset);
        UpdateMeshViewport(_selectedDataset);
        SetStatus($"Dataset attivo: {_selectedDataset.Name}");
    }

    private string BuildDatasetSummary(Dataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Nome: {dataset.Name}");
        sb.AppendLine($"Tipo: {dataset.Type}");

        switch (dataset)
        {
            case BoreholeDataset borehole:
                sb.AppendLine($"Profondità: {borehole.TotalDepth:F1} m");
                sb.AppendLine($"Coordinate: ({borehole.SurfaceCoordinates.X:F2}, {borehole.SurfaceCoordinates.Y:F2})");
                sb.AppendLine($"Unità di litologia: {borehole.LithologyUnits.Count}");
                break;
            case PhysicoChemDataset physico:
                sb.AppendLine($"Celle mesh: {physico.Mesh.Cells.Count}");
                sb.AppendLine($"Connessioni: {physico.Mesh.Connections.Count}");
                sb.AppendLine("Simulazioni supportate: geotermica, multifisica, termodinamica");
                sb.AppendLine($"Materiali: {physico.Materials.Count}, Forze: {physico.Forces.Count}");
                break;
            case Mesh3DDataset mesh3D:
                sb.AppendLine($"Vertici: {mesh3D.VertexCount}, Facce: {mesh3D.FaceCount}");
                sb.AppendLine($"Formato: {mesh3D.FileFormat}");
                break;
            default:
                sb.AppendLine("Editor GTK pronto per dataset generici.");
                break;
        }

        sb.AppendLine();
        sb.AppendLine("Le modifiche restano compatibili con la pipeline ImGui esistente.");
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
                _meshViewport.LoadFromPhysicoChem(physico.Mesh);
                break;
            case Mesh3DDataset mesh3D:
                _meshViewport.LoadFromMesh(mesh3D);
                break;
            default:
                _meshViewport.Clear();
                break;
        }
    }

    private void GenerateVoronoiMesh()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            _detailsView.Buffer.Text = "Seleziona un dataset PhysicoChem per generare la mesh.";
            return;
        }

        var boreholes = _projectManager.LoadedDatasets.OfType<BoreholeDataset>().ToList();
        if (boreholes.Count == 0)
        {
            _detailsView.Buffer.Text = "Aggiungi un dataset Borehole per vincolare la mesh Voronoi.";
            return;
        }

        var boreholeName = _boreholeSelector.ActiveText ?? boreholes.First().Name;
        var borehole = boreholes.FirstOrDefault(b => b.Name == boreholeName) ?? boreholes.First();

        physico.Mesh.GenerateVoronoiMesh(borehole, (int)_layerInput.Value, _radiusInput.Value, _heightInput.Value);
        var divisions = Math.Max(1, (int)_resolutionInput.Value / 10);
        physico.Mesh.SplitIntoGrid(divisions, divisions, divisions);
        _meshViewport.LoadFromPhysicoChem(physico.Mesh);
        _detailsView.Buffer.Text = BuildDatasetSummary(physico);
        SetStatus("Mesh Voronoi generata e sincronizzata con l'editor 3D.");
    }

    private void ImportFromMeshDataset()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            _detailsView.Buffer.Text = "Seleziona un dataset PhysicoChem per importare una mesh 3D.";
            return;
        }

        var meshDataset = _projectManager.LoadedDatasets.OfType<Mesh3DDataset>().FirstOrDefault();
        if (meshDataset == null)
        {
            _detailsView.Buffer.Text = "Aggiungi un dataset Mesh3D per eseguire l'import.";
            return;
        }

        physico.Mesh.FromMesh3DDataset(meshDataset, _heightInput.Value);
        _meshViewport.LoadFromPhysicoChem(physico.Mesh);
        _detailsView.Buffer.Text = BuildDatasetSummary(physico);
        SetStatus("Mesh importata e pronta per simulazioni multiphysics.");
    }

    private void UpdateViewportCamera()
    {
        _meshViewport.SetCamera((float)_yawAdjustment.Value, (float)_pitchAdjustment.Value, (float)_zoomAdjustment.Value);
    }

    private void WireNodeEvents()
    {
        _nodeManager.NodeConnected += _ => QueueNodeRefresh();
        _nodeManager.NodeDisconnected += _ => QueueNodeRefresh();
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
    }

    private ToolItem CreateIconButton(string label, string tooltip, Pixbuf icon, EventHandler handler)
    {
        var button = new ToolButton(new Image(icon), label) { TooltipText = tooltip };
        button.Clicked += handler;
        return button;
    }

    private void OpenProjectDialog()
    {
        using var dialog = new FileChooserDialog("Apri progetto", this, FileChooserAction.Open, "Annulla", ResponseType.Cancel, "Apri", ResponseType.Accept)
        {
            SelectMultiple = false
        };
        var filter = new FileFilter { Name = "Progetti Geoscientist (.gtp)" };
        filter.AddPattern("*.gtp");
        dialog.AddFilter(filter);
        if (dialog.Run() == (int)ResponseType.Accept)
        {
            try
            {
                _projectManager.LoadProject(dialog.Filename);
                RefreshDatasetList();
                SetStatus($"Progetto caricato: {dialog.Filename}");
            }
            catch (Exception ex)
            {
                SetStatus($"Errore apertura progetto: {ex.Message}");
            }
        }
        dialog.Destroy();
    }

    private void SaveProjectDialog()
    {
        using var dialog = new FileChooserDialog("Salva progetto", this, FileChooserAction.Save, "Annulla", ResponseType.Cancel, "Salva", ResponseType.Accept)
        {
            DoOverwriteConfirmation = true
        };
        var filter = new FileFilter { Name = "Progetti Geoscientist (.gtp)" };
        filter.AddPattern("*.gtp");
        dialog.AddFilter(filter);
        if (dialog.Run() == (int)ResponseType.Accept)
        {
            var path = dialog.Filename.EndsWith(".gtp") ? dialog.Filename : dialog.Filename + ".gtp";
            _projectManager.SaveProject(path);
            SetStatus($"Progetto salvato in {path}");
        }
        dialog.Destroy();
    }

    private void AddExistingDataset()
    {
        using var dialog = new FileChooserDialog("Importa dataset", this, FileChooserAction.Open, "Annulla", ResponseType.Cancel, "Importa", ResponseType.Accept)
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
            SetStatus($"Dataset importato: {name}");
        }
        dialog.Destroy();
    }

    private void ReloadCurrentProject()
    {
        if (string.IsNullOrWhiteSpace(_projectManager.ProjectPath))
        {
            SetStatus("Nessun progetto da ricaricare.");
            return;
        }

        try
        {
            _projectManager.LoadProject(_projectManager.ProjectPath);
            RefreshDatasetList();
            SetStatus("Progetto ricaricato e sincronizzato.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore reload: {ex.Message}");
        }
    }

    private Widget BuildComposerPanel()
    {
        var frame = new Frame("Designer e librerie") { BorderWidth = 4 };
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6, BorderWidth = 6 };

        _datasetTypeSelector.AppendText("PhysicoChem");
        _datasetTypeSelector.AppendText("Borehole");
        _datasetTypeSelector.AppendText("Mesh3D");
        _datasetTypeSelector.Active = 0;

        grid.Attach(new Label("Nome"), 0, 0, 1, 1);
        grid.Attach(_datasetNameEntry, 1, 0, 1, 1);
        grid.Attach(new Label("Tipo"), 0, 1, 1, 1);
        grid.Attach(_datasetTypeSelector, 1, 1, 1, 1);

        var createButton = new Button("Crea dataset da zero");
        createButton.Clicked += (_, _) => CreateDatasetFromInputs();
        grid.Attach(createButton, 0, 2, 2, 1);

        _materialPhaseSelector.AppendText("Solido");
        _materialPhaseSelector.AppendText("Liquido");
        _materialPhaseSelector.AppendText("Gas");
        _materialPhaseSelector.Active = 0;

        grid.Attach(new Label("Materiale"), 0, 3, 1, 1);
        grid.Attach(_materialNameEntry, 1, 3, 1, 1);
        grid.Attach(_materialPhaseSelector, 2, 3, 1, 1);

        var materialButton = new Button("Aggiungi materiale e assegna alla mesh");
        materialButton.Clicked += (_, _) => AddMaterialToDataset();
        grid.Attach(materialButton, 0, 4, 3, 1);

        foreach (var type in Enum.GetNames(typeof(ForceType)))
            _forceTypeSelector.AppendText(type);
        _forceTypeSelector.Active = 0;

        grid.Attach(new Label("Forza"), 0, 5, 1, 1);
        grid.Attach(_forceNameEntry, 1, 5, 1, 1);
        grid.Attach(_forceTypeSelector, 2, 5, 1, 1);
        var forceButton = new Button("Aggiungi forza multiphysics");
        forceButton.Clicked += (_, _) => AddForceToDataset();
        grid.Attach(forceButton, 0, 6, 3, 1);

        frame.Add(grid);
        return frame;
    }

    private void CreateDatasetFromInputs()
    {
        var name = string.IsNullOrWhiteSpace(_datasetNameEntry.Text) ? $"Dataset_{DateTime.Now:HHmmss}" : _datasetNameEntry.Text.Trim();
        var type = _datasetTypeSelector.ActiveText ?? "PhysicoChem";

        Dataset dataset = type switch
        {
            "PhysicoChem" => new PhysicoChemDataset(name, "Designer GTK")
            {
                Materials = { new MaterialProperties { MaterialID = "Rock", Density = 2500, ThermalConductivity = 2.1, SpecificHeat = 900 } },
                Forces = { new ForceField("Gravity", ForceType.Gravity) }
            },
            "Borehole" => new BoreholeDataset(name, string.Empty)
            {
                SurfaceCoordinates = new System.Numerics.Vector2(0, 0),
                TotalDepth = 800,
                Elevation = 120
            },
            _ => Mesh3DDataset.CreateEmpty(name, string.Empty)
        };

        _projectManager.AddDataset(dataset);
        RefreshDatasetList();
        SetStatus($"Creato dataset {name} ({type}).");
    }

    private void AddMaterialToDataset()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            SetStatus("Seleziona un dataset PhysicoChem per assegnare materiali.");
            return;
        }

        var mat = new MaterialProperties
        {
            MaterialID = string.IsNullOrWhiteSpace(_materialNameEntry.Text) ? "Materiale GTK" : _materialNameEntry.Text.Trim(),
            Density = 2400,
            ThermalConductivity = 1.8,
            SpecificHeat = 800
        };
        physico.Materials.Add(mat);
        foreach (var cell in physico.Mesh.Cells.Values)
            cell.MaterialID = mat.MaterialID;

        _detailsView.Buffer.Text = BuildDatasetSummary(physico);
        SetStatus($"Materiale '{mat.MaterialID}' assegnato a tutte le celle.");
    }

    private void AddForceToDataset()
    {
        if (_selectedDataset is not PhysicoChemDataset physico)
        {
            SetStatus("Seleziona un dataset PhysicoChem per aggiungere forze.");
            return;
        }

        var forceName = string.IsNullOrWhiteSpace(_forceNameEntry.Text) ? "Forza" + (physico.Forces.Count + 1) : _forceNameEntry.Text.Trim();
        var type = Enum.TryParse<ForceType>(_forceTypeSelector.ActiveText, out var parsed) ? parsed : ForceType.Gravity;
        var force = new ForceField(forceName, type);
        if (type == ForceType.Gravity)
            force.GravityVector = (0, 0, -9.81);
        else if (type == ForceType.Vortex)
            force.VortexStrength = 2.5;

        physico.Forces.Add(force);
        _detailsView.Buffer.Text = BuildDatasetSummary(physico);
        SetStatus($"Forza '{forceName}' aggiunta ({type}).");
    }

    private void CombineMeshes(BooleanOperation op)
    {
        var meshes = _projectManager.LoadedDatasets.OfType<Mesh3DDataset>().ToList();
        if (meshes.Count < 2 && _selectedDataset is not Mesh3DDataset)
        {
            SetStatus("Servono almeno due mesh 3D per unire o sottrarre.");
            return;
        }

        var primary = _selectedDataset as Mesh3DDataset ?? meshes.First();
        var secondary = meshes.FirstOrDefault(m => m != primary);
        if (secondary == null)
        {
            SetStatus("Seleziona una seconda mesh da combinare.");
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
        SetStatus(op == BooleanOperation.Union ? "Mesh unificate." : "Mesh sottratta.");
    }

    private void GenerateVoronoiFromMesh()
    {
        if (_selectedDataset is not Mesh3DDataset mesh || !_projectManager.LoadedDatasets.OfType<PhysicoChemDataset>().Any())
        {
            SetStatus("Seleziona una mesh 3D e un dataset PhysicoChem per trasferire la Voronoi.");
            return;
        }

        var target = _projectManager.LoadedDatasets.OfType<PhysicoChemDataset>().First();
        target.Mesh.FromMesh3DDataset(mesh, _heightInput.Value);
        _meshViewport.LoadFromPhysicoChem(target.Mesh);
        _detailsView.Buffer.Text = BuildDatasetSummary(target);
        SetStatus("Mesh Voronoi aggiornata dal dataset 3D selezionato.");
    }

    private void SetStatus(string message)
    {
        _statusBar.Text = "🔹 " + message;
        Logger.Log(message);
    }

    private void ApplyProfessionalStyling()
    {
        _detailsView.ModifyFont(Pango.FontDescription.FromString("Cantarell 11"));
    }

    private enum BooleanOperation
    {
        Union,
        Subtract
    }

    private void EnsureDefaultReactor()
    {
        if (_projectManager.LoadedDatasets.OfType<PhysicoChemDataset>().Any()) return;
        var reactor = MultiphysicsExamples.CreateExothermicReactor(3.5, 6.0);
        reactor.Name = "Default Exothermic Reactor";
        _projectManager.AddDataset(reactor);
    }
}

internal static class CairoExtensions
{
    public static readonly Cairo.Color Accent = new(0.23, 0.74, 0.98);
    public static readonly Cairo.Color Accent2 = new(0.75, 0.49, 0.99);
    public static readonly Cairo.Color Warning = new(0.98, 0.72, 0.29);
    public static readonly Cairo.Color Success = new(0.38, 0.87, 0.54);
    public static readonly Cairo.Color Info = new(0.31, 0.6, 0.91);
    public static readonly Cairo.Color Primary = new(0.58, 0.65, 0.98);
    public static readonly Cairo.Color Muted = new(0.55, 0.58, 0.66);

    public static Pixbuf MakeIcon(Cairo.Color color)
    {
        const int size = 28;
        using var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, size, size);
        using var ctx = new Cairo.Context(surface);
        ctx.Operator = Cairo.Operator.Source;
        ctx.SetSourceRGBA(0, 0, 0, 0);
        ctx.Paint();
        ctx.Operator = Cairo.Operator.Over;

        ctx.Arc(size / 2.0, size / 2.0, size / 2.4, 0, Math.PI * 2);
        ctx.SetSourceRGBA(color.R, color.G, color.B, 0.85);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1, 1, 1, 0.35);
        ctx.LineWidth = 2;
        ctx.Stroke();

        ctx.MoveTo(size * 0.3, size * 0.55);
        ctx.LineTo(size * 0.48, size * 0.7);
        ctx.LineTo(size * 0.74, size * 0.32);
        ctx.SetSourceRGB(0.95, 0.97, 0.99);
        ctx.LineWidth = 3.6;
        ctx.Stroke();

        surface.Flush();
        return new Pixbuf(surface.Data, Gdk.Colorspace.Rgb, true, 8, size, size, surface.Stride);
    }
}
