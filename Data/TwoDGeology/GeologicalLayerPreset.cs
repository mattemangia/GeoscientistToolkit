// GeoscientistToolkit/Business/GIS/GeologicalLayerPresets.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
/// Provides complete geological cross-section presets with multiple layers
/// FIXED VERSION: Proper fault displacement applied to formations
/// </summary>
public static class GeologicalLayerPresets
{
    public enum PresetScenario
    {
        SimpleLayers,              
        ErodedAnticline,           
        ErodedSyncline,            
        JuraMountains,             
        FaultedLayers,             
        ThrustFault,               
        FoldedSequence,            
        UnconformitySequence,      
        BasinFilling,              
        ChannelFill                
    }
    
    public static CrossSection CreatePreset(PresetScenario scenario, float totalDistance = 10000f, float baseElevation = -2000f)
    {
        var section = scenario switch
        {
            PresetScenario.SimpleLayers => CreateSimpleLayers(totalDistance, baseElevation),
            PresetScenario.ErodedAnticline => CreateErodedAnticline(totalDistance, baseElevation),
            PresetScenario.ErodedSyncline => CreateErodedSyncline(totalDistance, baseElevation),
            PresetScenario.JuraMountains => CreateJuraMountains(totalDistance, baseElevation),
            PresetScenario.FaultedLayers => CreateFaultedLayers(totalDistance, baseElevation),
            PresetScenario.ThrustFault => CreateThrustFault(totalDistance, baseElevation),
            PresetScenario.FoldedSequence => CreateFoldedSequence(totalDistance, baseElevation),
            PresetScenario.UnconformitySequence => CreateUnconformitySequence(totalDistance, baseElevation),
            PresetScenario.BasinFilling => CreateBasinFilling(totalDistance, baseElevation),
            PresetScenario.ChannelFill => CreateChannelFill(totalDistance, baseElevation),
            _ => CreateSimpleLayers(totalDistance, baseElevation)
        };
        
        // CRITICAL: Validate and fix geology
        ValidateAndFixGeology(section);
        
        return section;
    }
    
    private static void ValidateAndFixGeology(CrossSection section)
    {
        if (section.Profile == null || section.Formations.Count == 0)
            return;
        
        // Fix all formations to properly contact topography without gaps
        ClipAllFormationsToTopography(section);
        
        // Ensure proper layer continuity when dipping
        EnsureLayerContinuity(section);
        
        // Fix overlaps while maintaining contact
        FixOverlappingFormations(section.Formations);
    }
    
    /// <summary>
    /// Apply fault displacement to formations - CRITICAL for restoration to work
    /// </summary>
    private static void ApplyFaultDisplacement(CrossSection section, ProjectedFault fault, float displacement)
    {
        if (fault.FaultTrace.Count < 2) return;
        
        // Calculate displacement vector based on fault type and dip
        var dipRad = fault.Dip * MathF.PI / 180f;
        Vector2 displacementVector;
        
        switch (fault.Type)
        {
            case GeologicalFeatureType.Fault_Normal:
                // Normal fault - hanging wall moves down
                displacementVector = new Vector2(
                    displacement * MathF.Cos(dipRad) * 0.3f, // Small horizontal component
                    -displacement * MathF.Sin(dipRad)); // Downward vertical component
                break;
                
            case GeologicalFeatureType.Fault_Reverse:
            case GeologicalFeatureType.Fault_Thrust:
                // Thrust/reverse fault - hanging wall moves up and over
                displacementVector = new Vector2(
                    displacement * MathF.Cos(dipRad), // Horizontal component (overthrust)
                    displacement * MathF.Sin(dipRad)); // Upward vertical component
                break;
                
            default:
                displacementVector = new Vector2(0, -displacement);
                break;
        }
        
        // Apply displacement to all formations
        foreach (var formation in section.Formations)
        {
            if (formation.Name == "Crystalline Basement")
                continue; // Don't displace basement
                
            // Displace top boundary
            for (int i = 0; i < formation.TopBoundary.Count; i++)
            {
                if (IsInHangingWall(formation.TopBoundary[i], fault))
                {
                    formation.TopBoundary[i] += displacementVector;
                }
            }
            
            // Displace bottom boundary
            for (int i = 0; i < formation.BottomBoundary.Count; i++)
            {
                if (IsInHangingWall(formation.BottomBoundary[i], fault))
                {
                    formation.BottomBoundary[i] += displacementVector;
                }
            }
        }
    }
    
