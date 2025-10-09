// GeoscientistToolkit/Business/GIS/GISAnalysisStats.cs

using System.Numerics;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
///     Statistical analysis and reporting for GIS datasets
/// </summary>
public static class GISAnalysisStats
{
    #region Attribute Statistics

    /// <summary>
    ///     Analyze attribute values across all features
    /// </summary>
    public static AttributeStatistics AnalyzeAttribute(GISDataset dataset, string attributeName)
    {
        var stats = new AttributeStatistics
        {
            AttributeName = attributeName,
            DatasetName = dataset.Name
        };

        var allValues = dataset.Layers
            .SelectMany(l => l.Features)
            .Where(f => f.Properties.ContainsKey(attributeName))
            .Select(f => f.Properties[attributeName])
            .Where(v => v != null)
            .ToList();

        stats.TotalCount = allValues.Count;
        stats.NullCount = dataset.Layers
            .SelectMany(l => l.Features)
            .Count(f => !f.Properties.ContainsKey(attributeName) || f.Properties[attributeName] == null);

        if (allValues.Count == 0)
        {
            stats.DataType = "Unknown";
            return stats;
        }

        // Determine data type
        var firstValue = allValues[0];
        if (firstValue is int or long or short)
            stats.DataType = "Integer";
        else if (firstValue is float or double or decimal)
            stats.DataType = "Numeric";
        else if (firstValue is bool)
            stats.DataType = "Boolean";
        else if (firstValue is DateTime)
            stats.DataType = "DateTime";
        else
            stats.DataType = "Text";

        // Unique values
        stats.UniqueValues = allValues.Distinct().Count();

        // Type-specific statistics
        if (stats.DataType == "Integer" || stats.DataType == "Numeric")
        {
            var numericValues = allValues.Select(v => Convert.ToDouble(v)).ToList();

            stats.Min = numericValues.Min();
            stats.Max = numericValues.Max();
            stats.Mean = numericValues.Average();
            stats.Sum = numericValues.Sum();

            // Standard deviation
            var variance = numericValues.Select(v => Math.Pow(v - stats.Mean.Value, 2)).Average();
            stats.StdDev = Math.Sqrt(variance);

            // Median
            var sorted = numericValues.OrderBy(v => v).ToList();
            stats.Median = sorted.Count % 2 == 0
                ? (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2
                : sorted[sorted.Count / 2];
        }

        // Frequency distribution for categorical or low-cardinality data
        if (stats.UniqueValues <= 100)
            stats.ValueFrequency = allValues
                .GroupBy(v => v.ToString())
                .OrderByDescending(g => g.Count())
                .Take(20)
                .ToDictionary(g => g.Key, g => g.Count());

        return stats;
    }

    #endregion

    #region Dataset Summary Statistics

    /// <summary>
    ///     Generate comprehensive statistics for a GIS dataset
    /// </summary>
    public static DatasetStatistics CalculateStatistics(GISDataset dataset)
    {
        var stats = new DatasetStatistics
        {
            DatasetName = dataset.Name,
            FilePath = dataset.FilePath,
            Tags = dataset.Tags.GetFlags().Select(t => t.GetDisplayName()).ToList(),
            CalculationDate = DateTime.Now
        };

        // Basic counts
        stats.LayerCount = dataset.Layers.Count;
        stats.TotalFeatures = dataset.Layers.Sum(l => l.Features.Count);
        stats.VectorLayers = dataset.Layers.Count(l => l.Type == LayerType.Vector);
        stats.RasterLayers = dataset.Layers.Count(l => l.Type == LayerType.Raster);

        // Feature type breakdown
        var allFeatures = dataset.Layers.SelectMany(l => l.Features).ToList();
        stats.PointCount = allFeatures.Count(f => f.Type == FeatureType.Point);
        stats.LineCount = allFeatures.Count(f => f.Type == FeatureType.Line);
        stats.PolygonCount = allFeatures.Count(f => f.Type == FeatureType.Polygon);
        stats.MultiPointCount = allFeatures.Count(f => f.Type == FeatureType.MultiPoint);
        stats.MultiLineCount = allFeatures.Count(f => f.Type == FeatureType.MultiLine);
        stats.MultiPolygonCount = allFeatures.Count(f => f.Type == FeatureType.MultiPolygon);

        // Spatial extent
        stats.BoundingBox = dataset.Bounds;
        stats.CenterPoint = dataset.Center;
        stats.Width = dataset.Bounds.Width;
        stats.Height = dataset.Bounds.Height;

        // Projection info
        stats.ProjectionName = dataset.Projection.Name;
        stats.EPSG = dataset.Projection.EPSG;
        stats.ProjectionType = dataset.Projection.Type.ToString();

        // Attribute analysis
        stats.TotalAttributes = allFeatures.Sum(f => f.Properties.Count);
        stats.UniqueAttributeNames = allFeatures
            .SelectMany(f => f.Properties.Keys)
            .Distinct()
            .Count();

        // Complexity metrics
        stats.AverageVerticesPerFeature = allFeatures.Any()
            ? allFeatures.Average(f => f.Coordinates.Count)
            : 0;

        stats.MaxVerticesInFeature = allFeatures.Any()
            ? allFeatures.Max(f => f.Coordinates.Count)
            : 0;

        // Calculate approximate data density
        if (stats.Width > 0 && stats.Height > 0)
        {
            var area = stats.Width * stats.Height;
            stats.FeatureDensity = stats.TotalFeatures / area;
        }

        // Layer-specific statistics
        stats.LayerStats = new List<LayerStatistics>();
        foreach (var layer in dataset.Layers) stats.LayerStats.Add(CalculateLayerStatistics(layer));

        return stats;
    }

    /// <summary>
    ///     Calculate statistics for a single layer
    /// </summary>
    public static LayerStatistics CalculateLayerStatistics(GISLayer layer)
    {
        var stats = new LayerStatistics
        {
            LayerName = layer.Name,
            LayerType = layer.Type.ToString(),
            FeatureCount = layer.Features.Count,
            IsVisible = layer.IsVisible,
            IsEditable = layer.IsEditable
        };

        if (layer.Type == LayerType.Vector && layer.Features.Any())
        {
            // Feature type distribution
            stats.FeatureTypes = layer.Features
                .GroupBy(f => f.Type)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            // Attribute analysis
            var allAttributes = layer.Features
                .SelectMany(f => f.Properties.Keys)
                .Distinct()
                .ToList();

            stats.AttributeCount = allAttributes.Count;
            stats.AttributeNames = allAttributes;

            // Spatial extent for this layer
            if (layer.Features.SelectMany(f => f.Coordinates).Any())
            {
                var coords = layer.Features.SelectMany(f => f.Coordinates).ToList();

                stats.MinX = coords.Min(c => c.X);
                stats.MaxX = coords.Max(c => c.X);
                stats.MinY = coords.Min(c => c.Y);
                stats.MaxY = coords.Max(c => c.Y);
                stats.Width = stats.MaxX - stats.MinX;
                stats.Height = stats.MaxY - stats.MinY;
            }

            // Complexity metrics
            stats.AverageVertices = layer.Features.Average(f => f.Coordinates.Count);
            stats.MaxVertices = layer.Features.Max(f => f.Coordinates.Count);
            stats.MinVertices = layer.Features.Min(f => f.Coordinates.Count);

            // Calculate total length for lines
            if (layer.Features.Any(f => f.Type == FeatureType.Line || f.Type == FeatureType.MultiLine))
                stats.TotalLength = CalculateTotalLength(
                    layer.Features.Where(f => f.Type == FeatureType.Line || f.Type == FeatureType.MultiLine).ToList());

            // Calculate total area for polygons
            if (layer.Features.Any(f => f.Type == FeatureType.Polygon || f.Type == FeatureType.MultiPolygon))
                stats.TotalArea = CalculateTotalArea(
                    layer.Features.Where(f => f.Type == FeatureType.Polygon || f.Type == FeatureType.MultiPolygon)
                        .ToList());
        }

        return stats;
    }

    #endregion

    #region Spatial Analysis

    /// <summary>
    ///     Calculate total length of all line features (in map units)
    /// </summary>
    public static double CalculateTotalLength(List<GISFeature> lineFeatures)
    {
        double totalLength = 0;

        foreach (var feature in lineFeatures)
        {
            if (feature.Coordinates.Count < 2)
                continue;

            for (var i = 0; i < feature.Coordinates.Count - 1; i++)
            {
                var p1 = feature.Coordinates[i];
                var p2 = feature.Coordinates[i + 1];

                // Use Haversine for geographic coordinates
                totalLength += CoordinateConverter.HaversineDistance(p1, p2);
            }
        }

        return totalLength;
    }

    /// <summary>
    ///     Calculate total area of all polygon features (in square kilometers)
    /// </summary>
    public static double CalculateTotalArea(List<GISFeature> polygonFeatures)
    {
        double totalArea = 0;

        foreach (var feature in polygonFeatures)
        {
            if (feature.Coordinates.Count < 3)
                continue;

            totalArea += CoordinateConverter.CalculatePolygonArea(feature.Coordinates);
        }

        return totalArea;
    }

    /// <summary>
    ///     Find features within a bounding box
    /// </summary>
    public static List<GISFeature> FindFeaturesInBounds(GISDataset dataset, BoundingBox bounds)
    {
        var features = new List<GISFeature>();

        foreach (var layer in dataset.Layers.Where(l => l.Type == LayerType.Vector))
        foreach (var feature in layer.Features)
        {
            // Check if any coordinate is within bounds
            var isInBounds = feature.Coordinates.Any(c =>
                c.X >= bounds.Min.X && c.X <= bounds.Max.X &&
                c.Y >= bounds.Min.Y && c.Y <= bounds.Max.Y);

            if (isInBounds)
                features.Add(feature);
        }

        return features;
    }

    /// <summary>
    ///     Find nearest feature to a point
    /// </summary>
    public static (GISFeature feature, double distance)? FindNearestFeature(
        GISDataset dataset, Vector2 point, double maxDistance = double.MaxValue)
    {
        GISFeature nearestFeature = null;
        var minDistance = double.MaxValue;

        foreach (var layer in dataset.Layers.Where(l => l.Type == LayerType.Vector))
        foreach (var feature in layer.Features)
        foreach (var coord in feature.Coordinates)
        {
            var distance = CoordinateConverter.HaversineDistance(point, coord);

            if (distance < minDistance && distance <= maxDistance)
            {
                minDistance = distance;
                nearestFeature = feature;
            }
        }

        return nearestFeature != null ? (nearestFeature, minDistance) : null;
    }

    #endregion

    #region Data Quality Assessment

    /// <summary>
    ///     Assess overall data quality
    /// </summary>
    public static QualityReport AssessDataQuality(GISDataset dataset)
    {
        var report = new QualityReport
        {
            DatasetName = dataset.Name,
            AssessmentDate = DateTime.Now
        };

        // Completeness checks
        report.Issues.Add(CheckCompleteness(dataset));

        // Geometry validity
        report.Issues.Add(CheckGeometryValidity(dataset));

        // Attribute completeness
        report.Issues.Add(CheckAttributeCompleteness(dataset));

        // Spatial accuracy (if projection info available)
        report.Issues.Add(CheckProjectionInfo(dataset));

        // Topology checks
        report.Issues.Add(CheckTopology(dataset));

        // Calculate overall score (0-100)
        var issueCount = report.Issues.Sum(i => i.Count);
        var maxPossibleIssues = dataset.Layers.Sum(l => l.Features.Count) * 5; // 5 checks per feature
        report.QualityScore = maxPossibleIssues > 0
            ? (1.0 - (double)issueCount / maxPossibleIssues) * 100
            : 100;

        report.OverallStatus = report.QualityScore >= 90 ? "Excellent" :
            report.QualityScore >= 75 ? "Good" :
            report.QualityScore >= 50 ? "Fair" : "Poor";

        return report;
    }

    private static QualityIssueGroup CheckCompleteness(GISDataset dataset)
    {
        var group = new QualityIssueGroup
        {
            Category = "Completeness",
            Description = "Checking for empty or missing data"
        };

        foreach (var layer in dataset.Layers)
        {
            if (layer.Features.Count == 0) group.Issues.Add($"Layer '{layer.Name}' has no features");

            foreach (var feature in layer.Features)
                if (feature.Coordinates.Count == 0)
                    group.Issues.Add($"Feature {feature.Id} in layer '{layer.Name}' has no coordinates");
        }

        group.Count = group.Issues.Count;
        return group;
    }

    private static QualityIssueGroup CheckGeometryValidity(GISDataset dataset)
    {
        var group = new QualityIssueGroup
        {
            Category = "Geometry Validity",
            Description = "Checking for invalid geometries"
        };

        foreach (var layer in dataset.Layers.Where(l => l.Type == LayerType.Vector))
        foreach (var feature in layer.Features)
        {
            // Check minimum vertices
            var minVertices = feature.Type switch
            {
                FeatureType.Point => 1,
                FeatureType.Line => 2,
                FeatureType.Polygon => 3,
                _ => 1
            };

            if (feature.Coordinates.Count < minVertices)
                group.Issues.Add($"Feature {feature.Id} has insufficient vertices for {feature.Type}");

            // Check for duplicate consecutive vertices
            for (var i = 1; i < feature.Coordinates.Count; i++)
                if (feature.Coordinates[i] == feature.Coordinates[i - 1])
                {
                    group.Issues.Add($"Feature {feature.Id} has duplicate consecutive vertices");
                    break;
                }

            // Check polygon closure
            if (feature.Type == FeatureType.Polygon && feature.Coordinates.Count >= 4)
            {
                var first = feature.Coordinates[0];
                var last = feature.Coordinates[^1];
                if (first != last) group.Issues.Add($"Polygon feature {feature.Id} is not closed");
            }
        }

        group.Count = group.Issues.Count;
        return group;
    }

    private static QualityIssueGroup CheckAttributeCompleteness(GISDataset dataset)
    {
        var group = new QualityIssueGroup
        {
            Category = "Attribute Completeness",
            Description = "Checking for missing or null attribute values"
        };

        // Get all attribute names
        var allAttributeNames = dataset.Layers
            .SelectMany(l => l.Features)
            .SelectMany(f => f.Properties.Keys)
            .Distinct()
            .ToList();

        foreach (var layer in dataset.Layers)
        foreach (var feature in layer.Features)
        foreach (var attrName in allAttributeNames)
            if (!feature.Properties.ContainsKey(attrName) || feature.Properties[attrName] == null)
                group.Issues.Add($"Feature {feature.Id} missing attribute '{attrName}'");

        group.Count = group.Issues.Count;
        return group;
    }

    private static QualityIssueGroup CheckProjectionInfo(GISDataset dataset)
    {
        var group = new QualityIssueGroup
        {
            Category = "Projection Information",
            Description = "Checking coordinate reference system"
        };

        if (string.IsNullOrEmpty(dataset.Projection.EPSG))
            group.Issues.Add("Dataset has no projection/CRS information");

        if (string.IsNullOrEmpty(dataset.Projection.Name)) group.Issues.Add("Projection name is not specified");

        group.Count = group.Issues.Count;
        return group;
    }

    private static QualityIssueGroup CheckTopology(GISDataset dataset)
    {
        var group = new QualityIssueGroup
        {
            Category = "Topology",
            Description = "Checking for self-intersections in polygons."
        };

        // Check for self-intersections in polygons
        foreach (var layer in dataset.Layers.Where(l => l.Type == LayerType.Vector))
        {
            var polygonFeatures = layer.Features.Where(f => f.Type == FeatureType.Polygon).ToList();

            foreach (var feature in polygonFeatures)
                // --- TODO COMPLETED ---
                // Use the robust implementation from GISOperationsImpl
                if (GISOperationsImpl.IsSelfIntersecting(feature.Coordinates))
                    group.Issues.Add($"Polygon feature {feature.Id} in layer '{layer.Name}' is self-intersecting.");
            // --- END MODIFICATION ---
        }

        group.Count = group.Issues.Count;
        return group;
    }

    #endregion
}

#region Statistics Classes

public class DatasetStatistics
{
    public string DatasetName { get; set; }
    public string FilePath { get; set; }
    public DateTime CalculationDate { get; set; }
    public List<string> Tags { get; set; }

