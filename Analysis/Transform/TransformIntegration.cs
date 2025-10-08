// GeoscientistToolkit/Analysis/Transform/TransformIntegration.cs

using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Transform;

/// <summary>
///     Integrates the Transform/Crop Tool overlay with the CtCombinedViewer / CtImageStackViewer.
///     Routes draw and mouse input to the currently selected overlay.
/// </summary>
public static class TransformIntegration
{
    private static readonly Dictionary<CtImageStackDataset, TransformTool> _activeTools = new();

    public static void RegisterTool(CtImageStackDataset dataset, TransformTool tool)
    {
        if (dataset != null && tool != null) _activeTools[dataset] = tool;
    }

    public static void UnregisterTool(CtImageStackDataset dataset)
    {
        if (dataset != null) _activeTools.Remove(dataset);
    }

    public static TransformTool GetActiveTool(CtImageStackDataset dataset)
    {
        return dataset != null && _activeTools.TryGetValue(dataset, out var tool) ? tool : null;
    }

    public static void DrawOverlay(ImDrawListPtr dl, CtImageStackDataset dataset, int viewIndex,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
    {
        var tool = GetActiveTool(dataset);
        var overlay = tool?.Overlay;
        if (overlay != null && tool.ShowPreview)
            overlay.DrawOnSlice(dl, viewIndex, imagePos, imageSize, imageWidth, imageHeight);
    }

    public static bool HandleMouseInput(CtImageStackDataset dataset, Vector2 mousePos,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int viewIndex,
        bool clickedFromItem, bool draggingFromItem, bool releasedFromItem)
    {
        var tool = GetActiveTool(dataset);
        var overlay = tool?.Overlay;
        if (overlay == null || !tool.ShowPreview) return false;

        // Only interact when the mouse is inside the displayed image rect
        var insideImage =
            mousePos.X >= imagePos.X && mousePos.X <= imagePos.X + imageSize.X &&
            mousePos.Y >= imagePos.Y && mousePos.Y <= imagePos.Y + imageSize.Y;

        // Be robust: OR item-scoped events with global ImGui mouse state.
        // This fixes edge cases where the InvisibleButton didn't register the click.
        var clicked = clickedFromItem || (insideImage && ImGui.IsMouseClicked(ImGuiMouseButton.Left));
        var dragging = draggingFromItem || (insideImage && ImGui.IsMouseDragging(ImGuiMouseButton.Left));
        var released = releasedFromItem || ImGui.IsMouseReleased(ImGuiMouseButton.Left);

        if (!insideImage && !(overlay is null))
            // If the press started inside and ended outside we still want to allow the release
            // but otherwise ignore out-of-bounds interaction.
            if (!dragging && !released)
                return false;

        return overlay.HandleMouseInput(mousePos, imagePos, imageSize,
            imageWidth, imageHeight, viewIndex, clicked, dragging, released);
    }
}