// GeoscientistToolkit/Data/CtImageStack/CtVolume3DControlPanel.cs

using System.Numerics;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.CtImageStack;

public class CtVolume3DControlPanel : BasePanel
{
    private readonly CtImageStackDataset _dataset;
    private readonly ImGuiExportFileDialog _exportModelDialog;

    private readonly ImGuiExportFileDialog _screenshotDialog;
    private readonly CtVolume3DViewer _viewer;

    private int _exportFileFormat;
    private string _newPlaneName = "Plane";
    private int _selectedPlaneIndex = -1;

    private int _meshIsoValue = 128;
    private int _meshStepSize = 2;
    private bool _isExporting;
    private float _exportProgress;
    private string _exportStatus = "";

    public CtVolume3DControlPanel(CtVolume3DViewer viewer, CtImageStackDataset dataset)
        : base("3D Volume Controls", new Vector2(400, 600))
    {
        _viewer = viewer;
        _dataset = dataset;

        _screenshotDialog = new ImGuiExportFileDialog("ScreenshotDialog", "Save Screenshot");
        _screenshotDialog.SetExtensions((".png", "PNG Image"));
        _exportModelDialog = new ImGuiExportFileDialog("ExportModelDialog", "Export 3D Model");
        _exportModelDialog.SetExtensions((".obj", "Wavefront OBJ"), (".stl", "STL Mesh"));
    }

