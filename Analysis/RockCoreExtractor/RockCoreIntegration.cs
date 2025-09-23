// GeoscientistToolkit/Analysis/RockCoreExtractor/RockCoreIntegration.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Tools;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.RockCoreExtractor
{
    /// <summary>
    /// Integrates the Rock Core Extractor overlay with the CtCombinedViewer.
    /// This class manages the connection between the tool and the viewer.
    /// </summary>
    public static class RockCoreIntegration
    {
        private static readonly Dictionary<CtImageStackDataset, RockCoreExtractorTool> _activeTools = 
            new Dictionary<CtImageStackDataset, RockCoreExtractorTool>();

        /// <summary>
        /// Registers a Rock Core Extractor tool for a dataset.
        /// </summary>
        public static void RegisterTool(CtImageStackDataset dataset, RockCoreExtractorTool tool)
        {
            if (dataset != null && tool != null)
            {
                _activeTools[dataset] = tool;
            }
        }

        /// <summary>
        /// Unregisters a tool for a dataset.
        /// </summary>
        public static void UnregisterTool(CtImageStackDataset dataset)
        {
            if (dataset != null)
            {
                _activeTools.Remove(dataset);
            }
        }

        /// <summary>
        /// Gets the active tool for a dataset, if any.
        /// </summary>
        public static RockCoreExtractorTool GetActiveTool(CtImageStackDataset dataset)
        {
            return dataset != null && _activeTools.TryGetValue(dataset, out var tool) ? tool : null;
        }

        /// <summary>
        /// Draws the overlay on a slice view if a tool is active.
        /// This should be called from CtCombinedViewer.DrawSingleSlice after drawing the texture.
        /// </summary>
        public static void DrawOverlay(CtImageStackDataset dataset, ImDrawListPtr dl, int viewIndex, 
            Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight,
            int sliceX, int sliceY, int sliceZ)
        {
            var tool = GetActiveTool(dataset);
            if (tool?.Overlay != null && tool.ShowPreview)
            {
                tool.Overlay.DrawOnSlice(dl, viewIndex, imagePos, imageSize, 
                    imageWidth, imageHeight, sliceX, sliceY, sliceZ);
            }
        }

        /// <summary>
        /// Handles mouse input for the overlay if a tool is active.
        /// Returns true if the input was handled by the overlay.
        /// </summary>
        public static bool HandleMouseInput(CtImageStackDataset dataset, Vector2 mousePos, 
            Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int viewIndex,
            bool clicked, bool dragging, bool released)
        {
            var tool = GetActiveTool(dataset);
            if (tool?.Overlay != null && tool.ShowPreview)
            {
                return tool.Overlay.HandleMouseInput(mousePos, imagePos, imageSize, 
                    imageWidth, imageHeight, viewIndex, clicked, dragging, released);
            }
            return false;
        }

        /// <summary>
        /// Clears all registered tools.
        /// </summary>
        public static void Clear()
        {
            _activeTools.Clear();
        }
    }
}