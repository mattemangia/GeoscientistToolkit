// GeoscientistToolkit/UI/DatasetPanel.cs
// This class implements the UI panel that displays the list of loaded datasets.
// The datasets are organized in a tree view, categorized by their type.

using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class DatasetPanel
    {
        public void Submit(ref bool pOpen, Action<Dataset> onDatasetSelected)
        {
            if (ImGui.Begin("Datasets", ref pOpen))
            {
                var datasetsByType = ProjectManager.Instance.LoadedDatasets.GroupBy(d => d.Type);

                foreach (var group in datasetsByType)
                {
                    if (ImGui.TreeNode(group.Key.ToString()))
                    {
                        foreach (var dataset in group)
                        {
                            if (ImGui.Selectable(dataset.Name, false, ImGuiSelectableFlags.None, new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, 0)))
                            {
                                onDatasetSelected(dataset);
                            }
                        }
                        ImGui.TreePop();
                    }
                }
            }
            ImGui.End();
        }
    }
}