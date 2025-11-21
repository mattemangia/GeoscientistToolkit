using System;
using System.Linq;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Materials;
using Gtk;

namespace GeoscientistToolkit.GtkUI.Dialogs;

/// <summary>
/// Material Library Browser - Professional dialog for selecting materials from the library
/// Supports 440+ chemical compounds and physical materials
/// </summary>
public class MaterialLibraryDialog : Dialog
{
    private readonly MaterialLibrary _materialLibrary;
    private readonly CompoundLibrary _compoundLibrary;

    private readonly TreeView _materialTreeView;
    private readonly ListStore _materialStore;
    private readonly Entry _searchEntry;
    private readonly ComboBoxText _categoryFilter;
    private readonly TextView _propertiesView;

    private PhysicalMaterial? _selectedMaterial;
    private ChemicalCompound? _selectedCompound;

    public PhysicalMaterial? SelectedMaterial => _selectedMaterial;
    public ChemicalCompound? SelectedCompound => _selectedCompound;
    public bool IsCompoundSelected { get; private set; }

    public MaterialLibraryDialog(Window parent) : base("Material Library Browser", parent, DialogFlags.Modal)
    {
        _materialLibrary = MaterialLibrary.Instance;
        _compoundLibrary = CompoundLibrary.Instance;

        SetDefaultSize(900, 600);
        BorderWidth = 8;

        // Store: Name, Category, Type (Physical/Chemical), Object
        _materialStore = new ListStore(typeof(string), typeof(string), typeof(string), typeof(object));
        _materialTreeView = new TreeView(_materialStore);
        _searchEntry = new Entry { PlaceholderText = "Search materials and compounds..." };
        _categoryFilter = new ComboBoxText();
        _propertiesView = new TextView { Editable = false, WrapMode = WrapMode.Word, Monospace = true };

        BuildUI();
        PopulateMaterials();

        AddButton("Cancel", ResponseType.Cancel);
        AddButton("Select", ResponseType.Ok);

        ShowAll();
    }

    private void BuildUI()
    {
        var contentBox = new VBox(false, 8);

        // Header with search and filter
        var headerBox = new HBox(false, 6);
        headerBox.PackStart(new Label("Search:"), false, false, 0);
        _searchEntry.Changed += (_, _) => FilterMaterials();
        headerBox.PackStart(_searchEntry, true, true, 0);

        headerBox.PackStart(new Label("Category:"), false, false, 0);
        _categoryFilter.AppendText("All");
        _categoryFilter.AppendText("Rocks");
        _categoryFilter.AppendText("Fluids");
        _categoryFilter.AppendText("Metals");
        _categoryFilter.AppendText("Compounds");
        _categoryFilter.AppendText("Minerals");
        _categoryFilter.AppendText("Gases");
        _categoryFilter.Active = 0;
        _categoryFilter.Changed += (_, _) => FilterMaterials();
        headerBox.PackStart(_categoryFilter, false, false, 0);

        contentBox.PackStart(headerBox, false, false, 0);

        // Main content: tree view + properties
        var mainPane = new HPaned { Position = 400 };

        // Left: Material list
        var treeFrame = new Frame("Materials & Compounds");
        _materialTreeView.AppendColumn("Name", new CellRendererText(), "text", 0);
        _materialTreeView.AppendColumn("Category", new CellRendererText(), "text", 1);
        _materialTreeView.AppendColumn("Type", new CellRendererText(), "text", 2);
        _materialTreeView.HeadersVisible = true;
        _materialTreeView.Selection.Changed += OnSelectionChanged;

        var treeScroller = new ScrolledWindow();
        treeScroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        treeScroller.Add(_materialTreeView);
        treeFrame.Add(treeScroller);
        mainPane.Add1(treeFrame);

        // Right: Properties view
        var propsFrame = new Frame("Properties");
        var propsScroller = new ScrolledWindow();
        propsScroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        propsScroller.Add(_propertiesView);
        propsFrame.Add(propsScroller);
        mainPane.Add2(propsFrame);

        contentBox.PackStart(mainPane, true, true, 0);

        VBox.PackStart(contentBox, true, true, 0);
    }

