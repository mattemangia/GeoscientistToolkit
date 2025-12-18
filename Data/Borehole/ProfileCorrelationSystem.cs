// GeoscientistToolkit/Data/Borehole/ProfileCorrelationSystem.cs

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Borehole;

/// <summary>
/// Represents a correlation profile - a line connecting multiple boreholes
/// </summary>
public class CorrelationProfile
{
    /// <summary>Unique identifier for this profile</summary>
    public string ID { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name for the profile</summary>
    public string Name { get; set; } = "Profile";

    /// <summary>Ordered list of borehole IDs in this profile</summary>
    public List<string> BoreholeOrder { get; set; } = new();

    /// <summary>Profile line start point (for display and intersection calculation)</summary>
    public Vector2 StartPoint { get; set; }

    /// <summary>Profile line end point</summary>
    public Vector2 EndPoint { get; set; }

    /// <summary>Azimuth angle of the profile in degrees (0=North, 90=East)</summary>
    public float Azimuth { get; set; }

    /// <summary>Color for rendering this profile</summary>
    public Vector4 Color { get; set; } = new(0.3f, 0.5f, 0.8f, 1.0f);

    /// <summary>Whether this profile is visible in viewers</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Creation timestamp</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Profile description/notes</summary>
    public string Notes { get; set; } = "";

    /// <summary>
    /// Calculate the profile line from borehole positions
    /// </summary>
    public void CalculateProfileLine(Dictionary<string, BoreholeDataset> boreholes)
    {
        if (BoreholeOrder.Count < 2) return;

        var points = BoreholeOrder
            .Where(id => boreholes.ContainsKey(id))
            .Select(id => boreholes[id].SurfaceCoordinates)
            .ToList();

        if (points.Count < 2) return;

        // Fit a line through the points using least squares
        float sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        int n = points.Count;

        foreach (var p in points)
        {
            sumX += p.X;
            sumY += p.Y;
            sumXY += p.X * p.Y;
            sumX2 += p.X * p.X;
        }

        float meanX = sumX / n;
        float meanY = sumY / n;

        // Calculate slope and extend line
        float denominator = sumX2 - sumX * sumX / n;
        if (MathF.Abs(denominator) < 0.0001f)
        {
            // Vertical line
            StartPoint = new Vector2(meanX, points.Min(p => p.Y) - 50);
            EndPoint = new Vector2(meanX, points.Max(p => p.Y) + 50);
            Azimuth = 0;
        }
        else
        {
            float slope = (sumXY - sumX * sumY / n) / denominator;
            float intercept = meanY - slope * meanX;

            // Extend line beyond borehole bounds
            float minX = points.Min(p => p.X) - 50;
            float maxX = points.Max(p => p.X) + 50;

            StartPoint = new Vector2(minX, slope * minX + intercept);
            EndPoint = new Vector2(maxX, slope * maxX + intercept);

            // Calculate azimuth
            Azimuth = MathF.Atan2(EndPoint.X - StartPoint.X, EndPoint.Y - StartPoint.Y) * 180f / MathF.PI;
            if (Azimuth < 0) Azimuth += 360;
        }
    }

    /// <summary>
    /// Get the distance along the profile for a given borehole
    /// </summary>
    public float GetDistanceAlongProfile(Vector2 boreholePosition)
    {
        var profileDir = Vector2.Normalize(EndPoint - StartPoint);
        var toPoint = boreholePosition - StartPoint;
        return Vector2.Dot(toPoint, profileDir);
    }

    /// <summary>
    /// Check if this profile is approximately parallel to another
    /// </summary>
    public bool IsParallelTo(CorrelationProfile other, float toleranceDegrees = 15f)
    {
        var angleDiff = MathF.Abs(Azimuth - other.Azimuth);
        if (angleDiff > 180) angleDiff = 360 - angleDiff;
        return angleDiff < toleranceDegrees || MathF.Abs(angleDiff - 180) < toleranceDegrees;
    }

    /// <summary>
    /// Check if this profile is approximately perpendicular to another
    /// </summary>
    public bool IsPerpendicularTo(CorrelationProfile other, float toleranceDegrees = 15f)
    {
        var angleDiff = MathF.Abs(Azimuth - other.Azimuth);
        if (angleDiff > 180) angleDiff = 360 - angleDiff;
        return MathF.Abs(angleDiff - 90) < toleranceDegrees || MathF.Abs(angleDiff - 270) < toleranceDegrees;
    }
}

/// <summary>
/// Represents an intersection point between two profiles
/// </summary>
public class ProfileIntersection
{
    public string ID { get; set; } = Guid.NewGuid().ToString();
    public string Profile1ID { get; set; }
    public string Profile2ID { get; set; }
    public Vector2 IntersectionPoint { get; set; }

    /// <summary>Distance along profile 1 to intersection</summary>
    public float DistanceAlongProfile1 { get; set; }

    /// <summary>Distance along profile 2 to intersection</summary>
    public float DistanceAlongProfile2 { get; set; }

    /// <summary>Angle between the two profiles at intersection (degrees)</summary>
    public float IntersectionAngle { get; set; }

