// GeoscientistToolkit/Analysis/ThermalConductivity/ThermalConductivityTool.cs

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.ThermalConductivity;

public class ThermalConductivityTool : IDatasetTools, IDisposable
{
    private static Vector3[,] _colormapData;

    // CACHED isocontours to avoid regenerating every frame
    private readonly List<(Vector2, Vector2)> _cachedIsocontours = new();

    private readonly ImGuiExportFileDialog _compositePngExportDialog =
        new("ExportCompositePng", "Export Composite Image");

    private readonly ImGuiExportFileDialog _csvExportDialog = new("ExportThermalCsv", "Export Results to CSV");
    private readonly ProgressBarDialog _isosurfaceProgressDialog = new("Generating Isosurface");
    private readonly ThermalOptions _options = new();
    private readonly ImGuiExportFileDialog _pngExportDialog = new("ExportThermalPng", "Export Slice Image");
    private readonly ProgressBarDialog _progressDialog = new("Thermal Simulation");
    private readonly ImGuiExportFileDialog _rtfReportExportDialog = new("ExportRtfReport", "Export Rich Text Report");
    private readonly ImGuiExportFileDialog _sliceCsvExportDialog = new("ExportSliceCsv", "Export Slice to CSV");
    private readonly ImGuiExportFileDialog _stlExportDialog = new("ExportStl", "Export Mesh to STL");
    private readonly ImGuiExportFileDialog _txtReportExportDialog = new("ExportTxtReport", "Export Text Report");
    private (int sliceDir, int sliceIdx, float isoValue, int numContours) _cachedIsocontoursKey = (-1, -1, -1, -1);
    private CancellationTokenSource _cancellationTokenSource;
    private int _colorMapIndex;

    private double _isosurfaceValue = 300.0;
    private bool _isSimulationRunning;
    private string _materialSearchFilter = "";
    private int _numIsocontours = 10;
    private int _numIsosurfaces = 5;
    private PhysicalMaterial _selectedLibraryMaterial;
    private int _selectedSliceDirectionInt;

    // Slice viewer state
    private int _selectedSliceIndex;
    private bool _showIsocontours = true;

    // Material library browser state
    private bool _showMaterialLibraryBrowser;
    private Task _simulationTask;
    private byte _targetMaterialIdForAssignment;

    public ThermalConductivityTool()
    {
        _csvExportDialog.SetExtensions((".csv", "Comma-separated values"));
        _sliceCsvExportDialog.SetExtensions((".csv", "Comma-separated values"));
        _pngExportDialog.SetExtensions((".png", "Portable Network Graphics"));
        _compositePngExportDialog.SetExtensions((".png", "Portable Network Graphics"));
        _txtReportExportDialog.SetExtensions((".txt", "Text Document"));
        _rtfReportExportDialog.SetExtensions((".rtf", "Rich Text Format"));
        _stlExportDialog.SetExtensions((".stl", "Stereolithography"));

        InitializeColormaps();
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ctDataset)
        {
            ImGui.TextDisabled("This tool requires a CT Image Stack dataset.");
            return;
        }

        _options.Dataset = ctDataset;

        // Check if the simulation task has finished (for any reason) and clean up the UI state.
        // This is the primary mechanism for returning to an interactive state.
        if (_isSimulationRunning && _simulationTask != null && _simulationTask.IsCompleted)
        {
            Logger.Log("[ThermalTool] Simulation task has completed, cleaning up UI state.");
            _isSimulationRunning = false;
        }

        // If the simulation is running, we display the progress dialog and handle cancellation.
        if (_isSimulationRunning)
        {
            _progressDialog.Submit();

            // If the dialog is closed by the user (either via its Cancel button or by closing the window),
            // we must signal the background task to stop. We check if cancellation has already been requested
            // to avoid sending the signal multiple times.
            if ((_progressDialog.IsCancellationRequested || !_progressDialog.IsActive) &&
                _cancellationTokenSource?.IsCancellationRequested == false)
            {
                Logger.Log("[ThermalTool] User initiated cancellation. Signaling background task.");
                _cancellationTokenSource.Cancel();
            }

            // While the simulation is running, we don't draw the rest of the tool's UI.
            return;
        }


