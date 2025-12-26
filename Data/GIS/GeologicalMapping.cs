// GeoscientistToolkit/Business/GIS/GeologicalMapping.cs

using System.Numerics;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
/// Comprehensive geological mapping system with structural geology features
/// </summary>
public static class GeologicalMapping
{
    #region Geological Feature Types

    public enum GeologicalFeatureType
    {
        // Lithology/Formations
        Formation,
        Member,
        Bed,
        Unconformity,
        Intrusion,
        
        // Structural Features
        Fault_Normal,
        Fault_Reverse,
        Fault_Transform,
        Fault_Thrust,
        Fault_Detachment,
        Fault_Undefined,
        Fault_Strike_Slip,
        
        // Folds
        Anticline,
        Syncline,
        Monocline,
        Dome,
        Basin,
        
        // Planar Features
        Bedding,
        Foliation,
        Cleavage,
        Joint,
        Vein,
        Dike,
        Sill,
        
        // Linear Features
        Lineation,
        StretchingLineation,
        MineralLineation,
        IntersectionLineation,
        
        // Point Features
        StrikeDip,
        FoldAxis,
        Sample,
        Outcrop,
        Well,
        Mine,
        Borehole
    }
    public enum FoldStyle
    {
        Concentric,      // Concentric folds (parallel folds)
        Similar,         // Similar folds (convergent) 
        Chevron,         // Chevron folds (sharp angular)
        Kink,            // Kink bands
        Ptygmatic,       // Ptygmatic folds (chaotic)
        Recumbent,       // Recumbent folds (overturned)
        Isoclinal,       // Isoclinal folds (tight, parallel limbs)
        Box,             // Box folds
        Disharmonic,     // Disharmonic folds
        Parallel,    // Parallel Fold
        Tight,           // Tight folds
        Open,            // Open folds
        Gentle,          // Gentle folds
        Closed,           // Closed folds
        Anticline,
        Syncline
    }

    public class GeologicalFeature : GISFeature
    {
        public GeologicalFeatureType GeologicalType { get; set; }
        public float? Strike { get; set; } // 0-360 degrees
        public float? Dip { get; set; } // 0-90 degrees
        public string DipDirection { get; set; } // N, S, E, W, NE, SE, SW, NW
        public float? Plunge { get; set; } // For linear features
        public float? Trend { get; set; } // For linear features
        public string FormationName { get; set; }
        public string BoreholeName { get; set; }
        public string LithologyCode { get; set; }
        public string AgeCode { get; set; }
        public string Description { get; set; }
        public float? Thickness { get; set; } // In meters
        public float? Displacement { get; set; } // For faults, in meters
        public string MovementSense { get; set; } // For faults: Normal, Reverse, Dextral, Sinistral
        public bool IsInferred { get; set; }
        public bool IsCovered { get; set; }
        
        public GeologicalFeature() : base()
        {
            Properties["geological_type"] = GeologicalType.ToString();
        }
        
        public void UpdateProperties()
        {
            Properties["geological_type"] = GeologicalType.ToString();
            if (Strike.HasValue) Properties["strike"] = Strike.Value;
            if (Dip.HasValue) Properties["dip"] = Dip.Value;
            if (!string.IsNullOrEmpty(DipDirection)) Properties["dip_direction"] = DipDirection;
            if (Plunge.HasValue) Properties["plunge"] = Plunge.Value;
            if (Trend.HasValue) Properties["trend"] = Trend.Value;
            if (!string.IsNullOrEmpty(FormationName)) Properties["formation"] = FormationName;
            if (!string.IsNullOrEmpty(BoreholeName)) Properties["borehole_name"] = BoreholeName;
            if (!string.IsNullOrEmpty(LithologyCode)) Properties["lithology"] = LithologyCode;
            if (!string.IsNullOrEmpty(AgeCode)) Properties["age_code"] = AgeCode;
            if (!string.IsNullOrEmpty(Description)) Properties["description"] = Description;
            if (Thickness.HasValue) Properties["thickness"] = Thickness.Value;
            if (Displacement.HasValue) Properties["displacement"] = Displacement.Value;
            if (!string.IsNullOrEmpty(MovementSense)) Properties["movement"] = MovementSense;
            Properties["inferred"] = IsInferred;
            Properties["covered"] = IsCovered;
        }
    }

