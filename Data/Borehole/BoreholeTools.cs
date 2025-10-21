// GeoscientistToolkit/UI/Borehole/BoreholeTools.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Borehole;

/// <summary>
///     Tools for creating and editing borehole/well log data
/// </summary>
public class BoreholeTools : IDatasetTools
{
    private readonly ImGuiExportFileDialog _exportBinaryDialog;

    private readonly string[] _grainSizes = new[]
    {
        "Clay", "Silt", "Very Fine", "Fine", "Medium", "Coarse", "Very Coarse", "Gravel"
    };

    private readonly string[] _lithologyTypes = new[]
    {
        "Sandstone", "Shale", "Limestone", "Clay", "Siltstone",
        "Conglomerate", "Basement", "Coal", "Dolomite", "Mudstone",
        "Marl", "Chalk", "Granite", "Basalt", "Anhydrite"
    };

    private string[] _availableParameters;

    private LithologyUnit _editingUnit = new();
    private float _importDepthFrom;
    private float _importDepthTo = 10f;
    private Vector4 _newColor = new(0.8f, 0.7f, 0.5f, 1.0f);
    private float _newDepthFrom;
    private float _newDepthTo = 10f;
    private string _newDescription = "";
    private string _newGrainSize = "Medium";
    private string _newLithologyType = "Sandstone";

    private string _newUnitName = "New Unit";
    private bool[] _selectedParameters;

    private Dataset _selectedSourceDataset;
    private LithologyUnit _selectedUnit;
    private bool _showAddUnitDialog;
    private bool _showEditUnitDialog;
    private bool _showImportParametersDialog;

