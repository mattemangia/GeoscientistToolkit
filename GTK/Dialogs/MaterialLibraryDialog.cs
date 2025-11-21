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

        this.ContentArea.PackStart(contentBox, true, true, 0);
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
            .Where(m => string.IsNullOrEmpty(searchTerm) ||
                        m.Name.ToLower().Contains(searchTerm) ||
                        (!string.IsNullOrEmpty(m.Notes) && m.Notes.ToLower().Contains(searchTerm)))
            .Where(m => categoryFilter == "All" || DetermineCategory(m) == categoryFilter);

        foreach (var material in filteredMaterials.OrderBy(m => m.Name))
        {
            _materialStore.AppendValues(material.Name, DetermineCategory(material), "Physical", material);
        }

        // Filter chemical compounds
        var filteredCompounds = _compoundLibrary.Compounds
            .Where(c => string.IsNullOrEmpty(searchTerm) || c.Name.ToLower().Contains(searchTerm) ||
                        (!string.IsNullOrEmpty(c.ChemicalFormula) && c.ChemicalFormula.ToLower().Contains(searchTerm)))
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
        if (material.ThermalConductivity_W_mK.HasValue)
            text += $"  Thermal Conductivity: {material.ThermalConductivity_W_mK:F3} W/m·K\n";
        if (material.SpecificHeatCapacity_J_kgK.HasValue)
            text += $"  Specific Heat: {material.SpecificHeatCapacity_J_kgK:F0} J/kg·K\n";
        text += "\n";

        text += "MECHANICAL PROPERTIES:\n";
        if (material.Density_kg_m3.HasValue)
            text += $"  Density: {material.Density_kg_m3:F0} kg/m³\n";
        if (material.YoungModulus_GPa.HasValue)
            text += $"  Young's Modulus: {material.YoungModulus_GPa:F1} GPa\n";
        if (material.PoissonRatio.HasValue)
            text += $"  Poisson's Ratio: {material.PoissonRatio:F3}\n\n";

        text += "PETROPHYSICAL PROPERTIES:\n";
        if (material.TypicalPorosity_fraction.HasValue)
            text += $"  Porosity: {material.TypicalPorosity_fraction * 100:F1} %\n";
        if (material.Extra.TryGetValue("Permeability", out var permeability))
            text += $"  Permeability: {permeability:E2} m²\n\n";

        if (material.Vp_m_s.HasValue || material.Vs_m_s.HasValue)
        {
            text += "ACOUSTIC PROPERTIES:\n";
            if (material.Vp_m_s.HasValue)
                text += $"  P-wave Velocity: {material.Vp_m_s:F0} m/s\n";
            if (material.Vs_m_s.HasValue)
                text += $"  S-wave Velocity: {material.Vs_m_s:F0} m/s\n\n";
        }

        if (!string.IsNullOrEmpty(material.Notes))
            text += $"NOTES:\n{material.Notes}\n";

        _propertiesView.Buffer.Text = text;
    }

    private void DisplayCompoundProperties(ChemicalCompound compound)
    {
        var text = $"CHEMICAL COMPOUND: {compound.Name}\n";
        if (!string.IsNullOrEmpty(compound.ChemicalFormula))
            text += $"Formula: {compound.ChemicalFormula}\n";
        text += $"{'=',60}\n\n";

        text += "THERMODYNAMIC PROPERTIES:\n";
        if (compound.MolecularWeight_g_mol.HasValue)
            text += $"  Molecular Weight: {compound.MolecularWeight_g_mol:F2} g/mol\n";
        if (compound.Density_g_cm3.HasValue)
            text += $"  Density: {compound.Density_g_cm3:F2} g/cm³\n";
        if (compound.MolarVolume_cm3_mol.HasValue)
            text += $"  Molar Volume: {compound.MolarVolume_cm3_mol:F2} cm³/mol\n\n";

        if (compound.GibbsFreeEnergyFormation_kJ_mol.HasValue || compound.EnthalpyFormation_kJ_mol.HasValue ||
            compound.Entropy_J_molK.HasValue)
        {
            text += "STANDARD FORMATION PROPERTIES (298.15 K):\n";
            if (compound.GibbsFreeEnergyFormation_kJ_mol.HasValue)
                text += $"  ΔG°: {compound.GibbsFreeEnergyFormation_kJ_mol:F1} kJ/mol\n";
            if (compound.EnthalpyFormation_kJ_mol.HasValue)
                text += $"  ΔH°: {compound.EnthalpyFormation_kJ_mol:F1} kJ/mol\n";
            if (compound.Entropy_J_molK.HasValue)
                text += $"  S°: {compound.Entropy_J_molK:F2} J/mol·K\n\n";
        }

        if (compound.LogKsp_25C.HasValue)
        {
            text += "SOLUBILITY:\n";
            var logKsp = compound.LogKsp_25C.Value;
            text += $"  log10(Ksp): {logKsp:F2}\n";
            text += $"  Ksp: {Math.Pow(10, logKsp):E2}\n";
            if (compound.Solubility_g_100mL_25C.HasValue)
                text += $"  Solubility: {compound.Solubility_g_100mL_25C:F4} g/100mL\n\n";
        }

        if (compound.pKa > 0 || compound.pKb > 0)
        {
            text += "ACID-BASE PROPERTIES:\n";
            if (compound.pKa > 0)
                text += $"  pKa: {compound.pKa:F2}\n";
            if (compound.pKb > 0)
                text += $"  pKb: {compound.pKb:F2}\n\n";
        }

        if (compound.ActivationEnergy_Dissolution_kJ_mol.HasValue || compound.ActivationEnergy_Precipitation_kJ_mol.HasValue)
        {
            text += "KINETIC PROPERTIES:\n";
            if (compound.ActivationEnergy_Dissolution_kJ_mol.HasValue)
                text += $"  Activation Energy (dissolution): {compound.ActivationEnergy_Dissolution_kJ_mol:F1} kJ/mol\n";
            if (compound.ActivationEnergy_Precipitation_kJ_mol.HasValue)
                text += $"  Activation Energy (precipitation): {compound.ActivationEnergy_Precipitation_kJ_mol:F1} kJ/mol\n";
            if (compound.RateConstant_Dissolution_mol_m2_s.HasValue)
                text += $"  Rate Constant (dissolution): {compound.RateConstant_Dissolution_mol_m2_s:E2} mol/m²/s\n";
            if (compound.RateConstant_Precipitation_mol_m2_s.HasValue)
                text += $"  Rate Constant (precipitation): {compound.RateConstant_Precipitation_mol_m2_s:E2} mol/m²/s\n\n";
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
        return compound.Phase switch
        {
            CompoundPhase.Gas => "Gases",
            CompoundPhase.Aqueous or CompoundPhase.Liquid => "Fluids",
            CompoundPhase.Solid when compound.CrystalSystem.HasValue => "Minerals",
            _ => "Compounds"
        };
    }
}
