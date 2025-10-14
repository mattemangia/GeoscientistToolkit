// GeoscientistToolkit/UI/GeoScriptInterpreter.cs

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

/// <summary>
///     Provides a REPL (Read-Eval-Print-Loop) interface for executing GeoScript commands.
/// </summary>
public class GeoScriptInterpreter
{
    private static readonly GeoScriptEngine _engine = new();
    private readonly List<string> _commandHistory = new();
    private readonly StringBuilder _outputLog = new();
    private readonly List<string> _suggestions = new();

    private Dataset _associatedDataset;
    private int _historyIndex = -1;
    private bool _isExecuting;
    private string _currentInput = "";
    private bool _needsToSetFocus;
    private bool _scrollToBottom;

    public GeoScriptInterpreter()
    {
        _outputLog.AppendLine("Welcome to the GeoScript Terminal. Type 'HELP' for available commands.");
    }

    public void SetAssociatedDataset(Dataset dataset)
    {
        _associatedDataset = dataset;
    }

    public void Draw()
    {
        // --- OUTPUT LOG ---
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5.0f);
        var outputRegionHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() - 5;
        ImGui.BeginChild("##TerminalOutput", new Vector2(-1, outputRegionHeight), ImGuiChildFlags.Border,
            ImGuiWindowFlags.None);
        ImGui.TextUnformatted(_outputLog.ToString());
        if (_scrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            _scrollToBottom = false;
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();

        // --- INPUT LINE ---
        ImGui.PushItemWidth(-1);
        var flags = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackCompletion |
                    ImGuiInputTextFlags.CallbackHistory;
        unsafe
        {
            if (ImGui.InputText("##TerminalInput", ref _currentInput, 2048, flags, HandleInputCallback))
            {
                if (!string.IsNullOrWhiteSpace(_currentInput) && !_isExecuting)
                {
                    ExecuteCommand(_currentInput);
                    _currentInput = "";
                }

                _needsToSetFocus = true;
            }
        }

        if (_needsToSetFocus)
        {
            ImGui.SetKeyboardFocusHere(-1);
            _needsToSetFocus = false;
        }

        ImGui.PopItemWidth();
    }

    private unsafe int HandleInputCallback(ImGuiInputTextCallbackData* data)
    {
        switch (data->EventFlag)
        {
            case ImGuiInputTextFlags.CallbackHistory:
            {
                var prevHistoryIndex = _historyIndex;
                if (data->EventKey == ImGuiKey.UpArrow)
                {
                    if (_historyIndex == -1)
                        _historyIndex = _commandHistory.Count - 1;
                    else if (_historyIndex > 0)
                        _historyIndex--;
                }
                else if (data->EventKey == ImGuiKey.DownArrow)
                {
                    if (_historyIndex != -1)
                        if (++_historyIndex >= _commandHistory.Count)
                            _historyIndex = -1;
                }

                if (prevHistoryIndex != _historyIndex)
                {
                    var historyStr = _historyIndex >= 0 ? _commandHistory[_historyIndex] : "";
                    UpdateBuffer(data, historyStr);
                }

                break;
            }
            case ImGuiInputTextFlags.CallbackCompletion:
            {
                UpdateSuggestions(data);
                if (_suggestions.Count == 1)
                {
                    // Single suggestion, complete it directly
                    InsertSuggestion(_suggestions[0], data);
                }
                else if (_suggestions.Count > 1)
                {
                    // Multiple suggestions, list them in the output
                    LogToOutput($"Suggestions: {string.Join("  ", _suggestions)}");
                }

                break;
            }
        }

        return 0;
    }

    private unsafe void UpdateBuffer(ImGuiInputTextCallbackData* data, string text)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        if (textBytes.Length + 1 < data->BufSize)
        {
            Marshal.Copy(textBytes, 0, (IntPtr)data->Buf, textBytes.Length);
            data->Buf[textBytes.Length] = 0; // Null terminate
            data->BufTextLen = textBytes.Length;
            data->CursorPos = textBytes.Length;
            data->BufDirty = 1;
        }
    }

    private unsafe void InsertSuggestion(string suggestion, ImGuiInputTextCallbackData* data)
    {
        var text = Marshal.PtrToStringUTF8((IntPtr)data->Buf, data->BufTextLen) ?? "";
        var wordStart = data->CursorPos;
        while (wordStart > 0 && !char.IsWhiteSpace(text[wordStart - 1])) wordStart--;
        
        var textBefore = text.Substring(0, wordStart);
        var newText = textBefore + suggestion + " ";
        
        UpdateBuffer(data, newText);
    }
    
    private unsafe void UpdateSuggestions(ImGuiInputTextCallbackData* data)
    {
        var text = Marshal.PtrToStringUTF8((IntPtr)data->Buf, data->BufTextLen) ?? "";
        var wordStart = data->CursorPos;
        while (wordStart > 0 && !char.IsWhiteSpace(text[wordStart - 1])) wordStart--;
        var currentWord = text.Substring(wordStart, data->CursorPos - wordStart);

        _suggestions.Clear();
        if (string.IsNullOrWhiteSpace(currentWord)) return;
        
        _suggestions.AddRange(CommandRegistry.GetAllCommands()
            .Where(c => c.Name.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name));
    }

    private void LogToOutput(string message)
    {
        _outputLog.AppendLine(message);
        _scrollToBottom = true;
    }

    private async void ExecuteCommand(string command)
    {
        _isExecuting = true;
        LogToOutput($"> {command}");

        if (_commandHistory.LastOrDefault() != command)
        {
            _commandHistory.Add(command);
        }
        _historyIndex = -1;

        try
        {
            if (command.Trim().Equals("HELP", StringComparison.OrdinalIgnoreCase))
            {
                DisplayHelp();
            }
            else if (command.Trim().Equals("CLEAR", StringComparison.OrdinalIgnoreCase))
            {
                 _outputLog.Clear();
                _scrollToBottom = true;
            }
            else
            {
                var contextDatasets = ProjectManager.Instance.LoadedDatasets.ToDictionary(ds => ds.Name, ds => ds);
                var resultDataset = await _engine.ExecuteAsync(command, _associatedDataset, contextDatasets);

                if (resultDataset != _associatedDataset)
                {
                    ProjectManager.Instance.AddDataset(resultDataset);
                    LogToOutput(
                        $"✔ Success: New dataset '{resultDataset.Name}' created and added to the project.");
                }
                else
                {
                    LogToOutput(
                        $"✔ Success: In-place operation completed on '{_associatedDataset.Name}'.");
                }
            }
        }
        catch (Exception ex)
        {
            LogToOutput($"✖ Error: {ex.Message}");
        }
        finally
        {
            _isExecuting = false;
        }
    }

    private void DisplayHelp()
    {
        var helpBuilder = new StringBuilder();
        helpBuilder.AppendLine("Available Commands:");
        helpBuilder.AppendLine("-------------------");
        helpBuilder.AppendLine("HELP          - Displays this help message.");
        helpBuilder.AppendLine("CLEAR         - Clears the terminal screen.");
        
        var commands = CommandRegistry.GetAllCommands().OrderBy(c => c.Name);
        foreach (var cmd in commands)
        {
            helpBuilder.AppendLine($"{cmd.Name.PadRight(14)}- {cmd.HelpText}");
        }
        helpBuilder.AppendLine("\nTip: Use Arrow Up/Down for history and Tab for autocompletion.");
        LogToOutput(helpBuilder.ToString());
    }
}