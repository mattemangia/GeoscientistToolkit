// GAIA/Data/CtImageStack/CtSegmentationIntegration.cs

using System.Numerics;
using System.Threading.Tasks;
using GAIA.Business;
using GAIA.Data.CtImageStack.Segmentation;
using GAIA.Util;
using ImGuiNET;

namespace GAIA.Data.CtImageStack;

public class CtSegmentationIntegration : IDisposable
{
    private static readonly Dictionary<CtImageStackDataset, CtSegmentationIntegration> _activeInstances = new();

    private readonly BrushTool _brushTool;
    private readonly InterpolationManager _interpolationManager;
    private readonly LassoTool _lassoTool;
    private readonly MagicWandTool _magicWandTool;
    private readonly SegmentationManager _segmentationManager;
    private readonly CtImageStackDataset _dataset;

    // Growth cap for a single 3D flood so a click on a huge/background region cannot run unbounded.
    private const int MaxWand3DVoxels = 80_000_000;

    private CtOperationHandle _applyOperation;
    private CtOperationHandle _wand3DOperation;
    private bool _subtractStroke;
    private byte[] _currentPreviewMask;
    private int _currentPreviewSlice;
    private int _currentPreviewView;

    private bool _interpolationMode;
    private byte[] _interpolationStartMask;
    private int _interpolationStartSlice;
    private int _interpolationStartView;

    private CtSegmentationIntegration(CtImageStackDataset dataset)
    {
        _dataset = dataset;
        _segmentationManager = new SegmentationManager(dataset);
        _interpolationManager = new InterpolationManager(dataset, _segmentationManager);
        _brushTool = new BrushTool();
        _lassoTool = new LassoTool();
        _magicWandTool = new MagicWandTool();
        _segmentationManager.SelectionPreviewChanged += OnSelectionPreviewChanged;
        _segmentationManager.SelectionCompleted += OnSelectionCompleted;
    }

    public bool HasActiveSelection => ActiveTool?.HasActiveSelection ?? false;
    public ISegmentationTool ActiveTool { get; private set; }

    public byte TargetMaterialId
    {
        get => _segmentationManager.TargetMaterialId;
        set => _segmentationManager.TargetMaterialId = value;
    }

    public void Dispose()
    {
        _brushTool?.Dispose();
        _lassoTool?.Dispose();
        _magicWandTool?.Dispose();
        _segmentationManager?.Dispose();
    }

    public static void Initialize(CtImageStackDataset d)
    {
        if (!_activeInstances.ContainsKey(d)) _activeInstances[d] = new CtSegmentationIntegration(d);
    }

    public static CtSegmentationIntegration GetInstance(CtImageStackDataset d)
    {
        _activeInstances.TryGetValue(d, out var i);
        return i;
    }

    public static void Cleanup(CtImageStackDataset d)
    {
        if (_activeInstances.TryGetValue(d, out var i))
        {
            i.Dispose();
            _activeInstances.Remove(d);
        }
    }