    #endregion

    #region Geological Symbology

    public static class GeologicalSymbols
    {
        /// <summary>
        /// Generate strike and dip symbol as line segments
        /// </summary>
        public static List<Vector2[]> GenerateStrikeDipSymbol(Vector2 position, float strike, float dip, float scale = 20f)
        {
            var symbols = new List<Vector2[]>();
            
            // Convert strike to radians (geological convention: measured from North)
            var strikeRad = strike * MathF.PI / 180f;
            
            // Strike line (perpendicular to dip direction)
            var strikeDir = new Vector2(MathF.Sin(strikeRad), -MathF.Cos(strikeRad));
            var strikeStart = position - strikeDir * scale * 0.5f;
            var strikeEnd = position + strikeDir * scale * 0.5f;
            symbols.Add(new[] { strikeStart, strikeEnd });
            
            // Dip tick (perpendicular to strike, pointing down-dip)
            var dipDir = new Vector2(MathF.Cos(strikeRad), MathF.Sin(strikeRad));
            var dipEnd = position + dipDir * scale * 0.3f;
            symbols.Add(new[] { position, dipEnd });
            
            return symbols;
        }
        
        /// <summary>
        /// Generate fault symbol with appropriate decoration
        /// </summary>
        public static List<Vector2[]> GenerateFaultSymbol(Vector2[] faultLine, GeologicalFeatureType faultType, 
            float scale = 10f, string movementSense = null)
        {
            var symbols = new List<Vector2[]>();
            symbols.Add(faultLine); // Main fault trace
            
            // Add decorations based on fault type
            var spacing = scale * 3f; // Space between symbols
            var currentDist = 0f;
            
            for (int i = 0; i < faultLine.Length - 1; i++)
            {
                var segment = faultLine[i + 1] - faultLine[i];
                var segmentLength = segment.Length();
                var segmentDir = Vector2.Normalize(segment);
                var normal = new Vector2(-segmentDir.Y, segmentDir.X);
                
                while (currentDist < segmentLength)
                {
                    var pos = faultLine[i] + segmentDir * currentDist;
                    
                    switch (faultType)
                    {
                        case GeologicalFeatureType.Fault_Normal:
                            // Ball and bar symbol on downthrown side
                            var ballPos = pos + normal * scale * 0.3f;
                            symbols.Add(GenerateCircle(ballPos, scale * 0.15f, 8));
                            symbols.Add(new[] { pos, ballPos });
                            break;
                            
                        case GeologicalFeatureType.Fault_Reverse:
                        case GeologicalFeatureType.Fault_Thrust:
                            // Triangle teeth on upthrown side
                            var tooth1 = pos + normal * scale * 0.4f;
                            var tooth2 = pos + segmentDir * scale * 0.2f;
                            var tooth3 = pos - segmentDir * scale * 0.2f;
                            symbols.Add(new[] { tooth1, tooth2, pos, tooth3, tooth1 });
                            break;
                            
                        case GeologicalFeatureType.Fault_Transform:
                            // Arrows showing lateral movement
                            if (!string.IsNullOrEmpty(movementSense))
                            {
                                var arrowDir = movementSense == "Dextral" ? segmentDir : -segmentDir;
                                var arrowBase = pos + normal * scale * 0.2f;
                                var arrowTip = arrowBase + arrowDir * scale * 0.4f;
                                symbols.Add(new[] { arrowBase, arrowTip });
                                // Arrowhead
                                var head1 = arrowTip - arrowDir * scale * 0.15f + normal * scale * 0.1f;
                                var head2 = arrowTip - arrowDir * scale * 0.15f - normal * scale * 0.1f;
                                symbols.Add(new[] { head1, arrowTip, head2 });
                            }
                            break;
                    }
                    
                    currentDist += spacing;
                }
                currentDist -= segmentLength;
            }
            
            return symbols;
        }
        
