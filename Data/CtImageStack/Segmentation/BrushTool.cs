// GeoscientistToolkit/Data/CtImageStack/Segmentation/BrushTool.cs

using System.Numerics;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation;

public class BrushTool : ISegmentationTool
{
    private int _height;
    private Vector2 _lastPos;
    private SegmentationManager _manager;
    private byte[] _selectionMask;
    private int _width;

    public float BrushSize { get; set; } = 10.0f;
    public float Hardness { get; set; } = 1.0f;

    public string Name => "Brush";
    public string Icon => "Brush";

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
        _lastPos = startPos;
        HasActiveSelection = true;

        ApplyBrush(startPos);
        _manager.NotifyPreviewChanged(_selectionMask, SliceIndex, ViewIndex);
    }

    public void UpdateSelection(Vector2 currentPos)
    {
        if (!HasActiveSelection) return;

        var distance = Vector2.Distance(_lastPos, currentPos);
        var steps = Math.Max(1, (int)(distance / (BrushSize * 0.25f)));

        for (var i = 0; i <= steps; i++)
        {
            var t = steps > 0 ? i / (float)steps : 0;
            var pos = Vector2.Lerp(_lastPos, currentPos, t);
            ApplyBrush(pos);
        }

        _lastPos = currentPos;
        _manager.NotifyPreviewChanged(_selectionMask, SliceIndex, ViewIndex);
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

    private void ApplyBrush(Vector2 pos)
    {
        var centerX = (int)pos.X;
        var centerY = (int)pos.Y;
        var radius = (int)Math.Ceiling(BrushSize);

        var startX = Math.Max(0, centerX - radius);
        var endX = Math.Min(_width - 1, centerX + radius);
        var startY = Math.Max(0, centerY - radius);
        var endY = Math.Min(_height - 1, centerY + radius);

        for (var y = startY; y <= endY; y++)
        for (var x = startX; x <= endX; x++)
            ProcessBrushPixel(x, y, centerX, centerY);
    }

    private void ProcessBrushPixel(int x, int y, int centerX, int centerY)
    {
        var distance = MathF.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));

        if (distance <= BrushSize)
        {
            var index = y * _width + x;

            if (Hardness >= 1.0f)
            {
                _selectionMask[index] = 255;
            }
            else
            {
                var falloff = Math.Clamp(1.0f - distance / BrushSize, 0.0f, 1.0f);
                var intensity = MathF.Pow(falloff, 1.0f / (1.0f - Hardness + 0.01f));
                var value = (byte)(intensity * 255);
                _selectionMask[index] = Math.Max(_selectionMask[index], value);
            }
        }
    }
}
