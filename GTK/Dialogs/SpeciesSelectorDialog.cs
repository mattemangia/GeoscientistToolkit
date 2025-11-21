using System;
using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Materials;
using Gtk;

namespace GeoscientistToolkit.GtkUI.Dialogs;

/// <summary>
/// Species/Compound Selector Dialog
/// Professional interface for selecting dissolved species in water
/// Supports 440+ chemical compounds from the compound library
/// </summary>
public class SpeciesSelectorDialog : Dialog
{
    private readonly CompoundLibrary _compoundLibrary;

    private readonly TreeView _availableTreeView;
    private readonly TreeView _selectedTreeView;
    private readonly ListStore _availableStore;
    private readonly ListStore _selectedStore;

    private readonly Entry _searchEntry;
    private readonly ComboBoxText _phaseFilter;
    private readonly TextView _compoundInfoView;

    private readonly Dictionary<string, double> _selectedConcentrations = new();

    public Dictionary<string, double> SelectedConcentrations => new(_selectedConcentrations);

    public SpeciesSelectorDialog(Window parent, Dictionary<string, double>? initialConcentrations = null)
        : base("Configure Dissolved Species", parent, DialogFlags.Modal)
    {
        _compoundLibrary = CompoundLibrary.Instance;

        SetDefaultSize(1000, 700);
        BorderWidth = 8;

        // Available: Name, Formula, Phase, Compound object
        _availableStore = new ListStore(typeof(string), typeof(string), typeof(string), typeof(ChemicalCompound));
        _availableTreeView = new TreeView(_availableStore);

        // Selected: Name, Formula, Concentration, Compound object
        _selectedStore = new ListStore(typeof(string), typeof(string), typeof(string), typeof(ChemicalCompound));
        _selectedTreeView = new TreeView(_selectedStore);

        _searchEntry = new Entry { PlaceholderText = "Search by name or formula..." };
        _phaseFilter = new ComboBoxText();
        _compoundInfoView = new TextView { Editable = false, WrapMode = WrapMode.Word, Monospace = true };

        // Load initial concentrations if provided
        if (initialConcentrations != null)
        {
            foreach (var (species, conc) in initialConcentrations)
            {
                _selectedConcentrations[species] = conc;
            }
        }

        BuildUI();
        PopulateCompounds();

        AddButton("Cancel", ResponseType.Cancel);
        AddButton("Apply", ResponseType.Ok);

        ShowAll();
    }

    private void BuildUI()
    {
        var contentBox = new VBox(false, 8);

        // Header with search and filter
        var headerBox = new HBox(false, 6);
        headerBox.PackStart(new Label("Search:"), false, false, 0);
        _searchEntry.Changed += (_, _) => FilterCompounds();
        headerBox.PackStart(_searchEntry, true, true, 0);

        headerBox.PackStart(new Label("Phase:"), false, false, 0);
        _phaseFilter.AppendText("All");
        _phaseFilter.AppendText("Aqueous");
        _phaseFilter.AppendText("Gas");
        _phaseFilter.AppendText("Solid");
        _phaseFilter.Active = 1; // Default to aqueous
        _phaseFilter.Changed += (_, _) => FilterCompounds();
        headerBox.PackStart(_phaseFilter, false, false, 0);

        contentBox.PackStart(headerBox, false, false, 0);

        // Main layout: 3 panels
        var mainPane = new HPaned { Position = 400 };

        // Left panel: Available compounds
        var leftBox = new VBox(false, 4);
        leftBox.PackStart(new Label("Available Compounds (440+)") { Xalign = 0 }, false, false, 0);

        _availableTreeView.AppendColumn("Name", new CellRendererText(), "text", 0);
        _availableTreeView.AppendColumn("Formula", new CellRendererText(), "text", 1);
        _availableTreeView.AppendColumn("Phase", new CellRendererText(), "text", 2);
        _availableTreeView.HeadersVisible = true;
        _availableTreeView.Selection.Changed += OnAvailableSelectionChanged;

        var availableScroller = new ScrolledWindow();
        availableScroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        availableScroller.Add(_availableTreeView);
        leftBox.PackStart(availableScroller, true, true, 0);

        mainPane.Add1(leftBox);

        // Right side: selected + info
        var rightPane = new VPaned { Position = 300 };

        // Top right: Selected species
        var selectedBox = new VBox(false, 4);
        var selectedHeader = new HBox(false, 6);
        selectedHeader.PackStart(new Label("Selected Species") { Xalign = 0 }, true, true, 0);

        var addButton = new Button("Add →");
        addButton.Clicked += OnAddSpecies;
        selectedHeader.PackStart(addButton, false, false, 0);

        var removeButton = new Button("← Remove");
        removeButton.Clicked += OnRemoveSpecies;
        selectedHeader.PackStart(removeButton, false, false, 0);

        selectedBox.PackStart(selectedHeader, false, false, 0);

        _selectedTreeView.AppendColumn("Name", new CellRendererText(), "text", 0);
        _selectedTreeView.AppendColumn("Formula", new CellRendererText(), "text", 1);

        var concRenderer = new CellRendererText { Editable = true };
        concRenderer.Edited += OnConcentrationEdited;
        _selectedTreeView.AppendColumn("Concentration (M)", concRenderer, "text", 2);
        _selectedTreeView.HeadersVisible = true;

        var selectedScroller = new ScrolledWindow();
        selectedScroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        selectedScroller.Add(_selectedTreeView);
        selectedBox.PackStart(selectedScroller, true, true, 0);

        rightPane.Add1(selectedBox);

        // Bottom right: Compound info
        var infoFrame = new Frame("Compound Properties");
        var infoScroller = new ScrolledWindow();
        infoScroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        infoScroller.Add(_compoundInfoView);
        infoFrame.Add(infoScroller);
        rightPane.Add2(infoFrame);

        mainPane.Add2(rightPane);

        contentBox.PackStart(mainPane, true, true, 0);

        VBox.PackStart(contentBox, true, true, 0);
    }

