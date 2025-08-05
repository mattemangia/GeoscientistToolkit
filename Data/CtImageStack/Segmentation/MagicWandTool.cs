// GeoscientistToolkit/Data/CtImageStack/Segmentation/MagicWandTool.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation
{
    public class MagicWandTool : ISegmentationTool
    {
        private SegmentationManager _manager;
        private byte[] _selectionMask;
        private int _width;
        private int _height;
        private bool _isActive;

        public string Name => "Magic Wand";
        public string Icon => "✨";
        public bool HasActiveSelection => _isActive;

        // --- ADDED: Public properties for the interface ---
        public int SliceIndex { get; protected set; }
        public int ViewIndex { get; protected set; }

        public byte Tolerance { get; set; } = 10;
        public bool SelectOnlyFromCurrentMaterial { get; set; } = false;

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
            _isActive = true;

            RunRegionGrowing((int)startPos.X, (int)startPos.Y);

            _manager.NotifyPreviewChanged(_selectionMask, this.SliceIndex, this.ViewIndex);
        }

        private void RunRegionGrowing(int startX, int startY)
        {
            if (startX < 0 || startX >= _width || startY < 0 || startY >= _height) return;

            var grayscale = _manager.GetGrayscaleSlice(this.SliceIndex, this.ViewIndex);
            var queue = new Queue<(int, int)>();

            byte startValue = grayscale[startY * _width + startX];
            int minVal = Math.Max(0, startValue - Tolerance);
            int maxVal = Math.Min(255, startValue + Tolerance);

            queue.Enqueue((startX, startY));
            _selectionMask[startY * _width + startX] = 255;

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();

                int[] dx = { 0, 0, 1, -1 };
                int[] dy = { 1, -1, 0, 0 };

                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];

                    if (nx >= 0 && nx < _width && ny >= 0 && ny < _height)
                    {
                        int neighborIndex = ny * _width + nx;
                        if (_selectionMask[neighborIndex] == 0)
                        {
                            byte neighborValue = grayscale[neighborIndex];
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

        public void UpdateSelection(Vector2 currentPos) { /* Magic wand doesn't update continuously */ }

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

        public void Dispose()
        {
            _selectionMask = null;
        }
    }
}