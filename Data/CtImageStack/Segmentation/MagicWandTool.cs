// GeoscientistToolkit/Data/CtImageStack/Segmentation/MagicWandTool.cs

using System.Numerics;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation;

public class MagicWandTool : ISegmentationTool
{
    private int _height;
    private SegmentationManager _manager;
    private byte[] _selectionMask;
    private int _width;

    public byte Tolerance { get; set; } = 10;
    public bool SelectOnlyFromCurrentMaterial { get; set; } = false;

    public string Name => "Magic Wand";
    public string Icon => "✨";
    public bool HasActiveSelection { get; private set; }

    // --- ADDED: Public properties for the interface ---
    public int SliceIndex { get; protected set; }
    public int ViewIndex { get; protected set; }

    public void Initialize(SegmentationManager manager)
    {
        _manager = manager;
    }

    public void StartSelection(Vector2 startPos, int sliceIndex, int viewIndex)
    {
        SliceIndex = sliceIndex;
        ViewIndex = viewIndex;
        (_width, _height) = _manager.GetSliceDimensions(viewIndex);
        _selectionMask = new byte[_width * _height];
        HasActiveSelection = true;

        RunRegionGrowing((int)startPos.X, (int)startPos.Y);

        _manager.NotifyPreviewChanged(_selectionMask, SliceIndex, ViewIndex);
    }

    public void UpdateSelection(Vector2 currentPos)
    {
        /* Magic wand doesn't update continuously */
    }

    public void EndSelection()
    {
        if (!HasActiveSelection) return;
        // The selection is finalized.
        // The manager will call GetSelectionMask() and then CancelSelection().
    }

    public void CancelSelection()
    {
        HasActiveSelection = false;
        _selectionMask = null;
    }

    public byte[] GetSelectionMask()
    {
        return _selectionMask;
    }

    public void Dispose()
    {
        _selectionMask = null;
    }

    // ALGORITHM: Seeded Region Growing
    //
    // This method implements a flood-fill region growing algorithm based on intensity similarity.
    // Starting from a seed point, it expands the selection to 4-connected neighbors whose intensity
    // values fall within the tolerance threshold.
    //
    // References:
    // - Adams, R., & Bischof, L. (1994). "Seeded region growing." IEEE Transactions on Pattern
    //   Analysis and Machine Intelligence, 16(6), 641-647.
    //   DOI: 10.1109/34.295913
    //
    // - Gonzalez, R.C., & Woods, R.E. (2018). "Digital Image Processing," 4th ed. Pearson.
    //   Chapter 10: Image Segmentation (Region Growing)
    //
    private void RunRegionGrowing(int startX, int startY)
    {
        if (startX < 0 || startX >= _width || startY < 0 || startY >= _height) return;

        var grayscale = _manager.GetGrayscaleSlice(SliceIndex, ViewIndex);
        var queue = new Queue<(int, int)>();

        var startValue = grayscale[startY * _width + startX];
        var minVal = Math.Max(0, startValue - Tolerance);
        var maxVal = Math.Min(255, startValue + Tolerance);

        queue.Enqueue((startX, startY));
        _selectionMask[startY * _width + startX] = 255;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();

            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            for (var i = 0; i < 4; i++)
            {
                var nx = x + dx[i];
                var ny = y + dy[i];

                if (nx >= 0 && nx < _width && ny >= 0 && ny < _height)
                {
                    var neighborIndex = ny * _width + nx;
                    if (_selectionMask[neighborIndex] == 0)
                    {
                        var neighborValue = grayscale[neighborIndex];
                        if (neighborValue >= minVal && neighborValue <= maxVal)
                        {
                            _selectionMask[neighborIndex] = 255;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
            }
        }
    }
}