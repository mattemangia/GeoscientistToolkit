// GeoscientistToolkit/Data/CtImageStack/Segmentation/MagneticLassoTool.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation
{
    public class MagneticLassoTool : LassoTool, ISegmentationTool
    {
        private byte[] _gradientMagnitude;
        private Vector2[] _gradientDirection;
        private float[,] _costMap;
        private List<Vector2> _anchorPoints;
        
        public override string Name => "Magnetic Lasso";
        public override string Icon => "🧲";
        
        public float EdgeSensitivity { get; set; } = 0.5f;
        public float SearchRadius { get; set; } = 30.0f;
        public float AnchorThreshold { get; set; } = 20.0f;
        
        public override void Initialize(SegmentationManager manager)
        {
            base.Initialize(manager);
            _anchorPoints = new List<Vector2>();
        }
        
        public override void StartSelection(Vector2 startPos, int sliceIndex, int viewIndex)
        {
            base.StartSelection(startPos, sliceIndex, viewIndex);
            
            // Compute gradient information for edge detection
            ComputeGradients();
            _anchorPoints.Clear();
            _anchorPoints.Add(startPos);
        }
        
        public override void UpdateSelection(Vector2 currentPos)
        {
            if (!_isActive) return;
            
            // Check if we need a new anchor point
            var lastAnchor = _anchorPoints.Last();
            if (Vector2.Distance(lastAnchor, currentPos) > AnchorThreshold)
            {
                _anchorPoints.Add(currentPos);
            }
            
            // Find the best path from the last anchor to current position
            var path = FindOptimalPath(_anchorPoints.Last(), currentPos);
            
            // Update points with the optimal path
            _points.Clear();
            for (int i = 0; i < _anchorPoints.Count - 1; i++)
            {
                _points.Add(_anchorPoints[i]);
            }
            _points.AddRange(path);
            
            UpdateSelectionMask();
            _manager.NotifyPreviewChanged(_selectionMask, _sliceIndex, _viewIndex);
        }
        
        private void ComputeGradients()
        {
            var grayscale = _manager.GetGrayscaleSlice(_sliceIndex, _viewIndex);
            _gradientMagnitude = new byte[_width * _height];
            _gradientDirection = new Vector2[_width * _height];
            
            // Sobel edge detection
            int[,] sobelX = { { -1, 0, 1 }, { -2, 0, 2 }, { -1, 0, 1 } };
            int[,] sobelY = { { -1, -2, -1 }, { 0, 0, 0 }, { 1, 2, 1 } };
            
            Parallel.For(1, _height - 1, y =>
            {
                for (int x = 1; x < _width - 1; x++)
                {
                    float gx = 0, gy = 0;
                    
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int idx = (y + dy) * _width + (x + dx);
                            gx += grayscale[idx] * sobelX[dy + 1, dx + 1];
                            gy += grayscale[idx] * sobelY[dy + 1, dx + 1];
                        }
                    }
                    
                    int index = y * _width + x;
                    float magnitude = MathF.Sqrt(gx * gx + gy * gy);
                    _gradientMagnitude[index] = (byte)Math.Min(255, magnitude);
                    
                    if (magnitude > 0)
                    {
                        _gradientDirection[index] = Vector2.Normalize(new Vector2(gx, gy));
                    }
                }
            });
        }
        
        private List<Vector2> FindOptimalPath(Vector2 start, Vector2 end)
        {
            // Simplified Dijkstra's algorithm for path finding
            int startX = (int)start.X, startY = (int)start.Y;
            int endX = (int)end.X, endY = (int)end.Y;
            
            // Limit search area for performance
            int minX = Math.Max(0, Math.Min(startX, endX) - (int)SearchRadius);
            int maxX = Math.Min(_width - 1, Math.Max(startX, endX) + (int)SearchRadius);
            int minY = Math.Max(0, Math.Min(startY, endY) - (int)SearchRadius);
            int maxY = Math.Min(_height - 1, Math.Max(startY, endY) + (int)SearchRadius);
            
            int searchWidth = maxX - minX + 1;
            int searchHeight = maxY - minY + 1;
            
            var distances = new float[searchHeight, searchWidth];
            var visited = new bool[searchHeight, searchWidth];
            var previous = new (int, int)[searchHeight, searchWidth];
            
            // Initialize distances
            for (int y = 0; y < searchHeight; y++)
            {
                for (int x = 0; x < searchWidth; x++)
                {
                    distances[y, x] = float.MaxValue;
                }
            }
            
            distances[startY - minY, startX - minX] = 0;
            
            var queue = new SortedSet<(float dist, int x, int y)>(
                Comparer<(float, int, int)>.Create((a, b) => 
                    a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : 
                    a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : 
                    a.Item3.CompareTo(b.Item3)));
            
            queue.Add((0, startX - minX, startY - minY));
            
            // 8-connected neighbors
            int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };
            
            while (queue.Count > 0)
            {
                var current = queue.First();
                queue.Remove(current);
                
                int cx = current.Item2;
                int cy = current.Item3;
                
                if (visited[cy, cx]) continue;
                visited[cy, cx] = true;
                
                if (cx + minX == endX && cy + minY == endY)
                    break;
                
                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + dx[i];
                    int ny = cy + dy[i];
                    
                    if (nx < 0 || nx >= searchWidth || ny < 0 || ny >= searchHeight)
                        continue;
                    
                    if (visited[ny, nx]) continue;
                    
                    float cost = CalculateEdgeCost(cx + minX, cy + minY, nx + minX, ny + minY);
                    float newDist = distances[cy, cx] + cost;
                    
                    if (newDist < distances[ny, nx])
                    {
                        distances[ny, nx] = newDist;
                        previous[ny, nx] = (cx, cy);
                        queue.Add((newDist, nx, ny));
                    }
                }
            }
            
            // Reconstruct path
            var path = new List<Vector2>();
            int px = endX - minX, py = endY - minY;
            
            while (px != startX - minX || py != startY - minY)
            {
                path.Add(new Vector2(px + minX, py + minY));
                var prev = previous[py, px];
                px = prev.Item1;
                py = prev.Item2;
            }
            
            path.Reverse();
            return path;
        }
        
        private float CalculateEdgeCost(int x1, int y1, int x2, int y2)
        {
            if (x2 < 0 || x2 >= _width || y2 < 0 || y2 >= _height)
                return float.MaxValue;
            
            int idx = y2 * _width + x2;
            float edgeStrength = _gradientMagnitude[idx] / 255.0f;
            
            // Lower cost for stronger edges
            float edgeCost = 1.0f - (edgeStrength * EdgeSensitivity);
            
            // Add distance cost
            float dist = MathF.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            
            return edgeCost * dist;
        }
        
        public override void Dispose()
        {
            base.Dispose();
            _gradientMagnitude = null;
            _gradientDirection = null;
            _costMap = null;
            _anchorPoints?.Clear();
        }
    }
}