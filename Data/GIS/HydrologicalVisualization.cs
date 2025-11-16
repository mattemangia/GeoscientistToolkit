// GeoscientistToolkit/Data/GIS/HydrologicalVisualization.cs
//
// Visualization overlay system for hydrological analysis in GISViewer
//

using System.Numerics;
using GeoscientistToolkit.Analysis.Hydrological;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.GIS.Tools;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
/// Renders hydrological analysis results as overlays on the GIS map
/// </summary>
public class HydrologicalVisualization
{
    private HydrologicalAnalysisToolEnhanced _tool;
    private bool _enabled = false;
    private bool _animate = false;
    private float _animationSpeed = 1.0f;
    private int _animationFrame = 0;
    private float _animationTimer = 0f;

    // Visualization settings
    private bool _showFlowPaths = true;
    private bool _showWatersheds = true;
    private bool _showWaterDepth = true;
    private bool _showWaterBodies = true;
    private float _waterOpacity = 0.6f;
    private float _pathThickness = 2.0f;

    // Colors
    private Vector4 _flowPathColor = new Vector4(0.2f, 0.6f, 1.0f, 1.0f);
    private Vector4 _watershedColor = new Vector4(0.3f, 0.8f, 0.3f, 0.3f);
    private Vector4 _waterShallowColor = new Vector4(0.3f, 0.7f, 1.0f, 0.5f);
    private Vector4 _waterDeepColor = new Vector4(0.0f, 0.2f, 0.8f, 0.8f);

    public void SetTool(HydrologicalAnalysisToolEnhanced tool)
    {
        _tool = tool;
        _enabled = tool != null;
    }

    public bool IsEnabled => _enabled && _tool != null;

    public void Render(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan,
        Func<Vector2, Vector2, Vector2, float, Vector2, Vector2> worldToScreen)
    {
        if (!IsEnabled || _tool?.ElevationLayer == null) return;

        var elevationLayer = _tool.ElevationLayer;
        var bounds = elevationLayer.Bounds;

        // Update animation
        if (_animate && _tool.GetWaterDepth() != null)
        {
            _animationTimer += ImGui.GetIO().DeltaTime * _animationSpeed;
            if (_animationTimer >= 0.1f) // 10 FPS animation
            {
                _animationTimer = 0;
                _animationFrame++;
                // Loop animation
                // This would need access to timestep count
            }
        }

        // Render in order: watershed -> water depth -> water bodies -> flow paths (top)

        // 1. Draw watershed
        if (_showWatersheds && _tool.ShowWatershed)
        {
            var watershed = _tool.GetCurrentWatershed();
            if (watershed != null)
            {
                RenderWatershed(drawList, watershed, bounds, elevationLayer.Width, elevationLayer.Height,
                    canvasPos, canvasSize, zoom, pan, worldToScreen);
            }
        }

        // 2. Draw water depth
        if (_showWaterDepth)
        {
            var waterDepth = _tool.GetWaterDepth();
            if (waterDepth != null)
            {
                RenderWaterDepth(drawList, waterDepth, bounds, elevationLayer.Width, elevationLayer.Height,
                    canvasPos, canvasSize, zoom, pan, worldToScreen);
            }
        }

        // 3. Draw water bodies
        if (_showWaterBodies && _tool.ShowWaterBodies)
        {
            var tracker = _tool.GetWaterBodyTracker();
            if (tracker != null)
            {
                RenderWaterBodies(drawList, tracker, bounds, elevationLayer.Width, elevationLayer.Height,
                    canvasPos, canvasSize, zoom, pan, worldToScreen);
            }
        }

        // 4. Draw flow paths (on top)
        if (_showFlowPaths && _tool.ShowFlowPath)
        {
            var flowPath = _tool.GetCurrentFlowPath();
            if (flowPath != null && flowPath.Count > 0)
            {
                RenderFlowPath(drawList, flowPath, bounds, elevationLayer.Width, elevationLayer.Height,
                    canvasPos, canvasSize, zoom, pan, worldToScreen);
            }
        }
    }

