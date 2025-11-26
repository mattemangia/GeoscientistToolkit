using System;
using System.Collections.Generic;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Data.Materials;
using Gtk;

namespace GeoscientistToolkit.GtkUI.Dialogs;

/// <summary>
/// Domain Creator Dialog - Professional interface for creating reactor domains
/// Supports 8+ geometry types with material assignment
/// </summary>
public class DomainCreatorDialog : Dialog
{
    private readonly Entry _nameEntry;
    private readonly ComboBoxText _geometrySelector;
    private readonly ComboBoxText _materialSelector;
    private readonly CheckButton _activeCheckbox;
    private readonly CheckButton _interactionCheckbox;

    // Geometry parameters
    private readonly Grid _geometryParamsGrid;
    private readonly Dictionary<string, Widget> _paramWidgets = new();

    private readonly List<MaterialProperties> _availableMaterials;

    public ReactorDomain? CreatedDomain { get; private set; }

    public DomainCreatorDialog(Window parent, List<MaterialProperties> materials) : base("Create Reactor Domain", parent, DialogFlags.Modal)
    {
        _availableMaterials = materials;

        SetDefaultSize(500, 600);
        BorderWidth = 8;

        _nameEntry = new Entry { PlaceholderText = "Domain name (e.g., ReactorCore)" };
        _geometrySelector = new ComboBoxText();
        _materialSelector = new ComboBoxText();
        _activeCheckbox = new CheckButton("Domain is active") { Active = true };
        _interactionCheckbox = new CheckButton("Allow interaction with other domains") { Active = true };
        _geometryParamsGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6, BorderWidth = 6 };

        BuildUI();
        PopulateGeometries();
        PopulateMaterials();

        AddButton("Cancel", ResponseType.Cancel);
        AddButton("Create", ResponseType.Ok);

        Response += OnResponse;

