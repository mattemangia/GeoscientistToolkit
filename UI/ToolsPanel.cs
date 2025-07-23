// GeoscientistToolkit/UI/ToolsPanel.cs (Fixed to handle pop-out state correctly)
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
    public class ToolsPanel : BasePanel
    {
        private Dataset _selectedDataset;
        
        public ToolsPanel() : base("Tools", new Vector2(280, 350))
        {
        }
        
        public void Submit(ref bool pOpen, Dataset selectedDataset)
        {
            // CRITICAL: Update the dataset BEFORE calling base.Submit()
            // This ensures the dataset is available even when popped out
            _selectedDataset = selectedDataset;
            
            base.Submit(ref pOpen);
        }
        
        protected override void DrawContent()
        {
            if (_selectedDataset != null)
            {
                var toolsRenderer = DatasetUIFactory.CreateTools(_selectedDataset);
                toolsRenderer.Draw(_selectedDataset);
            }
            else
            {
                var windowSize = ImGui.GetWindowSize();
                var text = "No dataset selected";
                var textSize = ImGui.CalcTextSize(text);
                ImGui.SetCursorPos(new Vector2((windowSize.X - textSize.X) * 0.5f, (windowSize.Y - textSize.Y) * 0.5f));
                ImGui.TextDisabled(text);
            }
        }
    }
}