    private void PopulateMaterials()
    {
        _materialStore.Clear();

        // Add physical materials
        foreach (var material in _materialLibrary.Materials.OrderBy(m => m.Name))
        {
            string category = DetermineCategory(material);
            _materialStore.AppendValues(material.Name, category, "Physical", material);
        }

        // Add chemical compounds
        foreach (var compound in _compoundLibrary.Compounds.OrderBy(c => c.Name))
        {
            string category = DetermineCompoundCategory(compound);
            _materialStore.AppendValues(compound.Name, category, "Chemical", compound);
        }
    }

    private void FilterMaterials()
    {
        _materialStore.Clear();
        string searchTerm = _searchEntry.Text.ToLower();
        string categoryFilter = _categoryFilter.ActiveText ?? "All";

        // Filter physical materials
        var filteredMaterials = _materialLibrary.Materials
            .Where(m => (string.IsNullOrEmpty(searchTerm) || m.Name.ToLower().Contains(searchTerm)))
            .Where(m => categoryFilter == "All" || DetermineCategory(m) == categoryFilter);

        foreach (var material in filteredMaterials.OrderBy(m => m.Name))
        {
            _materialStore.AppendValues(material.Name, DetermineCategory(material), "Physical", material);
        }

        // Filter chemical compounds
        var filteredCompounds = _compoundLibrary.Compounds
            .Where(c => (string.IsNullOrEmpty(searchTerm) || c.Name.ToLower().Contains(searchTerm) ||
                        (c.Formula != null && c.Formula.ToLower().Contains(searchTerm))))
            .Where(c => categoryFilter == "All" || DetermineCompoundCategory(c) == categoryFilter);

        foreach (var compound in filteredCompounds.OrderBy(c => c.Name))
        {
            _materialStore.AppendValues(compound.Name, DetermineCompoundCategory(compound), "Chemical", compound);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_materialTreeView.Selection.GetSelected(out TreeIter iter))
        {
            var obj = _materialStore.GetValue(iter, 3);

            if (obj is PhysicalMaterial material)
            {
                _selectedMaterial = material;
                _selectedCompound = null;
                IsCompoundSelected = false;
                DisplayMaterialProperties(material);
            }
            else if (obj is ChemicalCompound compound)
            {
                _selectedCompound = compound;
                _selectedMaterial = null;
                IsCompoundSelected = true;
                DisplayCompoundProperties(compound);
            }
        }
    }

    private void DisplayMaterialProperties(PhysicalMaterial material)
    {
        var text = $"PHYSICAL MATERIAL: {material.Name}\n";
        text += $"{'=',60}\n\n";

        text += "THERMAL PROPERTIES:\n";
        text += $"  Thermal Conductivity: {material.ThermalConductivity:F3} W/m·K\n";
        text += $"  Specific Heat: {material.SpecificHeat:F0} J/kg·K\n\n";

        text += "MECHANICAL PROPERTIES:\n";
        text += $"  Density: {material.Density:F0} kg/m³\n";
        if (material.YoungModulus > 0)
            text += $"  Young's Modulus: {material.YoungModulus / 1e9:F1} GPa\n";
        if (material.PoissonRatio > 0)
            text += $"  Poisson's Ratio: {material.PoissonRatio:F3}\n\n";

        text += "PETROPHYSICAL PROPERTIES:\n";
        text += $"  Porosity: {material.Porosity * 100:F1} %\n";
        if (material.Permeability > 0)
            text += $"  Permeability: {material.Permeability:E2} m²\n\n";

        if (material.AcousticVelocityP > 0 || material.AcousticVelocityS > 0)
        {
            text += "ACOUSTIC PROPERTIES:\n";
            if (material.AcousticVelocityP > 0)
                text += $"  P-wave Velocity: {material.AcousticVelocityP:F0} m/s\n";
            if (material.AcousticVelocityS > 0)
                text += $"  S-wave Velocity: {material.AcousticVelocityS:F0} m/s\n\n";
        }

        if (!string.IsNullOrEmpty(material.Description))
            text += $"DESCRIPTION:\n{material.Description}\n";

        _propertiesView.Buffer.Text = text;
    }

