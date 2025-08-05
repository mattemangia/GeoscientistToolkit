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
        private int _sliceIndex;
        private int _viewIndex;
        private int _width;
        private int _height;
        private bool _isActive;
        private bool _selectOnlyFromMaterial;

        public string Name => "Magic Wand";
        public string Icon => "✨";
        public bool HasActiveSelection => _isActive;

        public byte Tolerance { get; set; } = 10;
        public bool SelectOnlyFromCurrentMaterial { get; set; } = false;

        public void Initialize(SegmentationManager manager)
        {
            _manager = manager;
        }

        public void StartSelection(Vector2 startPos, int sliceIndex, int viewIndex)
        {
            _sliceIndex = sliceIndex;
            _viewIndex = viewIndex;
            (_width, _height) = _manager.GetSliceDimensions(viewIndex);
            _selectionMask = new byte[_width * _height];
            _isActive = true;
            _selectOnlyFromMaterial = SelectOnlyFromCurrentMaterial;

            RunRegionGrowing((int)startPos.X, (int)startPos.Y);
            
            _manager.NotifyPreviewChanged(_selectionMask, _sliceIndex, _viewIndex);
        }

        private void RunRegionGrowing(int startX, int startY)
        {
            if (startX < 0 || startX >= _width || startY < 0 || startY >= _height) return;

            var grayscale = _manager.GetGrayscaleSlice(_sliceIndex, _viewIndex);
            var queue = new Queue<(int, int)>();
            var visited = new bool[_width * _height];

            byte startValue = grayscale[startY * _width + startX];
            int minVal = Math.Max(0, startValue - Tolerance);
            int maxVal = Math.Min(255, startValue + Tolerance);

            // If selecting only from material, get the label data
            byte[] labelData = null;
            byte targetMaterialId = _manager.TargetMaterialId;
            if (_selectOnlyFromMaterial)
            {
                labelData = GetLabelSliceData();
            }

            queue.Enqueue((startX, startY));
            visited[startY * _width + startX] = true;

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                int currentIndex = y * _width + x;
                
                // Check if we should include this pixel based on material constraint
                if (_selectOnlyFromMaterial && labelData != null)
                {
                    // In Add mode: only select from areas already labeled with target material
                    // In Remove mode: only select from areas with target material
                    if (labelData[currentIndex] != targetMaterialId)
                        continue;
                }
                
                _selectionMask[currentIndex] = 255;

                // Check 4-connectivity neighbors
                int[] dx = { 0, 0, 1, -1 };
                int[] dy = { 1, -1, 0, 0 };

                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];

                    if (nx >= 0 && nx < _width && ny >= 0 && ny < _height)
                    {
                        int neighborIndex = ny * _width + nx;
                        if (!visited[neighborIndex])
                        {
                            visited[neighborIndex] = true;
                            byte neighborValue = grayscale[neighborIndex];
                            if (neighborValue >= minVal && neighborValue <= maxVal)
                            {
                                queue.Enqueue((nx, ny));
                            }
                        }
                    }
                }
            }
        }

        private byte[] GetLabelSliceData()
        {
            // This is a simplified version - in real implementation, 
            // you'd need to extract label data based on viewIndex
            var dataset = _manager.GetDataset();
            if (dataset?.LabelData == null) return null;

            var (width, height) = _manager.GetSliceDimensions(_viewIndex);
            byte[] labelData = new byte[width * height];

            switch (_viewIndex)
            {
                case 0: // XY
                    dataset.LabelData.ReadSliceZ(_sliceIndex, labelData);
                    break;
                case 1: // XZ
                    for (int z = 0; z < height; z++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            labelData[z * width + x] = dataset.LabelData[x, _sliceIndex, z];
                        }
                    }
                    break;
                case 2: // YZ
                    for (int z = 0; z < height; z++)
                    {
                        for (int y = 0; y < width; y++)
                        {
                            labelData[z * width + y] = dataset.LabelData[_sliceIndex, y, z];
                        }
                    }
                    break;
            }

            return labelData;
        }

        public void UpdateSelection(Vector2 currentPos) 
        { 
            // Magic wand doesn't update continuously 
        }

        public void EndSelection()
        {
            if (!_isActive) return;
            _isActive = false;
            _manager.ApplySelectionAsync(_selectionMask, _sliceIndex, _viewIndex);
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