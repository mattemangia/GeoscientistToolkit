// GAIA/Data/CtImageStack/CtRenderingPanel.cs
// FIXED: Material color changes now trigger 2D slice updates

using System.Numerics;
using GAIA.UI;
using GAIA.Util;
using ImGuiNET;

namespace GAIA.Data.CtImageStack;

/// <summary>
///     Control panel for CT rendering settings in the combined viewer.
///     This panel focuses exclusively on view and rendering adjustments.
/// </summary>
public class CtRenderingPanel : BasePanel
{
    private const int WindowLevelDebounceMs = 200;
    private readonly CtImageStackDataset _dataset;
    private readonly CtCombinedViewer _viewer;
    private int _currentViewMode;
    private int? _pendingSliceX;
    private int? _pendingSliceY;
    private int? _pendingSliceZ;
    private float? _pendingWindowLevel;
    private float? _pendingWindowWidth;
    private long _lastWindowLevelEditMs;

    public CtRenderingPanel(CtCombinedViewer viewer, CtImageStackDataset dataset)
        : base("CT Rendering Controls", new Vector2(400, 600))
    {
        _viewer = viewer;
        _dataset = dataset;
        _currentViewMode = (int)_viewer.ViewMode;

        // Keep the rendering controls in front of the CT viewer, which can otherwise fully cover
        // this floating window and leave no titlebar to click.
        AlwaysOnTop = true;
    }

