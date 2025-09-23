// GeoscientistToolkit/Analysis/Transform/TransformIntegration.cs
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Transform
{
    /// <summary>
    /// Integrates the Transform Tool overlay with the CtCombinedViewer.
    /// </summary>
    public static class TransformIntegration
    {
        private static readonly Dictionary<CtImageStackDataset, TransformTool> _activeTools = new();

        public static void RegisterTool(CtImageStackDataset dataset, TransformTool tool)
        {
            if (dataset != null && tool != null)
            {
                _activeTools[dataset] = tool;
            }
        }

        public static void UnregisterTool(CtImageStackDataset dataset)
        {
            if (dataset != null)
            {
                _activeTools.Remove(dataset);
            }
        }

        public static TransformTool GetActiveTool(CtImageStackDataset dataset)
        {
            return dataset != null && _activeTools.TryGetValue(dataset, out var tool) ? tool : null;
        }

        public static void DrawOverlay(ImDrawListPtr dl, CtImageStackDataset dataset, int viewIndex, 
            Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
        {
            var tool = GetActiveTool(dataset);
            if (tool?.Overlay != null && tool.ShowPreview)
            {
                tool.Overlay.DrawOnSlice(dl, viewIndex, imagePos, imageSize, imageWidth, imageHeight);
            }
        }

        public static bool HandleMouseInput(CtImageStackDataset dataset, Vector2 mousePos, 
            Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int viewIndex,
            bool clicked, bool dragging, bool released)
        {
            var tool = GetActiveTool(dataset);
            if (tool?.Overlay != null && tool.ShowPreview)
            {
                // Defer input handling to the overlay class
                return tool.Overlay.HandleMouseInput(mousePos, imagePos, imageSize, 
                    imageWidth, imageHeight, viewIndex, clicked, dragging, released);
            }
            return false;
        }
    }
}