    private void RenderFlowPath(ImDrawListPtr drawList, List<(int row, int col)> flowPath,
        BoundingBox bounds, int width, int height,
        Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan,
        Func<Vector2, Vector2, Vector2, float, Vector2, Vector2> worldToScreen)
    {
        if (flowPath.Count < 2) return;

        float cellWidth = (bounds.Max.X - bounds.Min.X) / width;
        float cellHeight = (bounds.Max.Y - bounds.Min.Y) / height;

        var points = new List<Vector2>();
        foreach (var (row, col) in flowPath)
        {
            // Skip edge cells that are out of bounds
            if (row < 0 || row >= height || col < 0 || col >= width)
                continue;

            float worldX = bounds.Min.X + (col + 0.5f) * cellWidth;
            float worldY = bounds.Min.Y + (row + 0.5f) * cellHeight;
            var worldPos = new Vector2(worldX, worldY);
            var screenPos = worldToScreen(worldPos, canvasPos, canvasSize, zoom, pan);
            points.Add(screenPos);
        }

        // Draw polyline with gradient (source to outlet)
        for (int i = 0; i < points.Count - 1; i++)
        {
            float t = (float)i / (points.Count - 1);
            var color = Vector4.Lerp(_flowPathColor, new Vector4(0.0f, 0.3f, 0.9f, 1.0f), t);
            drawList.AddLine(points[i], points[i + 1], ImGui.GetColorU32(color), _pathThickness);
        }

        // Draw start and end markers
        if (points.Count > 0)
        {
            drawList.AddCircleFilled(points[0], 5, ImGui.GetColorU32(new Vector4(0.3f, 1.0f, 0.3f, 1.0f))); // Start (green)
            drawList.AddCircleFilled(points[points.Count - 1], 5, ImGui.GetColorU32(new Vector4(1.0f, 0.3f, 0.3f, 1.0f))); // End (red)
        }
    }

    private void RenderWatershed(ImDrawListPtr drawList, bool[,] watershed,
        BoundingBox bounds, int width, int height,
        Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan,
        Func<Vector2, Vector2, Vector2, float, Vector2, Vector2> worldToScreen)
    {
        float cellWidth = (bounds.Max.X - bounds.Min.X) / width;
        float cellHeight = (bounds.Max.Y - bounds.Min.Y) / height;

        // Draw watershed cells
        for (int r = 0; r < watershed.GetLength(0); r++)
        {
            for (int c = 0; c < watershed.GetLength(1); c++)
            {
                if (watershed[r, c])
                {
                    float worldX = bounds.Min.X + c * cellWidth;
                    float worldY = bounds.Min.Y + r * cellHeight;

                    var tl = worldToScreen(new Vector2(worldX, worldY), canvasPos, canvasSize, zoom, pan);
                    var br = worldToScreen(new Vector2(worldX + cellWidth, worldY + cellHeight), canvasPos, canvasSize, zoom, pan);

                    drawList.AddRectFilled(tl, br, ImGui.GetColorU32(_watershedColor));
                }
            }
        }
    }

    private void RenderWaterDepth(ImDrawListPtr drawList, float[,] waterDepth,
        BoundingBox bounds, int width, int height,
        Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan,
        Func<Vector2, Vector2, Vector2, float, Vector2, Vector2> worldToScreen)
    {
        float cellWidth = (bounds.Max.X - bounds.Min.X) / width;
        float cellHeight = (bounds.Max.Y - bounds.Min.Y) / height;

        // Find max depth for normalization
        float maxDepth = 0f;
        for (int r = 0; r < waterDepth.GetLength(0); r++)
            for (int c = 0; c < waterDepth.GetLength(1); c++)
                maxDepth = Math.Max(maxDepth, waterDepth[r, c]);

        if (maxDepth < 0.001f) return; // No water

        // Draw water depth cells
        for (int r = 0; r < waterDepth.GetLength(0); r++)
        {
            for (int c = 0; c < waterDepth.GetLength(1); c++)
            {
                float depth = waterDepth[r, c];
                if (depth > 0.01f) // Only render if significant water
                {
                    float worldX = bounds.Min.X + c * cellWidth;
                    float worldY = bounds.Min.Y + r * cellHeight;

                    var tl = worldToScreen(new Vector2(worldX, worldY), canvasPos, canvasSize, zoom, pan);
                    var br = worldToScreen(new Vector2(worldX + cellWidth, worldY + cellHeight), canvasPos, canvasSize, zoom, pan);

                    // Color based on depth (shallow to deep)
                    float t = Math.Min(depth / maxDepth, 1.0f);
                    var color = Vector4.Lerp(_waterShallowColor, _waterDeepColor, t);
                    color.W *= _waterOpacity;

                    drawList.AddRectFilled(tl, br, ImGui.GetColorU32(color));
                }
            }
        }
    }

