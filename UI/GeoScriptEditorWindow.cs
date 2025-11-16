// GeoscientistToolkit/UI/Windows/GeoScriptEditorWindow.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Windows;

/// <summary>
/// A window for editing and executing GeoScript scripts with syntax highlighting and autocompletion
/// </summary>
public class GeoScriptEditorWindow
{
    private bool _isOpen;
    private string _scriptContent = "# GeoScript Editor\n# Write your script here. Use |> to chain operations.\n\n# Example:\n# FILTER type=gaussian size=5 |> BRIGHTNESS_CONTRAST brightness=10 contrast=1.2\n";
    private Dataset _selectedDataset;
    private int _selectedDatasetIndex = -1;
    private readonly GeoScriptEngine _engine = new();
    private StringBuilder _output = new StringBuilder();
    private bool _isExecuting = false;
    private Vector2 _scriptEditorSize = new Vector2(0, 300);
    private int _cursorPosition = 0;
    private bool _showAutocomplete = false;
    private List<string> _autocompleteOptions = new();
    private int _autocompleteSelectedIndex = 0;
    private string _currentWord = "";

    // Syntax highlighting colors
    private static readonly Vector4 ColorKeyword = new Vector4(0.4f, 0.7f, 1.0f, 1.0f);
    private static readonly Vector4 ColorComment = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
    private static readonly Vector4 ColorString = new Vector4(0.9f, 0.7f, 0.4f, 1.0f);
    private static readonly Vector4 ColorNumber = new Vector4(0.7f, 0.9f, 0.7f, 1.0f);
    private static readonly Vector4 ColorOperator = new Vector4(0.9f, 0.5f, 0.5f, 1.0f);
    private static readonly Vector4 ColorParameter = new Vector4(0.8f, 0.6f, 0.9f, 1.0f);

    // All available GeoScript commands for autocompletion
    private static readonly string[] Commands = new[]
    {
        // Image commands
        "BRIGHTNESS_CONTRAST", "FILTER", "THRESHOLD", "BINARIZE", "GRAYSCALE", "INVERT", "NORMALIZE",

        // CT Image Stack commands
        "CT_SEGMENT", "CT_FILTER3D", "CT_ADD_MATERIAL", "CT_REMOVE_MATERIAL", "CT_ANALYZE_POROSITY",
        "CT_CROP", "CT_EXTRACT_SLICE", "CT_LABEL_ANALYSIS",

        // Borehole commands
        "BH_ADD_LITHOLOGY", "BH_REMOVE_LITHOLOGY", "BH_ADD_LOG", "BH_CALCULATE_POROSITY",
        "BH_CALCULATE_SATURATION", "BH_DEPTH_SHIFT", "BH_CORRELATION",

        // Table commands
        "SELECT", "CALCULATE", "SORTBY", "GROUPBY", "RENAME", "DROP", "TAKE", "UNIQUE", "JOIN",

        // GIS commands
        "BUFFER", "DISSOLVE", "EXPLODE", "CLEAN", "RECLASSIFY", "SLOPE", "ASPECT", "CONTOUR",
        "GIS_ADD_LAYER", "GIS_REMOVE_LAYER", "GIS_INTERSECT", "GIS_UNION", "GIS_CLIP",
        "GIS_CALCULATE_AREA", "GIS_CALCULATE_LENGTH", "GIS_REPROJECT",

        // PNM commands
        "PNM_FILTER_PORES", "PNM_FILTER_THROATS", "PNM_CALCULATE_PERMEABILITY",
        "PNM_DRAINAGE_SIMULATION", "PNM_IMBIBITION_SIMULATION", "PNM_EXTRACT_LARGEST_CLUSTER", "PNM_STATISTICS",
        "RUNPNMREACTIVETRANSPORT", "SETPNMSPECIES", "SETPNMMINERALS", "EXPORTPNMRESULTS",

        // Seismic commands
        "SEIS_FILTER", "SEIS_AGC", "SEIS_VELOCITY_ANALYSIS", "SEIS_NMO_CORRECTION",
        "SEIS_STACK", "SEIS_MIGRATION", "SEIS_PICK_HORIZON",

        // AcousticVolume commands
        "ACOUSTIC_THRESHOLD", "ACOUSTIC_EXTRACT_TARGETS",

        // Mesh3D commands
        "MESH_SMOOTH", "MESH_DECIMATE", "MESH_REPAIR", "MESH_CALCULATE_VOLUME",

        // Video commands
        "VIDEO_EXTRACT_FRAME", "VIDEO_STABILIZE",

        // Audio commands
        "AUDIO_TRIM", "AUDIO_NORMALIZE",

        // Text commands
        "TEXT_SEARCH", "TEXT_REPLACE", "TEXT_STATISTICS",

        // Thermodynamics commands
        "CREATEDIAGRAM", "EQUILIBRATE", "SATURATION", "BALANCEREACTION", "EVAPORATE", "REACT",
        "CALCULATEPHASES", "CALCULATECARBONATEALKALINITY",

        // Petrology commands
        "FRACTIONATEMAGMA", "LIQUIDUSSOLIDUS", "METAMORPHICPT",

        // Reactor commands
        "CREATEREACTOR", "ADDDOMAIN", "SETMINERALS", "RUNSIMULATION",

        // Utility commands
        "LISTOPS", "DISPTYPE", "UNLOAD", "INFO"
    };