        /// <summary>
        /// Generate fold axis symbol
        /// </summary>
        public static List<Vector2[]> GenerateFoldSymbol(Vector2[] axisLine, GeologicalFeatureType foldType, 
            float scale = 15f, float? plunge = null)
        {
            var symbols = new List<Vector2[]>();
            symbols.Add(axisLine); // Main axis trace
            
            // Add arrows based on fold type
            var spacing = scale * 4f;
            var currentDist = spacing * 0.5f; // Start offset
            
            for (int i = 0; i < axisLine.Length - 1; i++)
            {
                var segment = axisLine[i + 1] - axisLine[i];
                var segmentLength = segment.Length();
                var segmentDir = Vector2.Normalize(segment);
                var normal = new Vector2(-segmentDir.Y, segmentDir.X);
                
                while (currentDist < segmentLength)
                {
                    var pos = axisLine[i] + segmentDir * currentDist;
                    
                    if (foldType == GeologicalFeatureType.Anticline)
                    {
                        // Arrows pointing away from axis
                        var arrow1 = pos + normal * scale * 0.5f;
                        var arrow2 = pos - normal * scale * 0.5f;
                        symbols.Add(new[] { pos, arrow1 });
                        symbols.Add(new[] { pos, arrow2 });
                        // Add arrowheads
                        AddArrowhead(symbols, pos, arrow1, scale * 0.2f);
                        AddArrowhead(symbols, pos, arrow2, scale * 0.2f);
                    }
                    else if (foldType == GeologicalFeatureType.Syncline)
                    {
                        // Arrows pointing toward axis
                        var arrow1 = pos + normal * scale * 0.5f;
                        var arrow2 = pos - normal * scale * 0.5f;
                        symbols.Add(new[] { arrow1, pos });
                        symbols.Add(new[] { arrow2, pos });
                        // Add arrowheads
                        AddArrowhead(symbols, arrow1, pos, scale * 0.2f);
                        AddArrowhead(symbols, arrow2, pos, scale * 0.2f);
                    }
                    
                    currentDist += spacing;
                }
                currentDist -= segmentLength;
            }
            
            // Add plunge arrow if specified
            if (plunge.HasValue && axisLine.Length >= 2)
            {
                var endSegment = axisLine[^1] - axisLine[^2];
                var plungeDir = Vector2.Normalize(endSegment);
                var plungePos = axisLine[^1] + plungeDir * scale * 0.3f;
                symbols.Add(new[] { axisLine[^1], plungePos });
                AddArrowhead(symbols, axisLine[^1], plungePos, scale * 0.25f);
            }
            
            return symbols;
        }
        
        /// <summary>
        /// Generate bedding symbol
        /// </summary>
        public static List<Vector2[]> GenerateBeddingSymbol(Vector2 position, float strike, float dip, 
            bool isOverturned = false, float scale = 20f)
        {
            var symbols = new List<Vector2[]>();
            
            var strikeRad = strike * MathF.PI / 180f;
            var strikeDir = new Vector2(MathF.Sin(strikeRad), -MathF.Cos(strikeRad));
            
            // Main strike line
            var strikeStart = position - strikeDir * scale * 0.5f;
            var strikeEnd = position + strikeDir * scale * 0.5f;
            symbols.Add(new[] { strikeStart, strikeEnd });
            
            // Dip tick
            var dipDir = new Vector2(MathF.Cos(strikeRad), MathF.Sin(strikeRad));
            var dipEnd = position + dipDir * scale * 0.3f;
            
            if (isOverturned)
            {
                // Add circle for overturned beds
                symbols.Add(GenerateCircle(dipEnd, scale * 0.1f, 8));
            }
            
            symbols.Add(new[] { position, dipEnd });
            
            // Add small perpendicular tick for horizontal beds (dip = 0)
            if (Math.Abs(dip) < 1f)
            {
                var tick1 = position + strikeDir * scale * 0.2f;
                var tick2 = position - strikeDir * scale * 0.2f;
                symbols.Add(new[] { tick1 + dipDir * scale * 0.1f, tick1 - dipDir * scale * 0.1f });
                symbols.Add(new[] { tick2 + dipDir * scale * 0.1f, tick2 - dipDir * scale * 0.1f });
            }
            
            return symbols;
        }
        
