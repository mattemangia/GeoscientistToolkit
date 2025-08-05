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

            var lastAnchor = _anchorPoints.LastOrDefault();
            if (Vector2.Distance(lastAnchor, currentPos) > AnchorThreshold)
            {
                var optimalPathSegment = FindOptimalPath(lastAnchor, currentPos);
                _points.AddRange(optimalPathSegment);
                _anchorPoints.Add(_points.Last());
            }

            // --- CORRECTED: Use public properties from the base class ---
            var livePath = FindOptimalPath(_anchorPoints.Last(), currentPos);
            var fullPath = new List<Vector2>(_points);
            fullPath.AddRange(livePath);

            UpdateSelectionMaskWithNewPath(fullPath);
            _manager.NotifyPreviewChanged(_selectionMask, this.SliceIndex, this.ViewIndex);
        }

        private void UpdateSelectionMaskWithNewPath(List<Vector2> path)
        {
            if (path.Count < 2) return;

            Array.Clear(_selectionMask, 0, _selectionMask.Length);

            for (int i = 0; i < path.Count - 1; i++)
            {
                DrawLine(path[i], path[i + 1]);
            }
        }

        private void ComputeGradients()
        {
            // --- CORRECTED: Use public properties from the base class ---
            var grayscale = _manager.GetGrayscaleSlice(this.SliceIndex, this.ViewIndex);
            _gradientMagnitude = new byte[_width * _height];
            _gradientDirection = new Vector2[_width * _height];

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
            var path = new List<Vector2>();
            int startX = (int)start.X, startY = (int)start.Y;
            int endX = (int)end.X, endY = (int)end.Y;

            if (startX == endX && startY == endY) return path;

            // Simple Dijkstra's algorithm for path finding
            var distances = new Dictionary<Point, float>();
            var previous = new Dictionary<Point, Point>();
            var queue = new PriorityQueue<Point, float>();

            var startPoint = new Point(startX, startY);
            distances[startPoint] = 0;
            queue.Enqueue(startPoint, 0);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (current.X == endX && current.Y == endY) break;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        int nx = current.X + dx;
                        int ny = current.Y + dy;

                        if (nx < 0 || nx >= _width || ny < 0 || ny >= _height) continue;

                        float cost = CalculateEdgeCost(nx, ny);
                        float newDist = distances[current] + cost;

                        var neighbor = new Point(nx, ny);
                        if (!distances.ContainsKey(neighbor) || newDist < distances[neighbor])
                        {
                            distances[neighbor] = newDist;
                            previous[neighbor] = current;
                            queue.Enqueue(neighbor, newDist);
                        }
                    }
                }
            }

            // Reconstruct path
            var pathPoint = new Point(endX, endY);
            while (previous.ContainsKey(pathPoint))
            {
                path.Add(new Vector2(pathPoint.X, pathPoint.Y));
                pathPoint = previous[pathPoint];
            }
            path.Reverse();
            return path;
        }

        private float CalculateEdgeCost(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return float.MaxValue;

            int idx = y * _width + x;
            float edgeStrength = _gradientMagnitude[idx] / 255.0f;

            // Cost is inversely proportional to edge strength
            return 1.1f - (edgeStrength * EdgeSensitivity);
        }

        public override void Dispose()
        {
            base.Dispose();
            _gradientMagnitude = null;
            _gradientDirection = null;
            _anchorPoints?.Clear();
        }

        // Helper struct for pathfinding
        private readonly struct Point : IEquatable<Point>
        {
            public readonly int X;
            public readonly int Y;
            public Point(int x, int y) { X = x; Y = y; }
            public bool Equals(Point other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is Point other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y);
        }
    }
}