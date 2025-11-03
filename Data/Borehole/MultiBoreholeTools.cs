// GeoscientistToolkit/UI/Tools/MultiBoreholeTools.cs

using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Linq;

namespace GeoscientistToolkit.UI.Tools;

/// <summary>
/// Tools wrapper for DatasetGroup containing multiple boreholes
/// Provides access to multi-borehole geothermal analysis
/// </summary>
public class MultiBoreholeTools : IDatasetTools
{
    private MultiBoreholeGeothermalTools _geothermalTools;

    public void Draw(Dataset dataset)
    {
        if (dataset is not DatasetGroup group)
        {
            ImGui.TextDisabled("Invalid dataset type.");
            return;
        }

        // Verify all datasets are boreholes
        var boreholes = group.Datasets.OfType<Data.Borehole.BoreholeDataset>().ToList();
        if (boreholes.Count == 0)
        {
            ImGui.TextDisabled("No boreholes in this group.");
            return;
        }

        if (boreholes.Count != group.Datasets.Count)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1, 0.7f, 0, 1), 
                "Warning: Group contains non-borehole datasets.");
        }

        ImGui.TextColored(new System.Numerics.Vector4(0.4f, 0.8f, 1.0f, 1.0f), 
            "Multi-Borehole Tools");
        ImGui.Separator();

        ImGui.Text($"Boreholes in group: {boreholes.Count}");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Geothermal Analysis", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Initialize geothermal tools if needed
            if (_geothermalTools == null)
            {
                _geothermalTools = new MultiBoreholeGeothermalTools();
            }

            // Draw the geothermal tools
            _geothermalTools.Draw(dataset);
        }
    }
}