        /// <summary>
        /// Generate borehole symbol
        /// </summary>
        public static List<Vector2[]> GenerateBoreholeSymbol(Vector2 position, float scale = 15f)
        {
            var symbols = new List<Vector2[]>();
            var radius = scale * 0.4f;

            // Circle
            symbols.Add(GenerateCircle(position, radius, 16));

            // Plus sign
            var p1 = position + new Vector2(0, -radius);
            var p2 = position + new Vector2(0, radius);
            symbols.Add(new[] { p1, p2 });

            var p3 = position + new Vector2(-radius, 0);
            var p4 = position + new Vector2(radius, 0);
            symbols.Add(new[] { p3, p4 });

            return symbols;
        }
        
        private static void AddArrowhead(List<Vector2[]> symbols, Vector2 from, Vector2 to, float size)
        {
            var dir = Vector2.Normalize(to - from);
            var perp = new Vector2(-dir.Y, dir.X);
            var head1 = to - dir * size + perp * size * 0.5f;
            var head2 = to - dir * size - perp * size * 0.5f;
            symbols.Add(new[] { head1, to, head2 });
        }
        
        private static Vector2[] GenerateCircle(Vector2 center, float radius, int segments)
        {
            var points = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                var angle = i * 2f * MathF.PI / segments;
                points[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            }
            return points;
        }
    }

    #endregion

    #region Lithology Patterns and Colors

    public static class LithologyPatterns
    {
        public static Dictionary<string, Vector4> StandardColors = new()
        {
            // Igneous
            ["Granite"] = new Vector4(0.9f, 0.6f, 0.6f, 1f),
            ["Basalt"] = new Vector4(0.3f, 0.3f, 0.3f, 1f),
            ["Gabbro"] = new Vector4(0.2f, 0.4f, 0.2f, 1f),
            ["Diorite"] = new Vector4(0.5f, 0.5f, 0.5f, 1f),
            ["Rhyolite"] = new Vector4(0.8f, 0.7f, 0.7f, 1f),
            ["Andesite"] = new Vector4(0.6f, 0.5f, 0.5f, 1f),
            
            // Sedimentary
            ["Sandstone"] = new Vector4(0.9f, 0.8f, 0.5f, 1f),
            ["Limestone"] = new Vector4(0.6f, 0.8f, 0.9f, 1f),
            ["Shale"] = new Vector4(0.7f, 0.7f, 0.6f, 1f),
            ["Conglomerate"] = new Vector4(0.8f, 0.6f, 0.4f, 1f),
            ["Dolomite"] = new Vector4(0.7f, 0.8f, 0.9f, 1f),
            ["Siltstone"] = new Vector4(0.8f, 0.7f, 0.6f, 1f),
            ["Mudstone"] = new Vector4(0.6f, 0.5f, 0.4f, 1f),
            ["Coal"] = new Vector4(0.1f, 0.1f, 0.1f, 1f),
            ["Chalk"] = new Vector4(0.95f, 0.95f, 0.95f, 1f),
            
            // Metamorphic
            ["Gneiss"] = new Vector4(0.7f, 0.6f, 0.7f, 1f),
            ["Schist"] = new Vector4(0.6f, 0.7f, 0.6f, 1f),
            ["Marble"] = new Vector4(0.9f, 0.9f, 0.95f, 1f),
            ["Slate"] = new Vector4(0.5f, 0.5f, 0.6f, 1f),
            ["Quartzite"] = new Vector4(0.95f, 0.9f, 0.8f, 1f),
            ["Phyllite"] = new Vector4(0.6f, 0.6f, 0.7f, 1f),
            
            // Unconsolidated
            ["Alluvium"] = new Vector4(0.95f, 0.9f, 0.7f, 1f),
            ["Colluvium"] = new Vector4(0.85f, 0.8f, 0.6f, 1f),
            ["Till"] = new Vector4(0.7f, 0.65f, 0.5f, 1f),
            ["Loess"] = new Vector4(0.9f, 0.85f, 0.6f, 1f)
        };
        
    }

