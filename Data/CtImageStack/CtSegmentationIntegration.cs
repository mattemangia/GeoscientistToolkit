// GAIA/Data/CtImageStack/CtSegmentationIntegration.cs

using System.Numerics;
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
    private CtOperationHandle _applyOperation;
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
            int tolerance = wand.Tolerance;
            if (ImGui.SliderInt("Tolerance", ref tolerance, 0, 100)) wand.Tolerance = (byte)tolerance;
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

    public void HandleMouseInput(Vector2 mousePos, int sliceIndex, int viewIndex, bool isDown, bool isDragging,
        bool isReleased)
    {
        if (ActiveTool == null || TargetMaterialId == 0) return;

        if (isDown)
        {
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
                // This is the key change: EndSelection now commits the mask to the manager's cache.
                _segmentationManager.EndSelection();
            }
        }
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
