// GeoscientistToolkit/Analysis/AmbientOcclusionSegmentation/AmbientOcclusionIntegration.cs

using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.AmbientOcclusionSegmentation;

/// <summary>
/// Integration layer for ambient occlusion segmentation with CT viewer
/// Manages overlay rendering and preview visualization
/// </summary>
public static class AmbientOcclusionIntegration
{
    private static readonly Dictionary<CtImageStackDataset, AmbientOcclusionPreview> _activePreviews = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Register an active preview for a dataset
    /// </summary>
    public static void RegisterPreview(CtImageStackDataset dataset, AmbientOcclusionPreview preview)
    {
        lock (_lock)
        {
            _activePreviews[dataset] = preview;
        }
    }

    /// <summary>
    /// Unregister preview for a dataset
    /// </summary>
    public static void UnregisterPreview(CtImageStackDataset dataset)
    {
        lock (_lock)
        {
            _activePreviews.Remove(dataset);
        }
    }

    /// <summary>
    /// Check if there's an active preview for the dataset
    /// </summary>
    public static bool HasPreview(CtImageStackDataset dataset)
    {
        lock (_lock)
        {
            return _activePreviews.ContainsKey(dataset);
        }
    }

    /// <summary>
    /// Draw overlay for ambient occlusion preview
    /// </summary>
    public static void DrawOverlay(CtImageStackDataset dataset, ImDrawListPtr dl, int viewIndex,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int sliceX, int sliceY, int sliceZ)
    {
        lock (_lock)
        {
            if (!_activePreviews.TryGetValue(dataset, out var preview))
                return;

            if (!preview.ShowPreview || preview.Result == null)
                return;

            DrawAoFieldOverlay(dl, preview, viewIndex, imagePos, imageSize, imageWidth, imageHeight,
                sliceX, sliceY, sliceZ);
        }
    }

    private static void DrawAoFieldOverlay(ImDrawListPtr dl, AmbientOcclusionPreview preview, int viewIndex,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, int sliceX, int sliceY, int sliceZ)
    {
        var aoField = preview.Result.AoField;
        var mask = preview.Result.SegmentationMask;

        if (aoField == null || mask == null)
            return;

        // Determine which slice to visualize based on view
        int sliceIndex = viewIndex switch
        {
            0 => sliceZ, // XY view
            1 => sliceY, // XZ view
            2 => sliceX, // YZ view
            _ => 0
        };

        // Draw based on preview mode
        if (preview.ShowAoField)
        {
            DrawAoFieldHeatmap(dl, aoField, viewIndex, sliceIndex, imagePos, imageSize, imageWidth, imageHeight);
        }

        if (preview.ShowSegmentationMask)
        {
            DrawSegmentationOverlay(dl, mask, viewIndex, sliceIndex, imagePos, imageSize, imageWidth, imageHeight,
                preview.OverlayColor, preview.OverlayOpacity);
        }

        // Draw statistics in corner
        if (preview.ShowStatistics)
        {
            DrawStatistics(dl, preview, imagePos, imageSize);
        }
    }

    private static void DrawAoFieldHeatmap(ImDrawListPtr dl, float[,,] aoField, int viewIndex, int sliceIndex,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight)
    {
        int width = aoField.GetLength(0);
        int height = aoField.GetLength(1);
        int depth = aoField.GetLength(2);

        // Sample subset of voxels for performance (every 4th pixel)
        int stepSize = Math.Max(1, Math.Min(width, height) / 256);

        for (int y = 0; y < imageHeight; y += stepSize)
        {
            for (int x = 0; x < imageWidth; x += stepSize)
            {
                float aoValue = GetAoValue(aoField, x, y, sliceIndex, viewIndex, width, height, depth,
                    imageWidth, imageHeight);

                if (aoValue < 0)
                    continue;

                // Convert AO value to heatmap color
                var color = GetHeatmapColor(aoValue, 0.5f); // 50% opacity

                // Draw pixel
                var pixelPos = imagePos + new Vector2(
                    x * imageSize.X / imageWidth,
                    y * imageSize.Y / imageHeight);

                var pixelSize = new Vector2(
                    stepSize * imageSize.X / imageWidth,
                    stepSize * imageSize.Y / imageHeight);

                dl.AddRectFilled(pixelPos, pixelPos + pixelSize, ImGui.ColorConvertFloat4ToU32(color));
            }
        }
    }