    #endregion

    #region Profile Generation

    /// <summary>
    /// Generate a detailed topographic profile along a line
    /// </summary>
    public static class ProfileGenerator
    {
        public class ProfilePoint
        {
            public Vector2 Position { get; set; } // Original map position
            public float Distance { get; set; } // Distance along profile line
            public float Elevation { get; set; } // Elevation at this point
            public List<GeologicalFeature> Features { get; set; } = new(); // Geological features at this point
        }
        
        public class TopographicProfile
        {
            public Vector2 StartPoint { get; set; }
            public Vector2 EndPoint { get; set; }
            public float TotalDistance { get; set; }
            public List<ProfilePoint> Points { get; set; } = new();
            public float MinElevation { get; set; }
            public float MaxElevation { get; set; }
            public float VerticalExaggeration { get; set; } = 1.0f;
            public string Name { get; set; }
            public DateTime CreatedAt { get; set; }
        }
        
        /// <summary>
        /// Generate a topographic profile from DEM data
        /// </summary>
        public static TopographicProfile GenerateProfile(
            float[,] demData, 
            BoundingBox demBounds,
            Vector2 startPoint, 
            Vector2 endPoint, 
            int numSamples = 100,
            List<GeologicalFeature> geologicalFeatures = null)
        {
            var profile = new TopographicProfile
            {
                StartPoint = startPoint,
                EndPoint = endPoint,
                CreatedAt = DateTime.Now,
                Name = $"Profile {DateTime.Now:yyyy-MM-dd HH:mm}"
            };
            
            // Calculate total distance
            var dx = endPoint.X - startPoint.X;
            var dy = endPoint.Y - startPoint.Y;
            profile.TotalDistance = MathF.Sqrt(dx * dx + dy * dy);
            
            // Generate sample points along the line
            float minElev = float.MaxValue;
            float maxElev = float.MinValue;
            
            for (int i = 0; i < numSamples; i++)
            {
                float t = i / (float)(numSamples - 1);
                var samplePos = Vector2.Lerp(startPoint, endPoint, t);
                
                // Sample elevation from DEM
                float elevation = SampleDEM(demData, demBounds, samplePos);
                
                var point = new ProfilePoint
                {
                    Position = samplePos,
                    Distance = t * profile.TotalDistance,
                    Elevation = elevation
                };
                
                // Find geological features that intersect this point
                if (geologicalFeatures != null)
                {
                    point.Features = FindIntersectingFeatures(samplePos, geologicalFeatures, 10f); // 10m tolerance
                }
                
                profile.Points.Add(point);
                
                if (elevation < minElev) minElev = elevation;
                if (elevation > maxElev) maxElev = elevation;
            }
            
            profile.MinElevation = minElev;
            profile.MaxElevation = maxElev;
            
            return profile;
        }
        
        /// <summary>
        /// Sample elevation from DEM at a specific coordinate
        /// </summary>
        private static float SampleDEM(float[,] demData, BoundingBox bounds, Vector2 position)
        {
            int width = demData.GetLength(0);
            int height = demData.GetLength(1);
            
            // Convert world coordinates to pixel coordinates
            float nx = (position.X - bounds.Min.X) / (bounds.Max.X - bounds.Min.X);
            float ny = (position.Y - bounds.Min.Y) / (bounds.Max.Y - bounds.Min.Y);
            
            // Clamp to valid range
            nx = Math.Clamp(nx, 0, 1);
            ny = Math.Clamp(ny, 0, 1);
            
            // Convert to pixel indices
            float fx = nx * (width - 1);
            float fy = ny * (height - 1);
            
            int x0 = (int)Math.Floor(fx);
            int y0 = (int)Math.Floor(fy);
            int x1 = Math.Min(x0 + 1, width - 1);
            int y1 = Math.Min(y0 + 1, height - 1);
            
            // Bilinear interpolation
            float wx = fx - x0;
            float wy = fy - y0;
            
            float v00 = demData[x0, y0];
            float v10 = demData[x1, y0];
            float v01 = demData[x0, y1];
            float v11 = demData[x1, y1];
            
            float v0 = v00 * (1 - wx) + v10 * wx;
            float v1 = v01 * (1 - wx) + v11 * wx;
            
            return v0 * (1 - wy) + v1 * wy;
        }
        
