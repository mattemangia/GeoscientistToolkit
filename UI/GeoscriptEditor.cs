// GeoscientistToolkit/UI/GeoScriptEditor.cs

using System.Data;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

// Required for encoding

// Required for Marshal

namespace GeoscientistToolkit.UI;

/// <summary>
///     A reusable ImGui component for editing GeoScript with context-aware autocomplete.
/// </summary>
public class GeoScriptEditor
{
    private static readonly GeoScriptEngine _engine = new();
    private readonly List<Suggestion> _suggestions = new();
    private int _activeSuggestionIndex = -1;

    // Context for suggestions
    private Dataset _associatedDataset;
    private bool _isExecuting;

    // Execution state
    private string _lastError = "";
    private string _lastSuccessMessage = "";
    private bool _needsToSetFocus;
    private string _scriptText = "";
    private bool _showSuggestionsPopup;

    // Import/Export dialogs
    private readonly ImGuiFileDialog _importDialog;
    private readonly ImGuiExportFileDialog _exportDialog;

    public GeoScriptEditor()
    {
        _importDialog = new ImGuiFileDialog("##ImportGeoScript", FileDialogType.OpenFile, "Import GeoScript");
        _exportDialog = new ImGuiExportFileDialog("##ExportGeoScript", "Export GeoScript");
        _exportDialog.SetExtensions((".geoscript", "GeoScript File"));
    }

    /// <summary>
    ///     Sets the dataset this editor is currently working with, which is crucial for context-aware suggestions.
    /// </summary>
    public void SetAssociatedDataset(Dataset dataset)
    {
        _associatedDataset = dataset;
    }

    public void Draw()
    {
        if (_associatedDataset == null)
        {
            var style = ImGui.GetStyle();
            // Use a child window to create a contained, styled message area.
            // CORRECTED LINE: Replaced 'false' with 'ImGuiChildFlags.None'
            ImGui.BeginChild("##NoDatasetWarning", Vector2.Zero, ImGuiChildFlags.None,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMove);

            var windowSize = ImGui.GetWindowSize();
            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetWindowPos();

            // 1. Draw a red "title bar" rectangle at the top of the child window.
            var titleBarHeight = ImGui.GetTextLineHeight() + style.FramePadding.Y * 2;
            drawList.AddRectFilled(
                windowPos,
                new Vector2(windowPos.X + windowSize.X, windowPos.Y + titleBarHeight),
                ImGui.GetColorU32(new Vector4(0.7f, 0.2f, 0.2f, 1.0f))
            );

            // Draw a border around the whole thing to make it look more like a panel
            drawList.AddRect(
                windowPos,
                new Vector2(windowPos.X + windowSize.X, windowPos.Y + windowSize.Y),
                ImGui.GetColorU32(new Vector4(0.7f, 0.2f, 0.2f, 1.0f)),
                style.ChildRounding
            );

            // 2. Draw title text on the bar (centered)
            var title = "⚠ No Dataset Selected";
            var titleSize = ImGui.CalcTextSize(title);
            ImGui.SetCursorPos(new Vector2(
                (windowSize.X - titleSize.X) * 0.5f,
                (titleBarHeight - titleSize.Y) * 0.5f
            ));
            ImGui.Text(title);

            // 3. Draw the message body
            ImGui.SetCursorPos(new Vector2(style.WindowPadding.X, titleBarHeight + style.WindowPadding.Y));
            ImGui.BeginGroup();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.8f, 1.0f));
            ImGui.TextWrapped(
                "The GeoScript Terminal requires an active dataset to act upon. Please select a 'Context Dataset' from the dropdown list above to begin scripting.");
            ImGui.PopStyleColor();
            ImGui.EndGroup();

