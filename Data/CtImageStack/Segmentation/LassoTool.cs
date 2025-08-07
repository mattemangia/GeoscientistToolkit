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
        protected int _width;
        protected int _height;
        protected bool _isActive;

        public virtual string Name => "Lasso";
        public virtual string Icon => "✂";

        public bool HasActiveSelection => _isActive;

        // --- ADDED: Public properties for the interface ---
        public int SliceIndex { get; protected set; }
        public int ViewIndex { get; protected set; }

        public float MinPointDistance { get; set; } = 2.0f;

        public virtual void Initialize(SegmentationManager manager)
        {
            _manager = manager;
            _points = new List<Vector2>();
        }

        public virtual void StartSelection(Vector2 startPos, int sliceIndex, int viewIndex)
        {
            this.SliceIndex = sliceIndex;
            this.ViewIndex = viewIndex;
            (_width, _height) = _manager.GetSliceDimensions(viewIndex);

            _points.Clear();
            _points.Add(startPos);
            _selectionMask = new byte[_width * _height];
            _isActive = true;
        }

        public virtual void UpdateSelection(Vector2 currentPos)
        {
            if (!_isActive) return;

            if (_points.Count == 0 || Vector2.Distance(_points.Last(), currentPos) >= MinPointDistance)
            {
                _points.Add(currentPos);
                UpdateSelectionMask();
                _manager.NotifyPreviewChanged(_selectionMask, this.SliceIndex, this.ViewIndex);
            }
        }

        public virtual void EndSelection()
        {
            if (!_isActive || _points.Count < 3)
            {
                CancelSelection();
                return;
            }

            _points.Add(_points[0]);
            FillPolygon();

            // The selection is now finalized.
            // The manager will call GetSelectionMask() and then CancelSelection().
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
            Array.Clear(_selectionMask, 0, _selectionMask.Length);
            for (int i = 0; i < _points.Count - 1; i++)
            {
                DrawLine(_points[i], _points[i + 1]);
            }
        }

        protected void DrawLine(Vector2 p1, Vector2 p2)
        {
            int x1 = (int)p1.X, y1 = (int)p1.Y;
            int x2 = (int)p2.X, y2 = (int)p2.Y;

            int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
            int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
            int err = dx + dy, e2;

            while (true)
            {
                if (x1 >= 0 && x1 < _width && y1 >= 0 && y1 < _height)
                    _selectionMask[y1 * _width + x1] = 255;
                if (x1 == x2 && y1 == y2) break;
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x1 += sx; }
                if (e2 <= dx) { err += dx; y1 += sy; }
            }
        }

        protected void FillPolygon()
        {
            if (_points.Count < 3) return;

            var edges = new List<Edge>();
            for (int i = 0; i < _points.Count - 1; i++)
            {
                var p1 = _points[i];
                var p2 = _points[i + 1];
                if (Math.Abs(p1.Y - p2.Y) > 0.01f)
                {
                    edges.Add(new Edge
                    {
                        YMin = Math.Min(p1.Y, p2.Y),
                        YMax = Math.Max(p1.Y, p2.Y),
                        XAtYMin = p1.Y < p2.Y ? p1.X : p2.X,
                        Slope = (p2.X - p1.X) / (p2.Y - p1.Y)
                    });
                }
            }

            if (edges.Count == 0) return;

            int minY = Math.Max(0, (int)Math.Ceiling(edges.Min(e => e.YMin)));
            int maxY = Math.Min(_height - 1, (int)Math.Floor(edges.Max(e => e.YMax)));

            var activeEdges = new List<Edge>();
            var intersections = new List<float>();

            for (int y = minY; y <= maxY; y++)
            {
                activeEdges.RemoveAll(e => y >= e.YMax);
                activeEdges.AddRange(edges.Where(e => Math.Abs(e.YMin - y) < 0.01f));

                intersections.Clear();
                foreach (var edge in activeEdges)
                {
                    intersections.Add(edge.XAtYMin + (y - edge.YMin) * edge.Slope);
                }

                intersections.Sort();

                for (int i = 0; i < intersections.Count - 1; i += 2)
                {
                    int xStart = Math.Max(0, (int)Math.Ceiling(intersections[i]));
                    int xEnd = Math.Min(_width - 1, (int)Math.Floor(intersections[i + 1]));
                    for (int x = xStart; x <= xEnd; x++)
                    {
                        _selectionMask[y * _width + x] = 255;
                    }
                }
            }
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