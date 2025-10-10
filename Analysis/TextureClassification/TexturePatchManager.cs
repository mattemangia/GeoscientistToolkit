// GeoscientistToolkit/Analysis/TextureClassification/TexturePatchManager.cs

using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.TextureClassification;

public class TrainingPatch
{
    public int X { get; set; }
    public int Y { get; set; }
    public int SliceIndex { get; set; }
    public int ViewIndex { get; set; }
    public int ClassId { get; set; }
    public int PatchSize { get; set; }
    public byte[] Data { get; set; }
}

public class TexturePatchManager : IDisposable
{
    private readonly CtImageStackDataset _dataset;
    private readonly object _lock = new();
    private readonly List<TrainingPatch> _patches = new();

    public TexturePatchManager(CtImageStackDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _patches.Clear();
        }
    }

    public void AddPatch(int x, int y, int sliceIndex, int viewIndex, int classId, int patchSize)
    {
        lock (_lock)
        {
            var halfSize = patchSize / 2;
            var (width, height) = GetSliceDimensions(viewIndex);

            // Bounds check
            if (x < halfSize || y < halfSize ||
                x >= width - halfSize || y >= height - halfSize)
            {
                Logger.LogWarning($"[TexturePatchManager] Patch at ({x},{y}) too close to edge");
                return;
            }

            // Extract patch data
            var patchData = ExtractPatchData(x, y, sliceIndex, viewIndex, patchSize);
            if (patchData == null) return;

            var patch = new TrainingPatch
            {
                X = x,
                Y = y,
                SliceIndex = sliceIndex,
                ViewIndex = viewIndex,
                ClassId = classId,
                PatchSize = patchSize,
                Data = patchData
            };

            _patches.Add(patch);
            Logger.Log(
                $"[TexturePatchManager] Added patch {_patches.Count} at ({x},{y}) slice {sliceIndex}, class {classId}");
        }
    }

    public void RemovePatchAt(int x, int y, int sliceIndex, int viewIndex)
    {
        lock (_lock)
        {
            var toRemove = _patches.FindAll(p =>
            {
                var halfSize = p.PatchSize / 2;
                return p.SliceIndex == sliceIndex &&
                       p.ViewIndex == viewIndex &&
                       Math.Abs(p.X - x) < halfSize &&
                       Math.Abs(p.Y - y) < halfSize;
            });

            foreach (var patch in toRemove)
            {
                _patches.Remove(patch);
                Logger.Log($"[TexturePatchManager] Removed patch at ({patch.X},{patch.Y})");
            }
        }
    }

    public void ClearAllPatches()
    {
        lock (_lock)
        {
            _patches.Clear();
            Logger.Log("[TexturePatchManager] Cleared all patches");
        }
    }

    public int GetTotalPatchCount()
    {
        lock (_lock)
        {
            return _patches.Count;
        }
    }

    public Dictionary<int, int> GetClassCounts()
    {
        lock (_lock)
        {
            return _patches.GroupBy(p => p.ClassId)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    public List<TrainingPatch> GetAllPatches()
    {
        lock (_lock)
        {
            return new List<TrainingPatch>(_patches);
        }
    }

    public List<TrainingPatch> GetPatchesForSlice(int sliceIndex, int viewIndex)
    {
        lock (_lock)
        {
            return _patches.Where(p => p.SliceIndex == sliceIndex && p.ViewIndex == viewIndex).ToList();
        }
    }

    private byte[] ExtractPatchData(int centerX, int centerY, int sliceIndex, int viewIndex, int patchSize)
    {
        var halfSize = patchSize / 2;
        var patchData = new byte[patchSize * patchSize];
        var (width, height) = GetSliceDimensions(viewIndex);

        // Get the full slice
        var sliceData = new byte[width * height];
        ReadSlice(sliceIndex, viewIndex, sliceData);

        // Extract patch
        for (var py = 0; py < patchSize; py++)
        for (var px = 0; px < patchSize; px++)
        {
            var sx = centerX - halfSize + px;
            var sy = centerY - halfSize + py;

            if (sx >= 0 && sx < width && sy >= 0 && sy < height)
                patchData[py * patchSize + px] = sliceData[sy * width + sx];
        }

        return patchData;
    }

    private (int width, int height) GetSliceDimensions(int viewIndex)
    {
        return viewIndex switch
        {
            0 => (_dataset.Width, _dataset.Height),
            1 => (_dataset.Width, _dataset.Depth),
            2 => (_dataset.Height, _dataset.Depth),
            _ => (0, 0)
        };
    }

    private void ReadSlice(int sliceIndex, int viewIndex, byte[] buffer)
    {
        switch (viewIndex)
        {
            case 0:
                _dataset.VolumeData.ReadSliceZ(sliceIndex, buffer);
                break;
            case 1:
                for (var z = 0; z < _dataset.Depth; z++)
                for (var x = 0; x < _dataset.Width; x++)
                    buffer[z * _dataset.Width + x] = _dataset.VolumeData[x, sliceIndex, z];
                break;
            case 2:
                for (var z = 0; z < _dataset.Depth; z++)
                for (var y = 0; y < _dataset.Height; y++)
                    buffer[z * _dataset.Height + y] = _dataset.VolumeData[sliceIndex, y, z];
                break;
        }
    }
}