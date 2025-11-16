// GeoscientistToolkit/UI/Windows/ORCFluidEditorWindow.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Windows;

/// <summary>
/// Window for browsing, editing, and managing ORC working fluids for power generation simulations
/// </summary>
public class ORCFluidEditorWindow
{
    private bool _isOpen;
    private string _searchFilter = "";
    private ORCFluid? _selectedFluid;
    private bool _showAddFluidDialog;
    private string _statusMessage = "";
    private float _statusMessageTimer;

    // Filter options
    private bool _showOnlyUserFluids;
    private int _filterCategory = -1; // -1 = all

    // Edit buffers for new/edited fluids
    private ORCFluid? _editingFluid;
    private string _editName = "";
    private string _editFormula = "";
    private string _editRefCode = "";
    private int _editCategory;
    private int _editSafety;
    private bool _editIsNatural;

    // Critical properties
    private float _editTcrit;
    private float _editPcrit;
    private float _editRhoCrit;

    // Triple point
    private float _editTtriple;
    private float _editPtriple;

    // Molecular
    private float _editMW;
    private float _editOmega;

    // Environmental
    private float _editODP;
    private float _editGWP;
    private float _editLifetime;

    // Antoine coefficients
    private float _editAntoineA;
    private float _editAntoineB;
    private float _editAntoineC;
    private float _editAntoineTmin;
    private float _editAntoineTmax;

    // Recommended parameters
    private float _editRecEvapPress;
    private float _editRecCondTemp;
    private float _editMinTemp;
    private float _editMaxTemp;

    private string _editNotes = "";
    private string _editManufacturer = "";

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