    private void RenderWaterBodies(ImDrawListPtr drawList, WaterBodyTracker tracker,
        BoundingBox bounds, int width, int height,
        Vector2 canvasPos, Vector2 canvasSize, float zoom, Vector2 pan,
        Func<Vector2, Vector2, Vector2, float, Vector2, Vector2> worldToScreen)
    {
        float cellWidth = (bounds.Max.X - bounds.Min.X) / width;
        float cellHeight = (bounds.Max.Y - bounds.Min.Y) / height;

        foreach (var waterBody in tracker.WaterBodies)
        {
            var color = GetColorForType(waterBody.Type);

            // Draw water body cells
            foreach (var (row, col) in waterBody.Cells)
            {
                if (row >= 0 && row < height && col >= 0 && col < width)
                {
                    float worldX = bounds.Min.X + col * cellWidth;
                    float worldY = bounds.Min.Y + row * cellHeight;

                    var tl = worldToScreen(new Vector2(worldX, worldY), canvasPos, canvasSize, zoom, pan);
                    var br = worldToScreen(new Vector2(worldX + cellWidth, worldY + cellHeight), canvasPos, canvasSize, zoom, pan);

                    drawList.AddRectFilled(tl, br, ImGui.GetColorU32(color));
                }
            }

            // Draw label at centroid
            float centroidWorldX = bounds.Min.X + waterBody.Centroid.X * cellWidth;
            float centroidWorldY = bounds.Min.Y + waterBody.Centroid.Y * cellHeight;
            var centroidScreen = worldToScreen(new Vector2(centroidWorldX, centroidWorldY), canvasPos, canvasSize, zoom, pan);

            var label = $"{waterBody.Type} #{waterBody.Id}";
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(centroidScreen - textSize * 0.5f, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), label);
        }
    }

    private Vector4 GetColorForType(WaterBodyType type)
    {
        return type switch
        {
            WaterBodyType.Lake => new Vector4(0.2f, 0.5f, 0.8f, 0.7f),
            WaterBodyType.River => new Vector4(0.3f, 0.6f, 0.9f, 0.8f),
            WaterBodyType.Sea => new Vector4(0.1f, 0.3f, 0.6f, 0.6f),
            WaterBodyType.Pond => new Vector4(0.4f, 0.7f, 1.0f, 0.6f),
            WaterBodyType.Stream => new Vector4(0.5f, 0.8f, 1.0f, 0.7f),
            _ => new Vector4(0.5f, 0.5f, 1.0f, 0.7f)
        };
    }

    public void DrawControls()
    {
        if (!IsEnabled) return;

        if (ImGui.CollapsingHeader("Hydrological Visualization"))
        {
            ImGui.Checkbox("Show Flow Paths", ref _showFlowPaths);
            ImGui.Checkbox("Show Watersheds", ref _showWatersheds);
            ImGui.Checkbox("Show Water Depth", ref _showWaterDepth);
            ImGui.Checkbox("Show Water Bodies", ref _showWaterBodies);

            ImGui.Separator();

            ImGui.SliderFloat("Water Opacity", ref _waterOpacity, 0f, 1f);
            ImGui.SliderFloat("Path Thickness", ref _pathThickness, 1f, 5f);

            ImGui.Separator();

            ImGui.Checkbox("Animate", ref _animate);
            if (_animate)
            {
                ImGui.SliderFloat("Speed", ref _animationSpeed, 0.1f, 5f);
            }
        }
    }
}