    private void DisplayCompoundProperties(ChemicalCompound compound)
    {
        var text = $"CHEMICAL COMPOUND: {compound.Name}\n";
        if (!string.IsNullOrEmpty(compound.Formula))
            text += $"Formula: {compound.Formula}\n";
        text += $"{'=',60}\n\n";

        text += "THERMODYNAMIC PROPERTIES:\n";
        text += $"  Molecular Weight: {compound.MolecularWeight:F2} g/mol\n";
        if (compound.Density > 0)
            text += $"  Density: {compound.Density:F2} g/cm³\n";
        if (compound.MolarVolume > 0)
            text += $"  Molar Volume: {compound.MolarVolume:F2} cm³/mol\n\n";

        if (compound.GibbsEnergy != 0 || compound.Enthalpy != 0 || compound.Entropy != 0)
        {
            text += "STANDARD FORMATION PROPERTIES (298.15 K):\n";
            text += $"  ΔG°: {compound.GibbsEnergy:F1} kJ/mol\n";
            text += $"  ΔH°: {compound.Enthalpy:F1} kJ/mol\n";
            text += $"  S°: {compound.Entropy:F2} J/mol·K\n\n";
        }

        if (compound.Ksp > 0)
        {
            text += "SOLUBILITY:\n";
            text += $"  Ksp: {compound.Ksp:E2}\n";
            text += $"  Solubility: {compound.Solubility:F4} mol/L\n\n";
        }

        if (compound.pKa > 0 || compound.pKb > 0)
        {
            text += "ACID-BASE PROPERTIES:\n";
            if (compound.pKa > 0)
                text += $"  pKa: {compound.pKa:F2}\n";
            if (compound.pKb > 0)
                text += $"  pKb: {compound.pKb:F2}\n\n";
        }

        if (compound.ActivationEnergy > 0)
        {
            text += "KINETIC PROPERTIES:\n";
            text += $"  Activation Energy: {compound.ActivationEnergy:F1} kJ/mol\n";
            if (compound.RateConstant > 0)
                text += $"  Rate Constant: {compound.RateConstant:E2} (units vary)\n\n";
        }

        text += "CLASSIFICATION:\n";
        text += $"  Phase: {compound.Phase}\n";
        text += $"  Crystal System: {compound.CrystalSystem}\n";

        _propertiesView.Buffer.Text = text;
    }

    private string DetermineCategory(PhysicalMaterial material)
    {
        var name = material.Name.ToLower();
        if (name.Contains("granite") || name.Contains("sandstone") || name.Contains("limestone") ||
            name.Contains("shale") || name.Contains("basalt") || name.Contains("rock"))
            return "Rocks";
        if (name.Contains("water") || name.Contains("oil") || name.Contains("brine"))
            return "Fluids";
        if (name.Contains("steel") || name.Contains("copper") || name.Contains("aluminum") || name.Contains("iron"))
            return "Metals";
        if (name.Contains("air") || name.Contains("co2") || name.Contains("gas"))
            return "Gases";
        return "Other";
    }

    private string DetermineCompoundCategory(ChemicalCompound compound)
    {
        if (compound.Phase == "gas")
            return "Gases";
        if (compound.Phase == "aqueous" || compound.Phase == "liquid")
            return "Fluids";
        if (compound.Phase == "solid" && compound.CrystalSystem != "Unknown")
            return "Minerals";
        return "Compounds";
    }
}
