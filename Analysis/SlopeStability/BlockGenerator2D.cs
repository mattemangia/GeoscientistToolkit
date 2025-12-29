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
            // Implementation of polygon extraction using "Left-Turning Walk" (simplest cycle finding for planar graphs)

            var polygons = new List<(List<Vector2>, List<int>, int)>();
            var visitedDirectedEdges = new HashSet<(Vector2 from, Vector2 to)>();

            foreach (var startNode in graph.PointToSegments.Keys)
            {
                // Get all connected segments (neighbors)
                var connectedSegments = graph.GetConnectedPoints(startNode);

                foreach (var nextNode in connectedSegments)
                {
                    if (visitedDirectedEdges.Contains((startNode, nextNode))) continue;

                    // Start a walk
                    var currentPolygon = new List<Vector2>();
                    var currentJointSets = new HashSet<int>();
                    var currentWalkEdges = new HashSet<(Vector2, Vector2)>();

                    Vector2 curr = startNode;
                    Vector2 next = nextNode;
                    Vector2 startOfWalk = startNode;
                    Vector2 secondOfWalk = nextNode; // To check full closure

                    bool closed = false;
                    int steps = 0;

                    while (steps < 100) // Safety break
                    {
                        currentPolygon.Add(curr);
                        visitedDirectedEdges.Add((curr, next));
                        currentWalkEdges.Add((curr, next));

                        // Collect joint set info from the edge
                        int edgeJsId = graph.GetSegmentId(curr, next);
                        if (edgeJsId >= 0) currentJointSets.Add(edgeJsId);

                        if (Vector2.DistanceSquared(next, startOfWalk) < 1e-5f)
                        {
                            closed = true;
                            break;
                        }

                        // Find next edge: choose the one with the smallest angle relative to current direction (sharpest left turn)
                        // Current direction: next - curr
                        // We want to turn "left" (counter-clockwise) as much as possible to trace minimal cycles

                        Vector2 currentDir = Vector2.Normalize(next - curr);
                        var candidates = graph.GetConnectedPoints(next);

                        Vector2 bestNext = Vector2.Zero;
                        float bestAngle = -1000f; // Maximize angle (closest to "left" / backwards)
                        bool found = false;

                        foreach (var candidate in candidates)
                        {
                            if (Vector2.DistanceSquared(candidate, curr) < 1e-5f) continue; // Don't go back immediately

                            Vector2 newDir = Vector2.Normalize(candidate - next);

                            // Calculate angle.
                            // Cross product (2D) tells us relative orientation
                            // Dot product tells us forward/backward
                            // Angle from currentDir to newDir should be minimal positive CCW angle

                            // Let's use atan2 relative to current direction
                            // Rotate everything so currentDir is (1,0)
                            // Angle of newDir relative to currentDir
                            float angle = MathF.Atan2(newDir.Y, newDir.X) - MathF.Atan2(currentDir.Y, currentDir.X);

                            // Normalize to [-PI, PI]
                            if (angle <= -MathF.PI) angle += 2 * MathF.PI;
                            if (angle > MathF.PI) angle -= 2 * MathF.PI;

                            // We want the smallest turn to the *left*.
                            // Left turns are positive angles (0 to PI). Right turns are negative.
                            // However, we want the "sharpest left" which corresponds to the smallest positive angle
                            // if we treat "straight back" as PI.
                            // Actually, standard "always turn left" means maximize the CCW angle.
                            // Let's normalize angle to [0, 2PI) relative to "backwards" vector?
                            // No, simpler:
                            // We want the edge that is "most left" relative to forward.
                            // That means maximizing the signed angle in [0, 2PI) range?

                            // Let's normalize to [0, 2*PI) where 0 is straight ahead?
                            // No, usually best to find the edge that has the smallest *clockwise* angle from the "backward" vector
                            // OR just smallest angle diff from current vector in CCW direction.

                            // Let's use simple heuristic: Minimize turning angle to the left
                            // We normalize angle to (0, 2PI) from currentDir
                            if (angle < 0) angle += 2 * MathF.PI;

                            // We want smallest angle (sharpest left turn relative to straight ahead is PI/2??)
                            // Actually, for minimal cycles (faces), we want to turn *left* as much as possible at every vertex.
                            // That corresponds to picking the edge with the *largest* angle relative to incoming edge?
                            // Wait, if we are walking the perimeter CCW, we turn Left.
                            // So we pick the edge that is "most left".
                            // Relative to currentDir, "left" is +90 deg. "Right" is -90 deg.
                            // We want the neighbor that has the largest signed angle (closest to +PI)?
                            // No, closest to "folding back" on the left side.

                            // Let's use the property: Order neighbors by angle, pick next one in CCW order from incoming.
                            // Since we don't have sorted neighbors pre-calculated, we do it here.

                            // We want the candidate that minimizes the angle *change* in the CW direction?
                            // No, for CCW faces, we turn Left.
                            // We want the candidate that has the SMALLEST angle in the "Right-hand side" (CW) direction
                            // so that we hug the wall on the left.
                            // Actually, let's stick to standard: "rightmost turn" traverses outer boundary CW, "leftmost turn" traverses faces CCW.
                            // Let's perform "leftmost turn" (smallest angle relative to "backwards" vector in CW direction?).

                            // Simplest robust method:
                            // Normalize angle so it represents a turn from current heading [0..2PI)
                            // We want the turn that is "most left" -> largest angle < PI?
                            // Actually, best is: find neighbor such that angle(back_vector, neighbor_vector) is minimal positive?

                            // Let's use this:
                            // Angle relative to current segment vector.
                            // We want the candidate that is "most to the left".
                            // This minimizes the angle if we measure from "right" to "left"?

                            // Let's try: select candidate with smallest angle difference in (0, 2PI) range relative to *reverse* of current edge?
                            // Standard algorithm: Sort neighbors CCW. Pick next neighbor in list after the one we came from.
                            // Since we don't have list, we find the one "after" the incoming edge (from curr to next).
                            // Incoming edge direction (next->curr) is -currentDir.
                            // We want the first neighbor CCW from (next->curr).

                            // Vector pointing back: -currentDir
                            float backAngle = MathF.Atan2(-currentDir.Y, -currentDir.X);
                            float candAngle = MathF.Atan2(newDir.Y, newDir.X);

                            float diff = candAngle - backAngle;
                            if (diff <= 0) diff += 2 * MathF.PI;

                            // We want the SMALLEST diff (first ray CCW from backward ray)
                            if (!found || diff < bestAngle)
                            {
                                bestAngle = diff;
                                bestNext = candidate;
                                found = true;
                            }
                        }

                        if (!found) break; // Dead end

                        curr = next;
                        next = bestNext;
                        steps++;
                    }

                    if (closed && currentPolygon.Count > 2)
                    {
                        // Identify material (sample center point)
                        Vector2 center = Vector2.Zero;
                        foreach (var v in currentPolygon) center += v;
                        center /= currentPolygon.Count;

                        // Default material 0, could sample geological section here
                        int matId = 0;

                        polygons.Add((currentPolygon, currentJointSets.ToList(), matId));
                    }
                }
            }

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

        // Cache segments for lookup
        private Dictionary<int, LineSegment2D> _segmentsCache = new();

        public void BuildAdjacency(List<LineSegment2D> segments)
        {
            // Cache segments
            for(int i=0; i<segments.Count; i++) _segmentsCache[i] = segments[i];

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

        public List<Vector2> GetConnectedPoints(Vector2 point)
        {
            var connected = new HashSet<Vector2>();
            var rounded = Round(point);
            if (PointToSegments.TryGetValue(rounded, out var segIds))
            {
                foreach(var segId in segIds)
                {
                    // For a segment, its endpoints and any intersections on it are connected to 'point'
                    // Find immediate neighbors on this segment
                    var seg = _segmentsCache[segId];
                    var pointsOnSeg = new List<Vector2> { seg.P1, seg.P2 };
                    if (Intersections.ContainsKey(segId))
                        pointsOnSeg.AddRange(Intersections[segId].Select(x => x.point));

                    // Sort points along segment to find immediate neighbors
                    Vector2 dir = seg.P2 - seg.P1;
                    pointsOnSeg.Sort((a, b) => Vector2.Dot(a, dir).CompareTo(Vector2.Dot(b, dir)));

                    // Find 'point' in this sorted list
                    // Use fuzzy comparison
                    int idx = pointsOnSeg.FindIndex(p => Vector2.DistanceSquared(p, point) < 1e-6f);
                    if (idx >= 0)
                    {
                        if (idx > 0) connected.Add(pointsOnSeg[idx - 1]);
                        if (idx < pointsOnSeg.Count - 1) connected.Add(pointsOnSeg[idx + 1]);
                    }
                }
            }
            return connected.ToList();
        }

        public int GetSegmentId(Vector2 p1, Vector2 p2)
        {
            // Find segment that contains both points
            // This is a bit inefficient but works
            // Need intersection of segments at p1 and segments at p2
            var r1 = Round(p1);
            var r2 = Round(p2);
            if (PointToSegments.TryGetValue(r1, out var s1) && PointToSegments.TryGetValue(r2, out var s2))
            {
                foreach(var id in s1)
                {
                    if (s2.Contains(id))
                    {
                         // Check if it's the right joint set ID we need
                         return _segmentsCache[id].JointSetId;
                    }
                }
            }
            return -1;
        }

        private Vector2 Round(Vector2 v)
        {
            return new Vector2(
                MathF.Round(v.X * 1000f) / 1000f,
                MathF.Round(v.Y * 1000f) / 1000f
            );
        }

        private void AddPointSegmentMapping(Vector2 point, int segmentIndex)
        {
            // Round point to avoid floating point precision issues
            var roundedPoint = Round(point);

            if (!PointToSegments.ContainsKey(roundedPoint))
                PointToSegments[roundedPoint] = new List<int>();

            if (!PointToSegments[roundedPoint].Contains(segmentIndex))
                PointToSegments[roundedPoint].Add(segmentIndex);
        }
    }

    #endregion
}
