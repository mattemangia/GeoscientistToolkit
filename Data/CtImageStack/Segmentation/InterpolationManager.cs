// GeoscientistToolkit/Data/CtImageStack/Segmentation/InterpolationManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation
{
    public class InterpolationManager
    {
        private readonly CtImageStackDataset _dataset;
        private readonly SegmentationManager _segmentationManager;
        
        public enum InterpolationType
        {
            Linear2D,
            ShapeInterpolation,
            Morphological3D
        }
        
        public InterpolationManager(CtImageStackDataset dataset, SegmentationManager segmentationManager)
        {
            _dataset = dataset;
            _segmentationManager = segmentationManager;
        }
        
        public async Task InterpolateSlicesAsync(
            byte[] startMask, int startSlice,
            byte[] endMask, int endSlice,
            int viewIndex, InterpolationType type)
        {
            if (startSlice == endSlice) return;
            
            // Ensure correct order
            if (startSlice > endSlice)
            {
                (startSlice, endSlice) = (endSlice, startSlice);
                (startMask, endMask) = (endMask, startMask);
            }
            
            Logger.Log($"[InterpolationManager] Interpolating between slices {startSlice} and {endSlice} using {type}");
            
            switch (type)
            {
                case InterpolationType.Linear2D:
                    await InterpolateLinear2DAsync(startMask, startSlice, endMask, endSlice, viewIndex);
                    break;
                    
                case InterpolationType.ShapeInterpolation:
                    await InterpolateShapeBasedAsync(startMask, startSlice, endMask, endSlice, viewIndex);
                    break;
                    
                case InterpolationType.Morphological3D:
                    await InterpolateMorphological3DAsync(startMask, startSlice, endMask, endSlice, viewIndex);
                    break;
            }
            
            ProjectManager.Instance.NotifyDatasetDataChanged(_dataset);
        }
        
        private async Task InterpolateLinear2DAsync(
            byte[] startMask, int startSlice,
            byte[] endMask, int endSlice,
            int viewIndex)
        {
            var (width, height) = _segmentationManager.GetSliceDimensions(viewIndex);
            int numSlices = endSlice - startSlice - 1;
            
            await Task.Run(() =>
            {
                Parallel.For(1, numSlices + 1, i =>
                {
                    float t = i / (float)(numSlices + 1);
                    var interpolatedMask = new byte[width * height];
                    
                    for (int idx = 0; idx < interpolatedMask.Length; idx++)
                    {
                        float startVal = startMask[idx] / 255.0f;
                        float endVal = endMask[idx] / 255.0f;
                        float interpolated = startVal * (1 - t) + endVal * t;
                        interpolatedMask[idx] = (byte)(interpolated * 255);
                    }
                    
                    // Apply threshold to create binary mask
                    for (int idx = 0; idx < interpolatedMask.Length; idx++)
                    {
                        interpolatedMask[idx] = interpolatedMask[idx] > 127 ? (byte)255 : (byte)0;
                    }
                    
                    ApplyMaskToSlice(interpolatedMask, startSlice + i, viewIndex);
                });
            });
        }
        
        private async Task InterpolateShapeBasedAsync(
            byte[] startMask, int startSlice,
            byte[] endMask, int endSlice,
            int viewIndex)
        {
            var (width, height) = _segmentationManager.GetSliceDimensions(viewIndex);
            
            // Extract contours from both masks
            var startContours = ExtractContours(startMask, width, height);
            var endContours = ExtractContours(endMask, width, height);
            
            if (startContours.Count == 0 || endContours.Count == 0) return;
            
            // Match contours between slices
            var matchedPairs = MatchContours(startContours, endContours);
            
            int numSlices = endSlice - startSlice - 1;
            
            await Task.Run(() =>
            {
                Parallel.For(1, numSlices + 1, i =>
                {
                    float t = i / (float)(numSlices + 1);
                    var interpolatedMask = new byte[width * height];
                    
                    foreach (var (startContour, endContour) in matchedPairs)
                    {
                        var interpolatedContour = InterpolateContour(startContour, endContour, t);
                        FillContour(interpolatedMask, interpolatedContour, width, height);
                    }
                    
                    ApplyMaskToSlice(interpolatedMask, startSlice + i, viewIndex);
                });
            });
        }
        
        private async Task InterpolateMorphological3DAsync(
            byte[] startMask, int startSlice,
            byte[] endMask, int endSlice,
            int viewIndex)
        {
            var (width, height) = _segmentationManager.GetSliceDimensions(viewIndex);
            int depth = endSlice - startSlice + 1;
            
            // Create 3D volume for morphological operations
            var volume = new byte[width, height, depth];
            
            // Set boundary conditions
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    volume[x, y, 0] = startMask[y * width + x];
                    volume[x, y, depth - 1] = endMask[y * width + x];
                }
            }
            
            await Task.Run(() =>
            {
                // Apply 3D morphological closing to fill gaps
                var processedVolume = ApplyMorphologicalClosing3D(volume);
                
                // Extract intermediate slices
                Parallel.For(1, depth - 1, z =>
                {
                    var interpolatedMask = new byte[width * height];
                    
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            interpolatedMask[y * width + x] = processedVolume[x, y, z];
                        }
                    }
                    
                    ApplyMaskToSlice(interpolatedMask, startSlice + z, viewIndex);
                });
            });
        }
        
        private List<List<Vector2>> ExtractContours(byte[] mask, int width, int height)
        {
            var contours = new List<List<Vector2>>();
            var visited = new bool[width * height];
            
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int idx = y * width + x;
                    
                    if (mask[idx] > 0 && !visited[idx] && IsEdgePixel(mask, x, y, width, height))
                    {
                        var contour = TraceContour(mask, visited, x, y, width, height);
                        if (contour.Count > 10) // Ignore very small contours
                        {
                            contours.Add(contour);
                        }
                    }
                }
            }
            
            return contours;
        }
        
        private bool IsEdgePixel(byte[] mask, int x, int y, int width, int height)
        {
            int idx = y * width + x;
            if (mask[idx] == 0) return false;
            
            // Check 8-neighbors
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    
                    int nx = x + dx;
                    int ny = y + dy;
                    
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        if (mask[ny * width + nx] == 0)
                            return true;
                    }
                }
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
            int dir = 0;
            
            do
            {
                contour.Add(new Vector2(x, y));
                visited[y * width + x] = true;
                
                // Find next contour point
                bool found = false;
                for (int i = 0; i < 8; i++)
                {
                    int checkDir = (dir + i) % 8;
                    int nx = x + directions[checkDir].dx;
                    int ny = y + directions[checkDir].dy;
                    
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        int idx = ny * width + nx;
                        if (mask[idx] > 0 && (!visited[idx] || (nx == startX && ny == startY && contour.Count > 2)))
                        {
                            x = nx;
                            y = ny;
                            dir = (checkDir + 6) % 8; // Start search from opposite direction
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
            List<List<Vector2>> startContours,
            List<List<Vector2>> endContours)
        {
            var pairs = new List<(List<Vector2>, List<Vector2>)>();
            var usedEnd = new bool[endContours.Count];
            
            // Simple nearest centroid matching
            foreach (var startContour in startContours)
            {
                var startCentroid = CalculateCentroid(startContour);
                float minDist = float.MaxValue;
                int bestMatch = -1;
                
                for (int i = 0; i < endContours.Count; i++)
                {
                    if (usedEnd[i]) continue;
                    
                    var endCentroid = CalculateCentroid(endContours[i]);
                    float dist = Vector2.Distance(startCentroid, endCentroid);
                    
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
            var sum = Vector2.Zero;
            foreach (var point in contour)
            {
                sum += point;
            }
            return sum / contour.Count;
        }
        
        private List<Vector2> InterpolateContour(List<Vector2> start, List<Vector2> end, float t)
        {
            // Resample contours to have equal number of points
            int targetPoints = Math.Max(start.Count, end.Count);
            var resampledStart = ResampleContour(start, targetPoints);
            var resampledEnd = ResampleContour(end, targetPoints);
            
            var interpolated = new List<Vector2>(targetPoints);
            
            for (int i = 0; i < targetPoints; i++)
            {
                interpolated.Add(Vector2.Lerp(resampledStart[i], resampledEnd[i], t));
            }
            
            return interpolated;
        }
        
        private List<Vector2> ResampleContour(List<Vector2> contour, int targetPoints)
        {
            if (contour.Count == targetPoints) return new List<Vector2>(contour);
            
            var resampled = new List<Vector2>(targetPoints);
            float totalLength = 0;
            
            // Calculate total contour length
            for (int i = 0; i < contour.Count; i++)
            {
                int next = (i + 1) % contour.Count;
                totalLength += Vector2.Distance(contour[i], contour[next]);
            }
            
            float segmentLength = totalLength / targetPoints;
            float currentDist = 0;
            int currentIndex = 0;
            
            resampled.Add(contour[0]);
            
            for (int i = 1; i < targetPoints; i++)
            {
                float targetDist = i * segmentLength;
                
                while (currentDist < targetDist && currentIndex < contour.Count - 1)
                {
                    int next = (currentIndex + 1) % contour.Count;
                    float edgeLength = Vector2.Distance(contour[currentIndex], contour[next]);
                    
                    if (currentDist + edgeLength >= targetDist)
                    {
                        float t = (targetDist - currentDist) / edgeLength;
                        resampled.Add(Vector2.Lerp(contour[currentIndex], contour[next], t));
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
            // Create edge list for scanline fill
            var edges = new List<Edge>();
            
            for (int i = 0; i < contour.Count; i++)
            {
                var p1 = contour[i];
                var p2 = contour[(i + 1) % contour.Count];
                
                if (Math.Abs(p1.Y - p2.Y) < 0.01f) continue;
                
                edges.Add(new Edge
                {
                    YMin = Math.Min(p1.Y, p2.Y),
                    YMax = Math.Max(p1.Y, p2.Y),
                    XAtYMin = p1.Y < p2.Y ? p1.X : p2.X,
                    Slope = (p2.X - p1.X) / (p2.Y - p1.Y)
                });
            }
            
            if (edges.Count == 0) return;
            
            int minY = Math.Max(0, (int)edges.Min(e => e.YMin));
            int maxY = Math.Min(height - 1, (int)edges.Max(e => e.YMax));
            
            for (int y = minY; y <= maxY; y++)
            {
                var activeEdges = edges.Where(e => y >= e.YMin && y < e.YMax).ToList();
                if (activeEdges.Count < 2) continue;
                
                var intersections = new List<float>();
                foreach (var edge in activeEdges)
                {
                    float x = edge.XAtYMin + (y - edge.YMin) * edge.Slope;
                    intersections.Add(x);
                }
                
                intersections.Sort();
                
                for (int i = 0; i < intersections.Count - 1; i += 2)
                {
                    int x1 = Math.Max(0, (int)intersections[i]);
                    int x2 = Math.Min(width - 1, (int)intersections[i + 1]);
                    
                    for (int x = x1; x <= x2; x++)
                    {
                        mask[y * width + x] = 255;
                    }
                }
            }
        }
        
        private byte[,,] ApplyMorphologicalClosing3D(byte[,,] volume)
        {
            int width = volume.GetLength(0);
            int height = volume.GetLength(1);
            int depth = volume.GetLength(2);
            
            // Simple 3D closing operation (dilation followed by erosion)
            var dilated = new byte[width, height, depth];
            var closed = new byte[width, height, depth];
            
            // 3D dilation
            Parallel.For(1, depth - 1, z =>
            {
                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        byte maxVal = 0;
                        
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    maxVal = Math.Max(maxVal, volume[x + dx, y + dy, z + dz]);
                                }
                            }
                        }
                        
                        dilated[x, y, z] = maxVal;
                    }
                }
            });
            
            // Copy boundaries
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    dilated[x, y, 0] = volume[x, y, 0];
                    dilated[x, y, depth - 1] = volume[x, y, depth - 1];
                }
            }
            
            // 3D erosion
            Parallel.For(1, depth - 1, z =>
            {
                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        byte minVal = 255;
                        
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    minVal = Math.Min(minVal, dilated[x + dx, y + dy, z + dz]);
                                }
                            }
                        }
                        
                        closed[x, y, z] = minVal;
                    }
                }
            });
            
            // Copy boundaries again
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    closed[x, y, 0] = volume[x, y, 0];
                    closed[x, y, depth - 1] = volume[x, y, depth - 1];
                }
            }
            
            return closed;
        }
        
        private void ApplyMaskToSlice(byte[] mask, int sliceIndex, int viewIndex)
        {
            var action = new SegmentationAction
            {
                MaterialId = _segmentationManager.TargetMaterialId,
                IsAddOperation = _segmentationManager.IsAddMode,
                SliceIndex = sliceIndex,
                ViewIndex = viewIndex,
                SelectionMask = mask
            };
            
            ApplyMaskToVolume(mask, sliceIndex, viewIndex);
        }
        
        private void ApplyMaskToVolume(byte[] mask, int sliceIndex, int viewIndex)
        {
            var labels = _dataset.LabelData;
            var materialId = _segmentationManager.TargetMaterialId;
            var isAdd = _segmentationManager.IsAddMode;
            
            switch (viewIndex)
            {
                case 0: // XY view
                    var labelSlice = new byte[_dataset.Width * _dataset.Height];
                    labels.ReadSliceZ(sliceIndex, labelSlice);
                    
                    for (int i = 0; i < mask.Length; i++)
                    {
                        if (mask[i] > 0)
                        {
                            labelSlice[i] = isAdd ? materialId : (byte)0;
                        }
                    }
                    
                    labels.WriteSliceZ(sliceIndex, labelSlice);
                    break;
                    
                case 1: // XZ view
                    for (int z = 0; z < _dataset.Depth; z++)
                    {
                        for (int x = 0; x < _dataset.Width; x++)
                        {
                            int maskIdx = z * _dataset.Width + x;
                            if (mask[maskIdx] > 0)
                            {
                                labels[x, sliceIndex, z] = isAdd ? materialId : (byte)0;
                            }
                        }
                    }
                    break;
                    
                case 2: // YZ view
                    for (int z = 0; z < _dataset.Depth; z++)
                    {
                        for (int y = 0; y < _dataset.Height; y++)
                        {
                            int maskIdx = z * _dataset.Height + y;
                            if (mask[maskIdx] > 0)
                            {
                                labels[sliceIndex, y, z] = isAdd ? materialId : (byte)0;
                            }
                        }
                    }
                    break;
            }
        }
        
        private class Edge
        {
            public float YMin { get; set; }
            public float YMax { get; set; }
            public float XAtYMin { get; set; }
            public float Slope { get; set; }
        }
    }
}