// GeoscientistToolkit/Data/CtImageStack/Segmentation/InterpolationManager.cs

using System.Collections.Concurrent;
using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation;

public class InterpolationManager
{
    public enum InterpolationType
    {
        Linear2D,
        ShapeInterpolation,
        Morphological3D
    }

    private readonly CtImageStackDataset _dataset;
    private readonly SegmentationManager _segmentationManager;

    public InterpolationManager(CtImageStackDataset dataset, SegmentationManager segmentationManager)
    {
        _dataset = dataset;
        _segmentationManager = segmentationManager;
    }

    public Dictionary<(int slice, int view), byte[]> InterpolateSlices(
        byte[] startMask, int startSlice,
        byte[] endMask, int endSlice,
        int viewIndex, InterpolationType type)
    {
        if (startSlice == endSlice) return new Dictionary<(int slice, int view), byte[]>();

        if (startSlice > endSlice)
        {
            (startSlice, endSlice) = (endSlice, startSlice);
            (startMask, endMask) = (endMask, startMask);
        }

        Logger.Log($"[InterpolationManager] Interpolating between slices {startSlice} and {endSlice} using {type}");

        return type switch
        {
            InterpolationType.Linear2D => InterpolateLinear2D(startMask, startSlice, endMask, endSlice, viewIndex),
            InterpolationType.ShapeInterpolation => InterpolateShapeBased(startMask, startSlice, endMask, endSlice,
                viewIndex),
            InterpolationType.Morphological3D => InterpolateMorphological3D(startMask, startSlice, endMask, endSlice,
                viewIndex),
            _ => new Dictionary<(int slice, int view), byte[]>()
        };
    }

    private Dictionary<(int slice, int view), byte[]> InterpolateLinear2D(
        byte[] startMask, int startSlice, byte[] endMask, int endSlice, int viewIndex)
    {
        var (width, height) = _segmentationManager.GetSliceDimensions(viewIndex);
        var numSlices = endSlice - startSlice - 1;
        var results = new ConcurrentDictionary<(int, int), byte[]>();

        Parallel.For(1, numSlices + 1, i =>
        {
            var t = i / (float)(numSlices + 1);
            var interpolatedMask = new byte[width * height];

            for (var idx = 0; idx < interpolatedMask.Length; idx++)
            {
                var startVal = startMask[idx] / 255.0f;
                var endVal = endMask[idx] / 255.0f;
                var interpolated = startVal * (1 - t) + endVal * t;
                interpolatedMask[idx] = (byte)(interpolated * 255);
            }

            for (var idx = 0; idx < interpolatedMask.Length; idx++)
                interpolatedMask[idx] = interpolatedMask[idx] > 127 ? (byte)255 : (byte)0;

            results.TryAdd((startSlice + i, viewIndex), interpolatedMask);
        });
        return new Dictionary<(int, int), byte[]>(results);
    }

    private Dictionary<(int slice, int view), byte[]> InterpolateShapeBased(
        byte[] startMask, int startSlice, byte[] endMask, int endSlice, int viewIndex)
    {
        var (width, height) = _segmentationManager.GetSliceDimensions(viewIndex);
        var results = new ConcurrentDictionary<(int, int), byte[]>();

        var startContours = ExtractContours(startMask, width, height);
        var endContours = ExtractContours(endMask, width, height);

        if (startContours.Count == 0 || endContours.Count == 0) return new Dictionary<(int, int), byte[]>();

        var matchedPairs = MatchContours(startContours, endContours);
        var numSlices = endSlice - startSlice - 1;

        if (numSlices <= 0) return new Dictionary<(int, int), byte[]>();

        Parallel.For(1, numSlices + 1, i =>
        {
            var t = i / (float)(numSlices + 1);
            var interpolatedMask = new byte[width * height];

            foreach (var (startContour, endContour) in matchedPairs)
            {
                var interpolatedContour = InterpolateContour(startContour, endContour, t);
                FillContour(interpolatedMask, interpolatedContour, width, height);
            }

            results.TryAdd((startSlice + i, viewIndex), interpolatedMask);
        });
        return new Dictionary<(int, int), byte[]>(results);
    }