    public void DrawSegmentationControls(Material selectedMaterial)
    {
        if (ImGui.CollapsingHeader("Interactive Segmentation", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            ImGui.Text("Select Tool:");
            var buttonSize = new Vector2(36, 36);
            DrawToolButton(_brushTool, "Brush (B)", buttonSize, DrawBrushIcon);
            ImGui.SameLine();
            DrawToolButton(_lassoTool, "Lasso (L)", buttonSize, DrawLassoIcon);
            ImGui.SameLine();
            DrawToolButton(_magicWandTool, "Magic Wand (W)", buttonSize, DrawWandIcon);
            ImGui.SameLine();
            DrawToolButton(null, "Turn Off Tool (Esc)", buttonSize, DrawOffIcon);
            if (ActiveTool != null)
                ImGui.TextColored(new Vector4(0.7f, 0.85f, 1f, 1f),
                    "Left mouse = add to selection · Right mouse = remove");
            ImGui.Separator();
            if (selectedMaterial != null)
            {
                _segmentationManager.TargetMaterialId = selectedMaterial.ID;
                ImGui.Text($"Target: {selectedMaterial.Name}");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "Select a material to enable tools.");
            }

            if (selectedMaterial == null) ImGui.BeginDisabled();
            DrawToolControls();
            if (selectedMaterial == null) ImGui.EndDisabled();
            ImGui.Separator();
            ImGui.Text("Actions:");
            var hasSelections = _segmentationManager.HasActiveSelections;
            if (!hasSelections || _applyOperation?.IsActive == true) ImGui.BeginDisabled();
            if (ImGui.Button("Add Selections to Material", new Vector2(-1, 0)))
            {
                _segmentationManager.IsAddMode = true;
                _applyOperation = CtOperationCoordinator.For(_dataset).Enqueue("Applying selections",
                    (token, progress) => _segmentationManager.ApplyActiveSelectionsToVolumeAsync(token, progress));
            }

            if (ImGui.Button("Remove Selections from Material", new Vector2(-1, 0)))
            {
                _segmentationManager.IsAddMode = false;
                _applyOperation = CtOperationCoordinator.For(_dataset).Enqueue("Removing selections",
                    (token, progress) => _segmentationManager.ApplyActiveSelectionsToVolumeAsync(token, progress));
            }

            if (!hasSelections || _applyOperation?.IsActive == true) ImGui.EndDisabled();
            if (_applyOperation?.IsActive == true)
            {
                ImGui.ProgressBar(_applyOperation.Progress, new Vector2(-1, 0), _applyOperation.Name);
                if (ImGui.Button("Cancel selection operation", new Vector2(-1, 0))) _applyOperation.Cancel();
            }
            if (ImGui.Button("Clear All Unapplied Selections", new Vector2(-1, 0)))
                _segmentationManager.ClearActiveSelections();
            ImGui.Separator();
            DrawInterpolationControls(selectedMaterial);
            ImGui.Separator();
            if (_applyOperation?.IsActive == true) ImGui.BeginDisabled();
            if (ImGui.Button("Undo"))
                _applyOperation = CtOperationCoordinator.For(_dataset).Enqueue("Undo segmentation",
                    (token, progress) => _segmentationManager.UndoAsync(token, progress));
            ImGui.SameLine();
            if (ImGui.Button("Redo"))
                _applyOperation = CtOperationCoordinator.For(_dataset).Enqueue("Redo segmentation",
                    (token, progress) => _segmentationManager.RedoAsync(token, progress));
            if (_applyOperation?.IsActive == true) ImGui.EndDisabled();
            ImGui.Unindent();
        }
    }

    private void DrawToolControls()
    {
        if (ActiveTool is BrushTool brush)
        {
            ImGui.Text("Brush Settings:");
            var brushSize = brush.BrushSize;
            if (ImGui.SliderFloat("Size", ref brushSize, 1.0f, 200.0f, "%.0f")) brush.BrushSize = brushSize;
            var hardness = brush.Hardness;
            if (ImGui.SliderFloat("Hardness", ref hardness, 0.1f, 1.0f, "%.2f")) brush.Hardness = hardness;
        }
        else if (ActiveTool is MagicWandTool wand)
        {
            ImGui.Text("Magic Wand Settings:");
            ImGui.Text("Mode:");
            ImGui.SameLine();
            if (ImGui.RadioButton("2D slice", !wand.Use3D)) wand.Use3D = false;
            ImGui.SameLine();
            if (ImGui.RadioButton("3D object", wand.Use3D)) wand.Use3D = true;

            int tolerance = wand.Tolerance;
            if (ImGui.SliderInt("Tolerance", ref tolerance, 0, 100)) wand.Tolerance = (byte)tolerance;

            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), wand.Use3D
                ? "Left-click an object to select it across every slice; right-click to remove it."
                : "Left-click a region to select it on this slice; right-click to remove it.");

