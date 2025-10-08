// GeoscientistToolkit/Data/CtImageStack/CtSegmentationIntegration.cs

using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack.Segmentation;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.CtImageStack;

public class CtSegmentationIntegration : IDisposable
{
    private static readonly Dictionary<CtImageStackDataset, CtSegmentationIntegration> _activeInstances = new();

    private readonly BrushTool _brushTool;
    private readonly InterpolationManager _interpolationManager;
    private readonly LassoTool _lassoTool;
    private readonly MagicWandTool _magicWandTool;
    private readonly SegmentationManager _segmentationManager;
    private byte[] _currentPreviewMask;
    private int _currentPreviewSlice;
    private int _currentPreviewView;

    private bool _interpolationMode;
    private byte[] _interpolationStartMask;
    private int _interpolationStartSlice;
    private int _interpolationStartView;

    private CtSegmentationIntegration(CtImageStackDataset dataset)
    {
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
            if (!hasSelections) ImGui.BeginDisabled();
            if (ImGui.Button("Add Selections to Material", new Vector2(-1, 0)))
            {
                _segmentationManager.IsAddMode = true;
                _ = _segmentationManager.ApplyActiveSelectionsToVolumeAsync();
            }

            if (ImGui.Button("Remove Selections from Material", new Vector2(-1, 0)))
            {
                _segmentationManager.IsAddMode = false;
                _ = _segmentationManager.ApplyActiveSelectionsToVolumeAsync();
            }

            if (!hasSelections) ImGui.EndDisabled();
            if (ImGui.Button("Clear All Unapplied Selections", new Vector2(-1, 0)))
                _segmentationManager.ClearActiveSelections();
            ImGui.Separator();
            DrawInterpolationControls(selectedMaterial);
            ImGui.Separator();
            if (ImGui.Button("Undo")) _segmentationManager.Undo();
            ImGui.SameLine();
            if (ImGui.Button("Redo")) _segmentationManager.Redo();
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

    private void DrawBrushIcon(ImDrawListPtr dl, Vector2 p, Vector2 s, uint c, bool a)
    {
        var h1 = p + new Vector2(s.X * 0.3f, s.Y * 0.7f);
        var h2 = p + new Vector2(s.X * 0.7f, s.Y * 0.3f);
        dl.AddLine(h1, h2, c, 3f);
        var hc = p + new Vector2(s.X * 0.25f, s.Y * 0.25f);
        dl.AddRectFilled(hc, hc + new Vector2(s.X * 0.2f, s.Y * 0.4f), c);
    }

    private void DrawLassoIcon(ImDrawListPtr dl, Vector2 p, Vector2 s, uint c, bool a)
    {
        var p1 = p + new Vector2(s.X * 0.2f, s.Y * 0.2f);
        var p2 = p + new Vector2(s.X * 0.8f, s.Y * 0.3f);
        var p3 = p + new Vector2(s.X * 0.7f, s.Y * 0.8f);
        var p4 = p + new Vector2(s.X * 0.3f, s.Y * 0.7f);
        dl.AddPolyline(ref p1, 4, c, ImDrawFlags.Closed, 2f);
    }

    private void DrawWandIcon(ImDrawListPtr dl, Vector2 p, Vector2 s, uint c, bool a)
    {
        var p1 = p + new Vector2(s.X * 0.2f, s.Y * 0.8f);
        var p2 = p + new Vector2(s.X * 0.8f, s.Y * 0.2f);
        dl.AddLine(p1, p2, c, 3f);
        for (var i = 0; i < 5; i++)
        {
            var ang = i * (MathF.PI * 2 / 5f) - MathF.PI / 2;
            var o = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * s.X * 0.15f;
            dl.AddLine(p2, p2 + o, c, 1.5f);
        }
    }

    private void DrawOffIcon(ImDrawListPtr dl, Vector2 p, Vector2 s, uint c, bool a)
    {
        dl.AddLine(p + s * 0.2f, p + s * 0.8f, c, 2.5f);
        dl.AddLine(p + new Vector2(s.X * 0.2f, s.Y * 0.8f), p + new Vector2(s.X * 0.8f, s.Y * 0.2f), c, 2.5f);
    }

    #endregion
}