    // Layer counts
    public int LayerCount { get; set; }
    public int VectorLayers { get; set; }
    public int RasterLayers { get; set; }

    // Feature counts
    public int TotalFeatures { get; set; }
    public int PointCount { get; set; }
    public int LineCount { get; set; }
    public int PolygonCount { get; set; }
    public int MultiPointCount { get; set; }
    public int MultiLineCount { get; set; }
    public int MultiPolygonCount { get; set; }

    // Spatial extent
    public BoundingBox BoundingBox { get; set; }
    public Vector2 CenterPoint { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    // Projection
    public string ProjectionName { get; set; }
    public string EPSG { get; set; }
    public string ProjectionType { get; set; }

    // Attributes
    public int TotalAttributes { get; set; }
    public int UniqueAttributeNames { get; set; }

    // Complexity
    public double AverageVerticesPerFeature { get; set; }
    public int MaxVerticesInFeature { get; set; }
    public double FeatureDensity { get; set; }

    // Layer-specific
    public List<LayerStatistics> LayerStats { get; set; }
}

public class LayerStatistics
{
    public string LayerName { get; set; }
    public string LayerType { get; set; }
    public int FeatureCount { get; set; }
    public bool IsVisible { get; set; }
    public bool IsEditable { get; set; }

    public Dictionary<string, int> FeatureTypes { get; set; }

