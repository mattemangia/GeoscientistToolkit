// GeoscientistToolkit/UI/Windows/GeoScriptTerminalWindow.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Windows;

/// <summary>
///     A window that hosts the GeoScript interpreter, providing a terminal-like interface.
///     This window supports being "popped out" into its own native window.
/// </summary>
public class GeoScriptTerminalWindow
{
    private readonly GeoScriptInterpreter _interpreter = new();
    private bool _isOpen;
    private bool _isPoppedOut;
    private bool _pendingPopIn; // Flag to defer pop-in until safe
    private PopOutWindow _popOutWindow;
    private Dataset _selectedContextDataset;
    private int _selectedDatasetIndex = -1;

    public void Show()
    {
        _isOpen = true;
        // If no dataset is selected, try to select the first one automatically
        if (_selectedContextDataset == null && ProjectManager.Instance.LoadedDatasets.Any())
        {
            _selectedDatasetIndex = 0;
            _selectedContextDataset = ProjectManager.Instance.LoadedDatasets[_selectedDatasetIndex];
            _interpreter.SetAssociatedDataset(_selectedContextDataset);
        }
    }

    public void Draw()
    {
        if (!_isOpen) return;

        // --- Handle deferred pop-in BEFORE processing the pop-out window ---
        if (_pendingPopIn)
        {
            _pendingPopIn = false;
            PerformPopIn();
        }

        // --- Pop-out Window Management ---
        if (_isPoppedOut)
        {
            // If the pop-out window exists, process its frame. It will draw its own content.
            if (_popOutWindow != null && _popOutWindow.Exists)
                _popOutWindow.ProcessFrame();
            else
                // If the window was closed by the user (e.g., clicking the 'X' button), 
                // schedule a deferred pop-in instead of doing it immediately.
                RequestPopIn();

            // Do not draw the integrated ImGui window when it's popped out.
            return;
        }

        // --- Integrated ImGui Window Drawing ---
        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("GeoScript Terminal", ref _isOpen))
        {
            // Add a context menu to the window for the "Pop Out" action.
            if (ImGui.BeginPopupContextWindow())
            {
                if (ImGui.MenuItem("Pop Out")) PopOut();
                ImGui.EndPopup();
            }

            DrawContents();
        }

