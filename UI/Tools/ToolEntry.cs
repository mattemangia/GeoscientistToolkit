using GeoscientistToolkit.UI.Interfaces;

namespace GeoscientistToolkit.UI.Tools;

public partial class CtImageStackCompositeTool
{
    private class ToolEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public IDatasetTools Tool { get; set; }
        public ToolCategory Category { get; set; }
    }
}