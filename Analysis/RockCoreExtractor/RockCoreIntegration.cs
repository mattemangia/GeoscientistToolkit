// GeoscientistToolkit/Analysis/RockCoreExtractor/RockCoreIntegration.cs

using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.RockCoreExtractor;

/// <summary>
///     Integrates the Rock Core Extractor overlay with viewers.
///     Ensures the tool is bound to the dataset even before its UI is opened.
/// </summary>
public static class RockCoreIntegration
{
    private static readonly Dictionary<CtImageStackDataset, RockCoreExtractorTool> _activeTools = new();

    public static void RegisterTool(CtImageStackDataset dataset, RockCoreExtractorTool tool)
    {
        if (dataset == null || tool == null) return;
        _activeTools[dataset] = tool;
        // Ensure the tool has a dataset and overlay immediately
        tool.AttachDataset(dataset);
    }

    public static void UnregisterTool(CtImageStackDataset dataset)
    {
        if (dataset == null) return;
        _activeTools.Remove(dataset);
    }

    public static RockCoreExtractorTool GetActiveTool(CtImageStackDataset dataset)
    {
        if (dataset == null) return null;
        return _activeTools.TryGetValue(dataset, out var tool) ? tool : null;
    }

    public static void DrawOverlay(CtImageStackDataset dataset, ImDrawListPtr dl,
        int viewIndex, Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight,
        int sliceX, int sliceY, int sliceZ)
    {
        var tool = GetActiveTool(dataset);
        if (tool?.Overlay == null || !tool.ShowPreview) return;
        tool.Overlay.DrawOnSlice(dl, viewIndex, imagePos, imageSize, imageWidth, imageHeight, sliceX, sliceY, sliceZ);
    }

    public static bool HandleMouseInput(CtImageStackDataset dataset, Vector2 mousePos,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int viewIndex,
        bool clicked, bool dragging, bool released)
    {
        var tool = GetActiveTool(dataset);
        if (tool?.Overlay != null && tool.ShowPreview)
        {
            // Attach again defensively in case the tool was deserialized without UI pass
            tool.AttachDataset(dataset);
            return tool.Overlay.HandleMouseInput(mousePos, imagePos, imageSize,
                imageWidth, imageHeight, viewIndex, clicked, dragging, released);
        }

        return false;
    }

    public static void Clear()
    {
        _activeTools.Clear();
    }
}