    /// <summary>
    /// Determine if a point is in the hanging wall of a fault
    /// </summary>
    private static bool IsInHangingWall(Vector2 point, ProjectedFault fault)
    {
        if (fault.FaultTrace.Count < 2) return false;
        
        // Find closest segment on fault trace
        float minDist = float.MaxValue;
        int closestSegment = 0;
        
        for (int i = 0; i < fault.FaultTrace.Count - 1; i++)
        {
            var dist = DistanceToLineSegment(point, fault.FaultTrace[i], fault.FaultTrace[i + 1]);
            if (dist < minDist)
            {
                minDist = dist;
                closestSegment = i;
            }
        }
        
        var p1 = fault.FaultTrace[closestSegment];
        var p2 = fault.FaultTrace[closestSegment + 1];
        
        // Use cross product to determine which side of fault
        var cross = (p2.X - p1.X) * (point.Y - p1.Y) - (p2.Y - p1.Y) * (point.X - p1.X);
        
        // For thrust faults dipping to the right (increasing X), hanging wall is on left (negative cross product)
        // For normal faults, hanging wall is typically on the downthrown side
        bool isDippingRight = p2.X > p1.X && p2.Y < p1.Y;
        
        if (fault.Type == GeologicalFeatureType.Fault_Thrust)
        {
            // Thrust faults - hanging wall is above the fault
            return isDippingRight ? cross < 0 : cross > 0;
        }
        else if (fault.Type == GeologicalFeatureType.Fault_Normal)
        {
            // Normal faults - hanging wall is on the downthrown side
            return isDippingRight ? cross > 0 : cross < 0;
        }
        
        return false;
    }
    
    private static float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
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
    /// CRITICAL FIX: Ensure all formations are properly clipped to topography with NO GAPS
    /// </summary>
    private static void ClipAllFormationsToTopography(CrossSection section)
    {
        var topographyPoints = section.Profile.Points.Select(p => new Vector2(p.Distance, p.Elevation)).ToList();
        
        foreach (var formation in section.Formations)
        {
            if (formation.Name == "Crystalline Basement")
                continue; // Don't clip basement
                
            // Clip each point to be AT OR BELOW topography (no gaps!)
            for (int i = 0; i < formation.TopBoundary.Count; i++)
            {
                var point = formation.TopBoundary[i];
                float topoElevation = GetTopographyElevationAt(section.Profile, point.X);
                
                // If point is above topography, bring it DOWN to topography exactly
                if (point.Y > topoElevation)
                {
                    formation.TopBoundary[i] = new Vector2(point.X, topoElevation);
                }
            }
            
            // Ensure bottom boundary doesn't go above top
            for (int i = 0; i < formation.BottomBoundary.Count && i < formation.TopBoundary.Count; i++)
            {
                if (formation.BottomBoundary[i].Y > formation.TopBoundary[i].Y)
                {
                    formation.BottomBoundary[i] = new Vector2(
                        formation.BottomBoundary[i].X,
                        formation.TopBoundary[i].Y - 10f // Minimum thickness
                    );
                }
            }
        }
    }
    
    /// <summary>
    /// FIX: Ensure layer continuity when dipping - new layers appear on top
    /// </summary>
    private static void EnsureLayerContinuity(CrossSection section)
    {
        // When a layer dips below another, ensure the upper layer extends to fill the space
        var sortedFormations = section.Formations
            .Where(f => f.Name != "Crystalline Basement")
            .OrderByDescending(f => f.TopBoundary.Count > 0 ? f.TopBoundary.Average(p => p.Y) : float.MinValue)
            .ToList();
        
        for (int i = 0; i < sortedFormations.Count - 1; i++)
        {
            var upperFormation = sortedFormations[i];
            var lowerFormation = sortedFormations[i + 1];
            
            // Ensure upper formation's bottom follows lower formation's top
            for (int j = 0; j < upperFormation.BottomBoundary.Count && j < lowerFormation.TopBoundary.Count; j++)
            {
                upperFormation.BottomBoundary[j] = new Vector2(
                    upperFormation.BottomBoundary[j].X,
                    lowerFormation.TopBoundary[j].Y
                );
            }
        }
    }
    
    private static float GetTopographyElevationAt(GeologicalMapping.ProfileGenerator.TopographicProfile profile, float x)
    {
        if (profile.Points.Count == 0) return 0f;
        
        // Find bracketing points and interpolate
        for (int i = 0; i < profile.Points.Count - 1; i++)
        {
            var p1 = profile.Points[i];
            var p2 = profile.Points[i + 1];
            
            if (x >= p1.Distance && x <= p2.Distance)
            {
                if (p2.Distance - p1.Distance < 0.001f)
                    return p1.Elevation;
                    
                float t = (x - p1.Distance) / (p2.Distance - p1.Distance);
                return p1.Elevation + t * (p2.Elevation - p1.Elevation);
            }
        }
        
        // Return edge values if outside range
        if (x <= profile.Points[0].Distance)
            return profile.Points[0].Elevation;
        
        return profile.Points[^1].Elevation;
    }
    
    private static void FixOverlappingFormations(List<ProjectedFormation> formations)
    {
        var sorted = formations.Where(f => f.Name != "Crystalline Basement")
            .OrderByDescending(f => f.TopBoundary.Count > 0 ? f.TopBoundary.Average(p => p.Y) : float.MinValue)
            .ToList();
        
        for (int i = 1; i < sorted.Count; i++)
        {
            var currentFormation = sorted[i];
            var formationAbove = sorted[i - 1];
            
            // Ensure current formation's top doesn't go above the formation above's bottom
            for (int j = 0; j < currentFormation.TopBoundary.Count && j < formationAbove.BottomBoundary.Count; j++)
            {
                float maxAllowedElevation = formationAbove.BottomBoundary[j].Y;
                
                if (currentFormation.TopBoundary[j].Y > maxAllowedElevation)
                {
                    currentFormation.TopBoundary[j] = new Vector2(
                        currentFormation.TopBoundary[j].X,
                        maxAllowedElevation
                    );
                }
            }
        }
    }
    
