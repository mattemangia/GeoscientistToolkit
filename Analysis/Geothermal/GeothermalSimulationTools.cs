// GeoscientistToolkit/Analysis/Geothermal/GeothermalSimulationTools.cs

using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.UI.Visualization;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     ImGui tool for configuring and running geothermal simulations on borehole data.
/// </summary>
public class GeothermalSimulationTools : IDatasetTools, IDisposable
{
    // Export file dialog
    private readonly ImGuiExportFileDialog _exportDialog = new("geothermal_export", "Export Geothermal Results");
    private readonly float _newLayerPermeability = 1e-14f;
    private readonly GeothermalSimulationOptions _options = new();
    private CancellationTokenSource _cancellationTokenSource;
    private BoreholeCrossSectionViewer _crossSectionViewer;

    // BTES Curve Editor
    private ImGuiCurveEditor _btesCurveEditor;
    private GeothermalSimulationSolver _currentSolver; // Track the active solver

    private bool _isSimulationRunning;
    private string _mappedDensityColumn = "";
    private string _mappedPermeabilityColumn = "";
    private string _mappedPorosityColumn = "";
    private string _mappedSpecificHeatColumn = "";

    // Parameter column mapping
    private string _mappedThermalConductivityColumn = "";

    private GeothermalMesh _mesh;
    private GeothermalMeshPreview _meshPreview;
    private string _meshPreviewInitError; // Store init errors to show in window
    private bool _meshPreviewWindowLoggedOnce;
    private float _newIsosurfaceTemp = 20f;
    private float _newLayerConductivity = 2.5f;
    private float _newLayerDensity = 2650f;

    // Material property editing
    private string _newLayerName = "";
    private float _newLayerPorosity = 0.1f;
    private float _newLayerSpecificHeat = 900f;
    private GeothermalSimulationResults _results;
    private int _selectedFlowConfig;

    // UI state
    private int _selectedHeatExchangerType;
    private int _selectedIsosurface;
    private int _selectedPreset; // Add preset selection state
    private int _selectedResultTab = 0;
    private bool _show3DVisualization;
    private bool _showAdvancedOptions;
    private bool _showMeshPreview;
    private bool _showMeshPreviewInPanel; // NEW: Alternative rendering mode
    private bool _showParameterMapping;
    private bool _showResults;
    private string _simulationMessage = "";
    private float _simulationProgress;
    private GeothermalVisualization3D _visualization3D;

    // ORC power generation
    private ORCVisualization _orcVisualization;
    private ORCSimulation.ORCCycleResults[] _orcResults;
    private EconomicResults _economicResults;

