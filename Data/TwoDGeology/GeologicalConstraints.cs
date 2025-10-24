// GeoscientistToolkit/Business/GIS/GeologicalConstraints.cs

using System.Numerics;
using GeoscientistToolkit.Util;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
/// Enforces geological constraints on cross-sections.
/// CRITICAL RULE: Geological formations must NEVER overlap!
/// </summary>
public static class GeologicalConstraints
{
    /// <summary>
    /// Check if two formations overlap at any point
    /// </summary>
    public static bool DoFormationsOverlap(ProjectedFormation formation1, ProjectedFormation formation2, float tolerance = 1f)
    {
        if (formation1 == null || formation2 == null) return false;
        if (formation1.TopBoundary.Count < 2 || formation2.TopBoundary.Count < 2) return false;
        
        // Check X range overlap first
        var f1MinX = formation1.TopBoundary.Min(p => p.X);
        var f1MaxX = formation1.TopBoundary.Max(p => p.X);
        var f2MinX = formation2.TopBoundary.Min(p => p.X);
        var f2MaxX = formation2.TopBoundary.Max(p => p.X);
        
        if (f1MaxX < f2MinX || f2MaxX < f1MinX)
            return false; // No horizontal overlap
        
        // Check for vertical overlap in the overlapping X range
        float overlapMinX = Math.Max(f1MinX, f2MinX);
        float overlapMaxX = Math.Min(f1MaxX, f2MaxX);
        
        // Sample points along the overlap region
        int samples = 20;
        for (int i = 0; i <= samples; i++)
        {
            float x = overlapMinX + (overlapMaxX - overlapMinX) * i / samples;
            
            var f1Top = InterpolateY(formation1.TopBoundary, x);
            var f1Bottom = InterpolateY(formation1.BottomBoundary, x);
            var f2Top = InterpolateY(formation2.TopBoundary, x);
            var f2Bottom = InterpolateY(formation2.BottomBoundary, x);
            
            // Check if there's vertical overlap
            bool f1InF2 = (f1Top <= f2Top + tolerance && f1Top >= f2Bottom - tolerance) ||
                         (f1Bottom <= f2Top + tolerance && f1Bottom >= f2Bottom - tolerance);
            bool f2InF1 = (f2Top <= f1Top + tolerance && f2Top >= f1Bottom - tolerance) ||
                         (f2Bottom <= f1Top + tolerance && f2Bottom >= f1Bottom - tolerance);
            
            if (f1InF2 || f2InF1)
                return true; // Overlap detected
        }
        
        return false;
    }
    
    /// <summary>
    /// Check if a formation overlaps with any other formation in the list
    /// </summary>
    public static bool DoesFormationOverlapAny(ProjectedFormation formation, List<ProjectedFormation> otherFormations, float tolerance = 1f)
    {
        foreach (var other in otherFormations)
        {
            if (other == formation) continue;
            if (DoFormationsOverlap(formation, other, tolerance))
                return true;
        }
        return false;
    }
    
    /// <summary>
    /// Validate all formations in a cross-section for overlaps
    /// </summary>
    public static List<(ProjectedFormation f1, ProjectedFormation f2)> FindAllOverlaps(List<ProjectedFormation> formations, float tolerance = 1f)
    {
        var overlaps = new List<(ProjectedFormation, ProjectedFormation)>();
        
        for (int i = 0; i < formations.Count; i++)
        {
            for (int j = i + 1; j < formations.Count; j++)
            {
                if (DoFormationsOverlap(formations[i], formations[j], tolerance))
                {
                    overlaps.Add((formations[i], formations[j]));
                }
            }
        }
        
        return overlaps;
    }
    
