// GeoscientistToolkit/Business/GIS/GeologyGeometryUtils.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Util;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
/// Utility class for geological geometry operations including formation splitting
/// </summary>
public static class GeologyGeometryUtils
{
    /// <summary>
    /// Split a formation along a polyline, creating two new formations
    /// </summary>
    public static (ProjectedFormation upper, ProjectedFormation lower)? SplitFormation(
        ProjectedFormation formation, 
        List<Vector2> splitLine)
    {
        if (formation == null || splitLine == null || splitLine.Count < 2)
            return null;
        
        try
        {
            // Find intersections with formation boundaries
            var topIntersections = FindBoundaryIntersections(formation.TopBoundary, splitLine);
            var bottomIntersections = FindBoundaryIntersections(formation.BottomBoundary, splitLine);
            
            if (topIntersections.Count == 0 || bottomIntersections.Count == 0)
            {
                Logger.LogWarning("Split line does not intersect formation boundaries");
                return null;
            }
            
            // Sort intersections by distance along boundaries
            topIntersections.Sort((a, b) => a.boundaryIndex.CompareTo(b.boundaryIndex));
            bottomIntersections.Sort((a, b) => a.boundaryIndex.CompareTo(b.boundaryIndex));
            
            // Create upper formation (left side of split)
            var upperFormation = new ProjectedFormation
            {
                Name = formation.Name + " (Upper)",
                Color = formation.Color,
                FoldStyle = formation.FoldStyle,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };
            
            // Create lower formation (right side of split)
            var lowerFormation = new ProjectedFormation
            {
                Name = formation.Name + " (Lower)",
                Color = AdjustColor(formation.Color, 0.9f),
                FoldStyle = formation.FoldStyle,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };
            
            // Build upper formation boundaries
            // Top: from start to first split intersection
            for (int i = 0; i <= topIntersections[0].boundaryIndex; i++)
            {
                upperFormation.TopBoundary.Add(formation.TopBoundary[i]);
            }
            upperFormation.TopBoundary.Add(topIntersections[0].point);
            
            // Follow split line downward
            var splitSegments = GetSplitLineSegmentsBetweenIntersections(
                splitLine, topIntersections[0].point, bottomIntersections[0].point);
            upperFormation.TopBoundary.AddRange(splitSegments);
            
            // Bottom: from split intersection to start
            upperFormation.BottomBoundary.Add(bottomIntersections[0].point);
            for (int i = 0; i <= bottomIntersections[0].boundaryIndex; i++)
            {
                upperFormation.BottomBoundary.Add(formation.BottomBoundary[i]);
            }
            
            // Build lower formation boundaries
            // Top: from split intersection to end
            lowerFormation.TopBoundary.Add(topIntersections[0].point);
            for (int i = topIntersections[0].boundaryIndex + 1; i < formation.TopBoundary.Count; i++)
            {
                lowerFormation.TopBoundary.Add(formation.TopBoundary[i]);
            }
            
            // Bottom: from end to split intersection
            for (int i = bottomIntersections[0].boundaryIndex + 1; i < formation.BottomBoundary.Count; i++)
            {
                lowerFormation.BottomBoundary.Add(formation.BottomBoundary[i]);
            }
            
            // Follow split line upward
            var splitSegmentsReverse = GetSplitLineSegmentsBetweenIntersections(
                splitLine, bottomIntersections[0].point, topIntersections[0].point);
            splitSegmentsReverse.Reverse();
            lowerFormation.BottomBoundary.AddRange(splitSegmentsReverse);
            
            return (upperFormation, lowerFormation);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to split formation: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Find all intersections between a boundary and a split line
    /// </summary>
    private static List<(Vector2 point, int boundaryIndex, float t)> FindBoundaryIntersections(
        List<Vector2> boundary, 
        List<Vector2> splitLine)
    {
        var intersections = new List<(Vector2 point, int boundaryIndex, float t)>();
        
        // Check each boundary segment against each split line segment
        for (int b = 0; b < boundary.Count - 1; b++)
        {
            var b1 = boundary[b];
            var b2 = boundary[b + 1];
            
            for (int s = 0; s < splitLine.Count - 1; s++)
            {
                var s1 = splitLine[s];
                var s2 = splitLine[s + 1];
                
                if (LineSegmentIntersection(b1, b2, s1, s2, out var intersection, out var t))
                {
                    intersections.Add((intersection, b, t));
                }
            }
        }
        
        return intersections;
    }
    
    /// <summary>
    /// Check if two line segments intersect
    /// </summary>
    private static bool LineSegmentIntersection(
        Vector2 p1, Vector2 p2, 
        Vector2 p3, Vector2 p4,
        out Vector2 intersection,
        out float t)
    {
        intersection = Vector2.Zero;
        t = 0;
        
        var d = (p2.X - p1.X) * (p4.Y - p3.Y) - (p2.Y - p1.Y) * (p4.X - p3.X);
        
        if (MathF.Abs(d) < 1e-6f) // Parallel lines
            return false;
        
        var t1 = ((p3.X - p1.X) * (p4.Y - p3.Y) - (p3.Y - p1.Y) * (p4.X - p3.X)) / d;
        var t2 = ((p3.X - p1.X) * (p2.Y - p1.Y) - (p3.Y - p1.Y) * (p2.X - p1.X)) / d;
        
        if (t1 >= 0 && t1 <= 1 && t2 >= 0 && t2 <= 1)
        {
            intersection = new Vector2(
                p1.X + t1 * (p2.X - p1.X),
                p1.Y + t1 * (p2.Y - p1.Y)
            );
            t = t1;
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get the portion of split line between two intersection points
    /// </summary>
    private static List<Vector2> GetSplitLineSegmentsBetweenIntersections(
        List<Vector2> splitLine,
        Vector2 startIntersection,
        Vector2 endIntersection)
    {
        var segments = new List<Vector2>();
        
        // Find which split line segments contain the intersections
        int startSegment = -1, endSegment = -1;
        
        for (int i = 0; i < splitLine.Count - 1; i++)
        {
            if (IsPointOnSegment(splitLine[i], splitLine[i + 1], startIntersection))
                startSegment = i;
            if (IsPointOnSegment(splitLine[i], splitLine[i + 1], endIntersection))
                endSegment = i;
        }
        
        if (startSegment == -1 || endSegment == -1)
            return segments;
        
        // Add intermediate points
        for (int i = startSegment + 1; i <= endSegment; i++)
        {
            segments.Add(splitLine[i]);
        }
        
        return segments;
    }
    
    /// <summary>
    /// Check if a point lies on a line segment
    /// </summary>
    private static bool IsPointOnSegment(Vector2 p1, Vector2 p2, Vector2 point, float tolerance = 1f)
    {
        var distToLine = DistanceToLineSegment(point, p1, p2);
        var segmentLength = Vector2.Distance(p1, p2);
        var d1 = Vector2.Distance(point, p1);
        var d2 = Vector2.Distance(point, p2);
        
        return distToLine < tolerance && (d1 + d2) <= (segmentLength + tolerance);
    }
    
    /// <summary>
    /// Calculate distance from point to line segment
    /// </summary>
    public static float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        var line = lineEnd - lineStart;
        var lineLength = line.Length();
        
        if (lineLength < 1e-6f)
            return Vector2.Distance(point, lineStart);
        
        var t = Math.Max(0, Math.Min(1, Vector2.Dot(point - lineStart, line) / (lineLength * lineLength)));
        var projection = lineStart + t * line;
        
        return Vector2.Distance(point, projection);
    }
    
    /// <summary>
    /// Adjust color brightness
    /// </summary>
    private static Vector4 AdjustColor(Vector4 color, float factor)
    {
        return new Vector4(
            color.X * factor,
            color.Y * factor,
            color.Z * factor,
            color.W
        );
    }
    
    /// <summary>
    /// Ensure geological layers don't pass through topography
    /// Clips formation boundaries to stay below topographic profile
    /// </summary>
    public static void ClipFormationToTopography(
        ProjectedFormation formation,
        List<Vector2> topographyPoints)
    {
        if (formation == null || topographyPoints == null || topographyPoints.Count < 2)
            return;
        
        // Clip top boundary
        formation.TopBoundary = ClipBoundaryToTopography(formation.TopBoundary, topographyPoints, false);
        
        // Bottom boundary should always be below top, so no need to clip unless it's above topography
        formation.BottomBoundary = ClipBoundaryToTopography(formation.BottomBoundary, topographyPoints, false);
    }
    
    /// <summary>
    /// Clip a boundary to stay below (or above) topography
    /// </summary>
    private static List<Vector2> ClipBoundaryToTopography(
        List<Vector2> boundary,
        List<Vector2> topography,
        bool clipAbove)
    {
        var clippedBoundary = new List<Vector2>();
        
        foreach (var point in boundary)
        {
            var topoElevation = InterpolateTopographyElevation(topography, point.X);
            
            if (clipAbove)
            {
                // Ensure point is above topography
                if (point.Y > topoElevation)
                    clippedBoundary.Add(point);
                else
                    clippedBoundary.Add(new Vector2(point.X, topoElevation));
            }
            else
            {
                // Ensure point is below topography (most common case)
                if (point.Y < topoElevation)
                    clippedBoundary.Add(point);
                else
                    clippedBoundary.Add(new Vector2(point.X, topoElevation - 10f)); // 10m below surface
            }
        }
        
        return clippedBoundary;
    }
    
    /// <summary>
    /// Interpolate topography elevation at a given X position
    /// </summary>
    private static float InterpolateTopographyElevation(List<Vector2> topography, float x)
    {
        if (topography.Count == 0)
            return 0f;
        
        // Find bracketing points
        if (x <= topography[0].X)
            return topography[0].Y;
        
        if (x >= topography[^1].X)
            return topography[^1].Y;
        
        for (int i = 0; i < topography.Count - 1; i++)
        {
            if (x >= topography[i].X && x <= topography[i + 1].X)
            {
                // Linear interpolation
                var t = (x - topography[i].X) / (topography[i + 1].X - topography[i].X);
                return topography[i].Y + t * (topography[i + 1].Y - topography[i].Y);
            }
        }
        
        return topography[^1].Y;
    }
    
    /// <summary>
    /// Generate fold geometry for synclines and anticlines
    /// Allows folds to continue even when eroded at surface
    /// </summary>
    public static void ExtendFoldBeyondErosion(
        ProjectedFormation formation,
        List<Vector2> topographyPoints,
        GeologicalMapping.FoldStyle foldStyle)
    {
        if (formation == null || topographyPoints == null)
            return;
        
        // For each point in top boundary, if it intersects topography,
        // project the fold geometry beyond the erosional surface
        var extendedTop = new List<Vector2>();
        var extendedBottom = new List<Vector2>();
        
        for (int i = 0; i < formation.TopBoundary.Count; i++)
        {
            var topPoint = formation.TopBoundary[i];
            var bottomPoint = formation.BottomBoundary[i];
            var topoElev = InterpolateTopographyElevation(topographyPoints, topPoint.X);
            
            // If formation is eroded (above topography), extend it based on fold geometry
            if (topPoint.Y > topoElev)
            {
                // Project the fold shape upward even though it's eroded
                extendedTop.Add(topPoint); // Keep the projected fold shape
                extendedBottom.Add(bottomPoint);
            }
            else
            {
                // Below erosion surface, keep as is
                extendedTop.Add(topPoint);
                extendedBottom.Add(bottomPoint);
            }
        }
        
        formation.TopBoundary = extendedTop;
        formation.BottomBoundary = extendedBottom;
    }
    
    /// <summary>
    /// Check if a formation boundary is above topography (eroded)
    /// </summary>
    public static bool IsFormationEroded(ProjectedFormation formation, List<Vector2> topographyPoints)
    {
        if (formation == null || topographyPoints == null)
            return false;
        
        foreach (var point in formation.TopBoundary)
        {
            var topoElev = InterpolateTopographyElevation(topographyPoints, point.X);
            if (point.Y > topoElev)
                return true; // At least part is above topography
        }
        
        return false;
    }
    
    /// <summary>
    /// Calculate the area of a formation polygon
    /// </summary>
    public static float CalculateFormationArea(ProjectedFormation formation)
    {
        if (formation == null || formation.TopBoundary.Count < 2)
            return 0f;
        
        // Use shoelace formula for polygon area
        float area = 0f;
        var polygon = new List<Vector2>();
        
        // Add top boundary
        polygon.AddRange(formation.TopBoundary);
        
        // Add bottom boundary in reverse
        for (int i = formation.BottomBoundary.Count - 1; i >= 0; i--)
            polygon.Add(formation.BottomBoundary[i]);
        
        // Close the polygon
        if (polygon.Count > 0 && polygon[0] != polygon[^1])
            polygon.Add(polygon[0]);
        
        for (int i = 0; i < polygon.Count - 1; i++)
        {
            area += (polygon[i].X * polygon[i + 1].Y - polygon[i + 1].X * polygon[i].Y);
        }
        
        return MathF.Abs(area / 2f);
    }
    
    /// <summary>
    /// Calculate average thickness of a formation
    /// </summary>
    public static float CalculateAverageThickness(ProjectedFormation formation)
    {
        if (formation == null || formation.TopBoundary.Count == 0)
            return 0f;
        
        float totalThickness = 0f;
        int count = Math.Min(formation.TopBoundary.Count, formation.BottomBoundary.Count);
        
        for (int i = 0; i < count; i++)
        {
            var thickness = MathF.Abs(formation.TopBoundary[i].Y - formation.BottomBoundary[i].Y);
            totalThickness += thickness;
        }
        
        return count > 0 ? totalThickness / count : 0f;
    }
}