    public void Draw(Dataset dataset)
    {
        if (dataset is not BoreholeDataset boreholeDataset)
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "This tool requires a borehole dataset!");
            return;
        }

        // Initialize options if needed
        if (_options.BoreholeDataset != boreholeDataset)
        {
            _options.BoreholeDataset = boreholeDataset;
            // --- MODIFICATION START ---
            // Ensure Heat Exchanger Depth is reset to the new borehole's total depth on dataset change
            _options.HeatExchangerDepth = boreholeDataset.TotalDepth;
            // --- MODIFICATION END ---

            // CRITICAL FIX: Clear existing layer data when loading new borehole
            _options.LayerThermalConductivities.Clear();
            _options.LayerSpecificHeats.Clear();
            _options.LayerDensities.Clear();
            _options.LayerPorosities.Clear();
            _options.LayerPermeabilities.Clear();

            // Auto-detect parameter columns when loading a new dataset
            AutoDetectParameterColumns(boreholeDataset);

            // Now initialize from borehole FIRST, then fill in any missing defaults
            InitializeLayerProperties(boreholeDataset);
            _options.SetDefaultValues(); // Only fills in layers that don't exist
        }

        ImGui.Separator();

        // Show dataset info
        ImGui.Text($"Well: {boreholeDataset.WellName}");
        ImGui.Text($"Depth: {boreholeDataset.TotalDepth:F1} m");
        ImGui.Text($"Diameter: {boreholeDataset.WellDiameter * 1000:F0} mm");
        ImGui.Text($"Lithology Units: {boreholeDataset.LithologyUnits.Count}");

        // Validate borehole data
        if (boreholeDataset.TotalDepth <= 0)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Error: Invalid borehole depth!");
            return;
        }

        if (boreholeDataset.LithologyUnits.Count == 0)
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1),
                "Warning: No lithology units defined. Using default properties.");

        ImGui.Separator();

        if (_isSimulationRunning)
            RenderSimulationProgress();
        else
            RenderConfiguration();

        // Always call RenderMeshPreviewModal to handle the popup lifecycle
        RenderMeshPreviewModal();

        // Render results in a separate window when available
        if (_results != null && _showResults) RenderResultsWindow();

        // Handle export dialog
        if (_exportDialog.IsOpen)
            if (_exportDialog.Submit())
            {
                var selectedPath = _exportDialog.SelectedPath;
                ExportResults(selectedPath);
            }

        // Handle BTES curve editor
        HandleBTESCurveEditor();
    }


    public void Dispose()
    {
        _visualization3D?.Dispose();
        _meshPreview?.Dispose();
        _orcVisualization?.Dispose();
        _cancellationTokenSource?.Dispose();
    }

    private void InitializeLayerProperties(BoreholeDataset boreholeDataset)
    {
        // Initialize with borehole-specific lithology
        // This will use the ACTUAL layer names from the borehole
        foreach (var unit in boreholeDataset.LithologyUnits)
        {
            // CRITICAL FIX: Use actual unit Name, not generic RockType
            // RockType is a category (e.g., "Sandstone")
            // Name is the specific unit (e.g., "Sandstone Aquifer 1")
            var name = !string.IsNullOrEmpty(unit.Name) ? unit.Name : unit.RockType ?? "Unknown";

            // Try to get parameters using mapped columns first, then fall back to default names
            var conductivity = GetParameterValue(unit, _mappedThermalConductivityColumn,
                new[] { "ThermalConductivity", "Thermal Conductivity", "TC", "K", "Lambda" }, 2.5);
            _options.LayerThermalConductivities[name] = conductivity;

            var specificHeat = GetParameterValue(unit, _mappedSpecificHeatColumn,
                new[] { "SpecificHeat", "Specific Heat", "Cp", "SH" }, 900);
            _options.LayerSpecificHeats[name] = specificHeat;

            var density = GetParameterValue(unit, _mappedDensityColumn,
                new[] { "Density", "Rho", "RHO", "RHOB" }, 2650);
            _options.LayerDensities[name] = density;

            var porosity = GetParameterValue(unit, _mappedPorosityColumn,
                new[] { "Porosity", "PHI", "PHIT", "Por" }, 0.1);
            // Handle both percentage and decimal
            porosity = porosity > 1 ? porosity / 100.0 : porosity;
            _options.LayerPorosities[name] = porosity;

            var permeability = GetParameterValue(unit, _mappedPermeabilityColumn,
                new[] { "Permeability", "Perm", "K", "PERM" }, 1e-14);
            _options.LayerPermeabilities[name] = permeability;
        }
    }

    /// <summary>
    ///     Gets a parameter value from a lithology unit, trying the mapped column first,
    ///     then falling back to a list of common parameter names
    /// </summary>
    private double GetParameterValue(LithologyUnit unit, string mappedColumn, string[] fallbackNames,
        double defaultValue)
    {
        // Try mapped column first
        if (!string.IsNullOrEmpty(mappedColumn) && unit.Parameters.TryGetValue(mappedColumn, out var mappedValue))
            return mappedValue;

        // Try fallback names
        foreach (var name in fallbackNames)
            if (unit.Parameters.TryGetValue(name, out var value))
                return value;

        // Return default
        return defaultValue;
    }

    /// <summary>
    ///     Automatically detects the best matching columns for required parameters based on common naming conventions
    /// </summary>
    private void AutoDetectParameterColumns(BoreholeDataset boreholeDataset)
    {
        if (boreholeDataset?.ParameterTracks == null || !boreholeDataset.ParameterTracks.Any())
            return;

        var availableColumns = boreholeDataset.ParameterTracks.Keys.ToList();

        // Thermal Conductivity patterns
        var tcPatterns = new[]
            { "thermalconductivity", "thermal_conductivity", "tc", "lambda", "conductivity", "k_thermal" };
        _mappedThermalConductivityColumn = FindBestMatch(availableColumns, tcPatterns);

        // Specific Heat patterns
        var shPatterns = new[] { "specificheat", "specific_heat", "cp", "sh", "heat_capacity" };
        _mappedSpecificHeatColumn = FindBestMatch(availableColumns, shPatterns);

        // Density patterns
        var densPatterns = new[] { "density", "rho", "rhob", "bulk_density", "dens" };
        _mappedDensityColumn = FindBestMatch(availableColumns, densPatterns);

        // Porosity patterns
        var porPatterns = new[] { "porosity", "phi", "phit", "por", "poros" };
        _mappedPorosityColumn = FindBestMatch(availableColumns, porPatterns);

        // Permeability patterns
        var permPatterns = new[] { "permeability", "perm", "k_perm", "hydraulic_conductivity" };
        _mappedPermeabilityColumn = FindBestMatch(availableColumns, permPatterns);

        Logger.Log($"Auto-detected columns: TC={_mappedThermalConductivityColumn}, SH={_mappedSpecificHeatColumn}, " +
                   $"Dens={_mappedDensityColumn}, Por={_mappedPorosityColumn}, Perm={_mappedPermeabilityColumn}");
    }

    /// <summary>
    ///     Finds the best matching column name from available columns based on pattern matching
    /// </summary>
    private string FindBestMatch(List<string> availableColumns, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            // Try exact match (case-insensitive)
            var exactMatch = availableColumns.FirstOrDefault(c =>
                string.Equals(c, pattern, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
                return exactMatch;
        }

        // Try partial match
        foreach (var pattern in patterns)
        {
            var partialMatch = availableColumns.FirstOrDefault(c =>
                c.ToLowerInvariant().Contains(pattern));
            if (partialMatch != null)
                return partialMatch;
        }

        return ""; // No match found
    }

    private void RenderConfiguration()
    {
        ImGui.Text("Simulation Configuration");
        ImGui.Separator();

        // Quick Results Summary (if available)
        if (_results != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.7f, 0.4f, 1.0f));
            if (ImGui.CollapsingHeader("📊 Quick Results Summary", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.PopStyleColor();
                ImGui.Indent();

                ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "Key Performance Metrics:");
                ImGui.Separator();

                ImGui.Text($"Average Heat Rate: {_results.AverageHeatExtractionRate:F0} W");
                ImGui.Text($"Total Energy Extracted: {_results.TotalExtractedEnergy / 1e9:F2} GJ");
                ImGui.Text($"Borehole Resistance: {_results.BoreholeThermalResistance:F3} m·K/W");
                ImGui.Text($"Thermal Influence Radius: {_results.ThermalInfluenceRadius:F1} m");

                if (_results.OutletTemperature.Any())
                {
                    var finalTemp = _results.OutletTemperature.Last().temperature - 273.15;
                    ImGui.Text($"Final Outlet Temperature: {finalTemp:F1} °C");
                }

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f),
                    "👉 Click 'Show Results' for detailed analysis");

                ImGui.Unindent();
            }
            else
            {
                ImGui.PopStyleColor();
            }

            ImGui.Separator();
        }

        // Preset Selector
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.4f, 0.7f, 1.0f));
        if (ImGui.CollapsingHeader("Simulation Presets", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.PopStyleColor();

            ImGui.TextWrapped(
                "Select a preset to quickly configure the simulation for common scenarios. You can modify any parameter after applying a preset.");
            ImGui.Spacing();

            var presetNames = new[]
            {
                "Custom (Current Settings)",
                "Shallow GSHP (50-200m)",
                "Medium Depth Heating (500-1500m)",
                "Deep Geothermal Production (2000-5000m)",
                "Enhanced Geothermal System (3000-6000m)",
                "Aquifer Thermal Storage (50-300m)",
                "BTES Thermal Battery (50-300m)",
                "Quick Exploration Test"
            };

            var currentPreset = _selectedPreset;
            if (ImGui.Combo("Preset Configuration", ref currentPreset, presetNames, presetNames.Length))
            {
                _selectedPreset = currentPreset;
                if (_selectedPreset > 0) // Not "Custom"
                {
                    var preset = (GeothermalSimulationPreset)_selectedPreset;
                    _options.ApplyPreset(preset);

                    // Update UI state to match preset
                    _selectedHeatExchangerType = (int)_options.HeatExchangerType;
                    _selectedFlowConfig = (int)_options.FlowConfiguration;
                }
            }

            // Show description of selected preset
            if (_selectedPreset > 0)
            {
                var preset = (GeothermalSimulationPreset)_selectedPreset;
                var description = GeothermalSimulationOptions.GetPresetDescription(preset);
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), $"📋 {description}");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.5f, 1.0f),
                    "💡 Using custom parameters. Any changes will switch to Custom mode.");
            }

            ImGui.Spacing();
            ImGui.Separator();
        }
        else
        {
            ImGui.PopStyleColor();
        }

        // BTES (Thermal Battery) Mode Section
        if (_options.EnableBTESMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.8f, 0.3f, 0.2f, 1.0f));
            if (ImGui.CollapsingHeader("🔋 BTES Thermal Battery Configuration", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.PopStyleColor();

                ImGui.TextWrapped(
                    "Borehole Thermal Energy Storage (BTES) mode simulates seasonal heat storage cycles. " +
                    "Heat is injected in summer (charging) and extracted in winter (discharging).");
                ImGui.Spacing();

                var btesEnabled = _options.EnableBTESMode;
                if (ImGui.Checkbox("Enable BTES Mode", ref btesEnabled))
                {
                    _options.EnableBTESMode = btesEnabled;
                    if (btesEnabled && _options.SeasonalEnergyCurve.Count == 0)
                    {
                        _options.InitializeDefaultSeasonalCurve();
                    }
                    _selectedPreset = 0; // Switch to Custom
                }

                if (_options.EnableBTESMode)
                {
                    ImGui.Indent();

                    // Annual energy storage
                    var annualEnergy = (float)_options.BTESAnnualEnergyStorage;
                    if (ImGui.SliderFloat("Annual Energy Storage (MWh/year)", ref annualEnergy, 100, 5000, "%.0f"))
                    {
                        _options.BTESAnnualEnergyStorage = annualEnergy;
                        _selectedPreset = 0;
                    }

                    // Peak ratio
                    var peakRatio = (float)_options.BTESSeasonalPeakRatio;
                    if (ImGui.SliderFloat("Peak to Average Ratio", ref peakRatio, 1.0f, 5.0f, "%.1f"))
                    {
                        _options.BTESSeasonalPeakRatio = peakRatio;
                        _selectedPreset = 0;
                    }

                    // Charging temperature
                    var chargingTemp = (float)(_options.BTESChargingTemperature - 273.15);
                    if (ImGui.SliderFloat("Charging Temperature (°C)", ref chargingTemp, 30, 90, "%.1f"))
                    {
                        _options.BTESChargingTemperature = chargingTemp + 273.15;
                        _selectedPreset = 0;
                    }

                    // Discharging temperature
                    var dischargingTemp = (float)(_options.BTESDischargingTemperature - 273.15);
                    if (ImGui.SliderFloat("Discharging Temperature (°C)", ref dischargingTemp, 0, 25, "%.1f"))
                    {
                        _options.BTESDischargingTemperature = dischargingTemp + 273.15;
                        _selectedPreset = 0;
                    }

                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Text("Seasonal Curve Options:");

                    // Random variations checkbox
                    var applyRandomVariations = _options.BTESApplyRandomVariations;
                    if (ImGui.Checkbox("Apply Random Weather Variations", ref applyRandomVariations))
                    {
                        _options.BTESApplyRandomVariations = applyRandomVariations;
                        _selectedPreset = 0;
                    }

                    if (_options.BTESApplyRandomVariations)
                    {
                        ImGui.Indent();

                        // Variation magnitude
                        var variationMag = (float)_options.BTESRandomVariationMagnitude * 100f;
                        if (ImGui.SliderFloat("Variation Magnitude (%)", ref variationMag, 0, 50, "%.0f"))
                        {
                            _options.BTESRandomVariationMagnitude = variationMag / 100f;
                            _selectedPreset = 0;
                        }

                        // Random seed
                        var randomSeed = _options.BTESRandomSeed;
                        if (ImGui.InputInt("Random Seed (0=random)", ref randomSeed))
                        {
                            _options.BTESRandomSeed = Math.Max(0, randomSeed);
                            _selectedPreset = 0;
                        }

                        ImGui.Unindent();
                    }

                    // Regenerate curve button
                    if (ImGui.Button("Regenerate Seasonal Curve"))
                    {
                        _options.InitializeDefaultSeasonalCurve();
                    }

                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"({_options.SeasonalEnergyCurve.Count} days)");

                    // Edit curve button
                    if (ImGui.Button("Edit Seasonal Curve..."))
                    {
                        InitializeBTESCurveEditor();
                        _btesCurveEditor?.Open();
                    }

                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "✏ Interactive curve editor");

                    // Save all frames checkbox
                    var saveAllFrames = _options.SaveAllTimeFrames;
                    if (ImGui.Checkbox("Save All Time Frames (for animation)", ref saveAllFrames))
                    {
                        _options.SaveAllTimeFrames = saveAllFrames;
                        _selectedPreset = 0;
                    }

                    if (_options.SaveAllTimeFrames)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "⚠ May require significant memory");
                    }

                    ImGui.Unindent();
                }

                ImGui.Spacing();
            }
            else
            {
                ImGui.PopStyleColor();
            }
        }

        // Parameter Column Mapping Section
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.7f, 0.4f, 0.2f, 1.0f));
        if (ImGui.CollapsingHeader("Parameter Column Mapping", ImGuiTreeNodeFlags.None))
        {
            ImGui.PopStyleColor();

            ImGui.TextWrapped(
                "Map columns from your dataset (LAS file, parameter tracks, etc.) to the required simulation parameters. " +
                "This is useful when your data uses different column names than the standard ones.");
            ImGui.Spacing();

            // Get available parameter tracks from dataset
            var availableColumns = new List<string> { "(None - use defaults)" };
            if (_options.BoreholeDataset?.ParameterTracks != null)
                availableColumns.AddRange(_options.BoreholeDataset.ParameterTracks.Keys.OrderBy(k => k));

            var columnsArray = availableColumns.ToArray();

            ImGui.Text("Available Columns in Dataset:");
            ImGui.Indent();
            if (availableColumns.Count > 1)
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f),
                    string.Join(", ", availableColumns.Skip(1)));
            else
                ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "No parameter tracks found in dataset");
            ImGui.Unindent();
            ImGui.Spacing();

            // Create mapping UI
            if (ImGui.BeginTable("ParamMapping", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Required Parameter", ImGuiTableColumnFlags.WidthFixed, 200);
                ImGui.TableSetupColumn("Mapped Column", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Default Fallbacks", ImGuiTableColumnFlags.WidthFixed, 250);
                ImGui.TableHeadersRow();

                // Thermal Conductivity
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Thermal Conductivity (W/m·K)");
                ImGui.TableNextColumn();
                var tcIdx = Array.IndexOf(columnsArray, _mappedThermalConductivityColumn);
                if (tcIdx < 0) tcIdx = 0;
                if (ImGui.Combo("##TC", ref tcIdx, columnsArray, columnsArray.Length))
                    _mappedThermalConductivityColumn = tcIdx > 0 ? columnsArray[tcIdx] : "";
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "TC, K, Lambda");

                // Specific Heat
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Specific Heat (J/kg·K)");
                ImGui.TableNextColumn();
                var shIdx = Array.IndexOf(columnsArray, _mappedSpecificHeatColumn);
                if (shIdx < 0) shIdx = 0;
                if (ImGui.Combo("##SH", ref shIdx, columnsArray, columnsArray.Length))
                    _mappedSpecificHeatColumn = shIdx > 0 ? columnsArray[shIdx] : "";
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Cp, SH");

                // Density
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Density (kg/m³)");
                ImGui.TableNextColumn();
                var densIdx = Array.IndexOf(columnsArray, _mappedDensityColumn);
                if (densIdx < 0) densIdx = 0;
                if (ImGui.Combo("##Dens", ref densIdx, columnsArray, columnsArray.Length))
                    _mappedDensityColumn = densIdx > 0 ? columnsArray[densIdx] : "";
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Rho, RHO, RHOB");

                // Porosity
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Porosity (fraction or %)");
                ImGui.TableNextColumn();
                var porIdx = Array.IndexOf(columnsArray, _mappedPorosityColumn);
                if (porIdx < 0) porIdx = 0;
                if (ImGui.Combo("##Por", ref porIdx, columnsArray, columnsArray.Length))
                    _mappedPorosityColumn = porIdx > 0 ? columnsArray[porIdx] : "";
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "PHI, PHIT, Por");

                // Permeability
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Permeability (m²)");
                ImGui.TableNextColumn();
                var permIdx = Array.IndexOf(columnsArray, _mappedPermeabilityColumn);
                if (permIdx < 0) permIdx = 0;
                if (ImGui.Combo("##Perm", ref permIdx, columnsArray, columnsArray.Length))
                    _mappedPermeabilityColumn = permIdx > 0 ? columnsArray[permIdx] : "";
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "K, PERM");

                ImGui.EndTable();
            }

            ImGui.Spacing();

            if (ImGui.Button("Apply Mapping & Refresh Properties", new Vector2(250, 0)))
            {
                // Re-initialize layer properties with new mapping
                _options.LayerThermalConductivities.Clear();
                _options.LayerSpecificHeats.Clear();
                _options.LayerDensities.Clear();
                _options.LayerPorosities.Clear();
                _options.LayerPermeabilities.Clear();
                InitializeLayerProperties(_options.BoreholeDataset);
                _options.SetDefaultValues(); // Fill in any missing defaults
            }

            ImGui.SetItemTooltip("Re-read all parameters from the dataset using the current column mapping");

            ImGui.SameLine();
            if (ImGui.Button("Auto-Detect Columns", new Vector2(150, 0)))
                AutoDetectParameterColumns(_options.BoreholeDataset);
            ImGui.SetItemTooltip("Automatically detect the best matching columns based on common naming conventions");

            // Show preview of extracted values
            if (_options.BoreholeDataset?.LithologyUnits != null && _options.BoreholeDataset.LithologyUnits.Any())
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Preview of Extracted Values:");

                if (ImGui.BeginTable("ParamPreview", 6,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                        ImGuiTableFlags.ScrollX,
                        new Vector2(0, 150)))
                {
                    ImGui.TableSetupColumn("Layer", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableSetupColumn("k (W/m·K)", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Cp (J/kg·K)", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableSetupColumn("ρ (kg/m³)", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("φ (-)", ImGuiTableColumnFlags.WidthFixed, 70);
                    ImGui.TableSetupColumn("K (m²)", ImGuiTableColumnFlags.WidthFixed, 90);
                    ImGui.TableHeadersRow();

                    foreach (var unit in _options.BoreholeDataset.LithologyUnits)
                    {
                        var name = !string.IsNullOrEmpty(unit.Name) ? unit.Name : unit.RockType ?? "Unknown";

                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(name.Length > 18 ? name.Substring(0, 15) + "..." : name);
                        if (name.Length > 18)
                            ImGui.SetItemTooltip(name);

                        ImGui.TableNextColumn();
                        var tc = GetParameterValue(unit, _mappedThermalConductivityColumn,
                            new[] { "ThermalConductivity", "Thermal Conductivity", "TC", "K", "Lambda" }, 2.5);
                        var tcColor = unit.Parameters.ContainsKey(_mappedThermalConductivityColumn) ||
                                      unit.Parameters.ContainsKey("ThermalConductivity") ||
                                      unit.Parameters.ContainsKey("Thermal Conductivity")
                            ? new Vector4(0.3f, 1.0f, 0.5f, 1.0f)
                            : new Vector4(1.0f, 0.7f, 0.3f, 1.0f); // Orange for defaults
                        ImGui.TextColored(tcColor, $"{tc:F2}");

                        ImGui.TableNextColumn();
                        var sh = GetParameterValue(unit, _mappedSpecificHeatColumn,
                            new[] { "SpecificHeat", "Specific Heat", "Cp", "SH" }, 900);
                        var shColor = unit.Parameters.ContainsKey(_mappedSpecificHeatColumn) ||
                                      unit.Parameters.ContainsKey("SpecificHeat") ||
                                      unit.Parameters.ContainsKey("Specific Heat")
                            ? new Vector4(0.3f, 1.0f, 0.5f, 1.0f)
                            : new Vector4(1.0f, 0.7f, 0.3f, 1.0f);
                        ImGui.TextColored(shColor, $"{sh:F0}");

                        ImGui.TableNextColumn();
                        var dens = GetParameterValue(unit, _mappedDensityColumn,
                            new[] { "Density", "Rho", "RHO", "RHOB" }, 2650);
                        var densColor = unit.Parameters.ContainsKey(_mappedDensityColumn) ||
                                        unit.Parameters.ContainsKey("Density")
                            ? new Vector4(0.3f, 1.0f, 0.5f, 1.0f)
                            : new Vector4(1.0f, 0.7f, 0.3f, 1.0f);
                        ImGui.TextColored(densColor, $"{dens:F0}");

                        ImGui.TableNextColumn();
                        var por = GetParameterValue(unit, _mappedPorosityColumn,
                            new[] { "Porosity", "PHI", "PHIT", "Por" }, 0.1);
                        por = por > 1 ? por / 100.0 : por;
                        var porColor = unit.Parameters.ContainsKey(_mappedPorosityColumn) ||
                                       unit.Parameters.ContainsKey("Porosity")
                            ? new Vector4(0.3f, 1.0f, 0.5f, 1.0f)
                            : new Vector4(1.0f, 0.7f, 0.3f, 1.0f);
                        ImGui.TextColored(porColor, $"{por:F3}");

                        ImGui.TableNextColumn();
                        var perm = GetParameterValue(unit, _mappedPermeabilityColumn,
                            new[] { "Permeability", "Perm", "K", "PERM" }, 1e-14);
                        var permColor = unit.Parameters.ContainsKey(_mappedPermeabilityColumn) ||
                                        unit.Parameters.ContainsKey("Permeability")
                            ? new Vector4(0.3f, 1.0f, 0.5f, 1.0f)
                            : new Vector4(1.0f, 0.7f, 0.3f, 1.0f);
                        ImGui.TextColored(permColor, $"{perm:E2}");
                    }

                    ImGui.EndTable();
                }

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "● ");
                ImGui.SameLine();
                ImGui.Text("Value from dataset  ");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f), "● ");
                ImGui.SameLine();
                ImGui.Text("Default value (not found in dataset)");
            }

            ImGui.Spacing();
            ImGui.Separator();
        }
        else
        {
            ImGui.PopStyleColor();
        }

        if (ImGui.Button("Run Simulation", new Vector2(200, 30))) StartSimulation();

        ImGui.SameLine();
        if (_results != null && ImGui.Button("Show Results", new Vector2(200, 30))) _showResults = true;

        ImGui.SameLine();
        if (ImGui.Button("Mesh Preview", new Vector2(200, 30)))
        {
            _showMeshPreview = !_showMeshPreview;
            Logger.Log($"Mesh Preview button clicked. _showMeshPreview = {_showMeshPreview}");

            if (_showMeshPreview)
            {
                Logger.Log("Initializing mesh preview...");
                InitializeMeshPreview(_options.BoreholeDataset);

                // ALWAYS open the popup, even if initialization failed
                // We'll show the error inside the popup
                ImGui.OpenPopup("Mesh Preview Window");
                Logger.Log("Called ImGui.OpenPopup - popup should open next frame");
            }
            else
            {
                Logger.Log("Closing mesh preview");
                _meshPreview?.Dispose();
                _meshPreview = null;
                _showMeshPreviewInPanel = false;
            }
        }

        // NEW: Add alternative rendering option
        if (_meshPreview != null)
        {
            ImGui.SameLine();
            if (ImGui.Checkbox("Show in Panel", ref _showMeshPreviewInPanel))
            {
                Logger.Log($"Show in Panel toggled: {_showMeshPreviewInPanel}");
                // Close modal if showing in panel
                if (_showMeshPreviewInPanel) _showMeshPreview = true; // Keep preview alive
            }

            ImGui.SetItemTooltip("Alternative: Show preview directly in this panel instead of popup");
        }

        ImGui.Separator();

        // Handle GraphicsDevice unavailable popup
        if (ImGui.BeginPopupModal("GraphicsDevice Unavailable", ref _showMeshPreview,
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Graphics device is not available for 3D mesh preview.");
            ImGui.Spacing();
            ImGui.TextWrapped(
                "The mesh preview requires a graphics device which is not currently initialized. This typically happens when:");
            ImGui.BulletText("The application is still starting up");
            ImGui.BulletText("You're running in a headless environment");
            ImGui.BulletText("Graphics initialization failed");
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f),
                "You can still run simulations - only the 3D preview is affected.");
            ImGui.Spacing();
            if (ImGui.Button("OK", new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
                _showMeshPreview = false;
            }

            ImGui.EndPopup();
        }

        // Heat Exchanger Configuration
        if (ImGui.CollapsingHeader("Heat Exchanger Configuration"))
        {
            var heatExType = _selectedHeatExchangerType;
            if (ImGui.Combo("Type", ref heatExType, "U-Tube\0Coaxial\0"))
            {
                _selectedHeatExchangerType = heatExType;
                _options.HeatExchangerType = (HeatExchangerType)_selectedHeatExchangerType;
                _selectedPreset = 0; // Switch to Custom
            }

            var flowConfig = _selectedFlowConfig;
            if (ImGui.Combo("Flow", ref flowConfig, "Standard Counter-Flow\0Reversed Counter-Flow\0"))
            {
                _selectedFlowConfig = flowConfig;
                _options.FlowConfiguration = (FlowConfiguration)_selectedFlowConfig;
                _selectedPreset = 0; // Switch to Custom
            }

            var pipeInnerDiam = (float)(_options.PipeInnerDiameter * 1000);
            if (ImGui.SliderFloat("Pipe Inner Diameter (mm)", ref pipeInnerDiam, 20, 200))
            {
                _options.PipeInnerDiameter = pipeInnerDiam / 1000;
                _selectedPreset = 0; // Switch to Custom
            }

            var pipeOuterDiam = (float)(_options.PipeOuterDiameter * 1000);
            if (ImGui.SliderFloat("Pipe Outer Diameter (mm)", ref pipeOuterDiam, 25, 220))
            {
                _options.PipeOuterDiameter = pipeOuterDiam / 1000;
                _selectedPreset = 0; // Switch to Custom
            }

            var pipeSpacing = (float)(_options.PipeSpacing * 1000);
            if (ImGui.SliderFloat("Pipe Spacing (mm)", ref pipeSpacing, 50, 300))
            {
                _options.PipeSpacing = pipeSpacing / 1000;
                _selectedPreset = 0; // Switch to Custom
            }

            var pipeConductivity = (float)_options.PipeThermalConductivity;
            if (ImGui.SliderFloat("Pipe Conductivity (W/m·K)", ref pipeConductivity, 0.1f, 50.0f))
            {
                _options.PipeThermalConductivity = pipeConductivity;
                _selectedPreset = 0; // Switch to Custom
            }

            if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
            {
                var innerPipeConductivity = (float)_options.InnerPipeThermalConductivity;
                if (ImGui.SliderFloat("Inner Pipe Conductivity (W/m·K)", ref innerPipeConductivity, 0.01f, 50.0f,
                        "%.3f", ImGuiSliderFlags.Logarithmic))
                {
                    _options.InnerPipeThermalConductivity = innerPipeConductivity;
                    _selectedPreset = 0; // Switch to Custom
                }

                ImGui.SetItemTooltip(
                    "Controls internal heat loss.\nLow (<0.1): Insulated (VIT)\nHigh (>15): Conductive (Steel)");
            }

            var groutConductivity = (float)_options.GroutThermalConductivity;
            if (ImGui.SliderFloat("Grout Conductivity (W/m·K)", ref groutConductivity, 1.0f, 5.0f))
            {
                _options.GroutThermalConductivity = groutConductivity;
                _selectedPreset = 0; // Switch to Custom
            }

            // --- MODIFICATION START ---
            ImGui.Separator();
            ImGui.Text("Operational Depth");
            var heDepth = _options.HeatExchangerDepth;
            if (ImGui.SliderFloat("Heat Exchanger Depth (m)", ref heDepth, 10.0f, _options.BoreholeDataset.TotalDepth,
                    "%.1f m"))
            {
                _options.HeatExchangerDepth = heDepth;
                _selectedPreset = 0; // Switch to Custom
            }

            ImGui.SetItemTooltip(
                $"Sets the active depth of the heat exchanger down to a maximum of the borehole's total depth ({_options.BoreholeDataset.TotalDepth:F1} m).");
            // --- MODIFICATION END ---
        }

        // Fluid Properties
        if (ImGui.CollapsingHeader("Fluid Properties"))
        {
            var massFlow = (float)_options.FluidMassFlowRate;
            if (ImGui.SliderFloat("Mass Flow Rate (kg/s)", ref massFlow, 0.1f, 50.0f))
            {
                _options.FluidMassFlowRate = massFlow;
                _selectedPreset = 0; // Switch to Custom
            }

            var inletTemp = (float)(_options.FluidInletTemperature - 273.15);
            if (ImGui.SliderFloat("Inlet Temperature (°C)", ref inletTemp, 0, 50))
            {
                _options.FluidInletTemperature = inletTemp + 273.15;
                _selectedPreset = 0; // Switch to Custom
            }

            var specificHeat = (float)_options.FluidSpecificHeat;
            if (ImGui.InputFloat("Specific Heat (J/kg·K)", ref specificHeat))
            {
                _options.FluidSpecificHeat = specificHeat;
                _selectedPreset = 0; // Switch to Custom
            }

            var density = (float)_options.FluidDensity;
            if (ImGui.InputFloat("Density (kg/m³)", ref density))
            {
                _options.FluidDensity = density;
                _selectedPreset = 0; // Switch to Custom
            }

            var viscosity = (float)(_options.FluidViscosity * 1000);
            if (ImGui.InputFloat("Viscosity (mPa·s)", ref viscosity))
            {
                _options.FluidViscosity = viscosity / 1000;
                _selectedPreset = 0; // Switch to Custom
            }

            var thermalCond = (float)_options.FluidThermalConductivity;
            if (ImGui.InputFloat("Thermal Conductivity (W/m·K)", ref thermalCond))
            {
                _options.FluidThermalConductivity = thermalCond;
                _selectedPreset = 0; // Switch to Custom
            }
        }

        // Ground Properties
        if (ImGui.CollapsingHeader("Ground Properties"))
        {
            var surfaceTemp = (float)(_options.SurfaceTemperature - 273.15);
            if (ImGui.SliderFloat("Surface Temperature (°C)", ref surfaceTemp, -10, 30))
                _options.SurfaceTemperature = surfaceTemp + 273.15;

            var geoGradient = (float)(_options.AverageGeothermalGradient * 1000);
            if (ImGui.SliderFloat("Geothermal Gradient (K/km)", ref geoGradient, 10, 50))
                _options.AverageGeothermalGradient = geoGradient / 1000;

            var geoFlux = (float)(_options.GeothermalHeatFlux * 1000);
            if (ImGui.SliderFloat("Geothermal Heat Flux (mW/m²)", ref geoFlux, 40, 100))
                _options.GeothermalHeatFlux = geoFlux / 1000;

            // Layer properties editor
            ImGui.Separator();
            ImGui.Text("Layer Properties:");

            if (ImGui.BeginTable("LayerProps", 6))
            {
                ImGui.TableSetupColumn("Layer");
                ImGui.TableSetupColumn("k (W/m·K)");
                ImGui.TableSetupColumn("Cp (J/kg·K)");
                ImGui.TableSetupColumn("ρ (kg/m³)");
                ImGui.TableSetupColumn("φ (-)");
                ImGui.TableSetupColumn("K (m²)");
                ImGui.TableHeadersRow();

                foreach (var layer in _options.LayerThermalConductivities.Keys.ToList())
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(layer);

                    ImGui.TableNextColumn();
                    var k = (float)_options.LayerThermalConductivities[layer];
                    if (ImGui.InputFloat($"##k_{layer}", ref k, 0, 0, "%.2f"))
                        _options.LayerThermalConductivities[layer] = k;

                    ImGui.TableNextColumn();
                    var cp = (float)_options.LayerSpecificHeats.GetValueOrDefault(layer, 900);
                    if (ImGui.InputFloat($"##cp_{layer}", ref cp, 0, 0, "%.0f"))
                        _options.LayerSpecificHeats[layer] = cp;

                    ImGui.TableNextColumn();
                    var rho = (float)_options.LayerDensities.GetValueOrDefault(layer, 2650);
                    if (ImGui.InputFloat($"##rho_{layer}", ref rho, 0, 0, "%.0f"))
                        _options.LayerDensities[layer] = rho;

                    ImGui.TableNextColumn();
                    var phi = (float)_options.LayerPorosities.GetValueOrDefault(layer, 0.1);
                    if (ImGui.InputFloat($"##phi_{layer}", ref phi, 0, 0, "%.3f"))
                        _options.LayerPorosities[layer] = phi;

                    ImGui.TableNextColumn();
                    var perm = (float)_options.LayerPermeabilities.GetValueOrDefault(layer, 1e-14);
                    var permLog = MathF.Log10(perm);
                    if (ImGui.InputFloat($"##perm_{layer}", ref permLog, 0, 0, "%.1f"))
                        _options.LayerPermeabilities[layer] = MathF.Pow(10, permLog);
                    ImGui.SetItemTooltip($"Permeability: {perm:E2} m²");
                }

                // Add new layer row
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.InputText("##newlayer", ref _newLayerName, 50);

                ImGui.TableNextColumn();
                ImGui.InputFloat("##newk", ref _newLayerConductivity, 0, 0, "%.2f");

                ImGui.TableNextColumn();
                ImGui.InputFloat("##newcp", ref _newLayerSpecificHeat, 0, 0, "%.0f");

                ImGui.TableNextColumn();
                ImGui.InputFloat("##newrho", ref _newLayerDensity, 0, 0, "%.0f");

                ImGui.TableNextColumn();
                ImGui.InputFloat("##newphi", ref _newLayerPorosity, 0, 0, "%.3f");

                ImGui.TableNextColumn();
                if (ImGui.Button("Add"))
                    if (!string.IsNullOrEmpty(_newLayerName))
                    {
                        _options.LayerThermalConductivities[_newLayerName] = _newLayerConductivity;
                        _options.LayerSpecificHeats[_newLayerName] = _newLayerSpecificHeat;
                        _options.LayerDensities[_newLayerName] = _newLayerDensity;
                        _options.LayerPorosities[_newLayerName] = _newLayerPorosity;
                        _options.LayerPermeabilities[_newLayerName] = _newLayerPermeability;
                        _newLayerName = "";
                    }

                ImGui.EndTable();
            }
        }

        // Groundwater Flow
        if (ImGui.CollapsingHeader("Groundwater Flow"))
        {
            var simulateGroundwaterFlow = _options.SimulateGroundwaterFlow;
            if (ImGui.Checkbox("Simulate Groundwater Flow", ref simulateGroundwaterFlow))
                _options.SimulateGroundwaterFlow = simulateGroundwaterFlow;

            if (_options.SimulateGroundwaterFlow)
            {
                var gwVel = _options.GroundwaterVelocity;
                var gwSpeed = gwVel.Length() * 86400; // m/day
                var gwDir = gwVel.Length() > 0 ? Vector3.Normalize(gwVel) : Vector3.UnitX;

                if (ImGui.SliderFloat("Flow Speed (m/day)", ref gwSpeed, 0, 10))
                    _options.GroundwaterVelocity = gwDir * gwSpeed / 86400;

                var azimuth = MathF.Atan2(gwDir.Y, gwDir.X) * 180 / MathF.PI;
                if (ImGui.SliderFloat("Flow Direction (°)", ref azimuth, -180, 180))
                {
                    var rad = azimuth * MathF.PI / 180;
                    gwDir = new Vector3(MathF.Cos(rad), MathF.Sin(rad), 0);
                    _options.GroundwaterVelocity = gwDir * gwSpeed / 86400;
                }

                var gwTemp = (float)(_options.GroundwaterTemperature - 273.15);
                if (ImGui.SliderFloat("Groundwater Temperature (°C)", ref gwTemp, 0, 30))
                    _options.GroundwaterTemperature = gwTemp + 273.15;

                var headTop = (float)_options.HydraulicHeadTop;
                if (ImGui.InputFloat("Hydraulic Head Top (m)", ref headTop))
                    _options.HydraulicHeadTop = headTop;

                var headBottom = (float)_options.HydraulicHeadBottom;
                if (ImGui.InputFloat("Hydraulic Head Bottom (m)", ref headBottom))
                    _options.HydraulicHeadBottom = headBottom;
            }
        }

        // Advanced Options
        ImGui.Checkbox("Show Advanced Options", ref _showAdvancedOptions);

        if (_showAdvancedOptions)
        {
            if (ImGui.CollapsingHeader("Simulation Domain"))
            {
                var domainRadius = (float)_options.DomainRadius;
                if (ImGui.SliderFloat("Domain Radius (m)", ref domainRadius, 20, 200))
                    _options.DomainRadius = domainRadius;

                // --- MODIFICATION START ---
                var domainExt = (float)_options.DomainExtension;
                if (ImGui.SliderFloat("Domain Extension (m)", ref domainExt, 0, 500))
                    _options.DomainExtension = domainExt;
                ImGui.SetItemTooltip(
                    "How far the simulation domain extends below the borehole bottom and above the surface.\nA value of at least 50m is recommended to observe effects below the borehole.");
                // --- MODIFICATION END ---

                var radialGridPoints = _options.RadialGridPoints;
                if (ImGui.SliderInt("Radial Grid Points", ref radialGridPoints, 20, 100))
                    _options.RadialGridPoints = radialGridPoints;

                var angularGridPoints = _options.AngularGridPoints;
                if (ImGui.SliderInt("Angular Grid Points", ref angularGridPoints, 12, 72))
                    _options.AngularGridPoints = angularGridPoints;

                var verticalGridPoints = _options.VerticalGridPoints;
                if (ImGui.SliderInt("Vertical Grid Points", ref verticalGridPoints, 50, 200))
                    _options.VerticalGridPoints = verticalGridPoints;
            }

            if (ImGui.CollapsingHeader("Solver Settings"))
            {
                var simTime = (float)(_options.SimulationTime / 86400);
                if (ImGui.InputFloat("Simulation Time (days)", ref simTime))
                    _options.SimulationTime = simTime * 86400;

                var timeStep = (float)(_options.TimeStep / 3600);
                if (ImGui.InputFloat("Time Step (hours)", ref timeStep))
                    _options.TimeStep = timeStep * 3600;

                var saveInterval = _options.SaveInterval;
                if (ImGui.InputInt("Save Interval", ref saveInterval))
                    _options.SaveInterval = saveInterval;

                var tolerance = (float)_options.ConvergenceTolerance;
                if (ImGui.InputFloat("Convergence Tolerance", ref tolerance, 0, 0, "%.0e"))
                    _options.ConvergenceTolerance = tolerance;

                var maxIterationsPerStep = _options.MaxIterationsPerStep;
                if (ImGui.InputInt("Max Iterations/Step", ref maxIterationsPerStep))
                    _options.MaxIterationsPerStep = maxIterationsPerStep;

                var useSIMD = _options.UseSIMD;
                if (ImGui.Checkbox("Use SIMD", ref useSIMD))
                    _options.UseSIMD = useSIMD;

                var useGPU = _options.UseGPU;
                if (ImGui.Checkbox("Use GPU", ref useGPU))
                    _options.UseGPU = useGPU;

                ImGui.Separator();
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f), "⚠️ Performance Warning:");
                ImGui.TextWrapped("Geomechanics is computationally expensive and may significantly increase simulation time.");
                ImGui.Spacing();

                var enableGeomechanics = _options.EnableGeomechanics;
                if (ImGui.Checkbox("Enable Geomechanics", ref enableGeomechanics))
                    _options.EnableGeomechanics = enableGeomechanics;

                ImGui.SetItemTooltip(
                    "Enable geomechanics simulation for stress, strain, and ground deformation analysis.\n" +
                    "Calculates thermal stress from temperature changes and pore pressure effects.\n" +
                    "Uses SIMD and OpenCL acceleration when available.\n" +
                    "WARNING: This is computationally expensive!");

                if (_options.EnableGeomechanics)
                {
                    ImGui.Indent();
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Rock Properties:");

                    var youngsModulus = _options.GeomechanicsYoungsModulus;
                    if (ImGui.InputFloat("Young's Modulus (GPa)", ref youngsModulus))
                        _options.GeomechanicsYoungsModulus = youngsModulus;

                    var poissonsRatio = _options.GeomechanicsPoissonsRatio;
                    if (ImGui.SliderFloat("Poisson's Ratio", ref poissonsRatio, 0.0f, 0.5f))
                        _options.GeomechanicsPoissonsRatio = poissonsRatio;

                    var thermalExpansion = _options.GeomechanicsThermalExpansion * 1e6f; // Display as ×10⁻⁶
                    if (ImGui.InputFloat("Thermal Expansion (×10⁻⁶ K⁻¹)", ref thermalExpansion))
                        _options.GeomechanicsThermalExpansion = thermalExpansion * 1e-6f;

                    var biotCoeff = _options.GeomechanicsBiotCoefficient;
                    if (ImGui.SliderFloat("Biot Coefficient", ref biotCoeff, 0.0f, 1.0f))
                        _options.GeomechanicsBiotCoefficient = biotCoeff;

                    ImGui.Unindent();
                }
            }

            // Thermodynamics and Geochemistry Section
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.7f, 0.4f, 0.9f, 1.0f));
            if (ImGui.CollapsingHeader("⚗️ Thermodynamics & Geochemistry"))
            {
                ImGui.PopStyleColor();

                var enableThermo = _options.EnableThermodynamics;
                if (ImGui.Checkbox("Enable Thermodynamics", ref enableThermo))
                    _options.EnableThermodynamics = enableThermo;

                ImGui.SetItemTooltip(
                    "Enable thermodynamic modeling for fluid-rock interactions, precipitation, and dissolution.\n" +
                    "This allows simulation of CCUS scenarios with carbonate mineralization.");

                if (_options.EnableThermodynamics)
                {
                    ImGui.Indent();

                    // Pore Network Model Settings
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Pore Network Model:");

                    var generatePNM = _options.GeneratePoreNetworkModel;
                    if (ImGui.Checkbox("Generate PNM from Lithology", ref generatePNM))
                        _options.GeneratePoreNetworkModel = generatePNM;

                    if (_options.GeneratePoreNetworkModel)
                    {
                        var pnmMode = (int)_options.PnmGenerationMode;
                        var modeNames = new[] { "Conservative (1 erosion)", "Aggressive (3 erosions)" };
                        if (ImGui.Combo("PNM Mode", ref pnmMode, modeNames, modeNames.Length))
                        {
                            _options.PnmGenerationMode = pnmMode == 0
                                ? PoreNetworkGenerationMode.Conservative
                                : PoreNetworkGenerationMode.Aggressive;
                            _options.PnmErosionPasses = pnmMode == 0 ? 1 : 3;
                        }
                    }

                    // Thermodynamic Time Step
                    ImGui.Spacing();
                    var thermoTimeStep = (float)(_options.ThermodynamicTimeStep / 3600);
                    if (ImGui.SliderFloat("Thermo Time Step (hours)", ref thermoTimeStep, 0.1f, 168f))
                        _options.ThermodynamicTimeStep = thermoTimeStep * 3600;
                    ImGui.SetItemTooltip("Time interval between thermodynamic calculations");

                    // Fluid Composition Editor
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Fluid Composition:");

                    if (ImGui.Button("+ Add Species"))
                    {
                        _options.FluidComposition.Add(new FluidCompositionEntry
                        {
                            SpeciesName = "CO2",
                            Concentration_mol_L = 0.01,
                            Units = "mol/L",
                            Notes = ""
                        });
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Load CCUS Preset"))
                    {
                        _options.FluidComposition.Clear();
                        _options.FluidComposition.Add(new FluidCompositionEntry
                        {
                            SpeciesName = "CO2",
                            Concentration_mol_L = 0.5,
                            Units = "mol/L",
                            Notes = "Dissolved CO2"
                        });
                        _options.FluidComposition.Add(new FluidCompositionEntry
                        {
                            SpeciesName = "Ca2+",
                            Concentration_mol_L = 0.01,
                            Units = "mol/L",
                            Notes = "Calcium ions"
                        });
                        _options.FluidComposition.Add(new FluidCompositionEntry
                        {
                            SpeciesName = "Na+",
                            Concentration_mol_L = 0.5,
                            Units = "mol/L",
                            Notes = "Sodium background"
                        });
                        _options.FluidComposition.Add(new FluidCompositionEntry
                        {
                            SpeciesName = "Cl-",
                            Concentration_mol_L = 0.52,
                            Units = "mol/L",
                            Notes = "Chloride"
                        });
                    }

                    // Display fluid composition table
                    if (_options.FluidComposition.Count > 0)
                    {
                        ImGui.Spacing();
                        if (ImGui.BeginTable("FluidCompositionTable", 4,
                                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                                new Vector2(0, 150)))
                        {
                            ImGui.TableSetupColumn("Species", ImGuiTableColumnFlags.WidthFixed, 100);
                            ImGui.TableSetupColumn("Concentration (mol/L)", ImGuiTableColumnFlags.WidthFixed, 150);
                            ImGui.TableSetupColumn("Notes", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 60);
                            ImGui.TableHeadersRow();

                            for (int i = 0; i < _options.FluidComposition.Count; i++)
                            {
                                var entry = _options.FluidComposition[i];
                                ImGui.TableNextRow();

                                ImGui.TableNextColumn();
                                var speciesName = entry.SpeciesName;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputText($"##species{i}", ref speciesName, 50))
                                    entry.SpeciesName = speciesName;

                                ImGui.TableNextColumn();
                                var conc = (float)entry.Concentration_mol_L;
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputFloat($"##conc{i}", ref conc, 0, 0, "%.4f"))
                                    entry.Concentration_mol_L = conc;

                                ImGui.TableNextColumn();
                                var notes = entry.Notes ?? "";
                                ImGui.SetNextItemWidth(-1);
                                if (ImGui.InputText($"##notes{i}", ref notes, 200))
                                    entry.Notes = notes;

                                ImGui.TableNextColumn();
                                if (ImGui.SmallButton($"X##{i}"))
                                {
                                    _options.FluidComposition.RemoveAt(i);
                                    break;
                                }
                            }

                            ImGui.EndTable();
                        }
                    }

                    // Visualization Options
                    ImGui.Spacing();
                    var enablePrecipViz = _options.EnablePrecipitationVisualization;
                    if (ImGui.Checkbox("Enable Precipitation Visualization", ref enablePrecipViz))
                        _options.EnablePrecipitationVisualization = enablePrecipViz;

                    ImGui.Unindent();
                }
            }
            else
            {
                ImGui.PopStyleColor();
            }

            // ORC Power Generation Section
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.7f, 0.3f, 1.0f));
            if (ImGui.CollapsingHeader("⚡ ORC Power Generation & Economics"))
            {
                ImGui.PopStyleColor();

                var enableORC = _options.EnableORCSimulation;
                if (ImGui.Checkbox("Enable ORC Simulation", ref enableORC))
                    _options.EnableORCSimulation = enableORC;

                ImGui.SetItemTooltip(
                    "Enable Organic Rankine Cycle power generation simulation.\n" +
                    "Converts geothermal heat to electrical power using working fluid (R245fa, isobutane, etc.).");

                if (_options.EnableORCSimulation)
                {
                    ImGui.Indent();

                    // GPU/SIMD acceleration options
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Acceleration:");

                    var useGPU = _options.UseORCGPU;
                    if (ImGui.Checkbox("Use GPU (OpenCL) Acceleration", ref useGPU))
                        _options.UseORCGPU = useGPU;
                    ImGui.SetItemTooltip("GPU acceleration using OpenCL 1.2 via Silk.NET for batch ORC calculations");

                    if (!useGPU)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "(Using CPU with SIMD)");
                    }

                    // ORC Cycle Parameters
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Cycle Parameters:");

                    var evapPressure = _options.ORCEvaporatorPressure / 1e5f; // Convert to bar
                    if (ImGui.SliderFloat("Evaporator Pressure (bar)", ref evapPressure, 5.0f, 30.0f))
                        _options.ORCEvaporatorPressure = evapPressure * 1e5f;
                    ImGui.SetItemTooltip("High-side pressure (turbine inlet)");

                    var condTempC = _options.ORCCondenserTemperature - 273.15f;
                    if (ImGui.SliderFloat("Condenser Temperature (°C)", ref condTempC, 20.0f, 50.0f))
                        _options.ORCCondenserTemperature = condTempC + 273.15f;
                    ImGui.SetItemTooltip("Low-side temperature (cooling water temperature)");

                    var pinch = _options.ORCMinPinchPoint;
                    if (ImGui.SliderFloat("Min Pinch Point (K)", ref pinch, 5.0f, 20.0f))
                        _options.ORCMinPinchPoint = pinch;
                    ImGui.SetItemTooltip("Minimum temperature difference in heat exchangers");

                    var superheat = _options.ORCSuperheat;
                    if (ImGui.SliderFloat("Superheat (K)", ref superheat, 0.0f, 15.0f))
                        _options.ORCSuperheat = superheat;
                    ImGui.SetItemTooltip("Turbine inlet superheat above saturation");

                    // Component Efficiencies
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Component Efficiencies:");

                    var turbEff = _options.ORCTurbineEfficiency * 100f;
                    if (ImGui.SliderFloat("Turbine Efficiency (%)", ref turbEff, 70.0f, 95.0f))
                        _options.ORCTurbineEfficiency = turbEff / 100f;

                    var pumpEff = _options.ORCPumpEfficiency * 100f;
                    if (ImGui.SliderFloat("Pump Efficiency (%)", ref pumpEff, 60.0f, 85.0f))
                        _options.ORCPumpEfficiency = pumpEff / 100f;

                    var genEff = _options.ORCGeneratorEfficiency * 100f;
                    if (ImGui.SliderFloat("Generator Efficiency (%)", ref genEff, 90.0f, 98.0f))
                        _options.ORCGeneratorEfficiency = genEff / 100f;

                    // Working Fluid Selection from ORCFluidLibrary
                    ImGui.Spacing();
                    var fluids = GeoscientistToolkit.Business.ORCFluidLibrary.Instance.GetAllFluids();
                    var fluidNames = fluids.Select(f => $"{f.Name} ({f.TemperatureRange_K_Min - 273.15f:F0}-{f.TemperatureRange_K_Max - 273.15f:F0}°C)").ToArray();
                    var currentFluidIdx = -1;
                    for (int i = 0; i < fluids.Count; i++)
                    {
                        if (fluids[i].Name == _options.ORCWorkingFluid)
                        {
                            currentFluidIdx = i;
                            break;
                        }
                    }
                    if (currentFluidIdx < 0) currentFluidIdx = 0; // Default to first fluid if not found
                    if (ImGui.Combo("Working Fluid", ref currentFluidIdx, fluidNames.ToArray(), fluidNames.Length))
                        _options.ORCWorkingFluid = fluids[currentFluidIdx].Name;
                    ImGui.SetItemTooltip("Select working fluid based on geothermal temperature range. Use Edit→ORC Fluid Library to add/edit fluids.");

                    // Mass Flow Limit
                    var maxFlow = _options.ORCMaxMassFlowRate;
                    if (ImGui.SliderFloat("Max Mass Flow (kg/s)", ref maxFlow, 10.0f, 200.0f))
                        _options.ORCMaxMassFlowRate = maxFlow;

                    ImGui.Unindent();
                }

                // Economic Analysis Section
                ImGui.Spacing();
                ImGui.Separator();

                var enableEcon = _options.EnableEconomicAnalysis;
                if (ImGui.Checkbox("Enable Economic Analysis", ref enableEcon))
                    _options.EnableEconomicAnalysis = enableEcon;

                ImGui.SetItemTooltip(
                    "Calculate NPV, IRR, LCOE, and payback period for the geothermal power project.\n" +
                    "Includes sensitivity analysis for key parameters.");

                if (_options.EnableEconomicAnalysis)
                {
                    ImGui.Indent();

                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Project Parameters:");

                    var projectLifetime = _options.EconomicProjectLifetime;
                    if (ImGui.SliderInt("Project Lifetime (years)", ref projectLifetime, 15, 50))
                        _options.EconomicProjectLifetime = projectLifetime;

                    var elecPrice = _options.ElectricityPrice;
                    if (ImGui.SliderFloat("Electricity Price (USD/MWh)", ref elecPrice, 40.0f, 200.0f))
                        _options.ElectricityPrice = elecPrice;

                    var discountRate = _options.DiscountRate * 100f;
                    if (ImGui.SliderFloat("Discount Rate (%)", ref discountRate, 3.0f, 15.0f))
                        _options.DiscountRate = discountRate / 100f;
                    ImGui.SetItemTooltip("WACC (Weighted Average Cost of Capital)");

                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Well Configuration:");

                    var prodWells = _options.NumberOfProductionWells;
                    if (ImGui.SliderInt("Production Wells", ref prodWells, 1, 5))
                        _options.NumberOfProductionWells = prodWells;

                    var injWells = _options.NumberOfInjectionWells;
                    if (ImGui.SliderInt("Injection Wells", ref injWells, 1, 5))
                        _options.NumberOfInjectionWells = injWells;

                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Cost Parameters:");

                    var drillingCost = _options.DrillingCostPerMeter;
                    if (ImGui.SliderFloat("Drilling Cost (USD/m)", ref drillingCost, 500.0f, 5000.0f))
                        _options.DrillingCostPerMeter = drillingCost;

                    var plantCost = _options.PowerPlantSpecificCost;
                    if (ImGui.SliderFloat("Plant Specific Cost (USD/kW)", ref plantCost, 1500.0f, 5000.0f))
                        _options.PowerPlantSpecificCost = plantCost;

                    var omPercent = _options.AnnualOMPercentage * 100f;
                    if (ImGui.SliderFloat("Annual O&M (%)", ref omPercent, 1.0f, 6.0f))
                        _options.AnnualOMPercentage = omPercent / 100f;
                    ImGui.SetItemTooltip("Operating & maintenance cost as % of capital cost");

                    var capFactor = _options.EconomicCapacityFactor * 100f;
                    if (ImGui.SliderFloat("Capacity Factor (%)", ref capFactor, 70.0f, 98.0f))
                        _options.EconomicCapacityFactor = capFactor / 100f;
                    ImGui.SetItemTooltip("Average annual plant availability and utilization");

                    ImGui.Unindent();
                }
            }
            else
            {
                ImGui.PopStyleColor();
            }
        }

        // ALTERNATIVE RENDERING: Show preview directly in panel if requested
        if (_showMeshPreviewInPanel && _meshPreview != null && _options.BoreholeDataset != null)
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "🔍 Mesh Preview (In-Panel Mode)");
            ImGui.Separator();

            // Show error if present
            if (!string.IsNullOrEmpty(_meshPreviewInitError))
            {
                ImGui.TextColored(new Vector4(1, 0.2f, 0.2f, 1), $"❌ Error: {_meshPreviewInitError}");
                if (ImGui.Button("Retry")) InitializeMeshPreview(_options.BoreholeDataset);
            }
            else
            {
                // Render in a scrollable child window
                var availSpace = ImGui.GetContentRegionAvail();
                if (ImGui.BeginChild("MeshPreviewInPanel", new Vector2(availSpace.X, Math.Min(600, availSpace.Y)),
                        ImGuiChildFlags.Border))
                    try
                    {
                        _meshPreview.Render(_options.BoreholeDataset, _options);
                    }
                    catch (Exception ex)
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error: {ex.Message}");
                    }

                ImGui.EndChild();
            }
        }
    }


    private void RenderSimulationProgress()
    {
        ImGui.Text("Simulation Running...");
        ImGui.ProgressBar(_simulationProgress, new Vector2(-1, 0), _simulationMessage);

        // Display convergence status with color coding
        if (_currentSolver != null)
        {
            ImGui.Separator();

            // Color-code status based on convergence
            var status = _currentSolver.ConvergenceStatus;
            var statusColor = new Vector4(0.7f, 0.7f, 0.7f, 1.0f); // Default gray

            if (status.Contains("converged"))
                statusColor = new Vector4(0.3f, 1.0f, 0.3f, 1.0f); // Green
            else if (status.Contains("DIVERGED") || status.Contains("NaN") || status.Contains("Inf"))
                statusColor = new Vector4(1.0f, 0.2f, 0.2f, 1.0f); // Red
            else if (status.Contains("max iterations"))
                statusColor = new Vector4(1.0f, 0.7f, 0.2f, 1.0f); // Orange

            ImGui.TextColored(statusColor, $"Status: {status}");
            ImGui.Text($"Time Step: {_currentSolver.CurrentTimeStep}");
            ImGui.Text($"Simulation Time: {_currentSolver.CurrentSimulationTime / 86400.0:F2} days");

            // Convergence history graph
            if (_currentSolver.ConvergenceHistory.Count > 0)
            {
                ImGui.Separator();
                ImGui.Text("Overall Convergence (per time step):");

                var values = _currentSolver.ConvergenceHistory.Select(v => (float)Math.Log10(Math.Max(v, 1e-10)))
                    .ToArray();
                if (values.Length > 0)
                {
                    var minVal = values.Min();
                    var maxVal = values.Max();
                    ImGui.PlotLines("##conv_hist", ref values[0], values.Length,
                        0, $"Log10(Error) [{minVal:F1} to {maxVal:F1}]",
                        minVal - 0.5f, maxVal + 0.5f, new Vector2(0, 100));

                    var lastError = _currentSolver.ConvergenceHistory.LastOrDefault();
                    var errorColor = lastError < 1e-6 ? new Vector4(0.3f, 1.0f, 0.3f, 1.0f) :
                        lastError < 1e-4 ? new Vector4(1.0f, 0.7f, 0.2f, 1.0f) :
                        new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
                    ImGui.TextColored(errorColor, $"Current Error: {lastError:E3}");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"(Target: {_options.ConvergenceTolerance:E3})");
                }
            }

            // Heat transfer convergence (last time step)
            if (_currentSolver.HeatConvergenceHistory.Count > 0)
            {
                ImGui.Separator();
                ImGui.Text("Heat Transfer Convergence (current step):");

                // Show last 100 iterations
                var heatVals = _currentSolver.HeatConvergenceHistory.TakeLast(100)
                    .Select(v => (float)Math.Log10(Math.Max(v, 1e-10))).ToArray();
                if (heatVals.Length > 0)
                {
                    var minVal = heatVals.Min();
                    var maxVal = heatVals.Max();
                    ImGui.PlotLines("##heat_conv", ref heatVals[0], heatVals.Length,
                        0, $"Log10(Heat Error) [{minVal:F1} to {maxVal:F1}]",
                        minVal - 0.5f, maxVal + 0.5f, new Vector2(0, 80));

                    var lastHeat = _currentSolver.HeatConvergenceHistory.LastOrDefault();
                    ImGui.Text($"Heat Iterations: {_currentSolver.HeatConvergenceHistory.Count}, Error: {lastHeat:E3}");
                }
            }

            // Flow convergence (last time step)
            if (_currentSolver.FlowConvergenceHistory.Count > 0 && _options.SimulateGroundwaterFlow)
            {
                ImGui.Separator();
                ImGui.Text("Groundwater Flow Convergence (current step):");

                // Show last 100 iterations
                var flowVals = _currentSolver.FlowConvergenceHistory.TakeLast(100)
                    .Select(v => (float)Math.Log10(Math.Max(v, 1e-10))).ToArray();
                if (flowVals.Length > 0)
                {
                    var minVal = flowVals.Min();
                    var maxVal = flowVals.Max();
                    ImGui.PlotLines("##flow_conv", ref flowVals[0], flowVals.Length,
                        0, $"Log10(Flow Error) [{minVal:F1} to {maxVal:F1}]",
                        minVal - 0.5f, maxVal + 0.5f, new Vector2(0, 80));

                    var lastFlow = _currentSolver.FlowConvergenceHistory.LastOrDefault();
                    ImGui.Text($"Flow Iterations: {_currentSolver.FlowConvergenceHistory.Count}, Error: {lastFlow:E3}");
                }
            }

            // Adaptive time step info
            if (_currentSolver.TimeStepHistory.Count > 0)
            {
                ImGui.Separator();
                var currentDt = _currentSolver.TimeStepHistory.LastOrDefault();
                var avgDt = _currentSolver.TimeStepHistory.Average();
                ImGui.Text($"Adaptive Time Step: {currentDt:F2} s (avg: {avgDt:F2} s)");
                ImGui.TextDisabled($"User specified: {_options.TimeStep:F0} s");
            }

            // Adaptive time step history
            if (_currentSolver.TimeStepHistory.Count > 0)
            {
                ImGui.Separator();
                ImGui.Text("Adaptive Time Step:");
                var dtVals = _currentSolver.TimeStepHistory.Select(v => (float)v).ToArray();
                if (dtVals.Length > 0)
                {
                    ImGui.PlotLines("##dt_hist", ref dtVals[0], dtVals.Length,
                        0, "Time Step (s)", 0, float.MaxValue, new Vector2(0, 60));
                    ImGui.Text($"Current dt: {dtVals.LastOrDefault():F3} s");
                }
            }
        }


        ImGui.Separator();
        if (ImGui.Button("Cancel")) _cancellationTokenSource?.Cancel();
    }

    private void RenderResultsWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(1200, 800), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new Vector2(150, 150), ImGuiCond.FirstUseEver);

        var isOpen = _showResults;

        if (!ImGui.Begin("Geothermal Simulation Results", ref isOpen, ImGuiWindowFlags.None))
        {
            ImGui.End();
            _showResults = isOpen;
            return;
        }

        // Header with controls
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "Simulation Results");
        ImGui.Separator();
        ImGui.Spacing();

        // Control buttons
        if (ImGui.Button("Export Results..."))
            _exportDialog.Open();

        ImGui.SameLine();
        if (ImGui.Button("3D Visualization"))
        {
            _show3DVisualization = !_show3DVisualization;
            if (_show3DVisualization)
            {
                InitializeVisualization();

                // Verify initialization succeeded
                if (_visualization3D == null)
                {
                    Logger.LogError("Failed to initialize 3D visualization. GraphicsDevice may be unavailable.");
                    _show3DVisualization = false;
                }
            }
            else
            {
                _visualization3D?.Dispose();
                _visualization3D = null;
            }
        }

        // Add status indicator
        if (_show3DVisualization)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), "● 3D View Active");
        }

        // SURGICAL FIX 2: Add button to initialize 2D visualization if not done yet
        if (_crossSectionViewer == null && _results != null && _mesh != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Initialize 2D Viewer"))
            {
                _crossSectionViewer = new BoreholeCrossSectionViewer();
                _crossSectionViewer.LoadResults(_results, _mesh, _options);
                Logger.Log("2D cross-section viewer initialized");
            }

            ImGui.SetItemTooltip("Initialize the 2D borehole cross-section viewer");
        }

        ImGui.Separator();

        // Show 3D visualization or tabs
        if (_show3DVisualization && _visualization3D != null)
        {
            Render3DVisualizationWithControls();
        }
        else
        {
            // Result tabs
            if (ImGui.BeginTabBar("ResultTabs"))
            {
                if (ImGui.BeginTabItem("Summary"))
                {
                    RenderSummaryTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Thermal Performance"))
                {
                    RenderThermalPerformanceTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Flow Analysis"))
                {
                    RenderFlowAnalysisTab();
                    ImGui.EndTabItem();
                }

                // FIX: Add the new tab for the cross-section
                if (ImGui.BeginTabItem("Borehole Cross-Section"))
                {
                    RenderCrossSectionTab();
                    ImGui.EndTabItem();
                }


                if (ImGui.BeginTabItem("Layer Analysis"))
                {
                    RenderLayerAnalysisTab();
                    ImGui.EndTabItem();
                }

                // Thermodynamics & Precipitation Tab
                if (_options.EnableThermodynamics && ImGui.BeginTabItem("⚗️ Geochemistry & Precipitation"))
                {
                    RenderThermodynamicsTab();
                    ImGui.EndTabItem();
                }

                // ORC Power Generation & Economics Tab
                if (_options.EnableORCSimulation && ImGui.BeginTabItem("⚡ ORC Power & Economics"))
                {
                    RenderORCTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        ImGui.End();
        _showResults = isOpen;

        // Cleanup when window is closed
        if (!_showResults)
        {
            _show3DVisualization = false;
            _visualization3D?.Dispose();
            _visualization3D = null;
        }
    }

    private void RenderResults()
    {
        // Legacy method - now just calls the window version
        RenderResultsWindow();
    }

    private void RenderSummaryTab()
    {
        ImGui.TextWrapped(_results.GenerateSummaryReport());
    }

    private void RenderThermalPerformanceTab()
    {
        // Heat extraction plot
        if (_results.HeatExtractionRate.Any())
        {
            var times = _results.HeatExtractionRate.Select(h => (float)(h.time / 86400)).ToArray();
            var rates = _results.HeatExtractionRate.Select(h => (float)h.heatRate).ToArray();

            ImGui.PlotLines("Heat Extraction Rate (W)", ref rates[0], rates.Length,
                0, "", rates.Min() * 0.9f, rates.Max() * 1.1f, new Vector2(0, 200));
        }

        // Outlet temperature plot
        if (_results.OutletTemperature.Any())
        {
            var times = _results.OutletTemperature.Select(t => (float)(t.time / 86400)).ToArray();
            var temps = _results.OutletTemperature.Select(t => (float)(t.temperature - 273.15)).ToArray();

            ImGui.PlotLines("Outlet Temperature (°C)", ref temps[0], temps.Length,
                0, "", temps.Min() * 0.9f, temps.Max() * 1.1f, new Vector2(0, 200));
        }

        // COP plot
        if (_results.CoefficientOfPerformance.Any())
        {
            var times = _results.CoefficientOfPerformance.Select(c => (float)(c.time / 86400)).ToArray();
            var cops = _results.CoefficientOfPerformance.Select(c => (float)c.cop).ToArray();

            ImGui.PlotLines("Coefficient of Performance", ref cops[0], cops.Length,
                0, "", 0, cops.Max() * 1.1f, new Vector2(0, 200));
        }

        ImGui.Separator();
        ImGui.Text($"Total Extracted Energy: {_results.TotalExtractedEnergy / 1e9:F2} GJ");
        ImGui.Text($"Average Heat Rate: {_results.AverageHeatExtractionRate:F0} W");
        ImGui.Text($"Borehole Thermal Resistance: {_results.BoreholeThermalResistance:F3} m·K/W");
        ImGui.Text($"Thermal Influence Radius: {_results.ThermalInfluenceRadius:F1} m");
    }

    private void RenderFlowAnalysisTab()
    {
        if (!_options.SimulateGroundwaterFlow)
        {
            ImGui.Text("Groundwater flow simulation was not enabled.");
            return;
        }

        ImGui.Text($"Average Péclet Number: {_results.AveragePecletNumber:F2}");
        ImGui.Text($"Longitudinal Dispersivity: {_results.LongitudinalDispersivity:F3} m");
        ImGui.Text($"Transverse Dispersivity: {_results.TransverseDispersivity:F3} m");
        ImGui.Text($"Pressure Drawdown: {_results.PressureDrawdown:F0} Pa");

        ImGui.Separator();

        // Flow rates by layer
        if (_results.LayerFlowRates.Any())
        {
            ImGui.Text("Layer Flow Rates:");
            if (ImGui.BeginTable("FlowRates", 2))
            {
                ImGui.TableSetupColumn("Layer");
                ImGui.TableSetupColumn("Flow Rate (m³/s)");
                ImGui.TableHeadersRow();

                foreach (var kvp in _results.LayerFlowRates.OrderByDescending(l => l.Value))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(kvp.Key);
                    ImGui.TableNextColumn();
                    ImGui.Text($"{kvp.Value:E3}");
                }

                ImGui.EndTable();
            }
        }
    }

    private void RenderLayerAnalysisTab()
    {
        // Heat flux contributions
        if (_results.LayerHeatFluxContributions.Any())
        {
            ImGui.Text("Heat Flux Contributions:");
            if (ImGui.BeginTable("HeatFlux", 3))
            {
                ImGui.TableSetupColumn("Layer");
                ImGui.TableSetupColumn("Contribution (%)");
                ImGui.TableSetupColumn("Temp Change (K)");
                ImGui.TableHeadersRow();

                foreach (var layer in _results.LayerHeatFluxContributions.Keys)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(layer);

                    ImGui.TableNextColumn();
                    var flux = _results.LayerHeatFluxContributions[layer];
                    ImGui.Text($"{flux:F1}%");

                    ImGui.TableNextColumn();
                    var tempChange = _results.LayerTemperatureChanges.GetValueOrDefault(layer, 0);
                    ImGui.Text($"{tempChange:F2}");
                }

                ImGui.EndTable();
            }
        }
    }

    private void RenderThermodynamicsTab()
    {
        ImGui.TextColored(new Vector4(0.7f, 0.4f, 0.9f, 1.0f), "Geochemistry & Precipitation Analysis");
        ImGui.Separator();
        ImGui.Spacing();

        // Pore Network Model Status
        if (_results.PoreNetworkModel != null)
        {
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "✓ Pore Network Model Generated");
            ImGui.Text($"  Pores: {_results.PoreNetworkModel.Pores.Count}");
            ImGui.Text($"  Throats: {_results.PoreNetworkModel.Throats.Count}");
        }
        else
        {
            ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.2f, 1.0f), "⚠ No pore network model available");
            ImGui.TextWrapped("Enable thermodynamics and generate PNM to see precipitation data.");
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Total Precipitated/Dissolved Minerals
        if (_results.TotalPrecipitation_mol.Any())
        {
            ImGui.TextColored(new Vector4(0.5f, 0.9f, 1.0f, 1.0f), "Precipitated Minerals:");
            if (ImGui.BeginTable("PrecipTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Mineral", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("Total Moles", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Mass (kg)", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var (mineral, moles) in _results.TotalPrecipitation_mol.OrderByDescending(x => x.Value))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(mineral);

                    ImGui.TableNextColumn();
                    ImGui.Text($"{moles:E3}");

                    ImGui.TableNextColumn();
                    // Estimate mass (assuming typical molar mass ~100 g/mol)
                    var mass_kg = moles * 0.1; // rough estimate
                    ImGui.Text($"{mass_kg:F4}");

                    // Visual bar for relative amount
                    var maxMoles = _results.TotalPrecipitation_mol.Values.Max();
                    var fraction = (float)(moles / maxMoles);
                    ImGui.SameLine();
                    ImGui.ProgressBar(fraction, new Vector2(100, 0), "");
                }

                ImGui.EndTable();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Permeability Evolution
        if (_results.PermeabilityEvolution.Any())
        {
            ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f), "Permeability Evolution:");

            var times = _results.PermeabilityEvolution.Select(p => (float)(p.time / 86400)).ToArray(); // days
            var permRatios = _results.PermeabilityEvolution.Select(p => (float)p.averagePermeability).ToArray();

            if (times.Length > 0)
            {
                var label = string.Format("Final: {0:F4}", permRatios[^1]);
                ImGui.PlotLines("Permeability Ratio", ref permRatios[0], permRatios.Length,
                    0, label, 0f, 1.2f, new Vector2(0, 150));

                var finalPerm = permRatios[^1];
                var reduction = (1.0f - finalPerm) * 100f;
                var color = reduction > 50 ? new Vector4(1.0f, 0.2f, 0.2f, 1.0f) :
                           reduction > 20 ? new Vector4(1.0f, 0.7f, 0.2f, 1.0f) :
                           new Vector4(0.3f, 1.0f, 0.3f, 1.0f);

                ImGui.TextColored(color, $"Permeability Reduction: {reduction:F1}%");
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // pH Evolution
        if (_results.PHEvolution.Any())
        {
            ImGui.TextColored(new Vector4(0.6f, 1.0f, 0.6f, 1.0f), "pH Evolution:");

            var times = _results.PHEvolution.Select(p => (float)(p.time / 86400)).ToArray();
            var pHValues = _results.PHEvolution.Select(p => (float)p.pH).ToArray();

            if (times.Length > 0)
            {
                var pHLabel = string.Format("Final pH: {0:F2}", pHValues[^1]);
                ImGui.PlotLines("pH", ref pHValues[0], pHValues.Length,
                    0, pHLabel, 0f, 14f, new Vector2(0, 100));
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Saturation Indices
        if (_results.SaturationIndexEvolution.Any())
        {
            ImGui.TextColored(new Vector4(0.9f, 0.7f, 1.0f, 1.0f), "Saturation Indices (Final):");

            if (ImGui.BeginTable("SITable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Mineral", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("SI", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var (mineral, siHistory) in _results.SaturationIndexEvolution)
                {
                    if (!siHistory.Any()) continue;

                    var finalSI = siHistory[^1].saturationIndex;

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(mineral);

                    ImGui.TableNextColumn();
                    ImGui.Text($"{finalSI:F2}");

                    ImGui.TableNextColumn();
                    if (finalSI > 0)
                    {
                        ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), "Supersaturated (Precipitating)");
                    }
                    else if (finalSI < 0)
                    {
                        ImGui.TextColored(new Vector4(0.5f, 0.7f, 1.0f, 1.0f), "Undersaturated (Dissolving)");
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f), "Equilibrium");
                    }
                }

                ImGui.EndTable();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // 2D Precipitation Visualization
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "2D Precipitation Maps:");
        ImGui.TextWrapped("Precipitation concentration fields show where minerals are forming in the borehole.");

        if (_results.PrecipitationFields.Any())
        {
            foreach (var (mineral, field) in _results.PrecipitationFields.Take(3)) // Show first 3 minerals
            {
                ImGui.Spacing();
                ImGui.Text($"{mineral}:");

                // Display simple statistics
                var nr = field.GetLength(0);
                var ntheta = field.GetLength(1);
                var nz = field.GetLength(2);

                float maxConc = 0f;
                float totalPrecip = 0f;
                for (int r = 0; r < nr; r++)
                    for (int theta = 0; theta < ntheta; theta++)
                        for (int z = 0; z < nz; z++)
                        {
                            var conc = field[r, theta, z];
                            if (conc > maxConc) maxConc = conc;
                            totalPrecip += conc;
                        }

                ImGui.BulletText($"Max concentration: {maxConc:E3} mol/m³");
                ImGui.BulletText($"Total precipitated: {totalPrecip:E3} mol");

                // TODO: Add actual 2D slice visualization here
                ImGui.TextDisabled("  [2D slice visualization will be rendered here]");
            }
        }
        else
        {
            ImGui.TextDisabled("No precipitation data available yet. Run simulation with thermodynamics enabled.");
        }
    }

    private void RenderORCTab()
    {
        ImGui.TextColored(new Vector4(0.2f, 0.7f, 0.3f, 1.0f), "ORC Power Generation & Economic Analysis");
        ImGui.Separator();
        ImGui.Spacing();

        // Check if ORC results are available
        if (_orcResults == null || _orcResults.Length == 0)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.2f, 1.0f), "⚠ No ORC simulation results available");
            ImGui.TextWrapped("ORC analysis is performed after geothermal simulation completes.");
            ImGui.TextWrapped("The simulation must complete successfully to generate ORC power data.");
            return;
        }

        // Initialize ORC visualization if needed
        if (_orcVisualization == null)
        {
            try
            {
                // Note: GraphicsDeviceManager not available in this context
                // ORC visualization will use fallback text-based display
                _orcVisualization = null;

                // TODO: Implement graphics device access when available
                // var graphicsDevice = GraphicsDeviceManager.Instance?.GraphicsDevice;
                // if (graphicsDevice != null)
                // {
                //     _orcVisualization = new ORCVisualization(graphicsDevice);
                //
                //     // Extract temperature range from results
                //     float[] temps = new float[_orcResults.Length];
                //     for (int i = 0; i < _orcResults.Length; i++)
                //     {
                //         temps[i] = _orcResults[i].GeothermalFluidInletTemp;
                //     }
                //
                //     _orcVisualization.UpdateResults(_orcResults, temps);
                //
                //     if (_economicResults != null)
                //     {
                //         _orcVisualization.UpdateEconomics(_economicResults);
                //     }
                // }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to initialize ORC visualization: {ex.Message}");
            }
        }

        // Render ORC visualization UI
        if (_orcVisualization != null)
        {
            _orcVisualization.RenderUI();
        }
        else
        {
            // Fallback: display simple text summary
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.5f, 1.0f), "✓ ORC Simulation Complete");
            ImGui.Spacing();

            // Find optimal operating point
            int maxPowerIdx = 0;
            float maxPower = 0;
            for (int i = 0; i < _orcResults.Length; i++)
            {
                if (_orcResults[i].NetPower > maxPower)
                {
                    maxPower = _orcResults[i].NetPower;
                    maxPowerIdx = i;
                }
            }

            var optimalResult = _orcResults[maxPowerIdx];

            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Optimal Operating Point:");
            ImGui.BulletText($"Geo Fluid Temp: {optimalResult.GeothermalFluidInletTemp - 273.15f:F1} °C");
            ImGui.BulletText($"Net Power Output: {optimalResult.NetPower / 1e6f:F2} MW");
            ImGui.BulletText($"Thermal Efficiency: {optimalResult.ThermalEfficiency * 100f:F2} %");
            ImGui.BulletText($"ORC Mass Flow: {optimalResult.MassFlowRate:F2} kg/s");
            ImGui.BulletText($"Turbine Work: {optimalResult.TurbineWork / 1e6f:F2} MW");
            ImGui.BulletText($"Pump Work: {optimalResult.PumpWork / 1e6f:F2} MW");
            ImGui.BulletText($"Heat Input: {optimalResult.HeatInput / 1e6f:F2} MW");

            // Economic summary
            if (_economicResults != null)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.2f, 1.0f, 1.0f, 1.0f), "Economic Summary:");
                ImGui.BulletText($"Total CAPEX: ${_economicResults.TotalCapitalCostMUSD:F2} M");
                ImGui.BulletText($"NPV (30 years): ${_economicResults.NPV_MUSD:F2} M");
                ImGui.BulletText($"IRR: {_economicResults.IRR:F2} %");
                ImGui.BulletText($"Payback Period: {_economicResults.PaybackPeriodYears:F1} years");
                ImGui.BulletText($"LCOE: ${_economicResults.LCOE_USDperMWh:F2} /MWh");
                ImGui.BulletText($"Annual Energy: {_economicResults.AnnualEnergyProductionMWh:F0} MWh");

                ImGui.Spacing();
                if (_economicResults.NPV_MUSD > 0 && _economicResults.IRR > 8.0f)
                {
                    ImGui.TextColored(new Vector4(0.2f, 1f, 0.2f, 1), "✓ Project appears economically viable");
                }
                else if (_economicResults.NPV_MUSD > 0)
                {
                    ImGui.TextColored(new Vector4(1f, 1f, 0.2f, 1), "⚠ Marginal project viability");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1f, 0.2f, 0.2f, 1), "✗ Project not economically viable");
                }
            }
        }
    }

    private void RenderVisualizationTab()
    {
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "3D Visualization Options");
        ImGui.Separator();

        // Show generation status
        if (!_results.TemperatureIsosurfaces.Any() && !_results.TemperatureSlices.Any())
        {
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), "⚠ No visualization data generated yet.");
            ImGui.TextWrapped(
                "Isosurfaces and slices are generated during simulation. You can also manually generate them below:");
            ImGui.Spacing();
        }

        // Always show controls to generate new visualizations
        ImGui.Text("Generate New Visualizations:");
        ImGui.InputFloat("Isosurface Temperature (°C)", ref _newIsosurfaceTemp);
        if (ImGui.Button("Generate Isosurface")) GenerateNewIsosurface(_newIsosurfaceTemp + 273.15f);
        ImGui.SetItemTooltip("Create a 3D surface where temperature equals this value");

        ImGui.Separator();

        // Temperature isosurfaces
        if (_results.TemperatureIsosurfaces.Any())
        {
            ImGui.Text($"Temperature Isosurfaces ({_results.TemperatureIsosurfaces.Count} available):");
            var isoNames = _results.TemperatureIsosurfaces
                .Select(iso => $"{Path.GetFileNameWithoutExtension(iso.Name)}").ToArray();
            ImGui.ListBox("##Isosurfaces", ref _selectedIsosurface, isoNames, Math.Min(5, isoNames.Length));

            if (ImGui.Button("View Selected Isosurface")) ViewSingleIsosurface();
            ImGui.SetItemTooltip("Show only the selected isosurface");

            ImGui.SameLine();
            if (ImGui.Button("View All Isosurfaces")) ViewAllIsosurfaces();
            ImGui.SetItemTooltip("Show all isosurfaces together");

            ImGui.Separator();
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No isosurfaces available. Generate one above.");
            ImGui.Separator();
        }

        // Temperature slices
        if (_results.TemperatureSlices.Any())
        {
            ImGui.Text($"2D Temperature Slices ({_results.TemperatureSlices.Count} available):");

            foreach (var slice in _results.TemperatureSlices)
                if (ImGui.Button($"View Slice at {slice.Key:F1} m depth"))
                    if (_visualization3D != null)
                    {
                        var normalizedDepth = slice.Key / _options.BoreholeDataset.TotalDepth;
                        _visualization3D.SetRenderMode(GeothermalVisualization3D.RenderMode.Slices);
                        _visualization3D.SetSliceDepth((float)normalizedDepth);
                    }

            ImGui.Separator();
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No 2D slices available.");
            ImGui.Text("Slices are generated during simulation if 'Generate2DSlices' is enabled.");
            ImGui.Separator();
        }

        // Streamlines
        if (_options.SimulateGroundwaterFlow)
        {
            if (ImGui.Button("Show Streamlines")) ShowStreamlines();
            ImGui.SetItemTooltip("Visualize groundwater flow paths");

            ImGui.SameLine();
            if (ImGui.Button("Show Velocity Field")) ShowVelocityField();
            ImGui.SetItemTooltip("Display velocity magnitude on the domain surface");

            ImGui.Text("Flux Visualization:");
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f),
                "• Use 'Velocity' render mode to see flux magnitude with directional patterns");
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f),
                "• Streamlines show flow paths colored by velocity");
        }

        if (ImGui.Button("Clear Visualizations")) _visualization3D?.ClearDynamicMeshes();
        ImGui.SetItemTooltip("Remove all dynamically generated visualization objects");
    }

    private void InitializeMeshPreview(BoreholeDataset borehole)
    {
        Logger.Log("=== InitializeMeshPreview START ===");
        Logger.Log($"VeldridManager.GraphicsDevice: {(VeldridManager.GraphicsDevice != null ? "OK" : "NULL")}");
        Logger.Log($"BoreholeDataset: {(borehole != null ? "OK" : "NULL")}");

        // Check if GraphicsDevice is available in VeldridManager
        if (VeldridManager.GraphicsDevice == null)
        {
            Logger.LogWarning("VeldridManager.GraphicsDevice not available. Mesh preview cannot be generated.");
            // DON'T reset the flag - let the window show the error
            _meshPreviewInitError = "Graphics device not available. Check if GPU is initialized.";
            return;
        }

        try
        {
            Logger.Log("Creating GeothermalMeshPreview...");

            // Create mesh preview if it doesn't exist
            if (_meshPreview == null)
            {
                _meshPreview = new GeothermalMeshPreview();
                Logger.Log("New GeothermalMeshPreview created");
            }
            else
            {
                // If mesh preview exists, dispose and recreate
                Logger.Log("Disposing existing mesh preview");
                _meshPreview.Dispose();
                _meshPreview = new GeothermalMeshPreview();
                Logger.Log("Mesh preview recreated");
            }

            // Generate preview using VeldridManager graphics device
            Logger.Log("Calling GeneratePreview...");
            _meshPreview.GeneratePreview(borehole, _options);
            Logger.Log("GeneratePreview completed successfully");
            _meshPreviewInitError = null; // Clear any previous errors
            Logger.Log("=== InitializeMeshPreview END (SUCCESS) ===");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to initialize mesh preview: {ex.Message}");
            Logger.LogError($"Stack trace: {ex.StackTrace}");

            // Store error but DON'T close the window - show error in window
            _meshPreviewInitError = $"Initialization failed: {ex.Message}";

            // Keep partial state if needed
            // _meshPreview?.Dispose();
            // _meshPreview = null;

            // DON'T DO THIS - it causes the flicker!
            // _showMeshPreview = false;

            Logger.Log("=== InitializeMeshPreview END (FAILED) ===");
        }
    }

    private void RenderMeshPreviewModal()
    {
        // CRITICAL: Only clean up AFTER the popup is confirmed closed
        // Check if popup is actually open using ImGui's internal state
        var popupIsOpen = ImGui.IsPopupOpen("Mesh Preview Window");

        // If _showMeshPreview is true, ensure popup is open
        if (_showMeshPreview && !popupIsOpen) ImGui.SetNextWindowSize(new Vector2(1400, 900), ImGuiCond.FirstUseEver);
        // Popup not yet opened, will open next frame
        // Use a dummy bool for the modal - we control closing manually via our Close button
        var dummyOpen = true;

        // Only render if popup is actually open
        // CRITICAL FIX: Add NoNav to prevent navigation to windows behind this modal
        if (ImGui.BeginPopupModal("Mesh Preview Window", ref dummyOpen, ImGuiWindowFlags.NoNav))
        {
            // Debug: Log first time only
            if (!_meshPreviewWindowLoggedOnce)
            {
                Logger.Log("=== Mesh Preview Modal OPENED AND RENDERING ===");
                Logger.Log(
                    $"State check: _meshPreview={(_meshPreview != null ? "EXISTS" : "NULL")}, _options.BoreholeDataset={(_options.BoreholeDataset != null ? "EXISTS" : "NULL")}, _meshPreviewInitError={(string.IsNullOrEmpty(_meshPreviewInitError) ? "NONE" : _meshPreviewInitError)}");
                _meshPreviewWindowLoggedOnce = true;
            }

            try
            {
                // Header with info
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f),
                    "🔍 Mesh Configuration Preview - Pre-Simulation");
                ImGui.Separator();
                ImGui.Spacing();

                // DEBUG: Show actual state
                ImGui.Text(
                    $"Debug State: _meshPreview={(_meshPreview != null ? "EXISTS" : "NULL")}, Dataset={(_options.BoreholeDataset != null ? "EXISTS" : "NULL")}");
                if (!string.IsNullOrEmpty(_meshPreviewInitError)) ImGui.Text($"Error: {_meshPreviewInitError}");
                ImGui.Separator();

                // Show initialization error if present
                if (!string.IsNullOrEmpty(_meshPreviewInitError))
                {
                    ImGui.TextColored(new Vector4(1, 0.2f, 0.2f, 1), "❌ Initialization Error:");
                    ImGui.Spacing();
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.3f, 0.1f, 0.1f, 0.5f));
                    if (ImGui.BeginChild("ErrorBox", new Vector2(0, 150), ImGuiChildFlags.Border))
                    {
                        ImGui.TextWrapped(_meshPreviewInitError);
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();
                        ImGui.TextWrapped("This usually means:");
                        ImGui.BulletText("Graphics device initialization failed");
                        ImGui.BulletText("Resource creation error (textures, buffers)");
                        ImGui.BulletText("GPU driver issue");
                        ImGui.Spacing();
                        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f),
                            "Check the console log for detailed error messages.");
                    }

                    ImGui.EndChild();
                    ImGui.PopStyleColor();
                    ImGui.Spacing();
                }
                // Check if mesh preview exists and initialized successfully
                else if (_meshPreview != null && _options.BoreholeDataset != null)
                {
                    ImGui.Text("Calling _meshPreview.Render()...");
                    _meshPreview.Render(_options.BoreholeDataset, _options);
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "⚠️ Mesh preview not ready");
                    if (_meshPreview == null)
                    {
                        ImGui.Text("  • Preview object not initialized (_meshPreview is NULL)");
                        ImGui.Text("     This means InitializeMeshPreview either:");
                        ImGui.Text("     1. Never ran");
                        ImGui.Text("     2. Threw an exception silently");
                        ImGui.Text("     3. Set _meshPreview but it was cleared");
                    }

                    if (_options.BoreholeDataset == null)
                        ImGui.Text("  • Borehole dataset not available");

                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                        "No errors in console? Check if initialization completed.");
                    ImGui.Text("Click 'Force Reinitialize' to try again with verbose logging.");
                }

                ImGui.Spacing();
                ImGui.Separator();

                // Always show retry button
                if (ImGui.Button("Force Reinitialize", new Vector2(200, 0)))
                {
                    Logger.Log("========================================");
                    Logger.Log("USER CLICKED FORCE REINITIALIZE");
                    Logger.Log(
                        $"Current state: _meshPreview={_meshPreview != null}, _meshPreviewInitError={_meshPreviewInitError}");
                    Logger.Log("========================================");
                    InitializeMeshPreview(_options.BoreholeDataset);
                    Logger.Log(
                        $"After init: _meshPreview={_meshPreview != null}, _meshPreviewInitError={_meshPreviewInitError}");
                }

                ImGui.SameLine();

                // MANUAL close button - this is the ONLY way to close the popup
                if (ImGui.Button("Close", new Vector2(120, 0)))
                {
                    Logger.Log("User clicked Close button - closing modal");
                    _showMeshPreview = false;
                    ImGui.CloseCurrentPopup();
                }
            }
            catch (Exception ex)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), $"❌ Render Error: {ex.Message}");
                Logger.LogError($"Mesh preview render error: {ex.Message}\n{ex.StackTrace}");
            }

            ImGui.EndPopup();
        }

        // CRITICAL FIX: Only cleanup when popup is confirmed closed AND flag is false
        // This prevents premature cleanup before the popup has had a chance to open
        if (!_showMeshPreview && !popupIsOpen && _meshPreview != null)
        {
            Logger.Log("Cleaning up mesh preview resources (popup confirmed closed)");
            _meshPreview?.Dispose();
            _meshPreview = null;
            _meshPreviewInitError = null;
            _meshPreviewWindowLoggedOnce = false; // Reset for next time
        }
    }

    // Keep old method for compatibility but redirect to modal
    private void RenderMeshPreviewWindow()
    {
        RenderMeshPreviewModal();
    }

    private void InitializeVisualization()
    {
        var graphicsDevice = VeldridManager.GraphicsDevice;
        if (_visualization3D == null && graphicsDevice != null)
        {
            _visualization3D = new GeothermalVisualization3D(graphicsDevice);

            if (_results != null && _mesh != null)
            {
                _visualization3D.LoadResults(_results, _mesh, _options);

                // SURGICAL FIX 1: Initialize 2D cross-section viewer
                if (_crossSectionViewer == null)
                    _crossSectionViewer = new BoreholeCrossSectionViewer();

                _crossSectionViewer.LoadResults(_results, _mesh, _options);
            }
        }
    }

    private void Render3DVisualizationWithControls()
    {
        if (_visualization3D == null) return;

        var availableSize = ImGui.GetContentRegionAvail();
        var controlPanelWidth = Math.Max(300, availableSize.X * 0.25f);
        var viewWidth = availableSize.X - controlPanelWidth - ImGui.GetStyle().ItemSpacing.X;

        // Controls Panel on the left
        ImGui.BeginChild("3DControls", new Vector2(controlPanelWidth, availableSize.Y), ImGuiChildFlags.Border);
        {
            ImGui.TextColored(new Vector4(0.3f, 0.7f, 1.0f, 1.0f), "3D Visualization Controls");
            ImGui.Separator();

            // Instructions
            ImGui.TextWrapped("Mouse Controls:");
            ImGui.BulletText("Left drag: Rotate view");
            ImGui.BulletText("Right drag: Pan camera");
            ImGui.BulletText("Scroll: Zoom in/out");
            ImGui.Separator();

            _visualization3D.RenderControls();

            ImGui.Separator();

            // Move the tab bar into the control panel
            if (ImGui.BeginTabBar("ResultTabs"))
            {
                if (ImGui.BeginTabItem("Summary"))
                {
                    RenderSummaryTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Visualization"))
                {
                    RenderVisualizationTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // 3D View Panel on the right
        // CRITICAL: NoMove prevents window dragging, NoScrollWithMouse allows our custom mouse handling
        ImGui.BeginChild("3DView", new Vector2(viewWidth, availableSize.Y), ImGuiChildFlags.Border,
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse);
        {
            var viewportSize = ImGui.GetContentRegionAvail();
            _visualization3D.Resize((uint)viewportSize.X, (uint)viewportSize.Y);
            _visualization3D.Render();

            var textureId = _visualization3D.GetRenderTargetImGuiBinding();
            ImGui.Image(textureId, viewportSize);

            if (ImGui.IsItemHovered())
            {
                var io = ImGui.GetIO();
                var mousePos = ImGui.GetMousePos() - ImGui.GetItemRectMin();
                var leftButton = ImGui.IsMouseDown(ImGuiMouseButton.Left);
                var rightButton = ImGui.IsMouseDown(ImGuiMouseButton.Right);

                // CRITICAL FIX: Force window focus and capture all mouse input
                if (leftButton || rightButton || Math.Abs(io.MouseWheel) > 0.01f)
                {
                    ImGui.SetWindowFocus();
                    io.WantCaptureMouse = true;
                }

                _visualization3D.HandleMouseInput(mousePos, leftButton, rightButton);

                if (Math.Abs(io.MouseWheel) > 0.01f)
                    _visualization3D.HandleMouseWheel(io.MouseWheel);
            }
        }
        ImGui.EndChild();
    }


    private void StartSimulation()
    {
        // Validate that we have valid borehole data
        if (_options.BoreholeDataset == null || _options.BoreholeDataset.TotalDepth <= 0)
        {
            Logger.LogError("Cannot start simulation: Invalid or empty borehole dataset.");
            return;
        }

        Logger.Log("Starting geothermal simulation...");
        _isSimulationRunning = true;
        _simulationProgress = 0f;
        _simulationMessage = "Initializing...";
        _showResults = false;

        _cancellationTokenSource = new CancellationTokenSource();

        // Run simulation asynchronously to prevent UI hang
        Task.Run(async () =>
        {
            try
            {
                // Create mesh
                _simulationMessage = "Generating mesh...";
                Logger.Log("Generating computational mesh...");
                _mesh = GeothermalMeshGenerator.GenerateCylindricalMesh(_options.BoreholeDataset, _options);

                // Validate mesh
                if (_mesh == null || _mesh.RadialPoints == 0 || _mesh.AngularPoints == 0 || _mesh.VerticalPoints == 0)
                    throw new InvalidOperationException("Failed to generate valid mesh. Check borehole data.");

                Logger.Log(
                    $"Mesh generation complete: {_mesh.RadialPoints}x{_mesh.AngularPoints}x{_mesh.VerticalPoints} cells.");

                // Create solver
                var progress = new Progress<(float progress, string message)>(update =>
                {
                    _simulationProgress = update.progress;
                    _simulationMessage = update.message;
                });

                Logger.Log("Initializing simulation solver...");
                var solver = new GeothermalSimulationSolver(_options, _mesh, progress, _cancellationTokenSource.Token);
                _currentSolver = solver; // Track solver for convergence visualization

                // Run simulation
                Logger.Log("Executing simulation time stepping...");
                _results = await solver.RunSimulationAsync();
                if (_crossSectionViewer != null)
                {
                    _crossSectionViewer.LoadResults(_results, _mesh, _options);
                    Logger.Log("BoreholeCrossSectionViewer updated with new simulation results.");
                }

                // Run ORC power generation analysis if enabled
                if (_options.EnableORCSimulation)
                {
                    Logger.Log("Running ORC power generation analysis...");
                    _simulationMessage = "Analyzing ORC power generation...";

                    try
                    {
                        // Extract fluid outlet temperatures from simulation results
                        if (_results.OutletTemperature != null && _results.OutletTemperature.Any())
                        {
                            // Get outlet temperature range (vary by ±20K to analyze performance curve)
                            var lastOutletTemp = _results.OutletTemperature.Last();
                            float avgOutletTemp = (float)lastOutletTemp.temperature;
                            int numPoints = 50;
                            float[] tempRange = new float[numPoints];
                            for (int i = 0; i < numPoints; i++)
                            {
                                tempRange[i] = avgOutletTemp - 10f + (20f * i / (numPoints - 1));
                            }

                            // Create ORC configuration from options
                            var orcConfig = new ORCConfiguration
                            {
                                EvaporatorPressure = _options.ORCEvaporatorPressure,
                                CondenserTemperature = _options.ORCCondenserTemperature,
                                TurbineEfficiency = _options.ORCTurbineEfficiency,
                                PumpEfficiency = _options.ORCPumpEfficiency,
                                GeneratorEfficiency = _options.ORCGeneratorEfficiency,
                                MinPinchPointTemperature = _options.ORCMinPinchPoint,
                                SuperheatDegrees = _options.ORCSuperheat,
                                MaxORCMassFlowRate = _options.ORCMaxMassFlowRate,
                                FluidName = _options.ORCWorkingFluid
                            };

                            // Run ORC simulation (CPU with SIMD or GPU with OpenCL)
                            bool useGPU = _options.UseORCGPU;
                            bool gpuInitialized = false;

                            if (useGPU)
                            {
                                Logger.Log("Using OpenCL GPU acceleration for ORC simulation...");
                                using (var orcSolver = new ORCOpenCLSolver(orcConfig))
                                {
                                    if (orcSolver.Initialize())
                                    {
                                        _orcResults = orcSolver.SimulateBatch(tempRange, (float)_options.FluidMassFlowRate);
                                        Logger.Log("ORC GPU simulation completed successfully.");
                                        gpuInitialized = true;
                                    }
                                    else
                                    {
                                        Logger.LogWarning("GPU initialization failed, falling back to CPU...");
                                        gpuInitialized = false;
                                    }
                                }
                            }

                            if (!useGPU || !gpuInitialized)
                            {
                                Logger.Log("Using CPU with SIMD for ORC simulation...");
                                var orcSim = new ORCSimulation
                                {
                                    Config = orcConfig,
                                    UseSIMD = _options.UseSIMD
                                };
                                _orcResults = orcSim.SimulateCycleBatch(tempRange, (float)_options.FluidMassFlowRate);
                                Logger.Log("ORC CPU simulation completed successfully.");
                            }

                            // Run economic analysis if enabled
                            if (_options.EnableEconomicAnalysis && _orcResults != null && _orcResults.Length > 0)
                            {
                                Logger.Log("Running economic analysis...");
                                _simulationMessage = "Calculating project economics...";

                                var economics = new GeothermalEconomics
                                {
                                    Parameters = new EconomicParameters
                                    {
                                        NumberOfWells = _options.NumberOfProductionWells + _options.NumberOfInjectionWells,
                                        WellDepthMeters = _options.BoreholeDataset?.TotalDepth ?? 2000f,
                                        CapacityFactor = _options.EconomicCapacityFactor,
                                        DrillingCostPerMeter = _options.DrillingCostPerMeter,
                                        PowerPlantSpecificCost = _options.PowerPlantSpecificCost,
                                        ElectricityPrice = _options.ElectricityPrice,
                                        DiscountRate = _options.DiscountRate,
                                        FixedOMCostPerMW = _options.PowerPlantSpecificCost * 1000f * _options.AnnualOMPercentage
                                    }
                                };

                                _economicResults = economics.CalculateEconomics(_orcResults, _options.EconomicProjectLifetime);
                                Logger.Log($"Economic analysis complete - NPV: ${_economicResults.NPV_MUSD:F2}M, IRR: {_economicResults.IRR:F2}%");
                            }
                        }
                        else
                        {
                            Logger.LogWarning("No fluid temperature data available for ORC analysis.");
                        }
                    }
                    catch (Exception orcEx)
                    {
                        Logger.LogError($"ORC simulation failed: {orcEx.Message}");
                        Logger.LogError($"Stack trace: {orcEx.StackTrace}");
                    }
                }

                _isSimulationRunning = false;
                _showResults = true;
                _currentSolver = null; // Clear solver reference
                Logger.Log("Geothermal simulation completed successfully.");
            }
            catch (OperationCanceledException)
            {
                _simulationMessage = "Simulation cancelled";
                Logger.LogWarning("Geothermal simulation was cancelled by the user.");
                _isSimulationRunning = false;
            }
            catch (Exception ex)
            {
                _simulationMessage = $"Error: {ex.Message}";
                Logger.LogError($"An error occurred during the geothermal simulation: {ex.Message}");
                Logger.LogError($"Stack Trace: {ex.StackTrace}");
                _isSimulationRunning = false;
            }
        });
    }

    private void ExportResults(string basePath)
    {
        if (_results == null)
        {
            Logger.LogWarning("Attempted to export results, but no results are available.");
            return;
        }

        try
        {
            _results.ExportToCSV(basePath);
            Logger.Log($"Results exported to {basePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export results: {ex.Message}");
        }
    }

    private void ViewSingleIsosurface()
    {
        if (_visualization3D == null) return;
        _visualization3D.ClearDynamicMeshes();
        if (_selectedIsosurface < _results.TemperatureIsosurfaces.Count)
        {
            _visualization3D.AddMesh(_results.TemperatureIsosurfaces[_selectedIsosurface]);
            _visualization3D.SetRenderMode(GeothermalVisualization3D.RenderMode.Isosurface);
        }
    }

    private void ViewAllIsosurfaces()
    {
        if (_visualization3D == null) return;
        _visualization3D.ClearDynamicMeshes();
        _visualization3D.AddMeshes(_results.TemperatureIsosurfaces);
        _visualization3D.SetRenderMode(GeothermalVisualization3D.RenderMode.Isosurface);
    }

    private void GenerateNewIsosurface(float temperature)
    {
        if (_visualization3D == null || _results.FinalTemperatureField == null) return;

        Logger.Log($"Generating isosurface at {temperature - 273.15:F1}°C");

        Task.Run(async () =>
        {
            try
            {
                var labelData = new SimpleLabelVolume(_mesh.RadialPoints, _mesh.AngularPoints, _mesh.VerticalPoints);
                for (var i = 0; i < _mesh.RadialPoints; i++)
                for (var j = 0; j < _mesh.AngularPoints; j++)
                for (var k = 0; k < _mesh.VerticalPoints; k++)
                    labelData.Data[i, j, k] = _mesh.MaterialIds[i, j, k] == 255 ? (byte)0 : (byte)1;

                var isosurface = await IsosurfaceGenerator.GenerateIsosurfaceAsync(
                    _results.FinalTemperatureField,
                    labelData,
                    temperature,
                    new Vector3(1, 1, 1),
                    null,
                    CancellationToken.None
                );
                isosurface.Name = $"Isosurface_{temperature - 273.15:F1}C";

                // This needs to be marshalled back to the UI thread if you have strict threading.
                // For now, we assume the renderer can handle this.
                _results.TemperatureIsosurfaces.Add(isosurface);
                _visualization3D.AddMesh(isosurface);
                _visualization3D.SetRenderMode(GeothermalVisualization3D.RenderMode.Isosurface);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to generate isosurface: {ex.Message}");
            }
        });
    }

    /// <summary>
    ///     Renders the cross-section tab, now using the custom ImGui renderer.
    /// </summary>
    private void RenderCrossSectionTab()
    {
        if (_crossSectionViewer == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1),
                "2D Cross-section viewer not initialized. Run a simulation first.");
            return;
        }

        // Left side panel for controls
        ImGui.BeginChild("CrossSectionControls", new Vector2(300, 0), ImGuiChildFlags.Border);
        _crossSectionViewer.RenderControls();
        ImGui.EndChild();

        ImGui.SameLine();

        // Right side for the plot itself
        ImGui.BeginChild("CrossSectionPlot", Vector2.Zero, ImGuiChildFlags.Border);
        _crossSectionViewer.RenderPlotInImGui();
        ImGui.EndChild();
    }

    private void ShowStreamlines()
    {
        if (_visualization3D == null || _results?.Streamlines == null || !_results.Streamlines.Any())
        {
            Logger.LogWarning("No streamlines to display.");
            return;
        }

        // Create an empty mesh object to hold the streamline geometry.
        // The path is temporary and not critical for in-memory visualization.
        var streamlineMesh =
            Mesh3DDataset.CreateEmpty("Streamlines", Path.Combine(Path.GetTempPath(), "streamlines_export.obj"));

        var vertices = new List<Vector3>();
        var faces = new List<int[]>();

        // Process each streamline path
        foreach (var streamline in _results.Streamlines)
        {
            var baseVertexIndex = vertices.Count;
            vertices.AddRange(streamline);

            // Create line segments (faces with 2 vertices) connecting the points of the streamline
            for (var i = 0; i < streamline.Count - 1; i++)
                faces.Add(new[] { baseVertexIndex + i, baseVertexIndex + i + 1 });
        }

        // Populate the mesh object with the generated data
        streamlineMesh.Vertices.AddRange(vertices);
        streamlineMesh.Faces.AddRange(faces);

        // Update the mesh's internal counts
        streamlineMesh.VertexCount = streamlineMesh.Vertices.Count;
        streamlineMesh.FaceCount = streamlineMesh.Faces.Count;

        // Display the generated mesh in the 3D viewer
        _visualization3D.ClearDynamicMeshes();
        _visualization3D.AddMesh(streamlineMesh);
        _visualization3D.SetRenderMode(GeothermalVisualization3D.RenderMode.Streamlines);
        Logger.Log("Streamlines added to visualization.");
    }

    private void ShowVelocityField()
    {
        if (_visualization3D == null || _results?.DarcyVelocityField == null)
        {
            Logger.LogWarning("No velocity field data available to display.");
            return;
        }

        // The visualization class handles the rendering of the velocity field.
        // This method's job is to simply switch the renderer to the correct mode.
        _visualization3D.ClearDynamicMeshes();
        _visualization3D.SetRenderMode(GeothermalVisualization3D.RenderMode.Velocity);
        Logger.Log(
            "Velocity field visualization activated. The domain surface now shows flux magnitude with directional indicators.");
    }

    /// <summary>
    ///     Initializes the BTES curve editor with current seasonal curve data
    /// </summary>
    private void InitializeBTESCurveEditor()
    {
        if (_options.SeasonalEnergyCurve.Count == 0)
        {
            _options.InitializeDefaultSeasonalCurve();
        }

        // Convert seasonal curve to curve points (day, energy)
        var curvePoints = new List<CurvePoint>();
        for (int i = 0; i < _options.SeasonalEnergyCurve.Count; i++)
        {
            float day = i;
            float energy = (float)_options.SeasonalEnergyCurve[i];
            curvePoints.Add(new CurvePoint(day, energy));
        }

        // Find min/max for curve editor range
        float minEnergy = (float)_options.SeasonalEnergyCurve.Min();
        float maxEnergy = (float)_options.SeasonalEnergyCurve.Max();
        float energyRange = Math.Max(Math.Abs(minEnergy), Math.Abs(maxEnergy));

        _btesCurveEditor = new ImGuiCurveEditor(
            "BTESSeasonalCurve",
            "BTES Seasonal Energy Curve Editor",
            "Day of Year",
            "Daily Energy (kWh/day)",
            curvePoints,
            rangeMin: new Vector2(0, -energyRange * 1.2f),
            rangeMax: new Vector2(364, energyRange * 1.2f)
        );
    }

    /// <summary>
    ///     Handles BTES curve editor submission (called every frame)
    /// </summary>
    private void HandleBTESCurveEditor()
    {
        if (_btesCurveEditor == null) return;

        if (_btesCurveEditor.Submit(out float[] curveData, resolution: 365))
        {
            // User clicked OK - update seasonal curve
            _options.SeasonalEnergyCurve.Clear();
            foreach (float value in curveData)
            {
                _options.SeasonalEnergyCurve.Add(value);
            }
            Logger.Log($"BTES seasonal curve updated with {curveData.Length} points");
            _selectedPreset = 0; // Switch to Custom
        }
    }
}