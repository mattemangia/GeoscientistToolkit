// GAIA/Data/CtImageStack/Segmentation/SegmentationManager.cs

using System.Collections.Concurrent;
using System.Numerics;
using GAIA.Business;
using GAIA.Util;

namespace GAIA.Data.CtImageStack.Segmentation;

public class SegmentationManager : IDisposable
{
    private readonly ConcurrentDictionary<(int slice, int view), byte[]> _activeSelections = new();
    private readonly CtImageStackDataset _dataset;
    private readonly Stack<SegmentationAction> _redoStack = new();
    private readonly Stack<SegmentationAction> _undoStack = new();

    private ISegmentationTool _currentTool;
    private Vector2 _lastMousePosition;

    public SegmentationManager(CtImageStackDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
    }

    public ISegmentationTool CurrentTool
    {
        get => _currentTool;
        set
        {
            _currentTool?.CancelSelection();
            _currentTool?.Dispose();
            _currentTool = value;
            _currentTool?.Initialize(this);
        }
    }

    public byte TargetMaterialId { get; set; } = 1;
    public bool IsAddMode { get; set; } = true;
    public bool HasActiveSelections => !_activeSelections.IsEmpty;

    public void Dispose()
    {
        _currentTool?.Dispose();
        _activeSelections.Clear();
    }

    public event Action<byte[], int, int> SelectionPreviewChanged;
    public event Action SelectionCompleted;

    public CtImageStackDataset GetDataset()
    {
        return _dataset;
    }

    public void StartSelection(Vector2 pos, int slice, int view)
    {
        _lastMousePosition = pos;
        _currentTool?.StartSelection(pos, slice, view);
    }

    public void UpdateSelection(Vector2 pos)
    {
        _lastMousePosition = pos;
        _currentTool?.UpdateSelection(pos);
    }

    public Vector2 GetLastMousePosition()
    {
        return _lastMousePosition;
    }

    public void CancelSelection()
    {
        _currentTool?.CancelSelection();
    }

    public void EndSelection()
    {
        if (_currentTool == null || !_currentTool.HasActiveSelection)
        {
            SelectionCompleted?.Invoke();
            return;
        }

        _currentTool.EndSelection(); // Allow tool to finalize its mask (e.g., closing a lasso)

        var selectionMask = _currentTool.GetSelectionMask();
        var sliceIndex = _currentTool.SliceIndex;
        var viewIndex = _currentTool.ViewIndex;

        if (selectionMask != null) CommitSelectionToCache(selectionMask, sliceIndex, viewIndex);

        // After committing, the tool's specific job is done, so we cancel its active state.
        _currentTool.CancelSelection();
        SelectionCompleted?.Invoke();
    }

    public void CommitSelectionToCache(byte[] selectionMask, int sliceIndex, int viewIndex)
    {
        if (selectionMask == null) return;
        var key = (sliceIndex, viewIndex);
        var clonedMask = (byte[])selectionMask.Clone();

        _activeSelections.AddOrUpdate(key, clonedMask, (k, existingMask) =>
        {
            // Merge new selection into existing one for the same slice
            for (var i = 0; i < existingMask.Length; i++)
                if (clonedMask[i] > 0)
                    existingMask[i] = 255;
            return existingMask;
        });

        // This is only an unapplied overlay; do not invalidate the full 3D label texture.
        SelectionPreviewChanged?.Invoke(clonedMask, sliceIndex, viewIndex);
    }

    public void CommitMultipleSelectionsToCache(Dictionary<(int slice, int view), byte[]> selections)
    {
        foreach (var sel in selections) CommitSelectionToCache(sel.Value, sel.Key.slice, sel.Key.view);
    }