            if (_wand3DOperation?.IsActive == true)
            {
                ImGui.ProgressBar(_wand3DOperation.Progress, new Vector2(-1, 0), _wand3DOperation.Name);
                if (ImGui.Button("Cancel 3D wand", new Vector2(-1, 0))) _wand3DOperation.Cancel();
            }
        }
    }

    private void DrawInterpolationControls(Material selectedMaterial)
    {
        ImGui.Text("Interpolation:");
        if (selectedMaterial == null) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Interpolation Mode", ref _interpolationMode)) _interpolationStartMask = null;
        if (selectedMaterial == null) ImGui.EndDisabled();
        if (_interpolationMode)
        {
            ImGui.TextWrapped("Create a selection on a start slice, then another on an end slice to interpolate.");
            if (_interpolationStartMask != null)
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f),
                    $"Start slice defined at: {_interpolationStartSlice}.");
        }
    }

    private void SetActiveTool(ISegmentationTool tool)
    {
        ActiveTool?.CancelSelection();
        ActiveTool = tool;
        _segmentationManager.CurrentTool = tool;
    }

    public void HandleMouseInput(Vector2 mousePos, int sliceIndex, int viewIndex,
        bool leftDown, bool leftDragging, bool leftReleased,
        bool rightDown, bool rightDragging, bool rightReleased)
    {
        if (ActiveTool == null || TargetMaterialId == 0) return;

        // The 3D magic wand is a single-click action that spans the whole volume, so it takes its own
        // path instead of the per-slice start/drag/end cycle. Left click adds the object, right removes.
        if (ActiveTool is MagicWandTool { Use3D: true })
        {
            if (leftDown) StartWand3D(mousePos, sliceIndex, viewIndex, false);
            else if (rightDown) StartWand3D(mousePos, sliceIndex, viewIndex, true);
            return;
        }

        var isDown = leftDown || rightDown;
        var isDragging = leftDragging || rightDragging;
        var isReleased = leftReleased || rightReleased;

        if (isDown)
        {
            // Remember which button started the stroke; the right button carves the selection instead
            // of adding to it. Additive clicks always accumulate — earlier selections are never wiped.
            _subtractStroke = rightDown;
            _segmentationManager.StartSelection(mousePos, sliceIndex, viewIndex);
        }
        else if (isDragging)
        {
            _segmentationManager.UpdateSelection(mousePos);
        }
        else if (isReleased)
        {
            if (_interpolationMode)
            {
                HandleInterpolation(sliceIndex, viewIndex);
                // After handling interpolation, we clear the tool's active selection
                // because the results are already committed to the cache.
                ActiveTool?.CancelSelection();
                _segmentationManager.EndSelection(); // This effectively does nothing now but is good for consistency.
            }
            else
            {
                // EndSelection commits the stroke to the manager's cache (add) or carves it out (subtract).
                _segmentationManager.EndSelection(_subtractStroke);
            }

            _subtractStroke = false;
        }
    }

    // Converts a click on a slice view into an absolute volume voxel for the 3D flood seed.
    private (int x, int y, int z) SeedVoxelFromView(Vector2 mousePos, int sliceIndex, int viewIndex)
    {
        int px = (int)mousePos.X, py = (int)mousePos.Y;
        return viewIndex switch
        {
            0 => (px, py, sliceIndex), // XY (axial): image is (X, Y), slice is Z
            1 => (px, sliceIndex, py), // XZ (coronal): image is (X, Z), slice is Y
            _ => (sliceIndex, px, py)  // YZ (sagittal): image is (Y, Z), slice is X
        };
    }

    private void StartWand3D(Vector2 mousePos, int sliceIndex, int viewIndex, bool subtract)
    {
        if (ActiveTool is not MagicWandTool wand) return;
        if (_wand3DOperation?.IsActive == true) return; // one flood at a time

        var (sx, sy, sz) = SeedVoxelFromView(mousePos, sliceIndex, viewIndex);
        var tolerance = wand.Tolerance;
        _wand3DOperation = CtOperationCoordinator.For(_dataset).Enqueue(
            subtract ? "3D magic wand (remove)" : "3D magic wand (add)",
            (token, progress) => Task.Run(() =>
            {
                var masks = _segmentationManager.Run3DMagicWand(sx, sy, sz, tolerance, MaxWand3DVoxels, token,
                    progress);
                OpenTkManager.ExecuteOnMainThread(() =>
                {
                    if (subtract) _segmentationManager.SubtractMultipleSelectionsFromCache(masks);
                    else _segmentationManager.CommitMultipleSelectionsToCache(masks);
                    // Force every slice view to redraw so the flood is visible immediately.
                    ProjectManager.Instance.NotifyDatasetDataChanged(_dataset);
                });
                Logger.Log($"[Segmentation] 3D magic wand touched {masks.Count} slices " +
                           $"(seed {sx},{sy},{sz}, tol {tolerance}).");
            }, token));
    }


    private void HandleInterpolation(int sliceIndex, int viewIndex)
    {
        // This logic requires a final selection to be active in the tool.
        _segmentationManager.UpdateSelection(_segmentationManager.GetLastMousePosition());
        ActiveTool.EndSelection();

        var currentMask = ActiveTool.GetSelectionMask();
        if (currentMask == null || !ActiveTool.HasActiveSelection) return;
        if (_interpolationStartMask == null)
        {
            _interpolationStartMask = (byte[])currentMask.Clone();
            _interpolationStartSlice = sliceIndex;
            _interpolationStartView = viewIndex;
            Logger.Log($"[Interpolation] Start mask set on slice {sliceIndex}.");

            // Also commit this first selection to the cache so it's visible
            _segmentationManager.CommitSelectionToCache((byte[])currentMask.Clone(), sliceIndex, viewIndex);
        }
        else if (viewIndex == _interpolationStartView && sliceIndex != _interpolationStartSlice)
        {
            Logger.Log($"[Interpolation] End mask set on slice {sliceIndex}. Interpolating...");

            // Commit the end mask as well
            _segmentationManager.CommitSelectionToCache((byte[])currentMask.Clone(), sliceIndex, viewIndex);

            var interpolatedSlices = _interpolationManager.InterpolateSlices(
                _interpolationStartMask, _interpolationStartSlice, currentMask, sliceIndex,
                viewIndex, InterpolationManager.InterpolationType.ShapeInterpolation);

            if (interpolatedSlices.Any()) _segmentationManager.CommitMultipleSelectionsToCache(interpolatedSlices);
            _interpolationStartMask = null;
            _interpolationMode = false;
        }
    }

    private void OnSelectionPreviewChanged(byte[] mask, int slice, int view)
    {
        _currentPreviewMask = mask;
        _currentPreviewSlice = slice;
        _currentPreviewView = view;
    }

    private void OnSelectionCompleted()
    {
        _currentPreviewMask = null;
    }

    public byte[] GetPreviewMask(int slice, int view)
    {
        return _currentPreviewSlice == slice && _currentPreviewView == view ? _currentPreviewMask : null;
    }

    public byte[] GetCommittedSelectionMask(int slice, int view)
    {
        return _segmentationManager.GetActiveSelectionMask(slice, view);
    }

    #region Custom Icon Drawing

    private void DrawToolButton(ISegmentationTool tool, string tooltip, Vector2 size,
        Action<ImDrawListPtr, Vector2, Vector2, uint, bool> drawAction)
    {
        var dl = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var isActive = ActiveTool == tool;
        if (ImGui.InvisibleButton(tooltip, size))
        {
            SetActiveTool(tool);
            isActive = true;
        }

        var color = ImGui.GetColorU32(ImGuiCol.Text);
        var bgColor = ImGui.GetColorU32(ImGuiCol.Button);
        if (isActive)
        {
            bgColor = ImGui.GetColorU32(ImGuiCol.ButtonActive);
            color = ImGui.GetColorU32(ImGuiCol.CheckMark);
        }
        else if (ImGui.IsItemHovered())
        {
            bgColor = ImGui.GetColorU32(ImGuiCol.ButtonHovered);
        }

        dl.AddRectFilled(cursorPos, cursorPos + size, bgColor, 4.0f);
        dl.AddRect(cursorPos, cursorPos + size, ImGui.GetColorU32(ImGuiCol.Border), 4.0f);
        drawAction(dl, cursorPos, size, color, isActive);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
    }

    // A centred square icon area (~58% of the button) with a local fraction-to-pixel mapper, so
    // every glyph is drawn on the same crisp, padded grid regardless of the button size.
    private static (Func<float, float, Vector2> at, float extent) IconGrid(Vector2 p, Vector2 s)
    {
        var extent = MathF.Min(s.X, s.Y) * 0.58f;
        var origin = p + (s - new Vector2(extent)) * 0.5f;
        return ((fx, fy) => origin + new Vector2(fx * extent, fy * extent), extent);
    }

    private static void Sparkle(ImDrawListPtr dl, Vector2 center, float radius, uint c)
    {
        var thickness = MathF.Max(1.2f, radius * 0.32f);
        dl.AddLine(center - new Vector2(radius, 0), center + new Vector2(radius, 0), c, thickness);
        dl.AddLine(center - new Vector2(0, radius), center + new Vector2(0, radius), c, thickness);
    }

    private void DrawBrushIcon(ImDrawListPtr dl, Vector2 p, Vector2 s, uint c, bool a)
    {
        var (at, e) = IconGrid(p, s);
        var handle = MathF.Max(2f, e * 0.12f);
        // Wooden handle, then a slightly thicker ferrule, then a splayed bristle tip.
        dl.AddLine(at(0.88f, 0.10f), at(0.46f, 0.52f), c, handle);
        dl.AddLine(at(0.52f, 0.44f), at(0.36f, 0.60f), c, handle * 1.5f);
        dl.AddTriangleFilled(at(0.46f, 0.50f), at(0.10f, 0.72f), at(0.34f, 0.94f), c);
    }

    private void DrawLassoIcon(ImDrawListPtr dl, Vector2 p, Vector2 s, uint c, bool a)
    {
        var (at, e) = IconGrid(p, s);
        var thickness = MathF.Max(1.8f, e * 0.09f);
        // A rope loop with a dangling tail — reads as a lasso rather than a plain quad.
        dl.AddCircle(at(0.5f, 0.40f), e * 0.30f, c, 28, thickness);
        dl.AddBezierQuadratic(at(0.50f, 0.70f), at(0.40f, 0.92f), at(0.66f, 0.96f), c, thickness, 16);
    }

    private void DrawWandIcon(ImDrawListPtr dl, Vector2 p, Vector2 s, uint c, bool a)
    {
        var (at, e) = IconGrid(p, s);
        // Wand shaft with a bright tip star and two smaller sparkles.
        dl.AddLine(at(0.18f, 0.88f), at(0.60f, 0.44f), c, MathF.Max(2f, e * 0.11f));
        Sparkle(dl, at(0.68f, 0.34f), e * 0.17f, c);
        Sparkle(dl, at(0.90f, 0.60f), e * 0.09f, c);
        Sparkle(dl, at(0.86f, 0.14f), e * 0.08f, c);
    }

    private void DrawOffIcon(ImDrawListPtr dl, Vector2 p, Vector2 s, uint c, bool a)
    {
        var (at, e) = IconGrid(p, s);
        // Default arrow pointer: no active tool.
        dl.AddTriangleFilled(at(0.20f, 0.10f), at(0.20f, 0.74f), at(0.60f, 0.44f), c);
        dl.AddLine(at(0.42f, 0.52f), at(0.58f, 0.86f), c, MathF.Max(2.5f, e * 0.13f));
    }

    #endregion
}