    private Dictionary<(int slice, int view), byte[]> InterpolateMorphological3D(
        byte[] startMask, int startSlice, byte[] endMask, int endSlice, int viewIndex)
    {
        var (width, height) = _segmentationManager.GetSliceDimensions(viewIndex);
        var depth = endSlice - startSlice + 1;
        var results = new ConcurrentDictionary<(int, int), byte[]>();

        var volume = new byte[width, height, depth];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            volume[x, y, 0] = startMask[y * width + x];
            volume[x, y, depth - 1] = endMask[y * width + x];
        }

        var processedVolume = ApplyMorphologicalClosing3D(volume);

        Parallel.For(1, depth - 1, z =>
        {
            var interpolatedMask = new byte[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                interpolatedMask[y * width + x] = processedVolume[x, y, z];

            results.TryAdd((startSlice + z, viewIndex), interpolatedMask);
        });
        return new Dictionary<(int, int), byte[]>(results);
    }

    // --- ALL HELPER METHODS BELOW ARE UNCHANGED AND FULLY IMPLEMENTED ---

    private List<List<Vector2>> ExtractContours(byte[] mask, int width, int height)
    {
        var contours = new List<List<Vector2>>();
        var visited = new bool[width * height];

        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            var idx = y * width + x;

            if (mask[idx] > 0 && !visited[idx] && IsEdgePixel(mask, x, y, width, height))
            {
                var contour = TraceContour(mask, visited, x, y, width, height);
                if (contour.Count > 10) contours.Add(contour);
            }
        }