    public Task ApplyActiveSelectionsToVolumeAsync(CancellationToken token = default,
        IProgress<float> progress = null)
    {
        if (_activeSelections.IsEmpty) return Task.CompletedTask;
        var selectionsToApply = new Dictionary<(int slice, int view), byte[]>(_activeSelections);
        var addMode = IsAddMode;
        var materialId = TargetMaterialId;
        return Task.Run(async () =>
        {
            var compoundAction = new CompoundSegmentationAction();
            compoundAction.StoreMultipleStates(_dataset, selectionsToApply.Keys.ToList());
            var completed = 0;
            foreach (var sel in selectionsToApply)
            {
                token.ThrowIfCancellationRequested();
                ApplyMaskToVolume(sel.Value, sel.Key.slice, sel.Key.view, addMode, materialId);
                progress?.Report(++completed / (float)selectionsToApply.Count * .85f);
            }
            compoundAction.CaptureAfterStates(_dataset);
            _undoStack.Push(compoundAction);
            _redoStack.Clear();
            _activeSelections.Clear();
            OpenTkManager.ExecuteOnMainThread(() =>
            {
                ProjectManager.Instance.NotifyDatasetDataChanged(_dataset);
                SelectionCompleted?.Invoke();
            });
            Logger.Log($"[SegmentationManager] Applied {selectionsToApply.Count} selections to material {materialId}");
        }, token);
    }

    public async Task MergeMaterialsAsync(byte sourceMaterialId, byte targetMaterialId)
    {
        if (sourceMaterialId == targetMaterialId) return;

        await Task.Run(() =>
        {
            var labels = _dataset.LabelData;
            int width = _dataset.Width, height = _dataset.Height, depth = _dataset.Depth;
            var mergeAction = new CompoundSegmentationAction();

            var allSlices = Enumerable.Range(0, depth).Select(z => (z, 0)).ToList();
            mergeAction.StoreMultipleStates(_dataset, allSlices);

            Parallel.For(0, depth, z =>
            {
                var labelSlice = new byte[width * height];
                labels.ReadSliceZ(z, labelSlice);
                var modified = false;
                for (var i = 0; i < labelSlice.Length; i++)
                    if (labelSlice[i] == sourceMaterialId)
                    {
                        labelSlice[i] = targetMaterialId;
                        modified = true;
                    }

                if (modified) labels.WriteSliceZ(z, labelSlice);
            });

            mergeAction.CaptureAfterStates(_dataset);
            _undoStack.Push(mergeAction);
            _redoStack.Clear();

            var materialToRemove = _dataset.Materials.FirstOrDefault(m => m.ID == sourceMaterialId);
            if (materialToRemove != null) _dataset.Materials.Remove(materialToRemove);

            ProjectManager.Instance.NotifyDatasetDataChanged(_dataset);
        });
        Logger.Log($"[SegmentationManager] Merged material {sourceMaterialId} into {targetMaterialId}");
    }

    public void ClearActiveSelections()
    {
        if (_activeSelections.IsEmpty) return;
        _activeSelections.Clear();
        SelectionPreviewChanged?.Invoke(null, -1, -1);
        Logger.Log("[SegmentationManager] Cleared active selections");
    }

    private void ApplyMaskToVolume(byte[] mask, int slice, int view, bool addMode, byte materialId)
    {
        switch (view)
        {
            case 0: ApplyMaskToXYSlice(mask, slice, addMode, materialId); break;
            case 1: ApplyMaskToXZSlice(mask, slice, addMode, materialId); break;
            case 2: ApplyMaskToYZSlice(mask, slice, addMode, materialId); break;
        }
    }

    private void ApplyMaskToXYSlice(byte[] mask, int z, bool addMode, byte materialId)
    {
        var s = new byte[_dataset.Width * _dataset.Height];
        _dataset.LabelData.ReadSliceZ(z, s);
        for (var i = 0; i < mask.Length; i++)
            if (mask[i] > 0)
                s[i] = addMode ? materialId : (byte)0;
        _dataset.LabelData.WriteSliceZ(z, s);
    }

