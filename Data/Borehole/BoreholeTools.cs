// GeoscientistToolkit/UI/Borehole/BoreholeTools.cs

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Analysis.Geothermal;
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

// Added for ImGuiExportFileDialog

namespace GeoscientistToolkit.UI.Borehole;

/// <summary>
///     Categorized tools for creating, editing, and analyzing borehole/well log data
/// </summary>
public class BoreholeTools : IDatasetTools
{
    private readonly Dictionary<ToolCategory, string> _categoryDescriptions;

    private readonly Dictionary<ToolCategory, string> _categoryNames;

    // Export dialogs
    private readonly ImGuiExportFileDialog _exportBinaryDialog;
    private readonly ImGuiExportFileDialog _exportCsvDialog;
    private readonly ImGuiExportFileDialog _exportLasDialog;

    private readonly GeothermalSimulationTools _geothermalTool;

    private readonly string[] _grainSizes =
        { "Clay", "Silt", "Very Fine", "Fine", "Medium", "Coarse", "Very Coarse", "Gravel" };

    private readonly string[] _lithologyTypes =
    {
        "Sandstone", "Shale", "Limestone", "Clay", "Siltstone", "Conglomerate", "Basement", "Coal", "Dolomite",
        "Mudstone", "Marl", "Chalk", "Granite", "Basalt", "Anhydrite"
    };

    private readonly string[] _contactTypes =
    {
        "Sharp", "Erosive", "Gradational", "Conformable", "Unconformity", "Faulted", "Intrusive", "Indistinct"
    };

    private readonly Dictionary<ToolCategory, List<ToolEntry>> _toolsByCategory;

    private string[] _availableParameters;
    private LithologyUnit _editingUnit;
    private float _importDepthFrom;
    private float _importDepthTo = 10f;
    private Vector4 _newColor = new(0.8f, 0.7f, 0.5f, 1.0f);
    private float _newDepthFrom;
    private float _newDepthTo = 10f;
    private string _newDescription = "";
    private string _newGrainSize = "Medium";
    private string _newLithologyType = "Sandstone";
    private string _newUnitName = "New Unit";
    private ContactType _newUpperContactType = ContactType.Sharp;
    private ContactType _newLowerContactType = ContactType.Sharp;
    private ToolCategory _selectedCategory = ToolCategory.Management;
    private bool[] _selectedParameters;
    private Dataset _selectedSourceDataset;
    private bool _showAddUnitDialog;
    private bool _showEditUnitDialog;
    private bool _showImportParametersDialog;

    public BoreholeTools()
    {
        // GeothermalSimulationTools now uses VeldridManager.GraphicsDevice directly
        _geothermalTool = new GeothermalSimulationTools();

        // Initialize export dialogs
        _exportBinaryDialog = new ImGuiExportFileDialog("exportBoreholeBinary", "Export to Binary (.bhb)");
        _exportBinaryDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption(".bhb", "Borehole Binary File"));