        if (ImGui.BeginTabBar("ThermalTabs"))
        {
            if (ImGui.BeginTabItem("Setup"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Results"))
            {
                DrawResultsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Export"))
            {
                DrawExportTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        // Material library browser modal
        if (_showMaterialLibraryBrowser) DrawMaterialLibraryBrowser();
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    private void DrawSettingsTab()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        // Quick presets in a more compact layout
        if (ImGui.CollapsingHeader("Temperature Presets", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));

            if (ImGui.Button("Room -> Boiling", new Vector2((availWidth - 12) / 4, 0)))
            {
                _options.TemperatureHot = 373.15;
                _options.TemperatureCold = 293.15;
            }

            ImGui.SameLine();
            if (ImGui.Button("Freezing -> Room", new Vector2((availWidth - 12) / 4, 0)))
            {
                _options.TemperatureHot = 293.15;
                _options.TemperatureCold = 273.15;
            }

            ImGui.SameLine();
            if (ImGui.Button("Geothermal 50C", new Vector2((availWidth - 12) / 4, 0)))
            {
                _options.TemperatureHot = 323.15;
                _options.TemperatureCold = 283.15;
            }

            ImGui.SameLine();
            if (ImGui.Button("High Temp 500C", new Vector2((availWidth - 12) / 4, 0)))
            {
                _options.TemperatureHot = 773.15;
                _options.TemperatureCold = 293.15;
            }

            ImGui.PopStyleVar();
        }

        ImGui.Spacing();

        // Material properties with library integration - EXCLUDE EXTERIOR (ID: 0)
        if (ImGui.CollapsingHeader("Material Properties", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // Filter out exterior material (ID: 0)
            var visibleMaterials = _options.Dataset.Materials.Where(m => m.ID != 0).ToList();

            if (visibleMaterials.Count == 0)
            {
                ImGui.TextDisabled("No materials defined. Use segmentation tools to create materials.");
            }
            else
            {
                if (ImGui.BeginTable("MaterialsTable", 6,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                        ImGuiTableFlags.SizingFixedFit,
                        new Vector2(0, 200)))
                {
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 25);
                    ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableSetupColumn("k (W/m·K)", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("Library", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Properties", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    foreach (var material in visibleMaterials)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();

                        // Color indicator
                        var color = material.Color;
                        ImGui.ColorButton($"##color_{material.ID}", color, ImGuiColorEditFlags.NoTooltip,
                            new Vector2(16, 16));

                        ImGui.TableNextColumn();
                        ImGui.Text(material.Name);

                        ImGui.TableNextColumn();
                        if (!_options.MaterialConductivities.ContainsKey(material.ID))
                            _options.MaterialConductivities[material.ID] = 1.0;
                        var conductivity = (float)_options.MaterialConductivities[material.ID];
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputFloat($"##cond_{material.ID}", ref conductivity, 0.01f, 0.1f, "%.4f"))
                            _options.MaterialConductivities[material.ID] = Math.Max(0.001, conductivity);

                        // Validation indicator
                        if (conductivity <= 0)
                        {
                            ImGui.TableNextColumn();
                            ImGui.TextColored(new Vector4(1, 0, 0, 1), "!");
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Invalid conductivity value");
                        }
                        else
                        {
                            ImGui.TableNextColumn();

                            // Show library source
                            PhysicalMaterial libMat = null;
                            if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                                libMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);

                            if (libMat != null)
                            {
                                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "OK");
                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.BeginTooltip();
                                    ImGui.Text($"Linked: {libMat.Name}");
                                    if (libMat.ThermalConductivity_W_mK.HasValue)
                                        ImGui.Text($"k = {libMat.ThermalConductivity_W_mK:F4} W/m·K");
                                    ImGui.EndTooltip();
                                }
                            }
                            else
                            {
                                ImGui.TextDisabled("Manual");
                            }
                        }

                        ImGui.TableNextColumn();
                        // Show available properties from library
                        if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                        {
                            var libMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                            if (libMat != null)
                            {
                                var availableCount = 0;
                                if (libMat.ThermalConductivity_W_mK.HasValue) availableCount++;
                                if (libMat.SpecificHeatCapacity_J_kgK.HasValue) availableCount++;
                                if (libMat.Density_kg_m3.HasValue) availableCount++;
                                if (libMat.ThermalDiffusivity_m2_s.HasValue) availableCount++;

                                ImGui.Text($"{availableCount}/4");
                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.BeginTooltip();
                                    ImGui.Text("Available thermal properties:");
                                    if (libMat.ThermalConductivity_W_mK.HasValue)
                                        ImGui.Text($"  k = {libMat.ThermalConductivity_W_mK:F4} W/m·K");
                                    if (libMat.SpecificHeatCapacity_J_kgK.HasValue)
                                        ImGui.Text($"  cp = {libMat.SpecificHeatCapacity_J_kgK:F1} J/kg·K");
                                    if (libMat.Density_kg_m3.HasValue)
                                        ImGui.Text($"  rho = {libMat.Density_kg_m3:F1} kg/m³");
                                    if (libMat.ThermalDiffusivity_m2_s.HasValue)
                                        ImGui.Text($"  alpha = {libMat.ThermalDiffusivity_m2_s:E2} m²/s");
                                    ImGui.EndTooltip();
                                }
                            }
                            else
                            {
                                ImGui.TextDisabled("0/4");
                            }
                        }
                        else
                        {
                            ImGui.TextDisabled("---");
                        }

                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"Browse##browse_{material.ID}"))
                        {
                            _targetMaterialIdForAssignment = material.ID;
                            _showMaterialLibraryBrowser = true;
                        }

                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Browse and assign from material library");

                        // Clear library link button
                        if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"Clear##clear_{material.ID}"))
                            {
                                material.PhysicalMaterialName = null;
                                Logger.Log($"[ThermalTool] Cleared library link for {material.Name}");
                            }

                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear library link");
                        }
                    }

                    ImGui.EndTable();
                }

                ImGui.Spacing();

                // Quick material presets - EXCLUDE EXTERIOR
                ImGui.Text("Quick Assignments:");
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));

                if (ImGui.Button("Air (0.026)", new Vector2((availWidth - 8) / 3, 0)))
                    foreach (var mat in visibleMaterials)
                        _options.MaterialConductivities[mat.ID] = 0.026;
                ImGui.SameLine();
                if (ImGui.Button("Water (0.6)", new Vector2((availWidth - 8) / 3, 0)))
                    foreach (var mat in visibleMaterials)
                        _options.MaterialConductivities[mat.ID] = 0.6;
                ImGui.SameLine();
                if (ImGui.Button("Rock (2.5)", new Vector2((availWidth - 8) / 3, 0)))
                    foreach (var mat in visibleMaterials)
                        _options.MaterialConductivities[mat.ID] = 2.5;

                ImGui.PopStyleVar();
            }

            ImGui.Unindent();
        }

        ImGui.Spacing();

        // Simulation parameters in a cleaner layout
        if (ImGui.CollapsingHeader("Simulation Parameters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // Boundary temperatures
            ImGui.SeparatorText("Boundary Conditions");

            var tempHot = (float)_options.TemperatureHot;
            ImGui.SetNextItemWidth(150);
            if (ImGui.DragFloat("Hot (K)", ref tempHot, 1.0f, 273.15f, 1000.0f, "%.2f"))
                _options.TemperatureHot = Math.Max(tempHot, _options.TemperatureCold + 1);
            ImGui.SameLine();
            ImGui.TextDisabled($"({tempHot - 273.15:F1} C)");

            var tempCold = (float)_options.TemperatureCold;
            ImGui.SetNextItemWidth(150);
            if (ImGui.DragFloat("Cold (K)", ref tempCold, 1.0f, 0.0f, 1000.0f, "%.2f"))
                _options.TemperatureCold = Math.Min(tempCold, _options.TemperatureHot - 1);
            ImGui.SameLine();
            ImGui.TextDisabled($"({tempCold - 273.15:F1} C)");

            var dT = _options.TemperatureHot - _options.TemperatureCold;
            ImGui.Text($"Temperature gradient: {dT:F2} K");

            ImGui.Spacing();
            ImGui.SeparatorText("Heat Flow Configuration");

            var directionIndex = (int)_options.HeatFlowDirection;
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("Direction", ref directionIndex, "X-axis\0Y-axis\0Z-axis\0"))
                _options.HeatFlowDirection = (HeatFlowDirection)directionIndex;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Direction of applied temperature gradient");

            ImGui.Spacing();
            ImGui.SeparatorText("Solver Settings");

            var backendIndex = (int)_options.SolverBackend;
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("Backend", ref backendIndex, "CPU Parallel\0CPU SIMD\0GPU OpenCL\0"))
                _options.SolverBackend = (SolverBackend)backendIndex;

            var maxIter = _options.MaxIterations;
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputInt("Max Iterations", ref maxIter, 100, 1000))
                _options.MaxIterations = Math.Clamp(maxIter, 100, 100000);

            var tolerance = (float)_options.ConvergenceTolerance;
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputFloat("Tolerance", ref tolerance, 0, 0, "%.1e"))
                _options.ConvergenceTolerance = Math.Clamp(tolerance, 1e-9, 1e-3);

            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Validation and run button
        var canRun = ValidateSettings(out var validationMessages);

        if (!canRun)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.8f, 0, 1));
            ImGui.TextWrapped("Cannot run simulation:");
            ImGui.PopStyleColor();
            ImGui.Indent();
            foreach (var msg in validationMessages) ImGui.BulletText(msg);
            ImGui.Unindent();
            ImGui.Spacing();
        }

        // Run button
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.7f, 0.2f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.8f, 0.3f, 1.0f));
        ImGui.BeginDisabled(!canRun);
        if (ImGui.Button("> Run Simulation", new Vector2(-1, 40))) StartSimulation();
        ImGui.EndDisabled();
        ImGui.PopStyleColor(2);
    }

    private void DrawMaterialLibraryBrowser()
    {
        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
        var isOpen = true;
        if (ImGui.Begin("Material Library Browser##ThermalBrowser", ref isOpen,
                ImGuiWindowFlags.NoCollapse))
        {
            var targetMaterial = _options.Dataset.Materials.FirstOrDefault(m => m.ID == _targetMaterialIdForAssignment);
            if (targetMaterial != null)
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1),
                    $"Assigning to: {targetMaterial.Name}");
            else
                ImGui.Text("Select a material from the library:");

            ImGui.Separator();

            // Search filter
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##search", "Search materials (name, phase, properties)...",
                ref _materialSearchFilter, 256);

            ImGui.Spacing();

            // Split into two columns
            if (ImGui.BeginTable("LibraryBrowserTable", 2, ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Materials", ImGuiTableColumnFlags.WidthFixed, 350);
                ImGui.TableSetupColumn("Properties", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                // Left: Material list
                if (ImGui.BeginChild("MaterialList", new Vector2(0, -80), ImGuiChildFlags.Border))
                {
                    var materials = MaterialLibrary.Instance.Materials
                        .Where(m => string.IsNullOrEmpty(_materialSearchFilter) ||
                                    m.Name.Contains(_materialSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                                    m.Phase.ToString().Contains(_materialSearchFilter,
                                        StringComparison.OrdinalIgnoreCase) ||
                                    (m.Notes?.Contains(_materialSearchFilter, StringComparison.OrdinalIgnoreCase) ??
                                     false))
                        .OrderBy(m => m.Phase)
                        .ThenBy(m => m.Name)
                        .ToList();

                    if (materials.Count == 0)
                    {
                        ImGui.TextDisabled("No materials found.");
                    }
                    else
                    {
                        var currentPhase = "";
                        foreach (var mat in materials)
                        {
                            // Phase header
                            if (mat.Phase.ToString() != currentPhase)
                            {
                                currentPhase = mat.Phase.ToString();
                                ImGui.SeparatorText(currentPhase);
                            }

                            var isSelected = _selectedLibraryMaterial == mat;

                            // Material entry
                            if (ImGui.Selectable($"{mat.Name}##{mat.Name}", isSelected)) _selectedLibraryMaterial = mat;

                            // Quick property preview on hover
                            if (ImGui.IsItemHovered())
                            {
                                ImGui.BeginTooltip();
                                if (mat.ThermalConductivity_W_mK.HasValue)
                                    ImGui.Text($"k = {mat.ThermalConductivity_W_mK:F4} W/m·K");
                                if (mat.Density_kg_m3.HasValue)
                                    ImGui.Text($"rho = {mat.Density_kg_m3:F1} kg/m³");
                                ImGui.EndTooltip();
                            }
                        }
                    }
                }

                ImGui.EndChild();

                ImGui.TableNextColumn();

                // Right: Material properties
                if (ImGui.BeginChild("MaterialProperties", new Vector2(0, -80), ImGuiChildFlags.Border))
                {
                    if (_selectedLibraryMaterial != null)
                    {
                        var mat = _selectedLibraryMaterial;

                        ImGui.TextColored(new Vector4(0.5f, 1, 1, 1), mat.Name);
                        ImGui.TextDisabled($"Phase: {mat.Phase}");
                        ImGui.Spacing();

                        ImGui.SeparatorText("Thermal Properties");

                        if (mat.ThermalConductivity_W_mK.HasValue)
                            ImGui.Text($"Thermal Conductivity: {mat.ThermalConductivity_W_mK:F4} W/m·K");
                        else
                            ImGui.TextDisabled("Thermal Conductivity: N/A");

                        if (mat.SpecificHeatCapacity_J_kgK.HasValue)
                            ImGui.Text($"Specific Heat: {mat.SpecificHeatCapacity_J_kgK:F1} J/kg·K");
                        else
                            ImGui.TextDisabled("Specific Heat: N/A");

                        if (mat.ThermalDiffusivity_m2_s.HasValue)
                            ImGui.Text($"Thermal Diffusivity: {mat.ThermalDiffusivity_m2_s:E2} m²/s");
                        else
                            ImGui.TextDisabled("Thermal Diffusivity: N/A");

                        ImGui.Spacing();
                        ImGui.SeparatorText("General Properties");

                        if (mat.Density_kg_m3.HasValue)
                            ImGui.Text($"Density: {mat.Density_kg_m3:F1} kg/m³");
                        else
                            ImGui.TextDisabled("Density: N/A");

                        if (mat.MohsHardness.HasValue)
                            ImGui.Text($"Mohs Hardness: {mat.MohsHardness:F1}");

                        if (mat.YoungModulus_GPa.HasValue)
                            ImGui.Text($"Young's Modulus: {mat.YoungModulus_GPa:F1} GPa");

                        if (mat.PoissonRatio.HasValue)
                            ImGui.Text($"Poisson Ratio: {mat.PoissonRatio:F3}");

                        if (!string.IsNullOrEmpty(mat.Notes))
                        {
                            ImGui.Spacing();
                            ImGui.SeparatorText("Notes");
                            ImGui.TextWrapped(mat.Notes);
                        }

                        if (mat.Sources.Count > 0)
                        {
                            ImGui.Spacing();
                            ImGui.SeparatorText("Sources");
                            foreach (var source in mat.Sources) ImGui.BulletText(source);
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled("Select a material to view properties");
                    }
                }

                ImGui.EndChild();

                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.Separator();

            // Action buttons
            ImGui.BeginDisabled(_selectedLibraryMaterial == null);

            if (_targetMaterialIdForAssignment > 0)
            {
                // Assign to specific material
                if (ImGui.Button($"Assign to {targetMaterial?.Name}", new Vector2(-260, 0)))
                {
                    AssignLibraryMaterial(_targetMaterialIdForAssignment, _selectedLibraryMaterial);
                    _showMaterialLibraryBrowser = false;
                }
            }
            else
            {
                // Apply to all materials (excluding exterior)
                if (ImGui.Button("Apply to All Materials", new Vector2(-260, 0)))
                    if (_selectedLibraryMaterial?.ThermalConductivity_W_mK != null)
                    {
                        foreach (var mat in _options.Dataset.Materials.Where(m => m.ID != 0))
                        {
                            _options.MaterialConductivities[mat.ID] =
                                _selectedLibraryMaterial.ThermalConductivity_W_mK.Value;
                            mat.PhysicalMaterialName = _selectedLibraryMaterial.Name;
                        }

                        Logger.Log(
                            $"[ThermalTool] Applied {_selectedLibraryMaterial.Name} to all materials (excluding exterior)");
                        _showMaterialLibraryBrowser = false;
                    }
            }

            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Close", new Vector2(120, 0))) _showMaterialLibraryBrowser = false;

            ImGui.SameLine();
            ImGui.BeginDisabled(_selectedLibraryMaterial == null ||
                                !_selectedLibraryMaterial.ThermalConductivity_W_mK.HasValue);
            ImGui.TextDisabled($"k = {_selectedLibraryMaterial?.ThermalConductivity_W_mK:F4} W/m·K");
            ImGui.EndDisabled();
        }

        ImGui.End();

        if (!isOpen) _showMaterialLibraryBrowser = false;
    }

    private void AssignLibraryMaterial(byte materialId, PhysicalMaterial libraryMaterial)
    {
        var material = _options.Dataset.Materials.FirstOrDefault(m => m.ID == materialId);
        if (material == null || libraryMaterial == null) return;

        // Assign thermal conductivity
        if (libraryMaterial.ThermalConductivity_W_mK.HasValue)
            _options.MaterialConductivities[materialId] = libraryMaterial.ThermalConductivity_W_mK.Value;

        // Assign density
        if (libraryMaterial.Density_kg_m3.HasValue)
            material.Density = libraryMaterial.Density_kg_m3.Value / 1000.0; // kg/m³ to g/cm³

        // Link to library
        material.PhysicalMaterialName = libraryMaterial.Name;

        Logger.Log($"[ThermalTool] Assigned '{libraryMaterial.Name}' to '{material.Name}'");
        Logger.Log($"  k = {libraryMaterial.ThermalConductivity_W_mK:F4} W/m·K");
        if (libraryMaterial.Density_kg_m3.HasValue)
            Logger.Log($"  rho = {libraryMaterial.Density_kg_m3:F1} kg/m³");
    }

    private bool ValidateSettings(out List<string> messages)
    {
        messages = new List<string>();

        if (_options.TemperatureHot <= _options.TemperatureCold)
            messages.Add("Hot temperature must exceed cold temperature");

        // Only check non-exterior materials
        foreach (var kvp in _options.MaterialConductivities.Where(k => k.Key != 0))
            if (kvp.Value <= 0)
            {
                var mat = _options.Dataset.Materials.FirstOrDefault(m => m.ID == kvp.Key);
                var name = mat?.Name ?? $"Material {kvp.Key}";
                messages.Add($"{name}: invalid conductivity");
            }

        if (_options.Dataset.LabelData == null) messages.Add("Dataset has no material/label data");

        if (_options.Dataset.Width < 3 || _options.Dataset.Height < 3 || _options.Dataset.Depth < 3)
            messages.Add("Dataset too small (min 3x3x3 voxels)");

        // Check if there are any non-exterior materials
        var hasNonExteriorMaterials = _options.Dataset.Materials.Any(m => m.ID != 0);
        if (!hasNonExteriorMaterials)
            messages.Add("No materials defined (use segmentation tools to create materials)");

        return messages.Count == 0;
    }

    private void DrawResultsTab()
    {
        var results = _options.Dataset.ThermalResults;
        if (results == null)
        {
            ImGui.TextDisabled("No results available. Run a simulation from the Setup tab.");
            return;
        }

        // Summary
        if (ImGui.CollapsingHeader("Summary", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            ImGui.Text("Effective Conductivity:");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"{results.EffectiveConductivity:F4} W/m·K");

            ImGui.Text($"Computation Time: {results.ComputationTime.TotalSeconds:F2} seconds");
            ImGui.Text($"Temperature Range: {_options.TemperatureCold:F1} K to {_options.TemperatureHot:F1} K");
            ImGui.Text($"Heat Flow Direction: {_options.HeatFlowDirection}");

            if (results.AnalyticalEstimates.Count > 0)
            {
                ImGui.Spacing();
                ImGui.SeparatorText("Analytical Comparisons");

                if (ImGui.BeginTable("AnalyticalTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                    ImGui.TableSetupColumn("Model");
                    ImGui.TableSetupColumn("k (W/m·K)");
                    ImGui.TableSetupColumn("Error %");
                    ImGui.TableHeadersRow();

                    foreach (var (name, value) in results.AnalyticalEstimates.OrderBy(x => x.Value))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(name);
                        ImGui.TableNextColumn();
                        ImGui.Text($"{value:F4}");
                        ImGui.TableNextColumn();
                        var error = Math.Abs(results.EffectiveConductivity - value) / results.EffectiveConductivity *
                                    100.0;
                        ImGui.Text($"{error:F2}%");
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.Unindent();
        }

        if (ImGui.CollapsingHeader("2D Temperature Field Viewer", ImGuiTreeNodeFlags.DefaultOpen)) DrawSliceViewer();

        if (ImGui.CollapsingHeader("3D Isosurface Generation")) DrawIsosurfaceGenerator();
    }

    private void DrawExportTab()
    {
        var results = _options.Dataset.ThermalResults;
        if (results == null)
        {
            ImGui.TextDisabled("No results to export. Run a simulation first.");
            return;
        }

        ImGui.SeparatorText("Data Export");

        if (ImGui.Button("Export Summary (CSV)", new Vector2(-1, 0)))
            _csvExportDialog.Open($"ThermalSummary_{_options.Dataset.Name}.csv");

        if (ImGui.Button("Export Text Report (TXT)", new Vector2(-1, 0)))
            _txtReportExportDialog.Open($"ThermalReport_{_options.Dataset.Name}.txt");

        if (ImGui.Button("Export Rich Report (RTF)", new Vector2(-1, 0)))
            _rtfReportExportDialog.Open($"ThermalReport_{_options.Dataset.Name}.rtf");

        ImGui.Spacing();
        ImGui.SeparatorText("Image Export");

        if (ImGui.Button("Export Current Slice (PNG)", new Vector2(-1, 0)))
            _pngExportDialog.Open(
                $"ThermalSlice_{(HeatFlowDirection)_selectedSliceDirectionInt}{_selectedSliceIndex}.png");

        if (ImGui.Button("Export Composite Image (PNG)", new Vector2(-1, 0)))
            _compositePngExportDialog.Open($"ThermalComposite_{_options.Dataset.Name}.png");

        // Handle dialogs
        if (_csvExportDialog.Submit()) ExportSummaryToCsv(_csvExportDialog.SelectedPath);
        if (_txtReportExportDialog.Submit()) ExportTextReport(_txtReportExportDialog.SelectedPath, false);
        if (_rtfReportExportDialog.Submit()) ExportTextReport(_rtfReportExportDialog.SelectedPath, true);
        if (_pngExportDialog.Submit())
        {
            var (slice, width, height) = GetSelectedSlice();
            if (slice != null) ExportSliceToPng(_pngExportDialog.SelectedPath, slice, width, height);
        }

        if (_compositePngExportDialog.Submit()) ExportCompositeImage(_compositePngExportDialog.SelectedPath);
        if (_sliceCsvExportDialog.Submit())
        {
            var (slice, _, _) = GetSelectedSlice();
            if (slice != null) ExportSliceToCsv(_sliceCsvExportDialog.SelectedPath, slice);
        }
    }

    private void DrawSliceViewer()
    {
        var maxSlice = 0;
        var selectedDirection = (HeatFlowDirection)_selectedSliceDirectionInt;
        switch (selectedDirection)
        {
            case HeatFlowDirection.X: maxSlice = _options.Dataset.Width - 1; break;
            case HeatFlowDirection.Y: maxSlice = _options.Dataset.Height - 1; break;
            case HeatFlowDirection.Z: maxSlice = _options.Dataset.Depth - 1; break;
        }

        _selectedSliceIndex = Math.Clamp(_selectedSliceIndex, 0, maxSlice);

        ImGui.SetNextItemWidth(120);
        if (ImGui.Combo("Axis", ref _selectedSliceDirectionInt, "X\0Y\0Z\0"))
        {
            _selectedSliceIndex = 0;
            _cachedIsocontoursKey = (-1, -1, -1, -1); // Invalidate cache
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Slice", ref _selectedSliceIndex, 0, maxSlice))
            _cachedIsocontoursKey = (-1, -1, -1, -1); // Invalidate cache

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        if (ImGui.Combo("Colormap", ref _colorMapIndex, "Hot\0Rainbow\0"))
            _cachedIsocontoursKey = (-1, -1, -1, -1); // Invalidate cache

        if (ImGui.Checkbox("Show Isocontours", ref _showIsocontours))
            _cachedIsocontoursKey = (-1, -1, -1, -1); // Invalidate cache

        if (_showIsocontours)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt("Count", ref _numIsocontours, 2, 20))
                _cachedIsocontoursKey = (-1, -1, -1, -1); // Invalidate cache
        }

        var (slice, width, height) = GetSelectedSlice();
        if (slice == null) return;

        // Render slice
        var available = ImGui.GetContentRegionAvail();
        var dl = ImGui.GetWindowDrawList();
        var canvasPos = ImGui.GetCursorScreenPos();

        var aspectRatio = (float)width / height;
        var canvasSize = new Vector2(
            Math.Min(available.X - 100, available.Y * aspectRatio),
            Math.Min(available.Y - 20, (available.X - 100) / aspectRatio)
        );

        dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);

        // Render with adaptive sampling
        var pixelSkip = Math.Max(1, Math.Max(width, height) / 512);

        Parallel.For(0, height / pixelSkip, y =>
        {
            for (var x = 0; x < width; x += pixelSkip)
            {
                var actualY = y * pixelSkip;
                var temp = slice[x, actualY];
                var normalizedTemp = (temp - _options.TemperatureCold) /
                                     (_options.TemperatureHot - _options.TemperatureCold);
                normalizedTemp = Math.Clamp(normalizedTemp, 0.0, 1.0);
                var color = ApplyColorMap((float)normalizedTemp, _colorMapIndex);

                var px = canvasPos.X + (float)x / width * canvasSize.X;
                var py = canvasPos.Y + (float)actualY / height * canvasSize.Y;
                var pw = canvasSize.X / width * pixelSkip;
                var ph = canvasSize.Y / height * pixelSkip;

                dl.AddRectFilled(new Vector2(px, py), new Vector2(px + pw, py + ph), ImGui.GetColorU32(color));
            }
        });

        // Draw CACHED isocontours
        if (_showIsocontours)
        {
            var tempRange = _options.TemperatureHot - _options.TemperatureCold;

            // Check if we need to regenerate cache
            var currentKey = (_selectedSliceDirectionInt, _selectedSliceIndex, (float)tempRange, _numIsocontours);
            if (currentKey != _cachedIsocontoursKey)
            {
                // Regenerate all isocontours at once
                _cachedIsocontours.Clear();
                for (var i = 1; i <= _numIsocontours; i++)
                {
                    var isovalue = _options.TemperatureCold + i * tempRange / (_numIsocontours + 1);
                    var lines = IsosurfaceGenerator.GenerateIsocontours(slice, (float)isovalue);
                    _cachedIsocontours.AddRange(lines);
                }

                _cachedIsocontoursKey = currentKey;
            }

            // Draw cached contours
            var contourColorOuter = new Vector4(0.1f, 0.1f, 0.1f, 0.7f);
            var contourColorInner = new Vector4(1.0f, 1.0f, 1.0f, 0.9f);

            foreach (var (p1, p2) in _cachedIsocontours)
            {
                var sp1 = canvasPos + new Vector2(p1.X / width * canvasSize.X, p1.Y / height * canvasSize.Y);
                var sp2 = canvasPos + new Vector2(p2.X / width * canvasSize.X, p2.Y / height * canvasSize.Y);
                dl.AddLine(sp1, sp2, ImGui.GetColorU32(contourColorOuter), 2.5f);
                dl.AddLine(sp1, sp2, ImGui.GetColorU32(contourColorInner), 1.0f);
            }
        }

        dl.AddRect(canvasPos, canvasPos + canvasSize, 0xFFFFFFFF, 0, 0, 1.0f);

        ImGui.Dummy(canvasSize);

        // Tooltip
        if (ImGui.IsItemHovered())
        {
            var mousePos = ImGui.GetMousePos();
            var relativePos = mousePos - canvasPos;
            var hoverX = (int)(relativePos.X / canvasSize.X * width);
            var hoverY = (int)(relativePos.Y / canvasSize.Y * height);

            if (hoverX >= 0 && hoverX < width && hoverY >= 0 && hoverY < height)
            {
                var temp = slice[hoverX, hoverY];
                var tempC = temp - 273.15;
                ImGui.SetTooltip($"Pos: ({hoverX}, {hoverY})\nT: {temp:F2} K ({tempC:F2} C)");
            }
        }

        // Legend
        ImGui.SameLine();
        DrawColorScaleLegend(ImGui.GetCursorScreenPos(), new Vector2(30, canvasSize.Y));

        // Export buttons
        ImGui.Spacing();
        if (ImGui.Button("Export Slice as PNG", new Vector2(180, 0)))
            _pngExportDialog.Open($"Slice_{(HeatFlowDirection)_selectedSliceDirectionInt}{_selectedSliceIndex}.png");
        ImGui.SameLine();
        if (ImGui.Button("Export Slice to CSV", new Vector2(180, 0)))
            _sliceCsvExportDialog.Open(
                $"SliceData_{(HeatFlowDirection)_selectedSliceDirectionInt}{_selectedSliceIndex}.csv");
    }

    private void DrawColorScaleLegend(Vector2 pos, Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();

        var steps = 50;
        for (var i = 0; i < steps; i++)
        {
            var t = (float)i / (steps - 1);
            var color = ApplyColorMap(t, _colorMapIndex);

            var y1 = pos.Y + size.Y * (1.0f - t - 1.0f / steps);
            var y2 = pos.Y + size.Y * (1.0f - t);

            dl.AddRectFilled(
                new Vector2(pos.X, y1),
                new Vector2(pos.X + size.X, y2),
                ImGui.GetColorU32(color)
            );
        }

        dl.AddRect(pos, pos + size, 0xFFFFFFFF);

        var tempHot = _options.TemperatureHot;
        var tempCold = _options.TemperatureCold;

        dl.AddText(new Vector2(pos.X + size.X + 5, pos.Y - 5), 0xFFFFFFFF, $"{tempHot:F0}K");
        dl.AddText(new Vector2(pos.X + size.X + 5, pos.Y + size.Y - 10), 0xFFFFFFFF, $"{tempCold:F0}K");

        var tempMid = (tempHot + tempCold) / 2;
        dl.AddText(new Vector2(pos.X + size.X + 5, pos.Y + size.Y / 2 - 5), 0xFFFFFFFF, $"{tempMid:F0}K");
    }

    private void DrawIsosurfaceGenerator()
    {
        var results = _options.Dataset.ThermalResults;
        if (results?.TemperatureField == null) return;

        if (_isosurfaceProgressDialog.IsActive)
        {
            _isosurfaceProgressDialog.Submit();
            ImGui.BeginDisabled();
        }

        ImGui.Text($"Temperature range: {_options.TemperatureCold:F1} K to {_options.TemperatureHot:F1} K");
        ImGui.Text(
            $"                   {_options.TemperatureCold - 273.15:F1} C to {_options.TemperatureHot - 273.15:F1} C");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(150);
        ImGui.InputDouble("Temperature (K)", ref _isosurfaceValue);
        ImGui.SameLine();
        if (ImGui.SmallButton("Mid")) _isosurfaceValue = (_options.TemperatureHot + _options.TemperatureCold) / 2.0;

        var tempC = _isosurfaceValue - 273.15;
        ImGui.Text($"= {tempC:F2} C");

        if (ImGui.Button("Generate Single Isosurface", new Vector2(-1, 0)))
            _ = GenerateIsosurfaceAsync(_isosurfaceValue);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Batch Generation:");
        ImGui.SetNextItemWidth(150);
        ImGui.SliderInt("Number", ref _numIsosurfaces, 2, 20);

        if (ImGui.Button("Generate Multiple Isosurfaces", new Vector2(-1, 0)))
            _ = GenerateMultipleIsosurfacesAsync(_numIsosurfaces);

        if (results.IsosurfaceMeshes.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text($"Generated Meshes ({results.IsosurfaceMeshes.Count}):");

            if (ImGui.BeginTable("MeshTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("Vertices");
                ImGui.TableSetupColumn("Faces");
                ImGui.TableSetupColumn("Actions");
                ImGui.TableHeadersRow();

                for (var i = results.IsosurfaceMeshes.Count - 1; i >= 0; i--)
                {
                    var mesh = results.IsosurfaceMeshes[i];

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(mesh.Name);

                    ImGui.TableNextColumn();
                    ImGui.Text(mesh.VertexCount.ToString());

                    ImGui.TableNextColumn();
                    ImGui.Text(mesh.FaceCount.ToString());

                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"STL##{i}")) _stlExportDialog.Open($"{mesh.Name}.stl");
                    if (_stlExportDialog.Submit()) MeshExporter.ExportToStl(mesh, _stlExportDialog.SelectedPath);

                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Remove##{i}"))
                    {
                        ProjectManager.Instance.RemoveDataset(mesh);
                        results.IsosurfaceMeshes.RemoveAt(i);
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button("Clear All Meshes", new Vector2(-1, 0)))
            {
                foreach (var mesh in results.IsosurfaceMeshes) ProjectManager.Instance.RemoveDataset(mesh);
                results.IsosurfaceMeshes.Clear();
            }
        }

        if (_isosurfaceProgressDialog.IsActive) ImGui.EndDisabled();
    }

    private async Task GenerateIsosurfaceAsync(double temperature)
    {
        _isosurfaceProgressDialog.Open("Generating isosurface...");
        try
        {
            var results = _options.Dataset.ThermalResults;
            Logger.Log($"[ThermalTool] Generating isosurface at {temperature:F2} K");

            var progress =
                new Progress<(float p, string msg)>(report => _isosurfaceProgressDialog.Update(report.p, report.msg)
                );

            var voxelSize = new Vector3(
                _options.Dataset.PixelSize * 1e-6f,
                _options.Dataset.PixelSize * 1e-6f,
                _options.Dataset.SliceThickness * 1e-6f
            );

            var mesh = await IsosurfaceGenerator.GenerateIsosurfaceAsync(
                results.TemperatureField,
                (float)temperature,
                voxelSize,
                progress,
                _isosurfaceProgressDialog.CancellationToken
            );

            if (mesh.VertexCount > 0)
            {
                ProjectManager.Instance.AddDataset(mesh);
                results.IsosurfaceMeshes.Add(mesh);
                Logger.Log($"[ThermalTool] Generated mesh: {mesh.VertexCount} vertices, {mesh.FaceCount} faces");
            }
            else
            {
                Logger.LogWarning($"[ThermalTool] No surface found at {temperature:F2} K");
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("[ThermalTool] Isosurface generation canceled");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Isosurface generation failed: {ex.Message}");
        }
        finally
        {
            _isosurfaceProgressDialog.Close();
        }
    }

    private async Task GenerateMultipleIsosurfacesAsync(int count)
    {
        _isosurfaceProgressDialog.Open($"Batch generating {count} surfaces...");
        try
        {
            var results = _options.Dataset.ThermalResults;
            var tempRange = _options.TemperatureHot - _options.TemperatureCold;
            var voxelSize = new Vector3(
                _options.Dataset.PixelSize * 1e-6f,
                _options.Dataset.PixelSize * 1e-6f,
                _options.Dataset.SliceThickness * 1e-6f
            );

            var generated = 0;
            for (var i = 1; i <= count; i++)
            {
                _isosurfaceProgressDialog.CancellationToken.ThrowIfCancellationRequested();

                var temperature = _options.TemperatureCold + i * tempRange / (count + 1);
                var overallProgress = (float)(i - 1) / count;
                _isosurfaceProgressDialog.Update(overallProgress, $"Surface {i}/{count} at {temperature:F2} K");

                var subProgress = new Progress<(float p, string msg)>();
                var mesh = await IsosurfaceGenerator.GenerateIsosurfaceAsync(
                    results.TemperatureField,
                    (float)temperature,
                    voxelSize,
                    subProgress,
                    _isosurfaceProgressDialog.CancellationToken
                );

                if (mesh.VertexCount > 0)
                {
                    ProjectManager.Instance.AddDataset(mesh);
                    results.IsosurfaceMeshes.Add(mesh);
                    generated++;
                }
            }

            Logger.Log($"[ThermalTool] Generated {generated}/{count} isosurfaces");
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("[ThermalTool] Batch generation canceled");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Batch generation failed: {ex.Message}");
        }
        finally
        {
            _isosurfaceProgressDialog.Close();
        }
    }

    private void StartSimulation()
    {
        if (!ValidateSettings(out var validationMessages))
        {
            Logger.LogError("[ThermalTool] Validation failed");
            return;
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        _progressDialog.Open("Initializing simulation...");
        _isSimulationRunning = true;

        Logger.Log("[ThermalTool] ========== STARTING SIMULATION ==========");

        _simulationTask = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<float>(p =>
                {
                    var stage = p switch
                    {
                        < 0.05f => "Initializing...",
                        < 0.10f => "Loading properties...",
                        < 0.15f => "Setting up solver...",
                        < 0.85f => $"Solving ({(int)(p * 100)}%)...",
                        < 0.90f => "Computing k_eff...",
                        < 0.95f => "Analytical estimates...",
                        _ => "Finalizing..."
                    };
                    _progressDialog.Update(p, stage);
                });

                var results = await Task.Run(() =>
                        ThermalConductivitySolver.Solve(_options, progress, _cancellationTokenSource.Token),
                    _cancellationTokenSource.Token);

                _options.Dataset.ThermalResults = results;
                ProjectManager.Instance.NotifyDatasetDataChanged(_options.Dataset);

                Logger.Log(
                    $"[ThermalTool] ========== COMPLETE: k_eff = {results.EffectiveConductivity:F6} W/m·K ==========");
            }
            catch (OperationCanceledException)
            {
                Logger.Log("[ThermalTool] ========== CANCELED BY USER ==========");
                _options.Dataset.ThermalResults = null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ThermalTool] ========== FAILED: {ex.Message} ==========");
                Logger.LogError($"[ThermalTool] Stack trace: {ex.StackTrace}");
                _options.Dataset.ThermalResults = null;
            }
            finally
            {
                Logger.Log("[ThermalTool] Simulation task has finished.");
                // The UI thread is responsible for detecting task completion and updating its own state.
                // We ensure the dialog is closed from the background thread.
                _progressDialog.Close();
            }
        });
    }

    private void ExportSummaryToCsv(string path)
    {
        try
        {
            var results = _options.Dataset.ThermalResults;
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("# Thermal Conductivity Analysis Results");
                writer.WriteLine($"# Dataset: {_options.Dataset.Name}");
                writer.WriteLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine("#");
                writer.WriteLine();

                writer.WriteLine("## Summary Statistics");
                writer.WriteLine("Parameter,Value,Unit");
                writer.WriteLine($"Effective Thermal Conductivity,{results.EffectiveConductivity:F6},W/mK");
                writer.WriteLine($"Computation Time,{results.ComputationTime.TotalSeconds:F3},seconds");
                writer.WriteLine($"Hot Boundary Temperature,{_options.TemperatureHot:F2},K");
                writer.WriteLine($"Cold Boundary Temperature,{_options.TemperatureCold:F2},K");
                writer.WriteLine();

                writer.WriteLine("## Material Conductivities");
                writer.WriteLine("Material ID,Material Name,Conductivity (W/mK)");
                // Exclude exterior material (ID: 0) from export
                foreach (var material in _options.Dataset.Materials.Where(m => m.ID != 0).OrderBy(m => m.ID))
                {
                    var conductivity = results.MaterialConductivities.ContainsKey(material.ID)
                        ? results.MaterialConductivities[material.ID]
                        : 0.0;
                    writer.WriteLine($"{material.ID},{material.Name},{conductivity:F6}");
                }

                writer.WriteLine();

                if (results.AnalyticalEstimates.Count > 0)
                {
                    writer.WriteLine("## Analytical Model Estimates");
                    writer.WriteLine("Model,Conductivity (W/mK),Relative Error (%)");
                    foreach (var (name, value) in results.AnalyticalEstimates.OrderBy(x => x.Value))
                    {
                        var relativeError = (results.EffectiveConductivity - value) / results.EffectiveConductivity *
                                            100.0;
                        writer.WriteLine($"{name},{value:F6},{relativeError:F2}");
                    }
                }
            }

            Logger.Log($"[ThermalTool] Exported results to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to export CSV: {ex.Message}");
        }
    }

    private void ExportSliceToCsv(string path, float[,] slice)
    {
        if (slice == null) return;
        try
        {
            var width = slice.GetLength(0);
            var height = slice.GetLength(1);
            var sb = new StringBuilder();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    sb.Append(slice[x, y].ToString("F4"));
                    if (x < width - 1) sb.Append(",");
                }

                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
            Logger.Log($"[ThermalTool] Exported slice data to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to export slice CSV: {ex.Message}");
        }
    }

    private void ExportSliceToPng(string filePath, float[,] slice, int width, int height)
    {
        try
        {
            var imageData = new byte[width * height * 4];

            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var temp = slice[x, y];
                    var normalizedTemp = (temp - _options.TemperatureCold) /
                                         (_options.TemperatureHot - _options.TemperatureCold);
                    normalizedTemp = Math.Clamp(normalizedTemp, 0.0, 1.0);

                    var color = ApplyColorMap((float)normalizedTemp, _colorMapIndex);

                    var idx = (y * width + x) * 4;
                    imageData[idx + 0] = (byte)(color.X * 255);
                    imageData[idx + 1] = (byte)(color.Y * 255);
                    imageData[idx + 2] = (byte)(color.Z * 255);
                    imageData[idx + 3] = (byte)(color.W * 255);
                }
            });

            if (_showIsocontours)
            {
                var tempRange = _options.TemperatureHot - _options.TemperatureCold;
                for (var i = 1; i <= _numIsocontours; i++)
                {
                    var isovalue = _options.TemperatureCold + i * tempRange / (_numIsocontours + 1);
                    var lines = IsosurfaceGenerator.GenerateIsocontours(slice, (float)isovalue);

                    foreach (var (p1, p2) in lines)
                        DrawLineOnImage(imageData, width, height,
                            (int)p1.X, (int)p1.Y, (int)p2.X, (int)p2.Y,
                            255, 255, 255, 255);
                }
            }

            ImageExporter.ExportColorSlice(imageData, width, height, filePath);
            Logger.Log($"[ThermalTool] Exported slice to {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to export PNG: {ex.Message}");
        }
    }

    private void DrawLineOnImage(byte[] imageData, int width, int height,
        int x0, int y0, int x1, int y1, byte r, byte g, byte b, byte a)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                var idx = (y0 * width + x0) * 4;
                imageData[idx + 0] = r;
                imageData[idx + 1] = g;
                imageData[idx + 2] = b;
                imageData[idx + 3] = a;
            }

            if (x0 == x1 && y0 == y1) break;

            var e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private void ExportCompositeImage(string filePath)
    {
        try
        {
            var (slice, sliceWidth, sliceHeight) = GetSelectedSlice();
            if (slice == null) return;

            var padding = 20;
            var legendWidth = 60;
            var infoWidth = 250;
            var compositeWidth = sliceWidth + legendWidth + infoWidth + padding * 4;
            var compositeHeight = Math.Max(sliceHeight, 400) + padding * 2;

            var buffer = new byte[compositeWidth * compositeHeight * 4];
            Array.Fill(buffer, (byte)50);
            for (var i = 3; i < buffer.Length; i += 4) buffer[i] = 255;

            // Draw temperature slice
            for (var y = 0; y < sliceHeight; y++)
            for (var x = 0; x < sliceWidth; x++)
            {
                var temp = slice[x, y];
                var norm = Math.Clamp(
                    (temp - _options.TemperatureCold) / (_options.TemperatureHot - _options.TemperatureCold), 0.0, 1.0);
                var color = ApplyColorMap((float)norm, _colorMapIndex);

                var destIdx = ((y + padding) * compositeWidth + x + padding) * 4;
                buffer[destIdx + 0] = (byte)(color.X * 255);
                buffer[destIdx + 1] = (byte)(color.Y * 255);
                buffer[destIdx + 2] = (byte)(color.Z * 255);
                buffer[destIdx + 3] = 255;
            }

            // Draw color legend
            var legendX = sliceWidth + padding * 2;
            var legendY = padding;
            var barWidth = 30;
            for (var i = 0; i < sliceHeight; i++)
            {
                var color = ApplyColorMap((float)i / (sliceHeight - 1), _colorMapIndex);
                for (var j = 0; j < barWidth; j++)
                {
                    var destIdx = ((sliceHeight - 1 - i + legendY) * compositeWidth + j + legendX) * 4;
                    buffer[destIdx + 0] = (byte)(color.X * 255);
                    buffer[destIdx + 1] = (byte)(color.Y * 255);
                    buffer[destIdx + 2] = (byte)(color.Z * 255);
                }
            }

            ImageExporter.ExportColorSlice(buffer, compositeWidth, compositeHeight, filePath);
            Logger.Log($"[ThermalTool] Exported composite image to {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to export composite image: {ex.Message}");
        }
    }

    private void ExportTextReport(string filePath, bool isRtf)
    {
        try
        {
            var sb = new StringBuilder();
            var results = _options.Dataset.ThermalResults;

            sb.AppendLine("========================================");
            sb.AppendLine("   Thermal Conductivity Analysis Report");
            sb.AppendLine("========================================");
            sb.AppendLine();
            sb.AppendLine($"Dataset: {_options.Dataset.Name}");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("--- Simulation Summary ---");
            sb.AppendLine($"Effective Thermal Conductivity: {results.EffectiveConductivity:F6} W/mK");
            sb.AppendLine($"Computation Time: {results.ComputationTime.TotalSeconds:F3} seconds");
            sb.AppendLine();
            sb.AppendLine("--- Simulation Parameters ---");
            sb.AppendLine($"Hot Temperature: {_options.TemperatureHot:F2} K");
            sb.AppendLine($"Cold Temperature: {_options.TemperatureCold:F2} K");
            sb.AppendLine($"Heat Flow Direction: {_options.HeatFlowDirection}");
            sb.AppendLine($"Solver Backend: {_options.SolverBackend}");

            var content = sb.ToString();

            if (isRtf)
            {
                var rtf = new StringBuilder();
                rtf.AppendLine(@"{\rtf1\ansi\deff0");
                rtf.AppendLine(@"{\fonttbl{\f0 Arial;}}");
                rtf.AppendLine(@"\pard\f0\fs24");
                foreach (var line in content.Split(Environment.NewLine))
                    rtf.AppendLine(line.Replace(@"\", @"\\") + @"\par");
                rtf.AppendLine("}");
                content = rtf.ToString();
            }

            File.WriteAllText(filePath, content);
            Logger.Log($"[ThermalTool] Exported report to {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermalTool] Failed to export report: {ex.Message}");
        }
    }

    private (float[,] slice, int width, int height) GetSelectedSlice()
    {
        var results = _options.Dataset.ThermalResults;
        if (results?.TemperatureField == null) return (null, 0, 0);

        var W = _options.Dataset.Width;
        var H = _options.Dataset.Height;
        var D = _options.Dataset.Depth;
        var selectedDirection = (HeatFlowDirection)_selectedSliceDirectionInt;

        _selectedSliceIndex = Math.Clamp(_selectedSliceIndex, 0,
            selectedDirection == HeatFlowDirection.X ? W - 1 :
            selectedDirection == HeatFlowDirection.Y ? H - 1 : D - 1);

        if (results.TemperatureSlices.TryGetValue((selectedDirection.ToString()[0], _selectedSliceIndex),
                out var slice)) return (slice, slice.GetLength(0), slice.GetLength(1));

        switch (selectedDirection)
        {
            case HeatFlowDirection.X:
                var sliceX = new float[H, D];
                Parallel.For(0, H, y =>
                {
                    for (var z = 0; z < D; z++) sliceX[y, z] = results.TemperatureField[_selectedSliceIndex, y, z];
                });
                results.TemperatureSlices[('X', _selectedSliceIndex)] = sliceX;
                return (sliceX, H, D);
            case HeatFlowDirection.Y:
                var sliceY = new float[W, D];
                Parallel.For(0, W, x =>
                {
                    for (var z = 0; z < D; z++) sliceY[x, z] = results.TemperatureField[x, _selectedSliceIndex, z];
                });
                results.TemperatureSlices[('Y', _selectedSliceIndex)] = sliceY;
                return (sliceY, W, D);
            case HeatFlowDirection.Z:
                var sliceZ = new float[W, H];
                Parallel.For(0, W, x =>
                {
                    for (var y = 0; y < H; y++) sliceZ[x, y] = results.TemperatureField[x, y, _selectedSliceIndex];
                });
                results.TemperatureSlices[('Z', _selectedSliceIndex)] = sliceZ;
                return (sliceZ, W, H);
        }

        return (null, 0, 0);
    }

    private static void InitializeColormaps()
    {
        if (_colormapData != null) return;
        const int size = 256;
        _colormapData = new Vector3[2, size];

        // Hot (map 0)
        for (var i = 0; i < size; i++)
        {
            var t = i / (float)(size - 1);
            var r = Math.Min(1.0f, 3.0f * t);
            var g = Math.Clamp(3.0f * t - 1.0f, 0.0f, 1.0f);
            var b = Math.Clamp(3.0f * t - 2.0f, 0.0f, 1.0f);
            _colormapData[0, i] = new Vector3(r, g, b);
        }

        // Rainbow (map 1)
        for (var i = 0; i < size; i++)
        {
            var h = i / (float)(size - 1) * 0.7f;
            _colormapData[1, i] = HsvToRgb(h, 1.0f, 1.0f);
        }
    }

    private Vector4 ApplyColorMap(float normalizedIntensity, int colorMapIndex)
    {
        var mapIdx = Math.Clamp(colorMapIndex, 0, 1);
        var texelIdx = (int)(normalizedIntensity * 255);
        texelIdx = Math.Clamp(texelIdx, 0, 255);
        var rgb = _colormapData[mapIdx, texelIdx];
        return new Vector4(rgb.X, rgb.Y, rgb.Z, 1.0f);
    }

    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        float r, g, b;
        var i = (int)(h * 6);
        var f = h * 6 - i;
        var p = v * (1 - s);
        var q = v * (1 - f * s);
        var t = v * (1 - (1 - f) * s);
        switch (i % 6)
        {
            case 0:
                r = v;
                g = t;
                b = p;
                break;
            case 1:
                r = q;
                g = v;
                b = p;
                break;
            case 2:
                r = p;
                g = v;
                b = t;
                break;
            case 3:
                r = p;
                g = q;
                b = v;
                break;
            case 4:
                r = t;
                g = p;
                b = v;
                break;
            default:
                r = v;
                g = p;
                b = q;
                break;
        }

        return new Vector3(r, g, b);
    }
}