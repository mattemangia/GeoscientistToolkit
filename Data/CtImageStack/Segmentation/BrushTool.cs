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
        private int _sliceIndex;
        private int _viewIndex;
        private int _width;
        private int _height;
        private Vector2 _lastPos;
        private bool _isActive;
        
        public string Name => "Brush";
        public string Icon => "🖌";
        
        public float BrushSize { get; set; } = 10.0f;
        public BrushShape Shape { get; set; } = BrushShape.Circle;
        public float Hardness { get; set; } = 1.0f;
        
        public bool HasActiveSelection => _isActive;
        
        public enum BrushShape
        {
            Circle,
            Square
        }
        
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
            _lastPos = startPos;
            _isActive = true;
            
            ApplyBrush(startPos);
            _manager.NotifyPreviewChanged(_selectionMask, _sliceIndex, _viewIndex);
        }
        
        public void UpdateSelection(Vector2 currentPos)
        {
            if (!_isActive) return;
            
            // Interpolate between last and current position for smooth strokes
            var distance = Vector2.Distance(_lastPos, currentPos);
            var steps = Math.Max(1, (int)(distance / (BrushSize * 0.25f)));
            
            for (int i = 0; i <= steps; i++)
            {
                var t = steps > 0 ? i / (float)steps : 0;
                var pos = Vector2.Lerp(_lastPos, currentPos, t);
                ApplyBrush(pos);
            }
            
            _lastPos = currentPos;
            _manager.NotifyPreviewChanged(_selectionMask, _sliceIndex, _viewIndex);
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
        
        private void ApplyBrush(Vector2 pos)
        {
            int centerX = (int)pos.X;
            int centerY = (int)pos.Y;
            int radius = (int)Math.Ceiling(BrushSize);
            
            // Use parallel processing for large brushes
            if (radius > 20)
            {
                Parallel.For(-radius, radius + 1, dy =>
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        ProcessBrushPixel(centerX + dx, centerY + dy, centerX, centerY);
                    }
                });
            }
            else
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        ProcessBrushPixel(centerX + dx, centerY + dy, centerX, centerY);
                    }
                }
            }
        }
        
        private void ProcessBrushPixel(int x, int y, int centerX, int centerY)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return;
            
            float distance = 0;
            bool inBrush = false;
            
            switch (Shape)
            {
                case BrushShape.Circle:
                    distance = MathF.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                    inBrush = distance <= BrushSize;
                    break;
                    
                case BrushShape.Square:
                    var dx = Math.Abs(x - centerX);
                    var dy = Math.Abs(y - centerY);
                    distance = Math.Max(dx, dy);
                    inBrush = dx <= BrushSize && dy <= BrushSize;
                    break;
            }
            
            if (inBrush)
            {
                int index = y * _width + x;
                
                if (Hardness >= 1.0f)
                {
                    _selectionMask[index] = 255;
                }
                else
                {
                    // Soft brush with falloff
                    float normalizedDist = distance / BrushSize;
                    float intensity = 1.0f - MathF.Pow(normalizedDist, 1.0f / Hardness);
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