        _exportCsvDialog = new ImGuiExportFileDialog("exportBoreholeCsv", "Export to CSV");
        _exportCsvDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption(".csv", "Comma-Separated Values"));

        _exportLasDialog = new ImGuiExportFileDialog("exportBoreholeLas", "Export to LAS");
        _exportLasDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption(".las", "Log ASCII Standard"));

        _categoryNames = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Management, "Management" },
            { ToolCategory.Parameters, "Parameters" },
            { ToolCategory.Analysis, "Analysis" },
            { ToolCategory.Display, "Display" },
            { ToolCategory.Export, "Export" },
            { ToolCategory.Debug, "Debug" }
        };

        _categoryDescriptions = new Dictionary<ToolCategory, string>
        {
            { ToolCategory.Management, "Define well properties and lithological units." },
            { ToolCategory.Parameters, "Import log data and parameters from other datasets." },
            { ToolCategory.Analysis, "Run simulations and quantitative analysis." },
            { ToolCategory.Display, "Control track visibility, scaling, and appearance." },
            { ToolCategory.Export, "Save borehole data to various industry formats." },
            { ToolCategory.Debug, "Generate test data and perform data validation." }
        };

        _toolsByCategory = new Dictionary<ToolCategory, List<ToolEntry>>
        {
            {
                ToolCategory.Management,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Lithology Editor", Description = "Add, edit, and manage lithological units.",
                        DrawAction = DrawLithologyEditor
                    }
                }
            },
            {
                ToolCategory.Parameters,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Parameter Import", Description = "Import log parameters from other datasets.",
                        DrawAction = DrawParameterTools
                    }
                }
            },
            {
                ToolCategory.Analysis,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Geothermal Simulation",
                        Description = "Configure and run geothermal simulations on the borehole.",
                        DrawAction = ds => _geothermalTool.Draw(ds)
                    }
                }
            },
            {
                ToolCategory.Display,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Display Settings", Description = "Adjust track visibility, grid, legend, and scaling.",
                        DrawAction = DrawDisplayTools
                    }
                }
            },
            {
                ToolCategory.Export,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Data Export", Description = "Save the borehole data to .bhb, .csv, or .las files.",
                        DrawAction = DrawExportTools
                    }
                }
            },
            {
                ToolCategory.Debug,
                new List<ToolEntry>
                {
                    new()
                    {
                        Name = "Test Data Generator",
                        Description = "Generate realistic test borehole data for simulation testing.",
                        DrawAction = DrawDebugTools
                    }
                }
            }
        };
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not BoreholeDataset borehole)
            return;

        if (ImGui.CollapsingHeader("Well Information", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Well Name: {borehole.WellName}");
            ImGui.Text($"Total Depth: {borehole.TotalDepth:F2} m");
            ImGui.Text($"Units Defined: {borehole.LithologyUnits.Count}");
        }

        ImGui.Separator();

        DrawCategorizedToolsUI(borehole);

        DrawAddUnitDialog(borehole);
        DrawEditUnitDialog(borehole);
        DrawImportParametersDialog(borehole);

        // Handle export dialog submissions
        if (_exportBinaryDialog.Submit())
            ExportToBinary(borehole, _exportBinaryDialog.SelectedPath);
        if (_exportCsvDialog.Submit())
            ExportToCSV(borehole, _exportCsvDialog.SelectedPath);
        if (_exportLasDialog.Submit())
            ExportToLAS(borehole, _exportLasDialog.SelectedPath);
    }

    /// <summary>
    ///     Apre la dialog di editing per una specifica formazione litologica e porta automaticamente
    ///     l'utente alla categoria Management. Questo metodo puÃƒÂ² essere collegato al callback
    ///     OnLithologyClicked del BoreholeViewer per permettere l'editing diretto cliccando sulla
    ///     formazione nel viewer.
    /// </summary>
    /// <param name="unit">L'unitÃƒÂ  litologica da editare</param>
    public void EditUnit(LithologyUnit unit)
    {
        if (unit == null) return;

        _editingUnit = unit;
        _showEditUnitDialog = true;
        _selectedCategory = ToolCategory.Management;
    }

    private void DrawCategorizedToolsUI(BoreholeDataset borehole)
    {
        ImGui.Text("Category:");
        ImGui.SameLine();

        var currentCategoryName = _categoryNames[_selectedCategory];
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo("##CategorySelector", currentCategoryName))
        {
            foreach (var category in (ToolCategory[])Enum.GetValues(typeof(ToolCategory)))
            {
                if (ImGui.Selectable(_categoryNames[category], _selectedCategory == category))
                    _selectedCategory = category;

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_categoryDescriptions[category]);
            }

            ImGui.EndCombo();
        }

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), _categoryDescriptions[_selectedCategory]);
        ImGui.Separator();

        var tools = _toolsByCategory[_selectedCategory];
        if (ImGui.BeginTabBar($"Tools_{_selectedCategory}", ImGuiTabBarFlags.None))
        {
            foreach (var tool in tools)
                if (ImGui.BeginTabItem(tool.Name))
                {
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), tool.Description);
                    ImGui.Separator();
                    ImGui.Spacing();

                    ImGui.BeginChild($"ToolContent_{tool.Name}", Vector2.Zero, ImGuiChildFlags.None,
                        ImGuiWindowFlags.HorizontalScrollbar);
                    tool.DrawAction?.Invoke(borehole);
                    ImGui.EndChild();

                    ImGui.EndTabItem();
                }

            ImGui.EndTabBar();
        }
    }

    private void DrawLithologyEditor(Dataset dataset)
    {
        if (dataset is not BoreholeDataset borehole) return;

        if (ImGui.BeginTable("LithologyTable", 7,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX,
                new Vector2(0, 200)))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Lithology", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("From (m)", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("To (m)", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Upper Contact", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Lower Contact", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableHeadersRow();

            foreach (var unit in borehole.LithologyUnits.ToList())
            {
                ImGui.TableNextRow();
                ImGui.PushID(unit.ID);

                ImGui.TableNextColumn();
                ImGui.Text(unit.Name);

                ImGui.TableNextColumn();
                ImGui.Text(unit.LithologyType);

                ImGui.TableNextColumn();
                ImGui.Text($"{unit.DepthFrom:F2}");

                ImGui.TableNextColumn();
                ImGui.Text($"{unit.DepthTo:F2}");

                ImGui.TableNextColumn();
                ImGui.Text(unit.UpperContactType.ToString());

                ImGui.TableNextColumn();
                ImGui.Text(unit.LowerContactType.ToString());

                ImGui.TableNextColumn();
                if (ImGui.Button("Edit"))
                {
                    _editingUnit = unit;
                    _showEditUnitDialog = true;
                }

                ImGui.SameLine();
                if (ImGui.Button("Delete")) borehole.LithologyUnits.Remove(unit);

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (ImGui.Button("Add New Unit")) _showAddUnitDialog = true;
        ImGui.SameLine();
        if (ImGui.Button("Sort by Depth")) borehole.LithologyUnits.Sort((a, b) => a.DepthFrom.CompareTo(b.DepthFrom));
    }

    private void DrawParameterTools(Dataset dataset)
    {
        if (dataset is not BoreholeDataset borehole) return;

        ImGui.Text("Import Parameters from Other Datasets");
        ImGui.Separator();

        ImGui.Text("Select Source Dataset:");
        var availableDatasets = ProjectManager.Instance.LoadedDatasets
            .Where(d => d != dataset && (d is CtImageStackDataset || d is PNMDataset || d is AcousticVolumeDataset))
            .ToList();

        if (availableDatasets.Any())
        {
            if (ImGui.BeginCombo("##SourceDataset", _selectedSourceDataset?.Name ?? "Select dataset..."))
            {
                foreach (var d in availableDatasets)
                    if (ImGui.Selectable(d.Name ?? "Unnamed", _selectedSourceDataset == d))
                        _selectedSourceDataset = d;
                ImGui.EndCombo();
            }

            if (_selectedSourceDataset != null)
            {
                ImGui.Text($"Type: {_selectedSourceDataset.Type}");
                ImGui.DragFloatRange2("Depth Range", ref _importDepthFrom, ref _importDepthTo, 1.0f, 0,
                    borehole.TotalDepth, "%.1f m");

                if (ImGui.Button("Select Parameters to Import"))
                    _showImportParametersDialog = true;
            }
        }
        else
        {
            ImGui.TextDisabled("No compatible source datasets available in project.");
        }

        ImGui.Separator();
        ImGui.Text($"Current Parameter Tracks: {borehole.ParameterTracks.Count}");
        foreach (var track in borehole.ParameterTracks.Values)
            ImGui.BulletText($"{track.Name} ({track.Unit}): {track.Points.Count} points");
    }

    private void DrawDisplayTools(Dataset dataset)
    {
        if (dataset is not BoreholeDataset borehole) return;

        var showGrid = borehole.ShowGrid;
        if (ImGui.Checkbox("Show Grid", ref showGrid)) borehole.ShowGrid = showGrid;

        var showLegend = borehole.ShowLegend;
        if (ImGui.Checkbox("Show Legend", ref showLegend)) borehole.ShowLegend = showLegend;

        var trackWidth = borehole.TrackWidth;
        if (ImGui.SliderFloat("Track Width", ref trackWidth, 50, 300, "%.0f px")) borehole.TrackWidth = trackWidth;

        var depthScale = borehole.DepthScaleFactor;
        if (ImGui.SliderFloat("Depth Scale", ref depthScale, 0.5f, 5.0f, "%.2fx"))
            borehole.DepthScaleFactor = depthScale;

        ImGui.Separator();
        ImGui.Text("Parameter Track Visibility:");

        foreach (var track in borehole.ParameterTracks.Values.ToList())
        {
            var visible = track.IsVisible;
            if (ImGui.Checkbox($"{track.Name}##vis_{track.Name}", ref visible))
                track.IsVisible = visible;

            ImGui.SameLine();
            var color = track.Color;
            if (ImGui.ColorEdit4($"##color_{track.Name}", ref color,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                track.Color = color;

            if (track.IsLogarithmic)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(log)");
            }
        }
    }

    private void DrawExportTools(Dataset dataset)
    {
        if (dataset is not BoreholeDataset borehole) return;

        var defaultName = Path.GetFileNameWithoutExtension(borehole.FilePath ?? borehole.Name);
        var defaultPath = Path.GetDirectoryName(borehole.FilePath);

        if (ImGui.Button("Export to Binary (.bhb)", new Vector2(-1, 0)))
            _exportBinaryDialog.Open(defaultName, defaultPath);

        ImGui.TextWrapped(
            "Custom binary format for quick loading within the toolkit. Includes all lithology, parameters, and display settings.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Export to CSV", new Vector2(-1, 0)))
            _exportCsvDialog.Open(defaultName, defaultPath);

        ImGui.TextWrapped(
            "Exports interpolated parameter track data to a comma-separated values file, suitable for spreadsheets.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Export to LAS", new Vector2(-1, 0)))
            _exportLasDialog.Open(defaultName, defaultPath);

        ImGui.TextWrapped(
            "Exports parameter track data to Log ASCII Standard format, compatible with well logging software.");
    }

    private void DrawDebugTools(Dataset dataset)
    {
        if (dataset is not BoreholeDataset borehole) return;

        BoreholeDebugTools.DrawDebugTools(borehole);
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Debug Information"))
        {
            ImGui.Text($"Memory Usage (Approx): {borehole.GetSizeInBytes() / 1024.0:F2} KB");
            ImGui.Text($"Total Parameters in Units: {borehole.LithologyUnits.Sum(u => u.Parameters.Count)}");
            if (borehole.ParameterTracks.Any())
                ImGui.Text($"Total Points in Tracks: {borehole.ParameterTracks.Values.Sum(t => t.Points.Count)}");

            if (ImGui.Button("Validate Data Integrity"))
                ValidateBoreholeData(borehole);

            if (ImGui.Button("Generate Console Report"))
                GenerateTestReport(borehole);
        }
    }

    private void ValidateBoreholeData(BoreholeDataset borehole)
    {
        var issues = new List<string>();
        var sortedUnits = borehole.LithologyUnits.OrderBy(u => u.DepthFrom).ToList();
        for (var i = 0; i < sortedUnits.Count - 1; i++)
        {
            var unit1 = sortedUnits[i];
            var unit2 = sortedUnits[i + 1];
            if (unit1.DepthTo > unit2.DepthFrom)
                issues.Add($"Overlapping units: {unit1.Name} ({unit1.DepthTo}m) and {unit2.Name} ({unit2.DepthFrom}m)");
            if (Math.Abs(unit1.DepthTo - unit2.DepthFrom) > 0.01f)
                issues.Add($"Gap between units: {unit1.Name} ({unit1.DepthTo}m) and {unit2.Name} ({unit2.DepthFrom}m)");
        }

        foreach (var unit in borehole.LithologyUnits)
        foreach (var param in unit.Parameters)
            if (float.IsNaN(param.Value) || float.IsInfinity(param.Value))
                issues.Add($"Invalid parameter value in {unit.Name}: {param.Key} = {param.Value}");

        Logger.Log(issues.Any()
            ? $"Found {issues.Count} issues:\n{string.Join("\n", issues.Take(10))}"
            : "Borehole data validated successfully!");
    }

    private void GenerateTestReport(BoreholeDataset borehole)
    {
        var report = new StringBuilder();
        report.AppendLine($"Test Report for {borehole.WellName} generated on {DateTime.Now}");
        report.AppendLine(
            $"Total Depth: {borehole.TotalDepth} m, Units: {borehole.LithologyUnits.Count}, Tracks: {borehole.ParameterTracks.Count}");
        foreach (var unit in borehole.LithologyUnits.OrderBy(u => u.DepthFrom))
        {
            report.AppendLine($"{unit.Name} ({unit.LithologyType}): {unit.DepthFrom:F1}-{unit.DepthTo:F1}m");
            foreach (var param in unit.Parameters.Take(5))
                report.AppendLine($"  {param.Key}: {param.Value:F3}");
        }

        var reportPath = Path.Combine(Path.GetDirectoryName(borehole.FilePath) ?? Environment.CurrentDirectory,
            $"{borehole.WellName}_TestReport.txt");
        File.WriteAllText(reportPath, report.ToString());
        Logger.Log($"Test report saved to {reportPath}");
    }

    private void DrawAddUnitDialog(BoreholeDataset borehole)
    {
        if (!_showAddUnitDialog) return;
        ImGui.OpenPopup("Add Lithology Unit");
        var isOpen = true;
        if (ImGui.BeginPopupModal("Add Lithology Unit", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("Name", ref _newUnitName, 256);
            if (ImGui.BeginCombo("Lithology Type", _newLithologyType))
            {
                foreach (var type in _lithologyTypes)
                    if (ImGui.Selectable(type, _newLithologyType == type))
                        _newLithologyType = type;
                ImGui.EndCombo();
            }

            ImGui.DragFloatRange2("Depth Range", ref _newDepthFrom, ref _newDepthTo, 1.0f, 0, borehole.TotalDepth,
                "%.2f m");
            if (ImGui.BeginCombo("Grain Size", _newGrainSize))
            {
                foreach (var size in _grainSizes)
                    if (ImGui.Selectable(size, _newGrainSize == size))
                        _newGrainSize = size;
                ImGui.EndCombo();
            }

            ImGui.ColorEdit4("Color", ref _newColor);
            
            if (ImGui.BeginCombo("Upper Contact", _newUpperContactType.ToString()))
            {
                foreach (var type in _contactTypes)
                    if (ImGui.Selectable(type, _newUpperContactType.ToString() == type))
                        _newUpperContactType = Enum.Parse<ContactType>(type);
                ImGui.EndCombo();
            }
            
            if (ImGui.BeginCombo("Lower Contact", _newLowerContactType.ToString()))
            {
                foreach (var type in _contactTypes)
                    if (ImGui.Selectable(type, _newLowerContactType.ToString() == type))
                        _newLowerContactType = Enum.Parse<ContactType>(type);
                ImGui.EndCombo();
            }
            
            ImGui.InputTextMultiline("Description", ref _newDescription, 1024, new Vector2(300, 100));
            ImGui.Separator();
            if (ImGui.Button("Add", new Vector2(120, 0)))
            {
                borehole.AddLithologyUnit(new LithologyUnit
                {
                    Name = _newUnitName, 
                    LithologyType = _newLithologyType, 
                    DepthFrom = _newDepthFrom,
                    DepthTo = _newDepthTo, 
                    GrainSize = _newGrainSize, 
                    Color = _newColor, 
                    Description = _newDescription,
                    UpperContactType = _newUpperContactType,
                    LowerContactType = _newLowerContactType
                });
                _showAddUnitDialog = false;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) _showAddUnitDialog = false;
            ImGui.EndPopup();
        }

        if (!isOpen) _showAddUnitDialog = false;
    }

    private void DrawEditUnitDialog(BoreholeDataset borehole)
    {
        if (!_showEditUnitDialog || _editingUnit == null) return;
        ImGui.OpenPopup("Edit Lithology Unit");
        var isOpen = true;
        if (ImGui.BeginPopupModal("Edit Lithology Unit", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var name = _editingUnit.Name;
            if (ImGui.InputText("Name", ref name, 256)) _editingUnit.Name = name;
            if (ImGui.BeginCombo("Lithology Type", _editingUnit.LithologyType))
            {
                foreach (var type in _lithologyTypes)
                    if (ImGui.Selectable(type, _editingUnit.LithologyType == type))
                        _editingUnit.LithologyType = type;
                ImGui.EndCombo();
            }

            var depthFrom = _editingUnit.DepthFrom;
            var depthTo = _editingUnit.DepthTo;
            if (ImGui.DragFloatRange2("Depth Range", ref depthFrom, ref depthTo, 1.0f, 0, borehole.TotalDepth,
                    "%.2f m"))
            {
                _editingUnit.DepthFrom = depthFrom;
                _editingUnit.DepthTo = depthTo;
            }

            if (ImGui.BeginCombo("Grain Size", _editingUnit.GrainSize))
            {
                foreach (var size in _grainSizes)
                    if (ImGui.Selectable(size, _editingUnit.GrainSize == size))
                        _editingUnit.GrainSize = size;
                ImGui.EndCombo();
            }

            var color = _editingUnit.Color;
            if (ImGui.ColorEdit4("Color", ref color)) _editingUnit.Color = color;
            
            var upperContact = _editingUnit.UpperContactType.ToString();
            if (ImGui.BeginCombo("Upper Contact", upperContact))
            {
                foreach (var type in _contactTypes)
                    if (ImGui.Selectable(type, upperContact == type))
                        _editingUnit.UpperContactType = Enum.Parse<ContactType>(type);
                ImGui.EndCombo();
            }
            
            var lowerContact = _editingUnit.LowerContactType.ToString();
            if (ImGui.BeginCombo("Lower Contact", lowerContact))
            {
                foreach (var type in _contactTypes)
                    if (ImGui.Selectable(type, lowerContact == type))
                        _editingUnit.LowerContactType = Enum.Parse<ContactType>(type);
                ImGui.EndCombo();
            }
            
            var description = _editingUnit.Description;
            if (ImGui.InputTextMultiline("Description", ref description, 1024, new Vector2(300, 100)))
                _editingUnit.Description = description;
            if (ImGui.CollapsingHeader("Parameters"))
                foreach (var param in _editingUnit.Parameters.ToList())
                {
                    var value = param.Value;
                    if (ImGui.DragFloat($"{param.Key}", ref value, 0.01f)) _editingUnit.Parameters[param.Key] = value;
                }

            ImGui.Separator();
            if (ImGui.Button("Save", new Vector2(120, 0))) _showEditUnitDialog = false;
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                _showEditUnitDialog = false;
                _editingUnit = null;
            }

            ImGui.EndPopup();
        }

        if (!isOpen)
        {
            _showEditUnitDialog = false;
            _editingUnit = null;
        }
    }

    private void DrawImportParametersDialog(BoreholeDataset borehole)
    {
        if (!_showImportParametersDialog || _selectedSourceDataset == null) return;
        ImGui.OpenPopup("Import Parameters");
        var isOpen = true;
        if (ImGui.BeginPopupModal("Import Parameters", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Importing from: {_selectedSourceDataset.Name}");
            ImGui.Text($"Depth range: {_importDepthFrom:F1} - {_importDepthTo:F1} m");
            ImGui.Separator();

            _availableParameters = GetAvailableParameters(_selectedSourceDataset);
            if (_selectedParameters == null || _selectedParameters.Length != _availableParameters.Length)
                _selectedParameters = new bool[_availableParameters.Length];

            ImGui.Text("Select parameters to import:");
            if (_availableParameters.Length > 0)
                for (var i = 0; i < _availableParameters.Length; i++)
                    ImGui.Checkbox(_availableParameters[i], ref _selectedParameters[i]);
            else
                ImGui.TextDisabled("No importable parameters found for this dataset type.");

            ImGui.Separator();

            var canImport = _availableParameters.Length > 0 && _selectedParameters.Any(p => p);
            if (!canImport) ImGui.BeginDisabled();
            if (ImGui.Button("Import", new Vector2(120, 0)))
            {
                ImportParameters(borehole, _selectedSourceDataset, _importDepthFrom, _importDepthTo,
                    _selectedParameters);
                _showImportParametersDialog = false;
            }

            if (!canImport) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0))) _showImportParametersDialog = false;
            ImGui.EndPopup();
        }

        if (!isOpen) _showImportParametersDialog = false;
    }

    private string[] GetAvailableParameters(Dataset dataset)
    {
        // This logic correctly identifies parameters that BoreholeDataset knows how to import.
        return dataset switch
        {
            CtImageStackDataset => new[] { "Thermal Conductivity", "Porosity" },
            PNMDataset => new[] { "Permeability", "Porosity", "Tortuosity" },
            AcousticVolumeDataset => new[]
                { "P-Wave Velocity", "S-Wave Velocity", "Young's Modulus", "Poisson's Ratio" },
            _ => Array.Empty<string>()
        };
    }

    private void ImportParameters(BoreholeDataset borehole, Dataset source, float depthFrom, float depthTo,
        bool[] selectedParams)
    {
        // Get the list of all possible parameters for the source dataset type
        var allPossibleParams = GetAvailableParameters(source);

        // Build a list of only the parameter names the user has checked
        var selectedParamNames = new List<string>();
        for (var i = 0; i < selectedParams.Length; i++)
            if (selectedParams[i])
                selectedParamNames.Add(allPossibleParams[i]);

        // Call the correct method on the BoreholeDataset itself
        if (selectedParamNames.Any())
        {
            borehole.ImportParametersFromDataset(source, depthFrom, depthTo, selectedParamNames.ToArray());
            Logger.Log($"Successfully imported {selectedParamNames.Count} parameter(s) from '{source.Name}'.");
        }
    }

    private void ExportToBinary(BoreholeDataset borehole, string path)
    {
        try
        {
            // Use the method on the dataset itself for consistency
            borehole.SaveToBinaryFile(path);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export to binary file: {ex.Message}");
        }
    }

    private void ExportToCSV(BoreholeDataset borehole, string path)
    {
        try
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine($"# Well Name: {borehole.WellName}");
            writer.WriteLine($"# Field: {borehole.Field}");
            writer.WriteLine($"# Total Depth: {borehole.TotalDepth}");

            var tracks = borehole.ParameterTracks.Values.Where(t => t.Points.Any()).ToList();
            if (!tracks.Any())
            {
                writer.WriteLine("# No parameter data to export.");
                return;
            }

            var headers = "Depth (m)," + string.Join(",",
                tracks.Select(t => $"{t.Name.Replace(',', ' ')} ({t.Unit.Replace(',', ' ')})"));
            writer.WriteLine(headers);

            var allDepths = tracks.SelectMany(t => t.Points.Select(p => p.Depth)).Distinct().OrderBy(d => d).ToList();
            var step = 1.0f; // Interpolate every 1 meter, for example
            var startDepth = allDepths.Min();
            var endDepth = allDepths.Max();

            for (var depth = startDepth; depth <= endDepth; depth += step)
            {
                var values = new List<string> { depth.ToString("F4") };
                foreach (var track in tracks)
                {
                    var interpolatedValue = borehole.GetParameterValueAtDepth(track.Name, depth);
                    values.Add(interpolatedValue.HasValue ? interpolatedValue.Value.ToString("F4") : "");
                }

                writer.WriteLine(string.Join(",", values));
            }

            Logger.Log($"Exported borehole data to CSV: {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export to CSV: {ex.Message}");
        }
    }

    private void ExportToLAS(BoreholeDataset borehole, string path)
    {
        try
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine("~VERSION INFORMATION");
            writer.WriteLine(" VERS.                2.0 : CWLS LOG ASCII STANDARD - VERSION 2.0");
            writer.WriteLine(" WRAP.                 NO : ONE LINE PER DEPTH STEP");
            writer.WriteLine("~WELL INFORMATION");
            var tracks = borehole.ParameterTracks.Values.Where(t => t.Points.Any()).ToList();
            var startDepth = tracks.Any() ? tracks.SelectMany(t => t.Points).Min(p => (float?)p.Depth) ?? 0.0f : 0.0f;
            writer.WriteLine($" STRT.M {startDepth:F4} : START DEPTH");
            writer.WriteLine($" STOP.M {borehole.TotalDepth:F4} : STOP DEPTH");
            writer.WriteLine(" STEP.M              -999.25 : STEP (VARIABLE)");
            writer.WriteLine(" NULL.              -999.25 : NULL VALUE");
            writer.WriteLine($" WELL.   {borehole.WellName,-20} : WELL NAME");
            writer.WriteLine($" FLD.    {borehole.Field,-20} : FIELD NAME");
            writer.WriteLine("~CURVE INFORMATION");
            foreach (var track in tracks)
            {
                var mnemonic = new string(track.Name.Replace(" ", "_").Take(8).ToArray()).ToUpper();
                writer.WriteLine($" {mnemonic,-8}.{track.Unit,-15}       : {track.Name}");
            }

            writer.WriteLine("~PARAMETER INFORMATION");
            writer.WriteLine("~A  DEPTH" + string.Concat(tracks.Select(t =>
                $" {new string(t.Name.Replace(" ", "_").Take(8).ToArray()).ToUpper(),-15}")));

            var allDepths = tracks.SelectMany(t => t.Points.Select(p => p.Depth)).Distinct().OrderBy(d => d).ToList();

            foreach (var depth in allDepths)
            {
                var line = new StringBuilder();
                line.Append($"{depth,-16:F4}");
                foreach (var track in tracks)
                {
                    var val = borehole.GetParameterValueAtDepth(track.Name, depth);
                    line.Append(val.HasValue ? $"{val.Value,-16:F4}" : $"{"-999.25",-16}");
                }

                writer.WriteLine(line.ToString());
            }

            Logger.Log($"Exported borehole data to LAS: {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export to LAS: {ex.Message}");
        }
    }

    private enum ToolCategory
    {
        Management,
        Parameters,
        Analysis,
        Display,
        Export,
        Debug
    }

    private class ToolEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Action<Dataset> DrawAction { get; set; }
    }
}