    private void ApplyMaskToXZSlice(byte[] mask, int y, bool addMode, byte materialId)
    {
        var slice = new byte[_dataset.Width * _dataset.Height];
        for (var z = 0; z < _dataset.Depth; z++)
        {
            _dataset.LabelData.ReadSliceZ(z, slice);
            var modified = false;
            for (var x = 0; x < _dataset.Width; x++)
                if (mask[z * _dataset.Width + x] > 0)
                { slice[y * _dataset.Width + x] = addMode ? materialId : (byte)0; modified = true; }
            if (modified) _dataset.LabelData.WriteSliceZ(z, slice);
        }
    }

    private void ApplyMaskToYZSlice(byte[] mask, int x, bool addMode, byte materialId)
    {
        var slice = new byte[_dataset.Width * _dataset.Height];
        for (var z = 0; z < _dataset.Depth; z++)
        {
            _dataset.LabelData.ReadSliceZ(z, slice);
            var modified = false;
            for (var y = 0; y < _dataset.Height; y++)
                if (mask[z * _dataset.Height + y] > 0)
                { slice[y * _dataset.Width + x] = addMode ? materialId : (byte)0; modified = true; }
            if (modified) _dataset.LabelData.WriteSliceZ(z, slice);
        }
    }

    public async Task UndoAsync(CancellationToken token = default, IProgress<float> progress = null)
    {
        if (_undoStack.Any())
        {
            var a = _undoStack.Pop();
            await Task.Run(() => a.Restore(_dataset), token).ConfigureAwait(false);
            progress?.Report(.8f);
            _redoStack.Push(a);
            OpenTkManager.ExecuteOnMainThread(() => ProjectManager.Instance.NotifyDatasetDataChanged(_dataset));
        }
    }

    public async Task RedoAsync(CancellationToken token = default, IProgress<float> progress = null)
    {
        if (_redoStack.Any())
        {
            var a = _redoStack.Pop();
            await Task.Run(() => a.ReApply(_dataset), token).ConfigureAwait(false);
            progress?.Report(.8f);
            _undoStack.Push(a);
            OpenTkManager.ExecuteOnMainThread(() => ProjectManager.Instance.NotifyDatasetDataChanged(_dataset));
        }
    }

    public byte[] GetActiveSelectionMask(int slice, int view)
    {
        _activeSelections.TryGetValue((slice, view), out var m);
        return m;
    }

    public void NotifyPreviewChanged(byte[] mask, int slice, int view)
    {
        SelectionPreviewChanged?.Invoke(mask, slice, view);
    }

    public (int w, int h) GetSliceDimensions(int v)
    {
        return v switch
        {
            0 => (_dataset.Width, _dataset.Height), 1 => (_dataset.Width, _dataset.Depth),
            2 => (_dataset.Height, _dataset.Depth), _ => (0, 0)
        };
    }

    public byte[] GetGrayscaleSlice(int s, int v)
    {
        var (w, h) = GetSliceDimensions(v);
        var b = new byte[w * h];
        switch (v)
        {
            case 0: _dataset.VolumeData.ReadSliceZ(s, b); break;
            case 1: ExtractXZSlice(b, s); break;
            case 2: ExtractYZSlice(b, s); break;
        }

        return b;
    }

    private void ExtractXZSlice(byte[] b, int y)
    {
        for (var z = 0; z < _dataset.Depth; z++)
        for (var x = 0; x < _dataset.Width; x++)
            b[z * _dataset.Width + x] = _dataset.VolumeData[x, y, z];
    }

    private void ExtractYZSlice(byte[] b, int x)
    {
        for (var z = 0; z < _dataset.Depth; z++)
        for (var y = 0; y < _dataset.Height; y++)
            b[z * _dataset.Height + y] = _dataset.VolumeData[x, y, z];
    }
}

public abstract class SegmentationAction
{
    public abstract void Restore(CtImageStackDataset dataset);
    public abstract void ReApply(CtImageStackDataset dataset);
}