        ShowAll();
    }

    private void BuildUI()
    {
        var contentBox = new VBox(false, 8);

        // Domain name
        var nameBox = new HBox(false, 6);
        nameBox.PackStart(new Label("Domain Name:"), false, false, 0);
        nameBox.PackStart(_nameEntry, true, true, 0);
        contentBox.PackStart(nameBox, false, false, 0);

        // Geometry selection
        var geomBox = new HBox(false, 6);
        geomBox.PackStart(new Label("Geometry Type:"), false, false, 0);
        _geometrySelector.Changed += OnGeometryChanged;
        geomBox.PackStart(_geometrySelector, true, true, 0);
        contentBox.PackStart(geomBox, false, false, 0);

        // Material selection
        var matBox = new HBox(false, 6);
        matBox.PackStart(new Label("Material:"), false, false, 0);
        matBox.PackStart(_materialSelector, true, true, 0);
        contentBox.PackStart(matBox, false, false, 0);

        // Geometry parameters frame
        var paramsFrame = new Frame("Geometry Parameters");
        var paramsScroller = new ScrolledWindow();
        paramsScroller.SetPolicy(PolicyType.Never, PolicyType.Automatic);
        paramsScroller.Add(_geometryParamsGrid);
        paramsFrame.Add(paramsScroller);
        contentBox.PackStart(paramsFrame, true, true, 0);

        // Options
        contentBox.PackStart(_activeCheckbox, false, false, 0);
        contentBox.PackStart(_interactionCheckbox, false, false, 0);

        this.ContentArea.PackStart(contentBox, true, true, 0);
    }

    private void PopulateGeometries()
    {
        _geometrySelector.AppendText("Box (Rectangular)");
        _geometrySelector.AppendText("Sphere");
        _geometrySelector.AppendText("Cylinder");
        _geometrySelector.AppendText("Cone");
        _geometrySelector.AppendText("Torus");
        _geometrySelector.AppendText("Parallelepiped");
        _geometrySelector.AppendText("Custom 2D Extrusion");
        _geometrySelector.AppendText("Custom 3D Mesh");
        _geometrySelector.AppendText("Voronoi");
        _geometrySelector.Active = 0;
    }

    private void PopulateMaterials()
    {
        _materialSelector.AppendText("(None - Select Material)");
        foreach (var material in _availableMaterials)
        {
            _materialSelector.AppendText(material.MaterialID);
        }
        _materialSelector.Active = 0;
    }

    private void OnGeometryChanged(object? sender, EventArgs e)
    {
        ClearGeometryParams();

        switch (_geometrySelector.Active)
        {
            case 0: // Box
                AddParam("Center X (m)", 0.0);
                AddParam("Center Y (m)", 0.0);
                AddParam("Center Z (m)", 0.0);
                AddParam("Width (m)", 10.0);
                AddParam("Depth (m)", 10.0);
                AddParam("Height (m)", 10.0);
                break;

            case 1: // Sphere
                AddParam("Center X (m)", 0.0);
                AddParam("Center Y (m)", 0.0);
                AddParam("Center Z (m)", 0.0);
                AddParam("Radius (m)", 5.0);
                break;

            case 2: // Cylinder
                AddParam("Base Center X (m)", 0.0);
                AddParam("Base Center Y (m)", 0.0);
                AddParam("Base Center Z (m)", 0.0);
                AddParam("Axis X", 0.0);
                AddParam("Axis Y", 0.0);
                AddParam("Axis Z", 1.0);
                AddParam("Radius (m)", 2.0);
                AddParam("Height (m)", 10.0);
                break;

            case 3: // Cone
                AddParam("Base Center X (m)", 0.0);
                AddParam("Base Center Y (m)", 0.0);
                AddParam("Base Center Z (m)", 0.0);
                AddParam("Axis X", 0.0);
                AddParam("Axis Y", 0.0);
                AddParam("Axis Z", 1.0);
                AddParam("Base Radius (m)", 3.0);
                AddParam("Top Radius (m)", 1.0);
                AddParam("Height (m)", 8.0);
                break;

            case 4: // Torus
                AddParam("Center X (m)", 0.0);
                AddParam("Center Y (m)", 0.0);
                AddParam("Center Z (m)", 0.0);
                AddParam("Major Radius (m)", 5.0);
                AddParam("Minor Radius (m)", 1.5);
                break;

            case 5: // Parallelepiped
                AddParam("Corner X (m)", 0.0);
                AddParam("Corner Y (m)", 0.0);
                AddParam("Corner Z (m)", 0.0);
                AddParam("Edge1 X (m)", 10.0);
                AddParam("Edge1 Y (m)", 0.0);
                AddParam("Edge1 Z (m)", 0.0);
                AddParam("Edge2 X (m)", 0.0);
                AddParam("Edge2 Y (m)", 10.0);
                AddParam("Edge2 Z (m)", 0.0);
                AddParam("Edge3 X (m)", 0.0);
                AddParam("Edge3 Y (m)", 0.0);
                AddParam("Edge3 Z (m)", 10.0);
                break;

            case 6: // Custom 2D
                AddLabel("Custom 2D profile extrusion");
                AddLabel("(Will need to load from file)");
                break;

            case 7: // Custom 3D
                AddLabel("Custom 3D mesh");
                AddLabel("(Will need to load from file)");
                break;

            case 8: // Voronoi
                AddParam("Number of Sites", 100);
                AddParam("Width (m)", 10.0);
                AddParam("Depth (m)", 10.0);
                AddParam("Height (m)", 10.0);
                break;
        }

        ShowAll();
    }

    private void ClearGeometryParams()
    {
        foreach (var child in _geometryParamsGrid.Children)
        {
            _geometryParamsGrid.Remove(child);
        }
        _paramWidgets.Clear();
    }

    private void AddParam(string label, double defaultValue)
    {
        int row = _paramWidgets.Count;
        var labelWidget = new Label(label) { Xalign = 0 };
        var spinButton = new SpinButton(-10000, 10000, 0.1) { Value = defaultValue, WidthRequest = 120 };

        _geometryParamsGrid.Attach(labelWidget, 0, row, 1, 1);
        _geometryParamsGrid.Attach(spinButton, 1, row, 1, 1);

        _paramWidgets[label] = spinButton;
    }

    private void AddLabel(string text)
    {
        int row = _paramWidgets.Count;
        var label = new Label(text) { Xalign = 0 };
        _geometryParamsGrid.Attach(label, 0, row, 2, 1);
        _paramWidgets[text] = label;
    }

    private double GetParamValue(string label)
    {
        if (_paramWidgets.TryGetValue(label, out var widget) && widget is SpinButton spin)
            return spin.Value;
        return 0.0;
    }

    private void OnResponse(object? sender, ResponseArgs args)
    {
        if (args.ResponseId == ResponseType.Ok)
        {
            CreatedDomain = CreateDomain();
        }
    }

    private ReactorDomain? CreateDomain()
    {
        var name = _nameEntry.Text;
        if (string.IsNullOrWhiteSpace(name))
            name = $"Domain_{DateTime.Now:HHmmss}";

        var domain = new ReactorDomain
        {
            Name = name,
            IsActive = _activeCheckbox.Active,
            AllowInteraction = _interactionCheckbox.Active
        };

        // Get selected material
        if (_materialSelector.Active > 0)
        {
            var materialId = _materialSelector.ActiveText;
            domain.Material = _availableMaterials.Find(m => m.MaterialID == materialId);
        }

        var geometry = new ReactorGeometry();

        switch (_geometrySelector.Active)
        {
            case 0: // Box
                geometry.Type = GeometryType.Box;
                geometry.Center = (GetParamValue("Center X (m)"), GetParamValue("Center Y (m)"), GetParamValue("Center Z (m)"));
                geometry.Dimensions = (GetParamValue("Width (m)"), GetParamValue("Height (m)"), GetParamValue("Depth (m)"));
                break;

            case 1: // Sphere
                geometry.Type = GeometryType.Sphere;
                geometry.Center = (GetParamValue("Center X (m)"), GetParamValue("Center Y (m)"), GetParamValue("Center Z (m)"));
                geometry.Radius = GetParamValue("Radius (m)");
                break;

            case 2: // Cylinder
                geometry.Type = GeometryType.Cylinder;
                geometry.CylinderBase = (GetParamValue("Base Center X (m)"), GetParamValue("Base Center Y (m)"), GetParamValue("Base Center Z (m)"));
                geometry.CylinderAxis = NormalizeAxis((GetParamValue("Axis X"), GetParamValue("Axis Y"), GetParamValue("Axis Z")));
                geometry.Radius = GetParamValue("Radius (m)");
                geometry.Height = GetParamValue("Height (m)");
                geometry.Center = AddAxisOffset(geometry.CylinderBase, geometry.CylinderAxis, geometry.Height / 2.0);
                break;

            case 3: // Cone
                geometry.Type = GeometryType.Cone;
                geometry.CylinderBase = (GetParamValue("Base Center X (m)"), GetParamValue("Base Center Y (m)"), GetParamValue("Base Center Z (m)"));
                geometry.CylinderAxis = NormalizeAxis((GetParamValue("Axis X"), GetParamValue("Axis Y"), GetParamValue("Axis Z")));
                geometry.Radius = GetParamValue("Base Radius (m)");
                geometry.TopRadius = GetParamValue("Top Radius (m)");
                geometry.Height = GetParamValue("Height (m)");
                geometry.Center = AddAxisOffset(geometry.CylinderBase, geometry.CylinderAxis, geometry.Height / 2.0);
                break;

            case 4: // Torus
                geometry.Type = GeometryType.Torus;
                geometry.Center = (GetParamValue("Center X (m)"), GetParamValue("Center Y (m)"), GetParamValue("Center Z (m)"));
                geometry.MajorRadius = GetParamValue("Major Radius (m)");
                geometry.MinorRadius = GetParamValue("Minor Radius (m)");
                break;

            case 5: // Parallelepiped
                geometry.Type = GeometryType.Parallelepiped;
                geometry.Corner = (GetParamValue("Corner X (m)"), GetParamValue("Corner Y (m)"), GetParamValue("Corner Z (m)"));
                geometry.Edge1 = (GetParamValue("Edge1 X (m)"), GetParamValue("Edge1 Y (m)"), GetParamValue("Edge1 Z (m)"));
                geometry.Edge2 = (GetParamValue("Edge2 X (m)"), GetParamValue("Edge2 Y (m)"), GetParamValue("Edge2 Z (m)"));
                geometry.Edge3 = (GetParamValue("Edge3 X (m)"), GetParamValue("Edge3 Y (m)"), GetParamValue("Edge3 Z (m)"));
                geometry.Center = (
                    geometry.Corner.X + geometry.Edge1.X / 2.0 + geometry.Edge2.X / 2.0 + geometry.Edge3.X / 2.0,
                    geometry.Corner.Y + geometry.Edge1.Y / 2.0 + geometry.Edge2.Y / 2.0 + geometry.Edge3.Y / 2.0,
                    geometry.Corner.Z + geometry.Edge1.Z / 2.0 + geometry.Edge2.Z / 2.0 + geometry.Edge3.Z / 2.0);
                break;

            case 6: // Custom 2D
                geometry.Type = GeometryType.Custom2D;
                geometry.Profile2D = new List<(double X, double Y)>
                {
                    (-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5)
                };
                geometry.ExtrusionDepth = 1.0;
                geometry.Center = (0, 0, 0);
                break;

            case 7: // Custom 3D
                geometry.Type = GeometryType.Custom3D;
                geometry.CustomPoints = new List<(double X, double Y, double Z)>
                {
                    (-0.5, -0.5, -0.5), (0.5, 0.5, 0.5)
                };
                geometry.Center = (0, 0, 0);
                break;

            case 8: // Voronoi
                geometry.Type = GeometryType.Box; // Treat as a box for bounding purposes
                geometry.Center = (0,0,0);
                geometry.Dimensions = (GetParamValue("Width (m)"), GetParamValue("Height (m)"), GetParamValue("Depth (m)"));
                var mesh = new PhysicoChemMesh();
                mesh.Generate2DExtrudedVoronoiMesh(
                    (int)GetParamValue("Number of Sites"),
                    GetParamValue("Width (m)"),
                    GetParamValue("Height (m)"),
                    GetParamValue("Depth (m)")
                );
                domain.VoronoiMesh = mesh;
                break;

            default:
                return null;
        }

        domain.Geometry = geometry;

        // Set initial conditions
        domain.InitialConditions = new InitialConditions
        {
            Temperature = 298.15,  // 25°C
            Pressure = 101325.0,   // 1 atm
            LiquidSaturation = 1.0,
            Concentrations = new Dictionary<string, double>()
        };

        return domain;
    }

    private static (double X, double Y, double Z) NormalizeAxis((double X, double Y, double Z) axis)
    {
        var magnitude = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z);
        if (magnitude < 1e-6)
            return (0, 0, 1);

        return (axis.X / magnitude, axis.Y / magnitude, axis.Z / magnitude);
    }

    private static (double X, double Y, double Z) AddAxisOffset((double X, double Y, double Z) origin,
        (double X, double Y, double Z) axis, double distance)
    {
        return (origin.X + axis.X * distance, origin.Y + axis.Y * distance, origin.Z + axis.Z * distance);
    }
}