    private static void DrawSegmentationOverlay(ImDrawListPtr dl, bool[,,] mask, int viewIndex, int sliceIndex,
        Vector2 imagePos, Vector2 imageSize, int imageWidth, int imageHeight, Vector4 color, float opacity)
    {
        int width = mask.GetLength(0);
        int height = mask.GetLength(1);
        int depth = mask.GetLength(2);

        var overlayColor = new Vector4(color.X, color.Y, color.Z, opacity);
        uint colorU32 = ImGui.ColorConvertFloat4ToU32(overlayColor);

        // Sample subset for performance
        int stepSize = Math.Max(1, Math.Min(width, height) / 512);

        for (int y = 0; y < imageHeight; y += stepSize)
        {
            for (int x = 0; x < imageWidth; x += stepSize)
            {
                bool isSegmented = GetMaskValue(mask, x, y, sliceIndex, viewIndex, width, height, depth,
                    imageWidth, imageHeight);

                if (!isSegmented)
                    continue;

                var pixelPos = imagePos + new Vector2(
                    x * imageSize.X / imageWidth,
                    y * imageSize.Y / imageHeight);

                var pixelSize = new Vector2(
                    stepSize * imageSize.X / imageWidth,
                    stepSize * imageSize.Y / imageHeight);

                dl.AddRectFilled(pixelPos, pixelPos + pixelSize, colorU32);
            }
        }
    }

