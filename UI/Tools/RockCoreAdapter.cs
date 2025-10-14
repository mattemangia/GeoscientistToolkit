using GeoscientistToolkit.Analysis.RockCoreExtractor;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Tools;

public partial class CtImageStackCompositeTool
{
    private sealed class RockCoreAdapter : IDatasetTools
    {
        public RockCoreAdapter(RockCoreExtractorTool tool)
        {
            Tool = tool ?? throw new ArgumentNullException(nameof(tool));
        }

        public RockCoreExtractorTool Tool { get; }

        public void Draw(Dataset dataset)
        {
            if (dataset is CtImageStackDataset ct)
            {
                Tool.AttachDataset(ct);
                Tool.DrawUI(ct);
            }
            else
            {
                ImGui.TextDisabled("Rock Core tool requires a CT Image Stack dataset.");
            }
        }
    }
}