    /// <summary>
    /// Adjust a formation to avoid overlapping with another
    /// Moves the formation vertically to the nearest non-overlapping position
    /// </summary>
    public static void ResolveOverlap(ProjectedFormation formationToMove, ProjectedFormation fixedFormation, bool moveUp = true)
    {
        if (!DoFormationsOverlap(formationToMove, fixedFormation))
            return; // No overlap, nothing to do
        
        // Find the overlap region
        var f1MinX = formationToMove.TopBoundary.Min(p => p.X);
        var f1MaxX = formationToMove.TopBoundary.Max(p => p.X);
        var f2MinX = fixedFormation.TopBoundary.Min(p => p.X);
        var f2MaxX = fixedFormation.TopBoundary.Max(p => p.X);
        
        float overlapMinX = Math.Max(f1MinX, f2MinX);
        float overlapMaxX = Math.Min(f1MaxX, f2MaxX);
        
        // Calculate required vertical offset
        float requiredOffset = 0f;
        
        int samples = 20;
        for (int i = 0; i <= samples; i++)
        {
            float x = overlapMinX + (overlapMaxX - overlapMinX) * i / samples;
            
            var movingTop = InterpolateY(formationToMove.TopBoundary, x);
            var movingBottom = InterpolateY(formationToMove.BottomBoundary, x);
            var fixedTop = InterpolateY(fixedFormation.TopBoundary, x);
            var fixedBottom = InterpolateY(fixedFormation.BottomBoundary, x);
            
            float offset;
            if (moveUp)
            {
                // Move formation above the fixed one
                offset = fixedTop - movingBottom + 10f; // 10m clearance
            }
            else
            {
                // Move formation below the fixed one
                offset = fixedBottom - movingTop - 10f; // 10m clearance
            }
            
            if (Math.Abs(offset) > Math.Abs(requiredOffset))
                requiredOffset = offset;
        }
        
        // Apply the offset
        if (requiredOffset != 0)
        {
            for (int i = 0; i < formationToMove.TopBoundary.Count; i++)
            {
                formationToMove.TopBoundary[i] = new Vector2(
                    formationToMove.TopBoundary[i].X,
                    formationToMove.TopBoundary[i].Y + requiredOffset
                );
            }
            
            for (int i = 0; i < formationToMove.BottomBoundary.Count; i++)
            {
                formationToMove.BottomBoundary[i] = new Vector2(
                    formationToMove.BottomBoundary[i].X,
                    formationToMove.BottomBoundary[i].Y + requiredOffset
                );
            }
            
            Logger.Log($"Resolved overlap by moving formation {(moveUp ? "up" : "down")} by {Math.Abs(requiredOffset):F1}m");
        }
    }
    
    /// <summary>
    /// Resolve all overlaps in a cross-section by adjusting formations
    /// Uses stratigraphic order (bottom-up) to determine which formations to move
    /// </summary>
    public static void ResolveAllOverlaps(List<ProjectedFormation> formations)
    {
        var overlaps = FindAllOverlaps(formations);
        if (overlaps.Count == 0)
            return;
        
        Logger.Log($"Found {overlaps.Count} formation overlaps, resolving...");
        
        // Sort formations by average depth (deepest first)
        var sortedFormations = formations
            .OrderBy(f => (f.TopBoundary.Average(p => p.Y) + f.BottomBoundary.Average(p => p.Y)) / 2f)
            .ToList();
        
        // Resolve overlaps from bottom to top
        for (int i = 0; i < sortedFormations.Count - 1; i++)
        {
            var lowerFormation = sortedFormations[i];
            
            for (int j = i + 1; j < sortedFormations.Count; j++)
            {
                var upperFormation = sortedFormations[j];
                
                if (DoFormationsOverlap(lowerFormation, upperFormation))
                {
                    // Move the upper formation up to avoid overlap
                    ResolveOverlap(upperFormation, lowerFormation, moveUp: true);
                }
            }
        }
        
        Logger.Log("All overlaps resolved");
    }
    
    /// <summary>
    /// Apply fault displacement to formations while preventing overlap
    /// This implements physically realistic fault behavior
    /// </summary>
    public static void ApplyFaultDisplacement(
        ProjectedFault fault,
        List<ProjectedFormation> formations,
        float displacementAmount)
    {
        if (fault.FaultTrace.Count < 2)
            return;
        
        // Determine which side of the fault each formation is on
        var faultLineStart = fault.FaultTrace[0];
        var faultLineEnd = fault.FaultTrace[^1];
        
        // Calculate fault normal (perpendicular to fault plane)
        var faultVector = faultLineEnd - faultLineStart;
        var faultNormal = new Vector2(-faultVector.Y, faultVector.X);
        faultNormal = Vector2.Normalize(faultNormal);
        
        // Determine displacement direction based on fault type
        bool hangingWallUp = fault.Type == GeologicalMapping.GeologicalFeatureType.Fault_Reverse ||
                             fault.Type == GeologicalMapping.GeologicalFeatureType.Fault_Thrust;
        
        foreach (var formation in formations)
        {
            // Determine if formation is on hanging wall or foot wall
            var formationCenter = new Vector2(
                formation.TopBoundary.Average(p => p.X),
                formation.TopBoundary.Average(p => p.Y)
            );
            
            var toCenter = formationCenter - faultLineStart;
            var side = Vector2.Dot(toCenter, faultNormal);
            
            bool onHangingWall = side > 0;
            
            // Apply displacement
            if ((onHangingWall && hangingWallUp) || (!onHangingWall && !hangingWallUp))
            {
                float verticalOffset = displacementAmount;
                
                for (int i = 0; i < formation.TopBoundary.Count; i++)
                {
                    formation.TopBoundary[i] = new Vector2(
                        formation.TopBoundary[i].X,
                        formation.TopBoundary[i].Y + verticalOffset
                    );
                }
                
                for (int i = 0; i < formation.BottomBoundary.Count; i++)
                {
                    formation.BottomBoundary[i] = new Vector2(
                        formation.BottomBoundary[i].X,
                        formation.BottomBoundary[i].Y + verticalOffset
                    );
                }
            }
        }
        
        // After displacement, resolve any created overlaps
        ResolveAllOverlaps(formations);
        
        Logger.Log($"Applied fault displacement of {displacementAmount:F1}m and resolved overlaps");
    }
    