    private static readonly string[] Parameters = new[]
    {
        // Image/CT parameters
        "brightness=", "contrast=", "type=", "size=", "min=", "max=", "threshold=", "sigma=",
        "method=", "material=", "void_material=", "axis=", "index=",

        // Borehole parameters
        "name=", "top=", "bottom=", "color=", "unit=", "depth=", "density_log=", "neutron_log=",
        "resistivity_log=", "porosity_log=", "a=", "m=", "n=", "offset=", "target=",

        // GIS parameters
        "layer=", "layer1=", "layer2=", "clip_layer=", "field=", "target_crs=",

        // PNM parameters
        "min_radius=", "max_radius=", "min_coord=", "max_length=", "direction=",
        "contact_angle=", "interfacial_tension=",

        // Seismic parameters
        "low=", "high=", "window=", "velocity=", "velocity_file=", "aperture=",

        // Media parameters
        "time=", "frame=", "smoothness=", "start=", "end=", "target_db=",

        // Mesh parameters
        "iterations=", "lambda=", "target_percent=",

        // Text parameters
        "pattern=", "case_sensitive=", "find=", "replace=",

        // General
        "WHERE", "value=", "radius=", "distance=", "tolerance="
    };

    public void Show()
    {
        _isOpen = true;

        // Auto-select first dataset if none selected
        if (_selectedDataset == null && ProjectManager.Instance.LoadedDatasets.Any())
        {
            _selectedDatasetIndex = 0;
            _selectedDataset = ProjectManager.Instance.LoadedDatasets[_selectedDatasetIndex];
        }
    }