    private void PopulateCompounds()
    {
        FilterCompounds();

        // Populate selected species
        foreach (var (species, conc) in _selectedConcentrations)
        {
            var compound = _compoundLibrary.Compounds.FirstOrDefault(c => c.Name == species || c.Formula == species);
            if (compound != null)
            {
                _selectedStore.AppendValues(compound.Name, compound.Formula ?? "", conc.ToString("F6"), compound);
            }
        }
    }

    private void FilterCompounds()
    {
        _availableStore.Clear();
        string searchTerm = _searchEntry.Text.ToLower();
        string phaseFilter = _phaseFilter.ActiveText?.ToLower() ?? "all";

        var filtered = _compoundLibrary.Compounds
            .Where(c => string.IsNullOrEmpty(searchTerm) ||
                       c.Name.ToLower().Contains(searchTerm) ||
                       (c.Formula != null && c.Formula.ToLower().Contains(searchTerm)))
            .Where(c => phaseFilter == "all" || c.Phase.ToLower() == phaseFilter)
            .OrderBy(c => c.Name);

        foreach (var compound in filtered)
        {
            _availableStore.AppendValues(compound.Name, compound.Formula ?? "", compound.Phase, compound);
        }
    }

    private void OnAvailableSelectionChanged(object? sender, EventArgs e)
    {
        if (_availableTreeView.Selection.GetSelected(out TreeIter iter))
        {
            var compound = (ChemicalCompound)_availableStore.GetValue(iter, 3);
            DisplayCompoundInfo(compound);
        }
    }

    private void OnAddSpecies(object? sender, EventArgs e)
    {
        if (_availableTreeView.Selection.GetSelected(out TreeIter iter))
        {
            var compound = (ChemicalCompound)_availableStore.GetValue(iter, 3);

            // Check if already added
            if (_selectedConcentrations.ContainsKey(compound.Name))
            {
                var dialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Warning, ButtonsType.Ok,
                    $"{compound.Name} is already in the selected list.");
                dialog.Run();
                dialog.Destroy();
                return;
            }

            // Add with default concentration
            double defaultConc = 0.001; // 1 mM
            _selectedConcentrations[compound.Name] = defaultConc;
            _selectedStore.AppendValues(compound.Name, compound.Formula ?? "", defaultConc.ToString("F6"), compound);
        }
    }

    private void OnRemoveSpecies(object? sender, EventArgs e)
    {
        if (_selectedTreeView.Selection.GetSelected(out TreeIter iter))
        {
            var compound = (ChemicalCompound)_selectedStore.GetValue(iter, 3);
            _selectedConcentrations.Remove(compound.Name);
            _selectedStore.Remove(ref iter);
        }
    }

    private void OnConcentrationEdited(object sender, EditedArgs args)
    {
        if (!_selectedStore.GetIterFromString(out TreeIter iter, args.Path))
            return;

        if (double.TryParse(args.NewText, out double concentration) && concentration >= 0)
        {
            var compound = (ChemicalCompound)_selectedStore.GetValue(iter, 3);
            _selectedConcentrations[compound.Name] = concentration;
            _selectedStore.SetValue(iter, 2, concentration.ToString("F6"));
        }
    }

    private void DisplayCompoundInfo(ChemicalCompound compound)
    {
        var text = $"COMPOUND: {compound.Name}\n";
        if (!string.IsNullOrEmpty(compound.Formula))
            text += $"Formula: {compound.Formula}\n";
        text += $"Phase: {compound.Phase}\n";
        text += $"{'=',50}\n\n";

        text += $"Molecular Weight: {compound.MolecularWeight:F2} g/mol\n";
        if (compound.Density > 0)
            text += $"Density: {compound.Density:F2} g/cm³\n\n";

        if (compound.GibbsEnergy != 0 || compound.Enthalpy != 0)
        {
            text += "THERMODYNAMIC DATA (298.15 K):\n";
            text += $"  ΔG°f: {compound.GibbsEnergy:F1} kJ/mol\n";
            text += $"  ΔH°f: {compound.Enthalpy:F1} kJ/mol\n";
            text += $"  S°: {compound.Entropy:F2} J/mol·K\n\n";
        }

        if (compound.Ksp > 0)
        {
            text += "SOLUBILITY:\n";
            text += $"  Ksp: {compound.Ksp:E2}\n";
            text += $"  Solubility: {compound.Solubility:F4} mol/L\n\n";
        }

        if (compound.pKa > 0)
            text += $"pKa: {compound.pKa:F2}\n";
        if (compound.pKb > 0)
            text += $"pKb: {compound.pKb:F2}\n";

        if (compound.ActivationEnergy > 0)
        {
            text += $"\nActivation Energy: {compound.ActivationEnergy:F1} kJ/mol\n";
        }

        _compoundInfoView.Buffer.Text = text;
    }
}