    #region Layer Definitions
    
    private static LayerDefinition[] GetStandardStratigraphicColumn()
    {
        return new[]
        {
            new LayerDefinition { Name = "Quaternary Alluvium", Color = new Vector4(0.9f, 0.85f, 0.6f, 0.8f), Thickness = 50f },
            new LayerDefinition { Name = "Tertiary Molasse", Color = new Vector4(0.85f, 0.75f, 0.5f, 0.8f), Thickness = 200f },
            new LayerDefinition { Name = "Upper Cretaceous Limestone", Color = new Vector4(0.7f, 0.85f, 0.7f, 0.8f), Thickness = 300f },
            new LayerDefinition { Name = "Lower Cretaceous Marl", Color = new Vector4(0.6f, 0.7f, 0.6f, 0.8f), Thickness = 250f },
            new LayerDefinition { Name = "Malm Limestone", Color = new Vector4(0.75f, 0.8f, 0.85f, 0.8f), Thickness = 400f },
            new LayerDefinition { Name = "Dogger Oolite", Color = new Vector4(0.85f, 0.85f, 0.75f, 0.8f), Thickness = 300f },
            new LayerDefinition { Name = "Lias Marl", Color = new Vector4(0.65f, 0.75f, 0.65f, 0.8f), Thickness = 350f },
            new LayerDefinition { Name = "Keuper Evaporites", Color = new Vector4(0.9f, 0.7f, 0.5f, 0.8f), Thickness = 200f },
            new LayerDefinition { Name = "Muschelkalk Limestone", Color = new Vector4(0.7f, 0.75f, 0.8f, 0.8f), Thickness = 250f },
            new LayerDefinition { Name = "Buntsandstein", Color = new Vector4(0.9f, 0.65f, 0.4f, 0.8f), Thickness = 300f },
        };
    }
    
    private struct LayerDefinition
    {
        public string Name;
        public Vector4 Color;
        public float Thickness;
    }
    
    #endregion
    
    #region Preset Implementations

    private static CrossSection CreateJuraMountains(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create Jura-style topography
        var profile = section.Profile;
        for (int i = 0; i < profile.Points.Count; i++)
        {
            float t = i / (float)(profile.Points.Count - 1);
            float x = t * totalDistance;
            
            // Multiple gentle hills
            float hill1 = 150f * MathF.Sin(x / (totalDistance * 0.3f) * MathF.PI);
            float hill2 = 80f * MathF.Sin(x / (totalDistance * 0.15f) * MathF.PI);
            float hill3 = 40f * MathF.Sin(x / (totalDistance * 0.08f) * MathF.PI);
            float elevation = hill1 + hill2 + hill3 + 200f;
            
            profile.Points[i].Elevation = elevation;
            profile.Points[i].Position = new Vector2(x, elevation);
        }
        profile.MinElevation = 0f;
        profile.MaxElevation = 400f;
        
        // CRITICAL: Add crystalline basement with 2° dip (Alpine subduction)
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 2f);
        section.Formations.Add(basement);
        
        var layers = GetStandardStratigraphicColumn().Skip(3).Take(6).ToArray();
        
        // Create initial undeformed layers
        float currentBase = -200f;
        
        foreach (var layer in layers)
        {
            var formation = new ProjectedFormation
            {
                Name = layer.Name,
                Color = layer.Color,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };
            
            // Create initially flat layers
            int numPoints = 100;
            for (int i = 0; i <= numPoints; i++)
            {
                float x = i / (float)numPoints * totalDistance;
                formation.TopBoundary.Add(new Vector2(x, currentBase));
                formation.BottomBoundary.Add(new Vector2(x, currentBase - layer.Thickness));
            }
            
            section.Formations.Add(formation);
            currentBase -= layer.Thickness;
        }
        
        // FIRST: Apply folding to create Jura-style folds
        float[] foldCenters = { totalDistance * 0.2f, totalDistance * 0.5f, totalDistance * 0.8f };
        float[] foldAmplitudes = { 400f, 500f, 350f };
        float foldWidth = totalDistance * 0.25f;
        
        foreach (var formation in section.Formations.Where(f => f.Name != "Crystalline Basement"))
        {
            ApplyMultipleFolds(formation, foldCenters, foldAmplitudes, foldWidth);
        }
        