        ImGui.End();
    }

    /// <summary>
    ///     Draws the actual content of the terminal, used by both integrated and popped-out windows.
    /// </summary>
    private void DrawContents()
    {
        DrawDatasetSelector();
        ImGui.Separator();
        
        if (_selectedContextDataset == null)
        {
            DrawNoDatasetWarning();
        }
        else
        {
            _interpreter.Draw();
        }
    }
    
    /// <summary>
    ///     Draws a warning panel when no dataset is selected for context.
    ///     This logic is adapted from the original GeoScriptEditor.
    /// </summary>
    private void DrawNoDatasetWarning()
    {
        var style = ImGui.GetStyle();
        ImGui.BeginChild("##NoDatasetWarning", Vector2.Zero, ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMove);

        var windowSize = ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        
        var titleBarHeight = ImGui.GetTextLineHeight() + style.FramePadding.Y * 2;
        drawList.AddRectFilled(
            windowPos,
            new Vector2(windowPos.X + windowSize.X, windowPos.Y + titleBarHeight),
            ImGui.GetColorU32(new Vector4(0.7f, 0.2f, 0.2f, 1.0f))
        );

        drawList.AddRect(
            windowPos,
            new Vector2(windowPos.X + windowSize.X, windowPos.Y + windowSize.Y),
            ImGui.GetColorU32(new Vector4(0.7f, 0.2f, 0.2f, 1.0f)),
            style.ChildRounding
        );
        
        var title = "⚠ No Dataset Selected";
        var titleSize = ImGui.CalcTextSize(title);
        ImGui.SetCursorPos(new Vector2(
            (windowSize.X - titleSize.X) * 0.5f,
            (titleBarHeight - titleSize.Y) * 0.5f
        ));
        ImGui.Text(title);
        
        ImGui.SetCursorPos(new Vector2(style.WindowPadding.X, titleBarHeight + style.WindowPadding.Y));
        ImGui.BeginGroup();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.8f, 1.0f));
        ImGui.TextWrapped(
            "The GeoScript Terminal requires an active dataset to act upon. Please select a 'Context Dataset' from the dropdown list above to begin scripting.");
        ImGui.PopStyleColor();
        ImGui.EndGroup();

        ImGui.EndChild();
    }


    /// <summary>
    ///     Handles the drawing logic when the terminal is in a separate window.
    ///     This method is set as the callback for the PopOutWindow instance.
    /// </summary>
    private void DrawPoppedOutWindow()
    {
        // Draw the terminal content filling the entire pop-out window.
        ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);
        ImGui.SetNextWindowPos(Vector2.Zero);
        
        var windowOpen = true;

        if (ImGui.Begin("GeoScript Terminal##PoppedOut",
                ref windowOpen,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoTitleBar))
        {
            // Add a context menu for the "Pop In" action.
            if (ImGui.BeginPopupContextWindow())
            {
                if (ImGui.MenuItem("Pop In")) RequestPopIn();
                ImGui.EndPopup();
            }

            DrawContents();
        }
        
        if (!windowOpen) RequestPopIn();

        ImGui.End();
    }

    /// <summary>
    ///     Transitions the window from an integrated panel to a separate OS window.
    /// </summary>
    private void PopOut()
    {
        if (_isPoppedOut) return;
        
        const float minWidth = 400;
        const float minHeight = 300;
        var size = ImGui.GetWindowSize();
        var pos = ImGui.GetWindowPos();

        var width = (int)Math.Max(size.X, minWidth);
        var height = (int)Math.Max(size.Y, minHeight);

        _popOutWindow = new PopOutWindow("GeoScript Terminal", (int)pos.X, (int)pos.Y, width, height);
        _popOutWindow.SetDrawCallback(DrawPoppedOutWindow);
        _isPoppedOut = true;
    }

    /// <summary>
    ///     Requests a deferred pop-in. The actual pop-in will occur at the start of the next frame,
    ///     ensuring we're not disposing resources while they're still being used.
    /// </summary>
    private void RequestPopIn()
    {
        if (!_isPoppedOut) return;
        _pendingPopIn = true;
    }

    /// <summary>
    ///     Actually performs the pop-in operation. This should only be called when it's safe
    ///     (i.e., not during the pop-out window's frame rendering).
    /// </summary>
    private void PerformPopIn()
    {
        if (!_isPoppedOut) return;
        
        _popOutWindow?.Dispose();
        _popOutWindow = null;
        _isPoppedOut = false;
    }

    private void DrawDatasetSelector()
    {
        var loadedDatasets = ProjectManager.Instance.LoadedDatasets;
        if (!loadedDatasets.Any())
        {
            ImGui.Text("Context Dataset: (No datasets loaded)");
            if (_selectedContextDataset != null)
            {
                _selectedContextDataset = null;
                _interpreter.SetAssociatedDataset(null);
            }

            return;
        }

        var datasetNames = loadedDatasets.Select(d => d.Name).ToArray();
        
        if (_selectedDatasetIndex >= datasetNames.Length)
        {
            _selectedDatasetIndex = -1;
            _selectedContextDataset = null;
            _interpreter.SetAssociatedDataset(null);
        }
        else if (_selectedDatasetIndex != -1 && loadedDatasets[_selectedDatasetIndex] != _selectedContextDataset)
        {
            _selectedContextDataset = loadedDatasets[_selectedDatasetIndex];
            _interpreter.SetAssociatedDataset(_selectedContextDataset);
        }

        ImGui.Text("Context Dataset:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.Combo("##ContextDataset", ref _selectedDatasetIndex, datasetNames, datasetNames.Length))
            if (_selectedDatasetIndex >= 0 && _selectedDatasetIndex < loadedDatasets.Count)
            {
                _selectedContextDataset = loadedDatasets[_selectedDatasetIndex];
                _interpreter.SetAssociatedDataset(_selectedContextDataset);
            }
    }
}