        /// <summary>
        /// Find geological features that intersect a point
        /// </summary>
        private static List<GeologicalFeature> FindIntersectingFeatures(
            Vector2 point, 
            List<GeologicalFeature> features, 
            float tolerance)
        {
            var intersecting = new List<GeologicalFeature>();
            
            foreach (var feature in features)
            {
                bool intersects = false;
                
                switch (feature.Type)
                {
                    case FeatureType.Point:
                        if (feature.Coordinates.Count > 0)
                        {
                            var dist = Vector2.Distance(point, feature.Coordinates[0]);
                            if (dist <= tolerance)
                                intersects = true;
                        }
                        break;
                        
                    case FeatureType.Line:
                        // Check distance to line segments
                        for (int i = 0; i < feature.Coordinates.Count - 1; i++)
                        {
                            var dist = DistanceToLineSegment(point, feature.Coordinates[i], feature.Coordinates[i + 1]);
                            if (dist <= tolerance)
                            {
                                intersects = true;
                                break;
                            }
                        }
                        break;
                        
                    case FeatureType.Polygon:
                        // Point in polygon test
                        if (IsPointInPolygon(point, feature.Coordinates))
                            intersects = true;
                        break;
                }
                
                if (intersects)
                    intersecting.Add(feature);
            }
            
            return intersecting;
        }
        
        public static float DistanceToLineSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var ap = point - a;
            var abLengthSq = ab.LengthSquared();
            
            if (abLengthSq == 0)
                return Vector2.Distance(point, a);
            
            var t = Math.Clamp(Vector2.Dot(ap, ab) / abLengthSq, 0, 1);
            var projection = a + ab * t;
            return Vector2.Distance(point, projection);
        }
        
        public static bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            int count = polygon.Count;
            bool inside = false;
            
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                    point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / 
                    (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                {
                    inside = !inside;
                }
            }
            
