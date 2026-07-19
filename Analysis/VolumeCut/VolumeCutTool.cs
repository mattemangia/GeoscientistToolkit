// GAIA/Analysis/VolumeCut/VolumeCutTool.cs

using System.Numerics;
using GAIA.Business;
using GAIA.Data;
using GAIA.Data.CtImageStack;
using GAIA.UI.Interfaces;
using GAIA.Util;
using ImGuiNET;

namespace GAIA.Analysis.VolumeCut;

/// <summary>
///     CT tool that cuts the volume with an adjustable box, cylinder or sphere. The region is
///     manipulated numerically, with handles on the slice views, and with handles in the 3D
///     viewport. The cut applies out-of-core to the grayscale volume, the labels, or both,
///     keeping either the interior or the exterior of the shape.
/// </summary>
public class VolumeCutTool : IDatasetTools, IDisposable
{
    private CtOperationHandle _applyOperation;
    private CtImageStackDataset _dataset;
    private (int W, int H, int D) _initializedDims;
    private bool _previewRemoved;

    public VolumeCutState State { get; } = new();
    public VolumeCutOverlay Overlay { get; private set; }

    public void Dispose()
    {
        ClearPreview();
        if (_dataset != null) VolumeCutIntegration.UnregisterTool(_dataset);
    }