        // THEN: Create thrust faults and apply their displacement
        var thrust1 = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 30f,
            DipDirection = "Northwest",
            Displacement = 1200f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.15f, 250f),
                new Vector2(totalDistance * 0.35f, -800f)
            }
        };
        section.Faults.Add(thrust1);
        ApplyFaultDisplacement(section, thrust1, thrust1.Displacement.Value);
        
        var thrust2 = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 25f,
            DipDirection = "Northwest",
            Displacement = 1500f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.45f, 300f),
                new Vector2(totalDistance * 0.65f, -900f)
            }
        };
        section.Faults.Add(thrust2);
        ApplyFaultDisplacement(section, thrust2, thrust2.Displacement.Value);
        
        var thrust3 = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 35f,
            DipDirection = "Northwest",
            Displacement = 1000f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.75f, 280f),
                new Vector2(totalDistance * 0.9f, -700f)
            }
        };
        section.Faults.Add(thrust3);
        ApplyFaultDisplacement(section, thrust3, thrust3.Displacement.Value);
        
        // Add back thrust (vergence opposite)
        var backThrust = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 40f,
            DipDirection = "Southeast",
            Displacement = 600f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.6f, 250f),
                new Vector2(totalDistance * 0.48f, -500f)
            }
        };
        section.Faults.Add(backThrust);
        ApplyFaultDisplacement(section, backThrust, backThrust.Displacement.Value);
        
        return section;
    }
    
    private static void ApplyMultipleFolds(ProjectedFormation formation, float[] foldCenters, 
        float[] foldAmplitudes, float foldWidth)
    {
        // Apply multiple fold axes to create complex Jura-style folding
        for (int i = 0; i < formation.TopBoundary.Count; i++)
        {
            var point = formation.TopBoundary[i];
            float totalOffset = 0f;
            
            // Sum contributions from multiple fold axes
            for (int f = 0; f < foldCenters.Length; f++)
            {
                float distFromCenter = Math.Abs(point.X - foldCenters[f]);
                float foldFactor = MathF.Exp(-(distFromCenter * distFromCenter) / (foldWidth * foldWidth * 0.3f));
                totalOffset += foldAmplitudes[f] * foldFactor;
            }
            
            formation.TopBoundary[i] = new Vector2(point.X, point.Y + totalOffset);
        }
        
        // Apply same folding to bottom boundary
        for (int i = 0; i < formation.BottomBoundary.Count; i++)
        {
            var point = formation.BottomBoundary[i];
            float totalOffset = 0f;
            
            for (int f = 0; f < foldCenters.Length; f++)
            {
                float distFromCenter = Math.Abs(point.X - foldCenters[f]);
                float foldFactor = MathF.Exp(-(distFromCenter * distFromCenter) / (foldWidth * foldWidth * 0.3f));
                totalOffset += foldAmplitudes[f] * foldFactor;
            }
            
            formation.BottomBoundary[i] = new Vector2(point.X, point.Y + totalOffset);
        }
    }
    
    private static CrossSection CreateThrustFault(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 100f);
        
        // Add crystalline basement
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 2f);
        section.Formations.Add(basement);
        
        // Create initial flat layers
        var layers = GetStandardStratigraphicColumn().Take(5).ToArray();
        float currentTop = 100f;
        
        foreach (var layer in layers)
        {
            var formation = CreateFlatLayer(layer.Name, layer.Color, totalDistance, currentTop, layer.Thickness);
            section.Formations.Add(formation);
            currentTop -= layer.Thickness;
        }
        
        // Create thrust fault system with proper displacement
        var mainThrust = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 22f,
            DipDirection = "West",
            Displacement = 3000f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.7f, 150f),
                new Vector2(totalDistance * 0.2f, -1200f)
            }
        };
        section.Faults.Add(mainThrust);
        ApplyFaultDisplacement(section, mainThrust, mainThrust.Displacement.Value);
        
        // Imbricate thrust
        var imbricateThrust = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 30f,
            DipDirection = "West",
            Displacement = 800f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.5f, 120f),
                new Vector2(totalDistance * 0.35f, -600f)
            }
        };
        section.Faults.Add(imbricateThrust);
        ApplyFaultDisplacement(section, imbricateThrust, imbricateThrust.Displacement.Value);
        
        // Floor thrust
        var floorThrust = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            Dip = 15f,
            DipDirection = "West",
            Displacement = 5000f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.9f, -500f),
                new Vector2(totalDistance * 0.1f, -1500f)
            }
        };
        section.Faults.Add(floorThrust);
        ApplyFaultDisplacement(section, floorThrust, floorThrust.Displacement.Value);
        
        return section;
    }
    
    private static CrossSection CreateFaultedLayers(float totalDistance, float baseElevation)
    {
        // Start with simple layers
        var section = CreateSimpleLayers(totalDistance, baseElevation);
        
        // Create graben with two normal faults
        float fault1X = totalDistance * 0.35f;
        var fault1 = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Normal,
            Dip = 65f,
            DipDirection = "East",
            Displacement = 400f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(fault1X, section.Profile.MaxElevation),
                new Vector2(fault1X + 600f, baseElevation + 200f)
            }
        };
        section.Faults.Add(fault1);
        ApplyFaultDisplacement(section, fault1, fault1.Displacement.Value);
        
        float fault2X = totalDistance * 0.65f;
        var fault2 = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Normal,
            Dip = 60f,
            DipDirection = "West",
            Displacement = 350f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(fault2X, section.Profile.MaxElevation),
                new Vector2(fault2X - 700f, baseElevation + 200f)
            }
        };
        section.Faults.Add(fault2);
        ApplyFaultDisplacement(section, fault2, fault2.Displacement.Value);
        
        return section;
    }
    
    private static CrossSection CreateErodedAnticline(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create valley topography
        var profile = section.Profile;
        for (int i = 0; i < profile.Points.Count; i++)
        {
            float t = i / (float)(profile.Points.Count - 1);
            float x = t * totalDistance;
            float centerDist = Math.Abs(x - totalDistance / 2f) / (totalDistance / 2f);
            float valleyDepth = 300f * (1f - centerDist * centerDist);
            profile.Points[i].Elevation = -valleyDepth;
            profile.Points[i].Position = new Vector2(x, -valleyDepth);
        }
        profile.MinElevation = -300f;
        profile.MaxElevation = 0f;
        
        // Add crystalline basement with 2° dip
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 2f);
        section.Formations.Add(basement);
        
        var layers = GetStandardStratigraphicColumn().Skip(2).Take(5).ToArray();
        
        float centerX = totalDistance / 2f;
        float foldWidth = totalDistance * 0.7f;
        float amplitude = 600f;
        
        // Create eroded anticline layers
        for (int idx = 0; idx < layers.Length; idx++)
        {
            var layer = layers[idx];
            var formation = CreateErodedAnticlineLayer(layer.Name, layer.Color, totalDistance,
                -500f - idx * layer.Thickness, layer.Thickness, centerX, foldWidth, 
                amplitude * (1f - idx * 0.15f), profile);
            section.Formations.Add(formation);
        }
        
        // Add valley fill that EXACTLY touches topography
        var valleyFill = new ProjectedFormation
        {
            Name = "Quaternary Valley Fill",
            Color = new Vector4(0.9f, 0.85f, 0.6f, 0.8f),
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        for (int i = 0; i < profile.Points.Count; i++)
        {
            float x = profile.Points[i].Distance;
            float topoElev = profile.Points[i].Elevation; // EXACTLY at topography
            float centerDist = Math.Abs(x - totalDistance / 2f) / (totalDistance / 2f);
            float fillDepth = 150f * (1f - centerDist * centerDist);
            
            valleyFill.TopBoundary.Add(new Vector2(x, topoElev));
            valleyFill.BottomBoundary.Add(new Vector2(x, topoElev - fillDepth));
        }
        
        section.Formations.Insert(1, valleyFill);
        
        // Add normal fault cutting through the anticline and apply displacement
        var fault = new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Normal,
            Dip = 70f,
            DipDirection = "West",
            Displacement = 350f,
            FaultTrace = new List<Vector2>
            {
                new Vector2(totalDistance * 0.35f, 50f),
                new Vector2(totalDistance * 0.32f, baseElevation + 200f)
            }
        };
        section.Faults.Add(fault);
        ApplyFaultDisplacement(section, fault, fault.Displacement.Value);
        
        return section;
    }

    // --- FIX START: Implement missing methods ---
    
    private static CrossSection CreateErodedSyncline(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 200f);

        // Create ridge topography
        var profile = section.Profile;
        for (int i = 0; i < profile.Points.Count; i++)
        {
            float x = profile.Points[i].Distance;
            float centerDist = Math.Abs(x - totalDistance / 2f) / (totalDistance / 2f);
            float ridgeHeight = 250f * centerDist * centerDist;
            profile.Points[i].Elevation = ridgeHeight;
            profile.Points[i].Position = new Vector2(x, ridgeHeight);
        }
        profile.MinElevation = 0f;
        profile.MaxElevation = 250f;

        var basement = CreateCrystallineBasement(totalDistance, baseElevation, -1f);
        section.Formations.Add(basement);

        var layers = GetStandardStratigraphicColumn().Take(4).ToArray();
        float currentBase = 0;
        foreach (var layer in layers)
        {
            var formation = CreateFlatLayer(layer.Name, layer.Color, totalDistance, currentBase, layer.Thickness);
            
            // Apply syncline fold
            ApplyFold(formation, totalDistance / 2f, -500f, totalDistance * 0.8f);

            section.Formations.Add(formation);
            currentBase -= layer.Thickness;
        }

        return section;
    }

    private static CrossSection CreateFoldedSequence(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 150f);
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 1f);
        section.Formations.Add(basement);

        var layers = GetStandardStratigraphicColumn().Take(5).ToArray();
        float currentBase = -100f;
        foreach (var layer in layers)
        {
            var formation = CreateFlatLayer(layer.Name, layer.Color, totalDistance, currentBase, layer.Thickness);
            
            // Apply multiple folds
            ApplyFold(formation, totalDistance * 0.25f, 300f, totalDistance * 0.4f); // Anticline
            ApplyFold(formation, totalDistance * 0.75f, -300f, totalDistance * 0.4f); // Syncline

            section.Formations.Add(formation);
            currentBase -= layer.Thickness;
        }
        return section;
    }

    private static CrossSection CreateUnconformitySequence(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 50f);
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 0f);
        section.Formations.Add(basement);

        // Create lower, tilted sequence
        var lowerLayers = GetStandardStratigraphicColumn().Skip(5).Take(3).ToArray();
        float currentBase = -800f;
        float dip = 15f; // 15 degrees dip
        foreach (var layer in lowerLayers)
        {
            var formation = CreateDippingLayer(layer.Name, layer.Color, totalDistance, currentBase, layer.Thickness, dip);
            section.Formations.Add(formation);
            currentBase -= layer.Thickness;
        }
        
        // Define unconformity surface (erosional surface)
        float unconformityElevation = -500f;

        // Truncate lower layers at the unconformity
        foreach(var f in section.Formations.Where(f => f.Name != "Crystalline Basement"))
        {
            for(int i=0; i<f.TopBoundary.Count; i++)
            {
                if(f.TopBoundary[i].Y > unconformityElevation)
                    f.TopBoundary[i] = new Vector2(f.TopBoundary[i].X, unconformityElevation);
            }
        }

        // Create upper, flat-lying sequence
        var upperLayers = GetStandardStratigraphicColumn().Take(2).ToArray();
        currentBase = unconformityElevation;
        foreach(var layer in upperLayers)
        {
            var formation = CreateFlatLayer(layer.Name, layer.Color, totalDistance, currentBase, layer.Thickness);
            section.Formations.Add(formation);
            currentBase -= layer.Thickness;
        }

        return section;
    }

    private static CrossSection CreateBasinFilling(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        var basement = new ProjectedFormation
        {
            Name = "Crystalline Basement",
            Color = new Vector4(0.5f, 0.3f, 0.4f, 0.95f),
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        // Create bowl-shaped basement
        int numPoints = 50;
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            float centerDist = Math.Abs(x - totalDistance / 2f) / (totalDistance / 2f);
            float basementTop = -1000f - 1500f * (1 - centerDist * centerDist);
            basement.TopBoundary.Add(new Vector2(x, basementTop));
            basement.BottomBoundary.Add(new Vector2(x, baseElevation));
        }
        section.Formations.Add(basement);

        // Fill with onlapping layers
        var layers = GetStandardStratigraphicColumn().Take(4).ToArray();
        for(int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
        {
            var layer = layers[layerIdx];
            var formation = new ProjectedFormation { Name = layer.Name, Color = layer.Color };
            var onlapFactor = 1.0f - (layerIdx * 0.2f);
            
            for(int i=0; i <= numPoints; i++)
            {
                var x = i / (float)numPoints * totalDistance;
                var prevLayerTop = GetElevationAtX(section.Formations.Last().TopBoundary, x);
                var top = prevLayerTop + layer.Thickness * onlapFactor;
                var bottom = prevLayerTop;

                // Ensure layer pinches out at basin edge
                var basinEdgeX = (1 - onlapFactor) * totalDistance / 2f;
                if(x < basinEdgeX || x > totalDistance - basinEdgeX)
                {
                    top = bottom;
                }

                formation.TopBoundary.Add(new Vector2(x, top));
                formation.BottomBoundary.Add(new Vector2(x, bottom));
            }
            section.Formations.Add(formation);
        }

        return section;
    }

    private static CrossSection CreateChannelFill(float totalDistance, float baseElevation)
    {
        var section = CreateSimpleLayers(totalDistance, baseElevation);

        // Carve a channel into the top layers
        var channelDepth = 200f;
        var channelWidth = totalDistance * 0.2f;
        var channelCenter = totalDistance * 0.6f;
        var channelBase = GetTopographyElevationAt(section.Profile, channelCenter) - channelDepth;
        
        var channelProfile = new List<Vector2>();
        for(int i=0; i<= 50; i++)
        {
            var x = (channelCenter - channelWidth/2f) + i/50f * channelWidth;
            var relX = (x-channelCenter)/(channelWidth/2f);
            var y = channelBase + channelDepth * (relX * relX);
            channelProfile.Add(new Vector2(x, y));
        }
        
        // Truncate existing layers
        foreach(var f in section.Formations.Where(f => f.Name != "Crystalline Basement"))
        {
            for(int i=0; i < f.TopBoundary.Count; i++)
            {
                var p = f.TopBoundary[i];
                var channelElev = GetElevationAtX(channelProfile, p.X, float.MaxValue);
                if(p.Y > channelElev)
                {
                    f.TopBoundary[i] = new Vector2(p.X, channelElev);
                }
            }
        }
        
        // Add channel fill formation
        var channelFill = new ProjectedFormation
        {
            Name = "Channel Sandstone",
            Color = new Vector4(0.9f, 0.8f, 0.3f, 0.8f),
            TopBoundary = new List<Vector2>(),
            BottomBoundary = channelProfile
        };
        foreach(var p in channelProfile)
        {
            channelFill.TopBoundary.Add(new Vector2(p.X, GetTopographyElevationAt(section.Profile, p.X)));
        }
        section.Formations.Add(channelFill);

        return section;
    }
    
    // --- FIX END ---
    #endregion
    
    #region Helper Methods
    
    private static CrossSection CreateBaseCrossSection(float totalDistance, float baseElevation, float topElevation)
    {
        var profile = new GeologicalMapping.ProfileGenerator.TopographicProfile
        {
            Name = "Cross Section",
            TotalDistance = totalDistance,
            MinElevation = baseElevation,
            MaxElevation = topElevation + 500f,
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2(totalDistance, 0),
            CreatedAt = DateTime.Now,
            VerticalExaggeration = 2.0f,
            Points = new List<GeologicalMapping.ProfileGenerator.ProfilePoint>()
        };
        
        int numPoints = 100;
        for (int i = 0; i <= numPoints; i++)
        {
            float distance = i / (float)numPoints * totalDistance;
            float elevation = topElevation + 30f * MathF.Sin(distance / 2000f * MathF.PI);
            
            profile.Points.Add(new GeologicalMapping.ProfileGenerator.ProfilePoint
            {
                Position = new Vector2(distance, elevation),
                Distance = distance,
                Elevation = elevation,
                Features = new List<GeologicalMapping.GeologicalFeature>()
            });
        }
        
        return new CrossSection
        {
            Profile = profile,
            VerticalExaggeration = 2.0f,
            Formations = new List<ProjectedFormation>(),
            Faults = new List<ProjectedFault>()
        };
    }
    
    /// <summary>
    /// CRITICAL: Create crystalline basement with slight dip (Jura/Alps style)
    /// </summary>
    private static ProjectedFormation CreateCrystallineBasement(float totalDistance, float baseElevation, float dipDegrees)
    {
        var basement = new ProjectedFormation
        {
            Name = "Crystalline Basement",
            Color = new Vector4(0.5f, 0.3f, 0.4f, 0.95f), // Pinkish-gray crystalline color
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        int numPoints = 50;
        float dipRad = dipDegrees * MathF.PI / 180f;
        
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            
            // Basement top with gentle dip (2° for Jura/Alps)
            float basementTop = -1200f - (x * MathF.Tan(dipRad));
            
            // Add slight undulations to simulate real basement
            basementTop += 50f * MathF.Sin(x / 2000f * MathF.PI);
            
            float basementBottom = baseElevation;
            
            basement.TopBoundary.Add(new Vector2(x, basementTop));
            basement.BottomBoundary.Add(new Vector2(x, basementBottom));
        }
        
        return basement;
    }
    
    private static ProjectedFormation CreateFlatLayer(string name, Vector4 color, float totalDistance, float top, float thickness)
    {
        var formation = new ProjectedFormation
        {
            Name = name,
            Color = color,
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        int numPoints = 50;
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            formation.TopBoundary.Add(new Vector2(x, top));
            formation.BottomBoundary.Add(new Vector2(x, top - thickness));
        }
        
        return formation;
    }

    private static ProjectedFormation CreateDippingLayer(string name, Vector4 color, float totalDistance, float top, float thickness, float dipDegrees)
    {
        var formation = new ProjectedFormation
        {
            Name = name,
            Color = color,
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        float dipRad = dipDegrees * MathF.PI / 180f;
        int numPoints = 50;
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            float dipOffset = x * MathF.Tan(dipRad);
            formation.TopBoundary.Add(new Vector2(x, top - dipOffset));
            formation.BottomBoundary.Add(new Vector2(x, top - thickness - dipOffset));
        }
        
        return formation;
    }
    
    private static ProjectedFormation CreateErodedAnticlineLayer(string name, Vector4 color, float totalDistance,
        float baseTop, float thickness, float centerX, float foldWidth, float amplitude,
        GeologicalMapping.ProfileGenerator.TopographicProfile profile)
    {
        var formation = new ProjectedFormation
        {
            Name = name,
            Color = color,
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>(),
            FoldStyle = FoldStyle.Anticline
        };
        
        int numPoints = 100;
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            float distFromCenter = Math.Abs(x - centerX);
            
            // Smooth Gaussian-like fold shape
            float foldFactor = MathF.Exp(-(distFromCenter * distFromCenter) / (foldWidth * foldWidth * 0.25f));
            
            float foldOffset = amplitude * foldFactor;
            float topElev = baseTop + foldOffset;
            
            // Check if eroded (above topography)
            float topoElev = GetTopographyElevationAt(profile, x);
            
            // If layer would be above topography, it's eroded - bring it to topography
            if (topElev > topoElev)
            {
                topElev = topoElev;
            }
            
            formation.TopBoundary.Add(new Vector2(x, topElev));
            formation.BottomBoundary.Add(new Vector2(x, topElev - thickness));
        }
        
        return formation;
    }
    
    private static CrossSection CreateSimpleLayers(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 100f);
        var layers = GetStandardStratigraphicColumn().Take(6).ToArray();
        
        // FIRST: Add crystalline basement with slight 2° dip (Jura-style)
        var basement = CreateCrystallineBasement(totalDistance, baseElevation, 2f);
        section.Formations.Add(basement);
        
        // Add sedimentary layers with slight regional dip
        float regionalDip = 5f;
        float currentTop = 100f; // Start at topography
        
        for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
        {
            var layer = layers[layerIdx];
            var formation = new ProjectedFormation
            {
                Name = layer.Name,
                Color = layer.Color,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };
            
            for (int i = 0; i <= 50; i++)
            {
                float x = i / 50f * totalDistance;
                float dipOffset = (x / totalDistance) * totalDistance * MathF.Tan(regionalDip * MathF.PI / 180f);
                
                float topElevation;
                if (layerIdx == 0)
                {
                    // First layer MUST follow topography exactly
                    topElevation = GetTopographyElevationAt(section.Profile, x);
                }
                else
                {
                    // Other layers follow previous layer's bottom
                    var prevFormation = section.Formations[layerIdx]; // +1 for basement
                    if (i < prevFormation.BottomBoundary.Count)
                    {
                        topElevation = prevFormation.BottomBoundary[i].Y;
                    }
                    else
                    {
                        topElevation = currentTop - dipOffset;
                    }
                }
                
                formation.TopBoundary.Add(new Vector2(x, topElevation));
                formation.BottomBoundary.Add(new Vector2(x, topElevation - layer.Thickness));
            }
            
            currentTop -= layer.Thickness;
            section.Formations.Add(formation);
        }
        
        return section;
    }
    
    private static void ApplyFold(ProjectedFormation formation, float centerX, float amplitude, float foldWidth)
    {
        for (int i = 0; i < formation.TopBoundary.Count; i++)
        {
            var point = formation.TopBoundary[i];
            float distFromCenter = Math.Abs(point.X - centerX);
            float foldFactor = MathF.Exp(-(distFromCenter * distFromCenter) / (foldWidth * foldWidth * 0.25f));
            float foldOffset = amplitude * foldFactor;
            formation.TopBoundary[i] = new Vector2(point.X, point.Y + foldOffset);
        }
        
        for (int i = 0; i < formation.BottomBoundary.Count; i++)
        {
            var point = formation.BottomBoundary[i];
            float distFromCenter = Math.Abs(point.X - centerX);
            float foldFactor = MathF.Exp(-(distFromCenter * distFromCenter) / (foldWidth * foldWidth * 0.25f));
            float foldOffset = amplitude * foldFactor;
            formation.BottomBoundary[i] = new Vector2(point.X, point.Y + foldOffset);
        }
    }

    private static float GetElevationAtX(List<Vector2> points, float x, float defaultValue = 0)
    {
        if (points == null || points.Count == 0) return defaultValue;
        for (int i = 0; i < points.Count - 1; i++)
        {
            if (x >= points[i].X && x <= points[i + 1].X)
            {
                float t = (x - points[i].X) / (points[i + 1].X - points[i].X);
                return points[i].Y + t * (points[i + 1].Y - points[i].Y);
            }
        }
        return defaultValue;
    }
    
    #endregion
    
    public static string GetPresetName(PresetScenario scenario) => scenario switch
    {
        PresetScenario.SimpleLayers => "Simple Dipping Layers",
        PresetScenario.ErodedAnticline => "Eroded Anticline with Valley Fill",
        PresetScenario.ErodedSyncline => "Eroded Syncline",
        PresetScenario.JuraMountains => "Jura Mountains (Multiple Folds + Thrusts)",
        PresetScenario.FaultedLayers => "Faulted Layers (Graben)",
        PresetScenario.ThrustFault => "Thrust Fault System",
        PresetScenario.FoldedSequence => "Folded Sequence",
        PresetScenario.UnconformitySequence => "Angular Unconformity",
        PresetScenario.BasinFilling => "Sedimentary Basin",
        PresetScenario.ChannelFill => "Incised Valley Channel",
        _ => "Unknown"
    };
    
    public static string GetPresetDescription(PresetScenario scenario) => scenario switch
    {
        PresetScenario.SimpleLayers => "Gently dipping sedimentary sequence on crystalline basement (2° dip)",
        PresetScenario.ErodedAnticline => "Anticline eroded with valley fill, layers continue through erosion",
        PresetScenario.ErodedSyncline => "Syncline with realistic erosion patterns",
        PresetScenario.JuraMountains => "Realistic Jura-style: multiple gentle folds with thrust faults on crystalline basement",
        PresetScenario.FaultedLayers => "Graben structure showing proper layer continuity",
        PresetScenario.ThrustFault => "Complete thrust system with floor, imbricate and main thrusts",
        PresetScenario.FoldedSequence => "Multiple folds with proper layer continuity",
        PresetScenario.UnconformitySequence => "Angular unconformity with proper contact relationships",
        PresetScenario.BasinFilling => "Progressive basin filling with syn-sedimentary faulting",
        PresetScenario.ChannelFill => "Incised valley with channel deposits",
        _ => "Unknown"
    };
}