    /// <summary>
    /// Calculate intersection between two line segments
    /// </summary>
    public static ProfileIntersection Calculate(CorrelationProfile profile1, CorrelationProfile profile2)
    {
        var p1 = profile1.StartPoint;
        var p2 = profile1.EndPoint;
        var p3 = profile2.StartPoint;
        var p4 = profile2.EndPoint;

        var d1 = p2 - p1;
        var d2 = p4 - p3;

        float cross = d1.X * d2.Y - d1.Y * d2.X;

        // Parallel lines
        if (MathF.Abs(cross) < 0.0001f)
            return null;

        var d3 = p3 - p1;
        float t = (d3.X * d2.Y - d3.Y * d2.X) / cross;
        float u = (d3.X * d1.Y - d3.Y * d1.X) / cross;

        // Intersection outside line segments (we allow extended lines)
        var intersection = new ProfileIntersection
        {
            Profile1ID = profile1.ID,
            Profile2ID = profile2.ID,
            IntersectionPoint = p1 + t * d1,
            DistanceAlongProfile1 = t * d1.Length(),
            DistanceAlongProfile2 = u * d2.Length()
        };

        // Calculate angle between profiles
        var angle = MathF.Abs(profile1.Azimuth - profile2.Azimuth);
        if (angle > 180) angle = 360 - angle;
        if (angle > 90) angle = 180 - angle;
        intersection.IntersectionAngle = angle;

        return intersection;
    }
}

/// <summary>
/// Represents a correlation between lithology units on different profiles
/// This extends LithologyCorrelation to support cross-profile correlations
/// </summary>
public class CrossProfileCorrelation
{
    public string ID { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Source profile ID</summary>
    public string SourceProfileID { get; set; }

    /// <summary>Target profile ID</summary>
    public string TargetProfileID { get; set; }

    /// <summary>Source lithology unit ID</summary>
    public string SourceLithologyID { get; set; }

    /// <summary>Source borehole ID</summary>
    public string SourceBoreholeID { get; set; }

    /// <summary>Target lithology unit ID</summary>
    public string TargetLithologyID { get; set; }

    /// <summary>Target borehole ID</summary>
    public string TargetBoreholeID { get; set; }

    /// <summary>Confidence level (0.0-1.0)</summary>
    public float Confidence { get; set; } = 1.0f;

    /// <summary>Whether this was auto-correlated</summary>
    public bool IsAutoCorrelated { get; set; }

    /// <summary>Associated profile intersection (if any)</summary>
    public string IntersectionID { get; set; }

    /// <summary>Color for rendering</summary>
    public Vector4 Color { get; set; } = new(0.8f, 0.5f, 0.3f, 0.8f);

    /// <summary>Notes</summary>
    public string Notes { get; set; } = "";

    /// <summary>Creation timestamp</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Represents an interpolated horizon surface from multiple profiles
/// </summary>
public class InterpolatedHorizon
{
    public string ID { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
    public string LithologyType { get; set; }
    public Vector4 Color { get; set; }

    /// <summary>Control points from all profiles (borehole positions + depths)</summary>
    public List<HorizonControlPoint> ControlPoints { get; set; } = new();

    /// <summary>Triangulated surface mesh (indices into ControlPoints)</summary>
    public List<(int A, int B, int C)> Triangles { get; set; } = new();

    /// <summary>Interpolated grid surface</summary>
    [JsonIgnore]
    public float[,] ElevationGrid { get; set; }

    /// <summary>Grid bounds for the elevation grid</summary>
    public BoundingBox GridBounds { get; set; }

    /// <summary>Grid resolution</summary>
    public int GridResolutionX { get; set; } = 50;
    public int GridResolutionY { get; set; } = 50;
}

/// <summary>
/// A control point for horizon interpolation
/// </summary>
public class HorizonControlPoint
{
    public string BoreholeID { get; set; }
    public string LithologyID { get; set; }
    public string ProfileID { get; set; }
    public Vector3 Position { get; set; } // X, Y, Z (Z = elevation of horizon top)
    public float Confidence { get; set; } = 1.0f;
}

/// <summary>
/// Main dataset for multi-profile borehole correlations
/// </summary>
public class MultiProfileCorrelationDataset : Dataset, ISerializableDataset
{
    public MultiProfileCorrelationDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.SubsurfaceGIS;
    }

    /// <summary>All correlation profiles</summary>
    public List<CorrelationProfile> Profiles { get; set; } = new();

    /// <summary>All borehole headers (shared across profiles)</summary>
    public Dictionary<string, BoreholeHeader> Headers { get; set; } = new();

    /// <summary>Correlations within profiles (adjacent boreholes)</summary>
    public List<LithologyCorrelation> IntraProfileCorrelations { get; set; } = new();

    /// <summary>Correlations between profiles</summary>
    public List<CrossProfileCorrelation> CrossProfileCorrelations { get; set; } = new();

    /// <summary>Profile intersections</summary>
    public List<ProfileIntersection> Intersections { get; set; } = new();

    /// <summary>Interpolated horizons from all correlations</summary>
    public List<InterpolatedHorizon> Horizons { get; set; } = new();

    /// <summary>Project metadata</summary>
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime ModifiedDate { get; set; } = DateTime.Now;

    /// <summary>Display settings</summary>
    public ProfileCorrelationDisplaySettings DisplaySettings { get; set; } = new();

    /// <summary>Interpolation settings for surface generation</summary>
    public SurfaceInterpolationSettings InterpolationSettings { get; set; } = new();

    #region Profile Management

    /// <summary>
    /// Create a new profile from selected boreholes
    /// </summary>
    public CorrelationProfile CreateProfile(string name, List<string> boreholeIDs,
        Dictionary<string, BoreholeDataset> boreholes)
    {
        var profile = new CorrelationProfile
        {
            Name = name,
            BoreholeOrder = new List<string>(boreholeIDs)
        };

        // Ensure boreholes are in headers
        foreach (var id in boreholeIDs)
        {
            if (!Headers.ContainsKey(id) && boreholes.TryGetValue(id, out var bh))
            {
                Headers[id] = new BoreholeHeader
                {
                    BoreholeID = id,
                    DisplayName = bh.WellName,
                    Coordinates = bh.SurfaceCoordinates,
                    Elevation = bh.Elevation,
                    TotalDepth = bh.TotalDepth
                };
            }
        }

        // Sort boreholes along the profile line
        SortBoreholesByProjection(profile, boreholes);

        // Calculate profile line
        profile.CalculateProfileLine(boreholes);

        // Assign unique color
        profile.Color = GetNextProfileColor();

        Profiles.Add(profile);
        ModifiedDate = DateTime.Now;

        // Recalculate intersections
        UpdateIntersections();

        Logger.Log($"Created profile '{name}' with {boreholeIDs.Count} boreholes");
        return profile;
    }

    /// <summary>
    /// Sort boreholes along the best-fit profile line
    /// </summary>
    private void SortBoreholesByProjection(CorrelationProfile profile, Dictionary<string, BoreholeDataset> boreholes)
    {
        if (profile.BoreholeOrder.Count < 2) return;

        var positions = profile.BoreholeOrder
            .Where(id => boreholes.ContainsKey(id))
            .Select(id => (ID: id, Pos: boreholes[id].SurfaceCoordinates))
            .ToList();

        if (positions.Count < 2) return;

        // Find the best projection axis (PCA-like approach)
        var centroid = new Vector2(
            positions.Average(p => p.Pos.X),
            positions.Average(p => p.Pos.Y));

        // Calculate covariance
        float cxx = 0, cyy = 0, cxy = 0;
        foreach (var (_, pos) in positions)
        {
            var dx = pos.X - centroid.X;
            var dy = pos.Y - centroid.Y;
            cxx += dx * dx;
            cyy += dy * dy;
            cxy += dx * dy;
        }

        // Principal direction
        float theta = 0.5f * MathF.Atan2(2 * cxy, cxx - cyy);
        var direction = new Vector2(MathF.Cos(theta), MathF.Sin(theta));

        // Sort by projection onto principal axis
        var sorted = positions
            .OrderBy(p => Vector2.Dot(p.Pos - centroid, direction))
            .Select(p => p.ID)
            .ToList();

        profile.BoreholeOrder = sorted;
    }

    /// <summary>
    /// Remove a profile
    /// </summary>
    public void RemoveProfile(string profileID)
    {
        var profile = Profiles.FirstOrDefault(p => p.ID == profileID);
        if (profile == null) return;

        // Remove intra-profile correlations for this profile
        IntraProfileCorrelations.RemoveAll(c =>
        {
            var sourceInProfile = profile.BoreholeOrder.Contains(c.SourceBoreholeID);
            var targetInProfile = profile.BoreholeOrder.Contains(c.TargetBoreholeID);
            return sourceInProfile && targetInProfile;
        });

        // Remove cross-profile correlations involving this profile
        CrossProfileCorrelations.RemoveAll(c =>
            c.SourceProfileID == profileID || c.TargetProfileID == profileID);

        Profiles.Remove(profile);
        UpdateIntersections();
        ModifiedDate = DateTime.Now;

        Logger.Log($"Removed profile '{profile.Name}'");
    }

    /// <summary>
    /// Update all profile intersections
    /// </summary>
    public void UpdateIntersections()
    {
        Intersections.Clear();

        for (int i = 0; i < Profiles.Count; i++)
        {
            for (int j = i + 1; j < Profiles.Count; j++)
            {
                var intersection = ProfileIntersection.Calculate(Profiles[i], Profiles[j]);
                if (intersection != null)
                {
                    Intersections.Add(intersection);
                }
            }
        }

        Logger.Log($"Found {Intersections.Count} profile intersections");
    }

    private Vector4 GetNextProfileColor()
    {
        var colors = new[]
        {
            new Vector4(0.3f, 0.5f, 0.8f, 1.0f), // Blue
            new Vector4(0.8f, 0.3f, 0.3f, 1.0f), // Red
            new Vector4(0.3f, 0.8f, 0.3f, 1.0f), // Green
            new Vector4(0.8f, 0.8f, 0.3f, 1.0f), // Yellow
            new Vector4(0.8f, 0.3f, 0.8f, 1.0f), // Magenta
            new Vector4(0.3f, 0.8f, 0.8f, 1.0f), // Cyan
            new Vector4(0.8f, 0.5f, 0.3f, 1.0f), // Orange
            new Vector4(0.5f, 0.3f, 0.8f, 1.0f), // Purple
        };

        return colors[Profiles.Count % colors.Length];
    }

    #endregion

    #region Correlation Management

    /// <summary>
    /// Add an intra-profile correlation (between adjacent boreholes in a profile)
    /// </summary>
    public bool AddIntraProfileCorrelation(string profileID,
        string sourceLithologyID, string sourceBoreholeID,
        string targetLithologyID, string targetBoreholeID,
        float confidence = 1.0f, bool isAuto = false)
    {
        var profile = Profiles.FirstOrDefault(p => p.ID == profileID);
        if (profile == null)
        {
            Logger.LogWarning($"Profile {profileID} not found");
            return false;
        }

        var sourceIndex = profile.BoreholeOrder.IndexOf(sourceBoreholeID);
        var targetIndex = profile.BoreholeOrder.IndexOf(targetBoreholeID);

        if (sourceIndex == -1 || targetIndex == -1)
        {
            Logger.LogWarning("Boreholes not found in profile");
            return false;
        }

        // Check adjacency
        if (Math.Abs(sourceIndex - targetIndex) != 1)
        {
            Logger.LogWarning("Can only correlate adjacent boreholes within a profile");
            return false;
        }

        // Check for existing correlation
        var existing = IntraProfileCorrelations.FirstOrDefault(c =>
            (c.SourceLithologyID == sourceLithologyID && c.TargetBoreholeID == targetBoreholeID) ||
            (c.TargetLithologyID == sourceLithologyID && c.SourceBoreholeID == targetBoreholeID));

        if (existing != null)
        {
            Logger.LogWarning("Correlation already exists");
            return false;
        }

        var correlation = new LithologyCorrelation
        {
            SourceLithologyID = sourceLithologyID,
            SourceBoreholeID = sourceBoreholeID,
            TargetLithologyID = targetLithologyID,
            TargetBoreholeID = targetBoreholeID,
            Confidence = confidence,
            IsAutoCorrelated = isAuto
        };

        IntraProfileCorrelations.Add(correlation);
        ModifiedDate = DateTime.Now;

        Logger.Log($"Added intra-profile correlation in {profile.Name}");
        return true;
    }

    /// <summary>
    /// Add a cross-profile correlation (between boreholes on different profiles)
    /// </summary>
    public bool AddCrossProfileCorrelation(
        string sourceProfileID, string sourceLithologyID, string sourceBoreholeID,
        string targetProfileID, string targetLithologyID, string targetBoreholeID,
        float confidence = 1.0f, bool isAuto = false)
    {
        var sourceProfile = Profiles.FirstOrDefault(p => p.ID == sourceProfileID);
        var targetProfile = Profiles.FirstOrDefault(p => p.ID == targetProfileID);

        if (sourceProfile == null || targetProfile == null)
        {
            Logger.LogWarning("Source or target profile not found");
            return false;
        }

        if (sourceProfileID == targetProfileID)
        {
            Logger.LogWarning("Use AddIntraProfileCorrelation for same-profile correlations");
            return false;
        }

        // Check for existing
        var existing = CrossProfileCorrelations.FirstOrDefault(c =>
            (c.SourceLithologyID == sourceLithologyID && c.TargetLithologyID == targetLithologyID) ||
            (c.TargetLithologyID == sourceLithologyID && c.SourceLithologyID == targetLithologyID));

        if (existing != null)
        {
            Logger.LogWarning("Cross-profile correlation already exists");
            return false;
        }

        // Find nearest intersection
        var intersectionID = Intersections
            .FirstOrDefault(i =>
                (i.Profile1ID == sourceProfileID && i.Profile2ID == targetProfileID) ||
                (i.Profile2ID == sourceProfileID && i.Profile1ID == targetProfileID))?.ID;

        var correlation = new CrossProfileCorrelation
        {
            SourceProfileID = sourceProfileID,
            TargetProfileID = targetProfileID,
            SourceLithologyID = sourceLithologyID,
            SourceBoreholeID = sourceBoreholeID,
            TargetLithologyID = targetLithologyID,
            TargetBoreholeID = targetBoreholeID,
            Confidence = confidence,
            IsAutoCorrelated = isAuto,
            IntersectionID = intersectionID
        };

        CrossProfileCorrelations.Add(correlation);
        ModifiedDate = DateTime.Now;

        Logger.Log($"Added cross-profile correlation between {sourceProfile.Name} and {targetProfile.Name}");
        return true;
    }

    /// <summary>
    /// Remove a correlation
    /// </summary>
    public void RemoveCorrelation(string correlationID, bool isCrossProfile)
    {
        if (isCrossProfile)
        {
            CrossProfileCorrelations.RemoveAll(c => c.ID == correlationID);
        }
        else
        {
            IntraProfileCorrelations.RemoveAll(c => c.ID == correlationID);
        }
        ModifiedDate = DateTime.Now;
    }

    /// <summary>
    /// Auto-correlate lithologies with matching names/types
    /// </summary>
    public int AutoCorrelate(Dictionary<string, BoreholeDataset> boreholes,
        float confidenceThreshold = 0.7f)
    {
        int correlationsAdded = 0;

        // Auto-correlate within profiles
        foreach (var profile in Profiles)
        {
            for (int i = 0; i < profile.BoreholeOrder.Count - 1; i++)
            {
                var bhID1 = profile.BoreholeOrder[i];
                var bhID2 = profile.BoreholeOrder[i + 1];

                if (!boreholes.TryGetValue(bhID1, out var bh1) ||
                    !boreholes.TryGetValue(bhID2, out var bh2))
                    continue;

                foreach (var unit1 in bh1.LithologyUnits)
                {
                    // Find matching units in adjacent borehole
                    var matches = bh2.LithologyUnits
                        .Where(u2 => IsSimilarLithology(unit1, u2))
                        .OrderBy(u2 => MathF.Abs(unit1.DepthFrom - u2.DepthFrom))
                        .ToList();

                    if (matches.Count > 0)
                    {
                        var bestMatch = matches.First();
                        float confidence = CalculateCorrelationConfidence(unit1, bestMatch, bh1, bh2);

                        if (confidence >= confidenceThreshold)
                        {
                            if (AddIntraProfileCorrelation(profile.ID, unit1.ID, bhID1,
                                bestMatch.ID, bhID2, confidence, true))
                            {
                                correlationsAdded++;
                            }
                        }
                    }
                }
            }
        }

        // Auto-correlate between profiles at intersections
        foreach (var intersection in Intersections)
        {
            var profile1 = Profiles.FirstOrDefault(p => p.ID == intersection.Profile1ID);
            var profile2 = Profiles.FirstOrDefault(p => p.ID == intersection.Profile2ID);

            if (profile1 == null || profile2 == null) continue;

            // Find boreholes closest to intersection on each profile
            var closest1 = FindClosestBoreholeToPoint(profile1, intersection.IntersectionPoint, boreholes);
            var closest2 = FindClosestBoreholeToPoint(profile2, intersection.IntersectionPoint, boreholes);

            if (closest1 == null || closest2 == null) continue;

            foreach (var unit1 in closest1.LithologyUnits)
            {
                var matches = closest2.LithologyUnits
                    .Where(u2 => IsSimilarLithology(unit1, u2))
                    .ToList();

                foreach (var match in matches)
                {
                    float confidence = CalculateCorrelationConfidence(unit1, match, closest1, closest2);
                    if (confidence >= confidenceThreshold)
                    {
                        var bh1ID = Headers.FirstOrDefault(h => h.Value.DisplayName == closest1.WellName).Key;
                        var bh2ID = Headers.FirstOrDefault(h => h.Value.DisplayName == closest2.WellName).Key;

                        if (AddCrossProfileCorrelation(profile1.ID, unit1.ID, bh1ID,
                            profile2.ID, match.ID, bh2ID, confidence, true))
                        {
                            correlationsAdded++;
                        }
                    }
                }
            }
        }

        Logger.Log($"Auto-correlation added {correlationsAdded} correlations");
        return correlationsAdded;
    }

    private bool IsSimilarLithology(LithologyUnit unit1, LithologyUnit unit2)
    {
        // Check if lithology types match
        if (!string.IsNullOrEmpty(unit1.LithologyType) && !string.IsNullOrEmpty(unit2.LithologyType))
        {
            if (unit1.LithologyType.Equals(unit2.LithologyType, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check if names are similar
        if (!string.IsNullOrEmpty(unit1.Name) && !string.IsNullOrEmpty(unit2.Name))
        {
            if (unit1.Name.Equals(unit2.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private float CalculateCorrelationConfidence(LithologyUnit unit1, LithologyUnit unit2,
        BoreholeDataset bh1, BoreholeDataset bh2)
    {
        float confidence = 0.5f;

        // Bonus for matching type
        if (unit1.LithologyType == unit2.LithologyType)
            confidence += 0.2f;

        // Bonus for matching name
        if (unit1.Name == unit2.Name)
            confidence += 0.15f;

        // Bonus for similar thickness
        float thickness1 = unit1.DepthTo - unit1.DepthFrom;
        float thickness2 = unit2.DepthTo - unit2.DepthFrom;
        float thicknessRatio = Math.Min(thickness1, thickness2) / Math.Max(thickness1, thickness2);
        confidence += thicknessRatio * 0.1f;

        // Penalty for large depth difference (relative to borehole distance)
        float distance = Vector2.Distance(bh1.SurfaceCoordinates, bh2.SurfaceCoordinates);
        float depthDiff = MathF.Abs(unit1.DepthFrom - unit2.DepthFrom);
        float normalizedDepthDiff = depthDiff / Math.Max(distance, 1);
        confidence -= Math.Min(normalizedDepthDiff * 0.1f, 0.2f);

        return Math.Clamp(confidence, 0, 1);
    }

    private BoreholeDataset FindClosestBoreholeToPoint(CorrelationProfile profile, Vector2 point,
        Dictionary<string, BoreholeDataset> boreholes)
    {
        BoreholeDataset closest = null;
        float minDistance = float.MaxValue;

        foreach (var bhID in profile.BoreholeOrder)
        {
            if (!boreholes.TryGetValue(bhID, out var bh)) continue;
            var dist = Vector2.Distance(bh.SurfaceCoordinates, point);
            if (dist < minDistance)
            {
                minDistance = dist;
                closest = bh;
            }
        }

        return closest;
    }

    #endregion

    #region Horizon Interpolation

    /// <summary>
    /// Build interpolated horizons from all correlations
    /// </summary>
    public void BuildHorizons(Dictionary<string, BoreholeDataset> boreholes)
    {
        Horizons.Clear();
        var visited = new HashSet<string>();
        var horizonGroups = new Dictionary<string, List<HorizonControlPoint>>();

        // Group correlated lithology units into horizons
        // First, process intra-profile correlations to build chains
        foreach (var profile in Profiles)
        {
            var profileCorrelations = IntraProfileCorrelations.Where(c =>
                profile.BoreholeOrder.Contains(c.SourceBoreholeID) &&
                profile.BoreholeOrder.Contains(c.TargetBoreholeID)).ToList();

            foreach (var correlation in profileCorrelations)
            {
                var groupKey = GetOrCreateHorizonGroup(correlation.SourceLithologyID,
                    correlation.TargetLithologyID, horizonGroups, visited, boreholes, profile.ID);

                AddControlPointIfNew(horizonGroups[groupKey], correlation.SourceBoreholeID,
                    correlation.SourceLithologyID, profile.ID, boreholes);
                AddControlPointIfNew(horizonGroups[groupKey], correlation.TargetBoreholeID,
                    correlation.TargetLithologyID, profile.ID, boreholes);
            }
        }

        // Then merge groups connected by cross-profile correlations
        foreach (var crossCorr in CrossProfileCorrelations)
        {
            // Find groups containing source and target
            string sourceGroup = horizonGroups.Keys.FirstOrDefault(k =>
                horizonGroups[k].Any(cp => cp.LithologyID == crossCorr.SourceLithologyID));
            string targetGroup = horizonGroups.Keys.FirstOrDefault(k =>
                horizonGroups[k].Any(cp => cp.LithologyID == crossCorr.TargetLithologyID));

            if (sourceGroup != null && targetGroup != null && sourceGroup != targetGroup)
            {
                // Merge target into source
                horizonGroups[sourceGroup].AddRange(horizonGroups[targetGroup]);
                horizonGroups.Remove(targetGroup);
            }
            else if (sourceGroup != null)
            {
                AddControlPointIfNew(horizonGroups[sourceGroup], crossCorr.TargetBoreholeID,
                    crossCorr.TargetLithologyID, crossCorr.TargetProfileID, boreholes);
            }
            else if (targetGroup != null)
            {
                AddControlPointIfNew(horizonGroups[targetGroup], crossCorr.SourceBoreholeID,
                    crossCorr.SourceLithologyID, crossCorr.SourceProfileID, boreholes);
            }
        }

        // Create horizons from groups
        foreach (var kvp in horizonGroups)
        {
            if (kvp.Value.Count < 3) continue; // Need at least 3 points for surface

            var firstPoint = kvp.Value.First();
            if (!boreholes.TryGetValue(firstPoint.BoreholeID, out var firstBh)) continue;
            var firstUnit = firstBh.LithologyUnits.FirstOrDefault(u => u.ID == firstPoint.LithologyID);
            if (firstUnit == null) continue;

            var horizon = new InterpolatedHorizon
            {
                Name = firstUnit.Name ?? firstUnit.LithologyType ?? "Unnamed Horizon",
                LithologyType = firstUnit.LithologyType,
                Color = firstUnit.Color,
                ControlPoints = kvp.Value
            };

            // Calculate grid bounds
            float minX = kvp.Value.Min(p => p.Position.X);
            float maxX = kvp.Value.Max(p => p.Position.X);
            float minY = kvp.Value.Min(p => p.Position.Y);
            float maxY = kvp.Value.Max(p => p.Position.Y);
            float buffer = Math.Max(maxX - minX, maxY - minY) * 0.1f;

            horizon.GridBounds = new BoundingBox
            {
                Min = new Vector2(minX - buffer, minY - buffer),
                Max = new Vector2(maxX + buffer, maxY + buffer)
            };

            // Triangulate the surface
            TriangulateHorizon(horizon);

            // Interpolate grid
            InterpolateHorizonGrid(horizon);

            Horizons.Add(horizon);
        }

        Logger.Log($"Built {Horizons.Count} interpolated horizons");
        ModifiedDate = DateTime.Now;
    }

    private string GetOrCreateHorizonGroup(string lithologyID1, string lithologyID2,
        Dictionary<string, List<HorizonControlPoint>> groups, HashSet<string> visited,
        Dictionary<string, BoreholeDataset> boreholes, string profileID)
    {
        // Check if either ID is already in a group
        foreach (var kvp in groups)
        {
            if (kvp.Value.Any(cp => cp.LithologyID == lithologyID1 || cp.LithologyID == lithologyID2))
            {
                return kvp.Key;
            }
        }

        // Create new group
        var groupKey = Guid.NewGuid().ToString();
        groups[groupKey] = new List<HorizonControlPoint>();
        return groupKey;
    }

    private void AddControlPointIfNew(List<HorizonControlPoint> points, string boreholeID,
        string lithologyID, string profileID, Dictionary<string, BoreholeDataset> boreholes)
    {
        if (points.Any(cp => cp.LithologyID == lithologyID)) return;

        if (!boreholes.TryGetValue(boreholeID, out var bh)) return;
        var unit = bh.LithologyUnits.FirstOrDefault(u => u.ID == lithologyID);
        if (unit == null) return;

        var controlPoint = new HorizonControlPoint
        {
            BoreholeID = boreholeID,
            LithologyID = lithologyID,
            ProfileID = profileID,
            Position = new Vector3(
                bh.SurfaceCoordinates.X,
                bh.SurfaceCoordinates.Y,
                bh.Elevation - unit.DepthFrom)
        };

        points.Add(controlPoint);
    }

    /// <summary>
    /// Triangulate horizon control points using Delaunay triangulation
    /// </summary>
    private void TriangulateHorizon(InterpolatedHorizon horizon)
    {
        var points = horizon.ControlPoints;
        if (points.Count < 3) return;

        // Simple Delaunay triangulation using Bowyer-Watson algorithm
        horizon.Triangles = DelaunayTriangulate(points);
    }

    /// <summary>
    /// Bowyer-Watson Delaunay triangulation algorithm
    /// </summary>
    private List<(int A, int B, int C)> DelaunayTriangulate(List<HorizonControlPoint> points)
    {
        var triangles = new List<(int A, int B, int C)>();
        if (points.Count < 3) return triangles;

        // Create super-triangle that contains all points
        float minX = points.Min(p => p.Position.X);
        float maxX = points.Max(p => p.Position.X);
        float minY = points.Min(p => p.Position.Y);
        float maxY = points.Max(p => p.Position.Y);

        float dx = maxX - minX;
        float dy = maxY - minY;
        float deltaMax = Math.Max(dx, dy) * 2;

        // Super-triangle vertices (as virtual points with negative indices)
        var superTriangle = new List<Vector2>
        {
            new Vector2(minX - deltaMax, minY - deltaMax),
            new Vector2(minX + dx / 2, maxY + deltaMax),
            new Vector2(maxX + deltaMax, minY - deltaMax)
        };

        // Start with super-triangle
        var workingTriangles = new List<(int A, int B, int C)> { (-1, -2, -3) };

        // Add points one at a time
        for (int i = 0; i < points.Count; i++)
        {
            var point = new Vector2(points[i].Position.X, points[i].Position.Y);
            var badTriangles = new List<(int A, int B, int C)>();

            // Find all triangles whose circumcircle contains the point
            foreach (var tri in workingTriangles)
            {
                if (IsInCircumcircle(point, tri, points, superTriangle))
                {
                    badTriangles.Add(tri);
                }
            }

            // Find the boundary of the polygonal hole
            var polygon = new List<(int A, int B)>();
            foreach (var tri in badTriangles)
            {
                var edges = new[] { (tri.A, tri.B), (tri.B, tri.C), (tri.C, tri.A) };
                foreach (var edge in edges)
                {
                    bool isShared = badTriangles.Any(other =>
                        other != tri && TriangleContainsEdge(other, edge));
                    if (!isShared)
                    {
                        polygon.Add(edge);
                    }
                }
            }

            // Remove bad triangles
            foreach (var tri in badTriangles)
            {
                workingTriangles.Remove(tri);
            }

            // Create new triangles from polygon edges to new point
            foreach (var edge in polygon)
            {
                workingTriangles.Add((edge.A, edge.B, i));
            }
        }

        // Remove triangles that share vertices with super-triangle
        foreach (var tri in workingTriangles)
        {
            if (tri.A >= 0 && tri.B >= 0 && tri.C >= 0)
            {
                triangles.Add(tri);
            }
        }

        return triangles;
    }

    private bool IsInCircumcircle(Vector2 point, (int A, int B, int C) triangle,
        List<HorizonControlPoint> points, List<Vector2> superVertices)
    {
        var a = GetVertex(triangle.A, points, superVertices);
        var b = GetVertex(triangle.B, points, superVertices);
        var c = GetVertex(triangle.C, points, superVertices);

        // Calculate circumcircle using determinant method
        float ax = a.X - point.X;
        float ay = a.Y - point.Y;
        float bx = b.X - point.X;
        float by = b.Y - point.Y;
        float cx = c.X - point.X;
        float cy = c.Y - point.Y;

        float det = (ax * ax + ay * ay) * (bx * cy - cx * by)
                  - (bx * bx + by * by) * (ax * cy - cx * ay)
                  + (cx * cx + cy * cy) * (ax * by - bx * ay);

        // Positive means inside (for counter-clockwise triangle)
        return det > 0;
    }

    private Vector2 GetVertex(int index, List<HorizonControlPoint> points, List<Vector2> superVertices)
    {
        if (index >= 0)
            return new Vector2(points[index].Position.X, points[index].Position.Y);
        else
            return superVertices[-(index + 1)];
    }

    private bool TriangleContainsEdge((int A, int B, int C) triangle, (int A, int B) edge)
    {
        var edges = new[] { (triangle.A, triangle.B), (triangle.B, triangle.C), (triangle.C, triangle.A) };
        return edges.Any(e =>
            (e.Item1 == edge.A && e.Item2 == edge.B) ||
            (e.Item1 == edge.B && e.Item2 == edge.A));
    }

    /// <summary>
    /// Interpolate horizon onto a regular grid
    /// </summary>
    private void InterpolateHorizonGrid(InterpolatedHorizon horizon)
    {
        horizon.ElevationGrid = new float[horizon.GridResolutionX, horizon.GridResolutionY];

        var bounds = horizon.GridBounds;
        float stepX = bounds.Width / (horizon.GridResolutionX - 1);
        float stepY = bounds.Height / (horizon.GridResolutionY - 1);

        for (int i = 0; i < horizon.GridResolutionX; i++)
        {
            for (int j = 0; j < horizon.GridResolutionY; j++)
            {
                float x = bounds.Min.X + i * stepX;
                float y = bounds.Min.Y + j * stepY;

                horizon.ElevationGrid[i, j] = InterpolateElevationAt(
                    new Vector2(x, y), horizon.ControlPoints, InterpolationSettings);
            }
        }
    }

    /// <summary>
    /// Interpolate elevation at a point using IDW or other methods
    /// </summary>
    private float InterpolateElevationAt(Vector2 point, List<HorizonControlPoint> controlPoints,
        SurfaceInterpolationSettings settings)
    {
        if (controlPoints.Count == 0) return 0;

        switch (settings.Method)
        {
            case InterpolationMethod.NearestNeighbor:
                return controlPoints
                    .OrderBy(cp => Vector2.Distance(new Vector2(cp.Position.X, cp.Position.Y), point))
                    .First().Position.Z;

            case InterpolationMethod.InverseDistanceWeighted:
            default:
                float weightSum = 0;
                float elevationSum = 0;

                foreach (var cp in controlPoints)
                {
                    float dist = Vector2.Distance(new Vector2(cp.Position.X, cp.Position.Y), point);
                    if (dist < 0.001f) return cp.Position.Z;

                    float weight = cp.Confidence / MathF.Pow(dist, settings.IDWPower);
                    weightSum += weight;
                    elevationSum += cp.Position.Z * weight;
                }

                return weightSum > 0 ? elevationSum / weightSum : 0;
        }
    }

    #endregion

    #region Serialization

    public override long GetSizeInBytes()
    {
        return Profiles.Count * 500 +
               Headers.Count * 100 +
               IntraProfileCorrelations.Count * 200 +
               CrossProfileCorrelations.Count * 250 +
               Horizons.Count * 1000;
    }

    public override void Load()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
        {
            IsMissing = true;
            return;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var dto = JsonSerializer.Deserialize<MultiProfileCorrelationDTO>(json);
            if (dto != null)
                LoadFromDTO(dto);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load multi-profile correlation dataset: {ex.Message}");
            IsMissing = true;
        }
    }

    public override void Unload()
    {
        Profiles.Clear();
        Headers.Clear();
        IntraProfileCorrelations.Clear();
        CrossProfileCorrelations.Clear();
        Horizons.Clear();
        Intersections.Clear();
    }

    public void SaveToFile(string path)
    {
        try
        {
            var dto = ToSerializableObject() as MultiProfileCorrelationDTO;
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(dto, options);
            File.WriteAllText(path, json);
            FilePath = path;
            Logger.Log($"Saved multi-profile correlation dataset to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save multi-profile correlation dataset: {ex.Message}");
        }
    }

    public object ToSerializableObject()
    {
        return new MultiProfileCorrelationDTO
        {
            TypeName = "MultiProfileCorrelation",
            Name = Name,
            FilePath = FilePath,
            Profiles = Profiles.Select(p => new CorrelationProfileDTO
            {
                ID = p.ID,
                Name = p.Name,
                BoreholeOrder = p.BoreholeOrder,
                StartPointX = p.StartPoint.X,
                StartPointY = p.StartPoint.Y,
                EndPointX = p.EndPoint.X,
                EndPointY = p.EndPoint.Y,
                Azimuth = p.Azimuth,
                ColorR = p.Color.X,
                ColorG = p.Color.Y,
                ColorB = p.Color.Z,
                ColorA = p.Color.W,
                IsVisible = p.IsVisible,
                CreatedAt = p.CreatedAt,
                Notes = p.Notes
            }).ToList(),
            Headers = Headers.ToDictionary(kvp => kvp.Key, kvp => new BoreholeHeaderDTO
            {
                BoreholeID = kvp.Value.BoreholeID,
                DisplayName = kvp.Value.DisplayName,
                CoordinatesX = kvp.Value.Coordinates.X,
                CoordinatesY = kvp.Value.Coordinates.Y,
                Elevation = kvp.Value.Elevation,
                TotalDepth = kvp.Value.TotalDepth,
                PositionIndex = kvp.Value.PositionIndex,
                Field = kvp.Value.Field,
                CustomLabel = kvp.Value.CustomLabel
            }),
            IntraProfileCorrelations = IntraProfileCorrelations.Select(c => new LithologyCorrelationDTO
            {
                ID = c.ID,
                SourceLithologyID = c.SourceLithologyID,
                SourceBoreholeID = c.SourceBoreholeID,
                TargetLithologyID = c.TargetLithologyID,
                TargetBoreholeID = c.TargetBoreholeID,
                Confidence = c.Confidence,
                IsAutoCorrelated = c.IsAutoCorrelated,
                ColorR = c.Color.X,
                ColorG = c.Color.Y,
                ColorB = c.Color.Z,
                ColorA = c.Color.W,
                Notes = c.Notes,
                CreatedAt = c.CreatedAt
            }).ToList(),
            CrossProfileCorrelations = CrossProfileCorrelations.Select(c => new CrossProfileCorrelationDTO
            {
                ID = c.ID,
                SourceProfileID = c.SourceProfileID,
                TargetProfileID = c.TargetProfileID,
                SourceLithologyID = c.SourceLithologyID,
                SourceBoreholeID = c.SourceBoreholeID,
                TargetLithologyID = c.TargetLithologyID,
                TargetBoreholeID = c.TargetBoreholeID,
                Confidence = c.Confidence,
                IsAutoCorrelated = c.IsAutoCorrelated,
                IntersectionID = c.IntersectionID,
                ColorR = c.Color.X,
                ColorG = c.Color.Y,
                ColorB = c.Color.Z,
                ColorA = c.Color.W,
                Notes = c.Notes,
                CreatedAt = c.CreatedAt
            }).ToList(),
            Intersections = Intersections.Select(i => new ProfileIntersectionDTO
            {
                ID = i.ID,
                Profile1ID = i.Profile1ID,
                Profile2ID = i.Profile2ID,
                IntersectionPointX = i.IntersectionPoint.X,
                IntersectionPointY = i.IntersectionPoint.Y,
                DistanceAlongProfile1 = i.DistanceAlongProfile1,
                DistanceAlongProfile2 = i.DistanceAlongProfile2,
                IntersectionAngle = i.IntersectionAngle
            }).ToList(),
            Horizons = Horizons.Select(h => new InterpolatedHorizonDTO
            {
                ID = h.ID,
                Name = h.Name,
                LithologyType = h.LithologyType,
                ColorR = h.Color.X,
                ColorG = h.Color.Y,
                ColorB = h.Color.Z,
                ColorA = h.Color.W,
                ControlPoints = h.ControlPoints.Select(cp => new HorizonControlPointDTO
                {
                    BoreholeID = cp.BoreholeID,
                    LithologyID = cp.LithologyID,
                    ProfileID = cp.ProfileID,
                    PositionX = cp.Position.X,
                    PositionY = cp.Position.Y,
                    PositionZ = cp.Position.Z,
                    Confidence = cp.Confidence
                }).ToList(),
                Triangles = h.Triangles.Select(t => new[] { t.A, t.B, t.C }).ToList(),
                GridBounds = h.GridBounds != null ? new BoundingBoxDTO
                {
                    Min = h.GridBounds.Min,
                    Max = h.GridBounds.Max
                } : null,
                GridResolutionX = h.GridResolutionX,
                GridResolutionY = h.GridResolutionY
            }).ToList(),
            Description = Description,
            Author = Author,
            CreatedDate = CreatedDate,
            ModifiedDate = ModifiedDate,
            DisplaySettings = DisplaySettings,
            InterpolationSettings = InterpolationSettings
        };
    }

    private void LoadFromDTO(MultiProfileCorrelationDTO dto)
    {
        Description = dto.Description;
        Author = dto.Author;
        CreatedDate = dto.CreatedDate;
        ModifiedDate = dto.ModifiedDate;
        DisplaySettings = dto.DisplaySettings ?? new ProfileCorrelationDisplaySettings();
        InterpolationSettings = dto.InterpolationSettings ?? new SurfaceInterpolationSettings();

        Profiles.Clear();
        if (dto.Profiles != null)
        {
            foreach (var p in dto.Profiles)
            {
                Profiles.Add(new CorrelationProfile
                {
                    ID = p.ID,
                    Name = p.Name,
                    BoreholeOrder = p.BoreholeOrder ?? new List<string>(),
                    StartPoint = new Vector2(p.StartPointX, p.StartPointY),
                    EndPoint = new Vector2(p.EndPointX, p.EndPointY),
                    Azimuth = p.Azimuth,
                    Color = new Vector4(p.ColorR, p.ColorG, p.ColorB, p.ColorA),
                    IsVisible = p.IsVisible,
                    CreatedAt = p.CreatedAt,
                    Notes = p.Notes
                });
            }
        }

        Headers.Clear();
        if (dto.Headers != null)
        {
            foreach (var kvp in dto.Headers)
            {
                Headers[kvp.Key] = new BoreholeHeader
                {
                    BoreholeID = kvp.Value.BoreholeID,
                    DisplayName = kvp.Value.DisplayName,
                    Coordinates = new Vector2(kvp.Value.CoordinatesX, kvp.Value.CoordinatesY),
                    Elevation = kvp.Value.Elevation,
                    TotalDepth = kvp.Value.TotalDepth,
                    PositionIndex = kvp.Value.PositionIndex,
                    Field = kvp.Value.Field,
                    CustomLabel = kvp.Value.CustomLabel
                };
            }
        }

        IntraProfileCorrelations.Clear();
        if (dto.IntraProfileCorrelations != null)
        {
            foreach (var c in dto.IntraProfileCorrelations)
            {
                IntraProfileCorrelations.Add(new LithologyCorrelation
                {
                    ID = c.ID,
                    SourceLithologyID = c.SourceLithologyID,
                    SourceBoreholeID = c.SourceBoreholeID,
                    TargetLithologyID = c.TargetLithologyID,
                    TargetBoreholeID = c.TargetBoreholeID,
                    Confidence = c.Confidence,
                    IsAutoCorrelated = c.IsAutoCorrelated,
                    Color = new Vector4(c.ColorR, c.ColorG, c.ColorB, c.ColorA),
                    Notes = c.Notes,
                    CreatedAt = c.CreatedAt
                });
            }
        }

        CrossProfileCorrelations.Clear();
        if (dto.CrossProfileCorrelations != null)
        {
            foreach (var c in dto.CrossProfileCorrelations)
            {
                CrossProfileCorrelations.Add(new CrossProfileCorrelation
                {
                    ID = c.ID,
                    SourceProfileID = c.SourceProfileID,
                    TargetProfileID = c.TargetProfileID,
                    SourceLithologyID = c.SourceLithologyID,
                    SourceBoreholeID = c.SourceBoreholeID,
                    TargetLithologyID = c.TargetLithologyID,
                    TargetBoreholeID = c.TargetBoreholeID,
                    Confidence = c.Confidence,
                    IsAutoCorrelated = c.IsAutoCorrelated,
                    IntersectionID = c.IntersectionID,
                    Color = new Vector4(c.ColorR, c.ColorG, c.ColorB, c.ColorA),
                    Notes = c.Notes,
                    CreatedAt = c.CreatedAt
                });
            }
        }

        Intersections.Clear();
        if (dto.Intersections != null)
        {
            foreach (var i in dto.Intersections)
            {
                Intersections.Add(new ProfileIntersection
                {
                    ID = i.ID,
                    Profile1ID = i.Profile1ID,
                    Profile2ID = i.Profile2ID,
                    IntersectionPoint = new Vector2(i.IntersectionPointX, i.IntersectionPointY),
                    DistanceAlongProfile1 = i.DistanceAlongProfile1,
                    DistanceAlongProfile2 = i.DistanceAlongProfile2,
                    IntersectionAngle = i.IntersectionAngle
                });
            }
        }

        Horizons.Clear();
        if (dto.Horizons != null)
        {
            foreach (var h in dto.Horizons)
            {
                var horizon = new InterpolatedHorizon
                {
                    ID = h.ID,
                    Name = h.Name,
                    LithologyType = h.LithologyType,
                    Color = new Vector4(h.ColorR, h.ColorG, h.ColorB, h.ColorA),
                    ControlPoints = h.ControlPoints?.Select(cp => new HorizonControlPoint
                    {
                        BoreholeID = cp.BoreholeID,
                        LithologyID = cp.LithologyID,
                        ProfileID = cp.ProfileID,
                        Position = new Vector3(cp.PositionX, cp.PositionY, cp.PositionZ),
                        Confidence = cp.Confidence
                    }).ToList() ?? new List<HorizonControlPoint>(),
                    Triangles = h.Triangles?.Select(t => (t[0], t[1], t[2])).ToList() ?? new List<(int, int, int)>(),
                    GridBounds = h.GridBounds != null ? new BoundingBox
                    {
                        Min = h.GridBounds.Min,
                        Max = h.GridBounds.Max
                    } : null,
                    GridResolutionX = h.GridResolutionX,
                    GridResolutionY = h.GridResolutionY
                };
                Horizons.Add(horizon);
            }
        }
    }

    #endregion
}

#region Display Settings

/// <summary>
/// Display settings for profile correlation viewer
/// </summary>
public class ProfileCorrelationDisplaySettings
{
    public float ColumnWidth { get; set; } = 120f;
    public float ColumnSpacing { get; set; } = 60f;
    public float DepthScale { get; set; } = 3f;
    public float HeaderHeight { get; set; } = 100f;
    public bool ShowCorrelationLines { get; set; } = true;
    public bool ShowLithologyNames { get; set; } = true;
    public bool ShowDepthScale { get; set; } = true;
    public bool ShowCoordinates { get; set; } = true;
    public bool ShowProfileLines { get; set; } = true;
    public bool ShowIntersections { get; set; } = true;
    public float LineThickness { get; set; } = 2f;
    public float CrossProfileLineThickness { get; set; } = 3f;
}

/// <summary>
/// Settings for surface interpolation
/// </summary>
public class SurfaceInterpolationSettings
{
    public InterpolationMethod Method { get; set; } = InterpolationMethod.InverseDistanceWeighted;
    public float IDWPower { get; set; } = 2.0f;
    public float InterpolationRadius { get; set; } = 500f;
    public int GridResolution { get; set; } = 50;
    public bool UseConfidenceWeighting { get; set; } = true;
}

#endregion

#region DTOs for Serialization

public class MultiProfileCorrelationDTO : DatasetDTO
{
    public List<CorrelationProfileDTO> Profiles { get; set; }
    public Dictionary<string, BoreholeHeaderDTO> Headers { get; set; }
    public List<LithologyCorrelationDTO> IntraProfileCorrelations { get; set; }
    public List<CrossProfileCorrelationDTO> CrossProfileCorrelations { get; set; }
    public List<ProfileIntersectionDTO> Intersections { get; set; }
    public List<InterpolatedHorizonDTO> Horizons { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
    public ProfileCorrelationDisplaySettings DisplaySettings { get; set; }
    public SurfaceInterpolationSettings InterpolationSettings { get; set; }
}

public class CorrelationProfileDTO
{
    public string ID { get; set; }
    public string Name { get; set; }
    public List<string> BoreholeOrder { get; set; }
    public float StartPointX { get; set; }
    public float StartPointY { get; set; }
    public float EndPointX { get; set; }
    public float EndPointY { get; set; }
    public float Azimuth { get; set; }
    public float ColorR { get; set; }
    public float ColorG { get; set; }
    public float ColorB { get; set; }
    public float ColorA { get; set; }
    public bool IsVisible { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Notes { get; set; }
}

public class CrossProfileCorrelationDTO
{
    public string ID { get; set; }
    public string SourceProfileID { get; set; }
    public string TargetProfileID { get; set; }
    public string SourceLithologyID { get; set; }
    public string SourceBoreholeID { get; set; }
    public string TargetLithologyID { get; set; }
    public string TargetBoreholeID { get; set; }
    public float Confidence { get; set; }
    public bool IsAutoCorrelated { get; set; }
    public string IntersectionID { get; set; }
    public float ColorR { get; set; }
    public float ColorG { get; set; }
    public float ColorB { get; set; }
    public float ColorA { get; set; }
    public string Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProfileIntersectionDTO
{
    public string ID { get; set; }
    public string Profile1ID { get; set; }
    public string Profile2ID { get; set; }
    public float IntersectionPointX { get; set; }
    public float IntersectionPointY { get; set; }
    public float DistanceAlongProfile1 { get; set; }
    public float DistanceAlongProfile2 { get; set; }
    public float IntersectionAngle { get; set; }
}

public class InterpolatedHorizonDTO
{
    public string ID { get; set; }
    public string Name { get; set; }
    public string LithologyType { get; set; }
    public float ColorR { get; set; }
    public float ColorG { get; set; }
    public float ColorB { get; set; }
    public float ColorA { get; set; }
    public List<HorizonControlPointDTO> ControlPoints { get; set; }
    public List<int[]> Triangles { get; set; }
    public BoundingBoxDTO GridBounds { get; set; }
    public int GridResolutionX { get; set; }
    public int GridResolutionY { get; set; }
}

public class HorizonControlPointDTO
{
    public string BoreholeID { get; set; }
    public string LithologyID { get; set; }
    public string ProfileID { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float Confidence { get; set; }
}

#endregion