        return contours;
    }

    private bool IsEdgePixel(byte[] mask, int x, int y, int width, int height)
    {
        var idx = y * width + x;
        if (mask[idx] == 0) return false;

        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && nx < width && ny >= 0 && ny < height && mask[ny * width + nx] == 0)
                return true;
        }

        return false;
    }

    private List<Vector2> TraceContour(byte[] mask, bool[] visited, int startX, int startY, int width, int height)
    {
        var contour = new List<Vector2>();
        var directions = new (int dx, int dy)[]
        {
            (1, 0), (1, 1), (0, 1), (-1, 1),
            (-1, 0), (-1, -1), (0, -1), (1, -1)
        };

        int x = startX, y = startY;
        var dir = 0;

        do
        {
            contour.Add(new Vector2(x, y));
            visited[y * width + x] = true;

            var found = false;
            for (var i = 0; i < 8; i++)
            {
                var checkDir = (dir + i) % 8;
                var nx = x + directions[checkDir].dx;
                var ny = y + directions[checkDir].dy;

                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    var idx = ny * width + nx;
                    if (mask[idx] > 0 && (!visited[idx] || (nx == startX && ny == startY && contour.Count > 2)))
                    {
                        x = nx;
                        y = ny;
                        dir = (checkDir + 6) % 8;
                        found = true;
                        break;
                    }
                }
            }

            if (!found) break;
        } while (x != startX || y != startY || contour.Count < 3);

        return contour;
    }

    private List<(List<Vector2>, List<Vector2>)> MatchContours(
        List<List<Vector2>> startContours, List<List<Vector2>> endContours)
    {
        var pairs = new List<(List<Vector2>, List<Vector2>)>();
        var usedEnd = new bool[endContours.Count];

        foreach (var startContour in startContours)
        {
            var startCentroid = CalculateCentroid(startContour);
            var minDist = float.MaxValue;
            var bestMatch = -1;

            for (var i = 0; i < endContours.Count; i++)
            {
                if (usedEnd[i]) continue;
                var endCentroid = CalculateCentroid(endContours[i]);
                var dist = Vector2.Distance(startCentroid, endCentroid);

                if (dist < minDist)
                {
                    minDist = dist;
                    bestMatch = i;
                }
            }

            if (bestMatch >= 0)
            {
                pairs.Add((startContour, endContours[bestMatch]));
                usedEnd[bestMatch] = true;
            }
        }

        return pairs;
    }

    private Vector2 CalculateCentroid(List<Vector2> contour)
    {
        if (contour.Count == 0) return Vector2.Zero;
        var sum = Vector2.Zero;
        foreach (var point in contour) sum += point;
        return sum / contour.Count;
    }

    private List<Vector2> InterpolateContour(List<Vector2> start, List<Vector2> end, float t)
    {
        var targetPoints = Math.Max(start.Count, end.Count);
        if (targetPoints == 0) return new List<Vector2>();

        var resampledStart = ResampleContour(start, targetPoints);
        var resampledEnd = ResampleContour(end, targetPoints);

        var interpolated = new List<Vector2>(targetPoints);
        for (var i = 0; i < targetPoints; i++) interpolated.Add(Vector2.Lerp(resampledStart[i], resampledEnd[i], t));
        return interpolated;
    }

    private List<Vector2> ResampleContour(List<Vector2> contour, int targetPoints)
    {
        if (contour.Count == targetPoints) return new List<Vector2>(contour);
        if (contour.Count < 2) return new List<Vector2>();

        var resampled = new List<Vector2>(targetPoints);
        float totalLength = 0;

        for (var i = 0; i < contour.Count; i++)
            totalLength += Vector2.Distance(contour[i], contour[(i + 1) % contour.Count]);

        if (totalLength < 1e-6) return new List<Vector2>();

        var segmentLength = totalLength / targetPoints;
        float currentDist = 0;
        var currentIndex = 0;

        resampled.Add(contour[0]);

        for (var i = 1; i < targetPoints; i++)
        {
            var targetDist = i * segmentLength;
            while (currentDist < targetDist && currentIndex < contour.Count)
            {
                var next = (currentIndex + 1) % contour.Count;
                var edgeLength = Vector2.Distance(contour[currentIndex], contour[next]);

                if (currentDist + edgeLength >= targetDist)
                {
                    var lerpT = (targetDist - currentDist) / edgeLength;
                    resampled.Add(Vector2.Lerp(contour[currentIndex], contour[next], lerpT));
                    break;
                }

                currentDist += edgeLength;
                currentIndex++;
            }
        }

        return resampled;
    }

    private void FillContour(byte[] mask, List<Vector2> contour, int width, int height)
    {
        if (contour.Count < 3) return;

        var minY = Math.Max(0, (int)Math.Floor(contour.Min(p => p.Y)));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(contour.Max(p => p.Y)));
        var intersections = new List<float>();

        for (var y = minY; y <= maxY; y++)
        {
            intersections.Clear();
            for (var i = 0; i < contour.Count; i++)
            {
                var p1 = contour[i];
                var p2 = contour[(i + 1) % contour.Count];

                if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                    if (Math.Abs(p2.Y - p1.Y) > 1e-6)
                    {
                        var x = (y - p1.Y) * (p2.X - p1.X) / (p2.Y - p1.Y) + p1.X;
                        intersections.Add(x);
                    }
            }

            intersections.Sort();

            for (var i = 0; i < intersections.Count - 1; i += 2)
            {
                var xStart = Math.Max(0, (int)Math.Ceiling(intersections[i]));
                var xEnd = Math.Min(width - 1, (int)Math.Floor(intersections[i + 1]));
                for (var x = xStart; x <= xEnd; x++) mask[y * width + x] = 255;
            }
        }
    }

    private byte[,,] ApplyMorphologicalClosing3D(byte[,,] volume)
    {
        var width = volume.GetLength(0);
        var height = volume.GetLength(1);
        var depth = volume.GetLength(2);

        var dilated = new byte[width, height, depth];
        var closed = new byte[width, height, depth];

        Parallel.For(1, depth - 1, z =>
        {
            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                byte maxVal = 0;
                for (var dz = -1; dz <= 1; dz++)
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    maxVal = Math.Max(maxVal, volume[x + dx, y + dy, z + dz]);
                dilated[x, y, z] = maxVal;
            }
        });

        Parallel.For(1, depth - 1, z =>
        {
            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                byte minVal = 255;
                for (var dz = -1; dz <= 1; dz++)
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    minVal = Math.Min(minVal, dilated[x + dx, y + dy, z + dz]);
                closed[x, y, z] = minVal;
            }
        });

        return closed;
    }
}