    private static void DrawStatistics(ImDrawListPtr dl, AmbientOcclusionPreview preview, Vector2 imagePos, Vector2 imageSize)
    {
        var result = preview.Result;
        if (result == null)
            return;

        // Calculate statistics
        var aoField = result.AoField;
        var mask = result.SegmentationMask;

        int totalVoxels = aoField.GetLength(0) * aoField.GetLength(1) * aoField.GetLength(2);
        int segmentedVoxels = 0;
        float minAo = float.MaxValue;
        float maxAo = float.MinValue;
        float avgAo = 0;

        for (int z = 0; z < aoField.GetLength(2); z++)
        {
            for (int y = 0; y < aoField.GetLength(1); y++)
            {
                for (int x = 0; x < aoField.GetLength(0); x++)
                {
                    float ao = aoField[x, y, z];
                    avgAo += ao;
                    minAo = Math.Min(minAo, ao);
                    maxAo = Math.Max(maxAo, ao);

                    if (mask[x, y, z])
                        segmentedVoxels++;
                }
            }
        }

        avgAo /= totalVoxels;
        float porosity = (float)segmentedVoxels / totalVoxels * 100f;

        // Draw semi-transparent background
        var bgPos = imagePos + new Vector2(10, 10);
        var bgSize = new Vector2(220, 120);
        dl.AddRectFilled(bgPos, bgPos + bgSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.7f)), 5f);

        // Draw text
        var textPos = bgPos + new Vector2(10, 10);
        var white = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1));
        var green = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 1, 0, 1));

        dl.AddText(textPos, white, "Ambient Occlusion Statistics");
        textPos.Y += 20;
        dl.AddText(textPos, white, $"AO Range: {minAo:F3} - {maxAo:F3}");
        textPos.Y += 18;
        dl.AddText(textPos, white, $"AO Average: {avgAo:F3}");
        textPos.Y += 18;
        dl.AddText(textPos, green, $"Porosity: {porosity:F2}%");
        textPos.Y += 18;
        dl.AddText(textPos, white, $"Processing: {result.ProcessingTime:F2}s");
        textPos.Y += 18;
        dl.AddText(textPos, white, $"Accel: {result.AccelerationType}");
    }

    private static float GetAoValue(float[,,] aoField, int imgX, int imgY, int sliceIndex, int viewIndex,
        int width, int height, int depth, int imageWidth, int imageHeight)
    {
        // Map image coordinates to volume coordinates
        int x, y, z;

        switch (viewIndex)
        {
            case 0: // XY view
                x = (int)((float)imgX / imageWidth * width);
                y = (int)((float)imgY / imageHeight * height);
                z = Math.Clamp(sliceIndex, 0, depth - 1);
                break;
            case 1: // XZ view
                x = (int)((float)imgX / imageWidth * width);
                z = (int)((float)imgY / imageHeight * depth);
                y = Math.Clamp(sliceIndex, 0, height - 1);
                break;
            case 2: // YZ view
                y = (int)((float)imgX / imageWidth * height);
                z = (int)((float)imgY / imageHeight * depth);
                x = Math.Clamp(sliceIndex, 0, width - 1);
                break;
            default:
                return -1;
        }

        if (x < 0 || x >= width || y < 0 || y >= height || z < 0 || z >= depth)
            return -1;

        return aoField[x, y, z];
    }

    private static bool GetMaskValue(bool[,,] mask, int imgX, int imgY, int sliceIndex, int viewIndex,
        int width, int height, int depth, int imageWidth, int imageHeight)
    {
        int x, y, z;

        switch (viewIndex)
        {
            case 0: // XY view
                x = (int)((float)imgX / imageWidth * width);
                y = (int)((float)imgY / imageHeight * height);
                z = Math.Clamp(sliceIndex, 0, depth - 1);
                break;
            case 1: // XZ view
                x = (int)((float)imgX / imageWidth * width);
                z = (int)((float)imgY / imageHeight * depth);
                y = Math.Clamp(sliceIndex, 0, height - 1);
                break;
            case 2: // YZ view
                y = (int)((float)imgX / imageWidth * height);
                z = (int)((float)imgY / imageHeight * depth);
                x = Math.Clamp(sliceIndex, 0, width - 1);
                break;
            default:
                return false;
        }

        if (x < 0 || x >= width || y < 0 || y >= height || z < 0 || z >= depth)
            return false;

        return mask[x, y, z];
    }

    private static Vector4 GetHeatmapColor(float value, float alpha)
    {
        // Blue -> Cyan -> Green -> Yellow -> Red heatmap
        value = Math.Clamp(value, 0f, 1f);

        float r, g, b;

        if (value < 0.25f)
        {
            // Blue to Cyan
            float t = value / 0.25f;
            r = 0;
            g = t;
            b = 1;
        }
        else if (value < 0.5f)
        {
            // Cyan to Green
            float t = (value - 0.25f) / 0.25f;
            r = 0;
            g = 1;
            b = 1 - t;
        }
        else if (value < 0.75f)
        {
            // Green to Yellow
            float t = (value - 0.5f) / 0.25f;
            r = t;
            g = 1;
            b = 0;
        }
        else
        {
            // Yellow to Red
            float t = (value - 0.75f) / 0.25f;
            r = 1;
            g = 1 - t;
            b = 0;
        }

        return new Vector4(r, g, b, alpha);
    }
}

/// <summary>
/// Preview state for ambient occlusion segmentation
/// </summary>
public class AmbientOcclusionPreview
{
    public AmbientOcclusionResult Result { get; set; }
    public bool ShowPreview { get; set; } = true;
    public bool ShowAoField { get; set; } = false;
    public bool ShowSegmentationMask { get; set; } = true;
    public bool ShowStatistics { get; set; } = true;
    public Vector4 OverlayColor { get; set; } = new Vector4(0, 1, 0, 0.5f);
    public float OverlayOpacity { get; set; } = 0.5f;
}
