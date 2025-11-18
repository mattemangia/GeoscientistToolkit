// GeoscientistToolkit/UI/Windows/CompoundLibraryEditorWindow.cs

using System.Numerics;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Windows;

/// <summary>
///     Window for browsing, searching, editing, and managing the thermodynamic compound library.
///     Allows users to add custom compounds or modify existing ones.
/// </summary>
public class CompoundLibraryEditorWindow
{
    private string _editColor = "";
    private int _editCrystalSystem;
    private float _editDensity;
    private float _editEnthalpyFormation;
    private float _editEntropy;
    private string _editFormula = "";

    // Thermodynamic property buffers
    private float _editGibbsEnergy;
    private bool _editHasCrystalSystem = true;
    private float _editHeatCapacity;
    private ChemicalCompound? _editingCompound;
    private int _editIonicCharge;
    private float _editLogKsp;
    private float _editMohsHardness;
    private float _editMolarVolume;
    private float _editMolecularWeight;

    // Edit buffers for new/edited compounds
    private string _editName = "";
    private string _editNotes = "";
    private int _editPhase;
    private float _editSolubility;
    private string _editSources = "";
    private bool _hasDensity;
    private bool _hasEnthalpyFormation;
    private bool _hasEntropy;
    private bool _hasGibbsEnergy;
    private bool _hasHeatCapacity;
    private bool _hasIonicCharge;
    private bool _hasLogKsp;
    private bool _hasMohsHardness;
    private bool _hasMolarVolume;
    private bool _hasMolecularWeight;
    private bool _hasSolubility;
    private bool _isOpen;
    private string _searchFilter = "";
    private ChemicalCompound? _selectedCompound;

    // Element selection
    private Element? _selectedElement;
    private int _selectedTabIndex; // 0=Compounds, 1=Elements
    private bool _showAddCompoundDialog;
    private bool _showOnlyAqueous;
    private bool _showOnlySolids;
    private string _statusMessage = "";
    private float _statusMessageTimer;

    public bool IsOpen
    {
        get => _isOpen;
        set => _isOpen = value;
    }

    public void Show()
    {
        _isOpen = true;
    }

