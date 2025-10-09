// GeoscientistToolkit/Business/GIS/GISOperations.cs

using System.Numerics;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Simplify;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
///     Complete implementations of GIS operations with real algorithms
/// </summary>
public static class GISOperationsImpl
{
    private static readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326);

    #region Terrain Analysis Implementations

    /// <summary>
    ///     Calculate slope from elevation grid using Horn's method (3x3 kernel)
    /// </summary>
    public static float[,] CalculateSlopeGrid(float[,] elevation, float cellSize)
    {
        var rows = elevation.GetLength(0);
        var cols = elevation.GetLength(1);
        var slope = new float[rows, cols];

        for (var row = 1; row < rows - 1; row++)
        for (var col = 1; col < cols - 1; col++)
        {
            // Horn's method - 3x3 kernel
            var dz_dx = (elevation[row - 1, col + 1] + 2 * elevation[row, col + 1] + elevation[row + 1, col + 1] -
                         (elevation[row - 1, col - 1] + 2 * elevation[row, col - 1] + elevation[row + 1, col - 1])) /
                        (8 * cellSize);

            var dz_dy = (elevation[row + 1, col - 1] + 2 * elevation[row + 1, col] + elevation[row + 1, col + 1] -
                         (elevation[row - 1, col - 1] + 2 * elevation[row - 1, col] + elevation[row - 1, col + 1])) /
                        (8 * cellSize);

            // Slope in degrees
            slope[row, col] = (float)(Math.Atan(Math.Sqrt(dz_dx * dz_dx + dz_dy * dz_dy)) * 180.0 / Math.PI);
        }

        return slope;
    }

    /// <summary>
    ///     Calculate aspect from elevation grid
    /// </summary>
    public static float[,] CalculateAspectGrid(float[,] elevation, float cellSize)
    {
        var rows = elevation.GetLength(0);
        var cols = elevation.GetLength(1);
        var aspect = new float[rows, cols];

        for (var row = 1; row < rows - 1; row++)
        for (var col = 1; col < cols - 1; col++)
        {
            // Calculate gradients
            var dz_dx = (elevation[row - 1, col + 1] + 2 * elevation[row, col + 1] + elevation[row + 1, col + 1] -
                         (elevation[row - 1, col - 1] + 2 * elevation[row, col - 1] + elevation[row + 1, col - 1])) /
                        (8 * cellSize);

            var dz_dy = (elevation[row + 1, col - 1] + 2 * elevation[row + 1, col] + elevation[row + 1, col + 1] -
                         (elevation[row - 1, col - 1] + 2 * elevation[row - 1, col] + elevation[row - 1, col + 1])) /
                        (8 * cellSize);

            // Aspect in degrees (0-360, clockwise from north)
            var aspectRad = (float)Math.Atan2(dz_dy, -dz_dx);
            aspect[row, col] = (float)((aspectRad * 180.0 / Math.PI + 360.0) % 360.0);
        }

        return aspect;
    }

    /// <summary>
    ///     Generate hillshade from elevation grid
    /// </summary>
    public static byte[,] GenerateHillshadeGrid(float[,] elevation, float cellSize, float azimuth = 315f,
        float altitude = 45f)
    {
        var rows = elevation.GetLength(0);
        var cols = elevation.GetLength(1);
        var hillshade = new byte[rows, cols];

        // Convert angles to radians
        var azimuthRad = azimuth * MathF.PI / 180f;
        var altitudeRad = altitude * MathF.PI / 180f;

        var zenithRad = MathF.PI / 2f - altitudeRad;

        for (var row = 1; row < rows - 1; row++)
        for (var col = 1; col < cols - 1; col++)
        {
            // Calculate slope and aspect
            var dz_dx = (elevation[row - 1, col + 1] + 2 * elevation[row, col + 1] + elevation[row + 1, col + 1] -
                         (elevation[row - 1, col - 1] + 2 * elevation[row, col - 1] + elevation[row + 1, col - 1])) /
                        (8 * cellSize);

            var dz_dy = (elevation[row + 1, col - 1] + 2 * elevation[row + 1, col] + elevation[row + 1, col + 1] -
                         (elevation[row - 1, col - 1] + 2 * elevation[row - 1, col] + elevation[row - 1, col + 1])) /
                        (8 * cellSize);

            var slopeRad = MathF.Atan(MathF.Sqrt(dz_dx * dz_dx + dz_dy * dz_dy));
            var aspectRad = MathF.Atan2(dz_dy, -dz_dx);

            // Hillshade calculation
            var hillshadeValue = MathF.Cos(zenithRad) * MathF.Cos(slopeRad) +
                                 MathF.Sin(zenithRad) * MathF.Sin(slopeRad) * MathF.Cos(azimuthRad - aspectRad);

            // Normalize to 0-255
            hillshade[row, col] = (byte)Math.Clamp(hillshadeValue * 255, 0, 255);
        }

        return hillshade;
    }

    /// <summary>
    ///     Generate contour lines from elevation grid using marching squares
    /// </summary>
    public static List<List<Vector2>> GenerateContourLines(float[,] elevation, float interval, float minElevation,
        float maxElevation, Vector2 origin, float cellSize)
    {
        var contours = new List<List<Vector2>>();
        var rows = elevation.GetLength(0);
        var cols = elevation.GetLength(1);

        // Generate contour levels
        var levels = new List<float>();
        for (var level = minElevation; level <= maxElevation; level += interval)
            if (level > minElevation && level < maxElevation)
                levels.Add(level);

        foreach (var level in levels)
        {
            // Marching squares algorithm for each contour level
            var contourSegments = ExtractContourLevel(elevation, level, origin, cellSize);
            contours.AddRange(contourSegments);
        }

        return contours;
    }

    private static List<List<Vector2>> ExtractContourLevel(float[,] elevation, float level, Vector2 origin,
        float cellSize)
    {
        var contours = new List<List<Vector2>>();
        var rows = elevation.GetLength(0);
        var cols = elevation.GetLength(1);

        // Simple contour extraction (can be improved with proper marching squares)
        for (var row = 0; row < rows - 1; row++)
        for (var col = 0; col < cols - 1; col++)
        {
            var segment = GetContourSegment(
                elevation[row, col], elevation[row, col + 1],
                elevation[row + 1, col], elevation[row + 1, col + 1],
                level, row, col, origin, cellSize);

            if (segment != null && segment.Count >= 2)
                contours.Add(segment);
        }

        return contours;
    }

    private static List<Vector2> GetContourSegment(float tl, float tr, float bl, float br, float level, int row,
        int col, Vector2 origin, float cellSize)
    {
        var segment = new List<Vector2>();

        // Determine cell configuration
        var config = 0;
        if (tl > level) config |= 1;
        if (tr > level) config |= 2;
        if (br > level) config |= 4;
        if (bl > level) config |= 8;

        // Get intersection points based on configuration
        switch (config)
        {
            case 1:
            case 14: // Top-left
                segment.Add(InterpolatePoint(tl, bl, level, col, row, col, row + 1, origin, cellSize));
                segment.Add(InterpolatePoint(tl, tr, level, col, row, col + 1, row, origin, cellSize));
                break;
            case 2:
            case 13: // Top-right
                segment.Add(InterpolatePoint(tl, tr, level, col, row, col + 1, row, origin, cellSize));
                segment.Add(InterpolatePoint(tr, br, level, col + 1, row, col + 1, row + 1, origin, cellSize));
                break;
            case 4:
            case 11: // Bottom-right
                segment.Add(InterpolatePoint(tr, br, level, col + 1, row, col + 1, row + 1, origin, cellSize));
                segment.Add(InterpolatePoint(bl, br, level, col, row + 1, col + 1, row + 1, origin, cellSize));
                break;
            case 7:
            case 8: // Bottom-left
                segment.Add(InterpolatePoint(bl, br, level, col, row + 1, col + 1, row + 1, origin, cellSize));
                segment.Add(InterpolatePoint(tl, bl, level, col, row, col, row + 1, origin, cellSize));
                break;
        }

        return segment;
    }

    private static Vector2 InterpolatePoint(float val1, float val2, float level, int x1, int y1, int x2, int y2,
        Vector2 origin, float cellSize)
    {
        var t = (level - val1) / (val2 - val1);
        var x = x1 + t * (x2 - x1);
        var y = y1 + t * (y2 - y1);

        return new Vector2(origin.X + x * cellSize, origin.Y + y * cellSize);
    }

    #endregion

    #region Vector Operations Implementations

    /// <summary>
    ///     Create buffer around a geometry using NetTopologySuite
    /// </summary>
    public static Geometry BufferGeometry(Geometry geometry, double distance)
    {
        if (geometry == null)
            return null;

        var bufferParams = new BufferParameters
        {
            EndCapStyle = EndCapStyle.Round,
            JoinStyle = JoinStyle.Round,
            QuadrantSegments = 8
        };

        return geometry.Buffer(distance, bufferParams);
    }

    /// <summary>
    ///     Clip geometry to boundary
    /// </summary>
    public static Geometry ClipGeometry(Geometry geometry, Geometry clipBoundary)
    {
        if (geometry == null || clipBoundary == null)
            return null;

        return geometry.Intersection(clipBoundary);
    }

    /// <summary>
    ///     Dissolve geometries based on attribute grouping
    /// </summary>
    public static Dictionary<object, Geometry> DissolveGeometries(List<(Geometry geometry, object attribute)> features)
    {
        var dissolved = new Dictionary<object, List<Geometry>>();

        // Group by attribute
        foreach (var (geometry, attribute) in features)
        {
            if (!dissolved.ContainsKey(attribute))
                dissolved[attribute] = new List<Geometry>();

            dissolved[attribute].Add(geometry);
        }

        // Union geometries in each group
        var result = new Dictionary<object, Geometry>();
        foreach (var kvp in dissolved)
            if (kvp.Value.Count == 1)
            {
                result[kvp.Key] = kvp.Value[0];
            }
            else
            {
                var collection = _geometryFactory.CreateGeometryCollection(kvp.Value.ToArray());
                result[kvp.Key] = collection.Union();
            }

        return result;
    }

    /// <summary>
    ///     Simplify geometry using Douglas-Peucker algorithm
    /// </summary>
    public static Geometry SimplifyGeometry(Geometry geometry, double tolerance)
    {
        if (geometry == null)
            return null;

        return DouglasPeuckerSimplifier.Simplify(geometry, tolerance);
    }

    /// <summary>
    ///     Calculate intersection of two geometries
    /// </summary>
    public static Geometry IntersectGeometries(Geometry geom1, Geometry geom2)
    {
        if (geom1 == null || geom2 == null)
            return null;

        return geom1.Intersection(geom2);
    }

    /// <summary>
    ///     Calculate union of geometries
    /// </summary>
    public static Geometry UnionGeometries(List<Geometry> geometries)
    {
        if (geometries == null || geometries.Count == 0)
            return null;

        if (geometries.Count == 1)
            return geometries[0];

        var collection = _geometryFactory.CreateGeometryCollection(geometries.ToArray());
        return collection.Union();
    }

    #endregion

    #region Remote Sensing Implementations

    /// <summary>
    ///     Calculate NDVI from NIR and Red bands
    /// </summary>
    public static float[,] CalculateNDVI(float[,] nirBand, float[,] redBand)
    {
        var rows = nirBand.GetLength(0);
        var cols = nirBand.GetLength(1);
        var ndvi = new float[rows, cols];

        for (var row = 0; row < rows; row++)
        for (var col = 0; col < cols; col++)
        {
            var nir = nirBand[row, col];
            var red = redBand[row, col];

            // NDVI = (NIR - Red) / (NIR + Red)
            var sum = nir + red;
            if (Math.Abs(sum) > 0.0001f)
                ndvi[row, col] = (nir - red) / sum;
            else
                ndvi[row, col] = 0f;
        }

        return ndvi;
    }

    /// <summary>
    ///     Calculate EVI (Enhanced Vegetation Index)
    /// </summary>
    public static float[,] CalculateEVI(float[,] nirBand, float[,] redBand, float[,] blueBand, float L = 1f,
        float C1 = 6f, float C2 = 7.5f, float G = 2.5f)
    {
        var rows = nirBand.GetLength(0);
        var cols = nirBand.GetLength(1);
        var evi = new float[rows, cols];

        for (var row = 0; row < rows; row++)
        for (var col = 0; col < cols; col++)
        {
            var nir = nirBand[row, col];
            var red = redBand[row, col];
            var blue = blueBand[row, col];

            // EVI = G * ((NIR - Red) / (NIR + C1 * Red - C2 * Blue + L))
            var denominator = nir + C1 * red - C2 * blue + L;
            if (Math.Abs(denominator) > 0.0001f)
                evi[row, col] = G * ((nir - red) / denominator);
            else
                evi[row, col] = 0f;
        }

        return evi;
    }

    /// <summary>
    ///     Simple k-means classification for multispectral data
    /// </summary>
    public static byte[,] ClassifyKMeans(float[][,] bands, int numClasses, int maxIterations = 100)
    {
        var rows = bands[0].GetLength(0);
        var cols = bands[0].GetLength(1);
        var numBands = bands.Length;

        var classified = new byte[rows, cols];
        var centroids = InitializeCentroids(bands, numClasses);

        for (var iter = 0; iter < maxIterations; iter++)
        {
            // Assign pixels to nearest centroid
            for (var row = 0; row < rows; row++)
            for (var col = 0; col < cols; col++)
            {
                var pixel = new float[numBands];
                for (var b = 0; b < numBands; b++)
                    pixel[b] = bands[b][row, col];

                classified[row, col] = (byte)FindNearestCentroid(pixel, centroids);
            }

            // Update centroids
            UpdateCentroids(bands, classified, centroids);
        }

        return classified;
    }

    private static float[][] InitializeCentroids(float[][,] bands, int numClasses)
    {
        var numBands = bands.Length;
        var centroids = new float[numClasses][];
        var random = new Random();

        var rows = bands[0].GetLength(0);
        var cols = bands[0].GetLength(1);

        for (var i = 0; i < numClasses; i++)
        {
            centroids[i] = new float[numBands];
            var row = random.Next(rows);
            var col = random.Next(cols);

            for (var b = 0; b < numBands; b++)
                centroids[i][b] = bands[b][row, col];
        }

        return centroids;
    }

    private static int FindNearestCentroid(float[] pixel, float[][] centroids)
    {
        var nearest = 0;
        var minDist = float.MaxValue;

        for (var i = 0; i < centroids.Length; i++)
        {
            float dist = 0;
            for (var b = 0; b < pixel.Length; b++)
            {
                var diff = pixel[b] - centroids[i][b];
                dist += diff * diff;
            }

            if (dist < minDist)
            {
                minDist = dist;
                nearest = i;
            }
        }

        return nearest;
    }

    private static void UpdateCentroids(float[][,] bands, byte[,] classified, float[][] centroids)
    {
        var rows = bands[0].GetLength(0);
        var cols = bands[0].GetLength(1);
        var numBands = bands.Length;
        var numClasses = centroids.Length;

        var sums = new float[numClasses][];
        var counts = new int[numClasses];

        for (var i = 0; i < numClasses; i++)
            sums[i] = new float[numBands];

        // Accumulate sums
        for (var row = 0; row < rows; row++)
        for (var col = 0; col < cols; col++)
        {
            int classId = classified[row, col];
            counts[classId]++;

            for (var b = 0; b < numBands; b++)
                sums[classId][b] += bands[b][row, col];
        }

        // Calculate means
        for (var i = 0; i < numClasses; i++)
            if (counts[i] > 0)
                for (var b = 0; b < numBands; b++)
                    centroids[i][b] = sums[i][b] / counts[i];
    }

    #endregion

    #region Hydrological Operations

    /// <summary>
    ///     Calculate D8 flow direction (steepest descent)
    /// </summary>
    public static byte[,] CalculateD8FlowDirection(float[,] elevation)
    {
        var rows = elevation.GetLength(0);
        var cols = elevation.GetLength(1);
        var flowDir = new byte[rows, cols];

        // D8 directions: E, SE, S, SW, W, NW, N, NE
        int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
        int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };
        byte[] dirCodes = { 1, 2, 4, 8, 16, 32, 64, 128 };

        for (var row = 1; row < rows - 1; row++)
        for (var col = 1; row < cols - 1; col++)
        {
            float maxSlope = 0;
            byte direction = 0;

            for (var i = 0; i < 8; i++)
            {
                var newRow = row + dy[i];
                var newCol = col + dx[i];

                if (newRow >= 0 && newRow < rows && newCol >= 0 && newCol < cols)
                {
                    var drop = elevation[row, col] - elevation[newRow, newCol];
                    var distance = i % 2 == 0 ? 1f : 1.414f; // Diagonal = sqrt(2)
                    var slope = drop / distance;

                    if (slope > maxSlope)
                    {
                        maxSlope = slope;
                        direction = dirCodes[i];
                    }
                }
            }

            flowDir[row, col] = direction;
        }

        return flowDir;
    }

    /// <summary>
    ///     Calculate flow accumulation from flow direction
    /// </summary>
    public static int[,] CalculateFlowAccumulation(byte[,] flowDirection)
    {
        var rows = flowDirection.GetLength(0);
        var cols = flowDirection.GetLength(1);
        var flowAcc = new int[rows, cols];

        // Initialize all cells with 1 (including themselves)
        for (var row = 0; row < rows; row++)
        for (var col = 0; col < cols; col++)
            flowAcc[row, col] = 1;

        // Process cells from highest to lowest elevation (simplified approach)
        // In production, use a proper topological sort
        for (var pass = 0; pass < rows * cols; pass++)
        for (var row = 1; row < rows - 1; row++)
        for (var col = 1; col < cols - 1; col++)
        {
            var dir = flowDirection[row, col];
            if (dir > 0)
            {
                var (targetRow, targetCol) = GetTargetCell(row, col, dir);
                if (targetRow >= 0 && targetRow < rows && targetCol >= 0 && targetCol < cols)
                    flowAcc[targetRow, targetCol] += flowAcc[row, col];
            }
        }

        return flowAcc;
    }

    private static (int row, int col) GetTargetCell(int row, int col, byte direction)
    {
        return direction switch
        {
            1 => (row, col + 1), // E
            2 => (row + 1, col + 1), // SE
            4 => (row + 1, col), // S
            8 => (row + 1, col - 1), // SW
            16 => (row, col - 1), // W
            32 => (row - 1, col - 1), // NW
            64 => (row - 1, col), // N
            128 => (row - 1, col + 1), // NE
            _ => (row, col)
        };
    }

    #endregion

    #region Topology and Validation

    /// <summary>
    ///     Check if two line segments intersect
    /// </summary>
    public static bool DoSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        var d1 = Direction(p3, p4, p1);
        var d2 = Direction(p3, p4, p2);
        var d3 = Direction(p1, p2, p3);
        var d4 = Direction(p1, p2, p4);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
            ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

        // Check for collinear cases
        if (Math.Abs(d1) < 0.0001f && OnSegment(p3, p1, p4)) return true;
        if (Math.Abs(d2) < 0.0001f && OnSegment(p3, p2, p4)) return true;
        if (Math.Abs(d3) < 0.0001f && OnSegment(p1, p3, p2)) return true;
        if (Math.Abs(d4) < 0.0001f && OnSegment(p1, p4, p2)) return true;

        return false;
    }

    private static float Direction(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p3.X - p1.X) * (p2.Y - p1.Y) - (p2.X - p1.X) * (p3.Y - p1.Y);
    }

    private static bool OnSegment(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return p2.X >= Math.Min(p1.X, p3.X) && p2.X <= Math.Max(p1.X, p3.X) &&
               p2.Y >= Math.Min(p1.Y, p3.Y) && p2.Y <= Math.Max(p1.Y, p3.Y);
    }

    /// <summary>
    ///     Check if polygon is self-intersecting
    /// </summary>
    public static bool IsSelfIntersecting(List<Vector2> polygon)
    {
        var n = polygon.Count;
        if (n < 4) return false;

        for (var i = 0; i < n; i++)
        for (var j = i + 2; j < n; j++)
        {
            // Skip adjacent segments
            if (i == 0 && j == n - 1) continue;

            var p1 = polygon[i];
            var p2 = polygon[(i + 1) % n];
            var p3 = polygon[j];
            var p4 = polygon[(j + 1) % n];

            if (DoSegmentsIntersect(p1, p2, p3, p4))
                return true;
        }

        return false;
    }

    #endregion
}