public class CompoundSegmentationAction : SegmentationAction
{
    private List<(int slice, int view, byte[] before, byte[] after)> _sliceChanges = new();

    public void StoreMultipleStates(CtImageStackDataset dataset, List<(int slice, int view)> keys)
    {
        _sliceChanges = keys.Select(key =>
        {
            var (width, height) = GetDimensions(dataset, key.view);
            var beforeState = new byte[width * height];
            ReadSlice(dataset, key.slice, key.view, beforeState);
            return (key.slice, key.view, beforeState, (byte[])null);
        }).ToList();
    }

    public void CaptureAfterStates(CtImageStackDataset dataset)
    {
        for (var i = 0; i < _sliceChanges.Count; i++)
        {
            var change = _sliceChanges[i];
            var (width, height) = GetDimensions(dataset, change.view);
            var afterState = new byte[width * height];
            ReadSlice(dataset, change.slice, change.view, afterState);
            _sliceChanges[i] = (change.slice, change.view, change.before, afterState);
        }
    }

    public override void Restore(CtImageStackDataset dataset)
    {
        foreach (var (slice, view, before, _) in _sliceChanges)
            if (before != null)
                WriteSlice(dataset, slice, view, before);
    }

    public override void ReApply(CtImageStackDataset dataset)
    {
        foreach (var (slice, view, _, after) in _sliceChanges)
            if (after != null)
                WriteSlice(dataset, slice, view, after);
    }

    private (int width, int height) GetDimensions(CtImageStackDataset d, int v)
    {
        return v switch
        {
            0 => (d.Width, d.Height),
            1 => (d.Width, d.Depth),
            2 => (d.Height, d.Depth),
            _ => (0, 0)
        };
    }

    private void ReadSlice(CtImageStackDataset dataset, int sliceIndex, int viewIndex, byte[] buffer)
    {
        switch (viewIndex)
        {
            case 0: dataset.LabelData.ReadSliceZ(sliceIndex, buffer); break;
            case 1:
                var xzSlice = new byte[dataset.Width * dataset.Height];
                for (var z = 0; z < dataset.Depth; z++)
                {
                    dataset.LabelData.ReadSliceZ(z, xzSlice);
                    for (var x = 0; x < dataset.Width; x++)
                        buffer[z * dataset.Width + x] = xzSlice[sliceIndex * dataset.Width + x];
                }
                break;
            case 2:
                var yzSlice = new byte[dataset.Width * dataset.Height];
                for (var z = 0; z < dataset.Depth; z++)
                {
                    dataset.LabelData.ReadSliceZ(z, yzSlice);
                    for (var y = 0; y < dataset.Height; y++)
                        buffer[z * dataset.Height + y] = yzSlice[y * dataset.Width + sliceIndex];
                }
                break;
        }
    }

    private void WriteSlice(CtImageStackDataset dataset, int sliceIndex, int viewIndex, byte[] buffer)
    {
        switch (viewIndex)
        {
            case 0: dataset.LabelData.WriteSliceZ(sliceIndex, buffer); break;
            case 1:
                var xzSlice = new byte[dataset.Width * dataset.Height];
                for (var z = 0; z < dataset.Depth; z++)
                {
                    dataset.LabelData.ReadSliceZ(z, xzSlice);
                    for (var x = 0; x < dataset.Width; x++)
                        xzSlice[sliceIndex * dataset.Width + x] = buffer[z * dataset.Width + x];
                    dataset.LabelData.WriteSliceZ(z, xzSlice);
                }
                break;
            case 2:
                var yzSlice = new byte[dataset.Width * dataset.Height];
                for (var z = 0; z < dataset.Depth; z++)
                {
                    dataset.LabelData.ReadSliceZ(z, yzSlice);
                    for (var y = 0; y < dataset.Height; y++)
                        yzSlice[y * dataset.Width + sliceIndex] = buffer[z * dataset.Height + y];
                    dataset.LabelData.WriteSliceZ(z, yzSlice);
                }
                break;
        }
    }
}