    public void Draw()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(1200, 800), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Compound & Element Library", ref _isOpen, ImGuiWindowFlags.NoCollapse))
        {
            // Update status message timer
            if (_statusMessageTimer > 0f)
            {
                _statusMessageTimer -= ImGui.GetIO().DeltaTime;
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), _statusMessage);
                ImGui.Spacing();
            }

            // Tab bar for Compounds vs Elements
            if (ImGui.BeginTabBar("LibraryTabs"))
            {
                if (ImGui.BeginTabItem("Compounds"))
                {
                    _selectedTabIndex = 0;
                    DrawCompoundsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Elements"))
                {
                    _selectedTabIndex = 1;
                    DrawElementsTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        ImGui.End();

        // Dialogs
        if (_showAddCompoundDialog)
            DrawAddCompoundDialog();
    }

    private void DrawCompoundsTab()
    {
        ImGui.TextWrapped("Browse, search, and manage compounds used for dissolution/precipitation calculations.");

        // Control bar
        DrawCompoundControlBar();
        ImGui.Separator();

        // Two-column layout: list on left, details on right
        if (ImGui.BeginTable("CompoundLibraryTable", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Compounds", ImGuiTableColumnFlags.WidthFixed, 350);
            ImGui.TableSetupColumn("Properties", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawCompoundList();

            ImGui.TableNextColumn();
            DrawCompoundDetails();

            ImGui.EndTable();
        }
    }

    private void DrawElementsTab()
    {
        ImGui.TextWrapped("Periodic table of elements with atomic properties for chemical calculations.");
        ImGui.Separator();

        // Two-column layout: list on left, details on right
        if (ImGui.BeginTable("ElementLibraryTable", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Elements", ImGuiTableColumnFlags.WidthFixed, 350);
            ImGui.TableSetupColumn("Properties", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawElementList();

            ImGui.TableNextColumn();
            DrawElementDetails();

            ImGui.EndTable();
        }
    }

    private void DrawElementList()
    {
        ImGui.Text("Periodic Table:");
        ImGui.Separator();

        if (ImGui.BeginChild("ElementList", new Vector2(0, 0), ImGuiChildFlags.Border))
        {
            var elements = CompoundLibrary.Instance.Elements
                .OrderBy(e => e.AtomicNumber)
                .ToList();

            var currentGroup = "";
            foreach (var element in elements)
            {
                // Group by element type
                var group = element.ElementType.ToString();
                if (group != currentGroup)
                {
                    currentGroup = group;
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.8f, 1f, 1f));
                    ImGui.SeparatorText(currentGroup);
                    ImGui.PopStyleColor();
                }

                var isSelected = _selectedElement == element;
                var displayName = $"{element.Symbol} ({element.AtomicNumber}) - {element.Name}";

                if (ImGui.Selectable(displayName, isSelected)) _selectedElement = element;

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Atomic Mass: {element.AtomicMass:F4} u");
                    if (element.Electronegativity.HasValue)
                        ImGui.Text($"Electronegativity: {element.Electronegativity:F2}");
                    ImGui.EndTooltip();
                }
            }
        }

        ImGui.EndChild();
    }

    private void DrawElementDetails()
    {
        if (_selectedElement == null)
        {
            ImGui.TextDisabled("Select an element to view properties");
            return;
        }

        var e = _selectedElement;

        ImGui.BeginChild("ElementDetails", new Vector2(0, 0), ImGuiChildFlags.Border);

        // Header
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 1f, 1f, 1f));
        ImGui.Text($"{e.Name} ({e.Symbol})");
        ImGui.PopStyleColor();
        ImGui.Text($"Atomic Number: {e.AtomicNumber}");
        ImGui.Text($"Element Type: {e.ElementType}");

        ImGui.Separator();

        if (ImGui.BeginTabBar("ElementTabs"))
        {
            if (ImGui.BeginTabItem("Basic Properties"))
            {
                if (ImGui.BeginTable("BasicProps", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                    ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 200);
                    ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                    void AddRow(string property, string value)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(property);
                        ImGui.TableNextColumn();
                        ImGui.Text(value);
                    }

                    AddRow("Atomic Mass", $"{e.AtomicMass:F6} u");
                    AddRow("Group", e.Group.ToString());
                    AddRow("Period", e.Period.ToString());

                    if (e.Electronegativity.HasValue)
                        AddRow("Electronegativity (Pauling)", $"{e.Electronegativity:F2}");

                    if (e.ValenceElectrons.HasValue)
                        AddRow("Valence Electrons", e.ValenceElectrons.ToString());

                    if (!string.IsNullOrEmpty(e.ElectronConfiguration))
                        AddRow("Electron Configuration", e.ElectronConfiguration);

                    ImGui.EndTable();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Atomic Radii"))
            {
                if (ImGui.BeginTable("RadiiProps", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                    ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 200);
                    ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                    void AddRow(string property, string value)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(property);
                        ImGui.TableNextColumn();
                        ImGui.Text(value);
                    }

                    if (e.AtomicRadius_pm.HasValue)
                        AddRow("Atomic Radius", $"{e.AtomicRadius_pm} pm");

                    if (e.CovalentRadius_pm.HasValue)
                        AddRow("Covalent Radius", $"{e.CovalentRadius_pm} pm");

                    if (e.VanDerWaalsRadius_pm.HasValue)
                        AddRow("Van der Waals Radius", $"{e.VanDerWaalsRadius_pm} pm");

                    if (e.IonicRadii.Any())
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text("Ionic Radii");
                        ImGui.TableNextColumn();
                        foreach (var kvp in e.IonicRadii) ImGui.Text($"{kvp.Key:+#;-#;0}: {kvp.Value} pm");
                    }

                    ImGui.EndTable();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Chemical"))
            {
                if (ImGui.BeginTable("ChemProps", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                    ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 200);
                    ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                    void AddRow(string property, string value)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(property);
                        ImGui.TableNextColumn();
                        ImGui.Text(value);
                    }

                    if (e.OxidationStates.Any())
                        AddRow("Oxidation States",
                            string.Join(", ", e.OxidationStates.Select(x => x > 0 ? $"+{x}" : x.ToString())));

                    if (e.FirstIonizationEnergy_kJ_mol.HasValue)
                        AddRow("1st Ionization Energy", $"{e.FirstIonizationEnergy_kJ_mol:F2} kJ/mol");

                    if (e.ElectronAffinity_kJ_mol.HasValue)
                        AddRow("Electron Affinity", $"{e.ElectronAffinity_kJ_mol:F2} kJ/mol");

                    ImGui.EndTable();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Physical"))
            {
                if (ImGui.BeginTable("PhysProps", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                    ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 200);
                    ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                    void AddRow(string property, string value)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(property);
                        ImGui.TableNextColumn();
                        ImGui.Text(value);
                    }

                    if (e.MeltingPoint_K.HasValue)
                        AddRow("Melting Point", $"{e.MeltingPoint_K:F2} K ({e.MeltingPoint_K - 273.15:F2} degC)");

                    if (e.BoilingPoint_K.HasValue)
                        AddRow("Boiling Point", $"{e.BoilingPoint_K:F2} K ({e.BoilingPoint_K - 273.15:F2} degC)");

                    if (e.Density_g_cm3.HasValue)
                        AddRow("Density", $"{e.Density_g_cm3:F3} g/cm3");

                    if (e.ThermalConductivity_W_mK.HasValue)
                        AddRow("Thermal Conductivity", $"{e.ThermalConductivity_W_mK:F2} W/m*K");

                    ImGui.EndTable();
                }

                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.EndChild();
    }

    private void DrawCompoundControlBar()
    {
        // Search
        ImGui.SetNextItemWidth(250);
        ImGui.InputTextWithHint("##search", "Search compounds...", ref _searchFilter, 256);

        ImGui.SameLine();
        ImGui.Checkbox("Solids Only", ref _showOnlySolids);
        ImGui.SameLine();
        ImGui.Checkbox("Aqueous Only", ref _showOnlyAqueous);

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(20, 0));
        ImGui.SameLine();

        if (ImGui.Button("Add New Compound", new Vector2(150, 0)))
        {
            PrepareNewCompound();
            _showAddCompoundDialog = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Load Library", new Vector2(120, 0)))
        {
            if (CompoundLibrary.Instance.Load())
                ShowStatus("Library loaded successfully");
            else
                ShowStatus("Failed to load library");
        }

        ImGui.SameLine();
        if (ImGui.Button("Save Library", new Vector2(120, 0)))
        {
            if (CompoundLibrary.Instance.Save())
                ShowStatus("Library saved successfully");
            else
                ShowStatus("Failed to save library");
        }

        ImGui.SameLine();
        ImGui.Text($"({CompoundLibrary.Instance.Compounds.Count} compounds)");
    }

    private void DrawCompoundList()
    {
        ImGui.Text("Compounds:");
        ImGui.Separator();

        if (ImGui.BeginChild("CompoundList", new Vector2(0, 0), ImGuiChildFlags.Border))
        {
            var compounds = CompoundLibrary.Instance.Compounds
                .Where(c => PassesFilter(c))
                .OrderBy(c => c.Phase)
                .ThenBy(c => c.Name)
                .ToList();

            var currentPhase = "";
            foreach (var compound in compounds)
            {
                if (compound.Phase.ToString() != currentPhase)
                {
                    currentPhase = compound.Phase.ToString();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.8f, 1f, 1f));
                    ImGui.SeparatorText(currentPhase);
                    ImGui.PopStyleColor();
                }

                var isSelected = _selectedCompound == compound;
                var displayName = $"{compound.Name}";
                if (!string.IsNullOrEmpty(compound.ChemicalFormula))
                    displayName += $" ({compound.ChemicalFormula})";

                if (ImGui.Selectable(displayName, isSelected)) _selectedCompound = compound;

                if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(compound.Notes))
                {
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(400);
                    ImGui.TextWrapped(compound.Notes);
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }
            }
        }

        ImGui.EndChild();
    }

    private void DrawCompoundDetails()
    {
        if (_selectedCompound == null)
        {
            ImGui.TextDisabled("Select a compound to view properties");
            return;
        }

        var c = _selectedCompound;

        ImGui.BeginChild("CompoundDetails", new Vector2(0, 0), ImGuiChildFlags.Border);

        // Header
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 1f, 1f, 1f));
        ImGui.Text(c.Name);
        ImGui.PopStyleColor();
        if (!string.IsNullOrEmpty(c.ChemicalFormula))
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({c.ChemicalFormula})");
        }

        ImGui.Spacing();
        ImGui.Text($"Phase: {c.Phase}");
        if (c.CrystalSystem.HasValue)
            ImGui.Text($"Crystal System: {c.CrystalSystem}");

        // Action buttons
        ImGui.Spacing();
        if (ImGui.Button("Edit Compound", new Vector2(130, 0)))
        {
            PrepareEditCompound(c);
            _showAddCompoundDialog = true;
        }

        ImGui.SameLine();
        if (c.IsUserCompound)
        {
            if (ImGui.Button("Delete", new Vector2(80, 0)))
                if (ImGui.IsKeyDown(ImGuiKey.LeftCtrl))
                {
                    CompoundLibrary.Instance.Remove(c.Name);
                    _selectedCompound = null;
                    ShowStatus($"Deleted {c.Name}");
                }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hold Ctrl and click to delete");
        }

        ImGui.Separator();

        // Tabbed property display
        if (ImGui.BeginTabBar("CompoundTabs"))
        {
            if (ImGui.BeginTabItem("Thermodynamic"))
            {
                DrawThermodynamicProperties(c);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Solubility"))
            {
                DrawSolubilityProperties(c);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Kinetics"))
            {
                DrawKineticProperties(c);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Physical"))
            {
                DrawPhysicalProperties(c);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Sources"))
            {
                DrawSources(c);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.EndChild();
    }

    private void DrawThermodynamicProperties(ChemicalCompound c)
    {
        ImGui.Spacing();
        ImGui.Text("Standard State Properties (298.15 K, 1 bar):");
        ImGui.Separator();

        if (ImGui.BeginTable("ThermoProps", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            void AddRow(string property, string value)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(property);
                ImGui.TableNextColumn();
                ImGui.Text(value);
            }

            if (c.GibbsFreeEnergyFormation_kJ_mol.HasValue)
                AddRow("DeltaGdegf", $"{c.GibbsFreeEnergyFormation_kJ_mol:F2} kJ/mol");

            if (c.EnthalpyFormation_kJ_mol.HasValue)
                AddRow("DeltaHdegf", $"{c.EnthalpyFormation_kJ_mol:F2} kJ/mol");

            if (c.Entropy_J_molK.HasValue)
                AddRow("Sdeg", $"{c.Entropy_J_molK:F2} J/mol*K");

            if (c.HeatCapacity_J_molK.HasValue)
                AddRow("Cp", $"{c.HeatCapacity_J_molK:F2} J/mol*K");

            if (c.MolarVolume_cm3_mol.HasValue)
                AddRow("Vm", $"{c.MolarVolume_cm3_mol:F3} cm3/mol");

            if (c.MolecularWeight_g_mol.HasValue)
                AddRow("Molecular Weight", $"{c.MolecularWeight_g_mol:F2} g/mol");

            if (c.Density_g_cm3.HasValue)
                AddRow("Density", $"{c.Density_g_cm3:F3} g/cm3");

            if (c.HeatCapacityPolynomial_a_b_c_d != null && c.HeatCapacityPolynomial_a_b_c_d.Length >= 4)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Cp(T) Polynomial");
                ImGui.TableNextColumn();
                ImGui.Text($"a={c.HeatCapacityPolynomial_a_b_c_d[0]:E3}");
                ImGui.Text($"b={c.HeatCapacityPolynomial_a_b_c_d[1]:E3}");
                ImGui.Text($"c={c.HeatCapacityPolynomial_a_b_c_d[2]:E3}");
                ImGui.Text($"d={c.HeatCapacityPolynomial_a_b_c_d[3]:E3}");
            }

            ImGui.EndTable();
        }
    }

    private void DrawSolubilityProperties(ChemicalCompound c)
    {
        ImGui.Spacing();
        ImGui.Text("Solubility & Equilibrium:");
        ImGui.Separator();

        if (ImGui.BeginTable("SolubProps", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            void AddRow(string property, string value)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(property);
                ImGui.TableNextColumn();
                ImGui.Text(value);
            }

            if (c.LogKsp_25C.HasValue)
            {
                AddRow("log Ksp (25degC)", $"{c.LogKsp_25C:F2}");
                var ksp = Math.Pow(10, c.LogKsp_25C.Value);
                AddRow("Ksp", $"{ksp:E3}");
            }

            if (c.Solubility_g_100mL_25C.HasValue)
                AddRow("Solubility (25degC)", $"{c.Solubility_g_100mL_25C:F4} g/100mL");

            if (c.DissolutionEnthalpy_kJ_mol.HasValue)
                AddRow("DeltaHdissolution", $"{c.DissolutionEnthalpy_kJ_mol:F2} kJ/mol");

            if (c.IonicCharge.HasValue)
                AddRow("Ionic Charge", $"{c.IonicCharge:+#;-#;0}");

            if (c.IonicConductivity_S_cm2_mol.HasValue)
                AddRow("Ionic Conductivity", $"{c.IonicConductivity_S_cm2_mol:F2} S*cm2/mol");

            ImGui.EndTable();
        }
    }

    private void DrawKineticProperties(ChemicalCompound c)
    {
        ImGui.Spacing();
        ImGui.Text("Dissolution/Precipitation Kinetics:");
        ImGui.Separator();

        if (ImGui.BeginTable("KineticProps", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 220);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            void AddRow(string property, string value)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(property);
                ImGui.TableNextColumn();
                ImGui.Text(value);
            }

            if (c.ActivationEnergy_Dissolution_kJ_mol.HasValue)
                AddRow("Ea (dissolution)", $"{c.ActivationEnergy_Dissolution_kJ_mol:F2} kJ/mol");

            if (c.ActivationEnergy_Precipitation_kJ_mol.HasValue)
                AddRow("Ea (precipitation)", $"{c.ActivationEnergy_Precipitation_kJ_mol:F2} kJ/mol");

            if (c.RateConstant_Dissolution_mol_m2_s.HasValue)
                AddRow("k (dissolution)", $"{c.RateConstant_Dissolution_mol_m2_s:E3} mol/m2/s");

            if (c.RateConstant_Precipitation_mol_m2_s.HasValue)
                AddRow("k (precipitation)", $"{c.RateConstant_Precipitation_mol_m2_s:E3} mol/m2/s");

            if (c.ReactionOrder_Dissolution.HasValue)
                AddRow("Reaction Order", $"{c.ReactionOrder_Dissolution:F2}");

            if (c.SpecificSurfaceArea_m2_g.HasValue)
                AddRow("Specific Surface Area", $"{c.SpecificSurfaceArea_m2_g:F3} m2/g");

            ImGui.EndTable();
        }
    }

    private void DrawPhysicalProperties(ChemicalCompound c)
    {
        ImGui.Spacing();
        ImGui.Text("Physical & Mineralogical:");
        ImGui.Separator();

        if (ImGui.BeginTable("PhysProps", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            void AddRow(string property, string value)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(property);
                ImGui.TableNextColumn();
                ImGui.Text(value);
            }

            if (c.MohsHardness.HasValue)
                AddRow("Mohs Hardness", $"{c.MohsHardness:F1}");

            if (!string.IsNullOrEmpty(c.Color))
                AddRow("Color", c.Color);

            if (!string.IsNullOrEmpty(c.Cleavage))
                AddRow("Cleavage", c.Cleavage);

            if (c.RefractiveIndex.HasValue)
                AddRow("Refractive Index", $"{c.RefractiveIndex:F3}");

            if (c.Synonyms.Any())
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Synonyms");
                ImGui.TableNextColumn();
                ImGui.TextWrapped(string.Join(", ", c.Synonyms));
            }

            ImGui.EndTable();
        }

        if (!string.IsNullOrEmpty(c.Notes))
        {
            ImGui.Spacing();
            ImGui.SeparatorText("Notes");
            ImGui.PushTextWrapPos();
            ImGui.TextWrapped(c.Notes);
            ImGui.PopTextWrapPos();
        }
    }

    private void DrawSources(ChemicalCompound c)
    {
        ImGui.Spacing();
        if (c.Sources.Any())
        {
            ImGui.Text("Literature Sources:");
            ImGui.Separator();
            ImGui.PushTextWrapPos();
            foreach (var source in c.Sources)
            {
                ImGui.BulletText(source);
                ImGui.Spacing();
            }

            ImGui.PopTextWrapPos();
        }
        else
        {
            ImGui.TextDisabled("No sources cited");
        }
    }

    private void DrawAddCompoundDialog()
    {
        ImGui.SetNextWindowSize(new Vector2(700, 800), ImGuiCond.FirstUseEver);
        var isOpen = true;

        var title = _editingCompound == null ? "Add New Compound" : $"Edit: {_editingCompound.Name}";

        if (ImGui.Begin(title, ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.TextWrapped("Enter compound properties. Leave fields blank if unknown.");
            ImGui.Separator();

            // Basic info
            ImGui.SeparatorText("Basic Information");
            ImGui.InputText("Name", ref _editName, 256);
            ImGui.InputText("Chemical Formula", ref _editFormula, 256);

            ImGui.SetNextItemWidth(200);
            var phaseNames = Enum.GetNames(typeof(CompoundPhase));
            ImGui.Combo("Phase", ref _editPhase, phaseNames, phaseNames.Length);

            if (_editPhase == 0) // Solid
            {
                ImGui.Checkbox("Has Crystal System", ref _editHasCrystalSystem);
                if (_editHasCrystalSystem)
                {
                    ImGui.SetNextItemWidth(200);
                    var crystalNames = Enum.GetNames(typeof(CrystalSystem));
                    ImGui.Combo("Crystal System", ref _editCrystalSystem, crystalNames, crystalNames.Length);
                }
            }

            // Thermodynamic properties
            ImGui.Spacing();
            ImGui.SeparatorText("Thermodynamic Properties (298.15 K, 1 bar)");

            DrawOptionalFloatInput("Gibbs Free Energy (kJ/mol)", ref _editGibbsEnergy, ref _hasGibbsEnergy);
            DrawOptionalFloatInput("Enthalpy of Formation (kJ/mol)", ref _editEnthalpyFormation,
                ref _hasEnthalpyFormation);
            DrawOptionalFloatInput("Entropy (J/mol*K)", ref _editEntropy, ref _hasEntropy);
            DrawOptionalFloatInput("Heat Capacity (J/mol*K)", ref _editHeatCapacity, ref _hasHeatCapacity);
            DrawOptionalFloatInput("Molar Volume (cm3/mol)", ref _editMolarVolume, ref _hasMolarVolume);
            DrawOptionalFloatInput("Molecular Weight (g/mol)", ref _editMolecularWeight, ref _hasMolecularWeight);
            DrawOptionalFloatInput("Density (g/cm3)", ref _editDensity, ref _hasDensity);

            // Solubility
            ImGui.Spacing();
            ImGui.SeparatorText("Solubility & Equilibrium");
            DrawOptionalFloatInput("log Ksp (25degC)", ref _editLogKsp, ref _hasLogKsp);
            DrawOptionalFloatInput("Solubility (g/100mL, 25degC)", ref _editSolubility, ref _hasSolubility);

            if (_editPhase == 1) // Aqueous
                DrawOptionalIntInput("Ionic Charge", ref _editIonicCharge, ref _hasIonicCharge);

            // Physical
            ImGui.Spacing();
            ImGui.SeparatorText("Physical Properties");
            DrawOptionalFloatInput("Mohs Hardness", ref _editMohsHardness, ref _hasMohsHardness, 0f, 10f);
            ImGui.InputText("Color", ref _editColor, 256);

            // Metadata
            ImGui.Spacing();
            ImGui.SeparatorText("Metadata");
            ImGui.InputTextMultiline("Notes", ref _editNotes, 2000, new Vector2(-1, 80));
            ImGui.InputTextMultiline("Sources (one per line)", ref _editSources, 4000, new Vector2(-1, 100));

            ImGui.Separator();

            // Buttons
            if (ImGui.Button("Save", new Vector2(120, 0)))
                if (SaveCompound())
                {
                    _showAddCompoundDialog = false;
                    ShowStatus(_editingCompound == null ? "Compound added" : "Compound updated");
                }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) _showAddCompoundDialog = false;
        }

        ImGui.End();

        if (!isOpen)
            _showAddCompoundDialog = false;
    }

    private void DrawOptionalFloatInput(string label, ref float value, ref bool hasValue, float min = float.MinValue,
        float max = float.MaxValue)
    {
        ImGui.Checkbox($"##{label}_check", ref hasValue);
        ImGui.SameLine();
        ImGui.BeginDisabled(!hasValue);
        ImGui.SetNextItemWidth(150);
        ImGui.DragFloat(label, ref value, 0.1f, min, max, "%.4f");
        ImGui.EndDisabled();
    }

    private void DrawOptionalIntInput(string label, ref int value, ref bool hasValue)
    {
        ImGui.Checkbox($"##{label}_check", ref hasValue);
        ImGui.SameLine();
        ImGui.BeginDisabled(!hasValue);
        ImGui.SetNextItemWidth(150);
        ImGui.DragInt(label, ref value);
        ImGui.EndDisabled();
    }

    private bool SaveCompound()
    {
        if (string.IsNullOrWhiteSpace(_editName))
        {
            ShowStatus("Error: Name is required");
            return false;
        }

        var compound = _editingCompound ?? new ChemicalCompound();

        compound.Name = _editName.Trim();
        compound.ChemicalFormula = _editFormula.Trim();
        compound.Phase = (CompoundPhase)_editPhase;

        if (_editPhase == 0 && _editHasCrystalSystem)
            compound.CrystalSystem = (CrystalSystem)_editCrystalSystem;
        else
            compound.CrystalSystem = null;

        compound.GibbsFreeEnergyFormation_kJ_mol = _hasGibbsEnergy ? _editGibbsEnergy : null;
        compound.EnthalpyFormation_kJ_mol = _hasEnthalpyFormation ? _editEnthalpyFormation : null;
        compound.Entropy_J_molK = _hasEntropy ? _editEntropy : null;
        compound.HeatCapacity_J_molK = _hasHeatCapacity ? _editHeatCapacity : null;
        compound.MolarVolume_cm3_mol = _hasMolarVolume ? _editMolarVolume : null;
        compound.MolecularWeight_g_mol = _hasMolecularWeight ? _editMolecularWeight : null;
        compound.Density_g_cm3 = _hasDensity ? _editDensity : null;
        compound.LogKsp_25C = _hasLogKsp ? _editLogKsp : null;
        compound.Solubility_g_100mL_25C = _hasSolubility ? _editSolubility : null;
        compound.MohsHardness = _hasMohsHardness ? _editMohsHardness : null;
        compound.IonicCharge = _hasIonicCharge ? _editIonicCharge : null;
        compound.Color = _editColor;
        compound.Notes = _editNotes;

        compound.Sources.Clear();
        if (!string.IsNullOrWhiteSpace(_editSources))
        {
            var sources = _editSources.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            compound.Sources.AddRange(sources.Select(s => s.Trim()));
        }

        CompoundLibrary.Instance.AddOrUpdate(compound);
        _selectedCompound = compound;

        return true;
    }

    private void PrepareNewCompound()
    {
        _editingCompound = null;
        _editName = "";
        _editFormula = "";
        _editPhase = 0;
        _editCrystalSystem = 0;
        _editHasCrystalSystem = true;
        _editNotes = "";
        _editSources = "";
        _editColor = "";

        _editGibbsEnergy = 0f;
        _hasGibbsEnergy = false;
        _editEnthalpyFormation = 0f;
        _hasEnthalpyFormation = false;
        _editEntropy = 0f;
        _hasEntropy = false;
        _editHeatCapacity = 0f;
        _hasHeatCapacity = false;
        _editMolarVolume = 0f;
        _hasMolarVolume = false;
        _editMolecularWeight = 0f;
        _hasMolecularWeight = false;
        _editDensity = 0f;
        _hasDensity = false;
        _editLogKsp = 0f;
        _hasLogKsp = false;
        _editSolubility = 0f;
        _hasSolubility = false;
        _editMohsHardness = 0f;
        _hasMohsHardness = false;
        _editIonicCharge = 0;
        _hasIonicCharge = false;
    }

    private void PrepareEditCompound(ChemicalCompound compound)
    {
        _editingCompound = compound;
        _editName = compound.Name;
        _editFormula = compound.ChemicalFormula;
        _editPhase = (int)compound.Phase;
        _editCrystalSystem = compound.CrystalSystem.HasValue ? (int)compound.CrystalSystem.Value : 0;
        _editHasCrystalSystem = compound.CrystalSystem.HasValue;
        _editNotes = compound.Notes;
        _editSources = string.Join("\n", compound.Sources);
        _editColor = compound.Color;

        _editGibbsEnergy = (float)(compound.GibbsFreeEnergyFormation_kJ_mol ?? 0);
        _hasGibbsEnergy = compound.GibbsFreeEnergyFormation_kJ_mol.HasValue;
        _editEnthalpyFormation = (float)(compound.EnthalpyFormation_kJ_mol ?? 0);
        _hasEnthalpyFormation = compound.EnthalpyFormation_kJ_mol.HasValue;
        _editEntropy = (float)(compound.Entropy_J_molK ?? 0);
        _hasEntropy = compound.Entropy_J_molK.HasValue;
        _editHeatCapacity = (float)(compound.HeatCapacity_J_molK ?? 0);
        _hasHeatCapacity = compound.HeatCapacity_J_molK.HasValue;
        _editMolarVolume = (float)(compound.MolarVolume_cm3_mol ?? 0);
        _hasMolarVolume = compound.MolarVolume_cm3_mol.HasValue;
        _editMolecularWeight = (float)(compound.MolecularWeight_g_mol ?? 0);
        _hasMolecularWeight = compound.MolecularWeight_g_mol.HasValue;
        _editDensity = (float)(compound.Density_g_cm3 ?? 0);
        _hasDensity = compound.Density_g_cm3.HasValue;
        _editLogKsp = (float)(compound.LogKsp_25C ?? 0);
        _hasLogKsp = compound.LogKsp_25C.HasValue;
        _editSolubility = (float)(compound.Solubility_g_100mL_25C ?? 0);
        _hasSolubility = compound.Solubility_g_100mL_25C.HasValue;
        _editMohsHardness = (float)(compound.MohsHardness ?? 0);
        _hasMohsHardness = compound.MohsHardness.HasValue;
        _editIonicCharge = compound.IonicCharge ?? 0;
        _hasIonicCharge = compound.IonicCharge.HasValue;
    }

    private bool PassesFilter(ChemicalCompound c)
    {
        if (_showOnlySolids && c.Phase != CompoundPhase.Solid)
            return false;
        if (_showOnlyAqueous && c.Phase != CompoundPhase.Aqueous)
            return false;

        if (string.IsNullOrWhiteSpace(_searchFilter))
            return true;

        var filter = _searchFilter.ToLower();
        return c.Name.ToLower().Contains(filter) ||
               c.ChemicalFormula.ToLower().Contains(filter) ||
               c.Notes.ToLower().Contains(filter) ||
               c.Synonyms.Any(s => s.ToLower().Contains(filter));
    }

    private void ShowStatus(string message)
    {
        _statusMessage = message;
        _statusMessageTimer = 3f;
        Logger.Log($"[CompoundLibraryEditor] {message}");
    }
}