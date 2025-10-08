// GeoscientistToolkit/Analysis/MaterialStatistics/MaterialStatisticsTool.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.MaterialStatistics;

public class MaterialStatisticsTool : IDatasetTools, IDisposable
{
    private CtImageStackDataset _currentDataset;
    private MaterialStatisticsWindow _statsWindow;
    private bool _windowOpen;

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ctDataset)
        {
            ImGui.TextDisabled("Material statistics requires a CT Image Stack dataset.");
            return;
        }

        // Create or update window for current dataset
        if (_currentDataset != ctDataset)
        {
            _statsWindow?.Dispose();
            _statsWindow = new MaterialStatisticsWindow(ctDataset);
            _currentDataset = ctDataset;
        }

        // UI Controls
        ImGui.Text("Material Volume Analysis");
        ImGui.Spacing();

        ImGui.TextWrapped("Analyze material volumes, percentages, and spatial distributions.");
        ImGui.Spacing();

        if (ImGui.Button("Open Statistics Window", new Vector2(-1, 0)))
        {
            _windowOpen = true;
            _statsWindow.MarkForRecalculation();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Features:");
        ImGui.BulletText("Volume calculations per material");
        ImGui.BulletText("Percentage of total volume");
        ImGui.BulletText("Center of mass computation");
        ImGui.BulletText("Bounding box analysis");
        ImGui.BulletText("Interactive pie/histogram charts");
        ImGui.BulletText("Export to CSV or image");

        // Draw the statistics window if open
        if (_windowOpen) _statsWindow.Submit(ref _windowOpen);
    }

    public void Dispose()
    {
        _statsWindow?.Dispose();
    }
}