    public void AttachDataset(CtImageStackDataset dataset)
    {
        var dims = dataset == null ? default : (dataset.Width, dataset.Height, dataset.Depth);
        if (ReferenceEquals(_dataset, dataset) && dims == _initializedDims) return;
        if (!ReferenceEquals(_dataset, dataset))
        {
            ClearPreview();
            _dataset = dataset;
            Overlay = dataset == null ? null : new VolumeCutOverlay(State, dataset);
        }

        // A dataset selected before its volume finishes loading reports 0 dimensions;
        // re-seed the region once the real sizes are known.
        _initializedDims = dims;
        if (dataset != null && dataset.Width > 0 && dataset.Height > 0 && dataset.Depth > 0)
        {
            State.ResetToVolume(dataset.Width, dataset.Height, dataset.Depth);
            State.ClampTo(dataset.Width, dataset.Height, dataset.Depth);
        }
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ctDataset)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "This tool requires a CT Image Stack dataset.");
            return;
        }

        AttachDataset(ctDataset);

        ImGui.TextColored(new Vector4(0.2f, 0.8f, 1.0f, 1), "Volume Cut");
        ImGui.TextWrapped("Cut the dataset with a box, cylinder or sphere. Drag the handles on the " +
                          "slice views or in the 3D viewport to adjust the region.");
        ImGui.Separator();

        DrawShapeSelector(ctDataset);
        ImGui.Separator();
        DrawShapeParameters(ctDataset);
        ImGui.Separator();
        DrawApplySection(ctDataset);
    }

    private void DrawShapeSelector(CtImageStackDataset dataset)
    {
        ImGui.Text("Cut Shape:");
        ImGui.SameLine();
        var shape = (int)State.Shape;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##CutShape", ref shape, "Box\0Cylinder\0Sphere\0"))
        {
            State.Shape = (VolumeCutShapeKind)shape;
            RefreshPreview();
        }

        var showOverlay = State.ShowOverlay;
        if (ImGui.Checkbox("Show handles in viewers", ref showOverlay)) State.ShowOverlay = showOverlay;
        ImGui.SameLine();
        if (ImGui.Button("Reset Region"))
        {
            State.ResetToVolume(dataset.Width, dataset.Height, dataset.Depth);
            RefreshPreview();
        }
    }

    private void DrawShapeParameters(CtImageStackDataset dataset)
    {
        var changed = false;
        switch (State.Shape)
        {
            case VolumeCutShapeKind.Box:
            {
                changed |= DrawRange("X", ref State.BoxMin.X, ref State.BoxMax.X, dataset.Width - 1);
                changed |= DrawRange("Y", ref State.BoxMin.Y, ref State.BoxMax.Y, dataset.Height - 1);
                changed |= DrawRange("Z", ref State.BoxMin.Z, ref State.BoxMax.Z, dataset.Depth - 1);
                break;
            }
            case VolumeCutShapeKind.Sphere:
            {
                changed |= DrawVector("Center", ref State.SphereCenter, dataset);
                var radius = State.SphereRadius;
                ImGui.SetNextItemWidth(-80);
                if (ImGui.DragFloat("Radius##sphere", ref radius, 1f, 1f,
                        Math.Max(dataset.Width, Math.Max(dataset.Height, dataset.Depth))))
                {
                    State.SphereRadius = radius;
                    changed = true;
                }

                break;
            }
            default:
            {
                var axis = State.CylinderAxis;
                ImGui.Text("Axis:");
                ImGui.SameLine();
                if (ImGui.RadioButton("X", axis == 0)) { State.CylinderAxis = 0; changed = true; }
                ImGui.SameLine();
                if (ImGui.RadioButton("Y", axis == 1)) { State.CylinderAxis = 1; changed = true; }
                ImGui.SameLine();
                if (ImGui.RadioButton("Z", axis == 2)) { State.CylinderAxis = 2; changed = true; }

                changed |= DrawVector("Center", ref State.CylinderCenter, dataset);
                var radius = State.CylinderRadius;
                ImGui.SetNextItemWidth(-80);
                if (ImGui.DragFloat("Radius##cyl", ref radius, 1f, 1f,
                        Math.Max(dataset.Width, Math.Max(dataset.Height, dataset.Depth))))
                {
                    State.CylinderRadius = radius;
                    changed = true;
                }

                var axisLength = State.CylinderAxis switch
                {
                    0 => dataset.Width, 1 => dataset.Height, _ => dataset.Depth
                } - 1;
                changed |= DrawRange("Extent", ref State.CylinderAxisMin, ref State.CylinderAxisMax, axisLength);
                break;
            }
        }

        if (changed)
        {
            State.ClampTo(dataset.Width, dataset.Height, dataset.Depth);
            RefreshPreview();
        }
    }

    private static bool DrawRange(string label, ref float min, ref float max, int limit)
    {
        var changed = false;
        int a = (int)min, b = (int)max;
        ImGui.SetNextItemWidth(-80);
        if (ImGui.DragIntRange2($"{label}##range", ref a, ref b, 1, 0, limit))
        {
            min = a;
            max = Math.Max(a + 1, b);
            changed = true;
        }

        return changed;
    }

    private static bool DrawVector(string label, ref Vector3 value, CtImageStackDataset dataset)
    {
        var local = value;
        ImGui.SetNextItemWidth(-80);
        var changed = ImGui.DragFloat3($"{label}##vec", ref local, 1f, 0,
            Math.Max(dataset.Width, Math.Max(dataset.Height, dataset.Depth)));
        if (changed) value = local;
        return changed;
    }

    private void DrawApplySection(CtImageStackDataset dataset)
    {
        ImGui.Text("Keep:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Interior", State.KeepMode == VolumeCutKeepMode.KeepInside))
        {
            State.KeepMode = VolumeCutKeepMode.KeepInside;
            RefreshPreview();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Exterior", State.KeepMode == VolumeCutKeepMode.KeepOutside))
        {
            State.KeepMode = VolumeCutKeepMode.KeepOutside;
            RefreshPreview();
        }

        var grayscale = State.ApplyToGrayscale;
        if (ImGui.Checkbox("Apply to grayscale (BW)", ref grayscale)) State.ApplyToGrayscale = grayscale;
        ImGui.SameLine();
        var labels = State.ApplyToLabels;
        if (ImGui.Checkbox("Apply to labels", ref labels)) State.ApplyToLabels = labels;

        var preview = _previewRemoved;
        if (ImGui.Checkbox("Preview removed region", ref preview))
        {
            _previewRemoved = preview;
            RefreshPreview();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Tints the voxels that the cut will clear. The preview is procedural\n" +
                             "(no mask volume is allocated), so it is instant on any dataset size.");

        ImGui.Spacing();
        var busy = _applyOperation?.IsActive == true;
        var canApply = !busy && (State.ApplyToGrayscale && dataset.VolumeData != null ||
                                 State.ApplyToLabels && dataset.LabelData != null);
        if (!canApply) ImGui.BeginDisabled();
        if (ImGui.Button("Apply Cut", new Vector2(-1, 28))) Apply(dataset);
        if (!canApply) ImGui.EndDisabled();
        ImGui.TextDisabled("The cut modifies the dataset in place. Save/export first if you need the original.");

        if (_applyOperation != null)
        {
            if (_applyOperation.IsActive)
            {
                ImGui.ProgressBar(_applyOperation.Progress, new Vector2(-1, 0), _applyOperation.Name);
                if (ImGui.Button("Cancel cut")) _applyOperation.Cancel();
            }
            else if (_applyOperation.Status == CtOperationStatus.Failed)
            {
                ImGui.TextColored(new Vector4(1f, .35f, .35f, 1f), _applyOperation.Error);
            }
            else if (_applyOperation.Status == CtOperationStatus.Completed)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Cut applied.");
            }
        }
    }

    private void Apply(CtImageStackDataset dataset)
    {
        ClearPreview();
        State.ClampTo(dataset.Width, dataset.Height, dataset.Depth);
        _applyOperation = CtOperationCoordinator.For(dataset).Enqueue("Cutting volume", async (token, progress) =>
        {
            VolumeCutProcessor.Apply(dataset, State, token, progress);
            OpenTkManager.ExecuteOnMainThread(() => ProjectManager.Instance.NotifyDatasetDataChanged(dataset));
        });
    }

    /// <summary>Procedural tint of the voxels the cut would clear; no memory, any dataset size.</summary>
    public void RefreshPreview()
    {
        if (_dataset == null) return;
        if (!_previewRemoved)
        {
            ClearPreview();
            return;
        }

        var state = State;
        var keepInside = state.KeepMode == VolumeCutKeepMode.KeepInside;
        var preview = new FunctionalCtPreviewVolume(_dataset.Width, _dataset.Height, _dataset.Depth,
            (x, y, z) => state.InsideShape(x, y, z) != keepInside ? (byte)255 : (byte)0);
        CtImageStackTools.UpdatePreviewVolumeFromExternal(_dataset, preview, new Vector4(1f, 0.25f, 0.2f, 0.45f));
        _previewRemovedActive = true;
    }

    private void ClearPreview()
    {
        if (_dataset != null && _previewRemovedActive)
            CtImageStackTools.UpdatePreviewVolumeFromExternal(_dataset, null, Vector4.Zero);
        _previewRemovedActive = false;
    }

    private bool _previewRemovedActive;
}