    protected override void DrawContent()
    {
        if (ImGui.BeginTabBar("ControlTabs"))
        {
            if (ImGui.BeginTabItem("Rendering"))
            {
                DrawRenderingTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Materials"))
            {
                DrawMaterialsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Slicing"))
            {
                DrawSlicingTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Export"))
            {
                DrawExportTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        HandleFileDialogs();
    }

    private void DrawRenderingTab()
    {
        ImGui.Text("Volume Rendering Controls");
        ImGui.Separator();

        ImGui.Text("Rendering Quality");
        ImGui.SliderFloat("Step Size", ref _viewer.StepSize, 0.1f, 5.0f, "%.2f");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Lower values = higher quality but slower rendering.");

        ImGui.Spacing();

        ImGui.Text("Grayscale Threshold");
        ImGui.DragFloatRange2("Range", ref _viewer.MinThreshold, ref _viewer.MaxThreshold, 0.005f, 0.0f, 1.0f,
            "Min: %.3f", "Max: %.3f");

        ImGui.Spacing();

        ImGui.Text("Display Options");
        ImGui.Checkbox("Show Grayscale Volume", ref _viewer.ShowGrayscale);

        ImGui.Text("Color Map");
        string[] colorMaps = { "Grayscale", "Hot", "Cool", "Rainbow" };
        ImGui.Combo("##ColorMap", ref _viewer.ColorMapIndex, colorMaps, colorMaps.Length);
    }

    private void DrawMaterialsTab()
    {
        ImGui.Text("Material Visibility & Opacity");
        ImGui.Separator();

        if (_dataset.Materials.Count <= 1)
        {
            ImGui.TextDisabled("No material labels loaded for this dataset.");
            return;
        }

        if (ImGui.Button("Show All")) _viewer.SetAllMaterialsVisibility(true);
        ImGui.SameLine();
        if (ImGui.Button("Hide All")) _viewer.SetAllMaterialsVisibility(false);
        ImGui.SameLine();
        if (ImGui.Button("Reset Opacity")) _viewer.ResetAllMaterialOpacities();

        ImGui.Spacing();

        if (ImGui.BeginChild("MaterialList", new Vector2(0, -50), ImGuiChildFlags.Border))
            foreach (var material in _dataset.Materials)
            {
                if (material.ID == 0) continue;

                ImGui.PushID(material.ID);

                var color = material.Color;
                ImGui.ColorButton("##color", color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha,
                    new Vector2(20, 20));
                ImGui.SameLine();

                var visible = _viewer.GetMaterialVisibility(material.ID);
                if (ImGui.Checkbox("##visible", ref visible)) _viewer.SetMaterialVisibility(material.ID, visible);
                ImGui.SameLine();
                ImGui.Text(material.Name);

                if (visible)
                {
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(100);
                    var opacity = _viewer.GetMaterialOpacity(material.ID);
                    if (ImGui.SliderFloat("##opacity", ref opacity, 0.0f, 1.0f, "%.2f"))
                        _viewer.SetMaterialOpacity(material.ID, opacity);
                }

                ImGui.PopID();
            }

        ImGui.EndChild();
    }

    private void DrawSlicingTab()
    {
        ImGui.Text("Volume Slicing Controls");
        ImGui.Separator();

        // Visual aid toggle
        var showPlaneVis = _viewer.ShowPlaneVisualizations;
        if (ImGui.Checkbox("Show Plane Visualizations", ref showPlaneVis))
            _viewer.ShowPlaneVisualizations = showPlaneVis;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Toggle visual representation of cutting and clipping planes in the 3D view");

        ImGui.Separator();

        // Orthogonal Slices
        if (ImGui.CollapsingHeader("Orthogonal Slices", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Checkbox("Enable Orthogonal Slices", ref _viewer.ShowSlices);

            if (_viewer.ShowSlices)
            {
                ImGui.Spacing();
                ImGui.Indent();
                ImGui.Text("X Slice (Red)");
                ImGui.SliderFloat("##xslice", ref _viewer.SlicePositions.X, 0.0f, 1.0f, "%.3f");
                ImGui.Text("Y Slice (Green)");
                ImGui.SliderFloat("##yslice", ref _viewer.SlicePositions.Y, 0.0f, 1.0f, "%.3f");
                ImGui.Text("Z Slice (Blue)");
                ImGui.SliderFloat("##zslice", ref _viewer.SlicePositions.Z, 0.0f, 1.0f, "%.3f");
                ImGui.Spacing();
                if (ImGui.Button("Reset to Center")) _viewer.SlicePositions = new Vector3(0.5f);
                ImGui.Unindent();
            }
        }

        // Axis-Aligned Cutting Planes
        if (ImGui.CollapsingHeader("Axis-Aligned Cutting Planes", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // X Cutting Plane
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.3f, 0.3f, 1));
            ImGui.Text("X Cutting Plane");
            ImGui.PopStyleColor();

            ImGui.Checkbox("Enable X Cut", ref _viewer.CutXEnabled);
            if (_viewer.CutXEnabled)
            {
                ImGui.Indent();
                ImGui.SliderFloat("Position##X", ref _viewer.CutXPosition, 0.0f, 1.0f, "%.3f");
                var forward = _viewer.CutXForward;
                if (ImGui.Checkbox("Cut Forward##X", ref forward))
                    _viewer.CutXForward = forward;
                ImGui.SameLine();
                if (ImGui.Button("Mirror##X"))
                    _viewer.CutXForward = !_viewer.CutXForward;
                ImGui.Unindent();
            }

            ImGui.Spacing();

            // Y Cutting Plane
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1, 0.3f, 1));
            ImGui.Text("Y Cutting Plane");
            ImGui.PopStyleColor();

            ImGui.Checkbox("Enable Y Cut", ref _viewer.CutYEnabled);
            if (_viewer.CutYEnabled)
            {
                ImGui.Indent();
                ImGui.SliderFloat("Position##Y", ref _viewer.CutYPosition, 0.0f, 1.0f, "%.3f");
                var forward = _viewer.CutYForward;
                if (ImGui.Checkbox("Cut Forward##Y", ref forward))
                    _viewer.CutYForward = forward;
                ImGui.SameLine();
                if (ImGui.Button("Mirror##Y"))
                    _viewer.CutYForward = !_viewer.CutYForward;
                ImGui.Unindent();
            }

            ImGui.Spacing();

            // Z Cutting Plane
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.3f, 1, 1));
            ImGui.Text("Z Cutting Plane");
            ImGui.PopStyleColor();

            ImGui.Checkbox("Enable Z Cut", ref _viewer.CutZEnabled);
            if (_viewer.CutZEnabled)
            {
                ImGui.Indent();
                ImGui.SliderFloat("Position##Z", ref _viewer.CutZPosition, 0.0f, 1.0f, "%.3f");
                var forward = _viewer.CutZForward;
                if (ImGui.Checkbox("Cut Forward##Z", ref forward))
                    _viewer.CutZForward = forward;
                ImGui.SameLine();
                if (ImGui.Button("Mirror##Z"))
                    _viewer.CutZForward = !_viewer.CutZForward;
                ImGui.Unindent();
            }

            ImGui.Unindent();
        }

        // Arbitrary Clipping Planes
        if (ImGui.CollapsingHeader("Arbitrary Clipping Planes", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // Add new plane
            ImGui.InputText("Name", ref _newPlaneName, 50);
            ImGui.SameLine();
            if (ImGui.Button("Add Plane"))
            {
                _viewer.ClippingPlanes.Add(new ClippingPlane(_newPlaneName));
                _newPlaneName = $"Plane {_viewer.ClippingPlanes.Count + 1}";
            }

            ImGui.Separator();

            // List of clipping planes
            if (_viewer.ClippingPlanes.Count == 0)
            {
                ImGui.TextDisabled("No clipping planes defined.");
                ImGui.TextWrapped("Click 'Add Plane' to create a new clipping plane.");
            }
            else
            {
                // Quick actions
                if (ImGui.Button("Enable All"))
                    foreach (var plane in _viewer.ClippingPlanes)
                        plane.Enabled = true;
                ImGui.SameLine();
                if (ImGui.Button("Disable All"))
                    foreach (var plane in _viewer.ClippingPlanes)
                        plane.Enabled = false;
                ImGui.SameLine();
                if (ImGui.Button("Clear All"))
                {
                    _viewer.ClippingPlanes.Clear();
                    _selectedPlaneIndex = -1;
                }

                ImGui.Spacing();

                // Plane list
                if (ImGui.BeginChild("PlaneList", new Vector2(0, 150), ImGuiChildFlags.Border))
                    for (var i = 0; i < _viewer.ClippingPlanes.Count; i++)
                    {
                        var plane = _viewer.ClippingPlanes[i];
                        ImGui.PushID(i);

                        var isSelected = _selectedPlaneIndex == i;
                        if (ImGui.Selectable($"{plane.Name}##plane", isSelected)) _selectedPlaneIndex = i;

                        ImGui.SameLine();
                        ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X - 80);

                        var enabled = plane.Enabled;
                        if (ImGui.Checkbox("##enabled", ref enabled)) plane.Enabled = enabled;

                        ImGui.SameLine();
                        if (ImGui.SmallButton("X"))
                        {
                            _viewer.ClippingPlanes.RemoveAt(i);
                            if (_selectedPlaneIndex >= _viewer.ClippingPlanes.Count)
                                _selectedPlaneIndex = _viewer.ClippingPlanes.Count - 1;
                            ImGui.PopID();
                            break;
                        }

                        ImGui.PopID();
                    }

                ImGui.EndChild();

                // Edit selected plane
                if (_selectedPlaneIndex >= 0 && _selectedPlaneIndex < _viewer.ClippingPlanes.Count)
                {
                    ImGui.Separator();
                    ImGui.Text("Edit Clipping Plane");

                    var plane = _viewer.ClippingPlanes[_selectedPlaneIndex];

                    ImGui.PushID(_selectedPlaneIndex);

                    // Name
                    var name = plane.Name;
                    if (ImGui.InputText("Name", ref name, 50)) plane.Name = name;

                    // Rotation controls
                    ImGui.Text("Orientation (Euler Angles):");
                    var rotationChanged = false;

                    var rotation = plane.Rotation;
                    if (ImGui.DragFloat("Pitch (X)", ref rotation.X, 1.0f, -180, 180, "%.0f°"))
                        rotationChanged = true;
                    if (ImGui.DragFloat("Yaw (Y)", ref rotation.Y, 1.0f, -180, 180, "%.0f°"))
                        rotationChanged = true;
                    if (ImGui.DragFloat("Roll (Z)", ref rotation.Z, 1.0f, -180, 180, "%.0f°"))
                        rotationChanged = true;

                    if (rotationChanged)
                    {
                        plane.Rotation = rotation;
                        _viewer.UpdateClippingPlaneNormal(plane);
                    }

                    // Position
                    ImGui.Text("Distance from Center:");
                    var distance = plane.Distance;
                    if (ImGui.SliderFloat("##distance", ref distance, -0.5f, 1.5f, "%.3f")) plane.Distance = distance;

                    // Mirror
                    var mirror = plane.Mirror;
                    if (ImGui.Checkbox("Mirror (Cut Opposite Side)", ref mirror)) plane.Mirror = mirror;

                    // Presets
                    ImGui.Spacing();
                    ImGui.Text("Quick Presets:");
                    if (ImGui.Button("XY", new Vector2(40, 0)))
                    {
                        plane.Rotation = new Vector3(0, 0, 0);
                        _viewer.UpdateClippingPlaneNormal(plane);
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("XZ", new Vector2(40, 0)))
                    {
                        plane.Rotation = new Vector3(-90, 0, 0);
                        _viewer.UpdateClippingPlaneNormal(plane);
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("YZ", new Vector2(40, 0)))
                    {
                        plane.Rotation = new Vector3(0, 90, 0);
                        _viewer.UpdateClippingPlaneNormal(plane);
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Diagonal", new Vector2(60, 0)))
                    {
                        plane.Rotation = new Vector3(-45, 45, 0);
                        _viewer.UpdateClippingPlaneNormal(plane);
                    }

                    ImGui.PopID();
                }
            }

            ImGui.Unindent();
        }
    }

    private void DrawExportTab()
    {
        ImGui.Text("Export Options");
        ImGui.Separator();

        ImGui.Text("Screenshot");
        if (ImGui.Button("Save Screenshot..."))
            _screenshotDialog.Open("screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("3D Mesh Export (Marching Cubes)");

        ImGui.SliderInt("Iso Value", ref _meshIsoValue, 1, 255);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Density threshold for surface extraction");

        ImGui.SliderInt("Step Size", ref _meshStepSize, 1, 8);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Larger values = faster but less detailed mesh");

        ImGui.Spacing();
        ImGui.RadioButton("OBJ", ref _exportFileFormat, 0);
        ImGui.SameLine();
        ImGui.RadioButton("STL", ref _exportFileFormat, 1);

        ImGui.BeginDisabled(_isExporting);
        if (ImGui.Button("Export 3D Model..."))
            _exportModelDialog.Open("model_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        ImGui.EndDisabled();

        if (_isExporting)
        {
            ImGui.ProgressBar(_exportProgress, new Vector2(-1, 0), _exportStatus);
        }
    }

    private void HandleFileDialogs()
    {
        if (_screenshotDialog.Submit()) _viewer.SaveScreenshot(_screenshotDialog.SelectedPath);

        if (_exportModelDialog.Submit())
        {
            var outputPath = _exportModelDialog.SelectedPath;
            _ = ExportMeshAsync(outputPath);
        }
    }

    private async Task ExportMeshAsync(string outputPath)
    {
        if (_dataset.Volume == null)
        {
            Logger.LogError("[CtVolume3DControlPanel] No volume data available for mesh export");
            return;
        }

        _isExporting = true;
        _exportProgress = 0;
        _exportStatus = "Starting mesh generation...";

        try
        {
            var mesher = new MarchingCubesMesher();
            var progress = new Progress<(float progress, string message)>(p =>
            {
                _exportProgress = p.progress;
                _exportStatus = p.message;
            });

            var (vertices, indices) = await mesher.GenerateMeshAsync(
                _dataset.Volume,
                (byte)_meshIsoValue,
                _meshStepSize,
                progress);

            if (vertices.Count == 0)
            {
                Logger.LogWarning("[CtVolume3DControlPanel] No mesh generated - try adjusting the iso value");
                return;
            }

            float scale = _dataset.PixelSize;
            if (_exportFileFormat == 0)
                MarchingCubesMesher.ExportToObj(outputPath, vertices, indices, scale);
            else
                MarchingCubesMesher.ExportToStl(outputPath, vertices, indices, scale);

            Logger.Log($"[CtVolume3DControlPanel] Exported mesh to '{outputPath}' ({vertices.Count} vertices, {indices.Count / 3} triangles)");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CtVolume3DControlPanel] Mesh export failed: {ex.Message}");
        }
        finally
        {
            _isExporting = false;
            _exportStatus = "";
        }
    }
}