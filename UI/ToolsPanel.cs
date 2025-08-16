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

        // --- FIX START ---
        // Cache the tools renderer to preserve its state across frames.
        private IDatasetTools _toolsRenderer;
        // Keep track of which dataset the current renderer is for.
        private Dataset _datasetForRenderer;
        // --- FIX END ---

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
            // --- FIX START ---
            // Check if the selected dataset has changed since the last frame.
            if (_selectedDataset != _datasetForRenderer)
            {
                // If it has, create a new tools renderer for the new dataset and cache it.
                // If no dataset is selected, the renderer becomes null.
                _toolsRenderer = _selectedDataset != null ? DatasetUIFactory.CreateTools(_selectedDataset) : null;
                _datasetForRenderer = _selectedDataset;
            }
            // --- FIX END ---

            if (_toolsRenderer != null)
            {
                // Always draw using the cached renderer instance. This preserves the state
                // of sliders, dropdowns, and other UI elements within the tools.
                _toolsRenderer.Draw(_selectedDataset);
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