    /// <summary>
    /// Check if a point is inside a formation polygon
    /// </summary>
    public static bool IsPointInFormation(Vector2 point, ProjectedFormation formation)
    {
        if (formation.TopBoundary.Count < 2 || formation.BottomBoundary.Count < 2)
            return false;
        
        // Get X bounds
        float minX = formation.TopBoundary.Min(p => p.X);
        float maxX = formation.TopBoundary.Max(p => p.X);
        
        if (point.X < minX || point.X > maxX)
            return false;
        
        // Interpolate top and bottom at this X
        float topY = InterpolateY(formation.TopBoundary, point.X);
        float bottomY = InterpolateY(formation.BottomBoundary, point.X);
        
        return point.Y <= topY && point.Y >= bottomY;
    }
    
    /// <summary>
    /// Interpolate Y coordinate at a given X position along a boundary
    /// </summary>
    private static float InterpolateY(List<Vector2> boundary, float x)
    {
        if (boundary.Count == 0) return 0f;
        if (boundary.Count == 1) return boundary[0].Y;
        
        // Clamp to boundary range
        if (x <= boundary[0].X) return boundary[0].Y;
        if (x >= boundary[^1].X) return boundary[^1].Y;
        
        // Find bracketing points
        for (int i = 0; i < boundary.Count - 1; i++)
        {
            if (x >= boundary[i].X && x <= boundary[i + 1].X)
            {
                float t = (x - boundary[i].X) / (boundary[i + 1].X - boundary[i].X);
                return boundary[i].Y + t * (boundary[i + 1].Y - boundary[i].Y);
            }
        }
        
        return boundary[^1].Y;
    }
    
    /// <summary>
    /// Validate that a formation respects geological constraints
    /// </summary>
    public static (bool valid, string message) ValidateFormation(ProjectedFormation formation, List<ProjectedFormation> existingFormations)
    {
        // Check if formation has valid geometry
        if (formation.TopBoundary.Count < 2)
            return (false, "Formation must have at least 2 points in top boundary");
        
        if (formation.BottomBoundary.Count < 2)
            return (false, "Formation must have at least 2 points in bottom boundary");
        
        // Check if top is above bottom
        for (int i = 0; i < Math.Min(formation.TopBoundary.Count, formation.BottomBoundary.Count); i++)
        {
            if (formation.TopBoundary[i].Y < formation.BottomBoundary[i].Y)
                return (false, "Formation top boundary must be above bottom boundary");
        }
        
        // Check for overlaps
        if (DoesFormationOverlapAny(formation, existingFormations))
            return (false, "Formation overlaps with existing formation(s)");
        
        return (true, "Formation is valid");
    }
    
    /// <summary>
    /// Auto-arrange formations to prevent overlaps while maintaining relative positions
    /// Useful after importing or batch operations
    /// </summary>
    public static void AutoArrangeFormations(List<ProjectedFormation> formations)
    {
        if (formations.Count == 0) return;
        
        Logger.Log($"Auto-arranging {formations.Count} formations to prevent overlaps...");
        
        // Sort by average elevation (highest first)
        var sorted = formations
            .OrderByDescending(f => (f.TopBoundary.Average(p => p.Y) + f.BottomBoundary.Average(p => p.Y)) / 2f)
            .ToList();
        
        // Process from top to bottom, ensuring no overlaps
        for (int i = 1; i < sorted.Count; i++)
        {
            var currentFormation = sorted[i];
            
            // Check against all formations above
            for (int j = 0; j < i; j++)
            {
                var upperFormation = sorted[j];
                
                if (DoFormationsOverlap(currentFormation, upperFormation))
                {
                    ResolveOverlap(currentFormation, upperFormation, moveUp: false);
                }
            }
        }
        
        Logger.Log("Auto-arrangement complete");
    }
}