    protected override void DrawContent()
    {
        if (ImGui.BeginTabBar("RenderingTabs"))
        {
            if (ImGui.BeginTabItem("View Settings"))
            {
                DrawViewSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("3D Rendering"))
            {
                Draw3DSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Materials"))
            {
                DrawMaterialsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Cutting Planes"))
            {
                DrawCuttingPlanesTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    // Add this method to allow inline drawing when popped out
    public void DrawContentInline()
    {
        DrawContent();
    }

    private void DrawViewSettings()
    {
        ImGui.Text("View Mode:");
        ImGui.RadioButton("Combined", ref _currentViewMode, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Slices Only", ref _currentViewMode, 1);
        ImGui.SameLine();
        ImGui.RadioButton("3D Only", ref _currentViewMode, 2);

        if (_currentViewMode != (int)_viewer.ViewMode)
            _viewer.ViewMode = (CtCombinedViewer.ViewModeEnum)_currentViewMode;

        ImGui.Separator();

        ImGui.Text("Display Options:");
        ImGui.Indent();

        var showCrosshairs = _viewer.ShowCrosshairs;
        if (ImGui.Checkbox("Show Crosshairs", ref showCrosshairs))
            _viewer.ShowCrosshairs = showCrosshairs;

        var syncViews = _viewer.SyncViews;
        if (ImGui.Checkbox("Sync Views", ref syncViews))
            _viewer.SyncViews = syncViews;

        var showScaleBar = _viewer.ShowScaleBar;
        if (ImGui.Checkbox("Show Scale Bar", ref showScaleBar))
            _viewer.ShowScaleBar = showScaleBar;

        var showCuttingPlanes = _viewer.ShowCuttingPlanes;
        if (ImGui.Checkbox("Show Cutting Planes", ref showCuttingPlanes))
            _viewer.ShowCuttingPlanes = showCuttingPlanes;

        ImGui.Unindent();

        ImGui.Separator();

        ImGui.Text("Window/Level:");
        var windowLevel = _pendingWindowLevel ?? _viewer.WindowLevel;
        var windowWidth = _pendingWindowWidth ?? _viewer.WindowWidth;

        if (ImGui.DragFloat("Level", ref windowLevel, 1f, 0f, 255f))
        {
            _pendingWindowLevel = windowLevel;
            _lastWindowLevelEditMs = Environment.TickCount64;
        }
        var levelEditFinished = ImGui.IsItemDeactivatedAfterEdit();

        if (ImGui.DragFloat("Width", ref windowWidth, 1f, 1f, 255f))
        {
            _pendingWindowWidth = windowWidth;
            _lastWindowLevelEditMs = Environment.TickCount64;
        }
        var widthEditFinished = ImGui.IsItemDeactivatedAfterEdit();

        if ((_pendingWindowLevel.HasValue || _pendingWindowWidth.HasValue) &&
            (levelEditFinished || widthEditFinished ||
             Environment.TickCount64 - _lastWindowLevelEditMs >= WindowLevelDebounceMs))
        {
            _viewer.SetWindowLevel(_pendingWindowLevel ?? _viewer.WindowLevel,
                _pendingWindowWidth ?? _viewer.WindowWidth);
            _pendingWindowLevel = null;
            _pendingWindowWidth = null;
        }

        var linkThresholds = _viewer.LinkThresholds;
        if (ImGui.Checkbox("Link 3D thresholds", ref linkThresholds))
            _viewer.LinkThresholds = linkThresholds;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Keeps the 3D grayscale thresholds on the same intensity range as this\n" +
                             "window/level, so slices and volume show the same features.\n" +
                             "Min = (Level - Width/2) / 255, Max = (Level + Width/2) / 255.");

        if (ImGui.Button("Auto W/L"))
        {
            _pendingWindowLevel = null;
            _pendingWindowWidth = null;
            _viewer.SetWindowLevel(128, 255);
        }

        ImGui.Separator();

        ImGui.Text("Slice Positions:");
        var sliceX = _pendingSliceX ?? _viewer.SliceX;
        var sliceY = _pendingSliceY ?? _viewer.SliceY;
        var sliceZ = _pendingSliceZ ?? _viewer.SliceZ;

        if (ImGui.SliderInt("X", ref sliceX, 0, _dataset.Width - 1)) _pendingSliceX = sliceX;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _viewer.SliceX = sliceX;
            _pendingSliceX = null;
        }

        if (ImGui.SliderInt("Y", ref sliceY, 0, _dataset.Height - 1)) _pendingSliceY = sliceY;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _viewer.SliceY = sliceY;
            _pendingSliceY = null;
        }

        if (ImGui.SliderInt("Z", ref sliceZ, 0, _dataset.Depth - 1)) _pendingSliceZ = sliceZ;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _viewer.SliceZ = sliceZ;
            _pendingSliceZ = null;
        }

        if (ImGui.Button("Center All"))
        {
            _viewer.SliceX = _dataset.Width / 2;
            _viewer.SliceY = _dataset.Height / 2;
            _viewer.SliceZ = _dataset.Depth / 2;
        }
    }

    private void Draw3DSettings()
    {
        if (_viewer.VolumeViewer == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "3D viewer not available");
            ImGui.TextWrapped("Import dataset with 'Optimized for 3D' option to enable volume rendering.");
            return;
        }

        ImGui.Text("Volume Rendering:");

        var showVolumeData = _viewer.ShowVolumeData;
        if (ImGui.Checkbox("Show Grayscale", ref showVolumeData)) _viewer.ShowVolumeData = showVolumeData;

        var showBoundingBox = _viewer.VolumeViewer.ShowBoundingBox;
        if (ImGui.Checkbox("Show Bounding Box", ref showBoundingBox))
            _viewer.VolumeViewer.ShowBoundingBox = showBoundingBox;

        if (showBoundingBox)
        {
            ImGui.SameLine();
            var showLabels = _viewer.VolumeViewer.ShowBoundingBoxLabels;
            if (ImGui.Checkbox("Coordinates", ref showLabels))
                _viewer.VolumeViewer.ShowBoundingBoxLabels = showLabels;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Annotates the box with the volume extent in voxels and physical units.");
        }

        var showSlices = _viewer.VolumeViewer.ShowSlices;
        if (ImGui.Checkbox("Show Slice Planes (3D crosshair)", ref showSlices))
            _viewer.VolumeViewer.ShowSlices = showSlices;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Draws the slice at the 2D crosshair on each axis:\n" +
                             $"X {CtViewPalette.CrosshairName(0)}, Y {CtViewPalette.CrosshairName(1)}, " +
                             $"Z {CtViewPalette.CrosshairName(2)}.");

        var showSliceOverlay = _viewer.VolumeViewer.ShowSliceOverlay;
        if (ImGui.Checkbox("Slice Overlay on Cuts", ref showSliceOverlay))
            _viewer.VolumeViewer.ShowSliceOverlay = showSliceOverlay;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Textures the exposed face of an enabled cut with the slice read at full\n" +
                             "resolution. The volume body is ray-marched from a reduced LOD, so hairline\n" +
                             "features such as cracks are only visible on these planes.");

        ImGui.Separator();

        var stepSize = _viewer.VolumeStepSize;
        if (ImGui.SliderFloat("Step Size", ref stepSize, 0.5f, 10.0f, "%.1f")) _viewer.VolumeStepSize = stepSize;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Lower values = higher quality but slower performance");

        var minThreshold = _viewer.MinThreshold;
        var maxThreshold = _viewer.MaxThreshold;

        if (ImGui.DragFloatRange2("Threshold", ref minThreshold, ref maxThreshold, 0.01f, 0.0f, 1.0f,
                "Min: %.2f", "Max: %.2f"))
        {
            _viewer.MinThreshold = minThreshold;
            _viewer.MaxThreshold = maxThreshold;
        }

