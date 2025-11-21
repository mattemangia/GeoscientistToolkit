using System.Linq;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;
using Gtk;

namespace GeoscientistToolkit.Gtk;

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
        Add(root);

        WireNodeEvents();
        RefreshDatasetList();
        RefreshNodeList();
    }

    private Widget BuildToolbar()
    {
        var box = new HBox(false, 6);

        var newProjectButton = new Button("Nuovo Progetto");
        newProjectButton.Clicked += (_, _) =>
        {
            _projectManager.NewProject();
            RefreshDatasetList();
            _meshViewport.Clear();
            _detailsView.Buffer.Text = "Nuovo progetto creato.";
        };

        var saveSettingsButton = new Button("Salva impostazioni");
        saveSettingsButton.Clicked += (_, _) => _settingsManager.SaveSettings();

        var refreshNodesButton = new Button("Aggiorna cluster");
        refreshNodesButton.Clicked += (_, _) => RefreshNodeList();

        box.PackStart(newProjectButton, false, false, 0);
        box.PackStart(saveSettingsButton, false, false, 0);
        box.PackStart(refreshNodesButton, false, false, 0);

        return box;
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

        return box;
    }

    private Widget BuildMeshTab()
    {
        var box = new VBox(false, 6);

        var controls = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
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

        box.PackStart(controls, false, false, 0);

        var viewportFrame = new Frame("Visualizzazione 3D stile PetraSim/COMSOL");
        viewportFrame.Add(_meshViewport);
        box.PackStart(viewportFrame, true, true, 0);

        return box;
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
}