    public void Draw()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("GeoScript Editor", ref _isOpen))
        {
            DrawDatasetSelector();
            ImGui.Separator();

            if (_selectedDataset == null)
            {
                DrawNoDatasetWarning();
            }
            else
            {
                DrawEditorControls();
                ImGui.Separator();
                DrawScriptEditor();
                ImGui.Separator();
                DrawOutputPanel();
            }
        }
        ImGui.End();
    }

    private void DrawDatasetSelector()
    {
        ImGui.Text("Context Dataset:");
        ImGui.SameLine();

        var datasets = ProjectManager.Instance.LoadedDatasets;
        if (!datasets.Any())
        {
            ImGui.Text("(No datasets loaded)");
            return;
        }

        ImGui.SetNextItemWidth(300);
        var datasetNames = datasets.Select(d => d.Name).ToArray();

        if (ImGui.Combo("##DatasetSelector", ref _selectedDatasetIndex, datasetNames, datasetNames.Length))
        {
            _selectedDataset = datasets[_selectedDatasetIndex];
        }

        if (_selectedDataset != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({_selectedDataset.Type})");
        }
    }

    private void DrawNoDatasetWarning()
    {
        var windowSize = ImGui.GetWindowSize();
        ImGui.SetCursorPosX((windowSize.X - 400) * 0.5f);
        ImGui.SetCursorPosY(windowSize.Y * 0.3f);

        ImGui.BeginChild("##Warning", new Vector2(400, 150), ImGuiChildFlags.Border);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.0f, 1.0f));
        ImGui.TextWrapped("⚠ No Dataset Selected");
        ImGui.PopStyleColor();
        ImGui.Spacing();
        ImGui.TextWrapped("Please load a dataset and select it from the dropdown above to begin scripting.");
        ImGui.EndChild();
    }

    private void DrawEditorControls()
    {
        // Run button
        if (_isExecuting)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Running...", new Vector2(100, 0));
            ImGui.EndDisabled();
        }
        else
        {
            if (ImGui.Button("▶ Run Script", new Vector2(100, 0)))
            {
                ExecuteScript();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Output", new Vector2(100, 0)))
        {
            _output.Clear();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Script", new Vector2(100, 0)))
        {
            _scriptContent = "";
        }

        ImGui.SameLine();
        if (ImGui.Button("Load Example", new Vector2(120, 0)))
        {
            LoadExampleScript();
        }

        ImGui.SameLine();
        if (ImGui.Button("List Operations", new Vector2(120, 0)))
        {
            ShowAvailableOperations();
        }

        // Quick insert buttons
        ImGui.Spacing();
        ImGui.Text("Quick Insert:");
        ImGui.SameLine();

        if (ImGui.SmallButton("|>"))
        {
            InsertTextAtCursor(" |> ");
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("brightness="))
        {
            InsertTextAtCursor("brightness=");
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("contrast="))
        {
            InsertTextAtCursor("contrast=");
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("type="))
        {
            InsertTextAtCursor("type=");
        }
    }

    private void DrawScriptEditor()
    {
        ImGui.Text("Script:");

        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var editorHeight = Math.Max(200, availableHeight * 0.5f);

        ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]); // Use monospace if available
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.1f, 0.1f, 0.15f, 1.0f));

        // Multi-line text input
        if (ImGui.InputTextMultiline("##ScriptEditor", ref _scriptContent, 10000,
            new Vector2(-1, editorHeight),
            ImGuiInputTextFlags.AllowTabInput))
        {
            // Text changed - could trigger autocomplete here
            UpdateAutocomplete();
        }

        ImGui.PopStyleColor();
        ImGui.PopFont();

        // Show autocomplete popup if active
        if (_showAutocomplete && _autocompleteOptions.Any())
        {
            DrawAutocompletePopup();
        }

        // Help text
        ImGui.TextDisabled("Tip: Use Ctrl+Enter to run script, Tab for autocomplete");
    }

    private void DrawOutputPanel()
    {
        ImGui.Text("Output:");

        var outputHeight = ImGui.GetContentRegionAvail().Y;

        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.05f, 0.05f, 0.05f, 1.0f));
        ImGui.BeginChild("##Output", new Vector2(-1, outputHeight), ImGuiChildFlags.Border);

        ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]); // Monospace
        ImGui.TextWrapped(_output.ToString());
        ImGui.PopFont();

        // Auto-scroll to bottom
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            ImGui.SetScrollHereY(1.0f);

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawAutocompletePopup()
    {
        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.SetNextWindowPos(cursorPos);

        if (ImGui.BeginPopup("##Autocomplete"))
        {
            for (int i = 0; i < _autocompleteOptions.Count; i++)
            {
                bool isSelected = i == _autocompleteSelectedIndex;
                if (ImGui.Selectable(_autocompleteOptions[i], isSelected))
                {
                    CompleteWord(_autocompleteOptions[i]);
                    _showAutocomplete = false;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndPopup();
        }
        else
        {
            _showAutocomplete = false;
        }
    }

    private void UpdateAutocomplete()
    {
        // Simple autocomplete: find word at cursor and show matching commands
        var lines = _scriptContent.Split('\n');
        // This is simplified - in a real implementation you'd track cursor position properly

        if (lines.Length > 0)
        {
            var lastLine = lines[lines.Length - 1];
            var words = lastLine.Split(new[] { ' ', '\t', '|', '>' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length > 0)
            {
                _currentWord = words[words.Length - 1];

                if (_currentWord.Length > 0 && !_currentWord.StartsWith("#"))
                {
                    _autocompleteOptions = Commands
                        .Where(cmd => cmd.StartsWith(_currentWord.ToUpper()))
                        .ToList();

                    if (!_autocompleteOptions.Any())
                    {
                        _autocompleteOptions = Parameters
                            .Where(param => param.StartsWith(_currentWord.ToLower()))
                            .ToList();
                    }

                    _showAutocomplete = _autocompleteOptions.Any();
                    _autocompleteSelectedIndex = 0;
                }
            }
        }
    }

    private void CompleteWord(string completion)
    {
        // Replace current word with completion
        var lines = _scriptContent.Split('\n').ToList();
        if (lines.Count > 0)
        {
            var lastLine = lines[lines.Count - 1];
            var lastWordStart = lastLine.LastIndexOf(_currentWord);
            if (lastWordStart >= 0)
            {
                lastLine = lastLine.Substring(0, lastWordStart) + completion + lastLine.Substring(lastWordStart + _currentWord.Length);
                lines[lines.Count - 1] = lastLine;
                _scriptContent = string.Join("\n", lines);
            }
        }
    }

    private void InsertTextAtCursor(string text)
    {
        _scriptContent += text;
    }

    private async void ExecuteScript()
    {
        if (string.IsNullOrWhiteSpace(_scriptContent) || _selectedDataset == null || _isExecuting)
            return;

        _isExecuting = true;
        _output.Clear();
        _output.AppendLine($"=== Executing GeoScript ===");
        _output.AppendLine($"Dataset: {_selectedDataset.Name} ({_selectedDataset.Type})");
        _output.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.AppendLine();

        try
        {
            // Split script into lines and execute each non-empty, non-comment line
            var lines = _scriptContent.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
                .ToList();

            Dataset currentDataset = _selectedDataset;
            int lineNumber = 1;

            foreach (var line in lines)
            {
                _output.AppendLine($"[Line {lineNumber}] {line}");

                try
                {
                    var contextDatasets = ProjectManager.Instance.LoadedDatasets
                        .ToDictionary(d => d.Name, d => d);

                    var result = await _engine.ExecuteAsync(line, currentDataset, contextDatasets);

                    if (result != null && result != currentDataset)
                    {
                        // New dataset was created, add it to the project
                        ProjectManager.Instance.AddDataset(result);
                        currentDataset = result;
                        _output.AppendLine($"  ✓ Created: {result.Name}");
                    }
                    else if (result != null)
                    {
                        _output.AppendLine($"  ✓ Success");
                    }
                }
                catch (Exception ex)
                {
                    _output.AppendLine($"  ✗ Error: {ex.Message}");
                    Logger.LogError($"GeoScript error on line {lineNumber}: {ex.Message}");
                }

                lineNumber++;
                _output.AppendLine();
            }

            _output.AppendLine($"=== Execution Complete ===");
            Logger.LogInfo("GeoScript execution completed");
        }
        catch (Exception ex)
        {
            _output.AppendLine($"Fatal Error: {ex.Message}");
            Logger.LogError($"GeoScript fatal error: {ex.Message}");
        }
        finally
        {
            _isExecuting = false;
        }
    }

    private void LoadExampleScript()
    {
        if (_selectedDataset?.Type == DatasetType.SingleImage || _selectedDataset?.Type == DatasetType.CtImageStack)
        {
            _scriptContent = @"# Image Processing Example
# This script applies multiple operations in sequence

# First, apply a Gaussian filter to reduce noise
FILTER type=gaussian size=5

# Then adjust brightness and contrast
|> BRIGHTNESS_CONTRAST brightness=10 contrast=1.2

# Convert to grayscale
|> GRAYSCALE

# Apply threshold segmentation
|> THRESHOLD min=100 max=200

# Display info about the final result
|> INFO
";
        }
        else if (_selectedDataset?.Type == DatasetType.Table)
        {
            _scriptContent = @"# Table Processing Example

# Select specific rows
SELECT WHERE 'Value' > 100

# Sort by a column
|> SORTBY 'Name' DESC

# Take top 10 results
|> TAKE 10

# Display info
|> INFO
";
        }
        else
        {
            _scriptContent = @"# GeoScript Example
# Replace with operations suitable for your dataset type

# List available operations for this dataset
LISTOPS

# Display dataset information
|> DISPTYPE
";
        }
    }

    private void ShowAvailableOperations()
    {
        if (_selectedDataset == null)
        {
            _output.AppendLine("No dataset selected.");
            return;
        }

        _output.Clear();
        _output.AppendLine($"Available operations for {_selectedDataset.Type}:");
        _output.AppendLine(new string('=', 60));

        var commands = CommandRegistry.GetAllCommands();
        foreach (var cmd in commands.OrderBy(c => c.Name))
        {
            _output.AppendLine($"{cmd.Name,-25} - {cmd.HelpText}");
            if (!string.IsNullOrEmpty(cmd.Usage))
                _output.AppendLine($"  Usage: {cmd.Usage}");
            _output.AppendLine();
        }
    }
}