        ImGui.Separator();

        ImGui.Text("Color Map:");
        string[] colorMapNames = { "Grayscale", "Hot", "Cool", "Rainbow" };
        var colorMapIndex = _viewer.ColorMapIndex;
        if (ImGui.Combo("##ColorMap", ref colorMapIndex, colorMapNames, colorMapNames.Length))
            _viewer.ColorMapIndex = colorMapIndex;

        ImGui.Separator();

        if (ImGui.Button("Reset Camera")) _viewer.VolumeViewer?.ResetCamera();

        ImGui.SameLine();

        if (ImGui.Button("Save Screenshot..."))
            // Use the volume viewer's screenshot functionality directly
            if (_viewer.VolumeViewer != null)
            {
                var filename = "volume_screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), filename);
                _viewer.VolumeViewer.SaveScreenshot(path);
                Logger.Log($"Screenshot saved to: {path}");
            }
    }

    private void DrawMaterialsTab()
    {
        ImGui.Text("Material Visibility:");

        if (ImGui.Button("Show All"))
            foreach (var mat in _dataset.Materials.Where(m => m.ID != 0))
                _viewer.SetMaterialVisibility(mat.ID, true);

        ImGui.SameLine();

        if (ImGui.Button("Hide All"))
            foreach (var mat in _dataset.Materials.Where(m => m.ID != 0))
                _viewer.SetMaterialVisibility(mat.ID, false);

        ImGui.Separator();

        if (ImGui.BeginChild("MaterialList", new Vector2(0, -1), ImGuiChildFlags.Border))
            foreach (var material in _dataset.Materials.Where(m => m.ID != 0))
            {
                ImGui.PushID(material.ID);

                var isVisible = _viewer.GetMaterialVisibility(material.ID);
                if (ImGui.Checkbox($"##vis{material.ID}", ref isVisible))
                    _viewer.SetMaterialVisibility(material.ID, isVisible);

                ImGui.SameLine();

                var color = material.Color;
                var color3 = new Vector3(color.X, color.Y, color.Z);
                if (ImGui.ColorEdit3($"##color{material.ID}", ref color3,
                        ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                {
                    material.Color = new Vector4(color3, color.W);
                    // FIXED: Trigger 2D slice updates when color changes
                    _viewer.NotifyMaterialColorChanged();
                    _viewer.VolumeViewer?.SetMaterialVisibility(material.ID, isVisible); // Force 3D update
                }

                ImGui.SameLine();
                ImGui.Text(material.Name);

                if (_viewer.VolumeViewer != null)
                {
                    var opacity = _viewer.GetMaterialOpacity(material.ID);
                    ImGui.SetNextItemWidth(100);
                    if (ImGui.SliderFloat($"##opacity{material.ID}", ref opacity, 0.0f, 1.0f, "%.2f"))
                        // FIXED: SetMaterialOpacity now triggers slice updates in CtCombinedViewer
                        _viewer.SetMaterialOpacity(material.ID, opacity);
                }

                ImGui.PopID();
            }

        ImGui.EndChild();
    }

    private void DrawCuttingPlanesTab()
    {
        if (_viewer.VolumeViewer == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "3D viewer not available");
            return;
        }

        ImGui.Text("Axis-Aligned Cutting Planes:");
        ImGui.Separator();

        // X-axis cutting plane
        var cutXEnabled = _viewer.VolumeViewer.CutXEnabled;
        var cutXPosition = _viewer.VolumeViewer.CutXPosition;
        var cutXForward = _viewer.VolumeViewer.CutXForward;
        var showCutXVisual = _viewer.VolumeViewer.ShowCutXPlaneVisual;

        DrawAxisCuttingPlane("X", ref cutXEnabled, ref cutXPosition, ref cutXForward, ref showCutXVisual);

        _viewer.VolumeViewer.CutXEnabled = cutXEnabled;
        _viewer.VolumeViewer.CutXPosition = cutXPosition;
        _viewer.VolumeViewer.CutXForward = cutXForward;
        _viewer.VolumeViewer.ShowCutXPlaneVisual = showCutXVisual;

        ImGui.Separator();

        // Y-axis cutting plane
        var cutYEnabled = _viewer.VolumeViewer.CutYEnabled;
        var cutYPosition = _viewer.VolumeViewer.CutYPosition;
        var cutYForward = _viewer.VolumeViewer.CutYForward;
        var showCutYVisual = _viewer.VolumeViewer.ShowCutYPlaneVisual;

        DrawAxisCuttingPlane("Y", ref cutYEnabled, ref cutYPosition, ref cutYForward, ref showCutYVisual);

        _viewer.VolumeViewer.CutYEnabled = cutYEnabled;
        _viewer.VolumeViewer.CutYPosition = cutYPosition;
        _viewer.VolumeViewer.CutYForward = cutYForward;
        _viewer.VolumeViewer.ShowCutYPlaneVisual = showCutYVisual;

        ImGui.Separator();

        // Z-axis cutting plane
        var cutZEnabled = _viewer.VolumeViewer.CutZEnabled;
        var cutZPosition = _viewer.VolumeViewer.CutZPosition;
        var cutZForward = _viewer.VolumeViewer.CutZForward;
        var showCutZVisual = _viewer.VolumeViewer.ShowCutZPlaneVisual;

        DrawAxisCuttingPlane("Z", ref cutZEnabled, ref cutZPosition, ref cutZForward, ref showCutZVisual);

        _viewer.VolumeViewer.CutZEnabled = cutZEnabled;
        _viewer.VolumeViewer.CutZPosition = cutZPosition;
        _viewer.VolumeViewer.CutZForward = cutZForward;
        _viewer.VolumeViewer.ShowCutZPlaneVisual = showCutZVisual;

        ImGui.Separator();
        ImGui.Separator();

        // Arbitrary clipping planes
        ImGui.Text("Arbitrary Clipping Planes:");

        if (ImGui.Button("Add Plane"))
            _viewer.VolumeViewer.ClippingPlanes.Add(
                new ClippingPlane($"Plane {_viewer.VolumeViewer.ClippingPlanes.Count + 1}"));

        ImGui.SameLine();

        var showVisualizations = _viewer.VolumeViewer.ShowPlaneVisualizations;
        if (ImGui.Checkbox("Show All Visualizations", ref showVisualizations))
            _viewer.VolumeViewer.ShowPlaneVisualizations = showVisualizations;

        if (_viewer.VolumeViewer.ClippingPlanes.Count > 0)
        {
            ImGui.Separator();

            for (var i = 0; i < _viewer.VolumeViewer.ClippingPlanes.Count; i++)
            {
                var plane = _viewer.VolumeViewer.ClippingPlanes[i];
                ImGui.PushID($"Plane{i}");

                if (ImGui.CollapsingHeader(plane.Name))
                {
                    var enabled = plane.Enabled;
                    if (ImGui.Checkbox("Enabled", ref enabled)) plane.Enabled = enabled;

                    ImGui.SameLine();
                    var showVisualization = plane.IsVisualizationVisible;
                    if (ImGui.Checkbox("Show Visualization", ref showVisualization))
                        plane.IsVisualizationVisible = showVisualization;

                    ImGui.SameLine();
                    if (ImGui.Button("Remove"))
                    {
                        _viewer.VolumeViewer.ClippingPlanes.RemoveAt(i);
                        ImGui.PopID();
                        break;
                    }

                    var distance = plane.Distance;
                    if (ImGui.DragFloat("Distance", ref distance, 0.01f, -1.0f, 1.0f)) plane.Distance = distance;

                    var mirror = plane.Mirror;
                    if (ImGui.Checkbox("Mirror", ref mirror)) plane.Mirror = mirror;

                    var rotation = plane.Rotation;
                    if (ImGui.DragFloat3("Rotation", ref rotation, 1.0f, -180.0f, 180.0f))
                    {
                        plane.Rotation = rotation;
                        _viewer.VolumeViewer.UpdateClippingPlaneNormal(plane);
                    }
                }

                ImGui.PopID();
            }
        }
    }

    private void DrawAxisCuttingPlane(string axis, ref bool enabled, ref float position,
        ref bool forward, ref bool showVisual)
    {
        var index = axis == "X" ? 0 : axis == "Y" ? 1 : 2;
        ImGui.PushStyleColor(ImGuiCol.Text, CtViewPalette.ToVector4(CtViewPalette.Cut(index)));
        ImGui.Checkbox($"{axis}-Axis Cut", ref enabled);
        ImGui.PopStyleColor();

        if (enabled)
        {
            ImGui.SameLine();
            ImGui.Checkbox($"Show##{axis}", ref showVisual);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Outlines the cut plane in {CtViewPalette.CutName(index).ToLowerInvariant()} " +
                                 "and, with \"Slice Overlay on Cuts\", textures its face with the slice.");

            ImGui.DragFloat($"Position##{axis}", ref position, 0.01f, 0.0f, 1.0f);

            if (ImGui.RadioButton($"Forward##{axis}", forward))
                forward = true;
            ImGui.SameLine();
            if (ImGui.RadioButton($"Backward##{axis}", !forward))
                forward = false;
        }
    }
}