    public int AttributeCount { get; set; }
    public List<string> AttributeNames { get; set; }

    public float MinX { get; set; }
    public float MaxX { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    public double AverageVertices { get; set; }
    public int MaxVertices { get; set; }
    public int MinVertices { get; set; }

    public double TotalLength { get; set; } // For line layers
    public double TotalArea { get; set; } // For polygon layers
}

public class AttributeStatistics
{
    public string AttributeName { get; set; }
    public string DatasetName { get; set; }
    public string DataType { get; set; }

    public int TotalCount { get; set; }
    public int NullCount { get; set; }
    public int UniqueValues { get; set; }

    // Numeric statistics
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Mean { get; set; }
    public double? Median { get; set; }
    public double? StdDev { get; set; }
    public double? Sum { get; set; }

    // Frequency distribution
    public Dictionary<string, int> ValueFrequency { get; set; }
}

public class QualityReport
{
    public string DatasetName { get; set; }
    public DateTime AssessmentDate { get; set; }
    public double QualityScore { get; set; } // 0-100
    public string OverallStatus { get; set; }
    public List<QualityIssueGroup> Issues { get; set; } = new();
}

public class QualityIssueGroup
{
    public string Category { get; set; }
    public string Description { get; set; }
    public int Count { get; set; }
    public List<string> Issues { get; set; } = new();
}

#endregion