            return inside;
        }
    }

    #endregion

    #region Cross Section Generation

    /// <summary>
    /// Generate geological cross-sections with projected features
    /// </summary>
    public static class CrossSectionGenerator
    {
        public class CrossSection
        {
            public ProfileGenerator.TopographicProfile Profile { get; set; }
            public List<ProjectedFormation> Formations { get; set; } = new();
            public List<ProjectedFault> Faults { get; set; } = new();
            public float VerticalExaggeration { get; set; } = 2.0f;
        }
        
        public class ProjectedFormation
        {
            public string Name { get; set; }
            public string LithologyType { get; set; }
            public Vector4 Color { get; set; }
            public List<Vector2> TopBoundary { get; set; } = new();
            public List<Vector2> BottomBoundary { get; set; } = new();
            public FoldStyle? FoldStyle { get; set; }  
        }
        public class ProjectedFault
        {
            public GeologicalFeatureType Type { get; set; }
            public List<Vector2> FaultTrace { get; set; } = new();
            public float Dip { get; set; }
            public string DipDirection { get; set; }
            public float? Displacement { get; set; } 
        }
        
        /// <summary>
        /// Project geological features onto a cross-section
        /// </summary>
        public static CrossSection GenerateCrossSection(
            ProfileGenerator.TopographicProfile profile,
            List<GeologicalFeature> formations,
            List<GeologicalFeature> faults,
            float defaultThickness = 100f)
        {
            var section = new CrossSection
            {
                Profile = profile
            };
            
            // Project formations
            foreach (var formation in formations.Where(f => f.GeologicalType == GeologicalFeatureType.Formation))
            {
                var projected = ProjectFormation(formation, profile, defaultThickness);
                if (projected != null)
                    section.Formations.Add(projected);
            }
            
            // Project faults
            foreach (var fault in faults.Where(f => IsFaultType(f.GeologicalType)))
            {
                var projected = ProjectFault(fault, profile);
                if (projected != null)
                    section.Faults.Add(projected);
            }
            
            return section;
        }
        
        public static bool IsFaultType(GeologicalFeatureType type)
        {
            return type == GeologicalFeatureType.Fault_Normal ||
                   type == GeologicalFeatureType.Fault_Reverse ||
                   type == GeologicalFeatureType.Fault_Transform ||
                   type == GeologicalFeatureType.Fault_Thrust ||
                   type == GeologicalFeatureType.Fault_Detachment ||
                   type == GeologicalFeatureType.Fault_Undefined;
        }

        private static ProjectedFormation ProjectFormation(GeologicalFeature formation, ProfileGenerator.TopographicProfile profile, float defaultThickness)
        {
            if (formation.Coordinates.Count < 3) return null;

            var projected = new ProjectedFormation
            {
                Name = formation.FormationName ?? "Unnamed Formation",
                LithologyType = formation.LithologyCode ?? formation.FormationName ?? "Unknown",
                Color = LithologyPatterns.StandardColors.GetValueOrDefault(formation.LithologyCode ?? "", new Vector4(0.5f, 0.5f, 0.5f, 1f))
            };

            var formationThickness = formation.Thickness ?? defaultThickness;

            // Find where the profile intersects the formation polygon
            for (int i = 0; i < profile.Points.Count; i++)
            {
                var point = profile.Points[i];
                var pointInPolygon = ProfileGenerator.IsPointInPolygon(point.Position, formation.Coordinates);
                
                if (pointInPolygon)
                {
                    projected.TopBoundary.Add(new Vector2(point.Distance, point.Elevation));
                    // For simplicity, we assume constant thickness vertically
                    projected.BottomBoundary.Add(new Vector2(point.Distance, point.Elevation - formationThickness));
                }
            }

            if (projected.TopBoundary.Count > 1)
                return projected;

            return null;
        }
        
        private static ProjectedFault ProjectFault(GeologicalFeature fault, ProfileGenerator.TopographicProfile profile)
        {
            if (fault.Coordinates.Count < 2) return null;

            // Find intersection of fault line with profile line (simplified to closest point)
            Vector2 intersectionPoint = Vector2.Zero;
            float minDistance = float.MaxValue;
            int profileIndex = -1;

            for (int i = 0; i < profile.Points.Count; i++)
            {
                for (int j = 0; j < fault.Coordinates.Count - 1; j++)
                {
                    var dist = ProfileGenerator.DistanceToLineSegment(profile.Points[i].Position, fault.Coordinates[j], fault.Coordinates[j+1]);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        intersectionPoint = profile.Points[i].Position;
                        profileIndex = i;
                    }
                }
            }
            
            // If fault is close enough to the profile line
            if (minDistance < 50f && profileIndex != -1) // 50 units tolerance
            {
                var startPoint = profile.Points[profileIndex];
                var dip = fault.Dip ?? 45f; // Default dip of 45 degrees if not specified
                var dipRad = dip * MathF.PI / 180f;

                // Project fault into the subsurface from the intersection point
                var faultTrace = new List<Vector2>();
                var start = new Vector2(startPoint.Distance, startPoint.Elevation);
                faultTrace.Add(start);
                
                // End point at the bottom of the section (arbitrary depth)
                float sectionDepth = profile.MaxElevation - profile.MinElevation + 1000f; 
                var end = new Vector2(
                    start.X + (sectionDepth / MathF.Tan(dipRad)), // Horizontal component
                    start.Y - sectionDepth                         // Vertical component
                );
                faultTrace.Add(end);
                
                return new ProjectedFault
                {
                    Type = fault.GeologicalType,
                    Dip = dip,
                    DipDirection = fault.DipDirection,
                    FaultTrace = faultTrace
                };
            }

            return null;
        }
    }
    #endregion
}
