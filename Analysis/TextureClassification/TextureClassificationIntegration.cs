// GeoscientistToolkit/Analysis/TextureClassification/TextureClassificationIntegration.cs

using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Tools;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.TextureClassification;

public static class TextureClassificationIntegration
{
    private static readonly Dictionary<CtImageStackDataset, TextureClassificationTool> _activeTools = new();
    private static readonly object _lock = new();

    public static void RegisterTool(CtImageStackDataset dataset, TextureClassificationTool tool)
    {
        lock (_lock)
        {
            _activeTools[dataset] = tool;
        }
    }

    public static void UnregisterTool(CtImageStackDataset dataset)
    {
        lock (_lock)
        {
            _activeTools.Remove(dataset);
        }
    }

    public static bool HandleMouseInput(CtImageStackDataset dataset, Vector2 mousePos, Vector2 imagePos,
        Vector2 imageSize, int imageWidth, int imageHeight, int viewIndex, bool leftClick, bool leftDrag,
        bool leftRelease)
    {
        lock (_lock)
        {
            if (!_activeTools.TryGetValue(dataset, out var tool))
                return false;

            if (!tool.IsDrawingMode())
                return false;

            if (leftClick || leftDrag)
            {
                var relativePos = mousePos - imagePos;
                var normalizedX = relativePos.X / imageSize.X;
                var normalizedY = relativePos.Y / imageSize.Y;

                if (normalizedX < 0 || normalizedX > 1 || normalizedY < 0 || normalizedY > 1)
                    return false;

                var clickX = (int)(normalizedX * imageWidth);
                var clickY = (int)(normalizedY * imageHeight);

                var sliceIndex = viewIndex switch
                {
                    0 => dataset.Depth / 2, // These should be passed from viewer
                    1 => dataset.Height / 2,
                    2 => dataset.Width / 2,
                    _ => 0
                };

                tool.HandleViewerClick(new Vector2(clickX, clickY), sliceIndex, viewIndex, imageWidth, imageHeight);
                return true;
            }

            return false;
        }
    }

    public static bool HandleRightClick(CtImageStackDataset dataset, Vector2 mousePos, Vector2 imagePos,
        Vector2 imageSize, int imageWidth, int imageHeight, int viewIndex, int sliceIndex)
    {
        lock (_lock)
        {
            if (!_activeTools.TryGetValue(dataset, out var tool))
                return false;

            if (!tool.IsDrawingMode())
                return false;

            var relativePos = mousePos - imagePos;
            var normalizedX = relativePos.X / imageSize.X;
            var normalizedY = relativePos.Y / imageSize.Y;

            if (normalizedX < 0 || normalizedX > 1 || normalizedY < 0 || normalizedY > 1)
                return false;

            var clickX = (int)(normalizedX * imageWidth);
            var clickY = (int)(normalizedY * imageHeight);

            tool.HandleViewerRightClick(new Vector2(clickX, clickY), sliceIndex, viewIndex, imageWidth, imageHeight);
            return true;
        }
    }

    public static void DrawOverlay(CtImageStackDataset dataset, ImDrawListPtr dl, int viewIndex,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int sliceX, int sliceY, int sliceZ)
    {
        lock (_lock)
        {
            if (!_activeTools.TryGetValue(dataset, out var tool))
                return;

            var sliceIndex = viewIndex switch
            {
                0 => sliceZ,
                1 => sliceY,
                2 => sliceX,
                _ => 0
            };

            var patches = tool.GetPatchesForSlice(sliceIndex, viewIndex);

            foreach (var patch in patches)
            {
                var halfSize = patch.PatchSize / 2;

                var screenX = imagePos.X + patch.X / (float)imageWidth * imageSize.X;
                var screenY = imagePos.Y + patch.Y / (float)imageHeight * imageSize.Y;
                var patchScreenSize = patch.PatchSize / (float)imageWidth * imageSize.X;

                var topLeft = new Vector2(screenX - patchScreenSize / 2, screenY - patchScreenSize / 2);
                var bottomRight = new Vector2(screenX + patchScreenSize / 2, screenY + patchScreenSize / 2);

                // Color based on class
                var color = patch.ClassId switch
                {
                    1 => 0xFF0000FF, // Red
                    2 => 0xFF00FF00, // Green
                    3 => 0xFFFF0000, // Blue
                    4 => 0xFF00FFFF, // Yellow
                    5 => 0xFFFF00FF, // Magenta
                    _ => 0xFFFFFFFF // White
                };

                dl.AddRect(topLeft, bottomRight, color, 0, ImDrawFlags.None, 2.0f);
                dl.AddText(new Vector2(screenX + patchScreenSize / 2 + 5, screenY - 10), color, $"C{patch.ClassId}");
            }
        }
    }
}