            ImGui.EndChild();
            return;
        }

        ImGui.Text($"GeoScript Editor (Context: {_associatedDataset.Name})");
        ImGui.Separator();
        unsafe
        {
            // --- SCRIPT INPUT TEXT BOX ---
            // We use a callback to intercept keyboard events for autocomplete control.
            var flags = ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.AllowTabInput;
            ImGui.InputTextMultiline("##GeoScriptInput", ref _scriptText, 16384, new Vector2(-1, 150), flags,
                HandleInputCallback);
        }

        // --- AUTOCOMPLETE POPUP ---
        if (_showSuggestionsPopup && _suggestions.Any()) DrawSuggestionsPopup();

        // --- ACTION BUTTONS ---
        ImGui.Spacing();
        if (_isExecuting)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Executing...");
            ImGui.EndDisabled();
        }
        else
        {
            if (ImGui.Button("Execute Script")) ExecuteScript();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _scriptText = "";
            _lastError = "";
            _lastSuccessMessage = "";
        }

        ImGui.SameLine();
        if (ImGui.Button("Import Script..."))
        {
            _importDialog.Open(null, new[] { ".geoscript" });
        }

        ImGui.SameLine();
        if (ImGui.Button("Export Script..."))
        {
            var defaultName = !string.IsNullOrEmpty(_associatedDataset?.Name)
                ? $"{_associatedDataset.Name}_script.geoscript"
                : "script.geoscript";
            _exportDialog.Open(defaultName);
        }

        // --- HANDLE IMPORT/EXPORT DIALOGS ---
        HandleImportExportDialogs();

        // --- RESULT DISPLAY ---
        ImGui.Spacing();
        if (!string.IsNullOrEmpty(_lastError))
        {
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "Error:");
            ImGui.TextWrapped(_lastError);
        }
        else if (!string.IsNullOrEmpty(_lastSuccessMessage))
        {
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Success:");
            ImGui.TextWrapped(_lastSuccessMessage);
        }
    }

    /// <summary>
    ///     Main callback for the text input, handling keyboard events for autocomplete.
    /// </summary>
    private unsafe int HandleInputCallback(ImGuiInputTextCallbackData* data)
    {
        // This is the core of the keyboard interaction for the suggestion popup
        if (!_showSuggestionsPopup) return 0;

        switch (data->EventKey)
        {
            case ImGuiKey.UpArrow:
                _activeSuggestionIndex = Math.Max(0, _activeSuggestionIndex - 1);
                break;
            case ImGuiKey.DownArrow:
                _activeSuggestionIndex = Math.Min(_suggestions.Count - 1, _activeSuggestionIndex + 1);
                break;
            case ImGuiKey.Enter:
            case ImGuiKey.Tab:
                if (_activeSuggestionIndex >= 0 && _activeSuggestionIndex < _suggestions.Count)
                {
                    // User has accepted a suggestion
                    InsertSuggestion(_suggestions[_activeSuggestionIndex], data);
                    _showSuggestionsPopup = false;
                }

                break;
            case ImGuiKey.Escape:
                _showSuggestionsPopup = false;
                break;
        }

        return 0;
    }

    /// <summary>
    ///     Inserts the selected suggestion into the text input, replacing the current partial word.
    ///     This method MUST be called from an unsafe context because it manipulates the native ImGui buffer directly.
    /// </summary>
    private unsafe void InsertSuggestion(Suggestion suggestion, ImGuiInputTextCallbackData* data)
    {
        // Find the start of the current word at the cursor position
        var wordStart = data->CursorPos;
        // Handles cases where the current word is at the start of the script
        while (wordStart > 0)
        {
            var prevChar = _scriptText[wordStart - 1];
            if (char.IsWhiteSpace(prevChar) || prevChar == '>' || prevChar == '(' || prevChar == ',') break;
            wordStart--;
        }

        // Find the end of the current word
        var wordEnd = data->CursorPos;
        while (wordEnd < _scriptText.Length && !char.IsWhiteSpace(_scriptText[wordEnd])) wordEnd++;

        // Build the new script text
        var textBefore = _scriptText.Substring(0, wordStart);
        var textAfter = _scriptText.Substring(wordEnd);

        // Add a space after the inserted text for better flow
        var finalInsertText = suggestion.InsertText + " ";
        var newText = textBefore + finalInsertText + textAfter;

        // --- CORRECTED SNIPPET ---
        // Manually update the buffer since the DeleteChars/InsertChars helpers may not exist in all ImGui.NET versions.
        var newTextBytes = Encoding.UTF8.GetBytes(newText);
        if (newTextBytes.Length + 1 < data->BufSize) // +1 for null terminator
        {
            // Clear the buffer
            for (var i = 0; i < data->BufTextLen; i++)
                data->Buf[i] = 0;

            // Copy new text into the buffer
            Marshal.Copy(newTextBytes, 0, (IntPtr)data->Buf, newTextBytes.Length);
            data->Buf[newTextBytes.Length] = 0; // Null terminate

            // Update buffer state
            data->BufTextLen = newTextBytes.Length;
            data->BufDirty = 1; // Use 1 for true
        }

        // Move the cursor to the end of the newly inserted text
        data->CursorPos = (textBefore + finalInsertText).Length;
    }

    /// <summary>
    ///     The main logic to determine what suggestions to show based on the text and cursor position.
    /// </summary>
    private unsafe void UpdateSuggestions(ImGuiInputTextCallbackData* data)
    {
        var cursorPosition = data->CursorPos;
        if (cursorPosition == 0)
        {
            _showSuggestionsPopup = false;
            return;
        }

        // Find the start of the current word
        var wordStart = cursorPosition;
        while (wordStart > 0 && !char.IsWhiteSpace(_scriptText[wordStart - 1])) wordStart--;
        var currentWord = _scriptText.Substring(wordStart, cursorPosition - wordStart);

        // Determine context: what was the word *before* the current one?
        var previousWord = GetPreviousWord(wordStart).ToUpper();

        _suggestions.Clear();

        // --- CONTEXT-AWARE SUGGESTION LOGIC ---

        if (IsAtStartOfCommand(wordStart)) // Suggest main commands
        {
            _suggestions.AddRange(CommandRegistry.GetAllCommands()
                .Where(c => c.Name.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase))
                .Select(c => new Suggestion
                {
                    DisplayText = c.Name, InsertText = c.Name, HelpText = c.HelpText, Type = SuggestionType.Command
                }));
        }
        else if (previousWord == "SORTBY" && !currentWord.StartsWith("'")) // Suggest sort order
        {
            _suggestions.Add(new Suggestion
                { DisplayText = "ASC", InsertText = "ASC", HelpText = "Sort in ascending order." });
            _suggestions.Add(new Suggestion
                { DisplayText = "DESC", InsertText = "DESC", HelpText = "Sort in descending order." });
        }
        else if (previousWord == "RENAME" || previousWord == "TO") // Suggest column names
        {
            SuggestColumnNames(currentWord);
        }
        else if (currentWord.StartsWith("'")) // Suggesting column/attribute names
        {
            SuggestColumnNames(currentWord.Trim('\''));
        }
        else if (currentWord.StartsWith("@'")) // Suggesting other dataset names
        {
            SuggestDatasetNames(currentWord.TrimStart('@', '\''));
        }

        _showSuggestionsPopup = _suggestions.Any();
        if (_showSuggestionsPopup) _activeSuggestionIndex = 0;
    }

    private bool IsAtStartOfCommand(int wordStartIndex)
    {
        if (wordStartIndex == 0) return true;
        // Check if the character before the word is a space following a pipe, or just the start
        var i = wordStartIndex - 1;
        while (i > 0 && char.IsWhiteSpace(_scriptText[i])) i--;
        return _scriptText[i] == '>';
    }

    private string GetPreviousWord(int currentWordStartIndex)
    {
        var end = currentWordStartIndex - 1;
        while (end > 0 && char.IsWhiteSpace(_scriptText[end])) end--;
        var start = end;
        while (start > 0 && !char.IsWhiteSpace(_scriptText[start - 1])) start--;
        return _scriptText.Substring(start, end - start + 1);
    }

    /// <summary>
    ///     Suggests column names from a TableDataset or attribute keys from a GISDataset.
    /// </summary>
    /// <param name="partialName">The partially typed name to filter suggestions.</param>
    private void SuggestColumnNames(string partialName)
    {
        if (_associatedDataset is TableDataset tableDs)
        {
            var dt = tableDs.GetDataTable();
            if (dt == null) return;

            foreach (DataColumn col in dt.Columns)
                if (col.ColumnName.StartsWith(partialName, StringComparison.OrdinalIgnoreCase))
                    _suggestions.Add(new Suggestion
                    {
                        DisplayText = col.ColumnName,
                        InsertText = $"'{col.ColumnName}'",
                        HelpText = $"Table Column (Type: {col.DataType.Name})",
                        Type = SuggestionType.ColumnName
                    });
        }
        else if (_associatedDataset is GISDataset gisDs)
        {
            // For GIS, aggregate all unique property keys from all vector layers
            var allKeys = gisDs.Layers
                .Where(l => l.Type == LayerType.Vector)
                .SelectMany(l => l.Features)
                .SelectMany(f => f.Properties.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k);

            foreach (var key in allKeys)
                if (key.StartsWith(partialName, StringComparison.OrdinalIgnoreCase))
                    _suggestions.Add(new Suggestion
                    {
                        DisplayText = key,
                        InsertText = $"'{key}'",
                        HelpText = "Feature Attribute",
                        Type = SuggestionType.ColumnName
                    });
        }
    }

    private void SuggestDatasetNames(string partialName)
    {
        var allDatasets = ProjectManager.Instance.LoadedDatasets;
        foreach (var ds in allDatasets)
            if (ds.Name.StartsWith(partialName, StringComparison.OrdinalIgnoreCase))
                _suggestions.Add(new Suggestion
                {
                    DisplayText = ds.Name,
                    InsertText = $"@'{ds.Name}'",
                    HelpText = $"Dataset (Type: {ds.Type})",
                    Type = SuggestionType.DatasetName
                });
    }

    /// <summary>
    ///     Draws the suggestion popup window below the text input.
    /// </summary>
    private void DrawSuggestionsPopup()
    {
        ImGui.SetNextWindowPos(ImGui.GetCursorScreenPos() + new Vector2(0, 5));
        ImGui.SetNextWindowSize(new Vector2(350, Math.Min(_suggestions.Count, 8) * 25));

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.ChildWindow;
        if (ImGui.Begin("##Suggestions", ref _showSuggestionsPopup, flags))
            for (var i = 0; i < _suggestions.Count; i++)
            {
                var suggestion = _suggestions[i];
                var isSelected = i == _activeSuggestionIndex;

                if (ImGui.Selectable(suggestion.DisplayText, isSelected))
                {
                    // This block is for mouse clicks. Keyboard is handled in the callback.
                    // We need to request the insertion to happen after the draw loop.
                    Logger.LogWarning("Mouse click on suggestions not yet implemented. Use Enter/Tab.");
                    _showSuggestionsPopup = false;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextColored(GetSuggestionColor(suggestion.Type), suggestion.DisplayText);
                        ImGui.Separator();
                        ImGui.TextWrapped(suggestion.HelpText);
                        ImGui.EndTooltip();
                    }
                }
            }

        ImGui.End();
    }

    private Vector4 GetSuggestionColor(SuggestionType type)
    {
        return type switch
        {
            SuggestionType.Command => new Vector4(0.8f, 0.6f, 1.0f, 1.0f),
            SuggestionType.Keyword => new Vector4(0.5f, 0.8f, 1.0f, 1.0f),
            SuggestionType.ColumnName => new Vector4(0.5f, 1.0f, 0.8f, 1.0f),
            SuggestionType.DatasetName => new Vector4(1.0f, 0.8f, 0.5f, 1.0f),
            _ => new Vector4(1, 1, 1, 1)
        };
    }

    /// <summary>
    ///     Handles the import and export file dialogs.
    /// </summary>
    private void HandleImportExportDialogs()
    {
        // Handle import dialog
        if (_importDialog.Submit())
        {
            if (!string.IsNullOrEmpty(_importDialog.SelectedPath))
            {
                try
                {
                    _scriptText = File.ReadAllText(_importDialog.SelectedPath);
                    _lastSuccessMessage = $"Script imported from {Path.GetFileName(_importDialog.SelectedPath)}";
                    _lastError = "";
                    Logger.Log($"[GeoScriptEditor] Imported script from {_importDialog.SelectedPath}");
                }
                catch (Exception ex)
                {
                    _lastError = $"Failed to import script: {ex.Message}";
                    _lastSuccessMessage = "";
                    Logger.LogError($"[GeoScriptEditor] Import failed: {ex.Message}");
                }
            }
        }

        // Handle export dialog
        if (_exportDialog.Submit())
        {
            if (!string.IsNullOrEmpty(_exportDialog.SelectedPath))
            {
                try
                {
                    File.WriteAllText(_exportDialog.SelectedPath, _scriptText);
                    _lastSuccessMessage = $"Script exported to {Path.GetFileName(_exportDialog.SelectedPath)}";
                    _lastError = "";
                    Logger.Log($"[GeoScriptEditor] Exported script to {_exportDialog.SelectedPath}");
                }
                catch (Exception ex)
                {
                    _lastError = $"Failed to export script: {ex.Message}";
                    _lastSuccessMessage = "";
                    Logger.LogError($"[GeoScriptEditor] Export failed: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    ///     Executes the script asynchronously and updates the UI with the result.
    /// </summary>
    private async void ExecuteScript()
    {
        _isExecuting = true;
        _lastError = "";
        _lastSuccessMessage = "";

        try
        {
            // Gather context: all loaded datasets for @'name' references
            var contextDatasets = ProjectManager.Instance.LoadedDatasets
                .ToDictionary(ds => ds.Name, ds => ds);

            var resultDataset = await _engine.ExecuteAsync(_scriptText, _associatedDataset, contextDatasets);

            if (resultDataset != _associatedDataset) // A new dataset was created
            {
                ProjectManager.Instance.AddDataset(resultDataset);
                _lastSuccessMessage =
                    $"Script executed successfully. New dataset '{resultDataset.Name}' created and added to the project.";
            }
            else
            {
                _lastSuccessMessage =
                    "Script executed, but it did not generate a new dataset (e.g., in-place operations).";
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
        }
        finally
        {
            _isExecuting = false;
        }
    }

    /// <summary>
    ///     Represents a single autocomplete suggestion.
    /// </summary>
    private struct Suggestion
    {
        public string DisplayText;
        public string InsertText;
        public string HelpText;
        public SuggestionType Type;
    }

    private enum SuggestionType
    {
        Command,
        Keyword,
        ColumnName,
        DatasetName,
        Other
    }
}