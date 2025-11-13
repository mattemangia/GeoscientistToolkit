// GeoscientistToolkit/Business/GIS/GeologicalLayerPresets.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Util;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
/// Provides complete geological cross-section presets with multiple layers
/// CRITICAL: Formations MUST NEVER overlap and MUST NEVER go above topography
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
        Logger.Log($"Creating geological preset: {scenario}");
        
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
        
        Logger.Log($"Preset created with {section.Formations.Count} formations and {section.Faults.Count} faults");
        
        return section;
    }
    
    private static void ValidateAndFixGeology(CrossSection section)
    {
        if (section.Profile == null || section.Formations.Count == 0)
            return;
        
        Logger.Log("Validating and fixing geology...");
        
        // Step 1: Clip all formations to topography (CRITICAL - formations must NEVER go above topography)
        ClipAllFormationsToTopography(section);
        
        // Step 2: Sort formations by depth (top to bottom)
        var sortedFormations = section.Formations
            .Where(f => f.Name != "Crystalline Basement")
            .OrderByDescending(f => f.TopBoundary.Count > 0 ? f.TopBoundary.Average(p => p.Y) : float.MinValue)
            .ToList();
        
        // Step 3: Ensure no overlaps by making each formation's bottom match the next formation's top
        for (int i = 0; i < sortedFormations.Count - 1; i++)
        {
            var upperFormation = sortedFormations[i];
            var lowerFormation = sortedFormations[i + 1];
            
            // Make upper formation's bottom exactly match lower formation's top
            AlignFormationBoundaries(upperFormation, lowerFormation);
        }
        
        // Step 4: Ensure minimum thickness for all formations
        foreach (var formation in section.Formations)
        {
            EnsureMinimumThickness(formation, 10f);
        }
        
        // Step 5: Final validation
        var overlaps = GeologicalConstraints.FindAllOverlaps(section.Formations, tolerance: 0.1f);
        if (overlaps.Count > 0)
        {
            Logger.LogWarning($"Found {overlaps.Count} overlaps after fixing - resolving...");
            GeologicalConstraints.ResolveAllOverlaps(section.Formations);
        }
        
        Logger.Log("Geology validation complete");
    }
    
    /// <summary>
    /// CRITICAL: Clip all formations to topography - formations MUST NEVER go above the topographic line
    /// </summary>
    private static void ClipAllFormationsToTopography(CrossSection section)
    {
        foreach (var formation in section.Formations)
        {
            if (formation.Name == "Crystalline Basement")
                continue; // Don't clip basement
                
            // Clip top boundary to topography
            for (int i = 0; i < formation.TopBoundary.Count; i++)
            {
                var point = formation.TopBoundary[i];
                float topoElevation = GetTopographyElevationAt(section.Profile, point.X);
                
                // If point is above topography, bring it DOWN to topography exactly
                if (point.Y > topoElevation)
                {
                    formation.TopBoundary[i] = new Vector2(point.X, topoElevation);
                    Logger.Log($"Clipped {formation.Name} top boundary at X={point.X:F0} from {point.Y:F1} to {topoElevation:F1}");
                }
            }
            
            // Ensure bottom boundary is below top boundary
            for (int i = 0; i < formation.BottomBoundary.Count && i < formation.TopBoundary.Count; i++)
            {
                if (formation.BottomBoundary[i].Y > formation.TopBoundary[i].Y)
                {
                    formation.BottomBoundary[i] = new Vector2(
                        formation.BottomBoundary[i].X,
                        formation.TopBoundary[i].Y - 10f // Minimum 10m thickness
                    );
                }
            }
        }
    }
    
    /// <summary>
    /// Align boundaries between two formations to prevent gaps and overlaps
    /// </summary>
    private static void AlignFormationBoundaries(ProjectedFormation upper, ProjectedFormation lower)
    {
        int minCount = Math.Min(upper.BottomBoundary.Count, lower.TopBoundary.Count);
        
        for (int i = 0; i < minCount; i++)
        {
            // Make upper formation's bottom exactly match lower formation's top
            float lowerTopY = lower.TopBoundary[i].Y;
            upper.BottomBoundary[i] = new Vector2(upper.BottomBoundary[i].X, lowerTopY);
            
            // Ensure upper formation's top is above its bottom
            if (upper.TopBoundary[i].Y < lowerTopY)
            {
                upper.TopBoundary[i] = new Vector2(upper.TopBoundary[i].X, lowerTopY + 10f);
            }
        }
    }
    
    /// <summary>
    /// Ensure formation has minimum thickness at all points
    /// </summary>
    private static void EnsureMinimumThickness(ProjectedFormation formation, float minThickness)
    {
        for (int i = 0; i < Math.Min(formation.TopBoundary.Count, formation.BottomBoundary.Count); i++)
        {
            float currentThickness = formation.TopBoundary[i].Y - formation.BottomBoundary[i].Y;
            if (currentThickness < minThickness)
            {
                // Adjust bottom to maintain minimum thickness
                formation.BottomBoundary[i] = new Vector2(
                    formation.BottomBoundary[i].X,
                    formation.TopBoundary[i].Y - minThickness
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
    
    #region Layer Definitions
    
    private static LayerDefinition[] GetStandardStratigraphicColumn()
    {
        return new[]
        {
            new LayerDefinition { Name = "Quaternary Alluvium", Color = new Vector4(0.9f, 0.85f, 0.6f, 0.8f), Thickness = 50f },
            new LayerDefinition { Name = "Tertiary Molasse", Color = new Vector4(0.85f, 0.75f, 0.5f, 0.8f), Thickness = 150f },
            new LayerDefinition { Name = "Upper Cretaceous Limestone", Color = new Vector4(0.7f, 0.85f, 0.7f, 0.8f), Thickness = 250f },
            new LayerDefinition { Name = "Lower Cretaceous Marl", Color = new Vector4(0.6f, 0.7f, 0.6f, 0.8f), Thickness = 200f },
            new LayerDefinition { Name = "Malm Limestone", Color = new Vector4(0.75f, 0.8f, 0.85f, 0.8f), Thickness = 300f },
            new LayerDefinition { Name = "Dogger Oolite", Color = new Vector4(0.85f, 0.85f, 0.75f, 0.8f), Thickness = 250f },
            new LayerDefinition { Name = "Lias Marl", Color = new Vector4(0.65f, 0.75f, 0.65f, 0.8f), Thickness = 200f },
            new LayerDefinition { Name = "Keuper Evaporites", Color = new Vector4(0.9f, 0.7f, 0.5f, 0.8f), Thickness = 150f },
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

    private static CrossSection CreateSimpleLayers(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        var layers = GetStandardStratigraphicColumn().Take(5).ToArray();
        
        // Add crystalline basement at the bottom
        var basement = CreateCrystallineBasement(totalDistance, baseElevation);
        section.Formations.Add(basement);
        
        // CRITICAL FIX: Build layers from TOPOGRAPHY down, not from baseElevation up!
        // Get average topography elevation to start from
        float avgTopoElevation = section.Profile.Points.Average(p => p.Elevation);
        
        // Build layers from top (just below topography) going DOWN
        float currentTop = avgTopoElevation - 10f; // Start 10m below topography
        
        for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
        {
            var layer = layers[layerIdx];
            var formation = CreateHorizontalLayer(
                layer.Name,
                layer.Color,
                totalDistance,
                currentTop,
                layer.Thickness,
                0f // No dip for simple layers
            );
            
            section.Formations.Add(formation);
            currentTop -= layer.Thickness; // Move down for next layer
        }
        
        return section;
    }

    private static CrossSection CreateErodedAnticline(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create valley topography
        ApplyTopographyPreset(section.Profile, TopographyPresets.PresetType.Valley, 0.5f);
        
        var layers = GetStandardStratigraphicColumn().Take(5).ToArray();
        
        // Add basement
        section.Formations.Add(CreateCrystallineBasement(totalDistance, baseElevation));
        
        // CRITICAL FIX: Start from average topography elevation
        float avgTopoElevation = section.Profile.Points.Average(p => p.Elevation);
        
        // Create folded layers from topography down
        float centerX = totalDistance * 0.5f;
        float foldAmplitude = 400f;
        float foldWidth = totalDistance * 0.3f;
        
        float currentTop = avgTopoElevation - 10f; // Start just below topography
        
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            var formation = CreateAnticlineLayer(
                layer.Name,
                layer.Color,
                totalDistance,
                currentTop,
                layer.Thickness,
                centerX,
                foldWidth,
                foldAmplitude * (1.0f - i * 0.15f)
            );
            
            section.Formations.Add(formation);
            currentTop -= layer.Thickness;
        }
        
        return section;
    }

    private static CrossSection CreateErodedSyncline(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create valley topography matching syncline
        ApplyTopographyPreset(section.Profile, TopographyPresets.PresetType.SynclineValley, 0.7f);
        
        var layers = GetStandardStratigraphicColumn().Take(5).ToArray();
        
        // Add basement
        section.Formations.Add(CreateCrystallineBasement(totalDistance, baseElevation));
        
        // CRITICAL FIX: Start from average topography elevation
        float avgTopoElevation = section.Profile.Points.Average(p => p.Elevation);
        
        // Create synclinal fold from topography down
        float centerX = totalDistance * 0.5f;
        float foldAmplitude = -300f; // Negative for syncline
        float foldWidth = totalDistance * 0.35f;
        
        float currentTop = avgTopoElevation - 10f; // Start just below topography
        
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            var formation = CreateAnticlineLayer(
                layer.Name,
                layer.Color,
                totalDistance,
                currentTop,
                layer.Thickness,
                centerX,
                foldWidth,
                foldAmplitude * (1.0f - i * 0.15f)
            );
            formation.FoldStyle = FoldStyle.Syncline;
            
            section.Formations.Add(formation);
            currentTop -= layer.Thickness;
        }
        
        return section;
    }

    private static CrossSection CreateJuraMountains(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create multiple ridge topography
        ApplyTopographyPreset(section.Profile, TopographyPresets.PresetType.MultipleRidges, 1.0f);
        
        var layers = GetStandardStratigraphicColumn().Take(6).ToArray();
        
        // Add basement
        section.Formations.Add(CreateCrystallineBasement(totalDistance, baseElevation));
        
        // CRITICAL FIX: Start from average topography elevation
        float avgTopoElevation = section.Profile.Points.Average(p => p.Elevation);
        
        // Create multiple folds
        float[] foldCenters = { totalDistance * 0.25f, totalDistance * 0.5f, totalDistance * 0.75f };
        float foldWidth = totalDistance * 0.2f;
        
        float currentTop = avgTopoElevation - 10f; // Start just below topography
        
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            var formation = CreateHorizontalLayer(
                layer.Name,
                layer.Color,
                totalDistance,
                currentTop,
                layer.Thickness,
                0f
            );
            
            // Apply multiple folds
            foreach (var centerX in foldCenters)
            {
                float amplitude = 200f * (1.0f - i * 0.1f);
                ApplyFold(formation, centerX, amplitude, foldWidth);
            }
            
            section.Formations.Add(formation);
            currentTop -= layer.Thickness; // Move down
        }
        
        // Add thrust faults
        section.Faults.Add(CreateFault(
            GeologicalFeatureType.Fault_Thrust,
            totalDistance * 0.35f,
            avgTopoElevation - 1000f,
            totalDistance * 0.4f,
            avgTopoElevation + 200f,
            30f,
            "E"
        ));
        
        section.Faults.Add(CreateFault(
            GeologicalFeatureType.Fault_Thrust,
            totalDistance * 0.6f,
            avgTopoElevation - 1000f,
            totalDistance * 0.65f,
            avgTopoElevation + 200f,
            30f,
            "E"
        ));
        
        return section;
    }

    private static CrossSection CreateFaultedLayers(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create escarpment topography
        ApplyTopographyPreset(section.Profile, TopographyPresets.PresetType.Escarpment, 0.6f);
        
        var layers = GetStandardStratigraphicColumn().Take(5).ToArray();
        
        // Add basement
        section.Formations.Add(CreateCrystallineBasement(totalDistance, baseElevation));
        
        // Create horizontal layers
        // CRITICAL FIX: Start from average topography elevation
        float avgTopoElevation = section.Profile.Points.Average(p => p.Elevation);
        float currentTop = avgTopoElevation - 10f; // Start just below topography
        
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            var formation = CreateHorizontalLayer(
                layer.Name,
                layer.Color,
                totalDistance,
                currentTop,
                layer.Thickness,
                0f
            );
            
            section.Formations.Add(formation);
            currentTop -= layer.Thickness;
        }
        
        // Add normal faults creating graben
        section.Faults.Add(CreateFault(
            GeologicalFeatureType.Fault_Normal,
            totalDistance * 0.3f,
            -2000f,
            totalDistance * 0.3f,
            500f,
            60f,
            "E"
        ));
        
        section.Faults.Add(CreateFault(
            GeologicalFeatureType.Fault_Normal,
            totalDistance * 0.7f,
            -2000f,
            totalDistance * 0.7f,
            500f,
            60f,
            "W"
        ));
        
        return section;
    }

    private static CrossSection CreateThrustFault(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create asymmetric valley
        ApplyTopographyPreset(section.Profile, TopographyPresets.PresetType.AsymmetricValley, 0.8f);
        
        var layers = GetStandardStratigraphicColumn().Take(5).ToArray();
        
        // Add basement
        section.Formations.Add(CreateCrystallineBasement(totalDistance, baseElevation));
        
        // Create layers with dip
        // CRITICAL FIX: Start from average topography elevation
        float avgTopoElevation = section.Profile.Points.Average(p => p.Elevation);
        float currentTop = avgTopoElevation - 10f; // Start just below topography
        
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            var formation = CreateHorizontalLayer(
                layer.Name,
                layer.Color,
                totalDistance,
                currentTop,
                layer.Thickness,
                5f // 5 degree dip
            );
            
            section.Formations.Add(formation);
            currentTop -= layer.Thickness;
        }
        
        // Add main thrust
        section.Faults.Add(CreateFault(
            GeologicalFeatureType.Fault_Thrust,
            totalDistance * 0.4f,
            -2000f,
            totalDistance * 0.55f,
            100f,
            25f,
            "E"
        ));
        
        return section;
    }

    private static CrossSection CreateFoldedSequence(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create hills topography
        ApplyTopographyPreset(section.Profile, TopographyPresets.PresetType.Hills, 0.7f);
        
        var layers = GetStandardStratigraphicColumn().Take(5).ToArray();
        
        // Add basement
        section.Formations.Add(CreateCrystallineBasement(totalDistance, baseElevation));
        
        // Create anticline-syncline pair
        float anticlineCenter = totalDistance * 0.35f;
        float synclineCenter = totalDistance * 0.65f;
        float foldWidth = totalDistance * 0.15f;
        
        // CRITICAL FIX: Start from average topography elevation
        float avgTopoElevation = section.Profile.Points.Average(p => p.Elevation);
        float currentTop = avgTopoElevation - 10f; // Start just below topography
        
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            var formation = CreateHorizontalLayer(
                layer.Name,
                layer.Color,
                totalDistance,
                currentTop,
                layer.Thickness,
                0f
            );
            
            // Apply anticline
            ApplyFold(formation, anticlineCenter, 250f * (1.0f - i * 0.1f), foldWidth);
            // Apply syncline
            ApplyFold(formation, synclineCenter, -200f * (1.0f - i * 0.1f), foldWidth);
            
            section.Formations.Add(formation);
            currentTop -= layer.Thickness;
        }
        
        return section;
    }

    private static CrossSection CreateUnconformitySequence(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create gentle slope
        ApplyTopographyPreset(section.Profile, TopographyPresets.PresetType.GentleSlope, 0.5f);
        
        var layers = GetStandardStratigraphicColumn();
        
        // Add basement
        section.Formations.Add(CreateCrystallineBasement(totalDistance, baseElevation));
        
        // CRITICAL FIX: Start from average topography elevation
        float avgTopoElevation = section.Profile.Points.Average(p => p.Elevation);
        
        // Lower sequence (tilted) - build from middle depth going down
        float lowerTop = avgTopoElevation - 400f;
        for (int i = 5; i <= 7; i++)
        {
            var layer = layers[i];
            var formation = CreateHorizontalLayer(
                layer.Name + " (Lower)",
                layer.Color,
                totalDistance,
                lowerTop,
                layer.Thickness,
                15f // Tilted
            );
            section.Formations.Add(formation);
            lowerTop -= layer.Thickness;
        }
        
        // Upper sequence (horizontal, unconformably overlying) - start from topography
        float upperTop = avgTopoElevation - 10f;
        for (int i = 0; i <= 4; i++)
        {
            var layer = layers[i];
            var formation = CreateHorizontalLayer(
                layer.Name + " (Upper)",
                layer.Color,
                totalDistance,
                upperTop,
                layer.Thickness,
                0f // Horizontal
            );
            section.Formations.Add(formation);
            upperTop -= layer.Thickness;
        }
        
        return section;
    }

    private static CrossSection CreateBasinFilling(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create flat topography
        ApplyTopographyPreset(section.Profile, TopographyPresets.PresetType.Flat, 1.0f);
        
        var layers = GetStandardStratigraphicColumn().Take(6).ToArray();
        
        // Add basement
        section.Formations.Add(CreateCrystallineBasement(totalDistance, baseElevation));
        
        // Create basin shape - layers thicken towards center
        float centerX = totalDistance * 0.5f;
        float basinWidth = totalDistance * 0.6f;
        
        // CRITICAL FIX: Start from average topography elevation
        float avgTopoElevation = section.Profile.Points.Average(p => p.Elevation);
        float currentTop = avgTopoElevation - 10f; // Start just below topography
        
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            var formation = CreateBasinLayer(
                layer.Name,
                layer.Color,
                totalDistance,
                currentTop - layer.Thickness, // Bottom of this layer
                layer.Thickness,
                centerX,
                basinWidth
            );
            
            section.Formations.Add(formation);
            currentTop -= layer.Thickness;
        }
        
        return section;
    }

    private static CrossSection CreateChannelFill(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);
        
        // Create flat topography
        ApplyTopographyPreset(section.Profile, TopographyPresets.PresetType.Flat, 1.0f);
        
        var layers = GetStandardStratigraphicColumn();
        
        // Add basement
        section.Formations.Add(CreateCrystallineBasement(totalDistance, baseElevation));
        
        // CRITICAL FIX: Start from average topography elevation
        float avgTopoElevation = section.Profile.Points.Average(p => p.Elevation);
        
        // Add base layers
        float currentTop = avgTopoElevation - 10f;
        for (int i = 5; i <= 7; i++)
        {
            var layer = layers[i];
            var formation = CreateHorizontalLayer(
                layer.Name,
                layer.Color,
                totalDistance,
                currentTop,
                layer.Thickness,
                0f
            );
            section.Formations.Add(formation);
            currentTop -= layer.Thickness;
        }
        
        // Add channel fill (lens-shaped)
        float channelCenter = totalDistance * 0.5f;
        float channelWidth = totalDistance * 0.3f;
        
        var channelFill = CreateChannelFillLayer(
            "Channel Sand",
            new Vector4(0.95f, 0.85f, 0.5f, 0.8f),
            totalDistance,
            currentTop, // Base of channel
            150f,
            channelCenter,
            channelWidth
        );
        section.Formations.Add(channelFill);
        
        return section;
    }

    #endregion
    
    #region Helper Functions
    
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
        
        // Generate flat topography initially (will be modified by presets)
        var numPoints = 50;
        for (var i = 0; i <= numPoints; i++)
        {
            var distance = i / (float)numPoints * totalDistance;
            profile.Points.Add(new GeologicalMapping.ProfileGenerator.ProfilePoint
            {
                Position = new Vector2(distance, topElevation),
                Distance = distance,
                Elevation = topElevation,
                Features = new List<GeologicalFeature>()
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
    
    private static void ApplyTopographyPreset(GeologicalMapping.ProfileGenerator.TopographicProfile profile, TopographyPresets.PresetType type, float amplitude)
    {
        TopographyPresets.ApplyPreset(profile, type, amplitude);
    }
    
    private static ProjectedFormation CreateCrystallineBasement(float totalDistance, float baseElevation)
    {
        var basement = new ProjectedFormation
        {
            Name = "Crystalline Basement",
            Color = new Vector4(0.4f, 0.3f, 0.3f, 0.8f),
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        int numPoints = 50;
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            basement.TopBoundary.Add(new Vector2(x, baseElevation));
            basement.BottomBoundary.Add(new Vector2(x, baseElevation - 500f));
        }
        
        return basement;
    }
    
    private static ProjectedFormation CreateHorizontalLayer(
        string name, 
        Vector4 color, 
        float totalDistance, 
        float top, 
        float thickness,
        float dipDegrees)
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
    
    private static ProjectedFormation CreateAnticlineLayer(
        string name,
        Vector4 color,
        float totalDistance,
        float baseTop,
        float thickness,
        float centerX,
        float foldWidth,
        float amplitude)
    {
        var formation = new ProjectedFormation
        {
            Name = name,
            Color = color,
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>(),
            FoldStyle = amplitude > 0 ? FoldStyle.Anticline : FoldStyle.Syncline
        };
        
        int numPoints = 100;
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            float distFromCenter = Math.Abs(x - centerX);
            
            // Smooth Gaussian-like fold shape
            float foldFactor = MathF.Exp(-(distFromCenter * distFromCenter) / (foldWidth * foldWidth * 0.25f));
            float foldOffset = amplitude * foldFactor;
            
            formation.TopBoundary.Add(new Vector2(x, baseTop + foldOffset));
            formation.BottomBoundary.Add(new Vector2(x, baseTop + foldOffset - thickness));
        }
        
        return formation;
    }
    
    private static ProjectedFormation CreateBasinLayer(
        string name,
        Vector4 color,
        float totalDistance,
        float baseBottom,
        float maxThickness,
        float centerX,
        float basinWidth)
    {
        var formation = new ProjectedFormation
        {
            Name = name,
            Color = color,
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        int numPoints = 100;
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            float distFromCenter = Math.Abs(x - centerX);
            
            // Basin shape - thicker in center
            float thicknessFactor = 1.0f;
            if (distFromCenter < basinWidth / 2f)
            {
                float normalizedDist = distFromCenter / (basinWidth / 2f);
                thicknessFactor = 1.0f - normalizedDist * normalizedDist * 0.7f;
            }
            else
            {
                thicknessFactor = 0.3f;
            }
            
            float thickness = maxThickness * thicknessFactor;
            formation.TopBoundary.Add(new Vector2(x, baseBottom + thickness));
            formation.BottomBoundary.Add(new Vector2(x, baseBottom));
        }
        
        return formation;
    }
    
    private static ProjectedFormation CreateChannelFillLayer(
        string name,
        Vector4 color,
        float totalDistance,
        float baseBottom,
        float maxThickness,
        float centerX,
        float channelWidth)
    {
        var formation = new ProjectedFormation
        {
            Name = name,
            Color = color,
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };
        
        int numPoints = 100;
        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            float distFromCenter = Math.Abs(x - centerX);
            
            // Channel shape - lens geometry
            float thickness = 0f;
            if (distFromCenter < channelWidth / 2f)
            {
                float normalizedDist = distFromCenter / (channelWidth / 2f);
                thickness = maxThickness * (1.0f - normalizedDist * normalizedDist);
            }
            
            formation.TopBoundary.Add(new Vector2(x, baseBottom + thickness));
            formation.BottomBoundary.Add(new Vector2(x, baseBottom));
        }
        
        return formation;
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
    
    private static ProjectedFault CreateFault(
        GeologicalFeatureType type,
        float startX,
        float startY,
        float endX,
        float endY,
        float dip,
        string dipDirection)
    {
        return new ProjectedFault
        {
            Type = type,
            FaultTrace = new List<Vector2>
            {
                new Vector2(startX, startY),
                new Vector2(endX, endY)
            },
            Dip = dip,
            DipDirection = dipDirection,
            Displacement = null
        };
    }
    
    #endregion
    
    public static string GetPresetName(PresetScenario scenario) => scenario switch
    {
        PresetScenario.SimpleLayers => "Simple Horizontal Layers",
        PresetScenario.ErodedAnticline => "Eroded Anticline with Valley",
        PresetScenario.ErodedSyncline => "Eroded Syncline",
        PresetScenario.JuraMountains => "Jura Mountains (Multiple Folds)",
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
        PresetScenario.SimpleLayers => "Simple horizontal sedimentary sequence on crystalline basement",
        PresetScenario.ErodedAnticline => "Anticline structure eroded at the crest with valley topography",
        PresetScenario.ErodedSyncline => "Syncline with realistic erosion patterns and valley fill",
        PresetScenario.JuraMountains => "Jura-style: multiple gentle folds with thrust faults",
        PresetScenario.FaultedLayers => "Graben structure with normal faults",
        PresetScenario.ThrustFault => "Complete thrust fault system with hanging wall and footwall",
        PresetScenario.FoldedSequence => "Anticline and syncline pair showing fold geometry",
        PresetScenario.UnconformitySequence => "Angular unconformity between tilted lower and horizontal upper sequences",
        PresetScenario.BasinFilling => "Progressive basin filling with thickening towards center",
        PresetScenario.ChannelFill => "Incised valley channel with lens-shaped sand body",
        _ => "Unknown"
    };
}