    public BoreholeTools()
    {
        _exportBinaryDialog = new ImGuiExportFileDialog("ExportBoreholeBinary", "Export Borehole to Binary");
        _exportBinaryDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption(".bhb", "Borehole Binary File"));
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not BoreholeDataset borehole)
            return;

        ImGui.Text("Borehole Builder");
        ImGui.Separator();

        // Well information section
        if (ImGui.CollapsingHeader("Well Information", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Well Name: {borehole.WellName}");
            ImGui.Text($"Total Depth: {borehole.TotalDepth:F2} m");
            ImGui.Text($"Units Defined: {borehole.LithologyUnits.Count}");
            ImGui.Separator();
        }

        // Lithology units section
        if (ImGui.CollapsingHeader("Lithology Units", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.Button("Add Unit", new Vector2(-1, 0)))
            {
                _newDepthFrom = borehole.LithologyUnits.Any()
                    ? borehole.LithologyUnits.Max(u => u.DepthTo)
                    : 0f;
                _newDepthTo = _newDepthFrom + 10f;
                _showAddUnitDialog = true;
            }

            ImGui.Spacing();

            // List existing units
            for (var i = 0; i < borehole.LithologyUnits.Count; i++)
            {
                var unit = borehole.LithologyUnits[i];

                ImGui.PushID(i);

                // Color indicator
                var colorBox = unit.Color;
                if (ImGui.ColorButton("##color", colorBox, ImGuiColorEditFlags.NoAlpha, new Vector2(20, 20)))
                {
                    // Could open color picker
                }

                ImGui.SameLine();

                // Unit info
                var isSelected = _selectedUnit == unit;
                if (ImGui.Selectable($"{unit.Name} ({unit.DepthFrom:F1}-{unit.DepthTo:F1}m)", isSelected))
                    _selectedUnit = unit;

                // Context menu
                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.MenuItem("Edit"))
                    {
                        _selectedUnit = unit;
                        _editingUnit = new LithologyUnit
                        {
                            ID = unit.ID,
                            Name = unit.Name,
                            LithologyType = unit.LithologyType,
                            DepthFrom = unit.DepthFrom,
                            DepthTo = unit.DepthTo,
                            Color = unit.Color,
                            Description = unit.Description,
                            GrainSize = unit.GrainSize
                        };
                        _showEditUnitDialog = true;
                    }

                    if (ImGui.MenuItem("Delete"))
                    {
                        borehole.LithologyUnits.Remove(unit);
                        if (_selectedUnit == unit)
                            _selectedUnit = null;
                    }

                    ImGui.EndPopup();
                }

                ImGui.PopID();
            }
        }

        // Parameters section
        if (ImGui.CollapsingHeader("Parameters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (_selectedUnit != null)
            {
                ImGui.Text($"Selected: {_selectedUnit.Name}");
                ImGui.Separator();

                if (ImGui.Button("Import from Dataset...", new Vector2(-1, 0)))
                {
                    _importDepthFrom = _selectedUnit.DepthFrom;
                    _importDepthTo = _selectedUnit.DepthTo;
                    _showImportParametersDialog = true;
                }

                ImGui.Spacing();

                // Show current parameters
                if (_selectedUnit.Parameters.Any())
                {
                    ImGui.Text("Current Parameters:");
                    foreach (var param in _selectedUnit.Parameters)
                    {
                        ImGui.BulletText($"{param.Key}: {param.Value:F3}");

                        // Show source if available
                        if (_selectedUnit.ParameterSources.TryGetValue(param.Key, out var source))
                        {
                            ImGui.SameLine();
                            ImGui.TextDisabled($"(from {source.DatasetName})");
                        }
                    }
                }
                else
                {
                    ImGui.TextDisabled("No parameters defined");
                }
            }
            else
            {
                ImGui.TextDisabled("Select a unit to import parameters");
            }
        }

        // Export section
        if (ImGui.CollapsingHeader("Export"))
            if (ImGui.Button("Export to Binary (.bhb)..."))
                _exportBinaryDialog.Open(borehole.Name);

        if (_exportBinaryDialog.Submit()) borehole.SaveToBinaryFile(_exportBinaryDialog.SelectedPath);

        // Parameter tracks visibility
        if (ImGui.CollapsingHeader("Track Visibility"))
            foreach (var track in borehole.ParameterTracks.Values)
            {
                var visible = track.IsVisible;
                if (ImGui.Checkbox(track.Name, ref visible)) track.IsVisible = visible;
            }

        // Display settings
        if (ImGui.CollapsingHeader("Display Settings"))
        {
            var showGrid = borehole.ShowGrid;
            if (ImGui.Checkbox("Show Grid", ref showGrid))
                borehole.ShowGrid = showGrid;

            var showLegend = borehole.ShowLegend;
            if (ImGui.Checkbox("Show Legend", ref showLegend))
                borehole.ShowLegend = showLegend;

            var trackWidth = borehole.TrackWidth;
            if (ImGui.DragFloat("Track Width", ref trackWidth, 1f, 50f, 500f, "%.0f px"))
                borehole.TrackWidth = trackWidth;

            var depthScale = borehole.DepthScaleFactor;
            if (ImGui.DragFloat("Depth Scale", ref depthScale, 0.1f, 0.1f, 10f, "%.1f"))
                borehole.DepthScaleFactor = depthScale;
        }

        // Draw dialogs
        DrawAddUnitDialog(borehole);
        DrawEditUnitDialog(borehole);
        DrawImportParametersDialog(borehole);
    }

    private void DrawAddUnitDialog(BoreholeDataset borehole)
    {
        if (!_showAddUnitDialog)
            return;

        ImGui.OpenPopup("Add Lithology Unit");

        ImGui.SetNextWindowSize(new Vector2(400, 500), ImGuiCond.FirstUseEver);
        if (ImGui.BeginPopupModal("Add Lithology Unit", ref _showAddUnitDialog))
        {
            ImGui.InputText("Name", ref _newUnitName, 128);

            ImGui.Spacing();

            // Lithology type combo
            if (ImGui.BeginCombo("Lithology Type", _newLithologyType))
            {
                foreach (var type in _lithologyTypes)
                {
                    var isSelected = _newLithologyType == type;
                    if (ImGui.Selectable(type, isSelected))
                    {
                        _newLithologyType = type;
                        _newColor = GetDefaultColorForLithology(type);
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImGui.Spacing();

            // Grain size combo
            if (ImGui.BeginCombo("Grain Size", _newGrainSize))
            {
                foreach (var size in _grainSizes)
                {
                    var isSelected = _newGrainSize == size;
                    if (ImGui.Selectable(size, isSelected))
                        _newGrainSize = size;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Depth range
            ImGui.Text("Depth Range (m):");
            ImGui.DragFloat("From", ref _newDepthFrom, 0.1f, 0, borehole.TotalDepth, "%.2f");
            ImGui.DragFloat("To", ref _newDepthTo, 0.1f, _newDepthFrom, borehole.TotalDepth, "%.2f");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Color
            ImGui.Text("Color:");
            ImGui.ColorEdit4("##unitcolor", ref _newColor, ImGuiColorEditFlags.NoAlpha);

            ImGui.Spacing();

            // Description
            ImGui.InputTextMultiline("Description", ref _newDescription, 512, new Vector2(-1, 80));

            ImGui.Spacing();
            ImGui.Separator();

            // Buttons
            if (ImGui.Button("Add", new Vector2(120, 0)))
            {
                var newUnit = new LithologyUnit
                {
                    Name = _newUnitName,
                    LithologyType = _newLithologyType,
                    GrainSize = _newGrainSize,
                    DepthFrom = _newDepthFrom,
                    DepthTo = _newDepthTo,
                    Color = _newColor,
                    Description = _newDescription
                };

                borehole.AddLithologyUnit(newUnit);

                // Reset for next unit
                _newUnitName = "New Unit";
                _newDepthFrom = _newDepthTo;
                _newDepthTo = _newDepthFrom + 10f;
                _newDescription = "";

                _showAddUnitDialog = false;
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0))) _showAddUnitDialog = false;

            ImGui.EndPopup();
        }
    }

    private void DrawEditUnitDialog(BoreholeDataset borehole)
    {
        if (!_showEditUnitDialog || _selectedUnit == null)
            return;

        ImGui.OpenPopup("Edit Lithology Unit");

        ImGui.SetNextWindowSize(new Vector2(400, 500), ImGuiCond.FirstUseEver);
        if (ImGui.BeginPopupModal("Edit Lithology Unit", ref _showEditUnitDialog))
        {
            var unitName = _editingUnit.Name;
            if (ImGui.InputText("Name", ref unitName, 128))
                _editingUnit.Name = unitName;

            ImGui.Spacing();

            // Lithology type combo
            if (ImGui.BeginCombo("Lithology Type", _editingUnit.LithologyType))
            {
                foreach (var type in _lithologyTypes)
                {
                    var isSelected = _editingUnit.LithologyType == type;
                    if (ImGui.Selectable(type, isSelected))
                        _editingUnit.LithologyType = type;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImGui.Spacing();

            // Grain size combo
            if (ImGui.BeginCombo("Grain Size", _editingUnit.GrainSize))
            {
                foreach (var size in _grainSizes)
                {
                    var isSelected = _editingUnit.GrainSize == size;
                    if (ImGui.Selectable(size, isSelected))
                        _editingUnit.GrainSize = size;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Depth range
            ImGui.Text("Depth Range (m):");
            var depthFrom = _editingUnit.DepthFrom;
            if (ImGui.DragFloat("From", ref depthFrom, 0.1f, 0, borehole.TotalDepth, "%.2f"))
                _editingUnit.DepthFrom = depthFrom;

            var depthTo = _editingUnit.DepthTo;
            if (ImGui.DragFloat("To", ref depthTo, 0.1f, _editingUnit.DepthFrom, borehole.TotalDepth, "%.2f"))
                _editingUnit.DepthTo = depthTo;

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Color
            ImGui.Text("Color:");
            var color = _editingUnit.Color;
            if (ImGui.ColorEdit4("##editunitcolor", ref color, ImGuiColorEditFlags.NoAlpha))
                _editingUnit.Color = color;

            ImGui.Spacing();

            // Description
            var description = _editingUnit.Description;
            if (ImGui.InputTextMultiline("Description", ref description, 512, new Vector2(-1, 80)))
                _editingUnit.Description = description;

            ImGui.Spacing();
            ImGui.Separator();

            // Buttons
            if (ImGui.Button("Save", new Vector2(120, 0)))
            {
                _selectedUnit.Name = _editingUnit.Name;
                _selectedUnit.LithologyType = _editingUnit.LithologyType;
                _selectedUnit.GrainSize = _editingUnit.GrainSize;
                _selectedUnit.DepthFrom = _editingUnit.DepthFrom;
                _selectedUnit.DepthTo = _editingUnit.DepthTo;
                _selectedUnit.Color = _editingUnit.Color;
                _selectedUnit.Description = _editingUnit.Description;

                // Re-sort units by depth
                borehole.LithologyUnits.Sort((a, b) => a.DepthFrom.CompareTo(b.DepthFrom));

                _showEditUnitDialog = false;
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0))) _showEditUnitDialog = false;

            ImGui.EndPopup();
        }
    }

    private void DrawImportParametersDialog(BoreholeDataset borehole)
    {
        if (!_showImportParametersDialog)
            return;

        ImGui.OpenPopup("Import Parameters");

        ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
        if (ImGui.BeginPopupModal("Import Parameters", ref _showImportParametersDialog))
        {
            ImGui.Text($"Importing to: {_selectedUnit?.Name ?? "Unknown"}");
            ImGui.Separator();
            ImGui.Spacing();

            // Depth range
            ImGui.Text("Depth Range (m):");
            ImGui.DragFloat("From##import", ref _importDepthFrom, 0.1f, 0, borehole.TotalDepth, "%.2f");
            ImGui.DragFloat("To##import", ref _importDepthTo, 0.1f, _importDepthFrom, borehole.TotalDepth, "%.2f");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Dataset selection
            ImGui.Text("Source Dataset:");

            var availableDatasets = ProjectManager.Instance.LoadedDatasets
                .Where(d => d is CtImageStackDataset or PNMDataset or AcousticVolumeDataset)
                .ToList();

            var currentDatasetName = _selectedSourceDataset?.Name ?? "Select dataset...";

            if (ImGui.BeginCombo("##sourcedataset", currentDatasetName))
            {
                foreach (var ds in availableDatasets)
                {
                    var isSelected = _selectedSourceDataset == ds;

                    var label = $"{ds.Name} ({ds.Type})";
                    if (ImGui.Selectable(label, isSelected))
                    {
                        _selectedSourceDataset = ds;
                        UpdateAvailableParameters();
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImGui.Spacing();

            // Parameter selection
            if (_selectedSourceDataset != null && _availableParameters != null)
            {
                ImGui.Text("Available Parameters:");
                ImGui.BeginChild("ParamList", new Vector2(0, 200), ImGuiChildFlags.Border);

                for (var i = 0; i < _availableParameters.Length; i++)
                {
                    var selected = _selectedParameters[i];
                    if (ImGui.Checkbox(_availableParameters[i], ref selected)) _selectedParameters[i] = selected;
                }

                ImGui.EndChild();

                if (ImGui.Button("Select All"))
                    for (var i = 0; i < _selectedParameters.Length; i++)
                        _selectedParameters[i] = true;

                ImGui.SameLine();

                if (ImGui.Button("Deselect All"))
                    for (var i = 0; i < _selectedParameters.Length; i++)
                        _selectedParameters[i] = false;
            }
            else
            {
                ImGui.TextDisabled("Select a dataset to see available parameters");
            }

            ImGui.Spacing();
            ImGui.Separator();

            // Info about dataset
            if (_selectedSourceDataset != null)
            {
                ImGui.Text("Dataset Information:");

                if (_selectedSourceDataset is CtImageStackDataset ct)
                {
                    ImGui.BulletText($"Voxel Size: {ct.PixelSize} {ct.Unit}");
                    ImGui.BulletText($"Dimensions: {ct.Width}x{ct.Height}x{ct.Depth}");
                }
                else if (_selectedSourceDataset is PNMDataset pnm)
                {
                    ImGui.BulletText($"Voxel Size: {pnm.VoxelSize} µm");
                    ImGui.BulletText($"Pores: {pnm.Pores.Count}");
                }
                else if (_selectedSourceDataset is AcousticVolumeDataset acoustic)
                {
                    ImGui.BulletText($"Vp: {acoustic.PWaveVelocity:F1} m/s");
                    ImGui.BulletText($"Vs: {acoustic.SWaveVelocity:F1} m/s");
                }
            }

            ImGui.Spacing();
            ImGui.Separator();

            // Buttons
            var canImport = _selectedSourceDataset != null &&
                            _selectedParameters != null &&
                            _selectedParameters.Any(p => p);

            if (ImGui.Button("Import", new Vector2(120, 0)))
                if (canImport)
                {
                    var paramsToImport = _availableParameters
                        .Where((p, i) => _selectedParameters[i])
                        .ToArray();

                    borehole.ImportParametersFromDataset(
                        _selectedSourceDataset,
                        _importDepthFrom,
                        _importDepthTo,
                        paramsToImport);

                    Logger.Log($"Imported {paramsToImport.Length} parameters from {_selectedSourceDataset.Name}");

                    _showImportParametersDialog = false;
                }

            if (!canImport)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("Select dataset and parameters");
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(120, 0))) _showImportParametersDialog = false;

            ImGui.EndPopup();
        }
    }

    private void UpdateAvailableParameters()
    {
        if (_selectedSourceDataset == null)
        {
            _availableParameters = null;
            _selectedParameters = null;
            return;
        }

        var paramList = new List<string>();

        if (_selectedSourceDataset is CtImageStackDataset ct)
        {
            if (ct.ThermalResults != null)
                paramList.Add("Thermal Conductivity");

            if (ct.NmrResults != null)
                paramList.Add("Porosity");
        }
        else if (_selectedSourceDataset is PNMDataset pnm)
        {
            paramList.Add("Permeability");
            paramList.Add("Porosity");
            paramList.Add("Tortuosity");

            if (pnm.BulkDiffusivity > 0)
            {
                paramList.Add("Bulk Diffusivity");
                paramList.Add("Effective Diffusivity");
                paramList.Add("Formation Factor");
            }
        }
        else if (_selectedSourceDataset is AcousticVolumeDataset acoustic)
        {
            paramList.Add("P-Wave Velocity");
            paramList.Add("S-Wave Velocity");
            paramList.Add("Young's Modulus");
            paramList.Add("Poisson's Ratio");
        }

        _availableParameters = paramList.ToArray();
        _selectedParameters = new bool[_availableParameters.Length];

        // Auto-select all by default
        for (var i = 0; i < _selectedParameters.Length; i++)
            _selectedParameters[i] = true;
    }

    private Vector4 GetDefaultColorForLithology(string lithologyType)
    {
        return lithologyType switch
        {
            "Sandstone" => new Vector4(0.9f, 0.85f, 0.6f, 1.0f),
            "Shale" => new Vector4(0.4f, 0.4f, 0.4f, 1.0f),
            "Limestone" => new Vector4(0.8f, 0.8f, 0.9f, 1.0f),
            "Clay" => new Vector4(0.6f, 0.5f, 0.4f, 1.0f),
            "Siltstone" => new Vector4(0.7f, 0.65f, 0.5f, 1.0f),
            "Conglomerate" => new Vector4(0.75f, 0.7f, 0.65f, 1.0f),
            "Basement" => new Vector4(0.5f, 0.3f, 0.3f, 1.0f),
            "Coal" => new Vector4(0.2f, 0.2f, 0.2f, 1.0f),
            "Dolomite" => new Vector4(0.85f, 0.75f, 0.7f, 1.0f),
            "Mudstone" => new Vector4(0.5f, 0.45f, 0.4f, 1.0f),
            "Marl" => new Vector4(0.7f, 0.7f, 0.65f, 1.0f),
            "Chalk" => new Vector4(0.95f, 0.95f, 0.95f, 1.0f),
            "Granite" => new Vector4(0.7f, 0.6f, 0.5f, 1.0f),
            "Basalt" => new Vector4(0.3f, 0.3f, 0.35f, 1.0f),
            "Anhydrite" => new Vector4(0.9f, 0.9f, 0.95f, 1.0f),
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1.0f)
        };
    }
}