// GeoscientistToolkit/Analysis/SlopeStability/BlockGenerator2D.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Generates 2D blocks from geological cross-sections and joint sets.
    /// Adapted from 3D BlockGenerator for 2D slope stability analysis.
    /// </summary>
    public static class BlockGenerator2D
    {
        /// <summary>
        /// Generate 2D blocks from a geological section and joint sets.
        /// </summary>
        public static List<Block2D> GenerateBlocks(
            GeologicalSection section,
            List<JointSet> jointSets,
            BlockGeneration2DSettings settings,
            Dictionary<string, int> lithologyToMaterialMap = null)
        {
            var blocks = new List<Block2D>();
            int blockId = 0;

            Logger.Log("[BlockGenerator2D] Starting 2D block generation");
            Logger.Log($"  Section: {section.ProfileName}, Length: {section.TotalLength:F1}m");
            Logger.Log($"  Elevation range: {section.MinElevation:F1}m to {section.MaxElevation:F1}m");
            Logger.Log($"  Joint sets: {jointSets.Count}");

            // Step 1: Create domain boundary
            var domainBounds = CreateDomainBoundary(section, settings);
            Logger.Log($"  Domain bounds: {domainBounds.Count} vertices");

            // Step 2: Generate joint lines within the domain
            var jointLines = GenerateJointLines(section, jointSets, settings);
            Logger.Log($"  Generated {jointLines.Count} joint lines");

            // Step 3: Add formation boundaries as constraint lines
            var constraintLines = new List<(Vector2 p1, Vector2 p2, string type)>();

            if (settings.UseFormationBoundaries && section.FormationBoundaries != null)
            {
                foreach (var boundary in section.FormationBoundaries)
                {
                    for (int i = 0; i < boundary.Points.Count - 1; i++)
                    {
                        constraintLines.Add((boundary.Points[i], boundary.Points[i + 1], "formation"));
                    }
                }
                Logger.Log($"  Added {constraintLines.Count} formation boundary segments");
            }

            // Step 4: Compute intersections and create polygon network
            var polygons = ComputeBlockPolygons(domainBounds, jointLines, constraintLines, section, settings);
            Logger.Log($"  Computed {polygons.Count} polygons");

            // Step 5: Create Block2D objects from polygons
            foreach (var polygon in polygons)
            {
                if (polygon.vertices.Count < 3) continue;

                var block = new Block2D
                {
                    Id = blockId++,
                    Name = $"Block2D_{blockId}",
                    Vertices = polygon.vertices,
                    JointSetIds = polygon.jointSetIds,
                    MaterialId = polygon.materialId
                };

                block.CalculateProperties();

                // Filter by area
                if (settings.RemoveSmallBlocks && block.Area < settings.MinimumBlockArea)
                    continue;
                if (settings.RemoveLargeBlocks && block.Area > settings.MaximumBlockArea)
                    continue;

                // Determine if block is removable (exposed to surface)
                block.IsRemovable = IsExposedToSurface(block, section);

                blocks.Add(block);
            }

            Logger.Log($"[BlockGenerator2D] Generated {blocks.Count} blocks");
            return blocks;
        }

        /// <summary>
        /// Create the domain boundary polygon from section bounds.
        /// </summary>
        private static List<Vector2> CreateDomainBoundary(GeologicalSection section, BlockGeneration2DSettings settings)
        {
            var boundary = new List<Vector2>();

            // Bottom left
            boundary.Add(new Vector2(0, section.MinElevation));

            // Bottom right
            boundary.Add(new Vector2(section.TotalLength, section.MinElevation));

            // Top right (follow topography if enabled)
            if (settings.UseTopography && section.BoreholeColumns.Count > 0)
            {
                // Add topography points from right to left
                for (int i = section.BoreholeColumns.Count - 1; i >= 0; i--)
                {
                    var col = section.BoreholeColumns[i];
                    boundary.Add(new Vector2(col.DistanceAlongProfile, col.SurfaceElevation));
                }
            }
            else
            {
                // Simple rectangular boundary at max elevation
                boundary.Add(new Vector2(section.TotalLength, section.MaxElevation));
                boundary.Add(new Vector2(0, section.MaxElevation));
            }

            return boundary;
        }

        /// <summary>
        /// Generate joint lines from joint sets within the section domain.
        /// </summary>
        private static List<(Vector2 p1, Vector2 p2, int jointSetId)> GenerateJointLines(
            GeologicalSection section,
            List<JointSet> jointSets,
            BlockGeneration2DSettings settings)
        {
            var lines = new List<(Vector2, Vector2, int)>();
            var random = new Random();

            float minX = 0;
            float maxX = section.TotalLength;
            float minY = section.MinElevation;
            float maxY = section.MaxElevation;

            foreach (var jointSet in jointSets)
            {
                // Calculate joint orientation in 2D section
                // Dip direction determines if joint is visible in section
                // Joints perpendicular to section appear as lines with dip angle

                float dipRad = jointSet.Dip * MathF.PI / 180f;
                float spacing = jointSet.Spacing;

                // Joint normal in section plane
                // In 2D section: horizontal = 0°, vertical down = 90°
                Vector2 jointNormal = new Vector2(
                    MathF.Sin(dipRad),   // Horizontal component
                    -MathF.Cos(dipRad)   // Vertical component (negative because Y increases downward in elevation)
                );

                // Joint direction (perpendicular to normal)
                Vector2 jointDir = new Vector2(-jointNormal.Y, jointNormal.X);

                // Generate parallel joints with spacing
                // Number of joints needed to cover domain
                float domainSize = MathF.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
                int numJoints = (int)(domainSize / spacing) + 2;

                for (int i = -1; i < numJoints; i++)
                {
                    // Add random variation to spacing
                    float spacingVar = 1.0f + (float)(random.NextDouble() * 2 - 1) * settings.JointSpacingVariation;
                    float offset = i * spacing * spacingVar;

                    // Start point on one side of domain
                    Vector2 startPoint;
                    if (Math.Abs(jointNormal.X) > 0.1f)
                    {
                        // Project along X
                        startPoint = new Vector2(minX, minY) + jointNormal * offset;
                    }
                    else
                    {
                        // Project along Y
                        startPoint = new Vector2(minX, minY) + jointNormal * offset;
                    }

                    // Extend line across entire domain
                    float lineLength = domainSize * 2;
                    Vector2 p1 = startPoint - jointDir * lineLength;
                    Vector2 p2 = startPoint + jointDir * lineLength;

                    // Clip to domain bounds
                    if (ClipLineToBounds(ref p1, ref p2, minX, maxX, minY, maxY))
                    {
                        lines.Add((p1, p2, jointSet.Id));
                    }
                }
            }

            return lines;
        }

        /// <summary>
        /// Clip a line segment to rectangular bounds using Cohen-Sutherland algorithm.
        /// </summary>
        private static bool ClipLineToBounds(ref Vector2 p1, ref Vector2 p2,
            float xMin, float xMax, float yMin, float yMax)
        {
            const int INSIDE = 0; // 0000
            const int LEFT = 1;   // 0001
            const int RIGHT = 2;  // 0010
            const int BOTTOM = 4; // 0100
            const int TOP = 8;    // 1000

            int ComputeCode(Vector2 p)
            {
                int code = INSIDE;
                if (p.X < xMin) code |= LEFT;
                else if (p.X > xMax) code |= RIGHT;
                if (p.Y < yMin) code |= BOTTOM;
                else if (p.Y > yMax) code |= TOP;
                return code;
            }

            int code1 = ComputeCode(p1);
            int code2 = ComputeCode(p2);

            while (true)
            {
                if ((code1 | code2) == 0)
                {
                    // Both points inside
                    return true;
                }
                else if ((code1 & code2) != 0)
                {
                    // Both points outside same region
                    return false;
                }
                else
                {
                    // Line crosses boundary
                    int codeOut = (code1 != 0) ? code1 : code2;
                    Vector2 p;

                    if ((codeOut & TOP) != 0)
                    {
                        float x = p1.X + (p2.X - p1.X) * (yMax - p1.Y) / (p2.Y - p1.Y);
                        p = new Vector2(x, yMax);
                    }
                    else if ((codeOut & BOTTOM) != 0)
                    {
                    float x = p1.X + (p2.X - p1.X) * (yMin - p1.Y) / (p2.Y - p1.Y);
                        p = new Vector2(x, yMin);
                    }
                    else if ((codeOut & RIGHT) != 0)
                    {
                        float y = p1.Y + (p2.Y - p1.Y) * (xMax - p1.X) / (p2.X - p1.X);
                        p = new Vector2(xMax, y);
                    }
                    else // LEFT
                    {
                        float y = p1.Y + (p2.Y - p1.Y) * (xMin - p1.X) / (p2.X - p1.X);
                        p = new Vector2(xMin, y);
                    }

                    if (codeOut == code1)
                    {
                        p1 = p;
                        code1 = ComputeCode(p1);
                    }
                    else
                    {
                        p2 = p;
                        code2 = ComputeCode(p2);
                    }
                }
            }
        }

        /// <summary>
        /// Compute block polygons from domain boundary and joint/constraint lines.
        /// Uses plane-sweep algorithm to find all polygon regions.
        /// </summary>
        private static List<(List<Vector2> vertices, List<int> jointSetIds, int materialId)> ComputeBlockPolygons(
            List<Vector2> domainBounds,
            List<(Vector2 p1, Vector2 p2, int jointSetId)> jointLines,
            List<(Vector2 p1, Vector2 p2, string type)> constraintLines,
            GeologicalSection section,
            BlockGeneration2DSettings settings)
        {
            var polygons = new List<(List<Vector2>, List<int>, int)>();

            // For simplicity, we'll use a grid-based approach
            // More sophisticated implementation would use DCEL or sweep line algorithm

            // Create all line segments
            var allSegments = new List<LineSegment2D>();

            // Add domain boundary
            for (int i = 0; i < domainBounds.Count; i++)
            {
                var p1 = domainBounds[i];
                var p2 = domainBounds[(i + 1) % domainBounds.Count];
                allSegments.Add(new LineSegment2D(p1, p2, -1, "boundary"));
            }

            // Add joint lines
            foreach (var (p1, p2, jointSetId) in jointLines)
            {
                allSegments.Add(new LineSegment2D(p1, p2, jointSetId, "joint"));
            }

            // Add constraint lines
            foreach (var (p1, p2, type) in constraintLines)
            {
                allSegments.Add(new LineSegment2D(p1, p2, -2, type));
            }

            // Find all intersection points and split segments
            var graph = BuildSegmentGraph(allSegments);

            // Extract polygons from graph using region detection
            var extractedPolygons = ExtractPolygonsFromGraph(graph, section);

            return extractedPolygons;
        }

        /// <summary>
        /// Build a graph of connected line segments with intersection points.
        /// </summary>
        private static SegmentGraph BuildSegmentGraph(List<LineSegment2D> segments)
        {
            var graph = new SegmentGraph();

            // Find all intersections
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    if (LineSegmentIntersection(segments[i].P1, segments[i].P2,
                        segments[j].P1, segments[j].P2, out Vector2 intersection))
                    {
                        graph.AddIntersection(i, j, intersection);
                    }
                }
            }

            // Build adjacency from segments and intersections
            graph.BuildAdjacency(segments);

            return graph;
        }

        /// <summary>
        /// Calculate intersection point between two line segments.
        /// </summary>
        private static bool LineSegmentIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 intersection)
        {
            intersection = Vector2.Zero;

            float d = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
            if (Math.Abs(d) < 1e-6f) return false; // Parallel

            float t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / d;
            float u = -((p1.X - p2.X) * (p1.Y - p3.Y) - (p1.Y - p2.Y) * (p1.X - p3.X)) / d;

            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
            {
                intersection = new Vector2(
                    p1.X + t * (p2.X - p1.X),
                    p1.Y + t * (p2.Y - p1.Y)
                );
                return true;
            }

            return false;
        }

        /// <summary>
        /// Extract polygons from segment graph using region detection.
        /// </summary>
        private static List<(List<Vector2>, List<int>, int)> ExtractPolygonsFromGraph(
            SegmentGraph graph, GeologicalSection section)
        {
            // Simplified implementation:
            // In production, use proper DCEL or planar subdivision algorithm
            // For now, return empty - will be enhanced in next iteration

            var polygons = new List<(List<Vector2>, List<int>, int)>();

            // TODO: Implement proper polygon extraction
            // This is a placeholder

            return polygons;
        }

        /// <summary>
        /// Determine if a block is exposed to the surface.
        /// </summary>
        private static bool IsExposedToSurface(Block2D block, GeologicalSection section)
        {
            var bounds = block.GetBounds();

            // Check if any vertex is near the surface elevation
            foreach (var v in block.Vertices)
            {
                // Find nearest borehole column to get surface elevation at this distance
                float distance = v.X;
                float surfaceElev = InterpolateSurfaceElevation(distance, section);

                if (Math.Abs(v.Y - surfaceElev) < 0.5f) // Within 0.5m of surface
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Interpolate surface elevation at a given distance along the profile.
        /// </summary>
        private static float InterpolateSurfaceElevation(float distance, GeologicalSection section)
        {
            if (section.BoreholeColumns.Count == 0)
                return section.MaxElevation;

            // Find surrounding columns
            var before = section.BoreholeColumns.LastOrDefault(c => c.DistanceAlongProfile <= distance);
            var after = section.BoreholeColumns.FirstOrDefault(c => c.DistanceAlongProfile >= distance);

            if (before == null) return after?.SurfaceElevation ?? section.MaxElevation;
            if (after == null) return before.SurfaceElevation;

            // Linear interpolation
            float t = (distance - before.DistanceAlongProfile) /
                      (after.DistanceAlongProfile - before.DistanceAlongProfile);
            return before.SurfaceElevation + t * (after.SurfaceElevation - before.SurfaceElevation);
        }
    }

    #region Helper Classes

    /// <summary>
    /// 2D line segment with metadata.
    /// </summary>
    internal class LineSegment2D
    {
        public Vector2 P1 { get; set; }
        public Vector2 P2 { get; set; }
        public int JointSetId { get; set; }
        public string Type { get; set; }

        public LineSegment2D(Vector2 p1, Vector2 p2, int jointSetId, string type)
        {
            P1 = p1;
            P2 = p2;
            JointSetId = jointSetId;
            Type = type;
        }
    }

    /// <summary>
    /// Graph structure for segment intersection analysis.
    /// </summary>
    internal class SegmentGraph
    {
        public Dictionary<int, List<(int segment, Vector2 point)>> Intersections { get; set; }
        public Dictionary<Vector2, List<int>> PointToSegments { get; set; }

        public SegmentGraph()
        {
            Intersections = new Dictionary<int, List<(int, Vector2)>>();
            PointToSegments = new Dictionary<Vector2, List<int>>();
        }

        public void AddIntersection(int seg1, int seg2, Vector2 point)
        {
            if (!Intersections.ContainsKey(seg1))
                Intersections[seg1] = new List<(int, Vector2)>();
            if (!Intersections.ContainsKey(seg2))
                Intersections[seg2] = new List<(int, Vector2)>();

            Intersections[seg1].Add((seg2, point));
            Intersections[seg2].Add((seg1, point));
        }

        public void BuildAdjacency(List<LineSegment2D> segments)
        {
            // Build point-to-segment mapping
            for (int i = 0; i < segments.Count; i++)
            {
                AddPointSegmentMapping(segments[i].P1, i);
                AddPointSegmentMapping(segments[i].P2, i);

                if (Intersections.ContainsKey(i))
                {
                    foreach (var (_, point) in Intersections[i])
                    {
                        AddPointSegmentMapping(point, i);
                    }
                }
            }
        }

        private void AddPointSegmentMapping(Vector2 point, int segmentIndex)
        {
            // Round point to avoid floating point precision issues
            var roundedPoint = new Vector2(
                MathF.Round(point.X * 1000f) / 1000f,
                MathF.Round(point.Y * 1000f) / 1000f
            );

            if (!PointToSegments.ContainsKey(roundedPoint))
                PointToSegments[roundedPoint] = new List<int>();

            if (!PointToSegments[roundedPoint].Contains(segmentIndex))
                PointToSegments[roundedPoint].Add(segmentIndex);
        }
    }

    #endregion
}
