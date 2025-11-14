// GeoscientistToolkit/Data/CtImageStack/Segmentation/MagneticLassoTool.cs

using System.Numerics;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation;

public class MagneticLassoTool : LassoTool, ISegmentationTool
{
    private List<Vector2> _anchorPoints;
    private Vector2[] _gradientDirection;
    private byte[] _gradientMagnitude;

    public float EdgeSensitivity { get; set; } = 0.5f;
    public float SearchRadius { get; set; } = 30.0f;
    public float AnchorThreshold { get; set; } = 20.0f;

    public override string Name => "Magnetic Lasso";
    public override string Icon => "🧲";

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
        _manager.NotifyPreviewChanged(_selectionMask, SliceIndex, ViewIndex);
    }

    public override void Dispose()
    {
        base.Dispose();
        _gradientMagnitude = null;
        _gradientDirection = null;
        _anchorPoints?.Clear();
    }

    private void UpdateSelectionMaskWithNewPath(List<Vector2> path)
    {
        if (path.Count < 2) return;

        Array.Clear(_selectionMask, 0, _selectionMask.Length);

        for (var i = 0; i < path.Count - 1; i++) DrawLine(path[i], path[i + 1]);
    }

    // ALGORITHM: Sobel Edge Detection
    //
    // Computes image gradients using Sobel operators for edge detection. The gradient magnitude
    // and direction are used to guide the magnetic lasso path along high-contrast edges.
    //
    // References:
    // - Sobel, I., & Feldman, G. (1968). "A 3x3 isotropic gradient operator for image processing."
    //   Stanford Artificial Intelligence Project (SAIL).
    //
    // - Kanopoulos, N., Vasanthavada, N., & Baker, R.L. (1988). "Design of an image edge detection
    //   filter using the Sobel operator." IEEE Journal of Solid-State Circuits, 23(2), 358-367.
    //   DOI: 10.1109/4.996
    //
    private void ComputeGradients()
    {
        // --- CORRECTED: Use public properties from the base class ---
        var grayscale = _manager.GetGrayscaleSlice(SliceIndex, ViewIndex);
        _gradientMagnitude = new byte[_width * _height];
        _gradientDirection = new Vector2[_width * _height];

        int[,] sobelX = { { -1, 0, 1 }, { -2, 0, 2 }, { -1, 0, 1 } };
        int[,] sobelY = { { -1, -2, -1 }, { 0, 0, 0 }, { 1, 2, 1 } };

        Parallel.For(1, _height - 1, y =>
        {
            for (var x = 1; x < _width - 1; x++)
            {
                float gx = 0, gy = 0;

                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var idx = (y + dy) * _width + x + dx;
                    gx += grayscale[idx] * sobelX[dy + 1, dx + 1];
                    gy += grayscale[idx] * sobelY[dy + 1, dx + 1];
                }

                var index = y * _width + x;
                var magnitude = MathF.Sqrt(gx * gx + gy * gy);
                _gradientMagnitude[index] = (byte)Math.Min(255, magnitude);

                if (magnitude > 0) _gradientDirection[index] = Vector2.Normalize(new Vector2(gx, gy));
            }
        });
    }

    // ALGORITHM: Dijkstra's Shortest Path Algorithm (Intelligent Scissors)
    //
    // Finds the optimal path between two points by minimizing edge cost. The cost function is
    // inversely proportional to edge strength, causing the path to follow high-gradient regions
    // (edges). This technique is known as "Intelligent Scissors" or "Live Wire" in image editing.
    //
    // References:
    // - Dijkstra, E.W. (1959). "A note on two problems in connexion with graphs."
    //   Numerische Mathematik, 1(1), 269-271.
    //   DOI: 10.1007/BF01386390
    //
    // - Mortensen, E.N., & Barrett, W.A. (1995). "Intelligent scissors for image composition."
    //   Proceedings of the 22nd Annual Conference on Computer Graphics and Interactive Techniques
    //   (SIGGRAPH '95), 191-198.
    //   DOI: 10.1145/218380.218442
    //
    // - Mortensen, E.N., & Barrett, W.A. (1998). "Interactive segmentation with intelligent scissors."
    //   Graphical Models and Image Processing, 60(5), 349-384.
    //   DOI: 10.1006/gmip.1998.0480
    //
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

            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;

                var nx = current.X + dx;
                var ny = current.Y + dy;

                if (nx < 0 || nx >= _width || ny < 0 || ny >= _height) continue;

                var cost = CalculateEdgeCost(nx, ny);
                var newDist = distances[current] + cost;

                var neighbor = new Point(nx, ny);
                if (!distances.ContainsKey(neighbor) || newDist < distances[neighbor])
                {
                    distances[neighbor] = newDist;
                    previous[neighbor] = current;
                    queue.Enqueue(neighbor, newDist);
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

        var idx = y * _width + x;
        var edgeStrength = _gradientMagnitude[idx] / 255.0f;

        // Cost is inversely proportional to edge strength
        return 1.1f - edgeStrength * EdgeSensitivity;
    }

    // Helper struct for pathfinding
    private readonly struct Point : IEquatable<Point>
    {
        public readonly int X;
        public readonly int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(Point other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is Point other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }
    }
}