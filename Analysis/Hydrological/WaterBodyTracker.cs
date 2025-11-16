// GeoscientistToolkit/Analysis/Hydrological/WaterBodyTracker.cs
//
// Tracks water bodies (lakes, rivers, sea) over time during rainfall simulations
//

using System.Numerics;
using GeoscientistToolkit.Data.GIS;

namespace GeoscientistToolkit.Analysis.Hydrological;

/// <summary>
/// Represents a tracked water body (lake, river, or sea)
/// </summary>
public class WaterBody
{
    public int Id { get; set; }
    public WaterBodyType Type { get; set; }
    public List<(int row, int col)> Cells { get; set; } = new();
    public float AverageDepth { get; set; }
    public float MaxDepth { get; set; }
    public float Volume { get; set; }
    public float SurfaceArea { get; set; }
    public Vector2 Centroid { get; set; }
    public BoundingBox Bounds { get; set; }

    // Temporal tracking
    public List<float> VolumeHistory { get; set; } = new();
    public List<float> DepthHistory { get; set; } = new();
}

public enum WaterBodyType
{
    Lake,
    River,
    Sea,
    Pond,
    Stream
}

/// <summary>
/// Tracks and analyzes water bodies over time during simulations
/// </summary>
public class WaterBodyTracker
{
    private readonly float[,] _elevation;
    private readonly int _rows;
    private readonly int _cols;
    private readonly float _cellArea; // Area of each cell in square meters

    private List<WaterBody> _waterBodies = new();
    private int _nextId = 1;

    // Thresholds for classification
    private const float MinLakeDepth = 0.5f; // meters
    private const float MinLakeArea = 100; // cells
    private const float MinRiverDepth = 0.2f;
    private const float RiverAspectRatio = 5.0f; // Length/width ratio

    public WaterBodyTracker(float[,] elevation, float cellWidthMeters = 30f)
    {
        _elevation = elevation;
        _rows = elevation.GetLength(0);
        _cols = elevation.GetLength(1);
        _cellArea = cellWidthMeters * cellWidthMeters;
    }

    public List<WaterBody> WaterBodies => _waterBodies;