        ImGui.SetNextWindowSize(new Vector2(1400, 900), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("ORC Working Fluid Library", ref _isOpen, ImGuiWindowFlags.NoCollapse))
        {
            // Update status message timer
            if (_statusMessageTimer > 0f)
            {
                _statusMessageTimer -= ImGui.GetIO().DeltaTime;
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), _statusMessage);
                ImGui.Spacing();
            }

            // Header
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 1.0f, 1.0f), "ORC Working Fluid Database");
            ImGui.TextWrapped("Comprehensive library of organic Rankine cycle working fluids for geothermal and waste heat power generation.");
            ImGui.Separator();
            ImGui.Spacing();

            // Main layout: list on left, details on right
            if (ImGui.BeginTable("FluidBrowserLayout", 2, ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Fluid List", ImGuiTableColumnFlags.WidthFixed, 400);
                ImGui.TableSetupColumn("Fluid Details", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                DrawFluidList();

                ImGui.TableNextColumn();
                DrawFluidDetails();

                ImGui.EndTable();
            }

            // Add fluid dialog
            if (_showAddFluidDialog)
            {
                DrawAddFluidDialog();
            }
        }

        ImGui.End();
    }

    private void DrawFluidList()
    {
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), "Fluid Library");
        ImGui.Separator();

        // Search and filters
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search fluids...", ref _searchFilter, 100);

        ImGui.Checkbox("User fluids only", ref _showOnlyUserFluids);

        string[] categories = { "All Categories", "Low Temp (<100°C)", "Medium Temp (100-200°C)", "High Temp (>200°C)", "Cryogenic" };
        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##category", ref _filterCategory, categories, categories.Length);

        ImGui.Spacing();
        ImGui.Separator();

        // Add button
        if (ImGui.Button("+ Add New Fluid", new Vector2(-1, 30)))
        {
            _showAddFluidDialog = true;
            InitializeNewFluidBuffers();
        }

        ImGui.Spacing();

        // Fluid list
        var library = ORCFluidLibrary.Instance;
        var fluids = library.AllFluids.AsEnumerable();

        // Apply filters
        if (_showOnlyUserFluids)
            fluids = fluids.Where(f => f.IsUserFluid);

        if (_filterCategory >= 1)
            fluids = fluids.Where(f => (int)f.Category == _filterCategory - 1);

        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            var filter = _searchFilter.ToLower();
            fluids = fluids.Where(f =>
                f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                f.RefrigerantCode.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                f.ChemicalFormula.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var fluidList = fluids.ToList();

        ImGui.BeginChild("FluidScrollRegion", new Vector2(-1, -1), ImGuiChildFlags.Border);

        foreach (var fluid in fluidList)
        {
            bool isSelected = _selectedFluid == fluid;

            // Color coding by category
            Vector4 color = fluid.Category switch
            {
                FluidCategory.LowTemperature => new Vector4(0.3f, 0.7f, 1.0f, 1.0f),
                FluidCategory.MediumTemperature => new Vector4(1.0f, 0.8f, 0.3f, 1.0f),
                FluidCategory.HighTemperature => new Vector4(1.0f, 0.4f, 0.3f, 1.0f),
                _ => new Vector4(0.7f, 0.7f, 0.7f, 1.0f)
            };

            ImGui.PushStyleColor(ImGuiCol.Text, color);

            string displayName = $"{fluid.RefrigerantCode} - {fluid.Name}";
            if (fluid.IsUserFluid)
                displayName += " [User]";

            if (ImGui.Selectable(displayName, isSelected))
            {
                _selectedFluid = fluid;
            }

            ImGui.PopStyleColor();

            // Safety and temp range info
            if (isSelected || ImGui.IsItemHovered())
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                    $"   Safety: {fluid.Safety}, Temp: {fluid.MinimumTemperature_K - 273.15f:F0}-{fluid.MaximumTemperature_K - 273.15f:F0}°C, GWP: {fluid.GWP100:F0}");
                ImGui.Unindent();
            }
        }

        ImGui.EndChild();
    }

    private void DrawFluidDetails()
    {
        if (_selectedFluid == null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Select a fluid to view details");
            return;
        }

        var fluid = _selectedFluid;

        ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.4f, 1.0f), fluid.Name);
        ImGui.SameLine();
        if (fluid.IsUserFluid)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.2f, 1.0f), "[User Fluid]");
        }

        ImGui.Separator();

        // Action buttons
        if (ImGui.Button("Edit", new Vector2(80, 25)))
        {
            _editingFluid = fluid;
            InitializeEditBuffersFromFluid(fluid);
            _showAddFluidDialog = true;
        }

        ImGui.SameLine();
        if (fluid.IsUserFluid && ImGui.Button("Delete", new Vector2(80, 25)))
        {
            ORCFluidLibrary.Instance.RemoveFluid(fluid.Name);
            _selectedFluid = null;
            SetStatusMessage($"Deleted fluid: {fluid.Name}");
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy to User Fluid", new Vector2(150, 25)))
        {
            var copy = CloneFluid(fluid);
            copy.Name = fluid.Name + " (Copy)";
            copy.IsUserFluid = true;
            ORCFluidLibrary.Instance.AddFluid(copy);
            SetStatusMessage($"Created copy: {copy.Name}");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Tabs for different property categories
        if (ImGui.BeginTabBar("FluidDetailsTabs"))
        {
            if (ImGui.BeginTabItem("Overview"))
            {
                DrawOverviewTab(fluid);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Thermodynamic Properties"))
            {
                DrawThermodynamicTab(fluid);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Environmental & Safety"))
            {
                DrawEnvironmentalTab(fluid);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Applications"))
            {
                DrawApplicationsTab(fluid);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawOverviewTab(ORCFluid fluid)
    {
        ImGui.Columns(2);

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1.0f, 1.0f), "Basic Information:");
        ImGui.BulletText($"Refrigerant Code: {fluid.RefrigerantCode}");
        ImGui.BulletText($"Chemical Formula: {fluid.ChemicalFormula}");
        ImGui.BulletText($"Category: {fluid.Category}");
        ImGui.BulletText($"Safety Class (ASHRAE): {fluid.Safety}");
        ImGui.BulletText($"Natural/Synthetic: {(fluid.IsNaturalFluid ? "Natural" : "Synthetic")}");

        ImGui.NextColumn();

        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.6f, 1.0f), "Operating Range:");
        ImGui.BulletText($"Min Temperature: {fluid.MinimumTemperature_K - 273.15f:F1}°C");
        ImGui.BulletText($"Max Temperature: {fluid.MaximumTemperature_K - 273.15f:F1}°C");
        ImGui.BulletText($"Recommended Evap. Press: {fluid.RecommendedEvaporatorPressure_Pa / 1e5f:F1} bar");
        ImGui.BulletText($"Recommended Cond. Temp: {fluid.RecommendedCondenserTemperature_K - 273.15f:F1}°C");

        ImGui.Columns(1);
        ImGui.Separator();

        if (!string.IsNullOrEmpty(fluid.Notes))
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1.0f), "Notes:");
            ImGui.TextWrapped(fluid.Notes);
        }
    }

    private void DrawThermodynamicTab(ORCFluid fluid)
    {
        if (ImGui.BeginTable("ThermoProps", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 250);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            // Critical properties
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.4f, 1.0f), "Critical Temperature");
            ImGui.TableNextColumn();
            ImGui.Text($"{fluid.CriticalTemperature_K:F2} K ({fluid.CriticalTemperature_K - 273.15f:F2}°C)");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Critical Pressure");
            ImGui.TableNextColumn();
            ImGui.Text($"{fluid.CriticalPressure_Pa / 1e6f:F3} MPa ({fluid.CriticalPressure_Pa / 1e5f:F1} bar)");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Critical Density");
            ImGui.TableNextColumn();
            ImGui.Text($"{fluid.CriticalDensity_kg_m3:F1} kg/m³");

            // Triple point
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Triple Point Temperature");
            ImGui.TableNextColumn();
            ImGui.Text($"{fluid.TriplePointTemperature_K:F2} K ({fluid.TriplePointTemperature_K - 273.15f:F2}°C)");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Triple Point Pressure");
            ImGui.TableNextColumn();
            ImGui.Text($"{fluid.TriplePointPressure_Pa:E2} Pa");

            // Molecular
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(0.8f, 1.0f, 0.6f, 1.0f), "Molecular Weight");
            ImGui.TableNextColumn();
            ImGui.Text($"{fluid.MolecularWeight_g_mol:F2} g/mol");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Acentric Factor");
            ImGui.TableNextColumn();
            ImGui.Text($"{fluid.AccentricFactor:F4}");

            // Antoine equation
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.8f, 1.0f), "Antoine Coefficients");
            ImGui.TableNextColumn();
            ImGui.Text($"A={fluid.AntoineCoefficients_A_B_C[0]:F4}, B={fluid.AntoineCoefficients_A_B_C[1]:F2}, C={fluid.AntoineCoefficients_A_B_C[2]:F2}");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Antoine Valid Range");
            ImGui.TableNextColumn();
            ImGui.Text($"{fluid.AntoineValidRange_K[0] - 273.15f:F1}°C to {fluid.AntoineValidRange_K[1] - 273.15f:F1}°C");

            ImGui.EndTable();
        }
    }

    private void DrawEnvironmentalTab(ORCFluid fluid)
    {
        ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Environmental Impact:");
        ImGui.Spacing();

        ImGui.BulletText($"Ozone Depletion Potential (ODP): {fluid.ODP:F4}");
        ImGui.Indent();
        if (fluid.ODP == 0)
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f), "✓ No ozone depletion");
        else if (fluid.ODP < 0.05f)
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), "⚠ Low ozone depletion");
        else
            ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), "✗ Significant ozone depletion");
        ImGui.Unindent();

        ImGui.BulletText($"Global Warming Potential (100-year): {fluid.GWP100:F0}");
        ImGui.Indent();
        if (fluid.GWP100 < 150)
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f), "✓ Low GWP (natural refrigerant level)");
        else if (fluid.GWP100 < 750)
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), "⚠ Medium GWP");
        else
            ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), "✗ High GWP");
        ImGui.Unindent();

        ImGui.BulletText($"Atmospheric Lifetime: {fluid.AtmosphericLifetime_years:F2} years");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.4f, 1.0f), "Safety Classification:");
        ImGui.Spacing();

        ImGui.BulletText($"ASHRAE Safety Class: {fluid.Safety}");
        ImGui.Indent();
        string safetyDesc = fluid.Safety switch
        {
            SafetyClass.A1 => "✓ Low toxicity, non-flammable (safest)",
            SafetyClass.A2L => "⚠ Low toxicity, mildly flammable",
            SafetyClass.A3 => "⚠ Low toxicity, flammable",
            SafetyClass.B1 => "⚠ Higher toxicity, non-flammable",
            _ => "See ASHRAE Standard 34 for details"
        };
        ImGui.TextWrapped(safetyDesc);
        ImGui.Unindent();

        if (!string.IsNullOrEmpty(fluid.Manufacturer))
        {
            ImGui.Spacing();
            ImGui.BulletText($"Manufacturer: {fluid.Manufacturer}");
        }
    }

    private void DrawApplicationsTab(ORCFluid fluid)
    {
        ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), "Recommended Applications:");
        ImGui.Spacing();

        if (fluid.Applications.Any())
        {
            foreach (var app in fluid.Applications)
            {
                ImGui.BulletText(app);
            }
        }
        else
        {
            ImGui.TextDisabled("No specific applications listed");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (fluid.Sources.Any())
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1.0f), "Data Sources:");
            foreach (var source in fluid.Sources)
            {
                ImGui.BulletText(source);
            }
        }
    }

    private void DrawAddFluidDialog()
    {
        ImGui.SetNextWindowSize(new Vector2(700, 800), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        string title = _editingFluid == null ? "Add New ORC Fluid" : $"Edit Fluid: {_editingFluid.Name}";

        if (ImGui.BeginPopupModal(title, ref _showAddFluidDialog, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.BeginChild("EditFormScroll", new Vector2(-1, -50), ImGuiChildFlags.Border);

            // Basic info
            ImGui.TextColored(new Vector4(0.8f, 1.0f, 0.4f, 1.0f), "Basic Information");
            ImGui.Separator();

            ImGui.InputText("Fluid Name", ref _editName, 100);
            ImGui.InputText("Chemical Formula", ref _editFormula, 50);
            ImGui.InputText("Refrigerant Code", ref _editRefCode, 20);

            string[] categories = Enum.GetNames<FluidCategory>();
            ImGui.Combo("Category", ref _editCategory, categories, categories.Length);

            string[] safetyClasses = Enum.GetNames<SafetyClass>();
            ImGui.Combo("Safety Class", ref _editSafety, safetyClasses, safetyClasses.Length);

            ImGui.Checkbox("Natural Fluid", ref _editIsNatural);

            ImGui.Spacing();
            ImGui.Separator();

            // Critical properties
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.4f, 1.0f), "Critical Properties");
            ImGui.Separator();

            ImGui.InputFloat("Critical Temperature (K)", ref _editTcrit);
            ImGui.InputFloat("Critical Pressure (Pa)", ref _editPcrit);
            ImGui.InputFloat("Critical Density (kg/m³)", ref _editRhoCrit);

            ImGui.Spacing();
            ImGui.Separator();

            // Molecular properties
            ImGui.TextColored(new Vector4(0.6f, 1.0f, 0.8f, 1.0f), "Molecular Properties");
            ImGui.Separator();

            ImGui.InputFloat("Molecular Weight (g/mol)", ref _editMW);
            ImGui.InputFloat("Acentric Factor", ref _editOmega);

            ImGui.Spacing();
            ImGui.Separator();

            // Environmental
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Environmental Impact");
            ImGui.Separator();

            ImGui.InputFloat("ODP", ref _editODP);
            ImGui.InputFloat("GWP (100-year)", ref _editGWP);
            ImGui.InputFloat("Atmospheric Lifetime (years)", ref _editLifetime);

            ImGui.Spacing();
            ImGui.Separator();

            // Antoine equation
            ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.8f, 1.0f), "Saturation Pressure (Antoine Equation)");
            ImGui.TextDisabled("log10(P[Pa]) = A - B/(T[K] + C)");
            ImGui.Separator();

            ImGui.InputFloat("A", ref _editAntoineA);
            ImGui.InputFloat("B", ref _editAntoineB);
            ImGui.InputFloat("C", ref _editAntoineC);
            ImGui.InputFloat("Valid Range Tmin (K)", ref _editAntoineTmin);
            ImGui.InputFloat("Valid Range Tmax (K)", ref _editAntoineTmax);

            ImGui.Spacing();
            ImGui.Separator();

            // Recommended parameters
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1.0f, 1.0f), "Recommended Operating Parameters");
            ImGui.Separator();

            ImGui.InputFloat("Recommended Evaporator Pressure (Pa)", ref _editRecEvapPress);
            ImGui.InputFloat("Recommended Condenser Temp (K)", ref _editRecCondTemp);
            ImGui.InputFloat("Minimum Temperature (K)", ref _editMinTemp);
            ImGui.InputFloat("Maximum Temperature (K)", ref _editMaxTemp);

            ImGui.Spacing();
            ImGui.Separator();

            // Notes
            ImGui.InputTextMultiline("Notes", ref _editNotes, 500, new Vector2(-1, 80));
            ImGui.InputText("Manufacturer", ref _editManufacturer, 100);

            ImGui.EndChild();

            // Buttons
            ImGui.Spacing();
            if (ImGui.Button("Save", new Vector2(100, 30)))
            {
                SaveFluidFromBuffers();
                _showAddFluidDialog = false;
                _editingFluid = null;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 30)))
            {
                _showAddFluidDialog = false;
                _editingFluid = null;
            }

            ImGui.EndPopup();
        }

        if (_showAddFluidDialog && !ImGui.IsPopupOpen(title))
        {
            ImGui.OpenPopup(title);
        }
    }

    #region Helper Methods

    private void InitializeNewFluidBuffers()
    {
        _editName = "";
        _editFormula = "";
        _editRefCode = "";
        _editCategory = 0;
        _editSafety = 0;
        _editIsNatural = false;
        _editTcrit = 400.0f;
        _editPcrit = 3e6f;
        _editRhoCrit = 500.0f;
        _editTtriple = 200.0f;
        _editPtriple = 1000.0f;
        _editMW = 100.0f;
        _editOmega = 0.3f;
        _editODP = 0.0f;
        _editGWP = 0.0f;
        _editLifetime = 0.0f;
        _editAntoineA = 4.0f;
        _editAntoineB = 1200.0f;
        _editAntoineC = -50.0f;
        _editAntoineTmin = 273.15f;
        _editAntoineTmax = 400.0f;
        _editRecEvapPress = 1.5e6f;
        _editRecCondTemp = 303.15f;
        _editMinTemp = 273.15f;
        _editMaxTemp = 400.0f;
        _editNotes = "";
        _editManufacturer = "";
    }

    private void InitializeEditBuffersFromFluid(ORCFluid fluid)
    {
        _editName = fluid.Name;
        _editFormula = fluid.ChemicalFormula;
        _editRefCode = fluid.RefrigerantCode;
        _editCategory = (int)fluid.Category;
        _editSafety = (int)fluid.Safety;
        _editIsNatural = fluid.IsNaturalFluid;
        _editTcrit = fluid.CriticalTemperature_K;
        _editPcrit = fluid.CriticalPressure_Pa;
        _editRhoCrit = fluid.CriticalDensity_kg_m3;
        _editTtriple = fluid.TriplePointTemperature_K;
        _editPtriple = fluid.TriplePointPressure_Pa;
        _editMW = fluid.MolecularWeight_g_mol;
        _editOmega = fluid.AccentricFactor;
        _editODP = fluid.ODP;
        _editGWP = fluid.GWP100;
        _editLifetime = fluid.AtmosphericLifetime_years;
        _editAntoineA = fluid.AntoineCoefficients_A_B_C[0];
        _editAntoineB = fluid.AntoineCoefficients_A_B_C[1];
        _editAntoineC = fluid.AntoineCoefficients_A_B_C[2];
        _editAntoineTmin = fluid.AntoineValidRange_K[0];
        _editAntoineTmax = fluid.AntoineValidRange_K[1];
        _editRecEvapPress = fluid.RecommendedEvaporatorPressure_Pa;
        _editRecCondTemp = fluid.RecommendedCondenserTemperature_K;
        _editMinTemp = fluid.MinimumTemperature_K;
        _editMaxTemp = fluid.MaximumTemperature_K;
        _editNotes = fluid.Notes;
        _editManufacturer = fluid.Manufacturer;
    }

    private void SaveFluidFromBuffers()
    {
        var fluid = _editingFluid ?? new ORCFluid();

        fluid.Name = _editName;
        fluid.ChemicalFormula = _editFormula;
        fluid.RefrigerantCode = _editRefCode;
        fluid.Category = (FluidCategory)_editCategory;
        fluid.Safety = (SafetyClass)_editSafety;
        fluid.IsNaturalFluid = _editIsNatural;
        fluid.CriticalTemperature_K = _editTcrit;
        fluid.CriticalPressure_Pa = _editPcrit;
        fluid.CriticalDensity_kg_m3 = _editRhoCrit;
        fluid.TriplePointTemperature_K = _editTtriple;
        fluid.TriplePointPressure_Pa = _editPtriple;
        fluid.MolecularWeight_g_mol = _editMW;
        fluid.AccentricFactor = _editOmega;
        fluid.ODP = _editODP;
        fluid.GWP100 = _editGWP;
        fluid.AtmosphericLifetime_years = _editLifetime;
        fluid.AntoineCoefficients_A_B_C = new float[] { _editAntoineA, _editAntoineB, _editAntoineC };
        fluid.AntoineValidRange_K = new float[] { _editAntoineTmin, _editAntoineTmax };
        fluid.RecommendedEvaporatorPressure_Pa = _editRecEvapPress;
        fluid.RecommendedCondenserTemperature_K = _editRecCondTemp;
        fluid.MinimumTemperature_K = _editMinTemp;
        fluid.MaximumTemperature_K = _editMaxTemp;
        fluid.Notes = _editNotes;
        fluid.Manufacturer = _editManufacturer;

        if (_editingFluid == null)
        {
            ORCFluidLibrary.Instance.AddFluid(fluid);
            SetStatusMessage($"Added new fluid: {fluid.Name}");
        }
        else
        {
            ORCFluidLibrary.Instance.UpdateFluid(fluid);
            SetStatusMessage($"Updated fluid: {fluid.Name}");
        }

        _selectedFluid = fluid;
    }

    private ORCFluid CloneFluid(ORCFluid source)
    {
        return new ORCFluid
        {
            Name = source.Name,
            ChemicalFormula = source.ChemicalFormula,
            RefrigerantCode = source.RefrigerantCode,
            Category = source.Category,
            Safety = source.Safety,
            IsNaturalFluid = source.IsNaturalFluid,
            CriticalTemperature_K = source.CriticalTemperature_K,
            CriticalPressure_Pa = source.CriticalPressure_Pa,
            CriticalDensity_kg_m3 = source.CriticalDensity_kg_m3,
            TriplePointTemperature_K = source.TriplePointTemperature_K,
            TriplePointPressure_Pa = source.TriplePointPressure_Pa,
            MolecularWeight_g_mol = source.MolecularWeight_g_mol,
            AccentricFactor = source.AccentricFactor,
            ODP = source.ODP,
            GWP100 = source.GWP100,
            AtmosphericLifetime_years = source.AtmosphericLifetime_years,
            AntoineCoefficients_A_B_C = (float[])source.AntoineCoefficients_A_B_C.Clone(),
            AntoineValidRange_K = (float[])source.AntoineValidRange_K.Clone(),
            RecommendedEvaporatorPressure_Pa = source.RecommendedEvaporatorPressure_Pa,
            RecommendedCondenserTemperature_K = source.RecommendedCondenserTemperature_K,
            MinimumTemperature_K = source.MinimumTemperature_K,
            MaximumTemperature_K = source.MaximumTemperature_K,
            Notes = source.Notes,
            Manufacturer = source.Manufacturer
        };
    }

    private void SetStatusMessage(string message)
    {
        _statusMessage = message;
        _statusMessageTimer = 3.0f;
        Logger.Log(message);
    }

    #endregion
}
