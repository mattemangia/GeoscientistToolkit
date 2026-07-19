// GAIA/Analysis/Filtering/FilterSandboxIntegration.cs

using System.Numerics;
using GAIA.Data.CtImageStack;
using ImGuiNET;

namespace GAIA.Analysis.Filtering;

/// <summary>
///     Connects the filter sandbox (interactive ROI preview) to the CT slice viewers. The ROI is
///     a draggable/resizable region drawn on a slice view; the selected filter is computed only
///     inside the region, so parameters can be compared live even on 70GB+ datasets.
/// </summary>
public static class FilterSandboxIntegration
{
    private static readonly Dictionary<CtImageStackDataset, FilterUI> _owners = new();

    public static void Register(CtImageStackDataset dataset, FilterUI ui)
    {
        if (dataset == null || ui == null) return;
        _owners[dataset] = ui;
    }

    public static void Unregister(CtImageStackDataset dataset)
    {
        if (dataset == null) return;
        _owners.Remove(dataset);
    }

    public static void DrawOverlay(CtImageStackDataset dataset, ImDrawListPtr dl, int viewIndex,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int sliceX, int sliceY, int sliceZ)
    {
        if (dataset == null || !_owners.TryGetValue(dataset, out var ui)) return;
        ui.DrawSandboxOverlay(dataset, dl, viewIndex, imagePos, imageSize, imageWidth, imageHeight,
            sliceX, sliceY, sliceZ);
    }

    public static bool HandleMouseInput(CtImageStackDataset dataset, Vector2 mousePos, Vector2 imagePos,
        Vector2 imageSize, int imageWidth, int imageHeight, int viewIndex, bool clicked, bool dragging,
        bool released)
    {
        if (dataset == null || !_owners.TryGetValue(dataset, out var ui)) return false;
        return ui.HandleSandboxMouse(mousePos, imagePos, imageSize, imageWidth, imageHeight, viewIndex,
            clicked, dragging, released);
    }
}
