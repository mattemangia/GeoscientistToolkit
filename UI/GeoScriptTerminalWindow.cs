// GeoscientistToolkit/UI/Windows/GeoScriptTerminalWindow.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Windows;

/// <summary>
///     A window that hosts the GeoScript editor, providing a terminal-like interface.
/// </summary>
public class GeoScriptTerminalWindow
{
    private readonly GeoScriptEditor _editor = new();
    private bool _isOpen;
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
            _editor.SetAssociatedDataset(_selectedContextDataset);
        }
    }

    public void Draw()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("GeoScript Terminal", ref _isOpen))
        {
            DrawDatasetSelector();
            ImGui.Separator();
            _editor.Draw();
        }

        ImGui.End();
    }

    private void DrawDatasetSelector()
    {
        var loadedDatasets = ProjectManager.Instance.LoadedDatasets;
        if (!loadedDatasets.Any())
        {
            ImGui.Text("Context Dataset: (No datasets loaded)");
            // Ensure the editor knows there is no context if all datasets are removed
            if (_selectedContextDataset != null)
            {
                _selectedContextDataset = null;
                _editor.SetAssociatedDataset(null);
            }

            return;
        }

        var datasetNames = loadedDatasets.Select(d => d.Name).ToArray();

        // If the current index is invalid (e.g., dataset was removed), reset it.
        if (_selectedDatasetIndex >= datasetNames.Length)
        {
            _selectedDatasetIndex = -1;
            _selectedContextDataset = null;
            _editor.SetAssociatedDataset(null);
        }
        else if (_selectedDatasetIndex != -1 && loadedDatasets[_selectedDatasetIndex] != _selectedContextDataset)
        {
            // Sync selection if out of date
            _selectedContextDataset = loadedDatasets[_selectedDatasetIndex];
            _editor.SetAssociatedDataset(_selectedContextDataset);
        }


        ImGui.Text("Context Dataset:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.Combo("##ContextDataset", ref _selectedDatasetIndex, datasetNames, datasetNames.Length))
            if (_selectedDatasetIndex >= 0 && _selectedDatasetIndex < loadedDatasets.Count)
            {
                _selectedContextDataset = loadedDatasets[_selectedDatasetIndex];
                _editor.SetAssociatedDataset(_selectedContextDataset);
            }
    }
}