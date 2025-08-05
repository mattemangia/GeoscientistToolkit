// GeoscientistToolkit/Data/CtImageStack/Segmentation/LassoTool.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation
{
    public class LassoTool : ISegmentationTool
    {
        protected SegmentationManager _manager;
        protected List<Vector2> _points;
        protected byte[] _selectionMask;
        protected int _sliceIndex;
        protected int _viewIndex;
        protected int _width;
        protected int _height;
        protected bool _isActive;
        
        public virtual string Name => "Lasso";
        public virtual string Icon => "✂";
        
        public bool HasActiveSelection => _isActive;
        
        public float MinPointDistance { get; set; } = 2.0f;
        
        public virtual void Initialize(SegmentationManager manager)
        {
            _manager = manager;
            _points = new List<Vector2>();
        }
        
        public virtual void StartSelection(Vector2 startPos, int sliceIndex, int viewIndex)
        {
            _sliceIndex = sliceIndex;
            _viewIndex = viewIndex;
            (_width, _height) = _manager.GetSliceDimensions(viewIndex);
            
            _points.Clear();
            _points.Add(startPos);
            _selectionMask = new byte[_width * _height];
            _isActive = true;
        }
        
        public virtual void UpdateSelection(Vector2 currentPos)
        {
            if (!_isActive) return;
            
            // Only add point if it's far enough from the last point
            if (_points.Count == 0 || Vector2.Distance(_points.Last(), currentPos) >= MinPointDistance)
            {
                _points.Add(currentPos);
                UpdateSelectionMask();
                _manager.NotifyPreviewChanged(_selectionMask, _sliceIndex, _viewIndex);
            }
        }
        
        public virtual void EndSelection()
        {
            if (!_isActive || _points.Count < 3) return;
            
            // Close the polygon
            _points.Add(_points[0]);
            FillPolygon();
            
            _isActive = false;
            _manager.ApplySelectionAsync(_selectionMask, _sliceIndex, _viewIndex);
        }
        
        public void CancelSelection()
        {
            _isActive = false;
            _points.Clear();
            _selectionMask = null;
        }
        
        public byte[] GetSelectionMask()
        {
            return _selectionMask;
        }
        
        protected virtual void UpdateSelectionMask()
        {
            if (_points.Count < 2) return;
            
            // Clear mask
            Array.Clear(_selectionMask, 0, _selectionMask.Length);
            
            // Draw polygon outline
            for (int i = 0; i < _points.Count - 1; i++)
            {
                DrawLine(_points[i], _points[i + 1]);
            }
        }
        
        protected void DrawLine(Vector2 p1, Vector2 p2)
        {
            int x1 = (int)p1.X, y1 = (int)p1.Y;
            int x2 = (int)p2.X, y2 = (int)p2.Y;
            
            int dx = Math.Abs(x2 - x1);
            int dy = Math.Abs(y2 - y1);
            int sx = x1 < x2 ? 1 : -1;
            int sy = y1 < y2 ? 1 : -1;
            int err = dx - dy;
            
            while (true)
            {
                if (x1 >= 0 && x1 < _width && y1 >= 0 && y1 < _height)
                {
                    _selectionMask[y1 * _width + x1] = 255;
                }
                
                if (x1 == x2 && y1 == y2) break;
                
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x1 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y1 += sy;
                }
            }
        }
        
        protected void FillPolygon()
        {
            if (_points.Count < 3) return;
            
            // Use scanline fill algorithm
            var edges = new List<Edge>();
            
            // Build edge table
            for (int i = 0; i < _points.Count - 1; i++)
            {
                var p1 = _points[i];
                var p2 = _points[i + 1];
                
                if (Math.Abs(p1.Y - p2.Y) < 0.01f) continue; // Skip horizontal edges
                
                edges.Add(new Edge
                {
                    YMin = Math.Min(p1.Y, p2.Y),
                    YMax = Math.Max(p1.Y, p2.Y),
                    XAtYMin = p1.Y < p2.Y ? p1.X : p2.X,
                    Slope = (p2.X - p1.X) / (p2.Y - p1.Y)
                });
            }
            
            if (edges.Count == 0) return;
            
            // Sort edges by YMin
            edges.Sort((a, b) => a.YMin.CompareTo(b.YMin));
            
            int minY = Math.Max(0, (int)edges[0].YMin);
            int maxY = Math.Min(_height - 1, (int)edges.Max(e => e.YMax));
            
            // Scanline fill
            Parallel.For(minY, maxY + 1, y =>
            {
                var activeEdges = edges.Where(e => y >= e.YMin && y < e.YMax).ToList();
                if (activeEdges.Count < 2) return;
                
                // Calculate X intersections
                var intersections = new List<float>();
                foreach (var edge in activeEdges)
                {
                    float x = edge.XAtYMin + (y - edge.YMin) * edge.Slope;
                    intersections.Add(x);
                }
                
                intersections.Sort();
                
                // Fill between pairs of intersections
                for (int i = 0; i < intersections.Count - 1; i += 2)
                {
                    int x1 = Math.Max(0, (int)intersections[i]);
                    int x2 = Math.Min(_width - 1, (int)intersections[i + 1]);
                    
                    for (int x = x1; x <= x2; x++)
                    {
                        _selectionMask[y * _width + x] = 255;
                    }
                }
            });
        }
        
        private class Edge
        {
            public float YMin { get; set; }
            public float YMax { get; set; }
            public float XAtYMin { get; set; }
            public float Slope { get; set; }
        }
        
        public virtual void Dispose()
        {
            _points?.Clear();
            _selectionMask = null;
        }
    }
}