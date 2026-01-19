// GeoscientistToolkit/UI/PNMTools.cs - Production Version with Confining Pressure

using System.Globalization;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public class PNMTools : IDatasetTools
{
    private readonly float[] _bulkDiffusivities =
    {
        2.299e-9f, // Water self-diffusion in m²/s
        1.49e-9f, // Methane in water
        1.92e-9f, // CO2 in water
        5.0e-10f, // Light oil in water (approximate)
        1.0e-10f, // Heavy oil in water (approximate)
        1.0e-9f // Custom default
    };

    // --- NEW: Diffusivity Fluid Presets ---
    private readonly string[] _diffusivityFluidTypes =
    {
        "Water Self-diffusion (25°C)",
        "Methane in Water (25°C)",
        "CO₂ in Water (25°C)",
        "Oil in Water (Light, 25°C)",
        "Oil in Water (Heavy, 25°C)",
        "Custom"
    };

    private readonly ImGuiExportFileDialog _exportDialog;
    private readonly ImGuiExportFileDialog _exportResultsDialog;

    private readonly string[] _fluidTypes =
    {
        "Water (20°C, 1.0 cP)",
        "Air (20°C, 0.018 cP)",
        "Nitrogen (20°C, 0.018 cP)",
        "CO₂ (20°C, 0.015 cP)",
        "Oil (Light, 5.0 cP)",
        "Oil (Heavy, 100.0 cP)",
        "Custom"
    };


    private readonly float[] _fluidViscosities =
    {
        1.0f, // Water
        0.018f, // Air
        0.018f, // Nitrogen
        0.015f, // CO2
        5.0f, // Light oil
        100.0f, // Heavy oil
        1.0f // Custom default
    };

    private readonly float[] _pressureConversions = { 1.0f, 1000.0f, 100000.0f, 6894.76f };

    private readonly string[] _pressureUnits = { "Pa", "kPa", "bar", "psi" };

    private readonly (float pore, float throat)[] _rockCompressibilities =
    {
        (0.015f, 0.025f), // High porosity sandstone
        (0.008f, 0.015f), // Low porosity sandstone
        (0.005f, 0.010f), // Limestone
        (0.020f, 0.035f), // Shale
        (0.002f, 0.004f), // Granite
        (0.015f, 0.025f) // Custom default
    };

    // Rock type presets for compressibility
    private readonly string[] _rockTypes =
    {
        "Sandstone (High Porosity)",
        "Sandstone (Low Porosity)",
        "Limestone",
        "Shale",
        "Granite",
        "Custom"
    };

    private bool _calcDarcy = true;
    private bool _calcLatticeBoltzmann; // Default off (slowest)
    private bool _calcNavierStokes = true;
    private string _calculationStatus = "";
    private float _confiningPressure = 5.0f; // MPa (typical reservoir pressure)
    private bool _correctForTortuosity = true;
    private float _customBulkDiffusivity = 1.0e-9f; // m²/s
    private float _customViscosity = 1.0f; // cP
    private int _diffusivityFluidIndex;
    private string _diffusivityStatus = "";
    private int _diffusivitySteps = 2000;
    private int _diffusivityWalkers = 50000;
    private int _flowAxisIndex = 2; // Z-axis
    private int _fluidTypeIndex;

    // Pressure parameters
    private float _inletPressure = 2.0f; // Pa (default 2 Pa)

    // --- Permeability Calculator State ---
    private bool _isCalculating;

    // --- NEW: Diffusivity Calculator State ---
    private bool _isCalculatingDiffusivity;
    private DiffusivityResults _lastDiffusivityResults;


    // Store last results for display
    private PermeabilityResults _lastResults;
    private float _outletPressure; // Pa (default 0 Pa)
    private float _poreCompressibility = 0.015f; // 1/MPa (typical sandstone)
    private int _pressureUnitIndex; // 0=Pa, 1=kPa, 2=bar, 3=psi
    private int _rockTypeIndex; // For preset compressibility values
    private float _throatCompressibility = 0.025f; // 1/MPa (throats more compressible)

    // --- PNM Reactive Transport State ---
    private bool _isReactiveTransportRunning;
    private string _reactiveTransportStatus = "";
    private float _rtTotalTime = 3600f;
    private float _rtTimeStep = 1f;
    private float _rtOutputInterval = 60f;
    private int _rtFlowAxisIndex = 2;
    private float _rtInletPressure = 1.0f;
    private float _rtOutletPressure;
    private float _rtFluidViscosity = 1.0f;
    private float _rtFluidDensity = 1000f;
    private float _rtInletTemperature = 298.15f;
    private float _rtOutletTemperature = 298.15f;
    private float _rtThermalConductivity = 0.6f;
    private float _rtSpecificHeat = 4184f;
    private float _rtMolecularDiffusivity = 2.299e-9f;
    private float _rtDispersivity = 0.1f;
    private bool _rtEnableReactions = true;
    private bool _rtUpdateGeometry = true;
    private float _rtMinPoreRadius = 0.1f;
    private float _rtMinThroatRadius = 0.05f;
    private string _rtInitialConcentrations = "Ca2+=0.01; CO32-=0.01";
    private string _rtInletConcentrations = "";
    private string _rtInitialMinerals = "Calcite=0.0";
    private string _rtReactionMinerals = "Calcite";

    // Confining pressure parameters
    private bool _useConfiningPressure;
    private bool _useGpu;

    public PNMTools()
    {
        _exportDialog = new ImGuiExportFileDialog("ExportPNMDialog", "Export PNM");
        _exportDialog.SetExtensions((".pnm.json", "PNM JSON File"));

        _exportResultsDialog = new ImGuiExportFileDialog("ExportResultsDialog", "Export Results");
        _exportResultsDialog.SetExtensions(
            (".csv", "CSV (Comma-separated values)"),
            (".txt", "Text Report")
        );
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not PNMDataset pnm) return;

        ImGui.Text("PNM Analysis Tools");
        ImGui.Separator();

        // Network Statistics
        if (ImGui.CollapsingHeader("Network Statistics", ImGuiTreeNodeFlags.DefaultOpen)) DrawNetworkStatistics(pnm);

        ImGui.Spacing();

        // Absolute Permeability Calculator
        if (ImGui.CollapsingHeader("Absolute Permeability Calculator", ImGuiTreeNodeFlags.DefaultOpen))
            DrawPermeabilityCalculator(pnm);

        ImGui.Spacing();

        // --- NEW: Molecular Diffusivity Calculator ---
        if (ImGui.CollapsingHeader("Molecular Diffusivity Calculator", ImGuiTreeNodeFlags.DefaultOpen))
            DrawDiffusivityCalculator(pnm);

        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Reactive Transport", ImGuiTreeNodeFlags.DefaultOpen))
            DrawReactiveTransport(pnm);

        ImGui.Spacing();

        // Export Options
        if (ImGui.CollapsingHeader("Export")) DrawExportSection(pnm);

        // Handle dialogs
        HandleDialogs(pnm);
    }

    private void DrawDiffusivityCalculator(PNMDataset pnm)
    {
        ImGui.Indent();

        // Display last results if available
        if (_lastDiffusivityResults != null || pnm.EffectiveDiffusivity > 0)
        {
            DrawDiffusivityResults(pnm); // Pass the pnm object
            ImGui.Separator();
        }

        // Fluid properties section
        ImGui.Text("Fluid Properties:");
        ImGui.SetNextItemWidth(250);
        if (ImGui.Combo("Fluid Type##Diff", ref _diffusivityFluidIndex, _diffusivityFluidTypes,
                _diffusivityFluidTypes.Length))
            if (_diffusivityFluidIndex < _bulkDiffusivities.Length - 1)
                _customBulkDiffusivity = _bulkDiffusivities[_diffusivityFluidIndex];

        if (_diffusivityFluidIndex == _diffusivityFluidTypes.Length - 1) // Custom
        {
            ImGui.SetNextItemWidth(150);
            ImGui.InputFloat("Bulk Diffusivity (m²/s)", ref _customBulkDiffusivity, 1e-11f, 1e-10f, "%.2e");
            _customBulkDiffusivity = Math.Clamp(_customBulkDiffusivity, 1e-12f, 1e-7f);
        }
        else
        {
            ImGui.Text($"Bulk Diffusivity (D₀): {_bulkDiffusivities[_diffusivityFluidIndex]:E3} m²/s");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Simulation parameters
        ImGui.Text("Simulation Parameters:");
        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("Number of Walkers", ref _diffusivityWalkers, 1000, 10000);
        _diffusivityWalkers = Math.Clamp(_diffusivityWalkers, 100, 1000000);

        ImGui.SetNextItemWidth(150);
        ImGui.InputInt("Simulation Steps", ref _diffusivitySteps, 100, 1000);
        _diffusivitySteps = Math.Clamp(_diffusivitySteps, 100, 100000);

        ImGui.Spacing();
        ImGui.Separator();

        // Calculate button
        if (_isCalculatingDiffusivity)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Calculating...", new Vector2(-1, 30));
            ImGui.EndDisabled();
            ImGui.TextColored(new Vector4(1, 1, 0, 1), _diffusivityStatus);
        }
        else
        {
            var canCalculate = pnm.Pores.Count > 0 && pnm.Throats.Count > 0;
            if (!canCalculate) ImGui.BeginDisabled();

            if (ImGui.Button("Calculate Diffusivity", new Vector2(-1, 30)))
            {
                var options = new DiffusivityOptions
                {
                    Dataset = pnm,
                    BulkDiffusivity = _diffusivityFluidIndex == _diffusivityFluidTypes.Length - 1
                        ? _customBulkDiffusivity
                        : _bulkDiffusivities[_diffusivityFluidIndex],
                    NumberOfWalkers = _diffusivityWalkers,
                    NumberOfSteps = _diffusivitySteps
                };
                StartDiffusivityCalculation(options);
            }

            if (!canCalculate) ImGui.EndDisabled();
        }

        ImGui.Unindent();
    }

    private void DrawDiffusivityResults(PNMDataset pnm)
    {
        var results = _lastDiffusivityResults;

        // Prefer results; fall back to dataset members that already exist in your codebase.
        double D0 = results?.BulkDiffusivity ?? pnm.BulkDiffusivity;
        double Deff = results?.EffectiveDiffusivity ?? pnm.EffectiveDiffusivity;

        // Formation factor: recompute safely if needed
        var F = results?.FormationFactor ?? (D0 > 0 && Deff > 0 ? D0 / Deff : pnm.FormationFactor);

        // Transport tortuosity "τ²" as stored
        double tau2Raw = results?.Tortuosity ?? pnm.TransportTortuosity;

        // BUGFIX: if τ² was saved as a percent-style fraction (<1), rescale for display
        var tau2Display = tau2Raw > 0.0 && tau2Raw < 1.0 ? tau2Raw * 100.0 : tau2Raw;

        // Also show τ for sanity checks
        var tauDisplay = tau2Display > 0.0 ? Math.Sqrt(tau2Display) : 0.0;

        // Geometric tortuosity as-is
        double tauGeom = results?.GeometricTortuosity ?? pnm.Tortuosity;

        // Porosity: use only what we are sure exists (from results). If absent, show "—".
        var hasPhi = results != null && results.Porosity > 0.0;
        var phi = hasPhi ? results.Porosity : double.NaN;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.10f, 0.15f, 0.10f, 0.50f));
        ImGui.BeginChild("DiffusivityResults", new Vector2(-1, 220), ImGuiChildFlags.Border);

        ImGui.Text("Molecular Diffusivity Results");
        ImGui.Separator();

        if (ImGui.BeginTable("DiffResultsTable", 2,
                ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX))
        {
            ImGui.TableSetupColumn("Parameter", ImGuiTableColumnFlags.WidthFixed, 190);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Bulk Diffusivity (D₀):");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{D0:E3} m²/s");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Effective Diffusivity (D_eff):");
            ImGui.TableSetColumnIndex(1);
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"{Deff:E3} m²/s");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Formation Factor (F):");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{F:F3}");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Network Porosity (φ):");
            ImGui.TableSetColumnIndex(1);
            if (hasPhi) ImGui.Text($"{phi:P2}");
            else ImGui.Text("—");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Transport Tortuosity (τ²):");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{tau2Display:F3}");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Transport Tortuosity (τ):");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{tauDisplay:F3}");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Geometric Tortuosity (τ_geo):");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{tauGeom:F3}");

            ImGui.EndTable();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void StartDiffusivityCalculation(DiffusivityOptions options)
    {
        _isCalculatingDiffusivity = true;
        _diffusivityStatus = "Initializing random walk...";
        var pnm = options.Dataset;

        Task.Run(() =>
        {
            try
            {
                _lastDiffusivityResults = MolecularDiffusivity.Calculate(options,
                    progress => _diffusivityStatus = progress);
                _diffusivityStatus = "Calculation complete!";

                // --- NEW: Save results to the dataset object ---
                if (_lastDiffusivityResults != null)
                {
                    pnm.BulkDiffusivity = _lastDiffusivityResults.BulkDiffusivity;
                    pnm.EffectiveDiffusivity = _lastDiffusivityResults.EffectiveDiffusivity;
                    pnm.FormationFactor = _lastDiffusivityResults.FormationFactor;
                    pnm.TransportTortuosity = _lastDiffusivityResults.Tortuosity;

                    // Notify the UI and project manager that data has changed
                    ProjectManager.Instance.NotifyDatasetDataChanged(pnm);
                }
            }
            catch (Exception ex)
            {
                _diffusivityStatus = $"Error: {ex.Message}";
                Logger.LogError($"[Diffusivity] Calculation failed: {ex}");
            }
            finally
            {
                Thread.Sleep(1000);
                _isCalculatingDiffusivity = false;
            }
        });
    }

    private void DrawNetworkStatistics(PNMDataset pnm)
    {
        ImGui.Indent();

        if (ImGui.BeginTable("NetStatsTable", 2, ImGuiTableFlags.BordersInner))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Pores:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{pnm.Pores.Count:N0}");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Throats:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{pnm.Throats.Count:N0}");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Avg. Connectivity:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{(pnm.Pores.Count > 0 ? pnm.Throats.Count * 2.0f / pnm.Pores.Count : 0):F2}");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Voxel Size:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{pnm.VoxelSize:F3} μm");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Tortuosity:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{pnm.Tortuosity:F4}");

            // Porosity based on MATERIAL bounding box
            if (pnm.Pores.Count > 0)
            {
                var minBounds = new Vector3(
                    pnm.Pores.Min(p => p.Position.X),
                    pnm.Pores.Min(p => p.Position.Y),
                    pnm.Pores.Min(p => p.Position.Z));
                var maxBounds = new Vector3(
                    pnm.Pores.Max(p => p.Position.X),
                    pnm.Pores.Max(p => p.Position.Y),
                    pnm.Pores.Max(p => p.Position.Z));

                // Add margin for pore radii
                var margin = pnm.MaxPoreRadius;
                var materialVolumeVoxels = (maxBounds.X - minBounds.X + 2 * margin) *
                                           (maxBounds.Y - minBounds.Y + 2 * margin) *
                                           (maxBounds.Z - minBounds.Z + 2 * margin);

                var poreVolume = pnm.Pores.Sum(p => p.VolumeVoxels);
                var porosity = materialVolumeVoxels > 0 ? poreVolume / materialVolumeVoxels : 0;
                porosity = Math.Clamp(porosity, 0, 1);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Est. Porosity:");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{porosity:P2}");
            }

            ImGui.EndTable();
        }

        ImGui.Unindent();
    }

    private void DrawReactiveTransport(PNMDataset pnm)
    {
        ImGui.Indent();

        ImGui.Text("Simulation Controls:");
        ImGui.InputFloat("Total Time (s)", ref _rtTotalTime, 1, 10, "%.1f");
        _rtTotalTime = Math.Max(1f, _rtTotalTime);

        ImGui.InputFloat("Time Step (s)", ref _rtTimeStep, 0.1f, 1, "%.2f");
        _rtTimeStep = Math.Clamp(_rtTimeStep, 0.01f, _rtTotalTime);

        ImGui.InputFloat("Output Interval (s)", ref _rtOutputInterval, 1, 10, "%.1f");
        _rtOutputInterval = Math.Max(_rtTimeStep, _rtOutputInterval);

        ImGui.Separator();
        ImGui.Text("Flow & Thermal:");
        ImGui.SetNextItemWidth(150);
        ImGui.Combo("Flow Axis", ref _rtFlowAxisIndex, new[] { "X", "Y", "Z" }, 3);
        ImGui.InputFloat("Inlet Pressure (Pa)", ref _rtInletPressure, 1, 10, "%.2f");
        ImGui.InputFloat("Outlet Pressure (Pa)", ref _rtOutletPressure, 1, 10, "%.2f");
        ImGui.InputFloat("Fluid Viscosity (cP)", ref _rtFluidViscosity, 0.1f, 1, "%.3f");
        ImGui.InputFloat("Fluid Density (kg/m³)", ref _rtFluidDensity, 1, 10, "%.1f");
        ImGui.InputFloat("Inlet Temperature (K)", ref _rtInletTemperature, 1, 10, "%.2f");
        ImGui.InputFloat("Outlet Temperature (K)", ref _rtOutletTemperature, 1, 10, "%.2f");
        ImGui.InputFloat("Thermal Conductivity (W/m·K)", ref _rtThermalConductivity, 0.01f, 0.1f, "%.3f");
        ImGui.InputFloat("Specific Heat (J/kg·K)", ref _rtSpecificHeat, 1, 10, "%.1f");

        ImGui.Separator();
        ImGui.Text("Transport:");
        ImGui.InputFloat("Molecular Diffusivity (m²/s)", ref _rtMolecularDiffusivity, 1e-10f, 1e-9f, "%.2e");
        ImGui.InputFloat("Dispersivity (m)", ref _rtDispersivity, 0.01f, 0.1f, "%.3f");

        ImGui.Separator();
        ImGui.Text("Reactions & Geometry:");
        ImGui.Checkbox("Enable Reactions", ref _rtEnableReactions);
        ImGui.Checkbox("Update Geometry", ref _rtUpdateGeometry);
        ImGui.InputFloat("Min Pore Radius (vox)", ref _rtMinPoreRadius, 0.01f, 0.1f, "%.3f");
        ImGui.InputFloat("Min Throat Radius (vox)", ref _rtMinThroatRadius, 0.01f, 0.1f, "%.3f");

        ImGui.Separator();
        ImGui.Text("Initial Concentrations (mol/L): name=value");
        ImGui.InputTextMultiline("##PNMInitialConc", ref _rtInitialConcentrations, 2048,
            new Vector2(-1, 60));

        ImGui.Text("Inlet Concentrations (mol/L): name=value");
        ImGui.InputTextMultiline("##PNMInletConc", ref _rtInletConcentrations, 2048,
            new Vector2(-1, 60));

        ImGui.Text("Initial Minerals (μm³): name=value");
        ImGui.InputTextMultiline("##PNMInitialMinerals", ref _rtInitialMinerals, 2048,
            new Vector2(-1, 60));

        ImGui.Text("Reaction Minerals (comma-separated names)");
        ImGui.InputText("##PNMReactionMinerals", ref _rtReactionMinerals, 1024);

        ImGui.Spacing();

        if (_isReactiveTransportRunning)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Running...", new Vector2(-1, 30));
            ImGui.EndDisabled();
            ImGui.TextColored(new Vector4(1, 1, 0, 1), _reactiveTransportStatus);
        }
        else
        {
            if (ImGui.Button("Run Reactive Transport", new Vector2(-1, 30)))
            {
                var options = new PNMReactiveTransportOptions
                {
                    TotalTime = _rtTotalTime,
                    TimeStep = _rtTimeStep,
                    OutputInterval = _rtOutputInterval,
                    FlowAxis = (FlowAxis)_rtFlowAxisIndex,
                    InletPressure = _rtInletPressure,
                    OutletPressure = _rtOutletPressure,
                    FluidViscosity = _rtFluidViscosity,
                    FluidDensity = _rtFluidDensity,
                    InletTemperature = _rtInletTemperature,
                    OutletTemperature = _rtOutletTemperature,
                    ThermalConductivity = _rtThermalConductivity,
                    SpecificHeat = _rtSpecificHeat,
                    MolecularDiffusivity = _rtMolecularDiffusivity,
                    Dispersivity = _rtDispersivity,
                    EnableReactions = _rtEnableReactions,
                    UpdateGeometry = _rtUpdateGeometry,
                    MinPoreRadius = _rtMinPoreRadius,
                    MinThroatRadius = _rtMinThroatRadius,
                    InitialConcentrations = ParseKeyValuePairs(_rtInitialConcentrations),
                    InletConcentrations = ParseKeyValuePairs(_rtInletConcentrations),
                    InitialMinerals = ParseKeyValuePairs(_rtInitialMinerals),
                    ReactionMinerals = ParseList(_rtReactionMinerals)
                };

                StartReactiveTransport(pnm, options);
            }
        }

        if (pnm.ReactiveTransportResults != null)
        {
            ImGui.Spacing();
            ImGui.SeparatorText("Reactive Transport Results");
            ImGui.Text($"Initial permeability: {pnm.ReactiveTransportResults.InitialPermeability:E3} mD");
            ImGui.Text($"Final permeability: {pnm.ReactiveTransportResults.FinalPermeability:E3} mD");
            ImGui.Text($"Permeability change: {pnm.ReactiveTransportResults.PermeabilityChange:P2}");
            ImGui.Text($"Time steps stored: {pnm.ReactiveTransportResults.TimeSteps.Count}");
        }

        ImGui.Unindent();
    }

    private void StartReactiveTransport(PNMDataset pnm, PNMReactiveTransportOptions options)
    {
        _isReactiveTransportRunning = true;
        _reactiveTransportStatus = "Initializing reactive transport...";

        Task.Run(() =>
        {
            try
            {
                var progress = new Progress<(float progress, string message)>(p =>
                {
                    _reactiveTransportStatus = $"{p.message} ({p.progress:P0})";
                });

                var results = PNMReactiveTransport.Solve(pnm, options, progress);
                pnm.ReactiveTransportResults = results;
                pnm.ReactiveTransportState = results.TimeSteps.LastOrDefault();

                _reactiveTransportStatus = "Reactive transport complete.";
                ProjectManager.Instance.NotifyDatasetDataChanged(pnm);
            }
            catch (Exception ex)
            {
                _reactiveTransportStatus = $"Error: {ex.Message}";
                Logger.LogError($"[PNMReactiveTransport] Simulation failed: {ex}");
            }
            finally
            {
                Thread.Sleep(1000);
                _isReactiveTransportRunning = false;
            }
        });
    }

    private static Dictionary<string, float> ParseKeyValuePairs(string input)
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input)) return result;

        var entries = input.Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split('=', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;

            if (float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                result[key] = value;
        }

        return result;
    }

    private static List<string> ParseList(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new List<string>();

        return input
            .Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void DrawPermeabilityCalculator(PNMDataset pnm)
    {
        ImGui.Indent();

        // Display last results if available
        if (_lastResults != null || pnm.DarcyPermeability > 0 || pnm.NavierStokesPermeability > 0 ||
            pnm.LatticeBoltzmannPermeability > 0)
        {
            DrawPermeabilityResults(pnm);
            ImGui.Separator();
        }

        // Fluid properties section
        ImGui.Text("Fluid Properties:");
        ImGui.SetNextItemWidth(250);
        if (ImGui.Combo("Fluid Type", ref _fluidTypeIndex, _fluidTypes, _fluidTypes.Length))
            if (_fluidTypeIndex < _fluidViscosities.Length - 1)
                _customViscosity = _fluidViscosities[_fluidTypeIndex];

        if (_fluidTypeIndex == _fluidTypes.Length - 1) // Custom
        {
            ImGui.SetNextItemWidth(150);
            ImGui.InputFloat("Viscosity (cP)", ref _customViscosity, 0.001f, 0.1f, "%.3f");
            _customViscosity = Math.Clamp(_customViscosity, 0.001f, 10000f);

            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Dynamic viscosity in centipoise\n" +
                                 "Water @ 20°C: 1.0 cP\n" +
                                 "Air @ 20°C: 0.018 cP\n" +
                                 "Motor oil: 100-1000 cP");
        }
        else
        {
            ImGui.Text($"Viscosity: {_fluidViscosities[_fluidTypeIndex]:F3} cP");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Pressure configuration section
        ImGui.Text("Pressure Configuration:");

        // Pressure unit selector
        ImGui.SetNextItemWidth(100);
        ImGui.Combo("Pressure Unit", ref _pressureUnitIndex, _pressureUnits, _pressureUnits.Length);

        // Inlet pressure
        ImGui.SetNextItemWidth(150);
        var inletDisplay = _inletPressure / _pressureConversions[_pressureUnitIndex];
        if (ImGui.InputFloat($"Inlet Pressure ({_pressureUnits[_pressureUnitIndex]})", ref inletDisplay, 0.1f, 1.0f,
                "%.3f")) _inletPressure = inletDisplay * _pressureConversions[_pressureUnitIndex];

        // Outlet pressure
        ImGui.SetNextItemWidth(150);
        var outletDisplay = _outletPressure / _pressureConversions[_pressureUnitIndex];
        if (ImGui.InputFloat($"Outlet Pressure ({_pressureUnits[_pressureUnitIndex]})", ref outletDisplay, 0.1f, 1.0f,
                "%.3f")) _outletPressure = outletDisplay * _pressureConversions[_pressureUnitIndex];

        // Show pressure drop
        var pressureDrop = Math.Abs(_inletPressure - _outletPressure);
        ImGui.Text(
            $"Pressure Drop: {pressureDrop:F3} Pa ({pressureDrop / _pressureConversions[_pressureUnitIndex]:F3} {_pressureUnits[_pressureUnitIndex]})");

        if (pressureDrop < 0.001f)
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Warning: Pressure drop is too small!");

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Typical pressure drops:\n" +
                             "Laboratory: 0.1-10 kPa\n" +
                             "Field conditions: 10-1000 kPa\n" +
                             "High pressure: >1000 kPa");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Confining Pressure Section
        ImGui.Text("Confining Pressure (Stress Effects):");

        ImGui.Checkbox("Apply Confining Pressure", ref _useConfiningPressure);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Confining pressure simulates reservoir conditions.\n" +
                             "Increases in pressure reduce pore/throat radii,\n" +
                             "decreasing permeability.\n\n" +
                             "Typical values:\n" +
                             "Shallow reservoir: 5-15 MPa\n" +
                             "Deep reservoir: 20-50 MPa\n" +
                             "Ultra-deep: >50 MPa");

        if (_useConfiningPressure)
        {
            ImGui.Indent();

            // Confining pressure value
            ImGui.SetNextItemWidth(150);
            ImGui.InputFloat("Confining Pressure (MPa)", ref _confiningPressure, 0.5f, 5.0f, "%.1f");
            _confiningPressure = Math.Clamp(_confiningPressure, 0.0f, 200.0f);

            // Rock type selector for compressibility presets
            ImGui.SetNextItemWidth(250);
            if (ImGui.Combo("Rock Type", ref _rockTypeIndex, _rockTypes, _rockTypes.Length))
                if (_rockTypeIndex < _rockCompressibilities.Length - 1) // Not custom
                {
                    _poreCompressibility = _rockCompressibilities[_rockTypeIndex].pore;
                    _throatCompressibility = _rockCompressibilities[_rockTypeIndex].throat;
                }

            // Compressibility values
            if (_rockTypeIndex == _rockTypes.Length - 1) // Custom
            {
                ImGui.SetNextItemWidth(150);
                ImGui.InputFloat("Pore Compressibility (1/MPa)", ref _poreCompressibility, 0.001f, 0.01f, "%.4f");
                _poreCompressibility = Math.Clamp(_poreCompressibility, 0.001f, 0.1f);

                ImGui.SetNextItemWidth(150);
                ImGui.InputFloat("Throat Compressibility (1/MPa)", ref _throatCompressibility, 0.001f, 0.01f, "%.4f");
                _throatCompressibility = Math.Clamp(_throatCompressibility, 0.001f, 0.15f);
            }
            else
            {
                ImGui.Text($"Pore Compressibility: {_poreCompressibility:F4} 1/MPa");
                ImGui.Text($"Throat Compressibility: {_throatCompressibility:F4} 1/MPa");
            }

            // Estimate permeability reduction
            if (_confiningPressure > 0)
            {
                var poreReduction = 1.0f - MathF.Exp(-_poreCompressibility * _confiningPressure);
                var throatReduction = 1.0f - MathF.Exp(-_throatCompressibility * _confiningPressure);

                // Since K ∝ r^4, the permeability reduction is approximately
                var permReduction = MathF.Pow(1 - throatReduction, 4);

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Expected Effects:");
                ImGui.Text($"  Pore radius reduction: ~{poreReduction:P1}");
                ImGui.Text($"  Throat radius reduction: ~{throatReduction:P1}");
                ImGui.Text($"  Permeability reduction: ~{1 - permReduction:P1}");
            }

            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Flow direction
        ImGui.Text("Flow Configuration:");
        string[] axes = { "X-axis", "Y-axis", "Z-axis" };
        ImGui.SetNextItemWidth(150);
        ImGui.Combo("Flow Direction", ref _flowAxisIndex, axes, axes.Length);

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Direction of pressure gradient.\n" +
                             "Typically Z-axis for vertical cores.");

        ImGui.Spacing();

        // Tortuosity correction
        ImGui.Checkbox("Apply Tortuosity Correction", ref _correctForTortuosity);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Divides permeability by τ²\n" +
                             $"Current tortuosity: {pnm.Tortuosity:F3}\n" +
                             $"Correction factor: {1.0f / (pnm.Tortuosity * pnm.Tortuosity):F3}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Calculation Methods:");

        ImGui.Checkbox("Darcy (Simple Hagen-Poiseuille)", ref _calcDarcy);
        ImGui.Checkbox("Navier-Stokes (Entrance effects)", ref _calcNavierStokes);
        ImGui.Checkbox("Lattice-Boltzmann (Full resistance)", ref _calcLatticeBoltzmann);

        if (!_calcDarcy && !_calcNavierStokes && !_calcLatticeBoltzmann)
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Select at least one method");

        ImGui.Spacing();
        ImGui.Separator();

        // Solver options
        ImGui.Checkbox("Use GPU Acceleration", ref _useGpu);
        if (!OpenCLContext.IsAvailable)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(Not Available)");
            _useGpu = false;
        }

        ImGui.Spacing();

        // Calculate button
        if (_isCalculating)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Calculating...", new Vector2(-1, 30));
            ImGui.EndDisabled();

            ImGui.TextColored(new Vector4(1, 1, 0, 1), _calculationStatus);

            var progress = _calculationStatus.Contains("Darcy") ? 0.33f :
                _calculationStatus.Contains("Navier") ? 0.66f :
                _calculationStatus.Contains("Lattice") ? 0.90f : 0.1f;
            ImGui.ProgressBar(progress, new Vector2(-1, 0));
        }
        else
        {
            var canCalculate = (_calcDarcy || _calcNavierStokes || _calcLatticeBoltzmann) &&
                               pnm.Pores.Count > 0 && pnm.Throats.Count > 0 &&
                               Math.Abs(_inletPressure - _outletPressure) > 0.001f;

            if (!canCalculate) ImGui.BeginDisabled();

            if (ImGui.Button("Calculate Permeability", new Vector2(-1, 30)))
            {
                var options = new PermeabilityOptions
                {
                    Dataset = pnm,
                    Axis = (FlowAxis)_flowAxisIndex,
                    FluidViscosity = _fluidTypeIndex == _fluidTypes.Length - 1
                        ? _customViscosity
                        : _fluidViscosities[_fluidTypeIndex],
                    CorrectForTortuosity = _correctForTortuosity,
                    UseGpu = _useGpu && OpenCLContext.IsAvailable,
                    CalculateDarcy = _calcDarcy,
                    CalculateNavierStokes = _calcNavierStokes,
                    CalculateLatticeBoltzmann = _calcLatticeBoltzmann,
                    InletPressure = _inletPressure,
                    OutletPressure = _outletPressure,

                    // Confining pressure parameters
                    UseConfiningPressure = _useConfiningPressure,
                    ConfiningPressure = _confiningPressure,
                    PoreCompressibility = _poreCompressibility,
                    ThroatCompressibility = _throatCompressibility,
                    CriticalPressure = 100.0f // Could make this configurable
                };
                StartCalculation(options);
            }

            if (!canCalculate) ImGui.EndDisabled();
        }

        ImGui.Unindent();
    }

    private void DrawPermeabilityResults(PNMDataset pnm)
    {
        var results = _lastResults ?? AbsolutePermeability.GetLastResults();

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.15f, 0.5f));

        ImGui.BeginChild("PermeabilityResults", new Vector2(-1, 450), ImGuiChildFlags.Border,
            ImGuiWindowFlags.HorizontalScrollbar);

        ImGui.Text("Permeability Results");
        ImGui.Separator();

        // Parameters table
        if (ImGui.BeginTable("ParamsTable", 2,
                ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX))
        {
            ImGui.TableSetupColumn("Parameter", ImGuiTableColumnFlags.WidthFixed, 180);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 200);

            ImGui.TableHeadersRow();

            // Flow parameters
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Flow Axis:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{results?.FlowAxis ?? "Z"}");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Model Length:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{(results?.ModelLength ?? 0) * 1e6:F1} μm");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Cross-sectional Area:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{(results?.CrossSectionalArea ?? 0) * 1e12:F3} μm²");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Pressure Drop:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{results?.UsedPressureDrop ?? 1.0f:F3} Pa");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Viscosity:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{results?.UsedViscosity ?? 1.0f:F3} cP");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Total Flow Rate:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{results?.TotalFlowRate ?? 0:E3} m³/s");

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Tortuosity (τ):");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{results?.Tortuosity ?? pnm.Tortuosity:F4}");

            // Confining pressure info if applicable
            if (results?.AppliedConfiningPressure > 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextColored(new Vector4(1, 0.8f, 0, 1), "Confining Pressure:");
                ImGui.TableSetColumnIndex(1);
                ImGui.TextColored(new Vector4(1, 0.8f, 0, 1), $"{results.AppliedConfiningPressure:F1} MPa");

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Pore Reduction:");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{results.EffectivePoreReduction:F1}%");

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Throat Reduction:");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{results.EffectiveThroatReduction:F1}%");

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Closed Throats:");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{results.ClosedThroats:N0}");
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Permeability Values:");
        ImGui.Spacing();

        // Permeability results table
        if (ImGui.BeginTable("PermTable", 4, ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg |
                                             ImGuiTableFlags.ScrollX | ImGuiTableFlags.PadOuterX))
        {
            ImGui.TableSetupColumn("Method", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Uncorrected (mD)", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("τ²-Corrected (mD)", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Corrected (Darcy)", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableHeadersRow();

            // Darcy
            if (results?.DarcyUncorrected > 0 || pnm.DarcyPermeability > 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Darcy");
                ImGui.TableSetColumnIndex(1);
                var uncorrected = results?.DarcyUncorrected ?? pnm.DarcyPermeability;
                ImGui.Text($"{uncorrected:F3}");
                ImGui.TableSetColumnIndex(2);
                var corrected = results?.DarcyCorrected ??
                                (pnm.Tortuosity > 0 ? uncorrected / (pnm.Tortuosity * pnm.Tortuosity) : uncorrected);
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"{corrected:F3}");
                ImGui.TableSetColumnIndex(3);
                ImGui.Text($"{corrected / 1000:F6}");
            }

            // Navier-Stokes
            if (results?.NavierStokesUncorrected > 0 || pnm.NavierStokesPermeability > 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Navier-Stokes");
                ImGui.TableSetColumnIndex(1);
                var uncorrected = results?.NavierStokesUncorrected ?? pnm.NavierStokesPermeability;
                ImGui.Text($"{uncorrected:F3}");
                ImGui.TableSetColumnIndex(2);
                var corrected = results?.NavierStokesCorrected ??
                                (pnm.Tortuosity > 0 ? uncorrected / (pnm.Tortuosity * pnm.Tortuosity) : uncorrected);
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"{corrected:F3}");
                ImGui.TableSetColumnIndex(3);
                ImGui.Text($"{corrected / 1000:F6}");
            }

            // Lattice-Boltzmann
            if (results?.LatticeBoltzmannUncorrected > 0 || pnm.LatticeBoltzmannPermeability > 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Lattice-Boltzmann");
                ImGui.TableSetColumnIndex(1);
                var uncorrected = results?.LatticeBoltzmannUncorrected ?? pnm.LatticeBoltzmannPermeability;
                ImGui.Text($"{uncorrected:F3}");
                ImGui.TableSetColumnIndex(2);
                var corrected = results?.LatticeBoltzmannCorrected ??
                                (pnm.Tortuosity > 0 ? uncorrected / (pnm.Tortuosity * pnm.Tortuosity) : uncorrected);
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"{corrected:F3}");
                ImGui.TableSetColumnIndex(3);
                ImGui.Text($"{corrected / 1000:F6}");
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Spacing();

        // Export button
        if (ImGui.Button("Export Results...", new Vector2(-1, 0))) _exportResultsDialog.Open($"{pnm.Name}_results");

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawExportSection(PNMDataset pnm)
    {
        ImGui.Indent();

        ImGui.Text("Export Options:");
        ImGui.Separator();

        // PNM export
        if (ImGui.Button("Export PNM as JSON...", new Vector2(-1, 0))) _exportDialog.Open(pnm.Name);

        ImGui.Spacing();

        // Table datasets
        if (ImGui.Button("Create Pores Table", new Vector2(-1, 0)))
        {
            var poresTbl = pnm.BuildPoresTableDataset($"{pnm.Name}_Pores");
            ProjectManager.Instance.AddDataset(poresTbl);
            Logger.Log("[PNMTools] Created table dataset for pores");
        }

        if (ImGui.Button("Create Throats Table", new Vector2(-1, 0)))
        {
            var throatsTbl = pnm.BuildThroatsTableDataset($"{pnm.Name}_Throats");
            ProjectManager.Instance.AddDataset(throatsTbl);
            Logger.Log("[PNMTools] Created table dataset for throats");
        }

        ImGui.Spacing();

        // CSV export
        if (ImGui.Button("Export Pores CSV...", new Vector2(-1, 0)))
        {
            var dialog = new ImGuiExportFileDialog("ExportPoresCSV", "Export Pores");
            dialog.SetExtensions((".csv", "CSV File"));
            dialog.Open($"{pnm.Name}_pores");
            // Handle in next frame...
        }

        if (ImGui.Button("Export Throats CSV...", new Vector2(-1, 0)))
        {
            var dialog = new ImGuiExportFileDialog("ExportThroatsCSV", "Export Throats");
            dialog.SetExtensions((".csv", "CSV File"));
            dialog.Open($"{pnm.Name}_throats");
            // Handle in next frame...
        }

        ImGui.Unindent();
    }

    private void HandleDialogs(PNMDataset pnm)
    {
        if (_exportDialog.Submit())
            try
            {
                pnm.ExportToJson(_exportDialog.SelectedPath);
                Logger.Log($"[PNMTools] Exported PNM to '{_exportDialog.SelectedPath}'");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PNMTools] Export failed: {ex.Message}");
            }

        if (_exportResultsDialog.Submit())
            try
            {
                ExportResults(_exportResultsDialog.SelectedPath, pnm);
                Logger.Log($"[PNMTools] Exported results to '{_exportResultsDialog.SelectedPath}'");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PNMTools] Results export failed: {ex.Message}");
            }
    }

    private void ExportResults(string path, PNMDataset pnm)
    {
        var results = _lastResults ?? AbsolutePermeability.GetLastResults();
        var diffResults = _lastDiffusivityResults;

        // Check if we have any results to export
        if (results == null && diffResults == null &&
            pnm.DarcyPermeability == 0 && pnm.NavierStokesPermeability == 0 &&
            pnm.LatticeBoltzmannPermeability == 0 && pnm.EffectiveDiffusivity == 0)
        {
            Logger.LogWarning("[PNMTools] No results to export");
            return;
        }

        var ext = Path.GetExtension(path).ToLower();

        if (ext == ".csv")
            // Export as CSV
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine("Pore Network Analysis Results");
                writer.WriteLine($"Dataset,{pnm.Name}");
                writer.WriteLine($"Date,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine();

                // Network Statistics
                writer.WriteLine("Network Statistics");
                writer.WriteLine("Parameter,Value,Unit");
                writer.WriteLine($"Pore Count,{pnm.Pores.Count},");
                writer.WriteLine($"Throat Count,{pnm.Throats.Count},");
                writer.WriteLine($"Voxel Size,{pnm.VoxelSize:F3},μm");
                writer.WriteLine($"Geometric Tortuosity,{pnm.Tortuosity:F4},");

                if (pnm.Pores.Count > 0)
                {
                    var avgConnectivity = pnm.Throats.Count * 2.0 / pnm.Pores.Count;
                    writer.WriteLine($"Average Connectivity,{avgConnectivity:F2},");
                }

                writer.WriteLine();

                // Permeability Results Section
                if (results != null || pnm.DarcyPermeability > 0 || pnm.NavierStokesPermeability > 0 ||
                    pnm.LatticeBoltzmannPermeability > 0)
                {
                    writer.WriteLine("Permeability Analysis");
                    writer.WriteLine("Parameter,Value,Unit");

                    if (results != null)
                    {
                        writer.WriteLine($"Flow Axis,{results.FlowAxis},");
                        writer.WriteLine($"Model Length,{results.ModelLength * 1e6:F3},μm");
                        writer.WriteLine($"Cross-sectional Area,{results.CrossSectionalArea * 1e12:F3},μm²");
                        writer.WriteLine($"Pressure Drop,{results.UsedPressureDrop:F3},Pa");
                        writer.WriteLine($"Fluid Viscosity,{results.UsedViscosity:F3},cP");
                        writer.WriteLine($"Total Flow Rate,{results.TotalFlowRate:E3},m³/s");

                        if (results.AppliedConfiningPressure > 0)
                        {
                            writer.WriteLine($"Confining Pressure,{results.AppliedConfiningPressure:F1},MPa");
                            writer.WriteLine($"Pore Reduction,{results.EffectivePoreReduction:F1},%");
                            writer.WriteLine($"Throat Reduction,{results.EffectiveThroatReduction:F1},%");
                            writer.WriteLine($"Closed Throats,{results.ClosedThroats},");
                        }
                    }

                    writer.WriteLine();
                    writer.WriteLine("Permeability Results");
                    writer.WriteLine("Method,Uncorrected (mD),τ²-Corrected (mD),Uncorrected (D),τ²-Corrected (D)");

                    if ((results?.DarcyUncorrected ?? pnm.DarcyPermeability) > 0)
                    {
                        var darcyUnc = results?.DarcyUncorrected ?? pnm.DarcyPermeability;
                        var darcyCor = results?.DarcyCorrected ??
                                       (pnm.Tortuosity > 0 ? darcyUnc / (pnm.Tortuosity * pnm.Tortuosity) : darcyUnc);
                        writer.WriteLine($"Darcy,{darcyUnc:F6},{darcyCor:F6}," +
                                         $"{darcyUnc / 1000:F9},{darcyCor / 1000:F9}");
                    }

                    if ((results?.NavierStokesUncorrected ?? pnm.NavierStokesPermeability) > 0)
                    {
                        var nsUnc = results?.NavierStokesUncorrected ?? pnm.NavierStokesPermeability;
                        var nsCor = results?.NavierStokesCorrected ??
                                    (pnm.Tortuosity > 0 ? nsUnc / (pnm.Tortuosity * pnm.Tortuosity) : nsUnc);
                        writer.WriteLine($"Navier-Stokes,{nsUnc:F6},{nsCor:F6}," +
                                         $"{nsUnc / 1000:F9},{nsCor / 1000:F9}");
                    }

                    if ((results?.LatticeBoltzmannUncorrected ?? pnm.LatticeBoltzmannPermeability) > 0)
                    {
                        var lbUnc = results?.LatticeBoltzmannUncorrected ?? pnm.LatticeBoltzmannPermeability;
                        var lbCor = results?.LatticeBoltzmannCorrected ??
                                    (pnm.Tortuosity > 0 ? lbUnc / (pnm.Tortuosity * pnm.Tortuosity) : lbUnc);
                        writer.WriteLine($"Lattice-Boltzmann,{lbUnc:F6},{lbCor:F6}," +
                                         $"{lbUnc / 1000:F9},{lbCor / 1000:F9}");
                    }

                    writer.WriteLine();
                }

                // Diffusivity Results Section
                if (diffResults != null || pnm.EffectiveDiffusivity > 0)
                {
                    writer.WriteLine("Molecular Diffusivity Analysis");
                    writer.WriteLine("Parameter,Value,Unit");

                    var D0 = diffResults?.BulkDiffusivity ?? pnm.BulkDiffusivity;
                    var Deff = diffResults?.EffectiveDiffusivity ?? pnm.EffectiveDiffusivity;
                    var F = diffResults?.FormationFactor ??
                            (D0 > 0 && Deff > 0 ? D0 / Deff : pnm.FormationFactor);
                    var tau2Raw = diffResults?.Tortuosity ?? pnm.TransportTortuosity;
                    var tau2Display = tau2Raw > 0.0 && tau2Raw < 1.0 ? tau2Raw * 100.0 : tau2Raw;
                    var tauDisplay = tau2Display > 0.0 ? Math.Sqrt(tau2Display) : 0.0;
                    var tauGeom = diffResults?.GeometricTortuosity ?? pnm.Tortuosity;
                    var phi = diffResults?.Porosity ?? double.NaN;

                    writer.WriteLine($"Bulk Diffusivity (D₀),{D0:E3},m²/s");
                    writer.WriteLine($"Effective Diffusivity (D_eff),{Deff:E3},m²/s");
                    writer.WriteLine($"Formation Factor (F),{F:F3},");

                    if (!double.IsNaN(phi) && phi > 0)
                        writer.WriteLine($"Network Porosity (φ),{phi:F6},");

                    writer.WriteLine($"Transport Tortuosity (τ²),{tau2Display:F3},");
                    writer.WriteLine($"Transport Tortuosity (τ),{tauDisplay:F3},");
                    writer.WriteLine($"Geometric Tortuosity (τ_geo),{tauGeom:F4},");
                    writer.WriteLine();
                }
            }
        else
            // Export as text report
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine("================================================================================");
                writer.WriteLine("                    PORE NETWORK ANALYSIS REPORT");
                writer.WriteLine("================================================================================");
                writer.WriteLine();
                writer.WriteLine($"Dataset: {pnm.Name}");
                writer.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine();

                writer.WriteLine("NETWORK PROPERTIES");
                writer.WriteLine("------------------");
                writer.WriteLine($"  Pores:                {pnm.Pores.Count:N0}");
                writer.WriteLine($"  Throats:              {pnm.Throats.Count:N0}");
                writer.WriteLine($"  Voxel Size:           {pnm.VoxelSize:F3} μm");
                writer.WriteLine($"  Geometric Tortuosity: {pnm.Tortuosity:F4}");

                if (pnm.Pores.Count > 0)
                {
                    var avgConnectivity = pnm.Throats.Count * 2.0 / pnm.Pores.Count;
                    writer.WriteLine($"  Avg. Connectivity:    {avgConnectivity:F2}");
                }

                writer.WriteLine();

                // Permeability Results Section
                if (results != null || pnm.DarcyPermeability > 0 || pnm.NavierStokesPermeability > 0 ||
                    pnm.LatticeBoltzmannPermeability > 0)
                {
                    writer.WriteLine(
                        "================================================================================");
                    writer.WriteLine("                        PERMEABILITY ANALYSIS");
                    writer.WriteLine(
                        "================================================================================");
                    writer.WriteLine();

                    if (results != null)
                    {
                        writer.WriteLine("FLOW CONFIGURATION");
                        writer.WriteLine("------------------");
                        writer.WriteLine($"  Flow Axis:            {results.FlowAxis}");
                        writer.WriteLine($"  Model Length:         {results.ModelLength * 1e6:F3} μm");
                        writer.WriteLine($"  Cross-sectional Area: {results.CrossSectionalArea * 1e12:F3} μm²");
                        writer.WriteLine($"  Inlet Pressure:       {_inletPressure:F3} Pa");
                        writer.WriteLine($"  Outlet Pressure:      {_outletPressure:F3} Pa");
                        writer.WriteLine($"  Pressure Drop:        {results.UsedPressureDrop:F3} Pa");
                        writer.WriteLine($"  Fluid Viscosity:      {results.UsedViscosity:F3} cP");
                        writer.WriteLine($"  Total Flow Rate:      {results.TotalFlowRate:E3} m³/s");

                        if (results.AppliedConfiningPressure > 0)
                        {
                            writer.WriteLine();
                            writer.WriteLine("CONFINING PRESSURE EFFECTS");
                            writer.WriteLine("--------------------------");
                            writer.WriteLine($"  Applied Pressure:     {results.AppliedConfiningPressure:F1} MPa");
                            writer.WriteLine($"  Pore Reduction:       {results.EffectivePoreReduction:F1}%");
                            writer.WriteLine($"  Throat Reduction:     {results.EffectiveThroatReduction:F1}%");
                            writer.WriteLine($"  Closed Throats:       {results.ClosedThroats:N0}");
                        }

                        writer.WriteLine();
                    }

                    writer.WriteLine("PERMEABILITY RESULTS");
                    writer.WriteLine("--------------------");

                    var tortuosity = results?.Tortuosity ?? pnm.Tortuosity;
                    var tau2Correction = tortuosity > 0 ? 1.0f / (tortuosity * tortuosity) : 1.0f;

                    if ((results?.DarcyUncorrected ?? pnm.DarcyPermeability) > 0)
                    {
                        var darcyUnc = results?.DarcyUncorrected ?? pnm.DarcyPermeability;
                        var darcyCor = results?.DarcyCorrected ?? darcyUnc * tau2Correction;

                        writer.WriteLine("  Darcy Method:");
                        writer.WriteLine($"    Uncorrected:        {darcyUnc:F6} mD ({darcyUnc / 1000:F9} D)");
                        writer.WriteLine($"    τ²-Corrected:       {darcyCor:F6} mD ({darcyCor / 1000:F9} D)");
                    }

                    if ((results?.NavierStokesUncorrected ?? pnm.NavierStokesPermeability) > 0)
                    {
                        var nsUnc = results?.NavierStokesUncorrected ?? pnm.NavierStokesPermeability;
                        var nsCor = results?.NavierStokesCorrected ?? nsUnc * tau2Correction;

                        writer.WriteLine("  Navier-Stokes Method:");
                        writer.WriteLine($"    Uncorrected:        {nsUnc:F6} mD ({nsUnc / 1000:F9} D)");
                        writer.WriteLine($"    τ²-Corrected:       {nsCor:F6} mD ({nsCor / 1000:F9} D)");
                    }

                    if ((results?.LatticeBoltzmannUncorrected ?? pnm.LatticeBoltzmannPermeability) > 0)
                    {
                        var lbUnc = results?.LatticeBoltzmannUncorrected ?? pnm.LatticeBoltzmannPermeability;
                        var lbCor = results?.LatticeBoltzmannCorrected ?? lbUnc * tau2Correction;

                        writer.WriteLine("  Lattice-Boltzmann Method:");
                        writer.WriteLine($"    Uncorrected:        {lbUnc:F6} mD ({lbUnc / 1000:F9} D)");
                        writer.WriteLine($"    τ²-Corrected:       {lbCor:F6} mD ({lbCor / 1000:F9} D)");
                    }

                    writer.WriteLine();
                }

                // Diffusivity Results Section
                if (diffResults != null || pnm.EffectiveDiffusivity > 0)
                {
                    writer.WriteLine(
                        "================================================================================");
                    writer.WriteLine("                    MOLECULAR DIFFUSIVITY ANALYSIS");
                    writer.WriteLine(
                        "================================================================================");
                    writer.WriteLine();

                    var D0 = diffResults?.BulkDiffusivity ?? pnm.BulkDiffusivity;
                    var Deff = diffResults?.EffectiveDiffusivity ?? pnm.EffectiveDiffusivity;
                    var F = diffResults?.FormationFactor ??
                            (D0 > 0 && Deff > 0 ? D0 / Deff : pnm.FormationFactor);
                    var tau2Raw = diffResults?.Tortuosity ?? pnm.TransportTortuosity;
                    var tau2Display = tau2Raw > 0.0 && tau2Raw < 1.0 ? tau2Raw * 100.0 : tau2Raw;
                    var tauDisplay = tau2Display > 0.0 ? Math.Sqrt(tau2Display) : 0.0;
                    var tauGeom = diffResults?.GeometricTortuosity ?? pnm.Tortuosity;
                    var phi = diffResults?.Porosity ?? double.NaN;

                    writer.WriteLine("DIFFUSIVITY PARAMETERS");
                    writer.WriteLine("----------------------");
                    writer.WriteLine($"  Bulk Diffusivity (D₀):         {D0:E3} m²/s");
                    writer.WriteLine($"  Effective Diffusivity (D_eff): {Deff:E3} m²/s");
                    writer.WriteLine();

                    writer.WriteLine("TRANSPORT PROPERTIES");
                    writer.WriteLine("--------------------");
                    writer.WriteLine($"  Formation Factor (F):          {F:F3}");

                    if (!double.IsNaN(phi) && phi > 0)
                        writer.WriteLine($"  Network Porosity (φ):          {phi:P2}");

                    writer.WriteLine($"  Transport Tortuosity (τ²):     {tau2Display:F3}");
                    writer.WriteLine($"  Transport Tortuosity (τ):      {tauDisplay:F3}");
                    writer.WriteLine($"  Geometric Tortuosity (τ_geo):  {tauGeom:F4}");
                    writer.WriteLine();

                    writer.WriteLine("RELATIONSHIPS");
                    writer.WriteLine("-------------");
                    writer.WriteLine($"  D_eff = D₀ / F = {D0:E3} / {F:F3} = {Deff:E3} m²/s");
                    writer.WriteLine("  F = D₀ / D_eff = φ × τ² (Archie's Law)");
                    if (!double.IsNaN(phi) && phi > 0 && tau2Display > 0)
                    {
                        var archieCheck = phi * tau2Display / 100.0;
                        writer.WriteLine($"  φ × τ² = {phi:F4} × {tau2Display:F3} = {archieCheck:F3}");
                    }

                    writer.WriteLine();
                }

                writer.WriteLine("================================================================================");
                writer.WriteLine("                              END OF REPORT");
                writer.WriteLine("================================================================================");
            }
    }

    private void StartCalculation(PermeabilityOptions options)
    {
        _isCalculating = true;
        _calculationStatus = "Initializing...";

        Task.Run(() =>
        {
            try
            {
                if (options.UseConfiningPressure)
                    _calculationStatus = "Applying confining pressure effects...";

                if (options.CalculateDarcy)
                    _calculationStatus = "Calculating Darcy permeability...";

                if (options.CalculateNavierStokes)
                    _calculationStatus = "Calculating Navier-Stokes permeability...";

                if (options.CalculateLatticeBoltzmann)
                    _calculationStatus = "Calculating Lattice-Boltzmann permeability...";

                AbsolutePermeability.Calculate(options);
                _lastResults = AbsolutePermeability.GetLastResults();
                _calculationStatus = "Calculation completed!";

                ProjectManager.Instance.NotifyDatasetDataChanged(options.Dataset);
            }
            catch (Exception ex)
            {
                _calculationStatus = $"Error: {ex.Message}";
                Logger.LogError($"[Permeability] Calculation failed: {ex}");
            }
            finally
            {
                Thread.Sleep(1000);
                _isCalculating = false;
            }
        });
    }
}
