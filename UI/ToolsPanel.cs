// GeoscientistToolkit/UI/ToolsPanel.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
    public class ToolsPanel
    {
        public void Submit(ref bool pOpen, Dataset selectedDataset)
        {
            ImGui.SetNextWindowSize(new Vector2(280, 350), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin("Tools", ref pOpen))
            {
                ImGui.End();
                return;
            }

            if (selectedDataset != null)
            {
                // Get the appropriate tools UI renderer for the dataset type
                var toolsRenderer = DatasetUIFactory.CreateTools(selectedDataset);
                toolsRenderer.Draw(selectedDataset);
            }
            else
            {
                var windowSize = ImGui.GetWindowSize();
                var text = "No dataset selected";
                var textSize = ImGui.CalcTextSize(text);
                ImGui.SetCursorPos(new Vector2((windowSize.X - textSize.X) * 0.5f, (windowSize.Y - textSize.Y) * 0.5f));
                ImGui.TextDisabled(text);
            }

            ImGui.End();
        }
    }
}