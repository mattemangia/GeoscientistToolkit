// GeoscientistToolkit/Data/CtImageStack/Segmentation/BrushTool.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation
{
    public class BrushTool : ISegmentationTool
    {
        private SegmentationManager _manager;
        private byte[] _selectionMask;
        private int _width;
        private int _height;
        private Vector2 _lastPos;
        private bool _isActive;

        public string Name => "Brush";
        public string Icon => "🖌";

        public float BrushSize { get; set; } = 10.0f;
        public float Hardness { get; set; } = 1.0f;

        public bool HasActiveSelection => _isActive;

        // --- ADDED: Public properties for the interface ---
        public int SliceIndex { get; protected set; }
        public int ViewIndex { get; protected set; }

        public void Initialize(SegmentationManager manager)
        {
            _manager = manager;
        }

        public void StartSelection(Vector2 startPos, int sliceIndex, int viewIndex)
        {
            this.SliceIndex = sliceIndex;
            this.ViewIndex = viewIndex;
            (_width, _height) = _manager.GetSliceDimensions(viewIndex);

            _selectionMask = new byte[_width * _height];
            _lastPos = startPos;
            _isActive = true;

            ApplyBrush(startPos);
            _manager.NotifyPreviewChanged(_selectionMask, this.SliceIndex, this.ViewIndex);
        }

        public void UpdateSelection(Vector2 currentPos)
        {
            if (!_isActive) return;

            var distance = Vector2.Distance(_lastPos, currentPos);
            var steps = Math.Max(1, (int)(distance / (BrushSize * 0.25f)));

            for (int i = 0; i <= steps; i++)
            {
                var t = steps > 0 ? i / (float)steps : 0;
                var pos = Vector2.Lerp(_lastPos, currentPos, t);
                ApplyBrush(pos);
            }

            _lastPos = currentPos;
            _manager.NotifyPreviewChanged(_selectionMask, this.SliceIndex, this.ViewIndex);
        }

        public void EndSelection()
        {
            if (!_isActive) return;
            // The selection is finalized.
            // The manager will call GetSelectionMask() and then CancelSelection().
        }

        public void CancelSelection()
        {
            _isActive = false;
            _selectionMask = null;
        }

        public byte[] GetSelectionMask()
        {
            return _selectionMask;
        }

        private void ApplyBrush(Vector2 pos)
        {
            int centerX = (int)pos.X;
            int centerY = (int)pos.Y;
            int radius = (int)Math.Ceiling(BrushSize);

            int startX = Math.Max(0, centerX - radius);
            int endX = Math.Min(_width - 1, centerX + radius);
            int startY = Math.Max(0, centerY - radius);
            int endY = Math.Min(_height - 1, centerY + radius);

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    ProcessBrushPixel(x, y, centerX, centerY);
                }
            }
        }

        private void ProcessBrushPixel(int x, int y, int centerX, int centerY)
        {
            float distance = MathF.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));

            if (distance <= BrushSize)
            {
                int index = y * _width + x;

                if (Hardness >= 1.0f)
                {
                    _selectionMask[index] = 255;
                }
                else
                {
                    float falloff = Math.Clamp(1.0f - (distance / BrushSize), 0.0f, 1.0f);
                    float intensity = MathF.Pow(falloff, 1.0f / (1.0f - Hardness + 0.01f));
                    byte value = (byte)(intensity * 255);
                    _selectionMask[index] = Math.Max(_selectionMask[index], value);
                }
            }
        }

        public void Dispose()
        {
            _selectionMask = null;
        }
    }
}