    /// <summary>
    /// Update water bodies based on current water depth
    /// </summary>
    public void Update(float[,] waterDepth, int[,] flowAccumulation, int timeStep)
    {
        // Detect water bodies using flood-fill algorithm
        var visited = new bool[_rows, _cols];
        var newWaterBodies = new List<WaterBody>();

        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                if (waterDepth[r, c] >= MinRiverDepth && !visited[r, c])
                {
                    var waterBody = FloodFill(waterDepth, flowAccumulation, visited, r, c);
                    if (waterBody.Cells.Count > 0)
                    {
                        ClassifyWaterBody(waterBody, flowAccumulation);
                        CalculateProperties(waterBody, waterDepth);

                        // Try to match with existing water body
                        var existing = FindMatchingWaterBody(waterBody);
                        if (existing != null)
                        {
                            existing.Cells = waterBody.Cells;
                            existing.AverageDepth = waterBody.AverageDepth;
                            existing.MaxDepth = waterBody.MaxDepth;
                            existing.Volume = waterBody.Volume;
                            existing.SurfaceArea = waterBody.SurfaceArea;
                            existing.VolumeHistory.Add(waterBody.Volume);
                            existing.DepthHistory.Add(waterBody.AverageDepth);
                            newWaterBodies.Add(existing);
                        }
                        else
                        {
                            waterBody.Id = _nextId++;
                            waterBody.VolumeHistory.Add(waterBody.Volume);
                            waterBody.DepthHistory.Add(waterBody.AverageDepth);
                            newWaterBodies.Add(waterBody);
                        }
                    }
                }
            }
        }

        _waterBodies = newWaterBodies;
    }

    private WaterBody FloodFill(float[,] waterDepth, int[,] flowAccumulation, bool[,] visited, int startRow, int startCol)
    {
        var waterBody = new WaterBody();
        var queue = new Queue<(int r, int c)>();
        queue.Enqueue((startRow, startCol));
        visited[startRow, startCol] = true;

        int minRow = startRow, maxRow = startRow;
        int minCol = startCol, maxCol = startCol;

        while (queue.Count > 0)
        {
            var (r, c) = queue.Dequeue();
            waterBody.Cells.Add((r, c));

            minRow = Math.Min(minRow, r);
            maxRow = Math.Max(maxRow, r);
            minCol = Math.Min(minCol, c);
            maxCol = Math.Max(maxCol, c);

            // Check 8 neighbors
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;

                    int nr = r + dr;
                    int nc = c + dc;

                    if (nr >= 0 && nr < _rows && nc >= 0 && nc < _cols &&
                        !visited[nr, nc] && waterDepth[nr, nc] >= MinRiverDepth)
                    {
                        visited[nr, nc] = true;
                        queue.Enqueue((nr, nc));
                    }
                }
            }
        }

        // Calculate bounding box (will convert to world coords later if needed)
        waterBody.Bounds = new BoundingBox
        {
            Min = new Vector2(minCol, minRow),
            Max = new Vector2(maxCol, maxRow)
        };

        return waterBody;
    }

    private void ClassifyWaterBody(WaterBody waterBody, int[,] flowAccumulation)
    {
        if (waterBody.Cells.Count < 10)
        {
            waterBody.Type = WaterBodyType.Pond;
            return;
        }

        // Check if it's along an edge (likely sea/ocean)
        bool touchesEdge = waterBody.Cells.Any(cell =>
            cell.row == 0 || cell.row == _rows - 1 ||
            cell.col == 0 || cell.col == _cols - 1);

        if (touchesEdge)
        {
            waterBody.Type = WaterBodyType.Sea;
            return;
        }

        // Calculate aspect ratio
        float width = waterBody.Bounds.Max.X - waterBody.Bounds.Min.X + 1;
        float height = waterBody.Bounds.Max.Y - waterBody.Bounds.Min.Y + 1;
        float aspectRatio = Math.Max(width, height) / Math.Min(width, height);

        // Check average flow accumulation
        int totalFlowAcc = 0;
        foreach (var (row, col) in waterBody.Cells)
        {
            totalFlowAcc += flowAccumulation[row, col];
        }
        float avgFlowAcc = (float)totalFlowAcc / waterBody.Cells.Count;

        // Classify based on properties
        if (aspectRatio >= RiverAspectRatio && avgFlowAcc > 500)
        {
            waterBody.Type = waterBody.Cells.Count > 1000 ? WaterBodyType.River : WaterBodyType.Stream;
        }
        else
        {
            waterBody.Type = waterBody.Cells.Count >= MinLakeArea ? WaterBodyType.Lake : WaterBodyType.Pond;
        }
    }

    private void CalculateProperties(WaterBody waterBody, float[,] waterDepth)
    {
        float totalDepth = 0f;
        float maxDepth = 0f;
        float centroidX = 0f;
        float centroidY = 0f;

        foreach (var (row, col) in waterBody.Cells)
        {
            float depth = waterDepth[row, col];
            totalDepth += depth;
            maxDepth = Math.Max(maxDepth, depth);
            centroidX += col;
            centroidY += row;
        }

        waterBody.AverageDepth = totalDepth / waterBody.Cells.Count;
        waterBody.MaxDepth = maxDepth;
        waterBody.Volume = totalDepth * _cellArea; // cubic meters
        waterBody.SurfaceArea = waterBody.Cells.Count * _cellArea; // square meters
        waterBody.Centroid = new Vector2(centroidX / waterBody.Cells.Count, centroidY / waterBody.Cells.Count);
    }

    private WaterBody FindMatchingWaterBody(WaterBody newBody)
    {
        // Find existing water body whose centroid is close
        foreach (var existing in _waterBodies)
        {
            float distance = Vector2.Distance(existing.Centroid, newBody.Centroid);
            if (distance < 10) // Within 10 cells
            {
                return existing;
            }
        }
        return null;
    }

    /// <summary>
    /// Get statistics summary
    /// </summary>
    public string GetSummary()
    {
        int lakes = _waterBodies.Count(w => w.Type == WaterBodyType.Lake);
        int rivers = _waterBodies.Count(w => w.Type == WaterBodyType.River);
        int ponds = _waterBodies.Count(w => w.Type == WaterBodyType.Pond);
        int seas = _waterBodies.Count(w => w.Type == WaterBodyType.Sea);

        float totalVolume = _waterBodies.Sum(w => w.Volume);

        return $"Water Bodies: {lakes} lakes, {rivers} rivers, {ponds} ponds, {seas} seas | Total Volume: {totalVolume:F0} m³";
    }

    /// <summary>
    /// Get largest water body by volume
    /// </summary>
    public WaterBody GetLargest()
    {
        return _waterBodies.OrderByDescending(w => w.Volume).FirstOrDefault();
    }

    /// <summary>
    /// Export water bodies to GIS layers
    /// </summary>
    public List<GISLayer> ExportToGISLayers(BoundingBox worldBounds)
    {
        var layers = new List<GISLayer>();

        foreach (var waterBody in _waterBodies)
        {
            var layer = new GISLayer
            {
                Name = $"{waterBody.Type} #{waterBody.Id}",
                Type = LayerType.Vector,
                IsVisible = true,
                Color = GetColorForType(waterBody.Type)
            };

            // Convert cells to polygon
            var polygon = CellsToPolygon(waterBody.Cells, worldBounds);
            if (polygon.Count > 0)
            {
                layer.Features.Add(new GISFeature
                {
                    Type = FeatureType.Polygon,
                    Coordinates = polygon,
                    Properties = new Dictionary<string, object>
                    {
                        ["Type"] = waterBody.Type.ToString(),
                        ["Volume_m3"] = waterBody.Volume,
                        ["Area_m2"] = waterBody.SurfaceArea,
                        ["AvgDepth_m"] = waterBody.AverageDepth,
                        ["MaxDepth_m"] = waterBody.MaxDepth
                    }
                });
            }

            layers.Add(layer);
        }

        return layers;
    }

    private Vector4 GetColorForType(WaterBodyType type)
    {
        return type switch
        {
            WaterBodyType.Lake => new Vector4(0.2f, 0.5f, 0.8f, 0.7f),
            WaterBodyType.River => new Vector4(0.3f, 0.6f, 0.9f, 0.8f),
            WaterBodyType.Sea => new Vector4(0.1f, 0.3f, 0.6f, 0.6f),
            WaterBodyType.Pond => new Vector4(0.4f, 0.7f, 1.0f, 0.6f),
            WaterBodyType.Stream => new Vector4(0.5f, 0.8f, 1.0f, 0.7f),
            _ => new Vector4(0.5f, 0.5f, 1.0f, 0.7f)
        };
    }

    private List<Vector2> CellsToPolygon(List<(int row, int col)> cells, BoundingBox worldBounds)
    {
        if (cells.Count == 0) return new List<Vector2>();

        // For simplicity, create a bounding box polygon
        // In a full implementation, would use marching squares or similar
        int minRow = cells.Min(c => c.row);
        int maxRow = cells.Max(c => c.row);
        int minCol = cells.Min(c => c.col);
        int maxCol = cells.Max(c => c.col);

        // Convert to world coordinates
        float cellWidth = (worldBounds.Max.X - worldBounds.Min.X) / _cols;
        float cellHeight = (worldBounds.Max.Y - worldBounds.Min.Y) / _rows;

        return new List<Vector2>
        {
            new Vector2(worldBounds.Min.X + minCol * cellWidth, worldBounds.Min.Y + minRow * cellHeight),
            new Vector2(worldBounds.Min.X + maxCol * cellWidth, worldBounds.Min.Y + minRow * cellHeight),
            new Vector2(worldBounds.Min.X + maxCol * cellWidth, worldBounds.Min.Y + maxRow * cellHeight),
            new Vector2(worldBounds.Min.X + minCol * cellWidth, worldBounds.Min.Y + maxRow * cellHeight),
            new Vector2(worldBounds.Min.X + minCol * cellWidth, worldBounds.Min.Y + minRow * cellHeight)
        };
    }
}
