// GeoscientistToolkit/Data/CtImageStack/Segmentation/SegmentationManager.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using System.Linq;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation
{
    public class SegmentationManager : IDisposable
    {
        private readonly CtImageStackDataset _dataset;
        private readonly Stack<SegmentationAction> _undoStack = new Stack<SegmentationAction>();
        private readonly Stack<SegmentationAction> _redoStack = new Stack<SegmentationAction>();
        
        // Current tool
        private ISegmentationTool _currentTool;
        private byte _targetMaterialId = 1;
        private bool _isAddMode = true;
        
        // Selection cache for performance
        private readonly Dictionary<(int z, int viewIndex), byte[]> _selectionCache = new Dictionary<(int, int), byte[]>();
        private const int MAX_CACHE_SLICES = 10;
        
        // Track active selections across multiple slices for bulk operations
        private readonly Dictionary<(int slice, int view), byte[]> _activeSelections = new Dictionary<(int, int), byte[]>();
        
        public event Action<byte[], int, int> SelectionPreviewChanged;
        public event Action SelectionCompleted;
        
        public ISegmentationTool CurrentTool 
        { 
            get => _currentTool;
            set
            {
                _currentTool?.Dispose();
                _currentTool = value;
                _currentTool?.Initialize(this);
            }
        }
        
        public byte TargetMaterialId
        {
            get => _targetMaterialId;
            set => _targetMaterialId = value;
        }
        
        public bool IsAddMode
        {
            get => _isAddMode;
            set => _isAddMode = value;
        }
        
        public SegmentationManager(CtImageStackDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        }
        
        public CtImageStackDataset GetDataset() => _dataset;
        
        public void StartSelection(Vector2 startPos, int sliceIndex, int viewIndex)
        {
            _currentTool?.StartSelection(startPos, sliceIndex, viewIndex);
        }
        
        public void UpdateSelection(Vector2 currentPos)
        {
            _currentTool?.UpdateSelection(currentPos);
        }
        
        public void EndSelection()
        {
            _currentTool?.EndSelection();
        }
        
        public void CancelSelection()
        {
            _currentTool?.CancelSelection();
            ClearSelectionCache();
        }
        
        public async Task ApplySelectionAsync(byte[] selectionMask, int sliceIndex, int viewIndex)
        {
            if (selectionMask == null) return;
            
            // Store this selection for potential bulk operations
            _activeSelections[(sliceIndex, viewIndex)] = (byte[])selectionMask.Clone();
            
            var action = new SegmentationAction
            {
                MaterialId = _targetMaterialId,
                IsAddOperation = _isAddMode,
                SliceIndex = sliceIndex,
                ViewIndex = viewIndex,
                SelectionMask = (byte[])selectionMask.Clone()
            };
            
            // Store current state for undo
            action.StoreCurrentState(_dataset, sliceIndex, viewIndex);
            
            // Apply the selection
            await ApplyMaskToVolumeAsync(selectionMask, sliceIndex, viewIndex);
            
            // Add to undo stack
            _undoStack.Push(action);
            _redoStack.Clear();
            
            SelectionCompleted?.Invoke();
        }
        
        /// <summary>
        /// Extracts the current selection and creates a new material from it
        /// </summary>
        public async Task ExtractSelectionToNewMaterialAsync()
        {
            if (_activeSelections.Count == 0) return;
            
            // Get next available material ID
            byte newMaterialId = MaterialOperations.GetNextMaterialID(_dataset.Materials);
            
            // Create new material with a distinct color
            var random = new Random();
            var newMaterial = new Material(newMaterialId, $"Extracted Material {newMaterialId}", 
                new Vector4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 1.0f));
            _dataset.Materials.Add(newMaterial);
            
            // Apply all active selections with the new material ID
            byte originalTargetId = _targetMaterialId;
            _targetMaterialId = newMaterialId;
            
            foreach (var selection in _activeSelections)
            {
                await ApplyMaskToVolumeAsync(selection.Value, selection.Key.slice, selection.Key.view);
            }
            
            _targetMaterialId = originalTargetId;
            _activeSelections.Clear();
            
            Logger.Log($"[SegmentationManager] Extracted selection to new material: {newMaterial.Name}");
        }
        
        /// <summary>
        /// Merges one material into another
        /// </summary>
        public async Task MergeMaterialsAsync(byte sourceMaterialId, byte targetMaterialId)
        {
            if (sourceMaterialId == targetMaterialId) return;
            
            await Task.Run(() =>
            {
                var labels = _dataset.LabelData;
                int width = _dataset.Width;
                int height = _dataset.Height;
                int depth = _dataset.Depth;
                
                // Create a compound action for undo
                var mergeAction = new CompoundSegmentationAction();
                
                Parallel.For(0, depth, z =>
                {
                    var labelSlice = new byte[width * height];
                    labels.ReadSliceZ(z, labelSlice);
                    
                    bool modified = false;
                    var originalSlice = (byte[])labelSlice.Clone();
                    
                    for (int i = 0; i < labelSlice.Length; i++)
                    {
                        if (labelSlice[i] == sourceMaterialId)
                        {
                            labelSlice[i] = targetMaterialId;
                            modified = true;
                        }
                    }
                    
                    if (modified)
                    {
                        labels.WriteSliceZ(z, labelSlice);
                        
                        // Store the change for undo
                        lock (mergeAction)
                        {
                            mergeAction.AddSliceChange(z, originalSlice, labelSlice);
                        }
                    }
                });
                
                // Remove the source material from the list
                var materialToRemove = _dataset.Materials.FirstOrDefault(m => m.ID == sourceMaterialId);
                if (materialToRemove != null)
                {
                    _dataset.Materials.Remove(materialToRemove);
                }
                
                // Add to undo stack
                _undoStack.Push(mergeAction);
                _redoStack.Clear();
                
                ProjectManager.Instance.NotifyDatasetDataChanged(_dataset);
            });
            
            Logger.Log($"[SegmentationManager] Merged material {sourceMaterialId} into {targetMaterialId}");
        }
        
        /// <summary>
        /// Erases all active selection masks from their respective slices
        /// </summary>
        public async Task EraseActiveSelectionsAsync()
        {
            if (_activeSelections.Count == 0)
            {
                Logger.Log("[SegmentationManager] No active selections to erase");
                return;
            }
            
            // Temporarily switch to remove mode
            bool wasAddMode = _isAddMode;
            _isAddMode = false;
            
            foreach (var selection in _activeSelections)
            {
                await ApplyMaskToVolumeAsync(selection.Value, selection.Key.slice, selection.Key.view);
            }
            
            _isAddMode = wasAddMode;
            _activeSelections.Clear();
            
            Logger.Log($"[SegmentationManager] Erased active selections");
        }
        
        /// <summary>
        /// Clears all active selections without applying them
        /// </summary>
        public void ClearActiveSelections()
        {
            _activeSelections.Clear();
            Logger.Log("[SegmentationManager] Cleared active selections");
        }
        
        private async Task ApplyMaskToVolumeAsync(byte[] mask, int sliceIndex, int viewIndex)
        {
            await Task.Run(() =>
            {
                var labels = _dataset.LabelData;
                
                switch (viewIndex)
                {
                    case 0: // XY view
                        ApplyMaskToXYSlice(mask, sliceIndex);
                        break;
                    case 1: // XZ view
                        ApplyMaskToXZSlice(mask, sliceIndex);
                        break;
                    case 2: // YZ view
                        ApplyMaskToYZSlice(mask, sliceIndex);
                        break;
                }
                
                ProjectManager.Instance.NotifyDatasetDataChanged(_dataset);
            });
        }
        
        private void ApplyMaskToXYSlice(byte[] mask, int z)
        {
            var labelSlice = new byte[_dataset.Width * _dataset.Height];
            _dataset.LabelData.ReadSliceZ(z, labelSlice);
            
            Parallel.For(0, mask.Length, i =>
            {
                if (mask[i] > 0)
                {
                    labelSlice[i] = _isAddMode ? _targetMaterialId : (byte)0;
                }
            });
            
            _dataset.LabelData.WriteSliceZ(z, labelSlice);
        }
        
        private void ApplyMaskToXZSlice(byte[] mask, int y)
        {
            int width = _dataset.Width;
            int depth = _dataset.Depth;
            
            Parallel.For(0, depth, z =>
            {
                for (int x = 0; x < width; x++)
                {
                    int maskIndex = z * width + x;
                    if (mask[maskIndex] > 0)
                    {
                        _dataset.LabelData[x, y, z] = _isAddMode ? _targetMaterialId : (byte)0;
                    }
                }
            });
        }
        
        private void ApplyMaskToYZSlice(byte[] mask, int x)
        {
            int height = _dataset.Height;
            int depth = _dataset.Depth;
            
            Parallel.For(0, depth, z =>
            {
                for (int y = 0; y < height; y++)
                {
                    int maskIndex = z * height + y;
                    if (mask[maskIndex] > 0)
                    {
                        _dataset.LabelData[x, y, z] = _isAddMode ? _targetMaterialId : (byte)0;
                    }
                }
            });
        }
        
        public void Undo()
        {
            if (_undoStack.Count == 0) return;
            
            var action = _undoStack.Pop();
            action.Restore(_dataset);
            _redoStack.Push(action);
            
            ProjectManager.Instance.NotifyDatasetDataChanged(_dataset);
        }
        
        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            
            var action = _redoStack.Pop();
            
            if (action is SegmentationAction simpleAction)
            {
                simpleAction.StoreCurrentState(_dataset, simpleAction.SliceIndex, simpleAction.ViewIndex);
                ApplyMaskToVolumeAsync(simpleAction.SelectionMask, simpleAction.SliceIndex, simpleAction.ViewIndex).Wait();
            }
            else if (action is CompoundSegmentationAction)
            {
                action.Restore(_dataset);
            }
            
            _undoStack.Push(action);
        }
        
        public void NotifyPreviewChanged(byte[] previewMask, int sliceIndex, int viewIndex)
        {
            SelectionPreviewChanged?.Invoke(previewMask, sliceIndex, viewIndex);
        }
        
        public (int width, int height) GetSliceDimensions(int viewIndex)
        {
            return viewIndex switch
            {
                0 => (_dataset.Width, _dataset.Height),
                1 => (_dataset.Width, _dataset.Depth),
                2 => (_dataset.Height, _dataset.Depth),
                _ => (_dataset.Width, _dataset.Height)
            };
        }
        
        public byte[] GetGrayscaleSlice(int sliceIndex, int viewIndex)
        {
            var key = (sliceIndex, viewIndex);
            if (_selectionCache.TryGetValue(key, out var cached))
                return cached;
            
            var (width, height) = GetSliceDimensions(viewIndex);
            var slice = new byte[width * height];
            
            switch (viewIndex)
            {
                case 0: // XY
                    _dataset.VolumeData.ReadSliceZ(sliceIndex, slice);
                    break;
                case 1: // XZ
                    ExtractXZSlice(slice, sliceIndex);
                    break;
                case 2: // YZ
                    ExtractYZSlice(slice, sliceIndex);
                    break;
            }
            
            // Cache management
            if (_selectionCache.Count >= MAX_CACHE_SLICES)
            {
                var oldestKey = _selectionCache.Keys.First();
                _selectionCache.Remove(oldestKey);
            }
            
            _selectionCache[key] = slice;
            return slice;
        }
        
        private void ExtractXZSlice(byte[] buffer, int y)
        {
            int width = _dataset.Width;
            int depth = _dataset.Depth;
            
            Parallel.For(0, depth, z =>
            {
                for (int x = 0; x < width; x++)
                {
                    buffer[z * width + x] = _dataset.VolumeData[x, y, z];
                }
            });
        }
        
        private void ExtractYZSlice(byte[] buffer, int x)
        {
            int height = _dataset.Height;
            int depth = _dataset.Depth;
            
            Parallel.For(0, depth, z =>
            {
                for (int y = 0; y < height; y++)
                {
                    buffer[z * height + y] = _dataset.VolumeData[x, y, z];
                }
            });
        }
        
        private void ClearSelectionCache()
        {
            _selectionCache.Clear();
        }
        
        public void Dispose()
        {
            _currentTool?.Dispose();
            ClearSelectionCache();
            ClearActiveSelections();
        }
    }
    
    public class SegmentationAction
    {
        public byte MaterialId { get; set; }
        public bool IsAddOperation { get; set; }
        public int SliceIndex { get; set; }
        public int ViewIndex { get; set; }
        public byte[] SelectionMask { get; set; }
        public byte[] PreviousState { get; set; }
        
        public virtual void StoreCurrentState(CtImageStackDataset dataset, int sliceIndex, int viewIndex)
        {
            var (width, height) = GetDimensions(dataset, viewIndex);
            PreviousState = new byte[width * height];
            
            switch (viewIndex)
            {
                case 0: // XY
                    dataset.LabelData.ReadSliceZ(sliceIndex, PreviousState);
                    break;
                case 1: // XZ
                    ReadXZSlice(dataset, sliceIndex, PreviousState);
                    break;
                case 2: // YZ
                    ReadYZSlice(dataset, sliceIndex, PreviousState);
                    break;
            }
        }
        
        public virtual void Restore(CtImageStackDataset dataset)
        {
            if (PreviousState == null) return;
            
            switch (ViewIndex)
            {
                case 0: // XY
                    dataset.LabelData.WriteSliceZ(SliceIndex, PreviousState);
                    break;
                case 1: // XZ
                    WriteXZSlice(dataset, SliceIndex, PreviousState);
                    break;
                case 2: // YZ
                    WriteYZSlice(dataset, SliceIndex, PreviousState);
                    break;
            }
        }
        
        protected (int width, int height) GetDimensions(CtImageStackDataset dataset, int viewIndex)
        {
            return viewIndex switch
            {
                0 => (dataset.Width, dataset.Height),
                1 => (dataset.Width, dataset.Depth),
                2 => (dataset.Height, dataset.Depth),
                _ => (dataset.Width, dataset.Height)
            };
        }
        
        private void ReadXZSlice(CtImageStackDataset dataset, int y, byte[] buffer)
        {
            int width = dataset.Width;
            int depth = dataset.Depth;
            
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    buffer[z * width + x] = dataset.LabelData[x, y, z];
                }
            }
        }
        
        private void WriteXZSlice(CtImageStackDataset dataset, int y, byte[] buffer)
        {
            int width = dataset.Width;
            int depth = dataset.Depth;
            
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    dataset.LabelData[x, y, z] = buffer[z * width + x];
                }
            }
        }
        
        private void ReadYZSlice(CtImageStackDataset dataset, int x, byte[] buffer)
        {
            int height = dataset.Height;
            int depth = dataset.Depth;
            
            for (int z = 0; z < depth; z++)
            {
                for (int y = 0; y < height; y++)
                {
                    buffer[z * height + y] = dataset.LabelData[x, y, z];
                }
            }
        }
        
        private void WriteYZSlice(CtImageStackDataset dataset, int x, byte[] buffer)
        {
            int height = dataset.Height;
            int depth = dataset.Depth;
            
            for (int z = 0; z < depth; z++)
            {
                for (int y = 0; y < height; y++)
                {
                    dataset.LabelData[x, y, z] = buffer[z * height + y];
                }
            }
        }
    }
    
    /// <summary>
    /// Represents a compound action that affects multiple slices (like merge)
    /// </summary>
    public class CompoundSegmentationAction : SegmentationAction
    {
        private readonly List<(int slice, byte[] before, byte[] after)> _sliceChanges = new List<(int, byte[], byte[])>();
        
        public void AddSliceChange(int slice, byte[] before, byte[] after)
        {
            _sliceChanges.Add((slice, before, after));
        }
        
        public override void Restore(CtImageStackDataset dataset)
        {
            foreach (var (slice, before, _) in _sliceChanges)
            {
                dataset.LabelData.WriteSliceZ(slice, before);
            }
        }
    }
}